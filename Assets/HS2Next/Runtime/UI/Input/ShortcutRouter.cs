// The shortcut runtime: one KeyDownEvent handler on the panel root (bubble
// phase) feeds P's ShortcutResolver with the facts of the event — the stored key name (UI Toolkit's keyCode mapped to
// legacy-Input semantics through KeyNames.FromUiToolkit for the JIS OEM keys, spike S8), exact modifiers, repeat, the
// focused element kind (text field / other control / none) and its panel's scope, the pointer over the viewport, the
// viewport's focus, IME composition (Input.compositionString) and an open modal dialog — and runs the action from the
// UI registry. Before the resolver: Esc closes an open popup menu, then cancels an open drag edit. Held actions (camera
// keys) are polled every frame through IInputSource while their scope is active (ResolveHeld: Shift ×5, Ctrl ×0.2).
// The JIS ¥ / ろ keys (no legacy KeyCode) are tracked from KeyDown / KeyUp. Recent resolutions are kept for ui.state.
using System;
using System.Collections.Generic;
using System.Reflection;
using HS2Next.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Input
{
    public sealed class ShortcutRouter
    {
        readonly UiContext ctx;
        public IInputSource Source { get; set; } = new LegacyInputSource();
        KeyStroke pending;
        readonly HashSet<string> downByEvent = new HashSet<string>(StringComparer.Ordinal);
        readonly List<string> keysDown = new List<string>();
        readonly Dictionary<string, KeyCode> pollable = new Dictionary<string, KeyCode>(StringComparer.Ordinal);
        /// <summary>The last resolutions ("Trigger edit.undo", "None (text)", …), newest last, at most 32.</summary>
        public readonly List<string> Log = new List<string>();
        /// <summary>Actions run by keys so far (tests).</summary>
        public int Ran { get; private set; }
        /// <summary>Held actions run in the last poll.</summary>
        public readonly List<HeldAction> LastHeld = new List<HeldAction>();

        public ShortcutRouter(UiContext ctx)
        {
            this.ctx = ctx;
            ctx.Keys.Changed += RebuildPollable;
        }

        VisualElement attached;

        /// <summary>
        /// Listens for keys on the panel's visual tree (else <paramref name="root"/>): UI Toolkit sends a key event to the focused
        /// element, or to the panel's visual tree when nothing has focus (after a dialog closed, for example), which a handler on
        /// the UI root never sees (Phase 3.6, found by UI2: F1 / F11 did nothing until a click focused something).
        /// </summary>
        public void Attach(VisualElement root)
        {
            if (attached != null)
            {
                attached.UnregisterCallback<KeyDownEvent>(OnKeyDown);
                attached.UnregisterCallback<KeyUpEvent>(OnKeyUp, TrickleDown.TrickleDown);
                attached.UnregisterCallback<KeyDownEvent>(OnKeyDownTrickle, TrickleDown.TrickleDown);
            }
            var at = root.panel?.visualTree ?? root;
            attached = at;
            at.RegisterCallback<KeyDownEvent>(OnKeyDown);
            at.RegisterCallback<KeyUpEvent>(OnKeyUp, TrickleDown.TrickleDown);
            at.RegisterCallback<KeyDownEvent>(OnKeyDownTrickle, TrickleDown.TrickleDown);
            RebuildPollable();
        }

        void RebuildPollable()
        {
            pollable.Clear();
            var map = ctx.Keys.Map;
            if (map == null) return;
            foreach (var id in map.Entries)
                foreach (var c in map.ChordsOf(id))
                {
                    string k = c.First.Key;
                    if (k == null || !KeyNames.IsPollable(k) || pollable.ContainsKey(k)) continue;
                    if (Enum.TryParse(k, out KeyCode kc)) pollable[k] = kc;
                }
        }

        public static KeyMods Mods(EventModifiers m)
        {
            var r = KeyMods.None;
            if ((m & (EventModifiers.Control | EventModifiers.Command)) != 0) r |= KeyMods.Ctrl;
            if ((m & EventModifiers.Shift) != 0) r |= KeyMods.Shift;
            if ((m & EventModifiers.Alt) != 0) r |= KeyMods.Alt;
            return r;
        }

        KeyMods PolledMods()
        {
            var s = Source;
            var r = KeyMods.None;
            if (s.GetKey(KeyCode.LeftControl) || s.GetKey(KeyCode.RightControl)) r |= KeyMods.Ctrl;
            if (s.GetKey(KeyCode.LeftShift) || s.GetKey(KeyCode.RightShift)) r |= KeyMods.Shift;
            if (s.GetKey(KeyCode.LeftAlt) || s.GetKey(KeyCode.RightAlt)) r |= KeyMods.Alt;
            return r;
        }

        /// <summary>Text fields with this class handle Esc themselves (the tree's in-place rename cancels on Esc; a blur commits it).</summary>
        public const string OwnEscapeClass = "hs-text--own-escape";

        /// <summary>
        /// The element with keyboard focus, or null. UI Toolkit reports the outermost composite as the focused element (the tree,
        /// while its in-place rename field has the keyboard); when that is not a text field, the text field holding the innermost
        /// focused element is returned instead, so text-field rules (no shortcuts, the IME candidate window) apply there too.
        /// </summary>
        public VisualElement Focused
        {
            get
            {
                var fc = ctx.Shell?.Root?.panel?.focusController;
                var f = fc?.focusedElement as VisualElement;
                if (f == null || IsText(f)) return f;
                var leaf = Leaf(fc);
                if (leaf == null || leaf == f) return f;
                for (var e = leaf; e != null && e != f; e = e.hierarchy.parent)
                    if (e.ClassListContains("unity-base-text-field")) return e;
                return f;
            }
        }

        static Func<FocusController, Focusable> leafOf;
        static bool leafResolved;

        /// <summary>The innermost focused element (UI Toolkit keeps it internal; resolved once by reflection), or null.</summary>
        public static VisualElement Leaf(FocusController fc)
        {
            if (fc == null) return null;
            if (!leafResolved)
            {
                leafResolved = true;
                try
                {
                    var m = typeof(FocusController).GetMethod("GetLeafFocusedElement", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                    if (m != null) leafOf = (Func<FocusController, Focusable>)Delegate.CreateDelegate(typeof(Func<FocusController, Focusable>), m);
                }
                catch (Exception) { leafOf = null; }
                if (leafOf == null)
                    Debug.LogWarning("[ui] FocusController.GetLeafFocusedElement not found: a text field inside a list or tree counts as the list (shortcuts stay active there)");
            }
            if (leafOf == null) return null;
            try { return leafOf(fc) as VisualElement; }
            catch (Exception) { return null; }
        }

        /// <summary>True when the element (or an ancestor) is a text input field.</summary>
        public static bool IsText(VisualElement v)
        {
            for (var e = v; e != null; e = e.hierarchy.parent)
                if (e.ClassListContains("unity-base-text-field") || e.ClassListContains("unity-text-field")) return true;
            return false;
        }

        public FocusKind FocusKindOf(VisualElement f) => f == null || f == ctx.Viewport?.Element ? FocusKind.None : IsText(f) ? FocusKind.TextField : FocusKind.Control;

        /// <summary>The scope of the panel holding focus (Global when none).</summary>
        public ActionScope ScopeOf(VisualElement f)
        {
            for (var e = f; e != null; e = e.hierarchy.parent)
                if (e.userData is PanelDef d) return d.Scope;
            return ActionScope.Global;
        }

        public bool ImeComposing => !string.IsNullOrEmpty(Source.CompositionString);

        /// <summary>The resolver input of a key event with the current UI facts.</summary>
        public KeyInput Facts(string key, KeyMods mods, bool repeat, VisualElement focusedOverride = null)
        {
            var f = focusedOverride ?? Focused;
            return new KeyInput(key, mods)
            {
                Repeat = repeat,
                Focus = FocusKindOf(f),
                FocusScope = ScopeOf(f),
                PointerOverViewport = ctx.Viewport != null && ctx.Viewport.PointerOver,
                ViewportFocused = ctx.Viewport != null && f == ctx.Viewport.Element,
                ImeComposing = ImeComposing,
                ModalOpen = ctx.Dialogs != null && ctx.Dialogs.IsModalOpen,
                Pending = pending,
            };
        }

        void OnKeyUp(KeyUpEvent e)
        {
            if (e.keyCode == KeyCode.None) return;
            downByEvent.Remove(KeyNames.FromUiToolkit(e.keyCode.ToString(), ctx.Keys.Layout));
        }

        /// <summary>Esc while a text field has focus (no dialog, no IME composition): blur before the field takes the key.</summary>
        void OnKeyDownTrickle(KeyDownEvent e)
        {
            if (e.keyCode != KeyCode.Escape || e.modifiers != EventModifiers.None || ImeComposing) return;
            var f = Focused;
            // a colour picker's field: the picker cancels (it takes Esc in its own trickle-down handler)
            for (var p = f; p != null; p = p.hierarchy.parent) if (p is HS2Next.UI.Widgets.ColorPicker) return;
            if (ctx.Dialogs != null && ctx.Dialogs.IsModalOpen)
            {
                // Esc in a dialog's text field (the key overlay's search) cancels the dialog: the field would take the key first
                // (Phase 3.6, found by UI2)
                if (f != null && IsText(f) && Handle("Escape", KeyMods.None, false, f))
                {
                    e.StopPropagation();
                    ctx.Shell?.Root?.panel?.focusController?.IgnoreEvent(e);
                }
                return;
            }
            if (f == null || !IsText(f)) return;
            for (var p = f; p != null; p = p.hierarchy.parent) if (p.ClassListContains(OwnEscapeClass)) return;
            Note("Esc: text field blurred");
            f.Blur();
            ctx.Viewport?.Element?.Focus();
            e.StopPropagation();
            ctx.Shell?.Root?.panel?.focusController?.IgnoreEvent(e);
        }

        void OnKeyDown(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.None) return;
            string key = KeyNames.FromUiToolkit(e.keyCode.ToString(), ctx.Keys.Layout);
            bool repeat = !downByEvent.Add(key);
            var mods = Mods(e.modifiers);
            if (Handle(key, mods, repeat, e.target as VisualElement))
            {
                e.StopPropagation();
                ctx.Shell?.Root?.panel?.focusController?.IgnoreEvent(e);
            }
        }

        /// <summary>
        /// Resolves and runs one key (the KeyDown handler; ui.input key events). Returns true when the event is consumed.
        /// </summary>
        public bool Handle(string key, KeyMods mods, bool repeat, VisualElement target = null)
        {
            if (key == "Escape" && mods == KeyMods.None && !repeat)
            {
                if (ctx.Shell?.Menus != null && ctx.Shell.Menus.IsOpen) { ctx.Shell.Menus.CloseAll(); Note("Esc: menu closed"); return true; }
                if (ctx.ActiveEdit != null && ctx.ActiveEdit.IsOpen) { ctx.ActiveEdit.Cancel(); Note("Esc: edit cancelled"); return true; }
                if (ctx.Viewport != null && ctx.Viewport.CancelDrag()) { Note("Esc: drag cancelled"); return true; }
            }
            var resolver = ctx.Keys.Resolver;
            if (resolver == null) return false;
            var f = Focused;
            var input = Facts(key, mods, repeat, f ?? target);
            var r = resolver.Resolve(input);
            Note(r.ToString() + " [" + new KeyStroke(mods, key) + "]");
            pending = r.Outcome == ShortcutOutcome.Pending ? r.Stroke : default;
            switch (r.Outcome)
            {
                case ShortcutOutcome.None: return false;
                case ShortcutOutcome.Blocked: return true;
                case ShortcutOutcome.Pending: return true;
                case ShortcutOutcome.Held: return true;
            }
            if (r.ActionId == ActionIds.DialogAccept) { ctx.Dialogs?.Accept(); return true; }
            if (r.ActionId == ActionIds.DialogCancel) { ctx.Dialogs?.Cancel(); return true; }
            if (r.ActionId == ActionIds.TextBlur) { (f ?? target)?.Blur(); ctx.Viewport?.Element?.Focus(); return true; }
            Ran++;
            var st = ctx.Registry.RunWith(r.ActionId, new ActionInvocation { Source = "key", Repeat = repeat }, out _);
            if (st == ActionRunStatus.Unavailable) Note(r.ActionId + " unavailable");
            return true;
        }

        void Note(string s)
        {
            Log.Add(s);
            if (Log.Count > 32) Log.RemoveAt(0);
        }

        /// <summary>Per frame: runs the held actions for the keys down (camera keys), unless a text field has focus.</summary>
        public void Poll(float dt)
        {
            LastHeld.Clear();
            var resolver = ctx.Keys.Resolver;
            if (resolver == null) return;
            keysDown.Clear();
            foreach (var kv in pollable) if (Source.GetKey(kv.Value)) keysDown.Add(kv.Key);
            if (downByEvent.Contains(KeyNames.JisBackslash)) keysDown.Add(KeyNames.JisBackslash);
            // keys released while the window had no focus never send KeyUp: forget event-tracked keys Input says are up
            if (!Source.AnyKey) downByEvent.Clear();
            if (keysDown.Count == 0) return;
            var f = Focused;
            bool over = ctx.Viewport != null && ctx.Viewport.PointerOver;
            bool vf = ctx.Viewport != null && f == ctx.Viewport.Element;
            var held = resolver.ResolveHeld(keysDown, PolledMods(), FocusKindOf(f), ScopeOf(f), over, vf,
                ctx.Dialogs != null && ctx.Dialogs.IsModalOpen, ImeComposing);
            foreach (var h in held)
            {
                LastHeld.Add(h);
                ctx.Registry.RunWith(h.ActionId, new ActionInvocation { Source = "held", DeltaTime = dt, Speed = h.Speed }, out _);
            }
        }
    }

    /// <summary>
    /// The IME candidate-window position fix (spike S2a, UUM-146439): in LateUpdate, while a text input has focus and the
    /// IME composes, Input.compositionCursorPos = the caret's bottom-left of the field's input TextElement × panel scale + the
    /// window frame offset (the player reads it relative to the window rect including the title bar).
    /// </summary>
    public sealed class ImeCaretFix
    {
        readonly UiContext ctx;
        public bool Enabled = true;
        public Vector2 LastPos { get; private set; }
        public ImeCaretFix(UiContext ctx) { this.ctx = ctx; }

        public void LateUpdate()
        {
            if (!Enabled) return;
            var router = ctx.Host?.Router;
            var f = router?.Focused;
            if (f == null || !ShortcutRouter.IsText(f) || string.IsNullOrEmpty(UnityEngine.Input.compositionString)) return;
            TextField field = null;
            for (var e = f; e != null; e = e.hierarchy.parent) if (e is TextField t) { field = t; break; }
            var input = field?.Q(className: TextField.inputUssClassName)?.Q<TextElement>() ?? f.Q<TextElement>();
            if (input == null) return;
            var cp = field != null ? field.textSelection.cursorPosition : Vector2.zero;
            float lh = input.resolvedStyle.fontSize * 1.25f;
            var wb = input.worldBound;
            float s = f.panel != null ? f.panel.scaledPixelsPerPoint : 1f;
            var off = NativeWindow.ClientOffset();
            // caret bottom-left: x + cursor x, bottom of the caret line (cursorPosition is the line's bottom side)
            LastPos = new Vector2((wb.x + cp.x) * s + off.x, (wb.y + cp.y) * s + off.y);
            UnityEngine.Input.compositionCursorPos = LastPos;
            _ = lh;
        }
    }
}

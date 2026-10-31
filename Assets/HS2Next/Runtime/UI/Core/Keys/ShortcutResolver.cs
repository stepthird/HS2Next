// The shortcut resolver: a pure function from the facts of one key event to
// an action id (or nothing), used by package A's ShortcutRouter (root KeyDownEvent handler, bubble phase) and tested in
// plain Mono. Rules, in the order applied:
//   0. IME composing (non-empty composition string, or the event's character belongs to a composition) → nothing: every
//      key of a composition belongs to the IME, Enter included (a modal's name prompt must not accept on the commit Enter).
//   1. a modal dialog consumes everything except its own Enter (dialog.accept) and Esc (dialog.cancel);
//   2. a text field focused → no shortcut except actions marked TextAllowed; Esc blurs the field (text.blur);
//   3. scope order: the focused panel's scope, then Viewport when the pointer is over the viewport (or it has focus), then
//      Global; the first scope with a match wins;
//   4. exact modifier match (Ctrl+Z ≠ Z) in both presets;
//   5. key repeat triggers only Repeatable / Held actions;
//   6. two-stroke chords: a first stroke returns Pending (the caller passes it back as KeyInput.Pending with the next key).
// Held actions (camera keys) are not run by the key event: ResolveHeld lists the held actions for the keys currently down
// (polled each frame), allowing Shift (×5) and Ctrl (×0.2) as speed modifiers when no exact binding of that key with
// those modifiers exists in the active scopes. Modifier actions are queried with IsModifierHeld.
using System;
using System.Collections.Generic;

namespace HS2Next.UI.Core
{
    /// <summary>What has keyboard focus.</summary>
    public enum FocusKind { None, TextField, Control }

    /// <summary>The facts of one key event.</summary>
    public struct KeyInput
    {
        /// <summary>KeyCode name (KeyNames) of the key, or a wheel pseudo key.</summary>
        public string Key;
        public KeyMods Mods;
        public bool Repeat;
        public FocusKind Focus;
        /// <summary>Scope of the panel holding focus (Global when nothing panel-specific has focus).</summary>
        public ActionScope FocusScope;
        public bool PointerOverViewport;
        /// <summary>The viewport element has keyboard focus.</summary>
        public bool ViewportFocused;
        public bool ImeComposing;
        public bool ModalOpen;
        /// <summary>The first stroke returned as Pending by the previous call (default = none).</summary>
        public KeyStroke Pending;

        public KeyInput(string key, KeyMods mods = KeyMods.None)
        {
            Key = key;
            Mods = mods;
            Repeat = false;
            Focus = FocusKind.None;
            FocusScope = ActionScope.Global;
            PointerOverViewport = false;
            ViewportFocused = false;
            ImeComposing = false;
            ModalOpen = false;
            Pending = default;
        }

        public KeyStroke Stroke => new KeyStroke(Mods, Key);
    }

    public enum ShortcutOutcome
    {
        /// <summary>Not a shortcut: let the event continue (text input, focus navigation, …).</summary>
        None,
        /// <summary>Run ActionId now (and stop the event).</summary>
        Trigger,
        /// <summary>ActionId is a Held action whose key went down: stop the event; the per-frame poll runs it.</summary>
        Held,
        /// <summary>A first stroke of a two-stroke chord: stop the event, pass Stroke back as KeyInput.Pending.</summary>
        Pending,
        /// <summary>Swallowed without action (a modal dialog is open; a pending chord did not complete).</summary>
        Blocked,
    }

    public struct ShortcutResult
    {
        public ShortcutOutcome Outcome;
        public string ActionId;
        public ActionScope Scope;
        /// <summary>The pending first stroke (Outcome Pending).</summary>
        public KeyStroke Stroke;
        /// <summary>Why nothing ran ("ime", "modal", "text", "repeat", "unbound", "no key", "pending-miss").</summary>
        public string Reason;

        public bool Consumed => Outcome != ShortcutOutcome.None;

        public override string ToString() => Outcome + (ActionId != null ? " " + ActionId : "") + (Reason != null ? " (" + Reason + ")" : "");

        internal static ShortcutResult Nothing(string reason) => new ShortcutResult { Outcome = ShortcutOutcome.None, Reason = reason };
        internal static ShortcutResult Block(string reason) => new ShortcutResult { Outcome = ShortcutOutcome.Blocked, Reason = reason };
        internal static ShortcutResult Run(string id, ActionScope scope) => new ShortcutResult { Outcome = ShortcutOutcome.Trigger, ActionId = id, Scope = scope };
    }

    /// <summary>A held action active this frame, with the speed factor of the extra modifiers.</summary>
    public struct HeldAction
    {
        public string ActionId;
        public double Speed;
        public override string ToString() => ActionId + " ×" + Speed;
    }

    /// <summary>The resolver over one keymap; action facts come from <see cref="ActionManifest"/> unless a lookup is given.</summary>
    public sealed class ShortcutResolver
    {
        public const double ShiftSpeed = 5, CtrlSpeed = 0.2;

        readonly Keymap map;
        readonly Func<string, ActionInfo> info;
        // per scope: stroke → bindings (single-stroke chords and first strokes of two-stroke chords)
        readonly Dictionary<ActionScope, Dictionary<KeyStroke, List<Entry>>> byScope = new Dictionary<ActionScope, Dictionary<KeyStroke, List<Entry>>>();

        struct Entry
        {
            public string Id;
            public ActionInfo Info;
            public Chord Chord;
        }

        public Keymap Keymap => map;

        public ShortcutResolver(Keymap keymap, Func<string, ActionInfo> actionInfo = null)
        {
            map = keymap ?? throw new ArgumentNullException(nameof(keymap));
            info = actionInfo ?? (id => ActionManifest.TryGet(id, out var i) ? i : null);
            foreach (var id in map.Entries)
            {
                var ai = info(id);
                if (ai == null) continue;
                if (!byScope.TryGetValue(ai.Scope, out var d)) byScope[ai.Scope] = d = new Dictionary<KeyStroke, List<Entry>>();
                foreach (var c in map.ChordsOf(id))
                {
                    if (!d.TryGetValue(c.First, out var l)) d[c.First] = l = new List<Entry>();
                    l.Add(new Entry { Id = id, Info = ai, Chord = c });
                }
            }
        }

        static bool IsEnter(string key) => key == "Return" || key == "KeypadEnter";

        /// <summary>The active scopes in precedence order for this event.</summary>
        public static List<ActionScope> ScopesFor(ActionScope focusScope, bool pointerOverViewport, bool viewportFocused)
        {
            var l = new List<ActionScope>(3);
            if (focusScope != ActionScope.Global) l.Add(focusScope);
            if ((pointerOverViewport || viewportFocused) && !l.Contains(ActionScope.Viewport)) l.Add(ActionScope.Viewport);
            l.Add(ActionScope.Global);
            return l;
        }

        public ShortcutResult Resolve(KeyInput e)
        {
            if (e.ImeComposing) return ShortcutResult.Nothing("ime");
            if (string.IsNullOrEmpty(e.Key)) return ShortcutResult.Nothing("no key");
            if (KeyNames.ModifierKeys.Contains(e.Key)) return ShortcutResult.Nothing("modifier key");
            if (e.ModalOpen)
            {
                if (e.Repeat) return ShortcutResult.Block("modal");
                if (e.Key == "Escape" && e.Mods == KeyMods.None) return ShortcutResult.Run(ActionIds.DialogCancel, ActionScope.Global);
                if (IsEnter(e.Key) && e.Mods == KeyMods.None) return ShortcutResult.Run(ActionIds.DialogAccept, ActionScope.Global);
                // Inside the dialog's own text field keys reach the field; everything else is swallowed.
                return e.Focus == FocusKind.TextField ? ShortcutResult.Nothing("modal text") : ShortcutResult.Block("modal");
            }
            bool text = e.Focus == FocusKind.TextField;
            if (text && e.Key == "Escape" && e.Mods == KeyMods.None) return ShortcutResult.Run(ActionIds.TextBlur, ActionScope.Global);

            var stroke = e.Stroke;
            var scopes = ScopesFor(e.FocusScope, e.PointerOverViewport, e.ViewportFocused);

            // second stroke of a pending chord
            if (!e.Pending.IsEmpty)
            {
                foreach (var s in scopes)
                {
                    if (!byScope.TryGetValue(s, out var d) || !d.TryGetValue(e.Pending, out var l)) continue;
                    foreach (var en in l)
                        if (en.Chord.HasSecond && en.Chord.Second == stroke && (!text || en.Info.TextAllowed))
                            return ShortcutResult.Run(en.Id, s);
                }
                return ShortcutResult.Block("pending-miss");
            }

            foreach (var s in scopes)
            {
                if (!byScope.TryGetValue(s, out var d) || !d.TryGetValue(stroke, out var l)) continue;
                bool anyPending = false, repeatMiss = false, textMiss = false;
                Entry? single = null;
                foreach (var en in l)
                {
                    if (en.Info.Kind == ActionKind.Modifier) continue;
                    if (text && !en.Info.TextAllowed) { textMiss = true; continue; }
                    if (en.Chord.HasSecond) { anyPending = true; continue; }
                    if (e.Repeat && !en.Info.Repeatable) { repeatMiss = true; continue; }
                    if (single == null) single = en;
                }
                if (single != null)
                {
                    var en = single.Value;
                    return en.Info.Kind == ActionKind.Held
                        ? new ShortcutResult { Outcome = ShortcutOutcome.Held, ActionId = en.Id, Scope = s }
                        : ShortcutResult.Run(en.Id, s);
                }
                if (anyPending && !e.Repeat) return new ShortcutResult { Outcome = ShortcutOutcome.Pending, Stroke = stroke, Scope = s };
                if (repeatMiss) return ShortcutResult.Block("repeat");
                if (textMiss) return ShortcutResult.Nothing("text");
            }
            return ShortcutResult.Nothing(text ? "text" : "unbound");
        }

        /// <summary>The Held actions active for the keys currently down (KeyCode names) and the modifier state; none while a
        /// text field has focus, a modal is open or the IME composes.</summary>
        public List<HeldAction> ResolveHeld(IEnumerable<string> keysDown, KeyMods mods, FocusKind focus, ActionScope focusScope,
            bool pointerOverViewport, bool viewportFocused, bool modalOpen = false, bool imeComposing = false)
        {
            var r = new List<HeldAction>();
            if (focus == FocusKind.TextField || modalOpen || imeComposing || keysDown == null) return r;
            var scopes = ScopesFor(focusScope, pointerOverViewport, viewportFocused);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in keysDown)
            {
                if (string.IsNullOrEmpty(key) || KeyNames.ModifierKeys.Contains(key)) continue;
                // exact binding of this key with these modifiers (any kind) in an active scope wins
                var exact = Find(scopes, new KeyStroke(mods, key), out var exactEntry);
                if (exact)
                {
                    if (exactEntry.Info.Kind == ActionKind.Held && seen.Add(exactEntry.Id)) r.Add(new HeldAction { ActionId = exactEntry.Id, Speed = 1 });
                    continue;
                }
                // speed modifiers: drop Shift / Ctrl that the binding does not use
                foreach (var baseMods in SpeedVariants(mods))
                {
                    if (!Find(scopes, new KeyStroke(baseMods, key), out var en) || en.Info.Kind != ActionKind.Held) continue;
                    var extra = mods & ~baseMods;
                    double speed = 1;
                    if ((extra & KeyMods.Shift) != 0) speed *= ShiftSpeed;
                    if ((extra & KeyMods.Ctrl) != 0) speed *= CtrlSpeed;
                    if (seen.Add(en.Id)) r.Add(new HeldAction { ActionId = en.Id, Speed = speed });
                    break;
                }
            }
            return r;
        }

        static IEnumerable<KeyMods> SpeedVariants(KeyMods mods)
        {
            // fewest dropped modifiers first: drop Shift, drop Ctrl, drop both (Alt never dropped)
            if ((mods & KeyMods.Shift) != 0) yield return mods & ~KeyMods.Shift;
            if ((mods & KeyMods.Ctrl) != 0) yield return mods & ~KeyMods.Ctrl;
            if ((mods & KeyMods.Shift) != 0 && (mods & KeyMods.Ctrl) != 0) yield return mods & ~(KeyMods.Shift | KeyMods.Ctrl);
        }

        bool Find(List<ActionScope> scopes, KeyStroke stroke, out Entry entry)
        {
            foreach (var s in scopes)
            {
                if (!byScope.TryGetValue(s, out var d) || !d.TryGetValue(stroke, out var l)) continue;
                foreach (var en in l)
                    if (!en.Chord.HasSecond && en.Info.Kind != ActionKind.Modifier) { entry = en; return true; }
            }
            entry = default;
            return false;
        }

        /// <summary>Whether a Modifier action is held: one of its chords' key is down (or, for a modifier-only chord, its
        /// modifiers are down) and the modifier state equals the chord's modifiers exactly. Ignores text focus (vanilla's
        /// click modifiers do too).</summary>
        public bool IsModifierHeld(string actionId, IEnumerable<string> keysDown, KeyMods mods)
        {
            var held = keysDown == null ? new HashSet<string>() : new HashSet<string>(keysDown, StringComparer.Ordinal);
            foreach (var c in map.ChordsOf(actionId))
            {
                if (c.HasSecond) continue;
                if (c.First.IsModifierOnly)
                {
                    if ((mods & c.First.Mods) == c.First.Mods && c.First.Mods != KeyMods.None) return true;
                    continue;
                }
                if (held.Contains(c.First.Key) && mods == c.First.Mods) return true;
            }
            return false;
        }
    }
}

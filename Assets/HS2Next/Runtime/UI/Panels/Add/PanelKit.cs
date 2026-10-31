// Shared helpers of package C's panels: the debounced search field (120 ms),
// kind glyphs of the tree, the add placement option → session option, and the drag controller used by the tree rows
// (reparent / reorder) and the browser rows (add onto the viewport or a tree row). Drags are UI Toolkit pointer events
// with pointer capture on the source (no OS / editor drag and drop), so they work in the player and with the synthetic
// events of `ui.input`.
using System;
using System.Collections.Generic;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    /// <summary>
    /// A per-frame pump for package C's background work (thumbnail loaders, card names, folder scans). VisualElement
    /// schedulers turned out not to run every frame for panels in the batch-mode tests (34 ticks in 3,000 frames), so the
    /// work is driven by one MonoBehaviour on the UI host object instead.
    /// </summary>
    public sealed class PanelTicker : MonoBehaviour
    {
        static PanelTicker instance;
        readonly List<Action> actions = new List<Action>();
        readonly List<Action> copy = new List<Action>();

        static PanelTicker Get()
        {
            if (instance != null) return instance;
            var go = new GameObject("HS2Next UI panel ticker") { hideFlags = HideFlags.HideAndDontSave };
            if (UiHost.Current != null) go.transform.SetParent(UiHost.Current.transform, false);
            instance = go.AddComponent<PanelTicker>();
            return instance;
        }

        /// <summary>Runs <paramref name="a"/> every frame until <see cref="Remove"/>.</summary>
        public static void Add(Action a) { if (a != null && Application.isPlaying) Get().actions.Add(a); }
        public static void Remove(Action a) { if (instance != null) instance.actions.Remove(a); }

        void Update()
        {
            copy.Clear();
            copy.AddRange(actions);
            foreach (var a in copy)
            {
                try { a(); }
                catch (Exception e) { Debug.LogError("[ui] panel tick " + a.Method.DeclaringType?.Name + "." + a.Method.Name + " threw: " + e); }
            }
        }
    }

    public static class PanelKit
    {
        public const int SearchDebounceMs = 120;

        /// <summary>Runs <paramref name="tick"/> every frame while <paramref name="el"/> is attached to a panel (from construction
        /// for elements built before attachment; removed on detach).</summary>
        public static void EveryFrame(VisualElement el, Action tick)
        {
            PanelTicker.Add(tick);
            el.RegisterCallback<AttachToPanelEvent>(_ => { PanelTicker.Remove(tick); PanelTicker.Add(tick); });
            el.RegisterCallback<DetachFromPanelEvent>(_ => PanelTicker.Remove(tick));
        }

        /// <summary>A search field (placeholder = search.placeholder) calling <paramref name="changed"/> 120 ms after the last keystroke
        /// (Enter / blur apply at once).</summary>
        public static TextField SearchField(string name, Action<string> changed)
        {
            var f = new TextField { name = name };
            f.AddToClassList("hs-search");
            f.textEdition.placeholder = L10n.Get("search.placeholder");
            Loc.Bind(f, "search.placeholder", (e, t) => ((TextField)e).textEdition.placeholder = t);
            IVisualElementScheduledItem pending = null;
            string last = "";
            void Fire()
            {
                pending?.Pause();
                if (f.value == last) return;
                last = f.value;
                changed(last);
            }
            f.RegisterValueChangedCallback(e =>
            {
                pending?.Pause();
                pending = f.schedule.Execute(Fire).StartingIn(SearchDebounceMs);
            });
            f.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Fire(); }, TrickleDown.TrickleDown);
            f.RegisterCallback<FocusOutEvent>(_ => Fire());
            return f;
        }

        /// <summary>The glyph of a node kind (characters by sex).</summary>
        public static string Glyph(TreeNode n)
        {
            switch (n.Kind)
            {
                case NodeKind.Chara: return StudioOps.SexOf(n) == 0 ? "♂" : "♀";
                case NodeKind.Item: return "◆";
                case NodeKind.Light: return "☀";
                case NodeKind.Folder: return "▤";
                case NodeKind.Camera: return "◉";
                case NodeKind.Route: return "↝";
                default: return "?";
            }
        }

        /// <summary>Applies option add.placement to the session (where new root nodes go; vanilla initialPosition).</summary>
        public static void ApplyPlacement(UiContext ctx)
        {
            string p = ctx.Options.String(OptionDefs.AddPlacement);
            ctx.Session.Options.NewNodePlacement = p == "origin" ? NewNodePlacement.Origin : NewNodePlacement.CameraPivot;
            var dp = HS2Next.Scenes.Runtime.SceneViewCamera.DefaultPivot;   // the view's pivot before any view is stored (Phase 3.6, UI2)
            ctx.Session.Options.DefaultPivot = new HS2Next.Save.Vec3(dp.x, dp.y, dp.z);
        }

        static StyleSheet sheet;

        /// <summary>Adds package C's style sheet (Resources/UI/C/panels-c.uss) to a panel or dialog root.</summary>
        public static T Style<T>(T el) where T : VisualElement
        {
            if (sheet == null) sheet = Resources.Load<StyleSheet>("UI/C/panels-c");
            if (sheet != null && el != null && !el.styleSheets.Contains(sheet)) el.styleSheets.Add(sheet);
            return el;
        }

        public static Label Text(string text, string cls)
        {
            var l = new Label(text);
            if (cls != null) l.AddToClassList(cls);
            return l;
        }

        public static VisualElement Row(string cls, params VisualElement[] children)
        {
            var r = new VisualElement();
            r.AddToClassList("hs-row");
            if (cls != null) r.AddToClassList(cls);
            foreach (var c in children) if (c != null) r.Add(c);
            return r;
        }
    }

    // ------------------------------------------------------------------ drag and drop

    /// <summary>What a drag carries.</summary>
    public abstract class DragPayload
    {
        /// <summary>Ghost text.</summary>
        public abstract string Label { get; }
    }

    /// <summary>Tree nodes being moved.</summary>
    public sealed class NodesPayload : DragPayload
    {
        public readonly List<string> Ids;
        readonly string label;
        public NodesPayload(List<string> ids, string label) { Ids = ids; this.label = label; }
        public override string Label => label;
    }

    /// <summary>A browser row being added (character card, item, light, …): <see cref="Add"/> performs the add under
    /// (parentId, point) — null = root — and returns the result (its NewIds are the new nodes).</summary>
    public sealed class AddPayload : DragPayload
    {
        public readonly string Text;
        public readonly Func<string, string, OpResult> Add;
        /// <summary>The added node can take an attach point under a character (every kind but maps / poses / animations).</summary>
        public bool Attachable = true;
        /// <summary>The history label of the add (a viewport drop wraps add + placement in one entry with it).</summary>
        public string OpLabel;
        public AddPayload(string text, string opLabel, Func<string, string, OpResult> add) { Text = text; OpLabel = opLabel; Add = add; }
        public override string Label => "+ " + Text;
    }

    /// <summary>A browser row applied to a node (animation, pose): <see cref="Apply"/> takes the target node id.</summary>
    public sealed class ApplyPayload : DragPayload
    {
        public readonly string Text;
        public readonly Func<string, OpResult> Apply;
        public readonly NodeKind Kind;
        public ApplyPayload(string text, NodeKind kind, Func<string, OpResult> apply) { Text = text; Kind = kind; Apply = apply; }
        public override string Label => Text;
    }

    public interface IDropTarget
    {
        /// <summary>The element whose world bound receives drops (null = inactive).</summary>
        VisualElement DropElement { get; }
        /// <summary>Hovering at <paramref name="panelPos"/>; returns false with a reason when the drop is not allowed there.</summary>
        bool Hover(DragPayload payload, Vector2 panelPos, out string reason);
        /// <summary>Dropped at <paramref name="panelPos"/> (only called when Hover allowed it).</summary>
        void Drop(DragPayload payload, Vector2 panelPos);
        void Leave();
    }

    /// <summary>
    /// One drag at a time. A source element gets <see cref="MakeSource"/>: pointer down with the primary button, then a move
    /// beyond <see cref="Threshold"/> logical px starts the drag (the pointer is captured by the source); targets are found by
    /// world bound (topmost registered first); Esc or a pointer cancel aborts.
    /// </summary>
    public static class DragController
    {
        public const float Threshold = 6f;
        static readonly List<IDropTarget> targets = new List<IDropTarget>();
        static VisualElement ghost;
        static IDropTarget hovered;

        public static DragPayload Current { get; private set; }
        /// <summary>The last drop's outcome ("dropped", "cancelled", "refused: …") for tests.</summary>
        public static string LastOutcome { get; private set; }
        public static event Action<DragPayload> Started, Ended;

        public static void Register(IDropTarget t) { if (!targets.Contains(t)) targets.Insert(0, t); }
        public static void Unregister(IDropTarget t) => targets.Remove(t);

        /// <summary>
        /// Makes <paramref name="source"/> draggable: <paramref name="payload"/> builds the payload when the drag starts (null =
        /// no drag). Returns the manipulator (for removal).
        /// </summary>
        public static Manipulator MakeSource(VisualElement source, Func<PointerDownEvent, DragPayload> payload)
        {
            var m = new SourceManipulator(payload);
            source.AddManipulator(m);
            return m;
        }

        sealed class SourceManipulator : PointerManipulator
        {
            readonly Func<PointerDownEvent, DragPayload> make;
            Func<DragPayload> pending;
            Vector2 start;
            int pointer = -1;
            bool dragging;

            public SourceManipulator(Func<PointerDownEvent, DragPayload> make) { this.make = make; }

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down);
                target.RegisterCallback<PointerMoveEvent>(Move);
                target.RegisterCallback<PointerUpEvent>(Up);
                target.RegisterCallback<PointerCaptureOutEvent>(CaptureOut);
                target.RegisterCallback<KeyDownEvent>(Key);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(Down);
                target.UnregisterCallback<PointerMoveEvent>(Move);
                target.UnregisterCallback<PointerUpEvent>(Up);
                target.UnregisterCallback<PointerCaptureOutEvent>(CaptureOut);
                target.UnregisterCallback<KeyDownEvent>(Key);
            }

            void Down(PointerDownEvent e)
            {
                if (e.button != 0 || Current != null) return;
                var ev = e;
                pending = () => make(ev);
                start = e.position;
                pointer = e.pointerId;
                dragging = false;
            }

            void Move(PointerMoveEvent e)
            {
                if (pending == null || e.pointerId != pointer) return;
                if (!dragging)
                {
                    if ((((Vector2)e.position) - start).magnitude < Threshold) return;
                    var p = pending();
                    if (p == null) { pending = null; return; }
                    dragging = true;
                    target.CapturePointer(pointer);
                    Begin(target, p, e.position);
                }
                Over(e.position);
                e.StopPropagation();
            }

            void Up(PointerUpEvent e)
            {
                if (e.pointerId != pointer) return;
                bool was = dragging;
                pending = null;
                dragging = false;
                pointer = -1;
                if (target.HasPointerCapture(e.pointerId)) target.ReleasePointer(e.pointerId);
                if (was) { Finish(e.position, false); e.StopPropagation(); }
            }

            void CaptureOut(PointerCaptureOutEvent e)
            {
                if (!dragging) return;
                dragging = false;
                pending = null;
                Finish(Vector2.zero, true);
            }

            void Key(KeyDownEvent e)
            {
                if (!dragging || e.keyCode != KeyCode.Escape) return;
                dragging = false;
                pending = null;
                if (pointer >= 0 && target.HasPointerCapture(pointer)) target.ReleasePointer(pointer);
                Finish(Vector2.zero, true);
                e.StopPropagation();
            }
        }

        static void Begin(VisualElement source, DragPayload p, Vector2 pos)
        {
            Current = p;
            var root = source.panel?.visualTree;
            ghost = new Label(p.Label) { name = "drag-ghost", pickingMode = PickingMode.Ignore };
            ghost.AddToClassList("hs-drag-ghost");
            ghost.style.position = Position.Absolute;
            if (root != null) { PanelKit.Style(root); root.Add(ghost); }
            Started?.Invoke(p);
            Place(pos);
        }

        static void Place(Vector2 pos)
        {
            if (ghost == null) return;
            ghost.style.left = pos.x + 12;
            ghost.style.top = pos.y + 8;
        }

        static IDropTarget At(Vector2 pos)
        {
            foreach (var t in targets)
            {
                var el = t.DropElement;
                if (el == null || el.panel == null || el.resolvedStyle.display == DisplayStyle.None) continue;
                if (el.worldBound.Contains(pos)) return t;
            }
            return null;
        }

        static void Over(Vector2 pos)
        {
            Place(pos);
            var t = At(pos);
            if (t != hovered) { hovered?.Leave(); hovered = t; }
            string reason = null;
            bool ok = t != null && t.Hover(Current, pos, out reason);
            ghost?.EnableInClassList("hs-drag-ghost--refused", t != null && !ok);
            if (ghost != null) ghost.tooltip = ok ? "" : reason ?? "";
            if (ghost is Label gl) gl.text = Current.Label + (t != null && !ok && !string.IsNullOrEmpty(reason) ? "  ⃠ " + reason : "");
        }

        static void Finish(Vector2 pos, bool cancel)
        {
            var p = Current;
            var t = cancel ? null : At(pos);
            try
            {
                if (cancel || p == null) LastOutcome = "cancelled";
                else if (t == null) LastOutcome = "no target";
                else if (!t.Hover(p, pos, out var why)) LastOutcome = "refused: " + why;
                else { t.Drop(p, pos); LastOutcome = "dropped"; }
            }
            finally
            {
                hovered?.Leave();
                hovered = null;
                ghost?.RemoveFromHierarchy();
                ghost = null;
                Current = null;
                Ended?.Invoke(p);
            }
        }

        /// <summary>Tests: runs a drop as the pointer would (target found at <paramref name="panelPos"/>).</summary>
        public static string SimulateDrop(DragPayload p, Vector2 panelPos)
        {
            Current = p;
            Finish(panelPos, false);
            return LastOutcome;
        }
    }
    /// <summary>
    /// Two panes beside each other in a wide area and one above the other in a tall one (the folder tree and the grid of
    /// the character and pose browsers: wide in the bottom region, tall in a sidebar): a TwoPaneSplitView with the first pane
    /// fixed, rebuilt with the other orientation when the shape changes. Side by side the first pane takes its wide size;
    /// stacked it takes a share of the height (at least a minimum). A splitter drag holds until the shape changes.
    /// </summary>
    public sealed class AdaptiveSplit : VisualElement
    {
        readonly VisualElement first, second;
        readonly float wideSize, tallShare, tallMin;
        public TwoPaneSplitView Split { get; private set; }
        /// <summary>The panes are one above the other (a tall area).</summary>
        public bool Stacked { get; private set; }

        /// <summary>A tall area: narrower than 560 px or taller than wide.</summary>
        public static bool IsTall(float width, float height) => width < 560f || height > width;

        public AdaptiveSplit(string name, VisualElement first, VisualElement second, float wideSize, float tallShare = 0.3f, float tallMin = 120f)
        {
            this.name = name;
            this.first = first;
            this.second = second;
            this.wideSize = wideSize;
            this.tallShare = tallShare;
            this.tallMin = tallMin;
            AddToClassList("hs-adaptive-split");
            Build(false, wideSize);
            RegisterCallback<GeometryChangedEvent>(e => Fit(e.newRect.width, e.newRect.height));
        }

        void Fit(float w, float h)
        {
            if (float.IsNaN(w) || float.IsNaN(h) || w <= 1 || h <= 1) return;
            bool tall = IsTall(w, h);
            if (tall == Stacked) return;
            Build(tall, tall ? Mathf.Max(tallMin, h * tallShare) : wideSize);
        }

        void Build(bool stacked, float size)
        {
            Stacked = stacked;
            foreach (var e in new[] { first, second })
            {
                e.RemoveFromHierarchy();
                var st = e.style;
                st.width = StyleKeyword.Null; st.height = StyleKeyword.Null; st.flexGrow = StyleKeyword.Null; st.flexShrink = StyleKeyword.Null;
                st.flexBasis = StyleKeyword.Null; st.left = StyleKeyword.Null; st.top = StyleKeyword.Null; st.display = StyleKeyword.Null;
            }
            Split?.RemoveFromHierarchy();
            Split = new TwoPaneSplitView(0, size, stacked ? TwoPaneSplitViewOrientation.Vertical : TwoPaneSplitViewOrientation.Horizontal) { name = name + "-split" };
            Split.AddToClassList("hs-grow");
            Split.Add(first);
            Split.Add(second);
            Add(Split);
            EnableInClassList("hs-adaptive-split--stacked", stacked);
        }
    }
}

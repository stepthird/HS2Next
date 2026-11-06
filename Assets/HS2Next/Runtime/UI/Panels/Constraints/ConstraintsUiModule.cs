// The constraints in the Studio UI: the panel "constraints" (default region bottom) and the action constraints.panel (View menu, unbound),
// which opens the panel's region with the panel shown, or collapses it when it is the shown panel. The bottom region stays collapsed until the
// user opens the panel: the panel is registered at start only when layout.json already lists it (the user opened it before), otherwise by the
// first constraints.panel; a «Reset layout» that would open the bottom region for the panels registered this way leaves it collapsed.
// Beside the panel: what its rows, the section and the editor share. ConstraintText says how an entry stands, ConstraintDrag is one drag of
// a weight or an offset, ConstraintView is the life of a view: subscribed from attach to detach, reading the list when it changes, when
// the scene recompiles and at most twice a second for the states, never while it is not shown.
using System;
using System.IO;
using System.Text;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class ConstraintsUiModule : IUiModule
    {
        public const string PanelId = "constraints";
        public const string TitleKey = "panel.constraints";
        public const string StyleResource = "UI/Constraints/constraints";

        public int Order => 46;

        static UiContext readyCtx;
        static object lastLayout;
        static StyleSheet sheet;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            readyCtx = null;
            lastLayout = null;
        }

        public void Register(UiContext ctx)
        {
            var a = ctx.Registry.Actions;
            if (ActionManifest.Contains(ActionIds.ConstraintsPanel))
            {
                var d = a.Register(ActionIds.ConstraintsPanel, call => TogglePanel(ctx), () => ctx.Shell != null);
                d.IsChecked = () => PanelShown(ctx);
            }
            if (LayoutListsPanel(ctx)) EnsurePanel(ctx);
            ctx.ViewStateChanged += what => { if (what == "layout") OnLayout(ctx); };
            readyCtx = ctx;
            UiHost.Ready -= OnReady;
            UiHost.Ready += OnReady;
        }

        static void OnReady(UiHost h)
        {
            UiHost.Ready -= OnReady;
            lastLayout = (h?.Context ?? readyCtx)?.Shell?.Layout;
        }

        /// <summary>The style sheet of the panel and the section, on <paramref name="el"/>.</summary>
        internal static void Style(VisualElement el)
        {
            sheet ??= Resources.Load<StyleSheet>(StyleResource);
            if (sheet != null && !el.styleSheets.Contains(sheet)) el.styleSheets.Add(sheet);
        }

        /// <summary>True when layout.json lists the panel (in a region or hidden): the user had it before.</summary>
        public static bool LayoutListsPanel(UiContext ctx)
        {
            try
            {
                string file = Path.Combine(ctx.Options.ConfigDir, LayoutDocument.FileName);
                if (!File.Exists(file)) return false;
                var d = LayoutDocument.Parse(File.ReadAllText(file, Encoding.UTF8));
                return d != null && (d.RegionOf(PanelId) != null || d.Hidden.Contains(PanelId));
            }
            catch (Exception) { return false; }
        }

        /// <summary>Registers the panel (default region bottom) when it is not registered yet.</summary>
        public static void EnsurePanel(UiContext ctx)
        {
            if (ctx.Registry.Panels.TryGet(PanelId, out _)) return;
            ctx.Registry.Panels.Register(PanelId, TitleKey, LayoutRegions.Bottom, c => new ConstraintsPanel(c), ActionScope.Global);
        }

        /// <summary>The panel is the shown tab of an open region.</summary>
        public static bool PanelShown(UiContext ctx)
        {
            var sh = ctx.Shell;
            string r = sh?.RegionOf(PanelId);
            if (r == null) return false;
            var reg = sh.Regions[r];
            return !reg.Collapsed && reg.Active == PanelId;
        }

        /// <summary>constraints.panel: shows the panel (its region opened, the panel active), or collapses the region when the panel is its
        /// shown tab. Returns true when the panel is shown afterwards.</summary>
        public static bool TogglePanel(UiContext ctx)
        {
            var sh = ctx.Shell;
            if (sh == null) return false;
            EnsurePanel(ctx);
            if (PanelShown(ctx))
            {
                sh.SetCollapsed(sh.Regions[sh.RegionOf(PanelId)], true, true);
                return false;
            }
            return sh.ShowPanel(PanelId);
        }

        /// <summary>Shows the panel and returns it (null without a shell).</summary>
        public static ConstraintsPanel Show(UiContext ctx)
        {
            var sh = ctx.Shell;
            if (sh == null) return null;
            EnsurePanel(ctx);
            return sh.ShowPanel(PanelId) ? sh.PanelRoot(PanelId)?.Q<ConstraintsPanel>() : null;
        }

        // A layout replaced as a whole («Reset layout»): the bottom region does not open for the panels that appear only when asked for.
        static void OnLayout(UiContext ctx)
        {
            var sh = ctx.Shell;
            if (sh?.Layout == null || ReferenceEquals(sh.Layout, lastLayout)) return;
            bool first = lastLayout == null;
            lastLayout = sh.Layout;
            if (first || !sh.Regions.TryGetValue(LayoutRegions.Bottom, out var bottom) || bottom.Collapsed || !bottom.Panels.Contains(PanelId)) return;
            foreach (var p in bottom.Panels) if (p != PanelId && p != TimelineUiModule.PanelId) return;
            sh.SetCollapsed(bottom, true, true);
        }
    }

    /// <summary>How an entry stands, in the current language.</summary>
    public static class ConstraintText
    {
        /// <summary>The state marks an end that does not act: the row is greyed.</summary>
        public static bool Broken(ConstraintInfo i) =>
            i.State == ConstraintStates.SourceMissing || i.State == ConstraintStates.TargetMissing || i.State == ConstraintStates.Cycle ||
            i.State == ConstraintStates.Protected || i.State == ConstraintStates.Bad || i.State == ConstraintStates.Limit;

        static bool IsKey(string s) => s != null && s.StartsWith("constraints.", StringComparison.Ordinal);

        /// <summary>The state's line, or null: an active entry, or one that is off, says nothing of its state here.</summary>
        public static string State(ConstraintInfo i)
        {
            switch (i.State)
            {
                case ConstraintStates.Off: return L10n.Get("constraints.state.off");
                case ConstraintStates.SourceMissing: return L10n.Format("constraints.state.sourceMissing", i.Reason ?? "");
                case ConstraintStates.TargetMissing: return L10n.Format("constraints.state.targetMissing", i.Reason ?? "");
                case ConstraintStates.Cycle: return L10n.Format("constraints.state.cycle", i.Reason ?? "");
                case ConstraintStates.Protected: return L10n.Get("constraints.state.protected");
                case ConstraintStates.Bad: return L10n.Format("constraints.state.bad", i.Reason ?? "");
                case ConstraintStates.Pending: return L10n.Get("constraints.state.pending");
                case ConstraintStates.Limit: return L10n.Format("constraints.state.limit", ConstraintLimits.MaxEntries);
            }
            return null;
        }

        /// <summary>The mark of a row: "" when there is nothing to say.</summary>
        public static string Mark(ConstraintInfo i)
        {
            if (Broken(i)) return "!";
            if (i.State == ConstraintStates.Pending) return "…";
            if (i.State != ConstraintStates.Active) return "";
            if (!i.Effective) return "!";
            return IsKey(i.Reason) ? "i" : "";
        }

        /// <summary>Everything the row's tooltip says, one thing a line. <paramref name="band"/>: the target bone is one of the FK bones.</summary>
        public static string Tip(ConstraintInfo i, bool band)
        {
            var sb = new StringBuilder();
            void Line(string s)
            {
                if (string.IsNullOrEmpty(s)) return;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(s);
            }
            Line(State(i));
            if (i.State == ConstraintStates.Active && IsKey(i.Reason)) Line(L10n.Get(i.Reason));
            if (i.Lag != 0) Line(L10n.Get("constraints.lagTip"));
            if (band) Line(L10n.Get("constraints.bandTip"));
            return sb.ToString();
        }
    }

    /// <summary>
    /// One drag of a weight or an offset: one UI edit, each step goes to the operation with a merge key and shows at once, so the release leaves
    /// one entry, and none when the entry is back as it was at the press. Esc goes through the router, which cancels the edit: the steps
    /// that follow are dropped.
    /// </summary>
    public sealed class ConstraintDrag
    {
        /// <summary>A step's merge key: what puts it on the open edit's bus directly.</summary>
        public const string StepKey = "ui.drag";

        readonly UiContext ctx;
        readonly Action ended;
        UiEdit edit;
        bool dropped;
        string id;
        ConstraintEntry atPress;

        public ConstraintDrag(UiContext ctx, Action ended)
        {
            this.ctx = ctx;
            this.ended = ended;
        }

        /// <summary>An edit is open and takes steps.</summary>
        public bool Open => edit != null && edit.IsOpen && !dropped;
        /// <summary>The last result of the drag (tests).</summary>
        public OpResult LastResult { get; private set; }

        /// <summary>The press: keeps the entry and opens the edit.</summary>
        public void Begin(string entryId)
        {
            edit?.Cancel();
            dropped = false;
            id = entryId;
            atPress = ctx.Model.Constraints?.Find(entryId);
            var mine = ctx.BeginEdit(OpLabels.Constraint);
            if (!mine.IsOpen)
            {
                ctx.ShowResult(OpResult.From(mine.Failure));
                dropped = true;
                return;
            }
            edit = mine;
            mine.Ended += committed =>
            {
                if (edit == mine) edit = null;
                if (committed) return;
                dropped = true;
                ended();
            };
        }

        /// <summary>A move: a refusal ends the drag, once.</summary>
        public void Step(ConstraintEdit e)
        {
            if (!Open) return;
            var r = ctx.Ops.SetConstraint(id, e, UiContext.User(OpLabels.Constraint, StepKey));
            LastResult = r;
            if (r.Ok) return;
            ctx.ShowResult(r);
            edit?.Cancel();
        }

        /// <summary>The release.</summary>
        public OpResult End()
        {
            var e = edit;
            edit = null;
            OpResult r = OpResult.NothingToDo();
            if (e != null && e.IsOpen)
            {
                if (Same(atPress, ctx.Model.Constraints?.Find(id))) e.Cancel();
                else r = ctx.ShowResult(e.Commit());
            }
            atPress = null;
            LastResult = r;
            ended();
            return r;
        }

        /// <summary>Takes the drag back.</summary>
        public void Cancel()
        {
            var e = edit;
            edit = null;
            atPress = null;
            e?.Cancel();
        }

        static bool Same(ConstraintEntry a, ConstraintEntry b) =>
            ReferenceEquals(a, b) || (a != null && b != null && FloatBits.Same(a.Weight, b.Weight) && a.Offset.Pos.Equals(b.Offset.Pos) && a.Offset.Rot.Equals(b.Offset.Rot) && a.Offset.Scale.Equals(b.Offset.Scale));
    }

    /// <summary>A view of the list. Subscribed from attach to detach; a change of the list, of the tree or of a name reads at the next frame, a
    /// recompile too, the states at most twice a second; nothing is read while the view is not shown or an edit is open.</summary>
    public abstract class ConstraintView : VisualElement
    {
        public const float RefreshSeconds = 0.5f;

        protected readonly UiContext Ctx;
        bool dirty = true, hidden;
        float nextRead;
        int seen = int.MinValue;

        /// <summary>Reads of the list so far (tests: none while the view is closed).</summary>
        public int Reads { get; private set; }

        protected ConstraintView(UiContext ctx)
        {
            Ctx = ctx;
            ConstraintsUiModule.Style(this);
            RegisterCallback<AttachToPanelEvent>(OnAttached);
            RegisterCallback<DetachFromPanelEvent>(OnDetached);
            PanelKit.EveryFrame(this, Tick, fromConstruction: false);
        }

        void OnAttached(AttachToPanelEvent e)
        {
            Ctx.SceneChanged -= OnSceneChanged;
            Ctx.SceneChanged += OnSceneChanged;
            Ctx.SceneCleared -= OnCleared;
            Ctx.SceneCleared += OnCleared;
            Ctx.SceneLoaded -= OnLoaded;
            Ctx.SceneLoaded += OnLoaded;
            Ctx.SelectionChanged -= OnSelection;
            Ctx.SelectionChanged += OnSelection;
            Ctx.ViewStateChanged -= OnViewState;
            Ctx.ViewStateChanged += OnViewState;
            dirty = true;
            hidden = false;
            Opened();
        }

        void OnDetached(DetachFromPanelEvent e)
        {
            Ctx.SceneChanged -= OnSceneChanged;
            Ctx.SceneCleared -= OnCleared;
            Ctx.SceneLoaded -= OnLoaded;
            Ctx.SelectionChanged -= OnSelection;
            Ctx.ViewStateChanged -= OnViewState;
            Closed();
        }

        void OnSceneChanged(SceneChangeBatch b)
        {
            if (b.IsStructural || b.Touches(null, ConstraintEditCommand.ChangePath)) { dirty = true; return; }
            foreach (var c in b.Changes)
                if (c.Path == "name") { dirty = true; return; }
        }

        void OnCleared() => dirty = true;
        void OnLoaded(SceneLoadedArgs a) => dirty = true;
        void OnSelection(SelectionChangedArgs a) => dirty = true;
        void OnViewState(string what) { if (what == "language") dirty = true; }

        /// <summary>The view reads at the next frame.</summary>
        public void MarkDirty() => dirty = true;

        /// <summary>Attached to a panel.</summary>
        protected virtual void Opened() { }
        /// <summary>Taken off the panel: whatever is open ends here.</summary>
        protected virtual void Closed() { }
        /// <summary>Reads the list and shows it.</summary>
        protected abstract void Read();

        bool ShownNow()
        {
            if (panel == null) return false;
            for (VisualElement e = this; e != null; e = e.hierarchy.parent)
                if (e.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }

        bool Editing => Ctx.ActiveEdit != null && Ctx.ActiveEdit.IsOpen;

        void Tick()
        {
            float now = Time.unscaledTime;
            int version = Ctx.Ops?.Constraints?.Version ?? 0;
            bool due = now >= nextRead;
            if (!due && (hidden || (!dirty && version == seen))) return;
            if (due) nextRead = now + RefreshSeconds;
            hidden = !ShownNow();
            if (hidden || Editing) return;
            ReadNow();
        }

        /// <summary>Reads now, when the view is shown and no edit is open.</summary>
        public void ReadNow()
        {
            if (!ShownNow() || Editing) return;
            dirty = false;
            seen = Ctx.Ops?.Constraints?.Version ?? 0;
            Reads++;
            Read();
        }
    }
}

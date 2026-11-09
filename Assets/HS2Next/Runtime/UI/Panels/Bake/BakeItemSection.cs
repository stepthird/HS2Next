// The section «Bake» in the inspector of an item drawn from a bake: collapsed by default; its kind, source, size and the count of its mosaic parts, «Keep in the store» and
// «Open in panel»; for a piece also its cut: the count of steps, «Edit cut» (the Cutter with this piece and a marker put at its last step) and «Re-cut from source» (one entry
// «Edit cut» when the result changes). It reads bake.status and cut.status only while it is open and on a panel, so a closed section reads nothing and holds no subscriber.
// Element names: section-bake-item, bake-item-kind, bake-item-source, bake-item-size, bake-item-members, bake-item-keep, bake-item-open, bake-item-steps, bake-item-edit-cut,
// bake-item-recut.
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class BakeItemSection : VisualElement
    {
        public const string FoldName = "section-bake-item";
        const long RefreshMs = 500;

        static bool open;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => open = false;

        /// <summary>The section stands on an item drawn from a bake (a bake item or a piece), never on a plain item or a marker.</summary>
        public static bool StandsOn(TreeNode n) => n != null && n.Kind == NodeKind.Item && n.State is ItemNodeState s && s.Bake != null;

        readonly UiContext ctx;
        readonly string node;
        readonly bool piece;
        readonly Foldout fold;
        Label kind, source, size, members, steps;
        string shown;

        /// <summary>Reads so far (tests: none while the section is closed).</summary>
        public int Reads { get; private set; }
        public Foldout Fold => fold;
        public bool Piece => piece;

        public BakeItemSection(UiContext ctx, string node)
        {
            this.ctx = ctx;
            this.node = node;
            piece = ctx.Model.Find(node)?.State is ItemNodeState s && s.Cut != null;
            name = "bake-item-section";
            BakeUiModule.Style(this);
            fold = InspectorUi.Section("bake.item.title", open, FoldName);
            fold.RegisterValueChangedCallback(OnFold);
            Add(fold);
            RegisterCallback<AttachToPanelEvent>(_ => Listen(open));
            RegisterCallback<DetachFromPanelEvent>(_ => Listen(false));
            if (open) Build();
        }

        void OnFold(ChangeEvent<bool> e)
        {
            if (e.target != fold) return;
            open = e.newValue;
            if (open) Build();
            Listen(open && panel != null);
        }

        IVisualElementScheduledItem ticking;

        void Build()
        {
            if (kind != null) return;
            kind = Line("bake-item-kind");
            source = Line("bake-item-source");
            size = Line("bake-item-size");
            members = Line("bake-item-members");
            var bar = BakeUi.Bar("bake-item-bar");
            BakeUi.Btn(bar, "bake-item-keep", "bake.keep", Keep);
            BakeUi.Btn(bar, "bake-item-open", "bake.openPanel", () => BakeUiModule.Show(ctx));
            fold.Add(bar);
            if (!piece) return;
            var cut = new Label { name = "bake-item-cut" };
            Loc.Bind(cut, "cut.make");
            cut.AddToClassList("hs-bake__caption");
            fold.Add(cut);
            steps = Line("bake-item-steps");
            var cutBar = BakeUi.Bar("bake-item-cut-bar");
            BakeUi.Btn(cutBar, "bake-item-edit-cut", "bake.item.editCut", () => CutUiModule.EditCut(ctx, node));
            BakeUi.Btn(cutBar, "bake-item-recut", "bake.item.recut", Recut);
            fold.Add(cutBar);
        }

        Label Line(string id)
        {
            var l = BakeUi.Text(fold, id, "hs-bake__status");
            return l;
        }

        void Listen(bool on)
        {
            ticking?.Pause();
            if (!on) return;
            shown = null;
            Read();
            if (ticking == null) ticking = schedule.Execute(Read).Every(RefreshMs);
            else ticking.Resume();
        }

        void Read()
        {
            if (kind == null) return;
            // what is read changes with the node's bake block only: read again when it changes
            string now = (ctx.Model.Find(node)?.State as ItemNodeState)?.Bake?.Main + "/" + ctx.L10n.Language;
            if (now == shown) return;
            shown = now;
            Reads++;
            var r = ctx.Ops.BakeStatus(node);
            if (!(r.Value is BakeStatusInfo s)) return;
            kind.text = ctx.L10n.Format("bake.item.kind", BakePanel.KindText(s.Kind));
            source.text = ctx.L10n.Format("bake.item.source", s.Source?.Name ?? "");
            size.text = ctx.L10n.Format("bake.item.size", BakeUi.Size(s.Bytes));
            members.text = ctx.L10n.Format("bake.item.members", s.Members.Count);
            if (steps != null && ctx.Ops.CutStatus(node).Value is CutStatusInfo c) steps.text = ctx.L10n.Format("bake.item.steps", c.Steps.Count);
        }

        void Keep()
        {
            var r = ctx.ShowResult(ctx.Ops.BakeKeep(node));
            if (r.Ok && r.Value is BakeKeepResult k)
                ctx.Toasts?.Show(ctx.L10n.Format(k.Kept ? "bake.kept" : "bake.keptAlready", k.No), ToastKind.Info);
        }

        void Recut()
        {
            var r = ctx.ShowResult(ctx.Ops.CutSet(node, new CutSetRequest { Recut = true }, UiContext.User(OpLabels.CutEdit)));
            if (r.Ok && r.Value is CutSetResult c && !c.Changed) ctx.Toasts?.Show(ctx.L10n.Get("bake.item.recutSame"), ToastKind.Info);
            shown = null;
        }
    }
}

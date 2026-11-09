// The section «Chara to Item» in the character's State tab: collapsed by default, «Bake whole», «Bake parts…» (the panel with this character as its target) and the name of
// the last bake made from this character. «Bake whole» bakes the character as the panel's options say (one entry «Bake to item»). It reads only while it is open and on a
// panel, so a closed section reads nothing and holds no subscriber. Element names: section-bake, bake-section-whole, bake-section-parts, bake-section-last.
using System.Collections.Generic;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class BakeSection : VisualElement
    {
        public const string FoldName = "section-bake";
        const long RefreshMs = 500;

        // open or closed, across rebuilds of the inspector
        static bool open;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => open = false;

        readonly UiContext ctx;
        readonly string node;
        readonly Foldout fold;
        Label last;
        Button whole;
        IVisualElementScheduledItem ticking;
        bool busy;

        /// <summary>Reads so far (tests: none while the section is closed).</summary>
        public int Reads { get; private set; }
        public Foldout Fold => fold;

        public BakeSection(UiContext ctx, string node)
        {
            this.ctx = ctx;
            this.node = node;
            name = "bake-section";
            BakeUiModule.Style(this);
            fold = InspectorUi.Section(BakeUiModule.TitleKey, open, FoldName);
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

        void Build()
        {
            if (last != null) return;
            var bar = BakeUi.Bar("bake-section-bar");
            whole = BakeUi.Btn(bar, "bake-section-whole", "bake.section.whole", BakeWhole);
            BakeUi.Btn(bar, "bake-section-parts", "bake.section.parts", () => BakeUiModule.ShowWith(ctx, new[] { node }));
            fold.Add(bar);
            last = BakeUi.Text(fold, "bake-section-last");
            last.AddToClassList(InspectorUi.Dim);
        }

        void Listen(bool on)
        {
            ticking?.Pause();
            if (!on) return;
            Read();
            if (ticking == null) ticking = schedule.Execute(Read).Every(RefreshMs);
            else ticking.Resume();
        }

        void Read()
        {
            if (last == null || busy) return;
            Reads++;
            string n = BakeUiModule.LastBakeOf(node);
            last.text = n != null ? ctx.L10n.Format("bake.section.last", n) : ctx.L10n.Get("bake.section.none");
            whole.SetEnabled(!busy && StudioOps.BakeHasDevice);
        }

        // the whole character with the panel's options: one entry, the character hidden as the option says
        void BakeWhole()
        {
            if (busy) return;
            busy = true;
            whole.SetEnabled(false);
            last.text = ctx.L10n.Get("bake.capturing");
            var ids = new List<string> { node };
            var opt = BakePanel.WholeOptions();
            UiDefer.Frames(2, () =>
            {
                OpResult r = null;
                try { r = ctx.Ops.BakeMake(ids, opt, UiContext.User(OpLabels.BakeMake)); }
                finally { busy = false; }
                ctx.ShowResult(r);
                if (r.Ok && r.Value is BakeMakeResult made) BakeUiModule.NoteBakes(ctx, ids, made);
                Read();
            });
        }
    }
}

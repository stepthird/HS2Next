// The section «Ragdoll» in the character's State tab: collapsed by default, the state of the body, «Ragdoll», «Release» and «Open in panel». It reads only while it is open and on a panel, so a
// closed section reads nothing and holds no subscriber. Element names: section-ragdoll, ragdoll-section-state, ragdoll-section-make, ragdoll-section-release, ragdoll-section-open.
using HS2Next.Scenes;
using HS2Next.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class RagdollSection : VisualElement
    {
        public const string FoldName = "section-ragdoll";
        const long RefreshMs = 250;

        // open or closed, across rebuilds of the inspector
        static bool open;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => open = false;

        readonly UiContext ctx;
        readonly string node;
        readonly Foldout fold;
        Label state;
        Button make, release;
        IVisualElementScheduledItem ticking;

        /// <summary>Reads of the state so far (tests: none while the section is closed).</summary>
        public int Reads { get; private set; }
        public Foldout Fold => fold;

        public RagdollSection(UiContext ctx, string node)
        {
            this.ctx = ctx;
            this.node = node;
            name = "ragdoll-section";
            fold = InspectorUi.Section(RagdollUiModule.TitleKey, open, FoldName);
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
            if (state != null) return;
            state = new Label { name = "ragdoll-section-state" };
            state.AddToClassList(InspectorUi.Dim);
            fold.Add(state);
            make = Loc.Button("ragdoll.make", () => { ctx.ShowResult(ctx.Ops.RagdollBuild(node, true, null, UiContext.User(OpLabels.RagdollBuild))); Read(); });
            make.name = "ragdoll-section-make";
            fold.Add(make);
            release = Loc.Button("ragdoll.release", () => { ctx.ShowResult(ctx.Ops.RagdollRelease(node, true, UiContext.User(OpLabels.RagdollRelease))); Read(); });
            release.name = "ragdoll-section-release";
            fold.Add(release);
            var b = Loc.Button("ragdoll.openPanel", () => RagdollUiModule.Show(ctx));
            b.name = "ragdoll-section-open";
            fold.Add(b);
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
            if (state == null) return;
            Reads++;
            var row = ctx.Ops.RagdollStatus(node);
            bool on = row?.State != null;
            state.text = ctx.L10n.Get(on ? "ragdoll.state." + row.State : "ragdoll.state.none");
            make.SetEnabled(row != null && !on);
            release.SetEnabled(on);
        }
    }
}

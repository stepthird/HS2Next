// The section «Wet and stains» at the end of the character's State tab: collapsed by default, the names of the character's stains and «Open in panel». It reads the block only
// while it is open and on a panel, so a closed section reads nothing and holds no subscriber. Element names: section-wet, wet-section-list, wet-section-open.
using System.Text;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class WetSection : VisualElement
    {
        public const string FoldName = "section-wet";

        // open or closed, across rebuilds of the inspector
        static bool open;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => open = false;

        readonly UiContext ctx;
        readonly string node;
        readonly Foldout fold;
        Label list;

        /// <summary>Reads of the block so far (tests: none while the section is closed).</summary>
        public int Reads { get; private set; }
        public Foldout Fold => fold;

        public WetSection(UiContext ctx, string node)
        {
            this.ctx = ctx;
            this.node = node;
            name = "wet-section";
            fold = InspectorUi.Section(WetUiModule.TitleKey, open, FoldName);
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
            if (list != null) return;
            list = new Label { name = "wet-section-list" };
            list.AddToClassList(InspectorUi.Dim);
            fold.Add(list);
            var b = Loc.Button("wet.openPanel", () => WetUiModule.Show(ctx));
            b.name = "wet-section-open";
            fold.Add(b);
        }

        void Listen(bool on)
        {
            ctx.SceneChanged -= OnSceneChanged;
            if (!on) return;
            ctx.SceneChanged += OnSceneChanged;
            Read();
        }

        void OnSceneChanged(SceneChangeBatch b)
        {
            if (b.Touches(node, "chara.wet")) Read();
        }

        void Read()
        {
            if (list == null) return;
            Reads++;
            var block = ctx.Ops.WetBlock(node);
            if (block == null || block.Stains.Count == 0) { list.text = ctx.L10n.Get("wet.none"); return; }
            var sb = new StringBuilder();
            foreach (var s in block.Stains) sb.Append(sb.Length > 0 ? ", " : "").Append(s.Name);
            list.text = sb.ToString();
        }
    }
}

// The section Constraints of a node's inspector: on items, lights, folders, cameras and routes, and at the end of the character's Kinematics tab.
// Collapsed by default. Its rows are the entries that have this node at either end, the same row as the panel's; «Add» opens the panel's
// form with this node as the target, «Open in panel» shows the panel. The view stands on the panel only while the section is open, so a closed
// section reads nothing and holds no subscriber. With several nodes selected it lists the primary's and edits nothing.
// Element names: section-constraints, constraint-section-row-<i> (as the panel's rows), constraint-section-none, constraint-section-add,
// constraint-section-open, constraint-section-primary.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class ConstraintSection : VisualElement
    {
        public const string FoldName = "section-constraints";

        // open or closed, across rebuilds of the inspector
        static bool open;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => open = false;

        readonly UiContext ctx;
        readonly string node;
        readonly Foldout fold;

        /// <summary>The view, built when first opened; null before (tests).</summary>
        public ConstraintSectionView View { get; private set; }
        public Foldout Fold => fold;

        /// <summary>The kinds of the node inspector that end with the section (a character has it in its Kinematics tab).</summary>
        public static bool StandsOn(NodeKind kind) => kind == NodeKind.Item || kind == NodeKind.Light || kind == NodeKind.Folder || kind == NodeKind.Camera || kind == NodeKind.Route;

        public ConstraintSection(UiContext ctx, string node)
        {
            this.ctx = ctx;
            this.node = node;
            name = "constraint-section";
            fold = InspectorUi.Section(ConstraintsUiModule.TitleKey, open, FoldName);
            fold.RegisterValueChangedCallback(OnFold);
            Add(fold);
            if (open) Show();
        }

        void OnFold(ChangeEvent<bool> e)
        {
            if (e.target != fold) return;
            open = e.newValue;
            if (open) Show();
            else View?.RemoveFromHierarchy();
        }

        void Show()
        {
            View ??= new ConstraintSectionView(ctx, node);
            if (View.parent != fold.contentContainer) fold.Add(View);
        }
    }

    /// <summary>What the open section shows.</summary>
    public sealed class ConstraintSectionView : ConstraintView
    {
        readonly string node;
        readonly VisualElement rowsBox;
        readonly List<ConstraintLine> lines = new List<ConstraintLine>();
        readonly Label none, primaryOnly;
        readonly Button add, open;

        /// <summary>The rows shown (tests).</summary>
        public IReadOnlyList<ConstraintLine> Lines => lines;

        public ConstraintSectionView(UiContext ctx, string node) : base(ctx)
        {
            this.node = node;
            name = "constraint-section-view";
            primaryOnly = InspectorUi.Note("constraints.primaryOnly");
            primaryOnly.name = "constraint-section-primary";
            primaryOnly.style.whiteSpace = WhiteSpace.Normal;
            Add(primaryOnly);
            rowsBox = new VisualElement { name = "constraint-section-rows" };
            Add(rowsBox);
            none = InspectorUi.Note("constraints.none");
            none.name = "constraint-section-none";
            Add(none);
            add = InspectorUi.Button("constraints.add", AddHere, "constraint-section-add");
            open = InspectorUi.Button("constraints.openPanel", OpenPanel, "constraint-section-open");
            var line = InspectorUi.Line(add, open);
            line.style.flexWrap = Wrap.Wrap;
            Add(line);
        }

        protected override void Closed()
        {
            foreach (var l in lines) l.EndEdit();
        }

        protected override void Read()
        {
            var infos = Ctx.Ops.ConstraintsOf(node);
            var list = Ctx.Model.Constraints;
            int count = list?.Count ?? 0;
            bool one = Ctx.Selection.Count <= 1;
            while (lines.Count > infos.Count)
            {
                lines[lines.Count - 1].RemoveFromHierarchy();
                lines.RemoveAt(lines.Count - 1);
            }
            while (lines.Count < infos.Count)
            {
                var l = new ConstraintLine(Ctx, "constraint-section-row-", ShowEntry, MarkDirty);
                lines.Add(l);
                rowsBox.Add(l);
            }
            for (int i = 0; i < infos.Count; i++) lines[i].Bind(infos[i], list.IndexOf(infos[i].Entry.Id), count, i, false, one);
            none.style.display = infos.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            primaryOnly.style.display = one ? DisplayStyle.None : DisplayStyle.Flex;
            add.SetEnabled(one);
        }

        void AddHere() => ConstraintsUiModule.Show(Ctx)?.BeginAdd(new ConstraintEnd(node));

        void OpenPanel()
        {
            var p = ConstraintsUiModule.Show(Ctx);
            if (p == null || lines.Count == 0) return;
            if (Ctx.Ops.ConstraintOf(p.Selected) == null || !HasRow(p.Selected)) p.Select(lines[0].Id);
        }

        bool HasRow(string id)
        {
            foreach (var l in lines) if (l.Id == id) return true;
            return false;
        }

        void ShowEntry(string id) => ConstraintsUiModule.Show(Ctx)?.Select(id);
    }
}

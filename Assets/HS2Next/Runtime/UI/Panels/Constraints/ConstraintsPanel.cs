// The Constraints panel (panel id "constraints"): the scene's list on the left, the selected entry on the right, one above the other in a
// narrow or tall area. The list is virtualised, one row per entry in list order; «Add» opens the editor empty, «Clear» removes every entry.
// Every edit goes through StudioOps, one history entry each, so the panel works with the bridge off. Element names (ui.query / ui.input):
// constraints-add, constraints-clear, constraints-list, constraints-row-<i> (-on, -text, -lag, -mark, -weight, -up, -down, -remove),
// constraints-none, constraints-editor and what ConstraintEditor.cs names.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class ConstraintsPanel : ConstraintView
    {
        const float RowHeight = 26f, ListWidth = 460f;

        /// <summary>The panel on the shell now, or null.</summary>
        public static ConstraintsPanel Current { get; private set; }

        readonly List<ConstraintInfo> rows = new List<ConstraintInfo>();
        readonly List<ConstraintLine> lines = new List<ConstraintLine>();
        readonly ListView list;
        readonly Label none;
        readonly Button clearButton;

        /// <summary>The entry the editor shows, or null.</summary>
        public string Selected { get; private set; }
        public ConstraintEditor Editor { get; }
        public ListView List => list;
        public IReadOnlyList<ConstraintInfo> Rows => rows;
        /// <summary>The rows built now (tests).</summary>
        public IReadOnlyList<ConstraintLine> Lines => lines;

        public ConstraintsPanel(UiContext ctx) : base(ctx)
        {
            name = "constraints-panel";
            AddToClassList("hs-constraints");
            style.flexGrow = 1;
            focusable = true;

            var bar = new VisualElement { name = "constraints-bar" };
            bar.AddToClassList("hs-constraints__bar");
            var add = Loc.Button("constraints.add", () => BeginAdd());
            add.name = "constraints-add";
            clearButton = Loc.Button("constraints.clear", ClearAll);
            clearButton.name = "constraints-clear";
            bar.Add(add);
            bar.Add(clearButton);
            Add(bar);

            var left = new VisualElement { name = "constraints-left" };
            left.AddToClassList("hs-constraints__pane");
            none = InspectorUi.Note("constraints.noEntries");
            none.name = "constraints-none";
            left.Add(none);
            list = new ListView
            {
                name = "constraints-list", fixedItemHeight = RowHeight, virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.None, itemsSource = rows, makeItem = MakeLine, destroyItem = DestroyLine, bindItem = BindLine, unbindItem = UnbindLine,
            };
            list.AddToClassList("hs-grow");
            list.style.flexGrow = 1;
            left.Add(list);
            Editor = new ConstraintEditor(ctx, this);
            var split = new AdaptiveSplit("constraints", left, Editor, ListWidth, 0.4f, 120f);
            split.style.flexGrow = 1;
            Add(split);
        }

        VisualElement MakeLine()
        {
            var l = new ConstraintLine(Ctx, "constraints-row-", Select, MarkDirty);
            lines.Add(l);
            return l;
        }

        void DestroyLine(VisualElement e) => lines.Remove((ConstraintLine)e);

        void UnbindLine(VisualElement e, int i) => ((ConstraintLine)e).Unbind();

        void BindLine(VisualElement e, int i)
        {
            if (i < 0 || i >= rows.Count) return;
            ((ConstraintLine)e).Bind(rows[i], i, rows.Count, i, rows[i].Entry.Id == Selected, true);
        }

        protected override void Opened() => Current = this;

        protected override void Closed()
        {
            if (Current == this) Current = null;
            foreach (var l in lines) l.EndEdit();
            Editor.EndEdits();
        }

        protected override void Read()
        {
            rows.Clear();
            rows.AddRange(Ctx.Ops.ConstraintsOf());
            list.RefreshItems();
            none.style.display = rows.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            list.style.display = rows.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            clearButton.SetEnabled(rows.Count > 0);
            if (Selected != null && Ctx.Model.Constraints?.Find(Selected) == null) Select(null);
            Editor.Read();
        }

        /// <summary>Shows an entry in the editor (null: none).</summary>
        public void Select(string id)
        {
            Selected = id;
            Editor.Edit(id);
            list.RefreshItems();
        }

        /// <summary>Opens the editor empty, with <paramref name="target"/> preset when given.</summary>
        public void BeginAdd(ConstraintEnd target = null)
        {
            Selected = null;
            Editor.BeginAdd(target);
            list.RefreshItems();
        }

        void ClearAll()
        {
            Ctx.ShowResult(Ctx.Ops.ClearConstraints(UiContext.User(OpLabels.ConstraintClear)));
            MarkDirty();
        }
    }
}

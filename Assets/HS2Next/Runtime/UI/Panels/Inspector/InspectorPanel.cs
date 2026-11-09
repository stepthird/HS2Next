// The inspector panel: a header (kind, editable name, node id with a copy button, findings
// badge), the Transform section (every selected node with the channel), then the primary kind's sections: the character's
// tabs (CharaInspector), foldouts for items, lights and cameras, the folder's name only, a Phase 3.6 note for routes.
// Multi-selection: the primary's kind; kind rows apply to every selected node of that kind (U25 / T22), mixed values show
// "—". Values follow the model through A's bindings; the panel itself is rebuilt only when the shape changes (selection,
// kind, realization state, FK groups, item features, light type), checked on events and twice a second for
// realizations that finish later.
using System;
using System.Collections.Generic;
using System.Text;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;
using UiField = HS2Next.UI.Core.FieldSpec;

namespace HS2Next.UI.Panels
{
    public sealed class InspectorModule : IUiModule
    {
        public int Order => 200;

        public void Register(UiContext ctx)
        {
            InspectorSetup.Install();
            ctx.Registry.Panels.Register("inspector", "panel.inspector", LayoutRegions.Left, c => new InspectorPanel(c), ActionScope.Inspector);
            ctx.Registry.Panels.Register("scene", "panel.scene", LayoutRegions.Right, c => new ScenePanel(c), ActionScope.Inspector);
            CharaEditTab.RegisterActions(ctx);   // Phase 4.1 package W: edit.show
        }
    }

    public sealed class InspectorPanel : VisualElement
    {
        readonly UiContext ctx;
        readonly ScrollView scroll;
        string shape;
        public CharaInspector Chara { get; private set; }
        /// <summary>Rebuilds so far (tests).</summary>
        public int Builds { get; private set; }
        public string PrimaryId { get; private set; }
        public NodeKind? Kind { get; private set; }

        public InspectorPanel(UiContext ctx)
        {
            this.ctx = ctx;
            name = "inspector";
            AddToClassList("hs-inspector");
            style.flexGrow = 1;
            // rows fit the width (v0.2: a row a fraction of a pixel too wide put a horizontal scrollbar under a narrow inspector)
            scroll = new ScrollView(ScrollViewMode.Vertical) { name = "inspector-scroll", horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.style.flexGrow = 1;
            Add(scroll);
            ctx.SelectionChanged += _ => Refresh();
            ctx.SceneChanged += b => Refresh();
            ctx.SceneLoaded += _ => Refresh(true);
            ctx.SceneCleared += () => Refresh(true);
            ctx.SubSelectionChanged += s => Chara?.Highlight(s);
            ctx.SubSelectionChanged += _ => { if (Kind == NodeKind.Route) Refresh(); };   // Phase 3.6 package R: the Route point section
            ctx.ViewStateChanged += w => { if (w == "language") Refresh(true); };
            if (ctx.Runtime != null) ctx.Runtime.CharaBuilt += _ => Refresh();
            schedule.Execute(() => Refresh()).Every(500);
            Refresh(true);
        }

        /// <summary>Rebuilds when the selection's shape changed (or always with <paramref name="force"/>).</summary>
        public void Refresh(bool force = false)
        {
            // building reads realized state (ClothesStatesOf applies pending changes), which can raise SceneChanged /
            // CharaBuilt and so re-enter here: finish this build, then refresh once more
            if (building) { again = true; return; }
            string s = ShapeOf();
            if (!force && s == shape) return;
            shape = s;
            building = true;
            try { Build(); }
            finally { building = false; }
            if (again) { again = false; Refresh(); }
        }

        bool building, again;

        string ShapeOf()
        {
            var sb = new StringBuilder();
            var p = ctx.Selection.Primary;
            var n = p != null ? ctx.Model.Find(p) : null;
            if (n == null) return "";
            sb.Append(n.Kind).Append(':').Append(p).Append('|');
            foreach (var id in ctx.SelectedOfKind(n.Kind)) sb.Append(id).Append(',');
            sb.Append('|').Append(ctx.Selection.Count).Append('|');
            var r = ctx.Runtime?.Find(p);
            sb.Append(r?.State.ToString()).Append('|');
            // the header badge (and the character's MaterialEditor shader line) follow the node's warnings and errors, which a
            // rebuild can change without changing anything else of the shape
            int warnings = 0;
            foreach (var f in ctx.Findings) if (f.NodeId == p && f.Severity >= HS2Next.Save.FindingSeverity.Warning) warnings += Math.Max(1, f.Count);
            sb.Append('w').Append(warnings).Append('|');
            switch (n.Kind)
            {
                case NodeKind.Chara: sb.Append(CharaInspector.ShapeOf(ctx, p)); break;
                case NodeKind.Item:
                {
                    var f = InspectorGates.Features(ctx.Runtime, p);
                    if (f != null) sb.Append(f.FkBones.Count).Append(',').Append(f.Options).Append(',').Append(f.AnimePatterns.Count);
                    if (n.State is HS2Next.Save.ItemNodeState ist) sb.Append(',').Append(ist.Fk.Enabled);
                    break;
                }
                case NodeKind.Light: sb.Append(InspectorGates.TypeOf(ctx.Ops, n)); break;
                case NodeKind.Route: sb.Append(RouteInspector.ShapeOf(ctx, p)); break;   // Phase 3.6 package R
            }
            return sb.ToString();
        }

        void Build()
        {
            Builds++;
            scroll.Clear();
            Chara = null;
            var p = ctx.Selection.Primary;
            var n = p != null ? ctx.Model.Find(p) : null;
            PrimaryId = n?.Id;
            Kind = n?.Kind;
            if (n == null)
            {
                var empty = InspectorUi.Note("inspector.empty");
                empty.AddToClassList("hs-panel__empty");
                scroll.Add(empty);
                return;
            }
            scroll.Add(Header(n));
            if (n.Kind != NodeKind.Folder && n.Kind != NodeKind.Opaque) scroll.Add(TransformSection(n));
            var kindSel = TargetSelector.Selection(n.Kind);
            switch (n.Kind)
            {
                case NodeKind.Chara:
                    Chara = new CharaInspector(ctx, n.Id);
                    scroll.Add(Chara);
                    break;
                case NodeKind.Item: NodeInspectors.Item(ctx, n, kindSel, scroll); break;
                case NodeKind.Light: NodeInspectors.Sections(ctx, InspectorSchemas.Light, kindSel, scroll); break;
                case NodeKind.Camera: NodeInspectors.Sections(ctx, InspectorSchemas.Camera, kindSel, scroll); break;
                case NodeKind.Folder: break;
                case NodeKind.Route: RouteInspector.Build(ctx, n, kindSel, scroll); break;   // Phase 3.6 package R (RouteInspector.cs)
                default: scroll.Add(InspectorUi.Note("inspector.opaque")); break;
            }
            if (ConstraintSection.StandsOn(n.Kind)) scroll.Add(new ConstraintSection(ctx, n.Id));   // Phase 6.5: reads nothing until opened
            // plugin / later sections registered for the kind (5.1)
            foreach (var d in ctx.Registry.SectionsFor(StudioOps.KindName(n)))
            {
                try
                {
                    var fo = InspectorUi.Section(d.TitleKey ?? d.Id);
                    fo.Add(d.Factory(ctx));
                    scroll.Add(fo);
                }
                catch (Exception e) { Debug.LogError("[ui] inspector section " + d.Id + ": " + e); }
            }
        }

        VisualElement Header(TreeNode n)
        {
            var box = new VisualElement { name = "inspector-header" };
            box.AddToClassList("hs-section");
            var kind = Loc.Label("inspector.kind." + StudioOps.KindName(n));
            kind.name = "kind";
            kind.AddToClassList("hs-section__title");
            int same = ctx.SelectedOfKind(n.Kind).Count;
            Label count = null;
            if (ctx.Selection.Count > 1)
            {
                count = new Label(L10n.Format("inspector.selection.count", ctx.Selection.Count, same)) { name = "selection-count" };
                count.AddToClassList(InspectorUi.Dim);
            }
            box.Add(InspectorUi.Line(kind, count));
            var nameSpec = InspectorSchemas.Header.Fields[0];
            box.Add(InspectorUi.Row(ctx, nameSpec, TargetSelector.Primary()));
            var id = new Label(n.Id) { name = "node-id" };
            id.AddToClassList(InspectorUi.Dim);
            id.AddToClassList("hs-inspector__node-id");   // shrinks with an ellipsis (the 32-character id overflowed a narrow inspector)
            id.tooltip = n.Id;
            var copy = InspectorUi.Button("inspector.copyId", () =>
            {
                GUIUtility.systemCopyBuffer = n.Id;
                ctx.Toasts?.Show(L10n.Get("inspector.idCopied"), ToastKind.Info, 2f);
            }, "copy-id");
            int findings = 0;
            foreach (var f in ctx.Findings) if (f.NodeId == n.Id && f.Severity >= HS2Next.Save.FindingSeverity.Warning) findings += Math.Max(1, f.Count);
            Label badge = null;
            if (findings > 0)
            {
                badge = new Label(findings.ToString()) { name = "findings" };
                badge.AddToClassList("hs-row__badge");
                badge.AddToClassList("hs-row__badge--warning");
                Loc.Tooltip(badge, "inspector.findings", findings);
            }
            box.Add(InspectorUi.Line(id, badge, copy));
            return box;
        }

        VisualElement TransformSection(TreeNode n)
        {
            var fo = InspectorUi.Section(InspectorSchemas.Transform.LabelKey, true, "section-transform");
            bool scale = StudioOps.HasScaleChannel(n);
            var sel = TargetSelector.Selection();
            foreach (var f in InspectorSchemas.Transform.Fields)
            {
                if (!scale && (f.Path == "transform.scale" || f.Action == "ops.ResetTransform:scale")) continue;
                fo.Add(InspectorUi.Row(ctx, f, sel));
            }
            return fo;
        }
    }

    /// <summary>The item, light and camera inspectors: foldouts from the schema with the item's expanded rows.</summary>
    public static class NodeInspectors
    {
        public static void Sections(UiContext ctx, IReadOnlyList<SchemaSection> sections, TargetSelector sel, VisualElement into,
            Func<UiField, IEnumerable<VisualElement>> custom = null)
        {
            foreach (var s in sections)
            {
                if (s.Id == InspectorSchemas.ItemMaterialEditSection) continue;   // the item adds it: it needs the node
                var fo = InspectorUi.Section(s.LabelKey, true, "section-" + s.Id);
                InspectorUi.AddSection(ctx, s, sel, fo, custom);
                into.Add(fo);
            }
        }

        public static void Item(UiContext ctx, TreeNode n, TargetSelector sel, VisualElement into)
        {
            var real = ctx.Runtime?.FindItem(n.Id);
            if (real == null || real.State != RealizationState.Built)
                into.Add(InspectorUi.Note("inspector.item.notBuilt", real?.State.ToString() ?? "NotRealized"));
            var feat = InspectorGates.Features(ctx.Runtime, n.Id);
            Sections(ctx, InspectorSchemas.Item, sel, into, f =>
            {
                switch (f.Path)
                {
                    case "item.fk.bones[\"{bone}\"]":
                    {
                        var l = new List<VisualElement>();
                        if (feat != null && n.State is HS2Next.Save.ItemNodeState st && st.Fk.Enabled)
                            foreach (var b in feat.FkBones) l.Add(InspectorUi.Row(ctx, f, TargetSelector.Primary(NodeKind.Item), null, b, b));
                        return l;
                    }
                    case "item.options[{k}]":
                    {
                        var l = new List<VisualElement>();
                        for (int k = 0; feat != null && k < feat.Options; k++)
                            l.Add(InspectorUi.Row(ctx, f, sel, null, k.ToString(), L10n.Format("inspector.item.optionN", k + 1)));
                        return l;
                    }
                    case "item.anime.pattern":
                        return new VisualElement[]
                        {
                            new IndexRow(ctx, f, sel, id => Math.Max(1, InspectorGates.Features(ctx.Runtime, id)?.AnimePatterns.Count ?? 1),
                                (id, i) => { var fe = InspectorGates.Features(ctx.Runtime, id); return fe != null && i < fe.AnimePatterns.Count ? fe.AnimePatterns[i].name : null; }),
                        };
                }
                return null;
            });
            into.Add(MaterialEditView.ItemSection(ctx, n.Id));   // Phase 6.4: the primary item, built when first opened
            if (BakeItemSection.StandsOn(n)) into.Add(new BakeItemSection(ctx, n.Id));   // Phase 6.8: an item drawn from a bake; reads nothing until opened
        }
    }
}

// The scene panel: Map (current map + "Change…" = the map browser, pose fields with the
// vanilla parse rules, option / light toggles gated by the map's blocks), Screen effects (12 blocks with the effects spec's
// ranges and reset buttons; LUT, cubemap and sky by content refs; DOF focus and sun-shaft caster as node pickers with an
// eyedropper: the next node picked in the tree or the viewport), Chara light, Camera slots (ten rows: Save = one entry,
// Load = a view change, a saved / empty mark), Sound (stored; no playback in 3.5) and the Phase 3.6 background / frame
// section (Phase 3.6 package C, SceneImagesSection). Every write goes through StudioOps (InspectorWrites routes the scene paths).
using System;
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine.UIElements;
using UiField = HS2Next.UI.Core.FieldSpec;

namespace HS2Next.UI.Panels
{
    public sealed class ScenePanel : VisualElement
    {
        readonly UiContext ctx;
        readonly ScrollView scroll;
        /// <summary>The node row waiting for an eyedropper pick (null = none).</summary>
        public UiField Picking { get; private set; }
        List<string> pickRestore;

        public ScenePanel(UiContext ctx)
        {
            this.ctx = ctx;
            name = "scene-panel";
            style.flexGrow = 1;
            scroll = new ScrollView(ScrollViewMode.Vertical) { name = "scene-scroll" };
            scroll.style.flexGrow = 1;
            Add(scroll);
            ctx.SelectionChanged += OnSelection;
            ctx.ViewStateChanged += w => { if (w == "language") Build(); };
            var map = ctx.Runtime?.GetPart<MapRealization>();
            if (map != null) map.StateChanged += _ => schedule.Execute(() => MarkAll()).ExecuteLater(1);
            Build();
        }

        void Build()
        {
            scroll.Clear();
            var sections = InspectorSchemas.Scene;
            var sel = TargetSelector.Scene;
            VisualElement effects = null;
            foreach (var s in sections)
            {
                if (s.Id.StartsWith("effects.", StringComparison.Ordinal))
                {
                    if (effects == null)
                    {
                        effects = InspectorUi.Section("inspector.scene.section.effects", true, "section-effects");
                        scroll.Add(effects);
                    }
                    var block = InspectorUi.Section(s.LabelKey, false, "section-" + s.Id);
                    InspectorUi.AddSection(ctx, s, sel, block, Custom);
                    effects.Add(block);
                    continue;
                }
                if (s.Id == InspectorSchemas.MapMaterialsSection)
                {
                    scroll.Add(MaterialEditView.MapSection(ctx));   // Phase 6.4: built when first opened
                    continue;
                }
                var fo = InspectorUi.Section(s.LabelKey, true, "section-" + s.Id);
                if (s.Id == "cameraSlots") CameraSlots(fo, s);
                else
                {
                    if (s.Id == "map") fo.Add(MapHeader());
                    InspectorUi.AddSection(ctx, s, sel, fo, Custom);
                }
                scroll.Add(fo);
            }
            scroll.Add(SceneImagesSection.Build(ctx));   // Phase 3.6 package C: Background / frame (SceneImagesSection.cs)
            scroll.Add(SceneRoutesSection.Build(ctx));   // Phase 3.6 package R: the route list (SceneRoutesSection.cs)
        }

        void MarkAll() { foreach (var r in this.Query<FieldRow>().ToList()) r.Binding?.MarkDirty(); }

        IEnumerable<VisualElement> Custom(UiField f)
        {
            if (f.Kind == FieldKind.Node)
            {
                var row = InspectorUi.Row(ctx, f, TargetSelector.Scene);
                var pick = InspectorUi.Button("inspector.scene.pickNode", () => BeginPick(f), "pick");
                Loc.Tooltip(pick, "inspector.scene.pickNode.hint");
                row.Add(pick);
                return new[] { row };
            }
            return null;
        }

        VisualElement MapHeader()
        {
            var current = new Label { name = "map-name" };
            current.AddToClassList("hs-row__label");
            current.style.flexGrow = 1;
            ctx.Bindings.Bind(current, TargetSelector.Scene, "map.ref", (el, v) =>
            {
                string r = v.Value as string;
                string label = r == null ? L10n.Get("inspector.scene.map.none") : r;
                if (r != null && ContentIdName(r, out var nm)) label = nm;
                ((Label)el).text = label;
            });
            var change = InspectorUi.Button("inspector.scene.map.change", () => ctx.ShowResult(InspectorFlows.BrowseMap(ctx)), "map-change");
            return InspectorUi.Line(current, change);
        }

        bool ContentIdName(string r, out string name)
        {
            name = null;
            var db = ctx.App?.Db;
            if (db == null || !HS2Next.Content.ContentId.TryParse(r, out var key)) return false;
            foreach (var m in db.Maps.All) if (m.Key == key) { name = m.Name; return true; }
            return false;
        }

        void CameraSlots(VisualElement into, SchemaSection s)
        {
            UiField save = null, load = null;
            foreach (var f in s.Fields)
            {
                if (f.Action != null && f.Action.StartsWith("ops.SaveCameraSlot", StringComparison.Ordinal)) save = f;
                if (f.Action != null && f.Action.StartsWith("ops.LoadCameraSlot", StringComparison.Ordinal)) load = f;
            }
            for (int k = 0; k < CameraState.SlotCount; k++)
            {
                string arg = k.ToString();
                int slot = k;
                var label = new Label(L10n.Format("inspector.scene.cameraSlots.slot", k + 1)) { name = "slot-label" };
                label.AddToClassList("hs-row__label");
                var mark = new Label { name = "slot-mark" };
                mark.AddToClassList(InspectorUi.Dim);
                mark.style.flexGrow = 1;
                ctx.Bindings.Bind(mark, TargetSelector.Scene, "camera.slots", (el, v) =>
                {
                    var list = v.Value as List<CameraView>;
                    bool saved = list != null && slot < list.Count && list[slot] != null;
                    ((Label)el).text = L10n.Get(saved ? "inspector.scene.cameraSlots.saved" : "inspector.scene.cameraSlots.empty");
                });
                var bs = new Button(() => InspectorWrites.RunButton(ctx, save, arg, null)) { name = "save" };
                Loc.Bind(bs, save?.LabelKey ?? "inspector.scene.cameraSlots.save");
                var bl = new Button(() => InspectorWrites.RunButton(ctx, load, arg, null)) { name = "load" };
                Loc.Bind(bl, load?.LabelKey ?? "inspector.scene.cameraSlots.load");
                var row = InspectorUi.Line(label, mark, bs, bl);
                row.name = "camera-slot-" + k;
                into.Add(row);
            }
        }

        // ------------------------------------------------------------ eyedropper for node rows

        /// <summary>The next node selected in the tree or the viewport becomes the row's value; the selection is then restored.</summary>
        public void BeginPick(UiField f)
        {
            Picking = f;
            pickRestore = new List<string>(ctx.Selection.Ids);
            ctx.Toasts?.Show(L10n.Get("inspector.scene.pickNode.hint"), ToastKind.Info);
        }

        void OnSelection(SelectionChangedArgs a)
        {
            if (Picking == null) return;
            var f = Picking;
            var id = ctx.Selection.Primary;
            if (id == null) return;
            Picking = null;
            var restore = pickRestore ?? new List<string>();
            pickRestore = null;
            // the selection event is delivered while the bus is busy: write and restore on the next panel update
            schedule.Execute(() =>
            {
                InspectorWrites.Write(ctx, f, null, null, id);
                ctx.Ops.Select(restore, false, null);   // Picking is null: this change is not a pick
            }).ExecuteLater(0);
        }
    }
}

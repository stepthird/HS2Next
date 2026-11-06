// The Blend shapes section of the Advanced pose tab: one renderer's shapes as a virtualised list of sliders (0..100, typed -200..300). A row
// shows the override, or the current weight while there is none (it moves with the face). A renderer that keeps the mosaic in place is
// not offered.
using System;
using System.Collections.Generic;
using HS2Next.Save;
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
    public sealed partial class AdvancedPoseTab
    {
        const float BlendRowHeight = 26f, BlendListHeight = 260f;

        /// <summary>One shape of the current renderer.</summary>
        public readonly struct BlendShapeRow
        {
            public readonly string Name;
            public readonly int Index;
            public BlendShapeRow(string name, int index) { Name = name; Index = index; }
        }

        readonly List<PoseRendererRow> rendererRows = new List<PoseRendererRow>();
        readonly List<BlendShapeRow> shapes = new List<BlendShapeRow>();
        DropdownField rendererField;
        TextField blendSearch;
        ListView blendList;
        Button blendClear;
        StaleList blendStale;
        SkinnedMeshRenderer currentRenderer;
        readonly List<UiEdit> blendEdits = new List<UiEdit>();

        /// <summary>The address of the renderer shown, or null.</summary>
        public string SelectedRenderer => view.Renderer;
        public IReadOnlyList<PoseRendererRow> RendererRows => rendererRows;
        public IReadOnlyList<BlendShapeRow> ShapeRows => shapes;
        public ListView BlendList => blendList;
        public StaleList BlendStale => blendStale;

        void BuildBlend(VisualElement into)
        {
            rendererField = new DropdownField(new List<string>(), 0) { name = "adv-blend-renderer" };
            Loc.Bind(rendererField, "inspector.chara.adv.blendRenderer", (e, s) => ((DropdownField)e).label = s);
            rendererField.RegisterValueChangedCallback(e =>
            {
                int i = rendererField.index;
                if (i >= 0 && i < rendererRows.Count && rendererRows[i].Address != view.Renderer) SetRenderer(rendererRows[i].Address);
            });
            into.Add(rendererField);
            blendSearch = PanelKit.SearchField("adv-blend-search", SetBlendQuery);
            blendSearch.SetValueWithoutNotify(view.BlendQuery);
            into.Add(blendSearch);
            blendList = new ListView
            {
                name = "adv-blend-list", fixedItemHeight = BlendRowHeight, virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.None, itemsSource = shapes, makeItem = () => new BlendRow(this), bindItem = (e, i) => { if (i >= 0 && i < shapes.Count) ((BlendRow)e).Bind(shapes[i]); },
            };
            blendList.style.height = BlendListHeight;
            into.Add(blendList);
            blendClear = InspectorUi.Button("inspector.chara.adv.resetAll", () => ctx.ShowResult(ctx.Ops.ClearBlendWeights(Primary, null, null, UiContext.User(OpLabels.ClearBlendShapes))), "adv-blend-clear");
            into.Add(blendClear);
            blendStale = new StaleList("adv-blend-stale", RemoveStaleBlend);
            into.Add(blendStale);
        }

        void BlendStructure(CharaRealization r)
        {
            if (blendList == null) return;
            rendererRows.Clear();
            var labels = new List<string>();
            if (r != null)
                foreach (var row in AdvancedPoseParts.Renderers(r))
                {
                    if (AdvancedPoseGuard.Renderer(r, row.Renderer)) continue;
                    rendererRows.Add(row);
                    labels.Add(PartLabel(r, row.Part) + " / " + row.Name);
                }
            rendererField.choices = labels;
            if (rendererRows.FindIndex(x => x.Address == view.Renderer) < 0) view.Renderer = rendererRows.Count > 0 ? rendererRows[0].Address : null;
            SetRenderer(view.Renderer);
        }

        /// <summary>Shows the shapes of a renderer of this character (null: none).</summary>
        public void SetRenderer(string address)
        {
            view.Renderer = address;
            int i = rendererRows.FindIndex(x => x.Address == address);
            if (rendererField != null) rendererField.SetValueWithoutNotify(i >= 0 && i < rendererField.choices.Count ? rendererField.choices[i] : "");
            currentRenderer = i >= 0 ? rendererRows[i].Renderer : null;
            ReadShapes();
        }

        /// <summary>Searches the renderer's shapes by name.</summary>
        public void SetBlendQuery(string q)
        {
            view.BlendQuery = q ?? "";
            ReadShapes();
        }

        void ReadShapes()
        {
            shapes.Clear();
            var mesh = currentRenderer ? currentRenderer.sharedMesh : null;
            if (mesh)
            {
                var q = SearchQuery.Parse(view.BlendQuery);
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    string n = mesh.GetBlendShapeName(i);
                    if (q.IsEmpty || q.Matches(SearchText.Normalize(n))) shapes.Add(new BlendShapeRow(n, i));
                }
            }
            blendList?.RefreshItems();
        }

        internal float? BlendOverride(string shape)
        {
            var b = Block;
            return view.Renderer != null && b != null && b.BlendShapes.TryGetValue(view.Renderer, out var v) && v.Weights.TryGetValue(shape, out float w) ? w : (float?)null;
        }

        internal float BlendCurrent(int index)
        {
            var mesh = currentRenderer ? currentRenderer.sharedMesh : null;
            return mesh && index < mesh.blendShapeCount ? currentRenderer.GetBlendShapeWeight(index) : 0f;
        }

        void BlendBlockChanged(CharaRealization r)
        {
            if (blendList == null) return;
            var block = Block;
            blendClear.SetEnabled(block != null && block.BlendShapes.Count > 0);
            var c = AdvancedPoseDriver.Of(r);
            var stale = new List<string>();
            if (c != null)
            {
                stale.AddRange(c.StaleRenderers);
                stale.AddRange(c.StaleShapes);
            }
            blendStale.Set(stale);
            if (ctx.ActiveEdit == null) blendList.RefreshItems();
        }

        void BlendRefresh(CharaRealization r)
        {
            if (blendList == null) return;
            BlendBlockChanged(r);
        }

        // a stored renderer (its key as it stands) or "<renderer>:<shape>" of a renderer that exists without that shape
        void RemoveStaleBlend(string key)
        {
            var block = Block;
            if (block == null) return;
            var label = UiContext.User(OpLabels.ClearBlendShapes);
            if (block.BlendShapes.ContainsKey(key)) { ctx.ShowResult(ctx.Ops.ClearBlendWeights(Primary, key, null, label)); return; }
            foreach (var kv in block.BlendShapes)
            {
                if (!key.StartsWith(kv.Key + ":", StringComparison.Ordinal)) continue;
                string shape = key.Substring(kv.Key.Length + 1);
                if (kv.Value.Weights.ContainsKey(shape)) { ctx.ShowResult(ctx.Ops.ClearBlendWeights(Primary, kv.Key, shape, label)); return; }
            }
        }

        void BlendEndEdits()
        {
            for (int i = blendEdits.Count - 1; i >= 0; i--) blendEdits[i]?.Cancel();
            blendEdits.Clear();
        }

        internal void BlendEditOpened(UiEdit e) { blendEdits.Clear(); blendEdits.Add(e); }
        internal void BlendEditClosed() => blendEdits.Clear();
    }

    /// <summary>One shape: a slider with the current weight; the reset (it shows while the shape has an override) takes the override away.</summary>
    public sealed class BlendRow : VisualElement
    {
        readonly AdvancedPoseTab tab;
        readonly UiField spec;
        AdvancedPoseTab.BlendShapeRow shape;
        UiEdit edit;
        readonly Label mark;

        public readonly NumberSlider Slider;
        public string Shape => shape.Name;
        public OpResult LastResult { get; private set; }

        internal BlendRow(AdvancedPoseTab tab)
        {
            this.tab = tab;
            spec = AdvancedPoseTab.Row(InspectorSchemas.AdvBlendWeight);
            AddToClassList("hs-field");
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            Slider = new NumberSlider("", spec.Min, spec.Max) { TypedMin = spec.TypedMin, TypedMax = spec.TypedMax, Format = "0.##" };
            Slider.style.flexGrow = 1;
            Slider.style.flexShrink = 1;
            Slider.DefaultValue = () => tab.BlendOverride(shape.Name).HasValue ? (double?)tab.BlendOverride(shape.Name).Value : null;
            Slider.Edited += OnEdited;
            Add(Slider);
            mark = new Label("●") { name = "adv-blend-mark" };
            mark.AddToClassList("hs-row__badge");
            Add(mark);
        }

        internal void Bind(AdvancedPoseTab.BlendShapeRow row)
        {
            shape = row;
            name = "adv-blend-row-" + row.Name;
            Slider.LabelElement.text = row.Name;
            Slider.LabelElement.tooltip = row.Name;
            Show();
        }

        void Show()
        {
            if (Slider.Dragging) return;
            float? over = tab.BlendOverride(shape.Name);
            Slider.SetDisplay(over ?? tab.BlendCurrent(shape.Index), false);
            mark.style.display = over.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void OnEdited(EditPhase phase, double v)
        {
            var ctx = tab.Ctx;
            string address = tab.SelectedRenderer;
            if (address == null) return;
            switch (phase)
            {
                case EditPhase.Begin:
                    edit?.Cancel();
                    edit = ctx.BeginEdit(OpLabels.BlendShapes);
                    tab.BlendEditOpened(edit);
                    return;
                case EditPhase.End:
                {
                    var e = edit;
                    edit = null;
                    tab.BlendEditClosed();
                    if (e != null) LastResult = ctx.ShowResult(e.Commit());
                    Show();
                    tab.BlockChanged();
                    return;
                }
                case EditPhase.Preview:
                {
                    if (edit == null || !edit.IsOpen) return;   // Esc took the drag back: its last moves write nothing
                    var block = tab.Block;
                    var set = new[] { new KeyValuePair<string, float>(shape.Name, (float)spec.ClampTyped(v)) };
                    bool inLimit = block != null && CanStep(block, address);
                    LastResult = inLimit
                        ? tab.DragWrite(AdvancedPoseEdits.SetWeights(block, address, set), OpLabels.BlendShapes)
                        : ctx.ShowResult(ctx.Ops.SetBlendWeights(tab.Primary, address, set, null, false, UiContext.User(OpLabels.BlendShapes)));
                    mark.style.display = DisplayStyle.Flex;
                    return;
                }
                case EditPhase.Typed:
                {
                    var set = new[] { new KeyValuePair<string, float>(shape.Name, (float)spec.ClampTyped(v)) };
                    LastResult = ctx.ShowResult(ctx.Ops.SetBlendWeights(tab.Primary, address, set, null, false, UiContext.User(OpLabels.BlendShapes, "ui.typed/" + tab.Primary + "/" + address + "/" + shape.Name)));
                    tab.BlockChanged();
                    return;
                }
                default:   // the reset button, a double click on the slider
                    LastResult = ctx.ShowResult(ctx.Ops.ClearBlendWeights(tab.Primary, address, shape.Name, UiContext.User(OpLabels.ClearBlendShapes)));
                    tab.BlockChanged();
                    return;
            }
        }

        bool CanStep(CharaAdvancedPose block, string address)
        {
            bool has = block.BlendShapes.TryGetValue(address, out var v);
            if (has && v.Weights.ContainsKey(shape.Name)) return true;
            if (!has && block.BlendShapes.Count >= AdvancedPoseLimits.MaxRenderers) return false;
            int weights = 0;
            foreach (var b in block.BlendShapes.Values) weights += b.Weights.Count;
            return weights < AdvancedPoseLimits.MaxWeights;
        }
    }
}

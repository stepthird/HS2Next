// The Materials view, below the list: the selected material and the items that do not apply. The rows come from the current shader of the
// live material, so a card's items line up by name without a table: a property with HideInInspector is left out unless an item names
// it, and an item on a property the shader lacks has no row: it stands in Not applied. Rows exist only for the selected material.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed partial class MaterialEditView
    {
        /// <summary>The item properties that are also an item's colour slots: an edit draws over the slot.</summary>
        static readonly string[] ColourSlots = { "Color", "Color2", "Color3", "Color4" };

        VisualElement selectedBox, propsBox, keywordsBox, topBox;
        Label nameLabel, offNote, keywordsTitle;
        string rowsKey;
        MaterialSelection selection;
        readonly List<MaterialRow> valueRows = new List<MaterialRow>();

        Foldout notAppliedFold;
        VisualElement notAppliedList;
        bool statusDirty = true;
        readonly List<(MaterialOverride item, MaterialItemState state)> stale = new List<(MaterialOverride, MaterialItemState)>();

        /// <summary>The selected material's name, or null.</summary>
        public string SelectedMaterial => state.Material;
        /// <summary>The rows of the selected material: shader, queue, keywords, properties.</summary>
        public IReadOnlyList<MaterialRow> ValueRows => valueRows;
        /// <summary>The row of a stored property name, or null.</summary>
        public MaterialRow RowOf(string property) => valueRows.Find(r => r.Property == property);
        /// <summary>The items that do not apply, with why.</summary>
        public IReadOnlyList<(MaterialOverride item, MaterialItemState state)> Stale => stale;
        /// <summary>What the rows show.</summary>
        public MaterialSelection Selection => selection;

        void BuildSelected()
        {
            selectedBox = new VisualElement { name = "mat-selected" };
            nameLabel = new Label { name = "mat-name" };
            nameLabel.AddToClassList("hs-section__title");
            nameLabel.style.overflow = Overflow.Hidden;
            nameLabel.style.textOverflow = TextOverflow.Ellipsis;
            selectedBox.Add(nameLabel);
            offNote = InspectorUi.Note("inspector.materials.mosaic");
            offNote.name = "mat-off";
            offNote.style.whiteSpace = WhiteSpace.Normal;
            selectedBox.Add(offNote);
            topBox = new VisualElement { name = "mat-top" };
            selectedBox.Add(topBox);
            keywordsTitle = Loc.Label("inspector.materials.keywords");
            keywordsTitle.AddToClassList("hs-field__label");
            keywordsTitle.name = "mat-keywords-title";
            selectedBox.Add(keywordsTitle);
            keywordsBox = new VisualElement { name = "mat-keywords" };
            selectedBox.Add(keywordsBox);
            propsBox = new VisualElement { name = "mat-props" };
            selectedBox.Add(propsBox);
            Add(selectedBox);
        }

        /// <summary>Shows a material's rows (null: none).</summary>
        public void SelectMaterial(string material)
        {
            if (material == state.Material && rowsKey != null) return;
            EndEdits();
            state.Material = material;
            rowsKey = null;
            ShowSelected();
            foreach (var l in lines) l.Show();
        }

        // Builds the rows when the name or its shader changed, then shows the live values.
        void ShowSelected()
        {
            if (selectedBox == null) return;
            selection = state.Material != null && Target.Ready ? Target.Select(state.Material) : null;
            var shown = selection?.Shown;
            selectedBox.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (!shown)
            {
                rowsKey = null;
                ClearRows();
                return;
            }
            string key = state.Material + "|" + (shown.shader ? shown.shader.name : "") + "|" + selection.Protected;
            if (key != rowsKey)
            {
                rowsKey = key;
                BuildRows(shown);
            }
            nameLabel.text = state.Material + "  (" + L10n.Format("inspector.materials.slots", selection.Slots) + ")";
            nameLabel.tooltip = state.Material;
            var array = Array;
            foreach (var r in valueRows) r.Show(selection, MaterialEdits.Find(array, r.Key));
        }

        void ClearRows()
        {
            foreach (var r in valueRows) r.CancelEdit();
            valueRows.Clear();
            topBox.Clear();
            keywordsBox.Clear();
            propsBox.Clear();
        }

        void BuildRows(Material m)
        {
            ClearRows();
            string material = state.Material;
            bool off = selection.Protected;
            offNote.style.display = off ? DisplayStyle.Flex : DisplayStyle.None;
            var sh = m.shader;
            AddRow(topBox, new MaterialShaderRow(this, material), off);
            AddRow(topBox, new MaterialQueueRow(this, material), off);
            var keywords = sh && MaterialShaders.IsDirectName(sh.name) ? sh.keywordSpace.keywords : null;
            int shownKeywords = 0;
            if (keywords != null)
                foreach (var k in keywords)
                {
                    if (!k.name.StartsWith("_", StringComparison.Ordinal)) continue;
                    AddRow(keywordsBox, new MaterialKeywordRow(this, material, k.name.Substring(1)), off);
                    shownKeywords++;
                }
            keywordsTitle.style.display = shownKeywords > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (!sh) return;
            var named = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in Array)
                if (item is MeMaterialProperty p && p.Material == material && p.Property != null) named.Add(p.Property);
            for (int i = 0; i < sh.GetPropertyCount(); i++)
            {
                string prop = sh.GetPropertyName(i);
                if (!prop.StartsWith("_", StringComparison.Ordinal)) continue;
                string stored = prop.Substring(1);
                var flags = sh.GetPropertyFlags(i);
                if ((flags & ShaderPropertyFlags.HideInInspector) != 0 && !named.Contains(stored)) continue;
                // a build reaches a character's material before the URP mapping: what only the replacement declares is out of an item's reach
                if (place == Place.Character && !MaterialWrites.Declared(m, prop)) continue;
                var row = MakeRow(sh, i, m, material, stored, prop, flags);
                if (row == null) continue;
                AddRow(propsBox, row, off);
                if (!off && MaterialWrites.FallbackFixed(m, prop)) row.Off("inspector.materials.fallback");
            }
        }

        MaterialRow MakeRow(Shader sh, int i, Material m, string material, string stored, string prop, ShaderPropertyFlags flags)
        {
            switch (sh.GetPropertyType(i))
            {
                case ShaderPropertyType.Range:
                {
                    var limits = sh.GetPropertyRangeLimits(i);
                    return new MaterialFloatRow(this, material, stored, limits.x, limits.y, false);
                }
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Int:
                {
                    MaterialBaseline.TryRead(m, MaterialMember.Float, prop, out var v);
                    return new MaterialFloatRow(this, material, stored, 0, Math.Max(1f, 2f * Math.Abs(v.Float)), sh.GetPropertyType(i) == ShaderPropertyType.Int);
                }
                case ShaderPropertyType.Color:
                    return new MaterialColorRow(this, material, stored, (flags & ShaderPropertyFlags.HDR) != 0);
                case ShaderPropertyType.Texture:
                    if (sh.GetPropertyTextureDimension(i) != TextureDimension.Tex2D) return new MaterialReadOnlyRow(this, material, stored, ShaderPropertyType.Texture);
                    return new MaterialTextureRow(this, material, stored, (flags & ShaderPropertyFlags.NoScaleOffset) == 0);
                case ShaderPropertyType.Vector:
                    return new MaterialReadOnlyRow(this, material, stored, ShaderPropertyType.Vector);
                default:
                    return null;
            }
        }

        void AddRow(VisualElement into, MaterialRow row, bool off)
        {
            valueRows.Add(row);
            into.Add(row);
            if (off) row.Off("inspector.materials.mosaic");
        }

        // ------------------------------------------------------------ not applied

        void BuildNotApplied()
        {
            notAppliedFold = InspectorUi.Section(null, state.NotAppliedOpen, "mat-notapplied");
            notAppliedFold.RegisterValueChangedCallback(e =>
            {
                if (e.target != notAppliedFold) return;
                state.NotAppliedOpen = e.newValue;
                if (e.newValue) FillNotApplied();
            });
            notAppliedFold.style.display = DisplayStyle.None;
            Add(notAppliedFold);
        }

        // What each item of the array does now. Reads only, and only after the array or the parts changed.
        void ReadStatus()
        {
            if (!statusDirty || !Target.Ready) return;
            statusDirty = false;
            stale.Clear();
            var array = Array;
            bool over = false;
            int editable = 0;
            if (array.Count > 0)
            {
                var read = ctx.Ops.MaterialStatusRead(Target.Address, out _);
                foreach (var item in array)
                {
                    if (MaterialEdits.IsEditable(item)) editable++;
                    if (read == null) continue;
                    var s = read(item);
                    if (!s.Applied) stale.Add((item, s));
                    else if (item is MeColor c && System.Array.IndexOf(ColourSlots, c.Property) >= 0) over = true;
                }
            }
            resetAll.SetEnabled(editable > 0);
            if (overColour != null) overColour.style.display = over ? DisplayStyle.Flex : DisplayStyle.None;
            notAppliedFold.text = L10n.Format("inspector.materials.notApplied", stale.Count);
            notAppliedFold.style.display = stale.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (notAppliedFold.value) FillNotApplied();
        }

        /// <summary>The text of a status, in the current language.</summary>
        public static string Why(MaterialItemState s)
        {
            if (s.Status == MaterialStatus.MosaicProtected) return L10n.Get("inspector.materials.mosaic");
            if (s.Note == MaterialStatus.DrawsBody) return L10n.Get("inspector.materials.drawsBody");
            return L10n.Get("inspector.materials.why." + s.Status);
        }

        static string NameOf(MaterialOverride item) => MeKey.TryOf(item, out var k) ? k.ToString() : item.Type;

        void FillNotApplied()
        {
            if (notAppliedList == null)
            {
                notAppliedList = new VisualElement { name = "mat-notapplied-list" };
                notAppliedFold.Add(notAppliedList);
            }
            notAppliedList.Clear();
            for (int i = 0; i < stale.Count; i++)
            {
                var (item, s) = stale[i];
                var what = new Label(NameOf(item)) { name = "mat-notapplied-what", tooltip = NameOf(item) };
                what.AddToClassList("hs-row__label");
                what.style.overflow = Overflow.Hidden;
                what.style.textOverflow = TextOverflow.Ellipsis;
                var why = new Label(Why(s)) { name = "mat-notapplied-why" };
                why.AddToClassList(InspectorUi.Dim);
                why.style.whiteSpace = WhiteSpace.Normal;
                var text = new VisualElement();
                text.style.flexGrow = 1;
                text.style.flexShrink = 1;
                text.Add(what);
                text.Add(why);
                Button remove = null;
                if (MaterialEdits.IsEditable(item))
                {
                    var target = item;
                    remove = InspectorUi.Button("inspector.materials.remove", () => RemoveItem(target), "mat-notapplied-remove-" + i);
                }
                var line = InspectorUi.Line(text, remove);
                line.name = "mat-notapplied-" + i;
                notAppliedList.Add(line);
            }
        }

        /// <summary>Takes one stored item away (one entry); a read-only item stays.</summary>
        public OpResult RemoveItem(MaterialOverride item)
        {
            if (!MeKey.TryOf(item, out var key)) return OpResult.NothingToDo();
            var r = ctx.ShowResult(ctx.Ops.ClearMaterial(Target.Address, key, null, UiContext.User(OpLabels.MaterialClear)));
            ArrayChanged();
            return r;
        }
    }
}

// What the Materials view works on: one materialOverrides array, the renderers and materials it can name, and how to reach them. Three
// adapters, one per place the view stands in: a character with its hair, clothes and accessories (the target selector picks the array),
// a Studio item, the loaded map. Reads only: a value reaches the array through StudioOps, and nothing here copies a material. Constructing
// an adapter reads nothing of the character, item or map.
using System;
using System.Collections.Generic;
using System.Text;
using HS2Next.Chara;
using HS2Next.Chara.Ext;
using HS2Next.Chara.Mosaic;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using UnityEngine;
using OT = HS2Next.Data.Ext.MaterialEditorData.ObjectType;

namespace HS2Next.UI.Panels
{
    /// <summary>One renderer of a target, as the list shows it.</summary>
    public sealed class MaterialRendererEntry
    {
        public Renderer Renderer;
        /// <summary>What a `renderer` item calls it: the name on a character, the path on an item or the map.</summary>
        public string Key;
        /// <summary>A mosaic member: its rows are off.</summary>
        public bool Member;
        /// <summary>It draws the body's materials and has none of its own.</summary>
        public bool DrawsBody;
        /// <summary>Its materials by formatted name, each once.</summary>
        public readonly List<string> Materials = new List<string>();
    }

    /// <summary>A material name of a target, with its renderer slots.</summary>
    public sealed class MaterialNameEntry
    {
        public string Name, Shader;
        public int Slots;
        /// <summary>Only mosaic materials hold this name. A member's hold is asked when the name is selected: it takes a walk of every renderer.</summary>
        public bool Protected;
    }

    /// <summary>What a selected name reaches now.</summary>
    public sealed class MaterialSelection
    {
        public string Name;
        /// <summary>The materials of that name an edit acts on.</summary>
        public readonly List<Material> Editable = new List<Material>();
        /// <summary>A material of that name no edit acts on (a member's or a mosaic material), or null.</summary>
        public Material Held;
        /// <summary>The renderer slots that draw the name.</summary>
        public int Slots;
        /// <summary>Renderers that draw the body's materials hold the name, and no other does.</summary>
        public bool DrawsBody;
        /// <summary>The material whose values the rows show.</summary>
        public Material Shown => Editable.Count > 0 ? Editable[0] : Held;
        /// <summary>Only a member or the mosaic holds it: every row is off.</summary>
        public bool Protected => Editable.Count == 0 && Held;
    }

    /// <summary>One array and what it can name.</summary>
    public interface IMaterialTarget
    {
        /// <summary>The array in the operations' terms.</summary>
        MaterialEditTarget Address { get; }
        /// <summary>Whom the bindings of the array ask: the node, or the scene.</summary>
        TargetSelector Selector { get; }
        /// <summary>The owner of the view's state and of the typed merge keys: a node's id, or "map".</summary>
        string Owner { get; }
        /// <summary>The first level of the list is materials, not renderers (the map).</summary>
        bool MaterialsFirst { get; }
        /// <summary>Built and readable now.</summary>
        bool Ready { get; }
        /// <summary>A text that changes when the renderers do; "" when not ready.</summary>
        string Structure { get; }
        /// <summary>The renderers of the array's object.</summary>
        void FillRenderers(List<MaterialRendererEntry> into);
        /// <summary>The materials of the array's object by name.</summary>
        void FillMaterials(List<MaterialNameEntry> into);
        /// <summary>What a material name reaches now.</summary>
        MaterialSelection Select(string material);
    }

    /// <summary>A character's arrays; <see cref="Show"/> picks the one the view edits.</summary>
    public sealed class CharaMaterialTarget : IMaterialTarget
    {
        readonly UiContext ctx;

        public CharaMaterialTarget(UiContext ctx, string node)
        {
            this.ctx = ctx;
            Owner = node;
            Selector = TargetSelector.Node(node);
            Address = MaterialEditTarget.Character(node);
        }

        public MaterialEditTarget Address { get; private set; }
        public TargetSelector Selector { get; }
        public string Owner { get; }
        public bool MaterialsFirst => false;

        /// <summary>Moves to the array a target's text names; false when the text names none.</summary>
        public bool Show(string text)
        {
            if (!MaterialEditTarget.TryParse(Owner, text, out var t, out _)) return false;
            Address = t;
            return true;
        }

        CharaRealization Real
        {
            get
            {
                var r = ctx.Runtime?.FindChara(Owner);
                return r != null && r.State == RealizationState.Built && !r.InGraveyard && r.Root ? r : null;
            }
        }

        public bool Ready => Real != null;

        public string Structure
        {
            get
            {
                var r = Real;
                var cc = r?.Context;
                if (cc == null) return "";
                var sb = new StringBuilder();
                sb.Append(r.Root.GetHashCode()).Append('|').Append(cc.Head ? cc.Head.GetHashCode() : 0).Append('|');
                foreach (var h in cc.Hair) sb.Append(h ? h.GetHashCode() : 0).Append(',');
                sb.Append('|');
                foreach (var c in cc.Clothes) sb.Append(c ? c.GetHashCode() : 0).Append(',');
                sb.Append('|');
                if (cc.Accessories != null)
                    foreach (var s in cc.Accessories) sb.Append(s != null && s.Object ? s.Object.GetHashCode() : 0).Append(',');
                return sb.ToString();
            }
        }

        // The object of the array on the built character; false when it has none.
        bool TryObject(CharaRealization r, MaterialEditTarget t, out (OT, int) obj)
        {
            obj = (OT.Character, 0);
            switch (t.Kind)
            {
                case MaterialTargetKind.Hair: obj = (OT.Hair, t.Slot); return true;
                case MaterialTargetKind.Clothes: obj = (OT.Clothing, t.Slot); return true;
                case MaterialTargetKind.Accessory:
                    foreach (var s in r.Context.Accessories)
                        if (s != null && r.AccessoryIdOf(s) == t.AccessoryId) { obj = (OT.Accessory, s.Slot); return true; }
                    return false;
                default: return true;
            }
        }

        List<Renderer> RenderersOf(CharaRealization r, MaterialEditTarget t) =>
            r != null && TryObject(r, t, out var obj) ? MaterialEditorApplier.ObjectRenderers(r.Context, obj) : null;

        /// <summary>The arrays this character shows: each with renderers now, or with items stored. (text, label).</summary>
        public List<(string text, string label)> Choices()
        {
            var l = new List<(string, string)>();
            var r = Real;
            foreach (var t in ctx.Ops.MaterialTargetsOf(Owner))
            {
                if (t.Kind != MaterialTargetKind.Character && RenderersOf(r, t) == null && ctx.Ops.MaterialArray(t).Count == 0) continue;
                string label = t.Kind == MaterialTargetKind.Character ? L10n.Get("inspector.materials.character") : AdvancedPoseTab.PartLabel(r, t.Text);
                l.Add((t.Text, label));
            }
            return l;
        }

        public void FillRenderers(List<MaterialRendererEntry> into)
        {
            var r = Real;
            var rends = RenderersOf(r, Address);
            if (rends == null) return;
            var cc = r.Context;
            foreach (var x in rends)
            {
                if (!x) continue;
                var e = new MaterialRendererEntry
                {
                    Renderer = x, Key = MeKey.Format(x.name), Member = MaterialGuard.Renderer(x), DrawsBody = cc.DrawsBodyMaterials(x) || cc.DrawsBodySkin(x),
                };
                MaterialTargetKit.NamesOf(x, e.Materials);
                into.Add(e);
            }
        }

        public void FillMaterials(List<MaterialNameEntry> into) { }

        public MaterialSelection Select(string material)
        {
            var s = new MaterialSelection { Name = material };
            var r = Real;
            var rends = RenderersOf(r, Address);
            if (rends == null) return s;
            var cc = r.Context;
            bool body = false;
            foreach (var x in rends)
            {
                if (!x) continue;
                foreach (var m in x.sharedMaterials)
                {
                    if (!m || MeKey.Format(m.name) != material) continue;
                    s.Slots++;
                    if (MaterialGuard.Renderer(x) || MaterialGuard.Material(m)) { if (!s.Held) s.Held = m; }
                    else if (cc.DrawsBodyMaterials(x) || cc.DrawsBodySkin(x)) body = true;
                    else if (!s.Editable.Contains(m)) s.Editable.Add(m);
                }
            }
            s.DrawsBody = body && s.Editable.Count == 0;
            return s;
        }
    }

    /// <summary>What an item and the map share: a <see cref="MaterialTargets"/> names everything.</summary>
    public abstract class IndexedMaterialTarget : IMaterialTarget
    {
        public abstract MaterialEditTarget Address { get; }
        public abstract TargetSelector Selector { get; }
        public abstract string Owner { get; }
        public abstract bool MaterialsFirst { get; }
        /// <summary>The index of the built target, or null.</summary>
        public abstract MaterialTargets Index { get; }

        public bool Ready => Index != null;
        public string Structure { get { var t = Index; return t != null ? t.GetHashCode().ToString() : ""; } }

        public void FillRenderers(List<MaterialRendererEntry> into)
        {
            var t = Index;
            if (t == null) return;
            foreach (var r in t.Renderers)
            {
                if (!r) continue;
                var e = new MaterialRendererEntry { Renderer = r, Key = t.PathOf(r), Member = MaterialGuard.Renderer(r) };
                MaterialTargetKit.NamesOf(r, e.Materials);
                into.Add(e);
            }
        }

        public void FillMaterials(List<MaterialNameEntry> into)
        {
            var t = Index;
            if (t == null) return;
            var byName = new Dictionary<string, MaterialNameEntry>(StringComparer.Ordinal);
            foreach (var m in t.Materials)
            {
                if (!byName.TryGetValue(m.Name, out var e))
                {
                    byName[m.Name] = e = new MaterialNameEntry { Name = m.Name, Shader = m.Material && m.Material.shader ? m.Material.shader.name : "", Protected = true };
                    into.Add(e);
                }
                e.Slots += m.Slots;
                if (!MaterialGuard.Material(m.Material)) e.Protected = false;
            }
        }

        public MaterialSelection Select(string material)
        {
            var s = new MaterialSelection { Name = material };
            var t = Index;
            if (t == null) return s;
            foreach (var e in t.Named(material))
            {
                s.Slots += e.Slots;
                if (e.MemberSlots >= e.Slots || MaterialGuard.Material(e.Material)) { if (!s.Held) s.Held = e.Material; }
                else if (!s.Editable.Contains(e.Material)) s.Editable.Add(e.Material);
            }
            return s;
        }
    }

    /// <summary>A Studio item's array.</summary>
    public sealed class ItemMaterialTarget : IndexedMaterialTarget
    {
        readonly UiContext ctx;
        readonly string node;

        public ItemMaterialTarget(UiContext ctx, string node)
        {
            this.ctx = ctx;
            this.node = node;
            Address = MaterialEditTarget.Item(node);
            Selector = TargetSelector.Node(node);
        }

        public override MaterialEditTarget Address { get; }
        public override TargetSelector Selector { get; }
        public override string Owner => node;
        public override bool MaterialsFirst => false;

        public override MaterialTargets Index
        {
            get
            {
                var r = ctx.Runtime?.FindItem(node);
                return r != null && r.State == RealizationState.Built && !r.InGraveyard && r.Instance ? r.MaterialEdits?.Target : null;
            }
        }
    }

    /// <summary>The array of the loaded map.</summary>
    public sealed class MapMaterialTarget : IndexedMaterialTarget
    {
        /// <summary>The owner of a map's view.</summary>
        public const string MapOwner = "map";

        readonly UiContext ctx;

        public MapMaterialTarget(UiContext ctx)
        {
            this.ctx = ctx;
            Address = MaterialEditTarget.Map();
        }

        public override MaterialEditTarget Address { get; }
        public override TargetSelector Selector => TargetSelector.Scene;
        public override string Owner => MapOwner;
        public override bool MaterialsFirst => true;

        public override MaterialTargets Index
        {
            get
            {
                var map = ctx.Runtime?.GetPart<MapRealization>();
                return map != null && map.State == MapState.Loaded ? map.MaterialEdits?.Target : null;
            }
        }
    }

    static class MaterialTargetKit
    {
        /// <summary>The formatted names of a renderer's materials, each once.</summary>
        internal static void NamesOf(Renderer r, List<string> into)
        {
            foreach (var m in r.sharedMaterials)
            {
                if (!m) continue;
                string n = MeKey.Format(m.name);
                if (!into.Contains(n)) into.Add(n);
            }
        }
    }
}

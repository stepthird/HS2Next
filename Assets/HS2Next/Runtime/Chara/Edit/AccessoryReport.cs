// Read model of a built character's accessories (the accessory pages of the Maker, the bridge's chara.edit.accessories,
// the gates' facts and the tests): per build slot the document entry (id, category,
// item, stored parent, preferred slot, plugin rules), the list row the loader resolved and its columns (Name, MainAB,
// MainData, Parent, TexAB, TexD, TexC), the copy that serves the prefab (on request), whether the slot is loaded or is a
// MoreAccessories slot the build does not load, the facts of the realized prefab's CmpAccessory (hair-type, the colour
// areas, the alpha renderers, the two transform sets, the row's Parent column, dynamic bones and colliders) with its
// default colours, the anchor the loader used, the local transforms of the two sets, the object's activity and the read
// `show` flag, the hair shader type applied to a hair-type slot, every material slot of the own rendNormal / rendAlpha
// renderers with the colour properties as written, and the slot's dynamic-bone chains. Mirrors the HS2 side's
// chara.accessoryInfo. Nothing here writes the character. Unity-dependent; numbers and data names only.
using System.Collections.Generic;
using System.Linq;
using AIChara;
using HS2Next.Content;
using HS2Next.Data.Ext;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Chara.Edit
{
    public sealed class AccessoryReport
    {
        /// <summary>The character is male (the lists 350..363 are the same for both sexes).</summary>
        public bool Male;
        /// <summary>The build loads MoreAccessories slots (≥ 20; CharaExtOptions.MoreAccessories).</summary>
        public bool ExtraSlotsOn;
        /// <summary>Every slot holding an accessory, ascending by build slot: the document entries (with the table of the
        /// build) and, for a card build, the card's non-empty slots.</summary>
        public readonly List<SlotReport> Slots = new List<SlotReport>();

        /// <summary>The list columns a slot report carries (empty and absent columns are left out).</summary>
        public static readonly string[] RowColumns = { "Name", "MainAB", "MainData", "Parent", "TexAB", "TexD", "TexC" };

        /// <summary>The colour properties a material state carries when the material's shader declares them.</summary>
        public static readonly string[] ColorProperties = { "_Color", "_Color2", "_Color3", "_TopColor", "_UnderColor", "_Specular" };
        public static readonly string[] FloatProperties =
            { "_Glossiness", "_Glossiness2", "_Glossiness3", "_Glossiness4", "_Metallic", "_Metallic2", "_Metallic3", "_Metallic4", "_Smoothness" };

        /// <summary>The facts of an accessory prefab's CmpAccessory (the component of the slot object's root, as the loader
        /// reads it) and the row's Parent column.</summary>
        public sealed class PrefabFacts
        {
            /// <summary>The slot object carries a CmpAccessory.</summary>
            public bool HasCmpAccessory;
            public bool TypeHair;
            /// <summary>useColor01..03 (index 0..2).</summary>
            public bool[] UseColor = new bool[3];
            /// <summary>Live entries of rendAlpha (colour 4).</summary>
            public int AlphaRenderers;
            /// <summary>trfMove01 / trfMove02 are set.</summary>
            public bool Move1, Move2;
            /// <summary>The row's Parent column (null without a row); HeadWeighted = "null", Fixed = "0".</summary>
            public string ParentColumn;
            public bool HeadWeighted, Fixed;
            /// <summary>DynamicBone components and DynamicBone colliders (sphere / capsule and plane) under the slot object.</summary>
            public int DynamicBones, Colliders;

            public PrefabFacts Copy() => new PrefabFacts
            {
                HasCmpAccessory = HasCmpAccessory, TypeHair = TypeHair, UseColor = (bool[])UseColor.Clone(), AlphaRenderers = AlphaRenderers,
                Move1 = Move1, Move2 = Move2, ParentColumn = ParentColumn, HeadWeighted = HeadWeighted, Fixed = Fixed,
                DynamicBones = DynamicBones, Colliders = Colliders,
            };

            public override string ToString() =>
                !HasCmpAccessory ? "no CmpAccessory" :
                $"{(TypeHair ? "hair " : "")}areas {(UseColor[0] ? 1 : 0)}{(UseColor[1] ? 1 : 0)}{(UseColor[2] ? 1 : 0)} alpha {AlphaRenderers} sets {(Move1 ? 1 : 0)}{(Move2 ? 1 : 0)}" +
                $" parent {ParentColumn ?? "-"} chains {DynamicBones} colliders {Colliders}";
        }

        public sealed class SlotReport
        {
            /// <summary>The build slot (0..19 HS2's slots, ≥ 20 MoreAccessories); shown as slot + 1.</summary>
            public int Slot;
            /// <summary>The document entry's id (null for a card build's slot).</summary>
            public string Id;
            public int Category;
            /// <summary>The document's item reference ("guid#id"); for a card build the raw id as text.</summary>
            public string Item;
            /// <summary>The stored parent key ("" = the row's Parent column).</summary>
            public string Parent;
            /// <summary>The entry's preferred slot (importSlot); null when none.</summary>
            public int? ImportSlot;
            public bool NoShake;
            /// <summary>The entry's Additional Accessory Controls data (the document object, not a copy; null when none).</summary>
            public AccessoryRules Rules;
            /// <summary>The row the loader resolved ("category:key"); null when the slot did not resolve or is not loaded.</summary>
            public string Row;
            /// <summary>The row's columns of <see cref="RowColumns"/> (non-empty values only).</summary>
            public readonly SortedDictionary<string, string> Columns = new SortedDictionary<string, string>(System.StringComparer.Ordinal);
            /// <summary>With <c>served</c> asked for: the source the catalog serves the prefab from ("zipmod" / "abdata" / "none")
            /// and the serving zipmod's GUID (null for abdata); null otherwise.</summary>
            public string ServedKind, ServedGuid;
            /// <summary>The slot object exists.</summary>
            public bool Loaded;
            /// <summary>A MoreAccessories slot (≥ 20).</summary>
            public bool Extra;
            /// <summary>A slot ≥ 20 the build does not load (MoreAccessories off).</summary>
            public bool NotLoaded;
            /// <summary>The read `show` flag of the slot (MoreAccessories' show; true for slots 0..19). The scene's visibility is
            /// state.accessories; this flag no longer hides the object.</summary>
            public bool Show = true;
            /// <summary>The slot object's own activity (false without an object).</summary>
            public bool Active;
            public PrefabFacts Facts = new PrefabFacts();
            /// <summary>The prefab's default colours as document values (defColor0n, defGlossPower0n, defMetallicPower0n for the
            /// used areas 1..3 and, with alpha renderers, colour 4; the format defaults otherwise); null without a CmpAccessory.</summary>
            public AccessoryColor[] Defaults;
            /// <summary>The anchor the loader used: the transform's name and its path of names under the character's root;
            /// "head bones" (Parent "null": parented to the body top and rebound to the head bones) or "none" (no anchor).</summary>
            public string AnchorName = "none", AnchorPath;
            /// <summary>The local transforms of trfMove01 / trfMove02 (null where the prefab has none).</summary>
            public readonly MoveState[] Moves = new MoveState[2];
            /// <summary>The hair shader type the materials of a hair-type slot got (0 / 1); −1 for other slots.</summary>
            public int ShaderType = -1;
            /// <summary>Every slot of every own rendNormal renderer, then of every own rendAlpha renderer.</summary>
            public readonly List<MaterialState> Materials = new List<MaterialState>();
            /// <summary>The slot's dynamic-bone chains (stage order).</summary>
            public readonly List<ChainState> Chains = new List<ChainState>();

            public override string ToString() =>
                $"{Slot + 1:00} {Id ?? "-"} {Category}:{Item} {(Loaded ? Facts.ToString() : NotLoaded ? "not loaded (MoreAccessories off)" : "unloaded")} anchor {AnchorName}";
        }

        public sealed class MoveState
        {
            public string Transform;
            public Vector3 Position, Euler, Scale;
        }

        public sealed class MaterialState
        {
            /// <summary>"normal" (rendNormal) or "alpha" (rendAlpha).</summary>
            public string Kind;
            public string Renderer;
            /// <summary>The material slot and the renderer's slot count.</summary>
            public int Slot, Slots;
            public string Material, Shader;
            public int Queue;
            /// <summary>The renderer object's own activity.</summary>
            public bool Active;
            /// <summary>The colour properties of <see cref="ColorProperties"/> / <see cref="FloatProperties"/> the shader declares.</summary>
            public readonly SortedDictionary<string, Color> Colors = new SortedDictionary<string, Color>(System.StringComparer.Ordinal);
            public readonly SortedDictionary<string, float> Floats = new SortedDictionary<string, float>(System.StringComparer.Ordinal);
            /// <summary>_MainTex's name (null = none or not declared; "(unnamed)" for a texture without a name).</summary>
            public string MainTex;
        }

        public sealed class ChainState
        {
            public string Root;
            public int Colliders, Particles;
            public bool Active, NoShake, Enabled;
        }

        // ---------------------------------------------------------------- build

        /// <summary>The report of a built character (null for a null context); <paramref name="served"/> also asks the catalog
        /// which copy serves each prefab (slower); <paramref name="materials"/> false leaves the material and chain lists empty
        /// (the gates' facts do not read them).</summary>
        public static AccessoryReport Of(CharaContext ctx, bool served = false, bool materials = true)
        {
            if (ctx == null) return null;
            var r = new AccessoryReport { Male = ctx.IsMale, ExtraSlotsOn = ExtraOn(ctx) };
            foreach (int s in SlotNumbers(ctx)) r.Slots.Add(SlotOf(ctx, s, served, materials));
            return r;
        }

        /// <summary>The build slots holding an accessory, ascending: the document table's slots (document builds), or the
        /// non-empty realized slots (card builds).</summary>
        public static List<int> SlotNumbers(CharaContext ctx)
        {
            var l = new SortedSet<int>();
            if (ctx == null) return new List<int>();
            var table = ctx.Edit?.Input?.Slots;
            if (table != null) foreach (var s in table.EntryBySlot.Keys) l.Add(s);
            else
                for (int s = 0; s < ctx.Accessories.Count; s++)
                    if (ctx.Accessories[s] != null && !ctx.Accessories[s].IsEmpty) l.Add(s);
            return l.ToList();
        }

        /// <summary>The build slot of accessory <paramref name="accessoryId"/> in the build's table; −1 when it has none.</summary>
        public static int SlotOfAccessory(CharaContext ctx, string accessoryId)
        {
            var table = ctx?.Edit?.Input?.Slots;
            return accessoryId != null && table != null && table.SlotById.TryGetValue(accessoryId, out int s) ? s : -1;
        }

        static bool ExtraOn(CharaContext ctx) => ctx.Ext != null && ctx.Options != null && ctx.Options.Enabled && ctx.Options.MoreAccessories;

        /// <summary>One slot's report (null for a null context or a negative slot). A slot without an accessory reports
        /// category 350 and nothing else.</summary>
        public static SlotReport SlotOf(CharaContext ctx, int slot, bool served = false, bool materials = true)
        {
            if (ctx == null || slot < 0) return null;
            var p = new SlotReport { Slot = slot, Extra = slot >= MoreAccessoriesData.FirstSlot, Category = 350 };
            AccessoryEntry e = null;
            ctx.Edit?.Input?.Slots?.EntryBySlot.TryGetValue(slot, out e);
            var acc = slot < ctx.Accessories.Count ? ctx.Accessories[slot] : null;
            if (e != null)
            {
                p.Id = e.Id; p.Category = e.Category; p.Item = e.Item; p.Parent = e.Parent ?? ""; p.ImportSlot = e.ImportSlot;
                p.NoShake = e.NoShake; p.Rules = e.Rules;
            }
            else if (acc != null && !acc.IsEmpty)
            {
                p.Category = acc.Type; p.Item = acc.Id.ToString(System.Globalization.CultureInfo.InvariantCulture); p.Parent = acc.ParentKey ?? "";
                p.NoShake = acc.NoShake;
            }
            p.NotLoaded = p.Extra && (acc == null || slot >= ctx.Accessories.Count) && p.Category >= 351 && p.Category <= 363;
            if (acc == null) return p;
            p.Show = acc.Show;
            var row = acc.Entry;
            if (row != null)
            {
                p.Row = row.Category + ":" + row.Key;
                foreach (var col in RowColumns)
                {
                    string v = row[col];
                    if (!string.IsNullOrEmpty(v)) p.Columns[col] = v;
                }
                p.Facts.ParentColumn = row["Parent"];
                p.Facts.HeadWeighted = p.Facts.ParentColumn == "null";
                p.Facts.Fixed = p.Facts.ParentColumn == "0";
                if (served && ctx.Catalog != null && !string.IsNullOrEmpty(row["MainAB"]) && !string.IsNullOrEmpty(row["MainData"]))
                {
                    try
                    {
                        var ex = ctx.Catalog.ExplainLoad(row, row["MainAB"], row["MainData"]);
                        p.ServedKind = ex?.ServedKind; p.ServedGuid = ex?.ServedGuid;
                    }
                    catch (System.Exception x) { p.ServedKind = "error: " + x.GetType().Name; }
                }
            }
            var go = acc.Object;
            p.Loaded = go;
            if (!go) return p;
            p.Active = go.activeSelf;
            p.ShaderType = acc.HairShaderType;
            if (acc.HeadBound) p.AnchorName = "head bones";
            else if (acc.Anchor) { p.AnchorName = acc.Anchor.name; p.AnchorPath = NamePath(acc.Anchor, ctx.Root ? ctx.Root.transform : null); }
            var cmp = go.GetComponent<CmpAccessory>();
            FillFacts(p.Facts, cmp, go);
            if (cmp == null) return p;
            p.Defaults = DefaultsOf(cmp);
            var moves = new[] { cmp.trfMove01, cmp.trfMove02 };
            for (int i = 0; i < 2; i++)
                if (moves[i]) p.Moves[i] = new MoveState { Transform = moves[i].name, Position = moves[i].localPosition, Euler = moves[i].localEulerAngles, Scale = moves[i].localScale };
            if (!materials) return p;
            AddMaterials(p, "normal", cmp.rendNormal, go);
            AddMaterials(p, "alpha", cmp.rendAlpha, go);
            var stage = ctx.Motion != null ? ctx.Motion.Get<HS2Next.Motion.DynamicBones.DynamicBoneStage>() : null;
            if (stage != null)
                foreach (var c in stage.Chains)
                {
                    if (c == null || c.Category != HS2Next.Motion.DynamicBones.DynamicBoneCategory.Accessory || c.Slot != slot) continue;
                    p.Chains.Add(new ChainState { Root = c.RootName, Colliders = c.ColliderCount, Particles = c.ParticleCount, Active = c.Active, NoShake = c.NoShake, Enabled = c.Enabled });
                }
            return p;
        }

        /// <summary>The report of the slot holding accessory <paramref name="accessoryId"/> (null when the build's table has no
        /// slot for it).</summary>
        public static SlotReport AccessoryOf(CharaContext ctx, string accessoryId, bool served = false, bool materials = true)
        {
            int s = SlotOfAccessory(ctx, accessoryId);
            return s < 0 ? null : SlotOf(ctx, s, served, materials);
        }

        /// <summary>The facts of a slot object's CmpAccessory (<paramref name="cmp"/> null: HasCmpAccessory false; the row's
        /// Parent column stays as set by the caller).</summary>
        public static void FillFacts(PrefabFacts f, CmpAccessory cmp, GameObject go)
        {
            f.HasCmpAccessory = cmp != null;
            if (go)
            {
                f.DynamicBones = go.GetComponentsInChildren<global::DynamicBone>(true).Length;
                f.Colliders = go.GetComponentsInChildren<global::DynamicBoneCollider>(true).Length + go.GetComponentsInChildren<global::DynamicBonePlaneCollider>(true).Length;
            }
            if (cmp == null) return;
            f.TypeHair = cmp.typeHair != 0;
            f.UseColor[0] = cmp.useColor01 != 0; f.UseColor[1] = cmp.useColor02 != 0; f.UseColor[2] = cmp.useColor03 != 0;
            f.AlphaRenderers = cmp.rendAlpha?.Count(r => r) ?? 0;
            f.Move1 = cmp.trfMove01; f.Move2 = cmp.trfMove02;
        }

        /// <summary>The prefab's four colours as document values: areas 1..3 when useColor0n (defColor0n, defGlossPower0n,
        /// defMetallicPower0n), colour 4 when rendAlpha is not empty (defColor04 …); the format defaults for the rest.</summary>
        public static AccessoryColor[] DefaultsOf(CmpAccessory a)
        {
            if (a == null) return null;
            var r = new[] { new AccessoryColor(), new AccessoryColor(), new AccessoryColor(), new AccessoryColor() };
            void Set(int i, bool use, Color col, float gloss, float metallic)
            {
                if (!use) return;
                r[i].Color = new Color4(col.r, col.g, col.b, col.a);
                r[i].Gloss = gloss;
                r[i].Metallic = metallic;
            }
            Set(0, a.useColor01 != 0, a.defColor01, a.defGlossPower01, a.defMetallicPower01);
            Set(1, a.useColor02 != 0, a.defColor02, a.defGlossPower02, a.defMetallicPower02);
            Set(2, a.useColor03 != 0, a.defColor03, a.defGlossPower03, a.defMetallicPower03);
            Set(3, a.rendAlpha != null && a.rendAlpha.Count > 0, a.defColor04, a.defGlossPower04, a.defMetallicPower04);
            return r;
        }

        static void AddMaterials(SlotReport p, string kind, List<Renderer> rends, GameObject go)
        {
            if (rends == null) return;
            var seen = new HashSet<Renderer>();
            foreach (var r in rends)
            {
                if (!OwnRenderer(r, go) || !seen.Add(r)) continue;
                Material[] mats;
                try { mats = r.sharedMaterials; } catch (System.Exception) { mats = new Material[0]; }
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    var s = new MaterialState { Kind = kind, Renderer = r.name, Slot = i, Slots = mats.Length, Active = r.gameObject.activeSelf };
                    if (m)
                    {
                        s.Material = m.name; s.Shader = m.shader ? m.shader.name : null; s.Queue = m.renderQueue;
                        foreach (var c in ColorProperties) if (m.HasProperty(c)) s.Colors[c] = m.GetColor(c);
                        foreach (var f in FloatProperties) if (m.HasProperty(f)) s.Floats[f] = m.GetFloat(f);
                        if (m.HasProperty("_MainTex")) { var t = m.GetTexture("_MainTex"); s.MainTex = t ? (string.IsNullOrEmpty(t.name) ? "(unnamed)" : t.name) : null; }
                    }
                    p.Materials.Add(s);
                }
            }
        }

        // a renderer of the slot object's own instance (a prefab may reference a renderer of another prefab asset)
        static bool OwnRenderer(Renderer r, GameObject go)
        {
            try { return r && go && r.transform.IsChildOf(go.transform); }
            catch (MissingReferenceException) { return false; }
            catch (System.NullReferenceException) { return false; }
        }

        /// <summary>The names from <paramref name="root"/> (excluded) down to <paramref name="t"/>, joined by '/'.</summary>
        public static string NamePath(Transform t, Transform root)
        {
            var l = new List<string>();
            for (var x = t; x && x != root; x = x.parent) l.Add(x.name);
            l.Reverse();
            return string.Join("/", l);
        }

        public override string ToString() => string.Join("; ", Slots.Select(s => s.ToString()));
    }
}

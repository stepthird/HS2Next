// Read model of a built character's hair (Phase 4.3):
// per part the item, the realized prefab, its CmpHair facts (the seven flags, the acsDefColor count, the bundles with
// their limits, base pose and default rates), the stored and the effective bundles (the count rule) with
// their correction transforms' local pose, the dynamic-bone chains per bundle (root, activity, noShake, relation to the
// correction transform, the captured rest pose), the mesh pattern as written on the materials, the shader / render queue /
// main texture of every rendHair slot and the decoration colours per rendAccessory slot. Mirrors the HS2 side's
// chara.hairInfo. Nothing here writes the character. Unity-dependent; numbers only.
using System.Collections.Generic;
using System.Linq;
using AIChara;
using HS2Next.Chara.Edit.Units;
using HS2Next.Content;
using HS2Next.Motion.DynamicBones;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Chara.Edit
{
    public sealed class HairReport
    {
        /// <summary>hair.shaderType of the document (0 hair_dithering, 1 hair_cutout).</summary>
        public int ShaderType;
        /// <summary>The four parts, HS2 order (back, front, side, option).</summary>
        public readonly PartReport[] Parts = new PartReport[4];

        /// <summary>The relation of a chain to its bundle's correction transform: the correction transform is the
        /// chain's root, a transform inside the chain, an ancestor of the root, or none of these.</summary>
        public const string RelationRoot = "root", RelationInside = "inside", RelationAbove = "above", RelationUnrelated = "unrelated";

        public sealed class PartReport
        {
            /// <summary>0..3 and "back" / "front" / "side" / "option".</summary>
            public int Part;
            public string Name;
            /// <summary>The document's item reference ("guid#id").</summary>
            public string Item;
            /// <summary>The list row the realization resolved ("category:key"), null when none resolved.</summary>
            public string Row;
            /// <summary>The prefab asset name of the row (MainData, MainData02 for males when set); null without a row.</summary>
            public string Prefab;
            /// <summary>The realized part object exists (CharaContext.Hair[k]).</summary>
            public bool Realized;
            /// <summary>The realized object carries a CmpHair (HS2: "the part has a prefab"; none rows: false).</summary>
            public bool HasCmpHair;
            public bool UseTopColor, UseUnderColor, UseSameSkinColorButton, UseMesh, UseAcsColor01, UseAcsColor02, UseAcsColor03;
            /// <summary>CmpHair.acsDefColor (the prefab's default decoration colours).</summary>
            public Color[] AcsDefColor = new Color[0];
            public int AcsDefColorCount => AcsDefColor.Length;
            /// <summary>CmpHair.rendHair holds no live renderer: the part gets no shader, colours or mesh (GH8).</summary>
            public bool RendHairEmpty;
            /// <summary>The stored bundle list applies (count = boneInfo count, indices 0..n−1); false: every bundle at the
            /// prefab defaults.</summary>
            public bool StoredValid;
            /// <summary>The stored list's entry count.</summary>
            public int StoredCount;
            /// <summary>One entry per CmpHair.boneInfo, index order.</summary>
            public readonly List<Bundle> Bundles = new List<Bundle>();
            /// <summary>Hair chains of this part that belong to no bundle (on the prefab root; they ignore noShake).</summary>
            public readonly List<Chain> OtherChains = new List<Chain>();
            public MeshState Mesh = new MeshState();
            /// <summary>Every slot of every rendHair renderer (hierarchy order of CmpHair.rendHair).</summary>
            public readonly List<MaterialState> Materials = new List<MaterialState>();
            /// <summary>Every slot of every rendAccessory renderer.</summary>
            public readonly List<DecorationState> Decoration = new List<DecorationState>();

            /// <summary>HS2's row gates except the auto-colour and mesh-item parts, which read the document.</summary>
            public bool AcsShown(int n) => HasCmpHair && n >= 1 && n <= 3 && n <= AcsDefColorCount && (n == 1 ? UseAcsColor01 : n == 2 ? UseAcsColor02 : UseAcsColor03);
            public bool AnyBundleShown => HasCmpHair && Bundles.Any(b => b.Shown);
        }

        public sealed class Bundle
        {
            public int Index;
            /// <summary>The correction transform exists (HS2 lists only these).</summary>
            public bool Shown;
            /// <summary>«調整NN» number 1.. among the shown; 0 when not shown.</summary>
            public int Title;
            /// <summary>The correction transform's name (null when absent).</summary>
            public string Transform;
            public Vector3 PosMin, PosMax, RotMin, RotMax, BasePos, BaseRot;
            /// <summary>InverseLerp(min, max, base) per axis (equal limits → 0): the rates of a part without stored bundles.</summary>
            public Vector3 DefaultMove, DefaultRotate;
            /// <summary>The stored entry with this index (null when none; with an invalid list it is listed but not applied).</summary>
            public bool HasStored;
            public Vector3 StoredMove, StoredRotate;
            public bool StoredNoShake;
            /// <summary>What the unit applies (stored when the list is valid, else the defaults with noShake off).</summary>
            public Vector3 EffectiveMove, EffectiveRotate;
            public bool EffectiveNoShake;
            /// <summary>The correction transform's local pose now.</summary>
            public Vector3 LocalPosition, LocalEuler;
            public Quaternion LocalRotation = Quaternion.identity;
            public readonly List<Chain> Chains = new List<Chain>();
        }

        public sealed class Chain
        {
            public string Root, Owner;
            public int Bundle;
            public bool Active, NoShake, Enabled;
            public int Particles;
            /// <summary><see cref="RelationRoot"/>, <see cref="RelationInside"/>, <see cref="RelationAbove"/>,
            /// <see cref="RelationUnrelated"/> (against the bundle's correction transform; "unrelated" for other chains).</summary>
            public string Relation;
            /// <summary>For a chain in no bundle's list: the correction transform its <see cref="Relation"/> names (null when
            /// unrelated).</summary>
            public string Transform;
            /// <summary>The captured rest local pose of the root particle (the pose the chain's reset restores).</summary>
            public Vector3 RestRootPosition;
            public Quaternion RestRootRotation = Quaternion.identity;
        }

        public sealed class MeshState
        {
            /// <summary>The document's mesh item reference, colour (stored alpha) and layout.</summary>
            public string Item;
            public Color Color;
            public Vector4 Layout;
            /// <summary>The unit writes the mesh values (useMesh and a live rendHair renderer).</summary>
            public bool Written;
            /// <summary>As on the first rendHair slot: _MeshColorMask_B (name, size, format; null = no texture), its scale /
            /// offset, _MeshColor_01, _Mesh_onoff (NaN when the property is absent).</summary>
            public string Texture, TextureFormat;
            public int TextureWidth, TextureHeight;
            public Vector2 Scale = Vector2.one, Offset = Vector2.zero;
            public Color MeshColor;
            public float MeshOnOff = float.NaN;
            /// <summary>Every rendHair slot holds the same mesh values as the first.</summary>
            public bool Uniform = true;
        }

        public sealed class MaterialState
        {
            public string Renderer;
            public int Slot;
            public string Material, Shader;
            public int RenderQueue;
            /// <summary>_MainTex's name (null = none).</summary>
            public string MainTex;
            public string MeshTexture;
            public Vector2 MeshScale = Vector2.one, MeshOffset = Vector2.zero;
            public Color MeshColor, Color, TopColor, UnderColor, Specular;
            public float Metallic = float.NaN, Smoothness = float.NaN, Ringoff = float.NaN;
        }

        public sealed class DecorationState
        {
            public string Renderer;
            public int Slot;
            public string Material, Shader;
            /// <summary>_Color, _Color2, _Color3 (null when the shader lacks the property).</summary>
            public Color?[] Colors = new Color?[3];
        }

        // ---------------------------------------------------------------- build

        /// <summary>The report of a built character (null for a null context).</summary>
        public static HairReport Of(CharaContext ctx)
        {
            if (ctx == null) return null;
            var r = new HairReport { ShaderType = ctx.Chara?.Hair?.ShaderType ?? 0 };
            var stage = ctx.Motion != null ? ctx.Motion.Get<DynamicBoneStage>() : null;
            for (int k = 0; k < 4; k++) r.Parts[k] = PartOf(ctx, k, stage);
            return r;
        }

        /// <summary>One part's report (null for a null context or a part out of range).</summary>
        public static PartReport PartOf(CharaContext ctx, int k) =>
            ctx == null ? null : PartOf(ctx, k, ctx.Motion != null ? ctx.Motion.Get<DynamicBoneStage>() : null);

        static PartReport PartOf(CharaContext ctx, int k, DynamicBoneStage stage)
        {
            if (ctx == null || k < 0 || k > 3) return null;
            var p = new PartReport { Part = k, Name = UnitMap.HairParts[k] };
            var doc = ctx.Chara?.Hair?.Parts[k];
            p.Item = doc?.Item;
            var go = ctx.Hair != null && k < ctx.Hair.Length ? ctx.Hair[k] : null;
            p.Realized = go;
            // the row the realization built (the resolver's registration of the part key; the sex's prefab column)
            var row = RowOf(ctx, k);
            if (row != null)
            {
                p.Row = row.Category + ":" + row.Key;
                string d2 = row["MainData02"];
                p.Prefab = ctx.IsMale && !string.IsNullOrEmpty(d2) && d2 != "0" ? d2 : row["MainData"];
            }
            var cmp = go ? go.GetComponent<CmpHair>() : null;
            p.HasCmpHair = cmp != null;
            if (doc != null)
            {
                p.Mesh.Item = doc.Mesh.Item;
                p.Mesh.Color = EditContext.U(doc.Mesh.Color);
                p.Mesh.Layout = EditContext.U(doc.Mesh.Layout);
                p.StoredCount = doc.Bundles?.Count ?? 0;
            }
            if (cmp == null) return p;
            p.UseTopColor = cmp.useTopColor != 0; p.UseUnderColor = cmp.useUnderColor != 0;
            p.UseSameSkinColorButton = cmp.useSameSkinColorButton != 0; p.UseMesh = cmp.useMesh != 0;
            p.UseAcsColor01 = cmp.useAcsColor01 != 0; p.UseAcsColor02 = cmp.useAcsColor02 != 0; p.UseAcsColor03 = cmp.useAcsColor03 != 0;
            p.AcsDefColor = cmp.acsDefColor != null ? cmp.acsDefColor.ToArray() : new Color[0];
            var rends = HairUnit.HairRenderers(go, cmp);
            p.RendHairEmpty = rends.Count == 0;
            p.Mesh.Written = HairUnit.MeshDrawn(cmp, rends.Count);

            // bundles
            int n = cmp.boneInfo?.Count ?? 0;
            p.StoredValid = HairUnit.StoredValid(doc, n);
            var eff = HairUnit.EffectiveBundles(cmp, doc);
            var chains = stage == null ? new List<DynamicBoneChain>() : stage.Chains.Where(c => c.Category == DynamicBoneCategory.Hair && c.Kind == k).ToList();
            int title = 0;
            for (int b = 0; b < n; b++)
            {
                var bi = cmp.boneInfo[b];
                var e = new Bundle { Index = b, Shown = bi?.trfCorrect != null };
                if (e.Shown) e.Title = ++title;
                if (bi != null)
                {
                    e.Transform = bi.trfCorrect ? bi.trfCorrect.name : null;
                    e.PosMin = bi.posMin; e.PosMax = bi.posMax; e.RotMin = bi.rotMin; e.RotMax = bi.rotMax; e.BasePos = bi.basePos; e.BaseRot = bi.baseRot;
                    HairUnit.DefaultRates(bi, out e.DefaultMove, out e.DefaultRotate);
                    if (bi.trfCorrect)
                    {
                        bi.trfCorrect.GetLocalPositionAndRotation(out e.LocalPosition, out e.LocalRotation);
                        e.LocalEuler = bi.trfCorrect.localEulerAngles;
                    }
                }
                var st = HairUnit.BundleOf(doc, b);
                if (st != null) { e.HasStored = true; e.StoredMove = EditContext.U(st.Move); e.StoredRotate = EditContext.U(st.Rotate); e.StoredNoShake = st.NoShake; }
                if (b < eff.Length) { e.EffectiveMove = eff[b].Move; e.EffectiveRotate = eff[b].Rotate; e.EffectiveNoShake = eff[b].NoShake; }
                foreach (var c in chains.Where(c => c.Bundle == b)) e.Chains.Add(ChainOf(c, bi?.trfCorrect));
                p.Bundles.Add(e);
            }
            // chains in no bundle's list: the relation to the part's correction transforms (the strongest: a chain on the prefab
            // root can hold a correction transform inside it, e.g. [SH]hairs.collect.v4#1, and then simulates it)
            var trfs = Enumerable.Range(0, n).Select(b => cmp.boneInfo[b]?.trfCorrect).Where(t => t).ToList();
            foreach (var c in chains.Where(c => c.Bundle < 0 || c.Bundle >= n))
            {
                var ch = ChainOf(c, null);
                foreach (var t in trfs)
                {
                    string rel = RelationOf(c, t);
                    if (Strength(rel) > Strength(ch.Relation)) { ch.Relation = rel; ch.Transform = t.name; }
                }
                p.OtherChains.Add(ch);
            }

            // materials and the mesh state as written
            bool first = true;
            foreach (var rend in rends)
            {
                Material[] mats;
                try { mats = rend.sharedMaterials; } catch (System.Exception) { mats = new Material[0]; }
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    var s = new MaterialState { Renderer = rend.name, Slot = i, Material = m ? m.name : null, Shader = m && m.shader ? m.shader.name : null, RenderQueue = m ? m.renderQueue : 0 };
                    if (m)
                    {
                        s.MainTex = Tex(m, "_MainTex");
                        s.MeshTexture = Tex(m, "_MeshColorMask_B");
                        if (m.HasProperty("_MeshColorMask_B")) { s.MeshScale = m.GetTextureScale("_MeshColorMask_B"); s.MeshOffset = m.GetTextureOffset("_MeshColorMask_B"); }
                        s.MeshColor = Col(m, "_MeshColor_01"); s.Color = Col(m, "_Color"); s.TopColor = Col(m, "_TopColor"); s.UnderColor = Col(m, "_UnderColor"); s.Specular = Col(m, "_Specular");
                        s.Metallic = Flt(m, "_Metallic"); s.Smoothness = Flt(m, "_Smoothness"); s.Ringoff = Flt(m, "_Ringoff");
                        if (first)
                        {
                            first = false;
                            var t = m.HasProperty("_MeshColorMask_B") ? m.GetTexture("_MeshColorMask_B") : null;
                            p.Mesh.Texture = t ? t.name : null;
                            p.Mesh.TextureWidth = t ? t.width : 0; p.Mesh.TextureHeight = t ? t.height : 0;
                            p.Mesh.TextureFormat = t is Texture2D t2 ? t2.format.ToString() : t ? t.GetType().Name : null;
                            p.Mesh.Scale = s.MeshScale; p.Mesh.Offset = s.MeshOffset; p.Mesh.MeshColor = s.MeshColor;
                            p.Mesh.MeshOnOff = Flt(m, "_Mesh_onoff");
                        }
                        else if (s.MeshTexture != p.Mesh.Texture || s.MeshScale != p.Mesh.Scale || s.MeshOffset != p.Mesh.Offset || s.MeshColor != p.Mesh.MeshColor) p.Mesh.Uniform = false;
                    }
                    p.Materials.Add(s);
                }
            }
            if (cmp.rendAccessory != null)
                foreach (var rend in cmp.rendAccessory.Where(x => x))
                {
                    Material[] mats;
                    try { mats = rend.sharedMaterials; } catch (System.Exception) { mats = new Material[0]; }
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        var d = new DecorationState { Renderer = rend.name, Slot = i, Material = m ? m.name : null, Shader = m && m.shader ? m.shader.name : null };
                        if (m)
                        {
                            string[] props = { "_Color", "_Color2", "_Color3" };
                            for (int j = 0; j < 3; j++) d.Colors[j] = m.HasProperty(props[j]) ? m.GetColor(props[j]) : (Color?)null;
                        }
                        p.Decoration.Add(d);
                    }
                }
            return p;
        }

        static Chain ChainOf(DynamicBoneChain c, Transform trf)
        {
            var ch = new Chain
            {
                Root = c.RootName, Owner = c.Owner ? c.Owner.name : null, Bundle = c.Bundle, Active = c.Active, NoShake = c.NoShake, Enabled = c.Enabled,
                Particles = c.ParticleCount, Relation = RelationOf(c, trf),
            };
            if (c.ParticleCount > 0) { ch.RestRootPosition = c.RestLocalPosition(0); ch.RestRootRotation = c.RestLocalRotation(0); }
            return ch;
        }

        static int Strength(string relation) => relation == RelationRoot ? 3 : relation == RelationInside ? 2 : relation == RelationAbove ? 1 : 0;

        /// <summary>The relation of a chain to a correction transform.</summary>
        public static string RelationOf(DynamicBoneChain c, Transform trf)
        {
            if (!trf || c == null || !c.Root) return RelationUnrelated;
            if (c.Root == trf) return RelationRoot;
            for (int i = 1; i < c.ParticleCount; i++) if (c.ParticleTransform(i) == trf) return RelationInside;
            if (c.Root.IsChildOf(trf)) return RelationAbove;
            return RelationUnrelated;
        }

        /// <summary>The list row the part unit built for part <paramref name="k"/> (its last applied row; null before the
        /// unit ran or without an editor).</summary>
        static ListEntry RowOf(CharaContext ctx, int k) => (ctx.Editor?.Unit(UnitMap.HairUnits[k]) as HairUnit)?.Row;

        static string Tex(Material m, string prop)
        {
            if (!m.HasProperty(prop)) return null;
            var t = m.GetTexture(prop);
            return t ? (string.IsNullOrEmpty(t.name) ? "(unnamed)" : t.name) : null;
        }

        static Color Col(Material m, string prop) => m.HasProperty(prop) ? m.GetColor(prop) : new Color(float.NaN, float.NaN, float.NaN, float.NaN);
        static float Flt(Material m, string prop) => m.HasProperty(prop) ? m.GetFloat(prop) : float.NaN;

        public override string ToString() => string.Join("; ", Parts.Where(p => p != null).Select(p =>
            $"{p.Name} {p.Item} {(p.HasCmpHair ? p.Prefab ?? "?" : "no CmpHair")} bundles {p.Bundles.Count}{(p.StoredValid ? "" : " (defaults)")} mesh {(p.Mesh.Written ? p.Mesh.Texture ?? "null" : "-")}"));
    }
}

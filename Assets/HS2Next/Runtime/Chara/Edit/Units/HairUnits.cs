// Hair part units (hair.shader): the part prefab (swapped on an item change
// only), the hair shader and main texture of hairShaderType, the colours of rendHair, the decoration colours of
// rendAccessory and the bundle corrections (CmpHair.boneInfo trfCorrect). The writes are the pre-4.0 BuildHair writes
//, reading the documents. A change of hair.shaderType marks the four parts (their
// materials take the new shader; the scene's URP mapping is re-applied to them in the ownership step).
// The shader pass and the colour writes go through HairMaterials (Phase 4.5: shared with the hair-type accessories); the
// rules of which values a part writes (ColoursBeforeShaderAsHS2, the use flags of CmpHair) stay here.
// Phase 4.3:
//   mesh pattern  on a part with a live rendHair renderer (MeshOnEveryHair, the default: HS2 + the installed HairMeshforAll;
//                 off: only a prefab with useMesh), every slot of every rendHair renderer gets
//                 _MeshColorMask_B = the 306 row's texture (none / unresolved → null), its scale (lerp(0.5, 1, x),
//                 lerp(0.5, 1, y)) and offset (lerp(0.5, 1.5, z), lerp(0.5, 1.5, w)) of mesh.layout with clamped lerps, and
//                 _MeshColor_01 = mesh.color with alpha 1 (GH1, J9); _Mesh_onoff and _MeshColorMask stay the prefab's
//   decoration    _Color / _Color2 / _Color3 = accessoryColors[j] (alpha 1) on every slot of every rendAccessory renderer,
//                 only for useAcsColor0(j+1) and j < acsDefColor.Count (GH5, J10)
//   empty rendHair a part whose CmpHair.rendHair holds no live renderer gets no shader pass, colours or mesh values (GH8,
//                 J15); a prefab without CmpHair keeps the fallback (every renderer without "_acs")
//   bundle rule   the stored bundles apply only when the list holds exactly the prefab's boneInfo count with the indices
//                 0..n−1, else every bundle takes the prefab defaults (InverseLerp of the base pose, noShake off; GH2, J6)
//   chain marks   a change of the effective move / rotate rates marks the light chain re-derivation (deferrable in a drag),
//                 a change of an effective noShake the full rebuild; a swap marks the full rebuild (GH4, J7); the parity switch
//                 ChainRootBundlesAsHS2 (off) keeps chain-transform corrections at the prefab's base pose (HS2's look, HC11)
using System;
using System.Collections.Generic;
using System.Linq;
using AIChara;
using HS2Next.Content;
using HS2Next.Save;
using UnityEngine;
using EM = HS2Next.Chara.Edit.EditMaterials;

namespace HS2Next.Chara.Edit.Units
{
    public sealed class HairUnit : UnitBase
    {
        static readonly string[] HairProps = { "ChaFileHair.HairBack", "ChaFileHair.HairFront", "ChaFileHair.HairSide", "ChaFileHair.HairOption" };
        static readonly string[] HairNames = { "ct_hairB", "ct_hairF", "ct_hairS", "ct_hairO" };

        readonly int k;
        ListEntry appliedRow;
        Shader appliedShader;
        string appliedRates, appliedShake;
        bool loaded;
        // the mesh pattern texture of the last resolved 306 reference (cached per row; a bundle asset, not owned)
        string meshRef;
        bool meshResolved, meshLoaded;
        ListEntry meshRow;
        Texture2D meshTex;
        // per material of the part: the mesh values of the prefab's material, read when the copy is first seen (before any
        // shader pass); written back on a part that does not draw the mesh (see MeshValues)
        readonly Dictionary<Material, MeshValues> prefabMesh = new Dictionary<Material, MeshValues>();

        /// <summary>
        /// The mesh values (`_MeshColorMask_B` texture and scale / offset, `_MeshColor_01`) of a part material as the prefab
        /// had them. A part that does not draw the mesh (MeshOnEveryHair off and no useMesh, or a prefab without CmpHair) gets
        /// them written back after every shader pass, so its values do not depend on the shader history: a material whose
        /// prefab does not hold a value would otherwise keep the default of the first hair shader it had (hair_07 and hair_08
        /// declare different defaults), and an edit that switched the draw type would differ from a fresh build (fuzz seeds 23 /
        /// 35 with MeshOnEveryHair off). A value the prefab's shader does not declare takes the draw type's hair shader default.
        /// </summary>
        readonly struct MeshValues
        {
            readonly bool hasColor, hasTex;
            readonly Color color;
            readonly Texture tex;
            readonly Vector2 scale, offset;
            /// <summary>Bit i: the prefab material's own shader declares <see cref="ColourProps"/>[i].</summary>
            readonly int declared;

            MeshValues(bool hasColor, Color color, bool hasTex, Texture tex, Vector2 scale, Vector2 offset, int declared)
            { this.hasColor = hasColor; this.color = color; this.hasTex = hasTex; this.tex = tex; this.scale = scale; this.offset = offset; this.declared = declared; }

            public static MeshValues Of(Material m)
            {
                bool hc = m.HasProperty("_MeshColor_01"), ht = m.HasProperty("_MeshColorMask_B");
                int d = 0;
                for (int i = 0; i < ColourProps.Length; i++) if (m.HasProperty(ColourProps[i])) d |= 1 << i;
                return new MeshValues(hc, hc ? m.GetColor("_MeshColor_01") : default, ht, ht ? m.GetTexture("_MeshColorMask_B") : null,
                    ht ? m.GetTextureScale("_MeshColorMask_B") : Vector2.one, ht ? m.GetTextureOffset("_MeshColorMask_B") : Vector2.zero, d);
            }

            /// <summary>True when the prefab material's own shader declares the colour property (index in ColourProps).</summary>
            public bool Declares(int prop) => (declared & (1 << prop)) != 0;

            public void WriteTo(Material m, Shader hairShader)
            {
                if (m.HasProperty("_MeshColor_01"))
                {
                    if (hasColor) EM.SetColor(m, "_MeshColor_01", color);
                    else if (hairShader && hairShader.FindPropertyIndex("_MeshColor_01") is int i && i >= 0)
                        EM.SetColor(m, "_MeshColor_01", (Color)hairShader.GetPropertyDefaultVectorValue(i));
                }
                if (m.HasProperty("_MeshColorMask_B"))
                {
                    EM.SetTexture(m, "_MeshColorMask_B", hasTex ? tex : null);
                    EM.SetTextureScaleOffset(m, "_MeshColorMask_B", hasTex ? scale : Vector2.one, hasTex ? offset : Vector2.zero);
                }
            }
        }

        /// <summary>The hair colour properties HS2 writes before its shader pass at a load (colours, specular,
        /// metallic, smoothness, then the shader of the draw type).</summary>
        static readonly string[] ColourProps = { "_Color", "_TopColor", "_UnderColor", "_Specular", "_Metallic", "_Smoothness" };

        /// <summary>
        /// HS2-parity switch for rendHair materials whose prefab shader is not a hair shader (vanilla 300#2's shadow renderer
        /// with Unity's Default-Material / Standard; many mod materials: Standard, HairTemplateShader, ...): HS2 writes the
        /// colours before it switches the material to the hair shader, and a value of a property the old shader does not
        /// declare does not survive the switch, so such a material shows the hair shader's defaults for it (300#2's shadow:
        /// _TopColor (1, 0, 0), _UnderColor (0, 0.1045, 1), _Specular (0.155, 0.641, 0.764), _Smoothness 0; _Color and
        /// _Metallic, which Standard declares, carry the stored values) — after a load, and so in Studio; HS2's Maker shows a
        /// later colour edit until the next load. False (default) = the stored colours on every rendHair material (the unit
        /// writes them after its shader pass); true = HS2's load look (the draw type's hair shader default where the prefab's
        /// shader lacks the property). A decision (package E's compare, HC3); read at each unit run.
        /// </summary>
        public static bool ColoursBeforeShaderAsHS2;

        /// <summary>The value a colour property of a part material takes: the stored <paramref name="v"/>, or with
        /// <see cref="ColoursBeforeShaderAsHS2"/> the hair shader's default where the prefab's shader does not declare it.</summary>
        Color ColourOf(Material m, int prop, Color v, Shader hairShader)
        {
            if (!ColoursBeforeShaderAsHS2 || !hairShader || !prefabMesh.TryGetValue(m, out var rec) || rec.Declares(prop)) return v;
            int i = hairShader.FindPropertyIndex(ColourProps[prop]);
            return i >= 0 ? (Color)hairShader.GetPropertyDefaultVectorValue(i) : v;
        }

        float FloatOf(Material m, int prop, float v, Shader hairShader)
        {
            if (!ColoursBeforeShaderAsHS2 || !hairShader || !prefabMesh.TryGetValue(m, out var rec) || rec.Declares(prop)) return v;
            int i = hairShader.FindPropertyIndex(ColourProps[prop]);
            return i >= 0 ? hairShader.GetPropertyDefaultFloatValue(i) : v;
        }

        public HairUnit(int kind) : base(UnitMap.HairUnits[kind], EditStep.Parts, 10 + kind, "hair." + UnitMap.HairParts[kind], "hair.shaderType") { k = kind; }

        /// <summary>The list row the unit built last (the item, or the sex's default row when the item does not resolve); null
        /// before the first run.</summary>
        public ListEntry Row => appliedRow;

        /// <summary>The part key of a hair part's mesh pattern reference (registered with the document resolver).</summary>
        public static string MeshKey(int kind) => "chara:hair." + UnitMap.HairParts[kind] + ".mesh.item";

        public override void Apply(EditContext x)
        {
            var hair = x.Doc.Hair;
            string name = UnitMap.HairParts[k];
            var p = hair.Parts[k];
            x.Register(MeshKey(k), p.Mesh.Item, 306, "hair." + name + ".mesh.item");
            int shaderType = hair.ShaderType;
            var hairShader = HairMaterials.ShaderOf(x.Catalog, shaderType);
            var e = x.Row(300 + k, p.Item, HairProps[k], PartKeys.Hair(k), "hair." + name + ".item") ?? x.Lookup(300 + k, k == 1 ? x.S.HairFrontDefault : 0, null, null);
            string array = "hair." + name + ".materialOverrides";
            var go = x.Ctx.Hair[k];
            bool swap = !loaded || e != appliedRow || (!x.InBuild && x.MeStructureChanged(array, p.MaterialOverrides));
            if (swap)
            {
                int sibling = go ? go.transform.GetSiblingIndex() : -1;
                if (go) x.ReleasePart(go, Id); else x.ForgetMissing(Id);
                x.Ctx.Hair[k] = null;
                var parent = EditContext.Find(x.Ctx.HeadBone.transform, "N_hair_Root");
                x.BeginPartLoad();
                go = x.LoadPart(e, HairNames[k], parent);
                x.EndPartLoad(Id);
                loaded = true;
                appliedRow = e;
                x.MeLoaded(array, p.MaterialOverrides);
                appliedShader = null;
                prefabMesh.Clear();
                if (!go) { if (x.Report != null && !x.InBuild) x.Report.HairSwapped[k] = true; x.Swapped(EditObject.Hair(k)); return; }
                if (sibling >= 0) go.transform.SetSiblingIndex(sibling);
                else if (!x.InBuild) EditContext.Place(go, k, Enumerable.Range(0, 4).Select(j => (j, x.Ctx.Hair[j])));
                x.Ctx.Hair[k] = go;
                if (e["Weights"] == "2") x.Rebind(go, x.Ctx.HeadBones, "cf_J_FaceRoot", x.Ctx.Hips);
                else if (e["Weights"] == "1") x.Rebind(go, x.Ctx.BodyBones, "cf_J_Root", x.Ctx.Hips);
                if (!x.InBuild)
                {
                    x.PartSwapped(go, EditObject.Hair(k));
                    if (x.Report != null) x.Report.HairSwapped[k] = true;
                    x.MarkDynBone(true);
                    x.Editor.MarkUnit(UnitMap.ExtFinal);
                }
            }
            else if (!x.InBuild) x.MeCheckValues(array, EditObject.Hair(k), p.MaterialOverrides);
            if (!go) return;
            var cmp = go.GetComponent<CmpHair>();
            var rends = HairRenderers(go, cmp);
            bool shaderPass = swap || appliedShader != hairShader;
            Texture2D tex = null;
            if (shaderPass)
            {
                // the hair texture's bad-mip incidents are kept apart: they change with the shader type
                x.DrainBadMip(null);
                x.ForgetMissing(Id + "#tex");
                if (rends.Count > 0) tex = x.Tex(e, "TexAB", HairMaterials.TextureColumn(shaderType));   // GH8: a skipped part loads nothing
                x.DrainBadMip(Id + "#tex");
            }
            // GH1: the mesh pattern (HS2 writes it on every useMesh part, also with the pattern none)
            ResolveMesh(x, p.Mesh.Item);
            bool mesh = MeshDrawn(cmp, rends.Count);
            Texture2D mtex = mesh ? MeshTexture(x) : null;
            MeshScaleOffset(p.Mesh.Layout, out var mscale, out var moffset);
            var mcol = EditContext.A1(EditContext.U(p.Mesh.Color));
            foreach (var r in rends)
            {
                foreach (var m in x.OwnMaterials(r))
                {
                    if (!m) continue;
                    if (!prefabMesh.ContainsKey(m)) prefabMesh[m] = MeshValues.Of(m);   // the prefab's values, before the shader pass
                    // keywords kept, as in the game; the queue kept; _Ringoff from the row
                    if (shaderPass) HairMaterials.ApplyShader(m, hairShader, tex, e["RingOff"] == "1");
                    HairMaterials.ApplyColours(m,
                        ColourOf(m, 0, EditContext.A1(EditContext.U(p.BaseColor)), hairShader),
                        cmp == null || cmp.useTopColor != 0 ? ColourOf(m, 1, EditContext.A1(EditContext.U(p.TopColor)), hairShader) : (Color?)null,
                        cmp == null || cmp.useUnderColor != 0 ? ColourOf(m, 2, EditContext.A1(EditContext.U(p.UnderColor)), hairShader) : (Color?)null,
                        ColourOf(m, 3, EditContext.A1(EditContext.U(p.Specular)), hairShader),
                        FloatOf(m, 4, p.Metallic, hairShader),
                        FloatOf(m, 5, p.Smoothness, hairShader));
                    if (mesh)
                    {
                        // after the shader pass; a later URP mapping (step 8, or the scene builder's) keeps same-named values
                        EM.SetTexture(m, "_MeshColorMask_B", mtex);
                        EM.SetTextureScaleOffset(m, "_MeshColorMask_B", mscale, moffset);
                        EM.SetColor(m, "_MeshColor_01", mcol);
                    }
                    else prefabMesh[m].WriteTo(m, hairShader);   // a part that does not draw the mesh: the prefab's values
                }
            }
            if (shaderPass && !x.InBuild && hairShader && rends.Count > 0) x.Remap(go);
            appliedShader = hairShader;
            if (cmp != null && cmp.rendAccessory != null)
            {
                // GH5 / J10: every material slot, only the prefab's default-colour count, each colour its own index
                var use = new[] { cmp.useAcsColor01, cmp.useAcsColor02, cmp.useAcsColor03 };
                string[] props = { "_Color", "_Color2", "_Color3" };
                int defs = cmp.acsDefColor?.Count ?? 0;
                foreach (var r in cmp.rendAccessory.Where(r => r))
                {
                    Material[] mats = null;   // copied on the first write only
                    for (int i = 0; i < 3; i++)
                    {
                        if (use[i] == 0 || i >= defs || i >= p.AccessoryColors.Length) continue;
                        if (mats == null) mats = x.OwnMaterials(r);
                        var c = EditContext.A1(EditContext.U(p.AccessoryColors[i]));
                        foreach (var m in mats) if (m) EM.SetColor(m, props[i], c);
                    }
                }
            }
            var eff = EffectiveBundles(cmp, p);
            ApplyBundles(cmp, eff, null);
            // the corrections move the chain bones (light re-derivation at the commit of a drag); noShake is a
            // chain flag (full rebuild at once). Compared on the effective bundles, not the document's text.
            Signatures(eff, out string rates, out string shake);
            if (!x.InBuild && !swap)
            {
                if (shake != appliedShake) x.MarkDynBone(true);
                else if (rates != appliedRates)
                {
                    x.MarkDynBone(false);
                    // (UH2: the part's chains re-derive in ≤ 0.3 ms): in a drag frame the chains holding this part's
                    // chain-transform corrections are re-derived at once, so such a drag shows its edit; the deferred light
                    // re-derivation still runs at the commit
                    if (LiveChainRootDrags && x.Report != null && x.Report.Mode == FlushMode.Preview) RederiveChainRoots(x, cmp, p);
                }
            }
            appliedRates = rates;
            appliedShake = shake;
            x.Touched(EditObject.Hair(k));
            x.Log.Add($"hair{k}: {x.Name(e)}");
        }

        /// <summary>during a drag (a Preview flush) the chains of a part whose transforms include one of its bundle
        /// correction transforms are re-derived at once (UH2 measured the part's chains at ≤ 0.3 ms, 37 chains 0.25 ms).
        /// True by default; false = such chains follow only at the commit.</summary>
        public static bool LiveChainRootDrags = true;

        /// <summary>The light re-derivation of the chains of this part that hold one of its correction transforms: reset, restore,
        /// the corrections re-applied on those transforms, capture, particles at the bones (DynBoneUnit's light form, limited
        /// to these chains). Returns the number of chains re-derived.</summary>
        int RederiveChainRoots(EditContext x, CmpHair cmp, HairPart p)
        {
            var stage = x.Ctx.Motion != null ? x.Ctx.Motion.Get<HS2Next.Motion.DynamicBones.DynamicBoneStage>() : null;
            if (stage == null || cmp?.boneInfo == null) return 0;
            var trfs = new HashSet<Transform>();
            foreach (var bi in cmp.boneInfo) if (bi?.trfCorrect) trfs.Add(bi.trfCorrect);
            var chains = new List<HS2Next.Motion.DynamicBones.DynamicBoneChain>();
            var only = new HashSet<Transform>();
            foreach (var c in stage.Chains)
            {
                if (c.Category != HS2Next.Motion.DynamicBones.DynamicBoneCategory.Hair || c.Kind != k || !c.Source || !c.Root) continue;
                bool hit = false;
                for (int i = 0; i < c.ParticleCount; i++) { var t = c.ParticleTransform(i); if (t && trfs.Contains(t)) { hit = true; only.Add(t); } }
                if (hit) chains.Add(c);
            }
            if (chains.Count == 0) return 0;
            foreach (var c in chains) c.ResetAll();
            var was = new bool[chains.Count];
            for (int i = 0; i < chains.Count; i++) was[i] = chains[i].RestoreForRebuild();
            ApplyBundles(cmp, p, only);
            for (int i = 0; i < chains.Count; i++) chains[i].BuildAfterRestore(was[i]);
            foreach (var c in chains) c.ResetAll();
            return chains.Count;
        }

        /// <summary>The renderers the hair shader, colours and mesh values go to: CmpHair.rendHair's live renderers (HS2; none
        /// for an empty list, GH8 / J15); a prefab without CmpHair: every renderer without "_acs" in its name (4.0's
        /// fallback, kept).</summary>
        public static List<Renderer> HairRenderers(GameObject go, CmpHair cmp)
        {
            if (!go) return new List<Renderer>();
            if (cmp == null) return go.GetComponentsInChildren<Renderer>(true).Where(r => !r.name.Contains("_acs")).ToList();
            return cmp.rendHair == null ? new List<Renderer>() : cmp.rendHair.Where(r => r).ToList();
        }

        /// <summary>
        /// The installed plugin HairMeshforAll (package H) makes every part with a non-empty rendHair
        /// a useMesh part in HS2 (it sets CmpHair.useMesh on the instance before each mesh write). True (default; the user's
        /// choice) = follow it: the mesh values on every part with a live rendHair renderer and the mesh
        /// rows shown; false = vanilla's prefab useMesh flag. Read at each unit run.
        /// </summary>
        public static bool MeshOnEveryHair = true;

        /// <summary>
        /// HS2-parity switch for bundles whose correction transform is a dynamic-bone chain's root or inside a chain (212 mod
        /// bundles; package H's HC11: HS2's chain resets such a transform to the pose it captured at Start every frame, so an
        /// edit of these rates shows nothing while the chain runs). False (default, a decision) = the
        /// rates apply like every bundle's (the rest pose follows them; HS2's cancelled edit is a dead control);
        /// true = HS2's look: such a correction transform keeps the prefab's base pose (basePos / baseRot) whatever the rates
        /// (also while its chain is off, where HS2 shows the rates). Read at each unit run.
        /// </summary>
        public static bool ChainRootBundlesAsHS2;

        /// <summary>True when <paramref name="t"/> is the root of, or a transform inside, a dynamic-bone chain of the part
        /// (a DynamicBone under <paramref name="part"/> whose m_Root is <paramref name="t"/> or an ancestor of it).</summary>
        public static bool IsChainTransform(GameObject part, Transform t)
        {
            if (!part || !t) return false;
            foreach (var db in part.GetComponentsInChildren<global::DynamicBone>(true))
            {
                var root = db ? db.m_Root : null;
                if (root && (root == t || t.IsChildOf(root))) return true;
            }
            return false;
        }

        /// <summary>The unit writes the mesh values of a part (and the Maker shows its mesh rows): a CmpHair with a live rendHair
        /// renderer and useMesh (or <see cref="MeshOnEveryHair"/>).</summary>
        public static bool MeshDrawn(CmpHair cmp, int liveHairRenderers) => cmp != null && liveHairRenderers > 0 && (cmp.useMesh != 0 || MeshOnEveryHair);

        /// <summary>The mesh texture scale / offset of a stored layout (x, y, z, w): scale (lerp(0.5, 1, x), lerp(0.5, 1, y)),
        /// offset (lerp(0.5, 1.5, z), lerp(0.5, 1.5, w)), Mathf.Lerp (clamped; HS2 + the slider extender).</summary>
        public static void MeshScaleOffset(Vec4 layout, out Vector2 scale, out Vector2 offset)
        {
            scale = new Vector2(Mathf.Lerp(0.5f, 1f, layout.X), Mathf.Lerp(0.5f, 1f, layout.Y));
            offset = new Vector2(Mathf.Lerp(0.5f, 1.5f, layout.Z), Mathf.Lerp(0.5f, 1.5f, layout.W));
        }

        /// <summary>The 306 row of the part's mesh reference, resolved once per reference on every part (an unresolved
        /// reference is reported by the resolver as missing content, whatever the prefab; the texture is loaded only for a
        /// part that draws it, <see cref="MeshTexture"/>).</summary>
        void ResolveMesh(EditContext x, string reference)
        {
            if (meshResolved && reference == meshRef) return;
            meshRef = reference;
            meshResolved = true;
            meshLoaded = false;
            meshTex = null;
            meshRow = x.Lookup(306, EditContext.RawId(reference), null, MeshKey(k));
        }

        /// <summary>The texture of the resolved 306 row (MainAB / MainTex; a bundle asset of the catalog, not owned), cached per
        /// reference; null for the none row (MainAB "0") and for a reference that does not resolve.</summary>
        Texture2D MeshTexture(EditContext x)
        {
            if (meshLoaded && (meshTex || meshRow == null || !IsTexture(meshRow["MainAB"]))) return meshTex;
            meshLoaded = true;
            meshTex = null;
            x.DrainBadMip(null);
            x.ForgetMissing(Id + "#mesh");
            if (meshRow != null && IsTexture(meshRow["MainAB"]) && IsTexture(meshRow["MainTex"])) meshTex = x.Tex(meshRow, "MainAB", "MainTex");
            x.DrainBadMip(Id + "#mesh");
            return meshTex;
        }

        static bool IsTexture(string v) => !string.IsNullOrEmpty(v) && v != "0";

        // ---------------------------------------------------------------- bundles

        /// <summary>One bundle as applied: its rates (stored, or the prefab defaults) and noShake.</summary>
        public readonly struct Effective
        {
            public readonly Vector3 Move, Rotate;
            public readonly bool NoShake;
            /// <summary>True when the values come from the stored list (the count rule holds).</summary>
            public readonly bool Stored;
            public Effective(Vector3 move, Vector3 rotate, bool noShake, bool stored) { Move = move; Rotate = rotate; NoShake = noShake; Stored = stored; }
        }

        /// <summary>True when the stored list applies (HS2's load rule, J6): exactly <paramref name="count"/> entries whose
        /// indices are 0..count−1 (each once).</summary>
        public static bool StoredValid(HairPart part, int count)
        {
            var l = part?.Bundles;
            int n = l?.Count ?? 0;
            if (n != count) return false;
            if (n == 0) return true;
            var seen = new bool[n];
            foreach (var b in l)
            {
                if (b == null || b.Index < 0 || b.Index >= n || seen[b.Index]) return false;
                seen[b.Index] = true;
            }
            return true;
        }

        /// <summary>The prefab default rates of one bundle: InverseLerp(min, max, base) per axis (equal limits → 0).</summary>
        public static void DefaultRates(CmpHair.BoneInfo_S bi, out Vector3 move, out Vector3 rotate)
        {
            if (bi == null) { move = rotate = Vector3.zero; return; }
            move = new Vector3(EditContext.Inv(bi.posMin.x, bi.posMax.x, bi.basePos.x), EditContext.Inv(bi.posMin.y, bi.posMax.y, bi.basePos.y), EditContext.Inv(bi.posMin.z, bi.posMax.z, bi.basePos.z));
            rotate = new Vector3(EditContext.Inv(bi.rotMin.x, bi.rotMax.x, bi.baseRot.x), EditContext.Inv(bi.rotMin.y, bi.rotMax.y, bi.baseRot.y), EditContext.Inv(bi.rotMin.z, bi.rotMax.z, bi.baseRot.z));
        }

        /// <summary>The effective bundles of a part (one per CmpHair.boneInfo entry, index order): the stored entry b when the
        /// stored list is valid (<see cref="StoredValid"/>), else the prefab defaults with noShake off. Empty without a
        /// CmpHair.</summary>
        public static Effective[] EffectiveBundles(CmpHair cmp, HairPart part)
        {
            int n = cmp?.boneInfo?.Count ?? 0;
            var r = new Effective[n];
            if (n == 0) return r;
            bool valid = StoredValid(part, n);
            HairBundle[] byIndex = null;
            if (valid) { byIndex = new HairBundle[n]; foreach (var b in part.Bundles) byIndex[b.Index] = b; }
            for (int i = 0; i < n; i++)
            {
                if (valid) { var d = byIndex[i]; r[i] = new Effective(EditContext.U(d.Move), EditContext.U(d.Rotate), d.NoShake, true); continue; }
                DefaultRates(cmp.boneInfo[i], out var mv, out var rr);
                r[i] = new Effective(mv, rr, false, false);
            }
            return r;
        }

        /// <summary>The effective bundles of a realized part object (its CmpHair) for the part's document.</summary>
        public static Effective[] EffectiveBundles(GameObject part, HairPart doc) => EffectiveBundles(part ? part.GetComponent<CmpHair>() : null, doc);

        /// <summary>The effective noShake of bundle <paramref name="index"/> (false without a valid stored list).</summary>
        public static bool EffectiveNoShake(CmpHair cmp, HairPart part, int index)
        {
            int n = cmp?.boneInfo?.Count ?? 0;
            if (index < 0 || index >= n || !StoredValid(part, n)) return false;
            foreach (var b in part.Bundles) if (b.Index == index) return b.NoShake;
            return false;
        }

        static void Signatures(Effective[] eff, out string rates, out string shake)
        {
            var sr = new System.Text.StringBuilder(eff.Length * 48);
            var ss = new System.Text.StringBuilder(eff.Length);
            foreach (var b in eff)
            {
                foreach (var f in new[] { b.Move.x, b.Move.y, b.Move.z, b.Rotate.x, b.Rotate.y, b.Rotate.z })
                    sr.Append(BitConverter.SingleToInt32Bits(f).ToString("x8")).Append(',');
                ss.Append(b.NoShake ? '1' : '0');
            }
            rates = sr.ToString();
            shake = ss.ToString();
        }

        /// <summary>Writes the correction transforms of a part from its document (the effective bundles); with
        /// <paramref name="only"/> just the correction transforms in that set (the chain step's re-application).</summary>
        public static void ApplyBundles(CmpHair cmp, HairPart part, HashSet<Transform> only = null) => ApplyBundles(cmp, EffectiveBundles(cmp, part), only);

        static void ApplyBundles(CmpHair cmp, Effective[] eff, HashSet<Transform> only)
        {
            if (cmp?.boneInfo == null) return;
            global::DynamicBone[] chains = ChainRootBundlesAsHS2 ? cmp.GetComponentsInChildren<global::DynamicBone>(true) : null;
            for (int i = 0; i < cmp.boneInfo.Count && i < eff.Length; i++)
            {
                var bi = cmp.boneInfo[i];
                if (bi?.trfCorrect == null) continue;
                if (only != null && !only.Contains(bi.trfCorrect)) continue;
                if (chains != null && chains.Any(db => db && db.m_Root && (db.m_Root == bi.trfCorrect || bi.trfCorrect.IsChildOf(db.m_Root))))
                {
                    // ChainRootBundlesAsHS2: HS2's look (the chain keeps its Start pose = the prefab's base pose)
                    bi.trfCorrect.localPosition = bi.basePos;
                    bi.trfCorrect.localEulerAngles = bi.baseRot;
                    continue;
                }
                Vector3 mv = eff[i].Move, rr = eff[i].Rotate;
                // Rates outside 0..1 (typed values, slider-extender cards) extrapolate with ShapeEngine.SliderRange = Extrapolate,
                // as the extender's unclamped lerp in CmpHair.Update; Clamp = vanilla Mathf.Lerp.
                Func<float, float, float, float> lerp = ShapeEngine.RangeLerp;
                bi.trfCorrect.localPosition = new Vector3(lerp(bi.posMin.x, bi.posMax.x, mv.x), lerp(bi.posMin.y, bi.posMax.y, mv.y), lerp(bi.posMin.z, bi.posMax.z, mv.z));
                bi.trfCorrect.localEulerAngles = new Vector3(lerp(bi.rotMin.x, bi.rotMax.x, rr.x), lerp(bi.rotMin.y, bi.rotMax.y, rr.y), lerp(bi.rotMin.z, bi.rotMax.z, rr.z));
            }
        }

        /// <summary>The stored bundle with index <paramref name="i"/> (the later one of duplicates, as the adapter tree had it);
        /// not the count rule (see <see cref="EffectiveBundles(CmpHair, HairPart)"/>).</summary>
        public static HairBundle BundleOf(HairPart part, int i)
        {
            HairBundle r = null;
            if (part?.Bundles != null) foreach (var b in part.Bundles) if (b != null && b.Index == i) r = b;
            return r;
        }
    }

    /// <summary>Canonical text of document values for change detection (the members that need a part reload; of a material array, its
    /// values: its structure is MeStructure's).</summary>
    public static class Signature
    {
        public static string Of(System.Collections.Generic.IEnumerable<DocObject> l)
        {
            if (l == null) return "";
            return Text(w =>
            {
                w.StartArray();
                foreach (var o in l) { if (o == null) w.Null(); else o.WriteTo(w); }
                w.EndArray();
            });
        }

        public static string Of(DocObject o) => o == null ? "" : Text(w => o.WriteTo(w));

        static string Text(Action<CanonicalJsonWriter> body)
        {
            try { return System.Text.Encoding.UTF8.GetString(CanonicalJsonWriter.ToBytes(null, body)); }
            catch (Exception e) { return "unwritable " + e.GetType().Name + " " + e.Message; }
        }
    }
}

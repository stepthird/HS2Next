// The mosaic unit (package R of 5.M): the body of the mosaic stage
// slot (MosaicStageUnit, step 5 after the plugin texture stage: KSOX, KCOX, MaterialEditor; before the shapes and the plugin
// final stage). It runs in the build and in every edit flush that ran an earlier step (a part, a composite, a material, the
// plugin textures), so an edit equals a fresh build (S2). Each run:
//   1 patch guard (females): a body without o_mnpa / o_mnpb (a replaced body that lacks them) gets them from the vanilla
//     body prefab of abdata (not through the per-asset redirect), under o_body_cf/N_mnpb, bound by name to the body bones
//     (finding mosaic.patchesRestored; none on this install's bodies);
//   2 the members (MosaicSet) under the character root, inactive objects included;
//   3 per member: body-surface members (o_mnpa, o_mnpb: every vertex within 0.05 units of the body) draw the body draw
//     material's current _MainTex (the composite after KSOX / MaterialEditor) at the transferred body UVs (the mesh copy
//     "<mesh>#bodyuv", MosaicUvTransfer) in texture mode; a member whose original material has a main texture draws that
//     texture at its own UV0; every other member (the vanilla genital meshes) draws the procedural mode;
//   4 every slot of every member gets its mosaic material (shader HS2Next/Mosaic, queue 2500, the original material's
//     name and _WhiteOff), whatever the prefab, a replaced body or MaterialEditor put there; the material is the
//     character's own (OwnedMaterials, so the ownership step keeps it) and is kept across runs (values rewritten).
// The block and the cell are per camera (MosaicCamera, property blocks); the materials keep fixed defaults (S1 holds the
// same text for every run). Visibility (the son toggle, clothes states, InvisibleBody, the silhouette, N_mnpb's activity)
// is never changed here.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HS2Next.Chara.Edit;
using HS2Next.Save;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HS2Next.Chara.Edit.Units
{
    public sealed partial class MosaicStageUnit
    {
        partial void ApplyMosaic(EditContext x) => HS2Next.Chara.Mosaic.MosaicUnit.Apply(x);
    }
}

namespace HS2Next.Chara.Mosaic
{
    public static class MosaicUnit
    {
        /// <summary>The material default of the per-camera block (UV) until a camera sets it.</summary>
        public const float DefaultBlockUv = 1f / 32f;

        /// <summary>What a mosaic material was made from (kept per material across runs).</summary>
        sealed class SlotOrigin
        {
            public string Name;
            public float WhiteOff = 1f;
            public Texture MainTex;
            public Vector4 MainSt = new Vector4(1, 1, 0, 0);
            public Color Color = Color.white;
        }

        static readonly ConditionalWeakTable<Material, SlotOrigin> origins = new ConditionalWeakTable<Material, SlotOrigin>();
        static Shader shader;

        /// <summary>The mosaic shader (null when it is not in the build: then nothing is changed and the run logs it).</summary>
        public static Shader Shader => shader ? shader : shader = Shader.Find(MosaicSet.ShaderName);

        /// <summary>Total runs of the stage (all characters; tests).</summary>
        public static int Runs { get; private set; }

        /// <summary>True when <paramref name="m"/> is a material the mosaic unit made.</summary>
        public static bool IsUnitMaterial(Material m) => m && origins.TryGetValue(m, out _);

        // ---------------------------------------------------------------- the stage

        public static void Apply(EditContext x)
        {
            var ctx = x?.Ctx;
            if (ctx == null || !ctx.Root) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var st = MosaicSet.StateFor(ctx);
            st.Findings = new FindingList();
            Runs++;
            st.Runs++;
            if (!Shader)
            {
                st.Findings.Error("mosaic.shaderMissing", "", MosaicSet.ShaderName + " is not in the build: the members keep their materials");
                x.Log?.Add("mosaic: " + MosaicSet.ShaderName + " not found");
                return;
            }
            st.RestoredRenderers.RemoveWhere(r => !r);
            st.Restored = x.Male ? 0 : RestorePatches(x, st);
            var found = new List<Renderer>();
            foreach (var r in ctx.Root.GetComponentsInChildren<Renderer>(true))
                if (MosaicSet.IsMember(r)) found.Add(r);
            var old = st.Members.Where(m => m.Renderer).ToDictionary(m => m.Renderer);
            st.Members.Clear();
            int tex = 0, proc = 0;
            foreach (var r in found)
            {
                old.TryGetValue(r, out var m);
                m = ApplyMember(x, st, r, m ?? new MosaicMember { Renderer = r });
                st.Members.Add(m);
                if (m.Mode == MosaicMode.Texture) tex++; else proc++;
            }
            st.LastMilliseconds = sw.Elapsed.TotalMilliseconds;
            x.Log?.Add($"mosaic: {st.Members.Count} members ({tex} texture, {proc} procedural){(st.Restored > 0 ? $", {st.Restored} patches restored" : "")}, {st.LastMilliseconds:F2} ms");
        }

        /// <summary>A renderer added (E7) to a character between flushes: its materials now (the next flush runs the stage).</summary>
        internal static void ApplyAdded(MosaicState st, Renderer r)
        {
            var x = st.Chara?.Edit;
            if (x == null || !Shader) return;
            var m = st.Members.Find(a => a.Renderer == r);
            if (m == null) st.Members.Add(m = new MosaicMember { Renderer = r });
            m.Added = true;
            ApplyMember(x, st, r, m);
        }

        /// <summary>
        /// The re-assert after a plugin's final-stage hook (<paramref name="hookId"/>): every member slot whose material is not
        /// the mosaic material the stage gave it gets that material back (the member keeps its other state: mesh, bones,
        /// visibility), and a renderer that became a member after the stage ran joins the record with its mosaic material.
        /// Each one is a warning api.mosaicProtected (the hook id and the member path) in the character's mosaic findings and a
        /// line in its build log. Returns the member paths restored (empty when nothing changed or the stage never ran here).
        /// Only plugin hooks call it: the official build is unchanged.
        /// </summary>
        public static List<string> Reassert(CharaContext ctx, string hookId)
        {
            var restored = new List<string>();
            var st = ctx != null && ctx.Root ? MosaicSet.StateOf(ctx) : null;
            if (st == null) return restored;
            foreach (var m in st.Members)
            {
                var r = m.Renderer;
                if (!r || m.Materials == null || m.Materials.Length == 0) continue;
                var cur = r.sharedMaterials;
                bool differs = cur.Length != m.Materials.Length;
                for (int i = 0; !differs && i < cur.Length; i++)
                    if (m.Materials[i] && cur[i] != m.Materials[i]) differs = true;
                if (!differs) continue;
                var mats = new Material[m.Materials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = m.Materials[i] ? m.Materials[i] : i < cur.Length ? cur[i] : null;
                r.sharedMaterials = mats;
                restored.Add(m.Path);
            }
            foreach (var r in ctx.Root.GetComponentsInChildren<Renderer>(true))
            {
                if (!MosaicSet.IsMember(r) || st.Members.Exists(a => a.Renderer == r)) continue;
                int n = st.Members.Count;
                Adopt(st, r);
                if (st.Members.Count > n) restored.Add(st.Members[st.Members.Count - 1].Path);
            }
            foreach (var p in restored)
            {
                st.Findings.Warning(MosaicSet.ProtectedCode, p, "hook " + hookId + " left a mosaic member without its mosaic material; put back");
                ctx.Log?.Add("mosaic: re-asserted " + p + " after hook " + hookId);
            }
            return restored;
        }

        /// <summary>A member of a character that appeared after the stage ran (the silhouette's simple body, mapped by the
        /// shader map when it is first built): it joins the record now, as the next run would take it.</summary>
        internal static void Adopt(MosaicState st, Renderer r)
        {
            var x = st.Chara?.Edit;
            if (x == null || !Shader || st.Members.Exists(a => a.Renderer == r)) return;
            st.Members.Add(ApplyMember(x, st, r, new MosaicMember { Renderer = r }));
        }

        static MosaicMember ApplyMember(EditContext x, MosaicState st, Renderer r, MosaicMember m)
        {
            var ctx = x.Ctx;
            m.Renderer = r;
            m.Path = PathOf(ctx.Root.transform, r.transform);
            m.Added = m.Added || MosaicSet.IsAdded(r);
            var mats = r.sharedMaterials;
            var slotOrigins = new SlotOrigin[mats.Length];
            for (int i = 0; i < mats.Length; i++) slotOrigins[i] = OriginOf(mats[i]);

            // the source: the body composite at transferred body UVs, the part's own main texture, or none
            var smr = r as SkinnedMeshRenderer;
            var body = ctx.BodyRenderer;
            MosaicTransfer t = null;
            // body-surface candidates: skinned members of the body object (not the silhouette's simple body, not clothes)
            bool candidate = smr && body && smr != body && !x.Male && ctx.Body && r.transform.IsChildOf(ctx.Body.transform);
            if (candidate) t = MosaicUvTransfer.Transfer(smr, body);
            if (candidate && t == null) st.Findings.Info(MosaicSet.TransferCode, m.Path, "the body or the member mesh is not readable: procedural mosaic");
            var own = slotOrigins.FirstOrDefault(o => o != null && o.MainTex);
            var draw = ctx.BodyDraw;
            if (t != null && t.BodySurface && draw && draw.HasProperty("_MainTex") && draw.GetTexture("_MainTex"))
            {
                m.Mode = MosaicMode.Texture;
                m.SourceKind = "body";
                m.Source = draw.GetTexture("_MainTex");
                m.BodySurface = true;
                if (smr.sharedMesh != t.Copy) smr.sharedMesh = t.Copy;
                m.Region = t.Region;
            }
            else
            {
                // a member that is not (or no longer) a body-surface member draws its own mesh
                if (smr && MosaicUvTransfer.OfCopy(smr.sharedMesh) is MosaicTransfer was) smr.sharedMesh = was.Source;
                m.BodySurface = false;
                var mesh = MeshOf(r);
                m.Region = MosaicUvTransfer.OwnRegion(mesh) ?? new MosaicRegion();
                if (own != null)
                {
                    m.Mode = MosaicMode.Texture;
                    m.SourceKind = "own";
                    m.Source = own.MainTex;
                }
                else
                {
                    m.Mode = MosaicMode.Procedural;
                    m.SourceKind = null;
                    m.Source = null;
                }
            }
            CacheRegionBones(m, r);

            // every slot: its mosaic material (kept across runs)
            bool changed = false;
            var outMats = new Material[mats.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                var o = slotOrigins[i];
                if (o == null) { outMats[i] = mats[i]; continue; }
                var mat = mats[i] && origins.TryGetValue(mats[i], out _) ? mats[i] : null;
                if (!mat)
                {
                    mat = new Material(Shader) { name = o.Name, hideFlags = HideFlags.DontSave };
                    origins.Add(mat, o);
                    changed = true;
                }
                Fill(mat, m, o, m.SourceKind == "body" ? draw : null);
                x.Owned.Add(mat);
                outMats[i] = mat;
            }
            if (changed) r.sharedMaterials = outMats;
            m.Materials = outMats;
            m.Restored = st.RestoredRenderers.Contains(r);
            return m;
        }

        /// <summary>The origin of a slot: a mosaic material made here keeps its recorded origin; any other material is read.</summary>
        static SlotOrigin OriginOf(Material mat)
        {
            if (!mat) return null;
            if (origins.TryGetValue(mat, out var o)) return o;
            o = new SlotOrigin { Name = mat.name };
            if (mat.HasProperty("_WhiteOff")) o.WhiteOff = mat.GetFloat("_WhiteOff");
            if (mat.HasProperty("_MainTex"))
            {
                o.MainTex = mat.GetTexture("_MainTex");
                var sc = mat.GetTextureScale("_MainTex"); var of = mat.GetTextureOffset("_MainTex");
                o.MainSt = new Vector4(sc.x, sc.y, of.x, of.y);
            }
            if (mat.HasProperty("_Color")) o.Color = mat.GetColor("_Color");
            return o;
        }

        static void Fill(Material mat, MosaicMember m, SlotOrigin o, Material draw)
        {
            mat.shaderKeywords = Array.Empty<string>();
            mat.renderQueue = MosaicSet.Queue;
            mat.SetFloat("_WhiteOff", o.WhiteOff);
            mat.SetFloat("_MosaicMode", (float)m.Mode);
            mat.SetFloat("_MosaicBlockUv", DefaultBlockUv);
            mat.SetFloat("_MosaicCellPx", 0f);
            Texture main = null, detail = null;
            Vector4 mainSt = new Vector4(1, 1, 0, 0), detailSt = new Vector4(1, 1, 0, 0);
            Color color = Color.white;
            float gloss = 0.55f, metal = 0.03f;
            if (m.Mode == MosaicMode.Texture && draw)
            {
                main = draw.GetTexture("_MainTex");
                mainSt = St(draw, "_MainTex");
                if (draw.HasProperty("_DetailMainTex")) { detail = draw.GetTexture("_DetailMainTex"); detailSt = St(draw, "_DetailMainTex"); }
                if (draw.HasProperty("_Color")) color = draw.GetColor("_Color");
                if (draw.HasProperty("_Gloss")) gloss = draw.GetFloat("_Gloss");
                if (draw.HasProperty("_Metallic")) metal = draw.GetFloat("_Metallic");
            }
            else if (m.Mode == MosaicMode.Texture)
            {
                main = o.MainTex;
                mainSt = o.MainSt;
                color = o.Color;
            }
            mat.SetTexture("_MainTex", main);
            mat.SetTextureScale("_MainTex", new Vector2(mainSt.x, mainSt.y));
            mat.SetTextureOffset("_MainTex", new Vector2(mainSt.z, mainSt.w));
            mat.SetTexture("_DetailMainTex", detail);
            mat.SetTextureScale("_DetailMainTex", new Vector2(detailSt.x, detailSt.y));
            mat.SetTextureOffset("_DetailMainTex", new Vector2(detailSt.z, detailSt.w));
            mat.SetColor("_Color", color);
            mat.SetFloat("_Gloss", gloss);
            mat.SetFloat("_Metallic", metal);
        }

        static Vector4 St(Material m, string p)
        {
            var s = m.GetTextureScale(p); var o = m.GetTextureOffset(p);
            return new Vector4(s.x, s.y, o.x, o.y);
        }

        static Mesh MeshOf(Renderer r) =>
            r is SkinnedMeshRenderer s ? s.sharedMesh : r.TryGetComponent<MeshFilter>(out var f) ? f.sharedMesh : null;

        static void CacheRegionBones(MosaicMember m, Renderer r)
        {
            m.RegionBones = Array.Empty<Transform>();
            m.RegionBind = Array.Empty<Matrix4x4>();
            m.RegionWeights = Array.Empty<float>();
            if (!(r is SkinnedMeshRenderer smr) || m.Region == null || m.Region.Bones.Length == 0 || !smr.sharedMesh) return;
            var bones = smr.bones;
            var bind = smr.sharedMesh.bindposes;
            var bl = new List<Transform>(); var pl = new List<Matrix4x4>(); var wl = new List<float>();
            for (int k = 0; k < m.Region.Bones.Length; k++)
            {
                int i = m.Region.Bones[k];
                if (i < 0 || i >= bones.Length || i >= bind.Length || !bones[i]) continue;
                bl.Add(bones[i]); pl.Add(bind[i]); wl.Add(m.Region.Weights[k]);
            }
            float s = wl.Sum();
            m.RegionBones = bl.ToArray();
            m.RegionBind = pl.ToArray();
            m.RegionWeights = wl.Select(w => s > 0 ? w / s : 0f).ToArray();
        }

        static string PathOf(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (var c = t; c && c != root; c = c.parent) parts.Add(c.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        // ---------------------------------------------------------------- standalone members

        /// <summary>
        /// A member outside any character record: a renderer added through MosaicSet.Add (its slots get mosaic materials now:
        /// texture mode with its own main texture, else procedural) or a renderer the shader map swapped to the mosaic shader
        /// (kept as it is; the camera step gives it the per-camera cell or block).
        /// </summary>
        internal static MosaicMember Standalone(Renderer r, bool added)
        {
            var m = new MosaicMember { Renderer = r, Path = r.name, Added = added };
            var mats = r.sharedMaterials;
            var o = mats.Select(OriginOf).ToArray();
            var own = o.FirstOrDefault(x => x != null && x.MainTex && !MosaicSet.IsMosaicMaterial(mats[Array.IndexOf(o, x)]));
            m.Region = MosaicUvTransfer.OwnRegion(MeshOf(r)) ?? new MosaicRegion();
            CacheRegionBones(m, r);
            var anyMosaic = mats.FirstOrDefault(MosaicSet.IsMosaicMaterial);
            if (!added || !Shader)
            {
                m.Mode = anyMosaic && anyMosaic.HasProperty("_MosaicMode") && anyMosaic.GetFloat("_MosaicMode") > 0.5f ? MosaicMode.Texture : MosaicMode.Procedural;
                m.Materials = mats;
                return m;
            }
            m.Mode = own != null ? MosaicMode.Texture : MosaicMode.Procedural;
            m.SourceKind = own != null ? "own" : null;
            m.Source = own?.MainTex;
            var outMats = new Material[mats.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                if (o[i] == null) { outMats[i] = mats[i]; continue; }
                var mat = origins.TryGetValue(mats[i], out _) ? mats[i] : new Material(Shader) { name = o[i].Name, hideFlags = HideFlags.DontSave };
                if (mat != mats[i]) origins.Add(mat, o[i]);
                Fill(mat, m, own != null ? new SlotOrigin { Name = o[i].Name, WhiteOff = o[i].WhiteOff, MainTex = own.MainTex, MainSt = own.MainSt, Color = own.Color } : o[i], null);
                outMats[i] = mat;
            }
            r.sharedMaterials = outMats;
            m.Materials = outMats;
            return m;
        }

        // ---------------------------------------------------------------- patch guard

        /// <summary>The vanilla female patches put back where the body lacks them; returns how many renderers were added.</summary>
        static int RestorePatches(EditContext x, MosaicState st)
        {
            var ctx = x.Ctx;
            var bodyGo = ctx.Body;
            var bodyR = ctx.BodyRenderer;
            if (!bodyGo || !bodyR) return 0;
            bool HasRenderer(string name) => EditContext.Find(bodyGo.transform, name) is Transform t && t.GetComponent<Renderer>();
            var missing = new[] { "o_mnpa", "o_mnpb" }.Where(n => !HasRenderer(n)).ToList();
            if (missing.Count == 0) return 0;
            GameObject prefab = null;
            try
            {
                var bundle = ctx.Catalog?.LoadBundle(null, "chara/oo_base.unity3d", null);
                prefab = bundle ? bundle.LoadAsset<GameObject>(x.S.BodyPrefab) : null;
            }
            catch (Exception e) { x.Log?.Add("mosaic: vanilla body not readable: " + e.Message); }
            var srcN = prefab ? EditContext.Find(prefab.transform, "N_mnpb") : null;
            if (!srcN)
            {
                st.Findings.Error(MosaicSet.RestoredCode, "", "the body lacks " + string.Join(", ", missing) + " and the vanilla patches are not available");
                return 0;
            }
            var n = EditContext.Find(bodyGo.transform, "N_mnpb");
            var parent = bodyR.transform;
            var made = new List<GameObject>();
            if (!n)
            {
                var go = Object.Instantiate(srcN.gameObject, parent, false);
                go.name = "N_mnpb";
                // keep only the missing patches (a body may have one of them elsewhere)
                foreach (Transform c in go.transform.Cast<Transform>().ToList())
                    if (c.GetComponent<Renderer>() && !missing.Contains(c.name)) Object.DestroyImmediate(c.gameObject);
                made.Add(go);
            }
            else
                foreach (var name in missing)
                {
                    var src = EditContext.Find(srcN, name);
                    if (!src) continue;
                    var go = Object.Instantiate(src.gameObject, n, false);
                    go.name = name;
                    made.Add(go);
                }
            int count = 0;
            foreach (var go in made)
            {
                x.Rebind(go, ctx.BodyBones, "cf_J_Root", ctx.Hips);
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = bodyGo.layer;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    st.RestoredRenderers.Add(r);
                    r.renderingLayerMask = bodyR.renderingLayerMask;
                    if (r is SkinnedMeshRenderer s && x.ForceMatrixRecalculation) s.forceMatrixRecalculationPerRender = true;
                    count++;
                }
                if (x.Report != null) x.Report.NewObjects.Add(go);
            }
            st.Findings.Warning(MosaicSet.RestoredCode, "", "restored " + string.Join(", ", missing) + " from the vanilla body");
            x.Log?.Add("mosaic: patches restored (" + string.Join(", ", missing) + ")");
            return count;
        }
    }
}

// Chara to Item: insides (V9, V14). A renderer marked "fill" (a part bake without the body or
// the head) or "section" (a cut renderer) draws a reversed copy 1 mm inside: triangles turned round, normals negated, tangents' w
// negated, vertices moved along their normals by −0.01 world units converted to mesh units through each vertex's bind matrix at the
// bake's pose. Materials: for "fill", skin (a shader name containing "Skin") draws its stored flat colour with HS2Next/Standard
// (glossiness 0.15, metallic 0), everything else its own material; for "section", the bake's section colour flat and unlit
// (HS2Next/Unlit/Color), or the own material for garments and hair when "own fabric inside" is on. Not copied: shaders whose name
// contains Eye, namida, eyelash, eyeshadow, Liquid or Glass; the renderers named o_tooth, o_tang, o_eyebase_L, o_eyebase_R,
// o_eyelashes, o_eyeshadow, o_namida, o_mayuge, o_eyebrow; slots in a queue above 2500 or of RenderType Transparent; sub-meshes that
// are not triangles; members. The copy casts no shadow and keeps the outside's probes, anchor and layer. Copies are made when the
// prototype is built (not stored).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HS2Next.Scenes.Bake
{
    public static class BakeInsides
    {
        /// <summary>The inset in world units (1 mm).</summary>
        public const float Inset = 0.01f;
        public const string Suffix = "~inside";

        static readonly string[] ShaderWords = { "Eye", "namida", "eyelash", "eyeshadow", "Liquid", "Glass" };
        static readonly HashSet<string> RendererNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "o_tooth", "o_tang", "o_eyebase_L", "o_eyebase_R", "o_eyelashes", "o_eyeshadow", "o_namida", "o_mayuge", "o_eyebrow",
        };

        /// <summary>A renderer that may get an inside copy (a member, an excluded name, or no eligible slot: false).</summary>
        public static bool Eligible(BakeData b, BakeData.RendData r)
        {
            if (r.Member) return false;
            if (r.Node < 0 || r.Node >= b.Nodes.Count || RendererNames.Contains(b.Nodes[r.Node].Name)) return false;
            if (r.Mesh < 0 || r.Mesh >= b.Meshes.Count) return false;
            for (int k = 0; k < r.Materials.Length; k++) if (SlotEligible(b, r.Materials[k])) return true;
            return false;
        }

        /// <summary>A slot's material may be drawn inside.</summary>
        public static bool SlotEligible(BakeData b, int mat)
        {
            if (mat < 0 || mat >= b.Materials.Count) return false;
            var m = b.Materials[mat];
            if (m.Queue > 2500 || m.RenderType == "Transparent") return false;
            foreach (var w in ShaderWords) if (m.Shader != null && m.Shader.IndexOf(w, StringComparison.Ordinal) >= 0) return false;
            return true;
        }

        static bool IsSkin(BakeData.MatData m) => m.Shader != null && m.Shader.IndexOf("Skin", StringComparison.Ordinal) >= 0;

        static bool OwnFabric(BakeData b, BakeData.RendData r) =>
            b.Insides.OwnInside && r.Part != null && (r.Part.StartsWith("clothes", StringComparison.Ordinal) || r.Part.StartsWith("hair", StringComparison.Ordinal));

        // ------------------------------------------------------------ the copy's mesh (Unity-free arithmetic on the data)

        /// <summary>
        /// The reversed copy of a mesh: the sub-meshes kept (<paramref name="keep"/>, triangles only) with their triangles turned round,
        /// normals negated, tangents' w negated, positions moved by −inset along the normal, inset converted from world to mesh units by
        /// <paramref name="worldPerUnit"/> (the length in world of the vertex's normal through its bind matrix). Null when no sub-mesh is kept.
        /// </summary>
        public static MeshData Reversed(MeshData src, bool[] keep, Func<int, float> worldPerUnit)
        {
            var subs = new List<int>();
            for (int s = 0; s < src.Idx.Length; s++) if (src.Topo[s] == MeshTopology.Triangles && (keep == null || (s < keep.Length && keep[s]))) subs.Add(s);
            if (subs.Count == 0 || src.N == null) return null;
            var d = new MeshData { Name = src.Name + Suffix, Route = "inside" };
            int n = src.V.Length;
            d.V = new Vector3[n];
            d.N = new Vector3[n];
            for (int k = 0; k < n; k++)
            {
                var nn = src.N[k];
                float s = worldPerUnit(k);
                float inset = s > 1e-12f ? Inset / s : 0f;
                d.V[k] = src.V[k] - nn * inset;
                d.N[k] = -nn;
            }
            if (src.T != null)
            {
                d.T = new Vector4[n];
                for (int k = 0; k < n; k++) d.T[k] = new Vector4(src.T[k].x, src.T[k].y, src.T[k].z, -src.T[k].w);
            }
            for (int ch = 0; ch < 8; ch++) { d.Uv[ch] = src.Uv[ch]; d.UvDim[ch] = src.UvDim[ch]; }
            d.C = src.C;
            d.W = src.W;
            d.Bp = src.Bp;
            d.Idx = new int[subs.Count][];
            d.Topo = new MeshTopology[subs.Count];
            for (int i = 0; i < subs.Count; i++)
            {
                var a = src.Idx[subs[i]];
                var o = new int[a.Length - a.Length % 3];
                for (int t = 0; t + 2 < a.Length; t += 3) { o[t] = a[t]; o[t + 1] = a[t + 2]; o[t + 2] = a[t + 1]; }
                d.Idx[i] = o;
                d.Topo[i] = MeshTopology.Triangles;
            }
            return d;
        }

        /// <summary>World length per mesh unit along each vertex's normal at the prototype's pose: through the vertex's blended bind matrix
        /// (bones' localToWorld × bind poses) for a skinned mesh, through the node's localToWorld for an unskinned one.</summary>
        public static Func<int, float> Scales(MeshData md, Transform node, Transform[] bones, Matrix4x4 rootW2L)
        {
            var toRoot = BakeMeshes.D34.Of(rootW2L);
            if (md.W != null && md.Bp != null && bones != null)
            {
                var skin = new BakeMeshes.D34[Math.Min(md.Bp.Length, bones.Length)];
                for (int i = 0; i < skin.Length; i++)
                    skin[i] = BakeMeshes.D34.Mul(BakeMeshes.D34.Mul(toRoot, BakeMeshes.D34.Of(bones[i] ? bones[i].localToWorldMatrix : node.localToWorldMatrix)), BakeMeshes.D34.Of(md.Bp[i]));
                return k =>
                {
                    var w = md.W[k];
                    var M = new BakeMeshes.D34();
                    if (w.weight0 > 0f && w.boneIndex0 < skin.Length) M.AddScaled(skin[w.boneIndex0], w.weight0);
                    if (w.weight1 > 0f && w.boneIndex1 < skin.Length) M.AddScaled(skin[w.boneIndex1], w.weight1);
                    if (w.weight2 > 0f && w.boneIndex2 < skin.Length) M.AddScaled(skin[w.boneIndex2], w.weight2);
                    if (w.weight3 > 0f && w.boneIndex3 < skin.Length) M.AddScaled(skin[w.boneIndex3], w.weight3);
                    return M.Dir(md.N[k], false).magnitude;
                };
            }
            var l2w = BakeMeshes.D34.Mul(toRoot, BakeMeshes.D34.Of(node.localToWorldMatrix));
            return k => l2w.Dir(md.N[k], false).magnitude;
        }

        // ------------------------------------------------------------ the copy on the prototype

        /// <summary>Adds the inside copy of one built renderer (fill or section) under its node; returns the new renderer or null.</summary>
        public static Renderer Make(BakeData b, BakeData.RendData r, Renderer outside, MeshData data, Material[] outsideMats, Transform root, List<UnityEngine.Object> owned, Dictionary<string, Material> flatCache)
        {
            if (r.Inside != BakeData.InsideFill && r.Inside != BakeData.InsideSection) return null;
            if (r.Member || data == null) return null;
            if (r.Node >= 0 && r.Node < b.Nodes.Count && RendererNames.Contains(b.Nodes[r.Node].Name)) return null;
            bool section = r.Inside == BakeData.InsideSection;
            int subs = Math.Min(data.Idx.Length, r.Materials.Length);
            var keep = new bool[data.Idx.Length];
            var mats = new List<Material>();
            for (int s = 0; s < subs; s++)
            {
                int mi = r.Materials[s];
                if (!SlotEligible(b, mi) || data.Topo[s] != MeshTopology.Triangles) continue;
                var m = b.Materials[mi];
                Material use;
                if (section)
                {
                    if (OwnFabric(b, r)) use = outsideMats[s];
                    else use = Flat(b.Insides.Section, true, "bake_section", flatCache, owned);
                }
                else if (IsSkin(m)) use = Flat(m.Inside ?? new Vector3(0.5f, 0.4f, 0.35f), false, "bake_inside_" + m.Name, flatCache, owned);
                else use = outsideMats[s];
                if (!use) continue;
                keep[s] = true;
                mats.Add(use);
            }
            if (mats.Count == 0) return null;
            var smr = outside as SkinnedMeshRenderer;
            var node = outside.transform;
            var copy = Reversed(data, keep, Scales(data, node, smr ? smr.bones : null, root.worldToLocalMatrix));
            if (copy == null) return null;
            var mesh = BakeMeshes.Build(copy);
            owned.Add(mesh);
            var go = new GameObject(node.name + Suffix) { layer = node.gameObject.layer };
            go.transform.SetParent(node, false);
            Renderer rend;
            if (smr)
            {
                var s = go.AddComponent<SkinnedMeshRenderer>();
                s.bones = smr.bones;
                s.rootBone = smr.rootBone;
                s.sharedMesh = mesh;
                s.localBounds = smr.localBounds;
                s.updateWhenOffscreen = smr.updateWhenOffscreen;
                s.quality = smr.quality;
                rend = s;
            }
            else
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                rend = go.AddComponent<MeshRenderer>();
            }
            rend.sharedMaterials = mats.ToArray();
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = outside.receiveShadows;
            rend.lightProbeUsage = outside.lightProbeUsage;
            rend.reflectionProbeUsage = outside.reflectionProbeUsage;
            rend.probeAnchor = outside.probeAnchor;
            rend.renderingLayerMask = outside.renderingLayerMask;
            return rend;
        }

        static Material Flat(Vector3 rgb, bool unlit, string name, Dictionary<string, Material> cache, List<UnityEngine.Object> owned)
        {
            string key = (unlit ? "u" : "l") + rgb.x.ToString("R") + "," + rgb.y.ToString("R") + "," + rgb.z.ToString("R");
            if (cache.TryGetValue(key, out var m) && m) return m;
            m = BakeMaterials.Flat(rgb, unlit, name);
            owned.Add(m);
            cache[key] = m;
            return m;
        }
    }
}

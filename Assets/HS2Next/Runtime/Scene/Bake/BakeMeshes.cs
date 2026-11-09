// Chara to Item: meshes. A readable skinned renderer whose blend shapes all weigh 0 (or that has
// none) draws its mesh's own bind-pose data: that data is stored as it is, bit for bit, with its weights and bind poses (the "own" route; the
// round trip below moved positions by up to 4e-6 units, enough to flip pixels at alpha-test and triangle edges). Any other skinned renderer:
// SkinnedMeshRenderer.BakeMesh (posed, every
// blend shape at its weight, positions relative to the renderer's position and rotation without its scale), then unskinned to the
// bind pose with the mesh's own weights and bind poses: per vertex M = Σ wᵢ (boneᵢ.localToWorld × bindposeᵢ), bottom row (0, 0, 0, 1),
// world = TR(renderer) × baked, bind = M⁻¹ × world, normals and tangents by M⁻¹ as directions (renormalized, tangent w kept); the
// arithmetic is done in double. A skinned renderer whose mesh has no weights or no bind poses is stored as an unskinned renderer: the
// posed mesh relative to its node. An unskinned renderer: the mesh's own data. A mesh the API cannot read: its GPU buffers read back
// and decoded by their attribute descriptors (every VertexAttributeFormat), sub-meshes by their descriptors; a skinned one also takes
// its bone-weight buffer and bind poses. More than 4 influences: the 4 largest, renormalized (bake.weightsCut).
using System;
using System.Collections.Generic;
using HS2Next.Save;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Bake
{
    /// <summary>A mesh as data: bind-pose vertices for a skinned mesh, the node's local vertices for an unskinned one.</summary>
    public sealed class MeshData
    {
        public string Name = "";
        public Vector3[] V = Array.Empty<Vector3>();
        public Vector3[] N;
        public Vector4[] T;
        /// <summary>UV channels 0–7 (null: none) and their dimension (1–4).</summary>
        public readonly Vector4[][] Uv = new Vector4[8][];
        public readonly int[] UvDim = new int[8];
        public Color32[] C;
        /// <summary>Four influences per vertex, sorted by weight; null for an unskinned mesh.</summary>
        public BoneWeight[] W;
        public Matrix4x4[] Bp;
        public int[][] Idx = Array.Empty<int[]>();
        public MeshTopology[] Topo = Array.Empty<MeshTopology>();
        /// <summary>The capture's route (report only): own, api, bake+api, bake+posed, gpu, gpu+skin, posed.</summary>
        public string Route;
        /// <summary>The posed world positions at the capture (centroid, tests); not stored.</summary>
        public Vector3[] World;
        /// <summary>A posed mesh of an unreadable skinned renderer: the transform it hangs on (capture only).</summary>
        public Transform HangOn;

        public int VertexCount => V.Length;
        public int TriangleCount
        {
            get
            {
                int n = 0;
                for (int s = 0; s < Idx.Length; s++) if (Topo[s] == MeshTopology.Triangles) n += Idx[s].Length / 3;
                return n;
            }
        }
    }

    public static class BakeMeshes
    {
        /// <summary>Tests: capture every static mesh through the GPU buffers even when the API could read it (BD12 compares the two).</summary>
        public static bool ForceGpuRoute;

        // ------------------------------------------------------------ capture

        /// <summary>The mesh of one renderer as drawn; <paramref name="skinned"/> false when it is stored as an unskinned mesh.</summary>
        public static MeshData Capture(Renderer r, Mesh mesh, BakeCaptureResult res, out bool skinned)
        {
            var md = CaptureRoute(r, mesh, res, out skinned);
            if (md != null) res?.Inc("mesh.route." + md.Route);
            return md;
        }

        /// <summary>True when every blend shape of the renderer weighs 0 (or it has none): it draws its mesh's own data.</summary>
        public static bool BlendShapesOff(SkinnedMeshRenderer smr, Mesh mesh)
        {
            for (int i = 0; i < mesh.blendShapeCount; i++) if (smr.GetBlendShapeWeight(i) != 0f) return false;
            return true;
        }

        static MeshData CaptureRoute(Renderer r, Mesh mesh, BakeCaptureResult res, out bool skinned)
        {
            skinned = false;
            if (r is SkinnedMeshRenderer smr) return CaptureSkinned(smr, mesh, res, out skinned);
            if (mesh.isReadable && !ForceGpuRoute)
            {
                var md = FromApi(mesh);
                md.Route = "api";
                md.World = WorldOf(md.V, r.transform.localToWorldMatrix);
                res?.Inc("mesh.static.readable");
                return md;
            }
            res?.Inc("mesh.static.unreadable");
            var g = FromGpu(mesh, res, out string why);
            if (g == null)
            {
                res?.Findings.Warning("bake.meshUnreadable", r.name, "the mesh " + mesh.name + " cannot be read (" + why + "): left out");
                return null;
            }
            g.Route = "gpu";
            g.World = WorldOf(g.V, r.transform.localToWorldMatrix);
            return g;
        }

        static MeshData CaptureSkinned(SkinnedMeshRenderer smr, Mesh mesh, BakeCaptureResult res, out bool skinned)
        {
            skinned = false;
            BoneWeight[] w = null;
            Matrix4x4[] bp = null;
            string route;
            if (mesh.isReadable)
            {
                w = Weights4(mesh, res);
                bp = mesh.bindposes;
                route = "bake+api";
            }
            else
            {
                res?.Inc("mesh.skinned.unreadable");
                route = "bake+gpu";
                GpuSkin(mesh, out w, out bp, res);
            }
            bool hasSkin = w != null && w.Length == mesh.vertexCount && bp != null && bp.Length > 0;
            if (mesh.isReadable && hasSkin && BlendShapesOff(smr, mesh))
            {
                // the own route: the mesh's own bind-pose data, as the renderer draws it
                var own = FromApi(mesh);
                own.Name = mesh.name;
                own.W = w; own.Bp = bp; own.Route = "own";
                own.World = SkinWorld(own.V, w, BoneMatrices(smr, bp.Length), bp);
                skinned = true;
                res?.Inc("mesh.skinned.readable");
                return own;
            }
            Mesh baked = null;
            try
            {
                if (mesh.isReadable)
                {
                    baked = new Mesh { name = mesh.name, hideFlags = HideFlags.HideAndDontSave };
                    smr.BakeMesh(baked, false);
                    if (baked.vertexCount != mesh.vertexCount) { Object.DestroyImmediate(baked); baked = null; }
                }
                if (baked == null)
                {
                    // an unreadable skinned mesh: its own (bind-pose) buffers, skinned by its weights (blend shapes are not frozen)
                    string why = "no weights or bind poses";
                    var g = hasSkin ? FromGpu(mesh, res, out why) : null;
                    if (g != null)
                    {
                        g.W = w; g.Bp = bp; g.Route = "gpu+skin";
                        g.World = SkinWorld(g.V, w, BoneMatrices(smr, bp.Length), bp);
                        skinned = true;
                        res?.Inc("mesh.skinned.gpu");
                        return g;
                    }
                    // else the posed mesh as an unskinned mesh hung on the renderer's root bone
                    var posed = Posed(smr, mesh);
                    if (posed == null) { res?.Findings.Warning("bake.meshUnreadable", smr.name, "the skinned mesh " + mesh.name + " cannot be read (" + why + "): left out"); return null; }
                    res?.Findings.Warning("bake.meshPosed", smr.name, "the skinned mesh " + mesh.name + " cannot be read with its weights (" + why + "): stored posed, hung on its root bone");
                    res?.Inc("mesh.skinned.posed");
                    return posed;
                }
                var md = FromApi(baked);
                md.Name = mesh.name;
                // world = TR(renderer) × baked
                var trs = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                if (!hasSkin)
                {
                    // stored as an unskinned renderer: the posed mesh relative to its node
                    var toLocal = smr.transform.worldToLocalMatrix * trs;
                    md.World = WorldOf(md.V, trs);
                    ToSpace(md, toLocal);
                    md.Route = route.Replace("bake+", "bake+posed+");
                    res?.Inc("mesh.skinned.noWeights");
                    return md;
                }
                md.World = WorldOf(md.V, trs);
                ToWorldDirections(md, trs);
                var bones = BoneMatrices(smr, bp.Length);
                int far = Unskin(md.V, md.N, md.T, w, bones, bp);
                if (far > 0) res?.Inc("mesh.unskin.singular", far);
                md.W = w; md.Bp = bp; md.Route = route;
                skinned = true;
                res?.Inc(mesh.isReadable ? "mesh.skinned.readable" : "mesh.skinned.gpu");
                return md;
            }
            finally { if (baked) Object.DestroyImmediate(baked); }
        }

        /// <summary>The posed mesh of a skinned renderer relative to its root bone (else its own transform), from BakeMesh; null when it cannot
        /// be baked. Its <see cref="MeshData.HangOn"/> names the transform it hangs on.</summary>
        static MeshData Posed(SkinnedMeshRenderer smr, Mesh mesh)
        {
            Mesh baked = null;
            try
            {
                baked = new Mesh { name = mesh.name, hideFlags = HideFlags.HideAndDontSave };
                smr.BakeMesh(baked, false);
                if (baked.vertexCount == 0) return null;
                var md = FromApi(baked);
                md.Name = mesh.name;
                var trs = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                var hang = smr.rootBone ? smr.rootBone : smr.transform;
                md.World = WorldOf(md.V, trs);
                ToSpace(md, hang.worldToLocalMatrix * trs);
                md.HangOn = hang;
                md.Route = "posed";
                return md;
            }
            catch (Exception) { return null; }
            finally { if (baked) Object.DestroyImmediate(baked); }
        }

        /// <summary>boneᵢ.localToWorld for i &lt; count; a missing bone counts as the renderer's own transform.</summary>
        static Matrix4x4[] BoneMatrices(SkinnedMeshRenderer smr, int count)
        {
            var bones = smr.bones;
            var m = new Matrix4x4[count];
            var own = smr.transform.localToWorldMatrix;
            for (int i = 0; i < count; i++) m[i] = i < bones.Length && bones[i] ? bones[i].localToWorldMatrix : own;
            return m;
        }

        static Vector3[] WorldOf(Vector3[] v, Matrix4x4 m)
        {
            var w = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) w[i] = m.MultiplyPoint3x4(v[i]);
            return w;
        }

        /// <summary>Positions, normals and tangents of a baked mesh (relative to TR) put into world space.</summary>
        static void ToWorldDirections(MeshData md, Matrix4x4 trs)
        {
            var v = md.V;
            for (int i = 0; i < v.Length; i++) v[i] = md.World[i];
            if (md.N != null) for (int i = 0; i < md.N.Length; i++) md.N[i] = trs.MultiplyVector(md.N[i]);
            if (md.T != null)
                for (int i = 0; i < md.T.Length; i++)
                {
                    var t = trs.MultiplyVector(md.T[i]);
                    md.T[i] = new Vector4(t.x, t.y, t.z, md.T[i].w);
                }
        }

        /// <summary>A baked mesh (relative to TR) into a node's local space: positions by the matrix, normals by its inverse transpose,
        /// tangents by the matrix, both renormalized.</summary>
        static void ToSpace(MeshData md, Matrix4x4 m)
        {
            for (int i = 0; i < md.V.Length; i++) md.V[i] = m.MultiplyPoint3x4(md.V[i]);
            var nt = m.inverse.transpose;
            if (md.N != null) for (int i = 0; i < md.N.Length; i++) md.N[i] = nt.MultiplyVector(md.N[i]).normalized;
            if (md.T != null)
                for (int i = 0; i < md.T.Length; i++)
                {
                    var t = m.MultiplyVector(md.T[i]).normalized;
                    md.T[i] = new Vector4(t.x, t.y, t.z, md.T[i].w);
                }
        }

        // ------------------------------------------------------------ unskinning (double)

        /// <summary>
        /// World positions (and world normals, tangents) back to the bind pose in place: M = Σ wᵢ (bonesᵢ × bindposesᵢ) with the bottom row
        /// (0, 0, 0, 1), position M⁻¹ p, directions by M⁻¹ renormalized, tangent w kept; in double. Returns the vertices whose M was
        /// singular (left as given).
        /// </summary>
        public static int Unskin(Vector3[] v, Vector3[] n, Vector4[] t, BoneWeight[] w, Matrix4x4[] bones, Matrix4x4[] bindposes)
        {
            var skin = new D34[Math.Min(bones.Length, bindposes.Length)];
            for (int i = 0; i < skin.Length; i++) skin[i] = D34.Mul(D34.Of(bones[i]), D34.Of(bindposes[i]));
            int singular = 0;
            for (int k = 0; k < v.Length; k++)
            {
                var M = Blend(skin, w[k]);
                if (!M.Invert(out var inv)) { singular++; continue; }
                v[k] = inv.Point(v[k]);
                if (n != null && k < n.Length) n[k] = inv.Dir(n[k], true);
                if (t != null && k < t.Length)
                {
                    var d = inv.Dir(new Vector3(t[k].x, t[k].y, t[k].z), true);
                    t[k] = new Vector4(d.x, d.y, d.z, t[k].w);
                }
            }
            return singular;
        }

        /// <summary>Linear blend skinning of bind positions to world (the forward of <see cref="Unskin"/>), in double.</summary>
        public static Vector3[] SkinWorld(Vector3[] v, BoneWeight[] w, Matrix4x4[] skinMatrices, Matrix4x4[] bindposes = null)
        {
            int n = skinMatrices.Length;
            var skin = new D34[n];
            for (int i = 0; i < n; i++) skin[i] = bindposes != null ? D34.Mul(D34.Of(skinMatrices[i]), D34.Of(bindposes[i])) : D34.Of(skinMatrices[i]);
            var o = new Vector3[v.Length];
            for (int k = 0; k < v.Length; k++) o[k] = Blend(skin, w[k]).Point(v[k]);
            return o;
        }

        static D34 Blend(D34[] skin, BoneWeight w)
        {
            var M = new D34();
            Add(ref M, skin, w.boneIndex0, w.weight0);
            Add(ref M, skin, w.boneIndex1, w.weight1);
            Add(ref M, skin, w.boneIndex2, w.weight2);
            Add(ref M, skin, w.boneIndex3, w.weight3);
            return M;
        }

        static void Add(ref D34 M, D34[] skin, int i, float f)
        {
            if (f <= 0f || i < 0 || i >= skin.Length) return;
            M.AddScaled(skin[i], f);
        }

        /// <summary>A 3 × 4 affine matrix in double (the bottom row is (0, 0, 0, 1)).</summary>
        public struct D34
        {
            public double m00, m01, m02, m03, m10, m11, m12, m13, m20, m21, m22, m23;

            public static D34 Of(Matrix4x4 m) => new D34
            {
                m00 = m.m00, m01 = m.m01, m02 = m.m02, m03 = m.m03,
                m10 = m.m10, m11 = m.m11, m12 = m.m12, m13 = m.m13,
                m20 = m.m20, m21 = m.m21, m22 = m.m22, m23 = m.m23,
            };

            public static D34 Mul(D34 a, D34 b) => new D34
            {
                m00 = a.m00 * b.m00 + a.m01 * b.m10 + a.m02 * b.m20,
                m01 = a.m00 * b.m01 + a.m01 * b.m11 + a.m02 * b.m21,
                m02 = a.m00 * b.m02 + a.m01 * b.m12 + a.m02 * b.m22,
                m03 = a.m00 * b.m03 + a.m01 * b.m13 + a.m02 * b.m23 + a.m03,
                m10 = a.m10 * b.m00 + a.m11 * b.m10 + a.m12 * b.m20,
                m11 = a.m10 * b.m01 + a.m11 * b.m11 + a.m12 * b.m21,
                m12 = a.m10 * b.m02 + a.m11 * b.m12 + a.m12 * b.m22,
                m13 = a.m10 * b.m03 + a.m11 * b.m13 + a.m12 * b.m23 + a.m13,
                m20 = a.m20 * b.m00 + a.m21 * b.m10 + a.m22 * b.m20,
                m21 = a.m20 * b.m01 + a.m21 * b.m11 + a.m22 * b.m21,
                m22 = a.m20 * b.m02 + a.m21 * b.m12 + a.m22 * b.m22,
                m23 = a.m20 * b.m03 + a.m21 * b.m13 + a.m22 * b.m23 + a.m23,
            };

            public void AddScaled(D34 b, double f)
            {
                m00 += b.m00 * f; m01 += b.m01 * f; m02 += b.m02 * f; m03 += b.m03 * f;
                m10 += b.m10 * f; m11 += b.m11 * f; m12 += b.m12 * f; m13 += b.m13 * f;
                m20 += b.m20 * f; m21 += b.m21 * f; m22 += b.m22 * f; m23 += b.m23 * f;
            }

            public Vector3 Point(Vector3 p) => new Vector3(
                (float)(m00 * p.x + m01 * p.y + m02 * p.z + m03),
                (float)(m10 * p.x + m11 * p.y + m12 * p.z + m13),
                (float)(m20 * p.x + m21 * p.y + m22 * p.z + m23));

            public void PointD(double x, double y, double z, out double ox, out double oy, out double oz)
            {
                ox = m00 * x + m01 * y + m02 * z + m03;
                oy = m10 * x + m11 * y + m12 * z + m13;
                oz = m20 * x + m21 * y + m22 * z + m23;
            }

            public Vector3 Dir(Vector3 d, bool normalize)
            {
                double x = m00 * d.x + m01 * d.y + m02 * d.z, y = m10 * d.x + m11 * d.y + m12 * d.z, z = m20 * d.x + m21 * d.y + m22 * d.z;
                if (normalize)
                {
                    double l = Math.Sqrt(x * x + y * y + z * z);
                    if (l > 0) { x /= l; y /= l; z /= l; }
                }
                return new Vector3((float)x, (float)y, (float)z);
            }

            public bool Invert(out D34 r)
            {
                double c00 = m11 * m22 - m12 * m21, c01 = m02 * m21 - m01 * m22, c02 = m01 * m12 - m02 * m11;
                double det = m00 * c00 + m10 * c01 + m20 * c02;
                r = default;
                if (Math.Abs(det) < 1e-18 || double.IsNaN(det)) return false;
                double id = 1.0 / det;
                r.m00 = c00 * id; r.m01 = c01 * id; r.m02 = c02 * id;
                r.m10 = (m12 * m20 - m10 * m22) * id; r.m11 = (m00 * m22 - m02 * m20) * id; r.m12 = (m02 * m10 - m00 * m12) * id;
                r.m20 = (m10 * m21 - m11 * m20) * id; r.m21 = (m01 * m20 - m00 * m21) * id; r.m22 = (m00 * m11 - m01 * m10) * id;
                r.m03 = -(r.m00 * m03 + r.m01 * m13 + r.m02 * m23);
                r.m13 = -(r.m10 * m03 + r.m11 * m13 + r.m12 * m23);
                r.m23 = -(r.m20 * m03 + r.m21 * m13 + r.m22 * m23);
                return true;
            }
        }

        // ------------------------------------------------------------ the API route

        static readonly List<Vector4> uvTmp = new List<Vector4>();

        /// <summary>A readable mesh's data: vertices, normals, tangents, every UV channel with its dimension, colours, sub-meshes (base
        /// vertex applied); weights and bind poses are taken separately.</summary>
        public static MeshData FromApi(Mesh m)
        {
            var md = new MeshData { Name = m.name, V = m.vertices };
            int n = md.V.Length;
            var nn = m.normals; md.N = nn.Length == n && n > 0 ? nn : null;
            var tt = m.tangents; md.T = tt.Length == n && n > 0 ? tt : null;
            for (int ch = 0; ch < 8; ch++)
            {
                var a = VertexAttribute.TexCoord0 + ch;
                if (!m.HasVertexAttribute(a)) continue;
                uvTmp.Clear();
                m.GetUVs(ch, uvTmp);
                if (uvTmp.Count != n) continue;
                md.Uv[ch] = uvTmp.ToArray();
                md.UvDim[ch] = Mathf.Clamp(m.GetVertexAttributeDimension(a), 1, 4);
            }
            if (m.HasVertexAttribute(VertexAttribute.Color))
            {
                var c = m.colors32;
                md.C = c.Length == n ? c : null;
            }
            int sc = m.subMeshCount;
            md.Idx = new int[sc][];
            md.Topo = new MeshTopology[sc];
            for (int s = 0; s < sc; s++) { md.Topo[s] = m.GetTopology(s); md.Idx[s] = m.GetIndices(s, true); }
            return md;
        }

        /// <summary>A readable mesh's weights as four influences per vertex (the 4 largest, renormalized, when a vertex has more).</summary>
        public static BoneWeight[] Weights4(Mesh m, BakeCaptureResult res)
        {
            var per = m.GetBonesPerVertex();
            var all = m.GetAllBoneWeights();
            int n = m.vertexCount;
            if (per.Length != n) return null;
            var w = new BoneWeight[n];
            int o = 0, cut = 0;
            for (int k = 0; k < n; k++)
            {
                int c = per[k];
                var bw = new BoneWeight();
                float sum = 0f;
                int take = Math.Min(c, 4);
                for (int j = 0; j < take; j++) sum += all[o + j].weight;   // sorted by weight, largest first
                for (int j = 0; j < take; j++)
                {
                    var x = all[o + j];
                    float f = c > 4 && sum > 0f ? x.weight / sum : x.weight;
                    switch (j)
                    {
                        case 0: bw.boneIndex0 = x.boneIndex; bw.weight0 = f; break;
                        case 1: bw.boneIndex1 = x.boneIndex; bw.weight1 = f; break;
                        case 2: bw.boneIndex2 = x.boneIndex; bw.weight2 = f; break;
                        default: bw.boneIndex3 = x.boneIndex; bw.weight3 = f; break;
                    }
                }
                if (c > 4) cut++;
                w[k] = bw;
                o += c;
            }
            if (cut > 0 && res != null)
            {
                res.Inc("bake.weightsCut", cut);
                res.Findings.Info("bake.weightsCut", m.name, cut + " vertices had more than 4 influences: the 4 largest kept, renormalized");
            }
            return w;
        }

        // ------------------------------------------------------------ the GPU route

        /// <summary>
        /// An unreadable mesh through its GPU buffers: every vertex stream read back, each attribute decoded by its descriptor (format,
        /// dimension, stream, offset), the index buffer, the sub-meshes by their descriptors (start, count, base vertex, topology).
        /// Null with the reason when a buffer cannot be read.
        /// </summary>
        public static MeshData FromGpu(Mesh m, BakeCaptureResult res, out string why)
        {
            why = null;
            try
            {
                int vc = m.vertexCount;
                var attrs = m.GetVertexAttributes();
                var streams = new Dictionary<int, byte[]>();
                foreach (var a in attrs)
                {
                    if (streams.ContainsKey(a.stream)) continue;
                    using (var gb = m.GetVertexBuffer(a.stream))
                    {
                        if (gb == null) { why = "vertex stream " + a.stream + " has no buffer"; return null; }
                        var raw = ReadBuffer(gb);
                        if (raw == null) { why = "vertex stream " + a.stream + " could not be read back"; return null; }
                        streams[a.stream] = raw;
                    }
                }
                var md = new MeshData { Name = m.name };
                foreach (var a in attrs)
                {
                    var raw = streams[a.stream];
                    int stride = m.GetVertexBufferStride(a.stream), off = m.GetVertexAttributeOffset(a.attribute);
                    if (raw.Length < (long)stride * vc) { why = "vertex stream " + a.stream + " is shorter than its vertices"; return null; }
                    if (!Decodable(a.format)) { res?.Inc("mesh.gpu.formatUnknown"); continue; }
                    switch (a.attribute)
                    {
                        case VertexAttribute.Position: md.V = Vec3(raw, stride, off, a, vc); break;
                        case VertexAttribute.Normal: md.N = Vec3(raw, stride, off, a, vc); break;
                        case VertexAttribute.Tangent: md.T = Vec4(raw, stride, off, a, vc, 0f); break;
                        case VertexAttribute.Color:
                        {
                            var c = Vec4(raw, stride, off, a, vc, 1f);
                            md.C = new Color32[vc];
                            for (int k = 0; k < vc; k++) md.C[k] = new Color(c[k].x, c[k].y, c[k].z, c[k].w);
                            break;
                        }
                        default:
                            if (a.attribute >= VertexAttribute.TexCoord0 && a.attribute <= VertexAttribute.TexCoord7)
                            {
                                int ch = a.attribute - VertexAttribute.TexCoord0;
                                md.Uv[ch] = Vec4(raw, stride, off, a, vc, 0f);
                                md.UvDim[ch] = Mathf.Clamp(a.dimension, 1, 4);
                            }
                            break;
                    }
                    res?.Inc("mesh.gpu.attr." + a.format);
                }
                if (md.V.Length != vc) { why = "no position stream"; return null; }
                using (var ib = m.GetIndexBuffer())
                {
                    if (ib == null) { why = "no index buffer"; return null; }
                    var raw = ReadBuffer(ib);
                    if (raw == null) { why = "the index buffer could not be read back"; return null; }
                    bool i32 = m.indexFormat == IndexFormat.UInt32;
                    int sc = m.subMeshCount;
                    md.Idx = new int[sc][];
                    md.Topo = new MeshTopology[sc];
                    for (int s = 0; s < sc; s++)
                    {
                        var d = m.GetSubMesh(s);
                        md.Topo[s] = d.topology;
                        var idx = new int[d.indexCount];
                        for (int k = 0; k < d.indexCount; k++)
                        {
                            int j = d.indexStart + k;
                            idx[k] = (i32 ? BitConverter.ToInt32(raw, j * 4) : BitConverter.ToUInt16(raw, j * 2)) + d.baseVertex;
                        }
                        md.Idx[s] = idx;
                    }
                }
                res?.Inc("mesh.gpu.read");
                return md;
            }
            catch (Exception e) { why = e.GetType().Name + ": " + e.Message; return null; }
        }

        /// <summary>An unreadable skinned mesh's weights (bone-weight buffer, four bones) and bind poses; nulls when they cannot be read.</summary>
        static void GpuSkin(Mesh m, out BoneWeight[] w, out Matrix4x4[] bp, BakeCaptureResult res)
        {
            w = null; bp = null;
            try
            {
                var l = new List<Matrix4x4>();
                m.GetBindposes(l);
                bp = l.ToArray();
            }
            catch (Exception) { bp = null; }
            try
            {
                using (var gb = m.GetBoneWeightBuffer(SkinWeights.FourBones))
                {
                    if (gb == null) return;
                    var raw = ReadBuffer(gb);
                    if (raw == null) return;
                    // four floats of weight, four ints of bone index per vertex
                    int n = raw.Length / 32;
                    if (n != m.vertexCount) return;
                    w = new BoneWeight[n];
                    for (int k = 0; k < n; k++)
                    {
                        int o = k * 32;
                        w[k] = new BoneWeight
                        {
                            weight0 = BitConverter.ToSingle(raw, o), weight1 = BitConverter.ToSingle(raw, o + 4),
                            weight2 = BitConverter.ToSingle(raw, o + 8), weight3 = BitConverter.ToSingle(raw, o + 12),
                            boneIndex0 = BitConverter.ToInt32(raw, o + 16), boneIndex1 = BitConverter.ToInt32(raw, o + 20),
                            boneIndex2 = BitConverter.ToInt32(raw, o + 24), boneIndex3 = BitConverter.ToInt32(raw, o + 28),
                        };
                    }
                }
            }
            catch (Exception e) { res?.Findings.Info("bake.meshUnreadable", m.name, "bone weights: " + e.GetType().Name); w = null; }
        }

        static byte[] ReadBuffer(GraphicsBuffer gb)
        {
            var raw = new byte[(long)gb.count * gb.stride];
            try { gb.GetData(raw); return raw; }
            catch (Exception) { }
            var rq = AsyncGPUReadback.Request(gb);
            rq.WaitForCompletion();
            if (rq.hasError) return null;
            var data = rq.GetData<byte>();
            if (data.Length < raw.Length) return null;
            NativeArray<byte>.Copy(data, raw, raw.Length);
            return raw;
        }

        static bool Decodable(VertexAttributeFormat f)
        {
            switch (f)
            {
                case VertexAttributeFormat.Float32: case VertexAttributeFormat.Float16:
                case VertexAttributeFormat.UNorm8: case VertexAttributeFormat.SNorm8:
                case VertexAttributeFormat.UNorm16: case VertexAttributeFormat.SNorm16:
                case VertexAttributeFormat.UInt8: case VertexAttributeFormat.SInt8:
                case VertexAttributeFormat.UInt16: case VertexAttributeFormat.SInt16:
                case VertexAttributeFormat.UInt32: case VertexAttributeFormat.SInt32:
                    return true;
                default: return false;
            }
        }

        /// <summary>Bytes of one component of a vertex attribute format.</summary>
        public static int ComponentSize(VertexAttributeFormat f)
        {
            switch (f)
            {
                case VertexAttributeFormat.Float32: case VertexAttributeFormat.UInt32: case VertexAttributeFormat.SInt32: return 4;
                case VertexAttributeFormat.Float16: case VertexAttributeFormat.UNorm16: case VertexAttributeFormat.SNorm16:
                case VertexAttributeFormat.UInt16: case VertexAttributeFormat.SInt16: return 2;
                default: return 1;
            }
        }

        /// <summary>One component decoded as the GPU reads it (normalized formats to [0, 1] or [−1, 1], integers as their value).</summary>
        public static float Component(byte[] raw, int o, VertexAttributeFormat f)
        {
            switch (f)
            {
                case VertexAttributeFormat.Float32: return BitConverter.ToSingle(raw, o);
                case VertexAttributeFormat.Float16: return Mathf.HalfToFloat(BitConverter.ToUInt16(raw, o));
                case VertexAttributeFormat.UNorm8: return raw[o] / 255f;
                // the signed normalized formats by the reciprocal (as the mesh API reads them)
                case VertexAttributeFormat.SNorm8: return Math.Max((sbyte)raw[o] * (1f / 127f), -1f);
                case VertexAttributeFormat.UNorm16: return BitConverter.ToUInt16(raw, o) / 65535f;
                case VertexAttributeFormat.SNorm16: return Math.Max(BitConverter.ToInt16(raw, o) * (1f / 32767f), -1f);
                case VertexAttributeFormat.UInt8: return raw[o];
                case VertexAttributeFormat.SInt8: return (sbyte)raw[o];
                case VertexAttributeFormat.UInt16: return BitConverter.ToUInt16(raw, o);
                case VertexAttributeFormat.SInt16: return BitConverter.ToInt16(raw, o);
                case VertexAttributeFormat.UInt32: return BitConverter.ToUInt32(raw, o);
                case VertexAttributeFormat.SInt32: return BitConverter.ToInt32(raw, o);
                default: return float.NaN;
            }
        }

        static Vector4[] Vec4(byte[] raw, int stride, int off, VertexAttributeDescriptor a, int vc, float wDefault)
        {
            var o = new Vector4[vc];
            int cs = ComponentSize(a.format);
            for (int k = 0; k < vc; k++)
            {
                int b = k * stride + off;
                var v = new Vector4(0f, 0f, 0f, wDefault);
                for (int d = 0; d < a.dimension && d < 4; d++) v[d] = Component(raw, b + d * cs, a.format);
                o[k] = v;
            }
            return o;
        }

        static Vector3[] Vec3(byte[] raw, int stride, int off, VertexAttributeDescriptor a, int vc)
        {
            var o = new Vector3[vc];
            int cs = ComponentSize(a.format);
            for (int k = 0; k < vc; k++)
            {
                int b = k * stride + off;
                var v = Vector3.zero;
                for (int d = 0; d < a.dimension && d < 3; d++) v[d] = Component(raw, b + d * cs, a.format);
                o[k] = v;
            }
            return o;
        }

        // ------------------------------------------------------------ build

        /// <summary>A mesh from its data, readable (a bake's meshes stay readable: colliders, the Cutter), bounds computed.</summary>
        public static Mesh Build(MeshData md)
        {
            var m = new Mesh { name = md.Name };
            if (md.V.Length > 65535) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(md.V);
            if (md.N != null) m.SetNormals(md.N);
            if (md.T != null) m.SetTangents(md.T);
            for (int ch = 0; ch < 8; ch++)
            {
                var uv = md.Uv[ch];
                if (uv == null) continue;
                switch (md.UvDim[ch])
                {
                    case 1:
                    case 2:
                    {
                        var a = new Vector2[uv.Length];
                        for (int i = 0; i < a.Length; i++) a[i] = new Vector2(uv[i].x, uv[i].y);
                        m.SetUVs(ch, a);
                        break;
                    }
                    case 3:
                    {
                        var a = new Vector3[uv.Length];
                        for (int i = 0; i < a.Length; i++) a[i] = new Vector3(uv[i].x, uv[i].y, uv[i].z);
                        m.SetUVs(ch, a);
                        break;
                    }
                    default: m.SetUVs(ch, uv); break;
                }
            }
            if (md.C != null) m.SetColors(md.C);
            if (md.W != null) m.boneWeights = md.W;
            if (md.Bp != null) m.bindposes = md.Bp;
            m.subMeshCount = md.Idx.Length;
            for (int s = 0; s < md.Idx.Length; s++) m.SetIndices(md.Idx[s], md.Topo[s], s, false);
            m.RecalculateBounds();
            m.UploadMeshData(false);
            return m;
        }

        // ------------------------------------------------------------ the centroid (V21)

        /// <summary>The area-weighted centroid of the posed surface of a capture (triangle centroids weighted by area, world space).</summary>
        public static bool Centroid(BakeData b, BakeCaptureResult res, out Vector3 c)
        {
            double sx = 0, sy = 0, sz = 0, sa = 0;
            foreach (var mr in b.Meshes)
            {
                var md = mr.Data;
                if (md?.World == null) continue;
                Accumulate(md.World, md.Idx, md.Topo, ref sx, ref sy, ref sz, ref sa);
            }
            c = default;
            if (!(sa > 0)) return false;
            c = new Vector3((float)(sx / sa), (float)(sy / sa), (float)(sz / sa));
            return true;
        }

        /// <summary>Σ area × centroid and Σ area over the triangles of one mesh (world positions).</summary>
        public static void Accumulate(Vector3[] p, int[][] idx, MeshTopology[] topo, ref double sx, ref double sy, ref double sz, ref double sa)
        {
            for (int s = 0; s < idx.Length; s++)
            {
                if (topo[s] != MeshTopology.Triangles) continue;
                var l = idx[s];
                for (int i = 0; i + 2 < l.Length; i += 3)
                {
                    Vector3 a = p[l[i]], bb = p[l[i + 1]], cc = p[l[i + 2]];
                    double ux = bb.x - a.x, uy = bb.y - a.y, uz = bb.z - a.z, vx = cc.x - a.x, vy = cc.y - a.y, vz = cc.z - a.z;
                    double cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
                    double area = 0.5 * Math.Sqrt(cx * cx + cy * cy + cz * cz);
                    sa += area;
                    sx += area * ((double)a.x + bb.x + cc.x) / 3.0;
                    sy += area * ((double)a.y + bb.y + cc.y) / 3.0;
                    sz += area * ((double)a.z + bb.z + cc.z) / 3.0;
                }
            }
        }
    }
}

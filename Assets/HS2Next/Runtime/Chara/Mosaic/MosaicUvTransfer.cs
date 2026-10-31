// The geometry of the mosaic-set members (package R of 5.M):
//   Transfer   body-surface members: every member vertex is brought into the body mesh's space (through the bind poses of
//              the member's most weighted bone that the body also has, by name), projected onto the nearest body triangle
//              (closest point on the triangle; candidates are the body triangles whose box meets the member's box grown
//              by the surface distance), and the body's UV0 is interpolated there. A member whose every vertex lies within
//              SurfaceDistance (0.05 units) of the body is a body-surface member: a mesh copy "<mesh>#bodyuv" carries the
//              transferred UV0 (every other channel, the skin weights and bind poses as the member's). Transfers are cached
//              per (member mesh, body mesh content): the key of the body is its name, vertex count and a hash of its
//              positions and UV0, so the per-character body copies whose normals differ (the breast normals, a
//              MaterialEditor recalculation) share one entry. ReleaseAll destroys the copies (the catalog's release).
//   Region     what the per-camera block needs, in the member mesh's space: the area-weighted centroid, the radius about
//              it, the area-weighted mean normal, the UV per mesh unit along the densest direction (the largest singular
//              value of the triangle's UV Jacobian, the largest over the triangles that are not slivers) and the four
//              bones that carry most of the skin weight (renderer bone indices, weights normalised).
// Meshes that are not readable on the CPU get no transfer (the member falls back to the procedural mode).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HS2Next.Chara.Mosaic
{
    /// <summary>The per-camera block's inputs of a member mesh (mesh space).</summary>
    public sealed class MosaicRegion
    {
        public Vector3 Centroid;
        public float Radius;
        /// <summary>Area-weighted mean normal (normalised; zero when the triangles cancel).</summary>
        public Vector3 Normal;
        /// <summary>UV per mesh unit along the densest direction (0 without UVs).</summary>
        public float UvPerUnit;
        /// <summary>Up to four renderer bone indices by total skin weight, and their normalised weights (empty: not skinned).</summary>
        public int[] Bones = Array.Empty<int>();
        public float[] Weights = Array.Empty<float>();
    }

    /// <summary>One body-UV transfer (cached per member mesh and body mesh content).</summary>
    public sealed class MosaicTransfer
    {
        /// <summary>The member's own mesh (the key) and its copy with the transferred UV0 (null when not a body-surface member).</summary>
        public Mesh Source, Copy;
        public string BodyName;
        public long BodyKey;
        /// <summary>Every vertex within <see cref="MosaicUvTransfer.SurfaceDistance"/> of the body (mesh units, body space).</summary>
        public bool BodySurface;
        public float MaxDistance, MedianDistance;
        /// <summary>The transferred UV0 per member vertex (also for members that are not body-surface members).</summary>
        public Vector2[] Uv;
        /// <summary>The body triangle each vertex was projected onto (index into the body's triangle list / 3).</summary>
        public int[] Triangle;
        /// <summary>The region with the transferred UVs (body-surface members).</summary>
        public MosaicRegion Region;
        /// <summary>The bone name the two meshes' spaces were matched through (null: the spaces taken as equal).</summary>
        public string SharedBone;
        public double Milliseconds;
    }

    public static class MosaicUvTransfer
    {
        /// <summary>The largest distance (mesh units) of a body-surface member's vertices from the body (spec: 0.05).</summary>
        public const float SurfaceDistance = 0.05f;
        public const string CopySuffix = "#bodyuv";

        sealed class BodyKeyBox { public long Key; }
        static readonly ConditionalWeakTable<Mesh, BodyKeyBox> bodyKeys = new ConditionalWeakTable<Mesh, BodyKeyBox>();
        static readonly Dictionary<(Mesh source, long body), MosaicTransfer> cache = new Dictionary<(Mesh, long), MosaicTransfer>();
        static readonly ConditionalWeakTable<Mesh, MosaicTransfer> byCopy = new ConditionalWeakTable<Mesh, MosaicTransfer>();
        static readonly ConditionalWeakTable<Mesh, MosaicRegion> ownRegions = new ConditionalWeakTable<Mesh, MosaicRegion>();

        /// <summary>Transfers made since start (cache misses; tests).</summary>
        public static int Computed { get; private set; }
        /// <summary>Entries in the cache now.</summary>
        public static int Cached => cache.Count;

        /// <summary>The transfer whose copy <paramref name="mesh"/> is (null when it is not a copy made here).</summary>
        public static MosaicTransfer OfCopy(Mesh mesh) => mesh && byCopy.TryGetValue(mesh, out var t) ? t : null;

        /// <summary>The member's own mesh: the source of a copy made here, else the mesh itself.</summary>
        public static Mesh SourceOf(Mesh mesh) => OfCopy(mesh)?.Source ?? mesh;

        /// <summary>Destroys every copy and forgets the cache (with the catalog's bundles).</summary>
        public static int ReleaseAll()
        {
            int n = 0;
            foreach (var t in cache.Values)
                if (t.Copy) { Object.DestroyImmediate(t.Copy); n++; }
            cache.Clear();
            return n;
        }

        /// <summary>
        /// The body-UV transfer of a member onto a body (both skinned; null when a mesh is missing or not readable). Cached;
        /// entries whose member mesh was destroyed (unloaded bundles) are dropped with their copies on the next call.
        /// </summary>
        public static MosaicTransfer Transfer(SkinnedMeshRenderer member, SkinnedMeshRenderer body)
        {
            if (!member || !body) return null;
            var src = SourceOf(member.sharedMesh);
            var bm = body.sharedMesh;
            if (!src || !bm || !src.isReadable || !bm.isReadable) return null;
            Prune();
            long key = BodyKey(bm);
            if (cache.TryGetValue((src, key), out var t)) return t;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            t = Compute(src, member.bones, bm, body.bones, key);
            t.Milliseconds = sw.Elapsed.TotalMilliseconds;
            cache[(src, key)] = t;
            if (t.Copy) byCopy.Add(t.Copy, t);
            Computed++;
            return t;
        }

        static void Prune()
        {
            List<(Mesh, long)> dead = null;
            foreach (var kv in cache)
                if (!kv.Key.source) (dead ??= new List<(Mesh, long)>()).Add(kv.Key);
            if (dead == null) return;
            foreach (var k in dead)
            {
                if (cache[k].Copy) Object.DestroyImmediate(cache[k].Copy);
                cache.Remove(k);
            }
        }

        /// <summary>The body's content key: name, vertex count, positions and UV0 (memoized per mesh object).</summary>
        public static long BodyKey(Mesh m)
        {
            if (!m) return 0;
            if (bodyKeys.TryGetValue(m, out var b)) return b.Key;
            unchecked
            {
                long h = 1469598103934665603L;
                void Mix(int v) { h = (h ^ v) * 1099511628211L; }
                foreach (char c in m.name ?? "") Mix(c);
                Mix(m.vertexCount);
                var v3 = m.vertices;
                var uv = m.uv;
                for (int i = 0; i < v3.Length; i++)
                {
                    Mix(BitConverter.SingleToInt32Bits(v3[i].x)); Mix(BitConverter.SingleToInt32Bits(v3[i].y)); Mix(BitConverter.SingleToInt32Bits(v3[i].z));
                    if (i < uv.Length) { Mix(BitConverter.SingleToInt32Bits(uv[i].x)); Mix(BitConverter.SingleToInt32Bits(uv[i].y)); }
                }
                bodyKeys.Add(m, new BodyKeyBox { Key = h });
                return h;
            }
        }

        /// <summary>The member-space to body-space matrix through the member's most weighted bone that the body also has (by
        /// name); identity when they share none.</summary>
        public static Matrix4x4 MemberToBody(Mesh member, Transform[] memberBones, Mesh body, Transform[] bodyBones, out string bone)
        {
            bone = null;
            var mb = member.bindposes;
            var bb = body.bindposes;
            if (memberBones == null || bodyBones == null || mb.Length == 0 || bb.Length == 0) return Matrix4x4.identity;
            var bodyIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int j = 0; j < bodyBones.Length && j < bb.Length; j++)
                if (bodyBones[j] && !bodyIndex.ContainsKey(bodyBones[j].name)) bodyIndex[bodyBones[j].name] = j;
            var weight = BoneTotals(member);
            foreach (int i in Enumerable.Range(0, Math.Min(memberBones.Length, mb.Length)).OrderByDescending(i => i < weight.Length ? weight[i] : 0f).ThenBy(i => i))
            {
                if (!memberBones[i] || !bodyIndex.TryGetValue(memberBones[i].name, out int j)) continue;
                bone = memberBones[i].name;
                return bb[j].inverse * mb[i];
            }
            return Matrix4x4.identity;
        }

        static float[] BoneTotals(Mesh m)
        {
            var w = m.boneWeights;
            int n = m.bindposes.Length;
            var t = new float[n];
            foreach (var b in w)
            {
                if (b.boneIndex0 < n) t[b.boneIndex0] += b.weight0;
                if (b.boneIndex1 < n) t[b.boneIndex1] += b.weight1;
                if (b.boneIndex2 < n) t[b.boneIndex2] += b.weight2;
                if (b.boneIndex3 < n) t[b.boneIndex3] += b.weight3;
            }
            return t;
        }

        static MosaicTransfer Compute(Mesh src, Transform[] srcBones, Mesh body, Transform[] bodyBones, long key)
        {
            var t = new MosaicTransfer { Source = src, BodyName = body.name, BodyKey = key };
            var toBody = MemberToBody(src, srcBones, body, bodyBones, out t.SharedBone);
            var mv = src.vertices;
            var p = new Vector3[mv.Length];
            for (int i = 0; i < mv.Length; i++) p[i] = toBody.MultiplyPoint3x4(mv[i]);
            var bv = body.vertices;
            var buv = body.uv;
            var tris = AllTriangles(body);
            // candidates: body triangles whose box meets the member's box grown by the surface distance (and a margin)
            var box = new Bounds(p.Length > 0 ? p[0] : Vector3.zero, Vector3.zero);
            foreach (var v in p) box.Encapsulate(v);
            box.Expand(2f * (SurfaceDistance + 0.01f));
            var cand = new List<int>();
            for (int k = 0; k + 2 < tris.Length; k += 3)
            {
                Vector3 a = bv[tris[k]], b = bv[tris[k + 1]], c = bv[tris[k + 2]];
                var tb = new Bounds(a, Vector3.zero); tb.Encapsulate(b); tb.Encapsulate(c);
                if (tb.Intersects(box)) cand.Add(k);
            }
            t.Uv = new Vector2[p.Length];
            t.Triangle = new int[p.Length];
            var dist = new float[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                float best = float.MaxValue; int bk = -1; Vector3 bw = Vector3.zero;
                foreach (int k in cand)
                {
                    var q = ClosestPoint(p[i], bv[tris[k]], bv[tris[k + 1]], bv[tris[k + 2]], out var w);
                    float d = (q - p[i]).sqrMagnitude;
                    if (d < best) { best = d; bk = k; bw = w; }
                }
                if (bk < 0) { dist[i] = float.MaxValue; t.Triangle[i] = -1; continue; }
                dist[i] = Mathf.Sqrt(best);
                t.Triangle[i] = bk / 3;
                if (buv.Length == bv.Length) t.Uv[i] = buv[tris[bk]] * bw.x + buv[tris[bk + 1]] * bw.y + buv[tris[bk + 2]] * bw.z;
            }
            if (p.Length > 0)
            {
                var sorted = dist.OrderBy(d => d).ToArray();
                t.MaxDistance = sorted[sorted.Length - 1];
                t.MedianDistance = sorted[sorted.Length / 2];
            }
            t.BodySurface = p.Length > 0 && buv.Length == bv.Length && t.MaxDistance <= SurfaceDistance;
            if (t.BodySurface)
            {
                var copy = Object.Instantiate(src);
                copy.name = src.name + CopySuffix;
                copy.uv = t.Uv;
                copy.hideFlags = HideFlags.DontSave;
                t.Copy = copy;
                t.Region = Region(src, t.Uv);
            }
            return t;
        }

        static int[] AllTriangles(Mesh m)
        {
            if (m.subMeshCount <= 1) return m.triangles;
            var l = new List<int>();
            for (int s = 0; s < m.subMeshCount; s++) l.AddRange(m.GetTriangles(s));
            return l.ToArray();
        }

        /// <summary>
        /// The closest point of triangle (a, b, c) to <paramref name="p"/> and its barycentric weights (the published
        /// Voronoi-region method of Ericson, "Real-Time Collision Detection", 2005).
        /// </summary>
        public static Vector3 ClosestPoint(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out Vector3 w)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) { w = new Vector3(1, 0, 0); return a; }
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) { w = new Vector3(0, 1, 0); return b; }
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                w = new Vector3(1 - v, v, 0); return a + v * ab;
            }
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) { w = new Vector3(0, 0, 1); return c; }
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float u = d2 / (d2 - d6);
                w = new Vector3(1 - u, 0, u); return a + u * ac;
            }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float u = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                w = new Vector3(0, 1 - u, u); return b + u * (c - b);
            }
            float denom = 1f / (va + vb + vc);
            float vv = vb * denom, ww = vc * denom;
            w = new Vector3(1 - vv - ww, vv, ww);
            return a + ab * vv + ac * ww;
        }

        /// <summary>The body UV at the point of the whole body surface nearest to <paramref name="bodySpacePoint"/> (no
        /// candidate filter; tests compare the transfer with it).</summary>
        public static Vector2 NearestBodyUv(Mesh body, Vector3 bodySpacePoint, out float distance)
        {
            var bv = body.vertices; var buv = body.uv; var tris = AllTriangles(body);
            float best = float.MaxValue; Vector2 uv = Vector2.zero;
            for (int k = 0; k + 2 < tris.Length; k += 3)
            {
                var q = ClosestPoint(bodySpacePoint, bv[tris[k]], bv[tris[k + 1]], bv[tris[k + 2]], out var w);
                float d = (q - bodySpacePoint).sqrMagnitude;
                if (d < best) { best = d; uv = buv[tris[k]] * w.x + buv[tris[k + 1]] * w.y + buv[tris[k + 2]] * w.z; }
            }
            distance = Mathf.Sqrt(best);
            return uv;
        }

        /// <summary>The region of a mesh with its own UV0 (memoized per mesh object).</summary>
        public static MosaicRegion OwnRegion(Mesh m)
        {
            if (!m || !m.isReadable) return null;
            if (ownRegions.TryGetValue(m, out var r)) return r;
            r = Region(m, m.uv);
            ownRegions.Add(m, r);
            return r;
        }

        /// <summary>The region of a mesh with the given UV0 (null or short: no UV density).</summary>
        public static MosaicRegion Region(Mesh m, Vector2[] uv)
        {
            var r = new MosaicRegion();
            var v = m.vertices;
            if (v.Length == 0) return r;
            var tris = AllTriangles(m);
            bool hasUv = uv != null && uv.Length == v.Length;
            double area = 0;
            Vector3 c = Vector3.zero, n = Vector3.zero;
            var tri = new List<(float area, float sigma)>();
            for (int k = 0; k + 2 < tris.Length; k += 3)
            {
                Vector3 a = v[tris[k]], b = v[tris[k + 1]], d = v[tris[k + 2]];
                var cr = Vector3.Cross(b - a, d - a);
                float ar = 0.5f * cr.magnitude;
                if (ar <= 0f) continue;
                area += ar;
                c += (a + b + d) / 3f * ar;
                n += cr * 0.5f;
                if (hasUv) tri.Add((ar, Sigma(a, b, d, uv[tris[k]], uv[tris[k + 1]], uv[tris[k + 2]])));
            }
            if (area <= 0) { c = v.Aggregate(Vector3.zero, (s, x) => s + x) / v.Length; }
            else c /= (float)area;
            r.Centroid = c;
            r.Radius = v.Max(x => (x - c).magnitude);
            r.Normal = n.sqrMagnitude > 1e-20f ? n.normalized : Vector3.zero;
            if (tri.Count > 0)
            {
                // the densest direction over the triangles that are not slivers (at least 5 % of the median area)
                var areas = tri.Select(x => x.area).OrderBy(x => x).ToArray();
                float floor = 0.05f * areas[areas.Length / 2];
                r.UvPerUnit = tri.Where(x => x.area >= floor && !float.IsNaN(x.sigma) && !float.IsInfinity(x.sigma)).Select(x => x.sigma).DefaultIfEmpty(0f).Max();
            }
            var bw = m.boneWeights;
            int nb = m.bindposes.Length;
            if (bw.Length == v.Length && nb > 0)
            {
                var tot = BoneTotals(m);
                var top = Enumerable.Range(0, nb).Where(i => tot[i] > 0).OrderByDescending(i => tot[i]).ThenBy(i => i).Take(4).ToArray();
                float s = top.Sum(i => tot[i]);
                r.Bones = top;
                r.Weights = top.Select(i => s > 0 ? tot[i] / s : 0f).ToArray();
            }
            return r;
        }

        /// <summary>The largest singular value of a triangle's map from its surface to UV (UV per unit along the densest
        /// direction); NaN for a degenerate triangle.</summary>
        public static float Sigma(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            Vector3 e1 = b - a, e2 = c - a;
            float l1 = e1.magnitude;
            if (l1 <= 0f) return float.NaN;
            var x = e1 / l1;
            var nrm = Vector3.Cross(e1, e2);
            if (nrm.sqrMagnitude <= 0f) return float.NaN;
            var y = Vector3.Cross(nrm.normalized, x);
            // local 2D coordinates of the edges
            float p1x = l1, p1y = 0f, p2x = Vector3.Dot(e2, x), p2y = Vector3.Dot(e2, y);
            float det = p1x * p2y - p2x * p1y;
            if (Mathf.Abs(det) <= 1e-12f) return float.NaN;
            Vector2 d1 = ub - ua, d2 = uc - ua;
            // A = D * E^-1, E = [p1 p2] (columns), D = [d1 d2]
            float i00 = p2y / det, i01 = -p2x / det, i10 = -p1y / det, i11 = p1x / det;
            float a00 = d1.x * i00 + d2.x * i10, a01 = d1.x * i01 + d2.x * i11;
            float a10 = d1.y * i00 + d2.y * i10, a11 = d1.y * i01 + d2.y * i11;
            float s2 = a00 * a00 + a01 * a01 + a10 * a10 + a11 * a11;
            float dd = a00 * a11 - a01 * a10;
            float root = Mathf.Sqrt(Mathf.Max(0f, s2 * s2 - 4f * dd * dd));
            return Mathf.Sqrt(0.5f * (s2 + root));
        }
    }
}

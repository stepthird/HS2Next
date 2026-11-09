// The axis of an accessory slot of a built character (chara.accessoryAxis, and the `from: {key, accessory}` of fx.shoot, wet.attach and wet.stain).
//   points    from the slot's active mesh renderers: a skinned mesh in its pose (baked with a graphics device, skinned on the CPU from its bone weights and
//             bind poses without one), a readable mesh's vertices, an unreadable mesh as 11 points on each line from its bounds' centre to each of the 8
//             corners; every k-th point so that at most about 4,000 remain
//   centre    the mean of the points
//   axis      50 power iterations of the points' covariance from normalise(1, 0.3, 0.2), turned so that (centre - anchor) . axis >= 0
//   anchor    the position of the slot object's parent
//   muzzle    the mean of the points whose projection on the axis lies within 0.3 units of the largest
// A mesh is readable when the mesh says so: the Editor's wider access is not used, so the Editor reads what a player reads.
using System;
using System.Collections.Generic;
using HS2Next.Chara;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>The axis of an accessory slot, in world space.</summary>
    public sealed class AccessoryAxisResult
    {
        public Vector3 Axis, Center, Muzzle, Anchor;
        /// <summary>The points the axis was computed from (after the stride), and every point read.</summary>
        public int Points, Read;
        /// <summary>The renderers read: skinned meshes in their pose, readable meshes, meshes read by their bounds.</summary>
        public int Skinned, Plain, Bounds;
        /// <summary>The points in the muzzle's band.</summary>
        public int MuzzlePoints;
        /// <summary>"&lt;renderer&gt;: &lt;how it was read&gt;" per renderer read (readable, skinned (baked), skinned (CPU), bounds (...)); filled when not null before Gather.</summary>
        public List<string> Paths;
    }

    public static class AccessoryAxis
    {
        /// <summary>About this many points remain after the stride.</summary>
        public const int MaxPoints = 4000;
        /// <summary>Power iterations of the covariance.</summary>
        public const int Iterations = 50;
        /// <summary>The band of the muzzle below the largest projection, in units.</summary>
        public const double MuzzleBand = 0.3;
        /// <summary>Points on each line from a bounds' centre to a corner, both ends included.</summary>
        public const int LinePoints = 11;

        /// <summary>The accessory of a built character's slot (0-based, as chara.accessories), or null.</summary>
        public static AccessorySlot SlotOf(CharaRealization c, int slot)
        {
            var list = c?.Context?.Accessories;
            if (list == null || slot < 0) return null;
            if (slot < list.Count && list[slot] != null && list[slot].Slot == slot) return list[slot];
            foreach (var a in list)
                if (a != null && a.Slot == slot) return a;
            return null;
        }

        /// <summary>The axis of slot <paramref name="slot"/> of a built character; null with the reason when the slot is empty or holds fewer than 3 points.</summary>
        public static AccessoryAxisResult Of(CharaRealization c, int slot, out string error)
        {
            error = null;
            var a = SlotOf(c, slot);
            if (a == null || a.IsEmpty || !a.Object)
            {
                error = "no accessory in slot " + slot;
                return null;
            }
            var pts = new List<Vector3>();
            var counts = new AccessoryAxisResult { Paths = new List<string>() };
            Gather(a.Object, pts, counts, SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null);
            var parent = a.Object.transform.parent;
            var r = Compute(pts, parent ? parent.position : a.Object.transform.position);
            if (r == null)
            {
                error = "no readable mesh under slot " + slot;
                return null;
            }
            r.Skinned = counts.Skinned;
            r.Plain = counts.Plain;
            r.Bounds = counts.Bounds;
            r.Paths = counts.Paths;
            return r;
        }

        /// <summary>The points of every active mesh renderer under <paramref name="root"/> (world space); the renderers counted by how they were read.</summary>
        public static void Gather(GameObject root, List<Vector3> into, AccessoryAxisResult counts, bool graphics)
        {
            if (!root) return;
            Mesh baked = null;
            try
            {
                foreach (var r in root.GetComponentsInChildren<Renderer>(false))
                {
                    if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    if (r is SkinnedMeshRenderer s)
                    {
                        var m = s.sharedMesh;
                        if (!m) continue;
                        if (m.isReadable && (graphics ? Baked(s, ref baked, into) : CpuSkinned(s, into)))
                        {
                            counts.Skinned++;
                            counts.Paths?.Add(r.name + ": skinned (" + (graphics ? "baked" : "CPU") + ")");
                        }
                        else
                        {
                            BoundsPoints(s.localBounds, (s.rootBone ? s.rootBone : s.transform).localToWorldMatrix, into);
                            counts.Bounds++;
                            counts.Paths?.Add(r.name + ": bounds (" + (m.isReadable ? "skinned, not skinned" : "skinned, unreadable") + ")");
                        }
                    }
                    else if (r is MeshRenderer)
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        var m = mf ? mf.sharedMesh : null;
                        if (!m) continue;
                        if (m.isReadable && Vertices(m, r.transform.localToWorldMatrix, into))
                        {
                            counts.Plain++;
                            counts.Paths?.Add(r.name + ": readable");
                        }
                        else
                        {
                            BoundsPoints(m.bounds, r.transform.localToWorldMatrix, into);
                            counts.Bounds++;
                            counts.Paths?.Add(r.name + ": bounds (" + (m.isReadable ? "readable, no vertices" : "unreadable") + ")");
                        }
                    }
                }
            }
            finally
            {
                if (baked)
                {
                    if (Application.isPlaying) Object.Destroy(baked);
                    else Object.DestroyImmediate(baked);
                }
            }
        }

        static bool Baked(SkinnedMeshRenderer s, ref Mesh baked, List<Vector3> into)
        {
            if (!baked) baked = new Mesh { name = "accessory axis bake" };
            Vector3[] v;
            try
            {
                s.BakeMesh(baked, true);
                v = baked.vertices;
            }
            catch (Exception) { return false; }
            if (v == null || v.Length == 0) return false;
            var m = Matrix4x4.TRS(s.transform.position, s.transform.rotation, Vector3.one);   // the scale is in the baked vertices
            foreach (var p in v) into.Add(m.MultiplyPoint3x4(p));
            return true;
        }

        /// <summary>A readable skinned mesh skinned on the CPU from its bones, bind poses and four weights; false when they do not fit.</summary>
        static bool CpuSkinned(SkinnedMeshRenderer s, List<Vector3> into)
        {
            var mesh = s.sharedMesh;
            Vector3[] v;
            BoneWeight[] bw;
            Matrix4x4[] bp;
            try
            {
                v = mesh.vertices;
                bw = mesh.boneWeights;
                bp = mesh.bindposes;
            }
            catch (Exception) { return false; }
            var b = s.bones;
            if (v == null || v.Length == 0 || bw == null || bw.Length != v.Length || bp == null || b == null || bp.Length != b.Length) return false;
            var mats = new Matrix4x4[b.Length];
            var have = new bool[b.Length];
            for (int i = 0; i < b.Length; i++)
                if (b[i])
                {
                    mats[i] = b[i].localToWorldMatrix * bp[i];
                    have[i] = true;
                }
            int start = into.Count;
            for (int k = 0; k < v.Length; k++)
            {
                var w = bw[k];
                var p = Vector3.zero;
                float sum = 0f;
                Add(ref p, ref sum, w.boneIndex0, w.weight0, v[k], mats, have);
                Add(ref p, ref sum, w.boneIndex1, w.weight1, v[k], mats, have);
                Add(ref p, ref sum, w.boneIndex2, w.weight2, v[k], mats, have);
                Add(ref p, ref sum, w.boneIndex3, w.weight3, v[k], mats, have);
                if (!(sum > 0f))
                {
                    into.RemoveRange(start, into.Count - start);
                    return false;
                }
                into.Add(p / sum);
            }
            return true;
        }

        static void Add(ref Vector3 p, ref float sum, int i, float w, Vector3 v, Matrix4x4[] mats, bool[] have)
        {
            if (w <= 0f || i < 0 || i >= have.Length || !have[i]) return;
            p += w * mats[i].MultiplyPoint3x4(v);
            sum += w;
        }

        static bool Vertices(Mesh m, Matrix4x4 toWorld, List<Vector3> into)
        {
            Vector3[] v;
            try { v = m.vertices; }
            catch (Exception) { return false; }
            if (v == null || v.Length == 0) return false;
            foreach (var p in v) into.Add(toWorld.MultiplyPoint3x4(p));
            return true;
        }

        /// <summary>11 points on each line from the bounds' centre to each of its 8 corners (88 points, the centre among them 8 times).</summary>
        public static void BoundsPoints(Bounds b, Matrix4x4 toWorld, List<Vector3> into)
        {
            var c = b.center;
            var e = b.extents;
            for (int corner = 0; corner < 8; corner++)
            {
                var k = new Vector3((corner & 1) != 0 ? e.x : -e.x, (corner & 2) != 0 ? e.y : -e.y, (corner & 4) != 0 ? e.z : -e.z);
                for (int i = 0; i < LinePoints; i++) into.Add(toWorld.MultiplyPoint3x4(c + k * (i / (float)(LinePoints - 1))));
            }
        }

        /// <summary>
        /// The axis of points around an anchor (the arithmetic above, in double); null for fewer than 3 points. Every k-th point is kept, k the smallest stride that
        /// leaves at most <see cref="MaxPoints"/>.
        /// </summary>
        public static AccessoryAxisResult Compute(IReadOnlyList<Vector3> points, Vector3 anchor)
        {
            if (points == null || points.Count < 3) return null;
            int stride = Math.Max(1, (points.Count + MaxPoints - 1) / MaxPoints);
            int n = (points.Count + stride - 1) / stride;
            var xs = new double[n];
            var ys = new double[n];
            var zs = new double[n];
            double cx = 0, cy = 0, cz = 0;
            for (int i = 0, j = 0; i < points.Count; i += stride, j++)
            {
                var p = points[i];
                xs[j] = p.x; ys[j] = p.y; zs[j] = p.z;
                cx += p.x; cy += p.y; cz += p.z;
            }
            cx /= n; cy /= n; cz /= n;
            double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            for (int j = 0; j < n; j++)
            {
                double dx = xs[j] - cx, dy = ys[j] - cy, dz = zs[j] - cz;
                xx += dx * dx; xy += dx * dy; xz += dx * dz; yy += dy * dy; yz += dy * dz; zz += dz * dz;
            }
            xx /= n; xy /= n; xz /= n; yy /= n; yz /= n; zz /= n;
            double ax = 1, ay = 0.3, az = 0.2;
            Normalise(ref ax, ref ay, ref az);
            for (int it = 0; it < Iterations; it++)
            {
                double nx = xx * ax + xy * ay + xz * az;
                double ny = xy * ax + yy * ay + yz * az;
                double nz = xz * ax + yz * ay + zz * az;
                if (!Normalise(ref nx, ref ny, ref nz)) break;
                ax = nx; ay = ny; az = nz;
            }
            if ((cx - anchor.x) * ax + (cy - anchor.y) * ay + (cz - anchor.z) * az < 0) { ax = -ax; ay = -ay; az = -az; }
            double top = double.NegativeInfinity;
            for (int j = 0; j < n; j++) top = Math.Max(top, xs[j] * ax + ys[j] * ay + zs[j] * az);
            double mx = 0, my = 0, mz = 0;
            int m = 0;
            for (int j = 0; j < n; j++)
            {
                if (xs[j] * ax + ys[j] * ay + zs[j] * az < top - MuzzleBand) continue;
                mx += xs[j]; my += ys[j]; mz += zs[j];
                m++;
            }
            return new AccessoryAxisResult
            {
                Axis = new Vector3((float)ax, (float)ay, (float)az),
                Center = new Vector3((float)cx, (float)cy, (float)cz),
                Muzzle = new Vector3((float)(mx / m), (float)(my / m), (float)(mz / m)),
                Anchor = anchor,
                Points = n,
                Read = points.Count,
                MuzzlePoints = m,
            };
        }

        static bool Normalise(ref double x, ref double y, ref double z)
        {
            double l = Math.Sqrt(x * x + y * y + z * z);
            if (!(l > 1e-12) || double.IsInfinity(l)) return false;
            x /= l; y /= l; z /= l;
            return true;
        }
    }
}

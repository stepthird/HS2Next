// The rest pose of a character's skeleton, worked out on paper: nothing here writes a bone, so no latch of a writer that compares a bone with its last value sees it. From the
// hips down a rest matrix is the parent's times the bone's live local position, the rotation of the same-named transform of the skeleton's prefab and the live local scale; the
// parent of the hips counts with its world matrix, so the character's scale and body shape are in what comes out. A bone the prefab lacks keeps its live rotation.
using System;
using System.Collections.Generic;
using HS2Next.Chara;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class RagdollRest
    {
        const string PrefabName = "p_cf_anim";

        struct Dq { public double X, Y, Z, W; }

        sealed class Node
        {
            // the first three rows of the matrix
            public readonly double[] M = new double[12];
            public Dq Q;
        }

        readonly Dictionary<Transform, Node> nodes = new Dictionary<Transform, Node>();
        readonly Dictionary<string, Transform> prefab;
        readonly Transform hips;

        /// <summary>False when the prefab could not be read: the rest is the current pose.</summary>
        public bool FromPrefab => prefab != null;

        RagdollRest(Transform hips, Dictionary<string, Transform> prefab)
        {
            this.hips = hips;
            this.prefab = prefab;
        }

        /// <summary>The rest pose under <paramref name="hips"/> of a built character.</summary>
        public static RagdollRest Solve(CharaContext ctx, Transform hips)
        {
            GameObject go = null;
            try
            {
                go = ctx?.Catalog?.LoadAsset<GameObject>("studio/base/00.unity3d", PrefabName)
                     ?? ctx?.Catalog?.LoadAsset<GameObject>("chara/oo_base.unity3d", PrefabName);
            }
            catch (Exception) { }
            Dictionary<string, Transform> map = null;
            if (go)
            {
                map = new Dictionary<string, Transform>(StringComparer.Ordinal);
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    if (!map.ContainsKey(t.name)) map[t.name] = t;
            }
            return new RagdollRest(hips, map);
        }

        /// <summary>The bone's world position in the rest pose.</summary>
        public Vector3 Position(Transform bone)
        {
            var n = Get(bone);
            return new Vector3((float)n.M[3], (float)n.M[7], (float)n.M[11]);
        }

        /// <summary>The bone's world rotation in the rest pose.</summary>
        public Quaternion Rotation(Transform bone)
        {
            var q = Get(bone).Q;
            return new Quaternion((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);
        }

        /// <summary>The bone's world position in the rest pose, in double (the rest row's check).</summary>
        public void Position(Transform bone, out double x, out double y, out double z)
        {
            var n = Get(bone);
            x = n.M[3]; y = n.M[7]; z = n.M[11];
        }

        Node Get(Transform t)
        {
            if (nodes.TryGetValue(t, out var n)) return n;
            n = new Node();
            double[] pm;
            Dq pq;
            if (t == hips || !t.parent)
            {
                pm = new double[12];
                pm[0] = pm[5] = pm[10] = 1.0;
                pq = new Dq { W = 1.0 };
                var p = t.parent;
                if (p)
                {
                    var m = p.localToWorldMatrix;
                    for (int r = 0; r < 3; r++)
                        for (int c = 0; c < 4; c++) pm[r * 4 + c] = m[r, c];
                    var pr = p.rotation;
                    pq = new Dq { X = pr.x, Y = pr.y, Z = pr.z, W = pr.w };
                }
            }
            else
            {
                var pn = Get(t.parent);
                pm = pn.M;
                pq = pn.Q;
            }
            t.GetLocalPositionAndRotation(out var lp, out var lr);
            var ls = t.localScale;
            if (prefab != null && prefab.TryGetValue(t.name, out var pt)) lr = pt.localRotation;
            // the local matrix: TRS with the rotation in double
            double x = lr.x, y = lr.y, z = lr.z, w = lr.w;
            double norm = 1.0 / Math.Sqrt(x * x + y * y + z * z + w * w);
            x *= norm; y *= norm; z *= norm; w *= norm;
            double r00 = 1 - 2 * (y * y + z * z), r01 = 2 * (x * y - z * w), r02 = 2 * (x * z + y * w);
            double r10 = 2 * (x * y + z * w), r11 = 1 - 2 * (x * x + z * z), r12 = 2 * (y * z - x * w);
            double r20 = 2 * (x * z - y * w), r21 = 2 * (y * z + x * w), r22 = 1 - 2 * (x * x + y * y);
            double sx = ls.x, sy = ls.y, sz = ls.z;
            double[] l =
            {
                r00 * sx, r01 * sy, r02 * sz, lp.x,
                r10 * sx, r11 * sy, r12 * sz, lp.y,
                r20 * sx, r21 * sy, r22 * sz, lp.z,
            };
            for (int r = 0; r < 3; r++)
            {
                double a = pm[r * 4], b = pm[r * 4 + 1], c = pm[r * 4 + 2], d = pm[r * 4 + 3];
                n.M[r * 4] = a * l[0] + b * l[4] + c * l[8];
                n.M[r * 4 + 1] = a * l[1] + b * l[5] + c * l[9];
                n.M[r * 4 + 2] = a * l[2] + b * l[6] + c * l[10];
                n.M[r * 4 + 3] = a * l[3] + b * l[7] + c * l[11] + d;
            }
            n.Q = new Dq
            {
                X = pq.W * x + pq.X * w + pq.Y * z - pq.Z * y,
                Y = pq.W * y - pq.X * z + pq.Y * w + pq.Z * x,
                Z = pq.W * z + pq.X * y - pq.Y * x + pq.Z * w,
                W = pq.W * w - pq.X * x - pq.Y * y - pq.Z * z,
            };
            nodes[t] = n;
            return n;
        }
    }
}

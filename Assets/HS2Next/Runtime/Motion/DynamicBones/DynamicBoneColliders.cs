// Collider shapes for the dynamic-bone simulation, read from the DynamicBoneCollider /
// DynamicBonePlaneCollider data holders.
// A shape is evaluated once per step from the collider transform; the response functions are the
// formulas. Three evaluation paths: the DynamicBone job path of the shipped game (zero records
// for null / disabled / non-capsule-collider entries, optional legacy sphere-centre bug), the intended
// main-thread path (plane colliders, disabled entries skipped) and the
// DynamicBone_Ver02 path (non-null, enabled, active in hierarchy, correct spheres, no planes).
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Motion.DynamicBones
{
    public enum ColliderShapeKind : byte { None = 0, Sphere = 1, Capsule = 2, Plane = 3 }

    /// <summary>Which code path evaluates an entry of a collider list.</summary>
    public enum ColliderPath : byte
    {
        /// <summary>DynamicBone in HS2 (Burst job): capsules correct; spheres per <c>legacySphereBug</c>; null / disabled /
        /// plane entries are all-zero records (Outside sphere of radius 0 at the world origin).</summary>
        Job,
        /// <summary>The asset's main-thread routine (never executed in HS2): collider's own response, planes included.</summary>
        MainThread,
        /// <summary>DynamicBone_Ver02: non-null, enabled and active in hierarchy; correct spheres.</summary>
        Ver02,
    }

    /// <summary>One collider in world space. Sphere: centre A. Capsule: segment A-B. Plane: point A, unit normal B.</summary>
    public struct ColliderShape
    {
        public ColliderShapeKind Kind;
        /// <summary>m_Bound 1 (keep particles inside).</summary>
        public bool Inside;
        public Vector3 A, B;
        /// <summary>World radius m_Radius · |lossyScale.x|.</summary>
        public float Radius;
        /// <summary>Capsule: B − A and 1 / |B − A|² (precomputed for the hot loop).</summary>
        public Vector3 U;
        public float UU, InvUU;

        public override string ToString() => $"{Kind}{(Inside ? " inside" : "")} A{A} B{B} r{Radius:0.###}";
    }

    public static class DynamicBoneColliders
    {
        /// <summary>World shape of one list entry for the given path.</summary>
        public static ColliderShape Evaluate(MonoBehaviour src, ColliderPath path, bool legacySphereBug)
        {
            if (path == ColliderPath.Job)
            {
                var c = src as global::DynamicBoneCollider;
                if (c == null || !c.enabled) return new ColliderShape { Kind = ColliderShapeKind.Sphere };   // zero record
                return FromCollider(c, legacySphereBug);
            }
            if (src == null || !src.enabled) return default;
            if (path == ColliderPath.Ver02 && !src.gameObject.activeInHierarchy) return default;
            if (src is global::DynamicBoneCollider dc) return FromCollider(dc, false);
            if (path == ColliderPath.MainThread && src is global::DynamicBonePlaneCollider pc) return FromPlane(pc);
            return default;
        }

        /// <summary>h = m_Height/2 − m_Radius; h ≤ 0 → sphere at m_Center, else a capsule with ends m_Center ∓ h on the local axis.</summary>
        public static ColliderShape FromCollider(global::DynamicBoneCollider c, bool legacySphereBug)
        {
            var t = c.transform;
            var m = t.localToWorldMatrix;
            var s = new ColliderShape
            {
                Inside = c.m_Bound == 1,
                Radius = c.m_Radius * Mathf.Abs(t.lossyScale.x),
            };
            float h = c.m_Height * 0.5f - c.m_Radius;
            if (h <= 0f)
            {
                s.Kind = ColliderShapeKind.Sphere;
                // the job converts the centre to a direction (w = 0), dropping the collider's translation.
                s.A = legacySphereBug ? m.MultiplyVector(c.m_Center) : m.MultiplyPoint3x4(c.m_Center);
            }
            else
            {
                Vector3 axis = c.m_Direction == 0 ? new Vector3(h, 0, 0) : c.m_Direction == 1 ? new Vector3(0, h, 0) : new Vector3(0, 0, h);
                s.Kind = ColliderShapeKind.Capsule;
                s.A = m.MultiplyPoint3x4(c.m_Center - axis);
                s.B = m.MultiplyPoint3x4(c.m_Center + axis);
                s.U = s.B - s.A;
                s.UU = s.U.sqrMagnitude;
                s.InvUU = s.UU > 0f ? 1f / s.UU : 0f;
            }
            return s;
        }

        /// <summary>intended plane: normal = right / up / forward for X / Y / Z, through TransformPoint(m_Center).</summary>
        public static ColliderShape FromPlane(global::DynamicBonePlaneCollider c)
        {
            var t = c.transform;
            return new ColliderShape
            {
                Kind = ColliderShapeKind.Plane,
                Inside = c.m_Bound == 1,
                A = t.TransformPoint(c.m_Center),
                B = c.m_Direction == 0 ? t.right : c.m_Direction == 1 ? t.up : t.forward,
            };
        }

        /// <summary>response for a particle at x with radius pr (world).</summary>
        public static void Collide(in ColliderShape s, ref Vector3 x, float pr)
        {
            switch (s.Kind)
            {
                case ColliderShapeKind.Sphere:
                    if (s.Inside) InsideSphere(ref x, s.A, s.Radius - pr);
                    else OutsideSphere(ref x, s.A, s.Radius + pr);
                    break;
                case ColliderShapeKind.Capsule:
                    Capsule(ref x, s.A, s.B, s.Inside ? s.Radius - pr : s.Radius + pr, s.Inside);
                    break;
                case ColliderShapeKind.Plane:
                {
                    float d = Vector3.Dot(s.B, x - s.A);   // particle centre, radius ignored
                    if (s.Inside ? d > 0f : d < 0f) x -= s.B * d;
                    break;
                }
            }
        }

        /// <summary>
        /// Same response as <see cref="Collide"/>, written with scalar arithmetic for the per-particle loop (Mono does not
        /// inline the Vector3 operators). Capsule / sphere only; planes fall back to Collide.
        /// </summary>
        public static void CollideFast(ref ColliderShape s, ref float px, ref float py, ref float pz, float pr)
        {
            float cx, cy, cz, r;
            switch (s.Kind)
            {
                case ColliderShapeKind.Sphere:
                    cx = s.A.x; cy = s.A.y; cz = s.A.z;
                    break;
                case ColliderShapeKind.Capsule:
                {
                    float wx = px - s.A.x, wy = py - s.A.y, wz = pz - s.A.z;
                    float t = wx * s.U.x + wy * s.U.y + wz * s.U.z;
                    if (t <= 0f) { cx = s.A.x; cy = s.A.y; cz = s.A.z; }
                    else if (t >= s.UU) { cx = s.B.x; cy = s.B.y; cz = s.B.z; }
                    else { float f = t / s.UU; cx = s.A.x + s.U.x * f; cy = s.A.y + s.U.y * f; cz = s.A.z + s.U.z * f; }
                    break;
                }
                case ColliderShapeKind.Plane:
                {
                    var x = new Vector3(px, py, pz);
                    Collide(s, ref x, pr);
                    px = x.x; py = x.y; pz = x.z;
                    return;
                }
                default: return;
            }
            float nx = px - cx, ny = py - cy, nz = pz - cz;
            float l2 = nx * nx + ny * ny + nz * nz;
            if (s.Inside)
            {
                r = s.Radius - pr;
                if (!(l2 > r * r)) return;
            }
            else
            {
                r = s.Radius + pr;
                if (!(l2 > 0f && l2 < r * r)) return;
            }
            float l = Mathf.Sqrt(l2);
            float k = (r - l) / l;
            px += nx * k; py += ny * k; pz += nz * k;
        }

        public static void OutsideSphere(ref Vector3 x, Vector3 c, float r)
        {
            Vector3 d = x - c;
            float l2 = d.sqrMagnitude;
            if (l2 > 0f && l2 < r * r) x = c + d * (r / Mathf.Sqrt(l2));
        }

        // r may be negative (particle larger than the collider): squared in the test, used signed in the push (literal).
        public static void InsideSphere(ref Vector3 x, Vector3 c, float r)
        {
            Vector3 d = x - c;
            float l2 = d.sqrMagnitude;
            if (l2 > r * r) x = c + d * (r / Mathf.Sqrt(l2));
        }

        public static void Capsule(ref Vector3 x, Vector3 a, Vector3 b, float r, bool inside)
        {
            Vector3 u = b - a, w = x - a;
            float t = Vector3.Dot(w, u), uu = u.sqrMagnitude;
            if (t <= 0f) { if (inside) InsideSphere(ref x, a, r); else OutsideSphere(ref x, a, r); return; }
            if (t >= uu) { if (inside) InsideSphere(ref x, b, r); else OutsideSphere(ref x, b, r); return; }
            Vector3 n = x - (a + u * (t / uu));
            float l2 = n.sqrMagnitude;
            if (inside ? l2 > r * r : l2 > 0f && l2 < r * r)
            {
                float l = Mathf.Sqrt(l2);
                x += n * ((r - l) / l);
            }
        }

        /// <summary>Depth by which a point lies inside an Outside shape grown by pr (0 when outside); for tests and diagnostics.</summary>
        public static float Penetration(in ColliderShape s, Vector3 x, float pr)
        {
            if (s.Inside) return 0f;
            float r = s.Radius + pr;
            switch (s.Kind)
            {
                case ColliderShapeKind.Sphere: return Mathf.Max(0f, r - (x - s.A).magnitude);
                case ColliderShapeKind.Capsule:
                {
                    Vector3 u = s.B - s.A;
                    float t = Mathf.Clamp01(Vector3.Dot(x - s.A, u) / Mathf.Max(u.sqrMagnitude, 1e-12f));
                    return Mathf.Max(0f, r - (x - (s.A + u * t)).magnitude);
                }
                default: return 0f;
            }
        }
    }

    /// <summary>
    /// The colliders referenced by a set of chains, evaluated once per step and shared (one entry per distinct
    /// source component and path). Chains keep indices into the table.
    /// </summary>
    public sealed class ColliderTable
    {
        readonly List<MonoBehaviour> sources = new List<MonoBehaviour>();
        readonly List<ColliderPath> paths = new List<ColliderPath>();
        readonly Dictionary<(MonoBehaviour, ColliderPath), int> index = new Dictionary<(MonoBehaviour, ColliderPath), int>();
        ColliderShape[] shapes = new ColliderShape[0];
        int nullIndex = -1;

        /// <summary>(job path only).</summary>
        public bool LegacySphereBug = true;
        public int Count => sources.Count;
        public MonoBehaviour Source(int i) => sources[i];
        public ColliderPath Path(int i) => paths[i];
        public ColliderShape this[int i] => shapes[i];
        /// <summary>The evaluated shapes (valid after Update; index = entry).</summary>
        public ColliderShape[] Shapes => shapes;

        /// <summary>Index of an entry (added when new). Null entries share one slot per table.</summary>
        public int IndexOf(MonoBehaviour c, ColliderPath path)
        {
            if (ReferenceEquals(c, null))
            {
                if (nullIndex < 0) { nullIndex = sources.Count; sources.Add(null); paths.Add(path); }
                return nullIndex;
            }
            var key = (c, path);
            if (index.TryGetValue(key, out int i)) return i;
            i = sources.Count;
            sources.Add(c); paths.Add(path);
            index[key] = i;
            return i;
        }

        public void Clear() { sources.Clear(); paths.Clear(); index.Clear(); nullIndex = -1; shapes = new ColliderShape[0]; }

        /// <summary>Evaluates every entry from its transform (once per step, before the chains).</summary>
        public void Update()
        {
            if (shapes.Length != sources.Count) shapes = new ColliderShape[sources.Count];
            for (int i = 0; i < sources.Count; i++)
                shapes[i] = DynamicBoneColliders.Evaluate(sources[i], paths[i], LegacySphereBug);
        }
    }
}

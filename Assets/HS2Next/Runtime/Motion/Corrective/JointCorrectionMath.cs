// Euler and rotation helpers of the joint correction.
//
// Decompose/Compose reproduce the game's own Euler conversion (not Unity's eulerAngles): middle angle
// from asin of one matrix element, outer angles from atan2, and a gimbal branch only on the +0.9999
// side. The optional "wrap fix" picks, of the two Euler solutions of a rotation, the
// one with the smaller outer angles; it never changes a result whose outer angles are within ±90°.
// The trace-only matrix -> quaternion conversion of the LookAt part is reproduced (FromAxesTrace);
// FromAxesRobust is the deliberate fix. All functions are allocation-free.
using UnityEngine;

namespace HS2Next.Motion.Corrective
{
    public static class JointCorrectionMath
    {
        /// <summary>Threshold of the one-sided gimbal branch.</summary>
        public const float GimbalLimit = 0.9999f;

        /// <summary>Axis index (0 X, 1 Y, 2 Z) of the first, middle and last letter of an order.</summary>
        public static void Letters(JcOrder o, out int first, out int middle, out int last)
        {
            switch (o)
            {
                case JcOrder.XYZ: first = 0; middle = 1; last = 2; return;
                case JcOrder.XZY: first = 0; middle = 2; last = 1; return;
                case JcOrder.YXZ: first = 1; middle = 0; last = 2; return;
                case JcOrder.YZX: first = 1; middle = 2; last = 0; return;
                case JcOrder.ZXY: first = 2; middle = 0; last = 1; return;
                default: first = 2; middle = 1; last = 0; return;   // ZYX
            }
        }

        static float Atan2Deg(float y, float x)
        {
            float a = Mathf.Atan2(y, x) * Mathf.Rad2Deg;
            return float.IsNaN(a) ? 0f : a;
        }

        /// <summary>Decompose_O(q): (x, y, z) in degrees; middle angle in [-90, 90], outer angles in (-180, 180].</summary>
        public static Vector3 Decompose(Quaternion q, JcOrder o)
        {
            float x = q.x, y = q.y, z = q.z, w = q.w;
            // M(q) as Unity's Matrix4x4.TRS(0, q, 1) (column vectors, m_rc = row r, column c).
            float m00 = 1f - 2f * (y * y + z * z), m01 = 2f * (x * y - z * w), m02 = 2f * (x * z + y * w);
            float m10 = 2f * (x * y + z * w), m11 = 1f - 2f * (x * x + z * z), m12 = 2f * (y * z - x * w);
            float m20 = 2f * (x * z - y * w), m21 = 2f * (y * z + x * w), m22 = 1f - 2f * (x * x + y * y);
            float e, ax, ay, az;
            switch (o)
            {
                case JcOrder.XYZ:
                    e = m02; ay = Mathf.Asin(Mathf.Clamp(e, -1f, 1f)) * Mathf.Rad2Deg;
                    if (e > GimbalLimit) { az = 0f; ax = Atan2Deg(m21, m11); }
                    else { ax = Atan2Deg(-m12, m22); az = Atan2Deg(-m01, m00); }
                    break;
                case JcOrder.XZY:
                    e = -m01; az = Mathf.Asin(Mathf.Clamp(e, -1f, 1f)) * Mathf.Rad2Deg;
                    if (e > GimbalLimit) { ay = 0f; ax = Atan2Deg(-m12, m22); }
                    else { ax = Atan2Deg(m21, m11); ay = Atan2Deg(m02, m00); }
                    break;
                case JcOrder.YXZ:
                    e = -m12; ax = Mathf.Asin(Mathf.Clamp(e, -1f, 1f)) * Mathf.Rad2Deg;
                    if (e > GimbalLimit) { az = 0f; ay = Atan2Deg(-m20, m00); }
                    else { ay = Atan2Deg(m02, m22); az = Atan2Deg(m10, m11); }
                    break;
                case JcOrder.YZX:
                    e = m10; az = Mathf.Asin(Mathf.Clamp(e, -1f, 1f)) * Mathf.Rad2Deg;
                    if (e > GimbalLimit) { ax = 0f; ay = Atan2Deg(m02, m22); }
                    else { ax = Atan2Deg(-m12, m11); ay = Atan2Deg(-m20, m00); }
                    break;
                case JcOrder.ZXY:
                    e = m21; ax = Mathf.Asin(Mathf.Clamp(e, -1f, 1f)) * Mathf.Rad2Deg;
                    if (e > GimbalLimit) { ay = 0f; az = Atan2Deg(m10, m00); }
                    else { ay = Atan2Deg(-m20, m22); az = Atan2Deg(-m01, m11); }
                    break;
                default:   // ZYX
                    e = -m20; ay = Mathf.Asin(Mathf.Clamp(e, -1f, 1f)) * Mathf.Rad2Deg;
                    if (e > GimbalLimit) { ax = 0f; az = Atan2Deg(-m01, m11); }
                    else { ax = Atan2Deg(m21, m22); az = Atan2Deg(m10, m00); }
                    break;
            }
            return new Vector3(ax, ay, az);
        }

        /// <summary>
        /// Decompose with the optional wrap fix: when the alternate Euler solution
        /// (outer + 180°, middle 180° - middle) has a smaller sum of |outer angles|, it is returned
        /// instead (its middle angle then lies outside ±90°). fix = false is exactly <see cref="Decompose(Quaternion, JcOrder)"/>.
        /// </summary>
        public static Vector3 Decompose(Quaternion q, JcOrder o, bool fix)
        {
            var e = Decompose(q, o);
            if (!fix) return e;
            Letters(o, out int a, out int b, out int c);
            float ea = Wrap180(e[a] + 180f), ec = Wrap180(e[c] + 180f);
            if (Mathf.Abs(ea) + Mathf.Abs(ec) < Mathf.Abs(e[a]) + Mathf.Abs(e[c]))
            {
                float mb = e[b];
                e[a] = ea; e[c] = ec; e[b] = mb >= 0f ? 180f - mb : -180f - mb;
            }
            return e;
        }

        /// <summary>The alternate Euler solution of e for order o (same rotation; used by tests).</summary>
        public static Vector3 Alternate(Vector3 e, JcOrder o)
        {
            Letters(o, out int a, out int b, out int c);
            float mb = e[b];
            e[a] = Wrap180(e[a] + 180f); e[c] = Wrap180(e[c] + 180f); e[b] = mb >= 0f ? 180f - mb : -180f - mb;
            return e;
        }

        /// <summary>Angle wrapped into (-180, 180].</summary>
        public static float Wrap180(float a)
        {
            a %= 360f;
            if (a > 180f) a -= 360f;
            else if (a <= -180f) a += 360f;
            return a;
        }

        /// <summary>Compose_O(x, y, z): the axis rotations multiplied in the order's letter sequence, leftmost first.</summary>
        public static Quaternion Compose(Vector3 e, JcOrder o)
        {
            var rx = Quaternion.AngleAxis(e.x, Vector3.right);
            var ry = Quaternion.AngleAxis(e.y, Vector3.up);
            var rz = Quaternion.AngleAxis(e.z, Vector3.forward);
            switch (o)
            {
                case JcOrder.XYZ: return rx * ry * rz;
                case JcOrder.XZY: return rx * rz * ry;
                case JcOrder.YXZ: return ry * rx * rz;
                case JcOrder.YZX: return ry * rz * rx;
                case JcOrder.ZXY: return rz * rx * ry;
                default: return rz * ry * rx;   // ZYX
            }
        }

        /// <summary>World rotation from three axis columns with the trace formula only; identity when degenerate.</summary>
        public static Quaternion FromAxesTrace(Vector3 ax, Vector3 ay, Vector3 az)
        {
            float t1 = 1f + ax.x + ay.y + az.z;
            if (t1 == 0f) return Quaternion.identity;
            float w = Mathf.Sqrt(t1) * 0.5f;
            if (float.IsNaN(w)) return Quaternion.identity;
            float w4 = 4f * w;
            if (w4 == 0f) return Quaternion.identity;
            return new Quaternion((ay.z - az.y) / w4, (az.x - ax.z) / w4, (ax.y - ay.x) / w4, w);
        }

        /// <summary>Robust matrix -> quaternion (largest-diagonal branch); the deliberate fix.</summary>
        public static Quaternion FromAxesRobust(Vector3 ax, Vector3 ay, Vector3 az)
        {
            float m00 = ax.x, m11 = ay.y, m22 = az.z;
            float tr = m00 + m11 + m22;
            float x, y, z, w;
            if (tr > 0f)
            {
                float s = Mathf.Sqrt(tr + 1f) * 2f;
                w = 0.25f * s; x = (ay.z - az.y) / s; y = (az.x - ax.z) / s; z = (ax.y - ay.x) / s;
            }
            else if (m00 > m11 && m00 > m22)
            {
                float s = Mathf.Sqrt(1f + m00 - m11 - m22) * 2f;
                w = (ay.z - az.y) / s; x = 0.25f * s; y = (ay.x + ax.y) / s; z = (az.x + ax.z) / s;
            }
            else if (m11 > m22)
            {
                float s = Mathf.Sqrt(1f + m11 - m00 - m22) * 2f;
                w = (az.x - ax.z) / s; x = (ay.x + ax.y) / s; y = 0.25f * s; z = (az.y + ay.z) / s;
            }
            else
            {
                float s = Mathf.Sqrt(1f + m22 - m00 - m11) * 2f;
                w = (ax.y - ay.x) / s; x = (az.x + ax.z) / s; y = (az.y + ay.z) / s; z = 0.25f * s;
            }
            var q = new Quaternion(x, y, z, w);
            return Normalized(q);
        }

        /// <summary>Unit quaternion (identity for a zero / NaN input).</summary>
        public static Quaternion Normalized(Quaternion q)
        {
            float n = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (!(n > 1e-12f)) return Quaternion.identity;
            if (Mathf.Abs(n - 1f) < 1e-7f) return q;
            float k = 1f / n;
            return new Quaternion(q.x * k, q.y * k, q.z * k, q.w * k);
        }

        /// <summary>Strictly opposite signs (one &lt; 0, the other &gt; 0).</summary>
        public static bool Opposite(float a, float b) => (a < 0f && b > 0f) || (a > 0f && b < 0f);

        /// <summary>Exact component equality (Unity's == on quaternions is approximate).</summary>
        public static bool Same(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
    }
}

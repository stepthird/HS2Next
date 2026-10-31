// Angle primitives shared by the neck look and the eye look ("Notation").
// angle() uses atan2(|a x b|, a . b) instead of acos so that exactly parallel vectors give exactly 0
// (the AWAY side choice depends on the sign of an angle that is 0 for a centred target).
using UnityEngine;

namespace HS2Next.Motion.Look
{
    public static class LookMath
    {
        const float Tiny = 1e-15f;   // squared magnitude below which a vector counts as zero

        /// <summary>Unsigned angle in degrees between a and b (0 if either is near zero).</summary>
        public static float Angle(Vector3 a, Vector3 b)
        {
            if (a.sqrMagnitude < Tiny || b.sqrMagnitude < Tiny) return 0f;
            return Mathf.Atan2(Vector3.Cross(a, b).magnitude, Vector3.Dot(a, b)) * Mathf.Rad2Deg;
        }

        /// <summary>Component of a along n (zero if n is near zero).</summary>
        public static Vector3 Project(Vector3 a, Vector3 n)
        {
            float nn = n.sqrMagnitude;
            return nn < Tiny ? Vector3.zero : n * (Vector3.Dot(a, n) / nn);
        }

        /// <summary>signedAround(a, b, axis): angle between a and b with their axis components removed,
        /// negative when dot(axis, cross(a', b')) &lt; 0.</summary>
        public static float SignedAround(Vector3 a, Vector3 b, Vector3 axis)
        {
            var a2 = a - Project(a, axis);
            var b2 = b - Project(b, axis);
            float ang = Angle(a2, b2);
            return Vector3.Dot(axis, Vector3.Cross(a2, b2)) < 0f ? -ang : ang;
        }

        /// <summary>sgn(x) = -1 if x &lt; 0 else +1.</summary>
        public static float Sgn(float x) => x < 0f ? -1f : 1f;

        public static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        public static bool Finite(Quaternion q) => Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w);

        /// <summary>Bitwise-exact equality (Unity's == on quaternions is approximate).</summary>
        public static bool Same(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
    }
}

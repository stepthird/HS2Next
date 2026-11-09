// RotationCompare (Phase 6.10, the rotation audit): the angle between two rotations for checks with a bar under 1 degree.
// Quaternion.Angle answers exactly 0 when two rotations differ by less than about 0.16 degrees (|dot| > 1 - 1e-6), and Quaternion == / != use the
// same test without the absolute value (q == -q is false), so neither can judge a small bar, "equal" or "unchanged". Deg computes in double from the
// quaternions (the formula of ConstraintTest.Deg); its own check is YM2 in ConstraintTest_RotationCompare.cs (the row ConstraintTest).
using System;
using UnityEngine;

/// <summary>The angle between two rotations in degrees, in double, for the tests.</summary>
public static class RotationCompare
{
    /// <summary>
    /// The angle between two rotations in degrees, in double: 2 atan2(|v|, |w|) of the difference of the quaternions, so q and -q are the same
    /// rotation. 0 for equal components and for q against -q exactly (the same rotation); float rounding of the components shows at about
    /// 1e-5 degrees. A zero quaternion is no rotation: against anything it reads 180, as Quaternion.Angle reads it. A NaN component gives NaN,
    /// which meets no bar.
    /// </summary>
    public static double Deg(Quaternion a, Quaternion b)
    {
        double ax = -a.x, ay = -a.y, az = -a.z, aw = a.w, bx = b.x, by = b.y, bz = b.z, bw = b.w;
        if (ax * ax + ay * ay + az * az + aw * aw == 0 || bx * bx + by * by + bz * bz + bw * bw == 0) return 180;
        if (a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w) return 0;
        if (a.x == -b.x && a.y == -b.y && a.z == -b.z && a.w == -b.w) return 0;   // q against -q: the same rotation (the sums below round to about 1e-15)
        double x = aw * bx + ax * bw + ay * bz - az * by;
        double y = aw * by - ax * bz + ay * bw + az * bx;
        double z = aw * bz + ax * by - ay * bx + az * bw;
        double w = aw * bw - ax * bx - ay * by - az * bz;
        return 2.0 * Math.Atan2(Math.Sqrt(x * x + y * y + z * z), Math.Abs(w)) * 180.0 / Math.PI;
    }
}

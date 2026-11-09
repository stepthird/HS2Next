// ConstraintTest, the tests' angle between two rotations (Phase 6.10, the rotation audit; Editor/RotationCompare.cs):
//   YM2  RotationCompare.Deg: 0 for equal and for opposite quaternions; 0.01 degrees within 1e-6 for a rotation of 0.01 degrees about each axis, where
//        Quaternion.Angle reads 0 (read in the same check); 90 degrees within 1e-5 for a right angle about each axis (float components); a zero
//        quaternion reads 180 against identity, as Quaternion.Angle reads it. Numbers only, no host
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

public static partial class ConstraintTest
{
    static void Headless_F_RotationCompare()
    {
        Group("YM2", "RotationCompare.Deg", YM2);
    }

    static string YmR(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    static void YM2()
    {
        var axes = new[] { ("x", Vector3.right), ("y", Vector3.up), ("z", Vector3.forward) };
        // (a) equal and opposite quaternions
        var qs = new[] { Quaternion.identity, Quaternion.Euler(10, 20, 30), Quaternion.Euler(-75, 140, 5), Quaternion.AngleAxis(179.9f, new Vector3(1, 2, 3).normalized),
            Quaternion.AngleAxis(0.01f, Vector3.up), new Quaternion(0.5f, -0.5f, 0.5f, 0.5f) };
        var eq = qs.Select(q => RotationCompare.Deg(q, q)).ToList();
        var op = qs.Select(q => RotationCompare.Deg(q, new Quaternion(-q.x, -q.y, -q.z, -q.w))).ToList();
        Check("YM2 (a) equal and opposite quaternions read 0 (" + qs.Length + " rotations)", eq.All(v => v == 0) && op.All(v => v == 0),
            "equal [" + string.Join(", ", eq.Select(YmR)) + "], opposite [" + string.Join(", ", op.Select(YmR)) + "]");
        // (b) 0.01 degrees about each axis, where Quaternion.Angle reads 0
        var small = new List<string>(); bool okSmall = true, angleZero = true;
        foreach (var (name, axis) in axes)
        {
            var q = Quaternion.AngleAxis(0.01f, axis);
            double d = RotationCompare.Deg(Quaternion.identity, q), dn = RotationCompare.Deg(new Quaternion(-q.x, -q.y, -q.z, -q.w), Quaternion.identity);
            float qa = Quaternion.Angle(Quaternion.identity, q);
            okSmall &= Math.Abs(d - 0.01) <= 1e-6 && Math.Abs(dn - 0.01) <= 1e-6;
            angleZero &= qa == 0;
            small.Add($"{name}: Deg {YmR(d)} (against -q {YmR(dn)}), Quaternion.Angle {qa.ToString("R", CultureInfo.InvariantCulture)}");
        }
        Check("YM2 (b) 0.01 deg about each axis reads 0.01 within 1e-6; Quaternion.Angle of the same pair reads 0", okSmall && angleZero, string.Join("; ", small));
        // (c) a right angle about each axis (float components)
        var right = new List<string>(); bool okRight = true;
        foreach (var (name, axis) in axes)
        {
            var q = Quaternion.AngleAxis(90f, axis);
            double d = RotationCompare.Deg(Quaternion.identity, q), dr = RotationCompare.Deg(q, Quaternion.identity);
            okRight &= Math.Abs(d - 90) <= 1e-5 && Math.Abs(dr - 90) <= 1e-5;
            right.Add($"{name}: {YmR(d)} / {YmR(dr)}");
        }
        Check("YM2 (c) a right angle about each axis reads 90 within 1e-5", okRight, string.Join("; ", right));
        // (d) a zero quaternion is no rotation
        var zero = new Quaternion(0, 0, 0, 0);
        double z1 = RotationCompare.Deg(zero, Quaternion.identity), z2 = RotationCompare.Deg(Quaternion.Euler(10, 20, 30), zero), z3 = RotationCompare.Deg(zero, zero);
        float za = Quaternion.Angle(zero, Quaternion.identity);
        Check("YM2 (d) a zero quaternion reads 180 against identity, a rotation and itself, as Quaternion.Angle reads it against identity", z1 == 180 && z2 == 180 && z3 == 180 && Math.Abs(za - 180f) < 1e-3f,
            $"Deg {YmR(z1)}, {YmR(z2)}, {YmR(z3)}; Quaternion.Angle {za.ToString("R", CultureInfo.InvariantCulture)}");
        // printed, no verdict: 0.01 degrees on a posed rotation, where float rounding of the product shows
        var b = Quaternion.Euler(10, 20, 30);
        Info("YM2 0.01 deg after Euler(10, 20, 30): " + string.Join("; ", axes.Select(a => a.Item1 + " " + YmR(RotationCompare.Deg(b, b * Quaternion.AngleAxis(0.01f, a.Item2)))))
            + "; Quaternion.Angle " + string.Join(", ", axes.Select(a => Quaternion.Angle(b, b * Quaternion.AngleAxis(0.01f, a.Item2)).ToString("R", CultureInfo.InvariantCulture))));
    }
}

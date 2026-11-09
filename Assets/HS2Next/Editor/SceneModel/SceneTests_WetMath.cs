// WM2-WM5: a stain's arithmetic against fixed values, in double. WM2 the radius, its rounding, the drips' rate, a puddle's area and outline;
// WM3 the distance: anisotropy, the outer penalty, the noise; WM4 two stains in order are the composition of their two functions; WM5 the
// random numbers: a pinned sequence and no state. The pinned values were computed apart from this code, in arbitrary-precision integers.
using System;
using HS2Next.Scenes.Routes;
using HS2Next.Scenes.Wet;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save.Tests;

    public static class SceneTests_WetMath
    {
        const double Eps = 1e-12;

        static void Near(TestRunner t, double expected, double actual, string what) =>
            t.Check(Math.Abs(expected - actual) <= Eps, what + " (expected " + expected.ToString("R") + ", got " + actual.ToString("R") + ")");

        public static void Run(TestRunner t)
        {
            Radius(t);
            Amount(t);
            Drips(t);
            Puddles(t);
            Distance(t);
            Outer(t);
            Noise(t);
            Order(t);
            Random(t);
            Signature(t);
        }

        // ---------- WM2 ----------

        static void Radius(TestRunner t)
        {
            Near(t, 0, WetMath.Radius(0, 6.04, 2.2), "WM2: R is 0 at t 0");
            Near(t, 2.2 * Math.Sqrt(.5), WetMath.Radius(3.02, 6.04, 2.2), "WM2: R at half the spread");
            Near(t, 2.2, WetMath.Radius(6.04, 6.04, 2.2), "WM2: R at the spread");
            Near(t, 2.2, WetMath.Radius(600, 6.04, 2.2), "WM2: R stays at R_max");
            Near(t, 2.2, WetMath.Radius(.001, 0, 2.2), "WM2: spread 0 gives R_max at once");
            Near(t, 0, WetMath.Radius(0, 0, 2.2), "WM2: spread 0 and t 0 is dry");
            Near(t, 0, WetMath.Radius(-1, 6.04, 2.2), "WM2: a negative t is dry");

            Near(t, 199 / 128.0, WetMath.DrawnRadius(3.02, 6.04, 2.2), "WM2: a drawn R is a multiple of 1 / 128");
            Near(t, 2.2, WetMath.DrawnRadius(6.04, 6.04, 2.2), "WM2: R_max is drawn exact, not rounded");
            t.Check(WetMath.DrawnRadius(6.03, 6.04, 2.2) <= 2.2, "WM2: rounding never passes R_max");
            int changes = 0;
            double last = -1;
            for (int i = 0; i <= 3600; i++)
            {
                double r = WetMath.DrawnRadius(i / 60.0, 6.04, 2.2);
                if (r != last) changes++;
                last = r;
            }
            t.Check(changes <= 2.2 * 128 + 2, "WM2: a spread draws at most one step per 1 / 128 unit (" + changes + ")");

            Near(t, 2.895, WetMath.Reach(2.2, .195), "WM2: reach");
        }

        static void Amount(TestRunner t)
        {
            double reach = 2.895;
            Near(t, 0, WetMath.Amount(255, 2.2, .195, reach), "WM2: an unreached texel shows nothing");
            Near(t, 0, WetMath.Amount(0, 0, .195, reach), "WM2: R 0 is fully dry");
            Near(t, 1, WetMath.Amount(0, 2.2, .195, reach), "WM2: inside the edge a texel is full");
            double d = 100 / 254.0 * reach;
            Near(t, .5, WetMath.Amount(100, d + .0975, .195, reach), "WM2: half the feather gives half");
            t.Equal(0, WetMath.MapValue(-1, reach), "WM2: a negative distance maps to 0");
            t.Equal(254, WetMath.MapValue(reach, reach), "WM2: the reach maps to 254");
            t.Equal(254, WetMath.MapValue(reach * 3, reach), "WM2: past the reach stays 254");
            t.Equal(127, WetMath.MapValue(reach / 2, reach), "WM2: half the reach");
            t.Equal(100, WetMath.MapValue(WetMath.MapDistance(100, reach), reach), "WM2: a value survives its distance");
        }

        static void Drips(TestRunner t)
        {
            Near(t, 0, WetMath.DripIntensity(0, 6, 8), "WM2: no drip at t 0");
            Near(t, .5, WetMath.DripIntensity(.9, 6, 8), "WM2: rising over .3 of the spread");
            Near(t, 1, WetMath.DripIntensity(1.8, 6, 8), "WM2: risen");
            Near(t, 1, WetMath.DripIntensity(6, 6, 8), "WM2: holds to the end of the spread");
            Near(t, .25, WetMath.DripIntensity(10, 6, 8), "WM2: the tail at its half is a quarter");
            Near(t, 0, WetMath.DripIntensity(14, 6, 8), "WM2: the tail ends");
            Near(t, 0, WetMath.DripIntensity(20, 6, 8), "WM2: nothing after the tail");
            Near(t, .5, WetMath.DripIntensity(.25, 1, 8), "WM2: the rise is at least .5 s");
            Near(t, 0, WetMath.DripIntensity(7, 6, 0), "WM2: no tail stops at the spread");

            t.Equal(0L, WetMath.DropStepOf(0), "WM2: step 0");
            t.Equal(60L, WetMath.DropStepOf(1), "WM2: 60 steps a second");
            t.Equal(1L, WetMath.DropStepOf(1 / 60.0), "WM2: one step");
            t.Equal(60L, WetMath.DropStepOf((float)(1 / 60.0) * 60f), "WM2: a float time keeps its step");
            t.Equal(0, WetMath.DropsInStep(1, 10, 0, 1), "WM2: rate 0 emits nothing");
            t.Equal(0, WetMath.DropsInStep(1, 10, 18, 0), "WM2: intensity 0 emits nothing");
            int sum = 0;
            for (int i = 0; i < 6000; i++) sum += WetMath.DropsInStep(3, i, 18, 1);
            t.Check(Math.Abs(sum - 1800) < 90, "WM2: 18 a second over 100 s comes to about 1,800 (" + sum + ")");
            t.Equal(WetMath.DropsInStep(3, 77, 18, 1), WetMath.DropsInStep(3, 77, 18, 1), "WM2: a step's count has no state");
        }

        static void Puddles(TestRunner t)
        {
            Near(t, .04 * (.031 / .07) * (.031 / .07), WetMath.AreaOf(1, .031), "WM2: a droplet's area at the default size");
            Near(t, .04, WetMath.AreaOf(1, .07), "WM2: a droplet of .07 adds .04");
            Near(t, .08, WetMath.AreaOf(2, .07), "WM2: growth scales it");
            Near(t, 1.5 * Math.Sqrt(1 / Math.PI), WetMath.PuddleRadius(1, 1.5), "WM2: a puddle's radius");
            Near(t, 0, WetMath.PuddleRadius(-1, 1.5), "WM2: no area, no radius");
            Near(t, 1, WetMath.Outline(0, 0), "WM2: the outline at 0");
            Near(t, 1 + .16 * Math.Sin(3 + 2) + .09 * Math.Sin(7 + 3.4), WetMath.Outline(1, 2), "WM2: the outline at fixed values");

            double[] area = { 1, 1 }, xs = { 0, 10 }, zs = { 0, 0 };
            var far = WetMath.Join(5, 5, .1, 1, 2, i => area[i], i => xs[i], i => zs[i]);
            t.Equal(-1, far.Index, "WM2: a droplet far from every puddle starts one");
            Near(t, .1, far.Area, "WM2: a new puddle holds the droplet's area");
            double edge = WetMath.PuddleRadius(1, 1) + .59;
            var near = WetMath.Join(edge, 0, .25, 1, 2, i => area[i], i => xs[i], i => zs[i]);
            t.Equal(0, near.Index, "WM2: within the edge + .6 it joins");
            Near(t, 1.25, near.Area, "WM2: the area grows");
            Near(t, edge * (.25 / 1.25) * .5, near.X, "WM2: the centre moves by half the droplet's share");
            t.Equal(-1, WetMath.Join(WetMath.PuddleRadius(1, 1) + .61, 0, .1, 1, 1, i => area[i], i => xs[i], i => zs[i]).Index, "WM2: past the edge + .6 it does not");
            t.Equal(1, WetMath.Join(8, 0, .1, 3, 2, i => area[i], i => xs[i], i => zs[i]).Index, "WM2: the nearest of two");
        }

        // ---------- WM3 ----------

        // a frame on the axes: right +x, up +y, forward +z, the legs straight down from x = ±1
        static WetFrame Frame() => new WetFrame
        {
            Right = new Vec3d(1, 0, 0),
            Up = new Vec3d(0, 1, 0),
            Forward = new Vec3d(0, 0, 1),
            Mid = Vec3d.Zero,
            Scale = 1,
            HipL = new Vec3d(-1, 0, 0),
            KneeL = new Vec3d(-1, -4.2, 0),
            HipR = new Vec3d(1, 0, 0),
            KneeR = new Vec3d(1, -4.2, 0),
        };

        static readonly WetDistanceValues Plain = new WetDistanceValues { Up = 3, Down = .75, Front = 1, Back = 1.3, Side = 3 };

        static void Distance(TestRunner t)
        {
            var f = Frame();
            var o = new Vec3d(0, 10, 0);
            Near(t, 0, WetMath.Distance(f, Plain, o, o), "WM3: the origin is at 0");
            Near(t, 6, WetMath.Distance(f, Plain, o, o + new Vec3d(0, 2, 0)), "WM3: up scales by 3");
            Near(t, 1.5, WetMath.Distance(f, Plain, o, o + new Vec3d(0, -2, 0)), "WM3: down by .75");
            Near(t, 2, WetMath.Distance(f, Plain, o, o + new Vec3d(0, 0, 2)), "WM3: front by 1");
            Near(t, 2.6, WetMath.Distance(f, Plain, o, o + new Vec3d(0, 0, -2)), "WM3: back by 1.3");
            Near(t, 6, WetMath.Distance(f, Plain, o, o + new Vec3d(2, 0, 0)), "WM3: right by 3");
            Near(t, 6, WetMath.Distance(f, Plain, o, o + new Vec3d(-2, 0, 0)), "WM3: left by 3");
            Near(t, Math.Sqrt(9 + 1.5 * 1.5), WetMath.Distance(f, Plain, o, o + new Vec3d(1, -2, 0)), "WM3: the axes add as a length");

            var noisy = Plain;
            noisy.Noise = .35;
            var p = o + new Vec3d(1, 1, 1);
            Near(t, WetMath.Distance(f, Plain, o, p) - .35 * WetMath.Noise(p), WetMath.Distance(f, noisy, o, p), "WM3: the noise is taken off, at scale 1");
            var half = f;
            half.Scale = .05;
            Near(t, WetMath.Distance(half, Plain, o, p) - .35 * WetMath.Noise(p * 10), WetMath.Distance(half, noisy, o, p), "WM3: the noise's scale stops at .1");
        }

        static void Outer(TestRunner t)
        {
            var f = Frame();
            Near(t, 0, WetMath.Outer(f, 1.5, new Vec3d(0, 5, 0)), "WM3: nothing above the legs");
            Near(t, 0, WetMath.Outer(f, 1.5, new Vec3d(.5, -2, 0)), "WM3: the inner side of the right leg costs nothing");
            Near(t, 3, WetMath.Outer(f, 1.5, new Vec3d(1.5, -2, 0)), "WM3: the outer side costs twice `outer`");
            Near(t, 1.5, WetMath.Outer(f, 1.5, new Vec3d(1, -2, 1)), "WM3: the front of the leg costs `outer`");
            Near(t, 3, WetMath.Outer(f, 1.5, new Vec3d(-1.5, -2, 0)), "WM3: the left leg likewise");
            Near(t, 1.5, WetMath.Outer(f, 1.5, new Vec3d(1.5, 0, 0)), "WM3: at the top of the thigh the fade stands at half");
            Near(t, 0, WetMath.Outer(f, 1.5, new Vec3d(1.5, .15 * 4.2, 0)), "WM3: at -.15 of the length it fades out");
            var big = f;
            big.Scale = 2;
            Near(t, 6, WetMath.Outer(big, 1.5, new Vec3d(1.5, -2, 0)), "WM3: the body's scale scales it");
            // far past the knee the axis point stays at 1.6 of the thigh's length
            Near(t, 1.5 * (1 + .5 / Math.Sqrt(.25 + 13.28 * 13.28)), WetMath.Outer(f, 1.5, new Vec3d(1.5, -20, 0)), "WM3: past the clamp the axis point stays");
        }

        static void Noise(TestRunner t)
        {
            // pinned: 32-bit wrapping integers, an arithmetic shift
            Near(t, -1, WetMath.Lattice(0, 0, 0), "WM3: the lattice at 0");
            Near(t, Pin1, WetMath.Lattice(1, 2, 3), "WM3: the lattice at (1, 2, 3)");
            Near(t, Pin2, WetMath.Lattice(-7, 11, -13), "WM3: the lattice at (-7, 11, -13)");
            Near(t, Pin3, WetMath.Lattice(100000, -200000, 300000), "WM3: the lattice wraps");
            Near(t, WetMath.Lattice(2, 3, 4), WetMath.ValueNoise(new Vec3d(2, 3, 4)), "WM3: value noise is the lattice at a lattice point");
            Near(t, Pin4, WetMath.ValueNoise(new Vec3d(.25, .5, .75)), "WM3: value noise between points");
            Near(t, Pin5, WetMath.Noise(new Vec3d(1.5, -2.25, 3.75)), "WM3: the two octaves");
            double lo = 9, hi = -9;
            for (int i = 0; i < 20000; i++)
            {
                double v = WetMath.Noise(new Vec3d(WetMath.Rng(1, i) * 40 - 20, WetMath.Rng(2, i) * 40 - 20, WetMath.Rng(3, i) * 40 - 20));
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
            t.Check(lo >= -1 && hi <= 1 && lo < -.4 && hi > .4, "WM3: the noise stays in -1..1 and uses it (" + lo + ", " + hi + ")");
        }

        const double Pin1 = -0.20680552376592665, Pin2 = -0.16035706111238268, Pin3 = 0.9505607690547035, Pin4 = -0.34866160198271917, Pin5 = 0.1527187833915566;

        // ---------- WM4 ----------

        static void Order(TestRunner t)
        {
            var blood = new WetColourValues { Saturate = 1, Tint = new WetRgb(1, 1, 1), Stain = new WetRgb(.42, .03, .04), Amount = .85, Skin = .85 };
            var water = new WetColourValues { Saturate = 1.15, Tint = new WetRgb(1, 1, 1), Stain = new WetRgb(0, 0, 0), Amount = .3, Skin = .15 };
            var c = new WetRgb(.5, .25, .75);

            double l = .3 * .5 + .59 * .25 + .11 * .75;
            Near(t, .38, l, "WM4: the lightness");
            var w = WetMath.WetColour(c, water);
            Near(t, (l + (.5 - l) * 1.15) * .7, w.R, "WM4: water, red");
            Near(t, (l + (.25 - l) * 1.15) * .7, w.G, "WM4: water, green");
            Near(t, (l + (.75 - l) * 1.15) * .7, w.B, "WM4: water, blue");
            var b = WetMath.WetColour(c, blood);
            double k = .55 + .45 * l;
            Near(t, .5 * .15 + .42 * k * .85, b.R, "WM4: blood, red");
            Near(t, .25 * .15 + .03 * k * .85, b.G, "WM4: blood, green");
            Near(t, .75 * .15 + .04 * k * .85, b.B, "WM4: blood, blue");

            var none = blood;
            none.Amount = 0;
            none.Stain = new WetRgb(9, 9, 9);
            var n = WetMath.WetColour(c, none);
            Near(t, .5, n.R, "WM4: amount 0 leaves the stain out");

            var at0 = WetMath.Over(c, blood, 0, false);
            Near(t, c.R, at0.R, "WM4: a 0 changes nothing");
            var full = WetMath.Over(c, blood, 1, false);
            Near(t, b.R, full.R, "WM4: a 1 is the wet colour");
            var skin = WetMath.Over(c, water, 1, true);
            Near(t, .5 + (w.R - .5) * .15, skin.R, "WM4: on skin the factor weighs it");

            // water over blood is the two functions in order; the other order differs
            var first = WetMath.Over(c, blood, .7, false);
            var second = WetMath.Over(first, water, .4, false);
            double l1 = WetMath.Lightness(first);
            double r1 = (l1 + (first.R - l1) * 1.15) * .7;
            Near(t, first.R + (r1 - first.R) * .4, second.R, "WM4: water over blood composes");
            var other = WetMath.Over(WetMath.Over(c, water, .4, false), blood, .7, false);
            t.Check(Math.Abs(other.R - second.R) > 1e-3, "WM4: the order shows");
            t.Check(second.R < first.R, "WM4: water darkens the blood");
        }

        // ---------- WM5 ----------

        static void Random(TestRunner t)
        {
            double[] pin = { 0.43152799704850997, 0.7486828894513605, 0.0937509027694372, 0.6122551337059955 };
            for (int i = 0; i < pin.Length; i++) Near(t, pin[i], WetMath.Rng(1, i), "WM5: seed 1, number " + i);
            Near(t, 0.25670870597490125, WetMath.Rng(-5, 1234567890123L), "WM5: a negative seed and a long index");
            t.Equal(WetMath.Rng(9, 4), WetMath.Rng(9, 4), "WM5: no state");
            t.Check(WetMath.Rng(9, 4) != WetMath.Rng(10, 4), "WM5: the seed matters");
            t.Check(WetMath.Rng(9, 4) != WetMath.Rng(9, 5), "WM5: the index matters");
            double sum = 0, lo = 1, hi = 0;
            bool inside = true;
            for (int i = 0; i < 100000; i++)
            {
                double v = WetMath.Rng(7, i);
                inside &= v >= 0 && v < 1;
                sum += v;
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
            t.Check(inside, "WM5: in [0, 1)");
            t.Check(Math.Abs(sum / 100000 - .5) < .01, "WM5: the mean is about .5");
            t.Check(lo < .001 && hi > .999, "WM5: the range is used");
        }

        static void Signature(TestRunner t)
        {
            t.Equal("811c9dc5", WetMath.UvHash(new float[0]), "WM2: FNV-1a of nothing");
            t.Equal("811c9dc5", WetMath.UvHash(null), "WM2: no UV");
            t.Check(WetMath.UvHash(new[] { 0f, 1f }) != WetMath.UvHash(new[] { 1f, 0f }), "WM2: the order of the floats counts");
            t.Equal("o_bot_a|1847|8f3a11c2", WetMath.Signature("o_bot_a", 1847, "8f3a11c2"), "WM2: the signature's text");
        }
    }
}

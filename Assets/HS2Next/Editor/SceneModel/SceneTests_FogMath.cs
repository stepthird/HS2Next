// FG0: the CPU reference of the height fog (FogMath) against hand values: the three modes in the engine's and the
// post-processing stack's forms, the half-space height term's four branches (camera below / above the fog top, the point
// below / above it), a horizontal view (no NaN), excludeFar, far pixels of the distance term, the forward distance and the
// product rule of the two opaque terms.
using System;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save.Tests;
    using HS2Next.Scenes.Runtime;

    public static class SceneTests_FogMath
    {
        public static void Run(TestRunner t)
        {
            Modes(t);
            Height(t);
            Terms(t);
            Product(t);
        }

        static bool Near(double a, double b, double eps = 2e-5) => Math.Abs(a - b) <= eps;

        static void Modes(TestRunner t)
        {
            // the engine's form: exp(-d x) and exp(-(d x)^2) through exp2 with the 1 / ln 2 and 1 / sqrt(ln 2) scales
            t.Check(Near(FogMath.Factor(FogMath.Exp2, 0.001f, 0f, 300f, 100f), Math.Exp(-0.01)), "FG0 Exp2 factor = exp(-(d x)^2) (d 0.001, x 100)");
            t.Check(Near(FogMath.Factor(FogMath.Exp, 0.001f, 0f, 300f, 100f), Math.Exp(-0.1)), "FG0 Exp factor = exp(-d x)");
            t.Check(Near(FogMath.Factor(FogMath.Linear, 0.5f, 0f, 300f, 100f), 2.0 / 3.0), "FG0 Linear factor = (end - x) / (end - start), density ignored");
            t.Check(FogMath.Factor(FogMath.Linear, 0f, 0f, 300f, 400f) == 0f && FogMath.Factor(FogMath.Linear, 0f, 50f, 300f, 10f) == 1f, "FG0 Linear factor saturated at both ends");
            t.Check(FogMath.Factor(FogMath.Linear, 0f, 100f, 100f, 50f) == 0f, "FG0 Linear with end == start: no slope (factor 0)");
            t.Check(FogMath.Factor(FogMath.Exp2, 0.0005f, 0f, 300f, 0f) == 1f, "FG0 distance 0: factor 1");
            // the post-processing stack's form: exp2 without the scales
            t.Check(Near(FogMath.PostFactor(FogMath.Exp2, 0.001f, 0f, 300f, 100f), Math.Pow(2, -0.01)), "FG0 post Exp2 factor = 2^-(d x)^2");
            t.Check(Near(FogMath.PostFactor(FogMath.Exp, 0.001f, 0f, 300f, 100f), Math.Pow(2, -0.1)), "FG0 post Exp factor = 2^-(d x)");
            t.Check(Near(FogMath.PostFactor(FogMath.Linear, 0f, 0f, 300f, 150f), 0.5), "FG0 post Linear factor");
            t.Check(FogMath.ModeBit(1) == 1 && FogMath.ModeBit(2) == 2 && FogMath.ModeBit(3) == 4 && FogMath.ModeBit(0) == 0 && FogMath.ModeBit(4) == 0, "FG0 mode bits 1 / 2 / 4");
        }

        static void Height(TestRunner t)
        {
            const float top = 5f, hd = 0.2f;   // |a V| = 0.1 |V|
            // camera above the top, point below: g = |aV| FdotP^2 / |FdotV|
            double a = 0.1 * Math.Sqrt(200);
            t.Check(Near(FogMath.HeightTerm(0, 10, 0, 0, 0, -10, top, hd), a * 25 / 10, 1e-4), "FG0 height: camera above, point below the top");
            t.Check(FogMath.HeightTerm(0, 10, 0, 0, 7, -10, top, hd) == 0f, "FG0 height: camera and point above the top: 0");
            // camera below: g = -|aV| (FdotP + FdotC) when the point is below too
            double b = 0.1 * Math.Sqrt(4 + 100);
            t.Check(Near(FogMath.HeightTerm(0, 0, 0, 0, 2, -10, top, hd), 8 * b, 1e-4), "FG0 height: camera and point below the top");
            // camera below, point above: g = -|aV| ((FdotP + FdotC) - FdotP^2 / |FdotV|) = |aV| 2.5
            t.Check(Near(FogMath.HeightTerm(0, 0, 0, 0, 10, -10, top, hd), 2.5 * a, 1e-4), "FG0 height: camera below, point above the top");
            // horizontal views: finite values (the 1e-5 guard)
            float h1 = FogMath.HeightTerm(0, 0, 0, 0, 0, -10, top, hd), h2 = FogMath.HeightTerm(0, 5, 0, 0, 5, -10, top, hd);
            t.Check(Near(h1, 10, 1e-4) && h2 == 0f, $"FG0 height: horizontal below the top = 10 |aV| ({h1}), at the top = 0 ({h2})");
            bool finite = true;
            for (int i = -20; i <= 20; i++)
                for (int j = -20; j <= 20; j++)
                {
                    float v = FogMath.HeightTerm(0, 5f + i * 0.25f, 0, 3, 5f + j * 0.25f + 1e-6f * i, -7, top, 10f);
                    if (float.IsNaN(v) || float.IsInfinity(v)) finite = false;
                }
            t.Check(finite, "FG0 height: no NaN / infinity near FdotV = 0 (41 x 41 grid, height density 10)");
        }

        static void Terms(TestRunner t)
        {
            // B: radial distance minus near plus the height term, factor of the mode
            float fb = FogMath.ImageFogFactor(FogMath.Exp2, 0.001f, 0f, 300f, 0.1f, 0, 10, 0, 0, 0, -10, 5f, 0.2f, false, false);
            double g = Math.Sqrt(200) - 0.1 + 0.1 * Math.Sqrt(200) * 2.5;
            t.Check(Near(fb, Math.Exp(-Math.Pow(0.001 * g, 2)), 1e-5), $"FG0 image term = mode(radial - near + height) ({fb})");
            t.Check(FogMath.ImageFogFactor(FogMath.Exp2, 0.001f, 0f, 300f, 0.1f, 0, 10, 0, 0, 0, -15000, 5f, 0.2f, true, true) == 1f, "FG0 image term: excludeFar keeps far pixels");
            float farF = FogMath.ImageFogFactor(FogMath.Exp2, 0.001f, 0f, 300f, 0.1f, 0, 1, 0, 0, 1, -15000, 50f, 0.01f, false, true);
            t.Check(farF < 1e-6f, $"FG0 image term: far pixels without excludeFar become the fog colour ({farF})");
            // A: z distance (depth01 far - near), sky skipped
            float fa = FogMath.PostFogFactor(FogMath.Exp2, 0.001f, 0f, 300f, 100f / 15000f, 0.1f, 15000f);
            t.Check(Near(fa, Math.Pow(2, -Math.Pow(0.001 * (100 - 0.1), 2)), 1e-5), $"FG0 post term = mode(depth - near) ({fa})");
            float faLin = FogMath.PostFogFactor(FogMath.Linear, 0.005f, 0f, 3000f, 50.1f / 15000f, 0.1f, 15000f);
            t.Check(Near(faLin, Math.Pow(2, -Math.Pow(0.005 * 50, 2)), 1e-4) && FogMath.PostMode(FogMath.Exp) == FogMath.Exp && FogMath.PostMode(FogMath.Linear) == FogMath.Exp2,
                $"FG0 post term on a linear scene is exponential squared with the density ({faLin}); exponential scenes keep exponential");
            t.Check(FogMath.PostFogFactor(FogMath.Exp2, 0.01f, 0f, 300f, 0.99995f, 0.1f, 15000f) == 1f && FogMath.PostFogFactor(FogMath.Exp2, 0.01f, 0f, 300f, 1f, 0.1f, 15000f) == 1f,
                "FG0 post term skips sky / far pixels (depth01 >= 0.9999)");
            float op = FogMath.OpaqueFactor(FogMath.Exp2, 0.001f, 0f, 300f, 0.1f, 15000f, 1f, 0, 1, 0, 0, 1, -15000, 50f, 0.01f, true);
            t.Check(op == 1f, "FG0 opaque factor: far pixel with excludeFar = 1 (both terms keep it)");
            // forward distance: the clip-space form
            t.Check(Near(FogMath.ForwardDistance(100f, 0.08f, 15000f), (100 - 0.08) * 15000 / (15000 - 0.08), 1e-3) && FogMath.ForwardDistance(0.01f, 0.08f, 15000f) == 0f,
                "FG0 forward distance = (depth - near) far / (far - near), clamped at 0");
            t.Check(Near(FogMath.ForwardFactor(FogMath.Exp, 0.001f, 0f, 300f, 100.08f, 0.08f, 15000f), Math.Exp(-0.001 * 100.000533), 1e-5), "FG0 forward factor (Exp)");
        }

        static void Product(TestRunner t)
        {
            // lerp(F, lerp(F, c, a), b) = lerp(F, c, a b): two terms toward one colour compose to one factor
            float worst = 0;
            foreach (float F in new[] { 0f, 0.3f, 1f })
                foreach (float c in new[] { 0f, 0.5f, 2f })
                    foreach (float x in new[] { 0f, 0.25f, 0.9f, 1f })
                        foreach (float y in new[] { 0f, 0.5f, 1f })
                            worst = Math.Max(worst, Math.Abs(FogMath.Apply(F, FogMath.Apply(F, c, x), y) - FogMath.Apply(F, c, x * y)));
            t.Check(worst < 1e-6f, $"FG0 product rule: two fog terms toward one colour = one factor a b (worst {worst:E1})");
            t.Check(FogMath.Apply(0f, 0.8f, 0.5f) == 0.4f, "FG0 additive form: toward black");
        }
    }
}

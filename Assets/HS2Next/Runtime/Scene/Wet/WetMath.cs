// The arithmetic of a stain, in double and without the engine: the radius at a moment, the amount a distance map gives at that radius, the
// colour a stain lays over what is under it, the distance of a point from an origin, the noise and the random numbers of a stain, the
// drips' rate and the puddles' growth. Nothing here keeps time or state: a value is a function of what it is given, which is why a seek, a
// load and a capture show the same stain.
//   radius     R(t) = R_max · sqrt(clamp01(t / spread)), R_max at once with spread 0; drawn at multiples of 1 / 128, R_max exact
//   amount     a = 0 at a map value of 255 or with R ≤ 0, else clamp01((R − d) / feather), d = v / 254 · reach
//   colour     w = (l + (c − l) · saturate) · tint, then lerp(w, stain · (.55 + .45 l), amount); out = lerp(c, w, a · skin factor)
//   distance   sqrt(x² + y² + z²) + outer(p) − noise(p / max(.1, scale)) · noise, each axis of the pelvis frame scaled per direction
// A distance is in units (1 unit = 10 cm).
using System;
using System.Globalization;
using HS2Next.Scenes.Routes;

namespace HS2Next.Scenes.Wet
{
    /// <summary>An RGB colour of doubles, sRGB-encoded where a stain's arithmetic uses it.</summary>
    public struct WetRgb
    {
        public double R, G, B;

        public WetRgb(double r, double g, double b) { R = r; G = g; B = b; }

        public static WetRgb Lerp(WetRgb a, WetRgb b, double t) =>
            new WetRgb(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
    }

    /// <summary>The look of a stain as numbers (the members of the document's <c>look</c>).</summary>
    public struct WetColourValues
    {
        public double Saturate, Amount, Skin;
        public WetRgb Tint, Stain;
    }

    /// <summary>The members of a stain's <c>shape</c> a distance reads.</summary>
    public struct WetDistanceValues
    {
        public double Up, Down, Front, Back, Side, Outer, Noise;
    }

    /// <summary>The pelvis frame of a character at one moment: unit axes, the body's scale and the two legs.</summary>
    public struct WetFrame
    {
        /// <summary>The unit axes of the frame.</summary>
        public Vec3d Right, Up, Forward;
        /// <summary>The midpoint of the hips, and the body's scale (thigh length / 4.2).</summary>
        public Vec3d Mid;
        public double Scale;
        /// <summary>The top of each thigh and the bone below the knee.</summary>
        public Vec3d HipL, KneeL, HipR, KneeR;
    }

    /// <summary>The answer of <see cref="WetMath.Join"/>: the puddle a droplet goes to.</summary>
    public struct WetPuddleStep
    {
        /// <summary>The index of the puddle it joins, -1 for a new one.</summary>
        public int Index;
        /// <summary>The area and centre after the droplet landed.</summary>
        public double Area;
        public double X, Z;
    }

    public static class WetMath
    {
        // ---------- what the document's look and the map's bytes mean ----------

        /// <summary>A map's value where nothing reaches.</summary>
        public const int Unreached = 255;
        /// <summary>The largest value of a reached texel.</summary>
        public const int MapScale = 254;
        /// <summary>The bake draws distances at this fraction in its 16-bit target.</summary>
        public const double BakeScale = 1.0 / 16.0;
        /// <summary>The step of a drawn radius.</summary>
        public const double RadiusStep = 1.0 / 128.0;
        /// <summary>The fixed step of the drops, in seconds of a stain's clock.</summary>
        public const double DropStep = 1.0 / 60.0;
        /// <summary>How far a droplet falls per second squared, in units.</summary>
        public const double Gravity = 98.1;
        /// <summary>A droplet's reach beyond a puddle's radius when it looks for one to join.</summary>
        public const double JoinMargin = 0.6;
        /// <summary>The lowest point counts this much more for each drop of this height (<c>exp(−Δy / .6)</c>).</summary>
        public const double LowFall = 0.6;

        public static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        /// <summary>How far a map reaches for a largest radius and an edge: what value 254 stands for.</summary>
        public static double Reach(double maxRadius, double feather) => maxRadius + feather + 0.5;

        /// <summary>The radius at a moment.</summary>
        public static double Radius(double t, double spread, double maxRadius)
        {
            if (maxRadius <= 0) return 0;
            if (spread <= 0) return t > 0 ? maxRadius : 0;
            return maxRadius * Math.Sqrt(Clamp01(t / spread));
        }

        /// <summary>A radius as it is drawn: a multiple of <see cref="RadiusStep"/>, the largest radius exact.</summary>
        public static double DrawnRadius(double t, double spread, double maxRadius)
        {
            double r = Radius(t, spread, maxRadius);
            if (r >= maxRadius) return maxRadius;
            double q = Math.Round(r / RadiusStep, MidpointRounding.AwayFromZero) * RadiusStep;
            return q > maxRadius ? maxRadius : q;
        }

        /// <summary>The distance a map's value stands for.</summary>
        public static double MapDistance(int v, double reach) => v / (double)MapScale * reach;

        /// <summary>The map's value of a distance, as the bake's last pass writes it.</summary>
        public static int MapValue(double d, double reach)
        {
            if (!(reach > 0)) return Unreached;
            return (int)Math.Round(MapScale * Clamp01(Math.Max(d, 0) / reach), MidpointRounding.AwayFromZero);
        }

        /// <summary>What a stain shows on a texel of map value <paramref name="v"/> at radius <paramref name="r"/>.</summary>
        public static double Amount(int v, double r, double feather, double reach)
        {
            if (v >= Unreached || r <= 0) return 0;
            double d = MapDistance(v, reach);
            return feather > 0 ? Clamp01((r - d) / feather) : r >= d ? 1 : 0;
        }

        // ---------- colour ----------

        /// <summary>The weight of a colour's lightness.</summary>
        public static double Lightness(WetRgb c) => .3 * c.R + .59 * c.G + .11 * c.B;

        /// <summary>The fully wet colour of <paramref name="c"/> under a look.</summary>
        public static WetRgb WetColour(WetRgb c, in WetColourValues look)
        {
            double l = Lightness(c);
            var w = new WetRgb((l + (c.R - l) * look.Saturate) * look.Tint.R,
                               (l + (c.G - l) * look.Saturate) * look.Tint.G,
                               (l + (c.B - l) * look.Saturate) * look.Tint.B);
            if (look.Amount > 0)
            {
                double k = .55 + .45 * l;
                w = WetRgb.Lerp(w, new WetRgb(look.Stain.R * k, look.Stain.G * k, look.Stain.B * k), look.Amount);
            }
            return w;
        }

        /// <summary>A stain over <paramref name="c"/> at amount <paramref name="a"/>; <paramref name="skinKind"/> for the kinds with a skin factor.</summary>
        public static WetRgb Over(WetRgb c, in WetColourValues look, double a, bool skinKind) =>
            WetRgb.Lerp(c, WetColour(c, look), skinKind ? a * look.Skin : a);

        // ---------- distance ----------

        /// <summary>The distance of a point from an origin, scaled per direction, with the outer penalty and the noise.</summary>
        public static double Distance(in WetFrame f, in WetDistanceValues s, Vec3d origin, Vec3d p)
        {
            var q = p - origin;
            double y = Vec3d.Dot(q, f.Up), z = Vec3d.Dot(q, f.Forward), x = Vec3d.Dot(q, f.Right);
            y *= y > 0 ? s.Up : s.Down;
            z *= z > 0 ? s.Front : s.Back;
            x *= s.Side;
            return Math.Sqrt(x * x + y * y + z * z) + Outer(f, s.Outer, p) - Noise(p * (1.0 / Math.Max(.1, f.Scale))) * s.Noise;
        }

        /// <summary>What going round a leg to its outer side adds, on the nearer leg; 0 above the legs.</summary>
        public static double Outer(in WetFrame f, double outer, Vec3d p)
        {
            double best = double.MaxValue, cos = 1, tt = 0;
            bool any = false;
            for (int leg = 0; leg < 2; leg++)
            {
                Vec3d hip = leg == 0 ? f.HipL : f.HipR, knee = leg == 0 ? f.KneeL : f.KneeR, other = leg == 0 ? f.HipR : f.HipL;
                var axis = knee - hip;
                double len2 = axis.SqrMagnitude;
                if (len2 <= 0) continue;
                double at = Vec3d.Dot(p - hip, axis) / len2;
                if (at < -.15) continue;
                double clamped = at < 0 ? 0 : at > 1.6 ? 1.6 : at;
                var radial = p - (hip + axis * clamped);
                double mag = radial.Magnitude;
                if (mag >= best) continue;
                best = mag;
                tt = at;
                any = true;
                var inner = other - hip;
                inner = inner - axis * (Vec3d.Dot(inner, axis) / len2);
                double im = inner.Magnitude;
                cos = mag > 0 && im > 0 ? Vec3d.Dot(radial, inner) / (mag * im) : 1;
            }
            return any ? outer * (1 - cos) * f.Scale * Clamp01((tt + .15) / .3) : 0;
        }

        /// <summary>Two octaves of value noise, about −1 to 1.</summary>
        public static double Noise(Vec3d p) =>
            .65 * ValueNoise(p * 1.3) + .35 * ValueNoise(p * 3.1 + new Vec3d(17.3, 5.1, 9.7));

        /// <summary>The noise's value at a lattice point: 32-bit wrapping integers.</summary>
        public static double Lattice(int x, int y, int z)
        {
            unchecked
            {
                int h = 374761393 * x + 668265263 * y + 1274126177 * z;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xffff) / 32767.5 - 1;
            }
        }

        /// <summary>Trilinear noise between lattice points, with smoothstep weights.</summary>
        public static double ValueNoise(Vec3d p)
        {
            int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y), z = (int)Math.Floor(p.Z);
            double fx = Smooth(p.X - x), fy = Smooth(p.Y - y), fz = Smooth(p.Z - z);
            double a = Mix(Lattice(x, y, z), Lattice(x + 1, y, z), fx), b = Mix(Lattice(x, y + 1, z), Lattice(x + 1, y + 1, z), fx);
            double c = Mix(Lattice(x, y, z + 1), Lattice(x + 1, y, z + 1), fx), d = Mix(Lattice(x, y + 1, z + 1), Lattice(x + 1, y + 1, z + 1), fx);
            return Mix(Mix(a, b, fy), Mix(c, d, fy), fz);
        }

        static double Smooth(double f) => f * f * (3 - 2 * f);
        static double Mix(double a, double b, double t) => a + (b - a) * t;

        // ---------- random numbers ----------

        /// <summary>The <paramref name="index"/>-th number of a seed, in [0, 1): a function of its arguments, with no state.</summary>
        public static double Rng(int seed, long index)
        {
            unchecked
            {
                ulong z = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + (ulong)index * 0xD1342543DE82EF95UL + 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                z ^= z >> 31;
                return (z >> 11) * (1.0 / 9007199254740992.0);
            }
        }

        // ---------- drips ----------

        /// <summary>How strongly a stain drips at <paramref name="t"/>: it rises over <c>max(.5, .3 spread)</c>, holds, and falls as <c>(1 − k)²</c> over the tail after the spread.</summary>
        public static double DripIntensity(double t, double spread, double tail)
        {
            if (t <= 0) return 0;
            double rise = Clamp01(t / Math.Max(.5, .3 * spread));
            if (t <= spread) return rise;
            if (tail <= 0) return 0;
            double k = Clamp01((t - spread) / tail), left = 1 - k;
            return rise * left * left;
        }

        /// <summary>The number of the fixed step <paramref name="t"/> lies in; a stored time keeps its step.</summary>
        public static long DropStepOf(double t) => t <= 0 ? 0 : (long)Math.Floor(t / DropStep + 1e-3);

        /// <summary>How many droplets leave in fixed step <paramref name="step"/> of a stain: the expected number, the fraction by the seed, so a step's count needs no earlier step.</summary>
        public static int DropsInStep(int seed, long step, double rate, double intensity)
        {
            double e = rate * intensity * DropStep;
            if (!(e > 0)) return 0;
            int n = (int)Math.Floor(e);
            return Rng(seed, step * 8) < e - n ? n + 1 : n;
        }

        // ---------- puddles ----------

        /// <summary>What a droplet of a size adds to a puddle's area.</summary>
        public static double AreaOf(double growth, double size)
        {
            double k = size / .07;
            return .04 * growth * k * k;
        }

        /// <summary>A puddle's radius from its area.</summary>
        public static double PuddleRadius(double area, double scale) => Math.Sqrt(Math.Max(area, 0) / Math.PI) * scale;

        /// <summary>A puddle's outline at angle <paramref name="a"/>, as a factor of its radius.</summary>
        public static double Outline(double a, double seed) => 1 + .16 * Math.Sin(3 * a + seed) + .09 * Math.Sin(7 * a + 1.7 * seed);

        /// <summary>
        /// The puddle a droplet landing at (x, z) goes to: the nearest whose edge is within <see cref="JoinMargin"/>, its area grown and its centre moved
        /// towards the droplet by half the droplet's share; else a new one (<see cref="WetPuddleStep.Index"/> −1).
        /// </summary>
        public static WetPuddleStep Join(double x, double z, double add, double scale, int count, Func<int, double> areaOf, Func<int, double> xOf, Func<int, double> zOf)
        {
            int best = -1;
            double bestGap = double.MaxValue;
            for (int i = 0; i < count; i++)
            {
                double dx = xOf(i) - x, dz = zOf(i) - z, d = Math.Sqrt(dx * dx + dz * dz);
                if (d > PuddleRadius(areaOf(i), scale) + JoinMargin || d >= bestGap) continue;
                bestGap = d;
                best = i;
            }
            if (best < 0) return new WetPuddleStep { Index = -1, Area = add, X = x, Z = z };
            double area = areaOf(best) + add, share = area > 0 ? add / area : 0;
            return new WetPuddleStep
            {
                Index = best,
                Area = area,
                X = xOf(best) + (x - xOf(best)) * share * .5,
                Z = zOf(best) + (z - zOf(best)) * share * .5,
            };
        }

        // ---------- what names a mesh ----------

        /// <summary>FNV-1a over the bytes of the UV0 floats, as 8 hex digits.</summary>
        public static string UvHash(float[] uv)
        {
            uint h = 2166136261;
            if (uv != null)
                unchecked
                {
                    for (int i = 0; i < uv.Length; i++)
                    {
                        uint b = (uint)BitConverter.SingleToInt32Bits(uv[i]);
                        h = (h ^ (b & 0xff)) * 16777619;
                        h = (h ^ ((b >> 8) & 0xff)) * 16777619;
                        h = (h ^ ((b >> 16) & 0xff)) * 16777619;
                        h = (h ^ (b >> 24)) * 16777619;
                    }
                }
            return h.ToString("x8", CultureInfo.InvariantCulture);
        }

        /// <summary>A mesh's signature in a map: name, vertex count and <see cref="UvHash"/>.</summary>
        public static string Signature(string meshName, int vertices, string uvHash) =>
            (meshName ?? "") + "|" + vertices.ToString(CultureInfo.InvariantCulture) + "|" + uvHash;
    }
}

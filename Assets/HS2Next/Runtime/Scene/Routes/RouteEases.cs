// The 32 route ease functions: the forms HS2's tween plays,
// stated as mathematics over t in [0, 1] (start 0, end 1). They are the iTween variants of Penner's equations,
// including their small departures: the exponential eases have no end guards (easeInExpo(0) = 2^-10,
// easeOutExpo(1) = 1 - 2^-10, easeInOutExpo(1) = 1 - 2^-11); the elastic eases use the period 0.3 also for
// in-out; spring is iTween's spring. The evaluator clamps the result to [0, 1] before the spline.
// Independent reference values: Tools/route_reference.py -> Tests/Routes/route_reference.json.
using System;
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes.Routes
{
    public static class RouteEases
    {
        /// <summary>The 32 names in enum order (= the save format's list; <c>linear</c> is index 21).</summary>
        public static readonly IReadOnlyList<string> Names = Enum.GetNames(typeof(RouteEase));

        /// <summary>HS2's six panel entries in panel order (R10): Linear, Ease in (quad), Ease out (quad), Ease in (quart),
        /// Ease out (quart), Bounce.</summary>
        public static readonly IReadOnlyList<RouteEase> UiSix = new[]
        {
            RouteEase.linear, RouteEase.easeInQuad, RouteEase.easeOutQuad, RouteEase.easeInQuart, RouteEase.easeOutQuart, RouteEase.easeOutBounce,
        };

        static readonly Dictionary<string, RouteEase> byName = BuildNames();

        static Dictionary<string, RouteEase> BuildNames()
        {
            var d = new Dictionary<string, RouteEase>(StringComparer.Ordinal);
            foreach (RouteEase e in Enum.GetValues(typeof(RouteEase))) d[e.ToString()] = e;
            return d;
        }

        /// <summary>Exact (case-sensitive) member name → ease; false for null or an unknown name.</summary>
        public static bool TryParse(string name, out RouteEase ease)
        {
            if (name != null && byName.TryGetValue(name, out ease)) return true;
            ease = RouteEase.linear;
            return false;
        }

        /// <summary>The ease of a stored name; an unknown name evaluates as linear (finding route.unknownEase).</summary>
        public static double Evaluate(string name, double t) => Evaluate(TryParse(name, out var e) ? e : RouteEase.linear, t);

        /// <summary>The ease function itself, not clamped (back / elastic / spring overshoot outside [0, 1]).</summary>
        public static double Evaluate(RouteEase e, double t)
        {
            switch (e)
            {
                case RouteEase.linear: return t;
                case RouteEase.spring:
                    {
                        double v = Clamp01(t);
                        return (Math.Sin(v * Math.PI * (0.2 + 2.5 * v * v * v)) * Math.Pow(1 - v, 2.2) + v) * (1 + 1.2 * (1 - v));
                    }
                case RouteEase.easeInQuad: return t * t;
                case RouteEase.easeOutQuad: return -t * (t - 2);
                case RouteEase.easeInOutQuad:
                    {
                        double v = t * 2;
                        if (v < 1) return 0.5 * v * v;
                        v -= 1;
                        return -0.5 * (v * (v - 2) - 1);
                    }
                case RouteEase.easeInCubic: return t * t * t;
                case RouteEase.easeOutCubic: { double v = t - 1; return v * v * v + 1; }
                case RouteEase.easeInOutCubic:
                    {
                        double v = t * 2;
                        if (v < 1) return 0.5 * v * v * v;
                        v -= 2;
                        return 0.5 * (v * v * v + 2);
                    }
                case RouteEase.easeInQuart: return t * t * t * t;
                case RouteEase.easeOutQuart: { double v = t - 1; return -(v * v * v * v - 1); }
                case RouteEase.easeInOutQuart:
                    {
                        double v = t * 2;
                        if (v < 1) return 0.5 * v * v * v * v;
                        v -= 2;
                        return -0.5 * (v * v * v * v - 2);
                    }
                case RouteEase.easeInQuint: return t * t * t * t * t;
                case RouteEase.easeOutQuint: { double v = t - 1; return v * v * v * v * v + 1; }
                case RouteEase.easeInOutQuint:
                    {
                        double v = t * 2;
                        if (v < 1) return 0.5 * v * v * v * v * v;
                        v -= 2;
                        return 0.5 * (v * v * v * v * v + 2);
                    }
                case RouteEase.easeInSine: return 1 - Math.Cos(t * Math.PI / 2);
                case RouteEase.easeOutSine: return Math.Sin(t * Math.PI / 2);
                case RouteEase.easeInOutSine: return -0.5 * (Math.Cos(Math.PI * t) - 1);
                case RouteEase.easeInExpo: return Math.Pow(2, 10 * (t - 1));
                case RouteEase.easeOutExpo: return 1 - Math.Pow(2, -10 * t);
                case RouteEase.easeInOutExpo:
                    {
                        double v = t * 2;
                        if (v < 1) return 0.5 * Math.Pow(2, 10 * (v - 1));
                        v -= 1;
                        return 0.5 * (2 - Math.Pow(2, -10 * v));
                    }
                case RouteEase.easeInCirc: return -(Math.Sqrt(1 - t * t) - 1);
                case RouteEase.easeOutCirc: { double v = t - 1; return Math.Sqrt(1 - v * v); }
                case RouteEase.easeInOutCirc:
                    {
                        double v = t * 2;
                        if (v < 1) return -0.5 * (Math.Sqrt(1 - v * v) - 1);
                        v -= 2;
                        return 0.5 * (Math.Sqrt(1 - v * v) + 1);
                    }
                case RouteEase.easeInBounce: return 1 - OutBounce(1 - t);
                case RouteEase.easeOutBounce: return OutBounce(t);
                case RouteEase.easeInOutBounce:
                    return t < 0.5 ? (1 - OutBounce(1 - t * 2)) * 0.5 : OutBounce(t * 2 - 1) * 0.5 + 0.5;
                case RouteEase.easeInBack: { const double s = 1.70158; return t * t * ((s + 1) * t - s); }
                case RouteEase.easeOutBack: { const double s = 1.70158; double v = t - 1; return v * v * ((s + 1) * v + s) + 1; }
                case RouteEase.easeInOutBack:
                    {
                        const double s = 1.70158 * 1.525;
                        double v = t * 2;
                        if (v < 1) return 0.5 * (v * v * ((s + 1) * v - s));
                        v -= 2;
                        return 0.5 * (v * v * ((s + 1) * v + s) + 2);
                    }
                case RouteEase.easeInElastic:
                    {
                        if (t == 0) return 0;
                        if (t == 1) return 1;
                        const double p = 0.3, s = p / 4;
                        double v = t - 1;
                        return -(Math.Pow(2, 10 * v) * Math.Sin((v - s) * (2 * Math.PI) / p));
                    }
                case RouteEase.easeOutElastic:
                    {
                        if (t == 0) return 0;
                        if (t == 1) return 1;
                        const double p = 0.3, s = p / 4;
                        return Math.Pow(2, -10 * t) * Math.Sin((t - s) * (2 * Math.PI) / p) + 1;
                    }
                case RouteEase.easeInOutElastic:
                    {
                        if (t == 0) return 0;
                        double v = t * 2;
                        if (v == 2) return 1;
                        const double p = 0.3, s = p / 4;
                        v -= 1;
                        if (v < 0) return -0.5 * (Math.Pow(2, 10 * v) * Math.Sin((v - s) * (2 * Math.PI) / p));
                        return Math.Pow(2, -10 * v) * Math.Sin((v - s) * (2 * Math.PI) / p) * 0.5 + 1;
                    }
                default: return t;
            }
        }

        /// <summary>Penner's four-piece bounce (7.5625 · (t − c)² + k at the breaks 1/2.75, 2/2.75, 2.5/2.75).</summary>
        static double OutBounce(double t)
        {
            const double k = 7.5625;
            if (t < 1 / 2.75) return k * t * t;
            if (t < 2 / 2.75) { t -= 1.5 / 2.75; return k * t * t + 0.75; }
            if (t < 2.5 / 2.75) { t -= 2.25 / 2.75; return k * t * t + 0.9375; }
            t -= 2.625 / 2.75;
            return k * t * t + 0.984375;
        }

        /// <summary>The ease clamped to [0, 1] (what the spline receives).</summary>
        public static double Clamped(RouteEase e, double t) => Clamp01(Evaluate(e, t));

        internal static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : double.IsNaN(v) ? 0 : v;
    }
}

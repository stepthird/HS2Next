// The five key eases of the Timeline. A key's ease shapes the segment from that key to the next: the blend weight w of
// the segment's fraction u in [0, 1). step holds the left value until the next key's time; linear is u; inout (the
// default) is u² (3 − 2u); in is u²; out is u (2 − u). An unknown stored name evaluates as inout.
using System;

namespace HS2Next.Scenes.Timeline
{
    public enum TimelineEase { Step = 0, Linear = 1, InOut = 2, In = 3, Out = 4 }

    public static class TimelineEaseCurves
    {
        /// <summary>The stored names in enum order.</summary>
        public static readonly string[] Names = { "step", "linear", "inout", "in", "out" };

        /// <summary>The ease a key gets when none is given.</summary>
        public const TimelineEase Default = TimelineEase.InOut;

        public static string Name(TimelineEase e) => (int)e >= 0 && (int)e < Names.Length ? Names[(int)e] : Names[(int)Default];

        /// <summary>Exact (case-sensitive) stored name → ease; false (and the default) for null or an unknown name.</summary>
        public static bool TryParse(string name, out TimelineEase ease)
        {
            for (int i = 0; i < Names.Length; i++)
            {
                if (string.Equals(Names[i], name, StringComparison.Ordinal))
                {
                    ease = (TimelineEase)i;
                    return true;
                }
            }
            ease = Default;
            return false;
        }

        /// <summary>The ease a stored name evaluates as (an unknown name: inout).</summary>
        public static TimelineEase Parse(string name) => TryParse(name, out var e) ? e : Default;

        /// <summary>The blend weight of the segment fraction <paramref name="u"/> (not clamped).</summary>
        public static double Weight(TimelineEase e, double u)
        {
            switch (e)
            {
                case TimelineEase.Step: return 0;
                case TimelineEase.Linear: return u;
                case TimelineEase.In: return u * u;
                case TimelineEase.Out: return u * (2 - u);
                default: return u * u * (3 - 2 * u);
            }
        }
    }
}

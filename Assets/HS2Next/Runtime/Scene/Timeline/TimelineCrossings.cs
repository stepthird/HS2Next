// Advancing the play head and collecting the timed commands it crosses. From p by dt to n = p + dt: without a wrap the
// events with p < t ≤ n (p ≤ t on the first advance after a play that starts at p); looping, each time n reaches the
// duration it wraps (one pass): (p, duration], then [0, duration] for every full pass, then [0, n] — so an event at the
// duration and one at 0 both fire at a wrap, in that order, and an event at 0 fires at every pass. At most 100 passes are
// taken with their events in one advance; the rest only move the time. Not looping, (p, duration] and the end.
using System;
using System.Collections.Generic;

namespace HS2Next.Scenes.Timeline
{
    public static class TimelineCrossings
    {
        /// <summary>Passes (wraps) taken with their events in one advance; further passes only move the time.</summary>
        public const int MaxPasses = 100;

        /// <summary>
        /// The time after advancing <paramref name="time"/> by <paramref name="dt"/> (≤ 0 or NaN: no advance, nothing
        /// collected). Crossed events are added to <paramref name="fired"/> as indices into the ascending
        /// <paramref name="eventTimes"/>, in crossing order. <paramref name="ended"/>: not looping and the duration reached
        /// (the time is then the duration). <paramref name="skippedPasses"/>: full passes left out by the cap.
        /// </summary>
        public static double Advance(double time, double dt, double duration, bool loop, bool inclusiveStart,
            double[] eventTimes, int eventCount, List<int> fired, out bool ended, out long skippedPasses)
        {
            ended = false;
            skippedPasses = 0;
            if (!(dt > 0) || !(duration > 0)) return time;
            double n = time + dt;
            if (!loop)
            {
                if (n >= duration)
                {
                    Collect(time, duration, inclusiveStart, eventTimes, eventCount, fired);
                    ended = true;
                    return duration;
                }
                Collect(time, n, inclusiveStart, eventTimes, eventCount, fired);
                return n;
            }
            if (n < duration)
            {
                Collect(time, n, inclusiveStart, eventTimes, eventCount, fired);
                return n;
            }
            Collect(time, duration, inclusiveStart, eventTimes, eventCount, fired);
            n -= duration;
            int passes = 1;
            while (n >= duration)
            {
                if (passes >= MaxPasses)
                {
                    skippedPasses = (long)Math.Floor(n / duration);
                    n %= duration;
                    break;
                }
                Collect(0, duration, true, eventTimes, eventCount, fired);
                n -= duration;
                passes++;
            }
            Collect(0, n, true, eventTimes, eventCount, fired);
            return n;
        }

        /// <summary>Adds the indices of the times in (from, to] ([from, to] when <paramref name="inclusive"/>), ascending.</summary>
        public static void Collect(double from, double to, bool inclusive, double[] eventTimes, int eventCount, List<int> fired)
        {
            if (fired == null || eventTimes == null || eventCount <= 0 || to < from) return;
            // The first index past the lower bound (binary search on the ascending times).
            int lo = 0, hi = eventCount;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                bool below = inclusive ? eventTimes[mid] < from : eventTimes[mid] <= from;
                if (below) lo = mid + 1;
                else hi = mid;
            }
            for (int i = lo; i < eventCount && eventTimes[i] <= to; i++) fired.Add(i);
        }
    }
}

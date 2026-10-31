// Deferred UI calls run by UiHost.Update a number of frames later (focus after a dialog opens, …). Used instead of
// VisualElement.schedule: in Editor batch play the panel's schedulers ran only a few times per thousands of harness ticks
// (package C's measurement), so scheduled work of the shell is driven by the host's frame instead.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.UI
{
    public static class UiDefer
    {
        static readonly List<(int frame, Action a)> queue = new List<(int, Action)>();
        static readonly List<(int frame, Action a)> due = new List<(int, Action)>();

        /// <summary>Runs <paramref name="a"/> at the host's Update <paramref name="frames"/> frames from now (≥ 1).</summary>
        public static void Frames(int frames, Action a)
        {
            if (a == null) return;
            queue.Add((Time.frameCount + Math.Max(1, frames), a));
        }

        internal static void Pump()
        {
            if (queue.Count == 0) return;
            int now = Time.frameCount;
            due.Clear();
            for (int i = queue.Count - 1; i >= 0; i--)
                if (queue[i].frame <= now) { due.Add(queue[i]); queue.RemoveAt(i); }
            for (int i = due.Count - 1; i >= 0; i--)
            {
                try { due[i].a(); }
                catch (Exception e) { Debug.LogError("[ui] deferred call threw: " + e); }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => queue.Clear();
    }
}

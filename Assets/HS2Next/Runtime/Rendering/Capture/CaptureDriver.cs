// The main-thread pump of CaptureService's asynchronous captures (Phase 3.6
// package C): workers post their completions here and Update runs them (the bridge reply, the toast). One hidden object
// per process, created on first use in play mode / the player, kept across scene loads.
using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace HS2Next.Screenshots
{
    [DefaultExecutionOrder(-4000)]
    public sealed class CaptureDriver : MonoBehaviour
    {
        static CaptureDriver instance;
        static readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();

        /// <summary>Creates the pump when it does not exist (main thread; play mode only).</summary>
        public static void Ensure()
        {
            if (instance || !Application.isPlaying) return;
            var go = new GameObject("HS2Next capture driver") { hideFlags = HideFlags.DontSave };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<CaptureDriver>();
        }

        /// <summary>Runs <paramref name="a"/> on the main thread in the next Update (any thread).</summary>
        public static void Post(Action a)
        {
            if (a != null) queue.Enqueue(a);
        }

        /// <summary>Runs the posted actions now (main thread; tests that pump by hand).</summary>
        public static int Drain()
        {
            int n = 0;
            while (queue.TryDequeue(out var a))
            {
                n++;
                try { a(); }
                catch (Exception e) { Debug.LogWarning("[capture] posted action threw: " + e); }
            }
            return n;
        }

        void Update() => Drain();
    }
}

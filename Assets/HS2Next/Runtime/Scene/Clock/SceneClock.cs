// The scene clock: one source of scene time for every scene time consumer
// (characters, items, routes, map scripts, particles, shader time). The global pause sets the frame delta to 0;
// Time.timeScale is never touched. Unity-free (plain Mono tests: SceneTests_Clock).
using System;

namespace HS2Next.Scenes
{
    /// <summary>
    /// Scene time. <see cref="BeginFrame"/> once per player frame (the runtime driver passes Unity's delta);
    /// consumers read <see cref="FrameDt"/> (0 while paused, except inside <see cref="Step"/>). Runtime state only:
    /// not saved, reset on new / load (R20).
    /// </summary>
    public sealed class SceneClock
    {
        bool paused;

        /// <summary>Global pause. Setting it raises <see cref="PausedChanged"/> when the value changes.</summary>
        public bool Paused
        {
            get => paused;
            set
            {
                if (paused == value) return;
                paused = value;
                PausedChanged?.Invoke(value);
            }
        }

        /// <summary>Scene seconds since the scene was created or loaded (sum of every frame and step delta).</summary>
        public double Time { get; private set; }

        /// <summary>Player frames begun since the last reset (steps do not count).</summary>
        public long Frame { get; private set; }

        /// <summary>Steps run since the last reset.</summary>
        public long Steps { get; private set; }

        /// <summary>The delta every scene consumer uses this frame (0 while paused; the step's dt inside a step).</summary>
        public float FrameDt { get; private set; }

        /// <summary>The delta the driver passed to the last <see cref="BeginFrame"/> (whether paused or not).</summary>
        public float RawDt { get; private set; }

        /// <summary>True while <see cref="Step"/> runs its callbacks.</summary>
        public bool InStep { get; private set; }

        /// <summary>Raised with the new value when <see cref="Paused"/> changes (also by <see cref="Reset"/>).</summary>
        public event Action<bool> PausedChanged;

        /// <summary>Raised after each single step with its dt (inside <see cref="Step"/>, after the callback).</summary>
        public event Action<float> Stepped;

        /// <summary>
        /// Starts a player frame: <c>FrameDt = Paused ? 0 : rawDt</c>, <c>Time += FrameDt</c>, <c>Frame++</c>.
        /// A negative or non-finite delta counts as 0.
        /// </summary>
        public void BeginFrame(float rawDt)
        {
            if (InStep) throw new InvalidOperationException("SceneClock.BeginFrame inside a step");
            if (!(rawDt >= 0f) || float.IsInfinity(rawDt)) rawDt = 0f;
            RawDt = rawDt;
            FrameDt = paused ? 0f : rawDt;
            Time += FrameDt;
            Frame++;
        }

        /// <summary>
        /// Runs <paramref name="n"/> steps of <paramref name="dt"/> while paused: for each, <c>FrameDt = dt</c>,
        /// <c>Time += dt</c>, <paramref name="stepOnce"/>(dt), <see cref="Stepped"/>; <see cref="FrameDt"/> is 0
        /// again afterwards (also when a callback throws). Refused while running (<see cref="InvalidOperationException"/>,
        /// "pause first"); n ≥ 0 and 0 &lt; dt (finite) or <see cref="ArgumentOutOfRangeException"/>. The bridge and the UI
        /// apply the narrower ranges (n ≤ 10,000, dt ≤ 1).
        /// </summary>
        public void Step(int n, float dt, Action<float> stepOnce)
        {
            if (!paused) throw new InvalidOperationException("pause first");
            if (InStep) throw new InvalidOperationException("SceneClock.Step is not re-entrant");
            if (n < 0) throw new ArgumentOutOfRangeException(nameof(n), "n must be >= 0");
            if (!(dt > 0f) || float.IsInfinity(dt)) throw new ArgumentOutOfRangeException(nameof(dt), "dt must be > 0 and finite");
            InStep = true;
            try
            {
                for (int i = 0; i < n; i++)
                {
                    FrameDt = dt;
                    Time += dt;
                    Steps++;
                    stepOnce?.Invoke(dt);
                    Stepped?.Invoke(dt);
                }
            }
            finally
            {
                FrameDt = 0f;
                InStep = false;
            }
        }

        /// <summary>New / load: running, time 0, frame and step counters 0 (raises <see cref="PausedChanged"/> when it was paused).</summary>
        public void Reset()
        {
            if (InStep) throw new InvalidOperationException("SceneClock.Reset inside a step");
            Time = 0;
            Frame = 0;
            Steps = 0;
            FrameDt = 0f;
            RawDt = 0f;
            Paused = false;
        }

        public override string ToString() =>
            (paused ? "paused" : "running") + " t=" + Time.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " frame=" + Frame + " steps=" + Steps;
    }
}

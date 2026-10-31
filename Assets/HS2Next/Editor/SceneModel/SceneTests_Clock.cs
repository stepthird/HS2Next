// The scene clock: BeginFrame, pause, Step (only while paused; n × dt;
// FrameDt back to 0, also after a throwing callback), events, time as a double over 10^7 frames, reset.
using System;
using System.Collections.Generic;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save.Tests;

    public static class SceneTests_Clock
    {
        public static void Run(TestRunner t)
        {
            Frames(t);
            Pause(t);
            Steps(t);
            LongRun(t);
            Reset(t);
        }

        static void Frames(TestRunner t)
        {
            var c = new SceneClock();
            t.Check(!c.Paused && c.Time == 0 && c.Frame == 0 && c.FrameDt == 0, "new clock: running, time 0");
            c.BeginFrame(0.02f);
            c.BeginFrame(0.03f);
            t.Check(c.Frame == 2 && c.FrameDt == 0.03f && c.RawDt == 0.03f && c.Time == (double)0.02f + (double)0.03f, "two frames: FrameDt = raw, Time = Σ");
            c.BeginFrame(-1f);
            t.Check(c.FrameDt == 0 && c.Frame == 3, "a negative delta counts as 0");
            c.BeginFrame(float.NaN);
            c.BeginFrame(float.PositiveInfinity);
            t.Check(c.FrameDt == 0 && c.Time == (double)0.02f + (double)0.03f, "NaN / ∞ deltas count as 0");
        }

        static void Pause(TestRunner t)
        {
            var c = new SceneClock();
            var seen = new List<bool>();
            c.PausedChanged += v => seen.Add(v);
            c.BeginFrame(0.5f);
            c.Paused = true;
            c.Paused = true;
            c.BeginFrame(0.25f);
            t.Check(c.FrameDt == 0 && c.RawDt == 0.25f && c.Time == 0.5 && c.Frame == 2, "paused: FrameDt 0, time held, frames counted, RawDt kept");
            c.Paused = false;
            c.BeginFrame(0.25f);
            t.Check(c.FrameDt == 0.25f && c.Time == 0.75, "resumed: time advances");
            t.Check(seen.Count == 2 && seen[0] && !seen[1], "PausedChanged once per change (true, false)");
        }

        static void Steps(TestRunner t)
        {
            var c = new SceneClock();
            t.Throws<InvalidOperationException>(() => c.Step(1, 1 / 60f, null), "step while running is refused");
            c.Paused = true;
            var dts = new List<float>();
            var inside = new List<float>();
            int stepped = 0;
            c.Stepped += dt => stepped++;
            c.Step(3, 0.1f, dt => { dts.Add(dt); inside.Add(c.FrameDt); t.Check(c.InStep, "InStep inside the callback"); });
            t.Check(dts.Count == 3 && dts.TrueForAll(d => d == 0.1f) && inside.TrueForAll(d => d == 0.1f), "3 steps of 0.1: callback and FrameDt see dt");
            t.Check(stepped == 3 && c.Steps == 3, "Stepped raised per step; step counter 3");
            t.Check(c.FrameDt == 0 && !c.InStep, "FrameDt back to 0 after the steps");
            t.Check(c.Time == 3 * (double)0.1f && c.Frame == 0, "time advanced by n × dt; frames not counted");
            c.BeginFrame(1f);
            t.Check(c.FrameDt == 0 && c.Time == 3 * (double)0.1f, "still paused after the steps");
            c.Step(0, 0.1f, _ => t.Check(false, "no callback for n = 0"));
            t.Throws<ArgumentOutOfRangeException>(() => c.Step(-1, 0.1f, null), "n < 0 refused");
            t.Throws<ArgumentOutOfRangeException>(() => c.Step(1, 0f, null), "dt 0 refused");
            t.Throws<ArgumentOutOfRangeException>(() => c.Step(1, float.NaN, null), "dt NaN refused");
            t.Throws<InvalidOperationException>(() => c.Step(2, 0.1f, _ => throw new InvalidOperationException("boom")), "a throwing callback propagates");
            t.Check(c.FrameDt == 0 && !c.InStep, "FrameDt 0 and InStep false after a throwing callback");
            t.Throws<InvalidOperationException>(() => c.Step(1, 0.1f, _ => c.Step(1, 0.1f, null)), "steps are not re-entrant");
            t.Throws<InvalidOperationException>(() => c.Step(1, 0.1f, _ => c.BeginFrame(0.1f)), "BeginFrame inside a step refused");
            // "pause → step N × dt" advances the clock exactly as N frames of dt.
            var a = new SceneClock();
            var b = new SceneClock();
            for (int i = 0; i < 120; i++) a.BeginFrame(1 / 60f);
            b.Paused = true;
            b.Step(120, 1 / 60f, null);
            t.Check(a.Time == b.Time, "120 frames of 1/60 = 120 steps of 1/60 (bit-equal time)");
        }

        static void LongRun(TestRunner t)
        {
            var c = new SceneClock();
            const int n = 10000000;
            float dt = 1 / 60f;
            for (int i = 0; i < n; i++) c.BeginFrame(dt);
            double expect = n * (double)dt;
            t.Check(Math.Abs(c.Time - expect) <= 1e-9 * expect, $"10^7 frames: time {c.Time:R} ≈ n × dt {expect:R} (double accumulation)");
            t.Check(c.Frame == n, "frame counter 10^7");
            float single = 0f;
            for (int i = 0; i < n; i++) single += dt;
            t.Info($"after 10^7 frames of 1/60: double {c.Time:R} s, float accumulation would give {single:R} s");
        }

        static void Reset(TestRunner t)
        {
            var c = new SceneClock();
            bool? last = null;
            c.PausedChanged += v => last = v;
            c.BeginFrame(1f);
            c.Paused = true;
            c.Step(2, 0.5f, null);
            c.Reset();
            t.Check(!c.Paused && c.Time == 0 && c.Frame == 0 && c.Steps == 0 && c.FrameDt == 0 && c.RawDt == 0, "reset: running, counters 0");
            t.Check(last == false, "reset raises PausedChanged(false) when it was paused");
            last = null;
            c.Reset();
            t.Check(last == null, "reset of a running clock raises nothing");
        }
    }
}

// The scene clock's bridge commands (group "routes / time / capture"; Phase 3.6 package K):
//   scene.pause  {paused?, shaderTime?}  reads or sets the global pause (SceneRuntime.SetPaused; Time.timeScale untouched),
//                optionally the shader-time scope (all / captures / off, SceneShaderTime.Scope; runtime, not saved);
//                reply {paused, time, frame, steps, shaderTime}
//   scene.step   {n = 1 (1–10,000), dt = 1/60 (0 < dt ≤ 1)}  n frames of dt while paused, synchronous
//                (SceneRuntime.Step); while running → unavailable ("pause first"); reply {time, frame, steps, ms}
// Neither records history nor changes the document (the pause is runtime state; new / load reset it, R20).
using System.Collections.Generic;
using System.Diagnostics;
using HS2Next.Api.Bridge;
using HS2Next.Scenes.Runtime;

namespace HS2Next.App.Commands
{
    public static class TimeCommands
    {
        /// <summary>The commands of this file (CoreCommands.Phase36Names, package K's part).</summary>
        public static readonly IReadOnlyList<string> Names = new[] { "scene.pause", "scene.step" };

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("scene.pause", "Reads or sets the global pause of the scene clock (every scene time consumer stops; the UI, camera, edits, bridge and captures keep working); not saved, reset by scene.new / scene.load.", "time")
                    .Arg("paused", ArgTypes.Bool, "true pauses, false resumes; absent = read")
                    .Arg("shaderTime", ArgTypes.String, "the shader-time scope: all (every scene camera takes the scene clock's time), captures (capture / thumbnail cameras only), off (URP's time)")
                    .Return("paused", ArgTypes.Bool).Return("time", ArgTypes.Float, "scene seconds (SceneClock.Time)")
                    .Return("frame", ArgTypes.Int, "SceneClock.Frame").Return("steps", ArgTypes.Int, "steps run since the clock was reset")
                    .Return("shaderTime", ArgTypes.String, "the shader-time scope")
                    .Example("{\"cmd\":\"scene.pause\",\"paused\":true}")
                    .WithFlags(CommandFlags.Mutates),
                Pause);
            CommandKit.Register(r, app, CommandKit.Spec("scene.step", "Runs n frames of dt while the scene is paused (characters, items, routes, map scripts, particles, map animators, shader time), synchronously.", "time")
                    .Arg("n", ArgTypes.Int, "frames (1–10,000)", false, 1)
                    .Arg("dt", ArgTypes.Float, "seconds per frame (0 < dt ≤ 1)", false, 1f / 60f)
                    .Return("time", ArgTypes.Float, "scene seconds after the steps").Return("frame", ArgTypes.Int, "SceneClock.Frame")
                    .Return("steps", ArgTypes.Int, "steps run since the clock was reset").Return("ms", ArgTypes.Float, "milliseconds the steps took")
                    .Example("{\"cmd\":\"scene.step\",\"n\":30,\"dt\":0.016666668}")
                    .WithFlags(CommandFlags.Mutates).WithTimeout(300),
                Step);
        }

        static BridgeReply State(SceneRuntime rt) => new BridgeReply
        {
            ["paused"] = rt.Paused,
            ["time"] = rt.Clock.Time,
            ["frame"] = rt.Clock.Frame,
            ["steps"] = rt.StepsTaken,
            ["shaderTime"] = HS2Next.SceneShaderTime.ScopeName(HS2Next.SceneShaderTime.Scope),
        };

        static object Pause(CoreContext c)
        {
            var rt = c.Runtime ?? throw new BridgeException(BridgeCodes.NotReady, "no scene runtime");
            bool? paused = c.Args.TryGet<bool>("paused", out var pv) ? pv : (bool?)null;
            HS2Next.SceneShaderTimeScope? scope = null;
            if (c.Args.TryGet<string>("shaderTime", out var sv))
            {
                if (!HS2Next.SceneShaderTime.TryParseScope(sv, out var s)) throw CommandKit.BadArgs("shaderTime", "all, captures or off");
                scope = s;
            }
            if (scope != null) HS2Next.SceneShaderTime.Scope = scope.Value;
            if (paused != null) rt.SetPaused(paused.Value);
            return State(rt);
        }

        static object Step(CoreContext c)
        {
            var rt = c.Runtime ?? throw new BridgeException(BridgeCodes.NotReady, "no scene runtime");
            int n = c.Args.Get("n", 1);
            if (n < 1 || n > SceneRuntime.MaxSteps) throw CommandKit.BadArgs("n", "1–" + SceneRuntime.MaxSteps);
            float dt = c.Args.Get("dt", 1f / 60f);
            if (!(dt > 0f && dt <= 1f)) throw CommandKit.BadArgs("dt", "0 < dt ≤ 1");
            if (!rt.Paused) throw new BridgeException(CommandKit.Unavailable, "the scene is running: pause first (scene.pause {paused: true})");
            var sw = Stopwatch.StartNew();
            if (!rt.Step(n, dt)) throw new BridgeException(CommandKit.Unavailable, "the step was refused (a step is already running)");
            sw.Stop();
            var reply = State(rt);
            reply.Remove("paused");
            reply.Remove("shaderTime");
            reply["ms"] = sw.Elapsed.TotalMilliseconds;
            return reply;
        }
    }
}

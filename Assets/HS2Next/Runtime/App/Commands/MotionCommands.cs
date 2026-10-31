// chara.motion: the runtime motion switches of a built character and hand stepping.
//   switches     paused, timeScale, autoStep (CharaMotion), physics (HS2's shape.physics: dynbone + bust off and the
//                kinematic bust stage), look (eyes + neck off), stages {name: bool} (any stage but the FK writer's and
//                the bridge's own bustKinematic)
//   correction   {master}: the `correct` stage switch (runtime); {flags: [8]}: the model's chara.correction (recorded,
//                "Joint correction")
//   reset        CharaMotion.ResetAll (stage histories, time and step count) and the node's earlier overrides dropped:
//                switches set by earlier calls and not by this one go back to their defaults, this call's stay recorded
//   step         {dt, n}: Runtime.ApplyNow, n × CharaMotion.Step(dt), Runtime.LateApply (deterministic tests)
// Order inside one call: switches, correction, reset, step. The switches are kept in MotionOverrides (re-applied after a
// rebuild of the node, dropped by scene.new / scene.load); they are not saved and not recorded. Every argument is
// checked before anything changes.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Api.Bridge;
using HS2Next.Motion;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;

namespace HS2Next.App.Commands
{
    public static class MotionCommands
    {
        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("chara.motion", "Runtime motion switches of a built character (pause, time scale, auto step, physics, look, stages), joint correction, reset and hand stepping; not saved.", "motion")
                    .Arg("key", ArgTypes.Node, "a built character", true)
                    .Arg("paused", ArgTypes.Bool, "CharaMotion.Paused (steps run with dt 0)")
                    .Arg("timeScale", ArgTypes.Float, "CharaMotion.TimeScale (≥ 0)")
                    .Arg("autoStep", ArgTypes.Bool, "step from LateUpdate every frame (false: only step moves the character)")
                    .Arg("physics", ArgTypes.Bool, "false: dynamic bones and bust physics off, the bust chains in their kinematic pose (HS2 shape.physics on:false); true: back on")
                    .Arg("look", ArgTypes.Bool, "false: the eye and neck look stages off; true: back on")
                    .Arg("stages", ArgTypes.Object, "{<stage name>: bool} for the stages of the character (anim, hands, neck, eyes, face, correct, bust, dynbone, ...); the FK writer's stages are switched with fk.enable")
                    .Arg("correction", ArgTypes.Object, "{master: bool} = the correct stage; {flags: [8 bools]} = the model's chara.correction (recorded)")
                    .Arg("step", ArgTypes.Object, "{dt: float (0 < dt ≤ 1, default 1/60), n: int (0–10000, default 1)}: step the character by hand now")
                    .Arg("reset", ArgTypes.Bool, "drop the stage histories (time and step count back to 0) and the node's earlier overrides", false, false)
                    .Return("paused", ArgTypes.Bool).Return("timeScale", ArgTypes.Float).Return("autoStep", ArgTypes.Bool)
                    .Return("time", ArgTypes.Float, "CharaMotion.Time (scaled seconds stepped)").Return("stepCount", ArgTypes.Int)
                    .Return("errors", ArgTypes.Int).Return("maxStep", ArgTypes.Float).Return("physics", ArgTypes.Bool).Return("look", ArgTypes.Bool)
                    .Return("stages", ArgTypes.ArrayOf(ArgTypes.Object), "[{name, phase, enabled, owner: fk | motion | bridge}] in step order")
                    .Return("correction", ArgTypes.Object, "{master, flags}").Return("frame", ArgTypes.Int, "the bridge frame of this reply")
                    .Example("{\"cmd\":\"chara.motion\",\"key\":\"<id>\",\"autoStep\":false,\"physics\":false,\"step\":{\"dt\":0.016666668,\"n\":60}}")
                    .WithFlags(CommandFlags.Mutates),
                CharaMotion);
        }

        static string Owner(string stage) =>
            Array.IndexOf(MotionOverrides.FkStages, stage) >= 0 ? "fk" : stage == BustKinematicStage.StageName ? "bridge" : "motion";

        static void OnlyMembers(ICommandArgs a, string arg, params string[] allowed)
        {
            foreach (var n in a.Names)
                if (Array.IndexOf(allowed, n) < 0)
                    throw new BridgeException(BridgeCodes.BadArgs, "argument '" + arg + "': unknown member '" + n + "'",
                        new BridgeErrorDetail { Arg = arg + "." + n, Expected = "one of " + string.Join(", ", allowed), Candidates = allowed });
        }

        static object CharaMotion(CoreContext c)
        {
            var r = CommandKit.Built(c);
            var m = r.Motion;
            if (!m) throw CommandKit.NotRealized("character " + r.NodeId + " has no motion stages", "key", r.State.ToString());
            string id = r.NodeId;

            // ---- arguments (all checked before anything changes)
            bool? paused = c.Args.TryGet<bool>("paused", out var pv) ? pv : (bool?)null;
            float? timeScale = CharaCommands.Float(c, "timeScale", 0f);
            bool? autoStep = c.Args.TryGet<bool>("autoStep", out var av) ? av : (bool?)null;
            bool? physics = c.Args.TryGet<bool>("physics", out var phv) ? phv : (bool?)null;
            bool? look = c.Args.TryGet<bool>("look", out var lv) ? lv : (bool?)null;
            var stages = new List<(string name, bool on)>();
            var sv = c.Args.Nested("stages");
            if (sv != null)
            {
                var have = m.Stages.Select(s => s.Name).Where(s => s != null).Distinct().ToList();
                foreach (var name in sv.Names)
                {
                    bool on = sv.Require<bool>(name);
                    if (Array.IndexOf(MotionOverrides.FkStages, name) >= 0)
                        throw new BridgeException(BridgeCodes.BadArgs, "stage " + name + " is owned by the FK writer: use fk.enable",
                            new BridgeErrorDetail { Arg = "stages." + name, Expected = "a stage not owned by the FK writer" });
                    if (name == BustKinematicStage.StageName)
                        throw new BridgeException(BridgeCodes.BadArgs, "stage " + name + " belongs to the physics switch: use physics",
                            new BridgeErrorDetail { Arg = "stages." + name, Expected = "a motion stage" });
                    if (!have.Contains(name))
                        throw new BridgeException(BridgeCodes.BadArgs, "the character has no stage \"" + name + "\"",
                            new BridgeErrorDetail { Arg = "stages." + name, Expected = "a stage name", Candidates = have.Where(s => Owner(s) == "motion").ToList() });
                    stages.Add((name, on));
                }
            }
            bool? master = null;
            bool[] flags = null;
            var cv = c.Args.Nested("correction");
            if (cv != null)
            {
                OnlyMembers(cv, "correction", "master", "flags");
                if (cv.TryGet<bool>("master", out var mv)) master = mv;
                if (cv.TryGet<bool[]>("flags", out var fv))
                {
                    if (fv == null || fv.Length != 8) throw CommandKit.BadArgs("correction.flags", "8 bools");
                    flags = fv;
                }
            }
            bool doStep = false;
            float dt = 1f / 60f;
            int steps = 1;
            var sp = c.Args.Nested("step");
            if (sp != null)
            {
                OnlyMembers(sp, "step", "dt", "n");
                doStep = true;
                dt = sp.Get("dt", 1f / 60f);
                if (!(dt > 0f && dt <= 1f)) throw CommandKit.BadArgs("step.dt", "0 < dt ≤ 1");
                steps = sp.Get("n", 1);
                if (steps < 0 || steps > 10000) throw CommandKit.BadArgs("step.n", "0–10000");
            }
            bool reset = c.Args.Get("reset", false);

            // ---- switches (runtime only)
            var ov = MotionOverrides.For(c.Runtime);
            var before = ov.Get(id)?.Clone();
            var rec = ov.GetOrCreate(id);
            var given = new MotionOverrides.Record();
            if (paused != null) { m.Paused = paused.Value; rec.Paused = given.Paused = paused; }
            if (timeScale != null) { m.TimeScale = timeScale.Value; rec.TimeScale = given.TimeScale = timeScale; }
            if (autoStep != null) { m.AutoStep = autoStep.Value; rec.AutoStep = given.AutoStep = autoStep; }
            if (physics != null)
            {
                MotionOverrides.SetPhysics(m, physics.Value);
                rec.Physics = given.Physics = physics;
                rec.Stages.Remove(MotionOverrides.PhysicsStageDynBone);
                rec.Stages.Remove(MotionOverrides.PhysicsStageBust);
            }
            if (look != null)
            {
                MotionOverrides.SetLook(m, look.Value);
                rec.Look = given.Look = look;
                rec.Stages.Remove(MotionOverrides.LookStageEyes);
                rec.Stages.Remove(MotionOverrides.LookStageNeck);
            }
            foreach (var (name, on) in stages)
            {
                MotionOverrides.SetStage(m, name, on);
                rec.Stages[name] = on;
                given.Stages[name] = on;
            }

            // ---- correction
            if (master != null)
            {
                MotionOverrides.SetStage(m, MotionOverrides.CorrectStage, master.Value);
                rec.Master = given.Master = master;
            }
            if (flags != null)
                CommandKit.Check(c.Session.Set(id, "chara.correction", flags, CommandKit.Exec(c, "Joint correction")), "correction.flags");

            // ---- reset: earlier overrides back to their defaults, this call's switches stay recorded
            if (reset)
            {
                MotionOverrides.RestoreDefaults(m, before, given, c.Runtime.Settings.ManualStepping);
                ov.Set(id, given);
                m.ResetAll();
            }
            else ov.Set(id, rec);

            // ---- step by hand
            if (doStep)
            {
                if (m.AutoStep && steps > 0) c.Warn("step with autoStep true: the character also steps in this frame's LateUpdate");
                c.Runtime.ApplyNow();
                for (int i = 0; i < steps; i++) m.Step(dt);
                c.Runtime.LateApply();
            }
            return Reply(c, r);
        }

        /// <summary>The chara.motion reply of a built character.</summary>
        public static BridgeReply Reply(CoreContext c, CharaRealization r)
        {
            var m = r.Motion;
            var list = new List<object>();
            bool correct = false;
            foreach (var s in m.Stages)
            {
                if (s == null) continue;
                if (s.Name == MotionOverrides.CorrectStage && s.Enabled) correct = true;
                list.Add(new BridgeReply { ["name"] = s.Name, ["phase"] = (int)s.Phase, ["enabled"] = s.Enabled, ["owner"] = Owner(s.Name) });
            }
            var flags = (r.Node?.State as CharaNodeState)?.Correction ?? new bool[8];
            var fl = new List<object>();
            foreach (var b in flags) fl.Add(b);
            return new BridgeReply
            {
                ["paused"] = m.Paused,
                ["timeScale"] = m.TimeScale,
                ["autoStep"] = m.AutoStep,
                ["time"] = m.Time,
                ["stepCount"] = m.StepCount,
                ["errors"] = m.Errors,
                ["maxStep"] = m.MaxStep,
                ["physics"] = MotionOverrides.PhysicsOn(m),
                ["look"] = MotionOverrides.LookOn(m),
                ["stages"] = list,
                ["correction"] = new BridgeReply { ["master"] = correct, ["flags"] = fl },
                ["frame"] = c.Frames.Frame,
            };
        }
    }

    public static partial class CoreCommands
    {
        static partial void RegisterMotion(ICommandRegistry r, AppCore app) => MotionCommands.Register(r, app);

        // the runtime-side part of chara.motion: overrides follow rebuilds and are dropped with the scene
        static partial void AttachRuntime(AppCore app)
        {
            if (app?.Runtime != null) MotionOverrides.Attach(app.Runtime);
        }
    }
}

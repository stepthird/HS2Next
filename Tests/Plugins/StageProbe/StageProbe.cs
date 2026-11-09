// The test plugin of the character stages (CC0-1.0). stageprobe.add puts a stage on a character at a phase; the stage counts its calls
// and can throw from one of them or take itself off from its own Step. stageprobe.state reports the counts, kept across a disable and
// an enable; stageprobe.remove disposes what stageprobe.add returned.
using System;
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using HS2Next.Api.Unity;
using HS2Next.Motion;
using UnityEngine;

namespace Stp3.Test.StageProbe
{
    public sealed class StageProbePlugin : IPlugin
    {
        /// <summary>The stages' calls in order, as "phase call"; static, so that a disable and an enable keep them.</summary>
        static readonly List<string> Calls = new List<string>();
        static int steps, resets, makes, unloads;
        static GameObject lastRoot;

        readonly List<IDisposable> added = new List<IDisposable>();
        IPluginContext ctx;
        HeadWatch watch;

        public void Load(IPluginContext context)
        {
            ctx = context;
            context.Commands.Register(new CommandSpec("stageprobe.state", "The stages' counts.").WithFlags(CommandFlags.ReadOnly), _ =>
            {
                return new Dictionary<string, object>
                {
                    ["unity"] = ctx.Unity() != null, ["stages"] = ctx.Unity()?.Stages != null, ["steps"] = steps, ["resets"] = resets, ["makes"] = makes,
                    ["unloads"] = unloads, ["added"] = added.Count, ["root"] = lastRoot ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(lastRoot) : 0, ["calls"] = Calls.ToArray(),
                };
            });
            context.Commands.Register(new CommandSpec("stageprobe.clear", "Forgets the counts."), _ =>
            {
                Calls.Clear();
                steps = resets = makes = 0;
                return new Dictionary<string, object>();
            });
            context.Commands.Register(new CommandSpec("stageprobe.add", "Adds a stage to a character.")
                .Arg("key", ArgTypes.String, "the character's node", required: true)
                .Arg("phase", ArgTypes.Int, "the stage's phase")
                .Arg("name", ArgTypes.String, "the stage's name")
                .Arg("throwIn", ArgTypes.String, "the call that throws: make, Step, Reset, Enabled, Phase")
                .Arg("leaveInStep", ArgTypes.Bool, "the stage takes itself off from its first Step"), c =>
            {
                int phase = c.Args.Get("phase", 420);
                string name = c.Args.Get("name", "probe"), throwIn = c.Args.Get("throwIn", (string)null);
                bool leave = c.Args.Get("leaveInStep", false);
                try
                {
                    IDisposable token = null;
                    token = ctx.Unity().Stages.Add(c.Args.Require<string>("key"), root =>
                    {
                        makes++;
                        lastRoot = root;
                        if (throwIn == "make") throw new InvalidOperationException("thrown by the stage probe");
                        return new ProbeStage(phase, name, throwIn, leave ? () => token?.Dispose() : (Action)null);
                    });
                    added.Add(token);
                    return new Dictionary<string, object> { ["added"] = true };
                }
                catch (ArgumentException e) { return new Dictionary<string, object> { ["added"] = false, ["why"] = e.GetType().Name }; }
            });
            context.Commands.Register(new CommandSpec("stageprobe.watch", "Adds the guide's example to a character.")
                .Arg("key", ArgTypes.String, "the character's node", required: true), c =>
            {
                string nodeId = c.Args.Require<string>("key");
                added.Add(ctx.Unity().Stages.Add(nodeId, root => watch = new HeadWatch(root)));
                return new Dictionary<string, object>();
            });
            context.Commands.Register(new CommandSpec("stageprobe.head", "How high the guide's example saw the head.").WithFlags(CommandFlags.ReadOnly), _ =>
            {
                return new Dictionary<string, object> { ["y"] = watch?.Position.y ?? 0f };
            });
            context.Commands.Register(new CommandSpec("stageprobe.remove", "Takes every stage off, twice over."), _ =>
            {
                foreach (var t in added) { t.Dispose(); t.Dispose(); }
                int n = added.Count;
                added.Clear();
                return new Dictionary<string, object> { ["removed"] = n };
            });
        }

        public void Unload() => unloads++;

        /// <summary>The example of the plugin guide, as it stands there.</summary>
        sealed class HeadWatch : IMotionStage
        {
            readonly Transform head;

            public HeadWatch(GameObject root) { head = Array.Find(root.GetComponentsInChildren<Transform>(), t => t.name == "cf_J_Head"); }

            public Vector3 Position { get; private set; }
            public MotionPhase Phase => MotionPhase.Final;
            public string Name => "head";
            public bool Enabled { get; set; } = true;
            public void Step(in MotionFrame frame) { if (head) Position = head.position; }
            public void Reset() { }
        }

        sealed class ProbeStage : IMotionStage
        {
            readonly int phase;
            readonly string throwIn;
            readonly Action leave;
            bool enabled = true;

            public ProbeStage(int phase, string name, string throwIn, Action leave)
            {
                this.phase = phase;
                Name = name;
                this.throwIn = throwIn;
                this.leave = leave;
            }

            public MotionPhase Phase
            {
                get
                {
                    if (throwIn == "Phase") throw new InvalidOperationException("thrown by the stage probe");
                    return (MotionPhase)phase;
                }
            }

            public string Name { get; }

            public bool Enabled
            {
                get
                {
                    if (throwIn == "Enabled") throw new InvalidOperationException("thrown by the stage probe");
                    return enabled;
                }
                set => enabled = value;
            }

            public void Step(in MotionFrame frame)
            {
                steps++;
                Calls.Add(phase + " Step");
                if (throwIn == "Step") throw new InvalidOperationException("thrown by the stage probe");
                leave?.Invoke();
            }

            public void Reset()
            {
                resets++;
                Calls.Add(phase + " Reset");
                if (throwIn == "Reset") throw new InvalidOperationException("thrown by the stage probe");
            }
        }
    }
}

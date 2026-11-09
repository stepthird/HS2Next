// The test plugin of the viewport tools and the map query (CC0-1.0). toolprobe.begin starts a tool that writes down every call
// it gets and can throw from one of them; toolprobe.state reports the calls, kept across a disable and an enable; and
// toolprobe.surface asks the map query.
using System;
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using HS2Next.Api.Unity;
using UnityEngine;

namespace Stp3.Test.ToolProbe
{
    public sealed class ToolProbePlugin : IPlugin
    {
        /// <summary>The calls, in order; static, so that a disable and an enable keep them.</summary>
        static readonly List<string> Calls = new List<string>();

        IPluginContext ctx;
        ProbeTool tool;

        public void Load(IPluginContext context)
        {
            ctx = context;
            context.Commands.Register(new CommandSpec("toolprobe.state", "The tool's calls and what the engine side offers.").WithFlags(CommandFlags.ReadOnly), _ =>
            {
                var u = ctx.Unity();
                return new Dictionary<string, object>
                {
                    ["unity"] = u != null, ["tools"] = u?.Tools != null, ["map"] = u?.Map != null, ["running"] = u?.Tools?.Running ?? false,
                    ["loaded"] = u?.Map?.Loaded ?? false, ["roots"] = u?.Map?.Roots?.Count ?? 0, ["calls"] = Calls.ToArray(),
                };
            });
            context.Commands.Register(new CommandSpec("toolprobe.clear", "Forgets the calls."), _ =>
            {
                Calls.Clear();
                return new Dictionary<string, object>();
            });
            context.Commands.Register(new CommandSpec("toolprobe.begin", "Starts the tool.")
                .Arg("passThrough", ArgTypes.Bool, "what the tool does not claim goes on")
                .Arg("claim", ArgTypes.Bool, "the tool claims presses, keys and the wheel")
                .Arg("pick", ArgTypes.Bool, "a press reads the pick")
                .Arg("throwIn", ArgTypes.String, "the call that throws, by name"), c =>
            {
                tool = new ProbeTool { Claim = c.Args.Get("claim", true), Pick = c.Args.Get("pick", false), ThrowIn = c.Args.Get("throwIn", (string)null) };
                bool began = ctx.Unity().Tools.Begin(tool, new ToolOptions { PassThrough = c.Args.Get("passThrough", false), Hint = "plugin.stp3.test.toolprobe.hint" });
                return new Dictionary<string, object> { ["began"] = began };
            });
            context.Commands.Register(new CommandSpec("toolprobe.pick", "Starts the guide's example: the next press on a character is picked."), _ =>
            {
                var tools = ctx.Unity().Tools;
                bool began = tools.Begin(new PickPoint(tools, hit => Calls.Add("Picked " + hit.Part)), new ToolOptions { Filter = new ToolPickFilter { CharactersOnly = true } });
                return new Dictionary<string, object> { ["began"] = began };
            });
            context.Commands.Register(new CommandSpec("toolprobe.end", "Ends the tool."), _ =>
            {
                ctx.Unity().Tools.End(tool);
                return new Dictionary<string, object>();
            });
            context.Commands.Register(new CommandSpec("toolprobe.selftest", "Runs the tool's calls here with no host: the types load and work in this process."), _ =>
            {
                int picks = 0;
                ViewportTool t = new ProbeTool { Claim = true, Pick = true };
                var p = new ToolPointer(new Vector2(1, 2), new Ray(Vector3.zero, Vector3.forward), 0, 1, ToolMods.Ctrl, () => { picks++; return new ToolPick { Hit = true }; });
                int first = Calls.Count;
                t.Started();
                bool down = t.PointerDown(in p);
                t.PointerUp(in p);
                t.Ended(ToolEnd.Finished);
                var made = Calls.GetRange(first, Calls.Count - first);
                Calls.RemoveRange(first, made.Count);
                return new Dictionary<string, object> { ["down"] = down, ["picks"] = picks, ["calls"] = made.ToArray() };
            });
            context.Commands.Register(new CommandSpec("toolprobe.surface", "The map's surface under a point.").WithFlags(CommandFlags.ReadOnly)
                .Arg("x", ArgTypes.Float, "x", required: true).Arg("y", ArgTypes.Float, "y", required: true).Arg("z", ArgTypes.Float, "z", required: true), c =>
            {
                var p = new Vector3(c.Args.Require<float>("x"), c.Args.Require<float>("y"), c.Args.Require<float>("z"));
                bool hit = ctx.Unity().Map.Surface(p, out var s);
                var hits = new MapSurfaceHit[2];
                int n = ctx.Unity().Map.Surfaces(new[] { p, p + Vector3.up }, hits);
                return new Dictionary<string, object> { ["hit"] = hit, ["height"] = s.Height, ["normalY"] = s.Normal.y, ["source"] = s.Source, ["answers"] = n };
            });
        }

        public void Unload() => Calls.Add("Unload");

        /// <summary>The example of the plugin guide, as it stands there.</summary>
        sealed class PickPoint : ViewportTool
        {
            readonly IViewportTools tools;
            readonly Action<ToolPick> done;

            public PickPoint(IViewportTools tools, Action<ToolPick> done) { this.tools = tools; this.done = done; }

            public override bool PointerDown(in ToolPointer p)
            {
                if (p.Button != 0 || !p.Hit.Hit) return false;
                done(p.Hit);
                tools.End(this);
                return true;
            }
        }

        sealed class ProbeTool : ViewportTool
        {
            public bool Claim, Pick;
            public string ThrowIn;

            void Note(string name, string detail = null)
            {
                Calls.Add(detail == null ? name : name + " " + detail);
                if (name == ThrowIn) throw new InvalidOperationException("thrown by the tool probe");
            }

            public override void Started() => Note("Started");
            public override bool PointerDown(in ToolPointer p)
            {
                Note("PointerDown", p.Button + " " + p.Mods + (Pick ? " hit " + p.Hit.Hit : ""));
                return Claim;
            }
            public override void PointerUp(in ToolPointer p) => Note("PointerUp", p.Button.ToString());
            public override void PointerCancel() => Note("PointerCancel");
            public override bool Wheel(float notches, in ToolPointer p) { Note("Wheel", notches > 0 ? "up" : "down"); return Claim; }
            public override bool Key(string key, ToolMods mods) { Note("Key", key); return Claim; }
            public override void Ended(ToolEnd reason) => Note("Ended", reason.ToString());
        }
    }
}

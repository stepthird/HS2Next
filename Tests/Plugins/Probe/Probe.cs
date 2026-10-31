// The loader probe (CC0-1.0): a test plugin that exercises what a plugin can do in each kind of run - commands under
// its prefix, its own MonoBehaviour added from code, a private dependency from its lib folder, the host's JSON library,
// the typed events, the per-frame update, its settings file and in-process calls. Every reply is ids and numbers.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Stp3.Test.ProbeDep;

namespace Stp3.Test.Probe
{
    public sealed class ProbePlugin : IPlugin, IPluginUpdate, IPluginSceneLoaded
    {
        /// <summary>The loaded instance (null before Load and after Unload).</summary>
        public static ProbePlugin Instance { get; private set; }

        internal IPluginContext Ctx;
        internal int Updates, Loads, Unloads;
        internal float LastSceneDt;
        internal readonly List<string> SceneLoads = new List<string>();
        internal readonly Dictionary<string, int> Events = new Dictionary<string, int>(StringComparer.Ordinal);
        GameObject spawned;
        int spawnFrame = -1;
        string armed, armedReply;
        System.Threading.Tasks.Task<string> pendingAsync;

        /// <summary>Adds a line to the lifecycle log the loader tests read (one list per process, shared by the test plugins).</summary>
        internal static void TestLog(string line)
        {
            var l = AppDomain.CurrentDomain.GetData("stp3.test.lifecycle") as List<string>;
            if (l == null) AppDomain.CurrentDomain.SetData("stp3.test.lifecycle", l = new List<string>());
            lock (l) l.Add(line);
        }

        void Count(string name)
        {
            Events.TryGetValue(name, out int n);
            Events[name] = n + 1;
        }

        public void Load(IPluginContext context)
        {
            Ctx = context;
            Loads++;
            Instance = this;
            TestLog("load " + context.Guid);
            var c = context.Commands;
            c.Register(new CommandSpec("probe.ping", "Answers with the probe's counters and its dependency's text.")
                .Arg("n", ArgTypes.Int, "a number to double", defaultValue: 1).WithFlags(CommandFlags.ReadOnly), Ping);
            c.Register(new CommandSpec("probe.spawn", "Adds the probe's MonoBehaviour to a new GameObject.").WithFlags(CommandFlags.Mutates), Spawn);
            c.Register(new CommandSpec("probe.frames", "The probe behaviour's Update count.").WithFlags(CommandFlags.ReadOnly), Frames);
            c.Register(new CommandSpec("probe.despawn", "Destroys the probe's GameObject.").WithFlags(CommandFlags.Mutates), Despawn);
            c.Register(new CommandSpec("probe.json", "Parses JSON text with the host's JSON library.").Arg("text", ArgTypes.String, "JSON text", required: true)
                .WithFlags(CommandFlags.ReadOnly), Json);
            c.Register(new CommandSpec("probe.assemblies", "The probe's assemblies as the runtime sees them.").WithFlags(CommandFlags.ReadOnly), Assemblies);
            c.Register(new CommandSpec("probe.events", "Counts of the events the probe received.").WithFlags(CommandFlags.ReadOnly), _ => EventsReply());
            c.Register(new CommandSpec("probe.settings", "Writes a value to the probe's settings file and reads it back.")
                .Arg("value", ArgTypes.Int, "the value", required: true).WithFlags(CommandFlags.WritesFiles), Settings);
            c.Register(new CommandSpec("probe.call", "Runs a bridge request in-process (inline) and returns its reply.")
                .Arg("request", ArgTypes.Object, "the request object", required: true), CallInline);
            c.Register(new CommandSpec("probe.throw", "Throws (a fault of the plugin).").WithFlags(CommandFlags.ReadOnly), _ => throw new InvalidOperationException("probe.throw"));
            c.Register(new CommandSpec("probe.arm", "The next NodesAdded handler calls this request inline and keeps the reply's code.")
                .Arg("request", ArgTypes.Object, "the request object", required: true), cc => { armed = cc.Args.RawJson("request"); armedReply = null; return new Dictionary<string, object> { ["armed"] = true }; });
            c.Register(new CommandSpec("probe.callAsync", "Queues a bridge request (CallAsync); probe.asyncResult reports its reply.")
                .Arg("request", ArgTypes.Object, "the request object", required: true), cc => { pendingAsync = Ctx.CallAsync(cc.Args.RawJson("request")); return new Dictionary<string, object> { ["started"] = true }; });
            c.Register(new CommandSpec("probe.asyncResult", "The reply of the last probe.callAsync once it is there.").WithFlags(CommandFlags.ReadOnly),
                _ => new Dictionary<string, object> { ["done"] = pendingAsync != null && pendingAsync.IsCompleted, ["reply"] = pendingAsync != null && pendingAsync.IsCompleted ? new RawJson(pendingAsync.Result) : null });
            var e = context.Events;
            e.AppReady += () => Count("AppReady");
            e.SceneCleared += () => Count("SceneCleared");
            e.SceneLoaded += i => { Count("SceneLoaded." + i.Kind); TestLog("event SceneLoaded " + context.Guid + " " + i.Kind + " " + (i.Path ?? "-") + " nodes " + i.Nodes); };
            e.SceneSaving += () => Count("SceneSaving");
            e.SceneSaved += r => Count("SceneSaved." + (r.Ok ? "ok" : "failed"));
            e.NodesAdded += i =>
            {
                Count("NodesAdded." + i.Origin);
                if (armed == null) return;
                string req = armed;
                armed = null;
                var reply = JObject.Parse(Ctx.Call(req));
                armedReply = (bool?)reply["ok"] == true ? "ok" : (string)reply["code"];
            };
            e.NodesRemoved += i => Count("NodesRemoved." + i.Origin);
            e.NodesMoved += i => Count("NodesMoved");
            e.NodeForgotten += id => Count("NodeForgotten");
            e.CharaBuilt += i => Count("CharaBuilt");
            e.PausedChanged += p => Count("PausedChanged." + p);
            e.SceneStepped += s => Count("SceneStepped");
            e.Quitting += () => Count("Quitting");
            context.Log.Info("probe loaded (Api " + context.Host.ApiVersion + ", app " + context.Host.AppVersion + ", " + Helper.Hello() + ")");
        }

        public void Unload()
        {
            Unloads++;
            TestLog("unload " + Ctx?.Guid);
            if (spawned) UnityEngine.Object.Destroy(spawned);
            Ctx?.Log.Info("probe unloaded after " + Updates + " updates");
            if (Instance == this) Instance = null;
        }

        public void Update(in PluginFrame frame)
        {
            Updates++;
            LastSceneDt = frame.SceneDt;
        }

        public void SceneLoaded(SceneLoadInfo info)
        {
            SceneLoads.Add(info.Kind.ToString());
            TestLog("sceneLoaded " + Ctx.Guid + " " + info.Kind);
        }

        object Ping(ICommandContext c)
        {
            int n = c.Args.Get("n", 1);
            return new Dictionary<string, object>
            {
                ["n"] = n,
                ["twice"] = Helper.Twice(n),
                ["dep"] = Helper.Hello(),
                ["api"] = Ctx.Host.ApiVersion,
                ["updates"] = Updates,
                ["sceneLoads"] = SceneLoads.ToArray(),
                ["isPlayer"] = Ctx.Host.IsPlayer,
                ["isEditor"] = Ctx.Host.IsEditor,
            };
        }

        object Spawn(ICommandContext c)
        {
            if (!spawned)
            {
                spawned = new GameObject("Stp3 probe");
                spawned.AddComponent(typeof(ProbeBehaviour));
                spawnFrame = Time.frameCount;
            }
            return new Dictionary<string, object> { ["spawnFrame"] = spawnFrame, ["frame"] = Time.frameCount };
        }

        object Frames(ICommandContext c)
        {
            var b = spawned ? spawned.GetComponent<ProbeBehaviour>() : null;
            return new Dictionary<string, object>
            {
                ["alive"] = b != null,
                ["behaviourFrames"] = b != null ? b.Frames : 0,
                ["spawnFrame"] = spawnFrame,
                ["frame"] = Time.frameCount,
                ["updates"] = Updates,
                ["lastSceneDt"] = LastSceneDt,
            };
        }

        object Despawn(ICommandContext c)
        {
            bool had = spawned;
            if (spawned) UnityEngine.Object.Destroy(spawned);
            spawned = null;
            return new Dictionary<string, object> { ["destroyed"] = had };
        }

        object Json(ICommandContext c)
        {
            var o = JObject.Parse(c.Args.Require<string>("text"));
            var asm = typeof(JObject).Assembly;
            return new Dictionary<string, object>
            {
                ["members"] = o.Count,
                ["library"] = asm.GetName().Name,
                ["version"] = asm.GetName().Version.ToString(),
                ["copies"] = AppDomain.CurrentDomain.GetAssemblies().Count(a => a.GetName().Name == "Newtonsoft.Json"),
            };
        }

        object Assemblies(ICommandContext c)
        {
            var mine = new List<object>();
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                string n = a.GetName().Name;
                if (!n.StartsWith("Stp3.Test", StringComparison.Ordinal)) continue;
                string loc;
                try { loc = a.Location; }
                catch (Exception e) { loc = "(" + e.GetType().Name + ")"; }
                mine.Add(new Dictionary<string, object> { ["name"] = n, ["location"] = loc ?? "" });
            }
            bool listed = false;
            int unityCount = 0;
            try
            {
                foreach (var a in UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies())
                {
                    unityCount++;
                    if (a != null && a.GetName().Name == "Stp3.Test.Probe") listed = true;
                }
            }
            catch (Exception e) { return new Dictionary<string, object> { ["error"] = "GetLoadedAssemblies threw " + e.GetType().Name + ": " + e.Message }; }
            return new Dictionary<string, object>
            {
                ["mine"] = mine,
                ["apiCopies"] = AppDomain.CurrentDomain.GetAssemblies().Count(a => a.GetName().Name == "HS2Next.Api"),
                ["probeCopies"] = AppDomain.CurrentDomain.GetAssemblies().Count(a => a.GetName().Name == "Stp3.Test.Probe"),
                ["listedByUnity"] = listed,
                ["unityLoaded"] = unityCount,
            };
        }

        Dictionary<string, object> EventsReply()
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var kv in Events.OrderBy(k => k.Key, StringComparer.Ordinal)) d[kv.Key] = kv.Value;
            return new Dictionary<string, object> { ["events"] = d, ["sceneLoads"] = SceneLoads.ToArray(), ["updates"] = Updates, ["armedReply"] = armedReply };
        }

        object Settings(ICommandContext c)
        {
            int v = c.Args.Require<int>("value");
            Ctx.Settings.Write("{\"value\":" + v + "}");
            var back = JObject.Parse(Ctx.Settings.Read());
            return new Dictionary<string, object> { ["value"] = (int)back["value"], ["path"] = Ctx.Settings.Path };
        }

        object CallInline(ICommandContext c)
        {
            var sw = Stopwatch.StartNew();
            string reply = Ctx.Call(c.Args.RawJson("request"));
            sw.Stop();
            return new Dictionary<string, object> { ["reply"] = new RawJson(reply), ["us"] = sw.Elapsed.TotalMilliseconds * 1000.0 };
        }
    }

    /// <summary>The probe's own component: counts its Update calls.</summary>
    public sealed class ProbeBehaviour : MonoBehaviour
    {
        public int Frames;
        void Update() { Frames++; }
    }

    /// <summary>The probe's check method for the Editor host entry (a static method run after the plugin set loaded).</summary>
    public static class ProbeTests
    {
        /// <summary>Returns 0 when the probe instance is loaded with its commands registered; prints one report line.</summary>
        public static int Run(string[] args)
        {
            var p = ProbePlugin.Instance;
            bool ok = p != null && p.Loads == 1 && p.Ctx != null && p.Ctx.Commands.Contains("probe.ping");
            string line = "PROBE TESTS: " + (ok ? "PASS" : "FAIL") + " (instance " + (p != null) + ", loads " + (p?.Loads ?? 0) + ", probe.ping " +
                          (p?.Ctx?.Commands.Contains("probe.ping") ?? false) + ", dep " + Helper.Hello() + ", args " + string.Join(" ", args ?? new string[0]) + ")";
            UnityEngine.Debug.Log(line);
            int i = Array.IndexOf(args ?? new string[0], "-report");
            if (i >= 0 && i + 1 < args.Length) System.IO.File.AppendAllText(args[i + 1], line + "\n");
            return ok ? 0 : 1;
        }
    }
}

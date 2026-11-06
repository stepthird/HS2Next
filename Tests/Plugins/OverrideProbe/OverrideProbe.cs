// The override probe (CC0-1.0): a test plugin of the runtime overrides (IPluginContext.Overrides) and of the scene post
// stage (IPluginScenePost). Engine-free (the Api assembly only). The driver acquires named slots (a node and a path, a
// priority), sets them once or in every Update, clears and releases them, reads them back and measures the setter. Every
// reply is ids, paths and numbers.
//   overrideprobe.acquire  {name, key?, path, priority, auto?} -> {name, acquired, nodeId, path, kind, priority} or {refused};
//                          auto: a new instance (a runtime enable) acquires it again in its Load with the last numbers set
//   overrideprobe.set      {name, values?: [..], json?, perFrame?}  -> {name, result}
//   overrideprobe.clear    {name}                             -> {name, result}
//   overrideprobe.release  {name? | all, forget?}             -> {released} (forget: no auto slot any more)
//   overrideprobe.state    {}                                 -> {available, updates, slots: {name: {...}}, list: [...]}
//   overrideprobe.bench    {name, n}                          -> {n, medianUs, p95Us, collections, allocatedBytes}
//   overrideprobe.post     {mark?, throws?}                   -> {posts, marked, frame, sceneDt, sceneTime, paused}: ScenePost's calls, those
//                          with SceneDt = mark and the last of them; throws: ScenePost throws from now on
using System;
using System.Collections.Generic;
using System.Diagnostics;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;

namespace Stp3.Test.OverrideProbe
{
    public sealed class OverrideProbePlugin : IPlugin, IPluginUpdate, IPluginScenePost
    {
        sealed class Named
        {
            public IOverrideSlot Slot;
            public double[] Values;
            public bool PerFrame;
        }

        sealed class AutoSpec
        {
            public string Name, Key, Path;
            public int Priority;
            public double[] Values;
            public bool PerFrame;
        }

        /// <summary>Slots a new instance acquires again in its Load (kept across instances in the process).</summary>
        static readonly List<AutoSpec> auto = new List<AutoSpec>();
        /// <summary>Slots acquired again by the last Load.</summary>
        static int reacquired;

        IPluginContext ctx;
        readonly Dictionary<string, Named> slots = new Dictionary<string, Named>(StringComparer.Ordinal);
        int updates, frameSets, posts, marked;
        bool postThrows;
        float mark;
        PluginFrame lastMarked;

        public void Load(IPluginContext context)
        {
            ctx = context;
            var c = ctx.Commands;
            c.Register(new CommandSpec("overrideprobe.acquire", "Acquires a named runtime override slot.")
                .Arg("name", ArgTypes.String, "the slot's name in the probe", required: true)
                .Arg("key", ArgTypes.Node, "the node (absent: a scene-wide path)")
                .Arg("path", ArgTypes.String, "the override path", required: true)
                .Arg("priority", ArgTypes.Int, "the priority", defaultValue: 0)
                .Arg("auto", ArgTypes.Bool, "a new instance acquires it again in its Load", defaultValue: false), Acquire);
            c.Register(new CommandSpec("overrideprobe.set", "Sets a named slot from numbers or JSON, once or in every Update.")
                .Arg("name", ArgTypes.String, "the slot's name", required: true)
                .Arg("values", ArgTypes.ArrayOf(ArgTypes.Float), "the numbers (Set(double[]))")
                .Arg("json", ArgTypes.String, "the JSON form (SetJson)")
                .Arg("perFrame", ArgTypes.Bool, "set the numbers again in every Update", defaultValue: false), Set);
            c.Register(new CommandSpec("overrideprobe.clear", "Clears a named slot's value.")
                .Arg("name", ArgTypes.String, "the slot's name", required: true), Clear);
            c.Register(new CommandSpec("overrideprobe.release", "Releases a named slot, or every slot of the probe.")
                .Arg("name", ArgTypes.String, "the slot's name")
                .Arg("all", ArgTypes.Bool, "ReleaseAll", defaultValue: false)
                .Arg("forget", ArgTypes.Bool, "forget the auto slots too", defaultValue: false), Release);
            c.Register(new CommandSpec("overrideprobe.state", "The probe's slots and the listing of every owner's slots.").WithFlags(CommandFlags.ReadOnly), State);
            c.Register(new CommandSpec("overrideprobe.bench", "Times Set(x, y, z) of a named slot (median and p95 in microseconds, collections and bytes allocated).")
                .Arg("name", ArgTypes.String, "the slot's name", required: true)
                .Arg("n", ArgTypes.Int, "calls", defaultValue: 10000), Bench);
            c.Register(new CommandSpec("overrideprobe.post", "The probe's ScenePost calls, those with a marked scene delta and the last of them; throws makes ScenePost throw from now on.")
                .Arg("mark", ArgTypes.Float, "the scene delta that counts as marked (absent: keep)")
                .Arg("throws", ArgTypes.Bool, "ScenePost throws from now on", defaultValue: false), Post);
            reacquired = 0;
            foreach (var a in auto)
            {
                IOverrideSlot s;
                try { s = ctx.Overrides.Acquire(a.Key, a.Path, a.Priority); }
                catch (ArgumentException) { continue; }
                if (s == null) continue;
                slots[a.Name] = new Named { Slot = s, Values = a.Values, PerFrame = a.PerFrame };
                if (a.Values != null) s.Set(a.Values);
                reacquired++;
            }
        }

        public void Unload() => slots.Clear();

        public void Update(in PluginFrame frame)
        {
            updates++;
            foreach (var n in slots.Values)
                if (n.PerFrame && n.Values != null && !n.Slot.IsReleased) { n.Slot.Set(n.Values); frameSets++; }
        }

        public void ScenePost(in PluginFrame frame)
        {
            posts++;
            if (frame.SceneDt == mark)
            {
                marked++;
                lastMarked = frame;
            }
            if (postThrows) throw new InvalidOperationException("the probe's post fault");
        }

        object Post(ICommandContext c)
        {
            postThrows = c.Args.Get("throws", false);
            if (c.Args.Has("mark")) mark = c.Args.Get("mark", 0f);
            return new BridgeReply().With("posts", posts).With("marked", marked).With("frame", lastMarked.Frame).With("sceneDt", lastMarked.SceneDt)
                .With("sceneTime", lastMarked.SceneTime).With("paused", lastMarked.Paused);
        }

        Named Get(ICommandContext c)
        {
            string name = c.Args.Get<string>("name");
            if (name == null || !slots.TryGetValue(name, out var n)) throw new BridgeException(BridgeError.NotFound("no slot named " + name, "name"));
            return n;
        }

        object Acquire(ICommandContext c)
        {
            string name = c.Args.Get<string>("name");
            string key = c.Args.Get<string>("key");
            string path = c.Args.Get<string>("path");
            int priority = c.Args.Get("priority", 0);
            IOverrideSlot s;
            try { s = ctx.Overrides.Acquire(key, path, priority); }
            catch (ArgumentException e) { return new BridgeReply().With("name", name).With("acquired", false).With("refused", e.Message); }
            if (s == null) return new BridgeReply().With("name", name).With("acquired", false).With("refused", null);
            // the same slot again (the plugin's slot of the channel, its priority changed) keeps its numbers
            if (slots.TryGetValue(name, out var had) && had.Slot == s) { }
            else slots[name] = new Named { Slot = s };
            if (c.Args.Get("auto", false))
            {
                auto.RemoveAll(x => x.Name == name);
                auto.Add(new AutoSpec { Name = name, Key = key, Path = path, Priority = priority, Values = slots[name].Values, PerFrame = slots[name].PerFrame });
            }
            return new BridgeReply().With("name", name).With("acquired", true).With("nodeId", s.NodeId).With("path", s.Path).With("kind", s.Kind.ToString()).With("priority", s.Priority);
        }

        object Set(ICommandContext c)
        {
            var n = Get(c);
            bool result;
            try
            {
                if (c.Args.Has("json")) result = n.Slot.SetJson(c.Args.Get<string>("json"));
                else
                {
                    var v = c.Args.Get<double[]>("values");
                    result = n.Slot.Set(v);
                    n.Values = v;
                    n.PerFrame = c.Args.Get("perFrame", false);
                    string nm = c.Args.Get<string>("name");
                    foreach (var a in auto) if (a.Name == nm) { a.Values = v; a.PerFrame = n.PerFrame; }
                }
            }
            catch (ArgumentException e) { return new BridgeReply().With("name", c.Args.Get<string>("name")).With("refused", e.Message); }
            return new BridgeReply().With("name", c.Args.Get<string>("name")).With("result", result);
        }

        object Clear(ICommandContext c)
        {
            var n = Get(c);
            n.PerFrame = false;
            return new BridgeReply().With("name", c.Args.Get<string>("name")).With("result", n.Slot.Clear());
        }

        object Release(ICommandContext c)
        {
            if (c.Args.Get("forget", false)) auto.Clear();
            if (c.Args.Get("all", false))
            {
                int k = ctx.Overrides.ReleaseAll();
                return new BridgeReply().With("released", k);
            }
            var n = Get(c);
            bool was = !n.Slot.IsReleased;
            n.Slot.Release();
            n.Slot.Release();   // a second release does nothing
            return new BridgeReply().With("released", was ? 1 : 0);
        }

        object State(ICommandContext c)
        {
            var o = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var kv in slots)
            {
                var s = kv.Value.Slot;
                o[kv.Key] = new BridgeReply().With("nodeId", s.NodeId).With("path", s.Path).With("kind", s.Kind.ToString()).With("priority", s.Priority)
                    .With("isReleased", s.IsReleased).With("hasValue", s.HasValue).With("isWinning", s.IsWinning).With("targetExists", s.TargetExists);
            }
            var list = new List<object>();
            foreach (var i in ctx.Overrides.List())
                list.Add(new BridgeReply().With("nodeId", i.NodeId).With("path", i.Path).With("owner", i.Owner).With("priority", i.Priority)
                    .With("winning", i.Winning).With("effective", i.Effective).With("targetExists", i.TargetExists));
            return new BridgeReply().With("available", ctx.Overrides.Available).With("reacquired", reacquired).With("updates", updates).With("frameSets", frameSets).With("slots", o).With("list", list);
        }

        object Bench(ICommandContext c)
        {
            var s = Get(c).Slot;
            int n = Math.Max(10, c.Args.Get("n", 10000));
            var ticks = new long[n];
            // warm up (the first calls compile)
            for (int i = 0; i < 100; i++) s.Set(i * 0.001, 0.5, -i * 0.001);
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < n; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                s.Set(1 + i * 1e-4, 0.25, -1 - i * 1e-4);
                ticks[i] = Stopwatch.GetTimestamp() - t0;
            }
            long a1 = GC.GetAllocatedBytesForCurrentThread();
            int collections = GC.CollectionCount(0) - g0 + GC.CollectionCount(1) - g1 + GC.CollectionCount(2) - g2;
            Array.Sort(ticks);
            double us(long t) => t * 1e6 / Stopwatch.Frequency;
            return new BridgeReply().With("n", n).With("medianUs", Math.Round(us(ticks[n / 2]), 3)).With("p95Us", Math.Round(us(ticks[(int)(n * 0.95)]), 3))
                .With("maxUs", Math.Round(us(ticks[n - 1]), 3)).With("collections", collections).With("allocatedBytes", a1 - a0);
        }
    }
}

// The test plugin of the row RagdollCheck.player (CC0-1.0). What a command cannot see, by a Stopwatch on the player's main thread and by the app's own classes:
//   ragdollprobe.idle     a scene with no body: the ragdoll part's calls of a frame, time and heap, and that no physics object exists
//   ragdollprobe.tick     RagdollPart.Tick over frames of 1/60 s with the clock paused: time, substeps, awake parts
//   ragdollprobe.heap     the heap over a run of ticks, against a known allocation
//   ragdollprobe.gc       the collections and the heap now, for a stretch of real frames
//   ragdollprobe.body     a body as the world holds it: state, parts, lowest point, floor, patch, build time
//   ragdollprobe.hash     the bits of every part of a body
//   ragdollprobe.startwatch, .start  the same bits where physics starts: at the first tick of the world with time in it, before its first substep
//   ragdollprobe.settled  a body's settled buffer, as bits
//   ragdollprobe.seed     FaceSetup.Seed, so that two runs build the same face
//   ragdollprobe.fx       3,000 droplets and 400 splats: a step and the mesh
//   ragdollprobe.bullet   bullets through a body
//   ragdollprobe.pick     RagdollWorld.Pick on rays through a body
//   ragdollprobe.memory   the managed and native meters, with a known allocation as control
//   ragdollprobe.obstacle what an obstacle became on this player
//   ragdollprobe.item     what an item draws here: its bounds, its renderers and how many of their meshes this player can read
//   ragdollprobe.shaders  the shaders of the effects and the props
//   ragdollprobe.view     the view's camera on or off, every camera render counted since the plugin loaded
//   ragdollprobe.first    the first renders after a build, in memory: first against second against third, and values that are no number
// Times are of the calls themselves; nothing here is part of a scene. No render is written anywhere.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HS2Next;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using HS2Next.App;
using HS2Next.Chara.Mosaic;
using HS2Next.Motion.Face;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Screenshots;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Stp3.Test.RagdollProbe
{
    public sealed class RagdollProbePlugin : IPlugin
    {
        const float Dt = 1f / 60f;
        static readonly string[] EffectShaders = { "HS2Next/Hidden/WetDrops", "HS2Next/Unlit/Color" };

        int cameraRenders, viewRendersOff;
        bool viewOff;
        readonly Dictionary<string, int> perCamera = new Dictionary<string, int>(StringComparer.Ordinal);

        static SceneRuntime Rt => AppCore.Current.Runtime;
        static RagdollPart Part => Rt.GetPart<RagdollPart>();
        static Camera View => Rt.GetPart<SceneViewCamera>().Camera;

        static RagdollBody Body(string key) => Part.BodyOf(key) ?? throw new InvalidOperationException("no body on " + key);

        static double Round(double v) => Math.Round(v, 4);
        static double Ms(Stopwatch sw) => sw.Elapsed.TotalMilliseconds;

        static Dictionary<string, object> Row(List<double> v)
        {
            if (v.Count == 0) return new Dictionary<string, object> { ["n"] = 0, ["median"] = 0.0, ["p95"] = 0.0, ["max"] = 0.0, ["first"] = 0.0 };
            var s = v.OrderBy(x => x).ToList();
            return new Dictionary<string, object>
            {
                ["n"] = v.Count, ["median"] = Round(s[s.Count / 2]), ["p95"] = Round(s[Math.Min(s.Count - 1, (int)(s.Count * .95))]), ["max"] = Round(s[s.Count - 1]), ["first"] = Round(v[0]),
            };
        }

        static float[] Three(Vector3 v) => new[] { v.x, v.y, v.z };

        public void Load(IPluginContext context)
        {
            var c = context.Commands;
            RenderPipelineManager.beginCameraRendering += OnCamera;
            c.Register(new CommandSpec("ragdollprobe.idle", "Times the ragdoll part with no body.").Arg("n", ArgTypes.Int, "frames"), a => Idle(a.Args.Get("n", 200000)));
            c.Register(new CommandSpec("ragdollprobe.tick", "Times RagdollPart.Tick with the clock paused.").Arg("frames", ArgTypes.Int, "frames").Arg("warm", ArgTypes.Int, "frames before").Arg("wake", ArgTypes.Bool, "wake every body before each frame"),
                a => Tick(a.Args.Get("frames", 300), a.Args.Get("warm", 60), a.Args.Get("wake", true)));
            c.Register(new CommandSpec("ragdollprobe.heap", "The heap over a run of ticks, against a known allocation.").Arg("frames", ArgTypes.Int, "frames").Arg("bytes", ArgTypes.Int, "the control's size a frame"),
                a => Heap(a.Args.Get("frames", 2000), a.Args.Get("bytes", 256)));
            c.Register(new CommandSpec("ragdollprobe.gc", "The collections and the heap now.").WithFlags(CommandFlags.ReadOnly), _ => new Dictionary<string, object>
            {
                ["gen0"] = GC.CollectionCount(0), ["gen1"] = GC.CollectionCount(1), ["heap"] = GC.GetTotalMemory(false), ["frame"] = Time.frameCount,
            });
            c.Register(new CommandSpec("ragdollprobe.body", "A body as the world holds it.").Arg("key", ArgTypes.String, "a character", required: true).WithFlags(CommandFlags.ReadOnly), a => BodyRow(a.Args.Require<string>("key")));
            c.Register(new CommandSpec("ragdollprobe.hash", "The bits of every part of the bodies.").Arg("keys", ArgTypes.ArrayOf(ArgTypes.String), "characters, in order", required: true).WithFlags(CommandFlags.ReadOnly),
                a => Hash(a.Args.Get("keys", new List<string>())));
            c.Register(new CommandSpec("ragdollprobe.startwatch", "Hashes the bodies at the world's first tick with time in it, before its first substep.").Arg("keys", ArgTypes.ArrayOf(ArgTypes.String), "characters, in order", required: true),
                a => StartWatch(a.Args.Get("keys", new List<string>())));
            c.Register(new CommandSpec("ragdollprobe.start", "What the start watch read.").WithFlags(CommandFlags.ReadOnly), _ => new Dictionary<string, object>
            {
                ["read"] = start != null && start.Read, ["hash"] = start?.Value, ["dt"] = start?.Dt ?? 0f, ["steps"] = start?.Steps ?? -1, ["frame"] = start?.Frame ?? -1,
            });
            c.Register(new CommandSpec("ragdollprobe.settled", "A body's settled buffer, as bits.").Arg("key", ArgTypes.String, "a character", required: true).WithFlags(CommandFlags.ReadOnly), a => Settled(a.Args.Require<string>("key")));
            c.Register(new CommandSpec("ragdollprobe.seed", "FaceSetup.Seed.").Arg("value", ArgTypes.Int, "the seed"), a =>
            {
                FaceSetup.Seed = a.Args.Get("value", 0x5eed);
                return new Dictionary<string, object> { ["seed"] = FaceSetup.Seed };
            });
            c.Register(new CommandSpec("ragdollprobe.fx", "Times 3,000 droplets and 400 splats.").Arg("steps", ArgTypes.Int, "steps"), a => Fx(a.Args.Get("steps", 300)));
            c.Register(new CommandSpec("ragdollprobe.bullet", "Times bullets through a body.").Arg("key", ArgTypes.String, "a character", required: true).Arg("n", ArgTypes.Int, "bullets"),
                a => Bullet(a.Args.Require<string>("key"), a.Args.Get("n", 200)));
            c.Register(new CommandSpec("ragdollprobe.pick", "Times RagdollWorld.Pick.").Arg("key", ArgTypes.String, "a character", required: true).Arg("n", ArgTypes.Int, "rays"), a => Pick(a.Args.Require<string>("key"), a.Args.Get("n", 1000)));
            c.Register(new CommandSpec("ragdollprobe.memory", "The memory meters, read after a collection.").Arg("control", ArgTypes.Bool, "with a known allocation of 1 MiB on each meter"), a => Memory(a.Args.Get("control", false)));
            c.Register(new CommandSpec("ragdollprobe.obstacle", "What an obstacle became here.").Arg("key", ArgTypes.String, "an item", required: true).WithFlags(CommandFlags.ReadOnly), a => Obstacle(a.Args.Require<string>("key")));
            c.Register(new CommandSpec("ragdollprobe.item", "What an item draws here.").Arg("key", ArgTypes.String, "an item", required: true).WithFlags(CommandFlags.ReadOnly), a => Item(a.Args.Require<string>("key")));
            c.Register(new CommandSpec("ragdollprobe.shaders", "The shaders of the effects and the props.").WithFlags(CommandFlags.ReadOnly), _ => Shaders());
            c.Register(new CommandSpec("ragdollprobe.view", "The view's camera on or off; the camera renders so far.").Arg("on", ArgTypes.Bool, "on"), a => ViewOn(a.Args.Has("on") ? a.Args.Get("on", true) : (bool?)null));
            c.Register(new CommandSpec("ragdollprobe.first", "Three renders in memory, each with the view's values: first against second against third.").Arg("mode", ArgTypes.String, "half: one camera into a half-float target; capture: three captures").Arg("key", ArgTypes.String, "a character").Arg("width", ArgTypes.Int, "width").Arg("height", ArgTypes.Int, "height"),
                a => First(a.Args.Get("mode", "half"), a.Args.Get("key", (string)null), a.Args.Get("width", 960), a.Args.Get("height", 540)));
        }

        public void Unload() => RenderPipelineManager.beginCameraRendering -= OnCamera;

        void OnCamera(ScriptableRenderContext _, Camera cam)
        {
            cameraRenders++;
            if (viewOff && cam == View) viewRendersOff++;
            perCamera.TryGetValue(cam.name, out int n);
            perCamera[cam.name] = n + 1;
        }

        // ------------------------------------------------------------ no body

        object Idle(int n)
        {
            var part = Part;
            if (part.World != null) throw new InvalidOperationException("this scene has a world");
            for (int i = 0; i < 2000; i++) { part.Apply(); part.Tick(Dt); part.LateApply(); }
            // Mono's allocation counter of a thread reads 0 in a player: the heap's use, against a control of one small array a frame
            long a0 = GC.GetTotalMemory(false);
            int g0 = GC.CollectionCount(0);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < n; i++) { part.Apply(); part.Tick(Dt); part.LateApply(); }
            double ms = Ms(sw);
            long alloc = GC.GetTotalMemory(false) - a0;
            int collections = GC.CollectionCount(0) - g0;
            sw.Restart();
            for (int i = 0; i < n; i++) { }
            double nothing = Ms(sw);
            a0 = GC.GetTotalMemory(false);
            g0 = GC.CollectionCount(0);
            byte[] made = null;
            for (int i = 0; i < n; i++) made = new byte[16];
            long control = GC.GetTotalMemory(false) - a0;
            int controlCollections = GC.CollectionCount(0) - g0;
            GC.KeepAlive(made);
            return new Dictionary<string, object>
            {
                ["frames"] = n, ["usPerFrame"] = Round(ms * 1000.0 / n), ["bytes"] = alloc, ["collections"] = collections, ["emptyLoopUs"] = Round(nothing * 1000.0 / n), ["allocationControl"] = control,
                ["controlCollections"] = controlCollections, ["rigidbodies"] = Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                ["colliders"] = Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None).Sum(r => r.GetComponentsInChildren<Collider>(true).Length),
                ["scenes"] = WorldScenes(), ["shots"] = RagdollShots.Of(Rt) != null, ["world"] = part.World != null,
            };
        }

        static int WorldScenes()
        {
            int n = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).name.StartsWith("HS2Next Ragdoll", StringComparison.Ordinal)) n++;
            return n;
        }

        // ------------------------------------------------------------ ticks

        static int Awake(RagdollWorld w)
        {
            int n = 0;
            foreach (var b in w.Bodies)
                foreach (var p in b.Parts)
                    if (!p.Rb.IsSleeping()) n++;
            return n;
        }

        object Tick(int frames, int warm, bool wake)
        {
            var part = Part;
            var w = part.World ?? throw new InvalidOperationException("no world");
            for (int i = 0; i < warm; i++) part.Tick(Dt);
            var times = new List<double>(frames);
            var sw = new Stopwatch();
            int awakeLeast = int.MaxValue, parts = w.Bodies.Sum(b => b.Parts.Count), subs = 0;
            long steps0 = w.Steps;
            for (int i = 0; i < frames; i++)
            {
                if (wake)
                    foreach (var b in w.Bodies) b.WakeAll();
                awakeLeast = Math.Min(awakeLeast, Awake(w));
                sw.Restart();
                part.Tick(Dt);
                times.Add(Ms(sw));
                subs = Math.Max(subs, w.LastSubsteps);
            }
            var r = Row(times);
            r["substepsMost"] = subs;
            r["substepsAll"] = w.Steps - steps0;
            r["bodies"] = w.Bodies.Count;
            r["parts"] = parts;
            r["awakeLeast"] = awakeLeast;
            r["floor"] = Round(w.FloorY);
            r["lowest"] = Round(w.Bodies.Min(b => b.Lowest()));
            r["hz"] = w.Values.Hz;
            return r;
        }

        /// <summary>
        /// The heap over a run of ticks, beside the same time spent doing nothing: Mono's heap is the process's, so what other threads take in that time is the base. A third run of ticks
        /// allocates a known array a frame, the control.
        /// </summary>
        object Heap(int frames, int bytes)
        {
            var part = Part;
            var w = part.World ?? throw new InvalidOperationException("no world");
            for (int i = 0; i < 300; i++) { foreach (var b in w.Bodies) b.WakeAll(); part.Tick(Dt); }
            byte[] made = null;
            var sw = new Stopwatch();
            (long bytes, int collections, double ms) Window(int kind, double spend)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long a0 = GC.GetTotalMemory(false);
                int g0 = GC.CollectionCount(0);
                sw.Restart();
                if (kind == 1) { while (Ms(sw) < spend) { } }
                else
                    for (int i = 0; i < frames; i++)
                    {
                        var bodies = w.Bodies;
                        for (int k = 0; k < bodies.Count; k++) bodies[k].WakeAll();
                        part.Tick(Dt);
                        if (kind == 2) made = new byte[bytes];
                    }
                return (GC.GetTotalMemory(false) - a0, GC.CollectionCount(0) - g0, Ms(sw));
            }
            var ticks = Window(0, 0);
            var idle = Window(1, ticks.ms);
            var control = Window(2, 0);
            GC.KeepAlive(made);
            return new Dictionary<string, object>
            {
                ["frames"] = frames, ["bytes"] = ticks.bytes, ["collections"] = ticks.collections, ["ms"] = Round(ticks.ms), ["baseBytes"] = idle.bytes, ["baseCollections"] = idle.collections, ["baseMs"] = Round(idle.ms),
                ["controlBytes"] = control.bytes, ["controlCollections"] = control.collections, ["controlMs"] = Round(control.ms), ["controlSize"] = bytes, ["bodies"] = w.Bodies.Count,
            };
        }

        // ------------------------------------------------------------ a body

        object BodyRow(string key)
        {
            var w = Part.World ?? throw new InvalidOperationException("no world");
            var b = Body(key);
            var parts = new List<object>();
            double speed = 0;
            int awake = 0;
            foreach (var p in b.Parts)
            {
                var c = p.Rb.worldCenterOfMass;
                speed = Math.Max(speed, p.Rb.linearVelocity.magnitude);
                if (!p.Rb.IsSleeping()) awake++;
                parts.Add(new Dictionary<string, object> { ["key"] = p.Key, ["pos"] = Three(c), ["lowest"] = Round(RagdollBody.LowestOf(p)), ["finite"] = float.IsFinite(c.x + c.y + c.z) });
            }
            var patch = w.Floor?.PatchOf(b);
            var row = new Dictionary<string, object>
            {
                ["state"] = b.State.ToString(), ["awake"] = awake, ["lowest"] = Round(b.Lowest()), ["speed"] = Round(speed), ["pelvis"] = Three(b.Part("pelvis").Rb.worldCenterOfMass), ["buildMs"] = Round(b.BuildMs), ["mass"] = Round(b.Mass),
                ["floor"] = Round(w.FloorY), ["floorSource"] = w.FloorSource, ["room"] = new[] { w.RoomCenter.x, w.RoomCenter.y, w.RoomSize.x, w.RoomSize.y, w.RoomSize.z }, ["parts"] = parts, ["steps"] = w.Steps,
                ["worldHandle"] = w.Scene.handle.ToString(), ["recentres"] = w.Floor?.Recentres ?? 0,
            };
            if (patch != null)
                row["patch"] = new Dictionary<string, object>
                {
                    ["found"] = patch.Found, ["quads"] = patch.Quads, ["gpu"] = patch.FromGpu, ["mesh"] = patch.FromMesh, ["terrain"] = patch.FromTerrain, ["collider"] = patch.FromCollider, ["queryMs"] = Round(patch.QueryMs), ["meshMs"] = Round(patch.MeshMs),
                    ["reads"] = patch.Reads, ["lowest"] = Round(patch.Lowest), ["top"] = Round(patch.Top), ["center"] = new[] { patch.Center.x, patch.Center.y },
                };
            return row;
        }

        static ulong HashOf(List<string> keys, out int parts)
        {
            ulong h = 14695981039346656037UL;
            void Add(float f) { unchecked { h ^= (uint)BitConverter.SingleToInt32Bits(f); h *= 1099511628211UL; } }
            parts = 0;
            foreach (var k in keys)
                foreach (var p in Body(k).Parts)
                {
                    var a = p.Rb.position;
                    var q = p.Rb.rotation;
                    Add(a.x); Add(a.y); Add(a.z); Add(q.x); Add(q.y); Add(q.z); Add(q.w);
                    parts++;
                }
            return h;
        }

        object Hash(List<string> keys)
        {
            ulong h = HashOf(keys, out int parts);
            var chest = Body(keys[0]).Part("chest").Rb.position;
            return new Dictionary<string, object> { ["hash"] = h.ToString("x16"), ["parts"] = parts, ["chest"] = Three(chest), ["world"] = Part.World.Scene.handle.ToString(), ["steps"] = Part.World.Steps };
        }

        /// <summary>A world member that reads the bodies once, at the start of the first tick with time in it: after pushers and pins (none in RK1), before the states, the drives and the substeps.</summary>
        sealed class StartReader : IRagdollWorldMember
        {
            readonly List<string> keys;
            public bool Read;
            public string Value;
            public float Dt;
            public long Steps;
            public int Frame;

            public StartReader(List<string> keys) { this.keys = keys; }
            public bool HoldsWorld => false;

            public void BeforeSteps(float dt)
            {
                if (Read || !(dt > 0f)) return;
                Read = true;
                Value = HashOf(keys, out _).ToString("x16");
                Dt = dt;
                Steps = Part.World.Steps;
                Frame = Time.frameCount;
            }

            public void BeforeSubstep(float h) { }
            public void AfterSteps(float dt) { }
            public void WorldClosing() { }
        }

        StartReader start;

        object StartWatch(List<string> keys)
        {
            var w = Part.World ?? throw new InvalidOperationException("no world");
            start = new StartReader(keys);
            w.AddMember(start);
            return new Dictionary<string, object> { ["armed"] = true, ["members"] = w.Members.Count };
        }

        object Settled(string key)
        {
            var b = Body(key);
            var q = b.SettledParts;
            var parts = new Dictionary<string, object>();
            if (q != null)
                for (int i = 0; i < q.Length && i < b.Parts.Count; i++)
                    parts[b.Parts[i].Key] = new[] { BitConverter.SingleToInt32Bits(q[i].x), BitConverter.SingleToInt32Bits(q[i].y), BitConverter.SingleToInt32Bits(q[i].z), BitConverter.SingleToInt32Bits(q[i].w) };
            var hp = b.SettledHips;
            return new Dictionary<string, object>
            {
                ["held"] = q != null, ["changed"] = b.SettledChanged, ["parts"] = parts,
                ["hips"] = new[] { BitConverter.SingleToInt32Bits(hp.x), BitConverter.SingleToInt32Bits(hp.y), BitConverter.SingleToInt32Bits(hp.z) },
            };
        }

        // ------------------------------------------------------------ effects, bullets, picks

        object Fx(int steps)
        {
            var holder = new GameObject("ragdollprobe_fx");
            var fx = new RagdollFx(holder.transform);
            try
            {
                Func<float, float, float> floor = (x, z) => 0f, none = (x, z) => -1e6f;
                int shot = 0;
                for (int i = 0; i < 60; i++)
                {
                    fx.BeginShot(0, shot++);
                    fx.Blood(new Vector3(0f, 2f, 0f), Vector3.forward, 300f, true);
                }
                for (int i = 0; i < 90; i++) fx.Step(Dt, 98.1f, floor);
                var step = new List<double>(steps);
                var mesh = new List<double>(steps);
                var both = new List<double>(steps);
                int least = int.MaxValue;
                var sw = new Stopwatch();
                for (int n = 0; n < steps; n++)
                {
                    while (fx.Droplets < RagdollFx.MaxDroplets - 60)
                    {
                        fx.BeginShot(0, shot++);
                        fx.Blood(new Vector3(0f, 500f, 0f), Vector3.forward, 600f, true);
                    }
                    least = Math.Min(least, fx.Droplets);
                    sw.Restart();
                    fx.Step(Dt, 98.1f, none);
                    double a = Ms(sw);
                    sw.Restart();
                    fx.Draw();
                    double b = Ms(sw);
                    step.Add(a);
                    mesh.Add(b);
                    both.Add(a + b);
                }
                return new Dictionary<string, object> { ["droplets"] = least, ["splats"] = fx.Splats, ["step"] = Row(step), ["mesh"] = Row(mesh), ["both"] = Row(both) };
            }
            finally
            {
                fx.Dispose();
                Object.Destroy(holder);
            }
        }

        object Bullet(string key, int n)
        {
            var shots = RagdollShots.For(Rt);
            var b = Body(key);
            var times = new List<double>(n);
            var sw = new Stopwatch();
            int hit = 0, parts = 0;
            for (int i = 0; i < n; i++)
            {
                var at = b.Part("chest").Rb.worldCenterOfMass;
                var dir = new Vector3(Mathf.Sin(i * .37f), -.1f, Mathf.Cos(i * .37f)).normalized;
                var origin = at - dir * 30f;
                sw.Restart();
                var r = shots.Fire(origin, dir, origin, 181f, false);
                times.Add(Ms(sw));
                if (r.Hits > 0) hit++;
                parts += r.Blows.Count;
            }
            var row = Row(times);
            row["hit"] = hit;
            row["blows"] = parts;
            row["droplets"] = shots.Fx?.Droplets ?? 0;
            shots.Clear();
            return row;
        }

        object Pick(string key, int n)
        {
            var w = Part.World ?? throw new InvalidOperationException("no world");
            var b = Body(key);
            var times = new List<double>(n);
            var sw = new Stopwatch();
            int hit = 0;
            var at = b.Part("chest").Rb.worldCenterOfMass;
            for (int i = 0; i < n; i++)
            {
                float a = i * .618f, r = (i % 7) * .35f;
                var dir = new Vector3(Mathf.Sin(a), -.2f, Mathf.Cos(a)).normalized;
                var ray = new Ray(at + new Vector3(Mathf.Cos(a) * r, (i % 5 - 2) * 1.5f, -Mathf.Sin(a) * r) - dir * 30f, dir);
                sw.Restart();
                bool got = w.Pick(ray, out _);
                times.Add(Ms(sw));
                if (got) hit++;
            }
            var row = Row(times);
            row["hit"] = hit;
            return row;
        }

        // ------------------------------------------------------------ memory

        object Memory(bool control)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long managed = GC.GetTotalMemory(true), native = Profiler.GetTotalAllocatedMemoryLong(), mono = Profiler.GetMonoUsedSizeLong(), gfx = Profiler.GetAllocatedMemoryForGraphicsDriver();
            var row = new Dictionary<string, object> { ["managed"] = managed, ["native"] = native, ["mono"] = mono, ["graphics"] = gfx, ["rigidbodies"] = Part.World?.Bodies.Sum(b => b.Parts.Count) ?? 0 };
            if (!control) return row;
            const int size = 1 << 20;
            var a = new NativeArray<byte>(size, Allocator.Persistent);
            long nativeThen = Profiler.GetTotalAllocatedMemoryLong();
            a.Dispose();
            var made = new byte[size];
            long managedThen = GC.GetTotalMemory(false);
            GC.KeepAlive(made);
            row["controlNative"] = nativeThen - native;
            row["controlManaged"] = managedThen - managed;
            row["controlSize"] = size;
            return row;
        }

        object Obstacle(string key)
        {
            var w = Part.World ?? throw new InvalidOperationException("no world");
            var o = w.Obstacles?.Of(key);
            if (o == null) return new Dictionary<string, object> { ["found"] = false };
            var cols = o.Root ? o.Root.GetComponentsInChildren<MeshCollider>(true) : new MeshCollider[0];
            return new Dictionary<string, object>
            {
                ["found"] = true, ["meshes"] = o.Meshes, ["box"] = o.Box != null, ["boxSize"] = o.Box != null ? Three(o.Box.size) : null, ["meshColliders"] = cols.Length, ["readable"] = cols.Count(c => c.sharedMesh && c.sharedMesh.isReadable),
                ["vertices"] = cols.Sum(c => c.sharedMesh ? c.sharedMesh.vertexCount : 0),
            };
        }

        object Item(string key)
        {
            var r = Rt.FindItem(key) ?? throw new InvalidOperationException("no item " + key);
            int renderers = 0, readable = 0, vertices = 0;
            Bounds b = default;
            foreach (var x in VisibilityApplier.OwnRenderers(r.Holder))
            {
                if (!x.enabled || !x.gameObject.activeInHierarchy) continue;
                if (x is MeshRenderer)
                {
                    var m = x.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh : null;
                    if (!m) continue;
                    if (m.isReadable) { readable++; vertices = Math.Max(vertices, m.vertexCount); }
                }
                else if (!(x is SkinnedMeshRenderer)) continue;
                if (renderers++ == 0) b = x.bounds;
                else b.Encapsulate(x.bounds);
            }
            return new Dictionary<string, object> { ["renderers"] = renderers, ["readable"] = readable, ["vertices"] = vertices, ["size"] = Three(b.size), ["center"] = Three(b.center), ["min"] = Three(b.min), ["max"] = Three(b.max) };
        }

        object Shaders()
        {
            var rows = new List<object>();
            foreach (var name in EffectShaders)
            {
                var s = Shader.Find(name);
                rows.Add(new Dictionary<string, object> { ["name"] = name, ["found"] = s != null, ["supported"] = s != null && s.isSupported, ["passes"] = s != null ? s.passCount : 0 });
            }
            return new Dictionary<string, object> { ["rows"] = rows };
        }

        // ------------------------------------------------------------ the first renders

        object ViewOn(bool? on)
        {
            if (on.HasValue)
            {
                View.enabled = on.Value;
                viewOff = !on.Value;
                viewRendersOff = 0;
            }
            return new Dictionary<string, object> { ["enabled"] = View.enabled, ["cameraRenders"] = cameraRenders, ["viewRendersWhileOff"] = viewRendersOff, ["perCamera"] = new Dictionary<string, int>(perCamera), ["device"] = SystemInfo.graphicsDeviceType.ToString() };
        }

        /// <summary>One camera as a capture makes it, into a target of half floats: every render is one render.</summary>
        sealed class HalfCamera : IDisposable
        {
            readonly GameObject go;
            readonly Camera cam;
            readonly RenderTexture rt;
            readonly int w, h;

            public HalfCamera(Camera view, int width, int height)
            {
                w = width;
                h = height;
                go = new GameObject("ragdollprobe_camera") { hideFlags = HideFlags.HideAndDontSave };
                cam = go.AddComponent<Camera>();
                cam.enabled = false;
                cam.transform.SetPositionAndRotation(view.transform.position, view.transform.rotation);
                cam.fieldOfView = view.fieldOfView;
                cam.nearClipPlane = view.nearClipPlane;
                cam.farClipPlane = view.farClipPlane;
                var urp = go.AddComponent<UniversalAdditionalCameraData>();
                var setup = ViewSetup.Of(view);
                if (setup != null) setup.ApplyTo(cam, true);
                else
                {
                    cam.cullingMask = view.cullingMask;
                    cam.clearFlags = view.clearFlags;
                    cam.backgroundColor = view.backgroundColor;
                    cam.allowHDR = true;
                    urp.renderPostProcessing = true;
                }
                urp.antialiasing = AntialiasingMode.None;
                rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = 1, name = "ragdollprobe_first" };
                rt.Create();
                cam.targetTexture = rt;
                cam.ResetProjectionMatrix();
                cam.aspect = w / (float)h;
            }

            public ushort[] Render()
            {
                using (MosaicCamera.OutputScope(cam, 1)) cam.Render();
                var req = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBAHalf);
                req.WaitForCompletion();
                if (req.hasError) throw new InvalidOperationException("the read-back failed");
                return req.GetData<ushort>().ToArray();
            }

            public void Dispose()
            {
                cam.targetTexture = null;
                rt.Release();
                Object.Destroy(rt);
                Object.Destroy(go);
            }
        }

        static bool NoNumber(ushort half) => (half & 0x7c00) == 0x7c00;

        static Dictionary<string, object> Against(ushort[] a, ushort[] b, int w, int h, bool[] character)
        {
            int differ = 0, onChara = 0, x0 = w, y0 = h, x1 = -1, y1 = -1;
            for (int i = 0; i < w * h; i++)
            {
                int k = 4 * i;
                if (a[k] == b[k] && a[k + 1] == b[k + 1] && a[k + 2] == b[k + 2]) continue;
                differ++;
                if (character != null && character[i]) onChara++;
                int x = i % w, y = i / w;
                x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
            }
            return new Dictionary<string, object> { ["differ"] = differ, ["onCharacter"] = onChara, ["box"] = differ > 0 ? new[] { x0, y0, x1, y1 } : null };
        }

        static int NoNumbers(ushort[] a, int pixels)
        {
            int n = 0;
            for (int i = 0; i < pixels; i++)
                if (NoNumber(a[4 * i]) || NoNumber(a[4 * i + 1]) || NoNumber(a[4 * i + 2])) n++;
            return n;
        }

        static List<Renderer> HideCharacter(string key)
        {
            var off = new List<Renderer>();
            if (key == null) return off;
            var r = Rt.FindChara(key);
            if (r?.Context?.Root == null) return off;
            foreach (var x in r.Context.Root.GetComponentsInChildren<Renderer>(true))
                if (!x.forceRenderingOff) { x.forceRenderingOff = true; off.Add(x); }
            return off;
        }

        object First(string mode, string key, int w, int h)
        {
            int before = cameraRenders;
            var view = View;
            if (mode == "capture")
            {
                var req = new CaptureRequest { Width = w, Height = h, Supersample = 1, Aa = CaptureService.Smaa };
                var px = new Color32[3][];
                for (int i = 0; i < 3; i++)
                {
                    var r = CaptureService.RenderSync(Rt, view, req);
                    if (!r.Ok || r.Png == null) throw new InvalidOperationException("the capture failed: " + r.Error);
                    var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    ImageConversion.LoadImage(t, r.Png, false);
                    px[i] = t.GetPixels32();
                    Object.Destroy(t);
                    r.Png = null;
                }
                var off = HideCharacter(key);
                Color32[] empty;
                {
                    var r = CaptureService.RenderSync(Rt, view, req);
                    var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    ImageConversion.LoadImage(t, r.Png, false);
                    empty = t.GetPixels32();
                    Object.Destroy(t);
                    r.Png = null;
                }
                foreach (var x in off) x.forceRenderingOff = false;
                int chara = 0, d12 = 0, d23 = 0, on12 = 0;
                for (int i = 0; i < px[0].Length; i++)
                {
                    bool c = !px[1][i].Equals(empty[i]);
                    if (c) chara++;
                    if (!px[0][i].Equals(px[1][i])) { d12++; if (c) on12++; }
                    if (!px[1][i].Equals(px[2][i])) d23++;
                }
                return new Dictionary<string, object> { ["mode"] = mode, ["camerasBefore"] = before, ["firstSecond"] = d12, ["firstSecondOnCharacter"] = on12, ["secondThird"] = d23, ["character"] = chara, ["pixels"] = px[0].Length, ["noNumberKnown"] = false };
            }
            using (var cam = new HalfCamera(view, w, h))
            {
                var a = cam.Render();
                var b = cam.Render();
                var c = cam.Render();
                var off = HideCharacter(key);
                var e = cam.Render();
                foreach (var x in off) x.forceRenderingOff = false;
                // the character's pixels: a channel more than 1/255 apart from the render without it
                var chara = new bool[w * h];
                int count = 0;
                for (int i = 0; i < w * h; i++)
                {
                    int k = 4 * i;
                    for (int ch = 0; ch < 3; ch++)
                        if (Math.Abs(Mathf.HalfToFloat(b[k + ch]) - Mathf.HalfToFloat(e[k + ch])) > 1f / 255f) { chara[i] = true; count++; break; }
                }
                return new Dictionary<string, object>
                {
                    ["mode"] = mode, ["camerasBefore"] = before, ["firstSecond"] = Against(a, b, w, h, chara), ["secondThird"] = Against(b, c, w, h, chara), ["character"] = count, ["pixels"] = w * h, ["noNumberKnown"] = true,
                    ["noNumber"] = new[] { NoNumbers(a, w * h), NoNumbers(b, w * h), NoNumbers(c, w * h) },
                };
            }
        }
    }
}

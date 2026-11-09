// What the row WetCheck.player reads inside the player, by a Stopwatch on its main thread (CC0-1.0):
//   wetprobe.bake     a bake with no picture, a drag's step, a picture's read-back and encode, a whole bake with its pictures
//   wetprobe.step     a spread step: the character's tick when a stain's radius moved, and the same to the end of the GPU's work
//   wetprobe.idle     a scene with no stain: the wet part's calls of a frame, time and allocation
//   wetprobe.surface  the map query through IMapQuery: every question's time, its source
//   wetprobe.pick     a surface pick through IViewportTools: rays through the character (the UI must run)
//   wetprobe.load     a stain's maps read again as a load reads them
//   wetprobe.drops    the most drops a character can have: a step and the mesh
//   wetprobe.rts      the render textures that exist now, by arithmetic
//   wetprobe.mirror   the counters of the mirrors
//   wetprobe.count    the cameras rendered and the mirrors' renders over a stretch of frames
//   wetprobe.films    a character's films: made, drawn, seen by a camera
//   wetprobe.shaders  the hidden shaders of this phase as the player finds them
//   wetprobe.render, .rendered, .compare, .forget   the view through the capture service, kept in memory: never a file
//   wetprobe.flat     how flat a box of a kept render is: neighbours equal, colours in it
//   wetprobe.members  a character's members: where their bounds fall in an image, directly and in a floor's mirror; can leave them undrawn for a render kept in memory
//   wetprobe.mirrorframe  what a floor's mirror camera computes for an output of a size, on a camera of the probe's: the floor F, each member's block, the wide method's blocks
//   wetprobe.region   a kept render inside a mask of two renders and in a ring just outside it: equal neighbours, flat runs, blocks of a grid
//   wetprobe.puddle   a large puddle in a character's block, for a view that needs one
// Times are of the calls themselves; nothing here is part of a scene unless a command says so.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HS2Next;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using HS2Next.Api.Unity;
using HS2Next.App;
using HS2Next.Chara.Mosaic;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Screenshots;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Stp3.Test.WetProbe
{
    public sealed partial class WetProbePlugin
    {
        static readonly string[] HiddenShaders =
        {
            "HS2Next/Hidden/WetBake", "HS2Next/Hidden/WetCompose", "HS2Next/Hidden/WetFilm", "HS2Next/Hidden/WetPuddle", "HS2Next/Hidden/WetDrops", "HS2Next/Hidden/MapSurface",
        };

        sealed class Shot { public int W, H; public Color32[] Px; public CaptureResult Result; public bool Done; public string Error; }

        readonly Dictionary<string, Shot> shots = new Dictionary<string, Shot>(StringComparer.Ordinal);

        static SceneRuntime Rt => AppCore.Current.Runtime;
        static WetPart Part => Rt.GetPart<WetPart>();
        static Camera View => Rt.GetPart<SceneViewCamera>().Camera;
        static CharaNodeState State(string key) => (CharaNodeState)Rt.Model.Find(key).State;

        static double[] Stats(List<double> v)
        {
            if (v.Count == 0) return new double[] { 0, 0, 0, 0 };
            var s = v.OrderBy(x => x).ToList();
            return new[] { s[s.Count / 2], s[Math.Min(s.Count - 1, (int)(s.Count * .95))], s[s.Count - 1], v[0] };
        }

        static Dictionary<string, object> Row(List<double> v)
        {
            var s = Stats(v);
            return new Dictionary<string, object> { ["n"] = v.Count, ["median"] = Round(s[0]), ["p95"] = Round(s[1]), ["max"] = Round(s[2]), ["first"] = Round(s[3]) };
        }

        static double Round(double v) => Math.Round(v, 4);
        static double Ms(Stopwatch sw) => sw.Elapsed.TotalMilliseconds;

        void RegisterTimes(IPluginContext context)
        {
            var c = context.Commands;
            c.Register(new CommandSpec("wetprobe.bake", "Times a character's bake.").Arg("key", ArgTypes.String, "a character", required: true).Arg("rounds", ArgTypes.Int, "rounds"), a => Bake(a.Args.Require<string>("key"), a.Args.Get("rounds", 12)));
            c.Register(new CommandSpec("wetprobe.step", "Times a spread step.").Arg("key", ArgTypes.String, "a character", required: true).Arg("ticks", ArgTypes.Int, "ticks").Arg("dt", ArgTypes.Float, "a tick's time"), a => Step(a.Args.Require<string>("key"), a.Args.Get("ticks", 240), a.Args.Get("dt", 1f / 60f)));
            c.Register(new CommandSpec("wetprobe.idle", "Times the wet part with no stain.").Arg("n", ArgTypes.Int, "frames"), a => Idle(a.Args.Get("n", 200000)));
            c.Register(new CommandSpec("wetprobe.surface", "Times the map query.").Arg("x", ArgTypes.Float, "x").Arg("y", ArgTypes.Float, "y").Arg("z", ArgTypes.Float, "z").Arg("n", ArgTypes.Int, "questions"),
                a => Surface(new Vector3(a.Args.Get("x", 0f), a.Args.Get("y", 12f), a.Args.Get("z", 0f)), a.Args.Get("n", 200)));
            c.Register(new CommandSpec("wetprobe.pick", "Times a surface pick.").Arg("key", ArgTypes.String, "a character", required: true).Arg("passes", ArgTypes.Int, "passes"), a => Pick(a.Args.Require<string>("key"), a.Args.Get("passes", 3)));
            c.Register(new CommandSpec("wetprobe.load", "Times a stain's maps read again.").Arg("key", ArgTypes.String, "a character", required: true).Arg("keys", ArgTypes.ArrayOf(ArgTypes.String), "every character with a stain").Arg("rounds", ArgTypes.Int, "rounds"),
                a => LoadMaps(a.Args.Require<string>("key"), a.Args.Get("keys", new List<string>()), a.Args.Get("rounds", 5)));
            c.Register(new CommandSpec("wetprobe.drops", "Times the most drops a character can have.").Arg("n", ArgTypes.Int, "steps"), a => Drops(a.Args.Get("n", 300)));
            c.Register(new CommandSpec("wetprobe.rts", "The render textures now.").Arg("key", ArgTypes.String, "a character: also what its rig owns").WithFlags(CommandFlags.ReadOnly), a => RenderTextures(a.Args.Get("key", (string)null)));
            c.Register(new CommandSpec("wetprobe.parts", "What a stain can reach on a character, part by part.").Arg("key", ArgTypes.String, "a character", required: true).WithFlags(CommandFlags.ReadOnly), a => Parts(a.Args.Require<string>("key")));
            c.Register(new CommandSpec("wetprobe.mirror", "The mirrors' counters.").WithFlags(CommandFlags.ReadOnly), _ => Mirror());
            c.Register(new CommandSpec("wetprobe.count", "Starts counting camera renders, or ends it and answers.").Arg("start", ArgTypes.Bool, "start"), a => Count(a.Args.Get("start", false)));
            c.Register(new CommandSpec("wetprobe.films", "A character's films.").Arg("key", ArgTypes.String, "a character", required: true).WithFlags(CommandFlags.ReadOnly), a => Films(a.Args.Require<string>("key")));
            c.Register(new CommandSpec("wetprobe.shaders", "The phase's hidden shaders.").WithFlags(CommandFlags.ReadOnly), _ => Shaders());
            c.Register(new CommandSpec("wetprobe.render", "Renders the view through the capture service, in memory.").Arg("tag", ArgTypes.String, "a name", required: true).Arg("async", ArgTypes.Bool, "RenderAsync, not RenderSync")
                .Arg("width", ArgTypes.Int, "width").Arg("height", ArgTypes.Int, "height").Arg("k", ArgTypes.Int, "supersample").Arg("aa", ArgTypes.String, "smaa, msaa4 or none").Arg("keep", ArgTypes.Bool, "keep the pixels for wetprobe.compare"),
                a => Render(a.Args.Require<string>("tag"), a.Args.Get("async", false), a.Args.Get("width", 1920), a.Args.Get("height", 1080), a.Args.Get("k", 1), a.Args.Get("aa", CaptureService.Smaa), a.Args.Get("keep", true)));
            c.Register(new CommandSpec("wetprobe.rendered", "How a render stands.").Arg("tag", ArgTypes.String, "a name", required: true).WithFlags(CommandFlags.ReadOnly), a => Rendered(a.Args.Require<string>("tag")));
            c.Register(new CommandSpec("wetprobe.compare", "Compares two renders kept in memory: numbers only.").Arg("a", ArgTypes.String, "a render", required: true).Arg("b", ArgTypes.String, "a render", required: true)
                .Arg("threshold", ArgTypes.Int, "a channel's difference that counts").Arg("within", ArgTypes.String, "a render that is compared with a to say where to look").Arg("withinOf", ArgTypes.String, "the render it is compared with")
                .Arg("character", ArgTypes.ArrayOf(ArgTypes.String), "two renders: where they differ is the character").Arg("mirror", ArgTypes.ArrayOf(ArgTypes.String), "two renders: where they differ is the mirror's area")
                .Arg("member", ArgTypes.ArrayOf(ArgTypes.String), "two renders: where they differ is the members'").WithFlags(CommandFlags.ReadOnly),
                a => Compare(a.Args.Require<string>("a"), a.Args.Require<string>("b"), a.Args.Get("threshold", 2), a.Args.Get("within", (string)null), a.Args.Get("withinOf", (string)null),
                    a.Args.Get("character", new List<string>()), a.Args.Get("mirror", new List<string>()), a.Args.Get("member", new List<string>())));
            c.Register(new CommandSpec("wetprobe.flat", "How flat a box of a kept render is.").Arg("tag", ArgTypes.String, "a render", required: true).Arg("x0", ArgTypes.Int, "left", required: true).Arg("y0", ArgTypes.Int, "bottom", required: true)
                .Arg("x1", ArgTypes.Int, "right", required: true).Arg("y1", ArgTypes.Int, "top", required: true).WithFlags(CommandFlags.ReadOnly),
                a => Flat(a.Args.Require<string>("tag"), a.Args.Require<int>("x0"), a.Args.Require<int>("y0"), a.Args.Require<int>("x1"), a.Args.Require<int>("y1")));
            c.Register(new CommandSpec("wetprobe.mirrorframe", "What a floor's mirror camera computes for an output of a size.").Arg("width", ArgTypes.Int, "output width").Arg("height", ArgTypes.Int, "output height").Arg("plane", ArgTypes.Float, "the mirror's plane")
                .Arg("scale", ArgTypes.Float, "mirror.scale").Arg("k", ArgTypes.Int, "the capture's supersampling"), a => MirrorFrame(a.Args.Get("width", 960), a.Args.Get("height", 540), a.Args.Get("plane", 0f), a.Args.Get("scale", .5f), a.Args.Get("k", 1)));
            c.Register(new CommandSpec("wetprobe.region", "A kept render in a mask and in a ring outside it.").Arg("tag", ArgTypes.String, "the render read", required: true).Arg("mask", ArgTypes.ArrayOf(ArgTypes.String), "two renders: where they differ over threshold", required: true)
                .Arg("threshold", ArgTypes.Int, "a channel's difference that counts").Arg("erode", ArgTypes.Int, "pixels off the mask's rim").Arg("character", ArgTypes.ArrayOf(ArgTypes.String), "two renders: the ring stays where they differ").Arg("ringFrom", ArgTypes.Int, "the ring's inner distance").Arg("ringTo", ArgTypes.Int, "its outer distance")
                .Arg("block", ArgTypes.Float, "a grid's block in output pixels").Arg("anchorX", ArgTypes.Float, "its anchor").Arg("anchorY", ArgTypes.Float, "its anchor").Arg("band", ArgTypes.Int, "pixels left out at a block's edges and shortest runs left out").WithFlags(CommandFlags.ReadOnly),
                a => Region(a.Args.Require<string>("tag"), a.Args.Get("mask", new List<string>()), a.Args.Get("threshold", 2), a.Args.Get("erode", 1), a.Args.Get("character", new List<string>()), a.Args.Get("ringFrom", 3), a.Args.Get("ringTo", 9),
                    a.Args.Get("block", 0f), a.Args.Get("anchorX", 0f), a.Args.Get("anchorY", 0f), a.Args.Get("band", 2)));
            c.Register(new CommandSpec("wetprobe.project", "World points in an image of a size, directly and in a floor's mirror.").Arg("points", ArgTypes.ArrayOf(ArgTypes.Any), "[x, y, z] each", required: true)
                .Arg("width", ArgTypes.Int, "width").Arg("height", ArgTypes.Int, "height").Arg("plane", ArgTypes.Float, "the floor's mirror plane").WithFlags(CommandFlags.ReadOnly),
                a => ProjectPoints(a.Args.Get("points", new List<List<float>>()), a.Args.Get("width", 960), a.Args.Get("height", 540), a.Args.Get("plane", 0f)));
            c.Register(new CommandSpec("wetprobe.members", "A character's members in an image of a size.").Arg("key", ArgTypes.String, "a character", required: true).Arg("width", ArgTypes.Int, "width").Arg("height", ArgTypes.Int, "height")
                .Arg("plane", ArgTypes.Float, "the floor's mirror plane").Arg("hide", ArgTypes.Bool, "leave them undrawn, or draw them again"),
                a => Members(a.Args.Require<string>("key"), a.Args.Get("width", 960), a.Args.Get("height", 540), a.Args.Get("plane", 0f), a.Args.Has("hide") ? a.Args.Get("hide", false) : (bool?)null));
            c.Register(new CommandSpec("wetprobe.forget", "Forgets every render kept."), _ => { shots.Clear(); return new Dictionary<string, object>(); });
            c.Register(new CommandSpec("wetprobe.puddle", "Adds a puddle to the character's block.").Arg("key", ArgTypes.String, "a character", required: true).Arg("x", ArgTypes.Float, "x").Arg("y", ArgTypes.Float, "the height; absent: the character's floor").Arg("z", ArgTypes.Float, "z").Arg("area", ArgTypes.Float, "its area; 0: none").Arg("clear", ArgTypes.Bool, "the block's puddles go first"),
                a => Puddle(a.Args.Require<string>("key"), a.Args.Get("x", 0f), a.Args.Has("y") ? a.Args.Get("y", 0f) : float.NaN, a.Args.Get("z", 0f), a.Args.Get("area", 60f), a.Args.Get("clear", false)));
        }

        // ------------------------------------------------------------ bake

        object Bake(string key, int rounds)
        {
            var rig = Part.RigOf(key) ?? throw new InvalidOperationException("no rig on " + key);
            var st = State(key);
            var block = st.Wet.Clone();
            var stain = block.Stains.First(s => s.Id == block.Current);
            var draw = typeof(WetRig).GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException("WetRig.Draw");
            var run = rig.Runs.First(r => r.Doc.Id == stain.Id);
            List<double> bare = new List<double>(), preview = new List<double>(), commit = new List<double>(), whole = new List<double>();
            int maps = 0;
            string why = null;
            for (int i = 0; i < rounds + 2; i++)
            {
                object[] args = { block, stain, false, false, null };
                var sw = Stopwatch.StartNew();
                why = draw.Invoke(rig, args) as string;
                double t = Ms(sw);
                if (why != null) break;
                foreach (var f in (List<(WetTarget t, WetMapData d, float reach)>)args[4]) f.d.Release();
                sw.Restart();
                why = rig.Preview(block, stain);
                double p = Ms(sw);
                if (why != null) break;
                maps = run.Preview.Count;
                sw.Restart();
                foreach (var d in run.Preview.Values)
                {
                    var bytes = WetBake.ReadBack(d.Map);
                    if (bytes != null) WetBake.Encode(bytes, d.Width, d.Height);
                }
                double cm = Ms(sw);
                sw.Restart();
                why = rig.Bake(block, stain, false);
                double w = Ms(sw);
                if (why != null) break;
                if (i < 2) continue;   // the first two compile what they meet
                bare.Add(t); preview.Add(p); commit.Add(cm); whole.Add(w);
            }
            rig.Sync(st.Wet);
            return new Dictionary<string, object> { ["why"] = why, ["maps"] = maps, ["bake"] = Row(bare), ["preview"] = Row(preview), ["commit"] = Row(commit), ["whole"] = Row(whole), ["slots"] = rig.Slots.Count };
        }

        // ------------------------------------------------------------ a spread step

        object Step(string key, int ticks, float dt)
        {
            var rig = Part.RigOf(key) ?? throw new InvalidOperationException("no rig on " + key);
            var st = State(key);
            var run = rig.Runs.First(r => r.Doc.Id == st.Wet.Current);
            float t0 = run.T;
            bool running = run.Running;
            List<double> cpu = new List<double>(), read = new List<double>();
            int targets = 0;
            run.T = 0.02f;
            run.Running = true;
            rig.Tick(dt);   // a dry stain has no composite yet
            var rt = rig.Slots.Values.Select(s => s.Compose?.Wet).FirstOrDefault(x => x);
            // the main thread, a tick at a time
            for (int i = 0; i < ticks && run.Running; i++)
            {
                int was = rig.Composed;
                var sw = Stopwatch.StartNew();
                rig.Tick(dt);
                double c = Ms(sw);
                int n = rig.Composed - was;
                if (n == 0 || i < 8) continue;
                targets = Math.Max(targets, n);
                cpu.Add(c);
            }
            // what a read waits for with nothing queued
            for (int i = 0; i < 12; i++)
            {
                Sync(rt);
                var sw = Stopwatch.StartNew();
                Sync(rt);
                read.Add(Ms(sw));
            }
            // a run of steps and one read at its end: the GPU's work in it
            var through = new List<double>();
            for (int pass = 0; pass < 8; pass++)
            {
                run.T = 0.02f;
                int was = rig.Composed, steps = 0;
                Sync(rt);
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 40; i++)
                {
                    int c0 = rig.Composed;
                    rig.Tick(5f / 40f);
                    if (rig.Composed != c0) steps++;
                }
                Sync(rt);
                double all = Ms(sw);
                if (pass > 0 && steps > 0) through.Add(all / steps);
            }
            run.T = t0;
            run.Running = running;
            rig.Sync(st.Wet);
            return new Dictionary<string, object> { ["targets"] = targets, ["cpu"] = Row(cpu), ["perStep"] = Row(through), ["read"] = Row(read), ["slots"] = rig.Slots.Count };
        }

        static void Sync(RenderTexture rt)
        {
            if (!rt) return;
            var rq = AsyncGPUReadback.Request(rt, 0, 0, 1, 0, 1, 0, 1, TextureFormat.RGBA32);
            rq.WaitForCompletion();
        }

        // ------------------------------------------------------------ no stain

        object Idle(int n)
        {
            var part = Part;
            if (part.Rigs != 0) throw new InvalidOperationException("this scene has a stain");
            for (int i = 0; i < 2000; i++) { part.Apply(); part.Tick(1f / 60f); part.LateApply(); }
            // Mono's allocation counter of a thread reads 0 in a player: the heap's use, against a control of one small array a frame
            long a0 = GC.GetTotalMemory(false);
            int g0 = GC.CollectionCount(0);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < n; i++) { part.Apply(); part.Tick(1f / 60f); part.LateApply(); }
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
                ["controlCollections"] = controlCollections, ["rigs"] = part.Rigs, ["subscribed"] = PlanarMirrors.Subscribed, ["planes"] = PlanarMirrors.Planes, ["cameras"] = PlanarMirrors.Cameras,
                ["targets"] = PlanarMirrors.Targets, ["root"] = part.Root != null,
            };
        }

        // ------------------------------------------------------------ the map

        object Surface(Vector3 p, int n)
        {
            var map = ctx.Unity().Map;
            var times = new List<double>();
            var sources = new Dictionary<string, int>(StringComparer.Ordinal);
            int hits = 0;
            float height = float.NaN;
            for (int i = 0; i < n; i++)
            {
                var q = p + new Vector3((i % 15 - 7) * .37f, 0f, (i / 15 % 15 - 7) * .37f);
                var sw = Stopwatch.StartNew();
                bool hit = map.Surface(q, out var s);
                times.Add(Ms(sw));
                if (!hit) continue;
                hits++;
                if (float.IsNaN(height)) height = s.Height;
                string src = s.Source ?? "null";
                sources.TryGetValue(src, out int k);
                sources[src] = k + 1;
            }
            return new Dictionary<string, object> { ["times"] = Row(times), ["hits"] = hits, ["sources"] = sources, ["height"] = float.IsNaN(height) ? (object)null : height, ["loaded"] = map.Loaded, ["roots"] = map.Roots?.Count ?? 0 };
        }

        // ------------------------------------------------------------ the pick

        object Pick(string key, int passes)
        {
            var tools = ctx.Unity().Tools ?? throw new InvalidOperationException("no tools: the UI does not run");
            var r = Rt.FindChara(key);
            var bounds = new Bounds();
            bool any = false;
            foreach (var x in r.Root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!x.enabled) continue;
                if (any) bounds.Encapsulate(x.bounds); else bounds = x.bounds;
                any = true;
            }
            var from = View.transform.position;
            var filter = new ToolPickFilter { NodeId = key, CharactersOnly = true };
            var all = new List<double>();
            var onBody = new List<double>();
            int hit = 0, precise = 0, firstOf = -1;
            double first = 0;
            for (int pass = 0; pass < passes; pass++)
                for (int iy = 0; iy < 13; iy++)
                    for (int ix = 0; ix < 6; ix++)
                    {
                        var to = new Vector3(bounds.center.x + (ix - 2.5f) * 1.1f, bounds.center.y + (iy - 6f) * 1.7f, bounds.center.z);
                        var ray = new Ray(from, (to - from).normalized);
                        var sw = Stopwatch.StartNew();
                        var pick = tools.Pick(ray, filter);
                        double t = Ms(sw);
                        if (firstOf < 0) { firstOf = 0; first = t; }
                        if (pass == 0) continue;   // the first pass meets everything for the first time
                        all.Add(t);
                        if (pick != null && pick.Hit) { hit++; onBody.Add(t); if (pick.Precise) precise++; }
                    }
            var centre = new Ray(from, (bounds.center - from).normalized);
            var plain = tools.Pick(centre);
            var named = tools.Pick(centre, filter);
            return new Dictionary<string, object>
            {
                ["picks"] = Row(all), ["onBody"] = Row(onBody), ["hits"] = hit, ["precise"] = precise, ["veryFirst"] = Round(first), ["bounds"] = bounds.ToString(), ["from"] = from.ToString(), ["any"] = any,
                ["centreHit"] = plain.Hit, ["centreNode"] = plain.NodeId, ["centrePart"] = plain.Part, ["centreFiltered"] = named.Hit, ["key"] = key,
            };
        }

        // ------------------------------------------------------------ a load

        object LoadMaps(string key, List<string> keys, int rounds)
        {
            var sync = typeof(WetPart).GetMethod("Sync", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException("WetPart.Sync");
            var times = new List<double>();
            var part = Part;
            foreach (var k in keys) part.CaptureNow(k);
            int decoded = 0, maps = 0;
            for (int i = 0; i < rounds + 1; i++)
            {
                part.Clear();
                var sw = Stopwatch.StartNew();
                sync.Invoke(part, new object[] { key });
                double t = Ms(sw);
                var rig = part.RigOf(key);
                decoded = rig?.Decoded ?? -1;
                maps = rig?.Runs.Sum(r => r.Live.Count) ?? -1;
                foreach (var k in keys) if (k != key) sync.Invoke(part, new object[] { k });
                if (i > 0) times.Add(t);
            }
            return new Dictionary<string, object> { ["load"] = Row(times), ["decoded"] = decoded, ["maps"] = maps };
        }

        // ------------------------------------------------------------ drops

        object Drops(int n)
        {
            var go = new GameObject("wetprobe drops") { hideFlags = HideFlags.HideAndDontSave };
            var d = new WetDrops(go.transform);
            try
            {
                var colour = new Color(.85f, .88f, .75f, .2f);
                for (int i = 0; i < WetDrops.MaxDrops; i++)
                    d.Emit(new Vector3((i % 40) * .2f, 40f + i / 40 * .2f, 0f), .031f, colour, .6f, 1, 1, i, 0);
                var step = new List<double>();
                var mesh = new List<double>();
                var both = new List<double>();
                int least = d.Drops;
                for (int i = 0; i < n; i++)
                {
                    var sw = Stopwatch.StartNew();
                    d.Step(-1e7f, (a, b, c) => { });
                    double s = Ms(sw);
                    sw.Restart();
                    d.Draw();
                    double m = Ms(sw);
                    least = Math.Min(least, d.Drops);
                    if (i < 5) continue;
                    step.Add(s); mesh.Add(m); both.Add(s + m);
                }
                return new Dictionary<string, object> { ["drops"] = d.Drops, ["least"] = least, ["splashes"] = d.Splashes, ["step"] = Row(step), ["mesh"] = Row(mesh), ["both"] = Row(both) };
            }
            finally
            {
                d.Dispose();
                Object.Destroy(go);
            }
        }

        // ------------------------------------------------------------ memory, mirrors, shaders

        static long Bytes(RenderTexture t)
        {
            long px = (long)t.width * t.height * Math.Max(1, t.volumeDepth);
            long per = 4;
            switch (t.graphicsFormat)
            {
                case UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm: per = 1; break;
                case UnityEngine.Experimental.Rendering.GraphicsFormat.R16_UNorm: per = 2; break;
                case UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat: per = 8; break;
                case UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32B32A32_SFloat: per = 16; break;
            }
            double all = px * per * (t.useMipMap ? 4.0 / 3.0 : 1.0);
            if (t.depth > 0) all += px * 4;
            return (long)all;
        }

        object RenderTextures(string key)
        {
            long bytes = 0;
            int count = 0;
            foreach (var t in Resources.FindObjectsOfTypeAll<RenderTexture>())
            {
                if (!t || !t.IsCreated()) continue;
                bytes += Bytes(t);
                count++;
            }
            var o = new Dictionary<string, object> { ["bytes"] = bytes, ["count"] = count };
            var rig = key != null ? Part.RigOf(key) : null;
            if (rig != null)
            {
                long composites = 0, masks = 0, maps = 0;
                foreach (var s in rig.Slots.Values)
                {
                    if (s.Compose?.Wet) composites += Bytes(s.Compose.Wet);
                    if (s.Compose?.Mask) masks += Bytes(s.Compose.Mask);
                }
                var data = typeof(WetRig).GetField("data", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(rig) as Dictionary<string, WetMapData>;
                if (data != null)
                    foreach (var d in data.Values) if (d.Map) maps += Bytes(d.Map);
                o["composites"] = composites; o["masks"] = masks; o["maps"] = maps; o["slots"] = rig.Slots.Count;
            }
            return o;
        }

        object Parts(string key)
        {
            var r = Rt.FindChara(key);
            var rig = Part.RigOf(key);
            var rows = new List<object>();
            foreach (var t in WetTargets.Collect(r, true, true))
            {
                var m = WetTargets.MeshOf(t.Renderer);
                rows.Add(new Dictionary<string, object>
                {
                    ["address"] = t.Address, ["slot"] = t.Slot, ["kind"] = t.Kind.ToString(), ["state"] = t.State.ToString(), ["shown"] = t.Shown, ["mesh"] = m ? m.name : null, ["readable"] = m && m.isReadable,
                    ["live"] = rig != null && rig.Slots.ContainsKey(t.Key), ["emitters"] = rig?.Runs.Sum(x => x.Emitters.Count) ?? 0,
                });
            }
            return new Dictionary<string, object> { ["rows"] = rows };
        }

        object Mirror() => new Dictionary<string, object>
        {
            ["subscribed"] = PlanarMirrors.Subscribed, ["planes"] = PlanarMirrors.Planes, ["cameras"] = PlanarMirrors.Cameras, ["targets"] = PlanarMirrors.Targets, ["renders"] = PlanarMirrors.Renders,
            ["skipped"] = PlanarMirrors.SkippedOverCap, ["failures"] = PlanarMirrors.Failures,
        };

        readonly Dictionary<string, int> cameraRenders = new Dictionary<string, int>(StringComparer.Ordinal);
        int countFrom, mirrorFrom;

        void OnCamera(ScriptableRenderContext context, Camera cam)
        {
            string n = cam.cameraType + " " + cam.name;
            cameraRenders.TryGetValue(n, out int k);
            cameraRenders[n] = k + 1;
        }

        object Count(bool start)
        {
            RenderPipelineManager.beginCameraRendering -= OnCamera;
            if (start)
            {
                cameraRenders.Clear();
                countFrom = Time.frameCount;
                mirrorFrom = PlanarMirrors.Renders;
                RenderPipelineManager.beginCameraRendering += OnCamera;
                return new Dictionary<string, object>();
            }
            return new Dictionary<string, object>
            {
                ["frames"] = Time.frameCount - countFrom, ["mirrorRenders"] = PlanarMirrors.Renders - mirrorFrom, ["cameras"] = new Dictionary<string, int>(cameraRenders),
                ["planes"] = PlanarMirrors.Planes, ["subscribed"] = PlanarMirrors.Subscribed,
            };
        }

        object Films(string key)
        {
            var rig = Part.RigOf(key) ?? throw new InvalidOperationException("no rig on " + key);
            int made = 0, drawn = 0, seen = 0;
            var families = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var s in rig.Slots.Values)
            {
                var f = s.Film;
                if (f == null) continue;
                made++;
                families.TryGetValue(f.Family.ToString(), out int k);
                families[f.Family.ToString()] = k + 1;
                var r = f.Renderer;
                if (!r || !r.enabled || r.forceRenderingOff || !r.gameObject.activeInHierarchy) continue;
                drawn++;
                if (r.isVisible) seen++;
            }
            return new Dictionary<string, object> { ["slots"] = rig.Slots.Count, ["made"] = made, ["drawn"] = drawn, ["seen"] = seen, ["families"] = families };
        }

        object Shaders()
        {
            var rows = new List<object>();
            foreach (var name in HiddenShaders)
            {
                var s = Shader.Find(name);
                rows.Add(new Dictionary<string, object> { ["name"] = name, ["found"] = s != null, ["supported"] = s != null && s.isSupported, ["passes"] = s != null ? s.passCount : 0 });
            }
            return new Dictionary<string, object> { ["rows"] = rows };
        }

        // ------------------------------------------------------------ renders in memory

        object Render(string tag, bool isAsync, int w, int h, int k, string aa, bool keep)
        {
            var shot = new Shot();
            shots[tag] = shot;
            var req = new CaptureRequest { Width = w, Height = h, Supersample = k, Aa = aa };
            if (isAsync)
            {
                CaptureService.RenderAsync(Rt, View, req, null, r => Finish(shot, r, keep));
                return new Dictionary<string, object> { ["async"] = true };
            }
            Finish(shot, CaptureService.RenderSync(Rt, View, req), keep);
            return Rendered(tag);
        }

        void Finish(Shot s, CaptureResult r, bool keep)
        {
            s.Result = r;
            s.Error = r.Error;
            s.Done = true;
            if (r.Ok && keep && r.Png != null)
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    if (ImageConversion.LoadImage(t, r.Png, false)) { s.W = t.width; s.H = t.height; s.Px = t.GetPixels32(); }
                }
                finally { Object.Destroy(t); }
            }
            r.Png = null;
        }

        object Rendered(string tag)
        {
            if (!shots.TryGetValue(tag, out var s)) throw new InvalidOperationException("no render " + tag);
            var o = new Dictionary<string, object> { ["done"] = s.Done, ["error"] = s.Error };
            if (s.Done && s.Result != null)
            {
                var r = s.Result;
                o["width"] = r.Width; o["height"] = r.Height; o["renderMs"] = Round(r.RenderMs); o["readbackMs"] = Round(r.ReadbackMs); o["encodeMs"] = Round(r.EncodeMs); o["mainThreadMs"] = Round(r.MainThreadMs);
                o["totalMs"] = Round(r.TotalMs); o["gfxBefore"] = r.GfxBefore; o["gfxAfterRender"] = r.GfxAfterRender;
            }
            return o;
        }

        const int GridColumns = 16, GridRows = 9;

        Color32[][] Pair(List<string> tags)
        {
            if (tags == null || tags.Count != 2) return null;
            return new[] { shots[tags[0]].Px, shots[tags[1]].Px };
        }

        static bool In(Color32[][] mask, int i) => mask != null && Delta(mask[0][i], mask[1][i]) >= 8;

        // a pixel equal to its right and its upper neighbour
        static bool Flat(Color32[] px, int w, int h, int x, int y)
        {
            var c = px[y * w + x];
            if (x + 1 < w && Delta(c, px[y * w + x + 1]) != 0) return false;
            if (y + 1 < h && Delta(c, px[(y + 1) * w + x]) != 0) return false;
            return true;
        }

        object Compare(string a, string b, int threshold, string within, string withinOf, List<string> character, List<string> mirror, List<string> member)
        {
            var x = shots[a];
            var y = shots[b];
            if (x.Px == null || y.Px == null || x.Px.Length != y.Px.Length) throw new InvalidOperationException("the renders cannot be compared");
            Color32[] m = within != null ? shots[within].Px : null, mo = withinOf != null ? shots[withinOf].Px : null;
            Color32[][] onC = Pair(character), onM = Pair(mirror), onB = Pair(member);
            int w = x.W, h = x.H;
            long differ = 0, region = 0, any = 0;
            int most = 0;
            double sum = 0;
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            var grid = new int[GridColumns * GridRows];
            long cCount = 0, mCount = 0, bothCount = 0, bCount = 0, outside = 0, flatA = 0, flatB = 0, memberFlatA = 0, memberFlatB = 0;
            for (int i = 0; i < x.Px.Length; i++)
            {
                if (m != null && mo != null && Delta(m[i], mo[i]) < 8) continue;
                region++;
                int d = Delta(x.Px[i], y.Px[i]);
                sum += d;
                if (d > most) most = d;
                if (d > 0) any++;
                if (d <= threshold) continue;
                differ++;
                int px = i % w, py = i / w;
                if (px < x0) x0 = px;
                if (py < y0) y0 = py;
                if (px > x1) x1 = px;
                if (py > y1) y1 = py;
                grid[Math.Min(GridRows - 1, py * GridRows / h) * GridColumns + Math.Min(GridColumns - 1, px * GridColumns / w)]++;
                bool c = In(onC, i), r = In(onM, i), e = In(onB, i);
                if (c) cCount++;
                if (r) mCount++;
                if (c && r) bothCount++;
                if (e) bCount++;
                if (!c && !r && !e) outside++;
                bool fa = Flat(x.Px, w, h, px, py), fb = Flat(y.Px, w, h, px, py);
                if (fa) flatA++;
                if (fb) flatB++;
                if (e && fa) memberFlatA++;
                if (e && fb) memberFlatB++;
            }
            return new Dictionary<string, object>
            {
                ["pixels"] = region, ["differ"] = differ, ["most"] = most, ["mean"] = Round(region > 0 ? sum / region : 0), ["any"] = any,
                ["box"] = differ > 0 ? new[] { x0, y0, x1, y1 } : null, ["grid"] = grid, ["gridColumns"] = GridColumns, ["gridRows"] = GridRows, ["width"] = w, ["height"] = h,
                ["onCharacter"] = cCount, ["onMirror"] = mCount, ["onCharacterInMirror"] = bothCount, ["onMember"] = bCount, ["outside"] = outside,
                ["flatA"] = flatA, ["flatB"] = flatB, ["memberFlatA"] = memberFlatA, ["memberFlatB"] = memberFlatB,
            };
        }

        object Flat(string tag, int x0, int y0, int x1, int y1)
        {
            var s = shots[tag];
            if (s.Px == null) throw new InvalidOperationException("no pixels of " + tag);
            x0 = Math.Max(0, x0); y0 = Math.Max(0, y0); x1 = Math.Min(s.W - 1, x1); y1 = Math.Min(s.H - 1, y1);
            long pixels = 0, right = 0, up = 0, rightOf = 0, upOf = 0;
            var colours = new HashSet<int>();
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    pixels++;
                    var c = s.Px[y * s.W + x];
                    colours.Add(c.r | c.g << 8 | c.b << 16);
                    if (x < x1) { rightOf++; if (Delta(c, s.Px[y * s.W + x + 1]) == 0) right++; }
                    if (y < y1) { upOf++; if (Delta(c, s.Px[(y + 1) * s.W + x]) == 0) up++; }
                }
            return new Dictionary<string, object>
            {
                ["pixels"] = pixels, ["shareRight"] = Round(rightOf > 0 ? right / (double)rightOf : 0), ["shareUp"] = Round(upOf > 0 ? up / (double)upOf : 0), ["colours"] = colours.Count,
            };
        }

        object Members(string key, int w, int h, float plane, bool? hide)
        {
            var r = Rt.FindChara(key);
            var st = MosaicSet.StateOf(r.Context);
            var cam = View;
            var vp = Matrix4x4.Perspective(cam.fieldOfView, w / (float)h, cam.nearClipPlane, cam.farClipPlane) * cam.worldToCameraMatrix;
            var rows = new List<object>();
            if (st != null)
                foreach (var m in st.Members)
                {
                    var rd = m.Renderer;
                    if (!rd) continue;
                    if (hide.HasValue) rd.forceRenderingOff = hide.Value;
                    var row = new Dictionary<string, object>
                    {
                        ["name"] = rd.name, ["blockPx"] = Round(m.BlockPx), ["direct"] = Project(vp, rd.bounds, w, h, float.NaN), ["mirror"] = Project(vp, rd.bounds, w, h, plane), ["hidden"] = rd.forceRenderingOff,
                    };
                    if (m.Region != null)
                    {
                        var at = MosaicCamera.RegionMatrix(m);
                        var c = at.MultiplyPoint3x4(m.Region.Centroid);
                        var n = at.MultiplyVector(m.Region.Normal).normalized;
                        row["centroid"] = new[] { c.x, c.y, c.z };
                        row["normal"] = new[] { n.x, n.y, n.z };
                        row["mirrorCentroid"] = Project(vp, new Bounds(c, Vector3.zero), w, h, plane);
                    }
                    rows.Add(row);
                }
            return new Dictionary<string, object> { ["rows"] = rows };
        }

        Camera mirrorProbe;

        // the camera PlanarMirrors makes for a capture of w x h, from the view's pose, and what the mosaic's step computes for it; nothing is rendered
        object MirrorFrame(int w, int h, float plane, float scale, int k)
        {
            var view = View;
            if (!mirrorProbe)
            {
                var go = new GameObject("wetprobe mirror frame") { hideFlags = HideFlags.HideAndDontSave };
                mirrorProbe = go.AddComponent<Camera>();
                mirrorProbe.enabled = false;
            }
            var rc = mirrorProbe;
            rc.CopyFrom(view);
            rc.enabled = false;
            int tw = Math.Max(64, (int)(w * k * scale)), th = Math.Max(64, (int)(h * k * scale));
            var target = RenderTexture.GetTemporary(tw, th, 24);
            try
            {
                rc.targetTexture = target;
                rc.rect = new Rect(0f, 0f, 1f, 1f);
                rc.ResetProjectionMatrix();
                rc.aspect = w / (float)h;
                var persp = rc.projectionMatrix;
                var r = Matrix4x4.identity;
                r.m11 = -1f; r.m13 = 2f * plane;
                var viewMatrix = view.worldToCameraMatrix;
                rc.worldToCameraMatrix = viewMatrix * r;
                var vt = view.transform;
                rc.transform.SetPositionAndRotation(r.MultiplyPoint(vt.position), Quaternion.LookRotation(r.MultiplyVector(vt.forward), r.MultiplyVector(vt.up)));
                var w2c = rc.worldToCameraMatrix;
                var cpos = w2c.MultiplyPoint(new Vector3(0f, plane, 0f));
                var cn = w2c.MultiplyVector(Vector3.up).normalized;
                rc.projectionMatrix = persp;
                rc.projectionMatrix = rc.CalculateObliqueMatrix(new Vector4(cn.x, cn.y, cn.z, -Vector3.Dot(cpos, cn)));
                int outK = Math.Max(1, Mathf.RoundToInt(k * scale));
                MosaicCamera.Enter(rc, outK);
                var rows = new List<object>();
                var groups = new List<object>();
                float floor = 0f;
                try
                {
                    var f = MosaicCamera.FrameOf(rc);
                    floor = f.FloorPx;
                    foreach (var g in f.Groups)
                        foreach (var m in g.Members)
                            rows.Add(new Dictionary<string, object> { ["name"] = m.Renderer ? m.Renderer.name : "?", ["blockPx"] = Round(m.BlockPx), ["blockUv"] = m.BlockUv, ["mode"] = m.Mode.ToString(), ["cellPx"] = Round(m.CellPx) });
                    foreach (var g in HS2Next.MosaicWideFeature.Groups(f, rc, tw, th))
                        groups.Add(new Dictionary<string, object> { ["block"] = Round(g.Block), ["anchor"] = new[] { Round(g.Anchor.x), Round(g.Anchor.y) }, ["diameter"] = Round(g.Diameter) });
                }
                finally { MosaicCamera.Leave(rc); }
                return new Dictionary<string, object> { ["target"] = new[] { tw, th }, ["floorPx"] = Round(floor), ["outputScale"] = outK, ["gain"] = Round(1.0 / (scale * k)), ["members"] = rows, ["groups"] = groups };
            }
            finally
            {
                rc.targetTexture = null;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        static bool[] MaskOf(Color32[] a, Color32[] b, int threshold)
        {
            var m = new bool[a.Length];
            for (int i = 0; i < a.Length; i++) m[i] = Delta(a[i], b[i]) > threshold;
            return m;
        }

        static bool[] Grow(bool[] m, int w, int h, int by, bool shrink)
        {
            var cur = m;
            for (int s = 0; s < by; s++)
            {
                var n = new bool[cur.Length];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        bool c = cur[i], l = x > 0 && cur[i - 1], r = x + 1 < w && cur[i + 1], d = y > 0 && cur[i - w], u = y + 1 < h && cur[i + w];
                        n[i] = shrink ? c && l && r && d && u : c || l || r || d || u;
                    }
                cur = n;
            }
            return cur;
        }

        // complete runs of one colour inside a region, rows and columns: both neighbours inside and of another colour (MosaicTest's rule)
        static List<int> Runs(Color32[] px, bool[] mask, int w, int h)
        {
            var l = new List<int>();
            void Line(int count, Func<int, int> at)
            {
                int i = 0;
                while (i < count)
                {
                    if (!mask[at(i)]) { i++; continue; }
                    int j = i;
                    while (j + 1 < count && mask[at(j + 1)] && Delta(px[at(j + 1)], px[at(i)]) == 0) j++;
                    bool left = i > 0 && mask[at(i - 1)], right = j + 1 < count && mask[at(j + 1)];
                    if (left && right) l.Add(j - i + 1);
                    i = j + 1;
                }
            }
            for (int y = 0; y < h; y++) Line(w, x => y * w + x);
            for (int x = 0; x < w; x++) Line(h, y => y * w + x);
            return l;
        }

        static Dictionary<string, object> Read(Color32[] px, bool[] mask, int w, int h, float block, float ax, float ay, int band)
        {
            long n = 0, pairs = 0, eq = 0, eq1 = 0, eq2 = 0;
            var colours = new HashSet<int>();
            var cells = new Dictionary<long, int[]>();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (!mask[i]) continue;
                    n++;
                    var c = px[i];
                    colours.Add(c.r | c.g << 8 | c.b << 16);
                    if (x + 1 < w && mask[i + 1]) { int d = Delta(c, px[i + 1]); pairs++; if (d == 0) eq++; if (d <= 1) eq1++; if (d <= 2) eq2++; }
                    if (y + 1 < h && mask[i + w]) { int d = Delta(c, px[i + w]); pairs++; if (d == 0) eq++; if (d <= 1) eq1++; if (d <= 2) eq2++; }
                    if (block > 0f)
                    {
                        float fx = (x + .5f - ax) / block, fy = (y + .5f - ay) / block;
                        long kx = (long)Math.Floor(fx), ky = (long)Math.Floor(fy);
                        float ex = (fx - kx) * block, ey = (fy - ky) * block;
                        if (ex < band || ey < band || block - ex < band || block - ey < band) continue;
                        long key = kx * 100000 + ky;
                        if (!cells.TryGetValue(key, out var s)) cells[key] = s = new[] { 0, 255, 0, 255, 0, 255, 0 };
                        s[0] = Math.Max(s[0], c.r); s[1] = Math.Min(s[1], c.r); s[2] = Math.Max(s[2], c.g); s[3] = Math.Min(s[3], c.g); s[4] = Math.Max(s[4], c.b); s[5] = Math.Min(s[5], c.b); s[6]++;
                    }
                }
            var runs = Runs(px, mask, w, h).Where(r => r > band).OrderBy(r => r).ToList();
            var spread = cells.Values.Where(s => s[6] >= 8).Select(s => Math.Max(s[0] - s[1], Math.Max(s[2] - s[3], s[4] - s[5]))).OrderBy(s => s).ToList();
            return new Dictionary<string, object>
            {
                ["px"] = n, ["pairs"] = pairs, ["equal"] = Round(pairs > 0 ? eq / (double)pairs : 0), ["within1"] = Round(pairs > 0 ? eq1 / (double)pairs : 0), ["within2"] = Round(pairs > 0 ? eq2 / (double)pairs : 0), ["colours"] = colours.Count,
                ["runs"] = runs.Count, ["runMedian"] = runs.Count > 0 ? runs[runs.Count / 2] : 0, ["runP25"] = runs.Count > 0 ? runs[runs.Count / 4] : 0, ["runMax"] = runs.Count > 0 ? runs[runs.Count - 1] : 0,
                ["cells"] = spread.Count, ["spreadMedian"] = spread.Count > 0 ? spread[spread.Count / 2] : -1, ["spreadMax"] = spread.Count > 0 ? spread[spread.Count - 1] : -1,
            };
        }

        object Region(string tag, List<string> maskTags, int threshold, int erode, List<string> character, int ringFrom, int ringTo, float block, float ax, float ay, int band)
        {
            var s = shots[tag];
            var m = shots[maskTags[0]];
            var o = shots[maskTags[1]];
            int w = s.W, h = s.H;
            var full = MaskOf(m.Px, o.Px, threshold);
            var mask = Grow(full, w, h, erode, true);
            var outer = Grow(full, w, h, ringTo, false);
            var inner = Grow(full, w, h, ringFrom, false);
            var on = character.Count == 2 ? MaskOf(shots[character[0]].Px, shots[character[1]].Px, 8) : null;
            var ring = new bool[full.Length];
            int fullPx = 0, x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (int i = 0; i < ring.Length; i++)
            {
                ring[i] = outer[i] && !inner[i] && (on == null || on[i]);
                if (!full[i]) continue;
                fullPx++;
                int x = i % w, y = i / w;
                if (x < x0) x0 = x;
                if (y < y0) y0 = y;
                if (x > x1) x1 = x;
                if (y > y1) y1 = y;
            }
            return new Dictionary<string, object> { ["maskPx"] = fullPx, ["box"] = fullPx > 0 ? new[] { x0, y0, x1, y1 } : null, ["mask"] = Read(s.Px, mask, w, h, block, ax, ay, band), ["ring"] = Read(s.Px, ring, w, h, block, ax, ay, band) };
        }

        object ProjectPoints(List<List<float>> points, int w, int h, float plane)
        {
            var cam = View;
            var vp = Matrix4x4.Perspective(cam.fieldOfView, w / (float)h, cam.nearClipPlane, cam.farClipPlane) * cam.worldToCameraMatrix;
            var rows = new List<object>();
            foreach (var p in points)
            {
                var b = new Bounds(new Vector3(p[0], p[1], p[2]), Vector3.zero);
                rows.Add(new Dictionary<string, object> { ["direct"] = Project(vp, b, w, h, float.NaN), ["mirror"] = Project(vp, b, w, h, plane) });
            }
            return new Dictionary<string, object> { ["rows"] = rows };
        }

        // a box's corners in an image, y from the bottom as a kept render's rows; with a plane, their reflections in it
        static int[] Project(Matrix4x4 vp, Bounds b, int w, int h, float plane)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var p = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                if (!float.IsNaN(plane)) p.y = 2f * plane - p.y;
                var c = vp * new Vector4(p.x, p.y, p.z, 1f);
                if (c.w <= 0f) return null;
                float x = (c.x / c.w * .5f + .5f) * w, y = (c.y / c.w * .5f + .5f) * h;
                x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
            }
            return new[] { (int)x0, (int)y0, (int)x1, (int)y1 };
        }

        static int Delta(Color32 p, Color32 q) => Math.Max(Math.Abs(p.r - q.r), Math.Max(Math.Abs(p.g - q.g), Math.Abs(p.b - q.b)));

        // ------------------------------------------------------------ a puddle

        object Puddle(string key, float x, float y, float z, float area, bool clear)
        {
            var part = Part;
            part.CaptureNow(key);
            var block = State(key).Wet.Clone();
            float height = float.IsNaN(y) ? part.RigOf(key).Floor.Y : y;
            if (clear) block.Puddles.Clear();
            if (area > 0f) block.Puddles.Add(new WetPuddleEntry { Stain = block.Current, Center = new Vec3(x, height, z), Area = area, Seed = 7f });
            var res = Rt.Session.Commands.Execute(new WetEditCommand(key, block, WetEditCommand.LabelEdit), ExecuteOptions.NotRecorded);
            return new Dictionary<string, object> { ["ok"] = res.Ok, ["y"] = height };
        }
    }
}

// The test plugin of the row BakeCheck.player (CC0-1.0). A player has no command that renders a node alone, times a capture apart from its encoding,
// reads the managed heap during a capture, times a cut's procedure apart from the command around it, or lists the meshes an item cannot read, so this
// plugin does it on the player's main thread. Renders stay in memory and are judged by numbers; nothing is written as a picture. It changes nothing a
// scene saves (bakeprobe.time runs the command it is given).
//   bakeprobe.frames     the frame cap: vsync count and target frame rate
//   bakeprobe.item       an item as built: its source, fallback, prototype (main, load and build times and the load's stages, video and mesh bytes, the shaders its materials name)
//   bakeprobe.find       Shader.Find of names: found, supported
//   bakeprobe.look       a node against another at one view, each alone: footprint, the first rendered twice, the pixels over 2 and over 8 levels, by renderer
//   bakeprobe.shot       a node alone at one view: the SHA-256 of its pixels, its footprint
//   bakeprobe.capture    a character's bake captured on the main thread (its time and stages, the managed heap's and the private bytes' peaks during it), its encoding then on a worker
//   bakeprobe.legacy     the app's internal test switch of the old capture and load path (the identity check): read, or set on or off
//   bakeprobe.blobs      an item's bake: the main with its id and created set to fixed values, every data blob by entry (the identity check)
//   bakeprobe.cachems    the scene runtime's bake times by stage (BakeCache.Ms), read or cleared
//   bakeprobe.heapcontrol  the meters' control: a known managed allocation read by the same meters
//   bakeprobe.readres    an item's bake entries read from a saved scene file (BW4's read)
//   bakeprobe.plain      the view with an item's members drawn flat, the mosaic left out (BV6's positive control)
//   bakeprobe.encoded    how that encoding stands (once done: the bake's identity as bakeprobe.blobs gives it); bakeprobe.release frees the job (the private bytes after it);
//                        bakeprobe.unload one Resources.UnloadUnusedAssets + GC (the private bytes after it)
// The plugin builds against the player of 8d8e5516 too (the kept identity reference): the switch is read by reflection and absent there.
//   bakeprobe.cut        the procedure of pieces run again from their source: its time, the same bits, and (where the build has them) the source's meshes' decode
//                        and the pieces' encoding by round
//   bakeprobe.rows       IItemRows: a group's rows appended and replaced
//   bakeprobe.appgroup   what the app's item group costs: its store rows listed again, a group of its shape registered
//   bakeprobe.members    an item's mosaic members: names, mode, block, region; can leave them undrawn for a render kept in memory
//   bakeprobe.render, .rendered, .compare, .flat, .forget   the view through the capture service (sync, async) or the scene thumbnail, kept in memory
//   bakeprobe.time       a command and the apply after it, timed on the main thread
//   bakeprobe.meshes     an item's meshes: readable or not, vertices, bounds
//   bakeprobe.sample     Step 0's sample of item rows: which hold meshes a player cannot read
//   bakeprobe.verts      an item's drawn vertices kept, then the largest move against them; its bones' local angles
//   bakeprobe.parts      the scene runtime's parts by type
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using HS2Next;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using HS2Next.Api.Unity;
using HS2Next.App;
using HS2Next.Chara.Mosaic;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes.Bake;
using HS2Next.Scenes.Cut;
using HS2Next.Scenes.Runtime;
using HS2Next.Screenshots;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Stp3.Test.BakeProbe
{
    public sealed class BakeProbePlugin : IPlugin
    {
        const int RowsGroup = 820099;

        sealed class Shot { public int W, H; public Color32[] Px; public CaptureResult Result; public bool Done; public string Error; }

        sealed class View { public Vector3 Pivot, Rot; public float Dist = 25f, Fov = 23f; public int W = 1280, H = 720; }

        IPluginContext ctx;
        readonly Dictionary<string, Shot> shots = new Dictionary<string, Shot>(StringComparer.Ordinal);
        readonly Dictionary<string, Vector3[]> kept = new Dictionary<string, Vector3[]>(StringComparer.Ordinal);
        BakeJob job;
        Task encode;
        Stopwatch encodeClock;
        double encodeMs = -1;

        static SceneRuntime Rt => AppCore.Current.Runtime;
        static Camera ViewCam => Rt.GetPart<SceneViewCamera>().Camera;
        static double R(double v) => Math.Round(v, 4);

        public void Load(IPluginContext context)
        {
            ctx = context;
            var c = context.Commands;
            c.Register(new CommandSpec("bakeprobe.frames", "The frame cap: vsync count and target frame rate.").Arg("vsync", ArgTypes.Int, "QualitySettings.vSyncCount").Arg("fps", ArgTypes.Int, "Application.targetFrameRate"), a =>
            {
                if (a.Args.Has("vsync")) QualitySettings.vSyncCount = a.Args.Get("vsync", 0);
                if (a.Args.Has("fps")) Application.targetFrameRate = a.Args.Get("fps", -1);
                return new Dictionary<string, object> { ["vsync"] = QualitySettings.vSyncCount, ["fps"] = Application.targetFrameRate, ["screen"] = Screen.width + "x" + Screen.height, ["device"] = SystemInfo.graphicsDeviceType.ToString() };
            });
            c.Register(new CommandSpec("bakeprobe.item", "An item as built.").Arg("key", ArgTypes.String, "an item", required: true).WithFlags(CommandFlags.ReadOnly), a => Item(a.Args.Require<string>("key")));
            c.Register(new CommandSpec("bakeprobe.find", "Shader.Find of names.").Arg("names", ArgTypes.ArrayOf(ArgTypes.String), "shader names", required: true).WithFlags(CommandFlags.ReadOnly),
                a => Find(a.Args.Get("names", new List<string>())));
            c.Register(new CommandSpec("bakeprobe.look", "A node against another at one view, each alone, no AA.").WithTimeout(600).Arg("a", ArgTypes.String, "the source node", required: true).Arg("b", ArgTypes.String, "the other node", required: true)
                .Arg("view", ArgTypes.String, "full, face or auto (of a), or absent with pivot, rot, dist").Arg("pivot", ArgTypes.ArrayOf(ArgTypes.Float), "the pivot").Arg("rot", ArgTypes.ArrayOf(ArgTypes.Float), "Euler degrees")
                .Arg("dist", ArgTypes.Float, "distance").Arg("fov", ArgTypes.Float, "field of view").Arg("width", ArgTypes.Int, "width").Arg("height", ArgTypes.Int, "height").Arg("name", ArgTypes.Bool, "name the differing pixels by renderer"),
                a => Look(a.Args.Require<string>("a"), a.Args.Require<string>("b"), ViewOf(a.Args, a.Args.Require<string>("a")), a.Args.Get("name", true)));
            c.Register(new CommandSpec("bakeprobe.shot", "A node alone at one view, no AA: the SHA-256 of its pixels.").WithTimeout(600).Arg("key", ArgTypes.String, "a node", required: true)
                .Arg("view", ArgTypes.String, "full, face or auto, or absent with pivot, rot, dist").Arg("pivot", ArgTypes.ArrayOf(ArgTypes.Float), "the pivot").Arg("rot", ArgTypes.ArrayOf(ArgTypes.Float), "Euler degrees")
                .Arg("dist", ArgTypes.Float, "distance").Arg("fov", ArgTypes.Float, "field of view").Arg("width", ArgTypes.Int, "width").Arg("height", ArgTypes.Int, "height"),
                a => ShotOf(a.Args.Require<string>("key"), ViewOf(a.Args, a.Args.Require<string>("key"))));
            c.Register(new CommandSpec("bakeprobe.capture", "Captures a character's bake on the main thread, then encodes it on a worker.").WithTimeout(600).Arg("key", ArgTypes.String, "a character", required: true)
                .Arg("parts", ArgTypes.ArrayOf(ArgTypes.String), "part keys; empty: the whole character").Arg("insides", ArgTypes.Bool, "inside faces").Arg("centre", ArgTypes.Bool, "origin at the centre"),
                a => Capture(a.Args.Require<string>("key"), a.Args.Get("parts", new List<string>()), a.Args.Get("insides", true), a.Args.Get("centre", true)));
            c.Register(new CommandSpec("bakeprobe.legacy", "The app's internal test switch of the old capture and load path: read, or set.").Arg("on", ArgTypes.Bool, "the old path on or off"),
                a => Legacy(a.Args.Has("on") ? a.Args.Get("on", false) : (bool?)null));
            c.Register(new CommandSpec("bakeprobe.blobs", "An item's bake: its main with id and created set to fixed values, every data blob by entry.").Arg("key", ArgTypes.String, "an item drawn from a bake", required: true)
                .WithFlags(CommandFlags.ReadOnly), a => Blobs(a.Args.Require<string>("key")));
            c.Register(new CommandSpec("bakeprobe.cachems", "The scene runtime's bake times by stage, read or cleared.").Arg("clear", ArgTypes.Bool, "clear them after reading"),
                a => CacheMs(a.Args.Get("clear", false)));
            c.Register(new CommandSpec("bakeprobe.heapcontrol", "The heap meters' control: a known managed allocation held while the meters sample, as during a capture.").WithTimeout(600).Arg("mb", ArgTypes.Int, "MiB allocated"),
                a => HeapControl(a.Args.Get("mb", 256)));
            c.Register(new CommandSpec("bakeprobe.readres", "Reads an item's bake entries from a saved scene file, as a load reads them: the time and the bytes.").WithTimeout(600).Arg("key", ArgTypes.String, "an item drawn from a bake", required: true)
                .Arg("path", ArgTypes.String, "the scene file", required: true).WithFlags(CommandFlags.ReadOnly), a => ReadRes(a.Args.Require<string>("key"), a.Args.Require<string>("path")));
            c.Register(new CommandSpec("bakeprobe.plain", "Renders the view in memory with an item's members drawn in a flat colour and the wide pass off (the mosaic left out): the positive control.").WithTimeout(600)
                .Arg("key", ArgTypes.String, "an item", required: true).Arg("tag", ArgTypes.String, "a name", required: true).Arg("width", ArgTypes.Int, "width").Arg("height", ArgTypes.Int, "height"),
                a => Plain(a.Args.Require<string>("key"), a.Args.Require<string>("tag"), a.Args.Get("width", 1280), a.Args.Get("height", 720)));
            c.Register(new CommandSpec("bakeprobe.encoded", "How the encoding of the last capture stands.").WithFlags(CommandFlags.ReadOnly), _ => Encoded());
            c.Register(new CommandSpec("bakeprobe.release", "Frees the last capture: the private bytes after its job is disposed."), _ => { Release(); return new Dictionary<string, object> { ["private"] = PrivateBytes() }; });
            c.Register(new CommandSpec("bakeprobe.unload", "Starts one Resources.UnloadUnusedAssets (start), or reads whether it ended: then a GC and the private bytes.").Arg("start", ArgTypes.Bool, "start it"),
                a => Unload(a.Args.Get("start", false)));
            c.Register(new CommandSpec("bakeprobe.cut", "Runs the procedure of pieces again from their source.").WithTimeout(600).Arg("keys", ArgTypes.ArrayOf(ArgTypes.String), "the pieces of one cut", required: true)
                .Arg("rounds", ArgTypes.Int, "rounds"), a => Cut(a.Args.Get("keys", new List<string>()), a.Args.Get("rounds", 5)));
            c.Register(new CommandSpec("bakeprobe.rows", "A group's rows appended and replaced through IItemRows.").WithTimeout(600).Arg("n", ArgTypes.Int, "rows").Arg("rounds", ArgTypes.Int, "rounds"),
                a => Rows(a.Args.Get("n", 1000), a.Args.Get("rounds", 7)));
            c.Register(new CommandSpec("bakeprobe.appgroup", "What the app's item group costs.").WithTimeout(600).Arg("rounds", ArgTypes.Int, "rounds"), a => AppGroup(a.Args.Get("rounds", 7)));
            c.Register(new CommandSpec("bakeprobe.members", "An item's mosaic members.").Arg("key", ArgTypes.String, "an item", required: true).Arg("hide", ArgTypes.Bool, "leave them undrawn, or draw them again")
                .Arg("width", ArgTypes.Int, "an output width (the floor F)").Arg("height", ArgTypes.Int, "an output height"),
                a => Members(a.Args.Require<string>("key"), a.Args.Has("hide") ? a.Args.Get("hide", false) : (bool?)null, a.Args.Get("width", 1280), a.Args.Get("height", 720)));
            c.Register(new CommandSpec("bakeprobe.render", "Renders the view in memory: sync (a capture), async (a screenshot, a recorded frame) or thumb (the scene thumbnail).").WithTimeout(600).Arg("tag", ArgTypes.String, "a name", required: true)
                .Arg("path", ArgTypes.Enum("sync", "async", "thumb"), "the path").Arg("width", ArgTypes.Int, "width").Arg("height", ArgTypes.Int, "height").Arg("aa", ArgTypes.String, "smaa, msaa4 or none"),
                a => Render(a.Args.Require<string>("tag"), a.Args.Get("path", "sync"), a.Args.Get("width", 1280), a.Args.Get("height", 720), a.Args.Get("aa", CaptureService.Smaa)));
            c.Register(new CommandSpec("bakeprobe.rendered", "How a render stands.").Arg("tag", ArgTypes.String, "a name", required: true).WithFlags(CommandFlags.ReadOnly), a => Rendered(a.Args.Require<string>("tag")));
            c.Register(new CommandSpec("bakeprobe.compare", "Two kept renders: numbers only.").Arg("a", ArgTypes.String, "a render", required: true).Arg("b", ArgTypes.String, "a render", required: true)
                .Arg("threshold", ArgTypes.Int, "a channel's difference that counts").Arg("within", ArgTypes.String, "a render compared with withinOf to say where to look").Arg("withinOf", ArgTypes.String, "the render it is compared with").WithFlags(CommandFlags.ReadOnly),
                a => Compare(a.Args.Require<string>("a"), a.Args.Require<string>("b"), a.Args.Get("threshold", 2), a.Args.Get("within", (string)null), a.Args.Get("withinOf", (string)null)));
            c.Register(new CommandSpec("bakeprobe.flat", "Inside a mask of two renders: the share of pixels equal to their right and upper neighbour in a third.").Arg("tag", ArgTypes.String, "the render read", required: true)
                .Arg("mask", ArgTypes.ArrayOf(ArgTypes.String), "two renders: where they differ over 8 levels", required: true).WithFlags(CommandFlags.ReadOnly),
                a => Flat(a.Args.Require<string>("tag"), a.Args.Get("mask", new List<string>())));
            c.Register(new CommandSpec("bakeprobe.forget", "Forgets every render kept."), _ => { shots.Clear(); kept.Clear(); return new Dictionary<string, object>(); });
            c.Register(new CommandSpec("bakeprobe.time", "A command and the apply after it, timed on the main thread.").WithTimeout(600).Arg("command", ArgTypes.String, "the command", required: true)
                .Arg("args", ArgTypes.Object, "its arguments").Arg("apply", ArgTypes.Bool, "apply the scene after it").Arg("rounds", ArgTypes.Int, "rounds"),
                a => Time(a.Args.Require<string>("command"), a.Args.Get("args", new Dictionary<string, object>()), a.Args.Get("apply", true), a.Args.Get("rounds", 1)));
            c.Register(new CommandSpec("bakeprobe.duplicate", "Duplicates a node and applies the scene, timed on the main thread.").WithTimeout(600).Arg("key", ArgTypes.String, "a node", required: true),
                a => Duplicate(a.Args.Require<string>("key")));
            c.Register(new CommandSpec("bakeprobe.meshes", "An item's meshes.").Arg("key", ArgTypes.String, "an item", required: true).WithFlags(CommandFlags.ReadOnly), a => Meshes(a.Args.Require<string>("key")));
            c.Register(new CommandSpec("bakeprobe.sample", "Step 0's sample of item rows: which hold meshes a player cannot read.").WithTimeout(600).Arg("n", ArgTypes.Int, "rows").Arg("floorGb", ArgTypes.Float, "stop under this free memory"),
                a => Sample(a.Args.Get("n", 150), a.Args.Get("floorGb", 9f)));
            c.Register(new CommandSpec("bakeprobe.verts", "An item's drawn vertices: kept, or the largest move against the kept ones; its bones' local angles.").Arg("key", ArgTypes.String, "an item", required: true)
                .Arg("keep", ArgTypes.Bool, "keep them"), a => Verts(a.Args.Require<string>("key"), a.Args.Get("keep", false)));
            c.Register(new CommandSpec("bakeprobe.parts", "The scene runtime's parts by type.").WithFlags(CommandFlags.ReadOnly), _ => Parts());
        }

        public void Unload()
        {
            Release();
            if (LegacyField != null) LegacyField.SetValue(null, false);
        }

        // ------------------------------------------------------------ nodes and their renderers

        static IEnumerable<Renderer> RenderersOf(NodeRealization r)
        {
            if (r is ItemRealization it && it.Instance) return it.Instance.GetComponentsInChildren<Renderer>(true);
            if (r is CharaRealization c && c.Root) return c.Root.GetComponentsInChildren<Renderer>(true);
            return Enumerable.Empty<Renderer>();
        }

        static NodeRealization Node(string key) => (NodeRealization)Rt.FindItem(key) ?? Rt.FindChara(key) ?? throw new InvalidOperationException("no item or character " + key);

        static List<Renderer> Drawn(string key) => RenderersOf(Node(key)).Where(r => r && r.enabled && r.gameObject.activeInHierarchy).ToList();

        static List<Renderer> Everything() => Rt.Realizations.SelectMany(RenderersOf).Where(r => r).Distinct().ToList();

        /// <summary>Shows only <paramref name="show"/> among every renderer of the scene (forceRenderingOff), every value restored by Dispose.</summary>
        sealed class Only : IDisposable
        {
            readonly List<(Renderer r, bool off)> saved = new List<(Renderer, bool)>();
            public Only(IEnumerable<Renderer> all, ICollection<Renderer> show)
            {
                foreach (var r in all) { saved.Add((r, r.forceRenderingOff)); r.forceRenderingOff = !show.Contains(r); }
            }
            public void Dispose() { foreach (var (r, off) in saved) if (r) r.forceRenderingOff = off; }
        }

        static Bounds BoundsOf(IEnumerable<Renderer> rs)
        {
            Bounds? b = null;
            foreach (var r in rs) { if (b == null) b = r.bounds; else { var x = b.Value; x.Encapsulate(r.bounds); b = x; } }
            return b ?? new Bounds(Vector3.zero, Vector3.zero);
        }

        static Transform RootOf(NodeRealization r) => r is CharaRealization c && c.Root ? c.Root.transform : r is ItemRealization it && it.Instance ? it.Instance.transform : null;

        static View ViewOf(ICommandArgs a, string key)
        {
            var v = new View { W = a.Get("width", 1280), H = a.Get("height", 720), Fov = a.Get("fov", 23f) };
            string name = a.Get("view", (string)null);
            var node = Node(key);
            var root = RootOf(node);
            if (name == "full")
            {
                v.Pivot = root.position + root.rotation * new Vector3(0f, 10f, 0f);
                v.Rot = new Vector3(15f, 180f + root.eulerAngles.y, 0f);
                v.Dist = 25f;
            }
            else if (name == "face")
            {
                Transform face = null;
                if (node is CharaRealization c)
                {
                    var hb = c.Context?.HeadBones;
                    if (hb != null && hb.TryGetValue("cf_J_FaceRoot", out var f) && f) face = f;
                    else if (c.Context?.HeadBone) face = c.Context.HeadBone.transform;
                }
                v.Pivot = face ? face.position : root.position + Vector3.up * 15f;
                v.Rot = new Vector3(5f, 180f + root.eulerAngles.y, 0f);
                v.Dist = 6f;
            }
            else if (name == "auto")
            {
                var b = BoundsOf(Drawn(key));
                v.Pivot = b.center;
                v.Rot = new Vector3(20f, 150f, 0f);
                v.Dist = Mathf.Max(5f, b.extents.magnitude * 2.5f / Mathf.Tan(11.5f * Mathf.Deg2Rad) / 2f);
            }
            else
            {
                var p = a.Get("pivot", new List<float>());
                var e = a.Get("rot", new List<float>());
                if (p.Count != 3 || e.Count != 3) throw new ArgumentException("a view: full, face, auto, or pivot and rot as [x, y, z]");
                v.Pivot = new Vector3(p[0], p[1], p[2]);
                v.Rot = new Vector3(e[0], e[1], e[2]);
                v.Dist = a.Get("dist", 25f);
            }
            return v;
        }

        static Dictionary<string, object> ViewRow(View v) => new Dictionary<string, object>
        {
            ["pivot"] = new[] { v.Pivot.x, v.Pivot.y, v.Pivot.z }, ["rot"] = new[] { v.Rot.x, v.Rot.y, v.Rot.z }, ["dist"] = v.Dist, ["fov"] = v.Fov, ["width"] = v.W, ["height"] = v.H,
        };

        /// <summary>One render of the view camera at <paramref name="v"/>, no AA, in memory (rows from the top, as the PNG); the camera's pose put back.</summary>
        static Color32[] ShotAt(View v)
        {
            var cam = ViewCam;
            var t = cam.transform;
            var (pos, rot, fov) = (t.position, t.rotation, cam.fieldOfView);
            try
            {
                var q = Quaternion.Euler(v.Rot);
                t.SetPositionAndRotation(v.Pivot - q * Vector3.forward * v.Dist, q);
                cam.fieldOfView = v.Fov;
                var res = CaptureService.RenderSync(Rt, cam, new CaptureRequest { Width = v.W, Height = v.H, Aa = CaptureService.None, MaxPixels = CaptureLimits.HardMaxPixels });
                if (!res.Ok || res.Png == null) throw new InvalidOperationException("the render failed: " + res.Error);
                return Decode(res.Png, out _, out _);
            }
            finally { t.SetPositionAndRotation(pos, rot); cam.fieldOfView = fov; }
        }

        static Color32[] Decode(byte[] png, out int w, out int h)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(tex, png, false)) throw new InvalidOperationException("a render could not be decoded");
                w = tex.width; h = tex.height;
                return tex.GetPixels32();
            }
            finally { Object.Destroy(tex); }
        }

        static int Delta(Color32 a, Color32 b) => Math.Max(Math.Abs(a.r - b.r), Math.Max(Math.Abs(a.g - b.g), Math.Abs(a.b - b.b)));

        // ------------------------------------------------------------ item, find

        object Item(string key)
        {
            var it = Rt.FindItem(key) ?? throw new InvalidOperationException("no item " + key);
            var rs = it.Instance ? it.Instance.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            var o = new Dictionary<string, object>
            {
                ["state"] = it.State.ToString(), ["source"] = it.Source, ["fallback"] = it.IsFallback, ["built"] = (bool)it.Instance, ["renderers"] = rs.Length,
                ["fk"] = it.Features?.FkBones?.ToList() ?? new List<string>(),
                ["members"] = rs.Where(r => r && MosaicSet.IsAdded(r)).Select(r => r.name).ToList(),
                ["cacheLoads"] = BakeCache.Of(Rt, false)?.Loads ?? 0,
            };
            if (it.Instance)
            {
                var t = it.Instance.transform;
                o["position"] = new[] { t.position.x, t.position.y, t.position.z };
                o["euler"] = new[] { t.eulerAngles.x, t.eulerAngles.y, t.eulerAngles.z };
                o["scale"] = new[] { t.lossyScale.x, t.lossyScale.y, t.lossyScale.z };
                var smr = it.Instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                o["skinnedBones"] = smr ? smr.bones.Length : 0;
            }
            var p = BakeCache.PrototypeOf(it);
            if (p != null)
            {
                o["prototype"] = new Dictionary<string, object>
                {
                    ["main"] = p.Main, ["kind"] = p.Bake?.Kind, ["renderers"] = p.Bake?.Renderers.Count ?? 0, ["materials"] = p.Bake?.Materials.Count ?? 0, ["textures"] = p.Bake?.Textures.Count ?? 0,
                    ["loadMs"] = p.Ms.TryGetValue("load", out var lm) ? R(lm) : -1, ["buildMs"] = R(p.BuildMs), ["videoBytes"] = p.VideoBytes, ["meshBytes"] = p.MeshBytes, ["members"] = p.Members.Length,
                    ["shaders"] = p.Bake == null ? new List<string>() : p.Bake.Materials.Select(m => m.Shader).Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList(),
                    ["findings"] = p.Findings.Select(f => f.Code).ToList(),
                    ["sections"] = p.Bake?.Renderers.Count(x => x.Inside == BakeData.InsideSection) ?? 0,
                    ["split"] = p.Ms.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => (object)R(kv.Value)),
                };
            }
            var st = Rt.Model.Find(key)?.State as ItemNodeState;
            if (st?.Cut?.Source != null && st.Bake != null)
                o["newBlobs"] = st.Bake.Data.Count(d => !st.Cut.Source.Data.Contains(d));   // the piece's blobs its source does not hold: the meshes the cut changed
            return o;
        }

        static object Find(List<string> names)
        {
            var rows = new List<object>();
            foreach (var n in names)
            {
                var s = Shader.Find(n);
                rows.Add(new Dictionary<string, object> { ["name"] = n, ["found"] = s != null, ["supported"] = s != null && s.isSupported, ["passes"] = s != null ? s.passCount : 0 });
            }
            return new Dictionary<string, object> { ["rows"] = rows };
        }

        // ------------------------------------------------------------ look, shot

        object Look(string aKey, string bKey, View v, bool name)
        {
            var all = Everything();
            var src = Drawn(aKey);
            var bake = Drawn(bKey);
            Color32[] bg, a, a2, b;
            using (new Only(all, new HashSet<Renderer>())) bg = ShotAt(v);
            using (new Only(all, new HashSet<Renderer>(src))) { a = ShotAt(v); a2 = ShotAt(v); }
            using (new Only(all, new HashSet<Renderer>(bake))) b = ShotAt(v);
            int footprint = 0, footB = 0, noise = 0, differ = 0, over2 = 0, over8 = 0, max = 0;
            var diff = new List<int>();
            for (int i = 0; i < a.Length; i++)
            {
                if (Delta(a[i], bg[i]) > 0) footprint++;
                if (Delta(b[i], bg[i]) > 0) footB++;
                if (Delta(a[i], a2[i]) > 0) noise++;
                int d = Delta(a[i], b[i]);
                if (d > 0) { differ++; diff.Add(i); }
                if (d > 2) over2++;
                if (d > 8) over8++;
                if (d > max) max = d;
            }
            var by = new Dictionary<string, int[]>(StringComparer.Ordinal);
            int unnamed = 0;
            if (name && diff.Count > 0)
            {
                var best = Enumerable.Repeat(int.MaxValue, diff.Count).ToArray();
                var bestName = new string[diff.Count];
                void Attribute(IList<Renderer> list, Color32[] full)
                {
                    foreach (var r in list)
                    {
                        Color32[] alone;
                        using (new Only(all, new HashSet<Renderer> { r })) alone = ShotAt(v);
                        for (int k = 0; k < diff.Count; k++)
                        {
                            int i = diff[k];
                            if (bestName[k] != null && best[k] == 0) continue;
                            if (Delta(alone[i], bg[i]) == 0) continue;
                            int dd = Delta(alone[i], full[i]);
                            if (dd < best[k]) { best[k] = dd; bestName[k] = r.name; }
                        }
                    }
                }
                Attribute(src, a);
                if (bestName.Any(x => x == null)) Attribute(bake, b);
                for (int k = 0; k < diff.Count; k++)
                {
                    int i = diff[k];
                    string n = bestName[k] ?? "(no renderer)";
                    if (bestName[k] == null) unnamed++;
                    if (!by.TryGetValue(n, out var row)) by[n] = row = new int[3];
                    int d = Delta(a[i], b[i]);
                    row[0]++;
                    if (d > 2) row[1]++;
                    if (d > 8) row[2]++;
                }
            }
            return new Dictionary<string, object>
            {
                ["view"] = ViewRow(v), ["pixels"] = a.Length, ["footprint"] = footprint, ["footprintB"] = footB, ["noise"] = noise, ["differ"] = differ, ["over2"] = over2, ["over8"] = over8, ["max"] = max,
                ["unnamed"] = unnamed, ["sourceRenderers"] = src.Count, ["otherRenderers"] = bake.Count,
                ["byRenderer"] = by.OrderByDescending(kv => kv.Value[0]).Select(kv => new Dictionary<string, object> { ["name"] = kv.Key, ["any"] = kv.Value[0], ["over2"] = kv.Value[1], ["over8"] = kv.Value[2] }).ToList(),
            };
        }

        static string Sha(Color32[] px)
        {
            var bytes = new byte[px.Length * 4];
            for (int i = 0; i < px.Length; i++) { bytes[4 * i] = px[i].r; bytes[4 * i + 1] = px[i].g; bytes[4 * i + 2] = px[i].b; bytes[4 * i + 3] = px[i].a; }
            using (var s = SHA256.Create()) return BitConverter.ToString(s.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        object ShotOf(string key, View v)
        {
            var all = Everything();
            var mine = Drawn(key);
            Color32[] bg, px, px2;
            using (new Only(all, new HashSet<Renderer>())) bg = ShotAt(v);
            using (new Only(all, new HashSet<Renderer>(mine))) { px = ShotAt(v); px2 = ShotAt(v); }
            int foot = 0, twice = 0;
            for (int i = 0; i < px.Length; i++) { if (Delta(px[i], bg[i]) > 0) foot++; if (Delta(px[i], px2[i]) > 0) twice++; }
            return new Dictionary<string, object> { ["view"] = ViewRow(v), ["sha"] = Sha(px), ["footprint"] = foot, ["twice"] = twice, ["renderers"] = mine.Count, ["pixels"] = px.Length };
        }

        // ------------------------------------------------------------ capture and encoding (BW1, BW1b, BW2, BW7)

        object Capture(string key, List<string> parts, bool insides, bool centre)
        {
            Release();
            var c = Rt.FindChara(key) ?? throw new InvalidOperationException("no character " + key);
            bool? legacy = LegacyOn;
            var meters = Meters.Start();
            var sw = Stopwatch.StartNew();
            try { job = BakeJob.OfChara(c, new BakeOptions { Parts = parts.Count > 0 ? parts : null, Insides = insides, Centre = centre }); }
            finally
            {
                sw.Stop();
                meters.Stop();
            }
            var cap = job.Capture;
            double Of(Dictionary<string, double> d, string k) => d.TryGetValue(k, out var v) ? v : 0;
            int Count(string k) => cap.Counts.TryGetValue(k, out var v) ? v : 0;
            double materials = Of(cap.Ms, "materials"), readback = Of(cap.Ms, "textures"), build = Of(job.Ms, "textureBuild"), wait = Of(job.Ms, "encodeWait");
            var stages = new Dictionary<string, object>
            {
                ["capture"] = R(Of(cap.Ms, "capture")), ["nodes"] = R(Of(cap.Ms, "nodes")), ["meshes"] = R(Of(cap.Ms, "meshes")), ["materials"] = R(materials),
                ["readback"] = R(readback), ["textureBuild"] = R(build), ["encodeWait"] = R(wait), ["materialsRest"] = R(materials - readback - build - wait),
            };
            encodeClock = Stopwatch.StartNew();
            encodeMs = -1;
            var j = job;
            encode = Task.Run(() => j.Finish());
            return new Dictionary<string, object>
            {
                ["legacy"] = legacy, ["stages"] = stages, ["levelsRead"] = Count("tex.levels.read"), ["levelByLevelWaits"] = Count("tex.waits.levelByLevel"),
                ["maxInFlight"] = legacy == true ? LegacyLimit : BakeJob.MaxInFlight, ["encodeWaitMs"] = R(wait),
                ["captureMs"] = R(sw.Elapsed.TotalMilliseconds), ["split"] = cap.Ms.ToDictionary(kv => kv.Key, kv => (object)R(kv.Value)), ["heapBefore"] = meters.HeapBefore, ["heapPeak"] = meters.HeapPeak,
                ["heapSamples"] = meters.Samples, ["privateBefore"] = meters.PrivateBefore, ["privatePeak"] = meters.PrivatePeak, ["peakInFlight"] = j.PeakInFlight, ["textureBytes"] = j.TextureBytes, ["largestTexture"] = j.LargestTexture,
                ["renderers"] = cap.Bake?.Renderers.Count ?? 0, ["textures"] = cap.Bake?.Textures.Count ?? 0, ["meshes"] = cap.Bake?.Meshes.Count ?? 0, ["nodes"] = cap.Bake?.Nodes.Count ?? 0,
                ["counts"] = cap.Counts.ToDictionary(kv => kv.Key, kv => (object)kv.Value), ["findings"] = cap.Findings.Select(f => f.Code).ToList(),
            };
        }

        /// <summary>Two meters read on a thread every millisecond between Start and Stop: the managed heap (GC.GetTotalMemory(false), BW7's bar) and the
        /// process's private bytes (printed); each one's value at the start (after a collection) and its peak.</summary>
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct MemoryCounters
        {
            public uint Cb, PageFaultCount;
            public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage, QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage,
                PeakPagefileUsage, PrivateUsage;
        }

        [System.Runtime.InteropServices.DllImport("psapi.dll")]
        static extern bool GetProcessMemoryInfo(IntPtr process, ref MemoryCounters counters, uint size);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        /// <summary>The process's private bytes (PROCESS_MEMORY_COUNTERS_EX.PrivateUsage; Process.PrivateMemorySize64 reads 0 in the player); -1 when unread.</summary>
        static long PrivateBytes()
        {
            var c = new MemoryCounters { Cb = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MemoryCounters)) };
            return GetProcessMemoryInfo(GetCurrentProcess(), ref c, c.Cb) ? (long)(ulong)c.PrivateUsage : -1;
        }

        sealed class Meters
        {
            public long HeapBefore, HeapPeak, PrivateBefore, PrivatePeak;
            public int Samples;
            volatile bool stop;
            Thread thread;

            public static Meters Start()
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var m = new Meters();
                m.HeapBefore = m.HeapPeak = GC.GetTotalMemory(false);
                m.PrivateBefore = m.PrivatePeak = PrivateBytes();
                m.thread = new Thread(() =>
                {
                    while (!m.stop)
                    {
                        long h = GC.GetTotalMemory(false);
                        if (h > Interlocked.Read(ref m.HeapPeak)) Interlocked.Exchange(ref m.HeapPeak, h);
                        long pv = PrivateBytes();
                        if (pv > Interlocked.Read(ref m.PrivatePeak)) Interlocked.Exchange(ref m.PrivatePeak, pv);
                        Interlocked.Increment(ref m.Samples);
                        Thread.Sleep(1);
                    }
                }) { IsBackground = true, Name = "bakeprobe meters" };
                m.thread.Start();
                return m;
            }

            public void Stop()
            {
                stop = true;
                thread.Join(2000);
            }
        }

        /// <summary>BW7's control: <paramref name="mb"/> MiB of managed arrays allocated and touched while the meters run on an otherwise idle main
        /// thread, held for a moment, then let go; what each meter read above its start.</summary>
        static object HeapControl(int mb)
        {
            var meters = Meters.Start();
            var held = new List<byte[]>();
            var sw = Stopwatch.StartNew();
            try
            {
                for (int i = 0; i < mb; i++)
                {
                    var a = new byte[1 << 20];
                    for (int k = 0; k < a.Length; k += 4096) a[k] = 1;
                    held.Add(a);
                }
                Thread.Sleep(50);
            }
            finally { meters.Stop(); }
            long sum = held.Sum(x => (long)x.Length);
            held.Clear();
            GC.Collect();
            return new Dictionary<string, object>
            {
                ["allocated"] = sum, ["heapBefore"] = meters.HeapBefore, ["heapPeak"] = meters.HeapPeak, ["heapRead"] = meters.HeapPeak - meters.HeapBefore,
                ["privateBefore"] = meters.PrivateBefore, ["privatePeak"] = meters.PrivatePeak, ["privateRead"] = meters.PrivatePeak - meters.PrivateBefore, ["samples"] = meters.Samples,
                ["ms"] = R(sw.Elapsed.TotalMilliseconds),
            };
        }

        /// <summary>BW4's read: the bake's main and data entries read from the saved scene file (inflated as the container reads them), in the player.</summary>
        static object ReadRes(string key, string path)
        {
            var st = Rt.Model.Find(key)?.State as ItemNodeState ?? throw new InvalidOperationException("no item " + key);
            if (st.Bake == null) throw new InvalidOperationException("an item that is not drawn from a bake");
            var names = new HashSet<string>(new[] { st.Bake.Main }.Concat(st.Bake.Data), StringComparer.Ordinal);
            long bytes = 0;
            int read = 0;
            var sw = Stopwatch.StartNew();
            using (var f = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
            using (var z = new System.IO.Compression.ZipArchive(f, System.IO.Compression.ZipArchiveMode.Read))
            {
                var buf = new byte[1 << 20];
                foreach (var e in z.Entries)
                {
                    if (!names.Contains(e.FullName)) continue;
                    using (var s = e.Open())
                    {
                        int n;
                        while ((n = s.Read(buf, 0, buf.Length)) > 0) bytes += n;
                    }
                    read++;
                }
            }
            return new Dictionary<string, object> { ["ms"] = R(sw.Elapsed.TotalMilliseconds), ["entries"] = read, ["named"] = names.Count, ["bytes"] = bytes };
        }

        /// <summary>BV6's positive control: the view rendered with an item's members drawn by a flat magenta unlit material and the wide pass off (the
        /// mosaic left out), kept as <paramref name="tag"/>; every material and the switch put back.</summary>
        object Plain(string key, string tag, int w, int h)
        {
            var it = Rt.FindItem(key) ?? throw new InvalidOperationException("no item " + key);
            var mine = it.Instance ? new HashSet<Renderer>(it.Instance.GetComponentsInChildren<Renderer>(true)) : new HashSet<Renderer>();
            var members = MosaicSet.Standalone().Select(m => m.Renderer).Where(r => r && mine.Contains(r)).ToList();
            var sh = Shader.Find("HS2Next/Unlit/Color") ?? throw new InvalidOperationException("HS2Next/Unlit/Color not found");
            var flat = new Material(sh);
            flat.SetColor("_Color", new Color(1f, 0f, 1f, 1f));
            var saved = members.Select(r => (r, r.sharedMaterials)).ToList();
            bool wide = MosaicWide.Enabled;
            var shot = new Shot();
            shots[tag] = shot;
            try
            {
                foreach (var (r, m) in saved) r.sharedMaterials = Enumerable.Repeat(flat, m.Length).ToArray();
                MosaicWide.Enabled = false;
                Finish(shot, CaptureService.RenderSync(Rt, ViewCam, new CaptureRequest { Width = w, Height = h, Aa = CaptureService.None, MaxPixels = CaptureLimits.HardMaxPixels }));
            }
            finally
            {
                foreach (var (r, m) in saved) if (r) r.sharedMaterials = m;
                MosaicWide.Enabled = wide;
                Object.Destroy(flat);
            }
            var o = (Dictionary<string, object>)Rendered(tag);
            o["members"] = members.Count;
            return o;
        }

        object Encoded()
        {
            if (job == null || encode == null) return new Dictionary<string, object> { ["job"] = false };
            bool done = encode.IsCompleted;
            if (done && encodeMs < 0) encodeMs = encodeClock.Elapsed.TotalMilliseconds;
            var o = new Dictionary<string, object> { ["job"] = true, ["done"] = done, ["faulted"] = encode.IsFaulted, ["ms"] = R(done ? encodeMs : encodeClock.Elapsed.TotalMilliseconds) };
            if (encode.IsFaulted) o["error"] = (encode.Exception?.InnerException ?? encode.Exception)?.Message;
            if (done && !encode.IsFaulted)
            {
                o["bytes"] = job.Bytes;
                o["blobs"] = job.Blobs.Count + 1;
                o["main"] = job.MainSha;
                o["jobMs"] = job.Ms.ToDictionary(kv => kv.Key, kv => (object)R(kv.Value));
                o["identity"] = Identity(job.Main, job.MainSha);
            }
            return o;
        }

        // ------------------------------------------------------------ the old path's switch, a bake's identity, the runtime's times (YI1a, YI3, BW4)

        static readonly FieldInfo LegacyField = typeof(BakeJob).GetField("Legacy", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        static readonly FieldInfo LegacyLimitField = typeof(BakeJob).GetField("LegacyMaxInFlight", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        /// <summary>The switch's value, or null in a build that has none.</summary>
        static bool? LegacyOn => LegacyField != null ? (bool)LegacyField.GetValue(null) : (bool?)null;
        static long LegacyLimit => LegacyLimitField != null ? Convert.ToInt64(LegacyLimitField.GetValue(null)) : -1;

        static object Legacy(bool? on)
        {
            if (on.HasValue && LegacyField == null && on.Value) throw new InvalidOperationException("this build has no old path switch");
            if (on.HasValue && LegacyField != null) LegacyField.SetValue(null, on.Value);
            return new Dictionary<string, object> { ["switch"] = LegacyField != null, ["legacy"] = LegacyOn, ["maxInFlight"] = BakeJob.MaxInFlight, ["legacyMaxInFlight"] = LegacyLimit };
        }

        static string ShaHex(byte[] bytes)
        {
            using (var s = SHA256.Create()) return BitConverter.ToString(s.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>A bake's identity: its main's SHA-256 with id and created set to fixed values, and every data entry (meshes, then textures, in
        /// the main's order) with its blob; the data set's hash over the sorted distinct blobs.</summary>
        static Dictionary<string, object> Identity(BakeMain main, string mainSha)
        {
            var m = main.Clone();
            m.Id = "00000000000000000000000000000000";
            m.Created = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var entries = new List<object>();
            for (int i = 0; i < main.Meshes.Count; i++) entries.Add(new Dictionary<string, object> { ["kind"] = "mesh", ["index"] = i, ["name"] = main.Meshes[i].Name, ["blob"] = main.Meshes[i].Blob });
            for (int i = 0; i < main.Textures.Count; i++) entries.Add(new Dictionary<string, object> { ["kind"] = "texture", ["index"] = i, ["name"] = main.Textures[i].Name, ["blob"] = main.Textures[i].Blob });
            var data = main.DataBlobs();
            return new Dictionary<string, object>
            {
                ["main"] = mainSha, ["mainNormalized"] = ShaHex(m.ToJsonBytes()), ["data"] = data.Count,
                ["dataSet"] = ShaHex(System.Text.Encoding.ASCII.GetBytes(string.Join(",", data.OrderBy(x => x, StringComparer.Ordinal)))), ["entries"] = entries,
            };
        }

        static object Blobs(string key)
        {
            var st = Rt.Model.Find(key)?.State as ItemNodeState ?? throw new InvalidOperationException("no item " + key);
            if (st.Bake == null) throw new InvalidOperationException("an item that is not drawn from a bake");
            var blob = Rt.Session.Model.File.Res.Get(st.Bake.Main) ?? throw new InvalidOperationException("the scene holds no main " + st.Bake.Main);
            var o = Identity(BakeMain.FromBlob(blob), BakeBlob.ShaOf(st.Bake.Main));
            var block = st.Bake.Data.Select(BakeBlob.ShaOf).ToList();
            o["blockData"] = block.Count;
            o["blockDataSet"] = ShaHex(System.Text.Encoding.ASCII.GetBytes(string.Join(",", block.OrderBy(x => x, StringComparer.Ordinal))));
            o["cut"] = st.Cut != null;
            return o;
        }

        static object CacheMs(bool clear)
        {
            var cache = BakeCache.Of(Rt, false);
            var o = new Dictionary<string, object> { ["cache"] = cache != null, ["legacy"] = LegacyOn };
            if (cache == null) return o;
            o["ms"] = cache.Ms.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => (object)R(kv.Value));
            o["loads"] = cache.Loads;
            o["builds"] = cache.Builds;
            if (clear) cache.Ms.Clear();
            return o;
        }

        void Release()
        {
            try { encode?.Wait(60000); } catch (Exception) { }
            try { job?.Dispose(); } catch (Exception) { }
            job = null; encode = null;
        }

        AsyncOperation unloadOp;

        /// <summary>BW7's reading after the capture (printed, no bar): one Resources.UnloadUnusedAssets, then a full collection, then the private bytes.</summary>
        object Unload(bool start)
        {
            if (start || unloadOp == null)
            {
                unloadOp = Resources.UnloadUnusedAssets();
                return new Dictionary<string, object> { ["started"] = true, ["done"] = false };
            }
            if (!unloadOp.isDone) return new Dictionary<string, object> { ["done"] = false };
            unloadOp = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return new Dictionary<string, object> { ["done"] = true, ["private"] = PrivateBytes(), ["heap"] = GC.GetTotalMemory(false) };
        }

        // ------------------------------------------------------------ the procedure of pieces again (BW5)

        static object Cut(List<string> keys, int rounds)
        {
            if (keys.Count == 0) throw new ArgumentException("no piece");
            var states = keys.Select(k => Rt.Model.Find(k)?.State as ItemNodeState ?? throw new InvalidOperationException("no item " + k)).ToList();
            if (states.Any(s => s.Cut == null || s.Bake == null)) throw new InvalidOperationException("an item that is not a piece");
            var cut = states[0].Cut;
            var src = BakeBlobSet.Gather(cut.Source, Rt.Session.Model.File.Res, BakeProvider.StoreOf(Rt), null, keys[0]);
            if (src.Status != BakeBlobSet.Result.Ok) throw new InvalidOperationException("the source does not gather: " + src.Status);
            var keeps = states.Select(s => s.Cut.Steps[s.Cut.Steps.Count - 1].Keep).ToArray();
            var ids = states.Select(s => s.Bake.Id).ToList();
            var want = states.Select(s => BakeBlob.ShaOf(s.Bake.Main)).ToList();
            var ms = new List<double>();
            var same = new List<bool>();
            var decode = new List<object>();
            var encodeMs = new List<object>();
            var decoded = new List<object>();
            string error = null;
            for (int r = 0; r < rounds; r++)
            {
                var o = CutPieces.Run(cut, src.Main, src.Get, keeps, ids);
                if (o.Error != null) { error = o.Error + " " + o.ErrorText; break; }
                ms.Add(o.Milliseconds);
                same.Add(o.Pieces.Count == keys.Count && Enumerable.Range(0, keys.Count).All(i => o.Pieces[i].MainSha == want[i]));
                // the stages of the run where this build records them (read by name: the plugin builds against older players too)
                decode.Add(OutcomeField(o, "DecodeMilliseconds"));
                encodeMs.Add(OutcomeField(o, "EncodeMilliseconds"));
                decoded.Add(OutcomeField(o, "MeshesDecoded"));
            }
            var sorted = ms.OrderBy(x => x).ToList();
            return new Dictionary<string, object>
            {
                ["ms"] = ms.Select(R).ToList(), ["median"] = sorted.Count > 0 ? R(sorted[sorted.Count / 2]) : -1, ["sameBits"] = same, ["error"] = error, ["steps"] = cut.Steps.Count, ["keeps"] = keeps,
                ["stages"] = typeof(CutOutcome).GetField("DecodeMilliseconds") != null, ["decodeMs"] = decode, ["encodeMs"] = encodeMs, ["meshesDecoded"] = decoded,
            };
        }

        /// <summary>A field of a cut's outcome by name (a double rounded, an int as it is), or null when this build has none.</summary>
        static object OutcomeField(CutOutcome o, string name)
        {
            var f = typeof(CutOutcome).GetField(name, BindingFlags.Instance | BindingFlags.Public);
            var v = f?.GetValue(o);
            return v is double d ? (object)R(d) : v;
        }

        // ------------------------------------------------------------ rows (BW9) and the app's group (BW8)

        object Rows(int n, int rounds)
        {
            var rows = ctx.Unity()?.ItemRows ?? throw new InvalidOperationException("no IItemRows in this host");
            var append = new List<double>();
            var replace = new List<double>();
            for (int r = 0; r < rounds; r++)
            {
                var a = Enumerable.Range(1, n).Select(i => new ItemRow { Category = 0, Id = i, Name = "row " + i }).ToList();
                var b = Enumerable.Range(n + 1, n).Select(i => new ItemRow { Category = 0, Id = i, Name = "row " + i }).ToList();
                var g = rows.AddGroup(RowsGroup, "rows probe", _ => null);
                try
                {
                    g.SetCategory(0, "rows");
                    var sw = Stopwatch.StartNew();
                    g.SetRows(a);
                    append.Add(sw.Elapsed.TotalMilliseconds);
                    sw.Restart();
                    g.SetRows(b);
                    replace.Add(sw.Elapsed.TotalMilliseconds);
                }
                finally { g.Dispose(); }
            }
            return new Dictionary<string, object> { ["n"] = n, ["append"] = append.Select(R).ToList(), ["replace"] = replace.Select(R).ToList(), ["appendMedian"] = Median(append), ["replaceMedian"] = Median(replace) };
        }

        object AppGroup(int rounds)
        {
            var db = Rt.Services.Db;
            var refresh = new List<double>();
            var shape = new List<double>();
            int listed = -1;
            var rows = ctx.Unity()?.ItemRows ?? throw new InvalidOperationException("no IItemRows in this host");
            for (int r = 0; r < rounds; r++)
            {
                var sw = Stopwatch.StartNew();
                listed = AppItemRows.RefreshBakes(db);
                refresh.Add(sw.Elapsed.TotalMilliseconds);
                sw.Restart();
                var g = rows.AddGroup(RowsGroup, "rows probe", _ => null);
                g.SetCategory(0, "bakes");
                g.SetCategory(1, "markers");
                g.SetRows(Enumerable.Range(1, 4).Select(i => new ItemRow { Category = 1, Id = i, Name = "marker " + i }).ToList());
                g.Dispose();
                shape.Add(sw.Elapsed.TotalMilliseconds);
            }
            return new Dictionary<string, object>
            {
                ["registered"] = db.Generated.Group(AppItemRows.Group)?.Owner == AppItemRows.Owner, ["storeRows"] = listed, ["refresh"] = refresh.Select(R).ToList(), ["refreshMedian"] = Median(refresh),
                ["shape"] = shape.Select(R).ToList(), ["shapeMedian"] = Median(shape), ["generatedRows"] = db.Generated.RowCount,
            };
        }

        static double Median(List<double> v) => v.Count == 0 ? -1 : R(v.OrderBy(x => x).ElementAt(v.Count / 2));

        // ------------------------------------------------------------ members (BV6)

        static object Members(string key, bool? hide, int w, int h)
        {
            var it = Rt.FindItem(key) ?? throw new InvalidOperationException("no item " + key);
            var mine = it.Instance ? new HashSet<Renderer>(it.Instance.GetComponentsInChildren<Renderer>(true)) : new HashSet<Renderer>();
            var rows = new List<object>();
            foreach (var m in MosaicSet.Standalone())
            {
                var rd = m.Renderer;
                if (!rd || !mine.Contains(rd)) continue;
                if (hide.HasValue) rd.forceRenderingOff = hide.Value;
                var row = new Dictionary<string, object>
                {
                    ["name"] = rd.name, ["mode"] = m.Mode.ToString(), ["blockPx"] = R(m.BlockPx), ["cellPx"] = R(m.CellPx), ["lastCamera"] = m.LastCamera, ["hidden"] = rd.forceRenderingOff,
                    ["centre"] = new[] { rd.bounds.center.x, rd.bounds.center.y, rd.bounds.center.z },
                };
                if (m.Region != null)
                {
                    var at = MosaicCamera.RegionMatrix(m);
                    var c = at.MultiplyPoint3x4(m.Region.Centroid);
                    var n = at.MultiplyVector(m.Region.Normal).normalized;
                    row["centroid"] = new[] { c.x, c.y, c.z };
                    row["normal"] = new[] { n.x, n.y, n.z };
                }
                rows.Add(row);
            }
            var fwd = it.Instance ? it.Instance.transform.forward : Vector3.forward;
            return new Dictionary<string, object> { ["rows"] = rows, ["floorPx"] = R(MosaicCamera.FloorPx(w, h)), ["forward"] = new[] { fwd.x, fwd.y, fwd.z } };
        }

        // ------------------------------------------------------------ renders kept in memory (BV6)

        object Render(string tag, string path, int w, int h, string aa)
        {
            var shot = new Shot();
            shots[tag] = shot;
            if (path == "thumb")
            {
                var f = Rt.Ops.ThumbnailRenderer ?? throw new InvalidOperationException("no thumbnail renderer");
                var png = f();
                shot.Px = Decode(png, out shot.W, out shot.H);
                shot.Done = true;
                return Rendered(tag);
            }
            var req = new CaptureRequest { Width = w, Height = h, Aa = aa, MaxPixels = CaptureLimits.HardMaxPixels };
            if (path == "async")
            {
                CaptureService.RenderAsync(Rt, ViewCam, req, null, r => Finish(shot, r));
                return new Dictionary<string, object> { ["async"] = true };
            }
            Finish(shot, CaptureService.RenderSync(Rt, ViewCam, req));
            return Rendered(tag);
        }

        static void Finish(Shot s, CaptureResult r)
        {
            s.Result = r;
            s.Error = r.Error;
            if (r.Ok && r.Png != null) s.Px = Decode(r.Png, out s.W, out s.H);
            r.Png = null;
            s.Done = true;
        }

        object Rendered(string tag)
        {
            if (!shots.TryGetValue(tag, out var s)) throw new InvalidOperationException("no render " + tag);
            return new Dictionary<string, object> { ["done"] = s.Done, ["error"] = s.Error, ["width"] = s.W, ["height"] = s.H, ["pixels"] = s.Px?.Length ?? 0 };
        }

        object Compare(string a, string b, int threshold, string within, string withinOf)
        {
            var x = shots[a].Px;
            var y = shots[b].Px;
            if (x == null || y == null || x.Length != y.Length) throw new InvalidOperationException("the renders cannot be compared");
            Color32[] m = within != null ? shots[within].Px : null, mo = withinOf != null ? shots[withinOf].Px : null;
            long region = 0, differ = 0, any = 0;
            int most = 0;
            for (int i = 0; i < x.Length; i++)
            {
                if (m != null && mo != null && Delta(m[i], mo[i]) < 8) continue;
                region++;
                int d = Delta(x[i], y[i]);
                if (d > most) most = d;
                if (d > 0) any++;
                if (d > threshold) differ++;
            }
            return new Dictionary<string, object> { ["pixels"] = region, ["differ"] = differ, ["any"] = any, ["most"] = most };
        }

        object Flat(string tag, List<string> mask)
        {
            var s = shots[tag];
            if (mask.Count != 2) throw new ArgumentException("mask: two renders");
            var p = shots[mask[0]].Px;
            var q = shots[mask[1]].Px;
            long inMask = 0, right = 0, up = 0;
            for (int y = 0; y < s.H; y++)
                for (int x = 0; x < s.W; x++)
                {
                    int i = y * s.W + x;
                    if (Delta(p[i], q[i]) < 8) continue;
                    inMask++;
                    if (x + 1 < s.W && Delta(s.Px[i], s.Px[i + 1]) == 0) right++;
                    if (y + 1 < s.H && Delta(s.Px[i], s.Px[i + s.W]) == 0) up++;
                }
            return new Dictionary<string, object> { ["mask"] = inMask, ["shareRight"] = inMask > 0 ? R(right / (double)inMask) : 0, ["shareUp"] = inMask > 0 ? R(up / (double)inMask) : 0 };
        }

        // ------------------------------------------------------------ a command timed (BW4, BW8)

        object Time(string command, Dictionary<string, object> args, bool apply, int rounds)
        {
            var ms = new List<double>();
            ICommandReply last = null;
            for (int r = 0; r < Math.Max(1, rounds); r++)
            {
                var sw = Stopwatch.StartNew();
                last = ctx.Request(command, args);
                if (apply) Rt.ApplyNow();
                ms.Add(sw.Elapsed.TotalMilliseconds);
                if (!last.Ok) break;
            }
            return new Dictionary<string, object> { ["ms"] = ms.Select(R).ToList(), ["median"] = Median(ms), ["ok"] = last?.Ok ?? false, ["code"] = last?.Code, ["error"] = last?.Error, ["reply"] = last?.Json };
        }

        static object Duplicate(string key)
        {
            var sw = Stopwatch.StartNew();
            var r = AppCore.Current.Ops.Duplicate(new[] { key });
            Rt.ApplyNow();
            double ms = sw.Elapsed.TotalMilliseconds;
            return new Dictionary<string, object> { ["ok"] = r.Ok, ["ms"] = R(ms), ["ids"] = r.NewIds?.ToList() ?? new List<string>(), ["error"] = r.Ok ? null : r.ToString() };
        }

        // ------------------------------------------------------------ meshes, the sample (BD12)

        static object Meshes(string key)
        {
            var it = Rt.FindItem(key) ?? throw new InvalidOperationException("no item " + key);
            var rs = it.Instance ? it.Instance.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList() : new List<Renderer>();
            int meshes = 0, unreadable = 0, verts = 0, unreadableVerts = 0, skinned = 0;
            var names = new List<string>();
            foreach (var r in rs)
            {
                var m = BakeCapture.MeshOf(r);
                if (!m) continue;
                meshes++;
                verts += m.vertexCount;
                if (r is SkinnedMeshRenderer) skinned++;
                if (!m.isReadable) { unreadable++; unreadableVerts += m.vertexCount; names.Add(r.name); }
            }
            var b = BoundsOf(rs);
            return new Dictionary<string, object>
            {
                ["renderers"] = rs.Count, ["meshes"] = meshes, ["skinned"] = skinned, ["vertices"] = verts, ["unreadable"] = unreadable, ["unreadableVertices"] = unreadableVerts, ["unreadableRenderers"] = names,
                ["min"] = new[] { b.min.x, b.min.y, b.min.z }, ["max"] = new[] { b.max.x, b.max.y, b.max.z },
            };
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct MemoryStatus { public uint Length, Load; public ulong TotalPhys, AvailPhys, TotalPage, AvailPage, TotalVirtual, AvailVirtual, AvailExtended; }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool GlobalMemoryStatusEx(ref MemoryStatus m);

        static double FreeGb()
        {
            var m = new MemoryStatus { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MemoryStatus)) };
            return GlobalMemoryStatusEx(ref m) ? m.AvailPhys / (1024.0 * 1024 * 1024) : 999;
        }

        /// <summary>Step 0's sample as BD12 takes it: every (count / n)-th visible item row, its prefab loaded for the moment, its meshes read.</summary>
        static object Sample(int n, float floorGb)
        {
            var db = Rt.Services.Db;
            var cat = Rt.Services.Catalog;
            var rows = db.Items.Enumerate().ToList();
            int step = Math.Max(1, rows.Count / n), tried = 0, loaded = 0, meshes = 0, unreadable = 0, stoppedAt = -1;
            var found = new List<object>();
            for (int i = 0; i < rows.Count && tried < n; i += step)
            {
                if (tried % 25 == 0 && tried > 0) Resources.UnloadUnusedAssets();
                if (FreeGb() < floorGb) { stoppedAt = tried; break; }
                var row = rows[i];
                tried++;
                int un = 0, all = 0;
                try
                {
                    cat.LoadTransient("bakeprobe:sample", () =>
                    {
                        var go = cat.LoadAsset<GameObject>(row);
                        if (!go) return 0;
                        loaded++;
                        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                        {
                            var m = BakeCapture.MeshOf(r);
                            if (!m) continue;
                            all++;
                            if (!m.isReadable) un++;
                        }
                        return 0;
                    });
                }
                catch (Exception) { }
                meshes += all;
                unreadable += un;
                if (un > 0) found.Add(new Dictionary<string, object> { ["ref"] = row.Key.ToString(), ["meshes"] = all, ["unreadable"] = un });
            }
            Resources.UnloadUnusedAssets();
            return new Dictionary<string, object> { ["rows"] = rows.Count, ["tried"] = tried, ["loaded"] = loaded, ["meshes"] = meshes, ["unreadable"] = unreadable, ["found"] = found, ["stoppedAt"] = stoppedAt, ["freeGb"] = R(FreeGb()) };
        }

        // ------------------------------------------------------------ vertices (BR9), parts (BW8)

        object Verts(string key, bool keep)
        {
            var it = Rt.FindItem(key) ?? throw new InvalidOperationException("no item " + key);
            var pts = new List<Vector3>();
            var bones = new Dictionary<string, object>(StringComparer.Ordinal);
            var tmp = new Mesh();
            try
            {
                foreach (var r in it.Instance.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is SkinnedMeshRenderer s)
                    {
                        s.BakeMesh(tmp, true);
                        var m = s.transform.localToWorldMatrix;
                        foreach (var v in tmp.vertices) pts.Add(m.MultiplyPoint3x4(v));
                        foreach (var b in s.bones) if (b) bones[b.name] = new[] { b.localEulerAngles.x, b.localEulerAngles.y, b.localEulerAngles.z };
                    }
                    else if (r is MeshRenderer && r.GetComponent<MeshFilter>() is MeshFilter f && f.sharedMesh && f.sharedMesh.isReadable)
                    {
                        var m = r.transform.localToWorldMatrix;
                        foreach (var v in f.sharedMesh.vertices) pts.Add(m.MultiplyPoint3x4(v));
                    }
                }
            }
            finally { Object.Destroy(tmp); }
            var o = new Dictionary<string, object> { ["vertices"] = pts.Count, ["bones"] = bones };
            if (keep) { kept[key] = pts.ToArray(); return o; }
            if (kept.TryGetValue(key, out var before) && before.Length == pts.Count)
            {
                float moved = 0;
                for (int i = 0; i < before.Length; i++) moved = Mathf.Max(moved, Vector3.Distance(before[i], pts[i]));
                o["moved"] = R(moved);
            }
            return o;
        }

        static object Parts()
        {
            var names = Rt.Parts.Select(p => p.GetType().FullName).ToList();
            var ours = names.Where(n => n.Contains(".Bake") || n.Contains(".Cut") || n.Contains("GeneratedRow")).ToList();
            return new Dictionary<string, object> { ["parts"] = names, ["bakeParts"] = ours, ["bakeCache"] = BakeCache.Of(Rt, false) != null };
        }
    }
}

// BakeTest.render, the look of a bake (package P). Renders go through CaptureService.RenderSync on the
// host's view camera with no anti-aliasing, decoded in memory and compared there; nothing is written as an image.
//   BD1  a whole bake against its character, both targets, the full view (the Studio's reset view) and a face view: pixels over 2 levels
//        ≤ 0.2 % of the footprint, over 8 levels ≤ 0.01 %; every pixel over 8 named by its renderer; any difference counted
//   BD5  the residue named by renderer (every differing pixel attributed to the renderer drawn there)
//   BD3  textures per route: read-back bit-exact, decoded equal to a draw of the source, three channels expanded, the sRGB flag
//   BD4  materials: every declared property equal by type between the source (with its block) and the rebuilt material
//   BD15 the own route (the lead's round 5): every mesh stored by it equals the drawn sharedMesh's data in bits (positions, normals, tangents, UV
//        channels, colours, weights, bind poses, sub-meshes); Present: >= 1 mesh by the own route and >= 1 by the unskin route on each target
//   BB3  (render) textures shared by blob: the two pieces of a cut of a whole character and the items of two bakes of one character hold one
//        Texture2D per distinct texture blob and sampler state; the release of the last prototype destroys them; the time split of a cut
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next;
using HS2Next.Save;
using HS2Next.Scenes.Bake;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static partial class BakeTest
{
    // ------------------------------------------------------------ views and renders (in memory)

    /// <summary>A camera pose: pivot, Euler rotation, distance (the view camera's model).</summary>
    internal struct View
    {
        public string Name;
        public Vector3 Pivot, Rot;
        public float Distance, Fov;
        public int W, H;
    }

    /// <summary>The Studio's reset view around a character standing at <paramref name="root"/>.</summary>
    internal static View FullView(Transform root) => new View
    {
        Name = "full", Pivot = root.position + root.rotation * SceneViewCamera.DefaultPivot, Rot = SceneViewCamera.DefaultRot + new Vector3(0f, root.eulerAngles.y, 0f),
        Distance = SceneViewCamera.DefaultDistance, Fov = SceneViewCamera.DefaultFov, W = 1280, H = 720,
    };

    /// <summary>A close-up of the face: the head's face root, from in front, 6 units away.</summary>
    internal static View FaceView(Transform root, Transform face) => new View
    {
        Name = "face", Pivot = face ? face.position : root.position + Vector3.up * 15f, Rot = new Vector3(5f, 180f + root.eulerAngles.y, 0f),
        Distance = 6f, Fov = SceneViewCamera.DefaultFov, W = 1280, H = 720,
    };

    static Camera ViewCamera(Host h) => h.Rt.GetPart<SceneViewCamera>()?.Camera;

    // the shots of a group: renders, and the skinned renderers turned to recalculate their matrices (the most in one render, all together)
    static int shots, shotsForcedMost, shotsForced;

    /// <summary>One render of the view in memory (rows from the top, as the PNG). Every skinned renderer it may draw recalculates its skinning
    /// matrices at this render and gets its previous value back after it (ruled by the lead 04:4x): the engine computes them once per engine
    /// frame, and the batch harness steps the scene and renders many times within one, so a character whose bones moved was drawn as before.</summary>
    internal static Color32[] Shot(Host h, View v)
    {
        var cam = ViewCamera(h);
        var rot = Quaternion.Euler(v.Rot);
        cam.transform.SetPositionAndRotation(v.Pivot - rot * Vector3.forward * v.Distance, rot);
        cam.fieldOfView = v.Fov;
        var turned = new List<SkinnedMeshRenderer>();
        foreach (var s in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (s && !s.forceMatrixRecalculationPerRender) { s.forceMatrixRecalculationPerRender = true; turned.Add(s); }
        shots++;
        shotsForced += turned.Count;
        shotsForcedMost = Math.Max(shotsForcedMost, turned.Count);
        HS2Next.Screenshots.CaptureResult res;
        try
        {
            res = HS2Next.Screenshots.CaptureService.RenderSync(h.Rt, cam, new HS2Next.Screenshots.CaptureRequest
            {
                Width = v.W, Height = v.H, Aa = HS2Next.Screenshots.CaptureService.None, MaxPixels = HS2Next.Screenshots.CaptureLimits.HardMaxPixels,
            });
        }
        finally { foreach (var s in turned) if (s) s.forceMatrixRecalculationPerRender = false; }
        var t = new Texture2D(2, 2);
        try
        {
            t.LoadImage(res.Png);
            return t.GetPixels32();
        }
        finally { Object.DestroyImmediate(t); }
    }

    /// <summary>Shows only <paramref name="show"/> among <paramref name="all"/> (forceRenderingOff), every value restored by the returned scope.</summary>
    sealed class Only : IDisposable
    {
        readonly List<(Renderer r, bool off)> saved = new List<(Renderer, bool)>();
        public Only(IEnumerable<Renderer> all, ICollection<Renderer> show)
        {
            foreach (var r in all)
            {
                if (!r) continue;
                saved.Add((r, r.forceRenderingOff));
                r.forceRenderingOff = !show.Contains(r);
            }
        }
        public void Dispose() { foreach (var (r, off) in saved) if (r) r.forceRenderingOff = off; }
    }

    static int Diff(Color32 a, Color32 b) => Math.Max(Math.Abs(a.r - b.r), Math.Max(Math.Abs(a.g - b.g), Math.Abs(a.b - b.b)));

    /// <summary>The comparison of a source and its bake at one view.</summary>
    internal sealed class Compared
    {
        public string View;
        public int Pixels, Footprint, FootprintBake, Noise, Differ, Over2, Over8, Max;
        /// <summary>Pixels the bake draws where the source draws nothing (outside the mask).</summary>
        public int BakeOnly;
        /// <summary>The masked-out renderers' footprints: name → (pixels, over 2, over 8 in them).</summary>
        public readonly Dictionary<string, int[]> Excluded = new Dictionary<string, int[]>(StringComparer.Ordinal);
        public string ExcludedLine() => string.Join("; ", Excluded.Select(kv => $"{kv.Key} {kv.Value[0]} px ({kv.Value[1]} over 2, {kv.Value[2]} over 8)"));
        /// <summary>Differing pixels by the renderer drawn there: name → (any, over 2, over 8).</summary>
        public readonly Dictionary<string, int[]> ByRenderer = new Dictionary<string, int[]>(StringComparer.Ordinal);
        public double Over2Pct => Footprint > 0 ? 100.0 * Over2 / Footprint : 100.0;
        public double Over8Pct => Footprint > 0 ? 100.0 * Over8 / Footprint : 100.0;
        public double DifferPct => Footprint > 0 ? 100.0 * Differ / Footprint : 100.0;

        public string Line() =>
            $"{View}: footprint {Footprint} px (bake {FootprintBake}), source twice differ {Noise}; differ {Differ} ({F(DifferPct, "F3")} %), over 2 {Over2} ({F(Over2Pct, "F3")} %), over 8 {Over8} ({F(Over8Pct, "F4")} %), max {Max}";

        public string Named(int min = 0)
        {
            var sb = new StringBuilder();
            foreach (var kv in ByRenderer.OrderByDescending(k => k.Value[0]))
            {
                if (kv.Value[0] < min) continue;
                if (sb.Length > 0) sb.Append("; ");
                sb.Append(kv.Key).Append(' ').Append(kv.Value[0]).Append('/').Append(kv.Value[1]).Append('/').Append(kv.Value[2]);
            }
            return sb.Length == 0 ? "none" : sb.ToString();
        }
    }

    /// <summary>
    /// Renders the background, the source alone twice, the bake alone, compares in memory; then names every differing pixel by the
    /// renderer drawn there (the source's renderers first, each alone; a pixel no source renderer covers by the bake's).
    /// </summary>
    internal static Compared Compare(Host h, View v, IList<Renderer> source, IList<Renderer> bake, bool name = true, IList<Renderer> scene = null,
        IList<Renderer> maskOut = null)
    {
        var all = source.Concat(bake).Concat(scene ?? Array.Empty<Renderer>()).Concat(maskOut ?? Array.Empty<Renderer>()).Where(r => r).Distinct().ToList();
        var none = new HashSet<Renderer>();
        Color32[] bg, a, a2, b;
        using (new Only(all, none)) bg = Shot(h, v);
        var src = new HashSet<Renderer>(source.Where(r => r));
        using (new Only(all, src)) { a = Shot(h, v); a2 = Shot(h, v); }
        var bk = new HashSet<Renderer>(bake.Where(r => r));
        using (new Only(all, bk)) b = Shot(h, v);
        var c = new Compared { View = v.Name, Pixels = a.Length };
        // the masked-out renderers' footprints (each drawn alone) leave the comparison; their pixels are counted apart
        bool[] mask = null;
        if (maskOut != null && maskOut.Count > 0)
        {
            mask = new bool[a.Length];
            var bakeSet = new HashSet<Renderer>(bake.Where(r => r));
            foreach (var r in maskOut.Where(x => x).Distinct())
            {
                Color32[] alone;
                using (new Only(all, new HashSet<Renderer> { r })) alone = Shot(h, v);
                int px = 0, m2 = 0, m8 = 0;
                for (int i = 0; i < a.Length; i++)
                {
                    if (Diff(alone[i], bg[i]) == 0) continue;
                    mask[i] = true;
                    px++;
                    int d = Diff(a[i], b[i]);
                    if (d > 2) m2++;
                    if (d > 8) m8++;
                }
                c.Excluded[(bakeSet.Contains(r) ? "bake:" : "") + r.name] = new[] { px, m2, m8 };
            }
        }
        var differ = new List<int>();
        for (int i = 0; i < a.Length; i++)
        {
            if (mask != null && mask[i]) continue;
            if (Diff(b[i], bg[i]) > 0 && Diff(a[i], bg[i]) == 0) c.BakeOnly++;
            if (Diff(a[i], bg[i]) > 0) c.Footprint++;
            if (Diff(b[i], bg[i]) > 0) c.FootprintBake++;
            if (Diff(a[i], a2[i]) > 0) c.Noise++;
            int d = Diff(a[i], b[i]);
            if (d > 0) { c.Differ++; differ.Add(i); }
            if (d > 2) c.Over2++;
            if (d > 8) c.Over8++;
            if (d > c.Max) c.Max = d;
        }
        if (!name || differ.Count == 0) return c;
        // attribution: each renderer alone; the one whose colour at the pixel is nearest the full render's
        var best = new int[differ.Count];
        var bestName = new string[differ.Count];
        for (int k = 0; k < best.Length; k++) best[k] = int.MaxValue;
        void Attribute(IList<Renderer> list, Color32[] full, string prefix)
        {
            foreach (var r in list)
            {
                if (!r) continue;
                Color32[] alone;
                using (new Only(all, new HashSet<Renderer> { r })) alone = Shot(h, v);
                for (int k = 0; k < differ.Count; k++)
                {
                    int i = differ[k];
                    if (Diff(alone[i], bg[i]) == 0) continue;
                    int dd = Diff(alone[i], full[i]);
                    if (dd < best[k]) { best[k] = dd; bestName[k] = prefix + r.name; }
                }
            }
        }
        Attribute(source, a, "");
        bool rest = bestName.Any(x => x == null);
        if (rest)
        {
            var save = (int[])best.Clone();
            for (int k = 0; k < best.Length; k++) if (bestName[k] != null) best[k] = -1;   // keep the source's attribution
            Attribute(bake, b, "bake:");
            for (int k = 0; k < best.Length; k++) if (best[k] == -1) best[k] = save[k];
        }
        for (int k = 0; k < differ.Count; k++)
        {
            int i = differ[k];
            string n = bestName[k] ?? "(no renderer)";
            if (!c.ByRenderer.TryGetValue(n, out var cnt)) c.ByRenderer[n] = cnt = new int[3];
            int d = Diff(a[i], b[i]);
            cnt[0]++;
            if (d > 2) cnt[1]++;
            if (d > 8) cnt[2]++;
        }
        return c;
    }

    // ------------------------------------------------------------ a bake made into an item of the scene (the app's path)

    /// <summary>A bake of a character as the scene holds it: the blobs in the scene's res/ (and the store when asked), an item node with
    /// the bake block at the item's pose, built by the bake source through the item path.</summary>
    internal sealed class BakeItem
    {
        public BakeJob Job;
        public string Key;
        public ItemRealization It;
        public int? No;
        public List<Renderer> Renderers => It != null && It.Instance ? It.Instance.GetComponentsInChildren<Renderer>(true).ToList() : new List<Renderer>();
    }

    /// <summary>The state of a new bake item (the defaults of an item without a prefab, the app's group).</summary>
    internal static ItemNodeState BakeState(ItemBake block, int? no, string name)
    {
        var row = new HS2Next.Content.Studio.StudioItem
        {
            Key = new HS2Next.Content.ContentId(HS2Next.Content.Studio.AppItemRows.BakeGuid, no ?? 0), Group = HS2Next.Content.Studio.AppItemRows.Group,
            Category = HS2Next.Content.Studio.AppItemRows.BakesCategory, Name = name ?? "", BundlePath = "", FileName = "", Manifest = "",
        };
        var st = ItemDefaults.FromPrefab(null, row);
        st.Bake = block;
        return st;
    }

    /// <summary>Adds an item node of a finished job at its pose; the realization builds it (the prototype the job seeded).</summary>
    internal static BakeItem AddItem(Host h, BakeJob job, int? no, string name)
    {
        var bi = new BakeItem { Job = job, No = no };
        var r = h.Session.AddItem(BakeState(job.Block(no), no, name));
        if (!Check("the bake's item node is added", r.Ok, r.Code + " " + r.Message)) return bi;
        bi.Key = r.NewId;
        var cap = job.Capture;
        h.Session.Set(bi.Key, "transform.pos", new Vec3(cap.Position.x, cap.Position.y, cap.Position.z));
        var e = cap.Rotation.eulerAngles;
        h.Session.Set(bi.Key, "transform.rot", new Vec3(e.x, e.y, e.z));
        h.Session.Set(bi.Key, "transform.scale", new Vec3(cap.Scale.x, cap.Scale.y, cap.Scale.z));
        h.Rt.ApplyNow();
        bi.It = h.Rt.Realizations.OfType<ItemRealization>().FirstOrDefault(x => x.NodeId == bi.Key);
        return bi;
    }

    /// <summary>Bakes a character (capture, encode, the prototype), puts its blobs in the scene (and a store row when <paramref name="store"/>),
    /// and adds its item.</summary>
    internal static BakeItem BakeToItem(Host h, CharaRealization c, BakeOptions o, bool store, string name, bool keepTextures = false)
    {
        var job = new BakeJob { KeepTextureData = keepTextures };
        job.Capture = BakeCapture.Chara(c, o, job);
        job.Finish();
        job.Build(BakeCache.Of(h.Rt, true));
        job.AddTo(h.Rt.Session.Model.File.Res);
        int? no = null;
        if (store)
        {
            var s = BakeProvider.StoreOf(h.Rt);
            var row = s.Add(job.Row(name), job.AllBlobs());
            no = row.Number;
        }
        return AddItem(h, job, no, name);
    }

    /// <summary>Steps frames until the blink is open (the eyes as a capture would hold them), at most 600; false when it never was.</summary>
    static bool EyesOpen(Host h, CharaRealization c)
    {
        var s = c.Motion ? c.Motion.Get<HS2Next.Motion.Face.ExpressionStage>() : null;
        if (s == null) return true;
        for (int i = 0; i < 600; i++)
        {
            if (s.Blink.FixedFlags != 0 || s.Blink.OpenRate >= 1f) return true;
            h.Frames(1);
        }
        return false;
    }

    static Transform FaceOf(CharaRealization c)
    {
        var ctx = c.Context;
        if (ctx?.HeadBones != null && ctx.HeadBones.TryGetValue("cf_J_FaceRoot", out var f) && f) return f;
        return ctx?.HeadBone ? ctx.HeadBone.transform : null;
    }

    /// <summary>The test's own store folder (under Spike/out/bake/userdata), emptied: its files are deleted by name, never a tree.</summary>
    static void ClearStore()
    {
        foreach (var dir in new[] { Path.Combine(UserDataDir, "bakes"), Path.Combine(UserDataDir, "bakes.away") })
        {
            if (!Directory.Exists(dir)) continue;
            var blobs = Path.Combine(dir, "blobs");
            if (Directory.Exists(blobs))
            {
                foreach (var f in Directory.GetFiles(blobs)) File.Delete(f);
                Directory.Delete(blobs, false);
            }
            foreach (var f in Directory.GetFiles(dir)) File.Delete(f);
            Directory.Delete(dir, false);
        }
    }

    // ------------------------------------------------------------ BD1, BD5, BB1

    static void Render_Look()
    {
        Group("BD1", "a whole bake against its character (full and face views, no AA)", Bd1);
        Group("BD5", "the residue named by renderer; the three-channel textures built", Bd5);
        Group("BB1", "the order of the sources; a bake item builds with the store folder moved away", Bb1);
    }

    /// <summary>What BD1 saw, for BD5: per target and view the residue by renderer; the three-channel textures and how they were built.</summary>
    static readonly List<Compared> bd1Seen = new List<Compared>();
    static int bd1Three, bd1ThreeBuilt, bd1Unnamed;

    static void Bd1()
    {
        bd1Seen.Clear(); bd1Three = bd1ThreeBuilt = bd1Unnamed = 0;
        foreach (bool male in new[] { false, true })
        {
            string who = male ? "HERO107" : "BR-Chan";
            var h = NewHost();
            BakeItem bi = null;
            try
            {
                string key = LoadChara(h, male);
                var c = Chara(h, key);
                if (!Check($"BD1 {who}: the character is built", c != null && c.State == RealizationState.Built)) continue;
                Check($"BD1 {who}: the eyes are open at the capture", EyesOpen(h, c));
                bi = BakeToItem(h, c, new BakeOptions(), false, who);
                var it = bi.It;
                if (!Check($"BD1 {who}: the bake item is built from its bake", it != null && it.State == RealizationState.Built && it.Source == "bake" && !it.IsFallback,
                    it == null ? "no realization" : $"state {it.State}, source {it.Source}, fallback {it.IsFallback}")) continue;
                var cap = bi.Job.Capture;
                var proto = BakeCache.PrototypeOf(it);
                Info($"{who}: capture {F(cap.Ms.TryGetValue("capture", out var cm) ? cm : -1, "F0")} ms (textures {F(cap.Ms.TryGetValue("textures", out var tm) ? tm : -1, "F0")}, meshes {F(cap.Ms.TryGetValue("meshes", out var mm) ? mm : -1, "F0")}), " +
                     $"finish {F(bi.Job.Ms.TryGetValue("finish", out var fm) ? fm : -1, "F0")} ms, encode wait {F(bi.Job.Ms.TryGetValue("encodeWait", out var ew) ? ew : 0, "F0")} ms; nodes {cap.Bake.Nodes.Count}, renderers {cap.Bake.Renderers.Count}, " +
                     $"meshes {cap.Bake.Meshes.Count}, materials {cap.Bake.Materials.Count}, textures {cap.Bake.Textures.Count}, FK {cap.Bake.Fk.Count}, members {cap.Count("members")}, " +
                     $"texels {bi.Job.TextureBytes / (1 << 20)} MiB (largest {bi.Job.LargestTexture / (1 << 20)} MiB, peak in flight {bi.Job.PeakInFlight / (1 << 20)} MiB), blobs {bi.Job.Blobs.Count + 1} {F(bi.Job.Bytes / 1048576.0, "F1")} MiB, " +
                     $"prototype {F(proto?.BuildMs ?? -1, "F0")} ms, video {(proto?.VideoBytes ?? 0) / (1 << 20)} MiB (computed from formats)");
                Info($"{who}: routes " + string.Join(", ", cap.Counts.Where(kv => kv.Key.StartsWith("tex.route") || kv.Key.StartsWith("mesh.")).Select(kv => kv.Key + " " + kv.Value)));
                foreach (var f in cap.Findings) Info($"{who}: finding {f.Severity} {f.Code} {f.Path}: {f.Message}");
                if (proto != null) foreach (var f in proto.Findings) Info($"{who}: build finding {f.Severity} {f.Code} {f.Path}: {f.Message}");
                bd1Three += cap.Count("tex.route.rgba");
                foreach (var t in cap.Bake.Textures)
                    if (t.Route == BakeTextures.RouteThree && t.Built && t.Built.graphicsFormat.ToString().StartsWith("R8G8B8A8")) bd1ThreeBuilt++;
                var src = cap.Live.Where(r => r).ToList();
                var root = c.Root.transform;
                foreach (var v in new[] { FullView(root), FaceView(root, FaceOf(c)) })
                {
                    var r = Compare(h, v, src, bi.Renderers);
                    r.View = who + " " + v.Name;
                    bd1Seen.Add(r);
                    Measure($"BD1 {r.Line()}");
                    Check($"BD1 {who} {v.Name}: present: footprint >= 100,000 px and the source rendered twice differs by 0", r.Footprint >= 100000 && r.Noise == 0, $"footprint {r.Footprint}, twice {r.Noise}");
                    Check($"BD1 {who} {v.Name}: pixels over 2 levels <= 0.2 % of the footprint", r.Over2Pct <= 0.2, $"{r.Over2} px, {F(r.Over2Pct, "F4")} %");
                    Check($"BD1 {who} {v.Name}: pixels over 8 levels <= 0.01 % of the footprint", r.Over8Pct <= 0.01, $"{r.Over8} px, {F(r.Over8Pct, "F4")} %");
                    Info($"BD1 {who} {v.Name}: by renderer (any/over 2/over 8): {r.Named()}");
                    if (r.Over8 > 0) Info($"BD1 {who} {v.Name}: over 8 by renderer: " + string.Join("; ", r.ByRenderer.Where(kv => kv.Value[2] > 0).Select(kv => kv.Key + " " + kv.Value[2])));
                    if (r.ByRenderer.TryGetValue("(no renderer)", out var un)) bd1Unnamed += un[0];
                }
            }
            finally { h.Dispose(); bi?.Job?.Dispose(); }
        }
    }

    static void Bd5()
    {
        if (!Check("BD5 BD1 ran on both targets and both views", bd1Seen.Count == 4, bd1Seen.Count + " views")) return;
        int residue = bd1Seen.Sum(r => r.Differ);
        foreach (var r in bd1Seen) Info($"BD5 {r.View}: {r.Differ} px differ: {r.Named()}");
        Check("BD5 every differing pixel is named by a renderer", bd1Unnamed == 0, bd1Unnamed + " px without a renderer of the source or the bake");
        Check("BD5 the three-channel textures are built (as four channels)", bd1Three > 0 && bd1ThreeBuilt == bd1Three, $"{bd1ThreeBuilt} of {bd1Three} built");
        Check("BD5 present: the residue is > 0 (as S0-3's)", residue > 0, residue + " px over the four views");
    }

    /// <summary>BB1: a bake item of a store row, saved; a fresh host with the store folder moved away opens the scene and builds the item
    /// from the scene's res/ alone, drawn as the session's item (in bits); the order (a node with a bake block is the bake, even with
    /// a store row's reference).</summary>
    static void Bb1()
    {
        ClearStore();
        string scenePath = null;
        Color32[] before = null;
        View view = default;
        string key = null, mainSha = null;
        int? no = null;
        var h = NewHost();
        try
        {
            string ck = LoadChara(h, false);
            var c = Chara(h, ck);
            EyesOpen(h, c);
            var bi = BakeToItem(h, c, new BakeOptions(), true, "BB1");
            key = bi.Key; no = bi.No; mainSha = bi.Job.MainSha;
            var store = BakeProvider.StoreOf(h.Rt);
            var row = no.HasValue ? store.Find(no.Value) : null;
            Check("BB1 present: the store held the row before", row != null && row.Main == mainSha && store.HasBlob(mainSha), row == null ? "no row" : $"row {row.Number}, {row.Data.Count} data blobs");
            Check("BB1 the item is drawn from its bake (a store row's reference and a bake block: the bake)", bi.It?.Source == "bake" && !bi.It.IsFallback, bi.It?.Source);
            view = FullView(c.Root.transform);
            var all = h.Rt.Realizations.SelectMany(r => r is ItemRealization ir && ir.Instance ? ir.Instance.GetComponentsInChildren<Renderer>(true) : r is CharaRealization cr && cr.Root ? cr.Root.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>()).ToList();
            using (new Only(all, new HashSet<Renderer>(bi.Renderers))) before = Shot(h, view);
            var sr = h.Ok(new { cmd = "scene.save", name = "bb1_bake", overwrite = true, thumb = "placeholder" }, "BB1 scene.save");
            scenePath = (string)sr["path"];
            bi.Job.Dispose();
        }
        finally { h.Dispose(); }
        if (scenePath == null) return;
        string bakes = Path.Combine(UserDataDir, "bakes"), away = Path.Combine(UserDataDir, "bakes.away");
        Directory.Move(bakes, away);
        var h2 = NewHost();
        try
        {
            int loads = 0;
            var lr = h2.Ok(new { cmd = "scene.load", path = scenePath }, "BB1 scene.load with the store folder moved away");
            h2.Rt.ApplyNow();
            var findings = (lr["findings"] as Newtonsoft.Json.Linq.JArray)?.Select(x => (string)x["code"]).ToList() ?? new List<string>();
            var it = h2.Rt.Realizations.OfType<ItemRealization>().FirstOrDefault(x => x.NodeId == key);
            Check("BB1 the store folder is away", !Directory.Exists(bakes) && Directory.Exists(away));
            Check("BB1 the bake item builds from the scene's res/ alone", it != null && it.State == RealizationState.Built && it.Source == "bake" && !it.IsFallback && (BakeCache.Of(h2.Rt, false)?.Loads ?? 0) == loads + 1,
                it == null ? "no item" : $"state {it.State}, source {it.Source}, fallback {it.IsFallback}, built from blobs {(BakeCache.Of(h2.Rt, false)?.Loads ?? 0) - loads}");
            var rtFindings = h2.Rt.Findings.Where(f => f.Code.StartsWith("bake.")).Select(f => f.Code + " " + f.Message).ToList();
            Check("BB1 no bake finding at the load", rtFindings.Count == 0 && !findings.Any(f => f.StartsWith("bake.")), string.Join("; ", rtFindings.Concat(findings)));
            var proto = BakeCache.PrototypeOf(it);
            Check("BB1 the loaded prototype is the main's", proto != null && proto.Main == mainSha, proto?.Main);
            var rends = it != null && it.Instance ? it.Instance.GetComponentsInChildren<Renderer>(true).ToList() : new List<Renderer>();
            var all2 = h2.Rt.Realizations.SelectMany(r => r is ItemRealization ir && ir.Instance ? ir.Instance.GetComponentsInChildren<Renderer>(true) : r is CharaRealization cr && cr.Root ? cr.Root.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>()).ToList();
            Color32[] after, bg2;
            using (new Only(all2, new HashSet<Renderer>(rends))) after = Shot(h2, view);
            using (new Only(all2, new HashSet<Renderer>())) bg2 = Shot(h2, view);
            int differ = 0, foot = 0;
            for (int i = 0; i < after.Length && i < before.Length; i++)
            {
                if (Diff(after[i], before[i]) > 0) differ++;
                if (Diff(after[i], bg2[i]) > 0) foot++;
            }
            Check("BB1 the loaded bake draws as the session's bake, bit for bit", before != null && after.Length == before.Length && differ == 0 && foot >= 100000,
                $"{differ} px differ of {after.Length}; the bake's footprint {foot} px");
        }
        finally
        {
            h2.Dispose();
            if (Directory.Exists(away) && !Directory.Exists(bakes)) Directory.Move(away, bakes);
            ClearStore();
            try { if (File.Exists(scenePath)) File.Delete(scenePath); } catch (Exception) { }
        }
    }

    // ------------------------------------------------------------ helpers of the data checks

    /// <summary>The nodes of a built bake in the capture's order (depth-first, children in order), inside copies left out.</summary>
    internal static List<Transform> BakeNodes(Transform root)
    {
        var l = new List<Transform>();
        var st = new Stack<Transform>();
        st.Push(root);
        while (st.Count > 0)
        {
            var t = st.Pop();
            l.Add(t);
            for (int k = t.childCount - 1; k >= 0; k--)
            {
                var c = t.GetChild(k);
                if (c.name.EndsWith(BakeInsides.Suffix, StringComparison.Ordinal)) continue;
                st.Push(c);
            }
        }
        return l;
    }

    /// <summary>The built renderer of each of the bake's renderers (by its node), in BakeData.Renderers order.</summary>
    internal static List<Renderer> BuiltRenderers(BakeData b, GameObject instance)
    {
        var nodes = BakeNodes(instance.transform);
        var l = new List<Renderer>();
        foreach (var r in b.Renderers) l.Add(r.Node < nodes.Count ? nodes[r.Node].GetComponent<Renderer>() : null);
        return l;
    }

    static byte[] ReadLevel(Texture t, int level)
    {
        var rq = AsyncGPUReadback.Request(t, level);
        rq.WaitForCompletion();
        return rq.hasError ? null : rq.GetData<byte>().ToArray();
    }

    static bool Same(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    static int Mips(Texture t) => t is RenderTexture rt ? (rt.useMipMap ? Math.Max(1, rt.mipmapCount) : 1) : Math.Max(1, t.mipmapCount);

    // ------------------------------------------------------------ BD3, BD4, BD11

    static void Render_Data()
    {
        Group("BD3", "textures per route: read back bit-exact, decoded equal to a draw of the source, three channels expanded, the sRGB flag", Bd3);
        Group("BD4", "materials: every declared value by type, the property blocks, keywords, queue, passes, tag", Bd4);
        Group("BD11", "light: layers and rendering layers per renderer; the character light reaches the bake", Bd11);
        Group("BD15", "the own route: a readable skinned mesh whose blend shapes all weigh 0 is stored as its own data, bit for bit; the routes counted", Bd15);
    }

    static void Bd3()
    {
        var routes = new Dictionary<string, int>(StringComparer.Ordinal);
        var bad = new Dictionary<string, List<string>>(StringComparer.Ordinal) { ["own"] = new List<string>(), ["rgba"] = new List<string>(), ["decode"] = new List<string>(), ["srgb"] = new List<string>(), ["built"] = new List<string>(), ["levels"] = new List<string>() };
        long levelsCompared = 0;
        foreach (bool male in new[] { false, true })
        {
            string who = male ? "HERO107" : "BR-Chan";
            var h = NewHost();
            BakeJob job = null;
            try
            {
                var c = Chara(h, LoadChara(h, male));
                job = BakeJob.OfChara(c, new BakeOptions());
                job.Finish();
                var b = job.Capture.Bake;
                for (int i = 0; i < b.Textures.Count; i++)
                {
                    var tr = b.Textures[i];
                    var src = tr.Source;
                    string name = who + " " + tr.Name + " (" + tr.SourceFormat + ")";
                    if (!src || tr.Blob == null || !job.Blobs.TryGetValue(tr.Blob, out var blob)) { bad["built"].Add(name + ": no source or blob"); continue; }
                    routes.TryGetValue(tr.Route, out int rn); routes[tr.Route] = rn + 1;
                    var d = BakeTextureData.FromBlob(blob);
                    int mips = Mips(src);
                    if (d.Levels.Count != mips) bad["levels"].Add($"{name}: {d.Levels.Count} levels of {mips}");
                    // the texture as rebuilt from its blob: its levels read back equal the blob's
                    var built = BakeTextures.Build(BakeBlobs.FromTexture(d), tr);
                    try
                    {
                        if (built.isDataSRGB != src.isDataSRGB) bad["srgb"].Add($"{name}: built {built.isDataSRGB}, source {src.isDataSRGB}");
                        for (int l = 0; l < d.Levels.Count; l++)
                            if (!Same(ReadLevel(built, l), d.Levels[l])) { bad["built"].Add($"{name} level {l}"); break; }
                    }
                    finally { Object.DestroyImmediate(built); }
                    // the blob against the source, per route
                    if (tr.Route == BakeTextures.RouteOwn)
                    {
                        for (int l = 0; l < Math.Min(mips, d.Levels.Count); l++, levelsCompared++)
                            if (!Same(ReadLevel(src, l), d.Levels[l])) { bad["own"].Add($"{name} level {l}"); break; }
                    }
                    else if (tr.Route == BakeTextures.RouteThree)
                    {
                        for (int l = 0; l < Math.Min(mips, d.Levels.Count); l++, levelsCompared++)
                        {
                            var raw = ReadLevel(src, l);
                            int w = Math.Max(1, src.width >> l), hh = Math.Max(1, src.height >> l), n = w * hh;
                            var lv = d.Levels[l];
                            bool ok = raw != null && lv.Length == n * 4;
                            int stride = raw != null && raw.Length >= n * 4 ? 4 : 3;
                            bool bgr = tr.SourceFormat.StartsWith("B8G8R8");
                            for (int p = 0; ok && p < n; p++)
                                ok = lv[4 * p] == raw[stride * p + (bgr ? 2 : 0)] && lv[4 * p + 1] == raw[stride * p + 1] && lv[4 * p + 2] == raw[stride * p + (bgr ? 0 : 2)] && lv[4 * p + 3] == 255;
                            if (!ok) { bad["rgba"].Add($"{name} level {l}"); break; }
                        }
                    }
                    else
                    {
                        // a draw of the source, level by level, made again now
                        var again = BakeTextures.Capture(src, null, out _, out _);
                        if (again == null || again.Levels.Count != d.Levels.Count) { bad["decode"].Add($"{name}: drawn again {again?.Levels.Count ?? -1} levels"); continue; }
                        for (int l = 0; l < d.Levels.Count; l++, levelsCompared++)
                            if (!Same(again.Levels[l], d.Levels[l])) { bad["decode"].Add($"{name} level {l}"); break; }
                    }
                }
            }
            finally { h.Dispose(); job?.Dispose(); }
        }
        string Few(List<string> l) => l.Count == 0 ? "" : string.Join("; ", l.Take(6)) + (l.Count > 6 ? " …" : "");
        Info("BD3 routes: " + string.Join(", ", routes.Select(kv => kv.Key + " " + kv.Value)) + $"; levels compared {levelsCompared}");
        Check("BD3 present: each route has >= 1 texture on the targets", routes.TryGetValue(BakeTextures.RouteOwn, out int a1) && a1 > 0 && routes.TryGetValue(BakeTextures.RouteThree, out int a2) && a2 > 0 && routes.TryGetValue(BakeTextures.RouteDecode, out int a3) && a3 > 0,
            string.Join(", ", routes.Select(kv => kv.Key + " " + kv.Value)));
        Check("BD3 read back in the own format: every level bit-exact", bad["own"].Count == 0, Few(bad["own"]));
        Check("BD3 three channels: expanded to four with alpha 255, bit-exact", bad["rgba"].Count == 0, Few(bad["rgba"]));
        Check("BD3 decoded: every level equal to a draw of the source", bad["decode"].Count == 0, Few(bad["decode"]));
        Check("BD3 the sRGB flag of the built texture = the source's isDataSRGB", bad["srgb"].Count == 0, Few(bad["srgb"]));
        Check("BD3 every level of the source's chain stored, the built texture's levels equal the blob's", bad["levels"].Count == 0 && bad["built"].Count == 0, Few(bad["levels"].Concat(bad["built"]).ToList()));
    }

    static void Bd4()
    {
        var h = NewHost();
        BakeItem bi = null;
        Renderer fixture = null;
        try
        {
            var c = Chara(h, LoadChara(h, false));
            EyesOpen(h, c);
            // the fixture (ruled by the lead 03:4x): a renderer block on the top's first renderer for the first colour property its slot-0 shader
            // declares, a value other than the material's
            var top = c.Context.Clothes[0] ? c.Context.Clothes[0].GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.enabled && r.sharedMaterial && r.sharedMaterial.shader) : null;
            int blockDiffers = 0;
            string fixtureText = "no enabled renderer of the top";
            if (top)
            {
                var m = top.sharedMaterial;
                var sh = m.shader;
                string prop = null;
                for (int p = 0; p < sh.GetPropertyCount() && prop == null; p++) if (sh.GetPropertyType(p) == ShaderPropertyType.Color) prop = sh.GetPropertyName(p);
                fixtureText = top.name + ": its slot-0 shader " + sh.name + " declares no colour";
                if (prop != null)
                {
                    var had = m.GetColor(prop);
                    var want = new Color(0.2f, 0.6f, 0.9f, 1f);
                    if (want == had) want = new Color(0.9f, 0.6f, 0.2f, 1f);
                    var blk = new MaterialPropertyBlock();
                    top.GetPropertyBlock(blk);
                    blk.SetColor(prop, want);
                    top.SetPropertyBlock(blk);
                    fixture = top;
                    if (had != want) blockDiffers++;
                    fixtureText = $"{top.name} {sh.name}.{prop}: material {C4(had)}, block {C4(want)}";
                }
            }
            bi = BakeToItem(h, c, new BakeOptions(), false, "bd4");
            var b = bi.Job.Capture.Bake;
            var built = BuiltRenderers(b, bi.It.Instance);
            int compared = 0, slots = 0, members = 0;
            var bad = new List<string>();
            var rb = new MaterialPropertyBlock();
            var sb = new MaterialPropertyBlock();
            for (int i = 0; i < b.Renderers.Count; i++)
            {
                var src = bi.Job.Capture.Live[i];
                var dst = built[i];
                if (!src || !dst) { bad.Add("renderer " + i + " missing"); continue; }
                if (b.Renderers[i].Member) { members++; continue; }
                var sm = src.sharedMaterials;
                var dm = dst.sharedMaterials;
                bool hasBlock = src.HasPropertyBlock();
                for (int k = 0; k < sm.Length && k < dm.Length; k++)
                {
                    var a = sm[k];
                    var d = dm[k];
                    if (!a || !a.shader) continue;
                    slots++;
                    if (!d || !d.shader || d.shader.name != a.shader.name) { bad.Add($"{src.name}[{k}] shader {(d && d.shader ? d.shader.name : "none")} for {a.shader.name}"); continue; }
                    MaterialPropertyBlock r1 = null, s1 = null;
                    if (hasBlock)
                    {
                        src.GetPropertyBlock(rb); if (!rb.isEmpty) r1 = rb;
                        src.GetPropertyBlock(sb, k); if (!sb.isEmpty) s1 = sb;
                    }
                    var sh = a.shader;
                    for (int p = 0; p < sh.GetPropertyCount(); p++)
                    {
                        string n = sh.GetPropertyName(p);
                        bool ok = true;
                        compared++;
                        switch (sh.GetPropertyType(p))
                        {
                            case ShaderPropertyType.Float:
                            case ShaderPropertyType.Range:
                            {
                                float e = s1 != null && s1.HasFloat(n) ? s1.GetFloat(n) : r1 != null && r1.HasFloat(n) ? r1.GetFloat(n) : a.GetFloat(n);
                                ok = BitsEqual(e, d.GetFloat(n));
                                break;
                            }
                            case ShaderPropertyType.Int:
                            {
                                int e = s1 != null && s1.HasInteger(n) ? s1.GetInteger(n) : r1 != null && r1.HasInteger(n) ? r1.GetInteger(n) : a.GetInteger(n);
                                ok = e == d.GetInteger(n);
                                break;
                            }
                            case ShaderPropertyType.Color:
                            {
                                Color e = s1 != null && s1.HasColor(n) ? s1.GetColor(n) : r1 != null && r1.HasColor(n) ? r1.GetColor(n) : a.GetColor(n);
                                var g = d.GetColor(n);
                                ok = BitsEqual(e.r, g.r) && BitsEqual(e.g, g.g) && BitsEqual(e.b, g.b) && BitsEqual(e.a, g.a);
                                if (!ok && bad.Count < 8)
                                {
                                    // where: the values as read (GetColor) and as held (GetVector), the source's block or material
                                    string from = s1 != null && s1.HasColor(n) ? "slot block" : r1 != null && r1.HasColor(n) ? "renderer block" : "material";
                                    Info($"BD4 {src.name}[{k}].{n} ({from}): source {C4(e)} held {V4(a.GetVector(n))}; built {C4(g)} held {V4(d.GetVector(n))}");
                                }
                                break;
                            }
                            case ShaderPropertyType.Vector:
                            {
                                Vector4 e = s1 != null && s1.HasVector(n) ? s1.GetVector(n) : r1 != null && r1.HasVector(n) ? r1.GetVector(n) : a.GetVector(n);
                                var g = d.GetVector(n);
                                ok = BitsEqual(e.x, g.x) && BitsEqual(e.y, g.y) && BitsEqual(e.z, g.z) && BitsEqual(e.w, g.w);
                                break;
                            }
                            case ShaderPropertyType.Texture:
                            {
                                var e = s1 != null && s1.HasTexture(n) ? s1.GetTexture(n) : r1 != null && r1.HasTexture(n) ? r1.GetTexture(n) : a.GetTexture(n);
                                var g = d.GetTexture(n);
                                bool kind = e && e.dimension != TextureDimension.Tex2D;
                                ok = kind ? !g : (!e) == (!g) && (!e || (e.width == g.width && e.height == g.height && e.name == g.name));
                                ok &= a.GetTextureScale(n) == d.GetTextureScale(n) && a.GetTextureOffset(n) == d.GetTextureOffset(n) || (s1 != null && s1.HasVector(n + "_ST")) || (r1 != null && r1.HasVector(n + "_ST"));
                                break;
                            }
                        }
                        if (!ok) bad.Add($"{src.name}[{k}] {a.shader.name}.{n}");
                    }
                    var ka = new HashSet<string>(a.shaderKeywords ?? Array.Empty<string>());
                    var kd = new HashSet<string>(d.shaderKeywords ?? Array.Empty<string>());
                    if (!ka.SetEquals(kd)) bad.Add($"{src.name}[{k}] keywords [{string.Join(" ", ka.OrderBy(x => x))}] vs [{string.Join(" ", kd.OrderBy(x => x))}]");
                    if (a.renderQueue != d.renderQueue) bad.Add($"{src.name}[{k}] queue {a.renderQueue} vs {d.renderQueue}");
                    if (a.GetTag("RenderType", false, "") != d.GetTag("RenderType", false, "")) bad.Add($"{src.name}[{k}] RenderType");
                    var lm = new ShaderTagId("LightMode");
                    for (int pi = 0; pi < a.passCount; pi++)
                    {
                        string mode = sh.FindPassTagValue(pi, lm).name;
                        if (string.IsNullOrEmpty(mode)) mode = a.GetPassName(pi);
                        if (a.GetShaderPassEnabled(mode) != d.GetShaderPassEnabled(mode)) bad.Add($"{src.name}[{k}] pass {mode}");
                    }
                }
            }
            Info($"BD4 compared {compared} values on {slots} slots ({members} member renderers left to BV2)");
            Check("BD4 present: >= 1 block value differs from its material's", fixture && blockDiffers > 0 && b.Materials.Count > 0, fixtureText);
            Check("BD4 every declared value equal by type (the block's where it has one); keywords, queue, passes, tag", bad.Count == 0 && compared > 0,
                bad.Count + " differ" + (bad.Count > 0 ? ": " + string.Join("; ", bad.Take(8)) : ""));
        }
        finally
        {
            if (fixture) fixture.SetPropertyBlock(null);
            h.Dispose();
            bi?.Job?.Dispose();
        }
    }

    static string C4(Color c) => $"({c.r:R}, {c.g:R}, {c.b:R}, {c.a:R})";
    static string V4(Vector4 c) => $"({c.x:R}, {c.y:R}, {c.z:R}, {c.w:R})";

    static bool BitsEqual(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b) || (a == 0f && b == 0f);

    static void Bd11()
    {
        var h = NewHost();
        BakeItem bi = null;
        try
        {
            var c = Chara(h, LoadChara(h, false));
            EyesOpen(h, c);
            bi = BakeToItem(h, c, new BakeOptions(), false, "bd11");
            var b = bi.Job.Capture.Bake;
            var built = BuiltRenderers(b, bi.It.Instance);
            int layers = 0, masks = 0, n = 0;
            for (int i = 0; i < b.Renderers.Count; i++)
            {
                var s = bi.Job.Capture.Live[i];
                var d = built[i];
                if (!s || !d) continue;
                n++;
                if (s.gameObject.layer != d.gameObject.layer) layers++;
                if (s.renderingLayerMask != d.renderingLayerMask) masks++;
            }
            Check("BD11 every renderer keeps its layer and rendering-layer mask", n == b.Renderers.Count && layers == 0 && masks == 0, $"{n} renderers, layers differ {layers}, masks differ {masks}; the source's layer {bi.Job.Capture.Live[0].gameObject.layer}");
            var v = FullView(c.Root.transform);
            var all = SourceAndBake(h);
            Color32[] lit, dark;
            var bake = new HashSet<Renderer>(bi.Renderers);
            using (new Only(all, bake)) lit = Shot(h, v);
            var saved = bi.Renderers.Select(r => (r, r.gameObject.layer, r.renderingLayerMask)).ToList();
            foreach (var r in bi.Renderers) { r.gameObject.layer = 0; r.renderingLayerMask = 1u; }
            try { using (new Only(all, bake)) dark = Shot(h, v); }
            finally { foreach (var (r, l, m) in saved) { r.gameObject.layer = l; r.renderingLayerMask = m; } }
            int darker = 0;
            for (int i = 0; i < lit.Length; i++)
            {
                int a = lit[i].r + lit[i].g + lit[i].b, z = dark[i].r + dark[i].g + dark[i].b;
                if (z + 6 < a) darker++;
            }
            Check("BD11 present: forced to layer 0 the bake renders darker on >= 1,000 px (the character light reaches its own layer)", darker >= 1000, darker + " px darker");
        }
        finally { h.Dispose(); bi?.Job?.Dispose(); }
    }

    /// <summary>Every renderer of the host's characters and items.</summary>
    static List<Renderer> SourceAndBake(Host h) =>
        h.Rt.Realizations.SelectMany(r => r is ItemRealization ir && ir.Instance ? ir.Instance.GetComponentsInChildren<Renderer>(true)
            : r is CharaRealization cr && cr.Root ? cr.Root.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>()).ToList();

    // ------------------------------------------------------------ BD6, BD13

    static void Render_Parts()
    {
        Group("BD6", "hide masks: the body baked without the top: 0; with it: unchanged", Bd6);
        Group("BD13", "placement: a whole bake at the root, a part bake at its centroid, the same pixels", Bd13);
    }

    static float MaskOf(BakeData b, string renderer, string mask)
    {
        for (int i = 0; i < b.Renderers.Count; i++)
        {
            var n = b.Nodes[b.Renderers[i].Node].Name;
            if (n != renderer) continue;
            foreach (int mi in b.Renderers[i].Materials)
            {
                var p = mi >= 0 ? b.Materials[mi].Prop(mask) : null;
                if (p != null) return p.Type == 'i' ? p.I : p.F;
            }
        }
        return float.NaN;
    }

    static void Bd6()
    {
        var h = NewHost();
        try
        {
            var c = Chara(h, LoadChara(h, false));
            var body = c.Context.BodyRenderer;
            var bm = body ? body.sharedMaterial : null;
            float sa = bm && bm.HasProperty("_alpha_a") ? bm.GetFloat("_alpha_a") : float.NaN, sb = bm && bm.HasProperty("_alpha_b") ? bm.GetFloat("_alpha_b") : float.NaN,
                sd = bm && bm.HasProperty("_alpha_d") ? bm.GetFloat("_alpha_d") : float.NaN;
            Check("BD6 present: the body's masks were non-zero", (sa != 0f && !float.IsNaN(sa)) || (sb != 0f && !float.IsNaN(sb)) || (sd != 0f && !float.IsNaN(sd)), $"_alpha_a {sa}, _alpha_b {sb}, _alpha_d {sd} ({body?.name})");
            var j1 = BakeJob.OfChara(c, new BakeOptions { Parts = new[] { "body" } });
            var j2 = BakeJob.OfChara(c, new BakeOptions { Parts = new[] { "body", "clothes0" } });
            try
            {
                string bn = body.name;
                var b1 = j1.Capture.Bake;
                var b2 = j2.Capture.Bake;
                float a1 = MaskOf(b1, bn, "_alpha_a"), b1v = MaskOf(b1, bn, "_alpha_b"), d1 = MaskOf(b1, bn, "_alpha_d");
                float a2 = MaskOf(b2, bn, "_alpha_a"), b2v = MaskOf(b2, bn, "_alpha_b"), d2 = MaskOf(b2, bn, "_alpha_d");
                int otherNonZero = 0;
                foreach (var m in b1.Materials)
                    foreach (var k in new[] { "_alpha_a", "_alpha_b", "_alpha_d" })
                    {
                        var p = m.Prop(k);
                        if (p != null && (p.Type == 'f' ? p.F != 0f : p.I != 0)) otherNonZero++;
                    }
                Check("BD6 the body baked without the top and the bottom: _alpha_a, _alpha_b, _alpha_d 0 on every baked material", a1 == 0f && b1v == 0f && d1 == 0f && otherNonZero == 0,
                    $"body {a1} / {b1v} / {d1}; other non-zero {otherNonZero}; values changed {j1.Capture.Count("masksCleared")}");
                Check("BD6 with the top: _alpha_a, _alpha_b unchanged; without the bottom: _alpha_d 0", BitsEqual(a2, sa) && BitsEqual(b2v, sb) && d2 == 0f, $"body {a2} / {b2v} / {d2}");
                // the drawn values of the item of the first
                var proto = j1.Build(BakeCache.Of(h.Rt, true).Container);
                var r = BuiltRenderers(b1, proto.Root).FirstOrDefault(x => x && x.name == bn);
                var dm = r ? r.sharedMaterial : null;
                Check("BD6 the built body material draws the masks at 0", dm && dm.GetFloat("_alpha_a") == 0f && dm.GetFloat("_alpha_b") == 0f && dm.GetFloat("_alpha_d") == 0f);
            }
            finally { j1.Dispose(); j2.Dispose(); }
        }
        finally { h.Dispose(); }
    }

    static void Bd13()
    {
        var h = NewHost();
        BakeItem whole = null, centred = null, rooted = null;
        try
        {
            var c = Chara(h, LoadChara(h, false));
            EyesOpen(h, c);
            var root = c.Root.transform;
            whole = BakeToItem(h, c, new BakeOptions(), false, "bd13 whole");
            var wt = whole.It.Instance.transform;
            double dp = (wt.position - root.position).magnitude, dr = AngleDeg(wt.rotation, root.rotation);
            Check("BD13 a whole bake's item at the root's world pose", dp <= 1e-5 && dr <= 1e-4, $"position {F(dp, "E2")}, rotation {F(dr, "E2")} deg");
            var parts = new[] { "clothes0" };
            centred = BakeToItem(h, c, new BakeOptions { Parts = parts, Centre = true }, false, "bd13 centre");
            rooted = BakeToItem(h, c, new BakeOptions { Parts = parts, Centre = false }, false, "bd13 root");
            var cap = centred.Job.Capture;
            double off = (cap.Position - root.position).magnitude;
            Check("BD13 present: the centroid lies >= 1 unit from the root", off >= 1.0 && centred.Job.Capture.Bake.Origin == BakeData.OriginCentre && centred.Job.Capture.Bake.Nodes[0].Name == "centre", F(off) + " units");
            double ip = (centred.It.Instance.transform.position - cap.Position).magnitude;
            Check("BD13 a part bake's item at its centroid", ip <= 1e-5, F(ip, "E2"));
            var v = FullView(root);
            var r = Compare(h, v, rooted.Renderers, centred.Renderers, false, SourceAndBake(h));
            Measure("BD13 the part at its centroid against the part at the root: " + r.Line());
            Check("BD13 the same pixels (BD1's bars)", r.Footprint >= 2000 && r.Over2Pct <= 0.2 && r.Over8Pct <= 0.01, r.Line());
        }
        finally { h.Dispose(); whole?.Job?.Dispose(); centred?.Job?.Dispose(); rooted?.Job?.Dispose(); }
    }

    // ------------------------------------------------------------ BD7, BD8, BB5, BB4

    static void Render_Pose()
    {
        Group("BD7", "hidden parts: a garment off by its state and the body hidden by the body-hiding data are not baked; a hidden node is", Bd7);
        Group("BD8", "the blink held open: a bake taken mid-blink equals one taken open", Bd8);
        Group("BB5", "joint correction: an elbow bent by item FK moves the twist bones as on the character", Bb5);
        Group("BB4", "rebuild paths: built again from the blobs, FK values from the model, members added again", Bb4);
    }

    static int PartCount(BakeData b, string part) => b.Renderers.Count(r => r.Part == part);

    static void Bd7()
    {
        var h = NewHost();
        try
        {
            string key = LoadChara(h, false);
            var c = Chara(h, key);
            var j0 = BakeJob.OfChara(c, new BakeOptions());
            int all = j0.Capture.Bake.Renderers.Count, tops = PartCount(j0.Capture.Bake, "clothes0"), bodies = PartCount(j0.Capture.Bake, "body"), heads = PartCount(j0.Capture.Bake, "head");
            j0.Dispose();
            // a garment off by its state
            var st = (CharaNodeState)h.Rt.Model.Find(key).State;
            var v = (int[])st.State.Clothes.Clone();
            v[0] = 2;
            var w = h.Session.Set(key, "chara.state.clothes", v);
            h.Rt.ApplyNow();
            c = Chara(h, key);
            var topRends = c.Context.Clothes[0] ? c.Context.Clothes[0].GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
            int disabled = topRends.Count(r => !r.enabled || !r.gameObject.activeInHierarchy);
            Check("BD7 present: the top's renderer exists, disabled, with the top off", w.Ok && topRends.Length > 0 && disabled == topRends.Length && tops > 0, $"{topRends.Length} renderers, {disabled} disabled; baked before {tops}");
            var j1 = BakeJob.OfChara(c, new BakeOptions());
            Check("BD7 a garment off by its state is not baked", PartCount(j1.Capture.Bake, "clothes0") == 0, PartCount(j1.Capture.Bake, "clothes0") + " top renderers baked");
            j1.Dispose();
            v[0] = 0;
            h.Session.Set(key, "chara.state.clothes", v);
            h.Rt.ApplyNow();
            c = Chara(h, key);
            // the body hidden by the body-hiding data (its applier on the built character, undone after)
            var before = c.Root.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToList();
            HS2Next.Chara.Ext.InvisibleBodyApplier.Apply(c.Context, new HS2Next.Data.Ext.InvisibleBodyData { Visible = false });
            var off = before.Where(r => !r.enabled).ToList();
            try
            {
                var j2 = BakeJob.OfChara(c, new BakeOptions());
                bool bodyOff = off.Contains(c.Context.BodyRenderer);
                Check("BD7 present: the body's renderer exists, disabled by the body-hiding data", bodyOff && off.Count > 0, off.Count + " renderers disabled");
                Check("BD7 the body hidden by the body-hiding data is not baked", PartCount(j2.Capture.Bake, "body") == 0 && j2.Capture.Live.All(r => !off.Contains(r)),
                    $"body renderers baked {PartCount(j2.Capture.Bake, "body")} (shown {bodies}), head {PartCount(j2.Capture.Bake, "head")} (shown {heads})");
                j2.Dispose();
            }
            finally { foreach (var r in off) if (r) r.enabled = true; }
            // a hidden node: baked as if shown
            h.Ok(new { cmd = "obj.visible", key, visible = false }, "BD7 obj.visible false");
            h.Rt.ApplyNow();
            int forced = c.Root.GetComponentsInChildren<Renderer>(true).Count(r => r.forceRenderingOff);
            var j3 = BakeJob.OfChara(c, new BakeOptions());
            Check("BD7 a hidden node is baked as if shown", forced > 0 && j3.Capture.Bake.Renderers.Count == all, $"{forced} renderers forced off; baked {j3.Capture.Bake.Renderers.Count} of {all}");
            j3.Dispose();
            h.Ok(new { cmd = "obj.visible", key, visible = true }, "BD7 obj.visible true");
        }
        finally { h.Dispose(); }
    }

    static void Bd8()
    {
        var h = NewHost();
        try
        {
            string key = LoadChara(h, false);
            var c = Chara(h, key);
            var s = c.Motion.Get<HS2Next.Motion.Face.ExpressionStage>();
            if (!Check("BD8 the face stage is there and blinks", s != null && s.Blink.FixedFlags == 0, s == null ? "no face stage" : "fixed " + s.Blink.FixedFlags)) return;
            // a moment mid-blink: the eyes at most half open
            int frames = 0;
            while (frames < 1200 && s.Blink.OpenRate > 0.5f) { h.Frames(1); frames++; }
            var head = c.Context.Head ? c.Context.Head.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.sharedMesh && r.sharedMesh.blendShapeCount > 0 && r.name == "o_head") : null;
            var live = new Dictionary<SkinnedMeshRenderer, float[]>();
            foreach (var r in c.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.sharedMesh && r.sharedMesh.blendShapeCount > 0) live[r] = Enumerable.Range(0, r.sharedMesh.blendShapeCount).Select(i => r.GetBlendShapeWeight(i)).ToArray();
            float rateMid = s.Blink.OpenRate;
            var mid = BakeJob.OfChara(c, new BakeOptions());
            // the hold is undone: every live weight as it was
            int restored = 0, notRestored = 0;
            foreach (var kv in live)
                for (int i = 0; i < kv.Value.Length; i++) { if (BitsEqual(kv.Key.GetBlendShapeWeight(i), kv.Value[i])) restored++; else notRestored++; }
            // a moment open
            int f2 = 0;
            while (f2 < 1200 && s.Blink.OpenRate < 1f) { h.Frames(1); f2++; }
            var open = BakeJob.OfChara(c, new BakeOptions());
            double worst = 0, blinkLive = 0;
            int compared = 0;
            foreach (var kv in mid.Capture.Weights)
            {
                if (!open.Capture.Weights.TryGetValue(kv.Key, out var o)) continue;
                for (int i = 0; i < kv.Value.Length && i < o.Length; i++) { worst = Math.Max(worst, Math.Abs(kv.Value[i] - o[i])); compared++; }
                var r = mid.Capture.Live[kv.Key] as SkinnedMeshRenderer;
                if (r && live.TryGetValue(r, out var lw)) for (int i = 0; i < lw.Length && i < o.Length; i++) blinkLive = Math.Max(blinkLive, Math.Abs(lw[i] - o[i]));
            }
            Check("BD8 present: the blink weight was >= 50 when the first bake was taken", blinkLive >= 50 && rateMid <= 0.5f, $"open rate {F(rateMid)} after {frames} frames; the drawn weights off the open ones by up to {F(blinkLive)}");
            Check("BD8 a bake taken mid-blink equals one taken open <= 1e-4 in blend weights", compared > 0 && worst <= 1e-4 && mid.Capture.Count("blink.heldOpen") == 1, $"{compared} weights, worst {F(worst, "E2")}; held open {mid.Capture.Count("blink.heldOpen")}");
            Check("BD8 the face as drawn again after the capture", notRestored == 0 && restored > 0, $"{restored} weights back, {notRestored} not");
            mid.Dispose(); open.Dispose();
        }
        finally { h.Dispose(); }
    }

    /// <summary>The driven bones of the joint-correction rows on the left arm.</summary>
    static List<string> LeftArmTwists()
    {
        var l = new List<string>();
        foreach (var r in HS2Next.Motion.Corrective.JointCorrectionTable.Vanilla().Rows)
        {
            string n = r.HasCorrect ? r.CorrectBone : r.HasLookAt ? r.LookBone : null;
            if (n != null && n.EndsWith("_L", StringComparison.Ordinal) && (n.Contains("Arm") || n.Contains("Hand") || n.Contains("Elbo")) && !l.Contains(n)) l.Add(n);
        }
        return l;
    }

    static void Bb5()
    {
        var h = NewHost();
        BakeItem bi = null;
        try
        {
            string key = LoadChara(h, false);
            h.Session.Set(key, "chara.correction", new[] { true, true, true, true, true, true, true, true });
            h.Rt.ApplyNow();
            h.Frames(2);
            var c = Chara(h, key);
            bi = BakeToItem(h, c, new BakeOptions(), false, "bb5");
            var it = bi.It;
            var m = ItemMotion.Of(it);
            bool stage = m?.Motion && m.Motion.Get<HS2Next.Motion.Corrective.JointCorrectionStage>() != null;
            Check("BB5 the bake keeps the character's categories and its item has the joint-correction stage", bi.Job.Capture.Bake.Correction != null && bi.Job.Capture.Bake.Correction.All(x => x) && stage,
                $"categories {(bi.Job.Capture.Bake.Correction == null ? "none" : string.Join("", bi.Job.Capture.Bake.Correction.Select(x => x ? "1" : "0")))}, stage {stage}");
            const string elbow = "cf_J_ArmLow01_L";
            var twists = LeftArmTwists();
            var inst = it.Instance.transform;
            var baked = twists.ToDictionary(n => n, n => ItemDefaults.FindBone(inst, n)?.localRotation ?? Quaternion.identity);
            var bend = new Vec3(0f, -70f, 0f);
            var cur = c.Context.BodyBones[elbow].localEulerAngles;
            var target = new Vec3(cur.x + bend.X, cur.y + bend.Y, cur.z + bend.Z);
            h.Ok(new { cmd = "item.set", key = bi.Key, set = new Dictionary<string, object> { ["fk.enabled"] = true, ["fk.bones[\"" + elbow + "\"]"] = new[] { target.X, target.Y, target.Z } } }, "BB5 item.set FK of the elbow");
            var bones = new SortedDictionary<string, Vec3>(StringComparer.Ordinal) { [elbow] = target };
            h.Session.Set(key, "chara.fk.bones", bones);
            foreach (var g in HS2Next.Motion.Fk.FkGroupInfo.MemberNames) h.Session.Set(key, "chara.fk.groups." + g, true);
            h.Session.Set(key, "chara.fk.enabled", true);
            h.Rt.ApplyNow();
            h.Frames(3);
            double worst = 0, moved = 0;
            string worstName = "";
            int found = 0;
            foreach (var n in twists)
            {
                var a = ItemDefaults.FindBone(inst, n);
                Transform b = null;
                c.Context.BodyBones?.TryGetValue(n, out b);
                if (!a || !b) continue;
                found++;
                double d = AngleDeg(a.localRotation, b.localRotation);
                if (d > worst) { worst = d; worstName = n; }
                moved = Math.Max(moved, AngleDeg(a.localRotation, baked[n]));
            }
            var ie = ItemDefaults.FindBone(inst, elbow);
            double elbowDiff = ie ? AngleDeg(ie.localRotation, c.Context.BodyBones[elbow].localRotation) : -1;
            Check("BB5 present: a twist bone moves >= 5 degrees", moved >= 5.0, F(moved) + " deg");
            Check("BB5 the twist bones of the bent arm as on the character <= 0.02 degrees", found > 0 && worst <= 0.02 && elbowDiff >= 0 && elbowDiff <= 0.02,
                $"{found} bones, worst {F(worst, "E3")} deg ({worstName}); the elbow itself {F(elbowDiff, "E3")} deg");
        }
        finally { h.Dispose(); bi?.Job?.Dispose(); }
    }

    static void Bb4()
    {
        var h = NewHost();
        BakeItem bi = null;
        try
        {
            var c = Chara(h, LoadChara(h, false));
            EyesOpen(h, c);
            bi = BakeToItem(h, c, new BakeOptions(), false, "bb4");
            string key = bi.Key;
            var fkBone = bi.Job.Capture.Bake.Fk.FirstOrDefault(n => n.StartsWith("cf_J_Spine", StringComparison.Ordinal)) ?? bi.Job.Capture.Bake.Fk[0];
            h.Ok(new { cmd = "item.set", key, set = new Dictionary<string, object> { ["fk.enabled"] = true, ["fk.bones[\"" + fkBone + "\"]"] = new[] { 20, 0, 0 } } }, "BB4 item.set FK");
            h.Frames(1);
            var cache = BakeCache.Of(h.Rt, false);
            int members0 = Members(ItemOf(h, key));
            Check("BB4 present: the item was built before (from its bake, members " + members0 + ")", ItemOf(h, key)?.Source == "bake");
            void Judge(string what, string k)
            {
                var it = ItemOf(h, k);
                var bone = it != null && it.Instance ? ItemDefaults.FindBone(it.Instance.transform, fkBone) : null;
                double d = bone ? AngleDeg(bone.localRotation, Quaternion.Euler(20, 0, 0)) : -1;
                Check($"BB4 {what}: built from its bake, the FK value from the model, the members added again", it != null && it.State == RealizationState.Built && it.Source == "bake" && !it.IsFallback
                    && d >= 0 && d <= 1e-4 && Members(it) == members0, $"source {it?.Source}, FK {F(d, "E2")} deg, members {Members(it)} of {members0}, prototypes {cache.Count}, builds {cache.Builds}");
            }
            int builds = cache.Builds;
            Rebuild(h, ItemOf(h, key));
            h.Frames(1);
            Judge("a rebuild of its content", key);
            Check("BB4 a rebuild takes the prototype again (no new build)", cache.Builds == builds, $"builds {cache.Builds - builds}");
            var dup = h.Session.Duplicate(new[] { key });
            h.Rt.ApplyNow();
            h.Frames(1);
            Judge("a duplicate", dup.NewId);
            var del = h.Session.Delete(new[] { key });
            h.Rt.ApplyNow();
            var undo = h.Session.Commands.Undo();
            h.Rt.ApplyNow();
            h.Frames(1);
            Check("BB4 delete and undo", del.Ok && undo.Ok, del.Code + " " + undo.Code);
            Judge("a delete undone", key);
            var sr = h.Ok(new { cmd = "scene.save", name = "bb4_bake", overwrite = true, thumb = "placeholder" }, "BB4 scene.save");
            h.Ok(new { cmd = "scene.load", path = (string)sr["path"] }, "BB4 scene.load");
            h.Rt.ApplyNow();
            h.Frames(1);
            Judge("a load", key);
            try { File.Delete((string)sr["path"]); } catch (Exception) { }
        }
        finally { h.Dispose(); bi?.Job?.Dispose(); }
    }

    static int Members(ItemRealization it)
    {
        if (it == null || !it.Instance) return -1;
        return it.Instance.GetComponentsInChildren<Renderer>(true).Count(r => HS2Next.Chara.Mosaic.MosaicSet.IsAdded(r));
    }

    // ------------------------------------------------------------ BD12

    static void Render_Gpu()
    {
        Group("BD12", "meshes through their GPU buffers: every attribute format, the items of Step 0, a synthetic unreadable skinned mesh", Bd12);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct MemoryStatus { public uint Length, Load; public ulong TotalPhys, AvailPhys, TotalPage, AvailPage, TotalVirtual, AvailVirtual, AvailExtended; }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatus m);

    /// <summary>Free physical memory in GB (999 when it cannot be read).</summary>
    static double FreeGb()
    {
        var m = new MemoryStatus { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MemoryStatus)) };
        return GlobalMemoryStatusEx(ref m) ? m.AvailPhys / (1024.0 * 1024 * 1024) : 999;
    }

    static readonly VertexAttributeFormat[] AllFormats =
    {
        VertexAttributeFormat.Float32, VertexAttributeFormat.Float16, VertexAttributeFormat.UNorm8, VertexAttributeFormat.SNorm8, VertexAttributeFormat.UNorm16,
        VertexAttributeFormat.SNorm16, VertexAttributeFormat.UInt8, VertexAttributeFormat.SInt8, VertexAttributeFormat.UInt16, VertexAttributeFormat.SInt16,
        VertexAttributeFormat.UInt32, VertexAttributeFormat.SInt32,
    };

    /// <summary>Compares the API's data of a mesh with the GPU route's, channel by channel in bits; the differing channels named.</summary>
    static List<string> ApiAgainstGpu(Mesh m, Dictionary<string, int> formats)
    {
        var bad = new List<string>();
        var api = BakeMeshes.FromApi(m);
        var gpu = BakeMeshes.FromGpu(m, null, out string why);
        if (gpu == null) { bad.Add(m.name + ": " + why); return bad; }
        foreach (var a in m.GetVertexAttributes())
        {
            string k = a.attribute + " " + a.format + " x" + a.dimension;
            formats.TryGetValue(k, out int c); formats[k] = c + 1;
        }
        bool V3(Vector3[] x, Vector3[] y) => x == null ? y == null : y != null && x.Length == y.Length && x.Zip(y, (p, q) => BitsEqual(p.x, q.x) && BitsEqual(p.y, q.y) && BitsEqual(p.z, q.z)).All(b => b);
        bool V4(Vector4[] x, Vector4[] y, int dim) => x == null ? y == null : y != null && x.Length == y.Length && x.Zip(y, (p, q) => Enumerable.Range(0, dim).All(i => BitsEqual(p[i], q[i]))).All(b => b);
        // where: the counts and the first value that differs, per channel (diagnostics)
        string First3(Vector3[] x, Vector3[] y)
        {
            if (x == null || y == null) return $" (api {(x == null ? "none" : x.Length.ToString())}, gpu {(y == null ? "none" : y.Length.ToString())})";
            if (x.Length != y.Length) return $" (api {x.Length}, gpu {y.Length})";
            for (int i = 0; i < x.Length; i++) if (!(BitsEqual(x[i].x, y[i].x) && BitsEqual(x[i].y, y[i].y) && BitsEqual(x[i].z, y[i].z))) return $" (first at {i}: api ({x[i].x:R}, {x[i].y:R}, {x[i].z:R}) gpu ({y[i].x:R}, {y[i].y:R}, {y[i].z:R}))";
            return "";
        }
        string First4(Vector4[] x, Vector4[] y)
        {
            if (x == null || y == null) return $" (api {(x == null ? "none" : x.Length.ToString())}, gpu {(y == null ? "none" : y.Length.ToString())})";
            if (x.Length != y.Length) return $" (api {x.Length}, gpu {y.Length})";
            for (int i = 0; i < x.Length; i++) if (x[i] != y[i] || !BitsEqual(x[i].x, y[i].x)) return $" (first at {i}: api ({x[i].x:R}, {x[i].y:R}, {x[i].z:R}, {x[i].w:R}) gpu ({y[i].x:R}, {y[i].y:R}, {y[i].z:R}, {y[i].w:R}))";
            return "";
        }
        if (!V3(api.V, gpu.V)) bad.Add(m.name + " positions" + First3(api.V, gpu.V) + $" readable {m.isReadable}, vertices {m.vertexCount}, sub-meshes {m.subMeshCount}, index format {m.indexFormat}, streams {m.vertexBufferCount}");
        if (!V3(api.N, gpu.N)) bad.Add(m.name + " normals");
        if (!V4(api.T, gpu.T, 4)) bad.Add(m.name + " tangents");
        for (int ch = 0; ch < 8; ch++) if (!V4(api.Uv[ch], gpu.Uv[ch], Math.Max(1, api.UvDim[ch]))) bad.Add(m.name + " uv" + ch + First4(api.Uv[ch], gpu.Uv[ch]));
        if ((api.C == null) != (gpu.C == null) || (api.C != null && !api.C.SequenceEqual(gpu.C))) bad.Add(m.name + " colours");
        if (api.Idx.Length != gpu.Idx.Length) bad.Add(m.name + " sub-meshes");
        else for (int s = 0; s < api.Idx.Length; s++)
                if (api.Topo[s] != gpu.Topo[s] || !api.Idx[s].SequenceEqual(gpu.Idx[s]))
                {
                    var d = m.GetSubMesh(s);
                    int at = Enumerable.Range(0, Math.Min(api.Idx[s].Length, gpu.Idx[s].Length)).FirstOrDefault(i => api.Idx[s][i] != gpu.Idx[s][i]);
                    bad.Add(m.name + " indices " + s + $" (api {api.Idx[s].Length}, gpu {gpu.Idx[s].Length}; first at {at}: {(at < api.Idx[s].Length ? api.Idx[s][at] : -1)} / {(at < gpu.Idx[s].Length ? gpu.Idx[s][at] : -1)}; start {d.indexStart}, base vertex {d.baseVertex}, first vertex {d.firstVertex})");
                }
        return bad;
    }

    static bool IsIntegerFormat(VertexAttributeFormat f) =>
        f == VertexAttributeFormat.UInt8 || f == VertexAttributeFormat.SInt8 || f == VertexAttributeFormat.UInt16 || f == VertexAttributeFormat.SInt16
        || f == VertexAttributeFormat.UInt32 || f == VertexAttributeFormat.SInt32;

    /// <summary>The integer component <paramref name="i"/> of bytes written in format <paramref name="f"/> (little endian), as a float.</summary>
    static float WrittenInteger(byte[] raw, VertexAttributeFormat f, int i)
    {
        switch (f)
        {
            case VertexAttributeFormat.UInt8: return raw[i];
            case VertexAttributeFormat.SInt8: return (sbyte)raw[i];
            case VertexAttributeFormat.UInt16: return BitConverter.ToUInt16(raw, 2 * i);
            case VertexAttributeFormat.SInt16: return BitConverter.ToInt16(raw, 2 * i);
            case VertexAttributeFormat.UInt32: return BitConverter.ToUInt32(raw, 4 * i);
            case VertexAttributeFormat.SInt32: return BitConverter.ToInt32(raw, 4 * i);
            default: return float.NaN;
        }
    }

    /// <summary>An integer format (ruled by the lead 03:4x): the GPU route's texture coordinate 0 against the integers the test wrote, the positions
    /// against the API's.</summary>
    static List<string> WrittenAgainstGpu(Mesh m, VertexAttributeFormat f, byte[] raw)
    {
        var bad = new List<string>();
        var api = BakeMeshes.FromApi(m);
        var gpu = BakeMeshes.FromGpu(m, null, out string why);
        if (gpu == null) { bad.Add(m.name + ": " + why); return bad; }
        int n = m.vertexCount;
        if (gpu.V == null || api.V == null || gpu.V.Length != api.V.Length || !api.V.Zip(gpu.V, (p, q) => BitsEqual(p.x, q.x) && BitsEqual(p.y, q.y) && BitsEqual(p.z, q.z)).All(x => x))
            bad.Add(m.name + " positions");
        var uv = gpu.Uv[0];
        if (uv == null || uv.Length != n) { bad.Add(m.name + $" uv0 (gpu {(uv == null ? "none" : uv.Length.ToString())}, written {n})"); return bad; }
        for (int v = 0; v < n; v++)
            for (int c = 0; c < 4; c++)
            {
                float want = WrittenInteger(raw, f, 4 * v + c);
                if (!BitsEqual(want, uv[v][c])) { bad.Add(m.name + $" uv0 (first at {v}.{c}: written {want:R}, gpu {uv[v][c]:R})"); return bad; }
            }
        return bad;
    }

    /// <summary>A mesh whose texture coordinate 0 is stored in <paramref name="f"/> (4 components, values across the format's range).</summary>
    static Mesh FormatMesh(VertexAttributeFormat f) => FormatMesh(f, out _);

    /// <summary>The same, with the bytes written for texture coordinate 0.</summary>
    static Mesh FormatMesh(VertexAttributeFormat f, out byte[] written)
    {
        const int n = 64;
        var m = new Mesh { name = "bd12_" + f };
        m.SetVertexBufferParams(n, new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, f, 4, 1));
        var pos = new float[n * 3];
        for (int i = 0; i < n; i++) { pos[3 * i] = i % 8; pos[3 * i + 1] = i / 8; pos[3 * i + 2] = 0.25f * (i % 3); }
        m.SetVertexBufferData(pos, 0, 0, pos.Length, 0);
        int cs = BakeMeshes.ComponentSize(f);
        var raw = new byte[n * 4 * cs];
        var rnd = new System.Random(1200 + (int)f);
        rnd.NextBytes(raw);
        if (f == VertexAttributeFormat.Float16)
            for (int i = 0; i < n * 4; i++) { ushort hb = Mathf.FloatToHalf((float)(rnd.NextDouble() * 200 - 100)); raw[2 * i] = (byte)hb; raw[2 * i + 1] = (byte)(hb >> 8); }
        if (f == VertexAttributeFormat.Float32)
            for (int i = 0; i < n * 4; i++) { var b = BitConverter.GetBytes((float)(rnd.NextDouble() * 200 - 100)); Array.Copy(b, 0, raw, 4 * i, 4); }
        m.SetVertexBufferData(raw, 0, 0, raw.Length, 1);
        written = raw;
        var idx = new List<int>();
        for (int y = 0; y < 7; y++) for (int x = 0; x < 7; x++) { int a = y * 8 + x; idx.AddRange(new[] { a, a + 8, a + 1, a + 1, a + 8, a + 9 }); }
        m.SetIndices(idx, MeshTopology.Triangles, 0);
        m.RecalculateBounds();
        m.UploadMeshData(false);
        return m;
    }

    static void Bd12()
    {
        // (1) every attribute format the route decodes, on texture coordinate 0: the 6 integer formats against the integers written (the API's
        // GetUVs is no reference for them), the 6 others against the API's decoding (ruled by the lead 03:4x)
        var formats = new Dictionary<string, int>(StringComparer.Ordinal);
        var bad = new List<string>();
        foreach (var f in AllFormats)
        {
            var m = FormatMesh(f, out var written);
            try { bad.AddRange(IsIntegerFormat(f) ? WrittenAgainstGpu(m, f, written) : ApiAgainstGpu(m, formats)); }
            finally { Object.DestroyImmediate(m); }
        }
        Check("BD12 every vertex attribute format decodes as written (6 integer formats: the integers written; 6 others: as the API reads them; bits)", bad.Count == 0,
            bad.Count == 0 ? string.Join(", ", AllFormats) : string.Join("; ", bad.Take(8)));
        // (2) the sample of Step 0 (150 rows spread over the visible item rows) and its two named items: every mesh read both ways
        var h = NewHost();
        try
        {
            var db = h.Rt.Services.Db;
            var cat = h.Rt.Services.Catalog;
            var rows = db.Items.Enumerate().ToList();
            int step = Math.Max(1, rows.Count / 150), tried = 0, loaded = 0, meshes = 0, unreadable = 0, unreadableVerts = 0;
            var sampleFormats = new Dictionary<string, int>(StringComparer.Ordinal);
            var sampleBad = new List<string>();
            var unreadableRows = new List<string>();
            var unreadableOf = new Dictionary<string, int>(StringComparer.Ordinal);
            int stoppedAt = -1;
            for (int i = 0; i < rows.Count && tried < 150; i += step)
            {
                // the census's floor: free memory at least 9 GB, assets no row holds dropped every 25 rows
                if (tried % 25 == 0 && tried > 0) Resources.UnloadUnusedAssets();
                if (FreeGb() < 9.0) { stoppedAt = tried; break; }
                var row = rows[i];
                tried++;
                try
                {
                    cat.LoadTransient("bake:bd12", () =>
                    {
                        var go = cat.LoadAsset<GameObject>(row);
                        if (!go) return 0;
                        loaded++;
                        bool any = false;
                        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                        {
                            var mesh = BakeCapture.MeshOf(r);
                            if (!mesh) continue;
                            meshes++;
                            // an unreadable mesh has no data through the API in the Editor: judged by a render of its row below (ruled by the lead 03:4x)
                            if (!mesh.isReadable)
                            {
                                unreadable++; unreadableVerts += mesh.vertexCount; any = true;
                                unreadableOf.TryGetValue(row.Key.ToString(), out int u); unreadableOf[row.Key.ToString()] = u + 1;
                                continue;
                            }
                            sampleBad.AddRange(ApiAgainstGpu(mesh, sampleFormats).Select(x => row.Key + " " + x));
                        }
                        if (any) unreadableRows.Add(row.Key.ToString());
                        return 0;
                    });
                }
                catch (Exception e) { sampleBad.Add(row.Key + ": " + e.GetType().Name); }
            }
            if (stoppedAt >= 0) Warn($"BD12W1 the sample stopped after {stoppedAt} rows: free memory under 9 GB ({F(FreeGb(), "F1")} GB)");
            Info($"BD12 sample: {tried} rows of {rows.Count}, {loaded} loaded, {meshes} meshes, {unreadable} unreadable ({unreadableVerts} vertices) in {unreadableRows.Count} rows: {string.Join(", ", unreadableRows.Take(12))}");
            Info("BD12 formats seen: " + string.Join("; ", sampleFormats.OrderBy(kv => kv.Key).Select(kv => kv.Key + " " + kv.Value)));
            Check("BD12 present: the rows have unreadable meshes", unreadable > 0, $"{unreadable} meshes in {unreadableRows.Count} rows: " + string.Join(", ", unreadableOf.Select(kv => kv.Key + " " + kv.Value)));
            Check("BD12 every readable mesh of the sample read through its GPU buffers equals the API's data (bits)", sampleBad.Count == 0 && meshes - unreadable > 0,
                $"{meshes - unreadable} readable meshes; " + sampleBad.Count + " differ" + (sampleBad.Count > 0 ? ": " + string.Join("; ", sampleBad.Take(12)) : ""));
            // the two items of Step 0 and the sample's rows with unreadable meshes, drawn from a bake made through the GPU route, against the item itself
            foreach (var refText in new[] { "hooh.map.city.downtown#65", "[Fouetty]Street Props Pack#131" }.Concat(unreadableRows))
            {
                if (!HS2Next.Content.ContentId.TryParse(refText, out var id) || !db.TryGetItem(id, out HS2Next.Content.Studio.StudioItem row) || row == null) { Check($"BD12 {refText}: installed", false, "not installed, not judged"); continue; }
                var add = h.Call(new { cmd = "item.add", @ref = refText });
                string key = Key(add);
                h.Rt.ApplyNow();
                var it = ItemOf(h, key);
                if (!Check($"BD12 {refText}: the item is built", it != null && it.Instance, it != null && it.Instance ? "" : "not built")) continue;
                var formatsOf = new Dictionary<string, int>(StringComparer.Ordinal);
                var src = it.Instance.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy && BakeCapture.MeshOf(r)).ToList();
                foreach (var r in src) foreach (var a in BakeCapture.MeshOf(r).GetVertexAttributes()) { string k = a.attribute + " " + a.format + " x" + a.dimension; formatsOf.TryGetValue(k, out int c); formatsOf[k] = c + 1; }
                BakeMeshes.ForceGpuRoute = true;
                BakeJob job;
                try { job = BakeJob.OfItem(it); job.Finish(); }
                finally { BakeMeshes.ForceGpuRoute = false; }
                job.Build(BakeCache.Of(h.Rt, true));
                job.AddTo(h.Rt.Session.Model.File.Res);
                var bi = AddItem(h, job, null, refText + " (GPU route)");
                var c0 = it.Instance.GetComponentsInChildren<Renderer>(true).Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
                var v = new View { Name = refText, Pivot = c0.center, Rot = new Vector3(20f, 150f, 0f), Distance = Mathf.Max(5f, c0.extents.magnitude * 2.5f / Mathf.Tan(11.5f * Mathf.Deg2Rad) / 2f), Fov = SceneViewCamera.DefaultFov, W = 1280, H = 720 };
                var cmp = Compare(h, v, src, bi.Renderers, true, SourceAndBake(h));
                Measure($"BD12 {refText} through the GPU route: " + cmp.Line() + " ; routes " + string.Join(", ", job.Capture.Counts.Where(kv => kv.Key.StartsWith("mesh.")).Select(kv => kv.Key + " " + kv.Value)));
                Info($"BD12 {refText}: formats " + string.Join("; ", formatsOf.OrderBy(kv => kv.Key).Select(kv => kv.Key + " " + kv.Value)) + "; by renderer " + cmp.Named());
                string what = unreadableOf.TryGetValue(refText, out int un) ? $" ({un} unreadable meshes of the sample)" : "";
                Check($"BD12 {refText}{what}: a bake through the GPU route draws as the item (BD1's bars)", cmp.Footprint >= 2000 && cmp.Over2Pct <= 0.2 && cmp.Over8Pct <= 0.01, cmp.Line());
                h.Session.Delete(new[] { key, bi.Key });
                h.Rt.ApplyNow();
                job.Dispose();
            }
        }
        finally { h.Dispose(); }
        // (3) a synthetic unreadable skinned mesh
        Bd12Skinned();
    }

    static void Bd12Skinned()
    {
        var b = Synthetic("bd12_skin", new Color32(180, 120, 60, 255), out var tx);
        var src = b.Meshes[0].Data;
        var root = new GameObject("bd12_skinned");
        Mesh mesh = null;
        BakeJob job = null;
        var owned = new List<Object>();
        try
        {
            // the synthetic chain as live objects, its strip a skinned renderer on a mesh made unreadable
            var tr = new Transform[b.Nodes.Count];
            for (int i = 0; i < tr.Length; i++)
            {
                var n = b.Nodes[i];
                var go = i == 0 ? root : new GameObject(n.Name);
                if (i > 0) { go.transform.SetParent(tr[n.Parent], false); go.transform.SetLocalPositionAndRotation(n.P, n.R); }
                tr[i] = go.transform;
            }
            mesh = BakeMeshes.Build(src);
            mesh.UploadMeshData(true);
            var smr = tr[4].gameObject.AddComponent<SkinnedMeshRenderer>();
            smr.bones = new[] { tr[1], tr[2], tr[3] };
            smr.rootBone = tr[1];
            smr.sharedMesh = mesh;
            var mat = new Material(Shader.Find("HS2Next/Standard"));
            owned.Add(mat);
            smr.sharedMaterial = mat;
            smr.localBounds = new Bounds(new Vector3(0f, 3f, 0f), new Vector3(4f, 8f, 4f));
            Check("BD12 present: the synthetic skinned mesh is unreadable", !mesh.isReadable);
            job = BakeJob.OfProp(root.transform, new[] { "j1", "j2", "j3" }, BakeData.KindItem);
            job.Finish();
            var cap = job.Capture;
            var md = cap.Bake.Meshes.Count > 0 ? cap.Bake.Meshes[0].Data : null;
            bool skinned = cap.Bake.Renderers.Count == 1 && cap.Bake.Renderers[0].Skinned;
            bool same = md != null && md.V.Length == src.V.Length && md.V.Zip(src.V, (p, q) => (p - q).sqrMagnitude <= 1e-12f).All(x => x)
                && md.W != null && md.W.Zip(src.W, (p, q) => p.boneIndex0 == q.boneIndex0 && p.weight0 == q.weight0).All(x => x) && md.Bp != null && md.Bp.Length == src.Bp.Length;
            Check("BD12 an unreadable skinned mesh: its buffers, weights and bind poses read through the GPU, stored skinned", skinned && same && md.Route == "gpu+skin",
                $"route {md?.Route}, skinned {skinned}, data equal {same}; findings {string.Join("; ", cap.Findings.Select(f => f.Code))}");
        }
        finally
        {
            job?.Dispose();
            Object.DestroyImmediate(root);
            if (mesh) Object.DestroyImmediate(mesh);
            foreach (var o in owned) if (o) Object.DestroyImmediate(o);
        }
    }

    // ------------------------------------------------------------ BD2, BD9, BD10

    static void Render_State()
    {
        Group("BD2", "each part baked alone, «Fill hollow parts» off, against the character with the other parts hidden (BD1's bars outside the members), both targets; the inside copies with fill on", Bd2);
        Group("BD9", "a stained target (HERO107; BR-Chan printed): its composite baked; the film left out with its finding", Bd9);
        Group("BD10", "a ragdolled character: the bake's bones are the drawn ones", Bd10);
    }

    static string KindOf(string part) =>
        part == "body" || part == "head" ? part : part.StartsWith("hair", StringComparison.Ordinal) ? "hair" : part.StartsWith("clothes", StringComparison.Ordinal) ? "clothes"
        : part.StartsWith("acc", StringComparison.Ordinal) ? "accessory" : "other";

    static void Bd2()
    {
        bd2Warns = 0;
        Bd2Of(false);
        Bd2Of(true);
    }

    /// <summary>The members (mosaic) of a part bake and of its source: the bake's built renderers and the live ones the capture marked.</summary>
    static List<Renderer> MembersOf(BakeItem bi)
    {
        var cap = bi.Job.Capture;
        var built = BuiltRenderers(cap.Bake, bi.It.Instance);
        var l = new List<Renderer>();
        for (int i = 0; i < cap.Bake.Renderers.Count; i++)
        {
            if (!cap.Bake.Renderers[i].Member) continue;
            if (i < built.Count && built[i]) l.Add(built[i]);
            if (i < cap.Live.Count && cap.Live[i]) l.Add(cap.Live[i]);
        }
        return l;
    }

    static int bd2Warns;

    /// <summary>
    /// The swap test of a cutout edge (ruled by the lead 04:4x): the source's slots draw the bake's decoded textures in place of their compressed
    /// ones (material copies on the renderers for the render, the same sampler), compared with the bake on the same view and mask.
    /// </summary>
    static Compared Swapped(Host h, View v, BakeItem bi, IList<Renderer> scene, IList<Renderer> maskOut, out int swapped)
    {
        var cap = bi.Job.Capture;
        var built = BuiltRenderers(cap.Bake, bi.It.Instance);
        var saved = new List<(Renderer r, Material[] m)>();
        var copies = new List<Material>();
        swapped = 0;
        try
        {
            for (int i = 0; i < cap.Live.Count && i < built.Count; i++)
            {
                var src = cap.Live[i];
                var dst = built[i];
                if (!src || !dst || cap.Bake.Renderers[i].Member) continue;
                var sm = src.sharedMaterials;
                var dm = dst.sharedMaterials;
                var nm = (Material[])sm.Clone();
                bool any = false;
                for (int k = 0; k < sm.Length && k < dm.Length; k++)
                {
                    if (!sm[k] || !dm[k]) continue;
                    Material copy = null;
                    var sh = sm[k].shader;
                    for (int p = 0; p < sh.GetPropertyCount(); p++)
                    {
                        if (sh.GetPropertyType(p) != ShaderPropertyType.Texture) continue;
                        string n = sh.GetPropertyName(p);
                        var t = sm[k].GetTexture(n);
                        if (!t || !UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsCompressedFormat(t.graphicsFormat) || !dm[k].HasProperty(n) || !dm[k].GetTexture(n)) continue;
                        if (!copy) { copy = new Material(sm[k]); copies.Add(copy); }
                        copy.SetTexture(n, dm[k].GetTexture(n));
                        swapped++;
                    }
                    if (copy) { nm[k] = copy; any = true; }
                }
                if (!any) continue;
                saved.Add((src, sm));
                src.sharedMaterials = nm;
            }
            return Compare(h, v, cap.Live.Where(r => r).ToList(), bi.Renderers, true, scene, maskOut);
        }
        finally
        {
            foreach (var (r, m) in saved) if (r) r.sharedMaterials = m;
            foreach (var m in copies) if (m) Object.DestroyImmediate(m);
        }
    }

    static string MatFloat(Material m, params string[] names)
    {
        foreach (var n in names) if (m.HasProperty(n)) return n + " " + m.GetFloat(n).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        return string.Join("/", names) + " -";
    }

    /// <summary>Where a part over the 8-level bar differs (the lead's round 4; printed only): each renderer's sorting fields, slots' queue and blend
    /// state, shadow, occlusion and motion settings, bounds centre and its distance to the camera (the transparent sort key), source and bake; then
    /// each renderer alone, the bake's against the source's, the same view and mask.</summary>
    static void Bd2Order(Host h, View v, BakeItem bi, IList<Renderer> scene, IList<Renderer> maskOut, string who, string part)
    {
        var cap = bi.Job.Capture;
        var built = BuiltRenderers(cap.Bake, bi.It.Instance);
        var rot = Quaternion.Euler(v.Rot);
        var camPos = v.Pivot - rot * Vector3.forward * v.Distance;
        for (int i = 0; i < cap.Live.Count && i < built.Count; i++)
        {
            if (cap.Bake.Renderers[i].Member) continue;
            foreach (var (r, side) in new[] { (cap.Live[i], "source"), (built[i], "bake") })
            {
                if (!r) { Info($"BD2 {who} {part} order {side}: renderer {i} none"); continue; }
                var slots = new List<string>();
                var ms = r.sharedMaterials;
                for (int k = 0; k < ms.Length; k++)
                {
                    var m = ms[k];
                    if (!m) { slots.Add($"[{k}] none"); continue; }
                    slots.Add($"[{k}] {m.name} queue {m.renderQueue} RenderType {m.GetTag("RenderType", false, "")} {MatFloat(m, "_SrcBlend")} {MatFloat(m, "_DstBlend")} {MatFloat(m, "_ZWrite")} {MatFloat(m, "_Cull", "_CullMode")} " +
                              $"{MatFloat(m, "_Surface")} {MatFloat(m, "_AlphaClip", "_Cutoff")}");
                }
                var c = r.bounds.center;
                Info($"BD2 {who} {part} order {side} {r.name}: sortingLayerID {r.sortingLayerID} sortingOrder {r.sortingOrder} rendererPriority {r.rendererPriority} shadows {r.shadowCastingMode} " +
                     $"occlusionWhenDynamic {r.allowOcclusionWhenDynamic} motion vectors {r.motionVectorGenerationMode} bounds centre ({c.x:R}, {c.y:R}, {c.z:R}) distance to the camera {(c - camPos).magnitude:R}; " + string.Join("; ", slots));
            }
            if (!cap.Live[i] || !built[i]) continue;
            // the vertex data as drawn: positions, normals, tangents (world) and texture coordinates 0, the bake's against the source's
            if (cap.Live[i] is SkinnedMeshRenderer ls && built[i] is SkinnedMeshRenderer bs && ls.sharedMesh && bs.sharedMesh)
            {
                var m1 = new Mesh(); var m2 = new Mesh();
                try
                {
                    ls.BakeMesh(m1, true); bs.BakeMesh(m2, true);
                    var v1 = m1.vertices; var v2 = m2.vertices; var n1 = m1.normals; var n2 = m2.normals; var t1 = m1.tangents; var t2 = m2.tangents;
                    var u1 = new List<Vector4>(); var u2 = new List<Vector4>();
                    m1.GetUVs(0, u1); m2.GetUVs(0, u2);
                    double wp = 0, wn = 0, wt = 0, wu = 0; int tw = 0;
                    for (int k = 0; k < v1.Length && k < v2.Length; k++)
                    {
                        wp = Math.Max(wp, ((ls.transform.position + ls.transform.rotation * v1[k]) - (bs.transform.position + bs.transform.rotation * v2[k])).magnitude);
                        if (k < n1.Length && k < n2.Length) wn = Math.Max(wn, Vector3.Angle(ls.transform.rotation * n1[k], bs.transform.rotation * n2[k]));
                        if (k < t1.Length && k < t2.Length) { wt = Math.Max(wt, Vector3.Angle(ls.transform.rotation * (Vector3)t1[k], bs.transform.rotation * (Vector3)t2[k])); if (t1[k].w != t2[k].w) tw++; }
                        if (k < u1.Count && k < u2.Count) wu = Math.Max(wu, (u1[k] - u2[k]).magnitude);
                    }
                    Info($"BD2 {who} {part} order: {ls.name} vertex data (world): vertices {v1.Length}/{v2.Length}, positions worst {F(wp, "E2")}, normals {F(wn, "F3")} deg, tangents {F(wt, "F3")} deg " +
                         $"(handedness differs on {tw}), uv0 worst {F(wu, "E2")}; sub-meshes {ls.sharedMesh.subMeshCount}/{bs.sharedMesh.subMeshCount}, blend shapes {ls.sharedMesh.blendShapeCount}/{bs.sharedMesh.blendShapeCount}, " +
                         $"quality {ls.quality}/{bs.quality}, attributes source {string.Join(" ", ls.sharedMesh.GetVertexAttributes().Select(a => a.attribute + ":" + a.format + "x" + a.dimension))} / bake {string.Join(" ", bs.sharedMesh.GetVertexAttributes().Select(a => a.attribute + ":" + a.format + "x" + a.dimension))}");
                }
                finally { Object.DestroyImmediate(m1); Object.DestroyImmediate(m2); }
            }
            var alone = Compare(h, v, new List<Renderer> { cap.Live[i] }, new List<Renderer> { built[i] }, false, scene, maskOut);
            Info($"BD2 {who} {part} order: {cap.Live[i].name} alone, the bake's against the source's: {alone.Line()}, bake only {alone.BakeOnly}; " +
                 $"{(alone.Over2Pct <= 0.2 && alone.Over8Pct <= 0.01 ? "within" : "over")} BD1's bars");
        }
    }

    static void Bd2Of(bool male)
    {
        string who = male ? "HERO107" : "BR-Chan";
        var h = NewHost();
        try
        {
            string key = LoadChara(h, male);
            var c = Chara(h, key);
            EyesOpen(h, c);
            // the part of every drawn renderer, from a whole capture
            var whole = BakeJob.OfChara(c, new BakeOptions());
            var partOf = new Dictionary<Renderer, string>();
            for (int i = 0; i < whole.Capture.Live.Count; i++) if (whole.Capture.Live[i]) partOf[whole.Capture.Live[i]] = whole.Capture.Bake.Renderers[i].Part;
            whole.Dispose();
            var parts = partOf.Values.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
            var judgedKinds = new HashSet<string>();
            var kinds = new HashSet<string>(parts.Select(KindOf));
            var root = c.Root.transform;
            var views = new[] { FullView(root), FaceView(root, FaceOf(c)) };
            // the inside copies with fill on (ruled by the lead 03:4x): per part, the copies made equal the eligible renderers
            var copyBad = new List<string>();
            var copyLines = new List<string>();
            int partsWithCopies = 0;
            foreach (var part in parts)
            {
                var j = BakeJob.OfChara(c, new BakeOptions { Parts = new[] { part }, Insides = true });
                try
                {
                    var b = j.Capture.Bake;
                    int eligible = b.Insides.Fill ? b.Renderers.Count(r => BakeInsides.Eligible(b, r)) : 0;
                    var proto = j.Build(BakeCache.Of(h.Rt, true).Container);
                    int copies = proto?.Insides ?? -1;
                    copyLines.Add($"{part} {copies}/{eligible}");
                    if (copies != eligible) copyBad.Add($"{part}: copies {copies}, eligible {eligible} (fill {b.Insides.Fill})");
                    if (copies > 0) partsWithCopies++;
                }
                finally { j.Dispose(); }
            }
            Info($"BD2 {who} inside copies with fill on, made/eligible per part: " + string.Join(", ", copyLines));
            Check($"BD2 {who} present: >= 1 part with >= 1 inside copy (fill on)", partsWithCopies >= 1, partsWithCopies + " parts");
            Check($"BD2 {who} the inside copies made per part with fill on equal the eligible renderers of that part", copyBad.Count == 0 && copyLines.Count == parts.Count, copyBad.Count == 0 ? "" : string.Join("; ", copyBad));
            foreach (var part in parts)
            {
                // a garment over the body or the inner wear is off while they are compared: their masks are what a bake without it has
                bool masked = part == "body" || part == "clothes2" || part == "clothes3" || part == "clothes5";
                var st = (CharaNodeState)h.Rt.Model.Find(key).State;
                var saved = (int[])st.State.Clothes.Clone();
                if (masked)
                {
                    var v = (int[])saved.Clone();
                    v[0] = 2; v[1] = 2;
                    h.Session.Set(key, "chara.state.clothes", v);
                    h.Rt.ApplyNow();
                    c = Chara(h, key);
                }
                BakeItem bi = null;
                try
                {
                    bi = BakeToItem(h, c, new BakeOptions { Parts = new[] { part }, Insides = false }, false, "bd2 " + part);
                    var src = bi.Job.Capture.Live.Where(r => r).ToList();
                    // the mosaic members' footprints are masked out (ruled by the lead 04:4x: members are judged by the BV rows, spec BN8)
                    var members = MembersOf(bi);
                    Compared best = null;
                    View bestView = views[0];
                    foreach (var v in views)
                    {
                        var r = Compare(h, v, src, bi.Renderers, true, SourceAndBake(h), members);
                        if (best == null || r.Footprint > best.Footprint) { best = r; bestView = v; }
                        if (r.Footprint >= 2000) { best = r; bestView = v; break; }
                    }
                    if (best == null || best.Footprint < 2000) { Info($"BD2 {who} {part}: footprint {best?.Footprint ?? 0} px in both views: not judged"); continue; }
                    judgedKinds.Add(KindOf(part));
                    Measure($"BD2 {who} {part} ({bi.Job.Capture.Bake.Renderers.Count} renderers, insides {bi.Job.Capture.Bake.Renderers.Count(x => x.Inside == BakeData.InsideFill)}): {best.Line()}" +
                            (best.Excluded.Count > 0 ? "; masked out (members): " + best.ExcludedLine() : ""));
                    bool ok2 = best.Over2Pct <= 0.2, ok8 = best.Over8Pct <= 0.01;
                    if (!(ok2 && ok8)) Info($"BD2 {who} {part}: by renderer {best.Named()}");
                    string detail = $"{best.Over2} px over 2 ({F(best.Over2Pct, "F4")} %), {best.Over8} over 8 ({F(best.Over8Pct, "F4")} %), footprint {best.Footprint}, bake only {best.BakeOnly}";
                    if (!ok2 && ok8)
                    {
                        // over the 2-level bar alone: the swap test names a cutout edge of a decoded texture (ruled by the lead 04:4x)
                        var sw = Swapped(h, bestView, bi, SourceAndBake(h), members, out int swappedTextures);
                        Info($"BD2 {who} {part}: the source drawing the bake's decoded textures in place of its {swappedTextures} compressed ones: {sw.Line()}, bake only {sw.BakeOnly}");
                        if (swappedTextures > 0 && sw.Over2Pct <= 0.2 && sw.Over8 == 0 && sw.BakeOnly <= 5)
                        {
                            bd2Warns++;
                            Warn($"BD2W{bd2Warns} {who} {part}: decoded texture at a cutout edge: against the source {best.Over2} px over 2 ({F(best.Over2Pct, "F3")} %), {best.Over8} over 8, max {best.Max}, " +
                                 $"bake only {best.BakeOnly}; with the source drawing the bake's decoded textures {sw.Over2} over 2, {sw.Over8} over 8, max {sw.Max}, bake only {sw.BakeOnly} (BQ21's native blocks would remove it)");
                            Check($"BD2 {who} {part}: BD1's 8-level bar against the character with the other parts hidden (the 2-level excess: WARN BD2W{bd2Warns})", true, detail);
                            continue;
                        }
                    }
                    if (!ok8)
                    {
                        // where (printed only): the same swap test on a part over the 8-level bar; the verdict stays the bars'
                        var sw8 = Swapped(h, bestView, bi, SourceAndBake(h), members, out int swapped8);
                        Info($"BD2 {who} {part} (over the 8-level bar; printed only): the source drawing the bake's decoded textures in place of its {swapped8} compressed ones: {sw8.Line()}, bake only {sw8.BakeOnly}; by renderer {sw8.Named()}");
                        Bd2Order(h, bestView, bi, SourceAndBake(h), members, who, part);
                    }
                    Check($"BD2 {who} {part}: BD1's bars against the character with the other parts hidden", ok2 && ok8, detail);
                }
                finally
                {
                    if (bi?.Key != null) { h.Session.Delete(new[] { bi.Key }); h.Rt.ApplyNow(); }
                    bi?.Job?.Dispose();
                    if (masked) { h.Session.Set(key, "chara.state.clothes", saved); h.Rt.ApplyNow(); c = Chara(h, key); }
                }
            }
            Check($"BD2 {who} present: a part of every kind judged (footprint >= 2,000 px)", kinds.All(judgedKinds.Contains), $"kinds {string.Join(", ", kinds.OrderBy(x => x))}; judged {string.Join(", ", judgedKinds.OrderBy(x => x))}");
        }
        finally { h.Dispose(); }
    }

    static void Bd9()
    {
        // HERO107 judged (ruled by the lead 04:4x: data not yet read); BR-Chan's line printed beside it
        Bd9Of(true, true);
        Bd9Of(false, false);
    }

    static void Bd9Of(bool male, bool judged)
    {
        string who = male ? "HERO107" : "BR-Chan";
        var h = NewHost();
        BakeItem bi = null;
        try
        {
            string key = LoadChara(h, male);
            var c = Chara(h, key);
            EyesOpen(h, c);
            var root = c.Root.transform;
            var v = FullView(root);
            var srcAll = c.Root.GetComponentsInChildren<Renderer>(true).ToList();
            Color32[] dry;
            using (new Only(SourceAndBake(h), new HashSet<Renderer>(srcAll))) dry = Shot(h, v);
            h.Ok(new { cmd = "wet.attach", key, preset = "water" }, $"BD9 {who} wet.attach");
            h.Ok(new { cmd = "wet.start", key }, $"BD9 {who} wet.start");
            h.Frames(150);
            // the eyes open again before the capture (frames ran)
            EyesOpen(h, c);
            // the scene clock paused (not wet.dry: the stain stays), one apply: the source stays as it is captured (ruled by the lead 03:4x)
            h.Ok(new { cmd = "scene.pause", paused = true }, $"BD9 {who} scene.pause");
            h.Frames(1);
            c = Chara(h, key);
            var wet = h.Rt.GetPart<WetPart>();
            var films = wet != null && wet.Root ? wet.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(f => f.enabled && f.gameObject.activeInHierarchy).ToList() : new List<SkinnedMeshRenderer>();
            Color32[] stained;
            var everything = SourceAndBake(h).Concat(films).ToList();
            using (new Only(everything, new HashSet<Renderer>(srcAll.Concat(films)))) stained = Shot(h, v);
            int changed = 0;
            for (int i = 0; i < dry.Length; i++) if (Diff(dry[i], stained[i]) > 2) changed++;
            if (judged) Check($"BD9 {who} present: the stain changes >= 1,000 px", changed >= 1000, changed + " px");
            else Info($"BD9 {who} (printed beside): the stain changes {changed} px");
            bi = BakeToItem(h, c, new BakeOptions(), false, "bd9");
            var cap = bi.Job.Capture;
            int omitted = cap.Count("filmsOmitted");
            bool finding = cap.Findings.Any(f => f.Code == "bake.filmOmitted");
            string filmText = $"films {films.Count}, left out {omitted}, finding {finding}";
            bool filmOk = films.Count == 0 ? omitted == 0 : omitted == films.Count && finding;
            int blocks = cap.Counts.TryGetValue("mat.withBlock", out int nb) ? nb : 0;
            // the composite: the bake against the stained character with its films hidden
            var r = Compare(h, v, cap.Live.Where(x => x).ToList(), bi.Renderers, true, everything);
            Measure($"BD9 {who} the stained bake against the stained character without its films: " + r.Line());
            string detail = $"materials taken from a property block {blocks}; {r.Over2} px over 2 ({F(r.Over2Pct, "F4")} %), {r.Over8} over 8; by renderer {r.Named(5)}";
            if (judged)
            {
                Check($"BD9 {who} the film is left out with its finding (one per bake)", filmOk, filmText);
                Check($"BD9 {who} the stained composite is baked (BD1's bars against the character, films hidden)", blocks > 0 && r.Over2Pct <= 0.2 && r.Over8Pct <= 0.01, detail);
            }
            else Info($"BD9 {who} (printed beside): {filmText}; {(blocks > 0 && r.Over2Pct <= 0.2 && r.Over8Pct <= 0.01 ? "within" : "over")} BD1's bars: {detail}");
        }
        finally
        {
            try { h.Call(new { cmd = "scene.pause", paused = false }); } catch (Exception) { }
            h.Dispose();
            bi?.Job?.Dispose();
        }
    }

    static bool Bits3(Vector3[] a, Vector3[] b) => a == null ? b == null : b != null && a.Length == b.Length && a.Zip(b, (p, q) => BitsEqual(p.x, q.x) && BitsEqual(p.y, q.y) && BitsEqual(p.z, q.z)).All(x => x);
    static bool Bits4(Vector4[] a, Vector4[] b, int dim) => a == null ? b == null : b != null && a.Length == b.Length && a.Zip(b, (p, q) => Enumerable.Range(0, dim).All(c => BitsEqual(p[c], q[c]))).All(x => x);

    static void Bd15()
    {
        foreach (var male in new[] { false, true })
        {
            string who = male ? "HERO107" : "BR-Chan";
            var h = NewHost();
            BakeJob job = null;
            try
            {
                var c = Chara(h, LoadChara(h, male));
                EyesOpen(h, c);
                job = BakeJob.OfChara(c, new BakeOptions());
                var cap = job.Capture;
                var b = cap.Bake;
                var routes = cap.Counts.Where(kv => kv.Key.StartsWith("mesh.route.", StringComparison.Ordinal)).OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => kv.Key.Substring("mesh.route.".Length) + " " + kv.Value).ToList();
                Info($"BD15 {who}: meshes per route: {string.Join(", ", routes)}");
                int own = 0, unskin = 0, cut = 0;
                var bad = new List<string>();
                var seen = new HashSet<int>();
                for (int i = 0; i < b.Renderers.Count && i < cap.Live.Count; i++)
                {
                    int mi = b.Renderers[i].Mesh;
                    if (mi < 0 || mi >= b.Meshes.Count || !seen.Add(mi)) continue;
                    var md = b.Meshes[mi].Data;
                    if (md == null) continue;
                    if (md.Route == "bake+api") unskin++;
                    if (md.Route != "own") continue;
                    own++;
                    var smr = cap.Live[i] as SkinnedMeshRenderer;
                    var mesh = smr ? smr.sharedMesh : null;
                    if (!mesh) { bad.Add(b.Meshes[mi].Name + ": no source mesh"); continue; }
                    if (!BakeMeshes.BlendShapesOff(smr, mesh)) bad.Add(mesh.name + ": a blend shape weighs more than 0");
                    if (!Bits3(md.V, mesh.vertices)) bad.Add(mesh.name + " positions");
                    var nn = mesh.normals; if (!Bits3(md.N, nn.Length > 0 ? nn : null)) bad.Add(mesh.name + " normals");
                    var tt = mesh.tangents; if (!Bits4(md.T, tt.Length > 0 ? tt : null, 4)) bad.Add(mesh.name + " tangents");
                    for (int ch = 0; ch < 8; ch++)
                    {
                        var a = VertexAttribute.TexCoord0 + ch;
                        if (!mesh.HasVertexAttribute(a)) { if (md.Uv[ch] != null) bad.Add(mesh.name + " uv" + ch + " stored but absent"); continue; }
                        int dim = mesh.GetVertexAttributeDimension(a);
                        var uv = new List<Vector4>();
                        mesh.GetUVs(ch, uv);
                        if (md.UvDim[ch] != dim || !Bits4(md.Uv[ch], uv.ToArray(), dim)) bad.Add(mesh.name + " uv" + ch);
                    }
                    if (mesh.HasVertexAttribute(VertexAttribute.Color) ? (md.C == null || !md.C.SequenceEqual(mesh.colors32)) : md.C != null) bad.Add(mesh.name + " colours");
                    // weights: the mesh's own influences (a vertex with more than four keeps its four largest, renormalized: counted apart)
                    var per = mesh.GetBonesPerVertex();
                    var all = mesh.GetAllBoneWeights();
                    int o = 0, wbad = 0;
                    for (int k = 0; k < per.Length && md.W != null && k < md.W.Length; k++)
                    {
                        int nb = per[k];
                        if (nb > 4) { cut++; o += nb; continue; }
                        var w = md.W[k];
                        var ws = new[] { (w.boneIndex0, w.weight0), (w.boneIndex1, w.weight1), (w.boneIndex2, w.weight2), (w.boneIndex3, w.weight3) };
                        for (int q = 0; q < 4; q++)
                        {
                            bool same = q < nb ? ws[q].Item1 == all[o + q].boneIndex && BitsEqual(ws[q].Item2, all[o + q].weight) : ws[q].Item2 == 0f;
                            if (!same) { wbad++; break; }
                        }
                        o += nb;
                    }
                    if (md.W == null || md.W.Length != mesh.vertexCount || wbad > 0) bad.Add(mesh.name + $" weights ({wbad} vertices differ)");
                    var bp = mesh.bindposes;
                    if (md.Bp == null || md.Bp.Length != bp.Length || !md.Bp.Zip(bp, (x, y) => Enumerable.Range(0, 16).All(e => BitsEqual(x[e], y[e]))).All(x => x)) bad.Add(mesh.name + " bind poses");
                    if (md.Idx.Length != mesh.subMeshCount) bad.Add(mesh.name + " sub-meshes");
                    else for (int sm = 0; sm < mesh.subMeshCount; sm++)
                            if (md.Topo[sm] != mesh.GetTopology(sm) || !md.Idx[sm].SequenceEqual(mesh.GetIndices(sm, true))) bad.Add(mesh.name + " sub-mesh " + sm);
                }
                Check($"BD15 {who} present: >= 1 mesh by the own route and >= 1 by the unskin route", own >= 1 && unskin >= 1, $"own {own}, unskin (bake+api) {unskin}");
                Check($"BD15 {who}: every mesh by the own route equals the drawn sharedMesh's data in bits", own > 0 && bad.Count == 0,
                    $"{own} meshes; vertices with more than four influences (their four largest kept) {cut}" + (bad.Count > 0 ? "; differ: " + string.Join("; ", bad.Take(10)) : ""));
            }
            finally { job?.Dispose(); h.Dispose(); }
        }
    }

    static void Bd10()
    {
        var h = NewHost();
        BakeItem bi = null;
        try
        {
            using (MosaicTest.PreviewWorlds())
            {
                string key = LoadChara(h, false);
                var c = Chara(h, key);
                var hips = c.Context.BodyBones["cf_J_Hips"];
                var before = hips.position;
                var part = h.Rt.GetPart<RagdollPart>();
                // the Limp preset before the body is built (ruled by the lead 03:4x): it falls, as RagdollTest's bodies do
                var block = ((CharaNodeState)h.Rt.Model.Find(key).State).Ragdoll.Clone();
                RagdollPreset.ApplyTo(block.Values, RagdollPreset.Limp);
                var limp = h.Rt.Session.Commands.Execute(new HS2Next.Scenes.RagdollEditCommand(key, block), HS2Next.Scenes.ExecuteOptions.NotRecorded);
                Check("BD10 the Limp preset set", limp.Ok, limp.Code + " " + limp.Message);
                h.Rt.ApplyNow();
                var built = part?.Build(key, HS2Next.Scenes.ExecuteOptions.NotRecorded);
                if (!Check("BD10 a body is built on the character", built != null && built.Ok && part.BodyOf(key) != null, built == null ? "no ragdoll part" : built.Code + " " + built.Message)) return;
                h.Frames(40);
                double moved = (hips.position - before).magnitude;
                string how = "the fall alone";
                if (moved < 10.0)
                {
                    // RagdollTest's push (RK1) when the fall alone does not reach the bar
                    var body = part.BodyOf(key);
                    body.Push(body.Part("chest"), new Vector3(0f, 0f, -1500f));
                    h.Frames(40);
                    Info($"BD10 the fall alone moved the hips {F(moved)} units: pushed (chest, 1500 back) as RagdollTest's RK1");
                    moved = (hips.position - before).magnitude;
                    how = "the fall and the push";
                }
                Check("BD10 present: the body moved the bones >= 10 units from the animation", moved >= 10.0, F(moved) + " units (hips), " + how);
                bi = BakeToItem(h, c, new BakeOptions(), false, "bd10");
                var cap = bi.Job.Capture;
                var nodes = BakeNodes(bi.It.Instance.transform);
                double worst = 0;
                string worstName = "";
                int compared = 0;
                for (int i = 0; i < cap.LiveNodes.Count && i < nodes.Count; i++)
                {
                    var live = cap.LiveNodes[i];
                    if (!live) continue;
                    double d = (nodes[i].position - live.position).magnitude;
                    compared++;
                    if (d > worst) { worst = d; worstName = live.name; }
                }
                Check("BD10 the bake's bone transforms equal the drawn ones <= 1e-5", compared == cap.Bake.Nodes.Count && worst <= 1e-5, $"{compared} nodes, worst {F(worst, "E2")} units ({worstName})");
            }
        }
        finally { h.Dispose(); bi?.Job?.Dispose(); }
    }

    // ------------------------------------------------------------ BB3 (render): textures shared by blob (BQ21)

    static void Render_Share()
    {
        Group("BB3", "textures shared by blob: the two pieces of a cut of a whole character, the items of two bakes of one character", Bb3Shared);
    }

    /// <summary>What prototypes hold of textures: per prototype its entries with a built texture and its distinct textures; over all, the
    /// Texture objects built, by key (blob and sampler state), the blobs, and the textures their materials draw.</summary>
    sealed class TexHold
    {
        public readonly List<int> Entries = new List<int>();
        public readonly List<int> DistinctPer = new List<int>();
        public readonly List<HashSet<string>> BlobsPer = new List<HashSet<string>>();
        public readonly HashSet<Texture> Objects = new HashSet<Texture>();
        public readonly HashSet<Texture> Drawn = new HashSet<Texture>();
        public readonly Dictionary<string, HashSet<Texture>> ByKey = new Dictionary<string, HashSet<Texture>>(StringComparer.Ordinal);
        public readonly HashSet<string> Blobs = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>Video bytes when every prototype builds one texture per entry (as before the table), computed from formats.</summary>
        public long PerEntryBytes;

        public long Bytes => Objects.Sum(t => BakeTextures.VideoBytes(t));

        public static TexHold Of(IEnumerable<BakePrototype> ps)
        {
            var h = new TexHold();
            foreach (var p in ps)
            {
                int n = 0;
                var mine = new HashSet<Texture>();
                var blobs = new HashSet<string>(StringComparer.Ordinal);
                if (p?.Bake != null)
                    foreach (var t in p.Bake.Textures)
                    {
                        if (!t.Built) continue;
                        n++;
                        mine.Add(t.Built);
                        blobs.Add(t.Blob ?? "");
                        h.Objects.Add(t.Built);
                        h.Blobs.Add(t.Blob ?? "");
                        h.PerEntryBytes += BakeTextures.VideoBytes(t.Built);
                        string k = BakeCache.TextureKey(t.Blob, t) ?? "";
                        if (!h.ByKey.TryGetValue(k, out var set)) h.ByKey[k] = set = new HashSet<Texture>();
                        set.Add(t.Built);
                    }
                h.Entries.Add(n);
                h.DistinctPer.Add(mine.Count);
                h.BlobsPer.Add(blobs);
                if (p != null && p.Root)
                    foreach (var r in p.Root.GetComponentsInChildren<Renderer>(true))
                        foreach (var m in r.sharedMaterials)
                        {
                            if (!m) continue;
                            foreach (var name in m.GetTexturePropertyNames())
                            {
                                var x = m.GetTexture(name);
                                if (x) h.Drawn.Add(x);
                            }
                        }
            }
            return h;
        }
    }

    static string MiB(long bytes) => F(bytes / 1048576.0, "F1");

    /// <summary>The checks of one case: Present (each prototype references >= 100 textures), one Texture2D per distinct texture blob and
    /// sampler state, the materials draw only those, the runtime's table holds exactly them.</summary>
    static void SharedChecks(string what, List<BakePrototype> ps, BakeCache cache)
    {
        var h = TexHold.Of(ps);
        Check($"BB3 present ({what}): two prototypes, each references >= 100 textures", ps.Count == 2 && ps.All(p => p != null) && h.Entries.All(n => n >= 100),
            $"texture entries with a built texture {string.Join(", ", h.Entries)} (distinct textures per prototype {string.Join(", ", h.DistinctPer)})");
        int twoWays = h.ByKey.Keys.Select(k => k.Split('/')[0]).GroupBy(b => b).Count(g => g.Count() > 1);
        Check($"BB3 {what}: exactly one Texture2D per distinct texture blob (and sampler state)", h.ByKey.Count > 0 && h.ByKey.Values.All(s => s.Count == 1) && h.Objects.Count == h.ByKey.Count,
            $"textures held {h.Objects.Count}, distinct blobs {h.Blobs.Count}, distinct blob and sampler states {h.ByKey.Count} (blobs sampled two ways {twoWays}); " +
            $"video {MiB(h.Bytes)} MiB (computed from formats); one texture per prototype and entry would be {h.Entries.Sum()} textures, {MiB(h.PerEntryBytes)} MiB");
        if (h.BlobsPer.Count == 2) Info($"BB3 {what}: blobs of the first {h.BlobsPer[0].Count}, of the second {h.BlobsPer[1].Count}, in both {h.BlobsPer[0].Count(h.BlobsPer[1].Contains)}");
        Check($"BB3 {what}: the prototypes' materials draw only those textures", h.Drawn.Count > 0 && h.Drawn.IsSubsetOf(h.Objects),
            $"drawn {h.Drawn.Count}, not among them {h.Drawn.Count(t => !h.Objects.Contains(t))}");
        var table = new HashSet<Texture>(cache.HeldTextures);
        Check($"BB3 {what}: the runtime's table holds exactly them", table.SetEquals(h.Objects),
            $"table {cache.TexturesHeld} ({MiB(cache.TextureVideoBytes)} MiB), made {cache.TexturesMade}, handed out again {cache.TexturesShared}, released {cache.TexturesReleased}");
    }

    static void Bb3Shared()
    {
        // (1) the two pieces of a cut of the live whole character (cut.make, as the app runs it): a plane through its middle, front to its
        // side, so each piece keeps most of its parts
        var h = NewHost();
        try
        {
            string key = LoadChara(h);
            var c = Chara(h, key);
            h.Frames(2);
            var cache = BakeCache.Of(h.Rt, true);
            var ms0 = new Dictionary<string, double>(cache.Ms, StringComparer.Ordinal);
            int made0 = cache.TexturesMade, shared0 = cache.TexturesShared;
            var root = c.Root.transform;
            var rs = c.Root.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled).ToList();
            var bounds = rs[0].bounds;
            foreach (var r in rs) bounds.Encapsulate(r.bounds);
            var pos = new Vector3(root.position.x, bounds.center.y, root.position.z);
            var rot = root.rotation * Quaternion.Euler(0f, 0f, 90f);
            var cut = h.Ok(new { cmd = "cut.make", key, shape = new { kind = "plane", pos = new[] { pos.x, pos.y, pos.z }, rot = new[] { rot.x, rot.y, rot.z, rot.w } } },
                "BB3 cut.make of BR-Chan by a plane through her middle, front to side");
            var pieces = (cut["pieces"] as Newtonsoft.Json.Linq.JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
            h.Rt.ApplyNow();
            var ps = pieces.Select(k => BakeCache.PrototypeOf(h.Rt.FindItem(k))).ToList();
            SharedChecks("the two pieces of one cut", ps, cache);
            double D(string k) => (cache.Ms.TryGetValue(k, out var v) ? v : 0) - (ms0.TryGetValue(k, out var w) ? w : 0);
            double total = cut.Value<double?>("ms") ?? -1;
            double capture = D("capture"), finish = D("finish"), build = D("build"), inT = D("inflate.textures"), inM = D("inflate.meshes"), up = D("upload");
            Info($"BB3 the cut's time: {F(total, "F0")} ms = capture {F(capture, "F0")} (of it the capture's textures built {F(D("capture.upload"), "F0")}, waiting on the encoders " +
                 $"{F(D("encode.wait"), "F0")}) + finish {F(finish, "F0")} + the pieces' prototypes {F(build, "F0")} (of it blobs read: textures {F(inT, "F0")}, meshes {F(inM, "F0")}; " +
                 $"textures built from blobs {F(up, "F0")}) + the rest {F(total - capture - finish - build, "F0")} (the cut's run and the pieces' blobs, the scene's work); " +
                 $"texture encoding on workers {F(D("encode"), "F0")} (overlapping the capture); textures made {cache.TexturesMade - made0}, handed out again {cache.TexturesShared - shared0}");
        }
        finally { h.Dispose(); }

        // (2) the items of two bakes of the same character (bake.make twice, in the scene only), then their release
        h = NewHost();
        try
        {
            string key = LoadChara(h);
            h.Frames(2);
            var cache = BakeCache.Of(h.Rt, true);
            var a = h.Ok(new { cmd = "bake.make", key, hide = false, store = false, name = "bb3 a" }, "BB3 bake.make of BR-Chan (first)");
            int madeA = cache.TexturesMade;
            var b = h.Ok(new { cmd = "bake.make", key, hide = false, store = false, name = "bb3 b" }, "BB3 bake.make of BR-Chan (second)");
            string ia = (string)a["items"]?[0], ib = (string)b["items"]?[0];
            h.Rt.ApplyNow();
            var pa = BakeCache.PrototypeOf(h.Rt.FindItem(ia));
            var pb = BakeCache.PrototypeOf(h.Rt.FindItem(ib));
            Check("BB3 two bakes: two prototypes of two mains", pa != null && pb != null && pa != pb && pa.Main != pb.Main, $"{pa?.Main?.Substring(0, 8)} / {pb?.Main?.Substring(0, 8)}");
            Info($"BB3 two bakes: textures made by the first {madeA}, by the second {cache.TexturesMade - madeA}");
            SharedChecks("the items of two bakes of one character", new List<BakePrototype> { pa, pb }, cache);
            var all = TexHold.Of(new[] { pa, pb });
            var first = TexHold.Of(new[] { pa });
            // with the default graveyard an undone item is kept for a redo and keeps its prototype (as designed); printed
            h.Ok(new { cmd = "undo" }, "BB3 undo of the second bake (default graveyard)");
            h.Rt.ApplyNow();
            int keptUsers = pb?.Users ?? -1;
            int releasedDefault = cache.ReleaseUnused();
            Info($"BB3 with the default graveyard ({h.Rt.Settings.GraveyardMaxNodes} nodes) the undone item keeps its prototype: users {keptUsers}, released {releasedDefault}");
            h.Ok(new { cmd = "redo" }, "BB3 redo of the second bake");
            h.Rt.ApplyNow();
            // the graveyard limit at 0 before the undo, so the undone item is destroyed (ruled by the lead 03:4x)
            int graveyard = h.Rt.Settings.GraveyardMaxNodes;
            h.Rt.Settings.GraveyardMaxNodes = 0;
            h.Ok(new { cmd = "undo" }, "BB3 undo of the second bake (graveyard limit 0)");
            h.Rt.ApplyNow();
            h.Rt.Settings.GraveyardMaxNodes = graveyard;
            int released = cache.ReleaseUnused();
            Check("BB3 the second bake's prototype released: the first's textures stay, the table holds the first's alone",
                released == 1 && pb != null && !pb.Alive && first.Objects.Count > 0 && first.Objects.All(t => t) && new HashSet<Texture>(cache.HeldTextures).SetEquals(first.Objects),
                $"released {released}, alive {first.Objects.Count(t => t)} of {first.Objects.Count}, table {cache.TexturesHeld}");
            h.Ok(new { cmd = "scene.new" }, "BB3 scene.new");
            h.Rt.ApplyNow();
            Check("BB3 the last prototype released destroys its textures: the table is empty", all.Objects.Count > 0 && all.Objects.All(t => !t) && cache.TexturesHeld == 0,
                $"alive {all.Objects.Count(t => t)} of {all.Objects.Count}, table {cache.TexturesHeld}, released {cache.TexturesReleased}");
        }
        finally { h.Dispose(); }
    }
}

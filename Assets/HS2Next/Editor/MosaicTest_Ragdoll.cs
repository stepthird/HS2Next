// MosaicTest.render, the rendering rules under a physics body. Images stay in memory and are judged by numbers. The fixture is WV3m's: the
// composite with its WithoutMosaic control, clothes hidden on BR-Chan, HERO107 with his member on as in the clothed views, the marker on the members' own texture for him, the production
// pipeline, 960 x 540. The clock is paused and stepped, a world made for each walk. No floor is drawn, so the view can stand under a body that lies on its member.
//   RV1  a body writes the 16 bones and the hips' position and nothing else, by every transform under the root read just before and just after each of its stages, against a frame with the
//        stage resting; a bone the guard names is not written, and the finding comes once.
//   RV3  36 moments of a limp fall and 36 of an active body pushed and knocked down, each walked twice: for (a) and for (b). The view follows the member, 16 units away, along its normal or
//        40 or 70 degrees off it, whichever of the nine shows the member most at that moment, chosen by the size of the footprint alone, before any marker is read, so a moment's footprint
//        is the member as the body holds it then. A moment under 150 px of footprint
//        is not judged; a walk with under 30 judged moments fails. (a): 0 marker
//        pixels in the footprint, the marker shows in that footprint with the mosaic left out, every drawn member's block at least the frame's floor. (b): the coverage holds the footprint
//        dilated by 1.5 blocks, the changed pixels form flat blocks.
//   RV4  the coverage of (b) at least the footprint, at every moment of the same walks.
//   RV5  the blocks at capture scale k 1 and 2, read at moments of the (b) walks: in output pixels at least the floor, both methods; the colours of the footprint in the final output beside them.
//   RV7  effects over a member: droplets and a tracer drawn over it change no block and no group, 0 marker pixels, flat blocks; a stain from a ray at the member makes no map on it.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next;
using HS2Next.Chara;
using HS2Next.Chara.Mosaic;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Motion;
using HS2Next.Motion.Animation;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Scenes.Wet;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using static MakerTestKit;
using Object = UnityEngine.Object;

public static partial class MosaicTest
{
    const int RvW = 960, RvH = 540, RvMoments = 36, RvLeastJudged = 30, RvLeastFootprint = 150, RvLimpFrames = 2, RvActiveFrames = 8, RvSettle = 180;
    const float RvLeastMoved = 5f, RvView = 16f, RvDt = 1f / 60f;

    static void RagdollGroups(MakerCoreTest.Harness h)
    {
        if (!Want("RV1") && !Want("RV3") && !Want("RV7") && !OnlyNamed("RVF")) return;
        using (PreviewWorlds())
        {
            if (OnlyNamed("RVF")) Guard("RVF the first render of a process", () => RvFirst(h));
            if (Want("RV1")) Guard("RV1 what a body writes", () => Rv1(h));
            if (Want("RV3")) Guard("RV3, RV4, RV5 the rule at physics poses", () => Rv3(h));
            if (Want("RV7")) Guard("RV7 effects over a member", () => Rv7(h));
        }
    }

    sealed class PreviewScope : IDisposable
    {
        readonly Func<Scene> make = RagdollWorld.MakeScene;
        readonly Action<Scene> free = RagdollWorld.FreeScene;
        public PreviewScope()
        {
            RagdollWorld.MakeScene = () => EditorSceneManager.NewPreviewScene();
            RagdollWorld.FreeScene = s => EditorSceneManager.ClosePreviewScene(s);
        }
        public void Dispose()
        {
            RagdollWorld.MakeScene = make;
            RagdollWorld.FreeScene = free;
        }
    }

    /// <summary>While it lives a ragdoll world stands in a preview scene: the kind edit mode can make.</summary>
    internal static IDisposable PreviewWorlds() => new PreviewScope();

    enum RvCase { Limp, Active }

    /// <summary>One character on a paused clock with the ragdoll's and the stains' parts; the view's fixture of WV3m on it.</summary>
    sealed class RvScene : IDisposable
    {
        public readonly MakerEditTest.Node Node;
        public readonly RagdollPart Part = new RagdollPart();
        public readonly WetPart Wet = new WetPart();
        public readonly bool Male;
        public readonly string Label;
        public readonly MosaicMember Focus;
        readonly Transform hips;
        readonly Vector3 localView;
        readonly CharaAnimation anim;
        readonly Vec3 pos0, rot0;

        public RvScene(MakerCoreTest.Harness h, string label, string file, bool male, StudioAnime idle = null)
        {
            Label = label;
            Male = male;
            Node = NodeOf(h, file);
            Rt.AddPart(Part, Part.Dispose);
            Rt.AddPart(Wet, Wet.Dispose);
            var ctx = Ctx;
            HideClothes(ctx);
            var nDan = HS2Next.Chara.Edit.EditContext.Find(ctx.Body.transform, "N_dan");
            if (male && nDan) nDan.gameObject.SetActive(true);
            foreach (var s in ctx.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) s.forceMatrixRecalculationPerRender = true;
            Focus = Member(ctx, male ? "cm_o_dan00" : "o_mnpb");
            anim = CharaAnimation.Of(ctx.Root);
            if (idle != null && anim != null)
            {
                anim.Load(idle);
                anim.Speed = 1f;
            }
            Rt.SetPaused(true);
            Frames(RvSettle);   // the neck look eases in: until it has, two walks would not start from one pose
            hips = ctx.BodyBones["cf_J_Hips"];
            var n = (male ? new Vector3(0f, -.8f, .6f) : PatchPose(ctx).n).normalized;
            localView = Quaternion.Inverse(hips.rotation) * n;
            var doc = Node.Real.Node.Doc.Transform;
            pos0 = doc.Pos;
            rot0 = doc.Rot;
        }

        public SceneRuntime Rt => Node.Runtime;
        public string Id => Node.Id;
        public CharaContext Ctx => Node.Real.Context;
        public RagdollBody Body => Part.BodyOf(Id);
        /// <summary>The first render of this scene was read.</summary>
        public bool FirstRead;
        /// <summary>The hash of the parts at the last walk's first moment.</summary>
        public ulong FirstMoment { get; private set; }
        /// <summary>The last walk's start against the walk before it of that kind, and the world it ran in.</summary>
        public string StartNote { get; private set; } = "";
        public string WorldNote { get; private set; } = "";
        readonly Dictionary<RvCase, Dictionary<Transform, (Vector3 p, Quaternion r, Vector3 s)>> starts = new Dictionary<RvCase, Dictionary<Transform, (Vector3, Quaternion, Vector3)>>();
        readonly HashSet<string> sceneHandles = new HashSet<string>();

        // every transform under the root, as the pose sources leave it before the body is made
        Dictionary<Transform, (Vector3 p, Quaternion r, Vector3 s)> Bones()
        {
            var d = new Dictionary<Transform, (Vector3, Quaternion, Vector3)>();
            foreach (var t in Ctx.Root.GetComponentsInChildren<Transform>(true)) d[t] = (t.localPosition, t.localRotation, t.localScale);
            return d;
        }

        void CompareStart(RvCase kind, Dictionary<Transform, (Vector3 p, Quaternion r, Vector3 s)> now)
        {
            if (!starts.TryGetValue(kind, out var was))
            {
                starts[kind] = now;
                StartNote = $"the first walk of this kind, {now.Count} transforms";
                return;
            }
            starts[kind] = now;
            int differ = 0, signOnly = 0, gone = 0;
            double most = 0, moved = 0;
            var names = new List<(double deg, double move, string name)>();
            foreach (var kv in now)
            {
                if (!kv.Key || !was.TryGetValue(kv.Key, out var o)) { gone++; continue; }
                var (p, r, sc) = kv.Value;
                bool sameP = p.x == o.p.x && p.y == o.p.y && p.z == o.p.z, sameR = r.x == o.r.x && r.y == o.r.y && r.z == o.r.z && r.w == o.r.w;
                bool sameS = sc.x == o.s.x && sc.y == o.s.y && sc.z == o.s.z;
                if (sameP && sameR && sameS) continue;
                double deg = RagdollTest.Deg(r, o.r), move = (p - o.p).magnitude;
                if (sameP && sameS && deg == 0) { signOnly++; continue; }
                differ++;
                most = Math.Max(most, deg);
                moved = Math.Max(moved, move);
                names.Add((deg, move, kv.Key.name));
            }
            var top = names.OrderByDescending(x => x.deg + x.move).Take(14).Select(x => $"{x.name} {x.deg:G4} deg, {x.move:G4}");
            StartNote = $"against the walk before: {differ} of {now.Count} transforms differ in bits, {signOnly} by sign alone, {gone} not found, largest {most:G4} deg, {moved:G4} units: {string.Join("; ", top)}";
        }
        public void Frames(int n) { if (n > 0) Rt.Step(n, RvDt); }

        /// <summary>The member's centre now, and the way to look at it: its normal on BR-Chan, a fixed direction in the hips' frame on HERO107.</summary>
        public Vector3 Centre => MosaicCamera.RegionMatrix(Focus).MultiplyPoint3x4(Focus.Region.Centroid);
        public Vector3 View => Male ? (hips.rotation * localView).normalized : PatchPose(Ctx).n;
        public Vector3 Front => Ctx.Root.transform.forward;

        /// <summary>The member's own view, four 40 degrees off it and four 70 degrees off it.</summary>
        public Vector3[] Views()
        {
            var n = View;
            var x = Vector3.Cross(n, Mathf.Abs(n.y) < .9f ? Vector3.up : Vector3.right).normalized;
            var y = Vector3.Cross(n, x);
            float c = Mathf.Cos(40f * Mathf.Deg2Rad), s = Mathf.Sin(40f * Mathf.Deg2Rad), c7 = Mathf.Cos(70f * Mathf.Deg2Rad), s7 = Mathf.Sin(70f * Mathf.Deg2Rad);
            return new[]
            {
                n, (c * n + s * x).normalized, (c * n - s * x).normalized, (c * n + s * y).normalized, (c * n - s * y).normalized,
                (c7 * n + s7 * x).normalized, (c7 * n - s7 * x).normalized, (c7 * n + s7 * y).normalized, (c7 * n - s7 * y).normalized,
            };
        }

        /// <summary>A walk from the same start in a world of its own: <paramref name="at"/> at each moment. Returns a hash of the parts at the moments.</summary>
        public ulong Walk(RvCase kind, Action<int> at, int moments = RvMoments)
        {
            anim?.Restart();
            Frames(2);
            CompareStart(kind, Bones());
            bool none = Part.World == null;
            Part.Build(Id, ExecuteOptions.NotRecorded);
            var body = Body;
            if (body == null) throw new InvalidOperationException("no body on " + Label);
            var w = Part.World;
            bool fresh = none && w.Steps == 0 && w.ShotCounter == 0 && w.Bodies.Count == 1 && sceneHandles.Add(w.Scene.handle.ToString());
            WorldNote = $"{(fresh ? "a fresh world" : "NOT A FRESH WORLD")}: none before it {none}, steps {w.Steps}, shots {w.ShotCounter}, bodies {w.Bodies.Count}, scene {w.Scene.handle}, {sceneHandles.Count} scenes so far";
            if (kind == RvCase.Limp) body.GoLimp();
            ulong hash = 14695981039346656037UL;
            var chest = body.Part(RagdollParts.Chest);
            for (int i = 0; i < moments; i++)
            {
                if (i > 0) Frames(kind == RvCase.Limp ? RvLimpFrames : RvActiveFrames);
                if (kind == RvCase.Limp && i == 1) body.Push(chest, -Front * 150f);
                if (kind == RvCase.Active && i == 2) body.Push(chest, -Front * 300f);
                if (kind == RvCase.Active && i == 3) body.Knock();
                foreach (var p in body.Parts) hash = Mix(Mix(hash, p.Tr.position), p.Tr.rotation);
                if (i == 0) FirstMoment = hash;
                at(i);
            }
            Part.Release(Id, false, ExecuteOptions.NotRecorded);
            Node.Session.Set(Id, "transform.pos", pos0, ExecuteOptions.NotRecorded);
            Node.Session.Set(Id, "transform.rot", rot0, ExecuteOptions.NotRecorded);
            Rt.Apply();
            return hash;
        }

        static ulong Mix(ulong h, Vector3 v) => Bit(Bit(Bit(h, v.x), v.y), v.z);
        static ulong Mix(ulong h, Quaternion q) => Bit(Bit(Bit(Bit(h, q.x), q.y), q.z), q.w);
        static ulong Bit(ulong h, float f) { unchecked { return (h ^ (uint)BitConverter.SingleToInt32Bits(f)) * 1099511628211UL; } }

        public void Dispose()
        {
            MosaicWide.Enabled = false;
            Node.Dispose();
        }
    }

    /// <summary>Flat yellow members through the view's own camera: what the member covers at this moment, eroded by 1 as WV3m.</summary>
    static bool[] RvFootprint(Camera cam, int w, int h, CharaContext ctx, int erode = 1)
    {
        if (!yellow) yellow = Flat("mosaic_test_flat_yellow", new Color32(255, 255, 0, 255));
        var fp = MarkerRender(cam, w, h, ctx, yellow).Select(c => c.r > 0.9f && c.g > 0.9f && c.b < 0.1f).ToArray();
        return erode > 0 ? Erode(fp, w, h, erode) : fp;
    }

    /// <summary>
    /// The camera on the member from the direction that shows it most now, read at half size, by the size of the footprint alone, before any marker is read. Returns which of the nine;
    /// <paramref name="fixedView"/> gets the footprint along the member's own view at full size.
    /// </summary>
    static int RvAim(RvScene s, Camera cam, out int fixedView)
    {
        var c = s.Centre;
        var views = s.Views();
        Aim(cam, c, views[0], RvView);
        fixedView = RvFootprint(cam, RvW, RvH, s.Ctx).Count(x => x);
        int best = 0, most = -1;
        for (int i = 0; i < views.Length; i++)
        {
            Aim(cam, c, views[i], RvView);
            int n = RvFootprint(cam, RvW / 2, RvH / 2, s.Ctx, 0).Count(x => x);
            if (n > most) { most = n; best = i; }
        }
        Aim(cam, c, views[best], RvView);
        return best;
    }

    /// <summary>Marker pixels with the members drawing flat green through their own lit texture mode: what the reading sees at this light.</summary>
    static int RvLit(Camera cam, CharaContext ctx, Texture2D green, bool[] fp)
    {
        var saved = new List<(Material m, Texture t, Vector2 sc, Vector2 of, float mode, Color c)>();
        foreach (var mm in MosaicSet.StateOf(ctx).Members)
            foreach (var mat in mm.Renderer.sharedMaterials)
            {
                if (!mat || saved.Exists(x => x.m == mat)) continue;
                saved.Add((mat, mat.GetTexture("_MainTex"), mat.GetTextureScale("_MainTex"), mat.GetTextureOffset("_MainTex"), mat.GetFloat("_MosaicMode"), mat.GetColor("_Color")));
                mat.SetTexture("_MainTex", green); mat.SetTextureScale("_MainTex", Vector2.one); mat.SetTextureOffset("_MainTex", Vector2.zero);
                mat.SetFloat("_MosaicMode", 1f); mat.SetColor("_Color", Color.white);
            }
        try { return Markers(Render(cam, RvW, RvH), fp); }
        finally
        {
            foreach (var x in saved)
            {
                x.m.SetTexture("_MainTex", x.t); x.m.SetTextureScale("_MainTex", x.sc); x.m.SetTextureOffset("_MainTex", x.of);
                x.m.SetFloat("_MosaicMode", x.mode); x.m.SetColor("_Color", x.c);
            }
        }
    }

    /// <summary>The drawn members of the last camera render: least block in render pixels, the frame's floor, how many, how many under it.</summary>
    static (float least, float floor, int members, int under) RvBlocks(Camera cam)
    {
        var f = MosaicCamera.Last;
        if (f == null || f.Camera != cam) return (0f, 0f, 0, 0);
        float least = float.MaxValue;
        int n = 0, under = 0;
        foreach (var g in f.Groups)
            foreach (var m in g.Members)
            {
                n++;
                if (m.BlockPx < f.FloorPx) under++;
                least = Mathf.Min(least, m.BlockPx);
            }
        return (n > 0 ? least : 0f, f.FloorPx, n, under);
    }

    /// <summary>(b) in the view: the coverage against the footprint dilated by 1.5 blocks, the pixels the pass changes against the group's grid.</summary>
    static (bool ran, float block, int groups, int cover, int miss, int blocks, int mixed, int changed) RvWide(Camera cam, int w, int h, bool[] fp)
    {
        MosaicWide.Enabled = true;
        MosaicWide.DebugView = true;
        int e0 = MosaicWideFeature.EnqueuedCount;
        var debug = Render(cam, w, h);
        MosaicWide.DebugView = false;
        if (MosaicWideFeature.EnqueuedCount == e0 || MosaicWideFeature.LastGroups.Count == 0)
        {
            MosaicWide.Enabled = false;
            return (false, 0f, 0, 0, 0, 0, 0, 0);
        }
        var g = MosaicWideFeature.LastGroups[0];
        int groups = MosaicWideFeature.LastGroups.Count;
        float B = g.Block;
        var cov = debug.Select(x => x.r > 0.5f).ToArray();
        int cover = cov.Count(x => x), ir = Mathf.CeilToInt(1.5f * B), miss = 0;
        float r2 = 1.5f * B * 1.5f * B;
        var seen = (bool[])cov.Clone();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!fp[y * w + x]) continue;
                for (int dy = -ir; dy <= ir; dy++)
                    for (int dx = -ir; dx <= ir; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= w || yy >= h || dx * dx + dy * dy > r2) continue;
                        if (!seen[yy * w + xx]) { miss++; seen[yy * w + xx] = true; }
                    }
            }
        MosaicWide.Enabled = false;
        var off = Render(cam, w, h);
        MosaicWide.Enabled = true;
        var on = Render(cam, w, h);
        MosaicWide.Enabled = false;
        var sets = new Dictionary<long, HashSet<Color>>();
        int changed = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (off[i] == on[i]) continue;
                changed++;
                long kx = (long)Mathf.Floor((x + 0.5f - g.Anchor.x) / B), ky = (long)Mathf.Floor((y + 0.5f - g.Anchor.y) / B);
                long key = kx * 1000000 + ky;
                if (!sets.TryGetValue(key, out var set)) sets[key] = set = new HashSet<Color>();
                set.Add(on[i]);
            }
        return (true, B, groups, cover, miss, sets.Count, sets.Values.Count(s => s.Count > 1), changed);
    }

    // ------------------------------------------------------------ RV3, RV4, RV5

    static void Rv3(MakerCoreTest.Harness h)
    {
        var idle = CharaAnimation.Resolve(h.Catalog.LoadStudioLists(), ContentIds.Vanilla, 19, 0, 0);
        if (!Check("RV3 the idle row resolves", idle != null && !idle.IsH, "")) return;
        foreach (var (label, file, male) in new[] { ("BR-Chan", FemaleCard, false), ("HERO107", MaleCard, true) })
            using (var s = new RvScene(h, label, file, male, idle))
            {
                if (!Check($"RV3 {label} builds, the member and the animation are there", s.Node.Ok && s.Focus != null, s.Node.Error ?? "")) continue;
                NewLight();
                var ctx = s.Ctx;
                var green = Flat("rv_green", new Color32(0, 255, 0, 255));
                foreach (var kind in new[] { RvCase.Limp, RvCase.Active })
                {
                    string what = $"{label} {kind.ToString().ToLowerInvariant()}";
                    ulong a = RvMarkers(s, what, green);
                    ulong a0 = s.FirstMoment;
                    Info($"RV3 {what} the walk for (a): {s.WorldNote}; start {s.StartNote}");
                    ulong b = RvCoverage(s, what);
                    Info($"RV3 {what} the walk for (b): {s.WorldNote}; start {s.StartNote}");
                    Info($"RV3 {what}: the two walks' poses {(a == b ? "equal" : "differ")}: {a:x16}, {b:x16}; at the first moment {(a0 == s.FirstMoment ? "equal" : "differ")}: {a0:x16}, {s.FirstMoment:x16}");
                    if (a != b) Warn($"RV3 {what}", "the walk for (a) and the walk for (b) are not the same motion: each judged its own moments");
                }
            }
    }

    /// <summary>
    /// The first render of a scene, read on its own line, before the walks: with nothing rendered before it in the process the skin is not yet right, and a capture at that moment is a real
    /// path. Its footprint is the same view's; the member's pixels are compared with the second render as well as for markers, so a 0 there is not 0 on pixels that read as no number.
    /// </summary>
    static void RvFirstRender(RvScene s, Camera cam, Texture marker)
    {
        var ctx = s.Ctx;
        Aim(cam, s.Centre, s.View, RvView);
        var first = Render(cam, RvW, RvH);
        var second = Render(cam, RvW, RvH);
        var fp = RvFootprint(cam, RvW, RvH, ctx);
        int fpn = fp.Count(x => x), differ = 0, inside = 0, nan = 0, ctrl = 0;
        for (int i = 0; i < first.Length; i++)
        {
            if (first[i] == second[i]) continue;
            differ++;
            if (fp[i]) inside++;
            if (float.IsNaN(first[i].r) || float.IsNaN(first[i].g) || float.IsNaN(first[i].b)) nan++;
        }
        int mk = Markers(first, fp);
        WithoutMosaic(ctx, marker, () => ctrl = Markers(Render(cam, RvW, RvH), fp));
        Check($"RV3 {s.Label}: the first render of the scene: 0 marker pixels in the footprint, the member's pixels equal the second render's", fpn >= RvLeastFootprint && mk == 0 && inside == 0 && ctrl > 0,
            $"{differ} px differ from the second render, {nan} of them no number, {inside} in the footprint of {fpn} px, markers {mk}, left out {ctrl}");
    }

    // the walk for (a)
    static ulong RvMarkers(RvScene s, string what, Texture2D green)
    {
        var ctx = s.Ctx;
        Texture marker;
        var saved = new List<(Material m, Texture t)>();
        if (s.Male)
        {
            marker = Wv3mChecker(2048, 5);
            foreach (var mm in MosaicSet.StateOf(ctx).Members)
                foreach (var mat in mm.Renderer.sharedMaterials)
                    if (mat && mat.HasProperty("_MainTex") && !saved.Exists(x => x.m == mat)) { saved.Add((mat, mat.GetTexture("_MainTex"))); mat.SetTexture("_MainTex", marker); }
        }
        else
        {
            marker = Composite(ctx, 2048, 6, Cells(3), Skin);
            UseComposite(ctx, marker);
        }
        var cam = NewCam(false, AntialiasingMode.None);
        MosaicWide.Enabled = false;
        if (!s.FirstRead)
        {
            s.FirstRead = true;
            RvFirstRender(s, cam, marker);
        }
        int judged = 0, notJudged = 0, marks = 0, noCtrl = 0, dim = 0, under = 0, noBlock = 0, lumN = 0, leastCtrl = int.MaxValue, leastFp = int.MaxValue, blockMoments = 0;
        float leastLit = 1f, leastBlock = float.MaxValue, leastRatio = float.MaxValue, moved = 0f;
        int noise = 0, judgedFixed = 0;
        double lum = 0;
        var first = Vector3.zero;
        var lines = new List<string>();
        ulong hash = s.Walk(what.EndsWith("limp") ? RvCase.Limp : RvCase.Active, i =>
        {
            var c = s.Centre;
            if (i == 0) first = c;
            moved = Mathf.Max(moved, (c - first).magnitude);
            int view = RvAim(s, cam, out int fixedFp);
            if (fixedFp >= RvLeastFootprint) judgedFixed++;
            var warm = Render(cam, RvW, RvH);
            var fp = RvFootprint(cam, RvW, RvH, ctx);
            var px = Render(cam, RvW, RvH);
            int again = RvDiffer(warm, px, RvW, RvH).any;
            noise = Math.Max(noise, again);
            var (least, floor, members, low) = RvBlocks(cam);
            int fpn = fp.Count(x => x), mk = Markers(px, fp), ctrl = 0;
            WithoutMosaic(ctx, marker, () => ctrl = Markers(Render(cam, RvW, RvH), fp));
            int lit = RvLit(cam, ctx, green, fp);
            string state = s.Body.State.ToString();
            if (members > 0)
            {
                blockMoments++;
                under += low;
                leastBlock = Mathf.Min(leastBlock, least);
                leastRatio = Mathf.Min(leastRatio, least / Mathf.Max(floor, 1e-6f));
            }
            if (fpn < RvLeastFootprint) notJudged++;
            else
            {
                judged++;
                marks += mk;
                if (ctrl == 0) noCtrl++;
                if (lit * 2 < fpn) dim++;
                leastLit = Mathf.Min(leastLit, lit / (float)fpn);
                leastCtrl = Math.Min(leastCtrl, ctrl);
                leastFp = Math.Min(leastFp, fpn);
                for (int k = 0; k < px.Length; k++) if (fp[k]) { lum += px[k].r + px[k].g + px[k].b; lumN++; }
                if (members == 0) noBlock++;
            }
            lines.Add($"(a) {what} m{i:00} {state}: view {view}, member at ({c.x:F2}, {c.y:F2}, {c.z:F2}), along its own view {fixedFp} px, footprint {fpn} px{(fpn < RvLeastFootprint ? " NOT JUDGED" : "")}, markers {mk}, left out {ctrl}, flat green read {lit}; F {floor:F1}, least block {least:F1}, members {members}; two renders differ by {again} px");
        });
        foreach (var (m, t) in saved) m.SetTexture("_MainTex", t);
        double level = lumN > 0 ? lum / (3 * lumN) : 0;
        Info($"RV3 {what}: moments the view along the member's own normal alone would judge: {judgedFixed}; judged with the nine views: {judged}");
        Check($"RV3 {what} (a): 0 marker pixels in each moment's footprint, the marker shows with the mosaic left out in that footprint ({judged} moments judged, {notJudged} not judged)",
            judged >= RvLeastJudged && marks == 0 && noCtrl == 0 && level > 0.1,
            $"{marks} marker pixels, {noCtrl} moments with no marker left out, least left out {leastCtrl}, least footprint {leastFp} px, mean level {level:F3}");
        Check($"RV3 {what} (a) control: flat green through the members' own lit texture mode reads as a marker on at least half of each moment's footprint", judged > 0 && dim == 0, $"{dim} moments under half, least share {leastLit:F3}");
        Check($"RV3 {what}: every drawn member's block >= the frame's F at each moment ({blockMoments} moments)", blockMoments >= RvLeastJudged && under == 0 && noBlock == 0,
            $"{under} under, {noBlock} judged moments with no member in the frame, least block {leastBlock:F1} px, least block / F {leastRatio:F2}");
        Check($"RV3 {what}: the member moved at least {RvLeastMoved:F0} units over the walk", moved >= RvLeastMoved, $"{moved:F2} units");
        Check($"RV3 {what}: two renders of a moment are the same picture, so a footprint belongs to its moment", noise == 0, $"most {noise} px differ");
        foreach (var l in lines) Info("RV3 " + l);
        return hash;
    }

    // the walk for (b), RV4 and RV5 on it
    static ulong RvCoverage(RvScene s, string what)
    {
        var ctx = s.Ctx;
        if (!s.Male) UseComposite(ctx, Composite(ctx, 2048, 0, Gradient(2048), Skin));
        var cam = NewCam(true, AntialiasingMode.None);
        int vw = 0, notJudged = 0, miss = 0, blocks = 0, mixed = 0, ran = 0, groupsMax = 0, thin = 0, leastCover = int.MaxValue, leastFp = int.MaxValue, reads = 0, underOut = 0, wideUnder = 0, colourWarn = 0;
        float minB = float.MaxValue, leastOut = float.MaxValue, leastWideOut = float.MaxValue;
        var lines = new List<string>();
        float outFloor = Mathf.Max(4f, Mathf.Ceil(Mathf.Max(RvW, RvH) / 100f));
        ulong hash = s.Walk(what.EndsWith("limp") ? RvCase.Limp : RvCase.Active, i =>
        {
            MosaicWide.Enabled = false;
            var c = s.Centre;
            RvAim(s, cam, out _);
            Render(cam, RvW, RvH);
            var fp = RvFootprint(cam, RvW, RvH, ctx, 0);
            int fpn = fp.Count(x => x);
            if (fpn < RvLeastFootprint) { notJudged++; lines.Add($"(b) {what} m{i:00}: footprint {fpn} px NOT JUDGED"); return; }
            var w = RvWide(cam, RvW, RvH, fp);
            vw++;
            if (w.ran) ran++;
            miss += w.miss; blocks += w.blocks; mixed += w.mixed;
            groupsMax = Math.Max(groupsMax, w.groups);
            minB = Mathf.Min(minB, w.block);
            if (w.cover < fpn) thin++;
            leastCover = Math.Min(leastCover, w.cover);
            leastFp = Math.Min(leastFp, fpn);
            lines.Add($"(b) {what} m{i:00}: block {w.block:F1} px, footprint {fpn} px, coverage {w.cover} px, uncovered {w.miss} px, changed {w.changed} px, {w.blocks} blocks, {w.mixed} mixed");
            if (i % 9 != 0) return;
            foreach (int k in new[] { 1, 2 })
            {
                using (MosaicCamera.OutputScope(cam, k))
                {
                    var fpk = RvFootprint(cam, RvW * k, RvH * k, ctx, 0);
                    Shader.SetGlobalFloat(UnlitId, 1f);
                    var px = Render(cam, RvW * k, RvH * k);
                    Shader.SetGlobalFloat(UnlitId, 0f);
                    var (least, floor, members, low) = RvBlocks(cam);
                    MosaicWide.Enabled = true;
                    Render(cam, RvW * k, RvH * k);
                    MosaicWide.Enabled = false;
                    float wide = MosaicWideFeature.LastGroups.Count > 0 ? MosaicWideFeature.LastGroups[0].Block / k : 0f;
                    if (k > 1)
                    {
                        px = Downscale(px, RvW * k, RvH * k, k);
                        fpk = Downscale(fpk.Select(x => x ? Color.white : Color.black).ToArray(), RvW * k, RvH * k, k).Select(x => x.r > 0.99f).ToArray();
                    }
                    var colours = new HashSet<Color>();
                    int area = 0;
                    for (int q = 0; q < px.Length; q++) if (fpk[q]) { colours.Add(px[q]); area++; }
                    int bound = area / Mathf.Max(1, (int)(outFloor * outFloor / 2f)) + 16 + (k > 1 ? area / 4 : 0);
                    reads++;
                    if (members == 0 || least / k < outFloor) underOut++;
                    if (wide < outFloor) wideUnder++;
                    leastOut = Mathf.Min(leastOut, least / k);
                    leastWideOut = Mathf.Min(leastWideOut, wide);
                    if (colours.Count > bound) colourWarn++;
                    lines.Add($"RV5 {what} m{i:00} k {k}: (a) least block {least / k:F1} output px, (b) block {wide:F1} output px, F {outFloor}; {colours.Count} colours in {area} px of the output (bound {bound})");
                }
            }
        });
        Check($"RV3 {what} (b): the coverage holds each moment's footprint dilated by 1.5 blocks, the changed pixels form flat blocks ({vw} moments judged, {notJudged} not judged)",
            vw >= RvLeastJudged && ran == vw && miss == 0 && blocks > 0 && mixed == 0,
            $"uncovered {miss} px, {blocks} blocks, {mixed} mixed, least block {minB:F1} px, most groups {groupsMax}, the pass ran at {ran} moments");
        Check($"RV4 {what}: with render.mosaicWide on the coverage is at least the footprint at each moment ({vw} moments judged)", vw >= RvLeastJudged && thin == 0, $"{thin} moments thinner, least coverage {leastCover} px, least footprint {leastFp} px");
        Check($"RV5 {what}: the block on the final output >= F at capture k 1, 2 ({reads} readings)", reads >= 4 && underOut == 0 && wideUnder == 0, $"(a) {underOut} under, least {leastOut:F1} output px; (b) {wideUnder} under, least {leastWideOut:F1} output px; F {outFloor}");
        if (colourWarn > 0) Warn($"RV5 {what}", $"{colourWarn} readings with more colours in the footprint than the bound of MR2: an indirect measure, the blocks are the verdict");
        foreach (var l in lines) Info(l.StartsWith("RV5") ? l : "RV3 " + l);
        return hash;
    }

    // ------------------------------------------------------------ RVF

    /// <summary>
    /// The first render of a process, on its own with -only RVF, which leaves nothing before it: BR-Chan at the first moment of a limp walk, the view along the member's normal, the marker
    /// on. -rvFirst plain: the first render is the picture; footprint: the footprint comes first; nobody: as plain with no body; wide: the first render is method (b)'s coverage.
    /// </summary>
    static void RvFirst(MakerCoreTest.Harness h)
    {
        string mode = Arg("-rvFirst") ?? "plain";
        var idle = CharaAnimation.Resolve(h.Catalog.LoadStudioLists(), ContentIds.Vanilla, 19, 0, 0);
        using (var s = new RvScene(h, "BR-Chan", FemaleCard, false, idle))
        {
            NewLight();
            var ctx = s.Ctx;
            var marker = Composite(ctx, 2048, 6, Cells(3), Skin);
            UseComposite(ctx, marker);
            var cam = NewCam(mode == "wide", AntialiasingMode.None);
            MosaicWide.Enabled = false;
            Info($"RVF mode {mode}: no render before this line in this process: MosaicCamera calls {MosaicCamera.Calls}, graphics {SystemInfo.graphicsDeviceType}");
            if (mode == "nobody")
            {
                s.Frames(2);
                RvFirstAt(s, cam, marker, mode);
            }
            else s.Walk(RvCase.Limp, i => RvFirstAt(s, cam, marker, mode), 1);
        }
    }

    static void RvFirstAt(RvScene s, Camera cam, Texture marker, string mode)
    {
        var ctx = s.Ctx;
        Aim(cam, s.Centre, s.View, RvView);
        int w = RvW, h = RvH;
        bool wide = mode == "wide";
        bool[] before = mode == "footprint" ? RvFootprint(cam, w, h, ctx, 0) : null;
        if (wide) { MosaicWide.Enabled = true; MosaicWide.DebugView = true; }
        var first = Render(cam, w, h);
        var second = Render(cam, w, h);
        var third = Render(cam, w, h);
        if (wide) { MosaicWide.DebugView = false; MosaicWide.Enabled = false; }
        var raw = before ?? RvFootprint(cam, w, h, ctx, 0);
        var again = RvFootprint(cam, w, h, ctx, 0);
        var fp = Erode(raw, w, h, 1);
        // the character's own pixels: the picture against the same view with every renderer under it off
        var off = new List<Renderer>();
        foreach (var r in ctx.Root.GetComponentsInChildren<Renderer>(true)) if (r.enabled) { r.enabled = false; off.Add(r); }
        var empty = Render(cam, w, h);
        foreach (var r in off) r.enabled = true;
        var drawn = Render(cam, w, h);
        int rawN = raw.Count(x => x), fpN = fp.Count(x => x), fpMoved = 0;
        for (int i = 0; i < raw.Length; i++) if (raw[i] != again[i]) fpMoved++;
        var grid = new int[9, 16];
        int differ = 0, onChara = 0, inRaw = 0, inFp = 0, x0 = w, y0 = h, x1 = -1, y1 = -1, chara = 0;
        double sum = 0, top = 0;
        var meanFirst = new Vector3();
        var meanSecond = new Vector3();
        int nan = 0;
        for (int i = 0; i < first.Length; i++)
        {
            bool isChara = empty[i] != drawn[i];
            if (isChara) chara++;
            if (first[i] == second[i]) continue;
            int x = i % w, y = i / w;
            differ++;
            grid[y * 9 / h, x * 16 / w]++;
            if (isChara) onChara++;
            if (raw[i]) inRaw++;
            if (fp[i]) inFp++;
            x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
            if (float.IsNaN(first[i].r) || float.IsNaN(first[i].g) || float.IsNaN(first[i].b)) { nan++; continue; }
            double d = Mathf.Abs(first[i].r - second[i].r) + Mathf.Abs(first[i].g - second[i].g) + Mathf.Abs(first[i].b - second[i].b);
            sum += d;
            top = Math.Max(top, d);
            meanFirst += new Vector3(first[i].r, first[i].g, first[i].b);
            meanSecond += new Vector3(second[i].r, second[i].g, second[i].b);
        }
        int differ23 = RvDiffer(second, third, w, h).any;
        Info($"RVF {mode}: first against second render {differ} px, second against third {differ23} px; the character covers {chara} px, the footprint {rawN} px, eroded {fpN} px, the footprint moved by {fpMoved} px between two reads");
        if (differ > 0)
        {
            int rest = Math.Max(1, differ - nan);
            Info($"RVF {mode}: box x {x0}-{x1}, y {y0}-{y1} (y from the bottom); on the character {onChara}, off it {differ - onChara}; inside the footprint {inRaw}, eroded {inFp}; not a number on the first render {nan}, the rest {differ - nan}: mean difference {sum / rest:F4}, most {top:F4} (sum of r, g, b)");
            Info($"RVF {mode}: mean colour of the rest, first ({meanFirst.x / rest:F3}, {meanFirst.y / rest:F3}, {meanFirst.z / rest:F3}), second ({meanSecond.x / rest:F3}, {meanSecond.y / rest:F3}, {meanSecond.z / rest:F3})");
            for (int y = 8; y >= 0; y--) Info($"RVF {mode} grid row {y}: {string.Join(" ", Enumerable.Range(0, 16).Select(x => grid[y, x].ToString().PadLeft(4)))}");
        }
        if (wide)
        {
            int cover = first.Count(c => c.r > 0.5f), cover2 = second.Count(c => c.r > 0.5f), thin = 0;
            for (int i = 0; i < raw.Length; i++) if (raw[i] && !(first[i].r > 0.5f)) thin++;
            Check($"RVF {mode}: on the first render the coverage holds the footprint", cover > 0 && thin == 0, $"coverage {cover} px on the first render, {cover2} on the second, footprint {rawN} px, {thin} px of it uncovered");
            return;
        }
        int mk1 = Markers(first, fp), mk2 = Markers(second, fp), ctrl = 0;
        WithoutMosaic(ctx, marker, () => ctrl = Markers(Render(cam, w, h), fp));
        var (least, floor, members, low) = RvBlocks(cam);
        Check($"RVF {mode}: 0 marker pixels in the footprint on the first render", fpN >= RvLeastFootprint && mk1 == 0 && ctrl > 0, $"first render {mk1}, second {mk2}, footprint {fpN} px{(before != null ? " taken before it" : " taken after it")}, left out {ctrl}");
    }

    // ------------------------------------------------------------ RV1

    sealed class RvProbe : IMotionStage
    {
        readonly Action act;
        public RvProbe(int phase, Action act) { Phase = (MotionPhase)phase; this.act = act; }
        public MotionPhase Phase { get; }
        public string Name => "rv.probe";
        public bool Enabled { get; set; } = true;
        public void Step(in MotionFrame frame) => act();
        public void Reset() { }
    }

    /// <summary>Every transform under the root, read before a stage and compared after it, by bits.</summary>
    sealed class RvDiff
    {
        readonly Transform[] all;
        readonly Vector3[] pos, scale;
        readonly Quaternion[] rot;
        public readonly HashSet<Transform> Rotated = new HashSet<Transform>(), Moved = new HashSet<Transform>(), Scaled = new HashSet<Transform>();

        public RvDiff(Transform root)
        {
            all = root.GetComponentsInChildren<Transform>(true);
            pos = new Vector3[all.Length];
            scale = new Vector3[all.Length];
            rot = new Quaternion[all.Length];
        }

        public int Count => all.Length;

        public void Before()
        {
            for (int i = 0; i < all.Length; i++)
            {
                if (!all[i]) continue;
                pos[i] = all[i].localPosition; rot[i] = all[i].localRotation; scale[i] = all[i].localScale;
            }
        }

        public void After()
        {
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (!t) continue;
                var p = t.localPosition; var q = t.localRotation; var s = t.localScale;
                if (p.x != pos[i].x || p.y != pos[i].y || p.z != pos[i].z) Moved.Add(t);
                if (q.x != rot[i].x || q.y != rot[i].y || q.z != rot[i].z || q.w != rot[i].w) Rotated.Add(t);
                if (s.x != scale[i].x || s.y != scale[i].y || s.z != scale[i].z) Scaled.Add(t);
            }
        }

        public void Clear() { Rotated.Clear(); Moved.Clear(); Scaled.Clear(); }
    }

    static IMotionStage RvStage(CharaContext ctx, string name) => ctx.Motion.Stages.FirstOrDefault(x => x.Name == name);

    static void Rv1(MakerCoreTest.Harness h)
    {
        foreach (var (label, file, male) in new[] { ("BR-Chan", FemaleCard, false), ("HERO107", MaleCard, true) })
            using (var s = new RvScene(h, label, file, male))
            {
                if (!Check($"RV1 {label} builds", s.Node.Ok, s.Node.Error ?? "")) continue;
                var ctx = s.Ctx;
                var r = s.Part.Build(s.Id, ExecuteOptions.NotRecorded);
                var body = s.Body;
                if (!Check($"RV1 {label} a body", body != null, r.Message ?? "")) continue;
                body.GoLimp();
                body.Push(body.Part(RagdollParts.Chest), -s.Front * 150f);
                s.Frames(60);
                var bones = new HashSet<Transform>(body.Parts.Select(p => p.Bone));
                var hips = ctx.BodyBones["cf_J_Hips"];
                var diff = new RvDiff(ctx.Root.transform);
                foreach (var (from, to, stage) in new[] { (-99, -97, "ragdoll.restore"), (409, 411, "ragdoll"), (955, 957, "ragdoll.end") })
                {
                    var a = new RvProbe(from, diff.Before);
                    var b = new RvProbe(to, diff.After);
                    ctx.Motion.Add(a);
                    ctx.Motion.Add(b);
                    var st = RvStage(ctx, stage);
                    if (!Check($"RV1 {label}: the stage {stage} is on the character", st != null, string.Join(", ", ctx.Motion.Stages.Select(x => x.Name)))) { ctx.Motion.Remove(a); ctx.Motion.Remove(b); continue; }
                    diff.Clear();
                    s.Frames(1);
                    var rotated = diff.Rotated.ToList(); var moved = diff.Moved.ToList(); var scaled = diff.Scaled.ToList();
                    int others = rotated.Concat(moved).Concat(scaled).Count(t => !bones.Contains(t));
                    if (stage == "ragdoll")
                        Check($"RV1 {label} {stage}: 16 + 1 channels written of {diff.Count} transforms", rotated.Count == 16 && moved.Count == 1 && moved[0] == hips && scaled.Count == 0 && others == 0,
                            $"{rotated.Count} rotations, {moved.Count} positions ({string.Join(", ", moved.Select(t => t.name))}), {scaled.Count} scales, {others} on other transforms: {string.Join(", ", rotated.Concat(moved).Where(t => !bones.Contains(t)).Take(8).Select(t => t.name))}");
                    else
                        Check($"RV1 {label} {stage}: writes no transform but the 16 bones", others == 0 && moved.All(t => t == hips) && scaled.Count == 0, $"{rotated.Count} rotations, {moved.Count} positions, {scaled.Count} scales, {others} on other transforms");
                    st.Enabled = false;
                    diff.Clear();
                    s.Frames(1);
                    st.Enabled = true;
                    Check($"RV1 {label} {stage} resting: the same frame writes nothing", diff.Rotated.Count == 0 && diff.Moved.Count == 0 && diff.Scaled.Count == 0, $"{diff.Rotated.Count} rotations, {diff.Moved.Count} positions");
                    ctx.Motion.Remove(a);
                    ctx.Motion.Remove(b);
                }
                s.Part.Release(s.Id, false, ExecuteOptions.NotRecorded);
            }

        // a bone the guard names: hips, so that the control moves it far
        using (var s = new RvScene(h, "BR-Chan", FemaleCard, false))
        {
            var ctx = s.Ctx;
            var hips = ctx.BodyBones["cf_J_Hips"];
            float Fall(bool guarded, out RagdollBody kept, out int findings, out HashSet<Transform> written)
            {
                kept = null;
                MeshRenderer mr = null;
                MeshFilter mf = null;
                if (guarded)
                {
                    var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    mf = hips.gameObject.AddComponent<MeshFilter>();
                    mf.sharedMesh = quad.GetComponent<MeshFilter>().sharedMesh;
                    Object.DestroyImmediate(quad);
                    mr = hips.gameObject.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = Keep(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
                    MosaicSet.Add(mr);
                }
                var at = hips.position;
                int before = s.Rt.Findings.Count(f => f.Code == RagdollFindings.Protected);
                s.Part.Build(s.Id, ExecuteOptions.NotRecorded);
                var body = s.Body;
                kept = body;
                body.GoLimp();
                body.Push(body.Part(RagdollParts.Chest), -s.Front * 150f);
                s.Frames(60);
                var diff = new RvDiff(ctx.Root.transform);
                var a = new RvProbe(409, diff.Before);
                var b = new RvProbe(411, diff.After);
                ctx.Motion.Add(a);
                ctx.Motion.Add(b);
                s.Frames(3);
                ctx.Motion.Remove(a);
                ctx.Motion.Remove(b);
                written = new HashSet<Transform>(diff.Rotated.Concat(diff.Moved));
                float away = (hips.position - at).magnitude;
                findings = s.Rt.Findings.Count(f => f.Code == RagdollFindings.Protected) - before;
                s.Part.Release(s.Id, false, ExecuteOptions.NotRecorded);
                if (mr)
                {
                    Object.DestroyImmediate(mr);
                    Object.DestroyImmediate(mf);
                    MosaicSet.Standalone();
                }
                return away;
            }
            float free = Fall(false, out _, out int noFinding, out var writtenFree);
            Check("RV1 control: without the fixture the bone moves at least 1 and is written, no finding", free >= 1f && writtenFree.Contains(hips) && noFinding == 0, $"moved {free:F2}, findings {noFinding}");
            float held = Fall(true, out _, out int once, out var writtenHeld);
            Check("RV1 a bone the guard names is not written, its part is listed, the finding comes once", !writtenHeld.Contains(hips) && held < 1e-3f && once == 1, $"moved {held:F4}, findings {once}, {writtenHeld.Count} bones written");
        }
    }

    // ------------------------------------------------------------ RV7

    static void Rv7(MakerCoreTest.Harness h)
    {
        foreach (var (label, file, male) in new[] { ("BR-Chan", FemaleCard, false), ("HERO107", MaleCard, true) })
            using (var s = new RvScene(h, label, file, male))
            {
                if (!Check($"RV7 {label} builds", s.Node.Ok && s.Focus != null, s.Node.Error ?? "")) continue;
                NewLight();
                var ctx = s.Ctx;
                Texture marker;
                var saved = new List<(Material m, Texture t)>();
                if (male)
                {
                    marker = Wv3mChecker(2048, 5);
                    foreach (var mm in MosaicSet.StateOf(ctx).Members)
                        foreach (var mat in mm.Renderer.sharedMaterials)
                            if (mat && mat.HasProperty("_MainTex") && !saved.Exists(x => x.m == mat)) { saved.Add((mat, mat.GetTexture("_MainTex"))); mat.SetTexture("_MainTex", marker); }
                }
                else
                {
                    marker = Composite(ctx, 2048, 6, Cells(3), Skin);
                    UseComposite(ctx, marker);
                }
                s.Part.Build(s.Id, ExecuteOptions.NotRecorded);
                s.Frames(5);
                var c = s.Centre;
                var n = s.View;
                var cam = NewCam(false, AntialiasingMode.None);
                MosaicWide.Enabled = false;
                Aim(cam, c, n, RvView);
                var right = cam.transform.right;
                var fp = RvFootprint(cam, RvW, RvH, ctx);
                int fpn = fp.Count(x => x);
                // a shot across the view just before the member, and blood between it and the view
                var shots = RagdollShots.For(s.Rt);
                var from = c + n * 1f + right * 40f;
                shots.Fire(from, -right, from, 181f, false);
                var fx = shots.Fx;
                fx.BeginShot(0, 1000);
                fx.Blood(c + n * 1.5f + right * .35f, -n, 36f, false);
                s.Frames(1);
                var warm = Render(cam, RvW, RvH);
                var on = Render(cam, RvW, RvH);
                int noise = RvDiffer(warm, on, RvW, RvH).any;
                var (leastOn, floorOn, membersOn, underOn) = RvBlocks(cam);
                var blocksOn = MosaicCamera.Last.Groups.SelectMany(g => g.Members).Select(m => m.BlockPx).ToList();
                int markersOn = Markers(on, fp), ctrl = 0;
                WithoutMosaic(ctx, marker, () => ctrl = Markers(Render(cam, RvW, RvH), fp));
                var fr = fx.Renderer;
                bool drawn = fr && fr.enabled;
                if (fr) fr.enabled = false;
                var off = Render(cam, RvW, RvH);
                var blocksOff = MosaicCamera.Last.Groups.SelectMany(g => g.Members).Select(m => m.BlockPx).ToList();
                if (fr) fr.enabled = true;
                int inside = 0, outside = 0;
                for (int i = 0; i < on.Length; i++)
                    if (on[i] != off[i]) { if (fp[i]) inside++; else outside++; }
                Check($"RV7 {label}: the effects are drawn in this view, over at most half of the member and beside it", drawn && noise == 0 && outside >= 100 && inside > 0 && inside * 2 <= fpn && fpn >= RvLeastFootprint, $"{outside} px differ beside the member, {inside} px over it, footprint {fpn} px, two renders differ by {noise} px, droplets {fx.Droplets}, tracers {fx.Tracers}, flashes {fx.Flashes}");
                Check($"RV7 {label} (a): the blocks equal, 0 marker pixels, the marker shows with the mosaic left out", blocksOn.Count > 0 && blocksOn.SequenceEqual(blocksOff) && underOn == 0 && markersOn == 0 && ctrl > 0, $"{membersOn} members, least block {leastOn:F1} px, F {floorOn:F1}, markers {markersOn}, left out {ctrl}");
                var camB = NewCam(true, AntialiasingMode.None);
                Aim(camB, c, n, RvView);
                var wOn = RvWide(camB, RvW, RvH, fp);
                if (fr) fr.enabled = false;
                var wOff = RvWide(camB, RvW, RvH, fp);
                if (fr) fr.enabled = true;
                Check($"RV7 {label} (b): the block and the coverage equal, the changed pixels form flat blocks", wOn.ran && wOff.ran && wOn.block == wOff.block && wOn.cover == wOff.cover && wOn.miss == 0 && wOn.mixed == 0, $"block {wOn.block:F1} / {wOff.block:F1} px, coverage {wOn.cover} / {wOff.cover} px, uncovered {wOn.miss}, {wOn.blocks} blocks, {wOn.mixed} mixed");

                // a stain from a ray at the member
                var ray = new Ray(c + n * RvView, -n);
                var done = s.Wet.AddRay(s.Id, ray, WetPreset.Blood, "shot1", ExecuteOptions.NotRecorded, (b, st) => WetEdits.Start(st));
                s.Rt.Apply();
                var rig = s.Wet.RigOf(s.Id);
                int maps = rig?.Runs.Sum(x => x.Live.Count) ?? 0;
                var members = MosaicSet.StateOf(ctx).Members.Select(m => m.Renderer).Where(r => r).ToList();
                int onMember = rig?.Slots.Values.Count(sl => members.Contains(sl.Target.Renderer) || MosaicSet.IsMember(sl.Target.Renderer) || MosaicSet.IsMosaicMaterial(sl.Target.Material)) ?? 0;
                int overrides = 0;
                var block = new MaterialPropertyBlock();
                foreach (var r in members)
                    for (int i = 0; i < r.sharedMaterials.Length; i++)
                    {
                        r.GetPropertyBlock(block, i);
                        if (!block.isEmpty && block.HasTexture("_MainTex")) overrides++;
                    }
                Check($"RV7 {label}: a stain from a ray at the member makes maps, none on a member", done.Status == CommandStatus.Ok && maps > 0 && onMember == 0 && overrides == 0, $"{done.Status} {done.Message}, {maps} maps, {rig?.Slots.Count ?? 0} slots, {onMember} on a member, {overrides} overrides");
                foreach (var (m, t) in saved) m.SetTexture("_MainTex", t);
                s.Part.Release(s.Id, false, ExecuteOptions.NotRecorded);
            }
    }

    /// <summary>The production pipeline and a harness around <paramref name="a"/>: what another row's render checks stand on.</summary>
    internal static void WithRendering(Action<MakerCoreTest.Harness> a)
    {
        using (var p = new Pipeline())
        {
            if (!Check("the production URP asset and a graphics device", p.Ok, SystemInfo.graphicsDeviceType.ToString())) return;
            var h = MakerCoreTest.Harness.Open();
            try { a(h); }
            finally { Cleanup(); h.Dispose(); }
        }
    }

    static (int any, int visible, RectInt box) RvDiffer(Color[] a, Color[] b, int w, int h)
    {
        int any = 0, visible = 0, x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == b[i]) continue;
            any++;
            if (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) < .02f) continue;
            visible++;
            int x = i % w, y = i / w;
            x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
        }
        return (any, visible, new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1));
    }

    /// <summary>What the effects draw: in a capture and in a puddle's mirror against the same frame without them, and each kind alone against its size (RD12).</summary>
    internal static (int view, int mirror, int droplets, List<(string what, int px, int least, int most)> sizes, List<string> lines) EffectsShow(MakerCoreTest.Harness h)
    {
        const int W = 960, H = 540;
        var lines = new List<string>();
        var sizes = new List<(string, int, int, int)>();
        using (var s = new RvScene(h, "BR-Chan", FemaleCard, false))
        {
            NewLight();
            s.Part.Build(s.Id, ExecuteOptions.NotRecorded);
            s.Frames(5);
            var ctx = s.Ctx;
            var c = s.Centre + Vector3.up * 3f;
            var shots = RagdollShots.For(s.Rt);
            int a, b, droplets;
            using (var rig = new WetMirrorRig(ctx.Root.transform.position.y - .1f, .5f))
            {
                var cam = NewCam(true, AntialiasingMode.None);
                var below = c + new Vector3(0f, -.8f, .6f).normalized * 16f;
                rig.Place(cam, c, below);
                Render(cam, W, H);
                var from = c + Vector3.right * 40f;
                shots.Fire(from, Vector3.left, from, 181f, false);
                var fx = shots.Fx;
                fx.BeginShot(0, 1000);
                for (int i = 0; i < 6; i++) fx.Blood(c + Vector3.right * ((i - 2.5f) * .4f), Vector3.up, 600f, false);
                s.Frames(1);
                var first = Render(cam, W, H);
                var firstMirror = rig.Image();
                var view = Render(cam, W, H);
                var mirror = rig.Image();
                var d0 = RvDiffer(first, view, W, H);
                var d1 = RvDiffer(firstMirror, mirror, rig.W, rig.H);
                lines.Add($"two renders of one frame, nothing changed: view {d0.any} px, mirror {d1.any} px");
                droplets = fx.Droplets;
                fx.Renderer.enabled = false;
                var viewOff = Render(cam, W, H);
                var mirrorOff = rig.Image();
                fx.Renderer.enabled = true;
                var dv = RvDiffer(view, viewOff, W, H);
                var dm = RvDiffer(mirror, mirrorOff, rig.W, rig.H);
                a = dv.any; b = dm.any;
                lines.Add($"view: {dv.any} px differ, {dv.visible} by more than 0.02, in {dv.box}; mirror {rig.W} x {rig.H}: {dm.any}, {dm.visible}, in {dm.box}");
            }
            shots.Clear();

            // each kind alone, in empty space, no post-processing
            var plain = NewCam(false, AntialiasingMode.None);
            var at = new Vector3(0f, 300f, 0f);
            float d = 16f, ppu = H / (2f * d * Mathf.Tan(Fov * .5f * Mathf.Deg2Rad)), p2 = ppu * ppu;
            plain.transform.position = at - Vector3.forward * d;
            plain.transform.rotation = Quaternion.identity;
            var holder = Keep(new GameObject("rd12_effects"));
            int Alone(Action<RagdollFx> draw)
            {
                var off = Render(plain, W, H);
                var fx = new RagdollFx(holder.transform);
                try
                {
                    fx.BeginShot(0, 7);
                    draw(fx);
                    fx.Draw();
                    return RvDiffer(Render(plain, W, H), off, W, H).visible;
                }
                finally { fx.Dispose(); }
            }
            sizes.Add(("a tracer 10 units long, 0.06 to 0.018 wide", Alone(f => f.TracerTo(at - Vector3.right * 5f, at + Vector3.right * 5f)), (int)(10f * .018f * p2), (int)(10f * .06f * p2)));
            sizes.Add(("a flash of 0.5 to 0.8 on three axes", Alone(f => f.FlashAt(at, Vector3.up)), (int)(.5f * .5f * p2 * .5f), (int)(3f * .8f * .8f * p2)));
            sizes.Add(("6 droplets of 0.05 to 0.18 with their tails", Alone(f => f.Blood(at, Vector3.right, 36f, false)), (int)(6f * Mathf.PI * .025f * .025f * p2 * .5f), (int)(6f * (Mathf.PI * .09f * .09f + .09f * .54f) * p2)));
            s.Part.Release(s.Id, false, ExecuteOptions.NotRecorded);
            return (a, b, droplets, sizes, lines);
        }
    }
}

// MosaicTest.render, a puddle's mirror. The mirror image follows the rendering rules as the main view does. Images stay in memory and are
// judged by numbers. Fixture: MR1's and MR3's (BR-Chan, clothes hidden, a code-made composite), MR7's clothed views, a synthetic member through MosaicSet.Add. The view stands above
// a floor plane and looks at the member's reflected position, so the hidden camera sees the member from below; the image is read from the mirror's target.
//   WV3   method (a): 0 marker pixels in the member's footprint inside the mirror image, the marker shows with the mosaic left out; method (b): the coverage holds the footprint dilated
//         by 1.5 blocks, the pixels the pass changes form flat blocks, 0 mixed, counted on the group's own mask (one group per run); MR7's clothed views.
//   WV3m  WV3's checks at moving poses: the character carried round orbits with the idle stepped between moments, each moment's footprint computed at that moment. Method (a):
//         0 marker pixels, the marker shows with the mosaic left out, at every moment with a footprint; method (b): coverage and flat blocks; every drawn member's block at
//         least the frame's floor. At least 30 moments judged per character. The view and the puddle are those of the player's WV6M, a light on the member's side so that flat green
//         through the members' own lit texture mode reads as a marker; the procedural member draws no texture.
//   WV3a  method (a)'s block in the mirror frame: every drawn member's block at least the frame's floor, equal within one power-of-two step to a plain camera at the reflected pose.
//   WV1   no map, override or film on a member: real members of both targets and a synthetic one.
//   WV2   MR1's marker test with a stain on the skin around the patches and a film on clothes over them.
//   WV4   the option off: the mirror image equals a render with the pass absent.
//   WV5   the flat runs in the puddle on the final output at mirror scales 0.25, 0.5, 1 and capture k 1, 2, 4.
//   WV6   WV3's check through the view, a capture and a recorded frame's call.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next;
using HS2Next.Chara;
using HS2Next.Chara.Mosaic;
using HS2Next.Content;
using HS2Next.Motion.Animation;
using HS2Next.Scenes.Runtime;
using HS2Next.Screenshots;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static MakerTestKit;
using Object = UnityEngine.Object;

/// <summary>A floor plane with a puddle in view; the mirror's image and frame of the last camera render.</summary>
internal sealed class WetMirrorRig : IDisposable
{
    public readonly float Floor;
    public readonly PlanarMirrors.Handle Handle;
    readonly GameObject disc;
    readonly Material discMaterial;
    readonly Mesh discMesh;
    readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
    RenderTexture keep;
    int enqueued;
    public int W, H, Renders;
    public bool Srgb;
    /// <summary>The groups of the wide pass in the mirror (empty when it did not run there).</summary>
    public readonly List<MosaicWideGroup> Groups = new List<MosaicWideGroup>();
    /// <summary>The floor of the mirror frame, its output scale and each drawn member's block.</summary>
    public float FramePx; public int OutputScale;
    public readonly List<(MosaicMember m, float px, float uv)> Blocks = new List<(MosaicMember, float, float)>();
    public Vector3 CameraPosition; public Quaternion CameraRotation; public int TargetW, TargetH, Mask;
    public Matrix4x4 View, Projection;

    public float Plane => Floor + PlanarMirrors.PlaneOffset;
    public Vector3 Reflect(Vector3 p) => new Vector3(p.x, 2f * Plane - p.y, p.z);

    public WetMirrorRig(float floor, float scale)
    {
        Floor = floor;
        Handle = PlanarMirrors.Request(this, floor, scale);
        discMesh = new Mesh { name = "wv_disc" };
        const int seg = 32;
        var v = new Vector3[seg + 1]; var uv = new Vector2[seg + 1]; var tri = new int[seg * 3];
        for (int i = 0; i < seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            v[i + 1] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); uv[i + 1] = new Vector2(1f, 0f);
            tri[i * 3] = 0; tri[i * 3 + 1] = 1 + (i + 1) % seg; tri[i * 3 + 2] = 1 + i;
        }
        discMesh.vertices = v; discMesh.uv = uv; discMesh.triangles = tri; discMesh.RecalculateBounds();
        disc = new GameObject("wv_puddle") { layer = 10 };
        disc.AddComponent<MeshFilter>().sharedMesh = discMesh;
        var mr = disc.AddComponent<MeshRenderer>();
        discMaterial = new Material(Shader.Find("HS2Next/Hidden/WetPuddle"));
        mr.sharedMaterial = discMaterial;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        block.SetFloat(PlanarMirrors.PlaneYId, Handle.PlaneY);
        mr.SetPropertyBlock(block);
        Handle.Puddles.Add(mr);
        PlanarMirrors.BeforeRender = Before;
        PlanarMirrors.AfterRender = After;
    }

    public Renderer Puddle => disc.GetComponent<MeshRenderer>();

    /// <summary>The puddle's colour, and its mirror strength.</summary>
    public void Look(Color c, float strength)
    {
        block.SetColor("_WetPuddleColor", c);
        block.SetFloat("_WetMirrorStrength", strength);
        Puddle.SetPropertyBlock(block);
    }

    /// <summary>The puddle at <paramref name="centre"/> (on the floor) with this radius.</summary>
    public void Lay(Vector3 centre, float radius)
    {
        disc.transform.position = new Vector3(centre.x, Floor + 0.015f, centre.z);
        disc.transform.localScale = Vector3.one * radius;
    }

    /// <summary>The view above the floor, the hidden camera at <paramref name="below"/> looking at <paramref name="c"/>; a puddle where the view's axis meets the floor.</summary>
    public void Place(Camera cam, Vector3 c, Vector3 below)
    {
        var view = Reflect(below); var target = Reflect(c);
        cam.transform.position = view;
        cam.transform.rotation = Quaternion.LookRotation(target - view, Vector3.up);
        float t = (view.y - Floor) / Mathf.Max(view.y - target.y, 1e-3f);
        var hit = Vector3.Lerp(view, target, t);
        disc.transform.position = new Vector3(hit.x, Floor + 0.015f, hit.z);
        disc.transform.localScale = Vector3.one * Mathf.Max(20f, (view - hit).magnitude);
    }

    void Before(Camera rc)
    {
        enqueued = MosaicWideFeature.EnqueuedCount;
        var f = MosaicCamera.FrameOf(rc);
        Blocks.Clear();
        FramePx = f?.FloorPx ?? 0f; OutputScale = f?.OutputScale ?? 0;
        if (f != null) foreach (var g in f.Groups) foreach (var m in g.Members) Blocks.Add((m, m.BlockPx, m.BlockUv));
        CameraPosition = rc.transform.position; CameraRotation = rc.transform.rotation;
        TargetW = rc.pixelWidth; TargetH = rc.pixelHeight; Mask = rc.cullingMask;
        View = rc.worldToCameraMatrix; Projection = rc.projectionMatrix;
    }

    void After(Camera rc, RenderTexture t)
    {
        Groups.Clear();
        if (MosaicWideFeature.EnqueuedCount > enqueued) Groups.AddRange(MosaicWideFeature.LastGroups);
        W = t.width; H = t.height; Renders++; Srgb = t.sRGB;
        if (!keep || keep.width != W || keep.height != H)
        {
            if (keep) { keep.Release(); Object.DestroyImmediate(keep); }
            keep = new RenderTexture(t.descriptor) { name = "wv_mirror" };
            keep.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;
            keep.Create();
        }
        Graphics.CopyTexture(t, keep);
    }

    /// <summary>The last mirror image, linear values (in memory).</summary>
    public Color[] Image()
    {
        var px = new Color[W * H];
        foreach (var (c, i) in Bytes().Select((c, i) => (c, i)))
            px[i] = Srgb ? new Color(Mathf.GammaToLinearSpace(c.r / 255f), Mathf.GammaToLinearSpace(c.g / 255f), Mathf.GammaToLinearSpace(c.b / 255f), c.a / 255f) : (Color)c;
        return px;
    }

    /// <summary>The last mirror image as stored.</summary>
    public Color32[] Bytes()
    {
        var req = AsyncGPUReadback.Request(keep, 0, TextureFormat.RGBA32);
        req.WaitForCompletion();
        return req.hasError ? new Color32[W * H] : req.GetData<Color32>().ToArray();
    }

    public void Dispose()
    {
        PlanarMirrors.BeforeRender = null; PlanarMirrors.AfterRender = null;
        Handle.Dispose();
        if (keep) { keep.Release(); Object.DestroyImmediate(keep); }
        Object.DestroyImmediate(disc); Object.DestroyImmediate(discMaterial); Object.DestroyImmediate(discMesh);
    }
}

public static partial class MosaicTest
{
    /// <summary>The members' footprint inside the mirror image: flat yellow, so the error shader's magenta is told apart.</summary>
    static bool[] MirrorFootprint(WetMirrorRig rig, Camera cam, int w, int h, CharaContext ctx)
    {
        if (!yellow) yellow = Flat("mosaic_test_flat_yellow", new Color32(255, 255, 0, 255));
        MarkerRender(cam, w, h, ctx, yellow);
        return rig.Image().Select(c => c.r > 0.9f && c.g > 0.9f && c.b < 0.1f).ToArray();
    }

    /// <summary>The points below the plane the hidden camera stands at: from <paramref name="c"/> by direction and distance.</summary>
    static IEnumerable<(string name, Vector3 below, float dist)> WvViews(Vector3 c, Vector3 n, float plane, float[] dists)
    {
        var h = new Vector3(n.x, 0f, n.z);
        h = h.sqrMagnitude > 1e-4f ? h.normalized : Vector3.forward;
        foreach (var (az, el) in new[] { (0f, 35f), (0f, 55f), (40f, 45f), (-40f, 65f) })
        {
            var d = Quaternion.AngleAxis(az, Vector3.up) * (Mathf.Cos(el * Mathf.Deg2Rad) * h - Mathf.Sin(el * Mathf.Deg2Rad) * Vector3.up);
            foreach (var dist in dists)
            {
                var q = c + d * dist;
                if (q.y < plane - 0.5f) yield return ($"az {az:0} down {el:0} at {dist / 10f:0.#} m", q, dist);
            }
        }
    }

    /// <summary>The member slots drawn by an unlit material with <paramref name="tex"/> at their own UVs (the mosaic left out), around <paramref name="a"/>.</summary>
    static void WithoutMosaic(CharaContext ctx, Texture tex, Action a)
    {
        var plain = Keep(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
        plain.SetTexture("_BaseMap", tex); plain.SetColor("_BaseColor", Color.white);
        var saved = new List<(Renderer r, Material[] m)>();
        foreach (var mm in MosaicSet.StateOf(ctx).Members)
        {
            saved.Add((mm.Renderer, mm.Renderer.sharedMaterials));
            mm.Renderer.sharedMaterials = Enumerable.Repeat(plain, mm.Renderer.sharedMaterials.Length).ToArray();
        }
        bool wide = MosaicWide.Enabled;
        MosaicWide.Enabled = false;
        try { a(); }
        finally { foreach (var (r, m) in saved) r.sharedMaterials = m; MosaicWide.Enabled = wide; }
    }

    static Func<int, int, Color32> Cells(int shift) => (x, y) => ((x >> shift) & 1) == 0 && ((y >> shift) & 1) == 0 ? new Color32(0, 255, 0, 255) : Skin;

    /// <summary>The wide pass in the mirror: coverage against the dilated footprint, the changed pixels by blocks of the group's grid.</summary>
    static void WideInMirror(string label, WetMirrorRig rig, Camera cam, int W, int H, bool[] fp, ref int views, ref int miss, ref int blocks, ref int mixed, ref float minB, List<string> lines)
    {
        MosaicWide.Enabled = true;
        MosaicWide.DebugView = true;
        Render(cam, W, H);
        var cov = rig.Image().Select(x => x.r > 0.5f).ToArray();
        MosaicWide.DebugView = false;
        if (rig.Groups.Count == 0) { lines.Add($"{label}: the wide pass did not run in the mirror"); views = -1000000; MosaicWide.Enabled = false; return; }
        var g = rig.Groups[0];
        float B = g.Block;
        int w = rig.W, h = rig.H, ir = Mathf.CeilToInt(1.5f * B), m = 0, fpn = 0;
        float r2 = 1.5f * B * 1.5f * B;
        var seen = (bool[])cov.Clone();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!fp[y * w + x]) continue;
                fpn++;
                for (int dy = -ir; dy <= ir; dy++)
                    for (int dx = -ir; dx <= ir; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= w || yy >= h || dx * dx + dy * dy > r2) continue;
                        if (!seen[yy * w + xx]) { m++; seen[yy * w + xx] = true; }
                    }
            }
        MosaicWide.Enabled = false;
        Render(cam, W, H);
        var off = rig.Image();
        MosaicWide.Enabled = true;
        Render(cam, W, H);
        var on = rig.Image();
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
        int mx = sets.Values.Count(s => s.Count > 1);
        views++; miss += m; blocks += sets.Count; mixed += mx; minB = Mathf.Min(minB, B);
        lines.Add($"{label}: block {B:F1} px (frame floor {rig.FramePx:F1}), footprint {fpn} px, uncovered {m} px, changed {changed} px, {sets.Count} blocks, {mx} mixed");
    }

    static void WetGroups(MakerCoreTest.Harness h)
    {
        if (Want("WV3")) Guard("WV3 a real member in the mirror", () => Wv3(h));
        if (Want("WV3m")) Guard("WV3m a real member at moving poses", () => Wv3m(h));
        if (Want("WV3a")) Guard("WV3a method (a)'s block in the mirror frame", () => Wv3a(h));
        if (Want("WV3s")) Guard("WV3 a synthetic member in the mirror", () => Wv3Synthetic());
        if (Want("WV3c")) Guard("WV3 clothed views in the mirror", () => Wv3Clothed(h));
        if (Want("WV1")) Guard("WV1 no map, override or film on a member", () => Wv1(h));
        if (Want("WV2")) Guard("WV2 a stain and a film around the members", () => Wv2(h));
        if (Want("WV4")) Guard("WV4 the option off in the mirror", () => Wv4(h));
        if (Want("WV5")) Guard("WV5 the block on the final output", () => Wv5(h));
        if (Want("WV6")) Guard("WV6 every path that renders the view", () => Wv6(h));
    }

    static void Wv3(MakerCoreTest.Harness h)
    {
        const int W = 1280, H = 720;
        using (var f = new Disposer(h.Build(FemaleCard)))
        {
            if (!Check("WV3 BR-Chan builds", f.B.Ok, f.B.Error ?? "")) return;
            var ctx = f.B.Context;
            HideClothes(ctx);
            NewLight();
            var (c, n) = PatchPose(ctx);
            using (var rig = new WetMirrorRig(ctx.Root.transform.position.y - 0.1f, 0.5f))
            {
                foreach (var (label, shift, dists) in new[] { ("near", 3, new[] { 13f, 16f, 22f, 30f }), ("far", 5, new[] { 60f, 120f, 300f }) })
                {
                    var comp = Composite(ctx, 2048, 6, Cells(shift), Skin);
                    UseComposite(ctx, comp);
                    var cam = NewCam(false, AntialiasingMode.None);
                    MosaicWide.Enabled = false;
                    int worst = 0, fpTotal = 0, ctrlTotal = 0, ctrlViews = 0, views = 0, lumN = 0;
                    double lum = 0;
                    var lines = new List<string>();
                    foreach (var (name, below, dist) in WvViews(c, n, rig.Plane, dists))
                    {
                        rig.Place(cam, c, below);
                        var fp = Erode(MirrorFootprint(rig, cam, W, H, ctx), rig.W, rig.H, 1);
                        Render(cam, W, H);
                        var px = rig.Image();
                        int fpn = fp.Count(x => x), mk = Markers(px, fp), ctrl = 0;
                        WithoutMosaic(ctx, comp, () => { Render(cam, W, H); ctrl = Markers(rig.Image(), fp); });
                        for (int i = 0; i < px.Length; i++) if (fp[i]) { lum += px[i].r + px[i].g + px[i].b; lumN++; }
                        worst = Math.Max(worst, mk); fpTotal += fpn; views++;
                        if (fpn > 0) { ctrlTotal += ctrl; ctrlViews += ctrl > 0 ? 1 : 0; }
                        lines.Add($"{name}: footprint {fpn} px, markers {mk}, left out {ctrl}, block {Member(ctx, "o_mnpb")?.BlockPx:F1} px (main view's frame)");
                    }
                    double level = lumN > 0 ? lum / (3 * lumN) : 0;
                    Check($"WV3 (a) {label}: 0 marker pixels in the footprint inside the mirror image ({views} views)", worst == 0 && fpTotal > 0 && level > 0.02, $"worst {worst}, footprint total {fpTotal} px, mean level {level:F3}");
                    if (label == "near") Check("WV3 (a) control: with the mosaic left out the marker shows in the same footprint", ctrlTotal > 50 && ctrlViews > 0, $"{ctrlTotal} marker pixels in {ctrlViews} views");
                    foreach (var l in lines) Info("WV3 " + l);
                }
                // method (b)
                UseComposite(ctx, Composite(ctx, 2048, 0, Gradient(2048), Skin));
                var camB = NewCam(true, AntialiasingMode.None);
                int vw = 0, miss = 0, blocks = 0, mixed = 0, fpSum = 0;
                float minB = float.MaxValue;
                var wl = new List<string>();
                foreach (var (name, below, dist) in WvViews(c, n, rig.Plane, new[] { 16f, 30f, 60f }))
                {
                    rig.Place(camB, c, below);
                    MosaicWide.Enabled = false;
                    var fm = MirrorFootprint(rig, camB, W, H, ctx);
                    int fpn = fm.Count(x => x);
                    fpSum += fpn;
                    if (fpn == 0) { wl.Add($"{name}: no footprint"); continue; }
                    WideInMirror(name, rig, camB, W, H, fm, ref vw, ref miss, ref blocks, ref mixed, ref minB, wl);
                }
                Check($"WV3 (b): the coverage holds the footprint dilated by 1.5 blocks, the changed pixels form flat blocks ({vw} views)", vw > 0 && miss == 0 && blocks > 0 && mixed == 0, $"uncovered {miss} px, {blocks} blocks, {mixed} mixed, least block {minB:F1} px, footprint total {fpSum} px");
                foreach (var l in wl) Info("WV3 (b) " + l);
                // no skin from under the patches: the composite flat green inside their UV area, the members flat
                UseComposite(ctx, Composite(ctx, 2048, 1, (x, y) => new Color32(0, 255, 0, 255), Skin));
                var camU = NewCam(false, AntialiasingMode.None);
                int leak = 0, leakPlain = 0, member = 0, vu = 0;
                foreach (var (name, below, dist) in WvViews(c, n, rig.Plane, new[] { 13f, 16f, 22f, 30f, 60f }))
                {
                    rig.Place(camU, c, below);
                    int mp = MirrorFootprint(rig, camU, W, H, ctx).Count(x => x);
                    member += mp;
                    int lk = Markers(rig.Image(), null);
                    leak += lk;
                    vu++;
                    // the same view from a plain camera at the reflected pose, the puddle out of its way
                    var pl = NewCam(false, AntialiasingMode.None);
                    pl.transform.SetPositionAndRotation(rig.CameraPosition, rig.CameraRotation);
                    rig.Puddle.enabled = false;
                    int pk = Markers(MarkerRender(pl, rig.W, rig.H, ctx, yellow), null);
                    rig.Puddle.enabled = true;
                    leakPlain += pk;
                    Info($"WV3 under {name}: member {mp} px, skin {lk} px; a plain camera at the reflected pose {pk} px");
                }
                Check($"WV3 (a): the skin under the patches shows no more in the mirror than to a plain camera at the reflected pose ({vu} views)", leak <= leakPlain && member > 0, $"{leak} px, plain {leakPlain} px, {member} member px");
            }
        }
    }

    static void Wv3a(MakerCoreTest.Harness h)
    {
        const int W = 1280, H = 720;
        using (var f = new Disposer(h.Build(FemaleCard)))
        {
            if (!Check("WV3a BR-Chan builds", f.B.Ok, f.B.Error ?? "")) return;
            var ctx = f.B.Context;
            HideClothes(ctx);
            var (c, n) = PatchPose(ctx);
            var cam = NewCam(false, AntialiasingMode.None);
            var plain = NewCam(false, AntialiasingMode.None);
            using (var rig = new WetMirrorRig(ctx.Root.transform.position.y - 0.1f, 0.5f))
            {
                int views = 0, members = 0, under = 0, off = 0;
                float worst = 1f, leastRatio = float.MaxValue;
                var lines = new List<string>();
                foreach (var (name, below, dist) in WvViews(c, n, rig.Plane, new[] { 13f, 16f, 22f, 30f, 60f, 120f, 300f }))
                {
                    rig.Place(cam, c, below);
                    Render(cam, W, H);
                    if (rig.Blocks.Count == 0) { lines.Add($"{name}: no member in the mirror frame"); continue; }
                    views++;
                    var mirrored = rig.Blocks.ToList();
                    float floor = rig.FramePx;
                    plain.fieldOfView = cam.fieldOfView; plain.nearClipPlane = cam.nearClipPlane; plain.farClipPlane = cam.farClipPlane;
                    plain.transform.SetPositionAndRotation(rig.CameraPosition, rig.CameraRotation);
                    var rt = new RenderTexture(rig.TargetW, rig.TargetH, 24);
                    plain.targetTexture = rt;
                    plain.aspect = rig.TargetW / (float)rig.TargetH;
                    MosaicCameraFrame fr;
                    using (MosaicCamera.OutputScope(plain, rig.OutputScale)) fr = MosaicCamera.Apply(plain, false);
                    var sb = new List<string>();
                    foreach (var (m, px, uv) in mirrored)
                    {
                        members++;
                        if (px < floor) under++;
                        float ratio = Mathf.Max(px, m.BlockPx) / Mathf.Max(1e-6f, Mathf.Min(px, m.BlockPx));
                        if (ratio > 2.001f) off++;
                        worst = Mathf.Max(worst, ratio); leastRatio = Mathf.Min(leastRatio, px / Mathf.Max(floor, 1e-6f));
                        sb.Add($"{m.Renderer.name} {px:F1} px / {uv:0.######} UV, plain {m.BlockPx:F1} px");
                    }
                    lines.Add($"{name}: F {floor:F1} (plain {fr.FloorPx:F1}) {string.Join("; ", sb)}");
                    plain.targetTexture = null;
                    rt.Release(); Object.DestroyImmediate(rt);
                }
                Check($"WV3a every drawn member's block >= the mirror frame's F ({views} views, {members} members)", views > 0 && under == 0, $"{under} under, least block / F {leastRatio:F2}");
                Check("WV3a the block equals a plain camera's at the reflected pose within one power-of-two step", views > 0 && off == 0, $"{off} off, worst ratio {worst:F3}");
                foreach (var l in lines) Info("WV3a " + l);
            }
        }
    }

    static void Wv3Synthetic()
    {
        const int W = 1280, H = 720;
        var tex = Keep(new Texture2D(64, 64, TextureFormat.RGBA32, true, false) { name = "wv_marker", filterMode = FilterMode.Trilinear });
        var px32 = new Color32[64 * 64];
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) px32[y * 64 + x] = ((x >> 1) & 1) == 0 && ((y >> 1) & 1) == 0 ? new Color32(0, 255, 0, 255) : Skin;
        tex.SetPixels32(px32); tex.Apply(true);
        var mat = Keep(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
        mat.SetTexture("_MainTex", tex); mat.SetTexture("_BaseMap", tex);
        var sp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(sp.GetComponent<Collider>());
        sp.transform.position = new Vector3(0f, 2.6f, -30f); sp.transform.localScale = Vector3.one * 3f;
        sp.layer = 10;
        var mr = sp.GetComponent<Renderer>();
        mr.sharedMaterial = mat;
        var cam = NewCam(true, AntialiasingMode.None);
        NewLight();
        try
        {
            using (var rig = new WetMirrorRig(0f, 0.5f))
            {
                // the marker drawn plainly: before the renderer becomes a member
                var c = sp.transform.position;
                var below = c + new Vector3(0f, -0.6f, 1f).normalized * 16f;
                rig.Place(cam, c, below);
                MosaicWide.Enabled = false;
                Render(cam, W, H);
                int plain = Markers(rig.Image(), null);
                Check("WV3 synthetic: the marker shows in the mirror image before the sphere is a member", plain > 50, $"{plain} marker pixels");
                Check("WV3 synthetic: MosaicSet.Add", MosaicSet.Add(mr), "");
                Render(cam, W, H);
                int with = Markers(rig.Image(), null);
                Check("WV3 synthetic (a): 0 marker pixels once it is a member", with == 0, $"{with}, blocks {string.Join(", ", rig.Blocks.Select(b => b.px.ToString("F1")))}, F {rig.FramePx:F1}");
                Check("WV3 synthetic (a): the member's block >= the frame's F", rig.Blocks.Count == 1 && rig.Blocks[0].px >= rig.FramePx, $"{rig.Blocks.Count} members");
                int vw = 0, miss = 0, blocks = 0, mixed = 0;
                float minB = float.MaxValue;
                var wl = new List<string>();
                var fp = new bool[rig.W * rig.H];
                // the footprint: the member against the same view without it
                var withPx = rig.Image();
                mr.enabled = false;
                Render(cam, W, H);
                var noPx = rig.Image();
                mr.enabled = true;
                for (int i = 0; i < fp.Length; i++) fp[i] = withPx[i] != noPx[i];
                WideInMirror("sphere", rig, cam, W, H, fp, ref vw, ref miss, ref blocks, ref mixed, ref minB, wl);
                Check("WV3 synthetic (b): the coverage holds the footprint dilated by 1.5 blocks, flat blocks", vw == 1 && miss == 0 && blocks > 0 && mixed == 0, wl.FirstOrDefault() ?? "");
            }
        }
        finally { mr.enabled = false; Object.DestroyImmediate(sp); MosaicSet.Standalone(); MosaicWide.Enabled = false; }
    }

    static void Wv3Clothed(MakerCoreTest.Harness h)
    {
        const int W = 900, H = 900;
        foreach (var (label, file, male) in new[] { ("BR-Chan", FemaleCard, false), ("HERO107", MaleCard, true) })
            using (var f = new Disposer(h.Build(file)))
            {
                if (!Check($"WV3 {label} builds (clothed)", f.B.Ok, f.B.Error ?? "")) continue;
                var ctx = f.B.Context;
                var nDan = HS2Next.Chara.Edit.EditContext.Find(ctx.Body.transform, "N_dan");
                if (male && nDan) nDan.gameObject.SetActive(true);
                var focus = Member(ctx, male ? "cm_o_dan00" : "o_mnpb");
                if (!Check($"WV3 {label}: the focus member exists", focus != null, "")) continue;
                var c = MosaicCamera.RegionMatrix(focus).MultiplyPoint3x4(focus.Region.Centroid);
                var cam = NewCam(true, AntialiasingMode.None);
                using (var rig = new WetMirrorRig(ctx.Root.transform.position.y - 0.1f, 0.5f))
                {
                    int hidden = 0, bad = 0, views = 0;
                    foreach (var (name, below, dist) in WvViews(c, ctx.Root.transform.forward, rig.Plane, new[] { 16f, 30f }))
                    {
                        rig.Place(cam, c, below);
                        MosaicWide.Enabled = false;
                        int a = MirrorFootprint(rig, cam, W, H, ctx).Count(x => x);
                        Render(cam, W, H);
                        var off = rig.Image();
                        MosaicWide.Enabled = true;
                        Render(cam, W, H);
                        var on = rig.Image();
                        bool ran = rig.Groups.Count > 0;
                        MosaicWide.DebugView = true;
                        Render(cam, W, H);
                        int cov = rig.Image().Count(x => x.r > 0.5f);
                        MosaicWide.DebugView = false;
                        MosaicWide.Enabled = false;
                        int changed = 0;
                        for (int i = 0; i < on.Length; i++) if (on[i] != off[i]) changed++;
                        views++;
                        Info($"WV3 {label} clothed {name}: (a) visible member px {a}; (b) ran {ran}, coverage {cov} px, changed {changed} px");
                        if (a > 0 && cov == 0)
                        {
                            var pl = NewCam(true, AntialiasingMode.None);
                            pl.transform.SetPositionAndRotation(rig.CameraPosition, rig.CameraRotation);
                            rig.Puddle.enabled = false;
                            int pa = Footprint(pl, rig.W, rig.H, ctx).Count(x => x);
                            MosaicWide.Enabled = true; MosaicWide.DebugView = true;
                            int pc = Render(pl, rig.W, rig.H).Count(x => x.r > 0.5f);
                            MosaicWide.DebugView = false; MosaicWide.Enabled = false;
                            rig.Puddle.enabled = true;
                            Info($"WV3 {label} clothed {name}: a plain camera at the reflected pose: (a) {pa} px, coverage {pc} px");
                        }
                        if (a == 0) { hidden++; if (cov > 0 || changed > 0) bad++; }
                        else if (cov == 0) bad++;
                    }
                    Check($"WV3 {label} clothed: a member hidden by clothes gives no (a) pixel and no (b) coverage in the mirror, a visible one is covered", bad == 0 && views > 0, $"{hidden} of {views} views with the members hidden, {bad} views wrong");
                }
            }
    }

    // WV3m: the player's WV6M geometry: 960 x 540 at mirror scale 0.5, the view 16 from the member, a puddle of 200 at the view's axis, 36 moments an orbit, 12 idle frames between moments.
    const int Wv3mMoments = 36, Wv3mFrames = 12, Wv3mLeastJudged = 30;
    const float Wv3mDt = 0.03333f, Wv3mLeastTravelPx = 50f;

    static Texture2D Wv3mChecker(int size, int shift)
    {
        var cells = Cells(shift);
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) px[y * size + x] = cells(x, y);
        var t = Keep(new Texture2D(size, size, TextureFormat.RGBA32, true, false) { name = "wv3m_marker", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear });
        t.SetPixels32(px);
        t.Apply(true);
        return t;
    }

    /// <summary>Marker pixels in <paramref name="fp"/> with the members drawing flat green through their own lit texture mode: what the reading sees at this light.</summary>
    static int Wv3mLit(WetMirrorRig rig, Camera cam, int w, int h, CharaContext ctx, Texture2D green, bool[] fp)
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
        try { Render(cam, w, h); return Markers(rig.Image(), fp); }
        finally
        {
            foreach (var x in saved)
            {
                x.m.SetTexture("_MainTex", x.t); x.m.SetTextureScale("_MainTex", x.sc); x.m.SetTextureOffset("_MainTex", x.of);
                x.m.SetFloat("_MosaicMode", x.mode); x.m.SetColor("_Color", x.c);
            }
        }
    }

    static void Wv3m(MakerCoreTest.Harness h)
    {
        const int W = 960, H = 540;
        float radius = 1.5f * Mathf.Sqrt(200f / Mathf.PI);
        var idle = CharaAnimation.Resolve(h.Catalog.LoadStudioLists(), ContentIds.Vanilla, 19, 0, 0);
        if (!Check("WV3m the idle row resolves", idle != null && !idle.IsH, "")) return;
        foreach (var (label, file, male, orbits) in new[]
        {
            ("BR-Chan", FemaleCard, false, new[] { (r: 0.6f, phase: 0f), (r: 1.0f, phase: 2.0f) }),
            ("HERO107", MaleCard, true, new[] { (r: 0.6f, phase: 0f), (r: 0.8f, phase: 0.7f) }),
        })
            using (var f = new Disposer(h.Build(file)))
            {
                if (!Check($"WV3m {label} builds", f.B.Ok, f.B.Error ?? "")) continue;
                var ctx = f.B.Context;
                HideClothes(ctx);
                var light = NewLight();
                var nDan = HS2Next.Chara.Edit.EditContext.Find(ctx.Body.transform, "N_dan");
                if (male && nDan) nDan.gameObject.SetActive(true);
                var focus = Member(ctx, male ? "cm_o_dan00" : "o_mnpb");
                var anim = CharaAnimation.Of(ctx.Root);
                if (!Check($"WV3m {label}: the focus member and the animation exist", focus != null && anim != null && ctx.Motion != null, "")) continue;
                ctx.Motion.AutoStep = false;
                if (!Check($"WV3m {label}: the idle loads", anim.Load(idle), "")) continue;
                anim.Speed = 1f;
                ctx.Motion.Step(0f);
                Vector3 Centre() => MosaicCamera.RegionMatrix(focus).MultiplyPoint3x4(focus.Region.Centroid);
                var home = ctx.Root.transform.position;
                var c0 = Centre();
                var n = (male ? new Vector3(0f, -0.8f, 0.6f) : PatchPose(ctx).n).normalized;
                var below = c0 + n * 16f;
                light.transform.rotation = Quaternion.LookRotation(-n, Vector3.forward);   // on the side the hidden camera sees, so a green block reads as a marker

                void Walk((float r, float phase) o, Action<int> at)
                {
                    anim.Restart();
                    ctx.Root.transform.position = home;
                    for (int i = 0; i < Wv3mMoments; i++)
                    {
                        if (i > 0) for (int k = 0; k < Wv3mFrames; k++) ctx.Motion.Step(Wv3mDt);
                        float an = o.phase + 2f * Mathf.PI * i / Wv3mMoments;
                        ctx.Root.transform.position = home + new Vector3(o.r * Mathf.Cos(an), 0f, o.r * Mathf.Sin(an));
                        at(i);
                    }
                    ctx.Root.transform.position = home;
                }

                using (var rig = new WetMirrorRig(home.y, 0.5f))
                {
                    int judged = 0, notJudged = 0, badA = 0, marks = 0, noCtrl = 0, dimLit = 0, under = 0, blockMoments = 0, noBlock = 0, lumN = 0, leastCtrl = int.MaxValue, leastFp = int.MaxValue;
                    float leastLit = 1f;
                    float leastBlock = float.MaxValue, leastRatio = float.MaxValue, travel = float.MaxValue, idleMoved = 0f;
                    double lum = 0;
                    var lines = new List<string>();

                    // method (a)
                    Texture marker;
                    var green = Flat("wv3m_green", new Color32(0, 255, 0, 255));
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
                    var cam = NewCam(false, AntialiasingMode.None);
                    MosaicWide.Enabled = false;
                    rig.Place(cam, c0, below);
                    var centre = rig.Puddle.transform.position;
                    rig.Lay(centre, radius);
                    var view = cam.transform.position;
                    foreach (var o in orbits)
                    {
                        var first = Vector2.zero;
                        float far = 0f, path = 0f;
                        var last = Vector2.zero;
                        var rootless0 = Vector3.zero;
                        Walk(o, i =>
                        {
                            var fp = Erode(MirrorFootprint(rig, cam, W, H, ctx), rig.W, rig.H, 1);
                            Render(cam, W, H);
                            var px = rig.Image();
                            float F = rig.FramePx;
                            var drawn = rig.Blocks.ToList();
                            int fpn = fp.Count(x => x), mk = Markers(px, fp), ctrl = 0;
                            WithoutMosaic(ctx, marker, () => { Render(cam, W, H); ctrl = Markers(rig.Image(), fp); });
                            int lit = Wv3mLit(rig, cam, W, H, ctx, green, fp);
                            if (fpn == 0) notJudged++;
                            else
                            {
                                judged++;
                                marks += mk;
                                if (ctrl == 0) noCtrl++;
                                if (lit * 2 < fpn) dimLit++;
                                leastLit = Mathf.Min(leastLit, lit / (float)fpn);
                                if (mk > 0 || ctrl == 0) badA++;
                                leastCtrl = Math.Min(leastCtrl, ctrl); leastFp = Math.Min(leastFp, fpn);
                                for (int k = 0; k < px.Length; k++) if (fp[k]) { lum += px[k].r + px[k].g + px[k].b; lumN++; }
                                if (drawn.Count == 0) noBlock++;
                            }
                            if (drawn.Count > 0) blockMoments++;
                            float least = float.MaxValue;
                            foreach (var (m, b, uv) in drawn)
                            {
                                if (b < F) under++;
                                least = Mathf.Min(least, b);
                                leastRatio = Mathf.Min(leastRatio, b / Mathf.Max(F, 1e-6f));
                            }
                            if (drawn.Count > 0) leastBlock = Mathf.Min(leastBlock, least);
                            // where the member's reflection stands: in the output, and where the view's ray meets the floor
                            var world = Centre();
                            var mirrored = rig.Reflect(world);
                            var vp = cam.WorldToViewportPoint(mirrored);
                            var at = new Vector2(vp.x * W, vp.y * H);
                            float t = (view.y - rig.Floor) / Mathf.Max(view.y - mirrored.y, 1e-3f);
                            var hit = Vector3.Lerp(view, mirrored, t);
                            float dx = hit.x - centre.x, dz = hit.z - centre.z, d = Mathf.Sqrt(dx * dx + dz * dz);
                            var rootless = world - ctx.Root.transform.position;
                            if (i == 0) { first = at; last = at; rootless0 = rootless; }
                            far = Mathf.Max(far, (at - first).magnitude);
                            path += (at - last).magnitude;
                            last = at;
                            idleMoved = Mathf.Max(idleMoved, (rootless - rootless0).magnitude);
                            lines.Add($"(a) r {o.r:0.0} phase {o.phase:0.0} m{i:00}: centre ({at.x:F1}, {at.y:F1}), off the puddle's centre {d:F3} ({dx:F3}, {dz:F3}), from its edge {radius - d:F3}; footprint {fpn} px, markers {mk}, left out {ctrl}, flat green read {lit}; F {F:F1}, least block {(drawn.Count > 0 ? least : 0f):F1}, members {drawn.Count}");
                        });
                        lines.Add($"(a) r {o.r:0.0} phase {o.phase:0.0}: travel up to {far:F1} px from the first moment, path {path:F1} px (output {W} x {H})");
                        travel = Mathf.Min(travel, far);
                    }
                    foreach (var (m, t) in saved) m.SetTexture("_MainTex", t);
                    double level = lumN > 0 ? lum / (3 * lumN) : 0;
                    Check($"WV3m {label} (a): 0 marker pixels in each moment's footprint, the marker shows with the mosaic left out in that footprint ({judged} moments judged, {notJudged} not judged)",
                        judged >= Wv3mLeastJudged && badA == 0 && level > 0.1, $"{marks} marker pixels, {noCtrl} moments with no marker left out, least left out {leastCtrl}, least footprint {leastFp} px, mean level {level:F3}");
                    Check($"WV3m {label} (a) control: flat green through the members' own lit texture mode reads as a marker on at least half of each moment's footprint", judged > 0 && dimLit == 0, $"{dimLit} moments under half, least share {leastLit:F3}");
                    Check($"WV3m {label} WV3a: every drawn member's block >= the frame's F at each moment ({blockMoments} moments)",
                        blockMoments >= Wv3mLeastJudged && under == 0 && noBlock == 0, $"{under} under, {noBlock} judged moments with no member in the frame, least block {leastBlock:F1} px, least block / F {leastRatio:F2}");
                    Check($"WV3m {label}: the member travelled at least {Wv3mLeastTravelPx:F0} px on each orbit and the idle stepped the pose", travel >= Wv3mLeastTravelPx && idleMoved > 1e-4f, $"least travel {travel:F1} px, the idle moved the member {idleMoved:F3} units against the root");

                    // method (b)
                    if (!male) UseComposite(ctx, Composite(ctx, 2048, 0, Gradient(2048), Skin));
                    var camB = NewCam(true, AntialiasingMode.None);
                    rig.Place(camB, c0, below);
                    rig.Lay(centre, radius);
                    int vw = 0, miss = 0, blocks = 0, mixed = 0, emptyB = 0, underB = 0, groupsMax = 0;
                    float minB = float.MaxValue;
                    var wl = new List<string>();
                    foreach (var o in orbits)
                        Walk(o, i =>
                        {
                            MosaicWide.Enabled = false;
                            var fm = MirrorFootprint(rig, camB, W, H, ctx);
                            string name = $"r {o.r:0.0} phase {o.phase:0.0} m{i:00}";
                            if (!fm.Any(x => x)) { emptyB++; wl.Add($"{name}: no footprint"); return; }
                            WideInMirror(name, rig, camB, W, H, fm, ref vw, ref miss, ref blocks, ref mixed, ref minB, wl);
                            groupsMax = Math.Max(groupsMax, rig.Groups.Count);
                            if (rig.Groups.Count == 0 || rig.Groups[0].Block < rig.FramePx) underB++;
                        });
                    MosaicWide.Enabled = false;
                    Check($"WV3m {label} (b): the coverage holds each moment's footprint dilated by 1.5 blocks, the changed pixels form flat blocks ({vw} moments judged, {emptyB} not judged)",
                        vw >= Wv3mLeastJudged && miss == 0 && blocks > 0 && mixed == 0 && underB == 0, $"uncovered {miss} px, {blocks} blocks, {mixed} mixed, {underB} under F, least block {minB:F1} px, most groups {groupsMax}");
                    Info($"WV3m {label}: floor {rig.Floor:F3}, member at {c0.x:F3}, {c0.y:F3}, {c0.z:F3}, view {view.x:F3}, {view.y:F3}, {view.z:F3}, puddle at {centre.x:F3}, {centre.z:F3} radius {radius:F3}, mirror target {rig.W} x {rig.H}");
                    foreach (var l in lines) Info($"WV3m {label} {l}");
                    foreach (var l in wl) Info($"WV3m {label} (b) {l}");
                }
            }
    }

    /// <summary>A map of a target: distance 0 everywhere, or from a point; a stain in code, no rig.</summary>
    static RenderTexture WvMap(Renderer r, int slot, Vector3? origin, float reach)
    {
        var smr = r as SkinnedMeshRenderer;
        var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            smr.BakeMesh(baked);
            var toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            var v = baked.vertices;
            var d = new float[v.Length];
            if (origin is Vector3 o) for (int i = 0; i < v.Length; i++) d[i] = (toWorld.MultiplyPoint3x4(v[i]) - o).magnitude;
            var t = WetCompose.BaseOf(r.sharedMaterials[slot]);
            var size = WetBake.MapSize(t ? t.width : 512, t ? t.height : 512);
            return WetBake.Bake(baked.uv, baked.GetTriangles(Math.Min(slot, baked.subMeshCount - 1)), d, size.x, size.y, reach);
        }
        finally { Object.DestroyImmediate(baked); }
    }

    static WetLayer WvLayer(Texture map, float reach) => new WetLayer
    {
        Map = map, Radius = reach, Feather = 0.195f, Reach = reach, Saturate = 1f, Amount = 0.85f, Tint = Color.white, Stain = new Color(0.42f, 0.03f, 0.04f), Skin = 1f,
    };

    static void Wv1(MakerCoreTest.Harness h)
    {
        foreach (var (label, file, male) in new[] { ("BR-Chan", FemaleCard, false), ("HERO107", MaleCard, true) })
        {
            var node = NodeOf(h, file);
            try
            {
                var real = node.Real;
                var ctx = real.Context;
                var nDan = HS2Next.Chara.Edit.EditContext.Find(ctx.Body.transform, "N_dan");
                if (nDan) nDan.gameObject.SetActive(true);
                var members = MosaicSet.StateOf(ctx).Members.Select(m => m.Renderer).Where(r => r).ToList();
                var targets = WetTargets.Collect(real);
                int onMember = targets.Count(t => members.Contains(t.Renderer) || MosaicSet.IsMember(t.Renderer) || MosaicSet.IsMosaicMaterial(t.Material));
                Check($"WV1 {label}: no target is a member's renderer or material", members.Count > 0 && targets.Count > 0 && onMember == 0, $"{members.Count} members, {targets.Count} targets, {onMember} on a member");
                var maps = new List<RenderTexture>(); var comps = new List<WetCompose>(); var films = new List<WetFilm>();
                var root = new GameObject("wv_wet").transform;
                try
                {
                    foreach (var t in targets)
                    {
                        if (t.State != WetTargetStates.Ok || !t.Shown || !(t.Renderer is SkinnedMeshRenderer)) continue;
                        var map = WvMap(t.Renderer, t.Slot, null, 3f);
                        maps.Add(map);
                        var c = new WetCompose(t.Renderer, t.Slot, t.CompositeCap, t.Kind == WetKinds.Clothes);
                        c.Compose(new[] { WvLayer(map, 3f) });
                        comps.Add(c);
                        var f = t.Kind == WetKinds.Clothes ? WetFilm.Create(root, c) : null;
                        if (f != null) films.Add(f);
                    }
                    int overrides = 0;
                    var block = new MaterialPropertyBlock();
                    foreach (var r in members)
                        for (int i = 0; i < r.sharedMaterials.Length; i++)
                        {
                            r.GetPropertyBlock(block, i);
                            if (!block.isEmpty && block.HasTexture("_MainTex")) overrides++;
                        }
                    int filmOnMember = films.Count(f => members.Any(m => m is SkinnedMeshRenderer s && s.sharedMesh == f.Renderer.sharedMesh));
                    Check($"WV1 {label}: with stains and films on every target no member has an override or a film", comps.Count > 0 && overrides == 0 && filmOnMember == 0, $"{comps.Count} stained targets, {films.Count} films, {overrides} overrides on members, {filmOnMember} films");
                }
                finally
                {
                    foreach (var f in films) f.Dispose();
                    foreach (var c in comps) c.Dispose();
                    foreach (var m in maps) WetBake.Free(m);
                    Object.DestroyImmediate(root.gameObject);
                }
                if (!male)
                {
                    // a renderer of a part that becomes a member stops being a target
                    var one = targets.FirstOrDefault(t => t.Kind == WetKinds.Clothes)?.Renderer;
                    if (Check("WV1 a clothes renderer exists", one != null, ""))
                    {
                        MosaicSet.Add(one);
                        RunStage(ctx);
                        var again = WetTargets.Collect(real);
                        Check("WV1 a synthetic member through MosaicSet.Add is no target", again.All(t => t.Renderer != one), $"{again.Count} targets");
                    }
                }
            }
            finally { node.Dispose(); }
        }
    }

    static void Wv2(MakerCoreTest.Harness h)
    {
        const int W = 1280, H = 720;
        var node = NodeOf(h, FemaleCard);
        var root = new GameObject("wv_wet").transform;
        var maps = new List<RenderTexture>(); var comps = new List<WetCompose>(); var films = new List<WetFilm>();
        try
        {
            var real = node.Real;
            var ctx = real.Context;
            UseComposite(ctx, Composite(ctx, 2048, 6, MarkerTexel, Skin));
            NewLight();
            var (c, n) = PatchPose(ctx);
            // clothes on: a stain and a film on every garment, the skin stained around the patches
            foreach (var t in WetTargets.Collect(real))
            {
                if (t.State != WetTargetStates.Ok || !t.Shown || !(t.Renderer is SkinnedMeshRenderer)) continue;
                if (t.Kind != WetKinds.Clothes && t.Kind != WetKinds.Skin) continue;
                var map = WvMap(t.Renderer, t.Slot, c, 12f);
                maps.Add(map);
                var wc = new WetCompose(t.Renderer, t.Slot, t.CompositeCap, t.Kind == WetKinds.Clothes);
                wc.Compose(new[] { WvLayer(map, 12f) });
                comps.Add(wc);
                var f = t.Kind == WetKinds.Clothes ? WetFilm.Create(root, wc) : null;
                if (f != null) films.Add(f);
            }
            Check("WV2 the stain lies on the skin and on the clothes", comps.Count > 1, $"{comps.Count} targets, {films.Count} films");
            var right = Vector3.Cross(Vector3.up, n).normalized;
            var dirs = new[] { ("front", n), ("30 deg up", Quaternion.AngleAxis(-30f, right) * n), ("45 deg side", Quaternion.AngleAxis(45f, Vector3.up) * n) };
            MosaicWide.Enabled = false;
            int Sweep(string label, out int fpTotal, out int anywhere)
            {
                var cam = NewCam(true, AntialiasingMode.SubpixelMorphologicalAntiAliasing);
                int worst = 0; fpTotal = 0; anywhere = 0;
                foreach (var (dn, dir) in dirs)
                    foreach (var d in new[] { 3f, 10f, 30f, 100f })
                    {
                        Aim(cam, c, dir, d);
                        var fp = Erode(Footprint(cam, W, H, ctx), W, H, 4);
                        var px = Render(cam, W, H);
                        int fpn = fp.Count(x => x), mk = Markers(px, fp);
                        worst = Math.Max(worst, mk); fpTotal += fpn; anywhere += Markers(px, null);
                    }
                return worst;
            }
            int clothed = Sweep("clothed", out int fpC, out int anyC);
            Check("WV2 clothes on: 0 marker pixels in the footprint and in the whole image", clothed == 0 && anyC == 0, $"worst {clothed}, footprint total {fpC} px, whole image {anyC} px");
            // clothes off: the stain on the skin around the patches and over them
            foreach (var f in films) f.Suspend(true);
            HideClothes(ctx);
            int bare = Sweep("bare", out int fpB, out _);
            Check("WV2 clothes off: the skin stained around the patches, 0 marker pixels in the footprint", bare == 0 && fpB > 0, $"worst {bare}, footprint total {fpB} px");
        }
        finally
        {
            foreach (var f in films) f.Dispose();
            foreach (var c in comps) c.Dispose();
            foreach (var m in maps) WetBake.Free(m);
            Object.DestroyImmediate(root.gameObject);
            MosaicWide.Enabled = false;
            node.Dispose();
        }
    }

    static void Wv4(MakerCoreTest.Harness h)
    {
        const int W = 1280, H = 720;
        var wf = WideFeature();
        using (var f = new Disposer(h.Build(FemaleCard)))
        {
            if (!Check("WV4 BR-Chan builds", f.B.Ok, f.B.Error ?? "")) return;
            var ctx = f.B.Context;
            HideClothes(ctx);
            NewLight();
            var (c, n) = PatchPose(ctx);
            using (var rig = new WetMirrorRig(ctx.Root.transform.position.y - 0.1f, 0.5f))
            {
                var cam = NewCam(true, AntialiasingMode.None);
                int differ = 0, views = 0;
                foreach (var (name, below, dist) in WvViews(c, n, rig.Plane, new[] { 16f, 30f }))
                {
                    rig.Place(cam, c, below);
                    MosaicWide.Enabled = false;
                    Render(cam, W, H);
                    var off = rig.Bytes();
                    bool was = wf && wf.isActive;
                    if (wf) wf.SetActive(false);
                    try { MosaicWide.Enabled = true; Render(cam, W, H); }
                    finally { if (wf) wf.SetActive(was); MosaicWide.Enabled = false; }
                    var none = rig.Bytes();
                    for (int i = 0; i < off.Length; i++) if (off[i].r != none[i].r || off[i].g != none[i].g || off[i].b != none[i].b) differ++;
                    views++;
                }
                Check($"WV4 the option off renders the mirror image byte-identical to the renderer without the feature ({views} views)", views > 0 && differ == 0, $"{differ} px differ");
            }
        }
    }

    /// <summary>
    /// The block on the final output. A member's block in the mirror frame, by the numbers, on real members; on a large frontal member the blocks as they show in the puddle, with
    /// MR2's rule for flat runs. A block reaches the output through the puddle's filter: runs inside its band of 1 / (scale k) pixels between two blocks are left out.
    /// </summary>
    static void Wv5(MakerCoreTest.Harness h)
    {
        const int W = 960, H = 540;
        float floor = Mathf.Max(4f, Mathf.Ceil(Mathf.Max(W, H) / 100f));
        using (var f = new Disposer(h.Build(FemaleCard)))
        {
            if (!Check("WV5 BR-Chan builds", f.B.Ok, f.B.Error ?? "")) return;
            var ctx = f.B.Context;
            HideClothes(ctx);
            NewLight();
            var (c, n) = PatchPose(ctx);
            UseComposite(ctx, Composite(ctx, 2048, 0, Gradient(2048), Skin));
            foreach (var scale in new[] { 0.25f, 0.5f, 1f })
                using (var rig = new WetMirrorRig(ctx.Root.transform.position.y - 0.1f, scale))
                    foreach (var k in new[] { 1, 2, 4 })
                    {
                        var cam = NewCam(false, AntialiasingMode.None);
                        float least = float.MaxValue; int views = 0;
                        foreach (var (name, below, dist) in WvViews(c, n, rig.Plane, new[] { 13f, 16f, 22f, 30f, 60f }))
                        {
                            rig.Place(cam, c, below);
                            MosaicWide.Enabled = false;
                            using (MosaicCamera.OutputScope(cam, k)) Render(cam, W * k, H * k);
                            if (rig.Blocks.Count == 0) continue;
                            views++;
                            least = Mathf.Min(least, rig.Blocks.Min(b => b.px) / (scale * k));
                        }
                        Check($"WV5 scale {scale} k {k}: real members, the block in output pixels >= F ({views} views, F {floor})", views > 0 && least >= floor, $"least {least:F1} output px");
                    }
            foreach (var p in Patches) Find(ctx, p).enabled = false;
        }
        var grad = Gradient(1024);
        var qtex = Keep(new Texture2D(1024, 1024, TextureFormat.RGBA32, true, false) { name = "wv_quad", wrapMode = TextureWrapMode.Clamp });
        var qpx = new Color32[1024 * 1024];
        for (int y = 0; y < 1024; y++) for (int x = 0; x < 1024; x++) qpx[y * 1024 + x] = grad(x, y);
        qtex.SetPixels32(qpx); qtex.Apply(true);
        var qm = Keep(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
        qm.SetTexture("_MainTex", qtex); qm.SetTexture("_BaseMap", qtex);
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.name = "wv_quad";
        quad.layer = 10;
        var qr = quad.GetComponent<MeshRenderer>();
        qr.sharedMaterial = qm;
        MosaicSet.Add(qr);
        NewLight();
        Shader.SetGlobalFloat(UnlitId, 1f);
        try
        {
            foreach (var scale in new[] { 0.25f, 0.5f, 1f })
                using (var rig = new WetMirrorRig(0f, scale))
                {
                    rig.Look(new Color(1f, 1f, 1f, 1f), 10f);
                    foreach (var k in new[] { 1, 2, 4 })
                        foreach (var wide in new[] { false, true })
                        {
                            var cam = NewCam(false, AntialiasingMode.None);
                            var all = new List<int>();
                            float gain = 1f / (scale * k);
                            int band = Mathf.Max(1, Mathf.CeilToInt(gain));
                            float block = 0f;
                            foreach (var d in new[] { 16f, 22f, 30f })
                            {
                                var centre = new Vector3(0f, 14f, 0f);
                                var below = centre + new Vector3(0f, -0.87f, 0.5f).normalized * d;
                                float size = 0.7f * 2f * d * Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
                                quad.transform.position = centre;
                                quad.transform.rotation = Quaternion.LookRotation(centre - below, Vector3.up);
                                quad.transform.localScale = new Vector3(size, size, 1f);
                                rig.Place(cam, centre, below);
                                MosaicWide.Enabled = wide;
                                Color[] with, without;
                                using (MosaicCamera.OutputScope(cam, k))
                                {
                                    with = Render(cam, W * k, H * k);
                                    block = wide ? (rig.Groups.Count > 0 ? rig.Groups[0].Block * gain : 0f) : rig.Blocks.Where(x => x.m.Renderer == qr).Select(x => x.px * gain).DefaultIfEmpty(0f).Min();
                                    MosaicWide.Enabled = false;
                                    qr.enabled = false;
                                    without = Render(cam, W * k, H * k);
                                    qr.enabled = true;
                                }
                                if (k > 1) { with = Downscale(with, W * k, H * k, k); without = Downscale(without, W * k, H * k, k); }
                                var fp = new bool[with.Length];
                                for (int i = 0; i < fp.Length; i++) fp[i] = with[i] != without[i];
                                fp = Erode(fp, W, H, 2);
                                all.AddRange(Runs(with, fp, W, H).Where(r => r > band));
                            }
                            string what = wide ? "(a) and (b)" : "(a)";
                            if (all.Count == 0) { Check($"WV5 scale {scale} k {k} {what}: a large member's runs", false, "none"); continue; }
                            bool ok = RunsOk(all, floor, out string det);
                            Check($"WV5 scale {scale} k {k} {what}: a large member in the puddle: flat runs: median >= F, p25 >= F - 1 (F {floor}, the band's runs up to {band} px left out)", ok, $"{det}; the last block {block:F1} output px");
                        }
                }
        }
        finally
        {
            Shader.SetGlobalFloat(UnlitId, 0f);
            qr.enabled = false;
            Object.DestroyImmediate(quad);
            MosaicSet.Standalone();
            MosaicWide.Enabled = false;
        }
    }

    static void Wv6(MakerCoreTest.Harness h)
    {
        const int W = 960, H = 540;
        var node = NodeOf(h, FemaleCard);
        try
        {
            var real = node.Real;
            var ctx = real.Context;
            HideClothes(ctx);
            NewLight();
            var comp = Composite(ctx, 2048, 6, Cells(3), Skin);
            UseComposite(ctx, comp);
            var (c, n) = PatchPose(ctx);
            using (var rig = new WetMirrorRig(ctx.Root.transform.position.y - 0.1f, 0.5f))
            {
                var view = NewCam(true, AntialiasingMode.SubpixelMorphologicalAntiAliasing);
                var below = c + (new Vector3(n.x, 0f, n.z).normalized * 0.6f - Vector3.up * 0.8f).normalized * 16f;
                rig.Place(view, c, below);
                MosaicWide.Enabled = false;
                var paths = new (string name, Action run)[]
                {
                    ("the view", () => Render(view, W, H)),
                    ("capture", () => HS2Next.App.Commands.ViewCapture.Render(view, W, H, CaptureService.None)),
                    ("capture k 2", () => CaptureService.RenderSync(null, view, new CaptureRequest { Width = W, Height = H, Supersample = 2, Aa = CaptureService.None })),
                    ("a recorded frame's call", () => CaptureService.RenderSync(null, view, new CaptureRequest { Width = W, Height = H, Supersample = 1, Aa = CaptureService.Smaa })),
                };
                foreach (var (name, run) in paths)
                {
                    if (!yellow) yellow = Flat("mosaic_test_flat_yellow", new Color32(255, 255, 0, 255));
                    // the members flat for the footprint, through the same path
                    var saved = new List<(Material m, Texture t, float mode, Color col)>();
                    foreach (var mm in MosaicSet.StateOf(ctx).Members)
                        foreach (var mat in mm.Renderer.sharedMaterials)
                        {
                            if (!mat || saved.Exists(x => x.m == mat)) continue;
                            saved.Add((mat, mat.GetTexture("_MainTex"), mat.GetFloat("_MosaicMode"), mat.GetColor("_Color")));
                            mat.SetTexture("_MainTex", yellow); mat.SetFloat("_MosaicMode", 1f); mat.SetColor("_Color", Color.white);
                        }
                    Shader.SetGlobalFloat(UnlitId, 1f);
                    try { run(); }
                    finally
                    {
                        foreach (var x in saved) { x.m.SetTexture("_MainTex", x.t); x.m.SetFloat("_MosaicMode", x.mode); x.m.SetColor("_Color", x.col); }
                        Shader.SetGlobalFloat(UnlitId, 0f);
                    }
                    var fp = Erode(rig.Image().Select(x => x.r > 0.9f && x.g > 0.9f && x.b < 0.1f).ToArray(), rig.W, rig.H, 1);
                    run();
                    var px = rig.Image();
                    int fpn = fp.Count(x => x), mk = Markers(px, fp), control = 0;
                    WithoutMosaic(ctx, comp, () => { run(); control = Markers(rig.Image(), fp); });
                    float least = rig.Blocks.Count > 0 ? rig.Blocks.Min(b => b.px) : 0f;
                    Check($"WV6 {name}: 0 marker pixels in the footprint inside the mirror image, the block >= the frame's F", fpn > 0 && mk == 0 && control > 0 && least >= rig.FramePx, $"footprint {fpn} px, markers {mk}, left out {control}, least block {least:F1} px, F {rig.FramePx:F1}, target {rig.W}x{rig.H}, output scale {rig.OutputScale}");
                }
            }
        }
        finally { MosaicWide.Enabled = false; node.Dispose(); }
    }
}

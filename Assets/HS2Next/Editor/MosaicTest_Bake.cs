// MosaicTest.render, the rendering rules of a bake (BV1-BV3). Images stay in memory and are judged by numbers.
// The fixtures are the render rows': BR-Chan with the clothes hidden and the marker composite under the patches, HERO107 with his member on and the
// marker checker as the members' own texture; each baked whole (a capture of the Maker build), built as an item draws it (the prototype's copy with
// its materials copied per slot and mapped, the members added), the source hidden while the bake is drawn.
//   BV1  every member renderer of the bake is a member before its first frame: the first render shows 0 marker pixels in its footprint
//   BV2  a member's stored slots are the mosaic material's values; no other material is stored for it
//   BV3  (a) 0 marker pixels in the members' footprint and every block at least the floor F, (b) the wide pass covers the footprint; three views
//        and a 2x capture; the mode printed. A view whose footprint is under 150 px is not judged; at least 2 judged per target. Positive
//        control (MR1, RV3): the members drawn unlit with the marker texture, the mosaic left out, show the marker in the footprint; a copy built
//        with the members not added is printed (its stored slots draw the mosaic anyway).
// CutGroups (BV4, BV5) is the Cutter's part (a partial method: absent, it compiles away).
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next;
using HS2Next.Chara;
using HS2Next.Chara.Mosaic;
using HS2Next.Scenes.Bake;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using static MakerTestKit;
using Object = UnityEngine.Object;

public static partial class MosaicTest
{
    const int BvW = 1280, BvH = 720, BvLeastFootprint = 150, BvLeastJudged = 2;

    static partial void CutGroups(MakerCoreTest.Harness h);

    static void BakeGroups(MakerCoreTest.Harness h)
    {
        if (Want("BV1") || Want("BV2") || Want("BV3")) Guard("BV1-BV3 a bake of the members", () => Bv(h));
        CutGroups(h);
    }

    /// <summary>A bake of a Maker build drawn as an item draws it, beside its source.</summary>
    internal sealed class BvBake : IDisposable
    {
        public BakeJob Job;
        public BakePrototype Proto;
        public GameObject Container, Holder, Instance;
        public List<Renderer> Members = new List<Renderer>();
        public int Added;
        readonly List<Material> copies = new List<Material>();

        /// <summary>Bakes <paramref name="ctx"/> whole and builds its item copy; <paramref name="addMembers"/> false leaves the members out (the control).</summary>
        public static BvBake Of(CharaContext ctx, string label, bool male, bool addMembers, BvBake shareJob = null)
        {
            var b = new BvBake();
            if (shareJob != null) b.Job = shareJob.Job;
            else
            {
                b.Job = new BakeJob();
                b.Job.Capture = BakeCapture.Context(ctx, ctx.Root.transform, label, male ? 0 : 1, null, null, new BakeOptions(), b.Job);
            }
            b.Container = new GameObject("bv_bake_container");
            b.Container.SetActive(false);
            b.Proto = BakeBuild.Prototype(b.Job.Capture.Bake, i => b.Job.Capture.Bake.Meshes[i].Data,
                i => i < b.Job.Textures.Count ? b.Job.Textures[i] : null, b.Container.transform, false);
            var cap = b.Job.Capture;
            b.Holder = new GameObject("bv_bake_item");
            b.Holder.transform.SetPositionAndRotation(cap.Position, cap.Rotation);
            b.Holder.transform.localScale = cap.Scale;
            b.Instance = BakeBuild.Instantiate(b.Proto, b.Holder.transform);
            foreach (var r in b.Instance.GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++) if (ms[i]) { ms[i] = MaterialCopies.Copy(ms[i]); b.copies.Add(ms[i]); }
                r.sharedMaterials = ms;
            }
            ShaderMap.ApplyUrp(b.Instance, copy: false);
            bool skip = BakeBuild.SkipMembers;
            BakeBuild.SkipMembers = !addMembers;
            try { b.Added = BakeBuild.AddMembers(b.Proto, b.Instance); }
            finally { BakeBuild.SkipMembers = skip; }
            b.Members = BakeBuild.MembersOf(b.Proto, b.Instance);
            return b;
        }

        public void Dispose()
        {
            if (Holder) Object.DestroyImmediate(Holder);
            Proto?.Destroy();
            if (Container) Object.DestroyImmediate(Container);
            foreach (var m in copies) if (m) Object.DestroyImmediate(m);
            copies.Clear();
        }
    }

    /// <summary>The given renderers drawn flat with <paramref name="marker"/> (texture mode, the unlit test view; post-processing, AA and the wide
    /// pass off), everything else as it is; their material values come back (MarkerRender for renderers outside a character).</summary>
    static Color[] MarkerRenderOf(Camera cam, int w, int h, IList<Renderer> members, Texture2D marker)
    {
        var urp = cam.GetComponent<UniversalAdditionalCameraData>();
        bool post = urp.renderPostProcessing; var aa = urp.antialiasing; bool wide = MosaicWide.Enabled;
        urp.renderPostProcessing = false; urp.antialiasing = AntialiasingMode.None; MosaicWide.Enabled = false;
        var saved = new List<(Material m, Texture t, Vector2 sc, Vector2 of, float mode, Color c)>();
        foreach (var r in members)
            foreach (var mat in r.sharedMaterials)
            {
                if (!mat || saved.Exists(x => x.m == mat) || !mat.HasProperty("_MosaicMode")) continue;
                saved.Add((mat, mat.GetTexture("_MainTex"), mat.GetTextureScale("_MainTex"), mat.GetTextureOffset("_MainTex"), mat.GetFloat("_MosaicMode"), mat.GetColor("_Color")));
                mat.SetTexture("_MainTex", marker); mat.SetTextureScale("_MainTex", Vector2.one); mat.SetTextureOffset("_MainTex", Vector2.zero);
                mat.SetFloat("_MosaicMode", 1f); mat.SetColor("_Color", Color.white);
            }
        Shader.SetGlobalFloat(UnlitId, 1f);
        try { return Render(cam, w, h); }
        finally
        {
            foreach (var x in saved)
            {
                x.m.SetTexture("_MainTex", x.t); x.m.SetTextureScale("_MainTex", x.sc); x.m.SetTextureOffset("_MainTex", x.of);
                x.m.SetFloat("_MosaicMode", x.mode); x.m.SetColor("_Color", x.c);
            }
            Shader.SetGlobalFloat(UnlitId, 0f); urp.renderPostProcessing = post; urp.antialiasing = aa; MosaicWide.Enabled = wide;
        }
    }

    static bool[] FootprintOf(Camera cam, int w, int h, IList<Renderer> members)
    {
        if (!magenta) magenta = Flat("mosaic_test_flat", new Color32(255, 0, 255, 255));
        return MarkerRenderOf(cam, w, h, members, magenta).Select(c => c.r > 0.9f && c.g < 0.1f && c.b > 0.9f).ToArray();
    }

    /// <summary>forceRenderingOff on every renderer of <paramref name="roots"/> but those of <paramref name="shown"/>, put back by the scope.</summary>
    sealed class OnlyOf : IDisposable
    {
        readonly List<(Renderer r, bool off)> saved = new List<(Renderer, bool)>();
        public OnlyOf(IEnumerable<GameObject> roots, GameObject shown)
        {
            foreach (var root in roots)
            {
                if (!root) continue;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    saved.Add((r, r.forceRenderingOff));
                    r.forceRenderingOff = !(shown && r.transform.IsChildOf(shown.transform));
                }
            }
        }
        public void Dispose() { foreach (var (r, off) in saved) if (r) r.forceRenderingOff = off; }
    }

    /// <summary>The given members' slots drawn by an unlit material with <paramref name="tex"/> at their own UVs (the mosaic left out), around
    /// <paramref name="a"/> (WithoutMosaic for a bake's members: the positive control of MR1 and RV3).</summary>
    static void WithoutMosaicOf(IList<Renderer> members, Texture tex, Action a)
    {
        var plain = Keep(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
        plain.SetTexture("_BaseMap", tex); plain.SetColor("_BaseColor", Color.white);
        var saved = new List<(Renderer r, Material[] m)>();
        foreach (var r in members)
        {
            if (!r) continue;
            saved.Add((r, r.sharedMaterials));
            r.sharedMaterials = Enumerable.Repeat(plain, r.sharedMaterials.Length).ToArray();
        }
        bool wide = MosaicWide.Enabled;
        MosaicWide.Enabled = false;
        try { a(); }
        finally { foreach (var (r, m) in saved) if (r) r.sharedMaterials = m; MosaicWide.Enabled = wide; }
    }

    /// <summary>The standalone mosaic record of a renderer (a bake's member), or null.</summary>
    static MosaicMember StandaloneOf(Renderer r) => MosaicSet.Standalone().FirstOrDefault(m => m.Renderer == r);

    /// <summary>The judge of (a) and (b) on a bake's members at one view (BV3; the Cutter's BV4 calls it on a piece): marker pixels in the
    /// footprint, the members' blocks against the floor, the wide pass's coverage of the footprint dilated by 1.5 blocks. False when the
    /// footprint is under <see cref="BvLeastFootprint"/> (not judged).</summary>
    internal static bool BvJudge(string what, Camera cam, int w, int h, int k, IList<Renderer> members, GameObject[] hide, GameObject shown, List<string> lines,
        ref int markers, ref int under, ref int uncovered, ref int judged)
    {
        using (new OnlyOf(hide.Append(shown), shown))
        using (MosaicCamera.OutputScope(cam, k))
        {
            var fp = FootprintOf(cam, w * k, h * k, members);
            int fpn = fp.Count(x => x);
            if (fpn < BvLeastFootprint * k * k) { lines.Add($"{what}: footprint {fpn} px, not judged"); return false; }
            MosaicWide.Enabled = false;
            var px = Render(cam, w * k, h * k);
            int mk = Markers(px, fp);
            float floor = Mathf.Max(4f, Mathf.Ceil(Mathf.Max(w, h) / 100f));
            float least = float.MaxValue;
            string modes = "";
            foreach (var r in members)
            {
                var m = StandaloneOf(r);
                if (m == null) continue;
                if (m.BlockPx > 0f) least = Mathf.Min(least, m.BlockPx / k);
                modes += (modes.Length > 0 ? ", " : "") + r.name + " " + m.Mode;
            }
            MosaicWide.Enabled = true;
            MosaicWide.DebugView = true;
            var cov = Render(cam, w * k, h * k).Select(x => x.r > 0.5f).ToArray();
            MosaicWide.DebugView = false;
            var g = MosaicWideFeature.LastGroups.FirstOrDefault();
            MosaicWide.Enabled = false;
            float B = g.Block;
            int miss = 0, W = w * k, H = h * k, ir = Mathf.CeilToInt(1.5f * B);
            float r2 = 1.5f * B * 1.5f * B;
            var seen = (bool[])cov.Clone();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (!fp[y * W + x]) continue;
                    for (int dy = -ir; dy <= ir; dy++)
                        for (int dx = -ir; dx <= ir; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx < 0 || yy < 0 || xx >= W || yy >= H || dx * dx + dy * dy > r2) continue;
                            if (!seen[yy * W + xx]) { miss++; seen[yy * W + xx] = true; }
                        }
                }
            judged++;
            markers += mk;
            if (least < floor - 1e-3f || least == float.MaxValue) under++;
            uncovered += miss;
            lines.Add($"{what}: footprint {fpn} px, markers {mk}, least block {(least == float.MaxValue ? "none" : least.ToString("F1"))} output px (F {floor}), wide B {B:F1}, uncovered {miss} px; {modes}");
            return true;
        }
    }

    static void Bv(MakerCoreTest.Harness h)
    {
        foreach (var (label, file, male) in new[] { ("BR-Chan", FemaleCard, false), ("HERO107", MaleCard, true) })
            using (var f = new Disposer(h.Build(file)))
            {
                if (!Check($"BV {label} builds", f.B.Ok, f.B.Error ?? "")) continue;
                var ctx = f.B.Context;
                Texture marker;
                var saved = new List<(Material m, Texture t)>();
                if (male)
                {
                    var nDan = HS2Next.Chara.Edit.EditContext.Find(ctx.Body.transform, "N_dan");
                    if (nDan) nDan.gameObject.SetActive(true);
                    RunStage(ctx);
                    marker = Wv3mChecker(2048, 5);
                    foreach (var mm in MosaicSet.StateOf(ctx)?.Members ?? new List<MosaicMember>())
                        foreach (var mat in mm.Renderer.sharedMaterials)
                            if (mat && mat.HasProperty("_MainTex") && !saved.Exists(x => x.m == mat)) { saved.Add((mat, mat.GetTexture("_MainTex"))); mat.SetTexture("_MainTex", marker); }
                }
                else
                {
                    HideClothes(ctx);
                    marker = Composite(ctx, 2048, 6, MarkerTexel, Skin);
                    UseComposite(ctx, marker);
                }
                NewLight();
                var source = MosaicSet.StateOf(ctx)?.Members.Where(m => m.Renderer && m.Renderer.enabled && m.Renderer.gameObject.activeInHierarchy).ToList() ?? new List<MosaicMember>();
                var focus = Member(ctx, male ? "cm_o_dan00" : "o_mnpb");
                if (!Check($"BV1 {label} present: >= 1 member in the source", source.Count >= 1 && focus != null, $"{source.Count} members drawn: {string.Join(", ", source.Select(m => m.Renderer.name))}")) continue;
                BvBake bk = null, ctl = null;
                try
                {
                    bk = BvBake.Of(ctx, label, male, true);
                    var b = bk.Job.Capture.Bake;
                    int marked = b.Renderers.Count(r => r.Member);
                    // BV1: members before the first frame
                    var c = MosaicCamera.RegionMatrix(focus).MultiplyPoint3x4(focus.Region.Centroid);
                    var n = male ? ctx.Root.transform.forward : PatchPose(ctx).n;
                    var cam = NewCam(true, AntialiasingMode.None);
                    Aim(cam, c, n, 10f);
                    bool[] fp0;
                    Color[] first;
                    using (new OnlyOf(new[] { ctx.Root, bk.Holder }, bk.Holder))
                    {
                        MosaicWide.Enabled = false;
                        bool allAdded = bk.Members.All(MosaicSet.IsAdded);
                        first = Render(cam, BvW, BvH);
                        fp0 = FootprintOf(cam, BvW, BvH, bk.Members);
                        Check($"BV1 {label}: every member renderer of the bake is a member before its first frame", marked == source.Count && bk.Members.Count == marked && bk.Added == marked && allAdded,
                            $"source members {source.Count}, marked {marked}, built {bk.Members.Count}, added {bk.Added}, every one a member {allAdded}");
                    }
                    int mk0 = Markers(first, fp0);
                    Check($"BV1 {label}: the first frame shows 0 marker pixels in the members' footprint", mk0 == 0 && fp0.Count(x => x) >= BvLeastFootprint, $"markers {mk0}, footprint {fp0.Count(x => x)} px");
                    // BV2: the stored slots of a member are the mosaic material's values
                    int memberSlots = 0, notMosaic = 0, differ = 0;
                    var diffs = new List<string>();
                    for (int i = 0; i < b.Renderers.Count; i++)
                    {
                        var rd = b.Renderers[i];
                        if (!rd.Member) continue;
                        var live = bk.Job.Capture.Live[i];
                        var lm = live.sharedMaterials;
                        for (int k = 0; k < rd.Materials.Length; k++)
                        {
                            memberSlots++;
                            var md = rd.Materials[k] >= 0 ? b.Materials[rd.Materials[k]] : null;
                            if (md == null || !MosaicSet.IsMosaicShaderName(md.Shader)) { notMosaic++; continue; }
                            var a = k < lm.Length ? lm[k] : null;
                            if (!a) { differ++; continue; }
                            foreach (var p in md.Props)
                            {
                                bool same = true;
                                switch (p.Type)
                                {
                                    case 'f': same = a.GetFloat(p.Name) == p.F; break;
                                    case 'i': same = a.GetInteger(p.Name) == p.I; break;
                                    case 'c': same = (Vector4)a.GetColor(p.Name) == p.V; break;
                                    case 'v': same = a.GetVector(p.Name) == p.V; break;
                                    case 't': same = (a.GetTexture(p.Name) == null) == (p.Tex < 0); break;
                                }
                                if (!same) { differ++; if (diffs.Count < 6) diffs.Add(live.name + "." + p.Name); }
                            }
                        }
                    }
                    Check($"BV2 {label}: a member's stored slots are the mosaic material's values, no other material stored for it", memberSlots > 0 && notMosaic == 0 && differ == 0,
                        $"{memberSlots} slots, {notMosaic} not the mosaic shader, {differ} values differ {string.Join(", ", diffs)}");
                    // BV3: three views and a 2x capture
                    var lines = new List<string>();
                    int markers = 0, under = 0, uncovered = 0, judged = 0;
                    var right = Vector3.Cross(Vector3.up, n).normalized;
                    var views = new[] { ("front", n), ("30 deg up", Quaternion.AngleAxis(-30f, right) * n), ("45 deg side", Quaternion.AngleAxis(45f, Vector3.up) * n) };
                    var hide = new[] { ctx.Root };
                    foreach (var (vn, dir) in views)
                    {
                        Aim(cam, c, dir, 10f);
                        BvJudge($"BV3 {label} {vn}", cam, BvW, BvH, 1, bk.Members, hide, bk.Holder, lines, ref markers, ref under, ref uncovered, ref judged);
                    }
                    Aim(cam, c, n, 10f);
                    BvJudge($"BV3 {label} front, 2x capture", cam, BvW, BvH, 2, bk.Members, hide, bk.Holder, lines, ref markers, ref under, ref uncovered, ref judged);
                    foreach (var l in lines) Info(l);
                    Check($"BV3 {label} present: >= {BvLeastJudged} views judged (footprint >= {BvLeastFootprint} px)", judged >= BvLeastJudged, judged + " judged");
                    Check($"BV3 {label} (a): 0 marker pixels in the footprint, every block >= F", judged > 0 && markers == 0 && under == 0, $"markers {markers}, views with a block under F {under}");
                    Check($"BV3 {label} (b): the wide pass covers the footprint (dilated by 1.5 blocks)", judged > 0 && uncovered == 0, $"uncovered {uncovered} px");
                    // the positive control (MR1 and RV3's form, ruled by the lead 03:4x): the bake members' slots drawn unlit with the marker texture,
                    // the mosaic left out, show the marker in the same footprint
                    Aim(cam, c, n, 10f);
                    int posMarkers = 0, posFp;
                    using (new OnlyOf(new[] { ctx.Root, bk.Holder }, bk.Holder))
                    {
                        MosaicWide.Enabled = false;
                        var pfp = FootprintOf(cam, BvW, BvH, bk.Members);
                        posFp = pfp.Count(x => x);
                        WithoutMosaicOf(bk.Members, marker, () => posMarkers = Markers(Render(cam, BvW, BvH), pfp));
                    }
                    Check($"BV3 {label} present: the members drawn unlit with the marker texture (the mosaic left out) show the marker in the footprint", posMarkers > 0,
                        $"{posMarkers} marker pixels in a footprint of {posFp} px");
                    // an observation: a copy whose members are not added again still draws the mosaic (its stored slots are the mosaic material)
                    ctl = BvBake.Of(ctx, label, male, false, bk);
                    Aim(cam, c, n, 10f);
                    int ctlMarkers;
                    using (new OnlyOf(new[] { ctx.Root, bk.Holder, ctl.Holder }, ctl.Holder))
                    {
                        MosaicWide.Enabled = false;
                        var cpx = Render(cam, BvW, BvH);
                        var cfp = FootprintOf(cam, BvW, BvH, ctl.Members);
                        ctlMarkers = Markers(cpx, cfp);
                    }
                    Info($"BV3 {label}: a bake member's stored slots draw the mosaic even without the re-add: {ctlMarkers} marker pixels with the re-add skipped");
                }
                finally
                {
                    ctl?.Dispose();
                    bk?.Dispose();
                    bk?.Job?.Dispose();
                    foreach (var (m, t) in saved) if (m) m.SetTexture("_MainTex", t);
                    MosaicWide.Enabled = false;
                    MosaicWide.DebugView = false;
                }
            }
    }
}

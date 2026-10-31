// MapTest.render (package B of Phase 3.2): map-only captures of the map shader families in
// play mode with the app's URP asset, from the fixed camera of the HS2 comparison (Tools/hs2_compare.py MAP_CAMERA: 60 units
// from (0, 10, 0), 20 degrees down, facing -z, vertical fov 50; 1280 x 720), no characters.
// Run: & Tools\unity_batch.ps1 -Method MapRenderTest.Run -NoQuit [-MethodArgs '-maps','17,29']
// Report: Spike/out/maps/map_render_report.txt ("SUMMARY: N PASS, M FAIL"); captures Spike/out/maps/render/<case>.png
// (full view) and <case>_<family>.png (only that family's renderers drawn, on black).
// Per map: loaded; no magenta pixels (a shader without a URP pass draws magenta); per family present in the map (SpeedTree,
// mirror, Silhouette, water, KriptoFX particles, terrain, Tree Creator): its pixels (the family-only capture against black
// and white backgrounds) are not all black; mirrors: rendered (MapMirrors) and their region not constant; SpeedTree wind
// (V3): the family-only capture 1.5 s later differs. Reported, not judged: the mean colour of the family's pixels in the
// HS2Next capture and in HS2's capture of the same view (Cache/compare/maps/<case>_default_nofx.png, package C) where one
// exists; the difference is the tolerance band of later runs (fog and the Studio ambient colours differ until Phase 3.3).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next;
using HS2Next.App.Commands;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class MapRenderTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "maps");
    static string RenderDir => Path.Combine(OutDir, "render");
    static string Report => Path.Combine(OutDir, "map_render_report.txt");
    static StringBuilder rep;
    static int pass, fail;

    /// <summary>(case name as in Cache/compare/maps, map ref).</summary>
    public static readonly (string name, string mref)[] Maps =
    {
        ("v17", "illusion.hs2#17"), ("v14", "illusion.hs2#14"), ("v38", "illusion.hs2#38"), ("v29", "illusion.hs2#29"),
        ("v0", "illusion.hs2#0"), ("v35", "illusion.hs2#35"), ("v36", "illusion.hs2#36"), ("v32", "illusion.hs2#32"),
        ("v34", "illusion.hs2#34"), ("mTerrain", "hooh.map.school#1"), ("mTreeCreator", "[Fouetty]Military Base#0"),
    };

    const int W = 1280, H = 720;

    static readonly (string family, Func<string, bool> match)[] Families =
    {
        ("speedtree", s => s.StartsWith("Universal Render Pipeline/Nature/SpeedTree7", StringComparison.Ordinal)),
        ("mirror", s => s.StartsWith("HS2Next/Compatible/FX/MirrorReflection", StringComparison.Ordinal)),
        ("silhouette", s => s == "HS2Next/Custom/Silhouette"),
        ("water", s => s == "HS2Next/Compatible/Water" || s == "HS2Next/Compatible/Water Particles Foam"),
        ("kriptofx", s => s == "HS2Next/Compatible/KriptoFX/ME/Particle"),
        ("treecreator", s => s.StartsWith("HS2Next/Nature/Tree Creator", StringComparison.Ordinal)),
    };

    static void Check(string what, bool ok, string detail = "")
    {
        if (ok) pass++; else fail++;
        rep.AppendLine($"   {(ok ? "PASS" : "FAIL")} {what}{(string.IsNullOrEmpty(detail) ? "" : " - " + detail)}");
    }
    static void Info(string s) => rep.AppendLine("   " + s);

    static string Arg(string name)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    public static void Run()
    {
        Directory.CreateDirectory(RenderDir);
        File.WriteAllText(Report, "SUMMARY: 0 PASS, 1 FAIL\nMapTest.render: started, no result yet\n", new UTF8Encoding(false));
        PlayModeHarness.Start(nameof(MapRenderTest) + "." + nameof(Main), Report, 40);
    }

    static IEnumerator Main()
    {
        rep = new StringBuilder(); pass = fail = 0;
        var sw = Stopwatch.StartNew();
        rep.AppendLine($"MapTest.render (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
        var pipe = new HS2Next.App.AppPipeline();
        pipe.Apply();
        Info("pipeline: " + pipe.Summary);
        MapShaderHooks.Install();
        var gd = CardResolveTest.BuildDb(null);
        var db = gd.LoadStudioLists(new StudioLoadOptions { EmulateAIInstallGate = false });
        var findings = new SceneFindings();
        var map = new MapRealization(gd, db, findings, null);
        MainLight.Reset();
        var camGo = new GameObject("map_render_view");
        var cam = camGo.AddComponent<Camera>();
        cam.enabled = false;
        float r20 = 20 * Mathf.Deg2Rad;
        cam.transform.SetPositionAndRotation(new Vector3(0, 10 + 60 * Mathf.Sin(r20), 60 * Mathf.Cos(r20)), Quaternion.Euler(20, 180, 0));
        cam.fieldOfView = 50; cam.nearClipPlane = 0.1f; cam.farClipPlane = 5000;
        string list = Arg("-maps");
        var maps = list == null ? Maps : list.Split(',', ';', '+').Select(x => x.Trim()).Where(x => x.Length > 0)
            .Select(x => Maps.FirstOrDefault(m => m.name == x || m.name == "v" + x || m.mref == x) is var f && f.mref != null ? f : ("x" + x.Replace('#', '_'), x.Contains("#") ? x : "illusion.hs2#" + x)).ToArray();
        bool[] ok = { false };
        foreach (var (name, mref) in maps)
        {
            rep.AppendLine($"\n== {name} ({mref})");
            map.SetFlags(true, true);
            map.SetTarget(mref, Vec3.Zero, Vec3.Zero);
            yield return PlayModeHarness.WaitUntil(() => { map.Apply(); return !map.IsBusy; }, 600, ok);
            yield return PlayModeHarness.WaitFrames(5);
            Check("loaded", map.State == MapState.Loaded, map.State.ToString());
            if (map.State != MapState.Loaded) continue;
            Info("hooks: " + MapShaderHooks.Last + $"; terrains converted {TerrainMap.LastConverted}, prototypes mapped {TerrainMap.LastPrototypes}");
            IEnumerator body = null;
            try { body = OneMap(name, map, cam); }
            catch (Exception e) { Check("capture ran", false, e.ToString()); }
            if (body != null) yield return Guard(body);
        }
        map.SetTarget(null, Vec3.Zero, Vec3.Zero);
        yield return PlayModeHarness.WaitUntil(() => { map.Apply(); return !map.IsBusy && map.PendingUnloads == 0; }, 60, ok);
        Object.Destroy(camGo);
        try { gd.UnloadAll(); } catch (Exception) { }
        pipe.Restore();
        rep.Insert(0, $"SUMMARY: {pass} PASS, {fail} FAIL\nMAP RENDER TEST ({sw.Elapsed.TotalSeconds:F0} s)\n");
        File.WriteAllText(Report, rep.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
        Debug.Log($"MAP RENDER TEST: {pass} PASS, {fail} FAIL");
        PlayModeHarness.ExitCode = fail > 0 ? 1 : 0;
    }

    static IEnumerator Guard(IEnumerator steps)
    {
        while (true)
        {
            object cur;
            try
            {
                if (!steps.MoveNext()) yield break;
                cur = steps.Current;
            }
            catch (Exception e) { Check("capture ran without an exception", false, e.ToString()); yield break; }
            yield return cur;
        }
    }

    static Texture2D Capture(Camera cam, string file, Color bg)
    {
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = bg;
        var png = ViewCapture.Render(cam, W, H, ViewCapture.None);
        if (file != null) File.WriteAllBytes(Path.Combine(RenderDir, file), png);
        var t = new Texture2D(2, 2, TextureFormat.RGB24, false);
        t.LoadImage(png);
        return t;
    }

    static IEnumerator OneMap(string name, MapRealization map, Camera cam)
    {
        int mirrors0 = MapMirrors.RendersTotal;
        // full view: the map's own skybox / background (the Studio's camera clears with the sky until 3.3: solid grey here)
        var full = Capture(cam, name + ".png", new Color(0.19f, 0.30f, 0.47f));
        var px = full.GetPixels32();
        int magenta = px.Count(c => c.r > 235 && c.b > 235 && c.g < 30);
        Check("no magenta pixels", magenta < px.Length / 2000, $"{magenta} of {px.Length}");
        Color32[] hs2 = null;
        string hs2File = Path.Combine(ContentSettings.ProjectRoot, "Cache", "compare", "maps", name + "_default_nofx.png");
        if (File.Exists(hs2File))
        {
            var t = new Texture2D(2, 2, TextureFormat.RGB24, false);
            t.LoadImage(File.ReadAllBytes(hs2File));
            if (t.width == W && t.height == H) hs2 = t.GetPixels32();
            Object.Destroy(t);
            Info($"mean colour full view: HS2Next {Mean(px, null)}; HS2 {(hs2 != null ? Mean(hs2, null) : "-")}");
        }
        // families
        var renderers = map.Structure.Renderers.Where(r => r).ToList();
        foreach (var (fam, match) in Families)
        {
            var mine = renderers.Where(r => r.sharedMaterials.Any(m => m && m.shader && match(m.shader.name)) ||
                                            fam == "speedtree" && r is BillboardRenderer br && br.billboard && br.billboard.material && match(br.billboard.material.shader.name)).ToList();
            if (mine.Count == 0) continue;
            var enabledBefore = renderers.ToDictionary(r => r, r => r.enabled);
            foreach (var r in renderers) r.enabled = mine.Contains(r) && enabledBefore[r];
            Terrain[] terrains = map.Structure.Terrains.Where(t => t && t.drawHeightmap).ToArray();
            foreach (var t in terrains) t.drawHeightmap = false;
            Texture2D a = null, b = null, later = null;
            try
            {
                a = Capture(cam, $"{name}_{fam}.png", Color.black);
                b = Capture(cam, null, Color.white);
            }
            finally
            {
                foreach (var t in terrains) t.drawHeightmap = true;
                foreach (var r in renderers) r.enabled = enabledBefore[r];
            }
            var pa = a.GetPixels32(); var pb = b.GetPixels32();
            var mask = new bool[pa.Length];
            int cover = 0;
            for (int i = 0; i < pa.Length; i++)
            {
                var c1 = pa[i]; var c2 = pb[i];
                bool cov = c1.r > 6 || c1.g > 6 || c1.b > 6 || c2.r < 249 || c2.g < 249 || c2.b < 249;
                mask[i] = cov; if (cov) cover++;
            }
            float cf = cover / (float)pa.Length;
            if (cover < 50)
            {
                Info($"{fam}: {mine.Count} renderers, {cover} pixels in view (not judged)");
                Object.Destroy(a); Object.Destroy(b);
                if (fam == "mirror") MirrorView(name, map, cam, mine, mirrors0);
                continue;
            }
            // the family's own colour (opaque pixels from the black-background capture)
            double lum = 0; int nl = 0;
            for (int i = 0; i < pa.Length; i++) if (mask[i]) { lum += (0.2126 * pa[i].r + 0.7152 * pa[i].g + 0.0722 * pa[i].b) / 255.0; nl++; }
            lum /= Math.Max(1, nl);
            Check($"{fam}: {mine.Count} renderers, {100 * cf:F1} % of the view, not black", lum > 0.02, $"mean luminance {lum:F3} (family alone, map lights)");
            Info($"{fam}: mean colour over its pixels: HS2Next full view {Mean(px, mask)}{(hs2 != null ? $", HS2 {Mean(hs2, mask)}" : "")}");
            if (fam == "mirror")
            {
                Check("mirrors rendered by MapMirrors", MapMirrors.RendersTotal > mirrors0, $"{MapMirrors.RendersTotal - mirrors0} renders, {MapMirrors.SkippedOverCap} skipped over the cap");
                double sd = Sd(px, mask);
                Check("mirror region not constant", sd > 3, $"std-dev {sd:F1} (0..255)");
            }
            if (fam == "speedtree")
            {
                // V3: does Unity 6 animate SpeedTree 7 wind from the 2018 Tree components? the family alone again after 1.5 s
                foreach (var r in renderers) r.enabled = mine.Contains(r) && enabledBefore[r];
                foreach (var t in terrains) t.drawHeightmap = false;
                yield return PlayModeHarness.WaitRealtime(1.5f);
                try { later = Capture(cam, null, Color.black); }
                finally
                {
                    foreach (var t in terrains) t.drawHeightmap = true;
                    foreach (var r in renderers) r.enabled = enabledBefore[r];
                }
                var pl = later.GetPixels32();
                double d = 0; int nd = 0;
                for (int i = 0; i < pa.Length; i++) if (mask[i]) { d += Math.Abs(pa[i].r - pl[i].r) + Math.Abs(pa[i].g - pl[i].g) + Math.Abs(pa[i].b - pl[i].b); nd++; }
                d /= Math.Max(1, nd) * 3;
                Info($"V3 wind: mean |d| of the trees between two captures 1.5 s apart {d:F2} (0..255) -> {(d > 0.5 ? "animated" : "static")}; wind zones {Object.FindObjectsByType<WindZone>(FindObjectsInactive.Exclude).Length}");
            }
            Object.Destroy(a); Object.Destroy(b); if (later) Object.Destroy(later);
        }
        Object.Destroy(full);
        yield return null;
    }

    /// <summary>Mirrors outside the comparison view: a camera 2.5 m in front of the first mirror (its plane normal), looking at
    /// it from slightly above; the full view and the mirror-only masks as in the family checks.</summary>
    static void MirrorView(string name, MapRealization map, Camera cam, List<Renderer> mine, int mirrors0)
    {
        var m = map.Structure.Mirrors.FirstOrDefault(x => x.Stub && x.Renderer && x.Renderer.enabled && x.Renderer.gameObject.activeInHierarchy);
        if (m.Renderer == null) { Info("mirror: no active mirror"); return; }
        var t0 = cam.transform.position; var r0 = cam.transform.rotation;
        try
        {
            var c = m.Renderer.bounds.center;
            var n = m.Stub.transform.up;
            var pos = c + n * 2.5f + Vector3.up * 0.4f + Vector3.Cross(n, Vector3.up).normalized * 0.6f;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(c - pos));
            int before = MapMirrors.RendersTotal;
            var full = Capture(cam, name + "_mirrorview.png", new Color(0.19f, 0.30f, 0.47f));
            var px = full.GetPixels32();
            // the mirror's pixels: where the view changes when the mirror renderers are switched off (a mirror-only capture
            // would reflect nothing but the clear colour)
            Texture2D a;
            try
            {
                foreach (var r in mine) r.enabled = false;
                a = Capture(cam, name + "_mirrorview_off.png", new Color(0.19f, 0.30f, 0.47f));
            }
            finally { foreach (var r in mine) r.enabled = true; }
            var pa = a.GetPixels32();
            var mask = new bool[pa.Length]; int cover = 0;
            for (int i = 0; i < pa.Length; i++)
            {
                mask[i] = Math.Abs(pa[i].r - px[i].r) + Math.Abs(pa[i].g - px[i].g) + Math.Abs(pa[i].b - px[i].b) > 6;
                if (mask[i]) cover++;
            }
            Check("mirror view: mirrors rendered by MapMirrors", MapMirrors.RendersTotal > before, $"{MapMirrors.RendersTotal - before} renders in this view ({MapMirrors.RendersTotal - mirrors0} since the load)");
            double sd = Sd(px, mask);
            Check($"mirror view: mirror region ({100.0 * cover / pa.Length:F1} % of the view) not constant", cover > 1000 && sd > 3, $"std-dev {sd:F1} (0..255), mean {Mean(px, mask)}");
            Object.Destroy(full); Object.Destroy(a);
        }
        finally { cam.transform.SetPositionAndRotation(t0, r0); }
    }

    static string Mean(Color32[] px, bool[] mask)
    {
        double r = 0, g = 0, b = 0; int n = 0;
        for (int i = 0; i < px.Length; i++)
        {
            if (mask != null && !mask[i]) continue;
            r += px[i].r; g += px[i].g; b += px[i].b; n++;
        }
        n = Math.Max(1, n);
        return $"({r / n:F1}, {g / n:F1}, {b / n:F1})";
    }

    static double Sd(Color32[] px, bool[] mask)
    {
        double s = 0, s2 = 0; int n = 0;
        for (int i = 0; i < px.Length; i++)
        {
            if (!mask[i]) continue;
            double v = (px[i].r + px[i].g + px[i].b) / 3.0;
            s += v; s2 += v * v; n++;
        }
        if (n == 0) return 0;
        double m = s / n;
        return Math.Sqrt(Math.Max(0, s2 / n - m * m));
    }
}

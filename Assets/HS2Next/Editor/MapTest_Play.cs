// MapTest.play (package A): vanilla 10, 11, 17, 29, 32, 35, 37, 0 and one mod map per kind
// (a 2155X lightmapped map with probes, then a second one right after it (V4), a Bakery map, a multi-root map, a
// MapComponent-outside-root-0 map, a terrain map, a static-batched map, a PPv2 map, the mirror mod map, the 2 GB-lightmap
// park for memory) loaded in play mode through a standalone MapRealization: load → checks → flags / pose → checks →
// unload → leak check. Checks per map (MapCheck, shared with the corpus): state Loaded; every light has URP data, is folded
// (colour = original × intensity, intensity 1) and has rendering layers = culling mask; every renderer and terrain
// renderingLayerMask = 1 << layer; no Built-in shader left on any slot; lightmap and lightmapped-renderer counts equal the
// census row (Tools/survey/map_census.tsv); probes tetrahedralized; the block objects in the expected state for both flags;
// audio sources stopped and disabled; cameras and listeners disabled; MainLight per the rule; FMOD "file not found" lines
// during the load (0 expected with the abdata junction, M9); unload: material and texture counts back, the app scene active.
// Run: & Tools\unity_batch.ps1 -Method MapTest.RunPlay -NoQuit [-MethodArgs '-maps','10,32'] (no graphics needed).
// Report: Spike/out/maps/map_play_report.txt ("SUMMARY: N PASS, M FAIL").
// MapTest.RunAudioProbe: the M9 measurement (map 10 without and with the junction <dataPath>/../abdata; the junction is
// removed again only when this run created it) → Spike/out/maps/map_audio_probe.txt.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static partial class MapTest
{
    static string PlayReport => Path.Combine(OutDir, "map_play_report.txt");

    /// <summary>The maps of MapTest.play in order (refs).</summary>
    public static readonly string[] PlayMaps =
    {
        "illusion.hs2#10", "illusion.hs2#11", "illusion.hs2#17", "illusion.hs2#29", "illusion.hs2#32", "illusion.hs2#35", "illusion.hs2#37", "illusion.hs2#0",
        "2155x.map.phsubway#0", "2155x.map.interrogationroom#0",   // two lightmapped maps in a row (V4)
        "hooh.map.private.club#0", "creepy.classroom#0", "hooh.map.library.fixed#3", "hooh.map.school#1", "hooh.map.classroom.2020#7",
        "(Mas75) Airport#0", "com.nyaacho.map.poweder#1801", "com.nyaacho.map.park#1807",
    };

    public static void RunPlay()
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(PlayReport, "SUMMARY: 0 PASS, 1 FAIL\nMapTest.play: started, no result yet\n", new UTF8Encoding(false));
        PlayModeHarness.Start(nameof(MapTest) + "." + nameof(PlayMain), PlayReport, 40);
    }

    static IEnumerator PlayMain()
    {
        Begin();
        var sw = Stopwatch.StartNew();
        rep.AppendLine($"MapTest.play (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}); play mode entered {PlayModeHarness.EnterSeconds:F1} s after the request");
        RuntimeServices sv = null;
        try { sv = PlayServices(); }
        catch (Exception e) { Section("setup", "services"); Check("catalog and Studio lists", false, e.ToString()); }
        if (sv != null)
        {
            string list = Arg("-maps");
            var maps = list == null ? PlayMaps : list.Split(',', ';', '+').Select(x => x.Trim()).Where(x => x.Length > 0).Select(x => x.Contains("#") ? x : "illusion.hs2#" + x).ToArray();
            Section("setup", "abdata junction (M9)");
            string link = MapAudio.EnsureLink(sv.Hs2Root);
            Info($"junction {MapAudio.LinkPath}: {link} (the Editor creates it only with maps.audioLinkEditor)");
            var census = Census();
            Info($"census rows {census.Count}");
            yield return GuardCo("play", PlayMaps0(sv, maps, census));
            try { sv.Catalog.UnloadAll(); } catch (Exception e) { Info("catalog unload threw: " + e.Message); }
        }
        End("map_play_report.txt", "MAP PLAY TEST", sw);
        PlayModeHarness.ExitCode = fails > 0 ? 1 : 0;
    }

    static RuntimeServices PlayServices()
    {
        var gd = CardResolveTest.BuildDb(null);
        var db = gd.LoadStudioLists(new StudioLoadOptions { EmulateAIInstallGate = false });
        return new RuntimeServices { Catalog = gd, Db = db, Hs2Root = CardResolveTest.GameRoot };
    }

    /// <summary>Runs <paramref name="steps"/>; an exception becomes one FAIL line in the current section and ends it.</summary>
    static IEnumerator GuardCo(string what, IEnumerator steps)
    {
        while (true)
        {
            object cur;
            try
            {
                if (!steps.MoveNext()) yield break;
                cur = steps.Current;
            }
            catch (Exception e) { Check(what + " ran without an exception", false, e.ToString()); yield break; }
            yield return cur;
        }
    }

    // ================================================================ census

    public sealed class CensusRow
    {
        public string Ref, Name, Status;
        public int Lightmaps, Lightmapped, Probes, Lights, Renderers, Roots, StaticBatched;
    }

    public static Dictionary<string, CensusRow> Census()
    {
        var d = new Dictionary<string, CensusRow>(StringComparer.Ordinal);
        string path = Path.Combine(ContentSettings.ProjectRoot, "Tools", "survey", "map_census.tsv");
        if (!File.Exists(path)) return d;
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        var head = lines[0].Split('\t');
        int Col(string n) => Array.IndexOf(head, n);
        int cg = Col("guid"), cn = Col("no"), cs = Col("status"), cl = Col("lightmaps"), cld = Col("lightmapped"), cp = Col("probes"), cli = Col("lights"),
            cr = Col("renderers"), cro = Col("roots"), csb = Col("staticBatched"), cna = Col("name");
        int I(string[] f, int i) => i >= 0 && i < f.Length && int.TryParse(f[i].Split('.')[0], out int v) ? v : 0;
        for (int k = 1; k < lines.Length; k++)
        {
            var f = lines[k].Split('\t');
            if (f.Length < head.Length / 2) continue;
            var row = new CensusRow
            {
                Ref = f[cg] + "#" + f[cn], Name = f[cna], Status = f[cs], Lightmaps = I(f, cl), Lightmapped = I(f, cld), Probes = I(f, cp), Lights = I(f, cli),
                Renderers = I(f, cr), Roots = I(f, cro), StaticBatched = I(f, csb),
            };
            if (!d.ContainsKey(row.Ref) || row.Status == "ok") d[row.Ref] = row;   // duplicates: the ok row
        }
        return d;
    }

    // ================================================================ per-map checks (play and corpus)

    /// <summary>The checks of a loaded map; returns the problems (empty = all good), with numbers in <paramref name="info"/>.</summary>
    public static List<string> MapCheck(MapRealization map, CensusRow census, StringBuilder info)
    {
        var bad = new List<string>();
        var s = map.Structure;
        if (map.State != MapState.Loaded || s == null) { bad.Add("state " + map.State); return bad; }
        // lights
        int lights = 0, noData = 0, notFolded = 0, layersBad = 0;
        foreach (var l in s.Lights)
        {
            if (!l) continue;
            lights++;
            if (!l.TryGetComponent<UniversalAdditionalLightData>(out var d)) { noData++; continue; }
            var o = BundleLights.OriginalOf(l);
            var want = o ? BundleLights.Folded(o.Color, o.Intensity) : Color.clear;
            if (!o || Mathf.Abs(l.intensity - 1f) > 1e-6f || Mathf.Abs(l.color.r - want.r) > 1e-5f * Mathf.Max(1f, want.r) || Mathf.Abs(l.color.g - want.g) > 1e-5f * Mathf.Max(1f, want.g) ||
                Mathf.Abs(l.color.b - want.b) > 1e-5f * Mathf.Max(1f, want.b)) notFolded++;
            if (unchecked((uint)d.renderingLayers) != unchecked((uint)l.cullingMask) || l.renderingLayerMask != l.cullingMask) layersBad++;
        }
        if (noData > 0) bad.Add(noData + " lights without URP data");
        if (notFolded > 0) bad.Add(notFolded + " lights not folded");
        if (layersBad > 0) bad.Add(layersBad + " lights whose rendering layers differ from the culling mask");
        // renderers, terrains, shaders
        int rl = 0, builtIn = 0, slots = 0, fallback = 0;
        var builtInNames = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var r in s.Renderers)
        {
            if (!r) continue;
            if (r.renderingLayerMask != RenderingLayers.ForLayer(r.gameObject.layer)) rl++;
            foreach (var m in r.sharedMaterials)
            {
                if (!m) continue;
                slots++;
                if (map.Materials.IsBuiltIn(m)) { builtIn++; builtInNames.Add(m.shader ? m.shader.name : "(none)"); }
                var o = map.Materials.OriginOf(m);
                if (m.shader && m.shader.name == "Universal Render Pipeline/Lit" && o != null && o.ShaderOriginal != "Universal Render Pipeline/Lit") fallback++;
            }
        }
        foreach (var t in s.Terrains) if (t && t.renderingLayerMask != RenderingLayers.ForLayer(t.gameObject.layer)) rl++;
        if (rl > 0) bad.Add(rl + " renderers / terrains with renderingLayerMask != 1 << layer");
        if (builtIn > 0) bad.Add(builtIn + " slots on a Built-in shader: " + string.Join(", ", builtInNames.Take(5)));
        // lightmaps / probes against the census
        var lm = map.Lightmaps;
        int lightmapped = 0, outOfRange = 0;
        foreach (var r in s.Renderers)
            if (ShaderMap.IsLightmapped(r))
            {
                lightmapped++;
                if (r.lightmapIndex < lm.FirstIndex || r.lightmapIndex >= lm.FirstIndex + lm.SceneLightmaps) outOfRange++;
            }
        if (census != null)
        {
            if (lm.SceneLightmaps != census.Lightmaps) bad.Add($"lightmaps {lm.SceneLightmaps} (census {census.Lightmaps})");
            // the census counts serialized indices; a scene without lightmaps leaves its renderers unlit by lightmaps (HS2 too)
            int expectLm = census.Lightmaps > 0 ? census.Lightmapped : 0;
            if (lightmapped != expectLm) bad.Add($"lightmapped renderers {lightmapped} (census {census.Lightmapped}{(census.Lightmaps == 0 ? " with no lightmap: 0" : "")})");
            if (census.Probes > 0 && !map.ProbesTetrahedralized) bad.Add("probes not tetrahedralized");
            var lp = LightmapSettings.lightProbes;
            if (census.Probes > 0 && (lp == null || lp.count < census.Probes)) bad.Add($"probes {(lp != null ? lp.count : 0)} (census {census.Probes})");
        }
        if (outOfRange > 0) bad.Add(outOfRange + " lightmapped renderers outside the scene's lightmap range");
        // audio, cameras, listeners
        int audio = s.AudioSources.Count(a => a && (a.enabled || a.isPlaying));
        if (audio > 0) bad.Add(audio + " audio sources still enabled or playing");
        int cams = s.Cameras.Count(c => c && c.enabled) + s.Listeners.Count(l => l && l.enabled);
        if (cams > 0) bad.Add(cams + " cameras / listeners enabled");
        // main light
        var want2 = MainLight.Choose(MainLight.CharaLight, s.Lights, out _);
        if (MainLight.Current != want2) bad.Add($"main light {(MainLight.Current ? MainLight.Current.name : "none")} (rule: {(want2 ? want2.name : "none")})");
        // blocks
        string blocks = BlocksState(s.Component, map.OptionFlag, map.LightFlag);
        if (blocks != null) bad.Add(blocks);
        info?.Append($"lights {lights} (folded {lights - notFolded - noData}), renderers {s.Renderers.Count}, slots {slots} (URP/Lit fallback {fallback}), lightmaps {lm.SceneLightmaps} from {lm.FirstIndex} " +
                     $"(lightmapped {lightmapped}, routed {lm.RoutedSlots}), probes {(LightmapSettings.lightProbes != null ? LightmapSettings.lightProbes.count : 0)}, " +
                     $"audio {map.Audio.Sources} (were playing {map.Audio.WerePlaying}), main light {(MainLight.Current ? MainLight.Current.name : "none")} ({MainLight.Reason}), " +
                     $"scripts {map.Scripts.LightCurves}/{map.Scripts.UVScrolls}/{map.Scripts.FountainUVs}, copies {map.Materials.Count}, steps " +
                     string.Join(" ", map.StepMs.Select(kv => kv.Key + " " + kv.Value)));
        return bad;
    }

    /// <summary>Null when every block object is in the state HS2's application order gives for the flags, else a problem.</summary>
    static string BlocksState(Studio.MapComponent c, bool option, bool light)
    {
        if (!c) return null;
        var want = new Dictionary<GameObject, bool>();
        void Kind(List<Studio.MapComponent.OptionInfo_S> infos, bool v)
        {
            if (infos == null) return;
            foreach (var i in infos)
            {
                if (i == null) continue;
                if (i.objectsOff != null) foreach (var g in i.objectsOff) if (g) want[g] = !v;
                if (i.objectsOn != null) foreach (var g in i.objectsOn) if (g) want[g] = v;
            }
        }
        Kind(c.optionInfos, option);
        Kind(c.lightInfos, light);
        int wrong = want.Count(kv => kv.Key && kv.Key.activeSelf != kv.Value);
        return wrong == 0 ? null : $"{wrong} of {want.Count} block objects not in the state of option {option} / light {light}";
    }

    static int fmodLines;
    static void CountFmod(string condition, string stack, LogType type)
    {
        if (condition != null && condition.IndexOf("FMOD", StringComparison.Ordinal) >= 0 && condition.IndexOf("ile not found", StringComparison.Ordinal) >= 0) fmodLines++;
    }

    static int CountOf<T>() where T : Object => Resources.FindObjectsOfTypeAll<T>().Length;

    /// <summary>Textures without Unity's temporary render-texture pool ("TempBuffer N", released frames later by Unity).</summary>
    static int TextureCount() => Resources.FindObjectsOfTypeAll<Texture>().Count(t => !(t is RenderTexture && t.name.StartsWith("TempBuffer", StringComparison.Ordinal)) &&
        !t.name.StartsWith("Unity GI", StringComparison.Ordinal));   // Unity's realtime-GI textures of a GI scene stay with the GI manager

    static Dictionary<string, int> TextureNames()
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in Resources.FindObjectsOfTypeAll<Texture>())
        {
            if (t is RenderTexture && t.name.StartsWith("TempBuffer", StringComparison.Ordinal)) continue;
            string k = t.GetType().Name + ":" + t.name;
            d.TryGetValue(k, out int n); d[k] = n + 1;
        }
        return d;
    }

    /// <summary>"(+name, -name)" of the textures whose counts differ.</summary>
    public static string TextureDiff(Dictionary<string, int> a, Dictionary<string, int> b)
    {
        if (a == null || b == null) return "";
        var l = new List<string>();
        foreach (var k in a.Keys.Union(b.Keys))
        {
            a.TryGetValue(k, out int x); b.TryGetValue(k, out int y);
            if (x != y) l.Add((y > x ? "+" : "-") + k);
        }
        return l.Count == 0 ? "" : "(" + string.Join(", ", l.Take(6)) + ")";
    }

    // ================================================================ the play sequence

    static IEnumerator PlayMaps0(RuntimeServices sv, string[] maps, Dictionary<string, CensusRow> census)
    {
        MainLight.Reset();
        var findings = new SceneFindings();
        var map = new MapRealization(sv.Catalog, sv.Db, findings, sv.Hs2Root);
        var app = SceneManager.GetActiveScene();
        Application.logMessageReceived += CountFmod;
        bool[] ok = { false };
        try
        {
            string prevRef = null;
            for (int k = 0; k < maps.Length; k++)
            {
                string mref = maps[k];
                census.TryGetValue(mref, out var crow);
                Section("map " + mref, crow?.Name ?? "");
                bool chained = prevRef != null;   // V4: the second 2155X map follows the first without a clear
                int mats0 = 0, tex0 = 0;
                Dictionary<string, int> texBefore = null;
                if (!chained)
                {
                    yield return Resources.UnloadUnusedAssets();
                    mats0 = CountOf<Material>(); tex0 = TextureCount();
                    texBefore = TextureNames();
                }
                int f0 = findings.Count, fm0 = fmodLines;
                var sw = Stopwatch.StartNew();
                map.SetFlags(true, true);
                map.SetTarget(mref, Vec3.Zero, Vec3.Zero);
                yield return PlayModeHarness.WaitUntil(() => { map.Apply(); return !map.IsBusy; }, 600, ok);
                yield return PlayModeHarness.WaitFrames(3);   // the sea step one frame later
                map.Apply();
                Info($"{map.State} after {sw.ElapsedMilliseconds} ms (job {map.LoadMs} ms, bundle {map.BundleMs} ms); roots {map.Roots.Count}; findings {string.Join(", ", map.MapFindingsOfLoad.Select(x => x.Code).Distinct())}");
                Check("loaded", ok[0] && map.State == MapState.Loaded, $"{map.State}; {string.Join("; ", map.MapFindingsOfLoad.Where(x => x.Severity != FindingSeverity.Info).Select(x => x.Code + ": " + x.Message).Take(3))}");
                if (map.State != MapState.Loaded) { map.Clear(); yield return PlayModeHarness.WaitUntil(() => map.PendingUnloads == 0, 120, ok); prevRef = null; continue; }
                var info = new StringBuilder();
                var probs = MapCheck(map, crow, info);
                Info(info.ToString());
                Check("map checks (lights folded with layers, renderer layers, no Built-in shader, lightmaps / probes = census, audio / cameras off, main light, blocks)", probs.Count == 0, string.Join("; ", probs));
                Check("no map.stepFailed", !map.MapFindingsOfLoad.Any(x => x.Code == "map.stepFailed"), string.Join("; ", map.MapFindingsOfLoad.Where(x => x.Code == "map.stepFailed").Select(x => x.Message)));
                int fm = fmodLines - fm0;
                bool linked = MapAudio.LinkState == "created" || MapAudio.LinkState == "exists";
                if (linked && mref.StartsWith("illusion.hs2#", StringComparison.Ordinal))
                    Check("FMOD file-not-found lines during the load: 0 with the junction (M9)", fm == 0, $"{fm} lines, clips unavailable {map.Audio.ClipsUnavailable}");
                else Info($"FMOD file-not-found lines {fm}; clips unavailable {map.Audio.ClipsUnavailable} (link {MapAudio.LinkState}; V9 for mods)");
                if (fm > 0) Check("FMOD lines only with unavailable clips reported (map.envAudioUnavailable)", map.Audio.ClipsUnavailable > 0 && map.MapFindingsOfLoad.Any(x => x.Code == "map.envAudioUnavailable"),
                                  $"{fm} lines, {map.Audio.ClipsUnavailable} clips");
                if (chained)
                {
                    var lm = map.Lightmaps;
                    Check("V4: a lightmapped map loaded right after another: its range is the tail of the merged set and every renderer index inside it",
                          crow == null || lm.SceneLightmaps == crow.Lightmaps, $"first {lm.FirstIndex}, scene {lm.SceneLightmaps}, total {lm.LightmapsTotal}, census {crow?.Lightmaps}");
                }

                // flags: light off, option off, back
                var s = map.Structure;
                map.SetFlags(true, false); map.Apply();
                string b1 = BlocksState(s.Component, true, false);
                var m1 = MainLight.Current;
                map.SetFlags(false, true); map.Apply();
                string b2 = BlocksState(s.Component, false, true);
                map.SetFlags(true, true); map.Apply();
                string b3 = BlocksState(s.Component, true, true);
                Check("flags: blocks follow light off / option off / both on", b1 == null && b2 == null && b3 == null, b1 ?? b2 ?? b3);
                Check("main light refreshed after the block changes", MainLight.Current == MainLight.Choose(MainLight.CharaLight, s.Lights, out _),
                      $"light off: {(m1 ? m1.name : "none")}, now {(MainLight.Current ? MainLight.Current.name : "none")}");

                // pose (M1)
                var roots = s.Roots;
                var p0 = roots.Select(r => r.transform.position).ToArray();
                var q0 = roots.Select(r => r.transform.rotation).ToArray();
                map.SetTarget(mref, new Vec3(1, 2, 3), new Vec3(0, 90, 0)); map.Apply();
                var R = Quaternion.Euler(0, 90, 0); var T = new Vector3(1, 2, 3);
                float pe = 0; double re = 0;
                // p0 / q0 were read at the default pose: root 0 is at (0, identity) then; the others at their scene-file pose
                for (int i = 0; i < roots.Length; i++)
                {
                    pe = Mathf.Max(pe, (roots[i].transform.position - (R * p0[i] + T)).magnitude);
                    re = Math.Max(re, RotationCompare.Deg(roots[i].transform.rotation, R * q0[i]));
                }
                bool root0 = roots.Length == 0 || p0[0].magnitude < 1e-4f && RotationCompare.Deg(q0[0], Quaternion.identity) < 1e-3f;
                Check("pose (1,2,3) / (0,90,0): root 0 = (T, R) as HS2 (at the origin at the default pose), every other root at R·p0 + T",
                      root0 && pe < 1e-3f && re < 0.01f, $"{roots.Length} roots, root 0 default {p0.FirstOrDefault()}, position error {pe:E2}, rotation {re:E2}; static-batched {map.StaticBatchedRenderers}");
                map.SetTarget(mref, Vec3.Zero, Vec3.Zero); map.Apply();

                // map scripts: two seconds of steps
                if (map.Scripts.LightCurves + map.Scripts.UVScrolls + map.Scripts.FountainUVs > 0)
                {
                    for (int i = 0; i < 30; i++) map.StepScripts(1f / 15f);
                    var curves = map.Scripts.CurveStates().ToList();
                    bool inRange = curves.All(c => c.light && Mathf.Abs(c.light.intensity - 1f) < 1e-6f);
                    Check("map scripts stepped (curve lights folded at intensity 1)", inRange && map.Scripts.Time > 1.9f, $"{curves.Count} curves, t {map.Scripts.Time:F2}");
                }

                // next: chained (V4) or clear with the leak check
                bool chainNext = k + 1 < maps.Length && mref == "2155x.map.phsubway#0" && maps[k + 1] == "2155x.map.interrogationroom#0";
                if (chainNext) { prevRef = mref; continue; }
                prevRef = null;
                var scene = map.Scene;
                map.SetTarget(null, Vec3.Zero, Vec3.Zero);
                yield return PlayModeHarness.WaitUntil(() => { map.Apply(); return !map.IsBusy && map.PendingUnloads == 0; }, 120, ok);
                yield return Resources.UnloadUnusedAssets();
                int mats1 = CountOf<Material>(), tex1 = TextureCount();
                Check("unload: scene gone, the app scene active", ok[0] && !scene.isLoaded && SceneManager.GetActiveScene() == app, SceneManager.GetActiveScene().name);
                if (!chained)
                    Check("unload: material and texture counts back to the baseline (no leak)", mats1 == mats0 && tex1 == tex0, $"materials {mats0} -> {mats1}, textures {tex0} -> {tex1} {TextureDiff(texBefore, TextureNames())}");
                else Info($"after the chained pair: materials {mats1}, textures {tex1}");
            }
        }
        finally
        {
            Application.logMessageReceived -= CountFmod;
            map.Clear();
        }
        yield return PlayModeHarness.WaitUntil(() => map.PendingUnloads == 0, 60, ok);
        current = "summary";
        Info("findings by code: " + string.Join(", ", findings.GroupBy(f => f.Code).Select(g => g.Key + " x" + g.Count())));
    }

    // ================================================================ M9 measurement

    static string AudioReport => Path.Combine(OutDir, "map_audio_probe.txt");

    public static void RunAudioProbe()
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(AudioReport, "SUMMARY: 0 PASS, 1 FAIL\nMapTest.audio: started\n", new UTF8Encoding(false));
        PlayModeHarness.Start(nameof(MapTest) + "." + nameof(AudioMain), AudioReport, 15);
    }

    static IEnumerator AudioMain()
    {
        Begin();
        var sw = Stopwatch.StartNew();
        Section("M9", "relative clip resource path (V2)");
        var sv = PlayServices();
        string link = MapAudio.LinkPath;
        bool existed = Directory.Exists(link);
        Info($"Application.dataPath {Application.dataPath}; link path {link} (existed before: {existed}); CWD {Environment.CurrentDirectory}");
        bool[] ok = { false };
        string removedNote = "";
        if (existed)
        {
            var di = new DirectoryInfo(link);
            if ((di.Attributes & FileAttributes.ReparsePoint) != 0) { removedNote = MapAudio.RemoveJunction(link) ? "an existing junction removed for the measurement" : "could not remove the existing junction"; }
            else removedNote = "a real folder exists there: the first measurement runs with it";
        }
        Info(removedNote);
        var findings = new SceneFindings();
        var map = new MapRealization(sv.Catalog, sv.Db, findings, null);   // no hs2Root: EnsureLink does nothing
        Application.logMessageReceived += CountFmod;
        // candidates for the base of the relative path "../abdata/...": none, the data folder (Application.dataPath/..), the
        // working directory's parent (Environment.CurrentDirectory/..); each junction is removed again unless it is the one
        // the app uses (dataPath) and it worked
        string target = Path.Combine(CardResolveTest.GameRoot, "abdata");
        var cands = new List<(string name, string link)> { ("no junction", null), ("dataPath/../abdata", Path.GetFullPath(Path.Combine(Application.dataPath, "..", "abdata"))),
            ("CWD/../abdata", Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "abdata"))) };
        var lines = new List<int>();
        bool dataPathWorks = false;
        for (int pass = 0; pass < cands.Count; pass++)
        {
            var (cname, clink) = cands[pass];
            bool made = false;
            if (clink != null)
            {
                string r = MapAudio.CreateJunction(clink, target);
                made = r == "created";
                Info($"pass {pass + 1} {cname}: junction {clink}: {r}");
                if (!made && r != "exists") { lines.Add(-1); continue; }
            }
            int f0 = fmodLines;
            map.SetTarget("illusion.hs2#10", Vec3.Zero, Vec3.Zero);
            yield return PlayModeHarness.WaitUntil(() => { map.Apply(); return !map.IsBusy; }, 300, ok);
            yield return PlayModeHarness.WaitFrames(10);
            int failed = 0, loadedClips = 0, other = 0;
            foreach (var a in map.Structure?.AudioSources ?? new List<AudioSource>())
            {
                if (!a || !a.clip) continue;
                if (a.clip.loadState == AudioDataLoadState.Failed) failed++;
                else if (a.clip.loadState == AudioDataLoadState.Loaded) loadedClips++;
                else other++;
            }
            int n = fmodLines - f0;
            lines.Add(n);
            Info($"pass {pass + 1} ({cname}): state {map.State}, FMOD file-not-found lines {n}, clips failed {failed}, loaded {loadedClips}, other {other}");
            map.SetTarget(null, Vec3.Zero, Vec3.Zero);
            yield return PlayModeHarness.WaitUntil(() => { map.Apply(); return !map.IsBusy && map.PendingUnloads == 0; }, 60, ok);
            sv.Catalog.UnloadAll();
            yield return Resources.UnloadUnusedAssets();
            if (pass == 2 && n == 0 && lines[0] > 0) dataPathWorks = true;   // the Editor's rule: the working directory's parent
            if (made) Info($"   junction removed again: {MapAudio.RemoveJunction(clink)}");
        }
        Application.logMessageReceived -= CountFmod;
        Info("lines per pass: " + string.Join(", ", lines));
        Info(dataPathWorks ? "the Editor resolves the relative path against the working directory (CWD/../abdata), not Application.dataPath/.." : "the CWD rule did not hold either");
        Check("M9 (Editor): the FMOD lines appear without a junction and vanish with the CWD/../abdata junction", dataPathWorks && lines[1] == lines[0], string.Join(" / ", lines));
        End("map_audio_probe.txt", "MAP AUDIO PROBE", sw);
        PlayModeHarness.ExitCode = fails > 0 ? 1 : 0;
    }
}

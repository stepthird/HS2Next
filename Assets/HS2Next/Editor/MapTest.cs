// Batch test of the Studio map behaviour and lighting (package A of Phase 3.2).
// Run: -executeMethod MapTest.Run [-only P1,P3] (headless, -nographics, edit mode): P1–P8 on synthetic GameObjects and the
//      compat stubs (no bundle scene loads).
//      -executeMethod MapTest.RunPlay (play mode through PlayModeHarness, unity_batch -NoQuit; Editor/MapTest_Play.cs)
//      -executeMethod MapTest.RunCorpus [-src vanilla|mods] [-from N] [-limit N] (play mode, every map row; Editor/MapTest_Corpus.cs)
//      -executeMethod MapTest.RunAudioProbe (play mode: the M9 measurement of the relative clip resource path; MapTest_Play.cs)
//   P1  MapBehaviour: on / off lists × flag true / false, null entries, all blocks applied, idempotent; features from lengths
//   P2  MapComponent lookup (M2): first active in roots order, inactive skipped, none
//   P3  MainLight: chara light wins when shadowed; brightest gamma among map lights; ties; none → null sun; refresh after a toggle
//   P4  MapScripts: ME curve values at t = 0, 0.5, 1.9, 2.1 (loop) and the non-loop stop, folding of the value; UVScroll
//       offsets; AnimatedFountainUVs tile sequence (HS2's num / tilesY row term)
//   P5  MapMaterials: one copy per distinct material, mapped, tracked copies destroyed on release (material count back)
//   P6  MapLightmaps: LightmapPrefab entries (null renderer, out of range, valid), routing with one copy per material,
//       Bakery detection on synthetic storages
//   P7  pose rule (M1: root 0 = (T, R) as HS2, the other roots R·p0 + T) on synthetic roots; staticBatchMoved is info
//   P8  finding codes and levels
// Report: Spike/out/maps/map_report.txt, first line "SUMMARY: N PASS, M FAIL"; EditorApplication.Exit(1) on a FAIL.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static partial class MapTest
{
    public static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "maps");

    // ================================================================ report

    static StringBuilder rep;
    static int passes, fails, warns;
    static readonly Dictionary<string, (int pass, int fail, int warn)> perTest = new Dictionary<string, (int, int, int)>();
    static string current = "setup";
    static readonly List<string> exceptions = new List<string>();
    static string[] only;
    static readonly List<Object> made = new List<Object>();

    static void Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  {current} {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
        perTest.TryGetValue(current, out var c);
        perTest[current] = ok ? (c.pass + 1, c.fail, c.warn) : (c.pass, c.fail + 1, c.warn);
    }

    static void Warn(string what)
    {
        rep.AppendLine($"WARN  {current} {what}");
        warns++;
        perTest.TryGetValue(current, out var c);
        perTest[current] = (c.pass, c.fail, c.warn + 1);
    }

    static void Info(string s) => rep.AppendLine("   " + s);
    static void Section(string id, string title) { current = id; rep.AppendLine(); rep.AppendLine("== " + id + " " + title); }
    static bool Want(string id) => only == null || only.Contains(id);

    static void Guard(string id, string title, Action a)
    {
        if (!Want(id)) return;
        Section(id, title);
        int ex0 = exceptions.Count;
        var sw = Stopwatch.StartNew();
        try { a(); }
        catch (Exception e) { current = id; Check("(exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        finally { Cleanup(); }
        if (exceptions.Count > ex0)
        {
            current = id;
            Check("no exception logged", false, $"{exceptions.Count - ex0}: " + string.Join(" | ", exceptions.Skip(ex0).Take(3)));
        }
        Info($"{id}: {sw.Elapsed.TotalSeconds:F1} s");
    }

    static void OnLog(string condition, string stack, LogType type)
    {
        if (type == LogType.Exception) exceptions.Add(condition.Length > 300 ? condition.Substring(0, 300) : condition);
    }

    static string[] OnlyArgs()
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, "-only");
        if (i < 0) return null;
        var l = new List<string>();
        for (int k = i + 1; k < a.Length && !a[k].StartsWith("-", StringComparison.Ordinal); k++)
            l.AddRange(a[k].Split(',', '+', ';').Select(x => x.Trim()).Where(x => x.Length > 0));
        return l.ToArray();
    }

    static string Arg(string name)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    static void Begin()
    {
        rep = new StringBuilder(); passes = fails = warns = 0; perTest.Clear(); exceptions.Clear();
        only = OnlyArgs();
        Directory.CreateDirectory(OutDir);
        Application.logMessageReceived += OnLog;
    }

    static string End(string file, string title, Stopwatch sw)
    {
        Application.logMessageReceived -= OnLog;
        var sum = new StringBuilder();
        sum.AppendLine($"SUMMARY: {passes} PASS, {fails} FAIL ({warns} WARN, {sw.Elapsed.TotalSeconds:F0} s)");
        foreach (var kv in perTest.OrderBy(k => k.Key, StringComparer.Ordinal))
            sum.AppendLine($"  {kv.Key}: {kv.Value.pass} pass, {kv.Value.fail} fail{(kv.Value.warn > 0 ? ", " + kv.Value.warn + " warn" : "")}");
        rep.Insert(0, sum.ToString());
        string path = Path.Combine(OutDir, file);
        File.WriteAllText(path, rep.ToString(), new UTF8Encoding(false));
        Debug.Log($"{title} DONE: {passes} PASS, {fails} FAIL, {warns} WARN");
        return path;
    }

    // ================================================================ synthetic objects

    static GameObject Go(string name, Transform parent = null, bool active = true)
    {
        var g = new GameObject(name);
        if (parent) g.transform.SetParent(parent, false);
        g.SetActive(active);
        if (!parent) made.Add(g);
        return g;
    }

    static T Keep<T>(T o) where T : Object { made.Add(o); return o; }

    static void Cleanup()
    {
        foreach (var o in made) if (o) Object.DestroyImmediate(o);
        made.Clear();
        MainLight.Reset();
    }

    static Studio.MapComponent.OptionInfo_S Block(List<GameObject> on, List<GameObject> off) =>
        new Studio.MapComponent.OptionInfo_S { objectsOn = on, objectsOff = off };

    static Light AddLight(GameObject g, LightType type, Color color, float intensity, LightShadows shadows)
    {
        var l = g.AddComponent<Light>();
        l.type = type;
        l.color = color;
        l.intensity = intensity;
        l.shadows = shadows;
        return l;
    }

    // ================================================================ entry

    public static void Run()
    {
        Begin();
        var sw = Stopwatch.StartNew();
        rep.AppendLine($"map test (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}); edit mode, synthetic objects");
        Guard("P1", "MapBehaviour: option / light blocks", P1);
        Guard("P2", "MapComponent lookup (M2)", P2);
        Guard("P3", "MainLight rule (M4)", P3);
        Guard("P4", "MapScripts: ME_LightCurves, UVScroll, AnimatedFountainUVs", P4);
        Guard("P5", "MapMaterials: owned copies released", P5);
        Guard("P6", "MapLightmaps: LightmapPrefab, routing, Bakery detection", P6);
        Guard("P7", "pose rule (M1), static batch finding", P7);
        Guard("P8", "finding codes and levels", P8);
        End("map_report.txt", "MAP TEST", sw);
        if (fails > 0) TestExit.Exit(1);
    }

    // ================================================================ P1

    static void P1()
    {
        var root = Go("Map");
        var comp = root.AddComponent<Studio.MapComponent>();
        var aOn = Go("aOn", root.transform, false);
        var aOff = Go("aOff", root.transform, true);
        var bOn = Go("bOn", root.transform, false);
        var lOn = Go("lOn", root.transform, false);
        var lOff = Go("lOff", root.transform, true);
        comp.optionInfos = new List<Studio.MapComponent.OptionInfo_S>
        {
            Block(new List<GameObject> { aOn, null }, new List<GameObject> { aOff }),
            Block(new List<GameObject> { bOn }, null),
        };
        comp.lightInfos = new List<Studio.MapComponent.OptionInfo_S> { Block(new List<GameObject> { lOn }, new List<GameObject> { lOff, null }) };
        var b = new MapBehaviour();
        b.Bind(comp);
        Check("features from array lengths: option and light", b.FeatureOption && b.FeatureLight);
        Check("null entries counted (2)", b.NullRefs == 2, "" + b.NullRefs);

        int n = b.Apply(true, true);
        Check("true/true: every on-object active, every off-object inactive (all blocks)", aOn.activeSelf && bOn.activeSelf && !aOff.activeSelf && lOn.activeSelf && !lOff.activeSelf, $"{n} changes");
        Check("true/true from the scene file's state: 5 SetActive calls", n == 5, "" + n);
        Check("idempotent: the same flags again change nothing", b.Apply(true, true) == 0);
        n = b.Apply(false, true);
        Check("option false: option on-lists off, off-lists on; lights untouched", !aOn.activeSelf && !bOn.activeSelf && aOff.activeSelf && lOn.activeSelf && !lOff.activeSelf, $"{n} changes");
        n = b.Apply(false, false);
        Check("light false: light on-list off, off-list on", !lOn.activeSelf && lOff.activeSelf && n == 2, $"{n} changes");
        b.Apply(true, false);
        Check("option back to true with light false", aOn.activeSelf && bOn.activeSelf && !aOff.activeSelf && !lOn.activeSelf && lOff.activeSelf);
        Check("the applied flags are reported", b.OptionOn && !b.LightOn);

        // a single block helper as HS2's Visible setter: off-list first, then on-list (an object in both ends up as the on-list says)
        var both = Go("both", root.transform, false);
        var blk = Block(new List<GameObject> { both }, new List<GameObject> { both });
        MapBehaviour.ApplyBlock(blk, true);
        bool afterTrue = both.activeSelf;
        MapBehaviour.ApplyBlock(blk, false);
        Check("an object on both lists follows the list applied last (HS2's order)", afterTrue && !both.activeSelf);

        var empty = Go("Empty").AddComponent<Studio.MapComponent>();
        empty.optionInfos = new List<Studio.MapComponent.OptionInfo_S>();
        empty.lightInfos = null;
        var b2 = new MapBehaviour();
        b2.Bind(empty);
        Check("no blocks: features false, Apply changes nothing", !b2.FeatureOption && !b2.FeatureLight && b2.Apply(false, false) == 0);
        var b3 = new MapBehaviour();
        b3.Bind(null);
        Check("no component: features false", !b3.FeatureOption && !b3.FeatureLight && b3.Apply(true, true) == 0);

        // sea: USINGWATERVOLUME off on the owned copies of the sea renderers
        var sea = Go("sea", root.transform);
        var mr = sea.AddComponent<MeshRenderer>();
        var m = Keep(new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "seaMat" });
        m.EnableKeyword(MapBehaviour.SeaKeyword);
        mr.sharedMaterial = m;
        var mats = new MapMaterials();
        mats.Apply(new[] { root });
        var own = mr.sharedMaterial;
        b.ApplySea(new List<Renderer> { mr, null }, mats);
        Check("sea: the slot is an owned copy and the keyword is off on it; the bundle material keeps it",
              own != m && mats.IsOwned(own) && !MapBehaviour.HasKeyword(own, MapBehaviour.SeaKeyword) && MapBehaviour.HasKeyword(m, MapBehaviour.SeaKeyword) && b.SeaRenderers == 1 && b.SeaSlots == 1,
              $"renderers {b.SeaRenderers}, slots {b.SeaSlots}");
        mats.Release();
    }

    // ================================================================ P2

    static void P2()
    {
        var r0 = Go("R0");
        var r1 = Go("R1", null, false);
        r1.AddComponent<Studio.MapComponent>();
        var r2 = Go("R2");
        var inactiveChild = Go("child", r2.transform, false);
        inactiveChild.AddComponent<Studio.MapComponent>();
        var r3 = Go("R3");
        var deep = Go("a", Go("b", r3.transform).transform);
        var c3 = deep.AddComponent<Studio.MapComponent>();
        var r4 = Go("R4");
        r4.AddComponent<Studio.MapComponent>();
        var found = MapStructure.FindComponent(new[] { r0, r1, r2, r3, r4 }, out int ri);
        Check("the first active component in roots order (inactive root and inactive child skipped)", found == c3 && ri == 3, $"{(found ? found.name : "none")} root {ri}");
        var none = MapStructure.FindComponent(new[] { r0, r1, r2 }, out int rn);
        Check("only inactive ones: none", none == null && rn == -1);
        var s = MapStructure.Build(new[] { r0, r1, r2, r3, r4 });
        Check("MapStructure takes the same component and lists all four", s.Component == c3 && s.ComponentRoot == 3 && s.AllComponents.Count == 4, $"{s.AllComponents.Count}");
        var first = MapStructure.FindComponent(new[] { r4, r3 }, out int rf);
        Check("root 0 wins when it has one (HS2's case)", first && rf == 0);

        // paths: duplicate sibling names (roots too) get [k] on every occurrence
        var m0 = Go("Map");
        var m1 = Go("Map");
        Go("x", m0.transform); Go("x", m0.transform); Go("y", m0.transform);
        var paths = new List<string>();
        MapStructure.Walk(new[] { m0, m1 }, (t, p) => paths.Add(p));
        string got = string.Join(", ", paths);
        Check("paths: pre-order, [k] on every same-named sibling", got == "Map[0], Map[0]/x[0], Map[0]/x[1], Map[0]/y, Map[1]", got);
    }

    // ================================================================ P3

    static void P3()
    {
        MainLight.Reset();
        var chara = AddLight(Go("CharaLight"), LightType.Directional, Color.white, 1f, LightShadows.Soft);
        var a = AddLight(Go("A"), LightType.Directional, new Color(1f, 0.5f, 0.5f), 0.8f, LightShadows.Soft);   // 0.8
        var b = AddLight(Go("B"), LightType.Directional, new Color(0.5f, 0.5f, 1f), 2f, LightShadows.Hard);    // 2.0 (folded below)
        var c = AddLight(Go("C"), LightType.Directional, Color.white, 3f, LightShadows.None);                   // not shadowed
        var p = AddLight(Go("P"), LightType.Point, Color.white, 9f, LightShadows.Soft);                         // not directional
        BundleLights.Prepare(b.gameObject, fold: true);   // colour (1,1,2)·…, intensity 1; the original 2 decides
        var map = new List<Light> { a, b, c, p };
        MainLight.SetSource("mapOwner", "map", () => map);
        MainLight.SetCharaLight(chara);
        Check("the chara light wins when it is shadowed", MainLight.Current == chara && MainLight.Reason == MainLight.ReasonChara && RenderSettings.sun == chara);
        chara.shadows = LightShadows.None;
        MainLight.Refresh();
        Check("else the brightest shadowed directional light by original gamma max(rgb) x intensity (folded B: 1 x 2)", MainLight.Current == b && MainLight.Reason == MainLight.ReasonBrightest && RenderSettings.sun == b,
              $"{(MainLight.Current ? MainLight.Current.name : "none")}, B brightness {MainLight.Brightness(b)}");
        b.gameObject.SetActive(false);
        MainLight.Refresh();
        Check("refresh after a toggle: B inactive → A", MainLight.Current == a);
        var a2 = AddLight(Go("A2"), LightType.Directional, new Color(0.5f, 1f, 0.5f), 0.8f, LightShadows.Soft);   // ties with A
        map.Add(a2);
        var studio = AddLight(Go("S"), LightType.Directional, new Color(1f, 0.5f, 0.5f), 0.8f, LightShadows.Soft);
        MainLight.SetSource("studioOwner", "studio", () => new[] { studio });
        MainLight.Refresh();
        Check("ties: map lights before Studio lights, then the lower order", MainLight.Current == a);
        // registration order does not matter: a studio source registered first still ranks after the map
        MainLight.Reset();
        MainLight.SetSource("studioOwner", "studio", () => new[] { studio });
        MainLight.SetSource("mapOwner", "map", () => map);
        MainLight.Refresh();
        Check("map before studio whatever the registration order", MainLight.Current == a);
        a.enabled = false; a2.enabled = false; studio.enabled = false;
        MainLight.Refresh();
        // RenderSettings.sun = null: Unity's getter then reports its own pick (the brightest directional light), never a shadowed candidate here
        Check("no candidate → none, RenderSettings.sun cleared (Unity falls back to its brightest directional light, unshadowed)",
              MainLight.Current == null && MainLight.Reason == MainLight.ReasonNone && (RenderSettings.sun == null || !MainLight.IsCandidate(RenderSettings.sun)),
              $"current {(MainLight.Current ? MainLight.Current.name : "none")} ({MainLight.Reason}), sun {(RenderSettings.sun ? RenderSettings.sun.name : "null")}");
        var pure = MainLight.Choose(null, new[] { c, p }, out var why);
        Check("Choose: unshadowed and non-directional lights are no candidates", pure == null && why == MainLight.ReasonNone);
    }

    // ================================================================ P4

    static void P4()
    {
        var root = Go("Map");
        var lg = Go("curve", root.transform);
        var l = AddLight(lg, LightType.Point, new Color(1f, 0.5f, 0.25f), 2f, LightShadows.None);
        var me = lg.AddComponent<ME_LightCurves>();
        me.LightCurve = AnimationCurve.Linear(0f, 0.2f, 1f, 1f);
        me.GraphTimeMultiplier = 2f;
        me.GraphIntensityMultiplier = 3f;
        me.IsLoop = 1;
        var lg2 = Go("curveOnce", root.transform);
        var l2 = AddLight(lg2, LightType.Point, Color.white, 1f, LightShadows.None);
        var me2 = lg2.AddComponent<ME_LightCurves>();
        me2.LightCurve = AnimationCurve.Linear(0f, 0.2f, 1f, 1f);
        me2.GraphTimeMultiplier = 2f;
        me2.GraphIntensityMultiplier = 5f;
        me2.IsLoop = 0;
        // a curve light under an inactive object keeps its saved intensity until the object becomes active (HS2: no Awake / Update)
        var lg3 = Go("curveInactive", Go("offParent", root.transform, false).transform);
        var l3 = AddLight(lg3, LightType.Point, Color.white, 0.7f, LightShadows.None);
        var me3 = lg3.AddComponent<ME_LightCurves>();
        me3.LightCurve = AnimationCurve.Linear(0f, 0.5f, 1f, 1f);
        me3.GraphTimeMultiplier = 2f;
        me3.GraphIntensityMultiplier = 3f;
        me3.IsLoop = 1;
        BundleLights.Prepare(root, fold: true);

        // UVScroll on a renderer whose material is shared with another renderer (the owned copy scrolls both, Q-k)
        var sg = Go("scroll", root.transform);
        var sr = sg.AddComponent<MeshRenderer>();
        var other = Go("sameMat", root.transform).AddComponent<MeshRenderer>();
        var shared = Keep(new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "scrollMat" });
        sr.sharedMaterial = shared;
        other.sharedMaterial = shared;
        var us = sg.AddComponent<UVScroll>();
        us.scrollSpeedX = 0.5f;
        us.scrollSpeedY = -0.3f;
        // AnimatedFountainUVs 4 x 2 at 10 fps
        var fg = Go("fountain", root.transform);
        var fr = fg.AddComponent<MeshRenderer>();
        fr.sharedMaterial = shared;
        var af = fg.AddComponent<AnimatedFountainUVs>();
        af._uvTieX = 4; af._uvTieY = 2; af._fps = 10;

        var mats = new MapMaterials();
        mats.Apply(new[] { root });
        var s = MapStructure.Build(new[] { root });
        var scripts = new MapScripts();
        scripts.Start(s, mats);
        var o3 = BundleLights.OriginalOf(l3);
        Check("an inactive curve light keeps its saved intensity at the start (0.7, folded)", Mathf.Abs(o3.Intensity - 0.7f) < 1e-6f && Mathf.Abs(l3.color.r - 0.7f) < 1e-6f,
              $"{o3.Intensity} / {l3.color}");
        Check("captured 3 curves, 1 scroll, 1 fountain", scripts.LightCurves == 3 && scripts.UVScrolls == 1 && scripts.FountainUVs == 1 && scripts.Skipped == 0,
              $"{scripts.LightCurves}/{scripts.UVScrolls}/{scripts.FountainUVs}, skipped {scripts.Skipped}");
        float V(float t) => me.LightCurve.Evaluate(t / 2f) * 3f;
        var o = BundleLights.OriginalOf(l);
        bool Folded(Light x, BundleLightOriginal ox, float v, Color c0) =>
            Mathf.Abs(x.intensity - 1f) < 1e-6f && Mathf.Abs(ox.Intensity - v) < 1e-5f && (x.color - BundleLights.Folded(c0, v)).maxColorComponent < 1e-5f &&
            (BundleLights.Folded(c0, v) - x.color).maxColorComponent < 1e-5f;
        var c0 = new Color(1f, 0.5f, 0.25f, 1f);
        Check("t = 0: curve(0) × 3, folded (colour = original × v, intensity 1, original intensity = v)", Folded(l, o, V(0f), c0), $"{l.color} {l.intensity} orig {o.Intensity}");
        for (int i = 0; i < 5; i++) scripts.Step(0.1f);
        Check("t = 0.5: curve(0.25) × 3", Folded(l, o, V(0.5f), c0), $"orig {o.Intensity} expect {V(0.5f)}");
        for (int i = 0; i < 14; i++) scripts.Step(0.1f);
        Check("t = 1.9: curve(0.95) × 3", Folded(l, o, V(1.9f), c0), $"orig {o.Intensity} expect {V(1.9f)}");
        scripts.Step(0.1f);   // t' = 2.0: evaluated at 2.0, then restarted (HS2: startTime = Time.time)
        float at2 = o.Intensity;
        scripts.Step(0.1f);   // t' = 0.1 after the restart
        Check("loop: at 2.0 curve(1) × 3, at 2.1 the curve restarted (curve(0.05) × 3)", Mathf.Abs(at2 - V(2f)) < 1e-4f && Mathf.Abs(o.Intensity - V(0.1f)) < 1e-4f,
              $"{at2} / {o.Intensity}");
        var o2 = BundleLights.OriginalOf(l2);
        Check("non-loop: stopped at the end value curve(1) × 5", Mathf.Abs(o2.Intensity - me2.LightCurve.Evaluate(1f) * 5f) < 1e-4f, "" + o2.Intensity);
        scripts.Step(0.1f);
        Check("… and while it stays inactive", Mathf.Abs(o3.Intensity - 0.7f) < 1e-6f, "" + o3.Intensity);
        lg3.transform.parent.gameObject.SetActive(true);
        scripts.Step(0.1f);
        Check("activated: the curve starts (curve(0) x 3)", Mathf.Abs(o3.Intensity - me3.LightCurve.Evaluate(0f) * 3f) < 1e-5f, "" + o3.Intensity);
        lg3.transform.parent.gameObject.SetActive(false);
        lg.SetActive(false);
        scripts.Step(0.1f);
        float frozen = o.Intensity;
        scripts.Step(0.1f);
        lg.SetActive(true);
        scripts.Step(0.1f);
        Check("an inactive light does not step; re-activation restarts its clock (OnEnable)", Mathf.Abs(frozen - o.Intensity) > 0 && Mathf.Abs(o.Intensity - V(0f)) < 1e-4f, $"{frozen} -> {o.Intensity}");

        // UVScroll at map time t
        var copy = sr.sharedMaterial;
        float t = scripts.Time;
        var expect = MapScripts.ScrollOffset(t, 0.5f, -0.3f);
        Check("UVScroll: _MainTex offset = (repeat(t × 0.5, 1), repeat(t × −0.3, 1)) on the owned copy", (copy.GetTextureOffset("_MainTex") - expect).magnitude < 1e-5f && mats.IsOwned(copy),
              $"t {t}: {copy.GetTextureOffset("_MainTex")} expect {expect}");
        Check("UVScroll: the fallback's _BaseMap gets the same offset", (copy.GetTextureOffset("_BaseMap") - expect).magnitude < 1e-5f);
        Check("UVScroll writes the shared copy: the other renderer of the material scrolls too (Q-k)", other.sharedMaterial == copy);
        Check("UVScroll formula at t = 0.75: (0.375, 0.775)", (MapScripts.ScrollOffset(0.75f, 0.5f, -0.3f) - new Vector2(0.375f, 0.775f)).magnitude < 1e-5f);

        // AnimatedFountainUVs: renderer-owned copy, HS2's formula (row = i / tilesY)
        var fm = fr.sharedMaterial;
        Check("fountain: a renderer-owned copy distinct from the shared copy", fm != copy && mats.IsOwned(fm));
        var seq = new List<int>();
        for (int i = 0; i < 10; i++) seq.Add(MapScripts.FountainTile(i * 0.1f + 0.05f, 10, 4, 2));
        Check("tile sequence at 10 fps, 4 x 2: 0..7 then 0", string.Join(",", seq) == "0,1,2,3,4,5,6,7,0,1", string.Join(",", seq));
        var (off5, sc5) = MapScripts.FountainUv(5, 4, 2);
        Check("tile 5 of 4 x 2: offset (0.25, 1 − 0.5 − ⌊5/2⌋ × 0.5 = −0.5), scale (0.25, 0.5)", (off5 - new Vector2(0.25f, -0.5f)).magnitude < 1e-6f && (sc5 - new Vector2(0.25f, 0.5f)).magnitude < 1e-6f, $"{off5} {sc5}");
        int ti = MapScripts.FountainTile(scripts.Time, 10, 4, 2);
        var (offT, scT) = MapScripts.FountainUv(ti, 4, 2);
        Check("fountain material at the current time", (fm.GetTextureOffset("_MainTex") - offT).magnitude < 1e-6f && (fm.GetTextureScale("_MainTex") - scT).magnitude < 1e-6f,
              $"tile {ti}: {fm.GetTextureOffset("_MainTex")}");
        scripts.Stop();
        mats.Release();
    }

    // ================================================================ P5

    static int MaterialCount() => Resources.FindObjectsOfTypeAll<Material>().Length;

    static void P5()
    {
        var std = Shader.Find("Standard");
        var bundleA = Keep(new Material(std) { name = "bundleA" });
        var bundleB = Keep(new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "bundleB" });
        var root = Go("Map");
        var r1 = Go("r1", root.transform).AddComponent<MeshRenderer>();
        var r2 = Go("r2", root.transform, false).AddComponent<MeshRenderer>();
        var r3 = Go("r3", Go("Second").transform).AddComponent<MeshRenderer>();
        r1.sharedMaterials = new[] { bundleA, bundleB };
        r2.sharedMaterial = bundleA;
        r3.sharedMaterial = bundleA;
        int baseline = MaterialCount();
        var mats = new MapMaterials();
        int mapped = mats.Apply(new[] { root, r3.transform.parent.gameObject });
        Check("one copy per distinct material across roots (inactive included)", mats.Copied == 2 && r1.sharedMaterials[0] == r2.sharedMaterial && r2.sharedMaterial == r3.sharedMaterial && r1.sharedMaterials[0] != bundleA,
              $"copied {mats.Copied}");
        Check("the bundle materials are untouched", bundleA.shader == std && r1.sharedMaterials[0].shader.name == "HS2Next/Standard", r1.sharedMaterials[0].shader.name);
        Check("Standard mapped to HS2Next/Standard; a URP material kept (1 mapped)", mapped == 1, "" + mapped);
        var o = mats.OriginOf(r1.sharedMaterials[0]);
        Check("the copy remembers the bundle's shader name", o != null && o.ShaderOriginal == "Standard" && o.Source == bundleA);
        Check("no Built-in shader left", !mats.IsBuiltIn(r1.sharedMaterials[0]) && !mats.IsBuiltIn(r1.sharedMaterials[1]));
        Check("renderers got their rendering layers (1 << layer)", r1.renderingLayerMask == RenderingLayers.ForLayer(r1.gameObject.layer));
        var rt = mats.Track(new RenderTexture(16, 16, 0));
        var extra = mats.Copy(bundleB);
        Check("tracked: 2 copies + an extra copy + a render texture", mats.Count == 4 && mats.IsOwned(extra) && mats.IsOwned(rt), "" + mats.Count);
        Check("material count grew by 3", MaterialCount() == baseline + 3, $"{baseline} -> {MaterialCount()}");
        mats.Release();
        Check("release: every tracked object destroyed, material count back to the baseline", MaterialCount() == baseline && !rt && !extra && mats.Count == 0,
              $"{baseline} -> {MaterialCount()}");
    }

    // ================================================================ P6

    static void P6()
    {
        var saved = LightmapSettings.lightmaps;
        try
        {
            var t0 = Keep(new Texture2D(4, 4) { name = "lm0" });
            var t1 = Keep(new Texture2D(4, 4) { name = "lm1" });
            LightmapSettings.lightmaps = new[] { new LightmapData { lightmapColor = t0 }, new LightmapData { lightmapColor = t1 } };
            var root = Go("Map");
            var target = Go("target", root.transform).AddComponent<MeshRenderer>();
            var lp = root.AddComponent<LightmapPrefab>();
            lp.lightmapParameters = new List<LightmapPrefab.LightmapParameter_S>
            {
                new LightmapPrefab.LightmapParameter_S { renderer = null, lightmapIndex = 0, scaleOffset = Vector4.one },
                new LightmapPrefab.LightmapParameter_S { renderer = target, lightmapIndex = 2, scaleOffset = Vector4.one },
                new LightmapPrefab.LightmapParameter_S { renderer = target, lightmapIndex = 1, scaleOffset = new Vector4(0.5f, 0.25f, 0.1f, 0.2f) },
            };
            var inactive = Go("off", root.transform, false).AddComponent<LightmapPrefab>();
            inactive.lightmapParameters = new List<LightmapPrefab.LightmapParameter_S> { new LightmapPrefab.LightmapParameter_S { renderer = target, lightmapIndex = 0 } };
            // routing: two lightmapped renderers sharing a material whose replacement is lit without a lightmap path get one
            // URP/Lit copy. The original must declare LIGHTMAP_ON (Phase 3.7 L: other originals keep their replacement) and the
            // legacy family has a lightmap path since 3.7: the first built-in original of ShaderMap.Fitted that fits.
            var pick = ShaderMap.Fitted.Where(kv => !kv.Key.StartsWith("HS2Next/", StringComparison.Ordinal))
                .Select(kv => (name: kv.Key, o: Shader.Find(kv.Key), t: Shader.Find(kv.Value)))
                .FirstOrDefault(x => x.o && x.t && x.o != x.t && x.o.keywordSpace.FindKeyword("LIGHTMAP_ON").isValid
                                     && !ShaderMap.SupportsLightmaps(x.t) && !ShaderMap.NeedsNoLightmap(x.t));
            Check("an original with LIGHTMAP_ON whose replacement is lit without a lightmap path exists", pick.o, pick.name ?? "none");
            var legacy = pick.o ? pick.o : Shader.Find("Universal Render Pipeline/Lit");
            var lm1 = Go("lm1", root.transform).AddComponent<MeshRenderer>();
            var lm2 = Go("lm2", root.transform).AddComponent<MeshRenderer>();
            var leg = Keep(new Material(legacy) { name = "legacy" });
            lm1.sharedMaterial = leg; lm2.sharedMaterial = leg;
            // Bakery storages: one whose maps are in LightmapSettings, one whose maps are not, one with RNM data
            var inSet = root.AddComponent<ftLightmapsStorage>();
            inSet.maps = new List<Texture2D> { t0 };
            var only = Go("bakeryOnly", root.transform).AddComponent<ftLightmapsStorage>();
            only.maps = new List<Texture2D> { Keep(new Texture2D(4, 4) { name = "storageOnly" }) };
            var rnm = Go("rnm", root.transform).AddComponent<ftLightmapsStorage>();
            rnm.rnmMaps0 = new List<Texture2D> { t1 };
            rnm.mapsMode = new List<int> { 3 };

            var mats = new MapMaterials();
            mats.Apply(new[] { root });
            lm1.lightmapIndex = 0; lm2.lightmapIndex = 1;
            var s = MapStructure.Build(new[] { root });
            var r = MapLightmaps.Apply(s, mats, 0);
            Check("scene lightmaps = 2 from index 0", r.SceneLightmaps == 2 && r.FirstIndex == 0 && r.LightmapsTotal == 2);
            Check("LightmapPrefab: 4 entries, the valid one applied, null renderer and out-of-range counted dead, the inactive component skipped",
                  r.PrefabComponents == 2 && r.PrefabEntries == 4 && r.PrefabApplied == 1 && r.PrefabDead == 2, $"{r.PrefabComponents}/{r.PrefabEntries}/{r.PrefabApplied}/{r.PrefabDead}");
            Check("the valid entry set lightmapIndex 1 and its scale-offset", target.lightmapIndex == 1 && (target.lightmapScaleOffset - new Vector4(0.5f, 0.25f, 0.1f, 0.2f)).magnitude < 1e-6f,
                  $"{target.lightmapIndex} {target.lightmapScaleOffset}");
            var c1 = lm1.sharedMaterial; var c2 = lm2.sharedMaterial;
            Check("routing: lightmapped slots without a lightmap path → one URP/Lit copy shared by both renderers, tracked",
                  c1 == c2 && c1.shader.name == "Universal Render Pipeline/Lit" && mats.IsOwned(c1) && mats.OriginOf(c1)?.ShaderOriginal == pick.name && r.RoutedSlots == 2,
                  $"{pick.name}: {c1.shader.name} / {c2.shader.name}, routed {r.RoutedSlots}, lightmapped {r.LightmappedRenderers}");
            Check("Bakery: 3 storages, 1 map only in the storage, 1 with RNM / mode 3", r.BakeryStorages == 3 && r.BakeryOnlyMaps == 1 && r.BakeryModeStorages == 1,
                  $"{r.BakeryStorages}/{r.BakeryOnlyMaps}/{r.BakeryModeStorages}");
            lm1.lightmapIndex = lm2.lightmapIndex = target.lightmapIndex = -1;
            var r2 = MapLightmaps.Apply(s, null, 2);
            Check("lightmapsBefore = total: the scene has no lightmaps of its own, every prefab entry dead", r2.SceneLightmaps == 0 && r2.PrefabApplied == 0 && r2.PrefabDead == 3,
                  $"{r2.SceneLightmaps} {r2.PrefabApplied} {r2.PrefabDead}");
            mats.Release();
        }
        finally { LightmapSettings.lightmaps = saved; }
    }

    // ================================================================ P7

    static void P7()
    {
        var a = Go("A"); a.transform.SetPositionAndRotation(new Vector3(1, 0, 0), Quaternion.Euler(0, 30, 0));
        var b = Go("B"); b.transform.SetPositionAndRotation(new Vector3(0, 2, 5), Quaternion.identity);
        var roots = new[] { a, b };
        var p0 = new[] { a.transform.localPosition, b.transform.localPosition };
        var r0 = new[] { a.transform.localRotation, b.transform.localRotation };
        var pos = new Vector3(1, 2, 3); var rot = new Vector3(0, 90, 0);
        MapRealization.PoseRoots(roots, p0, r0, pos, rot);
        var q = Quaternion.Euler(rot);
        Check("root 0 = (T, R) as HS2 (its serialized pose discarded)", (a.transform.position - pos).magnitude < 1e-4f && RotationCompare.Deg(a.transform.rotation, q) < 1e-3f,
              $"A {a.transform.position} {a.transform.eulerAngles}");
        Check("root 1 at R·p0 + T with R·r0 (M1: the other roots follow the map)", (b.transform.position - (q * p0[1] + pos)).magnitude < 1e-4f && RotationCompare.Deg(b.transform.rotation, q * r0[1]) < 1e-3f,
              $"B {b.transform.position}");
        MapRealization.PoseRoots(roots, p0, r0, Vector3.zero, Vector3.zero);
        Check("default pose: root 0 at the origin, unrotated (HS2), root 1 at its scene-file pose",
              a.transform.position.magnitude < 1e-5f && RotationCompare.Deg(a.transform.rotation, Quaternion.identity) < 1e-3f && (b.transform.position - p0[1]).magnitude < 1e-5f && RotationCompare.Deg(b.transform.rotation, r0[1]) < 1e-3f);
        Check("map.staticBatchMoved is info (was warning)", MapFindings.Level("map.staticBatchMoved") == FindingSeverity.Info);
    }

    // ================================================================ P8

    static void P8()
    {
        var spec = new Dictionary<string, FindingSeverity>
        {
            ["map.stepFailed"] = FindingSeverity.Error, ["map.componentOutsideFirstRoot"] = FindingSeverity.Info, ["map.blockNullRefs"] = FindingSeverity.Info,
            ["map.unshadowedDirectional"] = FindingSeverity.Info, ["map.manyShadowedLights"] = FindingSeverity.Info, ["map.lightmapPrefabDead"] = FindingSeverity.Info,
            ["map.bakeryOnlyLightmaps"] = FindingSeverity.Warning, ["map.bakeryMode"] = FindingSeverity.Info, ["map.probesTimeout"] = FindingSeverity.Warning,
            ["map.scriptsInert"] = FindingSeverity.Info, ["map.envAudioUnavailable"] = FindingSeverity.Info, ["map.ppv2Ignored"] = FindingSeverity.Info,
            ["map.missingScripts"] = FindingSeverity.Info, ["map.realtimeProbes"] = FindingSeverity.Info, ["map.largeLightmaps"] = FindingSeverity.Warning,
            ["map.vram"] = FindingSeverity.Info, ["map.staticBatchMoved"] = FindingSeverity.Info,
        };
        var bad = spec.Where(kv => !MapFindings.Levels.TryGetValue(kv.Key, out var l) || l != kv.Value).Select(kv => kv.Key).ToList();
        Check("every finding code with its level in MapFindings", bad.Count == 0, string.Join(", ", bad));
        var f = new SceneFindings();
        MapFindings.Add(f, "map.bakeryOnlyLightmaps", "x");
        MapFindings.Add(f, "map.stepFailed", "y");
        Check("MapFindings.Add uses the table's level", f.Count == 2 && f[0].Severity == FindingSeverity.Warning && f[1].Severity == FindingSeverity.Error);
        // the structure's inert-script and missing-script counts (compat stubs that do not run)
        var root = Go("Map");
        root.AddComponent<Map.MapEnvSetting>();
        root.AddComponent<ME_LightCurves>();
        var s = MapStructure.Build(new[] { root });
        Check("inert scripts by class (MapEnvSetting), reproduced ones not listed, every MonoBehaviour in Scripts",
              s.InertScripts.ContainsKey("Map.MapEnvSetting") && !s.InertScripts.ContainsKey("ME_LightCurves") && s.Scripts.ContainsKey("ME_LightCurves") && s.Scripts.ContainsKey("Map.MapEnvSetting"),
              string.Join(", ", s.InertScripts.Keys) + " | " + string.Join(", ", s.Scripts.Keys));
    }
}

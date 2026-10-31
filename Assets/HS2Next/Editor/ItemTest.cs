// Batch test of the Studio item appearance (M1–M11; package A of Phase 3.1).
// Run: -executeMethod ItemTest.Run [-only M1,M3] (headless, -nographics, edit mode, ManualStepping).
//      -executeMethod ItemTest.RunCorpus [-limit N] [-src vanilla|mods] (M9 over every row, -nographics; Editor/ItemTest_Data.cs)
//      -executeMethod ItemTest.RunRender (graphics; Editor/ItemTest_Render.cs)
// Targets: vanilla ordinary props by number (89 bed, 197 tent, 590 sphere (alpha), 185 pendant light, 842 square (glass),
// 975 picture board (panel), 548 dust (particle component), 121 bonfire (option, light, particles), 557 tropical sea,
// 957 rotation gimmick (icon), 935 rotation (child root), 821 portable lamp (cookie light), 976 bird song (SE), 399
// sphere); mods from package C's expectation file (meta.targets: glass-only, null renderer entry, one per fitted Studio
// shader) and the AiLegend "Fire" prop (a Play On Awake AudioSource). M2, M5 and M9 read the expectation file
// Cache/items/expect_<install hash>.json.gz (Tools/survey/item_expect.py); without it they SKIP with a WARN.
// Report: Spike/out/items/item_report.txt, first line "SUMMARY: N PASS, M FAIL"; EditorApplication.Exit(1) on a FAIL.
// Numeric checks only; no image is decoded or viewed.
using System;
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
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static partial class ItemTest
{
    public const float Dt = 1f / 60f;
    public static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "items");

    // ================================================================ report

    static StringBuilder rep;
    static int passes, fails, warns;
    static readonly Dictionary<string, (int pass, int fail, int warn)> perTest = new Dictionary<string, (int, int, int)>();
    static string current = "setup";
    static readonly List<string> exceptions = new List<string>();
    static string[] only;

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

    /// <summary>The ids after -only ("M1,M3"; unity_batch.ps1 splits on commas, so also separate arguments up to the next flag), or null.</summary>
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

    static void End(string file, string title, Stopwatch sw)
    {
        Application.logMessageReceived -= OnLog;
        var sum = new StringBuilder();
        sum.AppendLine($"SUMMARY: {passes} PASS, {fails} FAIL ({warns} WARN, {sw.Elapsed.TotalSeconds:F0} s)");
        foreach (var kv in perTest.OrderBy(k => k.Key, StringComparer.Ordinal))
            sum.AppendLine($"  {kv.Key}: {kv.Value.pass} pass, {kv.Value.fail} fail{(kv.Value.warn > 0 ? ", " + kv.Value.warn + " warn" : "")}");
        rep.Insert(0, sum.ToString());
        File.WriteAllText(Path.Combine(OutDir, file), rep.ToString(), new UTF8Encoding(false));
        Debug.Log($"{title} DONE: {passes} PASS, {fails} FAIL, {warns} WARN");
    }

    // ================================================================ fixture

    /// <summary>A session and a runtime (no drivers, no standard parts unless asked) on shared services.</summary>
    public sealed class Scene : IDisposable
    {
        public RuntimeServices Services;
        public SceneSession Session;
        public SceneRuntime Runtime;

        public Scene(RuntimeServices services, bool standardParts = false, ulong seed = 0x17e3517e3UL)
        {
            Services = services;
            Session = SceneRuntimeTest.NewSession(services, seed);
            Runtime = new SceneRuntime(Session, services, null, false);
            if (standardParts) Runtime.AddStandardParts();
        }

        /// <summary>Adds a vanilla item by number (new-add defaults); null with a FAIL line on failure.</summary>
        public string Add(int no, string parent = null) => Add(ContentId.Vanilla(no), parent);

        public string Add(ContentId id, string parent = null)
        {
            var r = Runtime.Ops.AddItem(id, parent);
            Runtime.Apply();
            if (r == null || r.Status == CommandStatus.Error) { Check("add item " + id, false, r?.ToString() ?? "null"); return null; }
            return r.NewId;
        }

        public ItemRealization Item(string id) => id == null ? null : Runtime.FindItem(id);
        public ItemNodeState State(string id) => Session.Model.Find(id)?.State as ItemNodeState;

        public CommandResult Set(string id, string path, object value)
        {
            var r = Session.Set(id, path, value);
            Runtime.Apply();
            return r;
        }

        public void Dispose()
        {
            Runtime?.Dispose();
            Runtime = null;
        }
    }

    static RuntimeServices services;

    static RuntimeServices Services(StringBuilder head)
    {
        if (services != null) return services;
        services = SceneRuntimeTest.NewServices(head);
        services.UserData = Path.Combine(OutDir, "userdata");
        Directory.CreateDirectory(Path.Combine(services.UserData, "pattern"));
        Directory.CreateDirectory(Path.Combine(services.UserData, "bg"));
        return services;
    }

    /// <summary>The Studio property names a field of the item section writes (C's `writes` keys), with the test value.</summary>
    public static readonly string[] Fields =
    {
        "colors[0].main", "colors[0].metallic", "colors[0].glossiness", "colors[0].pattern.color", "colors[0].pattern.uv", "colors[0].pattern.rot", "colors[0].pattern.clamp",
        "colors[1].main", "colors[1].metallic", "colors[1].glossiness", "colors[1].pattern.color", "colors[1].pattern.uv", "colors[1].pattern.rot", "colors[1].pattern.clamp",
        "colors[2].main", "colors[2].metallic", "colors[2].glossiness", "colors[2].pattern.color", "colors[2].pattern.uv", "colors[2].pattern.rot", "colors[2].pattern.clamp",
        "colors[3].main", "alpha", "emission.color", "emission.power", "lightCancel",
    };

    /// <summary>The test value of a field in its model type (Color4 / float / Vec4 / bool).</summary>
    public static object TestValue(string field)
    {
        int k = field.StartsWith("colors[") ? field[7] - '0' : -1;
        switch (field.Substring(k >= 0 ? 10 : 0))
        {
            case "main": return k == 3 ? new Color4(0.2f, 0.9f, 0.6f, 0.5f) : new Color4(0.1f + 0.2f * k, 0.3f, 0.7f - 0.1f * k, 1f);
            case "metallic": return 0.11f + 0.1f * k;
            case "glossiness": return 0.66f - 0.1f * k;
            case "pattern.color": return new Color4(0.9f, 0.2f + 0.1f * k, 0.4f, 0.8f);
            case "pattern.uv": return new Vec4(2f + k, 3f, 0.25f, -0.5f);
            case "pattern.rot": return 0.3f + 0.1f * k;
            case "pattern.clamp": return false;
            case "alpha": return 0.625f;
            case "emission.color": return new Color4(0.5f, 0.25f, 1f, 1f);
            case "emission.power": return 2.5f;
            case "lightCancel": return 0.4f;
        }
        throw new ArgumentException(field);
    }

    /// <summary>The emission intensity the tests set with the colour (stops).</summary>
    public const float TestIntensity = 1.5f;

    /// <summary>The expected material value of a field's test value (Vector4 for colours / vectors, float otherwise).</summary>
    public static Vector4 Expected(string field)
    {
        var v = TestValue(field);
        if (field == "emission.color")
        {
            var c = (Color4)v;
            float f = Mathf.Pow(2f, TestIntensity);
            return new Vector4(c.R * f, c.G * f, c.B * f, c.A);
        }
        switch (v)
        {
            case Color4 c: return new Vector4(c.R, c.G, c.B, c.A);
            case Vec4 q: return new Vector4(q.X, q.Y, q.Z, q.W);
            case float f: return new Vector4(f, 0, 0, 0);
            case bool b: return new Vector4(b ? 1f : 0f, 0, 0, 0);
        }
        throw new ArgumentException(field);
    }

    /// <summary>Sets every field of <see cref="Fields"/> to its test value (one transaction) and the emission intensity.</summary>
    public static CommandResult SetAll(Scene s, string id)
    {
        using (var tx = s.Session.Commands.BeginOrJoin("Item test values", default))
        {
            foreach (var f in Fields)
            {
                var r = tx.Set(id, "item." + f, TestValue(f));
                if (r.Status == CommandStatus.Error) return r;
            }
            var ri = tx.Set(id, "item.emission.intensity", TestIntensity);
            if (ri.Status == CommandStatus.Error) return ri;
            var c = tx.Commit();
            s.Runtime.Apply();
            return c;
        }
    }

    /// <summary>A material property value as a Vector4 by the current shader's type (colour, vector, float), or null when undeclared.</summary>
    public static Vector4? Read(Material m, string prop)
    {
        if (!m || !m.shader) return null;
        int i = m.shader.FindPropertyIndex(prop);
        if (i < 0) return null;
        switch (m.shader.GetPropertyType(i))
        {
            case UnityEngine.Rendering.ShaderPropertyType.Color: { var c = m.GetColor(prop); return new Vector4(c.r, c.g, c.b, c.a); }
            case UnityEngine.Rendering.ShaderPropertyType.Vector: return m.GetVector(prop);
            case UnityEngine.Rendering.ShaderPropertyType.Texture: return null;
            case UnityEngine.Rendering.ShaderPropertyType.Int: return new Vector4(m.GetInteger(prop), 0, 0, 0);
            default: return new Vector4(m.GetFloat(prop), 0, 0, 0);
        }
    }

    static bool IsColorOrVector(Material m, string prop)
    {
        int i = m.shader.FindPropertyIndex(prop);
        var t = m.shader.GetPropertyType(i);
        return t == UnityEngine.Rendering.ShaderPropertyType.Color || t == UnityEngine.Rendering.ShaderPropertyType.Vector;
    }

    /// <summary>Bit-equal float32 comparison of the components a property type has.</summary>
    public static bool Same(Material m, string prop, Vector4 a, Vector4 b)
    {
        int i = m.shader.FindPropertyIndex(prop);
        var t = i >= 0 ? m.shader.GetPropertyType(i) : UnityEngine.Rendering.ShaderPropertyType.Float;
        bool gamma = i >= 0 && (m.shader.GetPropertyFlags(i) & UnityEngine.Rendering.ShaderPropertyFlags.Gamma) != 0;
        if (t == UnityEngine.Rendering.ShaderPropertyType.Color || gamma)
        {
            // Material colours (and [Gamma] floats such as Standard's _Metallic) are stored linear in a linear-space project:
            // Get returns Set's value through gamma -> linear -> gamma, a few float32 ulps off (MaxColorError records the
            // largest deviation)
            for (int k = 0; k < (t == UnityEngine.Rendering.ShaderPropertyType.Color ? 4 : 1); k++)
            {
                float d = Mathf.Abs(a[k] - b[k]);
                if (d > MaxColorError) MaxColorError = d;
                if (d > ColorTolerance * Mathf.Max(1f, Mathf.Abs(b[k]))) return false;
            }
            return true;
        }
        if (t == UnityEngine.Rendering.ShaderPropertyType.Vector) return a.x.Equals(b.x) && a.y.Equals(b.y) && a.z.Equals(b.z) && a.w.Equals(b.w);
        return a.x.Equals(b.x);
    }

    /// <summary>Tolerance of colour properties (relative above 1): the linear storage round trip of Material colours.</summary>
    public const float ColorTolerance = 1e-6f;
    public static float MaxColorError;

    /// <summary>Every Studio property value of every slot of the item (renderer index, slot, prop) → value.</summary>
    public static Dictionary<(int, int, string), Vector4> Snapshot(ItemRealization it)
    {
        var d = new Dictionary<(int, int, string), Vector4>();
        var rs = it.Writer.Renderers;
        for (int r = 0; r < rs.Count; r++)
            for (int i = 0; i < rs[r].Slots.Length; i++)
            {
                var m = rs[r].Slots[i]?.Material;
                if (!m) continue;
                foreach (var p in ItemMaterialWriter.StudioProps)
                {
                    var v = Read(m, p);
                    if (v.HasValue) d[(r, i, p)] = v.Value;
                }
            }
        return d;
    }

    // ================================================================ entry

    // The spec's 89 / 197 / 185 / 121 are AI-gated on an install without AI-Shoujo (HS2 hides them, HS2Next builds the
    // fallback): ungated vanilla rows with the same features stand in (1854 float mat: colours 1-3 + patterns 1-3; 1821 beach
    // parasol: colours + patterns 1-3; 1778 purple stand light: emission, light, option; 1367 candlestick: option, light,
    // particles); 1463 snow: particle colour without an ItemComponent.
    static readonly int[] Vanilla = { 1854, 1821, 590, 1778, 842, 975, 1463, 1367, 557, 957, 935, 821, 976, 399 };
    const int Snow = 1463;   // 548 (the spec's particle target) has an ItemComponent; 1463 snow has only a ParticleComponent
    const int Bed = 1854, Tent = 1821, Lamp = 1778, Bonfire = 1367;

    public static void Run()
    {
        Begin();
        var sw = Stopwatch.StartNew();
        var head = new StringBuilder();
        try
        {
            var sv = Services(head);
            rep.AppendLine($"item test (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}, play mode {Application.isPlaying})");
            rep.Append(head);
            LoadExpect();
            Guard("M1", "ownership and leaks", () => M1(sv));
            Guard("M2", "colour write per slot (expectation file writes)", () => M2(sv));
            Guard("M3", "pattern textures", () => M3(sv));
            Guard("M4", "panel image", () => M4(sv));
            Guard("M5", "new-add defaults = expectation (vanilla)", () => M5(sv));
            Guard("M6", "options", () => M6(sv));
            Guard("M7", "visibility", () => M7(sv));
            Guard("M8", "item lights", () => M8(sv));
            Guard("M10", "save round trip", () => M10(sv));
            Guard("M11", "audio, cameras", () => M11(sv));
            Guard("M12", "build parts, features, findings, icons, sea", () => M12(sv));
        }
        catch (Exception e) { current = "run"; Check("run (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        End("item_report.txt", "ITEM TEST", sw);
        services?.Catalog?.UnloadAll();
        services = null;
        // the expectation file's rows (gigabytes as JSON objects) are not kept after the run
        expect = null; expectPath = null; expectByKey.Clear();
        if (fails > 0) TestExit.Exit(1);
    }

    // ================================================================ M1 ownership

    static void M1(RuntimeServices sv)
    {
        using (var s = new Scene(sv))
        {
            int bad = 0, total = 0, shared = 0;
            var notes = new List<string>();
            foreach (var no in Vanilla)
            {
                var id = s.Add(no);
                var it = s.Item(id);
                if (it == null || it.State != RealizationState.Built) { Check($"{no} builds", false, it?.ToString()); continue; }
                var seen = new HashSet<Material>();
                foreach (var r in it.Instance.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                    {
                        if (!m) continue;
                        total++;
                        if (MaterialCopies.IsRecorded(m)) { bad++; if (notes.Count < 5) notes.Add(no + ":" + r.name + ":" + m.name); }
                        if (!seen.Add(m)) shared++;
                    }
                int slots = it.Writer.Renderers.Sum(e => e.Slots.Count(x => x != null));
                if (slots != it.Writer.OwnedMaterials) Check($"{no}: OwnedMaterials = owned slots", false, slots + " vs " + it.Writer.OwnedMaterials);
            }
            Check("every slot of every target holds an owned copy (no bundle material)", bad == 0, $"{bad} of {total}: " + string.Join(", ", notes));
            Check("no material object is shared by two slots of one item", shared == 0, shared + " shared");

            var a = s.Add(Bed);
            var b = s.Add(Bed);
            s.Set(a, "item.colors[0].main", new Color4(1f, 0f, 0f, 1f));
            s.Set(b, "item.colors[0].main", new Color4(0f, 0f, 1f, 1f));
            var ma = new HashSet<Material>(s.Item(a).Instance.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m));
            var mb = new HashSet<Material>(s.Item(b).Instance.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m));
            Check("two instances of " + Bed + " share no material", !ma.Overlaps(mb));
            var ca = s.Item(a).Writer.Renderers[0].Slots[0].Material.GetColor("_Color");
            var cb = s.Item(b).Writer.Renderers[0].Slots[0].Material.GetColor("_Color");
            Check("their colours differ (red / blue)", ca == Color.red && cb == Color.blue, ca + " / " + cb);
        }

        // leaks: 100 add / delete cycles (graveyard trimmed at once), rebuilds, scene clear
        var keep = sv.Settings.GraveyardMaxNodes;
        sv.Settings.GraveyardMaxNodes = 0;
        try
        {
            using (var s = new Scene(sv))
            {
                void Cycle(int no)
                {
                    var id = s.Add(no);
                    if (id == null) return;
                    s.Set(id, "item.colors[0].main", new Color4(0.5f, 0.5f, 0.5f, 1f));
                    s.Runtime.Ops.Delete(new[] { id });
                    s.Runtime.Apply();
                }
                int[] cycleItems = { Bed, 975, 957, Snow, Bonfire };
                foreach (var no in cycleItems) Cycle(no);   // warm: bundles, textures of the prefabs
                s.Session.Commands.ClearHistory();
                s.Runtime.Apply();
                int mats0 = Resources.FindObjectsOfTypeAll<Material>().Length, tex0 = Resources.FindObjectsOfTypeAll<Texture2D>().Length, cache0 = ItemTextures.CachedCount;
                for (int i = 0; i < 100; i++) Cycle(cycleItems[i % cycleItems.Length]);
                s.Session.Commands.ClearHistory();
                s.Runtime.Apply();
                int mats1 = Resources.FindObjectsOfTypeAll<Material>().Length, tex1 = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
                Check("100 add/delete cycles: Material count back to the baseline", mats1 == mats0, mats0 + " -> " + mats1);
                Check("100 add/delete cycles: Texture2D count back to the baseline", tex1 == tex0, tex0 + " -> " + tex1);
                Check("pattern / panel cache back to the baseline", ItemTextures.CachedCount == cache0, cache0 + " -> " + ItemTextures.CachedCount);

                // rebuild (ref change) and scene clear release the copies too
                var id = s.Add(Bed);
                s.Set(id, "item.ref", ContentId.Vanilla(Tent).ToString());
                s.Set(id, "item.ref", ContentId.Vanilla(Bed).ToString());
                s.Session.NewScene();
                s.Runtime.Apply();
                int mats2 = Resources.FindObjectsOfTypeAll<Material>().Length;
                Check("rebuilds and a scene clear release every copy", mats2 == mats0, mats0 + " -> " + mats2);
            }
        }
        finally { sv.Settings.GraveyardMaxNodes = keep; }
    }

    // ================================================================ M3 patterns

    static void M3(RuntimeServices sv)
    {
        var rows = sv.Catalog.EntriesOf(ItemTextures.PatternCategory).Where(e => e.IsVanilla && !string.IsNullOrEmpty(e["ThumbAB"]) && e["ThumbAB"] != "0").ToList();
        Check("vanilla st_pattern rows with a bundle exist", rows.Count > 0, "" + rows.Count);
        if (rows.Count == 0) return;
        var row = rows.FirstOrDefault(e => e.Key.Id > 0) ?? rows[0];
        using (var s = new Scene(sv))
        {
            var id = s.Add(Tent);
            var it = s.Item(id);
            if (it?.Component == null) { Check(Tent + " builds with an ItemComponent", false); return; }
            var before = PatternMasks(it);
            for (int k = 0; k < 3; k++)
            {
                s.Set(id, $"item.colors[{k}].pattern.item", row.Key.ToString());
                var after = PatternMasks(it);
                string expectName = row["ThumbTex"].Replace("thumb_", "");
                int onFlagged = 0, wrong = 0, otherChanged = 0;
                foreach (var kv in after)
                {
                    bool flagged = IsPatternSlot(it, kv.Key.r, kv.Key.i, k) && kv.Key.k == k;
                    if (flagged) { if (kv.Value == expectName) onFlagged++; else wrong++; }
                    else if (kv.Value != before[kv.Key]) otherChanged++;
                }
                Check($"pattern {k + 1} = st_pattern {row.Key} on every isPattern{k + 1} slot of normal entries, nothing else changed",
                    onFlagged > 0 && wrong == 0 && otherChanged == 0, $"{onFlagged} slots with {expectName}, {wrong} wrong, {otherChanged} other masks changed");
                before = after;
            }

            // res PNG → owned decoded texture, released when changed back to none
            int cache0 = ItemTextures.CachedCount;
            var png = SmallPng(new Color32(10, 200, 30, 255));
            var res = s.Session.Model.File.Res.Add(png);
            s.Set(id, "item.colors[0].pattern.file", FileRef.FromRes(res));
            s.Set(id, "item.colors[0].pattern.item", null);
            var tex = Mask(it, 0);
            Check("res PNG → an owned texture on the pattern-1 slots", tex != null && ItemTextures.CachedCount == cache0 + 1 && it.PatternSources[0] == "res:" + res,
                $"{(tex ? tex.name : "null")}, cache {cache0} -> {ItemTextures.CachedCount}, source {it.PatternSources[0]}");
            int f0 = s.Session.Findings.Count;
            s.Set(id, "item.colors[1].pattern.item", "illusion.hs2#987654");
            Check("missing row → null mask and item.patternMissing", Mask(it, 1) == null && s.Session.Findings.Skip(f0).Any(f => f.Code == "item.patternMissing"));
            s.Set(id, "item.colors[0].pattern.file", null);
            Check("back to none → null and the owned texture released", Mask(it, 0) == null && ItemTextures.CachedCount == cache0, "cache " + ItemTextures.CachedCount);

            // user and hs2 file references
            File.WriteAllBytes(Path.Combine(sv.UserData, "pattern", "item_test_pattern.png"), png);
            s.Set(id, "item.colors[2].pattern.item", null);
            s.Set(id, "item.colors[2].pattern.file", FileRef.FromUser("pattern/item_test_pattern.png"));
            Check("{user: pattern/x.png} → a texture", Mask(it, 2) != null, it.PatternSources[2]);
            int f1 = s.Session.Findings.Count;
            s.Set(id, "item.colors[2].pattern.file", FileRef.FromUser("../outside.png"));
            Check("{user: ../x} is refused (null, item.patternMissing)", Mask(it, 2) == null);
            s.Set(id, "item.colors[2].pattern.file", null);
            Check("cache back after the user reference", ItemTextures.CachedCount == cache0, "" + ItemTextures.CachedCount);
        }
    }

    static bool IsPatternSlot(ItemRealization it, int r, int i, int k)
    {
        var e = it.Writer.Renderers[r];
        foreach (var ri in it.Component.rendererInfos)
        {
            if (ri == null || ri.renderer != e.Renderer || ri.materials == null || i >= ri.materials.Count || ri.materials[i] == null) continue;
            bool normal = ri.materials.Any(mi => mi != null && (mi.isColor1 != 0 || mi.isColor2 != 0 || mi.isColor3 != 0 || mi.isEmission != 0));
            var mi2 = ri.materials[i];
            bool p = k == 0 ? mi2.isPattern1 != 0 : k == 1 ? mi2.isPattern2 != 0 : mi2.isPattern3 != 0;
            if (normal && p) return true;
        }
        return false;
    }

    static Dictionary<(int r, int i, int k), string> PatternMasks(ItemRealization it)
    {
        var d = new Dictionary<(int, int, int), string>();
        var rs = it.Writer.Renderers;
        for (int r = 0; r < rs.Count; r++)
            for (int i = 0; i < rs[r].Slots.Length; i++)
            {
                var m = rs[r].Slots[i]?.Material;
                if (!m) continue;
                for (int k = 0; k < 3; k++)
                    if (m.shader.FindPropertyIndex(ItemMaterialWriter.PatternMaskNames[k]) >= 0)
                    {
                        var t = m.GetTexture(ItemMaterialWriter.PatternMaskNames[k]);
                        d[(r, i, k)] = t ? t.name : null;
                    }
            }
        return d;
    }

    /// <summary>The pattern-k texture of the first isPattern slot (null when none).</summary>
    static Texture Mask(ItemRealization it, int k)
    {
        var rs = it.Writer.Renderers;
        for (int r = 0; r < rs.Count; r++)
            for (int i = 0; i < rs[r].Slots.Length; i++)
                if (rs[r].Slots[i]?.Material && IsPatternSlot(it, r, i, k)) return rs[r].Slots[i].Material.GetTexture(ItemMaterialWriter.PatternMaskNames[k]);
        return null;
    }

    static byte[] SmallPng(Color32 c, int w = 8, int h = 4)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color32[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = c;
        t.SetPixels32(px);
        t.Apply();
        var b = t.EncodeToPNG();
        Object.DestroyImmediate(t);
        return b;
    }

    // ================================================================ M4 panel

    static void M4(RuntimeServices sv)
    {
        var dir = Path.Combine(sv.Hs2Root, "DefaultData", "bg");
        var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.png").OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() : new List<string>();
        Check("the HS2 install has DefaultData/bg/*.png", files.Count > 0, dir);
        if (files.Count == 0) return;
        var first = Path.GetFileName(files[0]);
        var (w, h) = PngSize(File.ReadAllBytes(files[0]));
        using (var s = new Scene(sv))
        {
            var id = s.Add(975);
            var it = s.Item(id);
            if (it?.PanelComponent == null) { Check("975 has a PanelComponent", false); return; }
            var pr = it.PanelComponent.renderer.FirstOrDefault(r => r);
            Material Pm() => it.Writer.SlotOf(pr, 0)?.Material;
            Check("new item: panel null → _MainTex null (HS2's SetMainTex(\"\"))", Pm() && Pm().GetTexture("_MainTex") == null);
            s.Set(id, "item.panel", FileRef.FromHs2("DefaultData/bg/" + first));
            var t = Pm().GetTexture("_MainTex");
            Check($"hs2 DefaultData/bg/{first} → _MainTex {w} × {h}", t && t.width == w && t.height == h, t ? t.width + " × " + t.height : "null");
            int f0 = s.Session.Findings.Count;
            s.Set(id, "item.panel", FileRef.FromHs2("DefaultData/bg/no_such_file_item_test.png"));
            Check("missing file → the previous texture stays, item.panelMissing", Pm().GetTexture("_MainTex") == t && s.Session.Findings.Skip(f0).Any(f => f.Code == "item.panelMissing"));
            s.Set(id, "item.panel", null);
            Check("null → _MainTex null, the decoded texture released", Pm().GetTexture("_MainTex") == null && it.PanelSource == null);
            s.Set(id, "item.colors[0].main", new Color4(0.25f, 0.5f, 0.75f, 1f));
            s.Set(id, "item.colors[0].pattern.uv", new Vec4(2f, 3f, 0.25f, -0.5f));
            s.Set(id, "item.colors[0].pattern.rot", 0.3f);
            s.Set(id, "item.colors[0].pattern.clamp", false);
            var m = Pm();
            Check("panel colour 0 / uv / rot / clamp written to the panel material",
                m.GetColor("_Color") == new Color(0.25f, 0.5f, 0.75f, 1f) && m.GetVector("_patternuv1") == new Vector4(2f, 3f, 0.25f, -0.5f) &&
                m.GetFloat("_patternuv1Rotator") == 0.3f && m.GetFloat("_patternclamp1") == 0f,
                $"{m.GetColor("_Color")} {m.GetVector("_patternuv1")} {m.GetFloat("_patternuv1Rotator")} {m.GetFloat("_patternclamp1")}");
        }
    }

    static (int, int) PngSize(byte[] b) =>
        b.Length < 24 ? (0, 0) : ((b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19], (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23]);

    // ================================================================ M6 options

    static void M6(RuntimeServices sv)
    {
        using (var s = new Scene(sv))
        {
            var id = s.Add(Bonfire);
            var it = s.Item(id);
            var ic = it?.Component;
            if (ic?.optionInfos == null || ic.optionInfos.Count == 0) { Check(Bonfire + " has options", false); return; }
            var st = s.State(id);
            Check("new add: one true per option", st.Options.Count == ic.optionInfos.Count && st.Options.All(x => x), string.Join(",", st.Options));
            var o = ic.optionInfos[0];
            bool On(bool v) => (o.objectsOn ?? new List<GameObject>()).Where(g => g).All(g => g.activeSelf == v) && (o.objectsOff ?? new List<GameObject>()).Where(g => g).All(g => g.activeSelf == !v);
            Check("options[0] true: objectsOn active, objectsOff inactive", On(true));
            s.Set(id, "item.options[0]", false);
            Check("options[0] false: objectsOn inactive, objectsOff active", On(false));
            s.Session.Commands.Undo();
            s.Runtime.Apply();
            Check("undo → true again", On(true) && s.State(id).Options[0]);
        }
    }

    // ================================================================ M7 visibility

    static void M7(RuntimeServices sv)
    {
        using (var s = new Scene(sv))
        {
            var id = s.Add(Bonfire);
            var it = s.Item(id);
            if (it == null || !it.Instance) { Check(Bonfire + " builds", false); return; }
            var rs = it.Instance.GetComponentsInChildren<Renderer>(true);
            var ls = it.Instance.GetComponentsInChildren<Light>(true);
            bool lightsOn0 = ls.All(l => l.enabled);
            s.Set(id, "visible", false);
            Check("hidden: every renderer forceRenderingOff", rs.All(r => r.forceRenderingOff), rs.Count(r => !r.forceRenderingOff) + " visible");
            Check("hidden: lights unchanged (a hidden lamp still lights)", ls.Length > 0 && ls.All(l => l.enabled) == lightsOn0, ls.Length + " lights");
            // an option object activated while hidden stays hidden (T7)
            s.Set(id, "item.options[0]", false);
            s.Set(id, "item.options[0]", true);
            Check("hidden: renderers activated by an option later are hidden too", it.Instance.GetComponentsInChildren<Renderer>(true).All(r => r.forceRenderingOff));
            s.Set(id, "visible", true);
            Check("shown: every renderer restored", rs.All(r => !r.forceRenderingOff));
            Info("particle play / pause by visibility runs in play mode only (edit mode does not simulate): not checked here");
        }
    }

    // ================================================================ M8 lights

    static void M8(RuntimeServices sv)
    {
        using (var s = new Scene(sv))
        {
            foreach (var no in new[] { Lamp, Bonfire, 821 })
            {
                var row = sv.Db.Items.All.FirstOrDefault(r => r.IsVanilla && r.No == no);
                var prefab = row != null ? sv.Catalog.LoadAsset<GameObject>(row) : null;
                var orig = prefab ? prefab.GetComponentsInChildren<Light>(true) : new Light[0];
                var id = s.Add(no);
                var it = s.Item(id);
                var ls = it?.Instance ? it.Instance.GetComponentsInChildren<Light>(true) : new Light[0];
                Check($"{no}: lights present ({ls.Length})", ls.Length > 0 && ls.Length == orig.Length, ls.Length + " vs prefab " + orig.Length);
                bool data = ls.All(l => l.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>());
                Check($"{no}: every light has UniversalAdditionalLightData", data);
                bool fold = true;
                var d = new List<string>();
                for (int i = 0; i < ls.Length && i < orig.Length; i++)
                {
                    var e = BundleLights.Folded(orig[i].color, orig[i].intensity);
                    if (ls[i].intensity != 1f || ls[i].color != e) { fold = false; d.Add($"{ls[i].name}: {ls[i].color}×{ls[i].intensity} vs {e}"); }
                }
                Check($"{no}: colour = original × intensity, intensity 1", fold, string.Join("; ", d));
                var again = BundleLights.Prepare(it.Instance);
                Check($"{no}: Prepare twice is idempotent", again.Folded == 0 && again.AddedLightData == 0 && ls.All(l => l.intensity == 1f));
                if (no == 821) Check("821: a light with a cookie", ls.Any(l => l.cookie), string.Join(",", ls.Select(l => l.type + (l.cookie ? "+cookie" : ""))));
            }
        }
    }

    // ================================================================ M11 audio, cameras

    public const string AudioItem = "ailegendddeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeefeeeeeeeeee#43";

    static void M11(RuntimeServices sv)
    {
        using (var s = new Scene(sv))
        {
            var id = s.Add(976);
            var it = s.Item(id);
            Check("976 (SE) builds with its SEComponent", it?.SoundComponent != null);
            var src = it?.Instance ? it.Instance.GetComponentsInChildren<AudioSource>(true) : new AudioSource[0];
            Check("976: no playing audio source", src.All(a => !a.isPlaying), src.Length + " sources");
            Check("976: SE data reported (features.sound)", it?.Features?.Sound != null && it.Features.Sound.Loop, it?.Features?.Sound?.Clip);

            if (!ContentId.TryParse(AudioItem, out var aid) || !sv.Db.TryGetItem(aid, out var arow) || arow == null) { Warn("mod item " + AudioItem + " not installed: AudioSource suppression not checked"); return; }
            int f0 = s.Session.Findings.Count;
            var id2 = s.Add(aid);
            var it2 = s.Item(id2);
            var src2 = it2?.Instance ? it2.Instance.GetComponentsInChildren<AudioSource>(true) : new AudioSource[0];
            Check($"{arow.Name}: its Play On Awake AudioSource is stopped and disabled", src2.Length > 0 && src2.All(a => !a.enabled && !a.isPlaying) && it2.AudioSuppressed == src2.Length, src2.Length + " sources");
            Check($"{arow.Name}: item.audioSuppressed (info)", s.Session.Findings.Skip(f0).Any(f => f.Code == "item.audioSuppressed" && f.Severity == FindingSeverity.Info));
        }
    }
}

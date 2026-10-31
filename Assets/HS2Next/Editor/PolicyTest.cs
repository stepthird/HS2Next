// Release-policy checks of the official build (MX7 of the studio mosaic spec; Phase 5.M package P), headless:
//   PT1  the build-hook registry holds no hook and no FK node provider, and the plugins stage of this run loads nothing (an
//        official run: Tools/run_tests.ps1 starts every process with HS2NEXT_NO_PLUGINS=1 and HS2NEXT_PRIVATE_OFF=1 and
//        passes -pluginsDir only to the plugin rows): the stage's own resolution of the plugin folder and switch is off by
//        the environment switch, a test loader given it lists no plugin, and the registry is still empty afterwards
//   PT2  cards that carry uncensor body data build the prefab body: ReplacedBodyMesh null, no hook entry in ExtStats.Hooks,
//        the vanilla breast-normal source, the summary's "hooks -"; the data is kept in the document
//   PT3  body.uncensor round trip: the imported document's canonical chara.json read and written again byte for byte; the
//        container saved (.hs2nchara bytes) and loaded: chara.json byte-equal; partial loads
//        (SetCharaSectionsCommand.Merge): face / hair / profile keep the target's body.uncensor bytes, body takes the source's
//   PT4  hook code and the export: no .cs file under Assets registers a build hook or an FK node provider at start-up (an
//        InitializeOnLoad / RuntimeInitializeOnLoad method next to a CharaBuildHooks.Register* call, comments left out), no
//        runtime file does so outside the plugin adapter (Runtime/App/Plugins; plugins register through the loader), and the
//        meta file of every Assets folder the manifest excludes is excluded too
//   PT5  the scan cache: the catalog's zipmods list their manifest elements (cache version 8), the cache file has no
//        declaration rows of one plugin (U) and the T rows of every zipmod with a GUID; with -v7cache <a version 7 cache of
//        the install>: the upgrade of a copy in a scratch folder (every manifest read alone, nothing rescanned, the time)
//        and a second scan of the upgraded copy (nothing read)
// Run: Tools/unity_batch.ps1 -Method PolicyTest.Run -NoGraphics [-MethodArgs @('-only','PT1,PT3')]. Report:
// Spike/out/policy/policy_report.txt, first line "SUMMARY: N PASS, M FAIL"; exit 1 on a FAIL. Numbers only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HS2Next.App;
using HS2Next.App.Plugins;
using HS2Next.Chara;
using HS2Next.Chara.Ext;
using HS2Next.Content;
using HS2Next.Data;
using HS2Next.Data.Ext;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class PolicyTest
{
    static StringBuilder rep;
    static int passes, fails;
    static string current = "";
    static string[] only;
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "policy");

    static string Arg(string name, string def = null)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    static void Info(string s) => rep.AppendLine("   " + s);
    static bool Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  [{current}] {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
        return ok;
    }

    static void Guard(string id, string title, Action a)
    {
        if (only != null && !only.Contains(id)) return;
        current = id;
        rep.AppendLine();
        rep.AppendLine($"== {id} {title}");
        var sw = Stopwatch.StartNew();
        try { a(); }
        catch (Exception e) { Check("exception", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        Info($"({sw.Elapsed.TotalSeconds:F1} s)");
    }

    public static void Run()
    {
        rep = new StringBuilder(); passes = fails = 0;
        var o = Arg("-only");
        only = o != null ? o.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray() : null;
        var sw = Stopwatch.StartNew();
        MakerCoreTest.Harness h = null;
        try
        {
            Guard("PT1", "the build-hook registry of an official run", PT1);
            Guard("PT4", "no self-registering hook code under Assets; the export", PT4);
            if (only == null || only.Any(x => x == "PT2" || x == "PT3" || x == "PT5"))
            {
                h = MakerCoreTest.Harness.Open(new StringBuilder());
                Guard("PT2", "cards with uncensor body data build the prefab body", () => PT2(h));
                Guard("PT3", "body.uncensor round trip (save, load, partial loads)", () => PT3(h));
                Guard("PT5", "the scan cache lists manifest elements (version 8)", () => PT5(h.Catalog));
            }
        }
        catch (Exception e) { current = "run"; Check("run", false, e.ToString()); }
        finally { h?.Dispose(); }
        Directory.CreateDirectory(OutDir);
        string summary = $"SUMMARY: {passes} PASS, {fails} FAIL";
        File.WriteAllText(Path.Combine(OutDir, "policy_report.txt"),
            summary + $"\nPolicyTest (the release policy of the official build; {sw.Elapsed.TotalSeconds:F0} s)\n" + rep + "\n" + summary + "\n", new UTF8Encoding(false));
        Debug.Log("PolicyTest " + summary);
        if (Application.isBatchMode) EditorApplication.Exit(fails > 0 ? 1 : 0);
    }

    // ---------------------------------------------------------------- PT1

    static void PT1()
    {
        string noPlugins = Environment.GetEnvironmentVariable(PluginLaunch.EnvNoPlugins);
        Info("HS2NEXT_PRIVATE_OFF=" + (Environment.GetEnvironmentVariable("HS2NEXT_PRIVATE_OFF") ?? "(not set)") + ", " + PluginLaunch.EnvNoPlugins + "=" + (noPlugins ?? "(not set)"));
        Check("no build hook is registered", CharaBuildHooks.Registered.Count == 0, string.Join(", ", CharaBuildHooks.Registered));
        Check("no FK node provider is registered", CharaBuildHooks.FkNodeProviders.Count == 0 && CharaBuildHooks.FkNodes(null) == null, string.Join(", ", CharaBuildHooks.FkNodeProviders));
        foreach (HookStage s in Enum.GetValues(typeof(HookStage))) Check($"no hook at {s}", !CharaBuildHooks.Any(s));

        // the plugins stage of this run: the folder and switch it resolves (no -pluginsDir or -noPlugins on a test's
        // command line, the environment, the default settings), loaded by a test loader with its own user data
        var launch = AppSettings.Defaults(Array.Empty<string>()).PluginLaunch();
        Info("the stage's resolution: " + launch);
        if (!Check("the plugins stage of this run is switched off by " + PluginLaunch.EnvNoPlugins + " (an official run)",
                   !launch.Enabled && launch.Source == PluginLaunch.SourceEnv, launch.ToString())) return;   // never loads a local plugin folder
        var host = new PluginHost(null, Path.Combine(OutDir, "pt1_userdata"));
        try
        {
            host.Load(launch);
            var list = host.ListJson(false);
            Info(host.Summary);
            Check("the stage loads nothing: plugin.list has no plugin, enabled false, source env",
                  host.Entries.Count == 0 && list["enabled"]?.Type == JTokenType.Boolean && !(bool)list["enabled"] && (string)list["source"] == PluginLaunch.SourceEnv
                  && list["plugins"] is JArray pl && pl.Count == 0, list.ToString(Formatting.None));
            Check("bridge.version lists no plugin", host.Info().Count == 0, host.Info().Count.ToString());
        }
        finally { host.Shutdown(); }
        Check("the build-hook registry is still empty after the stage", CharaBuildHooks.Registered.Count == 0 && CharaBuildHooks.FkNodeProviders.Count == 0,
              string.Join(", ", CharaBuildHooks.Registered) + " | " + string.Join(", ", CharaBuildHooks.FkNodeProviders));
    }

    // ---------------------------------------------------------------- PT2

    /// <summary>Female cards (file order) whose plugin data names an uncensor body, up to <paramref name="max"/>.</summary>
    static List<(string file, string guid)> UncensorCards(int max, out int scanned)
    {
        var l = new List<(string, string)>();
        scanned = 0;
        string dir = Path.Combine(TestTargets.UserData, "chara", "female");
        if (!Directory.Exists(dir)) return l;
        foreach (var f in Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
        {
            if (l.Count >= max) break;
            scanned++;
            try
            {
                var g = CardExtensions.Decode(Card.Load(f))?.Uncensor?.BodyGUID;
                if (g != null && !g.StartsWith("Default.", StringComparison.Ordinal)) l.Add((f, g));
            }
            catch (Exception) { }
        }
        return l;
    }

    static void PT2(MakerCoreTest.Harness h)
    {
        var cards = UncensorCards(int.Parse(Arg("-cards", "6")), out int scanned);
        Info($"female cards scanned {scanned}; with uncensor body data {cards.Count}");
        if (!Check("cards with uncensor body data found", cards.Count > 0)) return;
        foreach (var (file, guid) in cards)
        {
            var b = h.Build(file);
            try
            {
                string name = Path.GetFileName(file);
                if (!Check(name + ": built", b.Ok, b.Error)) continue;
                var ctx = b.Context;
                Check(name + ": the prefab body (no replaced body mesh, no hook ran, vanilla normals, \"hooks -\"), the data kept",
                      ctx.ReplacedBodyMesh == null && ctx.BodyNormals == null && ctx.Stats.Hooks.Count == 0 && ctx.BustNormals?.Source == "vanilla"
                      && ctx.Stats.Summary().Contains("; hooks -; ") && ctx.Chara?.Body?.Uncensor?.Body == guid
                      && !ctx.Missing.Any(m => m.Kind != null && m.Kind.StartsWith("uncensor", StringComparison.Ordinal)),
                      $"replaced {ctx.ReplacedBodyMesh ?? "-"}, hooks {ctx.Stats.Hooks.Count}, normals {ctx.BustNormals?.Source}, data {ctx.Chara?.Body?.Uncensor?.Body}");
            }
            finally { b.Dispose(); }
            h.ReleaseBundles();
        }
    }

    // ---------------------------------------------------------------- PT3

    static byte[] Canon(CharaDocument d) => CanonicalJsonWriter.ToBytes(CharaDocument.EntryName, d.Write);
    static CharaDocument Read(byte[] b) => CharaDocument.Read(DocReader.ForDocument(b, new DocReadContext(CharaDocument.EntryName)));
    static string UncensorJson(byte[] chara) => DocReader.Parse(chara, CharaDocument.EntryName)["body"]?["uncensor"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "(absent)";
    static readonly DateTime Clock0 = new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc);

    static void PT3(MakerCoreTest.Harness h)
    {
        var cards = UncensorCards(2, out _);
        if (!Check("two cards with uncensor body data", cards.Count == 2)) return;
        var docs = new List<CharaContainer>();
        foreach (var (file, guid) in cards)
        {
            var c = MakerEditTest.Import(h, file, out string err);
            if (!Check(Path.GetFileName(file) + ": imported", c != null, err)) return;
            var bytes = Canon(c.Chara);
            var again = Canon(Read(bytes));
            Check(Path.GetFileName(file) + ": body.uncensor imported; chara.json read and written again byte for byte",
                  c.Chara.Body.Uncensor?.Body == guid && bytes.SequenceEqual(again), UncensorJson(bytes));
            var opts = AppSaveOptions.Complete(new SaveOptions { Thumbnail = ThumbnailMode.Placeholder, Clock = () => Clock0 }, h.Services);
            var file2 = c.ToBytes(opts);
            var loaded = CharaContainer.Load(file2);
            Check(Path.GetFileName(file) + ": the container saved and loaded: chara.json byte-equal",
                  loaded.Error == null && loaded.Value != null && Canon(loaded.Value.Chara).SequenceEqual(bytes), loaded.Error?.Message);
            docs.Add(c);
        }
        // partial loads: target = card 0, source = card 1 with a different uncensor value
        var t = Canon(docs[0].Chara);
        var sDoc = Read(Canon(docs[1].Chara));
        sDoc.Body.Uncensor = new HS2Next.Save.UncensorData { Body = "policy.test.body", Penis = "policy.test.penis", DisplayBalls = true, Legacy = "x" };
        var s = Canon(sDoc);
        string tU = UncensorJson(t), sU = UncensorJson(s);
        foreach (var f in new[] { LoadFlags.Face, LoadFlags.Hair, LoadFlags.Profile, LoadFlags.Face | LoadFlags.Hair | LoadFlags.Profile })
        {
            var m = SetCharaSectionsCommand.Merge(t, s, f, HeadNames.Vanilla);
            Check($"partial load {f}: the target's body.uncensor kept", UncensorJson(m) == tU, UncensorJson(m));
        }
        foreach (var f in new[] { LoadFlags.Body, LoadFlags.Face | LoadFlags.Body | LoadFlags.Hair | LoadFlags.Profile })
        {
            var m = SetCharaSectionsCommand.Merge(t, s, f, HeadNames.Vanilla);
            Check($"partial load {f}: the source's body.uncensor taken (byte-equal member)", UncensorJson(m) == sU && Canon(Read(m)).SequenceEqual(m), UncensorJson(m));
        }
    }

    // ---------------------------------------------------------------- PT4

    sealed class Manifest
    {
        public readonly List<(string pat, Regex rx, bool literal)> Include = new List<(string, Regex, bool)>();
        public readonly List<(string pat, Regex rx)> Exclude = new List<(string, Regex)>();

        /// <summary>The glob rules of Tools/export_manifest.json ('**/' any folders or none, '**' anything, '*' and '?' inside
        /// one folder; anchored, case-insensitive).</summary>
        public static Regex Glob(string g)
        {
            var sb = new StringBuilder("^");
            for (int i = 0; i < g.Length; i++)
            {
                char c = g[i];
                if (c == '*' && i + 1 < g.Length && g[i + 1] == '*')
                {
                    if (i + 2 < g.Length && g[i + 2] == '/') { sb.Append("(?:.*/)?"); i += 2; }
                    else { sb.Append(".*"); i += 1; }
                }
                else if (c == '*') sb.Append("[^/]*");
                else if (c == '?') sb.Append("[^/]");
                else sb.Append(Regex.Escape(c.ToString()));
            }
            return new Regex(sb.Append('$').ToString(), RegexOptions.IgnoreCase);
        }

        public static Manifest Load(string path)
        {
            var j = JObject.Parse(File.ReadAllText(path));
            var m = new Manifest();
            foreach (var p in j["include"].Select(x => (string)x)) m.Include.Add((p, Glob(p), p.IndexOfAny(new[] { '*', '?', '[' }) < 0));
            foreach (var p in j["exclude"].Select(x => (string)x)) m.Exclude.Add((p, Glob(p)));
            return m;
        }

        /// <summary>True when the export ships <paramref name="path"/> (the rules of leak_check.py's Manifest.classify).</summary>
        public bool Exported(string path)
        {
            bool inc = false;
            foreach (var (_, rx, lit) in Include)
                if (rx.IsMatch(path)) { if (lit) return true; inc = true; }
            if (Exclude.Any(e => e.rx.IsMatch(path))) return false;
            return inc;
        }
    }

    /// <summary>A call of the build-hook registration (Register, RegisterFkNodes and their owned forms).</summary>
    static readonly Regex HookRegistration = new Regex(@"CharaBuildHooks\s*\.\s*Register\w*\s*\(");
    static readonly Regex StartUpMethod = new Regex(@"\b(InitializeOnLoad\w*|RuntimeInitializeOnLoadMethod)\b");
    /// <summary>The folder of the plugin adapter: the only runtime code that registers hooks (for loaded plugins).</summary>
    const string PluginAdapter = "Assets/HS2Next/Runtime/App/Plugins/";

    /// <summary>C# text without its comments (line and block comments; string contents are not parsed, so a "//" inside a
    /// string cuts the rest of that line, which only makes the scan miss text after it).</summary>
    static string WithoutComments(string text) => Regex.Replace(text, @"/\*.*?\*/|//[^\n]*", " ", RegexOptions.Singleline);

    static void PT4()
    {
        string root = ContentSettings.ProjectRoot;
        var m = Manifest.Load(Path.Combine(root, "Tools", "export_manifest.json"));
        string assets = Path.Combine(root, "Assets");
        int scanned = 0, registering = 0, editorTests = 0;
        var startUp = new List<string>();
        var adapter = new List<string>();
        var outside = new List<string>();
        foreach (var f in Directory.GetFiles(assets, "*.cs", SearchOption.AllDirectories))
        {
            string rel = "Assets/" + f.Substring(assets.Length + 1).Replace('\\', '/');
            string code = WithoutComments(File.ReadAllText(f));
            scanned++;
            if (!HookRegistration.IsMatch(code)) continue;
            registering++;
            if (StartUpMethod.IsMatch(code)) startUp.Add(rel);
            else if (rel.StartsWith(PluginAdapter, StringComparison.Ordinal)) adapter.Add(rel);
            else if (rel.Split('/').Contains("Editor")) editorTests++;
            else outside.Add(rel);
        }
        Info($".cs files under Assets: {scanned}; calling the build-hook registration: {registering} (the plugin adapter {adapter.Count}, Editor tests {editorTests})");
        Check("no file under Assets registers a build hook or FK node provider at start-up", startUp.Count == 0, string.Join(", ", startUp));
        Check("no runtime file registers one outside the plugin adapter (Runtime/App/Plugins)", outside.Count == 0, string.Join(", ", outside));
        // folders the manifest excludes as a whole: their meta files are excluded too
        var folders = Directory.GetDirectories(assets).Select(d => "Assets/" + Path.GetFileName(d)).ToList();
        var metaShipped = new List<string>();
        foreach (var d in folders)
        {
            bool anyFile = Directory.EnumerateFiles(Path.Combine(root, d), "*", SearchOption.AllDirectories).Any();
            bool allOut = anyFile && Directory.EnumerateFiles(Path.Combine(root, d), "*", SearchOption.AllDirectories)
                .All(x => !m.Exported(d + "/" + x.Substring(Path.Combine(root, d).Length + 1).Replace('\\', '/')));
            if (allOut && File.Exists(Path.Combine(root, d + ".meta")) && m.Exported(d + ".meta")) metaShipped.Add(d + ".meta");
        }
        Check("the meta file of every Assets folder the export leaves out is left out too", metaShipped.Count == 0, string.Join(", ", metaShipped));
    }

    // ---------------------------------------------------------------- PT5

    static void PT5(ContentCatalog cat)
    {
        var withGuid = cat.ScannedZipmods.Where(z => z.Guid != null && z.SkippedBy == null).ToList();
        int listed = withGuid.Count(z => z.ManifestElements.Count > 0 && z.ManifestElements.Contains("guid"));
        Check("every loaded zipmod lists its manifest's top-level elements (guid among them)", listed == withGuid.Count, $"{listed} of {withGuid.Count}");
        var top = withGuid.SelectMany(z => z.ManifestElements).GroupBy(x => x).OrderByDescending(g => g.Count()).Take(12).Select(g => g.Key + " " + g.Count());
        Info("most common top-level elements: " + string.Join(", ", top));
        foreach (var src in cat.Sources.OfType<ZipmodSourceBase>())
        {
            string file = cat.CacheFileFor(src);
            if (file == null || !File.Exists(file)) { Info(src.Id + ": no cache file"); continue; }
            string first = File.ReadLines(file).First();
            int u = 0, t = 0, z = 0, zg = 0;
            foreach (var line in File.ReadLines(file))
            {
                if (line.StartsWith("U\t", StringComparison.Ordinal)) u++;
                else if (line == "T" || line.StartsWith("T\t", StringComparison.Ordinal)) t++;
                else if (line.StartsWith("Z\t", StringComparison.Ordinal)) { z++; if (!line.EndsWith("\t", StringComparison.Ordinal)) zg++; }
            }
            Check($"{Path.GetFileName(file)}: version 8 header, no U rows, one T row per zipmod with a GUID",
                  first == ZipmodSourceBase.CacheHeader && first.EndsWith("\t8", StringComparison.Ordinal) && u == 0 && t == zg, $"header {first.Replace('\t', ' ')}, Z {z} (with a GUID {zg}), T {t}, U {u}");
        }
        // the upgrade of a version 7 cache (a copy in a scratch folder; the source scans alone, no catalog)
        string v7 = Arg("-v7cache");
        if (v7 == null) { Info("-v7cache not given: the version 7 upgrade is not timed"); return; }
        if (!Check("the version 7 cache exists", File.Exists(v7), v7)) return;
        var install = cat.Sources.OfType<Hs2InstallSource>().FirstOrDefault();
        if (!Check("the install source", install != null)) return;
        string dir = Path.Combine(Path.GetTempPath(), "hs2next_policy_zu2_" + Process.GetCurrentProcess().Id);
        Directory.CreateDirectory(dir);
        try
        {
            string f = Path.Combine(dir, Path.GetFileName(v7));
            File.Copy(v7, f, true);
            int uBefore = File.ReadLines(f).Count(l => l.StartsWith("U\t", StringComparison.Ordinal));
            var src = new Hs2InstallSource(install.Root);
            var sw = Stopwatch.StartNew();
            var r = src.ScanZipmods(f);
            double up = sw.Elapsed.TotalMilliseconds;
            int guids = r.Zipmods.Count(x => x.Guid != null);
            Info($"ZU2: version 7 -> 8 upgrade {up:F0} ms: {r.ElementsRead} manifests read, {r.GamesRead} games read, {r.Scanned} rescanned, {r.Unreadable} unreadable, discarded {r.CacheDiscarded}; {uBefore} U rows dropped; {r.Zipmods.Count} zipmods ({guids} with a GUID)");
            Check("ZU2: the version 7 cache upgrades without a cold scan (every manifest of a zipmod with a GUID read alone)",
                  r.Scanned == 0 && !r.CacheDiscarded && r.ElementsRead == guids && r.GamesRead == 0, $"{r.ElementsRead} of {guids}");
            string head = File.ReadLines(f).First();
            Check("ZU2: the upgraded copy is version 8 without U rows", head == ZipmodSourceBase.CacheHeader && !File.ReadLines(f).Any(l => l.StartsWith("U\t", StringComparison.Ordinal)));
            sw.Restart();
            var r2 = new Hs2InstallSource(install.Root).ScanZipmods(f);
            double warm = sw.Elapsed.TotalMilliseconds;
            Info($"ZU2: a second scan of the upgraded copy {warm:F0} ms ({r2.ElementsRead} manifests read, {r2.Scanned} rescanned)");
            Check("ZU2: the second scan reads nothing", r2.ElementsRead == 0 && r2.Scanned == 0 && r2.GamesRead == 0);
        }
        finally
        {
            try { foreach (var x in Directory.GetFiles(dir)) File.Delete(x); Directory.Delete(dir); } catch (Exception) { }
        }
    }
}

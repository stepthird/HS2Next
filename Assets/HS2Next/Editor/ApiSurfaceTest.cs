// ApiSurfaceTest: the public contract of HS2Next compared with its committed listing under Tools/api/.
// The listing has four files, each headed by the Api version it describes:
//   HS2Next.Api.surface.txt, HS2Next.Api.Unity.surface.txt  every public type and member of the two Api assemblies, one
//       line each in ordinal order: "T <type> | <kind>", "I <type> : <interface>", "M <type>::<member> | <declaration>"
//       (namespace-qualified types; parameter names and defaults; constant and enum values; [Experimental], [Obsolete],
//       [Flags] and "implemented by plugins" marked);
//   commands.surface.json  the bridge protocol (version, error codes, envelope fields, headers) and every core and bridge
//       command as help describes it: name, aliases, group, owner, since, gate, required stages, flags, whether extra
//       arguments pass, arguments (name, type, required, default) and returned fields (name, type, always). Help texts are
//       left out; commands flagged experimental are listed by name only;
//   plugin_manifest.surface.json  the plugin manifest schema measured on the parser: the keys with their JSON types and
//       whether they are required, the refusal codes and states, and fixed probes of the GUID, prefix, semver and Api
//       compatibility rules.
// Each surface is flattened into keyed entries and compared with the listing: a removed or changed entry is MAJOR, an added
// entry MINOR (MAJOR for a member added to an interface plugins implement, a new abstract member, a newly required argument
// or manifest key), a change that only concerns experimental entries INFO; probes that are added or dropped are INFO. A
// listing passes when its largest class is at most INFO, or MINOR with a higher Api minor than the listing's, or MAJOR with
// a higher Api major. The classifier also runs on synthetic pairs. The command registry is built without an app (the
// handlers are not run), so the row needs neither the game install nor a test host.
// Run: -executeMethod ApiSurfaceTest.Run [-write] (headless, -nographics, edit mode). -write rewrites the listing from the
// code before the comparison: at a release whose version changes, never to hide a difference. Report
// Spike/out/api/api_surface_report.txt, first line "SUMMARY: N PASS, M FAIL"; exit 1 on a FAIL.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HS2Next.Api.Bridge;
using HS2Next.App.Plugins;
using HS2Next.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class ApiSurfaceTest
{
    public const string ApiFile = "HS2Next.Api.surface.txt", ApiUnityFile = "HS2Next.Api.Unity.surface.txt",
        CommandsFile = "commands.surface.json", ManifestFile = "plugin_manifest.surface.json", ReportFile = "api_surface_report.txt";

    public static string ListingDir => Path.Combine(ContentSettings.ProjectRoot, "Tools", "api");
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "api");

    /// <summary>Interfaces plugins implement: they never gain members within a major version (a new capability is a new
    /// optional interface). Every other public interface is implemented by the host and may gain members in a minor
    /// version.</summary>
    public static readonly string[] PluginImplemented =
    {
        "HS2Next.Api.Bridge.IBatchHook", "HS2Next.Api.Plugins.IPlugin", "HS2Next.Api.Plugins.IPluginSceneLoaded",
        "HS2Next.Api.Plugins.IPluginUpdate", "HS2Next.Api.Save.IExtBlockHandler", "HS2Next.Api.Save.IKkexConsumer",
        "HS2Next.Motion.IMotionStage",
    };

    /// <summary>The commands flagged experimental at the freeze (Api 1.0.0): UI introspection, dumps, diagnostics.</summary>
    public static readonly string[] Experimental100 =
    {
        "app.perf", "bones.dump", "chara.bones", "chara.clothesDump", "content.stats", "face.dump",
        "ui.action", "ui.input", "ui.panel", "ui.query", "ui.screenshot", "ui.state",
    };

    /// <summary>Constants whose value is the Api version itself (listed without the value: they change with every release).</summary>
    static readonly HashSet<string> VersionConstants = new HashSet<string>(StringComparer.Ordinal)
    {
        "HS2Next.ApiVersion::Major", "HS2Next.ApiVersion::Minor", "HS2Next.ApiVersion::Patch", "HS2Next.ApiVersion::Semver",
    };

    static StringBuilder rep;
    static int passes, fails;

    // ================================================================ model

    public enum Change { None = 0, Info = 1, Minor = 2, Major = 3 }

    /// <summary>One keyed entry of a surface. Kind: type, iface, member, cmd, cmdmeta (a command's group and since: their change
    /// is major, their coming and going follows the command's own entries), alias, arg, ret, exp, proto, mkey, mcode, mprobe.</summary>
    public sealed class Entry
    {
        public string Key, Value = "", Kind;
        public bool Exp;
        public override string ToString() => Value.Length > 0 ? Key + " | " + Value : Key;
    }

    public sealed class Diff
    {
        public Change Class;
        public string Text;
        public override string ToString() => Class.ToString().ToUpperInvariant() + " " + Text;
    }

    public struct Ver
    {
        public int Major, Minor, Patch;
        public Ver(int a, int b, int c) { Major = a; Minor = b; Patch = c; }
        public static Ver Current => new Ver(HS2Next.ApiVersion.Major, HS2Next.ApiVersion.Minor, HS2Next.ApiVersion.Patch);
        public int CompareTo(Ver o) => Major != o.Major ? Major.CompareTo(o.Major) : Minor != o.Minor ? Minor.CompareTo(o.Minor) : Patch.CompareTo(o.Patch);
        public override string ToString() => Major + "." + Minor + "." + Patch;
        public static bool TryParse(string s, out Ver v)
        {
            v = default;
            var m = Regex.Match(s ?? "", @"^(\d+)\.(\d+)\.(\d+)$");
            if (!m.Success) return false;
            v = new Ver(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
            return true;
        }
    }

    // ================================================================ run

    public static void Run()
    {
        rep = new StringBuilder();
        passes = fails = 0;
        var sw = Stopwatch.StartNew();
        bool write = TestArgs.Get().Contains("-write");
        string counts = "";
        try
        {
            var cur = Ver.Current;
            var apiAsm = typeof(HS2Next.ApiVersion).Assembly;
            var unityAsm = typeof(HS2Next.Api.Unity.IUnityPluginServices).Assembly;

            // AZ1 versions
            rep.AppendLine("AZ1 versions");
            string sem = HS2Next.ApiVersion.Semver;
            Check("AZ1", "ApiVersion.Semver is MAJOR.MINOR.PATCH of the constants", sem == cur.ToString(), sem + " / " + cur);
            foreach (var a in new[] { apiAsm, unityAsm })
            {
                var v = a.GetName().Version;
                Check("AZ1", a.GetName().Name + ": AssemblyVersion MAJOR.0.0.0", v.Major == cur.Major && v.Minor == 0 && v.Build == 0 && v.Revision == 0, v.ToString());
                string info = a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                Check("AZ1", a.GetName().Name + ": informational version = the Api semver", info == sem, info);
            }
            Check("AZ1", "Application.version is a semantic version", Regex.IsMatch(Application.version ?? "", @"^\d+\.\d+\.\d+$"), Application.version);
            rep.AppendLine("  Api " + sem + ", app " + Application.version);

            // the surfaces of the code
            var apiText = AssemblySurface(apiAsm, "HS2Next.Api", cur);
            var unityText = AssemblySurface(unityAsm, "HS2Next.Api.Unity", cur);
            var cmdJson = CommandSurface(cur, out var cmdNotes);
            var manJson = ManifestSurface(cur);
            string cmdText = JsonText(cmdJson), manText = JsonText(manJson);

            if (write)
            {
                Directory.CreateDirectory(ListingDir);
                WriteText(Path.Combine(ListingDir, ApiFile), apiText);
                WriteText(Path.Combine(ListingDir, ApiUnityFile), unityText);
                WriteText(Path.Combine(ListingDir, CommandsFile), cmdText);
                WriteText(Path.Combine(ListingDir, ManifestFile), manText);
                rep.AppendLine("-write: the listing was rewritten from the code (Api " + cur + ")");
            }
            Directory.CreateDirectory(OutDir);
            WriteText(Path.Combine(OutDir, ApiFile), apiText);
            WriteText(Path.Combine(OutDir, ApiUnityFile), unityText);
            WriteText(Path.Combine(OutDir, CommandsFile), cmdText);
            WriteText(Path.Combine(OutDir, ManifestFile), manText);

            var apiE = ParseText(apiText, out _);
            var unityE = ParseText(unityText, out _);
            var cmdE = FlattenCommands(ParseJson(cmdText));   // from the text, as the listing is read
            var manE = FlattenManifest(ParseJson(manText));
            counts = Counts(apiE, "HS2Next.Api") + "; " + Counts(unityE, "HS2Next.Api.Unity") + "; " + CommandCounts(cmdJson) + "; manifest "
                + manE.Count(e => e.Kind == "mkey") + " keys, " + manE.Count(e => e.Kind == "mcode") + " codes, " + manE.Count(e => e.Kind == "mprobe") + " probes";
            rep.AppendLine("  " + counts);
            foreach (var n in cmdNotes) rep.AppendLine("  " + n);

            // AZ2 the listing
            rep.AppendLine("AZ2 the listing (" + ListingDir.Replace('\\', '/') + ")");
            CompareFile(ApiFile, apiE, s => ParseText(s, out _), s => TextHeader(s), cur);
            CompareFile(ApiUnityFile, unityE, s => ParseText(s, out _), s => TextHeader(s), cur);
            CompareFile(CommandsFile, cmdE, s => FlattenCommands(ParseJson(s)), s => JsonHeader(s), cur);
            CompareFile(ManifestFile, manE, s => FlattenManifest(ParseJson(s)), s => JsonHeader(s), cur);

            // AZ3 the classifier
            rep.AppendLine("AZ3 the classifier on synthetic pairs");
            Synthetic();

            // AZ4 the command listing
            rep.AppendLine("AZ4 the command listing");
            Az4(cmdJson);

            // AZ5 the plugin manifests of the repository
            rep.AppendLine("AZ5 the plugin manifests of the sample and the test plugins");
            Az5(cur);
        }
        catch (Exception e) { Check("run", "no exception", false, e.ToString()); }
        string summary = $"SUMMARY: {passes} PASS, {fails} FAIL";
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, ReportFile), summary + "; " + counts + "\nApiSurfaceTest (the public contract against Tools/api; "
            + sw.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s)\n" + rep + "\n" + summary + "\n", new UTF8Encoding(false));
        Debug.Log("ApiSurfaceTest " + summary);
        if (Application.isBatchMode) TestExit.Exit(fails > 0 ? 1 : 0);
    }

    static void Check(string id, string what, bool ok, string detail = null)
    {
        if (ok) passes++; else fails++;
        rep.Append("  ").Append(ok ? "PASS " : "FAIL ").Append(id).Append(' ').Append(what);
        if (!string.IsNullOrEmpty(detail)) rep.Append(": ").Append(detail.Replace("\r", "").Replace("\n", "\n      "));
        rep.Append('\n');
    }

    static void WriteText(string path, string text) => File.WriteAllText(path, text, new UTF8Encoding(false));

    static string Counts(List<Entry> e, string name)
    {
        var types = e.Where(x => x.Kind == "type").ToList();
        var members = e.Where(x => x.Kind == "member").ToList();
        return $"{name} {types.Count(x => !x.Exp)} types + {types.Count(x => x.Exp)} experimental, {members.Count(x => !x.Exp)} members + {members.Count(x => x.Exp)} experimental";
    }

    static string CommandCounts(JObject c) =>
        $"commands {((JArray)c["commands"]).Count} frozen + {((JArray)c["experimental"]).Count} experimental, error codes {((JArray)c["protocol"]["errorCodes"]).Count}, protocol {c["protocol"]["version"]}";

    // ================================================================ AZ2 comparison with the listing

    static void CompareFile(string file, List<Entry> code, Func<string, List<Entry>> parse, Func<string, string> header, Ver cur)
    {
        string path = Path.Combine(ListingDir, file);
        if (!File.Exists(path)) { Check("AZ2", file + " exists", false, "missing (write it with -write)"); return; }
        string text = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n");
        string h = header(text);
        if (!Ver.TryParse(h, out var baseV)) { Check("AZ2", file + ": header names the Api version", false, h ?? "no header"); return; }
        List<Entry> listing;
        try { listing = parse(text); }
        catch (Exception e) { Check("AZ2", file + " is readable", false, e.Message); return; }
        var diffs = Compare(listing, code);
        var max = diffs.Count == 0 ? Change.None : diffs.Max(d => d.Class);
        var verdict = Judge(max, baseV, cur);
        string detail = $"listing {baseV}, code {cur}; {diffs.Count(d => d.Class == Change.Major)} major, {diffs.Count(d => d.Class == Change.Minor)} minor, {diffs.Count(d => d.Class == Change.Info)} info";
        if (verdict.Note != null) detail += "; " + verdict.Note;
        if (diffs.Count > 0) detail += "\n" + string.Join("\n", diffs.OrderByDescending(d => d.Class).ThenBy(d => d.Text, StringComparer.Ordinal).Take(60));
        if (diffs.Count > 60) detail += "\n… " + (diffs.Count - 60) + " more";
        Check("AZ2", file + ": " + listing.Count + " entries, no change the version does not allow", verdict.Pass, detail);
    }

    public sealed class Verdict
    {
        public bool Pass;
        public string Note;
    }

    /// <summary>Whether a largest change class is allowed between the listing's version and the code's.</summary>
    public static Verdict Judge(Change max, Ver listing, Ver code)
    {
        int c = code.CompareTo(listing);
        if (c < 0) return new Verdict { Pass = false, Note = "the code's version is lower than the listing's" };
        string later = c > 0 ? "the listing describes " + listing + ": rewrite it with -write when " + code + " is released" : null;
        switch (max)
        {
            case Change.None:
            case Change.Info:
                return new Verdict { Pass = true, Note = later };
            case Change.Minor:
                bool minorOk = code.Major > listing.Major || (code.Major == listing.Major && code.Minor > listing.Minor);
                return new Verdict { Pass = minorOk, Note = minorOk ? later : "additions need a higher minor version than " + listing };
            default:
                bool majorOk = code.Major > listing.Major;
                return new Verdict { Pass = majorOk, Note = majorOk ? later : "removals and changes need a higher major version than " + listing };
        }
    }

    /// <summary>The classified differences between a listing and the code's surface.</summary>
    public static List<Diff> Compare(List<Entry> before, List<Entry> after)
    {
        var b = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var e in before) b[e.Key] = e;
        var a = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var e in after) a[e.Key] = e;
        var pluginTypes = new HashSet<string>(after.Where(e => e.Kind == "type" && e.Value.Contains("implemented by plugins")).Select(e => e.Key.Substring(2)), StringComparer.Ordinal);
        var classTypes = new HashSet<string>(after.Where(e => e.Kind == "type" && Regex.IsMatch(e.Value, @"^(abstract )?class\b")).Select(e => e.Key.Substring(2)), StringComparer.Ordinal);
        var list = new List<Diff>();
        foreach (var kv in b)
        {
            var o = kv.Value;
            if (!a.TryGetValue(kv.Key, out var n))
            {
                list.Add(new Diff { Class = o.Exp || o.Kind == "mprobe" || o.Kind == "cmdmeta" ? Change.Info : Change.Major, Text = "removed: " + o });
                continue;
            }
            if (o.Value == n.Value && o.Exp == n.Exp) continue;
            Change k;
            if (o.Exp && n.Exp) k = Change.Info;
            else if (o.Exp) k = Change.Minor;
            else if (n.Exp) k = Change.Major;
            else if ((o.Kind == "arg" || o.Kind == "mkey") && Only(o.Value, n.Value, "required", "true", "false")) k = Change.Minor;
            else if (o.Kind == "ret" && Only(o.Value, n.Value, "always", "false", "true")) k = Change.Minor;
            else k = Change.Major;
            list.Add(new Diff { Class = k, Text = "changed: " + o + "  ->  " + n.Value + (o.Exp != n.Exp ? (n.Exp ? " (now experimental)" : " (no longer experimental)") : "") });
        }
        foreach (var kv in a)
        {
            if (b.ContainsKey(kv.Key)) continue;
            var n = kv.Value;
            Change k;
            string why = "";
            if (n.Exp || n.Kind == "mprobe" || n.Kind == "cmdmeta") k = Change.Info;
            else if (n.Kind == "member" && pluginTypes.Contains(DeclaringType(n.Key))) { k = Change.Major; why = " (an interface plugins implement)"; }
            else if (n.Kind == "member" && classTypes.Contains(DeclaringType(n.Key)) && Regex.IsMatch(n.Value, @"\babstract\b")) { k = Change.Major; why = " (a new abstract member)"; }
            else if (n.Kind == "mkey" && Field(n.Value, "required") == "true") { k = Change.Major; why = " (newly required)"; }
            else if (n.Kind == "arg" && Field(n.Value, "required") == "true" && b.ContainsKey(n.Key.Substring(0, n.Key.IndexOf(" arg ", StringComparison.Ordinal))))
            { k = Change.Major; why = " (newly required)"; }   // a required argument of a new command (or a new alias) is part of an addition
            else k = Change.Minor;
            list.Add(new Diff { Class = k, Text = "added: " + n + why });
        }
        return list;
    }

    static string DeclaringType(string memberKey)
    {
        int i = memberKey.IndexOf("::", StringComparison.Ordinal);
        return i > 2 ? memberKey.Substring(2, i - 2) : "";
    }

    /// <summary>The value of "name=value" in a "; "-separated entry value, or null.</summary>
    static string Field(string value, string name)
    {
        foreach (var part in value.Split(new[] { "; " }, StringSplitOptions.None))
            if (part.StartsWith(name + "=", StringComparison.Ordinal)) return part.Substring(name.Length + 1);
        return null;
    }

    /// <summary>True when the two values differ only in field <paramref name="name"/>, going from <paramref name="from"/> to <paramref name="to"/>.</summary>
    static bool Only(string before, string after, string name, string from, string to)
    {
        if (Field(before, name) != from || Field(after, name) != to) return false;
        return before.Replace(name + "=" + from, "") == after.Replace(name + "=" + to, "");
    }

    // ================================================================ AZ3 synthetic pairs

    static void Synthetic()
    {
        const string head = "# HS2Next.Api surface of Api 1.0.0\n";
        string baseText = head + string.Join("\n", new[]
        {
            "T X.A | sealed class",
            "M X.A::.ctor() | public .ctor()",
            "M X.A::Get(System.String) | public System.Int32 Get(System.String name)",
            "M X.A::Old() | public System.Void Old() [Experimental]",
            "T X.IHost | interface",
            "M X.IHost::Count | public System.Int32 Count { get; }",
            "T X.IPlug | interface implemented by plugins",
            "M X.IPlug::Run() | public System.Void Run()",
            "T X.Base | abstract class",
            "M X.Base::Step() | public abstract System.Void Step()",
        }) + "\n";
        var b = ParseText(baseText, out var hv);
        Check("AZ3", "a listing header is read", hv == "1.0.0", hv);
        Check("AZ3", "the experimental mark is read", b.Single(e => e.Key == "M X.A::Old()").Exp && !b.Single(e => e.Key == "M X.A::Get(System.String)").Exp);
        var v100 = new Ver(1, 0, 0);
        var v101 = new Ver(1, 0, 1);
        var v110 = new Ver(1, 1, 0);
        var v200 = new Ver(2, 0, 0);
        Pair("no change", baseText, baseText, Change.None, v100, true);
        Pair("a removed member", baseText, baseText.Replace("M X.A::.ctor() | public .ctor()\n", ""), Change.Major, v100, false);
        Pair("a removed member with a minor bump", baseText, baseText.Replace("M X.A::.ctor() | public .ctor()\n", ""), Change.Major, v110, false);
        Pair("a removed member with a major bump", baseText, baseText.Replace("M X.A::.ctor() | public .ctor()\n", ""), Change.Major, v200, true);
        Pair("a changed signature", baseText, baseText.Replace("System.Int32 Get(System.String name)", "System.Int64 Get(System.String name)"), Change.Major, v100, false);
        Pair("a renamed parameter", baseText, baseText.Replace("Get(System.String name)", "Get(System.String key)"), Change.Major, v100, false);
        string added = baseText + "M X.A::Put(System.String) | public System.Void Put(System.String name)\n";
        Pair("an added member without a bump", baseText, added, Change.Minor, v100, false);
        Pair("an added member with a patch bump", baseText, added, Change.Minor, v101, false);
        Pair("an added member with a minor bump", baseText, added, Change.Minor, v110, true);
        Pair("a member added to a host interface with a minor bump", baseText, baseText + "M X.IHost::Name | public System.String Name { get; }\n", Change.Minor, v110, true);
        Pair("a member added to an interface plugins implement", baseText, baseText + "M X.IPlug::Stop() | public System.Void Stop()\n", Change.Major, v110, false);
        Pair("a new abstract member of a class", baseText, baseText + "M X.Base::Reset() | public abstract System.Void Reset()\n", Change.Major, v110, false);
        Pair("an experimental member changed", baseText, baseText.Replace("System.Void Old() [Experimental]", "System.Int32 Old() [Experimental]"), Change.Info, v100, true);
        Pair("an experimental member removed", baseText, baseText.Replace("M X.A::Old() | public System.Void Old() [Experimental]\n", ""), Change.Info, v100, true);
        Pair("an experimental member added", baseText, baseText + "M X.A::New() | public System.Void New() [Experimental]\n", Change.Info, v100, true);
        Pair("a member made experimental", baseText, baseText.Replace("Get(System.String name)", "Get(System.String name) [Experimental]"), Change.Major, v110, false);
        Pair("the experimental mark removed (with a minor bump)", baseText, baseText.Replace("Old() [Experimental]", "Old()"), Change.Minor, v110, true);
        Pair("a lower version than the listing's", baseText, baseText, Change.None, new Ver(0, 9, 0), false);

        // command and manifest entries
        var cmd = new JObject
        {
            ["api"] = "1.0.0",
            ["protocol"] = new JObject { ["version"] = 1, ["errorCodes"] = new JArray("bad_args", "internal"), ["envelopeFields"] = new JArray("cmd"), ["headers"] = new JObject { ["token"] = "T" } },
            ["commands"] = new JArray(new JObject
            {
                ["name"] = "a.b", ["aliases"] = new JArray(), ["group"] = "a", ["owner"] = "core", ["since"] = "1.0.0", ["gate"] = "app", ["requires"] = new JArray(),
                ["flags"] = new JArray("readOnly"), ["allowExtraArgs"] = false,
                ["args"] = new JArray(new JObject { ["name"] = "key", ["type"] = "node", ["required"] = true }, new JObject { ["name"] = "n", ["type"] = "int", ["required"] = false, ["default"] = 1 }),
                ["returns"] = new JArray(new JObject { ["name"] = "x", ["type"] = "int", ["always"] = false }),
            }),
            ["experimental"] = new JArray("ui.x"),
        };
        Func<Action<JObject>, JObject> edit = f => { var c = (JObject)cmd.DeepClone(); f(c); return c; };
        JObject A(JObject c) => (JObject)((JArray)c["commands"])[0];
        CmdPair("an added optional argument", cmd, edit(c => ((JArray)A(c)["args"]).Add(new JObject { ["name"] = "m", ["type"] = "int", ["required"] = false })), Change.Minor);
        CmdPair("an added required argument", cmd, edit(c => ((JArray)A(c)["args"]).Add(new JObject { ["name"] = "m", ["type"] = "int", ["required"] = true })), Change.Major);
        CmdPair("a required argument made optional", cmd, edit(c => A(c)["args"][0]["required"] = false), Change.Minor);
        CmdPair("an optional argument made required", cmd, edit(c => A(c)["args"][1]["required"] = true), Change.Major);
        CmdPair("a changed default", cmd, edit(c => A(c)["args"][1]["default"] = 2), Change.Major);
        CmdPair("a changed argument type", cmd, edit(c => A(c)["args"][1]["type"] = "float"), Change.Major);
        CmdPair("a returned field now always present", cmd, edit(c => A(c)["returns"][0]["always"] = true), Change.Minor);
        CmdPair("a removed returned field", cmd, edit(c => ((JArray)A(c)["returns"]).Clear()), Change.Major);
        CmdPair("a changed gate", cmd, edit(c => A(c)["gate"] = "none"), Change.Major);
        CmdPair("an added alias", cmd, edit(c => ((JArray)A(c)["aliases"]).Add("a.old")), Change.Minor);
        CmdPair("a renamed command that keeps its old name as an alias", cmd, edit(c => { A(c)["name"] = "a.c"; A(c)["since"] = "1.1.0"; ((JArray)A(c)["aliases"]).Add("a.b"); }), Change.Minor);
        CmdPair("a renamed command without an alias", cmd, edit(c => A(c)["name"] = "a.c"), Change.Major);
        CmdPair("a changed help group", cmd, edit(c => A(c)["group"] = "b"), Change.Major);
        CmdPair("a removed alias", edit(c => ((JArray)A(c)["aliases"]).Add("a.old")), cmd, Change.Major);
        CmdPair("an added command", cmd, edit(c => ((JArray)c["commands"]).Add(new JObject { ["name"] = "a.c", ["group"] = "a", ["owner"] = "core", ["since"] = "1.1.0", ["gate"] = "app", ["flags"] = new JArray(), ["allowExtraArgs"] = false, ["args"] = new JArray(), ["returns"] = new JArray() })), Change.Minor);
        CmdPair("a removed command", cmd, edit(c => ((JArray)c["commands"]).Clear()), Change.Major);
        CmdPair("a command made experimental", cmd, edit(c => { ((JArray)c["commands"]).Clear(); ((JArray)c["experimental"]).Add("a.b"); }), Change.Major);
        CmdPair("an experimental command added", cmd, edit(c => ((JArray)c["experimental"]).Add("ui.y")), Change.Info);
        CmdPair("an experimental command removed", cmd, edit(c => ((JArray)c["experimental"]).Clear()), Change.Info);
        CmdPair("an added error code", cmd, edit(c => ((JArray)c["protocol"]["errorCodes"]).Add("new_code")), Change.Minor);
        CmdPair("a removed error code", cmd, edit(c => ((JArray)c["protocol"]["errorCodes"]).RemoveAt(0)), Change.Major);
        CmdPair("a changed protocol version", cmd, edit(c => c["protocol"]["version"] = 2), Change.Major);

        var man = new JObject
        {
            ["api"] = "1.0.0", ["fileName"] = "manifest.json", ["manifestVersion"] = 1, ["unknownKeys"] = "ignored",
            ["keys"] = new JArray(new JObject { ["name"] = "guid", ["type"] = "string", ["required"] = true }, new JObject { ["name"] = "url", ["type"] = "string", ["required"] = false }),
            ["codes"] = new JObject { ["BadManifest"] = "plugin.badManifest" }, ["states"] = new JObject { ["Loaded"] = "loaded" },
            ["probes"] = new JObject { ["guid"] = new JArray(new JObject { ["value"] = "a.b", ["valid"] = true, ["reserved"] = false }) },
        };
        Func<Action<JObject>, JObject> medit = f => { var c = (JObject)man.DeepClone(); f(c); return c; };
        ManPair("an added optional key", man, medit(c => ((JArray)c["keys"]).Add(new JObject { ["name"] = "icon", ["type"] = "string", ["required"] = false })), Change.Minor);
        ManPair("an added required key", man, medit(c => ((JArray)c["keys"]).Add(new JObject { ["name"] = "icon", ["type"] = "string", ["required"] = true })), Change.Major);
        ManPair("a key made optional", man, medit(c => c["keys"][0]["required"] = false), Change.Minor);
        ManPair("a changed rule probe", man, medit(c => c["probes"]["guid"][0]["valid"] = false), Change.Major);
        ManPair("an added rule probe", man, medit(c => ((JArray)c["probes"]["guid"]).Add(new JObject { ["value"] = "c.d", ["valid"] = true, ["reserved"] = false })), Change.Info);
        ManPair("a removed refusal code", man, medit(c => ((JObject)c["codes"]).RemoveAll()), Change.Major);
    }

    static void Pair(string what, string before, string after, Change want, Ver code, bool pass)
    {
        var d = Compare(ParseText(before, out var hv), ParseText(after, out _));
        var max = d.Count == 0 ? Change.None : d.Max(x => x.Class);
        Ver.TryParse(hv, out var listing);
        var v = Judge(max, listing, code);
        Check("AZ3", what + ": " + want + (pass ? ", passes" : ", fails") + " with code " + code, max == want && v.Pass == pass,
            max + " " + (v.Pass ? "pass" : "fail") + (d.Count > 0 ? "; " + string.Join("; ", d) : ""));
    }

    static void CmdPair(string what, JObject before, JObject after, Change want)
    {
        var d = Compare(FlattenCommands(before), FlattenCommands(after));
        var max = d.Count == 0 ? Change.None : d.Max(x => x.Class);
        Check("AZ3", "command surface, " + what + ": " + want, max == want, max + (d.Count > 0 ? "; " + string.Join("; ", d) : ""));
    }

    static void ManPair(string what, JObject before, JObject after, Change want)
    {
        var d = Compare(FlattenManifest(before), FlattenManifest(after));
        var max = d.Count == 0 ? Change.None : d.Max(x => x.Class);
        Check("AZ3", "manifest surface, " + what + ": " + want, max == want, max + (d.Count > 0 ? "; " + string.Join("; ", d) : ""));
    }

    // ================================================================ AZ4, AZ5

    static void Az4(JObject cmd)
    {
        Check("AZ4", "the Core commands register without an app (no registration error)", regErrors.Count == 0, string.Join("; ", regErrors));
        Check("AZ4", "help describes every core and bridge command (" + registered + ")", registered > 0 && helpBad.Count == 0, string.Join("; ", helpBad.Take(5)));
        var frozen = ((JArray)cmd["commands"]).Select(c => (string)c["name"]).ToList();
        var exp = ((JArray)cmd["experimental"]).Select(x => (string)x).ToList();
        Check("AZ4", "the code's commands: " + frozen.Count + " frozen + " + exp.Count + " experimental, each once", frozen.Concat(exp).Distinct(StringComparer.Ordinal).Count() == frozen.Count + exp.Count);
        string path = Path.Combine(ListingDir, CommandsFile);
        if (!File.Exists(path)) { Check("AZ4", "the command listing exists", false); return; }
        var listing = ParseJson(File.ReadAllText(path, Encoding.UTF8));
        var lf = new HashSet<string>(((JArray)listing["commands"]).Select(c => (string)c["name"]), StringComparer.Ordinal);
        var le = ((JArray)listing["experimental"]).Select(x => (string)x).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var missing = frozen.Where(n => !lf.Contains(n)).ToList();
        Check("AZ4", "the listing names every frozen core and bridge command of the code", missing.Count == 0, string.Join(", ", missing));
        if ((string)listing["api"] == "1.0.0")
            Check("AZ4", "the listing of 1.0.0 marks exactly the agreed experimental set (" + Experimental100.Length + ")",
                le.SequenceEqual(Experimental100.OrderBy(x => x, StringComparer.Ordinal)), string.Join(", ", le));
        rep.AppendLine("  experimental: " + string.Join(", ", exp));
    }

    static void Az5(Ver cur)
    {
        var root = ContentSettings.ProjectRoot;
        var files = new List<string>();
        foreach (var top in new[] { "Samples", Path.Combine("Tests", "Plugins") })
        {
            string d = Path.Combine(root, top);
            if (!Directory.Exists(d)) continue;
            foreach (var sub in Directory.GetDirectories(d))
            {
                string f = Path.Combine(sub, PluginManifest.FileName);
                if (File.Exists(f)) files.Add(f);
            }
        }
        var bad = new List<string>();
        foreach (var f in files.OrderBy(x => x, StringComparer.Ordinal))
        {
            var m = PluginManifest.Parse(File.ReadAllText(f, Encoding.UTF8), out string code, out string err, out _);
            string rel = f.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
            if (m == null) { bad.Add(rel + ": " + code + " " + err); continue; }
            if (!SemVer.ApiCompatible(m.ApiVersion, cur.Major, cur.Minor)) bad.Add(rel + ": apiVersion " + m.ApiVersion);
            if (m.MinAppVersion != null && SemVer.TryParse(m.MinAppVersion, out var need) && SemVer.TryParse(Application.version, out var app) && app.CompareTo(need) < 0)
                bad.Add(rel + ": minAppVersion " + m.MinAppVersion + " above the app's " + Application.version);
        }
        Check("AZ5", files.Count + " manifests declare an Api the host loads (" + cur.Major + "." + cur.Minor + ") and an app version it meets", files.Count > 0 && bad.Count == 0, string.Join("; ", bad));
        string old = "{\"manifestVersion\":1,\"guid\":\"a.b\",\"name\":\"n\",\"version\":\"1.0.0\",\"apiVersion\":\"0.3\",\"assemblies\":[\"P.dll\"]}";
        var om = PluginManifest.Parse(old, out _, out _, out _);
        Check("AZ5", "a 0.3 manifest parses and the host refuses its Api", om != null && !SemVer.ApiCompatible(om.ApiVersion, cur.Major, cur.Minor));
    }

    // ================================================================ the Api assemblies

    /// <summary>The surface text of one Api assembly: a header, then one line per type and member in ordinal order.</summary>
    public static string AssemblySurface(Assembly asm, string name, Ver v)
    {
        var lines = new List<string>();
        var plugin = new HashSet<string>(PluginImplemented, StringComparer.Ordinal);
        foreach (var t in asm.GetExportedTypes())
        {
            string tn = TypeName(t);
            bool texp = Experimental(t);
            lines.Add("T " + tn + " | " + TypeDecl(t) + Marks(t, texp) + (t.IsInterface && plugin.Contains(tn) ? " implemented by plugins" : ""));
            if (!IsDelegate(t) && !t.IsEnum)
            {
                // the interfaces the type declares (those a framework base class brings, such as an attribute's or an
                // exception's runtime interfaces, belong to the base, which the type line names)
                var inherited = new HashSet<Type>(t.BaseType?.GetInterfaces() ?? Type.EmptyTypes);
                foreach (var i in t.GetInterfaces())
                    if ((i.IsPublic || i.IsNestedPublic) && !inherited.Contains(i)) lines.Add("I " + tn + " : " + TypeName(i) + (texp ? " | [Experimental]" : ""));
            }
            foreach (var m in Members(t)) lines.Add(m);
        }
        lines.Sort(StringComparer.Ordinal);
        var sb = new StringBuilder();
        sb.Append("# ").Append(name).Append(" surface of Api ").Append(v).Append('\n');
        sb.Append("# Generated by ApiSurfaceTest -write: every public type and member, one line each in ordinal order. \"T\" a type, \"I\" an\n");
        sb.Append("# interface it implements, \"M\" a member (key before \" | \", declaration after). [Experimental] entries are outside the\n");
        sb.Append("# compatibility promise; the values of ApiVersion's constants are the version in this header.\n");
        foreach (var l in lines) sb.Append(l).Append('\n');
        return sb.ToString();
    }

    static string TextHeader(string text)
    {
        var m = Regex.Match(text, @"^# \S+ surface of Api (\S+)");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>Entries of a surface text (lines starting with '#' and empty lines skipped). <paramref name="version"/>: the header's.</summary>
    public static List<Entry> ParseText(string text, out string version)
    {
        version = TextHeader(text);
        var list = new List<Entry>();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.Length == 0 || raw[0] == '#') continue;
            int i = raw.IndexOf(" | ", StringComparison.Ordinal);
            string key = i < 0 ? raw : raw.Substring(0, i), value = i < 0 ? "" : raw.Substring(i + 3);
            var e = new Entry { Key = key, Value = value, Exp = value.Contains("[Experimental]") };
            e.Kind = key.StartsWith("T ", StringComparison.Ordinal) ? "type" : key.StartsWith("I ", StringComparison.Ordinal) ? "iface" : "member";
            list.Add(e);
        }
        return list;
    }

    static bool IsDelegate(Type t) => typeof(MulticastDelegate).IsAssignableFrom(t.BaseType) && t != typeof(MulticastDelegate);

    static bool Experimental(MemberInfo m)
    {
        for (var x = m; x != null; x = x.DeclaringType)
            if (x.IsDefined(typeof(HS2Next.Api.ExperimentalAttribute), false)) return true;
        return false;
    }

    static string Marks(MemberInfo m, bool exp)
    {
        var sb = new StringBuilder();
        if (m.IsDefined(typeof(ObsoleteAttribute), false)) sb.Append(" [Obsolete]");
        if (m is Type t && t.IsEnum && t.IsDefined(typeof(FlagsAttribute), false)) sb.Append(" [Flags]");
        if (exp) sb.Append(" [Experimental]");
        return sb.ToString();
    }

    static string TypeDecl(Type t)
    {
        if (IsDelegate(t))
        {
            var inv = t.GetMethod("Invoke");
            return "delegate " + TypeName(inv.ReturnType) + " (" + Params(inv) + ")" + Constraints(t.GetGenericArguments());
        }
        if (t.IsEnum) return "enum " + TypeName(Enum.GetUnderlyingType(t));
        if (t.IsInterface) return "interface" + Constraints(t.GetGenericArguments());
        string s;
        if (t.IsValueType) s = (t.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute") ? "readonly " : "") + "struct";
        else if (t.IsAbstract && t.IsSealed) s = "static class";
        else if (t.IsAbstract) s = "abstract class";
        else if (t.IsSealed) s = "sealed class";
        else s = "class";
        if (!t.IsValueType && t.BaseType != null && t.BaseType != typeof(object)) s += " : " + TypeName(t.BaseType);
        return s + Constraints(t.GetGenericArguments());
    }

    static IEnumerable<string> Members(Type t)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        string tn = TypeName(t);
        if (IsDelegate(t)) yield break;
        if (t.IsEnum)
        {
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
                yield return "M " + tn + "::" + f.Name + " | = " + Convert.ToString(f.GetRawConstantValue(), CultureInfo.InvariantCulture) + Marks(f, Experimental(f));
            yield break;
        }
        bool protectedVisible = !t.IsSealed && !t.IsValueType && !t.IsInterface;
        bool Visible(MethodBase m) => m != null && (m.IsPublic || (protectedVisible && (m.IsFamily || m.IsFamilyOrAssembly)));
        string Access(MethodBase m) => m.IsPublic ? "public" : "protected";
        foreach (var m in t.GetMembers(all))
        {
            if (m.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)) continue;
            bool exp = Experimental(m);
            switch (m)
            {
                case ConstructorInfo c:
                    if (c.IsStatic || !Visible(c)) break;
                    yield return "M " + tn + "::.ctor(" + ParamTypes(c) + ") | " + Access(c) + " .ctor(" + Params(c) + ")" + Marks(c, exp);
                    break;
                case MethodInfo mi:
                    if (!Visible(mi) || (mi.IsSpecialName && !mi.Name.StartsWith("op_", StringComparison.Ordinal))) break;
                    var bd = mi.GetBaseDefinition();
                    if (bd.DeclaringType != mi.DeclaringType && bd.DeclaringType.Assembly != t.Assembly) break;   // an override of a framework member (ToString, Equals, ...)
                    string gen = mi.IsGenericMethodDefinition ? "<" + string.Join(", ", mi.GetGenericArguments().Select(g => g.Name)) + ">" : "";
                    string arity = mi.IsGenericMethodDefinition ? "`" + mi.GetGenericArguments().Length : "";
                    yield return "M " + tn + "::" + mi.Name + arity + "(" + ParamTypes(mi) + ") | " + Access(mi) + " " + Modifiers(mi, t) + TypeName(mi.ReturnType) + " "
                        + mi.Name + gen + "(" + Params(mi) + ")" + (mi.IsGenericMethodDefinition ? Constraints(mi.GetGenericArguments()) : "") + Marks(mi, exp);
                    break;
                case PropertyInfo p:
                {
                    var g = p.GetGetMethod(true);
                    var s = p.GetSetMethod(true);
                    bool gv = Visible(g), sv = Visible(s);
                    if (!gv && !sv) break;
                    var any = gv ? g : s;
                    var idx = p.GetIndexParameters();
                    string key = idx.Length > 0 ? "Item[" + string.Join(", ", idx.Select(x => TypeName(x.ParameterType))) + "]" : p.Name;
                    string name = idx.Length > 0 ? "this[" + string.Join(", ", idx.Select(Param)) + "]" : p.Name;
                    string acc = Access(any);
                    string body = "{ " + (gv ? (Access(g) != acc ? Access(g) + " " : "") + "get; " : "") + (sv ? (Access(s) != acc ? Access(s) + " " : "") + "set; " : "") + "}";
                    yield return "M " + tn + "::" + key + " | " + acc + " " + Modifiers(any, t) + TypeName(p.PropertyType) + " " + name + " " + body + Marks(p, exp);
                    break;
                }
                case FieldInfo f:
                {
                    if (f.IsSpecialName) break;
                    bool fv = f.IsPublic || (protectedVisible && (f.IsFamily || f.IsFamilyOrAssembly));
                    if (!fv) break;
                    string acc = f.IsPublic ? "public" : "protected";
                    string key = tn + "::" + f.Name;
                    string decl;
                    if (f.IsLiteral)
                        decl = acc + " const " + TypeName(f.FieldType) + " " + f.Name + " = " + (VersionConstants.Contains(key) ? "(the Api version)" : Literal(f.GetRawConstantValue(), f.FieldType));
                    else
                        decl = acc + " " + (f.IsStatic ? "static " : "") + (f.IsInitOnly ? "readonly " : "") + TypeName(f.FieldType) + " " + f.Name;
                    yield return "M " + key + " | " + decl + Marks(f, exp);
                    break;
                }
                case EventInfo ev:
                {
                    var add = ev.GetAddMethod(true);
                    if (!Visible(add)) break;
                    yield return "M " + tn + "::" + ev.Name + " | " + Access(add) + " " + Modifiers(add, t) + "event " + TypeName(ev.EventHandlerType) + " " + ev.Name + Marks(ev, exp);
                    break;
                }
            }
        }
    }

    static string Modifiers(MethodInfo m, Type t)
    {
        if (m.IsStatic) return "static ";
        if (t.IsInterface) return "";
        bool isOverride = m.IsVirtual && m.GetBaseDefinition().DeclaringType != m.DeclaringType;
        if (m.IsAbstract) return "abstract ";
        if (isOverride) return m.IsFinal ? "sealed override " : "override ";
        if (m.IsVirtual && !m.IsFinal) return "virtual ";
        return "";
    }

    static string ParamTypes(MethodBase m) => string.Join(", ", m.GetParameters().Select(p =>
        (p.ParameterType.IsByRef ? (p.IsOut ? "out " : "ref ") : "") + TypeName(p.ParameterType)));

    static string Params(MethodBase m)
    {
        bool ext = m.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false);
        var ps = m.GetParameters();
        return string.Join(", ", ps.Select((p, i) => (ext && i == 0 ? "this " : "") + Param(p)));
    }

    static string Param(ParameterInfo p)
    {
        string mod = "";
        if (p.ParameterType.IsByRef)
            mod = p.IsOut ? "out " : p.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute") ? "in " : "ref ";
        else if (p.IsDefined(typeof(ParamArrayAttribute), false)) mod = "params ";
        string s = mod + TypeName(p.ParameterType) + " " + p.Name;
        object def = null;
        bool has;
        try { has = p.HasDefaultValue; if (has) def = p.DefaultValue; }
        catch (Exception) { has = p.IsOptional; def = has ? p.RawDefaultValue : null; }
        if (has) s += " = " + Literal(def, p.ParameterType);
        return s;
    }

    static string Literal(object v, Type t)
    {
        if (v == null || v is DBNull || v == Missing.Value) return "null";
        if (t != null && t.IsByRef) t = t.GetElementType();
        var u = t != null ? Nullable.GetUnderlyingType(t) ?? t : null;
        if (u != null && u.IsEnum && !(v is Enum)) v = Enum.ToObject(u, v);
        switch (v)
        {
            case string s: return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"";
            case bool b: return b ? "true" : "false";
            case char c: return "'" + c + "'";
            case Enum e: return TypeName(e.GetType()) + "." + e.ToString().Replace(", ", " | " + TypeName(e.GetType()) + ".");
            case float f: return f.ToString("R", CultureInfo.InvariantCulture) + "f";
            case double d: return d.ToString("R", CultureInfo.InvariantCulture);
            case IFormattable fm: return fm.ToString(null, CultureInfo.InvariantCulture);
            default: return v.ToString();
        }
    }

    static string Constraints(Type[] gps)
    {
        var sb = new StringBuilder();
        foreach (var gp in gps)
        {
            if (!gp.IsGenericParameter) continue;
            var parts = new List<string>();
            var a = gp.GenericParameterAttributes;
            bool isStruct = (a & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0;
            if ((a & GenericParameterAttributes.ReferenceTypeConstraint) != 0) parts.Add("class");
            if (isStruct) parts.Add("struct");
            foreach (var c in gp.GetGenericParameterConstraints().OrderBy(TypeName, StringComparer.Ordinal))
                if (!(isStruct && c == typeof(ValueType))) parts.Add(TypeName(c));
            if ((a & GenericParameterAttributes.DefaultConstructorConstraint) != 0 && !isStruct) parts.Add("new()");
            if (parts.Count > 0) sb.Append(" where ").Append(gp.Name).Append(" : ").Append(string.Join(", ", parts));
        }
        return sb.ToString();
    }

    /// <summary>A namespace-qualified C#-like name: nested types with '.', generic arguments in angle brackets, arrays, by-ref element types.</summary>
    public static string TypeName(Type t)
    {
        if (t == null) return "?";
        if (t.IsByRef || t.IsPointer) return TypeName(t.GetElementType()) + (t.IsPointer ? "*" : "");
        if (t.IsArray) return TypeName(t.GetElementType()) + "[" + new string(',', t.GetArrayRank() - 1) + "]";
        if (t.IsGenericParameter) return VarianceOf(t) + t.Name;
        var args = t.IsGenericType ? t.GetGenericArguments() : Type.EmptyTypes;
        string own = Regex.Replace(t.Name, "`\\d+$", "");
        string prefix;
        int parentArgs = 0;
        if (t.IsNested)
        {
            var d = t.DeclaringType;
            parentArgs = d.IsGenericTypeDefinition ? d.GetGenericArguments().Length : 0;
            prefix = (parentArgs > 0 && !t.IsGenericTypeDefinition ? TypeName(d.MakeGenericType(args.Take(parentArgs).ToArray())) : TypeName(d)) + ".";
        }
        else prefix = string.IsNullOrEmpty(t.Namespace) ? "" : t.Namespace + ".";
        var mine = args.Skip(parentArgs).ToArray();
        return prefix + own + (mine.Length > 0 ? "<" + string.Join(", ", mine.Select(TypeName)) + ">" : "");
    }

    static string VarianceOf(Type gp)
    {
        if (gp.DeclaringMethod != null) return "";
        var a = gp.GenericParameterAttributes & GenericParameterAttributes.VarianceMask;
        return a == GenericParameterAttributes.Covariant ? "out " : a == GenericParameterAttributes.Contravariant ? "in " : "";
    }

    // ================================================================ the commands

    /// <summary>
    /// The command surface: a registry of the bridge's own commands and every Core command (registered without an app; no
    /// handler runs), each described by help and reduced to its contract.
    /// </summary>
    public static JObject CommandSurface(Ver v, out List<string> notes)
    {
        notes = new List<string>();
        regErrors = new List<string>();
        helpBad = new List<string>();
        registered = 0;
        var server = new HS2Next.Bridge.BridgeServer(new HS2Next.Bridge.BridgeOptions { WriteEndpointFile = false });
        try
        {
            server.Start(false);
            HS2Next.App.Commands.CoreCommands.Register(server.Commands, null);
            regErrors.AddRange(HS2Next.App.Commands.CoreCommands.RegistrationErrors);
            var specs = server.Commands.Specs.Where(s => s.Owner == BridgeOwners.Core || s.Owner == BridgeOwners.Bridge).Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            int others = server.Commands.Specs.Count - specs.Count;
            if (others > 0) notes.Add(others + " commands of other owners left out");
            var frozen = new JArray();
            var exp = new List<string>();
            registered = specs.Count;
            foreach (var n in specs)
            {
                var reply = ParseJson(server.RunNow(new JObject { ["cmd"] = "help", ["name"] = n }.ToString(Formatting.None)));
                if (reply.Value<bool?>("ok") != true || !(reply["command"] is JObject c)) { helpBad.Add(n + ": " + reply.ToString(Formatting.None)); continue; }
                var flags = (c["flags"] as JArray ?? new JArray()).Select(x => (string)x).ToList();
                if (flags.Contains("experimental")) { exp.Add(n); continue; }
                frozen.Add(CommandContract(c, flags));
            }
            return new JObject
            {
                ["api"] = v.ToString(),
                ["about"] = "The bridge contract of this Api version: the protocol and every command not marked experimental (help texts left out). "
                    + "Experimental commands are listed by name; they may change in any minor version. Generated by ApiSurfaceTest -write.",
                ["protocol"] = new JObject
                {
                    ["version"] = BridgeProtocol.Version,
                    ["errorCodes"] = new JArray(BridgeCodes.All.Cast<object>().ToArray()),
                    ["envelopeFields"] = new JArray(EnvelopeFields.All.Cast<object>().ToArray()),
                    ["headers"] = new JObject { ["token"] = BridgeProtocol.TokenHeader, ["bridge"] = BridgeProtocol.BridgeHeader },
                },
                ["commands"] = frozen,
                ["experimental"] = new JArray(exp.Cast<object>().ToArray()),
            };
        }
        finally
        {
            try { server.Dispose(); } catch (Exception e) { notes.Add("bridge dispose threw: " + e.Message); }
        }
    }

    static List<string> regErrors = new List<string>(), helpBad = new List<string>();
    static int registered;

    /// <summary>JSON text as an object, strings kept as written (no date conversion), numbers with a fraction as double.</summary>
    public static JObject ParseJson(string text)
    {
        using (var r = new JsonTextReader(new StringReader(text ?? "")) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double })
            return (JObject)JToken.ReadFrom(r);
    }

    static JObject CommandContract(JObject c, List<string> flags)
    {
        var args = new JArray();
        foreach (var a in c["args"] as JArray ?? new JArray())
        {
            var o = new JObject { ["name"] = a["name"], ["type"] = a["type"], ["required"] = a["required"] };
            if (a["default"] != null) o["default"] = a["default"];
            args.Add(o);
        }
        var rets = new JArray();
        foreach (var r in c["returns"] as JArray ?? new JArray())
            rets.Add(new JObject { ["name"] = r["name"], ["type"] = r["type"], ["always"] = r["always"] });
        return new JObject
        {
            ["name"] = c["name"],
            ["aliases"] = new JArray((c["aliases"] as JArray ?? new JArray()).Select(x => (string)x).OrderBy(x => x, StringComparer.Ordinal).Cast<object>().ToArray()),
            ["group"] = c["group"],
            ["owner"] = c["owner"],
            ["since"] = c["since"],
            ["gate"] = c["gate"],
            ["requires"] = c["requires"] ?? new JArray(),
            ["flags"] = new JArray(flags.Cast<object>().ToArray()),
            ["allowExtraArgs"] = c["allowExtraArgs"],
            ["args"] = args,
            ["returns"] = rets,
        };
    }

    static string J(JToken t) => t == null ? "null" : t.ToString(Formatting.None);

    /// <summary>Keyed entries of a command surface (the listing's JSON or the code's).</summary>
    public static List<Entry> FlattenCommands(JObject s)
    {
        var l = new List<Entry>();
        var p = (JObject)s["protocol"];
        l.Add(new Entry { Kind = "proto", Key = "protocol version", Value = J(p["version"]) });
        foreach (var c in (JArray)p["errorCodes"]) l.Add(new Entry { Kind = "proto", Key = "protocol code " + (string)c });
        foreach (var c in (JArray)p["envelopeFields"]) l.Add(new Entry { Kind = "proto", Key = "protocol envelope " + (string)c });
        foreach (var h in (JObject)p["headers"]) l.Add(new Entry { Kind = "proto", Key = "protocol header " + h.Key, Value = J(h.Value) });
        foreach (JObject c in (JArray)s["commands"])
        {
            string n = (string)c["name"];
            var aliases = (c["aliases"] as JArray ?? new JArray()).Select(x => (string)x).ToList();
            l.Add(new Entry { Kind = "cmdmeta", Key = "command " + n + " group", Value = (string)c["group"] });
            l.Add(new Entry { Kind = "cmdmeta", Key = "command " + n + " since", Value = (string)c["since"] });
            foreach (var a in aliases) l.Add(new Entry { Kind = "alias", Key = "command " + n + " alias " + a });
            // the contract under the name and under every alias: a command renamed with its old name kept as an alias still
            // answers every request of the old name (a minor change); a removed alias removes that contract (major)
            string contract = "owner=" + (string)c["owner"] + "; gate=" + (string)c["gate"]
                + "; flags=" + string.Join(",", (c["flags"] as JArray ?? new JArray()).Select(x => (string)x)) + "; requires=" + string.Join(",", (c["requires"] as JArray ?? new JArray()).Select(x => (string)x))
                + "; allowExtraArgs=" + J(c["allowExtraArgs"]);
            foreach (var name in new[] { n }.Concat(aliases))
            {
                l.Add(new Entry { Kind = "cmd", Key = "command " + name, Value = contract });
                foreach (var a in c["args"] as JArray ?? new JArray())
                    l.Add(new Entry { Kind = "arg", Key = "command " + name + " arg " + (string)a["name"], Value = "type=" + (string)a["type"] + "; required=" + J(a["required"]) + "; default=" + J(a["default"]) });
                foreach (var r in c["returns"] as JArray ?? new JArray())
                    l.Add(new Entry { Kind = "ret", Key = "command " + name + " return " + (string)r["name"], Value = "type=" + (string)r["type"] + "; always=" + J(r["always"]) });
            }
        }
        foreach (var x in s["experimental"] as JArray ?? new JArray()) l.Add(new Entry { Kind = "exp", Key = "command " + (string)x, Value = "experimental", Exp = true });
        return l;
    }

    static string JsonHeader(string text)
    {
        try { return (string)ParseJson(text)["api"]; }
        catch (Exception) { return null; }
    }

    static string JsonText(JObject o)
    {
        var sw = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        using (var w = new JsonTextWriter(sw) { Formatting = Formatting.Indented, Indentation = 2 })
            o.WriteTo(w);
        return sw.ToString().Replace("\r\n", "\n") + "\n";
    }

    // ================================================================ the plugin manifest

    static readonly string[] GuidProbes =
    {
        "a.b", "a-b_c.D9", "stp3.sample.sequencer", "a." + new string('x', 126), "a." + new string('x', 127), "nodot", ".a.b", "a b.c", "a/b.c",
        "core", "bridge", "Core.x", "bridge.x", "hs2.x", "HS2.kkex", "hs2next.a", "HS2Next.Thing", "illusion.studio", "hs2x.a",
    };
    static readonly string[] PrefixProbes = { "seq.", "seq.track.", "a1.", "aB.", "Seq.", "seq", "seq..", "1a.", ".seq.", "", "a" + new string('b', 62) + ".", "a" + new string('b', 63) + "." };
    static readonly string[] SemverProbes = { "1.0.0", "0.1.0", "1.0.0-beta.2", "1.0.0-alpha", "1.2.3+build.5", "1.0", "01.0.0", "v1.0.0", "1.0.0.0", "1.0.0-", "" };
    static readonly string[] ApiProbes = { "0.3", "1.0", "1.1", "1.2", "2.0", "1", "1.0.0", "x.y" };
    static readonly int[][] HostProbes = { new[] { 0, 3 }, new[] { 1, 0 }, new[] { 1, 1 }, new[] { 2, 0 } };

    /// <summary>The manifest schema as the parser behaves: keys by probing a full valid manifest, codes, states, rule probes.</summary>
    public static JObject ManifestSurface(Ver v)
    {
        var full = new JObject
        {
            ["manifestVersion"] = 1, ["guid"] = "probe.plugin", ["name"] = "Probe", ["version"] = "1.0.0", ["apiVersion"] = "1.0",
            ["assemblies"] = new JArray("Probe.dll"), ["entryType"] = "Probe.Entry", ["commandPrefixes"] = new JArray("probe."),
            ["dependencies"] = new JArray(new JObject { ["guid"] = "probe.other", ["minVersion"] = "1.0.0" }), ["loadAfter"] = new JArray("probe.after"),
            ["loadBefore"] = new JArray("probe.before"), ["minAppVersion"] = "1.0.0", ["description"] = "d", ["author"] = "a", ["license"] = "CC0-1.0", ["url"] = "u",
        };
        bool Parses(JObject o) => PluginManifest.Parse(o.ToString(Formatting.None), out _, out _, out _) != null;
        if (!Parses(full)) throw new Exception("the probe manifest is refused: " + Refusal(full));
        var known = (string[])typeof(PluginManifest).GetField("Known", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) ?? new string[0];
        var keys = new JArray();
        foreach (var k in known.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (full[k] == null) throw new Exception("the probe manifest lacks the key " + k);
            var without = (JObject)full.DeepClone();
            without.Remove(k);
            var wrong = (JObject)full.DeepClone();
            wrong[k] = new JValue(true);
            keys.Add(new JObject { ["name"] = k, ["type"] = Parses(wrong) ? "any" : Kind(full[k]), ["required"] = !Parses(without) });
        }
        foreach (var k in new[] { "guid", "minVersion" })
        {
            var without = (JObject)full.DeepClone();
            ((JObject)without["dependencies"][0]).Remove(k);
            var wrong = (JObject)full.DeepClone();
            wrong["dependencies"][0][k] = new JValue(true);
            keys.Add(new JObject { ["name"] = "dependencies[]." + k, ["type"] = Parses(wrong) ? "any" : Kind(full["dependencies"][0][k]), ["required"] = !Parses(without) });
        }
        var extra = (JObject)full.DeepClone();
        extra["zzUnknownMember"] = new JObject { ["x"] = 1 };
        var nulls = (JObject)full.DeepClone();
        foreach (var k in new[] { "entryType", "commandPrefixes", "dependencies", "loadAfter", "loadBefore", "minAppVersion", "description", "author", "license", "url" })
            nulls[k] = JValue.CreateNull();
        var bom = "\uFEFF" + full.ToString(Formatting.None);

        var guid = new JArray();
        foreach (var g in GuidProbes)
            guid.Add(new JObject { ["value"] = g, ["valid"] = PluginManifest.IsValidGuid(g), ["reserved"] = PluginManifest.IsReservedGuid(g) });
        var prefix = new JArray();
        foreach (var p in PrefixProbes) prefix.Add(new JObject { ["value"] = p, ["valid"] = PluginManifest.IsValidPrefix(p) });
        var semver = new JArray();
        foreach (var s in SemverProbes) semver.Add(new JObject { ["value"] = s, ["valid"] = SemVer.TryParse(s, out _) });
        var order = new JArray();
        string[] chain = { "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.1.0", "2.0.0" };
        for (int i = 0; i + 1 < chain.Length; i++)
        {
            SemVer.TryParse(chain[i], out var a);
            SemVer.TryParse(chain[i + 1], out var b);
            order.Add(new JObject { ["value"] = chain[i] + " < " + chain[i + 1], ["valid"] = a != null && b != null && a.CompareTo(b) < 0 });
        }
        var api = new JArray();
        foreach (var pa in ApiProbes)
            foreach (var h in HostProbes)
                api.Add(new JObject { ["value"] = pa + " on " + h[0] + "." + h[1], ["valid"] = SemVer.ApiCompatible(pa, h[0], h[1]) });

        return new JObject
        {
            ["api"] = v.ToString(),
            ["about"] = "The plugin manifest schema of this Api version as the loader reads it: keys with their JSON types and whether they are "
                + "required, the refusal and information codes, the plugin states, and fixed probes of the GUID, command prefix, semver and Api "
                + "rules (\"valid\": the rule accepts the value). Generated by ApiSurfaceTest -write.",
            ["fileName"] = PluginManifest.FileName,
            ["manifestVersion"] = PluginManifest.CurrentManifestVersion,
            ["unknownKeys"] = Parses(extra) ? "ignored" : "refused",
            ["nullOptionalKeys"] = Parses(nulls) ? "absent" : "refused",
            ["byteOrderMark"] = PluginManifest.Parse(bom, out _, out _, out _) != null ? "accepted" : "refused",
            ["keys"] = keys,
            ["codes"] = Constants(typeof(PluginCodes)),
            ["states"] = Constants(typeof(PluginStates)),
            ["probes"] = new JObject { ["guid"] = guid, ["commandPrefix"] = prefix, ["semver"] = semver, ["semverOrder"] = order, ["apiOnHost"] = api },
        };
    }

    static string Refusal(JObject o)
    {
        PluginManifest.Parse(o.ToString(Formatting.None), out var code, out var err, out _);
        return code + " " + err;
    }

    static string Kind(JToken t)
    {
        switch (t.Type)
        {
            case JTokenType.Integer: return "integer";
            case JTokenType.String: return "string";
            case JTokenType.Boolean: return "boolean";
            case JTokenType.Object: return "object";
            case JTokenType.Array:
                var first = t.First;
                return "array<" + (first == null ? "any" : Kind(first)) + ">";
            default: return t.Type.ToString().ToLowerInvariant();
        }
    }

    static JObject Constants(Type t)
    {
        var o = new JObject();
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).OrderBy(f => f.Name, StringComparer.Ordinal))
            o[f.Name] = (string)f.GetRawConstantValue();
        return o;
    }

    /// <summary>Keyed entries of a manifest surface (the listing's JSON or the code's).</summary>
    public static List<Entry> FlattenManifest(JObject s)
    {
        var l = new List<Entry>();
        foreach (var k in new[] { "fileName", "manifestVersion", "unknownKeys", "nullOptionalKeys", "byteOrderMark" })
            if (s[k] != null) l.Add(new Entry { Kind = "mcode", Key = "manifest " + k, Value = J(s[k]) });
        foreach (var k in s["keys"] as JArray ?? new JArray())
            l.Add(new Entry { Kind = "mkey", Key = "manifest key " + (string)k["name"], Value = "type=" + (string)k["type"] + "; required=" + J(k["required"]) });
        foreach (var part in new[] { "codes", "states" })
            foreach (var c in s[part] as JObject ?? new JObject())
                l.Add(new Entry { Kind = "mcode", Key = "manifest " + part + " " + c.Key, Value = J(c.Value) });
        foreach (var g in s["probes"] as JObject ?? new JObject())
            foreach (var p in g.Value as JArray ?? new JArray())
            {
                var rest = ((JObject)p).Properties().Where(x => x.Name != "value").Select(x => x.Name + "=" + J(x.Value));
                l.Add(new Entry { Kind = "mprobe", Key = "manifest probe " + g.Key + " " + J(p["value"]), Value = string.Join("; ", rest) });
            }
        return l;
    }
}

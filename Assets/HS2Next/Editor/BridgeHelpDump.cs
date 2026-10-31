// The bridge's command reference and the proof that a change of the command layer leaves every reply as it was.
// A test host on the reference install's lists answers `help` and `help {name}` for every registered command; the replies go
// to <out>/help_dump.json in a canonical form (object members sorted, commands sorted by name, one command per line, no clock
// values) with the HS2-parity names (CoreCommands.ParityNames, when it exists) as `parity`. Tools/gen_commands_md.py writes
// Reference/COMMANDS.md from it.
// With -corpus the argument corpus follows (<out>/help_corpus.json): on a fixed scene (one character, one item, one light,
// one camera, one route) every registered command is sent its base request (its first example, or its required arguments
// with plain values) and, per declared argument, the base request without the argument (when the base has it), with null,
// with each other JSON kind and with short arrays, and for node arguments with the scene's nodes of every kind, a key prefix
// and key arrays. Error replies are kept whole without the request id; successful ones as their member names and warnings.
// The corpus runs twice on fresh hosts with the same id seed; both texts must be equal. Commands that capture or save images,
// replace or save the scene, load a map, undo, delete nodes, report the process or end it are left out; file writers get a
// name and a path that the write guard refuses, and those two arguments are not varied.
// Run: -executeMethod BridgeHelpDump.Run [-corpus] [-out <dir>] (headless, -nographics, edit mode). Report
// <out>/help_dump_report.txt, first line "SUMMARY: ..."; EditorApplication.Exit(1) on a failure.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HS2Next.Api.Bridge;
using HS2Next.App;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using Debug = UnityEngine.Debug;

public static class BridgeHelpDump
{
    public const string DumpFile = "help_dump.json", CorpusFile = "help_corpus.json", ReportFile = "help_dump_report.txt";

    /// <summary>Commands the corpus leaves out (captures and image files, scene replacement and saving, map loads, undo, node
    /// deletion, process reports and the process end).</summary>
    public static readonly HashSet<string> CorpusSkip = new HashSet<string>(StringComparer.Ordinal)
    {
        "app.quit", "batch", "bridge.version", "capture", "delete", "help", "log.tail", "map.load", "ping", "plugin.list", "redo",
        "scene.images", "scene.load", "scene.new", "scene.save", "screenshot", "ui.screenshot", "undo",
    };

    /// <summary>Commands sent after all others (their arguments change process-wide options).</summary>
    static readonly string[] CorpusLast = { "app.options" };

    /// <summary>Base arguments that replace an example's (an example that would load a card each time).</summary>
    static readonly Dictionary<string, JObject> BaseOverrides = new Dictionary<string, JObject>(StringComparer.Ordinal)
    {
        ["chara.load"] = new JObject { ["path"] = "zz_missing.png", ["sex"] = "female" },
    };

    /// <summary>The values every argument is sent (JSON).</summary>
    static readonly string[] Values = { "null", "\"zz\"", "7", "0.5", "true", "[]", "{}", "[\"zz\"]", "[7]", "[1,2]", "[1,\"a\",3]" };

    const ulong Seed = 0x5eed0c0de51UL;
    static readonly Regex Placeholder = new Regex(@"^<[^<>]*>$");

    public static void Run()
    {
        string outDir = Arg("-out") ?? BridgeCoreTest.OutDir;
        bool corpus = Environment.GetCommandLineArgs().Contains("-corpus");
        Directory.CreateDirectory(outDir);
        var rep = new StringBuilder();
        var sw = Stopwatch.StartNew();
        int fails = 0;
        string summary = "";
        var c = new BridgeCoreTest.Ctx { Id = "dump" };
        try
        {
            var head = new StringBuilder();
            c.Services = SceneRuntimeTest.NewServices(head);
            c.Targets = TestTargets.Load();
            c.FemaleCard = TestTargets.Abs(c.Targets.visual.female);
            c.FemaleRel = BridgeCoreTest.RelTo(c.FemaleCard, Path.Combine(TestTargets.UserData, "chara", "female"));
            rep.Append(head);

            // help dump
            c.BeginCheck();
            int count;
            string dump;
            try { dump = Dump(c, rep, ref fails, out count); }
            finally { c.EndCheck(); }
            string dumpPath = Path.Combine(outDir, DumpFile);
            File.WriteAllText(dumpPath, dump, new UTF8Encoding(false));
            string dumpSha = Sha(dump);
            summary = $"help dump {count} commands (sha256 {dumpSha})";
            rep.AppendLine($"help dump: {dumpPath}, {count} commands, {Encoding.UTF8.GetByteCount(dump)} bytes, sha256 {dumpSha}");

            if (corpus)
            {
                var stats1 = new Stats();
                c.BeginCheck();
                string t1;
                try { t1 = Corpus(c, stats1); }
                finally { c.EndCheck(); }
                var stats2 = new Stats();
                c.BeginCheck();
                string t2;
                try { t2 = Corpus(c, stats2); }
                finally { c.EndCheck(); }
                string corpusPath = Path.Combine(outDir, CorpusFile);
                File.WriteAllText(corpusPath, t1, new UTF8Encoding(false));
                string sha1 = Sha(t1), sha2 = Sha(t2);
                bool same = t1 == t2;
                if (!same)
                {
                    fails++;
                    var a = t1.Split('\n');
                    var b = t2.Split('\n');
                    int i = 0;
                    while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
                    rep.AppendLine("FAIL corpus not deterministic: first difference at line " + (i + 1));
                    rep.AppendLine("  run 1: " + (i < a.Length ? Short(a[i]) : "(end)"));
                    rep.AppendLine("  run 2: " + (i < b.Length ? Short(b[i]) : "(end)"));
                    File.WriteAllText(Path.Combine(outDir, "help_corpus_run2.json"), t2, new UTF8Encoding(false));
                }
                summary += $"; corpus {stats1.Requests} requests ({stats1.Errors} errors, {stats1.Oks} ok, {stats1.Commands} commands, sha256 {sha1}), "
                    + (same ? "deterministic" : "NOT deterministic (run 2 sha256 " + sha2 + ")");
                rep.AppendLine($"corpus: {corpusPath}, {stats1.Requests} requests, {stats1.Errors} errors, {stats1.Oks} ok, {stats1.Commands} commands, "
                    + $"{Encoding.UTF8.GetByteCount(t1)} bytes, sha256 {sha1}; run 2 sha256 {sha2}; {stats1.Seconds:F1} s + {stats2.Seconds:F1} s");
                rep.AppendLine("codes: " + string.Join(", ", stats1.Codes.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + " " + k.Value)));
                rep.AppendLine("skipped commands: " + string.Join(" ", CorpusSkip.OrderBy(x => x, StringComparer.Ordinal)));
                rep.AppendLine("requests per command:");
                foreach (var kv in stats1.PerCommand.OrderBy(k => k.Key, StringComparer.Ordinal))
                    rep.AppendLine("  " + kv.Key + " " + kv.Value);
            }
        }
        catch (Exception e)
        {
            fails++;
            rep.AppendLine("FAIL exception: " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
        }
        finally { c.EndCheck(); }
        string head1 = $"SUMMARY: {(fails == 0 ? "PASS" : fails + " FAIL")}; {summary}; {sw.Elapsed.TotalSeconds:F0} s";
        rep.Insert(0, head1 + "\n");
        File.WriteAllText(Path.Combine(outDir, ReportFile), rep.ToString(), new UTF8Encoding(false));
        c.Services?.Catalog?.UnloadAll();
        Debug.Log("BRIDGE HELP DUMP DONE: " + head1);
        if (fails > 0) EditorApplication.Exit(1);
    }

    // ---------------------------------------------------------------- help dump

    static string Dump(BridgeCoreTest.Ctx c, StringBuilder rep, ref int fails, out int count)
    {
        var app = c.Host(Seed);
        var list = c.Call(app, "{\"cmd\":\"help\"}");
        if (list.Value<bool?>("ok") != true || !(list["commands"] is JArray cmds)) throw new Exception("help failed: " + c.LastText);
        var names = cmds.Select(x => (string)x["name"]).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var lines = new List<string>();
        foreach (var n in names)
        {
            var r = c.Call(app, new JObject { ["cmd"] = "help", ["name"] = n }.ToString(Formatting.None));
            if (r.Value<bool?>("ok") != true || !(r["command"] is JObject cmd))
            {
                fails++;
                rep.AppendLine("FAIL help " + n + ": " + Short(c.LastText));
                continue;
            }
            lines.Add(Canon(cmd).ToString(Formatting.None));
        }
        count = lines.Count;
        var sb = new StringBuilder();
        sb.Append("{\n\"protocol\":").Append(Canon(list["protocol"] ?? JValue.CreateNull()).ToString(Formatting.None));
        sb.Append(",\n\"count\":").Append(count);
        sb.Append(",\n\"commands\":[\n").Append(string.Join(",\n", lines)).Append("\n]");
        var parity = ParityNames();
        if (parity != null) sb.Append(",\n\"parity\":").Append(new JArray(parity.OrderBy(x => x, StringComparer.Ordinal).Cast<object>().ToArray()).ToString(Formatting.None));
        sb.Append("\n}\n");
        return sb.ToString();
    }

    /// <summary>CoreCommands.ParityNames when the field or property exists (null otherwise).</summary>
    static List<string> ParityNames()
    {
        var t = typeof(HS2Next.App.Commands.CoreCommands);
        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        object v = t.GetField("ParityNames", F)?.GetValue(null) ?? t.GetProperty("ParityNames", F)?.GetValue(null);
        return (v as IEnumerable<string>)?.ToList();
    }

    /// <summary>A copy with every object's members sorted by name (ordinal); arrays keep their order.</summary>
    public static JToken Canon(JToken t)
    {
        switch (t)
        {
            case JObject o:
                var n = new JObject();
                foreach (var p in o.Properties().OrderBy(p => p.Name, StringComparer.Ordinal)) n[p.Name] = Canon(p.Value);
                return n;
            case JArray a:
                var na = new JArray();
                foreach (var x in a) na.Add(Canon(x));
                return na;
            default:
                return t.DeepClone();
        }
    }

    // ---------------------------------------------------------------- corpus

    sealed class Stats
    {
        public int Requests, Errors, Oks, Commands;
        public double Seconds;
        public readonly Dictionary<string, int> PerCommand = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly Dictionary<string, int> Codes = new Dictionary<string, int>(StringComparer.Ordinal);
    }

    sealed class Fixture
    {
        public string Chara, Item, Light, Camera, Route;

        /// <summary>The node a command's key arguments name by default (its subject by the name's first part).</summary>
        public string KeyFor(string cmd)
        {
            if (cmd.StartsWith("item.", StringComparison.Ordinal) || cmd.StartsWith("obj.", StringComparison.Ordinal) || cmd == "select") return Item;
            if (cmd.StartsWith("light.", StringComparison.Ordinal)) return Light;
            if (cmd.StartsWith("camera.", StringComparison.Ordinal)) return Camera;
            if (cmd.StartsWith("route.", StringComparison.Ordinal)) return Route;
            return Chara;
        }

        /// <summary>Extra values of a node argument: every node, a key prefix, key arrays.</summary>
        public IEnumerable<JToken> NodeValues()
        {
            foreach (var k in new[] { Chara, Item, Light, Camera, Route }) yield return k;
            yield return Chara.Length > 8 ? Chara.Substring(0, 8) : Chara;
            yield return new JArray(Chara);
            yield return new JArray(Item);
            yield return new JArray(Chara, Item);
            yield return new JArray(Chara, Chara);
        }
    }

    static string Corpus(BridgeCoreTest.Ctx c, Stats st)
    {
        var sw = Stopwatch.StartNew();
        var app = c.Host(Seed);
        var fx = new Fixture();
        string Add(JObject req, string what)
        {
            var r = c.Call(app, req.ToString(Formatting.None));
            string k = (string)r["key"] ?? (string)r["added"]?[0]?["key"];
            if (r.Value<bool?>("ok") != true || k == null) throw new Exception("fixture " + what + " failed: " + Short(c.LastText));
            return k;
        }
        fx.Chara = Add(new JObject { ["cmd"] = "chara.load", ["path"] = c.FemaleRel, ["sex"] = "female" }, "chara.load");
        c.Call(app, new JObject { ["cmd"] = "chara.motion", ["key"] = fx.Chara, ["paused"] = true }.ToString(Formatting.None));
        fx.Item = Add(new JObject { ["cmd"] = "item.add", ["no"] = 399 }, "item.add");
        fx.Light = Add(new JObject { ["cmd"] = "light.add", ["no"] = 0 }, "light.add");
        fx.Camera = Add(new JObject { ["cmd"] = "camera.add" }, "camera.add");
        fx.Route = Add(new JObject { ["cmd"] = "route.add" }, "route.add");

        var lines = new List<string>
        {
            new JObject { ["fixture"] = new JObject { ["chara"] = fx.Chara, ["item"] = fx.Item, ["light"] = fx.Light, ["camera"] = fx.Camera, ["route"] = fx.Route } }
                .ToString(Formatting.None),
        };
        var specs = app.BridgeServices.Commands.Specs.Where(s => !CorpusSkip.Contains(s.Name))
            .OrderBy(s => Array.IndexOf(CorpusLast, s.Name) >= 0 ? 1 : 0).ThenBy(s => s.Name, StringComparer.Ordinal).ToList();
        foreach (var s in specs)
        {
            st.Commands++;
            int n = 0;
            foreach (var req in Requests(s, fx))
            {
                string text = req.ToString(Formatting.None);
                string reply = app.Call(text);
                var (norm, ok, code) = Normalize(reply);
                if (ok) st.Oks++;
                else
                {
                    st.Errors++;
                    st.Codes.TryGetValue(code ?? "?", out int cc);
                    st.Codes[code ?? "?"] = cc + 1;
                }
                lines.Add("{\"req\":" + text + ",\"rep\":" + norm + "}");
                n++;
            }
            st.PerCommand[s.Name] = n;
            st.Requests += n;
        }
        st.Seconds = sw.Elapsed.TotalSeconds;
        return "[\n" + string.Join(",\n", lines) + "\n]\n";
    }

    /// <summary>The base request of <paramref name="s"/> and its argument variants.</summary>
    static IEnumerable<JObject> Requests(CommandSpec s, Fixture fx)
    {
        var b = Base(s, fx);
        yield return b;
        bool writer = (s.Flags & CommandFlags.WritesFiles) != 0;
        foreach (var a in s.Args)
        {
            if (writer && (a.Name == "name" || a.Name == "path" || a.Name == "overwrite")) continue;
            if (b.ContainsKey(a.Name)) yield return Without(b, a.Name);
            // key and keys exclude each other: the other one's variants are also sent without it
            string partner = a.Name == "keys" ? "key" : a.Name == "key" ? "keys" : null;
            var bases = new List<JObject> { b };
            if (partner != null && s.FindArg(partner) != null && b.ContainsKey(partner)) bases.Add(Without(b, partner));
            foreach (var bb in bases)
            {
                foreach (var v in Values) yield return With(bb, a.Name, JToken.Parse(v));
                if (IsNode(a))
                    foreach (var v in fx.NodeValues()) yield return With(bb, a.Name, v);
            }
        }
    }

    static bool IsNode(FieldSpec a) =>
        a.Type == ArgTypes.Node || a.Type == ArgTypes.ArrayOf(ArgTypes.Node) || a.Name == "key" || a.Name == "keys" || a.Name == "parent";

    static JObject Base(CommandSpec s, Fixture fx)
    {
        JObject b = null;
        if (BaseOverrides.TryGetValue(s.Name, out var over)) b = (JObject)over.DeepClone();
        else if (s.Examples.Count > 0)
        {
            try { b = JObject.Parse(s.Examples[0]); }
            catch (Exception) { b = null; }
        }
        if (b == null) b = new JObject();
        b.Remove("cmd");
        b.Remove("id");
        ReplacePlaceholders(b, fx.KeyFor(s.Name));
        foreach (var a in s.Args)
            if (a.Required && !b.ContainsKey(a.Name)) b[a.Name] = Plain(a, s.Name, fx);
        if ((s.Flags & CommandFlags.WritesFiles) != 0)
        {
            // both name and path (refused as "not both"), or a path that leaves its root (refused): nothing is written
            if (s.FindArg("name") != null) b["name"] = "zz";
            if (s.FindArg("path") != null) b["path"] = "../zz";
            b.Remove("overwrite");
        }
        var r = new JObject { ["cmd"] = s.Name };
        foreach (var p in b.Properties()) r[p.Name] = p.Value.DeepClone();
        return r;
    }

    static void ReplacePlaceholders(JToken t, string key)
    {
        if (t is JObject o)
        {
            foreach (var p in o.Properties().ToList())
            {
                if (p.Value.Type == JTokenType.String && Placeholder.IsMatch((string)p.Value)) p.Value = key;
                else ReplacePlaceholders(p.Value, key);
            }
        }
        else if (t is JArray a)
        {
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Type == JTokenType.String && Placeholder.IsMatch((string)a[i])) a[i] = key;
                else ReplacePlaceholders(a[i], key);
            }
        }
    }

    /// <summary>A plain value of a required argument's declared type.</summary>
    static JToken Plain(FieldSpec a, string cmd, Fixture fx)
    {
        string t = a.Type ?? ArgTypes.Any;
        if (t == ArgTypes.Node || a.Name == "key") return fx.KeyFor(cmd);
        if (t == ArgTypes.ArrayOf(ArgTypes.Node) || a.Name == "keys") return new JArray(fx.KeyFor(cmd));
        switch (t)
        {
            case ArgTypes.String: case ArgTypes.Path: case ArgTypes.Any: return "zz";
            case ArgTypes.Int: return 0;
            case ArgTypes.Float: return 0.5;
            case ArgTypes.Bool: return false;
            case ArgTypes.Vec3: case ArgTypes.Euler: return new JArray(0, 0, 0);
            case ArgTypes.Quat: return new JArray(0, 0, 0, 1);
            case ArgTypes.Color: return new JArray(1, 1, 1, 1);
            case ArgTypes.ContentId: return "illusion.hs2#0";
            case ArgTypes.Object: return new JObject();
        }
        if (t.StartsWith("enum(", StringComparison.Ordinal))
        {
            string inner = t.Substring(5).TrimEnd(')');
            return inner.Split('|')[0];
        }
        if (t.StartsWith("array<", StringComparison.Ordinal)) return new JArray();
        return "zz";
    }

    static JObject Without(JObject b, string arg)
    {
        var r = (JObject)b.DeepClone();
        r.Remove(arg);
        return r;
    }

    static JObject With(JObject b, string arg, JToken v)
    {
        var r = (JObject)b.DeepClone();
        r[arg] = v.DeepClone();
        return r;
    }

    /// <summary>The reply as kept in the corpus: an error whole without the request id; a success as its member names and warnings.</summary>
    static (string text, bool ok, string code) Normalize(string reply)
    {
        JObject r;
        try { r = JObject.Parse(reply); }
        catch (Exception) { return (JsonConvert.ToString(reply), false, "not_json"); }
        r.Remove("id");
        if (r.Value<bool?>("ok") == true)
        {
            var o = new JObject { ["ok"] = true, ["cmd"] = r["cmd"]?.DeepClone() };
            var members = new JArray();
            foreach (var p in r.Properties())
                if (p.Name != "ok" && p.Name != "cmd" && p.Name != "warnings") members.Add(p.Name);
            o["members"] = members;
            if (r["warnings"] != null) o["warnings"] = r["warnings"].DeepClone();
            return (o.ToString(Formatting.None), true, null);
        }
        return (r.ToString(Formatting.None), false, (string)r["code"]);
    }

    // ---------------------------------------------------------------- helpers

    static string Arg(string name)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    public static string Sha(string text)
    {
        using (var h = SHA256.Create())
            return BitConverter.ToString(h.ComputeHash(new UTF8Encoding(false).GetBytes(text))).Replace("-", "").ToLowerInvariant();
    }

    static string Short(string s) => s == null ? "null" : s.Length > 400 ? s.Substring(0, 400) + "…" : s;
}

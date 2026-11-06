// The command layer's checks inside BridgeCoreTest's plugin group (C37 runs every CK<n> method of this partial class):
// CK2 the HS2-parity names (CoreCommands.ParityNames) are registered under their own names; CK3 a Core command reached
// through an alias, through the dispatcher and help; CK4 every Core handler that may answer later carries
// CommandFlags.Defers, help lists the flag, and the inline in-process call refuses those commands before they run; CK5 the
// naming convention of Reference/COMMANDS.md over every registered Core and bridge name (no renames: no alias on any of them).
// Each check makes its own hosts (c.Host()); nothing here loads a character or writes a file. RunNaming runs the four alone
// (-executeMethod BridgeCoreTest.RunNaming, headless; report Spike/out/bridge/bridge_naming_report.txt, first line
// "SUMMARY: N PASS, M FAIL"; EditorApplication.Exit(1) on a FAIL).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HS2Next.Api.Bridge;
using HS2Next.App;
using HS2Next.App.Commands;
using HS2Next.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static partial class BridgeCoreTest
{
    /// <summary>The Core commands whose handlers may answer later than they return.</summary>
    static readonly string[] CkDeferring = { "app.setup", "capture", "map.load", "record.stop", "scene.load", "screenshot", "ui.input", "ui.screenshot" };   // app.setup: Phase 5.3; record.stop: Phase 6.2

    /// <summary>The groups of the naming convention (the subject of a name's first part; chara.edit is the Maker).</summary>
    static readonly string[] CkGroups =
    {
        "scene", "obj", "chara", "item", "map", "camera", "light", "route", "fk", "ik", "pose", "anime", "effects", "sound",
        "content", "app", "ui", "plugin", "bridge", "log", "timeline", "overrides", "record", "materials", "constraint",
    };

    /// <summary>The one-word names registered before the convention was written down (kept as they are).</summary>
    static readonly string[] CkSingleWords = { "batch", "capture", "delete", "help", "history", "maps", "ping", "redo", "scene", "screenshot", "select", "selection", "undo" };

    /// <summary>CK2: every HS2-parity name is a registered Core command under its own name (none renamed, none an alias).</summary>
    static void CK2(Ctx c)
    {
        var app = c.Host();
        var reg = app.BridgeServices.Commands;
        var par = CoreCommands.ParityNames.ToList();
        c.Check(par.Count == 89 && par.Distinct(StringComparer.Ordinal).Count() == par.Count, "CoreCommands.ParityNames: 89 distinct names", "" + par.Count);
        var missing = par.Where(n => reg.Find(n) == null).ToList();
        c.Check(missing.Count == 0, "every parity name is registered", string.Join(", ", missing));
        var aliased = par.Where(n => reg.Find(n) != null && reg.Find(n).Name != n).ToList();
        c.Check(aliased.Count == 0, "no parity name reaches its command through an alias", string.Join(", ", aliased));
        var notCore = par.Where(n => reg.Find(n) != null && reg.Find(n).Owner != BridgeOwners.Core).ToList();
        c.Check(notCore.Count == 0, "every parity command is owned by core", string.Join(", ", notCore));
        var all = new HashSet<string>(CoreCommands.AllNames, StringComparer.Ordinal);
        var outside = par.Where(n => !all.Contains(n)).ToList();
        c.Check(outside.Count == 0, "every parity name is in CoreCommands.AllNames", string.Join(", ", outside));
        var helpBad = new List<string>();
        foreach (var n in par)
        {
            var r = c.Call(new { cmd = "help", name = n });
            if (r.Value<bool?>("ok") != true || r["command"]?.Value<string>("name") != n) helpBad.Add(n);
        }
        c.Check(helpBad.Count == 0, "help {name} describes every parity command under its own name", string.Join(", ", helpBad.Take(10)));
    }

    /// <summary>CK3: a Core command registered with an alias answers under the old name with the deprecation warning, help
    /// resolves the alias and lists it, and a later registration of the alias name is refused.</summary>
    static void CK3(Ctx c)
    {
        var app = c.Host();
        var reg = app.BridgeServices.Commands;
        const string name = "app.ckAliasProbe", old = "app.ckOldProbe";
        var spec = CommandKit.Spec(name, "A test command reached through an old name.")
            .Arg("x", ArgTypes.Int, "a number answered back", false, 0)
            .Return("x", ArgTypes.Int, "the number")
            .Alias(old);
        CommandKit.Register(reg, app, spec, cc => new BridgeReply().With("x", cc.Args.Get("x", 0)));
        c.Check(reg.Find(old)?.Name == name, "the registry resolves the alias to the command", reg.Find(old)?.Name ?? "null");

        var viaAlias = c.Call(new { cmd = old, x = 3 });
        var warnings = (viaAlias["warnings"] as JArray)?.Select(w => (string)w).ToList() ?? new List<string>();
        c.Check(viaAlias.Value<bool?>("ok") == true && viaAlias.Value<int?>("x") == 3, "the alias runs the command", Ctx.Short(c.LastText));
        c.Check(warnings.Contains("deprecated: use " + name), "the alias reply warns \"deprecated: use " + name + "\"", string.Join(" | ", warnings));
        var direct = c.Call(new { cmd = name, x = 3 });
        var dw = (direct["warnings"] as JArray)?.Select(w => (string)w).ToList() ?? new List<string>();
        c.Check(direct.Value<bool?>("ok") == true && direct.Value<int?>("x") == 3 && !dw.Any(w => w.StartsWith("deprecated", StringComparison.Ordinal)),
            "the name itself runs without the warning", Ctx.Short(c.LastText));

        var h = c.Call(new { cmd = "help", name = old });
        var hc = h["command"] as JObject;
        c.Check(h.Value<bool?>("ok") == true && hc?.Value<string>("name") == name, "help {alias} describes the command", Ctx.Short(c.LastText));
        c.Check((hc?["aliases"] as JArray)?.Select(a => (string)a).Contains(old) == true, "help {name} lists the alias", Ctx.Short(c.LastText));
        var list = c.Call(new { cmd = "help" });
        var entry = (list["commands"] as JArray)?.OfType<JObject>().FirstOrDefault(e => e.Value<string>("name") == name);
        c.Check((entry?["aliases"] as JArray)?.Select(a => (string)a).Contains(old) == true, "help lists the command once with its alias",
            entry == null ? "no entry" : entry.ToString(Formatting.None));
        c.Check((list["commands"] as JArray)?.OfType<JObject>().Any(e => e.Value<string>("name") == old) == false, "help has no entry under the alias");

        bool refused = false;
        try { reg.Register(new CommandSpec(old, "a second command under the alias"), _ => null); }
        catch (Exception) { refused = true; }
        c.Check(refused, "registering a command named like the alias is refused");

        var run = RunNowOf(app);
        if (run == null) c.Warn("no inline entry (BridgeServer.RunNow): the alias through the inline call is not checked");
        else
        {
            var r = JObject.Parse(run(new JObject { ["cmd"] = old, ["x"] = 3 }.ToString(Formatting.None)));
            var rw = (r["warnings"] as JArray)?.Select(w => (string)w).ToList() ?? new List<string>();
            c.Check(r.Value<bool?>("ok") == true && r.Value<int?>("x") == 3 && rw.Contains("deprecated: use " + name), "the inline call takes the alias with the warning",
                r.ToString(Formatting.None));
        }
        reg.Unregister(name);
    }

    /// <summary>
    /// CK4: the Core specs flagged Defers are exactly the seven commands whose handlers may defer (app.setup since Phase 5.3); help lists "defers" for them;
    /// every place in the Core sources that defers a reply sits in the handler of a flagged registration; the inline call
    /// refuses each of them (unsupported) before it runs. The requests carry arguments the handlers would refuse, so even a
    /// refusal that failed could not render or load anything.
    /// </summary>
    static void CK4(Ctx c)
    {
        var app = c.Host();
        var reg = app.BridgeServices.Commands;
        var flagged = reg.Specs.Where(s => s.Owner == BridgeOwners.Core && (s.Flags & CommandFlags.Defers) != 0).Select(s => s.Name)
            .OrderBy(n => n, StringComparer.Ordinal).ToList();
        c.Check(flagged.SequenceEqual(CkDeferring), "the Core commands flagged Defers: " + string.Join(", ", CkDeferring), string.Join(", ", flagged));
        var noFlag = new List<string>();
        foreach (var n in CkDeferring)
        {
            var h = c.Call(new { cmd = "help", name = n });
            if ((h["command"]?["flags"] as JArray)?.Select(f => (string)f).Contains("defers") != true) noFlag.Add(n);
        }
        c.Check(noFlag.Count == 0, "help lists the flag \"defers\" for each of them", string.Join(", ", noFlag));

        // the places that defer, by source: each must sit in a handler whose registration line carries the flag
        var sites = DeferSites();
        c.Info("deferring places in the Core sources: " + string.Join("; ", sites.Select(s => s.file + ":" + s.line + " " + s.method)));
        c.Check(sites.Count == CkDeferring.Length, "the Core sources defer in " + CkDeferring.Length + " places", "" + sites.Count);
        var unflagged = sites.Where(s => !s.flagged).Select(s => s.file + ":" + s.line + " " + s.method).ToList();
        c.Check(unflagged.Count == 0, "each place sits in a handler registered with CommandFlags.Defers", string.Join("; ", unflagged));

        var run = RunNowOf(app);
        if (run == null) { c.Warn("no inline entry (BridgeServer.RunNow): the refusals are not checked"); return; }
        var requests = new Dictionary<string, JObject>(StringComparer.Ordinal)
        {
            ["app.setup"] = new JObject { ["cmd"] = "app.setup", ["op"] = "zz" },
            ["capture"] = new JObject { ["cmd"] = "capture", ["width"] = 1 },
            ["map.load"] = new JObject { ["cmd"] = "map.load", ["ref"] = "zz" },
            ["record.stop"] = new JObject { ["cmd"] = "record.stop" },
            ["scene.load"] = new JObject { ["cmd"] = "scene.load", ["path"] = "zz_missing.hs2nscene" },
            ["screenshot"] = new JObject { ["cmd"] = "screenshot", ["background"] = "zz" },
            ["ui.input"] = new JObject { ["cmd"] = "ui.input" },
            ["ui.screenshot"] = new JObject { ["cmd"] = "ui.screenshot", ["name"] = "zz" },
        };
        foreach (var n in CkDeferring)
        {
            string text = run(requests[n].ToString(Formatting.None));
            var r = JObject.Parse(text);
            c.Check(r.Value<bool?>("ok") == false && r.Value<string>("code") == BridgeCodes.Unsupported
                    && (r.Value<string>("error") ?? "").Contains("cannot be called inline"),
                "the inline call refuses " + n + " (unsupported: cannot be called inline)", text);
        }
    }

    /// <summary>
    /// CK5: the naming convention over every registered Core and bridge name: an HS2-parity name as it is; a one-word name only
    /// from the list kept before the convention; any other name "&lt;group&gt;.&lt;part&gt;…" with a convention group first and
    /// camelCase parts. No Core or bridge command carries an alias (no renames).
    /// </summary>
    static void CK5(Ctx c)
    {
        var app = c.Host();
        var reg = app.BridgeServices.Commands;
        var parity = new HashSet<string>(CoreCommands.ParityNames, StringComparer.Ordinal);
        var specs = reg.Specs.Where(s => s.Owner == BridgeOwners.Core || s.Owner == BridgeOwners.Bridge).ToList();
        var part = new Regex("^[a-z][A-Za-z0-9]*$");
        var bad = new List<string>();
        int nParity = 0, nSingle = 0, nGroup = 0;
        foreach (var s in specs)
        {
            string n = s.Name;
            var parts = n.Split('.');
            if (parity.Contains(n)) { nParity++; continue; }
            if (parts.Length == 1)
            {
                if (Array.IndexOf(CkSingleWords, n) >= 0) nSingle++;
                else bad.Add(n + ": a new one-word name");
                continue;
            }
            if (Array.IndexOf(CkGroups, parts[0]) < 0) bad.Add(n + ": group \"" + parts[0] + "\" is not a convention group");
            else if (!parts.All(p => part.IsMatch(p))) bad.Add(n + ": a part is not camelCase");
            else nGroup++;
        }
        c.Info($"names: {specs.Count} (parity {nParity}, kept one-word {nSingle}, convention groups {nGroup})");
        c.Check(bad.Count == 0, "every Core and bridge name follows the naming convention", string.Join("; ", bad.Take(10)));
        var aliased = specs.Where(s => s.Aliases.Count > 0).Select(s => s.Name + " <- " + string.Join(", ", s.Aliases)).ToList();
        c.Check(aliased.Count == 0, "no Core or bridge command has an alias (no renames)", string.Join("; ", aliased));
    }

    /// <summary>CK2–CK5 alone, each as a check of Run (its own report section; an exception is a FAIL).</summary>
    public static void RunNaming()
    {
        var c = new Ctx();
        Directory.CreateDirectory(OutDir);
        var checks = typeof(BridgeCoreTest).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(m => Regex.IsMatch(m.Name, @"^CK\d+$") && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Ctx))
            .OrderBy(m => int.Parse(m.Name.Substring(2))).ToList();
        try
        {
            var head = new System.Text.StringBuilder();
            c.Services = SceneRuntimeTest.NewServices(head);
            c.Rep.Append(head);
            foreach (var m in checks)
            {
                c.Id = m.Name;
                c.Rep.AppendLine().AppendLine("== " + m.Name);
                c.BeginCheck();
                try { m.Invoke(null, new object[] { c }); }
                catch (TargetInvocationException e) { c.Check(false, "exception", (e.InnerException ?? e).GetType().Name + ": " + (e.InnerException ?? e).Message + "\n" + (e.InnerException ?? e).StackTrace); }
                finally { c.EndCheck(); }
            }
        }
        catch (Exception e) { c.Id = "run"; c.Check(false, "run (exception)", e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        c.Rep.Insert(0, $"SUMMARY: {c.Passed} PASS, {c.Failed} FAIL ({c.Warned} WARN); checks {string.Join(", ", checks.Select(m => m.Name))}\n");
        File.WriteAllText(Path.Combine(OutDir, "bridge_naming_report.txt"), c.Rep.ToString(), new System.Text.UTF8Encoding(false));
        c.Services?.Catalog?.UnloadAll();
        UnityEngine.Debug.Log($"BRIDGE NAMING CHECKS DONE: {c.Passed} PASS, {c.Failed} FAIL, {c.Warned} WARN");
        if (c.Failed > 0) TestExit.Exit(1);
    }

    // ---------------------------------------------------------------- helpers of CK3 / CK4

    /// <summary>The inline in-process call of the host's bridge server (null when the server has none).</summary>
    static Func<string, string> RunNowOf(AppCore app)
    {
        var server = app.Bridge?.Server;
        var m = server?.GetType().GetMethod("RunNow", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
        if (m == null || m.ReturnType != typeof(string)) return null;
        return json => (string)m.Invoke(server, new object[] { json });
    }

    /// <summary>
    /// The places in the Core command sources (Runtime/App/Commands, Runtime/UI/Bridge) that defer a reply: file, line, the
    /// enclosing handler, and whether that handler's registration line (", Handler);") carries CommandFlags.Defers.
    /// </summary>
    static List<(string file, int line, string method, bool flagged)> DeferSites()
    {
        var l = new List<(string, int, string, bool)>();
        var method = new Regex(@"^\s*(?:static|public|internal|private)[^=;(]*\s(\w+)\s*\(\s*CoreContext\s+\w+\s*\)");
        foreach (var dir in new[] { "Assets/HS2Next/Runtime/App/Commands", "Assets/HS2Next/Runtime/UI/Bridge" })
        {
            string abs = Path.Combine(ContentSettings.ProjectRoot, dir);
            if (!Directory.Exists(abs)) continue;
            foreach (var f in Directory.GetFiles(abs, "*.cs").OrderBy(x => x, StringComparer.Ordinal))
            {
                var lines = File.ReadAllLines(f);
                string current = null;
                for (int i = 0; i < lines.Length; i++)
                {
                    var mm = method.Match(lines[i]);
                    if (mm.Success) current = mm.Groups[1].Value;
                    if (!lines[i].Contains(".Defer()")) continue;
                    bool flagged = current != null && RegisteredWithDefers(lines, current);
                    l.Add((Path.GetFileName(f), i + 1, current ?? "?", flagged));
                }
            }
        }
        return l;
    }

    /// <summary>True when a registration statement of <paramref name="handler"/> (from the line holding "Register(" to the line
    /// ending in "handler);", over one or several lines) carries CommandFlags.Defers.</summary>
    static bool RegisteredWithDefers(string[] lines, string handler)
    {
        var end = new Regex(@"(^|[\s,])" + Regex.Escape(handler) + @"\);");
        for (int j = 0; j < lines.Length; j++)
        {
            if (!end.IsMatch(lines[j])) continue;
            for (int k = j; k >= 0 && j - k < 40; k--)
            {
                if (lines[k].Contains("CommandFlags.Defers")) return true;
                if (lines[k].Contains("Register(")) break;
            }
        }
        return false;
    }
}

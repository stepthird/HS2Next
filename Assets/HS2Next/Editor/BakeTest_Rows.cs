// The generated item rows of the Studio list, headless (edit mode, AppCore.ForTests hosts, frames pumped by hand): the registry of
// generated groups, the item's instance from a generated row, the item browser's rows and item.search, the plugin view
// (IUnityPluginServices.ItemRows) with its faults, a plugin that comes late, leaves or is disabled, and the app's own group.
//   BakeRowsTest.Run   report Spike/out/bake/rows_report.txt (first line "SUMMARY: N PASS, M FAIL"); -only BR1+BR5 runs those groups.
//   BakeRowsTest.RunInto(...)  the same groups into another report (BakeTest's part).
// Every check that passes on a difference of 0 prints in its line what shows the thing compared is there. Each group removes the
// generated groups it added and disposes its hosts.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.App;
using HS2Next.App.Plugins;
using HS2Next.App.Plugins.Engine;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Bake;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Panels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static partial class BakeRowsTest
{
    public const float Dt = 1f / 60f;
    public const ulong DefaultSeed = 0x68a0a5eedUL;
    public const string TestGuid = "stp3.test.rows";
    public static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "bake");
    static string Report => Arg("-report") ?? Path.Combine(OutDir, "rows_report.txt");

    static StringBuilder rep;
    static int passes, fails, warns;
    static string current = "";
    static readonly Dictionary<string, (int pass, int fail)> perGroup = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
    static readonly List<string> measurements = new List<string>();
    static string[] only;
    static RuntimeServices services;

    // ------------------------------------------------------------ report

    static string Arg(string name)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    // another report's helpers while RunInto runs (BakeTest's part); null: this class's own report
    static Func<string, bool, string, bool> checkInto;
    static Action<string> infoInto, warnInto, measureInto;
    static Func<string, bool> wantInto;
    static Action<string, string, Action> groupInto;

    static bool Want(string id) => wantInto != null ? wantInto(id) : only == null || only.Contains(id, StringComparer.OrdinalIgnoreCase);

    static bool Check(string what, bool ok, string detail = "")
    {
        if (checkInto != null) return checkInto(what, ok, detail);
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  [{current}] {what}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
        perGroup.TryGetValue(current, out var v);
        perGroup[current] = ok ? (v.pass + 1, v.fail) : (v.pass, v.fail + 1);
        return ok;
    }

    static void Warn(string what, string detail = "")
    {
        if (warnInto != null) { warnInto(what + (string.IsNullOrEmpty(detail) ? "" : "  " + detail)); return; }
        rep.AppendLine($"WARN  [{current}] {what}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        warns++;
    }

    static void Info(string s)
    {
        if (infoInto != null) infoInto(s);
        else rep.AppendLine("      " + s);
    }

    static void Measure(string s)
    {
        if (measureInto != null) { measureInto(s); return; }
        measurements.Add("[" + current + "] " + s);
        Info(s);
    }

    static string Short(string s) => s == null ? "null" : s.Length > 400 ? s.Substring(0, 400) + "..." : s;

    /// <summary>One group of checks: its title, then the action; an exception is a FAIL of the group.</summary>
    static void Group(string id, string title, Action run)
    {
        if (groupInto != null) { groupInto(id, title, run); return; }
        if (!Want(id)) return;
        current = id;
        rep.AppendLine();
        rep.AppendLine(id + " " + title);
        try { run(); }
        catch (Exception e) { Check("no exception", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
    }

    // ------------------------------------------------------------ run

    public static void Run()
    {
        rep = new StringBuilder(); passes = fails = warns = 0; perGroup.Clear(); measurements.Clear();
        var o = Arg("-only");
        only = o == null ? null : o.Split(',', '+').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        Directory.CreateDirectory(OutDir);
        var sw = Stopwatch.StartNew();
        var head = new StringBuilder();
        try
        {
            services = SceneRuntimeTest.NewServices(head);
            rep.AppendLine($"BakeRowsTest (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
            rep.Append(head);
            RunAll();
        }
        catch (Exception e) { current = "run"; Check("run (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        var sum = new StringBuilder();
        sum.AppendLine($"SUMMARY: {passes} PASS, {fails} FAIL ({warns} WARN, {sw.Elapsed.TotalSeconds:F0} s)");
        foreach (var kv in perGroup.OrderBy(k => k.Key, StringComparer.Ordinal)) sum.AppendLine($"  {kv.Key}: {kv.Value.pass} pass, {kv.Value.fail} fail");
        if (measurements.Count > 0) { sum.AppendLine("measurements:"); foreach (var m in measurements) sum.AppendLine("  " + m); }
        sum.AppendLine("BakeRowsTest.Run (headless): the generated item rows");
        rep.Insert(0, sum.ToString());
        File.WriteAllText(Report, rep.ToString(), new UTF8Encoding(false));
        services?.Catalog?.UnloadAll();
        Debug.Log($"BAKE ROWS TEST DONE: {passes} PASS, {fails} FAIL");
        if (fails > 0) TestExit.Exit(1);
    }

    /// <summary>
    /// Every group of this file into another report (the part of BakeTest.Run): its check, info, warning, measurement, selection
    /// (-only) and group. <paramref name="s"/> null: lists of its own (one catalog per batch, its own Studio lists).
    /// </summary>
    public static void RunInto(RuntimeServices s, Func<string, bool, string, bool> check, Action<string> info, Action<string> warn, Action<string> measure,
        Func<string, bool> want, Action<string, string, Action> group)
    {
        checkInto = check; infoInto = info; warnInto = warn; measureInto = measure; wantInto = want; groupInto = group;
        try
        {
            services = s ?? SceneRuntimeTest.NewServices(new StringBuilder());
            RunAll();
        }
        finally
        {
            checkInto = null; infoInto = null; warnInto = null; measureInto = null; wantInto = null; groupInto = null;
        }
    }

    /// <summary>Every group of this file, in order.</summary>
    static void RunAll()
    {
        Group("BR1", "the registry: refusals, rows by (guid, id), a missing row falls back with its reference kept", BR1);
        Group("BR2", "the item browser's rows and item.search list the generated rows after the accessory rows; item.add takes them", BR2);
        Group("BR3", "appending and replacing 1,000 rows (BW9, Editor numbers)", BR3);
        Group("BR7", "a function that changes the scene: item.generatorWrote, the build goes on", BR7);
        Group("BV8", "a generated object's members: a mosaic material, a member's name, AddMosaic; a plain renderer is none", BV8);
        Group("BR10", "the app's group 731406: markers and store rows listed; a list that defines 731406 refuses it and bakes still load", BR10);
        Group("BR10", "the app's group lists the store's live rows, read again after a write", BR10Store);
        Group("BR10", "item.add {group, category, no} adds a row of a generated group: a store row, a marker, a plugin's row; an unknown number is not_found", BR10Ints);
        bool staged = (Want("BR4") || Want("BR5") || Want("BR6") || Want("BR8") || Want("BR9")) && StagePlugins();
        if (staged)
        {
            byte[] scene = null;
            Group("BR4", "an absent provider: the scene loads with fallbacks and saves byte-equal; with the plugin it builds the rows", () => scene = BR4());
            Group("BR5", "a plugin that comes late builds the fallback items again; Refresh and a changed row build theirs", () => BR5(scene));
            Group("BR8", "a plugin disabled or a group disposed: the group goes, items keep their objects until their next build", BR8);
            Group("BR9", "the sample Shapes: five rows, the torus bent by its eight FK bones, save and load with and without it", BR9);
            Group("BR6", "a function that throws or makes nothing: the fallback item, the finding, a fault; ten faults stop the plugin", BR6);
        }
        current = "end";
        Check("no generated group is left on the shared lists", services.Db.Generated.Groups.Count == 0, services.Db.Generated.Groups.Count + " groups: " + string.Join("; ", services.Db.Generated.Groups));
    }

    // ------------------------------------------------------------ hosts

    sealed class Host : IDisposable
    {
        public AppCore App;
        public SceneRuntime Rt => App.Runtime;
        public SceneSession Session => App.Session;
        public StudioDb Db => App.Db;
        public string LastText;

        public JObject Call(object request)
        {
            string json = request is string s ? s : JsonConvert.SerializeObject(request);
            LastText = App.Call(json);
            return JObject.Parse(LastText);
        }

        public JObject Ok(object request, string what)
        {
            var r = Call(request);
            Check(what, r.Value<bool?>("ok") == true, r.Value<bool?>("ok") == true ? "" : Short(LastText));
            return r;
        }

        public void Frames(int n, float dt = Dt)
        {
            for (int i = 0; i < n; i++) App.Frame(dt);
        }

        public void Dispose() { try { App?.Dispose(); } catch (Exception e) { Info("host dispose threw: " + e.Message); } }
    }

    static string UserDataDir => Path.Combine(OutDir, "rows_userdata");
    static string OutputDir => Path.Combine(OutDir, "rows_out");

    static Host NewHost(ulong seed = DefaultSeed)
    {
        var s = AppSettings.Defaults(Array.Empty<string>());
        s.Content = CardResolveTest.Settings;
        s.UserData = UserDataDir;
        s.Bridge.OutputDir = OutputDir;
        var o = services.NewSessionOptions();
        o.Ids = new CountingIdSource(seed);
        return new Host { App = AppCore.ForTests(services, s, null, o) };
    }

    /// <summary>A group number nobody holds, from <paramref name="start"/> up.</summary>
    static int FreeGroup(StudioDb db, int start)
    {
        for (int g = start; g < start + 1000; g++) if (db.Generated.Refusal(g) == null) return g;
        throw new InvalidOperationException("no free item group number from " + start);
    }

    /// <summary>A small object: a cube with a chain of <paramref name="bones"/> child transforms named b1, b2, ...</summary>
    static GameObject Cube(string name, int bones = 1)
    {
        var go = new GameObject(name);
        var mesh = new Mesh { name = name + "_mesh" };
        mesh.vertices = new[] { new Vector3(-1, -1, -1), new Vector3(1, -1, -1), new Vector3(1, 1, -1), new Vector3(-1, 1, -1), new Vector3(-1, -1, 1), new Vector3(1, -1, 1), new Vector3(1, 1, 1), new Vector3(-1, 1, 1) };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 1, 2, 6, 1, 6, 5, 0, 4, 7, 0, 7, 3 };
        mesh.RecalculateNormals();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var sh = Shader.Find("HS2Next/Standard");
        go.AddComponent<MeshRenderer>().sharedMaterial = new Material(sh ? sh : Shader.Find("Universal Render Pipeline/Lit")) { name = name + "_mat" };
        var parent = go.transform;
        for (int i = 1; i <= bones; i++)
        {
            var b = new GameObject("b" + i).transform;
            b.SetParent(parent, false);
            b.localPosition = new Vector3(0, 1, 0);
            parent = b;
        }
        return go;
    }

    static ItemNodeState ItemState(string guid, int id, int group, int category) =>
        new ItemNodeState { Ref = new ContentId(guid, id).ToString(), Group = group, Category = category };

    /// <summary>Adds an item node of <paramref name="st"/> and applies; its id.</summary>
    static string AddNode(Host h, ItemNodeState st)
    {
        var r = h.Session.AddItem(st);
        h.Rt.ApplyNow();
        return r.NewIds.FirstOrDefault();
    }

    static int FindingCount(Host h, string code, string node = null)
    {
        int n = 0;
        foreach (var f in h.Session.Findings) if (f.Code == code && (node == null || f.NodeId == node)) n += f.Count;
        return n;
    }

    // ------------------------------------------------------------ BR1

    static void BR1()
    {
        var host = NewHost();
        var db = host.Db;
        GeneratedGroup g = null;
        try
        {
            // a scene with no generated row: today's path, no part
            string plain = AddNode(host, new ItemNodeState { Ref = StudioDb.FallbackItem.ToString(), Group = 0, Category = 0 });
            var pr = host.Rt.FindItem(plain);
            Check("a list item with no generated group in the scene builds from its bundle, no rebuild part", pr?.Source == "bundle" && GeneratedRowRebuilds.Of(host.Rt) == null,
                "source " + pr?.Source + ", state " + pr?.State + ", parts " + host.Rt.Parts.Count + ", groups held " + db.Generated.Groups.Count);

            int itemsBefore = db.Items.Count, groupsBefore = db.ItemGroups(true).Count();
            int G = FreeGroup(db, 731490);
            var made = new List<GameObject>();
            g = db.Generated.Add(G, "Rows test", TestGuid, TestGuid, r =>
            {
                var go = Cube("row" + r.Id, 2);
                made.Add(go);
                return go;
            });
            g.SetCategory(0, "First");
            g.SetCategory(3, "Second");
            g.SetRows(new[]
            {
                new GeneratedRowSpec(0, 1, "One", new[] { "b1", "b2" }),
                new GeneratedRowSpec(0, 2, "Two"),
                new GeneratedRowSpec(3, 7, "Seven", new[] { "b1" }),
            });
            Info("group " + G + ": " + g);

            // rows resolve by (guid, id)
            var k1 = new ContentId(TestGuid, 1);
            var one = db.ResolveItem(k1, G, 0, out bool fb1);
            Check("a registered row resolves by (guid, id) to its own row", one != null && !fb1 && one.Name == "One" && one.Group == G && one.Category == 0,
                one + ", fallback " + fb1);
            var oneElsewhere = db.ResolveItem(k1, G, 3, out bool fb1b);
            Check("the stored category does not hide the row (the key is guid and id)", ReferenceEquals(oneElsewhere, one) && !fb1b, oneElsewhere + ", fallback " + fb1b);
            Check("TryGetItem by (guid, group, category, id), by (id, group, category) and by the reference alone find it",
                db.TryGetItem(TestGuid, G, 0, 1, out var a1) && ReferenceEquals(a1, one) && db.TryGetItem(k1, G, 0, out var a2) && ReferenceEquals(a2, one)
                && db.TryGetItem(k1, out var a3) && ReferenceEquals(a3, one), "");
            Check("the rows are not in db.Items; db.Items keeps its count", db.Items.Count == itemsBefore && !db.Items.TryGet(k1, out _),
                "db.Items " + db.Items.Count + " (before " + itemsBefore + ")");
            var groups = db.ItemGroups().ToList();
            Check("the group is listed after every list group, with its two categories in number order", groups.Count > 1 && groups[groups.Count - 1].Number == G
                && db.CategoriesOf(groups[groups.Count - 1]).Select(c => c.Number).SequenceEqual(new[] { 0, 3 }) && db.ItemGroups(true).Count() == groupsBefore + 1,
                groups.Count + " visible groups, the last " + groups[groups.Count - 1].Number + ", categories " + string.Join(",", db.CategoriesOf(groups[groups.Count - 1]).Select(c => c.Number + " " + c.Name)));
            db.TryGetItemCategory(G, 0, out var cat0);
            Check("ItemsIn lists the category's rows in the order given", db.ItemsIn(cat0).Select(r => r.Name).SequenceEqual(new[] { "One", "Two" }),
                string.Join(", ", db.ItemsIn(cat0).Select(r => r.Key + " " + r.Name)));

            // refusals
            int listGroup = db.ItemGroups(true).First(x => !db.Generated.Holds(x.Number)).Number;
            bool listOk = db.Generated.TryAdd(listGroup, "x", "other", "other", _ => null, out _, out string why1);
            Check("a group number the lists define is refused (rows.groupTaken)", !listOk && why1 != null && why1.StartsWith(GeneratedRows.TakenCode, StringComparison.Ordinal) && why1.Contains("lists"),
                "group " + listGroup + ": " + why1);
            bool accOk = db.Generated.TryAdd(StudioDb.AccessoryGroup, "x", "other", "other", _ => null, out _, out string why2);
            Check("the accessory group's number is refused", !accOk && why2 != null && why2.StartsWith(GeneratedRows.TakenCode, StringComparison.Ordinal) && why2.Contains("accessory"), why2);
            bool againOk = db.Generated.TryAdd(G, "x", "other", "other", _ => null, out _, out string why3);
            Check("a number another provider holds is refused", !againOk && why3 != null && why3.StartsWith(GeneratedRows.TakenCode, StringComparison.Ordinal) && why3.Contains(TestGuid), why3);
            var guard = new PluginGuard("stp3.test.rows.view", null, 100000);
            var view = new PluginEngineServices("stp3.test.rows.view", guard, null, null, () => host.Rt, null);
            string ex1 = Throws(() => view.ItemRows.AddGroup(G, "x", _ => null));
            string ex2 = Throws(() => view.ItemRows.AddGroup(listGroup, "x", _ => null));
            Check("the plugin view throws ArgumentException for a held and a list number", ex1 != null && ex1.StartsWith("ArgumentException") && ex1.Contains(GeneratedRows.TakenCode)
                && ex2 != null && ex2.StartsWith("ArgumentException") && ex2.Contains(GeneratedRows.TakenCode), ex1 + " | " + ex2);
            Check("the refused adds left the registry as it was", db.Generated.Groups.Count == 1 && db.Generated.Group(G) == g, db.Generated.Groups.Count + " groups");
            view.Unload();
            string dup = Throws(() => g.SetRows(new[] { new GeneratedRowSpec(0, 1, "a"), new GeneratedRowSpec(3, 1, "b") }));
            Check("two rows with one key are refused and the rows stay", dup != null && dup.Contains("rows.duplicateId") && g.Rows.Count == 3, dup + "; rows " + g.Rows.Count);

            // an item of a registered row builds from the group's function
            string n1 = AddNode(host, ItemState(TestGuid, 1, G, 0));
            var it1 = host.Rt.FindItem(n1);
            Check("an item of a registered row builds from its group's function under the holder", it1 != null && it1.State == RealizationState.Built && it1.Source == "generated"
                && !it1.IsFallback && ReferenceEquals(it1.Row, one) && it1.Instance && made.Count == 1 && it1.Instance == made[0] && it1.Instance.transform.parent == it1.Holder,
                "state " + it1?.State + ", source " + it1?.Source + ", row " + it1?.Row + ", made " + made.Count);
            Check("its FK bones are found by name in the object", it1?.Features != null && it1.Features.FkBones.SequenceEqual(new[] { "b1", "b2" }) && it1.Features.FkMissing.Count == 0,
                it1?.Features == null ? "no features" : string.Join(",", it1.Features.FkBones) + " / missing " + string.Join(",", it1.Features.FkMissing));
            Check("its material slot is the item's own copy", it1?.Instance.GetComponent<MeshRenderer>().sharedMaterial != made[0].GetComponent<MeshRenderer>().sharedMaterial || it1?.Writer.OwnedMaterials == 1,
                "owned " + it1?.Writer.OwnedMaterials);
            Check("an item of a generated group gets the rebuild part", GeneratedRowRebuilds.Of(host.Rt) != null, host.Rt.Parts.Count + " parts");

            // a missing row falls back with the reference kept
            var miss = db.ResolveItem(new ContentId(TestGuid, 99), G, 0, out bool fb99);
            Check("a missing row of a held group resolves to the fallback item", fb99 && miss != null && miss.Key == StudioDb.FallbackItem, miss + ", fallback " + fb99);
            string n99 = AddNode(host, ItemState(TestGuid, 99, G, 0));
            var it99 = host.Rt.FindItem(n99);
            var st99 = host.Session.Model.Find(n99)?.State as ItemNodeState;
            Check("an item of a missing row loads the fallback item, item.missing", it99 != null && it99.State == RealizationState.Built && it99.IsFallback && it99.Row?.Key == StudioDb.FallbackItem
                && it99.Source == "bundle" && FindingCount(host, "item.missing", n99) == 1, "state " + it99?.State + ", row " + it99?.Row + ", item.missing " + FindingCount(host, "item.missing", n99));
            Check("its reference, group and category are kept", st99 != null && st99.Ref == TestGuid + "#99" && st99.Group == G && st99.Category == 0, st99?.Ref + " " + st99?.Group + "/" + st99?.Category);

            // removal
            g.Remove();
            Check("a removed group leaves the group list and its rows resolve no more", !db.TryGetItemGroup(G, out _) && !db.TryGetItemCategory(G, 0, out _) && db.ItemGroups(true).Count() == groupsBefore
                && db.ResolveItem(k1, G, 0, out bool fbGone)?.Key == StudioDb.FallbackItem && fbGone && !db.TryGetItem(k1, out _), "groups " + db.ItemGroups(true).Count() + " (before " + groupsBefore + ")");
            Check("an item built from it keeps its object until its next build", it1 != null && it1.Instance && it1.Source == "generated", "instance " + (it1?.Instance ? it1.Instance.name : "none"));
            Rebuild(host.Rt, it1);
            Check("its next build loads the fallback item", it1.IsFallback && it1.Row?.Key == StudioDb.FallbackItem && it1.Source == "bundle", "row " + it1.Row + ", source " + it1.Source);
        }
        finally
        {
            g?.Remove();
            host.Dispose();
        }
    }

    // ------------------------------------------------------------ the test plugins

    const string ProbeGuid = "stp3.test.rowprobe", ShapesGuid = "stp3.sample.shapes";
    const int ProbeGroup = 731497, ShapesGroup = 820001;
    static readonly DateTime FixedClock = new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);
    static string PluginsDir => Arg("-rowsPlugins") ?? Path.Combine(OutDir, "rows_plugins");
    static bool pluginsStaged;

    /// <summary>Builds the row probe and the sample Shapes with dotnet against the Api, as plugins are built, into their own folder (once
    /// per process: an assembly loads once).</summary>
    static bool StagePlugins()
    {
        current = "plugins";
        if (pluginsStaged || Arg("-rowsPlugins") != null) return pluginsStaged = Directory.Exists(PluginsDir);
        string tool = Path.Combine(ContentSettings.ProjectRoot, "Tools", "plugins.ps1");
        foreach (var proj in new[] { Path.Combine("Tests", "Plugins", "RowProbe"), Path.Combine("Samples", "PluginKit", "Shapes") })
        {
            var psi = new ProcessStartInfo("pwsh", "-NoProfile -File \"" + tool + "\" -Build \"" + proj + "\" -Stage \"" + PluginsDir + "\"")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = ContentSettings.ProjectRoot,
            };
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                bool exited = p.WaitForExit(300000);
                if (!Check(proj + " is built and staged", exited && p.ExitCode == 0, Short(o.Trim().Split('\n').LastOrDefault()))) return false;
            }
        }
        return pluginsStaged = true;
    }

    /// <summary>A host with the two test plugins loaded (nothing else).</summary>
    static Host HostWithPlugins(ulong seed = DefaultSeed)
    {
        var h = NewHost(seed);
        h.App.LoadPlugins(PluginsDir);
        return h;
    }

    static JObject PluginEntry(Host h, string guid) =>
        (h.Call(new { cmd = "plugin.list" })["plugins"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x["guid"] == guid);

    static int Faults(Host h, string guid, string hook) => (int?)(PluginEntry(h, guid)?["faults"] as JObject)?[hook] ?? 0;

    static JObject ProbeState(Host h) => h.Call(new { cmd = "rowprobe.state" });

    /// <summary>The row probe's own count of its function's calls, read in process (no bridge call, so no frame); 0 before it was ever loaded.</summary>
    static int ProbeMakes()
    {
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = a.GetType("Stp3.Test.RowProbe.RowProbePlugin", false);
            var f = t?.GetField("makes", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (f != null) return (int)f.GetValue(null);
        }
        return 0;
    }

    static string AddRef(Host h, string reference)
    {
        var r = h.Call(new { cmd = "item.add", @ref = reference });
        h.Rt.ApplyNow();
        return (string)r["added"]?[0]?["key"];
    }

    // ------------------------------------------------------------ BR2

    static void BR2()
    {
        var host = NewHost();
        var db = host.Db;
        GeneratedGroup g = null;
        try
        {
            int G = FreeGroup(db, 731490);
            g = db.Generated.Add(G, "Rows test", TestGuid, TestGuid, r => Cube("row" + r.Id));
            g.SetCategory(0, "First");
            g.SetCategory(1, "Second");
            int rev0 = db.Generated.Revision;
            g.SetRows(new[] { new GeneratedRowSpec(0, 1, "Row one"), new GeneratedRowSpec(1, 2, "Row two"), new GeneratedRowSpec(0, 3, "Row three") });
            Check("setting rows changes the registry's revision (the browser reads its rows again)", db.Generated.Revision != rev0, rev0 + " -> " + db.Generated.Revision);

            var rows = ListBrowsers.ItemRows(db);
            int nItems = db.Items.Enumerate().Count(), nAcc = db.AccessoryItems.Count;
            Check("the browser's rows: the list rows, then the accessory rows, then the generated rows in their order", rows.Count == nItems + nAcc + 3
                && rows.Take(nItems).SequenceEqual(db.Items.Enumerate()) && rows.Skip(nItems).Take(nAcc).SequenceEqual(db.AccessoryItems) && rows.Skip(nItems + nAcc).SequenceEqual(g.Rows),
                $"{rows.Count} rows = {nItems} list + {nAcc} accessory + {rows.Count - nItems - nAcc} generated ({string.Join(", ", rows.Skip(nItems + nAcc).Select(r => r.Key + " " + r.Name))})");

            var all = host.Call(new { cmd = "item.search", q = "", limit = 1000000 })["items"] as JArray;
            var list = all?.OfType<JObject>().ToList() ?? new List<JObject>();
            int lastAcc = list.FindLastIndex(x => (int?)x["group"] == StudioDb.AccessoryGroup), firstGen = list.FindIndex(x => (int?)x["group"] == G);
            int lastOther = list.FindLastIndex(x => (int?)x["group"] != G);
            Check("item.search lists the generated rows after the accessory rows and every list row", nAcc > 0 && lastAcc >= 0 && firstGen > lastAcc && firstGen > lastOther
                && list.Count(x => (int?)x["group"] == G) == 3, $"{list.Count} rows; last accessory at {lastAcc}, last other at {lastOther}, first generated at {firstGen}");
            var only = (host.Call(new { cmd = "item.search", group = G, limit = 100 })["items"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
            Check("item.search {group} gives the group's rows with their names, ids and GUID", only.Select(x => (string)x["id"]).SequenceEqual(new[] { TestGuid + "#1", TestGuid + "#3", TestGuid + "#2" })
                && only.All(x => (string)x["guid"] == TestGuid && (string)x["groupName"] == "Rows test") && (string)only[0]["categoryName"] == "First" && (string)only[2]["categoryName"] == "Second",
                string.Join(" | ", only.Select(x => x.ToString(Formatting.None))));
            var byName = (host.Call(new { cmd = "item.search", q = "Rows test / Second", limit = 100 })["items"] as JArray)?.Count ?? -1;
            Check("item.search finds them by group and category name", byName == 1, byName + " found");

            string a1 = AddRef(host, TestGuid + "#3");
            var it1 = host.Rt.FindItem(a1);
            var st1 = host.Session.Model.Find(a1)?.State as ItemNodeState;
            Check("item.add with the row's reference adds an item of the row (group and category of the row) built from its function", it1 != null && it1.Source == "generated" && st1 != null
                && st1.Ref == TestGuid + "#3" && st1.Group == G && st1.Category == 0, "node " + a1 + ", source " + it1?.Source + ", state " + st1?.Ref + " " + st1?.Group + "/" + st1?.Category);
            var r2 = host.Call(new { cmd = "item.add", group = G, category = 1, guid = TestGuid, no = 2 });
            host.Rt.ApplyNow();
            string a2 = (string)r2["added"]?[0]?["key"];
            Check("item.add with group, category, guid and no adds it too", host.Rt.FindItem(a2)?.Source == "generated" && (host.Session.Model.Find(a2)?.State as ItemNodeState)?.Category == 1, Short(host.LastText));
            int undo = (host.Call(new { cmd = "history" })["undo"] as JArray)?.Count ?? -1;
            Check("each add is one history entry", undo == 2, undo + " entries");
        }
        finally
        {
            g?.Remove();
            host.Dispose();
        }
    }

    // ------------------------------------------------------------ BR3

    static void BR3()
    {
        var host = NewHost();
        var db = host.Db;
        try
        {
            int G = FreeGroup(db, 731490);
            List<GeneratedRowSpec> Rows(string tag)
            {
                var l = new List<GeneratedRowSpec>(1000);
                for (int i = 0; i < 1000; i++) l.Add(new GeneratedRowSpec(i % 10, i + 1, tag + " " + i, i % 3 == 0 ? new[] { "b" + i } : null));
                return l;
            }
            var a = Rows("first");
            var b = Rows("second");
            var append = new List<double>();
            var replace = new List<double>();
            for (int k = 0; k < 7; k++)
            {
                var g = db.Generated.Add(G, "Cost", TestGuid, TestGuid, _ => null);
                var sw = Stopwatch.StartNew();
                g.SetRows(a);
                append.Add(sw.Elapsed.TotalMilliseconds);
                sw.Restart();
                g.SetRows(b);
                replace.Add(sw.Elapsed.TotalMilliseconds);
                if (k == 0) Check("1,000 rows are listed", g.Rows.Count == 1000 && db.ItemGroups().Any(x => x.Number == G) && db.TryGetItem(TestGuid, G, 9, 1000, out _), g.Rows.Count + " rows");
                g.Remove();
            }
            double Med(List<double> l) { var s = l.OrderBy(x => x).ToList(); return s[s.Count / 2]; }
            Measure($"append 1,000 rows: median {Med(append):F3} ms, max {append.Max():F3} ms; replace 1,000: median {Med(replace):F3} ms, max {replace.Max():F3} ms (7 each, Editor; BW9 is the player's)");
            var swb = Stopwatch.StartNew();
            var rowsAll = ListBrowsers.ItemRows(db);
            Measure($"the browser's row list read once: {swb.Elapsed.TotalMilliseconds:F1} ms for {rowsAll.Count} rows");
            if (Med(append) <= 5 && Med(replace) <= 5) Check("append and replace of 1,000 rows within 5 ms (median)", true, $"{Med(append):F3} / {Med(replace):F3} ms");
            else Warn("BR3W1 append or replace of 1,000 rows over 5 ms (median)", $"{Med(append):F3} / {Med(replace):F3} ms");
        }
        finally { host.Dispose(); }
    }

    // ------------------------------------------------------------ BR7

    static void BR7()
    {
        var host = NewHost();
        var db = host.Db;
        GeneratedGroup g = null;
        try
        {
            int G = FreeGroup(db, 731490);
            int calls = 0;
            g = db.Generated.Add(G, "Writes", TestGuid, TestGuid, r =>
            {
                calls++;
                if (r.Id == 2) host.Session.AddFolder();   // the function changes the scene
                return Cube("w" + r.Id);
            });
            g.SetRows(new[] { new GeneratedRowSpec(0, 1, "Quiet"), new GeneratedRowSpec(0, 2, "Writes") });
            string quiet = AddNode(host, ItemState(TestGuid, 1, G, 0));
            Check("a function that leaves the scene alone: no finding", host.Rt.FindItem(quiet)?.Source == "generated" && FindingCount(host, "item.generatorWrote") == 0, "calls " + calls);
            long v0 = host.Session.Commands.Version;
            string loud = AddNode(host, ItemState(TestGuid, 2, G, 0));
            var it = host.Rt.FindItem(loud);
            Check("a function that changes the scene: item.generatorWrote", FindingCount(host, "item.generatorWrote", loud) == 1, "findings " + FindingCount(host, "item.generatorWrote") + ", version " + v0 + " -> " + host.Session.Commands.Version);
            Check("the build goes on: the item is its row's object", it != null && it.State == RealizationState.Built && it.Source == "generated" && !it.IsFallback, "state " + it?.State + ", source " + it?.Source);
        }
        finally
        {
            g?.Remove();
            host.Dispose();
        }
    }

    // ------------------------------------------------------------ BV8

    static void BV8()
    {
        var host = NewHost();
        var db = host.Db;
        PluginEngineServices view = null;
        try
        {
            int G = FreeGroup(db, 731490);
            var guard = new PluginGuard("stp3.test.rows.bv8", null, 100000);
            view = new PluginEngineServices("stp3.test.rows.bv8", guard, null, null, () => host.Rt, null);
            var u = (HS2Next.Api.Unity.IUnityPluginServices)view;
            Renderer Part(Transform root, string name, string shader)
            {
                var c = Cube(name, 0);
                c.transform.SetParent(root, false);
                var sh = Shader.Find(shader);
                var r = c.GetComponent<MeshRenderer>();
                if (sh) r.sharedMaterial = new Material(sh) { name = name + "_mat" };
                return r;
            }
            string mosaicShader = HS2Next.Chara.Mosaic.MosaicSet.ShaderName;
            var grp = u.ItemRows.AddGroup(G, "Members", req =>
            {
                var root = new GameObject("members");
                Part(root.transform, "shell", mosaicShader);
                Part(root.transform, "o_mnpa", "HS2Next/Standard");
                var third = Part(root.transform, "third", "HS2Next/Standard");
                u.AddMosaic(third);
                Part(root.transform, "plain", "HS2Next/Standard");
                return root;
            });
            grp.SetRows(new[] { new HS2Next.Api.Unity.ItemRow { Category = 0, Id = 1, Name = "Members" } });
            string n = AddNode(host, ItemState("stp3.test.rows.bv8", 1, G, 0));
            var it = host.Rt.FindItem(n);
            if (!Check("the generated item is built", it != null && it.Source == "generated" && it.Instance, "source " + it?.Source)) return;
            var tracked = HS2Next.Chara.Mosaic.MosaicSet.Standalone();
            string Desc(Renderer r) => r.name + ": member " + HS2Next.Chara.Mosaic.MosaicSet.IsMember(r) + ", added " + HS2Next.Chara.Mosaic.MosaicSet.IsAdded(r)
                + ", tracked " + tracked.Any(m => m.Renderer == r) + ", slots " + string.Join("/", r.sharedMaterials.Select(m => m ? (m.shader ? m.shader.name : "no shader") : "null"));
            var byName = it.Instance.GetComponentsInChildren<Renderer>(true).ToDictionary(r => r.name, r => r);
            Info("shader " + mosaicShader + " found: " + (bool)Shader.Find(mosaicShader));
            foreach (var r in byName.Values) Info(Desc(r));
            bool Member(string name) => byName.TryGetValue(name, out var r) && HS2Next.Chara.Mosaic.MosaicSet.IsMember(r) && tracked.Any(m => m.Renderer == r);
            Check("a renderer with a mosaic material is a member, tracked from its build", Member("shell"), byName.TryGetValue("shell", out var a) ? Desc(a) : "missing");
            Check("a renderer named o_mnpa is a member, added at its build", Member("o_mnpa") && HS2Next.Chara.Mosaic.MosaicSet.IsAdded(byName["o_mnpa"]), byName.TryGetValue("o_mnpa", out var b) ? Desc(b) : "missing");
            Check("a renderer the plugin added with AddMosaic is a member", Member("third") && HS2Next.Chara.Mosaic.MosaicSet.IsAdded(byName["third"]), byName.TryGetValue("third", out var c3) ? Desc(c3) : "missing");
            Check("a plain renderer is none (the rule reads names and materials, nothing else)", byName.TryGetValue("plain", out var p) && !HS2Next.Chara.Mosaic.MosaicSet.IsMember(p) && !tracked.Any(m => m.Renderer == p),
                p ? Desc(p) : "missing");
        }
        finally
        {
            view?.Unload();
            host.Dispose();
        }
    }

    // ------------------------------------------------------------ BR10

    static void BR10()
    {
        var host = NewHost();
        var db = host.Db;
        var keepMarker = AppItemRows.MarkerObject;
        var keepBake = ItemInstanceSource.Bake;
        try
        {
            Check("a test host does not register the app's group", !db.Generated.Holds(AppItemRows.Group), db.Generated.Groups.Count + " groups");
            var g = AppItemRows.Register(db, UserDataDir);
            if (!Check("the app's group registers", g != null && g.Owner == AppItemRows.Owner, g?.ToString())) return;
            Check("its four markers: hs2next.cut#1-4 in category 1, named Plane, Box, Tear, Hole", g.Rows.Select(r => r.Key + " " + r.Category + " " + r.Name).SequenceEqual(new[]
                { "hs2next.cut#1 1 Plane", "hs2next.cut#2 1 Box", "hs2next.cut#3 1 Tear", "hs2next.cut#4 1 Hole" }), string.Join(", ", g.Rows.Select(r => r.Key + " " + r.Category + " " + r.Name)));
            db.TryGetItemGroup(AppItemRows.Group, out var lg);
            Check("the group is «Chara to Item»; with no bake, category 0 «Baked objects» is not listed, category 1 «Cut tools» is", lg != null && lg.Name == AppItemRows.GroupName
                && db.CategoriesOf(lg).Select(c => c.Number + " " + c.Name).SequenceEqual(new[] { "1 " + AppItemRows.MarkersName })
                && db.CategoriesOf(lg, true).Select(c => c.Number + " " + c.Name).SequenceEqual(new[] { "0 " + AppItemRows.BakesName, "1 " + AppItemRows.MarkersName }),
                string.Join(", ", db.CategoriesOf(lg, true).Select(c => c.Number + " " + c.Name + (c.Visible ? "" : " (not listed)"))));
            // the store's rows, as the bake code gives them
            g.SetRows(AppItemRows.BakesCategory, new[]
            {
                new GeneratedRowSpec(0, 73100001, "First bake", new[] { "cf_J_Hips" }, AppItemRows.BakeGuid),
                new GeneratedRowSpec(0, 73100002, "Second bake", null, AppItemRows.BakeGuid),
            });
            var found = (host.Call(new { cmd = "item.search", group = AppItemRows.Group, limit = 100 })["items"] as JArray)?.OfType<JObject>().Select(x => (string)x["id"] + " " + (string)x["categoryName"]).ToList();
            Check("item.search lists the store's rows and the markers", found != null && found.SequenceEqual(new[] { "hs2next.bake#73100001 Baked objects", "hs2next.bake#73100002 Baked objects",
                "hs2next.cut#1 Cut tools", "hs2next.cut#2 Cut tools", "hs2next.cut#3 Cut tools", "hs2next.cut#4 Cut tools" }), found == null ? Short(host.LastText) : string.Join(", ", found));
            var rows = ListBrowsers.ItemRows(db);
            Check("the browser lists the six rows last", rows.Skip(rows.Count - 6).SequenceEqual(g.Rows) && g.Rows.Count == 6, rows.Count + " rows");
            Check("the browser labels the group from the strings, else its own names", ListBrowsers.AppRowLabels.Is(db, AppItemRows.Group) && !ListBrowsers.AppRowLabels.Is(db, 0)
                && ListBrowsers.AppRowLabels.Group().Length > 0 && ListBrowsers.AppRowLabels.Category(1).Length > 0,
                "group «" + ListBrowsers.AppRowLabels.Group() + "», categories «" + ListBrowsers.AppRowLabels.Category(0) + "», «" + ListBrowsers.AppRowLabels.Category(1) + "», a marker «" + ListBrowsers.AppRowLabels.Row(g.Rows[2]) + "»");
            Check("a second registration is refused (the number is held)", AppItemRows.Register(db, UserDataDir) == null && db.Counter(GeneratedRows.TakenCode) == 1,
                "counter " + db.Counter(GeneratedRows.TakenCode));

            // a marker's object comes from the cut code; with none, the fallback and the finding
            AppItemRows.MarkerObject = null;
            string m0 = AddRef(host, "hs2next.cut#2");
            Check("a marker with no marker code yet: the fallback item, item.generatorFailed", host.Rt.FindItem(m0)?.IsFallback == true && FindingCount(host, "item.generatorFailed", m0) == 1,
                "fallback " + host.Rt.FindItem(m0)?.IsFallback);
            AppItemRows.MarkerObject = r => r.Id == AppItemRows.Box ? Cube("box marker", 0) : null;
            string m1 = AddRef(host, "hs2next.cut#2");
            Check("a marker with its object: built from the marker code", host.Rt.FindItem(m1)?.Source == "generated" && host.Rt.FindItem(m1).Instance.name == "box marker",
                "source " + host.Rt.FindItem(m1)?.Source);
            Check("the app's group comes off a test's lists", AppItemRows.Remove(db) && !db.Generated.Holds(AppItemRows.Group), "");

            // a list that defines 731406: a group of that number in the item group space of a list database, as a list file puts it
            var listed = (StudioDb)Activator.CreateInstance(typeof(StudioDb), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new object[] { new StudioLoadOptions() }, null);
            var space = typeof(StudioDb).GetField("itemSpace", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(listed);
            var groups = (Dictionary<int, StudioGroup>)space.GetType().GetField("Groups").GetValue(space);
            groups[AppItemRows.Group] = new StudioGroup { Kind = StudioListKind.ItemGroup, Number = AppItemRows.Group, Name = "A list's group" };
            var refused = AppItemRows.Register(listed, UserDataDir);
            Check("a list that defines 731406: the app's group is not added, rows.groupTaken counted with a diagnostic line", refused == null && !listed.Generated.Holds(AppItemRows.Group)
                && listed.Counter(GeneratedRows.TakenCode) == 1 && listed.Diagnostics.Any(d => d.StartsWith(GeneratedRows.TakenCode, StringComparison.Ordinal)),
                listed.Diagnostics.LastOrDefault());
            // a bake still loads: the bake source answers before any row is looked for
            var sv = new RuntimeServices { Catalog = services.Catalog, Db = listed, Hs2Root = services.Hs2Root, Settings = services.Settings };
            var s = AppSettings.Defaults(Array.Empty<string>());
            s.Content = CardResolveTest.Settings; s.UserData = UserDataDir; s.Bridge.OutputDir = OutputDir;
            var o = sv.NewSessionOptions();
            o.Ids = new CountingIdSource(DefaultSeed);
            var lh = new Host { App = AppCore.ForTests(sv, s, null, o) };
            try
            {
                var bakeRow = new StudioItem { Key = new ContentId(AppItemRows.BakeGuid, 73100001), Group = AppItemRows.Group, Category = 0, Name = "First bake" };
                int asked = 0;
                ItemInstanceSource.Bake = (ItemRealization item, out ItemInstance made) =>
                {
                    asked++;
                    made = default;
                    if (!((item.Node.State as ItemNodeState)?.Ref ?? "").StartsWith(AppItemRows.BakeGuid + "#", StringComparison.Ordinal)) return false;
                    made = new ItemInstance { Object = Cube("a bake", 0), Row = bakeRow };
                    return true;
                };
                string bk = AddNode(lh, ItemState(AppItemRows.BakeGuid, 73100001, AppItemRows.Group, 0));
                var bi = lh.Rt.FindItem(bk);
                Check("an item of a bake still builds from its bake there", bi != null && bi.Source == "bake" && !bi.IsFallback && ReferenceEquals(bi.Row, bakeRow) && asked == 1,
                    "source " + bi?.Source + ", row " + bi?.Row + ", asked " + asked);
            }
            finally
            {
                ItemInstanceSource.Bake = keepBake;
                lh.Dispose();
            }
        }
        finally
        {
            AppItemRows.MarkerObject = keepMarker;
            ItemInstanceSource.Bake = keepBake;
            AppItemRows.Remove(db);
            host.Dispose();
        }
    }

    static void BR10Store()
    {
        var host = NewHost();
        var db = host.Db;
        string dir = Path.Combine(OutDir, "rows_store_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            var g0 = AppItemRows.Register(db, dir);
            Check("with no store yet the group lists no bake and nothing is created", g0 != null && g0.Rows.Count(r => r.Category == AppItemRows.BakesCategory) == 0 && !Directory.Exists(dir),
                g0?.Rows.Count + " rows; folder made " + Directory.Exists(dir));
            AppItemRows.Remove(db);
            var store = BakeStore.ForUserData(dir);
            KeyValuePair<string, byte[]> Blob(byte tag) { var b = BakeBlob.Encode(BakeBlobKind.Main, new[] { tag }); return new KeyValuePair<string, byte[]>(BakeBlob.Sha256(b), b); }
            var b1 = Blob(1);
            var b2 = Blob(2);
            var r1 = store.Add(new BakeStoreRow { Id = new string('a', 32), Name = "Shoes", Kind = "parts", Main = b1.Key, Fk = new List<string> { "cf_J_Foot01_L", "cf_J_Toes01_L" } }, new[] { b1 });
            var r2 = store.Add(new BakeStoreRow { Id = new string('b', 32), Name = "Whole", Kind = "chara", Main = b2.Key }, new[] { b2 });
            var g = AppItemRows.Register(db, dir);
            string Bakes() => string.Join(", ", g.Rows.Where(r => r.Category == AppItemRows.BakesCategory).Select(r => r.Key + " " + r.Name + " [" + string.Join(" ", r.Bones ?? new string[0]) + "]"));
            string want = AppItemRows.BakeGuid + "#" + r1.Number + " Shoes [cf_J_Foot01_L cf_J_Toes01_L], " + AppItemRows.BakeGuid + "#" + r2.Number + " Whole []";
            Check("registered, the group lists the store's live rows in category 0 with their FK names (the store holds 2 rows)", g != null && Bakes() == want
                && g.Rows.Count(r => r.Category == AppItemRows.MarkersCategory) == 4, Bakes());
            store.Rename(r1.Number, "Red shoes");
            store.Delete(r2.Number);
            int n = AppItemRows.RefreshBakes(db);
            Check("after a rename and a delete in the store, RefreshBakes lists its live rows again; the markers stay", n == 1 && Bakes() == AppItemRows.BakeGuid + "#" + r1.Number + " Red shoes [cf_J_Foot01_L cf_J_Toes01_L]"
                && g.Rows.Count(r => r.Category == AppItemRows.MarkersCategory) == 4, n + ": " + Bakes());
            var found = (host.Call(new { cmd = "item.search", group = AppItemRows.Group, category = AppItemRows.BakesCategory, limit = 10 })["items"] as JArray)?.OfType<JObject>().Select(x => (string)x["name"]).ToList();
            Check("item.search lists the live row", found != null && found.SequenceEqual(new[] { "Red shoes" }), found == null ? Short(host.LastText) : string.Join(", ", found));
        }
        finally
        {
            AppItemRows.Remove(db);
            host.Dispose();
            // only what this group made: the store's files and folders under its own folder
            string bakes = Path.Combine(dir, BakeStore.FolderName), blobs = Path.Combine(bakes, BakeStore.BlobsName);
            try
            {
                if (Directory.Exists(blobs)) { foreach (var f in Directory.GetFiles(blobs)) File.Delete(f); Directory.Delete(blobs); }
                if (Directory.Exists(bakes)) { foreach (var f in Directory.GetFiles(bakes)) File.Delete(f); Directory.Delete(bakes); }
                if (Directory.Exists(dir)) Directory.Delete(dir);
            }
            catch (Exception e) { Info("the test store's folder was not removed: " + e.Message); }
        }
    }

    /// <summary>Removes a test store's files and folders (index, blobs; nothing else), one by one.</summary>
    static void RemoveStoreFiles(string bakes)
    {
        string blobs = Path.Combine(bakes, BakeStore.BlobsName);
        if (Directory.Exists(blobs)) { foreach (var f in Directory.GetFiles(blobs)) File.Delete(f); Directory.Delete(blobs); }
        if (Directory.Exists(bakes)) { foreach (var f in Directory.GetFiles(bakes)) File.Delete(f); Directory.Delete(bakes); }
    }

    static void BR10Ints()
    {
        var host = NewHost();
        var db = host.Db;
        string bakes = Path.Combine(UserDataDir, BakeStore.FolderName);
        GeneratedGroup pg = null;
        try
        {
            RemoveStoreFiles(bakes);   // what an earlier run of this group left
            // a store of the host's UserData, so that the bake code finds the row when it is added
            var store = BakeStore.ForUserData(UserDataDir);
            var blob = BakeBlob.Encode(BakeBlobKind.Main, new byte[] { 7 });
            string sha = BakeBlob.Sha256(blob);
            var row = store.Add(new BakeStoreRow { Id = new string('c', 32), Name = "Gloves", Kind = "parts", Main = sha, Fk = new List<string> { "cf_J_Hand_L" } },
                new[] { new KeyValuePair<string, byte[]>(sha, blob) });
            var g = AppItemRows.Register(db, UserDataDir);
            if (!Check("the store's row is listed (present)", g != null && g.TryGetRowById(row.Number, AppItemRows.BakesCategory, out _), "row " + row.Number)) return;

            JObject Add(object req, out ItemNodeState st)
            {
                var r = host.Call(req);
                host.Rt.ApplyNow();
                string k = (string)r["added"]?[0]?["key"];
                st = k != null ? host.Session.Model.Find(k)?.State as ItemNodeState : null;
                return r;
            }
            var r1 = Add(new { cmd = "item.add", group = AppItemRows.Group, category = AppItemRows.BakesCategory, no = row.Number }, out var s1);
            Check("item.add {group: 731406, category: 0, no: <number>} adds the store's row with its bake data", r1.Value<bool?>("ok") == true && s1 != null
                && s1.Ref == AppItemRows.BakeGuid + "#" + row.Number && s1.Group == AppItemRows.Group && s1.Category == AppItemRows.BakesCategory && s1.Bake != null && s1.Bake.No == row.Number
                && s1.Bake.Main == "res/" + sha + ".bin", s1 == null ? Short(host.LastText) : s1.Ref + " " + s1.Group + "/" + s1.Category + ", bake " + (s1.Bake == null ? "none" : s1.Bake.Main + " no " + s1.Bake.No));
            var r2 = Add(new { cmd = "item.add", group = AppItemRows.Group, category = AppItemRows.MarkersCategory, no = AppItemRows.Box }, out var s2);
            Check("item.add {group: 731406, category: 1, no: 2} adds the box marker's row", r2.Value<bool?>("ok") == true && s2 != null && s2.Ref == AppItemRows.CutGuid + "#2"
                && s2.Category == AppItemRows.MarkersCategory && s2.Bake == null, s2 == null ? Short(host.LastText) : s2.Ref + " " + s2.Group + "/" + s2.Category);
            var r3 = Add(new { cmd = "item.add", group = AppItemRows.Group, no = AppItemRows.Tear }, out var s3);
            Check("without the category the number alone names the row (ids are unique in a group)", r3.Value<bool?>("ok") == true && s3 != null && s3.Ref == AppItemRows.CutGuid + "#3"
                && s3.Category == AppItemRows.MarkersCategory, s3 == null ? Short(host.LastText) : s3.Ref + " " + s3.Group + "/" + s3.Category);
            var r4 = host.Call(new { cmd = "item.add", group = AppItemRows.Group, category = AppItemRows.BakesCategory, no = 99 });
            Check("an unknown number answers not_found", r4.Value<bool?>("ok") == false && (string)r4["code"] == "not_found", Short(host.LastText));

            int G = FreeGroup(db, 731490);
            pg = db.Generated.Add(G, "Rows test", TestGuid, TestGuid, rq => Cube("row" + rq.Id));
            pg.SetRows(new[] { new GeneratedRowSpec(0, 1, "Row one"), new GeneratedRowSpec(1, 2, "Row two") });
            var r5 = Add(new { cmd = "item.add", group = G, category = 1, no = 2 }, out var s5);
            Check("a plugin's row by its group, category and number", r5.Value<bool?>("ok") == true && s5 != null && s5.Ref == TestGuid + "#2" && s5.Group == G && s5.Category == 1
                && host.Rt.FindItem((string)r5["added"]?[0]?["key"])?.Source == "generated", s5 == null ? Short(host.LastText) : s5.Ref + " " + s5.Group + "/" + s5.Category);
            int undo = (host.Call(new { cmd = "history" })["undo"] as JArray)?.Count ?? -1;
            Check("each add is one history entry", undo == 4, undo + " entries");
        }
        finally
        {
            pg?.Remove();
            AppItemRows.Remove(db);
            host.Dispose();
            try { RemoveStoreFiles(bakes); }
            catch (Exception e) { Info("the test store's files were not removed: " + e.Message); }
        }
    }

    // ------------------------------------------------------------ BR4

    static byte[] BR4()
    {
        byte[] first;
        string n1, n3;
        using (var a = HostWithPlugins())
        {
            var e = PluginEntry(a, ProbeGuid);
            if (!Check("the row probe is loaded and holds its group", (string)e?["state"] == "loaded" && a.Db.Generated.Group(ProbeGroup)?.Owner == ProbeGuid
                && (string)ProbeState(a)["addError"] == null, Short(e?.ToString(Formatting.None)) + "; " + Short(ProbeState(a).ToString(Formatting.None)))) return null;
            n1 = AddRef(a, ProbeGuid + "#1");
            n3 = AddRef(a, ProbeGuid + "#3");
            a.Ok(new { cmd = "item.set", key = n1, set = new Dictionary<string, object> { ["fk.enabled"] = true, ["fk.bones[\"rp1\"]"] = new[] { 0, 0, 30 }, ["colors[0].main"] = new[] { 0.2, 0.4, 0.6, 1 } } }, "item.set FK and a colour");
            a.Frames(2);
            var i1 = a.Rt.FindItem(n1);
            Check("with the plugin the scene's items are their rows' objects (present)", i1?.Source == "generated" && a.Rt.FindItem(n3)?.Source == "generated" && i1.Features?.FkBones.Contains("rp1") == true,
                "sources " + i1?.Source + ", " + a.Rt.FindItem(n3)?.Source);
            first = a.Session.ToBytes(new SaveOptions { Clock = () => FixedClock });
        }
        Check("the plugin's group left with its host", !services.Db.Generated.Holds(ProbeGroup) && !services.Db.Generated.Holds(ShapesGroup), services.Db.Generated.Groups.Count + " groups");
        using (var b = NewHost())
        {
            var lr = b.Session.Load(first);
            if (!Check("without the plugin the scene loads", lr.Ok, lr.Error?.Message)) return first;
            b.Rt.Apply();
            var i1 = b.Rt.FindItem(n1);
            var i3 = b.Rt.FindItem(n3);
            Check("its items load as the fallback item with item.missing", i1?.IsFallback == true && i3?.IsFallback == true && i1.Row?.Key == StudioDb.FallbackItem
                && FindingCount(b, "item.missing") == 2, "fallback " + i1?.IsFallback + "/" + i3?.IsFallback + ", item.missing " + FindingCount(b, "item.missing"));
            var again = b.Session.ToBytes(new SaveOptions { Clock = () => FixedClock });
            Check("saved again, the scene is byte-equal (every member of the two sections kept)", again.SequenceEqual(first), again.Length + " / " + first.Length + " bytes");
        }
        using (var c = HostWithPlugins())
        {
            var lr = c.Session.Load(first);
            if (!Check("with the plugin the scene loads", lr.Ok, lr.Error?.Message)) return first;
            c.Rt.Apply();
            c.Frames(2);
            var i1 = c.Rt.FindItem(n1);
            var bone = i1?.Instance ? i1.Instance.transform.Find("rp1") : null;
            Check("with the plugin the same scene builds the rows (present), FK as saved", i1?.Source == "generated" && c.Rt.FindItem(n3)?.Source == "generated" && bone
                && Vector3.Distance(bone.localEulerAngles, new Vector3(0, 0, 30)) < 1e-3f, "sources " + i1?.Source + ", " + c.Rt.FindItem(n3)?.Source + ", rp1 " + (bone ? bone.localEulerAngles.ToString("F3") : "none"));
            var again = c.Session.ToBytes(new SaveOptions { Clock = () => FixedClock });
            Check("saved with the plugin, the scene is byte-equal too", again.SequenceEqual(first), again.Length + " / " + first.Length + " bytes");
        }
        return first;
    }

    // ------------------------------------------------------------ BR5

    static void BR5(byte[] scene)
    {
        if (!Check("BR4's scene is there", scene != null)) return;
        var h = NewHost();
        try
        {
            var lr = h.Session.Load(scene);
            if (!Check("the scene loads before any plugin", lr.Ok, lr.Error?.Message)) return;
            h.Rt.Apply();
            var items = h.Rt.Realizations.OfType<ItemRealization>().Where(x => (x.Node.State as ItemNodeState)?.Group == ProbeGroup).ToList();
            Check("its items are the fallback item before (present)", items.Count == 2 && items.All(x => x.IsFallback), items.Count + " items, fallback " + string.Join(",", items.Select(x => x.IsFallback)));
            var part = GeneratedRowRebuilds.Of(h.Rt);
            Check("a fallback item of a group no list defines has the rebuild part", part != null, h.Rt.Parts.Count + " parts");
            // read in process, with no frame between the plugin's load and the check (a bridge call pumps frames)
            int makesBefore = ProbeMakes();
            h.App.LoadPlugins(PluginsDir);
            int makes0 = ProbeMakes(), rebuilt0 = part?.Rebuilt ?? -1;
            Check("the plugin's group came at its load; nothing was built yet", h.Db.Generated.Holds(ProbeGroup) && items.All(x => x.IsFallback) && makes0 == makesBefore && rebuilt0 == 0,
                $"read in process, no frame: the probe's builds {makesBefore} -> {makes0}, rebuilt {rebuilt0}, fallback {string.Join(",", items.Select(x => x.IsFallback))}");
            h.Frames(1);
            var byRef = items.ToDictionary(x => (x.Node.State as ItemNodeState).Ref, x => x);
            Check("at the next apply both items are built again from their rows", items.All(x => !x.IsFallback && x.Source == "generated") && part?.Rebuilt == 2,
                "rebuilt " + part?.Rebuilt + ", sources " + string.Join(",", items.Select(x => x.Source)));
            var s1 = (int)ProbeState(h)["makes"];
            Check("one build each: the function ran twice", s1 - makes0 == 2, (s1 - makes0) + " calls");

            var one = byRef[ProbeGuid + "#1"];
            var three = byRef[ProbeGuid + "#3"];
            var inst1 = one.Instance;
            var inst3 = three.Instance;
            h.Ok(new { cmd = "rowprobe.refresh", row = 1 }, "rowprobe.refresh 1");
            h.Frames(1);
            Check("Refresh(1) builds the item of row 1 again, not the other", one.Instance != inst1 && three.Instance == inst3 && part.Rebuilt == 3 && (int)ProbeState(h)["makes"] - s1 == 1,
                "rebuilt " + part.Rebuilt + ", row 1 new object " + (one.Instance != inst1) + ", row 3 same " + (three.Instance == inst3));
            inst1 = one.Instance;
            h.Ok(new { cmd = "rowprobe.rename", row = 3, name = "Probe three renamed" }, "rowprobe.rename 3");
            h.Frames(1);
            Check("a changed row builds its items again, the unchanged row's stay", three.Instance != inst3 && one.Instance == inst1 && part.Rebuilt == 4 && three.Row?.Name == "Probe three renamed",
                "rebuilt " + part.Rebuilt + ", row 3 " + three.Row?.Name);
            inst3 = three.Instance;
            h.Ok(new { cmd = "rowprobe.rename", row = 3, name = "Probe three renamed" }, "rowprobe.rename 3 to the same name");
            h.Frames(1);
            Check("rows set again unchanged build nothing", three.Instance == inst3 && one.Instance == inst1 && part.Rebuilt == 4, "rebuilt " + part.Rebuilt);
        }
        finally { h.Dispose(); }
    }

    // ------------------------------------------------------------ BR8

    static void BR8()
    {
        var h = HostWithPlugins();
        try
        {
            string n1 = AddRef(h, ProbeGuid + "#1"), n2 = AddRef(h, ProbeGuid + "#2");
            var i1 = h.Rt.FindItem(n1);
            var i2 = h.Rt.FindItem(n2);
            if (!Check("the rows existed: two items built from them (present)", i1?.Source == "generated" && i2?.Source == "generated" && h.Db.Generated.Holds(ProbeGroup), "")) return;
            var inst1 = i1.Instance;
            h.Ok(new { cmd = "plugin.disable", guid = ProbeGuid }, "plugin.disable");
            h.Frames(1);
            Check("disabled: the group and its rows are gone", !h.Db.Generated.Holds(ProbeGroup) && !h.Db.TryGetItemGroup(ProbeGroup, out _), (string)PluginEntry(h, ProbeGuid)?["state"]);
            Check("the items keep their objects", i1.Instance == inst1 && i1.Source == "generated" && i2.Source == "generated", "");
            Rebuild(h.Rt, i1);
            Check("an item's next build falls back", i1.IsFallback && i1.Row?.Key == StudioDb.FallbackItem && FindingCount(h, "item.missing", n1) >= 1, "fallback " + i1.IsFallback);
            h.Ok(new { cmd = "plugin.enable", guid = ProbeGuid }, "plugin.enable");
            h.Frames(1);
            Check("enabled again: the group is back and the fallback item is built from its row", h.Db.Generated.Holds(ProbeGroup) && i1.Source == "generated" && !i1.IsFallback, "source " + i1.Source);
            var inst2 = i2.Instance;
            h.Ok(new { cmd = "rowprobe.dispose" }, "rowprobe.dispose");
            Check("a disposed group is gone, the items keep their objects", !h.Db.Generated.Holds(ProbeGroup) && i2.Instance == inst2 && i2.Source == "generated", "");
            h.Ok(new { cmd = "rowprobe.add" }, "rowprobe.add");
            Check("the plugin can add it again", h.Db.Generated.Holds(ProbeGroup), (string)ProbeState(h)["addError"]);
        }
        finally { h.Dispose(); }
        Check("the plugin's groups left with its host", !services.Db.Generated.Holds(ProbeGroup) && !services.Db.Generated.Holds(ShapesGroup), services.Db.Generated.Groups.Count + " groups");
    }

    // ------------------------------------------------------------ BR9

    /// <summary>The skinned vertices of a renderer in world space, on the CPU (linear blend of the bone matrices and bind poses).</summary>
    static Vector3[] Skinned(SkinnedMeshRenderer smr)
    {
        var m = smr.sharedMesh;
        var v = m.vertices;
        var w = m.boneWeights;
        var bp = m.bindposes;
        var bones = smr.bones;
        var mats = new Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++) mats[i] = bones[i].localToWorldMatrix * bp[i];
        var o = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            var b = w[i];
            o[i] = mats[b.boneIndex0].MultiplyPoint3x4(v[i]) * b.weight0 + mats[b.boneIndex1].MultiplyPoint3x4(v[i]) * b.weight1
                 + mats[b.boneIndex2].MultiplyPoint3x4(v[i]) * b.weight2 + mats[b.boneIndex3].MultiplyPoint3x4(v[i]) * b.weight3;
        }
        return o;
    }

    static void BR9()
    {
        byte[] scene;
        string torus;
        var ring = Enumerable.Range(0, 8).Select(i => "ring" + i).ToArray();
        using (var h = HostWithPlugins())
        {
            var e = PluginEntry(h, ShapesGuid);
            if (!Check("the sample is loaded and holds its group", (string)e?["state"] == "loaded" && h.Db.Generated.Group(ShapesGroup)?.Owner == ShapesGuid, Short(e?.ToString(Formatting.None)))) return;
            var found = (h.Call(new { cmd = "item.search", group = ShapesGroup, limit = 100 })["items"] as JArray)?.OfType<JObject>().Select(x => (string)x["id"] + " " + (string)x["name"] + " " + (string)x["categoryName"]).ToList();
            Check("its five rows are listed", found != null && found.SequenceEqual(new[] { ShapesGuid + "#1 Cube Shapes", ShapesGuid + "#2 Sphere Shapes", ShapesGuid + "#3 Cylinder Shapes",
                ShapesGuid + "#4 Torus Shapes", ShapesGuid + "#5 Arrow Shapes" }), found == null ? Short(h.LastText) : string.Join(", ", found));
            foreach (int id in new[] { 1, 2, 3, 5 })
            {
                string k = AddRef(h, ShapesGuid + "#" + id);
                var it = h.Rt.FindItem(k);
                var mf = it?.Instance ? it.Instance.GetComponentInChildren<MeshFilter>() : null;
                Check("row " + id + " builds its mesh", it?.Source == "generated" && mf && mf.sharedMesh && mf.sharedMesh.vertexCount > 0 && it.Writer.OwnedMaterials == 1,
                    "source " + it?.Source + ", vertices " + (mf && mf.sharedMesh ? mf.sharedMesh.vertexCount : 0) + ", owned materials " + it?.Writer.OwnedMaterials);
            }
            torus = AddRef(h, ShapesGuid + "#4");
            var t = h.Rt.FindItem(torus);
            var smr = t?.Instance ? t.Instance.GetComponentInChildren<SkinnedMeshRenderer>() : null;
            if (!Check("item.add builds the torus, a skinned mesh with eight bones", t?.Source == "generated" && smr && smr.bones.Length == 8, "source " + t?.Source + ", bones " + (smr ? smr.bones.Length : 0))) return;
            Check("its eight FK bones are found", t.Features != null && t.Features.FkBones.SequenceEqual(ring), string.Join(",", t.Features?.FkBones ?? new List<string>()));
            h.Frames(2);
            var before = Skinned(smr);
            h.Ok(new { cmd = "item.set", key = torus, set = new Dictionary<string, object> { ["fk.enabled"] = true, ["fk.bones[\"ring2\"]"] = new[] { 0, -45, 40 }, ["fk.bones[\"ring5\"]"] = new[] { -30, -45, 0 } } }, "item.set FK of ring2 and ring5");
            h.Frames(2);
            var after = Skinned(smr);
            float moved = 0;
            for (int i = 0; i < before.Length; i++) moved = Mathf.Max(moved, Vector3.Distance(before[i], after[i]));
            var r2 = smr.bones[2];
            Check("its FK bends it: vertices move at least 1 unit (present)", moved >= 1f && Vector3.Distance(r2.localEulerAngles, new Vector3(0, 315, 40)) < 1e-2f,
                $"largest move {moved:F3} units over {before.Length} vertices; ring2 local {r2.localEulerAngles.ToString("F2")}");
            scene = h.Session.ToBytes(new SaveOptions { Clock = () => FixedClock });
        }
        using (var b = NewHost())
        {
            var lr = b.Session.Load(scene);
            if (!Check("without the plugin the scene loads", lr.Ok, lr.Error?.Message)) return;
            b.Rt.Apply();
            var shapes = b.Rt.Realizations.OfType<ItemRealization>().Where(x => (x.Node.State as ItemNodeState)?.Group == ShapesGroup).ToList();
            Check("without it the five items are the fallback item", shapes.Count == 5 && shapes.All(x => x.IsFallback), shapes.Count + " items");
            Check("saved again, byte-equal", b.Session.ToBytes(new SaveOptions { Clock = () => FixedClock }).SequenceEqual(scene), scene.Length + " bytes");
        }
        using (var c = HostWithPlugins())
        {
            var lr = c.Session.Load(scene);
            if (!Check("with the plugin the scene loads", lr.Ok, lr.Error?.Message)) return;
            c.Rt.Apply();
            c.Frames(2);
            var t = c.Rt.FindItem(torus);
            var smr = t?.Instance ? t.Instance.GetComponentInChildren<SkinnedMeshRenderer>() : null;
            Check("with it the torus is back, bent as saved", t?.Source == "generated" && smr && Vector3.Distance(smr.bones[2].localEulerAngles, new Vector3(0, 315, 40)) < 1e-2f,
                "ring2 " + (smr ? smr.bones[2].localEulerAngles.ToString("F2") : "none"));
            Check("saved again, byte-equal", c.Session.ToBytes(new SaveOptions { Clock = () => FixedClock }).SequenceEqual(scene), scene.Length + " bytes");
        }
    }

    // ------------------------------------------------------------ BR6

    static void BR6()
    {
        var h = HostWithPlugins();
        try
        {
            const string hook = "item rows 731497";
            h.Ok(new { cmd = "rowprobe.clear" }, "rowprobe.clear");
            string n1 = AddRef(h, ProbeGuid + "#1");
            var it = h.Rt.FindItem(n1);
            if (!Check("the item is built from its row", it?.Source == "generated", "source " + it?.Source)) return;
            h.Ok(new { cmd = "rowprobe.mode", mode = "throw" }, "rowprobe.mode throw");
            h.Ok(new { cmd = "rowprobe.refresh", row = 1 }, "rowprobe.refresh");
            h.Frames(1);
            Check("a throwing function: the fallback item, item.generatorFailed, one fault of the plugin", it.IsFallback && it.Row?.Key == StudioDb.FallbackItem && it.Source == "bundle"
                && FindingCount(h, "item.generatorFailed", n1) == 1 && Faults(h, ProbeGuid, hook) == 1, "fallback " + it.IsFallback + ", findings " + FindingCount(h, "item.generatorFailed", n1) + ", faults " + Faults(h, ProbeGuid, hook));
            h.Ok(new { cmd = "rowprobe.mode", mode = "null" }, "rowprobe.mode null");
            h.Ok(new { cmd = "rowprobe.refresh", row = 1 }, "rowprobe.refresh");
            h.Frames(1);
            Check("a function that makes nothing: the same, a second fault", it.IsFallback && FindingCount(h, "item.generatorFailed", n1) == 2 && Faults(h, ProbeGuid, hook) == 2,
                "findings " + FindingCount(h, "item.generatorFailed", n1) + ", faults " + Faults(h, ProbeGuid, hook));
            h.Ok(new { cmd = "rowprobe.mode", mode = "ok" }, "rowprobe.mode ok");
            h.Ok(new { cmd = "rowprobe.refresh", row = 1 }, "rowprobe.refresh");
            h.Frames(1);
            Check("a function that builds again: the row's object", !it.IsFallback && it.Source == "generated" && Faults(h, ProbeGuid, hook) == 2, "source " + it.Source);
            h.Ok(new { cmd = "rowprobe.mode", mode = "throw" }, "rowprobe.mode throw");
            int k = 0;
            while ((string)PluginEntry(h, ProbeGuid)?["state"] == "loaded" && k < 12)
            {
                h.Call(new { cmd = "rowprobe.refresh", row = 1 });
                h.Frames(1);
                k++;
            }
            var e = PluginEntry(h, ProbeGuid);
            Check("ten faults stop the plugin and its group goes", (string)e?["state"] == "faulted" && Faults(h, ProbeGuid, hook) == 10 && !h.Db.Generated.Holds(ProbeGroup),
                "state " + (string)e?["state"] + " after " + k + " more refreshes, faults " + Faults(h, ProbeGuid, hook));
            Rebuild(h.Rt, it);
            Check("the item's next build is the fallback item", it.IsFallback, "source " + it.Source);
        }
        finally { h.Dispose(); }
        Check("the plugin's groups left with its host", !services.Db.Generated.Holds(ProbeGroup) && !services.Db.Generated.Holds(ShapesGroup), services.Db.Generated.Groups.Count + " groups");
    }

    /// <summary>The runtime's own rebuild of one item (what a structural change does).</summary>
    static void Rebuild(SceneRuntime rt, NodeRealization r) =>
        typeof(SceneRuntime).GetMethod("RebuildContent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(rt, new object[] { r });

    static string Throws(Action a)
    {
        try { a(); return null; }
        catch (Exception e) { return e.GetType().Name + ": " + e.Message; }
    }
}

public static partial class BakeTest
{
    /// <summary>The generated item rows (BakeRowsTest's groups BR1-BR10 and BV8) as a part of BakeTest.Run.</summary>
    static void Headless_Rows() => BakeRowsTest.RunInto(null, Check, Info, Warn, Measure, Want, Group);
}

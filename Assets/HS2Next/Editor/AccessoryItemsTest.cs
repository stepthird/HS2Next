// Batch test of the accessories offered as Studio items (item group 647158; GA6 is
// the HS2 comparison of Tools/hs2_compare.py, case accessoryItems). Headless (-nographics), edit mode, ManualStepping.
// Run: -executeMethod AccessoryItemsTest.Run [-only GA1,GA2]
//   GA1 the group: second in the item groups (sort 0 after group 0), 13 categories 351-363 named as the character lists name
//       them, rows per category = what HS2 Studio lists (measured) less the in-mod duplicate keys HS2 lists twice
//       (known class, maxCount 2); the base game's rows first (the Possess 99 row included), then the mods by GUID
//   GA2 a scene with 6 accessory items (base game and mod, three with chains): save -> load -> save byte-identical; every node
//       keeps ref = the accessory's guid#id, group 647158 and its category; `requires` names hs2next.accessoryItems
//   GA3 the group switched off: absent from the lists; GA2's scene loads with the items unresolved (the fallback item stands in,
//       finding item.missing) and saves byte-identical
//   GA4 illusion.hs2#5 as accessory 351 and as item: two nodes, two rows, each from its own table
//   GA5 30 rows realized: built, no exception, the bundles owned by the item ("item:<node id 8>") and no load outside an owner
//       scope, no colour slot when the prefab has no item component, the chains of the prefab
// Report: Spike/out/items/accessory_items_report.txt, first line "SUMMARY: N PASS, M FAIL"; EditorApplication.Exit(1) on a FAIL.
// Numbers and data names only; nothing is rendered.
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
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class AccessoryItemsTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "items");
    static readonly DateTime FixedClock = new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc);

    // HS2 Studio's rows per accessory category with the accessory group on, and the base game's rows among them
    static readonly Dictionary<int, (int rows, int vanilla)> Hs2Rows = new Dictionary<int, (int, int)>
    {
        [351] = (976, 151), [352] = (87, 9), [353] = (67, 23), [354] = (118, 15), [355] = (306, 55), [356] = (42, 18), [357] = (51, 8),
        [358] = (226, 29), [359] = (110, 21), [360] = (117, 27), [361] = (265, 92), [362] = (88, 15), [363] = (60, 3),
    };
    // the references HS2 lists twice (a key defined twice inside one mod; one row here): known class inModDuplicateKey, maxCount 2
    static readonly (int category, string reference)[] Hs2Twice = { (351, "sena p_assault_f#86021"), (352, "tubge.mssp#1") };

    // GA2's rows: (category, reference, chains in HS2)
    static readonly (int category, string reference, int chains)[] SceneRows =
    {
        (351, "illusion.hs2#5", 0), (351, "illusion.hs2#43", 1), (355, "illusion.hs2#0", 0), (361, "illusion.hs2#43", 0),
        (351, "com.vvv.achair000f#627027", 5), (352, "sjjpl.acs.ear.pack#385007", 1),
    };

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

    static void Guard(string id, string title, Action a)
    {
        if (only != null && !only.Contains(id)) return;
        current = id;
        rep.AppendLine();
        rep.AppendLine("== " + id + " " + title);
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

    // ---------------------------------------------------------------- fixture

    sealed class Scene : IDisposable
    {
        public SceneSession Session;
        public SceneRuntime Runtime;

        public Scene(RuntimeServices services)
        {
            Session = SceneRuntimeTest.NewSession(services);
            Runtime = new SceneRuntime(Session, services, null, false);
        }

        public string Add(StudioItem row)
        {
            var r = Runtime.Ops.AddItem(row);
            Runtime.Apply();
            if (r == null || r.Status == CommandStatus.Error) { Check("add " + row, false, r?.ToString() ?? "null"); return null; }
            return r.NewId;
        }

        public ItemRealization Item(string id) => id == null ? null : Runtime.FindItem(id);
        public ItemNodeState State(string id) => Session.Model.Find(id)?.State as ItemNodeState;

        public void Dispose()
        {
            Runtime?.Dispose();
            Runtime = null;
        }
    }

    static RuntimeServices services;

    static StudioItem Row(StudioDb db, int category, string reference) =>
        ContentId.TryParse(reference, out var id) && db.TryGetAccessoryItem(category, id, out var r) ? r : null;

    static int PrefabChains(GameObject go) => go ? go.GetComponentsInChildren<DynamicBone>(true).Length : -1;

    // ---------------------------------------------------------------- run

    public static void Run()
    {
        rep = new StringBuilder(); passes = fails = warns = 0; perTest.Clear(); exceptions.Clear();
        only = OnlyArgs();
        Directory.CreateDirectory(OutDir);
        Application.logMessageReceived += OnLog;
        var total = Stopwatch.StartNew();
        byte[] ga2Bytes = null;
        try
        {
            var head = new StringBuilder();
            services = SceneRuntimeTest.NewServices(head);
            rep.Append(head);
            // the app's lists: no AI-Shoujo gate (ContentSettings.aiInstall, default true), as HS2 Studio shows them
            services.Db = services.Catalog.LoadStudioLists(new StudioLoadOptions { EmulateAIInstallGate = false });
            var db = services.Db;
            Info($"accessory items: {db.AccessoryItems.Count}; options AccessoryItems {db.Options.AccessoryItems}; build {string.Join(", ", db.TimingsMs.Select(kv => kv.Key + " " + kv.Value))}");

            Guard("GA1", "the group, its categories and rows", () => GA1(db));
            Guard("GA2", "save -> load -> save of a scene with accessory items", () => ga2Bytes = GA2(db));
            Guard("GA3", "the group switched off", () => GA3(ga2Bytes));
            Guard("GA4", "illusion.hs2#5 as accessory and as item", () => GA4(db));
            Guard("GA5", "30 rows realized", () => GA5(db));
        }
        catch (Exception e) { Check("(setup)", false, e.GetType().Name + ": " + e.Message); }
        Application.logMessageReceived -= OnLog;
        var sum = new StringBuilder();
        sum.AppendLine($"SUMMARY: {passes} PASS, {fails} FAIL ({warns} WARN, {total.Elapsed.TotalSeconds:F0} s)");
        foreach (var kv in perTest.OrderBy(k => k.Key, StringComparer.Ordinal))
            sum.AppendLine($"  {kv.Key}: {kv.Value.pass} pass, {kv.Value.fail} fail{(kv.Value.warn > 0 ? ", " + kv.Value.warn + " warn" : "")}");
        rep.Insert(0, sum.ToString());
        File.WriteAllText(Path.Combine(OutDir, "accessory_items_report.txt"), rep.ToString(), new UTF8Encoding(false));
        Debug.Log($"ACCESSORY ITEMS TEST DONE: {passes} PASS, {fails} FAIL, {warns} WARN");
        if (Application.isBatchMode) TestExit.Exit(fails == 0 ? 0 : 1);
    }

    // ---------------------------------------------------------------- GA1

    static void GA1(StudioDb db)
    {
        var groups = db.ItemGroups().ToList();
        int at = groups.FindIndex(g => g.Number == StudioDb.AccessoryGroup);
        Check("the group is listed second (sort 0, after group 0)", at == 1 && groups[0].Number == 0, $"position {at}; first {groups.FirstOrDefault()?.Number}");
        if (!db.TryGetItemGroup(StudioDb.AccessoryGroup, out var grp)) { Check("the group exists", false); return; }
        Check("the group's list name is HS2's", grp.Name == "アクセサリー" && grp.Sort == 0, grp.Name + " sort " + grp.Sort);
        var cats = db.CategoriesOf(grp).ToList();
        var numbers = cats.Select(c => c.Number).ToList();
        Check("13 categories 351-363 in order", numbers.SequenceEqual(Enumerable.Range(351, 13)), string.Join(",", numbers));
        Check("category names as the character lists name them (ao_head ... ao_kokan) and sort = number",
            cats.All(c => c.Name == ListCategories.NameOf(c.Number) && c.Sort == c.Number), string.Join(",", cats.Select(c => c.Name)));
        int known = 0, off = 0;
        var notes = new List<string>();
        foreach (var c in cats)
        {
            var rows = db.ItemsIn(c).ToList();
            var (hs2, hs2Vanilla) = Hs2Rows[c.Number];
            int twice = Hs2Twice.Count(t => t.category == c.Number && rows.Any(r => string.Equals(r.Key.ToString(), t.reference, StringComparison.OrdinalIgnoreCase)));
            int vanilla = rows.Count(r => r.IsVanilla);
            int firstMod = rows.FindIndex(r => !r.IsVanilla);
            bool vanillaFirst = firstMod < 0 || rows.Skip(firstMod).All(r => !r.IsVanilla);
            bool modOrder = true;
            for (int i = Math.Max(firstMod, 0) + 1; i < rows.Count && firstMod >= 0; i++)
                if (HS2Next.Content.Maker.MakerLists.CompareGuids(rows[i - 1].Guid, rows[i].Guid) > 0) { modOrder = false; break; }
            known += twice;
            bool ok = rows.Count == hs2 - twice && vanilla == hs2Vanilla && vanillaFirst && modOrder && rows.All(r => r.Group == StudioDb.AccessoryGroup && r.Category == c.Number);
            if (!ok) { off++; notes.Add($"{c.Number}: {rows.Count} rows (HS2 {hs2}, twice {twice}), vanilla {vanilla} (HS2 {hs2Vanilla}), vanilla first {vanillaFirst}, mods by GUID {modOrder}"); }
            Info($"{c.Number} {c.Name}: {rows.Count} rows ({vanilla} base game), HS2 {hs2}");
        }
        Check("rows per category = HS2's less the in-mod duplicate keys; base-game rows first (Possess 99 included), then the mods by GUID", off == 0, string.Join(" | ", notes));
        Check($"known class inModDuplicateKey: {known} (maxCount 2)", known <= 2);
        Info("counters: " + string.Join(", ", db.Counters.Where(kv => kv.Key.StartsWith("accessoryItems.", StringComparison.Ordinal)).Select(kv => kv.Key + " " + kv.Value)));
        foreach (var d in db.Diagnostics.Where(x => x.StartsWith("accessory ", StringComparison.Ordinal)).Take(20)) Info(d);
        Check("2,511 rows offered (466 base game, 2,045 mod)", db.AccessoryItems.Count == 2511 && db.AccessoryItems.Count(r => r.IsVanilla) == 466,
            $"{db.AccessoryItems.Count} ({db.AccessoryItems.Count(r => r.IsVanilla)} base game)");
        Check("no accessory row in the item table (vanilla accessory ids repeat item ids)", db.AccessoryItems.All(r => !db.Items.TryGet(r.Key, out var it) || it != r));
        var p399 = db.Items.Get(StudioDb.FallbackItem);
        Check("the fallback item is still an item", p399 != null && p399.Group != StudioDb.AccessoryGroup);
    }

    // ---------------------------------------------------------------- GA2

    static byte[] GA2(StudioDb db)
    {
        byte[] first, second;
        var ids = new List<string>();
        using (var s = new Scene(services))
        {
            foreach (var (cat, reference, chains) in SceneRows)
            {
                var row = Row(db, cat, reference);
                if (row == null) { Check($"row {cat} {reference} exists", false); continue; }
                var id = s.Add(row);
                if (id == null) continue;
                ids.Add(id);
                var st = s.State(id);
                var it = s.Item(id);
                Check($"{cat} {reference}: stored ref / group / category", st.Ref == row.Key.ToString() && st.Group == StudioDb.AccessoryGroup && st.Category == cat,
                    $"{st.Ref} {st.Group}/{st.Category}");
                Check($"{cat} {reference}: built from its accessory row, chains {chains} (HS2)", it != null && it.State == RealizationState.Built && it.Row == row && !it.IsFallback
                    && it.DynamicBones.Count == chains, $"state {it?.State}, row {it?.Row}, chains {it?.DynamicBones.Count}");
            }
            first = s.Session.ToBytes(new SaveOptions { Clock = () => FixedClock });
        }
        var lr0 = SceneContainer.Load(first);
        var req = lr0.Ok ? lr0.Value.LoadedManifest?.Requires?.Select(r => r.Guid).ToList() : null;
        Check("requires names hs2next.accessoryItems", req != null && req.Contains(SceneContainer.AccessoryItemsRequirement), req == null ? lr0.Error?.Message : string.Join(", ", req));
        using (var s2 = new Scene(services))
        {
            var lr = s2.Session.Load(first);
            Check("the saved scene loads", lr.Ok, lr.Error?.Message);
            if (!lr.Ok) return first;
            s2.Runtime.Apply();
            int resolved = ids.Count(id => s2.Item(id) is ItemRealization it && it.State == RealizationState.Built && !it.IsFallback && it.Row?.Group == StudioDb.AccessoryGroup);
            Check("every accessory item resolves to its accessory row after the load", resolved == ids.Count, $"{resolved} / {ids.Count}");
            second = s2.Session.ToBytes(new SaveOptions { Clock = () => FixedClock });
        }
        bool same = first.SequenceEqual(second);
        Check("save -> load -> save byte-identical", same, first.Length + " / " + second.Length + " bytes");
        return first;
    }

    // ---------------------------------------------------------------- GA3

    static void GA3(byte[] scene)
    {
        var off = services.Catalog.LoadStudioLists(new StudioLoadOptions { EmulateAIInstallGate = services.Db.Options.EmulateAIInstallGate, AccessoryItems = false });
        try
        {
            Check("switched off: the group is absent", !off.TryGetItemGroup(StudioDb.AccessoryGroup, out _) && off.AccessoryItems.Count == 0 && off.ItemGroups().All(g => g.Number != StudioDb.AccessoryGroup));
            Check("switched off: the item table is unchanged", off.Items.Count == services.Db.Items.Count, off.Items.Count + " / " + services.Db.Items.Count);
            if (scene == null) { Check("GA2's scene is there", false); return; }
            var sv = new RuntimeServices { Catalog = services.Catalog, Db = off, Hs2Root = services.Hs2Root, Settings = services.Settings };
            using (var s = new Scene(sv))
            {
                var lr = s.Session.Load(scene);
                Check("switched off: the scene loads", lr.Ok, lr.Error?.Message);
                if (!lr.Ok) return;
                s.Runtime.Apply();
                var items = s.Session.Model.PreOrder().Where(n => n.State is ItemNodeState st && st.Group == StudioDb.AccessoryGroup).ToList();
                int fallback = items.Count(n => s.Item(n.Id) is ItemRealization it && it.IsFallback && it.Row != null && it.Row.Key == StudioDb.FallbackItem);
                Check("switched off: every accessory item is missing (the fallback item stands in, never the item of the same id)", items.Count == SceneRows.Length && fallback == items.Count,
                    $"{fallback} / {items.Count}");
                Check("switched off: finding item.missing", s.Session.Findings.Any(f => f.Code == "item.missing"));
                var again = s.Session.ToBytes(new SaveOptions { Clock = () => FixedClock });
                Check("switched off: saves byte-identical", again.SequenceEqual(scene), again.Length + " / " + scene.Length + " bytes");
            }
        }
        finally { services.Catalog.LoadStudioLists(new StudioLoadOptions { EmulateAIInstallGate = services.Db.Options.EmulateAIInstallGate }); }
    }

    // ---------------------------------------------------------------- GA4

    static void GA4(StudioDb db)
    {
        var acc = Row(db, 351, "illusion.hs2#5");
        var item = db.Items.Get(ContentId.Vanilla(5));
        Check("illusion.hs2#5 is both an accessory (351) and an item", acc != null && item != null && acc != item, $"{acc} | {item}");
        if (acc == null || item == null) return;
        using (var s = new Scene(services))
        {
            var a = s.Add(acc);
            var i = s.Add(item);
            var ra = s.Item(a);
            var ri = s.Item(i);
            Check("two nodes, each built from its own row", a != null && i != null && a != i && ra?.Row == acc && ri?.Row == item && !ra.IsFallback && !ri.IsFallback,
                $"{ra?.Row} | {ri?.Row}");
            Check("distinct objects and prefabs", ra?.Instance != null && ri?.Instance != null && ra.Instance != ri.Instance && acc.FileName != item.FileName,
                acc.FileName + " | " + item.FileName);
            var sa = s.State(a);
            var si = s.State(i);
            Check("the stored nodes differ only by group / category", sa.Ref == si.Ref && sa.Group == StudioDb.AccessoryGroup && sa.Category == 351 && si.Group == item.Group && si.Category == item.Category,
                $"{sa.Ref} {sa.Group}/{sa.Category} | {si.Ref} {si.Group}/{si.Category}");
            bool fa, fi;
            Check("each resolves to its own table", ItemRealization.ResolveRow(db, sa, out fa) == acc && !fa && ItemRealization.ResolveRow(db, si, out fi) == item && !fi);
        }
    }

    // ---------------------------------------------------------------- GA5

    static void GA5(StudioDb db)
    {
        var all = db.AccessoryItems;
        int step = Math.Max(1, all.Count / 30);
        var pick = Enumerable.Range(0, 30).Select(k => all[(k * step + 7) % all.Count]).Distinct().ToList();
        int built = 0, chainsOk = 0, noSlots = 0, noComponent = 0, owned = 0, vanilla = pick.Count(r => r.IsVanilla);
        var notes = new List<string>();
        var cat = services.Catalog;
        int unscoped0 = cat.Ledger.UnscopedLoads;
        using (var s = new Scene(services))
        {
            foreach (var row in pick)
            {
                var id = s.Add(row);
                var it = s.Item(id);
                if (it == null || it.State != RealizationState.Built) { if (notes.Count < 6) notes.Add($"{row.Category} {row.Key}: {it?.State} {it?.Detail}"); continue; }
                built++;
                string owner = "item:" + (id.Length > 8 ? id.Substring(0, 8) : id);
                if (cat.Ledger.Largest(int.MaxValue).Any(x => x.Owners.Contains(owner))) owned++;
                else if (notes.Count < 6) notes.Add($"{row.Category} {row.Key}: no bundle owned by {owner}");
                int prefab = PrefabChains(it.Instance);
                if (it.DynamicBones.Count == prefab) chainsOk++;
                else if (notes.Count < 6) notes.Add($"{row.Category} {row.Key}: chains {it.DynamicBones.Count}, prefab {prefab}");
                if (it.Component == null)
                {
                    noComponent++;
                    if (it.Features == null || !it.Features.Color.Any(x => x)) noSlots++;
                    else if (notes.Count < 6) notes.Add($"{row.Category} {row.Key}: {it.Features.Color.Count(x => x)} colour slots without an item component");
                }
            }
        }
        Info($"{pick.Count} rows ({vanilla} base game) over {pick.Select(r => r.Category).Distinct().Count()} categories");
        Check("every row built", built == pick.Count, $"{built} / {pick.Count}: " + string.Join(" | ", notes));
        Check("each item owns its bundles (item:<node id 8>)", owned == built, $"{owned} / {built}");
        Check("no load outside an owner scope while the rows were added", cat.Ledger.UnscopedLoads == unscoped0, $"unscoped loads {unscoped0} -> {cat.Ledger.UnscopedLoads}");
        Check("the chains of the prefab run as item chains", chainsOk == built, $"{chainsOk} / {built}");
        Check("no colour slot without an item component (as HS2)", noSlots == noComponent, $"{noSlots} / {noComponent}");
    }
}

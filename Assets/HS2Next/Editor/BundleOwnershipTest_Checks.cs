// BO2-BO7 (edit mode, headless): bundle ownership in the app's own paths (AppCore test hosts and the S1 build harness).
//   BO2  a character build records owners for every bundle it used (base, edit units, motion); no load outside a scope
//        after the warm-up (a second build of a target).
//   BO3  a clothes change: the old generation is released, the old part's bundle reaches the LRU, a budget of 0 evicts it;
//        every renderer of the character keeps its mesh, materials and textures (counts, no image), also after the
//        unused objects are unloaded.
//   BO4  delete a character: the graveyard keeps its owners; undo revives it intact; pushed out of the graveyard (four more
//        deleted characters) its owners are released.
//   BO5  after release, eviction and UnloadUnusedAssets, the Texture2D count and bytes return within +2 % of the values before
//        the character existed (after one warm-up build).
//   BO6  determinism: the S1 cards (the visual targets + 20) built twice in one process, once with no eviction and once with a
//        budget of 0, an eviction and an unload of unused objects between every card: the snapshots equal byte for byte (and
//        equal the stored S1 baselines when present).
//   BOL  a loose PNG texture is not readable after its upload (the product default; LooseTextures.KeepReadable for tests).
//   BO7  over a Maker edit sequence (hair, clothes and accessory changes, a coordinate re-apply): the pins and the count of
//        loads outside a scope (0 after the warm-up).
//   BOP  the Maker prefetch's counters in content.stats; by default a prefetch opens the bundle only (no asynchronous asset
//        request: Unity resolves such a request by the bundle's build name, which mods share), and the edit still uses it.
//   BOS  PluginCheck's PA7 pattern: the sphere item (#399, in studio/00.unity3d: 1.07 GB, 1,714 textures) added, cleared by
//        scene.new and added again, with an eviction pass under the default budget between: the bundle stays open (the LRU
//        weighs resident bytes, not the objects estimate), no asset is loaded twice and one copy of the prefab stays; with a
//        budget of 0 the eviction, the reopen with a second copy (a reload) and UnloadUnusedAssets freeing the first still work.
// Run: -executeMethod BundleOwnershipTest.Run [-only BO2,BO6] [-limit N] (headless, -nographics, edit mode). Report
// Spike/out/bundles/bundle_ownership_report.txt, first line "SUMMARY: N PASS, M FAIL"; in batch mode it ends Unity with
// exit code 1 on a FAIL, else 0 (with or without -quit).
// Numbers, ids and bundle keys only (no image is read or written; card files appear by count).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.App;
using HS2Next.Content;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Tests.MakerCore;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static partial class BundleOwnershipTest
{
    static string Report => Path.Combine(OutDir, "bundle_ownership_report.txt");

    sealed class Host : IDisposable
    {
        public RuntimeServices Services;
        public AppCore App;
        public ContentCatalog Catalog => Services.Catalog;

        public JObject Call(object request)
        {
            string text = App.Call(Newtonsoft.Json.JsonConvert.SerializeObject(request));
            return JObject.Parse(text);
        }

        public void Dispose() { App?.Dispose(); App = null; }
    }

    static Host NewHost(RuntimeServices services)
    {
        var s = AppSettings.Defaults(Array.Empty<string>());
        s.Content = CardResolveTest.Settings;
        s.UserData = Path.Combine(OutDir, "userdata");
        s.Bridge.OutputDir = Path.Combine(OutDir, "out");
        var o = services.NewSessionOptions();   // random ids: owner labels carry the first 8 hex digits of a node id
        return new Host { Services = services, App = AppCore.ForTests(services, s, null, o) };
    }

    static string Ok(JObject r) => r?.Value<bool?>("ok") == true ? null : (r?.Value<string>("error") ?? r?.ToString(Newtonsoft.Json.Formatting.None) ?? "no reply");

    static string AddChara(Host h, string card, out string error)
    {
        var r = h.Call(new { cmd = "chara.load", path = card });
        error = Ok(r);
        return error == null ? (string)r["added"]?[0]?["key"] : null;
    }

    static string Id8(string id) => id != null && id.Length > 8 ? id.Substring(0, 8) : id;

    // The keys whose owners include one of this node's ("chara:<id8>/..."), and their owner labels.
    static Dictionary<string, List<string>> OwnedBy(ContentCatalog cat, string prefix) =>
        cat.Ledger.Largest(int.MaxValue).Where(x => x.Owners.Any(o => o.StartsWith(prefix, StringComparison.Ordinal)))
           .ToDictionary(x => x.Key, x => x.Owners, StringComparer.OrdinalIgnoreCase);

    static List<string> KeysInState(ContentCatalog cat, string state) =>
        cat.Ledger.Largest(int.MaxValue).Where(x => x.State == state).Select(x => x.Key).ToList();

    // Renderer census of a character: renderers, live meshes, live material slots, live Texture2D bindings, dead references.
    static (int renderers, int meshes, int materials, int textures, int dead) Census(GameObject root)
    {
        int r = 0, me = 0, ma = 0, tx = 0, dead = 0;
        if (!root) return (0, 0, 0, 0, 0);
        foreach (var ren in root.GetComponentsInChildren<Renderer>(true))
        {
            r++;
            Mesh mesh = ren is SkinnedMeshRenderer smr ? smr.sharedMesh : ren.GetComponent<MeshFilter>() is MeshFilter mf ? mf.sharedMesh : null;
            if (!(mesh is null)) { if (mesh && mesh.vertexCount > 0) me++; else dead++; }
            foreach (var m in ren.sharedMaterials)
            {
                if (m is null) continue;
                if (!m) { dead++; continue; }
                ma++;
                foreach (var pid in m.GetTexturePropertyNameIDs())
                {
                    var t = m.GetTexture(pid);
                    if (t is null) continue;
                    if (t && t.width > 0) tx++; else dead++;
                }
            }
        }
        return (r, me, ma, tx, dead);
    }

    static string C((int renderers, int meshes, int materials, int textures, int dead) c) =>
        $"renderers {c.renderers}, meshes {c.meshes}, materials {c.materials}, textures {c.textures}, dead {c.dead}";

    static (int count, long bytes) Textures()
    {
        var all = Resources.FindObjectsOfTypeAll<Texture2D>();
        long b = 0;
        foreach (var t in all) if (t) b += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);
        return (all.Length, b);
    }

    static GameObject RootOf(Host h, string id) => h.App.Runtime.FindChara(id)?.Root;

    static void UnloadUnusedNow()
    {
        EditorUtility.UnloadUnusedAssetsImmediate(true);
        GC.Collect();
    }

    // ================================================================ entry

    public static void Run()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        Directory.CreateDirectory(OutDir);
        var only = Arg("-only")?.Split(',').Select(x => x.Trim()).ToList();
        bool Want(string id) => only == null || only.Contains(id);
        var head = new StringBuilder();
        RuntimeServices services = null;
        try { services = SceneRuntimeTest.NewServices(head); }
        catch (Exception e) { Check("setup: services", false, e.ToString()); }
        Line($"BundleOwnershipTest (Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceType}); checks {(only == null ? "BO2-BO7" : string.Join(",", only))}");
        if (services != null)
        {
            var t = TestTargets.Load();
            string female = TestTargets.Abs(t.visual.female), male = TestTargets.Abs(t.visual.male);
            foreach (var (id, body) in new (string, Action)[]
            {
                ("BO2", () => BO2(services, female, male)),
                ("BO3", () => BO3(services, female)),
                ("BO4", () => BO4(services, female, male)),
                ("BO5", () => BO5(services, female)),
                ("BO7", () => BO7(services, female)),
                ("BO6", () => BO6()),
                ("BOL", () => BOL(services)),
                ("BOP", () => BOP(services, female)),
                ("BOS", () => BOS(services)),
            })
            {
                if (!Want(id)) continue;
                Line("");
                Line("== " + id);
                var t0 = sw.Elapsed.TotalSeconds;
                try { body(); }
                catch (Exception e) { Check(id + " threw", false, e.GetType().Name + ": " + e.Message + " " + e.StackTrace); }
                Info($"{id}: {sw.Elapsed.TotalSeconds - t0:F1} s; ledger {StatsLine(services.Catalog)}");
            }
            try { services.Catalog.UnloadAll(); } catch (Exception) { }
        }
        rep.Insert(0, $"SUMMARY: {passes} PASS, {fails} FAIL ({sw.Elapsed.TotalSeconds:F0} s)\nBundleOwnershipTest BO2-BO7 (owners, the LRU, the budget)\n");
        File.WriteAllText(Report, rep.ToString(), new UTF8Encoding(false));
        Debug.Log($"BUNDLE OWNERSHIP: {passes} PASS, {fails} FAIL");
        // ends Unity itself in batch mode, also when the job was started without -quit (it held the project lock idle once)
        if (Application.isBatchMode) TestExit.Exit(fails > 0 ? 1 : 0);
    }

    static string StatsLine(ContentCatalog cat)
    {
        var s = cat.Ledger.Stats();
        return $"open {s.Open} (owned {s.Owned}, pinned {s.Pinned}, thumbnail {s.ThumbnailOnly}, unscoped {s.Unscoped}, lru {s.Lru}), owners {s.Owners}, " +
               $"owned {s.OwnedBytes / 1048576.0:F1} MiB, lru {s.LruBytes / 1048576.0:F1} MiB, evictions {s.Evictions}, reloads {s.ReloadedAssets}, unscoped loads {s.UnscopedLoads}";
    }

    static string Arg(string name, string def = null)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    // ================================================================ BO2

    static void BO2(RuntimeServices services, string female, string male)
    {
        var cat = services.Catalog;
        using (var h = NewHost(services))
        {
            // warm-up: the first build of each target (one-time loads: shader registries, list bundles)
            foreach (var card in new[] { female, male })
            {
                string w = AddChara(h, card, out var err);
                if (w == null) { Check("BO2: warm-up build", false, err); return; }
                h.Call(new { cmd = "delete", key = w });
            }
            h.Call(new { cmd = "scene.new" });
            foreach (var (card, label) in new[] { (female, "female target"), (male, "male target") })
            {
                int unscoped0 = cat.Ledger.UnscopedLoads;
                var keysBefore = new HashSet<string>(cat.Ledger.Largest(int.MaxValue).Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
                string id = AddChara(h, card, out var err);
                if (id == null) { Check($"BO2 {label}: build", false, err); continue; }
                string prefix = "chara:" + Id8(id) + "/";
                var owned = OwnedBy(cat, prefix);
                var labels = owned.SelectMany(kv => kv.Value).Where(o => o.StartsWith(prefix, StringComparison.Ordinal)).Distinct().ToList();
                int baseN = owned.Count(kv => kv.Value.Any(o => o == prefix + "base"));
                int unitN = owned.Count(kv => kv.Value.Any(o => o.StartsWith(prefix, StringComparison.Ordinal) && o != prefix + "base" && o != prefix + "anime"));
                var newKeys = cat.Ledger.Largest(int.MaxValue).Where(x => !keysBefore.Contains(x.Key)).ToList();
                var newUnowned = newKeys.Where(x => x.Owners.Count == 0 || x.State == "unscoped").Select(x => x.Key).ToList();
                Info($"BO2 {label}: {owned.Count} bundles owned by the character ({baseN} base, {unitN} units; {labels.Count} owner labels, e.g. {string.Join(", ", labels.Take(6))}); {newKeys.Count} bundles new to the ledger");
                Check($"BO2 {label}: the build's bundles are owned (base and unit generations); none it opened is unowned or unscoped",
                      owned.Count > 0 && baseN >= 0 && unitN > 0 && newUnowned.Count == 0,
                      newUnowned.Count > 0 ? "unowned: " + string.Join(", ", newUnowned.Take(8)) : $"{owned.Count} owned");
                Check($"BO2 {label}: no load outside a scope after the warm-up", cat.Ledger.UnscopedLoads == unscoped0,
                      $"unscoped loads {unscoped0} -> {cat.Ledger.UnscopedLoads}");
            }
        }
    }

    // ================================================================ BO3

    // Two rows of a list category whose bundles are zipmod copies of distinct paths, not shipped by abdata.
    static List<ListEntry> ModRows(ContentCatalog cat, int category, int n)
    {
        var l = new List<ListEntry>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in cat.EntriesOf(category))
        {
            string ab = e["MainAB"];
            if (e.Zipmod == null || string.IsNullOrEmpty(ab) || ab == "0" || string.IsNullOrEmpty(e["MainData"]) || e["MainData"] == "0") continue;
            ab = ab.Replace('\\', '/');
            if (!e.Zipmod.HasBundle(ab) || cat.HasRealBundle(ab) || !paths.Add(ab)) continue;
            l.Add(e);
            if (l.Count >= n) break;
        }
        return l;
    }

    static string KeyOfRow(ListEntry e) => e.Zipmod.Path + "!" + e["MainAB"].Replace('\\', '/');

    static void BO3(RuntimeServices services, string female)
    {
        var cat = services.Catalog;
        long budget0 = cat.BundleCacheMB;
        using (var h = NewHost(services))
        {
            string id = AddChara(h, female, out var err);
            if (id == null) { Check("BO3: build", false, err); return; }
            var rows = ModRows(cat, 240, 2);
            if (rows.Count < 2) { Check("BO3: two mod top rows", false, rows.Count.ToString()); return; }
            string keyA = KeyOfRow(rows[0]), keyB = KeyOfRow(rows[1]);
            var r = h.Call(new { cmd = "chara.edit.item", key = id, path = "coord.clothes.top.item", @ref = rows[0].Key.ToString() });
            if (Ok(r) != null) { Check("BO3: top A", false, Ok(r)); return; }
            string stateA = cat.Ledger.StateOf(keyA);
            var ownersA = cat.Ledger.OwnersOf(keyA);
            r = h.Call(new { cmd = "chara.edit.item", key = id, path = "coord.clothes.top.item", @ref = rows[1].Key.ToString() });
            if (Ok(r) != null) { Check("BO3: top B", false, Ok(r)); return; }
            string stateA2 = cat.Ledger.StateOf(keyA), stateB = cat.Ledger.StateOf(keyB);
            var ownersB = cat.Ledger.OwnersOf(keyB);
            Info($"BO3: top A {rows[0].Key} ({stateA}, {string.Join("|", ownersA)}) -> top B {rows[1].Key} ({stateB}, {string.Join("|", ownersB)}); A now {stateA2}");
            Check("BO3: the change released the old generation: the old top's bundle is in the LRU, the new one owned by the next generation",
                  stateA == "owned" && stateA2 == "lru" && stateB == "owned"
                  && ownersB.Any(o => o.StartsWith("chara:", StringComparison.Ordinal) && o.Contains("#") && !ownersA.Contains(o)),
                  $"A {stateA} -> {stateA2}, B {stateB}; owners {string.Join("|", ownersA)} -> {string.Join("|", ownersB)}");
            var before = Census(RootOf(h, id));
            cat.BundleCacheMB = 0;
            int n = cat.EvictNow(-1, true);
            bool gone = !cat.Ledger.Knows(keyA);
            var after = Census(RootOf(h, id));
            UnloadUnusedNow();
            var after2 = Census(RootOf(h, id));
            Info($"BO3: evicted {n}; census before {C(before)}; after the eviction {C(after)}; after UnloadUnusedAssets {C(after2)}");
            Check("BO3: a budget of 0 evicts the old top's bundle; the character keeps every mesh, material and texture (also after UnloadUnusedAssets)",
                  n >= 1 && gone && before.Equals(after) && before.Equals(after2) && before.dead == 0 && before.meshes > 0,
                  $"evicted {n}, A known {!gone}, census {C(before)} / {C(after)} / {C(after2)}");
            // the evicted bundle reopens: back to top A
            r = h.Call(new { cmd = "chara.edit.item", key = id, path = "coord.clothes.top.item", @ref = rows[0].Key.ToString() });
            Check("BO3: the evicted bundle opens again for the next change (owned again)", Ok(r) == null && cat.Ledger.StateOf(keyA) == "owned",
                  Ok(r) ?? cat.Ledger.StateOf(keyA));
        }
        cat.BundleCacheMB = budget0;
    }

    // ================================================================ BO4

    static void BO4(RuntimeServices services, string female, string male)
    {
        var cat = services.Catalog;
        using (var h = NewHost(services))
        {
            string id = AddChara(h, female, out var err);
            if (id == null) { Check("BO4: build", false, err); return; }
            string prefix = "chara:" + Id8(id) + "/";
            var owned0 = OwnedBy(cat, prefix);
            var census0 = Census(RootOf(h, id));
            h.Call(new { cmd = "delete", key = id });
            var owned1 = OwnedBy(cat, prefix);
            bool kept = owned1.Count == owned0.Count && owned0.Keys.All(k => cat.Ledger.StateOf(k) != "lru");
            Check("BO4: a deleted character in the graveyard keeps its owners", kept, $"owned {owned0.Count} -> {owned1.Count}");
            h.Call(new { cmd = "undo" });
            var census1 = Census(RootOf(h, id));
            Check("BO4: undo revives it intact (same renderer census, owners kept)", census0.Equals(census1) && OwnedBy(cat, prefix).Count == owned0.Count,
                  $"{C(census0)} / {C(census1)}");
            h.Call(new { cmd = "delete", key = id });
            // four more deleted characters push it out of the graveyard (it keeps at most 4 characters)
            for (int i = 0; i < 4; i++)
            {
                string other = AddChara(h, male, out var e2);
                if (other != null) h.Call(new { cmd = "delete", key = other });
                else Info("BO4: filler build failed: " + e2);
            }
            var owned2 = OwnedBy(cat, prefix);
            int inLru = owned0.Keys.Count(k => cat.Ledger.StateOf(k) == "lru");
            Info($"BO4: after the graveyard let it go: owned by it {owned2.Count}; of its {owned0.Count} bundles {inLru} in the LRU, the others still owned by something else or pinned");
            Check("BO4: graveyard eviction releases its owners", owned2.Count == 0 && inLru > 0, $"still owned {owned2.Count}, in the LRU {inLru}");
        }
    }

    // ================================================================ BO5

    static void BO5(RuntimeServices services, string female)
    {
        var cat = services.Catalog;
        long budget0 = cat.BundleCacheMB;
        using (var h = NewHost(services))
        {
            // warm-up: one build and delete (the one-time caches fill)
            string w = AddChara(h, female, out var err);
            if (w == null) { Check("BO5: warm-up build", false, err); return; }
            h.Call(new { cmd = "scene.new" });
            cat.BundleCacheMB = 0;
            cat.EvictNow(-1, true);
            UnloadUnusedNow();
            var t0 = Textures();
            string id = AddChara(h, female, out err);
            if (id == null) { Check("BO5: build", false, err); return; }
            var t1 = Textures();
            h.Call(new { cmd = "scene.new" });
            int n = cat.EvictNow(-1, true);
            UnloadUnusedNow();
            var t2 = Textures();
            Info($"BO5: Texture2D before {t0.count} ({t0.bytes / 1048576.0:F1} MiB), with the character {t1.count} ({t1.bytes / 1048576.0:F1} MiB), after scene.new + {n} evictions + UnloadUnusedAssets {t2.count} ({t2.bytes / 1048576.0:F1} MiB)");
            Check("BO5: Texture2D count and bytes back within +2 % of the values before the character",
                  t2.count <= t0.count * 1.02 && t2.bytes <= t0.bytes * 1.02 && t1.count > t0.count,
                  $"count {t0.count} -> {t2.count}, bytes {t0.bytes} -> {t2.bytes}");
        }
        cat.BundleCacheMB = budget0;
    }

    // ================================================================ BO7

    static void BO7(RuntimeServices services, string female)
    {
        var cat = services.Catalog;
        using (var h = NewHost(services))
        {
            string id = AddChara(h, female, out var err);
            if (id == null) { Check("BO7: build", false, err); return; }
            int unscoped0 = cat.Ledger.UnscopedLoads;
            var unscopedKeys0 = new HashSet<string>(KeysInState(cat, "unscoped"), StringComparer.OrdinalIgnoreCase);
            var edits = new List<(string path, ListEntry row)>();
            foreach (var (cat0, path) in new[] { (301, "chara.hair.front.item"), (300, "chara.hair.back.item"), (240, "coord.clothes.top.item"), (241, "coord.clothes.bottom.item") })
            {
                var rows = ModRows(cat, cat0, 2);
                foreach (var row in rows) edits.Add((path, row));
                var van = cat.EntriesOf(cat0).Where(e => e.IsVanilla).Skip(1).Take(1);
                foreach (var row in van) edits.Add((path, row));
            }
            int okN = 0;
            var failed = new List<string>();
            foreach (var (path, row) in edits)
            {
                var r = h.Call(new { cmd = "chara.edit.item", key = id, path, @ref = row.Key.ToString() });
                if (Ok(r) == null) okN++; else failed.Add(path + " " + row.Key + ": " + Ok(r));
            }
            // an accessory: add one, change its item, remove it
            var accRows = ModRows(cat, 351, 2);
            if (accRows.Count == 2)
            {
                var a1 = h.Call(new { cmd = "chara.edit.accessory", key = id, op = "add", category = 351, @ref = accRows[0].Key.ToString() });
                string acc = (string)a1["accessory"];
                if (Ok(a1) == null && acc != null)
                {
                    okN++;
                    var a2 = h.Call(new { cmd = "chara.edit.item", key = id, path = "coord.accessories[\"" + acc + "\"].item", @ref = accRows[1].Key.ToString() });
                    if (Ok(a2) == null) okN++; else failed.Add("accessory change: " + Ok(a2));
                    var a3 = h.Call(new { cmd = "chara.edit.accessory", key = id, op = "remove", accessory = acc });
                    if (Ok(a3) == null) okN++; else failed.Add("accessory remove: " + Ok(a3));
                }
                else failed.Add("accessory add: " + Ok(a1));
            }
            // Studio item churn: vanilla and mod items added and deleted (their prefab defaults are read under an owner too)
            int items = 0;
            // vanilla rows and mod rows of zipmods outside the item corpus's crash list (Cache/items/corpus_crashes.txt)
            var crashFile = Path.Combine(ContentSettings.ProjectRoot, "Cache", "items", "corpus_crashes.txt");
            var crashGuids = new HashSet<string>(File.Exists(crashFile) ? File.ReadAllLines(crashFile).Select(l => l.Split('#')[0].Trim()) : Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var itemRows = services.Db.Items.Enumerate().Take(6000).ToList();
            var picks = itemRows.Where(r => r.Key.IsVanilla).Take(3)
                .Concat(itemRows.Where(r => !r.Key.IsVanilla && !crashGuids.Contains(r.Key.SourceGuid ?? "")).Skip(40).Take(3)).ToList();
            foreach (var row in picks)
            {
                var a = h.Call(new { cmd = "item.add", @ref = row.Key.ToString() });
                string item = (string)a["added"]?[0]?["key"];
                if (Ok(a) == null && item != null) { items++; h.Call(new { cmd = "delete", key = item }); }
                else failed.Add("item.add " + row.Key + ": " + Ok(a));
            }
            Info($"BO7: {items} of {picks.Count} Studio items added and deleted");
            // the clothes report the Maker's clothes facts build after every flush (it reads a main texture's size for a colour
            // group without creators: under a transient owner, never unscoped)
            var cr = h.Call(new { cmd = "chara.edit.clothes", key = id });
            Info($"BO7: chara.edit.clothes (ClothesReport, as the Maker's facts): {(Ok(cr) == null ? "ok" : Ok(cr))}");
            var pins = cat.Ledger.Largest(int.MaxValue).Where(x => x.State == "pinned").GroupBy(x => x.Owners.FirstOrDefault(o => o.StartsWith("pin:", StringComparison.Ordinal)) ?? "?")
                          .Select(g => g.Key + " " + g.Count()).ToList();
            var newUnscoped = KeysInState(cat, "unscoped").Where(k => !unscopedKeys0.Contains(k)).ToList();
            Info($"BO7: {okN} of {edits.Count + (accRows.Count == 2 ? 3 : 0)} edits applied{(failed.Count > 0 ? "; refused: " + string.Join("; ", failed.Take(4)) : "")}; pins: {string.Join(", ", pins)}");
            Info($"BO7: unscoped bundles before the sequence {unscopedKeys0.Count}: {string.Join(", ", unscopedKeys0.Take(10))}");
            Check("BO7: no load outside a scope over the Maker edit sequence and the clothes report (after the warm-up build)",
                  okN >= edits.Count / 2 && cat.Ledger.UnscopedLoads == unscoped0 && newUnscoped.Count == 0,
                  $"unscoped loads {unscoped0} -> {cat.Ledger.UnscopedLoads}; new unscoped bundles {newUnscoped.Count} {string.Join(", ", newUnscoped.Take(6))}");
        }
    }

    // ================================================================ BOP (the Maker prefetch's counters in content.stats)

    static void BOP(RuntimeServices services, string female)
    {
        var cat = services.Catalog;
        double keep0 = ContentCatalog.PrefetchKeepSec;
        using (var h = NewHost(services))
        {
            string id = AddChara(h, female, out var err);
            if (id == null) { Check("BOP: build", false, err); return; }
            var rows = ModRows(cat, 240, 4).Where(r => !cat.Ledger.Knows(KeyOfRow(r))).Take(2).ToList();
            if (rows.Count < 2) { Check("BOP: two mod top rows not open yet", false, rows.Count.ToString()); return; }
            var s0 = h.Call(new { cmd = "content.stats" })["prefetch"];
            bool a = cat.Prefetch(rows[0]), b = cat.Prefetch(rows[1]);
            cat.CompletePendingOpens();   // edit mode: no frames advance the opens or the asset requests
            cat.PumpPrefetch();
            cat.CompletePrefetchRequests();
            var s1 = h.Call(new { cmd = "content.stats" })["prefetch"];
            Info($"BOP: prefetch {a} / {b}; content.stats.prefetch before {s0?.ToString(Newtonsoft.Json.Formatting.None)}, after {s1?.ToString(Newtonsoft.Json.Formatting.None)}; asset requests allowed {cat.AssetPrefetchAllowed}");
            Check("BOP: two prefetches started (content.stats.prefetch.started +2); by default the prefetch opens the bundle only " +
                  "(no asynchronous asset request: ContentCatalog.PrefetchAssets off, assetRequests +0)",
                  a && b && (int)s1["started"] - (int)s0["started"] == 2 && !ContentCatalog.PrefetchAssets && !cat.AssetPrefetchAllowed
                  && (int)s1["assetRequests"] - (int)s0["assetRequests"] == 0 && cat.PendingAssetRequests == 0,
                  $"{s0} -> {s1}, PrefetchAssets {ContentCatalog.PrefetchAssets}");
            // the edit takes the first row: its flush owns the bundle, the prefetch counts it as used
            var r = h.Call(new { cmd = "chara.edit.item", key = id, path = "coord.clothes.top.item", @ref = rows[0].Key.ToString() });
            cat.PumpPrefetch();
            var s2 = h.Call(new { cmd = "content.stats" })["prefetch"];
            Check("BOP: the edit's flush uses the first prefetched (open-only) bundle (prefetch.used +1)", Ok(r) == null && (int)s2["used"] - (int)s1["used"] == 1,
                  Ok(r) ?? s2?.ToString(Newtonsoft.Json.Formatting.None));
            // the second is never used: it expires (keep time shortened for the test)
            ContentCatalog.PrefetchKeepSec = 0;
            cat.PumpPrefetch();
            var s3 = h.Call(new { cmd = "content.stats" })["prefetch"];
            Check("BOP: the unused prefetch expires (prefetch.expired +1, inFlight 0) and its bundle waits in the LRU",
                  (int)s3["expired"] - (int)s2["expired"] == 1 && (int)s3["inFlight"] == 0 && cat.Ledger.StateOf(KeyOfRow(rows[1])) == "lru",
                  $"{s3?.ToString(Newtonsoft.Json.Formatting.None)}, state {cat.Ledger.StateOf(KeyOfRow(rows[1]))}");
            Check("BOP: content.stats.prefetch has started, assetRequests, used, expired, refused, limited, inFlight",
                  new[] { "started", "assetRequests", "used", "expired", "refused", "limited", "inFlight" }.All(k => s3?[k] != null));
        }
        ContentCatalog.PrefetchKeepSec = keep0;
    }

    // ================================================================ BOS (a shared bundle released and used again: PA7)

    static void BOS(RuntimeServices services)
    {
        var cat = services.Catalog;
        long budget0 = cat.BundleCacheMB;
        var row = services.Db?.Items.Get(HS2Next.Content.Studio.StudioDb.FallbackItem);
        if (row == null) { Check("BOS: the sphere row (#399)", false, "no row"); return; }
        // root GameObjects named like the prefab outside every scene: the prefab objects loaded from the bundle
        int Copies() => Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g && !g.scene.IsValid() && g.transform.parent == null
                                                                             && string.Equals(g.name, row.FileName, StringComparison.OrdinalIgnoreCase));
        try
        {
            cat.BundleCacheMB = ContentCatalog.DefaultBundleCacheMB;
            using (var h = NewHost(services))
            {
                string Add(out string e)
                {
                    var a = h.Call(new { cmd = "item.add", @ref = row.Key.ToString() });
                    e = Ok(a);
                    return e == null ? (string)a["added"]?[0]?["key"] : null;
                }
                h.Call(new { cmd = "scene.new" });
                string item = Add(out var err);
                if (item == null) { Check("BOS: item.add of the sphere", false, err); return; }
                string key = cat.Ledger.Largest(int.MaxValue).Select(x => x.Key)
                    .FirstOrDefault(k => k.Replace('\\', '/').EndsWith(row.BundlePath, StringComparison.OrdinalIgnoreCase));
                if (key == null) { Check("BOS: the sphere's bundle is open", false, row.BundlePath); return; }
                int copies1 = Copies();
                int reloads0 = cat.Ledger.ReloadedAssets;
                long objects = 0, resident = 0;
                var steps = new List<string>();
                bool stayed = true;
                for (int cycle = 0; cycle < 3; cycle++)
                {
                    h.Call(new { cmd = "scene.new" });
                    string released = cat.Ledger.StateOf(key);
                    objects = cat.Ledger.ObjectsOf(key);
                    resident = cat.Ledger.ResidentOf(key);
                    int n = cat.EvictNow(-1, true);   // the tick's pass under the default budget
                    bool open = cat.Ledger.Knows(key);
                    string again = Add(out err);
                    if (again == null) { Check("BOS: item.add again", false, err); return; }
                    steps.Add($"{released} -> {(open ? "open" : "evicted")} ({n} evicted) -> {cat.Ledger.StateOf(key)}");
                    stayed &= released == "lru" && open && cat.Ledger.StateOf(key) == "owned";
                }
                int copies2 = Copies();
                Info($"BOS: {key}: resident {resident / 1048576.0:F1} MiB, objects estimate {objects / 1048576.0:F1} MiB, budget {cat.BundleCacheMB} MB; " +
                     $"cycles {string.Join("; ", steps)}; prefab copies {copies1} -> {copies2}; reloads {reloads0} -> {cat.Ledger.ReloadedAssets}");
                Check("BOS: the case is PA7's: the bundle's objects estimate alone is above the budget, its resident bytes are not",
                      objects > cat.Ledger.BudgetBytes && resident < cat.Ledger.BudgetBytes,
                      $"objects {objects}, resident {resident}, budget {cat.Ledger.BudgetBytes}");
                Check("BOS: 3 x (scene.new, eviction pass, item.add): the bundle waits in the LRU, stays open and is owned again; no asset loaded twice",
                      stayed && cat.Ledger.ReloadedAssets == reloads0, string.Join("; ", steps));
                Check("BOS: one copy of the sphere prefab after the re-adds (PA7 counted 2 GameObjects more per cycle)",
                      copies1 >= 1 && copies2 == copies1, $"{copies1} -> {copies2}");

                // the eviction itself still works: a budget of 0 evicts the released bundle, the re-add reopens it and loads a
                // second copy (a reload), UnloadUnusedAssets frees the first
                h.Call(new { cmd = "scene.new" });
                cat.BundleCacheMB = 0;
                int evicted = cat.EvictNow(-1, true);
                bool gone = !cat.Ledger.Knows(key);
                string third = Add(out err);
                int copies3 = Copies();
                int reloads3 = cat.Ledger.ReloadedAssets;
                UnloadUnusedNow();
                int copies4 = Copies();
                Info($"BOS: budget 0: {evicted} evicted, the bundle {(gone ? "evicted" : "still open")}; re-add {(third != null ? "ok" : err)}; prefab copies {copies2} -> {copies3}, after UnloadUnusedAssets {copies4}; reloads {reloads0} -> {reloads3}");
                Check("BOS: budget 0: the bundle is evicted, the re-add reopens it with a second prefab copy (a reload), UnloadUnusedAssets frees the first",
                      gone && third != null && copies3 == copies2 + 1 && reloads3 > reloads0 && copies4 <= copies2,
                      $"evicted {gone}, copies {copies2} -> {copies3} -> {copies4}, reloads {reloads0} -> {reloads3}");
                h.Call(new { cmd = "scene.new" });
            }
        }
        finally { cat.BundleCacheMB = budget0; }
    }

    // ================================================================ BOL (loose PNG readability, LQ22)

    static void BOL(RuntimeServices services)
    {
        // a synthetic 4 x 4 PNG made in memory (no file, no image of the game)
        var src = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) src.SetPixel(x, y, new Color(x / 3f, y / 3f, 0.5f, 1f));
        src.Apply(false);
        var png = src.EncodeToPNG();
        Object.DestroyImmediate(src);
        var loose = services.Catalog.Loose;
        bool keep0 = LooseTextures.KeepReadable;
        LooseTextures.KeepReadable = false;
        var a = loose.Decode("abdata/test/bol.png", png);
        LooseTextures.KeepReadable = true;
        var b = loose.Decode("abdata/test/bol.png", png);
        LooseTextures.KeepReadable = keep0;
        Check("BOL: a loose PNG texture is not readable after its upload by default; KeepReadable (tests) keeps the CPU copy; same size, format and mips",
              a && b && !a.isReadable && b.isReadable && a.width == b.width && a.height == b.height && a.format == b.format && a.mipmapCount == b.mipmapCount,
              $"default readable {(a ? a.isReadable.ToString() : "null")}, kept {(b ? b.isReadable.ToString() : "null")}, {(a ? a.width + "x" + a.height + " " + a.format + " mips " + a.mipmapCount : "")}");
        if (a) Object.DestroyImmediate(a);
        if (b) Object.DestroyImmediate(b);
        Info("BOL: CPU readers in the runtime (GetPixels / GetPixelData / GetRawTextureData / EncodeTo): capture, thumbnail capture, " +
             "colour grading bake, pose fragments, test acceptance helpers - none reads a loose PNG texture; the thumbnail disk cache copies them on the GPU");
    }

    // ================================================================ BO6

    static void BO6()
    {
        var cards = MakerCoreTest.SampleCards();
        int limit = int.Parse(Arg("-limit", "1000"));
        if (cards.Count > limit) cards = cards.Take(limit).ToList();
        var runs = new Dictionary<string, string>[2];
        var errors = new List<string>();
        int baselineEqual = 0, baselineMissing = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            bool evict = pass == 1;
            var h = MakerCoreTest.Harness.Open();
            var cat = h.Catalog;
            if (evict) cat.BundleCacheMB = 0;
            var shas = runs[pass] = new Dictionary<string, string>(StringComparer.Ordinal);
            int k = 0;
            foreach (var file in cards)
            {
                var owner = cat.NewOwner("card:" + (k++));
                MakerCoreTest.Built b;
                using (cat.Own(owner)) b = h.Build(file);
                if (b.NotCharacter) { b.Dispose(); cat.Release(owner); continue; }
                if (!b.Ok) { errors.Add($"pass {pass} card #{k}: {b.Error}"); b.Dispose(); cat.Release(owner); continue; }
                var snap = b.Snapshot();
                string sha = snap.Sha256;
                shas[b.CardSha] = sha;
                if (pass == 0)
                {
                    var bl = MakerCoreTest.LoadBaseline(b.CardSha);
                    if (bl == null) baselineMissing++;
                    else if (bl.Sha256 == sha) baselineEqual++;
                }
                b.Context?.Editor?.ReleaseBundleOwners();
                b.Dispose();
                cat.Release(owner);
                if (evict)
                {
                    cat.EvictNow(-1, true);
                    UnloadUnusedNow();
                }
            }
            Info($"BO6 pass {pass} ({(evict ? "budget 0, eviction and UnloadUnusedAssets between every card" : "no eviction")}): {shas.Count} snapshots; ledger {StatsLine(cat)}");
            h.Dispose();
            UnloadUnusedNow();
        }
        var diff = runs[0].Where(kv => !runs[1].TryGetValue(kv.Key, out var s) || s != kv.Value).Select(kv => kv.Key.Substring(0, 12)).ToList();
        Check($"BO6: the S1 snapshots of {runs[0].Count} cards are equal with and without forced eviction",
              runs[0].Count > 0 && runs[0].Count == runs[1].Count && diff.Count == 0 && errors.Count == 0,
              diff.Count > 0 ? "different: " + string.Join(", ", diff.Take(8)) : errors.Count > 0 ? string.Join("; ", errors.Take(3)) : $"{runs[0].Count} equal");
        Info($"BO6: the pass without eviction equals the stored S1 baselines on {baselineEqual} cards ({baselineMissing} without a baseline)");
    }
}

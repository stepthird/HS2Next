// Batch test: the Studio list DB built from the configured
// sources (vanilla + all zipmods). Checks the vanilla and mod counts of the spec, the parity
// switches, a few records, asset existence through the catalog, and the warm-start time.
// Writes Spike/out/content/studio_lists.txt (+ studio_lists_diagnostics.txt).
// Run: -executeMethod StudioListTest.Run [-seed N]
// Paths come from Config/settings.local.json (see CardResolveTest.Settings).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Content;
using HS2Next.Content.Studio;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class StudioListTest
{
    static string Arg(string name, string def)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    public static void Run()
    {
        var sb = new StringBuilder();
        int pass = 0, fail = 0;
        void Check(string what, bool ok)
        {
            if (ok) pass++; else fail++;
            sb.AppendLine($"{(ok ? "ok  " : "FAIL")} {what}");
        }
        void Eq(string what, long expected, long measured) => Check($"{what}: expected {expected}, measured {measured}", expected == measured);
        void Note(string s) => sb.AppendLine("     " + s);

        // ---------- build ----------
        sb.AppendLine("# build");
        var cat = CardResolveTest.BuildDb(sb);
        var sw = Stopwatch.StartNew();
        var db = cat.LoadStudioLists();
        long first = sw.ElapsedMilliseconds;
        sw.Restart();
        var again = cat.LoadStudioLists();
        long second = sw.ElapsedMilliseconds;
        sb.AppendLine($"LoadStudioLists: first call {first} ms ({string.Join(", ", db.TimingsMs.Select(kv => kv.Key + " " + kv.Value))}); second call {second} ms");
        sb.AppendLine($"IsInstallAdd {db.IsInstallAdd}; EmulateAIInstallGate {db.Options.EmulateAIInstallGate}; Lenient {db.Options.Lenient}");
        Check($"warm LoadStudioLists under 1 s (first call in this process {first} ms)", first < 1000);
        foreach (var s in db.Summary()) sb.AppendLine("  " + s);
        Check("second build gives the same counts", again.Items.Count == db.Items.Count && again.Anime.Count == db.Anime.Count && again.Diagnostics.Count == db.Diagnostics.Count);
        db = again;

        // ---------- vanilla (Appendix A.1) ----------
        sb.AppendLine();
        sb.AppendLine("# vanilla (spec Appendix A.1)");
        Eq("vanilla info bundles", 27, db.Counter("vanilla.bundles"));
        Eq("vanilla list assets: ExcelData", 830, db.Counter("vanilla.listAssets.exceldata"));
        Eq("vanilla list assets: ItemColorData", 47, db.Counter("vanilla.listAssets.itemdata"));
        bool V(StudioRecord r) => r.IsVanilla && r.Visible;
        bool VG(List<string> declaredBy) => declaredBy.Contains(ContentIds.Vanilla, StringComparer.OrdinalIgnoreCase);
        var vGroups = db.ItemGroups(true).Where(g => VG(g.DeclaredBy)).ToList();
        Eq("item groups declared by vanilla", 21, vGroups.Count);
        var vCats = db.AllItemCategories.Where(c => VG(c.DeclaredBy)).ToList();
        Eq("item categories declared by vanilla", 88, vCats.Count);
        Eq("vanilla item categories without vanilla items", 15, vCats.Count(c => !db.ItemsIn(c).Any(i => i.IsVanilla)));
        var vItems = db.Items.All.Where(V).ToList();
        Eq("vanilla items (visible)", 1285, vItems.Count);
        Check("every vanilla item is in a declared group + category", vItems.All(i => db.TryGetItemCategory(i.Group, i.Category, out var c) && c.HasGroup));
        Eq("vanilla item numbers are unique", vItems.Count, vItems.Select(i => i.No).Distinct().Count());
        Eq("max vanilla item number", 1948, vItems.Max(i => i.No));
        Eq("vanilla items with FK bone lists", 13, vItems.Count(i => i.Bones != null));
        Eq("ItemData colour records", 1895, db.ItemColors.Count);
        Eq("ItemData rows read (57: 2033, 60: 2)", 2035, db.Counter("itemdata.rows"));
        Eq("vanilla items with colour data", 1249, vItems.Count(i => i.Color != null));
        var hist = vItems.GroupBy(i => i.Color?.FlagCount ?? 0).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}");
        Note($"colour flag histogram (no data counts as 0): {string.Join(" ", hist)} (spec: 0:371 1:446 2:189 3:227 4:16)");
        Eq("vanilla lights", 9, db.Lights.All.Count(V));
        var vAnimeGroups = db.AnimeGroups(true).Where(g => VG(g.DeclaredBy)).ToList();
        Eq("anime groups declared by vanilla", 35, vAnimeGroups.Count);
        Eq("anime categories declared by vanilla (non-AI)", 411, db.AllAnimeCategories.Count(c => VG(c.DeclaredBy) && !c.AiContent));
        // Anime counts are per-GUID winners (mods replace 30 vanilla keys in the global index).
        var vAnime = db.Anime.All.Where(a => a.IsVanilla && !a.AiContent).ToList();
        Eq("vanilla Anime records (non-AI)", 549, vAnime.Count(a => !a.IsH));
        Eq("vanilla HAnime records", 6145, vAnime.Count(a => a.IsH));
        Eq("vanilla Anime with option items", 145, vAnime.Count(a => !a.IsH && a.Options.Count > 0));
        Eq("vanilla HAnime with option items", 494, vAnime.Count(a => a.IsH && a.Options.Count > 0));
        Eq("voice groups declared by vanilla", 14, db.VoiceGroups(true).Count(g => VG(g.DeclaredBy)));
        Eq("voice categories declared by vanilla", 112, db.AllVoiceCategories.Count(c => VG(c.DeclaredBy)));
        Eq("vanilla voices", 3360, db.Voices.All.Count(V));
        Eq("vanilla BGM", 21, db.Bgm.All.Count(V));
        var vMaps = db.Maps.All.Where(V).ToList();
        Eq("vanilla maps", 29, vMaps.Count);
        Check("vanilla map ids are 10..38", vMaps.Select(m => m.No).OrderBy(x => x).SequenceEqual(Enumerable.Range(10, 29)));
        Eq("vanilla filters", 33, db.Filters.All.Count(V));
        Eq("vanilla probes", 17, db.Probes.All.Count(V));
        Eq("vanilla skies", 5, db.Skies.All.Count(V));
        Eq("vanilla FK bones", 271, db.Bones.All.Count(V));
        Eq("vanilla accessory point groups", 10, db.AccessoryPointGroups.All.Count(V));
        Check("Filter/Probe/Sky vanilla ids are 0..n-1 in order", db.Filters.All.Where(V).Select(f => f.No).SequenceEqual(Enumerable.Range(0, 33))
            && db.Probes.All.Where(V).Select(f => f.No).SequenceEqual(Enumerable.Range(0, 17)) && db.Skies.All.Where(V).Select(f => f.No).SequenceEqual(Enumerable.Range(0, 5)));

        sb.AppendLine("## redefinitions (last wins) and gates");
        Eq("ItemList redefinitions", 141, db.Counter("redef.ItemList.vanilla"));
        Eq("ItemCategory redefinitions", 2, db.Counter("redef.ItemCategory.vanilla"));
        Eq("ItemGroup redefinitions", 3, db.Counter("redef.ItemGroup.vanilla"));
        Eq("ItemBoneList redefinitions", 3, db.Counter("redef.ItemBoneList.vanilla"));
        Eq("AnimeGroup redefinitions", 15, db.Counter("redef.AnimeGroup.vanilla"));
        Eq("HAnime redefinitions", 428, db.Counter("redef.HAnime.vanilla"));
        Eq("Map redefinitions", 5, db.Counter("redef.Map.vanilla"));
        Eq("Filter redefinitions", 1, db.Counter("redef.Filter.vanilla"));
        Eq("ItemList files behind the AI gate", 27, db.Counter("gate.ai.ItemListFiles"));
        Eq("ItemList rows behind the AI gate", 646, db.Counter("gate.ai.ItemListRows"));
        Eq("AnimeCategory rows behind the AI gate", 12, db.Counter("gate.ai.AnimeCategory"));
        Eq("Anime rows behind the AI gate", 426, db.Counter("gate.ai.AnimeRows"));
        Eq("BGM rows < 100 behind the AI gate", 19, db.Counter("gate.ai.Bgm"));
        Eq("Map rows 0/1 behind the AI gate", 2, db.Counter("gate.ai.Map"));
        Eq("vanilla HAnime rows with a missing bundle", 14, db.Diagnostics.Count(d => d.StartsWith("skip HAnime row (missingBundle)") && !d.Contains(" [")));
        Eq("vanilla AnimeCategory file read before its group", 1, db.Diagnostics.Count(d => d.StartsWith("AnimeCategory file read before its group") && !d.Contains(" [")));

        sb.AppendLine("## records");
        Check("fallback item illusion.hs2#399 present", db.TryGetItem(StudioDb.FallbackItem, out var sphere) && sphere.Visible && sphere.Group == 0 && sphere.Category == 0
            && sphere.Name == "スフィア(通常）" && sphere.Manifest == "studio00" && sphere.BundlePath == "studio/00.unity3d" && sphere.FileName == "p_ai_stu_sphere01_02");
        Check("ResolveItem(unknown) -> fallback 399", db.ResolveItem(new ContentId("hs2next.test.missing", 1)) == sphere);
        var gatedItem = db.Items.All.FirstOrDefault(i => i.IsVanilla && i.AiContent);
        Check("ResolveItem(AI-gated vanilla item) -> fallback 399 while the gate is on", gatedItem != null && db.ResolveItem(gatedItem.Key) == sphere && db.TryGetItem(gatedItem.Key, out var gi) && gi == gatedItem);
        Check("patch bundle redefinition 0/18/590: last wins (name, manifest, bundle from 03)", db.TryGetItem(ContentId.Vanilla(590), out var i590) && i590.Group == 0 && i590.Category == 18
            && i590.Name == "スフィア(透明）" && i590.Manifest == "studio03" && i590.BundlePath == "studio/03.unity3d");
        Check("590 keeps its first-insertion position (from bundle 00)", i590 != null && db.Shadowed.OfType<StudioItem>().Any(s => s.Key == i590.Key && s.ListBundle == "studio/info/00.unity3d" && s.Order == i590.Position));
        Check("Filter 32 defined twice in Filter_00: the second (スモッグ / Lut_Desert) wins", db.TryGetFilter(ContentId.Vanilla(32), out var f32) && f32.Name == "スモッグ" && f32.FileName == "Lut_Desert");
        Check("Map 16 redefined by Map_35 (manifest studio35)", db.TryGetMap(ContentId.Vanilla(16), out var m16) && m16.Manifest == "studio35" && m16.Name == "トイレ前");
        Check("AnimeGroup 11 スタジオ: sort 1 from bundle 34", db.TryGetAnimeGroup(11, out var ag11) && ag11.Name == "スタジオ" && ag11.Sort == 1);
        Check("anime category 0/0 基本 visible, 0/1 gated", db.TryGetAnimeCategory(0, 0, out var ac0) && ac0.Visible && db.TryGetAnimeCategory(0, 1, out var ac1) && ac1.AiContent && !ac1.Visible);
        var a000 = db.FindAnime(0, 0, 0);
        Check("anime 0/0/0 初期ポーズ visible, 0/0/1 gated", a000 != null && a000.Visible && a000.IsVanilla && (db.FindAnime(0, 0, 1)?.Visible ?? false) == false);
        Check("HAnime 1/100/1 弱ループ: controllers and clip", db.TryGetAnime(ContentIds.Vanilla, 1, 100, 1, out var h1) && h1.IsH && h1.Name == "弱ループ" && h1.BundlePath == "animator/h/female/01/aibu.unity3d"
            && h1.FileName == "aia_f_base" && h1.OverrideFile == "aia_f_00" && h1.Clip == "WLoop" && h1.IsMotion && h1.BreastLayer == 0);
        Check("anime 11/14/0 ポールダンス has its option item", db.TryGetAnime(ContentIds.Vanilla, 11, 14, 0, out var pole) && pole.Options.Count > 0 && pole.Options[0].Asset == "p_ai_hi_polestage00_ei_stu"
            && pole.Options[0].Manifest == "studio03" && pole.Options[0].ControllerAsset == "pole_studio" && pole.Options[0].ParentagePairs().Any(p => p.parent == "cf_J_Root"));
        Check("lights: 0 全体光 target All, 3 target Chara, 6 target Map", db.TryGetLight(ContentId.Vanilla(0), out var l0) && l0.Name == "全体光" && l0.FileName == "p_stneo_Directionallight" && l0.TargetKind == StudioLightTarget.All
            && db.TryGetLight(ContentId.Vanilla(3), out var l3) && l3.TargetKind == StudioLightTarget.Chara && db.TryGetLight(ContentId.Vanilla(6), out var l6) && l6.TargetKind == StudioLightTarget.Map);
        Check("sky 0 晴れた空 rotation (40,0,0)", db.TryGetSky(ContentId.Vanilla(0), out var sk0) && sk0.Name == "晴れた空" && sk0.SunRotation == new Vector3(40, 0, 0) && sk0.FileName == "m_h2_SkySuny00_00");
        Check("bone 1 cf_J_Head 頭 group 10; 126 syncs with 127", db.TryGetBone(ContentId.Vanilla(1), out var b1) && b1.TransformName == "cf_J_Head" && b1.Group == 10 && b1.SyncNo == -1
            && db.TryGetBone(ContentId.Vanilla(126), out var b126) && b126.SyncNo == 127 && db.FindBone(1) == b1);
        Check("accessory point group 0 髪 1-5", db.TryGetAccessoryPointGroup(ContentId.Vanilla(0), out var ap0) && ap0.Name == "髪" && ap0.First == 1 && ap0.Last == 5);
        Check("FK bones of item 1399 (3 joints)", db.TryGetItem(ContentId.Vanilla(1399), out var fk) && fk.Bones != null && fk.Bones.Count == 3 && fk.Bones[0] == "o_koi_stu_flybace_joint_00");
        var expectedOrder = new[] { 0, 1, 14, 2, 15, 3, 16, 4, 5, 17, 10, 18, 6, 7, 8, 9, 19, 11, 12, 13, 20 };
        var vOrder = db.ItemGroups(true).Where(g => VG(g.DeclaredBy)).Select(g => g.Number).ToArray();
        Check($"vanilla item groups in sort order: {string.Join(",", vOrder)}", vOrder.SequenceEqual(expectedOrder));
        var firstCat = db.CategoriesOf(db.ItemGroups().First(g => g.Number == 1)).First();
        var firstItems = db.ItemsIn(firstCat).Take(3).Select(i => i.No).ToArray();
        sb.AppendLine($"     group 1 first category {firstCat}: first items {string.Join(",", firstItems)}");

        // ---------- gate off ----------
        var off = cat.LoadStudioLists(new StudioLoadOptions { EmulateAIInstallGate = false });
        sb.AppendLine();
        sb.AppendLine("# EmulateAIInstallGate = false (with IsInstallAI)");
        Eq("vanilla items", 1931, off.Items.All.Count(V));
        Eq("vanilla Anime records", 975, off.Anime.All.Count(a => a.IsVanilla && !a.IsH));
        Eq("vanilla anime categories", 423, off.AllAnimeCategories.Count(c => VG(c.DeclaredBy)));
        Eq("vanilla BGM", 40, off.Bgm.All.Count(V));
        Eq("vanilla maps", 31, off.Maps.All.Count(V));
        Check("ResolveItem(AI item) returns the item when the gate is off", gatedItem != null && off.ResolveItem(gatedItem.Key)?.Key == gatedItem.Key);

        // ---------- mods (Appendix A.2) ----------
        sb.AppendLine();
        sb.AppendLine("# zipmods (Appendix A.2)");
        // the zipmod loader's set on (Phase 4.4) skips the 2 [AiLegend] map zipmods (<game> AI Girl): 1 Studio CSV and 1 map row each
        bool set44 = ContentCatalog.SideloaderZipmodSet;
        Note($"zipmod set {(ContentCatalog.SideloaderZipmodSet ? "on" : "off")}: {(cat.ScannedZipmods ?? new List<ZipmodInfo>()).Count(z => z.SkippedBy == "game")} files skipped by the <game> filter, {(cat.ScannedZipmods ?? new List<ZipmodInfo>()).Count(z => z.SkippedBy == "version")} older files of a GUID");
        Eq("zipmods with Studio CSVs", set44 ? 2583 : 2585, db.Counter("mod.zipmodsWithStudioCsvs"));
        Eq("Studio CSV files", set44 ? 5969 : 5971, db.Counter("mod.csvFiles"));
        // DEVIATION: the spec counts 1,191 case-sensitively; here bundle paths compare case-insensitively,
        // so studio/info/skeleton and studio/info/Skeleton are one virtual bundle.
        Eq("virtual bundles (spec 1,191 counted case-sensitively; 'skeleton'/'Skeleton' merge here)", 1190, db.Counter("mod.virtualBundles"));
        Note($"virtual bundles shared by >1 GUID: {db.Counter("mod.virtualBundlesSharedByGuids")} (spec 52, +1 for the merged 'skeleton' pair)");
        Eq("item rows read", 23794, db.Counter("mod.itemRowsRead"));
        Eq("distinct item group numbers (ItemGroup CSVs the game reads)", 162, db.Counter("mod.distinct.ItemGroup"));
        Note($"including the CSVs never read (suffix rule): {db.Counter("mod.distinct.ItemGroup.inclUnreadCsvs")}; groups only in unread CSVs: {db.Counter("mod.ItemGroupOnlyInUnreadCsvs")}");
        Eq("item groups shared by >1 mod GUID", 81, db.Counter("mod.sharedByGuids.ItemGroup"));
        Eq("item group numbers with differing names among mods", 0, db.Counter("conflict.names.ItemGroup.mod"));
        Eq("distinct (group, category) pairs", 1394, db.Counter("mod.distinct.ItemCategory"));
        Eq("item categories shared by >1 mod GUID", 168, db.Counter("mod.sharedByGuids.ItemCategory"));
        Eq("item categories with differing names among mods", 7, db.Counter("conflict.names.ItemCategory.mod"));
        Note($"item categories with differing names incl. vanilla definitions: {db.Counter("conflict.names.ItemCategory")}");
        Eq("duplicate ids inside single mods", 24, db.Counter("dup.ItemList.modIds"));
        Eq("rows with those duplicate ids", 52, db.Counter("dup.ItemList.modRows"));
        Eq("(GUID, group, category, id) duplicates", 10, db.Counter("dup.ItemList.modFullKeys"));
        Note($"rows with (GUID, group, category, id) duplicates: {db.Counter("dup.ItemList.modFullKeyRows")}");
        Eq("mod rows placed into vanilla groups", 198, db.Counter("mod.items.intoVanillaGroups"));
        Eq("mod ItemList files hit by the AI file-name gate", 7, db.Counter("gate.ai.ItemListFiles.mod"));
        Eq("mod ItemList rows hit by the AI file-name gate", 23, db.Counter("gate.ai.ItemListRows.mod"));
        Eq("single-list CSVs not read (suffix != folder)", 50, db.Counter("parity.singleListSuffixMismatch"));
        Eq("... all of them ItemGroup", 50, db.Counter("parity.singleListSuffixMismatch.itemgroup"));
        Eq("mod items invisible because their group is defined nowhere", 5, db.Counter("hidden.Item.noGroup.mod"));
        Note($"mod items invisible for a missing category: {db.Counter("hidden.Item.noCategory.mod")}; AI-gated: {db.Counter("hidden.Item.ai.mod")}");
        Note($"category rows read before their group (dropped by the game): item {db.Counter("order.categoryRowsBeforeGroup.ItemCategory")} in {db.Counter("order.categoryFileBeforeGroup.ItemCategory")} files (spec: 185 definitions), of which the group is defined later for {db.Counter("order.categoryRowsGroupDefinedLater.ItemCategory")} rows / {db.Counter("order.categoryGroupsDefinedLater.ItemCategory")} groups (spec: 10 groups); anime {db.Counter("order.categoryRowsBeforeGroup.AnimeCategory")}");
        Note($"item categories whose group is defined nowhere (orphans): {db.Counter("orphan.ItemCategory")}");
        Eq("item (group, category) keys defined only before their (existing) group = hidden by the game, kept here", 0, db.Counter("order.categoryInvisibleInGame.ItemCategory"));
        Eq("anime: global raw keys from mods (spec 1,449 incl. the row with a missing bundle)", 1448, db.Counter("global.Anime.modKeys"));
        Eq("anime: mod rows dropped for a missing controller bundle", 1, db.Counter("skip.Anime.missingBundle"));
        Eq("anime: mod keys colliding with vanilla", 30, db.Counter("global.Anime.modKeysCollidingWithVanilla"));
        // DEVIATION: the colliding mod keeps its CSVs in the
        // NUMERIC folder studio/info/20, which sorts between vanilla 14 and 31. It wins 11/14/0-4 (vanilla 03)
        // but loses 19/0/0-24 to vanilla bundle 34.
        Eq("anime: ... and replacing vanilla in the global index (spec says 30; folder '20' sorts before vanilla 34)", 5, db.Counter("global.Anime.modKeysReplacingVanilla"));
        Eq("anime: keys contested by more than one mod GUID", 11, db.Counter("global.Anime.keysContestedByMods"));
        Eq("anime: (GUID, group, category, no) duplicates inside mods", 3, db.Counter("redef.Anime.mod"));
        Note($"anime rows read from mods: {db.Counter("mod.rowsRead.Anime")} (spec: 1,478 rows in 38 files); contested keys logged: {db.Counter("global.Anime.contestedKeys")}");
        var dance = db.FindAnime(11, 14, 0);
        Check("global anime 11/14/0 is won by a mod (the game's last-wins winner) and the vanilla row stays reachable",
            dance != null && !dance.IsVanilla && db.TryGetAnime(ContentIds.Vanilla, 11, 14, 0, out var vd) && !vd.GlobalWinner && db.AnimeCandidates(11, 14, 0).Contains(vd));
        // The spec's 661 entry rows include 10 single-cell "0" lines (line 2 of the 10 one-header KKY map
        // CSVs); the Map reader skips them as header rows, as the game does.
        Eq("map rows read from mods (spec 661 entry rows incl. 10 single-cell '0' lines)", set44 ? 649 : 651, db.Counter("mod.rowsRead.Map"));
        Eq("CSV data rows swallowed as header rows (MakotoYuki90 Light 9, Probe 17)", 2,
            db.Counter("parity.csvDataRowReadAsHeader.Light") + db.Counter("parity.csvDataRowReadAsHeader.Probe"));
        Note($"all CSV data rows swallowed as headers: {db.Counter("parity.csvDataRowReadAsHeader")} ({string.Join(", ", db.Counters.Where(kv => kv.Key.StartsWith("parity.csvDataRowReadAsHeader.")).Select(kv => kv.Key.Substring(30) + " " + kv.Value))})");
        Eq("mod ItemBoneList rows read", 333, db.Counter("mod.rowsRead.ItemBoneList"));
        Eq("mod ItemBoneList rows without an item of their own GUID (dropped)", 17, db.Counter("itembonelist.noItem.mod"));
        Eq("mod ItemBoneList rows whose file-name group/category differs from the item (dropped, as HS2)", 30, db.Counter("itembonelist.fileGroupCategoryDiffers.mod"));
        Eq("vanilla ItemBoneList rows whose file-name group/category differs from the item", 0, db.Counter("itembonelist.fileGroupCategoryDiffers.vanilla"));
        Eq("hs2-scene-dependency manifests (zipmods declaring one)", 18, cat.ZipmodsByGuid.Values.Count(z => z.SceneDependencies.Count > 0));
        Note($"ItemList early stops: {db.Counter("parity.itemListStoppedEarly")} lists, {db.Counter("parity.itemListRowsLost")} rows lost; non-integer rows removed after merging: {db.Counter("mod.csvRowsRemovedNonInt")}");

        // ---------- lenient ----------
        var len = cat.LoadStudioLists(new StudioLoadOptions { Lenient = true });
        sb.AppendLine();
        sb.AppendLine("# Lenient = true");
        Eq("single-list CSVs accepted despite the suffix", 50, len.Counter("lenient.singleListAccepted.itemgroup"));
        Eq("mod items invisible because their group is defined nowhere", 0, len.Counter("hidden.Item.noGroup.mod"));
        var orphans = db.Items.All.Where(i => !i.IsVanilla && !i.Visible && !db.TryGetItemGroup(i.Group, out _)).ToList();
        Check($"the {orphans.Count} parity-invisible items are visible with lenient", orphans.Count == 5 && orphans.All(o => len.TryGetItem(o.Key, out var x) && x.Visible));
        Note($"orphans: {string.Join("; ", orphans.Select(o => $"{o.Key} g{o.Group}"))}");
        Eq("CSV data rows swallowed as headers", 0, len.Counter("parity.csvDataRowReadAsHeader"));
        Note($"lenient: items visible {len.Items.VisibleCount} (parity {db.Items.VisibleCount}), lights {len.Lights.Count} (parity {db.Lights.Count}), probes {len.Probes.Count} (parity {db.Probes.Count})");

        // ---------- load-order drops ----------
        var ord = cat.LoadStudioLists(new StudioLoadOptions { EmulateLoadOrderDrops = true });
        sb.AppendLine();
        sb.AppendLine("# EmulateLoadOrderDrops = true");
        Note($"category definitions dropped: item {ord.Counter("order.categoryDropped.ItemCategory")}, anime {ord.Counter("order.categoryDropped.AnimeCategory")}, voice {ord.Counter("order.categoryDropped.VoiceCategory")}");
        Note($"visible items: {ord.Items.VisibleCount} (two-phase {db.Items.VisibleCount}); hidden for a missing category: {ord.Counter("hidden.Item.noCategory.mod")}");
        Check("visible items with the game's drops = two-phase minus the items of categories defined only before their group",
            db.Items.VisibleCount - ord.Items.VisibleCount == ord.Counter("hidden.Item.noCategory.mod"));
        db = cat.LoadStudioLists();

        // ---------- assets through the catalog ----------
        sb.AppendLine();
        sb.AppendLine("# asset existence through the catalog");
        var rnd = new System.Random(int.Parse(Arg("-seed", "12345")));
        List<T> Pick<T>(IEnumerable<T> src, int n) => src.OrderBy(_ => rnd.Next()).Take(n).ToList();
        var vSample = Pick(db.Items.All.Where(V), 40);
        int vExist = vSample.Count(i => cat.BundleExists(i.BundlePath));
        int vLoaded = 0;
        foreach (var it in vSample)
        {
            var go = cat.LoadAsset<GameObject>(it);
            if (go) vLoaded++; else sb.AppendLine($"     vanilla prefab missing: {it}");
        }
        Eq("40 random vanilla items: bundle exists", vSample.Count, vExist);
        Eq("40 random vanilla items: LoadAsset<GameObject> returns a prefab", vSample.Count, vLoaded);
        cat.UnloadAll();
        var mSample = Pick(db.Items.All.Where(i => !i.IsVanilla && i.Visible), 40);
        int mExist = 0, mLoaded = 0;
        foreach (var it in mSample)
        {
            if (cat.BundleExists(it.BundlePath)) mExist++; else sb.AppendLine($"     mod bundle missing: {it} [{it.Guid}]");
            if (cat.LoadAsset<GameObject>(it)) mLoaded++; else sb.AppendLine($"     mod prefab not loaded: {it} [{it.Guid}]");
            cat.UnloadAll();
        }
        Eq("40 random mod items: bundle exists (own zipmod, other zipmod or abdata)", mSample.Count, mExist);
        Note($"40 random mod items: LoadAsset<GameObject> returned a prefab for {mLoaded}");
        Eq("vanilla maps: scene bundle exists", 29, vMaps.Count(m => cat.BundleExists(m.BundlePath)));
        var aSample = Pick(db.Anime.All.Where(a => a.Visible), 20);
        Eq("20 random visible anime: controller bundle exists", aSample.Count, aSample.Count(a => cat.BundleExists(a.BundlePath)));
        int aLoaded = 0;
        foreach (var a in aSample.Where(a => a.IsVanilla)) if (cat.LoadAsset<RuntimeAnimatorController>(a)) aLoaded++;
        Note($"vanilla anime in the sample {aSample.Count(a => a.IsVanilla)}, controllers loaded {aLoaded}; mod {aSample.Count(a => !a.IsVanilla)}");
        cat.UnloadAll();

        // Dependency manifest preload: an item whose manifest column names a declared manifest.
        var depItem = db.Items.All.Where(i => !i.IsVanilla && i.Visible && cat.ZipmodsDeclaringManifest(i.Manifest).Count > 0)
            .OrderBy(i => i.Zipmod != null && cat.ZipmodsDeclaringManifest(i.Manifest).Contains(i.Zipmod) ? 1 : 0).FirstOrDefault();
        if (depItem != null)
        {
            var go = cat.LoadAsset<GameObject>(depItem);
            var depLog = cat.Log.Where(l => l.StartsWith("dependency manifest")).ToList();
            Check($"dependency manifest preload: {depItem} (manifest {depItem.Manifest}, owner {depItem.Guid}) loads a prefab", go && depLog.Count == 0);
        }
        else Check("an item with a dependency manifest exists", false);
        cat.UnloadAll();

        // ---------- anime display order (as HS2 Studio measured it) ----------
        sb.AppendLine();
        sb.AppendLine("# anime display order: a mod row that repeats a base-game key keeps the key's first place (HS2 lists 19/0 and 19/5 by key)");
        foreach (var (g, c, vanilla, total) in new[] { (19, 0, 10, 44), (19, 5, 15, 26) })
        {
            db.TryGetAnimeCategory(g, c, out var ac);
            var rows = db.AnimeIn(ac).ToList();
            var nos = rows.Select(a => a.No).ToList();
            bool ascending = nos.SequenceEqual(Enumerable.Range(0, nos.Count));
            bool vanillaFirst = rows.Take(vanilla).All(a => a.IsVanilla) && rows.Skip(vanilla).All(a => !a.IsVanilla);
            Check($"anime {g}/{c}: HS2's order (nos 0..{total - 1} ascending, the {vanilla} base-game rows first; measured {nos.Count} rows, first {string.Join(",", nos.Take(12))})",
                nos.Count == total && ascending && vanillaFirst);
            var listed = db.Anime.Enumerate().Where(a => a.Group == g && a.Category == c).ToList();
            Check($"anime {g}/{c}: the table's display order (the browser's rows) = the category's order", listed.SequenceEqual(rows));
        }
        int offOrder = 0;
        foreach (var ac in db.AllAnimeCategories)
        {
            var rows = db.AnimeIn(ac).ToList();
            var listed = db.Anime.Enumerate().Where(a => a.Group == ac.Group && a.Category == ac.Number).ToList();
            if (!listed.SequenceEqual(rows.Where(a => a.Visible))) offOrder++;
        }
        Eq("anime categories whose table order differs from the category's display order", 0, offOrder);

        // ---------- diagnostics ----------
        sb.AppendLine();
        sb.AppendLine("# counters");
        foreach (var kv in db.Counters.OrderBy(kv => kv.Key, StringComparer.Ordinal)) sb.AppendLine($"  {kv.Key} = {kv.Value}");
        sb.AppendLine();
        sb.AppendLine($"# diagnostics: {db.Diagnostics.Count} lines (studio_lists_diagnostics.txt); first 40:");
        foreach (var d in db.Diagnostics.Take(40)) sb.AppendLine("  " + d);

        sb.Insert(0, $"studio list test: {pass} passed, {fail} failed\n\n");
        var outDir = Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "content");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "studio_lists.txt"), sb.ToString().Replace("\r\n", "\n"));
        File.WriteAllText(Path.Combine(outDir, "studio_lists_diagnostics.txt"), string.Join("\n", db.Diagnostics) + "\n");
        cat.UnloadAll();
        Debug.Log($"STUDIO LIST TEST DONE: {pass} passed, {fail} failed");
        if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
    }
}

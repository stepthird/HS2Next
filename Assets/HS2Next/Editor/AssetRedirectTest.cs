// Tests of the zipmod loader's set and the per-asset redirect (Runtime/Content/AssetRedirect.cs, ContentCatalog.cs; the
// redirect of Phase 4.4, package R), headless unless noted:
//   AR1  the zipmod set: files with a GUID, GUIDs loaded, the 9 zipmods the <game> filter skips (named once in the catalog
//        log), the file kept of each GUID with several files, load order; the lists with the set on vs off differ by the
//        game filter's rows only (the two saya rows; the two AiLegend maps of the Studio lists)
//   AR2  every redirectable path of the Step 0 census (Tools/survey/redirect_step0_census.txt, section 10.1): the copies in
//        load order, per copy the names, the names it serves, the serialized / resource file names and the external
//        references; 96 paths
//   AR3  the vanilla hair rows on the redirected hair paths: every prefab served by its zipmod copy (useMesh 1), the abdata
//        prefabs with the redirect off (useMesh 0); the rows whose _Mesh_onoff differs between the copies
//   AR4  the kimono tops 240#223 / #235 / #244: useBreak on with the redirect (off without); a breakRate writes _AlphaEx
//   AR5  the body (BR-Chan): cm_o_dan_f with a SkinnedMeshRenderer under an inactive N_dan, its bones bound to the skeleton,
//        o_body_cf slot 0 = the draw material, no "bones kept" for p_cf_body_00; the body digest equal to the redirect off
//        except the materials section's cm_o_dan_f block (RT hashes with a graphics device)
//   AR6  a mod row on a path two zipmods ship loads the first copy's asset
//   AR7  two copies of one path whose serialized files share a name (K1T0K1TN_eyelashes): (b) a copy handed out stays open
//        and the other is loaded from memory with its file names renamed (content.cabRewrite; package R2), both usable;
//        (c) with the rewrite off the copy handed out serves the other copy's request (content.cabClash); (a) a
//        thumbnail-only copy is unloaded and the other opened; no exception
//   AR8  thumbnails: SourceStampOf(e, path, asset) names the serving file, OpenBundleAsync(e, path, asset) opens it
//   AR9  the switches off: today's sources (vanilla rows read abdata), the earlier zipmod set (no game filter, the last
//        file per GUID); no redirect counted
//   AR10 the fixed abdata paths: p_cf_anim, p_cf_head_bone, p_cf_body_00_Nml stay abdata, p_cf_body_00 is the zipmod's
//   AR11 (headless only) every name of every redirectable path loaded through the rule (each path from no open bundle): the paths where Unity
//        refuses a copy = the census's 5 paths whose serving copies share a serialized-file name; every refusal rewritten
//        (no content.cabClash) and every name of those paths loaded; a zipmod bundle that does not open is logged
//   AR12 (headless only) the CAB rewrite on the census's 5 paths: every copy that serves a name opened in load order, then
//        each copy's names loaded again with all of them open (the same objects), the cost of each rewrite (ms, MiB)
//   RU1  a shared resource-file name alone (chara/38/so_hair_b_38: the abdata file open, then HairMod's copy): clash or not
//   RU3  the names pass cold / cached, a lookup, a first redirected load, the upgrade of a version 6 scan cache
// AR5, AR7 and AR9 keep the two switch states apart: separate catalogs, every bundle unloaded in between. A graphics run (for
// AR5's RT hashes) should be limited to -only AR5: the other rows load assets no card build loads.
//   RunS1Explain (headless, for T): S1 targets + 20 (-all every card, -only a.png,b.png) built with the redirect on; every
//     line that differs from the snapshot of the same card built with the switches off (-offDir <folder> of
//     MakerCoreTest.RunBaseline -dir <folder> -sideloader off; without it the S1 baselines) is classified redirect.body,
//     redirect.bodyUncensor, redirect.hairCopy or redirect.kimono, else "other"; the lines that differ from the S1 baselines
//     but not from the switches-off snapshot are counted apart (not the redirect's).
//   BuildFixtures: the fixture bundles of the plain-Mono content suite (Editor/Fixtures/Redirect/redirect_{none,lz4,lz4hc,
//     lzma}.unity3d) from the three text assets in Editor/Fixtures/Redirect/src.
// Run: -executeMethod AssetRedirectTest.Run [-only AR1,AR7]; AssetRedirectTest.RunS1Explain [-all|-targets|-only ...]
//      [-offDir <folder>] [-other]; AssetRedirectTest.BuildFixtures.
// Reports: Spike/out/maker/asset_redirect_report.txt, asset_redirect_s1explain_report.txt; first line "SUMMARY: N PASS,
// M FAIL". Numbers only: nothing is rendered or viewed; SFW visual targets.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HS2Next.Chara;
using HS2Next.Chara.Edit;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Scenes.Runtime;
using HS2Next.Tests.MakerCore;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;
using static MakerTestKit;

public static class AssetRedirectTest
{
    const string ReportFile = "asset_redirect_report.txt";
    static string Root => ContentSettings.ProjectRoot;
    static string FixtureDir => Path.Combine(Root, "Assets", "HS2Next", "Editor", "Fixtures", "Redirect");
    static string CensusFile => Path.Combine(Root, "Tools", "survey", "redirect_step0_census.txt");
    static string ModsDir => Path.Combine(CardResolveTest.GameRoot, "mods");

    // ================================================================ expectations (Step 0 census, Tools/survey)

    static readonly string[] GameSkipped =
    {
        "AI_UncensorSelector Base.zipmod", "[Female][AI][ddaa33]BDSM_BODY_ARM.zipmod", "[Female][AI][ddaa33]BDSM_BODY_D1.zipmod",
        "[Female][AI][ddaa33]BDSM_BODY_D4.zipmod", "[Female][AI][SAC]UncensorSelectorPack v0.31.zipmod", "[lsmslkly] AIS_SAC_Puffy_D v6.1.zipmod",
        "[ddaa33] saya.zipmod", "[AiLegend] Hill Forest.zipmod", "[AiLegend] Japanese Village.zipmod",
    };

    // the file each GUID with several files keeps (relative to the install's mods folder; census section 1 rules)
    static readonly (string guid, string kept)[] VersionChoices =
    {
        ("spockbauru.hs2.girlsentrance", "HS2_GirlsEntrance.zipmod"),
        ("hanmen.cloth.shader", "[Hanmen] [Hanmen] Cloth shaders v4.6.zipmod"),
        ("TD78.OpenPussy", "Sideloader Modpack/TD78/[Female][HS2][TD78]OpenPussy.zipmod"),
        ("TD78.OpenPussy2", "Sideloader Modpack/TD78/[Female][HS2][TD78]OpenPussy2.zipmod"),
        ("[Hanmen]sss.skin", "Sideloader Modpack - MaterialEditor Shaders/Hanmen/[Hanmen] NEXT-GEN Shaders 4.15 Beta.zipmod"),
    };

    const int FilesWithGuid = 5938, GuidsLoaded = 5924, RedirectablePaths = 96, AbdataPaths = 18;
    // vanilla hair rows whose _Mesh_onoff differs between the abdata prefab and HairMod's (4.3 package A's measurement)
    static readonly int[] MeshOnOffRows = { 29, 50, 51, 52, 53, 56, 57, 58, 59, 60, 61, 62, 63, 64 };
    static readonly int[] KimonoTops = { 223, 235, 244 };
    const string Body = "chara/oo_base.unity3d", Lashes = "K1T0-K1TN/K1T0K1TN_eyelashes.unity3d", Hair38 = "chara/38/so_hair_b_38.unity3d";

    // ================================================================ helpers

    /// <summary>Sets both switches for a block and restores them.</summary>
    sealed class Switches : IDisposable
    {
        readonly bool set, red;
        public Switches(bool zipmodSet, bool redirect)
        {
            set = ContentCatalog.SideloaderZipmodSet; red = ContentCatalog.SideloaderAssetRedirect;
            ContentCatalog.SideloaderZipmodSet = zipmodSet; ContentCatalog.SideloaderAssetRedirect = redirect;
        }
        public void Dispose() { ContentCatalog.SideloaderZipmodSet = set; ContentCatalog.SideloaderAssetRedirect = red; }
    }

    /// <summary>A catalog with the vanilla and mod lists, the switches as given (left set for its loads).</summary>
    static ContentCatalog NewCatalog(bool zipmodSet, bool redirect, out double scanMs)
    {
        var c = ContentCatalog.FromSettings(CardResolveTest.Settings);
        ContentCatalog.SideloaderZipmodSet = zipmodSet;
        ContentCatalog.SideloaderAssetRedirect = redirect;
        c.LoadVanillaLists();
        var t = Stopwatch.StartNew();
        c.LoadModLists();
        scanMs = t.Elapsed.TotalMilliseconds;
        return c;
    }

    static MakerCoreTest.Harness HarnessOf(ContentCatalog c) => new MakerCoreTest.Harness
    {
        Catalog = c,
        Services = new RuntimeServices { Catalog = c, Hs2Root = CardResolveTest.GameRoot, Settings = new HS2Next.Scenes.Runtime.SceneRuntimeSettings { ManualStepping = true } },
        Importer = CharaDocBuildTest.NewImporter(c),
    };

    static void Release(ContentCatalog c)
    {
        c?.UnloadAll();
        EditorUtility.UnloadUnusedAssetsImmediate(true);
        GC.Collect();
    }

    static string RelMod(string zipPath)
    {
        var mods = Path.GetFullPath(ModsDir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        var p = Path.GetFullPath(zipPath);
        return (p.StartsWith(mods, StringComparison.OrdinalIgnoreCase) ? p.Substring(mods.Length) : p).Replace('\\', '/');
    }

    static ListEntry Row(ContentCatalog c, int category, int id) => c.GetEntry(category, ContentId.Vanilla(id));

    static string Describe(AssetRedirect.Served s) => s.Describe();

    // ================================================================ Run

    public static void Run()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        bool set0 = ContentCatalog.SideloaderZipmodSet, red0 = ContentCatalog.SideloaderAssetRedirect;
        Rep.AppendLine($"asset redirect tests (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}); switches at start: zipmod set {set0}, redirect {red0}");
        ContentCatalog on = null;
        try
        {
            on = NewCatalog(true, true, out double scanMs);
            Info($"catalog with both switches on: lists {scanMs:F0} ms; {on.NamesPass}");
            foreach (var l in on.Log.Where(l => l.StartsWith("zipmod set", StringComparison.Ordinal) || l.StartsWith("asset redirect", StringComparison.Ordinal))) Info(l);
            if (Want("AR1")) Guard("AR1 the zipmod set", () => AR1(on));
            if (Want("AR2")) Guard("AR2 the redirectable paths of the census", () => AR2(on));
            if (Want("AR10")) Guard("AR10 the fixed abdata paths", () => AR10(on));
            if (Want("AR6")) Guard("AR6 a mod row on a path two zipmods ship", () => AR6(on));
            if (Want("AR8")) Guard("AR8 thumbnails", () => AR8(on));
            if (Want("AR4")) Guard("AR4 the kimono tops", () => AR4(on));
            Release(on);
            if (Want("AR3")) Guard("AR3 the vanilla hair rows", () => AR3(on));
            Release(on);
            if (Want("AR5")) Guard("AR5 the body (RU2)", () => AR5(on));
            Release(on);
            if (Want("AR7")) Guard("AR7 two copies whose files share a name (CAB rule)", () => AR7(on));
            Release(on);
            if (Want("RU1")) Guard("RU1 a shared resource-file name", () => RU1(on));
            Release(on);
            if (Want("AR9")) Guard("AR9 the switches off", () => AR9(on));
            Release(on);
            if (Want("AR11")) Guard("AR11 every name of every redirectable path", () => AR11(on));
            Release(on);
            if (Want("AR12")) Guard("AR12 the CAB rewrite on the census's 5 paths", () => AR12(on));
            Release(on);
            if (Want("RU3")) Guard("RU3 costs", () => RU3(on));
            Release(on);
            Info($"content.cabClash findings of the on catalog: {on.CabClashes.Count}; Unity refusals seen in this process: {ContentCatalog.CabClashMessages}");
            foreach (var cc in on.CabClashes) Info(cc.ToString());
            Info($"content.cabRewrite notes of the on catalog: {on.CabRewrites.Count} ({on.CabRewrites.Count(r => !r.Ok)} failed)");
        }
        finally
        {
            Release(on);
            ContentCatalog.SideloaderZipmodSet = set0;
            ContentCatalog.SideloaderAssetRedirect = red0;
        }
        Finish(ReportFile, "AssetRedirectTest (Sideloader's per-asset redirect: AR1-AR10, RU1-RU3)", sw, "ASSET REDIRECT");
    }

    // ---------------------------------------------------------------- AR1

    static void AR1(ContentCatalog on)
    {
        Check("AR1: zipmod files with a GUID", on.ScannedZipmods.Count(z => z.Guid != null) == FilesWithGuid, $"{on.ScannedZipmods.Count(z => z.Guid != null)} (census {FilesWithGuid})");
        Check("AR1: GUIDs loaded", on.ZipmodsByGuid.Count == GuidsLoaded, $"{on.ZipmodsByGuid.Count} (census {GuidsLoaded})");
        var game = on.ScannedZipmods.Where(z => z.SkippedBy == "game").Select(z => Path.GetFileName(z.Path)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Check("AR1: skipped by the <game> filter = the census's 9", game.SequenceEqual(GameSkipped.OrderBy(x => x, StringComparer.Ordinal)), string.Join(", ", game));
        Check("AR1: every skipped zipmod names AI Girl only", on.ScannedZipmods.Where(z => z.SkippedBy == "game").All(z => z.Games != null && z.Games.Count > 0 && z.Games.All(g => g.Trim().ToLowerInvariant() == "ai girl")));
        Check("AR1: a skipped zipmod adds nothing", on.ScannedZipmods.Where(z => z.SkippedBy == "game").All(z => z.Entries.Count == 0 && z.BundlePaths.Count == 0 && z.ManifestElements.Count == 0 && z.StudioCsvs.Count == 0)
                                                   && !on.ZipmodsByGuid.Values.Any(z => z.SkippedBy != null));
        var older = on.ScannedZipmods.Where(z => z.SkippedBy == "version").ToList();
        Check("AR1: one older file per GUID with several files", older.Count == VersionChoices.Length, string.Join(", ", older.Select(z => z.Guid + " " + RelMod(z.Path))));
        foreach (var (guid, kept) in VersionChoices)
        {
            on.ZipmodsByGuid.TryGetValue(guid, out var z);
            Check($"AR1: {guid} keeps {kept}", z != null && RelMod(z.Path) == kept, z == null ? "not loaded" : RelMod(z.Path) + " " + z.Version);
        }
        var order = on.ZipmodsInLoadOrder.Select(z => z.Guid).ToList();
        Check("AR1: load order = GUIDs in lower-case ordinal order", order.Count == on.ZipmodsByGuid.Count && order.SequenceEqual(order.OrderBy(g => g, StudioCsv.LoadOrder)));
        Check("AR1: the catalog log names the skipped zipmods", on.Log.Any(l => l.StartsWith("zipmod set", StringComparison.Ordinal) && GameSkipped.All(l.Contains)));

        // the lists with the set off: the game filter's rows only (the on catalog's side first, then its bundles unloaded:
        // two catalogs cannot hold the same bundle files open at once)
        var a = Keys(on);
        var rowsOn = new Dictionary<string, ListEntry>(StringComparer.Ordinal);
        foreach (int cat in on.Categories) foreach (var e in on.EntriesOf(cat)) rowsOn[cat.ToString(CultureInfo.InvariantCulture) + " " + e.Key] = e;
        var sOn = on.LoadStudioLists();
        var mapsOn = new HashSet<ContentId>(sOn.Maps.All.Select(m => m.Key));
        int itemsOn = sOn.Items.Count, animeOn = sOn.Anime.Count;
        Release(on);
        using (new Switches(false, false))
        {
            var off = NewCatalog(false, false, out double ms);
            try
            {
                Info($"catalog with both switches off: lists {ms:F0} ms; {off.ZipmodsByGuid.Count} GUIDs");
                var b = Keys(off);
                var onlyOff = b.Except(a).OrderBy(x => x, StringComparer.Ordinal).ToList();
                var onlyOn = a.Except(b).ToList();
                Check("AR1: rows only without the filter = the two saya rows", onlyOff.SequenceEqual(new[] { "240 ddaa33_saya#5122", "243 ddaa33_saya#5123" }), string.Join(", ", onlyOff));
                Check("AR1: no row only with the filter", onlyOn.Count == 0, string.Join(", ", onlyOn.Take(10)));
                int differ = 0;
                foreach (var kv in rowsOn)
                {
                    int sp = kv.Key.IndexOf(' ');
                    int cat = int.Parse(kv.Key.Substring(0, sp), CultureInfo.InvariantCulture);
                    if (off.TryGetEntry(cat, kv.Value.Key, out var e2) && !Same(kv.Value, e2)) differ++;
                }
                Check("AR1: every common row has the same columns", differ == 0, differ + " rows differ");
                var sOff = off.LoadStudioLists();
                var mapsOff = sOff.Maps.All.Where(m => !mapsOn.Contains(m.Key)).Select(m => m.Key.ToString()).ToList();
                Check("AR1: Studio maps only without the filter = the two AiLegend maps", mapsOff.Count == 2 && mapsOff.All(k => k.StartsWith("bestforest.map#", StringComparison.Ordinal) || k.StartsWith("japan.map#", StringComparison.Ordinal)),
                      string.Join(", ", mapsOff) + $"; maps {mapsOn.Count} / {sOff.Maps.Count}, items {itemsOn} / {sOff.Items.Count}, anime {animeOn} / {sOff.Anime.Count}");
                Check("AR1: Studio items and anime unchanged", itemsOn == sOff.Items.Count && animeOn == sOff.Anime.Count);
            }
            finally { Release(off); }
        }
    }

    static HashSet<string> Keys(ContentCatalog c)
    {
        var s = new HashSet<string>(StringComparer.Ordinal);
        foreach (int cat in c.Categories) foreach (var e in c.EntriesOf(cat)) s.Add(cat.ToString(CultureInfo.InvariantCulture) + " " + e.Key);
        return s;
    }

    static bool Same(ListEntry a, ListEntry b) =>
        a.Values.Count == b.Values.Count && a.Values.All(kv => b.Values.TryGetValue(kv.Key, out var v) && v == kv.Value);

    // ---------------------------------------------------------------- AR2

    sealed class CensusCopy { public string Name, Cab, Resource, Externals; public int Names, Serves; }

    static Dictionary<string, List<CensusCopy>> ReadCensus()
    {
        var d = new Dictionary<string, List<CensusCopy>>(StringComparer.Ordinal);
        List<CensusCopy> cur = null;
        bool in101 = false;
        var copy = new Regex(@"^\s+\[(\d+)\] (.+?): CAB (\S+) \(resource files ([^)]*)\); names (\d+); serves (\d+); [^;]+; list rows naming the path [^;]+; externals (.*)$");
        foreach (var line in File.ReadLines(CensusFile, Encoding.UTF8))
        {
            if (line.StartsWith("## 10.1", StringComparison.Ordinal)) { in101 = true; continue; }
            if (line.StartsWith("## 10.2", StringComparison.Ordinal)) break;
            if (!in101) continue;
            if (!line.StartsWith(" ", StringComparison.Ordinal) && line.Contains("\t") && line.Split('\t')[0].EndsWith(".unity3d", StringComparison.OrdinalIgnoreCase))
            {
                d[line.Split('\t')[0]] = cur = new List<CensusCopy>();
                continue;
            }
            var m = copy.Match(line);
            if (m.Success && cur != null)
                cur.Add(new CensusCopy
                {
                    Name = m.Groups[2].Value, Cab = m.Groups[3].Value, Resource = m.Groups[4].Value,
                    Names = int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture), Serves = int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture),
                    Externals = string.Join(",", Regex.Matches(m.Groups[7].Value, @"cab-[0-9a-f]{32}").Cast<Match>().Select(x => x.Value).Distinct().OrderBy(x => x, StringComparer.Ordinal)),
                });
        }
        return d;
    }

    static void AR2(ContentCatalog on)
    {
        var census = ReadCensus();
        var idx = on.Redirect;
        Check("AR2: the census lists 96 paths", census.Count == RedirectablePaths, census.Count.ToString(CultureInfo.InvariantCulture));
        Check("AR2: the catalog's redirectable paths = the census's", idx.RedirectablePaths.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(census.Keys.OrderBy(x => x, StringComparer.Ordinal)),
              $"{idx.RedirectablePaths.Count}: only here {string.Join(", ", idx.RedirectablePaths.Except(census.Keys).Take(5))}; only in the census {string.Join(", ", census.Keys.Except(idx.RedirectablePaths).Take(5))}");
        Check("AR2: 18 of them have an abdata file", idx.RedirectablePaths.Count(idx.HasAbdata) == AbdataPaths, idx.RedirectablePaths.Count(idx.HasAbdata).ToString(CultureInfo.InvariantCulture));
        Info(on.NamesPass?.ToString());
        Check("AR2: every copy's names read (none unreadable; LZMA copies through Unity once, then cached)", on.NamesPass != null && on.NamesPass.Unreadable == 0 && on.NamesPass.Copies >= 204,
              on.NamesPass?.ToString());
        foreach (var p in idx.RedirectablePaths)
            foreach (var z in idx.CopiesOf(p))
            {
                var n0 = z.NamesOf(p);
                if (n0 != null && n0.Compression != "LZ4HC") Info($"copy {p} {RelMod(z.Path)}: data blocks {n0.Compression}, names {n0.Status}");
            }
        int pathsOk = 0, copiesOk = 0, copies = 0;
        var bad = new List<string>();
        var externals = new List<string>();
        foreach (var kv in census.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            string p = kv.Key;
            var x = on.ExplainLoad(null, p, null);
            var mine = x.Copies;
            bool ok = mine.Count == kv.Value.Count;
            // served counts per copy: every name of every copy, served by the rule
            var names = new HashSet<string>(StringComparer.Ordinal);
            string abFile = on.AbdataFileOf(p);
            BundleNames ab = abFile != null ? BundleNames.ReadFile(abFile, 0, new FileInfo(abFile).Length) : null;
            if (ab?.Names != null) names.UnionWith(ab.Names);
            foreach (var z in idx.CopiesOf(p)) { var n = idx.NamesOf(z, p); if (n?.Names != null) names.UnionWith(n.Names); }
            var serves = new int[mine.Count];
            foreach (var n in names)
            {
                var s = idx.Serve(p, n);
                int i = s.Abdata ? 0 : s.Zip == null ? -1 : (abFile != null ? 1 : 0) + idx.CopiesOf(p).ToList().IndexOf(s.Zip);
                if (i >= 0 && i < serves.Length && (s.HasName || s.Abdata && ab != null && ab.Contains(n))) serves[i]++;
            }
            for (int i = 0; i < Math.Min(mine.Count, kv.Value.Count); i++)
            {
                copies++;
                var c = kv.Value[i]; var m = mine[i];
                string mname = m.Guid == null ? "abdata" : Path.GetFileName(m.File);
                var bn = m.Guid == null ? ab : idx.NamesOf(idx.CopiesOf(p)[i - (abFile != null ? 1 : 0)], p);
                string ext = string.Join(",", (bn?.Externals ?? "").Split(',').Where(e => e.StartsWith("cab-", StringComparison.Ordinal)).Distinct().OrderBy(e => e, StringComparer.Ordinal));
                string res = string.IsNullOrEmpty(bn?.ResourceFiles) ? "none" : bn.ResourceFiles.Replace(",", ", ");
                bool cok = mname == c.Name && bn?.Count == c.Names && serves[i] == c.Serves && bn?.SerializedFiles == c.Cab && res == c.Resource && ext == c.Externals;
                if (cok) copiesOk++;
                else
                {
                    ok = false;
                    if (bad.Count < 12) bad.Add($"{p} [{i}] {mname}/{c.Name}: names {bn?.Count}/{c.Names}, serves {serves[i]}/{c.Serves}, cab {bn?.SerializedFiles}/{c.Cab}, res {res}/{c.Resource}, ext {ext}/{c.Externals}");
                }
                if (m.Guid != null && ext.Length > 0) externals.Add($"{p} {mname}: {ext}");
            }
            if (ok) pathsOk++;
        }
        foreach (var b in bad) Info("differs: " + b);
        Check("AR2: every copy has its names after the rule asked (LZMA copies read through Unity)", idx.RedirectablePaths.All(p => idx.CopiesOf(p).All(z => idx.NamesOf(z, p)?.Status == BundleNames.Kind.Ok)));
        Check("AR2: per path the copies in load order, their names, served names, file names and external references = the census", pathsOk == census.Count && copiesOk == copies,
              $"{pathsOk} of {census.Count} paths, {copiesOk} of {copies} copies");
        Info($"zipmod copies with external references: {externals.Count}");
        foreach (var e in externals.Take(20)) Info("externals " + e);
    }

    // ---------------------------------------------------------------- AR10

    static void AR10(ContentCatalog on)
    {
        foreach (var (asset, kind) in new[] { ("p_cf_anim", "abdata"), ("p_cf_head_bone", "abdata"), ("p_cf_body_00_Nml", "abdata"), ("p_cf_body_00", "zipmod") })
        {
            var x = on.ExplainLoad(null, Body, asset);
            Check($"AR10: {Body} {asset} served by {kind}", x.Redirectable && x.ServedKind == kind && (kind == "abdata" || x.ServedGuid == "HS2_UncensorSelector.Base"),
                  $"{x.ServedKind} {x.ServedGuid}; today {x.TodayKind}");
        }
        var so = on.LoadAsset<ScriptableObject>(Body, "p_cf_body_00_Nml");
        var anim = on.LoadAsset<GameObject>(Body, "p_cf_anim");
        var hb = on.LoadAsset<GameObject>(Body, "p_cf_head_bone");
        var body = on.LoadAsset<GameObject>(Body, "p_cf_body_00");
        Check("AR10: the four assets load", so && anim && hb && body);
        Check("AR10: the body copy carries the cm_o_dan_f renderer", body && body.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r => r.name == "cm_o_dan_f"));
        Check("AR10: the redirect of the body is counted", on.Redirect.Stats.TryGetValue(Body, out int n) && n >= 1, n.ToString(CultureInfo.InvariantCulture));
        Check("AR10: a redirect line in the catalog log", on.Log.Any(l => l.StartsWith("redirect: " + Body + " p_cf_body_00", StringComparison.Ordinal)));
    }

    // ---------------------------------------------------------------- AR6

    static void AR6(ContentCatalog on)
    {
        // a mod row whose asset another zipmod's copy serves (prefer the cuzkuz top of the census)
        ListEntry pick = null; string col = null, path = null;
        foreach (int cat in on.Categories)
        {
            foreach (var e in on.EntriesOf(cat))
            {
                if (e.Zipmod == null || string.IsNullOrEmpty(e["MainAB"]) || !on.Redirect.IsRedirectable(e["MainAB"]) || on.Redirect.HasAbdata(e["MainAB"])) continue;
                foreach (var c in new[] { "MainData", "MainTex" })
                {
                    if (string.IsNullOrEmpty(e[c]) || e[c] == "0") continue;
                    var x = on.ExplainLoad(e, e["MainAB"], e[c]);
                    if (x.ServedKind == "zipmod" && x.TodayKind == "zipmod" && x.ServedGuid != x.TodayGuid && (pick == null || e["MainAB"].StartsWith("chara/cuzkuz/cuzkuz_top_05", StringComparison.Ordinal)))
                    { pick = e; col = c; path = e["MainAB"]; }
                }
            }
            if (pick != null && path.StartsWith("chara/cuzkuz/cuzkuz_top_05", StringComparison.Ordinal)) break;
        }
        if (pick == null) { Check("AR6: a mod row served by another zipmod's copy exists", false); return; }
        var ex = on.ExplainLoad(pick, path, pick[col]);
        Info($"row {pick.Category} {pick.Key} {path} {pick[col]}: served {ex.ServedGuid}, today {ex.TodayGuid}");
        Check("AR6: the first copy in load order serves", ReferenceEquals(on.Redirect.Serve(path, pick[col]).Zip, on.Redirect.CopiesOf(path).First(z => z.NamesOf(on.Redirect.Exact(path))?.Contains(pick[col]) == true)));
        int before = on.Redirect.Stats.TryGetValue(on.Redirect.Exact(path), out var n0) ? n0 : 0;
        var obj = on.LoadAsset<Object>(pick, "MainAB", col);
        Check("AR6: the asset loads from the serving copy", obj, obj ? obj.GetType().Name + " " + obj.name : "null");
        Check("AR6: the redirect is counted", (on.Redirect.Stats.TryGetValue(on.Redirect.Exact(path), out var n1) ? n1 : 0) == before + 1);
    }

    // ---------------------------------------------------------------- AR8

    static void AR8(ContentCatalog on)
    {
        ListEntry pick = null; string col = null;
        foreach (int cat in on.Categories)
        {
            foreach (var e in on.EntriesOf(cat))
            {
                string ab = e["ThumbAB"], tex = e["ThumbTex"];
                if (string.IsNullOrEmpty(ab) || string.IsNullOrEmpty(tex) || tex == "0" || !on.Redirect.IsRedirectable(ab)) continue;
                var x = on.ExplainLoad(e, ab, tex);
                if (x.ServedFile != null && x.ServedFile != x.TodayFile) { pick = e; col = "ThumbTex"; break; }
            }
            if (pick != null) break;
        }
        if (pick == null) { Check("AR8: a row whose thumbnail another copy serves exists", false); return; }
        string path = pick["ThumbAB"], asset = pick[col];
        var exp = on.ExplainLoad(pick, path, asset);
        Info($"row {pick.Category} {pick.Key}: {path} {asset}: served {exp.ServedGuid ?? exp.ServedKind}, today {exp.TodayGuid ?? exp.TodayKind}");
        string stamp = on.SourceStampOf(pick, path, asset), old = on.SourceStampOf(pick, path);
        Check("AR8: SourceStampOf(e, path, asset) names the serving file", stamp != null && stamp.StartsWith(exp.ServedFile + "|", StringComparison.OrdinalIgnoreCase), stamp);
        Check("AR8: the 4.0 overload keeps today's file", old != null && old.StartsWith(exp.TodayFile + "|", StringComparison.OrdinalIgnoreCase), old);
        Release(on);
        var open = on.OpenBundleAsync(pick, path, asset);
        on.CompletePendingOpens();
        Check("AR8: OpenBundleAsync(e, path, asset) opened a bundle", open != null && open.IsDone && open.Bundle, open?.Bundle ? "ok" : "none");
        var served = on.Redirect.Serve(path, asset).Zip as ZipmodInfo;
        var same = served != null ? served.OpenBundle(path, on) : null;
        Check("AR8: it is the serving copy", open?.Bundle && ReferenceEquals(open.Bundle, same));
        Release(on);
    }

    // ---------------------------------------------------------------- AR4

    static void AR4(ContentCatalog on)
    {
        var flags = new Dictionary<int, (int on, int off)>();
        foreach (int id in KimonoTops)
        {
            var e = Row(on, 240, id);
            var p = e != null ? on.LoadAsset<GameObject>(e, "MainAB", "MainData") : null;
            var cmp = p ? p.GetComponent<AIChara.CmpClothes>() : null;
            flags[id] = (cmp != null ? cmp.useBreak : -1, -1);
            var x = e != null ? on.ExplainLoad(e, e["MainAB"], e["MainData"]) : null;
            Check($"AR4: 240#{id} served by Terable Kimonos", x != null && x.ServedKind == "zipmod" && x.TodayKind == "abdata", x?.ToString());
        }
        Release(on);
        using (new Switches(true, false))
            foreach (int id in KimonoTops)
            {
                var e = Row(on, 240, id);
                var p = e != null ? on.LoadAsset<GameObject>(e, "MainAB", "MainData") : null;
                var cmp = p ? p.GetComponent<AIChara.CmpClothes>() : null;
                flags[id] = (flags[id].on, cmp != null ? cmp.useBreak : -1);
            }
        Release(on);
        foreach (var kv in flags) Check($"AR4: 240#{kv.Key} useBreak 1 with the redirect, 0 without", kv.Value.on == 1 && kv.Value.off == 0, $"{kv.Value.on} / {kv.Value.off}");

        // a breakRate on the redirected kimono writes _AlphaEx (BR-Chan wearing 240#223)
        var h = HarnessOf(on);
        using (var d = new Disposer(h.Build(FemaleCard)))
        {
            var live = d.B;
            if (!Check("AR4: BR-Chan built", live.Ok, live.Error)) return;
            var top = live.Context.Coord.Clothes.Parts[0];
            Set(live, "coord.clothes.top", () => { top.Item = "illusion.hs2#223"; top.BreakRate = 0.5f; });
            var go = live.Context.Clothes[0];
            var cmp = go ? go.GetComponent<AIChara.CmpClothes>() : null;
            var r = cmp?.rendNormal01?.FirstOrDefault(x => x);
            var m = r ? r.sharedMaterials.FirstOrDefault() : null;
            float a = m && m.HasProperty("_AlphaEx") ? m.GetFloat("_AlphaEx") : float.NaN;
            Check("AR4: 240#223 worn with breakRate 0.5: _AlphaEx 0.5 on the first slot", cmp != null && cmp.useBreak == 1 && Mathf.Abs(a - 0.5f) < 1e-6f, $"{go?.name} useBreak {cmp?.useBreak} _AlphaEx {a}");
        }
        Release(on);
    }

    // ---------------------------------------------------------------- AR3

    static Dictionary<string, (int useMesh, string onOff, string served)> HairFacts(ContentCatalog c)
    {
        var d = new Dictionary<string, (int, string, string)>(StringComparer.Ordinal);
        foreach (int cat in new[] { 300, 301, 302, 303 })
            foreach (var e in c.EntriesOf(cat))
            {
                if (!e.IsVanilla || string.IsNullOrEmpty(e["MainAB"]) || !c.Redirect.IsRedirectable(e["MainAB"]) || !c.Redirect.HasAbdata(e["MainAB"])) continue;
                foreach (var col in new[] { "MainData", "MainData02" })
                {
                    string asset = e[col];
                    if (string.IsNullOrEmpty(asset) || asset == "0") continue;
                    var x = c.ExplainLoad(e, e["MainAB"], asset);
                    var p = c.LoadAsset<GameObject>(e, "MainAB", col);
                    var cmp = p ? p.GetComponent<AIChara.CmpHair>() : null;
                    var vals = cmp?.rendHair == null ? "-" : string.Join(",", cmp.rendHair.Where(r => r).SelectMany(r => r.sharedMaterials)
                        .Select(m => m && m.HasProperty("_Mesh_onoff") ? m.GetFloat("_Mesh_onoff").ToString("R", CultureInfo.InvariantCulture) : "-"));
                    d[cat + "#" + e.Key.Id + "." + col] = (cmp != null ? cmp.useMesh : -1, vals, x.ServedKind + ":" + x.ServedGuid);
                }
            }
        return d;
    }

    static void AR3(ContentCatalog on)
    {
        var a = HairFacts(on);
        Release(on);
        Dictionary<string, (int useMesh, string onOff, string served)> b;
        using (new Switches(true, false)) { b = HairFacts(on); }
        Release(on);
        int back = a.Keys.Count(k => k.StartsWith("300#", StringComparison.Ordinal) && k.EndsWith(".MainData", StringComparison.Ordinal));
        int side = a.Keys.Count(k => k.StartsWith("302#", StringComparison.Ordinal) && k.EndsWith(".MainData", StringComparison.Ordinal));
        Info($"vanilla hair prefabs on redirected paths: {a.Count} (back rows {back}, side rows {side}; male variants {a.Keys.Count(k => k.EndsWith(".MainData02", StringComparison.Ordinal))})");
        Check("AR3: 49 back rows and 7 side rows on the hair paths (census)", back == 49 && side == 7, $"{back} / {side}");
        Check("AR3: every one served by a HairMod copy", a.Values.All(v => v.served.StartsWith("zipmod:", StringComparison.Ordinal)), string.Join(", ", a.Where(kv => !kv.Value.served.StartsWith("zipmod:", StringComparison.Ordinal)).Select(kv => kv.Key).Take(5)));
        Check("AR3: useMesh 1 on every served prefab", a.Values.All(v => v.useMesh == 1), string.Join(", ", a.Where(kv => kv.Value.useMesh != 1).Select(kv => kv.Key + "=" + kv.Value.useMesh).Take(5)));
        Check("AR3: useMesh 0 on every abdata prefab (redirect off)", b.Values.All(v => v.useMesh == 0 && v.served.StartsWith("abdata", StringComparison.Ordinal)),
              string.Join(", ", b.Where(kv => kv.Value.useMesh != 0).Select(kv => kv.Key + "=" + kv.Value.useMesh).Take(5)));
        var diff = a.Where(kv => b.TryGetValue(kv.Key, out var o) && o.onOff != kv.Value.onOff).Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var rows = diff.Select(k => int.Parse(k.Substring(4, k.IndexOf('.') - 4), CultureInfo.InvariantCulture)).Distinct().OrderBy(x => x).ToList();
        foreach (var k in diff) Info($"_Mesh_onoff {k}: abdata [{b[k].onOff}] -> zipmod [{a[k].onOff}]");
        Check("AR3: the rows whose _Mesh_onoff differs = 4.3's measurement", diff.All(k => k.StartsWith("300#", StringComparison.Ordinal)) && rows.SequenceEqual(MeshOnOffRows),
              $"{diff.Count} prefabs on rows {string.Join(",", rows)}");
    }

    // ---------------------------------------------------------------- AR5 (RU2)

    static void StripBlock(StringBuilder into, string text, string rendererMark)
    {
        bool skip = false;
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith("renderer ", StringComparison.Ordinal)) skip = line.Contains(rendererMark);
            if (!skip) into.Append(line).Append('\n');
        }
    }

    sealed class BodyFacts
    {
        public string Materials, MaterialsWithoutDan, Creators, Bones, Normals, Bust, Rts, Dan;
        public bool HasDan, DanInactive, BonesBound, Slot0Draw, NoBonesKept;
        public int DanBones;
    }

    static BodyFacts BuildBody(ContentCatalog c, out string error)
    {
        error = null;
        var h = HarnessOf(c);
        using (var d = new Disposer(h.Build(FemaleCard)))
        {
            var b = d.B;
            if (!b.Ok) { error = b.Error; return null; }
            var ctx = b.Context;
            var f = new BodyFacts();
            var dan = ctx.Body ? ctx.Body.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "cm_o_dan_f") : null;
            var smr = dan ? dan.GetComponent<SkinnedMeshRenderer>() : null;
            f.HasDan = smr;
            var nDan = dan ? dan.parent : null;
            while (nDan && nDan.name != "N_dan") nDan = nDan.parent;
            f.DanInactive = nDan && !nDan.gameObject.activeSelf;
            f.DanBones = smr ? smr.bones.Length : 0;
            var skeleton = new HashSet<Transform>(ctx.BodyBones.Values.Where(t => t));
            f.BonesBound = smr && smr.bones.Length == 3 && smr.bones.All(t => t && (skeleton.Contains(t) || t.IsChildOf(ctx.Anim.transform)));
            f.Dan = smr ? $"{smr.sharedMesh?.name} {smr.sharedMesh?.vertexCount} vertices, bones {string.Join(",", smr.bones.Select(t => t ? t.name : "null"))}, material {smr.sharedMaterial?.name} / {smr.sharedMaterial?.shader?.name}" : "none";
            var bodyRend = ctx.Body.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name == "o_body_cf");
            f.Slot0Draw = bodyRend && bodyRend.sharedMaterials.Length > 0 && bodyRend.sharedMaterials[0] == ctx.BodyDraw;
            f.NoBonesKept = !(ctx.Log ?? new List<string>()).Any(l => l.StartsWith("p_cf_body_00:", StringComparison.Ordinal) && l.Contains("bones kept"));
            f.Materials = FaceDigest.MaterialsText(ctx, "body");
            var sb = new StringBuilder();
            StripBlock(sb, f.Materials, "/cm_o_dan_f#");
            f.MaterialsWithoutDan = sb.ToString();
            f.Creators = FaceDigest.CreatorsText(ctx, "body");
            f.Bones = FaceDigest.BonesText(ctx, "body");
            f.Normals = string.Join("\n", FaceDigest.NormalsText(ctx, "body").Split('\n').Where(l => !l.Contains("/cm_o_dan_f#")));
            f.Bust = FaceDigest.BustText(ctx, "body");
            f.Rts = FaceDigest.RtsText(ctx, "body");
            return f;
        }
    }

    static void AR5(ContentCatalog on)
    {
        var a = BuildBody(on, out string ea);
        Release(on);
        BodyFacts b;
        string eb;
        using (new Switches(true, false)) { b = BuildBody(on, out eb); }
        Release(on);
        if (!Check("AR5: BR-Chan built with the redirect on and off", a != null && b != null, ea ?? eb)) return;
        Info("cm_o_dan_f (redirect on): " + a.Dan);
        Check("AR5: cm_o_dan_f carries a SkinnedMeshRenderer with the redirect (none without)", a.HasDan && !b.HasDan);
        Check("AR5: under an inactive N_dan", a.DanInactive && b.DanInactive);
        Check("AR5: its 3 bones bound to the skeleton", a.BonesBound, a.DanBones + " bones");
        Check("AR5: o_body_cf slot 0 = the draw material", a.Slot0Draw && b.Slot0Draw);
        Check("AR5 / RU2: no \"bones kept\" for p_cf_body_00 (Rebind log)", a.NoBonesKept && b.NoBonesKept);
        Check("AR5: body digest materials equal except the cm_o_dan_f block", a.MaterialsWithoutDan == b.MaterialsWithoutDan, Diff(a.MaterialsWithoutDan, b.MaterialsWithoutDan));
        Check("AR5: the materials section differs (the new renderer)", a.Materials != b.Materials);
        Check("AR5: body digest creators, bones, bust equal; normals equal except the cm_o_dan_f line", a.Creators == b.Creators && a.Bones == b.Bones && a.Normals == b.Normals && a.Bust == b.Bust,
              $"creators {a.Creators == b.Creators}, bones {a.Bones == b.Bones}, normals {a.Normals == b.Normals} {Diff(a.Normals, b.Normals)}, bust {a.Bust == b.Bust}");
        if (a.Rts == null || b.Rts == null) Info("composite RT hashes: no graphics device (checked by the graphics rows)");
        else Check("AR5 / RU2: the body composites' RT hashes equal", a.Rts == b.Rts, Diff(a.Rts, b.Rts));
    }

    static string Diff(string a, string b)
    {
        if (a == b) return "";
        var la = a.Split('\n'); var lb = b.Split('\n');
        for (int i = 0; i < Math.Min(la.Length, lb.Length); i++) if (la[i] != lb[i]) return $"line {i}: {Short(la[i], 200)} <> {Short(lb[i], 200)}";
        return $"lengths {la.Length} / {lb.Length}";
    }

    // ---------------------------------------------------------------- AR7 (CAB rule)

    // a row of the path whose named column the given copy serves
    static (ListEntry row, string col, string asset) RowServedBy(ContentCatalog c, string path, IRedirectZip copy)
    {
        foreach (int cat in c.Categories)
            foreach (var e in c.EntriesOf(cat))
                foreach (var kv in e.Values)
                {
                    if (!string.Equals(kv.Value, path, StringComparison.Ordinal)) continue;
                    string abKey = kv.Key;
                    foreach (var col in e.Values.Keys)
                    {
                        if (col == abKey || string.IsNullOrEmpty(e[col]) || e[col] == "0") continue;
                        if (!(abKey.EndsWith("AB", StringComparison.Ordinal))) continue;
                        var s = c.Redirect.Serve(path, e[col]);
                        if (s.HasName && ReferenceEquals(s.Zip, copy)) return (e, abKey + "|" + col, e[col]);
                    }
                }
        return (null, null, null);
    }

    static void AR7(ContentCatalog on)
    {
        var copies = on.Redirect.CopiesOf(Lashes);
        if (!Check("AR7: two copies of " + Lashes, copies.Count == 2, copies.Count.ToString(CultureInfo.InvariantCulture))) return;
        var a = RowServedBy(on, Lashes, copies[0]);
        var b = RowServedBy(on, Lashes, copies[1]);
        if (!Check("AR7: a row served by each copy", a.row != null && b.row != null)) return;
        Info($"copy A {RelMod(copies[0].Path)}: {a.row.Category} {a.row.Key} {a.col}; copy B {RelMod(copies[1].Path)}: {b.row.Category} {b.row.Key} {b.col}");
        string abA = a.col.Split('|')[0], colA = a.col.Split('|')[1], abB = b.col.Split('|')[0], colB = b.col.Split('|')[1];

        // (b): copy A handed out (a synchronous load), then a name only copy B holds: B from memory with its names renamed
        Release(on);
        int fb = on.CabClashes.Count, rb = on.CabRewrites.Count, mb = ContentCatalog.CabClashMessages;
        var errors = new List<string>();
        Application.LogCallback onLog = (msg, stack, type) =>
        {
            if ((type == LogType.Error || type == LogType.Exception) && msg != null && msg.IndexOf("another AssetBundle with the same files", StringComparison.Ordinal) < 0) errors.Add(msg);
        };
        Object ob = null;
        Exception ex = null;
        Object oa = null;
        Application.logMessageReceived += onLog;
        try
        {
            oa = on.LoadAsset<Object>(a.row, abA, colA);
            ob = on.LoadAsset<Object>(b.row, abB, colB);
        }
        catch (Exception e) { ex = e; }
        finally { Application.logMessageReceived -= onLog; }
        var rw = on.CabRewrites.Skip(rb).ToList();
        Info($"(b): Unity refusals {ContentCatalog.CabClashMessages - mb}; {string.Join(" | ", rw)}; copy B's asset {(ob ? ob.GetType().Name + " " + ob.name + (ob is Texture tb ? $" {tb.width}x{tb.height}" : "") : "null")}");
        Check("AR7 (b): no exception", ex == null, ex?.Message);
        Check("AR7 (b): Unity refused copy B and the catalog loaded it from memory with its file names renamed (content.cabRewrite), no content.cabClash",
              ContentCatalog.CabClashMessages - mb >= 1 && rw.Count == 1 && rw[0].Ok && rw[0].Key.StartsWith(copies[1].Path, StringComparison.OrdinalIgnoreCase) && on.CabClashes.Count == fb,
              string.Join(" | ", rw));
        Check("AR7 (b): copy B's asset loads (from the rewritten copy)", ob, ob ? ob.name : "null");
        Check("AR7 (b): copy A's asset still alive and served again from copy A", oa && on.LoadAsset<Object>(a.row, abA, colA) == oa);
        Check("AR7 (b): no other error while loading from the rewritten copy (its references to its own resource files)", errors.Count == 0, string.Join(" | ", errors.Take(3)));

        // (c): with the rewrite off: copy A handed out, then a name only copy B holds
        Release(on);
        bool rewrite0 = ContentCatalog.CabRewrite;
        ContentCatalog.CabRewrite = false;
        int f0 = on.CabClashes.Count, m0 = ContentCatalog.CabClashMessages;
        oa = null; ob = null; ex = null;
        try
        {
            oa = on.LoadAsset<Object>(a.row, abA, colA);
            ob = on.LoadAsset<Object>(b.row, abB, colB);
        }
        catch (Exception e) { ex = e; }
        finally { ContentCatalog.CabRewrite = rewrite0; }
        var fc = on.CabClashes.Skip(f0).ToList();
        Info($"(c): Unity refusals {ContentCatalog.CabClashMessages - m0}; findings: {string.Join(" | ", fc)}");
        Check("AR7 (c): no exception", ex == null, ex?.Message);
        Check("AR7 (c): copy A's asset loaded", oa);
        Check("AR7 (c): Unity refused copy B (the hook saw it) and the finding content.cabClash names both keys",
              ContentCatalog.CabClashMessages - m0 >= 1 && fc.Count == 1 && fc[0].OtherKey != null && fc[0].OtherKey.StartsWith(copies[0].Path, StringComparison.OrdinalIgnoreCase)
              && fc[0].Key.StartsWith(copies[1].Path, StringComparison.OrdinalIgnoreCase) && fc[0].Resolution.Contains("served from the open copy"));
        Check("AR7 (c): the request was served from the open copy A (which lacks the name)", !ob, ob ? ob.name : "null");

        // (a): copy A thumbnail-only (an asynchronous request), then copy B's name synchronously
        Release(on);
        int f1 = on.CabClashes.Count;
        var req = on.LoadAssetAsync<Object>(a.row, abA, colA, true, out var bundleA);
        Check("AR7 (a): copy A opened thumbnail-only", bundleA && on.IsThumbnailOnly(bundleA));
        if (req != null) { var dummy = req.asset; }
        ex = null;
        try { ob = on.LoadAsset<Object>(b.row, abB, colB); }
        catch (Exception e) { ex = e; }
        var fa = on.CabClashes.Skip(f1).ToList();
        Info($"(a): findings: {string.Join(" | ", fa)}");
        Check("AR7 (a): no exception", ex == null, ex?.Message);
        Check("AR7 (a): the unused copy A unloaded and copy B opened: its asset loads", ob && fa.Count == 1 && fa[0].Resolution.Contains("unloaded"), ob ? ob.name : "null");
        Release(on);
    }

    // ---------------------------------------------------------------- RU1

    static void RU1(ContentCatalog on)
    {
        // the abdata file and HairMod's copy of chara/38/so_hair_b_38 share only a resource-file name (Step 0 census)
        var copies = on.Redirect.CopiesOf(Hair38);
        var ab = on.AbdataFileOf(Hair38);
        var zn = copies.Count > 0 ? on.Redirect.NamesOf(copies[0], Hair38) : null;
        var an = ab != null ? BundleNames.ReadFile(ab, 0, new FileInfo(ab).Length) : null;
        Info($"{Hair38}: abdata {an?.SerializedFiles} / {an?.ResourceFiles}; {copies.FirstOrDefault()?.Guid} {zn?.SerializedFiles} / {zn?.ResourceFiles}");
        ListEntry row = on.EntriesOf(300).FirstOrDefault(e => e.IsVanilla && e["MainAB"] == Hair38);
        if (!Check("RU1: a vanilla row on " + Hair38, row != null)) return;
        Release(on);
        int m0 = ContentCatalog.CabClashMessages, f0 = on.CabClashes.Count;
        var abBundle = on.LoadAbdataBundle(Hair38);
        var abPrefab = abBundle ? abBundle.LoadAsset<GameObject>(row["MainData"]) : null;   // the abdata copy open and in use
        var t = Stopwatch.StartNew();
        var redirected = on.LoadAsset<GameObject>(row, "MainAB", "MainData");
        double ms = t.Elapsed.TotalMilliseconds;
        int refusals = ContentCatalog.CabClashMessages - m0;
        var cmp = redirected ? redirected.GetComponent<AIChara.CmpHair>() : null;
        Info($"RU1: abdata copy open ({(abPrefab ? "prefab loaded" : "no prefab")}); the rule's load of {row["MainData"]}: {(redirected ? "loaded" : "null")} in {ms:F1} ms, " +
             $"useMesh {cmp?.useMesh}, Unity refusals {refusals}, findings {string.Join(" | ", on.CabClashes.Skip(f0))}");
        Check("RU1: the abdata copy open and in use, the rule's load answered without an exception", abPrefab && (redirected || refusals > 0));
        Info(refusals == 0 && cmp != null && cmp.useMesh == 1
            ? "RU1 answer: a shared resource-file name alone does not clash (both copies open at once; HairMod's prefab served)"
            : $"RU1 answer: Unity refused HairMod's copy while the abdata copy was open ({refusals} refusals): a shared resource-file name clashes; the catalog's rule served {(cmp != null ? "useMesh " + cmp.useMesh : "nothing")}");
        Release(on);
    }

    // ---------------------------------------------------------------- AR9

    static void AR9(ContentCatalog on)
    {
        // the redirect off, the same catalog after every bundle is unloaded: today's sources
        using (new Switches(true, false))
        {
            int before = on.Redirect.Stats.Values.Sum();
            var e = Row(on, 300, 11);
            var p = e != null ? on.LoadAsset<GameObject>(e, "MainAB", "MainData") : null;
            var x = e != null ? on.ExplainLoad(e, e["MainAB"], e["MainData"]) : null;
            Check("AR9: redirect off: back #11 from the abdata file (useMesh 0)", p && p.GetComponent<AIChara.CmpHair>()?.useMesh == 0 && x.ServedKind == "abdata", x?.ToString());
            var body = on.LoadAsset<GameObject>(Body, "p_cf_body_00");
            Check("AR9: redirect off: the abdata body (cm_o_dan_f without a renderer)", body && !body.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r => r.name == "cm_o_dan_f"));
            Check("AR9: redirect off: nothing counted", on.Redirect.Stats.Values.Sum() == before);
            Check("AR9: redirect off: SourceStampOf(e, path, asset) = the 4.0 overload", e == null || on.SourceStampOf(e, e["ThumbAB"], e["ThumbTex"]) == on.SourceStampOf(e, e["ThumbAB"]));
        }
        Release(on);
        // the zipmod set off: a separate catalog, every bundle of the first unloaded
        using (new Switches(false, false))
        {
            var off = NewCatalog(false, false, out double ms);
            try
            {
                Check("AR9: set off: no zipmod skipped", off.ScannedZipmods.All(z => z.SkippedBy == null));
                Check("AR9: set off: the AI zipmods and saya load (4.3's set)", off.ZipmodsByGuid.ContainsKey("AI_UncensorSelector.Base") && off.ZipmodsByGuid.ContainsKey("ddaa33_saya")
                      && off.GetEntry(240, new ContentId("ddaa33_saya", 5122)) != null);
                // 4.3: the last scanned file per GUID
                var last = off.ScannedZipmods.Where(z => z.Guid != null).GroupBy(z => z.Guid, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
                Check("AR9: set off: the last scanned file of every GUID", off.ZipmodsByGuid.All(kv => ReferenceEquals(kv.Value, last[kv.Key])));
                Check("AR9: set off: the earlier body copies (AI_UncensorSelector Base first)", off.Redirect.CopiesOf(Body).Count == 2 && off.Redirect.CopiesOf(Body)[0].Guid == "AI_UncensorSelector.Base");
            }
            finally { Release(off); }
        }
    }

    // ---------------------------------------------------------------- AR11

    // the census's paths whose serving copies share a serialized-file name (Step 0, section 10.1 "needs (b)")
    static readonly string[] CensusClashPaths =
    {
        "K1T0-K1TN/K1T0K1TN_eyelashes.unity3d", "K1T0-K1TN/K1T0K1TN_eyelashes2_thumbs.unity3d", "K1T0-K1TN/K1T0K1TN_eyelashes_thumbs.unity3d",
        "chara/00/Darkruler_JulietStarling/thumbs.unity3d", "chara/00/Darkruler_Yuffie/textures.unity3d",
    };

    static void AR11(ContentCatalog on)
    {
        // every asset of these paths includes textures no card loads, some with a mip chain Unity cannot upload: with a
        // graphics device such a texture removes the device (seen once); the sweep runs headless only
        if (FaceDigest.HasDevice) { Info("AR11 skipped: headless only (a run without -nographics lost the D3D12 device on a bad-mip texture)"); return; }
        var idx = on.Redirect;
        var clashPaths = new List<string>();
        int loads = 0, nulls = 0, findings0 = on.CabClashes.Count, rewrites0 = on.CabRewrites.Count;
        var nullPaths = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var t = Stopwatch.StartNew();
        foreach (var p in idx.RedirectablePaths)
        {
            if (p == "abdata.unity3d") continue;   // the shader-bundle name 27 map / shader zipmods ship (no list row loads it)
            Release(on);
            int m0 = ContentCatalog.CabClashMessages;
            foreach (var n in idx.AllNames(p))
            {
                loads++;
                Object o = null;
                try { o = on.LoadAsset<Object>(p, n); }
                catch (Exception e) { Check($"AR11: {p} {n}: no exception", false, e.GetType().Name + ": " + e.Message); }
                if (!o) { nulls++; nullPaths[p] = nullPaths.TryGetValue(p, out var k) ? k + 1 : 1; }
            }
            if (ContentCatalog.CabClashMessages > m0) clashPaths.Add(p);
        }
        Release(on);
        var findings = on.CabClashes.Skip(findings0).ToList();
        var rewrites = on.CabRewrites.Skip(rewrites0).ToList();
        Info($"AR11: {loads} loads of {idx.RedirectablePaths.Count - 1} paths in {t.Elapsed.TotalSeconds:F0} s; {nulls} answered nothing: {string.Join(", ", nullPaths.Select(kv => kv.Key + " " + kv.Value))}");
        Info("AR11: paths where Unity refused a copy: " + string.Join(", ", clashPaths));
        Check("AR11: the paths with a refused copy = the census's 5 (serving copies with one serialized-file name)",
              clashPaths.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(CensusClashPaths.OrderBy(x => x, StringComparer.Ordinal)), string.Join(", ", clashPaths));
        Info($"AR11: rewrites {rewrites.Count} ({rewrites.Count(r => !r.Ok)} failed), {rewrites.Sum(r => r.Ms):F0} ms, {rewrites.Sum(r => r.Bytes) / 1048576.0:F1} MiB loaded from memory");
        Check("AR11: every refusal rewritten: no content.cabClash in the sweep (package R2)", findings.Count == 0 && rewrites.Count > 0 && rewrites.All(r => r.Ok),
              $"findings {findings.Count}: {string.Join(" | ", findings.Take(3))}; failed rewrites: {string.Join(" | ", rewrites.Where(r => !r.Ok).Take(3))}");
        Check("AR11: every name of the refused paths loads", clashPaths.All(p => !nullPaths.ContainsKey(p)),
              string.Join(", ", clashPaths.Where(nullPaths.ContainsKey).Select(p => p + " " + nullPaths[p])));
        // a zipmod "bundle" that does not open (a list CSV the zipmod ships) is logged
        var z = on.ZipmodsInLoadOrder.FirstOrDefault(x => x.BundlePaths.Any(b => b.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)));
        if (z != null)
        {
            string csv = z.BundlePaths.First(b => b.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
            var b0 = on.LoadBundle(new ListEntry { Zipmod = z }, csv);
            Check("AR11: a zipmod bundle that does not open is logged", !b0 && on.Log.Any(l => l.StartsWith("zipmod bundle did not open: ", StringComparison.Ordinal) && l.EndsWith("!" + csv, StringComparison.Ordinal)), csv);
        }
        Release(on);
    }

    // ---------------------------------------------------------------- AR12 (the CAB rewrite, package R2)

    static void AR12(ContentCatalog on)
    {
        if (FaceDigest.HasDevice) { Info("AR12 skipped: headless only (the copies hold textures no card loads)"); return; }
        var idx = on.Redirect;
        var ms = new List<double>();
        long bytes = 0;
        foreach (var p in CensusClashPaths)
        {
            Release(on);
            int r0 = on.CabRewrites.Count, f0 = on.CabClashes.Count;
            // per copy the names the rule serves from it, every copy opened in load order through its first name
            var byCopy = idx.CopiesOf(p).Select(c => (copy: c, names: idx.AllNames(p).Where(n => idx.Serve(p, n).HasName && ReferenceEquals(idx.Serve(p, n).Zip, c)).ToList()))
                            .Where(x => x.names.Count > 0).ToList();
            var first = new Dictionary<string, Object>(StringComparer.Ordinal);
            foreach (var (copy, names) in byCopy)
                foreach (var n in names) { var o = on.LoadAsset<Object>(p, n); if (o) first[n] = o; }
            // all copies open: every name again, the same object
            int same = 0, total = 0;
            foreach (var (copy, names) in byCopy)
                foreach (var n in names) { total++; if (first.TryGetValue(n, out var o) && on.LoadAsset<Object>(p, n) == o) same++; }
            var rw = on.CabRewrites.Skip(r0).ToList();
            ms.AddRange(rw.Where(r => r.Ok).Select(r => r.Ms));
            bytes += rw.Where(r => r.Ok).Sum(r => r.Bytes);
            Info($"AR12 {p}: {byCopy.Count} serving copies, {total} names; {string.Join(" | ", rw)}");
            Check($"AR12 {p}: every copy that serves a name usable at once (rewritten: {rw.Count(r => r.Ok)}), each name the same object again, no content.cabClash",
                  byCopy.Count >= 2 && rw.Count >= 1 && rw.All(r => r.Ok) && same == total && first.Count == total && on.CabClashes.Count == f0,
                  $"loaded {first.Count} / {total}, same {same}; findings {on.CabClashes.Count - f0}");
        }
        Release(on);
        if (ms.Count > 0)
        {
            ms.Sort();
            Info($"AR12 rewrite cost: {ms.Count} rewrites, median {ms[ms.Count / 2]:F1} ms, max {ms[ms.Count - 1]:F1} ms, {bytes / 1048576.0:F1} MiB loaded from memory in all");
        }
    }

    // ---------------------------------------------------------------- RU3

    static void RU3(ContentCatalog on)
    {
        string cache = on.NamesCacheFile();
        string aside = cache + ".ru3";
        double cold = double.NaN, warm = double.NaN, scanCold = double.NaN, scanWarm = double.NaN;
        try
        {
            if (File.Exists(cache)) File.Copy(cache, aside, true);
            if (File.Exists(cache)) File.Delete(cache);
            var t = Stopwatch.StartNew();
            var r = on.ScanModSources();
            scanCold = t.Elapsed.TotalMilliseconds;
            cold = on.NamesPass.Ms;
            Info("RU3 cold names pass: " + on.NamesPass);
            t.Restart();
            r = on.ScanModSources();
            scanWarm = t.Elapsed.TotalMilliseconds;
            warm = on.NamesPass.Ms;
            Info("RU3 cached names pass: " + on.NamesPass);
        }
        finally { if (File.Exists(aside)) { File.Copy(aside, cache, true); File.Delete(aside); } }
        Info($"RU3: the whole scan with the names pass: {scanCold:F0} ms (names cold), {scanWarm:F0} ms (names cached)");
        Target("RU3: names pass cold (added to the scan)", cold, 10000);
        Target("RU3: names pass cached (added to the scan)", warm, 200);

        // a lookup of a redirectable path (index + names), warm
        var idx = on.Redirect;
        var paths = idx.RedirectablePaths.ToList();
        var names = paths.Select(p => idx.AllNames(p).FirstOrDefault() ?? "x").ToList();
        int n = 0;
        var sw = Stopwatch.StartNew();
        for (int rep = 0; rep < 200; rep++)
            for (int i = 0; i < paths.Count; i++) { if (idx.IsRedirectable(paths[i])) n += idx.Serve(paths[i], names[i]).HasName ? 1 : 0; }
        double per = sw.Elapsed.TotalMilliseconds / (200.0 * paths.Count);
        Target("RU3: a redirect lookup (warm)", per, 0.01);

        // a first redirected load from a cold copy (the copy not open): vanilla hair rows of HairMod's paths
        var times = new List<double>();
        foreach (var e in on.EntriesOf(300).Where(e => e.IsVanilla && idx.HasAbdata(e["MainAB"] ?? "") && idx.IsRedirectable(e["MainAB"])).Take(8))
        {
            Release(on);
            var tl = Stopwatch.StartNew();
            var p = on.LoadAsset<GameObject>(e, "MainAB", "MainData");
            times.Add(tl.Elapsed.TotalMilliseconds);
        }
        Info("RU3 first redirected loads (ms): " + string.Join(", ", times.Select(F)));
        Target("RU3: first load of a redirected asset (cold copy, median)", Median(times), 30, "a cold first open of a HairMod copy also loads its abdata dependencies (chara/00/st_hair_00 ...) and the prefab with its textures; every bundle unloaded before each sample");
        Release(on);

        // the upgrade of a version 6 scan cache (the one-time cost of cache version 7): a copy in a scratch cache folder
        var src = on.Sources.OfType<Hs2InstallSource>().FirstOrDefault();
        string modlists = src != null ? on.CacheFileFor(src) : null;
        if (modlists != null && File.Exists(modlists))
        {
            string dir = Path.Combine(Path.GetTempPath(), "hs2next_ru3_" + System.Diagnostics.Process.GetCurrentProcess().Id);
            Directory.CreateDirectory(dir);
            try
            {
                var tmp = new ContentCatalog(dir).AddSource(new Hs2InstallSource(src.Root));
                string f = tmp.CacheFileFor(src);
                var sb = new StringBuilder("#hs2next-modlists\t6\n");
                foreach (var line in File.ReadLines(modlists).Skip(1))
                    if (!(line == "W" || line.StartsWith("W\t", StringComparison.Ordinal) || line.StartsWith("#end", StringComparison.Ordinal))) sb.Append(line).Append('\n');
                File.WriteAllText(f, sb.ToString(), new UTF8Encoding(false));
                var t = Stopwatch.StartNew();
                var r = tmp.ScanModSources();
                double up = t.Elapsed.TotalMilliseconds;
                Info($"RU3: the upgrade of a version 6 cache: {up:F0} ms, of which the cold names pass {tmp.NamesPass?.Ms:F0} ms (games read from {r.Sum(x => x.GamesRead)} manifests, {r.Sum(x => x.Scanned)} zipmods rescanned, discarded {r.Any(x => x.CacheDiscarded)})");
                Check("RU3: a version 6 cache upgrades without a cold scan", r.Sum(x => x.Scanned) == 0 && r.Sum(x => x.GamesRead) == FilesWithGuid, $"{r.Sum(x => x.GamesRead)} manifests");
            }
            finally
            {
                try { foreach (var x in Directory.GetFiles(dir)) File.Delete(x); Directory.Delete(dir); } catch (Exception) { }
            }
        }
    }

    // ================================================================ RunS1Explain (headless, for T)

    sealed class Classes
    {
        public readonly SortedDictionary<string, int> Lines = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, HashSet<string>> Cards = new SortedDictionary<string, HashSet<string>>(StringComparer.Ordinal);
        public void Add(string cls, string card)
        {
            Lines[cls] = Lines.TryGetValue(cls, out var n) ? n + 1 : 1;
            if (!Cards.TryGetValue(cls, out var s)) Cards[cls] = s = new HashSet<string>(StringComparer.Ordinal);
            s.Add(card);
        }
        public int Count(string cls) => Lines.TryGetValue(cls, out var n) ? n : 0;
    }

    public const string ClsBody = "redirect.body", ClsBodyUncensor = "redirect.bodyUncensor", ClsHair = "redirect.hairCopy", ClsKimono = "redirect.kimono",
        ClsModCopy = "redirect.modCopy", ClsOther = "other";

    public static void RunS1Explain()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        List<string> cards;
        string what;
        string only = Arg("-only");
        if (!string.IsNullOrEmpty(only))
        {
            var names = new HashSet<string>(only.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
            cards = MakerCoreTest.AllCards().Where(f => names.Contains(Path.GetFileName(f))).ToList();
            what = "the cards of -only (" + names.Count + " named)";
        }
        else if (Flag("-all")) { cards = MakerCoreTest.AllCards(); what = "every card"; }
        else if (Flag("-targets")) { cards = TestTargets.Load().VisualCards.Where(File.Exists).ToList(); what = "the visual targets"; }
        else { cards = MakerCoreTest.SampleCards(); what = "the visual targets + 20 sampled cards"; }
        int limit = int.Parse(Arg("-limit", int.MaxValue.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        if (cards.Count > limit) cards = cards.Take(limit).ToList();
        string offDir = Arg("-offDir");
        bool full = Flag("-other");
        Rep.AppendLine($"asset redirect S1 explain (Unity {Application.unityVersion}): {cards.Count} files, {what}; the switches-off snapshots: {(offDir ?? "none (the S1 baselines)")}; baselines {MakerCoreTest.BaselineDir}");
        var cls = new Classes();
        int equalOff = 0, differOff = 0, missingOff = 0, failed = 0, notChara = 0, n = 0, baseMissing = 0, baseEqual = 0, notRedirect = 0, notRedirectCards = 0;
        var others = new List<string>();
        var classPerCard = new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);
        bool set0 = ContentCatalog.SideloaderZipmodSet, red0 = ContentCatalog.SideloaderAssetRedirect;
        try
        {
            using (var h = MakerCoreTest.Harness.Open())
            {
                ContentCatalog.SideloaderZipmodSet = true;   // Harness.Open read the settings; the explain runs with the redirect on
                ContentCatalog.SideloaderAssetRedirect = true;
                Rep.AppendLine("catalog: " + string.Join("; ", h.Catalog.Log.Where(l => l.StartsWith("zipmod set", StringComparison.Ordinal) || l.StartsWith("asset redirect", StringComparison.Ordinal))));
                foreach (var file in cards)
                {
                    n++;
                    if (n % 25 == 0) Debug.Log($"AssetRedirectTest S1 explain: {n}/{cards.Count}, {sw.Elapsed.TotalSeconds:F0} s");
                    MakerCoreTest.Built b = null;
                    try
                    {
                        h.Catalog.Redirect.TakeRecent();
                        b = h.Build(file);
                        var recent = h.Catalog.Redirect.TakeRecent();
                        if (b.NotCharacter) { notChara++; continue; }
                        if (!b.Ok) { failed++; Check(Path.GetFileName(file) + ": built", false, b.Error); continue; }
                        var now = b.Snapshot();
                        var basis = MakerCoreTest.LoadBaseline(b.CardSha);
                        var off = offDir != null ? MakerCoreTest.LoadBaseline(b.CardSha, offDir) : basis;
                        if (basis == null) baseMissing++;
                        else if (basis.Sha256 == now.Sha256) baseEqual++;
                        if (off == null) { missingOff++; Info(Path.GetFileName(file) + ": no switches-off snapshot"); continue; }
                        if (off.Sha256 == now.Sha256) equalOff++;
                        else
                        {
                            differOff++;
                            var before = new Dictionary<string, int>(cls.Lines);
                            Classify(h.Catalog, b, off, now, recent, cls, others, full);
                            foreach (var kv in cls.Lines)
                            {
                                int d = kv.Value - (before.TryGetValue(kv.Key, out var v) ? v : 0);
                                if (d == 0) continue;
                                if (!classPerCard.TryGetValue(kv.Key, out var pc)) classPerCard[kv.Key] = pc = new SortedDictionary<string, int>(StringComparer.Ordinal);
                                pc[Path.GetFileName(file)] = d;
                            }
                        }
                        if (offDir != null && basis != null && basis.Sha256 != off.Sha256)
                        {
                            // lines that differ from the S1 baselines with the switches off: not the redirect's
                            int k = CharaSnapshot.Compare(basis, off, int.MaxValue).Lines.Count;
                            notRedirect += k;
                            notRedirectCards++;
                        }
                    }
                    catch (Exception e) { failed++; Check(Path.GetFileName(file) + ": no exception", false, e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace)); }
                    finally { b?.Dispose(); }
                    if (n % 10 == 0) h.ReleaseBundles();
                }
            }
        }
        finally { ContentCatalog.SideloaderZipmodSet = set0; ContentCatalog.SideloaderAssetRedirect = red0; }
        Info($"against the switches-off snapshots: {equalOff} equal, {differOff} differ, {missingOff} without one; {failed} failed, {notChara} not character cards");
        Info($"against the S1 baselines: {baseEqual} equal, {baseMissing} without a baseline" + (offDir != null ? $"; lines that differ with the switches off too (not the redirect's): {notRedirect} on {notRedirectCards} cards" : ""));
        foreach (var kv in cls.Lines) Info($"class {kv.Key}: {kv.Value} lines on {cls.Cards[kv.Key].Count} cards");
        foreach (var k in new[] { ClsBodyUncensor, ClsHair, ClsKimono, ClsModCopy, ClsOther })
            if (classPerCard.TryGetValue(k, out var pc)) foreach (var oc in pc.Take(full ? int.MaxValue : 60)) Info($"{k} card: {oc.Key} ({oc.Value} lines)");
        foreach (var o in full ? others : others.Take(60)) Info("other: " + o);
        Check("S1 explain: every line the redirect changes is classified (redirect.body / bodyUncensor / hairCopy / kimono / modCopy); 0 other",
              failed == 0 && missingOff == 0 && !cls.Lines.ContainsKey(ClsOther), $"other {cls.Count(ClsOther)} lines");
        Finish("asset_redirect_s1explain_report.txt", "AssetRedirectTest.RunS1Explain (the redirect's S1 classes)", sw, "ASSET REDIRECT S1");
    }

    /// <summary>The snapshot path of every transform (CharaSnapshot's: root name, "/" + child name, "[k]" for the k-th
    /// same-named sibling).</summary>
    static Dictionary<Transform, string> SnapPaths(Transform root)
    {
        var d = new Dictionary<Transform, string>();
        void Walk(Transform t, string p)
        {
            d[t] = p;
            var count = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                int k = count.TryGetValue(c.name, out var v) ? v + 1 : 0;
                count[c.name] = k;
                Walk(c, p + "/" + c.name + (k == 0 ? "" : "[" + k + "]"));
            }
        }
        Walk(root, root.name);
        return d;
    }

    static readonly HashSet<string> MaskTokens = new HashSet<string>(StringComparer.Ordinal) { "_AlphaEx", "_alpha_c", "_alpha_d" };
    static readonly Regex InvisibleCount = new Regex(@"invisible_\d+_renderers", RegexOptions.CultureInvariant);
    static int Invisible(string s) { var m = Regex.Match(s ?? "", @"invisible_(\d+)_renderers"); return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : -1; }

    // The class of a redirected load: the body copy of chara/oo_base, HairMod's copies of the vanilla hair paths, Terable
    // Kimonos' copies of the vanilla tops; a load whose earlier source was another zipmod's copy (a mod row on a path several
    // zipmods ship, served by the earliest loaded copy, RH2) is redirect.modCopy.
    static string ClassOf((string path, string asset, string served, string today) r)
    {
        if (r.today == "abdata")
        {
            if (r.path == Body) return ClsBody;
            if (r.path.IndexOf("/so_hair_", StringComparison.Ordinal) >= 0) return ClsHair;
            if (r.path.IndexOf("/fo_top_", StringComparison.Ordinal) >= 0) return ClsKimono;
            return ClsOther;
        }
        return ClsModCopy;
    }

    static void Classify(ContentCatalog cat, MakerCoreTest.Built b, CharaSnapshot basis, CharaSnapshot now, List<(string path, string asset, string served, string today)> recent,
                         Classes cls, List<string> others, bool full)
    {
        int cut = full ? int.MaxValue : 160;
        var ctx = b.Context;
        string card = Path.GetFileName(b.File);
        var paths = SnapPaths(b.Root.transform);
        // redirected assets (lower case) and their class
        var assetClass = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in recent)
        {
            string an = (r.asset ?? "").ToLowerInvariant();
            if (!assetClass.ContainsKey(an)) assetClass[an] = ClassOf(r);
        }
        bool body = recent.Any(r => r.path == Body && string.Equals(r.asset, "p_cf_body_00", StringComparison.OrdinalIgnoreCase));
        // part objects whose prefab another copy served, with that load's class
        var partRoots = new List<(string root, string cls)>();
        for (int k = 0; k < 4; k++)
        {
            string prefab = HairReport.PartOf(ctx, k)?.Prefab;
            if (ctx.Hair[k] && prefab != null && assetClass.TryGetValue(prefab.ToLowerInvariant(), out var c) && paths.TryGetValue(ctx.Hair[k].transform, out var p)) partRoots.Add((p, c));
        }
        for (int k = 0; k < 8; k++)
        {
            string prefab = ClothesReport.KindOf(ctx, k)?.Prefab;
            if (ctx.Clothes[k] && prefab != null && assetClass.TryGetValue(prefab.ToLowerInvariant(), out var c) && paths.TryGetValue(ctx.Clothes[k].transform, out var p)) partRoots.Add((p, c));
        }
        // the head part: its row from the face document
        var headDoc = ctx.Chara?.Face?.Head;
        if (ctx.Head && headDoc != null && ContentId.TryParse(headDoc, out var headId) && !headId.IsEmpty)
        {
            var hrow = cat.GetEntry(ctx.IsMale ? 110 : 210, headId);
            string hp = hrow?["MainData"];
            if (hp != null && assetClass.TryGetValue(hp.ToLowerInvariant(), out var hc) && paths.TryGetValue(ctx.Head.transform, out var hpath)) partRoots.Add((hpath, hc));
        }
        bool kimonoTop = partRoots.Any(x => x.cls == ClsKimono);
        string partsClass = partRoots.Any(x => x.cls == ClsModCopy) ? ClsModCopy : partRoots.Select(x => x.cls).FirstOrDefault(x => x != ClsOther);
        var recentPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in recent) if (!recentPaths.ContainsKey(r.path)) recentPaths[r.path] = ClassOf(r);
        // a body mesh a build hook replaced brings its own cm_o_dan_f (none in the official build)
        bool uncensorDan = ctx.ReplacedBodyMesh != null && ctx.Body && ctx.Body.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r => r.name == "cm_o_dan_f");
        string PartClass(string key)
        {
            int bar = key.IndexOf('|');
            string p = bar >= 0 ? key.Substring(bar + 1) : key;
            int br = p.LastIndexOf('[');
            foreach (var (r, c) in partRoots) if (p == r || p.StartsWith(r + "/", StringComparison.Ordinal) || (br > 0 && p.Substring(0, br) == r)) return c;
            return null;
        }
        // the class of a texture token's value naming a redirected texture asset
        string TextureClass(string value)
        {
            if (value == null) return null;
            int eq = value.IndexOf('=');
            string v = eq >= 0 ? value.Substring(eq + 1) : value;
            int par = v.IndexOf('(');
            string name = (par >= 0 ? v.Substring(0, par) : v).Trim().ToLowerInvariant();
            return name.Length > 0 && assetClass.TryGetValue(name, out var c) ? c : null;
        }
        var a = Keyed(basis.Lines); var cNow = Keyed(now.Lines);
        foreach (var kv in a.Concat(cNow.Where(x => !a.ContainsKey(x.Key))))
        {
            a.TryGetValue(kv.Key, out var la); cNow.TryGetValue(kv.Key, out var lc);
            if (la == lc) continue;
            string key = kv.Key, klass = ClsOther;
            bool danLine = key.Contains("/cm_o_dan_f|") || key.Contains("/cm_o_dan_f[") || key.EndsWith("/cm_o_dan_f", StringComparison.Ordinal) || key.Contains("/cm_o_dan_f/");
            string pc = PartClass(key);
            bool matLike = (key.StartsWith("mat|", StringComparison.Ordinal) || key.StartsWith("creator|", StringComparison.Ordinal)) && la != null && lc != null;
            var tokens = matLike ? TokenDiff(la, lc) : null;
            if (body && danLine) klass = uncensorDan ? ClsBodyUncensor : ClsBody;
            else if (body && uncensorDan && key.StartsWith("ctx|", StringComparison.Ordinal) && ((la ?? "") + (lc ?? "")).Contains("uncensor")) klass = ClsBodyUncensor;
            else if (pc == ClsHair)
            {
                // HairMod's copies differ from the abdata prefabs in useMesh and in material values: _Mesh_onoff, _HighLight (some
                // back rows), two materials' texture slots (HairModHS2_DX)
                if (tokens != null && tokens.All(t => t.name == "_Mesh_onoff" || t.name == "_HighLight" || t.texture)) klass = ClsHair;
            }
            else if (pc != null && pc != ClsOther) klass = pc;
            else if (tokens != null && tokens.Count > 0 && tokens.All(t => t.texture))
            {
                // texture slots that now hold the redirected copy's texture: the class of that load
                var tc = tokens.Select(t => TextureClass(t.after) ?? TextureClass(t.before)).ToList();
                if (tc.All(x => x != null && x != ClsOther)) klass = tc[0];
            }
            else if (kimonoTop && tokens != null && tokens.Count > 0 && tokens.All(t => MaskTokens.Contains(t.name))) klass = ClsKimono;
            else if (key.StartsWith("motion|", StringComparison.Ordinal) && partsClass != null) klass = partsClass;   // chains and colliders of the served prefabs
            else if (key.StartsWith("ctx|missing", StringComparison.Ordinal))
            {
                // a missing-content record (e.g. a texture the guard replaced) of a redirected path names the serving copy's texture
                string text = (la ?? "") + " " + (lc ?? "");
                foreach (var kvp in recentPaths) if (text.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0) { klass = kvp.Value; break; }
            }
            else if (body && key.StartsWith("ctx|ext", StringComparison.Ordinal) && la != null && lc != null
                     && InvisibleCount.Replace(la, "N") == InvisibleCount.Replace(lc, "N") && Invisible(lc) == Invisible(la) + 1)
                klass = ClsBody;   // InvisibleBody counts the body's renderers: one more with cm_o_dan_f
            if (klass == ClsOther)
            {
                others.Add($"{card} {Short(key, cut)}: {Short(la, cut)} <> {Short(lc, cut)}");
                if (full && pc == null && (key.Contains("/ct_hair") || key.Contains("/ct_head") || key.Contains("/ct_cloth") || key.Contains("/ct_")))
                {
                    for (int k = 0; k < 4; k++) { var pr = HairReport.PartOf(ctx, k); if (pr?.Prefab != null) others.Add($"   hair {k}: item {pr.Item} row {pr.Row} prefab {pr.Prefab} redirected {assetClass.ContainsKey(pr.Prefab.ToLowerInvariant())}"); }
                    others.Add("   recent: " + string.Join("; ", recent.Take(40).Select(r => r.path + " " + r.asset + " <- " + r.served + " (was " + r.today + ")")));
                }
            }
            cls.Add(klass, card);
        }
    }

    static Dictionary<string, string> Keyed(List<string> lines)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        var n = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var x in lines)
        {
            int p1 = x.IndexOf('|'); int p2 = p1 < 0 ? -1 : x.IndexOf('|', p1 + 1);
            string k = p2 < 0 ? x : x.Substring(0, p2);
            int c = n.TryGetValue(k, out var v) ? v + 1 : 0;
            n[k] = c;
            d[c == 0 ? k : k + "#" + c] = x;
        }
        return d;
    }

    /// <summary>The "name=value" tokens that differ between two material lines: their property names and whether the value is
    /// a texture ("name(WxH FORMAT mips=N)..." or none).</summary>
    static List<(string name, bool texture, string before, string after)> TokenDiff(string a, string b)
    {
        Dictionary<string, string> T(string s)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            var parts = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i <= s.Length; i++)
            {
                char ch = i < s.Length ? s[i] : ' ';
                if (ch == '(') depth++;
                else if (ch == ')') depth = Math.Max(0, depth - 1);
                else if (ch == ' ' && depth == 0) { if (i > start) parts.Add(s.Substring(start, i - start)); start = i + 1; }
            }
            foreach (var t in parts) { int e = t.IndexOf('='); d[e > 0 ? t.Substring(0, e) : t] = t; }
            return d;
        }
        var ta = T(a); var tb = T(b);
        var l = new List<(string, bool, string, string)>();
        foreach (var k in ta.Keys.Union(tb.Keys))
        {
            ta.TryGetValue(k, out var x); tb.TryGetValue(k, out var y);
            if (x == y) continue;
            bool tex = (x ?? "").Contains("(") || (y ?? "").Contains("(") || (x ?? "").EndsWith("=none", StringComparison.Ordinal) || (y ?? "").EndsWith("=none", StringComparison.Ordinal);
            l.Add((k, tex, x, y));
        }
        return l;
    }

    // ================================================================ BuildFixtures

    /// <summary>Builds the fixture bundles of the plain-Mono content suite from Editor/Fixtures/Redirect/src (three text assets):
    /// uncompressed, LZ4 (recompressed for run time), LZ4HC (chunk-based build) and LZMA; prints their compression.</summary>
    public static void BuildFixtures()
    {
        const string src = "Assets/HS2Next/Editor/Fixtures/Redirect/src";
        var assets = new[] { src + "/redirect_alpha.txt", src + "/redirect_beta.txt", src + "/redirect_gamma.bytes" };
        AssetDatabase.Refresh();
        string tmp = Path.Combine(Root, "Temp", "redirect_fixtures");
        int code = 0;
        try
        {
            void Build(string name, BuildAssetBundleOptions opt)
            {
                string dir = Path.Combine(tmp, name);
                Directory.CreateDirectory(dir);
                var build = new AssetBundleBuild { assetBundleName = "redirect_fixture.unity3d", assetNames = assets };
                var m = BuildPipeline.BuildAssetBundles(dir, new[] { build }, opt | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64);
                if (m == null) throw new Exception("build " + name + " failed");
                File.Copy(Path.Combine(dir, "redirect_fixture.unity3d"), Path.Combine(FixtureDir, "redirect_" + name + ".unity3d"), true);
            }
            Build("none", BuildAssetBundleOptions.UncompressedAssetBundle);
            Build("lz4hc", BuildAssetBundleOptions.ChunkBasedCompression);
            Build("lzma", BuildAssetBundleOptions.None);
            // run-time LZ4 from the uncompressed bundle
            var op = AssetBundle.RecompressAssetBundleAsync(Path.Combine(FixtureDir, "redirect_none.unity3d"), Path.Combine(tmp, "redirect_lz4.unity3d"), BuildCompression.LZ4Runtime);
            var t = Stopwatch.StartNew();
            while (!op.isDone && t.Elapsed.TotalSeconds < 60) System.Threading.Thread.Sleep(20);
            if (!op.isDone || !op.success) throw new Exception("LZ4 recompression: " + (op.isDone ? op.humanReadableResult : "not done in 60 s"));
            File.Copy(Path.Combine(tmp, "redirect_lz4.unity3d"), Path.Combine(FixtureDir, "redirect_lz4.unity3d"), true);
            foreach (var f in new[] { "none", "lz4", "lz4hc", "lzma" })
            {
                string p = Path.Combine(FixtureDir, "redirect_" + f + ".unity3d");
                var n = BundleNames.ReadFile(p, 0, new FileInfo(p).Length);
                Debug.Log($"AssetRedirectTest fixture redirect_{f}.unity3d: {new FileInfo(p).Length} bytes, {n.Status}, compression {n.Compression}, names {(n.Names == null ? "-" : string.Join(",", n.Names.OrderBy(x => x, StringComparer.Ordinal)))}");
            }
            AssetDatabase.Refresh();
        }
        catch (Exception e) { Debug.LogError("AssetRedirectTest.BuildFixtures: " + e); code = 1; }
        if (Application.isBatchMode) TestExit.Exit(code);
    }
}

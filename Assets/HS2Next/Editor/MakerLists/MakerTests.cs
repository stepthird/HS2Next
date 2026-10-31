// LS tests of the Maker lists, plain Mono
// (Tools/mono_tests.ps1 -Suite maker; fixtures = the project root):
//   MakerRulesTests   synthetic rows and manifests: faceSkinInfo / headPresetInfo reading rules, the head of a skin row
//                     (entry, raw, mismatch, missing head, male parity), presets, Possess 99, the gate switch, sexes, paths,
//                     names, the info cache round trip;
//   MakerCensusTests  the install (zipmod cache + vanilla fixture + manifests): counts per category and sex equal the census
//                     (Tools/survey/maker_core_census.txt) and the inventory, the 1,921
//                     faceSkinInfo-covered skins list under their mod head, the 56 uncovered under the vanilla head, Possess
//                     99 hidden, the 15 gated rows, the MakerLists.Rows timing target;
//   MakerPresetTests  preset resolution of the sampled heads (fixture P lines) against Sideloader's rule, and against H4
//                     (Cache/compare/maker/h4.json: HS2's pushes of the presets of 11 heads);
//   MakerHs2Tests     the lists against HS2's own (H1 / H3: Cache/compare/maker/lists/<category>.json, the rows of HS2's
//                     chaListCtrl through the Studio Bridge): the same references per category (gate open, as HS2 on this
//                     install; Possess 99 hidden as the Maker does) and the same (skin, head) pairs of the face-skin lists.
// The install groups skip with a notice when the caches are absent.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace HS2Next.Content.Maker.Tests
{
    using HS2Next.Save.Tests;

    sealed class MemRows : IListRows
    {
        public readonly List<ListRow> All = new List<ListRow>();
        public readonly Dictionary<string, ZipMakerInfo> Infos = new Dictionary<string, ZipMakerInfo>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<ListRow> RowsOf(int category) => All.Where(r => r.Category == category).ToList();
        public ListRow Find(int category, ContentId key) => All.FirstOrDefault(r => r.Category == category && r.Key == key);
        public readonly Dictionary<(int, ContentId), List<ListRow>> Defs = new Dictionary<(int, ContentId), List<ListRow>>();
        public IReadOnlyDictionary<ContentId, IReadOnlyList<ListRow>> RepeatedDefinitions(int category) =>
            Defs.Where(kv => kv.Key.Item1 == category).ToDictionary(kv => kv.Key.Item2, kv => (IReadOnlyList<ListRow>)kv.Value);
        public ZipMakerInfo InfoOf(string zipPath) => zipPath != null && Infos.TryGetValue(zipPath, out var z) ? z : null;
        public IReadOnlyList<ZipMakerInfo> InfosInLoadOrder => Infos.Values.OrderBy(z => z.Guid, GuidLoadOrder.Instance).ToList();

        public ListRow Add(int cat, string guid, int id, string zip = null, bool gated = false, params string[] kv)
        {
            var v = new Dictionary<string, string>();
            for (int i = 0; i + 1 < kv.Length; i += 2) v[kv[i]] = kv[i + 1];
            var r = new ListRow(cat, guid == null ? ContentId.Vanilla(id) : new ContentId(guid, id), v, zip, gated);
            All.Add(r);
            return r;
        }

        public ZipMakerInfo Zip(string path, string guid, string manifestBody, List<string> errors = null)
        {
            var z = new ZipMakerInfo { Path = path, Guid = guid, Stamp = "1:1" };
            MakerInfoCache.ParseManifest("<manifest><guid>" + guid + "</guid>" + manifestBody + "</manifest>", z, errors ?? new List<string>());
            Infos[path] = z;
            return z;
        }
    }

    public static class MakerRulesTests
    {
        public static void Run(TestRunner t)
        {
            // ---- manifest reading
            var errors = new List<string>();
            var m = new MemRows();
            var za = m.Zip("a.zipmod", "mod.a",
                "<faceSkinInfo skinID=\"10\" headID=\"5\" headGUID=\"mod.a\"/>" +
                "<faceSkinInfo skinID=\"10\" headID=\"6\" headGUID=\"mod.a\"/>" +          // second line for skin 10: unused (first wins)
                "<faceSkinInfo skinID=\"11\" headID=\"5\" headGUID=\" \"/>" +              // blank GUID: skipped
                "<faceSkinInfo skinID=\"x\" headID=\"5\" headGUID=\"mod.a\"/>" +           // not an integer: skipped
                "<faceSkinInfo skinID=\"12\" headID=\"7\" headGUID=\"mod.b\"/>" +
                "<faceSkinInfo skinID=\"13\" headID=\"9\" headGUID=\"mod.a\"/>" +          // mismatch with the row's HeadID 5
                "<faceSkinInfo skinID=\"14\" headID=\"99\" headGUID=\"mod.a\"/>" +         // head not installed
                "<sub><faceSkinInfo skinID=\"15\" headID=\"5\" headGUID=\"mod.a\"/></sub>" + // not a child of the root: ignored
                "<headPresetInfo preset=\"preset_head_02\"><headID>5</headID><headGUID>mod.a</headGUID><skinGUID>mod.a</skinGUID>" +
                "<eyelashesGUID> </eyelashesGUID><lipGUID>mod.c</lipGUID></headPresetInfo>" +
                "<headPresetInfo preset=\"\"><headID>5</headID></headPresetInfo>" +             // no preset: skipped
                "<headPresetInfo preset=\"preset_head_00\"><headID>1</headID></headPresetInfo>",  // vanilla head 1 (no GUID)
                errors);
            t.Equal(5, za.FaceSkins.Count, "manifest: five valid faceSkinInfo lines, direct children of the root only");
            t.Equal(2, za.HeadPresets.Count, "manifest: two valid headPresetInfo blocks");
            t.Equal(3, errors.Count, "manifest: three skipped lines reported");
            t.Equal(5, za.FaceSkinFor(10).HeadId, "manifest: the first line of a skin id wins");
            var hp = za.HeadPresets[0];
            t.Check(hp.HeadGuid == "mod.a" && hp.Guid("skinGUID") == "mod.a" && hp.Guid("eyelashesGUID") == null && hp.Guid("lipGUID") == "mod.c" && hp.Guid("detailGUID") == null,
                "manifest: GUID elements read, blank = none");
            t.Check(za.HeadPresets[1].HeadGuid == null && za.HeadPresets[1].HeadId == 1, "manifest: a block without headGUID is a vanilla head's");

            // ---- the info cache round trip
            var cacheFile = Path.Combine(t.TempDir ?? Path.GetTempPath(), "makerinfo-test.tsv");
            var zipFile = Path.Combine(t.TempDir ?? Path.GetTempPath(), "fake.zipmod");
            WriteZip(zipFile, "<manifest><guid>mod.z</guid><faceSkinInfo skinID=\"3\" headID=\"4\" headGUID=\"mod.z\"/>" +
                "<headPresetInfo preset=\"preset_head_02\"><headID>4</headID><headGUID>mod.z</headGUID><moleGUID>mod.z</moleGUID></headPresetInfo></manifest>");
            var c1 = MakerInfoCache.Load(cacheFile, new[] { (zipFile, "mod.z") });
            var c2 = MakerInfoCache.Load(cacheFile, new[] { (zipFile, "mod.z") });
            var z2 = c2.Get(zipFile);
            t.Check(c1.Scanned == 1 && c2.Scanned == 0 && File.Exists(cacheFile), "cache: first load reads the manifest, the second takes the cache");
            t.Check(z2 != null && z2.FaceSkins.Count == 1 && z2.FaceSkins[0].HeadGuid == "mod.z" && z2.FaceSkins[0].HeadId == 4
                    && z2.HeadPresets.Count == 1 && z2.HeadPresets[0].Guid("moleGUID") == "mod.z" && z2.HeadPresets[0].Guid("skinGUID") == null
                    && z2.HeadPresets[0].Zipmod == z2, "cache: entries survive the round trip");
            File.SetLastWriteTimeUtc(zipFile, DateTime.UtcNow.AddMinutes(1));
            t.Equal(1, MakerInfoCache.Load(cacheFile, new[] { (zipFile, "mod.z") }).Scanned, "cache: a changed stamp re-reads the manifest");

            // ---- skin rows and heads
            m.Add(210, null, 0, null, false, "Name", "v0", "Preset", "preset_head_00");
            m.Add(210, null, 1, null, false, "Name", "v1", "Preset", "preset_head_00");
            var h5 = m.Add(210, "mod.a", 5, "a.zipmod", false, "Name", "A5", "Preset", "preset_head_02");
            m.Add(210, "mod.a", 9, "a.zipmod", false, "Name", "A9", "Preset", "0");
            m.Add(210, "mod.b", 7, "b.zipmod", false, "Name", "B7");
            var vs = m.Add(211, null, 0, null, false, "HeadID", "1");
            var s10 = m.Add(211, "mod.a", 10, "a.zipmod", false, "HeadID", "5");
            var s12 = m.Add(211, "mod.a", 12, "a.zipmod", false, "HeadID", "7");
            var s13 = m.Add(211, "mod.a", 13, "a.zipmod", false, "HeadID", "5");
            var s14 = m.Add(211, "mod.a", 14, "a.zipmod", false, "HeadID", "99");
            var s16 = m.Add(211, "mod.a", 16, "a.zipmod", false, "HeadID", "0");
            var s17 = m.Add(211, "mod.a", 17, "a.zipmod", false, "HeadID", "-");
            FaceSkinHeads.How how;
            t.Check(FaceSkinHeads.HeadOf(vs, m, out how, out _) == ContentId.Vanilla(1) && how == FaceSkinHeads.How.Vanilla, "skin: vanilla row → vanilla head of its HeadID");
            t.Check(FaceSkinHeads.HeadOf(s10, m, out how, out _) == new ContentId("mod.a", 5) && how == FaceSkinHeads.How.FaceSkinInfo, "skin: faceSkinInfo → its mod head");
            t.Check(FaceSkinHeads.HeadOf(s12, m, out how, out _) == new ContentId("mod.b", 7) && how == FaceSkinHeads.How.FaceSkinInfo, "skin: faceSkinInfo naming another zipmod's head");
            t.Check(FaceSkinHeads.HeadOf(s13, m, out how, out _) == ContentId.Vanilla(5) && how == FaceSkinHeads.How.Mismatch, "skin: mismatching headID → raw HeadID (vanilla)");
            t.Check(FaceSkinHeads.HeadOf(s14, m, out how, out _) == ContentId.Vanilla(99) && how == FaceSkinHeads.How.HeadMissing, "skin: head not installed → raw HeadID");
            t.Check(FaceSkinHeads.HeadOf(s16, m, out how, out _) == ContentId.Vanilla(0) && how == FaceSkinHeads.How.Raw, "skin: no entry → raw HeadID (a vanilla head)");
            t.Check(FaceSkinHeads.HeadOf(s17, m, out how, out _).IsEmpty && how == FaceSkinHeads.How.NoHeadId, "skin: no integer HeadID → no head");
            // male parity: the entry of skin 20 binds to the female row of the same (GUID, id)
            m.Zip("m.zipmod", "mod.m", "<faceSkinInfo skinID=\"20\" headID=\"1\" headGUID=\"mod.m\"/><faceSkinInfo skinID=\"21\" headID=\"1\" headGUID=\"mod.m\"/>");
            m.Add(110, "mod.m", 1, "m.zipmod");
            m.Add(210, "mod.m", 1, "m.zipmod");
            m.Add(211, "mod.m", 20, "m.zipmod", false, "HeadID", "1");
            var ms20 = m.Add(111, "mod.m", 20, "m.zipmod", false, "HeadID", "1");
            var ms21 = m.Add(111, "mod.m", 21, "m.zipmod", false, "HeadID", "1");
            t.Check(FaceSkinHeads.HeadOf(ms20, m, out how, out _) == ContentId.Vanilla(1) && how == FaceSkinHeads.How.Raw, "skin: male row whose GUID has a female skin of the same id gets no entry (Sideloader binds 211 first)");
            t.Check(FaceSkinHeads.HeadOf(ms21, m) == new ContentId("mod.m", 1), "skin: male row → male mod head (110)");

            // a skin id defined twice in one zipmod (H3): the entry re-points the first definition, the second keeps its HeadID
            m.Zip("d.zipmod", "mod.d", "<faceSkinInfo skinID=\"30\" headID=\"2\" headGUID=\"mod.d\"/>");
            m.Add(210, "mod.d", 2, "d.zipmod");
            var d30 = m.Add(211, "mod.d", 30, "d.zipmod", false, "HeadID", "2");
            m.Defs[(211, d30.Key)] = new List<ListRow> { d30, new ListRow(211, d30.Key, new Dictionary<string, string> { ["HeadID"] = "1" }, "d.zipmod") };

            // ---- MakerLists over the synthetic rows
            m.Add(359, null, 0, null, false, "Possess", "99", "Name", "hidden");
            m.Add(359, null, 1, null, false, "Possess", "1", "Name", "shown", "EN_US", "Shown", "ZH_CN", "0");
            m.Add(359, null, 5, null, true, "Possess", "1", "Name", "gated");
            m.Add(359, "mod.a", 0, "a.zipmod", false, "Possess", "99", "Name", "mod possess 99");
            var L = new MakerLists(m);
            var back = L.Rows(359, 1).Select(r => r["Name"]).ToList();
            t.Check(back.SequenceEqual(new[] { "shown", "mod possess 99" }), "lists: vanilla Possess 99 hidden, mod Possess ignored (Sideloader forces 1), gated hidden: " + string.Join(",", back));
            L.ShowGated = true;
            t.Check(L.Rows(359, 1).Any(r => r["Name"] == "gated"), "lists: ShowGated opens the gate");
            L.ShowGated = false;
            t.Check(!L.IsListed(359, 1, ContentId.Vanilla(0)) && L.IsListed(359, 1, ContentId.Vanilla(1)) && !L.IsListed(359, 1, ContentId.Vanilla(5)), "lists: IsListed follows Possess and the gate");
            t.Check(L.Rows(211, 1, ListContext.ForHead(new ContentId("mod.a", 5))).SequenceEqual(new[] { s10 }), "lists: skins of a mod head");
            t.Check(L.Rows(211, 1, ListContext.ForHead(ContentId.Vanilla(5))).SequenceEqual(new[] { s13 }), "lists: a mismatching row lists under the vanilla head of its raw id");
            t.Check(L.Rows(211, 1, ListContext.ForHead(ContentId.Vanilla(0))).SequenceEqual(new[] { s16 }), "lists: an uncovered mod skin lists under the vanilla head");
            t.Equal(9, L.Rows(211, 1).Count, "lists: no head = every skin row (a reference once)");
            t.Check(L.Rows(211, 1, ListContext.ForHead(new ContentId("mod.d", 2))).SequenceEqual(new[] { d30 }) && L.Rows(211, 1, ListContext.ForHead(ContentId.Vanilla(1))).SequenceEqual(new[] { vs, d30 }),
                "lists: a skin defined twice lists under the faceSkinInfo head (first definition) and the raw head of the second");
            t.Check(L.IsListed(211, 1, d30.Key, ListContext.ForHead(ContentId.Vanilla(1))) && L.HeadOf(d30) == new ContentId("mod.d", 2), "lists: IsListed by any definition, HeadOf = the first definition's head");
            t.Equal(0, L.Rows(211, 0).Count, "lists: female categories have no list for males");
            t.Equal(0, L.Rows(111, 1).Count, "lists: male categories have no list for females");
            t.Check(L.SkinAfterHeadChange(new ContentId("mod.a", 5), ContentId.Vanilla(0), 1) == s10.Key, "lists: head change snaps the skin to the first listed one");
            t.Check(L.SkinAfterHeadChange(new ContentId("mod.a", 5), s10.Key, 1) == s10.Key, "lists: head change keeps a listed skin");
            t.Check(L.SkinAfterHeadChange(new ContentId("mod.a", 9), s10.Key, 1) == s10.Key, "lists: a head without skins keeps the skin");
            t.Check(L.PresetHeads(1).Select(h => h["Name"]).SequenceEqual(new[] { "v0", "v1", "A5" }), "lists: preset heads = Preset column set (\"0\" = none)");
            t.Equal("Shown", MakerLists.NameOf(m.Find(359, ContentId.Vanilla(1)), "en"), "names: en = EN_US");
            t.Equal("shown", MakerLists.NameOf(m.Find(359, ContentId.Vanilla(1)), "zh-Hans"), "names: \"0\" falls back to Name");
            t.Equal("shown", MakerLists.NameOf(m.Find(359, ContentId.Vanilla(1)), "ja"), "names: ja = Name");

            // ---- per-sex categories and paths
            t.Check(MakerLists.ClothesCategory(2, 0) == -1 && MakerLists.ClothesCategory(5, 0) == -1 && MakerLists.ClothesCategory(7, 0) == 147 && MakerLists.ClothesCategory(3, 1) == 243,
                "sexes: male clothes kinds 2, 3, 5, 6 have no list");
            t.Check(MakerLists.CategoryOfPath("appearance.chara.face.skin", 1) == 211 && MakerLists.CategoryOfPath("chara.face.skin", 0) == 111
                    && MakerLists.CategoryOfPath("coord.clothes.innerTop.item", 0) == -1 && MakerLists.CategoryOfPath("coord.clothes.top.colors[2].pattern", 1) == 348
                    && MakerLists.CategoryOfPath("chara.hair.option.item", 1) == 303 && MakerLists.CategoryOfPath("chara.hair.front.mesh.item", 1) == 306
                    && MakerLists.CategoryOfPath("chara.body.paint[1].layoutItem", 1) == 8 && MakerLists.CategoryOfPath("chara.face.makeup.paint[0].layoutItem", 1) == 7
                    && MakerLists.CategoryOfPath("coord.accessories[\"x\"].item", 1, 351) == 351 && MakerLists.CategoryOfPath("chara.face.beard.item", 1) == -1,
                "paths: ref members map to their categories per sex");

            // ---- presets
            m.Add(317, "mod.a", 3, "a.zipmod");
            m.Add(211, "mod.a", 2, "a.zipmod", false, "HeadID", "5");
            var e5 = HeadPresets.FindEntry(m, h5);
            t.Check(e5 != null && e5.Preset == "preset_head_02", "presets: entry by (head GUID, head id, Preset column)");
            var ev = HeadPresets.FindEntry(m, m.Find(210, ContentId.Vanilla(1)));
            t.Check(ev != null && ev.HeadGuid == null && ev.HeadId == 1, "presets: a vanilla head matches an entry without headGUID");
            t.Check(HeadPresets.FindEntry(m, m.Find(210, ContentId.Vanilla(0))) == null, "presets: no entry for vanilla head 0");
            var findings = new List<string>();
            var res = HeadPresets.ResolveAll(m, h5, 1, new Dictionary<string, int>
            {
                ["face.head"] = 5, ["face.skin"] = 2, ["face.eyes.left.iris.item"] = 3, ["face.makeup.lip.item"] = 4, ["face.mole.item"] = 1,
            }, findings);
            t.Check(res["face.head"] == new ContentId("mod.a", 5) && res["face.skin"] == new ContentId("mod.a", 2), "presets: GUID elements resolve their ids");
            t.Check(res["face.eyes.left.iris.item"] == ContentId.Vanilla(3) && res["face.mole.item"] == ContentId.Vanilla(1), "presets: properties without a GUID stay vanilla");
            t.Check(res["face.makeup.lip.item"] == ContentId.Vanilla(4) && findings.Count == 1 && findings[0].StartsWith("edit.presetUnresolved", StringComparison.Ordinal),
                "presets: a GUID whose row is missing keeps the vanilla id (edit.presetUnresolved)");
            t.Equal(18, HeadPresets.Properties.Length, "presets: 18 properties (11 face, 7 makeup)");
        }

        static void WriteZip(string path, string manifest)
        {
            if (File.Exists(path)) File.Delete(path);
            using (var fs = new FileStream(path, FileMode.CreateNew))
            using (var zf = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
            {
                var e = zf.CreateEntry("manifest.xml");
                using (var w = new StreamWriter(e.Open())) w.Write(manifest);
            }
        }
    }

    /// <summary>Shared loading of the install fixture (once per process).</summary>
    static class InstallFixture
    {
        static FixtureListRows rows;
        static bool tried;
        public static string Reason;

        public static FixtureListRows Get(TestRunner t)
        {
            if (tried) return rows;
            tried = true;
            var root = t.FixturesDir;
            var cache = FixtureListRows.FindModCache(root == null ? null : Path.Combine(root, "Cache"));
            if (cache == null) { Reason = "no zipmod cache Cache/modlists-hs2-install-*.tsv"; return null; }
            var sw = Stopwatch.StartNew();
            rows = FixtureListRows.Load(cache, Path.Combine(root, "Cache", "maker", "vanilla_lists.tsv"));
            t.Info($"fixture: {Path.GetFileName(cache)}, {rows.RawModRows.Count} mod rows, vanilla fixture {(rows.HasVanilla ? "yes" : "absent")} " +
                   $"{string.Join(" ", rows.VanillaComments)}; manifests {rows.Infos.ByPath.Count} ({rows.Infos.Scanned} read, {rows.Infos.LoadMs:0} ms); {sw.ElapsedMilliseconds} ms");
            return rows;
        }
    }

    public static class MakerCensusTests
    {
        // Tools/survey/maker_core_census.txt: vanilla shown / mods unique.
        static readonly (int cat, int sex, int vanilla, int mods)[] Inventory =
        {
            (6, 1, 8, 0), (7, 1, 6, 0), (8, 1, 20, 1),
            (110, 0, 1, 9), (210, 1, 4, 364), (111, 0, 8, 142), (211, 1, 49, 3003), (112, 0, 12, 31), (212, 1, 19, 124), (121, 0, 9, 15),
            (131, 0, 5, 10), (231, 1, 4, 46), (132, 0, 5, 10), (232, 1, 5, 62), (133, 0, 8, 20), (233, 1, 15, 67),
            (140, 0, 13, 132), (240, 1, 100, 808), (141, 0, 11, 89), (241, 1, 48, 467), (242, 1, 54, 425), (243, 1, 35, 268),
            (144, 0, 4, 35), (244, 1, 29, 217), (245, 1, 25, 272), (246, 1, 29, 177), (147, 0, 12, 76), (247, 1, 48, 412),
            (300, 1, 49, 943), (301, 1, 39, 302), (302, 1, 8, 126), (303, 1, 8, 139), (305, 1, 7, 0), (306, 1, 12, 0),
            (313, 1, 49, 1367), (314, 1, 27, 491), (315, 1, 9, 444), (316, 1, 44, 459), (317, 1, 30, 225), (318, 1, 8, 33),
            (319, 1, 45, 63), (320, 1, 17, 344), (322, 1, 19, 358), (323, 1, 5, 53), (334, 1, 5, 91), (335, 1, 15, 154),
            (336, 1, 7, 0), (348, 1, 61, 350), (350, 1, 1, 0),
            (351, 1, 149, 824), (352, 1, 9, 77), (353, 1, 23, 44), (354, 1, 15, 103), (355, 1, 55, 251), (356, 1, 18, 24),
            (357, 1, 8, 43), (358, 1, 29, 197), (359, 1, 19, 89), (360, 1, 27, 90), (361, 1, 92, 173), (362, 1, 14, 73), (363, 1, 3, 57),
        };

        public static void Run(TestRunner t)
        {
            var rows = InstallFixture.Get(t);
            if (rows == null) { t.Info("SKIP: " + InstallFixture.Reason); return; }
            var L = new MakerLists(rows);

            // ---- census: raw zipmod rows (before de-duplication)
            t.Equal(16326, rows.RawModRows.Count, "census: mod character-list rows in the cache");
            foreach (var (sex, hc, sc, nHeads, headZips, nSkins, skinZips) in new[] { (1, 210, 211, 364, 359, 3143, 393), (0, 110, 111, 9, 6, 142, 16) })
            {
                var mh = rows.RawModRows.Where(r => r.category == hc).ToList();
                var mskin = rows.RawModRows.Where(r => r.category == sc).ToList();
                t.Check(mh.Count == nHeads && mh.Select(r => r.zip).Distinct(StringComparer.OrdinalIgnoreCase).Count() == headZips,
                    $"census: mod heads {hc} rows / zipmods {mh.Count} / {mh.Select(r => r.zip).Distinct(StringComparer.OrdinalIgnoreCase).Count()} (expected {nHeads} / {headZips})");
                t.Check(mskin.Count == nSkins && mskin.Select(r => r.zip).Distinct(StringComparer.OrdinalIgnoreCase).Count() == skinZips,
                    $"census: mod face skins {sc} rows / zipmods {mskin.Count} / {mskin.Select(r => r.zip).Distinct(StringComparer.OrdinalIgnoreCase).Count()} (expected {nSkins} / {skinZips})");
            }
            var infoZips = rows.Infos.ByPath.Values.ToList();
            t.Equal(409, infoZips.Count, "census: zipmods with head / face-skin rows (manifests read)");
            t.Check(rows.Infos.FaceSkinCount == 2535 && infoZips.Count(z => z.FaceSkins.Count > 0) == 373,
                $"census: faceSkinInfo {rows.Infos.FaceSkinCount} in {infoZips.Count(z => z.FaceSkins.Count > 0)} zipmods (expected 2535 in 373)");
            t.Check(rows.Infos.HeadPresetCount == 349 && infoZips.Count(z => z.HeadPresets.Count > 0) == 349,
                $"census: headPresetInfo {rows.Infos.HeadPresetCount} in {infoZips.Count(z => z.HeadPresets.Count > 0)} zipmods (expected 349 in 349)");
            if (rows.Infos.Errors.Count > 0) t.Info("manifest notes: " + rows.Infos.Errors.Count + " (first: " + rows.Infos.Errors[0] + ")");

            // ---- faceSkinInfo coverage (census definition: raw rows whose HeadID is a head id of their own zipmod)
            var headIdsByZip = rows.RawModRows.Where(r => r.category == 210 || r.category == 110)
                .GroupBy(r => r.zip, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => new HashSet<string>(g.Select(r => r.id.ToString())), StringComparer.OrdinalIgnoreCase);
            int own = 0, covered = 0;
            var ownRows = new List<(string zip, string guid, int cat, int id, bool cov)>();
            foreach (var r in rows.RawModRows)
            {
                if (r.category != 211 && r.category != 111) continue;
                if (!r.values.TryGetValue("HeadID", out var hid) || !headIdsByZip.TryGetValue(r.zip, out var hs) || !hs.Contains(hid)) continue;
                own++;
                bool cov = rows.InfoOf(r.zip)?.FaceSkinFor(r.id) != null;
                if (cov) covered++;
                ownRows.Add((r.zip, r.guid, r.category, r.id, cov));
            }
            t.Equal(1977, own, "census: mod face skins naming a head id of their own zipmod");
            t.Equal(1921, covered, "census: of those, with a faceSkinInfo entry");
            t.Equal(56, own - covered, "census: of those, without an entry");
            // per definition (H3: Sideloader keeps every definition of a repeated id as a row; the entry re-points the first)
            int listedMod = 0, listedVanilla = 0, wrong = 0, laterCov = 0, firstCov = 0, uncov = 0;
            var occurrence = new Dictionary<(int, ContentId), int>();
            foreach (var o in ownRows)
            {
                int sex = o.cat == 211 ? 1 : 0;
                var key = new ContentId(o.guid, o.id);
                occurrence.TryGetValue((o.cat, key), out int nth);
                occurrence[(o.cat, key)] = nth + 1;
                var row = rows.Find(o.cat, key);
                ContentId want;
                if (o.cov && nth == 0)
                {
                    firstCov++;
                    var e = rows.InfoOf(o.zip).FaceSkinFor(o.id);
                    want = new ContentId(e.HeadGuid, e.HeadId);
                    if (L.HeadOf(row) == want && L.HeadSourceOf(row) == FaceSkinHeads.How.FaceSkinInfo && L.Rows(o.cat, sex, ListContext.ForHead(want)).Contains(row)) listedMod++;
                    else { wrong++; if (wrong <= 5) t.Info($"covered skin not under its mod head: {o.cat} {key} -> {L.HeadOf(row)} ({L.HeadSourceOf(row)}), entry {e}"); }
                    continue;
                }
                if (o.cov) laterCov++; else uncov++;
                // the raw HeadID of this definition (its own column)
                var def = nth == 0 ? row : rows.DefinitionsOf(o.cat, key)?[nth];
                want = ContentId.Vanilla(int.Parse(def["HeadID"]));
                if (L.IsListedUnder(row, want) && L.Rows(o.cat, sex, ListContext.ForHead(want)).Contains(row)) listedVanilla++;
                else { wrong++; if (wrong <= 5) t.Info($"skin definition not under the vanilla head of its HeadID: {o.cat} {key} HeadID {def["HeadID"]}"); }
            }
            t.Info($"faceSkinInfo per definition: {own} own-head skin rows: covered first definitions {firstCov} (under the mod head {listedMod}), covered later definitions {laterCov} and uncovered {uncov} (under the vanilla head of their HeadID {listedVanilla})");
            t.Equal(0, wrong, $"faceSkinInfo: every covered skin lists under its mod head ({listedMod} of {firstCov}; later definitions of a repeated id {laterCov} keep their HeadID as in HS2, H3), every uncovered one under the vanilla head of its HeadID ({listedVanilla} of {laterCov + uncov})");
            // the whole skin lists: how every shown mod skin row was placed
            foreach (int sc in new[] { 211, 111 })
            {
                var hist = L.Rows(sc, sc == 211 ? 1 : 0).Where(r => !r.IsVanilla).GroupBy(r => L.HeadSourceOf(r)).ToDictionary(g => g.Key, g => g.Count());
                t.Info($"skins {sc}: " + string.Join(", ", hist.OrderBy(kv => kv.Key).Select(kv => kv.Key + " " + kv.Value)));
                t.Check(!hist.ContainsKey(FaceSkinHeads.How.HeadMissing) && !hist.ContainsKey(FaceSkinHeads.How.Mismatch), $"faceSkinInfo {sc}: no entry names a missing head or a mismatching headID on this install (probe)");
            }

            if (!rows.HasVanilla) { t.Info("SKIP vanilla checks: Cache/maker/vanilla_lists.tsv absent (MakerListsDump.Run writes it)"); return; }

            // ---- vanilla census
            var vanilla = rows.Categories.SelectMany(c => rows.RowsOf(c)).Where(r => r.IsVanilla).ToList();
            t.Equal(1676, vanilla.Count, "census: vanilla rows incl. the gated ones");
            t.Equal(1661, vanilla.Count(r => !r.Gated), "census: vanilla rows kept by the game's gate");
            var gated = vanilla.Where(r => r.Gated).Select(r => r.Category + "#" + r.Key.Id).OrderBy(s => s, StringComparer.Ordinal).ToList();
            var want15 = new[] { "240#36", "240#37", "241#17", "241#18", "242#21", "243#17", "243#18", "244#6", "246#10", "247#14", "247#15", "351#1", "351#2", "359#5", "362#3" }
                .OrderBy(s => s, StringComparer.Ordinal).ToList();
            t.Check(gated.SequenceEqual(want15), "census: the 15 gated rows: " + string.Join(" ", gated));
            t.Check(rows.RowsOf(210).Where(r => r.IsVanilla).Select(r => r.Key.Id).SequenceEqual(new[] { 0, 1, 2, 3 }) &&
                    rows.RowsOf(110).Where(r => r.IsVanilla).Select(r => r.Key.Id).SequenceEqual(new[] { 0 }), "census: vanilla heads female 0-3, male 0");
            foreach (var (sc, want) in new[] { (211, "0:10 1:10 2:10 3:19"), (111, "0:8") })
            {
                var got = string.Join(" ", rows.RowsOf(sc).Where(r => r.IsVanilla).GroupBy(r => int.Parse(r["HeadID"])).OrderBy(g => g.Key).Select(g => g.Key + ":" + g.Count()));
                t.Equal(want, got, $"census: vanilla face skins {sc} per HeadID");
            }
            var p99 = vanilla.Where(r => r.Possess == 99).ToList();
            t.Check(p99.Count == 1 && p99[0].Category == 359, "Possess 99: one vanilla row, ao_back (" + string.Join(" ", p99) + ")");
            t.Check(p99.All(r => !L.Rows(r.Category, 1).Contains(r)), "Possess 99: hidden from the list");

            // ---- per category and sex = the inventory counts (vanilla shown + mods unique)
            int bad = 0;
            foreach (var (cat, sex, v, mods) in Inventory)
            {
                var l = L.Rows(cat, sex);
                int gv = l.Count(r => r.IsVanilla), gm = l.Count - gv;
                if (gv != v || gm != mods) { bad++; t.Info($"category {cat} sex {sex}: vanilla {gv} (expected {v}), mods {gm} (expected {mods})"); }
                if (MakerLists.HasList(cat, 1 - sex)) continue;
                if (L.Rows(cat, 1 - sex).Count != 0) { bad++; t.Info($"category {cat} listed for the other sex"); }
            }
            t.Equal(0, bad, $"lists: rows per category and sex equal the inventory ({Inventory.Length} categories)");
            t.Check(new[] { 242, 243, 245, 246 }.All(c => L.Rows(c, 0).Count == 0), "lists: no male lists for clothes kinds 2, 3, 5, 6");
            L.ShowGated = true;
            t.Equal(102, L.Rows(240, 1).Count(r => r.IsVanilla), "gate: ShowGated adds the gated fo_top rows (100 + 2)");
            L.ShowGated = false;

            // ---- skins per head on the real lists
            var h3 = L.Rows(211, 1, ListContext.ForHead(ContentId.Vanilla(3)));
            int h3First = rows.RowsOf(211).Count(r => !r.IsVanilla && L.HeadOf(r) == ContentId.Vanilla(3)), h3All = rows.RowsOf(211).Count(r => !r.IsVanilla && L.IsListedUnder(r, ContentId.Vanilla(3)));
            t.Info($"lists: head 3 = 19 vanilla skins + {h3First} mod skins whose first definition names it + {h3All - h3First} by a later definition (H3) = {h3.Count}");
            t.Equal(19 + h3All, h3.Count, "lists: head 3 = its 19 vanilla skins + the mod skins listed under it");
            int repeated = 0, extra = 0;
            foreach (int sc in new[] { 211, 111 })
            {
                repeated += rows.RepeatedDefinitions(sc)?.Count ?? 0;
                extra += L.SkinPairs(sc).Count() - L.Rows(sc, sc == 211 ? 1 : 0).Count(r => !L.HeadOf(r).IsEmpty);
            }
            t.Info($"H3: {repeated} skin references defined several times inside their zipmod; (skin, head) pairs added by their later definitions {extra}");

            // ---- timing (MakerLists.Rows for ft_skin_f with the head filter <= 2 ms)
            var heads = L.Rows(210, 1).ToList();
            var cold = new MakerLists(rows);
            var sw = Stopwatch.StartNew();
            cold.Rows(211, 1, ListContext.ForHead(heads[0].Key));
            double coldMs = sw.Elapsed.TotalMilliseconds;
            var ms = new List<double>();
            int n = 0;
            for (int i = 0; i < 200; i++)
            {
                sw.Restart();
                n += cold.Rows(211, 1, ListContext.ForHead(heads[i % heads.Count].Key)).Count;
                ms.Add(sw.Elapsed.TotalMilliseconds);
            }
            ms.Sort();
            double med = ms[ms.Count / 2];
            // cold: every list built from scratch (cache dropped) per call
            var coldList = new List<double>();
            for (int i = 0; i < 20; i++)
            {
                cold.Invalidate();
                sw.Restart();
                cold.Rows(211, 1, ListContext.ForHead(heads[i % heads.Count].Key));
                coldList.Add(sw.Elapsed.TotalMilliseconds);
            }
            coldList.Sort();
            double coldMed = coldList[coldList.Count / 2];
            t.Info($"timing: MakerLists.Rows(ft_skin_f, head) first call {coldMs:0.00} ms, rebuilt (Invalidate) median {coldMed:0.00} ms, warm median {med * 1000:0.0} us over 200 heads ({rows.RowsOf(211).Count} rows)");
            t.Check(med <= 2.0, $"timing: Rows(ft_skin_f, head) warm median {med:0.000} ms <= 2 ms");
            // the first call of a category builds the head index once per catalog load (FAIL above 2x)
            if (coldMed > 2.0) t.Info($"WARN timing: the first Rows(ft_skin_f, head) call builds the head index in {coldMed:0.00} ms (> 2 ms); later calls {med * 1000:0.0} us");
            t.Check(coldMed <= 10.0, $"timing: Rows(ft_skin_f, head) with the index rebuilt {coldMed:0.00} ms <= 10 ms (sanity bound; the 2 ms target is the call, WARN above)");
        }
    }

    public static class MakerHs2Tests
    {
        static readonly HashSet<int> MaleOnly = new HashSet<int> { 110, 111, 112, 121, 131, 132, 133, 140, 141, 144, 147 };

        public static void Run(TestRunner t)
        {
            var rows = InstallFixture.Get(t);
            if (rows == null) { t.Info("SKIP: " + InstallFixture.Reason); return; }
            if (!rows.HasVanilla) { t.Info("SKIP: no vanilla fixture"); return; }
            var dir = Path.Combine(t.FixturesDir ?? ".", "Cache", "compare", "maker", "lists");
            if (!Directory.Exists(dir)) { t.Info("SKIP: HS2's lists not recorded (" + dir + ", package H)"); return; }
            var L = new MakerLists(rows) { ShowGated = true };   // HS2 on this install lists the gated rows (H1)
            int cats = 0, catsSame = 0, orderSame = 0;
            foreach (var f in Directory.GetFiles(dir, "*.json").OrderBy(f => int.Parse(Path.GetFileNameWithoutExtension(f))))
            {
                var j = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(f));
                int cat = (int)j["category"];
                int sex = MaleOnly.Contains(cat) ? 0 : 1;
                var hs2 = j["rows"].Where(r => (int)r["possess"] != 99).Select(r => (string)r["ref"]).ToList();
                var ours = L.Rows(cat, sex).Select(r => r.Key.ToString()).ToList();
                cats++;
                var hs2Set = new HashSet<string>(hs2, StringComparer.OrdinalIgnoreCase);
                var oursSet = new HashSet<string>(ours, StringComparer.OrdinalIgnoreCase);
                // known differences: the KCOX plugin injects its "custom overlay" pattern row into 348 (plugin data: Phase 6);
                // a zipmod whose manifest says <game>AI Girl</game> is skipped by Sideloader in HS2 (the <game> rule: 5.2)
                int known = hs2Set.RemoveWhere(r => cat == 348 && r.EndsWith("#58947543", StringComparison.Ordinal)) + oursSet.RemoveWhere(r => r.StartsWith("ddaa33_saya#", StringComparison.Ordinal));
                if (known > 0) t.Info($"category {cat}: {known} known difference(s) left out (KCOX pattern row / <game>AI Girl</game> zipmod)");
                bool same = hs2Set.SetEquals(oursSet);
                if (same) catsSame++;
                else t.Info($"category {cat}: HS2 {hs2Set.Count} references, HS2Next {oursSet.Count}; only HS2: {string.Join(" ", hs2Set.Except(oursSet).Take(5))}; only HS2Next: {string.Join(" ", oursSet.Except(hs2Set).Take(5))}");
                // order: HS2's dictionary order (first occurrence of each reference) vs the catalog order
                var hs2Order = hs2.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (same && hs2Order.Where(oursSet.Contains).SequenceEqual(ours.Where(oursSet.Contains), StringComparer.OrdinalIgnoreCase)) orderSame++;
                if (FaceSkinHeads.IsSkinCategory(cat))
                {
                    var hp = new HashSet<string>(j["rows"].Where(r => (int)r["possess"] != 99).Select(r => (string)r["ref"] + " @ " + (string)r["headRef"]), StringComparer.OrdinalIgnoreCase);
                    var op = new HashSet<string>(L.SkinPairs(cat).Select(p => p.row.Key + " @ " + p.head), StringComparer.OrdinalIgnoreCase);
                    t.Check(hp.SetEquals(op), $"H3 {cat}: the (skin, head) pairs equal HS2's ({op.Count} pairs; only HS2 {hp.Except(op).Count()}: {string.Join(" ", hp.Except(op).Take(3))}; only HS2Next {op.Except(hp).Count()}: {string.Join(" ", op.Except(hp).Take(3))})");
                }
            }
            t.Equal(cats, catsSame, $"H1: the references of every Maker list equal HS2's ({catsSame} of {cats} categories; gate open as HS2 here, Possess 99 hidden)");
            t.Equal(cats, orderSame, $"order: every Maker list in HS2's order ({orderSame} of {cats}; vanilla rows first, zipmods by GUID in the invariant culture's order)");
        }
    }

    public static class MakerPresetTests
    {
        public static void Run(TestRunner t)
        {
            var rows = InstallFixture.Get(t);
            if (rows == null) { t.Info("SKIP: " + InstallFixture.Reason); return; }
            var samples = new[] { (210, new ContentId("cunihinx.ada4", 424)), (210, new ContentId("Darkruler.ElenaFisher", 166)), (210, new ContentId("mcm82.captainmmfr", 315)),
                (210, new ContentId("SelfishCrow.raven", 278)), (110, new ContentId("Supreme.Maleface", 38)) };
            int entries = 0;
            foreach (var (hc, key) in samples)
            {
                var head = rows.Find(hc, key);
                if (head == null) { t.Check(false, "presets: sample head installed: " + key); continue; }
                var e = HeadPresets.FindEntry(rows, head);
                if (e != null) entries++;
                if (rows.PresetMissing.Contains((hc, key))) { t.Info($"preset {key}: entry {(e == null ? "none" : e.ToString())}; the TextAsset {HeadPresets.PresetName(head)} is not in the head's MainAB (HS2's LoadFacePreset then loads nothing)"); continue; }
                if (!rows.PresetIds.TryGetValue((hc, key), out var ids)) { t.Info($"preset {key}: entry {(e == null ? "none" : e.ToString())}; ids not in the fixture (MakerListsDump writes them)"); continue; }
                var findings = new List<string>();
                var res = HeadPresets.ResolveAll(rows, head, hc == 110 ? 0 : 1, ids, findings);
                // Sideloader's rule restated: a property's ref is (entry GUID, id) when that row exists, else vanilla
                int bad = 0;
                foreach (var p in HeadPresets.Properties)
                {
                    if (!ids.TryGetValue(p.Member, out int id)) continue;
                    var g = HeadPresets.GuidOf(e, p);
                    var want = g != null && rows.Find(p.CategoryFor(hc == 110 ? 0 : 1), new ContentId(g, id)) != null ? new ContentId(g, id) : ContentId.Vanilla(id);
                    if (res[p.Member] != want) bad++;
                }
                string text = $"{key} ({(e == null ? "no entry" : e.Preset)}) resolves {res.Count} ids: " + string.Join(" ", res.Where(kv => !kv.Value.IsVanilla || kv.Value.Id != 0).Select(kv => kv.Key.Replace("face.", "") + "=" + kv.Value)) +
                    $" (the others vanilla #0); {findings.Count} unresolved" + (findings.Count > 0 ? ": " + string.Join("; ", findings.Select(f => f.Substring(0, f.IndexOf(':')))) : "");
                t.Info("preset " + text);
                t.Equal(0, bad, "presets: " + text);
            }
            foreach (var kv in rows.PresetIds.Where(kv => kv.Key.Item2.IsVanilla).OrderBy(kv => kv.Key.Item1).ThenBy(kv => kv.Key.Item2.Id))
            {
                var head = rows.Find(kv.Key.Item1, kv.Key.Item2);
                var res = HeadPresets.ResolveAll(rows, head, kv.Key.Item1 == 110 ? 0 : 1, kv.Value);
                t.Check(head != null && HeadPresets.FindEntry(rows, head) == null && res.Values.All(r => r.IsVanilla),
                    $"presets: vanilla head {kv.Key.Item2} ({kv.Key.Item1}): no entry, every id vanilla: " + string.Join(" ", res.Select(r => r.Key.Replace("face.", "") + "=" + r.Value.Id)));
            }
            t.Equal(4, entries, "presets: four of the five samples have an entry (Supreme.Maleface names preset_head_00, its entry preset_head_01)");
            CompareH4(t, rows);
        }

        // H4 key -> document member
        static readonly Dictionary<string, string> H4Members = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["headId"] = "face.head", ["skinId"] = "face.skin", ["detailId"] = "face.detail.item", ["eyebrowId"] = "face.eyebrow.item",
            ["pupil0.pupilId"] = "face.eyes.left.iris.item", ["pupil1.pupilId"] = "face.eyes.right.iris.item",
            ["pupil0.blackId"] = "face.eyes.left.pupil.item", ["pupil1.blackId"] = "face.eyes.right.pupil.item", ["hlId"] = "face.eyes.highlight.item",
            ["eyelashesId"] = "face.eyelashes.item", ["moleId"] = "face.mole.item", ["makeup.eyeshadowId"] = "face.makeup.eyeshadow.item",
            ["makeup.cheekId"] = "face.makeup.cheek.item", ["makeup.lipId"] = "face.makeup.lip.item",
            ["makeup.paint0.id"] = "face.makeup.paint[0].item", ["makeup.paint1.id"] = "face.makeup.paint[1].item",
            ["makeup.paint0.layoutId"] = "face.makeup.paint[0].layoutItem", ["makeup.paint1.layoutId"] = "face.makeup.paint[1].layoutItem",
        };

        // H4 (Cache/compare/maker/h4.json, package H): HS2's face preset pushes; `raw` = the preset's own ids, `ids` = HS2's result,
        // `via` = how HS2 got it. HS2Next resolves the raw ids with HeadPresets and must equal HS2 on every member HS2 resolved
        // through headPresetInfo or as a vanilla id; `compatibility` / `unlisted` members are Sideloader's fallbacks that HS2Next
        // does not reproduce (kept vanilla: confirmation point), reported, not judged; `noPreset` heads are not compared.
        static void CompareH4(TestRunner t, FixtureListRows rows)
        {
            var file = Path.Combine(t.FixturesDir ?? ".", "Cache", "compare", "maker", "h4.json");
            if (!File.Exists(file)) { t.Info("H4 not recorded yet (" + file + "): comparison pending"); return; }
            var arr = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(file));
            int heads = 0, equal = 0, fallback = 0, noPreset = 0, bad = 0;
            foreach (var h in arr)
            {
                int sex = (string)h["sex"] == "male" ? 0 : 1;
                var key = ContentId.Parse((string)h["head"]);
                var head = rows.Find(sex == 0 ? 110 : 210, key);
                if (head == null) { t.Check(false, "H4 head not installed: " + key); continue; }
                if (!(bool)h["presetFound"])
                {
                    noPreset++;
                    t.Check(rows.PresetMissing.Contains((sex == 0 ? 110 : 210, key)) || !rows.PresetIds.ContainsKey((sex == 0 ? 110 : 210, key)),
                        $"H4 {key}: HS2 found no preset TextAsset; the fixture agrees (or does not sample it)");
                    continue;
                }
                heads++;
                var raw = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var p in (Newtonsoft.Json.Linq.JObject)h["raw"]) if (H4Members.TryGetValue(p.Key, out var m)) raw[m] = (int)p.Value;
                var res = HeadPresets.ResolveAll(rows, head, sex, raw);
                int diff = 0;
                var notes = new List<string>();
                foreach (var p in (Newtonsoft.Json.Linq.JObject)h["ids"])
                {
                    if (!H4Members.TryGetValue(p.Key, out var m) || !res.TryGetValue(m, out var mine)) continue;
                    string via = (string)h["via"]?[p.Key] ?? "";
                    var theirs = ContentId.Parse((string)p.Value);
                    if (mine == theirs) continue;
                    if (via == "compatibility" || via == "unlisted") { fallback++; notes.Add($"{m}: HS2 {theirs} ({via}), HS2Next {mine}"); continue; }
                    diff++;
                    notes.Add($"{m}: HS2 {theirs} ({via}), HS2Next {mine} DIFFERS");
                }
                if (diff == 0) equal++; else bad++;
                t.Check(diff == 0, $"H4 {key} ({(string)h["preset"]}): HS2Next's preset refs equal HS2's" + (notes.Count > 0 ? " except Sideloader fallbacks: " + string.Join("; ", notes) : ""));
                if (notes.Count > 0) t.Info($"H4 {key}: " + string.Join("; ", notes));
            }
            t.Info($"H4: {heads} heads with a preset compared ({equal} equal on every judged member, {bad} differ), {noPreset} without a preset TextAsset, {fallback} members where HS2 used Sideloader's compatibility / unlisted fallback (not reproduced)");
        }
    }
}

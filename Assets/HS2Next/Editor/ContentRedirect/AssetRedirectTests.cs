// Unity-free tests of the per-asset redirect (Runtime/Content/AssetRedirect.cs) and of the zipmod scan cache
// (Runtime/Content/ZipmodSourceBase.cs), plain Mono: Tools/mono_tests.ps1 -Suite content (fixtures = the project root).
//   RedirectVersionTests   the manifest version order on a table of pairs, the file a GUID keeps, the game filter;
//   RedirectIndexTests     the index (exact trimmed paths, two or more sources, *.unity3d only, the case fallback), the rule
//                          (first copy holding the name, the abdata fallback, none -> the first copy, full-path names) and
//                          the read model;
//   RedirectNamesTests     the container reader on the committed fixture bundles (Editor/Fixtures/Redirect: uncompressed,
//                          LZ4, LZ4HC; built by AssetRedirectTest.BuildFixtures from this project's own text assets) and its
//                          LZMA refusal, the short-name rule, the names cache round trip and a truncated names cache;
//   RedirectScanCacheTests the zipmod scan on generated zipmods: the <game> lines and the game filter, the cache cut short
//                          at every column of its last line and inside its last data line (a cold scan, no exception, the
//                          same zipmods), the upgrade of a version 6 cache (games read from the manifests alone);
//   RedirectRewriteTests   the CAB rewrite of package R2 (Hs2BundleFile.Renamed) on the fixture bundles: every "CAB-" file name
//                          renamed with its length kept, the data equal but for the renamed names, the blocks stored, the
//                          container's names read again; a renamed name of another length and an LZMA bundle refused.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using HS2Next.IO;

namespace HS2Next.Content.Tests
{
    using HS2Next.Save.Tests;

    sealed class FakeZip : IRedirectZip
    {
        public string Path { get; set; }
        public string Guid { get; set; }
        public readonly List<string> Paths = new List<string>();
        public readonly Dictionary<string, BundleNames> Names = new Dictionary<string, BundleNames>(StringComparer.Ordinal);
        public IReadOnlyCollection<string> BundlePaths => Paths;
        public BundleNames NamesOf(string bundlePath) => Names.TryGetValue(bundlePath, out var n) ? n : null;

        public FakeZip(string guid) { Guid = guid; Path = guid + ".zipmod"; }

        public FakeZip Ship(string path, params string[] names)
        {
            Paths.Add(path);
            if (names.Length > 0) Names[path] = new BundleNames { Status = BundleNames.Kind.Ok, Names = new HashSet<string>(names.Select(BundleNames.ShortName), StringComparer.Ordinal) };
            return this;
        }
    }

    public static class RedirectVersionTests
    {
        public static void Run(TestRunner t)
        {
            // expected values cross-checked with the census's implementation (Tools/survey/maker_clothes_census.py cmp_versions)
            var pairs = new (string a, string b, int c)[]
            {
                ("1.0", "1.0.1", -1), ("v1.2", "1.2", 0), ("1.10", "1.9", 1), ("1.0a", "1.0", 1), ("", "0", 0), ("", "1", -1),
                ("1.a", "1.1", 1), ("1.0", "1", 0), ("r2", "v2", 0), ("1.0.0", "1.0", 0), ("1.2-beta", "1.2", 1), ("010", "9", 1),
                ("4.6", "3.1", 1), ("1.1.0", "1.1.0", 0), ("2.1", "4.1.5", -1), ("1_2", "1.2", 0), ("1,2", "1.3", -1), ("a", "b", -1),
                ("0.a", "0", 1), ("V1.0 beta", "1.0", 1), (null, "0", 0), ("  1.5 ", "1.5", 0),
            };
            foreach (var (a, b, c) in pairs)
            {
                t.Equal(c, SideloaderZipmods.CompareVersions(a, b), $"version {Q(a)} vs {Q(b)}");
                t.Equal(-c, SideloaderZipmods.CompareVersions(b, a), $"version {Q(b)} vs {Q(a)} (reversed)");
            }

            // the file a GUID keeps
            t.Equal(1, SideloaderZipmods.Choose(new[] { ("1.0", "a.zipmod", 9L), ("1.1", "b.zipmod", 0L) }), "the highest version");
            t.Equal(1, SideloaderZipmods.Choose(new[] { ("1.0", "ab.zipmod", 0L), ("v1.0", "abc.zipmod", 0L) }), "equal versions: the longer file name");
            t.Equal(0, SideloaderZipmods.Choose(new[] { ("1.0", "same.zipmod", 1L), ("1.0", "same.zipmod", 2L) }), "a complete tie keeps the earliest file of the scan");
            t.Equal(1, SideloaderZipmods.Choose(new[] { ("1.0", "a.zipmod", 5L), ("", "b.zipmod", 9L) }), "a file without a version: the newest last-write time");
            t.Equal(0, SideloaderZipmods.Choose(new[] { (null, "a.zipmod", 9L), ("2.0", "b.zipmod", 5L) }), "a file without a version: the newest even with a lower version");
            t.Equal(0, SideloaderZipmods.Choose(new[] { ((string)null, "a.zipmod", 5L), ((string)null, "b.zipmod", 5L) }), "equal times keep the earliest file");
            t.Equal(-1, SideloaderZipmods.Choose(new (string, string, long)[0]), "no file");

            // the game filter
            t.Check(SideloaderZipmods.LoadsOnHs2(null), "no <game> list loads");
            t.Check(SideloaderZipmods.LoadsOnHs2(new string[0]), "an empty <game> list loads");
            t.Check(SideloaderZipmods.LoadsOnHs2(new[] { " ", "" }), "blank <game> values only: loads");
            t.Check(!SideloaderZipmods.LoadsOnHs2(new[] { "ai girl" }), "AI Girl only: skipped");
            t.Check(!SideloaderZipmods.LoadsOnHs2(new[] { "Koikatsu", "AI Girl" }), "other games only: skipped");
            t.Check(SideloaderZipmods.LoadsOnHs2(new[] { "AI Girl", "HS2" }), "AI Girl and HS2: loads");
            t.Check(SideloaderZipmods.LoadsOnHs2(new[] { "Honey Select 2" }), "Honey Select 2 (any case): loads");
            t.Check(SideloaderZipmods.LoadsOnHs2(new[] { " honeyselect2 " }), "honeyselect2 (trimmed): loads");
            t.Check(!SideloaderZipmods.LoadsOnHs2(new[] { "honey select" }), "honey select (the first game): skipped");
        }

        static string Q(string s) => s == null ? "null" : "\"" + s + "\"";
    }

    public static class RedirectIndexTests
    {
        public static void Run(TestRunner t)
        {
            var za = new FakeZip("a").Ship("chara/p.unity3d", "x", "y").Ship("chara/q.unity3d", "k").Ship("list/foo.csv")
                                     .Ship("chara/two.unity3d", "a1").Ship("chara/abd.unity3d", "m");
            var zb = new FakeZip("b").Ship("chara/p.unity3d", "Y", "z").Ship("chara/two.unity3d", "b1").Ship("Chara/Case.unity3d", "c")
                                     .Ship("list/foo.csv").Ship("chara/nonames.unity3d");
            var zc = new FakeZip("c").Ship("chara/nonames.unity3d", "n");
            var abdata = new HashSet<string>(new[] { "chara/p.unity3d", "chara/abd.unity3d", "chara/only.unity3d" }, StringComparer.OrdinalIgnoreCase);
            var r = AssetRedirect.Build(new IRedirectZip[] { za, zb, zc }, abdata.Contains);

            t.Equal("chara/abd.unity3d|chara/nonames.unity3d|chara/p.unity3d|chara/two.unity3d", string.Join("|", r.RedirectablePaths), "redirectable paths: abdata + a copy, or two copies; *.unity3d only");
            t.Check(!r.IsRedirectable("chara/q.unity3d"), "one zipmod copy, no abdata file: not redirectable");
            t.Check(!r.IsRedirectable("chara/only.unity3d"), "an abdata file alone: not redirectable");
            t.Check(!r.IsRedirectable("list/foo.csv"), "a CSV two zipmods ship: not indexed");
            t.Check(!r.IsRedirectable("Chara/Case.unity3d"), "a path of one zipmod: not redirectable");
            t.Check(r.IsRedirectable("CHARA/P.UNITY3D"), "another case: the case-insensitive fallback");
            t.Equal("chara/p.unity3d", r.Exact("Chara/P.unity3d"), "the exact indexed path of another case");
            t.Equal("chara/p.unity3d", r.Exact("chara\\p.unity3d"), "backslashes are separators");
            t.Equal(2, r.CopiesOf("chara/p.unity3d").Count, "copies in load order");
            t.Check(ReferenceEquals(r.CopiesOf("chara/p.unity3d")[0], za), "load order: the first zipmod given first");

            // the rule
            var s = r.Serve("chara/p.unity3d", "x");
            t.Check(ReferenceEquals(s.Zip, za) && s.HasName, "a name only the first copy holds: the first copy");
            s = r.Serve("chara/p.unity3d", "Y");
            t.Check(ReferenceEquals(s.Zip, za), "a name both copies hold (any case): the first copy in load order");
            s = r.Serve("chara/p.unity3d", "z");
            t.Check(ReferenceEquals(s.Zip, zb), "a name only the second copy holds: the second copy");
            s = r.Serve("chara/p.unity3d", "assets/mods/Z.prefab");
            t.Check(ReferenceEquals(s.Zip, zb), "a full asset path matches by its short name");
            s = r.Serve("chara/p.unity3d", "w");
            t.Check(s.Abdata && s.Zip == null && !s.HasName, "a name no copy holds: the abdata file");
            s = r.Serve("chara/two.unity3d", "nothing");
            t.Check(ReferenceEquals(s.Zip, za) && !s.HasName, "no copy holds it and no abdata file: the first copy (missing)");
            s = r.Serve("chara/abd.unity3d", "m");
            t.Check(ReferenceEquals(s.Zip, za), "an abdata path: the zipmod copy holding the name");
            s = r.Serve("CHARA/P.UNITY3D", "z");
            t.Check(ReferenceEquals(s.Zip, zb), "a request in another case uses the fallback");
            s = r.Serve("chara/nonames.unity3d", "n");
            t.Check(ReferenceEquals(s.Zip, zc), "a copy without names is passed over");
            t.Check(r.Serve("chara/none.unity3d", "x").None, "an unknown path: nothing (the catalog's own source applies)");

            // names read on demand for a copy the scan could not read
            int asked = 0;
            r.ReadMissingNames = (z, p) => { asked++; return new BundleNames { Status = BundleNames.Kind.Ok, Names = new HashSet<string> { "n", "o" } }; };
            s = r.Serve("chara/nonames.unity3d", "o");
            t.Check(ReferenceEquals(s.Zip, zb) && asked == 1, "names read on demand serve, in load order");
            r.Serve("chara/nonames.unity3d", "o");
            t.Equal(1, asked, "names read on demand are kept");
            t.Equal(1, r.LateNames.Count(), "late names listed for the cache");

            // read model
            var x = r.Explain("chara/p.unity3d", "z", "C:/abdata/chara/p.unity3d", new AssetRedirect.Served { Abdata = true });
            t.Check(x.Redirectable && x.HasAbdata && x.Copies.Count == 3, "explain: redirectable, the abdata file and two copies");
            t.Check(x.Copies[0].Guid == null && x.Copies[1].Guid == "a" && x.Copies[2].Guid == "b", "explain: the abdata file first, then load order");
            t.Check(x.Copies[1].Has == false && x.Copies[2].Has == true, "explain: which copy holds the name");
            t.Check(x.ServedKind == "zipmod" && x.ServedGuid == "b" && x.TodayKind == "abdata", "explain: served by the second copy, today the abdata file");
            t.Equal("a1|b1", string.Join("|", r.AllNames("chara/two.unity3d")), "every name of a path");

            // stats
            t.Check(r.NoteRedirect("chara/p.unity3d", "z", s, new AssetRedirect.Served { Abdata = true }), "the first redirect of a path is reported");
            t.Check(!r.NoteRedirect("chara/p.unity3d", "x", s, new AssetRedirect.Served { Abdata = true }), "a later one is counted only");
            t.Equal(2, r.Stats["chara/p.unity3d"], "redirects per path");
            t.Equal(2, r.TakeRecent().Count, "recent redirects taken");
            t.Equal(0, r.TakeRecent().Count, "and cleared");
        }
    }

    public static class RedirectNamesTests
    {
        public static void Run(TestRunner t)
        {
            t.Equal("p_cf_body_00", BundleNames.ShortName("assets/illusion/assetbundle/prefabs/chara/female/p_cf_body_00.prefab"), "short name of a container key");
            t.Equal("tex.v2", BundleNames.ShortName("Assets/A/Tex.v2.png"), "only the last extension is removed, lower case");
            t.Equal("noext", BundleNames.ShortName("NoExt"), "a name without folder or extension");

            string dir = t.FixturesDir == null ? null : Path.Combine(t.FixturesDir, "Assets", "HS2Next", "Editor", "Fixtures", "Redirect");
            var expected = new[] { "redirect_alpha", "redirect_beta", "redirect_gamma" };
            foreach (var (file, comp) in new[] { ("redirect_none.unity3d", "none"), ("redirect_lz4.unity3d", "LZ4"), ("redirect_lz4hc.unity3d", "LZ4HC") })
            {
                string f = dir == null ? null : Path.Combine(dir, file);
                if (f == null || !File.Exists(f)) { t.Check(false, "fixture bundle " + file + " present (AssetRedirectTest.BuildFixtures)"); continue; }
                var n = BundleNames.ReadFile(f, 0, new FileInfo(f).Length);
                t.Equal(BundleNames.Kind.Ok, n.Status, file + ": read (" + n.Error + ")");
                t.Equal(string.Join(",", expected), n.Names == null ? null : string.Join(",", n.Names.OrderBy(x => x, StringComparer.Ordinal)), file + ": the container's short names");
                t.Equal(comp, n.Compression, file + ": compression");
                t.Check(n.SerializedFiles != null && n.SerializedFiles.StartsWith("CAB-", StringComparison.Ordinal), file + ": the serialized file's CAB name (" + n.SerializedFiles + ")");
                // the same bundle inside a zip: stored (in place) and deflated (in memory)
                foreach (var level in new[] { CompressionLevel.NoCompression, CompressionLevel.Optimal })
                {
                    string zip = Path.Combine(t.TempDir, "names_" + level + "_" + file + ".zipmod");
                    using (var za = ZipFile.Open(zip, ZipArchiveMode.Create)) za.CreateEntryFromFile(f, "abdata/chara/" + file, level);
                    var zn = BundleNames.ReadZipEntry(new ZipIndex(zip), "abdata/chara/" + file);
                    t.Check(zn.Status == BundleNames.Kind.Ok && zn.Count == expected.Length, file + ": read from a zip entry (" + level + ")");
                }
            }
            string lzma = dir == null ? null : Path.Combine(dir, "redirect_lzma.unity3d");
            if (lzma == null || !File.Exists(lzma)) t.Check(false, "fixture bundle redirect_lzma.unity3d present");
            else
            {
                var n = BundleNames.ReadFile(lzma, 0, new FileInfo(lzma).Length);
                t.Equal(BundleNames.Kind.Lzma, n.Status, "an LZMA bundle is refused (read later through Unity)");
                t.Check(n.Names == null, "and has no names");
            }
            var bad = Path.Combine(t.TempDir, "notabundle.unity3d");
            File.WriteAllBytes(bad, Encoding.ASCII.GetBytes("UnityWeb\0 not a UnityFS bundle"));
            var bn = BundleNames.ReadFile(bad, 0, new FileInfo(bad).Length);
            t.Check(bn.Status == BundleNames.Kind.Unreadable && bn.Error != null, "a file that is not a UnityFS bundle: unreadable, no exception");

            // the names cache
            string cache = Path.Combine(t.TempDir, "bundlenames-test.tsv");
            var entries = new List<(string, string, string, BundleNames)>
            {
                ("C:/mods/a.zipmod", "10:20", "chara/p.unity3d", new BundleNames { Status = BundleNames.Kind.Ok, Names = new HashSet<string> { "x", "y" }, Compression = "LZ4HC", SerializedFiles = "CAB-1", ResourceFiles = "CAB-1.resS", Externals = "cab-2" }),
                ("C:/mods/b.zipmod", "11:21", "chara/p.unity3d", new BundleNames { Status = BundleNames.Kind.Lzma, Compression = "LZMA", Error = "LZMA blocks" }),
                ("C:/mods/c.zipmod", "12:22", "chara/e.unity3d", new BundleNames { Status = BundleNames.Kind.Ok, Names = new HashSet<string>(), Externals = "" }),
            };
            t.Check(BundleNamesCache.Write(cache, entries) == null, "names cache written");
            t.Check(!File.Exists(cache + ".tmp"), "no temporary file left");
            var back = BundleNamesCache.Read(cache, out var err);
            t.Check(err == null && back.Count == 3, "names cache read back (" + err + ")");
            t.Check(back.TryGetValue(("C:/mods/a.zipmod", "chara/p.unity3d"), out var ea) && ea.stamp == "10:20" && ea.names.Contains("y") && ea.names.Count == 2
                    && ea.names.Compression == "LZ4HC" && ea.names.ResourceFiles == "CAB-1.resS" && ea.names.Externals == "cab-2", "an entry round-trips");
            t.Check(back.TryGetValue(("C:/mods/b.zipmod", "chara/p.unity3d"), out var eb) && eb.names.Status == BundleNames.Kind.Lzma && eb.names.Names == null && eb.names.Error == "LZMA blocks", "an LZMA entry round-trips");
            t.Check(back.TryGetValue(("C:/mods/c.zipmod", "chara/e.unity3d"), out var ec) && ec.names.Status == BundleNames.Kind.Ok && ec.names.Count == 0, "an empty container round-trips");
            var text = File.ReadAllText(cache);
            int lastLine = text.TrimEnd('\n').LastIndexOf('\n') + 1;
            int discarded = 0, cuts = 0;
            for (int cut = lastLine; cut < text.Length - 1; cut++)
            {
                cuts++;
                File.WriteAllText(cache, text.Substring(0, cut));
                Dictionary<(string, string), (string, BundleNames)> d = null;
                try { d = BundleNamesCache.Read(cache, out err); }
                catch (Exception e) { t.Check(false, "truncated names cache threw " + e.Message); continue; }
                if (d.Count == 0 && err != null) discarded++;
            }
            t.Equal(cuts, discarded, "a names cache cut inside its last line is discarded (" + cuts + " cuts)");
            File.WriteAllText(cache, "#hs2next-bundlenames\t0\nP\tx\n");
            t.Check(BundleNamesCache.Read(cache, out err).Count == 0 && err != null, "another version: read cold");
        }
    }

    public static class RedirectRewriteTests
    {
        static int Count(byte[] data, byte[] pat)
        {
            int n = 0;
            for (int i = 0; i + pat.Length <= data.Length; i++)
            {
                int k = 0;
                while (k < pat.Length && data[i + k] == pat[k]) k++;
                if (k == pat.Length) { n++; i += pat.Length - 1; }
            }
            return n;
        }

        static byte[] Replace(byte[] data, byte[] from, byte[] to)
        {
            var r = (byte[])data.Clone();
            for (int i = 0; i + from.Length <= r.Length; i++)
            {
                int k = 0;
                while (k < from.Length && r[i + k] == from[k]) k++;
                if (k == from.Length) { Buffer.BlockCopy(to, 0, r, i, to.Length); i += from.Length - 1; }
            }
            return r;
        }

        public static void Run(TestRunner t)
        {
            string dir = t.FixturesDir == null ? null : Path.Combine(t.FixturesDir, "Assets", "HS2Next", "Editor", "Fixtures", "Redirect");
            foreach (var file in new[] { "redirect_none.unity3d", "redirect_lz4.unity3d", "redirect_lz4hc.unity3d" })
            {
                string f = dir == null ? null : Path.Combine(dir, file);
                if (f == null || !File.Exists(f)) { t.Check(false, "fixture bundle " + file + " present"); continue; }
                var raw = File.ReadAllBytes(f);
                var a = Hs2BundleFile.FromBytes(raw, file);
                var stems = a.Nodes.Select(n => n.Path.Split('.')[0]).Where(x => x.StartsWith("CAB-", StringComparison.Ordinal)).Distinct().ToList();
                t.Check(stems.Count >= 1, file + ": a CAB- file name to rename (" + string.Join(",", a.Nodes.Select(n => n.Path)) + ")");
                if (stems.Count == 0) continue;
                // the old data, all nodes in their order
                var oldData = a.Nodes.SelectMany(n => a.ReadRange(n, 0, (int)n.Size)).ToArray();
                var bytes = a.Renamed(x => "CAB-" + new string('e', x.Length - 4), out var renamed, out int replaced);
                t.Equal(stems.Count, renamed.Count, file + ": every CAB- stem renamed");
                var b = Hs2BundleFile.FromBytes(bytes, file + " (rewritten)");
                t.Equal(a.FormatVersion, b.FormatVersion, file + ": the format version kept");
                t.Equal(a.UnityRevision, b.UnityRevision, file + ": the Unity revision kept");
                t.Equal(0, b.DataCompression, file + ": the data blocks stored uncompressed");
                t.Equal(a.Nodes.Count, b.Nodes.Count, file + ": the same nodes");
                bool same = a.Nodes.Count == b.Nodes.Count;
                for (int i = 0; same && i < a.Nodes.Count; i++)
                {
                    var na = a.Nodes[i]; var nb = b.Nodes[i];
                    string stem = na.Path.Split('.')[0];
                    string want = renamed.TryGetValue(stem, out var to) ? to + na.Path.Substring(stem.Length) : na.Path;
                    same = nb.Path == want && nb.Offset == na.Offset && nb.Size == na.Size && nb.Flags == na.Flags;
                }
                t.Check(same, file + ": node names renamed, offsets / sizes / flags kept");
                var newData = b.Nodes.SelectMany(n => b.ReadRange(n, 0, (int)n.Size)).ToArray();
                var expect = oldData;
                int occurrences = 0;
                foreach (var kv in renamed)
                {
                    var from = Encoding.UTF8.GetBytes(kv.Key);
                    occurrences += Count(oldData, from);
                    expect = Replace(expect, from, Encoding.UTF8.GetBytes(kv.Value));
                    t.Equal(0, Count(newData, from), file + ": the old name " + kv.Key + " gone from the data");
                }
                t.Check(newData.SequenceEqual(expect), file + ": the data equal to the old data with the names replaced");
                t.Equal(occurrences, replaced, file + ": the replaced occurrences counted");
                string tmp = Path.Combine(t.TempDir, "rewritten_" + file);
                File.WriteAllBytes(tmp, bytes);
                var names = BundleNames.ReadFile(tmp, 0, bytes.LongLength);
                t.Check(names.Status == BundleNames.Kind.Ok && names.Count == 3 && names.Compression == "none", file + ": the rewritten bundle's container read (" + names.Error + ")");
                t.Equal(renamed[stems[0]], names.SerializedFiles?.Split(',')[0], file + ": the serialized file carries the new name");
                bool threw = false;
                try { a.Renamed(x => x + "0", out _, out _); }
                catch (Hs2SerializedFileException) { threw = true; }
                t.Check(threw, file + ": a renamed name of another length is refused");
                bool kept = a.Renamed(x => null, out var none, out int r0).Length > 0 && none.Count == 0 && r0 == 0;
                t.Check(kept, file + ": no rename: the bundle rewritten uncompressed, nothing replaced");
            }
            string lzma = dir == null ? null : Path.Combine(dir, "redirect_lzma.unity3d");
            if (lzma != null && File.Exists(lzma))
            {
                bool threw = false;
                try { Hs2BundleFile.FromBytes(File.ReadAllBytes(lzma), "lzma").Renamed(x => "CAB-" + new string('e', x.Length - 4), out _, out _); }
                catch (Hs2SerializedFileException) { threw = true; }
                t.Check(threw, "an LZMA bundle is not rewritten (the catalog then serves the open copy)");
            }
        }
    }

    public static class RedirectScanCacheTests
    {
        sealed class DirSource : ZipmodSourceBase
        {
            public DirSource(string dir) : base("test", "test", dir) { }
            public override IEnumerable<ListEntry> ReadLists(IBundleCache cache) => Enumerable.Empty<ListEntry>();
            public override bool HasBundle(string path) => false;
            public override UnityEngine.AssetBundle OpenBundle(string path, IBundleCache cache) => null;
        }

        static void Zip(string file, string guid, string version, string[] games, int rows, string extra = null)
        {
            using (var z = ZipFile.Open(file, ZipArchiveMode.Create))
            {
                var m = new StringBuilder("<manifest schema-ver=\"1\"><guid>" + guid + "</guid><name>" + guid + "</name>" + (version == null ? "" : "<version>" + version + "</version>"));
                foreach (var g in games ?? new string[0]) m.Append("<game>").Append(g).Append("</game>");
                if (extra != null) m.Append(extra);
                m.Append("</manifest>");
                Text(z, "manifest.xml", m.ToString());
                var csv = new StringBuilder("240\n0\nTest\nID,Kind,Possess,Name,MainAB,MainData\n");
                for (int i = 0; i < rows; i++) csv.Append(i + 100).Append(",0,0,Row").Append(i).Append(",chara/").Append(guid).Append(".unity3d,p_").Append(i).Append('\n');
                Text(z, "abdata/list/characustom/" + guid + "_list.csv", csv.ToString());
                Text(z, "abdata/chara/" + guid + ".unity3d", "not a bundle");
            }
        }

        static void Text(ZipArchive z, string name, string text)
        {
            using (var w = new StreamWriter(z.CreateEntry(name).Open(), new UTF8Encoding(false))) w.Write(text);
        }

        static string Shape(ZipmodScanResult r) => string.Join(";", r.Zipmods.Select(z =>
            $"{Path.GetFileName(z.Path)}:{z.Guid}:{z.Version}:{(z.Games == null ? "-" : string.Join("+", z.Games))}:{z.SkippedBy}:{z.Entries.Count}:{z.BundlePaths.Count}:{string.Join("+", z.ManifestElements)}"));

        public static void Run(TestRunner t)
        {
            string mods = Path.Combine(t.TempDir, "scan_mods");
            Directory.CreateDirectory(mods);
            Zip(Path.Combine(mods, "a.zipmod"), "test.a", "1.0", null, 3, "<pluginBlock kind=\"x\"><item value=\"1\" /><item value=\"2\" /></pluginBlock><pluginBlock kind=\"y\" />");
            Zip(Path.Combine(mods, "ai.zipmod"), "test.ai", "1.0", new[] { "AI Girl" }, 2);
            Zip(Path.Combine(mods, "both.zipmod"), "test.both", "2", new[] { "AI Girl", "HS2" }, 1);
            File.WriteAllText(Path.Combine(mods, "noguid.zip"), "not a zip");
            string cache = Path.Combine(t.TempDir, "modlists-test.tsv");
            var src = new DirSource(mods);

            var cold = src.ScanZipmods(cache);
            t.Equal(4, cold.Scanned, "cold scan reads every file");
            t.Equal(1, cold.SkippedByGame, "one zipmod skipped by the game filter");
            var ai = cold.Zipmods.First(z => z.Guid == "test.ai");
            t.Check(ai.SkippedBy == "game" && ai.Entries.Count == 0 && ai.BundlePaths.Count == 0, "a skipped zipmod has no rows and no bundle paths");
            t.Equal("AI Girl", ai.Games == null ? null : string.Join("+", ai.Games), "its <game> values are kept");
            var a = cold.Zipmods.First(z => z.Guid == "test.a");
            t.Check(a.SkippedBy == null && a.Entries.Count == 3 && a.Games != null && a.Games.Count == 0 && a.Stamp != null, "a zipmod without <game> loads with its rows");
            t.Check(cold.Zipmods.First(z => z.Guid == "test.both").SkippedBy == null, "a zipmod naming HS2 among others loads");
            t.Equal("guid+name+version+pluginBlock", string.Join("+", a.ManifestElements), "the manifest's top-level element names, distinct, in manifest order");
            t.Check(a.HasManifestElement("pluginBlock") && !a.HasManifestElement("item") && ai.ManifestElements.Count == 0, "an element below the root is not listed; a skipped zipmod lists none");
            var blocks = a.ReadManifestElements("pluginBlock");
            t.Check(blocks.Count == 2 && blocks[0].Contains("<item value=\"2\"") && a.ReadManifestElement("pluginBlock") == blocks[0] && a.ReadManifestElement("nothing") == null,
                    "an element's XML is read from the zip on demand (every one of that name, the first alone)");
            string reference = Shape(cold);
            string text = File.ReadAllText(cache);
            t.Check(text.StartsWith(ZipmodSourceBase.CacheHeader + "\n", StringComparison.Ordinal) && ZipmodSourceBase.CacheHeader.EndsWith("\t8", StringComparison.Ordinal)
                    && text.EndsWith("#end\t4\n", StringComparison.Ordinal), "the cache has the version 8 header and its end line");
            t.Check(text.Contains("\nW\tAI Girl\n") && text.Contains("\nW\tAI Girl\tHS2\n"), "the cache has the <game> lines");
            t.Check(text.Contains("\nT\tguid\tname\tversion\tpluginBlock\n") && text.Contains("\nT\tguid\tname\tversion\tgame\n"), "the cache has the element lines");
            t.Check(!File.Exists(cache + ".tmp"), "no temporary file left");

            var warm = src.ScanZipmods(cache);
            t.Check(warm.Scanned == 0 && !warm.CacheDiscarded && Shape(warm) == reference, "a cached scan reads nothing and gives the same zipmods");
            src.GameFilter = false;
            var off = src.ScanZipmods(cache);
            t.Check(off.Scanned == 0 && off.SkippedByGame == 0 && off.Zipmods.First(z => z.Guid == "test.ai").Entries.Count == 2, "the game filter off: the same cache, the zipmod loads (no cold scan)");
            src.GameFilter = true;

            // cut short: every column of the last line, and inside the last data line
            int lastLine = text.TrimEnd('\n').LastIndexOf('\n') + 1;
            int prevLine = text.Substring(0, lastLine - 1).LastIndexOf('\n') + 1;
            var cuts = Enumerable.Range(lastLine, text.Length - 1 - lastLine).Concat(Enumerable.Range(prevLine + 1, lastLine - prevLine - 2)).ToList();
            int ok = 0;
            foreach (int cut in cuts)
            {
                File.WriteAllText(cache, text.Substring(0, cut));
                ZipmodScanResult r = null;
                try { r = src.ScanZipmods(cache); }
                catch (Exception e) { t.Check(false, $"scan of a cache cut at {cut} threw {e.GetType().Name}: {e.Message}"); continue; }
                if (r.CacheDiscarded && r.Scanned == 4 && Shape(r) == reference && r.Errors.Any(x => x.Contains("ignored"))) ok++;
                else t.Info($"cut at {cut}: discarded {r.CacheDiscarded}, scanned {r.Scanned}, same {Shape(r) == reference}");
            }
            t.Equal(cuts.Count, ok, $"a cache cut short is ignored with one message and rescanned cold ({cuts.Count} cuts)");
            t.Check(File.ReadAllText(cache) == text, "the rescan wrote the complete cache again");

            // a version 6 cache: no <game> or element lines, no end line; upgraded by reading each manifest alone
            var v6 = new StringBuilder("#hs2next-modlists\t6\n");
            foreach (var line in text.Split('\n').Skip(1))
                if (line.Length > 0 && !line.StartsWith("W\t", StringComparison.Ordinal) && line != "W" && !line.StartsWith("T\t", StringComparison.Ordinal) && line != "T"
                    && !line.StartsWith("#end", StringComparison.Ordinal)) v6.Append(line).Append('\n');
            File.WriteAllText(cache, v6.ToString());
            var up = src.ScanZipmods(cache);
            t.Check(up.Scanned == 0 && up.GamesRead == 3 && up.ElementsRead == 3 && !up.CacheDiscarded,
                    $"a version 6 cache: nothing rescanned, 3 manifests read (scanned {up.Scanned}, games {up.GamesRead}, elements {up.ElementsRead})");
            t.Equal(reference, Shape(up), "the upgraded scan gives the same zipmods");
            t.Check(File.ReadAllText(cache) == text, "the upgraded cache equals a cold version 8 cache");

            // a version 7 cache: <game> lines and the end line, no element lines, a declaration line of one plugin (U): the
            // element names are read from each manifest alone and the U line is dropped
            var v7 = new StringBuilder("#hs2next-modlists\t7\n");
            foreach (var line in text.Split('\n').Skip(1))
            {
                if (line.Length == 0 || line.StartsWith("T\t", StringComparison.Ordinal) || line == "T") continue;
                v7.Append(line).Append('\n');
                if (line.StartsWith("N\ttest.a\t", StringComparison.Ordinal)) v7.Append("U\tbody\tguid\tsome.body\tfile\tchara/x.unity3d\n");
            }
            File.WriteAllText(cache, v7.ToString());
            var up7 = src.ScanZipmods(cache);
            t.Check(up7.Scanned == 0 && up7.GamesRead == 0 && up7.ElementsRead == 3 && !up7.CacheDiscarded,
                    $"a version 7 cache: nothing rescanned, the elements of 3 manifests read (scanned {up7.Scanned}, games {up7.GamesRead}, elements {up7.ElementsRead})");
            t.Equal(reference, Shape(up7), "the upgraded version 7 scan gives the same zipmods");
            t.Check(File.ReadAllText(cache) == text, "the upgraded version 7 cache equals a cold version 8 cache (the U line dropped)");
            var again = src.ScanZipmods(cache);
            t.Check(again.Scanned == 0 && again.ElementsRead == 0 && again.GamesRead == 0 && File.ReadAllText(cache) == text, "the upgraded cache is read as it is (nothing read again)");

            // a broken line in the middle (not at the end)
            File.WriteAllText(cache, text.Replace("\nE\t240\t101\t", "\nE\tx240\t101\t"));
            var broken = src.ScanZipmods(cache);
            t.Check(broken.CacheDiscarded && broken.Scanned == 4 && Shape(broken) == reference, "a line with a bad number: the cache is ignored, a cold scan");

            // several files of one GUID: the file the loader keeps (GUIDs grouped without regard to case)
            string dups = Path.Combine(t.TempDir, "scan_dups");
            Directory.CreateDirectory(dups);
            Zip(Path.Combine(dups, "dup_old.zipmod"), "test.dup", "1.0", null, 1);
            Zip(Path.Combine(dups, "dup_new.zipmod"), "TEST.DUP", "1.1", null, 1);
            Zip(Path.Combine(dups, "nover_a.zipmod"), "test.nover", null, null, 1);
            Zip(Path.Combine(dups, "nover_b.zipmod"), "test.nover", "2.0", null, 1);
            File.SetLastWriteTimeUtc(Path.Combine(dups, "nover_a.zipmod"), new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(Path.Combine(dups, "nover_b.zipmod"), new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc));
            Zip(Path.Combine(dups, "ai_twin.zipmod"), "test.dup", "9.0", new[] { "AI Girl" }, 1);
            var ds = new DirSource(dups).ScanZipmods(null);
            SideloaderZipmods.MarkOlderFiles(new[] { (IReadOnlyList<ZipmodInfo>)ds.Zipmods }, true);
            string S(string f) => ds.Zipmods.First(z => Path.GetFileName(z.Path) == f).SkippedBy ?? "loaded";
            t.Equal("version|loaded|loaded|version|game", string.Join("|", S("dup_old.zipmod"), S("dup_new.zipmod"), S("nover_a.zipmod"), S("nover_b.zipmod"), S("ai_twin.zipmod")),
                    "the higher version of a GUID in any case; without a version on every file the newest; a game-filtered twin takes no part");
            SideloaderZipmods.MarkOlderFiles(new[] { (IReadOnlyList<ZipmodInfo>)ds.Zipmods }, false);
            t.Check(ds.Zipmods.All(z => z.SkippedBy != "version"), "the set off clears the version marks");
        }
    }
}

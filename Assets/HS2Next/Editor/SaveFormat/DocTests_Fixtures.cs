// Format v1 freeze (Phase 4.6): the committed pre-release fixtures of FixtureWriter
// (Tests/SaveFormat/fixtures/v02_dev1/, prerelease "dev.1", written before the freeze and never regenerated) load as
// format v1 with no finding of their own (F1), hold the golden documents byte for byte (F2), re-save with the frozen
// writer as prerelease null with createdUtc and the documents kept (F3) and then round-trip byte-identically (F4); an
// unknown pre-release value is still read best effort with an info (F5); the canonical float text is guarded by the
// golden texts (F7). The node-block case of a retired namespace (F6) is a scene-suite test (SceneTests_Convert). Group
// Coordinate (DocTests_Coordinate): the coordinate file of a character (extract / apply / save, T-C1..T-C3).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using static HS2Next.Save.Tests.InfraFixtures;

namespace HS2Next.Save.Tests
{
    public static class DocTests_FixturesFreeze
    {
        static readonly DateTime ResaveClock = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

        static SaveOptions Resave() => new SaveOptions { Clock = () => ResaveClock, Thumbnail = ThumbnailMode.Keep, Registry = new ExtRegistry(), AppVersion = "0.3.0" };
        static LoadOptions Load() => new LoadOptions { Registry = new ExtRegistry() };

        /// <summary>Fixture name → (main document, loader, golden documents by entry name).</summary>
        sealed class Kind
        {
            public string File, Main;
            public Func<byte[], LoadResult<ContainerBase>> Load;
            public Dictionary<string, string> Docs;
        }

        static LoadResult<ContainerBase> Wrap<T>(LoadResult<T> r) where T : ContainerBase =>
            new LoadResult<ContainerBase> { Value = r.Value, Error = r.Error, Findings = r.Findings, NewerFormat = r.NewerFormat, MissingRequiredExt = r.MissingRequiredExt };

        static IEnumerable<Kind> Kinds()
        {
            string node = "charas/" + FixtureWriter.V02CharaNode + "/";
            yield return new Kind
            {
                File = "chara.hs2nchara", Main = CharaDocument.EntryName, Load = b => Wrap(CharaContainer.Load(b, Load())),
                Docs = new Dictionary<string, string> { ["chara.json"] = "chara.json", ["coord/coord.json"] = "coord.json" },
            };
            yield return new Kind
            {
                File = "coord.hs2ncoord", Main = CoordDocument.EntryName, Load = b => Wrap(CoordContainer.Load(b, Load())),
                Docs = new Dictionary<string, string> { ["coord.json"] = "coord.json" },
            };
            yield return new Kind
            {
                File = "scene.hs2nscene", Main = SceneDocument.EntryName, Load = b => Wrap(SceneContainer.Load(b, Load())),
                Docs = new Dictionary<string, string> { ["scene.json"] = "scene.json", [node + "chara.json"] = "chara.json", [node + "coord/coord.json"] = "coord.json" },
            };
            yield return new Kind
            {
                File = "pose.hs2npose", Main = PoseDocument.EntryName, Load = b => Wrap(PoseContainer.Load(b, Load())),
                Docs = new Dictionary<string, string> { ["pose.json"] = "pose.json" },
            };
        }

        public static void Run(TestRunner t)
        {
            DocTests_T1Golden.FixturesDir = t.FixturesDir;
            string dir = FixtureWriter.V02Dir(t.FixturesDir ?? Path.Combine(DocTests_T1Golden.GoldenDir, ".."));
            foreach (var k in Kinds())
            {
                string path = Path.Combine(dir, k.File);
                if (!File.Exists(path)) { t.Check(false, k.File + ": fixture present in " + dir); continue; }
                Fixture(t, k, File.ReadAllBytes(path));
            }
            FloatText(t);
            t.Check(!SaveFormats.IsAcceptedPrerelease("dev.2") && SaveFormats.IsAcceptedPrerelease("dev.1") && SaveFormats.IsAcceptedPrerelease(null) && SaveFormats.Prerelease == null,
                "the frozen constants: Prerelease null, dev.1 and null accepted, dev.2 not");
            t.Check(SaveFormats.FormatVersion == 1 && SaveFormats.MinReaderVersion == 1, "formatVersion and minReaderVersion stay 1 at the freeze");
        }

        static void Fixture(TestRunner t, Kind k, byte[] file)
        {
            string w = k.File + ": ";
            var raw = ContainerReader.Read(file);
            var m0 = Manifest(raw);
            t.Check((string)m0["prerelease"] == "dev.1" && (int)m0["formatVersion"] == 1 && (string)m0["app"]["version"] == FixtureWriter.V02AppVersion,
                w + "the fixture is a pre-freeze file (prerelease dev.1, formatVersion 1, app.version 0.2.0)");

            // F1: loads as v1; no error, no manifest finding, warnings only those the golden documents carry themselves.
            var r = k.Load(file);
            if (!r.Ok) { t.Check(false, w + "T-F1 loads (" + r + ")"); return; }
            t.Check(!r.Findings.HasErrors, w + "T-F1 no error finding" + (r.Findings.HasErrors ? " (" + r.Findings + ")" : ""));
            t.Check(!r.Findings.Has("manifest.prerelease") && !r.NewerFormat && !r.MissingRequiredExt, w + "T-F1 no manifest.prerelease, not newer, no missing required ext");
            var docWarn = DocumentWarnings(k);
            var fileWarn = Warnings(r.Findings);
            bool sameWarn = docWarn.Count == fileWarn.Count && docWarn.All(kv => fileWarn.TryGetValue(kv.Key, out int n) && n == kv.Value);
            t.Check(sameWarn, w + "T-F1 no warning beyond the golden documents' own (" + Show(fileWarn) + " vs " + Show(docWarn) + ")");

            // F2: the documents inside are the golden bytes (canonical: the goldens load and save byte-identically).
            foreach (var kv in k.Docs)
            {
                var e = raw.Get(kv.Key);
                var g = DocTests_T1Golden.Golden(kv.Value);
                t.Check(e != null && DocIo.Same(e.Data, g), w + "T-F2 " + kv.Key + " = golden " + kv.Value + (e == null ? " (missing)" : DocIo.Same(e.Data, g) ? "" : " (" + DocIo.FirstDiff(g, e.Data) + ")"));
            }

            // F3: re-saved by the frozen writer.
            byte[] s1;
            try { s1 = r.Value.ToBytes(Resave()); }
            catch (Exception e) { t.Check(false, w + "T-F3 re-save threw " + e.GetType().Name + ": " + e.Message); return; }
            var raw1 = ContainerReader.Read(s1);
            var m1 = Manifest(raw1);
            t.Check(m1["prerelease"] != null && m1["prerelease"].Type == JTokenType.Null, w + "T-F3 re-saved manifest prerelease null");
            t.Check((int)m1["formatVersion"] == 1 && (int)m1["minReaderVersion"] == 1, w + "T-F3 formatVersion 1, minReaderVersion 1");
            t.Equal("2026-10-20T00:00:00Z", (string)m1["createdUtc"], w + "T-F3 createdUtc kept");
            t.Equal("2026-10-26T12:00:00Z", (string)m1["modifiedUtc"], w + "T-F3 modifiedUtc = the save time");
            foreach (var kv in k.Docs)
            {
                var a = raw.Get(kv.Key);
                var b = raw1.Get(kv.Key);
                t.Check(a != null && b != null && DocIo.Same(a.Data, b.Data), w + "T-F3 " + kv.Key + " byte-equal after the re-save");
            }
            var names0 = raw.Entries.Select(e => e.Name).ToList();
            var names1 = raw1.Entries.Select(e => e.Name).ToList();
            t.Check(names0.SequenceEqual(names1), w + "T-F3 the same entries in the same order");
            t.Check(DocIo.Same(raw.Thumbnail, raw1.Thumbnail), w + "T-F3 thumbnail kept");
            // Only prerelease, app.version and modifiedUtc differ in the manifest.
            var diff = ManifestDiff(m0, m1);
            t.Equal("app.version,modifiedUtc,prerelease", diff, w + "T-F3 manifest members that differ");

            // F4: load → save of the re-saved bytes is byte-identical.
            var r1 = k.Load(s1);
            if (!r1.Ok) { t.Check(false, w + "T-F4 re-saved file loads (" + r1 + ")"); return; }
            t.Check(!r1.Findings.Has("manifest.prerelease"), w + "T-F4 the re-saved file reads without manifest.prerelease");
            byte[] s2 = r1.Value.ToBytes(Resave());
            t.Check(DocIo.Same(s1, s2), w + "T-F4 load → save byte-identical" + (DocIo.Same(s1, s2) ? "" : " (" + DocIo.FirstDiff(s1, s2) + ")"));

            // F5: another pre-release value is read best effort with the info.
            var dev2 = Repack(file, mm => EditJson(mm, "manifest.json", j => j["prerelease"] = "dev.2"), k.Main);
            var r2 = k.Load(dev2);
            var f5 = r2.Ok ? r2.Findings.FirstOrDefault(f => f.Code == "manifest.prerelease") : null;
            t.Check(r2.Ok && f5 != null && f5.Severity == FindingSeverity.Info, w + "T-F5 prerelease dev.2 → info manifest.prerelease (" + r2 + ")");
            var dev1 = Repack(file, mm => EditJson(mm, "manifest.json", j => j["prerelease"] = null), k.Main);
            var r3 = k.Load(dev1);
            t.Check(r3.Ok && !r3.Findings.Has("manifest.prerelease"), w + "T-F5 prerelease null → no finding");
        }

        // DocReader.Parse keeps the time strings as strings (no date parsing).
        static JObject Manifest(ContainerReader raw) => DocReader.Parse(raw.Get("manifest.json").Data, "manifest.json");

        static string ManifestDiff(JObject a, JObject b)
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var p in a.Properties().Select(p => p.Name).Union(b.Properties().Select(p => p.Name)))
            {
                if (p == "app")
                {
                    foreach (var q in ((JObject)a["app"]).Properties().Select(x => x.Name).Union(((JObject)b["app"]).Properties().Select(x => x.Name)))
                        if (!JToken.DeepEquals(a["app"][q], b["app"][q])) names.Add("app." + q);
                    continue;
                }
                if (!JToken.DeepEquals(a[p], b[p])) names.Add(p);
            }
            return string.Join(",", names);
        }

        /// <summary>Warning-or-error findings of reading the kind's golden documents directly (code → total count).</summary>
        static Dictionary<string, int> DocumentWarnings(Kind k)
        {
            var f = new FindingList();
            foreach (var g in k.Docs.Values)
            {
                var b = DocTests_T1Golden.Golden(g);
                switch (g)
                {
                    case "chara.json": DocIo.Chara(b, f); break;
                    case "coord.json": DocIo.Coord(b, f); break;
                    case "scene.json": DocIo.Scene(b, f); break;
                    case "pose.json": PoseDocument.Read(DocReader.ForDocument(b, new DocReadContext(PoseDocument.EntryName, false, f))); break;
                }
            }
            return Warnings(f);
        }

        static Dictionary<string, int> Warnings(FindingList f)
        {
            var d = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var x in f)
            {
                if (x.Severity == FindingSeverity.Info) continue;
                d.TryGetValue(x.Code, out int n);
                d[x.Code] = n + x.Count;
            }
            return d;
        }

        static string Show(Dictionary<string, int> d) => "{" + string.Join(", ", d.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + " " + kv.Value)) + "}";

        /// <summary>T-F7: the canonical float text is normative as Mono's "R" (G7, G9 when G7 does not round-trip).</summary>
        static void FloatText(TestRunner t)
        {
            t.Equal("0.160784319", JsonText.F32(0.160784319f), "T-F7 T(0.160784319f) = the golden text");
            t.Equal("0.427450985", JsonText.F32(0.427450985f), "T-F7 T(0.427450985f) = the golden text");
            t.Equal("0.796078444", JsonText.F32(0.796078444f), "T-F7 T(0.796078444f) = the golden text");
            t.Equal("8.513037E+08", JsonText.F32(8.513037E+08f), "T-F7 T(8.513037E+08f)");
            var scene = Encoding.UTF8.GetString(DocTests_T1Golden.Golden("scene.json"));
            t.Check(scene.Contains("0.796078444") && scene.Contains("0.427450985") && scene.Contains("0.160784319"), "T-F7 the golden scene holds the three 9-digit texts");
        }
    }

    /// <summary>
    /// The coordinate file of a character (Phase 4.6 coordinate save; CharaContainer.ExtractCoordinate / ApplyCoordinate): T-C1
    /// the extracted coordinate holds coord/coord.json byte for byte, the coordinate-scope ext blocks and hs2.kkex copies and
    /// exactly the resources the coordinate references (none of the character's own); T-C2 applying it to another character and
    /// extracting again gives the same file bytes; T-C3 a saved .hs2ncoord (placeholder thumbnail) reloads and re-saves
    /// byte-identically.
    /// </summary>
    public static class DocTests_Coordinate
    {
        static readonly DateTime Clock = new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc);
        static SaveOptions Opts() => new SaveOptions { Clock = () => Clock, Thumbnail = ThumbnailMode.Placeholder, Registry = new ExtRegistry(), AppVersion = "0.3.0" };
        static LoadOptions Load() => new LoadOptions { Registry = new ExtRegistry() };

        const string G5 = "res/5555555555555555555555555555555555555555555555555555555555555555.png";
        const string G6 = "res/6666666666666666666666666666666666666666666666666666666666666666.png";
        const string G1 = "res/1111111111111111111111111111111111111111111111111111111111111111.png";

        public static void Run(TestRunner t)
        {
            DocTests_T1Golden.FixturesDir = t.FixturesDir;
            var c = Source(out string coordRes5, out string coordRes6, out string charaRes, out string unused);
            byte[] cBytes = c.ToBytes(Opts());
            var raw = ContainerReader.Read(cBytes);

            // T-C1
            var ex = c.ExtractCoordinate();
            byte[] exBytes = ex.ToBytes(Opts());
            var exRaw = ContainerReader.Read(exBytes);
            t.Check(DocIo.Same(raw.Get("coord/coord.json").Data, exRaw.Get("coord.json").Data), "T-C1 coord.json = the character's coord/coord.json byte for byte");
            t.Check(ex.CoordExt.Blocks.Keys.SequenceEqual(c.Subtree.CoordExt.Blocks.Keys) &&
                    ex.CoordExt.Blocks.All(kv => kv.Value.Files.Keys.SequenceEqual(c.Subtree.CoordExt.Blocks[kv.Key].Files.Keys) &&
                                                kv.Value.Files.All(f => DocIo.Same(f.Value, c.Subtree.CoordExt.Blocks[kv.Key].Files[f.Key]))),
                "T-C1 the coordinate-scope ext blocks are copied");
            var k0 = c.Subtree.CoordExt.Kkex.Save();
            var k1 = ex.CoordExt.Kkex?.Save();
            t.Check(k1 != null && k1.Keys.SequenceEqual(k0.Keys) && k1.All(kv => DocIo.Same(kv.Value, k0[kv.Key])), "T-C1 the hs2.kkex copies are copied");
            var names = new SortedSet<string>(ex.Res.Names, StringComparer.Ordinal);
            t.Check(names.SetEquals(new[] { coordRes5, coordRes6 }) && !names.Contains(charaRes) && !names.Contains(unused),
                "T-C1 res/ = the coordinate's references only (" + string.Join(", ", names) + ")");
            t.Check(exRaw.Entries.Any(e => e.Name == coordRes5) && exRaw.Entries.Any(e => e.Name == coordRes6) && !exRaw.Entries.Any(e => e.Name == charaRes),
                "T-C1 the written file holds the coordinate's resources");
            t.Check(ex.Import == null, "T-C1 an extracted coordinate has no import provenance");

            // T-C2
            var other = new CharaContainer();
            other.Subtree.Chara = DocIo.Chara(DocTests_T1Golden.Golden("chara.json"));
            other.Subtree.Coord = new CoordDocument { Sex = 1, Name = "other" };
            other.ApplyCoordinate(ex);
            var ex2 = other.ExtractCoordinate();
            byte[] ex2Bytes = ex2.ToBytes(Opts());
            t.Check(DocIo.Same(exBytes, ex2Bytes), "T-C2 apply → extract gives the same coordinate file" + (DocIo.Same(exBytes, ex2Bytes) ? "" : " (" + DocIo.FirstDiff(exBytes, ex2Bytes) + ")"));

            // T-C3
            string path = System.IO.Path.Combine(t.TempDir, "tc3.hs2ncoord");
            var sr = ex.Save(path, Opts());
            byte[] saved = System.IO.File.ReadAllBytes(path);
            var lr = CoordContainer.Load(saved, Load());
            t.Check(sr.Ok && lr.Ok && !lr.Findings.HasErrors, "T-C3 the saved .hs2ncoord loads (" + lr + ")");
            if (lr.Ok)
            {
                var again = lr.Value.ToBytes(Opts());
                t.Check(DocIo.Same(saved, again), "T-C3 reload → save byte-identical" + (DocIo.Same(saved, again) ? "" : " (" + DocIo.FirstDiff(saved, again) + ")"));
                t.Check(PngInfo.ReadHeader(ContainerReader.Read(saved).Thumbnail)?.Width == 504 && lr.Value.Coord.Name == ex.Coord.Name, "T-C3 the placeholder thumbnail (504 wide), the name kept");
            }
        }

        /// <summary>The golden character with real resources: two referenced by the coordinate, one by the character only, one
        /// unreferenced; coordinate-scope ext data with a block and an hs2.kkex copy.</summary>
        static CharaContainer Source(out string coordRes5, out string coordRes6, out string charaRes, out string unused)
        {
            var c = new CharaContainer();
            coordRes5 = c.Res.Add(InfraFixtures.FakePng(501));
            coordRes6 = c.Res.Add(InfraFixtures.FakePng(502));
            charaRes = c.Res.Add(InfraFixtures.FakePng(503));
            unused = c.Res.Add(InfraFixtures.FakePng(504));
            string coordText = Encoding.UTF8.GetString(DocTests_T1Golden.Golden("coord.json")).Replace(G5, coordRes5).Replace(G6, coordRes6);
            string charaText = Encoding.UTF8.GetString(DocTests_T1Golden.Golden("chara.json")).Replace(G1, charaRes);
            c.Subtree.Chara = DocIo.Chara(DocIo.Utf8(charaText));
            c.Subtree.Coord = DocIo.Coord(DocIo.Utf8(coordText));
            c.Subtree.CoordExt.GetOrAdd("example.coord.plugin").Files["data.bin"] = new byte[] { 1, 2, 3 };
            c.Subtree.CoordExt.Kkex = new KkexStore { Source = "hs2.coord", BlockVersion = "3" };
            c.Subtree.CoordExt.Kkex.Add("KCOX", "kkex", null, 1, "coord", "source", null, new byte[] { 0x92, 0x07, 0x80 });
            return c;
        }
    }
}

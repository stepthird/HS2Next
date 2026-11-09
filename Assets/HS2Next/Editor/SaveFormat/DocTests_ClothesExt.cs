// Clothes parts' inline ext and the lowest-covering format revision (Phase 5.1):
// CE1 documents and files without part ext are written exactly as before (format 1, the goldens and the frozen fixtures
// byte for byte); CE2 a handler's SetClothesJson on two kinds gives format 2 (minReaderVersion 1), the member written last
// in the part, the manifest entry of the coord scope, and ClothesJson after a reload; CE3 part ext without a handler
// survives load → save byte for byte; CE4 a reader of revision 1 binds a format 2 file with NewerFormat, keeps the member
// and reports Downgraded on save, and a format 1 file that carries part ext (what that older writer gives back) is read
// in full by this reader; CE5 the migration 1 → 2 is empty, silent and idempotent; CE6 the 1 MiB limit, kind names, scope,
// clearing (which brings the file back to format 1). Synthetic namespaces only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Api.Save;
using Newtonsoft.Json.Linq;
using static HS2Next.Save.Tests.InfraFixtures;

namespace HS2Next.Save.Tests
{
    public static class DocTests_ClothesExt
    {
        const string Ns = "stp3.parts";

        static JObject Json(byte[] file, string entry) => JObject.Parse(Encoding.UTF8.GetString(ContainerReader.Read(file).Get(entry).Data));

        static int FormatOf(byte[] file) => (int)Json(file, "manifest.json")["formatVersion"];

        /// <summary>The canonical text a read context hands out for a JSON value.</summary>
        static string Canon(string json) => CanonicalJsonWriter.ToText(DocReader.TryParseValue(json, out _));

        const string TopJson = "{\"colour\": \"red\", \"n\": [1, 2.5]}";
        static string ReadBack => "C shoes=" + Canon("7") + ";top=" + Canon(TopJson);

        /// <summary>A coord-scope handler that writes part data for the given kinds and records what it read.</summary>
        static FakeHandler PartWriter(List<string> log, Dictionary<string, string> write)
        {
            var h = new FakeHandler(Ns, log) { Scopes = ExtScope.Coord };
            h.OnWrite = ctx => { foreach (var kv in write) ctx.SetClothesJson(kv.Key, kv.Value); };
            h.OnRead = ctx => log.Add("C " + string.Join(";", ctx.ClothesJson.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value)));
            return h;
        }

        static ExtRegistry Reg(params IExtBlockHandler[] hs)
        {
            var r = new ExtRegistry();
            foreach (var h in hs) r.Register(h);
            return r;
        }

        public static void Run(TestRunner t)
        {
            DocTests_T1Golden.FixturesDir = t.FixturesDir;
            CE1(t);
            var v2 = CE2(t);
            CE3(t, v2);
            CE4(t, v2);
            CE5(t);
            CE6(t);
        }

        static void CE1(TestRunner t)
        {
            var golden = DocTests_T1Golden.Golden("coord.json");
            var coord = DocIo.Coord(golden);
            t.Check(!coord.Clothes.HasPartExt() && coord.Clothes.Parts.All(p => p.Ext != null && p.Ext.IsEmpty), "CE1: the golden coordinate has no part ext");
            t.Check(DocIo.Same(golden, DocIo.Bytes(coord)), "CE1: the golden coordinate is written byte for byte");
            var c = new CoordContainer { Coord = coord };
            var bytes = c.ToBytes(Opts());
            t.Check(FormatOf(bytes) == 1 && (int)Json(bytes, "manifest.json")["minReaderVersion"] == 1, "CE1: a coordinate file without part ext is format 1");
            t.Check(DocIo.Same(golden, ContainerReader.Read(bytes).Get("coord.json").Data), "CE1: its coord.json is the golden");
            foreach (var f in new[] { "chara.hs2nchara", "coord.hs2ncoord", "scene.hs2nscene" })
            {
                string path = Path.Combine(FixtureWriter.V02Dir(t.FixturesDir ?? Path.Combine(DocTests_T1Golden.GoldenDir, "..")), f);
                if (!File.Exists(path)) { t.Check(false, "CE1: fixture " + f + " present"); continue; }
                var file = File.ReadAllBytes(path);
                LoadResult<ContainerBase> r = f.EndsWith("nchara") ? Wrap(CharaContainer.Load(file, LOpts())) : f.EndsWith("ncoord") ? Wrap(CoordContainer.Load(file, LOpts())) : Wrap(SceneContainer.Load(file, LOpts()));
                if (!r.Ok) { t.Check(false, "CE1: " + f + " loads (" + r + ")"); continue; }
                var s1 = r.Value.ToBytes(Opts());
                t.Check(FormatOf(s1) == 1 && r.Findings.CountOf("migrate.core") == 0, "CE1: " + f + " re-saved as format 1, no migration finding");
                foreach (var e in ContainerReader.Read(file).Entries.Where(e => e.Name.EndsWith(".json") && e.Name != "manifest.json"))
                    t.Check(DocIo.Same(e.Data, ContainerReader.Read(s1).Get(e.Name)?.Data), "CE1: " + f + " " + e.Name + " byte-equal after the re-save");
            }
            var empty = SmallChara();
            empty.Coord.Clothes.Parts[3].Ext = new ExtInline();
            var eb = empty.ToBytes(Opts());
            t.Check(FormatOf(eb) == 1 && !Encoding.UTF8.GetString(ContainerReader.Read(eb).Get("coord/coord.json").Data).Contains("\"ext\""), "CE1: an empty part ext writes no member and stays format 1");
            t.Check(eb.SequenceEqual(SmallChara().ToBytes(Opts())), "CE1: and the file is byte-equal to one without the holder");
        }

        static LoadResult<ContainerBase> Wrap<T>(LoadResult<T> r) where T : ContainerBase =>
            new LoadResult<ContainerBase> { Value = r.Value, Error = r.Error, Findings = r.Findings, NewerFormat = r.NewerFormat, MissingRequiredExt = r.MissingRequiredExt };

        static byte[] CE2(TestRunner t)
        {
            var log = new List<string>();
            var write = new Dictionary<string, string> { ["top"] = TopJson, ["shoes"] = "7" };
            var reg = Reg(PartWriter(log, write));
            var chara = SmallChara();
            var bytes = chara.ToBytes(Opts(reg));
            var man = Json(bytes, "manifest.json");
            t.Check(FormatOf(bytes) == 2 && (int)man["minReaderVersion"] == 1, "CE2: a file with part ext is format 2, minReaderVersion 1");
            var top = (JObject)Json(bytes, "coord/coord.json")["clothes"]["top"];
            t.Check(top.Properties().Last().Name == "ext" && (string)top["ext"][Ns]["colour"] == "red", "CE2: the part's ext is its last member (where an older reader keeps an unknown member)");
            t.Check(Json(bytes, "coord/coord.json")["clothes"]["bottom"]["ext"] == null && (int)Json(bytes, "coord/coord.json")["clothes"]["shoes"]["ext"][Ns] == 7, "CE2: only the written kinds carry it");
            t.Check(man["ext"].Any(e => (string)e["ns"] == Ns && (string)e["scope"] == "coord" && (string)e["root"] == "coord/") && !man["ext"].Any(e => (string)e["ns"] == Ns && (string)e["scope"] == "chara"),
                "CE2: one manifest entry of the coord scope covers the part data");
            log.Clear();
            var l = CharaContainer.Load(bytes, LOpts(reg));
            t.Check(l.Ok && !l.Findings.HasErrors && !l.NewerFormat && l.Findings.CountOf("migrate.core") == 0, "CE2: this reader loads it without findings of its own (" + l + ")");
            t.Check(log.Contains(ReadBack) && log.Any(x => x.StartsWith("R " + Ns + " Coord", StringComparison.Ordinal) && x.Contains("json=null acc=0")),
                "CE2: ClothesJson after the reload (canonical text by kind); no main or accessory value\n    got: " + string.Join(" | ", log));
            if (l.Ok)
            {
                t.Check(l.Value.ToBytes(Opts(reg)).SequenceEqual(bytes), "CE2: load → save with the handler byte-identical");
                var coord = l.Value.ExtractCoordinate();
                var cb = coord.ToBytes(Opts());
                t.Check(FormatOf(cb) == 2 && (int)Json(cb, "coord.json")["clothes"]["shoes"]["ext"][Ns] == 7, "CE2: the extracted coordinate keeps the part data and is format 2");
                var other = SmallChara();
                other.ApplyCoordinate(coord);
                t.Check(other.Coord.Clothes.Parts[0].Ext.Contains(Ns) && FormatOf(other.ToBytes(Opts())) == 2, "CE2: applying it to a character file carries the part data");
            }
            var scene = SmallScene(1, 0, CharaContainer.Load(bytes, LOpts()).Unwrap());
            var sb = scene.ToBytes(Opts());
            t.Check(FormatOf(sb) == 2, "CE2: a scene with such a character is format 2");
            t.Check(FormatOf(SmallScene(1, 0).ToBytes(Opts())) == 1, "CE2: a scene without is format 1");
            // other scopes: no clothes data
            var scopes = new List<string>();
            var probe = new FakeHandler("stp3.probe", scopes) { OnRead = ctx => scopes.Add("C " + ctx.Scope + " " + ctx.ClothesJson.Count) };
            var pc = SmallChara();
            pc.Chara.Ext.Set("stp3.probe", JToken.Parse("1"));
            CharaContainer.Load(pc.ToBytes(Opts()), LOpts(Reg(probe)));
            t.Check(scopes.Contains("C Chara 0"), "CE2: ClothesJson is empty outside the coord scope");
            return bytes;
        }

        static void CE3(TestRunner t, byte[] v2)
        {
            var l = CharaContainer.Load(v2, LOpts());
            t.Check(l.Ok && l.Findings.CountOf("ext.unknown") == 1 && l.Value.Coord.Clothes.Parts[0].Ext.Contains(Ns), "CE3: without a handler the part data is an unknown block (" + l + ")");
            t.Check(l.Ok && l.Value.ToBytes(Opts()).SequenceEqual(v2), "CE3: load → save without a handler byte for byte (format 2 kept)");
            var other = new List<string>();
            var reg = Reg(new FakeHandler("stp3.other", other) { OnWrite = ctx => ctx.SetJson("1") });
            var lo = CharaContainer.Load(v2, LOpts(reg)).Unwrap();
            var back = Json(lo.ToBytes(Opts(reg)), "coord/coord.json");
            t.Check(JToken.DeepEquals(back["clothes"], Json(v2, "coord/coord.json")["clothes"]) && back["ext"]?["stp3.other"] != null,
                "CE3: another plugin's writes leave the unknown part data in place (the clothes equal)");
        }

        static void CE4(TestRunner t, byte[] v2)
        {
            var s1 = LOpts();
            s1.Migrations = new CoreMigrationRegistry(1);
            var l = CharaContainer.Load(v2, s1);
            t.Check(l.Ok && l.NewerFormat && l.Findings.Has("manifest.newerFormat") && l.Value.Coord.Clothes.Parts[0].Ext.Contains(Ns),
                "CE4: a revision-1 reader binds the format 2 file with NewerFormat and keeps the part data (" + l + ")");
            if (!l.Ok) return;
            string path = Path.Combine(t.TempDir, "ce4.hs2nchara");
            var sr = l.Value.Save(path, Opts());
            t.Check(sr.Ok && sr.Downgraded, "CE4: saving it reports Downgraded");
            // What a revision-1 writer gives back: the same documents (the member kept as an unknown one, written after the
            // known members, where this writer puts ext) under formatVersion 1.
            var old = Repack(File.ReadAllBytes(path), m => EditJson(m, "manifest.json", j => j["formatVersion"] = 1));
            var log = new List<string>();
            var reg = Reg(PartWriter(log, new Dictionary<string, string>()));
            var l2 = CharaContainer.Load(old, LOpts(reg));
            t.Check(l2.Ok && !l2.NewerFormat && l2.Findings.CountOf("migrate.core") == 0 && log.Contains(ReadBack),
                "CE4: this reader gets the part data back from such a file (empty migration, no finding)\n    got: " + string.Join(" | ", log));
            t.Check(l2.Ok && FormatOf(l2.Value.ToBytes(Opts())) == 2, "CE4: and writes it as format 2 again");
        }

        static void CE5(TestRunner t)
        {
            t.Check(CoreMigrationRegistry.Default.Supported == SaveFormats.SupportedVersion && SaveFormats.SupportedVersion == 5 && SaveFormats.FormatVersion == 1 &&
                    SaveFormats.ClothesPartExtVersion == 2 && SaveFormats.NippleThroughClothesVersion == 3 && SaveFormats.TimelineVersion == 4 && SaveFormats.MinReaderVersion == 1,
                "CE5: S = 4 (after 1.2.0), base and minReaderVersion 1, part ext at revision 2, the nipple switch at revision 3, the Timeline at revision 4");
            var v1 = SmallChara().ToBytes(Opts());
            var l = CharaContainer.Load(v1, LOpts());
            t.Check(l.Ok && l.Findings.Count == 0, "CE5: a format 1 file loads with no finding at all (the migration is silent) (" + l + ")");
            var docs = new Dictionary<string, JObject> { ["chara.json"] = Json(v1, "chara.json"), ["coord/coord.json"] = Json(v1, "coord/coord.json") };
            var before = string.Join("|", docs.Values.Select(j => j.ToString(Newtonsoft.Json.Formatting.None)));
            var findings = new FindingList();
            var m = CoreMigrationRegistry.Empty(1);
            var ctx = new MigrationContext(SaveFormats.Chara, Json(v1, "manifest.json"), docs, findings, 1);
            m.Apply(ctx);
            m.Apply(ctx);
            t.Check(m.From == 1 && findings.Count == 0 && string.Join("|", docs.Values.Select(j => j.ToString(Newtonsoft.Json.Formatting.None))) == before,
                "CE5: the migration 1 → 2 changes nothing and reports nothing, also when applied twice");
            t.Throws<InvalidOperationException>(() => new CoreMigrationRegistry(3, CoreMigrationRegistry.Empty(1)), "CE5: a registry beyond 2 still needs its own migration (no gap)");
            var reg = new CoreMigrationRegistry(2, CoreMigrationRegistry.Empty(1));
            var lo = LOpts();
            lo.Migrations = reg;
            var a = CharaContainer.Load(v1, lo);
            var b = CharaContainer.Load(v1, lo);
            t.Check(a.Ok && b.Ok && a.Value.ToBytes(Opts()).SequenceEqual(v1) && b.Value.ToBytes(Opts()).SequenceEqual(v1), "CE5: repeated loads through the migration write the same format 1 bytes");
        }

        static void CE6(TestRunner t)
        {
            var errors = new List<string>();
            var h = new FakeHandler(Ns, new List<string>());
            h.OnWrite = ctx =>
            {
                void Expect<TEx>(Action a, string what) where TEx : Exception
                {
                    try { a(); errors.Add(ctx.Scope + " " + what + ": no exception"); }
                    catch (TEx) { errors.Add(ctx.Scope + " " + what + ": ok"); }
                    catch (Exception e) { errors.Add(ctx.Scope + " " + what + ": " + e.GetType().Name); }
                }
                if (ctx.Scope == ExtScope.Chara)
                {
                    Expect<ArgumentException>(() => ctx.SetClothesJson("top", "1"), "outside the coord scope");
                    return;
                }
                Expect<ArgumentException>(() => ctx.SetClothesJson("hat", "1"), "unknown kind");
                Expect<ArgumentException>(() => ctx.SetClothesJson("Top", "1"), "kind names are exact");
                Expect<ArgumentNullException>(() => ctx.SetClothesJson(null, "1"), "null kind");
                Expect<ArgumentException>(() => ctx.SetClothesJson("top", "{\"a\": "), "not one JSON value");
                var big = "\"" + new string('x', ExtInline.MaxBlockBytes) + "\"";
                Expect<ArgumentException>(() => ctx.SetClothesJson("top", big), "over 1 MiB");
                var fits = "\"" + new string('x', ExtInline.MaxBlockBytes - 2) + "\"";
                try { ctx.SetClothesJson("gloves", fits); errors.Add(ctx.Scope + " exactly 1 MiB accepted: ok"); }
                catch (Exception e) { errors.Add(ctx.Scope + " exactly 1 MiB accepted: " + e.GetType().Name); }
                ctx.SetClothesJson("gloves", null);
                ctx.SetClothesJson("socks", "true");
            };
            var reg = Reg(h);
            var bytes = SmallChara().ToBytes(Opts(reg));
            string got = string.Join(" | ", errors);
            t.Check(errors.Count == 7 && errors.All(e => e.EndsWith(": ok", StringComparison.Ordinal)), "CE6: kind names, scope, JSON and the 1 MiB limit refused with ArgumentException\n    got: " + got);
            var coord = Json(bytes, "coord/coord.json")["clothes"];
            t.Check(coord["gloves"]["ext"] == null && (bool)coord["socks"]["ext"][Ns] && FormatOf(bytes) == 2, "CE6: null clears a kind set in the same write; the other kind is written");
            // clearing every part brings the file back to format 1 and removes the namespace from the manifest
            var l = CharaContainer.Load(bytes, LOpts(reg)).Unwrap();
            h.OnWrite = ctx => { if (ctx.Scope == ExtScope.Coord) ctx.SetClothesJson("socks", null); };
            var cleared = l.ToBytes(Opts(reg));
            t.Check(FormatOf(cleared) == 1 && !Json(cleared, "manifest.json")["ext"].Any(e => (string)e["ns"] == Ns) && cleared.SequenceEqual(SmallChara().ToBytes(Opts())),
                "CE6: a handler that writes no part data any more: the file is format 1 again, byte-equal to one that never had it");
        }
    }
}

// D1 tests: T7 versions, T8 non-finite (read side; the write side is in
// InfraTests_Container), T9 migration fixture, T12 resources.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using static HS2Next.Save.Tests.InfraFixtures;

namespace HS2Next.Save.Tests
{
    public static class InfraTests_Versions
    {
        public static void Run(TestRunner t)
        {
            T7(t);
            T8Read(t);
            T9(t);
            T12(t);
        }

        static void T7(TestRunner t)
        {
            int S = SaveFormats.SupportedVersion;
            var orig = SmallChara().ToBytes(Opts());
            var tooNew = Repack(orig, m => EditJson(m, "manifest.json", j => { j["minReaderVersion"] = S + 1; j["formatVersion"] = S + 1; j["app"]["version"] = "9.9.9"; }));
            var r = CharaContainer.Load(tooNew, LOpts());
            t.Check(r.ErrorCode == SaveErrorCode.ReaderTooOld && r.Error.Message.Contains("9.9.9"), "T7: minReaderVersion = S+1 → ReaderTooOld naming the app version (" + r + ")");

            var newer = Repack(orig, m =>
            {
                EditJson(m, "manifest.json", j => { j["formatVersion"] = S + 1; j["futureManifestMember"] = new JArray(1, 2); });
                EditJson(m, "chara.json", j => { j["futureTop"] = JObject.Parse("{\"x\": [0.5, 2.0]}"); ((JObject)j["profile"])["futureNested"] = "n"; });
            });
            var ln = CharaContainer.Load(newer, LOpts());
            t.Check(ln.Ok && ln.NewerFormat && ln.Findings.Has("manifest.newerFormat"), "T7: formatVersion = S+1 with minReaderVersion ≤ S loads with NewerFormat (" + ln + ")");
            string path = System.IO.Path.Combine(t.TempDir, "t7.hs2nchara");
            var sr = ln.Value.Save(path, Opts());
            t.Check(sr.Ok && sr.Downgraded, "T7: saving it reports Downgraded");
            var raw = ContainerReader.Read(path);
            var mj = JObject.Parse(Encoding.UTF8.GetString(raw.Get("manifest.json").Data));
            var cj = JObject.Parse(Encoding.UTF8.GetString(raw.Get("chara.json").Data));
            t.Check((int)mj["formatVersion"] == SaveFormats.FormatVersion && (int)mj["minReaderVersion"] == SaveFormats.MinReaderVersion,
                "T7: written with this app's lowest covering formatVersion (nothing later is used: the base) and its minReaderVersion");
            t.Check(mj["futureManifestMember"] != null && cj["futureTop"] != null && (string)cj["profile"]["futureNested"] == "n", "T7: unknown members kept (manifest, document root, nested)");
            var prop = cj.Properties().Select(p => p.Name).ToList();
            t.Check(prop.IndexOf("futureTop") == prop.IndexOf("studioOverlays") + 1, "T7: unknown members after the known ones, before ext");
            var pre = Repack(orig, m => EditJson(m, "manifest.json", j => j["prerelease"] = "dev.99"));
            var lp = CharaContainer.Load(pre, LOpts());
            t.Check(lp.Ok && lp.Findings.Has("manifest.prerelease"), "T7: unknown prerelease read best effort with a finding (15.15)");
            var written = JObject.Parse(Encoding.UTF8.GetString(ContainerReader.Read(orig).Get("manifest.json").Data))["prerelease"];
            t.Check(written != null && written.Type == JTokenType.Null, "T7: writers put prerelease null since the format v1 freeze");
            var dev1 = Repack(orig, m => EditJson(m, "manifest.json", j => j["prerelease"] = "dev.1"));
            var ld = CharaContainer.Load(dev1, LOpts());
            t.Check(ld.Ok && !ld.Findings.Has("manifest.prerelease"), "T7: a dev.1 file is format v1 (no finding)");
        }

        static void T8Read(TestRunner t)
        {
            var orig = SmallChara().ToBytes(Opts());
            var nan = Repack(orig, m =>
            {
                string s = Encoding.UTF8.GetString(m["chara.json"]);
                int i = s.IndexOf("\"shape\": [", StringComparison.Ordinal);
                m["chara.json"] = Utf8(s.Substring(0, i) + "\"shape\": [NaN, " + s.Substring(i + 10));
            });
            var r = CharaContainer.Load(nan, LOpts());
            t.Check(r.ErrorCode == SaveErrorCode.NonFinite && r.Error.JsonPath == "face.shape[0]" && r.Error.Entry == "chara.json", "T8: NaN literal on read → NonFinite with path (" + r + ")");
            var inf = Repack(orig, m =>
            {
                string s = Encoding.UTF8.GetString(m["chara.json"]);
                int i = s.IndexOf("\"shape\": [", StringComparison.Ordinal);
                m["chara.json"] = Utf8(s.Substring(0, i) + "\"shape\": [1E+39, " + s.Substring(i + 10));
            });
            t.Equal(SaveErrorCode.NonFinite, CharaContainer.Load(inf, LOpts()).ErrorCode, "T8: number outside the float32 range → NonFinite");
            var lenient = LOpts();
            lenient.Lenient = true;
            t.Equal(SaveErrorCode.NonFinite, CharaContainer.Load(nan, lenient).ErrorCode, "T8: NonFinite also fails a lenient load");
        }

        /// <summary>1 → 2: renames chara.json profile.nick → profile.name, never overwriting an existing name.</summary>
        sealed class RenameNick : ICoreMigration
        {
            public int From => 1;
            public int Applied;

            public void Apply(MigrationContext ctx)
            {
                Applied++;
                foreach (var kv in ctx.Documents)
                {
                    if (!kv.Key.EndsWith("chara.json", StringComparison.Ordinal)) continue;
                    if (!(kv.Value["profile"] is JObject p) || p["nick"] == null) continue;
                    if (p["name"] == null) p["name"] = p["nick"].DeepClone();
                    p.Remove("nick");
                    ctx.Report("renamed", kv.Key + ": profile.nick → profile.name");
                }
            }
        }

        static void T9(TestRunner t)
        {
            t.Throws<InvalidOperationException>(() => new CoreMigrationRegistry(3, new RenameNick()), "T9: a gap in the migration chain is a startup error");
            var mig = new RenameNick();
            var reg = new CoreMigrationRegistry(2, mig);
            var orig = SmallChara().ToBytes(Opts());
            var v1 = Repack(orig, m => EditJson(m, "chara.json", j => { var p = (JObject)j["profile"]; p.Remove("name"); p.AddFirst(new JProperty("nick", "Old Nick")); }));
            var lo = LOpts();
            lo.Migrations = reg;
            var r = CharaContainer.Load(v1, lo);
            t.Check(r.Ok && r.Value.Chara.Profile.Name == "Old Nick" && mig.Applied == 1 && r.Findings.Has("migrate.core.1.renamed"), "T9: v1 fixture upgraded (renamed member) (" + r + ")");
            t.Check(r.Ok && (r.Value.Chara.Profile.Unknown == null || r.Value.Chara.Profile.Unknown.Get("nick") == null), "T9: old member gone after the migration");
            var both = Repack(orig, m => EditJson(m, "chara.json", j => ((JObject)j["profile"]).AddFirst(new JProperty("nick", "Other"))));
            var r2 = CharaContainer.Load(both, lo);
            t.Check(r2.Ok && r2.Value.Chara.Profile.Name == "Test Chara", "T9: a file that already has the member is unchanged (idempotent)");
            var plain = CharaContainer.Load(orig, lo);
            t.Check(plain.Ok && plain.Value.Chara.Profile.Name == "Test Chara", "T9: migration on a file without the old member changes nothing");
            // a file written by S = 2 read by S = 1: NewerFormat
            var v2 = Repack(orig, m => EditJson(m, "manifest.json", j => j["formatVersion"] = 2));
            var s1 = LOpts();
            s1.Migrations = new CoreMigrationRegistry(1);
            t.Check(CharaContainer.Load(v2, s1).NewerFormat, "T9: formatVersion 2 on an S = 1 reader → NewerFormat");
            t.Check(!CharaContainer.Load(v2, lo).NewerFormat && mig.Applied == 3, "T9: formatVersion 2 on an S = 2 reader → no migration, not newer");
        }

        static void T12(TestRunner t)
        {
            var store = new ResStore();
            var png = FakePng(42, 1000);
            var jpg = new byte[600];
            new Random(5).NextBytes(jpg);
            jpg[0] = 0xFF; jpg[1] = 0xD8; jpg[2] = 0xFF;
            string a = store.Add(png), b = store.Add((byte[])png.Clone()), j = store.Add(jpg);
            t.Check(a == b && store.Count == 2, "T12: dedup by hash");
            t.Check(a.EndsWith(".png") && j.EndsWith(".jpg") && a == "res/" + ResStore.Sha256Hex(png) + ".png", "T12: name = res/<sha256>.<ext from magic>");
            t.Throws<ArgumentException>(() => store.Add(new byte[] { 1, 2, 3 }), "T12: bin refused outside the import API");
            var f = new FindingList();
            string bin = store.AddImported(new byte[] { 1, 2, 3 }, f, "test");
            t.Check(bin.EndsWith(".bin") && f.Has("res.unknownMediaType"), "T12: bin only through the import API, reported");

            // GC: unreferenced dropped; referenced only from an unknown member kept
            var c = SmallChara();
            string used = c.Res.Add(png);
            string unknownOnly = c.Res.Add(jpg);
            string unused = c.Res.Add(FakePng(77, 300));
            c.Chara.Body.SkinOverlays.Add(new SkinOverlay { Target = "BodyOver", Image = used });
            c.Chara.Unknown = new JsonLeftovers();
            c.Chara.Unknown.Add("futureImages", JArray.Parse("[{\"img\": \"" + unknownOnly + "\"}]"));
            var bytes = c.ToBytes(Opts());
            var raw = ContainerReader.Read(bytes);
            t.Check(raw.Contains(used) && raw.Contains(unknownOnly) && !raw.Contains(unused), "T12: GC drops unreferenced, keeps one referenced only from an unknown member");

            // hash mismatch: rename an entry to a wrong hash and point the reference at the wrong name → re-keyed + rewritten
            string wrong = "res/" + new string('0', 64) + ".png";
            var bad = Repack(bytes, m =>
            {
                m[wrong] = m[used];
                m.Remove(used);
                m["chara.json"] = Utf8(Encoding.UTF8.GetString(m["chara.json"]).Replace(used, wrong));
            });
            var l = CharaContainer.Load(bad, LOpts());
            t.Check(l.Ok && l.Findings.Has("res.hashMismatch") && l.Value.Chara.Body.SkinOverlays[0].Image == used && l.Value.Res.Contains(used), "T12: hash mismatch re-keyed, reference rewritten (" + l + ")");
            var re = ContainerReader.Read(l.Value.ToBytes(Opts()));
            t.Check(re.Contains(used) && !re.Contains(wrong), "T12: saved under the true name");

            // sharing in a scene: one res/ for every subtree; copy on placing
            var scene = SmallScene(2, 0, c);
            var sraw = ContainerReader.Read(scene.ToBytes(Opts()));
            t.Equal(2, sraw.Entries.Count(e => e.Name.StartsWith("res/")), "T12: scene shares one res/ (identical bytes stored once)");
            var bytesNoPng = SmallChara().ToBytes(Opts());
            t.Check(!ContainerReader.Read(bytesNoPng).Entries.Any(e => e.Name.StartsWith("res/")), "T12: no res/ without references");
            // a bad res name inside res/ is dropped with a finding
            var odd = Repack(bytes, m => m["res/readme.txt"] = Utf8("x"));
            var lo = CharaContainer.Load(odd, LOpts());
            t.Check(lo.Ok && lo.Findings.Has("res.badName") && !ContainerReader.Read(lo.Value.ToBytes(Opts())).Contains("res/readme.txt"), "T12: non-res names in res/ dropped");
        }
    }
}

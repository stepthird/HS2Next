// D1 ext tests: T6 ext contract with fake handlers, the ext/foreign-entry part of T5
// (unknown blocks and foreign entries survive, also when a character moves scene ↔ character file),
// T13 hs2.kkex index round trip and StripImportSources.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HS2Next.Api.Save;
using Newtonsoft.Json.Linq;
using static HS2Next.Save.Tests.InfraFixtures;

namespace HS2Next.Save.Tests
{
    public static class InfraTests_Ext
    {
        public static void Run(TestRunner t)
        {
            T6(t);
            T5Ext(t);
            T13(t);
        }

        static ExtRegistry Writers(List<string> log, out FakeHandler a, out FakeHandler b)
        {
            var reg = new ExtRegistry();
            a = new FakeHandler("stp3.a", log) { Version = 3 };
            b = new FakeHandler("stp3.b", log) { Version = 1, RequiredByDefault = true };
            a.OnWrite = ctx =>
            {
                ctx.SetJson("{\"scope\": \"" + ctx.Scope + "\", \"owner\": " + (ctx.OwnerId == null ? "null" : "\"" + ctx.OwnerId + "\"") + "}");
                if (ctx.Scope == ExtScope.Chara) ctx.AddFile("data/blob.bin", new byte[] { 1, 2, 3 });
            };
            b.OnWrite = ctx =>
            {
                if (ctx.Scope == ExtScope.Coord) ctx.SetAccessoryJson(Id(100), "[1, 2.5, true]");
                if (ctx.Scope == ExtScope.Node) ctx.SetJson("7");
            };
            reg.Register(b);
            reg.Register(a);
            return reg;
        }

        static void T6(TestRunner t)
        {
            var log = new List<string>();
            var reg = Writers(log, out var a, out var b);
            var scene = SmallScene(2, 2);
            var bytes = scene.ToBytes(Opts(reg));
            var expectWrite = new List<string>
            {
                "W stp3.a Scene -", "W stp3.b Scene -",
                "W stp3.a Node " + Id(1000), "W stp3.b Node " + Id(1000), "W stp3.a Node " + Id(1001), "W stp3.b Node " + Id(1001),
                "W stp3.a Node " + Id(5000), "W stp3.b Node " + Id(5000), "W stp3.a Node " + Id(5001), "W stp3.b Node " + Id(5001),
                "W stp3.a Chara " + Id(1000), "W stp3.b Chara " + Id(1000), "W stp3.a Coord " + Id(1000), "W stp3.b Coord " + Id(1000),
                "W stp3.a Chara " + Id(1001), "W stp3.b Chara " + Id(1001), "W stp3.a Coord " + Id(1001), "W stp3.b Coord " + Id(1001),
            };
            t.Check(log.SequenceEqual(expectWrite), "T6: Write order scene, nodes in array order, then per character chara → coord; handlers ordinal\n    got: " + string.Join(" | ", log));
            var man = ContainerReader.Read(bytes).Get("manifest.json").Data;
            var mj = JObject.Parse(Encoding.UTF8.GetString(man));
            var ext = (JArray)mj["ext"];
            string extList = string.Join(" ", ext.Select(e => (string)e["root"] + "|" + (string)e["scope"] + "|" + (string)e["ns"] + "|" + (int)e["version"] + "|" + (bool)e["required"]));
            string c0 = "charas/" + Id(1000) + "/", c1 = "charas/" + Id(1001) + "/";
            string expectExt = "|node|stp3.a|3|False |node|stp3.b|1|True |scene|stp3.a|3|False " +
                c0 + "|chara|stp3.a|3|False " + c0 + "coord/|coord|stp3.a|3|False " + c0 + "coord/|coord|stp3.b|1|True " +
                c1 + "|chara|stp3.a|3|False " + c1 + "coord/|coord|stp3.a|3|False " + c1 + "coord/|coord|stp3.b|1|True";
            t.Equal(expectExt, extList, "T6: manifest ext entries sorted (root, scope, ns), one node entry per ns, versions, required");
            var raw = ContainerReader.Read(bytes);
            t.Check(raw.Contains(c0 + "ext/stp3.a/data/blob.bin") && raw.Contains(c1 + "ext/stp3.a/data/blob.bin"), "T6: handler files under charas/<id>/ext/<ns>/");

            // Read order and contexts
            log.Clear();
            var lr = SceneContainer.Load(bytes, LOpts(reg));
            t.Check(lr.Ok, "T6: load " + lr);
            var expectRead = new List<string>
            {
                "R stp3.a Scene - v3 json={\"scope\":\"Scene\",\"owner\":null} acc=0 files=",
                "R stp3.a Node " + Id(1000) + " v3 json={\"scope\":\"Node\",\"owner\":\"" + Id(1000) + "\"} acc=0 files=",
                "R stp3.b Node " + Id(1000) + " v1 json=7 acc=0 files=",
            };
            t.Check(log.Take(3).SequenceEqual(expectRead), "T6: Read order and contexts\n    got: " + string.Join(" | ", log.Take(4)));
            t.Check(log.Contains("R stp3.a Chara " + Id(1000) + " v3 json={\"scope\":\"Chara\",\"owner\":\"" + Id(1000) + "\"} acc=0 files=data/blob.bin"), "T6: chara read context with files");
            t.Check(log.Contains("R stp3.b Coord " + Id(1001) + " v1 json=null acc=1 files="), "T6: coord read context with accessory JSON");
            t.Equal(15, log.Count, "T6: one Read per (instance, namespace) with data");

            // Decline, Read exception → unknown, kept unchanged and not offered to Write
            var log2 = new List<string>();
            var reg2 = new ExtRegistry();
            var a2 = new FakeHandler("stp3.a", log2) { OnRead = ctx => { if (ctx.Scope == ExtScope.Chara) ctx.Decline("newer data"); } };
            var b2 = new FakeHandler("stp3.b", log2) { OnRead = ctx => { if (ctx.Scope == ExtScope.Coord) throw new InvalidOperationException("boom"); } };
            a2.OnWrite = ctx => ctx.SetJson("\"rewritten\"");
            b2.OnWrite = ctx => { if (ctx.Scope == ExtScope.Coord) ctx.SetAccessoryJson(Id(100), "\"rewritten\""); };
            reg2.Register(a2);
            reg2.Register(b2);
            var l2 = SceneContainer.Load(bytes, LOpts(reg2));
            t.Check(l2.Ok && l2.Findings.Has("ext.declined") && l2.Findings.Has("ext.handlerFault"), "T6: decline and Read exception reported, load succeeds");
            log2.Clear();
            var again = l2.Value.ToBytes(Opts(reg2));
            t.Check(!log2.Contains("W stp3.a Chara " + Id(1000)) && !log2.Contains("W stp3.b Coord " + Id(1000)) && log2.Contains("W stp3.a Scene -"),
                "T6: declined/threw blocks not offered to Write, others are");
            var ch = Encoding.UTF8.GetString(ContainerReader.Read(again).Get(c0 + "chara.json").Data);
            t.Check(ch.Contains("\"stp3.a\": {") && ch.Contains("\"scope\": \"Chara\""), "T6: declined block written back unchanged");
            var co = Encoding.UTF8.GetString(ContainerReader.Read(again).Get(c0 + "coord/coord.json").Data);
            t.Check(co.Contains("\"stp3.b\": [1, 2.5, true]") && !co.Contains("\"stp3.b\": \"rewritten\""), "T6: block whose Read threw written back unchanged");
            t.Check(ContainerReader.Read(again).Contains(c0 + "ext/stp3.a/data/blob.bin"), "T6: declined block's files kept");

            // exception in Write keeps the block, the save succeeds
            var log3 = new List<string>();
            var reg3 = new ExtRegistry();
            var a3 = new FakeHandler("stp3.a", log3) { OnWrite = ctx => throw new Exception("write fault") };
            reg3.Register(a3);
            var l3 = SceneContainer.Load(bytes, LOpts(reg3)).Unwrap();
            string p3 = System.IO.Path.Combine(t.TempDir, "t6w.hs2nscene");
            var s3 = l3.Save(p3, Opts(reg3));
            t.Check(s3.Ok && s3.Findings.Has("ext.handlerFault"), "T6: exception in Write → save succeeds, fault reported (" + s3 + ")");
            var raw3 = ContainerReader.Read(System.IO.File.ReadAllBytes(p3));
            t.Check(Encoding.UTF8.GetString(raw3.Get(c0 + "chara.json").Data).Contains("\"scope\": \"Chara\"") && raw3.Contains(c0 + "ext/stp3.a/data/blob.bin"),
                "T6: block kept as loaded after a Write fault");
            var fresh = SmallChara();
            var sf = fresh.ToBytes(Opts(reg3));
            t.Check(!Encoding.UTF8.GetString(ContainerReader.Read(sf).Get("chara.json").Data).Contains("stp3.a"), "T6: Write fault on a new instance writes no block");
            t.Check(reg3.IsFaulted("stp3.a"), "T6: handler faulted after " + reg3.FaultCount("stp3.a") + " faults (limit 10)");

            // required unknown
            var none = SceneContainer.Load(bytes, LOpts(new ExtRegistry()));
            t.Check(none.Ok && none.MissingRequiredExt && none.Findings.Has("ext.missingRequired") && none.Findings.Has("ext.unknown"), "T6: required unknown → MissingRequiredExt + warning");
            var lo = LOpts(new ExtRegistry());
            lo.FailOnMissingRequiredExt = true;
            t.Equal(SaveErrorCode.MissingRequiredExt, SceneContainer.Load(bytes, lo).ErrorCode, "T6: FailOnMissingRequiredExt turns it into a failure");

            // registration rules
            var r = new ExtRegistry();
            var h = new FakeHandler("stp3.x", log);
            r.Register(h);
            t.Throws<ArgumentException>(() => r.Register(new FakeHandler("STP3.X", log)), "T6: duplicate namespace ignoring case rejected");
            foreach (var bad in new[] { "hs2.kkex", "hs2next.core", "illusion.hs2", "HS2.foo", "-bad", "a b", "" })
                t.Throws<ArgumentException>(() => r.Register(new FakeHandler(bad, log)), "T6: reserved/malformed namespace rejected: '" + bad + "'");
            r.RegisterCore(new FakeHandler("hs2next.core", log));
            t.Check(r.Get("hs2next.core") != null, "T6: Core may register hs2next.*");
            var view = r.ForOwner("com.example.plugin");
            view.Register(new FakeHandler("com.example.plugin.sub", log));
            t.Throws<ArgumentException>(() => view.Register(new FakeHandler("com.other", log)), "T6: a plugin may register only its own GUID and <guid>.*");
            t.Check(r.Unregister("stp3.x") && r.Get("stp3.x") == null, "T6: unregister");

            // Api validation in write contexts
            var reg4 = new ExtRegistry();
            var bad4 = new FakeHandler("stp3.bad", log) { OnWrite = ctx => ctx.SetJson("{not json") };
            var badPath = new FakeHandler("stp3.badpath", log) { OnWrite = ctx => ctx.AddFile("../x", new byte[1]) };
            reg4.Register(bad4);
            reg4.Register(badPath);
            var s4 = new SaveResult();
            var b4 = SmallChara().ToBytes(Opts(reg4));
            var cj = Encoding.UTF8.GetString(ContainerReader.Read(b4).Get("chara.json").Data);
            t.Check(!cj.Contains("stp3.bad") && reg4.FaultCount("stp3.bad") == 2 && reg4.FaultCount("stp3.badpath") >= 1,
                "T6: invalid SetJson text and unsafe AddFile paths throw ArgumentException inside Write (isolated)");
        }

        static void T5Ext(TestRunner t)
        {
            // An unknown block (inline on chara, coord root, an accessory; files), foreign entries, manifest entry with an unknown member.
            var log = new List<string>();
            var reg = new ExtRegistry();
            var w = new FakeHandler("org.future", log) { Version = 9, RequiredByDefault = false };
            w.OnWrite = ctx =>
            {
                if (ctx.Scope == ExtScope.Chara) { ctx.SetJson("{\"k\": [0.1, 1E+20, \"é\\u0001\"], \"n\": null}"); ctx.AddFile("a/b.bin", new byte[] { 7, 7, 7 }); }
                if (ctx.Scope == ExtScope.Coord) { ctx.SetJson("{\"coord\": true}"); ctx.SetAccessoryJson(Id(101), "{\"acc\": 1}"); }
            };
            reg.Register(w);
            var c = SmallChara();
            c.ForeignEntries["thirdparty/info.json"] = Utf8("{ \"not\": \"canonical\" }");
            var original = c.ToBytes(Opts(reg));
            // add an unknown member to the manifest ext entry
            original = Repack(original, m => EditJson(m, "manifest.json", j => ((JObject)((JArray)j["ext"])[0])["futureFlag"] = "x"));

            var none = new ExtRegistry();
            var l = CharaContainer.Load(original, LOpts(none)).Unwrap();
            var resaved = l.ToBytes(Opts(none));
            t.Check(original.SequenceEqual(resaved), "T5: unknown blocks (inline + files + manifest entry) and foreign entries survive byte-identical");

            // chara file → scene → chara file
            var scene = new SceneContainer { Title = "move" };
            string nodeId = Id(4242);
            scene.Scene.Nodes.Add(new SceneNode(nodeId, new CharaNodeState()));
            scene.PlaceCharacter(nodeId, l);
            var sceneBytes = scene.ToBytes(Opts(none));
            var raw = ContainerReader.Read(sceneBytes);
            string root = "charas/" + nodeId + "/";
            t.Check(raw.Contains(root + "ext/org.future/a/b.bin"), "T5: ext files re-rooted under charas/<id>/");
            var mj = JObject.Parse(Encoding.UTF8.GetString(raw.Get("manifest.json").Data));
            var entries = ((JArray)mj["ext"]).Select(e => (string)e["root"] + "|" + (string)e["scope"] + "|" + (string)e["ns"] + "|" + (int)e["version"] + "|" + (string)e["futureFlag"]).ToList();
            t.Check(entries.Contains(root + "|chara|org.future|9|x") && entries.Contains(root + "coord/|coord|org.future|9|"), "T5: manifest ext entries re-rooted with version and unknown members: " + string.Join(", ", entries));
            var ls = SceneContainer.Load(sceneBytes, LOpts(none)).Unwrap();
            var back = ls.ExtractCharacter(nodeId);
            back.ForeignEntries["thirdparty/info.json"] = l.ForeignEntries["thirdparty/info.json"];
            back.LoadedManifest = l.LoadedManifest;
            back.CreatedUtc = l.CreatedUtc;
            var backBytes = back.ToBytes(Opts(none));
            t.Check(original.SequenceEqual(backBytes), "T5: character → scene → character file gives the original bytes (ext blocks and entries intact)");

            // ext data with a corrupt file drops that block only
            var bad = Repack(original, m => m["ext/org.future/a/b.bin"] = m["ext/org.future/a/b.bin"]);
            var cd = ParseZip(bad, out _, out _, out _, out _);
            var fe = cd.First(e => e.Name == "ext/org.future/a/b.bin");
            bad[fe.Lho + 30 + fe.Name.Length + fe.LocalExtraLen] ^= 0xFF;
            var lb = CharaContainer.Load(bad, LOpts(none));
            t.Check(lb.Ok && lb.Findings.Has("EntryCorrupt") && lb.Value.Chara.Ext.Get("org.future") == null && lb.Value.Coord.Ext.Get("org.future") != null,
                "T5: a corrupt ext file drops that block (chara scope) only (" + lb + ")");

            // data without a manifest entry and an entry without data
            var noEntry = Repack(original, m => EditJson(m, "manifest.json", j => { ((JArray)j["ext"]).RemoveAt(0); ((JArray)j["ext"]).Add(JObject.Parse("{\"ns\":\"org.gone\",\"scope\":\"chara\",\"root\":\"\",\"version\":1,\"required\":false}")); }));
            var ln = CharaContainer.Load(noEntry, LOpts(none));
            t.Check(ln.Ok && ln.Findings.Has("ext.noManifestEntry") && ln.Findings.Has("ext.entryWithoutData"), "T5: data without entry → version 0 + report; entry without data reported");
            var lnBytes = ln.Value.ToBytes(Opts(none));
            var mj2 = JObject.Parse(Encoding.UTF8.GetString(ContainerReader.Read(lnBytes).Get("manifest.json").Data));
            t.Check(!mj2.ToString().Contains("org.gone") && mj2["ext"].Any(e => (string)e["scope"] == "chara" && (int)e["version"] == 0), "T5: entry without data dropped at save; data without entry written with version 0");

            // orphans: charas/<id>/ without a node, nodes/<id>/ext without a node
            var orphan = Repack(sceneBytes, m =>
            {
                m["charas/" + Id(9999) + "/chara.json"] = m[root + "chara.json"];
                m["charas/" + Id(9999) + "/coord/coord.json"] = m[root + "coord/coord.json"];
                m["nodes/" + Id(8888) + "/ext/org.x/f.bin"] = new byte[] { 1 };
                m["nodes/readme.txt"] = Utf8("foreign, not nodes/<id>/ext/");
            }, "scene.json");
            var lo = SceneContainer.Load(orphan, LOpts(none));
            t.Check(lo.Ok && lo.Findings.Has("scene.orphanSubtree") && lo.Findings.Has("container.orphan"), "T5: orphan subtree and node ext reported (" + lo + ")");
            var oraw = ContainerReader.Read(lo.Value.ToBytes(Opts(none)));
            t.Check(!oraw.Entries.Any(e => e.Name.Contains(Id(9999)) || e.Name.Contains(Id(8888))) && oraw.Contains("nodes/readme.txt"), "T5: orphans dropped at save, foreign nodes/readme.txt kept");
        }

        static void T13(TestRunner t)
        {
            var c = SmallChara();
            var k = new KkexStore { Source = "hs2.chara", BlockVersion = "3" };
            k.Add("com.deathweasel.bepinex.materialeditor", "kkex", null, 0, "chara", "source", null, new byte[] { 0x92, 0, 0x80 });
            k.Add("KSOX", "kkex", null, 2, "chara", "source", null, new byte[] { 0x92, 2, 0x81, 0xA1, 0x61, 1 });
            k.Add("eyecontrol", "kkex", null, 1, "chara", "opaque", "noDecoder", new byte[] { 0x92, 1, 0xC0 });
            k.Add("com.bepis.bepinex.sideloader", "object", "ChaFileClothes", 1, "chara", "source", null, new byte[] { 0x92, 1, 0x80 });
            k.Add("weird id/ü", "kkex", null, null, "chara", "opaque", "decodeError", new byte[] { 1, 2, 3 });
            c.Subtree.CharaExt.Kkex = k;
            var kc = new KkexStore { Source = "hs2.chara", BlockVersion = "3" };
            kc.Add("KCOX", "kkex", null, 2, "chara", "source", null, new byte[] { 9 });
            kc.Add("mikke.BeaverAI", "kkex", null, 1, "chara", "opaque", "noDecoder", new byte[] { 8 });
            c.Subtree.CoordExt.Kkex = kc;
            var bytes = c.ToBytes(Opts());
            var raw = ContainerReader.Read(bytes);
            t.Check(raw.Contains("ext/hs2.kkex/index.json") && raw.Contains("ext/hs2.kkex/weird%20id%2F%C3%BC.msgpack") && raw.Contains("ext/hs2.kkex/object/ChaFileClothes/com.bepis.bepinex.sideloader.msgpack") && raw.Contains("coord/ext/hs2.kkex/KCOX.msgpack"),
                "T13: layout (index.json, E(id) names, object/<ObjectName>/, coord scope)");
            var idx = JObject.Parse(Encoding.UTF8.GetString(raw.Get("ext/hs2.kkex/index.json").Data));
            var order = string.Join(",", idx["entries"].Select(e => (string)e["origin"] + ":" + (string)e["id"]));
            t.Equal("kkex:KSOX,kkex:com.deathweasel.bepinex.materialeditor,kkex:eyecontrol,kkex:weird id/ü,object:com.bepis.bepinex.sideloader", order, "T13: index entries sorted (origin, object, id)");
            var e0 = idx["entries"][0];
            t.Check((long)e0["size"] == 6 && ((string)e0["sha256"]).Length == 64 && e0["reason"].Type == JTokenType.Null && (int)e0["version"] == 2 && (string)e0["form"] == "chara", "T13: entry fields");
            var mj = JObject.Parse(Encoding.UTF8.GetString(raw.Get("manifest.json").Data));
            t.Check(mj["ext"].Any(e => (string)e["ns"] == "hs2.kkex" && (string)e["scope"] == "chara" && (string)e["root"] == "" && (int)e["version"] == 1) &&
                    mj["ext"].Any(e => (string)e["ns"] == "hs2.kkex" && (string)e["scope"] == "coord" && (string)e["root"] == "coord/"), "T13: manifest entries for hs2.kkex");
            var l = CharaContainer.Load(bytes, LOpts()).Unwrap();
            t.Check(l.Subtree.CharaExt.Kkex.Entries.Count == 5 && l.ToBytes(Opts()).SequenceEqual(bytes), "T13: index round trip byte-identical");
            // the SHA-256 of every file is verified at load: a wrong but well-formed hash is reported and corrected on save
            var badHash = Repack(bytes, m => EditJson(m, "ext/hs2.kkex/index.json", j => j["entries"][0]["sha256"] = new string('0', 64)));
            var lh = CharaContainer.Load(badHash, LOpts());
            t.Check(lh.Ok && lh.Findings.CountOf("kkex.indexMismatch") == 1, "T13: a wrong SHA-256 in index.json is reported at load (" + lh + ")");
            t.Check(lh.Ok && lh.Value.ToBytes(Opts()).SequenceEqual(bytes), "T13: the index is corrected on save");
            var o = Opts();
            o.StripImportSources = true;
            var stripped = ContainerReader.Read(l.ToBytes(o));
            var sidx = JObject.Parse(Encoding.UTF8.GetString(stripped.Get("ext/hs2.kkex/index.json").Data));
            t.Equal("eyecontrol,weird id/ü", string.Join(",", sidx["entries"].Select(e => (string)e["id"])), "T13: StripImportSources keeps exactly the opaque entries");
            t.Check(!stripped.Contains("ext/hs2.kkex/KSOX.msgpack") && !stripped.Contains("ext/hs2.kkex/object/ChaFileClothes/com.bepis.bepinex.sideloader.msgpack") && stripped.Contains("ext/hs2.kkex/eyecontrol.msgpack"),
                "T13: source files removed, opaque files kept");
            t.Check(stripped.Contains("coord/ext/hs2.kkex/mikke.BeaverAI.msgpack") && !stripped.Contains("coord/ext/hs2.kkex/KCOX.msgpack"), "T13: coord scope stripped too");
            t.Equal(5, l.Subtree.CharaExt.Kkex.Entries.Count, "T13: the in-memory store keeps its source entries");
            // a store with only source entries disappears entirely
            var only = SmallChara();
            only.Subtree.CharaExt.Kkex = new KkexStore { Source = "hs2.chara", BlockVersion = "3" };
            only.Subtree.CharaExt.Kkex.Add("KSOX", "kkex", null, 2, "chara", "source", null, new byte[] { 1 });
            var ob = ContainerReader.Read(only.ToBytes(o));
            t.Check(!ob.Entries.Any(e => e.Name.Contains("hs2.kkex")) && !Encoding.UTF8.GetString(ob.Get("manifest.json").Data).Contains("hs2.kkex"), "T13: namespace removed when nothing is left");
        }
    }
}

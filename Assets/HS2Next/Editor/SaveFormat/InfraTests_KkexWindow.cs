// The read-only window on imported HS2 plugin data (Phase 5.1): KW1 a handler that is
// also an IKkexConsumer gets the entries of the ids it lists, both roles, with the stored fields and bytes; KW2 a consumer that
// throws or changes the bytes it was handed leaves the store byte-equal (load → save is byte-identical), its throws count as
// faults of the handler; KW3 no call for unlisted ids, for an empty id list, for a scope the handler does not take or for a
// faulted handler; KW4 the calls come after the handler's Read of the same instance, also where it has no block, and carry the
// scene owner. Group PluginExt: the registry view of one plugin (fault callback, its namespaces, removal of all of them; the
// blocks a removed handler read are written back unchanged). Synthetic ids and bytes only.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Api.Save;
using static HS2Next.Save.Tests.InfraFixtures;

namespace HS2Next.Save.Tests
{
    public static class InfraTests_KkexWindow
    {
        /// <summary>A handler that is also a kkex consumer; logs every call.</summary>
        sealed class Consumer : IExtBlockHandler, IKkexConsumer
        {
            public string Namespace { get; set; }
            public int Version => 1;
            public ExtScope Scopes { get; set; } = ExtScope.Chara | ExtScope.Coord;
            public bool RequiredByDefault => false;
            public IReadOnlyList<string> KkexIds { get; set; }
            public readonly List<string> Log = new List<string>();
            public readonly List<byte[]> Bytes = new List<byte[]>();
            public bool ThrowInKkex, Scribble;
            public string WriteJson;

            public Consumer(string ns, params string[] ids) { Namespace = ns; KkexIds = ids; }

            public void Read(IExtReadContext ctx) => Log.Add("R " + ctx.Scope + " " + (ctx.OwnerId ?? "-"));

            public void Write(IExtWriteContext ctx)
            {
                if (WriteJson != null) ctx.SetJson(WriteJson);
            }

            public void ReadKkex(IKkexReadContext ctx)
            {
                Log.Add("K " + ctx.Scope + " " + (ctx.OwnerId ?? "-") + " " + ctx.Id + " " + ctx.Origin + " " + (ctx.Object ?? "-") + " " + ctx.Role + " " +
                        (ctx.Reason ?? "-") + " v" + (ctx.Version?.ToString() ?? "-") + " " + ctx.Form + " " + ctx.Size);
                var b = ctx.ReadBytes();
                Bytes.Add((byte[])b.Clone());
                if (Scribble) for (int i = 0; i < b.Length; i++) b[i] ^= 0xFF;
                if (ThrowInKkex) throw new InvalidOperationException("consumer fault");
            }
        }

        static readonly byte[] A = { 0x92, 0x02, 0x81, 0xA1, 0x61, 0x01 }, B = { 0x92, 0x01, 0xC0 }, C = { 0x92, 0x01, 0x80 }, D = { 0x91, 0x07 }, E = { 0x90 };

        /// <summary>A character with imported entries in both scopes and an inline block of stp3.kk in the chara scope.</summary>
        static CharaContainer Card()
        {
            var c = SmallChara();
            var k = new KkexStore { Source = "hs2.chara", BlockVersion = "3" };
            k.Add("test.alpha", "kkex", null, 2, "chara", "source", null, A);
            k.Add("test.beta", "kkex", null, 1, "chara", "opaque", "noDecoder", B);
            k.Add("test.alpha", "object", "CardPart", null, "chara", "opaque", "decodeError", C);
            k.Add("test.gamma", "kkex", null, 1, "chara", "opaque", "noDecoder", E);
            c.Subtree.CharaExt.Kkex = k;
            var kc = new KkexStore { Source = "hs2.chara", BlockVersion = "3" };
            kc.Add("test.alpha", "kkex", null, 3, "coord", "opaque", "unsupportedVersion", D);
            c.Subtree.CoordExt.Kkex = kc;
            c.Chara.Ext.Set("stp3.kk", Newtonsoft.Json.Linq.JToken.Parse("{\"mine\": 1}"));
            return c;
        }

        static ExtRegistry Reg(params IExtBlockHandler[] hs)
        {
            var r = new ExtRegistry();
            foreach (var h in hs) r.Register(h);
            return r;
        }

        public static void Run(TestRunner t)
        {
            var bytes = Card().ToBytes(Opts());

            // KW1 / KW4
            var kk = new Consumer("stp3.kk", "test.alpha", "test.beta");
            var l = CharaContainer.Load(bytes, LOpts(Reg(kk)));
            t.Check(l.Ok && !l.Findings.HasErrors, "KW1: loads with a consumer (" + l + ")");
            var expect = new[]
            {
                "R Chara -",
                "K Chara - test.alpha kkex - source - v2 chara 6",
                "K Chara - test.beta kkex - opaque noDecoder v1 chara 3",
                "K Chara - test.alpha object CardPart opaque decodeError v- chara 3",
                "K Coord - test.alpha kkex - opaque unsupportedVersion v3 coord 2",
            };
            // the stored order is (origin, object, id): kkex entries by id, then the object entry
            var stored = new[] { expect[0], expect[1], expect[2], expect[3], expect[4] };
            t.Check(kk.Log.SequenceEqual(stored), "KW1/KW4: ids matched in both roles and scopes, stored fields, Read of the instance first, the coord instance without a block of its own still called\n    got: " + string.Join(" | ", kk.Log));
            t.Check(kk.Bytes.Count == 4 && kk.Bytes[0].SequenceEqual(A) && kk.Bytes[1].SequenceEqual(B) && kk.Bytes[2].SequenceEqual(C) && kk.Bytes[3].SequenceEqual(D),
                "KW1: the bytes handed out equal the stored bytes");
            if (l.Ok)
            {
                var st = l.Value.Subtree.CharaExt.Kkex;
                t.Check(st.Files.Values.Any(f => f.SequenceEqual(A)) && l.Value.ToBytes(Opts()).SequenceEqual(bytes), "KW1: load → save without the consumer's own writes is byte-identical");
            }

            // KW2: a scribbling, throwing consumer: the store stays byte-equal; the throws are faults of the handler
            var bad = new Consumer("stp3.kk", "test.alpha", "test.beta") { ThrowInKkex = true, Scribble = true };
            var reg2 = Reg(bad);
            var l2 = CharaContainer.Load(bytes, LOpts(reg2));
            t.Check(l2.Ok && l2.Findings.CountOf("ext.handlerFault") == 4 && reg2.FaultCount("stp3.kk") == 4, "KW2: four ReadKkex throws → four ext.handlerFault, counted for the handler (" + l2 + ")");
            t.Check(l2.Ok && l2.Value.ToBytes(Opts()).SequenceEqual(bytes), "KW2: the store is written back byte-equal after the consumer scribbled on its copies and threw");
            for (int i = 0; i < 2; i++) CharaContainer.Load(bytes, LOpts(reg2));
            t.Check(reg2.IsFaulted("stp3.kk"), "KW2: ten counted faults → the handler is faulted");
            bad.Log.Clear();
            var l3 = CharaContainer.Load(bytes, LOpts(reg2));
            t.Check(l3.Ok && !bad.Log.Any(x => x.StartsWith("K ", StringComparison.Ordinal)), "KW3: a faulted handler gets no ReadKkex");

            // KW3: unlisted ids, empty and null lists, a scope the handler does not take, a plain handler
            var only = new Consumer("stp3.kk", "test.beta", "test.missing", null);
            CharaContainer.Load(bytes, LOpts(Reg(only)));
            t.Check(only.Log.Count(x => x.StartsWith("K ", StringComparison.Ordinal)) == 1 && only.Log.Contains("K Chara - test.beta kkex - opaque noDecoder v1 chara 3"),
                "KW3: only the listed id that exists is handed out (no call for test.alpha, test.gamma or missing ids)\n    got: " + string.Join(" | ", only.Log));
            var empty = new Consumer("stp3.kk");
            var nul = new Consumer("stp3.kn") { KkexIds = null };
            CharaContainer.Load(bytes, LOpts(Reg(empty, nul)));
            t.Check(!empty.Log.Concat(nul.Log).Any(x => x.StartsWith("K ", StringComparison.Ordinal)), "KW3: an empty or null id list → no call");
            var charaOnly = new Consumer("stp3.kk", "test.alpha") { Scopes = ExtScope.Chara };
            CharaContainer.Load(bytes, LOpts(Reg(charaOnly)));
            t.Check(charaOnly.Log.Count(x => x.StartsWith("K Chara", StringComparison.Ordinal)) == 2 && !charaOnly.Log.Any(x => x.StartsWith("K Coord", StringComparison.Ordinal)),
                "KW3: a handler without the coord scope gets no coord entries");
            var log = new List<string>();
            var plain = new FakeHandler("stp3.kk", log);
            var lp = CharaContainer.Load(bytes, LOpts(Reg(plain)));
            t.Check(lp.Ok && log.Count == 1 && log[0].StartsWith("R stp3.kk Chara", StringComparison.Ordinal), "KW3: a handler that is not a consumer sees only its block");

            // KW4: a scene character carries its owner; a consumer that writes its own block keeps the store unchanged
            var scene = SmallScene(1, 0, Card());
            var sb = scene.ToBytes(Opts());
            var sk = new Consumer("stp3.kk", "test.alpha");
            var ls = SceneContainer.Load(sb, LOpts(Reg(sk)));
            string owner = Id(1000);
            t.Check(ls.Ok && sk.Log.FirstOrDefault() == "R Chara " + owner && sk.Log.Contains("K Coord " + owner + " test.alpha kkex - opaque unsupportedVersion v3 coord 2"),
                "KW4: scene characters: Read first, the calls carry the node id\n    got: " + string.Join(" | ", sk.Log));
            var writer = new Consumer("stp3.kk", "test.alpha") { WriteJson = "{\"decoded\": true}" };
            var lw = CharaContainer.Load(bytes, LOpts(Reg(writer)));
            if (lw.Ok)
            {
                var back = CharaContainer.Load(lw.Value.ToBytes(Opts(Reg(writer))), LOpts()).Unwrap();
                t.Check(back.Subtree.CharaExt.Kkex.Save().Values.Zip(Card().Subtree.CharaExt.Kkex.Save().Values, (x, y) => x.SequenceEqual(y)).All(x => x) &&
                        back.Subtree.CoordExt.Kkex.Save().Count == 2 && back.Chara.Ext.Get("stp3.kk")?.ToString(Newtonsoft.Json.Formatting.None) == "{\"decoded\":true}",
                    "KW4: a consumer's own block is saved next to the unchanged store");
            }
            else t.Check(false, "KW4: load with a writing consumer (" + lw + ")");

            PluginExt(t);
        }

        /// <summary>A handler that throws in Read (to drive the owner's fault callback).</summary>
        sealed class Thrower : IExtBlockHandler
        {
            public string Namespace { get; set; }
            public int Version => 1;
            public ExtScope Scopes => ExtScope.Chara | ExtScope.Coord;
            public bool RequiredByDefault => false;
            public void Read(IExtReadContext ctx) => throw new InvalidOperationException("read fault");
            public void Write(IExtWriteContext ctx) => ctx.SetJson("[1]");
        }

        static void PluginExt(TestRunner t)
        {
            var reg = new ExtRegistry();
            var faults = new List<string>();
            var view = reg.ForOwner("stp3.plugin", (ns, hook, ex) => faults.Add(ns + "/" + hook + "/" + ex.Message));
            var log = new List<string>();
            view.Register(new FakeHandler("stp3.plugin.b", log));
            view.Register(new Thrower { Namespace = "stp3.plugin" });
            t.Throws<ArgumentException>(() => view.Register(new FakeHandler("stp3.other", log)), "PluginExt: the view refuses another owner's namespace");
            t.Throws<ArgumentException>(() => view.Register(new FakeHandler("hs2next.x", log)), "PluginExt: the view refuses a reserved namespace");
            reg.Register(new FakeHandler("stp3.pluginx", log));
            t.Equal("stp3.plugin,stp3.plugin.b", string.Join(",", reg.NamespacesOf("stp3.plugin")), "PluginExt: NamespacesOf lists the GUID and <guid>.* (not stp3.pluginx)");
            t.Equal("stp3.plugin", string.Join(",", reg.NamespacesOf("STP3.PLUGIN").Take(1)), "PluginExt: owners compare ignoring case");

            // the fault callback: told after the registry counted it; a throwing callback is ignored
            var c = SmallChara();
            c.Chara.Ext.Set("stp3.plugin", Newtonsoft.Json.Linq.JToken.Parse("[1]"));
            c.Chara.Ext.Set("stp3.plugin.b", Newtonsoft.Json.Linq.JToken.Parse("{\"b\": 2}"));
            var bytes = c.ToBytes(Opts());
            var l = CharaContainer.Load(bytes, LOpts(reg));
            t.Check(l.Ok && faults.Count == 1 && faults[0] == "stp3.plugin/Read/read fault" && reg.FaultCount("stp3.plugin") == 1, "PluginExt: the owner's callback is told of the fault (" + string.Join(" | ", faults) + ")");
            var reg2 = new ExtRegistry();
            reg2.ForOwner("stp3.plugin", (ns, hook, ex) => throw new Exception("callback fault")).Register(new Thrower { Namespace = "stp3.plugin" });
            var l2 = CharaContainer.Load(bytes, LOpts(reg2));
            t.Check(l2.Ok && reg2.FaultCount("stp3.plugin") == 1, "PluginExt: a throwing callback does not stop the load");

            // removal of every owned handler: blocks they read are written back unchanged, later loads keep them unknown
            var reg3 = new ExtRegistry();
            var log3 = new List<string>();
            var v3 = reg3.ForOwner("stp3.plugin");
            v3.Register(new FakeHandler("stp3.plugin", log3) { Scopes = ExtScope.Chara, OnWrite = ctx => ctx.SetJson("\"rewritten\"") });
            v3.Register(new FakeHandler("stp3.plugin.b", log3) { Scopes = ExtScope.Chara, OnWrite = ctx => ctx.SetJson("\"rewritten\"") });
            reg3.Register(new FakeHandler("stp3.keep", log3));
            var l3 = CharaContainer.Load(bytes, LOpts(reg3)).Unwrap();
            t.Equal(2, reg3.UnregisterOwner("stp3.plugin"), "PluginExt: UnregisterOwner removes the owner's two handlers");
            t.Check(reg3.Get("stp3.plugin") == null && reg3.Get("stp3.plugin.b") == null && reg3.Get("stp3.keep") != null, "PluginExt: another owner's handler stays");
            t.Check(l3.ToBytes(Opts(reg3)).SequenceEqual(bytes), "PluginExt: after the removal the blocks the handlers read are saved byte-equal (no data loss)");
            var l4 = CharaContainer.Load(bytes, LOpts(reg3));
            t.Check(l4.Ok && l4.Findings.CountOf("ext.unknown") == 2 && l4.Value.ToBytes(Opts(reg3)).SequenceEqual(bytes), "PluginExt: a later load keeps them unknown and writes them back");
            t.Equal(0, reg3.UnregisterOwner("stp3.plugin"), "PluginExt: a second removal finds nothing");
        }
    }
}

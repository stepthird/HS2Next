// D1 tests of the JSON layer: canonical writer text, float32 text, reader settings,
// typed getters, lenient mode, leftovers and ext round trip, NaN refusal (part of T8).
using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save.Tests
{
    public static class InfraTests_Json
    {
        static string Text(Action<CanonicalJsonWriter> body) => Encoding.UTF8.GetString(CanonicalJsonWriter.ToBytes("t.json", body));

        public static void Run(TestRunner t)
        {
            // float32 text
            t.Equal("1.0", JsonText.F32(1f), "T(1)");
            t.Equal("0.1", JsonText.F32(0.1f), "T(0.1)");
            t.Equal("1E+20", JsonText.F32(1e20f), "T(1e20)");
            t.Equal("8.513037E+08", JsonText.F32(8.513037E+08f), "T(8.513037E+08)");
            t.Equal("0.0", JsonText.F32(-0f), "T(-0) = 0.0");
            t.Equal("1E-07", JsonText.F32(1e-7f), "T(1e-7)");
            t.Check(JsonText.F32(-1e-45f).StartsWith("-"), "T(-1e-45) keeps sign");
            t.Equal("16777216.0", JsonText.F32(16777216f), "T(2^24)");
            t.ThrowsCode(SaveErrorCode.NonFinite, () => JsonText.F32(float.NaN), "T(NaN) refused");

            // writer layout
            string s = Text(w =>
            {
                w.StartObject();
                w.F32("a", 0.5f);
                w.V3("v", new Vec3(0.5f, 1f, 1e-7f));
                w.StartArray("aa"); w.F32Array(new[] { 1f, 2f }); w.F32Array(new[] { 3f }); w.EndArray();
                w.Str("s", "日本語\u0001\"x</");
                w.StartObject("o"); w.EndObject();
                w.StartArray("e"); w.EndArray();
                w.Null("n");
                w.IntArray("i", new[] { 1, -2 });
                w.BoolArray("b", new[] { true, false });
                w.StrArray("sa", new[] { "x", "y" });
                w.Map("m", new Dictionary<string, bool> { { "b", true }, { "a", false }, { "B", true } }, (ww, v) => ww.Bool(v));
                w.EndObject();
            });
            string expected = "{\n  \"a\": 0.5,\n  \"v\": [0.5, 1.0, 1E-07],\n  \"aa\": [\n    [1.0, 2.0],\n    [3.0]\n  ],\n" +
                "  \"s\": \"日本語\\u0001\\\"x</\",\n  \"o\": {},\n  \"e\": [],\n  \"n\": null,\n  \"i\": [1, -2],\n  \"b\": [true, false],\n" +
                "  \"sa\": [\n    \"x\",\n    \"y\"\n  ],\n  \"m\": {\n    \"B\": true,\n    \"a\": false,\n    \"b\": true\n  }\n}\n";
            t.Equal(expected, s, "writer layout");

            // NonFinite with path, no partial success
            var e = t.ThrowsCode(SaveErrorCode.NonFinite, () => Text(w => { w.StartObject(); w.StartObject("face"); w.F32Array("shape", new[] { 0f, 1f, float.PositiveInfinity }); w.EndObject(); w.EndObject(); }), "NonFinite in array");
            t.Equal("face.shape[2]", e?.JsonPath, "NonFinite path");
            e = t.ThrowsCode(SaveErrorCode.NonFinite, () => Text(w => { w.StartObject(); w.StartArray("x"); w.StartObject(); w.F32("y", float.NaN); w.EndObject(); w.EndArray(); w.EndObject(); }), "NonFinite member");
            t.Equal("x[0].y", e?.JsonPath, "NonFinite member path");
            t.Throws<InvalidOperationException>(() => Text(w => { w.StartObject(); w.Int("a", 1); w.Int("a", 2); w.EndObject(); }), "duplicate member write refused");

            // reader settings
            t.ThrowsCode(SaveErrorCode.BadDocument, () => DocReader.Parse(Encoding.UTF8.GetBytes("{\"a\":1,\"a\":2}"), "d.json"), "duplicate key is an error");
            e = t.ThrowsCode(SaveErrorCode.NonFinite, () => DocReader.Parse(Encoding.UTF8.GetBytes("{\"a\":{\"b\":[1.0, NaN]}}"), "d.json"), "NaN literal on read");
            t.Equal("a.b[1]", e?.JsonPath, "NaN literal path");
            var jo = DocReader.Parse(Encoding.UTF8.GetBytes("\uFEFF{\"t\": \"2026-10-16T01:02:03Z\"}"), "d.json");
            t.Equal(JTokenType.String, jo["t"].Type, "ISO time stays a string; BOM tolerated");
            t.ThrowsCode(SaveErrorCode.BadDocument, () => DocReader.Parse(Encoding.UTF8.GetBytes("{} {}"), "d.json"), "content after root");
            t.ThrowsCode(SaveErrorCode.BadDocument, () => DocReader.Parse(Encoding.UTF8.GetBytes("[1]"), "d.json"), "root must be object");
            t.ThrowsCode(SaveErrorCode.BadManifest, () => DocReader.Parse(Encoding.UTF8.GetBytes("{"), "manifest.json", SaveErrorCode.BadManifest), "syntax error code");

            // typed getters
            var ctx = new DocReadContext("d.json");
            var r = new DocReader(DocReader.Parse(Encoding.UTF8.GetBytes(
                "{\"f\": 3, \"i\": 3.0, \"v\": [1, 2, 3], \"c\": [1, 0.5, 0, 1], \"r\": \"a b [x]#-5\", \"id\": \"0123456789ABCDEF0123456789abcdef\"," +
                " \"res\": \"res/" + new string('a', 64) + ".png\", \"tm\": \"2026-10-16T01:02:03Z\", \"o\": {\"x\": 1, \"zz\": [1, {\"q\": 2}]}," +
                " \"arr\": [{\"k\": 1}, {\"k\": 2}], \"map\": {\"b\": [0, 0, 1], \"a\": [1, 0, 0]}, \"en\": \"weird\", \"unk1\": {\"deep\": [0.1, 8.513037E+08]}," +
                " \"ext\": {\"stp3.wetlab\": {\"x\": 1}}, \"unk2\": \"tail\", \"big\": 1E+39}"), "d.json"), ctx);
            t.Equal(3f, r.F32("f", 0), "int token read as float");
            t.Equal(3, r.Int("i", 0), "integral float read as int");
            t.Equal(new Vec3(1, 2, 3), r.V3("v", Vec3.Zero), "v3");
            t.Equal(new Color4(1, 0.5f, 0, 1), r.Col("c", Color4.White), "col");
            t.Equal("a b [x]#-5", r.Ref("r"), "ref verbatim");
            t.Equal("0123456789abcdef0123456789abcdef", r.Id("id"), "id lowercased");
            t.Check(ctx.Findings.Has("json.idUppercase"), "id uppercase reported");
            t.Equal("res/" + new string('a', 64) + ".png", r.Res("res"), "res");
            t.Equal(new DateTime(2026, 10, 16, 1, 2, 3, DateTimeKind.Utc), r.Time("tm"), "time");
            var o = r.Obj("o");
            t.Equal(1, o.Int("x", 0), "nested int");
            var ol = o.Leftovers();
            t.Check(ol != null && ol.Count == 1 && ol.Members[0].Key == "zz", "nested leftovers");
            t.Equal(2, r.ObjArray("arr").Count, "obj array");
            var map = r.Map("map", v => v.AsV3());
            t.Equal("a", new List<string>(map.Keys)[0], "map sorted");
            t.Equal("weird", r.EnumStr("en", "none", new[] { "none", "all" }), "unknown enum kept");
            t.Check(ctx.Findings.Has("json.unknownEnum"), "unknown enum reported");
            t.Equal(7, r.Int("missing", 7), "absent value");
            t.ThrowsCode(SaveErrorCode.BadDocument, () => r.Int("missing2"), "required member missing");
            t.ThrowsCode(SaveErrorCode.BadDocument, () => r.Int("r", 0), "type error strict");
            t.ThrowsCode(SaveErrorCode.NonFinite, () => r.F32("big", 0), "float out of range → NonFinite");
            var ext = r.Ext();
            t.Equal(1, ext.Count, "ext captured");
            var left = r.Leftovers();
            t.Check(left != null && left.Count == 2 && left.Members[0].Key == "unk1" && left.Members[1].Key == "unk2", "leftovers in read order without ext and known");

            // leftovers + ext written back canonically
            string back = Text(w => { w.StartObject(); w.Int("f", 3); w.Leftovers(left); w.Ext(ext); w.EndObject(); });
            t.Equal("{\n  \"f\": 3,\n  \"unk1\": {\n    \"deep\": [0.1, 8.513037E+08]\n  },\n  \"unk2\": \"tail\",\n  \"ext\": {\n    \"stp3.wetlab\": {\n      \"x\": 1\n    }\n  }\n}\n", back, "leftovers and ext written back");

            // lenient
            var lctx = new DocReadContext("d.json", lenient: true);
            var lr = new DocReader(DocReader.Parse(Encoding.UTF8.GetBytes("{\"a\": \"x\", \"v\": [1, 2], \"o\": 5}"), "d.json"), lctx);
            t.Equal(9, lr.Int("a", 9), "lenient: absent value on type error");
            t.Equal(Vec3.One, lr.V3("v", Vec3.One), "lenient: wrong length → absent");
            t.Equal(0, lr.Obj("o").Int("x", 0), "lenient: bad object → empty reader");
            t.Equal(3, lctx.Findings.CountOf("json.typeError"), "lenient: findings");
            t.Check(lr.Leftovers() == null, "lenient: bad tokens dropped, not leftovers");

            // raw token canonical round trip: canonical text → parse → write = same bytes
            string canon = "{\n  \"a\": [\n    [0.1, 2.0],\n    {\n      \"b\": null\n    }\n  ],\n  \"c\": 1E+20,\n  \"d\": 0.123456789012345,\n  \"e\": -12345678901234,\n  \"f\": \"\\u0000é\"\n}\n";
            var tok = DocReader.Parse(Encoding.UTF8.GetBytes(canon), "x.json");
            t.Equal(canon, Encoding.UTF8.GetString(CanonicalJsonWriter.ToBytes(tok)), "token round trip byte-identical");

            // random float32 bit patterns survive text → double → float (T2-style spot check)
            var rnd = new Random(1234);
            int bad = 0;
            var buf = new byte[4];
            for (int i = 0; i < 200000; i++)
            {
                rnd.NextBytes(buf);
                float f = BitConverter.ToSingle(buf, 0);
                if (float.IsNaN(f) || float.IsInfinity(f)) continue;
                string txt = JsonText.F32(f);
                var vv = new DocValue(DocReader.ParseText(txt, null), new DocReadContext(null)).AsF32();
                if (f == 0 ? vv != 0 || BitConverter.ToInt32(BitConverter.GetBytes(vv), 0) != 0 : BitConverter.ToInt32(BitConverter.GetBytes(vv), 0) != BitConverter.ToInt32(buf, 0)) bad++;
                if (JsonText.F64(double.Parse(txt, System.Globalization.CultureInfo.InvariantCulture)) != txt) bad++;
            }
            t.Equal(0, bad, "200k random floats: text round trip bit-exact (−0 → +0) and F64 rule reproduces T");

            // entry names
            t.Equal("KCOX", EntryNames.Escape("KCOX"), "E keeps safe");
            t.Equal("a%20b%2F%E3%81%82", EntryNames.Escape("a b/あ"), "E escapes bytes uppercase");
            t.Equal("a b/あ", EntryNames.Unescape("a%20b%2F%E3%81%82"), "unescape");
            t.Equal("%2E%2E", EntryNames.Escape(".."), "E of ..");
            t.Check(EntryNames.IsConforming("ext/hs2.kkex/object/ChaFileClothes/x%20y.msgpack"), "conforming");
            t.Check(!EntryNames.IsConforming("a/../b") && !EntryNames.IsConforming("a//b") && !EntryNames.IsConforming("é"), "non-conforming");
            foreach (var bad2 in new[] { "../x", "/x", "a\\b", "C:x", "a//b", "a\u0000b", "./a", "a/." })
                t.Check(EntryNames.Unsafe(bad2) != null, "unsafe: " + bad2.Replace("\u0000", "\\0"));
            t.Check(EntryNames.Unsafe("hello world/é.txt") == null, "safe but non-conforming");
        }
    }
}

// ExtendedSave (`KKEx`) trailer and `PoseToolsData` tests on synthetic trailers:
// decoded view with raw slices, unknown GUIDs and value types surviving, Encode() identity and edits, non-KKEx and
// malformed trailers kept raw, truncation of the trailer at every offset, PoseToolsData with and without skirt FK.
namespace HS2Next.Import.Pose.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using HS2Next.Import.Pose;
    using HS2Next.Save.Tests;

    public static class PoseTrailer
    {
        /// <summary>A block with ExtendedSave, an unknown GUID carrying many MessagePack types, and PoseToolsData.</summary>
        public static byte[] RichBlock(out byte[] unknownValue)
        {
            var unk = new Mp().Arr(2).FixInt(7).Map(9)
                .Str("map").Map(2).FixInt(1).Str("one").Str("neg").Raw(0xE0)
                .Str("arr16").Arr(16).Raw(Enumerable.Range(0, 16).Select(i => (byte)i).ToArray())
                .Str("bin").Bin(new byte[300])
                .Str("ext").Ext(99, new byte[] { 1, 2, 3 })
                .Str("f64").F64(-0.0)
                .Str("nil").Nil()
                .Str("long").Raw(0xD3, 0x80, 0, 0, 0, 0, 0, 0, 1)
                .Str("u64").Raw(0xCF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF)
                .Str(new string('k', 40)).Str(new string('v', 300));
            unknownValue = unk.ToArray();
            var m = new Mp().Map(3);
            PoseKit.ExtendedSaveEntry(m);
            m.Str("com.example.unknown").Raw(unknownValue);
            PoseKit.PoseToolsEntry(m, 0, 0, 11, 1f, 0f, Enumerable.Repeat(true, 8).ToArray(), null);
            return m.ToArray();
        }

        public static void Run(TestRunner t)
        {
            var block = RichBlock(out var unkValue);
            var trailer = PoseKit.KKEx(block);
            var s = PoseSpec.Standard(31);
            s.Trailer = trailer;
            var file = s.Build();
            var f = PoseFile.Read(file);
            t.Check(PoseKit.Same(trailer, f.Trailer), "trailer kept as raw bytes");
            var x = f.Ext;
            t.Check(x != null && x.Status == KKExStatus.Ok && x.Problem == null, "KKEx decoded: " + x);
            t.Check(x.Version == 3 && x.DeclaredLength == block.Length, "version 3 and length");
            t.Check(x.Entries.Select(e => e.Guid).SequenceEqual(new[] { KKExTrailer.ExtendedSaveGuid, "com.example.unknown", PoseToolsData.Guid }), "entries in file order");
            var u = x.Find("com.example.unknown");
            t.Check(u != null && PoseKit.Same(unkValue, u.RawValue) && u.Version == 7 && u.Problem == null, "unknown GUID: raw value slice and version");
            t.Check(u.Data is Dictionary<object, object> d && d.Count == 9 && d["f64"] is double z && BitConverter.DoubleToInt64Bits(z) == long.MinValue
                    && d["u64"] is ulong && (long)d["long"] == long.MinValue + 1 && d["ext"] is PoseMsgPackExt ext && ext.Type == 99 && d["nil"] == null
                    && ((byte[])d["bin"]).Length == 300 && ((List<object>)d["arr16"]).Count == 16, "unknown GUID: every MessagePack type decoded");
            t.Equal(4, f.GameName, "gameName 4");
            t.Check(PoseKit.Same(trailer, x.Encode()), "Encode() reproduces the trailer");
            t.Check(PoseKit.Same(file, f.Write()), "file with trailer: Write is byte-identical");

            // edits through the view
            x.Entries.RemoveAt(1);
            var e1 = KKExTrailer.Parse(x.Encode());
            t.Check(e1.Status == KKExStatus.Ok && e1.Entries.Count == 2 && e1.Find("com.example.unknown") == null && PoseKit.Same(e1.Find(PoseToolsData.Guid).RawValue, f.Ext.Find(PoseToolsData.Guid).RawValue), "remove an entry: others unchanged");
            var added = KKExEntry.Create("com.hs2next.test", 2, new Mp().Map(1).Str("a").FixInt(1).ToArray());
            e1.Entries.Add(added);
            var e2 = KKExTrailer.Parse(e1.Encode());
            t.Check(e2.Entries.Count == 3 && e2.Find("com.hs2next.test")?.Version == 2 && ((Dictionary<object, object>)e2.Find("com.hs2next.test").Data)["a"] is long one && one == 1, "add an entry with KKExEntry.Create");
            e2.Find(KKExTrailer.ExtendedSaveGuid).SetValue(0, new Mp().Map(1).Str("gameName").FixInt(5).ToArray());
            f.Trailer = e2.Encode();
            t.Check(f.GameName == 5 && f.Ext.Entries.Count == 3, "SetValue + assigning Trailer refreshes the views");

            // empty block, extra bytes
            var emptyBlock = KKExTrailer.Parse(PoseKit.KKEx(new byte[0]));
            t.Check(emptyBlock.Status == KKExStatus.Ok && emptyBlock.Entries.Count == 0 && PoseKit.Same(PoseKit.KKEx(new byte[0]), emptyBlock.Encode()), "length 0: no entries, Encode identical");
            var tail = PoseKit.Concat(PoseKit.KKEx(block), new byte[] { 9, 9, 9 });
            var tx = KKExTrailer.Parse(tail);
            t.Check(tx.Status == KKExStatus.Ok && tx.TrailingBytes?.Length == 3 && PoseKit.Same(tail, tx.Encode()), "bytes after the block kept");
            var inner = PoseKit.KKEx(PoseKit.Concat(block, new byte[] { 0xC0, 0xC0 }));
            var ix = KKExTrailer.Parse(inner);
            t.Check(ix.Status == KKExStatus.Ok && ix.BlockTail?.Length == 2 && PoseKit.Same(inner, ix.Encode()), "bytes after the map inside the block kept");
            var fresh = KKExTrailer.CreateEmpty();
            fresh.Entries.Add(KKExEntry.Create(PoseToolsData.Guid, 0, new Mp().Map(0).ToArray()));
            var fp = KKExTrailer.Parse(fresh.Encode());
            t.Check(fp.Status == KKExStatus.Ok && fp.Version == 3 && fp.Entries.Count == 1, "CreateEmpty + Create + Encode");

            // not KKEx / malformed: kept raw, flagged
            var cases = new (string label, byte[] bytes, KKExStatus status, PoseFileFlags flag)[]
            {
                ("random bytes", new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 }, KKExStatus.NotKKEx, PoseFileFlags.TrailerNotKKEx),
                ("other marker", PoseKit.KKEx(block, marker: "KKEy"), KKExStatus.NotKKEx, PoseFileFlags.TrailerNotKKEx),
                ("length beyond the end", PoseKit.KKEx(block, lengthOverride: block.Length + 1), KKExStatus.Malformed, PoseFileFlags.TrailerMalformed),
                ("negative length", PoseKit.KKEx(block, lengthOverride: -4), KKExStatus.Malformed, PoseFileFlags.TrailerMalformed),
                ("map count larger than the entries", PoseKit.KKEx(PoseKit.Concat(new byte[] { 0x84 }, block.Skip(1).ToArray())), KKExStatus.Malformed, PoseFileFlags.TrailerMalformed),
                ("unknown type byte", PoseKit.KKEx(new byte[] { 0x81, 0xA1, 0x61, 0xC1 }), KKExStatus.Malformed, PoseFileFlags.TrailerMalformed),
                ("marker only", PoseKit.Prefix(PoseKit.KKEx(block), 7), KKExStatus.Malformed, PoseFileFlags.TrailerMalformed),
                ("deep nesting", PoseKit.KKEx(PoseKit.Concat(new byte[] { 0x81, 0xA1, 0x61 }, Enumerable.Repeat((byte)0x91, 200).ToArray(), new byte[] { 0xC0 })), KKExStatus.Malformed, PoseFileFlags.TrailerMalformed),
            };
            foreach (var (label, bytes, status, flag) in cases)
            {
                var cs = PoseSpec.Standard(32);
                cs.Trailer = bytes;
                var cb = cs.Build();
                var cf = PoseFile.Read(cb);
                t.Check(cf.Ext.Status == status && (cf.Flags & flag) != 0 && PoseKit.Same(bytes, cf.Ext.Encode()) && PoseKit.Same(cb, cf.Write()),
                    $"trailer \"{label}\": {status}, flagged, kept raw ({cf.Ext.Problem})");
            }

            // every prefix of a trailer
            int fails = 0;
            string first = null;
            for (int len = 1; len <= trailer.Length; len++)
            {
                var cut = PoseKit.Prefix(trailer, len);
                try
                {
                    var k = KKExTrailer.Parse(cut);
                    bool ok = PoseKit.Same(cut, k.Encode()) && (len == trailer.Length ? k.Status == KKExStatus.Ok : k.Status != KKExStatus.Ok || len < 5);
                    if (!ok) { fails++; first = first ?? $"len {len}: {k}"; }
                    foreach (var e in k.Entries) { var _ = e.Data; }
                    PoseToolsData.From(k.Find(PoseToolsData.Guid));
                }
                catch (Exception e) { fails++; first = first ?? $"len {len}: {e.GetType().Name} {e.Message}"; }
            }
            t.Check(fails == 0, $"trailer cut at every offset ({trailer.Length}): never throws, Encode keeps the bytes, only the full trailer is Ok" + (first != null ? "; first " + first : ""));
        }
    }

    public static class PoseToolsDataView
    {
        public static void Run(TestRunner t)
        {
            var joint = new[] { true, false, true, true, false, true, true, true };
            // without skirt
            var m = new Mp().Map(2);
            PoseKit.PoseToolsEntry(m, 3, 7, 11, 0.75f, 0.25f, joint, null);
            PoseKit.ExtendedSaveEntry(m);
            var s = PoseSpec.Standard(41);
            s.Trailer = PoseKit.KKEx(m.ToArray());
            var f = PoseFile.Read(s.Build());
            var p = f.PoseTools;
            t.Check(p != null && p.Problems.Count == 0 && p.UnknownFields.Count == 0, "PoseToolsData decoded without problems");
            t.Check(p.Version == 0 && p.EyebrowPattern == 3 && p.EyesPattern == 7 && p.MouthPattern == 11, "pattern numbers");
            t.Check(p.EyeOpen == 0.75f && p.MouthOpen == 0.25f && p.EyeOpenBits == 0x3F400000u && p.MouthOpenBits == 0x3E800000u, "open values and float32 bits");
            t.Check(p.JointCorrection != null && p.JointCorrection.SequenceEqual(joint), "8 joint-correction categories");
            t.Check(p.SkirtFK == null, "no SkirtFKData");
            t.Check(p.FieldOrder.SequenceEqual(new[] { "EyebrowPatternData", "EyesPatternData", "MouthPatternData", "EyeOpenData", "MouthOpenData", "JointCorrectionData" }), "field order");
            t.Check(p.RawFields["MouthPatternData"].SequenceEqual(new byte[] { 0xD2, 0, 0, 0, 11 }), "raw field bytes kept (int32 form)");

            // with skirt: 48 keys 400..447, special floats
            var skirt = new List<(int key, uint x, uint y, uint z)>();
            for (int k = 400; k < 448; k++) skirt.Add((k, FloatBits.ToBits(k * 0.5f), k == 410 ? 0x80000000u : FloatBits.ToBits(-k), k == 420 ? 0x7FC0BEEFu : 0u));
            var m2 = new Mp().Map(1);
            PoseKit.PoseToolsEntry(m2, 0, 0, 0, 1f, 0f, Enumerable.Repeat(true, 8).ToArray(), skirt);
            var s2 = PoseSpec.Standard(42);
            s2.Trailer = PoseKit.KKEx(m2.ToArray());
            var b2 = s2.Build();
            var f2 = PoseFile.Read(b2);
            var p2 = f2.PoseTools;
            t.Check(p2 != null && p2.Problems.Count == 0 && p2.SkirtFK != null && p2.SkirtFK.Count == 48, "SkirtFKData: 48 values");
            t.Check(p2.SkirtFK.Select(e => e.Key).SequenceEqual(Enumerable.Range(400, 48)), "skirt keys 400..447 in file order");
            t.Check(p2.SkirtFK.Zip(skirt, (a, b) => a.XBits == b.x && a.YBits == b.y && a.ZBits == b.z).All(ok => ok), "skirt values bit-exact (−0, NaN payload)");
            t.Check(p2.SkirtFK[2].Rot.Equals(new PoseVector3(201, -402, 0)), "skirt Euler view");
            t.Check(p2.FieldOrder.IndexOf("SkirtFKData") == 5, "SkirtFKData before JointCorrectionData as the plugin writes it");
            t.Check(PoseKit.Same(b2, f2.Write()), "PoseToolsData with skirt: Write is byte-identical");

            // unknown fields, wrong types, plain-array forms
            var m3 = new Mp().Map(1).Str(PoseToolsData.Guid).Arr(2).FixInt(1).Map(5)
                .Str("EyeOpenData").Str("wide")
                .Str("JointCorrectionData").Arr(3).Bool(true).Bool(false).FixInt(1)
                .Str("SkirtFKData").Map(1).FixInt(5).Arr(3).F64(1.5).FixInt(2).F32(3)
                .Str("FutureField").Arr(0)
                .Str("MouthPatternData").Raw(0xCF, 0xFF, 0, 0, 0, 0, 0, 0, 0);
            var x3 = KKExTrailer.Parse(PoseKit.KKEx(m3.ToArray()));
            var p3 = PoseToolsData.From(x3.Find(PoseToolsData.Guid));
            t.Check(p3.Version == 1 && p3.UnknownFields.SequenceEqual(new[] { "FutureField" }) && p3.RawFields.ContainsKey("FutureField"), "unknown field listed and kept raw");
            t.Check(p3.EyeOpen == null && p3.MouthPattern == null && p3.Problems.Count >= 3, "wrong types are listed as problems: " + string.Join("; ", p3.Problems));
            t.Check(p3.JointCorrection != null && p3.JointCorrection.Length == 3 && p3.JointCorrection[0] && !p3.JointCorrection[1], "plain bool array accepted");
            t.Check(p3.SkirtFK != null && p3.SkirtFK.Count == 1 && p3.SkirtFK[0].Rot.Equals(new PoseVector3(1.5f, 2, 3)), "plain skirt map with mixed number types accepted");
            var p4 = PoseToolsData.From(KKExTrailer.Parse(PoseKit.KKEx(new Mp().Map(1).Str(PoseToolsData.Guid).Arr(2).FixInt(0).Str("not a map").ToArray())).Find(PoseToolsData.Guid));
            t.Check(p4.Problems.Count > 0 && p4.FieldOrder.Count == 0, "data that is not a map → problem, no exception");
            t.Check(PoseToolsData.From(null).Problems.Count == 1, "From(null) → problem");
        }
    }
}

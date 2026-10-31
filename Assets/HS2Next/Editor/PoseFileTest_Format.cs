// Pose-file reader/writer tests on synthetic files: every field,
// independent BinaryWriter/BinaryReader cross-checks, byte-identical rewrites, −0 / NaN payloads, multibyte and
// non-canonical strings, non-0/1 bools, duplicate keys, versions, typed errors with offsets (truncation at every
// byte offset, counts beyond the file, malformed strings, random mutations), and the PNG container.
namespace HS2Next.Import.Pose.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using HS2Next.Import.Pose;
    using HS2Next.Save.Tests;

    public static class PoseFields
    {
        public static void Run(TestRunner t)
        {
            var s = PoseSpec.Standard(7);
            s.Version = 101; s.Sex = 0; s.Name = "Standing sample 01"; s.Group = 3; s.Category = 4; s.No = 5; s.TimeBits = 0x3EC00000u; // 0.375
            s.EnableIK = 1; s.ActiveIK = new byte[] { 1, 0, 1, 0, 1 };
            s.EnableFK = 1; s.ActiveFK = new byte[] { 1, 1, 0, 1, 0, 1, 1 };
            s.Expression = new byte[] { 1, 0, 1, 0 };
            s.FK.Add((67, PoseSpec.Bits(0, 0, 0, 1, 2, 3)));
            var bytes = s.Build();
            var f = PoseFile.Read(bytes);
            t.Equal(PoseContainerKind.Dat, f.Kind, "kind Dat");
            t.Equal(101, f.Version, "version");
            t.Equal(0, f.Sex, "sex");
            t.Equal("Standing sample 01", f.Name, "name");
            t.Equal("3/4/5", f.AnimeGroup + "/" + f.AnimeCategory + "/" + f.AnimeNo, "anime record");
            t.Equal(0x3EC00000u, f.NormalizedTimeBits, "normalizedTime bits");
            t.Equal(0.375f, f.NormalizedTime, "normalizedTime");
            t.Check(f.HasNormalizedTime, "HasNormalizedTime");
            t.Equal(true, f.EnableIK, "enableIK");
            t.Equal("10101", string.Concat(f.ActiveIK.Select(b => b ? '1' : '0')), "activeIK");
            t.Equal(13, f.IK.Count, "IK count");
            t.Check(f.IK.Select(e => e.Key).SequenceEqual(Enumerable.Range(0, 13)), "IK keys 0..12 in order");
            t.Check(f.IK.Zip(s.IK, (e, x) => Enumerable.Range(0, 9).All(k => e.GetBits(k) == x.bits[k])).All(b => b), "IK values bit-exact");
            t.Equal(true, f.EnableFK, "enableFK");
            t.Equal("1101011", string.Concat(f.ActiveFK.Select(b => b ? '1' : '0')), "activeFK");
            t.Equal(65, f.FK.Count, "FK count");
            t.Check(f.FK.Select(e => e.Key).SequenceEqual(s.FK.Select(x => x.key)), "FK keys in file order");
            t.Check(f.FK.Zip(s.FK, (e, x) => Enumerable.Range(0, 9).All(k => e.GetBits(k) == x.bits[k])).All(b => b), "FK values bit-exact");
            t.Equal("1010", string.Concat(f.Expression.Select(b => b ? '1' : '0')), "expression");
            t.Check(f.Trailer == null && f.Ext == null && f.PoseTools == null && f.GameName == null, "no trailer, no views");
            t.Equal(PoseFileFlags.None, f.Flags, "no flags");
            t.Equal(0, f.PayloadOffset, "payload offset");
            t.Equal(bytes.Length, f.PayloadEnd, "payload end");
            t.Equal(s.FK[3].key, f.FK[3].Key, "entry key");
            t.Check(f.FK[3].SourceOffset > 0 && BitConverter.ToInt32(bytes, f.FK[3].SourceOffset) == f.FK[3].Key, "entry source offset points at its key");
            var e67 = f.FindFk(67);
            t.Check(e67 != null && e67.Rot.Equals(new PoseVector3(1, 2, 3)) && e67.Pos.Equals(PoseVector3.Zero) && e67.Scale.Equals(PoseVector3.One), "FindFk + vector views");
            t.Check(f.FindIk(6) == f.IK[6] && f.FindIk(13) == null, "FindIk");
            t.Check(PoseKit.Same(bytes, f.Write()), "Write is byte-identical");
            t.Check(PoseKit.Same(bytes, f.WritePayload(false)) && PoseKit.Same(bytes, f.WritePayload()), "WritePayload is byte-identical");
            t.Equal(PoseKit.IndependentSummary(bytes), PoseKit.IndependentSummary(f.Write()), "BinaryReader sees the same fields");

            // a pose built in code, read back with BinaryReader
            var g = new PoseFile { Sex = 1, Name = "built", AnimeGroup = 0, AnimeCategory = 0, AnimeNo = 0, NormalizedTime = 1.25f, EnableFK = true };
            t.Equal("01010000", string.Concat(g.ActiveFK.Select(b => b ? '1' : '0')) + (g.EnableIK ? "1" : "0"), "defaults: activeFK Neck+Body, IK off");
            t.Check(g.ActiveIK.All(b => b) && g.Expression.All(b => b) && g.Version == 101 && g.Kind == PoseContainerKind.Dat, "defaults: activeIK and expression all on, version 101, Dat");
            for (int k = 0; k < 13; k++) g.IK.Add(new PoseEntry(k, new PoseVector3(k, 0, 0), PoseVector3.Zero, PoseVector3.One));
            g.FK.Add(new PoseEntry(0) { Rot = new PoseVector3(10, 20, 30) });
            var gb = g.Write();
            var sum = PoseKit.IndependentSummary(gb);
            t.Check(sum.StartsWith(PoseFile.Magic + "|101|1|built|0/0/0|3fa00000|False11111|IK13 0:0:", StringComparison.Ordinal), "code-built pose read by BinaryReader: " + sum.Substring(0, Math.Min(80, sum.Length)));
            t.Check(sum.Contains("|True0101000|FK1 0:0:0:0:41200000:41a00000:41f00000:3f800000:3f800000:3f800000|1111|rest0"), "FK record and flags as BinaryWriter would write them");
            var g2 = PoseFile.Read(gb);
            t.Check(PoseKit.Same(gb, g2.Write()), "code-built pose: Read → Write identical");

            // modifications are written canonically
            f.Name = "renamed ポーズ";
            f.Sex = 1;
            f.FK[0].Rot = new PoseVector3(-1.5f, 0, 720);
            f.ActiveIK[1] = true;
            f.Trailer = new byte[] { 1, 2, 3 };
            var mod = PoseFile.Read(f.Write());
            t.Check(mod.Name == "renamed ポーズ" && mod.Sex == 1 && mod.FK[0].Rot.Equals(new PoseVector3(-1.5f, 0, 720)) && mod.ActiveIK[1] && PoseKit.Same(mod.Trailer, new byte[] { 1, 2, 3 }), "modified fields survive Write → Read");
            t.Check((mod.Flags & PoseFileFlags.TrailerNotKKEx) != 0 && mod.Ext.Status == KKExStatus.NotKKEx, "a non-KKEx trailer is flagged");
            t.Check(PoseFile.Read(PoseKit.Prefix(f.Write(), mod.PayloadEnd)).Trailer == null, "trailer removed by cutting at PayloadEnd");
        }
    }

    public static class PoseFloats
    {
        public static void Run(TestRunner t)
        {
            uint[] specials =
            {
                0x80000000u, 0x00000000u, 0x7FC12345u, 0x7F800001u, 0xFFBFFFFFu, 0xFFC00000u, 0x7F800000u, 0xFF800000u,
                0x00000001u, 0x807FFFFFu, 0x7F7FFFFFu, 0x3F800001u,
            };
            var s = PoseSpec.Standard(3);
            s.TimeBits = 0x7FA00001u; // signalling NaN as normalized time
            for (int i = 0; i < specials.Length; i++)
            {
                var bits = new uint[9];
                for (int k = 0; k < 9; k++) bits[k] = specials[(i + k) % specials.Length];
                s.FK.Add((1000 + i, bits));
                s.IK[i % 13] = (i % 13, bits);
            }
            var bytes = s.Build();
            var f = PoseFile.Read(bytes);
            t.Equal(0x7FA00001u, f.NormalizedTimeBits, "sNaN normalized time bits kept");
            bool ok = true;
            for (int i = 0; i < specials.Length; i++)
            {
                var e = f.FindFk(1000 + i);
                for (int k = 0; k < 9; k++) ok &= e.GetBits(k) == specials[(i + k) % specials.Length];
            }
            t.Check(ok, "−0, NaN payloads (quiet, signalling, negative), ±Inf, denormals kept as bits");
            t.Check(PoseKit.Same(bytes, f.Write()), "special floats: Write is byte-identical");
            var neg0 = f.FindFk(1000);
            t.Check(neg0.GetBits(0) == 0x80000000u && float.IsNegativeInfinity(1f / neg0[0]), "−0 view keeps the sign");
            t.Check(float.IsNaN(f.FindFk(1002)[0]) && FloatBits.IsNaN(f.FindFk(1002).GetBits(0)), "NaN view is NaN");
            t.Check(!FloatBits.IsNaN(0x7F800000u) && FloatBits.IsNaN(0xFFC00000u), "FloatBits.IsNaN");
            // the setter path writes the float's bits
            var c = f.FindFk(1001).Clone();
            t.Check(c.BitwiseEquals(f.FindFk(1001)), "Clone is bitwise equal");
            c.SetBits(4, 0x7FC00001u);
            t.Check(!c.BitwiseEquals(f.FindFk(1001)) && c.GetBits(4) == 0x7FC00001u, "SetBits");
            c.Rot = new PoseVector3(-0f, 1, 2);
            t.Equal(0x80000000u, c.GetBits(3), "−0 through the vector setter");
        }
    }

    public static class PoseStrings
    {
        public static void Run(TestRunner t)
        {
            string[] names =
            {
                "", "a", "立ちポーズ（サンプル）", "емодзи 😀 🧍‍♀️ 姿勢", new string('あ', 50) /* 150 bytes: 2-byte length prefix */,
                new string('x', 127), new string('x', 128), string.Concat(Enumerable.Repeat("😀", 5000)) /* 20,000 bytes: 3-byte prefix */,
            };
            foreach (var n in names)
            {
                var s = PoseSpec.Standard(5);
                s.Name = n;
                var b = s.Build();
                var f = PoseFile.Read(b);
                t.Check(f.Name == n && f.Flags == PoseFileFlags.None && PoseKit.Same(b, f.Write()), "name of " + System.Text.Encoding.UTF8.GetByteCount(n) + " UTF-8 bytes: read and rewritten");
            }
            // non-minimal length prefix: 0x85 0x00 = 5
            var nm = PoseSpec.Standard(5);
            nm.NameRawWithPrefix = new byte[] { 0x85, 0x00, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o' };
            var nb = nm.Build();
            var nf = PoseFile.Read(nb);
            t.Check(nf.Name == "hello" && (nf.Flags & PoseFileFlags.NonCanonicalString) != 0, "non-minimal length prefix read and flagged");
            t.Check(PoseKit.Same(nb, nf.Write()), "non-minimal length prefix kept on write");
            nf.Name = "hello!";
            var nw = nf.Write();
            int nameAt = nm.Fields.First(x => x.name == "name").start;
            t.Check(nw[nameAt] == 6 && nw[nameAt + 1] == (byte)'h' && nw[nameAt + 6] == (byte)'!' && PoseFile.Read(nw).Name == "hello!", "changed name re-encoded canonically (1-byte prefix)");
            // invalid UTF-8 (lone continuation byte, overlong form)
            var bad = PoseSpec.Standard(5);
            bad.NameRaw = new byte[] { 0x41, 0xFF, 0xC0, 0x80, 0x42 };
            var bb = bad.Build();
            var bf = PoseFile.Read(bb);
            t.Check(bf.Name.StartsWith("A") && bf.Name.EndsWith("B") && bf.Name.Contains('�'), "invalid UTF-8 decoded like BinaryReader (U+FFFD)");
            t.Check((bf.Flags & PoseFileFlags.NonCanonicalString) != 0 && PoseKit.Same(bb, bf.Write()), "invalid UTF-8 flagged and kept on write");
            // identifying string written with a non-minimal prefix still matches
            var id = PoseSpec.Standard(5);
            var ib = id.Build();
            var raw = PoseKit.Concat(new byte[] { 0x8A, 0x00 }, PoseKit.Prefix(ib, 11).Skip(1).ToArray(), ib.Skip(11).ToArray());
            var idf = PoseFile.Read(raw);
            t.Check((idf.Flags & PoseFileFlags.NonCanonicalString) != 0 && PoseKit.Same(raw, idf.Write()), "identifying string with a non-minimal prefix: read, flagged, kept");
        }
    }

    public static class PoseBools
    {
        public static void Run(TestRunner t)
        {
            var s = PoseSpec.Standard(9);
            s.ActiveFK = new byte[] { 2, 1, 0, 0xFF, 1, 0, 0x80 };
            s.EnableIK = 7;
            var b = s.Build();
            var f = PoseFile.Read(b);
            t.Check(f.EnableIK && f.ActiveFK[0] && f.ActiveFK[3] && f.ActiveFK[6] && !f.ActiveFK[2], "bytes other than 0/1 read as true");
            t.Check((f.Flags & PoseFileFlags.NonCanonicalBool) != 0, "non-0/1 bool flagged");
            t.Check(PoseKit.Same(b, f.Write()), "non-0/1 bool bytes kept on write");
            f.ActiveFK[0] = false; f.ActiveFK[2] = true;
            var w = f.Write();
            int at = s.Fields.Where(x => x.name == "activeFK").Select(x => x.start).First();
            t.Check(w[at] == 0 && w[at + 2] == 1 && w[at + 3] == 0xFF && w[at + 6] == 0x80, "changed bools written as 0/1, unchanged raw bytes kept");
        }
    }

    public static class PoseDuplicates
    {
        public static void Run(TestRunner t)
        {
            var s = PoseSpec.Standard(11);
            s.IK.Insert(5, (3, PoseSpec.Bits(9, 9, 9, 0, 0, 0)));
            s.FK.Add((5, PoseSpec.Bits(0, 0, 0, 11, 0, 0)));
            s.FK.Add((5, PoseSpec.Bits(0, 0, 0, 22, 0, 0)));
            var b = s.Build();
            var f = PoseFile.Read(b);
            t.Equal(14, f.IK.Count, "duplicate IK key kept (14 records)");
            t.Equal(66, f.FK.Count, "duplicate FK keys kept (66 records)");
            t.Check((f.Flags & (PoseFileFlags.DuplicateIkKey | PoseFileFlags.DuplicateFkKey | PoseFileFlags.NonStandardIk)) == (PoseFileFlags.DuplicateIkKey | PoseFileFlags.DuplicateFkKey | PoseFileFlags.NonStandardIk), "duplicate flags");
            t.Check(f.FindIk(3) == f.IK[3] && f.FindFk(5) == f.FK[5], "Find returns the first record");
            t.Check(f.FK.Where(e => e.Key == 5).Select(e => e.Rot.X).SequenceEqual(new float[] { f.FK[5].Rot.X, 11, 22 }), "duplicates in file order");
            t.Check(PoseKit.Same(b, f.Write()), "duplicates: Write is byte-identical");
            var odd = PoseSpec.Standard(11);
            odd.IK.RemoveAt(12);
            var of = PoseFile.Read(odd.Build());
            t.Check((of.Flags & PoseFileFlags.NonStandardIk) != 0 && (of.Flags & PoseFileFlags.DuplicateIkKey) == 0, "12 IK targets flagged NonStandardIk");
        }
    }

    public static class PoseVersions
    {
        public static void Run(TestRunner t)
        {
            foreach (int v in new[] { 100, 0, -1, 102, int.MaxValue })
            {
                var s = PoseSpec.Standard(13);
                s.Version = v;
                var b = s.Build();
                var f = PoseFile.Read(b);
                t.Check(f.Version == v && (f.Flags & PoseFileFlags.UnexpectedVersion) != 0, "version " + v + " read and flagged");
                t.Check(f.HasNormalizedTime == (v >= 101) && (v >= 101 ? f.NormalizedTimeBits == s.TimeBits : f.NormalizedTimeBits == 0), "version " + v + ": normalizedTime " + (v >= 101 ? "read" : "absent"));
                t.Check(f.IK.Count == 13 && f.FK.Count == 64 && f.Expression.All(x => x), "version " + v + ": the rest of the payload is aligned");
                t.Check(PoseKit.Same(b, f.Write()), "version " + v + ": Write is byte-identical");
            }
            var s101 = PoseFile.Read(PoseSpec.Standard(13).Build());
            s101.Version = 100;
            var down = PoseFile.Read(s101.Write());
            t.Check(down.Version == 100 && !down.HasNormalizedTime && down.FK.Count == 64, "writing version 100 drops normalizedTime");
        }
    }

    public static class PoseErrors
    {
        static bool Typed(Exception e, out PoseFormatException p) { p = e as PoseFormatException; return p != null; }

        public static void Run(TestRunner t)
        {
            // identifying string
            foreach (var magic in new[] { "【pause】", "KKEx", "", "【pose】 " })
            {
                var s = PoseSpec.Standard(1);
                s.Magic = magic;
                var e = PoseKit.Catch(() => PoseFile.Read(s.Build()));
                t.Check(Typed(e, out var p) && p.Code == PoseErrorCode.BadMagic && p.Offset == 0, "identifying string \"" + magic + "\" → BadMagic at 0" + (e != null && !(e is PoseFormatException) ? " (" + e.GetType().Name + ")" : ""));
            }
            var empty = PoseKit.Catch(() => PoseFile.Read(new byte[0]));
            t.Check(Typed(empty, out var pe) && pe.Code == PoseErrorCode.Truncated && pe.Offset == 0, "empty file → Truncated at 0");
            var nul = PoseKit.Catch(() => PoseFile.Read(null));
            t.Check(nul is PoseFormatException, "null → typed error");
            t.Check(!PoseFile.TryRead(new byte[] { 1, 2, 3 }, out var tp, out var te) && tp == null && te != null, "TryRead does not throw");
            var io = PoseKit.Catch(() => PoseFile.ReadFile(System.IO.Path.Combine(t.TempDir ?? System.IO.Path.GetTempPath(), "does_not_exist_" + Guid.NewGuid().ToString("N") + ".dat")));
            t.Check(Typed(io, out var pio) && pio.Code == PoseErrorCode.Io && pio.Offset == -1, "missing file → Io");

            // counts beyond the file and negative counts
            foreach (var (ik, fk) in new (int?, int?)[] { (1000000, null), (-1, null), (int.MaxValue, null), (null, 65), (null, -5), (null, int.MaxValue) })
            {
                var s = PoseSpec.Standard(2);
                s.IkCountOverride = ik; s.FkCountOverride = fk;
                var b = s.Build();
                var at = s.Fields.First(x => x.name == (ik != null ? "IK" : "FK")).start;
                var e = PoseKit.Catch(() => PoseFile.Read(b));
                t.Check(Typed(e, out var p) && p.Code == PoseErrorCode.CountOutOfRange && p.Offset == at,
                    $"{(ik != null ? "IK" : "FK")} count {(ik ?? fk)} → CountOutOfRange at {at}" + (p != null ? $" (got {p.Code} at {p.Offset})" : e != null ? " (" + e.GetType().Name + ")" : " (no error)"));
            }
            // a count that fits the file but reads into the following fields: IK 14 is caught above when the 14th
            // record does not fit; one more FK record than written but within the file size cannot be detected by size
            // (Studio would read garbage) – the reader then fails at the expression bytes or later.

            // malformed strings
            var s6 = PoseSpec.Standard(3);
            s6.NameRawWithPrefix = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 };
            var e6 = PoseKit.Catch(() => PoseFile.Read(s6.Build()));
            int nameAt = s6.Fields.First(x => x.name == "name").start;
            t.Check(Typed(e6, out var p6) && p6.Code == PoseErrorCode.BadString && p6.Offset == nameAt, "6-byte 7-bit length → BadString at the name");
            var s7 = PoseSpec.Standard(3);
            s7.NameRawWithPrefix = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F };
            var e7 = PoseKit.Catch(() => PoseFile.Read(s7.Build()));
            t.Check(Typed(e7, out var p7) && p7.Code == PoseErrorCode.BadString && p7.Offset == nameAt, "negative string length → BadString");
            var s8 = PoseSpec.Standard(3);
            s8.NameRawWithPrefix = new byte[] { 0xFF, 0xFF, 0x7F };
            var e8 = PoseKit.Catch(() => PoseFile.Read(s8.Build()));
            t.Check(Typed(e8, out var p8) && p8.Code == PoseErrorCode.Truncated && p8.Offset == nameAt, "string longer than the file → Truncated at the name");

            Truncation(t, "dat with trailer", WithTrailer(), 0);
            var png = PoseKit.Png(20);
            Truncation(t, "PNG", PoseKit.Concat(png, WithTrailer()), png.Length);
            Mutations(t);
        }

        static byte[] WithTrailer() => WithTrailer(out _);

        static byte[] WithTrailer(out PoseSpec spec)
        {
            var s = PoseSpec.Standard(4);
            s.IK.RemoveRange(3, 10);
            s.FK.RemoveRange(4, s.FK.Count - 4);
            s.Name = "短い名前";
            var m = new Mp().Map(1);
            PoseKit.ExtendedSaveEntry(m);
            s.Trailer = PoseKit.KKEx(m.ToArray());
            spec = s;
            return s.Build();
        }

        /// <summary>Every prefix of a small file: a typed error at the start of the field the cut falls into, or (cut at or after the payload end) a pose whose trailer is the rest.</summary>
        static void Truncation(TestRunner t, string label, byte[] full, int payloadStart)
        {
            WithTrailer(out var spec);
            int payloadEnd = payloadStart + spec.PayloadEnd;
            int fails = 0, checks = 0;
            string first = null;
            for (int len = 0; len < full.Length; len++)
            {
                var cut = PoseKit.Prefix(full, len);
                var e = PoseKit.Catch(() =>
                {
                    var f = PoseFile.Read(cut);
                    if (len < payloadEnd) throw new Exception("read succeeded on a cut payload");
                    if (!PoseKit.Same(cut, f.Write())) throw new Exception("rewrite differs");
                    if ((len == payloadEnd) != (f.Trailer == null)) throw new Exception("trailer presence wrong");
                });
                checks++;
                string why = null;
                if (len >= payloadEnd) { if (e != null) why = e.GetType().Name + ": " + e.Message; }
                else if (!(e is PoseFormatException p)) why = e == null ? "no error" : e.GetType().Name + ": " + e.Message;
                else if (payloadStart > 0 && len < payloadStart)
                {
                    // inside the image: broken chunk list (Truncated/BadPng)
                    if (p.Code != PoseErrorCode.Truncated && p.Code != PoseErrorCode.BadPng) why = "image cut gave " + p.Code;
                }
                else if (payloadStart > 0 && len - payloadStart < spec.Fields[0].end)
                {
                    if (p.Code != PoseErrorCode.NoPayload || p.Offset != payloadStart) why = $"cut inside the identifier gave {p.Code} at {p.Offset}";
                }
                else
                {
                    int rel = len - payloadStart;
                    var fld = spec.Fields.First(x => x.end > rel);
                    var want = (fld.name == "IK" || fld.name == "FK") && rel >= fld.start + 4 ? PoseErrorCode.CountOutOfRange : PoseErrorCode.Truncated;
                    if (p.Code != want || p.Offset != payloadStart + fld.start) why = $"cut in {fld.name}: expected {want} at {payloadStart + fld.start}, got {p.Code} at {p.Offset}";
                }
                if (why != null) { fails++; first = first ?? $"len {len}: {why}"; }
            }
            t.Check(fails == 0, $"{label}: truncation at every byte offset ({checks} prefixes of {full.Length} bytes) gives a typed error at the field start or a valid pose with the rest as trailer" + (first != null ? $"; {fails} wrong, first {first}" : ""));
        }

        /// <summary>Random byte mutations: only PoseFormatException escapes; every file that reads is rewritten byte for byte.</summary>
        static void Mutations(TestRunner t)
        {
            var baseFile = WithTrailer();
            var rnd = new Random(20261016);
            int read = 0, typed = 0, wrong = 0;
            string first = null;
            for (int i = 0; i < 3000; i++)
            {
                var b = (byte[])baseFile.Clone();
                int n = 1 + rnd.Next(4);
                for (int k = 0; k < n; k++) b[rnd.Next(b.Length)] = (byte)rnd.Next(256);
                if (rnd.Next(4) == 0) b = PoseKit.Prefix(b, rnd.Next(b.Length));
                try
                {
                    var f = PoseFile.Read(b);
                    read++;
                    if (!PoseKit.Same(b, f.Write())) { wrong++; first = first ?? "mutation " + i + ": rewrite differs at " + PoseKit.Diff(b, f.Write()); }
                    var x = f.Ext; var pt = f.PoseTools; var gn = f.GameName; // views never throw
                }
                catch (PoseFormatException) { typed++; }
                catch (Exception e) { wrong++; first = first ?? "mutation " + i + ": " + e.GetType().Name + " " + e.Message; }
            }
            t.Check(wrong == 0, $"3,000 random mutations: {read} read and rewritten identically, {typed} typed errors" + (first != null ? $"; {wrong} wrong, first {first}" : ""));
        }
    }

    public static class PosePngContainer
    {
        public static void Run(TestRunner t)
        {
            var png = PoseKit.Png(64);
            t.Equal(png.Length, PosePng.ImageLength(png), "chunk walk ends after the IEND CRC (a tEXt chunk containing \"IEND\" does not fool it)");
            var s = PoseSpec.Standard(21);
            var m = new Mp().Map(2);
            PoseKit.ExtendedSaveEntry(m);
            PoseKit.PoseToolsEntry(m, 1, 2, 3, 0.5f, 0.25f, new[] { true, true, false, true, true, true, true, true }, null);
            s.Trailer = PoseKit.KKEx(m.ToArray());
            var payload = s.Build();
            var file = PoseKit.Concat(png, payload);
            var f = PoseFile.Read(file);
            t.Equal(PoseContainerKind.Png, f.Kind, "kind Png");
            t.Check(PoseKit.Same(png, f.ImagePrefix), "image prefix kept as bytes");
            t.Equal(png.Length, f.PayloadOffset, "payload offset = image length");
            t.Check(f.FK.Count == 64 && f.IK.Count == 13 && f.PoseTools != null && f.GameName == 4, "payload and trailer read");
            t.Check(PoseKit.Same(file, f.Write()), "PNG pose: Write is byte-identical");
            t.Check(PoseKit.Same(payload, f.WritePayload()), "WritePayload = the .dat form");
            t.Check(PoseKit.Same(PoseKit.Prefix(payload, s.PayloadEnd), f.WritePayload(false)), "WritePayload(false) drops the trailer");

            var plain = PoseKit.Catch(() => PoseFile.Read(png));
            t.Check(plain is PoseFormatException p1 && p1.Code == PoseErrorCode.NoPayload && p1.Offset == png.Length, "PNG ending at IEND → NoPayload");
            var junk = PoseKit.Catch(() => PoseFile.Read(PoseKit.Concat(png, new byte[] { 0x0A, 1, 2, 3 })));
            t.Check(junk is PoseFormatException p2 && p2.Code == PoseErrorCode.NoPayload, "PNG + bytes that are not a pose → NoPayload");
            var noIend = PoseKit.Catch(() => PoseFile.Read(PoseKit.Prefix(png, png.Length - 12)));
            t.Check(noIend is PoseFormatException p3 && p3.Code == PoseErrorCode.Truncated, "PNG without IEND → Truncated");
            var huge = (byte[])png.Clone();
            huge[8] = 0x80; // IHDR length 0x80000000
            var e4 = PoseKit.Catch(() => PoseFile.Read(huge));
            t.Check(e4 is PoseFormatException p4 && p4.Code == PoseErrorCode.BadPng && p4.Offset == 8, "chunk length beyond 2^31-1 → BadPng at the chunk");

            // converting containers
            var d = PoseFile.Read(payload);
            d.Kind = PoseContainerKind.Png;
            t.Check(PoseKit.Catch(() => d.Write()) is InvalidOperationException, "Png without ImagePrefix cannot be written");
            d.ImagePrefix = png;
            t.Check(PoseKit.Same(file, d.Write()), ".dat → PNG by setting Kind and ImagePrefix");
            f.Kind = PoseContainerKind.Dat;
            f.ImagePrefix = null;
            t.Check(PoseKit.Same(payload, f.Write()), "PNG → .dat by setting Kind and clearing ImagePrefix");
            t.Check(PoseFile.StartsWithMagic(file, png.Length) && !PoseFile.StartsWithMagic(file, 0) && !PoseFile.StartsWithMagic(file, file.Length), "StartsWithMagic");

            // reader rule: offset 0, chunk walk, then the first identifier that starts a parseable payload
            var two = PoseKit.Concat(png, PoseKit.Png(10), payload);
            var tf = PoseFile.Read(two);
            t.Check(tf.Kind == PoseContainerKind.Png && (tf.Flags & PoseFileFlags.PayloadFoundByScan) != 0 && tf.PayloadOffset == two.Length - payload.Length && tf.ImagePrefix.Length == tf.PayloadOffset, "PNG with a second image before the payload: found by scan, both images kept as prefix");
            t.Check(PoseKit.Same(two, tf.Write()), "found by scan: Write is byte-identical");
            var garbage = PoseKit.Concat(new byte[] { 1, 2, 3, 0x0A, 0xE3, 0x80 }, payload);
            var gf = PoseFile.Read(garbage);
            t.Check(gf.Kind == PoseContainerKind.Dat && (gf.Flags & PoseFileFlags.PayloadFoundByScan) != 0 && gf.PayloadOffset == 6 && PoseKit.Same(garbage, gf.Write()), "unknown bytes before a payload: found by scan, kept");
            var brokenWalk = (byte[])file.Clone();
            brokenWalk[8 + 3] = 0x7F; // IHDR length 0x7F: the walk runs past the chunk list
            var bf = PoseKit.Catch(() => PoseFile.Read(brokenWalk));
            t.Check(bf == null && PoseFile.Read(brokenWalk).Flags.HasFlag(PoseFileFlags.PayloadFoundByScan), "broken chunk list: payload found by scan");
            var badTail = PoseKit.Concat(png, PoseKit.Png(10), PoseKit.Prefix(payload, 60));
            var be = PoseKit.Catch(() => PoseFile.Read(badTail));
            t.Check(be is PoseFormatException pb && pb.Code != PoseErrorCode.NoPayload && pb.Offset > png.Length, "truncated payload after a second image: the payload's error, not NoPayload (" + (be as PoseFormatException)?.Code + ")");
            var datPng = PoseFile.Read(payload);
            t.Check(datPng.Kind == PoseContainerKind.Dat && datPng.ImagePrefix == null && datPng.Flags == PoseFileFlags.None, "payload at offset 0 wins (content decides, not the extension)");
        }
    }
}

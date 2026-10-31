// D1 container tests: T3 determinism and ZIP fields, T4 readability and detection,
// T10 atomic save, T11 safety, plus the placeholder part of T16.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using static HS2Next.Save.Tests.InfraFixtures;

namespace HS2Next.Save.Tests
{
    public static class InfraTests_Container
    {
        public static void Run(TestRunner t)
        {
            T3(t);
            T4(t);
            T10(t);
            T11(t);
            T16Placeholder(t);
        }

        static void T3(TestRunner t)
        {
            var c = SmallChara();
            c.ForeignEntries["zz/foreign.txt"] = Utf8("foreign");
            c.ForeignEntries["Afirst.bin"] = new byte[] { 1, 2, 3 };
            c.Coord.Accessories[0].Ext.Set("stp3.test", Newtonsoft.Json.Linq.JToken.Parse("{\"a\": 1}"));
            c.Subtree.CharaExt.GetOrAdd("stp3.test").Files["data.bin"] = new byte[] { 9, 9 };
            c.Chara.Body.SkinOverlays.Add(new SkinOverlay { Target = "BodyOver", Image = c.Res.Add(FakePng(1)) });
            var a = c.ToBytes(Opts());
            var b = c.ToBytes(Opts());
            t.Check(a.SequenceEqual(b), "T3: two saves identical");
            var re = CharaContainer.Load(a, LOpts()).Unwrap().ToBytes(Opts());
            t.Check(a.SequenceEqual(re), "T3: load → save identical");

            var cd = ParseZip(a, out long eocd, out uint cdOff, out uint cdSize, out int comment);
            var names = cd.Select(e => e.Name).ToList();
            t.Equal("manifest.json", names[0], "T3: manifest first");
            t.Equal("chara.json", names[1], "T3: main document second");
            var rest = names.Skip(2).ToList();
            var sorted = new List<string>(rest);
            sorted.Sort(string.CompareOrdinal);
            t.Check(rest.SequenceEqual(sorted), "T3: other entries ordinal: " + string.Join(", ", rest));
            t.Check(rest.Contains("Afirst.bin") && rest.Contains("zz/foreign.txt") && rest.Contains("ext/stp3.test/data.bin") && rest.Any(n => n.StartsWith("res/")), "T3: entries present");
            bool fields = cd.All(e => e.Time == 0 && e.Date == 0x21 && e.Flags == 0 && e.Method == 8 && e.ExtraLen == 0 && e.CommentLen == 0 && e.ExtAttr == 0 &&
                                     e.LocalTime == 0 && e.LocalDate == 0x21 && e.LocalFlags == 0 && e.LocalMethod == 8 && e.LocalExtraLen == 0 && e.Needed == 20 && e.LocalNeeded == 20);
            t.Check(fields, "T3: DOS time 0x0000 / date 0x0021, flags 0, method 8, no extra field, no comment, needed 20 (every entry, local and central)");
            t.Info("T3: made-by values " + string.Join(",", cd.Select(e => "0x" + e.MadeBy.ToString("X4")).Distinct()));
            t.Check(cdOff + cdSize == eocd && comment == 0 && eocd + 22 == a.Length, "T3: absolute offsets (cdOffset + cdSize = EOCD), no comment, nothing after EOCD");
            t.Check(PngInfo.HasSignature(a) && cd.Min(e => e.Lho) == PngInfo.FindEnd(a, a.Length, out _), "T3: PNG prefix ends exactly at the first local header");
        }

        static int pyCount;

        static string Python(string script, string arg)
        {
            string file = Path.Combine(Path.GetTempPath(), "hs2n_py_" + Process.GetCurrentProcess().Id + "_" + (pyCount++) + ".py");
            File.WriteAllText(file, script);
            var psi = new ProcessStartInfo("python", "-I \"" + file + "\" \"" + arg + "\"")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            };
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                File.Delete(file);
                return o.Trim();
            }
        }

        static void T4(TestRunner t)
        {
            var c = SmallChara();
            c.ForeignEntries["notes/readme.txt"] = Utf8("hello");
            string path = Path.Combine(t.TempDir, "t4.hs2nchara");
            c.Save(path, Opts()).ThrowIfFailed();

            using (var z = ZipFile.OpenRead(path))
            {
                t.Equal(c.ToBytes(Opts()).Length > 0, true, "T4: saved");
                t.Check(z.Entries.Count >= 4 && z.Entries[0].FullName == "manifest.json", "T4: Mono ZipFile.OpenRead reads it (" + z.Entries.Count + " entries)");
                using (var s = z.GetEntry("chara.json").Open())
                using (var sr = new StreamReader(s)) t.Check(sr.ReadToEnd().Contains("\"Test Chara\""), "T4: Mono reads chara.json content");
            }
            string py = Python("import zipfile,sys; z=zipfile.ZipFile(sys.argv[1]); print(z.testzip(), len(z.namelist()), z.namelist()[0])", path);
            t.Check(py.StartsWith("None ") && py.EndsWith(" manifest.json"), "T4: Python zipfile reads it and every CRC is good (" + py + ")");

            // library listing: the manifest alone, nothing else inflated
            var lib = ContainerReader.Read(path, null, n => n == "manifest.json");
            var lm = lib.ReadManifest();
            t.Check(lm.Title == "Test Chara" && lm.Sex == 1 && lm.Format == "hs2next.chara" && lib.Get("chara.json").Skipped, "T4: manifest read alone for library lists");

            // plain ZIP without prefix
            var bytes = File.ReadAllBytes(path);
            var w = new ContainerWriter { MainDocument = "chara.json" };
            foreach (var kv in Entries(bytes)) w.Add(kv.Key, kv.Value);
            var plain = w.ToBytes();
            var lp = CharaContainer.Load(plain, LOpts());
            t.Check(lp.Ok && lp.Value.Thumbnail == null, "T4: plain ZIP accepted (no thumbnail)");

            // relative-offset `cat png zip`
            var png = ThumbnailPlaceholder.Png();
            var cat = new byte[png.Length + plain.Length];
            Buffer.BlockCopy(png, 0, cat, 0, png.Length);
            Buffer.BlockCopy(plain, 0, cat, png.Length, plain.Length);
            var lr = CharaContainer.Load(cat, LOpts());
            t.Check(lr.Ok && lr.Value.Thumbnail != null && lr.Value.Thumbnail.Length == png.Length && lr.Findings.Has("container.relativeOffsets"),
                "T4: relative-offset png+zip accepted, thumbnail found (" + lr + ")");
            t.Check(lr.Ok && lr.Value.Chara.Profile.Name == "Test Chara", "T4: relative-offset content read");

            // .png-renamed copy
            string pngPath = Path.Combine(t.TempDir, "copy.png");
            File.Copy(path, pngPath, true);
            var lpng = CharaContainer.Load(pngPath, LOpts());
            t.Check(lpng.Ok && lpng.Value.ForeignEntries.ContainsKey("notes/readme.txt"), "T4: .png-renamed copy read by manifest.format");

            // HS2 card / coordinate / Maker autosave
            t.Equal(SaveErrorCode.Hs2Card, CharaContainer.Load(Hs2Card("【AIS_Chara】", true), LOpts()).ErrorCode, "T4: HS2 card → Hs2Card");
            t.Equal(SaveErrorCode.Hs2Coordinate, CharaContainer.Load(Hs2Card("【AIS_Clothes】", true), LOpts()).ErrorCode, "T4: HS2 coordinate → Hs2Coordinate");
            t.Equal(SaveErrorCode.Hs2Card, CharaContainer.Load(Hs2Card("【AIS_Chara】", false), LOpts()).ErrorCode, "T4: Maker autosave → Hs2Card");
            t.Equal(SaveErrorCode.NotAContainer, CharaContainer.Load(Utf8("hello world, this is not a container at all"), LOpts()).ErrorCode, "T4: random bytes → NotAContainer");
            t.Equal(SaveErrorCode.NotAContainer, CharaContainer.Load(ThumbnailPlaceholder.Png(), LOpts()).ErrorCode, "T4: bare PNG → NotAContainer");
            var otherZip = RawZip(new[] { Item("readme.txt", "x") });
            t.Equal(SaveErrorCode.NotHs2Next, CharaContainer.Load(otherZip, LOpts()).ErrorCode, "T4: ZIP without manifest → NotHs2Next");
            var wrongFmt = RawZip(new[] { Item("manifest.json", ManifestJson("other.format")) });
            t.Equal(SaveErrorCode.NotHs2Next, CharaContainer.Load(wrongFmt, LOpts()).ErrorCode, "T4: foreign format → NotHs2Next");
            var scene = RawZip(new[] { Item("manifest.json", ManifestJson("hs2next.scene")) });
            t.Equal(SaveErrorCode.BadManifest, CharaContainer.Load(scene, LOpts()).ErrorCode, "T4: scene file opened as character → BadManifest");
            t.Equal(SaveErrorCode.Hs2Card, ContainerReader.Probe(WriteTemp(t, "card.png", Hs2Card("【AIS_Chara】", true))), "T4: Probe on a card file");
        }

        static string WriteTemp(TestRunner t, string name, byte[] b)
        {
            string p = Path.Combine(t.TempDir, name);
            File.WriteAllBytes(p, b);
            return p;
        }

        static void T10(TestRunner t)
        {
            string target = Path.Combine(t.TempDir, "t10.hs2nchara");
            var c = SmallChara();
            c.Save(target, Opts()).ThrowIfFailed();
            var before = File.ReadAllBytes(target);

            // fault after N bytes
            foreach (long n in new long[] { 0, 10, before.Length / 2, before.Length - 30 })
            {
                var o = Opts();
                o.StreamWrapper = s => new FaultStream(s, n);
                c.Chara.Profile.Name = "changed " + n;
                var r = c.Save(target, o);
                t.Check(!r.Ok && r.Error.Code == SaveErrorCode.IoFailure, "T10: fault after " + n + " bytes fails (" + r + ")");
                t.Check(File.ReadAllBytes(target).SequenceEqual(before), "T10: target unchanged after fault at " + n);
                t.Check(!File.Exists(target + ".tmp"), "T10: no tmp left after fault at " + n);
            }

            // stale tmp
            File.WriteAllBytes(target + ".tmp", Utf8("stale"));
            c.Chara.Profile.Name = "after stale";
            t.Check(c.Save(target, Opts()).Ok && !File.Exists(target + ".tmp"), "T10: stale tmp replaced and removed");
            t.Equal("after stale", CharaContainer.Load(target, LOpts()).Unwrap().Chara.Profile.Name, "T10: saved over the target");

            // new path
            string fresh = Path.Combine(t.TempDir, "sub_new.hs2nchara");
            t.Check(c.Save(fresh, Opts()).Ok && File.Exists(fresh), "T10: save to a new path");

            // save over the source file itself, foreign entries preserved, reader holds no handle
            c.ForeignEntries["keep/me.txt"] = Utf8("keep");
            c.Save(target, Opts()).ThrowIfFailed();
            var loaded = CharaContainer.Load(target, LOpts()).Unwrap();
            loaded.Chara.Profile.Name = "resaved";
            var rs = loaded.Save(target, Opts());
            t.Check(rs.Ok, "T10: save over the loaded source (" + rs + ")");
            var again = CharaContainer.Load(target, LOpts()).Unwrap();
            t.Check(again.Chara.Profile.Name == "resaved" && again.ForeignEntries.TryGetValue("keep/me.txt", out var fe) && Encoding.UTF8.GetString(fe) == "keep", "T10: foreign entry preserved over the source");
            File.Delete(target);
            t.Check(!File.Exists(target), "T10: loaded file can be deleted (no handle kept)");

            // NonFinite: no file written
            string nf = Path.Combine(t.TempDir, "nonfinite.hs2nchara");
            c.Chara.Face.Shape[3] = float.NaN;
            var rn = c.Save(nf, Opts());
            t.Check(!rn.Ok && rn.Error.Code == SaveErrorCode.NonFinite && rn.Error.JsonPath == "face.shape[3]" && rn.Error.Entry == "chara.json", "T8: NaN in the model → NonFinite at chara.json:face.shape[3] (" + rn + ")");
            t.Check(!File.Exists(nf) && !File.Exists(nf + ".tmp"), "T8: no file and no tmp written");
            c.Chara.Face.Shape[3] = float.NegativeInfinity;
            t.Equal(SaveErrorCode.NonFinite, c.Save(nf, Opts()).Error?.Code, "T8: -Infinity → NonFinite");
            c.Chara.Face.Shape[3] = 0.5f;

            // Unexpected exceptions never escape Save: they come back as BadDocument with a finding naming the type,
            // and the target stays as it was. I/O errors keep IoFailure.
            string guarded = Path.Combine(t.TempDir, "guarded.hs2nchara");
            File.WriteAllBytes(guarded, Utf8("old"));
            var broken = SmallChara();
            broken.Chara.Face = null;
            SaveResult rb = null;
            try { rb = broken.Save(guarded, Opts()); }
            catch (Exception e) { t.Check(false, "Save threw " + e.GetType().Name + " instead of returning the error"); }
            t.Check(rb != null && !rb.Ok && rb.Error.Code == SaveErrorCode.BadDocument && rb.Error.Entry == "chara.json" && rb.Error.InnerException is NullReferenceException,
                "T10: a null sub-object in a document → BadDocument at chara.json (" + rb + ")");
            t.Check(rb != null && rb.Findings.Any(f => f.Code == "save.unexpectedException" && f.Message.Contains("NullReferenceException")), "T10: finding save.unexpectedException names the exception type");
            t.Check(Encoding.UTF8.GetString(File.ReadAllBytes(guarded)) == "old" && !File.Exists(guarded + ".tmp"), "T10: target unchanged, no tmp");
            var nullForeign = SmallChara();
            nullForeign.ForeignEntries["notes/x.txt"] = null;
            SaveResult rf = null;
            try { rf = nullForeign.Save(guarded, Opts()); }
            catch (Exception e) { t.Check(false, "Save threw " + e.GetType().Name + " for a null foreign entry"); }
            t.Check(rf != null && !rf.Ok && rf.Error.Code == SaveErrorCode.BadDocument && rf.Error.InnerException is ArgumentNullException && rf.Findings.Has("save.unexpectedException"),
                "T10: an unexpected exception outside the document writers is returned too (" + rf + ")");
            var rio = SmallChara().Save(Path.Combine(t.TempDir, "no_such_folder", "x.hs2nchara"), Opts());
            t.Equal(SaveErrorCode.IoFailure, rio.Error?.Code, "T10: a missing folder stays IoFailure");
            t.Check(!rio.Findings.Has("save.unexpectedException"), "T10: no unexpected-exception finding for an I/O error");
        }

        static void T11(TestRunner t)
        {
            string man = ManifestJson();
            Func<string, RawItem[]> withName = n => new[] { Item("manifest.json", man), Item(n, "x") };
            foreach (var bad in new[] { "../x", "/x", "a\\b", "C:x", "a//b", "a\u0000b", "a/./b", "a/..", "x\u0001y" })
            {
                var z = RawZip(withName(bad));
                t.Equal(SaveErrorCode.UnsafeEntryName, CharaContainer.Load(z, LOpts()).ErrorCode, "T11: unsafe name " + bad.Replace("\u0000", "\\0").Replace("\u0001", "\\1"));
            }
            t.Equal(SaveErrorCode.DuplicateEntry, CharaContainer.Load(RawZip(new[] { Item("manifest.json", man), Item("a.txt", "1"), Item("a.txt", "2") }), LOpts()).ErrorCode, "T11: duplicate names");
            var enc = Item("x.bin", "secret"); enc.Flags = 1;
            t.Equal(SaveErrorCode.UnsupportedZip, CharaContainer.Load(RawZip(new[] { Item("manifest.json", man), enc }), LOpts()).ErrorCode, "T11: encryption flag");
            var m12 = Item("x.bin", "bz"); m12.Method = 12;
            t.Equal(SaveErrorCode.UnsupportedZip, CharaContainer.Load(RawZip(new[] { Item("manifest.json", man), m12 }), LOpts()).ErrorCode, "T11: method 12");
            var z64 = Item("x.bin", "big"); z64.USizeOverride = 0xFFFFFFFF;
            t.Equal(SaveErrorCode.UnsupportedZip, CharaContainer.Load(RawZip(new[] { Item("manifest.json", man), z64 }), LOpts()).ErrorCode, "T11: Zip64 size marker");
            t.Equal(SaveErrorCode.UnsupportedZip, CharaContainer.Load(RawZip(new[] { Item("manifest.json", man) }, null, false, 0xFFFF), LOpts()).ErrorCode, "T11: Zip64 count marker");

            var good = SmallChara().ToBytes(Opts());
            int pngEnd = (int)PngInfo.FindEnd(good, good.Length, out _);
            foreach (int cut in new[] { good.Length - 1, good.Length - 30, (pngEnd + good.Length) / 2, pngEnd + 100 })
            {
                var tr = new byte[cut];
                Buffer.BlockCopy(good, 0, tr, 0, cut);
                t.Equal(SaveErrorCode.CorruptContainer, CharaContainer.Load(tr, LOpts()).ErrorCode, "T11: truncated at " + cut);
            }

            // flipped byte in a res/ entry → missing resource + finding; in chara.json → EntryCorrupt
            var c = SmallChara();
            string resName = c.Res.Add(FakePng(7, 4000));
            c.Chara.Body.SkinOverlays.Add(new SkinOverlay { Target = "BodyOver", Image = resName });
            var bytes = c.ToBytes(Opts());
            var cd = ParseZip(bytes, out _, out _, out _, out _);
            var resE = cd.First(e => e.Name == resName);
            var flipped = (byte[])bytes.Clone();
            long dataAt = resE.Lho + 30 + Encoding.UTF8.GetByteCount(resE.Name) + resE.LocalExtraLen;
            flipped[dataAt + resE.CSize / 2] ^= 0x55;
            var lr = CharaContainer.Load(flipped, LOpts());
            t.Check(lr.Ok && !lr.Value.Res.Contains(resName) && lr.Findings.Has("res.unreadable"), "T11: flipped byte in res/ → missing resource + finding (" + lr + ")");
            t.Check(lr.Ok && lr.Value.Chara.Body.SkinOverlays[0].Image == resName, "T11: the reference is kept (dangling)");
            var saved = lr.Value.Save(Path.Combine(t.TempDir, "dangling.hs2nchara"), Opts());
            t.Check(saved.Ok && saved.Findings.Has("res.dangling"), "T11: dangling reference reported on save");

            var chE = cd.First(e => e.Name == "chara.json");
            var flipped2 = (byte[])bytes.Clone();
            long d2 = chE.Lho + 30 + chE.Name.Length + chE.LocalExtraLen;
            flipped2[d2 + chE.CSize / 2] ^= 0x01;
            t.Equal(SaveErrorCode.EntryCorrupt, CharaContainer.Load(flipped2, LOpts()).ErrorCode, "T11: flipped byte in chara.json → EntryCorrupt");

            // CRC is checked by us (a stored entry with a wrong CRC)
            var crcBad = Item("readme.txt", "x"); crcBad.CrcOverride = 1234;
            var zc = RawZip(new[] { Item("manifest.json", man), Item("chara.json", "{}"), Item("coord/coord.json", "{}"), crcBad });
            var lc = CharaContainer.Load(zc, LOpts());
            t.Check(lc.Ok && !lc.Value.ForeignEntries.ContainsKey("readme.txt") && lc.Findings.Has("EntryCorrupt"), "T11: CRC mismatch on a stored foreign entry detected (" + lc + ")");
            // declared size smaller than the inflated data → refused
            var big = Item("big.txt", new string('a', 5000), 8); big.USizeOverride = 100;
            var lb = CharaContainer.Load(RawZip(new[] { Item("manifest.json", man), Item("chara.json", "{}"), Item("coord/coord.json", "{}"), big }), LOpts());
            t.Check(lb.Ok && lb.Findings.Has("EntryCorrupt") && !lb.Value.ForeignEntries.ContainsKey("big.txt"), "T11: inflation stops at the declared size (" + lb + ")");
            // a minimal crafted file loads (documents {} → every member absent)
            t.Check(lb.Ok && lb.Value.Chara.Profile.Name == "", "T11: minimal hand-made container loads");
            var noCoord = RawZip(new[] { Item("manifest.json", man), Item("chara.json", "{}") });
            t.Equal(SaveErrorCode.MissingDocument, CharaContainer.Load(noCoord, LOpts()).ErrorCode, "T11: missing coord document → MissingDocument");
            var badJson = RawZip(new[] { Item("manifest.json", man), Item("chara.json", "{\"profile\": "), Item("coord/coord.json", "{}") });
            t.Equal(SaveErrorCode.BadDocument, CharaContainer.Load(badJson, LOpts()).ErrorCode, "T11: JSON syntax error → BadDocument");
            var badMan = RawZip(new[] { Item("manifest.json", "{\"format\":\"hs2next.chara\",\"formatVersion\":\"x\"}"), Item("chara.json", "{}"), Item("coord/coord.json", "{}") });
            t.Equal(SaveErrorCode.BadManifest, CharaContainer.Load(badMan, LOpts()).ErrorCode, "T11: manifest type error → BadManifest");
        }

        static void T16Placeholder(TestRunner t)
        {
            var p = ThumbnailPlaceholder.Png();
            var h = PngInfo.ReadHeader(p);
            t.Check(h.HasValue && h.Value.Width == 504 && h.Value.Height == 704 && h.Value.BitDepth == 8 && h.Value.ColorType == 2 && h.Value.Interlace == 0,
                "T16: placeholder is a 504x704 8-bit RGB PNG, no interlace (" + p.Length + " bytes)");
            string path = Path.Combine(Path.GetTempPath(), "hs2n_ph_" + Process.GetCurrentProcess().Id + ".png");
            File.WriteAllBytes(path, p);
            string py = Python("import zlib,struct,sys; b=open(sys.argv[1],'rb').read(); i=8; out=b''\nwhile i < len(b):\n n=struct.unpack('>I',b[i:i+4])[0]; ty=b[i+4:i+8]; d=b[i+8:i+8+n]; assert zlib.crc32(ty+d)==struct.unpack('>I',b[i+8+n:i+12+n])[0]; out+=d if ty==b'IDAT' else b''; i+=12+n\nr=zlib.decompress(out); print(len(r), set(r[1:1513]))", path);
            File.Delete(path);
            t.Check(py == "1065152 {128}", "T16: placeholder chunk CRCs and zlib data valid, all pixels 128 (python: " + py + ")");
            var c = SmallChara();
            var bytes = c.ToBytes(Opts());
            t.Check(bytes.Take(p.Length).SequenceEqual(p), "T16: headless save writes the placeholder");
            var m = CharaContainer.Load(bytes, LOpts()).Unwrap().LoadedManifest;
            t.Check(m.ThumbnailWidth == 504 && m.ThumbnailHeight == 704, "T16: manifest.thumbnail = 504 x 704");
            var keep = new byte[0];
            var o = Opts();
            o.Thumbnail = ThumbnailMode.Render;
            o.Renderer = () => ThumbnailPlaceholder.SolidRgb(504, 704, 10, 20, 30);
            var rendered = c.ToBytes(o);
            t.Check(PngInfo.ReadHeader(ContainerReader.Read(rendered).Thumbnail)?.Width == 504 && !rendered.Take(p.Length).SequenceEqual(p), "T16: Render mode uses the renderer's PNG");
            o.Renderer = null;
            var lc = CharaContainer.Load(rendered, LOpts()).Unwrap();
            var keptBytes = lc.ToBytes(o);
            t.Check(ContainerReader.Read(keptBytes).Thumbnail.SequenceEqual(ContainerReader.Read(rendered).Thumbnail), "T16: Render without a renderer keeps the previous thumbnail");
        }
    }
}

// D1 T14: size and speed on this machine (plain Mono here; the Unity editor run repeats
// it), and every limit enforced.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using static HS2Next.Save.Tests.InfraFixtures;

namespace HS2Next.Save.Tests
{
    public static class InfraTests_Perf
    {
        /// <summary>Samples GC.GetTotalMemory(false) every millisecond on a background thread; reports the peak above the baseline.</summary>
        sealed class PeakMeter : IDisposable
        {
            readonly Thread th;
            volatile bool stop;
            public long Baseline, Peak;

            public PeakMeter()
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Baseline = GC.GetTotalMemory(true);
                Peak = Baseline;
                th = new Thread(() =>
                {
                    while (!stop)
                    {
                        long m = GC.GetTotalMemory(false);
                        if (m > Peak) Peak = m;
                        Thread.Sleep(1);
                    }
                }) { IsBackground = true };
                th.Start();
            }

            public long Delta => Peak - Baseline;

            public void Dispose()
            {
                long m = GC.GetTotalMemory(false);
                if (m > Peak) Peak = m;
                stop = true;
                th.Join();
            }
        }

        static byte[] RandomPng(int size, int seed)
        {
            var b = new byte[size];
            new Random(seed).NextBytes(b);
            Array.Copy(PngInfo.Signature, b, 8);
            return b;
        }

        /// <summary>Median of n timed runs (after one warm-up).</summary>
        static double Median(int n, Action a)
        {
            a();
            var ms = new List<double>();
            for (int i = 0; i < n; i++)
            {
                var sw = Stopwatch.StartNew();
                a();
                ms.Add(sw.Elapsed.TotalMilliseconds);
            }
            ms.Sort();
            return ms[ms.Count / 2];
        }

        public static void Run(TestRunner t)
        {
            BigCoordinate(t);
            SmallCharacter(t);
            Scene(t);
            Limits(t);
        }

        static void BigCoordinate(TestRunner t)
        {
            var c = new CoordContainer();
            c.Coord.Name = "big";
            var acc = new AccessoryEntry { Id = Id(1), Category = 352, Item = "test.mod#1" };
            c.Coord.Accessories.Add(acc);
            acc.MaterialOverrides.Add(new MeTexture { Material = "mat", Property = "MainTex", Image = c.Res.Add(RandomPng(40 * 1000 * 1000, 1)) });
            for (int i = 0; i < 100; i++)
            {
                var a = new AccessoryEntry { Id = Id(10 + i), Category = 353, Item = "test.mod#" + (i + 2) };
                a.MaterialOverrides.Add(new MeTexture { Material = "m" + i, Property = "OverTex", Image = c.Res.Add(RandomPng(55 * 1000, 100 + i)) });
                c.Coord.Accessories.Add(a);
            }
            string path = Path.Combine(t.TempDir, "big.hs2ncoord");
            c.Save(path, Opts()).ThrowIfFailed();     // warm-up
            File.Delete(path);
            double save;
            long savePeak;
            using (var pm = new PeakMeter())
            {
                var sw = Stopwatch.StartNew();
                var r = c.Save(path, Opts());
                save = sw.Elapsed.TotalSeconds;
                r.ThrowIfFailed();
                pm.Dispose();
                savePeak = pm.Delta;
            }
            long size = new FileInfo(path).Length;
            CoordContainer.Load(path, LOpts()).Unwrap();   // warm-up
            c = null;
            double load;
            long loadPeak;
            CoordContainer loaded;
            using (var pm = new PeakMeter())
            {
                var sw = Stopwatch.StartNew();
                loaded = CoordContainer.Load(path, LOpts()).Unwrap();
                load = sw.Elapsed.TotalSeconds;
                pm.Dispose();
                loadPeak = pm.Delta;
            }
            t.Info(string.Format("T14 big coordinate: file {0:F1} MB, save {1:F2} s (peak +{2:F0} MB), load {3:F2} s (peak +{4:F0} MB), {5} resources",
                size / 1e6, save, savePeak / 1e6, load, loadPeak / 1e6, loaded.Res.Count));
            t.Check(size > 45e6 && size < 46.5e6, "T14: synthetic coordinate ≈ 45.5 MB (" + size + ")");
            t.Check(save <= 2.0, "T14: save ≤ 2 s (" + save.ToString("F2") + ")");
            t.Check(load <= 1.5, "T14: load ≤ 1.5 s (" + load.ToString("F2") + ")");
            t.Check(savePeak <= 3 * size && loadPeak <= 3 * size, "T14: peak managed memory ≤ 3 × file size (save +" + savePeak / 1000000 + " MB, load +" + loadPeak / 1000000 + " MB)");
        }

        static void SmallCharacter(TestRunner t)
        {
            var c = SmallChara(20);
            c.Chara.Body.SkinOverlays.Add(new SkinOverlay { Target = "BodyOver", Image = c.Res.Add(RandomPng(145 * 1000, 3)) });
            string path = Path.Combine(t.TempDir, "small.hs2nchara");
            c.Save(path, Opts()).ThrowIfFailed();
            long size = new FileInfo(path).Length;
            var o = Opts();
            double save = Median(9, () => c.Save(path, o).ThrowIfFailed());
            var lo = LOpts();
            double load = Median(9, () => CharaContainer.Load(path, lo).Unwrap());
            t.Info(string.Format("T14 character: file {0:F0} KB, save {1:F1} ms, load {2:F1} ms (median of 9)", size / 1e3, save, load));
            t.Check(size > 130e3 && size < 170e3, "T14: character ≈ 150 KB (" + size + ")");
            t.Check(save <= 30 && load <= 30, "T14: character save and load ≤ 30 ms (" + save.ToString("F1") + " / " + load.ToString("F1") + ")");
        }

        static void Scene(TestRunner t)
        {
            var s = SmallScene(10, 200, SmallChara(10));
            string path = Path.Combine(t.TempDir, "scene.hs2nscene");
            var o = Opts();
            s.Save(path, o).ThrowIfFailed();
            double save = Median(5, () => s.Save(path, o).ThrowIfFailed());
            double load = Median(5, () => SceneContainer.Load(path, LOpts()).Unwrap());
            t.Info(string.Format("T14 scene (10 characters, 200 items): file {0:F0} KB, save {1:F1} ms, load {2:F1} ms", new FileInfo(path).Length / 1e3, save, load));
            t.Check(save <= 500, "T14: scene save ≤ 0.5 s (" + save.ToString("F1") + " ms)");
        }

        static void Limits(TestRunner t)
        {
            string man = ManifestJson();
            // reader: declared sizes
            var big = Item("ext/x.y/big.bin", "x"); big.USizeOverride = (1u << 30) + 1;
            t.Equal(SaveErrorCode.TooLarge, CharaContainer.Load(RawZip(new[] { Item("manifest.json", man), big }), LOpts()).ErrorCode, "T14: reader refuses an entry declared over 1 GiB");
            var doc = Item("chara.json", "{}"); doc.USizeOverride = (257u << 20);
            t.Equal(SaveErrorCode.TooLarge, CharaContainer.Load(RawZip(new[] { Item("manifest.json", man), doc }), LOpts()).ErrorCode, "T14: reader refuses a JSON document declared over 256 MiB");
            var many = new List<RawItem> { Item("manifest.json", man) };
            for (int i = 0; i < 5; i++) { var e = Item("ext/x.y/p" + i, "x", 8); e.USizeOverride = (1u << 30) - 1; many.Add(e); }
            t.Equal(SaveErrorCode.TooLarge, CharaContainer.Load(RawZip(many), LOpts()).ErrorCode, "T14: reader refuses a declared total over 4 GiB");
            // writer: entry count with the real limit
            var w = new ContainerWriter { MainDocument = "chara.json" };
            for (int i = 0; i < 65535; i++) w.Add("f/" + i, new byte[0]);
            t.Throws<SaveFormatException>(() => w.CheckLimits(), "T14: writer refuses 65,535 entries (the count 0xFFFF is the Zip64 marker)");
            // writer: sizes (lowered limits; the checks are the same code)
            var lim = new ContainerLimits { MaxEntrySize = 1000, MaxDocumentSize = 100, MaxTotalSize = 1500 };
            var w1 = new ContainerWriter { Limits = lim };
            w1.Add("a.bin", new byte[1001]);
            t.Throws<SaveFormatException>(() => w1.CheckLimits(), "T14: writer refuses an entry over the entry limit");
            var w2 = new ContainerWriter { Limits = lim };
            w2.Add("chara.json", new byte[101]);
            t.Throws<SaveFormatException>(() => w2.CheckLimits(), "T14: writer refuses a document over the document limit");
            var w3 = new ContainerWriter { Limits = lim };
            w3.Add("a.bin", new byte[800]); w3.Add("b.bin", new byte[800]);
            t.Throws<SaveFormatException>(() => w3.CheckLimits(), "T14: writer refuses a total over the total limit");
            var w4 = new ContainerWriter { Limits = new ContainerLimits { MaxFileSize = 100 } };
            w4.Add("a.bin", new byte[500]);
            t.Throws<SaveFormatException>(() => w4.ToBytes(), "T14: writer refuses a file over the file-size limit");
            // inline ext block limit: 1 MiB of canonical UTF-8 text, enforced where inline data is produced
            var c = SmallChara();
            t.Throws<ArgumentException>(() => c.Chara.Ext.Set("stp3.big", new JValue(new string('x', 1 << 20))), "T14: ExtInline.Set refuses a block over 1 MiB");
            c.Chara.Ext.Set("stp3.edge", new JValue(new string('x', (1 << 20) - 2)));
            t.Equal(1 << 20, ExtInline.CanonicalSize(c.Chara.Ext.Get("stp3.edge")), "T14: a block of exactly 1 MiB (quotes included) is accepted");
            t.Throws<ArgumentException>(() => c.Chara.Ext.Set("stp3.wide", new JValue(new string('é', 600000))), "T14: the limit counts UTF-8 bytes (600,000 two-byte characters refused)");
            c.Chara.Ext.Remove("stp3.edge");
            // an oversized block written by another writer loads with a warning and is written back unchanged (preservation)
            var file = c.ToBytes(Opts());
            var bigFile = Repack(file, m => EditJson(m, "chara.json", j => j["ext"] = new JObject { ["stp3.big"] = new string('x', 1 << 20) }));
            var lb = CharaContainer.Load(bigFile, LOpts());
            t.Check(lb.Ok && lb.Findings.Has("ext.inlineTooLarge") && lb.Value.Chara.Ext.Get("stp3.big") != null, "T14: an inline block over 1 MiB in a file loads with warning ext.inlineTooLarge (" + lb + ")");
            var r = lb.Ok ? lb.Value.Save(Path.Combine(t.TempDir, "bigext.hs2nchara"), Opts()) : null;
            byte[] Doc(byte[] f) => Entries(f).First(e => e.Key == "chara.json").Value;
            t.Check(r != null && r.Ok && Doc(bigFile).SequenceEqual(Doc(File.ReadAllBytes(Path.Combine(t.TempDir, "bigext.hs2nchara")))),
                "T14: the oversized block is written back unchanged (" + r + ")");
        }
    }
}

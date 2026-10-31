// Unity-free tests of the zip index (Runtime/IO/ZipIndex.cs: the central directory read in one piece), plain Mono:
// Tools/mono_tests.ps1 -Suite content -Groups ZipIndex. Generated zips (stored and deflated entries, names in several
// scripts, an empty entry, a directory entry, a ZIP64 directory of 70,000 entries) against System.IO.Compression's reader:
// the same names, methods, sizes and contents; data offsets agree with the local headers; reading an entry first (before
// its data offset was asked) and asking afterwards agree.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using HS2Next.IO;

namespace HS2Next.Content.Tests
{
    using HS2Next.Save.Tests;

    public static class ZipIndexTests
    {
        public static void Run(TestRunner t)
        {
            string dir = Path.Combine(t.TempDir, "zipindex");
            Directory.CreateDirectory(dir);
            var rnd = new Random(52);

            // a mixed zip
            string mixed = Path.Combine(dir, "mixed.zip");
            var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            using (var z = ZipFile.Open(mixed, ZipArchiveMode.Create))
            {
                string[] names = { "manifest.xml", "abdata/chara/x/part.unity3d", "abdata/list/characustom/a_b.csv", "abdata/日本語/名前.png",
                                   "abdata/empty.bin", "abdata/dir/", "abdata/big/stored.unity3d", "abdata/Upper/Case.PNG" };
                foreach (var n in names)
                {
                    bool stored = n.EndsWith(".unity3d", StringComparison.Ordinal);
                    var e = z.CreateEntry(n, stored ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                    if (n.EndsWith("/", StringComparison.Ordinal)) continue;
                    int len = n.Contains("empty") ? 0 : n.Contains("big") ? 300000 : 1000 + rnd.Next(5000);
                    var b = new byte[len];
                    for (int i = 0; i < len; i++) b[i] = (byte)(i % 7 == 0 ? rnd.Next(256) : 'a' + i % 13);
                    using (var s = e.Open()) s.Write(b, 0, b.Length);
                    contents[n] = b;
                }
            }
            Compare(t, mixed, contents, "mixed");

            // a ZIP64 directory (more than 65,535 entries)
            string many = Path.Combine(dir, "many.zip");
            var small = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            using (var z = ZipFile.Open(many, ZipArchiveMode.Create))
                for (int i = 0; i < 70000; i++)
                {
                    string n = "abdata/f/" + i.ToString("D6") + ".txt";
                    var e = z.CreateEntry(n, i % 2 == 0 ? CompressionLevel.NoCompression : CompressionLevel.Fastest);
                    var b = Encoding.ASCII.GetBytes("entry " + i);
                    using (var s = e.Open()) s.Write(b, 0, b.Length);
                    if (i % 997 == 0 || i == 69999) small[n] = b;
                }
            Compare(t, many, small, "zip64", 70000);
        }

        static void Compare(TestRunner t, string file, Dictionary<string, byte[]> contents, string label, int expectCount = -1)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var idx = new ZipIndex(file);
            double ms = sw.Elapsed.TotalMilliseconds;
            using (var z = ZipFile.OpenRead(file))
            {
                var refs = z.Entries.ToList();
                t.Equal(refs.Count, idx.Entries.Count, $"zipindex {label}: entry count as the framework's reader ({ms:F0} ms)");
                if (expectCount >= 0) t.Equal(expectCount, idx.Entries.Count, $"zipindex {label}: {expectCount} entries (a ZIP64 directory)");
                int bad = 0;
                foreach (var r in refs)
                {
                    if (!idx.Entries.TryGetValue(r.FullName.Replace('\\', '/'), out var e)) { bad++; continue; }
                    if (e.Size != r.Length || e.CompressedSize != r.CompressedLength || e.Name != r.FullName) bad++;
                }
                t.Equal(0, bad, $"zipindex {label}: names, sizes and compressed sizes equal the framework's reader");
            }
            int wrong = 0, offsets = 0;
            bool first = true;
            foreach (var kv in contents)
            {
                var e = idx.Entries[kv.Key];
                byte[] got;
                long off;
                if (first) { got = idx.ReadAll(e); off = e.DataOffset; }   // read before the offset was asked
                else { off = e.DataOffset; got = idx.ReadAll(e); }
                first = false;
                if (!got.SequenceEqual(kv.Value)) wrong++;
                // the data offset points behind the local header of this entry
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read))
                {
                    var lh = new byte[30];
                    fs.Position = e.LocalHeaderOffset;
                    fs.Read(lh, 0, 30);
                    bool sig = lh[0] == 0x50 && lh[1] == 0x4b && lh[2] == 3 && lh[3] == 4;
                    long expect = e.LocalHeaderOffset + 30 + (lh[26] | lh[27] << 8) + (lh[28] | lh[29] << 8);
                    if (sig && off == expect) offsets++;
                }
            }
            t.Equal(0, wrong, $"zipindex {label}: {contents.Count} entries read back equal (stored and deflated)");
            t.Equal(contents.Count, offsets, $"zipindex {label}: data offsets behind each local header");
        }
    }
}

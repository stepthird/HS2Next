// Texture2D headers read from a bundle's bytes without Unity (v0.3 trial 2): the part thumbnails check the header of a
// thumbnail texture before Unity loads and uploads it, so a texture Unity 6 cannot upload never reaches the GPU.
// Two kinds were seen to remove the D3D12 device or crash the player during the asynchronous upload of a thumbnail:
//   * m_MipCount < 1 (bundles built with Unity 2018; Unity 6 asserts 'mipLevel < mipCount');
//   * a block-compressed format (DXT1 / DXT5 / BC4-BC7) whose width or height is not a multiple of four: Unity 6 falls
//     back to RGBA32, the upload then expects RGBA32-sized image data from the .resS stream that holds the compressed
//     chain ("Streaming Size differs from Actual Size", "unable to read texture data"), and the player crashed with an
//     access violation in a worker thread (the v0.3 trial: a 512 x 511 DXT5 head thumbnail).
// Defect() names these and the other header inconsistencies (no size, more mips than the size allows, a complete image
// size smaller than the chain the header declares, formats a desktop GPU cannot sample, which take the same fallback).
// Read() parses the bundle (Hs2BundleFile: uncompressed, LZ4, LZ4HC and LZMA blocks; a bundle it cannot parse is reported
// unreadable, and Malformed when its structure is broken - a node outside its data, as in a thumbnail bundle whose streamed
// texture Unity then fails to read) and returns the headers of every Texture2D object by its name and by the short names the
// bundle's container gives it (the names AssetBundle.LoadAsset matches). Only the fixed head of each object is read (name, size,
// format, mip count); image data are never read or decoded. Thread-safe (no Unity API): the loader reads on a worker.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace HS2Next.Content
{
    /// <summary>The fixed head of a serialized Texture2D.</summary>
    public struct TextureHeader
    {
        public string Name;
        public int Width, Height, Format, Mips, CompleteSize;
        /// <summary>The format's name for messages ("DXT5", or the number).</summary>
        public string FormatName => TextureHeaders.FormatName(Format);
        public override string ToString() => $"{Name} {Width}x{Height} {FormatName} mips {Mips}";
    }

    /// <summary>The Texture2D headers of one bundle (or why they could not be read).</summary>
    public sealed class BundleTextureHeaders
    {
        /// <summary>Headers by m_Name and by container short name (case-insensitive); null when <see cref="Error"/> is set.</summary>
        public Dictionary<string, TextureHeader> ByName;
        /// <summary>Why the bundle could not be read (LZMA blocks, an unknown layout, I/O), or null.</summary>
        public string Error;
        /// <summary>The bundle's data blocks are LZMA (read with Hs2Lzma; an error then names an LZMA stream it could not decode).</summary>
        public bool Lzma;
        /// <summary>The bundle's structure is broken (a node or block outside its data, a truncated or corrupt block): Unity may
        /// open it, but its streamed image data cannot be read. The thumbnail loader refuses its textures.</summary>
        public bool Malformed;
        public int Textures;

        public bool TryGet(string name, out TextureHeader h)
        {
            h = default;
            return ByName != null && name != null && ByName.TryGetValue(name, out h);
        }
    }

    /// <summary>Where a bundle's bytes are: a file region (a whole file, a stored zip entry) or a deflated zip entry.</summary>
    public sealed class BundleFileRef
    {
        /// <summary>The catalog's cache key of the bundle (the header cache's key with the file's stamp).</summary>
        public string Key;
        public string File;
        public long Offset, Length;
        /// <summary>A deflated zip entry: reads the whole bundle (on the worker); null for file regions.</summary>
        public Func<byte[]> ReadAll;
        /// <summary>Size and write time of <see cref="File"/>: a changed file is read again.</summary>
        public string Stamp;
    }

    public static class TextureHeaders
    {
        public const int ClassTexture2D = 28;

        // TextureFormat -> (block width, bytes per block); block width 1 = uncompressed bytes per pixel
        // (the table of Tools/texguard_repair.py and Tools/survey/thumb_guard_census.py)
        static readonly Dictionary<int, (int bw, int bpb)> Formats = new Dictionary<int, (int, int)>
        {
            [1] = (1, 1), [2] = (1, 2), [3] = (1, 3), [4] = (1, 4), [5] = (1, 4), [7] = (1, 2), [9] = (1, 2), [13] = (1, 2), [14] = (1, 4),
            [15] = (1, 2), [16] = (1, 4), [17] = (1, 8), [18] = (1, 4), [19] = (1, 8), [20] = (1, 16), [62] = (1, 2), [63] = (1, 1),
            [10] = (4, 8), [12] = (4, 16), [24] = (4, 16), [25] = (4, 16), [26] = (4, 8), [27] = (4, 16), [28] = (4, 8), [29] = (4, 16),
        };

        static readonly Dictionary<int, string> Names = new Dictionary<int, string>
        {
            [1] = "Alpha8", [2] = "ARGB4444", [3] = "RGB24", [4] = "RGBA32", [5] = "ARGB32", [7] = "RGB565", [9] = "R16", [10] = "DXT1",
            [12] = "DXT5", [13] = "RGBA4444", [14] = "BGRA32", [24] = "BC6H", [25] = "BC7", [26] = "BC4", [27] = "BC5",
            [28] = "DXT1Crunched", [29] = "DXT5Crunched", [34] = "ETC_RGB4", [47] = "ETC2_RGBA8", [63] = "R8",
        };

        public static string FormatName(int f) => Names.TryGetValue(f, out var n) ? n : f.ToString(CultureInfo.InvariantCulture);

        /// <summary>DXT1 / DXT5 / BC4-BC7 and crunched DXT (decoded to DXT): the top level must be a multiple of four.</summary>
        public static bool IsBlockCompressed(int f) => f == 10 || f == 12 || (f >= 24 && f <= 29);

        static bool IsCrunched(int f) => f == 28 || f == 29 || f == 64 || f == 65;

        /// <summary>PVRTC, ATC, EAC / ETC, ETC2, ASTC and the 3DS ETC formats: decoded on the CPU by desktop players.</summary>
        public static bool IsCpuDecoded(int f) => (f >= 30 && f <= 36) || (f >= 41 && f <= 61) || f == 64 || f == 65 || (f >= 66 && f <= 71);

        public static int FullChain(int w, int h)
        {
            int m = Math.Max(1, Math.Max(w, h)), n = 1;
            while (m > 1) { m >>= 1; n++; }
            return n;
        }

        /// <summary>Bytes of the first <paramref name="mips"/> levels of a known format, or -1.</summary>
        public static long ChainSize(int w, int h, int format, int mips)
        {
            if (!Formats.TryGetValue(format, out var f)) return -1;
            long total = 0;
            for (int i = 0; i < mips; i++)
            {
                int mw = Math.Max(1, w >> i), mh = Math.Max(1, h >> i);
                total += f.bw == 1 ? (long)mw * mh * f.bpb : (long)((mw + 3) / 4) * ((mh + 3) / 4) * f.bpb;
            }
            return total;
        }

        /// <summary>Why Unity 6 must not upload a texture with this header, or null (the census' classes).</summary>
        public static string Defect(TextureHeader h)
        {
            if (h.Width < 1 || h.Height < 1) return "empty size";
            if (h.Mips < 1) return "mip < 1";
            if (IsBlockCompressed(h.Format) && (h.Width % 4 != 0 || h.Height % 4 != 0)) return "block size";
            if (h.Mips > FullChain(h.Width, h.Height)) return "mips > chain";
            if (IsCpuDecoded(h.Format)) return "cpu format";
            if (!IsCrunched(h.Format))
            {
                long chain = ChainSize(h.Width, h.Height, h.Format, h.Mips);
                // a single image: the complete size holds at least the declared chain (a few bytes more are common and harmless)
                if (chain > 0 && h.CompleteSize < chain) return "image size " + h.CompleteSize + " < chain " + chain;
            }
            return null;
        }

        // ------------------------------------------------------------ the cache (per bundle key and file stamp)

        static readonly ConcurrentDictionary<string, BundleTextureHeaders> cache = new ConcurrentDictionary<string, BundleTextureHeaders>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Bundles read (or found unreadable) since the start; tests.</summary>
        public static int BundlesRead;

        static string CacheKey(BundleFileRef f) => f.Key + "|" + f.Stamp;

        /// <summary>The headers of a bundle read earlier (same key and stamp).</summary>
        public static bool TryCached(BundleFileRef f, out BundleTextureHeaders h)
        {
            h = null;
            return f != null && cache.TryGetValue(CacheKey(f), out h);
        }

        /// <summary>Reads (or takes from the cache) the headers of <paramref name="f"/>; never throws.</summary>
        public static BundleTextureHeaders Get(BundleFileRef f)
        {
            if (f == null) return new BundleTextureHeaders { Error = "no bundle file" };
            string k = CacheKey(f);
            if (cache.TryGetValue(k, out var h)) return h;
            h = Read(f);
            if (cache.Count > 8192) cache.Clear();
            cache[k] = h;
            return h;
        }

        /// <summary>Forgets every cached bundle (tests).</summary>
        public static void ClearCache() => cache.Clear();

        /// <summary>Reads the headers of a bundle file (no cache); never throws.</summary>
        public static BundleTextureHeaders Read(BundleFileRef f)
        {
            System.Threading.Interlocked.Increment(ref BundlesRead);
            try
            {
                Hs2BundleFile b = f.ReadAll != null ? Hs2BundleFile.FromBytes(f.ReadAll(), f.Key, lzma: true) : Hs2BundleFile.OpenFile(f.File, f.Offset, f.Length, f.Key, lzma: true);
                return Read(b);
            }
            catch (Exception e)
            {
                return new BundleTextureHeaders { Error = e.GetType().Name + ": " + e.Message, Lzma = e.Message.IndexOf("LZMA", StringComparison.Ordinal) >= 0, Malformed = IsStructural(e) };
            }
        }

        /// <summary>Reads the headers of an opened bundle; never throws.</summary>
        public static BundleTextureHeaders Read(Hs2BundleFile b)
        {
            var r = new BundleTextureHeaders();
            try
            {
                r.Lzma = b.DataCompression == 1;
                if (r.Lzma && !b.Lzma) { r.Error = "LZMA blocks"; return r; }
                var byName = new Dictionary<string, TextureHeader>(StringComparer.OrdinalIgnoreCase);
                foreach (var n in b.Nodes)
                {
                    if (!IsSerialized(b, n)) continue;
                    var node = n;
                    var sf = Hs2SerializedFile.FromStream(() => b.OpenNode(node), b.Label + ":" + n.Path);
                    if (!ParseVersion(sf.UnityVersion, out int major, out int minor) && !ParseVersion(b.UnityRevision, out major, out minor))
                        throw new Hs2SerializedFileException("unknown Unity version '" + sf.UnityVersion + "'");
                    var byPath = new Dictionary<long, TextureHeader>();
                    using (var s = b.OpenNode(node))
                    {
                        foreach (var o in sf.Objects)
                        {
                            if (o.ClassId != ClassTexture2D) continue;
                            var h = ReadHead(s, o, major, minor);
                            byPath[o.PathId] = h;
                            r.Textures++;
                            if (h.Name != null && !byName.ContainsKey(h.Name)) byName[h.Name] = h;
                        }
                        // the container's short names (what LoadAsset(name) matches) -> the texture they point at
                        foreach (var o in sf.Objects)
                        {
                            if (o.ClassId != Hs2SerializedFile.ClassAssetBundle) continue;
                            foreach (var (key, fileId, pathId) in Container(sf, o))
                            {
                                if (fileId != 0 || !byPath.TryGetValue(pathId, out var h)) continue;
                                string shortName = BundleNames.ShortName(key);
                                if (!string.IsNullOrEmpty(shortName) && !byName.ContainsKey(shortName)) byName[shortName] = h;
                            }
                        }
                    }
                }
                r.ByName = byName;
            }
            catch (Exception e)
            {
                r.Error = e.GetType().Name + ": " + e.Message;
                r.Malformed = IsStructural(e);
                r.ByName = null;
            }
            return r;
        }

        // A parse error that says the bundle itself is broken (as opposed to a layout this reader does not know).
        static bool IsStructural(Exception e)
        {
            if (!(e is Hs2SerializedFileException)) return false;
            string m = e.Message;
            foreach (var k in new[] { "outside the", "truncated", "past the", "in no block", "header size", "malformed", "lzma: " })
                if (m.IndexOf(k, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        // Serialized files carry node flag 4; older writers leave every node's flags 0, so there a name without an extension
        // counts too (the pre-upload check's rule). An empty node holds nothing to read.
        static bool IsSerialized(Hs2BundleFile b, Hs2BundleNode n)
        {
            if (n.Size <= 0) return false;
            if ((n.Flags & 4) != 0) return true;
            if (n.Flags != 0 || n.Path.IndexOf('.') >= 0) return false;
            foreach (var x in b.Nodes) if ((x.Flags & 4) != 0) return false;
            return true;
        }

        static bool ParseVersion(string v, out int major, out int minor)
        {
            major = minor = 0;
            if (string.IsNullOrEmpty(v)) return false;
            var p = v.Split('.');
            if (p.Length < 2 || !int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out major)) return false;
            int i = 0;
            while (i < p[1].Length && char.IsDigit(p[1][i])) i++;
            if (i == 0 || !int.TryParse(p[1].Substring(0, i), NumberStyles.Integer, CultureInfo.InvariantCulture, out minor)) return false;
            return major >= 3;
        }

        static bool AtLeast(int major, int minor, int m, int n) => major > m || (major == m && minor >= n);

        // The fixed head of a Texture2D: m_Name; from 2017.3 m_ForcedFallbackFormat and m_DownscaleFallback (2020.2: and
        // m_IsAlphaChannelOptional), aligned; m_Width, m_Height, m_CompleteImageSize; 2020.1: m_MipsStripped; m_TextureFormat;
        // m_MipCount (before 5.2 the bool m_MipMap).
        static TextureHeader ReadHead(Stream s, Hs2SerializedObject o, int major, int minor)
        {
            int want = (int)Math.Min(o.Size, 1024);
            var buf = Range(s, o.Start, want);
            int nameLen = buf.Length >= 4 ? BitConverter.ToInt32(buf, 0) : -1;
            if (nameLen < 0 || nameLen > 1 << 16) throw new Hs2SerializedFileException("texture name length " + nameLen);
            if (nameLen + 64 > buf.Length && buf.Length < o.Size) buf = Range(s, o.Start, (int)Math.Min(o.Size, nameLen + 128));
            var r = new Hs2FieldReader(buf, 0);
            var h = new TextureHeader { Name = r.AlignedString() };
            if (AtLeast(major, minor, 2017, 3))
            {
                r.I32();   // forced fallback format
                r.U8();    // downscale fallback
                if (AtLeast(major, minor, 2020, 2)) r.U8();   // alpha channel optional
                r.Align();
            }
            h.Width = r.I32();
            h.Height = r.I32();
            h.CompleteSize = r.I32();
            if (AtLeast(major, minor, 2020, 1)) r.I32();   // mips stripped
            h.Format = r.I32();
            if (AtLeast(major, minor, 5, 2)) h.Mips = r.I32();
            else h.Mips = r.U8() != 0 ? FullChain(h.Width, h.Height) : 1;
            return h;
        }

        static byte[] Range(Stream s, long start, int count)
        {
            var b = new byte[count];
            s.Position = start;
            int got = 0;
            while (got < count) { int n = s.Read(b, got, count - got); if (n <= 0) break; got += n; }
            if (got != count) Array.Resize(ref b, got);
            return b;
        }

        // The m_Container entries of an AssetBundle object: key, then preloadIndex, preloadSize and the asset PPtr.
        static IEnumerable<(string key, int fileId, long pathId)> Container(Hs2SerializedFile sf, Hs2SerializedObject o)
        {
            var r = sf.Reader(o);
            bool wide = sf.Version >= 14;
            r.AlignedString();
            int preload = r.I32();
            if (preload < 0 || preload > 10000000) throw new Hs2SerializedFileException("preload table size " + preload);
            r.Skip((long)preload * (wide ? 12 : 8));
            int n = r.I32();
            if (n < 0 || n > 10000000) throw new Hs2SerializedFileException("container size " + n);
            var list = new List<(string, int, long)>(n);
            for (int i = 0; i < n; i++)
            {
                string key = r.AlignedString();
                r.I32(); r.I32();   // preload index, size
                int fileId = r.I32();
                long pathId = wide ? r.I64() : r.I32();
                list.Add((key, fileId, pathId));
            }
            return list;
        }
    }
}

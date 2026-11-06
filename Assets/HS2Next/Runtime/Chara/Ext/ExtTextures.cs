// Decoding of the image files embedded in card plugin data (KSOX/KCOX PNGs, MaterialEditor PNG/JPEG;
// 4) with a content-hash cache shared by all characters: identical bytes
// are decoded once and reference-counted (Acquire / Release). Textures are uploaded and then made non-readable
// (no CPU copy kept). Sizes can be read from the file header without decoding (TrySize). A header that claims more than
// MaxDecodeSide pixels a side is refused before anything is allocated, wherever the image came from.
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HS2Next.Chara.Ext
{
    public static class ExtTextures
    {
        sealed class Entry { public Texture2D Tex; public int Refs; public long Bytes; }
        static readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>(StringComparer.Ordinal);

        /// <summary>Statistics since start (or <see cref="ResetStats"/>): decodes, cache hits, failures, estimated GPU bytes alive / peak.</summary>
        public static int Decoded, CacheHits, Failed;
        public static long DecodedBytes, LiveBytes, PeakBytes;
        public static int Live => cache.Count;

        public static void ResetStats() { Decoded = CacheHits = Failed = 0; DecodedBytes = 0; PeakBytes = LiveBytes; }

        /// <summary>The largest side a stored image decodes at, above every image on the install: a larger header is refused.</summary>
        public const int MaxDecodeSide = 16384;

        /// <summary>True when the image's header claims a side above <see cref="MaxDecodeSide"/>.</summary>
        public static bool TooLarge(byte[] b)
        {
            if (b == null || b.Length < 24) return false;
            if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
            {
                uint pw = ((uint)b[16] << 24) | ((uint)b[17] << 16) | ((uint)b[18] << 8) | b[19];
                uint ph = ((uint)b[20] << 24) | ((uint)b[21] << 16) | ((uint)b[22] << 8) | b[23];
                return pw > MaxDecodeSide || ph > MaxDecodeSide;
            }
            return TrySize(b, out int w, out int h) && (w > MaxDecodeSide || h > MaxDecodeSide);
        }

        /// <summary>Cache key: SHA-1 of the bytes plus the decode settings.</summary>
        public static string Key(byte[] bytes, bool linear, bool mips)
        {
            using (var sha = SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "") + (linear ? "L" : "S") + (mips ? "M" : "N");
        }

        /// <summary>Decodes (or reuses) an image. sRGB unless <paramref name="linear"/>. Returns null when the bytes are not a
        /// loadable PNG/JPEG. Every successful call must be paired with <see cref="Release"/> of the returned key.</summary>
        public static Texture2D Acquire(byte[] bytes, bool linear, bool mips, string name, out string key)
        {
            key = null;
            if (bytes == null || bytes.Length == 0) return null;
            if (TooLarge(bytes)) { Failed++; return null; }
            key = Key(bytes, linear, mips);
            if (cache.TryGetValue(key, out var e) && e.Tex)
            {
                e.Refs++; CacheHits++;
                return e.Tex;
            }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mips, linear) { name = name };
            bool ok;
            try { ok = tex.LoadImage(bytes, true); }
            catch (Exception) { ok = false; }
            if (!ok || tex.width <= 2 && tex.height <= 2 && !LooksTiny(bytes))
            {
                Object.DestroyImmediate(tex);
                Failed++; key = null;
                return null;
            }
            long b = (long)tex.width * tex.height * 4;
            if (mips) b = b * 4 / 3;
            cache[key] = new Entry { Tex = tex, Refs = 1, Bytes = b };
            Decoded++; DecodedBytes += b; LiveBytes += b;
            if (LiveBytes > PeakBytes) PeakBytes = LiveBytes;
            return tex;
        }

        /// <summary>Drops one reference; the texture is destroyed when none is left.</summary>
        public static void Release(string key)
        {
            if (key == null || !cache.TryGetValue(key, out var e)) return;
            if (--e.Refs > 0) return;
            cache.Remove(key);
            LiveBytes -= e.Bytes;
            if (e.Tex) Object.DestroyImmediate(e.Tex);
        }

        // LoadImage leaves a 2x2 texture (or an 8x8 "?" in some versions) when it fails without returning false;
        // a real 1..2 px image says so in its header.
        static bool LooksTiny(byte[] bytes) => TrySize(bytes, out int w, out int h) && w <= 2 && h <= 2;

        /// <summary>Width/height from a PNG IHDR or a JPEG SOFn header, without decoding.</summary>
        public static bool TrySize(byte[] b, out int w, out int h)
        {
            w = h = 0;
            if (b == null || b.Length < 24) return false;
            if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
            {
                w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
                h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
                return w > 0 && h > 0;
            }
            if (b[0] == 0xFF && b[1] == 0xD8)
            {
                int i = 2;
                while (i + 9 < b.Length)
                {
                    if (b[i] != 0xFF) { i++; continue; }
                    byte m = b[i + 1];
                    if (m == 0xD8 || m == 0x01 || (m >= 0xD0 && m <= 0xD7)) { i += 2; continue; }
                    int len = (b[i + 2] << 8) | b[i + 3];
                    bool sof = m >= 0xC0 && m <= 0xCF && m != 0xC4 && m != 0xC8 && m != 0xCC;
                    if (sof)
                    {
                        h = (b[i + 5] << 8) | b[i + 6];
                        w = (b[i + 7] << 8) | b[i + 8];
                        return w > 0 && h > 0;
                    }
                    i += 2 + len;
                }
            }
            return false;
        }
    }
}

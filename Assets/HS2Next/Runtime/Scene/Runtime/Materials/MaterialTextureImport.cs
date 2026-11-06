// An image file taken into a material edit. An edit's image lives in the store of the document, so a file is always embedded, wherever
// it comes from and whatever the image options say, and a saved file stands alone. The checks run in this order, and a refusal says
// why without the file's path, which nothing here keeps:
//   the path    absolute, relative to UserData, or hs2: + a path relative to the HS2 root; the file exists
//   the length  at most MaxBytes
//   the kind    a PNG or a JPEG by its first bytes; an animated PNG is refused
//   the header  readable, each side from 1 to MaxSide
//   the body    it decodes (MaterialTextureImport.Decode)
// StudioOps.ImportMaterialTexture and the edits of a command's `file` go through here; a decoded image stays held until the edit has
// applied it, so the apply finds it in the cache and the file decodes once.
using System;
using System.IO;
using HS2Next.Chara.Ext;
using HS2Next.Save;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>What an imported image may be. A card's or a file's image is not held to these; it is kept and applied as read.</summary>
    public static class MaterialTextureLimits
    {
        /// <summary>The longest file.</summary>
        public const long MaxBytes = 64L << 20;
        /// <summary>The longest side in pixels.</summary>
        public const int MaxSide = 8192;
    }

    public static class MaterialTextureImport
    {
        /// <summary>The file extensions a command's path may have.</summary>
        public static readonly string[] Extensions = { ".png", ".jpg", ".jpeg" };

        /// <summary>The name of an image decoded here (no file name: it would be shown by the material reads).</summary>
        const string TextureName = "me tex import";

        /// <summary>The bytes of the file <paramref name="picked"/> names, through the checks up to the header; null with
        /// <paramref name="why"/> otherwise.</summary>
        public static byte[] Read(string picked, FileRefContext ctx, out string why)
        {
            string full = FileRefs.Resolve(picked, ctx, out string resolveError);
            if (full == null) { why = "the path cannot be used"; return null; }
            byte[] bytes;
            try
            {
                var file = new FileInfo(full);
                if (!file.Exists) { why = "the file does not exist"; return null; }
                if (file.Length > MaterialTextureLimits.MaxBytes) { why = "the file is larger than " + (MaterialTextureLimits.MaxBytes >> 20) + " MiB"; return null; }
                bytes = File.ReadAllBytes(full);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                why = "the file cannot be read";
                return null;
            }
            why = Check(bytes);
            return why == null ? bytes : null;
        }

        /// <summary>What refuses these bytes as an image of an edit, before decoding; null when they pass.</summary>
        public static string Check(byte[] bytes)
        {
            if (bytes == null || bytes.LongLength > MaterialTextureLimits.MaxBytes) return "the file is larger than " + (MaterialTextureLimits.MaxBytes >> 20) + " MiB";
            var type = ResStore.Sniff(bytes);
            if (type == ResMediaType.Other) return "the file is neither a PNG nor a JPEG";
            if (type == ResMediaType.Png && Animated(bytes)) return "an animated PNG cannot be used";
            if (!ExtTextures.TrySize(bytes, out int w, out int h)) return "the image's header cannot be read";
            if (w > MaterialTextureLimits.MaxSide || h > MaterialTextureLimits.MaxSide) return "the image is larger than " + MaterialTextureLimits.MaxSide + " pixels on a side";
            return null;
        }

        /// <summary>Decodes <paramref name="bytes"/>. On success the cache holds one reference under the returned key, for
        /// <see cref="ExtTextures.Release"/>; null with <paramref name="why"/> when the body does not decode.</summary>
        public static string Decode(byte[] bytes, bool linear, out string why)
        {
            var tex = ExtTextures.Acquire(bytes, linear, true, TextureName, out string key);
            why = tex ? null : "the image cannot be decoded";
            return tex ? key : null;
        }

        // A PNG is animated when a chunk `acTL` comes before the first `IDAT`.
        static bool Animated(byte[] b)
        {
            long i = 8;
            while (i + 8 <= b.LongLength)
            {
                long length = ((long)b[i] << 24) | ((long)b[i + 1] << 16) | ((long)b[i + 2] << 8) | b[i + 3];
                if (b[i + 4] == 'I' && b[i + 5] == 'D' && b[i + 6] == 'A' && b[i + 7] == 'T') return false;
                if (b[i + 4] == 'a' && b[i + 5] == 'c' && b[i + 6] == 'T' && b[i + 7] == 'L') return true;
                i += 12 + length;
            }
            return false;
        }
    }
}

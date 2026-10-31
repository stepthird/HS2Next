// Pattern and panel texture sources of Studio items.
//
//   pattern `item` = "guid#id" (list category 348 st_pattern): the catalog row; ThumbAB non-empty → Texture2D
//              <ThumbTex without "thumb_"> from bundle <ThumbAB without "thumb/"> through the catalog (texture guard;
//              shared, never destroyed here); ThumbAB empty → the HS2 install's UserData/pattern/<ThumbTex> (read only)
//   `file`   = {res}  the PNG / JPEG in the scene container's res/ store
//              {hs2}  a file of the HS2 install (e.g. UserData/pattern/x.png, DefaultData/bg/x.png), read only
//              {user} a file of HS2Next's UserData (e.g. pattern/x.png, bg/x.png)
//   relative paths are guarded: no rooted path, drive, "..", ':' or empty segment.
// Decoded images are owned by a process-wide cache keyed by the source (res entry name, which carries the content hash;
// or the resolved absolute path and its write time) and reference-counted: each realization releases the keys it took
// (Release) when the texture is replaced and when its content is released; the last release destroys the texture.
using System;
using System.Collections.Generic;
using System.IO;
using HS2Next.Content;
using HS2Next.Save;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    public static class ItemTextures
    {
        /// <summary>List category of the Studio patterns (st_pattern).</summary>
        public const int PatternCategory = 348;

        /// <summary>One resolved texture: <see cref="Key"/> non-null = a cache reference the caller must <see cref="Release"/>.</summary>
        public struct Handle
        {
            public Texture Texture;
            /// <summary>Cache key of an owned decoded image, else null (bundle texture or none).</summary>
            public string Key;
            /// <summary>Why the source could not be resolved, else null (none is not an error).</summary>
            public string Error;
            /// <summary>Where the texture came from (for reports): "bundle:<path>/<asset>", "res:<name>", "file:<path>", or null.</summary>
            public string Source;
            public bool IsNone => Texture == null && Error == null;
        }

        sealed class Entry
        {
            public Texture2D Texture;
            public int Refs;
        }

        static readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>(StringComparer.Ordinal);

        /// <summary>Decoded images alive in the cache (tests: back to the baseline after releases).</summary>
        public static int CachedCount => cache.Count;

        /// <summary>Sum of the references of every cached image.</summary>
        public static int ReferenceCount
        {
            get
            {
                int n = 0;
                foreach (var e in cache.Values) n += e.Refs;
                return n;
            }
        }

        // ------------------------------------------------------------ sources

        /// <summary>The texture of a stored pattern: `item` first, else `file`, else none.</summary>
        public static Handle Pattern(RuntimeServices services, ResStore res, ItemPattern p)
        {
            if (p == null) return default;
            if (p.Item != null) return PatternRow(services, p.Item);
            if (p.File != null) return File(services, res, p.File);
            return default;
        }

        /// <summary>The texture of an st_pattern row "guid#id".</summary>
        public static Handle PatternRow(RuntimeServices services, string item)
        {
            if (!ContentId.TryParse(item, out var id) || id.IsEmpty) return new Handle { Error = "bad pattern reference \"" + item + "\"" };
            var cat = services?.Catalog;
            if (cat == null) return new Handle { Error = "no content catalog" };
            if (!cat.TryGetEntry(PatternCategory, id, out var e) || e == null) return new Handle { Error = "no st_pattern row " + id };
            string ab = e["ThumbAB"], tex = e["ThumbTex"];
            if (string.IsNullOrEmpty(tex) || tex == "0") return new Handle { Error = "st_pattern row " + id + " has no ThumbTex" };
            if (!string.IsNullOrEmpty(ab) && ab != "0")
            {
                string bundle = ab.Replace("thumb/", ""), asset = tex.Replace("thumb_", "");
                Texture2D t = null;
                try { t = cat.LoadAssetAt<Texture2D>(e, bundle, asset); }
                catch (Exception x) { return new Handle { Error = "st_pattern row " + id + ": " + x.GetType().Name + ": " + x.Message }; }
                return t ? new Handle { Texture = t, Source = "bundle:" + bundle + "/" + asset }
                         : new Handle { Error = "st_pattern row " + id + ": " + asset + " not found in " + bundle };
            }
            // a user pattern row (no bundle): the HS2 install's UserData/pattern/<ThumbTex>
            return Hs2File(services, "UserData/pattern/" + tex);
        }

        /// <summary>A file reference (res / hs2 / user).</summary>
        public static Handle File(RuntimeServices services, ResStore res, FileRef f)
        {
            if (f == null) return default;
            switch (f.Kind)
            {
                case FileRef.Kinds.Res:
                {
                    var bytes = res?.Get(f.Value);
                    if (bytes == null) return new Handle { Error = "res entry " + f.Value + " is not in the scene" };
                    return Decoded("res:" + f.Value, bytes, "res:" + f.Value);
                }
                case FileRef.Kinds.Hs2: return Hs2File(services, f.Value);
                case FileRef.Kinds.User:
                {
                    string root = services?.UserData;
                    if (string.IsNullOrEmpty(root)) return new Handle { Error = "no HS2Next UserData folder for " + f.Value };
                    return PathFile(root, f.Value);
                }
                default: return new Handle { Error = "file reference of an unknown kind" };
            }
        }

        static Handle Hs2File(RuntimeServices services, string rel)
        {
            string root = services?.Hs2Root;
            if (string.IsNullOrEmpty(root)) return new Handle { Error = "no HS2 install for " + rel };
            return PathFile(root, rel);
        }

        static Handle PathFile(string root, string rel)
        {
            if (!SafeRelative(rel)) return new Handle { Error = "file reference \"" + rel + "\" is not a plain relative path" };
            string full;
            try { full = Path.GetFullPath(Path.Combine(root, rel)); }
            catch (Exception e) { return new Handle { Error = "file reference \"" + rel + "\": " + e.Message }; }
            if (!System.IO.File.Exists(full)) return new Handle { Error = "file not found: " + rel };
            byte[] bytes;
            DateTime stamp;
            try
            {
                stamp = System.IO.File.GetLastWriteTimeUtc(full);
                string key = "file:" + full.ToLowerInvariant() + "|" + stamp.Ticks;
                if (cache.TryGetValue(key, out var hit)) { hit.Refs++; return new Handle { Texture = hit.Texture, Key = key, Source = "file:" + rel }; }
                bytes = System.IO.File.ReadAllBytes(full);
                return Decoded(key, bytes, "file:" + rel);
            }
            catch (Exception e) { return new Handle { Error = "file " + rel + ": " + e.GetType().Name + ": " + e.Message }; }
        }

        /// <summary>A relative path without root, drive, "..", ':' or empty segments ('/' or '\' separators).</summary>
        public static bool SafeRelative(string rel)
        {
            if (string.IsNullOrWhiteSpace(rel)) return false;
            if (rel.IndexOf(':') >= 0 || rel.StartsWith("/", StringComparison.Ordinal) || rel.StartsWith("\\", StringComparison.Ordinal)) return false;
            foreach (var seg in rel.Split('/', '\\'))
                if (seg.Length == 0 || seg == "." || seg == "..") return false;
            return rel.IndexOfAny(Path.GetInvalidPathChars()) < 0;
        }

        // ------------------------------------------------------------ cache

        static Handle Decoded(string key, byte[] bytes, string source)
        {
            if (cache.TryGetValue(key, out var hit)) { hit.Refs++; return new Handle { Texture = hit.Texture, Key = key, Source = source }; }
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = Path.GetFileName(source.Substring(source.IndexOf(':') + 1)) };
            bool ok;
            try { ok = t.LoadImage(bytes, false); }
            catch (Exception) { ok = false; }
            if (!ok)
            {
                Object.DestroyImmediate(t);
                return new Handle { Error = source + " is not a readable PNG / JPEG" };
            }
            t.hideFlags = HideFlags.DontSave;
            cache[key] = new Entry { Texture = t, Refs = 1 };
            return new Handle { Texture = t, Key = key, Source = source };
        }

        /// <summary>Drops one reference of <paramref name="key"/> (null is ignored); the last one destroys the texture.</summary>
        public static void Release(string key)
        {
            if (key == null || !cache.TryGetValue(key, out var e)) return;
            if (--e.Refs > 0) return;
            cache.Remove(key);
            if (e.Texture) Object.DestroyImmediate(e.Texture);
        }

        /// <summary>Releases the handle's reference and clears it.</summary>
        public static void Release(ref Handle h)
        {
            Release(h.Key);
            h = default;
        }
    }
}

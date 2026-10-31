// Sideloader's loose PNG textures (Phase 4.2 package L, gap GB10).
// A zipmod list row may name a bundle the zipmod does not ship and keep the texture as a file
// abdata/<bundle path without .unity3d>/<asset>.png instead. HS2's Sideloader answers every texture request with such a file
// when it exists (also when the bundle exists and holds an asset of that name), and makes a bundle path whose folder holds
// loose PNGs "exist" when no source ships the bundle. ContentCatalog consults this class first on every texture load
// (LoadAssetAt, LoadAsset, LoadAssetFrom), marks such bundle paths in OpenBundleAsync (BundleOpen.Loose), and the Maker
// thumbnails load loose thumbnails asynchronously through LoadAsync.
// Rules (behaviour of Sideloader, the spec section has the evidence):
//   - the files: every zip entry "abdata/....png" (lower-case prefix and extension) of the zipmods in
//     ContentCatalog.ZipmodsByGuid, keyed by its exact path (case-sensitive); a path shipped by several zipmods belongs to
//     the first in GUID load order (StudioCsv.LoadOrder);
//   - the lookup of (bundle B, asset A): B with every ".unity3d" removed (any case) + "/" + A + ".png", exact; the row's
//     manifest plays no part; requests of type Texture or Texture2D get the texture (another Texture subclass gets null, as
//     the game's cast of Sideloader's Texture2D fails), other types never look;
//   - the texture: new Texture2D(2, 2, RGBA32, mip chain) (sRGB) filled by ImageConversion.LoadImage (mips regenerated, size
//     and format from the file); wrap Clamp when the path "abdata/..." contains "clamp", Repeat when it contains "repeat"
//     (ordinal), else the default (Repeat); filter and aniso defaults; no name. Sideloader's copy stays readable; HS2Next's
//     drops the CPU copy after the upload (nothing in the app reads these textures on the CPU: the thumbnail disk cache
//     copies them on the GPU), which halves their memory and changes nothing visible (KeepReadable keeps it for tests).
// HS2Next keeps one texture per (zipmod, path) (Sideloader makes a new copy per request): a synchronous load pins it until
// ReleaseAll (ContentCatalog.UnloadAll), as a bundle keeps the assets a build loaded; LoadAsync callers release what they
// took (Release). The texture guard is not involved: a decoded PNG has a full mip chain built by LoadImage. Main thread only
// (LoadAsync reads the zip entry on a worker, the decode runs in Request.Finish on the main thread).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HS2Next.Content.Studio;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HS2Next.Content
{
    public sealed class LooseTextures
    {
        /// <summary>One loose PNG: the zipmod that ships it and its abdata-relative path (exact case, ".png" included).</summary>
        public readonly struct Hit
        {
            public readonly ZipmodInfo Zipmod;
            public readonly string Path;
            public Hit(ZipmodInfo zipmod, string path) { Zipmod = zipmod; Path = path; }
            public bool IsEmpty => Zipmod == null;
            /// <summary>The cache key: zipmod file + "!" + path.</summary>
            public string Key => Zipmod == null ? null : Zipmod.Path + "!" + Path;
            /// <summary>The wrap rule of Sideloader (on the path as it names the zip entry, "abdata/" included).</summary>
            public TextureWrapMode? Wrap => WrapOf(Path);
            public override string ToString() => Zipmod == null ? "(none)" : (Zipmod.Guid + ":abdata/" + Path);
        }

        sealed class Entry
        {
            public Hit Hit;
            public Texture2D Texture;
            public int Refs;        // LoadAsync / Acquire takes not yet released
            public bool Pinned;     // handed out by a synchronous load: lives until ReleaseAll
        }

        readonly ContentCatalog catalog;
        Dictionary<string, ZipmodInfo> index;   // exact abdata-relative path -> zipmod
        HashSet<string> folders;                // folders holding a loose PNG ("chara/stn")
        readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>(StringComparer.Ordinal);
        readonly Dictionary<Texture2D, Entry> byTexture = new Dictionary<Texture2D, Entry>();

        internal LooseTextures(ContentCatalog catalog) { this.catalog = catalog; }

        // ---------- statistics (tests, leak rows) ----------

        /// <summary>Textures alive in the cache (pinned or taken).</summary>
        public int Live => cache.Count;
        /// <summary>Of <see cref="Live"/>: pinned by a synchronous load.</summary>
        public int Pinned => cache.Values.Count(e => e.Pinned);
        /// <summary>Takes (LoadAsync / Acquire) not yet released.</summary>
        public int Taken => cache.Values.Sum(e => e.Refs);
        /// <summary>Textures decoded, destroyed, and loads answered from the cache, since the catalog was made.</summary>
        public int Created { get; private set; }
        public int Destroyed { get; private set; }
        public int CacheHits { get; private set; }
        /// <summary>Main-thread milliseconds spent in LoadImage (all decodes).</summary>
        public double DecodeMs { get; private set; }
        /// <summary>Loose PNG files known (the index) and the folders they are in.</summary>
        public int Files { get { Build(); return index.Count; } }
        public int FolderCount { get { Build(); return folders.Count; } }

        // ---------- index ----------

        /// <summary>Forgets the index (the zipmods changed); cached textures stay valid (their key names the zipmod file).</summary>
        internal void Reindex() { index = null; folders = null; }

        void Build()
        {
            if (index != null) return;
            index = new Dictionary<string, ZipmodInfo>(StringComparer.Ordinal);
            folders = new HashSet<string>(StringComparer.Ordinal);
            foreach (var z in catalog.ZipmodsByGuid.Values.OrderBy(z => z.Guid, StudioCsv.LoadOrder))
                foreach (var p in z.BundlePaths)
                {
                    if (!p.EndsWith(".png", StringComparison.Ordinal) || index.ContainsKey(p)) continue;
                    index[p] = z;
                    int slash = p.LastIndexOf('/');
                    if (slash > 0) folders.Add(p.Substring(0, slash));
                }
        }

        /// <summary>
        /// The loose PNG path of a request for <paramref name="asset"/> from <paramref name="bundlePath"/>: the bundle path with
        /// every ".unity3d" removed (any case), "/", the asset, ".png" (abdata-relative). Null for an empty / "0" argument.
        /// </summary>
        public static string PathOf(string bundlePath, string asset)
        {
            if (string.IsNullOrEmpty(bundlePath) || bundlePath == "0" || string.IsNullOrEmpty(asset) || asset == "0") return null;
            var sb = new StringBuilder(bundlePath.Length + asset.Length + 5);
            int i = 0;
            while (true)
            {
                int j = bundlePath.IndexOf(".unity3d", i, StringComparison.OrdinalIgnoreCase);
                if (j < 0) { sb.Append(bundlePath, i, bundlePath.Length - i); break; }
                sb.Append(bundlePath, i, j - i);
                i = j + 8;
            }
            return sb.Append('/').Append(asset).Append(".png").ToString();
        }

        /// <summary>Sideloader's wrap rule on an abdata-relative path: Clamp when "abdata/" + path contains "clamp", Repeat when
        /// it contains "repeat" (ordinal), else null (the texture default, Repeat).</summary>
        public static TextureWrapMode? WrapOf(string path)
        {
            if (path == null) return null;
            string full = "abdata/" + path;
            if (full.Contains("clamp")) return TextureWrapMode.Clamp;
            if (full.Contains("repeat")) return TextureWrapMode.Repeat;
            return null;
        }

        /// <summary>True when a request of type <typeparamref name="T"/> consults the loose PNGs (Texture and its subclasses).</summary>
        public static bool Consults<T>() => typeof(Texture).IsAssignableFrom(typeof(T));

        /// <summary>The loose PNG answering a texture request for <paramref name="asset"/> from <paramref name="bundlePath"/>.</summary>
        public bool TryFind(string bundlePath, string asset, out Hit hit)
        {
            hit = default;
            string p = PathOf(bundlePath, asset);
            if (p == null) return false;
            Build();
            if (!index.TryGetValue(p, out var z)) return false;
            hit = new Hit(z, p);
            return true;
        }

        /// <summary>As <see cref="TryFind(string, string, out Hit)"/> for the row's columns entry[bundleKey] / entry[assetKey].</summary>
        public bool TryFind(ListEntry e, string bundleKey, string assetKey, out Hit hit)
        {
            hit = default;
            return e != null && TryFind(e[bundleKey], e[assetKey], out hit);
        }

        /// <summary>True when the folder of <paramref name="bundlePath"/> (the path without its extension) holds a loose PNG:
        /// with no bundle shipped there, Sideloader treats the bundle as existing (an empty bundle).</summary>
        public bool HasFolder(string bundlePath)
        {
            if (string.IsNullOrEmpty(bundlePath) || bundlePath == "0") return false;
            Build();
            int dot = bundlePath.LastIndexOf('.');
            return folders.Contains(dot >= 0 ? bundlePath.Substring(0, dot) : bundlePath);
        }

        // ---------- loads ----------

        /// <summary>
        /// The texture of a synchronous load (ContentCatalog's texture loads): decoded on first use, then shared; pinned until
        /// <see cref="ReleaseAll"/>. Null when the zip entry cannot be read.
        /// </summary>
        public Texture2D Load(Hit hit)
        {
            var e = Get(hit, null);
            if (e == null) return null;
            e.Pinned = true;
            return e.Texture;
        }

        /// <summary>A synchronous take that the caller gives back with <see cref="Release"/> (tests; what LoadAsync's Finish does).</summary>
        public Texture2D Acquire(Hit hit)
        {
            var e = Get(hit, null);
            if (e == null) return null;
            e.Refs++;
            return e.Texture;
        }

        /// <summary>
        /// An asynchronous take: the zip entry is read on a worker; <see cref="Request.Finish"/> (main thread, once
        /// <see cref="Request.Ready"/>) decodes it (or takes the cached texture) — the caller releases the texture with
        /// <see cref="Release"/>.
        /// </summary>
        public Request LoadAsync(Hit hit)
        {
            var r = new Request(this, hit);
            if (hit.IsEmpty) { r.Complete(null); return r; }
            if (!cache.ContainsKey(hit.Key))
            {
                var idx = hit.Zipmod.Index;   // the central directory, read here (main thread) once per zipmod
                // thread-pool work (background threads: nothing waits for it at exit); a cancelled request reads nothing
                if (idx.Entries.TryGetValue("abdata/" + hit.Path, out var ze)) r.Read = Task.Run(() => r.Cancelled ? null : idx.ReadAll(ze));
                else r.Complete(null);
            }
            return r;
        }

        /// <summary>"loose|&lt;zipmod file&gt;|&lt;size&gt;|&lt;mtime ticks&gt;|&lt;path&gt;": the source stamp of a loose PNG, for caches keyed by
        /// source content (the Maker's disk thumbnail cache); null for an empty hit or a missing file.</summary>
        public string StampOf(Hit hit)
        {
            if (hit.IsEmpty) return null;
            if (!stamps.TryGetValue(hit.Zipmod.Path, out var st))
            {
                var fi = new System.IO.FileInfo(hit.Zipmod.Path);
                stamps[hit.Zipmod.Path] = st = fi.Exists ? hit.Zipmod.Path + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks : null;
            }
            return st == null ? null : "loose|" + st + "|" + hit.Path;
        }
        readonly Dictionary<string, string> stamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Gives back a take of <paramref name="texture"/> (LoadAsync / Acquire); the texture is destroyed when no take
        /// is left and no synchronous load pinned it. False when the texture is not a loose texture of this cache.</summary>
        public bool Release(Texture2D texture)
        {
            if (texture == null || !byTexture.TryGetValue(texture, out var e)) return false;
            if (e.Refs > 0) e.Refs--;
            if (e.Refs == 0 && !e.Pinned) Drop(e);
            return true;
        }

        /// <summary>The textures alive in the cache (diagnostics, tests).</summary>
        public IReadOnlyCollection<Texture2D> AliveTextures => byTexture.Keys;

        /// <summary>True when <paramref name="texture"/> is a loose texture of this cache.</summary>
        public bool Owns(Texture texture) => texture is Texture2D t && t != null && byTexture.ContainsKey(t);

        /// <summary>The loose PNG a texture of this cache was decoded from (diagnostics), or an empty hit.</summary>
        public Hit SourceOf(Texture texture) => texture is Texture2D t && t != null && byTexture.TryGetValue(t, out var e) ? e.Hit : default;

        /// <summary>Destroys every cached texture (with ContentCatalog.UnloadAll, when the bundles go).</summary>
        public void ReleaseAll()
        {
            foreach (var e in cache.Values.ToList()) Drop(e);
        }

        void Drop(Entry e)
        {
            cache.Remove(e.Hit.Key);
            if (e.Texture != null) byTexture.Remove(e.Texture);
            if (e.Texture) { Object.DestroyImmediate(e.Texture); Destroyed++; }
            e.Texture = null;
        }

        Entry Get(Hit hit, byte[] bytes)
        {
            if (hit.IsEmpty) return null;
            if (cache.TryGetValue(hit.Key, out var e) && e.Texture) { CacheHits++; return e; }
            if (bytes == null)
            {
                var idx = hit.Zipmod.Index;
                if (!idx.Entries.TryGetValue("abdata/" + hit.Path, out var ze)) return null;
                try { bytes = idx.ReadAll(ze); }
                catch (Exception) { return null; }
            }
            var t = Decode(hit.Path, bytes);
            e = new Entry { Hit = hit, Texture = t };
            cache[hit.Key] = e;
            byTexture[t] = e;
            return e;
        }

        /// <summary>Keep the decoded textures' CPU copy (tests that compare decoded data); false (the default) = not readable
        /// after the upload.</summary>
        public static bool KeepReadable;

        /// <summary>The texture Sideloader makes of a loose PNG's bytes (see the file header).</summary>
        public Texture2D Decode(string path, byte[] bytes)
        {
            var sw = Stopwatch.StartNew();
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            ImageConversion.LoadImage(t, bytes, !KeepReadable);
            var wrap = WrapOf(path);
            if (wrap.HasValue) t.wrapMode = wrap.Value;
            DecodeMs += sw.Elapsed.TotalMilliseconds;
            Created++;
            return t;
        }

        /// <summary>An asynchronous take (<see cref="LoadAsync"/>).</summary>
        public sealed class Request
        {
            readonly LooseTextures owner;
            internal Task<byte[]> Read;
            Texture2D texture;

            internal Request(LooseTextures owner, Hit hit) { this.owner = owner; Hit = hit; }

            public Hit Hit { get; }
            /// <summary>True once <see cref="Finish"/> ran (or the entry was missing).</summary>
            public bool IsDone { get; private set; }
            /// <summary>True after <see cref="Cancel"/>: the worker reads nothing (if it has not started) and nothing is taken.</summary>
            public bool Cancelled => cancelled;
            volatile bool cancelled;

            /// <summary>Drops the request (Clear / quit): no texture is taken, the worker's result is discarded.</summary>
            public void Cancel()
            {
                if (IsDone) return;
                cancelled = true;
                Read = null;
                Complete(null);
            }
            /// <summary>The taken texture (after <see cref="Finish"/>; null when the entry could not be read).</summary>
            public Texture2D Texture => texture;
            /// <summary>True when <see cref="Finish"/> will not wait for the worker.</summary>
            public bool Ready => IsDone || Read == null || Read.IsCompleted;

            internal void Complete(Texture2D t) { texture = t; IsDone = true; }

            /// <summary>Main thread: decodes the bytes (or takes the cached texture) and takes it once; idempotent.</summary>
            public Texture2D Finish()
            {
                if (IsDone) return texture;
                byte[] bytes = null;
                if (Read != null)
                {
                    try { bytes = Read.Result; } catch (Exception) { bytes = null; }
                    Read = null;
                }
                var e = owner.cache.TryGetValue(Hit.Key, out var c) && c.Texture ? c : bytes != null ? owner.Get(Hit, bytes) : owner.Get(Hit, null);
                if (e != null && ReferenceEquals(e, c)) owner.CacheHits++;
                if (e != null) e.Refs++;
                Complete(e?.Texture);
                return texture;
            }
        }
    }
}

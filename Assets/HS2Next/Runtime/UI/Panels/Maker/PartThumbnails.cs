// Part thumbnails of the Maker lists (C12): a cell asks with Request(category, key, owner) on
// bind and Release on unbind (owners pin what they show). A row's thumbnail is its ThumbAB / ThumbTex texture, loaded with
// ContentCatalog.LoadAssetAsync (at most 8 in flight, completions accepted within a per-frame budget, 2 ms by default) and
// used as loaded (DXT stays DXT: no decode, no copy); textures no cell shows leave a byte-counted LRU (the 3.5
// ThumbnailLoader policy, 40 MB for part thumbnails); a bundle the catalog opened only for thumbnails is unloaded when its
// last thumbnail has left the LRU. Rows without a thumbnail, or whose texture cannot be loaded, get the error tile;
// cells show the placeholder while loading (ThumbnailLoader.Placeholder / ErrorPlaceholder).
// Disk cache (local only, never distributed): a 128² PNG per (category, ContentId, source stamp) under Cache/thumbs/,
// written by a worker from a GPU read-back of the loaded texture on first view (not without a graphics device); later
// sessions read the PNG through the 3.5 file thumbnail path (a ThumbnailLoader) and open no bundle. Main thread only.
// 4.1 (F14): the texture column is part of the key (the face preset grid shows
// the texture its head row's `Preset` column names in ThumbAB); before a bundle load the texture guard's records are
// consulted — a (source, texture) listed in Cache/texguard/bad_textures.tsv gets the error tile without a load — and a
// texture whose asynchronous load completes with mipmapCount < 1 is not shown (error tile) and is recorded in the same list;
// bundles of the repair index open from their repaired copies (ContentCatalog.OpenBundleAsync). The pre-upload check of
// unknown textures stays 5.2. Shared(catalog) is the instance the Maker pages use (ticked by their lists).
// 4.2 (GB10): a thumbnail that is a Sideloader loose PNG (3,410 rows here)
// is read on a worker through ContentCatalog.Loose (LooseTextures.LoadAsync), decoded when accepted (within the budget),
// given back to the catalog's loose cache when it leaves the LRU, and written to the disk cache like a bundle thumbnail (key:
// the stamp of the zipmod that ships the PNG, LooseTextures.StampOf), so later sessions read the 128² PNG instead.
// Every instance is cleared at play-mode exit / app quit (ReleaseAllForQuit, PartThumbnailsQuitHook): asynchronous asset loads
// and bundle opens are completed, the disk loader's UnityWebRequests disposed and loose reads cancelled before Unity shuts down.
// v0.3 trial 2 (the head-type list crash): before a bundle texture is loaded, its header is read from the bundle's bytes on a
// worker (Content/TextureHeaders.cs, cached per bundle) and a texture Unity 6 cannot upload (no mip, a block-compressed size
// that is not a multiple of four, ...) gets the error tile without a load; it is logged once and recorded in the texture
// guard's list, so Tools/texguard_repair.py repairs it. A malformed bundle (its node table points outside its data: Unity opens
// it but cannot read its streamed image data) is refused the same way; a bundle the reader cannot parse otherwise loads as
// before (counted, logged once; the census covers it). A bundle with a header read or an asset load of it in flight is never
// unloaded. Diagnostics on the command line: -thumbHeaderCheck off (the check off, for before / after runs), -thumbDiskCache
// off (every thumbnail from its bundle, nothing written), -thumbLogLoads on (every load and bundle unload logged).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using HS2Next.Content;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace HS2Next.UI.Panels.Maker
{
    public sealed class PartThumbnails : IDisposable
    {
        public const long DefaultCapBytes = 40L * 1024 * 1024;
        public const int DiskSize = 128;

        sealed class Slot
        {
            public int Category;
            public ContentId Key;
            public string Column = ThumbColumn;
            public Texture2D Texture;        // bundle texture or the disk PNG (owned by the disk loader)
            public AssetBundle Bundle;       // the bundle the texture came from (null for disk PNGs)
            public long Bytes;
            public bool Failed, Queued;
            public string DiskPath;          // the PNG of the current stamp (null: no disk cache)
            public bool FromDisk;
            public AssetBundleRequest Request;
            public ContentCatalog.BundleOpen Opening;    // the bundle is being opened asynchronously
            public LooseTextures.Request Loose;          // 4.2 GB10: a Sideloader loose PNG being read (decoded on accept)
            public bool LooseTexture;                    // Texture is a loose PNG texture (released to the catalog on drop)
            public Task<BundleTextureHeaders> Header;    // the bundle's texture headers being read (before the load)
            public BundleFileRef HeaderFile;             // the file they are read from
            public Stopwatch Latency;
            public readonly HashSet<object> Owners = new HashSet<object>();
            public LinkedListNode<Slot> Lru;
        }

        readonly ContentCatalog catalog;
        readonly Dictionary<(int, ContentId, string), Slot> slots = new Dictionary<(int, ContentId, string), Slot>();
        readonly Dictionary<string, Slot> byDiskPath = new Dictionary<string, Slot>(StringComparer.OrdinalIgnoreCase);
        readonly LinkedList<Slot> lru = new LinkedList<Slot>();
        readonly Queue<Slot> waiting = new Queue<Slot>();
        readonly List<Slot> inFlight = new List<Slot>();
        readonly List<Slot> opening = new List<Slot>();
        readonly List<Slot> checking = new List<Slot>();
        readonly Dictionary<AssetBundle, int> textureCount = new Dictionary<AssetBundle, int>();
        readonly ThumbnailLoader disk;
        readonly object diskLock = new object();
        int diskWritesPending;

        /// <summary>The thumbnail cache folder (Cache/thumbs), or null: no disk cache.</summary>
        public string CacheDir { get; }
        /// <summary>Write PNGs on first view (needs a graphics device; off without one).</summary>
        public bool WriteDisk = true;
        /// <summary>Read PNGs written earlier.</summary>
        public bool ReadDisk = true;
        public int MaxInFlight = 8;
        public long CapBytes = DefaultCapBytes;

        /// <summary>The default texture column of a row's thumbnail; the face preset grid uses <see cref="PresetColumn"/>.</summary>
        public const string ThumbColumn = "ThumbTex", PresetColumn = "Preset";

        /// <summary>A thumbnail is ready (texture) or failed (null) for (category, key) of the ThumbTex column.</summary>
        public event Action<int, ContentId, Texture2D> Loaded;
        /// <summary>A thumbnail is ready (texture) or failed (null) for (category, key, column) — every column.</summary>
        public event Action<int, ContentId, string, Texture2D> LoadedColumn;
        /// <summary>The handlers of <see cref="LoadedColumn"/> (tests: a list dropped from the panel leaves none behind).</summary>
        public int LoadedColumnListeners => LoadedColumn?.GetInvocationList().Length ?? 0;

        /// <summary>Diagnostics: log every asynchronous thumbnail load (source / texture) before it starts and every bundle unload (<c>-thumbLogLoads on</c>).</summary>
        public static bool LogLoads = ArgIs("-thumbLogLoads", "on") || HS2Next.Content.ContentCatalog.LogBundles;
        /// <summary>Read a bundle texture's header before its load and refuse one Unity 6 cannot upload (default on;
        /// <c>-thumbHeaderCheck off</c> on the command line turns it off for a before / after comparison).</summary>
        public static bool HeaderCheck = !ArgIs("-thumbHeaderCheck", "off");
        /// <summary>New instances read and write the disk cache (<c>-thumbDiskCache off</c>: every thumbnail from its bundle or PNG source).</summary>
        public static bool DiskCacheDefault = !ArgIs("-thumbDiskCache", "off");

        static bool ArgIs(string name, string value)
        {
            try
            {
                var a = Environment.GetCommandLineArgs();
                int i = Array.IndexOf(a, name);
                return i >= 0 && i + 1 < a.Length && string.Equals(a[i + 1], value, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        /// <summary>Thumbnails refused by the texture guard's records (no load) and loads rejected for mipmapCount &lt; 1.</summary>
        public int GuardSkipped { get; private set; }
        public int BadMipRejected { get; private set; }
        /// <summary>Bundle textures refused by their header before a load (v0.3 trial 2), and loads whose header could not be
        /// read (LZMA bundles, unknown layouts: loaded as before).</summary>
        public int HeaderRejected { get; private set; }
        public int HeaderUnchecked { get; private set; }
        /// <summary>Bundle unloads put off because a header read or an asset load of the bundle was in flight.</summary>
        public int UnloadsDeferred { get; private set; }

        // statistics (U7)
        public int FromBundles { get; private set; }
        public int FromDiskCount { get; private set; }
        /// <summary>Thumbnails accepted from Sideloader loose PNGs (Phase 4.2 package L, GB10).</summary>
        public int FromLoose { get; private set; }
        public int FailedCount { get; private set; }
        public int Evicted { get; private set; }
        public int BundlesUnloaded { get; private set; }
        public int DiskWritten { get; private set; }
        public int DiskWritesPending { get { lock (diskLock) return diskWritesPending; } }
        public long CurrentBytes { get; private set; }
        public long PeakBytes { get; private set; }
        public readonly List<double> FirstViewMs = new List<double>();
        public readonly List<double> AcceptMs = new List<double>();
        /// <summary>Main-thread time of each request start (opening the bundle is synchronous) and the slowest start.</summary>
        public readonly List<double> StartMs = new List<double>();
        public string SlowestStart { get; private set; }
        /// <summary>Why rows got the error tile: "no columns", "no bundle", "no asset", "load threw", "png unreadable".</summary>
        public readonly Dictionary<string, int> FailReasons = new Dictionary<string, int>(StringComparer.Ordinal);
        public int Pending => waiting.Count + inFlight.Count + opening.Count + checking.Count + disk.Pending;
        /// <summary>Header reads in flight (before the asset load).</summary>
        public int HeaderChecksInFlight => checking.Count;
        /// <summary>UnityWebRequests of the disk loader in flight, and loose reads in flight (4.2; quit checks).</summary>
        public int DiskInFlight => disk.InFlight;
        public int LooseInFlight { get { int n = 0; foreach (var s in inFlight) if (s.Loose != null) n++; return n; } }
        /// <summary>Asynchronous asset loads (AssetBundleRequest) and bundle opens in flight (4.2; quit checks).</summary>
        public int AssetLoadsInFlight { get { int n = 0; foreach (var s in inFlight) if (s.Request != null) n++; return n; } }
        public int OpensInFlight => opening.Count;
        public int Count => lru.Count;

        public PartThumbnails(ContentCatalog catalog, string cacheDir)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            CacheDir = string.IsNullOrEmpty(cacheDir) ? null : cacheDir;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supportsAsyncGPUReadback) WriteDisk = false;
            if (!DiskCacheDefault) { ReadDisk = false; WriteDisk = false; }
            disk = new ThumbnailLoader { CapBytes = DefaultCapBytes, ShrinkAbove = int.MaxValue };
            disk.Loaded += OnDiskLoaded;
            Register();   // 4.2: cleared at play-mode exit / app quit
        }

        /// <summary>The thumbnail folder of a catalog's cache directory: &lt;cache&gt;/thumbs.</summary>
        public static string CacheDirFor(ContentCatalog c) => c?.CacheDir == null ? null : Path.Combine(c.CacheDir, "thumbs");

        /// <summary>The loaded thumbnail of (category, key, column), or null (touches the LRU).</summary>
        public Texture2D TryGet(int category, ContentId key, string column = ThumbColumn)
        {
            if (!slots.TryGetValue((category, key, column ?? ThumbColumn), out var s) || s.Texture == null) return null;
            Touch(s);
            return s.Texture;
        }

        /// <summary>True when the row has no thumbnail or it could not be loaded (show the error tile).</summary>
        public bool IsFailed(int category, ContentId key, string column = ThumbColumn) => slots.TryGetValue((category, key, column ?? ThumbColumn), out var s) && s.Failed;

        /// <summary>A cell (<paramref name="owner"/>) shows (category, key); returns the texture when it is already loaded.</summary>
        public Texture2D Request(int category, ContentId key, object owner, string column = ThumbColumn)
        {
            if (key.IsEmpty || stopped) return null;
            column ??= ThumbColumn;
            if (!slots.TryGetValue((category, key, column), out var s)) slots[(category, key, column)] = s = new Slot { Category = category, Key = key, Column = column };
            if (owner != null) s.Owners.Add(owner);
            if (s.Texture != null) { Touch(s); return s.Texture; }
            if (!s.Failed && !s.Queued && s.Request == null && s.Latency == null)
            {
                s.Queued = true;
                s.Latency = Stopwatch.StartNew();
                waiting.Enqueue(s);
            }
            return null;
        }

        /// <summary>The cell no longer shows (category, key).</summary>
        public void Release(int category, ContentId key, object owner, string column = ThumbColumn)
        {
            if (slots.TryGetValue((category, key, column ?? ThumbColumn), out var s)) s.Owners.Remove(owner);
        }

        void Touch(Slot s)
        {
            if (s.Lru == null) return;
            lru.Remove(s.Lru);
            lru.AddFirst(s.Lru);
        }

        /// <summary>Starts requests and accepts finished ones within <paramref name="budgetMs"/> of main-thread time; call once per frame.</summary>
        public void Update(double budgetMs = 2)
        {
            if (stopped) return;   // released for the quit: nothing is scheduled any more
            var sw = Stopwatch.StartNew();
            catalog.PumpOpens();
            for (int i = opening.Count - 1; i >= 0; i--)
            {
                var s = opening[i];
                if (!s.Opening.IsDone) continue;
                opening.RemoveAt(i);
                var b = s.Opening.Bundle;
                s.Opening = null;
                if (!b) { Fail(s, "no bundle"); continue; }
                StartAsset(s, catalog.GetEntry(s.Category, s.Key));
            }
            // header reads that finished: refuse the texture, or start its load
            for (int i = checking.Count - 1; i >= 0; i--)
            {
                var s = checking[i];
                if (!s.Header.IsCompleted) continue;
                checking.RemoveAt(i);
                BundleTextureHeaders h;
                try { h = s.Header.Result; } catch (Exception ex) { h = new BundleTextureHeaders { Error = ex.GetType().Name + ": " + ex.Message }; }
                s.Header = null;
                var e = catalog.GetEntry(s.Category, s.Key);
                if (s.Owners.Count == 0 && s.Texture == null) { DropUnstarted(s); continue; }   // nobody shows it any more
                if (HeaderAllows(s, e, h)) StartLoad(s, e);
            }
            while (inFlight.Count + opening.Count < MaxInFlight && waiting.Count > 0 && sw.Elapsed.TotalMilliseconds < budgetMs)
            {
                var s = waiting.Dequeue();
                s.Queued = false;
                if (s.Texture != null || s.Failed || s.Request != null || s.Opening != null || s.Loose != null || s.Header != null) continue;
                if (s.Owners.Count == 0) { s.Latency = null; continue; }   // nobody shows it any more
                Start(s);
            }
            for (int i = inFlight.Count - 1; i >= 0 && sw.Elapsed.TotalMilliseconds < budgetMs; i--)
            {
                var s = inFlight[i];
                if (s.Loose != null)
                {
                    // 4.2 GB10: a loose PNG whose bytes are read: decoded here, within the budget
                    if (!s.Loose.Ready) continue;
                    inFlight.RemoveAt(i);
                    var tl = Stopwatch.StartNew();
                    var lt = s.Loose.Finish();
                    s.Loose = null;
                    if (lt == null) { Fail(s, "loose png unreadable"); continue; }
                    s.LooseTexture = true;
                    Accept(s, lt, false);
                    AcceptMs.Add(tl.Elapsed.TotalMilliseconds);
                    if (WriteDisk && s.DiskPath != null && !File.Exists(s.DiskPath)) WritePng(s.DiskPath, lt);
                    continue;
                }
                if (!s.Request.isDone) continue;
                inFlight.RemoveAt(i);
                var t0 = Stopwatch.StartNew();
                var tex = s.Request.asset as Texture2D;
                s.Request = null;
                if (tex == null)
                {
                    UnloadIfUnused(s.Bundle);
                    s.Bundle = null;
                    Fail(s, "no asset");
                    continue;
                }
                if (tex.mipmapCount < 1)
                {
                    // a 2018 texture with m_MipCount 0 (the guard's bad-mip case): never shown, recorded like a guard incident
                    BadMipRejected++;
                    RecordBad(catalog.GetEntry(s.Category, s.Key), s.Column, tex);
                    s.Bundle = null;
                    Fail(s, "bad mips");
                    continue;
                }
                Accept(s, tex, false);
                AcceptMs.Add(t0.Elapsed.TotalMilliseconds);
                if (WriteDisk && s.DiskPath != null && !File.Exists(s.DiskPath)) WritePng(s.DiskPath, tex);
            }
            disk.Update(Math.Max(0.5, budgetMs - sw.Elapsed.TotalMilliseconds));
        }

        void Start(Slot s)
        {
            var e = catalog.GetEntry(s.Category, s.Key);
            if (e == null || string.IsNullOrEmpty(e["ThumbAB"]) || e["ThumbAB"] == "0" || string.IsNullOrEmpty(e[s.Column]) || e[s.Column] == "0") { Fail(s, "no columns"); return; }
            // 4.2 GB10: a Sideloader loose PNG answers the request (also when the bundle exists); its disk-cache key uses the
            // stamp of the zipmod that ships the PNG (the bundle path's stamp would name another source or none)
            bool loose = catalog.Loose.TryFind(e, "ThumbAB", s.Column, out var hit);
            if (!loose && GuardListed(e, s.Column)) { GuardSkipped++; Fail(s, "guard record"); return; }
            s.DiskPath = loose ? DiskPathOf(s, e, catalog.Loose.StampOf(hit)) : DiskPathOf(s, e);
            if (ReadDisk && s.DiskPath != null && File.Exists(s.DiskPath))
            {
                byDiskPath[s.DiskPath] = s;
                s.FromDisk = true;
                var t = disk.Request(s.DiskPath, s);
                if (t != null) OnDiskLoaded(s.DiskPath, t);
                return;
            }
            StartOpen(s, e);
        }

        // A header read or an asset load of the bundle is in flight: the bundle must stay open.
        bool InFlightFrom(AssetBundle b)
        {
            foreach (var x in inFlight) if (x.Bundle == b) return true;
            foreach (var x in checking) if (x.Bundle == b) return true;
            return false;
        }

        // A thumbnail-only bundle none of whose textures is held and none in flight is unloaded.
        void UnloadIfUnused(AssetBundle b)
        {
            if (!b || textureCount.ContainsKey(b)) return;
            if (InFlightFrom(b)) { UnloadsDeferred++; return; }
            if (releasingForQuit) { BundlesLeftAtQuit++; return; }   // closed with the others by the catalog at the quit
            if (LogLoads) UnityEngine.Debug.Log("[thumbs] unload " + b.name + " (" + catalog.BundleFileOf(b)?.Key + ")");
            if (catalog.UnloadThumbnailBundle(b)) BundlesUnloaded++;
        }

        // A slot that waited for its header while nobody shows it any more: forgotten, to be requested again.
        void DropUnstarted(Slot s)
        {
            var b = s.Bundle;
            s.Bundle = null;
            s.HeaderFile = null;
            s.Latency = null;
            UnloadIfUnused(b);
        }

        void StartOpen(Slot s, ListEntry e)
        {
            // 4.2 GB10: a loose PNG is read on a worker and decoded when accepted (within the budget)
            if (catalog.Loose.TryFind(e, "ThumbAB", s.Column, out var hit)) { s.Loose = catalog.Loose.LoadAsync(hit); inFlight.Add(s); return; }
            var t0 = Stopwatch.StartNew();
            ContentCatalog.BundleOpen open;
            // the copy the per-asset redirect serves the thumbnail texture from (the row's own source unless several ship the path)
            try { open = catalog.OpenBundleAsync(e, e["ThumbAB"], e[s.Column]); }
            catch (Exception) { open = null; }
            Note(t0.Elapsed.TotalMilliseconds, e);
            if (open == null) { Fail(s, "load threw"); return; }
            if (!open.IsDone) { s.Opening = open; opening.Add(s); return; }
            if (!open.Bundle) { Fail(s, "no bundle"); return; }
            StartAsset(s, e);
        }

        // The bundle is open: its texture headers are read (on a worker, once per bundle) before the texture is loaded.
        void StartAsset(Slot s, ListEntry e)
        {
            if (!HeaderCheck) { StartLoad(s, e); return; }
            var t0 = Stopwatch.StartNew();
            AssetBundle b = null;
            BundleFileRef file = null;
            try
            {
                b = e == null ? null : catalog.OpenForAsset(e, "ThumbAB", s.Column, true);
                file = b ? catalog.BundleFileOf(b) : null;
            }
            catch (Exception) { }
            Note(t0.Elapsed.TotalMilliseconds, e);
            if (!b) { Fail(s, "no bundle"); return; }
            s.Bundle = b;
            s.HeaderFile = file;
            if (file == null) { NoteUnchecked(s, e, "the bundle's file is not known (opened from memory)"); StartLoad(s, e); return; }
            if (TextureHeaders.TryCached(file, out var known)) { if (HeaderAllows(s, e, known)) StartLoad(s, e); return; }
            s.Header = Task.Run(() => TextureHeaders.Get(file));
            checking.Add(s);
        }

        // Starts the asynchronous load of the row's texture (the bundle is opened again: a cache hit, or a new copy when the
        // first was unloaded meanwhile).
        void StartLoad(Slot s, ListEntry e)
        {
            AssetBundleRequest req;
            var t0 = Stopwatch.StartNew();
            if (LogLoads && e != null) UnityEngine.Debug.Log("[thumbs] load " + SourceOf(e, s.Column) + " / " + e[s.Column] + " (" + s.Category + " " + s.Key + ")");
            var checkedFile = s.HeaderFile;
            try { req = e == null ? null : catalog.LoadAssetAsync<Texture2D>(e, "ThumbAB", s.Column, true, out s.Bundle); }
            catch (Exception) { req = null; }
            Note(t0.Elapsed.TotalMilliseconds, e);
            s.HeaderFile = null;
            if (req == null) { Fail(s, "no bundle"); return; }
            if (HeaderCheck && checkedFile != null && s.Bundle && catalog.BundleFileOf(s.Bundle) is BundleFileRef now && now.Key != checkedFile.Key)
                UnityEngine.Debug.LogWarning("[thumbs] the bundle changed between the header check and the load: " + checkedFile.Key + " -> " + now.Key);
            s.Request = req;
            inFlight.Add(s);
        }

        // ---------- the header check (v0.3 trial 2) ----------

        static readonly HashSet<string> loggedOnce = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static void LogOnce(string key, string line)
        {
            lock (loggedOnce)
            {
                if (loggedOnce.Count > 4096) loggedOnce.Clear();
                if (!loggedOnce.Add(key)) return;
            }
            UnityEngine.Debug.Log(line);
        }

        void NoteUnchecked(Slot s, ListEntry e, string why)
        {
            HeaderUnchecked++;
            string label = e != null ? SourceOf(e, s.Column) : "?";
            LogOnce("unchecked|" + label, "[thumbs] texture headers not checked before the load: " + label + " (" + why + ")");
        }

        // True when the texture may be loaded; false: refused (error tile, logged once, recorded in the guard's list).
        bool HeaderAllows(Slot s, ListEntry e, BundleTextureHeaders h)
        {
            if (e == null) { Fail(s, "no columns"); return false; }
            string name = e[s.Column];
            TextureHeader th = default;
            string defect;
            if (h != null && h.Malformed)
            {
                // a node table pointing outside the bundle's data: Unity opens it, but the streamed image data cannot be read
                // ("Could not open file archive ... unable to read texture data")
                defect = "malformed bundle (" + h.Error + ")";
                th.Name = name;
            }
            else
            {
                if (h == null || h.Error != null) { NoteUnchecked(s, e, h?.Error ?? "no headers"); return true; }
                if (!h.TryGet(name, out th)) return true;   // not a Texture2D of this bundle: the load finds nothing (error tile)
                defect = TextureHeaders.Defect(th);
                if (defect == null) return true;
            }
            HeaderRejected++;
            string label = SourceOf(e, s.Column);
            LogOnce("refused|" + label + "|" + name, $"[thumbs] texture refused before the upload ({defect}): {label} / {name} {th.Width}x{th.Height} {th.FormatName} mips {th.Mips}");
            HS2Next.Chara.TextureGuard.Record(new HS2Next.Chara.BadTexture
            {
                Source = label, Asset = name, Texture = th.Name ?? name, Width = th.Width, Height = th.Height, Format = th.FormatName, Mips = th.Mips,
                FullChain = TextureHeaders.FullChain(th.Width, th.Height), Reason = "thumbnail: " + defect + " (header check before the upload)",
            }, s.HeaderFile?.Key ?? (e.Zipmod != null ? e.Zipmod.Path + "!" + e["ThumbAB"]?.Replace('\\', '/') : ""));
            var b = s.Bundle;
            s.Bundle = null;
            s.HeaderFile = null;
            UnloadIfUnused(b);
            Fail(s, "texture header");
            return false;
        }

        void Note(double ms, ListEntry e)
        {
            StartMs.Add(ms);
            if (ms > slowest) { slowest = ms; SlowestStart = $"{ms:0.0} ms {e?.Zipmod?.Guid ?? "abdata"}:{e?["ThumbAB"]}"; }
        }
        double slowest = -1;

        // the disk cache key names the file the texture is served from (it changes for redirected rows only)
        string DiskPathOf(Slot s, ListEntry e) => DiskPathOf(s, e, CacheDir == null ? null : catalog.SourceStampOf(e, e["ThumbAB"], e[s.Column]));

        string DiskPathOf(Slot s, ListEntry e, string stamp)
        {
            if (CacheDir == null || stamp == null) return null;
            // the folder of the row's source: its GUID with invalid file-name characters replaced, a long one shortened (keeps the
            // longest cache path well under the Windows path limit under a per-user data root)
            return Path.Combine(CacheDir, s.Category.ToString(System.Globalization.CultureInfo.InvariantCulture),
                HS2Next.App.Setup.CacheNames.ThumbFolder(s.Key.SourceGuid),
                s.Key.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + "_" + Hash(stamp + "|" + e[s.Column]) + ".png");
        }

        static string Hash(string s)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
            return h.ToString("x16");
        }

        void OnDiskLoaded(string path, Texture2D t)
        {
            if (!byDiskPath.TryGetValue(path, out var s)) return;
            byDiskPath.Remove(path);
            if (t == null) { s.FromDisk = false; s.Latency ??= Stopwatch.StartNew(); ReadDisk_Fallback(s); return; }
            Accept(s, t, true);
        }

        // An unreadable PNG: forget it and load from the bundle.
        void ReadDisk_Fallback(Slot s)
        {
            try { if (s.DiskPath != null) File.Delete(s.DiskPath); } catch (Exception) { }
            FailReasons.TryGetValue("png unreadable", out int n);
            FailReasons["png unreadable"] = n + 1;
            var e = catalog.GetEntry(s.Category, s.Key);
            if (e == null) { Fail(s, "no columns"); return; }
            StartOpen(s, e);
        }

        static long BytesOf(Texture t)
        {
            if (t is Texture2D t2)
            {
                double bpp;
                switch (t2.format)
                {
                    case TextureFormat.DXT1: case TextureFormat.DXT1Crunched: case TextureFormat.BC4: case TextureFormat.ETC_RGB4: bpp = 0.5; break;
                    case TextureFormat.DXT5: case TextureFormat.DXT5Crunched: case TextureFormat.BC5: case TextureFormat.BC7: case TextureFormat.BC6H: case TextureFormat.Alpha8: case TextureFormat.R8: bpp = 1; break;
                    case TextureFormat.RGB565: case TextureFormat.RGBA4444: case TextureFormat.ARGB4444: case TextureFormat.R16: case TextureFormat.RG16: bpp = 2; break;
                    default: bpp = 4; break;
                }
                return (long)(t2.width * (double)t2.height * bpp * (t2.mipmapCount > 1 ? 4.0 / 3.0 : 1.0));
            }
            return (long)t.width * t.height * 4;
        }

        void Accept(Slot s, Texture2D t, bool fromDisk)
        {
            s.Texture = t;
            s.FromDisk = fromDisk;
            if (s.LooseTexture) FromLoose++;
            else if (!fromDisk && s.Bundle)
            {
                textureCount.TryGetValue(s.Bundle, out int n);
                textureCount[s.Bundle] = n + 1;
                FromBundles++;
            }
            else FromDiskCount++;
            s.Bytes = BytesOf(t);
            s.Lru = lru.AddFirst(s);
            CurrentBytes += s.Bytes;
            if (s.Latency != null) { FirstViewMs.Add(s.Latency.Elapsed.TotalMilliseconds); s.Latency = null; }
            Evict();
            PeakBytes = Math.Max(PeakBytes, CurrentBytes);
            if (s.Column == ThumbColumn) Loaded?.Invoke(s.Category, s.Key, t);
            LoadedColumn?.Invoke(s.Category, s.Key, s.Column, t);
        }

        void Evict()
        {
            var node = lru.Last;
            while (CurrentBytes > CapBytes && node != null)
            {
                var prev = node.Previous;
                if (node.Value.Owners.Count == 0) Drop(node.Value);
                node = prev;
            }
        }

        void Drop(Slot s)
        {
            if (s.Lru != null) { lru.Remove(s.Lru); s.Lru = null; }
            var tex = s.Texture;
            if (s.Texture != null) { CurrentBytes -= s.Bytes; s.Texture = null; Evicted++; }
            s.Bytes = 0;
            s.Latency = null;
            if (s.LooseTexture) { s.LooseTexture = false; catalog.Loose.Release(tex); }   // 4.2 GB10: destroyed when no take is left
            else if (s.FromDisk) { if (s.DiskPath != null) disk.Release(s.DiskPath, s); }
            else ReleaseBundle(s);
            s.FromDisk = false;
        }

        // The bundle's textures belong to the bundle: unloading it (thumbnail-only bundles) destroys them, so only when none is left.
        void ReleaseBundle(Slot s)
        {
            var b = s.Bundle;
            s.Bundle = null;
            if (!b || !textureCount.TryGetValue(b, out int n)) return;
            if (--n > 0) { textureCount[b] = n; return; }
            textureCount.Remove(b);
            // a load (or header read) of the same bundle still in flight keeps it open; its own drop unloads it later
            UnloadIfUnused(b);
        }

        void Fail(Slot s, string why)
        {
            FailReasons.TryGetValue(why, out int n);
            FailReasons[why] = n + 1;
            s.Failed = true;
            s.Latency = null;
            FailedCount++;
            if (s.Column == ThumbColumn) Loaded?.Invoke(s.Category, s.Key, null);
            LoadedColumn?.Invoke(s.Category, s.Key, s.Column, null);
        }

        // ---------- texture guard records (the guard's persistent list through TextureGuard.IsListed / Record) ----------

        /// <summary>The texture guard's source label of a row's thumbnail texture ("&lt;zipmod GUID | abdata&gt;:&lt;bundle path&gt;"): the
        /// copy that serves the texture when the asset redirect is on, else the row's own zipmod (abdata for vanilla rows).</summary>
        string SourceOf(ListEntry e, string column = ThumbColumn)
        {
            string ab = e?["ThumbAB"]?.Replace('\\', '/');
            string guid = e?.Zipmod?.Guid;
            var rd = ContentCatalog.SideloaderAssetRedirect ? catalog?.Redirect : null;
            if (rd != null && ab != null && rd.IsRedirectable(ab))
            {
                var served = rd.Serve(ab, e[column ?? ThumbColumn]);
                if (served.Zip != null) guid = served.Zip.Guid;
                else if (served.Abdata) guid = null;
            }
            return (guid ?? "abdata") + ":" + ab;
        }

        // A listed texture whose bundle has a repaired copy (Tools/texguard_repair.py) loads from that copy.
        bool GuardListed(ListEntry e, string column)
        {
            if (!HS2Next.Chara.TextureGuard.IsListed(SourceOf(e, column), e[column])) return false;
            string ab = e["ThumbAB"]?.Replace('\\', '/');
            return e.Zipmod == null || HS2Next.Chara.TextureGuard.RepairedPath(e.Zipmod.Path + "!" + ab) == null;
        }

        // A rejected thumbnail texture goes to the guard's list in its format.
        void RecordBad(ListEntry e, string column, Texture2D t)
        {
            if (e == null) return;
            string name = e[column];
            HS2Next.Chara.TextureGuard.Record(new HS2Next.Chara.BadTexture
            {
                Source = SourceOf(e, column), Asset = name, Texture = t != null ? t.name : name, Width = t != null ? t.width : 0, Height = t != null ? t.height : 0,
                Format = t != null ? t.format.ToString() : "", Mips = t != null ? t.mipmapCount : 0, Reason = "thumbnail: mipmapCount < 1 after an asynchronous load",
            }, e.Zipmod != null ? e.Zipmod.Path + "!" + e["ThumbAB"]?.Replace('\\', '/') : "");
        }

        // ---------- the instance of the Maker pages ----------

        static PartThumbnails shared;
        static ContentCatalog sharedFor;

        /// <summary>The part thumbnails the Maker pages share for <paramref name="catalog"/> (disk cache under the catalog's cache).</summary>
        public static PartThumbnails Shared(ContentCatalog catalog)
        {
            if (catalog == null) return null;
            if (shared != null && ReferenceEquals(sharedFor, catalog)) return shared;
            shared?.Clear();
            sharedFor = catalog;
            shared = new PartThumbnails(catalog, CacheDirFor(catalog));
            return shared;
        }

        /// <summary>
        /// The Maker pages' instance in numbers (ui.state's <c>thumbs</c>; null before a page made it): pending loads (queued,
        /// opening, header reads, asset loads, disk reads), the sources of the shown textures, the refusals and failures by
        /// reason, deferred unloads, the switches, and the catalog's open bundles.
        /// </summary>
        public static Dictionary<string, object> UiState(ContentCatalog catalog)
        {
            var p = shared != null && ReferenceEquals(sharedFor, catalog) ? shared : null;
            if (p == null) return null;
            return new Dictionary<string, object>
            {
                ["pending"] = p.Pending, ["headerChecks"] = p.checking.Count, ["assetLoads"] = p.AssetLoadsInFlight, ["opens"] = p.OpensInFlight,
                ["shown"] = p.Count, ["fromBundles"] = p.FromBundles, ["fromDisk"] = p.FromDiskCount, ["fromLoose"] = p.FromLoose,
                ["failed"] = p.FailedCount, ["failReasons"] = new Dictionary<string, int>(p.FailReasons),
                ["guardSkipped"] = p.GuardSkipped, ["headerRejected"] = p.HeaderRejected, ["headerUnchecked"] = p.HeaderUnchecked,
                ["badMipRejected"] = p.BadMipRejected, ["unloadsDeferred"] = p.UnloadsDeferred, ["bundlesUnloaded"] = p.BundlesUnloaded,
                ["headerCheck"] = HeaderCheck, ["readDisk"] = p.ReadDisk, ["writeDisk"] = p.WriteDisk,
                ["openBundles"] = catalog?.OpenBundleCount ?? 0, ["thumbnailOnlyBundles"] = catalog?.ThumbnailOnlyCount ?? 0,
            };
        }

        int lastFrame = -1;

        /// <summary>Update at most once per frame (several lists tick the shared instance).</summary>
        public void UpdateOncePerFrame(double budgetMs = 2)
        {
            int f = Time.frameCount;
            if (f == lastFrame) return;
            lastFrame = f;
            Update(budgetMs);
        }


        // GPU read-back of the loaded texture into a 128² sRGB PNG, encoded and written by a worker.
        void WritePng(string path, Texture src)
        {
            RenderTexture rt = null;
            try
            {
                rt = RenderTexture.GetTemporary(DiskSize, DiskSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(src, rt);
                lock (diskLock) diskWritesPending++;
                var target = rt;
                AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, r =>
                {
                    RenderTexture.ReleaseTemporary(target);
                    if (r.hasError) { lock (diskLock) diskWritesPending--; return; }
                    var data = r.GetData<byte>().ToArray();
                    Task.Run(() =>
                    {
                        try
                        {
                            var png = ImageConversion.EncodeArrayToPNG(data, GraphicsFormat.R8G8B8A8_SRGB, DiskSize, DiskSize);
                            Directory.CreateDirectory(Path.GetDirectoryName(path));
                            var tmp = path + ".tmp";
                            File.WriteAllBytes(tmp, png);
                            if (File.Exists(path)) File.Delete(path);
                            File.Move(tmp, path);
                            lock (diskLock) { diskWritesPending--; DiskWritten++; }
                        }
                        catch (Exception) { lock (diskLock) diskWritesPending--; }
                    });
                });
                rt = null;
            }
            catch (Exception) { if (rt != null) RenderTexture.ReleaseTemporary(rt); }
        }

        /// <summary>Drops every texture no cell shows (and unloads thumbnail-only bundles that held only those).</summary>
        public void Trim()
        {
            var node = lru.Last;
            while (node != null)
            {
                var prev = node.Previous;
                if (node.Value.Owners.Count == 0) Drop(node.Value);
                node = prev;
            }
        }

        /// <summary>Drops every texture and request (thumbnail-only bundles are unloaded).</summary>
        public void Clear()
        {
            foreach (var s in slots.Values) s.Owners.Clear();
            Trim();
            // 4.2 GB10: loose reads in flight are cancelled (their worker task is background thread-pool work that ends by
            // itself; nothing waits for it); the disk loader disposes its UnityWebRequests (in flight and done, not accepted)
            foreach (var s in inFlight) if (s.Loose != null) { s.Loose.Cancel(); s.Loose = null; }
            waiting.Clear();
            inFlight.Clear();
            opening.Clear();
            foreach (var s in checking) { s.Header = null; s.HeaderFile = null; }   // the worker reads end by themselves
            checking.Clear();
            byDiskPath.Clear();
            slots.Clear();
            disk.Clear();
        }

        public void Dispose()
        {
            Clear();
            lock (live) live.RemoveAll(w => !w.TryGetTarget(out var p) || ReferenceEquals(p, this));
        }

        // ---------- shutdown (Phase 4.2 package L): play-mode exit and app quit never wait on thumbnail work ----------
        // UiPanelsTest.play hung at exit (0 % CPU after "CodeReloadManager destroyed") with thumbnail loads in flight: disposing
        // the disk loader's UnityWebRequests alone did not help (3 of 3 runs hung); completing the in-flight AssetBundleRequests
        // and bundle opens before the shutdown did. Every instance is registered; Application.quitting and a hidden quit-hook
        // object (OnApplicationQuit / OnDisable, which run before the shutdown, also on EditorApplication.Exit from play mode)
        // clear them all.

        static readonly List<WeakReference<PartThumbnails>> live = new List<WeakReference<PartThumbnails>>();
        static bool quittingHooked;
        static bool releasingForQuit;   // inside ReleaseAllForQuit: thumbnail-only bundles stay open for the catalog's close
        bool stopped;                   // released for the quit: Request and Update schedule nothing any more

        /// <summary>The longest the quit release completes asynchronous asset loads in flight before it leaves the rest.</summary>
        public const int QuitWaitMs = 2000;

        /// <summary>Thumbnail loads the last quit release that found work could not complete within <see cref="QuitWaitMs"/> (0 expected).</summary>
        public static int PendingAtQuit { get; private set; }

        /// <summary>Thumbnail-only bundles the quit releases left open for the catalog, which closes every bundle at the quit.</summary>
        public static int BundlesLeftAtQuit { get; private set; }

        /// <summary>Diagnostics only (LooseTextureTest.RunQuit -nohook): false leaves the instances to Unity's shutdown, as before 4.2.</summary>
        public static bool ReleaseOnQuit = true;

        void Register()
        {
            lock (live) { live.RemoveAll(w => !w.TryGetTarget(out _)); live.Add(new WeakReference<PartThumbnails>(this)); }
            if (!quittingHooked) { quittingHooked = true; Application.quitting += () => { if (ReleaseOnQuit) ReleaseAllForQuit("Application.quitting"); }; }
            if (Application.isPlaying) PartThumbnailsQuitHook.Ensure();
        }

        /// <summary>Instances alive (registered and not disposed).</summary>
        public static int LiveInstances
        {
            get { int n = 0; lock (live) foreach (var w in live) if (w.TryGetTarget(out _)) n++; return n; }
        }

        /// <summary>
        /// Clears every live instance (idempotent; main thread) and stops it scheduling: completes the asynchronous asset loads in
        /// flight for at most <see cref="QuitWaitMs"/> (the rest is counted in <see cref="PendingAtQuit"/>), disposes the disk loaders'
        /// UnityWebRequests, cancels loose reads and queued work, releases the textures and leaves thumbnail-only bundles open for the
        /// catalog's own close at the quit; the shared instance is forgotten. Logs one line when something was pending. Returns the
        /// instances cleared, the web requests disposed (in flight in the disk loaders), the loose reads cancelled, the asynchronous
        /// asset loads and bundle opens completed. An exit with a thumbnail's AssetBundleRequest still in flight hung (UiPanelsTest.play
        /// 3 of 3 with only the web requests disposed; it exits once the loads are completed here).
        /// </summary>
        public static (int instances, int webRequests, int looseReads, int assetLoads, int bundleOpens) ReleaseAllForQuit(string why)
        {
            var all = new List<PartThumbnails>();
            lock (live) { foreach (var w in live) if (w.TryGetTarget(out var p)) all.Add(p); live.Clear(); }
            int web = 0, loose = 0, pending = 0, writes = 0, assets = 0, opens = 0, left = 0, bundlesLeft = BundlesLeftAtQuit;
            var catalogs = new List<ContentCatalog>();
            var wait = Stopwatch.StartNew();
            releasingForQuit = true;
            try
            {
                foreach (var p in all)
                {
                    try
                    {
                        p.stopped = true;
                        web += p.disk.InFlight;
                        foreach (var s in p.inFlight) if (s.Loose != null) loose++;
                        pending += p.Pending;
                        writes += p.DiskWritesPending;
                        // asynchronous asset loads and bundle opens are completed (blocking) before the shutdown, so that none is
                        // still in flight when Unity tears down; past the bound the rest is left and counted
                        foreach (var s in p.inFlight)
                            if (s.Request != null)
                            {
                                if (wait.ElapsedMilliseconds > QuitWaitMs) { left++; continue; }
                                assets++;
                                try { _ = s.Request.asset; } catch (Exception) { }
                            }
                        if (!catalogs.Contains(p.catalog)) catalogs.Add(p.catalog);
                        p.Clear();
                    }
                    catch (Exception ex) { UnityEngine.Debug.LogWarning("[thumbs] quit: clear failed: " + ex.Message); }
                }
            }
            finally { releasingForQuit = false; }
            if (all.Count > 0) PendingAtQuit = left;
            bundlesLeft = BundlesLeftAtQuit - bundlesLeft;
            foreach (var cat in catalogs)
                try { opens += cat.CompletePendingOpens(); } catch (Exception ex) { UnityEngine.Debug.LogWarning("[thumbs] quit: completing the bundle opens failed: " + ex.Message); }
            shared = null;
            sharedFor = null;
            // disk-cache writes whose GPU read-back is outstanding: finished now, while the device is alive (their encode and
            // file write run on thread-pool workers)
            if (writes > 0 && SystemInfo.supportsAsyncGPUReadback)
                try { AsyncGPUReadback.WaitAllRequests(); } catch (Exception) { }
            if (all.Count > 0 && (pending > 0 || writes > 0))
                UnityEngine.Debug.Log($"[thumbs] quit ({why}): cleared {all.Count} part-thumbnail instances, {pending} pending, disposed {web} web requests in flight, cancelled {loose} loose reads, completed {assets} asset loads and {opens} bundle opens, {writes} disk writes pending; {left} asset loads left after {QuitWaitMs} ms, {bundlesLeft} thumbnail bundles left to the catalog");
            return (all.Count, web, loose, assets, opens);
        }
    }
}

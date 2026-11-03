// Thumbnails of the card grid, the scene browser and the pose grid (
// fallback): PNGs are decoded off the main thread by UnityWebRequestTexture (file URI,
// non-readable, at most 8 requests in flight); images wider than 300 px are shrunk on the GPU (Graphics.ConvertTexture) to
// 252 px wide keeping the aspect (cards 252 × 352); an LRU capped by bytes (110 MB ≈ 300 cards) evicts textures no cell
// shows; files Unity cannot decode get the placeholder and are reported once (finding ui.thumbnailUnreadable, info).
// A cell asks with Request(path, owner) on bind and Release(path, owner) on unbind (a pending request nobody wants any more
// is dropped before it starts). Card content is never looked at: the loader only counts and times. Main thread only.
// Generated images (the pose browser's stick figures): a key the Generator claims is not a file; its disk-cached PNG, when
// the generator has one, is read like any file (a cached file that fails to decode is drawn again), else the generator is
// asked to make it and answers with Provide (the texture joins the same LRU) or Withdraw (the request ends without the error
// tile: the cell shows what its source says next, e.g. the pose's text tile).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

namespace HS2Next.UI.Panels
{
    public sealed class ThumbnailLoader : IDisposable
    {
        sealed class Slot
        {
            public string Path;
            public Texture2D Texture;
            public long Bytes;
            public bool Failed;
            public UnityWebRequest Request;
            public Stopwatch Latency;
            public readonly HashSet<object> Owners = new HashSet<object>();
            public LinkedListNode<Slot> Lru;
            public bool Queued;
            /// <summary>A generated key: being made by the generator / read from its cached file / to be drawn again.</summary>
            public bool Generating, FromCache, Regenerate;
        }

        /// <summary>Makes the images of keys that are not files (the pose browser's stick figures).</summary>
        public interface IGenerator
        {
            /// <summary>The key is the generator's.</summary>
            bool Claims(string key);
            /// <summary>The disk-cached image of a key (read as a file), or null.</summary>
            string CachedFile(string key);
            /// <summary>Starts making the key; the generator answers with <see cref="Provide"/> or <see cref="Withdraw"/>.</summary>
            void Want(string key);
        }

        /// <summary>The generator of non-file keys (null = every key is a file).</summary>
        public IGenerator Generator;

        public const long DefaultCapBytes = 110L * 1024 * 1024;

        readonly Dictionary<string, Slot> slots = new Dictionary<string, Slot>(StringComparer.OrdinalIgnoreCase);
        readonly LinkedList<Slot> lru = new LinkedList<Slot>();          // loaded textures, most recent first
        readonly Queue<Slot> waiting = new Queue<Slot>();
        readonly List<Slot> inFlight = new List<Slot>();
        readonly HashSet<string> reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Requests in flight at most (S4b: 8).</summary>
        public int MaxInFlight = 8;
        /// <summary>LRU cap in bytes (S4b: 110 MB).</summary>
        public long CapBytes = DefaultCapBytes;
        /// <summary>Images wider than this are shrunk to <see cref="ShrinkWidth"/>.</summary>
        public int ShrinkAbove = 300, ShrinkWidth = 252;

        /// <summary>A texture is ready for <c>path</c> (cells bound to it refresh).</summary>
        public event Action<string, Texture2D> Loaded;
        /// <summary>The file could not be decoded (placeholder); raised once per path and session.</summary>
        public event Action<string> Unreadable;

        // statistics (U-M1)
        public int Decoded { get; private set; }
        public int FailedCount { get; private set; }
        public int Shrunk { get; private set; }
        public int Evicted { get; private set; }
        /// <summary>Generated textures taken in (Provide), generated keys read from the generator's disk cache, withdrawn keys.</summary>
        public int GeneratedCount { get; private set; }
        public int GeneratedFromCache { get; private set; }
        public int Withdrawn { get; private set; }
        public long CurrentBytes { get; private set; }
        public long PeakBytes { get; private set; }
        public readonly List<double> MainThreadMs = new List<double>();
        public readonly List<double> LatencyMs = new List<double>();
        public readonly List<double> ShrinkMs = new List<double>();
        public int Count => lru.Count;
        public int Pending => waiting.Count + inFlight.Count;
        public int InFlight => inFlight.Count;
        public int Waiting => waiting.Count;
        /// <summary>Update calls so far (diagnostics).</summary>
        public int Updates { get; private set; }
        /// <summary>Age of the oldest request in flight, ms (0 = none).</summary>
        public double OldestInFlightMs { get { double m = 0; foreach (var s in inFlight) if (s.Latency != null) m = Math.Max(m, s.Latency.Elapsed.TotalMilliseconds); return m; } }

        static Texture2D placeholder, errorPlaceholder;

        /// <summary>A neutral grey tile shown while loading.</summary>
        public static Texture2D Placeholder => placeholder != null ? placeholder : placeholder = Solid(new Color(0.16f, 0.16f, 0.17f, 1f), false);
        /// <summary>The tile of undecodable files (grey with a red mark).</summary>
        public static Texture2D ErrorPlaceholder => errorPlaceholder != null ? errorPlaceholder : errorPlaceholder = Solid(new Color(0.16f, 0.16f, 0.17f, 1f), true);

        static Texture2D Solid(Color c, bool mark)
        {
            const int w = 18, h = 25;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = mark ? "HS2Next thumb error" : "HS2Next thumb placeholder", hideFlags = HideFlags.HideAndDontSave };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool m = mark && Math.Abs(x - y * w / (float)h) < 1.2f || mark && Math.Abs((w - 1 - x) - y * w / (float)h) < 1.2f;
                    px[y * w + x] = m ? new Color(0.75f, 0.2f, 0.2f, 1f) : c;
                }
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }

        /// <summary>The loaded texture of <paramref name="path"/> (touches the LRU), or null.</summary>
        public Texture2D TryGet(string path)
        {
            if (path == null || !slots.TryGetValue(path, out var s) || s.Texture == null) return null;
            Touch(s);
            return s.Texture;
        }

        /// <summary>True when the file failed to decode (show <see cref="ErrorPlaceholder"/>).</summary>
        public bool IsFailed(string path) => path != null && slots.TryGetValue(path, out var s) && s.Failed;

        /// <summary>A cell (<paramref name="owner"/>) wants the thumbnail; returns it when already loaded.</summary>
        public Texture2D Request(string path, object owner)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (!slots.TryGetValue(path, out var s)) slots[path] = s = new Slot { Path = path };
            if (owner != null) s.Owners.Add(owner);
            if (s.Texture != null) { Touch(s); return s.Texture; }
            if (!s.Failed && !s.Queued && s.Request == null && !s.Generating) { s.Queued = true; waiting.Enqueue(s); }
            return null;
        }

        /// <summary>True while a cell shows the key (the generator skips keys nobody wants any more).</summary>
        public bool Wanted(string path) => path != null && slots.TryGetValue(path, out var s) && s.Owners.Count > 0;

        /// <summary>The generator's texture for a key: it joins the LRU (the loader owns it from now on).</summary>
        public void Provide(string path, Texture2D t)
        {
            if (path == null || t == null) return;
            if (!slots.TryGetValue(path, out var s)) slots[path] = s = new Slot { Path = path };
            s.Generating = false;
            s.Regenerate = false;
            if (s.Texture != null) { Kill(t); return; }
            GeneratedCount++;
            Accept(s, t);
        }

        /// <summary>The generator gave a key up (it cannot be drawn, or nobody wanted it any more): the request ends without
        /// the error tile; a cell that shows the key again asks again.</summary>
        public void Withdraw(string path)
        {
            if (path == null || !slots.TryGetValue(path, out var s)) return;
            s.Generating = false;
            Withdrawn++;
        }

        /// <summary>The cell no longer shows the path (unbind).</summary>
        public void Release(string path, object owner)
        {
            if (path == null || !slots.TryGetValue(path, out var s)) return;
            s.Owners.Remove(owner);
        }

        void Touch(Slot s)
        {
            if (s.Lru == null) return;
            lru.Remove(s.Lru);
            lru.AddFirst(s.Lru);
        }

        /// <summary>Starts requests and accepts finished ones within <paramref name="budgetMs"/> of main-thread time; call once per frame.</summary>
        public void Update(double budgetMs = 4)
        {
            Updates++;
            UpdateCore(budgetMs);
        }

        void UpdateCore(double budgetMs)
        {
            var sw = Stopwatch.StartNew();
            while (inFlight.Count < MaxInFlight && waiting.Count > 0 && sw.Elapsed.TotalMilliseconds < budgetMs)
            {
                var s = waiting.Dequeue();
                s.Queued = false;
                if (s.Texture != null || s.Failed || s.Request != null) continue;
                if (s.Owners.Count == 0) continue;   // nobody shows it any more
                string file = s.Path;
                if (Generator != null && Generator.Claims(s.Path))
                {
                    file = s.Regenerate ? null : Generator.CachedFile(s.Path);
                    if (file == null) { s.Generating = true; Generator.Want(s.Path); continue; }
                    s.FromCache = true;
                }
                try
                {
                    s.Request = UnityWebRequestTexture.GetTexture(new Uri(file).AbsoluteUri, true);
                    s.Request.SendWebRequest();
                    s.Latency = Stopwatch.StartNew();
                    inFlight.Add(s);
                }
                catch (Exception) { Fail(s); }
            }
            for (int i = inFlight.Count - 1; i >= 0 && sw.Elapsed.TotalMilliseconds < budgetMs; i--)
            {
                var s = inFlight[i];
                if (!s.Request.isDone) continue;
                inFlight.RemoveAt(i);
                var req = s.Request;
                s.Request = null;
                try
                {
                    if (s.FromCache && Generator != null)
                    {
                        // a generated key's cached file, decoded like a file; one that fails is drawn again
                        s.FromCache = false;
                        Texture2D g = null;
                        if (req.result == UnityWebRequest.Result.Success) { try { g = DownloadHandlerTexture.GetContent(req); } catch (Exception) { g = null; } }
                        if (g == null || g.width <= 8)
                        {
                            if (g != null) Kill(g);
                            s.Regenerate = true;
                            s.Generating = true;
                            Generator.Want(s.Path);
                            continue;
                        }
                        g.name = "HS2Next thumb";
                        g.hideFlags = HideFlags.HideAndDontSave;
                        GeneratedFromCache++;
                        Accept(s, g);
                        continue;
                    }
                    if (req.result != UnityWebRequest.Result.Success) { Fail(s); continue; }
                    LatencyMs.Add(s.Latency.Elapsed.TotalMilliseconds);
                    var t0 = Stopwatch.StartNew();
                    Texture2D t = null;
                    try { t = DownloadHandlerTexture.GetContent(req); } catch (Exception) { t = null; }
                    if (t == null || t.width <= 8) { if (t != null) Kill(t); Fail(s); continue; }
                    t = Shrink(t);
                    t.name = "HS2Next thumb";
                    t.hideFlags = HideFlags.HideAndDontSave;
                    MainThreadMs.Add(t0.Elapsed.TotalMilliseconds);
                    Accept(s, t);
                }
                finally { req.Dispose(); }
            }
        }

        Texture2D Shrink(Texture2D t)
        {
            if (t.width <= ShrinkAbove) return t;
            var s0 = Stopwatch.StartNew();
            int h = Math.Max(1, (int)Math.Round(ShrinkWidth * (double)t.height / t.width));
            var small = new Texture2D(ShrinkWidth, h, TextureFormat.RGBA32, false);
            bool ok;
            try { ok = Graphics.ConvertTexture(t, small); } catch (Exception) { ok = false; }
            ShrinkMs.Add(s0.Elapsed.TotalMilliseconds);
            if (!ok) { Kill(small); return t; }
            Kill(t);
            Shrunk++;
            return small;
        }

        static long BytesOf(Texture2D t) => (long)t.width * t.height * 4;

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }

        void Accept(Slot s, Texture2D t)
        {
            s.Texture = t;
            s.Bytes = BytesOf(t);
            s.Lru = lru.AddFirst(s);
            CurrentBytes += s.Bytes;
            Decoded++;
            Evict();
            PeakBytes = Math.Max(PeakBytes, CurrentBytes);
            Loaded?.Invoke(s.Path, t);
        }

        void Evict()
        {
            var node = lru.Last;
            while (CurrentBytes > CapBytes && node != null)
            {
                var prev = node.Previous;
                var s = node.Value;
                if (s.Owners.Count == 0) Drop(s);   // shown textures stay (the visible grid is far below the cap)
                node = prev;
            }
        }

        void Drop(Slot s)
        {
            if (s.Lru != null) { lru.Remove(s.Lru); s.Lru = null; }
            if (s.Texture != null) { CurrentBytes -= s.Bytes; Kill(s.Texture); s.Texture = null; Evicted++; }
            s.Bytes = 0;
        }

        void Fail(Slot s)
        {
            s.Failed = true;
            FailedCount++;
            if (reported.Add(s.Path)) Unreadable?.Invoke(s.Path);
            Loaded?.Invoke(s.Path, null);
        }

        /// <summary>Forgets a path (the file changed or was deleted).</summary>
        public void Forget(string path)
        {
            if (path == null || !slots.TryGetValue(path, out var s)) return;
            Drop(s);
            if (s.Request != null) { inFlight.Remove(s); s.Request.Dispose(); s.Request = null; }
            s.Generating = false;
            slots.Remove(path);
        }

        /// <summary>Drops every texture and request.</summary>
        public void Clear()
        {
            foreach (var s in inFlight) s.Request?.Dispose();
            inFlight.Clear();
            waiting.Clear();
            foreach (var s in slots.Values) if (s.Texture != null) Kill(s.Texture);
            slots.Clear();
            lru.Clear();
            CurrentBytes = 0;
        }

        public void Dispose() => Clear();
    }
}

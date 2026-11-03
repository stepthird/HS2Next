// Lazily indexed character names of the character browser: a worker thread reads each
// card's name through a light header reader (the PNG chunk list is skipped by seeking, then the trailer header and the
// Parameter block only — never the face / body / coordinate blocks) and caches (path, size, mtime) → name for the session;
// until a card is indexed the browser shows and searches its file name. HS2Next character files (.hs2nchara) give the
// manifest title. No image is decoded. Unity-free (System + the HS2Next readers). The app's quit stops every live index's worker
// (StopAllForQuit) before the runtime shuts down: a worker still waiting for entries when the runtime aborted it crashed the process.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using HS2Next.IO;
using HS2Next.Save;

namespace HS2Next.UI.Panels
{
    /// <summary>What the header reader found in a card file.</summary>
    public struct CardHeaderInfo
    {
        /// <summary>The character's full name ("" when the card has none); null when the file is not a readable character card.</summary>
        public string Name;
        /// <summary>0 male, 1 female, -1 unknown.</summary>
        public int Sex;
    }

    public static class CardHeaderReader
    {
        const int MaxHeader = 1 << 20, MaxParameter = 1 << 20;

        /// <summary>Reads the name and sex of an HS2 / AI character card or the title and sex of a .hs2nchara file; never throws.</summary>
        public static CardHeaderInfo Read(string path)
        {
            try
            {
                if (path.EndsWith(SaveFormats.CharaExtension, StringComparison.OrdinalIgnoreCase)) return ReadHs2n(path);
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 14))
                    return ReadCard(fs);
            }
            catch (Exception) { return new CardHeaderInfo { Name = null, Sex = -1 }; }
        }

        static CardHeaderInfo ReadHs2n(string path)
        {
            var r = ContainerReader.Read(path, null, n => n == EntryNames.Manifest);
            var m = r.ReadManifest();
            return new CardHeaderInfo { Name = m.Title ?? "", Sex = m.Sex ?? -1 };
        }

        /// <summary>The card layout of HS2Next.Data.Card (PNG, then: int product, string marker, string version, int language,
        /// string userID, string dataID, int header length, msgpack header {lstInfo}, long data length, the blocks).</summary>
        static CardHeaderInfo ReadCard(Stream s)
        {
            var none = new CardHeaderInfo { Name = null, Sex = -1 };
            long start = PngEnd(s);
            if (start < 0) return none;
            s.Position = start;
            using (var br = new BinaryReader(s, Encoding.UTF8, true))
            {
                br.ReadInt32();
                string marker = br.ReadString();
                if (marker == null || marker.IndexOf("AIS_Chara", StringComparison.Ordinal) < 0) return none;
                br.ReadString();      // version
                br.ReadInt32();       // language
                br.ReadString();      // userID
                br.ReadString();      // dataID
                int hlen = br.ReadInt32();
                if (hlen <= 0 || hlen > MaxHeader) return none;
                var header = MsgPack.Read(br.ReadBytes(hlen));
                br.ReadInt64();
                long basePos = s.Position;
                foreach (var bi in header.Get("lstInfo").Items())
                {
                    if (bi.Get("name").Str() != "Parameter") continue;
                    long pos = Convert.ToInt64(bi.Get("pos"));
                    int size = bi.Get("size").Int();
                    if (size <= 0 || size > MaxParameter || basePos + pos + size > s.Length) return none;
                    s.Position = basePos + pos;
                    var p = MsgPack.Read(br.ReadBytes(size));
                    return new CardHeaderInfo { Name = p.Get("fullname").Str("") ?? "", Sex = p.Has("sex") ? p.Get("sex").Int(-1) : -1 };
                }
                return new CardHeaderInfo { Name = "", Sex = -1 };
            }
        }

        /// <summary>Offset after the PNG's IEND chunk by walking the chunk headers (seeks, no image read); 0 when the data has no
        /// PNG signature (Maker autosaves); -1 when the chunk list is broken.</summary>
        static long PngEnd(Stream s)
        {
            var sig = new byte[8];
            s.Position = 0;
            if (s.Read(sig, 0, 8) != 8) return -1;
            if (!(sig[0] == 0x89 && sig[1] == (byte)'P' && sig[2] == (byte)'N' && sig[3] == (byte)'G')) return 0;
            long p = 8, len = s.Length;
            var h = new byte[8];
            while (p + 12 <= len)
            {
                s.Position = p;
                if (s.Read(h, 0, 8) != 8) return -1;
                long n = ((long)h[0] << 24) | ((long)h[1] << 16) | ((long)h[2] << 8) | h[3];
                bool iend = h[4] == (byte)'I' && h[5] == (byte)'E' && h[6] == (byte)'N' && h[7] == (byte)'D';
                p += 12 + n;
                if (iend) return p <= len ? p : -1;
            }
            return -1;
        }
    }

    /// <summary>
    /// The session cache and worker. <see cref="Enqueue"/> adds entries (in the order given, typically the browser's current
    /// sort / scroll order); the worker indexes them one by one; <see cref="Drain"/> (main thread) hands out the finished ones.
    /// Entries already in the cache with the same size and mtime are answered without reading.
    /// </summary>
    public sealed class CardNameIndex : IDisposable
    {
        struct Key { public long Size; public long Ticks; }

        static readonly object cacheLock = new object();
        static readonly Dictionary<string, (Key key, CardHeaderInfo info)> cache = new Dictionary<string, (Key, CardHeaderInfo)>(StringComparer.OrdinalIgnoreCase);

        readonly BlockingCollection<CardEntry> queue = new BlockingCollection<CardEntry>();
        readonly ConcurrentQueue<CardEntry> done = new ConcurrentQueue<CardEntry>();
        readonly Thread worker;
        volatile bool stop;
        int read, cached, failed;

        /// <summary>Cards read from disk / answered from the cache / not readable so far.</summary>
        public int Read => read;
        public int Cached => cached;
        public int Failed => failed;
        /// <summary>Entries waiting for the worker.</summary>
        public int Pending => queue.Count;
        /// <summary>Milliseconds the worker spent reading (for the notes).</summary>
        public double WorkerMs { get; private set; }

        public CardNameIndex()
        {
            worker = new Thread(Run) { IsBackground = true, Name = "HS2Next card names", Priority = ThreadPriority.BelowNormal };
            lock (live) { live.RemoveAll(w => !w.TryGetTarget(out _)); live.Add(new WeakReference<CardNameIndex>(this)); }
            worker.Start();
        }

        static readonly List<WeakReference<CardNameIndex>> live = new List<WeakReference<CardNameIndex>>();

        /// <summary>
        /// The quit: stops every live index (no more entries are taken; the entry being read finishes) and waits for their workers
        /// together at most <paramref name="boundMs"/> ms. Returns the workers still running after the bound (0 expected).
        /// </summary>
        public static int StopAllForQuit(int boundMs)
        {
            var all = new List<CardNameIndex>();
            lock (live) { foreach (var w in live) if (w.TryGetTarget(out var x)) all.Add(x); live.Clear(); }
            foreach (var x in all) x.Dispose();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int running = 0;
            foreach (var x in all)
            {
                int left = Math.Max(0, boundMs - (int)sw.ElapsedMilliseconds);
                if (x.worker.IsAlive && !x.worker.Join(left)) running++;
            }
            return running;
        }

        /// <summary>Queues entries whose name is not indexed yet (entries answered from the session cache are finished at once).</summary>
        public void Enqueue(IEnumerable<CardEntry> entries)
        {
            foreach (var e in entries)
            {
                if (e == null || e.Indexed || e.Queued) continue;
                if (TryCached(e)) { done.Enqueue(e); continue; }
                e.Queued = true;
                if (!queue.IsAddingCompleted) queue.Add(e);
            }
        }

        bool TryCached(CardEntry e)
        {
            lock (cacheLock)
            {
                if (cache.TryGetValue(e.FullPath, out var c) && c.key.Size == e.Size && c.key.Ticks == e.ModifiedUtc.Ticks)
                {
                    Apply(e, c.info);
                    Interlocked.Increment(ref cached);
                    return true;
                }
            }
            return false;
        }

        static void Apply(CardEntry e, CardHeaderInfo info)
        {
            e.CharaName = info.Name;
            e.HeaderSex = info.Sex;
            e.Unreadable = info.Name == null;
            e.Indexed = true;
        }

        void Run()
        {
            var sw = new System.Diagnostics.Stopwatch();
            try
            {
                foreach (var e in queue.GetConsumingEnumerable())
                {
                    if (stop) break;
                    if (TryCached(e)) { done.Enqueue(e); continue; }
                    sw.Restart();
                    var info = CardHeaderReader.Read(e.FullPath);
                    WorkerMs += sw.Elapsed.TotalMilliseconds;
                    if (info.Name == null) Interlocked.Increment(ref failed); else Interlocked.Increment(ref read);
                    lock (cacheLock) cache[e.FullPath] = (new Key { Size = e.Size, Ticks = e.ModifiedUtc.Ticks }, info);
                    Apply(e, info);
                    done.Enqueue(e);
                }
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        /// <summary>Main thread: hands out up to <paramref name="max"/> finished entries.</summary>
        public int Drain(List<CardEntry> into, int max = int.MaxValue)
        {
            int n = 0;
            while (n < max && done.TryDequeue(out var e)) { e.Queued = false; into.Add(e); n++; }
            return n;
        }

        /// <summary>Forgets one file of the session cache (it was deleted).</summary>
        public static void Forget(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            lock (cacheLock) cache.Remove(path);
        }

        /// <summary>Forgets the session cache (tests).</summary>
        public static void ClearCache()
        {
            lock (cacheLock) cache.Clear();
        }

        public void Dispose()
        {
            stop = true;
            try { queue.CompleteAdding(); } catch (ObjectDisposedException) { }
            lock (live) live.RemoveAll(w => !w.TryGetTarget(out var x) || ReferenceEquals(x, this));
        }
    }
}

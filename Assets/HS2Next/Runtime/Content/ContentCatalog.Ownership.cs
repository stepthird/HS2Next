// Bundle ownership, the LRU of unowned bundles and the byte budget, the catalog's side (BundleOwners.cs holds the
// bookkeeping). Every bundle the catalog hands out or opens is recorded under the owner of the innermost scope
// (Own(owner)), under a pin for the paths every character and list needs (the abdata manifest, chara/oo_base,
// chara/mm_base, studio/base/00 with the skeleton, list/*), under the thumbnails' owner for thumbnail opens, and under
// "unscoped" otherwise. A released owner's bundles without another owner wait in the LRU; Tick evicts from its tail while
// the LRU's resident bytes exceed the budget, with AssetBundle.Unload(false): objects loaded from an evicted bundle stay
// valid while anything references them, and Resources.UnloadUnusedAssets (rate-limited, at quiet points, never implicitly
// in edit-mode hosts) frees them once nothing does - also while their bundle is open. So the estimate of the loaded
// textures is reported and triggers that call but does not weigh in the LRU: an eviction would not free it, and a large
// shared bundle (studio/00: 1.07 GB, 1,714 textures, estimated 1.99 GB) would be evicted at every release and reopened
// with a second copy of its objects at the next use. A bundle reopened after its eviction is a new bundle; a second load of an asset loaded before the
// eviction gives a second object (counted as a reload). Unload(true) stays where it is safe: thumbnail-only bundles,
// the CAB rule (a), the map's scene bundle, the list bundles and UnloadAll.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;

namespace HS2Next.Content
{
    public partial class ContentCatalog
    {
        /// <summary>The owners of the open bundles, the LRU of unowned ones and the byte budget.</summary>
        public BundleLedger Ledger { get; } = new BundleLedger();

        /// <summary>Default of the byte budget of the unowned bundles in MB (settings key content.bundleCacheMB).</summary>
        public const int DefaultBundleCacheMB = 768;

        /// <summary>The byte budget of the unowned bundles in MB (0: every unowned bundle is evicted at the next pass).</summary>
        public long BundleCacheMB
        {
            get => Ledger.BudgetBytes / BundleLedger.MiB;
            set => Ledger.BudgetBytes = Math.Max(0, value) * BundleLedger.MiB;
        }

        /// <summary>A new owner (see <see cref="Own"/>).</summary>
        public BundleOwner NewOwner(string label) => Ledger.NewOwner(label);

        /// <summary>Records every bundle handed out until the scope is disposed under <paramref name="owner"/> (main thread;
        /// scopes nest, the innermost wins).</summary>
        public BundleLedger.Scope Own(BundleOwner owner) => Ledger.Own(owner);

        /// <summary>Releases an owner: its bundles without another owner wait in the LRU; the next pass evicts over the budget.</summary>
        public void Release(BundleOwner owner)
        {
            if (owner == null || !owner.Live) return;
            if (Ledger.Release(owner) > 0) evictionDue = true;
        }

        /// <summary>
        /// A load whose bundles nothing keeps (a part's facts read by an edit command): run under a short-lived owner released
        /// right after, so its bundles wait in the LRU unless the following build or flush uses them.
        /// </summary>
        public T LoadTransient<T>(string label, Func<T> load)
        {
            var owner = Ledger.NewOwner(label ?? "transient");
            try { using (Own(owner)) return load(); }
            finally { Release(owner); }
        }

        /// <summary>A scene realization (a character, an item, a map) was destroyed: counts for the next UnloadUnusedAssets.</summary>
        public void NoteRealizationDestroyed() => destroyedSinceUnload++;

        // The pin of a bundle path (never evicted), or null.
        static string PinOf(string key)
        {
            string p = PathOfKey(key);
            if (p.Equals("abdata", StringComparison.OrdinalIgnoreCase)) return "abdata";
            if (p.StartsWith("list/", StringComparison.OrdinalIgnoreCase)) return "list";
            if (p.Equals("chara/oo_base.unity3d", StringComparison.OrdinalIgnoreCase)) return "oo_base";
            if (p.Equals("chara/mm_base.unity3d", StringComparison.OrdinalIgnoreCase)) return "mm_base";
            if (p.Equals("studio/base/00.unity3d", StringComparison.OrdinalIgnoreCase)) return "skeleton";
            // the scene's sky, colour grading and ambient probe (small vanilla bundles every scene uses)
            if (p.StartsWith("studio/sky/", StringComparison.OrdinalIgnoreCase) || p.StartsWith("studio/lut/", StringComparison.OrdinalIgnoreCase)
                || p.StartsWith("studio/probe/", StringComparison.OrdinalIgnoreCase)) return "scene";
            return null;
        }

        // ---------- content.stats: the numbers of other parts of this class ----------

        /// <summary>The pre-upload texture check's numbers and the quarantined bundles for content.stats (filled by the check's
        /// part of this class; zeros without it).</summary>
        public sealed class ContentStatsExtra
        {
            public int Verified, Patched, Refused, CacheHits, Pending, PrescanDone, PrescanTotal, RepairedOpens;
            /// <summary>Quarantined bundles: cache key and source stamp.</summary>
            public readonly List<(string Key, string Stamp)> Quarantined = new List<(string, string)>();
        }

        partial void FillContentStats(ContentStatsExtra x);

        /// <summary>The image-chain bytes of a bundle's textures from the pre-upload check's verdict (left negative when unknown:
        /// the cached headers are used then).</summary>
        partial void TextureObjectBytes(string key, ref long bytes);

        /// <summary>The texture check's numbers and the quarantine for content.stats.</summary>
        public ContentStatsExtra StatsExtra()
        {
            var x = new ContentStatsExtra();
            FillContentStats(x);
            return x;
        }

        // ---------- the bundle <-> key maps ----------

        readonly Dictionary<AssetBundle, string> keyOfBundle = new Dictionary<AssetBundle, string>();

        void Register(string key, AssetBundle b)
        {
            bundles[key] = b;
            keyOfBundle[b] = key;
            if (LogBundles) BundleLog($"open {key} '{(b ? b.name : "-")}' scope {Ledger.Current?.Label ?? "-"} async owner {asyncOwner?.Label ?? "-"} thumbnail opens {thumbnailOpens}");
        }

        // ---------- diagnostics: the bundle log ----------

        /// <summary>Diagnostics: log every bundle open, unload and eviction, every asynchronous asset request and the
        /// UnloadUnusedAssets calls with the bundle's key, its name and the frame (environment HS2NEXT_CONTENT_LOG=1 or the
        /// argument -contentLog on). Off by default.</summary>
        public static bool LogBundles = LogSwitch();

        static bool LogSwitch()
        {
            try
            {
                if (Environment.GetEnvironmentVariable("HS2NEXT_CONTENT_LOG") == "1") return true;
                var a = Environment.GetCommandLineArgs();
                int i = Array.IndexOf(a, "-contentLog");
                return i >= 0 && i + 1 < a.Length && a[i + 1] == "on";
            }
            catch (Exception) { return false; }
        }

        void BundleLog(string what) =>
            UnityEngine.Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", "[bundles] f" + Time.frameCount + " " + what);

        // The asynchronous asset requests in flight that read the bundle of key (diagnostics).
        int RequestsInFlight(string key)
        {
            int n = 0;
            foreach (var (k, r) in requests) if (r != null && !r.isDone && string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) n++;
            return n;
        }

        string KeyOf(AssetBundle b)
        {
            if (b is null) return null;
            if (keyOfBundle.TryGetValue(b, out var k)) return k;
            return bundles.FirstOrDefault(kv => kv.Value == b).Key;
        }

        // ---------- asset loads and requests ----------

        // A named load from a catalog bundle, through the texture guard; the name is remembered for the reload count.
        T GuardLoad<T>(AssetBundle b, string asset, string label) where T : UnityEngine.Object
        {
            var r = HS2Next.Chara.TextureGuard.Load<T>(b, asset, label, KeyOf);
            Ledger.AssetLoaded(KeyOf(b), asset);
            return r;
        }

        // Asynchronous asset requests in flight: their bundles are never evicted meanwhile.
        readonly List<(string Key, AssetBundleRequest Request)> requests = new List<(string, AssetBundleRequest)>();

        void TrackRequest(AssetBundle b, string asset, AssetBundleRequest r)
        {
            string key = KeyOf(b);
            if (LogBundles) BundleLog($"request {key ?? "(no key)"} '{(b ? b.name : "-")}' asset {asset} done {(r != null && r.isDone)}");
            if (key == null || r == null) return;
            Ledger.AssetLoaded(key, asset);
            if (r.isDone) return;
            Ledger.Busy(key, 1);
            requests.Add((key, r));
        }

        void PruneRequests()
        {
            for (int i = requests.Count - 1; i >= 0; i--)
            {
                var (k, r) = requests[i];
                if (r != null && !r.isDone) continue;
                if (LogBundles) BundleLog($"request done {k}");
                Ledger.Busy(k, -1);
                requests.RemoveAt(i);
            }
        }

        /// <summary>Asynchronous asset requests the catalog started that are still in flight.</summary>
        public int PendingAssetRequests { get { PruneRequests(); return requests.Count; } }

        // ---------- byte accounting ----------

        // Resident bytes: a bundle opened from a byte array holds the array; a deflated zip entry opened synchronously holds its
        // inflated bytes; a file-backed bundle holds its uncompressed size when its blocks are LZMA (decompressed whole), else at
        // most 1 MiB (the other blocks are read from the file on demand). Objects (once an asset was loaded from it): the image
        // chains of its Texture2D objects from the cached texture headers (an upper bound: every texture of the bundle); reported
// and counted for UnloadUnusedAssets, not in the LRU's weight.
        (long Resident, long Objects) MeasureBundle(string key, bool loaded)
        {
            long resident = 0, objects = 0;
            BundleFileRef f = null;
            try { f = BundleFileOfKey(key); } catch (Exception) { }
            if (memoryBacked.TryGetValue(key, out var len)) resident = len;
            else if (f != null)
            {
                if (f.ReadAll != null) resident = f.Length;
                else
                {
                    try
                    {
                        var bf = Hs2BundleFile.OpenFile(f.File, f.Offset, f.Length, key, true);
                        long u = 0;
                        foreach (var n in bf.Nodes) u = Math.Max(u, n.Offset + n.Size);
                        resident = bf.DataCompression == 1 ? u : Math.Min(u, BundleLedger.MiB);
                    }
                    catch (Exception) { resident = Math.Min(f.Length, BundleLedger.MiB); }
                }
            }
            long checkedBytes = -1;
            if (loaded) TextureObjectBytes(key, ref checkedBytes);
            if (checkedBytes >= 0) objects = checkedBytes;
            else if (loaded && f != null && TextureHeaders.TryCached(f, out var h) && h?.ByName != null)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var t in h.ByName.Values)
                    if (seen.Add(t.Name + "|" + t.Width + "x" + t.Height + "|" + t.Format + "|" + t.Mips))
                        objects += TextureHeaders.ChainSize(t.Width, t.Height, t.Format, t.Mips);
            }
            return (resident, objects);
        }

        // ---------- eviction and the upkeep ----------

        bool evictionDue;
        int releasesSeen;
        double lastEvictionPass = double.NegativeInfinity;
        int evictedSinceUnload, destroyedSinceUnload;
        long releasedObjectBytesAtUnload;   // Ledger.ReleasedObjectBytes at the last UnloadUnusedAssets

        /// <summary>
        /// Evicts from the LRU's tail while it weighs more than the budget (<paramref name="budgetMB"/> overrides it when not
        /// negative): each bundle is unloaded without destroying its loaded objects and forgotten by the caches. Never a bundle
        /// with an open, an asset request or a header read in flight, or one opened in this tick (<paramref name="nextTick"/>:
        /// the tick advances first, as at the end of a frame; tests use it). Returns the number evicted.
        /// </summary>
        public int EvictNow(long budgetMB = -1, bool nextTick = false)
        {
            if (nextTick) Ledger.NextTick();
            PruneRequests();
            var keys = Ledger.TakeEvictions(budgetMB < 0 ? -1 : budgetMB * BundleLedger.MiB);
            foreach (var key in keys)
            {
                if (!bundles.TryGetValue(key, out var b)) continue;
                bundles.Remove(key);
                memoryBacked.Remove(key);
                if (!(b is null)) keyOfBundle.Remove(b);
                string rel = PathOfKey(key);
                if (key.IndexOf('!') < 0 && byPath.TryGetValue(rel, out var bp) && bp == b) byPath.Remove(rel);
                if (b)
                {
                    if (LogBundles) BundleLog($"evict {key} '{b.name}' requests in flight {RequestsInFlight(key)}");
                    thumbnailOnly.Remove(b);
                    handedOut.Remove(b);
                    b.Unload(false);
                }
                evictedSinceUnload++;
            }
            return keys.Count;
        }

        /// <summary>
        /// The catalog's upkeep, once per frame from the app (<paramref name="now"/> = seconds of a monotonic clock): after a
        /// release batch or every 5 s, an eviction pass when <paramref name="quiet"/> (no open transaction, drag or capture);
        /// then the tick advances (bundles opened in the frame that ends stay one more tick). With
        /// <paramref name="allowUnloadUnused"/> (play mode and the player, never edit-mode hosts) it also starts
        /// Resources.UnloadUnusedAssets at a quiet point when the policy allows (<see cref="UnloadUnusedDue"/>).
        /// </summary>
        public void Tick(double now, bool quiet, bool allowUnloadUnused)
        {
            PruneRequests();
            FinishUnloadUnused();
            if (pendingOpens.Count > 0) PumpOpens();
            PumpPrefetch();
            if (Ledger.Releases != releasesSeen) { releasesSeen = Ledger.Releases; evictionDue = true; }
            if (quiet && pendingOpens.Count == 0 && (evictionDue || now - lastEvictionPass >= 5))
            {
                evictionDue = false;
                lastEvictionPass = now;
                EvictNow();
            }
            Ledger.NextTick();
            if (allowUnloadUnused && quiet && unloadOp == null && pendingOpens.Count == 0 && requests.Count == 0 && UnloadUnusedDue(now))
                StartUnloadUnused(now);
        }

        // ---------- Resources.UnloadUnusedAssets ----------

        /// <summary>Minimum seconds between two implicit UnloadUnusedAssets calls (settings key content.unloadUnusedMinSec).</summary>
        public double UnloadUnusedMinSec = 30;

        AsyncOperation unloadOp;
        Stopwatch unloadWatch;
        double lastUnloadAt = double.NegativeInfinity;
        readonly List<double> unloadMs = new List<double>();

        /// <summary>UnloadUnusedAssets calls the catalog made or saw, and their durations (ms, from the call to its end).</summary>
        public int UnloadUnusedCalls => unloadMs.Count;
        /// <summary>UnloadUnusedAssets calls the catalog started (counted at the start; <see cref="UnloadUnusedCalls"/> counts
        /// them when they end, a frame or more later): leak checks read it to tell a freed object from a missing one.</summary>
        public int UnloadUnusedStarted { get; private set; }
        public double UnloadUnusedLastMs => unloadMs.Count > 0 ? unloadMs[unloadMs.Count - 1] : 0;
        public double UnloadUnusedMedianMs
        {
            get
            {
                if (unloadMs.Count == 0) return 0;
                var s = unloadMs.OrderBy(x => x).ToList();
                return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
            }
        }
        public double UnloadUnusedMaxMs => unloadMs.Count > 0 ? unloadMs.Max() : 0;

        /// <summary>True when an implicit UnloadUnusedAssets may run now: a realization was destroyed, at least 8 bundles were
        /// evicted or 32 MB of object bytes released since the last call, and the last call is at least
        /// <see cref="UnloadUnusedMinSec"/> ago.</summary>
        public bool UnloadUnusedDue(double now) =>
            now - lastUnloadAt >= UnloadUnusedMinSec
            && (destroyedSinceUnload > 0 || evictedSinceUnload >= 8 || Ledger.ReleasedObjectBytes - releasedObjectBytesAtUnload >= 32 * BundleLedger.MiB);

        void StartUnloadUnused(double now)
        {
            lastUnloadAt = now;
            destroyedSinceUnload = evictedSinceUnload = 0;
            releasedObjectBytesAtUnload = Ledger.ReleasedObjectBytes;
            unloadWatch = Stopwatch.StartNew();
            UnloadUnusedStarted++;
            if (LogBundles) BundleLog($"UnloadUnusedAssets (tick) requests tracked {requests.Count}, pending opens {pendingOpens.Count}, prefetches {prefetches.Count}");
            unloadOp = Resources.UnloadUnusedAssets();
        }

        void FinishUnloadUnused()
        {
            if (unloadOp == null || !unloadOp.isDone) return;
            unloadMs.Add(unloadWatch.Elapsed.TotalMilliseconds);
            unloadOp = null;
            unloadWatch = null;
        }

        /// <summary>
        /// Resources.UnloadUnusedAssets now, for tests and the map's unload (counted like an implicit call; resets the triggers).
        /// The returned operation completes over the next frames (play mode) or at the editor's next update.
        /// </summary>
        public AsyncOperation ReleaseUnused(double now = double.NaN)
        {
            FinishUnloadUnused();
            lastUnloadAt = double.IsNaN(now) ? Time.realtimeSinceStartupAsDouble : now;
            destroyedSinceUnload = evictedSinceUnload = 0;
            releasedObjectBytesAtUnload = Ledger.ReleasedObjectBytes;
            unloadWatch = Stopwatch.StartNew();
            UnloadUnusedStarted++;
            if (LogBundles) BundleLog($"UnloadUnusedAssets (ReleaseUnused) requests tracked {requests.Count}");
            return unloadOp = Resources.UnloadUnusedAssets();
        }

        /// <summary>A call to UnloadUnusedAssets made elsewhere (the map's unload), counted with its duration in ms.</summary>
        public void NoteUnloadUnused(double ms, double now)
        {
            unloadMs.Add(ms);
            lastUnloadAt = now;
            destroyedSinceUnload = evictedSinceUnload = 0;
            releasedObjectBytesAtUnload = Ledger.ReleasedObjectBytes;
        }
    }
}

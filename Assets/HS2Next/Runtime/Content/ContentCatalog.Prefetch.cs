// Requests ahead of the edit (the Maker's lists): when a part cell has been hovered or keyboard-focused for a moment, the list
// asks the catalog to prefetch the row: the bundle that serves the row's MainData opens asynchronously (zip entries read and
// decompressed off the main thread). The asset itself is not requested ahead (PrefetchAssets, off): Unity 6's asynchronous
// asset load resolves its bundle by the bundle's build name (m_AssetBundleName), which mods share freely (every bundle of
// one XeyNiu zipmod is "XeyNiu Mods.unity3d"; two asslie shoes bundles share a vanilla thumbnail name), so a prefetched prefab
// was read through another open bundle of the same name and the player crashed integrating it (5.2 Maker session: the engine
// logs "AssetBundle 'XeyNiu Mods.unity3d' was already unloaded." for an open bundle whose namesake had been unloaded). The
// edit itself does not change: its flush loads the asset synchronously as before. Prefetched bundles belong to the owner "maker:prefetch" until another owner (the edit's unit)
// uses them or 30 s passed; at most 4 prefetches are in flight.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Content
{
    public partial class ContentCatalog
    {
        /// <summary>Prefetches in flight at most.</summary>
        public const int MaxPrefetchInFlight = 4;

        /// <summary>Seconds a prefetched bundle stays owned by the prefetch when nothing else uses it (tests shorten it).</summary>
        public static double PrefetchKeepSec = 30;

        sealed class PrefetchJob
        {
            public string Id, Asset, Key;
            public BundleOpen Open;
            public AssetBundleRequest Request;
            public double Started;
            public bool Claimed;
        }

        readonly List<PrefetchJob> prefetches = new List<PrefetchJob>();
        BundleOwner prefetchOwner;
        BundleOwner asyncOwner;   // the owner of the asynchronous opens started now (null: the thumbnails')

        /// <summary>The owner of prefetched bundles ("maker:prefetch").</summary>
        public BundleOwner PrefetchOwner => prefetchOwner != null && prefetchOwner.Live ? prefetchOwner : (prefetchOwner = Ledger.NewOwner("maker:prefetch"));

        /// <summary>Prefetches started, asset requests started, and prefetches whose bundle an edit then used.</summary>
        public int PrefetchStarted { get; private set; }
        public int PrefetchAssetRequests { get; private set; }
        public int PrefetchUsed { get; private set; }
        /// <summary>Prefetches that ended unused (kept <see cref="PrefetchKeepSec"/> without another owner, or their bundle went),
        /// prefetches whose asset the pre-load check refused (the bundle opened, no request), and requests not started because
        /// <see cref="MaxPrefetchInFlight"/> were in flight.</summary>
        public int PrefetchExpired { get; private set; }
        public int PrefetchRefused { get; private set; }
        public int PrefetchLimited { get; private set; }

        /// <summary>Prefetches not finished (opening, or requesting the asset).</summary>
        public int PrefetchInFlight { get { int n = 0; foreach (var j in prefetches) if (!j.Claimed || j.Request != null && !j.Request.isDone) n++; return n; } }

        partial void TextureCheckPresent(ref bool present);

        /// <summary>The prefetch also requests the asset asynchronously (off; environment HS2NEXT_PREFETCH_ASSETS=1 turns it on
        /// for diagnostics: see the file header).</summary>
        public static bool PrefetchAssets = Environment.GetEnvironmentVariable("HS2NEXT_PREFETCH_ASSETS") == "1";

        /// <summary>True when asset requests may run ahead of the edit (<see cref="PrefetchAssets"/> and the pre-upload texture
        /// check is there).</summary>
        public bool AssetPrefetchAllowed
        {
            get { if (!PrefetchAssets) return false; bool on = false; TextureCheckPresent(ref on); return on; }
        }

        /// <summary>
        /// Prefetches entry[<paramref name="assetKey"/>] from entry[<paramref name="bundleKey"/>] (see the file header); false when
        /// the row names no bundle or asset, the same row is already prefetched, or <see cref="MaxPrefetchInFlight"/> are in
        /// flight. Main thread.
        /// </summary>
        public bool Prefetch(ListEntry e, string bundleKey = "MainAB", string assetKey = "MainData")
        {
            string path = e?[bundleKey], asset = e?[assetKey];
            if (string.IsNullOrEmpty(path) || path == "0" || string.IsNullOrEmpty(asset) || asset == "0") return false;
            string id = e.Category + "|" + e.Key + "|" + bundleKey + "|" + assetKey;
            foreach (var j in prefetches) if (j.Id == id) return false;
            if (PrefetchInFlight >= MaxPrefetchInFlight) { PrefetchLimited++; return false; }
            BundleOpen open;
            var prev = asyncOwner;
            asyncOwner = PrefetchOwner;
            try { open = OpenBundleAsync(e, path, asset); }
            finally { asyncOwner = prev; }
            if (open == null || open.Loose || (open.IsDone && !open.Bundle)) return false;
            if (LogBundles) BundleLog($"prefetch start {e.Zipmod?.Guid ?? "abdata"} {path} asset {asset} open done {open.IsDone}");
            prefetches.Add(new PrefetchJob { Id = id, Asset = asset, Open = open, Started = Time.realtimeSinceStartupAsDouble });
            PrefetchStarted++;
            PumpPrefetch();
            return true;
        }

        /// <summary>Advances the prefetches (main thread, once per frame while any is there): finished opens get their asset
        /// request; a prefetched bundle another owner uses, or that waited <see cref="PrefetchKeepSec"/>, is left to its owners.</summary>
        public void PumpPrefetch()
        {
            if (prefetches.Count == 0) return;
            PumpOpens();
            double now = Time.realtimeSinceStartupAsDouble;
            for (int i = prefetches.Count - 1; i >= 0; i--)
            {
                var j = prefetches[i];
                if (!j.Claimed)
                {
                    if (!j.Open.IsDone) continue;
                    var b = j.Open.Bundle;
                    if (!b) { prefetches.RemoveAt(i); continue; }
                    j.Claimed = true;
                    j.Key = KeyOf(b);
                    if (j.Key != null)
                    {
                        // a thumbnail's open of the same bundle: no longer thumbnail-only (an edit is likely to use it)
                        thumbnailOnly.Remove(b);
                        Ledger.Use(j.Key, PrefetchOwner);
                        Ledger.Disown(j.Key, Ledger.Thumbs);
                    }
                    bool refused = AssetPrefetchAllowed && IsAssetRefused(b, j.Asset);   // a refused asset never reaches an asynchronous load
                    if (LogBundles) BundleLog($"prefetch claim {j.Key} '{b.name}' asset {j.Asset} refused {refused} request {AssetPrefetchAllowed && !refused}");
                    if (refused) PrefetchRefused++;
                    if (AssetPrefetchAllowed && !refused)
                    {
                        j.Request = b.LoadAssetAsync(j.Asset);
                        TrackRequest(b, j.Asset, j.Request);
                        PrefetchAssetRequests++;
                    }
                }
                bool usedElsewhere = false;
                if (j.Key != null)
                    foreach (var o in Ledger.OwnersOf(j.Key))
                        if (o != PrefetchOwner.Label && o != Ledger.Thumbs.Label) { usedElsewhere = true; break; }
                // a request still in flight keeps its bundle busy (the catalog tracks it); only an unused one waits for it
                if (!usedElsewhere && j.Request != null && !j.Request.isDone) continue;
                if (usedElsewhere || now - j.Started >= PrefetchKeepSec || j.Key == null || !Ledger.Knows(j.Key))
                {
                    if (usedElsewhere) PrefetchUsed++;
                    else PrefetchExpired++;
                    if (LogBundles) BundleLog($"prefetch end {j.Key} used {usedElsewhere} request done {(j.Request == null ? "-" : j.Request.isDone.ToString())}");
                    if (j.Key != null) Ledger.Disown(j.Key, PrefetchOwner);
                    prefetches.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// As <see cref="LoadBundle(ZipmodInfo, string, string)"/> with the bundle itself opened asynchronously when it is a zipmod
        /// copy not open yet (a map's bundle: LZMA map bundles otherwise decompress on the main thread for tens of seconds); the
        /// dependencies load at once. The open is recorded under <paramref name="bundleOwner"/>; call <see cref="PumpOpens"/>
        /// every frame until it is done (Tick does in play mode).
        /// </summary>
        public BundleOpen LoadBundleAsync(ZipmodInfo owner, string bundlePath, string manifest, BundleOwner bundleOwner)
        {
            if (string.IsNullOrEmpty(bundlePath) || bundlePath == "0") return new BundleOpen { IsDone = true };
            bundlePath = bundlePath.Replace('\\', '/');
            PreloadDependencies(owner, bundlePath, manifest);
            if (HasRealBundle(bundlePath)) return new BundleOpen { IsDone = true, Bundle = LoadAbdataBundle(bundlePath) };
            var z = owner != null && owner.HasBundle(bundlePath) ? owner : FindZipmodShipping(bundlePath);
            if (z == null) { LogOnce($"bundle not found: {bundlePath}"); return new BundleOpen { IsDone = true }; }
            var prev = asyncOwner;
            asyncOwner = bundleOwner ?? Ledger.Current ?? Ledger.Unscoped;
            try { return OpenAsync(z, bundlePath, () => OpenZipCopy(z, bundlePath)); }
            finally { asyncOwner = prev; }
        }

        /// <summary>Completes the prefetches' asset requests still in flight now (blocking; main thread): before a shutdown, and
        /// in edit-mode tests where no frame advances them. Returns the number completed.</summary>
        public int CompletePrefetchRequests()
        {
            int n = 0;
            foreach (var j in prefetches)
                if (j.Request != null && !j.Request.isDone) { _ = j.Request.asset; n++; }
            PruneRequests();
            return n;
        }

        // An open that is done at once, with the owner of the asynchronous opens started now: the thumbnails' (marked
        // thumbnail-only, today's rule) or the prefetch's.
        BundleOpen SyncOpen(Func<AssetBundle> open)
        {
            if (asyncOwner == null)
            {
                thumbnailOpens++;
                try { return new BundleOpen { IsDone = true, Bundle = open() }; }
                finally { thumbnailOpens--; }
            }
            using (Own(asyncOwner)) return new BundleOpen { IsDone = true, Bundle = open() };
        }
    }
}

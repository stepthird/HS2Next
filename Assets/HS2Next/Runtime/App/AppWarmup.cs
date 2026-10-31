// The app's asset warm-up (play sessions and players; edit-mode test hosts never call it). Switching the hair's draw type reads
// the shader of one of two materials of one vanilla bundle (HairMaterials). The first load of such a material in a process costs
// 20-25 ms on the main thread, and so does every load after an unused-asset sweep dropped it: once the hair's own materials took
// its shader, nothing references the source material any more. After the zipmod scan (so that the per-asset redirect is known)
// the app starts asynchronous loads of both materials under a pin owner, so the catalog never releases their bundle, and keeps
// the loaded materials referenced for the life of that catalog; a later switch finds them loaded. The loads do not hold up the
// start-up and change no pixel (the same materials, loaded earlier). The command-line flag -noWarmup turns the warm-up off (for
// before / after measurements).
using System.Collections.Generic;
using System.Diagnostics;
using HS2Next.Chara.Edit;
using HS2Next.Content;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace HS2Next.App
{
    public static class AppWarmup
    {
        /// <summary>The reason of the warm-up's pin owner in the catalog's ledger.</summary>
        public const string PinReason = "warm-up";

        static ContentCatalog started;
        static readonly List<Material> kept = new List<Material>();
        static int waiting, startFrame;
        static Stopwatch watch;

        /// <summary>Materials the warm-up of the current catalog loaded and keeps.</summary>
        public static int Kept => kept.Count;

        /// <summary>Loads of the current catalog's warm-up still in flight.</summary>
        public static int Pending => waiting;

        /// <summary>
        /// Starts the warm-up loads for <paramref name="catalog"/> (once per catalog; main thread). The loads complete on their
        /// own; one log line reports the materials kept and the time when the last one completes. Never throws.
        /// </summary>
        public static void Start(ContentCatalog catalog)
        {
            if (catalog == null || ReferenceEquals(catalog, started)) return;
            started = catalog;
            kept.Clear();
            waiting = 0;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-noWarmup") >= 0)
            {
                Debug.Log("[app] warm-up: off (-noWarmup)");
                return;
            }
            watch = Stopwatch.StartNew();
            startFrame = Time.frameCount;
            var requests = new List<AssetBundleRequest>();
            try
            {
                using (catalog.Own(catalog.Ledger.Pin(PinReason)))
                    for (int type = 0; type < 2; type++)
                    {
                        var r = catalog.LoadAssetAsync<Material>(HairMaterials.ShaderBundle, HairMaterials.ShaderMaterial(type));
                        if (r != null) requests.Add(r);
                    }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[app] warm-up: " + e.GetType().Name + ": " + e.Message);
            }
            if (requests.Count == 0)
            {
                Debug.Log("[app] warm-up: no hair shader material to load");
                return;
            }
            waiting = requests.Count;
            var mine = catalog;
            foreach (var request in requests)
            {
                var r = request;
                r.completed += _ =>
                {
                    if (!ReferenceEquals(mine, started)) return;   // a later catalog started its own warm-up
                    if (r.asset is Material m)
                    {
                        kept.Add(m);
                        MaterialCopies.Capture(m);   // as a synchronous load records the material's keywords
                    }
                    if (--waiting == 0)
                        Debug.Log($"[app] warm-up: {kept.Count} hair shader materials loaded and kept in {watch.ElapsedMilliseconds} ms over {Time.frameCount - startFrame} frames, asynchronously (bundle pinned)");
                };
            }
        }
    }
}

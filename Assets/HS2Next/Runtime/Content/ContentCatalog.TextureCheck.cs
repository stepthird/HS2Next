// The pre-upload texture check at the catalog's open points (Runtime/Content/TextureCheck.cs). Every bundle the catalog opens
// gets a texture verdict before Unity reads it: from the verdict cache of this machine (bundle key and source stamp), else
// computed from the bytes the open would read, on the calling thread (a first check costs about a millisecond for a typical
// LZ4 bundle; LZMA bundles are decoded whole, once per machine).
//   ok / unreadable  the open goes ahead unchanged (unreadable: a layout the reader does not know; the guard after the
//                    load still applies).
//   patch            the defective textures are patched; the patched bundle is written once to the repair cache
//                    (<cache>/texguard/bundles + repaired.tsv, the layout the guard already reads) and opened from that
//                    file, so every later session opens it as a repaired copy; when the cache cannot be written it is
//                    opened from memory. Loads from it report the repaired textures as the guard reports a repaired copy.
//   malformed        the bundle is refused (every load from it returns null) with the finding texture.malformedBundle.
// A deflated zip entry is inflated once inside the open (its bytes are checked in memory); an asynchronous open whose
// verdict is not known yet, or needs a patch, becomes a synchronous open. The check is the product's safety: there is no
// switch; TextureGuard.Enabled (tests only) turns it off together with the guard.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HS2Next.Chara;
using UnityEngine;

namespace HS2Next.Content
{
    /// <summary>The texture check's counts of one catalog (content.stats textureCheck).</summary>
    public sealed class TextureCheckCounts
    {
        /// <summary>Verdicts computed here, verdicts taken from the cache, bundles opened patched, bundles refused, bundles the
        /// reader could not parse (opened unchanged).</summary>
        public int Verified, CacheHits, Patched, Refused, Unreadable;
        /// <summary>Opens that read a stored repaired copy (patched at an open of an earlier session, or by the repair tool):
        /// their textures are repaired although this session patched nothing (Patched counts this session's patches only).</summary>
        public int RepairedOpens;
        /// <summary>Checks running on a worker (the background scan), and that scan's progress: entries handled of all
        /// entries (bundles and the other files of a zipmod's abdata), bundles checked, entries that are not bundles, and
        /// bundles left to the check at their open (LZMA, or deflated over the scan's limit).</summary>
        public int Pending, PrescanDone, PrescanTotal, PrescanChecked, PrescanNotBundles, PrescanLeft;
    }

    /// <summary>One finding of the texture check: kind (texture.malformedBundle, texture.patched), the bundle's key and source label.</summary>
    public sealed class TextureFinding
    {
        public string Kind, Key, Source, Detail;
        public override string ToString() => $"{Kind}: {Source}: {Detail}";
    }

    public partial class ContentCatalog
    {
        static TextureVerdictCache verdictCache;
        static string verdictCacheRoot;
        static readonly object verdictGate = new object();

        /// <summary>The texture check's counts of this catalog.</summary>
        public readonly TextureCheckCounts TextureCheckStats = new TextureCheckCounts();

        /// <summary>The texture check's findings of this catalog (each bundle once), oldest first.</summary>
        public readonly List<TextureFinding> TextureFindings = new List<TextureFinding>();
        readonly HashSet<string> textureFindingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The verdict cache of the content cache folder (<see cref="TextureGuard.CacheRoot"/>; its file is read on first use
        /// and again when the folder changes). Process-wide: every catalog and worker shares it.
        /// </summary>
        public static TextureVerdictCache TextureVerdicts
        {
            get
            {
                string root = TextureGuard.CacheRoot;
                lock (verdictGate)
                {
                    if (verdictCache == null || !string.Equals(root, verdictCacheRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        var c = new TextureVerdictCache(root);
                        c.Load();
                        verdictCache = c;
                        verdictCacheRoot = root;
                    }
                    return verdictCache;
                }
            }
        }

        /// <summary>
        /// The texture verdict of a bundle file: the cached one when its key and stamp match, else computed now (and cached).
        /// Any thread. Null for a null file.
        /// </summary>
        public static TextureVerdict TextureVerdictOf(BundleFileRef f)
        {
            if (f?.Key == null) return null;
            var cache = TextureVerdicts;
            if (cache.TryGet(f.Key, f.Stamp, out var v)) return v;
            v = TextureCheck.Analyze(f);
            cache.Add(v);
            return v;
        }

        /// <summary>The bytes the textures of a bundle hold once loaded (the sum of their declared mip chains), from the cached
        /// verdict of the bytes an open of <paramref name="key"/> reads; 0 when no verdict is known.</summary>
        public long TextureBytesOf(string key)
        {
            var f = key != null ? BundleFileOfKey(key) : null;
            return f != null && TextureVerdicts.TryGet(key, f.Stamp, out var v) ? v.TextureBytes : 0;
        }

        // content.stats: the check's numbers (and, with the crash quarantine, its entries)
        partial void FillContentStats(ContentStatsExtra x)
        {
            x.Verified = TextureCheckStats.Verified;
            x.Patched = TextureCheckStats.Patched;
            x.Refused = TextureCheckStats.Refused;
            x.CacheHits = TextureCheckStats.CacheHits;
            x.Pending = TextureCheckStats.Pending;
            x.PrescanDone = TextureCheckStats.PrescanDone;
            x.PrescanTotal = TextureCheckStats.PrescanTotal;
            x.RepairedOpens = TextureCheckStats.RepairedOpens;
            if (Quarantine != null) foreach (var e in Quarantine.Entries) x.Quarantined.Add((e.Key, e.Stamp));
        }

        // the bytes a bundle's loaded textures hold, from its cached verdict (left as it is when unknown)
        partial void TextureObjectBytes(string key, ref long bytes)
        {
            long b = TextureBytesOf(key);
            if (b > 0) bytes = b;
        }

        // every open passes the check (while the guard is on), so asset requests may run ahead of an edit
        partial void TextureCheckPresent(ref bool present) => present = TextureGuard.Enabled;

        // ---------------------------------------------------------------- assets that need a truncated mesh

        // the assets the check refuses (a truncated mesh in their preload range): bundle key -> container paths and short
        // names; process-wide, filled whenever a verdict is taken at an open
        static readonly Dictionary<string, HashSet<string>> refusedAssets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        static void NoteRefusals(TextureVerdict v)
        {
            if (v?.Key == null || v.RefusedAssets.Count == 0) return;
            TextureGuard.RefuseLoad = RefuseAssetLoad;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in v.RefusedAssets) { set.Add(a); set.Add(BundleNames.ShortName(a)); }
            lock (refusedAssets) refusedAssets[v.Key] = set;
        }

        static bool RefuseAssetLoad(AssetBundle b, string asset, Func<AssetBundle, string> keyOf)
        {
            if (asset == null || keyOf == null) return false;
            lock (refusedAssets) if (refusedAssets.Count == 0) return false;
            string key = keyOf(b);
            HashSet<string> set;
            lock (refusedAssets) if (key == null || !refusedAssets.TryGetValue(key, out set)) return false;
            if (!set.Contains(asset) && !set.Contains(BundleNames.ShortName(asset))) return false;
            (keyOf.Target as ContentCatalog)?.AddTextureFinding("content.meshTruncated", key, asset + ": refused (a mesh it needs ends before its data do; loading it would end the process)", warning: true, id: asset);
            return true;
        }

        /// <summary>True when a load of <paramref name="asset"/> from <paramref name="bundle"/> is refused (a truncated mesh in
        /// its preload range); for loads that do not pass the catalog's synchronous path (asynchronous asset requests).</summary>
        public bool IsAssetRefused(AssetBundle bundle, string asset) => bundle && RefuseAssetLoad(bundle, asset, KeyOf);

        partial void OnBundleOpening(string key, ref Func<AssetBundle> open)
        {
            if (key == null || open == null) return;
            EndStaleFirstUseMark();
            System.Threading.Volatile.Write(ref lastOpenTick, Environment.TickCount);
            if (RefusedByQuarantine(key)) { open = null; return; }
            if (!TextureGuard.Enabled) return;
            var f = BundleFileOfKey(key);
            if (f == null) return;
            if (TextureGuard.RepairedPath(key) != null) TextureCheckStats.RepairedOpens++;
            var cache = TextureVerdicts;
            if (cache.TryGet(key, f.Stamp, out var v)) TextureCheckStats.CacheHits++;
            else if (f.ReadAll != null)
            {
                // a deflated zip entry: inflated once inside the open, checked in memory
                open = () => OpenInflated(key, f);
                return;
            }
            else
            {
                v = TextureCheck.Analyze(f);
                cache.Add(v);
                TextureCheckStats.Verified++;
            }
            NoteRefusals(v);
            switch (v.Kind)
            {
                case TextureVerdictKind.Ok:
                    return;
                case TextureVerdictKind.Unreadable:
                    NoteUnreadable(key, v);
                    return;
                case TextureVerdictKind.Malformed:
                    Refuse(key, v);
                    open = null;
                    return;
            }
            byte[] patched;
            List<TextureDefect> actions;
            try
            {
                var b = f.ReadAll != null ? Hs2BundleFile.FromBytes(f.ReadAll(), key, lzma: true) : Hs2BundleFile.OpenFile(f.File, f.Offset, f.Length, key, lzma: true);
                patched = TextureCheck.Patch(b, out actions);
            }
            catch (Exception e) { NotePatchFailed(key, e); return; }
            if (patched == null) return;
            open = OpenPatched(key, patched, actions, store: true);
        }

        partial void OnBundleBytes(string key, ref byte[] bytes)
        {
            if (key == null || bytes == null) return;
            if (RefusedByQuarantine(key)) { bytes = null; return; }
            if (!TextureGuard.Enabled) return;
            var f = BundleFileOfKey(key);
            var v = VerdictOfBytes(key, f?.Stamp, bytes);
            switch (v.Kind)
            {
                case TextureVerdictKind.Ok:
                    return;
                case TextureVerdictKind.Unreadable:
                    NoteUnreadable(key, v);
                    return;
                case TextureVerdictKind.Malformed:
                    Refuse(key, v);
                    bytes = null;
                    return;
            }
            try
            {
                // these bytes may be a renamed copy (a refused open's rewrite): patched in memory, not stored
                var patched = TextureCheck.Patch(Hs2BundleFile.FromBytes(bytes, key, lzma: true), out var actions);
                if (patched == null) return;
                NotePatched(key, actions, null);
                bytes = patched;
            }
            catch (Exception e) { NotePatchFailed(key, e); }
        }

        // the verdict of in-memory bytes of a key (cached by the key and the stamp of its file when there is one)
        TextureVerdict VerdictOfBytes(string key, string stamp, byte[] bytes)
        {
            var cache = TextureVerdicts;
            if (stamp != null && cache.TryGet(key, stamp, out var v)) { TextureCheckStats.CacheHits++; NoteRefusals(v); return v; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { v = TextureCheck.Analyze(Hs2BundleFile.FromBytes(bytes, key, lzma: true)); }
            catch (Exception e) { v = new TextureVerdict { Kind = TextureVerdictKind.Unreadable, Error = e.GetType().Name + ": " + e.Message }; }
            v.Key = key;
            v.Stamp = stamp;
            v.Ms = sw.Elapsed.TotalMilliseconds;
            TextureCheckStats.Verified++;
            if (stamp != null) cache.Add(v);
            NoteRefusals(v);
            return v;
        }

        // a deflated zip entry: inflate, check, open (patched or not) from the bytes
        AssetBundle OpenInflated(string key, BundleFileRef f)
        {
            byte[] bytes;
            try { bytes = f.ReadAll(); }
            catch (Exception) { return null; }
            if (bytes == null) return null;
            var v = VerdictOfBytes(key, f.Stamp, bytes);
            if (v.Kind == TextureVerdictKind.Malformed) { Refuse(key, v); return null; }
            if (v.Kind == TextureVerdictKind.Unreadable) NoteUnreadable(key, v);
            if (v.NeedsPatch)
            {
                try
                {
                    var patched = TextureCheck.Patch(Hs2BundleFile.FromBytes(bytes, key, lzma: true), out var actions);
                    if (patched != null) return OpenPatched(key, patched, actions, store: true)();
                }
                catch (Exception e) { NotePatchFailed(key, e); }
            }
            var b = AssetBundle.LoadFromMemory(bytes);
            if (b) NoteMemoryBacked(key, bytes.LongLength);
            return b;
        }

        // the open of a patched bundle: from its repair-cache file when it could be written there, else from memory
        Func<AssetBundle> OpenPatched(string key, byte[] patched, List<TextureDefect> actions, bool store)
        {
            string label = SourceLabelOfKey(key);
            string actionsText = string.Join("|", actions.ConvertAll(a => TextureVerdict.Clean(a.Texture) + "=" + a.Action));
            // (tests that ask for the original bundles - TextureGuard.UseRepairedBundles off - get the patch in memory only)
            string path = store && TextureGuard.UseRepairedBundles ? TextureRepairCache.Store(TextureGuard.CacheRoot, key, label, patched, actionsText) : null;
            NotePatched(key, actions, path);
            TextureGuard.NoteRepaired(key, path, label, actionsText);
            if (path != null) return () => AssetBundle.LoadFromFile(path);
            return () =>
            {
                var b = AssetBundle.LoadFromMemory(patched);
                if (b) NoteMemoryBacked(key, patched.LongLength);
                return b;
            };
        }

        // "<zipmod GUID>:<bundle path>" or "abdata:<bundle path>" (the guard's source labels)
        string SourceLabelOfKey(string key)
        {
            int bang = key.IndexOf('!');
            if (bang >= 0)
            {
                string zip = key.Substring(0, bang);
                foreach (var z in loadedInOrder)
                    if (string.Equals(z.Path, zip, StringComparison.OrdinalIgnoreCase)) return Label(z, key.Substring(bang + 1));
                foreach (var z in ZipmodsByGuid.Values)
                    if (string.Equals(z.Path, zip, StringComparison.OrdinalIgnoreCase)) return Label(z, key.Substring(bang + 1));
                return Label(null, key.Substring(bang + 1));
            }
            return Label(null, PathOfKey(key));
        }

        void NotePatched(string key, List<TextureDefect> actions, string path)
        {
            TextureCheckStats.Patched++;
            string detail = string.Join(", ", actions.ConvertAll(a => a.Texture + " " + a.Action)) + (path != null ? " (repair cache)" : " (in memory)");
            AddTextureFinding("texture.patched", key, detail, warning: false);
        }

        void Refuse(string key, TextureVerdict v)
        {
            TextureCheckStats.Refused++;
            AddTextureFinding("texture.malformedBundle", key, v.Error ?? "malformed", warning: true);
        }

        void NoteUnreadable(string key, TextureVerdict v)
        {
            if (textureFindingKeys.Contains("unreadable|" + key)) return;
            textureFindingKeys.Add("unreadable|" + key);
            TextureCheckStats.Unreadable++;
            Log.Add($"texture check: {key}: not readable ({v.Error}); opened unchanged");
        }

        void NotePatchFailed(string key, Exception e)
        {
            AddTextureFinding("texture.patchFailed", key, e.GetType().Name + ": " + e.Message, warning: true);
        }

        void AddTextureFinding(string kind, string key, string detail, bool warning, string id = null)
        {
            if (!textureFindingKeys.Add(kind + "|" + key + (id != null ? "|" + id : ""))) return;
            var t = new TextureFinding { Kind = kind, Key = key, Source = SourceLabelOfKey(key), Detail = detail };
            TextureFindings.Add(t);
            Log.Add(t.ToString());
            if (warning) Debug.LogWarning("[content] " + t);
            else Debug.Log("[content] " + t);
        }

        // ---------------------------------------------------------------- the crash quarantine (CrashQuarantine.cs)

        /// <summary>The crash quarantine of this catalog (null: off; test hosts never have one unless they enable it).</summary>
        public CrashQuarantine Quarantine { get; private set; }

        readonly HashSet<AssetBundle> loadMarked = new HashSet<AssetBundle>();
        AssetBundle loadInFlight;
        int markFrame = -1;
        static ContentCatalog markOwner;
        static bool frameEndHooked;
        readonly Dictionary<string, string> zipStamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static string testCrashOnLoad;

        /// <summary>
        /// Turns the crash quarantine on with the folder of <paramref name="userData"/> (&lt;UserData&gt;/content): reads its
        /// list and the marker the previous process left; returns the entry that marker added (null: the last run ended
        /// normally). From now on the first asset load of each zipmod bundle is marked, and the mark stays while the frame
        /// uses what the load returned (an item's or a part's realization from a freshly loaded prefab: a native crash there
        /// names the bundle too); it ends at the end of the frame, when another bundle's first load begins, or at quit. Loads
        /// from a quarantined bundle are refused. Test builds take -testCrashOnLoad &lt;key or path&gt;: the process crashes at
        /// that load.
        /// </summary>
        public QuarantineEntry EnableQuarantine(string userData)
        {
            DisableQuarantine();
            Quarantine = CrashQuarantine.Open(userData);
            if (Quarantine == null) return null;
            TextureGuard.BeforeLoad += OnBeforeAssetLoad;
            TextureGuard.AfterLoad += OnAfterAssetLoad;
            markOwner = this;
            HookFrameEnd();
            Application.quitting += OnQuitting;
            if (Application.isEditor || Debug.isDebugBuild)
            {
                var a = Environment.GetCommandLineArgs();
                int i = Array.IndexOf(a, "-testCrashOnLoad");
                testCrashOnLoad = i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
            }
            var last = Quarantine.FromLastRun;
            if (last != null)
                AddTextureFinding("content.quarantined", last.Key, "the previous run ended while it loaded this bundle; its loads are refused until the mod changes", warning: true);
            return last;
        }

        /// <summary>Turns the quarantine off (the marker is emptied; the list stays).</summary>
        public void DisableQuarantine()
        {
            if (Quarantine == null) return;
            TextureGuard.BeforeLoad -= OnBeforeAssetLoad;
            TextureGuard.AfterLoad -= OnAfterAssetLoad;
            Application.quitting -= OnQuitting;
            if (ReferenceEquals(markOwner, this)) { markOwner = null; UnhookFrameEnd(); }
            Quarantine.Dispose();
            Quarantine = null;
            loadMarked.Clear();
            loadInFlight = null;
            markFrame = -1;
        }

        /// <summary>Lifts a quarantined bundle (by its key, or its key without the zipmod's folders); true when one was lifted.</summary>
        public bool ClearQuarantine(string key) => Quarantine != null && Quarantine.Clear(key);

        /// <summary>
        /// Marks a scene load from <paramref name="bundle"/> as in flight (a map's scene bundle; call <see cref="EndLoadMark"/>
        /// when it returns). No-op without the quarantine or for base-game bundles.
        /// </summary>
        public void BeginLoadMark(AssetBundle bundle)
        {
            if (Quarantine == null || !bundle) return;
            string key = KeyOf(bundle);
            if (!CrashQuarantine.IsZipmodKey(key)) return;
            Quarantine.Enter(key, ZipStampOf(key));
            loadInFlight = bundle;
            markFrame = -1;   // a scene load's mark ends with EndLoadMark
            CrashIfAsked(key);
        }

        /// <summary>The load marked by <see cref="BeginLoadMark"/> returned.</summary>
        public void EndLoadMark()
        {
            if (Quarantine == null || !loadInFlight) return;
            Quarantine.Exit();
            loadInFlight = null;
        }

        /// <summary>
        /// Ends the mark of a bundle's first asset load and its first use (the marker's first byte becomes zero). Called at
        /// the end of every frame (the player loop), at quit, and in tests; a mark older than the current frame also ends at
        /// the catalog's next open.
        /// </summary>
        public void EndFirstUseMark()
        {
            if (Quarantine == null || markFrame < 0) return;   // (an unloaded bundle compares equal to null: the frame decides)
            Quarantine.Exit();
            loadInFlight = null;
            markFrame = -1;
        }

        void EndStaleFirstUseMark()
        {
            if (markFrame >= 0 && markFrame != Time.frameCount) EndFirstUseMark();
        }

        // quit (in the Editor also the end of play mode): the mark ends and the end-of-frame system leaves the player loop
        void OnQuitting()
        {
            EndFirstUseMark();
            if (ReferenceEquals(markOwner, this)) UnhookFrameEnd();
        }

        // the end of each frame: a system appended to the player loop's PostLateUpdate (kept by later player loop changes that
        // start from the current loop); a system of the same type name left by an earlier load of the scripts is replaced
        struct QuarantineFrameEnd { }

        static void HookFrameEnd()
        {
            if (frameEndHooked) return;
            try
            {
                if (EditFrameEnd(true)) frameEndHooked = true;
            }
            catch (Exception e) { Debug.LogWarning("[content] crash quarantine: no end-of-frame hook (" + e.GetType().Name + ": " + e.Message + "); marks end at the next open"); }
        }

        static void UnhookFrameEnd()
        {
            if (!frameEndHooked) return;
            frameEndHooked = false;
            try { EditFrameEnd(false); }
            catch (Exception e) { Debug.LogWarning("[content] crash quarantine: the end-of-frame hook stays (" + e.GetType().Name + ": " + e.Message + ")"); }
        }

        // removes the systems named like QuarantineFrameEnd from PostLateUpdate and, when add is set, appends one; true when the
        // player loop has that phase
        static bool EditFrameEnd(bool add)
        {
            var loop = UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop();
            if (loop.subSystemList == null) return false;
            string name = typeof(QuarantineFrameEnd).FullName;
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != typeof(UnityEngine.PlayerLoop.PostLateUpdate)) continue;
                var phase = loop.subSystemList[i];
                var list = new List<UnityEngine.LowLevel.PlayerLoopSystem>();
                foreach (var sys in phase.subSystemList ?? new UnityEngine.LowLevel.PlayerLoopSystem[0])
                    if (sys.type == null || sys.type.FullName != name) list.Add(sys);
                if (add) list.Add(new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(QuarantineFrameEnd), updateDelegate = () => markOwner?.EndFirstUseMark() });
                phase.subSystemList = list.ToArray();
                loop.subSystemList[i] = phase;
                UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(loop);
                return true;
            }
            return false;
        }

        // the stamp of a zipmod copy's source file ("size:mtime ticks"; the quarantine lifts an entry when it changes)
        string ZipStampOf(string key)
        {
            int bang = key.IndexOf('!');
            string zip = bang > 0 ? key.Substring(0, bang) : key;
            if (zipStamps.TryGetValue(zip, out var st)) return st;
            var fi = new FileInfo(zip);
            st = fi.Exists ? fi.Length.ToString(CultureInfo.InvariantCulture) + ":" + fi.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) : "";
            zipStamps[zip] = st;
            return st;
        }

        // the quarantine's part of an open: a quarantined zipmod copy is refused
        bool RefusedByQuarantine(string key)
        {
            if (Quarantine == null || !CrashQuarantine.IsZipmodKey(key)) return false;
            if (!Quarantine.IsQuarantined(key, ZipStampOf(key))) return false;
            AddTextureFinding("content.quarantined", key, "refused: the process ended while it loaded this bundle (content.quarantine clears it)", warning: true);
            return true;
        }

        void OnBeforeAssetLoad(AssetBundle b)
        {
            if (Quarantine == null || !b || loadMarked.Contains(b)) return;
            string key = KeyOf(b);
            if (key == null) return;   // another catalog's bundle
            loadMarked.Add(b);
            if (!CrashQuarantine.IsZipmodKey(key)) return;
            Quarantine.Enter(key, ZipStampOf(key));   // replaces an earlier bundle's mark
            loadInFlight = b;
            markFrame = Time.frameCount;
            CrashIfAsked(key);
        }

        // the load returned: the mark stays for the first use of what it returned (EndFirstUseMark ends it with the frame)
        void OnAfterAssetLoad(AssetBundle b) { }

        static void CrashIfAsked(string key)
        {
            if (testCrashOnLoad == null || key == null) return;
            if (!string.Equals(key, testCrashOnLoad, StringComparison.OrdinalIgnoreCase) && !key.EndsWith("!" + testCrashOnLoad, StringComparison.OrdinalIgnoreCase)) return;
            Debug.LogWarning("[content] -testCrashOnLoad: crashing at the load of " + key);
            UnityEngine.Diagnostics.Utils.ForceCrash(UnityEngine.Diagnostics.ForcedCrashCategory.AccessViolation);
        }

        // ---------------------------------------------------------------- the background scan (content.texturePrescan)

        System.Threading.Thread prescan;
        volatile bool prescanStop;
        int lastOpenTick;

        /// <summary>The background scan runs (or ran) in this catalog.</summary>
        public bool PrescanRunning => prescan != null && prescan.IsAlive;

        /// <summary>
        /// Starts the background check of every bundle of the loaded zipmods and the base game (one worker thread, below normal
        /// priority; a bundle with a cached verdict is skipped): it fills the verdict cache, so first opens later need no
        /// check. Its memory stays small: one bundle at a time, read through the bundle reader's bounded block cache; LZMA
        /// bundles (decoded whole) and zip entries stored deflated over <see cref="PrescanDeflatedLimit"/> bytes (inflated
        /// whole) are left to the check at their open. It pauses while the catalog opens bundles (the UI is busy loading)
        /// and stops with <see cref="StopTexturePrescan"/>. Main thread (the work list is taken here).
        /// </summary>
        public void StartTexturePrescan()
        {
            if (PrescanRunning) return;
            var zips = new List<(string path, string[] bundles)>();
            foreach (var z in loadedInOrder) zips.Add((z.Path, z.BundlePaths.ToArray()));
            var abdata = new List<(string id, string dir)>();
            foreach (var h in sources.OfType<Hs2InstallSource>()) abdata.Add((h.Id, h.Abdata));
            var cache = TextureVerdicts;
            prescanStop = false;
            prescan = new System.Threading.Thread(() => PrescanRun(zips, abdata, cache)) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal, Name = "texture prescan" };
            prescan.Start();
        }

        /// <summary>Asks the background scan to stop (it ends after its current bundle).</summary>
        public void StopTexturePrescan() => prescanStop = true;

        /// <summary>The largest deflated zip entry the background check inflates (bytes); larger ones are checked at their open.</summary>
        public static long PrescanDeflatedLimit = 16L << 20;

        void PrescanRun(List<(string path, string[] bundles)> zips, List<(string id, string dir)> abdata, TextureVerdictCache cache)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var st = TextureCheckStats;
            int total = 0;
            foreach (var z in zips) total += z.bundles.Length;
            System.Threading.Interlocked.Exchange(ref st.PrescanTotal, Math.Max(0, total));
            System.Threading.Interlocked.Exchange(ref st.Pending, 1);
            string state = "done";
            try
            {
                foreach (var (id, dir) in abdata)
                {
                    if (!Directory.Exists(dir)) continue;
                    var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".resource", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)).ToList();
                    System.Threading.Interlocked.Add(ref st.PrescanTotal, files.Count);
                    foreach (var f in files)
                    {
                        if (prescanStop) { state = "stopped"; return; }
                        var fi = new FileInfo(f);
                        PrescanOne(new BundleFileRef { Key = id + ":" + f.Substring(dir.Length + 1).Replace('\\', '/'), File = f, Offset = 0, Length = fi.Length, Stamp = StampText(fi) }, 0, cache);
                    }
                }
                foreach (var (path, bundles) in zips)
                {
                    if (prescanStop) { state = "stopped"; return; }
                    HS2Next.IO.ZipIndex index;
                    FileInfo fi;
                    try { index = new HS2Next.IO.ZipIndex(path); fi = new FileInfo(path); }
                    catch (Exception) { System.Threading.Interlocked.Add(ref st.PrescanDone, bundles.Length); continue; }
                    foreach (var bp in bundles)
                    {
                        if (prescanStop) { state = "stopped"; return; }
                        // only asset bundles: a zipmod's abdata also holds loose textures, lists and sounds (per-file redirects)
                        if (!index.Entries.TryGetValue("abdata/" + bp, out var e) || !TextureCheck.IsBundleEntry(index, e, bp))
                        {
                            System.Threading.Interlocked.Increment(ref st.PrescanNotBundles);
                            System.Threading.Interlocked.Increment(ref st.PrescanDone);
                            continue;
                        }
                        var r = new BundleFileRef { Key = path + "!" + bp, File = path, Offset = e.Method == 0 ? e.DataOffset : 0, Length = e.Size, Stamp = StampText(fi) };
                        if (e.Method != 0) { var ix = index; var ee = e; r.ReadAll = () => ix.ReadAll(ee); }
                        PrescanOne(r, e.Method != 0 ? e.Size : 0, cache);
                    }
                }
            }
            catch (System.Threading.ThreadAbortException) { state = "aborted"; }
            catch (Exception e) { state = "stopped"; Debug.LogWarning("[content] texture prescan stopped: " + e.GetType().Name + ": " + e.Message); }
            finally
            {
                System.Threading.Interlocked.Exchange(ref st.Pending, 0);
                Debug.Log($"[content] texture prescan: {st.PrescanDone} of {st.PrescanTotal} entries in {sw.Elapsed.TotalSeconds:F0} s ({state}): " +
                          $"{st.PrescanChecked} bundles checked, {st.PrescanNotBundles} entries not bundles, {st.PrescanLeft} bundles left to the check at their open (LZMA, or deflated over {PrescanDeflatedLimit >> 20} MB)");
            }
        }

        // one bundle of the scan: skipped when its verdict is cached; an LZMA bundle or a large deflated entry is left to the
        // check at its open (both would be decoded whole); otherwise checked through the bounded block cache and released
        void PrescanOne(BundleFileRef r, long deflatedSize, TextureVerdictCache cache)
        {
            WaitWhileBusy();
            var st = TextureCheckStats;
            if (!cache.TryGet(r.Key, r.Stamp, out _))
            {
                if (deflatedSize > PrescanDeflatedLimit) { System.Threading.Interlocked.Increment(ref st.PrescanLeft); System.Threading.Interlocked.Increment(ref st.PrescanDone); return; }
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Hs2BundleFile b = null;
                try { b = r.ReadAll != null ? Hs2BundleFile.FromBytes(r.ReadAll(), r.Key, lzma: true) : Hs2BundleFile.OpenFile(r.File, r.Offset, r.Length, r.Key, lzma: true); }
                catch (Exception) { }
                if (b != null && b.DataCompression == 1) { System.Threading.Interlocked.Increment(ref st.PrescanLeft); System.Threading.Interlocked.Increment(ref st.PrescanDone); return; }
                TextureVerdict v;
                if (b == null) v = TextureCheck.Analyze(r);   // a bundle the reader cannot open: its verdict says why
                else
                {
                    v = TextureCheck.Analyze(b);
                    v.Key = r.Key;
                    v.Stamp = r.Stamp;
                    v.Ms = sw.Elapsed.TotalMilliseconds;
                }
                cache.Add(v);
                System.Threading.Interlocked.Increment(ref st.PrescanChecked);
            }
            System.Threading.Interlocked.Increment(ref st.PrescanDone);
        }

        void WaitWhileBusy()
        {
            while (!prescanStop && unchecked(Environment.TickCount - System.Threading.Volatile.Read(ref lastOpenTick)) < 2000) System.Threading.Thread.Sleep(200);
        }

        static string StampText(FileInfo fi) => fi.Length.ToString(CultureInfo.InvariantCulture) + ":" + fi.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
    }
}

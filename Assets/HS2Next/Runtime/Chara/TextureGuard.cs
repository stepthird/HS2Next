// Texture guard: some mod bundle textures (built with Unity 2018.x) are serialized with m_MipCount = 0, which Unity 6
// cannot read. Loading one logs the native assertion 'mipLevel < mipCount' (or 'mipLevel >= 0 && mipLevel <
// GetMipCount()'); without a graphics device nothing else happens, with one the asynchronous upload inside
// AssetBundle.LoadAsset removes the D3D12 device ("source data missing for async texture upload") or crashes D3D11.
// Two layers:
// 1. In process (every catalog load: ContentCatalog.LoadAsset -> TextureGuard.Load). The assertion names no texture,
//    so the guard counts assertions (and "image data of Texture 'X' has an unexpected size" errors, which do name it)
//    around each load; after a load that logged one, the textures the asset references (the texture itself, a
//    material's slots, the materials of a prefab's renderers) are checked: mip count < 1 or above a full chain, or a
//    size error by name, makes a texture bad (a directly loaded texture whose load asserted is bad by definition).
//    Every loaded material slot holding a bad texture gets a 4x4 fallback (mid-grey with the texture's sRGB flag; flat
//    normal for *Bump* / *Normal* slots), the bad object is destroyed, and the incident goes to the build
//    (CharaAssembler.Build drains it into CharaContext.Missing, kind "texture.badmip", and its log) and to the
//    persistent list Cache/texguard/bad_textures.tsv. Later loads from a cached bundle re-report swapped slots.
//    Cache/ here is the content settings' cache directory (CacheRoot: cacheDir / -cacheDir, default <project>/Cache; the
//    player folder's Cache in a player unless -cacheDir points elsewhere).
//    This saves headless runs only: with a graphics device the crash happens before LoadAsset returns.
// 2. Repaired bundles. Phase 5.2: the catalog checks every bundle before it opens it (Content/TextureCheck.cs,
//    ContentCatalog.TextureCheck.cs) and writes a patched bundle once to Cache/texguard/bundles with its line in the index
//    Cache/texguard/repaired.tsv (NoteRepaired registers it at once); Tools/texguard_repair.py wrote the same layout from
//    the list before and stays as a cross-check. ContentCatalog opens an indexed bundle from its copy (IBundleCache.GetOrLoad
//    -> RepairedPath), and loads from it are reported like layer 1 with the repair action as the reason.
// v0.3 trial 2: a second kind of texture Unity 6 cannot upload safely — a block-compressed one (DXT1 / DXT5 / BC4-BC7) whose
// width or height is not a multiple of four (it falls back to RGBA32 and the upload of its streamed data crashed the player).
// This guard does not see it (no assertion, the load "succeeds"); the catalog's texture check pads it before the open, and
// the thumbnail loader still reads each thumbnail's header before its load (Content/TextureHeaders.cs).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HS2Next.Chara
{
    /// <summary>One texture the guard replaced.</summary>
    public sealed class BadTexture
    {
        /// <summary>Where the load came from: "&lt;zipmod GUID | abdata&gt;:&lt;bundle path&gt;".</summary>
        public string Source;
        /// <summary>The asset whose load asserted (prefab, material or the texture itself).</summary>
        public string Asset;
        public string Texture, Reason, Format;
        public int Width, Height, Mips, FullChain;
        public bool Srgb;
        /// <summary>Material texture slots switched to the fallback.</summary>
        public int Slots;
        /// <summary>True when the bundle was opened from a repaired copy (Tools/texguard_repair.py).</summary>
        public bool Repaired;

        public string Detail => $"{Source}/{Asset}: {Texture} {Width}x{Height} {Format} mips {Mips} (full chain {FullChain}; {Reason}; {(Repaired ? "repaired copy" : $"{Slots} material slots -> fallback")})";
        public override string ToString() => Detail;
    }

    public static class TextureGuard
    {
        /// <summary>Off: catalog loads are plain AssetBundle.LoadAsset calls.</summary>
        public static bool Enabled = true;
        /// <summary>Diagnostics: write every texture of an asserted load to <see cref="Diagnostics"/>.</summary>
        public static bool Verbose;
        /// <summary>Open bundles listed in the repair index from their repaired copies (Cache/texguard/bundles).</summary>
        public static bool UseRepairedBundles = true;

        public static readonly List<string> Diagnostics = new List<string>();

        /// <summary>Called on the main thread right before and right after every catalog asset load (also with the guard off):
        /// the catalog's crash quarantine marks the first load of a zipmod bundle as in flight.</summary>
        public static Action<AssetBundle> BeforeLoad, AfterLoad;

        /// <summary>Asked before every catalog asset load (bundle, asset name, the bundle's cache key): true refuses the load
        /// (it returns null); the catalog's check refuses the assets that need a truncated mesh.</summary>
        public static Func<AssetBundle, string, Func<AssetBundle, string>, bool> RefuseLoad;
        /// <summary>Asserted loads seen and textures replaced in this process.</summary>
        public static int Incidents, Replaced;

        static int asserts;
        static bool hooked;
        // Texture names from "The image data of Texture 'X' has an unexpected size (expected N, got M)" errors.
        static readonly List<string> sizeErrors = new List<string>();
        static readonly Regex SizeError = new Regex(@"Texture '(.+?)' has an unexpected size", RegexOptions.Compiled);
        static readonly List<BadTexture> pending = new List<BadTexture>();
        // source/asset -> what an earlier load of it replaced (a directly loaded texture, or the asset that asserted).
        static readonly Dictionary<string, List<BadTexture>> known = new Dictionary<string, List<BadTexture>>(StringComparer.OrdinalIgnoreCase);
        // (material instance, texture property) -> the texture it held before the swap; sources with incidents.
        static readonly Dictionary<(Material, int), BadTexture> swappedSlots = new Dictionary<(Material, int), BadTexture>();
        static readonly HashSet<string> incidentSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly Texture2D[] fallbacks = new Texture2D[3];   // grey sRGB, grey linear, flat normal

        static void Hook()
        {
            if (hooked) return;
            hooked = true;
            Application.logMessageReceivedThreaded += OnLog;
        }

        static void OnLog(string msg, string stack, LogType type)
        {
            if (msg == null) return;
            if (type == LogType.Assert && msg.IndexOf("mipLevel", StringComparison.Ordinal) >= 0)
                Interlocked.Increment(ref asserts);
            else if (type == LogType.Error && msg.IndexOf("has an unexpected size", StringComparison.Ordinal) >= 0)
            {
                var m = SizeError.Match(msg);
                if (m.Success) lock (sizeErrors) sizeErrors.Add(m.Groups[1].Value);
            }
        }

        /// <summary>Clears the incidents not yet taken (call at the start of a build).</summary>
        public static void BeginBuild() { lock (pending) pending.Clear(); }

        /// <summary>The incidents since <see cref="BeginBuild"/> (each texture once).</summary>
        public static List<BadTexture> TakePending()
        {
            lock (pending)
            {
                var r = pending.GroupBy(b => b.Source + "/" + b.Texture, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
                pending.Clear();
                return r;
            }
        }

        /// <summary>Forgets the remembered incidents (after the bundles were unloaded nothing refers to them).</summary>
        public static void ForgetLoaded() { known.Clear(); swappedSlots.Clear(); incidentSources.Clear(); }

        /// <summary>Loads <paramref name="asset"/> from <paramref name="bundle"/>; when the load logs a mip assertion or an
        /// image-size error, the bad textures are replaced (see the file header).</summary>
        /// <param name="cacheKey">The bundle's cache key (written to the persistent list for the repair tool); evaluated only on an incident.</param>
        public static T Load<T>(AssetBundle bundle, string asset, string source, Func<AssetBundle, string> cacheKey = null) where T : Object
        {
            if (!bundle) return null;
            if (RefuseLoad != null && RefuseLoad(bundle, asset, cacheKey)) return null;
            if (!Enabled)
            {
                BeforeLoad?.Invoke(bundle);
                T plain;
                try { plain = bundle.LoadAsset<T>(asset); }
                finally { AfterLoad?.Invoke(bundle); }
                MaterialCopies.Capture(plain);
                return plain;
            }
            Hook();
            int before = Volatile.Read(ref asserts), sizeBefore;
            lock (sizeErrors) sizeBefore = sizeErrors.Count;
            BeforeLoad?.Invoke(bundle);
            T o;
            try { o = bundle.LoadAsset<T>(asset); }
            finally { AfterLoad?.Invoke(bundle); }
            MaterialCopies.Capture(o);   // the loaded keywords, before the checks below read the materials' properties
            string key = source + "/" + asset;
            List<string> sized;
            lock (sizeErrors) { sized = sizeErrors.Skip(sizeBefore).ToList(); if (sizeErrors.Count > 4096) sizeErrors.Clear(); }
            bool asserted = Volatile.Read(ref asserts) != before;
            if (!asserted && sized.Count == 0)
            {
                if (known.TryGetValue(key, out var old))
                {
                    lock (pending) pending.AddRange(old);
                    // A directly loaded bad texture was destroyed: hand out the fallback again.
                    if (!o && typeof(T).IsAssignableFrom(typeof(Texture2D)) && old.Count == 1 && old[0].Texture == asset)
                        return Fallback(old[0].Srgb, false) as T;
                }
                else if (o && incidentSources.Contains(source)) ReportSwapped(o);
                else if (o && RepairedSources.ContainsKey(source)) ReportRepaired(o, source, asset, key);
                return o;
            }
            Incidents++;
            if (!o) { Note($"{key}: {(asserted ? "mip assertion" : "image size error")}, asset not loaded"); return o; }
            return Handle(o, source, asset, key, cacheKey?.Invoke(bundle), new HashSet<string>(sized, StringComparer.Ordinal), asserted);
        }

        static T Handle<T>(T o, string source, string asset, string key, string bundleKey, HashSet<string> sized, bool asserted) where T : Object
        {
            var found = new List<(Texture2D tex, string reason)>();
            var cands = Textures(o);
            if (Verbose)
                foreach (var t in cands) Note($"{key}: candidate {Describe(t)}{(sized.Contains(t.name) ? " (image size error)" : "")}");
            foreach (var t in cands)
            {
                var r = Inconsistency(t) ?? (sized.Contains(t.name) ? "image data size mismatch" : null);
                if (r != null) found.Add((t, r));
            }
            if (o is Texture2D direct && found.All(f => f.tex != direct)) found.Add((direct, asserted ? "load asserted" : "image size error on load"));
            if (found.Count == 0)
            {
                Note($"{key}: {(asserted ? "mip assertion" : "image size error (" + string.Join(", ", sized) + ")")}, no bad texture among {cands.Count} referenced: " + string.Join("; ", cands.Select(Describe)));
                return o;
            }

            var list = new List<BadTexture>();
            foreach (var (tex, reason) in found)
            {
                var b = new BadTexture
                {
                    Source = source, Asset = asset, Texture = tex.name, Reason = reason, Format = tex.format.ToString(),
                    Width = tex.width, Height = tex.height, Mips = tex.mipmapCount, FullChain = FullChain(tex.width, tex.height), Srgb = tex.isDataSRGB,
                };
                b.Slots = ReplaceEverywhere(tex, b);
                list.Add(b);
                Record(b, bundleKey);
                Replaced++;
            }
            known[key] = list;
            incidentSources.Add(source);
            lock (pending) pending.AddRange(list);
            Object result = o;
            foreach (var (tex, _) in found)
            {
                if (ReferenceEquals(tex, o)) result = Fallback(tex.isDataSRGB, false);
                Object.DestroyImmediate(tex, true);   // a bundle asset: allowDestroyingAssets
            }
            return (T)result;
        }

        // A clean load from a bundle that had an incident: report the swapped slots the asset's materials still hold.
        static void ReportSwapped(Object o)
        {
            var mats = new List<Material>();
            switch (o)
            {
                case Material m: mats.Add(m); break;
                case GameObject go: foreach (var r in go.GetComponentsInChildren<Renderer>(true)) mats.AddRange(r.sharedMaterials); break;
                default: return;
            }
            foreach (var m in mats)
            {
                if (!m) continue;
                foreach (var id in m.GetTexturePropertyNameIDs())
                    if (swappedSlots.TryGetValue((m, id), out var b) && Array.IndexOf(fallbacks, m.GetTexture(id) as Texture2D) >= 0)
                        lock (pending) pending.Add(b);
            }
        }

        /// <summary>Why the texture's mip chain cannot be right, or null.</summary>
        public static string Inconsistency(Texture2D t)
        {
            if (!t) return null;
            int full = FullChain(t.width, t.height);
            if (t.mipmapCount < 1) return "no mip level";
            if (t.mipmapCount > full) return $"{t.mipmapCount} mips > full chain {full}";
            if (t.width < 1 || t.height < 1) return "empty size";
            return null;
        }

        public static int FullChain(int w, int h)
        {
            int m = Math.Max(1, Math.Max(w, h)), n = 1;
            while (m > 1) { m >>= 1; n++; }
            return n;
        }

        static string Describe(Texture t)
        {
            if (!t) return "(null)";
            var t2 = t as Texture2D;
            return $"{t.name} {t.GetType().Name} {t.width}x{t.height} mips {t.mipmapCount} {(t2 ? t2.format.ToString() : t.graphicsFormat.ToString())} " +
                   $"srgb {t.isDataSRGB} readable {t.isReadable} streaming {(t2 && t2.streamingMipmaps)}";
        }

        // Texture2Ds referenced by the asset: itself, a material's slots, or the materials of a prefab's renderers.
        static List<Texture2D> Textures(Object o)
        {
            var set = new List<Texture2D>();
            void Mat(Material m)
            {
                if (!m) return;
                foreach (var id in m.GetTexturePropertyNameIDs())
                    if (m.GetTexture(id) is Texture2D t && t && !set.Contains(t)) set.Add(t);
            }
            switch (o)
            {
                case Texture2D t: set.Add(t); break;
                case Material m: Mat(m); break;
                case GameObject go:
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                        foreach (var m in r.sharedMaterials) Mat(m);
                    break;
            }
            return set;
        }

        // Swaps the texture for the fallback in every loaded material; returns the slots changed.
        static int ReplaceEverywhere(Texture2D bad, BadTexture info)
        {
            bool srgb = info.Srgb;
            int n = 0;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (!m) continue;
                var ids = m.GetTexturePropertyNameIDs();
                var names = m.GetTexturePropertyNames();
                for (int i = 0; i < ids.Length; i++)
                {
                    if (m.GetTexture(ids[i]) != bad) continue;
                    m.SetTexture(ids[i], Fallback(srgb, IsNormalSlot(names[i])));
                    swappedSlots[(m, ids[i])] = info;
                    n++;
                }
            }
            return n;
        }

        static bool IsNormalSlot(string p) => p.IndexOf("Bump", StringComparison.OrdinalIgnoreCase) >= 0 || p.IndexOf("Normal", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>The 4x4 fallback: mid-grey (sRGB or linear), or a flat normal (linear, (0.5, 0.5, 1) with alpha 0.5 so both RGB and AG unpacking give +Z).</summary>
        public static Texture2D Fallback(bool srgb, bool normal)
        {
            int k = normal ? 2 : srgb ? 0 : 1;
            if (fallbacks[k]) return fallbacks[k];
            var c = normal ? new Color32(128, 128, 255, 128) : new Color32(128, 128, 128, 255);
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false, !srgb || normal)
            {
                name = normal ? "HS2Next_TextureGuard_flatnormal" : srgb ? "HS2Next_TextureGuard_grey" : "HS2Next_TextureGuard_grey_linear",
                hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[16];
            for (int i = 0; i < 16; i++) px[i] = c;
            t.SetPixels32(px);
            t.Apply(false, false);
            fallbacks[k] = t;
            return t;
        }

        static void Note(string s) { lock (Diagnostics) Diagnostics.Add(s); Debug.Log("TextureGuard: " + s); }

        // ------------------------------------------------------------ persistent list and repaired bundles

        static string cacheRoot;

        /// <summary>
        /// The cache folder the guard's folder lives in: <see cref="HS2Next.Content.ContentCatalog.FromSettings"/> sets it to the
        /// content settings' cache directory (cacheDir / -cacheDir; empty = &lt;data root&gt;/Cache, the project's Cache in the
        /// Editor), so a player given the Editor's cache sees its bad-texture list and repaired bundles
        ///. Default before any catalog: &lt;data root&gt;/Cache. A new value re-reads the
        /// repair index.
        /// </summary>
        public static string CacheRoot
        {
            get => cacheRoot ?? HS2Next.Content.ContentSettings.Roots.DefaultCacheDir;
            set
            {
                string v = string.IsNullOrEmpty(value) ? null : Path.GetFullPath(value);
                if (string.Equals(v, cacheRoot, StringComparison.OrdinalIgnoreCase)) return;
                cacheRoot = v;
                ReloadIndex();
                recorded.Clear();
                lock (listedCheck) listed = null;   // the list of the new folder is read on the next question
            }
        }

        /// <summary>&lt;<see cref="CacheRoot"/>&gt;/texguard (default &lt;data root&gt;/Cache/texguard).</summary>
        public static string CacheDir => Path.Combine(CacheRoot, "texguard");
        /// <summary>Incidents of all runs: source, asset, texture, size, format, mips, reason, bundle cache key (tab-separated).</summary>
        public static string ListFile => Path.Combine(CacheDir, "bad_textures.tsv");
        static string IndexFile => Path.Combine(CacheDir, "repaired.tsv");

        // Repair index: bundle cache key -> repaired copy; source label -> texture name -> action ("mips 0->1", "grey 4x4").
        static Dictionary<string, string> repaired;
        static readonly Dictionary<string, Dictionary<string, string>> repairedBySource = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        static Dictionary<string, Dictionary<string, string>> RepairedSources { get { LoadIndex(); return repairedBySource; } }
        static readonly HashSet<string> recorded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // (source, texture) and (source, asset) pairs of the list file, re-read when the file changed (checked at most every 2 s)
        static HashSet<string> listed;
        static DateTime listedStamp;
        static readonly System.Diagnostics.Stopwatch listedCheck = new System.Diagnostics.Stopwatch();

        /// <summary>
        /// True when the persistent list (<see cref="ListFile"/>, written by the guard, the thumbnail loader and the census'
        /// --record-guard) names <paramref name="textureOrAsset"/> of <paramref name="source"/> ("&lt;zipmod GUID | abdata&gt;:&lt;bundle
        /// path&gt;") as a texture or as the asset whose load asserted. The list is re-read when the file changed (checked at most
        /// every 2 s); false while the guard is off.
        /// </summary>
        public static bool IsListed(string source, string textureOrAsset)
        {
            if (!Enabled || source == null || textureOrAsset == null) return false;
            lock (listedCheck)
            {
                if (listed == null || !listedCheck.IsRunning || listedCheck.ElapsedMilliseconds > 2000)
                {
                    listedCheck.Restart();
                    string f = ListFile;
                    DateTime stamp = DateTime.MinValue;
                    try { if (File.Exists(f)) stamp = File.GetLastWriteTimeUtc(f); } catch (Exception) { }
                    if (listed == null || stamp != listedStamp)
                    {
                        listedStamp = stamp;
                        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        try
                        {
                            if (stamp != DateTime.MinValue)
                                foreach (var l in File.ReadLines(f))
                                {
                                    var c = l.Split('\t');
                                    if (c.Length < 3) continue;
                                    set.Add(c[0] + "\t" + c[2]);
                                    set.Add(c[0] + "\t" + c[1]);
                                }
                        }
                        catch (Exception) { }
                        listed = set;
                    }
                }
                return listed.Count > 0 && listed.Contains(source + "\t" + textureOrAsset);
            }
        }

        /// <summary>
        /// Appends a bad texture to the persistent list (source, asset, texture, size, format, mips, reason, bundle cache key)
        /// unless the list already names (source, texture); <see cref="IsListed"/> knows it at once.
        /// </summary>
        public static void Record(BadTexture b, string ck)
        {
            if (b == null) return;
            lock (listedCheck)
            {
                listed?.Add(b.Source + "\t" + b.Texture);
                listed?.Add(b.Source + "\t" + b.Asset);
            }
            try
            {
                string line = string.Join("\t", b.Source, b.Asset, b.Texture, $"{b.Width}x{b.Height}", b.Format, b.Mips, b.Reason, ck ?? "");
                string id = b.Source + "\t" + b.Texture;
                if (!recorded.Add(id)) return;
                Directory.CreateDirectory(CacheDir);
                if (File.Exists(ListFile) && File.ReadLines(ListFile).Any(l => l.StartsWith(b.Source + "\t") && l.Split('\t').ElementAtOrDefault(2) == b.Texture)) return;
                File.AppendAllText(ListFile, line + "\n", new UTF8Encoding(false));
            }
            catch (Exception e) { Note("could not record: " + e.Message); }
        }

        static void LoadIndex()
        {
            if (repaired != null) return;
            repaired = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(IndexFile)) return;
                foreach (var l in File.ReadAllLines(IndexFile, Encoding.UTF8))
                {
                    // cache key, repaired file (relative to CacheDir), source label, textures ("name=action" joined by '|')
                    var f = l.TrimStart('\uFEFF').Split('\t');
                    if (f.Length < 4 || l.StartsWith("#")) continue;
                    var p = Path.Combine(CacheDir, f[1]);
                    if (!File.Exists(p)) continue;
                    repaired[f[0]] = p;
                    if (!repairedBySource.TryGetValue(f[2], out var d)) repairedBySource[f[2]] = d = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var t in f[3].Split('|'))
                    {
                        int eq = t.LastIndexOf('=');
                        if (eq > 0) d[t.Substring(0, eq)] = t.Substring(eq + 1);
                    }
                }
            }
            catch (Exception e) { Note("repair index unreadable: " + e.Message); }
        }

        /// <summary>The repaired copy for a bundle cache key ("&lt;zip path&gt;!&lt;bundle path&gt;" or an abdata path), or null.</summary>
        public static string RepairedPath(string cacheKey)
        {
            if (!Enabled || !UseRepairedBundles || cacheKey == null) return null;
            LoadIndex();
            return repaired.TryGetValue(cacheKey, out var r) ? r : null;
        }

        /// <summary>
        /// Registers a bundle the catalog's texture check patched (main thread): its repaired copy (null when it is held in
        /// memory only) and the textures it repaired ("name=action|..."), so the next open of the key takes the copy and loads
        /// from the source are reported as loads from a repaired copy.
        /// </summary>
        public static void NoteRepaired(string cacheKey, string path, string source, string actions)
        {
            LoadIndex();
            if (cacheKey != null && path != null) repaired[cacheKey] = path;
            if (source == null || string.IsNullOrEmpty(actions)) return;
            if (!repairedBySource.TryGetValue(source, out var d)) repairedBySource[source] = d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var t in actions.Split('|'))
            {
                int eq = t.LastIndexOf('=');
                if (eq > 0) d[t.Substring(0, eq)] = t.Substring(eq + 1);
            }
        }

        /// <summary>Bundles of the repair index whose repaired copy exists under <see cref="CacheDir"/>.</summary>
        public static int RepairedCount { get { LoadIndex(); return repaired.Count; } }

        /// <summary>Re-reads the repair index (after Tools/texguard_repair.py ran).</summary>
        public static void ReloadIndex() { repaired = null; repairedBySource.Clear(); }

        // A load from a repaired bundle: report the repaired textures the asset references (mip count fixed, or 4x4 grey).
        static void ReportRepaired(Object o, string source, string asset, string key)
        {
            if (!repairedBySource.TryGetValue(source, out var actions)) return;
            var list = new List<BadTexture>();
            foreach (var t in Textures(o))
                if (actions.TryGetValue(t.name, out var action))
                    list.Add(new BadTexture
                    {
                        Source = source, Asset = asset, Texture = t.name, Reason = "repaired bundle: " + action, Format = t.format.ToString(),
                        Width = t.width, Height = t.height, Mips = t.mipmapCount, FullChain = FullChain(t.width, t.height), Srgb = t.isDataSRGB, Repaired = true,
                    });
            if (list.Count == 0) return;
            known[key] = list;
            lock (pending) pending.AddRange(list);
        }
    }
}

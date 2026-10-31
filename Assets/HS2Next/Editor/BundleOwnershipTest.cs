// Bundle ownership, the LRU and the byte budget (package O).
// L0b (Step 0, play mode, headless): what Unity 6.6 does with AssetBundle.Unload(false) and Resources.UnloadUnusedAssets —
//   (1) objects loaded from a bundle unloaded with Unload(false) stay valid while referenced (a held texture, an instantiated
//       prefab's meshes, materials and textures), for a file-backed and a memory-backed bundle;
//   (2) objects of a still-open bundle that nothing references are freed by UnloadUnusedAssets, and load again from the bundle;
//   (3) a bundle reopened after Unload(false) opens without a clash and a load of the same asset gives a second object, while
//       the old one lives; the old one goes at the next UnloadUnusedAssets once nothing holds it;
//   (4) the duration of UnloadUnusedAssets with 0, 2 and 4 characters built (3 calls each; Unity's own "Total:" line of the
//       log is the native part).
// Run: Tools/unity_batch.ps1 -Method BundleOwnershipTest.RunL0b -NoQuit -NoGraphics. Report Spike/out/bundles/l0b_report.txt
// (numbers and object counts only; no image is read or written).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Content;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static partial class BundleOwnershipTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "bundles");
    static string L0bReport => Path.Combine(OutDir, "l0b_report.txt");

    static readonly StringBuilder rep = new StringBuilder();
    static int passes, fails, warns;

    static void Reset() { rep.Clear(); passes = fails = warns = 0; }

    static void Check(string what, bool ok, string detail = null)
    {
        if (ok) passes++; else fails++;
        Line((ok ? "PASS " : "FAIL ") + what + (string.IsNullOrEmpty(detail) ? "" : " — " + detail));
    }

    static void Info(string s) => Line("INFO " + s);

    static void Line(string s)
    {
        rep.AppendLine(s);
        Debug.Log("[BundleOwnershipTest] " + s);
    }

    static string F(double v) => v.ToString("F1", CultureInfo.InvariantCulture);

    /// <summary>L0b in play mode (headless): -executeMethod BundleOwnershipTest.RunL0b (runner -NoQuit).</summary>
    public static void RunL0b()
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(L0bReport, "SUMMARY: 0 PASS, 1 FAIL\nL0b: started, no result yet\n", new UTF8Encoding(false));
        PlayModeHarness.Start(nameof(BundleOwnershipTest) + "." + nameof(L0bPlay), L0bReport, 25);
    }

    // A candidate bundle: a zipmod copy of a character part (prefab + at least two textures), not shipped by abdata.
    sealed class Probe
    {
        public ListEntry Row;
        public string Path, Prefab;
        public bool Memory;   // a deflated zip entry: opened from a byte array
        public long Bytes;
    }

    static IEnumerable<Probe> Candidates(ContentCatalog cat, bool memory)
    {
        int[] cats = { 351, 352, 353, 354, 355, 356, 357, 358, 359, 360, 361, 362, 363, 240, 241, 242, 243, 244, 247 };
        foreach (int c in cats)
            foreach (var e in cat.EntriesOf(c))
            {
                var z = e.Zipmod;
                string path = e["MainAB"], prefab = e["MainData"];
                if (z == null || string.IsNullOrEmpty(path) || path == "0" || string.IsNullOrEmpty(prefab) || prefab == "0") continue;
                path = path.Replace('\\', '/');
                if (!z.HasBundle(path) || cat.HasRealBundle(path)) continue;
                HS2Next.IO.ZipIndex.Entry ze;
                try { if (!z.Index.Entries.TryGetValue("abdata/" + path, out ze)) continue; }
                catch (Exception) { continue; }
                if ((ze.Method != 0) != memory || ze.Size < 256 * 1024 || ze.Size > 12L * 1024 * 1024) continue;
                yield return new Probe { Row = e, Path = path, Prefab = prefab, Memory = memory, Bytes = ze.Size };
            }
    }

    // In memory now: listed by FindObjectsOfTypeAll (which never loads). A lookup by id would load a persistent object of a
    // still-open bundle again from its file (the id stays the same while the bundle is open).
    static bool Alive(EntityId id) => LoadedTextureIds().Contains(id);

    static HashSet<EntityId> LoadedTextureIds() => new HashSet<EntityId>(Resources.FindObjectsOfTypeAll<Texture2D>().Where(t => t).Select(t => t.GetEntityId()));

    // Loads every texture of the bundle and returns their ids; keeps no reference (a helper, so no iterator field holds them).
    static List<EntityId> LoadAllTextureIds(AssetBundle b, out string firstName)
    {
        var all = b.LoadAllAssets<Texture2D>();
        firstName = all.Length > 0 ? all[0].name : null;
        return all.Where(t => t).Select(t => t.GetEntityId()).ToList();
    }

    static int CountNamed<T>(string name) where T : Object => Resources.FindObjectsOfTypeAll<T>().Count(o => o && o.name == name);

    // The renderers' assets of an instance: (renderers, meshes alive, materials alive, main textures alive, nulls).
    static (int renderers, int meshes, int materials, int textures, int dead) InstanceAssets(GameObject inst)
    {
        int r = 0, me = 0, ma = 0, tx = 0, dead = 0;
        foreach (var ren in inst.GetComponentsInChildren<Renderer>(true))
        {
            r++;
            Mesh mesh = ren is SkinnedMeshRenderer smr ? smr.sharedMesh : ren.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh) { if (mesh.vertexCount > 0) me++; else dead++; }
            foreach (var m in ren.sharedMaterials)
            {
                if (!m) continue;
                ma++;
                foreach (var pid in m.GetTexturePropertyNameIDs())
                {
                    var t = m.GetTexture(pid);
                    if (t is Texture2D t2) { if (t2 && t2.width > 0) tx++; else dead++; }
                }
            }
        }
        return (r, me, ma, tx, dead);
    }

    static double lastUnloadMs; static int lastUnloadFrames;

    static IEnumerator TimedUnloadUnused(string label)
    {
        GC.Collect();
        int objs0 = Resources.FindObjectsOfTypeAll<Object>().Length;
        var sw = Stopwatch.StartNew();
        int f0 = Time.frameCount;
        Debug.Log("[BundleOwnershipTest] UnloadUnusedAssets begin " + label);
        var op = Resources.UnloadUnusedAssets();
        yield return op;
        sw.Stop();
        lastUnloadMs = sw.Elapsed.TotalMilliseconds;
        lastUnloadFrames = Time.frameCount - f0;
        Debug.Log("[BundleOwnershipTest] UnloadUnusedAssets end " + label);
        Info($"UnloadUnusedAssets {label}: {F(lastUnloadMs)} ms wall, {lastUnloadFrames} frames; objects {objs0} -> {Resources.FindObjectsOfTypeAll<Object>().Length}");
    }

    static IEnumerator L0bPlay()
    {
        Reset();
        var swAll = Stopwatch.StartNew();
        Line($"L0b (Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceType}, play mode)");
        MakerCoreTest.Harness h = null;
        GameObject root = null;
        try
        {
            h = MakerCoreTest.Harness.Open();
            root = new GameObject("L0b root");
            root.SetActive(false);
        }
        catch (Exception e) { Check("setup", false, e.ToString()); }
        if (h == null) { Finish(swAll); yield break; }
        var cat = h.Catalog;
        Info($"catalog: {cat.Count} rows, {cat.ZipmodsByGuid.Count} zipmods, open bundles {cat.OpenBundleCount}");

        // ---- claims 1 and 3, a file-backed and a memory-backed bundle
        foreach (bool memory in new[] { false, true })
        {
            string kind = memory ? "memory-backed" : "file-backed";
            Probe pr = null; AssetBundle b = null; Texture2D[] texs = null;
            foreach (var c in Candidates(cat, memory).Take(40))
            {
                var bb = cat.LoadBundle(c.Row, c.Path);
                if (!bb) continue;
                if (!bb.Contains(c.Prefab)) continue;
                var t = bb.LoadAllAssets<Texture2D>();
                if (t.Length < 2) continue;
                pr = c; b = bb; texs = t;
                break;
            }
            if (pr == null) { Check($"claim 1 ({kind}): a candidate bundle (prefab + 2 textures, 256 KiB..12 MiB)", false); continue; }
            Info($"claim 1 ({kind}): {pr.Row.Category} {pr.Row.Key} {pr.Path} ({pr.Bytes / 1024} KiB), {texs.Length} textures, prefab {pr.Prefab}");
            var prefab = b.LoadAsset<GameObject>(pr.Prefab);
            if (!prefab) { Check($"claim 1 ({kind}): the prefab loads", false); continue; }
            var inst = Object.Instantiate(prefab, root.transform);
            var held = texs[0];
            string heldName = held.name;
            var heldId = held.GetEntityId();
            var dropped = texs.Skip(1).Select(t => t.GetEntityId()).ToList();
            texs = null;
            var a0 = InstanceAssets(inst);
            Info($"claim 1 ({kind}): instance renderers {a0.renderers}, meshes {a0.meshes}, materials {a0.materials}, textures {a0.textures}");

            int clash0 = ContentCatalog.CabClashMessages;
            b.Unload(false);
            bool bundleGone = !b;
            var a1 = InstanceAssets(inst);
            Check($"claim 1 ({kind}): after Unload(false) the held texture and the instance's assets are valid at once",
                  bundleGone && held && held.width > 0 && prefab && a1.meshes == a0.meshes && a1.materials == a0.materials && a1.textures == a0.textures && a1.dead == 0,
                  $"bundle unloaded {bundleGone}, texture {(held ? held.width + "x" + held.height : "dead")}, meshes {a1.meshes}/{a0.meshes}, materials {a1.materials}/{a0.materials}, textures {a1.textures}/{a0.textures}, dead {a1.dead}");
            yield return TimedUnloadUnused($"after Unload(false) of the {kind} bundle");
            var a2 = InstanceAssets(inst);
            int droppedAlive = dropped.Count(Alive);
            Check($"claim 1 ({kind}): after UnloadUnusedAssets the held texture, the prefab and the instance's assets still valid",
                  held && held.width > 0 && prefab && a2.meshes == a0.meshes && a2.materials == a0.materials && a2.textures == a0.textures && a2.dead == 0,
                  $"texture {(held ? "alive" : "dead")}, meshes {a2.meshes}, materials {a2.materials}, textures {a2.textures}, dead {a2.dead}");
            Info($"claim 1 ({kind}): textures of the unloaded bundle nothing held (not in the instance): {droppedAlive} of {dropped.Count} still alive after UnloadUnusedAssets (expected: only those the instance's materials use)");

            // claim 3: reopen, load the same asset again
            var b2 = cat.LoadBundle(pr.Row, pr.Path);
            bool reopened = b2 && ContentCatalog.CabClashMessages == clash0;
            var again = b2 ? b2.LoadAsset<Texture2D>(heldName) : null;
            bool second = again && held && again.GetEntityId() != heldId;
            int named = CountNamed<Texture2D>(heldName);
            Check($"claim 3 ({kind}): the bundle reopens after Unload(false) without a clash; the same asset loads as a second object; the old one lives",
                  reopened && second && named >= 2, $"reopened {(bool)b2}, clash lines {ContentCatalog.CabClashMessages - clash0}, second object {second}, Texture2D named '{heldName}': {named}");
            var prefab2 = b2 ? b2.LoadAsset<GameObject>(pr.Prefab) : null;
            Info($"claim 3 ({kind}): prefab reload gives a second object: {(prefab2 && prefab2 != prefab)}");
            // drop the old objects: the old texture goes at the next UnloadUnusedAssets
            Object.Destroy(inst);
            inst = null; held = null; prefab = null; prefab2 = null;
            yield return null;
            yield return TimedUnloadUnused($"after dropping the old objects ({kind})");
            bool oldGone = !Alive(heldId);
            bool newAlive = again && again.width > 0;
            Check($"claim 3 ({kind}): once nothing holds it the old object is freed by UnloadUnusedAssets; the reloaded one (held) lives",
                  oldGone && newAlive, $"old alive {!oldGone}, new alive {newAlive}, Texture2D named '{heldName}': {CountNamed<Texture2D>(heldName)}");
            again = null;
            if (b2) cat.UnloadBundle(b2);
        }

        // ---- claim 2: unreferenced objects of a still-open bundle
        {
            Probe pr = null; AssetBundle b = null;
            foreach (var c in Candidates(cat, false).Skip(5).Take(40))
            {
                var bb = cat.LoadBundle(c.Row, c.Path);
                if (!bb) continue;
                if (bb.GetAllAssetNames().Length < 3) continue;
                pr = c; b = bb;
                break;
            }
            if (pr == null) Check("claim 2: a candidate bundle", false);
            else
            {
                var ids = LoadAllTextureIds(b, out string firstName);
                Info($"claim 2: {pr.Path}: {ids.Count} textures loaded, no reference kept; bundle open");
                var live0 = LoadedTextureIds();
                int alive0 = ids.Count(live0.Contains);
                yield return TimedUnloadUnused("with the open bundle's textures unreferenced");
                var live1 = LoadedTextureIds();
                int alive1 = ids.Count(live1.Contains);
                bool open = b;
                var re = open && firstName != null ? b.LoadAsset<Texture2D>(firstName) : null;
                bool reloaded = re && re.width > 0 && LoadedTextureIds().Contains(re.GetEntityId());
                bool sameId = re && ids.Contains(re.GetEntityId());
                Check("claim 2: textures of a still-open bundle that nothing references are freed by UnloadUnusedAssets; the bundle stays open and loads them again",
                      ids.Count > 0 && alive0 == ids.Count && alive1 == 0 && open && reloaded,
                      $"in memory before {alive0} of {ids.Count}, after {alive1}; bundle open {open}; loaded again {reloaded} (same id as before: {sameId})");
                live0 = live1 = null;
                re = null;
                cat.UnloadBundle(b);
            }
        }

        // ---- claim 4: UnloadUnusedAssets duration with 0, 2, 4 characters
        SceneSession session = null; SceneRuntime runtime = null;
        try
        {
            session = SceneRuntimeTest.NewSession(h.Services);
            runtime = new SceneRuntime(session, h.Services, null, false);
        }
        catch (Exception e) { Check("claim 4: session", false, e.ToString()); }
        if (runtime != null)
        {
            var t = TestTargets.Load();
            var cards = new[] { t.visual.female, t.visual.male, t.visual.female, t.visual.male }.Select(TestTargets.Abs).ToList();
            var times = new Dictionary<int, List<double>>();
            int built = 0;
            foreach (int n in new[] { 0, 2, 4 })
            {
                while (built < n)
                {
                    string err = null;
                    try
                    {
                        var c = MakerEditTest.Import(h, cards[built], out err);
                        if (c != null) { session.AddCharaFromContainer(c); runtime.Apply(); }
                    }
                    catch (Exception e) { err = e.GetType().Name + ": " + e.Message; }
                    if (err != null) Check($"claim 4: build character {built + 1}", false, err);
                    built++;
                    yield return null;
                }
                int charas = runtime.Charas().Count(r => r.State == RealizationState.Built);
                times[n] = new List<double>();
                for (int i = 0; i < 3; i++)
                {
                    yield return TimedUnloadUnused($"#{i + 1} with {charas} characters built");
                    times[n].Add(lastUnloadMs);
                }
                Info($"claim 4: {charas} characters: {string.Join(" / ", times[n].Select(F))} ms; Texture2D {Resources.FindObjectsOfTypeAll<Texture2D>().Length}, Mesh {Resources.FindObjectsOfTypeAll<Mesh>().Length}, Material {Resources.FindObjectsOfTypeAll<Material>().Length}, open bundles {cat.OpenBundleCount}");
            }
            Check("claim 4: UnloadUnusedAssets measured with 0, 2 and 4 characters", times.Count == 3 && times.Values.All(l => l.Count == 3));
            try { runtime.Dispose(); } catch (Exception e) { Info("runtime dispose: " + e.Message); }
        }
        if (root) Object.Destroy(root);
        Finish(swAll);
    }

    static void Finish(Stopwatch sw)
    {
        rep.Insert(0, $"SUMMARY: {passes} PASS, {fails} FAIL{(warns > 0 ? ", " + warns + " WARN" : "")} ({sw.Elapsed.TotalSeconds:F0} s)\nBundleOwnershipTest L0b (Unload(false) and UnloadUnusedAssets on Unity 6.6)\n");
        File.WriteAllText(L0bReport, rep.ToString(), new UTF8Encoding(false));
        Debug.Log($"BUNDLE OWNERSHIP L0b: {passes} PASS, {fails} FAIL");
        PlayModeHarness.ExitCode = fails > 0 ? 1 : 0;
    }
}

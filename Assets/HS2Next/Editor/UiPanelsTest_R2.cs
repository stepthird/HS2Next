// UiPanelsTest.play groups of the second v0.3 trial (package R), in the app with the UI in Editor play mode (same harness and
// Ctx as UiPanelsTest):
//   R2P the pose browser over the HS2 install's pose folder: the app's tab first show (list shown / every name known, the pose
//     index of the app's cache folder), then browsers over the same folders with a test index under Spike/out: no index (the
//     list from the folder walk at once, names filled in while the files are read, a PNG listed only once read as a pose) and
//     with the index (no file opened); both = the files read without an index (paths, names, sexes, images, folders); a saved
//     .hs2npose is read alone on the rescan, a deleted one leaves the list and the index; the sex filter over indexed rows;
//     targets: the list ≤ 500 ms with the index, every name ≤ 2,000 ms without one (over it on the first pass, read cold: a
//     WARN and a second pass, read warm, must hold it), no main-thread fill step ≥ 100 ms.
//   R2S the silhouette drawn (BR-Chan and HERO107): renders of the view in memory (no AA, no post-processing) with the
//     character hidden (background), shown, silhouette on in an opaque colour and in HS2's half-transparent default, and off
//     again; pixel statistics only (the character's mask against the background, the share of the mask in the silhouette
//     colour, the blend of the half-transparent colour, the restored image against the first); the simple body's shaders are
//     in the player's shader set (Resources/HS2NextShaders).
//   R2C cameras as items: «Add ▸ camera at the current view» (one entry, the node at the view's pose and field of view,
//     selected), look through it by the shortcut (Keypad 0), the tree row's menu, the inspector's button and the Cameras tab,
//     back to the free camera (the free view as it was), «Update» from another view (one entry), 60 cameras from 60 views
//     (unique names, the tab lists them all, each looked through gives its own pose), save -> load equal (names, poses,
//     field of view, the camera in use; byte identity), a camera added after the load takes the next number, the 10 slots
//     untouched, delete from the tab.
// Card images, poses and renders are decoded and counted, never viewed or saved.
// Run alone: & Tools\unity_batch.ps1 -Method UiPanelsTest.Run -NoQuit -MethodArgs @('-only','R2P+R2S+R2C')
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HS2Next.App.Commands;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static partial class UiPanelsTest
{
    [UnityEditor.InitializeOnLoadMethod]
    static void RegisterR2Groups()
    {
        if (!Groups.Any(g => g.id == "R2P")) Groups.Add(("R2P", R2PoseGroup));
        if (!Groups.Any(g => g.id == "R2S")) Groups.Add(("R2S", R2SilhouetteGroup));
        if (!Groups.Any(g => g.id == "R2C")) Groups.Add(("R2C", R2CameraGroup));
    }

    // ---------------------------------------------------------------- R2P pose browser

    static IEnumerator R2PumpUntil(PoseBrowser b, Func<bool> done, double sec, List<double> frameMs = null)
    {
        var sw = Stopwatch.StartNew();
        while (!done() && sw.Elapsed.TotalSeconds < sec)
        {
            var f = Stopwatch.StartNew();
            b.Pump();
            frameMs?.Add(f.Elapsed.TotalMilliseconds);
            yield return null;
        }
    }

    static string R2Key(PoseEntry e) => e.FullPath + "|" + e.Name + "|" + e.Sex + "|" + e.HasImage + "|" + e.Status;

    static IEnumerator R2PoseGroup(Ctx c)
    {
        var ui = c.Ui;
        string hs2 = c.App.Paths.Hs2PoseDir, own = c.App.Paths.PoseDir;
        if (string.IsNullOrEmpty(hs2) || !Directory.Exists(hs2)) { c.Check(false, "the HS2 pose folder exists: " + hs2); yield break; }

        // (1) the app's tab, first show (the app's pose index: present when an earlier session wrote it)
        string appIndex = PoseFiles.IndexPath(c.App.Catalog?.CacheDir);
        bool hadIndex = appIndex != null && File.Exists(appIndex);
        var sw = Stopwatch.StartNew();
        ui.Shell.SetPanelVisible(PoseBrowser.PanelId, true);
        ui.Shell.FocusPanel(PoseBrowser.PanelId);
        var ok = new bool[1];
        yield return WaitFor(() => AddModule.Poses != null && AddModule.Poses.Ready && AddModule.Poses.Grid.Index.Count > 0, 60, ok);
        double shown = sw.Elapsed.TotalMilliseconds;
        yield return WaitFor(() => AddModule.Poses != null && AddModule.Poses.Complete, 120, ok);
        double complete = sw.Elapsed.TotalMilliseconds;
        var app = AddModule.Poses;
        var af = app?.Files;
        c.Info($"app tab first show ({(hadIndex ? "index present" : "no index yet")}): list shown {shown:0} ms after the show (browser {app?.FirstListMs:0} ms), every name {complete:0} ms (browser {app?.CompleteMs:0} ms); "
            + $"entries {af?.Entries.Count}, listed {af?.ListedCount}, folders {af?.Folders.Count}, index hits {af?.IndexHits}, files read {af?.FilesRead}, index written {af?.IndexWritten}, longest fill step {app?.MaxStepMs:0.0} ms (names {app?.MaxTakeMs:0.0}, folders {app?.MaxFolderMs:0.0}, list {app?.MaxRefreshMs:0.0}; {app?.FillSteps} steps)");
        c.Check(app != null && app.Complete && af.ListedCount > 0, "the app's pose tab lists the poses (" + af?.ListedCount + ")");
        if (hadIndex) c.Check(app.FirstListMs <= 500, $"the app's pose tab with its index: the list in {app.FirstListMs:0} ms (target ≤ 500)");
        var state = PoseBrowser.UiState();
        c.Check(state != null && (bool)state["complete"] && (int?)state["listed"] == af?.ListedCount, "ui.state poses: complete, listed " + (state != null ? state["listed"] : null));

        // (2) a test index: none, then present
        string idx = Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "ui_panels", "pose_index_test.tsv");
        try { if (File.Exists(idx)) File.Delete(idx); } catch (Exception) { }
        var cold = new PoseBrowser(ui) { UseAppFolders = false, Hs2Dir = hs2, OwnDir = own, IndexFile = idx };
        var frames = new List<double>();
        cold.StartScan();
        yield return R2PumpUntil(cold, () => cold.Ready, 30, frames);
        int unreadAtList = cold.Files?.Unread ?? -1, shownAtList = cold.Index?.Indices.Count ?? -1;
        bool pngAtList = cold.Files != null && cold.Index.Indices.Any(i => cold.Files.Entries[i].HasImage && cold.Files.Entries[i].Status == PoseEntryStatus.Pending);
        yield return R2PumpUntil(cold, () => cold.Complete, 120, frames);
        var cf = cold.Files;
        c.Info($"no index: list {cold.FirstListMs:0} ms ({shownAtList} shown, {unreadAtList} files to read), every name {cold.CompleteMs:0} ms; files read {cf?.FilesRead}, index written {cf?.IndexWritten}; "
            + $"main-thread step max {cold.MaxStepMs:0.0} ms (names {cold.MaxTakeMs:0.0}, folders {cold.MaxFolderMs:0.0}, list {cold.MaxRefreshMs:0.0}; {cold.FillSteps} steps), pumped frame p95 {Pct(frames, 0.95):0.0} ms / max {(frames.Count > 0 ? frames.Max() : 0):0.0} ms");
        c.Check(cold.Complete && cf.IndexHits == 0 && cf.FilesRead == cf.Entries.Count && cf.IndexWritten && File.Exists(idx), "no index: every file read once, the index written");
        c.Check(cold.FirstListMs <= 500 && unreadAtList > 0 && shownAtList > 0, $"no index: the list shows from the folder walk before the files are read ({cold.FirstListMs:0} ms, {shownAtList} shown, {unreadAtList} unread)");
        c.Check(!pngAtList, "no index: a PNG not read yet is not listed (it may be a plain image)");
        c.Check(cold.MaxStepMs < 100, $"no index: the longest main-thread fill step {cold.MaxStepMs:0.0} ms (< 100)");
        if (cold.CompleteMs <= 2000) c.Check(true, $"no index: every name in {cold.CompleteMs:0} ms (target ≤ 2,000)");
        else
        {
            // The first pass may read the files cold: earlier rows of a suite (the all-card import, the censuses) push them out
            // of the OS file cache. That time is a timing-class WARN; a second pass without the index, right after the first,
            // reads them warm and must hold the target (integration fix of the v0.3 trial 2 integrator).
            c.Warning($"no index: every name in {cold.CompleteMs:0} ms on the first pass (target 2,000; the OS file cache was cold)");
            try { if (File.Exists(idx)) File.Delete(idx); } catch (Exception) { }
            var second = new PoseBrowser(ui) { UseAppFolders = false, Hs2Dir = hs2, OwnDir = own, IndexFile = idx };
            second.StartScan();
            yield return R2PumpUntil(second, () => second.Complete, 120);
            var sf = second.Files;
            c.Info($"no index, second pass: every name {second.CompleteMs:0} ms; files read {sf?.FilesRead}, index written {sf?.IndexWritten}");
            c.Check(second.Complete && second.CompleteMs <= 2000 && sf.FilesRead == sf.Entries.Count && sf.IndexWritten && File.Exists(idx),
                $"no index, second pass (OS file cache warm): every name in {second.CompleteMs:0} ms (target ≤ 2,000), every file read, the index written");
        }

        var warm = new PoseBrowser(ui) { UseAppFolders = false, Hs2Dir = hs2, OwnDir = own, IndexFile = idx };
        frames.Clear();
        warm.StartScan();
        yield return R2PumpUntil(warm, () => warm.Complete, 30, frames);
        var wf = warm.Files;
        c.Info($"with the index: list {warm.FirstListMs:0} ms (List {wf?.ListMs:0} ms), complete {warm.CompleteMs:0} ms; index hits {wf?.IndexHits}, files read {wf?.FilesRead}; step max {warm.MaxStepMs:0.0} ms");
        c.Check(warm.Complete && wf.FilesRead == 0 && wf.IndexHits == wf.Entries.Count && !wf.IndexWritten, "with the index: no file opened, the index not rewritten");
        c.Check(warm.FirstListMs <= 500, $"with the index: the list in {warm.FirstListMs:0} ms (target ≤ 500)");

        // a browser over the HS2Next folder alone (as the pose group's) shares the index: the HS2 rows stay in it
        var ownOnly = new PoseBrowser(ui) { UseAppFolders = false, Hs2Dir = null, OwnDir = own, IndexFile = idx };
        ownOnly.StartScan();
        yield return R2PumpUntil(ownOnly, () => ownOnly.Complete, 30);
        var again = new PoseBrowser(ui) { UseAppFolders = false, Hs2Dir = hs2, OwnDir = own, IndexFile = idx };
        again.StartScan();
        yield return R2PumpUntil(again, () => again.Complete, 30);
        c.Check(ownOnly.Complete && again.Complete && again.Files.FilesRead == 0 && again.Files.IndexHits == again.Files.Entries.Count,
            $"a scan of the HS2Next folder alone keeps the HS2 rows of the shared index (then {again.Files?.FilesRead} files read, {again.Files?.IndexHits} of {again.Files?.Entries.Count} hits)");

        // the same rows as reading every file without an index
        var full = PoseFiles.Scan(hs2, own, null);
        var a = wf.Entries.Select(R2Key).ToList();
        var b = full.Entries.Select(R2Key).ToList();
        int differ = 0;
        for (int i = 0; i < Math.Min(a.Count, b.Count); i++) if (a[i] != b[i]) differ++;
        c.Check(a.Count == b.Count && differ == 0 && wf.ListedCount == full.ListedCount && wf.Folders.SequenceEqual(full.Folders) && wf.NotAPose == full.NotAPose,
            $"index rows = rows read from the files: {a.Count} / {b.Count} entries, {differ} differ, listed {wf.ListedCount} / {full.ListedCount}, folders {wf.Folders.Count} / {full.Folders.Count}, not a pose {wf.NotAPose} / {full.NotAPose}");
        c.Check(warm.Index.Indices.All(i => wf.Entries[i].Status == PoseEntryStatus.Pose), "every shown row is a pose (plain PNGs and unreadable files left out)");
        c.Check(cold.Index.Indices.Count == warm.Index.Indices.Count && cold.Index.Indices.SequenceEqual(warm.Index.Indices), "the filled-in list = the indexed list (" + warm.Index.Indices.Count + " rows)");

        // sex filter over indexed rows
        warm.SexFilter = 1;
        int females = warm.Index.Indices.Count;
        warm.SexFilter = 0;
        int males = warm.Index.Indices.Count;
        warm.SexFilter = null;
        c.Check(females + males <= warm.Index.Indices.Count && females > 0 && warm.Index.Indices.Where(i => wf.Entries[i].Sex == 1).Count() == females,
            $"sex filter on indexed rows: female {females}, male {males}, all {warm.Index.Indices.Count}");

        // a new HS2Next pose: read alone on the rescan; deleted: gone from the list and the index
        yield return NewScene(c);
        var ch = ui.Ops.AddChara(c.FemaleCard, null, null, -1, null, User);
        string chara = ch.NewIds.FirstOrDefault();
        yield return PlayModeHarness.WaitFrames(10);
        string err = warm.Save(chara, "r2_test_pose", true);
        string file = Path.Combine(own, "r2_test_pose.hs2npose");
        c.Check(err == null && File.Exists(file), "save a pose (.hs2npose) " + (err ?? ""));
        warm.Rescan();
        yield return R2PumpUntil(warm, () => warm.Complete, 30);
        wf = warm.Files;
        var mine = wf.Entries.FirstOrDefault(e => e.IsHs2n && e.FileName == "r2_test_pose");
        c.Check(mine != null && mine.Listed && mine.CanDelete && wf.FilesRead == 1 && wf.IndexHits == wf.Entries.Count - 1 && wf.IndexWritten,
            $"rescan after the save: the new file read alone (read {wf.FilesRead}, hits {wf.IndexHits} of {wf.Entries.Count}), listed, deletable");
        try { File.Delete(file); } catch (Exception) { }
        warm.Rescan();
        yield return R2PumpUntil(warm, () => warm.Complete, 30);
        wf = warm.Files;
        var idxRows = PoseIndex.Load(idx);
        c.Check(!wf.Entries.Any(e => e.FileName == "r2_test_pose") && wf.FilesRead == 0 && wf.IndexWritten && !idxRows.ContainsKey(file),
            "rescan after the delete: gone from the list and from the index (no file read)");
        if (chara != null) ui.Ops.Delete(new[] { chara }, User);
    }

    // ---------------------------------------------------------------- R2S silhouette drawn

    sealed class R2Shot
    {
        public int W, H;
        public Color32[] Px;
    }

    static R2Shot R2Capture(Camera cam, int w, int h)
    {
        var png = ViewCapture.Render(cam, w, h, ViewCapture.None);
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(png);
        var s = new R2Shot { W = t.width, H = t.height, Px = t.GetPixels32() };
        Object.Destroy(t);
        return s;
    }

    static int R2D(Color32 a, Color32 b) => Math.Max(Math.Abs(a.r - b.r), Math.Max(Math.Abs(a.g - b.g), Math.Abs(a.b - b.b)));

    static IEnumerator R2SilhouetteGroup(Ctx c)
    {
        var ui = c.Ui;
        // the player draws only the shaders of its set (the URP Unlit shader the old override looked up is not in it)
        string setText = "";
        try { setText = File.ReadAllText(Path.Combine(ContentSettings.ProjectRoot, "Assets", "HS2Next", "Resources", "HS2NextShaders.shadervariants")); } catch (Exception) { }
        foreach (var sh in new[] { "HS2Next/Custom/Silhouette", HS2Next.Chara.Mosaic.MosaicSet.ShaderName })
        {
            var s = Shader.Find(sh);
            string guid = s ? UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(s)) : null;
            c.Check(s && !string.IsNullOrEmpty(guid) && setText.Contains("guid: " + guid), "the player's shader set holds " + sh);
        }
        var view = c.App.Runtime.GetPart<SceneViewCamera>();
        bool pp = view.Setup.PostProcessing;
        view.Setup.PostProcessing = false;
        try
        {
            foreach (var (label, card) in new[] { ("BR-Chan", c.FemaleCard), ("HERO107", c.MaleCard) })
            {
                yield return NewScene(c);
                var res = ui.Ops.AddChara(card, null, null, -1, null, User);
                string id = res.NewIds.FirstOrDefault();
                c.Check(id != null, label + " added");
                if (id == null) continue;
                ui.Ops.SetTransform(new[] { id }, Vector3.zero, Vector3.zero, null, User);
                c.App.Session.Set(null, "camera.current", new CameraView { Pivot = new Vec3(0, 8.5f, 0), Rot = new Vec3(0, 180, 0), Distance = 42, Fov = 23 });
                yield return PlayModeHarness.WaitFrames(30);
                c.App.Runtime.ApplyNow();
                c.App.Runtime.LateApply();
                const int W = 192, H = 320;
                var cam = view.Camera;
                ui.Ops.SetVisible(new[] { id }, false, User);
                yield return PlayModeHarness.WaitFrames(3);
                var bg = R2Capture(cam, W, H);
                ui.Ops.SetVisible(new[] { id }, true, User);
                yield return PlayModeHarness.WaitFrames(3);
                var off1 = R2Capture(cam, W, H);
                var opaque = new Color4(1f, 0f, 1f, 1f);
                ui.Ops.SetState(new[] { id }, new (string, object)[] { ("silhouette", new CharaSilhouette { Enabled = true, Color = opaque }) }, User);
                yield return PlayModeHarness.WaitFrames(3);
                var sil = R2Capture(cam, W, H);
                var half = new Color4(0.188f, 0.286f, 0.8f, 0.5f);
                ui.Ops.SetState(new[] { id }, "silhouette.color", half, User);
                yield return PlayModeHarness.WaitFrames(3);
                var silHalf = R2Capture(cam, W, H);
                ui.Ops.SetState(new[] { id }, "silhouette.enabled", false, User);
                yield return PlayModeHarness.WaitFrames(10);
                var off2 = R2Capture(cam, W, H);

                int n = bg.Px.Length, maskOff = 0, maskSil = 0, magenta = 0, outsideChanged = 0, halfOk = 0, halfIn = 0, restoredDiff = 0;
                var colours = new HashSet<int>();
                double halfErr = 0;
                for (int i = 0; i < n; i++)
                {
                    bool inOff = R2D(off1.Px[i], bg.Px[i]) > 6, inSil = R2D(sil.Px[i], bg.Px[i]) > 6;
                    if (inOff) maskOff++;
                    if (inSil)
                    {
                        maskSil++;
                        var p = sil.Px[i];
                        colours.Add(p.r << 16 | p.g << 8 | p.b);
                        if (p.r >= 250 && p.g <= 5 && p.b >= 250) magenta++;
                        // the half-transparent colour over the background, blended once (sRGB target: compare in linear)
                        var bgc = ((Color)bg.Px[i]).linear;
                        var exp = new Color(0.188f, 0.286f, 0.8f).linear * 0.5f + bgc * 0.5f;
                        var got = ((Color)silHalf.Px[i]).linear;
                        double e = Math.Max(Math.Abs(exp.r - got.r), Math.Max(Math.Abs(exp.g - got.g), Math.Abs(exp.b - got.b)));
                        if (p.r >= 250 && p.g <= 5 && p.b >= 250) { halfIn++; halfErr = Math.Max(halfErr, e); if (e <= 0.02) halfOk++; }
                    }
                    else if (R2D(silHalf.Px[i], bg.Px[i]) > 6) outsideChanged++;
                    if (R2D(off2.Px[i], off1.Px[i]) > 8) restoredDiff++;
                }
                c.Info($"{label}: {W} x {H}, character {maskOff} px, silhouette {maskSil} px ({(maskOff > 0 ? 100.0 * maskSil / maskOff : 0):0.0} % of it), "
                    + $"opaque colour {magenta} px ({(maskSil > 0 ? 100.0 * magenta / maskSil : 0):0.0} %), {colours.Count} colours in the silhouette; half-transparent blend within 0.02 linear "
                    + $"{halfOk} / {halfIn} px (max error {halfErr:0.000}), changed outside the silhouette {outsideChanged} px; restored vs before {restoredDiff} px differ > 8 / 255");
                c.Check(maskOff > 1000 && maskSil > maskOff / 3 && maskSil <= maskOff * 11 / 10, $"{label}: the silhouette covers the figure ({maskSil} px vs {maskOff} px dressed)");
                c.Check(magenta >= maskSil * 9 / 10, $"{label}: the silhouette is one flat colour ({magenta} of {maskSil} px in the opaque silhouette colour; the rest the mosaic and edges)");
                c.Check(halfIn > 0 && halfOk >= halfIn * 98 / 100 && outsideChanged <= maskSil / 50, $"{label}: HS2's half-transparent default blends once over the background ({halfOk} / {halfIn} px)");
                c.Check(restoredDiff <= Math.Max(50, maskOff / 50), $"{label}: switched off, the character draws as before ({restoredDiff} px differ)");
                var real = c.App.Runtime.FindChara(id);
                var dump = real?.StateRealizer?.SilhouetteDump();
                c.Info(label + ": " + Newtonsoft.Json.JsonConvert.SerializeObject(dump));
                ui.Ops.Delete(new[] { id }, User);
            }
        }
        finally { view.Setup.PostProcessing = pp; }
    }

    // ---------------------------------------------------------------- R2C cameras as items

    static CameraView R2View(float x, float y, float z, float rx, float ry, float dist, float fov) =>
        new CameraView { Pivot = new Vec3(x, y, z), Rot = new Vec3(rx, ry, 0), Distance = dist, Fov = fov };

    static IEnumerator R2SetView(Ctx c, CameraView v)
    {
        c.App.Session.Set(null, "camera.current", v);
        yield return PlayModeHarness.WaitFrames(2);
        c.App.Runtime.ApplyNow();
        c.App.Runtime.LateApply();
    }

    static string R2Pose(Transform t) => t ? $"({t.position.x:0.###}, {t.position.y:0.###}, {t.position.z:0.###}) / ({t.eulerAngles.x:0.##}, {t.eulerAngles.y:0.##}, {t.eulerAngles.z:0.##})" : "none";

    static bool R2Same(Transform a, Transform b) =>
        a && b && Vector3.Distance(a.position, b.position) < 1e-3f && Quaternion.Angle(a.rotation, b.rotation) < 0.01f;

    static string R2Active(Ctx c) => c.App.Runtime.GetPart<SceneViewCamera>()?.ActiveCameraId;

    static IEnumerator R2CameraGroup(Ctx c)
    {
        var ui = c.Ui;
        yield return NewScene(c);
        var view = c.App.Runtime.GetPart<SceneViewCamera>();
        var cam = view.Camera.transform;
        var slotsBefore = JsonText(c.App.Session.Model.Settings.Camera.Slots);
        var v1 = R2View(1, 9, 2, 12, 160, 30, 31);
        yield return R2SetView(c, v1);
        var free = new Vector3(cam.position.x, cam.position.y, cam.position.z);
        var freeRot = cam.rotation;

        // the action of «Add ▸ camera at the current view» and its menu rows
        var tbAdd = ui.Shell.Toolbar.Element.Q<Button>("tb-add");
        if (tbAdd != null) { using (var e = NavigationSubmitEvent.GetPooled()) { e.target = tbAdd; tbAdd.SendEvent(e); } }
        yield return PlayModeHarness.WaitFrames(2);
        var addRow = ui.Shell.Overlay.Q("tb-add-menu")?.Q("menu-item-" + ActionIds.AddCameraFromView);
        c.Check(addRow != null, "the Add menu has «" + L10n.Get(ActionIds.LabelKey(ActionIds.AddCameraFromView)) + "»");
        if (addRow != null) U2Ev(c, new { type = "pointer", target = "menu-item-" + ActionIds.AddCameraFromView });
        else ui.Shell.Menus.CloseAll();
        yield return PlayModeHarness.WaitFrames(3);
        var cams = ui.Ops.AllCameras();
        string a = cams.FirstOrDefault()?.Id;
        var ah = a != null ? c.App.Runtime.Find(a)?.Holder : null;
        var ast = a != null ? ui.Model.Find(a).State as CameraNodeState : null;
        c.Check(cams.Count == 1 && c.UndoLabel == OpLabels.AddCamera && ui.Selection.Primary == a, $"add from the view: one camera, one entry ({c.UndoLabel}), selected");
        c.Check(R2Same(ah, cam) && ast != null && ast.Fov.HasValue && Mathf.Abs(ast.Fov.Value - 31f) < 1e-3f && !ast.Active,
            $"the camera stands at the view: {R2Pose(ah)} vs view {R2Pose(cam)}, fov {ast?.Fov} (view 31), not in use");
        c.Check(ui.Model.Find(a)?.Label == "Camera 1", "named " + ui.Model.Find(a)?.Label);

        // another view, then look through the camera by the shortcut
        yield return R2SetView(c, R2View(-4, 5, 6, 30, 40, 18, 45));
        ui.Selection.Set(new[] { a });
        var r = ui.Keys.Map.ChordsOf(ActionIds.CameraLookThrough).Select(ch => ch.ToString()).ToList();
        c.Check(r.Contains("Keypad0"), "camera.lookThrough is bound to Keypad0 (" + string.Join(", ", r) + ")");
        ui.Registry.Run(ActionIds.CameraLookThrough, "test");
        yield return PlayModeHarness.WaitFrames(3);
        c.Check(R2Active(c) == a && R2Same(cam, ah) && Mathf.Abs(view.Camera.fieldOfView - 31f) < 1e-3f && c.UndoLabel == OpLabels.ActiveCamera,
            $"look through (shortcut): the view = the camera ({R2Pose(cam)}, fov {view.Camera.fieldOfView:0.##}), entry {c.UndoLabel}");
        ui.Registry.Run(ActionIds.CameraLookThrough, "test");
        yield return PlayModeHarness.WaitFrames(3);
        c.Check(R2Active(c) == null && c.UndoLabel == OpLabels.ReleaseCamera && Mathf.Abs(view.Camera.fieldOfView - 45f) < 1e-3f,
            $"the shortcut again: back to the free camera (fov {view.Camera.fieldOfView:0.##}, the free view's 45), entry {c.UndoLabel}");

        // the tree row's menu
        var ws = WorkspacePanel.Current;
        ws?.ContextMenu(a, new Vector2(120, 120));
        yield return PlayModeHarness.WaitFrames(2);
        var menuRow = ui.Shell.Overlay.Q("menu-item-ctx-camera-active");
        var updRow = ui.Shell.Overlay.Q("menu-item-ctx-camera-update");
        c.Check(menuRow != null && updRow != null, "the camera row's menu: look through and update from the view");
        if (menuRow != null) U2Ev(c, new { type = "pointer", target = "menu-item-ctx-camera-active" });
        yield return PlayModeHarness.WaitFrames(3);
        c.Check(R2Active(c) == a, "the tree menu looks through the camera");
        ws?.ContextMenu(a, new Vector2(120, 120));
        yield return PlayModeHarness.WaitFrames(2);
        if (ui.Shell.Overlay.Q("menu-item-ctx-camera-active") != null) U2Ev(c, new { type = "pointer", target = "menu-item-ctx-camera-active" });
        yield return PlayModeHarness.WaitFrames(3);
        c.Check(R2Active(c) == null, "the tree menu again: back to the free camera");

        // the inspector's buttons (look through, back to the free camera)
        ui.Selection.Set(new[] { a });
        ui.Shell.FocusPanel("inspector");
        yield return PlayModeHarness.WaitFrames(4);
        var insp = ui.Shell.PanelRoot("inspector");
        var look = insp?.Query<Button>().ToList().FirstOrDefault(b => b.text == L10n.Get("inspector.camera.lookThrough"));
        var back = insp?.Query<Button>().ToList().FirstOrDefault(b => b.text == L10n.Get("inspector.camera.free"));
        c.Check(look != null && back != null, "the camera inspector has «" + L10n.Get("inspector.camera.lookThrough") + "» and «" + L10n.Get("inspector.camera.free") + "»");
        if (look != null) { using (var e = NavigationSubmitEvent.GetPooled()) { e.target = look; look.SendEvent(e); } }
        yield return PlayModeHarness.WaitFrames(3);
        c.Check(R2Active(c) == a, "the inspector button looks through the camera");
        if (back != null) { using (var e = NavigationSubmitEvent.GetPooled()) { e.target = back; back.SendEvent(e); } }
        yield return PlayModeHarness.WaitFrames(3);
        c.Check(R2Active(c) == null && c.UndoLabel == OpLabels.ReleaseCamera, "the inspector's free-camera button releases it (" + c.UndoLabel + ")");

        // the Cameras tab: listed; Update from the current view; View
        ui.Shell.SetPanelVisible(CameraListPanel.PanelId, true);
        ui.Shell.FocusPanel(CameraListPanel.PanelId);
        yield return PlayModeHarness.WaitFrames(4);
        var panel = ui.Shell.PanelRoot(CameraListPanel.PanelId)?.Q<CameraListPanel>() ?? ui.Shell.PanelRoot(CameraListPanel.PanelId) as CameraListPanel;
        c.Check(panel != null && ui.Shell.RegionOf(CameraListPanel.PanelId) == LayoutRegions.Right, "the Cameras tab in the right sidebar (" + ui.Shell.RegionOf(CameraListPanel.PanelId) + ")");
        if (panel == null) yield break;
        panel.Refresh();
        c.Check(panel.Ids.SequenceEqual(new[] { a }), "the tab lists the camera (" + panel.Ids.Count + ")");
        var v2 = R2View(3, 12, -5, 5, 270, 22, 27);
        yield return R2SetView(c, v2);
        var row = panel.Q("camera-row-" + a);
        var upd = row?.Q<Button>("camera-update");
        c.Check(upd != null, "the row has «" + L10n.Get("cameras.update") + "»");
        if (upd != null) { using (var e = NavigationSubmitEvent.GetPooled()) { e.target = upd; upd.SendEvent(e); } }
        yield return PlayModeHarness.WaitFrames(3);
        c.Check(R2Same(ah, cam) && Mathf.Abs((ast?.Fov ?? 0) - 27f) < 1e-3f && c.UndoLabel == OpLabels.Camera, $"Update: the camera to the current view ({R2Pose(ah)}, fov {ast?.Fov}), entry {c.UndoLabel}");
        ui.Session.Commands.Undo();
        yield return PlayModeHarness.WaitFrames(2);
        c.Check(!R2Same(ah, cam) && Mathf.Abs((ast?.Fov ?? 0) - 31f) < 1e-3f, "undo of Update: the camera back (fov " + ast?.Fov + ")");
        var viewBtn = panel.Q("camera-row-" + a)?.Q<Button>("camera-view");
        if (viewBtn != null) { using (var e = NavigationSubmitEvent.GetPooled()) { e.target = viewBtn; viewBtn.SendEvent(e); } }
        yield return PlayModeHarness.WaitFrames(3);
        panel.Refresh();
        c.Check(R2Active(c) == a && panel.Q("camera-row-" + a)?.Q<Button>("camera-view")?.ClassListContains("hs-seg--on") == true, "the tab's «View» looks through it and shows it pressed");
        var free2 = new Vector3(cam.position.x, cam.position.y, cam.position.z);
        var freeBtn = panel.Q<Button>("cameras-free");
        if (freeBtn != null) { using (var e = NavigationSubmitEvent.GetPooled()) { e.target = freeBtn; freeBtn.SendEvent(e); } }
        yield return PlayModeHarness.WaitFrames(3);
        var fv = c.App.Session.Model.Settings.Camera.Current;
        c.Check(R2Active(c) == null && fv != null && Mathf.Abs(fv.Fov - 27f) < 1e-3f && Mathf.Abs(fv.Distance - 22f) < 1e-3f, "«Free camera» returns to the free view as it was (fov " + fv?.Fov + ", distance " + fv?.Distance + ")");

        // many cameras: 60 views, 60 cameras
        var sw = Stopwatch.StartNew();
        var poses = new List<(string id, Vector3 pos, Quaternion rot, float fov)>();
        for (int i = 0; i < 60; i++)
        {
            yield return R2SetView(c, R2View(i * 0.5f - 15, 4 + (i % 7), (i % 5) - 2, (i * 7) % 60 - 20, i * 6, 10 + i % 13, 15 + i % 40));
            var res = ui.Ops.AddCameraFromView(null, false, User);
            if (!res.Ok) { c.Check(false, "add camera " + i + ": " + res); break; }
            poses.Add((res.NewIds[0], cam.position, cam.rotation, view.Camera.fieldOfView));
        }
        double addMs = sw.Elapsed.TotalMilliseconds;
        cams = ui.Ops.AllCameras();
        var names = cams.Select(n => n.Label).ToList();
        panel.Refresh();
        c.Info($"60 cameras added in {addMs:0} ms (with the view changes); names {names.First()} … {names.Last()}");
        c.Check(cams.Count == 61 && names.Distinct().Count() == 61 && names.Last() == "Camera 61" && panel.Ids.Count == 61, $"61 cameras, 61 distinct names, the tab lists {panel.Ids.Count}");
        int wrong = 0;
        var lookSw = new List<double>();
        foreach (var p in poses.Where((_, k) => k % 6 == 0))
        {
            var t0 = Stopwatch.StartNew();
            ui.Ops.SetActiveCamera(p.id, User);
            lookSw.Add(t0.Elapsed.TotalMilliseconds);
            yield return PlayModeHarness.WaitFrames(2);
            if (Vector3.Distance(cam.position, p.pos) > 1e-3f || Quaternion.Angle(cam.rotation, p.rot) > 0.01f || Mathf.Abs(view.Camera.fieldOfView - p.fov) > 1e-3f) wrong++;
        }
        ui.Ops.SetActiveCamera(null, User);
        yield return PlayModeHarness.WaitFrames(2);
        c.Check(wrong == 0, $"10 of them looked through: each shows its own pose and field of view ({wrong} wrong; switch {Median(lookSw):0.00} ms median)");

        // save -> load equal; a new camera after the load takes the next number; the slots untouched
        ui.Ops.SetActiveCamera(poses[10].id, User);
        yield return PlayModeHarness.WaitFrames(2);
        var opt = new SaveOptions { Thumbnail = ThumbnailMode.Placeholder, Clock = () => new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc) };
        var before = c.App.Runtime.Ops.ToBytes(opt);
        string Desc(TreeNode n) { var s = n.State as CameraNodeState; var d = n.Doc.Transform; return n.Label + "|" + d.Pos + "|" + d.Rot + "|" + s?.Fov + "|" + s?.Active; }
        var descBefore = ui.Ops.AllCameras().Select(Desc).ToList();
        var lr = c.App.Runtime.Ops.Load(before);
        yield return PlayModeHarness.WaitFrames(5);
        var descAfter = ui.Ops.AllCameras().Select(Desc).ToList();
        var again = c.App.Runtime.Ops.ToBytes(opt);
        c.Check(lr.Ok && descAfter.SequenceEqual(descBefore) && before.SequenceEqual(again), $"save -> load: {descAfter.Count} cameras equal (names, poses, fov, in use), the file byte-identical ({before.Length} / {again.Length} bytes)");
        string act = R2Active(c);
        c.Check(act != null && ui.Model.Find(act)?.Label == ui.Model.Find(poses[10].id)?.Label, "after the load the view looks through the same camera (" + (act != null ? ui.Model.Find(act)?.Label : "none") + ")");
        ui.Ops.SetActiveCamera(null, User);
        var nr = ui.Ops.AddCameraFromView(null, false, User);
        c.Check(nr.Ok && ui.Model.Find(nr.NewIds[0])?.Label == "Camera 62", "a camera added after the load: " + (nr.Ok ? ui.Model.Find(nr.NewIds[0])?.Label : nr.ToString()));
        c.Check(JsonText(c.App.Session.Model.Settings.Camera.Slots) == slotsBefore, "the 10 camera slots untouched");

        // delete from the tab
        panel.Refresh();
        string last = nr.Ok ? nr.NewIds[0] : null;
        var del = last != null ? panel.Q("camera-row-" + last)?.Q<Button>("camera-delete") : null;
        if (del == null && last != null)
        {
            panel.List.ScrollToItem(panel.Ids.Count - 1);
            yield return PlayModeHarness.WaitFrames(3);
            del = panel.Q("camera-row-" + last)?.Q<Button>("camera-delete");
        }
        if (del != null) { using (var e = NavigationSubmitEvent.GetPooled()) { e.target = del; del.SendEvent(e); } }
        yield return PlayModeHarness.WaitFrames(3);
        panel.Refresh();
        c.Check(del != null && ui.Model.Find(last) == null && c.UndoLabel == OpLabels.Delete && panel.Ids.Count == 61, "«×» in the tab deletes the camera (" + c.UndoLabel + ", " + panel.Ids.Count + " listed)");
        yield return NewScene(c);
    }

    static string JsonText(object o) => Newtonsoft.Json.JsonConvert.SerializeObject(o);
}

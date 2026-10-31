// UiOpsTest (Phase 3.5 package B): the operations layer, headless, edit mode, on the SFW
// visual targets of Tools/targets.json and vanilla items.
//   O1 StudioOps.Labels: distinct, the labels, and package P's mirror HS2Next.UI.Core.OpLabels equal by name
//   O2 every StudioOps method that writes the document: Ok, one history entry with its label, undo restores the scene.doc bytes
//      of before and redo those of after (Source User)
//   O3 BeginOrJoin: several ops inside an open transaction are one entry with the transaction's label; Cancel reverts all
//   O4 parity table over every extracted handler: the bridge handler and the op on two hosts with the same ids give equal
//      scene.doc bytes and the same history label
//   O5 ChangeCharacter keeps the node (id, transform, children, animation, FK / IK, face, look, state, name); undo swaps back
//   O6 ImportScene remaps ids (nodes, parents, route points) and selects the imported top nodes
//   O7 transform drag: one entry per drag, Cancel reverts; read-only ops record nothing (RestartAnimation, LoadCameraSlot)
//   O12 delete a moving character, undo, step: no exception, the dynamic-bone particles were reset (every
//      particle equal to a fresh build of the same pose within 1e-4; Phase 3.6 K's investigation variants measured first)
// Run: -executeMethod UiOpsTest.Run [-only O2+O4]; report Spike/out/ui_ops/ui_ops_report.txt.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HS2Next.App;
using HS2Next.Content;
using HS2Next.Motion.DynamicBones;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class UiOpsTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "ui_ops");
    static StringBuilder rep;
    static int passes, fails;
    static string current = "";
    static readonly Dictionary<string, (int pass, int fail)> perTest = new Dictionary<string, (int, int)>();
    static readonly List<string> measurements = new List<string>();
    static string[] only;
    static RuntimeServices services;
    static string femaleCard, maleCard;
    static readonly ExecuteOptions User = new ExecuteOptions { Source = CommandSource.User };
    const ulong Seed = 0x0b5ea5e0b5ea5eUL;

    static string Arg(string name)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    static void Info(string s) => rep.AppendLine("   " + s);
    static int warns;
    static void Warn(string s) { warns++; rep.AppendLine($"WARN  [{current}] {s}"); }
    static void Measure(string s) { measurements.Add($"[{current}] {s}"); Info("MEASURE " + s); }

    static bool Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  [{current}] {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
        perTest.TryGetValue(current, out var v);
        perTest[current] = ok ? (v.pass + 1, v.fail) : (v.pass, v.fail + 1);
        return ok;
    }

    static void Guard(string id, string title, Action a)
    {
        if (only != null && !only.Contains(id)) return;
        current = id;
        rep.AppendLine();
        rep.AppendLine($"== {id} {title}");
        try { a(); }
        catch (Exception e) { Check("exception", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
    }

    public static void Run()
    {
        rep = new StringBuilder(); passes = fails = warns = 0; perTest.Clear(); measurements.Clear();
        var o = Arg("-only");
        only = o == null ? null : o.Split(',', '+').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        Directory.CreateDirectory(OutDir);
        var sw = Stopwatch.StartNew();
        var head = new StringBuilder();
        try
        {
            Guard("O1", "labels", O1);
            services = SceneRuntimeTest.NewServices(head);
            var t = TestTargets.Load();
            femaleCard = TestTargets.Abs(t.visual.female);
            maleCard = TestTargets.Abs(t.visual.male);
            rep.Insert(0, $"UI operations test (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})\n" + head);
            Guard("O2", "every op: one entry, its label, undo / redo restore scene.doc", O2);
            Guard("O3", "BeginOrJoin", O3);
            Guard("O4", "handler / op parity", O4);
            Guard("O5", "ChangeCharacter keeps the node", O5);
            Guard("O6", "ImportScene remaps ids", O6);
            Guard("O7", "transform drag, unrecorded ops", O7);
            Guard("O12", "graveyard restore resets dynamic bones", O12);
        }
        catch (Exception e) { current = "run"; Check("run (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        var sum = new StringBuilder();
        sum.AppendLine($"SUMMARY: {passes} PASS, {fails} FAIL ({warns} WARN, {sw.Elapsed.TotalSeconds:F0} s)");
        foreach (var kv in perTest.OrderBy(k => k.Key.Length).ThenBy(k => k.Key, StringComparer.Ordinal)) sum.AppendLine($"  {kv.Key}: {kv.Value.pass} pass, {kv.Value.fail} fail");
        if (measurements.Count > 0) { sum.AppendLine("measurements:"); foreach (var m in measurements) sum.AppendLine("  " + m); }
        rep.Insert(0, sum.ToString());
        File.WriteAllText(Path.Combine(OutDir, "ui_ops_report.txt"), rep.ToString(), new UTF8Encoding(false));
        services?.Catalog?.UnloadAll();
        Debug.Log($"UI OPS TEST DONE: {passes} PASS, {fails} FAIL");
        if (fails > 0) TestExit.Exit(1);
    }

    // ================================================================ fixture

    static string UserDataDir => Path.Combine(OutDir, "userdata");

    static AppCore Host(ulong seed = Seed)
    {
        var s = AppSettings.Defaults(Array.Empty<string>());
        s.Content = CardResolveTest.Settings;
        s.UserData = UserDataDir;
        s.Bridge.OutputDir = Path.Combine(OutDir, "out");
        var o = services.NewSessionOptions();
        o.Ids = new CountingIdSource(seed);
        return AppCore.ForTests(services, s, null, o);
    }

    static string Sha(byte[] b) { using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(b)).Replace("-", ""); }
    static string Doc(AppCore app) => Sha(DocClone.Bytes(app.Session.ExportDocument()));
    static int Undos(AppCore app) => app.Session.Commands.UndoList.Count;
    static string Top(AppCore app) => app.Session.Commands.UndoList.Count > 0 ? app.Session.Commands.UndoList[0].Label : null;

    static JObject Call(AppCore app, object req)
    {
        var r = JObject.Parse(app.Call(req is string s ? s : JsonConvert.SerializeObject(req)));
        if (r.Value<bool?>("ok") != true) Info("call " + JsonConvert.SerializeObject(req) + " -> " + r.ToString(Formatting.None));
        return r;
    }

    sealed class Fixture
    {
        public AppCore App;
        public StudioOps Ops => App.Ops;
        public string F, I, I2, L, Cam, D;
    }

    /// <summary>BR-Chan, two vanilla items (399), a light (Light row 1), a camera, a folder; history cleared of nothing.</summary>
    static Fixture Make(ulong seed = Seed)
    {
        var f = new Fixture { App = Host(seed) };
        var ops = f.Ops;
        f.F = ops.AddChara(femaleCard, null, null, -1, null, User).NewIds.FirstOrDefault();
        f.I = ops.AddItem(ContentId.Vanilla(399), null, null, -1, User).NewIds.FirstOrDefault();
        f.I2 = ops.AddItem(ContentId.Vanilla(399), null, null, -1, User).NewIds.FirstOrDefault();
        f.L = ops.AddLight(ContentId.Vanilla(1), null, null, -1, User).NewIds.FirstOrDefault();
        f.Cam = ops.AddCamera(null, null, -1, User).NewIds.FirstOrDefault();
        f.D = ops.AddFolder(null, null, -1, User).NewIds.FirstOrDefault();
        f.App.Runtime.ApplyNow();
        return f;
    }

    // ================================================================ O1

    static void O1()
    {
        var all = StudioOps.Labels.All;
        Check($"StudioOps.Labels.All: {all.Length} distinct labels", all.Distinct().Count() == all.Length);
        var consts = typeof(StudioOps.Labels).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).ToList();
        Check("Labels.All lists every constant", consts.Count == all.Length && consts.All(f => all.Contains((string)f.GetValue(null))), consts.Count + " constants");
        string[] spec =
        {
            "Add character", "Add item", "Add light", "Add folder", "Add camera", "Delete", "Duplicate", "Parent", "Reorder", "Visibility", "Rename", "Copy transform",
            "Transform", "Reset transform", "Move to pivot", "Animation", "Animation speed", "Animation options", "Face", "Hands", "Look", "Look target", "Clothes",
            "Accessories", "Character state", "Joint correction", "Change character", "FK", "FK bones", "FK from animation", "FK from pose", "IK", "IK targets",
            "IK from animation", "IK from pose", "Convert IK", "Bake IK", "Load pose", "Item colour", "Item pattern", "Item alpha", "Item emission", "Item panel",
            "Item FK", "Item dynamic bones", "Item options", "Item animation", "Light", "Chara light", "Camera slot", "Active camera", "Release camera", "Camera",
            "Map", "Map pose", "Map options", "Map lights", "Effects", "Reset effects", "Sky", "Sound", "Title", "Import scene",
        };
        var missing = spec.Where(x => !all.Contains(x)).ToList();
        Check($"every label is in the table ({spec.Length})", missing.Count == 0, string.Join(", ", missing));
        Info("labels beyond the spec table: " + string.Join(", ", all.Where(x => !spec.Contains(x))));
        var p = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("HS2Next.UI.Core.OpLabels")).FirstOrDefault(x => x != null);
        if (p == null) { Check("package P's HS2Next.UI.Core.OpLabels exists", false); return; }
        var bad = new List<string>();
        var extra = new List<string>();
        foreach (var f in p.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral && f.FieldType == typeof(string)))
        {
            var mine = typeof(StudioOps.Labels).GetField(f.Name, BindingFlags.Public | BindingFlags.Static);
            if (mine == null) { extra.Add(f.Name); continue; }
            if ((string)mine.GetValue(null) != (string)f.GetValue(null)) bad.Add(f.Name + ": \"" + f.GetValue(null) + "\" vs \"" + mine.GetValue(null) + "\"");
        }
        var notMirrored = consts.Where(f => p.GetField(f.Name, BindingFlags.Public | BindingFlags.Static) == null).Select(f => f.Name).ToList();
        Check("OpLabels (package P's mirror) = StudioOps.Labels for every shared name", bad.Count == 0, string.Join("; ", bad));
        Info("OpLabels names not in StudioOps.Labels: " + (extra.Count == 0 ? "none" : string.Join(", ", extra)) + "; StudioOps.Labels names not mirrored: " + (notMirrored.Count == 0 ? "none" : string.Join(", ", notMirrored)));
    }

    // ================================================================ O2

    static void O2()
    {
        var f = Make();
        var app = f.App;
        var ops = f.Ops;
        Check("fixture built (BR-Chan, 2 items, light, camera, folder)", f.F != null && f.I != null && f.I2 != null && f.L != null && f.Cam != null && f.D != null
            && app.Runtime.FindChara(f.F)?.State == RealizationState.Built);
        var idle = services.Db.FindAnime(19, 0, 0);
        string dup = null;
        var saved = Path.Combine(OutDir, "o2_import.hs2nscene");
        var table = new List<(string name, string label, Func<OpResult> op)>
        {
            ("AddChara", "Add character", () => ops.AddChara(maleCard, null, null, -1, null, User)),
            ("AddItem", "Add item", () => ops.AddItem(ContentId.Vanilla(399), null, null, -1, User)),
            ("AddLight", "Add light", () => ops.AddLight(ContentId.Vanilla(2), null, null, -1, User)),
            ("AddFolder", "Add folder", () => ops.AddFolder(null, null, -1, User)),
            ("AddCamera", "Add camera", () => ops.AddCamera(null, null, -1, User)),
            ("Duplicate", "Duplicate", () => { var r = ops.Duplicate(new[] { f.I }, User); dup = r.NewIds.FirstOrDefault(); return r; }),
            ("Delete", "Delete", () => ops.Delete(new[] { dup ?? f.I2 }, User)),
            ("Move", "Parent", () => ops.Move(f.I, f.D, null, -1, true, User)),
            ("Reorder", "Reorder", () => ops.Reorder(f.Cam, 0, User)),
            ("SetVisible", "Visibility", () => ops.SetVisible(new[] { f.I2 }, false, User)),
            ("Rename", "Rename", () => ops.Rename(f.I2, "O2 box", User)),
            ("SetTransform", "Transform", () => ops.SetTransform(new[] { f.F }, new Vector3(1, 0, 2), new Vector3(0, 30, 0), null, User)),
            ("CopyTransform", "Copy transform", () => ops.CopyTransform(f.F, new[] { f.I2 }, User)),
            ("ResetTransform", "Reset transform", () => ops.ResetTransform(new[] { f.I2 }, TransformChannel.Rot, User)),
            ("MoveToPivot", "Move to pivot", () => ops.MoveToPivot(new[] { f.I2 }, User)),
            ("SetAnimation", "Animation", () => ops.SetAnimation(new[] { f.F }, idle, 0.25f, null, null, null, User)),
            ("SetAnimeSpeed", "Animation speed", () => ops.SetAnimeSpeed(new[] { f.F }, 0.5f, true, User)),
            ("SetAnimePattern", "Animation speed", () => ops.SetAnimePattern(new[] { f.F }, 0.5f, User)),
            ("SetAnimeOption", "Animation options", () => ops.SetAnimeOption(new[] { f.F }, false, new[] { 0.3f, 0.6f }, null, User)),
            ("SetFace", "Face", () => ops.SetFace(new[] { f.F }, new FaceEdit { Mouth = 1, EyesOpen = 0.5f, Tears = 0.3f, Eyebrow = 1 }, User)),
            ("SetHands", "Hands", () => ops.SetHands(new[] { f.F }, 2, 3, User)),
            ("SetLook", "Look", () => ops.SetLook(new[] { f.F }, 1, 1, User)),
            ("SetLookTarget", "Look target", () => ops.SetLookTarget(f.F, new Vector3(0, 15, 5), User)),
            ("SetClothes", "Clothes", () => ops.SetClothes(new[] { f.F }, 0, 2, true, User)),
            ("SetClothesAll", "Clothes", () => ops.SetClothesAll(new[] { f.F }, 0, User)),
            ("SetAccessoriesAll", "Accessories", () => ops.SetAccessoriesAll(new[] { f.F }, false, User)),
            ("SetState", "Character state", () => ops.SetState(new[] { f.F }, new (string, object)[] { ("wet", 0.4f), ("liquid[2]", 1) }, User)),
            ("SetCorrection", "Joint correction", () => ops.SetCorrection(new[] { f.F }, new Dictionary<int, bool> { [0] = !((CharaNodeState)app.Session.Model.Find(f.F).State).Correction[0] }, User)),
            ("SetFkEnabled", "FK", () => ops.SetFkEnabled(f.F, true, User)),
            ("FkFromAnimation", "FK from animation", () => ops.FkFromAnimation(f.F, null, false, User)),
            ("SetIkEnabled", "IK", () => ops.SetIkEnabled(f.F, true, User)),
            ("IkFromAnimation", "IK from animation", () => ops.IkFromAnimation(f.F, null, false, User)),
            ("BakeIk", "Bake IK", () => ops.BakeIk(f.F, User)),
            ("ChangeCharacter", "Change character", () => ops.ChangeCharacter(new[] { f.F }, maleCard, null, User)),
            ("ChangeCharacter back", "Change character", () => ops.ChangeCharacter(new[] { f.F }, femaleCard, null, User)),
            ("SetFkHips", "FK bones", () => ops.SetFkHips(new[] { f.F }, 0.25f, true, new Vec3(0.1f, 0.9f, 0f), User)),
            ("SetFkHips clear position", "FK bones", () => ops.SetFkHips(new[] { f.F }, null, true, null, User)),
            ("SetLookTargetLocal", "Look target", () => ops.SetLookTargetLocal(new[] { f.F }, new Vec3(0.5f, 1.5f, 3f), User)),
            ("SetFace lipSync", "Face", () => ops.SetFace(new[] { f.F }, new FaceEdit { LipSync = false }, User)),
            ("SetItem", "Item alpha", () => ops.SetItem(new[] { f.I }, new (string, object)[] { ("alpha", 0.5f) }, User)),
            ("SetItemAnimation", "Item animation", () => ops.SetItemAnimation(new[] { f.I }, null, 0.5f, null, User)),
            ("SetLight", "Light", () => ops.SetLight(new[] { f.L }, new List<(string, object)> { ("light.intensity", 1.5f) }, User)),
            ("SetCharaLight", "Chara light", () => ops.SetCharaLight(new List<(string, object)> { ("charaLight.intensity", 0.7f) }, User)),
            ("SaveCameraSlot", "Camera slot", () => ops.SaveCameraSlot(3, User)),
            ("SetActiveCamera", "Active camera", () => ops.SetActiveCamera(f.Cam, User)),
            ("SetActiveCamera null", "Release camera", () => ops.SetActiveCamera(null, User)),
            ("SetCameraFov", "Camera", () => ops.SetCameraFov(new[] { f.Cam }, 30f, User)),
            ("LoadMap", "Map", () => ops.LoadMap(ContentId.Vanilla(10).ToString(), User)),
            ("SetMap", "Map options", () => ops.SetMap(null, null, false, null, User)),
            ("SetMap pose", "Map pose", () => ops.SetMap(new Vector3(0, 1, 0), null, null, null, User)),
            ("SetEffects", "Effects", () => ops.SetEffects(new List<(string, object)> { ("effects.bloom.intensity", 5f) }, User)),
            ("ResetEffects", "Reset effects", () => ops.ResetEffects(new[] { "bloom" }, User)),
            ("SetSky", "Sky", () => ops.SetSky(true, null, User)),
            ("SetSound", "Sound", () => ops.SetSound(new List<(string, object)> { ("sound.bgm.play", true) }, User)),
            ("SetTitle", "Title", () => ops.SetTitle("O2 scene", User)),
            ("ImportScene", "Import scene", () => { app.Runtime.Ops.Save(saved, new SaveOptions { Thumbnail = ThumbnailMode.Placeholder }); return ops.ImportScene(saved, User); }),
        };
        var failed = new List<string>();
        foreach (var (name, label, op) in table)
        {
            string before = Doc(app);
            int n0 = Undos(app);
            OpResult r;
            try { r = op(); }
            catch (Exception e) { failed.Add(name + " threw " + e.GetType().Name + ": " + e.Message); continue; }
            app.Runtime.ApplyNow();
            string after = Doc(app);
            bool docChange = name == "SetTitle" ? app.Session.Model.Title == "O2 scene" : after != before;   // the title lives in the manifest
            bool ok = r.Ok && r.Status == CommandStatus.Ok && Undos(app) == n0 + 1 && Top(app) == label && docChange;
            if (!ok) { failed.Add($"{name}: {r} entries {Undos(app) - n0} top \"{Top(app)}\" changed {docChange}"); continue; }
            var u = app.Session.Commands.Undo();
            app.Runtime.ApplyNow();
            string undone = Doc(app);
            var rd = app.Session.Commands.Redo();
            app.Runtime.ApplyNow();
            string redone = Doc(app);
            if (!u.Ok || undone != before) failed.Add(name + ": undo does not restore scene.doc");
            if (!rd.Ok || redone != after) failed.Add(name + ": redo does not restore scene.doc");
        }
        Check($"{table.Count} ops: Ok, one entry with the label, scene.doc changed, undo / redo restore its bytes", failed.Count == 0,
            failed.Count + " problems: " + string.Join("; ", failed.Take(8)));
        foreach (var x in failed) Info(x);
        try { File.Delete(saved); } catch (Exception) { }
        app.Dispose();
    }

    // ================================================================ O3

    static void O3()
    {
        var f = Make();
        var app = f.App;
        var ops = f.Ops;
        int n0 = Undos(app);
        string before = Doc(app);
        var o = new ExecuteOptions { Source = CommandSource.User, Label = "UI batch" };
        using (var tx = app.Session.Commands.BeginOrJoin("UI batch", o))
        {
            var a = ops.SetFace(new[] { f.F }, new FaceEdit { Mouth = 2 }, User);
            var b = ops.SetHands(new[] { f.F }, 1, 1, User);
            var c = ops.SetClothes(new[] { f.F }, 0, 2, true, User);
            var d = ops.SetTransform(new[] { f.I }, new Vector3(3, 0, 0), null, null, User);
            Check("ops inside an open transaction succeed", a.Ok && b.Ok && c.Ok && d.Ok, a + " " + b + " " + c + " " + d);
            Check("nothing recorded while the transaction is open", Undos(app) == n0);
            tx.Commit();
        }
        Check("one entry with the transaction's label", Undos(app) == n0 + 1 && Top(app) == "UI batch", Undos(app) - n0 + " " + Top(app));
        app.Session.Commands.Undo();
        app.Runtime.ApplyNow();
        Check("one undo restores all four", Doc(app) == before);
        using (var tx = app.Session.Commands.BeginOrJoin("UI drag", o))
        {
            ops.SetState(new[] { f.F }, "wet", 0.9f, User);
            ops.SetItem(new[] { f.I }, new (string, object)[] { ("alpha", 0.2f) }, User);
            tx.Cancel();
        }
        app.Runtime.ApplyNow();
        Check("Cancel reverts every op of the transaction, nothing recorded", Doc(app) == before && Undos(app) == n0);
        app.Dispose();
    }

    // ================================================================ O4 parity

    static void O4()
    {
        var a = Make();
        string startDoc = Doc(a.App);
        var b = new Fixture { F = a.F, I = a.I, I2 = a.I2, L = a.L, Cam = a.Cam, D = a.D };   // the op side's ids (same seed)
        var idle = services.Db.FindAnime(19, 0, 0);
        var rows = new List<(string name, object req, Func<StudioOps, OpResult> op)>
        {
            ("item.set", new { cmd = "item.set", key = a.I, set = new { alpha = 0.5 } }, o => o.SetItem(new[] { b.I }, new (string, object)[] { ("alpha", 0.5f) }, Bridge("Item alpha"))),
            ("obj.visible", new { cmd = "obj.visible", key = a.I2, visible = false }, o => o.SetVisible(new[] { b.I2 }, false, Bridge("Visibility"))),
            ("light.set", new { cmd = "light.set", key = a.L, set = new { intensity = 1.5, range = 30 } }, o => o.SetLight(new[] { b.L }, new List<(string, object)> { ("light.intensity", 1.5f), ("light.range", 30f) }, Bridge("Light"))),
            ("charaLight.set", new { cmd = "charaLight.set", set = new { intensity = 0.7, rot = new[] { 30, 45 } } }, o => o.SetCharaLight(new List<(string, object)> { ("charaLight.intensity", 0.7f), ("charaLight.rot", new[] { 30f, 45f }) }, Bridge("Chara light"))),
            ("camera.slot", new { cmd = "camera.slot", index = 2, op = "save" }, o => o.SaveCameraSlot(2, Bridge("Camera slot"))),
            ("sound.set", new Dictionary<string, object> { ["cmd"] = "sound.set", ["set"] = new Dictionary<string, object> { ["bgm.play"] = true } }, o => o.SetSound(new List<(string, object)> { ("sound.bgm.play", true) }, Bridge("Sound"))),
            ("effects.set", new Dictionary<string, object> { ["cmd"] = "effects.set", ["set"] = new Dictionary<string, object> { ["bloom.intensity"] = 5 } }, o => o.SetEffects(new List<(string, object)> { ("effects.bloom.intensity", 5f) }, Bridge("Effects"))),
            ("effects.reset", new { cmd = "effects.reset", blocks = new[] { "bloom" } }, o => o.ResetEffects(new[] { "bloom" }, Bridge("Reset effects"))),
            ("map.set", new { cmd = "map.set", option = false, pos = new[] { 0, 1, 0 } }, o => o.SetMap(new Vector3(0, 1, 0), null, false, null, Bridge(null))),
            ("obj.transform", new { cmd = "obj.transform", key = a.F, pos = new[] { 1, 2, 3 }, rot = new[] { 0, 45, 0 } }, o => o.SetTransform(new[] { b.F }, new Vector3(1, 2, 3), new Vector3(0, 45, 0), null, Bridge("Transform"))),
            ("chara.anime", new { cmd = "chara.anime", key = a.F, group = 19, category = 0, no = 0, time = 0.3 }, o => o.SetAnimation(new[] { b.F }, idle, 0.3f, null, null, null, Bridge("Animation"))),
            ("chara.speed", new { cmd = "chara.speed", key = a.F, speed = 0.5, loop = true }, o => o.SetAnimeSpeed(new[] { b.F }, 0.5f, true, Bridge("Animation speed"))),
            ("chara.animeOption", new { cmd = "chara.animeOption", key = a.F, visible = false }, o => o.SetAnimeOption(new[] { b.F }, false, null, null, Bridge("Animation options"))),
            ("chara.face", new { cmd = "chara.face", key = a.F, mouth = 2, eyesOpen = 0.4, eyebrow = 1, blush = 0.2 }, o => o.SetFace(new[] { b.F }, new FaceEdit { Mouth = 2, EyesOpen = 0.4f, Eyebrow = 1, Cheek = 0.2f }, Bridge("Face"))),
            ("chara.hands", new { cmd = "chara.hands", key = a.F, left = 2 }, o => o.SetHands(new[] { b.F }, 2, null, Bridge("Hands"))),
            ("look.target", new { cmd = "look.target", key = a.F, pos = new[] { 0, 14, 4 } }, o => o.SetLookTarget(b.F, new Vector3(0, 14, 4), Bridge("Look target"))),
            ("fk.set hips", new { cmd = "fk.set", key = a.F, hipsOffset = 0.2, hipsPosition = new[] { 0.1, 0.8, 0 } }, o => o.SetFkHips(new[] { b.F }, 0.2f, true, new Vec3(0.1f, 0.8f, 0f), Bridge("FK bones"))),
        };
        var side = new List<(string doc, string top, int undos, bool ok)>();
        // one bridge per process: the bridge side runs alone first, then a second host with the same ids runs the ops
        foreach (var (name, req, _) in rows)
        {
            var ra = Call(a.App, req);
            a.App.Runtime.ApplyNow();
            side.Add((Doc(a.App), Top(a.App), Undos(a.App), ra.Value<bool?>("ok") == true));
        }
        var saveA = Call(a.App, new { cmd = "chara.save", key = a.F, name = "o4_bridge", overwrite = true, thumb = "placeholder" });
        string pathA = (string)saveA["path"];
        a.App.Dispose();
        b = Make();
        Check("the op host has the same ids and start document", b.F == a.F && b.I == a.I && b.L == a.L && Doc(b.App) == startDoc);
        int ok = 0, k = 0;
        foreach (var (name, _, op) in rows)
        {
            var rb = op(b.Ops);
            b.App.Frame();   // AppCore.Call runs the handler at the start of one frame (the characters step after it)
            var x = side[k++];
            bool same = x.ok && rb.Ok && x.doc == Doc(b.App) && x.top == Top(b.App) && x.undos == Undos(b.App);
            if (same) ok++;
            else Info($"{name}: bridge ok {x.ok} op {rb}; doc equal {x.doc == Doc(b.App)}; labels \"{x.top}\" / \"{Top(b.App)}\"; entries {x.undos} / {Undos(b.App)}");
        }
        Check($"bridge handler and StudioOps give equal scene.doc bytes, labels and entry counts ({ok} / {rows.Count} handlers)", ok == rows.Count);
        // chara.save vs StudioOps.SaveChara: the same character documents and reply members (the files differ in their timestamps)
        string pathB = Path.Combine(b.App.Paths.CharaDir, "o4_ops.hs2nchara");
        int n0 = Undos(b.App);
        var sb = b.Ops.SaveChara(b.F, pathB, false);
        var vb = sb.Value as CharaSaveResult;
        var la = pathA != null ? CharaContainer.Load(pathA) : null;
        var lb = File.Exists(pathB) ? CharaContainer.Load(pathB) : null;
        bool docs = la != null && lb != null && la.Ok && lb.Ok
                    && Sha(DocClone.Bytes(la.Value.Subtree.Chara)) == Sha(DocClone.Bytes(lb.Value.Subtree.Chara))
                    && Sha(DocClone.Bytes(la.Value.Subtree.Coord)) == Sha(DocClone.Bytes(lb.Value.Subtree.Coord));
        Check("chara.save and SaveChara write the same character documents, thumbnail kind, nothing recorded",
            saveA.Value<bool?>("ok") == true && sb.Ok && vb != null && docs && (string)saveA["thumbnail"] == vb.Thumbnail && vb.Thumbnail == "placeholder" && Undos(b.App) == n0,
            Ctx(saveA) + " / " + sb + " docs " + docs);
        foreach (var x in new[] { pathA, pathB }) try { if (x != null) File.Delete(x); } catch (Exception) { }
        b.App.Dispose();
    }

    static ExecuteOptions Bridge(string label) => new ExecuteOptions { Source = CommandSource.Bridge, Label = label };

    // ================================================================ O5

    static void O5()
    {
        var f = Make();
        var app = f.App;
        var ops = f.Ops;
        var idle = services.Db.FindAnime(19, 0, 0);
        ops.SetTransform(new[] { f.F }, new Vector3(2, 0, 1), new Vector3(0, 90, 0), new Vector3(1.1f, 1.1f, 1.1f), User);
        ops.Move(f.I, f.F, "N_Hand_R", -1, false, User);
        ops.SetAnimation(new[] { f.F }, idle, 0.4f, 0.7f, null, null, User);
        ops.SetFkEnabled(f.F, true, User);
        ops.SetFace(new[] { f.F }, new FaceEdit { Mouth = 3, Tears = 0.5f }, User);
        ops.SetLook(new[] { f.F }, 2, 3, User);
        ops.SetClothes(new[] { f.F }, 0, 2, true, User);
        ops.SetState(new[] { f.F }, "wet", 0.3f, User);
        ops.Rename(f.F, "O5 character", User);
        app.Runtime.ApplyNow();
        string json0 = app.Session.NodeJson(f.F);
        var app0 = app.Session.Model.Find(f.F).Appearance;
        var r = ops.ChangeCharacter(new[] { f.F }, maleCard, null, User);
        app.Runtime.ApplyNow();
        var n = app.Session.Model.Find(f.F);
        var real = app.Runtime.FindChara(f.F);
        Check("ChangeCharacter: Ok, built as the male card", r.Ok && real?.State == RealizationState.Built && real.Sex == 0, r + " " + real?.State);
        var j0 = JObject.Parse(json0);
        var j1 = JObject.Parse(app.Session.NodeJson(f.F));
        foreach (var j in new[] { j0, j1 }) (j.SelectToken("chara.state") as JObject)?.Remove("accessories");
        Check("the node keeps id, transform, animation, FK, face, look, state and name (node JSON identical but the accessory map, which follows the new outfit)",
            JToken.DeepEquals(j0, j1), Diff(j0, j1));
        Check("the child keeps its parent and attach point", app.Session.Model.Find(f.I)?.Parent == n && app.Session.Model.Find(f.I)?.Point == "N_Hand_R");
        Check("the appearance changed (a new object)", !ReferenceEquals(n.Appearance, app0));
        app.Session.Commands.Undo();
        app.Runtime.ApplyNow();
        Check("undo swaps the appearance back (the same object) and rebuilds the female", ReferenceEquals(app.Session.Model.Find(f.F).Appearance, app0) && app.Runtime.FindChara(f.F)?.Sex == 1);
        var bad = ops.ChangeCharacter(new[] { f.I }, maleCard, null, User);
        Check("ChangeCharacter on an item → wrong_kind", bad.Code == CommandCodes.WrongKind, bad.ToString());
        var missing = ops.ChangeCharacter(new[] { f.F }, Path.Combine(OutDir, "nosuch.png"), null, User);
        Check("ChangeCharacter with an unreadable card → bad_args", missing.Code == CommandCodes.BadArgs, missing.ToString());
        app.Dispose();
    }

    static string Diff(JToken a, JToken b, string path = "")
    {
        if (JToken.DeepEquals(a, b)) return "";
        if (a is JObject oa && b is JObject ob)
        {
            foreach (var p in oa.Properties())
            {
                var d = Diff(p.Value, ob[p.Name], path + "." + p.Name);
                if (d.Length > 0) return d;
            }
            foreach (var p in ob.Properties()) if (oa[p.Name] == null) return path + "." + p.Name + " added";
        }
        return path + ": " + Ctx(a) + " vs " + Ctx(b);
    }

    static string Ctx(JToken t) { var s = t?.ToString(Formatting.None) ?? "null"; return s.Length > 120 ? s.Substring(0, 120) + "…" : s; }

    // ================================================================ O6

    static void O6()
    {
        var f = Make();
        var app = f.App;
        var ops = f.Ops;
        ops.Move(f.I, f.D, null, -1, false, User);
        app.Session.AddRoute(null, null, -1, User);
        app.Runtime.ApplyNow();
        var file = Path.Combine(OutDir, "o6_source.hs2nscene");
        var sr = app.Runtime.Ops.Save(file, new SaveOptions { Thumbnail = ThumbnailMode.Placeholder });
        Check("source scene saved", sr.Ok, sr.Error?.Message);
        var fileIds = new HashSet<string>(app.Session.Model.PreOrder().Select(x => x.Id));
        var filePoints = new HashSet<string>(app.Session.Model.PreOrder().Select(x => x.Doc.State).OfType<RouteNodeState>().SelectMany(x => x.Points).Select(p => p.Id));
        int nodes = fileIds.Count;
        var r1 = ops.ImportScene(file, User);
        var r2 = ops.ImportScene(file, User);
        app.Runtime.ApplyNow();
        var now = app.Session.Model.PreOrder().ToList();
        Check("two imports: Ok, the node count tripled", r1.Ok && r2.Ok && now.Count == 3 * nodes, now.Count + " vs " + nodes);
        Check("every node id is unique", now.Select(x => x.Id).Distinct().Count() == now.Count);
        var imported = now.Where(x => !fileIds.Contains(x.Id)).ToList();
        Check("the imported nodes have new ids", imported.Count == 2 * nodes);
        var importedItem = imported.Where(x => x.Kind == NodeKind.Item && x.Parent != null).ToList();
        Check("parents inside the imported subtree remapped (item under the imported folder)", importedItem.Count == 2 && importedItem.All(x => x.Parent.Kind == NodeKind.Folder && !fileIds.Contains(x.Parent.Id)));
        var pts = imported.Select(x => x.Doc.State).OfType<RouteNodeState>().SelectMany(x => x.Points).Select(p => p.Id).ToList();
        Check("route point ids new", pts.Count > 0 && pts.All(p => !filePoints.Contains(p)) && pts.Distinct().Count() == pts.Count, pts.Count + " points");
        Check("the second import's top nodes are selected", app.Session.Selection.Ids.SequenceEqual(r2.NewIds) && r2.NewIds.All(id => app.Session.Model.Find(id)?.Parent == null));
        Check("imported characters are built", imported.Where(x => x.Kind == NodeKind.Chara).All(x => app.Runtime.FindChara(x.Id)?.State == RealizationState.Built));
        app.Session.Commands.Undo();
        app.Runtime.ApplyNow();
        Check("undo removes one import", app.Session.Model.Count == 2 * nodes);
        try { File.Delete(file); } catch (Exception) { }
        app.Dispose();
    }

    // ================================================================ O7

    static void O7()
    {
        var f = Make();
        var app = f.App;
        var ops = f.Ops;
        int n0 = Undos(app);
        string before = Doc(app);
        var drag = ops.BeginTransformDrag(new[] { f.I }, out var fail, User);
        Check("BeginTransformDrag opens", drag != null && drag.IsOpen, fail?.ToString());
        for (int i = 1; i <= 5; i++) drag.Set(f.I, new Vector3(i * 0.5f, 0, 0));
        var holder = app.Runtime.Find(f.I)?.Holder;
        Check("the drag previews (the holder follows)", holder && Mathf.Abs(holder.localPosition.x - 2.5f) < 1e-5f && Undos(app) == n0);
        var c = drag.Commit();
        Check("Commit: one entry \"Transform\"", c.Ok && Undos(app) == n0 + 1 && Top(app) == "Transform", c + " " + Top(app));
        var d2 = ops.BeginTransformDrag(new[] { f.I }, out fail, User);
        d2.Set(f.I, new Vector3(9, 9, 9));
        d2.Cancel();
        Check("Cancel: no entry, the transform back", Undos(app) == n0 + 1 && Mathf.Abs(app.Session.Model.Find(f.I).Doc.Transform.Pos.X - 2.5f) < 1e-5f);
        app.Session.Commands.Undo();
        app.Runtime.ApplyNow();
        Check("undo of the drag restores scene.doc", Doc(app) == before);
        int n1 = Undos(app);
        ops.RestartAnimation(new[] { f.F });
        ops.RestartItemAnimation(new[] { f.I });
        ops.LoadCameraSlot(0, User);
        ops.Select(new[] { f.I }, false, null);
        Check("RestartAnimation, RestartItemAnimation, LoadCameraSlot, Select record nothing", Undos(app) == n1);
        // "Use card state": at add (one entry "Add character") and on an existing node ("Use card state")
        int n2 = Undos(app);
        var add = ops.AddChara(femaleCard, null, null, -1, new CardAddOptions { UseCardState = true }, User);
        Check("AddChara with UseCardState: one entry \"Add character\"", add.Ok && add.NewIds.Count == 1 && Undos(app) == n2 + 1 && Top(app) == "Add character", add + " " + Top(app));
        var use = ops.UseCardState(new[] { f.F }, femaleCard, User);
        Check("UseCardState on a node: Ok (an entry \"Use card state\" when the card's Status block differs from the node)", use.Ok && (use.Status == CommandStatus.NoOp || Top(app) == "Use card state"), use + " " + Top(app));
        var noPath = ops.UseCardState(new[] { f.F }, null, User);
        Check("UseCardState of a document-stored character without a card path → bad_args", noPath.Code == CommandCodes.BadArgs, noPath.ToString());
        app.Dispose();
    }

    // ================================================================ O12

    static readonly List<string> names = new List<string>();
    static readonly List<Vector3> roots = new List<Vector3>();

    /// <summary>World positions of every dynamic-bone particle with a transform; <see cref="names"/> = chain root / bone, <see cref="roots"/> = the chain root's position per particle.</summary>
    static List<Vector3> Particles(CharaRealization r)
    {
        names.Clear();
        roots.Clear();
        var l = new List<Vector3>();
        var st = r?.Motion?.Get<DynamicBoneStage>();
        if (st == null) return l;
        foreach (var ch in st.Chains)
            for (int i = 0; i < ch.ParticleCount; i++)
            {
                var t = ch.ParticleTransform(i);
                if (!t) continue;
                l.Add(t.position);
                names.Add((ch.Root ? ch.Root.name : "?") + "/" + t.name + (ch.Active ? "" : " (inactive)"));
                roots.Add(ch.Root ? ch.Root.position : Vector3.zero);
            }
        return l;
    }

    static void O12()
    {
        // first without the reset (the comparison number), then with it (the gate)
        CharaRealization.PhysicsResetOnReparent = false;
        try { Measure("O12 without the physics reset: " + O12Revive(out _, out _)); }
        finally { CharaRealization.PhysicsResetOnReparent = true; }
        O12Investigation();
        O12Body();
    }

    // ---------------------------------------------------------------- O12 investigation (Phase 3.6 package K)

    /// <summary>Chain cf_J_Legsk_05_00 of a character: particles (x, p), bone locals, root pose, colliders, as text.</summary>
    static string DumpSkirt05(CharaRealization r, string label)
    {
        var st = r?.Motion?.Get<DynamicBoneStage>();
        var ch = st?.Chains.FirstOrDefault(c => c.Root && c.Root.name == "cf_J_Legsk_05_00");
        if (ch == null) return label + ": no chain cf_J_Legsk_05_00";
        var sb = new StringBuilder();
        sb.Append(label).Append(": active ").Append(ch.Active).Append(", root ").Append(ch.Root.position.ToString("F5")).Append(" ").Append(ch.Root.rotation.eulerAngles.ToString("F3"))
          .Append(", owner ").Append(ch.Owner ? ch.Owner.position.ToString("F5") : "-").Append(", colliders ").Append(ch.ColliderCount);
        for (int k = 0; k < ch.ColliderCount; k++)
        {
            int ci = ch.ColliderIndex(k);
            var col = ci >= 0 && ci < st.Colliders.Count ? st.Colliders.Source(ci) : null;
            sb.Append(k == 0 ? " [" : ", ").Append(col ? col.name + " " + col.transform.position.ToString("F4") : "?");
            if (k == ch.ColliderCount - 1) sb.Append("]");
        }
        for (int i = 0; i < ch.ParticleCount; i++)
        {
            var t = ch.ParticleTransform(i);
            sb.Append("\n      p").Append(i).Append(' ').Append(t ? t.name : "(end)").Append(" x ").Append(ch.Position(i).ToString("F5")).Append(" prev ").Append(ch.PrevPosition(i).ToString("F5"));
            if (t) sb.Append(" localRot ").Append(t.localRotation.ToString("F6")).Append(" localPos ").Append(t.localPosition.ToString("F5"));
        }
        return sb.ToString();
    }

    /// <summary>
    /// The O12 history with a hook between the delete and the undo (the character buried): the max particle distance after one
    /// step to a fresh build of the same pose, and the dumps of cf_J_Legsk_05 before the step.
    /// </summary>
    static string O12Variant(string name, Action<CharaRealization> buried, bool dump)
    {
        var idle = services.Db.FindAnime(19, 0, 0);
        var a = Make(Seed + 20);
        a.App.Runtime.SetManualStepping(true);
        a.Ops.SetAnimation(new[] { a.F }, idle, 0f, 0f, null, null, User);
        a.Ops.SetLook(new[] { a.F }, 0, 4, User);
        var r = a.App.Runtime.FindChara(a.F);
        for (int i = 0; i < 10; i++) r.Motion.Step(1f / 60f);
        a.Ops.SetTransform(new[] { a.F }, new Vector3(3, 0, 0), null, null, User);
        for (int i = 0; i < 2; i++) r.Motion.Step(1f / 60f);
        a.Ops.Delete(new[] { a.F }, User);
        a.App.Runtime.ApplyNow();
        buried?.Invoke(a.App.Runtime.FindChara(a.F));
        a.App.Session.Commands.Undo();
        a.App.Runtime.ApplyNow();
        r = a.App.Runtime.FindChara(a.F);
        if (dump) Info("O12 dump " + DumpSkirt05(r, name + ", revived, before the step"));
        r.Motion.Step(1f / 60f);
        var revived = Particles(r);
        var b = Make(Seed + 21);
        b.App.Runtime.SetManualStepping(true);
        b.Ops.SetAnimation(new[] { b.F }, idle, 0f, 0f, null, null, User);
        b.Ops.SetLook(new[] { b.F }, 0, 4, User);
        b.Ops.SetTransform(new[] { b.F }, new Vector3(3, 0, 0), null, null, User);
        var rb = b.App.Runtime.FindChara(b.F);
        if (dump) Info("O12 dump " + DumpSkirt05(rb, "fresh build, before the step"));
        rb.Motion.Step(1f / 60f);
        var fresh = Particles(rb);
        int within = revived.Count == fresh.Count ? revived.Zip(fresh, (x, y) => Vector3.Distance(x, y) <= 1e-4f ? 1 : 0).Sum() : 0;
        float max = revived.Count == fresh.Count ? revived.Zip(fresh, (x, y) => Vector3.Distance(x, y)).DefaultIfEmpty(0).Max() : float.PositiveInfinity;
        a.App.Dispose();
        b.App.Dispose();
        return $"{name}: {within} / {revived.Count} particles within 1e-4 of a fresh build, max {max:G4}";
    }

    static void O12Investigation()
    {
        // the 3.5 behaviour (physics reset after the revive only): the skirt chain cf_J_Legsk_05 differs
        CharaRealization.RestPoseBeforeRevive = false;
        try
        {
            Measure("O12 investigation: " + O12Variant("3.5 revive (reset after the revive only)", null, true));
            // hypothesis (a): the revive re-enables the Animator, which binds the chain bones' physics-displaced pose as its
            // defaults; the game's bone reset before that bind (DynamicBoneChain.ResetBeforeUpdate) while buried avoids it
            Measure("O12 investigation: " + O12Variant("chain bones reset while buried by hand (a)", c => c?.Motion?.Get<DynamicBoneStage>()?.ResetBeforeUpdate(), false));
            // hypothesis (c): stale collider positions: a collider table update while buried
            Measure("O12 investigation: " + O12Variant("collider table updated while buried (c)", c => c?.Motion?.Get<DynamicBoneStage>()?.Colliders.Update(), false));
        }
        finally { CharaRealization.RestPoseBeforeRevive = true; }
        Measure("O12 investigation: " + O12Variant("3.6 revive (SceneRuntime.BeforeRevive: rest pose before the re-enable)", null, false));
    }

    /// <summary>Delete a moving character, undo, one step; the max particle distance to a fresh build of the same pose.</summary>
    static string O12Revive(out List<Vector3> revived, out List<Vector3> fresh)
    {
        var idle = services.Db.FindAnime(19, 0, 0);
        var a = Make(Seed + 10);
        a.App.Runtime.SetManualStepping(true);
        a.Ops.SetAnimation(new[] { a.F }, idle, 0f, 0f, null, null, User);
        a.Ops.SetLook(new[] { a.F }, 0, 4, User);
        var r = a.App.Runtime.FindChara(a.F);
        for (int i = 0; i < 10; i++) r.Motion.Step(1f / 60f);
        a.Ops.SetTransform(new[] { a.F }, new Vector3(3, 0, 0), null, null, User);
        for (int i = 0; i < 2; i++) r.Motion.Step(1f / 60f);
        a.Ops.Delete(new[] { a.F }, User);
        a.App.Session.Commands.Undo();
        a.App.Runtime.ApplyNow();
        r = a.App.Runtime.FindChara(a.F);
        r.Motion.Step(1f / 60f);
        revived = Particles(r);
        var b = Make(Seed + 11);
        b.App.Runtime.SetManualStepping(true);
        b.Ops.SetAnimation(new[] { b.F }, idle, 0f, 0f, null, null, User);
        b.Ops.SetLook(new[] { b.F }, 0, 4, User);
        b.Ops.SetTransform(new[] { b.F }, new Vector3(3, 0, 0), null, null, User);
        var rb = b.App.Runtime.FindChara(b.F);
        rb.Motion.Step(1f / 60f);
        fresh = Particles(rb);
        var rv = revived; var fr = fresh;
        int within = rv.Count == fr.Count ? rv.Zip(fr, (x, y) => Vector3.Distance(x, y) <= 1e-4f ? 1 : 0).Sum() : 0;
        float max = rv.Count == fr.Count ? rv.Zip(fr, (x, y) => Vector3.Distance(x, y)).DefaultIfEmpty(0).Max() : float.PositiveInfinity;
        a.App.Dispose();
        b.App.Dispose();
        return $"{within} / {rv.Count} particles within 1e-4 of a fresh build, max {max:G4}";
    }

    static void O12Body()
    {
        var idle = services.Db.FindAnime(19, 0, 0);
        var a = Make();
        var ops = a.Ops;
        var app = a.App;
        app.Runtime.SetManualStepping(true);
        ops.SetAnimation(new[] { a.F }, idle, 0f, 0f, null, null, User);
        ops.SetLook(new[] { a.F }, 0, 4, User);   // neck FIX: the neck look smooths over frames (not physics)
        var r = app.Runtime.FindChara(a.F);
        for (int i = 0; i < 10; i++) r.Motion.Step(1f / 60f);
        ops.SetTransform(new[] { a.F }, new Vector3(3, 0, 0), null, null, User);   // a sudden move: the chains lag behind
        for (int i = 0; i < 2; i++) r.Motion.Step(1f / 60f);
        Exception ex = null;
        try
        {
            ops.Delete(new[] { a.F }, User);
            app.Session.Commands.Undo();
            app.Runtime.ApplyNow();
        }
        catch (Exception e) { ex = e; }
        r = app.Runtime.FindChara(a.F);
        Check("delete -> undo: no exception, the character revived from the graveyard", ex == null && r != null && r.State == RealizationState.Built && app.Runtime.LastRevived > 0, ex?.Message + " revived " + app.Runtime.LastRevived);
        try { r.Motion.Step(1f / 60f); } catch (Exception e) { ex = e; }
        Check("a step after the revive runs without an exception", ex == null, ex?.Message);
        var revived = Particles(r);
        var revivedRoots = new List<Vector3>(roots);

        var b = Make(Seed + 1);
        b.App.Runtime.SetManualStepping(true);
        b.Ops.SetAnimation(new[] { b.F }, idle, 0f, 0f, null, null, User);
        b.Ops.SetLook(new[] { b.F }, 0, 4, User);
        b.Ops.SetTransform(new[] { b.F }, new Vector3(3, 0, 0), null, null, User);
        var rb = b.App.Runtime.FindChara(b.F);
        rb.Motion.Step(1f / 60f);
        var fresh = Particles(rb);
        var freshRoots = new List<Vector3>(roots);
        // a chain whose root bone itself is posed differently is not a dynamic-bone history difference (the root is not simulated)
        float max = 0f, maxRootMoved = 0f;
        int excluded = 0;
        if (revived.Count != fresh.Count) max = float.PositiveInfinity;
        else
            for (int i = 0; i < revived.Count; i++)
            {
                float d = Vector3.Distance(revived[i], fresh[i]);
                if (Vector3.Distance(revivedRoots[i], freshRoots[i]) > 1e-5f) { excluded++; maxRootMoved = Mathf.Max(maxRootMoved, d); continue; }
                max = Mathf.Max(max, d);
            }
        Measure($"O12: particles of chains whose root bone differs (not simulated, excluded from the gate): {excluded}, their max distance {maxRootMoved:G4}");
        Measure($"O12: {revived.Count} particles; max distance revived vs fresh after one step {max:G4}");
        if (revived.Count == fresh.Count)
            for (int i = 0; i < revived.Count; i++)
                if (Vector3.Distance(revived[i], fresh[i]) > 1e-4f) { Info($"particle {i} ({(i < names.Count ? names[i] : "")}): {revived[i].ToString("F4")} vs {fresh[i].ToString("F4")}"); if (i > 30) break; }
        int within = revived.Count == fresh.Count ? revived.Zip(fresh, (x, y) => Vector3.Distance(x, y) <= 1e-4f ? 1 : 0).Sum() : 0;
        // Phase 3.6 K: the chain bones get their rest pose before the revived holder re-enables the
        // Animator (SceneRuntime.BeforeRevive), so the gate is every particle (was 90 % with a known skirt-chain WARN)
        Check($"the physics reset on revive: every particle equals a fresh build of the same pose within 1e-4 ({within} / {revived.Count})",
            revived.Count > 0 && within == revived.Count, max.ToString("G4"));
        // the same history without delete / undo, reset by hand: the revive path must equal it exactly
        var c3 = Make(Seed + 2);
        c3.App.Runtime.SetManualStepping(true);
        c3.Ops.SetAnimation(new[] { c3.F }, idle, 0f, 0f, null, null, User);
        c3.Ops.SetLook(new[] { c3.F }, 0, 4, User);
        var rc = c3.App.Runtime.FindChara(c3.F);
        for (int i = 0; i < 10; i++) rc.Motion.Step(1f / 60f);
        c3.Ops.SetTransform(new[] { c3.F }, new Vector3(3, 0, 0), null, null, User);
        for (int i = 0; i < 2; i++) rc.Motion.Step(1f / 60f);
        rc.Motion.Get<DynamicBoneStage>()?.Reset();
        foreach (var bs in rc.Motion.GetAll<BustStage>()) bs.Reset();
        rc.Motion.Step(1f / 60f);
        var byHand = Particles(rc);
        var handRoots = new List<Vector3>(roots);
        float max2 = revived.Count == byHand.Count ? 0f : float.PositiveInfinity;
        if (revived.Count == byHand.Count)
            for (int i = 0; i < revived.Count; i++)
                if (Vector3.Distance(revivedRoots[i], handRoots[i]) <= 1e-5f) max2 = Mathf.Max(max2, Vector3.Distance(revived[i], byHand[i]));
        Measure($"O12: max distance revived vs the same history reset by hand {max2:G4}");
        Measure("O12: the revive vs a hand reset of the same history: max " + max2.ToString("G4") + " (the same skirt chain)");
        var ra = app.Runtime.FindChara(a.F);
        var diffs = new List<(float d, string n)>();
        foreach (var kv in ra.Context.BodyBones)
            if (kv.Value && rc.Context.BodyBones.TryGetValue(kv.Key, out var tb) && tb)
                diffs.Add((Vector3.Distance(kv.Value.position, tb.position) + Quaternion.Angle(kv.Value.rotation, tb.rotation) * 1e-3f, kv.Key));
        Info("body bones revived vs hand reset, largest: " + string.Join(", ", diffs.OrderByDescending(x => x.d).Take(8).Select(x => x.n + " " + x.d.ToString("G3"))));
        a.App.Dispose();
        b.App.Dispose();
        c3.App.Dispose();
    }
}

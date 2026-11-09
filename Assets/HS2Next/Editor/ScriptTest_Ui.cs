// ScriptTest, package U's play part (Phase 6.9): the panel «Scene scripts», the Timeline panel's script line and the strings of #U69, with the app
// and its UI in Editor play (port 17919 or -port), BR-Chan in the female role and HERO107 in the male role. Input through ui.input (synthetic pointer, wheel, key and text
// events raised through the panel); no real pointer. Judged by the widgets, the history, the scene's scripts, the store, the files written and the script player's results.
// Frames a recorded pass writes are counted and deleted, never opened. The run's UserData (Spike/out/script/play_userdata): its layout.json and its script store are removed
// before the part (the panel takes its lazy path, the store starts empty) and the store, the frames and the exported files after it.
//   JU1  every key of #U69 in en, ja and zh-Hans with the same placeholders; the texts spec 13.3 gives; the history labels as ScriptEditCommand names them; every English word on
//        the block's list (spec 13.3's words and the mechanical words of the rows); no other subject; every key the panel, its dialogs and the Timeline's script line use is in
//        the table; the action has its label
//   JU2  the panel appears when asked for (lazy, region bottom beside the Timeline panel, the View menu, «Reset layout»); a closed panel reads nothing; a wheel and a drag over
//        the panel leave the camera (the same wheel over the view moves it)
//   JU3  Play, Stop, Prepare again and Record… through the panel equal script.play / script.stop / script.play {prepare: always} / script.play {record}; the list's and the
//        store's controls equal their commands (Loop, Rename, Remove, Keep in the store, Export…, Add to scene, Import…, Delete, Restore, Purge…)
//   JU4  the role picker casts (and «Not cast» uncasts) as script.cast; «Use selected»; uncast rows marked; a prop role's picker offers the items of its row only
//   JU5  the Timeline panel's header: «Script: <name>», «edited» after an edit, «Save edits into the script» as script.capture; hidden for a Timeline of no script
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HS2Next.App;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Scenes.Scripts;
using HS2Next.Scenes.Timeline;
using HS2Next.Screenshots;
using HS2Next.UI;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using HS2Next.UI.Viewport;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class ScriptTest
{
    // ------------------------------------------------------------ the part

    static IEnumerator Play_U_Panel()
    {
        if (!WantAny("JU1", "JU2", "JU3", "JU4", "JU5")) yield break;
        if (POpen("JU1", "the strings of #U69")) JuStrings();
        if (!WantAny("JU2", "JU3", "JU4", "JU5")) yield break;
        if (HasArg("-noUi")) { current = "JU"; Check("the UI (the row runs without -noUi)", false); yield break; }
        // the run's own UserData: no layout.json (the lazy path), no script store
        try { File.Delete(Path.Combine(JuUserData, "config", LayoutDocument.FileName)); } catch (Exception) { }
        JuClearStore();
        yield return PStartApp();
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage == AppStage.Ready && UiHost.Current != null && UiHost.Current.IsReady, 240, ok);
        if (!ok[0]) { current = "JU"; Check("app and UI ready", false); yield break; }
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { current = "JU"; Check("a graphics device", false, "the row needs one"); yield break; }
        yield return PlayModeHarness.WaitUntil(() => JuQ("tb-view") != null && JuQ("tb-view").worldBound.width > 0 && !float.IsNaN(JuQ("tb-view").worldBound.x), 10, ok);
        yield return PFrames(5);
        if (Want("JU2")) yield return JuSafe(JuLazy());
        if (WantAny("JU3", "JU4", "JU5"))
        {
            yield return JuSafe(JuFixture(ok));
            if (ok[0])
            {
                if (Want("JU4")) yield return JuSafe(JuRoles());
                if (Want("JU3")) yield return JuSafe(JuPlay());
                if (Want("JU3")) yield return JuSafe(JuControls());
                if (Want("JU5")) yield return JuSafe(JuHeader());
            }
        }
        try { if (FrameRecorder.IsBusy) FrameRecorder.Current?.Stop(); } catch (Exception) { }
        try { if (JuApp != null) ScriptPlayer.Stop(JuApp, false); } catch (Exception) { }
        JuUi?.Dialogs?.CloseCurrent();
        JuRestoreCapture();
        HS2Next.Motion.Face.FaceSetup.Seed = null;
        yield return PStopApp();
        JuClearStore();
        foreach (var d in juMade) PDeleteFrames(d);
        foreach (var f in juFiles) { try { if (File.Exists(f)) File.Delete(f); } catch (Exception) { } }
        try { if (Directory.Exists(JuExportDir) && Directory.GetFileSystemEntries(JuExportDir).Length == 0) Directory.Delete(JuExportDir); } catch (Exception) { }
    }

    // ------------------------------------------------------------ the app, its UI, input

    static AppCore JuApp => AppCore.Current;
    static UiContext JuUi => UiHost.Current?.Context;
    static IPanel JuPanel => UiHost.Current?.Root?.panel;
    static ScriptsPanel JuSp => ScriptsPanel.Current;
    static int JuUndos => JuApp.Session.Commands.UndoList.Count;
    static string JuTop => JuApp.Session.Commands.UndoList.Count > 0 ? JuApp.Session.Commands.UndoList[0].Label : null;
    static string JuUserData => Path.GetFullPath(Path.Combine(ContentSettings.ProjectRoot, PlayUserData));
    static string JuStoreDir => Path.Combine(JuUserData, ScriptStore.FolderName);
    static string JuOutRoot => JuApp?.Paths?.OutputDir ?? Path.GetFullPath(Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "script", "play_out"));
    static string JuExportDir => Path.Combine(JuOutRoot, "u_export");

    static string juA, juB, juItem, juScript;
    static readonly List<string> juMade = new List<string>();
    static readonly List<string> juFiles = new List<string>();
    static string juCapMode = CaptureSettings.ModeFit;
    static int juCapW = 1920, juCapH = 1080, juCapSs = 1;
    static bool juCapFrame = true, juCapSaved;

    /// <summary>The capture settings JU3's record changed, put back (once).</summary>
    static void JuRestoreCapture()
    {
        if (!juCapSaved) return;
        CaptureSettings.Mode = juCapMode; CaptureSettings.Width = juCapW; CaptureSettings.Height = juCapH; CaptureSettings.Supersample = juCapSs; CaptureSettings.Frame = juCapFrame;
        juCapSaved = false;
    }

    /// <summary>Removes the run's script store (its files, then the folder) and nothing else.</summary>
    static void JuClearStore()
    {
        try
        {
            if (!Directory.Exists(JuStoreDir)) return;
            foreach (var f in Directory.GetFiles(JuStoreDir)) File.Delete(f);
            if (Directory.GetFileSystemEntries(JuStoreDir).Length == 0) Directory.Delete(JuStoreDir);
        }
        catch (Exception e) { Debug.LogWarning("[ScriptTest] the play store was not removed: " + e.Message); }
    }

    /// <summary>Runs a group's coroutine, nested ones too: an exception is a FAIL of the group, and the part goes on with the next group.</summary>
    static IEnumerator JuSafe(IEnumerator body)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(body);
        while (stack.Count > 0)
        {
            object cur;
            try
            {
                if (!stack.Peek().MoveNext()) { stack.Pop(); continue; }
                cur = stack.Peek().Current;
            }
            catch (Exception e)
            {
                Check("the group ran without an exception", false, e.GetType().Name + ": " + e.Message + " " + e.StackTrace);
                yield break;
            }
            if (cur is IEnumerator inner) stack.Push(inner);
            else yield return cur;
        }
    }

    static VisualElement JuQ(string name) => JuPanel?.visualTree.Q(name);
    static string JuText(string name) => (JuQ(name) as TextElement)?.text;

    static bool JuVisible(VisualElement e)
    {
        if (e == null || e.panel == null) return false;
        for (var x = e; x != null; x = x.hierarchy.parent) if (x.resolvedStyle.display == DisplayStyle.None) return false;
        return e.worldBound.width > 0;
    }

    static JObject JuPt(Vector2 p, string phase, int button = 0) =>
        new JObject { ["type"] = "pointer", ["x"] = p.x, ["y"] = p.y, ["phase"] = phase, ["button"] = button, ["clicks"] = 1 };

    static JObject JuKey(string key) => new JObject { ["type"] = "key", ["key"] = key };
    static JObject JuTyped(string text) => new JObject { ["type"] = "text", ["text"] = text };
    static JObject JuWheel(Vector2 p, float delta) => new JObject { ["type"] = "wheel", ["x"] = p.x, ["y"] = p.y, ["delta"] = delta };

    static IEnumerator JuIn(params JObject[] events)
    {
        yield return PCall(JuApp, new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(events.Cast<object>().ToArray()), ["frames"] = 1 });
        if (!POk) Check("ui.input", false, Short(pLastText));
        yield return PFrames(1);
    }

    static IEnumerator JuReveal(VisualElement e)
    {
        for (var x = e?.hierarchy.parent; x != null; x = x.hierarchy.parent)
            if (x is Foldout f && !f.value) f.value = true;
        yield return PFrames(2);
        for (var x = e?.hierarchy.parent; x != null; x = x.hierarchy.parent)
            if (x is ScrollView sv) sv.ScrollTo(e);
        yield return PFrames(3);
    }

    /// <summary>A click on an element of the UI by name, brought into sight first; a toast over its centre is waited out (a press on a toast only takes it away).</summary>
    static IEnumerator JuClick(string name)
    {
        var e = JuQ(name);
        yield return JuReveal(e);
        e = JuQ(name);
        bool seen = e != null && JuVisible(e);
        if (!Check("the element " + name, seen, seen ? "" : e == null ? "absent" : "not visible")) yield break;
        if (!e.enabledInHierarchy)
        {
            // the panel reads its state four times a second: a button a command has just enabled may lag behind by one read
            var en = new bool[1];
            var el = e;
            yield return PlayModeHarness.WaitUntil(() => el.enabledInHierarchy, 2, en);
            if (!en[0]) Info("the element " + name + " is disabled: the click reaches nothing");
        }
        var at = JuPanel.Pick(e.worldBound.center);
        if (at != null && at.name == "toast")
        {
            var gone = new bool[1];
            yield return PlayModeHarness.WaitUntil(() => JuUi.Toasts.Count == 0, 6, gone);
            Info("a toast covered " + name + ": waited until the toasts expired (" + (gone[0] ? "gone" : "still there") + ")");
        }
        at = JuPanel.Pick(e.worldBound.center);
        bool on = false;
        for (var x = at; x != null; x = x.hierarchy.parent) if (x == e) { on = true; break; }
        if (!on) Info("the point " + e.worldBound.center + " of " + name + " lies on " + (at == null ? "nothing" : at.GetType().Name + " " + at.name));
        yield return JuIn(JuPt(e.worldBound.center, "click"));
        yield return PFrames(2);
    }

    static string JuCam()
    {
        var t = JuUi.Viewport.ViewCamera.transform;
        return t.position.ToString("F4") + " " + t.rotation.ToString("F4");
    }

    /// <summary>A bridge request through the app's bridge; the reply in pLast; false when it is not ok (a FAIL line with the reply).</summary>
    static IEnumerator JuCmd(object req, string what, bool[] ok = null)
    {
        yield return PCall(JuApp, req);
        if (ok != null) ok[0] = POk;
        if (!POk) Check(what, false, Short(pLastText));
    }

    static SceneScript JuScriptOf(string id) => JuApp.Session.Model.Scripts?.Find(id);

    /// <summary>A script's document as JSON without its id (the id differs between two copies), for comparing.</summary>
    static JObject JuDocNoId(SceneScript s)
    {
        if (s == null) return null;
        var o = JObject.Parse(Encoding.UTF8.GetString(s.ToDocumentBytes()));
        o.Remove("id");
        return o;
    }

    static string JuJson(JToken t) => t == null ? "null" : t.ToString(Formatting.None);

    /// <summary>The members of the panel's result <paramref name="panel"/> (the script player's reply) as the command answered them: every member of the panel's
    /// result but time (it moves with real time) and the record's folder (each pass records into its own) equal in the command's reply; null when equal, else the first
    /// member that differs.</summary>
    static string JuSame(JObject panel, JObject command)
    {
        if (panel == null || command == null) return "a reply is missing";
        foreach (var p in panel.Properties())
        {
            if (p.Name == "time") continue;
            var a = p.Value;
            var b = command[p.Name];
            if (p.Name == "record" && a is JObject ra && b is JObject rb)
            {
                foreach (var q in ra.Properties())
                    if (q.Name != "path" && !JToken.DeepEquals(q.Value, rb[q.Name])) return "record." + q.Name + ": " + JuJson(q.Value) + " / " + JuJson(rb[q.Name]);
                continue;
            }
            if (!JToken.DeepEquals(a, b)) return p.Name + ": " + JuJson(a) + " / " + JuJson(b);
        }
        return null;
    }

    /// <summary><paramref name="at"/> is <paramref name="el"/> or inside it.</summary>
    static bool JuWithin(VisualElement at, VisualElement el)
    {
        for (var x = at; x != null; x = x.hierarchy.parent) if (x == el) return true;
        return false;
    }

    /// <summary>Real time passes (the frames of a batch Editor can be much shorter than a screen's).</summary>
    static IEnumerator JuSeconds(double s)
    {
        float until = Time.realtimeSinceStartup + (float)s;
        while (Time.realtimeSinceStartup < until) yield return null;
    }

    static bool JuSamePath(string a, string b) =>
        a != null && b != null && string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, '/'), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, '/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>Undoes <paramref name="n"/> entries (the bus's own undo).</summary>
    static void JuUndo(int n)
    {
        for (int i = 0; i < n; i++) JuApp.Session.Commands.Undo();
        JuApp.Runtime.ApplyNow();
    }

    // ------------------------------------------------------------ JU1: the strings

    // the keys and texts spec 13.3 gives
    static readonly Dictionary<string, string[]> JuSpecStrings = new Dictionary<string, string[]>
    {
        ["panel.scripts"] = new[] { "Scene scripts", "シーンの台本", "场景脚本" },
        ["script.play"] = new[] { "Play", "再生", "播放" },
        ["script.stop"] = new[] { "Stop", "停止", "停止" },
        ["script.prepare"] = new[] { "Prepare again", "準備をやり直す", "重新准备" },
        ["script.record"] = new[] { "Record…", "連番に書き出す…", "录制…" },
        ["script.loop"] = new[] { "Loop", "繰り返す", "循环" },
        ["script.roles"] = new[] { "Roles", "役", "角色" },
        ["script.role.use"] = new[] { "Use selected", "選択中を使う", "使用所选" },
        ["script.uncast"] = new[] { "Not cast", "未割り当て", "未分配" },
        ["script.keep"] = new[] { "Keep in the store", "置き場に残す", "保存到库" },
        ["script.store"] = new[] { "Script store", "台本の置き場", "脚本库" },
        ["script.add"] = new[] { "Add to scene", "シーンに足す", "添加到场景" },
        ["script.import"] = new[] { "Import…", "取り込む…", "导入…" },
        ["script.export"] = new[] { "Export…", "書き出す…", "导出…" },
        ["script.remove"] = new[] { "Remove", "外す", "移除" },
        ["script.capture"] = new[] { "Save edits into the script", "編集を台本に書き戻す", "将编辑保存到脚本" },
        ["script.edited"] = new[] { "edited", "編集あり", "已编辑" },
        ["timeline.fromScript"] = new[] { "Script: {0}", "台本: {0}", "脚本: {0}" },
        ["script.failures"] = new[] { "Last play", "前回の再生", "上次播放" },
    };

    // every English word the block may use: the words of spec 13.3 and the mechanical words of the rows of 13.2's controls (a word added here is a choice the owner reads at the
    // release review)
    static readonly HashSet<string> JuWords = new HashSet<string>(StringComparer.Ordinal)
    {
        // spec 13.3: the feature's words and its keys' texts
        "scene", "scenes", "script", "scripts", "role", "roles", "cast", "set", "up", "preparation", "prepare", "prepared", "play", "stop", "record", "recorded", "loop", "store",
        "import", "imported", "export", "exported", "again", "use", "selected", "not", "keep", "kept", "in", "the", "add", "to", "remove", "save", "edit", "edits", "into", "edited",
        "last",
        // the controls of 13.2 and their lines
        "rename", "delete", "deleted", "show", "restore", "purge", "purged", "removes", "removed", "row", "rows", "file", "files", "panel", "character", "item", "female", "male",
        "either", "loaded", "timeline", "borrow", "load", "step", "steps", "sent", "failed", "skipped", "timed", "commands", "pass", "fixed", "time", "numbered", "png", "index",
        "newer", "version", "nothing", "empty", "matches", "written", "change", "next", "press", "give", "such", "select", "list", "first",
        // the small words
        "a", "an", "one", "and", "of", "no", "is", "be", "it", "as", "at", "this", "their", "that", "has", "yet", "see", "cannot", "read", "s",
    };

    // words of other subjects, in any language
    static readonly string[] JuOtherSubjects =
    {
        "plugin", "HS2", "Studio", "mod", "hook", "BepInEx", "KKAPI", "AI", "WetLab", "scenectl", "プラグイン", "插件", "モッド",
    };

    static void JuStrings()
    {
        string file = Path.Combine(ContentSettings.ProjectRoot, "Assets", "HS2Next", "Resources", "UI", "L10n", "strings.tsv");
        string text = File.ReadAllText(file, Encoding.UTF8);
        var tb = L10nTable.Parse(text);
        var rows = new List<string[]>();
        bool inside = false;
        foreach (string line in text.Split('\n'))
        {
            if (line.StartsWith("#", StringComparison.Ordinal)) { inside = line.StartsWith("#U69", StringComparison.Ordinal); continue; }
            if (inside && line.Length > 0) rows.Add(line.Split('\t'));
        }
        Info("the block #U69 holds " + rows.Count + " rows");
        Check("the table reads with no problem", tb.Problems.Count == 0, string.Join("; ", tb.Problems.Take(5)));
        var keys = rows.Select(r => r[0]).ToList();
        Check("no key twice in the block", keys.Count == keys.Distinct().Count());
        string[] lang = { "en", "ja", "zh-Hans" };
        var missing = new List<string>();
        foreach (var k in keys)
            foreach (var l in lang)
                if (string.IsNullOrEmpty(tb.Cell(k, l))) missing.Add(k + " (" + l + ")");
        Check("every key of the block in en, ja and zh-Hans", missing.Count == 0, keys.Count + " keys, missing " + missing.Count + ": " + string.Join(", ", missing.Take(20)));
        int wrong = 0;
        foreach (var kv in JuSpecStrings)
        {
            if (!keys.Contains(kv.Key)) { wrong++; Check("the block holds " + kv.Key, false); continue; }
            for (int i = 0; i < 3; i++)
                if (kv.Value[i] != tb.Cell(kv.Key, lang[i])) { wrong++; Check(kv.Key + " in " + lang[i] + " as spec 13.3 gives it", false, tb.Cell(kv.Key, lang[i])); }
        }
        Check("the " + JuSpecStrings.Count + " keys spec 13.3 gives hold its texts", wrong == 0, wrong + " differ");
        var marks = new Regex(@"\{\d\}");
        int placeholders = 0, unequal = 0;
        foreach (var k in keys)
        {
            var en = marks.Matches(tb.Cell(k, "en") ?? "").Cast<Match>().Select(m => m.Value).OrderBy(x => x).ToList();
            bool same = en.SequenceEqual(marks.Matches(tb.Cell(k, "ja") ?? "").Cast<Match>().Select(m => m.Value).OrderBy(x => x)) &&
                        en.SequenceEqual(marks.Matches(tb.Cell(k, "zh-Hans") ?? "").Cast<Match>().Select(m => m.Value).OrderBy(x => x));
            if (!same) { unequal++; Check(k + ": the same placeholders in every language", false); }
            if (en.Count > 0) placeholders++;
        }
        Check("the same placeholders in every language", unequal == 0, placeholders + " keys with placeholders");
        var unmarked = rows.Where(r => r.Length < 5 || r[4] != "zh?").Select(r => r[0]).ToList();
        Check("every zh-Hans cell awaits its review (zh?), as the neighbouring blocks", unmarked.Count == 0, string.Join(", ", unmarked.Take(10)));
        var history = new Dictionary<string, string>
        {
            ["history.script.add"] = ScriptEditCommand.LabelAdd, ["history.script.edit"] = ScriptEditCommand.LabelEdit, ["history.script.remove"] = ScriptEditCommand.LabelRemove,
            ["history.script.cast"] = ScriptEditCommand.LabelCast, ["history.script.load"] = ScriptEditCommand.LabelLoad, ["history.script.prepare"] = ScriptEditCommand.LabelPrepare,
            ["history.script.capture"] = ScriptEditCommand.LabelCapture, ["history.script.borrow"] = ScriptEditCommand.LabelBorrow,
        };
        foreach (var h in history) Check("the history label " + h.Key + " is ScriptEditCommand's «" + h.Value + "»", tb.Cell(h.Key, "en") == h.Value, tb.Cell(h.Key, "en"));
        Check("the action scripts.panel has its label", tb.Has(ActionIds.LabelKey(ActionIds.ScriptsPanel)), ActionIds.LabelKey(ActionIds.ScriptsPanel));
        var word = new Regex(@"[A-Za-z]+");
        var outside = new SortedSet<string>(StringComparer.Ordinal);
        int words = 0;
        foreach (var r in rows)
            foreach (Match m in word.Matches(r[1]))
            {
                words++;
                if (!JuWords.Contains(m.Value.ToLowerInvariant())) outside.Add(r[0] + ": " + m.Value);
            }
        Check("every English word of the block is on the list", outside.Count == 0, words + " words read, " + outside.Count + " not on it: " + string.Join("; ", outside.Take(20)));
        int cells = 0, hits = 0;
        foreach (var r in rows)
            foreach (string cell in r.Skip(1).Take(3))
            {
                cells++;
                foreach (var w in JuOtherSubjects)
                {
                    bool ascii = w.All(c => c < 128);
                    bool hit = ascii ? Regex.IsMatch(cell, @"(?<![A-Za-z])" + Regex.Escape(w) + @"(?![A-Za-z])", RegexOptions.IgnoreCase) : cell.Contains(w);
                    if (hit) { hits++; Check(r[0] + " names no other subject (" + w + ")", false, cell); }
                }
            }
        Check("no cell names another subject", hits == 0, cells + " cells searched for " + JuOtherSubjects.Length + " words");
        // every key the panel, its dialogs and the Timeline's script line use stands in the table
        string dir = Path.Combine(ContentSettings.ProjectRoot, "Assets", "HS2Next", "Runtime", "UI", "Panels", "Scripts");
        var used = new SortedSet<string>(StringComparer.Ordinal);
        int files = 0;
        foreach (string f in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            files++;
            foreach (Match m in Regex.Matches(File.ReadAllText(f, Encoding.UTF8), "\"((?:script|panel|timeline|history|action|common|msg|capture)\\.[A-Za-z0-9.]*)\"")) used.Add(m.Groups[1].Value);
        }
        var expanded = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var k in used)
        {
            if (!k.EndsWith(".", StringComparison.Ordinal)) { expanded.Add(k); continue; }
            IEnumerable<string> tails = k switch
            {
                "script.phase." => new[] { "setup", "prepare", "cast", "load", "play" },
                _ => new[] { "?" },
            };
            foreach (var t in tails) expanded.Add(k + t);
        }
        var absent = expanded.Where(k => !tb.Has(k)).ToList();
        Check("every key the panel, its dialogs and the script line use is in the table", absent.Count == 0 && expanded.Count > 0,
            expanded.Count + " keys from " + files + " files; absent " + string.Join(", ", absent));
    }

    // ------------------------------------------------------------ JU2: the lazy panel

    static IEnumerator JuLazy()
    {
        if (!POpen("JU2", "the panel appears when asked for; a closed panel reads nothing; the camera keeps away from the panel")) yield break;
        var ui = JuUi;
        var sh = ui.Shell;
        string file = Path.Combine(ui.Options.ConfigDir, LayoutDocument.FileName);
        string layout = File.Exists(file) ? File.ReadAllText(file, Encoding.UTF8) : "";
        bool listed = layout.Contains("\"" + ScriptsUiModule.PanelId + "\"");
        bool registered = ui.Registry.Panels.TryGet(ScriptsUiModule.PanelId, out _);
        Check("registered at the start only when layout.json lists it", registered == listed, registered + " / " + listed);
        Info("layout.json lists it at the start: " + listed + " (the part removed layout.json before the app started)");
        Check("the action stands in the manifest and in the registry", ActionManifest.Contains(ActionIds.ScriptsPanel) && ui.Registry.Actions.TryGet(ActionIds.ScriptsPanel, out _));
        if (ScriptsUiModule.PanelShown(ui))
        {
            yield return PCall(JuApp, new { cmd = "ui.action", action = ActionIds.ScriptsPanel });
            yield return PFrames(3);
        }
        Check("not shown before it is asked for", !ScriptsUiModule.PanelShown(ui));
        yield return JuIn(JuPt(JuQ("tb-view").worldBound.center, "click"));
        yield return PFrames(2);
        Check("the View menu lists it", JuQ("menu-item-" + ActionIds.ScriptsPanel) != null);
        ui.Menus?.CloseAll();
        yield return PFrames(2);
        yield return PCall(JuApp, new { cmd = "ui.action", action = ActionIds.ScriptsPanel });
        Check("ui.action opens it", POk && ScriptsUiModule.PanelShown(ui), Short(pLastText));
        yield return PFrames(4);
        var p = JuSp;
        Check("shown, its title the feature's name", p != null && JuVisible(p) && ui.L10n.Get(ScriptsUiModule.TitleKey) == "Scene scripts", ui.L10n.Get(ScriptsUiModule.TitleKey));
        Check("its default region is the bottom", sh.RegionOf(ScriptsUiModule.PanelId) == LayoutRegions.Bottom, sh.RegionOf(ScriptsUiModule.PanelId));
        yield return PCall(JuApp, new { cmd = "ui.action", action = ActionIds.TimelinePanel });
        yield return PFrames(3);
        Check("beside the Timeline panel: both in the bottom region", sh.RegionOf(TimelineUiModule.PanelId) == LayoutRegions.Bottom && sh.Regions[LayoutRegions.Bottom].Panels.Contains(ScriptsUiModule.PanelId),
            string.Join(",", sh.Regions[LayoutRegions.Bottom].Panels));
        yield return PCall(JuApp, new { cmd = "ui.action", action = ActionIds.ScriptsPanel });
        yield return PFrames(4);
        p = JuSp;
        Check("the action shows it again over the Timeline panel", ScriptsUiModule.PanelShown(ui) && p != null && JuVisible(p));
        int r0 = p?.Reads ?? 0;
        var read = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => p != null && p.Reads > r0, 3, read);
        Check("Present: the shown panel reads", read[0], p == null ? "no panel" : (p.Reads - r0) + " reads");
        // a wheel and a drag over the panel stay in the panel; the same wheel over the view moves the camera
        string cam = JuCam();
        var c = p.worldBound.center;
        yield return JuIn(JuWheel(c, -3));
        yield return JuIn(JuPt(c, "down"), JuPt(c + new Vector2(40, 10), "move"), JuPt(c + new Vector2(80, 20), "move"), JuPt(c + new Vector2(80, 20), "up"));
        yield return PFrames(3);
        Check("a wheel and a drag over the panel leave the camera", JuCam() == cam, JuCam() + " / " + cam);
        var vc = ui.Viewport.Element.worldBound.center;
        bool overView = JuWithin(JuPanel.Pick(vc), ui.Viewport.Element);
        yield return JuIn(JuWheel(vc, -3));
        yield return PFrames(3);
        Check("Present: the same wheel over the view moves the camera", overView && JuCam() != cam, (overView ? "" : "the view's centre is covered; ") + JuCam());
        // closed: no reads, also when the scene changes
        yield return PCall(JuApp, new { cmd = "ui.action", action = ActionIds.ScriptsPanel });
        yield return PFrames(3);
        Check("the action again collapses it", !ScriptsUiModule.PanelShown(ui));
        r0 = p.Reads;
        yield return PCall(JuApp, new { cmd = "camera.set", dist = 30 });
        yield return PCall(JuApp, new { cmd = "scene.new" });
        yield return JuSeconds(1.0);
        Check("a closed panel reads nothing for 1 s of real time, also when the scene changes", p.Reads == r0, (p.Reads - r0).ToString());
        sh.ResetLayout();
        yield return PFrames(3);
        Check("«Reset layout» leaves the bottom region collapsed", sh.Regions[LayoutRegions.Bottom].Collapsed && !ScriptsUiModule.PanelShown(ui));
    }

    // ------------------------------------------------------------ the fixture

    /// <summary>A new scene with BR-Chan and HERO107 standing still, an item, and the script «Panel check» put in by script.set (roles a, b; a set-up step, a stain made in
    /// the preparation; a 2 s Timeline with a pos track on a and a timed command); the panel shown with the script selected.</summary>
    static IEnumerator JuFixture(bool[] ok)
    {
        ok[0] = false;
        current = "JU";
        var app = JuApp;
        var ready = new bool[1];
        yield return JuCmd(new { cmd = "scene.new" }, "fixture: scene.new");
        HS2Next.Motion.Face.FaceSetup.Seed = 0x5eed;
        yield return JuCmd(new { cmd = "chara.load", path = femaleRel, sex = "female" }, "fixture: chara.load BR-Chan");
        juA = Key(pLast);
        yield return JuCmd(new { cmd = "chara.load", path = maleRel, sex = "male" }, "fixture: chara.load HERO107");
        juB = Key(pLast);
        yield return JuCmd(new { cmd = "item.add", no = 399 }, "fixture: item.add 399");
        juItem = Key(pLast);
        if (juA == null || juB == null || juItem == null) { Check("fixture: the nodes added", false); yield break; }
        yield return PlayModeHarness.WaitUntil(() => app.Runtime.FindChara(juA)?.State == RealizationState.Built && app.Runtime.FindChara(juB)?.State == RealizationState.Built, 180, ready);
        if (!Check("fixture: both characters built", ready[0])) yield break;
        yield return JuCmd(new { cmd = "obj.transform", key = juB, pos = new[] { 5, 0, 0 }, rot = new[] { 0, -90, 0 } }, "fixture: HERO107 at (5, 0, 0)");
        yield return JuCmd(new { cmd = "obj.transform", key = juItem, pos = new[] { 2.5, 0, 3 } }, "fixture: the item at (2.5, 0, 3)");
        yield return JuCmd(new { cmd = "chara.anime", key = juA, group = 19, category = 0, no = 0, time = 0.5f, speed = 0f }, "fixture: BR-Chan's row");
        yield return JuCmd(new { cmd = "camera.set", target = new[] { 2.5, 10, 0 }, rot = new[] { 5, 180, 0 }, dist = 40, fov = 30 }, "fixture: the camera");
        var s = NewScript("Panel check", CharaRole("a", "female"), CharaRole("b", "male"));
        s.Roles.Add(ScriptRole.Item("p", ScriptRoles.RefOf(app.Session.Model.Find(juItem)), new ScriptHint("prop")));
        s.Setup.Add(J("{\"cmd\":\"obj.transform\",\"key\":\"$b\",\"pos\":[5,0,0],\"rot\":[0,-90,0]}"));
        s.Prepare.Add(J("{\"cmd\":\"wet.attach\",\"key\":\"$a\",\"preset\":\"water\"}"));
        s.Timeline = ScriptTimeline(2f, true, Track("pos", "$a", TimelineKey.OfVector(0f, new Vec3(0, 0, 0))));
        s.Timeline.Tracks[0].Keys.Add(TimelineKey.OfVector(2f, new Vec3(0, 0, 3)));
        s.Timeline.Events.Add(Event(0.5f, "{\"cmd\":\"ping\"}"));
        var doc = JObject.Parse(Encoding.UTF8.GetString(s.ToDocumentBytes()));
        yield return JuCmd(new JObject { ["cmd"] = "script.set", ["script"] = doc }, "fixture: script.set «Panel check»");
        juScript = (string)pLast?["script"];
        if (!Check("fixture: the script is in the scene", juScript != null && JuScriptOf(juScript) != null, Short(pLastText))) yield break;
        JuUi.Ops.Select(new[] { juA });
        yield return PCall(app, new { cmd = "ui.action", action = ActionIds.ScriptsPanel });
        yield return PFrames(5);
        if (!ScriptsUiModule.PanelShown(JuUi)) { yield return PCall(app, new { cmd = "ui.action", action = ActionIds.ScriptsPanel }); yield return PFrames(5); }
        if (!Check("fixture: the panel shown", ScriptsUiModule.PanelShown(JuUi) && JuSp != null)) yield break;
        JuSp.Select(juScript);
        yield return PFrames(3);
        ok[0] = true;
    }

    /// <summary>The fixture's script cast to BR-Chan and HERO107 (and the item) through script.cast, when a group runs without JU4 before it.</summary>
    static IEnumerator JuCastAll()
    {
        var s = JuScriptOf(juScript);
        if (s != null && s.CastOf("a") == juA && s.CastOf("b") == juB && s.CastOf("p") == juItem) yield break;
        yield return JuCmd(new JObject { ["cmd"] = "script.cast", ["script"] = juScript, ["roles"] = new JObject { ["a"] = juA, ["b"] = juB, ["p"] = juItem } }, "script.cast of the fixture");
    }

    // ------------------------------------------------------------ JU4: the roles

    static IEnumerator JuRoles()
    {
        if (!POpen("JU4", "the role picker casts; «Use selected»; uncast rows marked; a prop role's picker")) yield break;
        var app = JuApp;
        var p = JuSp;
        var s0 = JuScriptOf(juScript);
        Check("Present: the roles section lists a row per role, every role uncast and marked", JuQ("scripts-role-a") != null && JuQ("scripts-role-b") != null && JuQ("scripts-role-p") != null &&
            JuQ("scripts-role-a").ClassListContains("hs-scripts__line--uncast") && JuQ("scripts-role-b").ClassListContains("hs-scripts__line--uncast") && s0.Cast.Count == 0,
            "cast " + JuJson(JObject.FromObject(s0.Cast)));
        // the picker of a: the scene's characters and «Not cast»
        int u = JuUndos;
        yield return JuClick("scripts-role-a-pick");
        bool listsA = JuQ("menu-item-scripts-cast-" + juA) != null, listsB = JuQ("menu-item-scripts-cast-" + juB) != null, listsItem = JuQ("menu-item-scripts-cast-" + juItem) != null;
        Check("the picker of a character role offers the scene's characters and «Not cast», no item", listsA && listsB && !listsItem && JuQ("menu-item-scripts-cast-none") != null,
            listsA + " " + listsB + " " + listsItem);
        yield return JuClick("menu-item-scripts-cast-" + juA);
        var s1 = JuScriptOf(juScript);
        Check("choosing BR-Chan casts a in one entry «Cast roles»", s1.CastOf("a") == juA && JuUndos == u + 1 && JuTop == ScriptEditCommand.LabelCast, (JuUndos - u) + " " + JuTop + " " + s1.CastOf("a"));
        yield return PFrames(8);
        Check("the row of a: the cast node's name, not marked", JuText("scripts-role-a-pick") == app.Session.Model.Find(juA).Label && !JuQ("scripts-role-a").ClassListContains("hs-scripts__line--uncast"),
            JuText("scripts-role-a-pick"));
        // the same through script.cast from the same state: the same cast
        var panelCast = JObject.FromObject(s1.Cast);
        JuUndo(1);
        yield return JuCmd(new JObject { ["cmd"] = "script.cast", ["script"] = juScript, ["roles"] = new JObject { ["a"] = juA } }, "script.cast {a}");
        var cmdCast = JObject.FromObject(JuScriptOf(juScript).Cast);
        Check("the picker equals script.cast: the same cast, one entry each", JToken.DeepEquals(panelCast, cmdCast) && JuTop == ScriptEditCommand.LabelCast, JuJson(panelCast) + " / " + JuJson(cmdCast));
        // «Use selected» on b
        JuUi.Ops.Select(new[] { juB });
        yield return PFrames(3);
        u = JuUndos;
        yield return JuClick("scripts-role-b-use");
        var s2 = JuScriptOf(juScript);
        Check("«Use selected» casts b to the selected HERO107 in one entry «Cast roles»", s2.CastOf("b") == juB && JuUndos == u + 1 && JuTop == ScriptEditCommand.LabelCast, (JuUndos - u) + " " + s2.CastOf("b"));
        // «Use selected» with no node of the role's kind selected: nothing changes
        JuUi.Ops.Select(new[] { juItem });
        yield return PFrames(3);
        u = JuUndos;
        yield return JuClick("scripts-role-a-use");
        Check("«Use selected» with an item selected leaves a character role as it was (no entry)", JuScriptOf(juScript).CastOf("a") == juA && JuUndos == u, (JuUndos - u).ToString());
        // a prop role's picker: the items of its row, no character
        yield return JuClick("scripts-role-p-pick");
        bool pItem = JuQ("menu-item-scripts-cast-" + juItem) != null, pChara = JuQ("menu-item-scripts-cast-" + juA) != null;
        Check("the picker of a prop role offers the items of its row only", pItem && !pChara && JuSp.Candidates(JuScriptOf(juScript).FindRole("p")).All(n => n.Kind == NodeKind.Item), pItem + " " + pChara);
        yield return JuClick("menu-item-scripts-cast-" + juItem);
        Check("choosing the item casts p", JuScriptOf(juScript).CastOf("p") == juItem);
        // «Not cast» uncasts, and the row is marked again
        u = JuUndos;
        yield return JuClick("scripts-role-b-pick");
        yield return JuClick("menu-item-scripts-cast-none");
        yield return PFrames(8);
        Check("«Not cast» uncasts b in one entry «Cast roles», the row marked", JuScriptOf(juScript).CastOf("b") == null && JuUndos == u + 1 && JuTop == ScriptEditCommand.LabelCast &&
            JuQ("scripts-role-b").ClassListContains("hs-scripts__line--uncast") && JuText("scripts-role-b-pick") == JuUi.L10n.Get("script.uncast"), (JuUndos - u) + " " + JuText("scripts-role-b-pick"));
        JuUi.Ops.Select(new[] { juB });
        yield return PFrames(3);
        yield return JuClick("scripts-role-b-use");
        yield return PFrames(8);
        Check("the list shows the roles cast, 3 of 3", JuText("scripts-row-0-cast") == JuUi.L10n.Format("script.castCount", 3, 3), JuText("scripts-row-0-cast"));
    }

    // ------------------------------------------------------------ JU3: play, stop, prepare again, record

    static IEnumerator JuPlay()
    {
        if (!POpen("JU3", "Play, Stop, Prepare again and Record… through the panel equal the commands")) yield break;
        var app = JuApp;
        yield return JuCastAll();
        JuSp.Select(juScript);
        yield return PFrames(3);
        var tlp = app.Runtime.GetPart<TimelinePlayback>();

        // «Play» against script.play, from the same state (never played, not prepared)
        int u = JuUndos;
        yield return JuClick("scripts-play");
        yield return PlayModeHarness.WaitUntil(() => JuSp.LastPanelPlay != null && !JuSp.Busy, 30, new bool[1]);
        var lp = JuSp.LastPanelPlay;
        var panelReply = lp?.ToReply();
        var panelLabels = app.Session.Commands.UndoList.Take(JuUndos - u).Select(e => e.Label).ToList();
        var panelTl = app.Session.Model.Timeline?.CompiledHash();
        Check("Present: «Play» prepared, loaded and plays (the preparation made a stain)", lp != null && lp.Ok && lp.Prepared && lp.Loaded && tlp.Player.State == TimelinePlayState.Playing &&
            JuStains(juA) > 0, JuJson(panelReply) + " stains " + JuStains(juA));
        Info("«Play» added: " + string.Join(", ", panelLabels));
        ScriptPlayer.Stop(app, false);
        JuUndo(panelLabels.Count);
        Check("its entries undone: not prepared, no stain, no Timeline", JuScriptOf(juScript).Prepared == null && JuStains(juA) == 0 && app.Session.Model.Timeline == null,
            "prepared " + JuScriptOf(juScript).Prepared + ", stains " + JuStains(juA) + ", timeline " + (app.Session.Model.Timeline != null));
        u = JuUndos;
        yield return JuCmd(new JObject { ["cmd"] = "script.play", ["script"] = juScript }, "script.play");
        var cmdReply = pLast;
        var cmdLabels = app.Session.Commands.UndoList.Take(JuUndos - u).Select(e => e.Label).ToList();
        string diff = JuSame(panelReply, cmdReply);
        Check("«Play» equals script.play: the reply's members (time aside), the entries, the Timeline loaded", diff == null && panelLabels.SequenceEqual(cmdLabels) &&
            panelTl != null && panelTl == app.Session.Model.Timeline?.CompiledHash(),
            (diff ?? "replies equal") + "; " + string.Join(",", panelLabels) + " / " + string.Join(",", cmdLabels));
        yield return PFrames(10);

        // «Stop» against script.stop (reset): the Timeline stopped, the cast's stain dried, no entry
        u = JuUndos;
        int stainsBefore = JuStains(juA);
        yield return JuClick("scripts-stop");
        var ps = JuSp.LastPanelStop;
        var panelStop = ps?.ToReply();
        Check("Present: a stain was there before «Stop»", stainsBefore > 0, stainsBefore.ToString());
        Check("«Stop»: the Timeline stopped, the stain dried, no entry", ps != null && ps.Dried == 1 && tlp.Player.State != TimelinePlayState.Playing && JuUndos == u, JuJson(panelStop) + " entries " + (JuUndos - u));
        yield return JuCmd(new JObject { ["cmd"] = "script.play", ["script"] = juScript, ["prepare"] = "always" }, "script.play {prepare: always} before script.stop");
        yield return PFrames(10);
        u = JuUndos;
        yield return JuCmd(new JObject { ["cmd"] = "script.stop" }, "script.stop");
        diff = JuSame(panelStop, pLast);
        Check("«Stop» equals script.stop: the reply's members, no entry", diff == null && JuUndos == u, (diff ?? "replies equal") + "; " + JuJson(panelStop));

        // «Prepare again» against script.play {prepare: always}
        u = JuUndos;
        yield return JuClick("scripts-prepare");
        yield return PlayModeHarness.WaitUntil(() => !JuSp.Busy, 30, new bool[1]);
        lp = JuSp.LastPanelPlay;
        panelReply = lp?.ToReply();
        panelLabels = app.Session.Commands.UndoList.Take(JuUndos - u).Select(e => e.Label).ToList();
        Check("Present: «Prepare again» prepared although the mark was set", lp != null && lp.Prepared && panelLabels.Contains(ScriptEditCommand.LabelPrepare), JuJson(panelReply) + " " + string.Join(",", panelLabels));
        ScriptPlayer.Stop(app, true);
        yield return PFrames(3);
        u = JuUndos;
        yield return JuCmd(new JObject { ["cmd"] = "script.play", ["script"] = juScript, ["prepare"] = "always" }, "script.play {prepare: always}");
        cmdReply = pLast;
        cmdLabels = app.Session.Commands.UndoList.Take(JuUndos - u).Select(e => e.Label).ToList();
        diff = JuSame(panelReply, cmdReply);
        Check("«Prepare again» equals script.play {prepare: always}: the reply's members (time aside), the entries", diff == null && panelLabels.SequenceEqual(cmdLabels),
            (diff ?? "replies equal") + "; " + string.Join(",", panelLabels) + " / " + string.Join(",", cmdLabels));
        ScriptPlayer.Stop(app, true);
        yield return PFrames(3);

        // «Record…» against script.play {record}: the dialog's size, frame rate and folder; one pass, every frame written
        juCapMode = CaptureSettings.Mode; juCapW = CaptureSettings.Width; juCapH = CaptureSettings.Height; juCapSs = CaptureSettings.Supersample; juCapFrame = CaptureSettings.Frame;
        juCapSaved = true;
        CaptureSettings.Mode = CaptureSettings.ModeCustom; CaptureSettings.Width = 320; CaptureSettings.Height = 180; CaptureSettings.Supersample = 1; CaptureSettings.Frame = false;
        string dirPanel = PRecordDir("u_panel"), dirCmd = PRecordDir("u_command");
        juMade.Add(dirPanel);
        juMade.Add(dirCmd);
        PDeleteFrames(dirPanel);
        PDeleteFrames(dirCmd);
        yield return JuClick("scripts-record");
        yield return PFrames(3);
        var folder = JuQ(ScriptsDialogs.RecordId + "-folder") as TextField;
        if (!Check("«Record…» shows its dialog with the folder field", JuUi.Dialogs.Current?.Spec.Id == ScriptsDialogs.RecordId && folder != null)) yield break;
        folder.value = dirPanel;
        yield return PFrames(2);
        Check("the dialog shows the size and the frame rate", JuText(ScriptsDialogs.RecordId + "-size") == JuUi.L10n.Format("timeline.export.size", 320, 180, 320, 180, 30), JuText(ScriptsDialogs.RecordId + "-size"));
        yield return JuClick("dialog-button-record");
        yield return PlayModeHarness.WaitUntil(() => JuSp.LastPanelPlay?.Record != null && !JuSp.Busy, 30, new bool[1]);
        var rec = FrameRecorder.Current;
        yield return PlayModeHarness.WaitUntil(() => rec != null && rec.IsDone, 60, new bool[1]);
        yield return PFrames(3);
        lp = JuSp.LastPanelPlay;
        panelReply = lp?.ToReply();
        int want = (int)Math.Round(2f * 30, MidpointRounding.AwayFromZero);
        int filesPanel = Directory.Exists(dirPanel) ? Directory.GetFiles(dirPanel, "*.png").Length : 0;
        Check("«Record…»: one pass into the dialog's folder, frames = round(duration x fps) = " + want + ", every frame written", lp?.Record != null && JuSamePath((string)lp.Record["path"], dirPanel) &&
            rec != null && rec.IsDone && rec.PassFrames == want && rec.Written == want && filesPanel == want && rec.StoppedBy == FrameRecorder.ByPass,
            JuJson(lp?.Record) + "; pass " + rec?.PassFrames + ", written " + rec?.Written + ", files " + filesPanel + ", stoppedBy " + rec?.StoppedBy);
        int writtenPanel = rec?.Written ?? -1;
        PDeleteFrames(dirPanel);
        yield return JuCmd(new JObject
        {
            ["cmd"] = "script.play", ["script"] = juScript,
            ["record"] = new JObject { ["path"] = dirCmd, ["width"] = 320, ["height"] = 180, ["fps"] = 30, ["supersample"] = 1, ["frame"] = false },
        }, "script.play {record}");
        cmdReply = pLast;
        rec = FrameRecorder.Current;
        yield return PlayModeHarness.WaitUntil(() => rec != null && rec.IsDone, 60, new bool[1]);
        yield return PFrames(3);
        int filesCmd = Directory.Exists(dirCmd) ? Directory.GetFiles(dirCmd, "*.png").Length : 0;
        diff = JuSame(panelReply, cmdReply);
        Check("«Record…» equals script.play {record}: the reply's members (folder and time aside), the frames written", diff == null && rec != null && rec.Written == writtenPanel && filesCmd == want,
            (diff ?? "replies equal") + "; written " + writtenPanel + " / " + rec?.Written + ", files " + filesCmd);
        PDeleteFrames(dirCmd);
        Check("the frames were deleted", !Directory.Exists(dirPanel) && !Directory.Exists(dirCmd));
        JuRestoreCapture();
        ScriptPlayer.Stop(app, true);
        yield return PFrames(3);
        // «Last play»
        var last = JuQ("scripts-last") as Foldout;
        Check("«Last play» is collapsed by default and reads the last play's counts", last != null && !last.value &&
            JuText("scripts-last-summary") == JuUi.L10n.Format("script.lastSummary", lp.SetupSent, lp.SetupFailed, lp.PrepareSent, lp.PrepareFailed, lp.Skipped), JuText("scripts-last-summary"));
    }

    /// <summary>The stains of a character in the model.</summary>
    static int JuStains(string id) => JuApp.Session.Model.Find(id)?.State is CharaNodeState cs && cs.Wet != null ? cs.Wet.Stains.Count : 0;

    // ------------------------------------------------------------ JU3: the list's and the store's controls

    static IEnumerator JuControls()
    {
        if (!POpen("JU3", "the list's and the store's controls equal their commands")) yield break;
        var app = JuApp;
        JuSp.Select(juScript);
        yield return PFrames(3);
        int index = app.Session.Model.Scripts.IndexOf(juScript);
        string row = "scripts-row-" + index;

        // «Loop»
        bool loop0 = JuScriptOf(juScript).Timeline.Loop;
        int u = JuUndos;
        yield return JuClick("scripts-loop");
        var afterPanel = JuDocNoId(JuScriptOf(juScript));
        Check("«Loop» flips the script's loop in one entry «Edit script»", JuScriptOf(juScript).Timeline.Loop == !loop0 && JuUndos == u + 1 && JuTop == ScriptEditCommand.LabelEdit, (JuUndos - u) + " " + JuTop);
        JuUndo(1);
        var flipped = JObject.Parse(Encoding.UTF8.GetString(JuScriptOf(juScript).ToDocumentBytes()));
        flipped["timeline"]["loop"] = !loop0;
        yield return JuCmd(new JObject { ["cmd"] = "script.set", ["script"] = flipped, ["replace"] = true }, "script.set {replace} with the loop flipped");
        Check("«Loop» equals script.set {replace}: the same document", JToken.DeepEquals(afterPanel, JuDocNoId(JuScriptOf(juScript))), JuJson(afterPanel?["timeline"]?["loop"]) + " / " + JuJson(JuDocNoId(JuScriptOf(juScript))?["timeline"]?["loop"]));
        JuUndo(1);

        // «Rename» in place: Esc cancels, Enter confirms
        string name0 = JuScriptOf(juScript).Name;
        u = JuUndos;
        yield return JuClick("scripts-rename");
        yield return PFrames(3);
        Check("«Rename» puts a field in the row", JuQ(row + "-edit") is TextField && JuSp.Renaming == juScript);
        yield return JuIn(JuTyped("X"));
        yield return JuIn(JuKey("Escape"));
        yield return PFrames(3);
        Check("Esc cancels: the name as it was, no entry", JuScriptOf(juScript).Name == name0 && JuUndos == u && JuSp.Renaming == null, JuScriptOf(juScript).Name + " " + (JuUndos - u));
        yield return JuClick("scripts-rename");
        yield return PFrames(3);
        if (JuQ(row + "-edit") is TextField tf) tf.SelectAll();
        yield return JuIn(JuTyped("Renamed"));
        yield return JuIn(JuKey("Return"));
        yield return PFrames(3);
        afterPanel = JuDocNoId(JuScriptOf(juScript));
        Check("Enter confirms: the name typed, one entry «Edit script»", JuScriptOf(juScript).Name == "Renamed" && JuUndos == u + 1 && JuTop == ScriptEditCommand.LabelEdit, JuScriptOf(juScript).Name + " " + (JuUndos - u));
        JuUndo(1);
        var renamed = JObject.Parse(Encoding.UTF8.GetString(JuScriptOf(juScript).ToDocumentBytes()));
        renamed["name"] = "Renamed";
        yield return JuCmd(new JObject { ["cmd"] = "script.set", ["script"] = renamed, ["replace"] = true }, "script.set {replace} with the name");
        Check("«Rename» equals script.set {replace}: the same document", JToken.DeepEquals(afterPanel, JuDocNoId(JuScriptOf(juScript))), JuJson(afterPanel?["name"]));
        JuUndo(1);

        // «Keep in the store» against script.keep: the same row, the same file
        yield return JuClick("scripts-keep");
        var store = ScriptStore.ForUserData(JuUserData, null);
        var kept = store.FindById(juScript);
        byte[] keptPanel = kept != null && File.Exists(store.PathOf(kept.No)) ? File.ReadAllBytes(store.PathOf(kept.No)) : null;
        Check("Present: «Keep in the store» made a row of the script", kept != null && keptPanel != null && kept.Name == JuScriptOf(juScript).Name, kept == null ? "no row" : kept.No + " " + kept.Name);
        yield return JuCmd(new JObject { ["cmd"] = "script.keep", ["script"] = juScript }, "script.keep");
        store = ScriptStore.ForUserData(JuUserData, null);
        int keptNo = kept?.No ?? -1;
        byte[] keptCmd = keptNo > 0 && File.Exists(store.PathOf(keptNo)) ? File.ReadAllBytes(store.PathOf(keptNo)) : null;
        Check("«Keep in the store» equals script.keep: the same row number, the same file bytes", (int?)pLast?["no"] == keptNo && keptPanel != null && keptCmd != null && keptPanel.SequenceEqual(keptCmd) && store.LiveRows().Count() == 1,
            pLast?["no"] + " / " + keptNo + ", rows " + store.LiveRows().Count());

        // «Export…» against script.export: the same bytes
        Directory.CreateDirectory(JuExportDir);
        string fPanel = Path.Combine(JuExportDir, "panel" + ScriptsDialogs.FileExtension), fCmd = Path.Combine(JuExportDir, "command" + ScriptsDialogs.FileExtension);
        juFiles.Add(fPanel);
        juFiles.Add(fCmd);
        foreach (var f in new[] { fPanel, fCmd }) if (File.Exists(f)) File.Delete(f);
        yield return JuClick("scripts-export");
        yield return PFrames(3);
        var pathField = JuQ(ScriptsDialogs.ExportId + "-path") as TextField;
        if (!Check("«Export…» shows its dialog with the file field", JuUi.Dialogs.Current?.Spec.Id == ScriptsDialogs.ExportId && pathField != null)) yield break;
        pathField.value = "u_export/panel";   // relative to the output folder; the extension added
        yield return JuClick("dialog-button-export");
        yield return PFrames(3);
        Check("the dialog wrote the file (relative to the output folder, the extension added) and closed", File.Exists(fPanel) && JuUi.Dialogs.Current == null, fPanel);
        yield return JuCmd(new JObject { ["cmd"] = "script.export", ["script"] = juScript, ["path"] = fCmd }, "script.export");
        Check("«Export…» equals script.export: the same bytes", File.Exists(fPanel) && File.Exists(fCmd) && File.ReadAllBytes(fPanel).SequenceEqual(File.ReadAllBytes(fCmd)),
            (File.Exists(fPanel) ? new FileInfo(fPanel).Length : -1) + " / " + (File.Exists(fCmd) ? new FileInfo(fCmd).Length : -1) + " bytes");

        // «Import…» against script.import {to: store}: a new row each, the same document (the copies get new ids)
        int rows0 = ScriptStore.ForUserData(JuUserData, null).LiveRows().Count();
        yield return JuClick("scripts-import");
        yield return PFrames(3);
        var importField = JuQ(ScriptsDialogs.ImportId + "-path") as TextField;
        if (!Check("«Import…» shows its dialog with the file field", JuUi.Dialogs.Current?.Spec.Id == ScriptsDialogs.ImportId && importField != null)) yield break;
        importField.value = fPanel;
        yield return JuClick("dialog-button-import");
        yield return PFrames(3);
        store = ScriptStore.ForUserData(JuUserData, null);
        var imported = store.LiveRows().OrderBy(r => r.No).LastOrDefault();
        Check("the dialog imported the file as a new row and closed", store.LiveRows().Count() == rows0 + 1 && imported != null && imported.No != keptNo && JuUi.Dialogs.Current == null,
            "rows " + rows0 + " -> " + store.LiveRows().Count());
        yield return JuCmd(new JObject { ["cmd"] = "script.import", ["path"] = fPanel, ["to"] = "store" }, "script.import {to: store}");
        store = ScriptStore.ForUserData(JuUserData, null);
        int cmdNo = (int?)pLast?["no"] ?? -1;
        var docPanel = imported != null ? JuDocNoId(store.Read(imported.No, null)) : null;
        var docCmd = cmdNo > 0 ? JuDocNoId(store.Read(cmdNo, null)) : null;
        Check("«Import…» equals script.import: a new row each, the same document", docPanel != null && docCmd != null && JToken.DeepEquals(docPanel, docCmd) && store.LiveRows().Count() == rows0 + 2,
            "rows " + store.LiveRows().Count() + ", numbers " + imported?.No + " / " + cmdNo);
        JuSp.MarkDirty();
        yield return PFrames(4);

        // «Add to scene» against script.add
        int scripts0 = app.Session.Model.Scripts.Count;
        u = JuUndos;
        yield return JuClick("scripts-store-row-" + keptNo + "-add");
        var added = app.Session.Model.Scripts.Count == scripts0 + 1 ? app.Session.Model.Scripts.Scripts[scripts0] : null;
        var addedDoc = JuDocNoId(added);
        Check("«Add to scene» adds the row as a scene script in one entry «Add script»", added != null && added.Store == keptNo && JuUndos == u + 1 && JuTop == ScriptEditCommand.LabelAdd,
            "scripts " + scripts0 + " -> " + app.Session.Model.Scripts.Count + ", " + (JuUndos - u) + " " + JuTop);
        JuUndo(1);
        yield return JuCmd(new JObject { ["cmd"] = "script.add", ["no"] = keptNo }, "script.add");
        var addedCmd = app.Session.Model.Scripts.Count == scripts0 + 1 ? app.Session.Model.Scripts.Scripts[scripts0] : null;
        Check("«Add to scene» equals script.add: the same document and store number", addedDoc != null && JToken.DeepEquals(addedDoc, JuDocNoId(addedCmd)) && addedCmd.Store == keptNo,
            JuJson(addedDoc?["name"]) + " / " + JuJson(JuDocNoId(addedCmd)?["name"]));
        JuUndo(1);
        JuSp.Select(juScript);
        yield return PFrames(4);

        // «Delete» asked twice, «Show deleted», «Restore», against script.delete
        int delNo = imported?.No ?? -1;
        string del = "scripts-store-row-" + delNo;
        yield return JuClick(del + "-delete");
        Check("the first press of «Delete» asks and hides nothing", JuSp.Asking == delNo && !ScriptStore.ForUserData(JuUserData, null).Find(delNo).Deleted && JuText(del + "-delete") == JuUi.L10n.Get("script.deleteAsk"),
            JuSp.Asking + " " + JuText(del + "-delete"));
        yield return JuClick(del + "-delete");
        Check("the second press hides the row", ScriptStore.ForUserData(JuUserData, null).Find(delNo).Deleted && JuQ(del) == null);
        yield return JuCmd(new JObject { ["cmd"] = "script.delete", ["no"] = cmdNo }, "script.delete");
        Check("«Delete» equals script.delete: both rows hidden, their files kept", ScriptStore.ForUserData(JuUserData, null).Find(cmdNo).Deleted && File.Exists(ScriptStore.ForUserData(JuUserData, null).PathOf(delNo)));
        JuSp.MarkDirty();
        yield return JuClick("scripts-show-deleted");
        yield return PFrames(4);
        Check("«Show deleted» lists the hidden rows with «Restore»", JuQ(del + "-restore") != null && JuQ("scripts-store-row-" + cmdNo + "-restore") != null);
        yield return JuClick(del + "-restore");
        Check("«Restore» shows the row again", !ScriptStore.ForUserData(JuUserData, null).Find(delNo).Deleted);
        yield return JuCmd(new JObject { ["cmd"] = "script.delete", ["no"] = cmdNo, ["restore"] = true }, "script.delete {restore}");
        Check("«Restore» equals script.delete {restore}", !ScriptStore.ForUserData(JuUserData, null).Find(cmdNo).Deleted);

        // «Purge…» against script.purge: each removes the file of one hidden row
        yield return JuCmd(new JObject { ["cmd"] = "script.delete", ["no"] = cmdNo }, "script.delete before script.purge");
        yield return JuCmd(new JObject { ["cmd"] = "script.purge" }, "script.purge");
        var cmdPurge = pLast;
        yield return JuCmd(new JObject { ["cmd"] = "script.delete", ["no"] = delNo }, "script.delete before «Purge…»");
        JuSp.MarkDirty();
        yield return PFrames(4);
        string delFile = ScriptStore.ForUserData(JuUserData, null).PathOf(delNo);
        yield return JuClick("scripts-purge");
        yield return PFrames(3);
        Check("«Purge…» lists the hidden row it removes", JuUi.Dialogs.Current?.Spec.Id == ScriptsDialogs.PurgeId && JuQ(ScriptsDialogs.PurgeId + "-row-" + delNo) != null && JuQ(ScriptsDialogs.PurgeId + "-row-" + keptNo) == null);
        yield return JuClick("dialog-button-purge");
        yield return PFrames(3);
        store = ScriptStore.ForUserData(JuUserData, null);
        Check("Present: the purge removed the hidden row's file", !File.Exists(delFile) && store.Find(delNo) == null && File.Exists(store.PathOf(keptNo)), delFile);
        Check("«Purge…» equals script.purge: one row and one file each", cmdPurge != null && (int?)cmdPurge["rows"] == 1 && (int?)cmdPurge["files"] == 1 &&
            JuText("scripts-store-note") == JuUi.L10n.Format("script.purged", 1, 1, BakeUi.Size((long?)cmdPurge["bytes"] ?? 0)), JuJson(cmdPurge) + " / " + JuText("scripts-store-note"));
        yield return JuClick("scripts-show-deleted");

        // «Remove» against script.remove: the loaded script's mark goes, the Timeline stays
        yield return JuCmd(new JObject { ["cmd"] = "script.play", ["script"] = juScript, ["prepare"] = "never" }, "script.play before «Remove»");
        ScriptPlayer.Stop(app, false);
        JuSp.Select(juScript);
        yield return PFrames(4);
        Check("Present: the Timeline was loaded from the script", app.Session.Model.Timeline?.Script?.Id == juScript);
        u = JuUndos;
        int count0 = app.Session.Model.Scripts.Count;
        yield return JuClick("scripts-remove");
        bool panelGone = JuScriptOf(juScript) == null, panelKept = app.Session.Model.Timeline != null && app.Session.Model.Timeline.Script == null;
        Check("«Remove» removes the script in one entry «Remove script»; the Timeline stays without its mark", panelGone && panelKept && JuUndos == u + 1 && JuTop == ScriptEditCommand.LabelRemove,
            panelGone + " " + panelKept + " " + (JuUndos - u) + " " + JuTop);
        JuUndo(1);
        Check("its undo brings the script and the mark back", JuScriptOf(juScript) != null && app.Session.Model.Timeline?.Script?.Id == juScript);
        yield return JuCmd(new JObject { ["cmd"] = "script.remove", ["script"] = juScript }, "script.remove");
        Check("«Remove» equals script.remove", JuScriptOf(juScript) == null && (app.Session.Model.Scripts?.Count ?? 0) == count0 - 1 && app.Session.Model.Timeline != null && app.Session.Model.Timeline.Script == null &&
            JuTop == ScriptEditCommand.LabelRemove);
        JuUndo(1);
        JuSp.Select(juScript);
        yield return PFrames(4);
    }

    // ------------------------------------------------------------ JU5: the Timeline panel's header

    static IEnumerator JuHeader()
    {
        if (!POpen("JU5", "the Timeline panel's header: the script's name, «edited», «Save edits into the script»")) yield break;
        var app = JuApp;
        yield return JuCastAll();
        yield return JuCmd(new JObject { ["cmd"] = "script.play", ["script"] = juScript, ["prepare"] = "never" }, "script.play");
        ScriptPlayer.Stop(app, false);
        yield return PCall(app, new { cmd = "ui.action", action = ActionIds.TimelinePanel });
        yield return PFrames(5);
        if (!TimelineUiModule.PanelShown(JuUi)) { yield return PCall(app, new { cmd = "ui.action", action = ActionIds.TimelinePanel }); yield return PFrames(5); }
        var name = JuScriptOf(juScript)?.Name;
        var line = JuPanel.visualTree.Q<TimelineScriptLine>();
        Check("Present: the Timeline came from the script", app.Session.Model.Timeline?.Script?.Id == juScript);
        Check("the header reads «Script: <name>», no «edited», no button", line != null && JuVisible(line) && JuText("timeline-script-name") == JuUi.L10n.Format("timeline.fromScript", name) &&
            !JuVisible(JuQ("timeline-script-edited")) && !JuVisible(JuQ("timeline-script-capture")), JuText("timeline-script-name"));
        int computed = line?.Computed ?? 0;
        yield return PFrames(30);
        Check("idle, the line does not compute «edited» again", line != null && line.Computed == computed, (line?.Computed - computed).ToString());
        // an edit of the Timeline: «edited» and the button
        yield return JuCmd(new JObject { ["cmd"] = "timeline.key.set", ["track"] = 0, ["t"] = 1.0, ["v"] = new JArray(0, 1, 0) }, "timeline.key.set");
        yield return PFrames(4);
        Check("after an edit the header reads «edited» and shows «Save edits into the script»", app.Session.Model.Timeline.IsEditedSinceLoad && JuVisible(JuQ("timeline-script-edited")) &&
            JuVisible(JuQ("timeline-script-capture")) && JuText("timeline-script-edited") == JuUi.L10n.Get("script.edited"));
        int u = JuUndos;
        yield return JuClick("timeline-script-capture");
        yield return PFrames(4);
        var docPanel = JuDocNoId(JuScriptOf(juScript));
        int keys = JuScriptOf(juScript)?.Timeline?.Tracks.FirstOrDefault()?.Keys.Count ?? 0;
        Check("«Save edits into the script»: one entry «Save edits into script», the key in the script, «edited» gone", JuUndos == u + 1 && JuTop == ScriptEditCommand.LabelCapture && keys == 3 &&
            !app.Session.Model.Timeline.IsEditedSinceLoad && !JuVisible(JuQ("timeline-script-edited")) && !JuVisible(JuQ("timeline-script-capture")), (JuUndos - u) + " " + JuTop + " keys " + keys);
        JuUndo(1);
        Check("its undo: «edited» again", app.Session.Model.Timeline.IsEditedSinceLoad);
        yield return JuCmd(new JObject { ["cmd"] = "script.capture" }, "script.capture");
        Check("«Save edits into the script» equals script.capture: the same script", docPanel != null && JToken.DeepEquals(docPanel, JuDocNoId(JuScriptOf(juScript))) && JuTop == ScriptEditCommand.LabelCapture);
        // a renamed script: the header follows
        var named = JObject.Parse(Encoding.UTF8.GetString(JuScriptOf(juScript).ToDocumentBytes()));
        named["name"] = "Header check";
        yield return JuCmd(new JObject { ["cmd"] = "script.set", ["script"] = named, ["replace"] = true }, "script.set {replace} with a new name");
        yield return PFrames(4);
        Check("the header follows a rename", JuText("timeline-script-name") == JuUi.L10n.Format("timeline.fromScript", "Header check"), JuText("timeline-script-name"));
        // a Timeline of no script: the line hidden
        yield return JuCmd(new JObject { ["cmd"] = "script.remove", ["script"] = juScript }, "script.remove (the mark goes)");
        yield return PFrames(4);
        Check("a Timeline of no script: the line hidden", app.Session.Model.Timeline != null && app.Session.Model.Timeline.Script == null && line != null && !JuVisible(line));
    }

}

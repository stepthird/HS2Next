// BakeTest.play, the panels «Chara to Item» and «Cutter», their sections and the tool that places a marker (Phase 6.8 package U): the app with the UI in Editor play, BR-Chan, input
// through ui.input (synthetic pointer, wheel, key and text events raised through the panel); judged by the widgets, the history, the store and the scene; a render stays in
// memory and is judged by numbers. No real pointer is involved here.
//   BakeTest.RunPlay  play mode, the app (port 17899); report Spike/out/bake/bake_play_report.txt; the run's own UserData Spike/out/bake/play_userdata (its layout.json and its
//                     store are removed before the run, so the panels take their lazy path and the store starts empty). Parts Play_<x>() (IEnumerator) in ordinal order.
//                     A click waits for the toolbar's first layout and for the toasts of earlier steps that cover its point (a press on a toast only takes it away).
//   BY3   every key of the block #U68 in en, ja and zh-Hans with the same placeholders, the texts of spec 15.4, the history labels as the operations name them, the words of the block,
//         no other subject; every key the panels and sections use is in the table
//   BY4   the panels appear when asked for (lazy, region bottom, the View menu, «Reset layout»); the State tab's section and the item inspector's section; a closed panel and a
//         closed section read nothing
//   BY4p  the panel «Chara to Item»: «Use selected», the presets and parts, «Bake» in one entry «Bake to item» with its progress line, the name used once
//   BY4c  the panel «Cutter»: the target, «Add plane», the mode, the section colour, «Cut» stepped one mesh per frame in one entry «Cut», «Apply to piece» in one entry «Edit cut»,
//         the inspector's «Edit cut» and «Re-cut from source»
//   BY5   the store's list: rows, "damaged", rename in place (Enter confirms, Esc cancels; the items with the old name in one entry), delete asked twice, «Show deleted», «Restore»,
//         the purge dialog lists what it removes, «Import old bakes…» from the test's folder of synthetic old bakes; with a list that defines 731406 (the app's group absent)
//         «Add» of a store row still adds one item that builds from its blobs, in one entry (spec 9.7)
//   BI1   CutTool: a press on a surface puts the marker at the hit, Y along the normal and X along the view's right within 1 degree; the marker follows the pointer; the wheel turns it
//         15 degrees a notch about the normal; the release is one entry «Move marker»; a press on the marker finds the surface behind it; a miss, a plane marker and a modifier pass;
//         Esc puts the marker back, then ends the tool
//   BI2   markers: shown in the view (at least 500 px), 0 px in a capture
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HS2Next.Api.Unity;
using HS2Next.App;
using HS2Next.App.Commands;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Bake;
using HS2Next.Scenes.Cut;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using HS2Next.UI.Viewport;
using HS2Next.UI.Widgets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

public static partial class BakeTest
{
    /// <summary>The play row's port.</summary>
    public const int PlayPort = 17899;
    static string PlayReport => Arg("-report") ?? Path.Combine(OutDir, "bake_play_report.txt");
    const string PlayUserData = "Spike/out/bake/play_userdata";
    static string PlayUserDataDir => Path.Combine(ContentSettings.ProjectRoot, PlayUserData);
    static string PlayLaunch =>
        "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":true,\"language\":\"en\"},\"userData\":\"" + PlayUserData + "\",\"bridge\":{\"port\":" + (Arg("-port") ?? PlayPort.ToString()) +
        ",\"outputDir\":\"Spike/out/bake/play_out\",\"allowQuit\":false}}";

    // ------------------------------------------------------------ the play harness

    static JObject pLast;
    static string pLastText;

    static bool POk => pLast?.Value<bool?>("ok") == true;

    /// <summary>One bridge request in real frames; the reply lands in pLast.</summary>
    static IEnumerator PCall(AppCore app, object req, double timeoutSec = 300)
    {
        string json = req is string s ? s : JsonConvert.SerializeObject(req);
        var t = app.Bridge.Execute(json);
        float until = Time.realtimeSinceStartup + (float)timeoutSec;
        while (!t.IsCompleted && Time.realtimeSinceStartup < until) yield return null;
        pLastText = t.IsCompleted ? (t.IsFaulted ? "{\"ok\":false,\"error\":\"faulted\"}" : t.Result) : "{\"ok\":false,\"code\":\"timeout\"}";
        try { pLast = JObject.Parse(pLastText); }
        catch (Exception) { pLast = new JObject { ["ok"] = false, ["error"] = "unparsable: " + pLastText }; }
    }

    /// <summary>n real frames (the harness pumps a coroutine from the Editor's update, so one yield is not one frame).</summary>
    static IEnumerator PFrames(int n) => PlayModeHarness.WaitFrames(n);

    /// <summary>Opens a play group; false when -only leaves it out.</summary>
    static bool POpen(string id, string title)
    {
        if (!Want(id)) return false;
        current = id;
        rep.AppendLine();
        rep.AppendLine($"== {id} {title}");
        return true;
    }

    public static void RunPlay()
    {
        Directory.CreateDirectory(OutDir);
        // the run's own UserData: no layout.json (the panels' lazy path) and no store (it starts empty); nothing else is touched
        try { File.Delete(Path.Combine(PlayUserDataDir, "config", LayoutDocument.FileName)); } catch (Exception) { }
        UClearStore();
        File.WriteAllText(PlayReport, "SUMMARY: 0 PASS, 1 FAIL\nBakeTest.play: started, no result yet\n", new UTF8Encoding(false));
        PlayModeHarness.Start("BakeTest.PlayMain", PlayReport, 25, true, PlayLaunch);
    }

    /// <summary>Removes the store of the run's own UserData (Spike/out/bake/play_userdata/bakes) and nothing else.</summary>
    static void UClearStore()
    {
        string dir = Path.Combine(PlayUserDataDir, BakeStore.FolderName);
        if (!Directory.Exists(dir)) return;
        string blobs = Path.Combine(dir, BakeStore.BlobsName);
        if (Directory.Exists(blobs)) { foreach (var f in Directory.GetFiles(blobs)) File.Delete(f); Directory.Delete(blobs, false); }
        foreach (var f in Directory.GetFiles(dir)) File.Delete(f);
        Directory.Delete(dir, false);
    }

    static IEnumerator PlayMain()
    {
        Begin();
        var sw = Stopwatch.StartNew();
        rep.AppendLine($"BakeTest.play (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}, screen {Screen.width}x{Screen.height}); input through ui.input, no real pointer");
        try { LoadTargets(); }
        catch (Exception e) { current = "play"; Check("targets", false, e.ToString()); }
        var parts = Parts("Play_", typeof(IEnumerator));
        Info("parts: " + string.Join(", ", parts.Select(p => p.Name.Substring("Play_".Length))));
        foreach (var p in parts)
        {
            current = p.Name;
            IEnumerator body = null;
            try { body = (IEnumerator)p.Invoke(null, null); }
            catch (Exception e) { Check("part " + p.Name + " started", false, (e.InnerException ?? e).ToString()); }
            if (body != null) yield return USafe(body);
        }
        try { if (UUi?.Tools != null && UUi.Tools.Running) UUi.Tools.End(UUi.Tools.Current); } catch (Exception) { }
        HS2Next.Motion.Face.FaceSetup.Seed = null;
        End(PlayReport, sw, "BakeTest.RunPlay (play mode)");
        Debug.Log($"BAKE PLAY TEST DONE: {passes} PASS, {fails} FAIL");
        PlayModeHarness.ExitCode = fails > 0 ? 1 : 0;
    }

    // ------------------------------------------------------------ the app and its UI

    static AppCore UApp => AppCore.Current;
    static UiHost UHost => UiHost.Current;
    static UiContext UUi => UHost?.Context;
    static ToolHost UTools => UUi.Tools;
    static ViewportHost UVp => UUi.Viewport;
    static IPanel UPanel => UHost?.Root?.panel;
    static SceneRuntime URt => UApp.Runtime;
    static int UUndos => UUi.Session.Commands.UndoList.Count;
    static string UTop => UUi.Session.Commands.UndoList.Count > 0 ? UUi.Session.Commands.UndoList[0].Label : null;
    static BakePanel UBp => BakePanel.Current;
    static CutPanel UCp => CutPanel.Current;
    static string uA;
    static Vector2 uEmpty;

    /// <summary>Runs a group's coroutine by hand, nested ones too: an exception is a FAIL of the group, and the row goes on to write its report.</summary>
    static IEnumerator USafe(IEnumerator body)
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

    static IEnumerator UIn(params JObject[] events)
    {
        yield return PCall(UApp, new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(events.Cast<object>().ToArray()), ["frames"] = 1 });
        if (!POk) Check("ui.input", false, Short(pLastText));
        yield return PFrames(1);
    }

    /// <summary>Events through ui.input with no frame waited after them: what a handler shows at once can be read before a deferred step runs.</summary>
    static IEnumerator UInNow(params JObject[] events)
    {
        yield return PCall(UApp, new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(events.Cast<object>().ToArray()), ["frames"] = 0 });
        if (!POk) Check("ui.input", false, Short(pLastText));
    }

    /// <summary>A click on an element by name with no frame waited after it (the element brought into sight first).</summary>
    static IEnumerator UClickNow(string name)
    {
        var e = UQ(name);
        yield return UReveal(e);
        bool seen = e != null && UVisible(e);
        if (!Check("the element " + name, seen, seen ? "" : e == null ? "absent" : "not visible")) yield break;
        yield return UUncovered(e);
        UPointOn(e);
        yield return UInNow(UPt(e.worldBound.center, "click"));
    }

    static JObject UPt(Vector2 p, string phase, int button = 0, string mods = null)
    {
        var o = new JObject { ["type"] = "pointer", ["x"] = p.x, ["y"] = p.y, ["phase"] = phase, ["button"] = button, ["clicks"] = 1 };
        if (mods != null) o["mods"] = mods;
        return o;
    }

    static JObject UWheel(Vector2 p, float delta) => new JObject { ["type"] = "wheel", ["x"] = p.x, ["y"] = p.y, ["delta"] = delta };

    static JObject UKey(string key, string mods = null)
    {
        var o = new JObject { ["type"] = "key", ["key"] = key };
        if (mods != null) o["mods"] = mods;
        return o;
    }

    static JObject UText(string text) => new JObject { ["type"] = "text", ["text"] = text };

    static bool UVisible(VisualElement e)
    {
        if (e == null || e.panel == null) return false;
        for (var x = e; x != null; x = x.hierarchy.parent) if (x.resolvedStyle.display == DisplayStyle.None) return false;
        return e.worldBound.width > 0;
    }

    static IEnumerator UReveal(VisualElement e)
    {
        for (var x = e?.hierarchy.parent; x != null; x = x.hierarchy.parent)
            if (x is Foldout f && !f.value) f.value = true;
        yield return PFrames(2);
        for (var x = e?.hierarchy.parent; x != null; x = x.hierarchy.parent)
            if (x is ScrollView sv) sv.ScrollTo(e);
        yield return PFrames(3);
    }

    /// <summary>A click on an element of the UI (by name, anywhere in the panel), brought into sight first.</summary>
    static IEnumerator UClick(string name)
    {
        var e = UPanel?.visualTree.Q(name);
        yield return UReveal(e);
        bool seen = e != null && UVisible(e);
        if (!Check("the element " + name, seen, seen ? "" : e == null ? "absent" : "not visible")) yield break;
        yield return UUncovered(e);
        UPointOn(e);
        yield return UIn(UPt(e.worldBound.center, "click"));
        yield return PFrames(2);
    }

    /// <summary>A toast of an earlier step that covers the centre of <paramref name="e"/> goes first (a press on a toast takes the toast away and reaches nothing
    /// under it): the click waits until the toasts have expired, at most 6 s.</summary>
    static IEnumerator UUncovered(VisualElement e)
    {
        var at = UPanel.Pick(e.worldBound.center);
        if (at == null || at.name != "toast") yield break;
        var gone = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => UUi.Toasts.Count == 0, 6, gone);
        Info("a toast covered " + e.name + ": waited until the toasts expired (" + (gone[0] ? "gone" : "still there") + ")");
    }

    /// <summary>A note when the element under the centre of <paramref name="e"/> is neither it nor inside it (diagnosis of a click).</summary>
    static void UPointOn(VisualElement e)
    {
        var at = UPanel.Pick(e.worldBound.center);
        for (var x = at; x != null; x = x.hierarchy.parent) if (x == e) return;
        Info("the point " + e.worldBound.center + " of " + e.name + " lies on " + (at == null ? "nothing" : at.GetType().Name + " " + at.name) + "; the element's bound " + e.worldBound);
    }

    static VisualElement UQ(string name) => UPanel?.visualTree.Q(name);

    static string UTextOf(string name) => (UQ(name) as TextElement)?.text;

    static Vector2 UOn(Vector3 world) => UVp.Gizmo.ToPanel(world);

    static string UCam()
    {
        var t = UVp.ViewCamera.transform;
        return t.position.ToString("F4") + " " + t.rotation.ToString("F4");
    }

    /// <summary>The app and its UI ready, a new scene with BR-Chan standing still, the layout reset, BR-Chan selected; false when it failed.</summary>
    static IEnumerator ULoad(bool[] ok)
    {
        ok[0] = false;
        var ready = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting && UiHost.Current != null && UiHost.Current.IsReady, 240, ready);
        if (!Check("app and UI ready", ready[0])) yield break;
        if (UTools.Running) UTools.End(UTools.Current);
        UUi.Dialogs?.CloseCurrent();
        yield return PCall(UApp, new { cmd = "scene.new" });
        Check("scene.new", POk, Short(pLastText));
        HS2Next.Motion.Face.FaceSetup.Seed = 0x5eed;
        yield return PCall(UApp, new { cmd = "chara.load", path = femaleRel, sex = "female" });
        Check("chara.load BR-Chan", POk, Short(pLastText));
        uA = Key(pLast);
        yield return PlayModeHarness.WaitUntil(() => URt.FindChara(uA)?.State == RealizationState.Built, 180, ready);
        if (!Check("BR-Chan built", ready[0])) yield break;
        yield return PCall(UApp, new { cmd = "chara.anime", key = uA, group = 19, category = 0, no = 0, time = 0.5f, speed = 0f });
        UUi.Shell.ResetLayout();
        UUi.Ops.Select(new[] { uA });
        yield return PFrames(3);
        ok[0] = true;
    }

    /// <summary>Waits until <paramref name="done"/> or <paramref name="seconds"/> of real time; the outcome in the returned flag.</summary>
    static IEnumerator UWait(Func<bool> done, double seconds, bool[] ok) => PlayModeHarness.WaitUntil(done, seconds, ok);

    // ------------------------------------------------------------ BY3: the strings

    static IEnumerator Play_U_0_Strings()
    {
        if (!POpen("BY3", "the strings of #U68")) yield break;
        BY3();
        yield break;
    }

    // the keys and texts spec 15.4 gives (null: not given there)
    static readonly Dictionary<string, string[]> SpecStrings = new Dictionary<string, string[]>
    {
        ["panel.bake"] = new[] { "Chara to Item", "キャラをアイテムに", "角色转道具" },
        ["panel.cut"] = new[] { "Cutter", "カッター", "切割" },
        ["bake.make"] = new[] { "Bake", "焼く", "烘焙" },
        ["bake.whole"] = new[] { "Whole", "全体", "整体" },
        ["bake.preset.body"] = new[] { "Body", "体", "身体" },
        ["bake.preset.hair"] = new[] { "Hair", "髪", "头发" },
        ["bake.preset.clothes"] = new[] { "Clothes", "服", "服装" },
        ["bake.preset.accs"] = new[] { "Accessories", "アクセ", "饰品" },
        ["bake.split"] = new[] { "One item per part", "部位ごとに別のアイテム", "每个部位一个道具" },
        ["bake.fill"] = new[] { "Fill hollow parts", "空洞を塞ぐ", "填充空心部位" },
        ["bake.centre"] = new[] { "Origin at the parts' centre", "原点を部位の中心に", "原点在部位中心" },
        ["bake.hide"] = new[] { "Hide the character", "キャラを隠す", "隐藏角色" },
        ["bake.keep"] = new[] { "Keep in the store", "置き場に残す", "保存到库" },
        ["bake.store"] = new[] { "Bakes", "焼いた物", "烘焙列表" },
        ["bake.import"] = new[] { "Import old bakes…", "古い焼き物を取り込む…", "导入旧烘焙…" },
        ["bake.purge"] = new[] { "Purge…", "完全に消す…", "清除…" },
        ["cut.make"] = new[] { "Cut", "切る", "切割" },
        ["cut.marker.plane"] = new[] { "Plane", "平面", "平面" },
        ["cut.marker.box"] = new[] { "Box", "箱", "盒" },
        ["cut.marker.tear"] = new[] { "Tear", "裂け目", "裂口" },
        ["cut.marker.hole"] = new[] { "Hole", "穴", "孔" },
        ["cut.mode.both"] = new[] { "Both sides", "両側", "两侧" },
        ["cut.mode.front"] = new[] { "Front", "表", "正面" },
        ["cut.mode.back"] = new[] { "Back", "裏", "背面" },
        ["cut.mode.inside"] = new[] { "Inside", "内側", "内侧" },
        ["cut.mode.outside"] = new[] { "Outside", "外側", "外侧" },
        ["cut.section"] = new[] { "Section colour", "断面の色", "截面颜色" },
        ["cut.ownInside"] = new[] { "Own fabric inside", "服と髪は自分の裏地", "服装和头发显示自身内侧" },
        ["cut.ragged"] = new[] { "Ragged edge", "ギザギザ", "锯齿边" },
        ["cut.tooth"] = new[] { "Tooth size", "歯の間隔", "齿距" },
        ["cut.random"] = new[] { "Randomness", "不揃いさ", "随机度" },
        ["cut.pattern"] = new[] { "Pattern #", "模様番号", "图案编号" },
        ["cut.clothesOnly"] = new[] { "Clothes only", "服だけ", "仅服装" },
        ["cut.tool.hint"] = new[] { "Press on a surface: place the marker there. Wheel: turn it. Esc: leave.", "面を押す: 切断具をそこに置く。ホイール: 回す。Esc: 終わる。", null },
        // handed over by A and C
        ["bake.rows.bakes"] = new[] { "Baked objects", "焼いた物", "烘焙物体" },
        ["bake.rows.markers"] = new[] { "Cut tools", "切断具", "切割工具" },
        ["history.bake.make"] = new[] { "Bake to item", "アイテムに焼く", "烘焙为道具" },
        ["history.bake.rename"] = new[] { "Rename bake", "焼いた物の名前を変える", "重命名烘焙" },
        ["history.cut.make"] = new[] { "Cut", "切る", "切割" },
        ["history.cut.edit"] = new[] { "Edit cut", "切り方を変える", "编辑切割" },
        ["history.cut.moveMarker"] = new[] { "Move marker", "切断具を動かす", "移动切割工具" },
    };

    // every English word the block may use: the feature's words of spec 15.4 and the mechanical words of its rows (a word added here is a choice the owner reads at the release review)
    static readonly HashSet<string> BlockWords = new HashSet<string>(StringComparer.Ordinal)
    {
        // spec 15.4
        "bake", "baked", "bakes", "baking", "item", "items", "part", "parts", "cut", "cutter", "cutting", "plane", "box", "tear", "hole", "section", "ragged", "store", "import", "imported",
        "importing", "marker", "chara", "piece", "pieces",
        // the rows of 15.2 and 15.3
        "whole", "body", "hair", "clothes", "accessories", "accessory", "one", "per", "fill", "hollow", "origin", "at", "the", "s", "centre", "hide", "character", "characters", "keep",
        "in", "old", "purge", "both", "sides", "front", "back", "inside", "outside", "colour", "own", "fabric", "edge", "tooth", "size", "randomness", "pattern", "only", "use", "selected",
        "add", "rename", "delete", "deleted", "show", "restore", "name", "fk", "bones", "options", "folder", "apply", "to", "target", "place", "on", "surface", "with", "new", "dark", "red",
        "flesh", "bone", "black", "grey", "rgb", "head", "side", "extension", "other", "wheel", "turn", "it", "esc", "leave", "press", "there", "panel", "edit", "move", "re", "from",
        "source", "kind", "steps", "open", "objects", "tools", "last", "damaged", "kept", "holds", "already", "as",
        // the lines and notes
        "a", "an", "and", "or", "of", "no", "not", "yet", "is", "be", "this", "then", "first", "here", "has", "for", "give", "gives", "same", "result", "takes", "made", "matches",
        "cannot", "list", "row", "rows", "files", "uses", "removes", "removed", "skipped", "index", "read", "copied", "aside", "next", "write", "needs", "graphics", "device",
        "built", "capturing", "again", "nothing", "select", "mosaic",
    };

    // words of other subjects, in any language
    static readonly string[] OtherSubjects =
    {
        "plugin", "HS2", "Studio", "mod", "hook", "BepInEx", "KKAPI", "MaterialEditor", "CharaToItem", "AI", "プラグイン", "插件", "モッド",
    };

    static void BY3()
    {
        string file = Path.Combine(ContentSettings.ProjectRoot, "Assets", "HS2Next", "Resources", "UI", "L10n", "strings.tsv");
        string text = File.ReadAllText(file, Encoding.UTF8);
        var tb = L10nTable.Parse(text);
        var rows = new List<string[]>();
        bool inside = false;
        foreach (string line in text.Split('\n'))
        {
            if (line.StartsWith("#", StringComparison.Ordinal)) { inside = line.StartsWith("#U68", StringComparison.Ordinal); continue; }
            if (inside && line.Length > 0) rows.Add(line.Split('\t'));
        }
        Check("Present: the block #U68 holds its rows", rows.Count >= 100, rows.Count + " rows");
        var keys = rows.Select(r => r[0]).ToList();
        Check("no key twice in the block", keys.Count == keys.Distinct().Count());
        string[] lang = { "en", "ja", "zh-Hans" };
        var missing = new List<string>();
        foreach (var k in keys)
            foreach (var l in lang)
                if (string.IsNullOrEmpty(tb.Cell(k, l))) missing.Add(k + " (" + l + ")");
        Check("every key of the block in en, ja and zh-Hans", missing.Count == 0, keys.Count + " keys, missing " + missing.Count + ": " + string.Join(", ", missing.Take(20)));
        int wrong = 0;
        foreach (var kv in SpecStrings)
        {
            if (!keys.Contains(kv.Key)) { wrong++; Check("the block holds " + kv.Key, false); continue; }
            for (int i = 0; i < 3; i++)
                if (kv.Value[i] != null && kv.Value[i] != tb.Cell(kv.Key, lang[i])) { wrong++; Check(kv.Key + " in " + lang[i] + " as spec 15.4 or its package gives it", false, tb.Cell(kv.Key, lang[i])); }
        }
        Check("the " + SpecStrings.Count + " keys spec 15.4 and the packages give hold their texts", wrong == 0, wrong + " differ");
        var marks = new Regex(@"\{\d\}");
        int placeholders = 0;
        foreach (var k in keys)
        {
            var en = marks.Matches(tb.Cell(k, "en") ?? "").Cast<Match>().Select(m => m.Value).OrderBy(x => x).ToList();
            bool same = en.SequenceEqual(marks.Matches(tb.Cell(k, "ja") ?? "").Cast<Match>().Select(m => m.Value).OrderBy(x => x)) &&
                        en.SequenceEqual(marks.Matches(tb.Cell(k, "zh-Hans") ?? "").Cast<Match>().Select(m => m.Value).OrderBy(x => x));
            if (!same) Check(k + ": the same placeholders in every language", false);
            if (en.Count > 0) placeholders++;
        }
        Check("the same placeholders in every language", true, placeholders + " keys with placeholders");
        var unmarked = rows.Where(r => r.Length < 5 || r[4] != "zh?").Select(r => r[0]).ToList();
        Check("every zh-Hans cell awaits its review (zh?), as the neighbouring blocks", unmarked.Count == 0, string.Join(", ", unmarked.Take(10)));
        var history = new Dictionary<string, string>
        {
            ["history.bake.make"] = OpLabels.BakeMake, ["history.bake.rename"] = OpLabels.BakeRename, ["history.cut.make"] = OpLabels.CutMake, ["history.cut.edit"] = OpLabels.CutEdit,
            ["history.cut.moveMarker"] = OpLabels.CutMoveMarker,
        };
        foreach (var h in history) Check("the history label " + h.Key + " is the operation's «" + h.Value + "»", tb.Cell(h.Key, "en") == h.Value && OpLabels.All.Contains(h.Value), tb.Cell(h.Key, "en"));
        Check("the panels' actions have their labels", tb.Has(ActionIds.LabelKey(ActionIds.BakePanel)) && tb.Has(ActionIds.LabelKey(ActionIds.CutPanel)));
        var word = new Regex(@"[A-Za-z]+");
        var outside = new SortedSet<string>(StringComparer.Ordinal);
        int words = 0;
        foreach (var r in rows)
            foreach (Match m in word.Matches(r[1]))
            {
                words++;
                if (!BlockWords.Contains(m.Value.ToLowerInvariant())) outside.Add(r[0] + ": " + m.Value);
            }
        Check("every English word of the block is on the list", outside.Count == 0, words + " words read, " + outside.Count + " not on it: " + string.Join("; ", outside.Take(20)));
        int cells = 0, hits = 0;
        foreach (var r in rows)
            foreach (string cell in r.Skip(1).Take(3))
            {
                cells++;
                foreach (var w in OtherSubjects)
                {
                    bool ascii = w.All(c => c < 128);
                    bool hit = ascii ? Regex.IsMatch(cell, @"(?<![A-Za-z])" + Regex.Escape(w) + @"(?![A-Za-z])", RegexOptions.IgnoreCase) : cell.Contains(w);
                    if (hit) { hits++; Check(r[0] + " names no other subject (" + w + ")", false, cell); }
                }
            }
        Check("no cell names another subject", hits == 0, "Present: " + cells + " cells searched for " + OtherSubjects.Length + " words");
        // every key the panels, sections and the tool use stands in the table
        string dir = Path.Combine(ContentSettings.ProjectRoot, "Assets", "HS2Next", "Runtime", "UI", "Panels");
        var used = new SortedSet<string>(StringComparer.Ordinal);
        int files = 0;
        foreach (string sub in new[] { "Bake", "Cut" })
            foreach (string f in Directory.GetFiles(Path.Combine(dir, sub), "*.cs", SearchOption.AllDirectories))
            {
                files++;
                foreach (Match m in Regex.Matches(File.ReadAllText(f, Encoding.UTF8), "\"((?:bake|cut|panel|action|inspector)\\.[A-Za-z0-9.]*)\"")) used.Add(m.Groups[1].Value);
            }
        var expanded = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var k in used)
        {
            if (!k.EndsWith(".", StringComparison.Ordinal)) { expanded.Add(k); continue; }
            IEnumerable<string> tails = k switch
            {
                "bake.kind." => BakeKinds.Kinds,
                "bake.part." => new[] { "body", "head", "hair0", "hair1", "hair2", "hair3", "other" },
                "bake.preset." => BakePanel.Presets.Where(p => p != "whole"),
                "cut.add." => CutPanel.Kinds,
                "cut.marker." => CutPanel.Kinds,
                "cut.mode." => new[] { "both", "front", "back", "inside", "outside" },
                "cut.section." => CutShapeInfo.SectionPresets.Select(p => p.name),
                "inspector.chara.fk.group." => BakePanel.FkKeys,
                "inspector.charaEdit.clothes.kind." => new[] { "top", "bottom", "innerTop", "innerBottom", "gloves", "pantyhose", "socks", "shoes" },
                _ => new[] { "?" },
            };
            foreach (var t in tails) expanded.Add(k + t);
        }
        var absent = expanded.Where(k => !tb.Has(k)).ToList();
        Check("every key the panels, sections and tool use is in the table", absent.Count == 0, "Present: " + expanded.Count + " keys from " + files + " files; absent " + string.Join(", ", absent));
    }

    // ------------------------------------------------------------ BY4: the panels come when asked for, the sections

    static IEnumerator Play_U_1_Lazy()
    {
        if (!Want("BY4")) yield break;
        current = "BY4";
        var ready = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting && UiHost.Current != null && UiHost.Current.IsReady, 240, ready);
        if (!POpen("BY4", "the panels appear when asked for; the sections; closed reads nothing")) yield break;
        if (!Check("app and UI ready", ready[0])) yield break;
        // the toolbar laid out before its first click (the row may start at once after the UI is ready)
        yield return PlayModeHarness.WaitUntil(() => UQ("tb-view") != null && UQ("tb-view").worldBound.width > 0 && !float.IsNaN(UQ("tb-view").worldBound.x), 10, ready);
        if (!Check("the toolbar is laid out", ready[0], UQ("tb-view")?.worldBound.ToString())) yield break;
        yield return PFrames(5);
        var ui = UUi;
        var sh = ui.Shell;
        string file = Path.Combine(ui.Options.ConfigDir, LayoutDocument.FileName);
        string layout = File.Exists(file) ? File.ReadAllText(file, Encoding.UTF8) : "";
        foreach (var (id, action, title, en) in new[]
        {
            (BakeUiModule.PanelId, ActionIds.BakePanel, BakeUiModule.TitleKey, "Chara to Item"),
            (CutUiModule.PanelId, ActionIds.CutPanel, CutUiModule.TitleKey, "Cutter"),
        })
        {
            bool listed = layout.Contains("\"" + id + "\"");
            bool registered = ui.Registry.Panels.TryGet(id, out _);
            Check(id + ": registered at the start only when layout.json lists it", registered == listed, registered + " / " + listed);
            Info(id + ": layout.json lists it at the start: " + listed);
            Check(id + ": the action stands in the manifest and in the registry", ActionManifest.Contains(action) && ui.Registry.Actions.TryGet(action, out _));
            bool shown() => id == BakeUiModule.PanelId ? BakeUiModule.PanelShown(ui) : CutUiModule.PanelShown(ui);
            if (shown())
            {
                yield return PCall(UApp, new { cmd = "ui.action", action });
                yield return PFrames(3);
            }
            Check(id + ": not shown before it is asked for", !shown());
            var menu = UQ("tb-view");
            yield return UIn(UPt(menu.worldBound.center, "click"));
            yield return PFrames(2);
            Check(id + ": the View menu lists it", UQ("menu-item-" + action) != null);
            ui.Menus?.CloseAll();
            yield return PFrames(2);
            yield return PCall(UApp, new { cmd = "ui.action", action });
            Check(id + ": ui.action opens it", POk && shown(), Short(pLastText));
            yield return PFrames(4);
            VisualElement p = id == BakeUiModule.PanelId ? (VisualElement)UBp : UCp;
            Check(id + ": shown, its title the feature's name", p != null && UVisible(p) && ui.L10n.Get(title) == en, ui.L10n.Get(title));
            Check(id + ": its default region is the bottom", sh.RegionOf(id) == LayoutRegions.Bottom, sh.RegionOf(id));
            yield return PCall(UApp, new { cmd = "ui.action", action });
            yield return PFrames(3);
            Check(id + ": the action again collapses it", !shown());
            sh.ResetLayout();
            yield return PFrames(3);
            Check(id + ": «Reset layout» leaves the bottom region collapsed", sh.Regions[LayoutRegions.Bottom].Collapsed && !shown());
        }
    }

    static IEnumerator Play_U_2_Sections()
    {
        if (!POpen("BY4", "the State tab's section, the item inspector's section, closed panels")) yield break;
        var ok = new bool[1];
        yield return ULoad(ok);
        if (!ok[0]) yield break;
        var ui = UUi;
        ui.Shell.FocusPanel("inspector");
        yield return PFrames(3);
        var insp = ui.Shell.PanelRoot("inspector") as InspectorPanel ?? ui.Shell.PanelRoot("inspector")?.Q<InspectorPanel>();
        insp?.Chara?.ShowTab("state");
        yield return PFrames(4);
        var section = UPanel.visualTree.Q<BakeSection>();
        if (!Check("the section stands in the State tab, closed", section != null && !section.Fold.value, (insp?.Chara != null) + " " + (section != null))) yield break;
        var ragdoll = UPanel.visualTree.Q<RagdollSection>();
        var wet = UPanel.visualTree.Q<WetSection>();
        int at = section.parent.IndexOf(section), n = section.parent.childCount;
        Check("it stands at the end of the tab's own rows, before the ragdoll's and the stains' sections", ragdoll != null && wet != null && at == n - 3 && section.parent.IndexOf(ragdoll) == n - 2 && section.parent.IndexOf(wet) == n - 1, at + " of " + n);
        Check("the strip stays at 13 tabs", insp.Chara.TabBodies.Count == 13, insp.Chara.TabBodies.Count.ToString());
        int sr = section.Reads;
        yield return PFrames(30);
        Check("a closed section reads nothing", section.Reads == sr, (section.Reads - sr).ToString());
        section.Fold.value = true;
        yield return PFrames(3);
        Check("Present: open, it reads", section.Reads > sr, (section.Reads - sr).ToString());
        Check("opened: the line says no bake yet", UTextOf("bake-section-last") == ui.L10n.Get("bake.section.none"), UTextOf("bake-section-last"));

        // «Bake whole»: the character with the panel's options, one entry
        int u = UUndos;
        int items = ui.Model.PreOrder().Count(x => x.Kind == NodeKind.Item);
        var sw = Stopwatch.StartNew();
        yield return UClickNow("bake-section-whole");
        Check("«Bake whole»: the line says it captures", UTextOf("bake-section-last") == ui.L10n.Get("bake.capturing"), UTextOf("bake-section-last"));
        yield return UWait(() => UUndos > u || BakeUiModule.LastBakeOf(uA) != null, 120, ok);
        yield return PFrames(3);
        Measure("BY4 «Bake whole» of BR-Chan in the Editor: " + sw.Elapsed.TotalSeconds.ToString("F1") + " s (not an acceptance number)");
        var bakeItem = ui.Model.PreOrder().FirstOrDefault(x => x.Kind == NodeKind.Item && x.State is ItemNodeState s && s.Bake != null);
        Check("«Bake whole»: one entry «Bake to item», an item drawn from a bake", ok[0] && UUndos == u + 1 && UTop == OpLabels.BakeMake && bakeItem != null && ui.Model.PreOrder().Count(x => x.Kind == NodeKind.Item) == items + 1,
            (UUndos - u) + " " + UTop + " " + (bakeItem != null));
        Check("the character is hidden as the option says", ui.Model.Find(uA)?.Doc.Visible == false);
        if (bakeItem == null) yield break;
        Info("selected after the bake: " + string.Join(",", ui.Selection.Ids) + (ui.Selection.Primary == bakeItem.Id ? " (the new item)" : ""));
        // the line is the character's: its inspector again, the State tab
        ui.Ops.Select(new[] { uA });
        yield return PFrames(3);
        ui.Shell.FocusPanel("inspector");
        yield return PFrames(3);
        insp = ui.Shell.PanelRoot("inspector") as InspectorPanel ?? ui.Shell.PanelRoot("inspector")?.Q<InspectorPanel>();
        insp?.Chara?.ShowTab("state");
        yield return PFrames(4);
        section = UPanel.visualTree.Q<BakeSection>();
        Check("the character's section again, open as it was left", section != null && section.Fold.value);
        string nm = BakeUiModule.LastBakeOf(uA);
        Check("the line names the last bake", nm != null && nm == bakeItem.Label && UTextOf("bake-section-last") == ui.L10n.Format("bake.section.last", nm), UTextOf("bake-section-last") + " / " + bakeItem.Label);
        var row = (UUi.Ops.BakeList().Value as BakeListResult)?.Rows.FirstOrDefault(r => r.Id == ((ItemNodeState)bakeItem.State).Bake.Id);
        Check("«Keep in the store» was on: the store holds the bake", row != null, row?.Number.ToString());

        // «Bake parts…»: the panel with this character
        yield return UClick("bake-section-parts");
        yield return PFrames(4);
        Check("«Bake parts…» shows the panel with this character as its target", BakeUiModule.PanelShown(ui) && UBp != null && UBp.Targets.Count == 1 && UBp.Targets[0] == uA, UBp == null ? "no panel" : string.Join(",", UBp.Targets));
        section.Fold.value = false;

        // a closed panel reads nothing
        var bp = UBp;
        yield return PFrames(6);
        int r0 = bp.Reads;
        Check("Present: the shown panel reads", r0 > 0, r0.ToString());
        yield return PCall(UApp, new { cmd = "ui.action", action = ActionIds.BakePanel });
        yield return PFrames(3);
        Check("the action collapses it", !BakeUiModule.PanelShown(ui));
        r0 = bp.Reads;
        ui.Ops.SetTransform(new[] { bakeItem.Id }, new Vector3(1, 0, 0), null, null, UiContext.User());
        yield return PFrames(30);
        Check("a closed panel reads nothing, also when the scene changes", bp.Reads == r0, (bp.Reads - r0).ToString());
        yield return PCall(UApp, new { cmd = "ui.action", action = ActionIds.CutPanel });
        yield return PFrames(4);
        var cp = UCp;
        Check("Present: the Cutter shown reads", cp != null && cp.Reads > 0, cp?.Reads.ToString());
        yield return PCall(UApp, new { cmd = "ui.action", action = ActionIds.CutPanel });
        yield return PFrames(3);
        int c0 = cp?.Reads ?? 0;
        ui.Ops.SetTransform(new[] { bakeItem.Id }, new Vector3(2, 0, 0), null, null, UiContext.User());
        yield return PFrames(30);
        Check("the Cutter closed reads nothing", cp != null && cp.Reads == c0, cp == null ? "no panel" : (cp.Reads - c0).ToString());

        // the item inspector's section: on an item drawn from a bake, closed until opened
        ui.Ops.Select(new[] { bakeItem.Id });
        yield return PFrames(3);
        ui.Shell.FocusPanel("inspector");
        yield return PFrames(4);
        var item = UPanel.visualTree.Q<BakeItemSection>();
        if (!Check("the item inspector shows the section «Bake», closed", item != null && !item.Fold.value && !item.Piece)) yield break;
        int ir = item.Reads;
        yield return PFrames(30);
        Check("a closed section reads nothing", item.Reads == ir, (item.Reads - ir).ToString());
        item.Fold.value = true;
        yield return PFrames(4);
        Check("Present: open, it reads", item.Reads > ir, (item.Reads - ir).ToString());
        var status = UUi.Ops.BakeStatus(bakeItem.Id).Value as BakeStatusInfo;
        Check("its lines: kind, source, size, mosaic parts as bake.status reads them", status != null &&
            UTextOf("bake-item-kind") == ui.L10n.Format("bake.item.kind", BakePanel.KindText(status.Kind)) &&
            UTextOf("bake-item-source") == ui.L10n.Format("bake.item.source", status.Source?.Name ?? "") &&
            UTextOf("bake-item-size") == ui.L10n.Format("bake.item.size", BakeUi.Size(status.Bytes)) &&
            UTextOf("bake-item-members") == ui.L10n.Format("bake.item.members", status.Members.Count),
            UTextOf("bake-item-kind") + " | " + UTextOf("bake-item-source") + " | " + UTextOf("bake-item-size") + " | " + UTextOf("bake-item-members"));
        int toasts = ui.Toasts.History.Count;
        yield return UClick("bake-item-keep");
        Check("«Keep in the store» of a bake the store holds: a note with its number, nothing new", ui.Toasts.History.Count > toasts && ui.Toasts.History.Last() == "Info: " + ui.L10n.Format("bake.keptAlready", row?.Number ?? -1) &&
            (UUi.Ops.BakeList().Value as BakeListResult).Rows.Count(r => r.Id == row?.Id) == 1, ui.Toasts.History.LastOrDefault());
        yield return UClick("bake-item-open");
        yield return PFrames(3);
        Check("«Open in panel» shows the panel", BakeUiModule.PanelShown(ui) && UBp != null);
        item.Fold.value = false;
        ui.Shell.ResetLayout();
        // a marker is an item with no bake: no section
        yield return PCall(UApp, new { cmd = "cut.marker", kind = "plane" });
        string mk = Key(pLast) ?? (string)pLast?["key"];
        ui.Ops.Select(new[] { mk });
        ui.Shell.FocusPanel("inspector");
        yield return PFrames(4);
        Check("a marker's inspector has no section «Bake»", mk != null && UPanel.visualTree.Q<BakeItemSection>() == null, mk);
    }

    // ------------------------------------------------------------ BY4p: the panel «Chara to Item»

    static IEnumerator Play_U_3_BakePanel()
    {
        if (!POpen("BY4p", "the panel «Chara to Item»")) yield break;
        var ok = new bool[1];
        yield return ULoad(ok);
        if (!ok[0]) yield break;
        var ui = UUi;
        var p = BakeUiModule.Show(ui);
        yield return PFrames(4);
        if (!Check("the panel", p != null && UVisible(p))) yield break;
        ui.Ops.Select(new[] { uA });
        yield return UClick("bake-use-selected");
        yield return PFrames(3);
        Check("«Use selected» takes the selected character", p.Targets.Count == 1 && p.Targets[0] == uA && UTextOf("bake-target") == ui.L10n.Format("bake.target", ui.Model.Find(uA).Label), UTextOf("bake-target"));
        var parts = UUi.Ops.BakeParts(uA).Value as List<BakePartRow>;
        int toggles = parts?.Count(x => UQ("bake-part-" + x.Part) is Toggle) ?? 0;
        Check("Present: a switch per part of the character", parts != null && parts.Count >= 5 && toggles == parts.Count, toggles + " of " + parts?.Count);
        yield return UClick("bake-preset-clothes");
        var clothes = parts.Where(x => x.Part.StartsWith("clothes", StringComparison.Ordinal)).Select(x => x.Part).ToList();
        Check("«Clothes» chooses the garments", p.Chosen.Count == clothes.Count && clothes.All(p.Chosen.Contains) && clothes.All(k => ((Toggle)UQ("bake-part-" + k)).value), string.Join(",", p.Chosen));
        yield return UClick("bake-preset-body");
        Check("«Body» chooses the body, the head and the hair", p.Chosen.Contains("body") && p.Chosen.Contains("head") && !p.Chosen.Any(k => k.StartsWith("clothes", StringComparison.Ordinal)), string.Join(",", p.Chosen));
        yield return UClick("bake-preset-whole");
        Check("«Whole» chooses nothing: the whole character", p.Chosen.Count == 0);
        string shoes = clothes.Contains("clothes7") ? "clothes7" : clothes.LastOrDefault();
        if (!Check("Present: a garment to bake", shoes != null)) yield break;
        var sw7 = UQ("bake-part-" + shoes) as Toggle;
        bool was = sw7 != null && sw7.value;
        yield return UClick("bake-part-" + shoes);
        Info("the switch " + shoes + ": " + was + " before the click, " + (sw7 != null && sw7.value) + " after; attached " + (sw7?.panel != null) + "; the same element now " + ReferenceEquals(sw7, UQ("bake-part-" + shoes)));
        Check("a part's switch chooses it", p.Chosen.Count == 1 && p.Chosen.Contains(shoes), string.Join(",", p.Chosen));
        // the options: no hiding, a name
        var hide = (Toggle)UQ("bake-hide");
        if (hide.value) yield return UClick("bake-hide");
        Check("«Hide the character» off", !hide.value);
        var name = (TextField)UQ("bake-name");
        yield return UReveal(name);
        name.Focus();
        yield return PFrames(3);
        yield return UIn(UKey("A", "ctrl"), UText("U shoes"));
        Check("the name typed", name.value == "U shoes", name.value);
        int u = UUndos;
        yield return UClickNow("bake-make");
        Check("«Bake»: the progress line says it captures", UTextOf("bake-progress") == ui.L10n.Get("bake.capturing"), UTextOf("bake-progress"));
        yield return UWait(() => !p.Busy && UUndos > u, 120, ok);
        yield return PFrames(3);
        var made = ui.Model.PreOrder().Where(x => x.Kind == NodeKind.Item && x.State is ItemNodeState s && s.Bake != null).ToList();
        Check("one entry «Bake to item», one item named as typed", ok[0] && UUndos == u + 1 && UTop == OpLabels.BakeMake && made.Count == 1 && made[0].Label == "U shoes", (UUndos - u) + " " + UTop + " " + string.Join(",", made.Select(x => x.Label)));
        Check("the progress line says what was made", UTextOf("bake-progress") == ui.L10n.Format("bake.made", 1), UTextOf("bake-progress"));
        Check("the name is used once: the field is clear", name.value == "", name.value);
        Check("the character stays shown", ui.Model.Find(uA)?.Doc.Visible == true);
        if (made.Count == 1)
        {
            var st = UUi.Ops.BakeStatus(made[0].Id).Value as BakeStatusInfo;
            Check("the bake holds the chosen part", st != null && st.Kind == BakeKinds.Parts && st.Parts != null && st.Parts.SequenceEqual(new[] { shoes }), st?.Kind + " " + string.Join(",", st?.Parts ?? new List<string>()));
        }
        yield return PFrames(8);
        Check("the store's list shows the new row", (UUi.Ops.BakeList().Value as BakeListResult).Rows.Any(r => r.Name == "U shoes") && p.StoreRows.Any(r => r.Name == "U shoes"));
        yield return PCall(UApp, new { cmd = "undo" });
        yield return PFrames(3);
        Check("undo takes the item back; the store keeps the row", !ui.Model.PreOrder().Any(x => x.Label == "U shoes") && (UUi.Ops.BakeList().Value as BakeListResult).Rows.Any(r => r.Name == "U shoes"));
        if (!hide.value) yield return UClick("bake-hide");
        ui.Shell.ResetLayout();
    }

    // ------------------------------------------------------------ BY4c: the panel «Cutter»

    static IEnumerator Play_U_4_CutPanel()
    {
        if (!POpen("BY4c", "the panel «Cutter»")) yield break;
        var ok = new bool[1];
        yield return ULoad(ok);
        if (!ok[0]) yield break;
        var ui = UUi;
        // a garment's bake as the target (made through the operation: the panel's own bake is BY4p's)
        var r = ui.ShowResult(ui.Ops.BakeMake(new[] { uA }, new BakeMakeOptions { Parts = new[] { "clothes1" }, Hide = false, Store = false, Name = "U cut source" }, UiContext.User(OpLabels.BakeMake)));
        string src = (r.Value as BakeMakeResult)?.Items.FirstOrDefault();
        if (!Check("Present: a bake item to cut", r.Ok && src != null, Short(r.Message))) yield break;
        var p = CutUiModule.Show(ui);
        yield return PFrames(4);
        if (!Check("the panel", p != null && UVisible(p))) yield break;
        ui.Ops.Select(new[] { src });
        yield return UClick("cut-use-selected");
        Check("«Use selected» takes the item", p.Target == src && UTextOf("cut-target") == ui.L10n.Format("cut.target", ui.Model.Find(src).Label), UTextOf("cut-target"));
        int u = UUndos;
        yield return UClick("cut-add-plane");
        yield return PFrames(3);
        string marker = p.Marker;
        Check("«Add plane»: a marker at the target, selected, one entry «Add item»", marker != null && CutMarkers.KindOf(ui.Model.Find(marker)?.State as ItemNodeState) == CutKind.Plane && ui.Selection.Primary == marker &&
            UUndos == u + 1 && UTop == OpLabels.AddItem, marker + " " + (UUndos - u) + " " + UTop);
        Check("the marker's modes: Both sides, Front, Back", UQ("cut-mode-both") != null && UQ("cut-mode-front") != null && UQ("cut-mode-back") != null && UQ("cut-mode-inside") == null);
        yield return UClick("cut-mode-front");
        yield return UClick("cut-section-bone");
        Check("the panel takes the mode and the colour", p.RequestNow().Mode == "front" && Mathf.Approximately(p.RequestNow().Section.Color.X, 0.89f), p.RequestNow().Mode);
        var drop = (DropdownField)UQ("cut-marker");
        Check("the scene's markers in the drop-down, the chosen one shown", drop.choices.Contains(marker) && drop.value == marker, string.Join(",", drop.choices));
        u = UUndos;
        int stepFrames0 = Time.frameCount;
        yield return UClick("cut-make");
        yield return UWait(() => !p.Cutting && UUndos > u, 120, ok);
        yield return PFrames(3);
        var pieces = ui.Model.PreOrder().Where(x => x.State is ItemNodeState s && s.Cut != null).ToList();
        Check("«Cut»: one entry «Cut», one piece (front), the target hidden", ok[0] && UUndos == u + 1 && UTop == OpLabels.CutMake && pieces.Count == 1 && ui.Model.Find(src)?.Doc.Visible == false,
            (UUndos - u) + " " + UTop + " " + pieces.Count);
        Check("one mesh per frame: the cut stepped over at least as many frames as meshes", p.Stepped >= 1 && p.SteppedFrames >= p.Stepped, "Present: " + p.Stepped + " meshes in " + p.SteppedFrames + " frames");
        Check("the progress line says what was made", UTextOf("cut-progress") == ui.L10n.Format("cut.made", 1), UTextOf("cut-progress"));
        if (pieces.Count != 1) yield break;
        string piece = pieces[0].Id;
        var cs = ui.Ops.CutStatus(piece).Value as CutStatusInfo;
        Check("the piece's procedure: the plane, front, the bone colour", cs != null && cs.Steps.Count == 1 && cs.Steps[0].Shape == "plane" && cs.Steps[0].Keep == "front" && Mathf.Approximately(cs.Section.Color.X, 0.89f));

        // «Apply to piece»: the piece as target, another colour
        ui.Ops.Select(new[] { piece });
        yield return PFrames(2);
        yield return UClick("cut-use-selected");
        Check("«Apply to piece» shows for a piece", p.Target == piece && UVisible(UQ("cut-apply")));
        yield return UClick("cut-section-grey");
        u = UUndos;
        yield return UClick("cut-apply");
        yield return UWait(() => !p.Cutting && UUndos > u, 120, ok);
        yield return PFrames(3);
        cs = ui.Ops.CutStatus(piece).Value as CutStatusInfo;
        Check("«Apply to piece»: one entry «Edit cut», the new colour", ok[0] && UUndos == u + 1 && UTop == OpLabels.CutEdit && cs != null && Mathf.Approximately(cs.Section.Color.X, 0.45f), (UUndos - u) + " " + UTop);
        Check("the progress line says it applied", UTextOf("cut-progress") == ui.L10n.Get("cut.applied"), UTextOf("cut-progress"));

        // the inspector's section of a piece: «Edit cut», «Re-cut from source»
        yield return UClick("cut-section-darkRed");
        ui.Ops.Select(new[] { piece });
        ui.Shell.FocusPanel("inspector");
        yield return PFrames(4);
        var item = UPanel.visualTree.Q<BakeItemSection>();
        if (!Check("the piece's section «Bake», with its cut", item != null && item.Piece)) yield break;
        item.Fold.value = true;
        yield return PFrames(4);
        Check("the piece's lines: kind piece, one step", UTextOf("bake-item-kind") == ui.L10n.Format("bake.item.kind", ui.L10n.Get("bake.kind.piece")) && UTextOf("bake-item-steps") == ui.L10n.Format("bake.item.steps", 1),
            UTextOf("bake-item-kind") + " | " + UTextOf("bake-item-steps"));
        u = UUndos;
        int markers = ui.Ops.CutMarkerNodes().Count;
        yield return UClick("bake-item-edit-cut");
        yield return PFrames(4);
        p = UCp;
        Check("«Edit cut»: the Cutter on the piece, a marker at its last step selected, one entry «Add item»", p != null && CutUiModule.PanelShown(ui) && p.Target == piece && ui.Ops.CutMarkerNodes().Count == markers + 1 &&
            p.Marker == ui.Selection.Primary && UUndos == u + 1 && UTop == OpLabels.AddItem, p?.Target + " " + p?.Marker + " " + (UUndos - u));
        Check("«Edit cut» takes the piece's values: its colour", p != null && Mathf.Approximately(p.RequestNow().Section.Color.X, 0.45f) && p.RequestNow().Mode == "front", p?.RequestNow().Mode);
        var stepAt = ui.Ops.CutStatus(piece).Value as CutStatusInfo;
        var m = URt.HolderOf(p.Marker);
        Info("the marker of «Edit cut» stands at " + (m ? m.position.ToString("F4") : "?"));
        ui.Ops.Select(new[] { piece });
        ui.Shell.FocusPanel("inspector");
        yield return PFrames(4);
        item = UPanel.visualTree.Q<BakeItemSection>();
        u = UUndos;
        int toasts = ui.Toasts.History.Count;
        yield return UClick("bake-item-recut");
        yield return PFrames(3);
        Check("«Re-cut from source» with nothing changed: the same result, no entry, a note", UUndos == u && ui.Toasts.History.Count > toasts && ui.Toasts.History.Last() == "Info: " + ui.L10n.Get("bake.item.recutSame"), ui.Toasts.History.LastOrDefault());
        if (item != null) item.Fold.value = false;
        ui.Shell.ResetLayout();
    }

    // ------------------------------------------------------------ BY5: the store's list

    static IEnumerator Play_U_5_Store()
    {
        if (!POpen("BY5", "the store's list")) yield break;
        var ok = new bool[1];
        yield return ULoad(ok);
        if (!ok[0]) yield break;
        var ui = UUi;
        var store = ui.Ops.OpenBakeStore();
        if (!Check("the run's own store", store != null && store.Folder.Replace('\\', '/').Contains(PlayUserData), store?.Folder)) yield break;
        // rows of the test's own: synthetic bakes, one with a blob file taken away
        var nos = new List<int>();
        foreach (var (nm, c) in new[] { ("by5 one", new Color32(200, 30, 30, 255)), ("by5 two", new Color32(30, 200, 30, 255)), ("by5 three", new Color32(30, 30, 200, 255)), ("by5 damaged", new Color32(90, 90, 90, 255)) })
        {
            var job = SyntheticJob(nm, c);
            nos.Add(store.Add(job.Row(nm), job.AllBlobs()).Number);
            job.Dispose();
        }
        var damagedRow = store.Find(nos[3]);
        string lost = damagedRow.Data.FirstOrDefault(sha => !store.AllRows().Any(x => x.Number != nos[3] && x.Blobs().Contains(sha)));
        if (lost != null) File.Delete(Path.Combine(store.BlobsPath, lost + ".bin"));
        AppItemRows.RefreshBakes(ui.App.Db);
        var p = BakeUiModule.Show(ui);
        yield return PFrames(6);
        if (!Check("the panel", p != null && UVisible(p))) yield break;
        p.MarkDirty();
        yield return UWait(() => p.CheckedCount >= p.StoreRows.Count && p.StoreRows.Count >= 4, 30, ok);
        yield return PFrames(4);
        var live = (ui.Ops.BakeList().Value as BakeListResult).Rows;
        Check("Present: the list shows the store's live rows", p.StoreRows.Count == live.Count && live.Count >= 4 && nos.All(n => UQ("bake-row-" + n) != null), p.StoreRows.Count + " / " + live.Count);
        Check("Present: a blob of a row was taken away", lost != null && !File.Exists(Path.Combine(store.BlobsPath, lost + ".bin")));
        Check("that row shows \"damaged\", the others not", ok[0] && UTextOf("bake-row-" + nos[3] + "-name").Contains(ui.L10n.Get("bake.damaged")) && !UTextOf("bake-row-" + nos[0] + "-name").Contains(ui.L10n.Get("bake.damaged")) && p.DamagedCount == 1,
            UTextOf("bake-row-" + nos[3] + "-name") + " / damaged " + p.DamagedCount + " of " + p.CheckedCount);

        // rename in place: Enter confirms
        int u = UUndos;
        yield return UClick("bake-row-" + nos[0] + "-rename");
        yield return PFrames(3);
        var edit = UQ("bake-row-" + nos[0] + "-edit") as TextField;
        Check("«Rename»: a field in the row with the name, focused", edit != null && edit.value == "by5 one" && p.Renaming == nos[0] && edit.focusController?.focusedElement != null, edit?.value);
        yield return UIn(UKey("A", "ctrl"), UText("by5 renamed"), UKey("Return"));
        yield return PFrames(4);
        Check("Enter confirms: the store's row takes the name, the list shows it", store.Find(nos[0])?.Name == "by5 renamed" && p.Renaming == -1 && UTextOf("bake-row-" + nos[0] + "-name").Contains("by5 renamed"), store.Find(nos[0])?.Name);
        Check("no item had the old name: no entry", UUndos == u, (UUndos - u).ToString());
        // Esc cancels
        yield return UClick("bake-row-" + nos[1] + "-rename");
        yield return PFrames(3);
        yield return UIn(UKey("A", "ctrl"), UText("not this"), UKey("Escape"));
        yield return PFrames(4);
        Check("Esc cancels: the name stays, the field goes", store.Find(nos[1])?.Name == "by5 two" && p.Renaming == -1 && UQ("bake-row-" + nos[1] + "-edit") == null, store.Find(nos[1])?.Name);
        // an item with the old name: one entry «Rename bake», undo renames the item back, the store keeps the name
        yield return UClick("bake-row-" + nos[2] + "-add");
        yield return PFrames(4);
        var added = ui.Model.PreOrder().FirstOrDefault(x => x.State is ItemNodeState s && s.Bake?.No == nos[2]);
        Check("«Add»: the row as an item, one entry", added != null && UTop == OpLabels.AddItem, added?.Label);
        // an item added from the list has no name of its own: its label is the row's (spec 7.4 renames the items whose own name is the old one); it is given that name
        Info("the added item's own name: " + (added?.Doc.Name ?? "none") + ", its label " + added?.Label);
        if (added != null) ui.ShowResult(ui.Ops.Rename(added.Id, "by5 three", UiContext.User()));
        Check("Present: a node has the old name as its own", added != null && ui.Model.Find(added.Id)?.Doc.Name == "by5 three", added?.Doc.Name);
        u = UUndos;
        yield return UClick("bake-row-" + nos[2] + "-rename");
        yield return PFrames(3);
        edit = UQ("bake-row-" + nos[2] + "-edit") as TextField;
        var focused = UHost.Router.Focused;
        Check("«Rename» of the added row: its field, focused", edit != null && p.Renaming == nos[2] && focused != null && (focused == edit || focused.GetFirstAncestorOfType<TextField>() == edit),
            (edit != null) + " renaming " + p.Renaming + " focused " + (focused == null ? "null" : focused.GetType().Name + " " + focused.name));
        yield return UIn(UKey("A", "ctrl"), UText("by5 third"), UKey("Return"));
        yield return PFrames(4);
        Check("the item with the old name takes the new one, one entry «Rename bake»", added != null && ui.Model.Find(added.Id)?.Label == "by5 third" && UUndos == u + 1 && UTop == OpLabels.BakeRename,
            (UUndos - u) + " " + UTop + "; the store's name " + store.Find(nos[2])?.Name + ", the item's " + (added != null ? ui.Model.Find(added.Id)?.Label : "none"));
        yield return PCall(UApp, new { cmd = "undo" });
        yield return PFrames(3);
        Check("undo renames the item back; the store keeps the new name", added != null && ui.Model.Find(added.Id)?.Label == "by5 three" && store.Find(nos[2])?.Name == "by5 third");

        // delete: the first press asks, the second deletes
        yield return UClick("bake-row-" + nos[1] + "-delete");
        yield return PFrames(3);
        Check("the first «Delete» asks: the row stays, its button asks", store.Find(nos[1])?.Deleted == false && p.Asking == nos[1] && UTextOf("bake-row-" + nos[1] + "-delete") == ui.L10n.Get("bake.deleteAsk"), UTextOf("bake-row-" + nos[1] + "-delete"));
        yield return UClick("bake-row-" + nos[1] + "-delete");
        yield return PFrames(4);
        Check("the second deletes: the row is marked, the list leaves it out, no file touched", store.Find(nos[1])?.Deleted == true && UQ("bake-row-" + nos[1]) == null && store.Find(nos[1]).Blobs().All(sha => File.Exists(Path.Combine(store.BlobsPath, sha + ".bin"))));
        yield return UClick("bake-show-deleted");
        yield return PFrames(4);
        Check("«Show deleted» lists it with «Restore»", UQ("bake-row-" + nos[1] + "-restore") != null && UTextOf("bake-row-" + nos[1] + "-name").Contains(ui.L10n.Get("bake.deletedMark")));
        yield return UClick("bake-row-" + nos[1] + "-restore");
        yield return PFrames(4);
        Check("«Restore» clears the mark", store.Find(nos[1])?.Deleted == false && UQ("bake-row-" + nos[1] + "-delete") != null);
        yield return UClick("bake-row-" + nos[1] + "-delete");
        yield return UClick("bake-row-" + nos[1] + "-delete");
        yield return PFrames(3);
        Check("deleted again for the purge", store.Find(nos[1])?.Deleted == true);

        // the purge dialog lists what it removes; Cancel removes nothing; «Purge» removes it
        var preview = ui.Ops.BakePurgePreview().Value as BakePurgeResult;
        yield return UClick("bake-purge");
        yield return PFrames(4);
        var dlg = ui.Dialogs?.Current;
        var listed = dlg?.Element?.Query<Label>().Where(l => l.name != null && l.name.StartsWith("bake-purge-row-", StringComparison.Ordinal)).ToList().Select(l => l.name).ToList() ?? new List<string>();
        var gone = store.AllRows().Where(x => x.Deleted).Select(x => "bake-purge-row-" + x.Number).ToList();
        Check("«Purge…» opens a dialog that lists the deleted rows it removes", dlg != null && dlg.Spec.Id == "bake-purge" && listed.Count == gone.Count && gone.All(listed.Contains), string.Join(",", listed));
        Check("Present: the purge would remove at least one row and one file", preview != null && preview.Rows >= 1 && preview.Blobs >= 1, preview?.Rows + " rows, " + preview?.Blobs + " files");
        Check("its message says how many and how much", dlg?.Spec.Message == ui.L10n.Format("bake.purgeWhat", preview.Rows, preview.Blobs + preview.Leftovers, BakeUi.Size(preview.Bytes)), dlg?.Spec.Message);
        yield return UClick("dialog-button-cancel");
        yield return PFrames(3);
        Check("Cancel removes nothing", ui.Dialogs.Current == null && store.Find(nos[1])?.Deleted == true);
        yield return UClick("bake-purge");
        yield return PFrames(3);
        yield return UClick("dialog-button-purge");
        yield return PFrames(4);
        Check("«Purge» removes the deleted rows and the files no row lists", store.Find(nos[1]) == null && store.AllRows().All(x => !x.Deleted) && UTextOf("bake-store-note") == ui.L10n.Format("bake.purged", preview.Rows, preview.Blobs + preview.Leftovers, BakeUi.Size(preview.Bytes)),
            UTextOf("bake-store-note"));
        yield return UClick("bake-show-deleted");

        // «Import old bakes…» from the test's folder of synthetic old bakes (never anyone's own folder)
        string synth = Path.Combine(ContentSettings.ProjectRoot, "Tests", "Bake", "old");
        var field = (TextField)UQ("bake-import-folder");
        Check("the folder field starts at the old bakes' default folder", field.value == (OldBakeImport.DefaultFolder(ui.App.Paths?.Hs2Root) ?? ""), field.value);
        field.value = synth;
        yield return PFrames(2);
        if (!Check("the field holds the test's folder before «Import»", field.value == synth && Directory.Exists(synth), field.value)) yield break;
        int before = store.AllRows().Count;
        yield return UClick("bake-import");
        yield return UWait(() => !p.Busy, 120, ok);
        yield return PFrames(4);
        int got = store.AllRows().Count - before;
        Check("«Import old bakes…»: the folder's bakes become rows, the note says so", got >= 1 && UTextOf("bake-store-note") == ui.L10n.Format("bake.imported", got, 0), "Present: " + got + " imported; " + UTextOf("bake-store-note"));
        yield return UClick("bake-import");
        yield return UWait(() => !p.Busy, 120, ok);
        yield return PFrames(4);
        Check("a second import adds nothing", store.AllRows().Count - before == got && UTextOf("bake-store-note") == ui.L10n.Format("bake.imported", 0, got), UTextOf("bake-store-note"));
        field.value = OldBakeImport.DefaultFolder(ui.App.Paths?.Hs2Root) ?? "";
        yield return UListDefinesGroup(p, store, nos[0]);
        ui.Shell.ResetLayout();
    }

    /// <summary>
    /// BY5, spec 9.7 (the lead's ruling after U's hand-back): a list that defines 731406 (a group of that number in the item group space, as a list file puts it,
    /// the method of BR10's fixture): the app's group is not there, and «Add» of a store row still adds one item that builds from its blobs, in one entry. The
    /// app's group comes back at the end.
    /// </summary>
    static IEnumerator UListDefinesGroup(BakePanel p, BakeStore store, int no)
    {
        var ui = UUi;
        var db = ui.App.Db;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var space = typeof(StudioDb).GetField("itemSpace", flags).GetValue(db);
        var groups = (Dictionary<int, StudioGroup>)space.GetType().GetField("Groups").GetValue(space);
        bool removed = AppItemRows.Remove(db);
        bool inserted = false;
        try
        {
            if (!groups.ContainsKey(AppItemRows.Group))
            {
                groups[AppItemRows.Group] = new StudioGroup { Kind = StudioListKind.ItemGroup, Number = AppItemRows.Group, Name = "A list's group" };
                inserted = true;
            }
            bool absent = !db.Generated.Holds(AppItemRows.Group) && db.TryGetItemGroup(AppItemRows.Group, out var lg) && lg.Name == "A list's group";
            Check("Present: with a list that defines 731406 the app's group is absent", removed && absent,
                "the app's group held " + db.Generated.Holds(AppItemRows.Group) + ", the list's group " + (db.TryGetItemGroup(AppItemRows.Group, out var g2) ? g2.Name : "none") + ", taken off " + removed);
            p.MarkDirty();
            yield return PFrames(4);
            var row = store.Find(no);
            int items = ui.Model.PreOrder().Count(x => x.State is ItemNodeState s && s.Bake?.No == no);
            int u = UUndos;
            yield return UClick("bake-row-" + no + "-add");
            yield return PFrames(4);
            var mine = ui.Model.PreOrder().Where(x => x.State is ItemNodeState s && s.Bake?.No == no).ToList();
            var node = mine.LastOrDefault();
            var it = node != null ? URt.FindItem(node.Id) : null;
            var st = node?.State as ItemNodeState;
            bool blobs = st?.Bake != null && st.Bake.Blobs().All(n => URt.Session.Model.File.Res.Contains(n));
            Check("«Add» of a store row adds one item that builds from its blobs (source bake, not the fallback), one entry «Add item»",
                row != null && mine.Count == items + 1 && it != null && it.State == RealizationState.Built && it.Source == "bake" && !it.IsFallback && blobs && st.Ref == AppItemRows.BakeGuid + "#" + no &&
                st.Group == AppItemRows.Group && st.Category == AppItemRows.BakesCategory && st.Bake.Id == row.Id && UUndos == u + 1 && UTop == OpLabels.AddItem,
                (mine.Count - items) + " items, source " + it?.Source + ", fallback " + it?.IsFallback + ", blobs in the scene " + blobs + ", ref " + st?.Ref + ", " + (UUndos - u) + " " + UTop);
        }
        finally
        {
            if (inserted) groups.Remove(AppItemRows.Group);
            if (removed) AppItemRows.Register(db, ui.App.Paths?.UserData);
        }
        Check("the app's group is back", db.Generated.Holds(AppItemRows.Group));
    }

    // ------------------------------------------------------------ BI1: the tool

    /// <summary>The surface under a panel point with the markers left out (as the tool picks).</summary>
    static ToolPick USurface(Vector2 at)
    {
        using (CutMarkers.HideForCapture()) return UTools.Pick(UVp.RayAt(at));
    }

    /// <summary>The angle between two directions in degrees, in double.</summary>
    static double UDeg(Vector3 a, Vector3 b) => AngleRad(a, b) * 180.0 / Math.PI;

    /// <summary>The view on BR-Chan's chest, and a corner of the view where no surface is.</summary>
    static IEnumerator UChestView()
    {
        var chest = URt.FindChara(uA).Context.BodyBones["cf_J_Spine02"].position;
        UVp.Camera.SetView(chest, new Vector3(5, 180, 0), 30, 23);
        UVp.Camera.Flush();
        yield return PFrames(3);
        var wb = UVp.Element.worldBound;
        bool found = false;
        foreach (var p in new[] { new Vector2(wb.xMin + 24, wb.yMin + 24), new Vector2(wb.xMax - 24, wb.yMin + 24), new Vector2(wb.xMin + 24, wb.yMax - 24), new Vector2(wb.xMax - 24, wb.yMax - 24) })
        {
            if (UPanel.Pick(p) != UVp.Element || USurface(p).Hit) continue;
            uEmpty = p;
            found = true;
            break;
        }
        Check("Present: a corner of the view where no surface is", found, uEmpty.ToString());
    }

    static IEnumerator Play_U_6_Tool()
    {
        if (!POpen("BI1", "the tool that places a marker on a surface")) yield break;
        var ok = new bool[1];
        yield return ULoad(ok);
        if (!ok[0]) yield break;
        var ui = UUi;
        var p = CutUiModule.Show(ui);
        yield return PFrames(4);
        ui.Ops.Select(new[] { uA });
        yield return UClick("cut-use-selected");
        yield return UClick("cut-add-tear");
        yield return PFrames(3);
        string tear = p.Marker;
        if (!Check("a tear marker, selected", tear != null && CutMarkers.KindOf(ui.Model.Find(tear)?.State as ItemNodeState) == CutKind.Tear && ui.Selection.Primary == tear)) yield break;
        yield return UClick("cut-place");
        var tool = p.Tool;
        Check("«Place on surface» begins the tool, its hint in the status line and the panel", tool != null && UTools.Current == tool && !UTools.Blocks && ui.Shell.StatusBar.Hint.text == ui.L10n.Get(CutTool.HintKey) && UVisible(UQ("cut-hint")),
            ui.Shell.StatusBar.Hint.text);
        if (tool == null) yield break;
        yield return UChestView();
        var chest = URt.FindChara(uA).Context.BodyBones["cf_J_Spine02"].position;
        var on = UOn(chest);
        var want = USurface(on);
        if (!Check("Present: a surface was hit, with a triangle", want.Hit && want.Precise && want.NodeId == uA, want.NodeId + " precise " + want.Precise)) yield break;
        var holder = URt.HolderOf(tear);
        var before = (holder.position, holder.rotation);
        string cam = UCam();
        int u = UUndos;
        var right = UVp.ViewCamera.transform.right;
        yield return UIn(UPt(on, "down"));
        Check("a press on the surface is claimed", UTools.PressHeld && tool.Moving == tear, UTools.PressHeld + " " + tool.Moving);
        double dist = (holder.position - want.Point).magnitude;
        double up = UDeg(holder.up, want.Normal);
        var flat = right - Vector3.Dot(right, want.Normal.normalized) * want.Normal.normalized;
        double x = UDeg(holder.right, flat);
        Check("the marker stands at the hit", dist <= 1e-3, dist.ToString("G4"));
        Check("its Y along the normal within 1 degree", up <= 1.0, up.ToString("G4") + " deg");
        Check("its X along the view's right laid on the surface within 1 degree", x <= 1.0, x.ToString("G4") + " deg");
        Check("the camera stays during the press", UCam() == cam);

        // the marker follows the pointer over the surface
        var on2 = on + new Vector2(18, 26);
        var want2 = USurface(on2);
        yield return UIn(UPt(on2, "move"));
        double d2 = (holder.position - want2.Point).magnitude;
        Check("a move: the marker follows to the surface under the pointer", want2.Hit && d2 <= 1e-3 && (want2.Point - want.Point).magnitude > 0.01f, d2.ToString("G4") + ", moved " + (want2.Point - want.Point).magnitude.ToString("G4"));

        // the wheel: 15 degrees a notch about the normal
        var r0 = holder.right;
        var n0 = holder.up;
        yield return UIn(UWheel(on2, -3));
        double turned = UDeg(holder.right, r0), tilt = UDeg(holder.up, n0);
        Check("a notch turns it 15 degrees about the normal", Math.Abs(turned - 15) <= 0.01 && tilt <= 1e-3, turned.ToString("G6") + " deg, the normal moved " + tilt.ToString("G4"));
        Check("the wheel is claimed: the camera stays", UCam() == cam);
        yield return UIn(UWheel(on2, 6));
        turned = UDeg(holder.right, r0);
        Check("two notches the other way: 15 degrees to the other side", Math.Abs(turned - 15) <= 0.01 && Math.Abs(tool.Twist + 15) <= 1e-4, turned.ToString("G6") + " deg, twist " + tool.Twist);
        Check("nothing is recorded during the press", UUndos == u);
        yield return UIn(UPt(on2, "up"));
        yield return PFrames(2);
        Check("the release: one entry «Move marker»", UUndos == u + 1 && UTop == OpLabels.CutMoveMarker && !UTools.PressHeld && tool.Moving == null, (UUndos - u) + " " + UTop);
        var moved = (holder.position, holder.rotation);
        yield return PCall(UApp, new { cmd = "undo" });
        yield return PFrames(2);
        holder = URt.HolderOf(tear);
        Check("undo puts the marker back", (holder.position - before.position).magnitude <= 1e-4 && AngleDeg(holder.rotation, before.rotation) <= 1e-3, (holder.position - before.position).magnitude.ToString("G4"));
        yield return PCall(UApp, new { cmd = "redo" });
        yield return PFrames(2);
        holder = URt.HolderOf(tear);
        Check("redo moves it again", (holder.position - moved.position).magnitude <= 1e-4 && AngleDeg(holder.rotation, moved.rotation) <= 1e-3);

        // a press on the marker finds the surface behind it: the marker lifted off the surface first, so that an ordinary pick meets it
        ui.ShowResult(ui.Ops.SetTransform(new[] { tear }, holder.position + holder.up * 0.3f, null, null, UiContext.User()));
        yield return PFrames(2);
        holder = URt.HolderOf(tear);
        var onMarker = UOn(holder.position);
        var plain = UTools.Pick(UVp.RayAt(onMarker));
        var behind = USurface(onMarker);
        Check("Present: an ordinary pick there hits the marker", plain.Hit && plain.NodeId == tear, plain.NodeId);
        u = UUndos;
        yield return UIn(UPt(onMarker, "down"));
        Check("a press on the marker is claimed and puts it on the surface behind", UTools.PressHeld && behind.Hit && behind.NodeId == uA && (URt.HolderOf(tear).position - behind.Point).magnitude <= 1e-3,
            behind.NodeId + " " + (URt.HolderOf(tear).position - behind.Point).magnitude.ToString("G4"));
        yield return UIn(UPt(onMarker, "up"));

        // Esc during a press puts it back, the tool goes on; Esc after that ends the tool
        holder = URt.HolderOf(tear);
        var kept = (holder.position, holder.rotation);
        u = UUndos;
        yield return UIn(UPt(on, "down"));
        Check("Present: the press moved it", (URt.HolderOf(tear).position - kept.position).magnitude > 0.01f);
        yield return UIn(UKey("Escape"));
        yield return PFrames(2);
        holder = URt.HolderOf(tear);
        Check("Esc during the press puts the marker back, no entry, the tool goes on", (holder.position - kept.position).magnitude <= 1e-4 && AngleDeg(holder.rotation, kept.rotation) <= 1e-3 && UUndos == u && !UTools.PressHeld && p.Tool == tool,
            (holder.position - kept.position).magnitude.ToString("G4") + " " + (UUndos - u));
        yield return UIn(UPt(on, "up"));

        // what is let through: a miss, a modifier, a plane marker
        u = UUndos;
        cam = UCam();
        yield return UIn(UPt(uEmpty, "down"), UPt(uEmpty + new Vector2(30, 12), "move"), UPt(uEmpty + new Vector2(60, 24), "move"), UPt(uEmpty + new Vector2(60, 24), "up"));
        Check("a press that hits no surface is let through: the camera orbits", UCam() != cam && !UTools.PressHeld && UUndos == u, UCam());
        yield return UChestView();
        on = UOn(chest);
        int taken = tool.Taken;
        ui.Ops.Select(new[] { tear });
        yield return PFrames(2);
        yield return UIn(UPt(on, "down", 0, "alt"), UPt(on, "up", 0, "alt"));
        Check("a press with a modifier is let through", tool.Taken == taken && !UTools.PressHeld);
        yield return PCall(UApp, new { cmd = "cut.marker", kind = "plane" });
        string plane = (string)pLast?["key"];
        ui.Ops.Select(new[] { plane });
        yield return PFrames(2);
        var ph = URt.HolderOf(plane);
        var pBefore = ph ? ph.position : Vector3.zero;
        u = UUndos;
        yield return UIn(UPt(on, "down"));
        bool claimed = UTools.PressHeld;
        yield return UIn(UPt(on, "up"));
        Check("with a plane marker selected the press is let through, the marker stays", plane != null && !claimed && tool.Taken == taken && ph && (ph.position - pBefore).magnitude == 0 && UUndos == u, plane);
        yield return UIn(UKey("Escape"));
        yield return PFrames(2);
        Check("Esc with no press ends the tool, «Place on surface» goes off", UTools.Current == null && p.Tool == null && !((Toggle)UQ("cut-place")).value && !UVisible(UQ("cut-hint")));
        ui.Shell.ResetLayout();
    }

    // ------------------------------------------------------------ BI2: markers in the view, not in a capture

    /// <summary>One render of the view camera into memory (480 x 270).</summary>
    static Color32[] URender()
    {
        var cam = UVp.ViewCamera;
        const int w = 480, h = 270;
        var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
        var prevT = cam.targetTexture;
        var prevR = cam.rect;
        UHost.GizmosIntoTargets = true;
        try
        {
            cam.targetTexture = rt;
            cam.rect = new Rect(0, 0, 1, 1);
            cam.Render();
            var prevA = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prevA;
            var px = tex.GetPixels32();
            UnityEngine.Object.DestroyImmediate(tex);
            return px;
        }
        finally
        {
            cam.targetTexture = prevT;
            cam.rect = prevR;
            UHost.GizmosIntoTargets = false;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    static int UDiffer(Color32[] a, Color32[] b)
    {
        int n = 0;
        for (int i = 0; i < a.Length; i++) if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b) n++;
        return n;
    }

    static IEnumerator Play_U_7_Markers()
    {
        if (!POpen("BI2", "markers in the view, not in a capture")) yield break;
        var ok = new bool[1];
        yield return ULoad(ok);
        if (!ok[0]) yield break;
        var ui = UUi;
        yield return PCall(UApp, new { cmd = "scene.pause", paused = true });
        yield return PCall(UApp, new { cmd = "chara.face", key = uA, blink = false });
        var hips = URt.FindChara(uA).Context.BodyBones["cf_J_Hips"].position;
        yield return PCall(UApp, new { cmd = "cut.marker", kind = "box", target = uA });
        string box = (string)pLast?["key"];
        ui.Ops.Select(Array.Empty<string>());
        UVp.Camera.SetView(hips, new Vector3(20, 160, 0), 30, 30);
        UVp.Camera.Flush();
        yield return PFrames(4);
        if (!Check("Present: a marker in the scene", box != null && URt.HolderOf(box))) yield break;
        URender();   // a first render of a scene may hold undefined pixels on a character: it is not compared
        var shown = URender();
        var shownAgain = URender();
        int same = UDiffer(shown, shownAgain);
        Check("two renders of the view agree (the base of the comparison)", same == 0, same + " px");
        var cam = UVp.ViewCamera;
        var cap1 = ViewCapture.Render(cam, 512, 288, ViewCapture.None);
        var full1 = HS2Next.Screenshots.CaptureService.RenderSync(URt, cam, new HS2Next.Screenshots.CaptureRequest { Width = 320, Height = 180, Aa = HS2Next.Screenshots.CaptureService.None });
        ui.ShowResult(ui.Ops.SetVisible(new[] { box }, false, UiContext.User()));
        yield return PFrames(3);
        var hidden = URender();
        var cap2 = ViewCapture.Render(cam, 512, 288, ViewCapture.None);
        var full2 = HS2Next.Screenshots.CaptureService.RenderSync(URt, cam, new HS2Next.Screenshots.CaptureRequest { Width = 320, Height = 180, Aa = HS2Next.Screenshots.CaptureService.None });
        int inView = UDiffer(shown, hidden);
        Measure("BI2 the marker in the view: " + inView + " px of 129,600");
        Check("the marker shows in the view", inView >= 500, "Present: " + inView + " px");
        Check("a capture of the view holds nothing of it: the same bytes with the marker shown and hidden", cap1 != null && cap2 != null && cap1.SequenceEqual(cap2), cap1?.Length + " / " + cap2?.Length);
        Check("the capture service's picture likewise", full1 != null && full1.Ok && full2 != null && full2.Ok && full1.Png.SequenceEqual(full2.Png), full1?.Png?.Length + " / " + full2?.Png?.Length);
        ui.ShowResult(ui.Ops.SetVisible(new[] { box }, true, UiContext.User()));
        yield return PCall(UApp, new { cmd = "chara.face", key = uA, blink = true });
        yield return PCall(UApp, new { cmd = "scene.pause", paused = false });
        yield return PCall(UApp, new { cmd = "scene.new" });
    }
}

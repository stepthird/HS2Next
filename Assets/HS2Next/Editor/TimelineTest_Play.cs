// The Timeline panel's checks of TimelineTest.play (TU1-TU11; Phase 6.2 package U), a part file of TimelineTest: one play part
// (Play_U_Panel) on the app with the UI, driven like a user through the bridge's ui.action / ui.input and judged by element
// state (names, classes, visibility, texts), the scene's history and the Timeline document (timeline.get) and status, never by
// pixels. Its own scene: BR-Chan (FK on with the Body and Neck groups), a vanilla item, a light.
//   TU1  the panel is not in the default layout (the bottom region collapsed, no panel shown there); timeline.panel opens the
//        bottom region with the panel shown (ui.state), again collapses it, a third time shows it; the toolbar toggle and the
//        View menu entry exist.
//   TU2  New Timeline: one history entry «Timeline», a Timeline of 10 s.
//   TU3  the add-track menu per selection (an item: position, rotation, scale, its colour slots; a character: the animation and
//        face types; a light: its colour; an FK bone handle: the bone; always camera and depth of field); a track added with
//        one key at the play head (one entry «Add track»); the entry of a channel that has a track is disabled; the command
//        refuses a second track on a channel.
//   TU4  Key (paused at 1 s after a model edit: the key stores the shown value, one entry «Key»); a double click on a lane
//        seeks there and keys the shown value.
//   TU5  a key drag is one entry «Move keys» (snapped to 0.01 s); Esc during a drag puts the keys back and records nothing.
//   TU6  the ease switch: Linear (segment) and Ease out (its menu), one entry «Key» each.
//   TU7  Delete in the panel deletes the selected key (one entry «Delete keys»); undo then redo of TU2-TU7 step by step give
//        timeline.get's document byte-equal to the one recorded at each history count.
//   TU8  play / pause / stop from the header; Stop and keep this pose: one entry «Bake Timeline pose», stopped.
//   TU9  scrubbing the ruler seeks (the play head follows, paused) and records nothing.
//   TU10 while playing: the inspector's position row of the tracked item shows the overridden mark (its tooltip names the
//        Timeline), the camera controller is disabled by the camera track with the viewport line, a gizmo drag on the item is
//        refused with the toast; paused: the controller works, the drag works and suspends the track; stopped: no mark.
//   TU11 every row of the strings block of the Timeline panel has en, ja and zh-Hans.
//   TU12 while frames are recorded (record.start, the global pause on), the toolbar's Step is disabled and the action time.step
//        refuses with a toast; the scene time does not move.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HS2Next.App;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Scenes.Timeline;
using HS2Next.UI;
using HS2Next.UI.Core;
using HS2Next.UI.Gizmos;
using HS2Next.UI.Panels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class TimelineTest
{
    static AppCore UApp => AppCore.Current;
    static UiContext UUi => UiHost.Current?.Context;
    static JObject ulast;
    static string ulastText;
    static bool UOk => ulast?.Value<bool?>("ok") == true;
    static string uA, uP, uL;
    /// <summary>timeline.get's document text at each history count (TU2-TU7), for the undo / redo walk of TU7.</summary>
    static readonly Dictionary<int, string> uSnap = new Dictionary<int, string>();
    static int uStart;

    // ------------------------------------------------------------ helpers

    /// <summary>One bridge request in process (the queued path, as a client); the reply in <see cref="ulast"/>.</summary>
    static IEnumerator UCall(object request, double timeoutSec = 60)
    {
        string json = request is string s ? s : JsonConvert.SerializeObject(request);
        ulast = null;
        ulastText = null;
        var t = UApp.Bridge.Execute(json);
        // the harness may tick more often than the player loop runs: wait player frames, bounded by real time
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!t.IsCompleted && sw.Elapsed.TotalSeconds < timeoutSec) yield return PlayModeHarness.WaitFrames(1);
        ulastText = t.IsCompleted && !t.IsFaulted ? t.Result : "{\"ok\":false,\"code\":\"timeout\"}";
        try { ulast = JObject.Parse(ulastText); }
        catch (Exception) { ulast = new JObject { ["ok"] = false }; }
    }

    static IEnumerator UOkCall(object request, string what)
    {
        yield return UCall(request);
        Check(what, UOk, UOk ? "" : Short(ulastText));
    }

    static IEnumerator UFrames(int n) => PlayModeHarness.WaitFrames(n);

    static VisualElement UQ(string name) => UiHost.Current?.Root?.panel?.visualTree?.Q(name);
    static bool UVisible(VisualElement e)
    {
        for (var x = e; x != null; x = x.hierarchy.parent)
            if (x.resolvedStyle.display == DisplayStyle.None || x.resolvedStyle.visibility == Visibility.Hidden) return false;
        return e != null && e.panel != null;
    }

    /// <summary>ui.input of one pointer event at a panel point (phase click / down / move / up).</summary>
    static IEnumerator UPointer(Vector2 p, string phase = "click", int clicks = 1, int button = 0, string[] mods = null)
    {
        var ev = new JObject { ["type"] = "pointer", ["x"] = p.x, ["y"] = p.y, ["phase"] = phase, ["button"] = button, ["clicks"] = clicks };
        if (mods != null) ev["mods"] = new JArray(mods);
        yield return UCall(new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(ev), ["frames"] = 1 });
    }

    /// <summary>ui.input: a click on the element named <paramref name="name"/> (its centre).</summary>
    static IEnumerator UClick(string name)
    {
        yield return UCall(new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(new JObject { ["type"] = "pointer", ["target"] = name }), ["frames"] = 1 });
        yield return UFrames(2);
    }

    static IEnumerator UKey(string key)
    {
        yield return UCall(new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(new JObject { ["type"] = "key", ["key"] = key }), ["frames"] = 1 });
        yield return UFrames(2);
    }

    static int UUndo => UUi.Session.Commands.UndoList.Count;
    static string UTop => UUi.Session.Commands.UndoList.Count > 0 ? UUi.Session.Commands.UndoList[0].Label : null;
    static TimelineDocument UDoc => UUi.Model.Timeline;
    static string UDocJson => UDoc?.ToJson() ?? "null";
    static void USnap() => uSnap[UUndo] = UDocJson;

    static TimelineTrack UTrack(Func<TimelineTrack, bool> f) => UDoc?.Tracks.FirstOrDefault(f);

    static IEnumerator UStatus()
    {
        yield return UCall(new { cmd = "timeline.status" });
    }

    static bool UMenuHas(string id) => UQ("menu-item-" + id) != null;
    static bool UMenuDisabled(string id) => UQ("menu-item-" + id)?.ClassListContains("hs-menu__item--disabled") ?? false;

    static IEnumerator UAddMenu()
    {
        UUi.Menus?.CloseAll();
        yield return UClick("timeline-add");
    }

    /// <summary>The panel point of track <paramref name="lane"/>'s lane at time <paramref name="t"/>.</summary>
    static Vector2 ULanePoint(int lane, double t)
    {
        var p = TimelinePanel.Current;
        var l = UQ("timeline-lane-" + lane);
        if (p == null || l == null) return Vector2.zero;
        var lanes = UQ("timeline-lanes");
        return new Vector2(lanes.worldBound.x + p.X(t), l.worldBound.center.y);
    }

    static Vector2 URulerPoint(double t)
    {
        var p = TimelinePanel.Current;
        var r = UQ("timeline-ruler");
        return p == null || r == null ? Vector2.zero : new Vector2(r.worldBound.x + p.X(t), r.worldBound.center.y);
    }

    // ------------------------------------------------------------ the part

    static IEnumerator Play_U_Panel()
    {
        var parts = new (string id, string title, Func<IEnumerator> run)[]
        {
            ("TU0", "setup: app, UI, scene", UPrepare),
            ("TU1", "timeline.panel and the default layout", TU1),
            ("TU2", "New Timeline", TU2),
            ("TU3", "the add-track menu", TU3),
            ("TU4", "Key and the lane double click", TU4),
            ("TU5", "a key drag and Esc", TU5),
            ("TU6", "the ease switch", TU6),
            ("TU7", "delete keys; undo / redo of TU2-TU7", TU7),
            ("TU8", "play, pause, stop, stop and keep this pose", TU8),
            ("TU9", "scrubbing", TU9),
            ("TU10", "the overridden mark, the camera controller, the gizmo rule", TU10),
            ("TU11", "the strings", TU11),
            ("TU12", "no Step while recording", TU12),
        };
        bool ready = true;
        foreach (var (id, title, run) in parts)
        {
            if (id != "TU0" && (!Want(id) || !ready)) continue;
            current = id;
            rep.AppendLine();
            rep.AppendLine($"== {id} {title}");
            int before = fails;
            var e = run();
            while (true)
            {
                object cur;
                try { if (!e.MoveNext()) break; cur = e.Current; }
                catch (Exception ex) { Check("exception", false, ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace); break; }
                yield return cur;
            }
            UUi?.Menus?.CloseAll();
            while (UUi?.Dialogs != null && UUi.Dialogs.IsModalOpen) UUi.Dialogs.CloseCurrent();
            if (id == "TU0" && fails > before) ready = false;
        }
    }

    static IEnumerator UPrepare()
    {
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 240, ok);
        Check("app ready", ok[0] && UApp?.Stage == AppStage.Ready, UApp?.Stage.ToString());
        yield return PlayModeHarness.WaitUntil(() => UiHost.Current != null && UiHost.Current.IsReady, 60, ok);
        if (!Check("UI ready", ok[0])) yield break;
        if (!Check("the bridge runs in process", UApp.Bridge != null)) yield break;
        yield return UOkCall(new { cmd = "scene.new" }, "scene.new");
        yield return UCall(new { cmd = "chara.load", path = femaleRel, sex = "female" });
        Check("chara.load BR-Chan", UOk, Short(ulastText));
        uA = ulast.Key();
        yield return UCall(new { cmd = "item.add", no = 399 });
        Check("item.add 399", UOk, Short(ulastText));
        uP = ulast.Key();
        yield return UCall(new { cmd = "light.add", no = 0, pos = new[] { 0, 12, 3 } });
        Check("light.add 0", UOk, Short(ulastText));
        uL = ulast.Key();
        yield return PlayModeHarness.WaitUntil(() => UApp.Runtime.FindChara(uA)?.State == RealizationState.Built, 120, ok);
        Check("BR-Chan built", ok[0]);
        yield return UOkCall(new { cmd = "fk.enable", key = uA, on = true }, "FK on");
        yield return UOkCall(new { cmd = "fk.enable", key = uA, groups = new[] { "Body", "Neck" }, on = true }, "FK groups Body and Neck");
        UUi.Shell.ResetLayout();
        yield return UFrames(3);
    }

    static IEnumerator TU1()
    {
        var ui = UUi;
        var sh = ui.Shell;
        var bottom = sh.Regions[LayoutRegions.Bottom];
        Check("default layout: the bottom region collapsed, no panel shown there, the Timeline panel not shown",
            bottom.Collapsed && bottom.Panels.All(p => p == TimelineUiModule.PanelId) && !TimelineUiModule.PanelShown(ui),
            "collapsed " + bottom.Collapsed + ", panels [" + string.Join(", ", bottom.Panels) + "]");
        Check("default layout: left = workspace | inspector", sh.Regions[LayoutRegions.Left].Panels.SequenceEqual(new[] { "workspace", "inspector" }), string.Join(", ", sh.Regions[LayoutRegions.Left].Panels));
        var tb = UQ(TimelineChrome.ToolbarName);
        Check("the toolbar toggle tb-timeline.panel stands after the scene time", tb != null && tb.parent?.IndexOf(tb) == tb.parent?.IndexOf(UQ("tb-scene-time")) + 1);
        yield return UClick("tb-view");
        Check("the View menu lists Timeline (menu-item-timeline.panel)", UQ("menu-item-" + ActionIds.TimelinePanel) != null);
        ui.Menus?.CloseAll();
        yield return UOkCall(new { cmd = "ui.action", action = ActionIds.TimelinePanel }, "ui.action timeline.panel (open)");
        yield return UFrames(3);
        yield return UCall(new { cmd = "ui.state" });
        var bs = ulast?["layout"]?["regions"]?["bottom"];
        var pe = (ulast?["panels"] as JArray)?.FirstOrDefault(x => (string)x["id"] == TimelineUiModule.PanelId);
        Check("ui.state: the bottom region open with the Timeline panel visible and active",
            bs != null && bs.Value<bool>("collapsed") == false && pe != null && (string)pe["region"] == "bottom" && pe.Value<bool>("visible") && pe.Value<bool>("active"),
            Short(bs?.ToString(Formatting.None)) + " / " + pe?.ToString(Formatting.None));
        Check("without a Timeline the panel shows New Timeline", UVisible(UQ("timeline-new")) && !UVisible(UQ("timeline-header")));
        Check("the toolbar toggle is lit", UQ(TimelineChrome.ToolbarName)?.ClassListContains("hs-toolbar__button--on") == true);
        yield return UOkCall(new { cmd = "ui.action", action = ActionIds.TimelinePanel }, "ui.action timeline.panel (collapse)");
        yield return UFrames(3);
        Check("timeline.panel again collapses the region", bottom.Collapsed && !TimelineUiModule.PanelShown(ui));
        yield return UOkCall(new { cmd = "ui.action", action = ActionIds.TimelinePanel }, "ui.action timeline.panel (open again)");
        yield return UFrames(3);
        Check("a third time shows it again", TimelineUiModule.PanelShown(ui) && TimelinePanel.Current != null);
    }

    static IEnumerator TU2()
    {
        uSnap.Clear();
        uStart = UUndo;
        USnap();
        int u0 = UUndo;
        yield return UClick("timeline-new");
        yield return UFrames(2);
        var d = UDoc;
        Check("New Timeline: a Timeline of 10 s", d != null && d.Duration == 10f && d.Tracks.Count == 0, UDocJson);
        Check("one history entry «Timeline»", UUndo == u0 + 1 && UTop == TimelineEditCommand.LabelTimeline, UUndo - u0 + " / " + UTop);
        Check("the header shows (New Timeline hidden)", UVisible(UQ("timeline-header")) && !UVisible(UQ("timeline-new")));
        USnap();
    }

    static IEnumerator TU3()
    {
        var ui = UUi;
        yield return UOkCall(new { cmd = "select", keys = new[] { uP } }, "select the item");
        yield return UFrames(2);
        yield return UAddMenu();
        Check("item: position, rotation, scale, colours 1-4, camera, depth of field; no character types",
            new[] { "pos", "rot", "scale", "color0", "color1", "color2", "color3", "camera", "dof" }.All(x => UMenuHas("timeline-add-" + x)) && !UMenuHas("timeline-add-anime") && !UMenuHas("timeline-add-lightColor"));
        Info("item colour slots disabled: " + string.Join(", ", new[] { 0, 1, 2, 3 }.Where(s => UMenuDisabled("timeline-add-color" + s))));
        int u0 = UUndo;
        yield return UClick("menu-item-timeline-add-pos");
        yield return UFrames(2);
        var tr = UTrack(t => t.Type == TimelineTypes.Pos && t.Key == uP);
        Check("Position added with one key at the play head (0)", tr != null && tr.Keys.Count == 1 && tr.Keys[0].T == 0f, UDocJson);
        Check("one entry «Add track»", UUndo == u0 + 1 && UTop == TimelineEditCommand.LabelAddTrack, UUndo - u0 + " / " + UTop);
        Check("the panel shows the track row and its lane", UQ("timeline-track-0") != null && UQ("timeline-lane-0") != null && UQ("timeline-key-0-0") != null);
        USnap();
        yield return UAddMenu();
        Check("the channel that has a track: its entry disabled with the tooltip", UMenuDisabled("timeline-add-pos") && (UQ("menu-item-timeline-add-pos")?.tooltip ?? "") == L10n.Get("timeline.channelHasTrack"));
        ui.Menus.CloseAll();
        yield return UCall(new { cmd = "timeline.track.add", type = "pos", key = uP });
        Check("timeline.track.add of the same channel answers bad_args", !UOk && (string)ulast?["code"] == "bad_args", Short(ulastText));

        yield return UOkCall(new { cmd = "select", keys = new[] { uA } }, "select the character");
        yield return UFrames(2);
        yield return UAddMenu();
        Check("character: + scale and the animation and face types",
            new[] { "pos", "rot", "scale", "anime", "animeSpeed", "animeTime", "eyes", "mouth", "eyesOpen", "mouthOpen" }.All(x => UMenuHas("timeline-add-" + x)) && !UMenuHas("timeline-add-color0"));
        ui.Menus.CloseAll();
        yield return UOkCall(new { cmd = "select", keys = new[] { uL } }, "select the light");
        yield return UFrames(2);
        yield return UAddMenu();
        Check("light: position, rotation, its colour; no scale", UMenuHas("timeline-add-lightColor") && UMenuHas("timeline-add-pos") && !UMenuHas("timeline-add-scale"));
        ui.Menus.CloseAll();
        yield return UOkCall(new { cmd = "select", sub = uA + "/fk/cf_J_Neck" }, "select BR-Chan's FK neck handle");
        yield return UFrames(2);
        yield return UAddMenu();
        Check("an FK bone handle: Bone rotation of that bone", UMenuHas("timeline-add-bone"));
        u0 = UUndo;
        yield return UClick("menu-item-timeline-add-bone");
        yield return UFrames(2);
        var bone = UTrack(t => t.Type == TimelineTypes.Rot && t.Key == uA && t.Bone == "cf_J_Neck");
        Check("Bone rotation added (one entry «Add track»)", bone != null && bone.Keys.Count == 1 && UUndo == u0 + 1 && UTop == TimelineEditCommand.LabelAddTrack, UDocJson);
        USnap();
        yield return UOkCall(new { cmd = "select", keys = new string[0] }, "select nothing");
        yield return UFrames(2);
        yield return UAddMenu();
        Check("nothing selected: camera and depth of field only", UMenuHas("timeline-add-camera") && UMenuHas("timeline-add-dof") && !UMenuHas("timeline-add-pos"));
        yield return UClick("menu-item-timeline-add-camera");
        yield return UFrames(2);
        Check("Camera added", UTrack(t => t.Type == TimelineTypes.Camera)?.Keys.Count == 1, UDocJson);
        USnap();
    }

    static IEnumerator TU4()
    {
        var panel = TimelinePanel.Current;
        if (!Check("the panel is built", panel != null)) yield break;
        int lane = UDoc.IndexOfTrack(UTrack(t => t.Type == TimelineTypes.Pos && t.Key == uP)?.Id);
        yield return UClick("timeline-track-" + lane + "-label");
        Check("a click on the row selects the track", panel.SelectedTracks.Count == 1 && panel.SelectedTracks[0] == UDoc.Tracks[lane].Id);
        yield return UOkCall(new { cmd = "timeline.seek", t = 1 }, "timeline.seek 1");
        yield return UOkCall(new { cmd = "obj.transform", key = uP, pos = new[] { 1, 2, 3 } }, "move the item while paused");
        USnap();
        yield return UFrames(2);
        int u0 = UUndo;
        yield return UClick("timeline-key");
        var tr = UDoc.Tracks[lane];
        int k1 = tr.IndexOfTime(1f);
        var v = k1 >= 0 ? tr.Keys[k1].Vector : default;
        Check("Key at 1 s stores the shown value (the edit made while paused): (1, 2, 3)", k1 >= 0 && v.X == 1f && v.Y == 2f && v.Z == 3f, tr.ToJson());
        Check("one entry «Key»", UUndo == u0 + 1 && UTop == TimelineEditCommand.LabelKey, UUndo - u0 + " / " + UTop);
        USnap();
        u0 = UUndo;
        var p = ULanePoint(lane, 2.0);
        yield return UPointer(p, "click", 2);
        yield return UFrames(3);
        tr = UDoc.Tracks[lane];
        int k2 = tr.IndexOfTime(2f);
        yield return UStatus();
        double time = ulast?.Value<double?>("time") ?? -1;
        Check("a double click on the lane at 2 s seeks there", Math.Abs(time - 2.0) < 0.011, "time " + time);
        Check("and keys the shown value there (one entry «Key»)", k2 >= 0 && tr.Keys[k2].Vector.X == 1f && UUndo == u0 + 1 && UTop == TimelineEditCommand.LabelKey, tr.ToJson());
        USnap();
    }

    static IEnumerator TU5()
    {
        var panel = TimelinePanel.Current;
        int lane = UDoc.IndexOfTrack(UTrack(t => t.Type == TimelineTypes.Pos && t.Key == uP)?.Id);
        var tr = UDoc.Tracks[lane];
        int k = tr.IndexOfTime(2f);
        if (!Check("a key at 2 s", k >= 0)) yield break;
        var g = UQ("timeline-key-" + lane + "-" + k);
        Check("its glyph timeline-key-" + lane + "-" + k, g != null);
        var from = ULanePoint(lane, 2.0);
        float dx = (float)(0.5 * panel.Pps);
        int u0 = UUndo;
        yield return UPointer(from, "down");
        yield return UPointer(from + new Vector2(dx * 0.5f, 0), "move");
        yield return UPointer(from + new Vector2(dx, 0), "move");
        yield return UPointer(from + new Vector2(dx, 0), "up");
        yield return UFrames(2);
        tr = UDoc.Tracks[lane];
        Check("the drag moved the key 2 → 2.5 s (snapped to 0.01 s)", tr.IndexOfTime(2f) < 0 && tr.Keys.Any(x => Math.Abs(x.T - 2.5f) < 0.011f), tr.ToJson());
        Check("one entry «Move keys»", UUndo == u0 + 1 && UTop == TimelineEditCommand.LabelMoveKeys, UUndo - u0 + " / " + UTop);
        USnap();
        var moved = tr.Keys.First(x => Math.Abs(x.T - 2.5f) < 0.011f);
        float t25 = moved.T;
        u0 = UUndo;
        string before = UDocJson;
        var at = ULanePoint(lane, t25);
        yield return UPointer(at, "down");
        yield return UPointer(at + new Vector2(dx, 0), "move");
        Check("the drag is running", panel.KeyDragActive);
        yield return UKey("Escape");
        Check("Esc ends the drag", !panel.KeyDragActive);
        yield return UPointer(at + new Vector2(dx, 0), "up");
        yield return UFrames(2);
        Check("Esc: the keys back, nothing recorded", UUndo == u0 && UDocJson == before, UUndo - u0 + "");
    }

    static IEnumerator TU6()
    {
        var panel = TimelinePanel.Current;
        int lane = UDoc.IndexOfTrack(UTrack(t => t.Type == TimelineTypes.Pos && t.Key == uP)?.Id);
        var at = ULanePoint(lane, 1.0);
        yield return UPointer(at, "click");
        yield return UFrames(2);
        Check("a click on the key at 1 s selects it (the key editor shows it)", panel.SelectedKeys.Count == 1 && panel.SelectedKeys[0].T == 1f && UVisible(UQ("timeline-keyedit")));
        int u0 = UUndo;
        yield return UClick("timeline-ease-linear");
        var tr = UDoc.Tracks[lane];
        Check("Linear: the key's ease linear, one entry «Key»", tr.Keys[tr.IndexOfTime(1f)].Ease == TimelineEases.Linear && UUndo == u0 + 1 && UTop == TimelineEditCommand.LabelKey, tr.ToJson());
        Check("the switch shows Linear lit", UQ("timeline-ease-linear")?.ClassListContains("hs-segmented__button--on") == true);
        USnap();
        u0 = UUndo;
        yield return UClick("timeline-ease-more");
        yield return UClick("menu-item-timeline-ease-out");
        tr = UDoc.Tracks[lane];
        Check("Ease out from the drop-down, one entry «Key»", tr.Keys[tr.IndexOfTime(1f)].Ease == TimelineEases.Out && UUndo == u0 + 1, tr.ToJson());
        USnap();
    }

    static IEnumerator TU7()
    {
        var panel = TimelinePanel.Current;
        int lane = UDoc.IndexOfTrack(UTrack(t => t.Type == TimelineTypes.Pos && t.Key == uP)?.Id);
        var tr = UDoc.Tracks[lane];
        var k = tr.Keys.FirstOrDefault(x => Math.Abs(x.T - 2.5f) < 0.011f);
        if (!Check("the key near 2.5 s", k != null)) yield break;
        yield return UPointer(ULanePoint(lane, k.T), "click");
        yield return UFrames(2);
        int u0 = UUndo;
        yield return UKey("Delete");
        tr = UDoc.Tracks[lane];
        Check("Delete in the panel deletes the selected key, one entry «Delete keys»", tr.IndexOfTime(k.T) < 0 && UUndo == u0 + 1 && UTop == TimelineEditCommand.LabelDeleteKeys, UUndo - u0 + " / " + UTop);
        USnap();
        int top = UUndo, steps = 0, bad = 0;
        string firstBad = null;
        while (UUndo > uStart && steps < 40)
        {
            yield return UCall(new { cmd = "ui.action", action = ActionIds.Undo });
            yield return UFrames(1);
            steps++;
            if (uSnap.TryGetValue(UUndo, out var want) && want != UDocJson) { bad++; firstBad ??= "undo to " + UUndo; }
        }
        Check("undo of TU2-TU7 step by step: timeline.get equals the document at each history count (" + steps + " steps)", bad == 0 && UUndo == uStart && UDoc == null, firstBad ?? "");
        steps = 0;
        while (UUndo < top && steps < 40)
        {
            yield return UCall(new { cmd = "ui.action", action = ActionIds.Redo });
            yield return UFrames(1);
            steps++;
            if (uSnap.TryGetValue(UUndo, out var want) && want != UDocJson) { bad++; firstBad ??= "redo to " + UUndo; }
        }
        Check("redo back: the same documents (" + steps + " steps)", bad == 0 && UUndo == top && UDocJson == uSnap[top], firstBad ?? "");
        yield return UCall(new { cmd = "timeline.get" });
        Check("timeline.get answers the document after the redo", UOk && ulast?["timeline"]?.ToString(Formatting.None) == JObject.Parse(UDocJson).ToString(Formatting.None));
        yield return UFrames(2);
    }

    static IEnumerator TU8()
    {
        yield return UClick("timeline-play");
        yield return UFrames(5);
        yield return UStatus();
        double t1 = ulast?.Value<double?>("time") ?? -1;
        Check("Play: state playing", (string)ulast?["state"] == "playing", Short(ulastText));
        Check("the button shows pause", UQ("timeline-play") is Button pb && pb.text == "❚❚");
        yield return UFrames(5);
        yield return UStatus();
        Check("the play head advances", (ulast?.Value<double?>("time") ?? -1) > t1, t1 + " → " + ulast?["time"]);
        yield return UClick("timeline-play");
        yield return UStatus();
        Check("Pause: state paused", (string)ulast?["state"] == "paused", Short(ulastText));
        yield return UClick("timeline-stop");
        yield return UStatus();
        Check("Stop: state stopped at 0", (string)ulast?["state"] == "stopped" && ulast.Value<double>("time") == 0, Short(ulastText));
        yield return UClick("timeline-play");
        yield return UFrames(10);
        int u0 = UUndo;
        yield return UClick("timeline-stop-more");
        Check("the stop drop-down holds Stop and keep this pose", UQ("menu-item-timeline-stopbake") != null);
        yield return UClick("menu-item-timeline-stopbake");
        yield return UStatus();
        Check("Stop and keep this pose: one entry «Bake Timeline pose», stopped", UUndo == u0 + 1 && UTop == TimelineEditCommand.LabelBake && (string)ulast?["state"] == "stopped", UUndo - u0 + " / " + UTop + " / " + ulast?["state"]);
        // the Export frames dialog opens from the ⋯ menu (no recording is started here: TW covers the recorder)
        yield return UClick("timeline-menu");
        yield return UClick("menu-item-timeline-export");
        var dlg = UQ(TimelineExportDialog.DialogId) as TimelineExportDialog;
        var cmd = UQ("timeline-export-command") as TextField;
        Check("⋯ ▸ Export frames… opens the dialog (frame rate, one pass on with a Timeline, folder, the note, the ffmpeg line)",
            dlg != null && UUi.Dialogs.IsModalOpen && UQ("timeline-export-fps-dropdown") != null && dlg.OnePass && UQ("timeline-export-folder") != null &&
            UQ("timeline-export-note") != null && cmd != null && cmd.value == "ffmpeg -framerate 30 -i frame_%06d.png -pix_fmt yuv420p out.mp4" && UQ("dialog-button-start") != null,
            cmd?.value);
        UUi.Dialogs.CloseCurrent();
        yield return UFrames(2);
    }

    static IEnumerator TU9()
    {
        int u0 = UUndo;
        var a = URulerPoint(3.0);
        var b = URulerPoint(4.0);
        yield return UPointer(a, "down");
        yield return UPointer(Vector2.Lerp(a, b, 0.5f), "move");
        yield return UPointer(b, "move");
        yield return UStatus();
        double mid = ulast?.Value<double?>("time") ?? -1;
        yield return UPointer(b, "up");
        yield return UFrames(2);
        yield return UStatus();
        double t = ulast?.Value<double?>("time") ?? -1;
        Check("scrubbing the ruler seeks to the pointer (4 s), paused", Math.Abs(t - 4.0) < 0.02 && (string)ulast?["state"] == "paused", "time " + t + " (during the drag " + mid + ")");
        Check("the play head stands at the pointer", Math.Abs(UQ("timeline-head").worldBound.center.x - b.x) < 3);
        Check("scrubbing records nothing", UUndo == u0);
    }

    static IEnumerator TU10()
    {
        var ui = UUi;
        var cc = ui.Viewport.Camera;
        // the camera track drives the view while playing
        yield return UOkCall(new { cmd = "timeline.play", from = 0 }, "play");
        yield return UFrames(5);
        Check("playing a camera track: the camera controller is disabled", !cc.Enabled && cc.DrivenBy == "timeline", "driven by " + cc.DrivenBy);
        yield return PlayModeHarness.WaitRealtime(0.5);
        var line = UQ("viewport-camera-driven") as Label;
        Check("the viewport says the camera is driven by the Timeline", line != null && UVisible(line) && line.text == L10n.Format("viewport.cameraDriven", L10n.Get("panel.timeline")), line?.text);
        yield return UOkCall(new { cmd = "timeline.pause" }, "pause");
        yield return PlayModeHarness.WaitRealtime(0.5);
        Check("paused: the controller works and the line is gone", cc.Enabled && !UVisible(line));
        yield return UOkCall(new { cmd = "timeline.stop" }, "stop");
        int cam = UDoc.IndexOfTrack(UTrack(t => t.Type == TimelineTypes.Camera)?.Id);
        yield return UOkCall(new { cmd = "timeline.track.set", track = cam, enabled = false }, "the camera track off (for the gizmo checks)");

        // the item: the mark, the gizmo rule
        yield return UOkCall(new { cmd = "select", keys = new[] { uP } }, "select the item");
        ui.Shell.ShowPanel("inspector");
        ui.Tool = GizmoTool.Move;
        ui.Space = GizmoSpace.Auto;
        ui.Snap = false;
        var holder = ui.Runtime.HolderOf(uP);
        cc.SetView(holder.position, new Vector3(20, 30, 0), 10, 23);
        cc.Flush();
        yield return UFrames(5);
        yield return UOkCall(new { cmd = "timeline.play", from = 0 }, "play");
        yield return PlayModeHarness.WaitRealtime(1.0);
        var row = UQ("panel-inspector")?.Q("field-transform.pos");
        var mark = row?.Q(TimelineOverrideUi.MarkName);
        Check("playing: the inspector's position row shows the overridden mark", mark != null && UVisible(mark), row == null ? "no row field-transform.pos" : "row children " + row.childCount);
        Check("its tooltip names the Timeline and says the field is saved", mark != null && mark.tooltip == L10n.Format("ui.override.tip", L10n.Get("panel.timeline")), mark?.tooltip);
        var g = ui.Viewport.Gizmo;
        var hp = holder.position;
        float L = ui.Options.Int(OptionDefs.GizmoSize) * g.WorldPerPx(hp);
        Vector2 a0 = g.ToPanel(hp), aT = g.ToPanel(hp + Vector3.right * L);
        Vector2 grab = Vector2.Lerp(a0, aT, 0.6f);
        var hit = g.HitTest(grab);
        Check("the X arrow is under the grab point", hit.Kind == HandleKind.MoveAxis, hit.ToString());
        int toasts = ui.Toasts.History.Count;
        bool began = g.TryBegin(grab, EventModifiers.None);
        Check("playing: a gizmo drag on the item is refused with the toast", began && !g.Dragging && ui.Toasts.History.Count > toasts && ui.Toasts.History.Last().EndsWith(L10n.Get("toast.timelinePlaying")),
            "began " + began + ", dragging " + g.Dragging + ", toast " + ui.Toasts.History.LastOrDefault());
        if (g.Dragging) g.Cancel();
        yield return UOkCall(new { cmd = "timeline.pause" }, "pause");
        yield return UFrames(3);
        hp = holder.position;
        L = ui.Options.Int(OptionDefs.GizmoSize) * g.WorldPerPx(hp);
        a0 = g.ToPanel(hp);
        aT = g.ToPanel(hp + Vector3.right * L);
        grab = Vector2.Lerp(a0, aT, 0.6f);
        float x0 = hp.x;
        began = g.TryBegin(grab, EventModifiers.None);
        bool dragging = g.Dragging;
        if (dragging)
        {
            g.Drag(grab + (aT - a0).normalized * 30, EventModifiers.None);
            g.End();
        }
        yield return UFrames(3);
        yield return UStatus();
        Check("paused: the drag works and moves the item", began && dragging && Math.Abs(holder.position.x - x0) > 1e-4, "x " + x0 + " → " + holder.position.x);
        Check("paused: the edit suspends the position track", (ulast?.Value<int?>("suspended") ?? 0) >= 1, Short(ulastText));
        yield return UOkCall(new { cmd = "timeline.stop" }, "stop");
        yield return PlayModeHarness.WaitRealtime(1.0);
        row = UQ("panel-inspector")?.Q("field-transform.pos");
        Check("stopped: no mark", row != null && row.Q(TimelineOverrideUi.MarkName) == null);
    }

    static IEnumerator TU11()
    {
        var ta = Resources.Load<TextAsset>(UiHost.StringsResource);
        if (!Check("strings.tsv loads", ta != null)) yield break;
        var table = L10nTable.Parse(ta.text);
        var keys = new List<string>();
        bool inBlock = false;
        foreach (var raw in ta.text.Split('\n'))
        {
            var l = raw.TrimEnd('\r');
            if (l.StartsWith("#")) { inBlock = l.StartsWith("#U62", StringComparison.Ordinal); continue; }
            if (inBlock && l.Length > 0) keys.Add(l.Split('\t')[0]);
        }
        var missing = keys.Where(k => new[] { L10nTable.En, L10nTable.Ja, L10nTable.ZhHans }.Any(lang => string.IsNullOrEmpty(table.Cell(k, lang)))).ToList();
        Check("every key of the panel's strings block has en, ja and zh-Hans (" + keys.Count + " keys)", keys.Count >= 100 && missing.Count == 0, string.Join(", ", missing.Take(10)));
        var used = new[] { "panel.timeline", "timeline.new", "timeline.stopBake", "ui.override.tip", "viewport.cameraDriven", "toast.timelinePlaying", "status.timeline", "timeline.export.note", "history.timeline.bake" };
        Check("the spec's rows are in the block", used.All(keys.Contains), string.Join(", ", used.Where(k => !keys.Contains(k))));
        foreach (var a in new[] { ActionIds.TimelinePanel, ActionIds.TimelinePlayPause, ActionIds.TimelineStop, ActionIds.TimelineKey, ActionIds.TimelineExport })
            Check("action label " + a, keys.Contains(ActionIds.LabelKey(a)));
        yield return null;
    }

    static IEnumerator TU12()
    {
        var ui = UUi;
        yield return UOkCall(new { cmd = "record.start", path = "record/tu12", width = 64, height = 36, fps = 30, overwrite = true }, "record.start (64 x 36, until stopped)");
        yield return UFrames(3);
        yield return UOkCall(new { cmd = "scene.pause", paused = true }, "the global pause on");
        yield return UFrames(2);
        var pb = ui.Shell.Toolbar.Playback;
        pb.Refresh();
        Check("recording and paused: the toolbar's Step is disabled", pb.Step != null && !pb.Step.enabledSelf);
        double t0 = ui.Runtime.Clock.Time;
        int toasts = ui.Toasts.History.Count;
        yield return UCall(new { cmd = "ui.action", action = ActionIds.TimeStep });
        yield return UFrames(2);
        Check("time.step answers busy with the toast; the scene time does not move",
            UOk && ((string)ulast?["result"] ?? "").StartsWith("busy", StringComparison.Ordinal) && ui.Toasts.History.Count > toasts &&
            ui.Toasts.History.Last().EndsWith(L10n.Get("toast.stepRecording")) && ui.Runtime.Clock.Time == t0, Short(ulastText));
        yield return UCall(new { cmd = "scene.step", n = 1 });
        Check("scene.step answers busy as well", !UOk && (string)ulast?["code"] == "busy", Short(ulastText));
        yield return UOkCall(new { cmd = "record.stop" }, "record.stop");
        pb.Refresh();
        Check("after the recording the Step is enabled again (paused)", pb.Step.enabledSelf);
        yield return UOkCall(new { cmd = "scene.pause", paused = false }, "the global pause off");
    }
}

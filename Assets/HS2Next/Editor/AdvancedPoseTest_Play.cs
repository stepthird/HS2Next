// The Advanced pose tab and the bone handle in AdvancedPoseTest.play (AU1-AU9; Phase 6.3 package U), a part file of AdvancedPoseTest: one play
// part (Play_U_Tab) on the app with the UI, driven like a user through the bridge's ui.input / ui.action and judged by element state, the
// history, the block and the bones, never by pixels. Its own scene: BR-Chan and HERO107, under the global pause where a value is compared.
//   AU1  the tab exists on a character, nothing of it is built before it is first shown, nothing is read while it is closed; the note on
//        two selected
//   AU2  the tree: part, search, expand, Corrected only: counts equal pose.bones; the marks; virtualised
//   AU3  a row selected: the sub-selection, the gizmo on the bone at its world position, the three tools; a selection elsewhere follows
//   AU4  gizmo drags on a finger and on an arm bone with FK on: one entry each, Esc, undo, a press that moves nothing, the math; the FK value
//        untouched; the FK handle on a corrected bone edits the FK value
//   AU5  the rows: typing, chaining, ranges, a label drag, Reset, Reset all, a stale entry's Remove, a file's bad key's Remove
//   AU6  the pick: off = no sphere, no hit test; on: a click selects; FK and IK handles first; at most 256; off with the tab
//   AU7  Jiggle: a chain's rows with the card's value, one entry per drag, Esc, reset; the bust and hip chains
//   AU8  Blend shapes: one entry per drag, Esc, reset, typed range; the weight follows the face; no protected renderer offered
//   AU9  every string in en, ja, zh-Hans; a paused scene edits at once; the default layout and the actions as before
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HS2Next.App;
using HS2Next.Motion.AdvancedPose;
using HS2Next.Motion.DynamicBones;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Core;
using HS2Next.UI.Gizmos;
using HS2Next.UI.Panels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class AdvancedPoseTest
{
    const string AuFinger = "cf_J_Hand_Index02_L", AuArm = "cf_J_ArmUp00_R";

    static AppCore AuApp => AppCore.Current;
    static UiContext AuUi => UiHost.Current?.Context;
    static JObject auLast;
    static string auText;
    static bool AuOk => auLast?.Value<bool?>("ok") == true;
    static string auA, auB;

    // ------------------------------------------------------------ helpers

    static IEnumerator AuCall(object request, double timeoutSec = 60)
    {
        string json = request is string s ? s : JsonConvert.SerializeObject(request);
        auLast = null;
        auText = null;
        var t = AuApp.Bridge.Execute(json);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!t.IsCompleted && sw.Elapsed.TotalSeconds < timeoutSec) yield return PlayModeHarness.WaitFrames(1);
        auText = t.IsCompleted && !t.IsFaulted ? t.Result : "{\"ok\":false,\"code\":\"timeout\"}";
        try { auLast = JObject.Parse(auText); }
        catch (Exception) { auLast = new JObject { ["ok"] = false }; }
    }

    static IEnumerator AuOkCall(object request, string what)
    {
        yield return AuCall(request);
        Check(what, AuOk, AuOk ? "" : Short(auText));
    }

    static IEnumerator AuFrames(int n) => PlayModeHarness.WaitFrames(n);

    static VisualElement AuQ(string name) => UiHost.Current?.Root?.panel?.visualTree?.Q(name);
    static InspectorPanel AuInspector => UiHost.Current?.Root?.panel?.visualTree?.Q<InspectorPanel>();
    static AdvancedPoseTab AuTab => AdvancedPoseTab.Current;

    static bool AuVisible(VisualElement e)
    {
        if (e == null || e.panel == null) return false;
        for (var x = e; x != null; x = x.hierarchy.parent)
            if (x.resolvedStyle.display == DisplayStyle.None || x.resolvedStyle.visibility == Visibility.Hidden) return false;
        return true;
    }

    /// <summary>Brings an element of the inspector into its scrolled view (a click lands where the element is shown).</summary>
    static IEnumerator AuReveal(VisualElement e)
    {
        var sv = AuQ("inspector-scroll") as ScrollView;
        if (e == null || sv == null) yield break;
        for (var x = e.hierarchy.parent; x != null; x = x.hierarchy.parent)
            if (x == sv) { sv.ScrollTo(e); yield return AuFrames(3); yield break; }
    }

    static IEnumerator AuClick(string name)
    {
        yield return AuReveal(AuQ(name));
        yield return AuCall(new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(new JObject { ["type"] = "pointer", ["target"] = name }), ["frames"] = 1 });
        Check("click " + name, AuOk, AuOk ? "" : Short(auText));
        yield return AuFrames(2);
    }

    static IEnumerator AuKey(string key)
    {
        yield return AuCall(new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(new JObject { ["type"] = "key", ["key"] = key }), ["frames"] = 1 });
        yield return AuFrames(2);
    }

    static JObject AuPointer(Vector2 p, string phase, int button = 0) => new JObject { ["type"] = "pointer", ["x"] = p.x, ["y"] = p.y, ["phase"] = phase, ["button"] = button };

    /// <summary>A press at <paramref name="p"/>, moves to p + d in steps, a release (with <paramref name="esc"/>: Esc before the release).</summary>
    static IEnumerator AuDrag(Vector2 p, Vector2 d, int steps = 4, bool esc = false)
    {
        var ev = new JArray(AuPointer(p, "down"));
        for (int k = 1; k <= steps; k++) ev.Add(AuPointer(p + d * k / steps, "move"));
        if (esc) ev.Add(new JObject { ["type"] = "key", ["key"] = "Escape" });
        if (esc) ev.Add(AuPointer(p + d * 1.2f, "move"));
        ev.Add(AuPointer(p + d, "up"));
        yield return AuCall(new JObject { ["cmd"] = "ui.input", ["events"] = ev, ["frames"] = 2 });
        Check("ui.input drag", AuOk, AuOk ? "" : Short(auText));
        yield return AuFrames(2);
    }

    static IEnumerator AuClickAt(VisualElement e)
    {
        yield return AuReveal(e);
        yield return AuCall(new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(AuPointer(e.worldBound.center, "click")), ["frames"] = 2 });
        yield return AuFrames(2);
    }

    static int AuUndo => AuUi.Session.Commands.UndoList.Count;
    static string AuTop => AuUi.Session.Commands.UndoList.Count > 0 ? AuUi.Session.Commands.UndoList[0].Label : null;
    static CharaAdvancedPose AuBlock(string id) => (AuUi.Model.Find(id).State as CharaNodeState)?.AdvancedPose;
    static CharaRealization AuReal(string id) => AuApp.Runtime.FindChara(id);
    static Transform AuBone(string id, string address) => AdvancedPoseResolver.Resolve(AuReal(id), address);
    static TransformTool AuGizmo => AuUi.Viewport.Gizmo;

    /// <summary>One render of the view camera: the gizmo's pass runs and its state (pivot, axes, length, spheres drawn) is this frame's.</summary>
    static void AuRender()
    {
        var cam = AuUi.Viewport?.ViewCamera;
        if (!cam) return;
        int w = Mathf.Max(64, cam.pixelWidth), h = Mathf.Max(64, cam.pixelHeight);
        var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
        var prevT = cam.targetTexture;
        var prevR = cam.rect;
        UiHost.Current.GizmosIntoTargets = true;
        try
        {
            cam.targetTexture = rt;
            cam.rect = new Rect(0, 0, 1, 1);
            cam.Render();
        }
        finally
        {
            cam.targetTexture = prevT;
            cam.rect = prevR;
            UiHost.Current.GizmosIntoTargets = false;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    /// <summary>The view centred on the shown handle, near enough to tell its arms apart; the state rendered again.</summary>
    static IEnumerator AuCentreView()
    {
        AuRender();
        var cv = AuUi.Viewport.Camera.Current;
        AuUi.Viewport.Camera.SetView(AuGizmo.PivotWorld, new Vector3(cv.Rot.X, cv.Rot.Y, cv.Rot.Z), Mathf.Max(cv.Distance, 25f), cv.Fov);
        yield return AuFrames(4);
        AuRender();
    }

    static void AuApply()
    {
        AuUi.Runtime?.ApplyNow();
    }

    static IEnumerator AuUndoStep()
    {
        AuUi.Session.Commands.Undo();
        AuApply();
        yield return AuFrames(2);
    }

    static IEnumerator AuRedoStep()
    {
        AuUi.Session.Commands.Redo();
        AuApply();
        yield return AuFrames(2);
    }

    static IEnumerator AuClearAll(string id)
    {
        yield return AuCall(new { cmd = "pose.bones.clear", key = id, all = true });
        yield return AuCall(new { cmd = "pose.chains.clear", key = id, all = true });
        yield return AuCall(new { cmd = "pose.bust.clear", key = id, all = true });
        yield return AuCall(new { cmd = "pose.blendShapes.clear", key = id, all = true });
        yield return AuFrames(2);
    }

    /// <summary>Selects a character, shows the inspector and clicks its Advanced pose tab.</summary>
    static IEnumerator AuOpenTab(string id)
    {
        yield return AuOkCall(new { cmd = "select", keys = new[] { id } }, "select the character");
        yield return AuOkCall(new { cmd = "ui.panel", panel = "inspector", op = "show" }, "show the inspector");
        yield return AuFrames(3);
        AuInspector?.Refresh(true);
        yield return AuFrames(2);
        if (AuTab == null || AuTab.Primary != id) yield return AuClick("tab-" + AdvancedPoseTab.TabId);
        yield return AuFrames(3);
        Check("the tab is open on the character", AuTab != null && AuTab.Primary == id, AuInspector?.Chara?.CurrentTab);
        AuTab?.Refresh();
    }

    /// <summary>A bone's row brought into the tree: a search for its name, a wait for the row.</summary>
    static IEnumerator AuFindRow(string address)
    {
        var tab = AuTab;
        if (!Check("the tab is open for the row of " + address, tab != null)) yield break;
        tab.SetPart(AdvancedPoseResolver.BodyPart);
        tab.SetCorrectedOnly(false);
        tab.SetQuery(address);
        var ok = new bool[1];
        int v = -1;
        for (int i = 0; i < tab.VisibleRows.Count; i++) if (tab.PartRows[tab.VisibleRows[i]].Row.Address == address) v = i;
        if (!Check("the search lists " + address, v >= 0)) yield break;
        yield return AuFrames(2);
        tab.Tree.ScrollToItem(v);
        yield return PlayModeHarness.WaitUntil(() => AuQ("adv-row-" + address) != null, 5, ok);
        Check("the row of " + address + " is in the tree", ok[0]);
        yield return AuFrames(3);
    }

    static IEnumerator AuSelectBone(string id, string address)
    {
        yield return AuFindRow(address);
        var row = AuQ("adv-row-" + address);
        var tree = AuTab?.Tree;
        yield return AuClick("adv-row-" + address);
        Check("the row selects " + address, AuUi.SubSelection == id + "/bone/" + address, AuUi.SubSelection ?? "null");
        if (AuUi.SubSelection != id + "/bone/" + address)
        {
            var at = row != null ? row.worldBound.center : default;
            var hit = UiHost.Current?.Root?.panel?.Pick(at);
            Info("the row at " + row?.worldBound + ", shown " + AuVisible(row) + ", tree " + tree?.worldBound + ", selected index " + tree?.selectedIndex + ", listed " + AuTab?.VisibleRows.Count + ", picked " + (hit == null ? "nothing" : hit.name + " (" + hit.GetType().Name + ")") + ", same-named elements " + (UiHost.Current?.Root?.panel?.visualTree?.Query<VisualElement>("adv-row-" + address).ToList().Count ?? -1) + ", inspector " + AuQ("inspector-scroll")?.worldBound);
        }
        AuTab?.SetQuery("");
        yield return AuFrames(2);
    }

    /// <summary>The panel point and direction of an arrow of the shown handle (kind MoveAxis or ScaleAxis) that the hit test finds; false when none.</summary>
    static bool AuAxisGrab(HandleKind kind, out Vector2 p, out Vector2 dir, out int axis)
    {
        var gz = AuGizmo;
        var vb = AuUi.Viewport.Element.worldBound;
        Vector2 c = gz.ToPanel(gz.PivotWorld);
        p = default; dir = default; axis = -1;
        for (int i = 0; i < 3; i++)
        {
            var a = gz.Axes * (i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward);
            foreach (float f in new[] { 0.9f, 0.75f, 0.6f, 0.45f, 0.3f })
            {
                Vector2 q = gz.ToPanel(gz.PivotWorld + a * (gz.LengthWorld * f));
                var h = gz.HitTest(q);
                if (h.Kind != kind || h.Axis != i || !vb.Contains(q) || (q - c).magnitude < 12) continue;
                p = q; dir = (q - c).normalized; axis = i;
                return true;
            }
        }
        return false;
    }

    /// <summary>A panel point of a ring of the shown handle that the hit test finds, and the ring's direction there.</summary>
    static bool AuRingGrab(out Vector2 p, out Vector2 dir, out int axis)
    {
        var gz = AuGizmo;
        var vb = AuUi.Viewport.Element.worldBound;
        p = default; dir = default; axis = -1;
        for (int i = 0; i < 3; i++)
        {
            var n = i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
            var u = i == 0 ? Vector3.up : Vector3.right;
            var v = Vector3.Cross(n, u);
            for (int s = 0; s < 32; s++)
            {
                float ang = s * Mathf.PI * 2 / 32;
                var w0 = gz.PivotWorld + gz.Axes * (u * Mathf.Cos(ang) + v * Mathf.Sin(ang)) * gz.LengthWorld;
                var w1 = gz.PivotWorld + gz.Axes * (u * Mathf.Cos(ang + 0.1f) + v * Mathf.Sin(ang + 0.1f)) * gz.LengthWorld;
                Vector2 q = gz.ToPanel(w0), q1 = gz.ToPanel(w1);
                var h = gz.HitTest(q);
                if (h.Kind != HandleKind.RotateRing || h.Axis != i || !vb.Contains(q) || (q1 - q).magnitude < 1) continue;
                p = q; dir = (q1 - q).normalized; axis = i;
                return true;
            }
        }
        return false;
    }

    static string AuV(Vec3 v) => "(" + v.X.ToString("G6") + ", " + v.Y.ToString("G6") + ", " + v.Z.ToString("G6") + ")";
    static bool AuSame(Vec3 a, Vec3 b, float eps = 1e-5f) => Mathf.Abs(a.X - b.X) <= eps && Mathf.Abs(a.Y - b.Y) <= eps && Mathf.Abs(a.Z - b.Z) <= eps;
    static Vector3 AuU(Vec3 v) => new Vector3(v.X, v.Y, v.Z);

    // ------------------------------------------------------------ the part

    static IEnumerator Play_U_Tab()
    {
        var parts = new (string id, string title, Func<IEnumerator> run)[]
        {
            ("AU_setup", "setup: app, UI, scene", AuPrepare),
            ("AU1", "the tab", AU1),
            ("AU2", "the tree", AU2),
            ("AU3", "a row selected", AU3),
            ("AU4", "gizmo drags", AU4),
            ("AU5", "the rows", AU5),
            ("AU6", "the pick", AU6),
            ("AU7", "Jiggle", AU7),
            ("AU8", "Blend shapes", AU8),
            ("AU9", "strings, the paused scene, the layout", AU9),
        };
        bool any = parts.Any(x => x.id != "AU_setup" && Want(x.id));
        if (!any) yield break;
        bool ready = true;
        foreach (var (id, title, run) in parts)
        {
            if (id != "AU_setup" && (!Want(id) || !ready)) continue;
            current = id;
            rep.AppendLine();
            rep.AppendLine($"== {id} {title}");
            int before = fails;
            var stack = new Stack<IEnumerator>();
            stack.Push(run());
            while (stack.Count > 0)
            {
                object cur;
                try
                {
                    if (!stack.Peek().MoveNext()) { stack.Pop(); continue; }
                    cur = stack.Peek().Current;
                }
                catch (Exception ex) { Check("exception", false, ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace); break; }
                if (cur is IEnumerator nested) { stack.Push(nested); continue; }
                yield return cur;
            }
            AuUi?.Menus?.CloseAll();
            while (AuUi?.Dialogs != null && AuUi.Dialogs.IsModalOpen) AuUi.Dialogs.CloseCurrent();
            if (id == "AU_setup" && fails > before) ready = false;
        }
        if (AuUi != null && AuUi.Viewport != null) AuUi.Tool = GizmoTool.Move;
        yield return AuCall(new { cmd = "scene.pause", paused = false });
        yield return AuCall(new { cmd = "scene.new" });
    }

    static IEnumerator AuPrepare()
    {
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 240, ok);
        Check("app ready", ok[0] && AuApp?.Stage == AppStage.Ready, AuApp?.Stage.ToString());
        yield return PlayModeHarness.WaitUntil(() => UiHost.Current != null && UiHost.Current.IsReady, 60, ok);
        if (!Check("UI ready", ok[0])) yield break;
        yield return AuOkCall(new { cmd = "scene.new" }, "scene.new");
        yield return AuCall(new { cmd = "chara.load", path = femaleRel, sex = "female" });
        Check("chara.load BR-Chan", AuOk, Short(auText));
        auA = Key(auLast);
        yield return AuCall(new { cmd = "chara.load", path = maleRel, sex = "male" });
        Check("chara.load HERO107", AuOk, Short(auText));
        auB = Key(auLast);
        yield return PlayModeHarness.WaitUntil(() => AuReal(auA)?.State == RealizationState.Built && AuReal(auB)?.State == RealizationState.Built, 180, ok);
        Check("both built", ok[0]);
        AuUi.Shell.ResetLayout();
        yield return AuFrames(3);
    }

    // ------------------------------------------------------------ AU1

    static IEnumerator AU1()
    {
        yield return AuOkCall(new { cmd = "select", keys = new[] { auA } }, "select BR-Chan");
        yield return AuOkCall(new { cmd = "ui.panel", panel = "inspector", op = "show" }, "show the inspector");
        CharaInspector.LastTab = "anime";
        AuInspector.Refresh(true);
        yield return AuFrames(3);
        var ch = AuInspector.Chara;
        Check("the tab's button stands after Correction", AuQ("tab-" + AdvancedPoseTab.TabId) != null && AuQ("tab-correction")?.parent?.IndexOf(AuQ("tab-" + AdvancedPoseTab.TabId)) == AuQ("tab-correction")?.parent?.IndexOf(AuQ("tab-correction")) + 1);
        Check("before it is first shown nothing of it stands: no tree, no state", AuQ("adv-tree") == null && AuTab == null);
        yield return AuClick("tab-" + AdvancedPoseTab.TabId);
        Check("a click shows the tab", ch.CurrentTab == AdvancedPoseTab.TabId && AuTab != null && AuTab.Primary == auA, ch.CurrentTab);
        yield return AuCall(new { cmd = "ui.query", name = "adv-tree" });
        Check("ui.query finds the tree, visible", AuOk && auLast["elements"]?[0]?.Value<bool>("visible") == true, Short(auText));
        yield return AuCall(new { cmd = "ui.query", name = "adv-section-bones" });
        Check("ui.query finds the Bones section", AuOk && (auLast["elements"] as JArray)?.Count == 1, Short(auText));
        Check("the sections Bones, Jiggle and Blend shapes", AuQ("adv-section-jiggle") != null && AuQ("adv-section-blend") != null);
        Check("Jiggle and Blend shapes are closed and built when first opened", AuQ("adv-chains") == null && AuQ("adv-blend-list") == null);
        AuTab.Refresh();
        Check("one selected: no note", !AuVisible(AuQ("adv-primary-only")));

        // two selected: the note; the tab edits the first
        yield return AuOkCall(new { cmd = "select", keys = new[] { auA, auB } }, "select both");
        yield return AuFrames(3);
        AuInspector.Refresh();
        yield return AuFrames(3);
        Check("two selected: the tab on the primary", AuTab != null && AuTab.Primary == auA);
        AuTab?.Refresh();
        Check("two selected: the note shows", AuVisible(AuQ("adv-primary-only")));
        yield return AuOkCall(new { cmd = "select", keys = new[] { auA } }, "select BR-Chan again");
        yield return AuFrames(3);
        AuInspector.Refresh();
        yield return AuFrames(3);

        // nothing is read while the tab is closed: another tab, then the inspector's panel hidden behind another
        var tab = AuTab;
        Check("the tab is open", tab != null);
        int r0 = tab.Refreshes;
        var waited = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => tab.Refreshes > r0, 3, waited);
        Check("while open it refreshes", waited[0], "refreshes " + tab.Refreshes);
        yield return AuClick("tab-anime");
        Check("another tab: the tab is closed", AuTab == null);
        int r1 = tab.Refreshes;
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 1.3f) yield return AuFrames(2);
        Check("closed for 1.3 s: no refresh", tab.Refreshes == r1, (tab.Refreshes - r1).ToString());
        yield return AuClick("tab-" + AdvancedPoseTab.TabId);
        Check("back on the tab", AuTab == tab);
        AuUi.Shell.SetPanelVisible("inspector", true);
        yield return AuOkCall(new { cmd = "ui.panel", panel = "workspace", op = "show" }, "the workspace in front of the inspector");
        yield return AuFrames(3);
        int r2 = tab.Refreshes;
        t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 1.3f) yield return AuFrames(2);
        Check("behind another panel for 1.3 s: no refresh", tab.Refreshes == r2, (tab.Refreshes - r2).ToString());
        yield return AuOkCall(new { cmd = "ui.panel", panel = "inspector", op = "show" }, "the inspector in front");
        yield return AuFrames(3);
    }

    // ------------------------------------------------------------ AU2

    static IEnumerator AU2()
    {
        yield return AuOpenTab(auA);
        var tab = AuTab;
        if (!Check("the tab is open", tab != null)) yield break;
        tab.SetQuery("");
        tab.SetCorrectedOnly(false);

        // parts: every part's bones as pose.bones lists them
        foreach (var part in tab.PartChoices.ToList())
        {
            yield return AuCall(new { cmd = "pose.bones", key = auA, part, limit = 5000 });
            tab.SetPart(part);
            int n = auLast.Value<int>("count");
            Check("part " + part + ": the rows equal pose.bones", tab.PartRows.Count == n, tab.PartRows.Count + " / " + n);
        }
        Check("the parts: body and head first", tab.PartChoices[0] == AdvancedPoseResolver.BodyPart && tab.PartChoices[1] == AdvancedPoseResolver.HeadPart, string.Join(", ", tab.PartChoices.Take(4)));
        var partField = AuQ("adv-part") as DropdownField;
        Check("the part list names each part", partField != null && partField.choices.Count == tab.PartChoices.Count, partField?.choices.Count + " / " + tab.PartChoices.Count);
        partField.value = partField.choices[1];
        yield return AuFrames(2);
        Check("the part list changes the part", tab.Part == AdvancedPoseResolver.HeadPart, tab.Part);
        Check("head: open as far as cf_J_FaceRoot's first level", tab.VisibleRows.Any(i => tab.PartRows[i].Row.Address == "cf_J_FaceRoot") && tab.VisibleRows.Any(i => tab.PartRows[i].Row.Parent == "cf_J_FaceRoot"));
        partField.value = partField.choices[0];
        yield return AuFrames(2);
        Check("body: open as far as cf_J_Hips's first level", tab.VisibleRows.Any(i => tab.PartRows[i].Row.Address == "cf_J_Hips") && tab.VisibleRows.Any(i => tab.PartRows[i].Row.Parent == "cf_J_Hips"));

        // the facts of the rows
        yield return AuCall(new { cmd = "pose.bones", key = auA, part = "body", limit = 5000 });
        var listed = (auLast["bones"] as JArray).OfType<JObject>().ToList();
        Check("the rows' addresses, parents and depths equal pose.bones", listed.Count == tab.PartRows.Count && listed.Select((b, i) => (string)b["bone"] == tab.PartRows[i].Row.Address && (string)b["parent"] == tab.PartRows[i].Row.Parent && (int)b["depth"] == tab.PartRows[i].Row.Depth).All(x => x));
        Check("the FK mark: as pose.bones's fkGroup", listed.Select((b, i) => (b["fkGroup"]?.Type != JTokenType.Null) == tab.PartRows[i].Fk).All(x => x), listed.Count(b => b["fkGroup"]?.Type != JTokenType.Null) + " FK rows");
        Check("the greyed rows: as pose.bones's protected", listed.Select((b, i) => b.Value<bool>("protected") == tab.PartRows[i].Protected).All(x => x), listed.Count(b => b.Value<bool>("protected")) + " protected");

        // expand and collapse
        var closed = tab.VisibleRows.Select(i => tab.PartRows[i]).First(r => r.HasChildren && !tab.IsExpanded(r.Row.Address));
        int v0 = tab.VisibleRows.Count;
        int kids = listed.Count(b => (string)b["parent"] == closed.Row.Address);
        tab.SetExpanded(closed.Row.Address, true);
        yield return AuFrames(1);
        Check("opening a row lists its " + kids + " children", tab.VisibleRows.Count == v0 + kids, tab.VisibleRows.Count + " / " + (v0 + kids));
        tab.SetExpanded(closed.Row.Address, false);
        yield return AuFrames(1);
        Check("closing it takes them away", tab.VisibleRows.Count == v0);

        // a click on the arrow opens without selecting
        string sub0 = AuUi.SubSelection;
        yield return AuFrames(2);
        var lineWithArrow = tab.Tree.Query<VisualElement>(className: InspectorUi.RowClass).ToList().FirstOrDefault(l => { var a = l.Q<Label>("adv-row-arrow"); return a != null && a.text == "▸"; });
        if (Check("a closed row's arrow is on screen", lineWithArrow != null))
        {
            int v1 = tab.VisibleRows.Count;
            yield return AuClickAt(lineWithArrow.Q<Label>("adv-row-arrow"));
            Check("a click on the arrow opens the row", tab.VisibleRows.Count > v1, tab.VisibleRows.Count + " / " + v1);
            Check("and selects nothing", AuUi.SubSelection == sub0, AuUi.SubSelection ?? "null");
        }

        // a search: the matches with their ancestors, the count as pose.bones's filter
        var search = AuQ("adv-search") as TextField;
        foreach (var q in new[] { "Hand_Index", "cf_J_Spine", "zzz_no_such_bone" })
        {
            yield return AuCall(new { cmd = "pose.bones", key = auA, part = "body", filter = q, limit = 5000 });
            int want = auLast.Value<int>("count");
            search.value = q;
            var ok = new bool[1];
            yield return PlayModeHarness.WaitUntil(() => tab.Matches == want && tab.VisibleRows.Count >= want, 3, ok);
            Check("search \"" + q + "\": " + want + " matches as pose.bones", ok[0], tab.Matches + " / " + want);
            var key = SearchText.Normalize(q);
            var shown = tab.VisibleRows.Select(i => tab.PartRows[i]).ToList();
            var addresses = new HashSet<string>(shown.Select(r => r.Row.Address));
            Check("search \"" + q + "\": every match is listed, each with its parent", tab.PartRows.Where(r => r.Key.Contains(key)).All(r => addresses.Contains(r.Row.Address)) && shown.All(r => r.Row.Parent == null || addresses.Contains(r.Row.Parent)));
        }
        search.value = "";
        tab.SetQuery("");
        yield return AuFrames(1);

        // Corrected only and the mark
        yield return AuOkCall(new { cmd = "pose.bones.set", key = auA, bones = new Dictionary<string, object> { [AuFinger] = new { rot = new[] { 0, 0, 10 } }, ["cf_J_Spine02"] = new { pos = new[] { 0.01, 0, 0 } } } }, "two corrections");
        yield return AuFrames(2);
        var corr = AuQ("adv-corrected") as Toggle;
        corr.value = true;
        yield return AuFrames(2);
        yield return AuCall(new { cmd = "pose.bones", key = auA, part = "body", correctedOnly = true, limit = 5000 });
        Check("Corrected only: the count as pose.bones", tab.Matches == auLast.Value<int>("count") && tab.Matches == 2, tab.Matches + " / " + auLast.Value<int>("count"));
        var finger = AuQ("adv-row-" + AuFinger);
        Check("the corrected row shows its mark", finger != null && finger.Q<Label>("adv-row-corrected")?.resolvedStyle.display == DisplayStyle.Flex);
        corr.value = false;
        yield return AuFrames(2);
        Check("Corrected only off: the tree as before", tab.Matches == tab.PartRows.Count);
        yield return AuClearAll(auA);

        // a correction another stage hides: the tint, its tooltip says which
        foreach (var (bone, why, set) in new[]
        {
            ("cf_J_look_L", "eyeLook", (object)new { cmd = "pose.bones.set", key = auA, bone = "cf_J_look_L", rot = new[] { 0, 10, 0 } }),
            ("cf_J_MouthMove", "mouthWidth", new { cmd = "pose.bones.set", key = auA, bone = "cf_J_MouthMove", scale = new[] { 1.3, 1, 1 } }),
        })
        {
            yield return AuOkCall(set, "a correction on " + bone);
            yield return AuFrames(5);
            tab.SetPart(AdvancedPoseResolver.HeadPart);
            tab.SetQuery(bone);
            yield return AuFrames(2);
            tab.Refresh();
            yield return AuFrames(2);
            yield return AuCall(new { cmd = "pose.bones", key = auA, part = "head", filter = bone });
            var fact = (auLast["bones"] as JArray)?.OfType<JObject>().FirstOrDefault(b => (string)b["bone"] == bone);
            var text = AuQ("adv-row-" + bone)?.Q<Label>("adv-row-text");
            bool shownFact = fact?.Value<bool>("shown") ?? true;
            Check(bone + ": pose.bones says the value does not show", !shownFact, fact?.ToString(Formatting.None));
            Check(bone + ": the row's warning tint", text != null && text.ClassListContains("hs-text--warning"));
            Check(bone + ": its tooltip says why", text != null && text.tooltip.Contains(L10n.Get("inspector.chara.adv.why." + why)), text?.tooltip);
            yield return AuOkCall(new { cmd = "pose.bones.clear", key = auA, bone }, "clear " + bone);
            yield return AuFrames(2);
            tab.Refresh();
            yield return AuFrames(2);
            text = AuQ("adv-row-" + bone)?.Q<Label>("adv-row-text");
            Check(bone + ": no correction, no tint", text != null && !text.ClassListContains("hs-text--warning"));
        }
        tab.SetQuery("");
        tab.SetPart(AdvancedPoseResolver.BodyPart);
        yield return AuFrames(1);

        // virtualised: every row open, a screenful of elements
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var r in tab.PartRows.ToList()) if (r.HasChildren) tab.SetExpanded(r.Row.Address, true);
        Measure("AU2: " + tab.PartRows.Count + " rows opened one by one in " + sw.ElapsedMilliseconds + " ms");
        yield return AuFrames(3);
        Check("every row open: the tree lists all of the part", tab.VisibleRows.Count == tab.PartRows.Count, tab.VisibleRows.Count + " / " + tab.PartRows.Count);
        int elements = tab.Tree.Query<VisualElement>(className: InspectorUi.RowClass).ToList().Count;
        Check("the tree is virtualised: at most a screenful of row elements", elements > 0 && elements < 40, elements + " elements for " + tab.VisibleRows.Count + " rows");
        Measure("AU2: " + elements + " row elements for " + tab.VisibleRows.Count + " rows");
        foreach (var r in tab.PartRows.ToList()) if (r.HasChildren) tab.SetExpanded(r.Row.Address, false);
    }

    // ------------------------------------------------------------ AU3

    static IEnumerator AU3()
    {
        yield return AuOpenTab(auA);
        var tab = AuTab;
        if (!Check("the tab is open", tab != null)) yield break;
        yield return AuOkCall(new { cmd = "ui.action", action = "tool.move" }, "the Move tool");
        yield return AuSelectBone(auA, AuFinger);
        yield return AuFrames(2);
        var bone = AuBone(auA, AuFinger);
        Check("the bone's name in the editor, its path in the tooltip", (AuQ("adv-bone-name") as Label)?.text == AuFinger && (AuQ("adv-bone-name")?.tooltip ?? "").EndsWith("/" + AuFinger), AuQ("adv-bone-name")?.tooltip);
        Check("the editor shows, the note does not", AuVisible(AuQ("adv-bone-editor")) && !AuVisible(AuQ("adv-select-note")));
        AuRender();
        yield return AuCall(new { cmd = "ui.state" });
        Check("ui.state: the sub-selection", (string)auLast["sub"] == auA + "/bone/" + AuFinger, (string)auLast["sub"]);
        var pivot = auLast["gizmo"]?["pivot"] as JArray;
        var world = bone.position;
        Check("ui.state: the gizmo stands on the bone (1e-5)", pivot != null && Mathf.Abs((float)pivot[0] - world.x) <= 1e-5f && Mathf.Abs((float)pivot[1] - world.y) <= 1e-5f && Mathf.Abs((float)pivot[2] - world.z) <= 1e-5f, pivot?.ToString(Formatting.None) + " / " + world.ToString("F6"));
        foreach (var (action, tool) in new[] { ("tool.move", "move"), ("tool.rotate", "rotate"), ("tool.scale", "scale") })
        {
            yield return AuOkCall(new { cmd = "ui.action", action }, action);
            yield return AuFrames(2);
            AuRender();
            yield return AuCall(new { cmd = "ui.state" });
            var g = auLast["gizmo"];
            Check(tool + ": the handle shows", g?.Value<bool>("visible") == true && (string)g?["tool"] == tool, g?["tool"]?.ToString());
            var tips = (g?["handles"] as JArray)?.OfType<JObject>().Where(h => (string)h["kind"] == "axis" && (string)h["sub"] == auA + "/bone/" + AuFinger && (string)h["tool"] == tool).ToList();
            Check(tool + ": gizmo.handles lists its three axes", tips?.Count == 3, tips?.Count.ToString());
        }
        yield return AuOkCall(new { cmd = "ui.action", action = "tool.move" }, "the Move tool");

        // a selection elsewhere: the row follows (the part, the ancestors), a head bone switches the part
        yield return AuOkCall(new { cmd = "select", sub = auA + "/bone/cf_J_MouthMove" }, "select a head bone by the command");
        yield return AuFrames(3);
        Check("the tab follows: the part, the row, the editor", tab.SelectedBone == "cf_J_MouthMove" && tab.Part == AdvancedPoseResolver.HeadPart && tab.VisibleRows.Any(i => tab.PartRows[i].Row.Address == "cf_J_MouthMove") && (AuQ("adv-bone-name") as Label)?.text == "cf_J_MouthMove", tab.SelectedBone + " / " + tab.Part);
        yield return AuOkCall(new { cmd = "select", keys = new[] { auA } }, "select the character alone");
        yield return AuFrames(2);
        Check("no bone selected: the editor hides, the note shows", tab.SelectedBone == null && !AuVisible(AuQ("adv-bone-editor")) && AuVisible(AuQ("adv-select-note")));
        tab.SetPart(AdvancedPoseResolver.BodyPart);

        // a bone this character lacks: the handle falls back on the character, nothing is thrown
        yield return AuOkCall(new { cmd = "select", sub = auA + "/bone/no_such_bone_here" }, "select a name this character lacks");
        yield return AuFrames(2);
        AuRender();
        yield return AuCall(new { cmd = "ui.state" });
        var hp = auA != null ? AuUi.Runtime.HolderOf(auA) : null;
        var pv = auLast["gizmo"]?["pivot"] as JArray;
        Check("the handle falls back on the character's holder", pv != null && hp && Mathf.Abs((float)pv[1] - hp.position.y) <= 1e-4f, pv?.ToString(Formatting.None));
        yield return AuOkCall(new { cmd = "select", keys = new[] { auA } }, "select the character alone");

        yield return AuProtected();

        // an FK handle's address keeps its tab
        yield return AuOkCall(new { cmd = "fk.enable", key = auA, on = true }, "FK on");
        yield return AuOkCall(new { cmd = "fk.enable", key = auA, groups = new[] { "Neck" }, on = true }, "FK group Neck");
        yield return AuFrames(3);
        AuInspector.Refresh();
        yield return AuOkCall(new { cmd = "select", sub = auA + "/fk/cf_J_Neck" }, "select the FK neck handle");
        yield return AuFrames(3);
        Check("an FK handle's address opens FK / IK as before", AuInspector.Chara.CurrentTab == "kinematics", AuInspector.Chara.CurrentTab);
        yield return AuOkCall(new { cmd = "fk.enable", key = auA, on = false }, "FK off");
        yield return AuOkCall(new { cmd = "select", keys = new[] { auA } }, "select the character alone");
        yield return AuFrames(2);
    }

    // A synthetic part on an accessory, as a mod could bring: au_part/o_gomu is a member by its name, au_side nothing of the kind. BR-Chan's own
    // bones hold no such row, so this is what tells greyed rows from the others.
    static IEnumerator AuProtected()
    {
        var tab = AuTab;
        var r = AuReal(auA);
        var slot = r.Context.Accessories.FirstOrDefault(x => x != null && x.Object && r.AccessoryIdOf(x) != null);
        if (!Check("an accessory on the target", slot != null)) yield break;
        string part = PoseAddressGrammar.PartText(PosePart.Accessory, -1, r.AccessoryIdOf(slot));
        var top = slot.Object.transform;
        var fixture = new GameObject("au_part").transform;
        fixture.SetParent(top, false);
        var member = new GameObject("o_gomu").transform;
        member.SetParent(fixture, false);
        member.gameObject.AddComponent<MeshRenderer>();
        var side = new GameObject("au_side").transform;
        side.SetParent(top, false);
        string memberAddress = part + "/o_gomu", sideAddress = part + "/au_side";
        tab.SetPart(part);
        tab.SetQuery("");
        yield return AuFrames(2);
        yield return AuCall(new { cmd = "pose.bones", key = auA, part, limit = 5000 });
        var listed = (auLast["bones"] as JArray).OfType<JObject>().ToList();
        int guarded = listed.Count(b => b.Value<bool>("protected"));
        Check("the fixture's rows are protected in pose.bones", guarded >= 3 && listed.First(b => (string)b["bone"] == memberAddress).Value<bool>("protected") && !listed.First(b => (string)b["bone"] == sideAddress).Value<bool>("protected"), guarded + " of " + listed.Count);
        Check("the greyed rows: as pose.bones", tab.PartRows.Count == listed.Count && tab.PartRows.Count(x => x.Protected) == guarded, tab.PartRows.Count(x => x.Protected) + " / " + guarded);
        tab.SetQuery("o_gomu");
        yield return AuFrames(3);
        var line = AuQ("adv-row-" + memberAddress);
        if (Check("the member's row is listed", line != null))
        {
            Check("it is greyed, its tooltip says why", line.Q<Label>("adv-row-text").ClassListContains(InspectorUi.Dim) && line.tooltip == L10n.Get("inspector.chara.adv.protectedTip"), line.tooltip);
            string before = AuUi.SubSelection;
            yield return AuClick("adv-row-" + memberAddress);
            Check("a click does not select it", AuUi.SubSelection == before && tab.Tree.selectedIndex < 0, AuUi.SubSelection + ", index " + tab.Tree.selectedIndex);
        }
        tab.SetQuery("");
        yield return AuFrames(2);
        tab.SetPick(true);
        Check("the pick offers no sphere on it, one on its sibling", tab.PickRows.All(b => b.Address != memberAddress) && tab.PickRows.Any(b => b.Address == sideAddress));
        tab.SetPick(false);

        // selected by a command: the rows are off, a write is refused, the handle refuses at the press
        yield return AuOkCall(new { cmd = "select", sub = auA + "/bone/" + memberAddress }, "select the member by the command");
        yield return AuFrames(3);
        Check("its rows are off", tab.SelectedGuarded && !tab.PosRow.enabledSelf && !tab.RotRow.enabledSelf && !tab.ScaleRow.enabledSelf);
        int u = AuUndo;
        var res = tab.PosRow.Write(new Vector4(0.1f, 0, 0, 0));
        Check("a typed write is refused", res != null && !res.Ok && res.Code == "unsupported" && AuUndo == u && AuBlock(auA).IsEmpty, res?.Code);
        res = tab.PosRow.Write(new Vector4(0.1f, 0, 0, 0), true);
        Check("a step of a drag is refused too", res != null && !res.Ok && res.Code == "unsupported" && AuUndo == u && AuBlock(auA).IsEmpty, res?.Code);
        yield return AuOkCall(new { cmd = "ui.action", action = "tool.move" }, "the Move tool");
        yield return AuFrames(2);
        yield return AuCentreView();
        if (Check("the member's move arrow can be grabbed", AuAxisGrab(HandleKind.MoveAxis, out var p, out var d, out _)))
        {
            yield return AuDrag(p, d * 30);
            Check("a drag on its handle: no entry, no value, no drag left", AuUndo == u && AuBlock(auA).IsEmpty && !AuGizmo.Dragging, (AuUndo - u).ToString());
            yield return AuCall(new { cmd = "ui.state" });
            Check("the refusal is shown", (auLast["toasts"]?.ToString() ?? "").Contains(AdvancedPoseGuard.Refusal), auLast["toasts"]?.ToString(Formatting.None));
        }
        yield return AuOkCall(new { cmd = "select", sub = auA + "/bone/" + sideAddress }, "select its sibling by the command");
        yield return AuFrames(3);
        Check("its sibling's rows are on", !tab.SelectedGuarded && tab.PosRow.enabledSelf);
        yield return AuOkCall(new { cmd = "select", keys = new[] { auA } }, "select the character alone");
        tab.SetPart(AdvancedPoseResolver.BodyPart);
        UnityEngine.Object.Destroy(fixture.gameObject);
        UnityEngine.Object.Destroy(side.gameObject);
        yield return AuFrames(3);
    }

    // ------------------------------------------------------------ AU4

    static IEnumerator AU4()
    {
        yield return AuOkCall(new { cmd = "scene.pause", paused = true }, "the global pause");
        yield return AuOpenTab(auA);
        var tab = AuTab;
        if (!Check("the tab is open", tab != null)) yield break;
        yield return AuOkCall(new { cmd = "ui.action", action = "camera.frame" }, "frame the character");
        yield return AuFrames(5);
        yield return AuBoneDrags(AuFinger, false);
        yield return AuOkCall(new { cmd = "fk.enable", key = auA, on = true }, "FK on");
        yield return AuOkCall(new { cmd = "fk.enable", key = auA, groups = new[] { "Body" }, on = true }, "FK group Body");
        yield return AuFrames(3);
        AuInspector.Refresh();
        yield return AuFrames(2);
        if (AuTab == null) yield return AuOpenTab(auA);
        yield return AuBoneDrags(AuArm, true);
        yield return AuFkHandle();
        yield return AuOkCall(new { cmd = "fk.enable", key = auA, on = false }, "FK off");
        yield return AuOkCall(new { cmd = "ui.action", action = "tool.move" }, "the Move tool");
        yield return AuClearAll(auA);
        yield return AuOkCall(new { cmd = "select", keys = new[] { auA } }, "select the character alone");
        yield return AuOkCall(new { cmd = "scene.pause", paused = false }, "the pause off");
    }

    static Vec3? AuFkValue(string id, string key) =>
        (AuUi.Model.Find(id).State as CharaNodeState)?.Fk.Bones.TryGetValue(key, out var v) == true ? v : (Vec3?)null;

    static IEnumerator AuBoneDrags(string address, bool fk)
    {
        var bone = AuBone(auA, address);
        if (AuTab == null) yield return AuOpenTab(auA);
        yield return AuSelectBone(auA, address);
        yield return AuFrames(2);
        var fkBefore = AuFkValue(auA, address);
        if (fk) Check(address + ": FK holds a value", fkBefore != null);

        // Move
        yield return AuOkCall(new { cmd = "ui.action", action = "tool.move" }, "the Move tool");
        yield return AuFrames(2);
        yield return AuCentreView();
        var p0 = bone.localPosition; var r0 = bone.localRotation; var s0 = bone.localScale;
        if (Check(address + ": the move arrow can be grabbed", AuAxisGrab(HandleKind.MoveAxis, out var mp, out var md, out int ma), "pivot " + (Vector2)AuGizmo.ToPanel(AuGizmo.PivotWorld) + ", viewport " + AuUi.Viewport.Element.worldBound))
        {
            int u = AuUndo;
            var w0 = bone.position;
            yield return AuDrag(mp, md * 30);
            var v = AuBlock(auA).Bones.TryGetValue(address, out var c1) ? c1 : null;
            Check(address + " move: one entry «Bone correction»", AuUndo == u + 1 && AuTop == OpLabels.BoneCorrection, (AuUndo - u) + " / " + AuTop);
            if (Check(address + " move: stored", v != null && !v.IsPosIdentity, v != null ? AuV(v.Pos) : "none"))
            {
                Check(address + " move: local = base + pos (1e-4)", Dist(bone.localPosition, p0 + AuU(v.Pos)) <= 1e-4, Dist(bone.localPosition, p0 + AuU(v.Pos)).ToString("G3"));
                Check(address + " move: only the position channel", v.IsRotIdentity && v.IsScaleIdentity);
                AuRender();
                var axisWorld = AuGizmo.Axes * (ma == 0 ? Vector3.right : ma == 1 ? Vector3.up : Vector3.forward);
                var moved = bone.position - w0;
                Check(address + " move: the bone went along the grabbed axis", Vector3.Cross(moved.normalized, axisWorld.normalized).magnitude <= 0.01f, moved.ToString("F5") + " / " + axisWorld.ToString("F3"));
            }
            yield return AuUndoStep();
            Check(address + " move: undo: no entry, the bone back (1e-5)", !AuBlock(auA).Bones.ContainsKey(address) && Dist(bone.localPosition, p0) <= 1e-5, Dist(bone.localPosition, p0).ToString("G3"));
            yield return AuRedoStep();
            Check(address + " move: redo: the value again", AuBlock(auA).Bones.TryGetValue(address, out var c2) && v != null && AuSame(c2.Pos, v.Pos, 0f));
            yield return AuUndoStep();

            // a press that moves nothing
            u = AuUndo;
            yield return AuDrag(mp, Vector2.zero, 1);
            Check(address + " a press that moves nothing: no entry, no value", AuUndo == u && !AuBlock(auA).Bones.ContainsKey(address), (AuUndo - u).ToString());

            // Esc
            u = AuUndo;
            int cancels = AuGizmo.Cancels;
            yield return AuDrag(mp, md * 30, 4, true);
            Check(address + " Esc during a drag: no entry, no value, the bone back", AuUndo == u && !AuBlock(auA).Bones.ContainsKey(address) && Dist(bone.localPosition, p0) <= 1e-5 && !AuGizmo.Dragging, (AuUndo - u) + " entries, " + Dist(bone.localPosition, p0).ToString("G3"));
            Check(address + " Esc: the drag counts as cancelled", AuGizmo.Cancels == cancels + 1, (AuGizmo.Cancels - cancels).ToString());
        }

        // Rotate
        yield return AuOkCall(new { cmd = "ui.action", action = "tool.rotate" }, "the Rotate tool");
        yield return AuFrames(2);
        AuRender();
        if (Check(address + ": a ring can be grabbed", AuRingGrab(out var rp, out var rd, out _)))
        {
            int u = AuUndo;
            yield return AuDrag(rp, rd * 30);
            var v = AuBlock(auA).Bones.TryGetValue(address, out var c1) ? c1 : null;
            Check(address + " rotate: one entry «Bone correction»", AuUndo == u + 1 && AuTop == OpLabels.BoneCorrection, (AuUndo - u) + " / " + AuTop);
            if (Check(address + " rotate: stored", v != null && !v.IsRotIdentity && v.IsPosIdentity && v.IsScaleIdentity, v != null ? AuV(v.Rot) : "none"))
            {
                var want = r0 * Quaternion.Euler(AuU(v.Rot));
                Check(address + " rotate: local = base * Euler(rot) (0.01 deg)", Angle(bone.localRotation, want) <= 0.01, Angle(bone.localRotation, want).ToString("G3"));
                if (fk) Check(address + " rotate: the FK value untouched", AuFkValue(auA, address) is Vec3 fv && fkBefore is Vec3 fb && AuSame(fv, fb, 0f));
            }
            yield return AuUndoStep();
            Check(address + " rotate: undo: the bone back (0.01 deg)", !AuBlock(auA).Bones.ContainsKey(address) && Angle(bone.localRotation, r0) <= 0.01, Angle(bone.localRotation, r0).ToString("G3"));
            u = AuUndo;
            yield return AuDrag(rp, rd * 30, 4, true);
            Check(address + " rotate, Esc: no entry, the bone back", AuUndo == u && !AuBlock(auA).Bones.ContainsKey(address) && Angle(bone.localRotation, r0) <= 0.01);
        }

        // Scale, an axis and all three
        yield return AuOkCall(new { cmd = "ui.action", action = "tool.scale" }, "the Scale tool");
        yield return AuFrames(2);
        AuRender();
        if (Check(address + ": a scale arm can be grabbed", AuAxisGrab(HandleKind.ScaleAxis, out var sp, out var sd, out int sa)))
        {
            int u = AuUndo;
            yield return AuDrag(sp, sd * 20);
            var v = AuBlock(auA).Bones.TryGetValue(address, out var c1) ? c1 : null;
            Check(address + " scale: one entry «Bone correction»", AuUndo == u + 1 && AuTop == OpLabels.BoneCorrection, (AuUndo - u) + " / " + AuTop);
            if (Check(address + " scale: stored on the grabbed axis only", v != null && v.IsPosIdentity && v.IsRotIdentity && AuU(v.Scale)[sa] != 1f && AuU(v.Scale)[(sa + 1) % 3] == 1f && AuU(v.Scale)[(sa + 2) % 3] == 1f, v != null ? AuV(v.Scale) + " axis " + sa : "none"))
            {
                var want = Vector3.Scale(s0, AuU(v.Scale));
                Check(address + " scale: local = base x scale (1e-4)", Dist(bone.localScale, want) <= 1e-4, Dist(bone.localScale, want).ToString("G3"));
            }
            yield return AuUndoStep();
            Check(address + " scale: undo: the bone back", !AuBlock(auA).Bones.ContainsKey(address) && Dist(bone.localScale, s0) <= 1e-5);
            // a drag far beyond the limit stops at it
            yield return AuDrag(sp, -sd * 2000);
            var low = AuBlock(auA).Bones.TryGetValue(address, out var c3) ? c3 : null;
            Check(address + " scale: not below 0.001", low != null && AuU(low.Scale)[sa] >= AdvancedPoseRanges.MinScale - 1e-6f, low != null ? AuV(low.Scale) : "none");
            yield return AuUndoStep();
        }
        Check(address + ": nothing left", !AuBlock(auA).Bones.ContainsKey(address));
    }

    // the FK handle on a corrected bone edits the FK value and shows it under the correction
    static IEnumerator AuFkHandle()
    {
        var bone = AuBone(auA, AuArm);
        var corr = new Vector3(5f, -7f, 11f);
        yield return AuOkCall(new { cmd = "pose.bones.set", key = auA, bone = AuArm, rot = new[] { corr.x, corr.y, corr.z } }, "a rotation correction on the arm");
        yield return AuFrames(2);
        var fk0 = AuFkValue(auA, AuArm);
        Check("the shown local is FK * correction (0.01 deg)", fk0 is Vec3 f0 && Angle(bone.localRotation, Quaternion.Euler(AuU(f0)) * Quaternion.Euler(corr)) <= 0.01);
        yield return AuOkCall(new { cmd = "ui.action", action = "tool.rotate" }, "the Rotate tool");
        yield return AuOkCall(new { cmd = "select", sub = auA + "/fk/" + AuArm }, "select the FK arm handle");
        yield return AuFrames(3);
        yield return AuCentreView();
        if (Check("an FK ring can be grabbed", AuRingGrab(out var p, out var d, out _)))
        {
            int u = AuUndo;
            yield return AuDrag(p, d * 30);
            var fk1 = AuFkValue(auA, AuArm);
            Check("the FK handle: one entry «FK bones»", AuUndo == u + 1 && AuTop == OpLabels.FkBones, (AuUndo - u) + " / " + AuTop);
            Check("the FK value changed", fk1 is Vec3 a && fk0 is Vec3 b && !AuSame(a, b, 1e-3f));
            Check("the correction did not change", AuBlock(auA).Bones.TryGetValue(AuArm, out var c) && AuSame(c.Rot, new Vec3(corr.x, corr.y, corr.z), 0f));
            Check("the shown local is the new FK * correction (0.01 deg): the delta is not added twice", fk1 is Vec3 f1 && Angle(bone.localRotation, Quaternion.Euler(AuU(f1)) * Quaternion.Euler(corr)) <= 0.01, fk1 is Vec3 f2 ? Angle(bone.localRotation, Quaternion.Euler(AuU(f2)) * Quaternion.Euler(corr)).ToString("G3") : "");
            yield return AuUndoStep();

            // Esc during a drag, a move after it, the release
            int u2 = AuUndo, c2 = AuGizmo.Cancels;
            var fkB = AuFkValue(auA, AuArm);
            yield return AuDrag(p, d * 30, 4, true);
            var fkA = AuFkValue(auA, AuArm);
            Check("the FK handle, Esc with a move after it: no entry, the FK value as at the press (0.01 deg), the drag cancelled", AuUndo == u2 && fkA is Vec3 x && fkB is Vec3 y && AuSame(x, y, 0.01f) && !AuGizmo.Dragging && AuGizmo.Cancels == c2 + 1, (AuUndo - u2) + " entries, cancels +" + (AuGizmo.Cancels - c2));
            while (AuUndo > u2) yield return AuUndoStep();
        }
        yield return AuOkCall(new { cmd = "pose.bones.clear", key = auA, all = true }, "clear");
    }

    // ------------------------------------------------------------ AU5

    static IEnumerator AU5()
    {
        yield return AuOkCall(new { cmd = "scene.pause", paused = true }, "the global pause");
        yield return AuOpenTab(auA);
        var tab = AuTab;
        if (!Check("the tab is open", tab != null)) yield break;
        yield return AuSelectBone(auA, AuFinger);
        yield return AuFrames(1);
        var bone = AuBone(auA, AuFinger);
        var p0 = bone.localPosition;

        // typing
        int u = AuUndo;
        tab.PosRow.Field.Fields[0].value = 0.01f;
        yield return AuFrames(2);
        var v = AuBlock(auA).Bones.TryGetValue(AuFinger, out var c1) ? c1 : null;
        Check("typing a position: stored, the other channels identity", v != null && AuSame(v.Pos, new Vec3(0.01f, 0, 0), 1e-6f) && v.IsRotIdentity && v.IsScaleIdentity, v != null ? AuV(v.Pos) : "none");
        Check("typing a position: one entry «Bone correction»", AuUndo == u + 1 && AuTop == OpLabels.BoneCorrection, (AuUndo - u) + " / " + AuTop);
        Check("typing a position: shown at once", Dist(bone.localPosition, p0 + new Vector3(0.01f, 0, 0)) <= 1e-5, Dist(bone.localPosition, p0 + new Vector3(0.01f, 0, 0)).ToString("G3"));
        Check("the mark and Reset follow", (AuQ("adv-bone-reset") as Button)?.enabledSelf == true && AuVisible(AuQ("adv-row-" + AuFinger)?.Q("adv-row-corrected")) == AuVisible(AuQ("adv-row-" + AuFinger)));
        tab.PosRow.Field.Fields[1].value = 0.02f;
        yield return AuFrames(2);
        Check("typing another component of the channel chains: still one entry", AuUndo == u + 1, (AuUndo - u).ToString());
        Check("both components stored", AuBlock(auA).Bones.TryGetValue(AuFinger, out var c2) && AuSame(c2.Pos, new Vec3(0.01f, 0.02f, 0), 1e-6f));

        // the ranges of the row
        tab.PosRow.Field.Fields[2].value = 5000f;
        yield return AuFrames(2);
        Check("a typed position stops at 1000", AuBlock(auA).Bones.TryGetValue(AuFinger, out var c3) && c3.Pos.Z == 1000f, AuV(c3?.Pos ?? Vec3.Zero));
        tab.PosRow.Field.Fields[2].value = 0f;
        tab.ScaleRow.Field.Fields[0].value = 0f;
        yield return AuFrames(2);
        Check("a typed scale stops at 0.001", AuBlock(auA).Bones.TryGetValue(AuFinger, out var c4) && Mathf.Abs(c4.Scale.X - 0.001f) < 1e-6f, AuV(c4?.Scale ?? Vec3.One));
        tab.ScaleRow.Field.Fields[0].value = 1f;
        yield return AuFrames(2);

        // identity removes the entry
        tab.PosRow.Field.Fields[0].value = 0f;
        tab.PosRow.Field.Fields[1].value = 0f;
        yield return AuFrames(2);
        Check("an identity value leaves the map", !AuBlock(auA).Bones.ContainsKey(AuFinger) && AuBlock(auA).IsEmpty);
        Check("no entry: the bone back, Reset off", Dist(bone.localPosition, p0) <= 1e-5 && (AuQ("adv-bone-reset") as Button)?.enabledSelf == false);

        // a label drag: one entry; Esc
        yield return AuFrames(2);
        var label = tab.RotRow.Field.Fields[1].labelElement;
        yield return AuReveal(label);
        if (Check("the rotation's Y label is on screen", label != null && label.worldBound.width > 0, label?.worldBound.ToString()))
        {
            // the press and the release are real; ui.input's moves carry no delta, so the dragged values are given to the field
            var field = tab.RotRow.Field.Fields[1];
            Vector2 at = label.worldBound.center;
            u = AuUndo;
            yield return AuCall(new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(AuPointer(at, "down")), ["frames"] = 1 });
            field.value = 12f;
            yield return AuFrames(1);
            field.value = 15f;
            yield return AuFrames(1);
            yield return AuCall(new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(AuPointer(at, "up")), ["frames"] = 2 });
            Check("a label drag: one entry «Bone correction»", AuUndo == u + 1 && AuTop == OpLabels.BoneCorrection, (AuUndo - u) + " / " + AuTop);
            Check("a label drag: the last value", AuBlock(auA).Bones.TryGetValue(AuFinger, out var c5) && c5.Rot.Y == 15f, AuV(c5?.Rot ?? Vec3.Zero));
            yield return AuUndoStep();
            Check("undo: no value", !AuBlock(auA).Bones.ContainsKey(AuFinger));
            u = AuUndo;
            yield return AuCall(new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(AuPointer(at, "down")), ["frames"] = 1 });
            field.value = 12f;
            yield return AuFrames(1);
            yield return AuKey("Escape");
            // a pointer still captured may move on: those moves write nothing (Esc may also have ended the drag: no more moves come)
            bool held = label.HasPointerCapture(PointerId.mousePointerId);
            yield return AuCall(new { cmd = "ui.state" });
            Info("after Esc the label holds the pointer: " + held + "; focus " + auLast?["focus"] + "; keys " + auLast?["keys"]?.ToString(Formatting.None));
            if (held) field.value = 20f;
            yield return AuFrames(1);
            yield return AuCall(new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(AuPointer(at, "up")), ["frames"] = 2 });
            yield return AuFrames(2);
            Check("a label drag, Esc: no entry, no value; its last moves write nothing", AuUndo == u && !AuBlock(auA).Bones.ContainsKey(AuFinger), (AuUndo - u).ToString());
            Check("a label drag, Esc: the field shows the value again", field.value == 0f, field.value.ToString());
        }

        // Reset, one bone
        yield return AuOkCall(new { cmd = "pose.bones.set", key = auA, bone = AuFinger, pos = new[] { 0.01, 0, 0 } }, "a correction");
        yield return AuFrames(2);
        u = AuUndo;
        yield return AuClick("adv-bone-reset");
        Check("Reset: the entry gone, one entry «Clear bone corrections»", !AuBlock(auA).Bones.ContainsKey(AuFinger) && AuUndo == u + 1 && AuTop == OpLabels.ClearBoneCorrections, (AuUndo - u) + " / " + AuTop);

        // Reset all
        yield return AuOkCall(new { cmd = "pose.bones.set", key = auA, bones = new Dictionary<string, object> { [AuFinger] = new { rot = new[] { 0, 0, 10 } }, ["cf_J_Spine02"] = new { pos = new[] { 0.01, 0, 0 } } } }, "two corrections");
        yield return AuFrames(2);
        u = AuUndo;
        yield return AuClick("adv-reset-all");
        Check("Reset all: nothing left, one entry «Clear bone corrections»", AuBlock(auA).Bones.Count == 0 && AuUndo == u + 1 && AuTop == OpLabels.ClearBoneCorrections, (AuUndo - u) + " / " + AuTop);
        yield return AuUndoStep();
        Check("undo brings both back", AuBlock(auA).Bones.Count == 2);
        yield return AuOkCall(new { cmd = "pose.bones.clear", key = auA, all = true }, "clear");

        // a stale entry: listed, greyed, Remove
        yield return AuOkCall(new { cmd = "pose.bones.set", key = auA, bone = "no_such_bone_here", rot = new[] { 0, 0, 5 }, keepMissing = true }, "a correction of a name this character lacks");
        yield return AuFrames(2);
        tab.Refresh();
        Check("the entry is listed with the count", AuVisible(AuQ("adv-stale")) && (AuQ("adv-stale-title") as Label)?.text == L10n.Format("inspector.chara.adv.stale", 1), (AuQ("adv-stale-title") as Label)?.text);
        u = AuUndo;
        yield return AuClick("adv-stale-remove-0");
        tab.Refresh();
        Check("Remove: the entry gone, one entry", AuBlock(auA).Bones.Count == 0 && AuUndo == u + 1 && !AuVisible(AuQ("adv-stale")), (AuUndo - u).ToString());

        // a file's key that is no address (a raw key in the map)
        AuUi.Session.Set(auA, AdvancedPosePaths.Bones, Map(("hair:9/x", new BoneCorrection(Vec3.Zero, new Vec3(0, 0, 5), Vec3.One))));
        AuApply();
        yield return AuFrames(2);
        tab.Refresh();
        Check("a key that is no address is listed as it stands", tab.BoneStale.Shown.Contains("hair:9/x"), string.Join(", ", tab.BoneStale.Shown));
        if (tab.BoneStale.Shown.Contains("hair:9/x"))
        {
            yield return AuClick("adv-stale-remove-0");
            tab.Refresh();
            Check("its Remove takes it away", AuBlock(auA).Bones.Count == 0, string.Join(", ", AuBlock(auA).Bones.Keys));
        }
        yield return AuOkCall(new { cmd = "select", keys = new[] { auA } }, "select the character alone");
        yield return AuOkCall(new { cmd = "scene.pause", paused = false }, "the pause off");
    }

    // ------------------------------------------------------------ AU6

    static Dictionary<string, Vector2> AuPickPoints()
    {
        var l = new List<Dictionary<string, object>>();
        AuGizmo.BoneHandleStates(l);
        var d = new Dictionary<string, Vector2>();
        foreach (var h in l)
        {
            if ((string)h["kind"] != "bone") continue;
            var p = (double[])h["panel"];
            d[(string)h["sub"]] = new Vector2((float)p[0], (float)p[1]);
        }
        return d;
    }

    static IEnumerator AU6()
    {
        yield return AuOkCall(new { cmd = "scene.pause", paused = true }, "the global pause");
        yield return AuOpenTab(auA);
        var tab = AuTab;
        if (!Check("the tab is open", tab != null)) yield break;
        yield return AuOkCall(new { cmd = "ui.action", action = "camera.frame" }, "frame the character");
        yield return AuOkCall(new { cmd = "ui.action", action = "tool.select" }, "the Select tool");
        yield return AuFrames(5);
        var gz = AuGizmo;
        var ui = AuUi;
        tab.SetPart(AdvancedPoseResolver.BodyPart);
        var neck = AuBone(auA, "cf_J_Neck");
        Vector2 at = gz.ToPanel(neck.position);

        // off: no sphere, no hit test, no extra handle
        Check("off by default", !tab.PickOn && (AuQ("adv-pick") as Toggle)?.value == false);
        AuRender();
        int tests = gz.BonePickHitTests;
        var h0 = gz.HitTest(at);
        Check("off: nothing drawn", gz.BonePicksDrawn == 0, gz.BonePicksDrawn.ToString());
        Check("off: no hit test of the pick, no handle at the bone", gz.BonePickHitTests == tests && h0.Kind == HandleKind.None, h0 + ", tests " + (gz.BonePickHitTests - tests));
        Check("off: gizmo.handles lists no sphere", AuPickPoints().Count == 0);

        // on: spheres, a click selects, the tree follows
        (AuQ("adv-pick") as Toggle).value = true;
        yield return AuFrames(3);
        Check("on: the toggle turns it on", tab.PickOn);
        AuRender();
        int drawn = gz.BonePicksDrawn;
        Check("on: a sphere per row the tree lists", drawn > 0 && drawn == tab.VisibleRows.Count(i => !tab.PartRows[i].Protected) && drawn <= AdvancedPoseTab.MaxPicks, drawn + " / " + tab.VisibleRows.Count);
        Measure("AU6: " + drawn + " spheres for " + tab.VisibleRows.Count + " rows listed");
        var pts = AuPickPoints();
        Check("on: gizmo.handles lists them", pts.Count > 0 && pts.Count <= drawn, pts.Count + " / " + drawn);
        var vb = ui.Viewport.Element.worldBound;
        // a sphere nothing else lies on
        string target = null; Vector2 tp = default;
        foreach (var kv in pts)
        {
            if (!vb.Contains(kv.Value) || kv.Key == ui.SubSelection) continue;
            var h = gz.HitTest(kv.Value);
            if (h.Kind == HandleKind.Sub && h.Sub == kv.Key) { target = kv.Key; tp = kv.Value; break; }
        }
        if (Check("a sphere the hit test finds", target != null, pts.Count + " points"))
        {
            int u = AuUndo;
            yield return AuCall(new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(AuPointer(tp, "click")), ["frames"] = 2 });
            yield return AuFrames(3);
            Check("a click selects that bone", ui.SubSelection == target, ui.SubSelection + " / " + target);
            Check("a click records nothing", AuUndo == u, (AuUndo - u).ToString());
            StudioOps.TryParseSub(target, out _, out _, out var nm);
            Check("the tree holds the row selected", tab.SelectedBone == nm && tab.Tree.selectedIndex >= 0 && tab.PartRows[tab.VisibleRows[tab.Tree.selectedIndex]].Row.Address == nm, tab.SelectedBone);
        }

        // a corrected bone of another part stands among them
        yield return AuOkCall(new { cmd = "pose.bones.set", key = auA, bone = "cf_J_MouthMove", pos = new[] { 0.001, 0, 0 } }, "a correction on a head bone");
        yield return AuFrames(3);
        Check("every corrected bone has its sphere, whatever the part", tab.PickRows.Any(b => b.Address == "cf_J_MouthMove" && b.Corrected) && tab.Part == AdvancedPoseResolver.BodyPart);
        yield return AuOkCall(new { cmd = "pose.bones.clear", key = auA, all = true }, "clear");
        yield return AuOkCall(new { cmd = "select", keys = new[] { auA } }, "select the character alone");
        yield return AuFrames(2);

        // FK handles first, IK handles first
        yield return AuOkCall(new { cmd = "fk.enable", key = auA, on = true }, "FK on");
        yield return AuOkCall(new { cmd = "fk.enable", key = auA, groups = new[] { "Body", "Neck" }, on = true }, "FK groups Body and Neck");
        yield return AuFrames(3);
        AuInspector.Refresh();
        yield return AuFrames(2);
        if (AuTab != tab || !tab.PickOn) { tab = AuTab; (AuQ("adv-pick") as Toggle).value = true; yield return AuFrames(2); }
        at = gz.ToPanel(neck.position);
        var hk = gz.HitTest(at);
        Check("an FK handle on the same spot wins", hk.Kind == HandleKind.Sub && hk.Sub == auA + "/fk/cf_J_Neck", hk.ToString());
        ui.Options.Set(OptionDefs.ViewKinematicHandles, false);
        yield return AuFrames(2);
        AuRender();
        Check("view.kinematicHandles off: no sphere either", gz.BonePicksDrawn == 0 && gz.HitTest(at).Kind == HandleKind.None, gz.BonePicksDrawn + ", " + gz.HitTest(at));
        ui.Options.Set(OptionDefs.ViewKinematicHandles, true);
        yield return AuOkCall(new { cmd = "fk.enable", key = auA, on = false }, "FK off");
        yield return AuOkCall(new { cmd = "ik.enable", key = auA, on = true }, "IK on");
        yield return AuFrames(5);
        AuInspector.Refresh();
        yield return AuFrames(2);
        if (AuTab != tab || !tab.PickOn) { tab = AuTab; (AuQ("adv-pick") as Toggle).value = true; yield return AuFrames(2); }
        // the row of a bone the solve owns shows the IK mark
        {
            var owned = IkDriver.Of(AuReal(auA)).Owned.FirstOrDefault(x => x && AuReal(auA).Context.BodyBones.TryGetValue(x.name, out var known) && known == x);
            if (Check("the solve owns a body bone", owned != null))
            {
                tab.SetQuery(owned.name);
                yield return AuFrames(3);
                tab.Refresh();
                yield return AuFrames(2);
                var mark = AuQ("adv-row-" + owned.name)?.Q<Label>("adv-row-ik");
                Check("its row shows the IK mark", mark != null && mark.resolvedStyle.display == DisplayStyle.Flex && mark.tooltip == L10n.Get("inspector.chara.adv.ikTip"), owned.name + ": " + mark?.tooltip);
                tab.SetQuery("");
                yield return AuFrames(1);
            }
        }
        var ikDriver = IkDriver.Of(AuReal(auA));
        var ikHand = ikDriver.Effective(HS2Next.Motion.Ik.IkTargetId.LeftHand);
        var ikPos = AuReal(auA).Root.transform.TransformPoint(ikHand.Pos);
        var hi = gz.HitTest(gz.ToPanel(ikPos));
        Check("an IK handle wins", hi.Kind == HandleKind.Sub && hi.Sub.Contains("/ik/"), hi.ToString());
        yield return AuOkCall(new { cmd = "ik.enable", key = auA, on = false }, "IK off");
        yield return AuFrames(3);
        AuInspector.Refresh();
        yield return AuFrames(2);
        if (AuTab != tab || !tab.PickOn) { tab = AuTab; (AuQ("adv-pick") as Toggle).value = true; yield return AuFrames(2); }

        // at most 256
        foreach (var r in tab.PartRows.ToList()) if (r.HasChildren) tab.SetExpanded(r.Row.Address, true);
        yield return AuFrames(3);
        AuRender();
        Check("every row open: at most 256 spheres", tab.VisibleRows.Count > AdvancedPoseTab.MaxPicks && gz.BonePicksDrawn == AdvancedPoseTab.MaxPicks, gz.BonePicksDrawn + " for " + tab.VisibleRows.Count + " rows");
        foreach (var r in tab.PartRows.ToList()) if (r.HasChildren) tab.SetExpanded(r.Row.Address, false);

        // off when the tab closes
        yield return AuClick("tab-anime");
        yield return AuFrames(2);
        AuRender();
        Check("the tab closes: the pick is off, nothing drawn", AuTab == null && !tab.PickOn && gz.BonePicksDrawn == 0, gz.BonePicksDrawn.ToString());
        tests = gz.BonePickHitTests;
        gz.HitTest(at);
        Check("the tab closed: no hit test of the pick", gz.BonePickHitTests == tests);
        yield return AuClick("tab-" + AdvancedPoseTab.TabId);
        Check("back on the tab: the pick stays off", AuTab != null && !AuTab.PickOn && (AuQ("adv-pick") as Toggle)?.value == false);
        yield return AuOkCall(new { cmd = "ui.action", action = "tool.move" }, "the Move tool");
        yield return AuOkCall(new { cmd = "scene.pause", paused = false }, "the pause off");
    }

    // ------------------------------------------------------------ AU7

    static JObject AuChainReply(string address) => (auLast?["chains"] as JArray)?.OfType<JObject>().FirstOrDefault(c => (string)c["chain"] == address);

    static IEnumerator AU7()
    {
        yield return AuOpenTab(auA);
        var tab = AuTab;
        if (!Check("the tab is open", tab != null)) yield break;
        var fold = AuQ("adv-section-jiggle") as Foldout;
        fold.value = true;
        yield return AuFrames(3);
        tab.Refresh();
        yield return AuCall(new { cmd = "pose.chains", key = auA });
        var chains = (auLast["chains"] as JArray)?.OfType<JObject>().ToList();
        Check("the chains as pose.chains lists them", chains != null && tab.ChainRows.Count == chains.Count && chains.Select((c, i) => (string)c["chain"] == tab.ChainRows[i].Address).All(x => x), tab.ChainRows.Count + " / " + chains?.Count);
        if (!Check("BR-Chan has swing chains", tab.ChainRows.Count > 0)) yield break;
        Measure("AU7: " + tab.ChainRows.Count + " chains");
        var address = tab.ChainRows[0].Address;
        var baseDamping = (float)AuChainReply(address)["base"]["damping"];
        yield return AuClick("adv-chain-0");
        Check("a click selects the chain, its rows show", tab.SelectedChain == address && AuVisible(AuQ("adv-chain-editor")) && tab.ChainMembers.Count == 10, tab.SelectedChain);
        var damping = tab.ChainMembers.First(m => m.name.EndsWith(".damping", StringComparison.Ordinal));
        Check("no scene value: the row shows the card's, no reset", Mathf.Abs((float)damping.Number.Value - baseDamping) < 1e-5f && !AuVisible(damping.Reset) && damping.StoredValue == null, damping.Number.Value + " / " + baseDamping);

        // a drag: one entry
        int u = AuUndo;
        damping.Number.Begin();
        damping.Number.Raise(HS2Next.UI.Widgets.EditPhase.Preview, 0.2);
        yield return AuFrames(1);
        damping.Number.Raise(HS2Next.UI.Widgets.EditPhase.Preview, 0.35);
        yield return AuFrames(1);
        damping.Number.End();
        yield return AuFrames(2);
        Check("a drag: one entry «Jiggle values»", AuUndo == u + 1 && AuTop == OpLabels.JiggleValues, (AuUndo - u) + " / " + AuTop);
        Check("the model holds the last value", AuBlock(auA).Chains.TryGetValue(address, out var v1) && v1.Damping == 0.35f, v1?.Damping?.ToString());
        yield return AuCall(new { cmd = "pose.chains", key = auA });
        var rep1 = AuChainReply(address);
        Check("pose.chains: values = override, base = the card's", Mathf.Abs((float)rep1["values"]["damping"] - 0.35f) < 1e-5f && Mathf.Abs((float)rep1["base"]["damping"] - baseDamping) < 1e-5f, Short(rep1.ToString(Formatting.None)));
        Check("the row: the value, its reset, the card's value in the tooltip", Mathf.Abs((float)damping.Number.Value - 0.35f) < 1e-5f && AuVisible(damping.Reset) && damping.tooltip.Contains(baseDamping.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)), damping.tooltip);

        // the other kinds of row
        var enabled = tab.ChainMembers.First(m => m.name.EndsWith(".enabled", StringComparison.Ordinal));
        u = AuUndo;
        enabled.Check.value = !enabled.Check.value;
        yield return AuFrames(2);
        Check("a toggle: one entry, stored", AuUndo == u + 1 && AuBlock(auA).Chains[address].Enabled.HasValue, (AuUndo - u).ToString());
        enabled.ResetMember();
        yield return AuFrames(2);
        Check("its reset takes the member away", !AuBlock(auA).Chains[address].Enabled.HasValue);
        var freeze = tab.ChainMembers.First(m => m.name.EndsWith(".freezeAxis", StringComparison.Ordinal));
        freeze.Choice.index = 2;
        yield return AuFrames(2);
        Check("a choice: stored", AuBlock(auA).Chains[address].FreezeAxis == 2, AuBlock(auA).Chains[address].FreezeAxis?.ToString());
        freeze.ResetMember();
        var gravity = tab.ChainMembers.First(m => m.name.EndsWith(".gravity", StringComparison.Ordinal));
        gravity.Vector.Fields[1].value = -0.05f;
        yield return AuFrames(2);
        Check("a vector: stored", AuBlock(auA).Chains[address].Gravity is Vec3 g && Mathf.Abs(g.Y + 0.05f) < 1e-6f, AuBlock(auA).Chains[address].Gravity?.ToString());
        gravity.ResetMember();
        yield return AuFrames(2);

        // a vector row's label drag: one entry; Esc takes it back
        yield return AuReveal(gravity);
        var gx = gravity.Vector.Fields[0];
        Vector2 gat = gx.labelElement.worldBound.center;
        u = AuUndo;
        yield return AuCall(new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(AuPointer(gat, "down")), ["frames"] = 1 });
        gx.value = 0.01f;
        yield return AuFrames(1);
        gx.value = 0.02f;
        yield return AuFrames(1);
        yield return AuCall(new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(AuPointer(gat, "up")), ["frames"] = 2 });
        Check("a vector row's label drag: one entry, the last value", AuUndo == u + 1 && AuBlock(auA).Chains[address].Gravity is Vec3 g2 && Mathf.Abs(g2.X - 0.02f) < 1e-6f, (AuUndo - u) + " / " + AuBlock(auA).Chains[address].Gravity);
        gravity.ResetMember();
        yield return AuFrames(2);
        u = AuUndo;
        yield return AuCall(new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(AuPointer(gat, "down")), ["frames"] = 1 });
        gx.value = 0.03f;
        yield return AuFrames(1);
        yield return AuKey("Escape");
        if (gx.labelElement.HasPointerCapture(PointerId.mousePointerId)) gx.value = 0.04f;
        yield return AuFrames(1);
        yield return AuCall(new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(AuPointer(gat, "up")), ["frames"] = 2 });
        yield return AuFrames(2);
        Check("a vector row's label drag, Esc: no entry, no value", AuUndo == u && !AuBlock(auA).Chains[address].Gravity.HasValue, (AuUndo - u) + " / " + AuBlock(auA).Chains[address].Gravity);

        // Esc during a drag
        u = AuUndo;
        var before = AuBlock(auA).Chains[address].Damping;
        damping.Number.Begin();
        damping.Number.Raise(HS2Next.UI.Widgets.EditPhase.Preview, 0.8);
        yield return AuFrames(1);
        yield return AuKey("Escape");
        damping.Number.Raise(HS2Next.UI.Widgets.EditPhase.Preview, 0.9);
        damping.Number.End();
        yield return AuFrames(2);
        Check("Esc: no entry, the value back; the drag's last moves write nothing", AuUndo == u && AuBlock(auA).Chains[address].Damping == before, (AuUndo - u) + ", " + AuBlock(auA).Chains[address].Damping);

        // reset: the card's value again
        u = AuUndo;
        yield return AuClickAt(damping.Reset);
        Check("the reset button: the chain has nothing left, one entry", !AuBlock(auA).Chains.ContainsKey(address) && AuUndo == u + 1, (AuUndo - u) + ", " + string.Join(", ", AuBlock(auA).Chains.Keys));
        Check("the row shows the card's value again", Mathf.Abs((float)damping.Number.Value - baseDamping) < 1e-5f && damping.StoredValue == null);
        yield return AuUndoStep();
        Check("undo brings the value back", AuBlock(auA).Chains.TryGetValue(address, out var v2) && v2.Damping == before);
        u = AuUndo;
        yield return AuClick("adv-chains-clear");
        Check("Reset all: nothing left, one entry «Clear jiggle values»", AuBlock(auA).Chains.Count == 0 && AuUndo == u + 1 && AuTop == OpLabels.ClearJiggleValues, (AuUndo - u) + " / " + AuTop);

        // a stale chain
        AuUi.Session.Set(auA, AdvancedPosePaths.Chains, ChainMap(("hair:3/no_such_root", new ChainValues { Damping = 0.5f })));
        AuApply();
        yield return AuFrames(2);
        tab.Refresh();
        Check("a chain this character lacks is listed", tab.ChainStale.Shown.Contains("hair:3/no_such_root"), string.Join(", ", tab.ChainStale.Shown));
        yield return AuClick("adv-chains-stale-remove-0");
        Check("its Remove takes it away", AuBlock(auA).Chains.Count == 0);

        // the bust and hip chains
        foreach (var chain in new[] { "breastL", "hipR" })
        {
            var entry = AuQ("adv-bust-" + chain) as Foldout;
            if (!Check("the entry of " + chain, entry != null)) continue;
            entry.value = true;
            yield return AuFrames(3);
            var rows = tab.BustMembers(chain);
            if (!Check(chain + ": its seven rows", rows != null && rows.Count == 7, rows?.Count.ToString())) continue;
            var d = rows.First(m => m.name.EndsWith(".damping", StringComparison.Ordinal));
            yield return AuCall(new { cmd = "pose.bust", key = auA });
            var baseD = (float)(auLast["chains"] as JArray).OfType<JObject>().First(c => (string)c["chain"] == chain)["base"]["damping"];
            Check(chain + ": the row shows the card's value", Mathf.Abs((float)d.Number.Value - baseD) < 1e-5f && d.StoredValue == null, d.Number.Value + " / " + baseD);
            u = AuUndo;
            d.Number.Begin();
            d.Number.Raise(HS2Next.UI.Widgets.EditPhase.Preview, 0.11);
            yield return AuFrames(1);
            d.Number.Raise(HS2Next.UI.Widgets.EditPhase.Preview, 0.22);
            d.Number.End();
            yield return AuFrames(2);
            Check(chain + ": a drag: one entry, the last value", AuUndo == u + 1 && AuTop == OpLabels.JiggleValues && AuBlock(auA).Bust[BustSet.KindOf(chain)].Damping == 0.22f, (AuUndo - u) + " / " + AuTop);
            yield return AuCall(new { cmd = "pose.bust", key = auA });
            var rb = (auLast["chains"] as JArray).OfType<JObject>().First(c => (string)c["chain"] == chain);
            Check(chain + ": pose.bust: override and values", Mathf.Abs((float)rb["override"]["damping"] - 0.22f) < 1e-5f && Mathf.Abs((float)rb["values"]["damping"] - 0.22f) < 1e-5f, Short(rb.ToString(Formatting.None)));
            d.ResetMember();
            yield return AuFrames(2);
            Check(chain + ": the reset takes it away", AuBlock(auA).Bust[BustSet.KindOf(chain)].IsEmpty && Mathf.Abs((float)d.Number.Value - baseD) < 1e-5f);
            var fg = rows.First(m => m.name.EndsWith(".gravity", StringComparison.Ordinal));
            fg.Vector.Fields[1].value = -0.01f;
            yield return AuFrames(2);
            Check(chain + ": a vector row", AuBlock(auA).Bust[BustSet.KindOf(chain)].Gravity is Vec3 gg && Mathf.Abs(gg.Y + 0.01f) < 1e-6f);
            yield return AuClick("adv-bust-clear");
            Check(chain + ": Reset all", AuBlock(auA).Bust.IsEmpty);
        }
        yield return AuClearAll(auA);
        fold.value = false;
    }

    static SortedDictionary<string, ChainValues> ChainMap((string key, ChainValues v) e) =>
        new SortedDictionary<string, ChainValues>(StringComparer.Ordinal) { [e.key] = e.v };

    // ------------------------------------------------------------ AU8

    static BlendRow AuBlendRow(AdvancedPoseTab tab, string shape) => tab.BlendList.Query<BlendRow>().ToList().FirstOrDefault(r => r.Shape == shape);

    static IEnumerator AU8()
    {
        yield return AuOpenTab(auA);
        var tab = AuTab;
        if (!Check("the tab is open", tab != null)) yield break;
        var fold = AuQ("adv-section-blend") as Foldout;
        fold.value = true;
        yield return AuFrames(3);
        tab.Refresh();
        yield return AuCall(new { cmd = "pose.blendShapes", key = auA });
        var renderers = (auLast["renderers"] as JArray).OfType<JObject>().ToList();
        var open = renderers.Where(r => !r.Value<bool>("protected")).Select(r => (string)r["renderer"]).ToList();
        Check("the renderers offered: those of pose.blendShapes that are not protected", tab.RendererRows.Select(r => r.Address).SequenceEqual(open), tab.RendererRows.Count + " / " + open.Count + " (" + renderers.Count + " in all)");
        Check("no protected renderer is offered", renderers.Where(r => r.Value<bool>("protected")).All(r => tab.RendererRows.All(x => x.Address != (string)r["renderer"])), renderers.Count(r => r.Value<bool>("protected")) + " protected");
        // BR-Chan's protected renderers hold no shapes: a synthetic accessory part brings a member with a shape, and a control beside it
        {
            var ar = AuReal(auA);
            var slot = ar.Context.Accessories.FirstOrDefault(x => x != null && x.Object && ar.AccessoryIdOf(x) != null);
            if (Check("an accessory on the target", slot != null))
            {
                string part = PoseAddressGrammar.PartText(PosePart.Accessory, -1, ar.AccessoryIdOf(slot));
                var top = slot.Object.transform;
                var made = new List<UnityEngine.Object>();
                GameObject Skinned(string rendererName)
                {
                    var go = new GameObject(rendererName);
                    go.transform.SetParent(top, false);
                    var mesh = new Mesh { name = rendererName, vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                    mesh.AddBlendShapeFrame("au_shape", 100f, new[] { Vector3.forward, Vector3.forward, Vector3.forward }, null, null);
                    go.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
                    made.Add(mesh);
                    made.Add(go);
                    return go;
                }
                Skinned("o_dan_f");
                Skinned("au_blend");
                // the lists are read when the parts change: the tab's next build reads these
                AuInspector.Refresh(true);
                yield return AuFrames(3);
                tab = AuTab;
                if (Check("the tab is open again", tab != null))
                {
                    (AuQ("adv-section-blend") as Foldout).value = true;
                    yield return AuFrames(3);
                    tab.Refresh();
                    yield return AuCall(new { cmd = "pose.blendShapes", key = auA, part });
                    var rs = (auLast["renderers"] as JArray).OfType<JObject>().ToList();
                    Check("pose.blendShapes lists the member protected, the control not", rs.Any(x => (string)x["name"] == "o_dan_f" && x.Value<bool>("protected")) && rs.Any(x => (string)x["name"] == "au_blend" && !x.Value<bool>("protected")), rs.Count + " renderers");
                    Check("the member is not offered, the control is", tab.RendererRows.All(x => x.Name != "o_dan_f") && tab.RendererRows.Any(x => x.Name == "au_blend"), string.Join(", ", tab.RendererRows.Select(x => x.Name)));
                }
                foreach (var o in made) if (o) UnityEngine.Object.Destroy(o);
                yield return AuFrames(3);
                AuInspector.Refresh(true);
                yield return AuFrames(3);
                tab = AuTab;
                if (tab != null)
                {
                    fold = AuQ("adv-section-blend") as Foldout;
                    fold.value = true;
                    yield return AuFrames(3);
                    tab.Refresh();
                }
            }
        }
        if (!Check("the tab is open", tab != null)) yield break;
        const string address = "head/o_tooth";
        var field = AuQ("adv-blend-renderer") as DropdownField;
        int ri = tab.RendererRows.ToList().FindIndex(r => r.Address == address);
        if (!Check("o_tooth is offered", ri >= 0 && field != null)) yield break;
        field.value = field.choices[ri];
        yield return AuFrames(3);
        Check("the list changes the renderer", tab.SelectedRenderer == address, tab.SelectedRenderer);
        var renderer = AdvancedPoseParts.Renderer(AuReal(auA), address);
        int count = renderer.sharedMesh.blendShapeCount;
        Check("the shapes of its mesh", tab.ShapeRows.Count == count, tab.ShapeRows.Count + " / " + count);
        int elements = tab.BlendList.Query<BlendRow>().ToList().Count;
        Check("the list is virtualised", elements > 0 && elements <= count && elements < 20, elements.ToString());

        // a search
        string shape = tab.ShapeRows[0].Name;
        int index = tab.ShapeRows[0].Index;
        var search = AuQ("adv-blend-search") as TextField;
        search.value = shape;
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => tab.ShapeRows.Count >= 1 && tab.ShapeRows.All(s => SearchText.Normalize(s.Name).Contains(SearchText.Normalize(shape))) && tab.ShapeRows.Count < count, 3, ok);
        Check("a search narrows the shapes", ok[0] || count == 1, tab.ShapeRows.Count + " of " + count);
        search.value = "";
        tab.SetBlendQuery("");
        yield return AuFrames(3);
        var row = AuBlendRow(tab, shape);
        if (!Check("the row of the first shape", row != null)) yield break;
        float start = renderer.GetBlendShapeWeight(index);
        Check("no override: the current weight, no reset", Mathf.Abs((float)row.Slider.Value - start) < 0.01f && !AuVisible(row.Slider.ResetButton) && !AuVisible(row.Q("adv-blend-mark")), row.Slider.Value + " / " + start);

        // a drag: one entry
        int u = AuUndo;
        row.Slider.Begin();
        row.Slider.Raise(HS2Next.UI.Widgets.EditPhase.Preview, 40);
        yield return AuFrames(2);
        row.Slider.Raise(HS2Next.UI.Widgets.EditPhase.Preview, 60);
        row.Slider.End();
        yield return AuFrames(3);
        Check("a drag: one entry «Blend shapes»", AuUndo == u + 1 && AuTop == OpLabels.BlendShapes, (AuUndo - u) + " / " + AuTop);
        Check("the model holds the last value", AuBlock(auA).BlendShapes.TryGetValue(address, out var b1) && b1.Weights.TryGetValue(shape, out float w1) && w1 == 60f);
        Check("the mesh shows it", Mathf.Abs(renderer.GetBlendShapeWeight(index) - 60f) < 0.01f, renderer.GetBlendShapeWeight(index).ToString());
        row = AuBlendRow(tab, shape);
        Check("the row: its mark and its reset", AuVisible(row?.Q("adv-blend-mark")) && AuVisible(row?.Slider.ResetButton));

        // typed
        row.Slider.Field.value = 500f;
        yield return AuFrames(2);
        Check("a typed weight stops at 300", AuBlock(auA).BlendShapes[address].Weights[shape] == 300f, AuBlock(auA).BlendShapes[address].Weights[shape].ToString());
        row.Slider.Field.value = 60f;
        yield return AuFrames(2);

        // Esc
        u = AuUndo;
        row = AuBlendRow(tab, shape);
        row.Slider.Begin();
        row.Slider.Raise(HS2Next.UI.Widgets.EditPhase.Preview, 20);
        yield return AuFrames(2);
        yield return AuKey("Escape");
        row.Slider.Raise(HS2Next.UI.Widgets.EditPhase.Preview, 25);
        row.Slider.End();
        yield return AuFrames(3);
        Check("Esc: no entry, the value back; the drag's last moves write nothing", AuUndo == u && AuBlock(auA).BlendShapes[address].Weights[shape] == 60f, (AuUndo - u) + ", " + AuBlock(auA).BlendShapes[address].Weights[shape]);

        // the reset
        u = AuUndo;
        row = AuBlendRow(tab, shape);
        yield return AuClickAt(row.Slider.ResetButton);
        yield return AuFrames(1);
        Check("the reset: no entry left, one entry «Clear blend shapes»", !AuBlock(auA).BlendShapes.ContainsKey(address) && AuUndo == u + 1 && AuTop == OpLabels.ClearBlendShapes, (AuUndo - u) + " / " + AuTop);
        Check("the mesh's value again", Mathf.Abs(renderer.GetBlendShapeWeight(index) - start) < 0.01f, renderer.GetBlendShapeWeight(index) + " / " + start);

        // Reset all, a stale entry
        yield return AuOkCall(new { cmd = "pose.blendShapes.set", key = auA, renderer = address, shape, weight = 30 }, "a weight");
        yield return AuFrames(2);
        u = AuUndo;
        yield return AuClick("adv-blend-clear");
        Check("Reset all: nothing left, one entry", AuBlock(auA).BlendShapes.Count == 0 && AuUndo == u + 1, (AuUndo - u).ToString());
        yield return AuOkCall(new { cmd = "pose.blendShapes.set", key = auA, renderer = address, shape = "no_such_shape", weight = 30, keepMissing = true }, "a weight of a shape this mesh lacks");
        yield return AuOkCall(new { cmd = "pose.blendShapes.set", key = auA, renderer = "head/no_such_renderer", shape = "x", weight = 30, keepMissing = true }, "a weight of a renderer this character lacks");
        yield return AuFrames(2);
        tab.Refresh();
        Check("both are listed", tab.BlendStale.Shown.Contains("head/no_such_renderer") && tab.BlendStale.Shown.Contains(address + ":no_such_shape"), string.Join(", ", tab.BlendStale.Shown));
        yield return AuClick("adv-blend-stale-remove-0");
        yield return AuClick("adv-blend-stale-remove-0");
        Check("their Remove takes them away", AuBlock(auA).BlendShapes.Count == 0, string.Join(", ", AuBlock(auA).BlendShapes.Keys));

        // the weight follows the face while there is no override
        const string face = "head/o_head";
        ri = tab.RendererRows.ToList().FindIndex(r => r.Address == face);
        if (Check("o_head is offered", ri >= 0))
        {
            field.value = field.choices[ri];
            yield return AuFrames(3);
            var head = AdvancedPoseParts.Renderer(AuReal(auA), face);
            int shapes = head.sharedMesh.blendShapeCount;
            var w0 = new float[shapes];
            for (int k = 0; k < shapes; k++) w0[k] = head.GetBlendShapeWeight(k);
            yield return AuOkCall(new { cmd = "chara.face", key = auA, mouth = 3, mouthOpen = 1 }, "another mouth");
            yield return AuFrames(30);
            // the shape that moved most; the row is read in the frame of its refresh (a blink moves on between frames)
            int moved = 0;
            float most = -1f;
            for (int k = 0; k < shapes; k++)
            {
                float d = Mathf.Abs(head.GetBlendShapeWeight(k) - w0[k]);
                if (d > most) { most = d; moved = k; }
            }
            string movedName = head.sharedMesh.GetBlendShapeName(moved);
            Check("another mouth moves a shape", most > 1f, movedName + " by " + most);
            tab.SetBlendQuery(movedName);
            yield return AuFrames(3);
            tab.Refresh();
            var movedRow = AuBlendRow(tab, movedName);
            float shown = movedRow != null ? (float)movedRow.Slider.Value : float.NaN;
            float now = head.GetBlendShapeWeight(moved);
            Check("the row of that shape shows the current weight", movedRow != null && Mathf.Abs(shown - now) < 0.01f, shown + " / " + now);
            Check("no override on it: no mark, no reset", movedRow != null && !AuVisible(movedRow.Q("adv-blend-mark")) && !AuVisible(movedRow.Slider.ResetButton));
            yield return AuOkCall(new { cmd = "chara.face", key = auA, mouth = 0, mouthOpen = 0 }, "the mouth back");
            yield return AuFrames(30);
            tab.Refresh();
            movedRow = AuBlendRow(tab, movedName);
            float shown2 = movedRow != null ? (float)movedRow.Slider.Value : float.NaN;
            float now2 = head.GetBlendShapeWeight(moved);
            Check("the row follows the face", movedRow != null && Mathf.Abs(shown2 - now2) < 0.01f && Mathf.Abs(shown2 - shown) > 0.5f, shown + " -> " + shown2 + " / " + now2);
            tab.SetBlendQuery("");
        }
        yield return AuClearAll(auA);
        fold.value = false;
    }

    // ------------------------------------------------------------ AU9

    static IEnumerator AU9()
    {
        // every string
        var ta = Resources.Load<TextAsset>(UiHost.StringsResource);
        if (!Check("strings.tsv loads", ta != null)) yield break;
        var table = L10nTable.Parse(ta.text);
        var keys = new List<string>();
        bool inBlock = false;
        foreach (var raw in ta.text.Split('\n'))
        {
            var l = raw.TrimEnd('\r');
            if (l.StartsWith("#")) { inBlock = l.StartsWith("#U63", StringComparison.Ordinal); continue; }
            if (inBlock && l.Length > 0) keys.Add(l.Split('\t')[0]);
        }
        var missing = keys.Where(k => new[] { L10nTable.En, L10nTable.Ja, L10nTable.ZhHans }.Any(lang => string.IsNullOrEmpty(table.Cell(k, lang)))).ToList();
        Check("every key of the block has en, ja and zh-Hans (" + keys.Count + " keys)", keys.Count >= 60 && missing.Count == 0, string.Join(", ", missing.Take(10)));
        Measure("AU9: " + keys.Count + " string keys");
        Check("the hip chains' word", table.Cell("inspector.chara.adv.hipL", L10nTable.Ja) == "尻 左" && table.Cell("inspector.chara.adv.hipR", L10nTable.Ja) == "尻 右");

        // the tab in the three languages
        yield return AuOpenTab(auA);
        foreach (var lang in new[] { L10nTable.Ja, L10nTable.ZhHans, L10nTable.En })
        {
            yield return AuOkCall(new JObject { ["cmd"] = "ui.action", ["action"] = "ui.language", ["args"] = new JObject { ["language"] = lang } }, "language " + lang);
            yield return AuFrames(4);
            Check(lang + ": the tab's title", (AuQ("tab-" + AdvancedPoseTab.TabId) as Button)?.text == table.Cell("inspector.tab.advancedPose", lang), (AuQ("tab-" + AdvancedPoseTab.TabId) as Button)?.text);
            if (AuTab == null) yield return AuOpenTab(auA);
            Check(lang + ": the section's title", (AuQ("adv-section-bones") as Foldout)?.text == table.Cell("inspector.chara.adv.bones", lang), (AuQ("adv-section-bones") as Foldout)?.text);
            Check(lang + ": Reset all", (AuQ("adv-reset-all") as Button)?.text == table.Cell("inspector.chara.adv.resetAll", lang), (AuQ("adv-reset-all") as Button)?.text);
        }

        // a paused scene edits at once; time and animation stay
        yield return AuOkCall(new { cmd = "scene.pause", paused = true }, "the global pause");
        yield return AuFrames(2);
        yield return AuOpenTab(auA);
        var tab = AuTab;
        yield return AuSelectBone(auA, AuFinger);
        yield return AuFrames(1);
        yield return AuCall(new { cmd = "chara.anime", key = auA });
        var time0 = auLast?["time"]?.ToString();
        var bone = AuBone(auA, AuFinger);
        var lr = bone.localRotation;
        tab.RotRow.Field.Fields[2].value = 15f;
        var want = lr * Quaternion.Euler(0, 0, 15);
        Check("paused: the row's value shows at once", Angle(bone.localRotation, want) <= 0.01, Angle(bone.localRotation, want).ToString("G3"));
        yield return AuFrames(5);
        Check("paused: it holds", Angle(bone.localRotation, want) <= 0.01);
        yield return AuCall(new { cmd = "chara.anime", key = auA });
        Check("paused: the animation time stays", auLast?["time"]?.ToString() == time0, time0 + " / " + auLast?["time"]);
        yield return AuClearAll(auA);
        yield return AuOkCall(new { cmd = "scene.pause", paused = false }, "the pause off");

        // the default layout: as before; no action of its own
        AuUi.Shell.ResetLayout();
        yield return AuFrames(3);
        var sh = AuUi.Shell;
        Check("the default layout: left = workspace | inspector, the right and bottom regions as they were", sh.Regions[LayoutRegions.Left].Panels.SequenceEqual(new[] { "workspace", "inspector" }) && sh.Regions[LayoutRegions.Bottom].Collapsed, string.Join(", ", sh.Regions[LayoutRegions.Left].Panels));
        var acts = AuUi.Registry.Actions.All.Where(a => a.Id.IndexOf("advanced", StringComparison.OrdinalIgnoreCase) >= 0 || a.Id.StartsWith("adv", StringComparison.OrdinalIgnoreCase)).Select(a => a.Id).ToList();
        Check("no new action", acts.Count == 0, string.Join(", ", acts));
        yield return AuOkCall(new { cmd = "select", keys = new[] { auA } }, "select the character alone");
        yield return AuOkCall(new { cmd = "ui.panel", panel = "inspector", op = "show" }, "show the inspector");
        yield return AuFrames(2);
        Check("the tab comes back with the inspector", AuInspector?.Chara != null && AuInspector.Chara.TabBodies.ContainsKey(AdvancedPoseTab.TabId));
    }
}

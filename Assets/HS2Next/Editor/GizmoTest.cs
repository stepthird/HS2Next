// GizmoTest.render (package A; graphics): the app with the UI in Editor play mode.
//   R1 the gizmo is visible in the view: the view camera rendered into a texture with gizmos on and off differs in the
//      handle area by more than a threshold (and hardly elsewhere);
//   R2 captures never contain gizmos: ViewCapture (the scene thumbnail / capture path) gives bit-equal PNGs with gizmos
//      on and off (and with a selected node);
//   M  U-M3 UI CPU per frame: idle (default layout), during a synthesized gizmo drag with the inspector open, and while
//      the card grid of add.chara scrolls — the host's own work (UiHost Update + LateUpdate + gizmo build) and the UI
//      Toolkit profiler markers of the frame (ProfilerRecorder over the available UIElements / UIR markers).
// Run: & Tools\unity_batch.ps1 -Method GizmoTest.Run -NoQuit      Report: Spike/out/ui/gizmo_report.txt
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.App;
using HS2Next.App.Commands;
using HS2Next.Content;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Bridge;
using HS2Next.UI.Core;
using HS2Next.UI.Gizmos;
using Newtonsoft.Json.Linq;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.UIElements;

public static class GizmoTest
{
    public const int Port = 17396;
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "ui");
    static string ReportPath => Path.Combine(OutDir, "gizmo_report.txt");
    static string Launch =>
        "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":true,\"language\":\"en\"},\"userData\":\"Spike/out/ui/gizmo_userdata\",\"bridge\":{\"port\":" + Port + ",\"outputDir\":\"Spike/out/ui/gizmo_out\",\"allowQuit\":false}}";

    static readonly StringBuilder rep = new StringBuilder();
    static int pass, fail;
    static void Check(bool ok, string what) { if (ok) pass++; else fail++; rep.AppendLine((ok ? "PASS " : "FAIL ") + what); Debug.Log("GIZMO TEST " + (ok ? "PASS " : "FAIL ") + what); }
    static void Info(string what) { rep.AppendLine("INFO " + what); Debug.Log("GIZMO TEST INFO " + what); }

    public static void Run()
    {
        Directory.CreateDirectory(OutDir);
        string cfg = Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "ui", "gizmo_userdata", "config");
        try { if (Directory.Exists(cfg)) Directory.Delete(cfg, true); } catch (Exception) { }
        File.WriteAllText(ReportPath, "SUMMARY: 0 PASS, 1 FAIL (the play session did not finish)\n", new UTF8Encoding(false));
        PlayModeHarness.Start("GizmoTest.Play", ReportPath, 20, true, Launch);
    }

    static IEnumerator Play()
    {
        rep.Clear(); pass = fail = 0;
        rep.AppendLine($"GizmoTest.render (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}, screen {Screen.width}x{Screen.height})");
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting && UiHost.Current != null && UiHost.Current.IsReady, 240, ok);
        Check(ok[0], "app and UI ready");
        if (!ok[0]) { Finish(); yield break; }
        var host = UiHost.Current;
        var ui = host.Context;
        var e = Inner(host, ui);
        while (true)
        {
            bool more;
            try { more = e.MoveNext(); }
            catch (Exception ex) { Check(false, "threw " + ex); break; }
            if (!more) break;
            yield return e.Current;
        }
        Finish();
    }

    static IEnumerator Inner(UiHost host, UiContext ui)
    {
        ui.Ops.NewScene();
        var r = ui.Ops.AddFolder(null, null, -1, UiContext.User());
        string id = r.NewIds.FirstOrDefault();
        ui.Ops.Select(new[] { id });
        ui.Tool = GizmoTool.Move;
        var cc = ui.Viewport.Camera;
        var at = ui.Model.Find(id)?.Doc.Transform.Pos;   // the folder lands at the view's pivot (Phase 3.6: SessionOptions.DefaultPivot)
        cc.SetView(at != null ? new Vector3(at.Value.X, at.Value.Y, at.Value.Z) : Vector3.zero, new Vector3(20, 30, 0), 10, 23);
        cc.Flush();
        yield return PlayModeHarness.WaitFrames(5);
        var cam = ui.Viewport.ViewCamera;
        // R1: view with and without gizmos (the test lets the pass draw into the target texture)
        host.GizmosIntoTargets = true;
        var on = RenderView(cam, 640, 360);
        ui.Options.Set(OptionDefs.ViewGizmos, false);
        yield return PlayModeHarness.WaitFrames(1);
        var off = RenderView(cam, 640, 360);
        ui.Options.Set(OptionDefs.ViewGizmos, true);
        host.GizmosIntoTargets = false;
        yield return PlayModeHarness.WaitFrames(1);
        int diff = 0, diffCentre = 0;
        int cx = 320, cy = 180, rad = 90;
        for (int y = 0; y < 360; y++)
            for (int x = 0; x < 640; x++)
            {
                var a = on[y * 640 + x]; var b = off[y * 640 + x];
                if (Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b) > 30)
                {
                    diff++;
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) < rad * rad) diffCentre++;
                }
            }
        Info($"R1 pixels differing (> 30 / 765): {diff} in the frame, {diffCentre} within {rad} px of the pivot; pass draws {host.GizmoPass.Count}, recorded {host.GizmoPass.Recorded}");
        Check(diffCentre > 300 && diffCentre >= diff * 0.8, "R1 the gizmo is drawn in the view around the pivot");
        // R2: captures with gizmos on and off: an item beside the pivot gives the capture content; a capture is compared
        // with a second one with gizmos on (determinism baseline) and with one with gizmos off
        var ir = ui.Ops.AddItem(ContentId.Vanilla(821), null, null, -1, UiContext.User());
        if (ir.Ok && ir.NewIds.Count > 0) ui.Ops.SetTransform(ir.NewIds, new Vector3(1.0f, 0, 0), null, null, UiContext.User());
        ui.Ops.Select(new[] { id });
        yield return PlayModeHarness.WaitFrames(10);
        var p1 = ViewCapture.Render(cam, 512, 288);
        yield return PlayModeHarness.WaitFrames(1);
        var p1b = ViewCapture.Render(cam, 512, 288);
        ui.Options.Set(OptionDefs.ViewGizmos, false);
        yield return PlayModeHarness.WaitFrames(1);
        var p2 = ViewCapture.Render(cam, 512, 288);
        ui.Options.Set(OptionDefs.ViewGizmos, true);
        bool same = p1 != null && p2 != null && p1.SequenceEqual(p2);
        int baseDiff = PngDiff(p1, p1b), gizDiff = PngDiff(p1, p2);
        Info($"R2 capture sizes {p1?.Length} / {p1b?.Length} / {p2?.Length} bytes; bit-equal on/off {same}; differing pixels on/on {baseDiff}, on/off {gizDiff}");
        Check(same || gizDiff <= baseDiff + 20, "R2 captures contain no gizmo (bit-equal, or no more difference than two captures with gizmos on)");
        File.WriteAllBytes(Path.Combine(OutDir, "gizmo_capture.png"), p1 ?? new byte[0]);
        File.WriteAllBytes(Path.Combine(OutDir, "gizmo_capture_off.png"), p2 ?? new byte[0]);
        SavePng(on, 640, 360, Path.Combine(OutDir, "gizmo_view_on.png"));
        SavePng(off, 640, 360, Path.Combine(OutDir, "gizmo_view_off.png"));
        yield return PlayModeHarness.WaitFrames(2);

        // M: U-M3
        var all = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(all);
        Info("available marker names with UI / Panel: " + string.Join(", ", all.Select(h => ProfilerRecorderHandle.GetDescription(h).Name).Where(n => n != null && (n.Contains("UI") || n.Contains("Panel"))).Distinct().Take(40)));
        var markers = UiMarkers();
        Info("UI Toolkit markers recorded: " + string.Join(", ", markers.Select(m => m.name)));
        yield return Measure(host, markers, "idle (default layout)", 120, null);
        ui.Shell.FocusPanel("inspector");
        var g = ui.Viewport.Gizmo;
        var holder = ui.Runtime.HolderOf(id);
        float L = ui.Options.Int(OptionDefs.GizmoSize) * g.WorldPerPx(holder.position);
        Vector2 a0 = g.ToPanel(holder.position), aT = g.ToPanel(holder.position + Vector3.right * L);
        Vector2 grab = Vector2.Lerp(a0, aT, 0.6f), dir = (aT - a0).normalized;
        Send(host, "down", grab);
        int k = 0;
        yield return Measure(host, markers, "gizmo drag, inspector open", 120, () => { k++; Send(host, "move", grab + dir * (float)(Math.Sin(k * 0.1) * 40)); });
        Send(host, "up", grab);
        ui.Shell.FocusPanel("add.chara");
        yield return PlayModeHarness.WaitFrames(30);
        var panel = ui.Shell.PanelRoot("add.chara");
        var sv = panel?.Query<ScrollView>().ToList().OrderByDescending(s => s.contentContainer.childCount).FirstOrDefault();
        if (sv != null)
        {
            float y = 0;
            yield return Measure(host, markers, "card grid scrolling", 180, () => { y += 40; sv.scrollOffset = new Vector2(0, y % Math.Max(1, sv.contentContainer.layout.height)); });
        }
        else Info("U-M3 card grid: no ScrollView found in add.chara");
        foreach (var m in markers) m.rec.Dispose();
    }

    static void Send(UiHost h, string phase, Vector2 p) => UiCommands.Send(h, JObject.FromObject(new { type = "pointer", phase, x = p.x, y = p.y, button = 0 }), out _);

    static List<(string name, ProfilerRecorder rec)> UiMarkers()
    {
        var handles = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(handles);
        var l = new List<(string, ProfilerRecorder)>();
        foreach (var h in handles)
        {
            var d = ProfilerRecorderHandle.GetDescription(h);
            string n = d.Name ?? "";
            // top-level UI Toolkit work of a frame (names as listed by ProfilerRecorderHandle.GetAvailable in 6.6)
            if (!(n.StartsWith("UIElements", StringComparison.Ordinal) || n.StartsWith("UIR.", StringComparison.Ordinal) || n.StartsWith("UI.", StringComparison.Ordinal) ||
                  n.Contains("PanelRenderer") || n.Contains("UpdatePanels") || n.Contains("RuntimePanel") || n.Contains("UIToolkit"))) continue;
            l.Add((n, ProfilerRecorder.StartNew(d.Category, n, 4)));
            if (l.Count >= 24) break;
        }
        return l;
    }

    static IEnumerator Measure(UiHost host, List<(string name, ProfilerRecorder rec)> markers, string what, int frames, Action perFrame)
    {
        var hostMs = new List<double>();
        var uiMs = new List<double>();
        var frameMs = new List<double>();
        for (int i = 0; i < frames; i++)
        {
            perFrame?.Invoke();
            yield return PlayModeHarness.WaitFrames(1);
            hostMs.Add(host.LastHostMs);
            double sum = 0;
            foreach (var m in markers) if (m.rec.Valid && m.rec.Count > 0) sum += m.rec.LastValue / 1e6;
            uiMs.Add(sum);
            frameMs.Add(Time.unscaledDeltaTime * 1000.0);
        }
        string S(List<double> l) { var s = l.OrderBy(x => x).ToList(); return $"median {s[s.Count / 2]:0.00}, p95 {s[(int)(s.Count * 0.95)]:0.00}, max {s[s.Count - 1]:0.00}"; }
        Info($"U-M3 {what}: host {S(hostMs)} ms; UI Toolkit markers (sum, may nest) {S(uiMs)} ms; frame {S(frameMs)} ms");
    }

    static Color32[] RenderView(Camera cam, int w, int h)
    {
        var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
        var prevT = cam.targetTexture;
        var prevR = cam.rect;
        cam.targetTexture = rt;
        cam.rect = new Rect(0, 0, 1, 1);
        cam.Render();
        cam.targetTexture = prevT;
        cam.rect = prevR;
        var prevA = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prevA;
        RenderTexture.ReleaseTemporary(rt);
        var px = tex.GetPixels32();
        UnityEngine.Object.Destroy(tex);
        return px;
    }

    static int PngDiff(byte[] a, byte[] b)
    {
        if (a == null || b == null) return int.MaxValue;
        var ta = new Texture2D(2, 2); var tb = new Texture2D(2, 2);
        ta.LoadImage(a); tb.LoadImage(b);
        int n = 0;
        if (ta.width == tb.width && ta.height == tb.height)
        {
            var pa = ta.GetPixels32(); var pb = tb.GetPixels32();
            for (int i = 0; i < pa.Length; i++)
                if (Math.Abs(pa[i].r - pb[i].r) + Math.Abs(pa[i].g - pb[i].g) + Math.Abs(pa[i].b - pb[i].b) > 30) n++;
        }
        else n = int.MaxValue;
        UnityEngine.Object.Destroy(ta); UnityEngine.Object.Destroy(tb);
        return n;
    }

    static void SavePng(Color32[] px, int w, int h, string path)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.SetPixels32(px);
        t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
        UnityEngine.Object.Destroy(t);
    }

    static void Finish()
    {
        rep.Insert(0, $"SUMMARY: {pass} PASS, {fail} FAIL\n");
        File.WriteAllText(ReportPath, rep.ToString(), new UTF8Encoding(false));
        Debug.Log($"GIZMO TEST DONE: {pass} PASS, {fail} FAIL");
        PlayModeHarness.ExitCode = fail > 0 ? 1 : 0;
    }
}

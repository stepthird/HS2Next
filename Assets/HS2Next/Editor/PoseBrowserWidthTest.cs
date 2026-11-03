// PoseBrowserWidthTest.play: the pose browser in a narrow sidebar (after 1.1.0, package P round 2; the treatment of the
// character and list browsers): at 372, 300 and 247 px in Japanese and English, the footer's status line stays inside the
// panel (on a line of its own where the row is too narrow, wrapped), the footer's buttons stay inside it, and the list mode's
// column titles fit their columns or are cut with an ellipsis carrying the full title as a tooltip. Editor play with the
// UI (PlayModeHarness; -nographics works: element state and text measures only, no pixel and no pose image is looked at).
// The app's UserData is a test folder (Spike/out/pose_width/userdata); the pose library is listed read-only; nothing is
// written outside the test folder except the app's caches (pose index, stick figures).
// Run: & Tools\unity_batch.ps1 -Method PoseBrowserWidthTest.Run -NoQuit -NoGraphics
// Report: Spike/out/ui/pose_width_report.txt ("SUMMARY: N PASS, M FAIL").
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.App;
using HS2Next.Content;
using HS2Next.UI;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

public static class PoseBrowserWidthTest
{
    public const int Port = 17652;
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "ui");
    static string ReportPath => Path.Combine(OutDir, "pose_width_report.txt");
    static string UserDataDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "pose_width", "userdata");

    static string Launch =>
        "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":true,\"language\":\"en\"},\"userData\":\"" + UserDataDir.Replace('\\', '/') +
        "\",\"bridge\":{\"port\":" + Port + ",\"outputDir\":\"Spike/out/pose_width/out\",\"allowQuit\":false}}";

    static readonly StringBuilder rep = new StringBuilder();
    static int pass, fail;

    static void Check(bool ok, string what)
    {
        if (ok) pass++; else fail++;
        string l = (ok ? "PASS " : "FAIL ") + what;
        rep.AppendLine(l);
        Debug.Log("POSE WIDTH " + l);
    }

    static void Info(string what) { rep.AppendLine("INFO " + what); Debug.Log("POSE WIDTH INFO " + what); }

    public static void Run()
    {
        Directory.CreateDirectory(OutDir);
        Directory.CreateDirectory(Path.Combine(UserDataDir, "config"));
        rep.Clear(); pass = fail = 0;
        File.WriteAllText(ReportPath, "SUMMARY: 0 PASS, 1 FAIL (the play session did not finish)\n", new UTF8Encoding(false));
        PlayModeHarness.Start("PoseBrowserWidthTest.Play", ReportPath, 15, true, Launch);
    }

    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return PlayModeHarness.WaitFrames(1); }

    static float TextW(Label l) => l.MeasureTextSize(l.text ?? "", 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;

    // null when the label shows its text inside its box and the panel (a wrapping label: its wrapped height fits; a cut column
    // title: the ellipsis and the full text as tooltip), else the problem
    static string FitProblem(Label l, VisualElement panel, bool header)
    {
        var rs = l.resolvedStyle;
        float cw = l.contentRect.width, tw = TextW(l);
        var pb = panel.worldBound;
        // a column title may lie right of the panel (the header scrolls with the columns, each keeps its minimum width, inside
        // a clipping container): only its fit in its own column counts
        if (!header && (l.worldBound.xMax > pb.xMax + 0.5f || l.worldBound.xMin < pb.xMin - 0.5f)) return $"outside the panel ({l.worldBound.xMin:0}..{l.worldBound.xMax:0} of {pb.xMin:0}..{pb.xMax:0})";
        if (tw <= cw + 0.5f) return null;
        if (header) return rs.textOverflow == TextOverflow.Ellipsis && l.tooltip == l.text ? null : $"a {tw:0} px title in {cw:0} px without the ellipsis / tooltip";
        if (rs.whiteSpace != WhiteSpace.Normal) return $"a {tw:0} px line in {cw:0} px, no wrap";
        float th = l.MeasureTextSize(l.text, cw, VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined).y;
        return th <= l.contentRect.height + 1f ? null : $"wrapped {th:0} px in {l.contentRect.height:0} px";
    }

    static IEnumerator Play()
    {
        rep.AppendLine($"PoseBrowserWidthTest.play (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}, screen {Screen.width}x{Screen.height})");
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 240, ok);
        var app = AppCore.Current;
        Check(ok[0] && app.Stage == AppStage.Ready, "app ready (" + (app?.Stage.ToString() ?? "none") + ")");
        yield return PlayModeHarness.WaitUntil(() => UiHost.Current != null && UiHost.Current.IsReady, 30, ok);
        var host = UiHost.Current;
        Check(ok[0], "UiHost ready");
        if (!ok[0]) { Finish(); yield break; }
        var ui = host.Context;
        string lang0 = L10n.Default.Language;
        string pid = PoseBrowser.PanelId;
        Check(ui.Shell.ShowPanel(pid), "the pose panel shown");
        yield return PlayModeHarness.WaitUntil(() => AddModule.Poses != null && AddModule.Poses.Complete, 120, ok);
        var b = AddModule.Poses;
        Check(ok[0] && b.Index.Indices.Count > 0, $"the pose list complete ({b?.Index?.Indices.Count} shown)");
        var root = ui.Shell.PanelRoot(pid);
        string rid = ui.Shell.RegionOf(pid);
        var r = rid != null && ui.Shell.Regions.TryGetValue(rid, out var rr) ? rr : null;
        float rs0 = r?.Pane.worldBound.width ?? 0;
        Info($"the pose panel in region {rid} ({rs0:0} px wide)");
        foreach (var lang in new[] { "ja", "en" })
        {
            host.SetLanguage(lang, save: false);
            yield return Frames(4);
            // the language rebuilds panels: take the current browser and root again
            b = AddModule.Poses;
            root = ui.Shell.PanelRoot(pid);
            foreach (bool listMode in new[] { true, false })
            {
                b?.UseListMode(listMode);
                foreach (float w in new[] { 372f, 300f, 247f })
                {
                    if (w == 247f) root.style.width = 247; else { root.style.width = StyleKeyword.Null; if (r != null) ui.Shell.SetSize(r, w); }
                    yield return Frames(6);
                    var probs = new List<string>();
                    int titles = 0, statuses = 0, buttons = 0, cut = 0;
                    Label st = null;
                    foreach (var l in root.Query<Label>().ToList())
                    {
                        if (string.IsNullOrEmpty(l.text) || l.worldBound.width < 0.5f || l.resolvedStyle.display == DisplayStyle.None) continue;
                        bool header = l.name == PanelKit.HeaderTitleName || l.ClassListContains(PanelKit.HeaderTitleName);
                        bool status = l.name == "pose-status";
                        if (!header && !status) continue;
                        if (header)
                        {
                            titles++;
                            if (TextW(l) > l.contentRect.width + 0.5f) cut++;
                            if (w == 372f || w == 247f) Info($"{lang} {w:0} px: title «{l.text}» text {TextW(l):0} px, label {l.contentRect.width:0} px, header cell {l.parent?.parent?.worldBound.width ?? 0:0} px");
                        }
                        else { statuses++; st = l; }
                        string p = FitProblem(l, root, header);
                        if (p != null) probs.Add((header ? "title «" : "status «") + l.text + "» " + p);
                    }
                    var pb = root.worldBound;
                    var footer = root.Q("pose-footer");
                    float buttonsBottom = float.MinValue;
                    foreach (var bt in footer?.Query<Button>().ToList() ?? new List<Button>())
                    {
                        if (bt.resolvedStyle.display == DisplayStyle.None) continue;
                        buttons++;
                        buttonsBottom = Math.Max(buttonsBottom, bt.worldBound.yMax);
                        if (bt.worldBound.xMax > pb.xMax + 0.5f || bt.worldBound.xMin < pb.xMin - 0.5f) probs.Add($"button «{bt.text}» outside the panel ({bt.worldBound.xMin:0}..{bt.worldBound.xMax:0} of {pb.xMin:0}..{pb.xMax:0})");
                    }
                    bool needTitles = listMode;
                    if (listMode)
                    {
                        // the name column keeps its minimum (every column shrinks in proportion down to its minimum)
                        var nameTitle = root.Query<Label>().ToList().FirstOrDefault(l => (l.name == PanelKit.HeaderTitleName || l.ClassListContains(PanelKit.HeaderTitleName)) && l.text == L10n.Get("browser.col.name"));
                        float nameW = nameTitle?.parent?.parent?.worldBound.width ?? 0;
                        // (the header cell measures 2 px less than the column: its borders)
                        if (nameW < 137.5f) probs.Add($"the name column {nameW:0} px (< its 140 px minimum)");
                    }
                    Check(probs.Count == 0 && statuses == 1 && buttons == 3 && (!needTitles || titles == 3),
                        $"{lang} {(listMode ? "list" : "grid")} at {w:0} px (panel {pb.width:0} px): status line ({(st != null && st.worldBound.yMin >= buttonsBottom - 1 ? "own line" : "beside the buttons")}{(st != null && TextW(st) > st.contentRect.width + 0.5f ? ", wrapped" : "")}), {buttons} footer buttons" +
                        (listMode ? $" and {titles} column titles ({cut} cut)" : "") + " fit" +
                        (probs.Count > 0 ? ": " + string.Join("; ", probs.Take(6)) : ""));
                }
                root.style.width = StyleKeyword.Null;
                if (r != null) ui.Shell.SetSize(r, rs0);
                yield return Frames(2);
            }
            b?.UseListMode(false);
        }
        host.SetLanguage(lang0, save: false);
        yield return Frames(2);
        Finish();
    }

    static void Finish()
    {
        rep.Insert(0, $"SUMMARY: {pass} PASS, {fail} FAIL\n");
        File.WriteAllText(ReportPath, rep.ToString(), new UTF8Encoding(false));
        Debug.Log($"POSE WIDTH TEST DONE: {pass} PASS, {fail} FAIL");
        PlayModeHarness.ExitCode = fail > 0 ? 1 : 0;
    }
}

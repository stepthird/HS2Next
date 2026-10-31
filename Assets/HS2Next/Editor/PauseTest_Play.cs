// PauseTest.play (Phase 3.6 package K, R-M3 / R-M4 / R-M6): the global pause in real
// frames, the app in Editor play with the UI (port 17361), on a map with water and the sky on, a particle item (vanilla
// 1463 snow) and the female target:
//   P1 the toolbar's Pause through ui.action time.pause; the status / badge show it; Time.timeScale untouched
//   P2 R-M3 two captures 30 frames apart while paused: byte-identical with shaderTime all and captures; with off they differ
//      (water / sky read URP's time); resumed they differ; a step (ui.action time.step {n: 30}) changes them
//   P3 particles: the item's systems paused (isPaused) while paused, their time unchanged over frames, + n × dt by a step
//   P4 R-M6 while paused: the camera controller moves the view, a gizmo drag (ui.input on move X) records one entry, the
//      UI answers (ui.query), FK / transform edits are applied
//   P5 R-M4 pause / resume ms with the map's and the item's systems; scene.step 600 × 1/60 with the female on the map
// Run: -executeMethod PauseTest.RunPlay [-only P2]; report Spike/out/pause/pause_play_report.txt.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HS2Next.App;
using HS2Next.Content;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

public static partial class PauseTest
{
    public const int PlayPort = 17361;
    static string PlayReport => Path.Combine(OutDir, "pause_play_report.txt");
    static string PlayUserData => "Spike/out/pause/play_userdata";
    static string PlayLaunch =>
        "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":true,\"language\":\"en\"},\"userData\":\"" + PlayUserData + "\",\"bridge\":{\"port\":" + PlayPort + ",\"outputDir\":\"Spike/out/pause/play_out\",\"allowQuit\":false}}";

    public static void RunPlay()
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(PlayReport, "SUMMARY: 0 PASS, 1 FAIL\nPauseTest.play: started, no result yet\n", new UTF8Encoding(false));
        PlayModeHarness.Start("PauseTest.PlayMain", PlayReport, 30, true, PlayLaunch);
    }

    static JObject last;

    static IEnumerator Bridge(AppCore app, object req, double timeoutSec = 300)
    {
        string json = req is string s ? s : JsonConvert.SerializeObject(req);
        var t = app.Bridge.Execute(json);
        var until = Time.realtimeSinceStartup + timeoutSec;
        while (!t.IsCompleted && Time.realtimeSinceStartup < until) yield return null;
        try { last = t.IsCompleted && !t.IsFaulted ? JObject.Parse(t.Result) : new JObject { ["ok"] = false, ["error"] = t.IsFaulted ? t.Exception?.GetBaseException().Message : "timeout" }; }
        catch (Exception e) { last = new JObject { ["ok"] = false, ["error"] = e.Message }; }
        if (last.Value<bool?>("ok") != true) Info("call " + json + " -> " + Short(last.ToString(Formatting.None)));
    }

    static string Short(string s) => s == null ? "null" : s.Length > 400 ? s.Substring(0, 400) + "…" : s;
    static bool Ok => last != null && last.Value<bool?>("ok") == true;

    static IEnumerator WaitFrames(int n)
    {
        int target = Time.frameCount + n;
        while (Time.frameCount < target) yield return null;
    }

    static string Sha(string path)
    {
        if (path == null || !File.Exists(path)) return null;
        using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
    }

    static string capA, capB;
    static IEnumerator CapturePair(AppCore app, string tag, int framesApart)
    {
        capA = capB = null;
        yield return Bridge(app, new { cmd = "capture", name = "pause_" + tag + "_a", width = 640, height = 360 });
        if (Ok) capA = Sha((string)last["path"]);
        yield return WaitFrames(framesApart);
        yield return Bridge(app, new { cmd = "capture", name = "pause_" + tag + "_b", width = 640, height = 360 });
        if (Ok) capB = Sha((string)last["path"]);
    }

    static IEnumerator PlayMain()
    {
        rep = new StringBuilder(); passes = fails = warns = 0; perTest.Clear(); measurements.Clear();
        var o = Arg("-only");
        only = o == null ? null : o.Split(',', '+').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        rep.AppendLine($"PauseTest.play (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}, screen {Screen.width}x{Screen.height})");
        IEnumerator body = null;
        try { body = PlayBody(); } catch (Exception e) { current = "play"; Check("play started", false, e.ToString()); }
        while (body != null)
        {
            object cur;
            try { if (!body.MoveNext()) break; cur = body.Current; }
            catch (Exception e) { Check("play body ran without an exception", false, e.ToString()); break; }
            yield return cur;
        }
        var sum = new StringBuilder();
        sum.AppendLine($"SUMMARY: {passes} PASS, {fails} FAIL ({warns} WARN, {sw.Elapsed.TotalSeconds:F0} s)");
        foreach (var kv in perTest.OrderBy(k => k.Key, StringComparer.Ordinal)) sum.AppendLine($"  {kv.Key}: {kv.Value.pass} pass, {kv.Value.fail} fail");
        if (measurements.Count > 0) { sum.AppendLine("measurements:"); foreach (var m in measurements) sum.AppendLine("  " + m); }
        rep.Insert(0, sum.ToString());
        File.WriteAllText(PlayReport, rep.ToString(), new UTF8Encoding(false));
        Debug.Log($"PAUSE PLAY TEST DONE: {passes} PASS, {fails} FAIL");
        PlayModeHarness.ExitCode = fails > 0 ? 1 : 0;
    }

    static bool Want(string id) => only == null || only.Contains(id);

    static IEnumerator PlayBody()
    {
        current = "H";
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 240, ok);
        var app = AppCore.Current;
        if (!Check("app ready", ok[0] && app != null && app.Stage == AppStage.Ready, app?.Stage + " " + app?.Error)) yield break;
        yield return PlayModeHarness.WaitUntil(() => UiHost.Current != null && UiHost.Current.IsReady, 60, ok);
        var ui = UiHost.Current?.Context;
        Check("UI ready", ok[0] && ui != null);
        var rt = app.Runtime;
        Check("the shader-time feature is installed (AppPipeline)", HS2Next.SceneShaderTime.Installed != "none", HS2Next.SceneShaderTime.Installed);
        Check("the shader-time source is the app's clock", HS2Next.SceneShaderTime.Source == rt.Clock);

        // ---- scene: map 32 (water), sky on, a particle item, the female target
        yield return Bridge(app, new { cmd = "scene.new" });
        int mapNo = 32;
        yield return Bridge(app, new { cmd = "map.load", no = mapNo }, 600);
        Check("map " + mapNo + " loaded", Ok, Short(last?.ToString(Formatting.None)));
        yield return Bridge(app, new { cmd = "effects.set", set = new Dictionary<string, object> { ["sky.enabled"] = true } });
        Check("sky on", Ok);
        yield return Bridge(app, new { cmd = "item.add", no = 1463 });
        string snow = (string)last?["added"]?[0]?["key"];
        Check("particle item 1463 added", Ok && snow != null, Short(last?.ToString(Formatting.None)));
        var t = TestTargets.Load();
        string card = Path.GetFileName(TestTargets.Abs(t.visual.female));
        yield return Bridge(app, new { cmd = "chara.load", path = card, sex = "female" }, 300);
        string fem = (string)last?["added"]?[0]?["key"];
        Check("female target loaded", Ok && fem != null, Short(last?.ToString(Formatting.None)));
        yield return Bridge(app, new { cmd = "camera.set", target = new[] { 0f, 10f, 0f }, rot = new[] { 10f, 30f, 0f }, dist = 40f, fov = 45f });
        yield return WaitFrames(30);
        var snowItem = snow != null ? rt.FindItem(snow) : null;
        var ps = snowItem?.Particles?.Where(p => p).ToList() ?? new List<ParticleSystem>();
        Check("the snow item has playing particle systems", ps.Count > 0 && ps.Any(p => p.isPlaying), ps.Count + " systems");

        // ---- P1 toolbar pause
        current = "P1";
        if (Want("P1") || Want("P2") || Want("P3") || Want("P4"))
        {
            var sw0 = System.Diagnostics.Stopwatch.StartNew();
            yield return Bridge(app, new { cmd = "ui.action", action = "time.pause" });
            Check("ui.action time.pause (the toolbar action) pauses the scene", Ok && rt.Paused, Short(last?.ToString(Formatting.None)));
            Check("Time.timeScale untouched", Time.timeScale == 1f);
            yield return WaitFrames(2);
            var badge = UiHost.Current?.Root?.Q<UnityEngine.UIElements.Label>("viewport-paused");
            var status = UiHost.Current?.Root?.Q<UnityEngine.UIElements.Label>("status-paused");
            Check("the viewport badge and the status item show Paused", badge != null && badge.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.Flex && status != null && status.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.Flex,
                $"badge {badge?.resolvedStyle.display} '{badge?.text}', status {status?.resolvedStyle.display} '{status?.text}'");
            var freeze = rt.GetPart<SceneTimeFreeze>();
            Measure($"R-M4 pause with the map and the item: freeze {freeze?.LastFreezeMs:F3} ms ({freeze?.PausedParticles} particle systems, {freeze?.PausedAnimators} map Animators, {freeze?.PausedWinds} wind zones)");
        }

        // ---- P2 R-M3
        current = "P2";
        if (Want("P2"))
        {
            HS2Next.SceneShaderTime.Scope = HS2Next.SceneShaderTimeScope.All;
            yield return CapturePair(app, "all", 30);
            Check("R-M3 paused, shaderTime all: two captures 30 frames apart byte-identical", capA != null && capA == capB, capA + " / " + capB);
            HS2Next.SceneShaderTime.Scope = HS2Next.SceneShaderTimeScope.Captures;
            yield return CapturePair(app, "captures", 30);
            Check("R-M3 paused, shaderTime captures: byte-identical", capA != null && capA == capB, capA + " / " + capB);
            HS2Next.SceneShaderTime.Scope = HS2Next.SceneShaderTimeScope.Off;
            yield return CapturePair(app, "off", 30);
            bool offDiffer = capA != null && capB != null && capA != capB;
            if (offDiffer) Check("R-M3 paused, shaderTime off: the captures differ (URP's time moves water / sky)", true);
            else Warn("R-M3 paused, shaderTime off: the captures are equal (no time-dependent shader visible in this view)");
            HS2Next.SceneShaderTime.Scope = HS2Next.SceneShaderTimeScope.All;
            string before = null;
            yield return Bridge(app, new { cmd = "capture", name = "pause_step_a", width = 640, height = 360 });
            if (Ok) before = Sha((string)last["path"]);
            yield return Bridge(app, new { cmd = "ui.action", action = "time.step", args = new { n = 30 } });
            Check("ui.action time.step {n: 30} while paused", Ok);
            yield return Bridge(app, new { cmd = "capture", name = "pause_step_b", width = 640, height = 360 });
            string after = Ok ? Sha((string)last["path"]) : null;
            Check("a step changes the capture", before != null && after != null && before != after);
            yield return Bridge(app, new { cmd = "ui.action", action = "time.pause", args = new { paused = false } });
            Check("resume", Ok && !rt.Paused);
            yield return CapturePair(app, "running", 30);
            Check("running: the captures differ", capA != null && capB != null && capA != capB);
            yield return Bridge(app, new { cmd = "ui.action", action = "time.pause", args = new { paused = true } });
        }

        // ---- P3 particles
        current = "P3";
        if (Want("P3") && ps.Count > 0)
        {
            if (!rt.Paused) rt.SetPaused(true);
            yield return WaitFrames(2);
            var p0 = ps.First(p => p);
            Check("the item's particle systems are paused", ps.All(p => !p || p.isPaused), string.Join(", ", ps.Select(p => p ? p.name + (p.isPaused ? " paused" : " " + (p.isPlaying ? "playing" : "stopped")) : "-")));
            float tm0 = p0.time;
            yield return WaitFrames(20);
            Check("their time does not move over 20 paused frames", p0.time == tm0, tm0 + " → " + p0.time);
            rt.Step(30, 1f / 60f);
            float tm1 = p0.time;
            Check("a step of 30 × 1/60 simulates them (+0.5 s, within the system's duration wrap)", Mathf.Abs(Mathf.Repeat(tm1 - tm0, p0.main.duration) - Mathf.Repeat(0.5f, p0.main.duration)) < 0.02f, tm0 + " → " + tm1);
            Check("still paused after the step", ps.All(p => !p || p.isPaused));
        }

        // ---- P4 R-M6
        current = "P4";
        if (Want("P4") && ui != null)
        {
            if (!rt.Paused) rt.SetPaused(true);
            var cam = ui.Viewport.ViewCamera;
            var pos0 = cam ? cam.transform.position : Vector3.zero;
            ui.Viewport.Camera.Reset();
            yield return WaitFrames(3);
            Check("R-M6 the camera controller moves the view while paused (camera reset)", cam && (cam.transform.position - pos0).sqrMagnitude > 1e-6f, pos0 + " → " + (cam ? cam.transform.position.ToString() : "-"));
            yield return Bridge(app, new { cmd = "ui.query", name = "tb-scene-time" });
            Check("R-M6 the UI answers ui.query while paused", Ok, Short(last?.ToString(Formatting.None)));
            if (fem != null)
            {
                yield return Bridge(app, new { cmd = "ui.action", action = "tool.move" });
                ui.Ops.Select(new[] { fem });
                ui.Viewport.Camera.Frame(new[] { fem });
                yield return WaitFrames(4);
                var g = ui.Viewport.Gizmo;
                var holder = rt.HolderOf(fem);
                var start = ui.Model.Find(fem).Doc.Transform.Pos;
                float L = ui.Options.Int(OptionDefs.GizmoSize) * g.WorldPerPx(holder.position);
                var axis = ui.Space == GizmoSpace.World || ui.Space == GizmoSpace.Auto ? Vector3.right : holder.rotation * Vector3.right;
                Vector2 a0 = g.ToPanel(holder.position), aT = g.ToPanel(holder.position + axis * L);
                Vector2 grab = Vector2.Lerp(a0, aT, 0.6f), end = grab + (aT - a0).normalized * 50f;
                int undo0 = ui.Session.Commands.UndoList.Count;
                yield return Bridge(app, new
                {
                    cmd = "ui.input", frames = 2,
                    events = new object[]
                    {
                        new { type = "pointer", phase = "down", x = grab.x, y = grab.y, button = 0 },
                        new { type = "pointer", phase = "move", x = (grab.x + end.x) / 2, y = (grab.y + end.y) / 2, button = 0 },
                        new { type = "pointer", phase = "move", x = end.x, y = end.y, button = 0 },
                        new { type = "pointer", phase = "up", x = end.x, y = end.y, button = 0 },
                    },
                });
                var pos = ui.Model.Find(fem).Doc.Transform.Pos;
                Check("R-M6 a gizmo drag while paused moves the character in the model (one entry)", Ok && Mathf.Abs(pos.X - start.X) > 1e-3f && ui.Session.Commands.UndoList.Count == undo0 + 1,
                    $"x {start.X} → {pos.X}, entries {undo0} → {ui.Session.Commands.UndoList.Count}");
                yield return WaitFrames(2);
                Check("R-M6 the holder follows at once (edits are applied while paused)", Mathf.Abs(holder.localPosition.x - pos.X) < 1e-4f, holder.localPosition.x + " vs " + pos.X);
            }
        }

        // ---- P5 R-M4 on the map
        current = "P5";
        if (Want("P5"))
        {
            if (!rt.Paused) rt.SetPaused(true);
            yield return Bridge(app, new { cmd = "scene.step", n = 600, dt = 1f / 60f }, 120);
            Check("scene.step 600 on map " + mapNo + " with the female ≤ 3 s", Ok && last.Value<double>("ms") <= 3000, last?["ms"]?.ToString());
            Measure($"R-M4 scene.step 600 × 1/60 in Editor play on map {mapNo} with one character and a particle item: {last?["ms"]} ms");
            var freeze = rt.GetPart<SceneTimeFreeze>();
            var swr = System.Diagnostics.Stopwatch.StartNew();
            rt.SetPaused(false);
            Measure($"R-M4 resume {swr.Elapsed.TotalMilliseconds:F3} ms (thaw {freeze?.LastThawMs:F3} ms)");
            yield return WaitFrames(3);
            Check("resumed: the particles play again", ps.Count == 0 || ps.Any(p => p && p.isPlaying));
        }
        rt.SetPaused(false);
    }
}

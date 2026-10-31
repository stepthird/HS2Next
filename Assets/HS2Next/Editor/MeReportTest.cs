// MeReportTest.play: the report of MaterialEditor shader swaps HS2Next cannot draw (Next-Gen and other shaders without a
// URP replacement). The app in Editor play with the UI (port 17623), the female target card, synthetic MaterialEditor items
// added to its stored documents (no installed card is opened or named):
//   NG1 a document with two swaps on the face and body materials to Hanmen/Next-Gen Face and Hanmen/Next-Gen Body builds;
//       the face and body keep their standard shaders; one warning finding me.shaderUnavailable on the node, count 2, both
//       names in its message; the realization lists the two names
//   NG2 the inspector's header badge counts it and the character section shows one line with the names (its tooltip
//       names the plugin supply point); a rebuild without the swaps clears the finding, the line and the badge's share
//   NG3 a swap to a fitted name (AIT/Eye Translucency on the eye material) raises nothing
//   NG4 the build leaves the documents as they were (the two items stay in the stored character document); chara.load of
//       the character saved with the swaps lists the finding in its reply's findings
//   TB1 a material that arrived with the engine's error shader is drawn by ShaderMap.ApplyUrp with the URP/Lit fallback and
//       marked (ShaderMap.IsBrokenInBundle), the count rule of the character finding shader.brokenInBundle counts it, a URP
//       material is not marked, the female target has none
// Run: -executeMethod MeReportTest.RunPlay [-only NG1,NG2,TB1] (unity_batch -NoQuit); report Spike/out/mereport/me_report_play.txt.
// Numbers and names only: no image is captured, saved or opened (the character file is saved with the placeholder thumbnail).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.App;
using HS2Next.Chara.Ext;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

public static class MeReportTest
{
    public const int PlayPort = 17623;
    public const string NextGenFace = "Hanmen/Next-Gen Face", NextGenBody = "Hanmen/Next-Gen Body", FittedEye = "AIT/Eye Translucency";
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "mereport");
    static string PlayReport => Path.Combine(OutDir, "me_report_play.txt");
    const string PlayUserData = "Spike/out/mereport/play_userdata";
    static string PlayLaunch =>
        "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":true,\"language\":\"en\"},\"userData\":\"" + PlayUserData + "\",\"bridge\":{\"port\":" + PlayPort +
        ",\"outputDir\":\"Spike/out/mereport/play_out\",\"allowQuit\":false}}";

    static StringBuilder rep;
    static int passes, fails, warns;
    static string current = "";
    static string[] only;
    static readonly Dictionary<string, (int pass, int fail)> perTest = new Dictionary<string, (int, int)>();

    public static void RunPlay()
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(PlayReport, "SUMMARY: 0 PASS, 1 FAIL\nMeReportTest.play: started, no result yet\n", new UTF8Encoding(false));
        PlayModeHarness.Start("MeReportTest.PlayMain", PlayReport, 20, true, PlayLaunch);
    }

    static string Arg(string name)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    static void Info(string s) => rep.AppendLine("   " + s);
    static void Warn(string s) { warns++; rep.AppendLine($"WARN  [{current}] {s}"); }
    static bool Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  [{current}] {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
        perTest.TryGetValue(current, out var v);
        perTest[current] = ok ? (v.pass + 1, v.fail) : (v.pass, v.fail + 1);
        return ok;
    }
    static bool Want(string id) => only == null || only.Contains(id);
    static string Short(string s) => s == null ? "null" : s.Length > 400 ? s.Substring(0, 400) + "…" : s;

    static JObject last;
    static bool Ok => last != null && last.Value<bool?>("ok") == true;

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

    static IEnumerator PlayMain()
    {
        rep = new StringBuilder(); passes = fails = warns = 0; perTest.Clear();
        var o = Arg("-only");
        only = o == null ? null : o.Split(',', '+').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        rep.AppendLine($"MeReportTest.play (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
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
        rep.Insert(0, sum.ToString());
        File.WriteAllText(PlayReport, rep.ToString(), new UTF8Encoding(false));
        Debug.Log($"ME REPORT PLAY TEST DONE: {passes} PASS, {fails} FAIL");
        PlayModeHarness.ExitCode = fails > 0 ? 1 : 0;
    }

    // ------------------------------------------------------------ helpers

    // The first material of the character's renderer of that name: (MaterialEditor name, shader name).
    static (string material, string shader) MaterialOf(CharaRealization r, string renderer)
    {
        if (r == null || !r.Root) return (null, null);
        foreach (var rd in r.Root.GetComponentsInChildren<Renderer>(true))
        {
            if (rd.name != renderer) continue;
            var m = rd.sharedMaterials.FirstOrDefault(x => x);
            if (m) return (MaterialEditorApplier.Fmt(m.name), m.shader ? m.shader.name : null);
        }
        return (null, null);
    }

    static List<SceneFinding> MeFindings(AppCore app, string id) =>
        app.Session.Findings.Where(f => f.NodeId == id && f.Code == CharaRealization.MeShaderUnavailableCode).ToList();

    // The appearance with the given MaterialEditor shader items added to the stored character document.
    static CharaAppearance WithSwaps(CharaAppearance a, params (string material, string shader)[] swaps)
    {
        var c = a.Clone();
        foreach (var s in swaps)
            c.Stored.Chara.MaterialOverrides.Add(new MeShader { Material = s.material, Shader = s.shader, ShaderOriginal = null, RenderQueue = null, RenderQueueOriginal = null });
        return c;
    }

    // The inspector panel, brought to front (a docked panel that is not shown is not built).
    static HS2Next.UI.Panels.InspectorPanel Inspector()
    {
        var sh = UiHost.Current?.Context?.Shell;
        if (sh == null) return null;
        sh.FocusPanel("inspector");
        var root = sh.PanelRoot("inspector");
        return root as HS2Next.UI.Panels.InspectorPanel ?? root?.Q<HS2Next.UI.Panels.InspectorPanel>();
    }

    static int BadgeCount()
    {
        var b = Inspector()?.Q<VisualElement>("inspector-header")?.Q<Label>("findings");
        return b != null && int.TryParse(b.text, out int n) ? n : 0;
    }

    static Label MeLine() => Inspector()?.Q<Label>("me-shader-unavailable");

    static string InspectorShows() { var i = Inspector(); return i == null ? "no inspector panel" : "node " + (i.Q<Label>("node-id")?.text ?? "-") + ", chara section " + (i.Q<HS2Next.UI.Panels.CharaInspector>() != null); }

    static IEnumerator Rebuild(AppCore app, string id, CharaAppearance a, string what)
    {
        var res = app.Session.SetAppearance(id, a);
        Check(what + ": the appearance is set", res.Ok, res.Code + " " + res.Message);
        app.Runtime.ApplyNow();
        var r = app.Runtime.FindChara(id);
        Check(what + ": the character is built", r != null && r.State == RealizationState.Built, r?.State + " " + r?.Detail);
        yield return PlayModeHarness.WaitFrames(5);   // the inspector refreshes on the build event
    }

    // ------------------------------------------------------------ body

    static IEnumerator PlayBody()
    {
        current = "H";
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 240, ok);
        var app = AppCore.Current;
        if (!Check("app ready", ok[0] && app != null && app.Stage == AppStage.Ready, app?.Stage + " " + app?.Error)) yield break;
        yield return PlayModeHarness.WaitUntil(() => UiHost.Current != null && UiHost.Current.IsReady, 60, ok);
        Check("UI ready", ok[0] && UiHost.Current?.Context != null);
        var rt = app.Runtime;

        yield return Bridge(app, new { cmd = "scene.new" });
        var t = TestTargets.Load();
        string card = Path.GetFileName(TestTargets.Abs(t.visual.female));
        yield return Bridge(app, new { cmd = "chara.load", path = card, sex = "female" }, 300);
        string id = (string)last?["added"]?[0]?["key"];
        if (!Check("female target loaded", Ok && id != null, Short(last?.ToString(Formatting.None)))) yield break;
        var baseFindings = (last["findings"] as JArray)?.Count(f => (string)f["code"] == CharaRealization.MeShaderUnavailableCode) ?? -1;
        Check("the target as loaded: no me.shaderUnavailable", baseFindings == 0, "findings with the code " + baseFindings);
        var node = app.Session.Model.Find(id);
        var original = node?.Appearance;
        if (!Check("the node stores imported documents", original?.Stored != null)) yield break;
        var r0 = rt.FindChara(id);
        var face0 = MaterialOf(r0, "o_head");
        var body0 = MaterialOf(r0, "o_body_cf");
        var eye0 = MaterialOf(r0, "o_eyebase_L");
        Info($"face material {face0.material} ({face0.shader}), body {body0.material} ({body0.shader}), eye {eye0.material} ({eye0.shader})");
        Check("face, body and eye materials found", face0.material != null && body0.material != null && eye0.material != null);
        yield return Bridge(app, new { cmd = "select", key = id });
        yield return PlayModeHarness.WaitFrames(5);
        int badge0 = BadgeCount();
        Info("inspector badge before: " + badge0 + "; " + InspectorShows());
        Check("no MaterialEditor shader line before", MeLine() == null);

        // ---- NG1 Next-Gen face and body
        current = "NG1";
        var swapped = WithSwaps(original, (face0.material, NextGenFace), (body0.material, NextGenBody));
        if (Want("NG1") || Want("NG2") || Want("NG4"))
        {
            yield return Rebuild(app, id, swapped, "Next-Gen face + body");
            var r1 = rt.FindChara(id);
            var face1 = MaterialOf(r1, "o_head");
            var body1 = MaterialOf(r1, "o_body_cf");
            Check("the face keeps its standard shader", face1.shader != null && face1.shader == face0.shader, face1.shader + " / " + face0.shader);
            Check("the body keeps its standard shader", body1.shader != null && body1.shader == body0.shader, body1.shader + " / " + body0.shader);
            var fs = MeFindings(app, id);
            Check("one me.shaderUnavailable finding on the node", fs.Count == 1, fs.Count.ToString());
            var f = fs.FirstOrDefault();
            Check("it is a warning", f != null && f.Severity == FindingSeverity.Warning, f?.Severity.ToString());
            Check("its count is 2 (the skipped swaps)", f != null && f.Count == 2, f?.Count.ToString());
            Check("its message names both shaders", f != null && f.Message.Contains(NextGenFace) && f.Message.Contains(NextGenBody), f?.Message);
            var names = r1?.UnavailableMeShaders;
            Check("the realization lists the two names in ordinal order", names != null && names.SequenceEqual(new[] { NextGenBody, NextGenFace }),
                names == null ? "null" : string.Join(", ", names));
            Check("the build context recorded both as me.shader", r1?.Context != null && r1.Context.Missing.Count(m => m.Kind == "me.shader") == 2);
        }

        // ---- NG2 the inspector
        current = "NG2";
        if (Want("NG2"))
        {
            yield return Bridge(app, new { cmd = "select", key = id });
            yield return PlayModeHarness.WaitFrames(5);
            yield return PlayModeHarness.WaitRealtime(0.7);   // the inspector also refreshes on a 500 ms schedule
            int badge1 = BadgeCount();
            Check("the inspector badge counts the two skipped swaps", badge1 == badge0 + 2, badge0 + " -> " + badge1 + "; " + InspectorShows());
            var line = MeLine();
            Check("the character section shows the line", line != null && line.resolvedStyle.display == DisplayStyle.Flex);
            Check("the line names both shaders", line != null && line.text.Contains(NextGenFace) && line.text.Contains(NextGenBody), line?.text);
            Check("the line's tooltip is set", line != null && !string.IsNullOrEmpty(line.tooltip), line?.tooltip);
            yield return Rebuild(app, id, original.Clone(), "rebuild without the swaps");
            yield return Bridge(app, new { cmd = "select", key = id });
            yield return PlayModeHarness.WaitFrames(5);
            yield return PlayModeHarness.WaitRealtime(0.7);   // the inspector also refreshes on a 500 ms schedule
            Check("the finding is cleared", MeFindings(app, id).Count == 0);
            Check("the line is gone", MeLine() == null);
            Check("the badge is back", BadgeCount() == badge0, BadgeCount().ToString());
        }

        // ---- NG3 a fitted name
        current = "NG3";
        if (Want("NG3"))
        {
            yield return Rebuild(app, id, WithSwaps(original, (eye0.material, FittedEye)), "eye swap to " + FittedEye);
            var r3 = rt.FindChara(id);
            Check("no me.shaderUnavailable finding", MeFindings(app, id).Count == 0);
            Check("no unavailable names", r3 != null && r3.UnavailableMeShaders.Count == 0);
            Check("no me.shader record", r3?.Context != null && !r3.Context.Missing.Any(m => m.Kind == "me.shader"));
            var eye3 = MaterialOf(r3, "o_eyebase_L");
            Info("eye shader after the swap: " + eye3.shader);
            yield return Bridge(app, new { cmd = "select", key = id });
            yield return PlayModeHarness.WaitFrames(5);
            yield return PlayModeHarness.WaitRealtime(0.7);   // the inspector also refreshes on a 500 ms schedule
            Check("no line", MeLine() == null);
        }

        // ---- NG4 documents and the chara.load reply
        current = "NG4";
        if (Want("NG4"))
        {
            yield return Rebuild(app, id, swapped.Clone(), "Next-Gen face + body again");
            var st = app.Session.Model.Find(id)?.Appearance?.Stored;
            var items = st?.Chara.MaterialOverrides.OfType<MeShader>().Select(m => m.Material + "=" + m.Shader).ToList() ?? new List<string>();
            Check("the stored document keeps both swaps after the build",
                items.Contains(face0.material + "=" + NextGenFace) && items.Contains(body0.material + "=" + NextGenBody), string.Join("; ", items));
            Check("the stored document has the original items plus the two", st != null &&
                st.Chara.MaterialOverrides.Count == original.Stored.Chara.MaterialOverrides.Count + 2);
            string name = "mereport_ng4";
            yield return Bridge(app, new { cmd = "chara.save", key = id, name, overwrite = true, thumb = "placeholder" }, 120);
            Check("chara.save with the swaps", Ok, Short(last?.ToString(Formatting.None)));
            yield return Bridge(app, new { cmd = "scene.new" });
            yield return Bridge(app, new { cmd = "chara.load", path = name + ".hs2nchara" }, 300);
            Check("chara.load of the saved character", Ok, Short(last?.ToString(Formatting.None)));
            var mf = (last?["findings"] as JArray)?.Where(x => (string)x["code"] == CharaRealization.MeShaderUnavailableCode).ToList() ?? new List<JToken>();
            Check("chara.load lists me.shaderUnavailable once", mf.Count == 1, mf.Count.ToString());
            Check("as a warning with both names", mf.Count == 1 && (string)mf[0]["severity"] == "Warning" &&
                ((string)mf[0]["message"]).Contains(NextGenFace) && ((string)mf[0]["message"]).Contains(NextGenBody), mf.FirstOrDefault()?.ToString(Formatting.None));
        }
        // ---- TB1 a material that arrived with the engine's error shader (shader.brokenInBundle, the fallback tail)
        current = "TB1";
        if (Want("TB1"))
        {
            var err = Shader.Find(HS2Next.ShaderMap.EngineErrorShader);
            Check("the engine's error shader is found by name", err != null);
            var go = new GameObject("tb1 stage");
            var a = GameObject.CreatePrimitive(PrimitiveType.Quad);
            a.transform.SetParent(go.transform, false);
            var b = GameObject.CreatePrimitive(PrimitiveType.Quad);
            b.transform.SetParent(go.transform, false);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var me = err ? new Material(err) { name = "tb1 error" } : null;
            var ml = lit ? new Material(lit) { name = "tb1 lit" } : null;
            a.GetComponent<Renderer>().sharedMaterial = me;
            b.GetComponent<Renderer>().sharedMaterial = ml;
            HS2Next.ShaderMap.ApplyUrp(go);
            var ma = a.GetComponent<Renderer>().sharedMaterial;
            var mb = b.GetComponent<Renderer>().sharedMaterial;
            Check("ApplyUrp draws the error-shader material with the URP/Lit fallback", ma && ma.shader == lit && HS2Next.ShaderMap.IsFallback(ma), ma && ma.shader ? ma.shader.name : "null");
            Check("it is marked as arrived with the error shader", HS2Next.ShaderMap.IsBrokenInBundle(ma));
            Check("a URP/Lit material is not marked", !HS2Next.ShaderMap.IsBrokenInBundle(mb));
            Check("the character count rule counts 1 slot under the stage", CharaRealization.CountBrokenInBundle(go) == 1, CharaRealization.CountBrokenInBundle(go).ToString());
            yield return Bridge(app, new { cmd = "scene.new" });
            yield return Bridge(app, new { cmd = "chara.load", path = card, sex = "female" }, 300);
            string id2 = (string)last?["added"]?[0]?["key"];
            var built = id2 != null ? rt.FindChara(id2) : null;
            Check("the female target has no such slot and no shader.brokenInBundle finding", built != null && built.BrokenInBundleSlots == 0 &&
                !app.Session.Findings.Any(f => f.NodeId == id2 && f.Code == CharaRealization.BrokenInBundleCode));
            foreach (var m in new[] { ma, mb, me, ml }) if (m) UnityEngine.Object.Destroy(m);
            UnityEngine.Object.Destroy(go);
        }
        yield return Bridge(app, new { cmd = "scene.new" });
    }
}

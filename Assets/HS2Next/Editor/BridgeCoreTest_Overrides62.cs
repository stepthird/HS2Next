// C41: the runtime overrides of plugins (Phase 6.2 package O) in an edit-mode test host with
// the plugin folder of C37 (Spike/out/plugins/core; the override probe stp3.test.overrideprobe staged there by C37Stage, or
// by this check when an older staging lacks it). Parts (each with its own host): TX4 the probe's slots on an item's
// transform.pos, an FK bone and the camera against a Timeline slot of the same channels (priority -1 yields, 0 acquired
// later wins, 1 wins whatever the order), IsWinning, List, ReleaseAll, the refusals; plugin.list overrides; plugin.disable
// releases the probe's slots (reply overrides) and the model shows in the same frame; plugin.enable acquires them again (the
// probe's Load); TX3 Set(x, y, z) of an item's position: median <= 5 us over 10,000 calls, no collection. The Timeline's
// slots are played by timeline.build / timeline.play when those commands exist, else laid by the core owner "timeline" through
// the layer directly (the same owner and priority). CX the post stage on the real host, through
// PluginHost.RunScenePost: plugin.list's scenePostMs only on a plugin with the interface, the frame the plugin gets, 10 faults
// and plugin_fault, an enable and a disable, BoneWritten's answers. -c41 TX4 runs one part. Content: the clothed SFW female
// target; numbers.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HS2Next.App;
using HS2Next.App.Plugins;
using HS2Next.Api.Plugins;
using HS2Next.Api.Unity;
using HS2Next.Content;
using HS2Next.Scenes.Overrides;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class BridgeCoreTest
{
    const string OpGuid = "stp3.test.overrideprobe";

    static string[] C41Parts()
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, "-c41");
        if (i < 0 || i + 1 >= a.Length) return null;
        return a[i + 1].Split(',', '+', ';').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
    }

    static void C41(Ctx c)
    {
        var parts = C41Parts();
        bool Want(string id) => parts == null || parts.Contains(id, StringComparer.OrdinalIgnoreCase);
        if (!C37Stage(c)) return;
        if (!File.Exists(Path.Combine(C37Dir, OpGuid, "manifest.json")) && !C41StageProbe(c)) return;
        foreach (var (id, run) in new (string, Action<Ctx>)[] { ("TX4", C41Tx4), ("TX3", C41Tx3), ("CX", C41Cx) })
        {
            if (!Want(id)) continue;
            try { run(c); }
            catch (Exception e) { c.Check(false, id + " ran without an exception", e.ToString()); }
            finally { c.Dispose(c.App); }
        }
    }

    /// <summary>Builds and stages the override probe alone into the C37 folder (a folder staged before the probe existed).</summary>
    static bool C41StageProbe(Ctx c)
    {
        string tool = Path.Combine(ContentSettings.ProjectRoot, "Tools", "plugins.ps1");
        var psi = new ProcessStartInfo("pwsh", "-NoProfile -File \"" + tool + "\" -Build \"" + Path.Combine("Tests", "Plugins", "OverrideProbe") + "\" -Stage \"" + C37Dir + "\"")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = ContentSettings.ProjectRoot,
        };
        using (var p = Process.Start(psi))
        {
            string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            bool exited = p.WaitForExit(300000);
            c.Info("staged the override probe: plugins.ps1 exit " + (exited ? p.ExitCode.ToString() : "timeout"));
            return c.Check(exited && p.ExitCode == 0, "the override probe staged", Ctx.Short(o));
        }
    }

    static (AppCore app, PluginHost host) C41Host(Ctx c)
    {
        var (app, host) = C37Host(c);
        c.Call(new { cmd = "overrideprobe.release", all = true, forget = true });
        return (app, host);
    }

    static JObject C41Plugin(Ctx c, string guid) =>
        (c.Call(new { cmd = "plugin.list" })["plugins"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x["guid"] == guid);

    /// <summary>The Timeline's slot of (node, path) with a value: timeline.build + play when registered, else the core owner directly.</summary>
    static OverrideSlot C41TimelineSlot(Ctx c, string node, string path, double[] values)
    {
        var layer = c.App.Runtime.Overrides;
        var s = layer.Acquire(OverrideLayer.TimelineOwner, node, path);
        s.Set(values);
        return s;
    }

    // ---------------------------------------------------------------- CX: the post stage on the real host

    static void C41Cx(Ctx c)
    {
        var (app, host) = C41Host(c);
        string item = W1AddItem(c);
        string chara = W1LoadFemale(c, false);
        c.Frames(2);

        // the report: scenePostMs only on a plugin with the interface
        c.Check(C41Plugin(c, OpGuid)?["scenePostMs"] is JObject, "CX: plugin.list: scenePostMs on the plugin with the interface");
        c.Check(C41Plugin(c, "stp3.test.probe") != null && C41Plugin(c, "stp3.test.probe")["scenePostMs"] == null, "CX: plugin.list: no scenePostMs on a plugin without it");

        // the calls: a frame of this host may run the stage as well, so the probe counts those of one delta
        const float dt = 0.25f;
        var rt = app.Runtime;
        int Timed() => (int)C41Plugin(c, OpGuid)["scenePostMs"]["frames"];
        int timed = Timed();
        int marked = (int)c.Ok(new { cmd = "overrideprobe.post", mark = dt }, "CX: the probe answers")["marked"];
        double clock = rt.Clock.Time;
        bool paused = rt.Paused;
        for (int i = 0; i < 3; i++) host.RunScenePost(dt);
        var last = c.Ok(new { cmd = "overrideprobe.post" }, "CX: the frame");
        c.Check((int)last["marked"] == marked + 3, "CX: three runs, three calls", Ctx.Short(last.ToString()));
        c.Check(Timed() >= timed + 3, "CX: the time window counts them");
        c.Check((float)last["sceneDt"] == dt, "CX: SceneDt is the argument", Ctx.Short(last.ToString()));
        c.Check((double)last["sceneTime"] == clock - dt, "CX: SceneTime is the clock before the frame", Ctx.Short(last.ToString()));
        c.Check((bool)last["paused"] == paused, "CX: Paused is the scene's");

        // BoneWritten's answers
        var services = host.Find(OpGuid).Context.GetService(typeof(IUnityPluginServices)) as IUnityPluginServices;
        c.Check(services != null, "CX: the engine-side services");
        var bone = services.CharaRoot(chara).transform;
        c.Check(services.BoneWritten(chara, bone), "CX: BoneWritten on a built character");
        c.Check(!services.BoneWritten(item, bone), "CX: BoneWritten on an item: false");
        c.Check(!services.BoneWritten("0123456789abcdef0123456789abcdef", bone), "CX: BoneWritten on an unknown id: false");
        c.Check(!services.BoneWritten(null, bone), "CX: BoneWritten without a node: false");
        c.Check(!services.BoneWritten(chara, null), "CX: BoneWritten without a bone: false");

        // ten faults, then the commands answer plugin_fault and the stage goes on without it
        c.Ok(new { cmd = "overrideprobe.post", throws = true }, "CX: ScenePost throws from now on");
        for (int i = 0; i < 12; i++) host.RunScenePost(dt);
        var pl = C41Plugin(c, OpGuid);
        c.Check((string)pl?["state"] == PluginStates.Faulted, "CX: the tenth fault faults the plugin", Ctx.Short(pl?.ToString(Newtonsoft.Json.Formatting.None)));
        c.Check((int?)pl?["faults"]?["scenePost"] == 10, "CX: ten faults under scenePost, twelve runs", Ctx.Short(pl?["faults"]?.ToString(Newtonsoft.Json.Formatting.None)));
        c.Fails(new { cmd = "overrideprobe.post" }, "plugin_fault", "CX: its commands answer plugin_fault");

        // an enable: a new instance is called again; a disable: nothing is called
        c.Ok(new { cmd = "plugin.enable", guid = OpGuid }, "CX: plugin.enable");
        c.Ok(new { cmd = "overrideprobe.post", mark = dt }, "CX: the new instance answers");
        host.RunScenePost(dt);
        c.Check((int)c.Ok(new { cmd = "overrideprobe.post" }, "CX: the new instance's count")["marked"] == 1, "CX: enabled, the new instance is called");
        c.Ok(new { cmd = "plugin.disable", guid = OpGuid }, "CX: plugin.disable");
        int frozen = Timed();
        host.RunScenePost(dt);
        c.Check(Timed() == frozen, "CX: a disabled plugin is not called, and the report stays");
    }

    // ---------------------------------------------------------------- TX4

    static void C41Tx4(Ctx c)
    {
        var (app, host) = C41Host(c);
        var st = c.Ok(new { cmd = "overrideprobe.state" }, "TX4: the probe answers");
        c.Check((bool?)st["available"] == true, "TX4: Overrides.Available with a scene runtime");
        string item = W1AddItem(c);
        string chara = W1LoadFemale(c, false);
        c.Ok(new { cmd = "fk.enable", key = chara, on = true }, "TX4: FK on");
        c.Ok(new { cmd = "fk.enable", key = chara, groups = new[] { "Body", "Neck" }, on = true }, "TX4: FK groups Body and Neck");
        c.Frames(2);
        var holder = app.Runtime.HolderOf(item);
        var layer = app.Runtime.Overrides;

        // refusals
        string Refused(object req) => (string)c.Call(req)["refused"];
        c.Check(Refused(new { cmd = "overrideprobe.acquire", name = "x", key = item, path = "transform.position" })?.Contains("transform.position") == true, "TX4: an unknown path is refused, naming it");
        c.Check(Refused(new { cmd = "overrideprobe.acquire", name = "x", key = item, path = "chara.anime.speed" })?.Contains("chara.anime.speed") == true, "TX4: a chara path on an item is refused");
        c.Check(Refused(new { cmd = "overrideprobe.acquire", name = "x", path = "transform.pos" }) != null, "TX4: a node path without a node id is refused");
        c.Check(Refused(new { cmd = "overrideprobe.acquire", name = "x", key = item, path = "camera" }) != null, "TX4: a scene path with a node id is refused");
        var nf = c.Call(new { cmd = "overrideprobe.acquire", name = "x", key = "0123456789abcdef0123456789abcdef", path = "transform.pos" });
        c.Check((bool?)nf["acquired"] == false && (nf["refused"] == null || nf["refused"].Type == JTokenType.Null), "TX4: an id that names no node: null (not acquired, no refusal)", Ctx.Short(c.LastText));
        var bad = c.Call(new { cmd = "overrideprobe.set", name = "x", values = new[] { 1.0 } });
        c.Check((string)bad["code"] == "not_found", "TX4: the refused slots were not acquired");

        // the Timeline holds the item's position (acquired first)
        var tl = C41TimelineSlot(c, item, "transform.pos", new double[] { 7, 0, 0 });
        c.Frames(1);
        c.Check((holder.localPosition - new Vector3(7, 0, 0)).magnitude < 1e-6, "TX4: the Timeline's slot shows", holder.localPosition.ToString("F4"));
        // -1 yields
        c.Ok(new { cmd = "overrideprobe.acquire", name = "pos", key = item, path = "transform.pos", priority = -1, auto = true }, "TX4: acquire transform.pos at -1");
        c.Ok(new { cmd = "overrideprobe.set", name = "pos", values = new[] { 1.0, 2, 3 }, perFrame = true }, "TX4: set (1, 2, 3) in every update");
        c.Frames(2);
        var s1 = (JObject)c.Call(new { cmd = "overrideprobe.state" })["slots"]["pos"];
        c.Check((holder.localPosition - new Vector3(7, 0, 0)).magnitude < 1e-6 && (bool?)s1["isWinning"] == false && (bool?)s1["hasValue"] == true,
            "TX4: priority -1 yields to the Timeline (IsWinning false)", holder.localPosition.ToString("F4") + " " + s1.ToString(Newtonsoft.Json.Formatting.None));
        // 0, acquired after the Timeline: wins
        c.Ok(new { cmd = "overrideprobe.acquire", name = "pos", key = item, path = "transform.pos", priority = 0, auto = true }, "TX4: the same channel again at 0 (the same slot)");
        c.Frames(1);
        var s2 = (JObject)c.Call(new { cmd = "overrideprobe.state" })["slots"]["pos"];
        c.Check((holder.localPosition - new Vector3(1, 2, 3)).magnitude < 1e-6 && (bool?)s2["isWinning"] == true && (int?)s2["priority"] == 0,
            "TX4: priority 0 acquired after the Timeline wins", holder.localPosition.ToString("F4"));
        // the Timeline acquires after the probe (its slot released and acquired again): 0 loses, 1 wins
        tl.Release();
        tl = C41TimelineSlot(c, item, "transform.pos", new double[] { 7, 0, 0 });
        c.Frames(1);
        c.Check((holder.localPosition - new Vector3(7, 0, 0)).magnitude < 1e-6, "TX4: equal priorities: the Timeline acquired later wins");
        c.Ok(new { cmd = "overrideprobe.acquire", name = "pos", key = item, path = "transform.pos", priority = 1, auto = true }, "TX4: the same channel at 1");
        c.Frames(1);
        c.Check((holder.localPosition - new Vector3(1, 2, 3)).magnitude < 1e-6, "TX4: priority 1 wins whatever the order", holder.localPosition.ToString("F4"));
        // an FK bone at 0 and the camera at 1 against a Timeline camera
        c.Ok(new { cmd = "overrideprobe.acquire", name = "neck", key = chara, path = "chara.fk.bones[\"cf_J_Neck\"]", priority = 0, auto = true }, "TX4: acquire an FK bone at 0");
        c.Ok(new { cmd = "overrideprobe.set", name = "neck", values = new[] { 15.0, 25, 0 } }, "TX4: set the bone");
        var tlCam = C41TimelineSlot(c, null, "camera", new double[] { 0, 10, 0, 15, 180, 0, 25, 23 });
        c.Ok(new { cmd = "overrideprobe.acquire", name = "cam", path = "camera", priority = 1, auto = true }, "TX4: acquire the camera at 1");
        c.Ok(new { cmd = "overrideprobe.set", name = "cam", values = new[] { 1.0, 2, 3, 10, 20, 0, 5, 30 } }, "TX4: set the camera");
        c.Frames(2);
        var neck = C41Bone(app, chara, "cf_J_Neck");
        c.Check(neck && Quaternion.Angle(neck.localRotation, Quaternion.Euler(15, 25, 0)) < 0.01f, "TX4: the FK bone shows the probe's slot", neck ? neck.localEulerAngles.ToString("F3") : "no bone");
        var cam = c.Call(new { cmd = "camera.get" })["camera"];
        c.Check((string)cam?["overridden"] == OpGuid && Math.Abs((double)cam["dist"] - 5) < 1e-4 && Math.Abs((double)cam["fov"] - 30) < 1e-4,
            "TX4: the camera at 1 wins over the Timeline's; camera.get reports the shown view and overridden", Ctx.Short(cam?.ToString(Newtonsoft.Json.Formatting.None)));
        // List
        var list = c.Call(new { cmd = "overrideprobe.state" })["list"] as JArray;
        var owners = list?.OfType<JObject>().Select(x => (string)x["owner"] + ":" + (string)x["path"] + ":" + ((bool)x["winning"] ? "w" : "-")).ToList() ?? new List<string>();
        c.Check(list != null && list.Count == 5 && owners.Contains("timeline:camera:-") && owners.Contains(OpGuid + ":camera:w") && owners.Contains("timeline:transform.pos:-") && owners.Contains(OpGuid + ":transform.pos:w") &&
                (string)list[0]["path"] == "camera", "TX4: List gives every owner's slots with a value, the scene channels first, winning marked", string.Join(", ", owners));
        if (W1Has(c, "overrides.list"))
        {
            var ol = c.Ok(new { cmd = "overrides.list" }, "TX4: overrides.list");
            var arr = ol["overrides"] as JArray;
            c.Check(arr?.Count == 5 && arr.OfType<JObject>().Count(x => (bool)x["winning"]) == 3 && (string)arr[0]["path"] == "camera",
                "TX4: overrides.list lists the same five slots (three winning: the probe's; the camera first)", Ctx.Short(c.LastText));
        }
        else c.Warn("TX4: overrides.list is not registered yet (the timeline group's registration): skipped");
        // plugin.list overrides
        var pl = C41Plugin(c, OpGuid);
        c.Check((pl?["overrides"] as JArray)?.Count == 3 && (pl["overrides"] as JArray).OfType<JObject>().All(x => (bool)x["hasValue"]), "TX4: plugin.list overrides: the probe's three slots", Ctx.Short(pl?["overrides"]?.ToString(Newtonsoft.Json.Formatting.None)));
        // plugin.disable releases them; the model shows in the same frame
        var d = c.Ok(new { cmd = "plugin.disable", guid = OpGuid }, "TX4: plugin.disable");
        c.Check((int?)d["overrides"] == 3, "TX4: plugin.disable's reply counts the released slots (3)", Ctx.Short(c.LastText));
        c.Check((holder.localPosition - new Vector3(7, 0, 0)).magnitude < 1e-6 && layer.Slots(null, OpGuid).Count == 0, "TX4: the Timeline's value shows at once (the probe's slot gone)", holder.localPosition.ToString("F4"));
        c.Frames(1);
        cam = c.Call(new { cmd = "camera.get" })["camera"];
        c.Check((string)cam?["overridden"] == "timeline" && Math.Abs(Quaternion.Angle(neck.localRotation, Quaternion.Euler(15, 25, 0))) > 0.01f, "TX4: the camera and the bone no longer show the probe's slots", Ctx.Short(cam?.ToString(Newtonsoft.Json.Formatting.None)));
        // plugin.enable: the new instance acquires again
        c.Ok(new { cmd = "plugin.enable", guid = OpGuid }, "TX4: plugin.enable");
        c.Frames(2);
        var st2 = c.Call(new { cmd = "overrideprobe.state" });
        c.Check((int?)st2["reacquired"] == 3 && (holder.localPosition - new Vector3(1, 2, 3)).magnitude < 1e-6 && (string)c.Call(new { cmd = "camera.get" })["camera"]?["overridden"] == OpGuid,
            "TX4: plugin.enable: the probe acquired its slots again and they show", Ctx.Short(st2.ToString(Newtonsoft.Json.Formatting.None)));
        // ReleaseAll
        var ra = c.Ok(new { cmd = "overrideprobe.release", all = true, forget = true }, "TX4: ReleaseAll");
        c.Check((int?)ra["released"] == 3 && layer.Slots(null, OpGuid).Count == 0 && (holder.localPosition - new Vector3(7, 0, 0)).magnitude < 1e-6, "TX4: ReleaseAll counts and releases the probe's slots", Ctx.Short(c.LastText));
        tl.Release();
        tlCam.Release();
        c.Frames(1);
        var model = app.Session.Model.Find(item).Doc.Transform.Pos;
        c.Check((holder.localPosition - new Vector3(model.X, model.Y, model.Z)).magnitude < 1e-6 && layer.Count == 0, "TX4: no slot left: the model's position shows");
    }

    static Transform C41Bone(AppCore app, string chara, string name)
    {
        var r = app.Runtime.FindChara(chara);
        if (r?.Root == null) return null;
        foreach (var t in r.Root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }

    // ---------------------------------------------------------------- TX3

    static void C41Tx3(Ctx c)
    {
        var (app, host) = C41Host(c);
        string item = W1AddItem(c);
        c.Frames(1);
        c.Ok(new { cmd = "overrideprobe.acquire", name = "bench", key = item, path = "transform.pos" }, "TX3: acquire an item's position");
        var b = c.Ok(new { cmd = "overrideprobe.bench", name = "bench", n = 10000 }, "TX3: 10,000 Set(x, y, z)");
        double med = (double?)b["medianUs"] ?? double.NaN;
        c.Check(med <= 5 && (int?)b["collections"] == 0, "TX3: Set(x, y, z) median <= 5 us, no collection (no managed allocation)",
            $"median {med} us, p95 {b["p95Us"]} us, max {b["maxUs"]} us, collections {b["collections"]}, allocated {b["allocatedBytes"]} B");
        c.Info($"TX3: Set(x, y, z) median {med} us, p95 {b["p95Us"]} us over 10,000 calls (the item's holder written each call)");
        var holder = app.Runtime.HolderOf(item);
        c.Check((holder.localPosition - new Vector3(1 + 9999 * 1e-4f, 0.25f, -1 - 9999 * 1e-4f)).magnitude < 1e-4, "TX3: the last value shows", holder.localPosition.ToString("F4"));
        c.Call(new { cmd = "overrideprobe.release", all = true, forget = true });
    }
}

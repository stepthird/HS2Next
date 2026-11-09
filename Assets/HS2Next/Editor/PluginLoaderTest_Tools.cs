// The play row's check of a plugin's viewport tool (PluginLoaderTest.play, session 1, after PL13): the tool probe
// stp3.test.toolprobe on the host's real tool input, driven through ui.input. Names and counts only.
//   WI8  a plugin tool from Started to Ended: a claimed press, the wheel and a key reach it; a press it leaves gets no release;
//        Esc ends it; a call that throws is a counted fault of the plugin and ends it with Cancelled, the plugin goes on; a
//        disable ends a running tool with UiClosed after the plugin's Unload, and the host has no tool left. The guide's example
//        ends itself from its own press.
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

public static partial class PluginLoaderTest
{
    const string ToolGuid = "stp3.test.toolprobe";

    static JObject ToolState(PlayCtx c) => c.Call(new { cmd = "toolprobe.state" });

    static List<string> ToolCalls(PlayCtx c) => (ToolState(c)["calls"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();

    static void ToolClear(PlayCtx c) => c.Call(new { cmd = "toolprobe.clear" });

    static void ViewportPress(PlayCtx c, int button = 0) => c.Call(new
    {
        cmd = "ui.input", frames = 2,
        events = new object[]
        {
            new { type = "pointer", target = "viewport", phase = "down", button },
            new { type = "pointer", target = "viewport", phase = "up", button },
        },
    });

    static int ToolFaults(PlayCtx c, string hook) => (int?)PluginOf(c.Call(new { cmd = "plugin.list" }), ToolGuid)?["faults"]?[hook] ?? 0;

    static void WI8(PlayCtx c)
    {
        var st = ToolState(c);
        if (!c.Check((bool?)st["unity"] == true && (bool?)st["tools"] == true, "WI8 the engine side and Tools are there with the UI on", PlayCtx.Short(S(st)))) return;
        ToolClear(c);

        var b = c.Ok(new { cmd = "toolprobe.begin" }, "WI8 toolprobe.begin");
        var calls = ToolCalls(c);
        c.Check((bool?)b["began"] == true && calls.SequenceEqual(new[] { "Started" }) && (bool?)ToolState(c)["running"] == true, "WI8 Begin: Started, and a tool runs", string.Join(", ", calls));
        ViewportPress(c);
        c.Call(new { cmd = "ui.input", frames = 2, events = new object[] { new { type = "wheel", target = "viewport", delta = -3 } } });
        Key(c, "T");
        calls = ToolCalls(c);
        c.Check(calls.Contains("PointerDown 0 None") && calls.Contains("PointerUp 0") && calls.Contains("Wheel up") && calls.Contains("Key T"), "WI8 a claimed press, the wheel and a key reach the tool", string.Join(", ", calls));
        c.Ok(new { cmd = "toolprobe.end" }, "WI8 toolprobe.end");
        calls = ToolCalls(c);
        c.Check(calls.LastOrDefault() == "Ended Finished" && (bool?)ToolState(c)["running"] == false, "WI8 End: Ended(Finished) is the last call, no tool runs", string.Join(", ", calls));

        ToolClear(c);
        c.Ok(new { cmd = "toolprobe.begin", claim = false }, "WI8 toolprobe.begin claim false");
        ViewportPress(c);
        calls = ToolCalls(c);
        c.Check(calls.Contains("PointerDown 0 None") && !calls.Any(x => x.StartsWith("PointerUp", System.StringComparison.Ordinal)), "WI8 a press the tool leaves gets no release", string.Join(", ", calls));
        Key(c, "Escape");
        calls = ToolCalls(c);
        c.Check(calls.LastOrDefault() == "Ended Cancelled" && (bool?)ToolState(c)["running"] == false, "WI8 Esc with no press held ends the tool with Cancelled", string.Join(", ", calls));

        ToolClear(c);
        int before = ToolFaults(c, "tool PointerDown");
        c.Ok(new { cmd = "toolprobe.begin", throwIn = "PointerDown" }, "WI8 toolprobe.begin throwIn");
        ViewportPress(c);
        calls = ToolCalls(c);
        var p = PluginOf(c.Call(new { cmd = "plugin.list" }), ToolGuid);
        c.Check(calls.LastOrDefault() == "Ended Cancelled" && ToolFaults(c, "tool PointerDown") == before + 1 && (string)p?["state"] == "loaded" && (bool?)ToolState(c)["running"] == false,
            "WI8 a call that throws: a counted fault, Ended(Cancelled), the plugin stays loaded", string.Join(", ", calls) + "; state " + p?["state"]);

        ToolClear(c);
        c.Ok(new { cmd = "toolprobe.pick" }, "WI8 toolprobe.pick");
        c.Check((bool?)ToolState(c)["running"] == true, "WI8 the guide's example starts");
        c.Ok(new { cmd = "toolprobe.end" }, "WI8 toolprobe.end ends nothing of another object");
        c.Check((bool?)ToolState(c)["running"] == true, "WI8 End names a tool: the example runs on");
        Key(c, "Escape");

        ToolClear(c);
        c.Ok(new { cmd = "toolprobe.begin" }, "WI8 toolprobe.begin before the disable");
        c.Ok(new { cmd = "plugin.disable", guid = ToolGuid }, "WI8 plugin.disable");
        c.Ok(new { cmd = "plugin.enable", guid = ToolGuid }, "WI8 plugin.enable");
        calls = ToolCalls(c);
        int unload = calls.IndexOf("Unload"), ended = calls.IndexOf("Ended UiClosed");
        c.Check(unload >= 0 && ended > unload && (bool?)ToolState(c)["running"] == false, "WI8 a disable ends the tool with UiClosed after Unload, and no tool runs", string.Join(", ", calls));
    }
}

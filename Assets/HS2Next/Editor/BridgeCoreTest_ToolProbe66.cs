// BridgeCoreTest group C46 (Phase 6.6, package A): the test plugin stp3.test.toolprobe, built with dotnet against the Api as a
// plugin is, loaded by the real plugin host of a headless app. A class of the plugin derives from ViewportTool, overrides its
// virtual calls and builds a ToolPointer with a lazy pick, so the check is that the binding holds at run time; with no UI
// Tools is null. A plugin's tool on the host's real tool input is WI8 in PluginLoaderTest.play; the map query in a player is
// WY3 in PluginCheck.player.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HS2Next.Content;
using Newtonsoft.Json.Linq;

public static partial class BridgeCoreTest
{
    const string ToolProbeGuid = "stp3.test.toolprobe";

    /// <summary>Builds and stages the tool probe alone into the C37 folder, which -Build all fills: one name, one folder, as an
    /// assembly loads once per process.</summary>
    static bool C46StageProbe(Ctx c)
    {
        string tool = Path.Combine(ContentSettings.ProjectRoot, "Tools", "plugins.ps1");
        var psi = new ProcessStartInfo("pwsh", "-NoProfile -File \"" + tool + "\" -Build \"" + Path.Combine("Tests", "Plugins", "ToolProbe") + "\" -Stage \"" + C37Dir + "\"")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = ContentSettings.ProjectRoot,
        };
        using (var p = Process.Start(psi))
        {
            string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            bool exited = p.WaitForExit(300000);
            return c.Check(exited && p.ExitCode == 0, "C46: the tool probe is built and staged", Ctx.Short(o));
        }
    }

    static void C46(Ctx c)
    {
        if (!C37Stage(c)) return;
        if (!File.Exists(Path.Combine(C37Dir, ToolProbeGuid, "manifest.json")) && !C46StageProbe(c)) return;
        var (app, _) = C37Host(c);
        try
        {
            var entry = (c.Call(new { cmd = "plugin.list" })["plugins"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x["guid"] == ToolProbeGuid);
            if (!c.Check((string)entry?["state"] == "loaded" && (string)entry?["apiVersion"] == "1.3", "C46: the probe of Api 1.3 is loaded", Ctx.Short(entry?.ToString()))) return;
            var st = c.Ok(new { cmd = "toolprobe.state" }, "C46: toolprobe.state");
            c.Check((bool?)st["unity"] == true && (bool?)st["tools"] == false && (bool?)st["running"] == false, "C46: the engine side is there and Tools is null with no UI", Ctx.Short(st.ToString()));
            var t = c.Ok(new { cmd = "toolprobe.selftest" }, "C46: toolprobe.selftest");
            var calls = (t["calls"] as JArray)?.Select(x => (string)x).ToArray() ?? new string[0];
            c.Check((bool?)t["down"] == true && (int?)t["picks"] == 1 && calls.SequenceEqual(new[] { "Started", "PointerDown 0 Ctrl hit True", "PointerUp 0", "Ended Finished" }),
                "C46: the plugin's tool answers through ViewportTool, in-parameters and the lazy pick included", string.Join(", ", calls) + "; picks " + t["picks"]);
        }
        finally { c.Dispose(app); }
    }
}

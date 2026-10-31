// C29 of BridgeCoreTest: the Phase 3.6 time commands of package K (a
// group number of its own beside R's C27 and C's C28), headless: registration (TimeCommands.Names in Phase36Names),
// scene.pause read / set / shaderTime (shapes, bad_args), scene.step while running → unavailable, the n / dt ranges, a
// step of n × dt advances the clock by the double sum, no history entry, scene.new resets the pause.
using System.Linq;
using Newtonsoft.Json.Linq;

public static partial class BridgeCoreTest
{
    static void C29(Ctx c)
    {
        var app = c.Host();
        var reg = app.BridgeServices.Commands;
        var p36 = HS2Next.App.Commands.CoreCommands.Phase36Names;
        var names = HS2Next.App.Commands.TimeCommands.Names;
        c.Check(names.SequenceEqual(new[] { "scene.pause", "scene.step" }) && names.All(n => p36.Contains(n) && reg.Find(n) != null),
            "TimeCommands.Names (scene.pause, scene.step) are in Phase36Names and registered", string.Join(", ", names.Where(n => reg.Find(n) == null)));
        c.Ok(new { cmd = "scene.new" }, "scene.new");
        int undo0 = app.Session.Commands.UndoList.Count;
        var r = c.Ok(new { cmd = "scene.pause" }, "scene.pause (read)");
        c.Check(r.Value<bool?>("paused") == false && r["time"] != null && r["frame"] != null && r["steps"] != null && (string)r["shaderTime"] == "all",
            "scene.pause reply {paused false, time, frame, steps, shaderTime all}", Ctx.Short(c.LastText));
        c.Fails(new { cmd = "scene.step" }, "unavailable", "scene.step while running → unavailable (pause first)");
        c.Fails(new { cmd = "scene.pause", shaderTime = "never" }, "bad_args", "scene.pause shaderTime never");
        r = c.Ok(new { cmd = "scene.pause", paused = true }, "scene.pause {paused: true}");
        c.Check(r.Value<bool?>("paused") == true && app.Runtime.Paused, "paused");
        c.Fails(new { cmd = "scene.step", n = 0 }, "bad_args", "scene.step n 0");
        c.Fails(new { cmd = "scene.step", n = 10001 }, "bad_args", "scene.step n 10001");
        c.Fails(new { cmd = "scene.step", dt = 0 }, "bad_args", "scene.step dt 0");
        c.Fails(new { cmd = "scene.step", dt = 2 }, "bad_args", "scene.step dt 2");
        double t0 = app.Runtime.Clock.Time;
        r = c.Ok(new { cmd = "scene.step", n = 30, dt = 1f / 60f }, "scene.step {n: 30}");
        double expect = t0;
        for (int i = 0; i < 30; i++) expect += 1f / 60f;
        c.Check(r.Value<double>("time") == expect && app.Runtime.Clock.Time == expect && r.Value<long>("steps") == 30 && r["ms"] != null,
            "scene.step: time + 30 × 1/60 (double sum), steps 30, ms", Ctx.Short(c.LastText));
        c.Check(app.Session.Commands.UndoList.Count == undo0, "pause / step record no history entry");
        c.Ok(new { cmd = "scene.new" }, "scene.new while paused");
        c.Check(!app.Runtime.Paused && app.Runtime.Clock.Time <= 1f / 60f + 1e-9 && app.Runtime.StepsTaken == 0, "scene.new resets the clock (running, time 0 + the one frame AppCore.Call pumps, steps 0)");   // AppCore.Frame begins clock frames in edit mode (2a2491d)
    }
}

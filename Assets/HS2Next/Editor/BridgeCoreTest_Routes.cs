// C27 of BridgeCoreTest: the Phase 3.6 route commands of package R, further methods
// of the partial BridgeCoreTest (-only C27; packages C and K have C28 / C29): registration (RouteCommands.Names in
// CoreCommands.Phase36Names, owner core, summaries), route.add / addPoint / set / setPoint / deletePoint shapes and one
// history entry each with its label, route.play / stop / list (no entry), route.state while playing on the scene clock
// (scene.pause + scene.step: τ = n × dt exactly, the child root = the evaluator), undo of the route edits back to the empty
// route, errors (wrong_kind, not_found, unavailable, bad_args).
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Routes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class BridgeCoreTest
{
    static void C27(Ctx c)
    {
        var app = c.Host();
        var reg = app.BridgeServices.Commands;
        var names = HS2Next.App.Commands.RouteCommands.Names;
        var p36 = HS2Next.App.Commands.CoreCommands.Phase36Names;
        c.Check(names.Count == 9 && names.All(n => p36.Contains(n) && reg.Find(n) != null && reg.Find(n).Owner == HS2Next.Api.Bridge.BridgeOwners.Core && !string.IsNullOrEmpty(reg.Find(n).Summary)),
            "the 9 route commands are in Phase36Names, registered, owner core, with summaries", string.Join(", ", names.Where(n => reg.Find(n) == null)));
        try
        {
            C27Flow(c, app);
            C27Errors(c, app);
        }
        finally { RouteSettings.Reset(); }
    }

    static int C27Undos(Ctx c) => c.App.Session.Commands.UndoList.Count;
    static string C27Top(Ctx c) => c.App.Session.Commands.UndoList.Count > 0 ? c.App.Session.Commands.UndoList[0].Label : null;

    static void C27Flow(Ctx c, HS2Next.App.AppCore app)
    {
        c.Ok(new { cmd = "scene.new" }, "scene.new");
        string empty = DocText(app);
        int n0 = C27Undos(c);
        var add = c.Ok(new { cmd = "route.add", pos = new[] { 1, 0, 0 } }, "route.add");
        string key = (string)add["key"];
        c.Check(key != null && (add["points"] as JArray)?.Count == 1 && C27Undos(c) == n0 + 1 && C27Top(c) == "Add route", "route.add: {key, points [start]}, one entry \"Add route\"", Ctx.Short(add.ToString()));
        string afterAdd = DocText(app);
        var p1 = c.Ok(new { cmd = "route.addPoint", key }, "route.addPoint (default R8 placement)");
        c.Check((int)p1["index"] == 1 && (int)p1["count"] == 2 && C27Top(c) == "Add route point", "route.addPoint: {key, id, index 1, count 2}, \"Add route point\"", Ctx.Short(p1.ToString()));
        var p2 = c.Ok(new { cmd = "route.addPoint", key, pos = new[] { 6, 0, 10 } }, "route.addPoint pos");
        c.Check((int)p2["index"] == 2 && (int)p2["count"] == 3, "route.addPoint appends at index 2");
        var st0 = c.Ok(new { cmd = "route.state", key }, "route.state");
        var pts = st0["points"] as JArray;
        c.Check(pts?.Count == 3 && Math.Abs((double)pts[1]["pos"][2] - 10) < 1e-6 && (double)pts[1]["pos"][0] == 0, "the default second point sits at start + (0, 0, 10) (R8)", Ctx.Short(pts?.ToString()));
        var set = c.Ok(new { cmd = "route.set", key, loop = true, orient = "xy", showLine = true }, "route.set");
        c.Check((set["changed"] as JArray)?.Select(x => (string)x).Contains("route.loop") == false && (set["changed"] as JArray)?.Select(x => (string)x).Contains("route.orient") == true && C27Top(c) == "Route",
            "route.set: changed lists canonical paths (loop already true is noop), \"Route\"", Ctx.Short(set.ToString()));
        var sp = c.Ok(new { cmd = "route.setPoint", key, point = 1, connection = "curve", speed = 4, ease = "easeOutQuad" }, "route.setPoint curve");
        c.Check(sp["aid"]?["init"]?.Value<bool>() == true && (sp["changed"] as JArray)?.Count >= 4 && C27Top(c) == "Route point", "route.setPoint curve: the midpoint aid with init, one entry \"Route point\"", Ctx.Short(sp.ToString()));
        int nPlay = C27Undos(c);
        var pl = c.Ok(new { cmd = "route.play", key }, "route.play");
        c.Check((string)pl["routes"]?[0]?["state"] == "playing" && C27Undos(c) == nPlay, "route.play: playing, no history entry", Ctx.Short(pl.ToString()));
        c.Ok(new { cmd = "scene.pause", paused = true }, "scene.pause true");
        var before = c.Ok(new { cmd = "route.state", key, line = false }, "route.state paused");
        double tau = (double)before["time"];   // the frames the requests pumped (AppCore.Frame begins clock frames in edit mode)
        c.Ok(new { cmd = "scene.step", n = 30 }, "scene.step 30");
        var st = c.Ok(new { cmd = "route.state", key }, "route.state after 30 steps");
        for (int i = 0; i < 30; i++) tau += 1f / 60f;
        c.Check((string)st["state"] == "playing" && (double)st["time"] == tau, "τ + 30 × 1/60 exactly after scene.step 30 (paused: the pumped frames add nothing)", "time " + (double?)st["time"] + " vs " + tau);
        var route = (RouteNodeState)app.Session.Model.Find(key).State;
        var path = RoutePath.Build(route);
        var s = path.Evaluate(tau);
        var lp = st["childRoot"]?["localPos"] as JArray;
        c.Check(lp != null && Math.Abs((double)lp[0] - s.Position.X) < 1e-5 && Math.Abs((double)lp[1] - s.Position.Y) < 1e-5 && Math.Abs((double)lp[2] - s.Position.Z) < 1e-5,
            "the child root's local position = the evaluator at τ (1e-5)", Ctx.Short(lp?.ToString()));
        var segs = st["segments"] as JArray;
        c.Check(segs?.Count == 3 && Math.Abs((double)segs[0]["duration"] - path.Segments[0].Duration) < 1e-9 && (string)segs[0]["connection"] == "line" && (string)segs[1]["connection"] == "curve"
                && (string)segs[2]["connection"] == "line" && (string)segs[2]["to"] == route.Points[0].Id,
            "route.state segments = the evaluator's (durations, connections, the closing segment of a loop)", Ctx.Short(segs?.ToString()));
        c.Check(st["total"]?.Type == JTokenType.Null && (string)st["mode"] == "loop", "a loop's total is null (endless)");
        c.Check(st["line"]?["count"] != null && (int)st["line"]["count"] == (int)st["line"]["expected"], "route.state line: count = the rule", Ctx.Short(st["line"]?["count"]?.ToString()));
        c.Ok(new { cmd = "scene.pause", paused = false }, "scene.pause false");
        var ls = c.Ok(new { cmd = "route.list" }, "route.list");
        c.Check((ls["routes"] as JArray)?.Count == 1 && (string)ls["routes"][0]["key"] == key && (int)ls["routes"][0]["points"] == 3, "route.list: one route, 3 points", Ctx.Short(ls.ToString()));
        var stp = c.Ok(new { cmd = "route.stop", key }, "route.stop");
        c.Check((string)stp["routes"]?[0]?["state"] == "stopped" && C27Undos(c) == nPlay, "route.stop: stopped, no history entry");
        var del = c.Ok(new { cmd = "route.deletePoint", key, point = 2 }, "route.deletePoint 2");
        c.Check((del["deleted"] as JArray)?.Count == 1 && (int)del["count"] == 2 && C27Top(c) == "Delete route point", "route.deletePoint: {deleted [id], count}, \"Delete route point\"");
        // undo chain back to the empty route (play / stop recorded nothing; route.playing false again)
        for (int i = 0; i < 5; i++) c.App.Session.Commands.Undo();
        app.Runtime.ApplyNow();
        c.Check(DocText(app) == afterAdd, "five undos restore the document right after route.add (play / stop not in the history)");
        c.App.Session.Commands.Undo();
        app.Runtime.ApplyNow();
        c.Check(DocText(app) == empty, "one more undo removes the route (the empty scene's document)");
    }

    static string DocText(HS2Next.App.AppCore app) => System.Text.Encoding.UTF8.GetString(DocClone.Bytes(app.Session.ExportDocument()));

    static void C27Errors(Ctx c, HS2Next.App.AppCore app)
    {
        var add = c.Ok(new { cmd = "route.add" }, "route.add (errors)");
        string key = (string)add["key"];
        string start = (string)add["points"][0];
        c.Fails(new { cmd = "route.play", key }, "unavailable", "route.play of a one-point route → unavailable");
        c.Check(app.Session.Findings.Any(f => f.Code == "route.onePoint" && f.NodeId == key), "finding route.onePoint");
        c.Fails(new { cmd = "route.deletePoint", key, point = start }, "wrong_kind", "route.deletePoint of the start point → wrong_kind");
        c.Fails(new { cmd = "route.deletePoint", key, point = "nosuch" }, "not_found", "route.deletePoint of an unknown point → not_found");
        c.Fails(new { cmd = "route.setPoint", key, point = 0, pos = new[] { 1, 0, 0 } }, "bad_args", "route.setPoint pos on the start point → bad_args");
        c.Ok(new { cmd = "route.addPoint", key }, "route.addPoint");
        c.Fails(new { cmd = "route.setPoint", key, point = 1, rot = new[] { 0, 10, 0 } }, "bad_args", "route.setPoint rot on another point → bad_args");
        c.Fails(new { cmd = "route.setPoint", key, point = 1, ease = "easeSideways" }, "bad_args", "route.setPoint unknown ease → bad_args");
        c.Fails(new { cmd = "route.setPoint", key, point = 1 }, "bad_args", "route.setPoint with nothing to set → bad_args");
        c.Fails(new { cmd = "route.set", key, orient = "z" }, "bad_args", "route.set orient z → bad_args");
        c.Fails(new { cmd = "route.addPoint", key, index = 0 }, "bad_args", "route.addPoint index 0 (before the start point) → bad_args");
        var folder = c.Ok(new { cmd = "camera.add" }, "camera.add");
        c.Fails(new { cmd = "route.state", key = (string)folder["key"] }, "wrong_kind", "route.state on a camera → wrong_kind");
        c.Fails(new { cmd = "route.state", key = "nosuchnode" }, "not_found", "route.state unknown key → not_found");
        c.Fails(new { cmd = "route.add", parent = key }, "wrong_kind", "route.add inside a route → wrong_kind");
        RouteSettings.PointLimit = true;
        for (int i = 0; i < 9; i++) c.Ok(new { cmd = "route.addPoint", key }, "route.addPoint up to 11");
        c.Fails(new { cmd = "route.addPoint", key }, "unavailable", "route.addPoint with 11 points and the limit on → unavailable");
        RouteSettings.PointLimit = false;
        c.Ok(new { cmd = "route.addPoint", key }, "route.addPoint with the limit off (12 points)");
    }
}

// The route commands (group "routes / time / capture"; Phase 3.6 package R): mutations go
// through StudioOps.Routes (one entry per call; the transport not recorded), reads from the route's realization.
//   route.add         {parent, point, index, pos, rot}                    {key, points [startId]}           "Add route"
//   route.state       {key, line = true}                                  {key, name, playing, state, time, total, segment,
//                     percentage, eased, segments [...], childRoot {pos, rot, localPos, localRot}, points [...], loop,
//                     showLine, orient, color, line {count, points [world]}}
//   route.set         {key | keys, loop, showLine, orient, color}         {changed, noop}                   "Route"
//   route.addPoint    {key, index, pos}                                   {key, point, index, count}         "Add route point"
//   route.deletePoint {key, point | points}                               {deleted, count}                   "Delete route point"
//   route.setPoint    {key, point | points, pos, rot, speed, ease, connection, link, aid {pos, init}, resetAid}
//                                                                         {changed, noop, aid}               "Route point"
//   route.play        {key | keys | all, restart}                         {routes [{key, state}]}            not recorded
//   route.stop        {key | keys | all}                                  {routes [{key, state}]}            not recorded
//   route.list        {}                                                  {routes [{key, name, state, points, parent}]}
// Errors: wrong_kind (not a route; the start point deleted), not_found (point), unavailable (the point limit; Play of a
// one-point route, finding route.onePoint), bad_args (pos on the start point, rot on another, an unknown ease).
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Api.Bridge;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Routes;
using HS2Next.Scenes.Runtime;
using UnityEngine;

namespace HS2Next.App.Commands
{
    public static class RouteCommands
    {
        /// <summary>The commands of this file (part of CoreCommands.Phase36Names, package R).</summary>
        public static readonly IReadOnlyList<string> Names = new[]
        {
            "route.add", "route.state", "route.set", "route.addPoint", "route.deletePoint", "route.setPoint", "route.play", "route.stop", "route.list",
        };

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("route.add", "Adds a route with its start point at route-local zero (one undo entry \"Add route\"); placed like the other adds.", "routes")
                    .Arg("parent", ArgTypes.Node, "parent node (default the scene root; never inside a route)")
                    .Arg("point", ArgTypes.String, "attach point under a character parent")
                    .Arg("index", ArgTypes.Int, "sibling index (default the end)", false, -1)
                    .Arg("pos", ArgTypes.Vec3, "local position").Arg("rot", ArgTypes.Euler, "local rotation (Euler degrees)")
                    .Return("key", ArgTypes.String, "the new route").Return("points", ArgTypes.ArrayOf(ArgTypes.String), "[the start point's id]")
                    .Example("{\"cmd\":\"route.add\",\"pos\":[0,0,0]}")
                    .WithFlags(CommandFlags.Mutates), Add);
            CommandKit.Register(r, app, CommandKit.Spec("route.state", "A route's document values, its runtime state (stopped / playing / ended, route time, segments of the HS2 timing rule), the child root's pose and the drawn line.", "routes")
                    .Arg("key", ArgTypes.Node, "the route", true)
                    .Arg("line", ArgTypes.Bool, "include the line's world vertices", false, true)
                    .Return("state", ArgTypes.String, "stopped, playing or ended").Return("playing", ArgTypes.Bool, "route.playing in the model")
                    .Return("time", ArgTypes.Float, "route time τ (s)").Return("total", ArgTypes.Float, "total duration D (null = endless)")
                    .Return("segment", ArgTypes.Int, "queued segment index").Return("percentage", ArgTypes.Float, "tween percentage p").Return("eased", ArgTypes.Float, "spline parameter s")
                    .Return("segments", ArgTypes.ArrayOf(ArgTypes.Object), "{from, to, duration, speed, ease, connection, linked [ids], lead}")
                    .Return("childRoot", ArgTypes.Object, "{pos, rot (world, Euler), localPos, localRot}")
                    .Return("points", ArgTypes.ArrayOf(ArgTypes.Object), "{id, index, pos, rot, speed, ease, connection, link, aid {pos, init} | null, world}")
                    .Return("line", ArgTypes.Object, "{count, expected, points [world]} (null with one point)")
                    .WithFlags(CommandFlags.ReadOnly), State);
            CommandKit.Register(r, app, CommandKit.Spec("route.set", "Sets route members (loop, showLine, orient none / xy / y, color) on one or several routes (one undo entry \"Route\").", "routes")
                    .Arg("key", ArgTypes.Node, "a route").Arg("keys", ArgTypes.ArrayOf(ArgTypes.Node), "several routes")
                    .Arg("loop", ArgTypes.Bool, "loop the route (two points: HS2's ping-pong)").Arg("showLine", ArgTypes.Bool, "draw the route line and markers").Arg("orient", ArgTypes.Enum("none", "xy", "y"), "none, xy (orient to the path) or y (yaw only)")
                    .Arg("color", ArgTypes.Color, "[r, g, b(, a)] (alpha 1 when omitted)")
                    .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "canonical paths").Return("noop", ArgTypes.ArrayOf(ArgTypes.String), "")
                    .WithFlags(CommandFlags.Mutates), Set);
            CommandKit.Register(r, app, CommandKit.Spec("route.addPoint", "Adds a point (one undo entry \"Add route point\"); default appended, continuing the path (last + (last - previous)); the option routes.pointLimit allows 11 points.", "routes")
                    .Arg("key", ArgTypes.Node, "the route", true)
                    .Arg("index", ArgTypes.Int, "1 … count (default the end)")
                    .Arg("pos", ArgTypes.Vec3, "route-local position (default the continued path)")
                    .Return("key", ArgTypes.String, "").Return("point", ArgTypes.String, "the new point's id (\"id\" is an envelope field)").Return("index", ArgTypes.Int, "").Return("count", ArgTypes.Int, "points afterwards")
                    .WithFlags(CommandFlags.Mutates), AddPoint);
            CommandKit.Register(r, app, CommandKit.Spec("route.deletePoint", "Deletes points (one undo entry \"Delete route point\"); the start point cannot be deleted.", "routes")
                    .Arg("key", ArgTypes.Node, "the route", true)
                    .Arg("point", ArgTypes.Any, "a point id or index").Arg("points", ArgTypes.ArrayOf(ArgTypes.Any), "several")
                    .Return("deleted", ArgTypes.ArrayOf(ArgTypes.String), "ids").Return("count", ArgTypes.Int, "points afterwards")
                    .WithFlags(CommandFlags.Mutates), DeletePoint);
            CommandKit.Register(r, app, CommandKit.Spec("route.setPoint", "Sets point members (one undo entry \"Route point\"): pos (not the start point), rot (the start point only), speed, ease (one of the 32 names), connection line / curve (curve writes the midpoint aid), link, aid {pos, init}, resetAid.", "routes")
                    .Arg("key", ArgTypes.Node, "the route", true)
                    .Arg("point", ArgTypes.Any, "a point id or index").Arg("points", ArgTypes.ArrayOf(ArgTypes.Any), "several (pos / rot / aid: the first)")
                    .Arg("pos", ArgTypes.Vec3, "route-local position").Arg("rot", ArgTypes.Euler, "rotation of the start point")
                    .Arg("speed", ArgTypes.Float, "stored as given (the UI's range 0.1-30)").Arg("ease", ArgTypes.String, "a StudioTween ease name")
                    .Arg("connection", ArgTypes.Enum("line", "curve"), "line or curve (curve writes the midpoint aid)").Arg("link", ArgTypes.Bool, "merge this Curve point into the previous curve chain")
                    .Arg("aid", ArgTypes.Object, "{pos (point-local), init}").Arg("resetAid", ArgTypes.Bool, "the midpoint rule again")
                    .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "").Return("noop", ArgTypes.ArrayOf(ArgTypes.String), "")
                    .Return("aid", ArgTypes.Object, "the first point's aid {pos, init} or null")
                    .WithFlags(CommandFlags.Mutates), SetPoint);
            CommandKit.Register(r, app, CommandKit.Spec("route.play", "Plays routes from the start (an ended one restarts; restart also restarts playing ones); not recorded.", "routes")
                    .Arg("key", ArgTypes.Node, "a route").Arg("keys", ArgTypes.ArrayOf(ArgTypes.Node), "several").Arg("all", ArgTypes.Bool, "every route of the scene")
                    .Arg("restart", ArgTypes.Bool, "also restart playing routes", false, false)
                    .Return("routes", ArgTypes.ArrayOf(ArgTypes.Object), "{key, state}")
                    .WithFlags(CommandFlags.Mutates), Play);
            CommandKit.Register(r, app, CommandKit.Spec("route.stop", "Stops routes (the child root back at the start point); not recorded.", "routes")
                    .Arg("key", ArgTypes.Node, "a route").Arg("keys", ArgTypes.ArrayOf(ArgTypes.Node), "several").Arg("all", ArgTypes.Bool, "every route of the scene")
                    .Return("routes", ArgTypes.ArrayOf(ArgTypes.Object), "{key, state}")
                    .WithFlags(CommandFlags.Mutates), Stop);
            CommandKit.Register(r, app, CommandKit.Spec("route.list", "Every route of the scene in pre-order with its state.", "routes")
                    .Return("routes", ArgTypes.ArrayOf(ArgTypes.Object), "{key, name, state, points, parent}")
                    .WithFlags(CommandFlags.ReadOnly), List);
        }

        // ------------------------------------------------------------ helpers

        static ExecuteOptions Exec(CoreContext c, string label) => CommandKit.Exec(c, label);

        static TreeNode RouteKey(CoreContext c, string arg = "key") => CommandKit.KeyKind.Route.Check(CommandKit.Node(c, arg), arg);

        static List<string> RouteKeys(CoreContext c, bool allowAll)
        {
            if (allowAll && c.Args.Get("all", false))
                return CommandKit.Ops(c).AllRoutes().Select(n => n.Id).ToList();
            // keys is read without the array check (a non-array answers as the args view converts it)
            return CommandKit.Ids(CommandKit.Keys(c, CommandKit.KeyKind.Route, out _, CommandKit.KeysRule.NoArrayCheck));
        }

        /// <summary>Point ids of point / points (ids or indices; resolved to ids of the route).</summary>
        static List<string> PointArgs(CoreContext c, RouteNodeState route)
        {
            string parg = FileRoots.OneOf(c, "point", "points");
            var raw = new List<string>();
            string Text(Newtonsoft.Json.Linq.JToken t) => t.Type == Newtonsoft.Json.Linq.JTokenType.Integer || t.Type == Newtonsoft.Json.Linq.JTokenType.String ? t.ToString() : null;
            Newtonsoft.Json.Linq.JToken tok;
            try { tok = Newtonsoft.Json.Linq.JToken.Parse(c.Args.RawJson(parg)); }
            catch (Exception) { throw CommandKit.BadArgs(parg, "a point id or index"); }
            if (parg == "point") raw.Add(Text(tok) ?? throw CommandKit.BadArgs(parg, "a point id or index"));
            else if (tok is Newtonsoft.Json.Linq.JArray arr) foreach (var t in arr) raw.Add(Text(t) ?? throw CommandKit.BadArgs(parg, "point ids or indices"));
            else throw CommandKit.BadArgs(parg, ArgTypes.ArrayOf(ArgTypes.Any));
            var l = new List<string>();
            foreach (var s in raw)
            {
                int i = StudioOps.PointIndex(route, s);
                if (i < 0) throw CommandKit.NotFound("no point \"" + s + "\" in the route", parg, route.Points.Select(p => p.Id).ToList());
                if (!l.Contains(route.Points[i].Id)) l.Add(route.Points[i].Id);
            }
            if (l.Count == 0) throw CommandKit.BadArgs(parg, "point ids or indices", "no point given");
            return l;
        }

        static double[] D3(Vec3d v) => new[] { v.X, v.Y, v.Z };

        static object Changes(OpResult r) => new BridgeReply().With("changed", r.Changed.ToList()).With("noop", r.NoOp.ToList());

        static object AidOf(RoutePoint p) => p?.Aid == null ? null : new Dictionary<string, object> { ["pos"] = CommandKit.D3(p.Aid.Pos), ["init"] = p.Aid.Init };

        // ------------------------------------------------------------ handlers

        static object Add(CoreContext c)
        {
            var parent = CommandKit.OptionalNode(c, "parent");
            string point = c.Args.Get<string>("point");
            int index = c.Args.Get("index", -1);
            Vector3? pos = c.Args.TryGet<Vector3>("pos", out var p) ? p : (Vector3?)null;
            Vector3? rot = c.Args.TryGet<Vector3>("rot", out var q) ? q : (Vector3?)null;
            var r = CommandKit.Check(c, CommandKit.Ops(c).AddRoute(parent?.Id, point, index, pos, rot, Exec(c, StudioOps.Labels.AddRoute)), "parent");
            string id = r.NewIds.Count > 0 ? r.NewIds[0] : null;
            if (id == null) throw new BridgeException(BridgeCodes.Internal, "the route was not added");
            c.Runtime.LateApply();
            return new BridgeReply().With("key", id).With("points", new List<string> { r.Value as string });
        }

        static object State(CoreContext c)
        {
            var n = RouteKey(c);
            c.Runtime.ApplyNow();
            return Describe(c.App, n, c.Args.Get("line", true));
        }

        /// <summary>route.state's reply for one route (also used by RT1 and the tests).</summary>
        public static BridgeReply Describe(AppCore app, TreeNode n, bool withLine)
        {
            var st = (RouteNodeState)n.State;
            var ops = StudioOps.For(app.Runtime);
            var real = ops.RouteReal(n.Id);
            var path = real?.Path ?? RoutePath.Build(st, RouteSettings.Options());
            var s = real?.Sample ?? default;
            var reply = new BridgeReply
            {
                ["key"] = n.Id,
                ["name"] = n.Label,
                ["playing"] = st.Playing,
                ["state"] = RoutePlayback.StateName(real?.PlayState ?? RoutePlayState.Stopped),
                ["time"] = real?.Time ?? 0.0,
                ["total"] = double.IsInfinity(path.TotalDuration) ? null : (object)path.TotalDuration,
                ["mode"] = path.Mode.ToString().ToLowerInvariant(),
                ["segment"] = s.Valid ? s.Segment : (object)null,
                ["pass"] = s.Valid ? s.Pass : (object)null,
                ["reversed"] = s.Valid && s.Reversed,
                ["percentage"] = s.Valid ? s.Percentage : (object)null,
                ["eased"] = s.Valid ? s.Eased : (object)null,
                ["loop"] = st.Loop,
                ["showLine"] = st.ShowLine,
                ["orient"] = st.Orient,
                ["color"] = CommandKit.C(st.Color),
                ["built"] = real != null,
            };
            var segs = new List<object>();
            foreach (var g in path.Segments)
                segs.Add(new Dictionary<string, object>
                {
                    ["index"] = g.Index, ["from"] = g.FromId, ["to"] = g.ToId, ["fromIndex"] = g.From, ["toIndex"] = g.To,
                    ["duration"] = double.IsInfinity(g.Duration) ? null : (object)g.Duration, ["length"] = g.Length,
                    ["speed"] = (double)g.Speed, ["ease"] = g.Ease, ["connection"] = g.Connection == RouteConnection.Curve ? "curve" : "line",
                    ["linked"] = g.LinkedIds.ToList(), ["lead"] = g.HasLead, ["controlPoints"] = g.Path.Length,
                });
            reply["segments"] = segs;
            var holder = real?.Holder;
            var pts = new List<object>();
            for (int i = 0; i < st.Points.Count; i++)
            {
                var p = st.Points[i];
                var d = new Dictionary<string, object>
                {
                    ["id"] = p.Id, ["index"] = i, ["pos"] = CommandKit.D3(p.Pos), ["rot"] = CommandKit.D3(p.Rot), ["speed"] = (double)p.Speed,
                    ["ease"] = p.Ease, ["connection"] = p.Connection, ["link"] = p.Link, ["aid"] = AidOf(p),
                };
                if (holder) d["world"] = CommandKit.D3(holder.TransformPoint(UnityObjects.V(p.Pos)));
                if (holder && i < path.Aids.Length) d["aidWorld"] = CommandKit.D3(holder.TransformPoint(new Vector3((float)path.Aids[i].X, (float)path.Aids[i].Y, (float)path.Aids[i].Z)));
                pts.Add(d);
            }
            reply["points"] = pts;
            var root = real?.ChildRoot;
            reply["childRoot"] = root
                ? new Dictionary<string, object>
                {
                    ["pos"] = CommandKit.D3(root.position), ["rot"] = CommandKit.D3(root.eulerAngles), ["rotQ"] = CommandKit.D4(root.rotation),
                    ["localPos"] = CommandKit.D3(root.localPosition), ["localRot"] = CommandKit.D3(root.localEulerAngles), ["localRotQ"] = CommandKit.D4(root.localRotation),
                }
                : null;
            if (withLine)
            {
                var verts = real != null ? real.LineVertices : RouteLine.Vertices(st, path);
                if (verts.Count == 0) reply["line"] = null;
                else
                {
                    var wl = new List<double[]>(verts.Count);
                    foreach (var v in verts)
                        wl.Add(holder ? CommandKit.D3(holder.TransformPoint(new Vector3((float)v.X, (float)v.Y, (float)v.Z))) : D3(v));
                    reply["line"] = new Dictionary<string, object> { ["count"] = verts.Count, ["expected"] = RouteLine.ExpectedCount(st), ["points"] = wl, ["world"] = holder != null };
                }
            }
            var findings = path.Findings.Select(f => (object)new Dictionary<string, object> { ["code"] = f.Code, ["path"] = f.Path, ["severity"] = f.Severity.ToString().ToLowerInvariant() }).ToList();
            reply["findings"] = findings;
            return reply;
        }

        static object Set(CoreContext c)
        {
            var keys = RouteKeys(c, false);
            var values = new List<(string, object)>();
            if (c.Args.TryGet<bool>("loop", out var loop)) values.Add(("loop", loop));
            if (c.Args.TryGet<bool>("showLine", out var sl)) values.Add(("showLine", sl));
            if (c.Args.TryGet<string>("orient", out var or))
            {
                if (or != "none" && or != "xy" && or != "y") throw CommandKit.BadArgs("orient", "none, xy or y");
                values.Add(("orient", or));
            }
            if (c.Args.Has("color"))
            {
                var arr = c.Args.Get<float[]>("color");
                if (arr == null || (arr.Length != 3 && arr.Length != 4)) throw CommandKit.BadArgs("color", "[r, g, b(, a)]");
                values.Add(("color", new Color4(arr[0], arr[1], arr[2], arr.Length > 3 ? arr[3] : 1f)));
            }
            if (values.Count == 0) throw CommandKit.BadArgs("loop", "one of loop, showLine, orient, color", "nothing to set");
            var r = CommandKit.Check(c, CommandKit.Ops(c).SetRoute(keys, values, Exec(c, StudioOps.Labels.Route)));
            return Changes(r);
        }

        static object AddPoint(CoreContext c)
        {
            var n = RouteKey(c);
            int? index = c.Args.TryGet<int>("index", out var i) ? i : (int?)null;
            Vector3? pos = c.Args.TryGet<Vector3>("pos", out var p) ? p : (Vector3?)null;
            var r = CommandKit.Check(c, CommandKit.Ops(c).AddRoutePoint(n.Id, index, pos, Exec(c, StudioOps.Labels.AddRoutePoint)));
            var st = (RouteNodeState)n.State;
            string id = r.Value as string;
            return new BridgeReply().With("key", n.Id).With("point", id).With("index", StudioOps.PointIndex(st, id)).With("count", st.Points.Count);
        }

        static object DeletePoint(CoreContext c)
        {
            var n = RouteKey(c);
            var st = (RouteNodeState)n.State;
            var ids = PointArgs(c, st);
            var r = CommandKit.Check(c, CommandKit.Ops(c).DeleteRoutePoints(n.Id, ids, Exec(c, StudioOps.Labels.DeleteRoutePoint)));
            return new BridgeReply().With("deleted", r.Value as List<string> ?? new List<string>()).With("count", st.Points.Count);
        }

        static object SetPoint(CoreContext c)
        {
            var n = RouteKey(c);
            var st = (RouteNodeState)n.State;
            var ids = PointArgs(c, st);
            var ops = CommandKit.Ops(c);
            var values = new List<(string, object)>();
            if (c.Args.TryGet<float>("speed", out var sp)) values.Add(("speed", sp));
            if (c.Args.TryGet<string>("ease", out var ease))
            {
                if (!RouteEases.TryParse(ease, out _)) throw new BridgeException(BridgeError.BadArgs("ease", "one of the 32 StudioTween ease names", "unknown ease \"" + ease + "\""));
                values.Add(("ease", ease));
            }
            if (c.Args.TryGet<string>("connection", out var conn))
            {
                if (conn != "line" && conn != "curve") throw CommandKit.BadArgs("connection", "line or curve");
                values.Add(("connection", conn));
            }
            if (c.Args.TryGet<bool>("link", out var link)) values.Add(("link", link));
            bool hasPos = c.Args.TryGet<Vector3>("pos", out var pos), hasRot = c.Args.TryGet<Vector3>("rot", out var rot);
            var aidArgs = c.Args.Nested("aid");
            bool reset = c.Args.Get("resetAid", false);
            if (values.Count == 0 && !hasPos && !hasRot && aidArgs == null && !reset)
                throw CommandKit.BadArgs("speed", "one of pos, rot, speed, ease, connection, link, aid, resetAid", "nothing to set");
            var all = OpResult.NothingToDo();
            c.Runtime.ApplyNow();
            using (var tx = CommandKit.Tx(c, StudioOps.Labels.RoutePoint))
            {
                var o = Exec(c, StudioOps.Labels.RoutePoint);
                void Merge(OpResult r)
                {
                    foreach (var x in r.Changed) if (!all.Changed.Contains(x)) all.Changed.Add(x);
                    foreach (var x in r.NoOp) if (!all.NoOp.Contains(x)) all.NoOp.Add(x);
                }
                if (hasPos || hasRot) Merge(CommandKit.Check(c, ops.SetRoutePointPose(n.Id, ids[0], hasPos ? pos : (Vector3?)null, hasRot ? rot : (Vector3?)null, o)));
                if (values.Count > 0) Merge(CommandKit.Check(c, ops.SetRoutePoints(n.Id, ids, values, o)));
                if (aidArgs != null)
                {
                    int i = StudioOps.PointIndex(st, ids[0]);
                    var cur = st.Points[i].Aid;
                    var ap = aidArgs.TryGet<Vector3>("pos", out var apv) ? apv : cur != null ? UnityObjects.V(cur.Pos) : Vector3.zero;
                    bool init = aidArgs.Get("init", true);
                    var aid = new RouteAid { Pos = UnityObjects.V(ap), Init = init };
                    var res = tx.Set(n.Id, "route.points[" + i + "].aid", aid);
                    if (res.Status == CommandStatus.Error) CommandKit.Check(c, OpResult.From(res, "aid"));
                    (res.Status == CommandStatus.NoOp ? all.NoOp : all.Changed).Add("route.points[" + i + "].aid");
                }
                if (reset) Merge(CommandKit.Check(c, ops.ResetRouteAid(n.Id, ids, o)));
                CommandKit.Commit(tx);
            }
            c.Runtime.ApplyNow();
            var reply = (BridgeReply)Changes(all);
            reply["aid"] = AidOf(st.Points[StudioOps.PointIndex(st, ids[0])]);
            return reply;
        }

        static object States(CoreContext c, OpResult r)
        {
            var states = r.Value as Dictionary<string, string> ?? new Dictionary<string, string>();
            var l = new List<object>();
            foreach (var kv in states) l.Add(new Dictionary<string, object> { ["key"] = kv.Key, ["state"] = kv.Value });
            return new BridgeReply().With("routes", l);
        }

        static object Play(CoreContext c)
        {
            bool restart = c.Args.Get("restart", false);
            var ops = CommandKit.Ops(c);
            OpResult r;
            if (c.Args.Get("all", false)) r = ops.PlayAllRoutes(restart, Exec(c, null));
            else r = ops.PlayRoutes(RouteKeys(c, false), restart, Exec(c, null));
            CommandKit.Check(c, r, "key");
            c.Runtime.LateApply();
            return States(c, r);
        }

        static object Stop(CoreContext c)
        {
            var ops = CommandKit.Ops(c);
            var r = c.Args.Get("all", false) ? ops.StopAllRoutes(Exec(c, null)) : ops.StopRoutes(RouteKeys(c, false), Exec(c, null));
            CommandKit.Check(c, r, "key");
            c.Runtime.LateApply();
            return States(c, r);
        }

        static object List(CoreContext c)
        {
            var ops = CommandKit.Ops(c);
            c.Runtime.ApplyNow();
            var l = new List<object>();
            foreach (var n in ops.AllRoutes())
                l.Add(new Dictionary<string, object>
                {
                    ["key"] = n.Id, ["name"] = n.Label, ["state"] = RoutePlayback.StateName(ops.RouteState(n.Id)),
                    ["points"] = ((RouteNodeState)n.State).Points.Count, ["parent"] = n.Parent?.Id, ["playing"] = ((RouteNodeState)n.State).Playing,
                });
            return new BridgeReply().With("routes", l);
        }
    }
}

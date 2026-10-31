// StudioOps, routes (Phase 3.6 package R): add route / point, delete points (never the
// start point: the guard the 2.2 list commands lack), route and point setters (a switch to Curve writes the aid at the
// midpoint to the next point, point-local, in the same entry), point / aid poses (rot only on the start point, pos only
// on the others), reset aid, and the transport: play / stop / play all / stop all write `route.playing` without history
// (R7: like the current view; the value is saved, so the file is dirty) and drive RoutePlayback at once. Every edit is one
// entry (BeginOrJoin: a UI drag or a bridge batch absorbs it). Ranges are stored as given (the UI clamps speed to
// 0.1–30). The point limit is the option routes.pointLimit (RouteSettings.PointLimit, R9: at most 11 points).
using System;
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Scenes.Routes;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed partial class StudioOps
    {
        /// <summary>Members of SetRoute (relative to `route.`).</summary>
        public static readonly string[] RouteMembers = { "loop", "showLine", "orient", "color" };
        /// <summary>Members of SetRoutePoints (relative to `route.points[i].`).</summary>
        public static readonly string[] RoutePointMembers = { "speed", "ease", "connection", "link" };

        // ------------------------------------------------------------ lookup

        /// <summary>The route node <paramref name="id"/> (not_found / wrong_kind otherwise).</summary>
        TreeNode RouteNode(string id, string arg, out OpResult fail)
        {
            var n = Find(id, arg, out fail);
            if (n == null) return null;
            if (n.Kind != NodeKind.Route || !(n.State is RouteNodeState))
            {
                fail = OpResult.WrongKind("node " + n.Id + " is a " + KindName(n) + ", not a route", arg, "route");
                return null;
            }
            return n;
        }

        static RouteNodeState RouteOf(TreeNode n) => (RouteNodeState)n.State;

        /// <summary>The index of point <paramref name="pointId"/> in the route (also a decimal index), else -1.</summary>
        public static int PointIndex(RouteNodeState r, string pointId)
        {
            if (r?.Points == null || pointId == null) return -1;
            int i = r.Points.FindIndex(p => p?.Id == pointId);
            if (i >= 0) return i;
            return int.TryParse(pointId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int k) && k >= 0 && k < r.Points.Count ? k : -1;
        }

        List<int> PointIndices(RouteNodeState r, IEnumerable<string> pointIds, string arg, out OpResult fail)
        {
            fail = null;
            var l = new List<int>();
            foreach (var pid in pointIds ?? Array.Empty<string>())
            {
                int i = PointIndex(r, pid);
                if (i < 0)
                {
                    var ids = new List<string>();
                    foreach (var p in r.Points) ids.Add(p.Id);
                    fail = OpResult.NotFound("no point \"" + pid + "\" in the route", arg, ids);
                    return null;
                }
                if (!l.Contains(i)) l.Add(i);
            }
            if (l.Count == 0) fail = OpResult.BadArgs(arg, "point ids", "no point given");
            return fail == null ? l : null;
        }

        /// <summary>The route's realization (built, in the model) or null.</summary>
        public RouteRealization RouteReal(string id)
        {
            var r = rt.Find(id) as RouteRealization;
            return r != null && r.State == RealizationState.Built && rt.IsInModel(r) ? r : null;
        }

        /// <summary>The route's playback state (stopped when it is not built).</summary>
        public RoutePlayState RouteState(string id) => RouteReal(id)?.PlayState ?? RoutePlayState.Stopped;

        // ------------------------------------------------------------ adds

        /// <summary>
        /// Adds a route (one start point at route-local zero; placed like the other adds: the camera pivot by the placement
        /// option at the root) in one entry "Add route"; <paramref name="pos"/> / <paramref name="rot"/> set its transform in
        /// the same entry. NewIds = [the route]; Value = the start point's id.
        /// </summary>
        public OpResult AddRoute(string parentId = null, string point = null, int index = -1, Vector3? pos = null, Vector3? rot = null, ExecuteOptions o = default)
        {
            o = Labelled(o, Labels.AddRoute);
            if (pos == null && rot == null)
            {
                var r0 = OpResult.From(Session.AddRoute(parentId, point, index, o), "parent");
                rt.ApplyNow();
                if (r0.Ok && r0.NewIds.Count > 0) r0.Value = (Model.Find(r0.NewIds[0])?.State as RouteNodeState)?.Points[0].Id;
                return r0;
            }
            var tx = Open(o.Label, o, out var fail);
            if (tx == null) return fail;
            OpResult r;
            string id;
            using (tx)
            {
                var o2 = o;
                o2.Record = HistoryMode.Record;
                var res = Session.AddRoute(parentId, point, index, o2);
                if (res.Status == CommandStatus.Error) return OpResult.From(res, "parent");
                id = res.NewIds != null && res.NewIds.Count > 0 ? res.NewIds[0] : null;
                r = OpResult.From(res);
                if (id == null) return OpResult.Fail(CommandCodes.Internal, "the route was not added");
                var n = Model.Find(id);
                var e = SetTransformSteps(tx, n, pos, rot, null, r);
                if (e != null) return e;
                r = Commit(tx, r);
                if (r.NewIds.Count == 0) r.NewIds = res.NewIds;
            }
            rt.ApplyNow();
            r.Value = (Model.Find(id)?.State as RouteNodeState)?.Points[0].Id;
            return r;
        }

        /// <summary>
        /// Adds a point ("Add route point"): refused (unavailable, "point limit") when the option routes.pointLimit is on and
        /// the route has 11 points; <paramref name="index"/> default = appended (1 … count); <paramref name="pos"/> default R8
        /// (last + (last − previous), or start + (0, 0, 10) with only the start point; inside the list the neighbours' midpoint).
        /// The new point becomes the sub-selection. Value = the new point's id.
        /// </summary>
        public OpResult AddRoutePoint(string routeId, int? index = null, Vector3? pos = null, ExecuteOptions o = default)
        {
            var n = RouteNode(routeId, "key", out var fail);
            if (n == null) return fail;
            var route = RouteOf(n);
            if (!RouteGeometry.CanAddPoint(route, RouteSettings.PointLimit))
                return OpResult.Unavailable("the route has " + route.Points.Count + " points: the point limit (option routes.pointLimit) allows " + RouteGeometry.PointLimit, "key", "pointLimit");
            int i = index ?? route.Points.Count;
            if (i < 1 || i > route.Points.Count) return OpResult.BadArgs("index", "1–" + route.Points.Count, "a point is inserted after the start point (index 1 … " + route.Points.Count + ")");
            if (pos != null && !Finite(pos.Value)) return OpResult.BadArgs("pos", "vec3", "finite numbers");
            var at = pos != null ? UnityObjects.V(pos.Value) : RouteGeometry.InsertPointPosition(route, i);
            var pt = new RoutePoint { Id = Session.NewId(), Pos = at };
            var tx = Open(Labels.AddRoutePoint, o, out fail);
            if (tx == null) return fail;
            OpResult r = OpResult.NothingToDo();
            using (tx)
            {
                var s = tx.Execute(new ListInsertCommand(n.Id, "route.points", i, pt, Labels.AddRoutePoint));
                if (s.Status == CommandStatus.Error) return OpResult.From(s, "index");
                r.Record(n.Id, "route.points", s.Status);
                r = Commit(tx, r);
            }
            rt.ApplyNow();
            if (r.Ok) Select(new[] { n.Id }, false, n.Id + "/points/" + pt.Id);
            r.Value = pt.Id;
            return r;
        }

        /// <summary>
        /// Deletes points ("Delete route point", one entry): the start point (index 0) is refused (wrong_kind); the route keeps
        /// playing with the rebuilt path (τ continues). Value = the deleted ids in index order.
        /// </summary>
        public OpResult DeleteRoutePoints(string routeId, IEnumerable<string> pointIds, ExecuteOptions o = default)
        {
            var n = RouteNode(routeId, "key", out var fail);
            if (n == null) return fail;
            var route = RouteOf(n);
            var idx = PointIndices(route, pointIds, "point", out fail);
            if (idx == null) return fail;
            if (idx.Contains(0)) return OpResult.WrongKind("the start point cannot be deleted", "point", "a point other than the start point");
            idx.Sort();
            var ids = new List<string>();
            foreach (int i in idx) ids.Add(route.Points[i].Id);
            var tx = Open(Labels.DeleteRoutePoint, o, out fail);
            if (tx == null) return fail;
            OpResult r = OpResult.NothingToDo();
            using (tx)
            {
                for (int k = idx.Count - 1; k >= 0; k--)
                {
                    var s = tx.Execute(new ListRemoveCommand(n.Id, "route.points", idx[k], Labels.DeleteRoutePoint));
                    if (s.Status == CommandStatus.Error) return OpResult.From(s, "point");
                    r.Record(n.Id, "route.points", s.Status);
                }
                r = Commit(tx, r);
            }
            rt.ApplyNow();
            r.Value = ids;
            return r;
        }

        // ------------------------------------------------------------ setters

        /// <summary>Route members (loop, showLine, orient, color) on every route in one entry "Route" (U25). Changed / NoOp: canonical paths.</summary>
        public OpResult SetRoute(IEnumerable<string> ids, IList<(string member, object value)> values, ExecuteOptions o = default)
        {
            if (values == null || values.Count == 0) return OpResult.BadArgs("set", "one of " + string.Join(", ", RouteMembers), "nothing to set");
            var plan = new List<(string, object)>();
            foreach (var (m, v) in values)
            {
                string member = m != null && m.StartsWith("route.", StringComparison.Ordinal) ? m.Substring(6) : m;
                if (Array.IndexOf(RouteMembers, member) < 0) return OpResult.BadArgs(m ?? "set", "one of " + string.Join(", ", RouteMembers), "\"" + m + "\" is not a route member");
                plan.Add(("route." + member, v));
            }
            var targets = new List<string>();
            foreach (var id in ids ?? Array.Empty<string>())
            {
                var n = RouteNode(id, "keys", out var fail);
                if (n == null) return fail;
                if (!targets.Contains(n.Id)) targets.Add(n.Id);
            }
            if (targets.Count == 0) return OpResult.BadArgs("keys", "route ids", "no route given");
            return SetMany(targets, plan, Labels.Route, o, p => p.Substring(6));
        }

        /// <summary>
        /// Point members (speed, ease, connection, link) on the given points of one route in one entry "Route point"; a switch
        /// to `curve` of a point whose aid is null or not initialised writes the aid at the midpoint to the next point
        /// (point-local, the start point's rotation respected) with init true in the same entry.
        /// </summary>
        public OpResult SetRoutePoints(string routeId, IEnumerable<string> pointIds, IList<(string member, object value)> values, ExecuteOptions o = default)
        {
            var n = RouteNode(routeId, "key", out var fail);
            if (n == null) return fail;
            var route = RouteOf(n);
            var idx = PointIndices(route, pointIds, "point", out fail);
            if (idx == null) return fail;
            if (values == null || values.Count == 0) return OpResult.BadArgs("set", "one of " + string.Join(", ", RoutePointMembers), "nothing to set");
            foreach (var (m, _) in values)
                if (Array.IndexOf(RoutePointMembers, m) < 0) return OpResult.BadArgs(m ?? "set", "one of " + string.Join(", ", RoutePointMembers), "\"" + m + "\" is not a route point member");
            rt.ApplyNow();
            var tx = Open(Labels.RoutePoint, o, out fail);
            if (tx == null) return fail;
            var r = OpResult.NothingToDo();
            using (tx)
            {
                foreach (int i in idx)
                {
                    string head = "route.points[" + i + "].";
                    foreach (var (m, v) in values)
                    {
                        var s = tx.Set(n.Id, head + m, v);
                        if (s.Status == CommandStatus.Error) return OpResult.From(s, m);
                        r.Record(n.Id, head + m, s.Status);
                        if (m == "connection" && v is string c && c == "curve")
                        {
                            var p = route.Points[i];
                            if (p.Aid == null || !p.Aid.Init)
                            {
                                var aid = new RouteAid { Pos = RouteGeometry.MidpointAidLocal(route, i), Init = true };
                                var sa = tx.Set(n.Id, head + "aid", aid);
                                if (sa.Status == CommandStatus.Error) return OpResult.From(sa, "aid");
                                r.Record(n.Id, head + "aid", sa.Status);
                            }
                        }
                    }
                }
                r = Commit(tx, r);
            }
            rt.ApplyNow();
            return r;
        }

        /// <summary>
        /// A point's pose (gizmo and typed edits, "Route point"): <paramref name="rot"/> only on the start point,
        /// <paramref name="pos"/> only on the others (bad_args otherwise); route-local. Joins an open drag.
        /// </summary>
        public OpResult SetRoutePointPose(string routeId, string pointId, Vector3? pos, Vector3? rot, ExecuteOptions o = default)
        {
            var n = RouteNode(routeId, "key", out var fail);
            if (n == null) return fail;
            var route = RouteOf(n);
            int i = PointIndex(route, pointId);
            if (i < 0) return OpResult.NotFound("no point \"" + pointId + "\" in the route", "point");
            if (pos == null && rot == null) return OpResult.BadArgs("pos", "pos or rot", "nothing to set");
            if (pos != null && i == 0) return OpResult.BadArgs("pos", "rot", "the start point has a rotation only (it sits at the route's origin)");
            if (rot != null && i != 0) return OpResult.BadArgs("rot", "pos", "only the start point has a rotation");
            if (pos != null && !Finite(pos.Value)) return OpResult.BadArgs("pos", "vec3", "finite numbers");
            if (rot != null && !Finite(rot.Value)) return OpResult.BadArgs("rot", "euler", "finite numbers");
            string path = "route.points[" + i + "]." + (pos != null ? "pos" : "rot");
            object v = pos != null ? UnityObjects.V(pos.Value) : UnityObjects.V(rot.Value);
            return SetMany(new[] { n.Id }, new List<(string, object)> { (path, v) }, Labels.RoutePoint, o, _ => pos != null ? "pos" : "rot");
        }

        /// <summary>A point's aid position (point-local; init true), "Route point". Joins an open drag.</summary>
        public OpResult SetRouteAid(string routeId, string pointId, Vector3 pos, ExecuteOptions o = default)
        {
            var n = RouteNode(routeId, "key", out var fail);
            if (n == null) return fail;
            int i = PointIndex(RouteOf(n), pointId);
            if (i < 0) return OpResult.NotFound("no point \"" + pointId + "\" in the route", "point");
            if (!Finite(pos)) return OpResult.BadArgs("aid", "vec3", "finite numbers");
            var aid = new RouteAid { Pos = UnityObjects.V(pos), Init = true };
            return SetMany(new[] { n.Id }, new List<(string, object)> { ("route.points[" + i + "].aid", aid) }, Labels.RoutePoint, o, _ => "aid");
        }

        /// <summary>The midpoint rule of the aid again on the given points ("Route point"; HS2 has no such button).</summary>
        public OpResult ResetRouteAid(string routeId, IEnumerable<string> pointIds, ExecuteOptions o = default)
        {
            var n = RouteNode(routeId, "key", out var fail);
            if (n == null) return fail;
            var route = RouteOf(n);
            var idx = PointIndices(route, pointIds, "point", out fail);
            if (idx == null) return fail;
            var plan = new List<(string, object)>();
            foreach (int i in idx) plan.Add(("route.points[" + i + "].aid", new RouteAid { Pos = RouteGeometry.MidpointAidLocal(route, i), Init = true }));
            return SetMany(new[] { n.Id }, plan, Labels.RoutePoint, o, _ => "point");
        }

        // ------------------------------------------------------------ transport (not recorded, R7)

        static ExecuteOptions Transport(ExecuteOptions o) => new ExecuteOptions { Record = HistoryMode.None, Source = o.Source };

        /// <summary>
        /// Plays routes: a stopped route starts at τ 0, an ended one restarts, a playing one restarts only with
        /// <paramref name="restart"/>. A route with one point is refused (unavailable, finding route.onePoint) when it is the
        /// only one asked for, else skipped with a warning. Not recorded. Value = the routes' states (id → state).
        /// </summary>
        public OpResult PlayRoutes(IEnumerable<string> ids, bool restart = false, ExecuteOptions o = default)
        {
            var list = new List<TreeNode>();
            foreach (var id in ids ?? Array.Empty<string>())
            {
                var n = RouteNode(id, "keys", out var fail);
                if (n == null) return fail;
                if (!list.Contains(n)) list.Add(n);
            }
            return Play(list, restart, list.Count == 1, o);
        }

        OpResult Play(List<TreeNode> list, bool restart, bool single, ExecuteOptions o)
        {
            rt.ApplyNow();
            var r = OpResult.NothingToDo();
            var states = new Dictionary<string, string>(StringComparer.Ordinal);
            var pb = rt.GetPart<RoutePlayback>();
            foreach (var n in list)
            {
                var route = RouteOf(n);
                var real = RouteReal(n.Id);
                if (route.Points.Count < 2)
                {
                    rt.ReportOnce(FindingSeverity.Info, "route.onePoint", n.Id, "a route with one point cannot play");
                    if (single) return OpResult.Unavailable("route " + n.Id + " has one point: it cannot play (route.onePoint)", "key", "onePoint");
                    r.Warnings.Add("route " + n.Id + " has one point: skipped (route.onePoint)");
                    continue;
                }
                if (real == null)
                {
                    if (single) return OpResult.Unavailable("route " + n.Id + " is not realized", "key", rt.Find(n.Id)?.State.ToString() ?? "NotRealized");
                    r.Warnings.Add("route " + n.Id + " is not realized: skipped");
                    continue;
                }
                var before = real.PlayState;
                if (!route.Playing)
                {
                    var s = Session.Set(n.Id, "route.playing", true, Transport(o));
                    if (s.Status == CommandStatus.Error) return OpResult.From(s, "keys");
                    pb?.Restart(real);
                    r.Record(n.Id, "route.playing", CommandStatus.Ok);
                }
                else if (restart || before != RoutePlayState.Playing)
                {
                    pb?.Restart(real);
                    r.Record(n.Id, "route.playing", CommandStatus.Ok);
                }
                else r.Record(n.Id, "route.playing", CommandStatus.NoOp);
                states[n.Id] = RoutePlayback.StateName(real.PlayState);
            }
            rt.ApplyNow();
            r = r.With(r.Changed.Count > 0 ? CommandResult.Success(null) : CommandResult.NoOp(null));
            r.Value = states;
            return r;
        }

        /// <summary>Stops routes (the child root back at the start point's pose; not recorded). Value = id → state.</summary>
        public OpResult StopRoutes(IEnumerable<string> ids, ExecuteOptions o = default)
        {
            var list = new List<TreeNode>();
            foreach (var id in ids ?? Array.Empty<string>())
            {
                var n = RouteNode(id, "keys", out var fail);
                if (n == null) return fail;
                if (!list.Contains(n)) list.Add(n);
            }
            return Stop(list, o);
        }

        OpResult Stop(List<TreeNode> list, ExecuteOptions o)
        {
            rt.ApplyNow();
            var r = OpResult.NothingToDo();
            var states = new Dictionary<string, string>(StringComparer.Ordinal);
            var pb = rt.GetPart<RoutePlayback>();
            foreach (var n in list)
            {
                var route = RouteOf(n);
                var real = RouteReal(n.Id);
                bool changed = false;
                if (route.Playing)
                {
                    var s = Session.Set(n.Id, "route.playing", false, Transport(o));
                    if (s.Status == CommandStatus.Error) return OpResult.From(s, "keys");
                    changed = true;
                }
                if (real != null && real.PlayState != RoutePlayState.Stopped) { pb?.Stop(real); changed = true; }
                r.Record(n.Id, "route.playing", changed ? CommandStatus.Ok : CommandStatus.NoOp);
                states[n.Id] = RoutePlayback.StateName(real?.PlayState ?? RoutePlayState.Stopped);
            }
            rt.ApplyNow();
            r = r.With(r.Changed.Count > 0 ? CommandResult.Success(null) : CommandResult.NoOp(null));
            r.Value = states;
            return r;
        }

        /// <summary>Every route of the scene in pre-order.</summary>
        public List<TreeNode> AllRoutes()
        {
            var l = new List<TreeNode>();
            foreach (var n in Model.PreOrder()) if (n.Kind == NodeKind.Route && n.State is RouteNodeState) l.Add(n);
            return l;
        }

        /// <summary>The route list's Play all (the stopped and ended ones) or, with <paramref name="restart"/>, Restart all.</summary>
        public OpResult PlayAllRoutes(bool restart = false, ExecuteOptions o = default)
        {
            var l = new List<TreeNode>();
            foreach (var n in AllRoutes())
                if (restart || RouteState(n.Id) != RoutePlayState.Playing) l.Add(n);
            return Play(l, restart, false, o);
        }

        /// <summary>The route list's Stop all.</summary>
        public OpResult StopAllRoutes(ExecuteOptions o = default) => Stop(AllRoutes(), o);
    }
}

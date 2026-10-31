// The route actions of the Studio UI (Phase 3.6 package R): add.route (replacing the
// disabled 3.5 registrations: a later registration of an id wins), the route context actions route.play / stop / addPoint /
// deletePoint / resetAid / playAll / restartAll / stopAll (P's manifest ids; the tree's context menu, the inspector's
// buttons and the scene panel's route list run them), view.routes (the view toggle of route lines and markers, option
// view.routes) and edit.delete for a sub-selected route point (Delete on a point row deletes the point; the start point is
// refused with a toast; otherwise the 3.5 behaviour, WorkspaceModule.Delete). Targets: the action's `key` / `point`
// arguments (ui.action), else the selected routes and the sub-selected point. The module keeps RouteSettings (the
// runtime's route options) in step with the options routes.timing / orientSmoothing / pointLimit / lineWidth.
using System.Collections.Generic;
using System.Linq;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Routes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;

namespace HS2Next.UI.Panels
{
    public sealed class RouteUiModule : IUiModule
    {
        /// <summary>After WorkspaceModule (100) and InspectorModule (200): this module's add.route / edit.delete win.</summary>
        public int Order => 250;

        public void Register(UiContext ctx)
        {
            var a = ctx.Registry.Actions;
            bool M(string id) => ActionManifest.Contains(id);

            a.Register(ActionIds.AddRoute, _ =>
            {
                PanelKit.ApplyPlacement(ctx);
                var r = ctx.ShowResult(ctx.Ops.AddRoute(null, null, -1, null, null, UiContext.User()));
                if (r != null && r.Ok && r.NewIds.Count > 0) ctx.Selection.Set(new[] { r.NewIds[0] });   // U14: the new route is selected
                return r;
            });

            if (M(ActionIds.RoutePlay)) a.Register(ActionIds.RoutePlay, call => ctx.ShowResult(ctx.Ops.PlayRoutes(Routes(ctx, call), false, UiContext.User())), () => Routes(ctx, null).Any(id => CanPlay(ctx, id)));
            if (M(ActionIds.RouteStop)) a.Register(ActionIds.RouteStop, call => ctx.ShowResult(ctx.Ops.StopRoutes(Routes(ctx, call), UiContext.User())), () => Routes(ctx, null).Count > 0);
            if (M(ActionIds.RouteAddPoint))
                a.Register(ActionIds.RouteAddPoint, call =>
                {
                    var id = Routes(ctx, call).FirstOrDefault();
                    return id == null ? null : ctx.ShowResult(ctx.Ops.AddRoutePoint(id, null, null, UiContext.User(OpLabels.AddRoutePoint)));
                }, () => { var id = Routes(ctx, null).FirstOrDefault(); return id != null && ctx.Model.Find(id)?.State is RouteNodeState st && RouteGeometry.CanAddPoint(st, RouteSettings.PointLimit); });
            if (M(ActionIds.RouteDeletePoint))
                a.Register(ActionIds.RouteDeletePoint, call =>
                {
                    if (!Point(ctx, call, out var rid, out var pid, out int index)) return null;
                    if (index == 0) { ctx.Toasts?.Show(L10n.Get("route.startNotDeletable"), ToastKind.Warning); return null; }
                    return ctx.ShowResult(ctx.Ops.DeleteRoutePoints(rid, new[] { pid }, UiContext.User(OpLabels.DeleteRoutePoint)));
                }, () => Point(ctx, null, out _, out _, out int i) && i > 0);
            if (M(ActionIds.RouteResetAid))
                a.Register(ActionIds.RouteResetAid, call =>
                    Point(ctx, call, out var rid, out var pid, out _) ? ctx.ShowResult(ctx.Ops.ResetRouteAid(rid, new[] { pid }, UiContext.User(OpLabels.RoutePoint))) : null,
                    () => Point(ctx, null, out var rid, out _, out int i) && ctx.Model.Find(rid)?.State is RouteNodeState st && st.Points[i].ConnectionType == RouteConnection.Curve);
            if (M(ActionIds.RoutePlayAll)) a.Register(ActionIds.RoutePlayAll, _ => ctx.ShowResult(ctx.Ops.PlayAllRoutes(false, UiContext.User())), () => ctx.Ops.AllRoutes().Count > 0);
            if (M(ActionIds.RouteRestartAll)) a.Register(ActionIds.RouteRestartAll, _ => ctx.ShowResult(ctx.Ops.PlayAllRoutes(true, UiContext.User())), () => ctx.Ops.AllRoutes().Count > 0);
            if (M(ActionIds.RouteStopAll)) a.Register(ActionIds.RouteStopAll, _ => ctx.ShowResult(ctx.Ops.StopAllRoutes(UiContext.User())), () => ctx.Ops.AllRoutes().Count > 0);
            if (M(ActionIds.ViewRoutes))
            {
                var d = a.Register(ActionIds.ViewRoutes, call =>
                {
                    bool on = call.Arg("on") is bool b ? b : !ctx.Options.Bool(OptionDefs.ViewRoutes);
                    ctx.Options.Set(OptionDefs.ViewRoutes, on);
                    return on;
                });
                d.IsChecked = () => ctx.Options.Bool(OptionDefs.ViewRoutes);
            }

            // Delete on a sub-selected route point deletes the point (the start point: refused with a toast)
            a.Register(ActionIds.Delete, _ =>
            {
                if (SubPoint(ctx, out var rid, out var pid, out int index))
                {
                    if (index == 0) { ctx.Toasts?.Show(L10n.Get("route.startNotDeletable"), ToastKind.Warning); return null; }
                    return ctx.ShowResult(ctx.Ops.DeleteRoutePoints(rid, new[] { pid }, UiContext.User(OpLabels.DeleteRoutePoint)));
                }
                WorkspaceModule.Delete(ctx);
                return null;
            }, () => ctx.Selection.Count > 0);

            SyncSettings(ctx);
            ctx.Options.Changed += k => { if (k != null && k.StartsWith("routes.", System.StringComparison.Ordinal)) SyncSettings(ctx); };
        }

        /// <summary>RouteSettings from the options routes.* (the runtime's route options).</summary>
        public static void SyncSettings(UiContext ctx)
        {
            RouteSettings.Timing = ctx.Options.String(OptionDefs.RoutesTiming) == "length" ? RouteTiming.Length : RouteTiming.Hs2;
            RouteSettings.OrientSmoothing = ctx.Options.Bool(OptionDefs.RoutesOrientSmoothing);
            RouteSettings.PointLimit = ctx.Options.Bool(OptionDefs.RoutesPointLimit);
            float w = ctx.Options.Float(OptionDefs.RoutesLineWidth);
            RouteSettings.LineWidth = w > 0f ? w : 1f;
        }

        static bool CanPlay(UiContext ctx, string id) => ctx.Model.Find(id)?.State is RouteNodeState st && st.Points.Count >= 2;

        /// <summary>The action's routes: its `key` (or `keys`) argument, else the selected routes.</summary>
        public static List<string> Routes(UiContext ctx, ActionInvocation call)
        {
            var l = new List<string>();
            if (call?.Arg("key") is string k) { if (ctx.Model.Find(k)?.Kind == NodeKind.Route) l.Add(k); return l; }
            foreach (var id in ctx.Selection.Ids)
                if (ctx.Model.Find(id)?.Kind == NodeKind.Route) l.Add(id);
            return l;
        }

        /// <summary>The sub-selected route point (an aid address names its point).</summary>
        public static bool SubPoint(UiContext ctx, out string routeId, out string pointId, out int index)
        {
            index = -1;
            routeId = pointId = null;
            var sub = ctx.SubSelection;
            if (sub == null || !StudioOps.TryParseRouteSub(sub, out routeId, out pointId, out _)) return false;
            index = StudioOps.PointIndex(ctx.Model.Find(routeId)?.State as RouteNodeState, pointId);
            return index >= 0;
        }

        /// <summary>The action's point: `key` + `point` (id or index) arguments, else the sub-selection.</summary>
        static bool Point(UiContext ctx, ActionInvocation call, out string routeId, out string pointId, out int index)
        {
            if (call?.Arg("key") is string k && call.Arg("point") != null && ctx.Model.Find(k)?.State is RouteNodeState st)
            {
                routeId = k;
                index = StudioOps.PointIndex(st, call.Arg("point").ToString());
                pointId = index >= 0 ? st.Points[index].Id : null;
                return index >= 0;
            }
            return SubPoint(ctx, out routeId, out pointId, out index);
        }
    }
}

// The scene panel's Routes section (vanilla's route list window; Phase 3.6 package R):
// one row per route of the scene in pre-order (name, a select button, a play / stop button whose state colour is white
// stopped, green playing, red ended), then Play all (the stopped and ended ones), Restart all and Stop all (the schema's
// buttons of InspectorSchemas.SceneRoutes, which run the route.* actions). Rows are rebuilt when the set of routes or their
// names change and their states refreshed four times a second.
using System.Collections.Generic;
using System.Text;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public static class SceneRoutesSection
    {
        static readonly Color Stopped = new Color(1f, 1f, 1f, 1f), Playing = new Color(0.35f, 0.85f, 0.35f, 1f), Ended = new Color(0.9f, 0.3f, 0.3f, 1f);

        public static VisualElement Build(UiContext ctx)
        {
            VisualElement fo = null;
            foreach (var s in InspectorSchemas.SceneRoutes)
            {
                fo = InspectorUi.Section(s.LabelKey, false, "section-" + s.Id);
                var list = new VisualElement { name = "route-list" };
                fo.Add(list);
                var empty = InspectorUi.Note("inspector.scene.routes.none");
                empty.name = "route-list-empty";
                fo.Add(empty);
                InspectorUi.AddSection(ctx, s, TargetSelector.Scene, fo);
                string shape = null;
                void Refresh()
                {
                    var routes = ctx.Ops?.AllRoutes() ?? new List<TreeNode>();
                    var sb = new StringBuilder();
                    foreach (var n in routes) sb.Append(n.Id).Append(':').Append(n.Label).Append('|');
                    string sh = sb.ToString();
                    if (sh != shape)
                    {
                        shape = sh;
                        list.Clear();
                        foreach (var n in routes) list.Add(Row(ctx, n));
                        empty.style.display = routes.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
                    }
                    foreach (var row in list.Children()) (row.userData as System.Action)?.Invoke();
                }
                Refresh();
                fo.schedule.Execute(Refresh).Every(250);
            }
            return fo ?? new VisualElement();
        }

        static VisualElement Row(UiContext ctx, TreeNode n)
        {
            string id = n.Id;
            var name = new Label(n.Label) { name = "route-name" };
            name.AddToClassList("hs-row__label");
            name.style.flexGrow = 1;
            var select = InspectorUi.Button("inspector.scene.routes.select", () => ctx.Ops.Select(new[] { id }), "route-select");
            Button play = null;
            play = new Button(() =>
            {
                var st = ctx.Ops.RouteState(id);
                if (st == RoutePlayState.Playing) ctx.ShowResult(ctx.Ops.StopRoutes(new[] { id }, UiContext.User()));
                else ctx.ShowResult(ctx.Ops.PlayRoutes(new[] { id }, false, UiContext.User()));
            }) { name = "route-play" };
            play.AddToClassList("hs-field__button");
            var row = InspectorUi.Line(name, select, play);
            row.name = "route-row-" + id;
            row.userData = (System.Action)(() =>
            {
                var st = ctx.Ops.RouteState(id);
                play.text = st == RoutePlayState.Playing ? "■" : "▶";
                play.style.color = st == RoutePlayState.Playing ? Playing : st == RoutePlayState.Ended ? Ended : Stopped;
                play.tooltip = L10n.Get("route.state." + RoutePlayback.StateName(st));
                play.SetEnabled(ctx.Model.Find(id)?.State is RouteNodeState rs && rs.Points.Count >= 2);
            });
            return row;
        }
    }
}

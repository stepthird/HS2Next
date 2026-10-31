// The route inspector (Phase 3.6 package R; rows by package P in
// InspectorSchemas.Route): section Route — a state line (stopped / playing / ended, route time τ and total D, the point
// count "n / 11" while the point limit is on), Play / Stop / Add point (action buttons), Loop, Show line, Line colour (RGB),
// Orient; section Route point while a point or its aid of the primary route is sub-selected — Speed (0.1–30, one
// decimal), Ease (HS2's six entries, then "More…" with all 32; a stored value outside the six shows its own name),
// Connection, Link (a Curve point after a Curve point), Reset aid, the point's position (the start point: its rotation),
// the aid's position, Delete point. Rows follow their gates (InspectorGates route.*); writes go through InspectorWrites →
// StudioOps.Routes. Multi-selection of routes applies the Route rows to all of them (U25); point rows apply to the
// primary's sub-selected point (R25).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Routes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;
using UiField = HS2Next.UI.Core.FieldSpec;

namespace HS2Next.UI.Panels
{
    public static class RouteInspector
    {
        /// <summary>What the inspector rebuilds on (point count, sub-selected point, its connection / link, the limit option).</summary>
        public static string ShapeOf(UiContext ctx, string routeId)
        {
            var sb = new StringBuilder();
            var st = ctx.Model.Find(routeId)?.State as RouteNodeState;
            if (st == null) return "";
            sb.Append(st.Points.Count).Append(',').Append(RouteSettings.PointLimit);
            if (RouteUiModule.SubPoint(ctx, out var rid, out _, out int i) && rid == routeId)
            {
                var p = st.Points[i];
                sb.Append("|p").Append(i).Append(',').Append(p.Connection).Append(',').Append(p.Link);
                if (i > 0) sb.Append(',').Append(st.Points[i - 1].Connection);
            }
            foreach (int k in RouteGeometry.AbsorbedPoints(st)) sb.Append(",a").Append(k);
            return sb.ToString();
        }

        /// <summary>Adds the Route and Route point sections of the primary route <paramref name="n"/>.</summary>
        public static void Build(UiContext ctx, TreeNode n, TargetSelector kindSel, VisualElement into)
        {
            var sections = InspectorSchemas.ForKind("route");
            foreach (var s in sections)
            {
                if (s.Id == "route")
                {
                    var fo = InspectorUi.Section(s.LabelKey, true, "section-route");
                    fo.Add(StateLine(ctx, n.Id));
                    InspectorUi.AddSection(ctx, s, kindSel, fo);
                    into.Add(fo);
                }
                else if (s.Id == "routePoint")
                {
                    if (!RouteUiModule.SubPoint(ctx, out var rid, out var pid, out int index) || rid != n.Id) continue;
                    var fo = InspectorUi.Section(s.LabelKey, true, "section-routePoint");
                    var title = new Label(WorkspacePanel.RoutePointLabel(index)) { name = "route-point-label" };
                    title.AddToClassList("hs-row__label");
                    fo.Add(InspectorUi.Line(title));
                    string arg = index.ToString(CultureInfo.InvariantCulture);
                    var primary = TargetSelector.Primary(NodeKind.Route);
                    InspectorUi.AddSection(ctx, s, primary, fo, f =>
                    {
                        if (!f.IsTemplate) return null;
                        if (f.Path.EndsWith(".ease", StringComparison.Ordinal)) return new VisualElement[] { new RouteEaseRow(ctx, f, primary, arg) };
                        return new VisualElement[] { InspectorUi.Row(ctx, f, primary, null, arg) };
                    });
                    into.Add(fo);
                }
            }
        }

        /// <summary>The state line: stopped / playing / ended, τ and D, the point count against the limit.</summary>
        static VisualElement StateLine(UiContext ctx, string routeId)
        {
            var state = new Label { name = "route-state" };
            state.AddToClassList("hs-row__label");
            state.style.flexGrow = 1;
            var count = new Label { name = "route-point-count" };
            count.AddToClassList(InspectorUi.Dim);
            void Show()
            {
                var st = ctx.Model.Find(routeId)?.State as RouteNodeState;
                if (st == null) return;
                var real = ctx.Ops.RouteReal(routeId);
                var ps = real?.PlayState ?? RoutePlayState.Stopped;
                string word = L10n.Get("route.state." + RoutePlayback.StateName(ps));
                double total = real?.Path?.TotalDuration ?? 0;
                string t = (real?.Time ?? 0).ToString("0.00", CultureInfo.InvariantCulture) + " s";
                string d = double.IsInfinity(total) ? "∞" : total.ToString("0.00", CultureInfo.InvariantCulture) + " s";
                state.text = word + "  " + t + " / " + d;
                state.EnableInClassList("hs-route-state--playing", ps == RoutePlayState.Playing);
                state.EnableInClassList("hs-route-state--ended", ps == RoutePlayState.Ended);
                count.text = RouteSettings.PointLimit ? L10n.Format("route.pointCount", st.Points.Count, RouteGeometry.PointLimit) : st.Points.Count.ToString(CultureInfo.InvariantCulture);
            }
            Show();
            state.schedule.Execute(Show).Every(100);
            return InspectorUi.Line(state, count);
        }
    }

    /// <summary>
    /// The ease row of a route point (R10): a drop-down with HS2's six entries and "More…" (a menu of all 32 names); a stored
    /// ease outside the six shows its own name as an extra entry.
    /// </summary>
    public sealed class RouteEaseRow : VisualElement
    {
        readonly UiContext ctx;
        readonly UiField spec;
        readonly TargetSelector selector;
        readonly string arg;
        readonly List<string> values = new List<string>();
        public readonly DropdownField Dropdown;
        public FieldBinding Binding { get; }
        public OpResult LastResult { get; private set; }
        string shown;

        public RouteEaseRow(UiContext ctx, UiField spec, TargetSelector selector, string arg)
        {
            this.ctx = ctx;
            this.spec = spec;
            this.selector = selector;
            this.arg = arg;
            string path = spec.Expand(arg);
            name = "field-" + path;
            AddToClassList("hs-field");
            Dropdown = new DropdownField(L10n.Get(spec.LabelKey), new List<string>(), 0) { name = "dropdown" };
            Loc.Bind(Dropdown, spec.LabelKey, (e, s) => ((DropdownField)e).label = s);
            Dropdown.style.flexGrow = 1;
            Dropdown.RegisterValueChangedCallback(e =>
            {
                int i = Dropdown.index;
                if (i < 0 || i >= values.Count) return;
                if (values[i] == null) { Dropdown.SetValueWithoutNotify(Label(shown)); More(); return; }
                if (values[i] != shown) Set(values[i]);
            });
            Add(Dropdown);
            Binding = ctx.Bindings.Bind(this, selector, path, (el, v) => Show(v), spec);
        }

        static string Label(string ease) => ease == null ? "" : L10n.TryGet(InspectorSchemas.RouteEaseLabelKey(ease), out var t) ? t : ease;

        void Show(BoundValues v)
        {
            shown = v.Value as string ?? "linear";
            values.Clear();
            var labels = new List<string>();
            for (int i = 0; i < InspectorSchemas.RouteEaseUiCount; i++)
            {
                values.Add(InspectorSchemas.RouteEaseUi[i]);
                labels.Add(Label(InspectorSchemas.RouteEaseUi[i]));
            }
            if (!values.Contains(shown)) { values.Add(shown); labels.Add(Label(shown)); }
            values.Add(null);
            labels.Add(L10n.Get("inspector.route.point.easeMore"));
            Dropdown.choices = labels;
            Dropdown.SetValueWithoutNotify(v.Mixed ? "—" : Label(shown));
            string primary = v.Targets.Count > 0 ? v.Targets[0] : null;
            bool gated = spec.Gate == null || (primary != null && InspectorGates.Holds(ctx.Ops, spec.Gate, primary));
            style.display = primary != null && gated ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>"More…": every ease name in the save format's order.</summary>
        void More()
        {
            var items = new List<MenuItem>();
            foreach (var e in InspectorSchemas.RouteEaseNames)
            {
                string ease = e;
                items.Add(new MenuItem { Id = "ease-" + ease, Text = Label(ease), Checked = ease == shown, Run = () => Set(ease) });
            }
            CInput.ShowMenu(ctx, items, Dropdown.worldBound.center, "route-ease-menu");
        }

        public OpResult Set(string ease)
        {
            LastResult = InspectorWrites.Write(ctx, spec, arg, selector.Resolve(ctx), ease);
            Binding.MarkDirty();
            return LastResult;
        }
    }
}

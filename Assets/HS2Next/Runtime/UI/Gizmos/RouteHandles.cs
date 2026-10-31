// Route gizmos, line and markers (Phase 3.6 package R),
// a part of the transform gizmo (TransformTool, the view camera's gizmo pass: never in captures or thumbnails, R27):
//   Line      one strip per route with two or more points (RouteRealization.LineVertices: HS2's drawn line), on top (no
//             depth test, as HS2's depth-only sub-camera), width = option routes.lineWidth × 16 logical px, colour = the
//             route colour's RGB; shown when `showLine`, the route's effective visibility and the view toggle view.routes
//             are on (also while gizmos are hidden: HS2's sub-camera keeps the route layer); drawn while playing.
//   Markers   a dot at each point (HS2's markers keep their own blue whatever the route colour) with its label "S" / "1" …
//             (UI labels over the viewport: never in captures), and a small dot at each Curve point's aid while the route
//             is selected; the sub-selected one in the hover colour. Pickable within 8 logical px (the sub-selection).
//   Handles   a sub-selected start point gets a rotate gizmo, another point or an aid a move gizmo (Auto / World = world
//             axes as HS2, Local = the route's axes; the start point's own rotation for Local rotate); a drag is one
//             transaction ("Route point": StudioOps.SetRoutePointPose / SetRouteAid inside one UiEdit).
using System;
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Routes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Gizmos
{
    public sealed partial class TransformTool
    {
        /// <summary>HS2's point marker colour (the prefab's blue; never the route colour).</summary>
        public static readonly Color RouteMarker = new Color32(70, 120, 255, 255);
        public static readonly Color RouteAidMarker = new Color32(200, 200, 200, 255);
        /// <summary>Logical px of the line per unit of routes.lineWidth (HS2: × 16 screen px).</summary>
        public const float RouteLinePx = 16f;

        readonly Dictionary<string, Mesh> routeMeshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);
        readonly List<Label> routeLabels = new List<Label>();
        /// <summary>Routes whose line was drawn in the last frame (tests).</summary>
        public int RouteLinesDrawn { get; private set; }
        /// <summary>Line vertices drawn in the last frame (tests).</summary>
        public int RouteLineVertices { get; private set; }

        // ================================================================ target

        /// <summary>The route point / aid target of a sub-selection address; false (handled) when it does not resolve.</summary>
        bool RouteTarget(string sub, ref Target t, out bool ok)
        {
            ok = false;
            if (!StudioOps.TryParseRouteSub(sub, out var rid, out var pid, out bool aid)) return false;
            var real = ctx.Ops?.RouteReal(rid);
            var st = real?.Route;
            int i = StudioOps.PointIndex(st, pid);
            if (real == null || !real.Holder || i < 0 || real.Path == null) return true;
            var h = real.Holder;
            Vector3 local = aid && i < real.Path.Aids.Length ? V(real.Path.Aids[i]) : UnityObjects.V(st.Points[i].Pos);
            Quaternion rot = i == 0 ? h.rotation * Quaternion.Euler(UnityObjects.V(st.Points[0].Rot)) : h.rotation;
            t = new Target { NodeId = rid, Sub = sub, SubKind = "route", SubName = aid ? pid + "/aid" : pid, Holder = h, Pos = h.TransformPoint(local), Rot = rot };
            ok = true;
            return true;
        }

        static Vector3 V(Vec3d v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);

        /// <summary>The start point rotates (HS2: rotate only), other points and aids move (HS2: move only).</summary>
        GizmoTool RouteTool(in Target t, GizmoTool tool)
        {
            if (tool == GizmoTool.Select) return GizmoTool.Select;
            bool start = !t.SubName.EndsWith("/aid", StringComparison.Ordinal) && StudioOps.PointIndex(ctx.Ops.RouteReal(t.NodeId)?.Route, t.SubName) == 0;
            return start ? GizmoTool.Rotate : GizmoTool.Move;
        }

        /// <summary>Auto / World: world axes (HS2); Local: the point's frame; Parent: the route's.</summary>
        Quaternion RouteAxes(in Target t, GizmoSpace sp)
        {
            switch (sp)
            {
                case GizmoSpace.Local: return t.Rot;
                case GizmoSpace.Parent: return t.Holder ? t.Holder.rotation : Quaternion.identity;
                default: return Quaternion.identity;
            }
        }

        /// <summary>A drag step on a route point or aid (world pose → route-local / point-local, the ops).</summary>
        OpResult RouteApply(in Target t, Vector3? worldPos, Quaternion? worldRot)
        {
            var real = ctx.Ops.RouteReal(t.NodeId);
            var st = real?.Route;
            if (real == null || !real.Holder) return OpResult.NothingToDo();
            bool aid = t.SubName.EndsWith("/aid", StringComparison.Ordinal);
            string pid = aid ? t.SubName.Substring(0, t.SubName.Length - 4) : t.SubName;
            int i = StudioOps.PointIndex(st, pid);
            if (i < 0) return OpResult.NothingToDo();
            var h = real.Holder;
            var o = UiContext.User(OpLabels.RoutePoint);
            if (aid && worldPos.HasValue)
            {
                var routeLocal = h.InverseTransformPoint(worldPos.Value);
                var p = UnityObjects.V(st.Points[i].Pos);
                var pointRot = i == 0 ? Quaternion.Euler(UnityObjects.V(st.Points[0].Rot)) : Quaternion.identity;
                return ctx.Ops.SetRouteAid(t.NodeId, pid, Quaternion.Inverse(pointRot) * (routeLocal - p), o);
            }
            if (i == 0 && worldRot.HasValue)
            {
                var local = Quaternion.Inverse(h.rotation) * worldRot.Value;
                return ctx.Ops.SetRoutePointPose(t.NodeId, pid, null, local.eulerAngles, o);
            }
            if (i > 0 && worldPos.HasValue) return ctx.Ops.SetRoutePointPose(t.NodeId, pid, h.InverseTransformPoint(worldPos.Value), null, o);
            return OpResult.NothingToDo();
        }

        // ================================================================ markers and picking

        bool RoutesShown => ctx.Runtime != null && ctx.Options.Bool(OptionDefs.ViewRoutes);

        /// <summary>The visible routes (showLine, effective visibility) with their realizations.</summary>
        IEnumerable<(TreeNode node, RouteRealization real)> VisibleRoutes()
        {
            if (!RoutesShown) yield break;
            var pb = ctx.Runtime.GetPart<RoutePlayback>();
            if (pb == null) yield break;
            foreach (var r in pb.Routes)
            {
                if (r.State != RealizationState.Built || !r.Holder || r.InGraveyard || !ctx.Runtime.IsInModel(r)) continue;
                var st = r.Route;
                if (st == null || !st.ShowLine || !r.Node.EffectiveVisible) continue;
                yield return (r.Node, r);
            }
        }

        /// <summary>The pickable route markers: every point of the visible routes, the aids of Curve points of selected routes.</summary>
        IEnumerable<(string addr, Vector3 pos, bool aid, int index)> RouteMarkers()
        {
            foreach (var (n, r) in VisibleRoutes())
            {
                var st = r.Route;
                var h = r.Holder;
                bool selected = ctx.Selection.Contains(n.Id);
                for (int i = 0; i < st.Points.Count; i++)
                {
                    var p = st.Points[i];
                    if (p?.Id == null) continue;
                    yield return (n.Id + "/points/" + p.Id, h.TransformPoint(UnityObjects.V(p.Pos)), false, i);
                    if (selected && p.ConnectionType == RouteConnection.Curve && r.Path != null && i < r.Path.Aids.Length)
                        yield return (n.Id + "/points/" + p.Id + "/aid", h.TransformPoint(V(r.Path.Aids[i])), true, i);
                }
            }
        }

        /// <summary>A route marker within 8 logical px of <paramref name="p"/> (the nearest), as a Sub handle.</summary>
        bool RouteHit(Vector2 p, out GizmoHandle h)
        {
            h = new GizmoHandle { Kind = HandleKind.None, Axis = -1 };
            if (!Cam) return false;
            float best = SubPx;
            string hit = null;
            foreach (var (addr, pos, _, _) in RouteMarkers())
            {
                var sp = ToPanel(pos);
                if (sp.z <= 0) continue;
                float d = ((Vector2)sp - p).magnitude;
                if (d <= best) { best = d; hit = addr; }
            }
            if (hit == null) return false;
            h = new GizmoHandle { Kind = HandleKind.Sub, Axis = -1, Sub = hit };
            return true;
        }

        // ================================================================ drawing

        /// <summary>Lines and markers of the visible routes (before the handles: they draw on top of the lines).</summary>
        void RouteDraws(GizmoPass pass, Camera cam)
        {
            RouteLinesDrawn = 0;
            RouteLineVertices = 0;
            int labels = 0;
            var used = new HashSet<string>(StringComparer.Ordinal);
            if (cam && RoutesShown)
            {
                string sub = ctx.SubSelection;
                var camPos = cam.transform.position;
                float width = Mathf.Clamp(RouteSettings.LineWidth, 0.1f, 4f) * RouteLinePx;
                foreach (var (n, r) in VisibleRoutes())
                {
                    var st = r.Route;
                    var h = r.Holder;
                    var verts = r.LineVertices;
                    if (verts.Count >= 2)
                    {
                        if (!routeMeshes.TryGetValue(n.Id, out var mesh) || !mesh)
                        {
                            mesh = new Mesh { name = "route line " + n.Id, hideFlags = HideFlags.DontSave };
                            mesh.MarkDynamic();
                            routeMeshes[n.Id] = mesh;
                        }
                        Strip(mesh, verts, h, camPos, width);
                        used.Add(n.Id);
                        var c = new Color(st.Color.R, st.Color.G, st.Color.B, 1f);
                        pass.Add(new GizmoDraw { Mesh = mesh, Matrix = Matrix4x4.identity, Color = c });
                        RouteLinesDrawn++;
                        RouteLineVertices += verts.Count;
                    }
                }
                foreach (var (addr, pos, aid, index) in RouteMarkers())
                {
                    float wpp = WorldPerPx(pos);
                    bool on = addr == sub;
                    var col = on ? HoverColor : aid ? RouteAidMarker : RouteMarker;
                    pass.Add(new GizmoDraw { Mesh = GizmoMeshes.Disc, Matrix = Matrix4x4.TRS(pos, cam.transform.rotation, Vector3.one * (aid ? 4f : 6f) * wpp), Color = col });
                    if (!aid) PlaceLabel(labels++, pos, index == 0 ? "S" : index.ToString(System.Globalization.CultureInfo.InvariantCulture), on);
                }
            }
            for (int i = labels; i < routeLabels.Count; i++) if (routeLabels[i] != null) routeLabels[i].style.display = DisplayStyle.None;
            if (routeMeshes.Count > used.Count)
            {
                var drop = new List<string>();
                foreach (var kv in routeMeshes) if (!used.Contains(kv.Key)) drop.Add(kv.Key);
                foreach (var k in drop) { if (routeMeshes[k]) UnityEngine.Object.DestroyImmediate(routeMeshes[k]); routeMeshes.Remove(k); }
            }
        }

        readonly List<Vector3> stripVerts = new List<Vector3>();
        readonly List<int> stripTris = new List<int>();

        /// <summary>A camera-facing strip of <paramref name="widthPx"/> logical px through the route's world line.</summary>
        void Strip(Mesh mesh, IReadOnlyList<Vec3d> local, Transform h, Vector3 camPos, float widthPx)
        {
            int n = local.Count;
            stripVerts.Clear();
            stripTris.Clear();
            var w = new Vector3[n];
            for (int i = 0; i < n; i++) w[i] = h.TransformPoint(V(local[i]));
            for (int i = 0; i < n; i++)
            {
                var a = w[Math.Max(0, i - 1)];
                var b = w[Math.Min(n - 1, i + 1)];
                var tan = b - a;
                if (tan.sqrMagnitude < 1e-12f) tan = i + 1 < n ? w[i + 1] - w[i] : w[i] - w[Math.Max(0, i - 1)];
                var view = w[i] - camPos;
                var side = Vector3.Cross(tan, view);
                if (side.sqrMagnitude < 1e-12f) side = Vector3.Cross(tan, Vector3.up);
                side = side.normalized * (0.5f * widthPx * WorldPerPx(w[i]));
                stripVerts.Add(w[i] - side);
                stripVerts.Add(w[i] + side);
                if (i > 0)
                {
                    int k = stripVerts.Count - 4;
                    stripTris.Add(k); stripTris.Add(k + 1); stripTris.Add(k + 2);
                    stripTris.Add(k + 1); stripTris.Add(k + 3); stripTris.Add(k + 2);
                    // both faces (the gizmo shader may cull)
                    stripTris.Add(k); stripTris.Add(k + 2); stripTris.Add(k + 1);
                    stripTris.Add(k + 1); stripTris.Add(k + 2); stripTris.Add(k + 3);
                }
            }
            mesh.Clear();
            if (stripVerts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(stripVerts);
            mesh.SetTriangles(stripTris, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>The point label <paramref name="i"/> ("S", "1" …) over the viewport (a UI overlay: never in captures).</summary>
        void PlaceLabel(int i, Vector3 world, string text, bool on)
        {
            var vp = ctx.Viewport?.Element;
            if (vp == null) return;
            while (routeLabels.Count <= i) routeLabels.Add(null);
            var l = routeLabels[i];
            if (l == null || l.parent != vp)
            {
                l = new Label { name = "route-marker-" + i, pickingMode = PickingMode.Ignore };
                l.AddToClassList("hs-viewport__route-marker");
                var s = l.style;
                s.position = Position.Absolute;
                s.fontSize = 11;
                s.unityFontStyleAndWeight = FontStyle.Bold;
                s.paddingLeft = s.paddingRight = 2;
                s.backgroundColor = new Color(0f, 0f, 0f, 0.45f);
                vp.Add(l);
                routeLabels[i] = l;
            }
            var p = ToPanel(world);
            if (p.z <= 0) { l.style.display = DisplayStyle.None; return; }
            var origin = vp.worldBound.position;
            l.text = text;
            l.style.color = on ? HoverColor : Color.white;
            l.style.left = p.x - origin.x + 6;
            l.style.top = p.y - origin.y - 16;
            l.style.display = DisplayStyle.Flex;
        }
    }
}

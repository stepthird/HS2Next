// Route geometry rules shared by the evaluator and the route operations (
// 5): points and aids in route space (point positions are route-local; an aid is local to its point, and only the
// start point has a rotation), the midpoint rule of the aid (HS2 writes it when a point first becomes Curve), and the
// default position of a new point (R8). Unity-free.
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes.Routes
{
    public static class RouteGeometry
    {
        /// <summary>The rotation a point's frame has in route space: the start point's stored rotation; identity for the others
        /// (HS2's points other than the start have none; a stored one is ignored, route.pointRotIgnored).</summary>
        public static Quatd PointRotation(RouteNodeState route, int index) =>
            index == 0 && route.Points.Count > 0 ? Quatd.Euler(route.Points[0].Rot) : Quatd.Identity;

        /// <summary>Index of the point after <paramref name="index"/> (0 after the last).</summary>
        public static int NextIndex(RouteNodeState route, int index) => index + 1 >= route.Points.Count ? 0 : index + 1;

        /// <summary>
        /// The route-local position of point <paramref name="index"/>'s aid: pos + R · aid.pos when the point has an aid
        /// with <c>init</c> true; otherwise the midpoint between the point and the next one (what HS2 writes at the curve
        /// switch).
        /// </summary>
        public static Vec3d AidPosition(RouteNodeState route, int index)
        {
            var pts = new Vec3d[route.Points.Count];
            for (int i = 0; i < pts.Length; i++) pts[i] = Vec3d.From(route.Points[i].Pos);
            return AidPosition(route, index, pts, PointRotation(route, 0));
        }

        internal static Vec3d AidPosition(RouteNodeState route, int index, Vec3d[] points, Quatd startRot)
        {
            var p = route.Points[index];
            if (p.Aid != null && p.Aid.Init)
            {
                var rot = index == 0 ? startRot : Quatd.Identity;
                return points[index] + rot.Rotate(Vec3d.From(p.Aid.Pos));
            }
            return Vec3d.Lerp(points[index], points[NextIndex(route, index)], 0.5);
        }

        /// <summary>
        /// The point-local aid position of the midpoint rule (the value an op stores with <c>init</c> true when a point is
        /// switched to Curve, or by Reset aid): R⁻¹ · (midpoint − pos), R = the point's rotation (only the start point's is
        /// not identity).
        /// </summary>
        public static Vec3 MidpointAidLocal(RouteNodeState route, int index)
        {
            var p = Vec3d.From(route.Points[index].Pos);
            var next = Vec3d.From(route.Points[NextIndex(route, index)].Pos);
            var mid = Vec3d.Lerp(p, next, 0.5);
            return PointRotation(route, index).Inverse.Rotate(mid - p).ToVec3();
        }

        /// <summary>
        /// The default position of a point appended at the end (R8): last + (last − previous) with two or more points, else
        /// start + (0, 0, 10) in route space.
        /// </summary>
        public static Vec3 NextPointPosition(RouteNodeState route)
        {
            var pts = route.Points;
            if (pts.Count == 0) return Vec3.Zero;
            var last = Vec3d.From(pts[pts.Count - 1].Pos);
            if (pts.Count == 1) return (last + new Vec3d(0, 0, 10)).ToVec3();
            var prev = Vec3d.From(pts[pts.Count - 2].Pos);
            return (last + (last - prev)).ToVec3();
        }

        /// <summary>
        /// The default position of a point inserted at <paramref name="index"/> (1 ≤ index ≤ count): appended → <see
        /// cref="NextPointPosition"/>; inside the list → the midpoint between its neighbours (HS2 only appends).
        /// </summary>
        public static Vec3 InsertPointPosition(RouteNodeState route, int index)
        {
            var pts = route.Points;
            if (index >= pts.Count || index < 1) return NextPointPosition(route);
            return Vec3d.Lerp(Vec3d.From(pts[index - 1].Pos), Vec3d.From(pts[index].Pos), 0.5).ToVec3();
        }

        /// <summary>The point-limit rule (option routes.pointLimit, default on, R9): at most 11 points (start + 10).</summary>
        public const int PointLimit = 11;

        /// <summary>True when one more point may be added.</summary>
        public static bool CanAddPoint(RouteNodeState route, bool limitOn) => !limitOn || route.Points.Count < PointLimit;

        /// <summary>Whether a point can be linked (Link has an effect): a Curve point that follows a Curve point (HS2 merges it
        /// into the chain only then; the UI enables Link only there).</summary>
        public static bool LinkEffective(RouteNodeState route, int index)
        {
            if (index <= 0 || index >= route.Points.Count) return false;
            return route.Points[index].ConnectionType == RouteConnection.Curve && route.Points[index - 1].ConnectionType == RouteConnection.Curve;
        }

        /// <summary>Indices of the points whose speed / ease are unused (linked into a previous curve chain, as HS2 greys them).</summary>
        public static List<int> AbsorbedPoints(RouteNodeState route)
        {
            var path = RoutePath.Build(route);
            var l = new List<int>();
            foreach (var s in path.Segments) l.AddRange(s.Linked);
            return l;
        }
    }
}

// The route line's vertices (the curve rule as measured on HS2):
// one polyline per route with two or more points, in route space. A Line point adds one vertex (its own position); a
// curve chain (a Curve point and the linked Curve points after it, as the evaluator's segments merge them) adds
// 160 × (chain points) + 1 vertices along a centripetal Catmull-Rom through point, aid, point, aid, …, end point; with
// loop the line closes on the start point (a chain from the start point through the last point closes as a loop spline);
// without loop the last point is one vertex whatever its connection (its aid is not drawn).
// The curve is HS2's drawn spline, not the motion curve (J3): per span the knot intervals are |p(i+1) − p(i)|^0.5
// (an interval below 1e-4 takes the middle one, a middle one below 1e-4 is 1), an open chain repeats its end points
// instead of reflected ghosts, a closed chain wraps, and the parameter advances by spans / segments through the spans in
// float steps carried over span ends (the last vertex at t = 1). Unity-free.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Scenes.Routes;

namespace HS2Next.Scenes.Runtime
{
    public static class RouteLine
    {
        /// <summary>Vertices per chain point of a curve chain (HS2: 160).</summary>
        public const int CurveSegmentsPerPoint = 160;

        /// <summary>
        /// The line's route-local vertices for <paramref name="route"/> (empty with fewer than two points). Point and aid
        /// positions are the evaluator's (<paramref name="path"/>: <see cref="RoutePath.Points"/>, <see cref="RoutePath.Aids"/>).
        /// </summary>
        public static List<Vec3d> Vertices(RouteNodeState route, RoutePath path)
        {
            var outp = new List<Vec3d>();
            if (route?.Points == null || path == null) return outp;
            int n = route.Points.Count;
            if (n < 2 || path.Points.Length < n || path.Aids.Length < n) return outp;
            bool loop = route.Loop;
            var P = path.Points;
            var A = path.Aids;
            bool Curve(int x) => route.Points[x].ConnectionType == RouteConnection.Curve;
            bool Linked(int x) => route.Points[x].Link && Curve(x);
            int i = 0;
            while (i < n)
            {
                if (!Curve(i))
                {
                    outp.Add(P[i]);
                    i++;
                    if (i >= n && loop) outp.Add(P[0]);
                    continue;
                }
                if (!loop && i == n - 1)
                {
                    outp.Add(P[i]);
                    i++;
                    continue;
                }
                var chain = new List<Vec3d> { P[i], A[i] };
                int j = i + 1, k = 1;
                while (j < n && (loop || j != n - 1) && Linked(j))
                {
                    chain.Add(P[j]);
                    chain.Add(A[j]);
                    j++;
                    k++;
                }
                bool closed = i == 0 && j >= n && loop;
                if (j >= n) { if (!closed) chain.Add(P[0]); }
                else chain.Add(P[j]);
                Spline(chain, CurveSegmentsPerPoint * k, closed, outp);
                i = j;
            }
            return outp;
        }

        /// <summary>HS2's drawn spline through <paramref name="pts"/> with <paramref name="segments"/> + 1 vertices (see the file header).</summary>
        static void Spline(List<Vec3d> pts, int segments, bool loop, List<Vec3d> outp)
        {
            int num = pts.Count;
            int spans = loop ? num : num - 1;
            if (spans < 1) { outp.AddRange(pts); return; }
            float step = 1f / segments * spans;
            float carry = 0f;
            int start = outp.Count;
            int a = 0, b = 0, c = 0, last = 0;
            for (int i = 0; i < spans; i++)
            {
                a = i - 1; b = i + 1; c = i + 2;
                if (a < 0) a = loop ? spans - 1 : 0;
                if (loop && b > spans - 1) b -= spans;
                if (c > spans - 1) c = loop ? c - spans : spans;
                last = i;
                float t = carry;
                while (t <= 1f)
                {
                    outp.Add(Centripetal(pts[a], pts[i], pts[b], pts[c], t));
                    t = t + step;
                }
                carry = t - 1f;
            }
            if (outp.Count - start < segments + 1) outp.Add(Centripetal(pts[a], pts[last], pts[b], pts[c], 1f));
        }

        static Vec3d Centripetal(Vec3d p0, Vec3d p1, Vec3d p2, Vec3d p3, double t)
        {
            double d0 = Math.Pow((p1 - p0).SqrMagnitude, 0.25), d1 = Math.Pow((p2 - p1).SqrMagnitude, 0.25), d2 = Math.Pow((p3 - p2).SqrMagnitude, 0.25);
            if (d1 < 1e-4) d1 = 1.0;
            if (d0 < 1e-4) d0 = d1;
            if (d2 < 1e-4) d2 = d1;
            return new Vec3d(Axis(p0.X, p1.X, p2.X, p3.X, d0, d1, d2, t), Axis(p0.Y, p1.Y, p2.Y, p3.Y, d0, d1, d2, t), Axis(p0.Z, p1.Z, p2.Z, p3.Z, d0, d1, d2, t));
        }

        /// <summary>The non-uniform Catmull-Rom between x1 and x2 (Hermite form with tangents from the knot intervals).</summary>
        static double Axis(double x0, double x1, double x2, double x3, double d0, double d1, double d2, double t)
        {
            double m1 = ((x1 - x0) / d0 - (x2 - x0) / (d0 + d1) + (x2 - x1) / d1) * d1;
            double m2 = ((x2 - x1) / d1 - (x3 - x1) / (d1 + d2) + (x3 - x2) / d2) * d1;
            return x1 + m1 * t + (-3 * x1 + 3 * x2 - 2 * m1 - m2) * t * t + (2 * x1 - 2 * x2 + m1 + m2) * t * t * t;
        }

        /// <summary>The vertex count rule (a Line point 1, a chain 160 × points + 1, closing / last-point rules).</summary>
        public static int ExpectedCount(RouteNodeState route)
        {
            if (route?.Points == null || route.Points.Count < 2) return 0;
            int n = route.Points.Count, count = 0, i = 0;
            bool loop = route.Loop;
            bool Curve(int x) => route.Points[x].ConnectionType == RouteConnection.Curve;
            while (i < n)
            {
                if (!Curve(i)) { count++; i++; if (i >= n && loop) count++; continue; }
                if (!loop && i == n - 1) { count++; i++; continue; }
                int j = i + 1, k = 1;
                while (j < n && (loop || j != n - 1) && route.Points[j].Link && Curve(j)) { j++; k++; }
                count += CurveSegmentsPerPoint * k + 1;
                i = j;
            }
            return count;
        }
    }
}

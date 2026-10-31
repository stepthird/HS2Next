// Route path evaluator: closed forms (a straight segment of length d lasts
// 3d / (10·speed) with HS2's timing and d / (10·speed) with the length timing, positions linear in p), Catmull-Rom
// through every path point, ghost reflection and wrap-around, the 32 eases against the independent table of
// Tools/route_reference.py (Tests/Routes/route_reference.json) and clamping, segment building (Line / Curve / links /
// loop / no loop / 1, 2, 3, 11 points), the two-point ping-pong timeline, the loop period, zero speed, zero length,
// unknown ease, the aid in the start point's rotated frame, HS2's end carry after an expo ease, the reference cases,
// the geometry helpers of the ops, and the cost of Build / Evaluate (R-M5, reported).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;
    using HS2Next.Scenes.Routes;

    public static class SceneTests_Routes
    {
        public static void Run(TestRunner t)
        {
            ClosedForms(t);
            SplineBasics(t);
            Eases(t);
            Segments(t);
            PingPong(t);
            LoopPeriod(t);
            Degenerate(t);
            AidFrame(t);
            EndCarry(t);
            Reference(t);
            Geometry(t);
            Perf(t);
        }

        // ---------------- helpers ----------------

        internal static RoutePoint P(double x, double y, double z, float speed = 2f, string ease = "linear", string conn = "line",
            bool link = false, RouteAid aid = null, Vec3? rot = null, string id = null) => new RoutePoint
            {
                Id = id ?? Guid.NewGuid().ToString("N"),
                Pos = new Vec3((float)x, (float)y, (float)z),
                Rot = rot ?? Vec3.Zero,
                Speed = speed,
                Ease = ease,
                Connection = conn,
                Link = link,
                Aid = aid,
            };

        internal static RouteNodeState R(bool loop, params RoutePoint[] pts)
        {
            var r = new RouteNodeState { Loop = loop };
            r.Points.AddRange(pts);
            return r;
        }

        static bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol || (double.IsInfinity(a) && a == b);
        static bool Near(Vec3d a, Vec3d b, double tol) => Vec3d.Distance(a, b) <= tol;
        static bool Rel(double a, double b, double rel) => Math.Abs(a - b) <= rel * Math.Max(Math.Abs(a), Math.Abs(b)) || a == b;
        static string F(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

        // ---------------- groups ----------------

        static void ClosedForms(TestRunner t)
        {
            foreach (double d in new[] { 1.0, 10.0, 37.5 })
                foreach (float sp in new[] { 0.1f, 2f, 30f })
                {
                    var r = R(false, P(0, 0, 0, sp), P(d, 0, 0));
                    var hs2 = RoutePath.Build(r);
                    var len = RoutePath.Build(r, new RouteOptions { Timing = RouteTiming.Length });
                    double expectHs2 = 3 * d / (10.0 * sp), expectLen = d / (10.0 * sp);
                    t.Check(Rel(hs2.Segments[0].Duration, expectHs2, 1e-9), $"Hs2: straight {d} at speed {sp} lasts 3d/(10s) = {F(expectHs2)} (got {F(hs2.Segments[0].Duration)})");
                    t.Check(Rel(len.Segments[0].Duration, expectLen, 1e-9), $"Length: straight {d} at speed {sp} lasts d/(10s) = {F(expectLen)} (got {F(len.Segments[0].Duration)})");
                    t.Check(Rel(hs2.TotalDuration, expectHs2, 1e-9) && hs2.Mode == RoutePlayMode.Once, "no loop: the total is the one segment");
                    bool linear = true;
                    foreach (double f in new[] { 0.0, 0.1, 0.25, 0.5, 0.77, 0.999 })
                    {
                        var s = hs2.Evaluate(f * expectHs2);
                        linear &= Near(s.Position, new Vec3d(f * d, 0, 0), 1e-9 * Math.Max(1, d)) && Near(s.Percentage, f, 1e-12);
                    }
                    t.Check(linear, $"straight {d} at speed {sp}: position linear in p");
                    var end = hs2.Evaluate(expectHs2 * 1.5);
                    t.Check(end.Finished && Near(end.Position, new Vec3d(d, 0, 0), 1e-9) && end.Percentage == 1, "after the end: finished at the end point");
                }
            // Speed in units / s: 10·speed / 3 with HS2's rule.
            var r2 = RoutePath.Build(R(false, P(0, 0, 0, 3f), P(0, 0, 30)));
            var a = r2.Evaluate(0.5);
            t.Check(Near(a.Position.Z, 0.5 * 10 * 3 / 3.0, 1e-9), "Hs2: moves at 10·speed/3 units per second (" + F(a.Position.Z) + ")");
        }

        static void SplineBasics(TestRunner t)
        {
            var path = new[] { new Vec3d(0, 0, 0), new Vec3d(3, 1, 0), new Vec3d(5, -2, 4), new Vec3d(9, 0, 1), new Vec3d(12, 3, 3) };
            var pad = RouteSpline.Pad(path);
            t.Check(pad.Length == path.Length + 2, "one padding adds two ghosts");
            t.Check(Near(pad[0], 2 * path[0] - path[1], 0) && Near(pad[pad.Length - 1], 2 * path[4] - path[3], 0), "ghosts reflect the end spans");
            int m = path.Length - 1;
            bool through = true;
            for (int k = 0; k <= m; k++) through &= Near(RouteSpline.Interp(pad, (double)k / m), path[k], 1e-12);
            t.Check(through, "Catmull-Rom passes through every path point at the span boundaries");
            var closed = new[] { new Vec3d(0, 0, 0), new Vec3d(4, 0, 2), new Vec3d(8, 0, 0), new Vec3d(4, 0, -3), new Vec3d(0, 0, 0) };
            var cp = RouteSpline.Pad(closed);
            t.Check(Near(cp[0], closed[3], 0) && Near(cp[cp.Length - 1], closed[1], 0), "first = last: the ghosts wrap around (g0 = p[n-2], gn = p[1])");
            // C¹ across the joint of a closed chain: the tangent at the start equals the tangent at the end.
            double h = 1e-7;
            var t0 = (RouteSpline.Interp(cp, h) - RouteSpline.Interp(cp, 0)) * (1 / h);
            var t1 = (RouteSpline.Interp(cp, 1) - RouteSpline.Interp(cp, 1 - h)) * (1 / h);
            t.Check(Near(t0, t1, 1e-3 * Math.Max(1, t0.Magnitude)), "closed chain: tangent continuous across the joint (" + t0 + " vs " + t1 + ")");
            var nearly = new[] { new Vec3d(0, 0, 0), new Vec3d(4, 0, 2), new Vec3d(0, 0, 9e-6) };
            t.Check(Near(RouteSpline.Pad(nearly)[0], nearly[1], 0), "wrap-around within Unity's equality (|Δ|² < 1e-10)");
            var apart = new[] { new Vec3d(0, 0, 0), new Vec3d(4, 0, 2), new Vec3d(0, 0, 1e-4) };
            t.Check(Near(RouteSpline.Pad(apart)[0], 2 * apart[0] - apart[1], 0), "no wrap-around when the ends are 1e-4 apart");
            // Hs2 length of a two-point path: 3d; arc length: d.
            var two = RouteSpline.Pad(new[] { new Vec3d(1, 2, 3), new Vec3d(1, 2, 13) });
            t.Check(Rel(RouteSpline.Hs2Length(two), 30, 1e-12) && Rel(RouteSpline.ArcLength(two), 10, 1e-12), "two points: Hs2 length 3d, arc length d");
            // A quarter-circle-like curve: the arc length is below the Hs2 length and above the chord sum.
            var c = RouteSpline.Pad(new[] { new Vec3d(0, 0, 0), new Vec3d(7.07, 0, 2.93), new Vec3d(10, 0, 10) });
            double arc = RouteSpline.ArcLength(c), hs2 = RouteSpline.Hs2Length(c);
            double chords = Vec3d.Distance(new Vec3d(0, 0, 0), new Vec3d(7.07, 0, 2.93)) + Vec3d.Distance(new Vec3d(7.07, 0, 2.93), new Vec3d(10, 0, 10));
            t.Check(arc > chords && arc < hs2, $"curve: chords {F(chords)} < arc {F(arc)} < Hs2 {F(hs2)}");
            // Arc length against a fine chord sum.
            double fine = 0; var prev = RouteSpline.Interp(c, 0);
            for (int i = 1; i <= 200000; i++) { var q = RouteSpline.Interp(c, i / 200000.0); fine += Vec3d.Distance(prev, q); prev = q; }
            t.Check(Rel(arc, fine, 1e-8), $"arc length by quadrature equals a 200k-chord sum ({F(arc)} vs {F(fine)})");
        }

        static void Eases(TestRunner t)
        {
            t.Equal(32, RouteEases.Names.Count, "32 ease names");
            t.Equal("linear", RouteEases.Names[21], "linear is index 21");
            t.Equal(6, RouteEases.UiSix.Count, "six UI eases");
            var refDoc = LoadReference(t);
            if (refDoc == null) return;
            var eases = (JObject)refDoc["eases"];
            t.Equal(32, eases.Count, "the reference lists 32 eases");
            int bad = 0;
            foreach (var kv in eases)
            {
                t.Check(RouteEases.TryParse(kv.Key, out var e), "reference ease known: " + kv.Key);
                var probes = (JArray)kv.Value["probes"];
                var raw = (JArray)kv.Value["raw"];
                var cl = (JArray)kv.Value["clamped"];
                for (int i = 0; i < probes.Count; i++)
                {
                    double p = (double)probes[i], want = (double)raw[i], got = RouteEases.Evaluate(e, p);
                    double wantC = (double)cl[i], gotC = RouteEases.Clamped(e, p);
                    if (!Near(got, want, 1e-12) || !Near(gotC, wantC, 1e-12))
                    {
                        bad++;
                        t.Check(false, $"{kv.Key}({F(p)}) = {F(got)} / clamped {F(gotC)}, reference {F(want)} / {F(wantC)}");
                    }
                }
            }
            t.Check(bad == 0, "32 eases at 8 probes equal the reference (1e-12)");
            // The six of the UI at the spec's closed forms.
            t.Check(Near(RouteEases.Evaluate(RouteEase.easeInQuad, 0.3), 0.09, 1e-15) && Near(RouteEases.Evaluate(RouteEase.easeOutQuad, 0.3), 0.51, 1e-15),
                "quad in t², out −t(t − 2)");
            t.Check(Near(RouteEases.Evaluate(RouteEase.easeInQuart, 0.5), 0.0625, 1e-15) && Near(RouteEases.Evaluate(RouteEase.easeOutQuart, 0.5), 0.9375, 1e-15),
                "quart in t⁴, out −((t − 1)⁴ − 1)");
            t.Check(Near(RouteEases.Evaluate(RouteEase.easeOutBounce, 0.2), 7.5625 * 0.04, 1e-15) && Near(RouteEases.Evaluate(RouteEase.easeOutBounce, 1), 1, 1e-12),
                "bounce: 7.5625 t² on the first piece, 1 at the end");
            // Clamping before the spline: overshooting eases never leave the segment.
            bool clamped = true;
            foreach (var name in new[] { "easeInBack", "easeOutBack", "easeInOutBack", "easeInElastic", "easeOutElastic", "easeInOutElastic", "spring" })
            {
                var path = RoutePath.Build(R(false, P(0, 0, 0, 2f, name), P(10, 0, 0)));
                double d = path.Segments[0].Duration;
                for (int i = 0; i <= 200; i++)
                {
                    var s = path.Evaluate(d * i / 200.0);
                    clamped &= s.Eased >= 0 && s.Eased <= 1 && s.Position.X >= -1e-12 && s.Position.X <= 10 + 1e-12;
                }
            }
            t.Check(clamped, "back / elastic / spring overshoot clipped to the segment (s in [0, 1])");
            t.Check(RouteEases.Evaluate(RouteEase.easeOutBack, 0.6) > 1 && RouteEases.Clamped(RouteEase.easeOutBack, 0.6) == 1, "the unclamped ease overshoots, the clamped one does not");
        }

        static void Segments(TestRunner t)
        {
            // 1 point: nothing.
            var one = RoutePath.Build(R(true, P(1, 2, 3)));
            t.Check(one.Mode == RoutePlayMode.None && !one.CanPlay && one.Segments.Count == 0, "one point: no path");
            var s1 = one.Evaluate(5);
            t.Check(!s1.Valid && Near(s1.Position, new Vec3d(1, 2, 3), 0), "one point: the sample is the start point");
            // 2 points.
            var two = RoutePath.Build(R(false, P(0, 0, 0), P(10, 0, 0)));
            t.Check(two.Mode == RoutePlayMode.Once && two.Segments.Count == 1 && two.Segments[0].From == 0 && two.Segments[0].To == 1, "2 points no loop: one segment 0→1");
            var twoL = RoutePath.Build(R(true, P(0, 0, 0), P(10, 0, 0)));
            t.Check(twoL.Mode == RoutePlayMode.PingPong && twoL.Segments.Count == 2 && twoL.Segments[1].From == 1 && twoL.Segments[1].To == 0, "2 points loop: ping-pong queue 0→1, 1→0");
            // 3 points.
            var three = RoutePath.Build(R(false, P(0, 0, 0), P(10, 0, 0), P(10, 0, 10)));
            t.Check(three.Segments.Count == 2 && three.Mode == RoutePlayMode.Once, "3 points no loop: two segments, the last point starts none");
            var threeL = RoutePath.Build(R(true, P(0, 0, 0), P(10, 0, 0), P(10, 0, 10)));
            t.Check(threeL.Segments.Count == 3 && threeL.Mode == RoutePlayMode.Loop && threeL.Segments[2].From == 2 && threeL.Segments[2].To == 0,
                "3 points loop: the closing segment 2→0");
            // 11 points.
            var pts = new List<RoutePoint>();
            for (int i = 0; i < 11; i++) pts.Add(P(i * 3, 0, (i % 2) * 4));
            var eleven = RoutePath.Build(R(false, pts.ToArray()));
            var elevenL = RoutePath.Build(R(true, pts.ToArray()));
            t.Check(eleven.Segments.Count == 10 && elevenL.Segments.Count == 11, "11 points: 10 segments, 11 with loop");
            // Curve: the path is P_i, A_i, P_next.
            var curve = RoutePath.Build(R(false, P(0, 0, 0, 2f, "linear", "curve", aid: new RouteAid { Pos = new Vec3(5, 0, 5), Init = true }), P(10, 0, 0)));
            var cs = curve.Segments[0];
            t.Check(cs.Connection == RouteConnection.Curve && cs.Path.Length == 3 && Near(cs.Path[1], new Vec3d(5, 0, 5), 1e-9), "curve: path P0, A0, P1");
            t.Check(Near(curve.Evaluate(cs.Duration / 2).Position, new Vec3d(5, 0, 5), 1e-9), "curve: the aid is a pass-through point (at half the time)");
            // A curve point's speed and ease are the segment's; a linked point's are ignored.
            var chain = RoutePath.Build(R(false,
                P(0, 0, 0, 2f, "easeInQuad", "curve"), P(10, 0, 0, 7f, "easeOutBounce", "curve", true), P(10, 0, 10, 9f, "linear", "curve", true), P(0, 0, 10, 1f)));
            t.Check(chain.Segments.Count == 1, "no loop: one segment (the chain 0 → 3; the last point starts none)");
            var c0 = chain.Segments[0];
            t.Check(c0.From == 0 && c0.To == 3 && c0.Linked.Length == 2 && c0.Linked[0] == 1 && c0.Linked[1] == 2, "links merge points 1 and 2 into segment 0 (0 → 3)");
            t.Check(c0.Speed == 2f && c0.Ease == "easeInQuad" && c0.Path.Length == 7, "the chain uses point 0's speed and ease; path P0 A0 P1 A1 P2 A2 P3");
            var chainEnd = RoutePath.Build(R(false, P(0, 0, 0, 2f, "linear", "curve"), P(10, 0, 0, 2f, "linear", "curve", true), P(10, 0, 10, 2f, "linear", "curve", true)));
            t.Check(chainEnd.Segments.Count == 1 && chainEnd.Segments[0].To == 2 && chainEnd.Segments[0].Linked.Length == 1, "no loop: a linked last point is not absorbed (it ends the chain)");
            var chainLoop = RoutePath.Build(R(true, P(0, 0, 0, 2f, "linear", "curve"), P(10, 0, 0, 2f, "linear", "curve", true), P(10, 0, 10, 2f, "linear", "curve", true)));
            t.Check(chainLoop.Segments.Count == 1 && chainLoop.Segments[0].To == 0 && chainLoop.Segments[0].Linked.Length == 2 && chainLoop.Segments[0].Path.Length == 7,
                "loop: the chain absorbs the last point and closes on P0 (one segment)");
            // Link on a Line point, or after a Line point, starts its own segment.
            var linkLine = RoutePath.Build(R(false, P(0, 0, 0), P(10, 0, 0, 2f, "linear", "curve", true), P(10, 0, 10)));
            t.Check(linkLine.Segments.Count == 2 && linkLine.Segments[1].From == 1 && linkLine.Segments[1].Connection == RouteConnection.Curve,
                "a linked curve point after a Line point starts its own segment");
            var linkNoCurve = RoutePath.Build(R(false, P(0, 0, 0, 2f, "linear", "curve"), P(10, 0, 0, 2f, "linear", "line", true), P(10, 0, 10)));
            t.Check(linkNoCurve.Segments.Count == 2 && linkNoCurve.Segments[0].To == 1, "link on a Line point has no effect (isLink needs Curve)");
            // Closing segment with loop uses the last point's connection, speed and ease.
            var closing = RoutePath.Build(R(true, P(0, 0, 0), P(10, 0, 0), P(10, 0, 10, 5f, "easeOutQuad", "curve")));
            var cl = closing.Segments[2];
            t.Check(cl.From == 2 && cl.To == 0 && cl.Connection == RouteConnection.Curve && cl.Speed == 5f && cl.Ease == "easeOutQuad" && cl.Path.Length == 3,
                "the closing segment takes the last point's connection, speed and ease");
            // Ids.
            t.Check(closing.Segments[2].FromId == closing.Segments[2].FromId && closing.Segments[0].ToId != null, "segment ids filled");
        }

        static void PingPong(TestRunner t)
        {
            var path = RoutePath.Build(R(true, P(0, 0, 0, 2f, "linear"), P(10, 0, 0, 5f, "easeOutQuad")));
            double d0 = 30 / 20.0, d1 = 30 / 50.0;
            t.Check(Rel(path.Segments[0].Duration, d0, 1e-9) && Rel(path.Segments[1].Duration, d1, 1e-9), "ping-pong: legs 1.5 s (speed 2) and 0.6 s (speed 5)");
            t.Check(Rel(path.FirstPassDuration, d0 + d1, 1e-12) && Rel(path.RepeatDuration, d1, 1e-12) && double.IsPositiveInfinity(path.TotalDuration), "first pass, then legs of point 1");
            t.Check(path.RepeatSegments.Count == 1 && path.RepeatSegments[0].Index == 1, "only the last queued segment repeats");
            // First pass: 0→1 linear, then 1→0 with easeOutQuad.
            var a = path.Evaluate(0.75);
            t.Check(a.Pass == 0 && a.Segment == 0 && Near(a.Position.X, 5, 1e-9) && !a.Reversed, "first pass, segment 0 at half time: x = 5");
            var b = path.Evaluate(d0 + d1 * 0.5);
            double eb = RouteEases.Evaluate(RouteEase.easeOutQuad, 0.5);
            t.Check(b.Pass == 0 && b.Segment == 1 && Near(b.Position.X, 10 - 10 * eb, 1e-9), "first pass, segment 1 (1→0) eased by point 1");
            // Leg 1: reversed (0→1 physically), point 1's speed and ease on p = 1 − u.
            var c = path.Evaluate(d0 + d1 + d1 * 0.25);
            double ec = RouteEases.Evaluate(RouteEase.easeOutQuad, 0.75);
            t.Check(c.Pass == 1 && c.Reversed && Near(c.Percentage, 0.75, 1e-12) && Near(c.Position.X, 10 - 10 * ec, 1e-9),
                "leg 1 reversed: p = 1 − u, ease(p) of point 1 (" + F(c.Position.X) + ")");
            var c2 = path.Evaluate(d0 + d1 + d1 * 0.999999);
            t.Check(Near(c2.Position.X, 10, 1e-3), "leg 1 ends at P1");
            // Leg 2: forward again (1→0).
            var e = path.Evaluate(d0 + 2 * d1 + d1 * 0.25);
            t.Check(e.Pass == 2 && !e.Reversed && Near(e.Position.X, 10 - 10 * RouteEases.Evaluate(RouteEase.easeOutQuad, 0.25), 1e-9), "leg 2 forward (1→0)");
            // Far ahead: still alternating with period 2·d1.
            var f1 = path.Evaluate(d0 + d1 + 1000 * 2 * d1 + 0.1);
            var f2 = path.Evaluate(d0 + d1 + 0.1);
            t.Check(Near(f1.Position, f2.Position, 1e-6) && f1.Reversed, "the legs repeat with period 2 × leg");
            // Two linked curve points: the single closed segment ping-pongs.
            var pc = RoutePath.Build(R(true, P(0, 0, 0, 2f, "linear", "curve", aid: new RouteAid { Pos = new Vec3(5, 0, 5), Init = true }),
                P(10, 0, 0, 2f, "linear", "curve", true, new RouteAid { Pos = new Vec3(-5, 0, -5), Init = true })));
            t.Check(pc.Mode == RoutePlayMode.PingPong && pc.Segments.Count == 1 && pc.Segments[0].To == 0, "two linked curve points: one closed segment");
            double dd = pc.Segments[0].Duration;
            var g = pc.Evaluate(dd + dd * 0.3);
            var h = pc.Evaluate(dd * 0.7);
            t.Check(g.Reversed && Near(g.Position, h.Position, 1e-9), "the closed segment plays reversed after the first pass");
        }

        static void LoopPeriod(TestRunner t)
        {
            var path = RoutePath.Build(R(true, P(0, 0, 0, 2f), P(10, 0, 0, 3f, "easeInQuad"), P(10, 0, 10, 4f, "easeOutBounce")));
            double D = path.FirstPassDuration;
            t.Check(Rel(D, 1.5 + 1.0 + 3 * Math.Sqrt(200) / 40, 1e-9), "loop period = Σ segment durations (" + F(D) + ")");
            t.Check(path.RepeatSegments.Count == 3 && ReferenceEquals(path.RepeatSegments[0], path.Segments[0]), "without carry the repeat pass is the first pass");
            bool periodic = true;
            foreach (double tau in new[] { 0.0, 0.4, 1.7, 2.9, 3.5 })
                periodic &= Near(path.Evaluate(tau).Position, path.Evaluate(tau + 7 * D).Position, 1e-6) && path.Evaluate(tau + 7 * D).Pass == 7;
            t.Check(periodic, "τ and τ + 7D give the same pose (pass 7)");
            var at = path.Evaluate(D);
            t.Check(at.Segment == 0 && Near(at.Position, new Vec3d(0, 0, 0), 1e-9) && at.Pass == 1, "τ = D: back at the start of segment 0");
            var once = RoutePath.Build(R(false, P(0, 0, 0, 2f), P(10, 0, 0, 3f, "easeInQuad"), P(10, 0, 10)));
            t.Check(once.Evaluate(once.TotalDuration + 1).Finished && Near(once.Evaluate(1e9).Position, new Vec3d(10, 0, 10), 1e-9), "no loop: finished at the last point");
            t.Check(!once.Evaluate(once.TotalDuration * 0.999).Finished, "not finished before the end");
        }

        static void Degenerate(TestRunner t)
        {
            // Zero speed holds at the segment's start.
            var z = RoutePath.Build(R(false, P(0, 0, 0), P(10, 0, 0, 0f), P(10, 0, 10)));
            t.Check(double.IsPositiveInfinity(z.Segments[1].Duration) && double.IsPositiveInfinity(z.TotalDuration), "speed 0: infinite segment");
            var zs = z.Evaluate(1e6);
            t.Check(zs.Holding && zs.Segment == 1 && Near(zs.Position, new Vec3d(10, 0, 0), 1e-9) && !zs.Finished, "speed 0: the route holds at the segment's start");
            t.Check(Has(z, "route.zeroSpeed", "points[1].speed", FindingSeverity.Warning), "finding route.zeroSpeed");
            var neg = RoutePath.Build(R(false, P(0, 0, 0, -1f), P(10, 0, 0)));
            t.Check(double.IsPositiveInfinity(neg.Segments[0].Duration) && Has(neg, "route.zeroSpeed", "points[0].speed", FindingSeverity.Warning), "negative speed holds");
            var nan = RoutePath.Build(R(false, P(0, 0, 0, float.NaN), P(10, 0, 0)));
            t.Check(double.IsPositiveInfinity(nan.Segments[0].Duration), "NaN speed holds");
            var inf = RoutePath.Build(R(false, P(0, 0, 0, float.PositiveInfinity), P(10, 0, 0)));
            t.Check(double.IsPositiveInfinity(inf.Segments[0].Duration), "infinite speed holds (not finite)");
            // Zero length: skipped instantly.
            var zl = RoutePath.Build(R(false, P(0, 0, 0), P(0, 0, 0), P(10, 0, 0)));
            t.Check(zl.Segments[0].Duration == 0 && Rel(zl.Segments[1].Duration, 1.5, 1e-9), "a zero-length segment lasts 0");
            var zls = zl.Evaluate(0);
            t.Check(zls.Segment == 1 && Near(zls.Position, new Vec3d(0, 0, 0), 1e-12), "τ = 0 is already in the next segment");
            var allZero = RoutePath.Build(R(true, P(1, 1, 1), P(1, 1, 1), P(1, 1, 1)));
            var az = allZero.Evaluate(5);
            t.Check(az.Valid && Near(az.Position, new Vec3d(1, 1, 1), 1e-12) && allZero.FirstPassDuration == 0, "a loop of coincident points stands still");
            // Unknown ease: linear and a finding.
            var u = RoutePath.Build(R(false, P(0, 0, 0, 2f, "easeSideways"), P(10, 0, 0)));
            t.Check(!u.Segments[0].EaseKnown && u.Segments[0].EaseType == RouteEase.linear && Near(u.Evaluate(0.375).Position.X, 2.5, 1e-9), "unknown ease plays linear");
            t.Check(Has(u, "route.unknownEase", "points[0].ease", FindingSeverity.Warning), "finding route.unknownEase");
            // A stored rotation on another point is ignored.
            var pr = RoutePath.Build(R(false, P(0, 0, 0), P(10, 0, 0, 2f, "linear", "curve", aid: new RouteAid { Pos = new Vec3(0, 0, 5), Init = true }, rot: new Vec3(0, 90, 0)), P(10, 0, 10)));
            t.Check(Has(pr, "route.pointRotIgnored", "points[1].rot", FindingSeverity.Info), "finding route.pointRotIgnored");
            t.Check(Near(pr.Aids[1], new Vec3d(10, 0, 5), 1e-6), "the aid of a non-start point is not rotated by its stored rotation");
            // Negative / NaN τ.
            var l2 = RoutePath.Build(R(false, P(0, 0, 0), P(10, 0, 0)));
            t.Check(Near(l2.Evaluate(-3).Position, new Vec3d(0, 0, 0), 0) && Near(l2.Evaluate(double.NaN).Position, new Vec3d(0, 0, 0), 0), "τ < 0 and NaN evaluate at 0");
            // Empty route (no points; the reader always adds one, but Build must not throw).
            var empty = RoutePath.Build(new RouteNodeState());
            t.Check(empty.Mode == RoutePlayMode.None && !empty.Evaluate(1).Valid, "a route without points builds to nothing");
        }

        static bool Has(RoutePath p, string code, string path, FindingSeverity sev)
        {
            foreach (var f in p.Findings) if (f.Code == code && f.Path == path && f.Severity == sev) return true;
            return false;
        }

        static void AidFrame(TestRunner t)
        {
            // The start point rotated 90° about Y: its aid (point-local (5, 0, 0)) lies at route-local (0, 0, −5).
            var r = R(false, P(1, 0, 1, 2f, "linear", "curve", aid: new RouteAid { Pos = new Vec3(5, 0, 0), Init = true }, rot: new Vec3(0, 90, 0)), P(10, 0, 0));
            var path = RoutePath.Build(r);
            t.Check(Near(path.Aids[0], new Vec3d(1, 0, -4), 1e-6), "aid in the start point's rotated frame (" + path.Aids[0] + ")");
            t.Check(Near(path.Segments[0].Path[1], new Vec3d(1, 0, -4), 1e-6), "the curve passes through the rotated aid");
            // Missing aid or init false on a Curve point: the midpoint to the next point.
            var m = RoutePath.Build(R(true, P(0, 0, 0), P(10, 0, 0, 2f, "linear", "curve"), P(10, 0, 10, 2f, "linear", "curve", aid: new RouteAid { Pos = new Vec3(9, 9, 9), Init = false })));
            t.Check(Near(m.Aids[1], new Vec3d(10, 0, 5), 1e-9), "no aid: the midpoint to the next point");
            t.Check(Near(m.Aids[2], new Vec3d(5, 0, 5), 1e-9), "init false: the midpoint to P0 for the last point");
            // Euler conventions (Unity Z-X-Y): a point-local x axis under (0, 90, 0) → −z; under (90, 0, 0) a y axis → +z.
            t.Check(Near(Quatd.Euler(0, 90, 0).Rotate(new Vec3d(1, 0, 0)), new Vec3d(0, 0, -1), 1e-12), "Euler(0, 90, 0) · x = −z");
            t.Check(Near(Quatd.Euler(90, 0, 0).Rotate(new Vec3d(0, 1, 0)), new Vec3d(0, 0, 1), 1e-12), "Euler(90, 0, 0) · y = +z");
            var q = Quatd.Euler(30, 40, 50);
            var v = new Vec3d(0.3, -1.2, 2.5);
            var stepwise = Quatd.Euler(0, 40, 0).Rotate(Quatd.Euler(30, 0, 0).Rotate(Quatd.Euler(0, 0, 50).Rotate(v)));
            t.Check(Near(q.Rotate(v), stepwise, 1e-12), "Euler order: z, then x, then y");
            t.Check(Near(q.Inverse.Rotate(q.Rotate(v)), v, 1e-12), "inverse");
        }

        static void EndCarry(TestRunner t)
        {
            // easeOutExpo ends at 1 − 2^-10: the next segment starts with an extra leading point at the root.
            var r = R(false, P(0, 0, 0, 2f, "easeOutExpo"), P(10, 0, 0), P(10, 0, 10));
            var on = RoutePath.Build(r);
            var off = RoutePath.Build(r, new RouteOptions { Hs2EndCarry = false });
            var lenT = RoutePath.Build(r, new RouteOptions { Timing = RouteTiming.Length });
            var s1 = on.Segments[1];
            double endX = 10 * (1 - Math.Pow(2, -10));
            t.Check(s1.HasLead && s1.Path.Length == 3 && Near(s1.Path[0], new Vec3d(endX, 0, 0), 1e-9), "carry: segment 1 begins at the expo segment's end (" + s1.Path[0] + ")");
            t.Check(!off.Segments[1].HasLead && Rel(off.Segments[1].Duration, 1.5, 1e-9) && !lenT.Segments[1].HasLead, "no carry with the option off or the length timing");
            t.Check(s1.Duration < off.Segments[1].Duration, "the carried segment measures shorter (" + F(s1.Duration) + " s vs 1.5 s)");
            // Half of its time crawls the short lead span.
            var mid = on.Evaluate(on.Segments[0].Duration + s1.Duration * 0.5);
            t.Check(Near(mid.Position, new Vec3d(10, 0, 0), 1e-6), "carry: at half the segment's time the root has just reached P1");
            // The six UI eases end exactly (no carry).
            foreach (var e in RouteEases.UiSix)
            {
                var p = RoutePath.Build(R(false, P(0, 0, 0, 2f, e.ToString()), P(10, 0, 0), P(10, 0, 10)));
                t.Check(!p.Segments[1].HasLead, "no carry after " + e);
            }
            // A loop: the closing expo segment carries into segment 0 of the second pass, not the first.
            var loop = RoutePath.Build(R(true, P(0, 0, 0), P(10, 0, 0), P(10, 0, 10, 2f, "easeOutExpo")));
            t.Check(!loop.Segments[0].HasLead && loop.RepeatSegments[0].HasLead && !ReferenceEquals(loop.RepeatSegments[0], loop.Segments[0]),
                "loop: the first pass starts at P0, later passes with the carried point");
            t.Check(Rel(loop.RepeatDuration, loop.RepeatSegments[0].Duration + loop.RepeatSegments[1].Duration + loop.RepeatSegments[2].Duration, 1e-12), "repeat period = Σ repeat segments");
        }

        static JObject cachedRef;

        static JObject LoadReference(TestRunner t)
        {
            if (cachedRef != null) return cachedRef;
            string file = Path.Combine(t.FixturesDir ?? "Tests", "Routes", "route_reference.json");
            if (!File.Exists(file)) { t.Check(false, "reference table missing: " + file + " (python -I Tools/route_reference.py)"); return null; }
            cachedRef = JObject.Parse(File.ReadAllText(file));
            return cachedRef;
        }

        static Vec3d V(JToken a) => new Vec3d((double)a[0], (double)a[1], (double)a[2]);

        static RouteNodeState FromJson(JObject route)
        {
            var r = new RouteNodeState { Loop = (bool)route["loop"] };
            int i = 0;
            foreach (JObject p in (JArray)route["points"])
            {
                var pos = V(p["pos"]);
                var pt = P(pos.X, pos.Y, pos.Z, p["speed"] != null ? (float)(double)p["speed"] : 2f, (string)p["ease"] ?? "linear",
                    (string)p["connection"] ?? "line", p["link"] != null && (bool)p["link"], id: "p" + i++);
                if (p["rotY"] != null) pt.Rot = new Vec3(0, (float)(double)p["rotY"], 0);
                if (p["aid"] != null)
                {
                    var a = V(p["aid"]["pos"]);
                    pt.Aid = new RouteAid { Pos = new Vec3((float)a.X, (float)a.Y, (float)a.Z), Init = (bool)p["aid"]["init"] };
                }
                r.Points.Add(pt);
            }
            return r;
        }

        static void Reference(TestRunner t)
        {
            var doc = LoadReference(t);
            if (doc == null) return;
            var cases = (JObject)doc["cases"];
            t.Check(cases.Count >= 10, "reference cases: " + cases.Count);
            foreach (var kv in cases)
            {
                var c = (JObject)kv.Value;
                var path = RoutePath.Build(FromJson((JObject)c["route"]));
                string mode = (string)c["mode"];
                t.Equal(mode, path.Mode == RoutePlayMode.Once ? "once" : path.Mode == RoutePlayMode.Loop ? "loop" : path.Mode == RoutePlayMode.PingPong ? "pingpong" : "none", kv.Key + ": mode");
                var segs = (JArray)c["segments"];
                bool segOk = segs.Count == path.Segments.Count;
                for (int i = 0; segOk && i < segs.Count; i++)
                {
                    var s = path.Segments[i];
                    segOk &= (int)segs[i]["from"] == s.From && (int)segs[i]["to"] == s.To && (bool)segs[i]["lead"] == s.HasLead
                        && Rel((double)segs[i]["duration"], s.Duration, 1e-9) && Rel((double)segs[i]["length"], s.Length, 1e-9);
                }
                t.Check(segOk, kv.Key + ": segments (from, to, lead, length, duration within 1e-9)");
                var rep = (JArray)c["repeat"];
                bool repOk = rep.Count == path.RepeatSegments.Count;
                for (int i = 0; repOk && i < rep.Count; i++)
                    repOk &= (int)rep[i]["from"] == path.RepeatSegments[i].From && (bool)rep[i]["lead"] == path.RepeatSegments[i].HasLead
                        && Rel((double)rep[i]["duration"], path.RepeatSegments[i].Duration, 1e-9);
                t.Check(repOk, kv.Key + ": repeat segments");
                foreach (JObject smp in (JArray)c["samples"])
                {
                    double tau = (double)smp["tau"];
                    var s = path.Evaluate(tau);
                    bool ok = s.Segment == (int)smp["segment"] && Near(s.Percentage, (double)smp["p"], 1e-9) && Near(s.Eased, (double)smp["s"], 1e-9)
                        && Near(s.Position, V(smp["pos"]), 1e-7) && Near(s.LookTarget, V(smp["look"]), 1e-7)
                        && s.Reversed == (bool)smp["reversed"] && s.Finished == (bool)smp["finished"];
                    t.Check(ok, $"{kv.Key} τ={F(tau)}: segment {s.Segment}, p {F(s.Percentage)}, pos {s.Position} (reference segment {smp["segment"]}, p {smp["p"]}, pos {V(smp["pos"])})");
                }
            }
        }

        static void Geometry(TestRunner t)
        {
            var r = R(false, P(0, 0, 0, rot: new Vec3(0, 90, 0)), P(10, 0, 0));
            var local = RouteGeometry.MidpointAidLocal(r, 0);
            // midpoint (5, 0, 0) route-local; the start frame rotated 90° about Y: local = R⁻¹ · (5, 0, 0) = (0, 0, 5).
            t.Check(Near(Vec3d.From(local), new Vec3d(0, 0, 5), 1e-5), "midpoint aid of the rotated start point, point-local (" + local + ")");
            r.Points[0].Aid = new RouteAid { Pos = local, Init = true };
            t.Check(Near(RouteGeometry.AidPosition(r, 0), new Vec3d(5, 0, 0), 1e-5), "stored midpoint aid lands on the midpoint");
            var last = RouteGeometry.MidpointAidLocal(R(true, P(0, 0, 0), P(10, 0, 0), P(10, 0, 10)), 2);
            t.Check(Near(Vec3d.From(last), new Vec3d(-5, 0, -5), 1e-6), "the last point's midpoint aid points to P0");
            t.Check(Near(Vec3d.From(RouteGeometry.NextPointPosition(R(false, P(1, 2, 3)))), new Vec3d(1, 2, 13), 0), "R8: one point → start + (0, 0, 10)");
            t.Check(Near(Vec3d.From(RouteGeometry.NextPointPosition(R(false, P(0, 0, 0), P(4, 1, 2)))), new Vec3d(8, 2, 4), 0), "R8: last + (last − previous)");
            t.Check(Near(Vec3d.From(RouteGeometry.InsertPointPosition(R(false, P(0, 0, 0), P(4, 0, 2), P(9, 0, 9)), 1)), new Vec3d(2, 0, 1), 0), "inserted inside: the midpoint of its neighbours");
            var many = new List<RoutePoint>();
            for (int i = 0; i < 11; i++) many.Add(P(i, 0, 0));
            var full = R(false, many.ToArray());
            t.Check(!RouteGeometry.CanAddPoint(full, true) && RouteGeometry.CanAddPoint(full, false), "limit: 11 points with the option on; unlimited off");
            full.Points.RemoveAt(10);
            t.Check(RouteGeometry.CanAddPoint(full, true), "10 points: one more allowed");
            var lr = R(false, P(0, 0, 0, conn: "curve"), P(1, 0, 0, conn: "curve"), P(2, 0, 0), P(3, 0, 0, conn: "curve"));
            t.Check(RouteGeometry.LinkEffective(lr, 1) && !RouteGeometry.LinkEffective(lr, 0) && !RouteGeometry.LinkEffective(lr, 3) && !RouteGeometry.LinkEffective(lr, 2),
                "Link is effective only on a Curve point after a Curve point");
            var ab = R(false, P(0, 0, 0, conn: "curve"), P(1, 0, 0, conn: "curve", link: true), P(2, 0, 0), P(3, 0, 0));
            var absorbed = RouteGeometry.AbsorbedPoints(ab);
            t.Check(absorbed.Count == 1 && absorbed[0] == 1, "absorbed points (speed / ease greyed): [1]");
        }

        static void Perf(TestRunner t)
        {
            var pts = new List<RoutePoint>();
            for (int i = 0; i < 11; i++)
                pts.Add(P(i * 4, (i % 3) * 2, (i % 2) * 5, 2f + i, i % 2 == 0 ? "easeInOutQuad" : "linear", "curve", i % 3 != 0,
                    new RouteAid { Pos = new Vec3(1, 1, 1), Init = i % 2 == 0 }));
            var r = R(true, pts.ToArray());
            var plain = new List<RoutePoint>();
            for (int i = 0; i < 11; i++) plain.Add(P(i * 4, (i % 3) * 2, (i % 2) * 5, 2f + i, "easeOutQuad", "curve"));
            var r2 = R(true, plain.ToArray());
            for (int i = 0; i < 50; i++) { RoutePath.Build(r); RoutePath.Build(r2); }
            var builds = new List<double>();
            foreach (var route in new[] { r, r2 })
            {
                for (int k = 0; k < 7; k++)
                {
                    var sw = Stopwatch.StartNew();
                    for (int i = 0; i < 100; i++) RoutePath.Build(route);
                    builds.Add(sw.Elapsed.TotalMilliseconds / 100);
                }
            }
            builds.Sort();
            double build = builds[builds.Count / 2];
            var path = RoutePath.Build(r2);
            double sink = 0;
            for (int i = 0; i < 20000; i++) sink += path.Evaluate(i * 0.01).Position.X;
            var evals = new List<double>();
            for (int k = 0; k < 7; k++)
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 100000; i++) sink += path.Evaluate(i * 0.00137).Position.X;
                evals.Add(sw.Elapsed.TotalMilliseconds * 1000 / 100000);
            }
            evals.Sort();
            double eval = evals[evals.Count / 2];
            t.Info($"R-M5 Build {build:F4} ms (target 0.2; 11 curve points, chains and 11 separate curves), Evaluate {eval:F3} µs (target 5); sink {sink:E2}");
            t.Check(build <= 0.4, $"Build ≤ 2 × 0.2 ms ({build:F4} ms)");
            t.Check(eval <= 10, $"Evaluate ≤ 2 × 5 µs ({eval:F3} µs)");
        }
    }
}

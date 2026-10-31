// The route path evaluator: a pure function from route time τ to the child
// root's route-local position and look target, built from the route document. HS2's segment building (Line /
// Curve, links merging curve chains, a segment's speed and ease from its first point, the closing segment with loop),
// its timing rule (R3: the twice-padded length, a straight segment of length d lasts 3d / (10·speed)), its two-point
// ping-pong (R5) and its look-ahead, on a continuous timeline (R4: no one-frame lag, no lost remainders). Orientation is
// applied by the runtime (package R). Unity-free; tests: Editor/SceneModel/SceneTests_Routes.cs (plain Mono).
using System;
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes.Routes
{
    /// <summary>How a segment's duration is measured (R3).</summary>
    public enum RouteTiming
    {
        /// <summary>HS2's rule: the length of the twice-padded spline (a straight segment counts 3×) / (10 · speed).</summary>
        Hs2,
        /// <summary>Constant speed: the curve's own arc length / (10 · speed) (an option for new work).</summary>
        Length,
    }

    /// <summary>What happens after the queued segments have played once.</summary>
    public enum RoutePlayMode
    {
        /// <summary>One point or none: nothing plays (route.onePoint).</summary>
        None,
        /// <summary>No loop: the route stops at the end of the last segment (<see cref="RouteSample.Finished"/>).</summary>
        Once,
        /// <summary>Loop with three or more points: the queue restarts at segment 0 forever.</summary>
        Loop,
        /// <summary>Loop with exactly two points: HS2's ping-pong of the last queued segment (R5).</summary>
        PingPong,
    }

    public sealed class RouteOptions
    {
        /// <summary>Segment timing; default HS2's (scenes keep their speeds).</summary>
        public RouteTiming Timing = RouteTiming.Hs2;

        /// <summary>
        /// HS2 starts a segment with an extra leading control point at the child root's position when the root is not at the
        /// segment's first path point. On a continuous timeline this happens only after a segment whose ease does not reach
        /// exactly 1 at its end (easeOutExpo: 1 − 2^-10, easeInOutExpo: 1 − 2^-11): the next segment then begins with a
        /// short extra span that takes half (or a share) of its time. True (default) reproduces it with the `Hs2` timing;
        /// the `Length` timing never does it.
        /// </summary>
        public bool Hs2EndCarry = true;

        /// <summary>Look-ahead of the orient target in segment parameter (HS2: 0.05).</summary>
        public double LookAhead = 0.05;

        public static RouteOptions Default => new RouteOptions();
    }

    /// <summary>One evaluation of a route at a route time.</summary>
    public struct RouteSample
    {
        /// <summary>False when the route cannot play (one point): <see cref="Position"/> is the start point.</summary>
        public bool Valid;
        /// <summary>Route-local position of the child root.</summary>
        public Vec3d Position;
        /// <summary>Route-local look target for orient (C(ease(min(1, p + look-ahead)))).</summary>
        public Vec3d LookTarget;
        /// <summary>Index of the queued segment (0 … <see cref="RoutePath.Segments"/>.Count − 1), whatever the pass.</summary>
        public int Segment;
        /// <summary>0 = the first pass; then the loop pass (Loop) or the leg after the first pass (PingPong, 1, 2, …).</summary>
        public long Pass;
        /// <summary>The segment being played (the first pass's or the repeat pass's object).</summary>
        public RouteSegment Current;
        /// <summary>True on a reversed ping-pong leg (p = 1 − u).</summary>
        public bool Reversed;
        /// <summary>Seconds into the current segment (leg).</summary>
        public double LocalTime;
        /// <summary>The tween percentage p (0 … 1; on reversed legs 1 → 0).</summary>
        public double Percentage;
        /// <summary>The spline parameter s = clamp₀₁(ease(p)).</summary>
        public double Eased;
        /// <summary>No loop and τ ≥ the total duration: the root stands at the end.</summary>
        public bool Finished;
        /// <summary>The segment's speed is ≤ 0 or not finite: the root holds at the segment's start (route.zeroSpeed).</summary>
        public bool Holding;
    }

    /// <summary>One queued segment: its path (control points, route-local), timing, speed and ease.</summary>
    public sealed class RouteSegment
    {
        /// <summary>Queue index.</summary>
        public int Index;
        /// <summary>Index of the point that starts it (its speed / ease / connection are used).</summary>
        public int From;
        /// <summary>Index of the point it ends on (0 for the closing segment).</summary>
        public int To;
        /// <summary>Indices of the linked curve points merged into this segment (no segment of their own).</summary>
        public int[] Linked = Array.Empty<int>();
        public string FromId, ToId;
        public string[] LinkedIds = Array.Empty<string>();
        public RouteConnection Connection;
        public float Speed;
        /// <summary>The ease name as stored.</summary>
        public string Ease;
        /// <summary>The ease evaluated (linear for an unknown name).</summary>
        public RouteEase EaseType;
        public bool EaseKnown;
        /// <summary>The control points of the path (with the HS2 leading point when <see cref="HasLead"/>).</summary>
        public Vec3d[] Path;
        /// <summary>True when HS2's extra leading point (the previous segment's end) starts <see cref="Path"/>.</summary>
        public bool HasLead;
        /// <summary>The once-padded control array the curve is evaluated on.</summary>
        public Vec3d[] Padded;
        /// <summary>The measured length (by the timing rule).</summary>
        public double Length;
        /// <summary>Seconds; +∞ when the speed is ≤ 0 or not finite; 0 for a zero-length segment (skipped).</summary>
        public double Duration;

        /// <summary>The curve at spline parameter s.</summary>
        public Vec3d Interp(double s) => RouteSpline.Interp(Padded, s);

        /// <summary>s = clamp₀₁(ease(p)).</summary>
        public double EaseAt(double p) => RouteEases.Clamp01(RouteEases.Evaluate(EaseType, p));

        /// <summary>Where the segment leaves the root: C(clamp₀₁(ease(1))).</summary>
        public Vec3d End => Interp(EaseAt(1));
    }

    public sealed class RoutePath
    {
        RouteSegment[] segs = Array.Empty<RouteSegment>();
        RouteSegment[] repeat = Array.Empty<RouteSegment>();
        double[] segStart = Array.Empty<double>(), repStart = Array.Empty<double>();
        double lookAhead = 0.05;

        /// <summary>The queue as played the first time.</summary>
        public IReadOnlyList<RouteSegment> Segments => segs;

        /// <summary>What repeats after the first pass: Loop → the steady pass (the same objects as <see cref="Segments"/>
        /// unless HS2's end carry changes a path); PingPong → the last queued segment; otherwise empty.</summary>
        public IReadOnlyList<RouteSegment> RepeatSegments => repeat;

        public RoutePlayMode Mode { get; private set; }
        public RouteOptions Options { get; private set; }

        /// <summary>Route-local positions of the points (index order).</summary>
        public Vec3d[] Points { get; private set; } = Array.Empty<Vec3d>();
        /// <summary>Route-local aid positions (the midpoint rule where a Curve point has none); for Line points the same rule.</summary>
        public Vec3d[] Aids { get; private set; } = Array.Empty<Vec3d>();
        /// <summary>The start point's rotation (route-local).</summary>
        public Quatd StartRotation { get; private set; } = Quatd.Identity;

        /// <summary>route.zeroSpeed, route.unknownEase, route.pointRotIgnored (paths `points[i].…`).</summary>
        public FindingList Findings { get; } = new FindingList();

        /// <summary>Seconds of the first pass (+∞ when a segment holds).</summary>
        public double FirstPassDuration { get; private set; }
        /// <summary>Seconds of one repeat (Loop: the steady pass; PingPong: one leg; otherwise 0).</summary>
        public double RepeatDuration { get; private set; }
        /// <summary>Once: the first pass; Loop / PingPong: +∞ (endless); None: 0.</summary>
        public double TotalDuration => Mode == RoutePlayMode.Once ? FirstPassDuration : Mode == RoutePlayMode.None ? 0 : double.PositiveInfinity;

        /// <summary>At least two points.</summary>
        public bool CanPlay => Mode != RoutePlayMode.None;

        public static RoutePath Build(RouteNodeState route, RouteOptions options = null)
        {
            var o = options ?? RouteOptions.Default;
            var path = new RoutePath { Options = o, lookAhead = o.LookAhead, source = route };
            if (route == null || route.Points == null || route.Points.Count == 0)
            {
                path.Mode = RoutePlayMode.None;
                path.Points = new[] { Vec3d.Zero };
                path.Aids = new[] { Vec3d.Zero };
                return path;
            }
            var pts = route.Points;
            int n = pts.Count;
            path.StartRotation = Quatd.Euler(pts[0].Rot);
            path.Points = new Vec3d[n];
            path.Aids = new Vec3d[n];
            for (int i = 0; i < n; i++)
            {
                path.Points[i] = Vec3d.From(pts[i].Pos);
                if (i > 0 && !(pts[i].Rot.X == 0 && pts[i].Rot.Y == 0 && pts[i].Rot.Z == 0))
                    path.Findings.Info("route.pointRotIgnored", "points[" + i + "].rot", "a point other than the start stores a rotation; it is ignored");
            }
            for (int i = 0; i < n; i++) path.Aids[i] = RouteGeometry.AidPosition(route, i, path.Points, path.StartRotation);
            if (n < 2)
            {
                path.Mode = RoutePlayMode.None;
                return path;
            }
            path.Mode = !route.Loop ? RoutePlayMode.Once : n == 2 ? RoutePlayMode.PingPong : RoutePlayMode.Loop;

            var queue = BuildQueue(route, path);
            bool carry = o.Hs2EndCarry && o.Timing == RouteTiming.Hs2;
            // First pass: the first segment starts where Play put the root (the start point); each further one starts where
            // the previous one left it.
            path.segs = Realize(queue, path, o, carry, null);
            if (path.Mode == RoutePlayMode.Loop)
            {
                // The queue restarts and re-generates every segment; the steady pass is reached when the leads stop changing.
                var cur = path.segs;
                // Nothing to carry when the first pass has no lead and the closing segment ends on segment 0's start.
                bool closes = !AnyLead(cur) && Vec3d.ApproxEqual(Last(cur).End, cur[0].Path[0]);
                for (int it = 0; it < 8 && carry && !closes; it++)
                {
                    var next = Realize(queue, path, o, true, Last(cur));
                    bool same = SameLeads(next, cur);
                    cur = next;
                    if (same) break;
                }
                path.repeat = AnyLead(cur) || AnyLead(path.segs) ? cur : path.segs;
            }
            else if (path.Mode == RoutePlayMode.PingPong)
            {
                // After the first pass only the last queued segment alternates (reversed first), its path not re-generated.
                path.repeat = new[] { path.segs[path.segs.Length - 1] };
            }
            path.segStart = Starts(path.segs, out double d1);
            path.repStart = Starts(path.repeat, out double d2);
            path.FirstPassDuration = d1;
            path.RepeatDuration = d2;
            return path;
        }

        static RouteSegment Last(RouteSegment[] a) => a.Length == 0 ? null : a[a.Length - 1];

        static bool AnyLead(RouteSegment[] a)
        {
            foreach (var s in a) if (s.HasLead) return true;
            return false;
        }

        static bool SameLeads(RouteSegment[] a, RouteSegment[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].HasLead != b[i].HasLead) return false;
                if (a[i].HasLead && (a[i].Path[0] - b[i].Path[0]).SqrMagnitude > 1e-24) return false;
            }
            return true;
        }

        static double[] Starts(RouteSegment[] a, out double total)
        {
            var s = new double[a.Length];
            double t = 0;
            for (int i = 0; i < a.Length; i++)
            {
                s[i] = t;
                t += a[i].Duration;
            }
            total = t;
            return s;
        }

        /// <summary>A queued segment before timing: the point indices of its path in order.</summary>
        sealed class QueuedSegment
        {
            public int From, To;
            public List<int> Linked = new List<int>();
            public RouteConnection Connection;
            /// <summary>Path entries: point index (≥ 0) or −(index + 1) for that point's aid.</summary>
            public List<int> Entries = new List<int>();
        }

        static bool IsLink(RoutePoint p) => p.Link && IsCurve(p);
        static bool IsCurve(RoutePoint p) => p.ConnectionType == RouteConnection.Curve;

        /// <summary>segment building (HS2's queue order).</summary>
        static List<QueuedSegment> BuildQueue(RouteNodeState route, RoutePath path)
        {
            var pts = route.Points;
            int n = pts.Count;
            bool loop = route.Loop;
            var q = new List<QueuedSegment>();
            int i = 0;
            while (i < n)
            {
                if (!loop && i == n - 1) break;
                int start = i++;
                var seg = new QueuedSegment { From = start };
                if (!IsCurve(pts[start]))
                {
                    seg.Connection = RouteConnection.Line;
                    seg.To = start == n - 1 ? 0 : start + 1;
                    seg.Entries.Add(start);
                    seg.Entries.Add(seg.To);
                }
                else
                {
                    seg.Connection = RouteConnection.Curve;
                    seg.Entries.Add(start);
                    seg.Entries.Add(-(start + 1));
                    while (i < n && (loop || i != n - 1) && IsLink(pts[i]))
                    {
                        seg.Entries.Add(i);
                        seg.Entries.Add(-(i + 1));
                        seg.Linked.Add(i);
                        i++;
                    }
                    seg.To = i >= n ? 0 : i;
                    seg.Entries.Add(seg.To);
                }
                q.Add(seg);
            }
            return q;
        }

        static RouteSegment[] Realize(List<QueuedSegment> queue, RoutePath path, RouteOptions o, bool carry, RouteSegment before)
        {
            var outp = new RouteSegment[queue.Count];
            RouteSegment prev = before;
            for (int k = 0; k < queue.Count; k++)
            {
                var qs = queue[k];
                var ctl = new List<Vec3d>(qs.Entries.Count + 1);
                foreach (int e in qs.Entries) ctl.Add(e >= 0 ? path.Points[e] : path.Aids[-e - 1]);
                bool lead = false;
                if (carry && prev != null && !double.IsInfinity(prev.Duration))
                {
                    var end = prev.End;
                    if (!Vec3d.ApproxEqual(end, ctl[0]))
                    {
                        ctl.Insert(0, end);
                        lead = true;
                    }
                }
                var seg = path.MakeSegment(k, qs, ctl.ToArray(), lead, o);
                outp[k] = seg;
                prev = seg;
            }
            return outp;
        }

        /// <summary>The document the path was built from (MakeSegment reads the points' ids, speeds and eases).</summary>
        RouteNodeState source;

        RouteSegment MakeSegment(int index, QueuedSegment qs, Vec3d[] ctl, bool lead, RouteOptions o)
        {
            var p = source.Points[qs.From];
            var seg = new RouteSegment
            {
                Index = index,
                From = qs.From,
                To = qs.To,
                Linked = qs.Linked.ToArray(),
                FromId = p.Id,
                ToId = source.Points[qs.To].Id,
                Connection = qs.Connection,
                Speed = p.Speed,
                Ease = p.Ease,
                Path = ctl,
                HasLead = lead,
                Padded = RouteSpline.Pad(ctl),
            };
            seg.LinkedIds = new string[seg.Linked.Length];
            for (int j = 0; j < seg.Linked.Length; j++) seg.LinkedIds[j] = source.Points[seg.Linked[j]].Id;
            seg.EaseKnown = RouteEases.TryParse(p.Ease, out var ease);
            seg.EaseType = ease;
            if (!seg.EaseKnown)
                Findings.Warning("route.unknownEase", "points[" + qs.From + "].ease", "unknown ease '" + p.Ease + "' plays linear");
            seg.Length = o.Timing == RouteTiming.Length ? RouteSpline.ArcLength(seg.Padded) : RouteSpline.Hs2Length(seg.Padded);
            double speed = p.Speed;
            if (!(speed > 0) || double.IsInfinity(speed))
            {
                seg.Duration = double.PositiveInfinity;
                Findings.Warning("route.zeroSpeed", "points[" + qs.From + "].speed", "segment speed " + p.Speed.ToString(System.Globalization.CultureInfo.InvariantCulture) + " holds the route");
            }
            else seg.Duration = seg.Length > 0 ? seg.Length / (10.0 * speed) : 0;
            return seg;
        }

        /// <summary>The child root's route-local state at route time τ (seconds since Play; negative or NaN counts as 0).</summary>
        public RouteSample Evaluate(double tau)
        {
            var r = new RouteSample();
            if (Mode == RoutePlayMode.None || segs.Length == 0)
            {
                r.Position = r.LookTarget = Points.Length > 0 ? Points[0] : Vec3d.Zero;
                return r;
            }
            r.Valid = true;
            if (!(tau > 0)) tau = 0;
            if (tau < FirstPassDuration)
            {
                Locate(segs, segStart, tau, ref r);
                return Fill(ref r);
            }
            double rest = tau - FirstPassDuration;
            switch (Mode)
            {
                case RoutePlayMode.Loop:
                    {
                        double d = RepeatDuration;
                        if (!(d > 0))
                        {
                            // Every segment has zero length: the root stays where the pass left it.
                            r.Current = segs[segs.Length - 1];
                            r.Segment = r.Current.Index;
                            r.Percentage = 1;
                            return Fill(ref r);
                        }
                        double passes = Math.Floor(rest / d);
                        double local = rest - passes * d;
                        if (local >= d) { local = 0; passes += 1; }
                        r.Pass = 1 + (long)passes;
                        Locate(repeat, repStart, local, ref r);
                        return Fill(ref r);
                    }
                case RoutePlayMode.PingPong:
                    {
                        var leg = repeat[0];
                        double d = leg.Duration;
                        r.Current = leg;
                        r.Segment = leg.Index;
                        if (!(d > 0))
                        {
                            r.Percentage = 1;
                            return Fill(ref r);
                        }
                        double legs = Math.Floor(rest / d);
                        double local = rest - legs * d;
                        if (local >= d) { local = 0; legs += 1; }
                        r.Pass = 1 + (long)legs;
                        r.Reversed = ((long)legs & 1) == 0;
                        r.LocalTime = local;
                        double u = local / d;
                        r.Percentage = r.Reversed ? 1 - u : u;
                        return Fill(ref r);
                    }
                default:
                    {
                        r.Current = segs[segs.Length - 1];
                        r.Segment = r.Current.Index;
                        r.LocalTime = r.Current.Duration;
                        r.Percentage = 1;
                        r.Finished = true;
                        return Fill(ref r);
                    }
            }
        }

        /// <summary>Finds the segment of a pass at local time t (zero-duration segments are skipped; +∞ holds).</summary>
        static void Locate(RouteSegment[] a, double[] starts, double t, ref RouteSample r)
        {
            int k = a.Length - 1;
            for (int i = 0; i < a.Length; i++)
            {
                if (t < starts[i] + a[i].Duration) { k = i; break; }
            }
            var s = a[k];
            r.Current = s;
            r.Segment = s.Index;
            double local = t - starts[k];
            if (local < 0) local = 0;
            r.LocalTime = local;
            if (double.IsInfinity(s.Duration)) { r.Percentage = 0; r.Holding = true; }
            else if (s.Duration > 0) r.Percentage = Math.Min(1, local / s.Duration);
            else r.Percentage = 1;
        }

        RouteSample Fill(ref RouteSample r)
        {
            var s = r.Current;
            double p = r.Percentage;
            r.Eased = s.EaseAt(p);
            r.Position = s.Interp(r.Eased);
            r.LookTarget = s.Interp(s.EaseAt(Math.Min(1, p + lookAhead)));
            return r;
        }

        RoutePath() { }
    }
}

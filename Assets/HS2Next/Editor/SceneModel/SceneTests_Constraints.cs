// CM2-CM6, CM8: the arithmetic of a constraint and the remap of its ends. CG1-CG10: the graph of true loops and the sort of the motion
// pass, on a fake tree that stands for the built hierarchy, with the real bone names where the table of IK targets asks for them.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Api.Plugins;
using HS2Next.Save;
using HS2Next.Scenes.Constraints;
using HS2Next.Scenes.Overrides;
using HS2Next.Scenes.Routes;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save.Tests;

    public static class SceneTests_Constraints
    {
        public static void Run(TestRunner t)
        {
            CM2(t);
            CM3(t);
            CM4(t);
            CM5(t);
            CM6(t);
            CM8(t);
            CG1(t);
            CG2(t);
            CG3(t);
            CG4(t);
            CG5(t);
            CG6(t);
            CG7(t);
            CG8(t);
            CG9(t);
            CG10(t);
        }

        // ------------------------------------------------------------ helpers of the arithmetic

        const ConstraintParts P = ConstraintParts.Position, R = ConstraintParts.Rotation, S = ConstraintParts.Scale, L = ConstraintParts.LookAt, A = ConstraintParts.Parent;

        static double Diff(Vec3d a, Vec3d b) => (a - b).Magnitude;

        // the angle between two rotations through what they do to the axes, so a sign of the quaternion does not count
        static double Diff(Quatd a, Quatd b) => Math.Max(Diff(a.Rotate(new Vec3d(1, 0, 0)), b.Rotate(new Vec3d(1, 0, 0))),
            Math.Max(Diff(a.Rotate(new Vec3d(0, 1, 0)), b.Rotate(new Vec3d(0, 1, 0))), Diff(a.Rotate(new Vec3d(0, 0, 1)), b.Rotate(new Vec3d(0, 0, 1)))));

        static double Rnd(Random r, double a, double b) => a + (b - a) * r.NextDouble();
        static Vec3d RndV(Random r, double a, double b) => new Vec3d(Rnd(r, a, b), Rnd(r, a, b), Rnd(r, a, b));
        static Quatd RndQ(Random r) => ConstraintMath.Normalized(new Quatd(Rnd(r, -1, 1), Rnd(r, -1, 1), Rnd(r, -1, 1), Rnd(r, -1, 1)));
        static Vec3d RndS(Random r) => new Vec3d(Rnd(r, 0.3, 2) * (r.Next(4) == 0 ? -1 : 1), Rnd(r, 0.3, 2), Rnd(r, 0.3, 2));
        static ConstraintPose RndPose(Random r) => new ConstraintPose(RndV(r, -5, 5), RndQ(r), RndS(r));

        // a parent chain of random transforms as one frame: its matrix multiplied out, its rotation the product; uniform scales stay free of skew
        static ConstraintFrame Chain(Random r, int depth, bool skew)
        {
            var f = ConstraintFrame.World;
            for (int i = 0; i < depth; i++)
            {
                double u = Rnd(r, 0.5, 1.5);
                var local = ConstraintFrame.Of(RndV(r, -2, 2), RndQ(r), skew ? new Vec3d(Rnd(r, 0.5, 1.5), Rnd(r, 0.5, 1.5), Rnd(r, 0.5, 1.5)) : new Vec3d(u, u, u));
                f = Compose(f, local);
            }
            return f;
        }

        static ConstraintFrame Compose(in ConstraintFrame a, in ConstraintFrame b)
        {
            var x = a.Point(new Vec3d(b.M00, b.M10, b.M20)) - a.Point(Vec3d.Zero);
            var y = a.Point(new Vec3d(b.M01, b.M11, b.M21)) - a.Point(Vec3d.Zero);
            var z = a.Point(new Vec3d(b.M02, b.M12, b.M22)) - a.Point(Vec3d.Zero);
            var p = a.Point(new Vec3d(b.M03, b.M13, b.M23));
            return new ConstraintFrame
            {
                M00 = x.X, M01 = y.X, M02 = z.X, M03 = p.X,
                M10 = x.Y, M11 = y.Y, M12 = z.Y, M13 = p.Y,
                M20 = x.Z, M21 = y.Z, M22 = z.Z, M23 = p.Z,
                Rot = ConstraintMath.Normalized(a.Rot * b.Rot),
                Scale = ConstraintMath.Mul(a.Scale, b.Scale),
            };
        }

        static readonly ConstraintParts[] Sets = { P, R, S, L, A, P | R, P | L, P | S, R | S, L | S, P | R | S, P | L | S, A | S };

        // ------------------------------------------------------------ CM2

        static void CM2(TestRunner t)
        {
            t.Equal(P | L | S, ConstraintMath.PartsOf(new[] { "position", "lookAt", "scale" }), "CM2: the strings of an entry as a set");
            t.Equal(ConstraintParts.None, ConstraintMath.PartsOf(new[] { "wobble" }), "CM2: an unknown string counts for nothing");
            t.Equal(ConstraintParts.None, ConstraintMath.PartsOf(null), "CM2: no list");
            var stored = ConstraintMath.StoredOf(new ConstraintOffsetValue(new Vec3d(1, 2, 3), Quatd.Euler(10, 20, 30), new Vec3d(2, 2, 2)));
            var value = ConstraintMath.ValueOf(stored);
            t.Check(Diff(value.Pos, new Vec3d(1, 2, 3)) == 0 && Diff(value.Rot, Quatd.Euler(10, 20, 30)) < 1e-6 && value.Scale.Y == 2, "CM2: an offset as an entry stores it and back");

            var rnd = new Random(652);
            double worstP = 0, worstR = 0, worstS = 0, worstL = 0, worstA = 0, worstUp = 0;
            for (int i = 0; i < 1000; i++)
            {
                var s = RndPose(rnd);
                var o = new ConstraintOffsetValue(RndV(rnd, -1, 1), RndQ(rnd), RndS(rnd));
                var tp = RndV(rnd, -5, 5);
                var up = RndQ(rnd).Rotate(Vec3d.Up);

                var w = ConstraintMath.World(P, o, s, tp, up);
                worstP = Math.Max(worstP, Diff(w.Pos, new Vec3d(s.Pos.X + o.Pos.X, s.Pos.Y + o.Pos.Y, s.Pos.Z + o.Pos.Z)));
                if (!w.HasPos || w.HasRot || w.HasScale) t.Check(false, "CM2: position gives position only");

                w = ConstraintMath.World(R, o, s, tp, up);
                var xs = s.Rot.Rotate(o.Rot.Rotate(new Vec3d(1, 2, 3)));
                worstR = Math.Max(worstR, Diff(w.Rot.Rotate(new Vec3d(1, 2, 3)), xs));
                if (w.HasPos || !w.HasRot || w.HasScale) t.Check(false, "CM2: rotation gives rotation only");

                w = ConstraintMath.World(S, o, s, tp, up);
                worstS = Math.Max(worstS, Diff(w.Scale, new Vec3d(s.Scale.X * o.Scale.X, s.Scale.Y * o.Scale.Y, s.Scale.Z * o.Scale.Z)));

                w = ConstraintMath.World(L, ConstraintOffsetValue.Identity, s, tp, up);
                var dir = s.Pos - tp;
                dir = dir * (1 / dir.Magnitude);
                worstL = Math.Max(worstL, Diff(w.Rot.Rotate(new Vec3d(0, 0, 1)), dir));
                // right stays level and up looks up: the closed form of a look with the world's up
                worstUp = Math.Max(worstUp, Math.Abs(w.Rot.Rotate(new Vec3d(1, 0, 0)).Y));
                if (w.Rot.Rotate(Vec3d.Up).Y < -1e-12) t.Check(false, "CM2: a look keeps its up on the world's side");

                var frame = ConstraintFrame.Of(s.Pos, s.Rot, s.Scale);
                w = ConstraintMath.World(A, o, s, tp, up);
                worstA = Math.Max(worstA, Diff(w.Pos, frame.Point(o.Pos)));
                worstA = Math.Max(worstA, Diff(w.Rot.Rotate(new Vec3d(3, 1, 2)), s.Rot.Rotate(o.Rot.Rotate(new Vec3d(3, 1, 2)))));
            }
            t.Check(worstP <= 1e-12 && worstS <= 1e-12, "CM2: position and scale against closed forms, 1,000 frames (worst " + worstP + ", " + worstS + ")");
            t.Check(worstR <= 1e-12, "CM2: rotation against closed forms (worst " + worstR + ")");
            t.Check(worstL <= 1e-12 && worstUp <= 1e-12, "CM2: look-at against closed forms (worst " + worstL + ", " + worstUp + ")");
            t.Check(worstA <= 1e-12, "CM2: parent against closed forms (worst " + worstA + ")");

            // the euler text of an offset reads back as the same rotation
            double worstE = 0;
            for (int i = 0; i < 1000; i++)
            {
                var q = RndQ(rnd);
                var e = ConstraintMath.ToEuler(q);
                worstE = Math.Max(worstE, Diff(Quatd.Euler(e.X, e.Y, e.Z), q));
            }
            foreach (var e0 in new[] { new Vec3d(90, 30, 0), new Vec3d(-90, 30, 0), new Vec3d(90, 0, 40), new Vec3d(0, 0, 0), new Vec3d(45, 180, -120) })
            {
                var q = Quatd.Euler(e0.X, e0.Y, e0.Z);
                var e = ConstraintMath.ToEuler(q);
                worstE = Math.Max(worstE, Diff(Quatd.Euler(e.X, e.Y, e.Z), q));
            }
            t.Check(worstE <= 2e-7, "CM2: rotation to euler and back, gimbal cases too (worst " + worstE + ")");

            // world to local, against each end kind: a value put in a frame and read in the world comes back
            double wNodeP = 0, wNodeR = 0, wNodeS = 0, wRootP = 0, wSkew = 0;
            for (int i = 0; i < 1000; i++)
            {
                var parent = Chain(rnd, 1 + rnd.Next(4), false);
                var lp = RndV(rnd, -3, 3);
                var lq = RndQ(rnd);
                var ls = new Vec3d(Rnd(rnd, 0.3, 2), Rnd(rnd, 0.3, 2), Rnd(rnd, 0.3, 2));
                var wp = parent.Point(lp);
                var wq = ConstraintMath.Normalized(parent.Rot * lq);
                var wl = ConstraintMath.Mul(parent.Scale, ls);

                t.Check(parent.InversePoint(wp, out var back), "CM2: the frame has an inverse");
                wNodeP = Math.Max(wNodeP, Diff(back, lp));
                wNodeR = Math.Max(wNodeR, Diff(ConstraintMath.LocalRotation(wq, parent), lq));
                // a node that inherits: the channel holds the local scale; a node that does not: the world scale itself; a bone: as the first
                wNodeS = Math.Max(wNodeS, Diff(ConstraintMath.NodeScale(wl, parent, true, Vec3d.Zero), ls));
                wNodeS = Math.Max(wNodeS, Diff(ConstraintMath.NodeScale(wl, parent, false, Vec3d.Zero), wl));
                wNodeS = Math.Max(wNodeS, Diff(ConstraintMath.LocalScale(wl, parent, Vec3d.Zero), ls));

                // an IK target against the character's root: scale included, as the solve reads it back
                var root = Chain(rnd, 2, false);
                root.InversePoint(wp, out var inRoot);
                wRootP = Math.Max(wRootP, Diff(root.Point(inRoot), wp));

                var skew = Chain(rnd, 3, true);
                skew.InversePoint(wp, out var inSkew);
                wSkew = Math.Max(wSkew, Diff(skew.Point(inSkew), wp));
            }
            t.Check(wNodeP <= 1e-10 && wNodeR <= 1e-12 && wNodeS <= 1e-12, "CM2: world to local on a node and a bone (worst " + wNodeP + ", " + wNodeR + ", " + wNodeS + ")");
            t.Check(wRootP <= 1e-10, "CM2: world to local against a root (worst " + wRootP + ")");
            t.Check(wSkew <= 1e-10, "CM2: world to local under a skewed parent, through the matrix (worst " + wSkew + ")");

            var flat = ConstraintFrame.Of(Vec3d.Zero, Quatd.Identity, new Vec3d(0, 1, 1));
            t.Check(!flat.InversePoint(new Vec3d(1, 1, 1), out _), "CM2: a flat frame has no inverse");
            var keep = new Vec3d(7, 8, 9);
            t.Check(Diff(ConstraintMath.LocalScale(new Vec3d(2, 2, 2), flat, keep), new Vec3d(7, 2, 2)) == 0, "CM2: a flat parent component keeps the value before");
        }

        // ------------------------------------------------------------ CM3

        static void CM3(TestRunner t)
        {
            var rnd = new Random(653);
            double worstP = 0, worstR = 0, worstS = 0;
            foreach (var set in Sets)
                for (int i = 0; i < 200; i++)
                {
                    var s = RndPose(rnd);
                    var tg = RndPose(rnd);
                    var up = tg.Rot.Rotate(Vec3d.Up);
                    var o = ConstraintMath.Take(set, s, tg);
                    var w = ConstraintMath.World(set, o, s, tg.Pos, up);
                    if (w.HasPos) worstP = Math.Max(worstP, Diff(w.Pos, tg.Pos));
                    if (w.HasRot) worstR = Math.Max(worstR, Diff(w.Rot, tg.Rot));
                    if (w.HasScale) worstS = Math.Max(worstS, Diff(w.Scale, tg.Scale));
                    if (((set & (P | A)) != 0) != w.HasPos) t.Check(false, "CM3: " + set + " position");
                    if (((set & (R | L | A)) != 0) != w.HasRot) t.Check(false, "CM3: " + set + " rotation");
                    if (((set & S) != 0) != w.HasScale) t.Check(false, "CM3: " + set + " scale");
                    if ((set & (P | A)) == 0 && Diff(o.Pos, Vec3d.Zero) != 0) t.Check(false, "CM3: " + set + " leaves the position offset at 0");
                    if ((set & (R | L | A)) == 0 && Diff(o.Rot, Quatd.Identity) != 0) t.Check(false, "CM3: " + set + " leaves the rotation offset at identity");
                    if ((set & S) == 0 && Diff(o.Scale, new Vec3d(1, 1, 1)) != 0) t.Check(false, "CM3: " + set + " leaves the scale offset at 1");
                }
            t.Check(worstP <= 1e-9 && worstR <= 1e-9 && worstS <= 1e-9, "CM3: offset then value gives the target back (worst " + worstP + ", " + worstR + ", " + worstS + ")");

            // a source with a component near 0
            var flat = new ConstraintPose(new Vec3d(1, 2, 3), Quatd.Identity, new Vec3d(1e-7, 2, 4));
            var goal = new ConstraintPose(new Vec3d(2, 2, 3), Quatd.Identity, new Vec3d(3, 3, 3));
            var off = ConstraintMath.Take(S, flat, goal);
            t.Check(off.Scale.X == 1 && off.Scale.Y == 1.5 && off.Scale.Z == 0.75, "CM3: a source scale under 1e-6 gives 1 (" + off.Scale + ")");
            off = ConstraintMath.Take(A, flat, goal);
            t.Check(off.Pos.X == 1, "CM3: parent counts that component as 1 (" + off.Pos + ")");
            var zero = new ConstraintPose(flat.Pos, flat.Rot, new Vec3d(0, 2, 4));
            var w0 = ConstraintMath.World(S, new ConstraintOffsetValue(Vec3d.Zero, Quatd.Identity, new Vec3d(1, 1.5, 0.75)), zero, Vec3d.Zero, Vec3d.Up);
            t.Check(w0.Scale.X == 0 && w0.Scale.Y == 3, "CM3: scale follows the source to 0, as shown");
        }

        // ------------------------------------------------------------ CM4

        static void CM4(TestRunner t)
        {
            var a = new Vec3d(1, 2, 3);
            var b = new Vec3d(3, 6, -1);
            t.Check(Diff(ConstraintMath.Lerp(a, b, 0), a) == 0, "CM4: lerp at 0 is the base");
            t.Check(Diff(ConstraintMath.Lerp(a, b, 1), b) == 0, "CM4: lerp at 1 is the value");
            t.Check(Diff(ConstraintMath.Lerp(a, b, 0.5), new Vec3d(2, 4, 1)) < 1e-15, "CM4: lerp at 0.5 is the midpoint");

            var q0 = Quatd.Euler(0, 10, 0);
            var q1 = Quatd.Euler(0, 70, 0);
            var s0 = ConstraintMath.Slerp(q0, q1, 0);
            var s1 = ConstraintMath.Slerp(q0, q1, 1);
            t.Check(s0.X == q0.X && s0.Y == q0.Y && s0.Z == q0.Z && s0.W == q0.W, "CM4: slerp at 0 is the base, bit for bit");
            t.Check(s1.X == q1.X && s1.Y == q1.Y && s1.Z == q1.Z && s1.W == q1.W, "CM4: slerp at 1 is the value, bit for bit");
            t.Check(Diff(ConstraintMath.Slerp(q0, q1, 0.5), Quatd.Euler(0, 40, 0)) < 1e-12, "CM4: slerp at 0.5 is the middle of the arc");

            // the shorter arc across 180 degrees: from 170 to -170 goes through 180, not through 0
            var c0 = Quatd.Euler(0, 170, 0);
            var c1 = Quatd.Euler(0, -170, 0);
            t.Check(Diff(ConstraintMath.Slerp(c0, c1, 0.5), Quatd.Euler(0, 180, 0)) < 1e-12, "CM4: the shorter arc across 180");
            t.Check(Diff(ConstraintMath.Slerp(c0, c1, 0.25), Quatd.Euler(0, 175, 0)) < 1e-12, "CM4: a quarter of that arc");
            // a quaternion of the other sign is the same rotation
            var neg = new Quatd(-q1.X, -q1.Y, -q1.Z, -q1.W);
            t.Check(Diff(ConstraintMath.Slerp(q0, neg, 0.5), Quatd.Euler(0, 40, 0)) < 1e-12, "CM4: the sign of the quaternion does not count");
            t.Check(Diff(ConstraintMath.Slerp(q0, q0, 0.5), q0) < 1e-12, "CM4: equal rotations");
            var near = Quatd.Euler(0, 10.0000001, 0);
            t.Check(!double.IsNaN(ConstraintMath.Slerp(q0, near, 0.5).W), "CM4: nearly equal rotations");
        }

        // ------------------------------------------------------------ CM5

        sealed class Host : IOverrideHost
        {
            public int Applied;
            public bool TryKindOf(string nodeId, out NodeKind kind) { kind = NodeKind.Item; return true; }
            public bool TargetExists(string nodeId) => true;
            public void Apply(OverrideChannelState channel) => Applied++;
            public bool IsEffective(OverrideSlot winner) => true;
            public IEnumerable<string> NodeOrder() { yield break; }
        }

        const string Node = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        static void CM5(TestRunner t)
        {
            var host = new Host();
            var layer = new OverrideLayer(host);
            var pos = new ConstraintChannel(layer.Acquire("constraints", Node, "transform.pos", 10), false);
            var rot = new ConstraintChannel(layer.Acquire("constraints", Node, "transform.rot", 10), true);

            // a hand half on one object and half on another: the second at 0.5 over the first at 1
            var first = new Vec3d(2, 0, 0);
            var second = new Vec3d(4, 2, 0);
            void Frame()
            {
                pos.Begin(new Vec3d(100, 100, 100));
                pos.Blend(first, 1);
                pos.Blend(second, 0.5);
                pos.Flush();
                rot.Begin(Quatd.Euler(0, 90, 0));
                rot.Blend(Quatd.Euler(0, 10, 0), 1);
                rot.Blend(Quatd.Euler(0, 30, 0), 0.5);
                rot.Flush();
            }
            Frame();
            var slot = pos.Slot;
            t.Check(slot[0] == 3 && slot[1] == 1 && slot[2] == 0, "CM5: 1 then 0.5 gives the midpoint (" + slot.ValueJson() + ")");
            t.Check(Diff(rot.Rotation, Quatd.Euler(0, 20, 0)) < 1e-12, "CM5: the same for a rotation");
            t.Equal(1L, pos.Writes, "CM5: one Set per channel in a frame");
            t.Equal(2, host.Applied, "CM5: one apply per channel in a frame");

            // the order is the list's: 0.5 first, then 1, is the second value alone
            pos.Begin(new Vec3d(100, 100, 100));
            pos.Blend(second, 0.5);
            pos.Blend(first, 1);
            t.Check(Diff(pos.Vector, first) == 0, "CM5: the later entry blends over the earlier one");
            pos.Begin(new Vec3d(0, 0, 0));
            pos.Blend(new Vec3d(4, 0, 0), 0.5);
            t.Check(Diff(pos.Vector, new Vec3d(2, 0, 0)) < 1e-15, "CM5: the first blends over the base");
            Frame();   // back to the frame's value: nothing moved at the slot, since nothing was flushed in between

            // a stack at rest writes nothing, though its first entry alone differs from the result
            int version = layer.ValueVersion, applied = host.Applied;
            long writes = pos.Writes;
            for (int i = 0; i < 100; i++) Frame();
            t.Equal(writes, pos.Writes, "CM5: a resting stack makes no Set");
            t.Equal(version, layer.ValueVersion, "CM5: the layer sees no change from a resting stack");
            t.Equal(applied, host.Applied, "CM5: nothing is applied for a resting stack");

            first = new Vec3d(2.5, 0, 0);
            Frame();
            t.Equal(writes + 1, pos.Writes, "CM5: a moved stack sets once");

            pos.Clear();
            t.Check(!slot.HasValue, "CM5: cleared");
            Frame();
            t.Check(slot.HasValue, "CM5: after a clear the same value writes again");
        }

        // ------------------------------------------------------------ CM6

        static void CM6(TestRunner t)
        {
            var o = ConstraintOffsetValue.Identity;
            var up = new Vec3d(0, 0, 1);

            // coincident points: no rotation; the caller holds the last one
            var at = new ConstraintPose(new Vec3d(1, 2, 3), Quatd.Identity, new Vec3d(1, 1, 1));
            var w = ConstraintMath.World(L, o, at, new Vec3d(1, 2, 3), up);
            t.Check(!w.HasRot, "CM6: at one point a look gives no rotation");
            w = ConstraintMath.World(L, o, at, new Vec3d(1, 2, 3 + 0.9e-6), up);
            t.Check(!w.HasRot, "CM6: under 1e-6 counts as one point");
            w = ConstraintMath.World(L, o, at, new Vec3d(1, 2, 3 + 1.1e-6), up);
            t.Check(w.HasRot, "CM6: above 1e-6 it looks");
            var taken = ConstraintMath.Take(L, at, at);
            t.Check(Diff(taken.Rot, Quatd.Identity) == 0, "CM6: an offset taken at one point stays identity");

            // straight up: the world's up cannot hold, the target's own does
            var above = new ConstraintPose(new Vec3d(0, 5, 0), Quatd.Identity, new Vec3d(1, 1, 1));
            w = ConstraintMath.World(L, o, above, Vec3d.Zero, up);
            t.Check(Diff(w.Rot.Rotate(new Vec3d(0, 0, 1)), Vec3d.Up) < 1e-12, "CM6: straight up, +Z points at the source");
            t.Check(Diff(w.Rot.Rotate(Vec3d.Up), new Vec3d(0, 0, -1)) < 1e-12 || Vec3d.Dot(w.Rot.Rotate(Vec3d.Up), up) > 0.999, "CM6: its up comes from the target's up");
            t.Check(Vec3d.Dot(w.Rot.Rotate(Vec3d.Up), up) > 0.999, "CM6: the target's up is kept");
            // just off parallel, within 1e-4, the same; a little further the world's up holds
            var nearly = new ConstraintPose(new Vec3d(0.5e-4, 1, 0), Quatd.Identity, new Vec3d(1, 1, 1));
            var wn = ConstraintMath.World(L, o, nearly, Vec3d.Zero, up);
            t.Check(Vec3d.Dot(wn.Rot.Rotate(Vec3d.Up), up) > 0.999, "CM6: within 1e-4 of parallel it falls back");
            var off = new ConstraintPose(new Vec3d(2e-4, 1, 0), Quatd.Identity, new Vec3d(1, 1, 1));
            var wf = ConstraintMath.World(L, o, off, Vec3d.Zero, up);
            t.Check(Math.Abs(wf.Rot.Rotate(new Vec3d(1, 0, 0)).Y) < 1e-12, "CM6: past it the world's up holds");
            // a target's up that is parallel as well still gives a rotation
            var wp = ConstraintMath.World(L, o, above, Vec3d.Zero, Vec3d.Up);
            t.Check(Diff(wp.Rot.Rotate(new Vec3d(0, 0, 1)), Vec3d.Up) < 1e-12, "CM6: every up parallel still looks");

            // position first: the look aims from where this same constraint puts the target
            var src = new ConstraintPose(new Vec3d(0, 0, 10), Quatd.Identity, new Vec3d(1, 1, 1));
            var off2 = new ConstraintOffsetValue(new Vec3d(10, 0, -10), Quatd.Identity, new Vec3d(1, 1, 1));
            var both = ConstraintMath.World(P | L, off2, src, new Vec3d(99, 0, 0), Vec3d.Up);
            t.Check(Diff(both.Pos, new Vec3d(10, 0, 0)) == 0, "CM6: position first");
            var dir = new Vec3d(-10, 0, 10);
            dir = dir * (1 / dir.Magnitude);
            t.Check(Diff(both.Rot.Rotate(new Vec3d(0, 0, 1)), dir) < 1e-12, "CM6: the look aims from there, not from where it stood");
            var hold = ConstraintMath.World(P | L, new ConstraintOffsetValue(Vec3d.Zero, Quatd.Identity, new Vec3d(1, 1, 1)), src, new Vec3d(99, 0, 0), Vec3d.Up);
            t.Check(!hold.HasRot && hold.HasPos, "CM6: a position on the source leaves the look nothing to aim at");

            // an offset turns the look
            var o90 = new ConstraintOffsetValue(Vec3d.Zero, Quatd.Euler(0, 90, 0), new Vec3d(1, 1, 1));
            var turned = ConstraintMath.World(L, o90, new ConstraintPose(new Vec3d(0, 0, 5), Quatd.Identity, new Vec3d(1, 1, 1)), Vec3d.Zero, Vec3d.Up);
            t.Check(Diff(turned.Rot, Quatd.Euler(0, 90, 0)) < 1e-12, "CM6: the offset comes after the look");
        }

        // ------------------------------------------------------------ CM8

        static string Id(int n) => n.ToString("x32");

        static ConstraintEntry Entry(int id, int source, int target, string bone = null, string ik = null, string targetBone = null) => new ConstraintEntry
        {
            Id = Id(id),
            Label = "row " + id,
            Weight = 0.5f,
            On = id % 2 == 0,
            Source = new ConstraintEnd(Id(source), bone),
            Target = new ConstraintEnd(Id(target), targetBone, ik),
            Components = new List<string> { ConstraintComponents.Position, ConstraintComponents.Rotation },
            Offset = new ConstraintOffset(new Vec3(1, 2, 3), new Vec3(10, 20, 30), Vec3.One),
        };

        static void CM8(TestRunner t)
        {
            var list = new ConstraintList(new[]
            {
                Entry(1, 10, 11),                                     // both ends copied
                Entry(2, 10, 99),                                     // the target outside
                Entry(3, 98, 11),                                     // the source outside
                Entry(4, 11, 12, "acc:" + Id(7) + "/n_bone", "LeftHand"),   // both copied, with an address and an IK target
                Entry(5, 98, 99),                                     // neither
            });
            string before = list.ToJson();
            var map = new Dictionary<string, string> { [Id(10)] = Id(20), [Id(11)] = Id(21), [Id(12)] = Id(22) };
            int next = 100;
            var copies = ConstraintRemap.Copy(list.Entries, map, () => Id(next++), out int leftOut);
            t.Equal(2, copies.Count, "CM8: the entries with both ends");
            t.Equal(3, leftOut, "CM8: the others are counted");
            t.Equal(Id(100), copies[0].Id, "CM8: a new id");
            t.Equal(Id(101), copies[1].Id, "CM8: a new id for each, in list order");
            t.Equal(Id(20), copies[0].Source.Node, "CM8: the source on its copy");
            t.Equal(Id(21), copies[0].Target.Node, "CM8: the target on its copy");
            t.Equal("acc:" + Id(7) + "/n_bone", copies[1].Source.Bone, "CM8: an address stays");
            t.Equal("LeftHand", copies[1].Target.Ik, "CM8: an IK target stays");
            t.Check(copies[0].Label == "row 1" && copies[0].Weight == 0.5f && !copies[0].On && copies[0].Components.Count == 2 && copies[0].Offset.Pos.Y == 2, "CM8: everything else of the entry is kept");
            t.Equal(before, list.ToJson(), "CM8: the originals are not changed");
            t.Check(!ReferenceEquals(copies[0].Offset, list.Entries[0].Offset) && !ReferenceEquals(copies[0].Components, list.Entries[0].Components), "CM8: nothing is shared with the original");

            int again = 100;
            var second = ConstraintRemap.Copy(list.Entries, map, () => Id(again++), out _);
            t.Equal(string.Join("|", copies.Select(c => c.ToJson())), string.Join("|", second.Select(c => c.ToJson())), "CM8: deterministic");

            var unknown = ConstraintList.Parse("[{\"id\":\"" + Id(1) + "\",\"later\":7,\"source\":{\"node\":\"" + Id(10) + "\",\"x\":1},\"target\":{\"node\":\"" + Id(11) + "\"},\"components\":[\"position\"]}]");
            var kept = ConstraintRemap.Copy(unknown.Entries, map, () => Id(200), out _);
            t.Check(kept[0].ToJson().Contains("\"later\"") && kept[0].ToJson().Contains("\"x\""), "CM8: unknown members come along");

            var after = ConstraintRemap.Appended(list, copies);
            t.Equal(7, after.Count, "CM8: inserted after the original entries");
            t.Check(ReferenceEquals(after.Entries[0], list.Entries[0]) && ReferenceEquals(after.Entries[5], copies[0]), "CM8: the originals first, shared");
            t.Equal(5, list.Count, "CM8: the list before stays");
            t.Check(after.Problem(list) == null, "CM8: the result is a list an edit may store");
            t.Check(ConstraintRemap.Appended(list, new List<ConstraintEntry>()) == null, "CM8: nothing to add, no edit");
            t.Check(ConstraintRemap.Appended(null, null) == null, "CM8: no list, nothing to add");
            t.Equal(2, ConstraintRemap.Appended(null, copies).Count, "CM8: a scene with no list before");

            var empty = ConstraintRemap.Copy(null, map, () => Id(1), out int none);
            t.Check(empty.Count == 0 && none == 0, "CM8: no entries");
            ConstraintRemap.Copy(list.Entries, new Dictionary<string, string>(), () => Id(1), out int all);
            t.Equal(5, all, "CM8: nothing copied, nothing kept");
        }

        // ------------------------------------------------------------ the fake tree

        sealed class Tree : IConstraintTree
        {
            readonly List<int> parent = new List<int>();
            readonly List<string> names = new List<string>();
            public int Calls;

            public int Add(int parentPlace, string name = null)
            {
                parent.Add(parentPlace);
                names.Add(name);
                return parent.Count - 1;
            }

            public void Move(int place, int parentPlace) => parent[place] = parentPlace;
            public int Parent(int place) => parent[place];
            public int Count => parent.Count;
            public int Named(int under, string name)
            {
                for (int i = 0; i < parent.Count; i++)
                    if (names[i] == name && Under(i, under)) return i;
                return ConstraintGraph.NoPlace;
            }

            public bool Under(int a, int b)
            {
                Calls++;
                for (int p = a; p >= 0; p = parent[p])
                    if (p == b) return true;
                return false;
            }
        }

        // a character as the runtime builds one: a holder, a root under it, a skeleton under the root with the names of the table
        sealed class Body
        {
            public int Holder, Root, Unit;
            public int Hips, Spine01, Spine02, Head, ArmUpL, ArmUpR, HandL, HandR, LegUpL, LegUpR, FootL, FootR;
        }

        static Body Chara(Tree tr, int parent, List<ConstraintUnit> units = null)
        {
            var b = new Body { Holder = tr.Add(parent, "holder") };
            b.Root = tr.Add(b.Holder, "root");
            b.Hips = tr.Add(b.Root, "cf_J_Hips");
            b.Spine01 = tr.Add(b.Hips, "cf_J_Spine01");
            b.Spine02 = tr.Add(b.Spine01, "cf_J_Spine02");
            b.Head = tr.Add(b.Spine02, "cf_J_Head");
            b.ArmUpL = tr.Add(b.Spine02, "cf_J_ArmUp00_L");
            b.ArmUpR = tr.Add(b.Spine02, "cf_J_ArmUp00_R");
            b.HandL = tr.Add(b.ArmUpL, "cf_J_Hand_L");
            b.HandR = tr.Add(b.ArmUpR, "cf_J_Hand_R");
            b.LegUpL = tr.Add(b.Hips, "cf_J_LegUp00_L");
            b.LegUpR = tr.Add(b.Hips, "cf_J_LegUp00_R");
            b.FootL = tr.Add(b.LegUpL, "cf_J_Foot01_L");
            b.FootR = tr.Add(b.LegUpR, "cf_J_Foot01_R");
            if (units != null)
            {
                b.Unit = units.Count;
                units.Add(new ConstraintUnit(b.Root, parent));
            }
            return b;
        }

        static int IkIndex(string name) => Array.IndexOf(IkTargets.Names, name);

        static ConstraintLink Ik(Tree tr, Body b, int index, int source, string target)
        {
            int ik = IkIndex(target);
            return ConstraintLink.OfIk(index, source, ik, tr.Named(b.Root, ConstraintGraph.WrittenBone(ik)), b.Unit);
        }

        static ConstraintLink Node_(Tree tr, int index, int source, int holder) => ConstraintLink.OfNode(index, source, holder, tr.Parent(holder));

        static int[] Suspended(Tree tr, params ConstraintLink[] links) => ConstraintGraph.Suspend(links, tr).Select(c => c.Position).ToArray();

        static string Show(IEnumerable<int> a) => "[" + string.Join(",", a) + "]";

        // ------------------------------------------------------------ CG1

        static void CG1(TestRunner t)
        {
            var tr = new Tree();
            int world = tr.Add(-1);
            var n = new int[9];
            for (int i = 0; i < n.Length; i++) n[i] = tr.Add(world);

            // A -> B, B -> A
            var ab = Node_(tr, 0, n[1], n[0]);
            var ba = Node_(tr, 1, n[0], n[1]);
            t.Equal("[1]", Show(Suspended(tr, ab, ba)), "CG1: A from B, B from A: the higher index suspends");
            t.Check(ConstraintGraph.LoopThrough(new[] { ab, ba }, 1, tr).Length == 2, "CG1: the edit's check finds the loop");
            t.Equal("[]", Show(ConstraintGraph.LoopThrough(new[] { ab }, 0, tr)), "CG1: one way is no loop");

            // a loop of 3 and of 8
            foreach (int len in new[] { 3, 8 })
            {
                var l = new ConstraintLink[len];
                for (int i = 0; i < len; i++) l[i] = Node_(tr, i, n[(i + 1) % len], n[i]);
                t.Equal("[" + (len - 1) + "]", Show(Suspended(tr, l)), "CG1: a loop of " + len);
                var rows = ConstraintGraph.LoopThrough(l, 0, tr);
                t.Equal(len, rows.Length, "CG1: the loop's rows, " + len);
                t.Equal(0, rows[0], "CG1: rows start at the entry");
                for (int i = 0; i < rows.Length; i++)
                    t.Check(ConstraintGraph.Leads(l[rows[i]], l[rows[(i + 1) % rows.Length]], tr), "CG1: row " + i + " of " + len + " leads to the next");
                t.Equal(0, ConstraintGraph.Suspend(l.Take(len - 1).ToArray(), tr).Count, "CG1: the chain without the closing entry has no loop");
            }

            // the loop is told with the entry it suspends
            var c = ConstraintGraph.Suspend(new[] { ab, ba }, tr);
            t.Equal(1, c[0].Position, "CG1: the suspended entry");
            t.Equal("[1,0]", Show(c[0].Rows), "CG1: and its loop");

            // two loops apart: one entry of each; a loop beside a chain leaves the chain alone
            var d = new[]
            {
                Node_(tr, 0, n[1], n[0]), Node_(tr, 1, n[0], n[1]),
                Node_(tr, 2, n[3], n[2]), Node_(tr, 3, n[2], n[3]),
                Node_(tr, 4, n[4], n[5]),
            };
            t.Equal("[1,3]", Show(Suspended(tr, d)), "CG1: two loops, two entries");

            // two loops through one entry: the highest goes first, the rest is looked at again
            var e = new[]
            {
                Node_(tr, 0, n[1], n[0]),   // 0 reads 1
                Node_(tr, 1, n[0], n[1]),   // 1 reads 0: closes with 0
                Node_(tr, 2, n[0], n[2]),   // 2 reads 0
                Node_(tr, 3, n[2], n[0]),   // 3 reads 2, writes 0: closes with 2, and writes what 1 reads
            };
            var s = Suspended(tr, e);
            t.Check(s.Contains(3) && s.Contains(1), "CG1: every loop leaves a suspended entry " + Show(s));
        }

        // ------------------------------------------------------------ CG2

        static void CG2(TestRunner t)
        {
            var tr = new Tree();
            int world = tr.Add(-1);
            int a = tr.Add(world), under = tr.Add(a), b = tr.Add(world);
            t.Equal("[0]", Show(Suspended(tr, Node_(tr, 0, a, a))), "CG2: source equal to target");
            t.Equal("[0]", Show(Suspended(tr, Node_(tr, 0, under, a))), "CG2: source under target");
            var self = Node_(tr, 0, a, a);
            t.Equal("[0]", Show(ConstraintGraph.LoopThrough(new[] { self }, 0, tr)), "CG2: the loop of a self-loop is the entry");

            var body = new List<ConstraintUnit>();
            var c = Chara(tr, world, body);
            // a forearm cannot look at its own hand
            t.Equal("[0]", Show(Suspended(tr, ConstraintLink.OfBone(0, c.HandL, c.ArmUpL, c.Unit))), "CG2: a bone from a bone under it");
            // a hand's IK target cannot follow what that hand carries
            int rod = tr.Add(c.HandL);
            t.Equal("[0]", Show(Suspended(tr, Ik(tr, c, 0, rod, "LeftHand"))), "CG2: an IK target from what its hand carries");
            t.Equal("[]", Show(Suspended(tr, Node_(tr, 0, b, a))), "CG2: two free nodes");
        }

        // ------------------------------------------------------------ CG3

        static void CG3(TestRunner t)
        {
            var tr = new Tree();
            int world = tr.Add(-1);
            var c = Chara(tr, world);
            int onHand = tr.Add(c.HandL);
            t.Equal("[]", Show(Suspended(tr, Node_(tr, 0, c.HandL, onHand))), "CG3: a node attached to a bone may follow it");
            int a = tr.Add(world), child = tr.Add(a);
            t.Equal("[]", Show(Suspended(tr, Node_(tr, 0, a, child))), "CG3: a target under its source");
            t.Equal("[]", Show(Suspended(tr, ConstraintLink.OfBone(0, c.ArmUpL, c.HandL, 0))), "CG3: a bone from a bone above it");
        }

        // ------------------------------------------------------------ CG4

        static void CG4(TestRunner t)
        {
            var table = new Dictionary<string, string>
            {
                ["Body"] = "cf_J_Hips", ["LeftThigh"] = "cf_J_Hips", ["RightThigh"] = "cf_J_Hips",
                ["LeftShoulder"] = "cf_J_Spine01", ["RightShoulder"] = "cf_J_Spine01",
                ["LeftArmChain"] = "cf_J_ArmUp00_L", ["LeftHand"] = "cf_J_ArmUp00_L",
                ["RightArmChain"] = "cf_J_ArmUp00_R", ["RightHand"] = "cf_J_ArmUp00_R",
                ["LeftLegChain"] = "cf_J_LegUp00_L", ["LeftFoot"] = "cf_J_LegUp00_L",
                ["RightLegChain"] = "cf_J_LegUp00_R", ["RightFoot"] = "cf_J_LegUp00_R",
            };
            t.Equal(IkTargets.Count, table.Count, "CG4: the table has the 13 targets");
            t.Check(ConstraintGraph.WrittenBone(-1) == null && ConstraintGraph.WrittenBone(13) == null, "CG4: no bone for no target");
            var tr = new Tree();
            int world = tr.Add(-1);
            var units = new List<ConstraintUnit>();
            var c = Chara(tr, world, units);
            var bones = new[] { c.Hips, c.Spine01, c.Spine02, c.Head, c.ArmUpL, c.ArmUpR, c.HandL, c.HandR, c.LegUpL, c.LegUpR, c.FootL, c.FootR };
            for (int i = 0; i < IkTargets.Count; i++)
            {
                string name = IkTargets.Names[i];
                t.Equal(table[name], ConstraintGraph.WrittenBone(i), "CG4: " + name + " writes its row's bone");
                int written = tr.Named(c.Root, table[name]);
                foreach (int bone in bones)
                {
                    bool loop = tr.Under(bone, written);
                    var got = Suspended(tr, Ik(tr, c, 0, bone, name));
                    t.Equal(loop ? "[0]" : "[]", Show(got), "CG4: " + name + " from place " + bone + " under " + table[name]);
                }
            }
            // the same on the second character is no loop at all
            var other = Chara(tr, world, units);
            for (int i = 0; i < IkTargets.Count; i++)
                t.Equal("[]", Show(Suspended(tr, Ik(tr, c, 0, other.HandL, IkTargets.Names[i]))), "CG4: " + IkTargets.Names[i] + " from another character");
            // a static table: nothing here asks whether the limb is switched on, so the answer is the same in both states
        }

        // ------------------------------------------------------------ CG5

        static void CG5(TestRunner t)
        {
            var tr = new Tree();
            int world = tr.Add(-1);
            int a = tr.Add(world), b = tr.Add(a), folder = tr.Add(world);
            var c = Chara(tr, folder);

            // node under node: writing a parent moves a source under it
            t.Check(ConstraintGraph.Leads(Node_(tr, 0, world, a), Node_(tr, 1, b, world), tr), "CG5: a source under a written node");
            int other = tr.Add(world);
            t.Equal("[1]", Show(Suspended(tr, Node_(tr, 0, b, other), Node_(tr, 1, other, a))), "CG5: node under node, each from the other");

            // character under node
            int free = tr.Add(world);
            t.Equal("[1]", Show(Suspended(tr, Node_(tr, 0, free, folder), Node_(tr, 1, c.HandL, free))), "CG5: a character under a written folder");

            // bones under a holder
            t.Equal("[0]", Show(Suspended(tr, Node_(tr, 0, c.Head, c.Holder))), "CG5: a holder from a bone under it");

            // a bone between: one bone's write reaches what hangs below it, and only that
            t.Equal("[1]", Show(Suspended(tr, ConstraintLink.OfBone(0, free, c.ArmUpL, 0), Node_(tr, 1, c.HandL, free))), "CG5: through a bone");
            t.Equal("[]", Show(Suspended(tr, ConstraintLink.OfBone(0, free, c.ArmUpR, 0), Node_(tr, 1, c.HandL, free))), "CG5: the other arm");
        }

        // ------------------------------------------------------------ CG6

        static void CG6(TestRunner t)
        {
            var tr = new Tree();
            int world = tr.Add(-1);
            var units = new List<ConstraintUnit>();
            var c = Chara(tr, world, units);
            int onRight = tr.Add(c.HandR);
            t.Equal("[0]", Show(Suspended(tr, Ik(tr, c, 0, onRight, "RightHand"))), "CG6: a node on a hand against that hand's IK target");
            t.Equal("[]", Show(Suspended(tr, Ik(tr, c, 0, onRight, "LeftHand"))), "CG6: against the other hand's");
            t.Equal("[0]", Show(Suspended(tr, Ik(tr, c, 0, onRight, "RightArmChain"))), "CG6: against that arm's chain");
            t.Equal("[0]", Show(Suspended(tr, Ik(tr, c, 0, onRight, "Body"))), "CG6: the body moves all of it");
            t.Equal("[0]", Show(Suspended(tr, Ik(tr, c, 0, onRight, "LeftShoulder"))), "CG6: the torso turns all of it");
            t.Equal("[]", Show(Suspended(tr, Ik(tr, c, 0, onRight, "LeftFoot"))), "CG6: a foot does not");
        }

        // ------------------------------------------------------------ CG7

        static string Order(ConstraintPlan p) => string.Join(" ", p.Steps);

        static void CG7(TestRunner t)
        {
            // the two-hand grip: a rod in the right hand steers the left hand's target
            var tr = new Tree();
            int world = tr.Add(-1);
            var units = new List<ConstraintUnit>();
            var c = Chara(tr, world, units);
            int rod = tr.Add(c.HandR);
            var grip = new[] { Ik(tr, c, 0, rod, "LeftHand") };
            t.Equal(0, ConstraintGraph.Suspend(grip, tr).Count, "CG7: the grip is no true loop");
            var plan = ConstraintPlan.Build(grip, units, tr);
            t.Check(plan.Lag[0], "CG7: it lags one frame");
            t.Equal("K0 M0", Order(plan), "CG7: the constraint before the step");
            t.Check(plan.Edges.Any(e => e.Dropped && e.Kind == ConstraintEdgeKind.E2), "CG7: its E2 went");
            t.Check(plan.Edges.All(e => !e.Dropped || e.Droppable), "CG7: nothing else");

            // two characters, each steering a limb of the other: the lowest index takes the lag
            var tr2 = new Tree();
            int w2 = tr2.Add(-1);
            var u2 = new List<ConstraintUnit>();
            var a = Chara(tr2, w2, u2);
            var b = Chara(tr2, w2, u2);
            var cross = new[] { Ik(tr2, a, 0, b.HandR, "LeftHand"), Ik(tr2, b, 1, a.HandR, "LeftHand") };
            t.Equal(0, ConstraintGraph.Suspend(cross, tr2).Count, "CG7: the cross is no true loop");
            plan = ConstraintPlan.Build(cross, u2, tr2);
            t.Check(plan.Lag[0] && !plan.Lag[1], "CG7: the lowest index lags, the other not");
            t.Equal("K0 M0 K1 M1", Order(plan), "CG7: the cross's order");

            // deterministic: the same input again, and the sort does not look at anything else
            var again = ConstraintPlan.Build(cross, u2, tr2);
            t.Equal(Order(plan), Order(again), "CG7: same input, same order");

            // a chain beside a loop has no lag, wherever it stands in the list: only an edge on a loop is ever dropped
            var tr3 = new Tree();
            int w3 = tr3.Add(-1);
            var u3 = new List<ConstraintUnit>();
            var x = Chara(tr3, w3, u3);
            int r3 = tr3.Add(x.HandR), n1 = tr3.Add(w3);
            var side = new[] { Node_(tr3, 0, x.Head, n1), Ik(tr3, x, 1, r3, "LeftHand") };
            plan = ConstraintPlan.Build(side, u3, tr3);
            t.Check(!plan.Lag[0] && plan.Lag[1], "CG7: the node on the head does not lag for the grip beside it");
            t.Equal("K1 M0 K0", Order(plan), "CG7: the grip's constraint, the step, the node");

            // a target whose owner is the source's own unit by a parallel edge: nothing to gain, so no mark
            var tr4 = new Tree();
            int w4 = tr4.Add(-1);
            var u4 = new List<ConstraintUnit>();
            var y = Chara(tr4, w4, u4);
            int on4 = tr4.Add(y.HandR), onL = tr4.Add(y.HandL);
            var mixed = new[]
            {
                ConstraintLink.OfBone(0, y.HandR, y.Head, y.Unit),   // reads the right arm, written below
                Ik(tr4, y, 1, on4, "LeftHand"),
                Node_(tr4, 2, y.Head, onL),
            };
            plan = ConstraintPlan.Build(mixed, u4, tr4);
            t.Check(!plan.Lag[0], "CG7: a bone on its own character reads inside the step");
            t.Check(plan.Lag[1], "CG7: the grip lags");
            t.Check(!plan.Lag[2], "CG7: a node under the character reads after its step");
        }

        // ------------------------------------------------------------ CG8

        static void CG8(TestRunner t)
        {
            var tr = new Tree();
            int world = tr.Add(-1);
            int a = tr.Add(world), b = tr.Add(world), c = tr.Add(world), d = tr.Add(world);
            int bOnA = tr.Add(b);
            var links = new[] { Node_(tr, 0, a, c), Node_(tr, 1, d, a), Node_(tr, 2, bOnA, d) };
            t.Equal("[]", Show(Suspended(tr, links)), "CG8: no loop before the move");

            // the tree move: b goes under c, which entry 0 writes: 2 reads 0, 1 reads 2, 0 reads 1
            tr.Move(b, c);
            var after = ConstraintGraph.Suspend(links, tr);
            t.Equal("[2]", Show(after.Select(x => x.Position)), "CG8: the move closes a loop, the highest index is suspended");
            t.Equal(3, after[0].Rows.Length, "CG8: the finding names the three rows");
            var kept = links.Take(2).ToArray();
            var plan = ConstraintPlan.Build(kept, new List<ConstraintUnit>(), tr);
            t.Equal("K1 K0", Order(plan), "CG8: the others run");

            // undo of the move: active again
            tr.Move(b, world);
            t.Equal("[]", Show(Suspended(tr, links)), "CG8: undone, nothing is suspended");
            plan = ConstraintPlan.Build(links, new List<ConstraintUnit>(), tr);
            t.Equal(3, plan.Steps.Length, "CG8: and all three run");
        }

        // ------------------------------------------------------------ CG9

        static bool Has(ConstraintPlan p, ConstraintEdgeKind kind, int from, int to) => p.Edges.Any(e => e.Kind == kind && e.From == from && e.To == to);

        static int At(ConstraintPlan p, bool unit, int index)
        {
            for (int i = 0; i < p.Steps.Length; i++)
                if (p.Steps[i].IsUnit == unit && p.Steps[i].Index == index) return i;
            return -1;
        }

        static void CG9(TestRunner t)
        {
            // E1: y hangs under x
            {
                var tr = new Tree();
                int w = tr.Add(-1);
                var u = new List<ConstraintUnit>();
                var y0 = Chara(tr, w, u);   // pre-order would put this one first; the edge goes the other way
                var x = Chara(tr, w, u);
                tr.Move(y0.Holder, x.HandL);
                u[y0.Unit] = new ConstraintUnit(y0.Root, x.HandL);
                var p = ConstraintPlan.Build(Array.Empty<ConstraintLink>(), u, tr);
                t.Check(Has(p, ConstraintEdgeKind.E1, x.Unit, y0.Unit), "CG9: E1 parent to child");
                t.Equal("M1 M0", Order(p), "CG9: E1 against the pre-order");
            }
            // E2: a node on x's bone; a bone target on y from x; no edge when the owner is y
            {
                var tr = new Tree();
                int w = tr.Add(-1);
                var u = new List<ConstraintUnit>();
                var y = Chara(tr, w, u);
                var x = Chara(tr, w, u);
                int n = tr.Add(w);
                var links = new[] { Node_(tr, 0, x.HandL, n) };
                var p = ConstraintPlan.Build(links, u, tr);
                t.Check(Has(p, ConstraintEdgeKind.E2, x.Unit, p.VertexOf(0)), "CG9: E2 from the source's owner");
                t.Check(p.Edges.First(e => e.Kind == ConstraintEdgeKind.E2).Droppable, "CG9: E2 can be dropped");
                t.Check(At(p, true, x.Unit) < At(p, false, 0), "CG9: E2 runs the owner first");
                var bone = new[] { ConstraintLink.OfBone(0, x.HandL, y.Head, y.Unit) };
                p = ConstraintPlan.Build(bone, u, tr);
                t.Check(Has(p, ConstraintEdgeKind.E2, x.Unit, y.Unit), "CG9: E2 unit to unit for a bone target");
                t.Equal("M1 M0", Order(p), "CG9: E2 against the pre-order");
                t.Equal("[0]", Show(p.Bones[y.Unit]), "CG9: the bone link runs in its character's step");
                t.Equal(0, p.Bones[x.Unit].Length, "CG9: and in no other");
                var same = new[] { ConstraintLink.OfBone(0, y.HandL, y.Head, y.Unit) };
                p = ConstraintPlan.Build(same, u, tr);
                t.Equal(0, p.Edges.Count, "CG9: a bone from its own character has no edge");
                int free = tr.Add(w);
                p = ConstraintPlan.Build(new[] { Node_(tr, 0, free, n) }, u, tr);
                t.Equal(0, p.Edges.Count, "CG9: a free source has no owner");
            }
            // E3: a node under x's bone; an IK target on y under x; none from y itself
            {
                var tr = new Tree();
                int w = tr.Add(-1);
                var u = new List<ConstraintUnit>();
                var x = Chara(tr, w, u);
                int onHand = tr.Add(x.HandL), free = tr.Add(w);
                var p = ConstraintPlan.Build(new[] { Node_(tr, 0, free, onHand) }, u, tr);
                t.Check(Has(p, ConstraintEdgeKind.E3, x.Unit, p.VertexOf(0)), "CG9: E3 from the frame's owner");
                t.Check(!p.Edges.First(e => e.Kind == ConstraintEdgeKind.E3).Droppable, "CG9: E3 cannot be dropped");
                t.Check(At(p, true, x.Unit) < At(p, false, 0), "CG9: E3 runs the owner first");

                var y = Chara(tr, x.HandL, u);
                p = ConstraintPlan.Build(new[] { Ik(tr, y, 0, free, "LeftHand") }, u, tr);
                t.Check(Has(p, ConstraintEdgeKind.E3, x.Unit, p.VertexOf(0)), "CG9: E3 of an IK target from the unit y hangs under");
                t.Check(!Has(p, ConstraintEdgeKind.E3, y.Unit, p.VertexOf(0)), "CG9: and never from y itself");
                t.Check(Has(p, ConstraintEdgeKind.E4, p.VertexOf(0), y.Unit), "CG9: E4 on that target");
                t.Check(!p.Lag[0], "CG9: E3 and E4 do not close on y");
                t.Equal("M0 K0 M1", Order(p), "CG9: x, the target, y");

                var solo = new Tree();
                int sw = solo.Add(-1);
                var su = new List<ConstraintUnit>();
                var s = Chara(solo, sw, su);
                int sf = solo.Add(sw);
                p = ConstraintPlan.Build(new[] { Ik(solo, s, 0, sf, "LeftHand") }, su, solo);
                t.Check(!p.Edges.Any(e => e.Kind == ConstraintEdgeKind.E3), "CG9: a free character's IK target has no E3");
            }
            // E4: an IK target; a holder above the root
            {
                var tr = new Tree();
                int w = tr.Add(-1);
                var u = new List<ConstraintUnit>();
                int folder = tr.Add(w), free = tr.Add(w);
                var y = Chara(tr, folder, u);
                var p = ConstraintPlan.Build(new[] { Ik(tr, y, 0, free, "Body") }, u, tr);
                t.Check(Has(p, ConstraintEdgeKind.E4, p.VertexOf(0), y.Unit), "CG9: E4 on an IK target");
                t.Equal("K0 M0", Order(p), "CG9: E4 runs the target first");
                p = ConstraintPlan.Build(new[] { Node_(tr, 0, free, y.Holder) }, u, tr);
                t.Check(Has(p, ConstraintEdgeKind.E4, p.VertexOf(0), y.Unit), "CG9: E4 on the character's holder");
                p = ConstraintPlan.Build(new[] { Node_(tr, 0, free, folder) }, u, tr);
                t.Check(Has(p, ConstraintEdgeKind.E4, p.VertexOf(0), y.Unit), "CG9: E4 on a holder above it");
                t.Check(!p.Edges.First().Droppable, "CG9: E4 cannot be dropped");
            }
            // E5: a source under what j writes
            {
                var tr = new Tree();
                int w = tr.Add(-1);
                var u = new List<ConstraintUnit>();
                var y = Chara(tr, w, u);
                int n = tr.Add(w), m = tr.Add(n), free = tr.Add(w), o = tr.Add(w);
                var links = new[] { Node_(tr, 0, m, o), Node_(tr, 1, free, n) };
                var p = ConstraintPlan.Build(links, u, tr);
                t.Check(Has(p, ConstraintEdgeKind.E5, p.VertexOf(1), p.VertexOf(0)), "CG9: E5 from the writer");
                t.Check(p.Edges.First(e => e.Kind == ConstraintEdgeKind.E5).Droppable, "CG9: E5 can be dropped");
                t.Check(At(p, false, 1) < At(p, false, 0), "CG9: E5 against the list");
                var bone = new[] { ConstraintLink.OfBone(0, m, y.Head, y.Unit), Node_(tr, 1, free, n) };
                p = ConstraintPlan.Build(bone, u, tr);
                t.Check(Has(p, ConstraintEdgeKind.E5, p.VertexOf(1), y.Unit), "CG9: E5 to the step of a bone target");
                t.Check(At(p, false, 1) < At(p, true, y.Unit), "CG9: the writer before the step");
            }
            // E6: one target, by list order, against the plain order of ready vertices
            {
                var tr = new Tree();
                int w = tr.Add(-1);
                var u = new List<ConstraintUnit>();
                var x = Chara(tr, w, u);
                int n = tr.Add(w), f = tr.Add(w);
                var links = new[] { Node_(tr, 0, x.Head, n), Node_(tr, 1, f, n) };
                var p = ConstraintPlan.Build(links, u, tr);
                t.Check(Has(p, ConstraintEdgeKind.E6, p.VertexOf(0), p.VertexOf(1)), "CG9: E6 by list order");
                t.Check(!p.Edges.First(e => e.Kind == ConstraintEdgeKind.E6).Droppable, "CG9: E6 cannot be dropped");
                t.Equal("M0 K0 K1", Order(p), "CG9: entry 1 is ready first and still waits for entry 0");
                var ik = new[] { Ik(tr, x, 0, f, "LeftHand"), Ik(tr, x, 1, f, "LeftHand"), Ik(tr, x, 2, f, "RightHand") };
                p = ConstraintPlan.Build(ik, u, tr);
                t.Check(Has(p, ConstraintEdgeKind.E6, p.VertexOf(0), p.VertexOf(1)), "CG9: E6 on one IK target");
                t.Equal(1, p.Edges.Count(e => e.Kind == ConstraintEdgeKind.E6), "CG9: and not between two IK targets");
                var stack = new[] { ConstraintLink.OfBone(0, f, x.Head, x.Unit), ConstraintLink.OfBone(1, f, x.Head, x.Unit) };
                t.Equal("[0,1]", Show(ConstraintPlan.Build(stack, u, tr).Bones[x.Unit]), "CG9: E6 on one bone");
            }
            // E7: a node under a node, a bone under a bone; the list says the other way
            {
                var tr = new Tree();
                int w = tr.Add(-1);
                var u = new List<ConstraintUnit>();
                var x = Chara(tr, w, u);
                int n = tr.Add(w), child = tr.Add(n), f = tr.Add(w);
                var links = new[] { Node_(tr, 0, f, child), Node_(tr, 1, f, n) };
                var p = ConstraintPlan.Build(links, u, tr);
                t.Check(Has(p, ConstraintEdgeKind.E7, p.VertexOf(1), p.VertexOf(0)), "CG9: E7 parent's writer first");
                t.Check(!p.Edges.First(e => e.Kind == ConstraintEdgeKind.E7).Droppable, "CG9: E7 cannot be dropped");
                t.Equal("K1 K0 M0", Order(p), "CG9: E7 against the list");
                var bones = new[] { ConstraintLink.OfBone(0, f, x.HandL, x.Unit), ConstraintLink.OfBone(1, f, x.ArmUpL, x.Unit), ConstraintLink.OfBone(2, f, x.HandR, x.Unit) };
                t.Equal("[1,0,2]", Show(ConstraintPlan.Build(bones, u, tr).Bones[x.Unit]), "CG9: E7 on bones of one character");
                // a holder above an IK target's character: its root moves first, so the target's world value converts against the frame of this frame
                var ik = new[] { Ik(tr, x, 0, f, "LeftHand"), Node_(tr, 1, f, x.Holder) };
                p = ConstraintPlan.Build(ik, u, tr);
                t.Check(Has(p, ConstraintEdgeKind.E7, p.VertexOf(1), p.VertexOf(0)), "CG9: E7 from a holder to an IK target under it");
                t.Equal("K1 K0 M0", Order(p), "CG9: that order");
            }
            // bone links: E5 inside one character
            {
                var tr = new Tree();
                int w = tr.Add(-1);
                var u = new List<ConstraintUnit>();
                var x = Chara(tr, w, u);
                var chain = new[] { ConstraintLink.OfBone(0, x.HandL, x.Head, x.Unit), ConstraintLink.OfBone(1, x.Head, x.HandL, x.Unit) };
                t.Equal(2, ConstraintGraph.Suspend(chain, tr).Count + 1, "CG9: each reads the other: a true loop");
                var line = new[] { ConstraintLink.OfBone(0, x.HandL, x.Head, x.Unit), ConstraintLink.OfBone(1, x.FootR, x.ArmUpL, x.Unit) };
                t.Equal("[1,0]", Show(ConstraintPlan.Build(line, u, tr).Bones[x.Unit]), "CG9: E5 among bones, against the list");
            }
            // ready vertices: constraints first by list index, then units by pre-order
            {
                var tr = new Tree();
                int w = tr.Add(-1);
                var u = new List<ConstraintUnit>();
                Chara(tr, w, u);
                Chara(tr, w, u);
                int a = tr.Add(w), b = tr.Add(w), f = tr.Add(w);
                var p = ConstraintPlan.Build(new[] { Node_(tr, 0, f, a), Node_(tr, 1, f, b) }, u, tr);
                t.Equal("K0 K1 M0 M1", Order(p), "CG9: the order of ready vertices");
            }
        }

        // ------------------------------------------------------------ CG10

        // Random scenes on a random tree: characters and items at any depth, nodes hanging anywhere, every kind of target, stacks.
        sealed class Scene
        {
            public Tree Tree = new Tree();
            public List<ConstraintUnit> Units = new List<ConstraintUnit>();
            public List<Body> Bodies = new List<Body>();
            public List<int> Places = new List<int>();
            public List<ConstraintLink> Links = new List<ConstraintLink>();
        }

        static Scene Make(int seed)
        {
            var r = new Random(seed);
            var s = new Scene();
            var tr = s.Tree;
            int world = tr.Add(-1);
            s.Places.Add(tr.Add(world));
            int nodes = 2 + r.Next(10), charas = r.Next(5), items = r.Next(3);
            for (int i = 0; i < nodes; i++) s.Places.Add(tr.Add(s.Places[r.Next(s.Places.Count)]));
            for (int i = 0; i < charas; i++)
            {
                var b = Chara(tr, s.Places[r.Next(s.Places.Count)], s.Units);
                s.Bodies.Add(b);
                s.Places.AddRange(new[] { b.Hips, b.Head, b.HandL, b.HandR, b.ArmUpL, b.FootR, b.FootL });
                if (r.Next(2) == 0) s.Places.Add(tr.Add(b.HandR));
                if (r.Next(3) == 0) s.Places.Add(tr.Add(b.HandL));
                if (r.Next(3) == 0) s.Places.Add(tr.Add(b.Holder));
            }
            for (int i = 0; i < items; i++)
            {
                // an item with motion: its root is a place under its holder, the units keep pre-order only by being appended
                int parent = s.Places[r.Next(s.Places.Count)];
                int holder = tr.Add(parent);
                int root = r.Next(2) == 0 ? holder : tr.Add(holder);
                int bone = tr.Add(root);
                s.Units.Add(new ConstraintUnit(root, parent));
                s.Places.Add(holder);
                s.Places.Add(bone);
            }
            int count = 1 + r.Next(14);
            for (int i = 0; i < count; i++)
            {
                int source = s.Places[r.Next(s.Places.Count)];
                int kind = r.Next(3);
                if (kind == 2 && s.Bodies.Count > 0)
                {
                    var b = s.Bodies[r.Next(s.Bodies.Count)];
                    s.Links.Add(Ik(tr, b, i, source, IkTargets.Names[r.Next(IkTargets.Count)]));
                }
                else if (kind == 1 && s.Bodies.Count > 0)
                {
                    var b = s.Bodies[r.Next(s.Bodies.Count)];
                    var bones = new[] { b.Hips, b.Head, b.HandL, b.HandR, b.ArmUpL, b.FootR };
                    s.Links.Add(ConstraintLink.OfBone(i, source, bones[r.Next(bones.Length)], b.Unit));
                }
                else
                {
                    int target = s.Places[1 + r.Next(s.Places.Count - 1)];
                    s.Links.Add(ConstraintLink.OfNode(i, source, target, tr.Parent(target)));
                }
            }
            return s;
        }

        static int Depth(Tree tr, int place)
        {
            int d = 0;
            for (int p = tr.Parent(place); p >= 0; p = tr.Parent(p)) d++;
            return d;
        }

        // a cycle search of its own, by plain reachability, so the fuzz does not lean on the code it checks
        static bool HasTrueLoop(IReadOnlyList<ConstraintLink> l, Tree tr)
        {
            int n = l.Count;
            var reach = new bool[n, n];
            for (int j = 0; j < n; j++)
                for (int k = 0; k < n; k++)
                    reach[j, k] = tr.Under(l[k].Source, l[j].Writes);
            for (int m = 0; m < n; m++)
                for (int a = 0; a < n; a++)
                    for (int b = 0; b < n; b++)
                        if (reach[a, m] && reach[m, b]) reach[a, b] = true;
            for (int i = 0; i < n; i++)
                if (reach[i, i]) return true;
            return false;
        }

        static string Fingerprint(ConstraintPlan p, IEnumerable<ConstraintCycle> c) =>
            Order(p) + " | " + string.Join(",", p.Lag) + " | " + string.Join(";", p.Bones.Select(Show)) + " | " + string.Join(";", c.Select(x => x.Position + Show(x.Rows)));

        static void CG10(TestRunner t)
        {
            int rounds = 2000, loops = 0, lags = 0, withBones = 0, steps = 0, bad = 0;
            for (int seed = 0; seed < rounds; seed++)
            {
                var s = Make(seed);
                var cycles = ConstraintGraph.Suspend(s.Links, s.Tree);
                var gone = new HashSet<int>(cycles.Select(c => c.Position));
                var live = s.Links.Where((l, i) => !gone.Contains(i)).ToList();
                loops += cycles.Count;

                // every true loop leaves a suspended entry, and only an entry on a loop is suspended
                if (HasTrueLoop(live, s.Tree)) { t.Check(false, "CG10: seed " + seed + " keeps a true loop"); bad++; }
                foreach (var c in cycles)
                {
                    bool closes = c.Rows.Length > 0 && c.Rows[0] == c.Position;
                    for (int i = 0; closes && i < c.Rows.Length; i++)
                        closes = ConstraintGraph.Leads(s.Links[c.Rows[i]], s.Links[c.Rows[(i + 1) % c.Rows.Length]], s.Tree);
                    if (!closes) { t.Check(false, "CG10: seed " + seed + " suspends an entry without a loop " + Show(c.Rows)); bad++; }
                    if (c.Rows.Any(r => r > c.Position)) { t.Check(false, "CG10: seed " + seed + " suspends an entry below the highest of its loop " + Show(c.Rows)); bad++; }
                }

                // the sort ends with only droppable edges dropped, and the order respects every kept edge
                ConstraintPlan plan;
                try { plan = ConstraintPlan.Build(live, s.Units, s.Tree); }
                catch (Exception e) { t.Check(false, "CG10: seed " + seed + " " + e.Message); bad++; continue; }
                var at = new Dictionary<int, int>();
                for (int i = 0; i < plan.Steps.Length; i++) at[plan.Steps[i].IsUnit ? plan.Steps[i].Index : plan.VertexOf(plan.Steps[i].Index)] = i;
                steps += plan.Steps.Length;
                int vertices = s.Units.Count + live.Count(l => l.Kind != ConstraintTargetKind.Bone);
                if (plan.Steps.Length != vertices || at.Count != vertices) { t.Check(false, "CG10: seed " + seed + " every vertex runs once"); bad++; }
                foreach (var e in plan.Edges)
                {
                    if (e.Dropped && !e.Droppable) { t.Check(false, "CG10: seed " + seed + " dropped " + e.Kind); bad++; }
                    if (!e.Dropped && at[e.From] >= at[e.To]) { t.Check(false, "CG10: seed " + seed + " " + e.Kind + " out of order"); bad++; }
                    if (e.Droppable && e.Link < 0) { t.Check(false, "CG10: seed " + seed + " an edge without its constraint"); bad++; }
                }

                // a mark for each drop and a drop for each mark
                for (int k = 0; k < live.Count; k++)
                {
                    bool dropped = plan.Edges.Any(e => e.Dropped && e.Link == k);
                    if (dropped != plan.Lag[k]) { t.Check(false, "CG10: seed " + seed + " lag mark of " + k); bad++; }
                    if (plan.Lag[k]) lags++;
                }

                // an edge is only dropped on a loop: putting the kept edges and that edge together has a path back
                foreach (var e in plan.Edges.Where(x => x.Dropped))
                    if (!Reaches(plan, e.To, e.From)) { t.Check(false, "CG10: seed " + seed + " dropped an edge on no loop"); bad++; }

                // the fixed edges climb in (depth, rank), or stay and climb in the list: the proof that the sort ends
                foreach (var e in plan.Edges.Where(x => !x.Droppable))
                {
                    var from = Rank(plan, s, live, e.From);
                    var to = Rank(plan, s, live, e.To);
                    bool climbs = from.d < to.d || (from.d == to.d && from.rank < to.rank) || (e.Kind == ConstraintEdgeKind.E6 && from.d == to.d && e.From < e.To);
                    if (!climbs) { t.Check(false, "CG10: seed " + seed + " " + e.Kind + " does not climb"); bad++; }
                }

                // bones of one character: each runs once, a parent before its child, a stack in list order, a source's writer first
                for (int u = 0; u < plan.Bones.Length; u++)
                {
                    var seq = plan.Bones[u];
                    withBones += seq.Length > 1 ? 1 : 0;
                    for (int a = 0; a < seq.Length; a++)
                        for (int b = a + 1; b < seq.Length; b++)
                        {
                            var early = live[seq[a]];
                            var late = live[seq[b]];
                            bool wrong = (early.Target != late.Target && s.Tree.Under(early.Target, late.Target)) ||
                                (early.Target == late.Target && seq[a] > seq[b]) ||
                                s.Tree.Under(early.Source, late.Target);
                            if (wrong) { t.Check(false, "CG10: seed " + seed + " bones of unit " + u + " " + Show(seq)); bad++; }
                        }
                }

                // same input, same output
                var again = Make(seed);
                var c2 = ConstraintGraph.Suspend(again.Links, again.Tree);
                var gone2 = new HashSet<int>(c2.Select(c => c.Position));
                var p2 = ConstraintPlan.Build(again.Links.Where((l, i) => !gone2.Contains(i)).ToList(), again.Units, again.Tree);
                if (Fingerprint(plan, cycles) != Fingerprint(p2, c2)) { t.Check(false, "CG10: seed " + seed + " is not deterministic"); bad++; }
            }
            t.Equal(0, bad, "CG10: " + rounds + " graphs");
            t.Check(loops > 100 && lags > 100 && withBones > 100, "CG10: the graphs reach loops, lags and bone orders (" + loops + ", " + lags + ", " + withBones + ")");
            t.Info("CG10: " + rounds + " graphs, " + steps + " steps, " + loops + " suspended, " + lags + " lags, " + withBones + " characters with bone orders");
        }

        // a path along the edges that remain
        static bool Reaches(ConstraintPlan p, int from, int to)
        {
            var seen = new HashSet<int> { from };
            var q = new Queue<int>();
            q.Enqueue(from);
            while (q.Count > 0)
            {
                int v = q.Dequeue();
                if (v == to) return true;
                foreach (var e in p.Edges)
                    if (e.From == v && !e.Dropped && seen.Add(e.To)) q.Enqueue(e.To);
            }
            return false;
        }

        static (int d, int rank) Rank(ConstraintPlan p, Scene s, List<ConstraintLink> live, int vertex)
        {
            if (vertex < p.Units) return (Depth(s.Tree, s.Units[vertex].Root), 1);
            var l = live[vertex - p.Units];
            return l.Kind == ConstraintTargetKind.Ik ? (Depth(s.Tree, s.Units[l.Unit].Root), 0) : (Depth(s.Tree, l.Target), 0);
        }
    }
}

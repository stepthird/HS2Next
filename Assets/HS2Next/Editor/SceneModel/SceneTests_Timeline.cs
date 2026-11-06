// The Timeline engine without the engine (TL1-TL7): the five eases exact in double; the segment rule (before the first
// key, at a key, between, after the last, one key, no key); the values per type (vectors, colours with alpha, rotations on
// the shorter arc, a 180° pair, the camera, the depth of field with its on / off fade, size carry, blur and focus blend);
// the crossings of the play head (play from 0, resume, wrap, the 100-pass cap, not looping, seek and pause fire none, a
// step); the playback state machine against a recording sink (play idempotent, pause, stop releases every value, seek
// from stopped, autoplay, a document change, missing targets, the authoring suspension); the timeline.build input
// (camera and dof carry-over, the dof defaults and the sharp / full conversion, eases, the step types, the refusals);
// the four compositions of Tests/Timeline/compositions (17 tracks / 120 keys with their eases; every type of all_types).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2Next.Api.Bridge;
using HS2Next.Save;
using HS2Next.Scenes.Timeline;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save.Tests;

    public static class SceneTests_Timeline
    {
        public static void Run(TestRunner t)
        {
            Eases(t);
            Segments(t);
            Values(t);
            Crossings(t);
            StateMachine(t);
            Suspension(t);
            BuildParser(t);
            BuildEvents(t);
            Compositions(t);
        }

        // ---------- fakes ----------

        /// <summary>A sink that records what the player shows (by channel text) and the runs it fires.</summary>
        sealed class Sink : ITimelineSink
        {
            public readonly Dictionary<string, NodeKind> Nodes = new Dictionary<string, NodeKind>();
            public readonly Dictionary<string, object> Shown = new Dictionary<string, object>();
            public readonly List<TimelineEventRun> Fired = new List<TimelineEventRun>();
            public int Sets, Clears, Releases, ReleaseAlls;

            public NodeKind? KindOf(string nodeId) => nodeId != null && Nodes.TryGetValue(nodeId, out var k) ? k : (NodeKind?)null;
            void Put(TimelineChannel c, object v) { Shown[c.ToString()] = v; Sets++; }
            public void SetNumber(TimelineChannel c, double v) => Put(c, v);
            public void SetInteger(TimelineChannel c, int v) => Put(c, v);
            public void SetVector(TimelineChannel c, in TimelineVec3 v) => Put(c, v);
            public void SetRotation(TimelineChannel c, in TimelineQuat v) => Put(c, v);
            public void SetColor(TimelineChannel c, in TimelineColor v) => Put(c, v);
            public void SetCamera(TimelineChannel c, in TimelineCameraValue v) => Put(c, v);
            public void SetDepthOfField(TimelineChannel c, in TimelineDofValue v) => Put(c, v);
            public void SetAnimation(TimelineChannel c, in TimelineAnimeRow v) => Put(c, v);
            public void Clear(TimelineChannel c) { Shown.Remove(c.ToString()); Clears++; }
            public void Release(TimelineChannel c) { Shown.Remove(c.ToString()); Releases++; }
            public void ReleaseAll() { Shown.Clear(); ReleaseAlls++; }
            public void Fire(TimelineEventRun run) => Fired.Add(run);

            public T Get<T>(string nodeId, string path) => Shown.TryGetValue((nodeId ?? "scene") + " " + path, out var v) ? (T)v : default;
            public bool Has(string nodeId, string path) => Shown.ContainsKey((nodeId ?? "scene") + " " + path);
        }

        /// <summary>A build host over a fixed set of nodes: animation rows with group &lt; 90 exist; eye position p is pattern 100 + p.</summary>
        sealed class Host : ITimelineBuildHost
        {
            public readonly Dictionary<string, NodeKind> Nodes = new Dictionary<string, NodeKind>();
            public bool Built = true;

            public NodeKind? KindOf(string nodeId) => nodeId != null && Nodes.TryGetValue(nodeId, out var k) ? k : (NodeKind?)null;

            public AnimeRef ResolveAnime(string charaKey, JObject key, string arg)
            {
                int g = (int?)key["group"] ?? 0, c = (int?)key["category"] ?? 0, n = (int?)key["no"] ?? 0;
                if (g >= 90) return null;
                return new AnimeRef((string)key["source"] ?? DocDefaults.VanillaSource, g, c, n);
            }

            public int EyePatternAt(string charaKey, int position, string arg)
            {
                if (!Built) throw new BridgeException(BridgeCodes.Unavailable, "the character is not built");
                return 100 + Math.Max(0, Math.Min(5, position));
            }
        }

        const string A = "a0000000000000000000000000000001", B = "b0000000000000000000000000000002", P = "c0000000000000000000000000000003",
            L = "d0000000000000000000000000000004", F = "e0000000000000000000000000000005";

        static Host MakeHost()
        {
            var h = new Host();
            h.Nodes[A] = NodeKind.Chara;
            h.Nodes[B] = NodeKind.Chara;
            h.Nodes[P] = NodeKind.Item;
            h.Nodes[L] = NodeKind.Light;
            h.Nodes[F] = NodeKind.Folder;
            return h;
        }

        static Sink MakeSink()
        {
            var s = new Sink();
            foreach (var kv in MakeHost().Nodes) s.Nodes[kv.Key] = kv.Value;
            return s;
        }

        static TimelineEase[] E(params TimelineEase[] e) => e;
        static float[] T(params float[] t) => t;

        // ---------- TL1 eases ----------

        static void Eases(TestRunner t)
        {
            var us = new[] { 0, 0.25, 0.5, 0.75, 0.999 };
            bool ok = true;
            foreach (double u in us)
            {
                ok &= TimelineEaseCurves.Weight(TimelineEase.Step, u) == 0;
                ok &= TimelineEaseCurves.Weight(TimelineEase.Linear, u) == u;
                ok &= TimelineEaseCurves.Weight(TimelineEase.InOut, u) == u * u * (3 - 2 * u);
                ok &= TimelineEaseCurves.Weight(TimelineEase.In, u) == u * u;
                ok &= TimelineEaseCurves.Weight(TimelineEase.Out, u) == u * (2 - u);
            }
            t.Check(ok, "TL1 the five eases at u in {0, 0.25, 0.5, 0.75, 0.999} equal their formulas exactly");
            t.Check(TimelineEaseCurves.Weight(TimelineEase.InOut, 0.25) == 0.15625 && TimelineEaseCurves.Weight(TimelineEase.InOut, 0.5) == 0.5 &&
                    TimelineEaseCurves.Weight(TimelineEase.InOut, 0.75) == 0.84375, "TL1 inout 0.15625 / 0.5 / 0.84375");
            t.Check(TimelineEaseCurves.Weight(TimelineEase.In, 0.25) == 0.0625 && TimelineEaseCurves.Weight(TimelineEase.In, 0.75) == 0.5625 &&
                    TimelineEaseCurves.Weight(TimelineEase.Out, 0.25) == 0.4375 && TimelineEaseCurves.Weight(TimelineEase.Out, 0.75) == 0.9375,
                "TL1 in 0.0625 / 0.5625, out 0.4375 / 0.9375");
            t.Check(TimelineEaseCurves.Weight(TimelineEase.InOut, 0.999) == 0.999 * 0.999 * (3 - 2 * 0.999) &&
                    TimelineEaseCurves.Weight(TimelineEase.Out, 0.999) == 0.999 * (2 - 0.999), "TL1 near the right key: inout and out exact");
            bool names = true;
            for (int i = 0; i < 5; i++) names &= TimelineEaseCurves.TryParse(TimelineEaseCurves.Name((TimelineEase)i), out var e) && (int)e == i;
            t.Check(names && TimelineEaseCurves.Names.SequenceEqual(new[] { "step", "linear", "inout", "in", "out" }), "TL1 names step / linear / inout / in / out round-trip");
            t.Check(!TimelineEaseCurves.TryParse("Linear", out var d1) && d1 == TimelineEase.InOut && TimelineEaseCurves.Parse("bounce") == TimelineEase.InOut &&
                    TimelineEaseCurves.Parse(null) == TimelineEase.InOut, "TL1 an unknown (or differently cased) name evaluates as inout");
        }

        // ---------- TL2 segments ----------

        static void Segments(TestRunner t)
        {
            var times = new[] { 1.0, 2.0, 4.0 };
            bool F2(double now, int l, int r, double u) => TimelineSegments.Find(times, 3, now, out int l2, out int r2, out double u2) && l2 == l && r2 == r && u2 == u;
            t.Check(F2(0.5, 0, -1, 0), "TL2 before the first key: the first key's value, no right key");
            t.Check(F2(1.0, 0, 1, 0), "TL2 at a key: left = that key, u = 0");
            t.Check(F2(1.5, 0, 1, 0.5) && F2(3.0, 1, 2, 0.5), "TL2 between keys: u = (now - tL) / (tR - tL)");
            t.Check(F2(2.0, 1, 2, 0), "TL2 at the second key: left = it");
            t.Check(F2(4.0, 2, -1, 0) && F2(9.0, 2, -1, 0), "TL2 at and after the last key: the last value holds");
            var one = new[] { 3.0 };
            t.Check(TimelineSegments.Find(one, 1, 0, out int a, out int b, out _) && a == 0 && b == -1 &&
                    TimelineSegments.Find(one, 1, 7, out a, out b, out _) && a == 0 && b == -1, "TL2 one key: its value at every time");
            t.Check(!TimelineSegments.Find(new double[0], 0, 1, out _, out _, out _), "TL2 no key: no value");
            t.Check(TimelineSegments.LeftKey(new[] { 1.0, 1.0, 2.0 }, 3, 1.0) == 1, "TL2 equal times: the last key with t <= now is the left");

            // Through a track: before / at / between / after, values handed exactly.
            var tr = CompiledTrack.Numbers("n", TimelineTrackType.AnimeSpeed, A, true, null, T(1, 2, 4), E(TimelineEase.Linear, TimelineEase.InOut, TimelineEase.Linear), new[] { 10.0, 20.0, 40.0 });
            var s = MakeSink();
            double V(double now)
            {
                s.Shown.Clear();
                var c = Chan(tr);
                tr.Evaluate(now, s, c);
                return s.Get<double>(A, "chara.anime.speed");
            }
            t.Check(V(0) == 10 && V(1) == 10 && V(1.5) == 15 && V(2) == 20 && V(3) == 20 + 20 * 0.5 && V(4) == 40 && V(100) == 40,
                "TL2 a number track: first value before, keys exact, linear and inout between, last value after");
            var empty = CompiledTrack.Numbers("e", TimelineTrackType.AnimeSpeed, A, true, null, T(), E(), new double[0]);
            s.Shown.Clear();
            t.Check(!empty.Evaluate(1, s, Chan(empty)) && s.Shown.Count == 0, "TL2 a track without keys hands no value");
        }

        /// <summary>A channel for a track outside a player (through a one-track timeline).</summary>
        static TimelineChannel Chan(CompiledTrack tr)
        {
            var p = new TimelinePlayer(MakeSink());
            p.SetTimeline(new CompiledTimeline(100, true, false, new[] { tr }, null));
            return tr.Channel;
        }

        // ---------- TL3 values ----------

        static void Values(TestRunner t)
        {
            var v = TimelineVec3.Lerp(new TimelineVec3(0, 0, 0), new TimelineVec3(2, 4, -6), 0.5);
            t.Check(v.Equals(new TimelineVec3(1, 2, -3)) && TimelineVec3.Lerp(new TimelineVec3(0, 0, 0), new TimelineVec3(1, 1, 1), 1.5).Equals(new TimelineVec3(1.5, 1.5, 1.5)), "TL3 vectors lerp per component, unclamped");
            var c = TimelineColor.Lerp(new TimelineColor(1, 1, 1, 1), new TimelineColor(0, 0.5, 1, 0), 0.5);
            t.Check(c.Equals(new TimelineColor(0.5, 0.75, 1, 0.5)), "TL3 colours lerp per component, alpha included");

            // Rotations: 350° → 10° turns 20° on the shorter arc; the middle is yaw 0.
            var qa = TimelineQuat.FromEuler(0, 350, 0);
            var qb = TimelineQuat.FromEuler(0, 10, 0);
            var mid = TimelineQuat.Interpolate(qa, qb, 0.5);
            var end = TimelineQuat.Interpolate(qa, qb, 1);
            t.Check(Math.Abs(TimelineQuat.AngleBetween(qa, qb) - 20) < 1e-9 && TimelineQuat.AngleBetween(mid, TimelineQuat.Identity) < 1e-9 && TimelineQuat.AngleBetween(end, qb) < 1e-9 &&
                    Math.Abs(TimelineQuat.AngleBetween(qa, TimelineQuat.Slerp(qa, qb, 0.25)) - 5) < 1e-9, "TL3 350° → 10° turns 20° (middle at 0°; a slerp quarter 5°)");
            var y180 = TimelineQuat.FromEuler(0, 180, 0);
            var h1 = TimelineQuat.Interpolate(TimelineQuat.Identity, y180, 0.5);
            var h2 = TimelineQuat.Interpolate(TimelineQuat.Identity, y180, 0.5);
            t.Check(h1.Equals(h2) && Math.Abs(TimelineQuat.AngleBetween(TimelineQuat.Identity, h1) - 90) < 1e-9 && Math.Abs(h1.X) < 1e-12 && Math.Abs(h1.Z) < 1e-12 && h1.Y > 0,
                "TL3 a 180° pair is deterministic: +90° about y at the middle");
            // The switch at dot 0.95 (an arc of 2·acos(0.95) ≈ 36.4°): at or above a normalised lerp, below a slerp.
            double yawAt(double dot) => 2 * Math.Acos(dot) * 180 / Math.PI;
            TimelineQuat Nlerp(TimelineQuat x, TimelineQuat y, double w) => new TimelineQuat(x.X + (y.X - x.X) * w, x.Y + (y.Y - x.Y) * w, x.Z + (y.Z - x.Z) * w, x.W + (y.W - x.W) * w).Normalized();
            var lo = TimelineQuat.FromEuler(0, 0, 0);
            var above = TimelineQuat.FromEuler(0, yawAt(0.95024), 0);
            var below = TimelineQuat.FromEuler(0, yawAt(0.94943), 0);
            t.Check(TimelineQuat.Dot(lo, above) >= TimelineQuat.LerpFromDot && TimelineQuat.Dot(lo, below) < TimelineQuat.LerpFromDot, "TL3 the pair 0.95024 / 0.94943 straddles the switch");
            var ia = TimelineQuat.Interpolate(lo, above, 0.3);
            var ib = TimelineQuat.Interpolate(lo, below, 0.3);
            t.Check(ia.Equals(Nlerp(lo, above, 0.3)) && TimelineQuat.AngleBetween(ia, TimelineQuat.Slerp(lo, above, 0.3)) > 1e-3,
                "TL3 dot 0.95024: the normalised lerp (it differs from the slerp)");
            t.Check(ib.Equals(TimelineQuat.Slerp(lo, below, 0.3)) && TimelineQuat.AngleBetween(ib, Nlerp(lo, below, 0.3)) > 1e-3 &&
                    Math.Abs(TimelineQuat.AngleBetween(lo, ib) - 0.3 * yawAt(0.94943)) < 1e-9, "TL3 dot 0.94943: the slerp (a constant turn rate)");
            var flipped = TimelineQuat.Interpolate(lo, above.Negated(), 0.3);
            t.Check(TimelineQuat.AngleBetween(flipped, ia) < 1e-12, "TL3 the switch is taken after the shorter-arc flip (−q blends as q)");
            var unity = TimelineQuat.FromEuler(30, 45, 60);
            // The engine's order: z, then x, then y (q = qy * qx * qz); a reference value of that convention.
            var refq = TimelineQuat.FromEuler(0, 45, 0) * TimelineQuat.FromEuler(30, 0, 0) * TimelineQuat.FromEuler(0, 0, 60);
            t.Check(TimelineQuat.AngleBetween(unity, refq) < 1e-9 && Math.Abs(unity.Length - 1) < 1e-12, "TL3 Euler to quaternion in the engine's z-x-y order, unit length");
            var e = unity.ToEuler();
            t.Check(Math.Abs(e.X - 30) < 1e-9 && Math.Abs(e.Y - 45) < 1e-9 && Math.Abs(e.Z - 60) < 1e-9, "TL3 quaternion back to Euler (30, 45, 60)");
            var tiny = TimelineQuat.Interpolate(qa, TimelineQuat.FromEuler(0, 350 + 1e-8, 0), 0.5);
            t.Check(Math.Abs(tiny.Length - 1) < 1e-12 && TimelineQuat.AngleBetween(tiny, qa) < 1e-7 && TimelineQuat.Slerp(qa, qa, 0.5).Equals(Nlerp(qa, qa, 0.5)),
                "TL3 a vanishing arc blends to a unit quaternion");

            // A rotation track hands the key quaternion exactly at a key and in a step segment.
            var rt = CompiledTrack.Rotations("r", P, null, true, null, T(0, 2), E(TimelineEase.Step, TimelineEase.Linear), new[] { new TimelineVec3(0, 350, 0), new TimelineVec3(0, 10, 0) });
            var s = MakeSink();
            var rc = Chan(rt);
            rt.Evaluate(1, s, rc);
            t.Check(s.Get<TimelineQuat>(P, "transform.rot").Equals(TimelineQuat.FromEuler(0, 350, 0)), "TL3 a step segment hands the left key's quaternion unchanged");
            var bt = CompiledTrack.Rotations("b", A, "cf_J_Neck", true, null, T(0, 2), E(TimelineEase.Linear, TimelineEase.Linear), new[] { new TimelineVec3(0, 350, 0), new TimelineVec3(0, 10, 0) });
            var bc = Chan(bt);
            bt.Evaluate(1, s, bc);
            t.Check(bc.Path == "chara.fk.bones[\"cf_J_Neck\"]" && TimelineQuat.AngleBetween(s.Get<TimelineQuat>(A, bc.Path), TimelineQuat.Identity) < 1e-9, "TL3 an FK bone track: channel chara.fk.bones[\"cf_J_Neck\"], slerped");

            // Camera: target / dist / fov linear, rotation slerp.
            var ca = new TimelineCameraValue(new TimelineVec3(0, 10, 0), TimelineQuat.FromEuler(0, 350, 0), 30, 30);
            var cb = new TimelineCameraValue(new TimelineVec3(2, 12, -2), TimelineQuat.FromEuler(0, 10, 0), 20, 40);
            var cm = TimelineCameraValue.Blend(ca, cb, 0.5);
            t.Check(cm.Target.Equals(new TimelineVec3(1, 11, -1)) && cm.Dist == 25 && cm.Fov == 35 && TimelineQuat.AngleBetween(cm.Rotation, TimelineQuat.Identity) < 1e-9,
                "TL3 camera: target, dist, fov linear; rotation on the shorter arc");
            var cj = JObject.Parse(cm.ToJson());
            t.Check(cj["target"] is JArray && (double)cj["dist"] == 25 && (double)cj["fov"] == 35 && cj["rot"] is JArray, "TL3 camera JSON form {target, rot, dist, fov}");

            // Depth of field.
            var node = DofFocus.OnNode(A);
            var pt = DofFocus.AtPoint(new TimelineVec3(1, 2, 3));
            var on = new TimelineDofKey(true, node, 0.5, 0.8, 2);
            var off = new TimelineDofKey(false, node, 0.2, 0.6, double.NaN);
            var f1 = TimelineDofValue.Blend(on, off, 0.5);
            t.Check(f1.Enabled && f1.Aperture == 0.4 && f1.FocalSize == 0.5 && f1.Blur == 2 && f1.FocusTo == null,
                "TL3 dof on → off: enabled while blending, aperture fades to 0, the off side keeps the on side's size, the one blur set");
            t.Check(TimelineDofValue.Blend(on, off, 0).Enabled && !TimelineDofValue.Blend(on, off, 1).Enabled, "TL3 dof enabled = a.on at w 0, b.on at w 1");
            var offA = new TimelineDofKey(false, pt, 0.3, 0.7, 4);
            var onB = new TimelineDofKey(true, node, 0.6, 0.9, 2);
            var f2 = TimelineDofValue.Blend(offA, onB, 0.25);
            t.Check(f2.Enabled && f2.Aperture == 0.9 * 0.25 && f2.FocalSize == 0.6 && f2.Blur == 4 + (2 - 4) * 0.25, "TL3 dof off → on: aperture from 0, size of the on side, blur lerped");
            t.Check(f2.Focus.Equals(pt) && f2.FocusTo.Equals(node) && f2.FocusBlend == 0.25, "TL3 dof different foci: focus, focusTo and focusBlend = w");
            var nb = TimelineDofValue.Blend(off, new TimelineDofKey(false, DofFocus.OnNode(A), 0.1, 0.1, double.NaN), 0.5);
            t.Check(!nb.HasBlur && nb.FocusTo == null && !nb.Enabled, "TL3 dof: no blur when neither key sets one; equal foci do not blend; both off stays off");
            var dj = JObject.Parse(f2.ToJson());
            t.Check((bool)dj["enabled"] && dj["focus"]["point"] is JArray && (string)dj["focusTo"]["node"] == A && (double)dj["focusBlend"] == 0.25 && dj["blur"] != null,
                "TL3 dof JSON form with focusTo / focusBlend");
            t.Check(DofFocus.Pivot.ToJson() == "null" && DofFocus.AtDistance(12).ToJson() == "{\"dist\":12.0}" &&
                    DofFocus.OnNode(A, "cf_J_Head", new TimelineVec3(0, 0.5, 0)).ToJson() == "{\"node\":\"" + A + "\",\"bone\":\"cf_J_Head\",\"offset\":[0.0,0.5,0.0]}",
                "TL3 focus forms as JSON (pivot null, dist, node with bone and offset)");
            var dt = CompiledTrack.Dofs("d", true, null, T(0, 2), E(TimelineEase.Step, TimelineEase.Linear), new[] { on, off });
            var dc = Chan(dt);
            dt.Evaluate(1, s, dc);
            var stepDof = s.Get<TimelineDofValue>(null, "effects.depthOfField");
            t.Check(stepDof.Enabled && stepDof.Aperture == 0.8 && stepDof.FocusTo == null, "TL3 dof step segment: the left key's value");

            // Step-only types ignore their keys' ease.
            var at = CompiledTrack.Animes("an", A, true, null, T(0, 2), E(TimelineEase.Linear, TimelineEase.Linear),
                new[] { new TimelineAnimeRow("illusion.hs2", 19, 0, 0), new TimelineAnimeRow("illusion.hs2", 11, 5, 0) });
            var ac = Chan(at);
            at.Evaluate(1.9, s, ac);
            var it = CompiledTrack.Integers("ey", TimelineTrackType.Eyes, A, true, null, T(0, 2), E(TimelineEase.Linear, TimelineEase.Linear), new[] { 1, 5 });
            var ic = Chan(it);
            it.Evaluate(1.9, s, ic);
            t.Check(s.Get<TimelineAnimeRow>(A, "chara.anime.ref").Group == 19 && s.Get<int>(A, "chara.face.eyes") == 1, "TL3 animation rows and patterns step whatever the ease");
            it.Evaluate(2, s, ic);
            t.Check(s.Get<int>(A, "chara.face.eyes") == 5, "TL3 a pattern changes at its key's time");
            var colT = CompiledTrack.Colors("c", L, null, true, null, T(0, 1), E(TimelineEase.Linear, TimelineEase.Linear), new[] { new TimelineColor(1, 1, 1, 1), new TimelineColor(0, 0, 0, 0) });
            var cc = Chan(colT);
            colT.Evaluate(0.5, s, cc);
            var item = CompiledTrack.Colors("c2", P, 2, true, null, T(0), E(TimelineEase.Linear), new[] { new TimelineColor(0.25, 0.5, 0.75, 1) });
            var icc = Chan(item);
            t.Check(cc.Path == "light.color" && s.Get<TimelineColor>(L, "light.color").Equals(new TimelineColor(0.5, 0.5, 0.5, 0.5)) && icc.Path == "item.colors[2].main" && icc.ModelPath == "item.colors[2]",
                "TL3 colour channels: a light's colour, an item's slot (suspended by edits of the whole slot)");
        }

        // ---------- TL4 crossings ----------

        static List<int> Adv(double time, double dt, double duration, bool loop, bool incl, double[] ev, out double n, out bool ended, out long skipped)
        {
            var fired = new List<int>();
            n = TimelineCrossings.Advance(time, dt, duration, loop, incl, ev, ev.Length, fired, out ended, out skipped);
            return fired;
        }

        static void Crossings(TestRunner t)
        {
            var ev = new[] { 0.0, 1.0, 2.5, 5.0 };
            var f = Adv(0, 0.5, 5, true, true, ev, out double n, out bool ended, out long sk);
            t.Check(f.SequenceEqual(new[] { 0 }) && n == 0.5 && !ended, "TL4 play from 0: the first advance runs the event at 0");
            f = Adv(0, 0.5, 5, true, false, ev, out n, out _, out _);
            t.Check(f.Count == 0 && n == 0.5, "TL4 a resume at 0 excludes its start");
            f = Adv(0.5, 0.5, 5, true, false, ev, out n, out _, out _);
            t.Check(f.SequenceEqual(new[] { 1 }), "TL4 p < t <= n: the event at the advance's end runs");
            f = Adv(1, 1, 5, true, false, ev, out n, out _, out _);
            t.Check(f.Count == 0 && n == 2, "TL4 the event at the start of an advance does not run again");
            f = Adv(4.5, 1.0, 5, true, false, ev, out n, out ended, out _);
            t.Check(f.SequenceEqual(new[] { 3, 0 }) && n == 0.5 && !ended, "TL4 a wrap runs the event at the duration, then the one at 0");
            f = Adv(2, 8, 5, true, false, ev, out n, out _, out sk);
            t.Check(f.SequenceEqual(new[] { 2, 3, 0, 1, 2, 3, 0 }) && n == 0 && sk == 0, "TL4 two wraps in one advance: (p, d], a full pass [0, d], then [0, n] (n = 0 runs the event at 0)");
            var one = new[] { 0.5 };
            f = Adv(0, 150.25, 1, true, false, one, out n, out _, out sk);
            t.Check(f.Count == 100 && sk == 50 && n == 0.25, "TL4 150 passes in one advance: events of 100 passes, 50 skipped, time 0.25");
            f = Adv(4, 2, 5, false, false, ev, out n, out ended, out _);
            t.Check(f.SequenceEqual(new[] { 3 }) && ended && n == 5, "TL4 not looping: the event at the duration runs once and the playback ends");
            f = Adv(2, 0, 5, true, false, ev, out n, out ended, out _);
            t.Check(f.Count == 0 && n == 2 && !ended, "TL4 dt 0 (the global pause): no advance, nothing runs");
            f = Adv(2, double.NaN, 5, true, true, ev, out n, out _, out _);
            t.Check(f.Count == 0 && n == 2, "TL4 dt NaN: no advance");
            f = Adv(2.5, 1, 5, true, true, ev, out n, out _, out _);
            t.Check(f.SequenceEqual(new[] { 2 }), "TL4 a play from s runs the event at s");
            var two = new[] { 1.0, 1.0, 3.0 };
            f = Adv(0.5, 1, 5, true, false, two, out n, out _, out _);
            t.Check(f.SequenceEqual(new[] { 0, 1 }), "TL4 two events at one time run in list order");

            // Through the player: run requests, the one-step rule, seek and pause run none.
            var s = MakeSink();
            var p = new TimelinePlayer(s);
            var events = new[]
            {
                Ev("e0", 0f, "{\"cmd\":\"obj.transform\",\"key\":\"" + P + "\",\"pos\":[0,0,0]}"),
                Ev("e1", 1f, "{\"cmd\":\"chara.face\",\"key\":\"" + A + "\",\"mouth\":1,\"undo\":true}"),
                Ev("e2", 2f, "{\"cmd\":\"timeline.set\",\"duration\":3}"),
                Ev("e3", 2f, "{\"cmd\":\"x.y\",\"id\":\"mine\"}"),
                Ev("e4", 3f, "{\"cmd\":\"timeline.pause\"}"),
                new TimelineEventInput { Id = "e5", T = 3.5f, Enabled = false, Do = JObject.Parse("{\"cmd\":\"chara.face\",\"key\":\"" + A + "\"}") },
            };
            p.SetTimeline(new CompiledTimeline(4, true, false, null, events));
            t.Check(p.Timeline.EventCount == 6 && p.Timeline.Runs.Count == 3, "TL4 runs: disabled, timeline-editing and id-carrying events never run");
            p.Play();
            p.Tick(0.5);
            t.Check(s.Fired.Count == 1 && s.Fired[0].Id == "e0", "TL4 player: play from 0 runs the event at 0 on the first tick");
            var req = JObject.Parse(s.Fired[0].RequestJson);
            t.Check((string)req["cmd"] == "obj.transform" && req["undo"] != null && !(bool)req["undo"] && (string)req["id"] == "timeline:e0",
                "TL4 run request: undo false added, id timeline:<event id>");
            p.Pause();
            p.Tick(1);
            p.Seek(0.9);
            p.Tick(0);
            t.Check(s.Fired.Count == 1 && p.State == TimelinePlayState.Paused && p.Time == 0.9, "TL4 pause and seek run nothing");
            p.Play();
            p.Tick(0.1);
            t.Check(s.Fired.Count == 2 && (bool)JObject.Parse(s.Fired[1].RequestJson)["undo"] && p.EventsFired == 2, "TL4 resume from 0.9 runs the event at 1.0 (its own undo: true kept)");
            p.Tick(2.0);
            t.Check(s.Fired.Count == 3 && s.Fired[2].Id == "e4", "TL4 the timeline.pause event is allowed and runs");
            for (int i = 0; i < 11; i++) p.Tick(0.1);
            t.Check(Math.Abs(p.Time - 0.1) < 1e-9 && s.Fired.Count == 4 && s.Fired[3].Id == "e0", "TL4 steps advance the time and the wrap runs the event at 0 again");
            var looped = new TimelinePlayer(MakeSink());
            looped.SetTimeline(new CompiledTimeline(1, true, false, null, new[] { Ev("x", 0.5f, "{\"cmd\":\"a.b\"}") }));
            looped.Play();
            looped.Tick(150.25);
            t.Check(looped.SkippedPasses == 50 && looped.Warnings.Count == 1 && looped.EventsFired == 100, "TL4 player: the cap's warning and the skipped passes");
        }

        static TimelineEventInput Ev(string id, float tt, string json) => new TimelineEventInput { Id = id, T = tt, Do = JObject.Parse(json) };

        // ---------- TL5 the state machine ----------

        static CompiledTimeline Basic(double duration = 4, bool loop = true, bool autoplay = false, IEnumerable<TimelineEventInput> events = null) =>
            new CompiledTimeline(duration, loop, autoplay, new[]
            {
                CompiledTrack.Vectors("pos", TimelineTrackType.Pos, P, true, null, T(0, 4), E(TimelineEase.Linear, TimelineEase.Linear), new[] { new TimelineVec3(0, 0, 0), new TimelineVec3(4, 0, 0) }),
                CompiledTrack.Cameras("cam", true, null, T(0), E(TimelineEase.InOut), new[] { new TimelineVec3(0, 10, 0) }, new[] { new TimelineVec3(0, 0, 0) }, new[] { 30.0 }, new[] { 30.0 }),
            }, events);

        static void StateMachine(TestRunner t)
        {
            var s = MakeSink();
            var p = new TimelinePlayer(s);
            t.Check(p.State == TimelinePlayState.None && !p.Play() && !p.Seek(1) && p.State == TimelinePlayState.None, "TL5 no Timeline: state none, play and seek do nothing");
            p.SetTimeline(Basic());
            t.Check(p.State == TimelinePlayState.Stopped && p.Time == 0 && s.Sets == 0, "TL5 a Timeline: stopped at 0, nothing shown");
            p.Tick(0.1);
            t.Check(s.Sets == 0 && p.Time == 0, "TL5 stopped: a tick does nothing");
            p.Play();
            p.Tick(1);
            t.Check(p.State == TimelinePlayState.Playing && p.Time == 1 && s.Get<TimelineVec3>(P, "transform.pos").Equals(new TimelineVec3(1, 0, 0)) && s.Has(null, "camera") && p.ValuesShown == 2,
                "TL5 playing: the values are set every tick");
            p.Play();
            p.Tick(0);
            t.Check(p.State == TimelinePlayState.Playing && p.Time == 1, "TL5 play while playing changes nothing (no pause toggle)");
            p.Play(3);
            t.Check(p.Time == 3 && p.State == TimelinePlayState.Playing, "TL5 play {from} while playing restarts there");
            p.Tick(0);
            p.Pause();
            int sets = s.Sets;
            p.Tick(1);
            t.Check(p.State == TimelinePlayState.Paused && p.Time == 3 && s.Sets == sets, "TL5 paused: time fixed, nothing set again");
            t.Check(!p.Pause() && p.State == TimelinePlayState.Paused, "TL5 pause while paused: nothing");
            p.Stop();
            t.Check(p.State == TimelinePlayState.Stopped && p.Time == 0 && s.Shown.Count == 0 && s.ReleaseAlls == 1 && p.ValuesShown == 0, "TL5 stop: every value released, time 0");
            p.Seek(2);
            t.Check(p.State == TimelinePlayState.Paused && p.Time == 2 && s.Get<TimelineVec3>(P, "transform.pos").Equals(new TimelineVec3(2, 0, 0)), "TL5 seek from stopped: paused there, the values set in the call");
            p.Seek(-5);
            t.Check(p.Time == 0, "TL5 seek below 0 clamps to 0");
            p.Seek(99);
            t.Check(p.Time == 4, "TL5 seek past the duration clamps to it");
            p.Play();
            p.Tick(0.5);
            t.Check(p.Time == 0.5, "TL5 play from paused at the duration (looping) wraps on");

            // A document change: the state is kept, the time clamped, removed channels released, values set at the next tick.
            p.Play(3.5);
            p.Tick(0);
            int rel = s.Releases;
            var shorter = new CompiledTimeline(2, true, false, new[]
            {
                CompiledTrack.Vectors("pos", TimelineTrackType.Pos, P, true, null, T(0, 2), E(TimelineEase.Linear, TimelineEase.Linear), new[] { new TimelineVec3(0, 0, 0), new TimelineVec3(-2, 0, 0) }),
            }, null);
            p.SetTimeline(shorter);
            t.Check(p.State == TimelinePlayState.Playing && p.Time == 2 && p.Dirty && s.Releases == rel + 1 && !s.Has(null, "camera"),
                "TL5 a document change while playing: state kept, time clamped, the removed camera track released");
            p.Pause();
            p.SetTimeline(new CompiledTimeline(2, true, false, new[]
            {
                CompiledTrack.Vectors("pos", TimelineTrackType.Pos, P, true, null, T(0, 2), E(TimelineEase.Linear, TimelineEase.Linear), new[] { new TimelineVec3(0, 0, 0), new TimelineVec3(-8, 0, 0) }),
            }, null));
            p.Tick(0);
            t.Check(p.State == TimelinePlayState.Paused && s.Get<TimelineVec3>(P, "transform.pos").Equals(new TimelineVec3(-8, 0, 0)), "TL5 a document change while paused: evaluated again at the next tick");
            p.SetTimeline(null);
            t.Check(p.State == TimelinePlayState.None && s.Shown.Count == 0 && p.Time == 0, "TL5 the Timeline removed: none, every value released");

            // Not looping: ends at the duration with the last values held; play again starts at 0.
            var q = new TimelinePlayer(s);
            q.SetTimeline(Basic(4, false));
            q.Play();
            q.Tick(3);
            q.Tick(3);
            t.Check(q.State == TimelinePlayState.Ended && q.Time == 4 && s.Get<TimelineVec3>(P, "transform.pos").Equals(new TimelineVec3(4, 0, 0)), "TL5 not looping: ended at the duration, last values held");
            q.Play();
            t.Check(q.State == TimelinePlayState.Playing && q.Time == 0, "TL5 play from ended starts at 0");

            // Autoplay after a load.
            var ap = new TimelinePlayer(MakeSink());
            ap.Reset(Basic(4, true, false));
            t.Check(!ap.Autoplay() && ap.State == TimelinePlayState.Stopped, "TL5 no autoplay: stays stopped after a load");
            var aps = MakeSink();
            ap = new TimelinePlayer(aps);
            ap.Reset(Basic(4, true, true, new[] { Ev("z", 0f, "{\"cmd\":\"a.b\"}") }));
            t.Check(ap.Autoplay() && ap.State == TimelinePlayState.Playing, "TL5 autoplay: plays from 0 after a load");
            ap.Tick(0.1);
            t.Check(aps.Fired.Count == 1, "TL5 autoplay's first advance runs the event at 0");
            ap.Reset(null);
            t.Check(ap.State == TimelinePlayState.None && aps.ReleaseAlls == 1, "TL5 a scene clear: none, the sink drops what it held");

            // Targets: a missing node holds no value; it shows again when the node returns; a kind the type does not take.
            var ts = MakeSink();
            ts.Nodes.Remove(P);
            var tp = new TimelinePlayer(ts);
            tp.SetTimeline(new CompiledTimeline(4, true, false, new[]
            {
                CompiledTrack.Vectors("pos", TimelineTrackType.Pos, P, true, null, T(0), E(TimelineEase.Linear), new[] { new TimelineVec3(1, 2, 3) }),
                CompiledTrack.Rotations("bone", P, "cf_J_Neck", true, null, T(0), E(TimelineEase.Linear), new[] { new TimelineVec3(0, 0, 0) }),
                CompiledTrack.Numbers("off", TimelineTrackType.EyesOpen, A, false, null, T(0), E(TimelineEase.Linear), new[] { 0.5 }),
                CompiledTrack.Vectors("dup", TimelineTrackType.Pos, P, true, null, T(0), E(TimelineEase.Linear), new[] { new TimelineVec3(9, 9, 9) }),
                CompiledTrack.Unknown("unk", "blush", A, null, null, true, null),
            }, null));
            var trs = tp.Timeline.Tracks;
            t.Check(trs[0].Active && trs[1].Active && trs[2].State == TimelineTrackState.Disabled && trs[3].State == TimelineTrackState.DuplicateChannel &&
                    trs[4].State == TimelineTrackState.UnknownType, "TL5 track states: active, disabled, a second track of a channel, an unknown type");
            tp.Seek(1);
            t.Check(ts.Shown.Count == 0 && tp.CountActiveTracks() == 0, "TL5 a missing node: no value");
            ts.Nodes[P] = NodeKind.Item;
            tp.MarkDirty();
            tp.Tick(0);
            t.Check(ts.Get<TimelineVec3>(P, "transform.pos").Equals(new TimelineVec3(1, 2, 3)) && !ts.Has(P, "chara.fk.bones[\"cf_J_Neck\"]") && ts.Shown.Count == 1 && tp.CountActiveTracks() == 1,
                "TL5 the node back: its position shows; a bone track on an item shows nothing; disabled and second tracks show nothing");
            ts.Nodes.Remove(P);
            tp.MarkDirty();
            tp.Tick(0);
            t.Check(ts.Shown.Count == 0 && ts.Releases == 1, "TL5 the node gone again: its value released");
        }

        // ---------- TL5 the authoring suspension ----------

        static SceneChangeBatch Changed(ChangeOrigin origin, string nodeId, string path) =>
            new SceneChangeBatch(origin, CommandSource.User, new[] { new SceneChange(nodeId == null ? SceneChangeKind.SceneChanged : SceneChangeKind.NodeChanged, nodeId, path) });

        static void Suspension(TestRunner t)
        {
            var s = MakeSink();
            var p = new TimelinePlayer(s);
            p.SetTimeline(Basic());
            p.Play();
            p.Tick(1);
            t.Check(p.OnModelChanged(Changed(ChangeOrigin.Command, P, "transform.pos")) == 0 && s.Has(P, "transform.pos"), "TL5 playing: an edit suspends nothing");
            p.Pause();
            t.Check(p.OnModelChanged(Changed(ChangeOrigin.Capture, P, "transform.pos")) == 0, "TL5 a save capture suspends nothing");
            t.Check(p.OnModelChanged(Changed(ChangeOrigin.Command, P, "transform.rot")) == 0 && s.Has(P, "transform.pos"), "TL5 an edit of another channel suspends nothing");
            t.Check(p.OnModelChanged(Changed(ChangeOrigin.Preview, P, "transform")) == 1 && !s.Has(P, "transform.pos") && p.SuspendedCount == 1,
                "TL5 paused: an edit of the channel (a parent path too) suspends it and clears its value");
            t.Check(p.OnModelChanged(Changed(ChangeOrigin.Undo, null, "camera.current")) == 1 && !s.Has(null, "camera"), "TL5 a camera move while paused suspends the camera track");
            p.MarkDirty();
            p.Tick(0);
            t.Check(!s.Has(P, "transform.pos") && !s.Has(null, "camera"), "TL5 suspended channels are left alone by a re-evaluation");
            // Same keys: still suspended; changed keys: released from the suspension.
            p.SetTimeline(Basic());
            p.Tick(0);
            t.Check(p.SuspendedCount == 2 && !s.Has(P, "transform.pos"), "TL5 a document change with the same keys keeps the suspension");
            p.SetTimeline(new CompiledTimeline(4, true, false, new[]
            {
                CompiledTrack.Vectors("pos", TimelineTrackType.Pos, P, true, null, T(0, 4), E(TimelineEase.Linear, TimelineEase.Linear), new[] { new TimelineVec3(0, 0, 0), new TimelineVec3(8, 0, 0) }),
                CompiledTrack.Cameras("cam", true, null, T(0), E(TimelineEase.InOut), new[] { new TimelineVec3(0, 10, 0) }, new[] { new TimelineVec3(0, 0, 0) }, new[] { 30.0 }, new[] { 30.0 }),
            }, null));
            p.Tick(0);
            t.Check(p.SuspendedCount == 1 && s.Get<TimelineVec3>(P, "transform.pos").Equals(new TimelineVec3(2, 0, 0)) && !s.Has(null, "camera"),
                "TL5 the track's keys changed: its suspension ends; the other stays");
            p.Seek(2);
            t.Check(p.SuspendedCount == 0 && s.Has(null, "camera"), "TL5 a seek ends every suspension");
            p.OnModelChanged(Changed(ChangeOrigin.Command, P, "transform.pos"));
            p.Play();
            p.Tick(0);
            t.Check(p.SuspendedCount == 0 && s.Has(P, "transform.pos"), "TL5 play ends the suspension");
            int dirtyBefore = s.Sets;
            p.Pause();
            p.OnModelChanged(new SceneChangeBatch(ChangeOrigin.Undo, CommandSource.User, new[] { new SceneChange(SceneChangeKind.NodeAdded, P) }));
            t.Check(p.Dirty, "TL5 a structural change marks the values dirty");
            p.Stop();
            t.Check(p.SuspendedCount == 0 && s.Shown.Count == 0, "TL5 stop ends every suspension");
        }

        // ---------- TL6 the build input ----------

        static List<TimelineTrack> Build(string json, float duration = 10, Host host = null, List<string> warnings = null, bool strict = false, IReadOnlyList<TimelineTrack> existing = null) =>
            TimelineBuildInput.ParseTracks(JToken.Parse(json), duration, host ?? MakeHost(), new CountingIdSource(), strict, warnings ?? new List<string>(), existing);

        static BridgeException Refused(TestRunner t, string json, string code, string argPart, string what, Host host = null, bool strict = false, float duration = 10)
        {
            var e = t.Throws<BridgeException>(() => Build(json, duration, host, null, strict), what);
            if (e != null)
                t.Check(e.Code == code && (argPart == null || (e.Error.Detail?.Arg ?? "").Contains(argPart) || e.Message.Contains(argPart)),
                    what + ": " + code + (argPart != null ? " naming " + argPart : "") + " (got " + e.Code + " " + e.Error.Detail?.Arg + ": " + e.Message + ")");
            return e;
        }

        static void BuildParser(TestRunner t)
        {
            // Camera carry-over.
            var tr = Build("[{\"type\":\"camera\",\"keys\":[{\"t\":0,\"target\":[1,2,3],\"rot\":[4,5,6],\"dist\":7,\"fov\":30,\"ease\":\"linear\"},{\"t\":1,\"target\":[0,0,0]},{\"t\":2,\"fov\":40}]}]");
            var k = tr[0].Keys;
            t.Check(tr.Count == 1 && tr[0].Key == null && tr[0].Id != null && tr[0].Id.Length == 32 && k.Count == 3, "TL6 a camera track: no node, a new id, 3 keys");
            t.Check(k[1].Camera.Target.Equals(new Vec3(0, 0, 0)) && k[1].Camera.Rot.Equals(new Vec3(4, 5, 6)) && k[1].Camera.Dist == 7 && k[1].Camera.Fov == 30 &&
                    k[2].Camera.Target.Equals(new Vec3(0, 0, 0)) && k[2].Camera.Fov == 40 && k[2].Camera.Dist == 7, "TL6 camera members carry over from the previous key");
            t.Check(k[0].Ease == "linear" && k[1].Ease == "inout" && k[2].Ease == "inout", "TL6 a key's ease overrides; the track's default is inout");
            Refused(t, "[{\"type\":\"camera\",\"keys\":[{\"t\":0,\"target\":[1,2,3],\"rot\":[4,5,6],\"dist\":7}]}]", BridgeCodes.BadArgs, "fov", "TL6 the first camera key needs all four");

            // Dof: first-key defaults, carry-over, sharp / full.
            tr = Build("[{\"type\":\"dof\",\"keys\":[{\"t\":0},{\"t\":1,\"focus\":{\"point\":[1,2,3]}},{\"t\":2,\"sharp\":0.06,\"full\":2,\"on\":false,\"blur\":null},{\"t\":3,\"size\":5,\"aperture\":0.5},{\"t\":4,\"full\":5}]}]");
            k = tr[0].Keys;
            TimelineBuildInput.FromRange(0.1, 3, out double s0, out double a0);
            double u0 = 1 / 1.1 - 1 / 3.0, aa0 = 1 / u0;
            t.Check(s0 == Math.Min(2, aa0 * 0.1 / 1.1) && a0 == Math.Min(0.99, aa0 / (1 + aa0)) && Math.Abs(s0 - 0.15789473684210525) < 1e-12 && Math.Abs(a0 - 0.63461538461538458) < 1e-12,
                "TL6 sharp 0.1 / full 3 → size 0.157895, aperture 0.634615");
            var d0 = k[0].Dof;
            t.Check(d0.On && d0.Focus == null && d0.Size == (float)s0 && d0.Aperture == (float)a0 && d0.Blur == 3f, "TL6 dof first-key defaults: on, the pivot, sharp 0.1, full 3, blur 3");
            var d1 = k[1].Dof;
            t.Check(d1.On && d1.Focus != null && d1.Focus.Kind == TimelineFocusKind.Point && d1.Size == d0.Size && d1.Aperture == d0.Aperture && d1.Blur == 3f,
                "TL6 dof members carry over (on, size, aperture, blur)");
            var d2 = k[2].Dof;
            double u2 = 1 / 1.06 - 1 / 2.0, a2 = 1 / u2;
            t.Check(!d2.On && d2.Blur == null && d2.Focus.Kind == TimelineFocusKind.Point && d2.Size == (float)Math.Min(2, a2 * 0.06 / 1.06) && d2.Aperture == (float)(a2 / (1 + a2)),
                "TL6 sharp 0.06 / full 2 → size and aperture; blur null = the renderer's; the focus carries");
            var d3 = k[3].Dof;
            t.Check(d3.Size == 2f && d3.Aperture == 0.5f && !d3.On, "TL6 size clamped to 2");
            var d4 = k[4].Dof;
            double u4 = 1 / 1.06 - 1 / 5.0, a4 = 1 / u4;
            t.Check(d4.Size == (float)Math.Min(2, a4 * 0.06 / 1.06) && d4.Aperture == (float)(a4 / (1 + a4)), "TL6 full alone: sharp carries from the last range key");
            Refused(t, "[{\"type\":\"dof\",\"keys\":[{\"t\":0,\"sharp\":0.1,\"full\":1.1}]}]", BridgeCodes.BadArgs, "full", "TL6 full not clearly larger than 1 + sharp is refused");
            Refused(t, "[{\"type\":\"dof\",\"keys\":[{\"t\":0,\"sharp\":0.1,\"size\":1}]}]", BridgeCodes.BadArgs, null, "TL6 size / aperture with sharp / full in one key is refused");
            tr = Build("[{\"type\":\"dof\",\"keys\":[{\"t\":0,\"aperture\":3,\"focus\":\"" + A + "\"},{\"t\":1,\"focus\":{\"key\":\"" + B + "\",\"bone\":\"cf_J_Head\",\"offset\":[0,0.1,0]}},{\"t\":2,\"focus\":{\"dist\":12}},{\"t\":3,\"focus\":\"target\"},{\"t\":4,\"focus\":null}]}]");
            k = tr[0].Keys;
            t.Check(k[0].Dof.Aperture == 0.99f && k[0].Dof.Focus.Kind == TimelineFocusKind.Node && k[0].Dof.Focus.Node == A &&
                    k[1].Dof.Focus.Node == B && k[1].Dof.Focus.Bone == "cf_J_Head" && k[1].Dof.Focus.Offset.Value.Equals(new Vec3(0, 0.1f, 0)) &&
                    k[2].Dof.Focus.Kind == TimelineFocusKind.Dist && k[2].Dof.Focus.Dist == 12 && k[3].Dof.Focus == null && k[4].Dof.Focus == null,
                "TL6 focus forms: a node id, {key, bone, offset}, {dist}, \"target\" and null (the pivot); aperture clamped to 0.99");
            var w = new List<string>();
            Build("[{\"type\":\"dof\",\"keys\":[{\"t\":0,\"near\":false}]}]", warnings: w);
            t.Check(w.Count == 1 && w[0].Contains("near"), "TL6 near: false is a warning");
            Refused(t, "[{\"type\":\"dof\",\"keys\":[{\"t\":0,\"focus\":\"" + new string('f', 32) + "\"}]}]", BridgeCodes.NotFound, "focus", "TL6 a focus node that is not in the scene");

            // Track ease default and key override; step-only types store step.
            tr = Build("[{\"type\":\"pos\",\"key\":\"" + P + "\",\"ease\":\"linear\",\"keys\":[{\"t\":0,\"v\":[1,2,3]},{\"t\":1,\"v\":[4,5,6],\"ease\":\"out\"},{\"t\":0.5,\"v\":[0,0,0],\"note\":\"x\"}]}," +
                       "{\"type\":\"anime\",\"key\":\"" + A + "\",\"ease\":\"linear\",\"keys\":[{\"t\":0,\"group\":19,\"category\":0,\"no\":0,\"ease\":\"inout\"}]}," +
                       "{\"type\":\"eyes\",\"key\":\"" + A + "\",\"keys\":[{\"t\":0,\"v\":2,\"ease\":\"out\"}]}," +
                       "{\"type\":\"eyes\",\"key\":\"" + B + "\",\"raw\":true,\"keys\":[{\"t\":0,\"v\":7}]}," +
                       "{\"type\":\"mouth\",\"key\":\"" + A + "\",\"keys\":[{\"t\":0,\"v\":3}]}]", warnings: w = new List<string>());
            t.Check(tr[0].Keys.Select(x => x.T).SequenceEqual(new[] { 0f, 0.5f, 1f }) && tr[0].Keys[0].Ease == "linear" && tr[0].Keys[1].Ease == "linear" && tr[0].Keys[2].Ease == "out" &&
                    tr[0].Keys[2].Vector.Equals(new Vec3(4, 5, 6)), "TL6 keys sorted by time; the track's ease is the keys' default, a key's own wins");
            t.Check(w.Count == 0, "TL6 notes of tracks and keys are dropped without a warning");
            t.Check(tr[1].Keys[0].Ease == "step" && tr[1].Keys[0].Anime.Group == 19 && tr[1].Keys[0].Anime.Source == DocDefaults.VanillaSource &&
                    tr[2].Keys[0].Ease == "step" && tr[4].Keys[0].Ease == "step", "TL6 anime, eyes and mouth store step");
            t.Check(tr[2].Keys[0].Integer == 102 && tr[3].Keys[0].Integer == 7 && tr[4].Keys[0].Integer == 3, "TL6 eyes: a list position resolved by the host, raw: the pattern id; mouth: the pattern id");
            tr = Build("[{\"type\":\"color\",\"key\":\"" + P + "\",\"slot\":2,\"keys\":[{\"t\":0,\"v\":[1,0.5,0.25]},{\"t\":1,\"v\":[0,0,0,0.5]}]},{\"type\":\"color\",\"key\":\"" + L + "\",\"keys\":[{\"t\":0,\"v\":[1,1,1]}]}," +
                       "{\"type\":\"rot\",\"key\":\"" + A + "\",\"bone\":\"cf_J_Neck\",\"keys\":[{\"t\":0,\"v\":[0,350,0]}]},{\"type\":\"rot\",\"key\":\"" + A + "\",\"keys\":[{\"t\":0,\"v\":[0,10,0]}]}," +
                       "{\"type\":\"pos\",\"key\":\"" + F + "\",\"enabled\":false,\"label\":\"Folder\",\"keys\":[]}]");
            t.Check(tr[0].Slot == 2 && tr[0].Keys[0].Color.Equals(new Color4(1, 0.5f, 0.25f, 1)) && tr[0].Keys[1].Color.A == 0.5f && tr[1].Slot == null &&
                    tr[2].Bone == "cf_J_Neck" && tr[3].Bone == null && !tr[4].Enabled && tr[4].Label == "Folder" && tr[4].Keys.Count == 0,
                "TL6 colours (alpha 1 when 3 numbers), a light's colour, a bone and a node rotation of one character, a folder's position, enabled, label");

            // The refusals.
            var h = MakeHost();
            Refused(t, "[{\"type\":\"leftHand\",\"key\":\"" + A + "\",\"keys\":[]}]", BridgeCodes.BadArgs, "tracks[0].type", "TL6 an HS2 type this version does not take");
            Refused(t, "[{\"type\":\"spin\",\"keys\":[]}]", BridgeCodes.BadArgs, "tracks[0].type", "TL6 an unknown type");
            Refused(t, "[{\"type\":\"pos\",\"key\":\"" + new string('0', 32) + "\",\"keys\":[]}]", BridgeCodes.NotFound, "tracks[0].key", "TL6 a node that is not in the scene");
            Refused(t, "[{\"type\":\"rot\",\"key\":\"" + P + "\",\"bone\":\"cf_J_Neck\",\"keys\":[]}]", BridgeCodes.WrongKind, "tracks[0].key", "TL6 a bone track on an item");
            Refused(t, "[{\"type\":\"scale\",\"key\":\"" + L + "\",\"keys\":[]}]", BridgeCodes.WrongKind, "tracks[0].key", "TL6 a scale track on a light");
            Refused(t, "[{\"type\":\"eyesOpen\",\"key\":\"" + P + "\",\"keys\":[]}]", BridgeCodes.WrongKind, "tracks[0].key", "TL6 a face track on an item");
            var num = Refused(t, "[{\"type\":\"pos\",\"key\":3,\"keys\":[]}]", BridgeCodes.BadArgs, "tracks[0].key", "TL6 a number as key");
            t.Check(num != null && num.Message.Contains("node ids"), "TL6 the number-key refusal says keys are node ids");
            Refused(t, "[{\"type\":\"pos\",\"keys\":[]}]", BridgeCodes.BadArgs, "tracks[0].key", "TL6 a node track without a key");
            Refused(t, "[{\"type\":\"camera\",\"key\":\"" + A + "\",\"keys\":[]}]", BridgeCodes.BadArgs, "tracks[0].key", "TL6 a camera track naming a node");
            Refused(t, "[{\"type\":\"pos\",\"key\":\"" + P + "\",\"keys\":[{\"t\":11,\"v\":[0,0,0]}]}]", BridgeCodes.BadArgs, "tracks[0].keys[0].t", "TL6 a key time past the duration");
            Refused(t, "[{\"type\":\"pos\",\"key\":\"" + P + "\",\"keys\":[{\"t\":-0.5,\"v\":[0,0,0]}]}]", BridgeCodes.BadArgs, "tracks[0].keys[0].t", "TL6 a negative key time");
            var dup = Refused(t, "[{\"type\":\"pos\",\"key\":\"" + P + "\",\"keys\":[{\"t\":1,\"v\":[0,0,0]},{\"t\":1.0,\"v\":[1,0,0]}]}]", BridgeCodes.BadArgs, "tracks[0].keys[1].t", "TL6 two keys at one time");
            t.Check(dup != null && dup.Message.Contains("tracks[0]") && dup.Message.Contains("1 s"), "TL6 the equal-time refusal names the track and the time");
            Refused(t, "[{\"type\":\"pos\",\"key\":\"" + P + "\",\"keys\":[]},{\"type\":\"pos\",\"key\":\"" + P + "\",\"keys\":[]}]", BridgeCodes.BadArgs, "tracks[1]", "TL6 a second track on a channel");
            var ex = Build("[{\"type\":\"rot\",\"key\":\"" + A + "\",\"bone\":\"cf_J_Head\",\"keys\":[]}]");
            t.Throws<BridgeException>(() => Build("[{\"type\":\"rot\",\"key\":\"" + A + "\",\"bone\":\"cf_J_Head\",\"keys\":[]}]", existing: ex), "TL6 append: a channel the existing tracks drive is refused");
            t.Check(Build("[{\"type\":\"rot\",\"key\":\"" + A + "\",\"bone\":\"cf_J_Neck\",\"keys\":[]}]", existing: ex).Count == 1, "TL6 append: another bone of the character is a new channel");
            Refused(t, "[{\"type\":\"anime\",\"key\":\"" + A + "\",\"keys\":[{\"t\":0,\"group\":95,\"category\":0,\"no\":0}]}]", BridgeCodes.BadArgs, "tracks[0].keys[0]", "TL6 an animation row that does not exist");
            var unbuilt = MakeHost();
            unbuilt.Built = false;
            Refused(t, "[{\"type\":\"eyes\",\"key\":\"" + A + "\",\"keys\":[{\"t\":0,\"v\":1}]}]", BridgeCodes.Unavailable, null, "TL6 eye positions need a built character", unbuilt);
            t.Check(Build("[{\"type\":\"eyes\",\"key\":\"" + A + "\",\"raw\":true,\"keys\":[{\"t\":0,\"v\":1}]}]", host: unbuilt).Count == 1, "TL6 raw eye pattern ids need no built character");
            Refused(t, "[{\"type\":\"color\",\"key\":\"" + P + "\",\"keys\":[]}]", BridgeCodes.BadArgs, "slot", "TL6 an item colour track without a slot");
            Refused(t, "[{\"type\":\"color\",\"key\":\"" + L + "\",\"slot\":0,\"keys\":[]}]", BridgeCodes.BadArgs, "slot", "TL6 a light colour track with a slot");
            Refused(t, "[{\"type\":\"color\",\"key\":\"" + P + "\",\"slot\":4,\"keys\":[]}]", BridgeCodes.BadArgs, "slot", "TL6 a colour slot outside 0-3");
            Refused(t, "[{\"type\":\"pos\",\"key\":\"" + P + "\",\"slot\":1,\"keys\":[]}]", BridgeCodes.BadArgs, "slot", "TL6 a slot on another type");
            Refused(t, "[{\"type\":\"pos\",\"key\":\"" + A + "\",\"bone\":\"cf_J_Neck\",\"keys\":[]}]", BridgeCodes.BadArgs, "bone", "TL6 a bone on a pos track");
            Refused(t, "[{\"type\":\"pos\",\"key\":\"" + P + "\",\"keys\":[{\"t\":0,\"v\":[0,0]}]}]", BridgeCodes.BadArgs, "keys[0].v", "TL6 a vector of two numbers");
            Refused(t, "[{\"type\":\"pos\",\"key\":\"" + P + "\",\"keys\":[{\"t\":0}]}]", BridgeCodes.BadArgs, "keys[0].v", "TL6 a key without v");
            Refused(t, "[{\"type\":\"pos\",\"key\":\"" + P + "\",\"ease\":\"bounce\",\"keys\":[]}]", BridgeCodes.BadArgs, "tracks[0].ease", "TL6 an unknown ease");
            Refused(t, "{\"type\":\"pos\"}", BridgeCodes.BadArgs, "tracks", "TL6 tracks must be an array");
            w = new List<string>();
            Build("[{\"type\":\"pos\",\"key\":\"" + P + "\",\"colour\":1,\"keys\":[{\"t\":0,\"v\":[0,0,0],\"w\":2}]}]", warnings: w);
            t.Check(w.Count == 2 && w.Any(x => x.Contains("'colour'")) && w.Any(x => x.Contains("'w'")), "TL6 unknown members of tracks and keys are warnings");
            Refused(t, "[{\"type\":\"pos\",\"key\":\"" + P + "\",\"colour\":1,\"keys\":[]}]", BridgeCodes.BadArgs, "colour", "TL6 strict: an unknown member is refused", strict: true);
            t.Check(Build("[]").Count == 0 && Build("null").Count == 0, "TL6 no tracks: an empty list");
        }

        static void BuildEvents(TestRunner t)
        {
            var w = new List<string>();
            var ev = TimelineBuildInput.ParseEvents(JToken.Parse("[{\"t\":2,\"do\":{\"cmd\":\"chara.face\",\"key\":\"x\"},\"note\":\"n\"},{\"t\":0,\"do\":{\"cmd\":\"timeline.play\"},\"enabled\":false}," +
                "{\"t\":2,\"do\":{\"cmd\":\"obj.transform\",\"undo\":true}}]"), 10, new CountingIdSource(), false, w);
            t.Check(ev.Count == 3 && ev[0].T == 0 && ev[1].T == 2 && ev[2].T == 2 && (string)ev[1].Do["cmd"] == "chara.face" && (string)ev[2].Do["cmd"] == "obj.transform" &&
                    ev[1].Note == "n" && !ev[0].Enabled && ev.All(x => x.Id != null && x.Id.Length == 32) && w.Count == 0,
                "TL6 events: new ids, sorted by time (stable), notes kept, enabled read, timeline.play allowed");
            var ids = new CountingIdSource();
            foreach (var bad in new[] { "{\"t\":0,\"do\":{\"key\":\"x\"}}", "{\"t\":0,\"do\":{\"cmd\":\"\"}}", "{\"t\":0,\"do\":{\"cmd\":\"a.b\",\"id\":\"q\"}}",
                         "{\"t\":0,\"do\":{\"cmd\":\"timeline.build\"}}", "{\"t\":0,\"do\":{\"cmd\":\"timeline.key.set\"}}", "{\"t\":0,\"do\":{\"cmd\":\"timeline.track.add\"}}",
                         "{\"t\":0,\"do\":\"chara.face\"}", "{\"t\":11,\"do\":{\"cmd\":\"a.b\"}}", "{\"do\":{\"cmd\":\"a.b\"}}" })
            {
                var e = t.Throws<BridgeException>(() => TimelineBuildInput.ParseEvents(JToken.Parse("[" + bad + "]"), 10, ids, false, null), "TL6 event refused: " + bad);
                if (e != null) t.Check(e.Code == BridgeCodes.BadArgs, "TL6 event refusal is bad_args: " + bad);
            }
            t.Check(TimelineBuildInput.ParseEvents(JToken.Parse("[{\"t\":50,\"do\":{\"cmd\":\"a.b\"}}]"), null, ids, false, null).Count == 1, "TL6 events without a duration bound (a new Timeline)");
            var big = new JObject { ["cmd"] = "a.b", ["pad"] = new string('x', 70000) };
            t.Throws<BridgeException>(() => TimelineBuildInput.ParseEvents(new JArray(new JObject { ["t"] = 0, ["do"] = big }), 10, ids, false, null), "TL6 a request over 64 KiB is refused");
            t.Check(TimelineEventRules.Request(JObject.Parse("{\"cmd\":\"a.b\",\"x\":1}"), "abc") == "{\"cmd\":\"a.b\",\"x\":1,\"undo\":false,\"id\":\"timeline:abc\"}" &&
                    TimelineEventRules.Request(JObject.Parse("{\"cmd\":\"a.b\",\"undo\":true}"), "abc") == "{\"cmd\":\"a.b\",\"undo\":true,\"id\":\"timeline:abc\"}",
                "TL6 the run request: undo false unless given, id timeline:<event id>");
        }

        // ---------- TL7 the compositions ----------

        static readonly Dictionary<string, string> Placeholders = new Dictionary<string, string> { ["$a"] = A, ["$b"] = B, ["$p"] = P, ["$l"] = L };

        static JObject Load(TestRunner t, string name)
        {
            string path = Path.Combine(t.FixturesDir ?? "", "Timeline", "compositions", name + ".json");
            if (!File.Exists(path))
            {
                t.Check(false, "TL7 fixture " + name + ".json exists");
                return null;
            }
            string text = File.ReadAllText(path);
            foreach (var kv in Placeholders) text = text.Replace("\"" + kv.Key + "\"", "\"" + kv.Value + "\"");
            return JObject.Parse(text);
        }

        static void Compositions(TestRunner t)
        {
            var expect = new[] { ("camera_dof", 2, 10), ("walk", 5, 28), ("bones9", 10, 82) };
            int tracks = 0, keys = 0;
            var types = new Dictionary<string, int>();
            var inputEases = new Dictionary<string, Dictionary<string, int>>();
            var storedEases = new Dictionary<string, Dictionary<string, int>>();
            foreach (var (name, nt, nk) in expect)
            {
                var o = Load(t, name);
                if (o == null) continue;
                var w = new List<string>();
                var tr = TimelineBuildInput.ParseTracks(o["tracks"], (float)o["duration"], MakeHost(), new CountingIdSource(), true, w);
                int k = tr.Sum(x => x.Keys.Count);
                t.Check(tr.Count == nt && k == nk && w.Count == 0, "TL7 " + name + ": " + nt + " tracks / " + nk + " keys, strict, no warning (got " + tr.Count + " / " + k + ")");
                tracks += tr.Count;
                keys += k;
                foreach (var x in tr)
                {
                    string ty = x.Type + (x.Bone != null ? ":bone" : "");
                    types[ty] = (types.TryGetValue(ty, out int c) ? c : 0) + 1;
                }
                var ie = inputEases[name] = new Dictionary<string, int>();
                foreach (var jt in (JArray)o["tracks"])
                    foreach (var jk in (JArray)jt["keys"])
                    {
                        string e = (string)jk["ease"] ?? "default";
                        ie[e] = (ie.TryGetValue(e, out int c) ? c : 0) + 1;
                    }
                var se = storedEases[name] = new Dictionary<string, int>();
                foreach (var x in tr)
                    foreach (var kk in x.Keys) se[kk.Ease] = (se.TryGetValue(kk.Ease, out int c) ? c : 0) + 1;
                // Compiled and played: every track active, a value for each at a few times, no exception.
                var doc = new TimelineDocument { Duration = (float)o["duration"], Loop = (bool)o["loop"], Tracks = tr };
                var sink = MakeSink();
                var p = new TimelinePlayer(sink);
                p.SetTimeline(TimelineCompiler.Compile(doc));
                bool shownAll = true;
                foreach (double at in new[] { 0, 0.37, 2.5, 7.79, 9.99, 12.0, 25.0 })
                {
                    p.Seek(at);
                    shownAll &= sink.Shown.Count == nt;
                }
                t.Check(shownAll && p.Timeline.Tracks.All(x => x.Active), "TL7 " + name + ": compiled, every track active and shown at 7 times");
            }
            t.Check(tracks == 17 && keys == 120, "TL7 the three compositions: 17 tracks / 120 keys (got " + tracks + " / " + keys + ")");
            t.Check(Count(types, "camera") == 3 && Count(types, "dof") == 1 && Count(types, "pos") == 1 && Count(types, "rot") == 1 && Count(types, "anime") == 1 &&
                    Count(types, "animeSpeed") == 1 && Count(types, "rot:bone") == 9, "TL7 types: camera 3, dof 1, pos 1, rot 1, anime 1, animeSpeed 1, FK bone rot 9");
            t.Check(Same(inputEases, "camera_dof", ("linear", 2), ("step", 6), ("inout", 2)), "TL7 camera_dof key eases: linear 2, step 6, inout 2");
            t.Check(Same(inputEases, "walk", ("default", 11), ("linear", 9), ("step", 8)), "TL7 walk key eases: track default 11, linear 9, step 8");
            t.Check(Same(inputEases, "bones9", ("default", 60), ("out", 3), ("step", 10), ("linear", 9)), "TL7 bones9 key eases: track default 60, out 3, step 10, linear 9");
            t.Check(Same(storedEases, "camera_dof", ("linear", 2), ("step", 6), ("inout", 2)) && Same(storedEases, "walk", ("linear", 12), ("step", 15), ("inout", 1)) &&
                    Same(storedEases, "bones9", ("inout", 60), ("out", 3), ("step", 10), ("linear", 9)), "TL7 stored eases after the track defaults and the step types");

            // all_types: every type, every ease, every focus form, six events.
            var all = Load(t, "all_types");
            if (all == null) return;
            var aw = new List<string>();
            var at2 = TimelineBuildInput.ParseTracks(all["tracks"], (float)all["duration"], MakeHost(), new CountingIdSource(), true, aw);
            var kinds = at2.Select(x => TimelineTrackTypes.FromStored(x.Type, x.Bone)).ToList();
            bool everyType = true;
            foreach (TimelineTrackType ty in Enum.GetValues(typeof(TimelineTrackType)))
                if (ty != TimelineTrackType.Unknown) everyType &= kinds.Contains(ty);
            t.Check(at2.Count == 15 && everyType && at2.Count(x => x.Type == "color" && x.Slot == 0) == 1 && at2.Count(x => x.Type == "color" && x.Slot == null) == 1 && aw.Count == 0,
                "TL7 all_types: 15 tracks, one of every type (an item colour slot 0 and a light colour)");
            t.Check(at2.All(x => x.Keys.Count >= 3 && x.Keys.Count <= 5) && at2.Sum(x => x.Keys.Count) == 49, "TL7 all_types: 3-5 keys per track, 49 keys");
            var eases = new HashSet<string>(at2.SelectMany(x => x.Keys).Select(x => x.Ease));
            t.Check(eases.SetEquals(TimelineEaseCurves.Names), "TL7 all_types: every ease");
            var dof = at2.First(x => x.Type == "dof").Keys.Select(x => x.Dof.Focus).ToList();
            t.Check(dof.Count == 5 && dof[0] == null && dof[1].Kind == TimelineFocusKind.Node && dof[1].Bone == null && dof[2].Kind == TimelineFocusKind.Node && dof[2].Bone != null &&
                    dof[3].Kind == TimelineFocusKind.Point && dof[4].Kind == TimelineFocusKind.Dist, "TL7 all_types: a dof key of each focus form");
            var aev = TimelineBuildInput.ParseEvents(all["events"], (float)all["duration"], new CountingIdSource(), true, aw);
            var cmds = new HashSet<string>(aev.Select(x => (string)x.Do["cmd"]));
            t.Check(aev.Count == 6 && cmds.SetEquals(new[] { "obj.transform", "chara.face", "chara.anime" }) && aev.Select(x => x.T).Distinct().SequenceEqual(new[] { 0f, 4f, 8f }),
                "TL7 all_types: 6 events (obj.transform, chara.face, chara.anime) at 0, the middle and the duration");
            var adoc = new TimelineDocument { Duration = (float)all["duration"], Tracks = at2, Events = aev };
            var asink = MakeSink();
            var ap = new TimelinePlayer(asink);
            ap.SetTimeline(TimelineCompiler.Compile(adoc));
            ap.Play();
            for (int i = 0; i < 15; i++) ap.Tick(0.5);
            t.Check(asink.Shown.Count == 15 && asink.Fired.Count == 4 && ap.Time == 7.5, "TL7 all_types played to 7.5 s: 15 values shown, the events at 0 and 4 run (" + asink.Fired.Count + ")");
            ap.Tick(0.5);
            t.Check(asink.Fired.Count == 8 && ap.Time == 0 && asink.Fired.Skip(4).Select(x => x.T).SequenceEqual(new[] { 8.0, 8.0, 0.0, 0.0 }),
                "TL7 all_types: the wrap runs the two events at the duration, then the two at 0");
            // The stored form round-trips through the Timeline document's writer and reader unchanged for evaluation.
            var back = TimelineDocument.Parse(adoc.ToJson());
            var c1 = TimelineCompiler.Compile(adoc);
            var c2 = TimelineCompiler.Compile(back);
            bool same = c1.Tracks.Count == c2.Tracks.Count;
            for (int i = 0; same && i < c1.Tracks.Count; i++) same &= c1.Tracks[i].SameKeys(c2.Tracks[i]);
            t.Check(same && c2.Runs.Count == 6, "TL7 all_types: the written and read document compiles to the same keys and runs");
        }

        static int Count(Dictionary<string, int> d, string k) => d.TryGetValue(k, out int c) ? c : 0;

        static bool Same(Dictionary<string, Dictionary<string, int>> d, string name, params (string, int)[] want)
        {
            if (!d.TryGetValue(name, out var got)) return false;
            if (got.Count != want.Length) return false;
            foreach (var (k, n) in want) if (!got.TryGetValue(k, out int c) || c != n) return false;
            return true;
        }
    }
}

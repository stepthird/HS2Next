// ScriptTest checks of the generic commands of scene scripts (Phase 6.9 package G), on the SFW targets of
// Tools/targets.json (BR-Chan, HERO107), judged by numbers:
//   headless   JC1 obj.moveTo's arithmetic on hand-made positions, through the command on folders (pause and steps): the reply, the yaw, the slot from the
//              shown position, the lerp, the end in the model, no history; a replaced move starts from the shown position; an early release writes the
//              shown position; the animation speed's clamp on BR-Chan.
//              JC5 dof's sharp / full give timeline.build's stored focal size and aperture for 20 pairs; u < 0.02 refused by both.
//              JC7 fk.enable's limb names switch the Body group with the warning; unknown names still bad_args.
//              JC8 plugin.call, plugin.set, motion.play stay unknown (the script import's warning is C's part of JC8).
//   play       JC2 obj.moveTo on HERO107 at fixed steps: the pause holds the move, the slot lerps over the scene clock, a save mid-move stores the start,
//              the end in the model. JC3 camera.follow over a Timeline camera track: the smoothing, smoothstep, rot to rotTo, dist to distTo per step against
//              the formulas, the pause holds it, the end releases the slot. JC4 dof: every row of the mapping, blends with each ease, release and reset
//              leave the scene's block, unsupported members warn. JC6 chara.accessoryAxis: hand-made points against an independent eigenvector, the
//              targets' accessory slots (a readable mesh, a skinned one, one read by its bounds), whether HERO107 has slot 2, and `from: {key,
//              accessory}` in fx.shoot, wet.attach, wet.stain.
// ScriptTest runs them as its parts G_Commands (ScriptTest.Run: JC1, JC5, JC7, JC8 on a host of their own; ScriptTest.RunPlay: JC2, JC3, JC4, JC6 in the app
// started for them). Time moves only through scene.pause and scene steps, so the play checks run the same in an edit-mode host and in the play app; the
// checks write through a Sink, so ScriptTestG holds every helper and no name of this file can meet another part's. Every check that compares a difference
// prints what shows the thing compared is there.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using HS2Next.App;
using HS2Next.App.Commands;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Overrides;
using HS2Next.Scenes.Runtime;
using HS2Next.Scenes.Timeline;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class ScriptTestG
{
    // ------------------------------------------------------------ the report a part writes into

    /// <summary>Where the checks write: this class's own report, or ScriptTest's.</summary>
    public sealed class Sink
    {
        public Func<string, bool, string, bool> Check;
        public Action<string> Info, Warn, Measure;
        /// <summary>Runs a headless group (skipped when -only leaves it out).</summary>
        public Action<string, string, Action> Group;
        /// <summary>Opens a play group; false when -only leaves it out.</summary>
        public Func<string, string, bool> Open;
    }

    /// <summary>A host the checks call: an edit-mode test host (calls and frames at once) or the play app (calls and frames in real frames).</summary>
    public abstract class GHost
    {
        public AppCore App;
        public SceneRuntime Rt => App.Runtime;
        public SceneSession Session => App.Session;
        public JObject Last;
        public string LastText;
        public abstract IEnumerator Call(object request, double timeoutSec = 300);
        public abstract IEnumerator Frames(int n);
        protected void Took(string text)
        {
            LastText = text;
            try { Last = JObject.Parse(text); }
            catch (Exception) { Last = new JObject { ["ok"] = false, ["error"] = "unparsable: " + text }; }
        }
        protected static string Json(object request) => request is string s ? s : JsonConvert.SerializeObject(request);
    }

    public sealed class GEditHost : GHost
    {
        public float Dt = 1f / 60f;
        public override IEnumerator Call(object request, double timeoutSec = 300)
        {
            Took(App.Call(Json(request)));
            yield break;
        }
        public override IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) App.Frame(Dt);
            yield break;
        }
    }

    public sealed class GPlayHost : GHost
    {
        public override IEnumerator Call(object request, double timeoutSec = 300)
        {
            var t = App.Bridge.Execute(Json(request));
            float until = Time.realtimeSinceStartup + (float)timeoutSec;
            while (!t.IsCompleted && Time.realtimeSinceStartup < until) yield return null;
            Took(t.IsCompleted ? (t.IsFaulted ? "{\"ok\":false,\"error\":\"faulted\"}" : t.Result) : "{\"ok\":false,\"code\":\"timeout\"}");
        }
        public override IEnumerator Frames(int n) => PlayModeHarness.WaitFrames(n);
    }

    /// <summary>Runs a coroutine of an edit-mode host to its end at once (nested coroutines included).</summary>
    public static void Drain(IEnumerator e)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(e);
        while (stack.Count > 0)
        {
            var top = stack.Peek();
            if (!top.MoveNext()) { stack.Pop(); continue; }
            if (top.Current is IEnumerator inner) stack.Push(inner);
        }
    }

    /// <summary>The generic commands in the host's registry when the group "script" has not registered them yet.</summary>
    public static void EnsureRegistered(AppCore app)
    {
        var reg = app?.Bridge?.Server?.Commands;
        if (reg != null && !reg.Contains("obj.moveTo")) ScriptedCommands.Register(reg, app);
    }

    // ------------------------------------------------------------ small helpers

    const float Dt30 = 1f / 30f;
    const string Clock = "2026-11-08T00:00:00Z";
    static readonly string[] Eases = ScriptedDof.Eases;

    static bool IsOk(JObject r) => r?.Value<bool?>("ok") == true;
    static string Code(JObject r) => (string)r?["code"];
    static string Short(string s) => s == null ? "null" : s.Length > 400 ? s.Substring(0, 400) + "..." : s;
    static string Warnings(JObject r) => r?["warnings"] is JArray a ? string.Join(" | ", a.Select(x => (string)x)) : "";
    static bool Warned(JObject r, string text) => r?["warnings"] is JArray a && a.Any(x => ((string)x ?? "").Contains(text));
    static int WarnCount(JObject r, string text) => r?["warnings"] is JArray a ? a.Count(x => ((string)x ?? "").Contains(text)) : 0;
    static Vector3 V3(JToken t) => new Vector3((float)(double)t[0], (float)(double)t[1], (float)(double)t[2]);
    static string F(double d) => d.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
    static string F(Vector3 v) => "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";
    static string Key(JObject r) => (string)r?["key"] ?? (string)r?["added"]?[0]?["key"];

    /// <summary>The angle between two rotations in degrees, in double (Quaternion.Angle reads 0 under about 0.16 degrees).</summary>
    static double Deg(Quaternion a, Quaternion b)
    {
        double d = Math.Abs((double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z + (double)a.w * b.w);
        double la = Math.Sqrt((double)a.x * a.x + (double)a.y * a.y + (double)a.z * a.z + (double)a.w * a.w);
        double lb = Math.Sqrt((double)b.x * b.x + (double)b.y * b.y + (double)b.z * b.z + (double)b.w * b.w);
        d /= la * lb;
        return d >= 1 ? 0 : 2 * Math.Acos(d) * 180 / Math.PI;
    }

    static Vector3 SlotPos(SceneRuntime rt, string id)
    {
        var w = rt.Overrides.WinnerOf(id, OverrideChannel.TransformPos);
        return w != null && w.HasValue ? new Vector3((float)w[0], (float)w[1], (float)w[2]) : new Vector3(float.NaN, float.NaN, float.NaN);
    }

    static Vector3 ModelPos(SceneRuntime rt, string id)
    {
        var p = rt.Model.Find(id)?.Doc.Transform?.Pos ?? new Vec3(float.NaN, float.NaN, float.NaN);
        return new Vector3(p.X, p.Y, p.Z);
    }

    static Vector3 ModelRot(SceneRuntime rt, string id)
    {
        var p = rt.Model.Find(id)?.Doc.Transform?.Rot ?? new Vec3(float.NaN, float.NaN, float.NaN);
        return new Vector3(p.X, p.Y, p.Z);
    }

    static string MoveOwner(SceneRuntime rt, string id) => rt.Overrides.WinnerOf(id, OverrideChannel.TransformPos)?.Owner;
    static int Undo(GHost h) => h.Session.Commands.UndoList.Count;

    static void Place(GHost h, string id, Vector3 pos, Vector3 rot)
    {
        h.Session.Set(id, "transform.pos", new Vec3(pos.x, pos.y, pos.z));
        h.Session.Set(id, "transform.rot", new Vec3(rot.x, rot.y, rot.z));
        h.Rt.ApplyNow();
    }

    // ------------------------------------------------------------ the expected arithmetic (written from the spec, in double)

    static void ExpectedPlan(Vector3 from, Vector3 target, float stop, out Vector3 end, out double? yaw, out double dist)
    {
        double dx = (double)target.x - from.x, dz = (double)target.z - from.z;
        double len = Math.Sqrt(dx * dx + dz * dz);
        end = len > stop ? new Vector3((float)(from.x + dx / len * (len - stop)), from.y, (float)(from.z + dz / len * (len - stop))) : from;
        yaw = dx * dx + dz * dz > 1e-6 ? Math.Atan2(dx, dz) * 180 / Math.PI : (double?)null;
        double ex = end.x - (double)from.x, ez = end.z - (double)from.z;
        dist = Math.Sqrt(ex * ex + ez * ez);
    }

    static double ExpectedStance(double distance, double time, double stance)
    {
        double v = distance / Math.Max(0.05, time) / stance;
        return Math.Max(0.2, Math.Min(2, double.IsNaN(v) ? 0.2 : v));
    }

    static double Smooth(double u)
    {
        u = Math.Max(0, Math.Min(1, u));
        return u * u * (3 - 2 * u);
    }

    static double EaseOf(string e, double u)
    {
        u = Math.Max(0, Math.Min(1, u));
        switch (e)
        {
            case "linear": return u;
            case "in": return u * u;
            case "out": return 1 - (1 - u) * (1 - u);
            default: return u * u * (3 - 2 * u);
        }
    }

    // ============================================================ headless: JC1, JC5, JC7, JC8

    /// <summary>The headless checks on one host of <paramref name="newHost"/> (disposed here).</summary>
    public static void Headless(Sink s, Func<GHost> newHost, string femaleRel)
    {
        var h = newHost();
        try
        {
            EnsureRegistered(h.App);
            string a = null;
            s.Group("JC1", "obj.moveTo's arithmetic: end, yaw, the speed clamp, a replaced move, an early release", () => a = Jc1(s, h, femaleRel));
            s.Group("JC5", "dof's sharp / full against timeline.build's for 20 pairs; u < 0.02 refused", () => Jc5(s, h));
            s.Group("JC7", "fk.enable's limb names switch the Body group with a warning", () => Jc7(s, h, a ?? LoadChara(s, h, femaleRel, "female")));
            s.Group("JC8", "plugin.call, plugin.set and motion.play stay unknown", () => Jc8(s, h));
        }
        finally { try { h.App.Dispose(); } catch (Exception e) { s.Info("host dispose threw: " + e.Message); } }
    }

    static JObject Sync(GHost h, object request)
    {
        Drain(h.Call(request));
        return h.Last;
    }

    static JObject Ok(Sink s, GHost h, object request, string what)
    {
        var r = Sync(h, request);
        if (!IsOk(r)) s.Check(what, false, Short(h.LastText));
        return r;
    }

    static string LoadChara(Sink s, GHost h, string rel, string sex)
    {
        var k = Key(Ok(s, h, new { cmd = "chara.load", path = rel, sex }, "load the " + sex + " target"));
        Ok(s, h, new { cmd = "chara.motion", key = k, physics = false }, "physics off");
        return k;
    }

    static string Jc1(Sink s, GHost h, string femaleRel)
    {
        // the arithmetic on hand-made positions
        var cases = new (string name, Vector3 from, Vector3 target, float stop)[]
        {
            ("|d| > stop", new Vector3(1, 2, 3), new Vector3(9, 5, -3), 4),
            ("|d| > stop, stop 0", new Vector3(-2, 1, 4), new Vector3(3, -6, -8), 0),
            ("|d| <= stop", new Vector3(0, 0, 0), new Vector3(2, 9, 1), 4),
            ("|d| = stop", new Vector3(0, 0, 0), new Vector3(3, 0, 4), 5),
            ("|d|^2 <= 1e-6 (the old yaw stays)", new Vector3(5, 0, 5), new Vector3(5.0005f, 3, 5), 4),
        };
        int far = 0, near = 0;
        foreach (var c in cases)
        {
            var p = ScriptedMoves.Plan(c.from, c.target, c.stop);
            ExpectedPlan(c.from, c.target, c.stop, out var end, out var yaw, out var dist);
            double dx = c.target.x - (double)c.from.x, dz = c.target.z - (double)c.from.z;
            if (Math.Sqrt(dx * dx + dz * dz) > c.stop) far++; else near++;
            bool yawOk = yaw.HasValue ? p.Yaw.HasValue && Math.Abs(p.Yaw.Value - yaw.Value) <= 1e-4 : !p.Yaw.HasValue;
            s.Check("plan " + c.name + ": end, yaw and distance as the arithmetic", (p.End - end).magnitude <= 1e-5f && yawOk && Math.Abs(p.Distance - dist) <= 1e-5,
                "end " + F(p.End) + " want " + F(end) + ", yaw " + (p.Yaw.HasValue ? F(p.Yaw.Value) : "none") + " want " + (yaw.HasValue ? F(yaw.Value) : "none") + ", distance " + F(p.Distance) + " want " + F(dist));
        }
        s.Check("Present: cases with |d| > stop and with |d| <= stop", far >= 1 && near >= 1, far + " and " + near);
        var speeds = new (float d, float t, float k)[] { (6, 1, 1), (6, 10, 1), (1, 10, 1), (3, 2, 1), (0, 0, 1), (9, 3, 2) };
        foreach (var x in speeds)
        {
            double got = ScriptedMoves.StanceSpeed(x.d, x.t, x.k), want = ExpectedStance(x.d, x.t, x.k);
            s.Check("the speed clamp: distance " + F(x.d) + ", time " + F(x.t) + ", stanceSpeed " + F(x.k), Math.Abs(got - want) <= 1e-6, F(got) + " want " + F(want));
        }

        // through the command, on folders, the scene paused
        var rt = h.Rt;
        Ok(s, h, new { cmd = "scene.pause", paused = true }, "pause");
        var fa = h.Session.AddFolder().NewId;
        Place(h, fa, new Vector3(1, 2, 3), new Vector3(10, 40, 5));
        int undo = Undo(h);
        var r = Ok(s, h, new { cmd = "obj.moveTo", key = fa, target = new[] { 9f, 5f, -3f }, stop = 4f, time = 1f }, "obj.moveTo on a folder");
        ExpectedPlan(new Vector3(1, 2, 3), new Vector3(9, 5, -3), 4f, out var e1, out var y1, out var d1);
        if (IsOk(r))
        {
            s.Check("the reply: to and distance", (V3(r["to"]) - e1).magnitude <= 1e-5f && Math.Abs((double)r["distance"] - d1) <= 1e-5, Short(r.ToString(Formatting.None)));
            var rot = ModelRot(rt, fa);
            s.Check("the rotation in the model is (0, yaw, 0) at once", Math.Abs(rot.x) <= 1e-6f && Math.Abs(rot.y - y1.Value) <= 1e-4 && Math.Abs(rot.z) <= 1e-6f, F(rot) + " want yaw " + F(y1.Value));
            s.Check("a slot of the owner move shows the start at once", MoveOwner(rt, fa) == ScriptedMoves.Owner && (SlotPos(rt, fa) - new Vector3(1, 2, 3)).magnitude == 0f, MoveOwner(rt, fa) + " " + F(SlotPos(rt, fa)));
            s.Check("the call added no history entry", Undo(h) == undo, undo + " -> " + Undo(h));
            Ok(s, h, new { cmd = "scene.step", n = 30, dt = 1f / 60f }, "30 steps of 1/60");
            var mid = Vector3.LerpUnclamped(new Vector3(1, 2, 3), e1, 30 * (1f / 60f) / 1f);
            var hold = rt.HolderOf(fa).position;
            s.Check("half way: the slot is lerp(from, end, elapsed / time)", (SlotPos(rt, fa) - mid).magnitude <= 1e-4f && (hold - mid).magnitude <= 1e-4f, "slot " + F(SlotPos(rt, fa)) + ", holder " + F(hold) + ", want " + F(mid));
            s.Check("half way: the model keeps the start", ModelPos(rt, fa) == new Vector3(1, 2, 3), F(ModelPos(rt, fa)));
            Ok(s, h, new { cmd = "scene.step", n = 32, dt = 1f / 60f }, "32 more steps");
            s.Check("the end: the model holds the end, the slot is released", (ModelPos(rt, fa) - e1).magnitude <= 1e-6f && MoveOwner(rt, fa) == null && ScriptedMoves.Of(rt).Count == 0,
                "model " + F(ModelPos(rt, fa)) + " want " + F(e1) + ", owner " + (MoveOwner(rt, fa) ?? "none"));
            s.Check("the end: the holder stands at the end", (rt.HolderOf(fa).position - e1).magnitude <= 1e-5f, F(rt.HolderOf(fa).position));
            s.Check("the move added no history entry", Undo(h) == undo, undo + " -> " + Undo(h));
            s.Check("Present: the folder moved", (e1 - new Vector3(1, 2, 3)).magnitude >= 1f, F((e1 - new Vector3(1, 2, 3)).magnitude));
        }

        // a replaced move starts from the shown position
        var fb = h.Session.AddFolder().NewId;
        Place(h, fb, Vector3.zero, Vector3.zero);
        Ok(s, h, new { cmd = "obj.moveTo", key = fb, target = new[] { 10f, 0f, 0f }, stop = 0f, time = 1f }, "the first move");
        Ok(s, h, new { cmd = "scene.step", n = 30, dt = 1f / 60f }, "half of it");
        var shown = SlotPos(rt, fb);
        r = Ok(s, h, new { cmd = "obj.moveTo", key = fb, target = new[] { 5f, 0f, 10f }, stop = 0f, time = 2f }, "a new move of the same node");
        if (IsOk(r))
        {
            ExpectedPlan(shown, new Vector3(5, 0, 10), 0f, out var e2, out _, out var d2);
            s.Check("the new move starts from the shown position (not the model's)", Math.Abs((double)r["distance"] - d2) <= 1e-5 && (SlotPos(rt, fb) - shown).magnitude == 0f && ModelPos(rt, fb) == Vector3.zero,
                "distance " + F((double)r["distance"]) + " want " + F(d2) + " (from the model it would be " + F(new Vector3(5, 0, 10).magnitude) + "), shown " + F(shown));
            Ok(s, h, new { cmd = "scene.step", n = 1, dt = 1f / 60f }, "one step");
            var want = Vector3.LerpUnclamped(shown, e2, (1f / 60f) / 2f);
            s.Check("one slot per node, lerping on the new move", ScriptedMoves.Of(rt).Count == 1 && (SlotPos(rt, fb) - want).magnitude <= 1e-5f, F(SlotPos(rt, fb)) + " want " + F(want));
            s.Check("Present: the shown position was away from the model's", shown.magnitude >= 1f, F(shown));
        }

        // an early release writes the shown position
        Ok(s, h, new { cmd = "scene.step", n = 20, dt = 1f / 60f }, "20 steps");
        var before = SlotPos(rt, fb);
        int k = ScriptedMoves.Of(rt).ReleaseAll();
        rt.ApplyNow();
        s.Check("an early release writes the shown position into the model and releases the slot", k == 1 && ModelPos(rt, fb) == before && MoveOwner(rt, fb) == null && (rt.HolderOf(fb).position - before).magnitude <= 1e-5f,
            "released " + k + ", model " + F(ModelPos(rt, fb)) + ", shown " + F(before));
        s.Check("Present: the released move was under way", (before - shown).magnitude >= 0.5f, F((before - shown).magnitude));

        // the animation speed's clamp through the command
        string a = LoadChara(s, h, femaleRel, "female");
        Place(h, a, Vector3.zero, Vector3.zero);
        foreach (var x in new (float tz, float time, float stance)[] { (16, 1, 1), (6, 10, 2), (9, 3, 2) })
        {
            r = Ok(s, h, new { cmd = "obj.moveTo", key = a, target = new[] { 0f, 0f, x.tz }, stop = 4f, time = x.time, stanceSpeed = x.stance }, "obj.moveTo with stanceSpeed");
            if (!IsOk(r)) continue;
            double want = ExpectedStance((double)r["distance"], x.time, x.stance);
            var st = (CharaNodeState)rt.Model.Find(a).State;
            s.Check("stanceSpeed " + F(x.stance) + " over " + F(x.time) + " s: the character's animation speed in the model", Math.Abs(st.Anime.Speed - want) <= 1e-5, F(st.Anime.Speed) + " want " + F(want));
            ScriptedMoves.Of(rt).ReleaseAll();
            Place(h, a, Vector3.zero, Vector3.zero);
        }
        Ok(s, h, new { cmd = "scene.pause", paused = false }, "resume");
        return a;
    }

    static void Jc5(Sink s, GHost h)
    {
        var pairs = new List<(double sharp, double full)>();
        foreach (var sh in new[] { 0.0, 0.05, 0.1, 0.25, 0.5 })
            foreach (var fu in new[] { 1.6, 2.5, 3.0, 8.0 })
                pairs.Add((sh, fu));
        var keys = new JArray();
        for (int i = 0; i < pairs.Count; i++) keys.Add(new JObject { ["t"] = i * 0.5, ["sharp"] = pairs[i].sharp, ["full"] = pairs[i].full });
        var tracks = new JArray { new JObject { ["type"] = "dof", ["keys"] = keys } };
        var r = Ok(s, h, new JObject { ["cmd"] = "timeline.build", ["duration"] = 12, ["play"] = false, ["tracks"] = tracks }.ToString(Formatting.None), "timeline.build with 20 dof keys");
        if (!IsOk(r)) return;
        var stored = h.Rt.Model.Timeline?.Tracks.FirstOrDefault()?.Keys;
        if (!s.Check("the stored dof track holds 20 keys", stored != null && stored.Count == pairs.Count, (stored?.Count ?? 0).ToString())) return;
        int equal = 0;
        var bad = new List<string>();
        for (int i = 0; i < pairs.Count; i++)
        {
            var q = Sync(h, new { cmd = "dof", sharp = pairs[i].sharp, full = pairs[i].full });
            if (!IsOk(q)) { bad.Add(i + ": " + Short(h.LastText)); continue; }
            float size = (float)(double)q["dof"]["size"], ap = (float)(double)q["dof"]["aperture"];
            var k = stored[i].Dof;
            if (BitConverter.SingleToInt32Bits(size) == BitConverter.SingleToInt32Bits(k.Size) && BitConverter.SingleToInt32Bits(ap) == BitConverter.SingleToInt32Bits(k.Aperture)) equal++;
            else bad.Add("sharp " + F(pairs[i].sharp) + " full " + F(pairs[i].full) + ": dof " + F(size) + "/" + F(ap) + ", timeline " + F(k.Size) + "/" + F(k.Aperture));
        }
        s.Check("20 pairs: dof's focal size and aperture equal timeline.build's stored ones (as floats, bit for bit)", equal == pairs.Count, equal + " of " + pairs.Count + " " + string.Join("; ", bad.Take(5)));
        // one of the two alone takes the other's default (0.1, 3)
        var one = Sync(h, new { cmd = "dof", sharp = 0.5 });
        TimelineBuildInput.FromRange(0.5, 3.0, out double s1, out double a1);
        var two = Sync(h, new { cmd = "dof", full = 5.0 });
        TimelineBuildInput.FromRange(0.1, 5.0, out double s2, out double a2);
        s.Check("sharp alone takes full 3, full alone takes sharp 0.1", IsOk(one) && IsOk(two) &&
            Math.Abs((double)one["dof"]["size"] - s1) <= 1e-12 && Math.Abs((double)one["dof"]["aperture"] - a1) <= 1e-12 &&
            Math.Abs((double)two["dof"]["size"] - s2) <= 1e-12 && Math.Abs((double)two["dof"]["aperture"] - a2) <= 1e-12,
            Short(one?.ToString(Formatting.None)) + " / " + Short(two?.ToString(Formatting.None)));
        // u < 0.02 refused by both
        var refused = Sync(h, new { cmd = "dof", sharp = 1.0, full = 2.0 });
        var tl = Sync(h, new JObject { ["cmd"] = "timeline.build", ["duration"] = 12, ["play"] = false, ["tracks"] = new JArray { new JObject { ["type"] = "dof", ["keys"] = new JArray { new JObject { ["t"] = 0, ["sharp"] = 1.0, ["full"] = 2.0 } } } } }.ToString(Formatting.None));
        s.Check("u < 0.02 (sharp 1, full 2): dof and timeline.build both answer bad_args", Code(refused) == "bad_args" && Code(tl) == "bad_args" && ((string)refused["error"] ?? "").Contains("full must be clearly larger than 1 + sharp"),
            Short(refused?.ToString(Formatting.None)) + " / " + Short(tl?.ToString(Formatting.None)));
        var edge = Sync(h, new { cmd = "dof", sharp = 0.0, full = 1.0204 });
        s.Check("u just under 0.02 (sharp 0, full 1.0204) refused", Code(edge) == "bad_args", Short(edge?.ToString(Formatting.None)));
        Sync(h, new { cmd = "dof", release = true });
        Sync(h, new { cmd = "timeline.clear" });
    }

    static void Jc7(Sink s, GHost h, string a)
    {
        var r = Ok(s, h, new { cmd = "fk.enable", key = a, on = false, groups = new[] { "Body" } }, "Body off");
        bool bodyOff = IsOk(r) && r["activeFK"]?[3]?.Value<bool>() == false;
        s.Check("Present: Body was off before", bodyOff, Short(h.LastText));
        r = Sync(h, new { cmd = "fk.enable", key = a, on = true, groups = new[] { "RightArm" } });
        s.Check("RightArm switches the Body group on, with the warning", IsOk(r) && r["activeFK"]?[3]?.Value<bool>() == true && Warned(r, "RightArm switches the Body group in HS2Next"), Short(h.LastText));
        r = Sync(h, new { cmd = "fk.enable", key = a, on = false, groups = new[] { "leftleg" } });
        s.Check("leftleg (any case) switches it off, the warning names LeftLeg", IsOk(r) && r["activeFK"]?[3]?.Value<bool>() == false && Warned(r, "LeftLeg switches the Body group in HS2Next"), Short(h.LastText));
        r = Sync(h, new { cmd = "fk.enable", key = a, on = true, groups = new[] { "RightArm", "LeftArm", "RightHand", "LeftHand" } });
        bool[] g = IsOk(r) ? r["activeFK"].Select(x => x.Value<bool>()).ToArray() : new bool[0];
        s.Check("the owner's form [RightArm, LeftArm, RightHand, LeftHand]: Body and both hands on, two warnings", IsOk(r) && g.Length == 7 && g[3] && g[4] && g[5] &&
            WarnCount(r, "switches the Body group in HS2Next") == 2, Short(h.LastText));
        r = Sync(h, new { cmd = "fk.enable", key = a, on = true, groups = new[] { "RightLeg", "LeftLeg" } });
        s.Check("[RightLeg, LeftLeg] answers ok with two warnings", IsOk(r) && WarnCount(r, "switches the Body group in HS2Next") == 2, Short(h.LastText));
        r = Sync(h, new { cmd = "fk.enable", key = a, on = true, groups = new[] { "RightFoot" } });
        s.Check("an unknown name still answers bad_args \"unknown FK group\"", Code(r) == "bad_args" && ((string)r["error"] ?? "").Contains("unknown FK group"), Short(h.LastText));
        r = Sync(h, new { cmd = "fk.enable", key = a, on = true, groups = new[] { "Body", "Neck" } });
        s.Check("the group names of before answer with no warning", IsOk(r) && Warnings(r).Length == 0, Short(h.LastText));
        Sync(h, new { cmd = "fk.enable", key = a, on = false });
    }

    static void Jc8(Sink s, GHost h)
    {
        foreach (var cmd in new[] { "plugin.call", "plugin.set", "motion.play" })
        {
            var r = Sync(h, new { cmd });
            s.Check(cmd + " stays unknown (unknown_command)", Code(r) == "unknown_command", Short(h.LastText));
        }
        s.Info("JC8: the warning of script.set / script.import for a step whose command the registry does not know is package C's part of JC8 (not run here)");
    }

    // ============================================================ play: JC2, JC3, JC4, JC6

    /// <summary>
    /// The play checks on <paramref name="h"/> (an edit-mode host drained at once, or the play app): one scene with both targets, idle and paused; time moves
    /// by scene steps only. The scene is left new and running.
    /// </summary>
    public static IEnumerator Play(Sink s, GHost h, string femaleRel, string maleRel)
    {
        EnsureRegistered(h.App);
        yield return h.Call(new { cmd = "scene.new" });
        yield return h.Call(new { cmd = "scene.pause", paused = true });
        yield return h.Call(new { cmd = "chara.load", path = femaleRel, sex = "female" });
        string f = Key(h.Last);
        yield return h.Call(new { cmd = "chara.load", path = maleRel, sex = "male" });
        string m = Key(h.Last);
        if (!s.Check("the two targets are in the scene", f != null && m != null, Short(h.LastText))) yield break;
        foreach (var k in new[] { f, m })
        {
            yield return h.Call(new { cmd = "chara.motion", key = k, physics = false });
            yield return h.Call(new { cmd = "chara.anime", key = k, group = 19, category = 0, no = 0, time = 0.5f, speed = 0f });
        }
        Place(h, f, new Vector3(-4, 0, 0), new Vector3(0, 90, 0));
        Place(h, m, new Vector3(4, 0, 0), new Vector3(0, -90, 0));
        yield return h.Frames(2);
        if (s.Open("JC2", "obj.moveTo at fixed steps: the pause holds it, the lerp, a save mid-move, the end")) yield return Jc2(s, h, m);
        if (s.Open("JC3", "camera.follow over a Timeline camera track against the formulas")) yield return Jc3(s, h, m);
        if (s.Open("JC4", "dof: the mapping, blends with each ease, release and reset")) yield return Jc4(s, h, f, m);
        if (s.Open("JC6", "chara.accessoryAxis and from: {key, accessory}")) yield return Jc6(s, h, f, m);
        ScriptedMoves.Of(h.Rt).ReleaseAll();
        yield return h.Call(new { cmd = "scene.new" });
        yield return h.Call(new { cmd = "scene.pause", paused = false });
    }

    static IEnumerator Jc2(Sink s, GHost h, string m)
    {
        var rt = h.Rt;
        Place(h, m, new Vector3(4, 0, 0), Vector3.zero);
        var start = rt.HolderOf(m).position;
        int undo = Undo(h);
        yield return h.Call(new { cmd = "obj.moveTo", key = m, target = new[] { 4f, 0f, 10f }, stop = 2f, time = 2f });
        var r = h.Last;
        if (!s.Check("obj.moveTo on HERO107", IsOk(r), Short(h.LastText))) yield break;
        s.Check("the reply: to (4, 0, 8), distance 8", (V3(r["to"]) - new Vector3(4, 0, 8)).magnitude <= 1e-5f && Math.Abs((double)r["distance"] - 8) <= 1e-5, Short(r.ToString(Formatting.None)));
        yield return h.Frames(10);
        s.Check("the pause holds the move: 10 frames, the slot and the holder at the start", (SlotPos(rt, m) - new Vector3(4, 0, 0)).magnitude == 0f && (rt.HolderOf(m).position - start).magnitude <= 1e-5f,
            "slot " + F(SlotPos(rt, m)) + ", holder " + F(rt.HolderOf(m).position));
        rt.Step(30, Dt30);
        var mid = new Vector3(4, 0, 0 + 8f * Math.Min(1f, 30 * Dt30 / 2f));
        s.Check("30 steps of 1/30 s: the slot and the holder are half way", (SlotPos(rt, m) - mid).magnitude <= 1e-4f && (rt.HolderOf(m).position - mid).magnitude <= 1e-4f,
            "slot " + F(SlotPos(rt, m)) + ", holder " + F(rt.HolderOf(m).position) + ", want " + F(mid));
        yield return h.Call(new { cmd = "scene.save", name = "g_jc2_mid", overwrite = true, thumb = "placeholder", clock = Clock });
        string path = (string)h.Last?["path"];
        Vector3? saved = null;
        if (IsOk(h.Last) && path != null && File.Exists(path))
        {
            saved = SavedPos(path, m);
            try { File.Delete(path); } catch (Exception) { }
        }
        s.Check("a save mid-move stores the start", saved.HasValue && saved.Value == new Vector3(4, 0, 0) && ModelPos(rt, m) == new Vector3(4, 0, 0),
            "saved " + (saved.HasValue ? F(saved.Value) : "none (" + Short(h.LastText) + ")") + ", model " + F(ModelPos(rt, m)));
        rt.Step(31, Dt30);
        s.Check("the end: the model holds (4, 0, 8), the slot is released, the holder stands there", (ModelPos(rt, m) - new Vector3(4, 0, 8)).magnitude <= 1e-5f && MoveOwner(rt, m) == null &&
            (rt.HolderOf(m).position - new Vector3(4, 0, 8)).magnitude <= 1e-4f, "model " + F(ModelPos(rt, m)) + ", owner " + (MoveOwner(rt, m) ?? "none") + ", holder " + F(rt.HolderOf(m).position));
        s.Check("no history entry", Undo(h) == undo, undo + " -> " + Undo(h));
        float moved = (rt.HolderOf(m).position - start).magnitude;
        s.Check("Present: the node moved at least 5 units", moved >= 5f, F(moved));
    }

    /// <summary>The stored position of a node in a saved scene's scene.json.</summary>
    static Vector3? SavedPos(string path, string id)
    {
        using (var ms = new MemoryStream(File.ReadAllBytes(path)))
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
        {
            var e = zip.GetEntry("scene.json");
            if (e == null) return null;
            string text;
            using (var sr = new StreamReader(e.Open(), Encoding.UTF8)) text = sr.ReadToEnd();
            var doc = JObject.Parse(text);
            var node = doc.Descendants().OfType<JObject>().FirstOrDefault(o => (string)o["id"] == id && o["transform"] != null);
            var p = node?["transform"]?["pos"];
            return p is JArray a && a.Count == 3 ? V3(a) : (Vector3?)null;
        }
    }

    static IEnumerator Jc3(Sink s, GHost h, string m)
    {
        var rt = h.Rt;
        Place(h, m, new Vector3(4, 0, 8), Vector3.zero);
        var cam = rt.GetPart<SceneViewCamera>();
        // a Timeline camera track the follow lies over
        var tk = new JObject { ["t"] = 0, ["target"] = new JArray(0, 10, 0), ["rot"] = new JArray(15, 180, 0), ["dist"] = 25, ["fov"] = 23 };
        yield return h.Call(new JObject { ["cmd"] = "timeline.build", ["duration"] = 30, ["play"] = true, ["tracks"] = new JArray { new JObject { ["type"] = "camera", ["keys"] = new JArray { tk } } } }.ToString(Formatting.None));
        if (!s.Check("timeline.build with a camera track", IsOk(h.Last), Short(h.LastText))) yield break;
        yield return h.Frames(2);
        var tlRot = Quaternion.Euler(15, 180, 0);
        var tlPos = new Vector3(0, 10, 0) + tlRot * new Vector3(0, 0, -25);
        s.Check("Present: the Timeline's camera shows before the follow", rt.Overrides.WinnerOf(null, OverrideChannel.Camera)?.Owner == "timeline" && (cam.Camera.transform.position - tlPos).magnitude <= 1e-3f,
            F(cam.Camera.transform.position) + " want " + F(tlPos));
        // a moving subject
        yield return h.Call(new { cmd = "obj.moveTo", key = m, target = new[] { 4f, 0f, -8f }, stop = 0f, time = 3f });
        var offset = new Vector3(0, 1, 0);
        var rot0 = new Vector3(10, 180, 0);
        var rot1 = new Vector3(30, 90, 0);
        float dist0 = 8, dist1 = 4, seconds = 2;
        yield return h.Call(new { cmd = "camera.follow", target = new { key = m, bone = "cf_J_Spine02", offset = new[] { 0f, 1f, 0f } }, rot = new[] { 10f, 180f, 0f }, rotTo = new[] { 30f, 90f, 0f }, dist = dist0, distTo = dist1, seconds, fov = 30f });
        if (!s.Check("camera.follow answers following and until", IsOk(h.Last) && h.Last["following"]?.Value<bool>() == true && Math.Abs((double)h.Last["until"] - (rt.Clock.Time + seconds)) <= 1e-6, Short(h.LastText))) yield break;
        var bone = rt.FindChara(m).Context.BodyBones["cf_J_Spine02"];
        var sp = bone.position + offset;
        float elapsed = 0f;
        double worstPos = 0, worstRot = 0, worstDist = 0;
        var first = cam.Camera.transform.position;
        Vector3 lastFollowed = first;
        bool winnerOk = rt.Overrides.WinnerOf(null, OverrideChannel.Camera)?.Owner == ScriptedFollow.Owner;
        // the first frame takes the point of its frame, u = 0
        Check3(cam, sp, rot0, dist0, 30f, ref worstPos, ref worstRot);
        yield return h.Frames(5);
        s.Check("the pause holds the follow: 5 frames move the camera by nothing", (cam.Camera.transform.position - first).magnitude == 0f, F((cam.Camera.transform.position - first).magnitude));
        int steps = 0, after = 0;
        bool released = false, back = false;
        for (int k = 1; k <= 70; k++)
        {
            rt.Step(1, Dt30);
            elapsed += Dt30;
            if (elapsed >= seconds)
            {
                after++;
                if (!released)
                {
                    released = !rt.GetPart<ScriptedFollow>().Active && rt.Overrides.WinnerOf(null, OverrideChannel.Camera)?.Owner == "timeline";
                    back = (cam.Camera.transform.position - tlPos).magnitude <= 1e-3f;
                }
                continue;
            }
            steps++;
            var p = bone.position + offset;
            sp = Vector3.LerpUnclamped(sp, p, (float)(1.0 - Math.Exp(-4.0 * Dt30)));
            double u = Smooth(elapsed / (double)seconds);
            var rot = new Vector3((float)(rot0.x + (rot1.x - rot0.x) * u), (float)(rot0.y + (rot1.y - rot0.y) * u), (float)(rot0.z + (rot1.z - rot0.z) * u));
            float dist = (float)(dist0 + (dist1 - dist0) * u);
            Check3(cam, sp, rot, dist, 30f, ref worstPos, ref worstRot);
            var w = rt.Overrides.WinnerOf(null, OverrideChannel.Camera);
            if (w != null && w.HasValue) worstDist = Math.Max(worstDist, Math.Abs(w[7] - dist));
            winnerOk &= w?.Owner == ScriptedFollow.Owner;
            lastFollowed = cam.Camera.transform.position;
        }
        s.Check("every followed step: the camera at s + Euler(lerp(rot, rotTo, u)) . (0, 0, -lerp(dist, distTo, u)), s smoothed by 1 - e^(-4 dt), u = smoothstep",
            steps >= 55 && worstPos <= 2e-3 && worstRot <= 2e-3 && worstDist <= 1e-5, steps + " steps, worst position " + F(worstPos) + ", worst angle " + F(worstRot) + " deg, worst distance " + F(worstDist));
        s.Check("the follow's slot wins over the Timeline's camera while it runs", winnerOk, "");
        s.Check("the end releases the slot: the Timeline's camera shows again", after > 0 && released && back, after + " steps after the end, released " + released + ", back " + back);
        float movedCam = (lastFollowed - first).magnitude;
        s.Check("Present: the camera moved at least 1 unit during the follow", movedCam >= 1f, F(movedCam));
        ScriptedMoves.Of(rt).ReleaseAll();
        yield return h.Call(new { cmd = "camera.follow", seconds = 0 });
        s.Check("seconds 0 answers following false", IsOk(h.Last) && h.Last["following"]?.Value<bool>() == false, Short(h.LastText));
        yield return h.Call(new { cmd = "timeline.clear" });
    }

    static void Check3(SceneViewCamera cam, Vector3 s, Vector3 rot, float dist, float fov, ref double worstPos, ref double worstRot)
    {
        var q = Quaternion.Euler(rot);
        var want = s + q * new Vector3(0, 0, -dist);
        worstPos = Math.Max(worstPos, (cam.Camera.transform.position - want).magnitude);
        worstRot = Math.Max(worstRot, Deg(cam.Camera.transform.rotation, q));
        if (Math.Abs(cam.Camera.fieldOfView - fov) > 1e-4f) worstRot = Math.Max(worstRot, 999);
    }

    static string DofBlock(SceneRuntime rt)
    {
        var d = rt.Model.Settings.Effects.DepthOfField;
        return "enabled " + d.Enabled + ", focus " + (d.Focus ?? "null") + ", focalSize " + F(d.FocalSize) + ", aperture " + F(d.Aperture);
    }

    static IEnumerator Jc4(Sink s, GHost h, string f, string m)
    {
        var rt = h.Rt;
        ScriptedMoves.Of(rt).ReleaseAll();
        Place(h, f, new Vector3(-4, 0, 0), new Vector3(0, 90, 0));
        Place(h, m, new Vector3(4, 0, 0), new Vector3(0, -90, 0));
        var fx = rt.GetPart<SceneEffects>();
        var part = ScriptedDof.Of(rt);
        string block = DofBlock(rt);
        s.Info("the scene's own depth of field: " + block);
        var tk = new JObject { ["t"] = 0, ["on"] = true, ["size"] = 0.3, ["aperture"] = 0.5 };
        yield return h.Call(new JObject { ["cmd"] = "timeline.build", ["duration"] = 30, ["play"] = true, ["tracks"] = new JArray { new JObject { ["type"] = "dof", ["keys"] = new JArray { tk } } } }.ToString(Formatting.None));
        yield return h.Frames(2);
        s.Check("Present: the Timeline's dof track shows", rt.Overrides.WinnerOf(null, OverrideChannel.DepthOfField)?.Owner == "timeline", Short(h.LastText));

        yield return h.Call(new { cmd = "dof", size = 0.7 });
        var r = h.Last;
        s.Check("the first call starts from the value shown (the Timeline's) and sets size", IsOk(r) && (bool)r["dof"]["on"] && Math.Abs((double)r["dof"]["aperture"] - 0.5) <= 1e-6 &&
            Math.Abs((double)r["dof"]["size"] - 0.7) <= 1e-6 && (string)r["dof"]["driver"] == ScriptedDof.Owner && rt.Overrides.WinnerOf(null, OverrideChannel.DepthOfField)?.Owner == ScriptedDof.Owner, Short(h.LastText));
        yield return h.Call(new { cmd = "dof", aperture = 0.4 });
        s.Check("a field left out keeps its value", IsOk(h.Last) && Math.Abs((double)h.Last["dof"]["size"] - 0.7) <= 1e-6 && Math.Abs((double)h.Last["dof"]["aperture"] - 0.4) <= 1e-6, Short(h.LastText));
        yield return h.Call(new { cmd = "dof", on = false });
        bool off = IsOk(h.Last) && part.Value.Enabled == false;
        yield return h.Call(new { cmd = "dof", on = true });
        s.Check("on: false and true set enabled", off && part.Value.Enabled, Short(h.LastText));
        yield return h.Call(new { cmd = "dof", focus = "target" });
        bool pivot1 = part.Value.Focus.Kind == OverrideFocusKind.Pivot;
        yield return h.Call(new JObject { ["cmd"] = "dof", ["focus"] = null }.ToString(Formatting.None));
        s.Check("focus \"target\" and focus null: the camera pivot", pivot1 && part.Value.Focus.Kind == OverrideFocusKind.Pivot && (string)h.Last["dof"]["focus"] == "target", Short(h.LastText));

        // a node, a bone, a point, a distance
        yield return h.Call(new { cmd = "dof", focus = f });
        rt.Step(1, Dt30);
        var pf = fx.PointOf(f).point;
        s.Check("focus a node: {node}, resolved between the eyes", part.Value.Focus.Kind == OverrideFocusKind.Node && part.Value.Focus.Node == f && pf.HasValue && (fx.Dof.FocusPoint - pf.Value).magnitude <= 1e-4f && fx.PointOf(f).kind == "eyes",
            F(fx.Dof.FocusPoint) + " " + fx.PointOf(f).kind);
        var head = rt.FindChara(m).Context.BodyBones["cf_J_Head"];
        yield return h.Call(new { cmd = "dof", focus = new { key = m, bone = "cf_J_Head", offset = new[] { 0f, 0.5f, 0f } } });
        rt.Step(1, Dt30);
        s.Check("focus {key, bone, offset}: {node, bone, offset}, resolved at the bone plus the offset", part.Value.Focus.Kind == OverrideFocusKind.NodeBone && part.Value.Focus.Bone == "cf_J_Head" &&
            (fx.Dof.FocusPoint - (head.position + new Vector3(0, 0.5f, 0))).magnitude <= 1e-4f, F(fx.Dof.FocusPoint));
        yield return h.Call(new { cmd = "dof", focus = new { point = new[] { 1f, 2f, 3f } } });
        rt.Step(1, Dt30);
        s.Check("focus {point}", part.Value.Focus.Kind == OverrideFocusKind.Point && (fx.Dof.FocusPoint - new Vector3(1, 2, 3)).magnitude <= 1e-5f, F(fx.Dof.FocusPoint));
        yield return h.Call(new { cmd = "dof", focus = new { dist = 7f } });
        rt.Step(1, Dt30);
        var ct = rt.GetPart<SceneViewCamera>().Camera.transform;
        s.Check("focus {dist}", part.Value.Focus.Kind == OverrideFocusKind.Dist && (fx.Dof.FocusPoint - (ct.position + ct.forward * 7f)).magnitude <= 1e-3f, F(fx.Dof.FocusPoint));
        yield return h.Call(new { cmd = "dof", focus = 5 });
        s.Check("focus a number: bad_args \"keys are node ids\"", Code(h.Last) == "bad_args" && ((string)h.Last["error"] ?? "").Contains("keys are node ids"), Short(h.LastText));

        // blends with each ease. The two foci are the nodes' points resolved after each step (a focus follows a moving node): the expectation of a step is
        // lerp(point of the old focus, point of the new focus, ease(elapsed / blend)) with both points of that step (the lead's ruling after play2)
        yield return h.Call(new { cmd = "dof", focus = f });
        rt.Step(1, Dt30);
        var pA = fx.PointOf(f).point.Value;
        var pB = fx.PointOf(m).point.Value;
        float apart = (pA - pB).magnitude;
        string[] nodes = { m, f, m, f };
        string prev = f;
        for (int i = 0; i < Eases.Length; i++)
        {
            string e = Eases[i];
            yield return h.Call(new { cmd = "dof", focus = nodes[i], blend = 1.0, ease = e });
            bool startOk = IsOk(h.Last) && part.Blending && part.Value.FocusTo?.Node == nodes[i] && part.Value.FocusBlend == 0;
            double el = 0, worstW = 0, worstP = 0;
            int n = 0;
            while (part.Blending && n < 20)
            {
                rt.Step(1, 0.1f);
                el += 0.1f;
                n++;
                if (!part.Blending) break;
                double want = EaseOf(e, el / 1.0);
                worstW = Math.Max(worstW, Math.Abs(part.Value.FocusBlend - want));
                var pFrom = fx.PointOf(prev).point.Value;
                var pTo = fx.PointOf(nodes[i]).point.Value;
                worstP = Math.Max(worstP, (fx.Dof.FocusPoint - Vector3.LerpUnclamped(pFrom, pTo, (float)want)).magnitude);
            }
            var pEnd = fx.PointOf(nodes[i]).point.Value;
            bool endOk = !part.Blending && part.Value.Focus.Kind == OverrideFocusKind.Node && part.Value.Focus.Node == nodes[i] && part.Value.FocusTo == null && (fx.Dof.FocusPoint - pEnd).magnitude <= 1e-4f;
            s.Check("blend " + e + " over 1 s: the weight follows the ease and the point the blend of the two foci of each step; at the end the new focus", startOk && endOk && n >= 10 && n <= 11 && worstW <= 1e-6 && worstP <= 1e-3,
                n + " steps, worst weight " + F(worstW) + ", worst point " + F(worstP) + ", start " + startOk + ", end " + endOk);
            prev = nodes[i];
        }
        s.Check("Present: the focus moved at least 1 unit between the two nodes", apart >= 1f, F(apart));

        // a blend whose new focus moves: HERO107 is moved 1.5 units by obj.transform half way through the blend
        {
            var mStart = rt.Model.Find(m).Doc.Transform.Pos;
            yield return h.Call(new { cmd = "dof", focus = m, blend = 1.0, ease = "linear" });
            bool startOk = IsOk(h.Last) && part.Blending && part.Value.FocusTo?.Node == m;
            var before = fx.PointOf(m).point.Value;
            double el = 0, worstP = 0;
            float movedMax = 0f;
            int n = 0;
            bool moveOk = false;
            while (part.Blending && n < 20)
            {
                rt.Step(1, 0.1f);
                el += 0.1f;
                n++;
                movedMax = Math.Max(movedMax, (fx.PointOf(m).point.Value - before).magnitude);
                if (!part.Blending) break;
                double want = EaseOf("linear", el / 1.0);
                worstP = Math.Max(worstP, (fx.Dof.FocusPoint - Vector3.LerpUnclamped(fx.PointOf(prev).point.Value, fx.PointOf(m).point.Value, (float)want)).magnitude);
                if (n == 4)
                {
                    yield return h.Call(new { cmd = "obj.transform", key = m, pos = new[] { mStart.X, mStart.Y, mStart.Z + 1.5f } });
                    moveOk = IsOk(h.Last);
                }
            }
            var pEnd = fx.PointOf(m).point.Value;
            bool endOk = !part.Blending && part.Value.Focus.Node == m && part.Value.FocusTo == null && (fx.Dof.FocusPoint - pEnd).magnitude <= 1e-4f;
            s.Check("a blend to a node that moves during it: the point the blend of the two foci of each step; at the end the moved node's point", startOk && moveOk && endOk && n >= 10 && n <= 11 && worstP <= 1e-3,
                n + " steps, worst point " + F(worstP) + ", start " + startOk + ", obj.transform " + moveOk + ", end " + endOk);
            s.Check("Present: the new focus moved at least 1 unit during that blend", movedMax >= 1f, F(movedMax));
            prev = m;
            Place(h, m, new Vector3(4, 0, 0), new Vector3(0, -90, 0));
        }

        // sharp / full, clamps, blur, near, members with no effect, both forms
        // the command reads numbers as floats, as timeline.build does: the expectation takes the inputs as floats (the lead's ruling after play1)
        yield return h.Call(new { cmd = "dof", sharp = 0.2, full = 4.0 });
        TimelineBuildInput.FromRange((double)0.2f, (double)4.0f, out double sz, out double ap);
        s.Check("sharp / full: focal size and aperture by the arithmetic (inputs as floats)", IsOk(h.Last) && Math.Abs(part.Value.FocalSize - sz) <= 1e-12 && Math.Abs(part.Value.Aperture - ap) <= 1e-12 &&
            Math.Abs((double)h.Last["dof"]["sharp"] - 0.2) <= 1e-6 && Math.Abs((double)h.Last["dof"]["full"] - 4.0) <= 1e-6,
            "size " + F(part.Value.FocalSize) + " want " + F(sz) + ", aperture " + F(part.Value.Aperture) + " want " + F(ap) + "; " + Short(h.LastText));
        yield return h.Call(new { cmd = "dof", sharp = 0.3, full = 5.5 });
        TimelineBuildInput.FromRange((double)0.3f, (double)5.5f, out double sz2, out double ap2);
        s.Check("sharp 0.3 / full 5.5 (a pair not read before): focal size and aperture by the arithmetic", IsOk(h.Last) && Math.Abs(part.Value.FocalSize - sz2) <= 1e-12 && Math.Abs(part.Value.Aperture - ap2) <= 1e-12,
            "size " + part.Value.FocalSize.ToString("R") + " want " + sz2.ToString("R") + ", aperture " + part.Value.Aperture.ToString("R") + " want " + ap2.ToString("R"));
        yield return h.Call(new { cmd = "dof", sharp = 1.0, full = 2.0 });
        s.Check("u < 0.02: bad_args", Code(h.Last) == "bad_args", Short(h.LastText));
        yield return h.Call(new { cmd = "dof", size = 5.0, aperture = 1.5 });
        bool hi = IsOk(h.Last) && part.Value.FocalSize == 2 && part.Value.Aperture == 0.99;
        yield return h.Call(new { cmd = "dof", size = -1.0, aperture = -0.5 });
        s.Check("size and aperture clamped to 0-2 and 0-0.99", hi && part.Value.FocalSize == 0 && part.Value.Aperture == 0, Short(h.LastText));
        yield return h.Call(new { cmd = "dof", size = 0.5, aperture = 0.6, blur = 4.0 });
        rt.Step(1, Dt30);
        s.Check("blur: the slot's blur and the renderer's largest blur", part.Value.Blur == 4 && Math.Abs(fx.Dof.MaxBlur1080 - 4f) <= 1e-6f, F(fx.Dof.MaxBlur1080));
        var beforeNear = part.Value;
        yield return h.Call(new { cmd = "dof", near = false });
        s.Check("near: false warns and changes nothing", IsOk(h.Last) && Warned(h.Last, "near: " + ScriptedCommands.NearWarning) && part.Value.Equals(beforeNear), Warnings(h.Last));
        yield return h.Call(new { cmd = "dof", overlap = 1, highRes = true, blurType = 2, samples = 3, visualize = false, maxBlurSize = 4 });
        s.Check("overlap, highRes, blurType, samples, visualize, maxBlurSize: accepted, one warning each, no change", IsOk(h.Last) && WarnCount(h.Last, ScriptedCommands.DofIgnoredWarning) == 6 && part.Value.Equals(beforeNear), Warnings(h.Last));
        yield return h.Call(new { cmd = "dof", size = 0.5, sharp = 0.1 });
        s.Check("size and sharp together: bad_args", Code(h.Last) == "bad_args", Short(h.LastText));

        // release and reset
        yield return h.Call(new { cmd = "dof", release = true });
        s.Check("release: the Timeline's dof track shows again", IsOk(h.Last) && !part.Holding && (string)h.Last["dof"]["driver"] == "timeline" && rt.Overrides.WinnerOf(null, OverrideChannel.DepthOfField)?.Owner == "timeline", Short(h.LastText));
        yield return h.Call(new { cmd = "dof", size = 1.0 });
        bool again = part.Holding && Math.Abs(part.Value.Aperture - 0.5) <= 1e-6;
        yield return h.Call(new { cmd = "dof", reset = true });
        s.Check("reset: released, the Timeline's shows; taken again it starts from the Timeline's value", again && !part.Holding && (string)h.Last["dof"]["driver"] == "timeline", Short(h.LastText));
        yield return h.Call(new { cmd = "timeline.clear" });
        yield return h.Call(new { cmd = "dof", release = true });
        s.Check("without a Timeline the scene's block shows (driver scene)", IsOk(h.Last) && (string)h.Last["dof"]["driver"] == "scene", Short(h.LastText));
        s.Check("the scene's own depth of field never changed", DofBlock(rt) == block, DofBlock(rt) + " / before: " + block);
    }

    // ------------------------------------------------------------ JC6

    /// <summary>
    /// The axis of points written again here from the spec, in double: every k-th point (at most about 4,000), the centre, the covariance, then the axis by
    /// <paramref name="power"/> (50 power iterations from normalise(1, 0.3, 0.2), as the spec) or else the largest eigenvector by Jacobi rotations (an
    /// independent route, for well-conditioned points); turned towards the centre from the anchor; the muzzle the mean within 0.3 of the largest projection.
    /// </summary>
    static void Reference(IReadOnlyList<Vector3> pts, Vector3 anchor, bool power, out Vector3 axis, out Vector3 centre, out Vector3 muzzle, out double ratio)
    {
        int stride = Math.Max(1, (pts.Count + 3999) / 4000);
        var kept = new List<double[]>();
        for (int i = 0; i < pts.Count; i += stride) kept.Add(new double[] { pts[i].x, pts[i].y, pts[i].z });
        var c = new double[3];
        foreach (var p in kept) for (int i = 0; i < 3; i++) c[i] += p[i];
        for (int i = 0; i < 3; i++) c[i] /= kept.Count;
        var a = new double[3, 3];
        foreach (var p in kept)
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++) a[i, j] += (p[i] - c[i]) * (p[j] - c[j]);
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) a[i, j] /= kept.Count;
        var cov = (double[,])a.Clone();
        var v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        for (int sweep = 0; sweep < 50; sweep++)
            for (int p = 0; p < 2; p++)
                for (int q = p + 1; q < 3; q++)
                {
                    if (Math.Abs(a[p, q]) < 1e-300) continue;
                    double th = 0.5 * Math.Atan2(2 * a[p, q], a[q, q] - a[p, p]);
                    double cs = Math.Cos(th), sn = Math.Sin(th);
                    for (int k = 0; k < 3; k++) { double kp = a[k, p], kq = a[k, q]; a[k, p] = cs * kp - sn * kq; a[k, q] = sn * kp + cs * kq; }
                    for (int k = 0; k < 3; k++) { double pk = a[p, k], qk = a[q, k]; a[p, k] = cs * pk - sn * qk; a[q, k] = sn * pk + cs * qk; }
                    for (int k = 0; k < 3; k++) { double kp = v[k, p], kq = v[k, q]; v[k, p] = cs * kp - sn * kq; v[k, q] = sn * kp + cs * kq; }
                }
        var ev = new[] { a[0, 0], a[1, 1], a[2, 2] };
        int best = 0;
        for (int i = 1; i < 3; i++) if (ev[i] > ev[best]) best = i;
        var sorted = ev.OrderByDescending(x => x).ToArray();
        ratio = sorted[0] > 0 ? sorted[1] / sorted[0] : 1;
        var ax = new double[] { v[0, best], v[1, best], v[2, best] };
        if (power)
        {
            ax = new double[] { 1, 0.3, 0.2 };
            Norm(ax);
            for (int it = 0; it < 50; it++)
            {
                var n = new double[3];
                for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) n[i] += cov[i, j] * ax[j];
                if (!Norm(n)) break;
                ax = n;
            }
        }
        else Norm(ax);
        if ((c[0] - anchor.x) * ax[0] + (c[1] - anchor.y) * ax[1] + (c[2] - anchor.z) * ax[2] < 0) for (int i = 0; i < 3; i++) ax[i] = -ax[i];
        double top = kept.Max(p => p[0] * ax[0] + p[1] * ax[1] + p[2] * ax[2]);
        var band = kept.Where(p => p[0] * ax[0] + p[1] * ax[1] + p[2] * ax[2] >= top - 0.3).ToList();
        axis = new Vector3((float)ax[0], (float)ax[1], (float)ax[2]);
        centre = new Vector3((float)c[0], (float)c[1], (float)c[2]);
        muzzle = new Vector3((float)band.Average(p => p[0]), (float)band.Average(p => p[1]), (float)band.Average(p => p[2]));
    }

    static bool Norm(double[] x)
    {
        double l = Math.Sqrt(x[0] * x[0] + x[1] * x[1] + x[2] * x[2]);
        if (!(l > 1e-12)) return false;
        for (int i = 0; i < 3; i++) x[i] /= l;
        return true;
    }

    static double Cos(Vector3 a, Vector3 b) => ((double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z) / (a.magnitude * (double)b.magnitude);

    static IEnumerator Jc6(Sink s, GHost h, string f, string m)
    {
        var rt = h.Rt;
        ScriptedMoves.Of(rt).ReleaseAll();
        Place(h, f, new Vector3(-4, 0, 0), new Vector3(0, 90, 0));
        Place(h, m, new Vector3(4, 0, 0), new Vector3(0, -90, 0));
        yield return h.Frames(2);

        // hand-made points, against the eigenvector route (an independent one)
        var dir = new Vector3(1, 2, 0.5f).normalized;
        var side = Vector3.Cross(dir, Vector3.up).normalized;
        var line = new List<Vector3>();
        for (int i = 0; i <= 400; i++) line.Add(dir * (0.5f + 0.0123f * i) + side * (0.02f * Mathf.Sin(i * 0.7f)));
        var got = AccessoryAxis.Compute(line, Vector3.zero);
        Reference(line, Vector3.zero, false, out var rAxis, out var rCentre, out var rMuzzle, out double rRatio);
        s.Check("hand-made line: the axis, centre and muzzle against the eigenvector route", got != null && Cos(got.Axis, rAxis) >= 1 - 1e-6 && (got.Center - rCentre).magnitude <= 1e-5f &&
            (got.Muzzle - rMuzzle).magnitude <= 1e-4f && Cos(got.Axis, dir) >= 0.9999, got == null ? "null" : "axis " + F(got.Axis) + " ref " + F(rAxis) + ", muzzle " + F(got.Muzzle) + " ref " + F(rMuzzle) +
            ", band " + got.MuzzlePoints + ", eigenvalue ratio " + F(rRatio));
        var flipped = AccessoryAxis.Compute(line, dir * 10f);
        s.Check("the anchor on the far side turns the axis", flipped != null && Cos(flipped.Axis, dir) <= -0.9999, flipped == null ? "null" : F(flipped.Axis));
        var many = new List<Vector3>();
        for (int i = 0; i < 10000; i++) many.Add(dir * (0.00123f * i) + side * (0.05f * Mathf.Cos(i)));
        var gm = AccessoryAxis.Compute(many, Vector3.zero);
        Reference(many, Vector3.zero, false, out var mAxis, out _, out var mMuzzle, out _);
        s.Check("10,000 points: every 3rd kept (3,334), the same as the reference", gm != null && gm.Points == 3334 && gm.Read == 10000 && Cos(gm.Axis, mAxis) >= 1 - 1e-6 && (gm.Muzzle - mMuzzle).magnitude <= 1e-4f,
            gm == null ? "null" : gm.Points + " kept, muzzle " + F(gm.Muzzle) + " ref " + F(mMuzzle));
        s.Check("fewer than 3 points: none", AccessoryAxis.Compute(new[] { Vector3.zero, Vector3.one }, Vector3.zero) == null, "");
        var box = new List<Vector3>();
        AccessoryAxis.BoundsPoints(new Bounds(new Vector3(1, 2, 3), new Vector3(2, 4, 6)), Matrix4x4.identity, box);
        s.Check("a bounds gives 88 points: 11 on each line from the centre to each corner", box.Count == 88 && box.Count(p => p == new Vector3(1, 2, 3)) == 8 && box.Contains(new Vector3(2, 4, 6)) && box.Contains(new Vector3(0, 0, 0)), box.Count.ToString());

        // the targets' slots
        var judged = new List<(string who, string id, int slot, AccessoryAxisResult a)>();
        int skinned = 0, plain = 0, bounds = 0, mismatched = 0;
        var refBad = new List<string>();
        foreach (var (who, id) in new[] { ("BR-Chan", f), ("HERO107", m) })
        {
            var c = rt.FindChara(id);
            var list = c?.Context?.Accessories;
            if (list == null) { s.Check(who + ": the accessory slots are readable", false, ""); continue; }
            foreach (var slot in list)
            {
                if (slot == null || slot.IsEmpty || !slot.Object) continue;
                yield return h.Call(new { cmd = "chara.accessoryAxis", key = id, slot = slot.Slot });
                var r = h.Last;
                var a = AccessoryAxis.Of(c, slot.Slot, out string err);
                if (a == null)
                {
                    s.Info(who + " slot " + slot.Slot + ": " + err + "; the command answers " + Code(r));
                    if (Code(r) != "not_found") mismatched++;
                    continue;
                }
                if (!IsOk(r) || (V3(r["axis"]) - a.Axis).magnitude > 1e-6f || (V3(r["muzzle"]) - a.Muzzle).magnitude > 1e-5f || (V3(r["center"]) - a.Center).magnitude > 1e-5f || (V3(r["anchor"]) - a.Anchor).magnitude > 1e-5f)
                    mismatched++;
                var pts = new List<Vector3>();
                AccessoryAxis.Gather(slot.Object, pts, new AccessoryAxisResult(), SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null);
                Reference(pts, a.Anchor, true, out var ax, out var cz, out var mz, out double ratio);
                Reference(pts, a.Anchor, false, out var ex, out _, out _, out _);
                double align = Cos(ax, a.Axis);
                if (pts.Count != a.Read || align < 1 - 1e-6 || (cz - a.Center).magnitude > 1e-4f || (mz - a.Muzzle).magnitude > 1e-4f)
                    refBad.Add(who + " " + slot.Slot + ": points " + pts.Count + "/" + a.Read + ", cos " + F(align) + ", muzzle off " + F((mz - a.Muzzle).magnitude));
                s.Info(who + " slot " + slot.Slot + ": the eigenvector route gives |cos| " + F(Math.Abs(Cos(ex, a.Axis))) + " (second / first eigenvalue " + F(ratio) + ")");
                if (a.Skinned > 0) skinned++;
                if (a.Plain > 0) plain++;
                if (a.Bounds > 0) bounds++;
                float reach = (a.Muzzle - a.Anchor).magnitude;
                s.Info(who + " slot " + slot.Slot + ": " + a.Read + " points (" + a.Points + " kept), skinned " + a.Skinned + ", readable " + a.Plain + ", bounds " + a.Bounds + ", muzzle to anchor " + F(reach) +
                    " (INFO, no floor), axis " + F(a.Axis) + ", band " + a.MuzzlePoints);
                s.Info(who + " slot " + slot.Slot + " renderers: " + (a.Paths != null && a.Paths.Count > 0 ? string.Join("; ", a.Paths) : "none"));
                judged.Add((who, id, slot.Slot, a));
            }
        }
        var hero = rt.FindChara(m);
        var s2 = AccessoryAxis.SlotOf(hero, 2);
        s.Info("HERO107 accessory slot 2: " + (s2 == null || s2.IsEmpty || !s2.Object ? "absent" : "present (" + (s2.Object ? s2.Object.name : "no object") + ")"));
        s.Check("the command answers what the axis reads, for every slot", judged.Count > 0 && mismatched == 0, judged.Count + " slots, " + mismatched + " differ");
        s.Check("every slot: the points, the axis, the centre and the muzzle as the spec's arithmetic written again in the test (cos >= 1 - 1e-6, within 1e-4)", judged.Count > 0 && refBad.Count == 0, string.Join("; ", refBad.Take(6)));
        var far = judged.OrderByDescending(x => (x.a.Muzzle - x.a.Anchor).magnitude).FirstOrDefault();
        s.Info("the targets' largest muzzle-to-anchor distance (INFO, no floor): " + (far.a != null ? far.who + " slot " + far.slot + ": " + F((far.a.Muzzle - far.a.Anchor).magnitude) : "no slot"));

        // test accessories under a target's slot anchor, read as that slot (the lead's ruling after play1): the slot's own renderers are switched off while
        // a test object is its only active renderer, and come back after
        bool copyOk = false;
        var host = judged.FirstOrDefault(x => x.id == f && AccessoryAxis.SlotOf(rt.FindChara(f), x.slot)?.Object?.GetComponentsInChildren<MeshFilter>(true).Any(mf => mf.sharedMesh && mf.sharedMesh.isReadable) == true);
        if (s.Check("a BR-Chan slot with a readable mesh to copy", host.a != null, ""))
        {
            var fc = rt.FindChara(f);
            var slotObj = AccessoryAxis.SlotOf(fc, host.slot).Object;
            var anchorT = slotObj.transform.parent;
            var own = slotObj.GetComponentsInChildren<Renderer>(true).Select(x => (r: x, on: x.enabled)).ToList();
            var source = slotObj.GetComponentsInChildren<MeshFilter>(true).First(mf => mf.sharedMesh && mf.sharedMesh.isReadable);
            GameObject test = null;
            Mesh copy = null, bar = null;
            try
            {
                foreach (var x in own) x.r.enabled = false;
                // the bounds route: a copy of the accessory's mesh made unreadable
                copy = UnityEngine.Object.Instantiate(source.sharedMesh);
                copy.name = "g6 unreadable copy";
                copy.UploadMeshData(true);
                test = new GameObject("g6 unreadable copy");
                test.transform.SetParent(slotObj.transform, false);
                test.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                test.AddComponent<MeshFilter>().sharedMesh = copy;
                test.AddComponent<MeshRenderer>();
                var want = new List<Vector3>();
                AccessoryAxis.BoundsPoints(copy.bounds, test.transform.localToWorldMatrix, want);
                yield return h.Call(new { cmd = "chara.accessoryAxis", key = f, slot = host.slot });
                var rc = h.Last;
                var ac = AccessoryAxis.Of(fc, host.slot, out string errc);
                var pc = new List<Vector3>();
                AccessoryAxis.Gather(slotObj, pc, new AccessoryAxisResult(), SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null);
                bool same = pc.Count == want.Count && pc.Select((q, i) => (q - want[i]).magnitude).DefaultIfEmpty(float.MaxValue).Max() <= 1e-5f;
                Vector3 cAxis = default, cMuz = default;
                if (pc.Count >= 3) Reference(pc, anchorT.position, true, out cAxis, out _, out cMuz, out _);
                s.Info("the copy of BR-Chan slot " + host.slot + "'s mesh (" + source.sharedMesh.name + "): isReadable " + copy.isReadable + "; renderers: " + (ac?.Paths != null ? string.Join("; ", ac.Paths) : errc));
                copyOk = ac != null && !copy.isReadable && ac.Bounds == 1 && ac.Skinned == 0 && ac.Plain == 0 && ac.Read == 88 && same && IsOk(rc) &&
                    (V3(rc["muzzle"]) - ac.Muzzle).magnitude <= 1e-5f && (V3(rc["axis"]) - ac.Axis).magnitude <= 1e-6f && Cos(cAxis, ac.Axis) >= 1 - 1e-6 && (cMuz - ac.Muzzle).magnitude <= 1e-4f;
                s.Check("an unreadable mesh (the copy, isReadable False) is read by its bounds: 88 points, 11 on each line from the centre to each corner; the command, the axis and the test's arithmetic agree", copyOk,
                    ac == null ? errc : "isReadable " + copy.isReadable + ", bounds " + ac.Bounds + ", skinned " + ac.Skinned + ", readable " + ac.Plain + ", points " + ac.Read + ", the 88 points " + same + ", reply " + Short(h.LastText));
                UnityEngine.Object.DestroyImmediate(test);
                test = null;

                // a straight bar of known geometry: about 3 units long, turned, from 0.1 to 3.1 units out of the slot's place
                bar = new Mesh { name = "g6 bar" };
                float hw = 0.05f, z0 = 0.1f, z1 = 3.1f;
                var bv = new List<Vector3>();
                foreach (var z in new[] { z0, z1 }) foreach (var y in new[] { -hw, hw }) foreach (var xx in new[] { -hw, hw }) bv.Add(new Vector3(xx, y, z));
                bar.SetVertices(bv);
                bar.SetTriangles(new[] { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 }, 0);
                test = new GameObject("g6 bar");
                test.transform.SetParent(slotObj.transform, false);
                var ls = test.transform.lossyScale;
                test.transform.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
                test.transform.localRotation = Quaternion.Euler(20f, 35f, 10f);
                test.AddComponent<MeshFilter>().sharedMesh = bar;
                test.AddComponent<MeshRenderer>();
                var nearEnd = test.transform.TransformPoint(new Vector3(0, 0, z0));
                var farEnd = test.transform.TransformPoint(new Vector3(0, 0, z1));
                var barDir = (farEnd - nearEnd).normalized;
                yield return h.Call(new { cmd = "chara.accessoryAxis", key = f, slot = host.slot });
                var rb = h.Last;
                if (s.Check("the bar read through chara.accessoryAxis", IsOk(rb), Short(h.LastText)))
                {
                    var mz = V3(rb["muzzle"]);
                    var ax = V3(rb["axis"]);
                    var an = V3(rb["anchor"]);
                    double angle = Math.Acos(Math.Max(-1, Math.Min(1, Cos(ax, barDir)))) * 180 / Math.PI;
                    s.Check("the bar: the muzzle within 0.05 units of its far end, the axis within 1 degree of it", (mz - farEnd).magnitude <= 0.05f && angle <= 1.0,
                        "muzzle off by " + F((mz - farEnd).magnitude) + ", axis off by " + F(angle) + " deg, the bar's length " + F((farEnd - nearEnd).magnitude) + ", anchor " + F(an) + " (the slot's parent " + F(anchorT.position) + ")");
                    float reach = (mz - an).magnitude;
                    s.Check("Present: the muzzle lies at least 1 unit from the anchor (the bar)", reach >= 1f, F(reach));
                }
            }
            finally
            {
                if (test) UnityEngine.Object.DestroyImmediate(test);
                if (copy) UnityEngine.Object.DestroyImmediate(copy);
                if (bar) UnityEngine.Object.DestroyImmediate(bar);
                foreach (var x in own) if (x.r) x.r.enabled = x.on;
            }
            var back = AccessoryAxis.Of(fc, host.slot, out _);
            s.Check("the slot reads as before once the test objects are gone", back != null && (back.Muzzle - host.a.Muzzle).magnitude <= 1e-5f && back.Read == host.a.Read, back == null ? "none" : F(back.Muzzle) + " / " + F(host.a.Muzzle));
        }
        s.Check("the routes: a skinned mesh and a readable mesh on the targets, a mesh read by its bounds (the unreadable copy)", skinned >= 1 && plain >= 1 && copyOk,
            "skinned " + skinned + ", readable " + plain + ", bounds on the targets " + bounds + ", the copy by its bounds " + copyOk + " (graphics " + SystemInfo.graphicsDeviceType + ")");
        if (far.a == null) yield break;

        // from: {key, accessory}
        string other = far.id == f ? m : f;
        var from = new { key = far.id, accessory = far.slot };
        yield return h.Call(new { cmd = "fx.shoot", from, target = new { key = other, bone = "cf_J_Spine02" } });
        s.Check("fx.shoot from {key, accessory}: the shot starts at the muzzle", IsOk(h.Last) && (V3(h.Last["muzzle"]) - far.a.Muzzle).magnitude <= 1e-4f, Short(h.LastText));
        yield return h.Call(new { cmd = "wet.attach", key = other, from, target = new { key = other, bone = "cf_J_Spine03" }, preset = "water" });
        s.Check("wet.attach from {key, accessory} answers ok", IsOk(h.Last), Short(h.LastText));
        yield return h.Call(new { cmd = "wet.stain", key = other, name = "g6", from, target = new { key = other, bone = "cf_J_Spine03" } });
        s.Check("wet.stain from {key, accessory} answers ok", IsOk(h.Last), Short(h.LastText));
        yield return h.Call(new { cmd = "wet.remove", key = other, all = true });
        yield return h.Call(new { cmd = "fx.shoot", from = new { key = far.id, accessory = 99 }, target = new { key = other } });
        s.Check("an empty slot: not_found", Code(h.Last) == "not_found", Short(h.LastText));
        yield return h.Call(new { cmd = "fx.shoot", from = new { key = far.id, accessory = "a" }, target = new { key = other } });
        s.Check("an accessory that is not a number: bad_args", Code(h.Last) == "bad_args", Short(h.LastText));
        yield return h.Call(new { cmd = "ragdoll.release" });
    }
}

// The parts ScriptTest runs (found by name): the headless groups on a host of its own, the play groups in the app started for them.
public static partial class ScriptTest
{
    static ScriptTestG.Sink GSink() => new ScriptTestG.Sink { Check = Check, Info = Info, Warn = Warn, Measure = Measure, Group = Group, Open = POpen };

    static void Headless_G_Commands()
    {
        if (!WantAny("JC1", "JC5", "JC7", "JC8")) return;
        ScriptTestG.Headless(GSink(), () => new ScriptTestG.GEditHost { App = NewHost(0x69c0ffeeUL).App }, femaleRel);
    }

    static IEnumerator Play_G_Commands()
    {
        if (!WantAny("JC2", "JC3", "JC4", "JC6")) yield break;
        yield return PStartApp();
        var app = AppCore.Current;
        if (app == null || app.Stage != AppStage.Ready || app.Bridge == null || app.Runtime == null) yield break;
        yield return ScriptTestG.Play(GSink(), new ScriptTestG.GPlayHost { App = app }, femaleRel, maleRel);
        yield return PStopApp();
    }
}

// ConstraintTest, the override layer's new paths and the driven-bone stages (Phase 6.5 package O), edit mode:
//   CL1  the five paths parse, their kinds, the refusals, Accepts, the canonical text
//   CL2  priority: 10 over the Timeline's 0 and a plugin's 0, under a plugin's 11, equal by acquisition
//   CL3  ValueBelow, and 0 B
//   CL4  a release restores each channel, as the last column of the paths' table says
//   CL5  overrides.list rows; effective for a stale bone, an IK group that is off, a writer after the stage
//   CL6  a plugin's slot on a bone and on an IK target
//   CO1  the phases -99, 440, 955 only with a driven bone; no other stage changes; a node's slot adds none
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Api.Plugins;
using HS2Next.App;
using HS2Next.Motion;
using HS2Next.Motion.AdvancedPose;
using HS2Next.Motion.Ik;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Overrides;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class ConstraintTest
{
    const string Owner = OverrideLayer.ConstraintsOwner;
    const int Prio = OverrideLayer.ConstraintsPriority;
    /// <summary>A bone no clip of the idle row binds, on both targets.</summary>
    internal const string FreeBone = "cf_J_EarBase_s_L";

    static void Headless_B_Layer()
    {
        Group("CL1", "the five paths", CL1);
        Group("CL2", "priority", CL2);
        Group("CL3", "ValueBelow", CL3);
        Group("CL4", "a release restores", CL4);
        Group("CL5", "overrides.list and effective", CL5);
        Group("CL6", "a plugin's slot on a bone and on an IK target", CL6);
        Group("CO1", "the stages of driven bones", CO1);
    }

    /// <summary>A host with the female target, standing on the idle row, stepped by the frames of the host.</summary>
    static (Host h, string key) LayerHost(string tag, bool anime = true)
    {
        var h = NewHost();
        var r = h.Ok(new { cmd = "chara.load", path = femaleRel, sex = "female" }, tag + ": chara.load");
        string key = Key(r);
        if (anime) h.Ok(new { cmd = "chara.anime", key, group = 19, category = 0, no = 0, time = 0f, speed = 0f }, tag + ": the idle row");
        h.Frames(3);
        return (h, key);
    }

    static Transform BoneOf(Host h, string key, string address) => AdvancedPoseResolver.Resolve(h.Rt.FindChara(key), address);

    static double Dist(Vector3 a, Vector3 b) => (a - b).magnitude;
    static double Angle(Quaternion a, Quaternion b) => Deg(a, b);

    static void CL1()
    {
        var bones = new[] { ("pos", OverrideChannel.BonePos, OverrideValueKind.Vector3), ("rot", OverrideChannel.BoneRot, OverrideValueKind.Rotation), ("scale", OverrideChannel.BoneScale, OverrideValueKind.Vector3) };
        foreach (var address in new[] { "cf_J_Hand_R", "hair:0/cf_J_hair_x", "clothes:3/joint#2", "acc:0123456789abcdef0123456789abcdef/N_x" })
            foreach (var (member, ch, kind) in bones)
            {
                string text = OverridePath.BoneText(address, member);
                bool ok = OverridePath.TryParse(text, out var p, out var e);
                Check(text + " parses", ok, e);
                if (!ok) continue;
                Check(text + ": channel, kind, address, canonical text", p.Channel == ch && p.Kind == kind && p.Bone == address && p.Text == text && p.IsBone && !p.IsIkTarget && !p.SceneWide);
            }
        foreach (var name in IkTargets.Names)
        {
            string text = OverridePath.IkText(name, "pos");
            bool ok = OverridePath.TryParse(text, out var p, out var e);
            Check(text + " parses", ok && p.Channel == OverrideChannel.IkPos && p.Kind == OverrideValueKind.Vector3 && p.Index == Array.IndexOf(IkTargets.Names, name) && p.IsIkTarget, e);
            string rot = OverridePath.IkText(name, "rot");
            bool hasRot = IkGroupInfo.UsesRotation((IkTargetId)Array.IndexOf(IkTargets.Names, name));
            bool okr = OverridePath.TryParse(rot, out var q, out var er);
            Check(rot + (hasRot ? " parses" : " is refused: no rotation"), okr == hasRot && (!okr || (q.Channel == OverrideChannel.IkRot && q.Kind == OverrideValueKind.Rotation)), er);
        }
        foreach (var bad in new[]
        {
            "chara.bone[\"cf_J_Hand_R\"]", "chara.bone[\"cf_J_Hand_R\"].width", "chara.bone[\"\"].pos", "chara.bone[\"hair:0/joint#0\"].pos", "chara.bone[\"a/b\"].pos", "chara.bone[\"hair:9/x\"].pos",
            "chara.bone.pos", "chara.ik.targets.Nobody.pos", "chara.ik.targets.LeftHand.scale", "chara.ik.targets.LeftHand", "chara.ik.targets",
        })
            Check("refused: " + bad, !OverridePath.TryParse(bad, out _, out var why) && !string.IsNullOrEmpty(why), why);
        var bp = OverridePath.Parse(OverridePath.BoneText("cf_J_Hand_R", "pos"));
        var ip = OverridePath.Parse(OverridePath.IkText("LeftHand", "pos"));
        foreach (NodeKind k in Enum.GetValues(typeof(NodeKind)))
            Check("Accepts " + k, bp.Accepts(k) == (k == NodeKind.Chara) && ip.Accepts(k) == (k == NodeKind.Chara));
        Check("a model change never touches a bone's channel", !bp.TouchedBy("chara") && !bp.TouchedBy(null) && !bp.TouchedBy("chara.fk"));
        Check("an IK target's edits touch its channel, another target's do not", ip.TouchedBy("chara.ik.targets.LeftHand") && ip.TouchedBy("chara.ik") && !ip.TouchedBy("chara.ik.targets.RightHand"));
        var same = OverridePath.Parse("chara.bone[\"cf_J_Hand_R\"].pos");
        Check("one instance per canonical text", ReferenceEquals(same, bp));
        var (h, key) = LayerHost("CL1");
        try
        {
            var rt = h.Rt;
            var folder = h.Session.AddFolder().NewIds[0];
            foreach (var path in new[] { bp.Text, ip.Text })
            {
                bool threw = false;
                try { rt.Overrides.Acquire(Owner, folder, path, Prio); }
                catch (ArgumentException e) { threw = e.Message.Contains("not a path of a folder"); }
                Check(path + " on a folder: ArgumentException at Acquire", threw);
            }
            bool bad = false;
            try { rt.Overrides.Acquire(Owner, key, "chara.bone[\"hair:0/joint#0\"].pos", Prio); }
            catch (ArgumentException) { bad = true; }
            Check("a bad address: ArgumentException at Acquire", bad);
            var s = rt.Overrides.Acquire(Owner, key, bp.Text, Prio);
            Check("a character takes the path", s != null && s.Path == bp.Text && s.Kind == OverrideValueKind.Vector3);
            s.Release();
            Check("the owner's name", rt.Overrides.OwnerName(Owner) == "Constraints", rt.Overrides.OwnerName(Owner));
        }
        finally { h.Dispose(); }
    }

    static void CL2()
    {
        var (h, key) = LayerHost("CL2");
        try
        {
            var ov = h.Rt.Overrides;
            string path = OverridePath.BoneText(FreeBone, "pos");
            var tl = ov.Acquire(OverrideLayer.TimelineOwner, key, path, 0);
            var pl = ov.Acquire("test.plugin", key, path, 0);
            tl.Set(1, 0, 0);
            pl.Set(2, 0, 0);
            Check("equal priority: the later acquired wins", ov.Winner(key, path) == pl);
            var c = ov.Acquire(Owner, key, path, Prio);
            c.Set(3, 0, 0);
            Check("10 over the Timeline's 0 and a plugin's 0", ov.Winner(key, path) == c);
            var top = ov.Acquire("test.top", key, path, 11);
            top.Set(4, 0, 0);
            Check("under a plugin's 11", ov.Winner(key, path) == top);
            var tie = ov.Acquire("test.tie", key, path, Prio);
            top.Release();
            tie.Set(5, 0, 0);
            Check("at 10 the later acquired wins, as everywhere", ov.Winner(key, path) == tie);
            tie.Release();
            Check("back to the constraints", ov.Winner(key, path) == c);
            ov.ReleaseOwner(Owner); ov.ReleaseOwner("test.plugin"); ov.ReleaseOwner(OverrideLayer.TimelineOwner);
        }
        finally { h.Dispose(); }
    }

    static void CL3()
    {
        var (h, key) = LayerHost("CL3");
        try
        {
            var ov = h.Rt.Overrides;
            string path = OverridePath.BoneText(FreeBone, "rot");
            var into = new double[9];
            var c = ov.Acquire(Owner, key, path, Prio);
            Check("no other slot: false", !ov.ValueBelow(c, into));
            var tl = ov.Acquire(OverrideLayer.TimelineOwner, key, path, 0);
            Check("a slot without a value does not count", !ov.ValueBelow(c, into));
            tl.Set(0, 0, 0.6, 0.8);
            bool got = ov.ValueBelow(c, into);
            Check("the next slot's value", got && into[2] == tl[2] && into[3] == tl[3], string.Join(",", into.Take(4)));
            var pl = ov.Acquire("test.plugin", key, path, 5);
            pl.Set(0.6, 0, 0, 0.8);
            got = ov.ValueBelow(c, into);
            Check("the best other slot, by priority", got && into[0] == pl[0] && into[3] == pl[3]);
            var top = ov.Acquire("test.top", key, path, 11);
            top.Set(0, 0.6, 0, 0.8);
            Check("a slot above counts as well: the value is what shows without this one", ov.ValueBelow(c, into) && into[1] == top[1]);
            Check("the same question from the slot above gives the next one down", ov.ValueBelow(top, into) && into[0] == pl[0]);
            top.Release(); pl.Release();
            ov.ValueBelow(c, into);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) ov.ValueBelow(c, into);
            long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
            Check("0 B over 10,000 calls", alloc == 0, alloc + " B");
            tl.Release(); c.Release();
        }
        finally { h.Dispose(); }
    }

    static void CL4()
    {
        var (h, key) = LayerHost("CL4");
        try
        {
            var ov = h.Rt.Overrides;
            var t = BoneOf(h, key, FreeBone);
            t.GetLocalPositionAndRotation(out var p0, out var r0);
            var s0 = t.localScale;
            var sp = ov.Acquire(Owner, key, OverridePath.BoneText(FreeBone, "pos"), Prio);
            var sr = ov.Acquire(Owner, key, OverridePath.BoneText(FreeBone, "rot"), Prio);
            var ss = ov.Acquire(Owner, key, OverridePath.BoneText(FreeBone, "scale"), Prio);
            var vp = p0 + new Vector3(0.1f, 0.2f, 0.3f);
            var vr = r0 * Quaternion.Euler(30f, 40f, 50f);
            var vs = new Vector3(1.5f, 0.5f, 2f);
            sp.Set(vp.x, vp.y, vp.z); sr.Set(vr.x, vr.y, vr.z, vr.w); ss.Set(vs.x, vs.y, vs.z);
            Check("a Set writes nothing to the bone", Dist(t.localPosition, p0) == 0 && Deg(t.localRotation, r0) == 0 && t.localScale.Equals(s0));
            h.Frames(1);
            Check("the next step shows the three channels", Dist(t.localPosition, vp) <= 1e-6 && Angle(t.localRotation, vr) <= 0.01 && Dist(t.localScale, vs) <= 1e-6, t.localPosition + " " + t.localScale);
            h.Frames(5);
            Check("five more steps hold them", Dist(t.localPosition, vp) <= 1e-6 && Angle(t.localRotation, vr) <= 0.01 && Dist(t.localScale, vs) <= 1e-6);
            sp.Release();
            h.Frames(1);
            Check("position released: the pose's value in the next step, the others stay", Dist(t.localPosition, p0) <= 1e-6 && Angle(t.localRotation, vr) <= 0.01 && Dist(t.localScale, vs) <= 1e-6, t.localPosition.ToString("F6"));
            sr.Clear();
            h.Frames(1);
            Check("rotation cleared: the pose's value in the next step", Angle(t.localRotation, r0) <= 1e-4 && Dist(t.localScale, vs) <= 1e-6);
            ss.Release();
            sr.Release();
            h.Frames(1);
            Check("scale released: the pose's value in the next step", Dist(t.localScale, s0) <= 1e-6);
            var c = h.Rt.FindChara(key);
            h.Frames(1);
            Check("no driven state left after the last channel goes and its restore ran", DrivenPoseDriver.Of(c) == null && !c.Motion.Stages.Any(s => s.Name.StartsWith("bones.driven", StringComparison.Ordinal)));

            // an IK target: the model's value or the snapshot at the next solve
            h.Ok(new { cmd = "ik.enable", key, on = true }, "ik.enable");
            h.Frames(3);
            var ik = IkDriver.Of(c);
            var hand = c.Context.BodyBones["cf_J_Hand_R"];
            int ti = (int)IkTargetId.RightHand;
            var model = ik.Effective(IkTargetId.RightHand);
            var h0 = hand.position;
            var ipos = ov.Acquire(Owner, key, OverridePath.IkText("RightHand", "pos"), Prio);
            var irot = ov.Acquire(Owner, key, OverridePath.IkText("RightHand", "rot"), Prio);
            var v = model.Pos + new Vector3(0.1f, 0.05f, 0.1f);
            var q = Quaternion.Euler(model.Rot) * Quaternion.Euler(0f, 30f, 0f);
            ipos.Set(v.x, v.y, v.z);
            irot.Set(q.x, q.y, q.z, q.w);
            Check("Set writes TargetOverrides at once", ik.Stage.TargetOverrides[ti].HasPos && ik.Stage.TargetOverrides[ti].HasRot && Dist(ik.Stage.TargetOverrides[ti].Pos, v) == 0);
            Check("Targets and Effective keep the model's value", ik.Effective(IkTargetId.RightHand).Pos.Equals(model.Pos) && ik.Effective(IkTargetId.RightHand).Rot.Equals(model.Rot));
            Check("Shown gives the override", ik.Shown(IkTargetId.RightHand).Pos == v);
            h.Frames(1);
            double moved = Dist(hand.position, h0);
            Check("the next solve reaches it", moved > 0.05 && Dist(hand.position, c.Root.transform.TransformPoint(v)) <= 1e-4, $"moved {moved:G4}, off the target by {Dist(hand.position, c.Root.transform.TransformPoint(v)):G3}");
            Check("the hand's rotation follows the override", Angle(Quaternion.Inverse(c.Root.transform.rotation) * hand.rotation, q) <= 0.05, Angle(Quaternion.Inverse(c.Root.transform.rotation) * hand.rotation, q).ToString("G3"));
            ipos.Release();
            irot.Release();
            Check("released: the override is off at once", !ik.Stage.TargetOverrides[ti].HasPos && !ik.Stage.TargetOverrides[ti].HasRot);
            h.Frames(1);
            Check("the next solve is back on the model's value", Dist(hand.position, h0) <= 1e-4, Dist(hand.position, h0).ToString("G3"));
        }
        finally { h.Dispose(); }
    }

    static JObject RowOf(JObject list, string key, string path) =>
        (list["overrides"] as JArray)?.OfType<JObject>().FirstOrDefault(o => (string)o["key"] == key && (string)o["path"] == path);

    static void CL5()
    {
        var (h, key) = LayerHost("CL5");
        try
        {
            var ov = h.Rt.Overrides;
            string good = OverridePath.BoneText(FreeBone, "rot"), stale = OverridePath.BoneText("no_such_bone_anywhere", "pos"), ikp = OverridePath.IkText("LeftHand", "pos");
            var s1 = ov.Acquire(Owner, key, good, Prio);
            var s2 = ov.Acquire(Owner, key, stale, Prio);
            var s3 = ov.Acquire(Owner, key, ikp, Prio);
            s1.Set(0, 0, 0, 1); s2.Set(0, 0, 0); s3.Set(0, 0, 0);
            h.Frames(2);
            var list = h.Ok(new { cmd = "overrides.list", key }, "overrides.list");
            var a = RowOf(list, key, good); var b = RowOf(list, key, stale); var c = RowOf(list, key, ikp);
            Check("the three rows, with no change to the command", a != null && b != null && c != null, Short(list.ToString()));
            if (a == null || b == null || c == null) return;
            Check("owner and priority", (string)a["owner"] == Owner && (int)a["priority"] == Prio);
            Check("a bone that resolves: effective", (bool)a["effective"] && (bool)a["winning"]);
            Check("a stale bone: not effective, still winning", !(bool)b["effective"] && (bool)b["winning"]);
            Check("an IK group that is off: not effective", !(bool)c["effective"]);
            h.Ok(new { cmd = "ik.enable", key, on = true }, "ik.enable");
            h.Frames(1);
            Check("the group on: effective", (bool)RowOf(h.Call(new { cmd = "overrides.list", key }), key, ikp)["effective"]);
            h.Ok(new { cmd = "ik.enable", key, on = false }, "ik.enable off");
            h.Frames(1);

            // a later writer: the rule of the eye look while it is on; a rotation driven on an eye bone does not show
            var r = h.Rt.FindChara(key);
            var eye = r.Look?.Eyes?.Eyes?.FirstOrDefault(x => x != null && x.Transform)?.Transform;
            if (Check("the target has an eye bone", eye != null))
            {
                string address = eye.name;
                string path = OverridePath.BoneText(address, "rot");
                var s4 = ov.Acquire(Owner, key, path, Prio);
                s4.Set(0.1, 0, 0, 0.995);
                h.Frames(2);
                bool look = r.Look.Eyes.LookEnabled;
                var row = RowOf(h.Call(new { cmd = "overrides.list", key }), key, path);
                Check("an eye bone's rotation with the eye look on: not effective", row != null && (!look || !(bool)row["effective"]), $"look {look}, row {row}");
                s4.Release();
            }
            // a bone a later stage writes: a chain bone under physics is another writer after the stage
            var d = DrivenPoseDriver.Of(r);
            var e = d?.Set.Of(FreeBone);
            Check("the entry of the bone", e != null);
            if (e != null)
            {
                h.Frames(1);
                e.Bone.localRotation = Quaternion.Euler(10f, 20f, 30f);   // what a writer after the stage leaves
                Check("a value written over the driven one: not effective", !s1.IsEffective);
                h.Frames(2);
                Check("the next step shows it again: effective", s1.IsEffective);
            }
            s1.Release(); s2.Release(); s3.Release();
            h.Frames(2);
            Check("no row left", ((JArray)h.Call(new { cmd = "overrides.list", key })["overrides"]).Count == 0);
        }
        finally { h.Dispose(); }
    }

    static void CL6()
    {
        var (h, key) = LayerHost("CL6");
        try
        {
            var ov = h.Rt.Overrides;
            var c = h.Rt.FindChara(key);
            var t = BoneOf(h, key, FreeBone);
            var r0 = t.localRotation;
            var slot = ov.Acquire("test.plugin", key, OverridePath.BoneText(FreeBone, "rot"), 0);
            var v = r0 * Quaternion.Euler(0f, 0f, 25f);
            slot.Set(v.x, v.y, v.z, v.w);
            Check("a plugin's slot on a bone shows at the next step, not at Set", Deg(t.localRotation, r0) == 0);
            Check("the stages come with the value", c.Motion.Stages.Any(s => s.Name == "bones.driven"));
            h.Frames(1);
            Check("the next step writes it", Angle(t.localRotation, v) <= 0.01, Angle(t.localRotation, v).ToString("G3"));
            Check("no evaluator: the set writes the channels by itself", DrivenPoseDriver.Of(c).Set.Evaluator == null);
            slot.Release();
            h.Frames(2);
            Check("the slot gone: the pose back, stages gone", Angle(t.localRotation, r0) <= 1e-4 && !c.Motion.Stages.Any(s => s.Name == "bones.driven"));

            h.Ok(new { cmd = "ik.enable", key, on = true }, "ik.enable");
            h.Frames(3);
            var ik = IkDriver.Of(c);
            var m = ik.Effective(IkTargetId.LeftFoot);
            var foot = c.Context.BodyBones["cf_J_Foot01_L"];
            var s2 = ov.Acquire("test.plugin", key, OverridePath.IkText("LeftFoot", "pos"), 0);
            var p = m.Pos + new Vector3(0f, 0.15f, 0.1f);
            s2.Set(p.x, p.y, p.z);
            h.Frames(1);
            Check("a plugin's slot on an IK target reaches the solve", Dist(foot.position, c.Root.transform.TransformPoint(p)) <= 1e-3, Dist(foot.position, c.Root.transform.TransformPoint(p)).ToString("G3"));
            ov.ReleaseOwner("test.plugin");
        }
        finally { h.Dispose(); }
    }

    static void CO1()
    {
        foreach (var who in new[] { (femaleRel, "female"), (maleRel, "male") })
        {
            var h = NewHost();
            try
            {
                string key = Key(h.Ok(new { cmd = "chara.load", path = who.Item1, sex = who.Item2 }, who.Item2 + ": chara.load"));
                h.Frames(2);
                var c = h.Rt.FindChara(key);
                var before = c.Motion.Stages.Select(s => s.Name).ToList();
                Check(who.Item2 + ": no driven stage on an unused character", !before.Any(n => n.StartsWith("bones.driven", StringComparison.Ordinal)), string.Join(",", before));
                Check(who.Item2 + ": no pass, no driver on a scene without constraints", h.Rt.Pass == null && !c.Motion.Scheduled);

                // a node's slot adds no stage
                var folder = h.Session.AddFolder().NewIds[0];
                h.Frames(1);
                var ns = h.Rt.Overrides.Acquire(Owner, folder, "transform.pos", Prio);
                ns.Set(1, 2, 3);
                h.Frames(2);
                Check(who.Item2 + ": a node's slot changes no stage", c.Motion.Stages.Select(s => s.Name).SequenceEqual(before));
                ns.Release();

                // an IK target's slot adds no stage either
                var ks = h.Rt.Overrides.Acquire(Owner, key, OverridePath.IkText("LeftHand", "pos"), Prio);
                ks.Set(0, 1, 0);
                h.Frames(1);
                Check(who.Item2 + ": an IK target's slot changes no stage", c.Motion.Stages.Select(s => s.Name).SequenceEqual(before));
                ks.Release();

                var bs = h.Rt.Overrides.Acquire(Owner, key, OverridePath.BoneText(FreeBone, "pos"), Prio);
                bs.Set(0, 0.1, 0);
                var list = c.Motion.Stages.ToList();
                var names = list.Select(s => s.Name).ToList();
                int rs = names.IndexOf("bones.driven.restore"), ap = names.IndexOf("bones.driven"), en = names.IndexOf("bones.driven.end");
                Check(who.Item2 + ": three stages with a driven bone", rs >= 0 && ap >= 0 && en >= 0, string.Join(",", names));
                if (rs >= 0 && ap >= 0 && en >= 0)
                {
                    Check(who.Item2 + ": phases -99, 440, 955", (int)list[rs].Phase == -99 && (int)list[ap].Phase == 440 && (int)list[en].Phase == 955);
                    bool sorted = true;
                    for (int i = 1; i < list.Count; i++) sorted &= list[i - 1].Phase <= list[i].Phase;
                    Check(who.Item2 + ": Stages sorted by phase", sorted);
                    var rest = names.Where(n => !n.StartsWith("bones.driven", StringComparison.Ordinal));
                    Check(who.Item2 + ": no other stage changes", rest.SequenceEqual(before));
                    int fk = names.IndexOf("fk");
                    Check(who.Item2 + ": 440 lies after FK", fk < 0 || fk < ap);
                }
                bs.Release();
                h.Frames(2);
                Check(who.Item2 + ": the list as before once the last channel went and its restore ran", c.Motion.Stages.Select(s => s.Name).SequenceEqual(before), string.Join(",", c.Motion.Stages.Select(s => s.Name)));

                // the same with real constraints
                var other = h.Session.AddFolder().NewIds[0];
                h.Frames(1);
                Put(h, who.Item2 + ": a node-only constraint", CEntry(new ConstraintEnd(folder), new ConstraintEnd(other), ConstraintComponents.Position));
                h.Frames(2);
                Check(who.Item2 + ": node-only constraints change no stage", c.Motion.Stages.Select(s => s.Name).SequenceEqual(before));
                Check(who.Item2 + ": the pass is on", h.Rt.Pass != null && c.Motion.Scheduled);
                Put(h, who.Item2 + ": a bone target", CEntry(new ConstraintEnd(folder), new ConstraintEnd(key, FreeBone), ConstraintComponents.Rotation));
                Check(who.Item2 + ": three stages with a bone target", c.Motion.Stages.Count(s => s.Name.StartsWith("bones.driven", StringComparison.Ordinal)) == 3);
                h.Frames(2);
                var clear = h.Session.Commands.Execute(ConstraintEditCommand.Clear());
                Check(who.Item2 + ": the list cleared", clear.Ok, clear.Message);
                h.Frames(3);
                Check(who.Item2 + ": the empty list: the stages as before, no pass, nothing scheduled", c.Motion.Stages.Select(s => s.Name).SequenceEqual(before) && h.Rt.Pass == null && !c.Motion.Scheduled, string.Join(",", c.Motion.Stages.Select(s => s.Name)));
            }
            finally { h.Dispose(); }
        }
    }
}

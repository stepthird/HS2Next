// ConstraintTest, driven bones in real frames (Phase 6.5 package O). These prove the stages -99, 440, 955 with what writes
// through them: slots of the constraints' owner, and for a weight an evaluator of this file doing what the constraints' evaluator does at stage 440.
//   CO6  a driven bone with a correction and a card modifier, on bones with and without a clip, at weights 1 and 0.5; under heels, the hips offset, the neck
//        patterns, IK: no drift on any frame of 600, and after switching off the bones equal a twin that never had one. Counts the restores whose rotation
//        setter changed the last bits, the case the latches of IK and the neck look see
//   CO7  row changes with a driven bone on every channel class: after off, equal to a fresh build (the bind's second read of the defaults)
//   CO8  a chain under a driven bone swings, physically; a driven chain bone does not drift
//   CO9  twist bones under a driven arm equal those of the same rotation given by FK
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HS2Next.App;
using HS2Next.Chara.Ext;
using HS2Next.Content;
using HS2Next.Data;
using HS2Next.Data.Ext;
using HS2Next.Motion;
using HS2Next.Motion.AdvancedPose;
using HS2Next.Motion.DynamicBones;
using HS2Next.Motion.Ik;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Overrides;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class ConstraintTest
{
    static string pA, pB, pC;

    /// <summary>scene.new, then n characters of the female target in one batch, the row in one batch: they run in lockstep.</summary>
    static IEnumerator PScene(AppCore app, int n, int speedRow, string what, bool physics = false)
    {
        pA = pB = pC = null;
        yield return PCall(app, new { cmd = "scene.pause", paused = false });
        yield return PCall(app, new { cmd = "scene.new" });
        Check(what + ": scene.new", POk, Short(pLastText));
        var cmds = new List<object>();
        for (int i = 0; i < n; i++)
        {
            cmds.Add(new Dictionary<string, object> { ["cmd"] = "chara.load", ["path"] = femaleRel, ["sex"] = "female", ["as"] = "c" + i });
            cmds.Add(new Dictionary<string, object> { ["cmd"] = "chara.motion", ["key"] = "$c" + i + ".added.0.key", ["physics"] = physics });
        }
        yield return PCall(app, new { cmd = "batch", cmds });
        if (!Check(what + ": the characters", POk, Short(pLastText))) yield break;
        var res = pLast["results"] as JArray;
        pA = (string)res?[0]?["added"]?[0]?["key"];
        if (n > 1) pB = (string)res?[2]?["added"]?[0]?["key"];
        if (n > 2) pC = (string)res?[4]?["added"]?[0]?["key"];
        if (speedRow >= 0) yield return PRow(app, 19, 0, 0, speedRow, what);
    }

    static IEnumerator PRow(AppCore app, int g, int c, int no, float speed, string what, float time = 0f)
    {
        var cmds = new List<object>();
        foreach (var k in new[] { pA, pB, pC })
            if (k != null) cmds.Add(new { cmd = "chara.anime", key = k, group = g, category = c, no, time, speed });
        yield return PCall(app, new { cmd = "batch", cmds });
        Check(what + ": the row in one batch", POk, Short(pLastText));
    }

    static CharaRealization PR(AppCore app, string k) => app.Runtime.FindChara(k);
    static Transform B(CharaRealization r, string name) => AdvancedPoseResolver.Resolve(r, name);
    static (Vector3 p, Quaternion r, Vector3 s) Local(Transform t) => (t.localPosition, t.localRotation, t.localScale);

    sealed class Worst
    {
        public double P, R, S;
        public string Where = "";
        public int N;
        public void Add((Vector3 p, Quaternion r, Vector3 s) a, (Vector3 p, Quaternion r, Vector3 s) b, string where)
        {
            N++;
            double dp = (a.p - b.p).magnitude, dr = Deg(a.r, b.r), ds = (a.s - b.s).magnitude;
            if (dp > P) { P = dp; Where = where; }
            if (dr > R) { R = dr; Where = where; }
            if (ds > S) { S = ds; Where = where; }
        }
        public bool Ok(double p = 1e-5, double r = 0.01, double s = 1e-5) => P <= p && R <= r && S <= s;
        public override string ToString() => $"pos {P:G3} rot {R:G3} deg scale {S:G3} over {N} samples ({Where})";
    }

    /// <summary>Calls <see cref="OnLate"/> in every LateApply: a sample after all motion, once a frame.</summary>
    sealed class LateHook : ISceneRuntimePart
    {
        public Action OnLate;
        public void Attach(SceneRuntime r) { }
        public void OnChanged(SceneChangeBatch b) { }
        public void Apply() { }
        public void LateApply() => OnLate?.Invoke();
        public void Clear() { }
        public void OnLoadedRealized() { }
    }

    /// <summary>n frames with <paramref name="sample"/> called after all motion in each.</summary>
    static IEnumerator Sampled(AppCore app, int n, Action sample)
    {
        var hook = new LateHook();
        int left = n;
        hook.OnLate = () => { if (left > 0) { left--; sample(); } };
        app.Runtime.AddPart(hook);
        float until = Time.realtimeSinceStartup + 120f;
        while (left > 0 && Time.realtimeSinceStartup < until) yield return null;
        app.Runtime.RemovePart(hook);
        Check($"{n} frames sampled", left == 0, left + " left");
    }

    /// <summary>The driven channels of one bone on a character, by slots of the constraints' owner.</summary>
    sealed class Drive
    {
        public OverrideSlot Pos, Rot, Scale;
        public Vector3 P, S = Vector3.one;
        public Quaternion R = Quaternion.identity;

        public static Drive On(AppCore app, string key, string bone, Vector3? p, Quaternion? r, Vector3? s)
        {
            var ov = app.Runtime.Overrides;
            var d = new Drive();
            if (p != null) { d.P = p.Value; d.Pos = ov.Acquire(Owner, key, OverridePath.BoneText(bone, "pos"), Prio); d.Pos.Set(d.P.x, d.P.y, d.P.z); }
            if (r != null) { d.R = r.Value; d.Rot = ov.Acquire(Owner, key, OverridePath.BoneText(bone, "rot"), Prio); d.Rot.Set(d.R.x, d.R.y, d.R.z, d.R.w); }
            if (s != null) { d.S = s.Value; d.Scale = ov.Acquire(Owner, key, OverridePath.BoneText(bone, "scale"), Prio); d.Scale.Set(d.S.x, d.S.y, d.S.z); }
            return d;
        }

        public void Release() { Pos?.Release(); Rot?.Release(); Scale?.Release(); }
    }

    /// <summary>What the constraints' evaluator does at stage 440 for a weight: the value over the bone's local value, the slots, the write.</summary>
    sealed class Blend : IDrivenEvaluator
    {
        public readonly List<(DrivenBone e, Drive d, float w)> Bones = new List<(DrivenBone, Drive, float)>();
        readonly SceneRuntime rt;
        public Blend(SceneRuntime rt) { this.rt = rt; }
        public void EvaluateBones(DrivenBones set, in MotionFrame frame)
        {
            foreach (var (e, d, w) in Bones)
            {
                e.Bone.GetLocalPositionAndRotation(out var lp, out var lr);
                var ls = e.Bone.localScale;
                var p = Vector3.Lerp(lp, d.P, w); var r = Quaternion.Slerp(lr, d.R, w); var s = Vector3.Lerp(ls, d.S, w);
                d.Pos.Set(p.x, p.y, p.z); d.Rot.Set(r.x, r.y, r.z, r.w); d.Scale.Set(s.x, s.y, s.z);
                set.Write(e, DrivenChannels.Position | DrivenChannels.Rotation | DrivenChannels.Scale);
            }
        }
    }

    /// <summary>Counts, right after the driven restore, the rotations the setter stored with other bits than Base.</summary>
    sealed class BitWatch : IMotionStage
    {
        readonly DrivenBones set;
        public int Restores, Changed;
        public BitWatch(DrivenBones set) { this.set = set; }
        public MotionPhase Phase => (MotionPhase)(-98);
        public string Name => "test.bitwatch";
        public bool Enabled { get; set; } = true;
        public void Reset() { }
        public void Step(in MotionFrame frame)
        {
            foreach (var e in set.Entries)
            {
                if (!e.Bone) continue;
                var q = e.Bone.localRotation; var b = e.BaseRot;
                if (b.x == 0f && b.y == 0f && b.z == 0f && b.w == 0f) continue;   // the first step: no base yet
                Restores++;
                if (q.x != b.x || q.y != b.y || q.z != b.z || q.w != b.w) Changed++;
            }
        }
    }

    static BoneModifierApplier Applier(CharaRealization r)
    {
        var c = r.Context;
        if (!c.Bones) c.Bones = r.Root.GetComponent<BoneModifierApplier>() ?? r.Root.AddComponent<BoneModifierApplier>();
        return c.Bones;
    }

    static readonly Vector3 ModS = new Vector3(1.05f, 0.95f, 1.1f), ModP = new Vector3(0.01f, 0.02f, -0.01f), ModR = new Vector3(25f, 20f, -5f);
    const float ModL = 1.1f;

    static void AddMods(CharaRealization r, params string[] bones)
    {
        var a = Applier(r);
        foreach (var b in bones)
            a.Add(new BoneModifierApplier.Modifier { Target = B(r, b), Bone = b, Source = "test", Location = 1, Scale = ModS, Length = ModL, Position = ModP, Rotation = ModR });
        HS2Next.Motion.Modifiers.BoneModifierSetup.Sync(r.Context);
    }

    static readonly BoneCorrection OrderCorr = new BoneCorrection(new Vec3(0.02f, -0.01f, 0.005f), new Vec3(-7f, 11f, 13f), new Vec3(1.1f, 1.2f, 0.9f));

    static (Vector3 p, Quaternion r, Vector3 s) WithCorr((Vector3 p, Quaternion r, Vector3 s) o, BoneCorrection c) =>
        (o.p + UnityObjects.V(c.Pos), o.r * Quaternion.Euler(UnityObjects.V(c.Rot)), Vector3.Scale(o.s, UnityObjects.V(c.Scale)));

    static SortedDictionary<string, BoneCorrection> Map(params (string b, BoneCorrection c)[] e)
    {
        var m = new SortedDictionary<string, BoneCorrection>(StringComparer.Ordinal);
        foreach (var (b, c) in e) m[b] = c;
        return m;
    }

    /// <summary>Every transform under the two characters' roots, compared by position in the tree (the same card, the same order).</summary>
    static void AllBones(CharaRealization a, CharaRealization b, Worst w, string what)
    {
        var x = a.Root.GetComponentsInChildren<Transform>(true);
        var y = b.Root.GetComponentsInChildren<Transform>(true);
        if (x.Length != y.Length) { w.P = double.MaxValue; w.Where = $"{x.Length} transforms against {y.Length}"; return; }
        for (int i = 0; i < x.Length; i++) w.Add(Local(x[i]), Local(y[i]), what + " " + x[i].name);
    }

    static IEnumerator Play_B_Bones()
    {
        current = "Bones";
        if (!new[] { "CO6", "CO7", "CO8", "CO9" }.Any(Want)) yield break;
        var ready = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 240, ready);
        var app = AppCore.Current;
        if (!Check("the app runs with its bridge and runtime", ready[0] && app != null && app.Stage == AppStage.Ready && app.Bridge != null && app.Runtime != null, app?.Stage.ToString())) yield break;
        float old = Time.captureDeltaTime;
        Time.captureDeltaTime = Dt;
        HS2Next.Motion.Face.FaceSetup.Seed = 0x5eed;
        try
        {
            if (POpen("CO6", "a driven bone with what else writes it: no drift, and nothing left after off"))
            {
                yield return CO6Weight(app, 1f);
                yield return CO6Weight(app, 0.5f);
                yield return CO6Heels(app);
                yield return CO6Hips(app);
                yield return CO6Ik(app);
                yield return CO6Neck(app);
                yield return CO6Fk(app);
                yield return CO6Twist(app);
            }
            if (POpen("CO7", "row changes with a driven bone on every channel class")) yield return CO7(app);
            if (POpen("CO8", "physics under a driven bone, a driven chain bone")) yield return CO8(app);
            if (POpen("CO9", "twist bones under a driven arm equal those of the same rotation by FK")) yield return CO9(app);
            yield return PCall(app, new { cmd = "scene.pause", paused = false });
            yield return PCall(app, new { cmd = "scene.new" });
        }
        finally { Time.captureDeltaTime = old; HS2Next.Motion.Face.FaceSetup.Seed = null; }
    }

    // ------------------------------------------------------------ CO6

    static IEnumerator CO6Weight(AppCore app, float weight)
    {
        const string unbound = FreeBone, bound = "cf_J_ArmUp00_R";
        string tag = "weight " + weight;
        yield return PScene(app, 3, 1, "CO6 " + tag);
        if (pC == null) yield break;
        yield return PFrames(5);
        var ra = PR(app, pA); var rb = PR(app, pB); var rc = PR(app, pC);
        AddMods(ra, unbound, bound); AddMods(rb, unbound, bound); AddMods(rc, unbound, bound);
        app.Session.Set(pA, "chara.advancedPose.bones", Map((unbound, OrderCorr), (bound, OrderCorr)));
        app.Session.Set(pB, "chara.advancedPose.bones", Map((unbound, OrderCorr), (bound, OrderCorr)));
        app.Runtime.ApplyNow();
        yield return PFrames(3);
        var o1 = Local(B(rc, unbound)); var o2 = Local(B(rc, bound));
        var tp1 = o1.p + new Vector3(0.03f, 0.01f, -0.02f); var tr1 = o1.r * Quaternion.Euler(15f, -25f, 35f); var ts1 = new Vector3(1.2f, 0.9f, 1.1f);
        var tp2 = o2.p + new Vector3(-0.02f, 0.02f, 0.01f); var tr2 = o2.r * Quaternion.Euler(-20f, 30f, 10f); var ts2 = new Vector3(0.9f, 1.15f, 1.05f);
        var d1 = Drive.On(app, pA, unbound, tp1, tr1, ts1);
        var d2 = Drive.On(app, pA, bound, tp2, tr2, ts2);
        var set = DrivenPoseDriver.Of(ra)?.Set;
        if (!Check(tag + ": the character has its driven set", set != null)) yield break;
        var watch = new BitWatch(set);
        ra.Motion.Add(watch);
        Blend blend = null;
        if (weight < 1f)
        {
            blend = new Blend(app.Runtime);
            blend.Bones.Add((set.Of(unbound), d1, weight));
            blend.Bones.Add((set.Of(bound), d2, weight));
            set.Evaluator = blend;
        }
        var w1 = new Worst(); var w2 = new Worst(); var wb = new Worst();
        yield return Sampled(app, 600, () =>
        {
            var c1 = Local(B(rc, unbound)); var c2 = Local(B(rc, bound));
            var e1 = (Vector3.Lerp(c1.p, tp1, weight), Quaternion.Slerp(c1.r, tr1, weight), Vector3.Lerp(c1.s, ts1, weight));
            var e2 = (Vector3.Lerp(c2.p, tp2, weight), Quaternion.Slerp(c2.r, tr2, weight), Vector3.Lerp(c2.s, ts2, weight));
            w1.Add(Local(B(ra, unbound)), WithCorr(e1, OrderCorr), unbound);
            w2.Add(Local(B(ra, bound)), WithCorr(e2, OrderCorr), bound);
            wb.Add(Local(B(rb, unbound)), WithCorr(c1, OrderCorr), "twin " + unbound);
        });
        Check(tag + ": 600 frames, a bone without a clip: value, then the correction on top", w1.Ok(), w1.ToString());
        Check(tag + ": 600 frames, a bone with a clip: value, then the correction on top", w2.Ok(), w2.ToString());
        Check(tag + ": the twin holds its modifier and correction", wb.Ok(), wb.ToString());
        Measure($"CO6 {tag}: without a clip {w1}; with a clip {w2}; {watch.Changed} of {watch.Restores} restores changed the rotation's last bits");
        ra.Motion.Remove(watch);
        set.Evaluator = null;
        d1.Release(); d2.Release();
        var wo = new Worst();
        yield return Sampled(app, 120, () => AllBones(ra, rb, wo, "off"));
        Check(tag + ": switched off, every transform equals the twin that never had one, in each of 120 frames", wo.Ok(), wo.ToString());
        Measure($"CO6 {tag} off: {wo}");
        Check(tag + ": the stages went", !ra.Motion.Stages.Any(s => s.Name.StartsWith("bones.driven", StringComparison.Ordinal)));
    }

    static IEnumerator CO6Heels(AppCore app)
    {
        yield return PScene(app, 2, -1, "CO6 heels");
        if (pB == null) yield break;
        var lists = HS2Next.Content.Maker.MakerCatalog.For(app.Services.Catalog);
        var row = app.Services.Catalog.EntriesOf(HeelsData.FemaleShoesCategory).Where(e => HeelsData.For(e) != null && lists.IsListed(247, 1, e.Key))
            .OrderBy(e => e.Key.ToString(), StringComparer.Ordinal).FirstOrDefault(e => HeelsData.For(e).Root.y > 0);
        if (!Check("an installed heel shoe", row != null)) yield break;
        foreach (var k in new[] { pA, pB })
        {
            yield return PCall(app, new { cmd = "chara.edit.item", key = k, path = "coord.clothes.shoes.item", @ref = row.Key.ToString() });
            Check("the heel shoe on", POk, Short(pLastText));
        }
        yield return PFrames(5);
        var ra = PR(app, pA); var rb = PR(app, pB);
        string[] bones = { "cf_J_Foot01_L", "cf_J_Foot02_L", "cf_J_Toes01_L" };
        var drives = new List<Drive>();
        var want = new Dictionary<string, Quaternion>();
        foreach (var b in bones)
        {
            var q = B(rb, b).localRotation * Quaternion.Euler(8f, -6f, 4f);
            want[b] = q;
            drives.Add(Drive.On(app, pA, b, null, q, null));
        }
        var w = new Worst(); var wo = new Worst();
        yield return Sampled(app, 600, () =>
        {
            foreach (var b in bones)
            {
                var t = Local(B(ra, b));
                w.Add((t.p, t.r, t.s), (Local(B(rb, b)).p, want[b], Local(B(rb, b)).s), b);
            }
            wo.Add(Local(B(ra, "cf_J_LegLow01_L")), Local(B(rb, "cf_J_LegLow01_L")), "the shin above");
        });
        Check("heels: 600 frames, the joints hold the driven rotation, position and scale as the twin's", w.Ok(), w.ToString());
        Check("heels: the shin above is the twin's", wo.Ok(), wo.ToString());
        Measure("CO6 heels: " + w);
        foreach (var d in drives) d.Release();
        var wf = new Worst();
        yield return Sampled(app, 60, () => AllBones(ra, rb, wf, "off"));
        Check("heels: switched off, every transform equals the twin", wf.Ok(), wf.ToString());
    }

    static IEnumerator CO6Hips(AppCore app)
    {
        yield return PScene(app, 2, -1, "CO6 hips");
        if (pB == null) yield break;
        yield return PFrames(3);
        foreach (var k in new[] { pA, pB })
        {
            yield return PCall(app, new { cmd = "fk.enable", key = k, on = true });
            yield return PCall(app, new { cmd = "fk.enable", key = k, groups = new[] { "Body" }, on = true });
            yield return PCall(app, new { cmd = "fk.set", key = k, hipsOffset = 0.3f });
            Check("FK on with a hips offset", POk, Short(pLastText));
        }
        yield return PFrames(3);
        var ra = PR(app, pA); var rb = PR(app, pB);
        const string bone = "cf_J_Hips", child = "cf_J_Spine01";
        var o = Local(B(rb, bone));
        var p = o.p + new Vector3(0.05f, 0.02f, -0.03f); var q = o.r * Quaternion.Euler(5f, 12f, -7f);
        var d = Drive.On(app, pA, bone, p, q, null);
        var w = new Worst(); var wc = new Worst();
        yield return Sampled(app, 600, () =>
        {
            w.Add(Local(B(ra, bone)), (p, q, o.s), bone);
            wc.Add(Local(B(ra, child)), Local(B(rb, child)), child);
        });
        Check("the hips offset: 600 frames on the driven value, no accumulation", w.Ok(), w.ToString());
        Check("the hips offset: the child's local values are the twin's", wc.Ok(), wc.ToString());
        Measure("CO6 hips: " + w);
        d.Release();
        var wf = new Worst();
        yield return Sampled(app, 60, () => AllBones(ra, rb, wf, "off"));
        Check("the hips offset: switched off, every transform equals the twin", wf.Ok(), wf.ToString());
    }

    static IEnumerator CO6Ik(AppCore app)
    {
        // the animation stage off: nothing rewrites a bone between two frames, so the latch is what stands between a frame and the next
        foreach (var (speed, name) in new[] { (-1, "no clip"), (1, "a clip running"), (-2, "the animation stage off") })
        {
            yield return PScene(app, 2, Math.Max(speed, -1), "CO6 IK, " + name);
            if (pB == null) yield break;
            yield return PFrames(3);
            if (speed == -2)
            {
                yield return PCall(app, new { cmd = "batch", cmds = new object[]
                {
                    new { cmd = "chara.motion", key = pA, stages = new Dictionary<string, bool> { ["anim"] = false } },
                    new { cmd = "chara.motion", key = pB, stages = new Dictionary<string, bool> { ["anim"] = false } },
                } });
                Check("the animation stage off", POk, Short(pLastText));
            }
            yield return PCall(app, new { cmd = "batch", cmds = new object[] { new { cmd = "ik.enable", key = pA, on = true }, new { cmd = "ik.enable", key = pB, on = true } } });
            Check("IK on, both in one frame", POk, Short(pLastText));
            yield return PFrames(5);
            var ra = PR(app, pA); var rb = PR(app, pB);
            // a target off the pose that moves on both: IK's output differs from its input, so a latch that failed would show as another input
            var hand = IkDriver.Of(ra).Effective(IkTargetId.RightHand).Pos;
            var moverA = app.Runtime.Overrides.Acquire("test.mover", pA, OverridePath.IkText("RightHand", "pos"), 0);
            var moverB = app.Runtime.Overrides.Acquire("test.mover", pB, OverridePath.IkText("RightHand", "pos"), 0);
            int tick = 0;
            void Move()
            {
                tick++;
                var v = hand + new Vector3(0.1f, 0.05f + 0.05f * Mathf.Sin(tick * 0.1f), 0.1f);
                moverA.Set(v.x, v.y, v.z); moverB.Set(v.x, v.y, v.z);
            }
            Move();
            yield return PFrames(3);
            var slots = new[] { "cf_J_ArmUp00_R", "cf_J_ArmLow01_R", "cf_J_Hand_R", "cf_J_LegUp00_L", "cf_J_LegLow01_L" };
            var drives = new List<Drive>();
            var want = new Dictionary<string, Quaternion>();
            foreach (var b in slots)
            {
                var q = B(rb, b).localRotation * Quaternion.Euler(9f, 7f, -5f);
                want[b] = q;
                drives.Add(Drive.On(app, pA, b, null, q, null));
            }
            var set = DrivenPoseDriver.Of(ra)?.Set;
            var watch = new BitWatch(set);
            ra.Motion.Add(watch);
            var w = new Worst(); var wi = new Worst();
            int lostA = 0, lostB = 0;
            yield return Sampled(app, 600, () =>
            {
                Move();
                lostA += LatchesLost(ra); lostB += LatchesLost(rb);
                foreach (var b in slots) w.Add(Local(B(ra, b)), (Local(B(ra, b)).p, want[b], Local(B(ra, b)).s), b);
                // what IK takes as its input is the twin's: the driven value never enters it
                var ia = IkDriver.Of(ra).Stage.LastInput; var ib = IkDriver.Of(rb).Stage.LastInput;
                for (int i = 0; i < ia.Length; i++) if (ia[i].Has) wi.Add((ia[i].Pos, Quaternion.Euler(ia[i].Rot), Vector3.one), (ib[i].Pos, Quaternion.Euler(ib[i].Rot), Vector3.one), IkTargets.Names[i]);
            });
            Check(name + ": 600 frames, the driven IK bones hold their value", w.Ok(), w.ToString());
            Check(name + ": IK's input pose stays the twin's on every frame", wi.Ok(1e-5, 0.01, 1), wi.ToString());
            Check(name + ": a restore never costs IK its latch: lost as often as on the twin", lostA == lostB, $"{lostA} against {lostB}");
            Measure($"CO6 IK, {name}: {w}; input {wi}; {watch.Changed} of {watch.Restores} restores changed the rotation's last bits; latches lost {lostA}, on the twin {lostB}");
            ra.Motion.Remove(watch);
            foreach (var d in drives) d.Release();
            var wf = new Worst();
            yield return Sampled(app, 60, () => { Move(); AllBones(ra, rb, wf, "off"); });
            Check(name + ": switched off, every transform equals the twin", wf.Ok(), wf.ToString());
            Measure($"CO6 IK, {name} off: {wf}");
            app.Runtime.Overrides.ReleaseOwner("test.mover");
        }
    }

    // the bones the last solve owned that the step did not find as it had left them
    static int LatchesLost(CharaRealization r)
    {
        var stage = IkDriver.Of(r).Stage;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var restored = (bool[])typeof(IkStage).GetField("restored", flags).GetValue(stage);
        var owned = (bool[])typeof(IkStage).GetField("ownedPrev", flags).GetValue(stage);
        int n = 0;
        for (int i = 0; i < owned.Length; i++) if (owned[i] && !restored[i]) n++;
        return n;
    }

    static IEnumerator CO6Neck(AppCore app)
    {
        foreach (var (clip, name) in new[] { (-1, "no clip"), (1, "a clip running"), (-2, "the animation stage off") })
        {
            yield return PScene(app, 2, Math.Max(clip, -1), "CO6 neck, " + name);
            if (pB == null) yield break;
            yield return PFrames(3);
            if (clip == -2)
            {
                yield return PCall(app, new { cmd = "batch", cmds = new object[]
                {
                    new { cmd = "chara.motion", key = pA, stages = new Dictionary<string, bool> { ["anim"] = false } },
                    new { cmd = "chara.motion", key = pB, stages = new Dictionary<string, bool> { ["anim"] = false } },
                } });
                Check("the animation stage off", POk, Short(pLastText));
            }
            var ra = PR(app, pA); var rb = PR(app, pB);
            string[] bones = { "cf_J_Neck", "cf_J_Head" };
            foreach (var (ptn, pname) in new[] { (0, "forward"), (1, "camera"), (3, "animation"), (4, "fix") })
            {
                yield return PCall(app, new { cmd = "batch", cmds = new object[] { new { cmd = "chara.face", key = pA, neckLook = ptn }, new { cmd = "chara.face", key = pB, neckLook = ptn } } });
                Check($"{name}: neck pattern {pname}", POk, Short(pLastText));
                yield return PFrames(30);
                var drives = new List<Drive>();
                var want = new Dictionary<string, Quaternion>();
                foreach (var b in bones)
                {
                    var q = B(rb, b).localRotation * Quaternion.Euler(6f, -8f, 3f);
                    want[b] = q;
                    drives.Add(Drive.On(app, pA, b, null, q, null));
                }
                var set = DrivenPoseDriver.Of(ra)?.Set;
                var watch = new BitWatch(set);
                ra.Motion.Add(watch);
                var w = new Worst();
                yield return Sampled(app, 300, () => { foreach (var b in bones) w.Add(Local(B(ra, b)), (Local(B(ra, b)).p, want[b], Local(B(ra, b)).s), b); });
                Check($"{name}, {pname}: 300 frames on the driven value", w.Ok(), w.ToString());
                ra.Motion.Remove(watch);
                foreach (var d in drives) d.Release();
                var wf = new Worst();
                yield return Sampled(app, 30, () => { foreach (var b in bones) wf.Add(Local(B(ra, b)), Local(B(rb, b)), b); });
                Check($"{name}, {pname}: switched off, the twin's values", wf.Ok(), wf.ToString());
                Measure($"CO6 neck {name}, {pname}: off {wf}; {watch.Changed} of {watch.Restores} restores changed the rotation's last bits");
            }
        }
    }

    // a writer between the modifiers and stage 440 on a bone no clip binds: FK on a hair bone with a card modifier. What the frame ended on is not what the
    // restore gives back, so the modifier's pass takes that value for new input; when FK goes the bone must show what the twin shows
    static IEnumerator CO6Fk(AppCore app)
    {
        yield return PScene(app, 2, 0, "CO6 FK");
        if (pB == null) yield break;
        yield return PFrames(3);
        var ra = PR(app, pA); var rb = PR(app, pB);
        var hair = FkDriver.Of(ra)?.Table.Of(HS2Next.Motion.Fk.FkGroup.Hair).FirstOrDefault(x => x.Used && x.Bone);
        if (!Check("an FK bone in the hair", hair != null)) yield break;
        string bone = AdvancedPoseResolver.Bones(ra).FirstOrDefault(x => x.Bone == hair.Bone)?.Address;
        if (!Check("it has an address", bone != null, hair.Bone.name)) yield break;
        AddMods(ra, bone); AddMods(rb, bone);
        foreach (var k in new[] { pA, pB })
        {
            yield return PCall(app, new { cmd = "batch", cmds = new object[]
            {
                new { cmd = "fk.enable", key = k, on = true },
                new { cmd = "fk.enable", key = k, groups = new[] { "Hair" }, on = true },
                new { cmd = "fk.set", key = k, bones = new Dictionary<string, float[]> { [hair.Bone.name] = new[] { 20f, 10f, -15f } } },
            } });
            Check("FK on the hair bone", POk, Short(pLastText));
        }
        yield return PFrames(5);
        var t = B(ra, bone); var u = B(rb, bone);
        var v = u.localRotation * Quaternion.Euler(10f, 0f, 30f);
        var d = Drive.On(app, pA, bone, null, v, null);
        var w = new Worst();
        yield return Sampled(app, 120, () => w.Add(Local(t), (Local(u).p, v, Local(u).s), bone));
        Check("120 frames on the driven value, position and scale the twin's", w.Ok(), w.ToString());
        d.Release();
        var wo = new Worst();
        yield return Sampled(app, 60, () => wo.Add(Local(t), Local(u), "off"));
        Check("off, FK still on: the twin's values", wo.Ok(), wo.ToString());
        foreach (var k in new[] { pA, pB })
        {
            yield return PCall(app, new { cmd = "fk.enable", key = k, on = false });
            Check("FK off", POk, Short(pLastText));
        }
        var wf = new Worst();
        yield return PFrames(3);
        yield return Sampled(app, 60, () => wf.Add(Local(t), Local(u), "FK off"));
        Check("FK off too: the twin's values", wf.Ok(), wf.ToString());
        Measure($"CO6 FK: driven {w}; off {wo}; FK off {wf}");
    }

    // a bone joint correction writes after stage 440: the driven value does not show, the slot says so, and nothing is left after off
    static IEnumerator CO6Twist(AppCore app)
    {
        foreach (var (speed, name) in new[] { (-1, "no clip"), (1, "a clip running") })
        {
            yield return PScene(app, 2, speed, "CO6 twist, " + name);
            if (pB == null) yield break;
            yield return PFrames(3);
            var ra = PR(app, pA); var rb = PR(app, pB);
            const string twist = "cf_J_ArmUp01_dam_R", arm = "cf_J_ArmUp00_R";
            // the arm turned on both, so the row has something to write
            var eulers = new Dictionary<string, float[]> { [arm] = new[] { 20f, 35f, -40f } };
            foreach (var k in new[] { pA, pB })
            {
                yield return PCall(app, new { cmd = "batch", cmds = new object[]
                {
                    new { cmd = "fk.enable", key = k, on = true },
                    new { cmd = "fk.enable", key = k, groups = new[] { "Body" }, on = true },
                    new { cmd = "fk.set", key = k, bones = eulers },
                    new { cmd = "chara.motion", key = k, correction = new { flags = new[] { true, true, true, true, true, true, true, true } } },
                } });
                Check("the arm by FK, every row of joint correction on", POk, Short(pLastText));
            }
            yield return PFrames(3);
            var t = B(ra, twist); var u = B(rb, twist);
            var v = u.localRotation * Quaternion.Euler(30f, 0f, 0f);
            var d = Drive.On(app, pA, twist, null, v, null);
            yield return PFrames(5);
            Check(name + ": the slot says what the bone holds", d.Rot.IsEffective == (Deg(t.localRotation, v) <= 1e-3), $"{d.Rot.IsEffective}, off the value by {Deg(t.localRotation, v):G3} deg");
            var wd = new Worst();
            yield return Sampled(app, 120, () => wd.Add(Local(t), Local(u), "driven"));
            Measure($"CO6 twist {name}: while driven, against the twin: {wd}");
            d.Release();
            var wo = new Worst();
            yield return PFrames(2);
            yield return Sampled(app, 60, () => wo.Add(Local(t), Local(u), "off"));
            Check(name + ": off: the twin's values", wo.Ok(), wo.ToString());
        }
    }

    // ------------------------------------------------------------ CO7

    static readonly (int g, int c, int n, string name) Idle = (19, 0, 0, "Idle_00"), Dance = (11, 5, 0, "dance_00"), Chair = (19, 1, 11, "Chair_A"), Attack = (15, 31, 1, "attack_01");

    static IEnumerator CO7(AppCore app)
    {
        // cf_J_Kokan: a channel the first clip writes and other states do not; a bone no clip binds; a bone every clip binds
        string[] bones = { "cf_J_Kokan", FreeBone, "cf_J_ArmUp00_R" };
        var orders = new[]
        {
            new[] { Idle, Chair, Idle, Chair, Idle },
            new[] { Chair, Idle },
            new[] { Idle, Dance, Idle },
            new[] { Idle, Dance, Chair, Idle },
            new[] { Attack, Idle, Chair, Idle },
        };
        foreach (var byHand in new[] { false, true })
            foreach (var order in orders)
            {
                string names = (byHand ? "by hand, " : "") + string.Join(", ", order.Select(x => x.name));
                yield return PScene(app, 2, -1, "CO7");
                if (pB == null) yield break;
                var ra = PR(app, pA); var rb = PR(app, pB);
                yield return PCall(app, new { cmd = "batch", cmds = new object[] { new { cmd = "chara.motion", key = pA, autoStep = !byHand, look = false, physics = false }, new { cmd = "chara.motion", key = pB, autoStep = !byHand, look = false, physics = false } } });
                yield return PFrames(3);
                var drives = bones.Select(b => Drive.On(app, pA, b, B(rb, b).localPosition + new Vector3(0.02f, 0f, 0.01f), B(rb, b).localRotation * Quaternion.Euler(10f, 5f, -5f), new Vector3(1.1f, 0.9f, 1.05f))).ToList();
                yield return PFrames(3);
                foreach (var row in order)
                {
                    yield return PCall(app, new { cmd = "batch", cmds = new object[] { new { cmd = "chara.anime", key = pA, group = row.g, category = row.c, no = row.n, time = 0.5f, speed = 0f }, new { cmd = "chara.anime", key = pB, group = row.g, category = row.c, no = row.n, time = 0.5f, speed = 0f } } });
                    if (byHand)
                    {
                        yield return PFrames(40);   // many frames before the first step: the second read waits for it
                        for (int i = 0; i < 2; i++)
                        {
                            yield return PCall(app, new { cmd = "chara.motion", key = pA, step = new { dt = Dt, n = 1 } });
                            yield return PCall(app, new { cmd = "chara.motion", key = pB, step = new { dt = Dt, n = 1 } });
                            yield return PFrames(2);
                        }
                    }
                    else yield return PFrames(6);
                    var w = new Worst();
                    for (int i = 0; i < bones.Length; i++) w.Add(Local(B(ra, bones[i])), (drives[i].P, drives[i].R, drives[i].S), row.name + " " + bones[i]);
                    Check($"{names}: at {row.name}, the driven values", w.Ok(1e-5, 0.01, 1e-5), w.ToString());
                }
                foreach (var d in drives) d.Release();
                if (byHand)
                {
                    yield return PCall(app, new { cmd = "chara.motion", key = pA, step = new { dt = Dt, n = 2 } });
                    yield return PCall(app, new { cmd = "chara.motion", key = pB, step = new { dt = Dt, n = 2 } });
                    yield return PFrames(2);
                }
                else yield return PFrames(6);
                var wt = new Worst();
                AllBones(ra, rb, wt, "off");
                Check($"{names}: after off, every transform equals the twin", wt.Ok(1e-5, 0.01, 1e-5), wt.ToString());
                // a fresh build on the last row
                var last = order[order.Length - 1];
                yield return PCall(app, new { cmd = "batch", cmds = new object[]
                {
                    new Dictionary<string, object> { ["cmd"] = "chara.load", ["path"] = femaleRel, ["sex"] = "female", ["as"] = "f" },
                    new Dictionary<string, object> { ["cmd"] = "chara.motion", ["key"] = "$f.added.0.key", ["autoStep"] = !byHand, ["look"] = false, ["physics"] = false },
                    new Dictionary<string, object> { ["cmd"] = "chara.anime", ["key"] = "$f.added.0.key", ["group"] = last.g, ["category"] = last.c, ["no"] = last.n, ["time"] = 0.5f, ["speed"] = 0f },
                } });
                if (!Check($"{names}: a fresh build", POk, Short(pLastText))) continue;
                string fresh = (string)(pLast["results"] as JArray)?[0]?["added"]?[0]?["key"];
                yield return PFrames(6);
                if (byHand) { yield return PCall(app, new { cmd = "chara.motion", key = fresh, step = new { dt = Dt, n = 2 } }); yield return PFrames(2); }
                // a row leaves bones behind on any character that played it (the twin too): those say nothing about a driven bone
                var wr = new Worst();
                int behind = 0;
                var fa = ra.Root.GetComponentsInChildren<Transform>(true);
                var fb = rb.Root.GetComponentsInChildren<Transform>(true);
                var ff = PR(app, fresh).Root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < fa.Length && i < ff.Length; i++)
                {
                    var one = new Worst();
                    one.Add(Local(fb[i]), Local(ff[i]), "");
                    if (!one.Ok(1e-5, 0.01, 1e-5)) { behind++; continue; }
                    wr.Add(Local(fa[i]), Local(ff[i]), "fresh " + fa[i].name);
                }
                Check($"{names}: after off, every transform the twin shares with a fresh build", wr.Ok(1e-5, 0.01, 1e-5) && fa.Length == ff.Length && behind < 16, wr + $", {behind} left behind by the rows on both");
            }
    }

    // ------------------------------------------------------------ CO8

    /// <summary>The same motion as a driven bone's, written after physics and taken off before the next step: what a post write would be.</summary>
    sealed class PostWrite : IMotionStage
    {
        public Transform Bone;
        public Quaternion Value, Base;
        public int Phases;
        public bool On;
        public PostWrite(int phase) { Phases = phase; }
        public MotionPhase Phase => (MotionPhase)Phases;
        public string Name => "test.postwrite";
        public bool Enabled { get; set; } = true;
        public void Reset() { }
        public void Step(in MotionFrame frame)
        {
            if (Phases < 0) { if (On) Bone.localRotation = Base; On = false; return; }
            Base = Bone.localRotation;
            Bone.localRotation = Value;
            On = true;
        }
    }

    static IEnumerator CO8(AppCore app)
    {
        yield return PScene(app, 2, 0, "CO8", physics: true);
        if (pB == null) yield break;
        yield return PFrames(120);
        var ra = PR(app, pA); var rb = PR(app, pB);
        var chainsA = ra.Motion.Get<DynamicBoneStage>()?.Chains; var chainsB = rb.Motion.Get<DynamicBoneStage>()?.Chains;
        int ci = chainsA == null ? -1 : chainsA.FindIndex(c => c.Active && c.ParticleCount >= 3 && c.ParticleTransform(0) && c.ParticleTransform(0).parent);
        if (!Check("a moving chain on the target", ci >= 0)) yield break;
        var ca = chainsA[ci]; var cb = chainsB[ci];
        var parentA = ca.ParticleTransform(0).parent; var parentB = cb.ParticleTransform(0).parent;
        int tail = ca.ParticleCount - 1;
        Info($"chain of {ca.ParticleCount} particles under {parentA.name}");
        var rows = AdvancedPoseResolver.Bones(ra);
        string address = rows.FirstOrDefault(x => x.Bone == parentA)?.Address;
        if (!Check("the chain's parent has an address", address != null && AdvancedPoseResolver.Resolve(ra, address) == parentA, address)) yield break;
        var slot = app.Runtime.Overrides.Acquire(Owner, pA, OverridePath.BoneText(address, "rot"), Prio);
        var post = new PostWrite(970) { Bone = parentB };
        var undo = new PostWrite(-99) { Bone = parentB };
        rb.Motion.Add(post); rb.Motion.Add(undo);
        var r0 = parentA.localRotation;
        double swing = 0, rigidB = 0;
        var restA = parentA.InverseTransformPoint(ca.Position(tail));
        var restB = parentB.InverseTransformPoint(cb.Position(tail));
        int n = 0;
        yield return Sampled(app, 240, () =>
        {
            n++;
            var q = r0 * Quaternion.Euler(0f, 0f, 30f * Mathf.Sin(n * 0.15f));
            slot.Set(q.x, q.y, q.z, q.w);
            post.Value = q;
            undo.Base = post.Base; undo.On = post.On;
            if (n < 2) return;
            // how far the chain's tail stands from where it would be if it were rigid on the moved parent
            swing = Math.Max(swing, (ca.Position(tail) - parentA.TransformPoint(restA)).magnitude);
            rigidB = Math.Max(rigidB, (cb.Position(tail) - ca.Position(tail)).magnitude);
        });
        Check("the chain's tail leaves the moved parent's rest and swings, physically", swing > 1e-3, swing.ToString("G3"));
        Check("it differs from the same motion written after physics", rigidB > 1e-3, rigidB.ToString("G3"));
        Measure($"CO8 under a driven parent: swing {swing:G3}, against a write after physics {rigidB:G3}");
        rb.Motion.Remove(post); rb.Motion.Remove(undo);
        if (post.On) parentB.localRotation = post.Base;
        slot.Release();
        yield return PFrames(120);

        // a driven chain bone
        var bone = ca.ParticleTransform(1);
        string chainAddress = rows.FirstOrDefault(x => x.Bone == bone)?.Address;
        if (!Check("a chain bone with an address", bone && chainAddress != null, bone ? bone.name : "none")) yield break;
        var q0 = bone.localRotation * Quaternion.Euler(10f, 0f, 0f);
        var d = Drive.On(app, pA, chainAddress, null, q0, null);
        yield return PFrames(100);
        var e = DrivenPoseDriver.Of(ra)?.Set.Of(chainAddress);
        if (!Check("the entry", e != null)) yield break;
        var b0 = (e.BasePos, e.BaseRot);
        var wd = new Worst();
        yield return Sampled(app, 500, () => wd.Add((e.BasePos, e.BaseRot, Vector3.one), (b0.BasePos, b0.BaseRot, Vector3.one), "base"));
        Check("a driven chain bone's base does not drift over 600 frames", wd.Ok(), wd.ToString());
        Measure("CO8 driven chain bone: base " + wd);
        d.Release();
        var decay = new List<string>();
        var wf = new Worst();
        for (int k = 0; k < 5; k++)
        {
            yield return PFrames(240);
            wf = new Worst();
            wf.Add(Local(bone), Local(cb.ParticleTransform(1)), "after off");
            decay.Add(wf.P.ToString("G3"));
        }
        Check("switched off and at rest: the twin's value", wf.Ok(1e-3, 0.1, 1e-3), wf + "; every 240 frames " + string.Join(", ", decay));
    }

    // ------------------------------------------------------------ CO9

    static IEnumerator CO9(AppCore app)
    {
        yield return PScene(app, 2, 0, "CO9");
        if (pB == null) yield break;
        yield return PFrames(3);
        var ra = PR(app, pA); var rb = PR(app, pB);
        var eulers = new Dictionary<string, Vector3> { ["cf_J_ArmUp00_R"] = new Vector3(20f, 35f, -40f), ["cf_J_ArmLow01_R"] = new Vector3(0f, -50f, 0f), ["cf_J_Hand_R"] = new Vector3(15f, 10f, -25f) };
        yield return PCall(app, new { cmd = "fk.enable", key = pB, on = true });
        Check("FK on the twin", POk, Short(pLastText));
        yield return PCall(app, new { cmd = "fk.enable", key = pB, groups = new[] { "Body" }, on = true });
        yield return PCall(app, new { cmd = "fk.set", key = pB, bones = eulers.ToDictionary(k => k.Key, k => new[] { k.Value.x, k.Value.y, k.Value.z }) });
        Check("the arm by FK", POk && (pLast["missing"] as JArray)?.Count == null, Short(pLastText));
        var drives = eulers.Select(k => Drive.On(app, pA, k.Key, null, Quaternion.Euler(k.Value), null)).ToList();
        yield return PFrames(10);
        var w = new Worst();
        foreach (var k in eulers.Keys) w.Add(Local(B(ra, k)), Local(B(rb, k)), k);
        Check("the arm stands alike", w.Ok(1e-5, 0.01, 1e-5), w.ToString());
        var wt = new Worst();
        int moved = 0;
        foreach (var t in new[] { "cf_J_ArmUp01_dam_R", "cf_J_ArmUp02_dam_R", "cf_J_ArmLow02_dam_R", "cf_J_ArmElbo_dam_01_R", "cf_J_ArmElboura_dam_R", "cf_J_Hand_Wrist_dam_R" })
        {
            var a = B(ra, t); var b = B(rb, t);
            if (!Check("twist bone " + t, a && b)) continue;
            wt.Add(Local(a), Local(b), t);
            if (Deg(a.localRotation, Quaternion.identity) > 0.1f) moved++;
        }
        Check("the twist bones equal those of the same rotation by FK", wt.Ok(1e-5, 0.01, 1e-5), wt.ToString());
        Check("and they did turn", moved > 0, moved + " of 6 off their rest");
        Measure($"CO9 twist bones: {wt}; {moved} of 6 turned");
        foreach (var d in drives) d.Release();
    }
}

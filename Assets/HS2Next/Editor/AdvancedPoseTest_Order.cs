// AdvancedPoseTest, the order of a frame (Phase 6.3 package M).
//   AO1  static: Stages sorted with the phases -100, 450, 950, on a character without modifiers the end stage last; BoneModifierApplier no order attribute, CharaMotion 1000
// Play mode, real LateUpdate (the character's own frames at a fixed delta; characters loaded in one batch run in lockstep):
//   AO2  modifiers (rotation, position, length, scale; source "test") on an unbound and a bound bone under a correction, 600 frames
//        running, 120 paused: unbound final = orig + modifiers + correction (1e-5, 0.01 deg), no drift; after clearing = orig + modifiers
//        once, not twice
//   AO3  the same on clip-bound channels: A (modifier and correction) = B (modifier) with the correction composed, B = C (neither) with the
//        modifier composed: clip, then modifier, then correction
//   AO4  a heel shoe's joints, the hips offset, the neck patterns forward, camera, animation, fix: no drift with corrections on those bones;
//        the release of the heels and FK off return to the values without
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HS2Next.App;
using HS2Next.Chara.Ext;
using HS2Next.Content;
using HS2Next.Data;
using HS2Next.Data.Ext;
using HS2Next.Motion;
using HS2Next.Motion.AdvancedPose;
using HS2Next.Motion.Animation;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static partial class AdvancedPoseTest
{
    static void Headless_M_Order()
    {
        Group("AO1", "the order, static", AO1);
    }

    static void AO1()
    {
        var f = NewTwins(femaleRel, "female", "AO1");
        try
        {
            var m = f.RA.Motion;
            var before = m.Stages.Select(s => s.Name).ToList();
            Check("no pose stage before a correction", !before.Any(n => n.StartsWith("pose.", StringComparison.Ordinal)), string.Join(",", before));
            SetBones(f.H, f.A, Map(("cf_J_Hand_Index02_L", Corr(rot: new Vec3(10f, 0f, 0f)))));
            var list = m.Stages.ToList();
            var names = list.Select(s => s.Name).ToList();
            int restore = names.IndexOf("pose.restore"), apply = names.IndexOf("pose.apply"), end = names.IndexOf("pose.end");
            Check("three stages", restore >= 0 && apply >= 0 && end >= 0, string.Join(",", names));
            Check("phases -100, 450, 950", (int)list[restore].Phase == -100 && (int)list[apply].Phase == 450 && (int)list[end].Phase == 950,
                $"{(int)list[restore].Phase}, {(int)list[apply].Phase}, {(int)list[end].Phase}");
            bool sorted = true;
            for (int i = 1; i < list.Count; i++) sorted &= list[i - 1].Phase <= list[i].Phase;
            Check("Stages sorted by phase", sorted);
            Check("the restore is first", restore == 0, names[0]);
            Check("the apply lies after FK and before the eye look",
                list.Where(s => s.Name == "fk").All(s => list.IndexOf(s) < apply) && list.Where(s => s.Phase == MotionPhase.EyeLook).All(s => list.IndexOf(s) > apply) &&
                list.Any(s => s.Name == "fk") && list.Any(s => s.Phase == MotionPhase.EyeLook));
            Check("the end is last, after the first-frame stage and the physics", end == list.Count - 1, names[list.Count - 1]);
            Check("BoneModifierApplier has no order attribute", typeof(BoneModifierApplier).GetCustomAttribute<DefaultExecutionOrder>() == null);
            Check("CharaMotion has the attribute 1000", typeof(CharaMotion).GetCustomAttribute<DefaultExecutionOrder>()?.order == 1000);
            SetBones(f.H, f.A, Map());
            var after = m.Stages.Select(s => s.Name).ToList();
            Check("the stage list is the one before after the last correction goes", after.SequenceEqual(before), string.Join(",", after));
            Check("no state left", AdvancedPoseDriver.Of(f.RA) == null);
            foreach (var who in new[] { (femaleRel, "female"), (maleRel, "male") })
            {
                var k = Key(f.H.Ok(new { cmd = "chara.load", path = who.Item1, sex = who.Item2 }, "the target's card modifiers"));
                var mods = f.H.Rt.FindChara(k).Context.Bones?.Modifiers;
                Info(who.Item2 + " target: " + (mods?.Count ?? 0) + " card modifiers; with a rotation, position or length value: " +
                     (mods == null ? "" : string.Join(", ", mods.Where(x => x.Rotation != Vector3.zero || x.Position != Vector3.zero || x.Length != 1f).Select(x => x.Bone).Distinct().Take(20))));
            }
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ play mode

    static JObject pLast;
    static string pLastText;
    static string pA, pB, pC;

    static bool POk => pLast?.Value<bool?>("ok") == true;

    static IEnumerator PCall(AppCore app, object req, double timeoutSec = 300)
    {
        string json = req is string s ? s : JsonConvert.SerializeObject(req);
        var t = app.Bridge.Execute(json);
        float until = Time.realtimeSinceStartup + (float)timeoutSec;
        while (!t.IsCompleted && Time.realtimeSinceStartup < until) yield return null;
        pLastText = t.IsCompleted ? (t.IsFaulted ? "{\"ok\":false,\"error\":\"faulted\"}" : t.Result) : "{\"ok\":false,\"code\":\"timeout\"}";
        try { pLast = JObject.Parse(pLastText); }
        catch (Exception) { pLast = new JObject { ["ok"] = false, ["error"] = "unparsable: " + pLastText }; }
    }

    /// <summary>n real frames (the harness pumps a coroutine from the Editor's update, so one yield is not one frame).</summary>
    static IEnumerator PFrames(int n) => PlayModeHarness.WaitFrames(n);

    static bool POpen(string id, string title)
    {
        if (!Want(id)) return false;
        current = id;
        rep.AppendLine();
        rep.AppendLine($"== {id} {title}");
        return true;
    }

    /// <summary>scene.new, then n characters of the target in one batch (autoStep, physics off), the row in one batch: they run in lockstep.</summary>
    static IEnumerator PScene(AppCore app, int n, int speedRow, string what)
    {
        pA = pB = pC = null;
        yield return PCall(app, new { cmd = "scene.new" });
        Check(what + ": scene.new", POk, Short(pLastText));
        var cmds = new List<object>();
        for (int i = 0; i < n; i++)
        {
            cmds.Add(new Dictionary<string, object> { ["cmd"] = "chara.load", ["path"] = femaleRel, ["sex"] = "female", ["as"] = "c" + i });
            cmds.Add(new Dictionary<string, object> { ["cmd"] = "chara.motion", ["key"] = "$c" + i + ".added.0.key", ["physics"] = false });
        }
        yield return PCall(app, new { cmd = "batch", cmds });
        if (!Check(what + ": the characters", POk, Short(pLastText))) yield break;
        var res = pLast["results"] as JArray;
        pA = (string)res?[0]?["added"]?[0]?["key"];
        if (n > 1) pB = (string)res?[2]?["added"]?[0]?["key"];
        if (n > 2) pC = (string)res?[4]?["added"]?[0]?["key"];
        yield return PRow(app, speedRow, what);
    }

    /// <summary>The idle row on every character of the fixture in one frame: speed 1, 0, or no clip at all (-1).</summary>
    static IEnumerator PRow(AppCore app, int speed, string what)
    {
        if (speed < 0) yield break;
        var cmds = new List<object>();
        foreach (var k in new[] { pA, pB, pC })
            if (k != null) cmds.Add(new { cmd = "chara.anime", key = k, group = 19, category = 0, no = 0, time = 0f, speed = (float)speed });
        yield return PCall(app, new { cmd = "batch", cmds });
        Check(what + ": the row in one batch", POk, Short(pLastText));
    }

    static CharaRealization PR(AppCore app, string k) => app.Runtime.FindChara(k);

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
            a.Add(new BoneModifierApplier.Modifier { Target = Bone(r, b), Bone = b, Source = "test", Location = 1, Scale = ModS, Length = ModL, Position = ModP, Rotation = ModR });
        HS2Next.Motion.Modifiers.BoneModifierSetup.Sync(r.Context);
    }

    static (Vector3 p, Quaternion r, Vector3 s) WithMods((Vector3 p, Quaternion r, Vector3 s) o) =>
        (o.p * ModL + ModP, o.r * Quaternion.Euler(ModR), Vector3.Scale(o.s, ModS));

    static (Vector3 p, Quaternion r, Vector3 s) WithCorr((Vector3 p, Quaternion r, Vector3 s) o, BoneCorrection c) =>
        (o.p + UnityObjects.V(c.Pos), o.r * Quaternion.Euler(UnityObjects.V(c.Rot)), Vector3.Scale(o.s, UnityObjects.V(c.Scale)));

    sealed class Worst
    {
        public double P, R, S;
        public string Where = "";
        public void Add((Vector3 p, Quaternion r, Vector3 s) a, (Vector3 p, Quaternion r, Vector3 s) b, string where)
        {
            double dp = Dist(a.p, b.p), dr = Angle(a.r, b.r), ds = Dist(a.s, b.s);
            if (dp > P) { P = dp; Where = where; }
            if (dr > R) { R = dr; Where = where; }
            if (ds > S) { S = ds; Where = where; }
        }
        public bool Ok(double p = 1e-5, double r = 0.01, double s = 1e-5) => P <= p && R <= r && S <= s;
        public override string ToString() => $"pos {P:G3} rot {R:G3} deg scale {S:G3} ({Where})";
    }

    static readonly BoneCorrection OrderCorr = new BoneCorrection(new Vec3(0.02f, -0.01f, 0.005f), new Vec3(-7f, 11f, 13f), new Vec3(1.1f, 1.2f, 0.9f));

    static IEnumerator Play_M_Order()
    {
        current = "M_Order";
        var ready = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 240, ready);
        var app = AppCore.Current;
        if (!Check("the app runs with its bridge and runtime", ready[0] && app != null && app.Stage == AppStage.Ready && app.Bridge != null && app.Runtime != null, app?.Stage.ToString())) yield break;
        float old = Time.captureDeltaTime;
        Time.captureDeltaTime = Dt;
        try
        {
            if (POpen("AO2", "the card's modifiers under a correction, real frames")) yield return AO2(app);
            if (POpen("AO3", "clip-bound channels: the correction composes on what the frame's clip gave")) yield return AO3(app);
            if (POpen("AO4", "heels, the hips offset, the neck patterns: no drift")) yield return AO4(app);
            yield return PCall(app, new { cmd = "scene.pause", paused = false });
            yield return PCall(app, new { cmd = "scene.new" });
        }
        finally { Time.captureDeltaTime = old; }
    }

    // ------------------------------------------------------------ AO2

    static IEnumerator AO2(AppCore app)
    {
        const string unbound = "cf_J_EarBase_s_L", bound = "cf_J_ArmUp00_R";
        yield return PScene(app, 3, 1, "AO2");
        if (pC == null) yield break;
        yield return PFrames(5);
        var ra = PR(app, pA); var rb = PR(app, pB); var rc = PR(app, pC);
        Check("the unbound bone holds the same value on the three", Dist(Bone(ra, unbound).localPosition, Bone(rc, unbound).localPosition) <= 1e-6);
        var orig = Local(Bone(rc, unbound));
        AddMods(ra, unbound, bound);
        AddMods(rb, unbound, bound);
        app.Session.Set(pA, "chara.advancedPose.bones", Map((unbound, OrderCorr), (bound, OrderCorr)));
        app.Runtime.ApplyNow();
        var modified = WithMods(orig);
        var final = WithCorr(modified, OrderCorr);
        var wu = new Worst(); var wb = new Worst(); var wm = new Worst();
        int frames = 0;
        for (int k = 0; k < 6; k++)
        {
            yield return PFrames(100);
            frames += 100;
            wu.Add(Local(Bone(ra, unbound)), final, unbound + " at frame " + frames);
            wm.Add(Local(Bone(rb, unbound)), modified, unbound + " at frame " + frames);
            wb.Add(Local(Bone(ra, bound)), WithCorr(Local(Bone(rb, bound)), OrderCorr), bound + " at frame " + frames);
        }
        Check("600 frames: unbound final = orig + modifiers + correction", wu.Ok(), wu.ToString());
        Check("600 frames: the twin holds orig + modifiers", wm.Ok(), wm.ToString());
        Check("600 frames: bound final = the twin's value with the correction composed", wb.Ok(), wb.ToString());
        Measure("AO2 running: " + wu);
        yield return PCall(app, new { cmd = "scene.pause", paused = true });
        Check("the global pause", POk, Short(pLastText));
        var wp = new Worst(); var wq = new Worst();
        for (int k = 0; k < 6; k++)
        {
            yield return PFrames(20);
            wp.Add(Local(Bone(ra, unbound)), final, unbound + " paused");
            wq.Add(Local(Bone(ra, bound)), WithCorr(Local(Bone(rb, bound)), OrderCorr), bound + " paused");
        }
        Check("120 frames paused: unbound holds", wp.Ok(), wp.ToString());
        Check("120 frames paused: bound holds", wq.Ok(), wq.ToString());
        yield return PCall(app, new { cmd = "scene.pause", paused = false });
        yield return PFrames(30);
        app.Session.Set(pA, "chara.advancedPose.bones", Map());
        app.Runtime.ApplyNow();
        var wc = new Worst();
        wc.Add(Local(Bone(ra, unbound)), modified, "right after clearing");
        yield return PFrames(2);
        wc.Add(Local(Bone(ra, unbound)), modified, "2 frames after clearing");
        yield return PFrames(60);
        wc.Add(Local(Bone(ra, unbound)), modified, "60 frames after clearing");
        Check("after clearing: orig + modifiers once, not twice", wc.Ok(), wc.ToString());
        var wd = new Worst();
        wd.Add(Local(Bone(ra, bound)), Local(Bone(rb, bound)), "the bound bone");
        Check("after clearing: the bound bone equals the twin", wd.Ok(), wd.ToString());
        ra.Context.Bones.Remove("test");
        rb.Context.Bones.Remove("test");
        yield return PFrames(2);

        // a character stepped by hand, with frames between the steps: the modifiers take no correction for a new base
        AddMods(ra, unbound);
        yield return PFrames(2);
        app.Session.Set(pA, "chara.advancedPose.bones", Map((unbound, OrderCorr)));
        app.Runtime.ApplyNow();
        yield return PCall(app, new { cmd = "chara.motion", key = pA, autoStep = false });
        Check("the character by hand", POk, Short(pLastText));
        var wh = new Worst();
        for (int k = 0; k < 30; k++)
        {
            yield return PCall(app, new { cmd = "chara.motion", key = pA, step = new { dt = Dt, n = 1 } });
            wh.Add(Local(Bone(ra, unbound)), final, unbound + " after hand step " + k);
            yield return PFrames(3);
            wh.Add(Local(Bone(ra, unbound)), final, unbound + " 3 frames after hand step " + k);
        }
        Check("30 hand steps with frames between: orig + modifiers + correction throughout", wh.Ok(), wh.ToString());
        app.Session.Set(pA, "chara.advancedPose.bones", Map());
        app.Runtime.ApplyNow();
        yield return PFrames(2);
        var we = new Worst();
        we.Add(Local(Bone(ra, unbound)), modified, "after clearing");
        Check("after the hand steps and clearing: orig + modifiers once, not 30 times", we.Ok(), we.ToString());
        yield return PCall(app, new { cmd = "chara.motion", key = pA, autoStep = true });
        ra.Context.Bones.Remove("test");
    }

    // ------------------------------------------------------------ AO3

    static IEnumerator AO3(AppCore app)
    {
        string[] bones = { "cf_J_ArmUp00_R", "cf_J_Hand_Index02_L", "cf_J_Spine02", "cf_J_Hips", "cf_J_Foot02_L" };
        yield return PScene(app, 3, 1, "AO3");
        if (pC == null) yield break;
        yield return PFrames(5);
        var ra = PR(app, pA); var rb = PR(app, pB); var rc = PR(app, pC);
        AddMods(ra, bones);
        AddMods(rb, bones);
        app.Session.Set(pA, "chara.advancedPose.bones", Map(bones.Select(b => (b, OrderCorr)).ToArray()));
        app.Runtime.ApplyNow();
        var w = new Worst();
        var wm = new Worst();
        for (int k = 0; k < 12; k++)
        {
            yield return PFrames(50);
            for (int i = 0; i < bones.Length; i++)
            {
                var a = Local(Bone(ra, bones[i])); var b = Local(Bone(rb, bones[i])); var c = Local(Bone(rc, bones[i]));
                w.Add(a, WithCorr(b, OrderCorr), bones[i]);
                wm.Add(b, WithMods(c), bones[i]);
            }
        }
        Check("600 frames: the correction composes on what the frame's clip and the modifiers gave", w.Ok(), w.ToString());
        Check("600 frames: the modifier composes on the frame's clip, every channel", wm.Ok(1e-4, 0.01, 1e-4), wm.ToString());
        Measure($"AO3 modifier and correction against the modifier: {w}; the modifier against neither: {wm}");
        ra.Context.Bones.Remove("test");
        rb.Context.Bones.Remove("test");
    }

    // ------------------------------------------------------------ AO4

    static IEnumerator AO4(AppCore app)
    {
        yield return AO4Heels(app);
        yield return AO4Hips(app);
        yield return AO4Neck(app);
    }

    static IEnumerator Watch(AppCore app, string[] bones, BoneCorrection c, int frames, int every, Worst w, string what)
    {
        var ra = PR(app, pA); var rb = PR(app, pB);
        for (int n = 0; n < frames; n += every)
        {
            yield return PFrames(every);
            foreach (var b in bones) w.Add(Local(Bone(ra, b)), WithCorr(Local(Bone(rb, b)), c), what + " " + b);
        }
    }

    static IEnumerator AO4Heels(AppCore app)
    {
        yield return PScene(app, 2, -1, "AO4 heels");
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
        var lift = ra.Motion.Get<HS2Next.Motion.Heels.HeelsLift>();
        var foot = ra.Motion.Get<HS2Next.Motion.Heels.HeelsFoot>();
        Check("the heels are in effect", lift != null && lift.Lifted && foot != null && foot.Written);
        string[] bones = { "cf_J_Foot01_L", "cf_J_Foot02_L", "cf_J_Toes01_L", "cf_J_Foot01_R", "cf_J_LegLow01_L" };
        var c = new BoneCorrection(new Vec3(0.005f, 0.002f, -0.003f), new Vec3(5f, -6f, 7f), new Vec3(1.05f, 0.95f, 1.1f));
        app.Session.Set(pA, "chara.advancedPose.bones", Map(bones.Select(b => (b, c)).ToArray()));
        app.Runtime.ApplyNow();
        var w = new Worst();
        yield return Watch(app, bones, c, 600, 100, w, "heels");
        Check("the joints under a correction: 600 frames, composed on the twin", w.Ok(), w.ToString());
        Measure("AO4 heels: " + w);
        foreach (var k in new[] { pA, pB })
        {
            yield return PCall(app, new { cmd = "chara.clothes", key = k, states = new Dictionary<string, int> { ["shoes"] = 2 } });
            Check("shoes off: the heels' release", POk, Short(pLastText));
        }
        yield return PFrames(3);
        Check("released", !lift.Lifted && !foot.Written);
        var w2 = new Worst();
        yield return Watch(app, bones, c, 120, 40, w2, "released");
        Check("released: composed on the twin", w2.Ok(), w2.ToString());
        app.Session.Set(pA, "chara.advancedPose.bones", Map());
        app.Runtime.ApplyNow();
        yield return PFrames(3);
        var w3 = new Worst();
        foreach (var b in bones) w3.Add(Local(Bone(ra, b)), Local(Bone(rb, b)), b);
        Check("released and cleared: the values without", w3.Ok(1e-6, 0.001, 1e-6), w3.ToString());
    }

    static IEnumerator AO4Hips(AppCore app)
    {
        yield return PScene(app, 2, -1, "AO4 hips");
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
        string[] bones = { "cf_J_Hips", "cf_J_Spine01" };
        var c = new BoneCorrection(new Vec3(0.01f, 0.02f, -0.005f), new Vec3(4f, 9f, -3f), new Vec3(1f, 1f, 1f));
        app.Session.Set(pA, "chara.advancedPose.bones", Map(bones.Select(b => (b, c)).ToArray()));
        app.Runtime.ApplyNow();
        var w = new Worst();
        yield return Watch(app, bones, c, 600, 100, w, "offset");
        Check("the hips offset without a clip: 600 frames, no accumulation", w.Ok(), w.ToString());
        Measure("AO4 hips: " + w);
        foreach (var k in new[] { pA, pB })
        {
            yield return PCall(app, new { cmd = "fk.enable", key = k, on = false });
            Check("FK off", POk, Short(pLastText));
        }
        yield return PFrames(3);
        var w2 = new Worst();
        yield return Watch(app, bones, c, 120, 40, w2, "FK off");
        Check("FK off: composed on the twin", w2.Ok(), w2.ToString());
        app.Session.Set(pA, "chara.advancedPose.bones", Map());
        app.Runtime.ApplyNow();
        yield return PFrames(3);
        var w3 = new Worst();
        foreach (var b in bones) w3.Add(Local(Bone(PR(app, pA), b)), Local(Bone(PR(app, pB), b)), b);
        Check("FK off and cleared: the values without", w3.Ok(1e-6, 0.001, 1e-6), w3.ToString());
    }

    static IEnumerator AO4Neck(AppCore app)
    {
        foreach (var (clip, name) in new[] { (-1, "no clip"), (1, "a clip running") })
        {
            yield return PScene(app, 2, clip, "AO4 neck, " + name);
            if (pB == null) yield break;
            yield return PFrames(3);
            string[] bones = { "cf_J_Neck", "cf_J_Head" };
            var c = new BoneCorrection(new Vec3(0f, 0f, 0f), new Vec3(6f, -8f, 3f), new Vec3(1f, 1f, 1f));
            app.Session.Set(pA, "chara.advancedPose.bones", Map(bones.Select(b => (b, c)).ToArray()));
            app.Runtime.ApplyNow();
            foreach (var (ptn, pname) in new[] { (0, "forward"), (1, "camera"), (3, "animation"), (4, "fix") })
            {
                yield return PCall(app, new { cmd = "batch", cmds = new object[] { new { cmd = "chara.face", key = pA, neckLook = ptn }, new { cmd = "chara.face", key = pB, neckLook = ptn } } });
                Check($"{name}: neck pattern {pname}", POk, Short(pLastText));
                yield return PFrames(30);
                var w = new Worst();
                yield return Watch(app, bones, c, 300, 100, w, pname);
                Check($"{name}, {pname}: 300 frames, composed on the twin", w.Ok(), w.ToString());
                yield return PCall(app, new { cmd = "scene.pause", paused = true });
                var wp = new Worst();
                yield return Watch(app, bones, c, 60, 20, wp, pname + " paused");
                Check($"{name}, {pname}: 60 frames paused, composed on the twin", wp.Ok(), wp.ToString());
                yield return PCall(app, new { cmd = "scene.pause", paused = false });
                var wr = new Worst();
                yield return Watch(app, bones, c, 60, 20, wr, pname + " resumed");
                Check($"{name}, {pname}: resumed, composed on the twin", wr.Ok(), wr.ToString());
                Measure($"AO4 neck {name}, {pname}: {w}; paused {wp}; resumed {wr}");
            }
            app.Session.Set(pA, "chara.advancedPose.bones", Map());
            app.Runtime.ApplyNow();
            yield return PFrames(3);
            var w3 = new Worst();
            foreach (var b in bones) w3.Add(Local(Bone(PR(app, pA), b)), Local(Bone(PR(app, pB), b)), b);
            Check(name + ": cleared, the values without", w3.Ok(1e-5, 0.01, 1e-5), w3.ToString());
        }
    }
}

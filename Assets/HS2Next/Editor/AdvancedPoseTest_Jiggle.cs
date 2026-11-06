// AdvancedPoseTest, the jiggle values (Phase 6.3 package J, AT3's chain half). Two characters of one
// card, one with the scene values and its twin without, stepped by hand in lockstep with physics on.
//   AJ1  each member of a chain through pose.chains.set: the value in use, base = the component's field; fields and documents unchanged
//   AJ2  a card's value (a document's dynamicBones entry), a scene value over it, its removal, a chain rebuild: the coordinate document
//        byte-equal throughout
//   AJ3  an edit at step k resets nothing: the next step's particles stay within twice the step's own motion of the twin's; enabled off
//        and on restore the pose and reset the particles. The timing of ApplyOverride (AT3)
//   AJ4  chara.change, a chain rebuild, the other sex's target, a hair swap and undo: found by address, or stale and back
//   AJ5  bust and hip chains: each member on breastL and hipR, a softness edit under a scene value, removal bit-equal, a male target
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using HS2Next.Motion.AdvancedPose;
using HS2Next.Motion.DynamicBones;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class AdvancedPoseTest
{
    static void Headless_J_Jiggle()
    {
        Group("AJ1", "each member of a chain: the value in use, the card's data untouched", AJ1);
        Group("AJ2", "a card's value under a scene value", AJ2);
        Group("AJ3", "an edit resets nothing", AJ3);
        Group("AJ4", "rebuild and part swaps", AJ4);
        Group("AJ5", "bust and hip chains", AJ5);
    }

    // ------------------------------------------------------------ helpers

    /// <summary>The chain with the most particles in a part ("hair:", "acc:", ...), not switched off by the card.</summary>
    static PoseChainRow PickChain(CharaRealization r, string prefix) =>
        AdvancedPoseParts.Chains(r).Where(x => x.Part.StartsWith(prefix, StringComparison.Ordinal) && x.Chain.ParticleCount >= 3 && !x.Chain.NoShake)
            .OrderByDescending(x => x.Chain.ParticleCount).FirstOrDefault();

    /// <summary>The model's chain map replaced (no command), applied.</summary>
    static void SetChains(Host h, string id, params (string address, ChainValues v)[] entries)
    {
        var m = new SortedDictionary<string, ChainValues>(StringComparer.Ordinal);
        foreach (var (a, v) in entries) m[a] = v;
        var res = h.Session.Set(id, "chara.advancedPose.chains", m);
        if (!res.Ok) Check("set chara.advancedPose.chains", false, res.ToString());
        h.Rt.ApplyNow();
    }

    static void SetBust(Host h, string id, string chain, BustValues v)
    {
        var res = h.Session.Set(id, "chara.advancedPose.bust." + chain, v);
        if (!res.Ok) Check("set chara.advancedPose.bust." + chain, false, res.ToString());
        h.Rt.ApplyNow();
    }

    static float[] ChainParams(DynamicBoneChain c)
    {
        var l = new List<float>();
        for (int i = 0; i < c.ParticleCount; i++) { l.Add(c.Damping(i)); l.Add(c.Elasticity(i)); l.Add(c.Stiffness(i)); l.Add(c.Inert(i)); l.Add(c.Radius(i)); }
        return l.ToArray();
    }

    static string Fields(DynamicBoneChain c)
    {
        var s = c.Source;
        return string.Join(",", s.m_Damping, s.m_Elasticity, s.m_Stiffness, s.m_Inert, s.m_Radius, s.m_FreezeAxis, s.m_BlendWeight, s.m_Gravity.ToString("R"), s.m_Force.ToString("R"), c.Enabled);
    }

    static string Docs(CharaRealization r)
    {
        var cc = r.Context;
        return Encoding.UTF8.GetString(CanonicalJsonWriter.ToDocumentBytes("chara", cc.Chara.Write)) + Encoding.UTF8.GetString(CanonicalJsonWriter.ToDocumentBytes("coord", cc.Coord.Write));
    }

    // the worst distance between two chains' particles
    static double ParticleDiff(DynamicBoneChain a, DynamicBoneChain b)
    {
        double w = 0;
        for (int i = 0; i < a.ParticleCount && i < b.ParticleCount; i++) w = Math.Max(w, Dist(a.Position(i), b.Position(i)));
        return w;
    }

    static double ParticleDiff(BustChain a, BustChain b)
    {
        double w = 0;
        for (int i = 0; i < a.ParticleCount && i < b.ParticleCount; i++) w = Math.Max(w, Dist(a.Position(i), b.Position(i)));
        return w;
    }

    static float Curve(AnimationCurve c, float u) => c != null && c.length > 0 ? c.Evaluate(u) : 1f;

    /// <summary>Both physics histories dropped; the reset of chara.motion gives back every switch the call does not name.</summary>
    static void ResetPair(Twins f)
    {
        foreach (var k in new[] { f.A, f.B }) f.H.Ok(new { cmd = "chara.motion", key = k, autoStep = false, physics = true, reset = true }, "reset");
    }

    static Vector3[] Particles(DynamicBoneChain c) => Enumerable.Range(0, c.ParticleCount).Select(c.Position).ToArray();
    static Vector3[] Particles(BustChain c) => Enumerable.Range(0, c.ParticleCount).Select(c.Position).ToArray();

    // ------------------------------------------------------------ AJ1

    static void AJ1()
    {
        var f = NewTwins(femaleRel, "female", "AJ1", physics: true);
        try
        {
            f.Step(10);
            var rows = AdvancedPoseParts.Chains(f.RA);
            Info($"chains on the target: {rows.Count}: " + string.Join(", ", rows.GroupBy(x => x.Part.Split(':')[0]).Select(g => g.Key + " " + g.Count())));
            Check("the twins list the same addresses", rows.Select(x => x.Address).SequenceEqual(AdvancedPoseParts.Chains(f.RB).Select(x => x.Address)));
            foreach (var prefix in new[] { "hair:", "acc:" })
            {
                var row = PickChain(f.RA, prefix);
                if (row == null) { Warn("no chain of the part " + prefix + " on the target"); continue; }
                AJ1Chain(f, row);
            }
        }
        finally { f.Dispose(); }
    }

    static object Num(params float[] v) => v;

    static void AJ1Chain(Twins f, PoseChainRow row)
    {
        var h = f.H;
        var c = row.Chain;
        var twin = AdvancedPoseParts.Chain(f.RB, row.Address);
        string at = row.Part.Split(':')[0];
        Info($"{at}: {c.ParticleCount} particles");
        string fields0 = Fields(c), docs0 = Docs(f.RA);
        var soft0 = ChainParams(c);
        var s = c.Source;

        JObject ChainRow() => (h.Call(new { cmd = "pose.chains", key = f.A })["chains"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x["chain"] == row.Address);
        JObject Set(string member, object value)
        {
            var q = new Dictionary<string, object> { ["cmd"] = "pose.chains.set", ["key"] = f.A, ["chain"] = row.Address, [member] = value };
            var r = h.Ok(q, $"{at}: pose.chains.set {member}");
            h.Rt.ApplyNow();
            return r;
        }
        void Clear() { h.Ok(new { cmd = "pose.chains.clear", key = f.A, chain = row.Address }, $"{at}: pose.chains.clear"); h.Rt.ApplyNow(); }

        // the soft members: the per-particle values are the scene value through the curves, the others bit-equal; base = the field
        var soft = new (string name, float v, Func<DynamicBoneChain, int, float> read, Func<global::DynamicBone, AnimationCurve> curve, float field, int slot)[]
        {
            ("damping", 0.31f, (x, i) => x.Damping(i), b => b.m_DampingDistrib, s.m_Damping, 0),
            ("elasticity", 0.12f, (x, i) => x.Elasticity(i), b => b.m_ElasticityDistrib, s.m_Elasticity, 1),
            ("stiffness", 0.44f, (x, i) => x.Stiffness(i), b => b.m_StiffnessDistrib, s.m_Stiffness, 2),
            ("inert", 0.27f, (x, i) => x.Inert(i), b => b.m_InertDistrib, s.m_Inert, 3),
            ("radius", 0.05f, (x, i) => x.Radius(i), b => b.m_RadiusDistrib, s.m_Radius, 4),
        };
        foreach (var (name, v, read, curve, field, slot) in soft)
        {
            var r = Set(name, v);
            double worst = 0;
            int others = 0;
            var now = ChainParams(c);
            for (int i = 0; i < c.ParticleCount; i++)
            {
                float u = c.TotalLength > 0f ? c.BoneLength(i) / c.TotalLength : 0f;
                float want = v * (c.TotalLength > 0f ? Curve(curve(s), u) : 1f);
                want = slot == 4 ? Mathf.Max(want, 0f) : Mathf.Clamp01(want);
                worst = Math.Max(worst, Math.Abs(read(c, i) - want));
                for (int k = 0; k < 5; k++)
                    if (k != slot && now[i * 5 + k] != soft0[i * 5 + k]) others++;
            }
            var pr = ChainRow();
            Check($"{at} {name}: the particles use it, the other members unchanged", worst <= 1e-6 && others == 0, $"worst {worst:G3}, {others} others differ");
            Check($"{at} {name}: pose.chains values = the scene value, base = the field, override = the member",
                pr != null && Math.Abs((double)pr["values"]?[name] - v) <= 1e-6 && Math.Abs((double)pr["base"]?[name] - field) <= 1e-6 && Math.Abs((double)pr["override"]?[name] - v) <= 1e-6 && Math.Abs((double)r["values"]?[name] - v) <= 1e-6,
                Short(pr?.ToString()));
            f.Step(2);
            Check($"{at} {name}: fields and documents unchanged", Fields(c) == fields0 && Docs(f.RA) == docs0);
            Clear();
            Check($"{at} {name}: removed, every particle value bit-equal to before", ChainParams(c).SequenceEqual(soft0));
        }

        // gravity and force: read each step; base = the field
        foreach (var (name, v) in new[] { ("gravity", new Vector3(0f, -0.2f, 0f)), ("force", new Vector3(0.1f, 0f, 0f)) })
        {
            f.Step(1);
            Set(name, new[] { v.x, v.y, v.z });
            double d0 = ParticleDiff(c, twin);
            f.Step(20);
            double d1 = ParticleDiff(c, twin);
            var pr = ChainRow();
            var ba = pr?["base"]?[name] as JArray;
            var fv = name == "gravity" ? s.m_Gravity : s.m_Force;
            Check($"{at} {name}: moves the particles, base = the field", d1 > d0 + 1e-4 && ba != null && Dist(new Vector3((float)ba[0], (float)ba[1], (float)ba[2]), fv) <= 1e-6, $"difference to the twin {d0:G3} -> {d1:G3}");
            Check($"{at} {name}: fields and documents unchanged", Fields(c) == fields0 && Docs(f.RA) == docs0);
            Clear();
            ResetPair(f);
            f.Step(4);
        }
        Check($"{at}: after gravity and force the pair runs in step again", ParticleDiff(c, twin) <= 1e-5, ParticleDiff(c, twin).ToString("G3"));

        // the freeze axis: a side force on both, then each axis on one: the chain leaves the twin (the component's own axis acts only in the
        // intended behaviour; a scene value acts), 0 keeps it with the twin
        var side = new ChainValues { Force = new Vec3(0.1f, 0f, 0f) };
        double best = 0;
        foreach (int axis in new[] { 0, 1, 2, 3 })
        {
            SetChains(h, f.A, (row.Address, new ChainValues { Force = side.Force, FreezeAxis = axis }));
            SetChains(h, f.B, (row.Address, side));
            ResetPair(f);
            f.Step(20);
            double d = ParticleDiff(c, twin);
            Info($"{at} freeze axis {axis}: difference to the twin {d:G3}");
            if (axis == 0) Check($"{at} freeze axis 0: as without one", d <= 1e-5, d.ToString("G3"));
            else best = Math.Max(best, d);
        }
        Check($"{at} a freeze axis acts", best > 1e-4, best.ToString("G3"));
        SetChains(h, f.A);
        SetChains(h, f.B);
        Check($"{at}: freeze axis: fields and documents unchanged", Fields(c) == fields0 && Docs(f.RA) == docs0);

        // the weight: SetWeight's semantics; base = the card's; removal gives it back
        float w0 = c.Weight;
        Set("weight", 0.5f);
        Check($"{at} weight: in use, base kept", c.Weight == 0.5f && c.BaseWeight == w0 && Math.Abs((double)ChainRow()?["base"]?["weight"] - w0) <= 1e-6, $"{c.Weight} base {c.BaseWeight}");
        f.Step(2);
        Set("weight", 0f);
        bool rest = true;
        for (int i = 0; i < c.ParticleCount; i++)
        {
            var t = c.ParticleTransform(i);
            if (t && (t.localPosition != c.RestLocalPosition(i) || Angle(t.localRotation, c.RestLocalRotation(i)) > 1e-3)) rest = false;
        }
        Check($"{at} weight 0: the bones at the rest pose", c.Weight == 0f && rest);
        Clear();
        Check($"{at} weight removed: the card's value", c.Weight == w0 && c.BaseWeight == w0);

        // enabled: false stops the chain and returns the bones; true does not override the card's no-shake
        Set("enabled", false);
        Check($"{at} enabled false: stopped at once", !c.Active && c.Enabled == false && c.BaseEnabled);
        Set("enabled", true);
        Check($"{at} enabled true: running", c.Active && c.Enabled);
        c.NoShake = true;
        f.Step(1);
        Check($"{at} enabled true does not override no-shake", !c.Active);
        c.NoShake = false;
        Clear();
        f.Step(1);
        Check($"{at} enabled removed: running, the switch as before", c.Active && c.Enabled);
        Check($"{at}: fields and documents unchanged at the end", Fields(c) == fields0 && Docs(f.RA) == docs0);
    }

    // ------------------------------------------------------------ AJ2

    static void AJ2()
    {
        var f = NewTwins(femaleRel, "female", "AJ2", physics: true);
        try
        {
            f.Step(2);
            var row = PickChain(f.RA, "acc:");
            if (row == null) { Warn("no accessory chain on the target"); return; }
            string id = row.Part.Substring("acc:".Length);
            const float card = 0.55f, scene = 0.2f;
            string path = "coord.accessories[\"" + id + "\"].dynamicBones";
            f.H.Ok(new { cmd = "chara.edit.set", key = f.A, set = new Dictionary<string, object> { [path] = new object[] { new { root = row.Root, damping = card } } } }, "a document's entry for the chain");
            f.H.Rt.ApplyNow();
            DynamicBoneChain C() => AdvancedPoseParts.Chain(f.RA, row.Address);
            float Used(DynamicBoneChain x) => x.Damping(x.ParticleCount - 1) / Curve(x.Source.m_DampingDistrib, x.TotalLength > 0f ? x.BoneLength(x.ParticleCount - 1) / x.TotalLength : 0f);
            var c = C();
            if (!Check("the chain after the document's edit", c != null, row.Address)) return;
            Check("the card's value is in the field and in use", c.Source.m_Damping == card && Math.Abs(Used(c) - card) <= 1e-5, $"{c.Source.m_Damping}, {Used(c)}");
            string coord = Docs(f.RA);

            SetChains(f.H, f.A, (row.Address, new ChainValues { Damping = scene }));
            c = C();
            Check("the scene value wins", c.Override?.Damping == scene && Math.Abs(Used(c) - scene) <= 1e-5 && c.Source.m_Damping == card, $"{Used(c)}, field {c.Source.m_Damping}");
            Check("the document byte-equal under the scene value", Docs(f.RA) == coord);

            var dyn = f.RA.Motion.Get<DynamicBoneStage>();
            dyn.Rebuild();
            c = C();
            Check("a chain rebuild keeps it", c.Override?.Damping == scene && Math.Abs(Used(c) - scene) <= 1e-5 && c.Source.m_Damping == card, $"{Used(c)}");
            Check("the document byte-equal after the rebuild", Docs(f.RA) == coord);

            f.Step(5);
            SetChains(f.H, f.A);
            c = C();
            Check("removal gives the card's value", c.Override == null && Math.Abs(Used(c) - card) <= 1e-5 && c.Source.m_Damping == card, $"{Used(c)}");
            Check("the document byte-equal after the removal", Docs(f.RA) == coord);

            // the card's value edited under a scene value: the field changes, the scene value stays
            SetChains(f.H, f.A, (row.Address, new ChainValues { Damping = scene }));
            f.H.Ok(new { cmd = "chara.edit.set", key = f.A, set = new Dictionary<string, object> { [path] = new object[] { new { root = row.Root, damping = 0.66f } } } }, "the document edited under a scene value");
            f.H.Rt.ApplyNow();
            c = C();
            Check("a document's edit keeps the scene value in use", c != null && c.Source.m_Damping == 0.66f && c.Override?.Damping == scene && Math.Abs(Used(c) - scene) <= 1e-5, c == null ? "no chain" : $"{Used(c)}, field {c.Source.m_Damping}");
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ AJ3

    static void AJ3()
    {
        var f = NewTwins(femaleRel, "female", "AJ3", physics: true);
        try
        {
            foreach (var k in new[] { f.A, f.B })
                f.H.Ok(new { cmd = "chara.anime", key = k, group = 11, category = 5, no = 0, time = 0f, speed = 1f }, "a moving row");
            ResetPair(f);
            var row = PickChain(f.RA, "hair:") ?? PickChain(f.RA, "acc:");
            if (!Check("a chain on the target", row != null)) return;
            var c = row.Chain;
            var twin = AdvancedPoseParts.Chain(f.RB, row.Address);
            f.Step(30);
            Check("the pair runs in step", ParticleDiff(c, twin) <= 1e-5, ParticleDiff(c, twin).ToString("G3"));

            var members = new (string name, ChainValues v)[]
            {
                ("damping", new ChainValues { Damping = 0.9f }),
                ("elasticity", new ChainValues { Elasticity = 0.9f }),
                ("stiffness", new ChainValues { Stiffness = 0.9f }),
                ("inert", new ChainValues { Inert = 0.9f }),
                ("radius", new ChainValues { Radius = 0.5f }),
            };
            foreach (var (name, v) in members)
            {
                var x0 = Enumerable.Range(0, c.ParticleCount).Select(i => twin.Position(i)).ToArray();
                var before = Enumerable.Range(0, c.ParticleCount).Select(i => c.Position(i)).ToArray();
                var p0 = Enumerable.Range(0, c.ParticleCount).Select(i => c.PrevPosition(i)).ToArray();
                var bones0 = Enumerable.Range(0, c.ParticleCount).Select(i => c.ParticleTransform(i) ? c.ParticleTransform(i).localRotation : Quaternion.identity).ToArray();
                SetChains(f.H, f.A, (row.Address, v));
                Check($"{name}: the edit touches no particle and no bone",
                    Enumerable.Range(0, c.ParticleCount).All(i => c.Position(i) == before[i] && c.PrevPosition(i) == p0[i] && (!c.ParticleTransform(i) || c.ParticleTransform(i).localRotation == bones0[i])));
                f.Step(1);
                double worst = double.NegativeInfinity, worstDiff = 0, worstMotion = 0;
                for (int i = 0; i < c.ParticleCount; i++)
                {
                    double diff = Dist(c.Position(i), twin.Position(i));
                    double motion = Dist(twin.Position(i), x0[i]);
                    if (diff - 2 * motion > worst) { worst = diff - 2 * motion; worstDiff = diff; worstMotion = motion; }
                }
                Check($"{name}: the next step is continuous", worst <= 1e-5 && twin.StepsLastFrame > 0, $"worst particle: difference {worstDiff:G3}, the step's motion {worstMotion:G3}");
                SetChains(f.H, f.A);
                ResetPair(f);
                f.Step(4);
            }

            // enabled: the pose restored, the particles reset
            SetChains(f.H, f.A, (row.Address, new ChainValues { Enabled = false }));
            bool rest = true;
            for (int i = 0; i < c.ParticleCount; i++)
            {
                var t = c.ParticleTransform(i);
                if (t && (t.localPosition != c.RestLocalPosition(i) || Angle(t.localRotation, c.RestLocalRotation(i)) > 1e-3)) rest = false;
            }
            Check("enabled false: the pose restored at once", !c.Active && rest);
            var held = Particles(c);
            f.Step(3);
            Check("enabled false: stays stopped", !c.Active && Particles(c).SequenceEqual(held));
            SetChains(f.H, f.A, (row.Address, new ChainValues { Enabled = true }));
            bool reset = c.Active;
            for (int i = 0; i < c.ParticleCount; i++)
            {
                var t = c.ParticleTransform(i);
                if (c.Position(i) != c.PrevPosition(i) || (t && Dist(c.Position(i), t.position) > 1e-5)) reset = false;
            }
            Check("enabled true: running, the particles reset at the bones", reset);
            SetChains(f.H, f.A);

            // AT3's chain half: one ApplyOverride with every soft member on the longest chain of the target, 1,000 times
            var longest = AdvancedPoseParts.Chains(f.RA).OrderByDescending(x => x.Chain.ParticleCount).First().Chain;
            longest.Override = new ChainOverride { Damping = 0.3f, Elasticity = 0.2f, Stiffness = 0.1f, Inert = 0.4f, Radius = 0.02f };
            var ticks = new long[1000];
            for (int i = 0; i < ticks.Length; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                longest.ApplyOverride();
                ticks[i] = Stopwatch.GetTimestamp() - t0;
            }
            longest.Override = null;
            longest.ApplyOverride();
            Array.Sort(ticks);
            double us = ticks[ticks.Length / 2] * 1e6 / Stopwatch.Frequency, p95 = ticks[ticks.Length * 95 / 100] * 1e6 / Stopwatch.Frequency;
            Measure($"AT3 ApplyOverride ({longest.ParticleCount} particles, the longest chain of the target, editor, no graphics): median {us:F1} us, p95 {p95:F1} us");

            // the bar's size: a chain of 50 bones with all five distribution curves, outside any character
            var top = new GameObject("at3_chain");
            try
            {
                var db = top.AddComponent<global::DynamicBone>();
                var t = top.transform;
                for (int i = 1; i < 50; i++)
                {
                    var g = new GameObject("b" + i);
                    g.transform.SetParent(t, false);
                    g.transform.localPosition = Vector3.up * 0.1f;
                    t = g.transform;
                }
                db.m_Root = top.transform;
                var curve = AnimationCurve.Linear(0f, 1f, 1f, 0.5f);
                db.m_DampingDistrib = curve; db.m_ElasticityDistrib = curve; db.m_StiffnessDistrib = curve; db.m_InertDistrib = curve; db.m_RadiusDistrib = curve;
                var big = new DynamicBoneChain(db, new DynamicBoneOptions(), new ColliderTable());
                big.Override = new ChainOverride { Damping = 0.3f, Elasticity = 0.2f, Stiffness = 0.1f, Inert = 0.4f, Radius = 0.02f };
                var ticks50 = new long[1000];
                for (int i = 0; i < ticks50.Length; i++)
                {
                    long t0 = Stopwatch.GetTimestamp();
                    big.ApplyOverride();
                    ticks50[i] = Stopwatch.GetTimestamp() - t0;
                }
                Array.Sort(ticks50);
                double us50 = ticks50[ticks50.Length / 2] * 1e6 / Stopwatch.Frequency, p9550 = ticks50[ticks50.Length * 95 / 100] * 1e6 / Stopwatch.Frequency;
                Measure($"AT3 ApplyOverride ({big.ParticleCount} particles, five curves, editor, no graphics): median {us50:F1} us, p95 {p9550:F1} us, bar 50 us");
                Check("AT3 ApplyOverride of 50 particles within the bar (the lead decides over it)", big.ParticleCount == 50 && us50 <= 50.0, $"{big.ParticleCount} particles, {us50:F1} us");
            }
            finally { UnityEngine.Object.DestroyImmediate(top); }
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ AJ4

    static void AJ4()
    {
        var f = NewTwins(femaleRel, "female", "AJ4", physics: true);
        try
        {
            f.Step(2);
            var rows = AdvancedPoseParts.Chains(f.RA);
            var hair = PickChain(f.RA, "hair:");
            if (!Check("a hair chain on the target", hair != null)) return;
            const float v = 0.33f;
            string addr = hair.Address;
            SetChains(f.H, f.A, (addr, new ChainValues { Damping = v }));
            string json0 = Block(f.H, f.A).ToJson();
            bool Acts() { var c = AdvancedPoseParts.Chain(f.RA, addr); return c != null && c.Override?.Damping == v && c.Source.m_Damping != v && c.Damping(0) != c.Source.m_Damping; }
            Check("it acts", Acts());

            f.H.Ok(new { cmd = "chara.change", key = f.A, path = femaleRel, sex = "female" }, "chara.change to the same card");
            f.H.Rt.ApplyNow();
            Check("after chara.change: found by address, a new chain with the value", Acts() && AdvancedPoseParts.Chain(f.RA, addr) != hair.Chain);
            f.RA.Motion.Get<DynamicBoneStage>().Rebuild();
            Check("after a chain rebuild: kept", Acts());

            f.H.Ok(new { cmd = "chara.change", key = f.A, path = maleRel, sex = "male" }, "chara.change to the other sex's target");
            f.H.Rt.ApplyNow();
            var c1 = AdvancedPoseDriver.Of(f.RA);
            bool there = AdvancedPoseParts.Chain(f.RA, addr) != null;
            Info(there ? "the other target has a chain of that address" : "the other target lacks the address");
            Check(there ? "the other target: it acts there" : "the other target: stale, the finding once", there ? Acts() : c1 != null && c1.StaleChains.SequenceEqual(new[] { addr }) && FindingCount(f.H, AdvancedPoseFindings.Stale, f.A) == 1,
                c1 == null ? "no state" : string.Join(",", c1.StaleChains));
            Check("the block keeps it", Block(f.H, f.A).ToJson() == json0);
            f.H.Ok(new { cmd = "chara.change", key = f.A, path = femaleRel, sex = "female" }, "chara.change back");
            f.H.Rt.ApplyNow();
            var c2 = AdvancedPoseDriver.Of(f.RA);
            Check("back: active again", Acts() && c2.StaleChains.Count == 0);

            // a hair swap and undo: the entry is on a chain of the front hair
            var front = AdvancedPoseParts.Chains(f.RA).FirstOrDefault(x => x.Part == "hair:1");
            string cur = EditValue(f.H, f.A, "chara.hair.front.item");
            string other = OtherRow(f.H, f.A, "chara.hair.front.item", cur);
            if (front == null || other == null) { Warn("no chain on the front hair or no other row"); return; }
            addr = front.Address;
            SetChains(f.H, f.A, (addr, new ChainValues { Damping = v }));
            json0 = Block(f.H, f.A).ToJson();
            Check("the front hair's chain acts", Acts());
            f.H.Ok(new { cmd = "chara.edit.item", key = f.A, path = "chara.hair.front.item", @ref = other }, "swap the part");
            f.H.Rt.ApplyNow();
            var c3 = AdvancedPoseDriver.Of(f.RA);
            bool found = AdvancedPoseParts.Chain(f.RA, addr) != null;
            Info(found ? "the other row has a chain of that root name" : "the other row lacks it");
            Check(found ? "after the swap: found by address" : "after the swap: stale", found ? Acts() : c3.StaleChains.Contains(addr), string.Join(",", c3.StaleChains));
            Check("the block keeps it", Block(f.H, f.A).ToJson() == json0);
            f.H.Ok(new { cmd = "undo" }, "undo the swap");
            f.H.Rt.ApplyNow();
            Check("after undo: active again", Acts() && AdvancedPoseDriver.Of(f.RA).StaleChains.Count == 0);
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ AJ5

    static float[] BustSoft(BustChain c)
    {
        var l = new List<float>();
        for (int i = 0; i < c.ParticleCount; i++) { l.Add(c.Damping(i)); l.Add(c.Elasticity(i)); l.Add(c.Stiffness(i)); l.Add(c.Inert(i)); }
        return l.ToArray();
    }

    static void AJ5()
    {
        var f = NewTwins(femaleRel, "female", "AJ5", physics: true);
        try
        {
            f.Step(10);
            var sa = f.RA.Motion.Get<BustStage>();
            var sb = f.RB.Motion.Get<BustStage>();
            if (!Check("the bust stage on both", sa != null && sb != null)) return;
            foreach (var (name, kind) in new[] { ("breastL", 0), ("hipR", 3) })
            {
                var a = sa.Chains[kind];
                var b = sb.Chains[kind];
                if (!Check(name + ": the chain exists", a != null && b != null && a.ParticleCount >= 2)) continue;
                var soft0 = BustSoft(a);
                var g0 = a.Gravity;
                var members = new (string m, BustValues v, float value, Func<BustChain, int, float> read)[]
                {
                    ("damping", new BustValues { Damping = 0.61f }, 0.61f, (x, i) => x.Damping(i)),
                    ("elasticity", new BustValues { Elasticity = 0.62f }, 0.62f, (x, i) => x.Elasticity(i)),
                    ("stiffness", new BustValues { Stiffness = 0.63f }, 0.63f, (x, i) => x.Stiffness(i)),
                    ("inert", new BustValues { Inert = 0.64f }, 0.64f, (x, i) => x.Inert(i)),
                };
                foreach (var (m, v, value, read) in members)
                {
                    SetBust(f.H, f.A, name, v);
                    bool all = Enumerable.Range(0, a.ParticleCount).All(i => read(a, i) == value);
                    f.Step(2);
                    all &= Enumerable.Range(0, a.ParticleCount).All(i => read(a, i) == value);
                    Check($"{name} {m}: live on every particle, at once and after steps", all);
                    SetBust(f.H, f.A, name, new BustValues());
                    Check($"{name} {m}: removal = the card's arrays bit-equal", BustSoft(a).SequenceEqual(soft0) && a.Override == null);
                }
                foreach (var (m, value) in new[] { ("gravity", new Vector3(0f, -0.03f, 0f)), ("force", new Vector3(0.02f, 0f, 0f)) })
                {
                    double d0 = ParticleDiff(a, b);
                    SetBust(f.H, f.A, name, m == "gravity" ? new BustValues { Gravity = new Vec3(value.x, value.y, value.z) } : new BustValues { Force = new Vec3(value.x, value.y, value.z) });
                    Check($"{name} {m}: the effective value, the live one untouched", (m == "gravity" ? a.EffectiveGravity : a.EffectiveForce) == value && a.Gravity == g0 && a.Source.Force == b.Source.Force);
                    f.Step(10);
                    double d1 = ParticleDiff(a, b);
                    Check($"{name} {m}: moves the particles", d1 > d0 + 1e-5, $"{d0:G3} -> {d1:G3}");
                    SetBust(f.H, f.A, name, new BustValues());
                    Check($"{name} {m}: removed", a.Override == null && a.EffectiveGravity == g0);
                    ResetPair(f);
                    f.Step(4);
                }
                SetBust(f.H, f.A, name, new BustValues { Enabled = false });
                Check($"{name} enabled false: off at once", !a.Active);
                var held = Particles(a);
                f.Step(2);
                Check($"{name} enabled false: stays off, the particles stand", !a.Active && Particles(a).SequenceEqual(held));
                SetBust(f.H, f.A, name, new BustValues { Enabled = true });
                Check($"{name} enabled true: on", a.Active);
                SetBust(f.H, f.A, name, new BustValues());
                f.Step(1);
                Check($"{name}: on after the removal", a.Active);
            }

            // true never overrides the FK breast
            {
                var a = sa.Chains[0];
                SetBust(f.H, f.A, "breastL", new BustValues { Enabled = true });
                sa.FkBreastActive = true;
                f.Step(1);
                Check("breastL enabled true does not override the FK breast", !a.Active);
                sa.FkBreastActive = false;
                SetBust(f.H, f.A, "breastL", new BustValues());
                f.Step(1);
                Check("breastL back on", a.Active);
            }

            // a softness edit under a scene value: the scene value stays live, the others and the table follow the card; removal = the twin
            {
                var a = sa.Chains[0];
                var b = sb.Chains[0];
                Check("the pair's arrays are equal before the edit", BustSoft(a).SequenceEqual(BustSoft(b)));
                SetBust(f.H, f.A, "breastL", new BustValues { Damping = 0.77f });
                foreach (var k in new[] { f.A, f.B })
                    f.H.Ok(new { cmd = "chara.edit.set", key = k, set = new Dictionary<string, object> { ["chara.body.bust.softness"] = 0.9f } }, "softness 0.9");
                f.Step(2);
                a.BaseSoft(0, out float bd, out _, out _, out _);
                Check("the scene value stays live after the card's write", a.Damping(0) == 0.77f, a.Damping(0).ToString("G9"));
                Check("the member it does not hold follows the card", a.Elasticity(0) == b.Elasticity(0) && a.Stiffness(0) == b.Stiffness(0), $"{a.Elasticity(0)} / {b.Elasticity(0)}");
                Check("the base follows the card", bd == b.Damping(0), $"{bd} / {b.Damping(0)}");
                SetBust(f.H, f.A, "breastL", new BustValues());
                Check("removal = the twin's arrays bit-equal", BustSoft(a).SequenceEqual(BustSoft(b)) && a.Gravity == b.Gravity);
            }
        }
        finally { f.Dispose(); }

        // the other sex: the chains run by the stage's male option (on by default): a scene value acts while they run; off: stored, no effect
        var g = NewTwins(maleRel, "male", "AJ5 male", physics: true);
        try
        {
            g.Step(4);
            var sa = g.RA.Motion.Get<BustStage>();
            if (!Check("male: the bust stage", sa != null)) return;
            var a = sa.Chains[0];
            if (a == null) { Info("male: no breast chain"); return; }
            bool on = sa.Options.MaleBustPhysics;
            Info($"male: the stage's option {on}, chain active {a.Active}");
            SetBust(g.H, g.A, "breastL", new BustValues { Damping = 0.61f });
            g.Step(2);
            Check("male: the scene value in use while the chains run", !a.Active || a.Damping(0) == 0.61f);
            sa.Options.MaleBustPhysics = false;
            sa.ApplySwitches();
            var x0 = Enumerable.Range(0, a.ParticleCount).Select(i => a.Position(i)).ToArray();
            g.Step(5);
            Check("male, option off: stored, the chain off, the particles stand", !a.Active && Enumerable.Range(0, a.ParticleCount).All(i => a.Position(i) == x0[i]) && Block(g.H, g.A).Bust.BreastL.Damping == 0.61f);
            var pb = g.H.Ok(new { cmd = "pose.bust", key = g.A }, "pose.bust");
            var row = (pb["chains"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x["chain"] == "breastL");
            Check("male, option off: pose.bust active false", row != null && (bool?)row["active"] == false, Short(row?.ToString()));
            sa.Options.MaleBustPhysics = on;
            SetBust(g.H, g.A, "breastL", new BustValues());
        }
        finally { g.Dispose(); }
    }
}

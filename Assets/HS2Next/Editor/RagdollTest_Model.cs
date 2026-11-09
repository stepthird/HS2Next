// RagdollTest, the model of a body (Phase 6.7 package P): the joints and muscles on paper against what was built.
//   RM3  a turn of the child about a joint's axis goes towards flexion, on every joint; the negated rule fails every row
//   RM4  spring, damper, subtree and pivot inertia of one arm against a computation by hand
//   RM5  a joint driven to a wanted rotation ends within .5 degrees
//   RM8  16 parts, 15 joints; a missing bone gives ragdoll.noBones, no body, the block kept; the same card with the bone gives a body
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Ragdoll;
using HS2Next.Scenes.Runtime;
using UnityEngine;

public static partial class RagdollTest
{
    static void Headless_B_Model()
    {
        Group("RM3", "the sign of a joint's axis", RM3);
        Group("RM4", "muscles on paper", RM4);
        Group("RM5", "a joint goes where it is told", RM5);
        Group("RM8", "a character that lacks a bone", RM8);
    }

    /// <summary>A body standing on a floor at 0, the idle row still.</summary>
    static Host Standing(string rel, string sex, string what, out string k, bool physics = false)
    {
        var h = NewHost();
        k = LoadChara(h, rel, sex, what, physics);
        Idle(h, k);
        h.Frames(3);
        FixedFloor(h);
        return h;
    }

    /// <summary>The rest pose in the pelvis' frame, from what the joints keep: the rotation and the place of every part.</summary>
    static void RestOf(RagdollBody b, out Quaternion[] rot, out Vector3[] at)
    {
        int n = b.Parts.Count;
        rot = new Quaternion[n];
        at = new Vector3[n];
        rot[0] = Quaternion.identity;
        for (int i = 1; i < n; i++)
        {
            var p = b.Parts[i];
            int q = p.Parent.Index;
            rot[i] = rot[q] * p.StartLocal;
            at[i] = at[q] + rot[q] * p.JointAnchor;
        }
    }

    static void RM3()
    {
        foreach (var (name, rel, sex) in Targets())
        {
            var h = Standing(rel, sex, name, out string k);
            try
            {
                var b = Body(h, k, name);
                RestOf(b, out var rot, out var at);
                Vector3 U = Vector3.up, F = Vector3.forward, R = Vector3.Cross(U, F);
                int plus = 0, minus = 0;
                string bad = "";
                for (int i = 1; i < b.Parts.Count; i++)
                {
                    var p = b.Parts[i];
                    var row = RagdollMath.JointOf(p.Key);
                    int side = i & 1;
                    var arm = (at[6 + side] - at[4 + side]).normalized;
                    var leg = (at[12 + side] - at[10 + side]).normalized;
                    var foot = rot[i] * Vector3.up;
                    Vector3 Dir(RagdollAxis a)
                    {
                        switch (a)
                        {
                            case RagdollAxis.Right: return R;
                            case RagdollAxis.Front: return F;
                            case RagdollAxis.Up: return U;
                            case RagdollAxis.Back: return -F;
                            case RagdollAxis.Down: return -U;
                            case RagdollAxis.Arm: return arm;
                            case RagdollAxis.Leg: return leg;
                            default: return foot;
                        }
                    }
                    var child = Dir(row.Child);
                    var flexion = Dir(row.Flexion);
                    var axis = rot[p.Parent.Index] * p.FlexAxis;
                    if (Vector3.Dot(Quaternion.AngleAxis(10f, axis) * child - child, flexion) > 0f) plus++; else bad += " " + p.Key;
                    if (Vector3.Dot(Quaternion.AngleAxis(10f, -axis) * child - child, flexion) > 0f) minus++;
                }
                Check(name + ": on every joint a turn of the child about its axis goes towards flexion", plus == 15, plus + " of 15" + bad);
                Check(name + ": Present: with the rule negated no row holds", minus == 0, minus + " of 15");
            }
            finally { h.Dispose(); }
        }
    }

    static void RM4()
    {
        var h = Standing(femaleRel, "female", "RM4", out string k);
        try
        {
            var b = Body(h, k, "RM4");
            h.Frames(2);
            RestOf(b, out var rot, out var at);
            var arm = b.Part("upperarm_L");
            double sub = 0;
            foreach (var key in new[] { "upperarm_L", "forearm_L", "hand_L" })
            {
                var q = b.Part(key);
                var com = at[q.Index] + rot[q.Index] * q.LocalCom;
                sub += q.Rb.inertiaTensor.magnitude + q.Rb.mass * (com - at[arm.Index]).sqrMagnitude;
            }
            double pivot = arm.Rb.inertiaTensor.magnitude + arm.Rb.mass * arm.LocalCom.sqrMagnitude;
            Check("subtree inertia", Math.Abs(sub - arm.SubInertia) <= 1e-3 * sub, $"by hand {sub:G6}, built {arm.SubInertia:G6}");
            Check("pivot inertia", Math.Abs(pivot - arm.PivotInertia) <= 1e-3 * pivot, $"by hand {pivot:G6}, built {arm.PivotInertia:G6}");
            var d = arm.Joint.slerpDrive;
            double w = 20, spring = w * w * sub, damper = 3 * pivot + 2 * 1 * w * sub;
            Check("spring", Math.Abs(spring - d.positionSpring) <= 1e-3 * spring, $"by hand {spring:G6}, built {d.positionSpring:G6}");
            Check("damper", Math.Abs(damper - d.positionDamper) <= 1e-3 * damper, $"by hand {damper:G6}, built {d.positionDamper:G6}");
        }
        finally { h.Dispose(); }
    }

    static void RM5()
    {
        foreach (var (name, rel, sex) in Targets())
        {
            var h = Standing(rel, sex, name, out string k);
            try
            {
                World(h, w => { w.Values.Gravity = 0f; w.Floor.Height = -200f; });
                Preset(h, k, RagdollPreset.Limp);
                var b = Body(h, k, name);
                var wanted = new Dictionary<string, Quaternion>();
                double least = double.MaxValue;
                foreach (var p in b.Parts.Skip(1))
                {
                    float a = Mathf.Min(60f, (p.Key == RagdollParts.Chest ? 0.95f : 0.85f) * (float)RagdollMath.JointOf(p.Key).MaxFlex);
                    var q = Quaternion.AngleAxis(a, p.FlexAxis) * p.StartLocal;
                    wanted[p.Key] = q;
                    least = Math.Min(least, Deg(p.LocalRotation, q));
                }
                Block(h, k, blk => { foreach (var kv in wanted) blk.Holds.Add(new RagdollHold { Part = kv.Key, Rot = new Quat4(kv.Value.x, kv.Value.y, kv.Value.z, kv.Value.w) }); });
                h.Frames(240);
                double worst = 0;
                string where = "";
                foreach (var p in b.Parts.Skip(1))
                {
                    double d = Deg(p.LocalRotation, wanted[p.Key]);
                    if (d > worst) { worst = d; where = p.Key; }
                }
                Check(name + ": every joint ends within .5 degrees of the wanted rotation", worst <= 0.5, $"at most {worst:G4} degrees on {where}");
                Check(name + ": Present: before the drive each was more than 20 degrees off", least > 20.0, $"at least {least:F2} degrees");
            }
            finally { h.Dispose(); }
        }
    }

    static void RM8()
    {
        var h = Standing(femaleRel, "female", "RM8", out string k);
        try
        {
            var r = h.Rt.FindChara(k);
            var bones = r.Context.BodyBones;
            var kept = bones["cf_J_Spine03"];
            bones.Remove("cf_J_Spine03");
            var res = h.Part.Build(k, ExecuteOptions.NotRecorded);
            int findings = h.Rt.Findings.Count(f => f.Code == RagdollFindings.NoBones && f.NodeId == k);
            var block = ((CharaNodeState)h.Rt.Model.Find(k).State).Ragdoll;
            Check("a missing bone gives no body", h.Part.BodyOf(k) == null && h.Part.World == null, res.Ok ? "the edit went through" : res.Code);
            Check("ragdoll.noBones, once", findings == 1, findings + " findings");
            Check("the block is kept", block.On);
            bones["cf_J_Spine03"] = kept;
            Block(h, k, b => b.Limp = false);
            Block(h, k, b => b.Values.Mass = 51f);
            var body = h.Part.BodyOf(k);
            Check("Present: the same card with the bone gives a body", body != null);
            Check("16 parts, 15 joints", body != null && body.Parts.Count == 16 && body.Parts.Count(p => p.Joint) == 15);
        }
        finally { h.Dispose(); }
    }
}

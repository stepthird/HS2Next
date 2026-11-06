// The motion pass of a scene with active constraints: the compiled steps, walked once per frame and per scene step. A step is a unit, a
// character's or an item's motion, or a constraint whose target is a node or an IK target: it reads its source, computes, and sets the
// target's channels. A constraint with a bone target runs inside its character's step, at the stage that writes driven bones, one at a
// time, so the next one reads and converts against a bone already written. Nothing here allocates per frame or keeps time.
// A target's channel holds what its stack comes to: the first constraint blends over the base, each next one over the value so far,
// and the last sets the slot, once. The base is what shows without this owner: the next slot of the channel, else the model on a node,
// the bone's value at the stage on a bone, the target's value on an IK target. With no base at all a weight counts as 1 for that frame.
// A value that did not change sets nothing. A write here asks no settle: a character whose time stands would run one in every frame
// behind a moving source. Instead one is asked when a character's driven values change in a frame and not in the next.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using HS2Next.Motion;
using HS2Next.Motion.AdvancedPose;
using HS2Next.Motion.Ik;
using HS2Next.Save;
using HS2Next.Scenes.Constraints;
using HS2Next.Scenes.Overrides;
using HS2Next.Scenes.Routes;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>Whether a character's driven values move: what asks its one settle when they stop.</summary>
    internal sealed class ConstraintDrive
    {
        public CharaRealization Chara;
        public bool Changed, Moving;
        public int Settles;
    }

    /// <summary>One target of a compile: what it writes, against which frame, through which channels.</summary>
    internal sealed class ConstraintTarget
    {
        public ConstraintTargetKind Kind;
        public NodeRealization Node;
        /// <summary>The holder or the bone; null for an IK target.</summary>
        public Transform Transform;
        /// <summary>What local values are local to: a holder's or a bone's parent, a character's root.</summary>
        public Transform Frame;
        public DrivenBone Bone;
        public IkStage Ik;
        public int IkIndex = -1;
        public ConstraintChannel Pos, Rot, Scale;
        /// <summary>The character whose settle this target's values concern; else null.</summary>
        public ConstraintDrive Drive;
    }

    /// <summary>One active constraint as the pass runs it.</summary>
    internal sealed class ConstraintLive
    {
        public int Index;
        public ConstraintParts Parts;
        public ConstraintOffsetValue Offset;
        public double Weight;
        public Transform Source;
        public ConstraintTarget Target;
        public bool WritesPos, WritesRot, WritesScale;
        /// <summary>This constraint ends the stack of that channel, so it sets the slot.</summary>
        public bool LastPos, LastRot, LastScale;
        // the last local rotation of a look, held while source and target stand at one point
        public bool HasRot;
        public Quatd LastRotValue;
    }

    /// <summary>One step of the pass: exactly one member is set.</summary>
    internal struct ConstraintRunStep
    {
        public CharaRealization Chara;
        public ItemRealization Item;
        public ConstraintLive Constraint;
    }

    /// <summary>The bone targets of one character, in the plan's order: what its stage calls.</summary>
    internal sealed class ConstraintBoneRun : IDrivenEvaluator
    {
        readonly ConstraintEvaluator owner;
        readonly ConstraintLive[] links;

        public ConstraintBoneRun(ConstraintEvaluator owner, ConstraintLive[] links)
        {
            this.owner = owner;
            this.links = links;
        }

        public void EvaluateBones(DrivenBones set, in MotionFrame frame) => owner.RunBones(set, links);
    }

    public sealed class ConstraintEvaluator : IMotionPass
    {
        static readonly Vec3d One = new Vec3d(1, 1, 1);

        readonly SceneRuntime rt;
        readonly double[] below = new double[9];
        readonly HashSet<string> faults = new HashSet<string>(StringComparer.Ordinal);
        ConstraintRunStep[] steps = Array.Empty<ConstraintRunStep>();
        ConstraintDrive[] drives = Array.Empty<ConstraintDrive>();
        ConstraintFrame frame;
        long walk, boneWalk, ticks;

        internal ConstraintEvaluator(SceneRuntime runtime) { rt = runtime; }

        /// <summary>The evaluation's wall time of each walk: the constraints and their part of the stage, not the characters' own steps.</summary>
        public TimelinePerfWindow Perf { get; } = new TimelinePerfWindow();
        /// <summary>Walks so far (tests).</summary>
        public long Walks => walk;

        internal void Install(ConstraintRunStep[] newSteps, ConstraintDrive[] newDrives)
        {
            steps = newSteps;
            drives = newDrives;
        }

        // ------------------------------------------------------------ the walk

        public void Run(float? dt, bool all)
        {
            walk++;
            ticks = 0;
            var s = steps;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i].Chara != null) rt.StepInPass(s[i].Chara, dt, all);
                else if (s[i].Item != null) rt.StepItemInPass(s[i].Item, dt);
                else RunNodeOrIk(s[i].Constraint);
            }
            EndWalk();
            Perf.Add(ticks * 1e6 / Stopwatch.Frequency);
        }

        void RunNodeOrIk(ConstraintLive c)
        {
            long t0 = Stopwatch.GetTimestamp();
            try
            {
                Evaluate(c, walk);
                var t = c.Target;
                if (c.LastPos) Commit(t, t.Pos, walk);
                if (c.LastRot) Commit(t, t.Rot, walk);
                if (c.LastScale) Commit(t, t.Scale, walk);
            }
            catch (Exception e) { Fault(e); }
            finally { rt.QuietTransforms = false; }
            ticks += Stopwatch.GetTimestamp() - t0;
        }

        internal void RunBones(DrivenBones set, ConstraintLive[] links)
        {
            long t0 = Stopwatch.GetTimestamp();
            long w = ++boneWalk;
            for (int i = 0; i < links.Length; i++)
            {
                var c = links[i];
                try
                {
                    Evaluate(c, w);
                    var t = c.Target;
                    var channels = DrivenChannels.None;
                    if (c.LastPos) { Commit(t, t.Pos, w); channels |= DrivenChannels.Position; }
                    if (c.LastRot) { Commit(t, t.Rot, w); channels |= DrivenChannels.Rotation; }
                    if (c.LastScale) { Commit(t, t.Scale, w); channels |= DrivenChannels.Scale; }
                    if (channels != DrivenChannels.None) set.Write(t.Bone, channels);
                }
                catch (Exception e) { Fault(e); }
                finally { rt.QuietTransforms = false; }
            }
            ticks += Stopwatch.GetTimestamp() - t0;
        }

        void Fault(Exception e)
        {
            if (faults.Add(e.GetType().Name)) Debug.LogWarning("constraints: an evaluation threw: " + e);
        }

        // the slot takes the stack's result; a write of the evaluator asks no settle
        void Commit(ConstraintTarget t, ConstraintChannel ch, long w)
        {
            if (ch.Walk != w) return;
            rt.QuietTransforms = true;
            bool changed = ch.Flush();
            rt.QuietTransforms = false;
            if (changed && t.Drive != null) t.Drive.Changed = true;
        }

        // Values that changed in the frame before and not in this one: the motion ended. What a settle moved inside its own frames is no motion.
        void EndWalk()
        {
            var d = drives;
            for (int i = 0; i < d.Length; i++)
            {
                var x = d[i];
                var m = x.Chara.Motion;
                if (!m) continue;
                if (m.Settles != x.Settles)
                {
                    x.Settles = m.Settles;
                    x.Changed = x.Moving = false;
                    continue;
                }
                if (x.Changed)
                {
                    x.Changed = false;
                    x.Moving = true;
                }
                else if (x.Moving)
                {
                    x.Moving = false;
                    x.Chara.WantSettle();
                }
            }
        }

        // ------------------------------------------------------------ one constraint

        static Vec3d V(Vector3 v) => new Vec3d(v.x, v.y, v.z);
        static Vec3d V(Vec3 v) => new Vec3d(v.X, v.Y, v.Z);
        static Quatd Q(Quaternion q) => ConstraintMath.Normalized(new Quatd(q.x, q.y, q.z, q.w));
        static Quatd Euler(Vector3 e) => ConstraintMath.Normalized(Quatd.Euler(e.x, e.y, e.z));
        static Quatd Euler(Vec3 e) => ConstraintMath.Normalized(Quatd.Euler(e.X, e.Y, e.Z));

        void ReadFrame(Transform t)
        {
            var m = t.localToWorldMatrix;
            frame.M00 = m.m00; frame.M01 = m.m01; frame.M02 = m.m02; frame.M03 = m.m03;
            frame.M10 = m.m10; frame.M11 = m.m11; frame.M12 = m.m12; frame.M13 = m.m13;
            frame.M20 = m.m20; frame.M21 = m.m21; frame.M22 = m.m22; frame.M23 = m.m23;
            frame.Rot = Q(t.rotation);
            frame.Scale = V(t.lossyScale);
        }

        /// <summary>What a transform shows in the world.</summary>
        internal static ConstraintPose PoseOf(Transform t, bool scale)
        {
            t.GetPositionAndRotation(out var p, out var q);
            return new ConstraintPose(V(p), Q(q), scale ? V(t.lossyScale) : One);
        }

        void Evaluate(ConstraintLive c, long w)
        {
            var t = c.Target;
            if (!c.Source || !t.Frame) return;   // gone since the compile: the next one says so
            var s = PoseOf(c.Source, (c.Parts & (ConstraintParts.Scale | ConstraintParts.Parent)) != 0);
            ReadFrame(t.Frame);
            var targetPos = Vec3d.Zero;
            var targetUp = Vec3d.Up;
            if ((c.Parts & ConstraintParts.LookAt) != 0) LookFrom(t, w, out targetPos, out targetUp);
            var world = ConstraintMath.World(c.Parts, c.Offset, s, targetPos, targetUp);

            if (c.WritesPos && world.HasPos && frame.InversePoint(world.Pos, out var lp)) FoldPos(t, lp, c.Weight, w);
            if (c.WritesRot)
            {
                if (world.HasRot)
                {
                    c.LastRotValue = ConstraintMath.LocalRotation(world.Rot, frame);
                    c.HasRot = true;
                }
                if (c.HasRot) FoldRot(t, c.LastRotValue, c.Weight, w);
            }
            if (c.WritesScale && world.HasScale)
            {
                bool flat = Math.Abs(frame.Scale.X) < ConstraintMath.ScaleFloor || Math.Abs(frame.Scale.Y) < ConstraintMath.ScaleFloor || Math.Abs(frame.Scale.Z) < ConstraintMath.ScaleFloor;
                var keep = One;
                if (flat) TryBaseVec(t, t.Scale, out keep);
                var local = t.Kind == ConstraintTargetKind.Node
                    ? ConstraintMath.NodeScale(world.Scale, frame, t.Node.Node.Doc.Transform.InheritScale, keep)
                    : ConstraintMath.LocalScale(world.Scale, frame, keep);
                FoldScale(t, local, c.Weight, w);
            }
        }

        // Where a look aims from and the up it falls back on: the stack so far, else what shows without this owner, so a look does not
        // depend on what the frame before left in the transform.
        void LookFrom(ConstraintTarget t, long w, out Vec3d pos, out Vec3d up)
        {
            if (t.Pos != null)
            {
                if (t.Pos.Walk == w) pos = frame.Point(t.Pos.Vector);
                else pos = TryBaseVec(t, t.Pos, out var b) ? frame.Point(b) : ShownPos(t);
            }
            else pos = ShownPos(t);
            Quatd r;
            if (t.Rot.Walk == w) r = t.Rot.Rotation;
            else if (!TryBaseRot(t, out r)) r = Quatd.Identity;
            up = (frame.Rot * r).Rotate(Vec3d.Up);
        }

        Vec3d ShownPos(ConstraintTarget t)
        {
            if (t.Transform) return V(t.Transform.position);
            return TryIk(t, out var v) ? frame.Point(V(v.Pos)) : frame.Point(Vec3d.Zero);
        }

        void FoldPos(ConstraintTarget t, Vec3d value, double weight, long w) => FoldVec(t, t.Pos, value, weight, w);
        void FoldScale(ConstraintTarget t, Vec3d value, double weight, long w) => FoldVec(t, t.Scale, value, weight, w);

        void FoldVec(ConstraintTarget t, ConstraintChannel ch, Vec3d value, double weight, long w)
        {
            if (ch.Walk != w)
            {
                ch.Walk = w;
                if (weight >= 1 || !TryBaseVec(t, ch, out var b))
                {
                    ch.Begin(value);
                    return;
                }
                ch.Begin(b);
            }
            ch.Blend(value, weight);
        }

        void FoldRot(ConstraintTarget t, Quatd value, double weight, long w)
        {
            var ch = t.Rot;
            if (ch.Walk != w)
            {
                ch.Walk = w;
                if (weight >= 1 || !TryBaseRot(t, out var b))
                {
                    ch.Begin(value);
                    return;
                }
                ch.Begin(b);
            }
            ch.Blend(value, weight);
        }

        // ------------------------------------------------------------ the base

        bool TryIk(ConstraintTarget t, out IkTargetValue v)
        {
            var s = t.Ik;
            v = s.Targets[t.IkIndex];
            if (v.Has) return true;
            if (!s.HasLastInput) return false;
            v = s.LastInput[t.IkIndex];
            return v.Has;
        }

        bool TryBaseVec(ConstraintTarget t, ConstraintChannel ch, out Vec3d b)
        {
            if (rt.Overrides.ValueBelow(ch.Slot, below))
            {
                b = new Vec3d(below[0], below[1], below[2]);
                return true;
            }
            bool pos = ch == t.Pos;
            switch (t.Kind)
            {
                case ConstraintTargetKind.Node:
                {
                    var m = t.Node.Node.Doc.Transform;
                    b = V(pos ? m.Pos : m.Scale);
                    return true;
                }
                case ConstraintTargetKind.Bone:
                    b = V(pos ? t.Transform.localPosition : t.Transform.localScale);
                    return true;
                default:
                    if (TryIk(t, out var v))
                    {
                        b = V(v.Pos);
                        return true;
                    }
                    b = default;
                    return false;
            }
        }

        bool TryBaseRot(ConstraintTarget t, out Quatd b)
        {
            if (rt.Overrides.ValueBelow(t.Rot.Slot, below))
            {
                b = ConstraintMath.Normalized(new Quatd(below[0], below[1], below[2], below[3]));
                return true;
            }
            switch (t.Kind)
            {
                case ConstraintTargetKind.Node:
                    b = Euler(t.Node.Node.Doc.Transform.Rot);
                    return true;
                case ConstraintTargetKind.Bone:
                    b = Q(t.Transform.localRotation);
                    return true;
                default:
                    if (TryIk(t, out var v))
                    {
                        b = Euler(v.Rot);
                        return true;
                    }
                    b = Quatd.Identity;
                    return false;
            }
        }
    }
}

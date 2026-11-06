// IK state of one built scene character (Phase 3.4 package B): the discovered
// IkBones, the IkStage on its CharaMotion (MotionPhase.Ik, registered by IkDriver at build, before the FK driver's stages),
// the applied groups and the effective targets. Created by IkDriver.Apply at build and found again through
// CharaMotion.Get<IkStage>().Owner, so it lives and dies with the character's root.
//   applied(g)   = chara.ik.enabled AND chara.ik.groups.<g> (IkStage.Applied, then IkStage.Refresh)
//   targets      a model value → IkStage.SetTarget (drops a snapshot); a missing target → IkStage.ClearTarget unless the
//                stage already holds a snapshot for it (kept until a value arrives or the character is rebuilt; never
//                written to the model)
//   first frame  IkOptions.MissingTargetsFirstFrame (HS2's InitTarget): after the first step that follows the
//                build, the source-bone values of the final pose are kept (FirstFrame) and every missing target takes
//                them as its snapshot; the capture rule writes them
//   suspend      IkStage.SetSuspended (StudioDefaultPoseScope)
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Motion;
using HS2Next.Motion.Ik;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class IkCharacter
    {
        public CharaRealization Realization { get; }
        public GameObject Root { get; }
        public IkBones Bones { get; }
        public IkStage Stage { get; }
        public bool Suspended { get; private set; }
        /// <summary>ik.enabled as last read.</summary>
        public bool Enabled { get; private set; }
        /// <summary>The source-bone values after the first step following the build (IkOptions.MissingTargetsFirstFrame), or null.</summary>
        public IkTargetValue[] FirstFrame { get; private set; }
        public readonly List<string> Log = new List<string>();

        readonly CharaMotion motion;
        FirstFrameStage firstFrameStage;

        internal IkCharacter(CharaRealization r, IkBones bones)
        {
            Realization = r;
            Root = r.Root;
            Bones = bones;
            motion = r.Motion;
            Stage = new IkStage(r.Root.transform, bones, motion) { Owner = this };
            if (motion)
            {
                motion.Add(Stage);
                if (IkOptions.MissingTargetsFirstFrame)
                {
                    firstFrameStage = new FirstFrameStage(this);
                    motion.Add(firstFrameStage);
                }
            }
        }

        /// <summary>Applied(g) = ik.enabled AND ik.groups.g (and the group's bones present).</summary>
        public bool Applied(IkGroup g) => Stage.IsApplied(g);

        /// <summary>True while the stage solves (a group applied, not suspended).</summary>
        public bool Running => Stage.Enabled && !Stage.Suspended && Stage.AnyApplied;

        /// <summary>The effective target of <paramref name="t"/> (model value, or the runtime snapshot; Has false = none yet).</summary>
        public IkTargetValue Effective(IkTargetId t) => Stage.Targets[(int)t];

        /// <summary>The target as the solve takes it: <see cref="Effective"/> with a runtime override's channels over it. What the handle shows;
        /// never what an edit fills its missing half from.</summary>
        public IkTargetValue Shown(IkTargetId t)
        {
            var v = Stage.Targets[(int)t];
            var o = Stage.TargetOverrides[(int)t];
            if (o.HasPos) { v.Pos = o.Pos; v.Has = true; }
            if (o.HasRot) { v.Rot = o.Rot; v.Has = true; }
            return v;
        }

        /// <summary>The bones the last solve wrote (the "IK-driven" mark of the 3.5 UI).</summary>
        public IReadOnlyCollection<Transform> Owned => Stage.Owned;

        /// <summary>The names of the targets whose effective value is a runtime snapshot.</summary>
        public IEnumerable<string> SnapshotNames() =>
            Enumerable.Range(0, IkGroupInfo.TargetCount).Where(i => Stage.Targets[i].Has && Stage.Targets[i].Snapshot).Select(i => IkGroupInfo.TargetNames[i]);

        // ------------------------------------------------------------ model → state

        static CharaIkState IkOf(CharaRealization r) => (r.Node?.State as CharaNodeState)?.Ik;

        internal static bool Flag(IkGroups g, IkGroup x)
        {
            if (g == null) return false;
            switch (x)
            {
                case IkGroup.Body: return g.Body;
                case IkGroup.RightLeg: return g.RightLeg;
                case IkGroup.LeftLeg: return g.LeftLeg;
                case IkGroup.RightArm: return g.RightArm;
                default: return g.LeftArm;
            }
        }

        /// <summary>Re-reads the whole block (build, `chara.ik` replaced).</summary>
        internal void ReadAll()
        {
            var ik = IkOf(Realization);
            if (ik == null) return;
            for (int i = 0; i < IkGroupInfo.TargetCount; i++) ReadTarget(i, ik);
            UpdateApplied(ik);
        }

        internal void UpdateApplied(CharaIkState ik)
        {
            Enabled = ik.Enabled;
            for (int g = 0; g < IkGroupInfo.Count; g++) Stage.Applied[g] = ik.Enabled && Flag(ik.Groups, (IkGroup)g);
            if (!Suspended) Stage.Refresh();
            else Stage.Enabled = Stage.AnyApplied;
        }

        /// <summary>Re-reads one target (index in IkTargets.Names order).</summary>
        internal void ReadTarget(int i, CharaIkState ik)
        {
            var t = ik.Targets?[i];
            if (t != null)
            {
                Stage.SetTarget((IkTargetId)i, new Vector3(t.Pos.X, t.Pos.Y, t.Pos.Z), IkGroupInfo.UsesRotation((IkTargetId)i) ? new Vector3(t.Rot.X, t.Rot.Y, t.Rot.Z) : Vector3.zero);
                return;
            }
            var cur = Stage.Targets[i];
            if (cur.Has && cur.Snapshot) return;   // keep the runtime snapshot
            if (FirstFrame != null && FirstFrame[i].Has)
            {
                var v = FirstFrame[i];
                v.Snapshot = true;
                Stage.Targets[i] = v;
                return;
            }
            Stage.ClearTarget((IkTargetId)i);
        }

        internal void OnChanged(string path)
        {
            var ik = IkOf(Realization);
            if (ik == null) return;
            const string tp = "chara.ik.targets.";
            if (path.StartsWith(tp, StringComparison.Ordinal))
            {
                string rest = path.Substring(tp.Length);
                int dot = rest.IndexOf('.');
                string name = dot >= 0 ? rest.Substring(0, dot) : rest;
                int i = Array.IndexOf(IkTargets.Names, name);
                if (i >= 0) { ReadTarget(i, ik); return; }
            }
            if (path == "chara.ik.targets")
            {
                for (int i = 0; i < IkGroupInfo.TargetCount; i++) ReadTarget(i, ik);
                return;
            }
            if (path == "chara.ik.enabled" || path.StartsWith("chara.ik.groups", StringComparison.Ordinal))
            {
                UpdateApplied(ik);
                return;
            }
            ReadAll();
        }

        // ------------------------------------------------------------ capture sources

        /// <summary>
        /// The 13 source-bone values of "the pose you see without IK" (clip + hand patterns + FK, before IK):
        /// the stage's input pose of the last step while IK runs; otherwise a probe of the stages before IkPrepare with the
        /// FK hips position / offset and the applied FK bones written inside it (restored by the probe). With
        /// IkOptions.MissingTargetsFirstFrame the first-frame values are used when present. Null when the character has
        /// no motion.
        /// </summary>
        public IkTargetValue[] PoseWithoutIk()
        {
            if (IkOptions.MissingTargetsFirstFrame && FirstFrame != null) return (IkTargetValue[])FirstFrame.Clone();
            var v = new IkTargetValue[IkGroupInfo.TargetCount];
            if (Running && Stage.HasLastInput)
            {
                Array.Copy(Stage.LastInput, v, v.Length);
                return v;
            }
            if (!motion || motion.Probing) return null;
            var fk = FkDriver.Of(Realization);
            motion.Probe(MotionPhase.IkPrepare, Root.transform, () =>
            {
                if (fk != null && !fk.Suspended)
                {
                    var h = fk.Hips;
                    if (h.Active && !h.Suspended && h.Hips)
                    {
                        var p = h.Position ?? h.Hips.localPosition;
                        h.Hips.localPosition = new Vector3(p.x, p.y + h.Offset, p.z);
                    }
                    if (fk.Stage.Enabled) fk.Stage.WriteApplied();
                }
                Bones.ReadTargets(v);
            });
            return v;
        }

        /// <summary>The 13 source-bone values of the clip + hand patterns (the probe before IkPrepare; "animation → IK"), or null.</summary>
        public IkTargetValue[] AnimationPose()
        {
            if (!motion || motion.Probing) return null;
            var v = new IkTargetValue[IkGroupInfo.TargetCount];
            motion.Probe(MotionPhase.IkPrepare, Root.transform, () => Bones.ReadTargets(v));
            return v;
        }

        /// <summary>The 13 source-bone values of the current final pose ("FK → IK").</summary>
        public IkTargetValue[] CurrentPose()
        {
            var v = new IkTargetValue[IkGroupInfo.TargetCount];
            AdvancedPoseDriver.Bare(Realization, () => Bones.ReadTargets(v));
            return v;
        }

        // ------------------------------------------------------------ suspend

        public IDisposable Suspend()
        {
            if (Suspended) return null;
            Suspended = true;
            Stage.SetSuspended(true);
            return new Resume(this);
        }

        sealed class Resume : IDisposable
        {
            IkCharacter c;
            public Resume(IkCharacter c) { this.c = c; }
            public void Dispose()
            {
                var x = c;
                c = null;
                if (x == null || !x.Root) return;
                x.Suspended = false;
                x.Stage.SetSuspended(false);
                x.Stage.Refresh();
            }
        }

        // ------------------------------------------------------------ first frame (vanilla parity)

        sealed class FirstFrameStage : IMotionStage
        {
            readonly IkCharacter c;
            public FirstFrameStage(IkCharacter c) { this.c = c; }
            public MotionPhase Phase => MotionPhase.Final;
            public string Name => "ikfirstframe";
            public bool Enabled { get; set; } = true;
            public void Step(in MotionFrame frame)
            {
                if (c.motion && c.motion.Probing) return;
                var v = new IkTargetValue[IkGroupInfo.TargetCount];
                AdvancedPoseDriver.Bare(c.Realization, () => c.Bones.ReadTargets(v));
                c.FirstFrame = v;
                Enabled = false;
                var ik = IkOf(c.Realization);
                if (ik == null) return;
                for (int i = 0; i < IkGroupInfo.TargetCount; i++)
                {
                    if (ik.Targets?[i] != null || !v[i].Has) continue;
                    var s = v[i];
                    s.Snapshot = true;
                    c.Stage.Targets[i] = s;   // HS2's first-frame copy replaces an earlier snapshot
                }
            }
            public void Reset() { }
        }

        public override string ToString() =>
            $"IK {(Enabled ? "on" : "off")} applied [{string.Join(",", Enumerable.Range(0, IkGroupInfo.Count).Where(g => Stage.IsApplied((IkGroup)g)).Select(g => ((IkGroup)g).ToString()))}] snapshots {SnapshotNames().Count()}{(Suspended ? " (suspended)" : "")}";
    }
}

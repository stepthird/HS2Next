// The IK driver behind CharaPoseDrivers.Ik (Phase 3.4 package B). Apply(Build) discovers
// the character's IkBones, installs the IkStage (MotionPhase.Ik; the FK driver applies after it and installs its stages
// then, which the stage finds lazily) and reads the stored `chara.ik` block; OnChanged updates what a changed path touches
// (enabled / groups → applied set, one target → its effective value, `chara.ik` → all); Suspend stops the writes for
// StudioDefaultPoseScope. Per-character state is an IkCharacter reachable from the character's CharaMotion. A rebuild
// (appearance change) creates a new root, so bones are rediscovered and snapshots dropped. Findings: ik.boneMissing
// (warning, once per node and build).
using System;
using System.Collections.Generic;
using HS2Next.Motion.Ik;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class IkDriver : ICharaPoseDriver
    {
        public static readonly IkDriver Instance = new IkDriver();

        /// <summary>The IK state of a built character, or null.</summary>
        public static IkCharacter Of(CharaRealization r)
        {
            if (r == null || r.State != RealizationState.Built || !r.Motion) return null;
            var c = r.Motion.Get<IkStage>()?.Owner as IkCharacter;
            return c != null && c.Realization == r ? c : null;
        }

        /// <summary>The IK state of the character built under <paramref name="charaRoot"/>, or null.</summary>
        public static IkCharacter Of(GameObject charaRoot)
        {
            var m = charaRoot ? charaRoot.GetComponent<HS2Next.Motion.CharaMotion>() : null;
            return m ? m.Get<IkStage>()?.Owner as IkCharacter : null;
        }

        public void Apply(CharaRealization r, CharaNodeState state, PoseApplyReason reason)
        {
            if (r == null || r.State != RealizationState.Built || r.Context == null || !r.Root) return;
            var c = Of(r) ?? Create(r);
            if (c == null) return;
            c.ReadAll();
        }

        public void OnChanged(CharaRealization r, string path)
        {
            var c = Of(r);
            if (c == null) { Apply(r, r?.Node?.State as CharaNodeState, PoseApplyReason.Changed); return; }
            c.OnChanged(path);
        }

        public IDisposable Suspend(GameObject charaRoot) => Of(charaRoot)?.Suspend();

        static IkCharacter Create(CharaRealization r)
        {
            if (!r.Motion || r.Context.BodyBones == null) return null;
            var bones = IkBones.Discover(r.Context.BodyBones, r.Root.transform);
            var c = new IkCharacter(r, bones);
            if (bones.Missing.Count > 0)
            {
                var off = new List<string>();
                for (int g = 0; g < IkGroupInfo.Count; g++) if (!bones.GroupAvailable[g]) off.Add(((IkGroup)g).ToString());
                r.Runtime?.ReportOnce(FindingSeverity.Warning, "ik.boneMissing", r.NodeId,
                    $"{bones.Missing.Count} IK bones missing on this character ({string.Join(", ", bones.Missing)}); groups disabled: {(off.Count > 0 ? string.Join(", ", off) : "none")}");
            }
            return c;
        }
    }
}

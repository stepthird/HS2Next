// A body's muscles and pins. Per tick each joint gets its spring, damper and target; per substep each part its pin and its gravity.
//   muscle   w from the value, the state and the part's weakness; the drive is written again only when w moved by more than .05 or a value changed. With w 0 it still carries the
//            friction, which is what stops a head that keeps nodding
//   target   the rotation the pose sources give the joint, or the crouch blend of a getting up, or the hold's rotation
//   pin      an acceleration towards the target's centre of mass with the target's speed; with gravity compensation the pin does not sag by g / w²
//   gravity  on a part that is awake or pinned: a sleeping body is left asleep
using HS2Next.Save;
using HS2Next.Scenes.Ragdoll;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed partial class RagdollBody
    {
        bool drivesDirty = true;

        /// <summary>The next tick writes every drive.</summary>
        internal void ForceDrives()
        {
            drivesDirty = true;
            for (int i = 0; i < parts.Count; i++) { parts[i].AppliedW = -1f; parts[i].TargetWritten = false; }
        }

        internal void BeforeSteps(float dt)
        {
            if (Buried) return;
            TickState(dt);
            TickSettle(dt);
            UpdateDrives();
        }

        /// <summary>The drives written now: a write in the first tick would wake a body that was put to sleep.</summary>
        internal void PrimeDrives() => UpdateDrives();

        void UpdateDrives()
        {
            var t = Values;
            var v = World.Values;
            bool force = drivesDirty;
            drivesDirty = false;
            bool crouch = State == RagdollState.GettingUp;
            float blend = crouch ? (float)RagdollMath.GetUpBlend(StateU) : 1f;
            for (int i = 1; i < parts.Count; i++)
            {
                var p = parts[i];
                float w = p.Held ? (float)RagdollMath.HoldMuscle(t.Muscle) : (float)RagdollMath.Muscle(t.Muscle, MuscleScale, v.WeakMuscle, p.Weakness);
                if (force || Mathf.Abs(w - p.AppliedW) > 0.05f)
                {
                    p.AppliedW = w;
                    p.Joint.slerpDrive = new JointDrive
                    {
                        positionSpring = (float)RagdollMath.Spring(w, p.SubInertia),
                        positionDamper = (float)RagdollMath.Damper(w, v.Friction, t.MuscleDamping, p.PivotInertia, p.SubInertia),
                        maximumForce = float.MaxValue,
                    };
                }
                Quaternion wanted;
                if (p.Held) wanted = p.HoldLocal;
                else
                {
                    if (w <= 0f || !p.HasTarget || !p.Parent.HasTarget) continue;
                    wanted = Quaternion.Inverse(p.Parent.TargetRot) * p.TargetRot;
                    if (crouch) wanted = Quaternion.Slerp(RagdollJoints.Crouch(p), wanted, blend);
                }
                var target = RagdollJoints.Target(p, wanted);
                if (p.TargetWritten && Same(target, p.AppliedTarget)) continue;
                p.AppliedTarget = target;
                p.TargetWritten = true;
                p.Joint.targetRotation = target;
            }
        }

        static bool Same(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;

        /// <summary>Before a substep: every part's pin and gravity, then what a getting up pulls.</summary>
        internal void Forces(Vector3 g)
        {
            if (Buried) return;
            var t = Values;
            float wp = t.PinFreq, pinned = t.Pin * PinScale;
            bool up = State == RagdollState.Up;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                var rb = p.Rb;
                bool on = pinned > 0f && p.HasTarget && !(t.PinHipsOnly && i != 0);
                if (on)
                {
                    var d = p.TargetCom - rb.worldCenterOfMass;
                    float k = (float)RagdollMath.PinK(t.Pin, PinScale, p.Weakness, d.magnitude, t.PinFalloff, up);
                    var a = k * (wp * wp * d + 2f * wp * (p.TargetVel - rb.linearVelocity));
                    if (t.GravityComp) a -= g * k;
                    rb.AddForce(a, ForceMode.Acceleration);
                }
                if (on || !rb.IsSleeping()) rb.AddForce(g, ForceMode.Acceleration);
            }
            if (Rising) RiseForces();
        }
    }
}

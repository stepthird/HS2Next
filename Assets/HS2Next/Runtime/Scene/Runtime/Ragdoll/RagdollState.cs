// The states of a body and what comes with them. Times are seconds of the world.
//   up         follows its pose; the pelvis too far from its target, a prop's hit, a stagger or a knock takes it down
//   down       no pin, weak muscles; when the pelvis stays slow long enough and the body has a pin it starts getting up
//   gettingUp  gather, rise over the feet, blend to the pose, turn to the pose's yaw, reseat. At the rise's start the character node moves so that the pose stands over the feet: that
//              move is the body's own consequence and is written silently, with no history entry
//   limp       no pin, no muscle; holds and ties stay
// Reseat puts a joint that ended far from its target back in its place, part by part; snap puts every part on its target.
// A body also keeps the pose it last came to rest in: that buffer, never a moving state, is what a save writes.
using System;
using HS2Next.Save;
using HS2Next.Scenes.Ragdoll;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>The state of a body.</summary>
    public enum RagdollState { Up, Down, GettingUp, Limp }

    public sealed partial class RagdollBody
    {
        const float SettleSpeed = 3f, SettleTime = 0.4f;

        /// <summary>The state now.</summary>
        public RagdollState State { get; private set; } = RagdollState.Up;
        /// <summary>Seconds of the world in this state.</summary>
        public float StateTime { get; private set; }
        /// <summary>Where a getting up stands, 1 is done.</summary>
        public double StateU { get; private set; }
        /// <summary>The scale of the pins and of the muscles in this state.</summary>
        public float PinScale { get; private set; } = 1f;
        public float MuscleScale { get; private set; } = 1f;
        /// <summary>True from the rise's start until the end of a getting up.</summary>
        public bool Rising { get; private set; }
        /// <summary>How many times the node moved under a getting up (tests).</summary>
        public int Relocations { get; private set; }

        /// <summary>What hits added up to, and how long the pelvis has been slow in state down.</summary>
        public float Stagger;
        float downSlow;

        // a release's blend: the seconds it lasts and how far it stands
        float blendOut, blendT;

        // the rise
        Vector3 feetMid, footAnchorL, footAnchorR;
        float standH, crouchH, chestOff, headOff;

        internal void SetState(RagdollState s)
        {
            State = s;
            StateTime = 0f;
            StateU = 0.0;
            Rising = false;
            downSlow = 0f;
            ScalesOf(s);
        }

        void ScalesOf(RagdollState s)
        {
            switch (s)
            {
                case RagdollState.Up: PinScale = 1f; MuscleScale = 1f; break;
                case RagdollState.Down: PinScale = 0f; MuscleScale = Values.DownMuscle; break;
                case RagdollState.GettingUp: PinScale = 0f; MuscleScale = Values.DownMuscle; break;
                default: PinScale = 0f; MuscleScale = 0f; break;
            }
        }

        /// <summary>The body goes down, from any state but limp (a knock, a hard hit).</summary>
        public void Knock()
        {
            if (State != RagdollState.Limp) SetState(RagdollState.Down);
            WakeAll();
        }

        /// <summary>The body stays limp until a release or a snap.</summary>
        public void GoLimp()
        {
            SetState(RagdollState.Limp);
            WakeAll();
        }

        public void WakeAll()
        {
            for (int i = 0; i < parts.Count; i++) parts[i].Rb.WakeUp();
        }

        void TickState(float dt)
        {
            StateTime += dt;
            var t = Values;
            var pelvis = parts[0];
            switch (State)
            {
                case RagdollState.Up:
                    if (t.KnockDistance > 0f && t.Pin > 0f && pelvis.HasTarget && (pelvis.Rb.worldCenterOfMass - pelvis.TargetCom).sqrMagnitude > t.KnockDistance * t.KnockDistance)
                        SetState(RagdollState.Down);
                    break;
                case RagdollState.Down:
                    MuscleScale = t.DownMuscle;
                    downSlow = pelvis.Rb.linearVelocity.magnitude < SettleSpeed ? downSlow + dt : 0f;
                    if (StateTime >= t.MinDown && downSlow >= SettleTime && t.Pin > 0f) SetState(RagdollState.GettingUp);
                    break;
                case RagdollState.GettingUp:
                    double u = RagdollMath.GetUpU(StateTime, t.GetUp);
                    StateU = u;
                    MuscleScale = (float)RagdollMath.GetUpMuscle(u, t.DownMuscle);
                    PinScale = (float)RagdollMath.GetUpPin(u);
                    if (!Rising && u >= RagdollMath.RiseFrom) StartRise();
                    if (u >= 1.0)
                    {
                        SetState(RagdollState.Up);
                        Reseat(45f);
                    }
                    break;
            }
            if (blendOut > 0f) blendT += dt;
        }

        // ------------------------------------------------------------ the rise

        void StartRise()
        {
            Rising = true;
            var left = Part("foot_L");
            var right = Part("foot_R");
            footAnchorL = left.Rb.worldCenterOfMass;
            footAnchorR = right.Rb.worldCenterOfMass;
            feetMid = (footAnchorL + footAnchorR) * 0.5f;
            var pelvis = parts[0];
            if (pelvis.HasTarget)
            {
                standH = pelvis.TargetCom.y - (left.TargetCom.y + right.TargetCom.y) * 0.5f;
                chestOff = Vector3.Distance(Part(RagdollParts.Chest).TargetCom, pelvis.TargetCom);
                headOff = Vector3.Distance(Part(RagdollParts.Head).TargetCom, pelvis.TargetCom);
            }
            else { standH = 8f; chestOff = 3f; headOff = 5f; }
            crouchH = (float)RagdollMath.CrouchHeight * standH;
            if (Values.Relocate) Relocate();
        }

        /// <summary>The node moves so that the pose stands over the feet and looks the way the body lies.</summary>
        void Relocate()
        {
            var r = Realization;
            if (r == null || !r.HasHolder) return;
            var pelvis = parts[0];
            var fwd = pelvis.Tr.forward;
            var bf = Flat(fwd);
            if (bf.sqrMagnitude < 0.25f)
            {
                var spine = Flat(pelvis.Tr.up);
                bf = fwd.y > 0f ? -spine : spine;
            }
            if (Values.FaceCamera)
            {
                var cam = World.Runtime.GetPart<SceneViewCamera>()?.Camera;
                if (cam)
                {
                    var to = Flat(cam.transform.position - feetMid);
                    if (to.magnitude > 1f) bf = to;
                }
            }
            var target = Flat(pelvis.TargetRot * Vector3.forward);
            if (bf.sqrMagnitude < 1e-8f || target.sqrMagnitude < 1e-8f) return;
            float yaw = Vector3.SignedAngle(target, bf, Vector3.up);
            var root = r.Root ? r.Root.transform.position : r.Holder.position;
            var turned = root + Quaternion.AngleAxis(yaw, Vector3.up) * (pelvis.TargetPos - root);
            var move = Flat(feetMid - turned);
            var doc = r.Node.Doc.Transform;
            var parent = r.Holder.parent;
            var local = parent ? parent.InverseTransformVector(move) : move;
            var pos = new Vec3(doc.Pos.X + local.x, doc.Pos.Y + local.y, doc.Pos.Z + local.z);
            var rot = new Vec3(doc.Rot.X, doc.Rot.Y + yaw, doc.Rot.Z);
            World.Runtime.Session.WriteSilently(ChangeOrigin.Capture, w =>
            {
                w.Set(NodeId, "transform.pos", pos);
                w.Set(NodeId, "transform.rot", rot);
            });
            World.Runtime.ApplyTransform(r);
            DropTargets();
            Relocations++;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        void RiseForces()
        {
            var t = Values;
            double u = StateU;
            float weight = (float)RagdollMath.RiseWeight(u);
            float height = (float)RagdollMath.RiseHeight(u, crouchH, standH);
            float wp = t.PinFreq;
            var pelvisGoal = new Vector3(feetMid.x, feetMid.y + height, feetMid.z);
            if (weight > 0f)
            {
                Pull(parts[0], pelvisGoal, weight, wp);
                Pull(Part(RagdollParts.Chest), pelvisGoal + Vector3.up * chestOff, weight, wp);
                Pull(Part(RagdollParts.Head), pelvisGoal + Vector3.up * headOff, weight * 0.5f, wp);
                Pull(Part("foot_L"), footAnchorL, weight, wp);
                Pull(Part("foot_R"), footAnchorR, weight, wp);
            }
            Yaw(parts[0], wp);
            Yaw(Part(RagdollParts.Chest), wp);
        }

        static void Pull(RagdollBodyPart p, Vector3 goal, float weight, float wp)
        {
            var rb = p.Rb;
            var d = goal - rb.worldCenterOfMass;
            rb.AddForce(weight * (wp * wp * d - 2f * wp * rb.linearVelocity), ForceMode.Acceleration);
        }

        static void Yaw(RagdollBodyPart p, float wp)
        {
            if (!p.HasTarget) return;
            var f = Flat(p.Tr.forward);
            var want = Flat(p.TargetRot * Vector3.forward);
            if (f.sqrMagnitude < 0.04f || want.sqrMagnitude < 0.04f) return;
            float a = Vector3.SignedAngle(f, want, Vector3.up) * Mathf.Deg2Rad;
            float acc = wp * wp * a - 2f * wp * p.Rb.angularVelocity.y;
            p.Rb.AddTorque(Vector3.up * acc, ForceMode.Acceleration);
        }

        // ------------------------------------------------------------ reseat, snap

        static double Angle(Quaternion a, Quaternion b) => RagdollMath.AngleDeg(a.x, a.y, a.z, a.w, b.x, b.y, b.z, b.w);

        /// <summary>A joint further than <paramref name="thresholdDeg"/> from its target, or whose parent was moved, goes to its target in its parent, at the parent's point of the joint.</summary>
        public void Reseat(float thresholdDeg)
        {
            var moved = new bool[parts.Count];
            var pelvis = parts[0];
            if (pelvis.HasTarget && Angle(pelvis.Tr.rotation, pelvis.TargetRot) > thresholdDeg)
            {
                var at = pelvis.Tr.position;
                pelvis.Tr.rotation = pelvis.TargetRot;
                pelvis.Rb.rotation = pelvis.TargetRot;
                pelvis.Rb.position = at;
                pelvis.Rb.angularVelocity = Vector3.zero;
                moved[0] = true;
            }
            for (int i = 1; i < parts.Count; i++)
            {
                var p = parts[i];
                var q = p.Parent;
                if (!p.HasTarget || !q.HasTarget) continue;
                var want = Quaternion.Inverse(q.TargetRot) * p.TargetRot;
                var now = Quaternion.Inverse(q.Tr.rotation) * p.Tr.rotation;
                if (!moved[q.Index] && Angle(now, want) <= thresholdDeg) continue;
                var rot = q.Tr.rotation * want;
                var at = q.Tr.position + q.Tr.rotation * p.JointAnchor;
                p.Tr.SetPositionAndRotation(at, rot);
                p.Rb.position = at;
                p.Rb.rotation = rot;
                p.Rb.linearVelocity = q.Rb.linearVelocity;
                p.Rb.angularVelocity = Vector3.zero;
                moved[i] = true;
            }
        }

        /// <summary>Every part at its target, no speed, state up.</summary>
        public void Snap()
        {
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (p.HasTarget) Put(p, p.TargetPos, p.TargetRot);
                p.Weakness = 0f;
            }
            Stagger = 0f;
            SetState(RagdollState.Up);
            ChainsAfterNextWrite();
        }

        // ------------------------------------------------------------ the pose at rest

        float slowFor;
        Quaternion[] settled;
        Vector3 settledHips;
        bool settledNew;

        /// <summary>The last pose at rest, in the holder's space: rotations of the parts in order; null before the first rest.</summary>
        public Quaternion[] SettledParts => settled;
        public Vector3 SettledHips => settledHips;
        /// <summary>True when the buffer changed since it was last written to the block.</summary>
        public bool SettledChanged => settledNew;
        /// <summary>The block holds what the buffer holds.</summary>
        internal void SettledWritten() => settledNew = false;

        /// <summary>The buffer as the block's pose: a pose that came from the file counts as written.</summary>
        internal void SettledFrom(RagdollPose pose)
        {
            settled = new Quaternion[parts.Count];
            for (int i = 0; i < parts.Count; i++)
                settled[i] = pose.Parts.TryGetValue(parts[i].Key, out var q) ? UnityObjects.Q(q) : Quaternion.identity;
            settledHips = UnityObjects.V(pose.Hips);
            settledNew = false;
        }

        /// <summary>The buffer as a block's pose.</summary>
        internal RagdollPose SettledPose()
        {
            var pose = new RagdollPose { Hips = UnityObjects.V(settledHips) };
            for (int i = 0; i < parts.Count; i++) pose.Parts[parts[i].Key] = UnityObjects.Q(settled[i]);
            return pose;
        }

        internal void AfterSteps(float dt)
        {
            if (Buried) return;
            // a body that sleeps did not move: what it holds is what it came to rest in
            bool awake = false, slow = true;
            for (int i = 0; i < parts.Count; i++)
            {
                var rb = parts[i].Rb;
                if (rb.IsSleeping()) continue;
                awake = true;
                if (rb.linearVelocity.sqrMagnitude >= SettleSpeed * SettleSpeed) { slow = false; break; }
            }
            if (!awake) return;
            slowFor = slow ? slowFor + dt : 0f;
            if (slowFor >= SettleTime) KeepSettled();
        }

        void KeepSettled()
        {
            var h = Realization?.Holder;
            if (!h) return;
            var inv = Quaternion.Inverse(h.rotation);
            settled ??= new Quaternion[parts.Count];
            bool changed = false;
            for (int i = 0; i < parts.Count; i++)
            {
                var q = inv * parts[i].Tr.rotation;
                if (!Same(q, settled[i])) { settled[i] = q; changed = true; }
            }
            var hips = h.InverseTransformPoint(parts[0].Tr.position);
            if (hips != settledHips) { settledHips = hips; changed = true; }
            if (changed) settledNew = true;
        }

        /// <summary>A command is about to replace the block: what stands now goes into the buffer when the body is at rest.</summary>
        internal void CaptureSettled()
        {
            if (slowFor >= SettleTime) KeepSettled();
        }

        // ------------------------------------------------------------ a release's blend

        /// <summary>True while a release blends to the pose.</summary>
        public bool Blending => blendOut > 0f;
        /// <summary>True when a blend is over.</summary>
        internal bool BlendDone => blendOut > 0f && blendT >= blendOut;

        /// <summary>Pins, ties and holds go, muscle and pins off, the proxy keeps falling while the bones go to the pose.</summary>
        internal void StartBlend(float seconds)
        {
            RemoveLinks();
            SetState(RagdollState.Limp);
            blendOut = seconds;
            blendT = 0f;
        }
    }
}

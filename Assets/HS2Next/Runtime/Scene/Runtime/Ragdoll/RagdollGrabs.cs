// What is done to a body from outside: a push, a grab, a pin, a tie, a turn, a settle, a hold, and the pushers.
//   grab     a kinematic anchor at the point and a joint with three linear drives: spring = stiffness · m, damper = 2 sqrt(k m) · .6, force = max · m · gravity, m the body's mass.
//            The anchor moves to the goal in the substeps, so a pause holds it
//   pin      a grab that stays. One that follows a node or a bone has a stiffer spring and takes its goal once per tick, from what the last frame left
//   tie      a fixed joint where the two parts are now
//   turn     once, on pelvis, abdomen and chest: a torque impulse towards a direction, in proportion to how far the part's front is from it
//   settle   the same per tick for a time, with a damper on the speed
//   hold     a joint keeps its rotation as it is now, with a strong muscle, whatever the state. A hold is never the joint's lock: a locked joint goes back to the rest pose
//   pusher   a kinematic capsule along two bones of a character, with or without a body: it moves bodies, bodies do not move it
// Everything here that a file keeps is made from the block (RagdollPart.Sync), and made again after a rebuild.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Scenes.Ragdoll;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>A rigid body held at a point by a spring to a kinematic anchor.</summary>
    public sealed class RagdollGrab
    {
        readonly RagdollWorld world;
        readonly GameObject anchorObject;
        readonly ConfigurableJoint joint;
        readonly float mass;
        Vector3 moved;

        internal RagdollGrab(RagdollWorld world, RagdollBodyPart part, Rigidbody target, Vector3 point, float mass, float stiffness, float maxW)
        {
            this.world = world;
            this.mass = mass;
            Part = part;
            Target = target;
            Goal = moved = point;
            LocalPoint = target.transform.InverseTransformPoint(point);
            anchorObject = world.NewRoot("Anchor");
            anchorObject.transform.position = point;
            if (part != null) anchorObject.transform.SetParent(part.Body.Container.transform, true);
            Anchor = anchorObject.AddComponent<Rigidbody>();
            Anchor.isKinematic = true;
            Anchor.useGravity = false;
            Anchor.position = point;
            joint = target.gameObject.AddComponent<ConfigurableJoint>();
            joint.autoConfigureConnectedAnchor = false;
            joint.connectedBody = Anchor;
            joint.anchor = LocalPoint;
            joint.connectedAnchor = Vector3.zero;
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Free;
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;
            joint.enableCollision = false;
            joint.enablePreprocessing = false;
            SetSpring(stiffness, maxW);
            target.WakeUp();
        }

        /// <summary>The part held; null for a prop.</summary>
        public RagdollBodyPart Part { get; }
        public Rigidbody Target { get; }
        public Rigidbody Anchor { get; }
        /// <summary>The held point in the target's space.</summary>
        public Vector3 LocalPoint { get; }
        /// <summary>Where the anchor goes in the next substep.</summary>
        public Vector3 Goal;
        /// <summary>True until <see cref="Drop"/>.</summary>
        public bool Live => joint;
        /// <summary>The held point now.</summary>
        public Vector3 Point => Target.transform.TransformPoint(LocalPoint);

        /// <summary>The spring by the mass this grab counts.</summary>
        public void SetSpring(float stiffness, float maxW)
        {
            RagdollMath.GrabDrive(stiffness, maxW, mass, world.Values.Gravity, out double k, out double d, out double f);
            var drive = new JointDrive { positionSpring = (float)k, positionDamper = (float)d, maximumForce = (float)f };
            joint.xDrive = drive;
            joint.yDrive = drive;
            joint.zDrive = drive;
        }

        internal void Move()
        {
            if (Goal == moved) return;
            moved = Goal;
            Anchor.MovePosition(Goal);
            Target.WakeUp();
        }

        /// <summary>Lets go.</summary>
        public void Drop()
        {
            world.Forget(this);
            if (joint) Object.DestroyImmediate(joint);
            if (anchorObject) Object.DestroyImmediate(anchorObject);
        }
    }

    /// <summary>How a pin stands, as the status says it.</summary>
    public static class RagdollPinStates
    {
        public const string Ok = "ok", Missing = "missing";
    }

    /// <summary>A pin of the block at work.</summary>
    public sealed class RagdollLivePin
    {
        Transform follow;

        internal RagdollLivePin(RagdollPin doc) { Doc = doc; }

        /// <summary>A copy of the block's row.</summary>
        public RagdollPin Doc { get; }
        public RagdollGrab Grab { get; internal set; }
        public string Id => Doc.Id;
        /// <summary>One of <see cref="RagdollPinStates"/>: a follow to a node that is not there leaves the pin where it is.</summary>
        public string State { get; private set; } = RagdollPinStates.Ok;

        internal void Make(RagdollBody body)
        {
            var p = body.Part(Doc.Part);
            if (p == null) return;
            var w = body.World;
            var v = w.Values;
            var point = p.Tr.TransformPoint(UnityObjects.V(Doc.Point));
            bool following = Doc.IsFollowing;
            Grab = w.Grab(p.Rb, point, following ? v.FollowStiffness : v.GrabStiffness, following ? v.FollowMax : v.GrabMax);
            if (Grab == null) return;
            if (Doc.At.HasValue) Grab.Goal = UnityObjects.V(Doc.At.Value);
            Read(body);
        }

        /// <summary>The spring again after a value changed.</summary>
        internal void Respring(RagdollWorldValues v)
        {
            if (Grab == null) return;
            if (Doc.IsFollowing) Grab.SetSpring(v.FollowStiffness, v.FollowMax);
            else Grab.SetSpring(v.GrabStiffness, v.GrabMax);
        }

        /// <summary>The goal from what it follows, as the last frame left it.</summary>
        internal void Read(RagdollBody body)
        {
            if (Grab == null || !Doc.IsFollowing) return;
            var rt = body.World.Runtime;
            // a node in the graveyard is gone to the user, though its holder lives on: the pin stays where it is, and an undo brings it back to following
            var node = rt.Find(Doc.Follow);
            if (node == null || node.InGraveyard) { follow = null; State = RagdollPinStates.Missing; return; }
            if (!follow) follow = Resolve(rt);
            if (!follow) { State = RagdollPinStates.Missing; return; }
            State = RagdollPinStates.Ok;
            Grab.Goal = Doc.Offset.HasValue ? follow.TransformPoint(UnityObjects.V(Doc.Offset.Value)) : follow.position;
        }

        Transform Resolve(SceneRuntime rt)
        {
            if (string.IsNullOrEmpty(Doc.Bone)) return rt.HolderOf(Doc.Follow);
            var r = rt.FindChara(Doc.Follow);
            return r != null && r.State == RealizationState.Built ? AdvancedPoseResolver.Resolve(r, Doc.Bone) : null;
        }
    }

    /// <summary>A tie of the block at work.</summary>
    sealed class RagdollLiveTie
    {
        public string A, B;
        public FixedJoint Joint;
    }

    /// <summary>A pusher of the block at work.</summary>
    public sealed class RagdollLivePusher
    {
        readonly RagdollWorld world;
        readonly GameObject go;
        readonly Rigidbody rb;
        readonly CapsuleCollider col;
        Transform a, b;
        Vector3 fromPos, toPos;
        Quaternion fromRot, toRot;

        internal RagdollLivePusher(RagdollWorld world, RagdollPusher doc)
        {
            this.world = world;
            Doc = doc;
            go = world.NewRoot("Pusher " + doc.Id);
            rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            col = go.AddComponent<CapsuleCollider>();
            col.direction = 2;
            col.radius = doc.AppliedRadius;
            col.sharedMaterial = world.Material;
            if (Resolve()) Seat();
        }

        /// <summary>A copy of the block's row.</summary>
        public RagdollPusher Doc { get; }
        public Rigidbody Rb => rb;
        public CapsuleCollider Collider => col;
        /// <summary>False when a bone is gone: the pusher drops out.</summary>
        public bool Alive => a && b;

        bool Resolve()
        {
            var r = world.Runtime.FindChara(Doc.Key);
            if (r == null || r.State != RealizationState.Built) return false;
            a = AdvancedPoseResolver.Resolve(r, Doc.A);
            b = AdvancedPoseResolver.Resolve(r, Doc.B);
            return a && b;
        }

        void Goal(out Vector3 at, out Quaternion rot, out float length)
        {
            var pa = a.position;
            var pb = b.position;
            var d = pb - pa;
            length = d.magnitude;
            at = (pa + pb) * 0.5f;
            rot = length > 1e-5f ? Quaternion.LookRotation(d / length) : Quaternion.identity;
        }

        void Seat()
        {
            Goal(out var at, out var rot, out float length);
            col.height = length + 2f * col.radius;
            go.transform.SetPositionAndRotation(at, rot);
            rb.position = at;
            rb.rotation = rot;
            fromPos = toPos = at;
            fromRot = toRot = rot;
        }

        /// <summary>The start of a tick: where the bones stand, as the last frame left them. A bone that is gone is looked for again, on a rebuilt character.</summary>
        internal void Read(RagdollWorld w)
        {
            if ((!a || !b) && !Resolve()) { w.Remove(this); return; }
            fromPos = toPos;
            fromRot = toRot;
            Goal(out toPos, out toRot, out float length);
            float h = length + 2f * col.radius;
            if (Mathf.Abs(h - col.height) > 1e-3f) col.height = h;
        }

        /// <summary>Before a substep: the part <paramref name="t"/> of the way through the frame's move, so the capsule moves at the leg's speed.</summary>
        internal void Move(float t)
        {
            rb.MovePosition(Vector3.Lerp(fromPos, toPos, t));
            rb.MoveRotation(Quaternion.Slerp(fromRot, toRot, t));
        }

        internal void Destroy()
        {
            if (go) Object.DestroyImmediate(go);
        }
    }

    public sealed partial class RagdollBody
    {
        readonly List<RagdollLivePin> pins = new List<RagdollLivePin>();
        readonly List<RagdollLiveTie> ties = new List<RagdollLiveTie>();

        Vector3 settleDir;
        float settleLeft, settleGain, settleDamp;

        /// <summary>The pins at work.</summary>
        public IReadOnlyList<RagdollLivePin> Pins => pins;
        /// <summary>The ties at work.</summary>
        public int TieCount => ties.Count;
        /// <summary>Seconds a settle still runs.</summary>
        public float SettleLeft => settleLeft;

        /// <summary>The pin of an id, or null.</summary>
        public RagdollLivePin PinOf(string id)
        {
            for (int i = 0; i < pins.Count; i++) if (pins[i].Id == id) return pins[i];
            return null;
        }

        // ------------------------------------------------------------ pushes

        /// <summary>The push of the tool: <paramref name="direction"/> at a point, by the push speed and the body's mass.</summary>
        public void PushAt(RagdollBodyPart p, Vector3 point, Vector3 direction)
        {
            WakeAll();
            p.Rb.AddForceAtPosition(direction * (World.Values.PushSpeed * Mass), point, ForceMode.Impulse);
        }

        /// <summary>An impulse at a part's centre.</summary>
        public void Push(RagdollBodyPart p, Vector3 impulse)
        {
            WakeAll();
            p.Rb.AddForce(impulse, ForceMode.Impulse);
        }

        // ------------------------------------------------------------ turn, settle

        static readonly string[] Trunk = { RagdollParts.Pelvis, RagdollParts.Abdomen, RagdollParts.Chest };

        /// <summary>Once, a torque impulse on the trunk towards <paramref name="dir"/>.</summary>
        public void Turn(Vector3 dir, float strength)
        {
            dir = dir.normalized;
            WakeAll();
            foreach (var key in Trunk)
            {
                var p = Part(key);
                var f = p.BoneRotation * Vector3.forward;
                var axis = Vector3.Cross(f, dir);
                float misalignment = Mathf.Clamp01((1f - Vector3.Dot(f, dir)) / 2f);
                p.Rb.AddTorque(axis.normalized * (strength * misalignment * p.Rb.inertiaTensor.magnitude), ForceMode.Impulse);
            }
        }

        /// <summary>For <paramref name="seconds"/> the trunk turns towards <paramref name="dir"/>, as a spring with a damper.</summary>
        public void Settle(Vector3 dir, float seconds, float gain, float damp)
        {
            settleDir = dir.normalized;
            settleLeft = seconds;
            settleGain = gain;
            settleDamp = damp;
            WakeAll();
        }

        void TickSettle(float dt)
        {
            if (settleLeft <= 0f || dt <= 0f) return;
            settleLeft -= dt;
            foreach (var key in Trunk)
            {
                var p = Part(key);
                var f = p.BoneRotation * Vector3.forward;
                var axis = Vector3.Cross(f, settleDir);
                float angle = Mathf.Atan2(axis.magnitude, Vector3.Dot(f, settleDir));
                var want = axis.normalized * (angle * settleGain);
                p.Rb.AddTorque((want - p.Rb.angularVelocity * settleDamp) * (dt * p.Rb.inertiaTensor.magnitude), ForceMode.Impulse);
            }
        }

        // ------------------------------------------------------------ what the block keeps

        /// <summary>The block's pins, ties and holds at work; what stayed the same stays untouched.</summary>
        internal void SyncLinks(CharaRagdoll block)
        {
            // holds
            for (int i = 0; i < parts.Count; i++) { parts[i].Held = false; }
            foreach (var h in block.AppliedHolds())
            {
                var p = Part(h.Part);
                if (p?.Parent == null) continue;
                p.Held = true;
                p.HoldLocal = UnityObjects.Q(h.Rot);
            }
            ForceDrives();
            // pins, by id
            for (int i = pins.Count - 1; i >= 0; i--)
            {
                var doc = block.Find(pins[i].Id);
                if (doc != null && SamePin(doc, pins[i].Doc)) continue;
                pins[i].Grab?.Drop();
                pins.RemoveAt(i);
            }
            foreach (var d in block.AppliedPins())
            {
                if (PinOf(d.Id) != null) continue;
                var pin = new RagdollLivePin(Copy(d));
                pin.Make(this);
                pins.Add(pin);
            }
            // ties, by their two parts
            for (int i = ties.Count - 1; i >= 0; i--)
            {
                bool keep = false;
                foreach (var t in block.AppliedTies()) keep |= t.A == ties[i].A && t.B == ties[i].B;
                if (keep) continue;
                if (ties[i].Joint) Object.DestroyImmediate(ties[i].Joint);
                ties.RemoveAt(i);
            }
            foreach (var t in block.AppliedTies())
            {
                bool have = false;
                for (int i = 0; i < ties.Count; i++) have |= ties[i].A == t.A && ties[i].B == t.B;
                if (!have) ties.Add(MakeTie(t.A, t.B));
            }
        }

        static RagdollPin Copy(RagdollPin d) => new RagdollPin { Id = d.Id, Part = d.Part, Point = d.Point, At = d.At, Follow = d.Follow, Bone = d.Bone, Offset = d.Offset };

        static bool SamePin(RagdollPin a, RagdollPin b) =>
            a.Part == b.Part && a.Point.Equals(b.Point) && a.At.Equals(b.At) && a.Follow == b.Follow && a.Bone == b.Bone && a.Offset.Equals(b.Offset);

        RagdollLiveTie MakeTie(string a, string b)
        {
            var t = new RagdollLiveTie { A = a, B = b };
            var pa = Part(a);
            var pb = Part(b);
            if (pa == null || pb == null) return t;
            t.Joint = pa.Tr.gameObject.AddComponent<FixedJoint>();
            t.Joint.connectedBody = pb.Rb;
            t.Joint.enableCollision = false;
            return t;
        }

        /// <summary>A rebuild or a burial: the joints go, what they are made from stays.</summary>
        void DropLinks()
        {
            for (int i = 0; i < pins.Count; i++)
            {
                pins[i].Grab?.Drop();
                pins[i].Grab = null;
            }
            for (int i = 0; i < ties.Count; i++)
            {
                if (ties[i].Joint) Object.DestroyImmediate(ties[i].Joint);
                ties[i].Joint = null;
            }
            // a grab of the tool on this body goes as well
            var all = World.Grabs;
            for (int i = all.Count - 1; i >= 0; i--)
                if (all[i].Part != null && all[i].Part.Body == this) all[i].Drop();
        }

        /// <summary>After a rebuild: the joints again, at the pose it has.</summary>
        void MakeLinks()
        {
            for (int i = 0; i < pins.Count; i++) pins[i].Make(this);
            for (int i = 0; i < ties.Count; i++)
            {
                var t = MakeTie(ties[i].A, ties[i].B);
                ties[i].Joint = t.Joint;
            }
        }

        /// <summary>A release's blend: no pin, tie or hold any more.</summary>
        void RemoveLinks()
        {
            DropLinks();
            pins.Clear();
            ties.Clear();
            for (int i = 0; i < parts.Count; i++) parts[i].Held = false;
            ForceDrives();
        }

        internal void FollowPins()
        {
            if (Buried) return;
            for (int i = 0; i < pins.Count; i++) pins[i].Read(this);
        }

        /// <summary>The springs again after a value of the world changed.</summary>
        internal void Respring()
        {
            for (int i = 0; i < pins.Count; i++) pins[i].Respring(World.Values);
        }
    }
}

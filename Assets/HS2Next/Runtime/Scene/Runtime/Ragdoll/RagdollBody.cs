// A character as a physics body: 16 rigid parts and 15 joints in the world of the scene, and a second set of driven bones through which the body writes the character. It is built
// from the rest pose (RagdollRest): sizes, joint frames and inertias come from there, then the parts go to the live pose or to a kept one.
//   the parts      a part's object has no scale, its local y runs along its segment, its local z is the reference made square to it; offset turns the part into its bone
//   who collides   a parent and its child never; a pair that overlaps in the rest pose or in the live pose at the build never; the left leg and the right leg only with legCollision
//   the stages     ragdoll.restore, ragdoll, ragdoll.end come through the character stage host, so a rebuild finds them again
//   the writes     parents first: the bone's local rotation from the part's rotation and the offset; for the pelvis also the local position of its origin. The rule that keeps a
//                  bone from a correction keeps it from a body: such a part drives nothing and is reported once per build
// The drives (RagdollDrives.cs), the states (RagdollState.cs) and what is done to a body (RagdollGrabs.cs) are the other files of this class.
using System;
using System.Collections.Generic;
using HS2Next.Chara;
using HS2Next.Motion;
using HS2Next.Motion.AdvancedPose;
using HS2Next.Motion.DynamicBones;
using HS2Next.Save;
using HS2Next.Scenes.Ragdoll;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>One rigid part of a body.</summary>
    public sealed class RagdollBodyPart
    {
        internal RagdollBodyPart(RagdollBody body, RagdollPartInfo row, int index)
        {
            Body = body;
            Row = row;
            Index = index;
        }

        public RagdollBody Body { get; }
        public RagdollPartInfo Row { get; }
        /// <summary>Its place in the body's parts, parents first.</summary>
        public int Index { get; }
        public string Key => Row.Key;
        public RagdollBodyPart Parent { get; internal set; }
        /// <summary>The bone it writes.</summary>
        public Transform Bone { get; internal set; }
        public Transform Tr { get; internal set; }
        public Rigidbody Rb { get; internal set; }
        public Collider Col { get; internal set; }
        public ConfigurableJoint Joint { get; internal set; }
        /// <summary>The set's entry of the bone; null when the part drives nothing.</summary>
        public DrivenBone Entry { get; internal set; }

        /// <summary>Turns the part's rotation into its bone's.</summary>
        public Quaternion Offset { get; internal set; }
        internal Quaternion OffsetInverse;
        /// <summary>The centre of mass in the part's space.</summary>
        public Vector3 LocalCom { get; internal set; }

        /// <summary>The child's rotation in its parent in the rest pose, and the flexion axis in the parent's frame.</summary>
        public Quaternion StartLocal { get; internal set; }
        public Vector3 FlexAxis { get; internal set; }
        /// <summary>|I| + m d² about the joint: this part alone, and the part with all that hangs on it.</summary>
        public float PivotInertia { get; internal set; }
        public float SubInertia { get; internal set; }
        /// <summary>Where the joint sits in the parent's space.</summary>
        public Vector3 JointAnchor { get; internal set; }
        internal Quaternion JointFrame, JointFrameInverse;
        internal float CrouchDeg;

        /// <summary>True once a target was read.</summary>
        public bool HasTarget { get; internal set; }
        /// <summary>Where the pose sources put the bone, and the part's rotation and centre of mass for that pose, with the speed of that centre.</summary>
        public Vector3 TargetPos { get; internal set; }
        public Quaternion TargetBoneRot { get; internal set; }
        public Quaternion TargetRot { get; internal set; }
        public Vector3 TargetCom { get; internal set; }
        public Vector3 TargetVel { get; internal set; }

        /// <summary>0..1: what a hit took from this part's pin and muscle.</summary>
        public float Weakness;
        /// <summary>The joint keeps this rotation in its parent, whatever the state.</summary>
        public bool Held { get; internal set; }
        public Quaternion HoldLocal { get; internal set; }

        internal float AppliedW = -1f;
        internal Quaternion AppliedTarget;
        internal bool TargetWritten;

        /// <summary>The bone's rotation as the body sets it.</summary>
        public Quaternion BoneRotation => Tr.rotation * Offset;
        /// <summary>The joint's rotation in its parent now.</summary>
        public Quaternion LocalRotation => Parent != null ? Quaternion.Inverse(Parent.Tr.rotation) * Tr.rotation : Tr.rotation;
    }

    /// <summary>The rotations of the parts and the place of the pelvis in world space: what a rebuild keeps.</summary>
    public sealed class RagdollWorldPose
    {
        public readonly Quaternion[] Parts = new Quaternion[RagdollParts.All.Count];
        public Vector3 Hips;
    }

    public sealed partial class RagdollBody : IDrivenEvaluator, IRagdollStages
    {
        // the first of each pair in the part order, left then right
        const int UpperArm = 4, Forearm = 6, Hand = 8, Thigh = 10, Shin = 12, Foot = 14;

        readonly List<RagdollBodyPart> parts = new List<RagdollBodyPart>(RagdollParts.All.Count);
        IDisposable stages;
        CharaMotion builtOn;
        readonly HashSet<int> overlaps = new HashSet<int>();
        readonly Action<HS2Next.Motion.Animation.CharaAnimation> beforeBind;
        HS2Next.Motion.Animation.CharaAnimation boundAnimation;
        bool targetsLive, chainsPending;

        internal RagdollBody(RagdollWorld world, CharaRealization r, RagdollBodyValues values)
        {
            World = world;
            Realization = r;
            NodeId = r.NodeId;
            Values = values;
            beforeBind = _ => { AdvancedPoseDriver.RestoreNow(Realization); DrivenPoseDriver.RestoreNow(Realization); RestoreBones(); };
        }

        public RagdollWorld World { get; }
        public CharaRealization Realization { get; private set; }
        public string NodeId { get; }
        /// <summary>The values as they act (clamped).</summary>
        public RagdollBodyValues Values { get; internal set; }
        public IReadOnlyList<RagdollBodyPart> Parts => parts;
        /// <summary>The root object of the proxies.</summary>
        public GameObject Container { get; private set; }
        /// <summary>The set of driven bones the body writes through; a rebuild makes a new one.</summary>
        public DrivenBones Set { get; private set; }
        /// <summary>The sum of the parts' masses.</summary>
        public float Mass { get; private set; }
        /// <summary>Thigh length / 4.2 in the rest pose.</summary>
        public float Scale { get; private set; }
        /// <summary>The thigh length and the shoulder width the sizes come from: a flush that changes them rebuilds the body.</summary>
        public float BuiltThigh { get; private set; }
        public float BuiltShoulder { get; private set; }
        /// <summary>Pairs of parts that do not collide because they overlap: in the rest pose, in the live pose.</summary>
        public int IgnoredRest { get; private set; }
        public int IgnoredLive { get; private set; }
        /// <summary>False when the skeleton's prefab could not be read and the rest is the pose of the moment.</summary>
        public bool RestFromPrefab { get; private set; }
        /// <summary>The keys of the parts that drive nothing.</summary>
        public readonly List<string> Protected = new List<string>();
        /// <summary>Milliseconds of the last build.</summary>
        public double BuildMs { get; private set; }
        /// <summary>The stages are off and the set writes nothing (a thumbnail).</summary>
        public bool Suspended { get; internal set; }
        /// <summary>The ankle's height over the toes against its height over the floor a character stands on. A test's door may set another.</summary>
        public static float AnkleRatio = 0.8833f;
        /// <summary>A test's door: the end of the body leaves the bones as they are.</summary>
        public bool LeaveBones;
        /// <summary>A test's door: a jump leaves the chains their history.</summary>
        public bool LeaveChains;
        /// <summary>The character lies in the graveyard: the proxies are off.</summary>
        public bool Buried { get; private set; }

        /// <summary>The part of a key, or null.</summary>
        public RagdollBodyPart Part(string key)
        {
            int i = RagdollParts.IndexOf(key);
            return i >= 0 && i < parts.Count ? parts[i] : null;
        }

        // ------------------------------------------------------------ build

        /// <summary>The first bone a body needs that this character lacks; null when it has them all.</summary>
        internal static string MissingBone(CharaContext ctx)
        {
            var bones = ctx?.BodyBones;
            if (bones == null) return "cf_J_Hips";
            foreach (var row in RagdollParts.All) if (!Has(bones, row.Bone)) return row.Bone;
            foreach (var n in RagdollMath.OtherBones) if (!Has(bones, n)) return n;
            foreach (var side in new[] { "L", "R" })
                if (ToesOf(bones, side) == null) return RagdollMath.Toes[0] + side;
            return null;
        }

        static bool Has(Dictionary<string, Transform> bones, string n) => bones.TryGetValue(n, out var t) && t;

        static Transform ToesOf(Dictionary<string, Transform> bones, string side)
        {
            foreach (var n in RagdollMath.Toes) if (bones.TryGetValue(n + side, out var t) && t) return t;
            return null;
        }

        /// <summary>Makes the proxies and the set on the character as it stands, the corrections and the driven values off. <paramref name="pose"/> null: at the pose of the moment.</summary>
        internal void Build(RagdollWorldPose pose)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            AdvancedPoseDriver.Bare(Realization, () => BuildBare(pose));
            BuildMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }

        void BuildBare(RagdollWorldPose pose)
        {
            var r = Realization;
            var ctx = r.Context;
            var bones = ctx.BodyBones;
            Transform B(string n) => bones[n];
            var hips = B("cf_J_Hips");
            var rest = RagdollRest.Solve(ctx, hips);
            RestFromPrefab = rest.FromPrefab;
            Vector3 P(string n) => rest.Position(bones[n]);

            var toesL = ToesOf(bones, "L");
            var toesR = ToesOf(bones, "R");
            var frame = new RagdollFrame();
            frame.Up = (P("cf_J_Neck") - P("cf_J_Hips")).normalized;
            var toeMid = (rest.Position(toesL) + rest.Position(toesR)) * 0.5f;
            var ankleMid = (P("cf_J_Foot01_L") + P("cf_J_Foot01_R")) * 0.5f;
            frame.Front = RagdollJoints.Ortho(toeMid - ankleMid, frame.Up);
            frame.Right = Vector3.Cross(frame.Up, frame.Front);
            float s = Vector3.Distance(P("cf_J_LegUp00_L"), P("cf_J_LegLow01_L")) / 4.2f;
            Scale = s;
            BuiltThigh = 4.2f * s;
            BuiltShoulder = Vector3.Distance(P("cf_J_ArmUp00_L"), P("cf_J_ArmUp00_R"));

            Container = World.NewRoot("Ragdoll " + NodeId);
            parts.Clear();
            for (int i = 0; i < RagdollParts.All.Count; i++)
            {
                var row = RagdollParts.All[i];
                var p = new RagdollBodyPart(this, row, i) { Bone = bones[row.Bone] };
                if (row.Parent != null) p.Parent = parts[RagdollParts.IndexOf(row.Parent)];
                parts.Add(p);
            }

            // the objects, in the rest pose
            var U = frame.Up;
            var F = frame.Front;
            Dir(0, U, F, rest);
            Dir(1, P("cf_J_Spine02") - P("cf_J_Spine01"), F, rest);
            Dir(2, P("cf_J_Neck") - P("cf_J_Spine02"), F, rest);
            Dir(3, P("cf_J_Head") - P("cf_J_Neck"), F, rest);
            for (int k = 0; k < 2; k++)
            {
                string sd = k == 0 ? "L" : "R";
                Vector3 a0 = P("cf_J_ArmUp00_" + sd), a1 = P("cf_J_ArmLow01_" + sd), a2 = P("cf_J_Hand_" + sd);
                Vector3 l0 = P("cf_J_LegUp00_" + sd), l1 = P("cf_J_LegLow01_" + sd), l2 = P("cf_J_Foot01_" + sd);
                var toes = rest.Position(k == 0 ? toesL : toesR);
                Dir(UpperArm + k, a1 - a0, F, rest);
                Dir(Forearm + k, a2 - a1, F, rest);
                Dir(Hand + k, a2 - a1, F, rest);
                Dir(Thigh + k, l1 - l0, F, rest);
                Dir(Shin + k, l2 - l1, F, rest);
                Dir(Foot + k, RagdollJoints.Ortho(toes - l2, U), U, rest);

                Capsule(parts[UpperArm + k], Vector3.Distance(a0, a1), 0.42f * s);
                Capsule(parts[Forearm + k], Vector3.Distance(a1, a2), 0.33f * s);
                Box(parts[Hand + k], new Vector3(0.32f * s, 1.0f * s, 0.85f * s), new Vector3(0f, 0.5f * s, 0f));
                Capsule(parts[Thigh + k], Vector3.Distance(l0, l1), 0.68f * s);
                Capsule(parts[Shin + k], Vector3.Distance(l1, l2), 0.45f * s);
                // the foot from the ankle to the toes, never from the ankle's height over the root or the hips
                var toeVec = toes - l2;
                var footDir = RagdollJoints.Ortho(toeVec, U);
                float toeLen = Vector3.Dot(toeVec, footDir);
                float ankleH = Mathf.Max(Vector3.Dot(l2 - toes, U) * 0.96f / AnkleRatio, 0.5f * s);
                Box(parts[Foot + k], new Vector3(0.8f * s, toeLen + 0.7f * s, ankleH + 0.2f * s), new Vector3(0f, toeLen / 2f, (-ankleH + 0.2f * s) / 2f));
            }
            var hipL = P("cf_J_LegUp00_L");
            var hipR = P("cf_J_LegUp00_R");
            float bottom = Vector3.Dot((hipL + hipR) * 0.5f - P("cf_J_Hips"), U) - 0.9f * s, top = 0.5f * s;
            Box(parts[0], new Vector3(Vector3.Distance(hipL, hipR) + 1.4f * s, top - bottom, 2.3f * s), new Vector3(0f, (top + bottom) * 0.5f, -0.1f * s));
            float abdomen = Vector3.Distance(P("cf_J_Spine01"), P("cf_J_Spine02"));
            Box(parts[1], new Vector3(2.4f * s, abdomen, 1.9f * s), new Vector3(0f, abdomen * 0.5f, 0f));
            float chest = Vector3.Distance(P("cf_J_Spine02"), P("cf_J_Neck"));
            Box(parts[2], new Vector3(Mathf.Max(0.95f * BuiltShoulder, 2.6f * s), chest, 2.2f * s), new Vector3(0f, chest * 0.5f, 0.1f * s));
            var sphere = parts[3].Tr.gameObject.AddComponent<SphereCollider>();
            sphere.radius = 1.0f * s;
            sphere.center = parts[3].Tr.InverseTransformPoint(P("cf_J_Head") + U * (0.8f * s));
            sphere.sharedMaterial = World.Material;
            parts[3].Col = sphere;

            // the rigid bodies
            Mass = 0f;
            var w = World.Values;
            foreach (var p in parts)
            {
                var rb = p.Rb;
                rb.mass = (float)RagdollMath.PartMass(p.Row.Share, Values.Mass);
                Mass += rb.mass;
                rb.useGravity = false;
                rb.linearDamping = 0.05f;
                rb.angularDamping = w.AngularDrag;
                rb.maxAngularVelocity = 50f;
                rb.maxDepenetrationVelocity = 50f;
                rb.sleepThreshold = 0.1f;
                rb.solverIterations = 20;
                rb.solverVelocityIterations = 8;
                rb.interpolation = RigidbodyInterpolation.None;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                p.LocalCom = rb.centerOfMass;
            }

            // the joints and what the muscles are made of
            for (int k = 1; k < parts.Count; k++)
            {
                var p = parts[k];
                int side = k & 1;   // the sides alternate from the upper arms on, left first
                var arm = parts[Forearm + side].Tr.position - parts[UpperArm + side].Tr.position;
                var leg = parts[Shin + side].Tr.position - parts[Thigh + side].Tr.position;
                RagdollJoints.Make(p, RagdollMath.JointOf(p.Key), frame, arm.normalized, leg.normalized);
            }
            for (int k = 1; k < parts.Count; k++)
            {
                var p = parts[k];
                float sum = 0f;
                for (int m = k; m < parts.Count; m++)
                {
                    var q = parts[m];
                    if (!Under(q, p)) continue;
                    var d = q.Rb.worldCenterOfMass - p.Tr.position;
                    sum += q.Rb.inertiaTensor.magnitude + q.Rb.mass * d.sqrMagnitude;
                }
                p.SubInertia = sum;
            }
            overlaps.Clear();
            IgnoredRest = IgnoreOverlaps();

            // to the live pose, or to the one kept
            if (pose != null) Place(pose);
            else
                foreach (var p in parts)
                {
                    p.Bone.GetPositionAndRotation(out var at, out var rot);
                    Put(p, at, rot * p.OffsetInverse);
                }
            IgnoredLive = IgnoreOverlaps();
            ApplyLegCollision();

            // the set, as the driven bones make their entries
            builtOn = r.Motion;
            Set = new DrivenBones(r.Motion) { Evaluator = this };
            Protected.Clear();
            foreach (var p in parts)
            {
                p.Entry = DrivenPoseDriver.BindInto(r, Set, p.Row.Bone, p.Bone);
                if (p.Entry == null) Protected.Add(p.Key);
            }
            ReadTargets(new MotionFrame(0f, 0f, 0f, 0));
            RagdollDriver.Bind(this);
            if (r.Animation != boundAnimation)
            {
                if (boundAnimation) boundAnimation.BeforeBind -= beforeBind;
                boundAnimation = r.Animation;
                if (boundAnimation) boundAnimation.BeforeBind += beforeBind;
            }
        }

        static bool Under(RagdollBodyPart q, RagdollBodyPart p)
        {
            for (var a = q; a != null; a = a.Parent) if (a == p) return true;
            return false;
        }

        // the object of a part at its bone in the rest pose: local y along the segment, local z the reference made square to it
        void Dir(int index, Vector3 segment, Vector3 reference, RagdollRest rest)
        {
            var p = parts[index];
            var y = segment.normalized;
            var z = reference - Vector3.Dot(reference, y) * y;
            if (z.sqrMagnitude < 1e-6f) z = Vector3.Cross(y, Vector3.right);
            var go = new GameObject(p.Key);
            go.transform.SetParent(Container.transform, false);
            go.transform.SetPositionAndRotation(rest.Position(p.Bone), Quaternion.LookRotation(z.normalized, y));
            p.Tr = go.transform;
            p.Offset = Quaternion.Inverse(go.transform.rotation) * rest.Rotation(p.Bone);
            p.OffsetInverse = Quaternion.Inverse(p.Offset);
            p.Rb = go.AddComponent<Rigidbody>();
        }

        void Capsule(RagdollBodyPart p, float length, float radius)
        {
            var c = p.Tr.gameObject.AddComponent<CapsuleCollider>();
            c.direction = 1;
            c.radius = radius;
            c.height = Mathf.Max(length, 2f * radius);
            c.center = new Vector3(0f, length * 0.5f, 0f);
            c.sharedMaterial = World.Material;
            p.Col = c;
        }

        void Box(RagdollBodyPart p, Vector3 size, Vector3 center)
        {
            var c = p.Tr.gameObject.AddComponent<BoxCollider>();
            c.size = size;
            c.center = center;
            c.sharedMaterial = World.Material;
            p.Col = c;
        }

        // pairs that overlap as the parts stand now and collide no more; a parent and its child are the joint's business
        int IgnoreOverlaps()
        {
            int n = 0;
            for (int i = 0; i < parts.Count; i++)
                for (int k = i + 1; k < parts.Count; k++)
                {
                    var a = parts[i];
                    var b = parts[k];
                    if (a.Parent == b || b.Parent == a) continue;
                    if (!Physics.ComputePenetration(a.Col, a.Tr.position, a.Tr.rotation, b.Col, b.Tr.position, b.Tr.rotation, out _, out _)) continue;
                    if (!overlaps.Add(i * 64 + k)) continue;
                    Physics.IgnoreCollision(a.Col, b.Col, true);
                    n++;
                }
            return n;
        }

        /// <summary>The left and the right leg collide or not, as the value says. A pair that overlapped at the build stays ignored.</summary>
        internal void ApplyLegCollision()
        {
            for (int a = Thigh; a <= Foot; a += 2)
                for (int b = Thigh + 1; b <= Foot + 1; b += 2)
                    Physics.IgnoreCollision(parts[a].Col, parts[b].Col, !Values.LegCollision || overlaps.Contains(Math.Min(a, b) * 64 + Math.Max(a, b)));
        }

        static void Put(RagdollBodyPart p, Vector3 at, Quaternion rot)
        {
            p.Tr.SetPositionAndRotation(at, rot);
            p.Rb.position = at;
            p.Rb.rotation = rot;
            p.Rb.linearVelocity = Vector3.zero;
            p.Rb.angularVelocity = Vector3.zero;
        }

        /// <summary>Every part to the pose: part by part, a part's place comes from its parent's joint point, so a pose survives other proportions.</summary>
        void Place(RagdollWorldPose pose)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                var rot = pose.Parts[i];
                // a part the pose lacks stays with the character
                if (rot.x == 0f && rot.y == 0f && rot.z == 0f && rot.w == 0f) rot = p.Bone.rotation * p.OffsetInverse;
                var at = p.Parent == null ? pose.Hips : p.Parent.Tr.position + p.Parent.Tr.rotation * p.JointAnchor;
                Put(p, at, rot);
            }
        }

        /// <summary>The lowest point of the colliders, from their shapes: it needs no physics step.</summary>
        public float Lowest()
        {
            float y = float.PositiveInfinity;
            for (int i = 0; i < parts.Count; i++) y = Mathf.Min(y, LowestOf(parts[i]));
            return y;
        }

        /// <summary>The lowest point of one part's collider.</summary>
        public static float LowestOf(RagdollBodyPart p)
        {
            var t = p.Tr;
            switch (p.Col)
            {
                case BoxCollider b:
                {
                    float y = float.PositiveInfinity;
                    var e = b.size * 0.5f;
                    for (int k = 0; k < 8; k++)
                    {
                        var c = b.center + new Vector3((k & 1) != 0 ? e.x : -e.x, (k & 2) != 0 ? e.y : -e.y, (k & 4) != 0 ? e.z : -e.z);
                        y = Mathf.Min(y, t.TransformPoint(c).y);
                    }
                    return y;
                }
                case CapsuleCollider c:
                {
                    float half = Mathf.Max(0f, c.height * 0.5f - c.radius);
                    var a = t.TransformPoint(c.center + Vector3.up * half);
                    var d = t.TransformPoint(c.center - Vector3.up * half);
                    return Mathf.Min(a.y, d.y) - c.radius;
                }
                case SphereCollider s:
                    return t.TransformPoint(s.center).y - s.radius;
                default:
                    return t.position.y;
            }
        }

        /// <summary>Every part goes to sleep: a loaded pose stands still until something acts on it. What the objects were given reaches the physics first: at the next step it would wake them.</summary>
        internal void SleepAll()
        {
            Physics.SyncTransforms();
            for (int i = 0; i < parts.Count; i++) parts[i].Rb.Sleep();
        }

        /// <summary>True when the bones no longer have the sizes this body was made from.</summary>
        internal bool SizesStale()
        {
            var ctx = Realization?.Context;
            if (ctx?.BodyBones == null) return false;
            var bones = ctx.BodyBones;
            var rest = RagdollRest.Solve(ctx, bones["cf_J_Hips"]);
            float thigh = Vector3.Distance(rest.Position(bones["cf_J_LegUp00_L"]), rest.Position(bones["cf_J_LegLow01_L"]));
            float shoulder = Vector3.Distance(rest.Position(bones["cf_J_ArmUp00_L"]), rest.Position(bones["cf_J_ArmUp00_R"]));
            return Mathf.Abs(thigh - BuiltThigh) > 1e-4f || Mathf.Abs(shoulder - BuiltShoulder) > 1e-4f;
        }

        /// <summary>The rotations of the parts and the pelvis' place now.</summary>
        public RagdollWorldPose CurrentPose()
        {
            var pose = new RagdollWorldPose { Hips = parts[0].Tr.position };
            for (int i = 0; i < parts.Count; i++) pose.Parts[i] = parts[i].Tr.rotation;
            return pose;
        }

        /// <summary>The proxies go and come again on the character as it stands now, at <paramref name="pose"/>: pins, ties and holds are made again; the state stays.</summary>
        internal void Rebuild(CharaRealization r, RagdollWorldPose pose)
        {
            Realization = r;
            DropLinks();
            RagdollDriver.Unbind(this);
            World.Forget(this);
            if (Container) Object.DestroyImmediate(Container);
            Container = null;
            Buried = false;
            Build(pose);
            World.Remember(this);
            MakeLinks();
            ForceDrives();
        }

        // ------------------------------------------------------------ stages

        /// <summary>The owner of a body's stages in the stage host.</summary>
        public const string StageOwner = "ragdoll";

        /// <summary>The three stages through the host. It calls the factory at once and again after every rebuild of the character, with the new bones: the body follows there.</summary>
        internal void AddStages()
        {
            stages ??= CharaStageHost.For(World.Runtime).Add(NodeId, StageOwner, MakeStages);
        }

        IReadOnlyList<IMotionStage> MakeStages(CharaRealization r)
        {
            if (Set == null || r.Motion != builtOn) Rebuild(r, CurrentPose());
            return new IMotionStage[] { new RagdollRestoreStage(this), new RagdollApplyStage(this), new RagdollEndStage(this) };
        }

        internal void RemoveStages()
        {
            stages?.Dispose();
            stages = null;
        }

        /// <summary>The stages of the character on or off (a thumbnail, a test's door).</summary>
        internal void SetStagesEnabled(bool on)
        {
            var m = Realization?.Motion;
            if (!m) return;
            foreach (var s in m.Stages)
                if (s is RagdollRestoreStage || s is RagdollApplyStage || s is RagdollEndStage) s.Enabled = on;
        }

        // ------------------------------------------------------------ the frame

        /// <summary>Stage 410, before the writes: the pose sources as they stand, for every part.</summary>
        public void ReadTargets(in MotionFrame frame)
        {
            if (Buried) return;
            float dt = frame.UnscaledDt;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (!p.Bone) return;
                p.Bone.GetPositionAndRotation(out var at, out var rot);
                var target = rot * p.OffsetInverse;
                var com = at + target * p.LocalCom;
                p.TargetVel = targetsLive && dt > 0f ? Vector3.ClampMagnitude((com - p.TargetCom) / dt, 300f) : Vector3.zero;
                p.TargetPos = at;
                p.TargetBoneRot = rot;
                p.TargetRot = target;
                p.TargetCom = com;
                p.HasTarget = true;
            }
            targetsLive = true;
        }

        /// <summary>
        /// The set's writes come off. The rotation setter may store other last bits than it is given, and the neck look finds its own write by the bits: where the restore gave it back
        /// what it left, it is told what the bone holds, as the set tells IK. Otherwise it would take its own output for a new input, on a pose nothing else writes again.
        /// </summary>
        public void RestoreBones()
        {
            var set = Set;
            if (set == null || !set.Applied) return;
            set.Restore();
            var bones = Realization?.Look?.Neck?.Bones;
            if (bones == null) return;
            for (int i = 0; i < bones.Length; i++)
            {
                var b = bones[i];
                if (!b.written || !b.NeckBone) continue;
                var e = set.Of(b.NeckBone);
                if (e != null && Same(e.BaseRot, b.lastWritten)) b.lastWritten = b.NeckBone.localRotation;
            }
        }

        /// <summary>
        /// A jump of the bones is no motion: a chain would take it as a whip and could lie elsewhere for good. Its history goes, particles at the bones; the look and the blink keep theirs.
        /// </summary>
        void ResetChains()
        {
            var m = Realization?.Motion;
            if (!m || LeaveChains) return;
            m.Get<DynamicBoneStage>()?.Reset();
            foreach (var bust in m.GetAll<BustStage>()) bust.Reset();
        }

        /// <summary>The bones will jump at the next write: the chains start again from there.</summary>
        public void ChainsAfterNextWrite() => chainsPending = true;

        /// <summary>After the stage's write: a jump the write made is in the bones now, before the chains' stages run.</summary>
        public void AfterWrite()
        {
            if (!chainsPending) return;
            chainsPending = false;
            ResetChains();
        }

        /// <summary>The next read has no last place to take a speed from.</summary>
        public void DropTargets() => targetsLive = false;

        /// <summary>The bones from the parts, parents first.</summary>
        public void EvaluateBones(DrivenBones set, in MotionFrame frame)
        {
            float b = blendOut > 0f ? (float)RagdollMath.Smooth(blendT / blendOut) : 0f;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                var e = p.Entry;
                if (e == null) continue;
                var parent = p.Bone.parent;
                p.Tr.GetPositionAndRotation(out var at, out var rot);
                var bone = rot * p.Offset;
                if (b > 0f)
                {
                    bone = Quaternion.Slerp(bone, p.TargetBoneRot, b);
                    at = Vector3.Lerp(at, p.TargetPos, b);
                }
                e.SetRot(Quaternion.Inverse(parent.rotation) * bone);
                if (i == 0)
                {
                    e.SetPos(parent.InverseTransformPoint(at));
                    set.Write(e, DrivenChannels.Position | DrivenChannels.Rotation);
                }
                else set.Write(e, DrivenChannels.Rotation);
            }
        }

        /// <summary>
        /// Runs <paramref name="read"/> with the bones as the body writes them and nothing on top of them, and takes the writes off again: what a bake reads. What lay on top comes
        /// back in the next step.
        /// </summary>
        public void ReadBodyPose(Action read)
        {
            var r = Realization;
            AdvancedPoseDriver.RestoreNow(r);
            DrivenPoseDriver.RestoreNow(r);
            RestoreBones();
            Set.Apply(new MotionFrame(0f, 0f, 0f, 0));
            try { read(); }
            finally { RestoreBones(); }
        }

        // ------------------------------------------------------------ the graveyard

        /// <summary>The character went to the graveyard: the proxies are off, the state is kept.</summary>
        internal void Bury()
        {
            if (Buried) return;
            keptPose = CurrentPose();
            Buried = true;
            DropLinks();
            if (Container) Container.SetActive(false);
        }

        RagdollWorldPose keptPose;

        /// <summary>The character is back: the body as it lay. A joint switched off would take the pose of the moment as its zero, so the proxies are made anew.</summary>
        internal void Revive() => Rebuild(Realization, keptPose);

        // ------------------------------------------------------------ the end

        /// <summary>Stages off, the bones back to the pose sources, the proxies gone.</summary>
        internal void Destroy()
        {
            RemoveStages();
            if (Realization != null && Realization.State == RealizationState.Built)
            {
                // the reverse of the writes: what lies on top comes off first, and comes on again by itself in the next step
                AdvancedPoseDriver.RestoreNow(Realization);
                DrivenPoseDriver.RestoreNow(Realization);
            }
            if (!LeaveBones) RestoreBones();
            AdvancedPoseDriver.ApplyIfRestored(Realization);
            // a blend went to the pose over time: that was motion, the chains stay physical
            if (blendOut <= 0f && Realization != null && Realization.State == RealizationState.Built) ResetChains();
            if (boundAnimation) boundAnimation.BeforeBind -= beforeBind;
            boundAnimation = null;
            DropLinks();
            RagdollDriver.Unbind(this);
            if (Container) Object.DestroyImmediate(Container);
            Container = null;
            parts.Clear();
            Set = null;
        }
    }
}

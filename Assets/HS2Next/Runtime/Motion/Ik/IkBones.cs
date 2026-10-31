// IK bone discovery for one character. Discovered once per build from the body
// skeleton dictionary (CharaContext.BodyBones) by exact name. A missing bone disables the groups that need it
// (pelvis or spine → Body; a limb or its clavicle → that limb); the scene layer reports `ik.boneMissing` from Missing.
// Per limb the rest bend direction (the mid joint's offset from the line root → end) is stored in the frame of the
// root bone's parent at discovery; it is the last pole fallback of the solver.
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Motion.Ik
{
    /// <summary>One two-bone limb: root (upper bone), mid (lower bone), end (hand / foot), the clavicle for arms.</summary>
    public sealed class IkLimb
    {
        public IkGroup Group;
        public Transform Root, Mid, End;
        /// <summary>`cf_J_Shoulder_*` for arms, null for legs.</summary>
        public Transform Clavicle;
        /// <summary>Rest bend direction (unit) in the frame of Root.parent at discovery; zero when degenerate.</summary>
        public Vector3 RestBend;
        /// <summary>Discovery-time data of the bend-plane orientation, in the upper bone's own frame: its
        /// axis (toward the mid joint) and its rest bend direction (perpendicular to that axis: the hinge side).</summary>
        public Vector3 UpAxis, UpBend;
        /// <summary>The bend-plane data are usable.</summary>
        public bool HasBendFrame;
        /// <summary>All bones present.</summary>
        public bool Available;
    }

    public sealed class IkBones
    {
        /// <summary>Bone slots of the solved set (fixed order: latch arrays, owned set).</summary>
        public const int SlotCount = 17;
        public const int SlotHips = 0, SlotSpine01 = 1, SlotSpine02 = 2;

        public Transform Root { get; private set; }
        public Transform Hips { get; private set; }
        public Transform Spine01 { get; private set; }
        public Transform Spine02 { get; private set; }
        /// <summary>Read only (the torso leaves it alone, K5).</summary>
        public Transform Spine03 { get; private set; }
        /// <summary>Indexed by IkGroup (index 0 = Body is null).</summary>
        public readonly IkLimb[] Limbs = new IkLimb[IkGroupInfo.Count];
        /// <summary>Per group: every bone the group needs is present.</summary>
        public readonly bool[] GroupAvailable = new bool[IkGroupInfo.Count];
        /// <summary>The source bone of each target (for captures / snapshots); null when missing.</summary>
        public readonly Transform[] Sources = new Transform[IkGroupInfo.TargetCount];
        /// <summary>The solved bones by slot (null when missing): hips, Spine01, Spine02, then per limb in group order
        /// RightLeg, LeftLeg, RightArm, LeftArm: (legs) root, mid, end; (arms) clavicle, root, mid, end.</summary>
        public readonly Transform[] Slots = new Transform[SlotCount];
        /// <summary>Names of the missing bones (empty when all present).</summary>
        public readonly List<string> Missing = new List<string>();

        static readonly string[] SideOfGroup = { null, "R", "L", "R", "L" };

        static readonly int[][] slotsOf =
        {
            new[] { 0, 1, 2 }, new[] { 3, 4, 5 }, new[] { 6, 7, 8 }, new[] { 9, 10, 11, 12 }, new[] { 13, 14, 15, 16 },
        };

        /// <summary>The slots of a group's bones (Body: hips, Spine01, Spine02; legs: root, mid, end; arms: clavicle, root,
        /// mid, end). Shared array: do not modify.</summary>
        public static int[] SlotsOf(IkGroup g) => slotsOf[(int)g];

        public IkLimb Limb(IkGroup g) => Limbs[(int)g];

        /// <summary>Discovers the solved bones of one character. <paramref name="root"/> is the character root (root space).</summary>
        public static IkBones Discover(IReadOnlyDictionary<string, Transform> bodyBones, Transform root)
        {
            var b = new IkBones { Root = root };
            Transform Get(string name)
            {
                Transform t = null;
                if (bodyBones != null) bodyBones.TryGetValue(name, out t);
                if (!t) { b.Missing.Add(name); return null; }
                return t;
            }
            b.Hips = Get("cf_J_Hips");
            b.Spine01 = Get("cf_J_Spine01");
            b.Spine02 = Get("cf_J_Spine02");
            b.Spine03 = Get("cf_J_Spine03");
            b.GroupAvailable[0] = b.Hips && b.Spine01 && b.Spine02 && b.Spine03;
            b.Slots[SlotHips] = b.Hips; b.Slots[SlotSpine01] = b.Spine01; b.Slots[SlotSpine02] = b.Spine02;
            for (int g = 1; g < IkGroupInfo.Count; g++)
            {
                var grp = (IkGroup)g;
                string s = SideOfGroup[g];
                var l = new IkLimb { Group = grp };
                if (IkGroupInfo.IsArm(grp))
                {
                    l.Clavicle = Get("cf_J_Shoulder_" + s);
                    l.Root = Get("cf_J_ArmUp00_" + s);
                    l.Mid = Get("cf_J_ArmLow01_" + s);
                    l.End = Get("cf_J_Hand_" + s);
                    l.Available = l.Clavicle && l.Root && l.Mid && l.End;
                }
                else
                {
                    l.Root = Get("cf_J_LegUp00_" + s);
                    l.Mid = Get("cf_J_LegLow01_" + s);
                    l.End = Get("cf_J_Foot01_" + s);
                    l.Available = l.Root && l.Mid && l.End;
                }
                l.RestBend = RestBendOf(l, root, IkGroupInfo.IsArm(grp));
                BendFrameOf(l);
                b.Limbs[g] = l;
                b.GroupAvailable[g] = l.Available;
                var slots = SlotsOf(grp);
                int k = 0;
                if (IkGroupInfo.IsArm(grp)) b.Slots[slots[k++]] = l.Clavicle;
                b.Slots[slots[k++]] = l.Root;
                b.Slots[slots[k++]] = l.Mid;
                b.Slots[slots[k]] = l.End;
            }
            for (int i = 0; i < IkGroupInfo.TargetCount; i++)
                if (bodyBones != null && bodyBones.TryGetValue(IkGroupInfo.SourceBones[i], out var t) && t) b.Sources[i] = t;
            return b;
        }

        /// <summary>The bend-plane data of a limb from the discovery pose (rest bend direction as RestBend).</summary>
        static void BendFrameOf(IkLimb l)
        {
            if (!l.Available || !l.Root.parent) return;
            Vector3 a = l.Root.position, m = l.Mid.position, e = l.End.position;
            Vector3 bend = l.Root.parent.rotation * l.RestBend;             // world, perpendicular to root → end
            if (bend.sqrMagnitude < 1e-12f || (m - a).sqrMagnitude < 1e-12f) return;
            var iu = Quaternion.Inverse(l.Root.rotation);
            l.UpAxis = (iu * (m - a)).normalized;
            var ub = iu * bend;
            ub -= Vector3.Dot(ub, l.UpAxis) * l.UpAxis;
            if (ub.sqrMagnitude < 1e-12f) return;
            l.UpBend = ub.normalized;
            l.HasBendFrame = true;
        }

        /// <summary>The rest bend direction of a limb in its root's parent frame. Degenerate (straight limb):
        /// the character's forward for legs (knees forward, +Z) and backward for arms (elbows back, −Z), in that frame.</summary>
        static Vector3 RestBendOf(IkLimb l, Transform root, bool arm)
        {
            if (!l.Available || !l.Root.parent) return Vector3.zero;
            Vector3 a = l.Root.position, m = l.Mid.position, e = l.End.position;
            Vector3 u = e - a;
            Vector3 v = m - a;
            float len = u.magnitude;
            if (len > 1e-6f)
            {
                u /= len;
                v -= Vector3.Dot(v, u) * u;
            }
            float total = (m - a).magnitude + (e - m).magnitude;
            if (len <= 1e-6f || v.magnitude < 1e-5f * Mathf.Max(total, 1e-6f))
            {
                Vector3 fwd = root ? root.forward : Vector3.forward;
                v = arm ? -fwd : fwd;
                if (len > 1e-6f) v -= Vector3.Dot(v, u) * u;
                if (v.magnitude < 1e-6f) return Vector3.zero;
            }
            return Quaternion.Inverse(l.Root.parent.rotation) * v.normalized;
        }

        /// <summary>
        /// Reads the 13 target values from the current pose (the capture sources): root-space position of
        /// each source bone (Body = `cf_J_Hips`), root-space Euler for hands and feet, 0 otherwise. A missing source bone
        /// leaves its entry with Has = false. <paramref name="into"/> must hold 13 entries.
        /// </summary>
        public void ReadTargets(IkTargetValue[] into) => ReadTargets(Root, Sources, into);

        /// <summary>As <see cref="ReadTargets(IkTargetValue[])"/> for any root and source list.</summary>
        public static void ReadTargets(Transform root, Transform[] sources, IkTargetValue[] into)
        {
            Quaternion inv = root ? Quaternion.Inverse(root.rotation) : Quaternion.identity;
            for (int i = 0; i < IkGroupInfo.TargetCount; i++)
            {
                var s = sources[i];
                if (!s) { into[i] = default; continue; }
                Vector3 pos = root ? root.InverseTransformPoint(s.position) : s.position;
                Vector3 rot = IkGroupInfo.UsesRotation((IkTargetId)i) ? (inv * s.rotation).eulerAngles : Vector3.zero;
                into[i] = new IkTargetValue(pos, rot);
            }
        }
    }
}

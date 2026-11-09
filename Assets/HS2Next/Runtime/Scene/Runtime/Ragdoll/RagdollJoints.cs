// The joints of a body, made in the rest pose. A joint's angle runs against the turn of the child about the joint's axis, so a turn of the child towards flexion is a negative
// angle: the axis's sign is chosen by turning the child's direction 10 degrees about the candidate and seeing whether it moves towards the flexion direction, the low X limit is
// then minus the largest flexion, the high one the largest extension. A joint keeps what a drive needs: the rotation of the child in the parent in the rest pose, the axis in the
// parent's frame and the joint's frame.
using HS2Next.Scenes.Ragdoll;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>The character's axes in the rest pose, and the directions a joint's row names.</summary>
    internal struct RagdollFrame
    {
        public Vector3 Right, Front, Up;

        public Vector3 Of(RagdollAxis a, Vector3 arm, Vector3 leg, Vector3 foot)
        {
            switch (a)
            {
                case RagdollAxis.Right: return Right;
                case RagdollAxis.Front: return Front;
                case RagdollAxis.Up: return Up;
                case RagdollAxis.Back: return -Front;
                case RagdollAxis.Down: return -Up;
                case RagdollAxis.Arm: return arm;
                case RagdollAxis.Leg: return leg;
                default: return foot;
            }
        }
    }

    internal static class RagdollJoints
    {
        /// <summary><paramref name="v"/> without its part along the unit vector <paramref name="n"/>, normalized.</summary>
        internal static Vector3 Ortho(Vector3 v, Vector3 n) => (v - Vector3.Dot(v, n) * n).normalized;

        /// <summary>The candidate as it is when turning <paramref name="child"/> about it moves it towards <paramref name="flexion"/>, else negated.</summary>
        internal static Vector3 FlexionAxis(Vector3 primary, Vector3 child, Vector3 flexion)
        {
            var moved = Quaternion.AngleAxis(10f, primary) * child - child;
            return Vector3.Dot(moved, flexion) >= 0f ? primary : -primary;
        }

        /// <summary>The joint of <paramref name="p"/> to its parent, with the two at their rest pose.</summary>
        internal static void Make(RagdollBodyPart p, RagdollJointRow row, in RagdollFrame f, Vector3 arm, Vector3 leg)
        {
            var parent = p.Parent;
            var foot = p.Tr.up;
            var child = f.Of(row.Child, arm, leg, foot);
            var flexion = f.Of(row.Flexion, arm, leg, foot);
            var axis = FlexionAxis(f.Of(row.Primary, arm, leg, foot), child, flexion);
            var second = Ortho(f.Of(row.Secondary, arm, leg, foot), axis);

            var j = p.Tr.gameObject.AddComponent<ConfigurableJoint>();
            j.autoConfigureConnectedAnchor = false;
            j.connectedBody = parent.Rb;
            j.anchor = Vector3.zero;
            j.connectedAnchor = parent.Tr.InverseTransformPoint(p.Tr.position);
            j.axis = p.Tr.InverseTransformDirection(axis);
            j.secondaryAxis = p.Tr.InverseTransformDirection(second);
            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
            j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Limited;
            var l = new SoftJointLimit();
            l.limit = (float)-row.MaxFlex; j.lowAngularXLimit = l;
            l.limit = (float)row.MaxExt; j.highAngularXLimit = l;
            l.limit = (float)row.Y; j.angularYLimit = l;
            l.limit = (float)row.Z; j.angularZLimit = l;
            j.projectionMode = JointProjectionMode.PositionAndRotation;
            j.projectionDistance = 0.05f;
            j.projectionAngle = 5f;
            j.enablePreprocessing = false;
            j.enableCollision = false;
            j.rotationDriveMode = RotationDriveMode.Slerp;

            p.Joint = j;
            p.JointAnchor = j.connectedAnchor;
            p.StartLocal = Quaternion.Inverse(parent.Tr.rotation) * p.Tr.rotation;
            p.FlexAxis = Quaternion.Inverse(parent.Tr.rotation) * axis;
            p.CrouchDeg = (float)row.Crouch;
            var x = j.axis.normalized;
            var z = Vector3.Cross(x, j.secondaryAxis).normalized;
            var y = Vector3.Cross(z, x).normalized;
            p.JointFrame = Quaternion.LookRotation(z, y);
            p.JointFrameInverse = Quaternion.Inverse(p.JointFrame);
            var arm0 = p.Rb.worldCenterOfMass - p.Tr.position;
            p.PivotInertia = p.Rb.inertiaTensor.magnitude + p.Rb.mass * arm0.sqrMagnitude;
        }

        /// <summary>The target of a joint for the wanted rotation of the child in its parent.</summary>
        internal static Quaternion Target(RagdollBodyPart p, Quaternion wanted) =>
            p.JointFrameInverse * (Quaternion.Inverse(wanted) * p.StartLocal) * p.JointFrame;

        /// <summary>The child's rotation in its parent when folded for a crouch.</summary>
        internal static Quaternion Crouch(RagdollBodyPart p) => Quaternion.AngleAxis(p.CrouchDeg, p.FlexAxis) * p.StartLocal;
    }
}

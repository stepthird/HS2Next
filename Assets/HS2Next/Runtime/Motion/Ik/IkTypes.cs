// IK types.
// Scene-independent: the scene layer (Runtime/Scene/Runtime/Ik) maps the model (`chara.ik`) onto IkStage.Targets /
// Applied; this folder holds the bones (IkBones), the math (IkSolver) and the stage (IkStage).
// Naming: the target index enum is IkTargetId (not IkTarget) because HS2Next.Save.IkTarget is the model's target object.
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Motion.Ik
{
    /// <summary>The 13 Studio IK targets in `IKTargetEN` order (= `HS2Next.Save.IkTargets.Names` order).</summary>
    public enum IkTargetId
    {
        Body = 0,
        LeftShoulder = 1, LeftArmChain = 2, LeftHand = 3,
        RightShoulder = 4, RightArmChain = 5, RightHand = 6,
        LeftThigh = 7, LeftLegChain = 8, LeftFoot = 9,
        RightThigh = 10, RightLegChain = 11, RightFoot = 12,
    }

    /// <summary>The five IK groups in Studio's `activeIK` order (flag = 1 &lt;&lt; index).</summary>
    public enum IkGroup { Body = 0, RightLeg = 1, LeftLeg = 2, RightArm = 3, LeftArm = 4 }

    /// <summary>What a target is to the solver.</summary>
    public enum IkTargetKind { Body, Shoulder, BendGoal, Hand, Thigh, Foot }

    /// <summary>Static facts about groups and targets.</summary>
    public static class IkGroupInfo
    {
        /// <summary>Number of groups.</summary>
        public const int Count = 5;
        /// <summary>Number of targets.</summary>
        public const int TargetCount = 13;

        /// <summary>The model's flag name of a group (`ik.groups.&lt;name&gt;`), in group order.</summary>
        public static readonly string[] MemberNames = { "body", "rightLeg", "leftLeg", "rightArm", "leftArm" };

        /// <summary>The model's target names (`ik.targets.&lt;Name&gt;`), in target order.</summary>
        public static readonly string[] TargetNames =
        {
            "Body", "LeftShoulder", "LeftArmChain", "LeftHand", "RightShoulder", "RightArmChain", "RightHand",
            "LeftThigh", "LeftLegChain", "LeftFoot", "RightThigh", "RightLegChain", "RightFoot",
        };

        /// <summary>The bone each target is copied from (capture, copies). Body = `cf_J_Hips` (Q13).</summary>
        public static readonly string[] SourceBones =
        {
            "cf_J_Hips",
            "cf_J_ArmUp00_L", "cf_J_ArmLow01_L", "cf_J_Hand_L",
            "cf_J_ArmUp00_R", "cf_J_ArmLow01_R", "cf_J_Hand_R",
            "cf_J_LegUp00_L", "cf_J_LegLow01_L", "cf_J_Foot01_L",
            "cf_J_LegUp00_R", "cf_J_LegLow01_R", "cf_J_Foot01_R",
        };

        static readonly IkGroup[] groupOf =
        {
            IkGroup.Body,
            IkGroup.LeftArm, IkGroup.LeftArm, IkGroup.LeftArm,
            IkGroup.RightArm, IkGroup.RightArm, IkGroup.RightArm,
            IkGroup.LeftLeg, IkGroup.LeftLeg, IkGroup.LeftLeg,
            IkGroup.RightLeg, IkGroup.RightLeg, IkGroup.RightLeg,
        };

        static readonly IkTargetKind[] kindOf =
        {
            IkTargetKind.Body,
            IkTargetKind.Shoulder, IkTargetKind.BendGoal, IkTargetKind.Hand,
            IkTargetKind.Shoulder, IkTargetKind.BendGoal, IkTargetKind.Hand,
            IkTargetKind.Thigh, IkTargetKind.BendGoal, IkTargetKind.Foot,
            IkTargetKind.Thigh, IkTargetKind.BendGoal, IkTargetKind.Foot,
        };

        static readonly IkTargetId[][] targetsOf =
        {
            new[] { IkTargetId.Body },
            new[] { IkTargetId.RightThigh, IkTargetId.RightLegChain, IkTargetId.RightFoot },
            new[] { IkTargetId.LeftThigh, IkTargetId.LeftLegChain, IkTargetId.LeftFoot },
            new[] { IkTargetId.RightShoulder, IkTargetId.RightArmChain, IkTargetId.RightHand },
            new[] { IkTargetId.LeftShoulder, IkTargetId.LeftArmChain, IkTargetId.LeftHand },
        };

        /// <summary>The group a target belongs to.</summary>
        public static IkGroup GroupOf(IkTargetId t) => groupOf[(int)t];

        /// <summary>The solver role of a target.</summary>
        public static IkTargetKind KindOf(IkTargetId t) => kindOf[(int)t];

        /// <summary>The targets of a group (root / effector, bend goal, end for limbs; Body alone).</summary>
        public static IReadOnlyList<IkTargetId> TargetsOf(IkGroup g) => targetsOf[(int)g];

        /// <summary>True for the four targets whose rotation the solver uses (hands, feet); every other target's `rot`
        /// is stored as 0 by HS2Next writers.</summary>
        public static bool UsesRotation(IkTargetId t) { var k = kindOf[(int)t]; return k == IkTargetKind.Hand || k == IkTargetKind.Foot; }

        /// <summary>The `BoneGroup` flag of a group (1, 2, 4, 8, 16).</summary>
        public static int Flag(IkGroup g) => 1 << (int)g;

        /// <summary>True for the four limb groups.</summary>
        public static bool IsLimb(IkGroup g) => g != IkGroup.Body;

        /// <summary>True for the two arm groups.</summary>
        public static bool IsArm(IkGroup g) => g == IkGroup.RightArm || g == IkGroup.LeftArm;
    }

    /// <summary>
    /// One effective target in character-root space: position, and Euler rotation (degrees; used by hands and feet
    /// only). <see cref="Has"/> false = missing (the stage takes a runtime snapshot the first time it runs with the
    /// target's group applied, and then sets <see cref="Snapshot"/>).
    /// </summary>
    public struct IkTargetValue
    {
        public bool Has;
        /// <summary>The value is the stage's runtime snapshot (never written to the model by the runtime).</summary>
        public bool Snapshot;
        public Vector3 Pos;
        /// <summary>Euler degrees (root space).</summary>
        public Vector3 Rot;

        public IkTargetValue(Vector3 pos, Vector3 rot) { Has = true; Snapshot = false; Pos = pos; Rot = rot; }

        public override string ToString() => Has ? $"pos {Pos.x:R},{Pos.y:R},{Pos.z:R} rot {Rot.x:R},{Rot.y:R},{Rot.z:R}{(Snapshot ? " (snapshot)" : "")}" : "missing";
    }

    /// <summary>App-wide IK switches (app settings `kinematics`, mirrored here by the app layer).</summary>
    public static class IkOptions
    {
        /// <summary>Vanilla exclusivity preset: switching FK mode on switches IK off and vice versa (operations only).</summary>
        public static bool Exclusive;
        /// <summary>Limit the elbow / knee interior angle to [<see cref="LimitMinInterior"/>, 180°) in the two-bone solve.</summary>
        public static bool RotationLimits;
        /// <summary>Minimum interior angle (degrees) with <see cref="RotationLimits"/>.</summary>
        public static float LimitMinInterior = 20f;
        /// <summary>Vanilla parity: missing targets are snapshot after the first step following the build (HS2's InitTarget).</summary>
        public static bool MissingTargetsFirstFrame;
        /// <summary>How the two-bone solve orients the limb bones about their own axes.</summary>
        /// Default BendPlane (Phase 3.4 fix round: closer to HS2, K25); SwingOnly is the 3.4 spec's original rule.
        public static IkLimbOrientation LimbOrientation = IkLimbOrientation.BendPlane;
    }

    /// <summary>Limb bone orientation of the two-bone solve.</summary>
    public enum IkLimbOrientation
    {
        /// <summary>Swing-only rotations: the bones keep the input pose's roll (twist about their own axes).</summary>
        SwingOnly = 0,
        /// <summary>After the swing the upper bone is twisted about its axis so that its rest bend direction (the side the
        /// mid joint protrudes to at discovery, in the upper bone's own frame: the hinge) points to the pole side; the twist
        /// fades in with the pole's distance from the root–goal line (IkSolver.PoleFadeMin / PoleFadeMax of L1 + L2), so a
        /// bend goal on the line (copied from a straight limb) keeps the input's roll. The lower bone then swings onto the
        /// goal.</summary>
        BendPlane = 1,
    }

    /// <summary>Which bend-plane source a limb used.</summary>
    public enum IkPoleSource { None = 0, Target = 1, Clip = 2, Rest = 3 }

    /// <summary>Per-step solve information (runtime only; `ik.list solve`).</summary>
    public sealed class IkSolveInfo
    {
        /// <summary>A solve ran (at least one group applied).</summary>
        public bool Valid;
        /// <summary>CharaMotion step index of the solve.</summary>
        public int Step;
        /// <summary>Per target: distance of its bone from the target after the solve (world units); NaN for bend goals
        /// and targets of groups that did not run. Body: hips vs target; shoulders: upper-arm root vs target (clavicle
        /// distance residual); thighs: thigh vs target (thighs orient the hips only); hands / feet: end residual.</summary>
        public readonly float[] Residual = new float[IkGroupInfo.TargetCount];
        /// <summary>Per target (hands / feet): the reach was clamped (beyond d_max, below d_min or the limit interval).</summary>
        public readonly bool[] Clamped = new bool[IkGroupInfo.TargetCount];
        /// <summary>Per group (limbs): the pole source used; None for Body and groups that did not run.</summary>
        public readonly IkPoleSource[] Pole = new IkPoleSource[IkGroupInfo.Count];
        /// <summary>The body frame turn was skipped because a frame was degenerate (both legs applied).</summary>
        public bool BodyDegenerate;
        /// <summary>The torso ran (Body and both arms applied).</summary>
        public bool TorsoRan;
        /// <summary>A torso frame was degenerate in a pass (that pass left the bones unchanged).</summary>
        public bool TorsoDegenerate;
        /// <summary>Chest-frame angle left after the two passes (degrees); NaN when the torso did not run.</summary>
        public float TorsoResidual = float.NaN;
        /// <summary>Solve time (the solver only, ms).</summary>
        public double Milliseconds;
        /// <summary>Whole stage step time (latch, FK base, solve, mask; ms).</summary>
        public double StageMilliseconds;

        public void Clear()
        {
            Valid = false; Step = 0;
            for (int i = 0; i < Residual.Length; i++) { Residual[i] = float.NaN; Clamped[i] = false; }
            for (int i = 0; i < Pole.Length; i++) Pole[i] = IkPoleSource.None;
            BodyDegenerate = TorsoRan = TorsoDegenerate = false;
            TorsoResidual = float.NaN;
            Milliseconds = StageMilliseconds = 0;
        }

        public void CopyFrom(IkSolveInfo o)
        {
            Valid = o.Valid; Step = o.Step;
            System.Array.Copy(o.Residual, Residual, Residual.Length);
            System.Array.Copy(o.Clamped, Clamped, Clamped.Length);
            System.Array.Copy(o.Pole, Pole, Pole.Length);
            BodyDegenerate = o.BodyDegenerate; TorsoRan = o.TorsoRan; TorsoDegenerate = o.TorsoDegenerate;
            TorsoResidual = o.TorsoResidual; Milliseconds = o.Milliseconds; StageMilliseconds = o.StageMilliseconds;
        }
    }
}

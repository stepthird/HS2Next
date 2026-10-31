// Look targets of one character: the helper objects created at load, the target types 0..9 of the eye and neck look
// and the card's look fields, and Studio's movable look target "Look At Target"
// used by eye pattern 4.
//
// Target type 0 ("camera") is resolved every step: LookTargets.DefaultCamera when set (the Studio camera
// rig), else Camera.main. With neither, the eyes do nothing and the neck reuses its last target position
//. The guide gizmo of the look target is the Studio layer's job; this class only reports when it
// should be visible (LookAtGuideVisible / LookAtGuideChanged).
using System;
using HS2Next.Chara;
using UnityEngine;

namespace HS2Next.Motion.Look
{
    /// <summary>The ChaFileStatus look fields with their MemberInit defaults.</summary>
    [Serializable]
    public sealed class LookStatus
    {
        public int eyesLookPtn, eyesTargetType;
        public float eyesTargetAngle, eyesTargetRange = 1f, eyesTargetRate = 1f;
        public int neckLookPtn, neckTargetType;
        public float neckTargetAngle, neckTargetRange = 1f, neckTargetRate = 1f;

        public void CopyFrom(LookStatus s)
        {
            eyesLookPtn = s.eyesLookPtn; eyesTargetType = s.eyesTargetType; eyesTargetAngle = s.eyesTargetAngle;
            eyesTargetRange = s.eyesTargetRange; eyesTargetRate = s.eyesTargetRate;
            neckLookPtn = s.neckLookPtn; neckTargetType = s.neckTargetType; neckTargetAngle = s.neckTargetAngle;
            neckTargetRange = s.neckTargetRange; neckTargetRate = s.neckTargetRate;
        }
    }

    public sealed class LookTargets
    {
        /// <summary>The camera that target type 0 follows (null: Camera.main).</summary>
        public static Transform DefaultCamera;

        /// <summary>DefaultCamera, else Camera.main's transform, else null.</summary>
        public static Transform CameraTransform
        {
            get
            {
                if (DefaultCamera != null) return DefaultCamera;
                var c = Camera.main;
                return c != null ? c.transform : null;
            }
        }

        /// <summary>rotDeg of target types 1..9.</summary>
        public static readonly float[] TypeAngles = { 0f, 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f, 0f };

        public Transform NeckTargetP, NeckTarget, EyesTargetP, EyesTarget;
        /// <summary>Studio's look-target object (child of cf_J_Head); null when the skeleton has none.</summary>
        public Transform LookAtTarget;
        /// <summary>Its local position in the prefab (Studio's LookAtTargetInfo starts from it).</summary>
        public Vector3 LookAtTargetDefaultLocal;
        public readonly LookStatus Status = new LookStatus();

        readonly NeckLookStage neck;
        readonly EyeLookStage eyes;

        /// <summary>True while eye pattern 4 uses the look target (the Studio guide is shown).</summary>
        public bool LookAtGuideVisible { get; private set; }
        /// <summary>Raised when LookAtGuideVisible changes.</summary>
        public event Action<bool> LookAtGuideChanged;

        public LookTargets(NeckLookStage neck, EyeLookStage eyes) { this.neck = neck; this.eyes = eyes; }

        /// <summary>Creates the four helper objects and finds Studio's look target.</summary>
        public void CreateObjects(CharaContext ctx, Transform lookAtTarget)
        {
            var bones = ctx.BodyBones;
            if (bones != null && bones.TryGetValue("cf_J_Spine03", out var spine3))
            {
                NeckTargetP = Child(spine3, "N_NeckLookTargetP");
                NeckTarget = Child(NeckTargetP, "N_NeckLookTarget");
            }
            if (bones != null && bones.TryGetValue("cf_J_Head_s", out var headS))
            {
                EyesTargetP = Child(headS, "N_EyesLookTargetP");
                EyesTarget = Child(EyesTargetP, "N_EyesLookTarget");
            }
            LookAtTarget = lookAtTarget;
            if (LookAtTarget == null && bones != null && bones.TryGetValue("cf_J_Head", out var head))
                LookAtTarget = head.Find("Look At Target");
            if (LookAtTarget != null) LookAtTargetDefaultLocal = LookAtTarget.localPosition;
        }

        static Transform Child(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null) return existing;
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; t.localScale = Vector3.one;
            return t;
        }

        /// <summary>ChaControl.ChangeLookEyesTarget. type -1 = the stored type; trfTarg overrides everything.</summary>
        public void ChangeLookEyesTarget(int targetType, Transform trfTarg = null, float rate = 0.5f, float rotDeg = 0f, float range = 1f, float dis = 2f)
        {
            if (eyes == null) return;
            if (targetType == -1) targetType = Status.eyesTargetType; else Status.eyesTargetType = targetType;
            eyes.Target = null; eyes.FollowCamera = false;
            if (trfTarg != null) { eyes.Target = trfTarg; return; }
            if (targetType == 0) eyes.FollowCamera = true;
            else if (EyesTargetP != null && EyesTarget != null)
            {
                if (targetType >= 1 && targetType <= 9) { rotDeg = TypeAngles[targetType]; range = 1f; }
                EyesTargetP.localPosition = new Vector3(0f, 0.7f, 0f);
                EyesTargetP.localEulerAngles = new Vector3(0f, 0f, 360f - rotDeg);
                eyes.Target = EyesTarget;
                EyesTarget.localPosition = new Vector3(0f, targetType == 9 ? 0f : Mathf.Lerp(0f, range, rate), dis);
            }
            Status.eyesTargetAngle = rotDeg; Status.eyesTargetRange = range; Status.eyesTargetRate = rate;
        }

        /// <summary>ChaControl.ChangeLookNeckTarget: types 1..8 only, helper above cf_J_Spine03.</summary>
        public void ChangeLookNeckTarget(int targetType, Transform trfTarg = null, float rate = 0.5f, float rotDeg = 0f, float range = 1f, float dis = 0.8f)
        {
            if (neck == null) return;
            if (targetType == -1) targetType = Status.neckTargetType; else Status.neckTargetType = targetType;
            neck.Target = null; neck.FollowCamera = false;
            if (trfTarg != null) { neck.Target = trfTarg; return; }
            if (targetType == 0) neck.FollowCamera = true;
            else if (NeckTargetP != null && NeckTarget != null)
            {
                if (targetType >= 1 && targetType <= 8) { rotDeg = TypeAngles[targetType]; range = 1f; }
                NeckTargetP.localPosition = new Vector3(0f, 2.7f, 0f);
                NeckTargetP.localEulerAngles = new Vector3(0f, 0f, 360f - rotDeg);
                neck.Target = NeckTarget;
                NeckTarget.localPosition = new Vector3(0f, Mathf.Lerp(0f, range, rate), dis);
            }
            Status.neckTargetAngle = rotDeg; Status.neckTargetRange = range; Status.neckTargetRate = rate;
        }

        // ---- Studio look target

        /// <summary>The look target's position relative to cf_J_Head (Studio's LookAtTargetInfo.changeAmount.pos).</summary>
        public Vector3 LookAtTargetLocalPosition
        {
            get => LookAtTarget != null ? LookAtTarget.localPosition : LookAtTargetDefaultLocal;
            set { if (LookAtTarget != null) LookAtTarget.localPosition = value; }
        }

        /// <summary>Moves the look target to a world position (what the guide's world-space move does).</summary>
        public void MoveLookAtTarget(Vector3 world)
        {
            if (LookAtTarget != null) LookAtTarget.position = world;
        }

        public void ResetLookAtTarget() => LookAtTargetLocalPosition = LookAtTargetDefaultLocal;

        /// <summary>Entering / leaving eye pattern 4 (OCIChar.ChangeLookEyesPtn): retarget the eyes and show / hide the guide.</summary>
        internal void OnEyePatternChange(int oldPtn, int ptn)
        {
            if (eyes == null) return;
            if (ptn == 4 && oldPtn != 4)
            {
                if (LookAtTarget != null) { eyes.Target = LookAtTarget; eyes.FollowCamera = false; }
                SetGuide(true);
            }
            else if (oldPtn == 4 && ptn != 4)
            {
                eyes.Target = null; eyes.FollowCamera = true;
                SetGuide(false);
            }
        }

        void SetGuide(bool on)
        {
            if (LookAtGuideVisible == on) return;
            LookAtGuideVisible = on;
            LookAtGuideChanged?.Invoke(on);
        }
    }
}

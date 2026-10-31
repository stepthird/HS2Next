// Facade of the neck look and eye look for the Studio / UI layer: pattern operations, add defaults, UI button mapping,
// look target, FK interplay and scene persistence.
// One component on the character root (CharaLook.Of(root)); it has no Update: the stages run
// inside the character's CharaMotion.
using System;
using UnityEngine;

namespace HS2Next.Motion.Look
{
    [DisallowMultipleComponent]
    public sealed class CharaLook : MonoBehaviour
    {
        /// <summary>Neck panel buttons -> neck pattern (front, camera, animation, fixed); pattern 2 (AWAY) has no button.</summary>
        public static readonly int[] NeckButtonPatterns = { 0, 1, 3, 4 };
        /// <summary>Eye pattern that uses the movable look target.</summary>
        public const int EyeLookTargetPattern = 4;
        /// <summary>Neck pattern used while the FK neck group is active (FIX).</summary>
        public const int FkNeckPattern = 4;

        public NeckLookStage Neck { get; private set; }
        public EyeLookStage Eyes { get; private set; }
        public LookTargets Targets { get; private set; }
        public LookStatus Status => Targets?.Status;
        int neckPtnOld;

        public static CharaLook Of(GameObject chara) => chara != null ? chara.GetComponent<CharaLook>() : null;

        internal void Bind(NeckLookStage neck, EyeLookStage eyes, LookTargets targets)
        {
            Neck = neck; Eyes = eyes; Targets = targets;
        }

        // ---------------------------------------------------------------- neck patterns

        /// <summary>Current neck pattern (ChaControl.GetLookNeckPtn).</summary>
        public int NeckPattern => Status?.neckLookPtn ?? 0;

        /// <summary>Sets the neck look pattern as Studio does: raw pattern number (0..4 with vanilla data) and controller rate.</summary>
        public void SetNeckPattern(int ptn, float rate = 1f)
        {
            if (Neck == null) return;
            ptn = Neck.ClampPtn(ptn);
            Status.neckLookPtn = ptn;
            Neck.PtnNo = ptn;
            Neck.Rate = rate;
        }

        /// <summary>Neck panel button 0..3.</summary>
        public void SetNeckButton(int button) => SetNeckPattern(NeckButtonPatterns[Mathf.Clamp(button, 0, NeckButtonPatterns.Length - 1)]);

        /// <summary>The highlighted neck button for the current pattern (-1 for AWAY).</summary>
        public int NeckButton => Array.IndexOf(NeckButtonPatterns, NeckPattern);

        /// <summary>Neck buttons are disabled while FK is on and the FK neck group is active.</summary>
        public static bool NeckButtonsInteractable(bool enableFK, bool fkNeckGroupActive) => !enableFK || !fkNeckGroupActive;

        /// <summary>Controller rate (HS2 always uses 1; X and Y of the target only).</summary>
        public float NeckRate
        {
            get => Neck?.Rate ?? 1f;
            set { if (Neck != null) Neck.Rate = value; }
        }

        /// <summary>FK layer hook: activating the FK neck group remembers the pattern and switches to FIX (4); deactivating restores it.</summary>
        public void SetFkNeckGroupActive(bool active)
        {
            if (active) { neckPtnOld = NeckPattern; SetNeckPattern(FkNeckPattern); }
            else SetNeckPattern(neckPtnOld);
        }

        /// <summary>The pattern that SetFkNeckGroupActive(false) restores (Studio's neckPtnOld; also set by InitKinematic).</summary>
        public int NeckPatternBeforeFk { get => neckPtnOld; set => neckPtnOld = value; }

        // ---------------------------------------------------------------- eye patterns

        public int EyePattern => Status?.eyesLookPtn ?? 0;

        /// <summary>ChaControl.ChangeLookEyesPtn: raw pattern number, no target change.</summary>
        public void SetEyePatternRaw(int ptn)
        {
            if (Eyes == null) return;
            ptn = Eyes.ClampPtn(ptn);
            Status.eyesLookPtn = ptn;
            Eyes.PtnNo = ptn;
        }

        /// <summary>Sets the eye look pattern as Studio does: entering 4 targets the look-target object and shows its guide, leaving 4 returns to the camera.</summary>
        public void SetEyePattern(int ptn, bool force = false)
        {
            if (Eyes == null) return;
            ptn = Eyes.ClampPtn(ptn);
            int old = force ? -1 : EyePattern;
            Targets?.OnEyePatternChange(old, ptn);
            SetEyePatternRaw(ptn);
        }

        /// <summary>Eye panel button i = eye pattern i.</summary>
        public void SetEyeButton(int i) => SetEyePattern(i);

        /// <summary>The look-target size slider is interactable only in eye pattern 4.</summary>
        public bool LookTargetSizeInteractable => EyePattern == EyeLookTargetPattern;

        // ---------------------------------------------------------------- targets

        /// <summary>Eye target: an explicit transform (null = camera, target type 0).</summary>
        public void SetEyeTarget(Transform t)
        {
            if (t != null) Targets?.ChangeLookEyesTarget(-1, t);
            else Targets?.ChangeLookEyesTarget(0);
        }

        /// <summary>Neck target: an explicit transform (null = camera, target type 0).</summary>
        public void SetNeckTarget(Transform t)
        {
            if (t != null) Targets?.ChangeLookNeckTarget(-1, t);
            else Targets?.ChangeLookNeckTarget(0);
        }

        /// <summary>Target types: 0 camera, 1..8 directions around the head, 9 (eyes) straight ahead.</summary>
        public void SetEyeTargetType(int type, float rate = 0.5f) => Targets?.ChangeLookEyesTarget(type, null, rate);
        public void SetNeckTargetType(int type, float rate = 0.5f) => Targets?.ChangeLookNeckTarget(type, null, rate);

        public Transform LookAtTarget => Targets?.LookAtTarget;

        // ---------------------------------------------------------------- switches

        /// <summary>Per-character eye look switch.</summary>
        public bool EyeLookEnabled
        {
            get => Eyes != null && Eyes.LookEnabled;
            set { if (Eyes != null) Eyes.LookEnabled = value; }
        }

        /// <summary>The game's global eye-look switch (all characters).</summary>
        public static bool EyeLookGlobalEnabled
        {
            get => EyeLookStage.GlobalEnabled;
            set => EyeLookStage.GlobalEnabled = value;
        }

        /// <summary>NeckLookCalcVer2.SetEnable.</summary>
        public bool NeckLookEnabled
        {
            get => Neck != null && Neck.CalcEnabled;
            set { if (Neck != null) Neck.CalcEnabled = value; }
        }

        // ---------------------------------------------------------------- read-out

        /// <summary>Smoothed eye angles (degrees; H positive = character's right, V positive = down).</summary>
        public Vector2 EyeAngle(int eye) => Eyes != null ? new Vector2(Eyes.AngleH(eye), Eyes.AngleV(eye)) : Vector2.zero;

        /// <summary>Per-bone neck angles (x = H, y = V); bone 0 = neck, 1 = head.</summary>
        public Vector2 NeckBoneAngle(int bone) =>
            Neck?.Bones != null && bone >= 0 && bone < Neck.Bones.Length ? new Vector2(Neck.Bones[bone].AngleH, Neck.Bones[bone].AngleV) : Vector2.zero;

        /// <summary>The total the chain was asked to turn (x = V, y = H).</summary>
        public Vector2 NeckNowAngle => Neck?.NowAngle ?? Vector2.zero;

        // ---------------------------------------------------------------- persistence

        /// <summary>Neck state for a scene: pattern and the per-bone fixed rotations (dst length >= bone count).</summary>
        public int SaveNeck(Quaternion[] dst) { Neck?.SaveFixAngles(dst); return NeckPattern; }
        public void LoadNeck(int ptn, Quaternion[] src) { if (Neck == null) return; Neck.LoadFixAngles(Neck.ClampPtn(ptn), src); Status.neckLookPtn = Neck.PtnNo; }
        public void SaveEyes(Quaternion[] dst) => Eyes?.SaveAngles(dst);
        public void LoadEyes(Quaternion[] src) => Eyes?.LoadAngles(src);

        /// <summary>ChaControl's mannequin switch: neck with skipCalc (immediate), then the eyes, outside the normal step.</summary>
        public void ForceUpdate(float dt)
        {
            Neck?.ForceStep(dt);
            if (Eyes != null && Eyes.Enabled) Eyes.Step(new MotionFrame(dt, dt, 0f, 0));
        }
    }
}

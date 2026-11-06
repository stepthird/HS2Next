// Expression motion stage (MotionPhase.Face): the per-frame part —
// the `FaceBlendShape.OnLateUpdate` callback with blink, openness sources, weights
// (write order eyebrow → eyes → mouth), cross-fades, mouth width jitter and its
// application to `cf_J_MouthMove`, and the optional eye-look eyelid correction (off by
// default as in the game). Game-side and Studio-side operations (pattern changes, open values, blink
// flag, tears, cheek, ...) live in CharaExpression, reachable as `Api`.
//
// Time: the stage keeps its own clock (sum of the scaled step lengths) as the blink's `Time.time`; the
// cross-fade and the width timer use the step length. Step with dt 0 (pause) holds every state; the
// game would still perform frame-counted blink transitions while paused (README, deviations).
using UnityEngine;

namespace HS2Next.Motion.Face
{
    public sealed class ExpressionStage : IMotionStage
    {
        public MotionPhase Phase => MotionPhase.Face;
        public string Name => "face";
        public bool Enabled { get; set; } = true;

        public readonly FaceRig Rig;
        public readonly FbsControl Eyebrow, Eyes, Mouth;
        public readonly BlinkControl Blink;
        public readonly MouthWidthJitter Width;

        /// <summary>Eyebrow `SyncBlink`: the eyebrow openness follows the blink (no visible effect on vanilla heads).</summary>
        public bool SyncBlink;
        /// <summary>`voiceValue`: the mouth openness source; 0 without a voice. Fed by CharaExpression.VoiceLevel.</summary>
        public float VoiceValue;

        /// <summary>eyelid correction from the eye-look outputs (default false = vanilla behaviour).</summary>
        public bool EyelidCorrection;
        /// <summary>HS2Next fix: drop the correction (−1) while blinking is disabled instead of keeping the stale value.</summary>
        public bool ResetCorrectionWhenFixed;
        /// <summary>The eye-look stage's outputs (null when no look stage is attached; the correction is then unavailable).</summary>
        public IEyeLookOutputs EyeLook;
        /// <summary>`EyeLookUpCorrect` / `EyeLookDownCorrect` / `EyeLookSideCorrect` of the head.</summary>
        public float UpCorrect, DownCorrect, SideCorrect;

        /// <summary>`cf_J_MouthMove` (`trfMouthAdjustWidth`); its local scale x receives the mouth width.</summary>
        public Transform MouthWidthBone;
        /// <summary>Status `mouthAdjustWidth` (default true): apply the width to the bone; false leaves the bone as it is.</summary>
        public bool ApplyMouthWidth = true;
        /// <summary>The character (set by FaceSetup): while a card's bone modifier gives the width bone a scale, that modifier owns the bone's scale and the width is not written.</summary>
        internal HS2Next.Chara.CharaContext Context;

        /// <summary>The Studio / game facade (set by FaceSetup).</summary>
        public CharaExpression Api { get; internal set; }

        /// <summary>Accumulated scaled time (the blink clock).</summary>
        public float Clock { get; private set; }
        /// <summary>`blinkRate` of the last computation (−1 = blinking fixed).</summary>
        public float LastBlinkRate { get; private set; } = -1f;
        public int Computations { get; private set; }

        float lastWidth = float.NaN;

        public ExpressionStage(FaceRig rig, IFaceRandom random)
        {
            Rig = rig;
            Eyebrow = new FbsControl("eyebrow", rig.Eyebrow);
            Eyes = new FbsControl("eyes", rig.Eyes);
            Mouth = new FbsControl("mouth", rig.Mouth);
            SyncBlink = rig.Eyebrow.SyncBlink;
            Blink = new BlinkControl((byte)Mathf.Clamp(rig.BlinkFrequency, 0, 255), rig.BaseSpeed, random);
            Width = new MouthWidthJitter(rig.Mouth, random);
            UpCorrect = rig.EyeLookUpCorrect; DownCorrect = rig.EyeLookDownCorrect; SideCorrect = rig.EyeLookSideCorrect;
        }

        /// <summary>Replaces the random source of the blink and the mouth width (tests).</summary>
        public void SetRandom(IFaceRandom random)
        {
            if (random == null) return;
            Blink.Random = random; Width.Random = random;
        }

        public void Step(in MotionFrame frame)
        {
            float dt = frame.Dt;
            if (dt > 0f)
            {
                Clock += dt;
                Blink.Calc(Clock);   //
            }
            Compute(dt);
        }

        /// <summary>One computation from the current state without advancing time (used at attach).</summary>
        public void Evaluate() => Compute(0f);

        void Compute(float dt)
        {
            // blinkRate and the eyelid correction.
            float blinkRate;
            if (Blink.FixedFlags == 0)
            {
                blinkRate = Blink.OpenRate;
                if (!EyelidCorrection) Eyes.CorrectOpenMax = -1f;
                else if (EyeLook != null && EyeLook.HasOutput) Eyes.CorrectOpenMax = Correction(EyeLook.AngleHRate(0), EyeLook.AngleVRate);
            }
            else
            {
                blinkRate = -1f;
                if (!EyelidCorrection || ResetCorrectionWhenFixed) Eyes.CorrectOpenMax = -1f;
            }
            LastBlinkRate = blinkRate;

            // eyebrow, eyes, mouth (the order decides o_head index 0).
            if (blinkRate >= 0f && SyncBlink) Eyebrow.OpenRate = blinkRate;
            Eyebrow.Calculate(dt);
            if (blinkRate >= 0f) Eyes.OpenRate = blinkRate;
            Eyes.Calculate(dt);
            Mouth.OpenRate = VoiceValue;
            Mouth.Calculate(dt);
            if (Width.Enabled) Width.Calc(Mouth.OpenRate, dt);

            // the width goes to cf_J_MouthMove. Written only when it changes (README), never reset when disabled.
            var mods = Context?.Bones;
            if (ApplyMouthWidth && MouthWidthBone && !(mods && mods.HasScale(MouthWidthBone)))
            {
                float s = Width.AdjustWidthScale;
                if (s != lastWidth)
                {
                    var ls = MouthWidthBone.localScale;
                    ls.x = s;
                    MouthWidthBone.localScale = ls;
                    lastWidth = s;
                }
            }
            Computations++;
        }

        /// <summary>corrected eyes maximum from the left eye's horizontal rate H and the vertical rate V (V &gt; 0 = up).</summary>
        public float Correction(float h, float v)
        {
            float up = UpCorrect, down = DownCorrect, side = SideCorrect, max = Eyes.OpenMax;
            float bas = Mathf.Min(1f - up, max);
            float c = v > 0f ? Acc(up, v) : -Acc(down, -v);
            c -= Acc(side, Mathf.Abs(h));
            c = Mathf.Clamp(c, -Mathf.Max(down, side), up);
            c *= max;
            return bas + c;
        }

        static float Acc(float x, float t) => Mathf.Lerp(0f, x, Mathf.Sqrt(Mathf.Max(0f, t)));

        /// <summary>Finishes the cross-fades of the three controls (the target patterns show at once).</summary>
        public void FinishFades()
        {
            Eyebrow.FinishFade(); Eyes.FinishFade(); Mouth.FinishFade();
        }

        /// <summary>The mouth width bone is written again on the next computation (another writer — a face-shape edit or the
        /// bone modifiers re-applied by the edit units — has written its scale).</summary>
        public void InvalidateMouthWidth() => lastWidth = float.NaN;

        /// <summary>IMotionStage.Reset (teleport / re-enable): finish the cross-fades, eyes open with a fresh idle wait,
        /// restart the mouth width segment and rewrite the bone on the next computation. Pattern and open values are kept.</summary>
        public void Reset()
        {
            FinishFades();
            Blink.ResetIdle(Clock);
            Width.Restart();
            lastWidth = float.NaN;
        }
    }
}

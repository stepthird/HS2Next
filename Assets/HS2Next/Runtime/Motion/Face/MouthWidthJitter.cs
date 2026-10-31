// Mouth width jitter while speaking: the data `useAjustWidthScale`,
// `randTime*`, `randScale*`, `openRefValue`, applied to `cf_J_MouthMove`.
//
// A random-duration linear timer (`TimeProgressCtrlRandom`) walks the width from one random target to
// the next; the voice openness narrows it by openRefValue × openness; the result replaces 1 only while
// the openness (= voice level) exceeds 0.2. The enable flag is rewritten by every mouth-pattern change
// (CharaExpression: off for patterns 9..16), so the serialized value only matters until head load.
using UnityEngine;

namespace HS2Next.Motion.Face
{
    public sealed class MouthWidthJitter
    {
        /// <summary>The voice level above which the jittered width is used.</summary>
        public const float VoiceThreshold = 0.2f;

        /// <summary>`useAjustWidthScale`: run the computation (driven by the mouth pattern).</summary>
        public bool Enabled;
        /// <summary>`objAdjustWidthScale`: optional object whose local scale becomes (width, 1, 1) (null on vanilla heads).</summary>
        public Transform Target;
        public float RandTimeMin, RandTimeMax, RandScaleMin, RandScaleMax, OpenRefValue;

        /// <summary>Runtime state (all initial 1). Settable for tests.</summary>
        public float SclNow = 1f, SclStart = 1f, SclEnd = 1f;
        /// <summary>The width result: 1 unless the voice level is above 0.2 (kept from the last enabled frame while disabled).</summary>
        public float AdjustWidthScale = 1f;
        /// <summary>The width timer (current segment duration and elapsed time). Settable for tests.</summary>
        public float SegmentDuration, SegmentElapsed;

        IFaceRandom rng;
        public IFaceRandom Random { get => rng; set => rng = value ?? rng; }

        public MouthWidthJitter(FbsControlData mouth, IFaceRandom random)
        {
            rng = random;
            Enabled = mouth.UseAdjustWidthScale;
            Target = mouth.ObjAdjustWidthScale;
            RandTimeMin = mouth.RandTimeMin; RandTimeMax = mouth.RandTimeMax;
            RandScaleMin = mouth.RandScaleMin; RandScaleMax = mouth.RandScaleMax;
            OpenRefValue = mouth.OpenRefValue;
            Restart();
        }

        /// <summary>`Init`: first random segment, started at progress 0; widths back to 1.</summary>
        public void Restart()
        {
            SclNow = SclStart = SclEnd = AdjustWidthScale = 1f;
            SegmentDuration = rng.Range(RandTimeMin, RandTimeMax);
            SegmentElapsed = 0f;
        }

        // TimeProgressCtrlRandom read: advance; on completion return 1 once and restart with a new duration.
        float ReadTimer(float dt)
        {
            SegmentElapsed += dt;
            if (SegmentDuration > 0f && SegmentElapsed < SegmentDuration) return SegmentElapsed / SegmentDuration;
            SegmentDuration = rng.Range(RandTimeMin, RandTimeMax);
            SegmentElapsed = 0f;
            return 1f;
        }

        /// <summary>with this frame's mouth openness (the voice level). Call only while Enabled.</summary>
        public void Calc(float openRate, float dt)
        {
            AdjustWidthScale = 1f;
            float t = ReadTimer(dt);
            if (t == 1f)
            {
                SclStart = SclEnd;
                SclEnd = rng.Range(RandScaleMin, RandScaleMax);
                t = 0f;
            }
            SclNow = Mathf.Lerp(SclStart, SclEnd, t);
            SclNow = Mathf.Max(0f, SclNow - OpenRefValue * openRate);
            if (openRate > VoiceThreshold) AdjustWidthScale = SclNow;
            if (Target) Target.localScale = new Vector3(AdjustWidthScale, 1f, 1f);
        }
    }
}

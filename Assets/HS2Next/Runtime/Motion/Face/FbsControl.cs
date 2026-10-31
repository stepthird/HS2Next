// One expression control (eyebrow, eyes or mouth): the pattern mixes, the 0.15 s cross-fade and the
// per-frame blend-shape weights (
// pattern changes and cross-fade timer; the runtime state).
//
// Weights: value = Lerp(OpenMin, top, openRate) (top = corrected max if ≥ 0, else OpenMax), replaced
// by FixedRate when ≥ 0; n = trunc(value × 100) in float32 (0.53 → 52, 0.59 → 58), clamped 0..100;
// every Close/Open index of every pattern of a target is rewritten each frame (non-current ones to 0);
// back mix × (1 − b) and current mix × b, Close gets (100 − n), Open gets n.
// Per-frame work uses preallocated arrays only.
using System;
using UnityEngine;

namespace HS2Next.Motion.Face
{
    public sealed class FbsControl
    {
        /// <summary>Cross-fade duration of a blended pattern change (`FBSAssist.TimeProgressCtrl`).</summary>
        public const float BlendTime = 0.15f;

        /// <summary>Result of <see cref="ChangeFace"/>.</summary>
        public enum ChangeResult
        {
            Ok = 0,
            /// <summary>a Close/Open index of a requested pattern is ≥ the mesh's blendShapeCount.</summary>
            ShapeOutOfRange = 1,
            /// <summary>the mix weights add up to more than 1.</summary>
            WeightSum = 2,
            /// <summary>HS2Next: a pattern key is negative or ≥ the pattern count (the original throws).</summary>
            BadPattern = 3,
        }

        sealed class Target
        {
            public FbsTargetData Data;
            public SkinnedMeshRenderer R;
            /// <summary>Distinct blend-shape indices of the whole table (−1 and indices ≥ blendShapeCount left out), in first-use order.</summary>
            public int[] Keys;
            /// <summary>Scratch weights, one per key.</summary>
            public float[] W;
            /// <summary>Per pattern: position of Close / Open in Keys, −1 = none.</summary>
            public int[] ClosePos, OpenPos;
            public int ShapeCount;
        }

        public readonly string Name;
        readonly Target[] targets;

        public float OpenMin, OpenMax, FixedRate;
        /// <summary>Current openness (initial 0); set by the driver before each computation.</summary>
        public float OpenRate;
        /// <summary>Eye-look corrected maximum; −1 = not set (only the eyes use it).</summary>
        public float CorrectOpenMax = -1f;
        public int PatternCount { get; }
        public int TargetCount => targets.Length;
        public FbsTargetData TargetData(int i) => targets[i].Data;

        // dictNowFace / dictBackFace: pattern → mix weight (initial {0: 1}).
        readonly int[] nowP, backP;
        readonly float[] nowW, backW;
        int nowN, backN;
        // Cross-fade timer (initial: finished).
        float fadeElapsed = BlendTime, fadeProgress = 1f;
        readonly int[] oneP = new int[1];
        readonly float[] oneW = { 1f };

        /// <summary>Diagnostics of the last computation: value after the mapping, n, cross-fade progress b.</summary>
        public float LastValue { get; private set; }
        public int LastN { get; private set; }
        public float LastBlend { get; private set; } = 1f;

        public FbsControl(string name, FbsControlData data)
        {
            Name = name;
            OpenMin = data.OpenMin; OpenMax = data.OpenMax; FixedRate = data.FixedRate;
            PatternCount = data.PatternCount;
            targets = new Target[data.Targets.Length];
            for (int i = 0; i < targets.Length; i++) targets[i] = Build(data.Targets[i], PatternCount);
            int cap = Math.Max(1, PatternCount);
            nowP = new int[cap]; backP = new int[cap]; nowW = new float[cap]; backW = new float[cap];
            nowP[0] = 0; nowW[0] = 1f; nowN = 1;
            backP[0] = 0; backW[0] = 1f; backN = 1;
        }

        static Target Build(FbsTargetData d, int patterns)
        {
            var t = new Target { Data = d, R = d.Renderer, ShapeCount = d.BlendShapeCount };
            var keys = new System.Collections.Generic.List<int>();
            t.ClosePos = new int[patterns]; t.OpenPos = new int[patterns];
            int Pos(int idx)
            {
                // −1 = no shape; indices beyond the mesh would only produce Unity errors (deliberate skip, README).
                if (idx < 0 || idx >= t.ShapeCount) return -1;
                int k = keys.IndexOf(idx);
                if (k < 0) { keys.Add(idx); k = keys.Count - 1; }
                return k;
            }
            for (int p = 0; p < patterns; p++)
            {
                // Targets with fewer entries than target 0 (never in vanilla data; the original indexes blindly) read as "no shape".
                t.ClosePos[p] = Pos(p < d.Close.Length ? d.Close[p] : -1);
                t.OpenPos[p] = Pos(p < d.Open.Length ? d.Open[p] : -1);
            }
            t.Keys = keys.ToArray();
            t.W = new float[t.Keys.Length];
            return t;
        }

        // ------------------------------------------------------------------ pattern state

        /// <summary>The single pattern of the current mix, or −1 for a multi-pattern mix.</summary>
        public int CurrentPattern => nowN == 1 ? nowP[0] : -1;
        /// <summary>The single pattern of the previous mix (faded out during a cross-fade), or −1.</summary>
        public int PreviousPattern => backN == 1 ? backP[0] : -1;
        public bool IsFading => fadeProgress < 1f;
        public float FadeProgress => fadeProgress;

        /// <summary>`ChangePtn`: false when p is outside 0..PatternCount−1 (HS2Next also rejects negative p) or the change was rejected.</summary>
        public bool ChangePtn(int p, bool blend)
        {
            if (p < 0 || p >= PatternCount) return false;
            if (nowN == 1 && nowP[0] == p && nowW[0] == 1f) return true;   // already {p: 1}: an ongoing fade is left alone
            oneP[0] = p; oneW[0] = 1f;
            return ChangeFace(oneP, oneW, 1, blend) == ChangeResult.Ok;
        }

        /// <summary>`ChangeFace(mix, blend)`: validation (rejection changes nothing) and commit.</summary>
        public ChangeResult ChangeFace(int[] ptns, float[] weights, int count, bool blend)
        {
            if (count < 1 || count > nowP.Length) return ChangeResult.BadPattern;
            for (int i = 0; i < count; i++)
                if (ptns[i] < 0 || ptns[i] >= PatternCount) return ChangeResult.BadPattern;
            float sum = 0f;
            for (int ti = 0; ti < targets.Length; ti++)
            {
                var t = targets[ti];
                for (int i = 0; i < count; i++)
                {
                    int p = ptns[i];
                    if (t.R)
                    {
                        int c = p < t.Data.Close.Length ? t.Data.Close[p] : -1, o = p < t.Data.Open.Length ? t.Data.Open[p] : -1;
                        if (c >= t.ShapeCount || o >= t.ShapeCount) return ChangeResult.ShapeOutOfRange;
                    }
                    sum += weights[i];
                }
                if (ti == 0 && sum > 1f) return ChangeResult.WeightSum;   // checked after the first target only
            }
            Array.Copy(nowP, backP, nowN); Array.Copy(nowW, backW, nowN); backN = nowN;
            Array.Copy(ptns, nowP, count); Array.Copy(weights, nowW, count); nowN = count;
            if (blend) StartFade(); else FinishFade();
            return ChangeResult.Ok;
        }

        public void StartFade() { fadeElapsed = 0f; fadeProgress = 0f; }
        public void FinishFade() { fadeElapsed = BlendTime; fadeProgress = 1f; }

        float AdvanceFade(float dt)
        {
            fadeElapsed += dt;
            if (fadeElapsed < BlendTime) fadeProgress = fadeElapsed / BlendTime;
            else FinishFade();
            return fadeProgress;
        }

        // ------------------------------------------------------------------ weights

        /// <summary>`CalculateBlendShape`: one computation; dt advances the cross-fade (0 = hold).</summary>
        public void Calculate(float dt)
        {
            if (targets.Length == 0) return;
            float top = CorrectOpenMax >= 0f ? CorrectOpenMax : OpenMax;
            float value = Mathf.Lerp(OpenMin, top, OpenRate);
            if (FixedRate >= 0f) value = FixedRate;
            int n = (int)(float)(value * 100f);   // float32 product, truncated toward zero
            if (n < 0) n = 0; else if (n > 100) n = 100;
            float b = AdvanceFade(dt);
            LastValue = value; LastN = n; LastBlend = b;
            int cn = 100 - n;
            for (int ti = 0; ti < targets.Length; ti++)
            {
                var t = targets[ti];
                if (!t.R) continue;
                var w = t.W;
                Array.Clear(w, 0, w.Length);
                if (b != 1f)
                {
                    float rb = 1f - b;
                    for (int i = 0; i < backN; i++)
                    {
                        int q = backP[i]; float m = backW[i];
                        int c = t.ClosePos[q], o = t.OpenPos[q];
                        if (c >= 0) w[c] += m * cn * rb;
                        if (o >= 0) w[o] += m * n * rb;
                    }
                }
                for (int i = 0; i < nowN; i++)
                {
                    int q = nowP[i]; float m = nowW[i];
                    int c = t.ClosePos[q], o = t.OpenPos[q];
                    if (c >= 0) w[c] += m * cn * b;
                    if (o >= 0) w[o] += m * n * b;
                }
                var keys = t.Keys;
                for (int k = 0; k < keys.Length; k++) t.R.SetBlendShapeWeight(keys[k], w[k]);
            }
        }
    }
}

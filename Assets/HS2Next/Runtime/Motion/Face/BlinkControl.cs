// Blink generator (`FBSBlinkControl.CalcBlink`), plus the random
// source shared by the expression stage (injectable so tests are deterministic).
//
// idle → closing: sweep BaseSpeed + U(0, 0.05) s, count = 1..3; after the sweep the eye stays shut for
// `count` frames (frame-counted, not timed); opening: a new sweep BaseSpeed + U(0, 0.05) s; then idle
// for 0.2 × RandInt(0, BlinkFrequency) s. The fresh state is idle with blinkTime 0, so the first call
// starts a blink. While FixedFlags ≠ 0 the machine is frozen (openness kept, no transitions).
using UnityEngine;

namespace HS2Next.Motion.Face
{
    /// <summary>Random source of the expression stage (blink draws, mouth-width draws).</summary>
    public interface IFaceRandom
    {
        /// <summary>`RandFloat(a, b)`: uniform in [min, max].</summary>
        float Range(float min, float max);
        /// <summary>`RandInt(a, b)`: uniform in min..max−1; min when max ≤ min.</summary>
        int Range(int min, int maxExclusive);
    }

    /// <summary>Default random source: System.Random with an explicit seed (one per character).</summary>
    public sealed class SystemFaceRandom : IFaceRandom
    {
        readonly System.Random r;
        public SystemFaceRandom(int seed) { r = new System.Random(seed); }
        public float Range(float min, float max) => min + (float)r.NextDouble() * (max - min);
        public int Range(int min, int maxExclusive) => maxExclusive <= min ? min : r.Next(min, maxExclusive);
    }

    public sealed class BlinkControl
    {
        public const int Idle = 0, Closing = 1, Opening = -1;
        /// <summary>Random part added to BaseSpeed for each sweep.</summary>
        public const float SpeedJitter = 0.05f;
        /// <summary>Unit of the idle wait in seconds.</summary>
        public const float WaitUnit = 0.2f;

        /// <summary>Upper bound (exclusive) of the idle wait in units of 0.2 s (byte, vanilla 30).</summary>
        public byte BlinkFrequency;
        /// <summary>Base duration of one close or open sweep in seconds (vanilla 0.15).</summary>
        public float BaseSpeed;
        /// <summary>0 = blinking allowed; HS2 uses 0 and 1.</summary>
        public byte FixedFlags;

        public int Mode { get; private set; } = Idle;
        /// <summary>Duration of the current sweep (s).</summary>
        public float CalcSpeed { get; private set; }
        /// <summary>Absolute time at which the current phase ends.</summary>
        public float BlinkTime { get; private set; }
        /// <summary>Remaining shut frames of the current blink.</summary>
        public int Count { get; private set; }
        /// <summary>Current openness 0 (shut) .. 1 (open); initial 1.</summary>
        public float OpenRate { get; private set; } = 1f;

        IFaceRandom rng;
        public IFaceRandom Random { get => rng; set => rng = value ?? rng; }

        public BlinkControl(byte frequency, float baseSpeed, IFaceRandom random)
        {
            BlinkFrequency = frequency; BaseSpeed = baseSpeed; rng = random;
        }

        /// <summary>One call per frame with the current scaled time `now`.</summary>
        public void Calc(float now)
        {
            float remaining = Mathf.Max(0f, BlinkTime - now);
            float cand;
            if (Mode == Idle) cand = 1f;
            else if (CalcSpeed <= 0f) cand = Mode == Closing ? 0f : 1f;   // guard: BaseSpeed 0 with a zero draw
            else if (Mode == Closing) cand = Mathf.Clamp01(remaining / CalcSpeed);
            else cand = Mathf.Clamp01(1f - remaining / CalcSpeed);
            if (FixedFlags == 0) OpenRate = cand;
            if (FixedFlags != 0 || now <= BlinkTime) return;
            switch (Mode)
            {
                case Idle: StartClosing(now); break;
                case Closing:
                    Count--;
                    if (Count <= 0) StartOpening(now);
                    break;
                default: StartIdle(now); break;
            }
        }

        void StartClosing(float now)
        {
            CalcSpeed = BaseSpeed + rng.Range(0f, SpeedJitter);
            BlinkTime = now + CalcSpeed;
            Count = rng.Range(0, 3) + 1;
            Mode = Closing;
        }

        void StartOpening(float now)
        {
            CalcSpeed = BaseSpeed + rng.Range(0f, SpeedJitter);
            BlinkTime = now + CalcSpeed;
            Mode = Opening;
        }

        void StartIdle(float now)
        {
            int r = rng.Range(0, BlinkFrequency);
            BlinkTime = now + WaitUnit * r;
            Mode = Idle;
        }

        /// <summary>HS2Next reset (no game counterpart): eyes open, idle with a freshly drawn wait from `now`.</summary>
        public void ResetIdle(float now)
        {
            OpenRate = 1f;
            Count = 0;
            StartIdle(now);
        }

        /// <summary>Back to the fresh state (idle, blinkTime 0, open): the next Calc starts a blink.</summary>
        public void ResetFresh()
        {
            Mode = Idle; BlinkTime = 0f; CalcSpeed = 0f; Count = 0; OpenRate = 1f;
        }
    }
}

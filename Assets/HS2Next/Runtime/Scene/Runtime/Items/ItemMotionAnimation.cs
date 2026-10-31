// Item animation: the first Animator of the item's active hierarchy, driven
// only when the root ItemComponent has isAnime and the Animator has a controller, played through AnimationDriver, the
// Playable engine with manual evaluation that the characters use, so pause, speed and "set time"
// are explicit and edit mode steps exactly what play mode steps.
//
//   build    pattern p != 0 with a valid animeInfos[p] -> Play(state, layer 0, 0); else the controller's default state;
//            Evaluate(0); a stored time t != 0 -> Play(current layer-0 state, 0, t), Evaluate(0) (HS2 steps one second
//            before replaying the state at t: not reproduced, T14)
//   step     Evaluate(frame.Dt x anime.speed) (frame.Dt carries the item motion's TimeScale / Paused / MaxStep)
//   pattern  as HS2's SetAnimePattern: a valid entry plays its state with the engine default time (another state starts
//            from 0, the playing state continues); an invalid index changes nothing but the stored value
//   time     Play(current layer-0 state, 0, t) (a pattern changed in the same pass is that state)
//   restart  Play(current layer-0 state, 0, 0) (not a model change)
// Advanced: the layer-0 time moved away from the stored item.anime.time (a step with dt > 0, a pattern change, a
// restart); SaveCapture writes the time only then, so save -> load -> save stays byte-identical without stepping.
using System;
using System.Collections.Generic;
using HS2Next.Motion;
using HS2Next.Motion.Animation;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class ItemAnimationStage : IMotionStage, IDisposable
    {
        public MotionPhase Phase => MotionPhase.Animation;
        public string Name => "anim";
        public bool Enabled { get; set; } = true;

        public readonly Animator Animator;
        public readonly AnimationDriver Driver;
        /// <summary>ItemComponent.animeInfos: (name, state, valid) per index; empty when the component has none.</summary>
        public readonly IReadOnlyList<(string name, string state, bool valid)> Patterns;

        /// <summary>item.anime.speed as applied (multiplies the step's dt).</summary>
        public float Speed = 1f;
        /// <summary>item.anime.pattern as applied.</summary>
        public int Pattern { get; private set; }
        /// <summary>The layer-0 time moved since the model's anime.time was applied or captured.</summary>
        public bool Advanced { get; private set; }
        /// <summary>Evaluations with dt &gt; 0 so far (diagnostics).</summary>
        public int Evaluations { get; private set; }

        int pendingState;   // state hash queued by a pattern change and not yet evaluated

        public ItemAnimationStage(Animator animator, RuntimeAnimatorController controller, IReadOnlyList<(string, string, bool)> patterns, string graphName)
        {
            Animator = animator;
            Patterns = patterns ?? Array.Empty<(string, string, bool)>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.speed = 1f;   // the speed is applied to dt
            Driver = AnimationDriver.Create(AnimationEngine.Playable, animator, controller, graphName);
        }

        public bool Valid => Driver != null && Driver.Valid;

        /// <summary>The state hash of a valid pattern index, else 0.</summary>
        public int StateOf(int pattern)
        {
            if (pattern < 0 || pattern >= Patterns.Count || !Patterns[pattern].valid) return 0;
            return Resolve(Patterns[pattern].state);
        }

        int Resolve(string state)
        {
            if (string.IsNullOrEmpty(state) || !Valid) return 0;
            int h = Animator.StringToHash(state);
            if (Driver.HasState(0, h)) return h;
            return 0;
        }

        // ------------------------------------------------------------ build and changes

        /// <summary>The build: pattern, speed, then the stored time; the pose is evaluated once (dt 0).</summary>
        public void Build(int pattern, float speed, float time)
        {
            Speed = speed;
            Pattern = pattern;
            if (!Valid) return;
            int h = pattern != 0 ? StateOf(pattern) : 0;
            if (h != 0) Driver.Play(h, 0, 0f);
            Driver.Evaluate(0f);
            if (time != 0f && Driver.LayerCount > 0)
            {
                Driver.Play(Driver.State(0).fullPathHash, 0, time);
                Driver.Evaluate(0f);
            }
            pendingState = 0;
            Advanced = false;
        }

        /// <summary>item.anime.pattern changed (HS2's SetAnimePattern).</summary>
        public void SetPattern(int pattern)
        {
            Pattern = pattern;
            int h = StateOf(pattern);
            if (h == 0) return;
            Driver.Play(h, 0, float.NegativeInfinity);
            pendingState = h;
            Advanced = true;
        }

        /// <summary>item.anime.time changed: the current state (or the one a pattern change just queued) at <paramref name="t"/>.</summary>
        public void SetTime(float t)
        {
            if (!Valid || Driver.LayerCount == 0) return;
            Driver.Play(pendingState != 0 ? pendingState : Driver.State(0).fullPathHash, 0, t);
            pendingState = 0;
            Advanced = false;
        }

        /// <summary>The animation panel's restart: the current state from 0 (not a model change; captured later).</summary>
        public void Restart()
        {
            if (!Valid || Driver.LayerCount == 0) return;
            Driver.Play(pendingState != 0 ? pendingState : Driver.State(0).fullPathHash, 0, 0f);
            pendingState = 0;
            Driver.Evaluate(0f);
            Advanced = true;
        }

        /// <summary>Consumes queued plays with dt 0 (the pose and the state info are current afterwards).</summary>
        public void EvaluateNow()
        {
            if (!Valid) return;
            Driver.Evaluate(0f);
            pendingState = 0;
        }

        /// <summary>After a capture wrote the time into the model.</summary>
        public void MarkApplied() => Advanced = false;

        // ------------------------------------------------------------ queries

        /// <summary>Layer-0 normalized time (unwrapped), NaN without a valid driver.</summary>
        public float NormalizedTime => Valid && Driver.LayerCount > 0 ? Driver.State(0).normalizedTime : float.NaN;

        public AnimatorStateInfo StateInfo => Valid && Driver.LayerCount > 0 ? Driver.State(0) : default;

        /// <summary>The animeInfos state name of the playing layer-0 state (short or full path hash), else null.</summary>
        public string StateName
        {
            get
            {
                if (!Valid || Driver.LayerCount == 0) return null;
                var s = Driver.State(0);
                foreach (var p in Patterns)
                {
                    if (string.IsNullOrEmpty(p.state)) continue;
                    int h = Animator.StringToHash(p.state);
                    if (h == s.shortNameHash || h == s.fullPathHash) return p.state;
                }
                return null;
            }
        }

        // ------------------------------------------------------------ IMotionStage

        public void Step(in MotionFrame frame)
        {
            if (!Valid) return;
            float dt = frame.Dt * Mathf.Max(Speed, 0f);
            Driver.Evaluate(dt);
            pendingState = 0;
            if (dt > 0f) { Advanced = true; Evaluations++; }
        }

        /// <summary>Animation has no inertia.</summary>
        public void Reset() { }

        public void Dispose() => Driver?.Dispose();
    }
}

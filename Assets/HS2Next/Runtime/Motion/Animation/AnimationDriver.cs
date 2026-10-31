// Playback engines for one Animator (decision 1).
//
// The 2018.4 RuntimeAnimatorControllers of the game load and play in Unity 6 (Spike/out/motion/anim_spike.txt).
// Two engines drive them by hand, so that every evaluation happens inside the character's CharaMotion step
// (stage A, MotionPhase.Animation) with an explicit delta, in edit mode and in play mode alike:
//   - Playable (default): a PlayableGraph per Animator, AnimatorControllerPlayable -> AnimationPlayableOutput,
//     DirectorUpdateMode.Manual, graph.Evaluate(dt). The Animator component must be ENABLED for the output to
//     write transforms (measured: a disabled Animator gets no pose); its own controller slot stays empty, so
//     Unity never advances it by itself.
//   - Animator (fallback): the controller assigned to the Animator, the component DISABLED (so Unity's update
//     never runs it), advanced by Animator.Update(dt). Works headless and in play mode (measured).
// In both engines a queued Play(state, layer, t) is consumed by the next evaluation, also with dt = 0 (the
// "set time while paused" re-pose / deviation (b)); the state info describes the previous state
// until then. The caller passes dt already multiplied by the character's speed (Animator.speed stays 1).
using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace HS2Next.Motion.Animation
{
    /// <summary>Which engine plays the controllers.</summary>
    public enum AnimationEngine
    {
        /// <summary>PlayableGraph with manual evaluation (default).</summary>
        Playable,
        /// <summary>Fallback: the Animator's own controller slot, component disabled, Animator.Update(dt).</summary>
        Animator,
    }

    /// <summary>One controller playing on one Animator. Not thread-safe; main thread only.</summary>
    public abstract class AnimationDriver : IDisposable
    {
        public Animator Animator { get; }
        public RuntimeAnimatorController Controller { get; }
        /// <summary>Parameters of the controller, read once at creation.</summary>
        public AnimatorControllerParameter[] Parameters { get; protected set; } = Array.Empty<AnimatorControllerParameter>();

        protected AnimationDriver(Animator animator, RuntimeAnimatorController controller) { Animator = animator; Controller = controller; }

        public static AnimationDriver Create(AnimationEngine engine, Animator animator, RuntimeAnimatorController controller, string name)
        {
            if (!animator || !controller) return null;
            return engine == AnimationEngine.Animator ? new AnimatorEngineDriver(animator, controller) : (AnimationDriver)new PlayableEngineDriver(animator, controller, name);
        }

        public abstract bool Valid { get; }
        public abstract int LayerCount { get; }
        /// <summary>Queue a jump to a state (consumed by the next <see cref="Evaluate"/>). layer -1 = first layer that has the state; t = NegativeInfinity = engine default time.</summary>
        public abstract void Play(int stateHash, int layer, float normalizedTime);
        /// <summary>Advance by dt (seconds, speed already applied) and write the pose.</summary>
        public abstract void Evaluate(float dt);
        public abstract AnimatorStateInfo State(int layer);
        public abstract bool HasState(int layer, int stateHash);
        public abstract float GetLayerWeight(int layer);
        public abstract void SetLayerWeight(int layer, float w);
        public abstract void SetFloat(int id, float v);
        public abstract float GetFloat(int id);
        public abstract int GetInteger(int id);
        public abstract bool GetBool(int id);
        /// <summary>The layer's name in the controller ("" when not loaded).</summary>
        public abstract string LayerName(int layer);
        /// <summary>The clips the layer's current state plays, with their weights (a new array; for reports).</summary>
        public abstract AnimatorClipInfo[] ClipInfo(int layer);
        public abstract void Dispose();

        /// <summary>True if a parameter with exactly this name (and type, when given) exists.</summary>
        public bool HasParameter(string name, AnimatorControllerParameterType? type = null)
        {
            foreach (var p in Parameters) if (p.name == name && (type == null || p.type == type.Value)) return true;
            return false;
        }

        /// <summary>The first name of <paramref name="names"/> that exists as a parameter (exact, case-sensitive), or "".</summary>
        public string FirstParameter(params string[] names)
        {
            foreach (var n in names) if (HasParameter(n)) return n;
            return "";
        }

        public void SetFloat(string name, float v) { if (!string.IsNullOrEmpty(name)) SetFloat(Animator.StringToHash(name), v); }
    }

    sealed class PlayableEngineDriver : AnimationDriver
    {
        PlayableGraph graph;
        AnimatorControllerPlayable playable;

        public PlayableEngineDriver(Animator animator, RuntimeAnimatorController controller, string name) : base(animator, controller)
        {
            animator.runtimeAnimatorController = null;   // nothing for Unity to update on its own
            animator.enabled = true;                     // required for the playable output to write
            graph = PlayableGraph.Create(name);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            playable = AnimatorControllerPlayable.Create(graph, controller);
            var output = AnimationPlayableOutput.Create(graph, "body", animator);
            output.SetSourcePlayable(playable);
            int n = playable.GetParameterCount();
            var ps = new AnimatorControllerParameter[n];
            for (int i = 0; i < n; i++) ps[i] = playable.GetParameter(i);
            Parameters = ps;
        }

        public override bool Valid => graph.IsValid();
        public override int LayerCount => Valid ? playable.GetLayerCount() : 0;
        public override void Play(int stateHash, int layer, float t) { if (Valid) playable.Play(stateHash, layer, t); }
        public override void Evaluate(float dt) { if (Valid) graph.Evaluate(dt); }
        public override AnimatorStateInfo State(int layer) => Valid ? playable.GetCurrentAnimatorStateInfo(layer) : default;
        public override bool HasState(int layer, int stateHash) => Valid && playable.HasState(layer, stateHash);
        public override float GetLayerWeight(int layer) => Valid ? playable.GetLayerWeight(layer) : 0f;
        public override void SetLayerWeight(int layer, float w) { if (Valid) playable.SetLayerWeight(layer, w); }
        public override void SetFloat(int id, float v) { if (Valid) playable.SetFloat(id, v); }
        public override float GetFloat(int id) => Valid ? playable.GetFloat(id) : 0f;
        public override int GetInteger(int id) => Valid ? playable.GetInteger(id) : 0;
        public override bool GetBool(int id) => Valid && playable.GetBool(id);
        public override string LayerName(int layer) => Valid ? playable.GetLayerName(layer) : "";
        public override AnimatorClipInfo[] ClipInfo(int layer) => Valid ? playable.GetCurrentAnimatorClipInfo(layer) : Array.Empty<AnimatorClipInfo>();
        public override void Dispose() { if (graph.IsValid()) graph.Destroy(); }
    }

    sealed class AnimatorEngineDriver : AnimationDriver
    {
        public AnimatorEngineDriver(Animator animator, RuntimeAnimatorController controller) : base(animator, controller)
        {
            animator.enabled = false;   // Unity must not advance it; Update(dt) still evaluates (measured)
            animator.speed = 1f;
            animator.runtimeAnimatorController = controller;   // rebinds
            Parameters = animator.parameters;
        }

        bool Ok => Animator && Animator.runtimeAnimatorController == Controller;
        public override bool Valid => Ok;
        public override int LayerCount => Ok ? Animator.layerCount : 0;
        public override void Play(int stateHash, int layer, float t) { if (Ok) Animator.Play(stateHash, layer, t); }
        public override void Evaluate(float dt) { if (Ok) Animator.Update(dt); }
        public override AnimatorStateInfo State(int layer) => Ok ? Animator.GetCurrentAnimatorStateInfo(layer) : default;
        public override bool HasState(int layer, int stateHash) => Ok && Animator.HasState(layer, stateHash);
        public override float GetLayerWeight(int layer) => Ok ? Animator.GetLayerWeight(layer) : 0f;
        public override void SetLayerWeight(int layer, float w) { if (Ok) Animator.SetLayerWeight(layer, w); }
        public override void SetFloat(int id, float v) { if (Ok) Animator.SetFloat(id, v); }
        public override float GetFloat(int id) => Ok ? Animator.GetFloat(id) : 0f;
        public override int GetInteger(int id) => Ok ? Animator.GetInteger(id) : 0;
        public override bool GetBool(int id) => Ok && Animator.GetBool(id);
        public override string LayerName(int layer) => Ok ? Animator.GetLayerName(layer) : "";
        public override AnimatorClipInfo[] ClipInfo(int layer) => Ok ? Animator.GetCurrentAnimatorClipInfo(layer) : Array.Empty<AnimatorClipInfo>();
        public override void Dispose() { if (Ok) Animator.runtimeAnimatorController = null; }
    }
}

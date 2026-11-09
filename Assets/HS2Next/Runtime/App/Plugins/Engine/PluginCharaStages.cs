// The character stages of one plugin (the plugin's view of CharaStageHost): the plugin's stage runs behind an adapter that the
// character's CharaMotion sees as the stage, so every call into the plugin's code goes through its fault wrapper. A throw is counted as
// a fault of the plugin and the stage stays, until the count reaches its threshold. Phase and Name are read once, after the factory:
// the stage list sorts on them and names them in logs. The phase range is this view's rule; the host's own stages reach CharaStageHost
// without it. The adapter skips Step while the character is being probed, since a stage has no way to take a write off inside a probe.
// When the plugin unloads, is disabled or faults every entry of it ends and this view refuses further stages. Main thread only.
using System;
using System.Collections.Generic;
using HS2Next.Api.Unity;
using HS2Next.Motion;
using HS2Next.Scenes.Runtime;
using UnityEngine;

namespace HS2Next.App.Plugins.Engine
{
    /// <summary>One plugin's view of the character stages.</summary>
    public sealed class PluginCharaStages : ICharaStages
    {
        /// <summary>The lowest phase of a plugin's stage: after the host's restores.</summary>
        public const int MinPhase = -90;
        /// <summary>The highest phase of a plugin's stage: before the host's end stages.</summary>
        public const int MaxPhase = 940;

        readonly PluginGuard guard;
        readonly Func<SceneRuntime> runtime;
        readonly HashSet<CharaStageHost> hosts = new HashSet<CharaStageHost>();
        bool closed;

        /// <param name="guard">The plugin's fault wrapper.</param>
        /// <param name="runtime">The current scene runtime, or null (then no node is a character).</param>
        public PluginCharaStages(PluginGuard guard, Func<SceneRuntime> runtime)
        {
            this.guard = guard ?? throw new ArgumentNullException(nameof(guard));
            this.runtime = runtime ?? (() => null);
        }

        public IDisposable Add(string nodeId, Func<GameObject, IMotionStage> make)
        {
            if (closed) throw new ObjectDisposedException("the plugin " + guard.Guid + " is unloaded");
            if (make == null) throw new ArgumentNullException(nameof(make));
            var rt = runtime();
            if (rt == null) throw new ArgumentException("not a character: " + (nodeId ?? "(null)"), nameof(nodeId));
            var host = CharaStageHost.For(rt);
            var entry = host.Add(nodeId, guard.Guid, r => Make(host, r, make));
            hosts.Add(host);
            return entry;
        }

        /// <summary>The plugin leaves: its stages go, later Add calls throw. Idempotent.</summary>
        public void Close()
        {
            closed = true;
            foreach (var h in hosts) h.RemoveOwner(guard.Guid);
            hosts.Clear();
        }

        IMotionStage Make(CharaStageHost host, CharaRealization r, Func<GameObject, IMotionStage> make)
        {
            if (closed || guard.Faulted) return null;
            var stage = guard.Run("stage factory", () => make(r.Root), null);
            if (stage == null) return null;
            var (phase, name, ok) = guard.Run("stage factory", () => ((int)stage.Phase, stage.Name, true), (0, null, false));
            if (!ok) return null;
            string shown = guard.Guid + "/" + (string.IsNullOrEmpty(name) ? "stage" : name);
            if (phase < MinPhase || phase > MaxPhase)
            {
                guard.Count("stage " + shown, new ArgumentOutOfRangeException(nameof(IMotionStage.Phase), phase, "a stage of a plugin runs at a phase from " + MinPhase + " to " + MaxPhase));
                return null;
            }
            return new PluginStageAdapter(guard, host, stage, shown, (MotionPhase)phase, r.Motion);
        }
    }

    /// <summary>A plugin's stage as a character's motion sees it.</summary>
    public sealed class PluginStageAdapter : IMotionStage, IDisposable
    {
        readonly PluginGuard guard;
        readonly CharaStageHost host;
        readonly IMotionStage stage;
        readonly CharaMotion motion;
        readonly string hook;
        bool gone;

        /// <param name="guard">The plugin's fault wrapper.</param>
        /// <param name="host">The host that keeps the stage.</param>
        /// <param name="stage">The plugin's stage.</param>
        /// <param name="name">The name in the character's stage list.</param>
        /// <param name="phase">The stage's phase, as read after its factory.</param>
        /// <param name="motion">The character's motion.</param>
        public PluginStageAdapter(PluginGuard guard, CharaStageHost host, IMotionStage stage, string name, MotionPhase phase, CharaMotion motion)
        {
            this.guard = guard;
            this.host = host;
            this.stage = stage;
            this.motion = motion;
            Name = name;
            Phase = phase;
            hook = "stage " + name;
        }

        /// <summary>The plugin's guid.</summary>
        public string Owner => guard.Guid;

        public MotionPhase Phase { get; }

        public string Name { get; }

        public bool Enabled
        {
            get
            {
                if (gone) return false;
                Enter();
                try { return stage.Enabled; }
                catch (Exception e) { guard.Count(hook, e); return false; }
                finally { Exit(); }
            }
            set
            {
                if (gone) return;
                Enter();
                try { stage.Enabled = value; }
                catch (Exception e) { guard.Count(hook, e); }
                finally { Exit(); }
            }
        }

        public void Step(in MotionFrame frame)
        {
            if (gone || motion.Probing) return;
            Enter();
            try { stage.Step(in frame); }
            catch (Exception e) { guard.Count(hook, e); }
            finally { Exit(); }
        }

        public void Reset()
        {
            if (gone) return;
            Enter();
            try { stage.Reset(); }
            catch (Exception e) { guard.Count(hook, e); }
            finally { Exit(); }
        }

        /// <summary>The stage left the character: nothing calls the plugin's stage from here on.</summary>
        public void Dispose() => gone = true;

        void Enter()
        {
            host.BeginCall();
            guard.Enter();
        }

        void Exit()
        {
            guard.Exit();
            host.EndCall();
        }
    }
}

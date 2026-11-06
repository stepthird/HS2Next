// The scene post stage of the plugin host (Unity-free): the loaded plugins that implement IPluginScenePost, called in load
// order once per frame and once per scene step, after all motion. Every call runs inside the plugin's fault wrapper under
// the hook name "scenePost" and is timed; a call allocates nothing. The host rebuilds the list whenever a plugin loads or
// leaves; a list replaced during a pass takes effect at the next pass, and a caller closed during a pass is not called. A
// pass started from inside a pass (a plugin's ScenePost that steps the scene) returns at once.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using HS2Next.Api.Plugins;

namespace HS2Next.App.Plugins
{
    /// <summary>One plugin's place in the scene post stage: its handler, fault wrapper and time window.</summary>
    public sealed class ScenePostCaller
    {
        /// <summary>The hook name of the fault counts.</summary>
        public const string Hook = "scenePost";

        readonly PluginGuard guard;
        readonly PluginTimer time;
        readonly Action call;
        PluginFrame frame;
        bool closed;

        public ScenePostCaller(IPluginScenePost handler, PluginGuard guard, PluginTimer time)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            this.guard = guard ?? throw new ArgumentNullException(nameof(guard));
            this.time = time ?? throw new ArgumentNullException(nameof(time));
            call = () => handler.ScenePost(in frame);
        }

        /// <summary>The plugin left: it is not called again, also not by a pass that is running.</summary>
        public void Close() => closed = true;

        internal void Run(in PluginFrame f)
        {
            if (closed || guard.Faulted) return;
            frame = f;
            long t0 = Stopwatch.GetTimestamp();
            guard.Run(Hook, call);
            time.Add((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
        }
    }

    /// <summary>The callers of the scene post stage in load order.</summary>
    public sealed class ScenePostStage
    {
        ScenePostCaller[] callers = Array.Empty<ScenePostCaller>();
        bool running;

        /// <summary>At least one plugin takes part: the host hands the runtime its stage only then.</summary>
        public bool Any => callers.Length > 0;

        /// <summary>Plugins that take part.</summary>
        public int Count => callers.Length;

        /// <summary>Replaces the callers (in load order).</summary>
        public void Set(IEnumerable<ScenePostCaller> ordered) => callers = new List<ScenePostCaller>(ordered).ToArray();

        /// <summary>One pass: every caller in order, whatever the others do.</summary>
        public void Run(in PluginFrame frame)
        {
            if (running) return;
            running = true;
            try
            {
                var list = callers;
                for (int i = 0; i < list.Length; i++) list[i].Run(in frame);
            }
            finally { running = false; }
        }
    }
}

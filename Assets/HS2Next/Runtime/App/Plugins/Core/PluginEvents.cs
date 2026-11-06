// One plugin's IAppEvents (Unity-free). The host raises each app event on every loaded plugin's instance in load order;
// each handler runs inside the plugin's fault wrapper, so a throwing handler is counted and the next one still runs.
// A handler is never re-entered for the same plugin: an event raised while one of its handlers runs (e.g. a handler's
// in-process call that changes the scene) is queued and delivered right after the running delivery returns. After the
// plugin unloads or faults, Clear drops its delegates and later events are ignored. Main thread only.
using System;
using System.Collections.Generic;
using HS2Next.Api.Plugins;

namespace HS2Next.App.Plugins
{
    public enum PluginEventKind
    {
        AppReady, SceneCleared, SceneLoaded, SceneSaving, SceneSaved, NodesAdded, NodesRemoved, NodesMoved, NodeForgotten, CharaBuilt,
        PausedChanged, SceneStepped, Quitting, CaptureStarting, CaptureRendered, CaptureFinished,
    }

    public sealed class PluginEvents : IAppEvents
    {
        readonly PluginGuard guard;
        readonly Queue<KeyValuePair<PluginEventKind, object>> pending = new Queue<KeyValuePair<PluginEventKind, object>>();
        bool delivering, closed;

        public PluginEvents(PluginGuard guard) { this.guard = guard; }

        public event Action AppReady;
        public event Action SceneCleared;
        public event Action<SceneLoadInfo> SceneLoaded;
        public event Action SceneSaving;
        public event Action<SceneSaveResult> SceneSaved;
        public event Action<NodeChangeInfo> NodesAdded;
        public event Action<NodeChangeInfo> NodesRemoved;
        public event Action<NodeChangeInfo> NodesMoved;
        public event Action<string> NodeForgotten;
        public event Action<CharaBuiltInfo> CharaBuilt;
        public event Action<bool> PausedChanged;
        public event Action<StepInfo> SceneStepped;
        public event Action Quitting;
        public event Action<CaptureInfo> CaptureStarting;
        public event Action<CaptureInfo> CaptureRendered;
        public event Action<CaptureResultInfo> CaptureFinished;

        /// <summary>Events delivered (handlers called) so far.</summary>
        public int Delivered { get; private set; }
        /// <summary>Events queued because a handler of this plugin was running.</summary>
        public int Deferred { get; private set; }
        public bool IsDelivering => delivering;
        public bool IsClosed => closed;

        /// <summary>The number of subscribed handlers over all events.</summary>
        public int HandlerCount =>
            Len(AppReady) + Len(SceneCleared) + Len(SceneLoaded) + Len(SceneSaving) + Len(SceneSaved) + Len(NodesAdded) + Len(NodesRemoved) +
            Len(NodesMoved) + Len(NodeForgotten) + Len(CharaBuilt) + Len(PausedChanged) + Len(SceneStepped) + Len(Quitting) +
            Len(CaptureStarting) + Len(CaptureRendered) + Len(CaptureFinished);

        static int Len(Delegate d) => d?.GetInvocationList().Length ?? 0;

        /// <summary>Drops every handler; later events are ignored.</summary>
        public void Clear()
        {
            closed = true;
            pending.Clear();
            AppReady = null; SceneCleared = null; SceneLoaded = null; SceneSaving = null; SceneSaved = null; NodesAdded = null; NodesRemoved = null;
            NodesMoved = null; NodeForgotten = null; CharaBuilt = null; PausedChanged = null; SceneStepped = null; Quitting = null;
            CaptureStarting = null; CaptureRendered = null; CaptureFinished = null;
        }

        /// <summary>Delivers one event to this plugin's handlers (queued when one of them is running).</summary>
        public void Raise(PluginEventKind kind, object arg = null)
        {
            if (closed) return;
            if (delivering)
            {
                pending.Enqueue(new KeyValuePair<PluginEventKind, object>(kind, arg));
                Deferred++;
                return;
            }
            delivering = true;
            try
            {
                Deliver(kind, arg);
                while (pending.Count > 0 && !closed)
                {
                    var next = pending.Dequeue();
                    Deliver(next.Key, next.Value);
                }
            }
            finally { delivering = false; }
        }

        void Deliver(PluginEventKind kind, object arg)
        {
            Delegate d = kind switch
            {
                PluginEventKind.AppReady => AppReady,
                PluginEventKind.SceneCleared => SceneCleared,
                PluginEventKind.SceneLoaded => SceneLoaded,
                PluginEventKind.SceneSaving => SceneSaving,
                PluginEventKind.SceneSaved => SceneSaved,
                PluginEventKind.NodesAdded => NodesAdded,
                PluginEventKind.NodesRemoved => NodesRemoved,
                PluginEventKind.NodesMoved => NodesMoved,
                PluginEventKind.NodeForgotten => NodeForgotten,
                PluginEventKind.CharaBuilt => CharaBuilt,
                PluginEventKind.PausedChanged => PausedChanged,
                PluginEventKind.SceneStepped => SceneStepped,
                PluginEventKind.Quitting => Quitting,
                PluginEventKind.CaptureStarting => CaptureStarting,
                PluginEventKind.CaptureRendered => CaptureRendered,
                PluginEventKind.CaptureFinished => CaptureFinished,
                _ => null,
            };
            if (d == null) return;
            string hook = "event " + kind;
            foreach (var h in d.GetInvocationList())
            {
                if (closed) return;
                Delivered++;
                guard.Run(hook, () =>
                {
                    switch (h)
                    {
                        case Action a: a(); break;
                        case Action<SceneLoadInfo> a: a((SceneLoadInfo)arg); break;
                        case Action<SceneSaveResult> a: a((SceneSaveResult)arg); break;
                        case Action<NodeChangeInfo> a: a((NodeChangeInfo)arg); break;
                        case Action<string> a: a((string)arg); break;
                        case Action<CharaBuiltInfo> a: a((CharaBuiltInfo)arg); break;
                        case Action<bool> a: a(arg is bool b && b); break;
                        case Action<StepInfo> a: a(arg is StepInfo s ? s : default); break;
                        case Action<CaptureInfo> a: a((CaptureInfo)arg); break;
                        case Action<CaptureResultInfo> a: a((CaptureResultInfo)arg); break;
                    }
                });
            }
        }
    }
}

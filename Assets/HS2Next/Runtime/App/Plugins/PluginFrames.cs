// A plugin's view of the bridge's frame clock (IPluginContext.Frames): the bridge's own clock, except that the actions the
// plugin schedules (AtUpdate, AtLate) run only while this context's plugin instance is loaded: once it is unloaded, faulted
// or disabled they are dropped, not run (also after a runtime enable, which makes a new instance with a new context). The
// handlers it adds to FrameStarted / LateFrame are removed with its other registrations. While a scheduled action runs, the
// plugin's code counts as on the stack (its fault guard's depth). Waits are the bridge's own tasks.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HS2Next.Api.Bridge;

namespace HS2Next.App.Plugins
{
    public sealed class PluginFrames : IBridgeFrames
    {
        readonly IBridgeFrames inner;
        readonly Func<bool> alive;
        readonly Func<PluginGuard> guard;
        readonly object gate = new object();
        readonly List<KeyValuePair<Action<int>, Action<int>>> started = new List<KeyValuePair<Action<int>, Action<int>>>();
        readonly List<KeyValuePair<Action<int>, Action<int>>> late = new List<KeyValuePair<Action<int>, Action<int>>>();

        /// <param name="alive">True while the plugin instance that owns this view is loaded.</param>
        public PluginFrames(IBridgeFrames inner, Func<bool> alive, Func<PluginGuard> guard = null)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.alive = alive ?? (() => true);
            this.guard = guard ?? (() => null);
        }

        /// <summary>Actions dropped because the plugin was no longer loaded when they were due.</summary>
        public int Dropped { get; private set; }

        public int Frame => inner.Frame;
        public double Realtime => inner.Realtime;

        public void AtUpdate(int afterFrames, Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            inner.AtUpdate(afterFrames, Owned(action));
        }

        public void AtLate(int afterFrames, Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            inner.AtLate(afterFrames, Owned(action));
        }

        Action Owned(Action action) => () =>
        {
            if (!alive())
            {
                lock (gate) Dropped++;
                return;
            }
            var g = guard();
            g?.Enter();
            try { action(); }
            finally { g?.Exit(); }
        };

        public Task WaitFrames(int frames) => inner.WaitFrames(frames);
        public Task WaitLate(int afterFrames = 0) => inner.WaitLate(afterFrames);

        public event Action<int> FrameStarted
        {
            add => Add(started, value, w => inner.FrameStarted += w);
            remove => Remove(started, value, w => inner.FrameStarted -= w);
        }

        public event Action<int> LateFrame
        {
            add => Add(late, value, w => inner.LateFrame += w);
            remove => Remove(late, value, w => inner.LateFrame -= w);
        }

        void Add(List<KeyValuePair<Action<int>, Action<int>>> list, Action<int> h, Action<Action<int>> subscribe)
        {
            if (h == null) return;
            Action<int> w = f => { if (alive()) h(f); };
            lock (gate) list.Add(new KeyValuePair<Action<int>, Action<int>>(h, w));
            subscribe(w);
        }

        void Remove(List<KeyValuePair<Action<int>, Action<int>>> list, Action<int> h, Action<Action<int>> unsubscribe)
        {
            if (h == null) return;
            Action<int> w = null;
            lock (gate)
                for (int i = list.Count - 1; i >= 0; i--)
                    if (list[i].Key == h)
                    {
                        w = list[i].Value;
                        list.RemoveAt(i);
                        break;
                    }
            if (w != null) unsubscribe(w);
        }

        /// <summary>The handlers the plugin added to the clock's events (both events).</summary>
        public int HandlerCount { get { lock (gate) return started.Count + late.Count; } }

        /// <summary>Removes every handler the plugin added (the host's unload, fault and disable).</summary>
        public void Clear()
        {
            List<Action<int>> s, l;
            lock (gate)
            {
                s = started.ConvertAll(kv => kv.Value);
                l = late.ConvertAll(kv => kv.Value);
                started.Clear();
                late.Clear();
            }
            foreach (var w in s) inner.FrameStarted -= w;
            foreach (var w in l) inner.LateFrame -= w;
        }
    }
}

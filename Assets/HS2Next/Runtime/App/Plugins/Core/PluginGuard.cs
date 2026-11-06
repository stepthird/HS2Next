// The fault wrapper of one plugin (Unity-free): every call into plugin code goes through it. An exception is logged once
// per (hook, exception type) with its stack, counted per hook, and never stops the caller. At the threshold (10 counted
// faults) the plugin becomes faulted once: the host then stops its updates and events, removes its other
// registrations and tries its Unload once; its commands stay and answer plugin_fault. Thread-safe (a command or a
// deferred continuation may fault off the main thread); the faulted notification is raised on the thread of the fault
// that reached the threshold, so the host marshals it to the main thread. The guard also counts how deep the plugin's
// code is on the stack (every call it wraps, and the host's Enter / Exit around calls it does not wrap), so a runtime
// disable can refuse while the plugin's code runs.
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace HS2Next.App.Plugins
{
    public sealed class PluginGuard
    {
        public const int DefaultThreshold = 10;

        readonly object gate = new object();
        readonly Dictionary<string, int> byHook = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly HashSet<string> logged = new HashSet<string>(StringComparer.Ordinal);
        readonly Action<string> logWarning;
        int total;
        bool faulted;
        int depth;

        public PluginGuard(string guid, Action<string> logWarning, int threshold = DefaultThreshold)
        {
            Guid = guid;
            this.logWarning = logWarning;
            Threshold = threshold;
        }

        public string Guid { get; }
        public int Threshold { get; }
        /// <summary>Counted faults.</summary>
        public int Total { get { lock (gate) return total; } }
        public bool Faulted { get { lock (gate) return faulted; } }
        /// <summary>"&lt;hook&gt;: &lt;type&gt;: &lt;message&gt;" of the last fault, or null.</summary>
        public string LastError { get; private set; }
        /// <summary>Raised once when the count reaches the threshold.</summary>
        public event Action BecameFaulted;

        /// <summary>Faults per hook name ("update", "event SceneLoaded", "command seq.play", ...).</summary>
        public Dictionary<string, int> ByHook()
        {
            lock (gate) return new Dictionary<string, int>(byHook, StringComparer.Ordinal);
        }

        /// <summary>Calls into the plugin's code now on the stack (any thread): above 0 while the plugin's code runs.</summary>
        public int Depth => System.Threading.Volatile.Read(ref depth);

        /// <summary>Marks a call into the plugin's code that the guard does not wrap itself (pair with <see cref="Exit"/>).</summary>
        public void Enter() => System.Threading.Interlocked.Increment(ref depth);

        public void Exit() => System.Threading.Interlocked.Decrement(ref depth);

        /// <summary>Runs <paramref name="action"/>; false when it threw (the fault is counted).</summary>
        public bool Run(string hook, Action action)
        {
            Enter();
            try
            {
                action();
                return true;
            }
            catch (Exception e)
            {
                Count(hook, e);
                return false;
            }
            finally { Exit(); }
        }

        /// <summary>Runs <paramref name="f"/>; <paramref name="fallback"/> when it threw (the fault is counted).</summary>
        public T Run<T>(string hook, Func<T> f, T fallback)
        {
            Enter();
            try { return f(); }
            catch (Exception e)
            {
                Count(hook, e);
                return fallback;
            }
            finally { Exit(); }
        }

        /// <summary>Counts one fault of <paramref name="hook"/>.</summary>
        public void Count(string hook, Exception e)
        {
            if (e is System.Reflection.TargetInvocationException tie && tie.InnerException != null) e = tie.InnerException;
            bool raise = false, log;
            lock (gate)
            {
                total++;
                byHook.TryGetValue(hook, out int n);
                byHook[hook] = n + 1;
                LastError = hook + ": " + e.GetType().Name + ": " + e.Message;
                log = logged.Add(hook + "|" + e.GetType().FullName);
                if (!faulted && total >= Threshold)
                {
                    faulted = true;
                    raise = true;
                }
            }
            if (log)
            {
                try { logWarning?.Invoke("[plugin " + Guid + "] " + hook + " threw " + e); }
                catch (Exception) { }
            }
            if (raise)
            {
                try { logWarning?.Invoke("[plugin " + Guid + "] faulted after " + Threshold + " faults (last: " + LastError + ")"); }
                catch (Exception) { }
                try { BecameFaulted?.Invoke(); }
                catch (Exception) { }
            }
        }
    }

    /// <summary>The time a plugin's Update takes: the median and the maximum over the last frames (600 by default).</summary>
    public sealed class PluginTimer
    {
        readonly float[] ring;
        int count, next;

        public PluginTimer(int capacity = 600) { ring = new float[Math.Max(1, capacity)]; }

        public int Count => count;

        public void Add(double ms)
        {
            ring[next] = (float)ms;
            next = (next + 1) % ring.Length;
            if (count < ring.Length) count++;
        }

        /// <summary>Times <paramref name="action"/> and adds the milliseconds.</summary>
        public void Time(Action action)
        {
            long t0 = Stopwatch.GetTimestamp();
            try { action(); }
            finally { Add((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency); }
        }

        public double Median
        {
            get
            {
                if (count == 0) return 0;
                var a = new float[count];
                Array.Copy(ring, a, count);
                Array.Sort(a);
                return count % 2 == 1 ? a[count / 2] : (a[count / 2 - 1] + a[count / 2]) / 2.0;
            }
        }

        public double Max
        {
            get
            {
                double m = 0;
                for (int i = 0; i < count; i++) m = Math.Max(m, ring[i]);
                return m;
            }
        }
    }
}

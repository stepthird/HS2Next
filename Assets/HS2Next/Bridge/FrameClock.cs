// The bridge frame clock (HS2Next.Api.Bridge.IBridgeFrames): the pump calls
// BeginFrame at Update −10000, RunEndOfPass after the queue pass, and Late at LateUpdate 32000. Scheduled actions run
// outside the lock in scheduling order; an action that throws is reported through the bridge logger and does not stop
// the others. Editor batch tests drive the same calls by hand (BridgeServer.ManualFrame).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using HS2Next.Api.Bridge;

namespace HS2Next.Bridge
{
    public sealed class FrameClock : IBridgeFrames
    {
        readonly object gate = new object();
        readonly Stopwatch sw = Stopwatch.StartNew();
        readonly List<(int due, long seq, Action a)> updates = new List<(int, long, Action)>();
        readonly List<(int due, long seq, Action a)> lates = new List<(int, long, Action)>();
        readonly List<Action> endOfPass = new List<Action>();
        readonly Action<Exception, string> report;
        long seq;
        volatile int frame;
        int lateDone = int.MinValue;   // frame whose late point has run

        internal FrameClock(Action<Exception, string> report) { this.report = report; }

        public int Frame => frame;
        public double Realtime => sw.Elapsed.TotalSeconds;

        public event Action<int> FrameStarted;
        public event Action<int> LateFrame;

        public void AtUpdate(int afterFrames, Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (afterFrames < 0) throw new ArgumentOutOfRangeException(nameof(afterFrames));
            lock (gate)
            {
                if (afterFrames == 0) endOfPass.Add(action);
                else updates.Add((frame + afterFrames, seq++, action));
            }
        }

        public void AtLate(int afterFrames, Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (afterFrames < 0) throw new ArgumentOutOfRangeException(nameof(afterFrames));
            lock (gate)
            {
                // between frames (this frame's late point already ran) 0 means the next frame's late point
                int baseFrame = lateDone >= frame ? frame + 1 : frame;
                lates.Add((baseFrame + afterFrames, seq++, action));
            }
        }

        public Task WaitFrames(int frames)
        {
            var tcs = new TaskCompletionSource<bool>();
            AtUpdate(Math.Max(1, frames), () => tcs.TrySetResult(true));
            return tcs.Task;
        }

        public Task WaitLate(int afterFrames = 0)
        {
            var tcs = new TaskCompletionSource<bool>();
            AtLate(Math.Max(0, afterFrames), () => tcs.TrySetResult(true));
            return tcs.Task;
        }

        /// <summary>Start of the pump of frame <paramref name="f"/>: FrameStarted, then the update actions due.</summary>
        internal void BeginFrame(int f)
        {
            frame = f;
            Raise(FrameStarted, f, "FrameStarted");
            RunDue(updates, f);
        }

        /// <summary>After the pump's queue pass: the actions scheduled with AtUpdate(0).</summary>
        internal void RunEndOfPass()
        {
            for (int round = 0; round < 8; round++)   // actions may schedule more; bounded
            {
                List<Action> run;
                lock (gate)
                {
                    if (endOfPass.Count == 0) return;
                    run = new List<Action>(endOfPass);
                    endOfPass.Clear();
                }
                foreach (var a in run) Invoke(a, "AtUpdate(0)");
            }
        }

        /// <summary>The late point of frame <paramref name="f"/>: the late actions due, then LateFrame.</summary>
        internal void Late(int f)
        {
            frame = f;
            RunDue(lates, f);
            lock (gate) lateDone = f;
            Raise(LateFrame, f, "LateFrame");
        }

        /// <summary>Drops every scheduled action (bridge stop).</summary>
        internal void Clear()
        {
            lock (gate)
            {
                updates.Clear();
                lates.Clear();
                endOfPass.Clear();
            }
        }

        void RunDue(List<(int due, long seq, Action a)> list, int f)
        {
            List<(int due, long seq, Action a)> run = null;
            lock (gate)
            {
                for (int i = 0; i < list.Count; i++)
                    if (list[i].due <= f) (run ?? (run = new List<(int, long, Action)>())).Add(list[i]);
                if (run == null) return;
                list.RemoveAll(e => e.due <= f);
            }
            run.Sort((a, b) => a.seq.CompareTo(b.seq));
            foreach (var e in run) Invoke(e.a, "scheduled action");
        }

        void Invoke(Action a, string what)
        {
            try { a(); }
            catch (Exception e) { report?.Invoke(e, what); }
        }

        void Raise(Action<int> ev, int f, string what)
        {
            if (ev == null) return;
            foreach (Action<int> h in ev.GetInvocationList())
            {
                try { h(f); }
                catch (Exception e) { report?.Invoke(e, what); }
            }
        }
    }
}

// The bridge dispatcher: a FIFO of jobs run on the main thread by the
// pump, with a time budget per frame for starting new queue entries. A job is one request: a single command, or a
// batch whose members run in order as one queue entry. The job at the head holds the queue until it is finished, so
// requests are strictly ordered even when a handler defers (a capture at the late point, a load over many frames) or
// the envelope's waitFrames delays a reply. Member flow: gates (unknown command, readiness, envelope, unknown and
// missing arguments) → handler (synchronous, deferred or async) → reply object → optional hold of waitFrames frames.
// A deferred completion of a single command replies at once from the completing thread; batch members resume at
// the next pump. Deadlines are watched by a timer: an expired job is answered `timeout`, its cancellation token is
// signalled, and the pump drops it (ending its batch scope). Unity-free: the Unity layer only calls Pump / PumpLate.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using HS2Next.Api.Bridge;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HS2Next.Bridge
{
    enum MemberState { Pending, Waiting, Completed, Held, Done }

    sealed class Member
    {
        internal JObject Request;
        internal int Index;
        internal string As;
        internal MemberState State;
        // outcome (set under the job lock, possibly by another thread)
        internal bool HasOutcome;
        internal object Result;
        internal BridgeError Error;
        // finalization (main thread, or the completing thread on the single-command fast path)
        internal JObject Reply;
        internal bool Ok;
        internal int HoldUntil;
        /// <summary>Frame of the clock when the outcome arrived (waitFrames counts from it).</summary>
        internal int CompletedFrame;
        internal Invocation Inv;
        internal string Cmd;
    }

    sealed class Job
    {
        static long nextSeq;

        internal readonly long Seq = Interlocked.Increment(ref nextSeq);
        internal readonly object Lock = new object();
        internal readonly JObject Request;
        internal readonly bool IsBatch;
        internal readonly List<Member> Members = new List<Member>();
        internal readonly CancellationTokenSource Cts = new CancellationTokenSource();
        internal readonly Action<int, string> Respond;
        internal readonly string Cmd;
        internal readonly string IdJson;
        internal readonly long ReceivedTicks = Stopwatch.GetTimestamp();
        internal double TimeoutSec;
        internal long DeadlineTicks;
        internal int Index;
        /// <summary>Index of the member started last (−1 before the first).</summary>
        internal int Running = -1;
        internal bool StopOnError = true, Stopped, Begun, Finalized, Inline, RunNow;
        internal int FailedAt = -1;
        internal int HoldUntil = int.MinValue;
        internal string FinalJson;
        internal BatchScope Scope;
        internal readonly Dictionary<string, JObject> Named = new Dictionary<string, JObject>(StringComparer.Ordinal);
        internal volatile bool Abandoned;
        internal FrameClock Clock;
        readonly List<Action> afterReply = new List<Action>();
        int replied;

        internal Job(JObject request, string cmd, bool isBatch, Action<int, string> respond)
        {
            Request = request;
            Cmd = cmd;
            IsBatch = isBatch;
            Respond = respond;
            IdJson = request != null && request.TryGetValue(EnvelopeFields.Id, StringComparison.Ordinal, out var id) ? id.ToString(Formatting.None) : null;
        }

        internal bool Replied => Volatile.Read(ref replied) != 0;

        internal bool TryReply(int status, string json)
        {
            if (Interlocked.CompareExchange(ref replied, 1, 0) != 0) return false;
            try { Respond?.Invoke(status, json); }
            catch (Exception) { }
            List<Action> run;
            lock (afterReply) { run = new List<Action>(afterReply); afterReply.Clear(); }
            foreach (var a in run) Clock?.AtUpdate(1, a);
            return true;
        }

        /// <summary>Schedules an action for the first pump after the reply (at once when already replied).</summary>
        internal void AddAfterReply(Action a)
        {
            lock (afterReply)
            {
                if (!Replied) { afterReply.Add(a); return; }
            }
            Clock?.AtUpdate(1, a);
        }

        internal string State => !Begun ? "queued" : "running";
    }

    sealed class BatchScope : IBatchScope
    {
        readonly Job job;
        internal BatchScope(Job job) { this.job = job; }
        public int Count => job.Members.Count;
        public int Index => job.Running;
        public bool StopOnError => job.StopOnError;
        public string RequestId => job.IdJson;
        public IDictionary<string, object> Items { get; } = new Dictionary<string, object>(StringComparer.Ordinal);
    }

    /// <summary>The ICommandContext of one member invocation.</summary>
    sealed class Invocation : ICommandContext
    {
        readonly Dispatcher d;
        internal readonly Job Job;
        internal readonly Member Member;
        internal readonly List<string> Warnings = new List<string>();
        DeferredReply deferred;
        internal bool HandlerReturned;

        internal Invocation(Dispatcher d, Job job, Member m, CommandEntry entry, string calledAs, ArgsView args,
                            int waitFrames, bool strict, bool undo)
        {
            this.d = d;
            Job = job;
            Member = m;
            Entry = entry;
            Command = calledAs;
            Args = args;
            WaitFrames = waitFrames;
            Strict = strict;
            Undo = undo;
        }

        internal CommandEntry Entry { get; }
        public string Command { get; }
        public CommandSpec Spec => Entry.Spec;
        public ICommandArgs Args { get; }
        public string RequestId => Member.Request.TryGetValue(EnvelopeFields.Id, StringComparison.Ordinal, out var id) ? id.ToString(Formatting.None) : Job.IdJson;
        public int WaitFrames { get; }
        public double TimeoutSec => Job.TimeoutSec;
        public bool Strict { get; }
        public bool Undo { get; }
        public IBatchScope Batch => Job.Scope;
        public IBridgeFrames Frames => d.Server.Frames;
        public IBridgeReadiness Readiness => d.Server.Readiness;
        public IOutputRoot Output => d.Server.Output;
        public CancellationToken Cancelled => Job.Cts.Token;
        public bool IsDeferred => deferred != null;

        public void AfterReply(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            Job.AddAfterReply(action);
        }

        public void Warn(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            lock (Warnings) Warnings.Add(message);
        }

        public IDeferredReply Defer()
        {
            lock (Job.Lock)
            {
                if (deferred != null) return deferred;
                if (HandlerReturned || Member.HasOutcome) throw new InvalidOperationException("Defer must be called while the handler runs");
                if (Job.Inline) throw new InvalidOperationException(Spec.Name + (Job.RunNow ? " is called inline and cannot defer" : " is answered on the listener thread and cannot defer"));
                deferred = new DeferredReply(d, this);
                return deferred;
            }
        }

        internal DeferredReply Deferred => deferred;
        internal DeferredReply ForceDeferred() { lock (Job.Lock) return deferred ?? (deferred = new DeferredReply(d, this)); }
    }

    sealed class DeferredReply : IDeferredReply
    {
        readonly Dispatcher d;
        readonly Invocation inv;
        int done;

        internal DeferredReply(Dispatcher d, Invocation inv) { this.d = d; this.inv = inv; }

        public bool IsCompleted => Volatile.Read(ref done) != 0;
        public bool IsAbandoned => inv.Job.Abandoned;
        public CancellationToken Cancelled => inv.Job.Cts.Token;

        public bool Complete(object result) => Finish(result, null);
        public bool Fail(BridgeError error) => Finish(null, error ?? new BridgeError(BridgeCodes.Internal, "failed"));
        public bool Fail(Exception e) => Finish(null, d.ErrorOf(e, inv.Spec));

        bool Finish(object result, BridgeError error)
        {
            if (Interlocked.CompareExchange(ref done, 1, 0) != 0) return false;
            if (inv.Job.Abandoned) return false;
            d.SetOutcome(inv.Job, inv.Member, result, error, fromCompleter: true);
            return true;
        }
    }

    sealed class Dispatcher
    {
        internal readonly BridgeServer Server;
        readonly object queueLock = new object();
        readonly Queue<Job> queue = new Queue<Job>();
        readonly HashSet<Job> active = new HashSet<Job>();
        volatile Job current;
        Timer reaper;
        volatile bool shutDown;
        int mainThreadId = -1;

        internal Dispatcher(BridgeServer server) { Server = server; }

        BridgeOptions Options => Server.Options;

        internal int QueueLength { get { lock (queueLock) return queue.Count + (current != null ? 1 : 0); } }
        internal int MainThreadId => mainThreadId;

        internal void StartReaper()
        {
            shutDown = false;
            reaper = new Timer(_ => Reap(), null, Options.ReaperIntervalMs, Options.ReaperIntervalMs);
        }

        // ------------------------------------------------------------------ submission (any thread)

        /// <summary>Queues a job; false when the queue is full (the caller answers busy).</summary>
        internal bool Enqueue(Job job)
        {
            lock (queueLock)
            {
                if (shutDown) return false;
                if (queue.Count >= Options.QueueLimit) return false;
                job.DeadlineTicks = job.ReceivedTicks + (long)(job.TimeoutSec * Stopwatch.Frequency);
                queue.Enqueue(job);
                active.Add(job);
            }
            return true;
        }

        internal bool IsShutDown => shutDown;

        void Reap()
        {
            List<Job> expired = null;
            long now = Stopwatch.GetTimestamp();
            lock (queueLock)
            {
                foreach (var j in active)
                    if (!j.Replied && now > j.DeadlineTicks) (expired ?? (expired = new List<Job>())).Add(j);
            }
            if (expired == null) return;
            foreach (var j in expired) Expire(j);
        }

        void Expire(Job j)
        {
            j.Abandoned = true;
            var err = new BridgeError(BridgeCodes.Timeout,
                "timed out after " + j.TimeoutSec.ToString("0.###", CultureInfo.InvariantCulture) + " s (" + j.State + ")",
                new BridgeErrorDetail { Extra = new Dictionary<string, object> { ["timeoutSec"] = j.TimeoutSec, ["state"] = j.State } });
            if (j.TryReply(200, BridgeJson.Write(ErrorReply(j.Cmd, j.IdJson, err, null)))) Server.LogLine(BridgeLogLevel.Warning, "bridge: " + j.Cmd + " " + err.Message);
            Retire(j);
            try { j.Cts.Cancel(); } catch (Exception) { }
        }

        void Retire(Job j)
        {
            lock (queueLock) active.Remove(j);
        }

        /// <summary>Answers every pending job (503, not_ready) and stops the reaper. Ends a running batch scope when called on the main thread.</summary>
        internal void Shutdown()
        {
            List<Job> pending;
            lock (queueLock)
            {
                shutDown = true;
                pending = new List<Job>(active);
                active.Clear();
                queue.Clear();
            }
            reaper?.Dispose();
            reaper = null;
            var err = new BridgeError(BridgeCodes.NotReady, "the bridge is shutting down");
            foreach (var j in pending)
            {
                j.Abandoned = true;
                j.TryReply(503, BridgeJson.Write(ErrorReply(j.Cmd, j.IdJson, err, null)));
                try { j.Cts.Cancel(); } catch (Exception) { }
            }
            var c = current;
            if (c != null && (mainThreadId < 0 || Thread.CurrentThread.ManagedThreadId == mainThreadId)) EndBatch(c);
            current = null;
        }

        // ------------------------------------------------------------------ pump (main thread)

        internal void Pump(int frame)
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            Server.Frames.BeginFrame(frame);
            var sw = Stopwatch.StartNew();
            while (!shutDown)
            {
                var head = current;   // Shutdown from another thread may clear the field
                if (head != null)
                {
                    if (!Advance(head, frame)) break;
                    Retire(head);
                    current = null;
                    if (sw.Elapsed.TotalMilliseconds >= Options.PumpBudgetMs) break;
                    continue;
                }
                Job next = null;
                lock (queueLock) if (queue.Count > 0) next = queue.Dequeue();
                if (next == null) break;
                if (next.Abandoned || next.Replied) { Retire(next); continue; }
                current = next;
            }
            Server.Frames.RunEndOfPass();
        }

        internal void PumpLate(int frame)
        {
            Server.Frames.Late(frame);
        }

        /// <summary>Calls nested deeper than this answer busy (a handler that calls RunNow, whose handler calls RunNow, ...).</summary>
        internal const int MaxRunNowDepth = 8;
        int runNowDepth;

        /// <summary>
        /// BridgeServer.RunNow: one request run to completion on the main thread without the queue (in-process inline calls).
        /// Refusals are error replies; the reply text is what HTTP would get.
        /// </summary>
        internal string RunNow(string requestJson)
        {
            int main = mainThreadId;
            if (main >= 0 && Thread.CurrentThread.ManagedThreadId != main) throw new InvalidOperationException("an inline call must be made on the main thread");
            JObject req;
            try { req = BridgeJson.Parse(requestJson ?? "") as JObject; }
            catch (Exception e) { return BridgeJson.Write(ErrorReply(null, null, new BridgeError(BridgeCodes.BadJson, "the request is not valid JSON: " + e.Message), null)); }
            if (req == null) return BridgeJson.Write(ErrorReply(null, null, new BridgeError(BridgeCodes.BadJson, "the request must be a JSON object {\"cmd\": ...}"), null));
            string idJson = req.TryGetValue(EnvelopeFields.Id, StringComparison.Ordinal, out var idTok) ? idTok.ToString(Formatting.None) : null;
            if (!req.TryGetValue(EnvelopeFields.Cmd, StringComparison.Ordinal, out var cmdTok) || cmdTok.Type != JTokenType.String || ((string)cmdTok).Length == 0)
                return BridgeJson.Write(ErrorReply(null, idJson, BridgeError.Missing(EnvelopeFields.Cmd, ArgTypes.String), null));
            string cmd = (string)cmdTok;
            var entry = Server.Commands.Lookup(cmd, out _);
            string name = entry?.Spec.Name ?? cmd;
            if (runNowDepth >= MaxRunNowDepth)
                return BridgeJson.Write(ErrorReply(name, idJson, BridgeError.Busy("inline calls nested deeper than " + MaxRunNowDepth), null));
            if (entry != null)
            {
                var spec = entry.Spec;
                string why = spec.Name == "batch" ? "batch cannot be called inline"
                    : (spec.Flags & CommandFlags.Defers) != 0 ? spec.Name + " may answer later and cannot be called inline"
                    : spec.HandlesWaitFrames ? spec.Name + " waits frames itself and cannot be called inline"
                    : entry.Async != null ? spec.Name + " is asynchronous and cannot be called inline"
                    : EnvInt(req, EnvelopeFields.WaitFrames, 0, out _) > 0 ? "waitFrames cannot be used in an inline call"
                    : null;
                if (why != null) return BridgeJson.Write(ErrorReply(name, idJson, BridgeError.Unsupported(why + " (use the queued call)"), null));
                if ((spec.Flags & CommandFlags.Mutates) != 0 && Server.InlineGate != null)
                {
                    string busy = null;
                    try { busy = Server.InlineGate(spec); }
                    catch (Exception e) { Server.Report(e, "inline gate"); }
                    if (busy != null) return BridgeJson.Write(ErrorReply(name, idJson, BridgeError.Busy(busy), null));
                }
            }
            string reply = null;
            var job = new Job(req, name, false, (status, json) => reply = json) { Clock = Server.Frames, Inline = true, RunNow = true };
            job.Members.Add(new Member { Request = req, Index = 0, Cmd = cmd });
            job.TimeoutSec = Options.MaxTimeoutSec;
            runNowDepth++;
            try
            {
                int frame = Server.Frames.Frame;
                for (int guard = 0; guard < 100 && !Advance(job, frame); guard++) { }
            }
            finally { runNowDepth--; }
            if (!job.Replied)
            {
                job.Abandoned = true;
                job.TryReply(200, BridgeJson.Write(ErrorReply(name, idJson, new BridgeError(BridgeCodes.Internal, name + " did not complete inline"), null)));
                try { job.Cts.Cancel(); } catch (Exception) { }
            }
            if (reply != null && req.ContainsKey(EnvelopeFields.Timeout) && BridgeJson.Parse(reply) is JObject o)
            {
                // a timeout has no meaning inline: say so in the reply
                var w = o["warnings"] as JArray ?? new JArray();
                w.Add("timeout is ignored in an inline call");
                o["warnings"] = w;
                reply = BridgeJson.Write(o);
            }
            return reply;
        }

        /// <summary>Runs a job of listener-thread commands to completion on the calling thread (they never defer).</summary>
        internal void RunInline(Job job)
        {
            job.Inline = true;
            int frame = Server.Frames.Frame;
            for (int guard = 0; guard < 100 && !Advance(job, frame); guard++) { }
            if (!job.Replied) job.TryReply(200, BridgeJson.Write(ErrorReply(job.Cmd, job.IdJson, new BridgeError(BridgeCodes.Internal, "inline command did not complete"), null)));
        }

        /// <summary>Moves the job forward as far as it can in this frame; true when it is finished (replied or dropped).</summary>
        bool Advance(Job job, int frame)
        {
            if (!job.Begun)
            {
                job.Begun = true;
                if (job.IsBatch && !job.Abandoned)
                {
                    job.Scope = new BatchScope(job);
                    foreach (var h in Server.BatchHooks())
                    {
                        try { h.BatchBegin(job.Scope); }
                        catch (Exception e) { Server.Report(e, "batch hook BatchBegin"); }
                    }
                }
            }
            while (true)
            {
                if (job.Abandoned) { EndBatch(job); return true; }
                if (job.Index >= job.Members.Count || job.Stopped)
                {
                    if (!job.Finalized)
                    {
                        job.Finalized = true;
                        EndBatch(job);   // the scope closes right after the last member, before a reply hold
                        job.FinalJson = job.IsBatch ? BridgeJson.Write(BatchReply(job)) : BridgeJson.Write(job.Members[0].Reply);
                        if (job.IsBatch)
                        {
                            int wf = EnvInt(job.Request, EnvelopeFields.WaitFrames, 0, out _);
                            if (wf > 0) job.HoldUntil = frame + wf;
                        }
                    }
                    if (frame < job.HoldUntil) return false;
                    EndBatch(job);
                    job.TryReply(200, job.FinalJson);
                    return true;
                }
                var m = job.Members[job.Index];
                switch (m.State)
                {
                    case MemberState.Pending:
                        StartMember(job, m);
                        break;
                    case MemberState.Waiting:
                        lock (job.Lock)
                        {
                            if (m.State == MemberState.Waiting)
                            {
                                if (!m.HasOutcome) return false;
                                m.State = MemberState.Completed;
                            }
                        }
                        break;
                    case MemberState.Completed:
                        FinalizeMember(job, m, frame);
                        break;
                    case MemberState.Held:
                        if (frame < m.HoldUntil) return false;
                        m.State = MemberState.Done;
                        break;
                    case MemberState.Done:
                        if (!m.Ok && job.FailedAt < 0) job.FailedAt = m.Index;
                        if (m.As != null && m.Reply != null) job.Named[m.As] = m.Reply;
                        job.Index++;
                        if (!m.Ok && job.StopOnError) job.Stopped = true;
                        break;
                }
            }
        }

        void EndBatch(Job job)
        {
            var scope = job.Scope;
            if (scope == null) return;
            job.Scope = null;
            bool ok = !job.Abandoned && job.FailedAt < 0;
            int failedAt = job.FailedAt >= 0 ? job.FailedAt : job.Abandoned ? Math.Min(job.Index, job.Members.Count - 1) : -1;
            foreach (var h in Server.BatchHooks())
            {
                try { h.BatchEnd(scope, ok, failedAt); }
                catch (Exception e) { Server.Report(e, "batch hook BatchEnd"); }
            }
        }

        // ------------------------------------------------------------------ members

        void StartMember(Job job, Member m)
        {
            m.State = MemberState.Waiting;
            job.Running = m.Index;
            JObject req = m.Request;
            if (job.IsBatch && job.Named.Count > 0)
            {
                try { req = m.Request = Substitute(job, m.Request); }
                catch (BridgeException e) { SetOutcome(job, m, null, e.Error); return; }
            }
            string cmd = m.Cmd;
            var entry = Server.Commands.Lookup(cmd, out bool viaAlias);
            var gateErr = PreGate(cmd, entry);
            if (gateErr != null) { SetOutcome(job, m, null, gateErr); return; }
            if (entry == null)
            {
                var cands = NameMatch.Candidates(cmd, Server.Commands.AllNames());
                SetOutcome(job, m, null, new BridgeError(BridgeCodes.UnknownCommand, "unknown command " + cmd + (cands.Count > 0 ? " (did you mean " + string.Join(", ", cands) + "?)" : ""),
                    new BridgeErrorDetail { Arg = EnvelopeFields.Cmd, Candidates = cands }));
                return;
            }
            var spec = entry.Spec;
            if (job.IsBatch && spec.Name == "batch")
            {
                SetOutcome(job, m, null, BridgeError.BadArgs("cmds", "commands other than batch", "a batch cannot contain a batch"));
                return;
            }
            // envelope
            int waitFrames = EnvInt(req, EnvelopeFields.WaitFrames, -1, out var wfErr);
            bool strict = EnvBool(req, EnvelopeFields.Strict, job.IsBatch && EnvBool(job.Request, EnvelopeFields.Strict, false, out _), out var stErr);
            bool undo = EnvBool(req, EnvelopeFields.Undo, !job.IsBatch || EnvBool(job.Request, EnvelopeFields.Undo, true, out _), out var unErr);
            var envErr = wfErr ?? stErr ?? unErr;
            if (envErr != null) { SetOutcome(job, m, null, envErr); return; }
            if (waitFrames > 100000) { SetOutcome(job, m, null, BridgeError.BadArgs(EnvelopeFields.WaitFrames, "int (0..100000)")); return; }
            var args = new ArgsView(req, spec);
            var inv = new Invocation(this, job, m, entry, cmd, args, waitFrames, strict, undo);
            m.Inv = inv;
            if (viaAlias) inv.Warn("deprecated: use " + spec.Name);
            if (job.IsBatch && req.ContainsKey(EnvelopeFields.Timeout)) inv.Warn("timeout of a batch member is ignored (the batch's timeout applies)");
            if (args.Unknown.Count > 0)
            {
                var declared = new List<string>();
                foreach (var a in spec.Args) declared.Add(a.Name);
                if (strict)
                {
                    string u = args.Unknown[0];
                    var c = NameMatch.Candidates(u, declared);
                    SetOutcome(job, m, null, new BridgeError(BridgeCodes.BadArgs,
                        "unknown argument" + (args.Unknown.Count > 1 ? "s " : " ") + string.Join(", ", args.Unknown) + " for " + spec.Name + " (strict)" +
                        (c.Count > 0 ? "; did you mean " + string.Join(", ", c) + "?" : ""),
                        new BridgeErrorDetail { Arg = u, Candidates = c, Expected = declared.Count > 0 ? "one of " + string.Join(", ", declared) : "no arguments" }));
                    return;
                }
                foreach (var u in args.Unknown)
                {
                    var c = NameMatch.Candidates(u, declared);
                    inv.Warn("unknown argument '" + u + "' ignored" + (c.Count > 0 ? " (did you mean " + string.Join(", ", c) + "?)" : ""));
                }
            }
            foreach (var a in spec.Args)
                if (a.Required && !args.Has(a.Name))
                {
                    SetOutcome(job, m, null, BridgeError.Missing(a.Name, a.Type));
                    return;
                }
            // handler
            object result;
            try { result = entry.Async != null ? entry.Async(inv) : entry.Sync(inv); }
            catch (Exception e)
            {
                inv.HandlerReturned = true;
                if (inv.Deferred != null) inv.Deferred.Fail(e);
                else SetOutcome(job, m, null, ErrorOf(e, spec));
                return;
            }
            inv.HandlerReturned = true;
            // a task (async handler, or a sync handler that returned one) replies when it completes
            if (result is Task task)
            {
                if (task.IsCompleted) { CompleteFromTask(job, m, task, spec); return; }
                var dr = inv.ForceDeferred();
                task.ContinueWith(tk =>
                {
                    if (tk.IsCanceled) dr.Fail(new BridgeError(BridgeCodes.Timeout, "the command was cancelled"));
                    else if (tk.IsFaulted) dr.Fail(tk.Exception?.InnerException ?? tk.Exception);
                    else dr.Complete(TaskResult(tk));
                }, TaskContinuationOptions.ExecuteSynchronously);
                return;
            }
            if (inv.Deferred != null)
            {
                if (result != null) inv.Warn("the handler deferred and also returned a value; the value was ignored");
                return;   // Waiting until Complete / Fail (possibly already called)
            }
            SetOutcome(job, m, result, null);
        }

        void CompleteFromTask(Job job, Member m, Task t, CommandSpec spec)
        {
            if (t.IsCanceled) SetOutcome(job, m, null, new BridgeError(BridgeCodes.Timeout, "the command was cancelled"));
            else if (t.IsFaulted) SetOutcome(job, m, null, ErrorOf(t.Exception?.InnerException ?? t.Exception, spec));
            else SetOutcome(job, m, TaskResult(t), null);
        }

        /// <summary>The result of a completed Task&lt;T&gt; (null for a plain Task).</summary>
        static object TaskResult(Task t)
        {
            if (t is Task<object> to) return to.Result;
            var p = t.GetType().IsGenericType ? t.GetType().GetProperty("Result") : null;
            return p == null || p.PropertyType.Name == "VoidTaskResult" ? null : p.GetValue(t);
        }

        /// <summary>
        /// The readiness gate (any thread): not_ready for an unknown command before the app is ready and for a command
        /// whose gate or required stages are not ready; null when the command may run.
        /// </summary>
        internal BridgeError PreGate(string cmd, CommandEntry entry)
        {
            var readiness = Server.Readiness;
            if (entry == null)
                return readiness.AppReady ? null : NotReady("the app is not ready yet (unknown command " + cmd + ")", NameMatch.Candidates(cmd, Server.Commands.AllNames()));
            var spec = entry.Spec;
            var missing = spec.Gate == ReadyGate.App ? readiness.Missing(spec.Requires) : ReadinessMissingExtra(readiness, spec.Requires);
            return missing.Count == 0 ? null : NotReady(spec.Name + " needs " + string.Join(", ", missing) + " (not ready yet)", null);
        }

        static List<string> ReadinessMissingExtra(Readiness r, IReadOnlyList<string> extra)
        {
            var l = new List<string>();
            if (extra == null) return l;
            foreach (var n in extra) if (!r.IsReady(n)) l.Add(n);
            return l;
        }

        BridgeError NotReady(string message, IReadOnlyList<string> candidates)
        {
            var ready = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var kv in Server.Readiness.Snapshot()) ready[kv.Key] = kv.Value;
            var extra = new Dictionary<string, object> { ["ready"] = ready };
            var details = Server.Readiness.Details();
            if (details.Count > 0)
            {
                var d = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var kv in details) d[kv.Key] = kv.Value;
                extra["readyDetail"] = d;
            }
            return new BridgeError(BridgeCodes.NotReady, message,
                new BridgeErrorDetail { Candidates = candidates != null && candidates.Count > 0 ? candidates : null, Extra = extra });
        }

        /// <summary>Records a member's outcome. On the single-command fast path (completion outside the pump) it also replies.</summary>
        internal void SetOutcome(Job job, Member m, object result, BridgeError error, bool fromCompleter = false)
        {
            lock (job.Lock)
            {
                if (m.HasOutcome) return;
                m.HasOutcome = true;
                m.Result = result;
                m.Error = error;
                m.CompletedFrame = Server.Frames.Frame;
                if (fromCompleter && !job.IsBatch && !job.Abandoned && m.State == MemberState.Waiting)
                {
                    // reply now unless a waitFrames hold applies; the pump then only drops the finished job
                    if (HoldFrames(m) <= 0)
                    {
                        BuildReply(job, m);
                        m.State = MemberState.Done;
                        job.Finalized = true;
                        job.FinalJson = BridgeJson.Write(m.Reply);
                        job.TryReply(200, job.FinalJson);
                        Retire(job);
                    }
                }
            }
        }

        static int HoldFrames(Member m) => m.Inv != null && !m.Inv.Spec.HandlesWaitFrames && m.Inv.WaitFrames > 0 ? m.Inv.WaitFrames : 0;

        void FinalizeMember(Job job, Member m, int frame)
        {
            BuildReply(job, m);
            int hold = HoldFrames(m);
            // waitFrames counts from the frame in which the outcome arrived (a deferred reply may complete later)
            m.HoldUntil = Math.Min(frame, m.CompletedFrame) + hold;
            m.State = hold > 0 && frame < m.HoldUntil ? MemberState.Held : MemberState.Done;
        }

        void BuildReply(Job job, Member m)
        {
            string name = m.Inv?.Spec.Name ?? m.Cmd;
            string id = m.Request.TryGetValue(EnvelopeFields.Id, StringComparison.Ordinal, out var idTok) ? idTok.ToString(Formatting.None) : (job.IsBatch ? null : job.IdJson);
            List<string> warnings = null;
            if (m.Inv != null) lock (m.Inv.Warnings) warnings = m.Inv.Warnings.Count > 0 ? new List<string>(m.Inv.Warnings) : null;
            if (m.Error != null)
            {
                m.Ok = false;
                m.Reply = ErrorReply(name, id, m.Error, warnings);
                return;
            }
            JToken result;
            try { result = BridgeJson.FromResult(m.Result); }
            catch (Exception e)
            {
                Server.Report(e, name + " result serialization");
                m.Ok = false;
                m.Reply = ErrorReply(name, id, new BridgeError(BridgeCodes.Internal, "the result of " + name + " could not be serialized: " + e.Message), warnings);
                return;
            }
            var o = new JObject { ["ok"] = true, ["cmd"] = name };
            if (id != null) o["id"] = JRawOrParse(id);
            if (result is JObject ro)
            {
                foreach (var p in ro.Properties())
                {
                    if (IsEnvelopeReplyField(p.Name))
                    {
                        (warnings ?? (warnings = new List<string>())).Add("result field '" + p.Name + "' clashes with the envelope and was dropped");
                        continue;
                    }
                    o[p.Name] = p.Value;
                }
            }
            else if (result != null && result.Type != JTokenType.Null) o["result"] = result;
            if (warnings != null) o["warnings"] = new JArray(warnings.ToArray());
            m.Ok = true;
            m.Reply = o;
        }

        static bool IsEnvelopeReplyField(string n) => n == "ok" || n == "cmd" || n == "id" || n == "warnings" || n == "error" || n == "code" || n == "detail";

        static JToken JRawOrParse(string json)
        {
            try { return BridgeJson.Parse(json); }
            catch (Exception) { return new JRaw(json); }
        }

        internal static JObject ErrorReply(string cmd, string idJson, BridgeError err, List<string> warnings)
        {
            var o = new JObject { ["ok"] = false };
            if (cmd != null) o["cmd"] = cmd;
            if (idJson != null) o["id"] = JRawOrParse(idJson);
            o["error"] = err.Message;
            o["code"] = err.Code;
            var d = DetailToken(err.Detail);
            if (d != null) o["detail"] = d;
            if (warnings != null && warnings.Count > 0) o["warnings"] = new JArray(warnings.ToArray());
            return o;
        }

        static JObject DetailToken(BridgeErrorDetail d)
        {
            if (d == null || d.IsEmpty) return null;
            var o = new JObject();
            if (d.Arg != null) o["arg"] = d.Arg;
            if (d.Expected != null) o["expected"] = d.Expected;
            if (d.Candidates != null && d.Candidates.Count > 0) o["candidates"] = new JArray(new List<string>(d.Candidates).ToArray());
            if (d.Phase != null) o["phase"] = d.Phase;
            if (d.Extra != null)
                foreach (var kv in d.Extra)
                {
                    if (o.ContainsKey(kv.Key)) continue;
                    JToken v;
                    try { v = BridgeJson.FromResult(kv.Value) ?? JValue.CreateNull(); }
                    catch (Exception) { v = kv.Value?.ToString(); }
                    o[kv.Key] = v;
                }
            return o;
        }

        JObject BatchReply(Job job)
        {
            var results = new JArray();
            foreach (var m in job.Members)
                if (m.Reply != null) results.Add(m.Reply);
            bool ok = job.FailedAt < 0;
            var o = new JObject { ["ok"] = ok, ["cmd"] = "batch" };
            if (job.IdJson != null) o["id"] = JRawOrParse(job.IdJson);
            o["results"] = results;
            if (!ok)
            {
                o["failedAt"] = job.FailedAt;
                var failed = job.Members[job.FailedAt].Reply;
                o["error"] = "member " + job.FailedAt + " (" + (string)failed?["cmd"] + ") failed: " + (string)failed?["error"];
                o["code"] = (string)failed?["code"];
            }
            return o;
        }

        /// <summary>The error a handler exception answers with (internal for Core and the bridge, plugin_fault for plugins).</summary>
        internal BridgeError ErrorOf(Exception e, CommandSpec spec)
        {
            if (e is AggregateException ae && ae.InnerExceptions.Count == 1) e = ae.InnerException;
            if (e is BridgeException be) return be.Error;
            if (e is OperationCanceledException) return new BridgeError(BridgeCodes.Timeout, "the command was cancelled");
            Server.Report(e, spec?.Name ?? "command");
            bool plugin = spec != null && spec.Owner != BridgeOwners.Core && spec.Owner != BridgeOwners.Bridge;
            return new BridgeError(plugin ? BridgeCodes.PluginFault : BridgeCodes.Internal,
                (spec?.Name ?? "command") + " failed: " + e.GetType().Name + ": " + e.Message,
                plugin ? new BridgeErrorDetail { Extra = new Dictionary<string, object> { ["owner"] = spec.Owner } } : null);
        }

        // ------------------------------------------------------------------ envelope values

        internal static int EnvInt(JObject req, string name, int fallback, out BridgeError err)
        {
            err = null;
            if (req == null || !req.TryGetValue(name, StringComparison.Ordinal, out var t) || t.Type == JTokenType.Null) return fallback;
            try { int v = (int)ArgsView.ConvertTo(t, typeof(int), name); if (v >= 0) return v; }
            catch (BridgeException) { }
            err = BridgeError.BadArgs(name, "int (≥ 0)");
            return fallback;
        }

        internal static bool EnvBool(JObject req, string name, bool fallback, out BridgeError err)
        {
            err = null;
            if (req == null || !req.TryGetValue(name, StringComparison.Ordinal, out var t) || t.Type == JTokenType.Null) return fallback;
            if (t.Type == JTokenType.Boolean) return (bool)t;
            err = BridgeError.BadArgs(name, ArgTypes.Bool);
            return fallback;
        }

        // ------------------------------------------------------------------ "$name" substitution

        static JObject Substitute(Job job, JObject req)
        {
            var copy = (JObject)req.DeepClone();
            foreach (var p in new List<JProperty>(copy.Properties()))
            {
                if (p.Name == EnvelopeFields.Cmd || p.Name == EnvelopeFields.As) continue;
                p.Value = SubstituteToken(job, p.Value, p.Name);
            }
            return copy;
        }

        static JToken SubstituteToken(Job job, JToken t, string path)
        {
            switch (t.Type)
            {
                case JTokenType.String:
                    string s = (string)t;
                    if (s.Length < 2 || s[0] != '$') return t;
                    int dot = s.IndexOf('.');
                    string name = dot < 0 ? s.Substring(1) : s.Substring(1, dot - 1);
                    if (!job.Named.TryGetValue(name, out var target)) return t;
                    JToken cur = target;
                    if (dot >= 0)
                        foreach (var seg in s.Substring(dot + 1).Split('.'))
                        {
                            if (cur is JObject co && co.TryGetValue(seg, StringComparison.Ordinal, out var next)) cur = next;
                            else if (cur is JArray ca && int.TryParse(seg, NumberStyles.None, CultureInfo.InvariantCulture, out int i) && i < ca.Count) cur = ca[i];
                            else throw new BridgeException(BridgeError.BadArgs(path, "a path in the result of '" + name + "'",
                                "argument '" + path + "': \"" + s + "\" does not name a value in the result of '" + name + "' (missing '" + seg + "')"));
                        }
                    return cur.DeepClone();
                case JTokenType.Array:
                    var arr = (JArray)t;
                    for (int i = 0; i < arr.Count; i++) arr[i] = SubstituteToken(job, arr[i], path + "[" + i + "]");
                    return arr;
                case JTokenType.Object:
                    foreach (var p in new List<JProperty>(((JObject)t).Properties())) p.Value = SubstituteToken(job, p.Value, path + "." + p.Name);
                    return t;
                default:
                    return t;
            }
        }
    }
}

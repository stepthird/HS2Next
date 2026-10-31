// The automation bridge: transport + safety gate + dispatcher + the bridge's own
// commands, configured by BridgeOptions. Lifecycle: Start binds 127.0.0.1:<port> (5 retries × 200 ms by default),
// writes <UserData>/Bridge/endpoint.json {url, pid, startedAt, tokenRequired} (and the token file when a token is
// required), and starts the deadline timer; Stop closes the socket, answers everything pending with 503 and removes
// endpoint.json. Requests: empty body = ping; ping, help, bridge.version and log.tail sent alone are answered on the
// listener's worker thread (they work while the main thread is busy); everything else is a job for the main-thread
// pump (Pump at Update −10000, PumpLate at LateUpdate 32000; ManualFrame for Editor batch tests). Unity-free; the
// Unity layer (Unity/BridgeRuntime.cs) hosts it in play mode and the player.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HS2Next.Api.Bridge;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HS2Next.Bridge
{
    public enum BridgeState
    {
        Stopped,
        /// <summary>Accepting HTTP requests on 127.0.0.1.</summary>
        Listening,
        /// <summary>Started without a listener: in-process requests only (SubmitAsync / Execute).</summary>
        InProcess,
        /// <summary>The port stayed unavailable; in-process requests still work.</summary>
        Failed,
    }

    /// <summary>One loaded plugin as bridge.version reports it (filled by Core's plugin loader, Phase 5.1).</summary>
    public sealed class BridgePluginInfo
    {
        public string Guid;
        public string Version;
        public string State;
    }

    public sealed class BridgeServer : IBridgeServices, IDisposable
    {
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        readonly Dispatcher dispatcher;
        readonly IBridgeTransport transport;
        readonly List<IBatchHook> hooks = new List<IBatchHook>();
        readonly object lifeLock = new object();
        IReadOnlyList<KeyValuePair<string, string>> standardHeaders;
        string endpointPath, tokenPath;
        int manualFrame;
        // frame snapshot published by the Unity host (ping is answered off the main thread)
        double hostRealtime = -1;
        long hostRealtimeTicks;
        int screenW, screenH;

        public BridgeServer(BridgeOptions options, IBridgeTransport transport = null)
        {
            Options = (options ?? new BridgeOptions()).Clone();
            this.transport = transport ?? new HttpListenerTransport();
            Commands = new CommandRegistry();
            Readiness = new Readiness();
            foreach (var s in Options.RequiredStages ?? new string[0]) Readiness.Declare(s, true);
            foreach (var s in Options.OptionalStages ?? new string[0]) Readiness.Declare(s, false);
            Frames = new FrameClock(Report);
            Output = new OutputRoot(Options.ResolveOutputDir());
            Log = new LogRing(Options.LogCapacity);
            dispatcher = new Dispatcher(this);
            BuiltinCommands.Register(this);
        }

        public BridgeOptions Options { get; }
        public CommandRegistry Commands { get; }
        public Readiness Readiness { get; }
        public FrameClock Frames { get; }
        public OutputRoot Output { get; }
        public LogRing Log { get; }
        ICommandRegistry IBridgeServices.Commands => Commands;
        IBridgeReadiness IBridgeServices.Readiness => Readiness;
        IBridgeFrames IBridgeServices.Frames => Frames;
        IOutputRoot IBridgeServices.Output => Output;

        public BridgeState State { get; private set; }
        public bool IsListening => State == BridgeState.Listening && transport.IsListening;
        public int Port => transport.Port;
        public string Url => transport.Url;
        /// <summary>The token clients must send, or null when none is required.</summary>
        public string Token { get; private set; }
        /// <summary>The token file's permissions after the last start: "none" (no file written), "inherited" (the folder's), or
        /// "ownerOnly" (the explicit list of the current user, SYSTEM and Administrators).</summary>
        public string TokenAcl { get; private set; } = TokenFileAcl.StateNone;
        /// <summary>Why the last Start failed, or null.</summary>
        public string LastError { get; private set; }
        public DateTime StartedUtc { get; private set; }
        /// <summary>Requests waiting or running.</summary>
        public int QueueLength => dispatcher.QueueLength;
        /// <summary>Plugins for bridge.version (Phase 5.1). Called on a listener thread.</summary>
        public Func<IReadOnlyList<BridgePluginInfo>> PluginInfo { get; set; }

        /// <summary>
        /// The app's gate for in-process inline calls (<see cref="RunNow"/>) of commands flagged Mutates: a non-null text means
        /// the call must not change the scene now (another party's transaction is open, a scene event is being delivered) and
        /// is answered busy with that text. Null = no gate. Main thread.
        /// </summary>
        public Func<CommandSpec, string> InlineGate { get; set; }

        /// <summary>
        /// Runs one request now, inline on the calling (main) thread, and returns the reply JSON as HTTP would return it: the
        /// readiness gate, the name lookup (aliases warn), the argument checks and the handler of a queued request, without
        /// the queue, the safety headers and the timeout. Refused before the handler runs: batch, a command that may defer
        /// (CommandFlags.Defers), a command whose handler reads waitFrames itself, an async handler and waitFrames &gt; 0
        /// (unsupported: use the queued call); calls nested deeper than 8 (busy); a mutating command while
        /// <see cref="InlineGate"/> objects (busy). InvalidOperationException off the main thread.
        /// </summary>
        public string RunNow(string requestJson) => dispatcher.RunNow(requestJson);

        public void AddBatchHook(IBatchHook hook)
        {
            if (hook == null) throw new ArgumentNullException(nameof(hook));
            lock (hooks) if (!hooks.Contains(hook)) hooks.Add(hook);
        }

        public bool RemoveBatchHook(IBatchHook hook) { lock (hooks) return hooks.Remove(hook); }

        internal List<IBatchHook> BatchHooks() { lock (hooks) return new List<IBatchHook>(hooks); }

        // ------------------------------------------------------------------ lifecycle

        /// <summary>
        /// Starts the dispatcher (deadline timer) and, when <paramref name="listen"/>, binds and starts listening;
        /// false (State Failed, LastError) when the port stays unavailable. In-process requests work in every state
        /// but Stopped-after-Stop.
        /// </summary>
        public bool Start(bool listen = true)
        {
            lock (lifeLock)
            {
                if (State == BridgeState.Listening || State == BridgeState.InProcess) return true;
                LastError = null;
                Token = Options.RequireToken ? (string.IsNullOrEmpty(Options.Token) ? RequestGate.NewToken() : Options.Token) : null;
                TokenAcl = TokenFileAcl.StateNone;
                standardHeaders = new[]
                {
                    new KeyValuePair<string, string>("Cache-Control", "no-store"),
                    new KeyValuePair<string, string>(BridgeProtocol.BridgeHeader, BridgeVersion.Semver),
                };
                if (!listen)
                {
                    StartedUtc = DateTime.UtcNow;
                    State = BridgeState.InProcess;
                    dispatcher.StartReaper();
                    LogLine(BridgeLogLevel.Info, "bridge: started in-process (no listener)");
                    return true;
                }
                Exception last = null;
                for (int attempt = 0; attempt <= Math.Max(0, Options.BindRetries); attempt++)
                {
                    if (attempt > 0) Thread.Sleep(Math.Max(0, Options.BindRetryDelayMs));
                    int port = Options.Port > 0 ? Options.Port : FreePort();
                    try
                    {
                        transport.Start(port, OnExchange);
                        last = null;
                        break;
                    }
                    catch (Exception e)
                    {
                        last = e;
                        LogLine(BridgeLogLevel.Warning, "bridge: cannot listen on 127.0.0.1:" + port + " (attempt " + (attempt + 1) + "): " + e.Message);
                    }
                }
                if (last != null)
                {
                    State = BridgeState.Failed;
                    LastError = last.GetType().Name + ": " + last.Message;
                    StartedUtc = DateTime.UtcNow;
                    dispatcher.StartReaper();
                    LogLine(BridgeLogLevel.Error, "bridge: not listening: " + LastError);
                    return false;
                }
                StartedUtc = DateTime.UtcNow;
                State = BridgeState.Listening;
                dispatcher.StartReaper();
                WriteDiscoveryFiles();
                LogLine(BridgeLogLevel.Info, "bridge: listening on " + Url + (Token != null ? " (token required)" : ""));
                return true;
            }
        }

        /// <summary>Stops listening and answers everything pending (503). Safe to call repeatedly and from any thread.</summary>
        public void Stop()
        {
            lock (lifeLock)
            {
                if (State == BridgeState.Stopped) return;
                bool wasListening = State == BridgeState.Listening;
                State = BridgeState.Stopped;
                string url = Url;
                // answer what is pending (503) before the listener closes its connections
                dispatcher.Shutdown();
                if (wasListening) { try { transport.Stop(); } catch (Exception e) { Report(e, "transport stop"); } }
                Frames.Clear();
                DeleteDiscoveryFiles();
                LogLine(BridgeLogLevel.Info, "bridge: stopped (" + url + ")");
            }
        }

        public void Dispose() => Stop();

        static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }

        void WriteDiscoveryFiles()
        {
            string dir = Options.DiscoveryDir();
            if (dir == null) return;
            try
            {
                Directory.CreateDirectory(dir);
                if (Token != null)
                {
                    tokenPath = Path.Combine(dir, "token");
                    WriteToken(tokenPath, Token + "\n", TokenFileAcl.Restricts(Options.TokenAcl, dir));
                }
                if (Options.WriteEndpointFile)
                {
                    endpointPath = Path.Combine(dir, "endpoint.json");
                    var o = new JObject
                    {
                        ["url"] = Url,
                        ["pid"] = Process.GetCurrentProcess().Id,
                        ["startedAt"] = StartedUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
                        ["tokenRequired"] = Token != null,
                        ["protocol"] = BridgeProtocol.Version,
                        ["app"] = Options.AppName,
                    };
                    WriteAtomic(endpointPath, o.ToString(Formatting.Indented) + "\n");
                }
            }
            catch (Exception e) { LogLine(BridgeLogLevel.Warning, "bridge: cannot write the discovery files under " + dir + ": " + e.Message); }
        }

        void DeleteDiscoveryFiles()
        {
            try
            {
                if (endpointPath != null && File.Exists(endpointPath))
                {
                    // only our own file (another instance may have replaced it)
                    var o = BridgeJson.Parse(File.ReadAllText(endpointPath)) as JObject;
                    if (o != null && (int?)o["pid"] == Process.GetCurrentProcess().Id && (string)o["startedAt"] == StartedUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture))
                        File.Delete(endpointPath);
                }
            }
            catch (Exception e) { LogLine(BridgeLogLevel.Warning, "bridge: cannot remove endpoint.json: " + e.Message); }
            endpointPath = null;
        }

        /// <summary>
        /// Writes the token file atomically before endpoint.json names it. With <paramref name="restrict"/> the temporary file is
        /// created empty, given the owner-only list, then filled and moved into place (a move keeps the list), so the token is
        /// never readable through inherited entries; when the list cannot be set the file keeps the folder's permissions and a
        /// warning is logged.
        /// </summary>
        void WriteToken(string path, string text, bool restrict)
        {
            string tmp = path + ".tmp" + Process.GetCurrentProcess().Id;
            TokenAcl = TokenFileAcl.StateInherited;
            if (restrict)
            {
                File.WriteAllText(tmp, "", Utf8);
                string why = TokenFileAcl.RestrictToOwner(tmp);
                if (why == null) TokenAcl = TokenFileAcl.StateOwnerOnly;
                else LogLine(BridgeLogLevel.Warning, "bridge: the token file keeps its folder's permissions (" + why + ")");
            }
            File.WriteAllText(tmp, text, Utf8);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        static void WriteAtomic(string path, string text)
        {
            string tmp = path + ".tmp" + Process.GetCurrentProcess().Id;
            File.WriteAllText(tmp, text, Utf8);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        // ------------------------------------------------------------------ frames (main thread)

        /// <summary>The bridge pump: call once per frame on the main thread (Update, execution order −10000).</summary>
        public void Pump(int frame) => dispatcher.Pump(frame);

        /// <summary>The late point: call once per frame on the main thread (LateUpdate, execution order 32000).</summary>
        public void PumpLate(int frame) => dispatcher.PumpLate(frame);

        /// <summary>One manual frame for tests without a player loop: Pump and PumpLate of the next frame number.</summary>
        public int ManualFrame()
        {
            int f = Math.Max(manualFrame + 1, Frames.Frame + 1);
            manualFrame = f;
            Pump(f);
            PumpLate(f);
            return f;
        }

        /// <summary>Publishes the host's clock and screen for ping (called by the Unity host each frame).</summary>
        public void SetHostInfo(double realtimeSinceStartup, int screenWidth, int screenHeight)
        {
            Interlocked.Exchange(ref hostRealtimeTicks, Stopwatch.GetTimestamp());
            Volatile.Write(ref hostRealtime, realtimeSinceStartup);
            screenW = screenWidth;
            screenH = screenHeight;
        }

        internal double Realtime
        {
            get
            {
                double r = Volatile.Read(ref hostRealtime);
                if (r < 0) return Frames.Realtime;
                return r + (Stopwatch.GetTimestamp() - Interlocked.Read(ref hostRealtimeTicks)) / (double)Stopwatch.Frequency;
            }
        }

        internal string Screen => screenW + "x" + screenH;

        // ------------------------------------------------------------------ in-process submission

        /// <summary>
        /// Submits a request without HTTP (in-process tests and tools): the task completes with the reply JSON when the
        /// pump has run it. The safety gate does not apply; the size limit and parsing rules do.
        /// </summary>
        public Task<string> SubmitAsync(string requestJson)
        {
            var tcs = new TaskCompletionSource<string>();
            Submit(Utf8.GetBytes(requestJson ?? ""), (status, json) => tcs.TrySetResult(json));
            return tcs.Task;
        }

        /// <summary>Submits and pumps manual frames on this thread until the reply arrives (or <paramref name="maxFrames"/> pass).</summary>
        public string Execute(string requestJson, int maxFrames = 1000)
        {
            var t = SubmitAsync(requestJson);
            for (int i = 0; i < maxFrames && !t.IsCompleted; i++) ManualFrame();
            return t.IsCompleted ? t.Result : null;
        }

        // ------------------------------------------------------------------ HTTP side (worker threads)

        void OnExchange(IBridgeExchange ex)
        {
            if (State != BridgeState.Listening || dispatcher.IsShutDown)
            {
                RespondText(ex, 503, "service unavailable: the bridge is shutting down");
                return;
            }
            var gate = RequestGate.Check(ex, Port, Token, Options.MaxBodyBytes);
            if (gate != null)
            {
                RespondText(ex, gate.Value.status, gate.Value.text);
                return;
            }
            byte[] body;
            try { body = ReadBody(ex.Body, Options.MaxBodyBytes); }
            catch (Exception e)
            {
                RespondText(ex, 400, "bad request: " + e.Message);
                return;
            }
            if (body == null)
            {
                RespondText(ex, 413, "payload too large: the body limit is " + Options.MaxBodyBytes + " bytes");
                return;
            }
            var headers = standardHeaders;
            Submit(body, (status, json) =>
            {
                // a large reply finished on the main thread is encoded and written from a worker, so a slow client or a
                // 10 MB document never stalls the frame; small replies go out at once (AfterReply then runs after them)
                if (json.Length > InlineReplyChars && Thread.CurrentThread.ManagedThreadId == dispatcher.MainThreadId)
                    ThreadPool.QueueUserWorkItem(_ => ex.Respond(status, "application/json; charset=utf-8", Utf8.GetBytes(json), headers));
                else ex.Respond(status, "application/json; charset=utf-8", Utf8.GetBytes(json), headers);
            });
        }

        /// <summary>Replies longer than this (characters) are written from a worker thread when they finish on the main thread.</summary>
        const int InlineReplyChars = 32 * 1024;

        void RespondText(IBridgeExchange ex, int status, string text) =>
            ex.Respond(status, "text/plain; charset=utf-8", Utf8.GetBytes(text + "\n"), standardHeaders);

        /// <summary>The whole body, or null when it exceeds <paramref name="max"/> bytes.</summary>
        static byte[] ReadBody(Stream s, long max)
        {
            if (s == null) return new byte[0];
            using (var ms = new MemoryStream())
            {
                var buf = new byte[81920];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    if (ms.Length + n > max) return null;
                    ms.Write(buf, 0, n);
                }
                return ms.ToArray();
            }
        }

        /// <summary>Parses a body and answers inline or queues a job. <paramref name="respond"/> gets (HTTP status, JSON).</summary>
        void Submit(byte[] body, Action<int, string> respond)
        {
            if (body.Length > Options.MaxBodyBytes)
            {
                respond(413, BridgeJson.Write(Dispatcher.ErrorReply(null, null, new BridgeError(BridgeCodes.BadArgs, "the body exceeds " + Options.MaxBodyBytes + " bytes"), null)));
                return;
            }
            JObject req;
            string text;
            try
            {
                text = Utf8.GetString(body);
                if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            }
            catch (Exception) { text = null; }
            if (text == null || text.Trim().Length == 0) req = new JObject { ["cmd"] = "ping" };
            else
            {
                JToken tok;
                try { tok = BridgeJson.Parse(text); }
                catch (Exception e)
                {
                    respond(400, BridgeJson.Write(Dispatcher.ErrorReply(null, null, new BridgeError(BridgeCodes.BadJson, "the body is not valid JSON: " + e.Message), null)));
                    return;
                }
                req = tok as JObject;
                if (req == null)
                {
                    respond(400, BridgeJson.Write(Dispatcher.ErrorReply(null, null, new BridgeError(BridgeCodes.BadJson, "the body must be a JSON object {\"cmd\": ...}"), null)));
                    return;
                }
            }
            string idJson = req.TryGetValue(EnvelopeFields.Id, StringComparison.Ordinal, out var idTok) ? idTok.ToString(Formatting.None) : null;
            if (!req.TryGetValue(EnvelopeFields.Cmd, StringComparison.Ordinal, out var cmdTok) || cmdTok.Type != JTokenType.String || ((string)cmdTok).Length == 0)
            {
                respond(200, BridgeJson.Write(Dispatcher.ErrorReply(null, idJson, BridgeError.Missing(EnvelopeFields.Cmd, ArgTypes.String), null)));
                return;
            }
            string cmd = (string)cmdTok;
            var entry = Commands.Lookup(cmd, out _);
            bool isBatch = entry != null && entry.Spec.Name == "batch";
            var job = new Job(req, entry?.Spec.Name ?? cmd, isBatch, respond) { Clock = Frames };
            // not_ready is answered here, off the main thread, so a busy main thread never delays it (gate checked
            // again when the job runs)
            if (!isBatch)
            {
                var gateErr = dispatcher.PreGate(cmd, entry);
                if (gateErr != null)
                {
                    respond(200, BridgeJson.Write(Dispatcher.ErrorReply(job.Cmd, idJson, gateErr, null)));
                    return;
                }
            }
            // timeout
            double timeout;
            if (req.TryGetValue(EnvelopeFields.Timeout, StringComparison.Ordinal, out var toTok) && toTok.Type != JTokenType.Null)
            {
                if ((toTok.Type != JTokenType.Integer && toTok.Type != JTokenType.Float) || (double)toTok <= 0 || (double)toTok > Options.MaxTimeoutSec)
                {
                    respond(200, BridgeJson.Write(Dispatcher.ErrorReply(job.Cmd, idJson,
                        BridgeError.BadArgs(EnvelopeFields.Timeout, "seconds (0 < timeout ≤ " + Options.MaxTimeoutSec.ToString(CultureInfo.InvariantCulture) + ")"), null)));
                    return;
                }
                timeout = (double)toTok;
            }
            else timeout = isBatch ? -1 : DefaultTimeout(entry?.Spec);
            if (isBatch)
            {
                var err = BuildBatch(job, req, ref timeout);
                if (err != null)
                {
                    respond(200, BridgeJson.Write(Dispatcher.ErrorReply("batch", idJson, err, null)));
                    return;
                }
            }
            else job.Members.Add(new Member { Request = req, Index = 0, Cmd = cmd });
            job.TimeoutSec = timeout;
            int wf = Dispatcher.EnvInt(req, EnvelopeFields.WaitFrames, 0, out _);
            if (!isBatch && entry != null && entry.Inline && wf <= 0)
            {
                dispatcher.RunInline(job);
                return;
            }
            if (!dispatcher.Enqueue(job))
            {
                var e = dispatcher.IsShutDown ? new BridgeError(BridgeCodes.NotReady, "the bridge is shutting down")
                    : BridgeError.Busy("the request queue is full (" + Options.QueueLimit + " requests)");
                respond(dispatcher.IsShutDown ? 503 : 200, BridgeJson.Write(Dispatcher.ErrorReply(job.Cmd, idJson, e, null)));
            }
        }

        double DefaultTimeout(CommandSpec spec) =>
            Math.Min(Options.MaxTimeoutSec, spec != null && spec.DefaultTimeoutSec > 0 ? spec.DefaultTimeoutSec : Options.DefaultTimeoutSec);

        /// <summary>Validates a batch request and fills the job's members; null when valid.</summary>
        BridgeError BuildBatch(Job job, JObject req, ref double timeout)
        {
            if (!req.TryGetValue("cmds", StringComparison.Ordinal, out var cmdsTok) || !(cmdsTok is JArray cmds))
                return BridgeError.BadArgs("cmds", ArgTypes.ArrayOf(ArgTypes.Object), "batch needs cmds: [{\"cmd\": ...}, ...]");
            job.StopOnError = Dispatcher.EnvBool(req, "stopOnError", true, out var soErr);
            if (soErr != null) return BridgeError.BadArgs("stopOnError", ArgTypes.Bool);
            var names = new HashSet<string>(StringComparer.Ordinal);
            double sum = 0;
            for (int i = 0; i < cmds.Count; i++)
            {
                if (!(cmds[i] is JObject m)) return BridgeError.BadArgs("cmds[" + i + "]", ArgTypes.Object, "cmds[" + i + "] is not an object");
                if (!m.TryGetValue(EnvelopeFields.Cmd, StringComparison.Ordinal, out var c) || c.Type != JTokenType.String || ((string)c).Length == 0)
                    return BridgeError.Missing("cmds[" + i + "].cmd", ArgTypes.String);
                var e = Commands.Lookup((string)c, out _);
                if (e != null && e.Spec.Name == "batch") return BridgeError.BadArgs("cmds[" + i + "].cmd", "a command other than batch", "a batch cannot contain a batch");
                string asName = null;
                if (m.TryGetValue(EnvelopeFields.As, StringComparison.Ordinal, out var a) && a.Type != JTokenType.Null)
                {
                    if (a.Type != JTokenType.String || !IsPlainName((string)a))
                        return BridgeError.BadArgs("cmds[" + i + "].as", "a name ([A-Za-z][A-Za-z0-9_]*)");
                    asName = (string)a;
                    if (!names.Add(asName)) return BridgeError.BadArgs("cmds[" + i + "].as", "a name not used before in the batch", "the name '" + asName + "' is used twice");
                }
                sum += DefaultTimeout(e?.Spec);
                job.Members.Add(new Member { Request = m, Index = i, Cmd = (string)c, As = asName });
            }
            if (timeout < 0) timeout = Math.Min(Options.MaxTimeoutSec, Math.Max(Options.DefaultTimeoutSec, sum));
            return null;
        }

        static bool IsPlainName(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > 64 || !char.IsLetter(s[0])) return false;
            foreach (char ch in s) if (!(char.IsLetterOrDigit(ch) || ch == '_')) return false;
            return true;
        }

        // ------------------------------------------------------------------ logging

        internal void LogLine(BridgeLogLevel level, string message)
        {
            try { Options.Logger?.Invoke(level, message); }
            catch (Exception) { }
        }

        internal void Report(Exception e, string what) => LogLine(BridgeLogLevel.Warning, "bridge: " + what + " threw " + e);
    }
}

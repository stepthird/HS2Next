// Shared parts of the bridge tests: a check collector, a raw
// HTTP/1.1 client over TcpClient (so tests control every header, including Host, Origin and Content-Length), a
// background "main thread" that pumps manual frames, JSON helpers and servers on ephemeral ports. Unity-free: the
// same groups run under plain Mono (Tools/mono_tests.ps1 -Suite bridge) and in the Editor (BridgeTest.Run).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using HS2Next.Api.Bridge;
using Newtonsoft.Json.Linq;

namespace HS2Next.Bridge.Tests
{
    /// <summary>Counts checks; collects FAIL and info lines for the report.</summary>
    public sealed class BridgeChecks
    {
        public int Passed, Failed;
        public string Group;
        public readonly List<string> Lines = new List<string>();
        public string TempDir;
        public Action<string> Echo = Console.WriteLine;

        public bool Check(bool ok, string what)
        {
            if (ok) { Passed++; return true; }
            Failed++;
            string msg = Group + ": FAIL " + what;
            lock (Lines) Lines.Add(msg);
            Echo?.Invoke("  " + msg);
            return false;
        }

        public bool Equal<T>(T expected, T actual, string what)
        {
            bool ok = EqualityComparer<T>.Default.Equals(expected, actual);
            return Check(ok, ok ? what : what + " (expected " + Show(expected) + ", got " + Show(actual) + ")");
        }

        static string Show(object o) => o == null ? "null" : o is string s ? "\"" + (s.Length > 400 ? s.Substring(0, 400) + "…" : s) + "\"" : o.ToString();

        public void Info(string line)
        {
            lock (Lines) Lines.Add(Group + ": " + line);
            Echo?.Invoke("  " + Group + ": " + line);
        }

        public T Throws<T>(Action a, string what) where T : Exception
        {
            try { a(); }
            catch (T e) { Check(true, what); return e; }
            catch (Exception e) { Check(false, what + " (threw " + e.GetType().Name + ": " + e.Message + ")"); return null; }
            Check(false, what + " (no exception)");
            return null;
        }
    }

    /// <summary>A raw HTTP response.</summary>
    public sealed class RawResponse
    {
        public int Status;
        public readonly Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string Body;
        public string Error;
        public JObject Json { get { try { return JObject.Parse(Body); } catch (Exception) { return null; } } }
        public override string ToString() => Status + " " + (Body ?? Error);
    }

    public static class Http
    {
        /// <summary>
        /// Sends one request with exactly the given headers (Host defaults to 127.0.0.1:port; Content-Length from the body
        /// unless given) and reads the whole response (Connection: close).
        /// </summary>
        public static RawResponse Send(int port, string method, string path, string body, IDictionary<string, string> headers = null,
                                       int timeoutMs = 30000, bool sendBody = true)
        {
            var r = new RawResponse();
            try
            {
                using (var c = new TcpClient())
                {
                    c.NoDelay = true;
                    c.ReceiveTimeout = timeoutMs;
                    c.SendTimeout = timeoutMs;
                    c.Connect("127.0.0.1", port);
                    var s = c.GetStream();
                    byte[] payload = body == null ? new byte[0] : Encoding.UTF8.GetBytes(body);
                    var h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    h["Host"] = "127.0.0.1:" + port;
                    if (body != null || method == "POST") h["Content-Length"] = payload.Length.ToString();
                    h["Connection"] = "close";
                    if (headers != null) foreach (var kv in headers) { if (kv.Value == null) h.Remove(kv.Key); else h[kv.Key] = kv.Value; }
                    var sb = new StringBuilder();
                    sb.Append(method).Append(' ').Append(path).Append(" HTTP/1.1\r\n");
                    foreach (var kv in h) sb.Append(kv.Key).Append(": ").Append(kv.Value).Append("\r\n");
                    sb.Append("\r\n");
                    var head = Encoding.ASCII.GetBytes(sb.ToString());
                    s.Write(head, 0, head.Length);
                    if (sendBody && payload.Length > 0)
                    {
                        try { s.Write(payload, 0, payload.Length); }
                        catch (IOException) { }   // the server may answer (413) and close before the body is sent
                    }
                    var ms = new MemoryStream();
                    var buf = new byte[65536];
                    try
                    {
                        int n;
                        while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                    }
                    catch (IOException) { }
                    Parse(ms.ToArray(), r);
                }
            }
            catch (Exception e) { r.Error = e.GetType().Name + ": " + e.Message; }
            return r;
        }

        static void Parse(byte[] raw, RawResponse r)
        {
            int sep = -1;
            for (int i = 0; i + 3 < raw.Length; i++)
                if (raw[i] == '\r' && raw[i + 1] == '\n' && raw[i + 2] == '\r' && raw[i + 3] == '\n') { sep = i; break; }
            if (sep < 0) { r.Error = "no HTTP response (" + raw.Length + " bytes)"; return; }
            var head = Encoding.ASCII.GetString(raw, 0, sep).Split(new[] { "\r\n" }, StringSplitOptions.None);
            var first = head[0].Split(' ');
            if (first.Length >= 2) int.TryParse(first[1], out r.Status);
            for (int i = 1; i < head.Length; i++)
            {
                int c = head[i].IndexOf(':');
                if (c > 0) r.Headers[head[i].Substring(0, c).Trim()] = head[i].Substring(c + 1).Trim();
            }
            byte[] body = new byte[raw.Length - sep - 4];
            Array.Copy(raw, sep + 4, body, 0, body.Length);
            if (r.Headers.TryGetValue("Transfer-Encoding", out var te) && te.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0) body = Dechunk(body);
            r.Body = Encoding.UTF8.GetString(body);
        }

        static byte[] Dechunk(byte[] b)
        {
            var ms = new MemoryStream();
            int p = 0;
            while (p < b.Length)
            {
                int e = p;
                while (e + 1 < b.Length && !(b[e] == '\r' && b[e + 1] == '\n')) e++;
                int len = Convert.ToInt32(Encoding.ASCII.GetString(b, p, e - p).Split(';')[0].Trim(), 16);
                p = e + 2;
                if (len == 0) break;
                ms.Write(b, p, Math.Min(len, b.Length - p));
                p += len + 2;
            }
            return ms.ToArray();
        }

        /// <summary>POST / with a JSON body.</summary>
        public static RawResponse Post(int port, string json, IDictionary<string, string> headers = null, int timeoutMs = 30000) =>
            Send(port, "POST", "/", json, headers, timeoutMs);

        /// <summary>POST and parse; null on a transport failure or non-JSON body.</summary>
        public static JObject Call(int port, string json, int timeoutMs = 30000) => Post(port, json, null, timeoutMs).Json;
    }

    /// <summary>A background thread that plays the main thread: ManualFrame every <c>periodMs</c>.</summary>
    public sealed class PumpThread : IDisposable
    {
        readonly BridgeServer server;
        readonly Thread thread;
        volatile bool stop;
        public volatile int Frames;

        public PumpThread(BridgeServer server, int periodMs = 1)
        {
            this.server = server;
            thread = new Thread(() =>
            {
                while (!stop)
                {
                    server.ManualFrame();
                    Frames++;
                    if (periodMs > 0) Thread.Sleep(periodMs);
                }
            }) { IsBackground = true, Name = "bridge test pump" };
            thread.Start();
        }

        public void Dispose()
        {
            stop = true;
            thread.Join(5000);
        }
    }

    public static class Kit
    {
        /// <summary>A server on a free port with test-friendly options (no discovery files unless a user-data root is given).</summary>
        public static BridgeServer NewServer(Action<BridgeOptions> configure = null, bool start = true, List<string> log = null)
        {
            var o = new BridgeOptions
            {
                Port = 0,
                RequiredStages = new string[0],
                OptionalStages = new string[0],
                AppVersion = "test",
                UnityVersion = "none",
                Logger = (lvl, msg) => { if (log != null) lock (log) log.Add(lvl + ": " + msg); },
            };
            configure?.Invoke(o);
            var s = new BridgeServer(o);
            if (start && !s.Start()) throw new InvalidOperationException("bridge did not start: " + s.LastError);
            return s;
        }

        /// <summary>Submits in-process and steps manual frames until the reply (null after maxFrames).</summary>
        public static JObject Exec(BridgeServer s, string json, int maxFrames = 200)
        {
            var t = s.SubmitAsync(json);
            var sw = Stopwatch.StartNew();
            // frames, and real time for completions on other threads (thread-pool continuations)
            for (int i = 0; !t.IsCompleted && (i < maxFrames || sw.ElapsedMilliseconds < 3000); i++)
            {
                s.ManualFrame();
                if (i >= maxFrames) Thread.Sleep(1);
            }
            return t.IsCompleted ? JObject.Parse(t.Result) : null;
        }

        public static JObject Exec(BridgeServer s, JObject req, int maxFrames = 200) => Exec(s, req.ToString(Newtonsoft.Json.Formatting.None), maxFrames);

        public static string Str(JToken t, string path)
        {
            var v = t?.SelectToken(path);
            return v == null ? null : v.Type == JTokenType.String ? (string)v : v.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static bool Ok(JObject r) => r != null && r["ok"]?.Type == JTokenType.Boolean && (bool)r["ok"];

        public static string Code(JObject r) => (string)r?["code"];

        /// <summary>Waits until <paramref name="cond"/> or the timeout; returns cond().</summary>
        public static bool WaitFor(Func<bool> cond, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (!cond())
            {
                if (sw.ElapsedMilliseconds > timeoutMs) return cond();
                Thread.Sleep(2);
            }
            return true;
        }

        /// <summary>Registers an "echo" command that returns its arguments (minus the envelope) and counts calls.</summary>
        public static int[] RegisterEcho(BridgeServer s, string name = "test.echo", Action<CommandSpec> spec = null)
        {
            var calls = new int[1];
            var sp = new CommandSpec(name, "echo") { AllowExtraArgs = true, Gate = ReadyGate.None };
            spec?.Invoke(sp);
            s.Commands.Register(sp, ctx =>
            {
                Interlocked.Increment(ref calls[0]);
                var r = new BridgeReply();
                foreach (var n in ctx.Args.Names)
                    if (!EnvelopeFields.Contains(n)) r[n] = new RawJson(ctx.Args.RawJson(n));
                r["frame"] = ctx.Frames.Frame;
                return r;
            });
            return calls;
        }
    }
}

namespace HS2Next.Bridge.Tests
{
    /// <summary>The test groups in run order (shared by the plain-Mono entry and BridgeTest.Run in the Editor).</summary>
    public static class BridgeTestGroups
    {
        public static List<KeyValuePair<string, Action<BridgeChecks>>> All() => new List<KeyValuePair<string, Action<BridgeChecks>>>
        {
            new KeyValuePair<string, Action<BridgeChecks>>("Envelope", EnvelopeGroup.Run),
            new KeyValuePair<string, Action<BridgeChecks>>("Errors", ErrorsGroup.Run),
            new KeyValuePair<string, Action<BridgeChecks>>("Numbers", NumbersGroup.Run),
            new KeyValuePair<string, Action<BridgeChecks>>("Args", ArgsGroup.Run),
            new KeyValuePair<string, Action<BridgeChecks>>("Batch", BatchGroup.Run),
            new KeyValuePair<string, Action<BridgeChecks>>("Help", HelpGroup.Run),
            new KeyValuePair<string, Action<BridgeChecks>>("Frames", FramesGroup.Run),
            new KeyValuePair<string, Action<BridgeChecks>>("Output", OutputGroup.Run),
            new KeyValuePair<string, Action<BridgeChecks>>("Safety", SafetyGroup.Run),
            new KeyValuePair<string, Action<BridgeChecks>>("Lifecycle", LifecycleGroup.Run),
            new KeyValuePair<string, Action<BridgeChecks>>("Load", LoadGroup.Run),
            new KeyValuePair<string, Action<BridgeChecks>>("Inline", InlineGroup.Run),
            new KeyValuePair<string, Action<BridgeChecks>>("Build", BuildGroup.Run),
            new KeyValuePair<string, Action<BridgeChecks>>("Token", TokenGroup.Run),
        };
    }
}

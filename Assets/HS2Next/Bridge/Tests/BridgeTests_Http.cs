// Bridge tests over real HTTP on an ephemeral port: safety headers,
// status codes, the size limit, the token, the lifecycle (bind retry, restart, discovery files) and load (1,000
// sequential pings, 4 concurrent clients, answers while the main thread is busy).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using HS2Next.Api.Bridge;
using Newtonsoft.Json.Linq;

namespace HS2Next.Bridge.Tests
{
    public static class SafetyGroup
    {
        public static void Run(BridgeChecks t)
        {
            using (var s = Kit.NewServer(o => o.MaxBodyBytes = 64 * 1024))
            using (var pump = new PumpThread(s))
            {
                var calls = Kit.RegisterEcho(s);
                int port = s.Port;
                var ok = Http.Post(port, "{\"cmd\":\"test.echo\",\"v\":1}");
                t.Equal(200, ok.Status, "plain POST answered 200");
                t.Check(Kit.Ok(ok.Json), "plain POST ok");
                t.Equal("no-store", ok.Headers.TryGetValue("Cache-Control", out var cc) ? cc : null, "Cache-Control: no-store");
                t.Equal(BridgeVersion.Semver, ok.Headers.TryGetValue(BridgeProtocol.BridgeHeader, out var bv) ? bv : null, "X-HS2Next-Bridge header");
                t.Check(ok.Headers.TryGetValue("Content-Type", out var ct) && ct.StartsWith("application/json"), "JSON content type");
                int before = calls[0];

                foreach (var h in new[] { "Origin", "Sec-Fetch-Site", "Sec-Fetch-Mode", "Sec-Fetch-Dest", "Sec-Fetch-User" })
                {
                    var r = Http.Post(port, "{\"cmd\":\"test.echo\"}", new Dictionary<string, string> { [h] = h == "Origin" ? "http://evil.example" : "cross-site" });
                    t.Equal(403, r.Status, h + " header -> 403");
                }
                var empty = Http.Post(port, "{\"cmd\":\"test.echo\"}", new Dictionary<string, string> { ["Origin"] = "" });
                t.Equal(403, empty.Status, "an empty Origin header -> 403");
                var nullOrigin = Http.Post(port, "{\"cmd\":\"test.echo\"}", new Dictionary<string, string> { ["Origin"] = "null" });
                t.Equal(403, nullOrigin.Status, "Origin: null -> 403");
                var wrongHost = Http.Post(port, "{\"cmd\":\"test.echo\"}", new Dictionary<string, string> { ["Host"] = "evil.example:" + port });
                t.Check(wrongHost.Status == 403 || wrongHost.Status == 400, "wrong Host rejected (" + wrongHost.Status + ")");
                t.Info("wrong Host -> " + wrongHost.Status + " (400 = Mono's listener refused it before the bridge; 403 = the bridge's check)");
                var wrongPort = Http.Post(port, "{\"cmd\":\"test.echo\"}", new Dictionary<string, string> { ["Host"] = "127.0.0.1:1" });
                t.Check(wrongPort.Status == 403 || wrongPort.Status == 400, "Host with another port rejected (" + wrongPort.Status + ")");
                var noHost = Http.Send(port, "POST", "/", "{\"cmd\":\"test.echo\"}", new Dictionary<string, string> { ["Host"] = null });
                t.Check(noHost.Status == 403 || noHost.Status == 400, "no Host header rejected (" + noHost.Status + ")");
                var localhost = Http.Post(port, "{\"cmd\":\"test.echo\"}", new Dictionary<string, string> { ["Host"] = "localhost:" + port });
                t.Info("Host localhost:<port> -> " + localhost.Status + (localhost.Status == 400 ? " (Mono's listener matches the 127.0.0.1 prefix by host name)" : ""));
                t.Equal(before, calls[0], "no rejected request reached the handler");

                var get = Http.Send(port, "GET", "/", null);
                t.Equal(405, get.Status, "GET -> 405");
                t.Check(get.Body?.Contains("POST") == true, "405 carries a one-line hint");
                t.Equal(405, Http.Send(port, "PUT", "/", "{}").Status, "PUT -> 405");
                var options = Http.Send(port, "OPTIONS", "/", null);
                t.Equal(405, options.Status, "OPTIONS (preflight without Origin) -> 405");
                var preflight = Http.Send(port, "OPTIONS", "/", null, new Dictionary<string, string> { ["Origin"] = "http://evil.example", ["Access-Control-Request-Method"] = "POST" });
                t.Equal(403, preflight.Status, "CORS preflight -> 403");
                t.Equal(404, Http.Send(port, "POST", "/x", "{\"cmd\":\"ping\"}").Status, "POST /x -> 404");
                bool anyCors = false;
                foreach (var r in new[] { ok, get, options, preflight, wrongHost, empty })
                    foreach (var k in r.Headers.Keys) if (k.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase)) anyCors = true;
                t.Check(!anyCors, "no Access-Control-* header on any reply");

                // size limits (64 KiB in this server)
                var big = Http.Send(port, "POST", "/", new string(' ', 10), new Dictionary<string, string> { ["Content-Length"] = (64 * 1024 + 1).ToString() }, sendBody: false);
                t.Equal(413, big.Status, "declared Content-Length over the limit -> 413 (no body read)");
                var exact = new StringBuilder("{\"cmd\":\"test.echo\",\"pad\":\"");
                exact.Append('x', 64 * 1024 - exact.Length - 2).Append("\"}");
                var atLimit = Http.Post(port, exact.ToString());
                t.Check(atLimit.Status == 200 && Kit.Ok(atLimit.Json), "a body of exactly the limit is accepted (" + exact.Length + " bytes)");
                var chunked = ChunkedPost(port, 64 * 1024 + 100);
                t.Equal(413, chunked, "chunked body over the limit -> 413");
                var badJson = Http.Post(port, "{\"cmd\":");
                t.Equal(400, badJson.Status, "unparsable body -> 400");
                t.Equal(BridgeCodes.BadJson, Kit.Code(badJson.Json), "400 body is JSON with code bad_json");
                var emptyBody = Http.Post(port, "");
                t.Check(emptyBody.Status == 200 && (string)emptyBody.Json?["cmd"] == "ping", "empty body over HTTP = ping");
                var formType = Http.Post(port, "{\"cmd\":\"ping\"}", new Dictionary<string, string> { ["Content-Type"] = "application/x-www-form-urlencoded" });
                t.Check(Kit.Ok(formType.Json), "Content-Type is ignored (curl -d sends a form type)");
                var errReply = Http.Post(port, "{\"cmd\":\"no.such\"}");
                t.Equal(200, errReply.Status, "command errors are HTTP 200");
            }

            // 16 MiB default limit: declared length only, and a 10 MB body round trip
            using (var s = Kit.NewServer())
            using (var pump = new PumpThread(s))
            {
                Kit.RegisterEcho(s);
                var big = Http.Send(s.Port, "POST", "/", "{}", new Dictionary<string, string> { ["Content-Length"] = (BridgeProtocol.MaxBodyBytes + 1).ToString() }, sendBody: false);
                t.Equal(413, big.Status, "16 MiB + 1 declared -> 413");
                var sb = new StringBuilder("{\"cmd\":\"test.echo\",\"text\":\"");
                var chunk = "日本語-ü-0123456789";
                while (sb.Length < 7_000_000) sb.Append(chunk);
                sb.Append("\",\"f\":0.1}");
                var sw = Stopwatch.StartNew();
                var r = Http.Post(s.Port, sb.ToString(), null, 60000);
                sw.Stop();
                var j = r.Json;
                int bytes = Encoding.UTF8.GetByteCount(sb.ToString());
                t.Check(r.Status == 200 && Kit.Ok(j), "large body (" + bytes / 1_000_000.0 + " MB UTF-8) answered");
                t.Check(j != null && ((string)j["text"]).Length == sb.Length - "{\"cmd\":\"test.echo\",\"text\":\"".Length - "\",\"f\":0.1}".Length, "large non-ASCII body round trip");
                t.Info("large body round trip " + bytes + " bytes in " + sw.ElapsedMilliseconds + " ms");
            }

            // token
            using (var s = Kit.NewServer(o => { o.RequireToken = true; o.Token = "secret-token"; }))
            using (var pump = new PumpThread(s))
            {
                t.Equal(401, Http.Post(s.Port, "{\"cmd\":\"ping\"}").Status, "token required, none sent -> 401");
                t.Equal(401, Http.Post(s.Port, "{\"cmd\":\"ping\"}", new Dictionary<string, string> { [BridgeProtocol.TokenHeader] = "wrong" }).Status, "wrong token -> 401");
                t.Equal(401, Http.Post(s.Port, "{\"cmd\":\"ping\"}", new Dictionary<string, string> { [BridgeProtocol.TokenHeader] = "secret-token-x" }).Status, "longer token -> 401");
                var r = Http.Post(s.Port, "{\"cmd\":\"ping\"}", new Dictionary<string, string> { [BridgeProtocol.TokenHeader] = "secret-token" });
                t.Check(r.Status == 200 && Kit.Ok(r.Json), "right token -> 200");
                var v = Http.Post(s.Port, "{\"cmd\":\"bridge.version\"}", new Dictionary<string, string> { [BridgeProtocol.TokenHeader] = "secret-token" });
                t.Equal("true", Kit.Str(v.Json, "tokenRequired"), "bridge.version reports tokenRequired");
            }
            using (var s = Kit.NewServer(o => o.RequireToken = true))
            {
                t.Check(s.Token != null && s.Token.Length == 32, "a generated token has 128 bits (32 hex digits)");
            }
            t.Check(RequestGate.TokenEquals("abc", "abc") && !RequestGate.TokenEquals("abd", "abc") && !RequestGate.TokenEquals("ab", "abc") && !RequestGate.TokenEquals(null, "abc"), "TokenEquals");
            t.Check(RequestGate.HostOk("127.0.0.1:17322", 17322) && RequestGate.HostOk("LOCALHOST:17322", 17322) && !RequestGate.HostOk("127.0.0.1", 17322) && !RequestGate.HostOk("evil:17322", 17322), "HostOk");
        }

        static int ChunkedPost(int port, int total)
        {
            try
            {
                using (var c = new TcpClient())
                {
                    c.ReceiveTimeout = 10000;
                    c.Connect("127.0.0.1", port);
                    var s = c.GetStream();
                    var head = Encoding.ASCII.GetBytes("POST / HTTP/1.1\r\nHost: 127.0.0.1:" + port + "\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n");
                    s.Write(head, 0, head.Length);
                    int sent = 0;
                    var data = Encoding.ASCII.GetBytes(new string('x', 4096));
                    try
                    {
                        while (sent < total)
                        {
                            var size = Encoding.ASCII.GetBytes("1000\r\n");
                            s.Write(size, 0, size.Length);
                            s.Write(data, 0, data.Length);
                            s.Write(new byte[] { 13, 10 }, 0, 2);
                            sent += data.Length;
                        }
                        var end = Encoding.ASCII.GetBytes("0\r\n\r\n");
                        s.Write(end, 0, end.Length);
                    }
                    catch (IOException) { }
                    var ms = new MemoryStream();
                    var buf = new byte[4096];
                    try { int n; while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n); }
                    catch (IOException) { }
                    var txt = Encoding.ASCII.GetString(ms.ToArray());
                    var parts = txt.Split(' ');
                    return parts.Length > 1 && int.TryParse(parts[1], out int st) ? st : -1;
                }
            }
            catch (Exception) { return -2; }
        }
    }

    public static class LifecycleGroup
    {
        public static void Run(BridgeChecks t)
        {
            var log = new List<string>();
            int port = FreePort();
            // start / stop on the same port, with traffic in between
            var sw = Stopwatch.StartNew();
            int okCycles = 0;
            for (int i = 0; i < 20; i++)
            {
                using (var s = Kit.NewServer(o => o.Port = port, start: false, log: log))
                {
                    if (!s.Start()) { t.Check(false, "cycle " + i + ": start failed: " + s.LastError); break; }
                    var r = Http.Post(port, "{\"cmd\":\"ping\"}");
                    if (Kit.Ok(r.Json)) okCycles++;
                    s.Stop();
                    t.Check(!s.IsListening, "cycle " + i + ": not listening after Stop");
                }
            }
            t.Equal(20, okCycles, "20 start/ping/stop cycles on one port");
            t.Info("20 cycles in " + sw.ElapsedMilliseconds + " ms");
            t.Check(PortFree(port), "the port is free after the last stop (no orphaned listener)");
            t.Check(Kit.WaitFor(() => HttpListenerTransport.LiveAcceptThreads == 0, 3000), "no accept thread left after the cycles (" + HttpListenerTransport.LiveAcceptThreads + ")");
            var refused = Http.Post(port, "{\"cmd\":\"ping\"}", null, 2000);
            t.Check(refused.Status == 0 && refused.Error != null, "connection refused after stop");

            // bind retry: the port is taken for 300 ms
            var blocker = new TcpListener(IPAddress.Loopback, port);
            blocker.Start();
            var release = new Timer(_ => blocker.Stop(), null, 300, Timeout.Infinite);
            log.Clear();
            using (var s = Kit.NewServer(o => o.Port = port, start: false, log: log))
            {
                sw.Restart();
                bool started = s.Start();
                t.Check(started, "bind retry: started once the port was released (" + sw.ElapsedMilliseconds + " ms, " + s.LastError + ")");
                int warnings = 0;
                lock (log) foreach (var l in log) if (l.Contains("cannot listen")) warnings++;
                t.Check(warnings >= 1 && warnings <= 5, "bind retry: failed attempts logged (" + warnings + ")");
                t.Check(Kit.Ok(Http.Post(port, "{\"cmd\":\"ping\"}").Json), "bind retry: answers");
            }
            release.Dispose();
            // permanently taken
            var holder = new TcpListener(IPAddress.Loopback, port);
            holder.Start();
            using (var s = Kit.NewServer(o => o.Port = port, start: false))
            {
                sw.Restart();
                bool started = s.Start();
                long ms = sw.ElapsedMilliseconds;
                t.Check(!started && s.State == BridgeState.Failed && s.LastError != null, "port taken: Start false, State Failed, LastError");
                t.Check(ms >= 900 && ms < 3000, "port taken: gave up after 5 retries x 200 ms (" + ms + " ms)");
            }
            holder.Stop();

            // discovery files
            string root = Path.Combine(t.TempDir, "userdata_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            using (var s = Kit.NewServer(o => { o.UserDataRoot = root; o.RequireToken = true; }))
            {
                string ep = Path.Combine(root, "Bridge", "endpoint.json");
                string tok = Path.Combine(root, "Bridge", "token");
                t.Check(File.Exists(ep), "endpoint.json written at start");
                if (File.Exists(ep))
                {
                    var j = (JObject)Newtonsoft.Json.JsonConvert.DeserializeObject(File.ReadAllText(ep), new Newtonsoft.Json.JsonSerializerSettings { DateParseHandling = Newtonsoft.Json.DateParseHandling.None });
                    t.Equal(s.Url, (string)j["url"], "endpoint.json url");
                    t.Equal(Process.GetCurrentProcess().Id, (int)j["pid"], "endpoint.json pid");
                    t.Check(j["startedAt"] != null && ((string)j["startedAt"]).EndsWith("Z"), "endpoint.json startedAt (UTC)");
                    t.Equal(true, (bool)j["tokenRequired"], "endpoint.json tokenRequired");
                }
                t.Check(File.Exists(tok) && File.ReadAllText(tok).Trim() == s.Token, "token file holds the token");
                t.Equal(Path.Combine(Path.GetFullPath(root), "Bridge"), s.Output.Root, "default output root = <UserData>/Bridge");
                s.Stop();
                t.Check(!File.Exists(ep), "endpoint.json removed at stop");
            }
            using (var s = Kit.NewServer(o => { o.UserDataRoot = root; o.OutputDir = "out"; }))
            {
                t.Equal(Path.Combine(Path.GetFullPath(root), "out"), s.Output.Root, "relative OutputDir is under the user-data root");
                var other = new JObject { ["url"] = "http://127.0.0.1:1/", ["pid"] = 1, ["startedAt"] = "x", ["tokenRequired"] = false };
                File.WriteAllText(Path.Combine(root, "Bridge", "endpoint.json"), other.ToString());
                s.Stop();
                t.Check(File.Exists(Path.Combine(root, "Bridge", "endpoint.json")), "an endpoint.json of another instance is not removed");
            }

            // stop answers pending requests 503 (main thread not pumping)
            using (var s = Kit.NewServer())
            {
                Kit.RegisterEcho(s);
                RawResponse pending = null;
                var th = new Thread(() => pending = Http.Post(s.Port, "{\"cmd\":\"test.echo\"}", null, 10000));
                th.Start();
                Kit.WaitFor(() => s.QueueLength > 0, 3000);
                t.Equal(1, s.QueueLength, "a request is queued");
                s.Stop();
                th.Join(5000);
                t.Equal(503, pending?.Status ?? -1, "a request pending at stop -> 503");
                t.Equal(BridgeCodes.NotReady, Kit.Code(pending?.Json), "503 body: not_ready (shutting down)");
                t.Check(s.State == BridgeState.Stopped, "State Stopped");
                s.Stop();
                t.Check(true, "Stop is idempotent");
            }
            // restart of one instance
            using (var s = Kit.NewServer())
            {
                int p1 = s.Port;
                s.Stop();
                t.Check(s.Start() && Kit.Ok(Http.Post(s.Port, "{\"cmd\":\"ping\"}").Json), "a stopped server starts again");
                t.Check(p1 > 0, "port reported");
            }
            // options
            var oo = new BridgeOptions().ApplyCommandLine(new[] { "-noBridge", "-bridgePort", "18000", "-bridgeToken", "tk", "-other" });
            t.Check(!oo.Enabled && oo.Port == 18000 && oo.RequireToken && oo.Token == "tk", "ApplyCommandLine -noBridge -bridgePort -bridgeToken value");
            oo = new BridgeOptions { Enabled = false }.ApplyCommandLine(new[] { "-bridge", "-bridgeToken", "-x", "-bridgePort", "abc" });
            t.Check(oo.Enabled && oo.RequireToken && oo.Token == null && oo.Port == BridgeProtocol.DefaultPort, "ApplyCommandLine -bridge, -bridgeToken without value, bad port ignored");
            t.Check(new BridgeOptions().Enabled && new BridgeOptions().Port == 17322 && !new BridgeOptions().RequireToken, "defaults: enabled, 17322, no token");
            t.Check(new BridgeOptions().ResolveOutputDir() == null, "no user-data root and no output dir = no output root");
        }

        internal static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }

        internal static bool PortFree(int port)
        {
            try
            {
                var l = new TcpListener(IPAddress.Loopback, port);
                l.Start();
                l.Stop();
                return true;
            }
            catch (SocketException) { return false; }
        }
    }

    public static class LoadGroup
    {
        public static void Run(BridgeChecks t)
        {
            using (var s = Kit.NewServer())
            {
                Kit.RegisterEcho(s);
                // 1,000 sequential pings on the listener thread (no pump at all)
                var sw = Stopwatch.StartNew();
                int ok = 0;
                for (int i = 0; i < 1000; i++) if (Kit.Ok(Http.Post(s.Port, "{\"cmd\":\"ping\",\"id\":" + i + "}").Json)) ok++;
                t.Equal(1000, ok, "1,000 sequential pings without a running main thread");
                t.Info("1,000 pings in " + sw.ElapsedMilliseconds + " ms (" + (sw.ElapsedMilliseconds / 1000.0).ToString("0.00") + " ms each)");

                // ping, help, bridge.version, log.tail answered while a main-thread request waits in the queue
                var queued = s.SubmitAsync("{\"cmd\":\"test.echo\"}");
                t.Check(Kit.Ok(Http.Call(s.Port, "{\"cmd\":\"help\"}")) && Kit.Ok(Http.Call(s.Port, "{\"cmd\":\"bridge.version\"}")) && Kit.Ok(Http.Call(s.Port, "{\"cmd\":\"log.tail\"}")),
                    "help, bridge.version and log.tail answered while the main thread is busy");
                var p = Http.Call(s.Port, "{\"cmd\":\"ping\"}");
                t.Equal(1, (int?)p?["queued"] ?? -1, "ping reports the queued request");
                t.Check(!queued.IsCompleted, "the queued request waits for the pump");
                var wf = s.SubmitAsync("{\"cmd\":\"ping\",\"waitFrames\":1}");
                t.Check(!wf.IsCompleted, "ping with waitFrames goes through the queue");
                s.ManualFrame();
                s.ManualFrame();
                t.Check(queued.IsCompleted && wf.IsCompleted, "both answered by the pump");

                // 1,000 sequential main-thread commands with a pump thread
                using (var pump = new PumpThread(s, 0))
                {
                    sw.Restart();
                    ok = 0;
                    for (int i = 0; i < 1000; i++)
                    {
                        var r = Http.Post(s.Port, "{\"cmd\":\"test.echo\",\"id\":" + i + "}").Json;
                        if (Kit.Ok(r) && (int)r["id"] == i) ok++;
                    }
                    t.Equal(1000, ok, "1,000 sequential main-thread commands, ids matched");
                    t.Info("1,000 main-thread commands in " + sw.ElapsedMilliseconds + " ms, " + pump.Frames + " pump frames");

                    // 4 concurrent clients x 250 mixed requests
                    int clients = 4, each = 250;
                    var lost = new int[1];
                    var mismatched = new int[1];
                    var outOfOrder = new int[1];
                    var threads = new List<Thread>();
                    sw.Restart();
                    for (int c = 0; c < clients; c++)
                    {
                        int cc = c;
                        var th = new Thread(() =>
                        {
                            int lastSeen = -1;
                            for (int i = 0; i < each; i++)
                            {
                                string id = "\"c" + cc + "-" + i + "\"";
                                string cmd = i % 5 == 0 ? "ping" : i % 7 == 0 ? "help\",\"name\":\"ping" : "test.echo";
                                var r = Http.Post(s.Port, "{\"cmd\":\"" + cmd + "\",\"id\":" + id + ",\"seq\":" + i + "}", null, 30000).Json;
                                if (r == null || !Kit.Ok(r)) { Interlocked.Increment(ref lost[0]); continue; }
                                if ((string)r["id"] != "c" + cc + "-" + i) Interlocked.Increment(ref mismatched[0]);
                                if (r["seq"] != null) { int seq = (int)r["seq"]; if (seq <= lastSeen) Interlocked.Increment(ref outOfOrder[0]); lastSeen = seq; }
                            }
                        }) { IsBackground = true };
                        threads.Add(th);
                        th.Start();
                    }
                    foreach (var th in threads) th.Join(120000);
                    t.Equal(0, lost[0], "4 concurrent clients: no lost or failed replies (" + clients * each + " requests)");
                    t.Equal(0, mismatched[0], "4 concurrent clients: every reply carries its own id");
                    t.Equal(0, outOfOrder[0], "4 concurrent clients: per-client order kept");
                    t.Info(clients + " clients x " + each + " requests in " + sw.ElapsedMilliseconds + " ms");
                }
                // about one frame of latency for a main-thread command (pump every 16 ms)
                using (var pump = new PumpThread(s, 16))
                {
                    var lat = new List<long>();
                    for (int i = 0; i < 20; i++)
                    {
                        var sw2 = Stopwatch.StartNew();
                        Http.Post(s.Port, "{\"cmd\":\"test.echo\"}");
                        lat.Add(sw2.ElapsedMilliseconds);
                    }
                    lat.Sort();
                    t.Check(lat[lat.Count / 2] <= 40, "main-thread command at 60 fps answers in about one frame (median " + lat[lat.Count / 2] + " ms)");
                    t.Info("latency at 16 ms frames: median " + lat[lat.Count / 2] + " ms, max " + lat[lat.Count - 1] + " ms");
                }
            }
        }
    }
}

// Transport seam: the protocol runs on transport-neutral exchanges, so the Mono
// HttpListener transport can be replaced (e.g. by a TcpListener HTTP/1.1 server under a CoreCLR runtime) without
// touching the dispatcher or the safety rules. RequestGate holds the safety rules as plain functions.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HS2Next.Api.Bridge;

namespace HS2Next.Bridge
{
    /// <summary>One HTTP request and its response.</summary>
    public interface IBridgeExchange
    {
        string Method { get; }
        /// <summary>The path without the query string ("/").</summary>
        string Path { get; }
        /// <summary>A request header, or null when absent.</summary>
        string Header(string name);
        bool RemoteIsLoopback { get; }
        /// <summary>Declared body length, or −1 when unknown (chunked).</summary>
        long ContentLength { get; }
        Stream Body { get; }
        /// <summary>Sends the response and closes the exchange. Called once; failures (client gone) are swallowed.</summary>
        void Respond(int status, string contentType, byte[] body, IReadOnlyList<KeyValuePair<string, string>> headers);
    }

    /// <summary>Accepts exchanges on 127.0.0.1 and hands each to the callback on a worker thread.</summary>
    public interface IBridgeTransport : IDisposable
    {
        /// <summary>The bound port (0 before Start).</summary>
        int Port { get; }
        /// <summary>"http://127.0.0.1:&lt;port&gt;/".</summary>
        string Url { get; }
        bool IsListening { get; }
        /// <summary>Binds and starts accepting; throws when the port cannot be bound.</summary>
        void Start(int port, Action<IBridgeExchange> onRequest);
        /// <summary>Stops accepting and closes the socket and every open connection. Idempotent.</summary>
        void Stop();
    }

    /// <summary>The safety rules applied before a body is read.</summary>
    public static class RequestGate
    {
        /// <summary>Headers whose presence marks a browser request (any value).</summary>
        public static readonly IReadOnlyList<string> BrowserHeaders = new[] { "Origin", "Sec-Fetch-Site", "Sec-Fetch-Mode", "Sec-Fetch-Dest", "Sec-Fetch-User" };

        /// <summary>
        /// Null when the exchange may proceed, else (status, one-line text). Order: browser headers 403, Host 403,
        /// remote address 403, method 405, path 404, token 401, declared size 413.
        /// </summary>
        public static (int status, string text)? Check(IBridgeExchange ex, int port, string token, long maxBody)
        {
            foreach (var h in BrowserHeaders)
                if (ex.Header(h) != null) return (403, "forbidden: browser requests (" + h + " header) are refused");
            string host = ex.Header("Host");
            if (host == null || !HostOk(host, port)) return (403, "forbidden: Host must be 127.0.0.1:" + port + " or localhost:" + port);
            if (!ex.RemoteIsLoopback) return (403, "forbidden: only loopback clients are accepted");
            if (!string.Equals(ex.Method, "POST", StringComparison.OrdinalIgnoreCase))
                return (405, "method not allowed: POST a JSON object {\"cmd\": ...} to /");
            if (ex.Path != "/") return (404, "not found: POST to / (the path is always /)");
            if (token != null && !TokenEquals(ex.Header(BridgeProtocol.TokenHeader), token))
                return (401, "unauthorized: header " + BridgeProtocol.TokenHeader + " missing or wrong (see Bridge/token under the user-data folder)");
            if (ex.ContentLength > maxBody) return (413, "payload too large: the body limit is " + maxBody + " bytes");
            return null;
        }

        /// <summary>True for "127.0.0.1:port" or "localhost:port" (case-insensitive, surrounding spaces ignored).</summary>
        public static bool HostOk(string host, int port)
        {
            string h = host.Trim();
            string p = ":" + port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return string.Equals(h, "127.0.0.1" + p, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(h, "localhost" + p, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Constant-time comparison of the presented token.</summary>
        public static bool TokenEquals(string presented, string token)
        {
            if (presented == null || token == null) return false;
            var a = Encoding.UTF8.GetBytes(presented.Trim());
            var b = Encoding.UTF8.GetBytes(token);
            int diff = a.Length ^ b.Length;
            for (int i = 0; i < b.Length; i++) diff |= (i < a.Length ? a[i] : 0) ^ b[i];
            return diff == 0;
        }

        /// <summary>A random 128-bit token as 32 lower-case hex digits.</summary>
        public static string NewToken()
        {
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var sb = new StringBuilder(32);
            foreach (var x in bytes) sb.Append(x.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}

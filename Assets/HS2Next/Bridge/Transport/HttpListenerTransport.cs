// HttpListener transport: Mono's managed listener on the literal prefix
// http://127.0.0.1:<port>/ (never a host name or wildcard, so no URL ACL, no admin rights and no firewall prompt).
// One accept thread; every exchange is handled on a thread-pool thread, so slow clients and long waits do not block
// accepting. Stop closes the listener, which closes the socket and the open connections, and joins the accept thread.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;

namespace HS2Next.Bridge
{
    public sealed class HttpListenerTransport : IBridgeTransport
    {
        static int liveAcceptThreads;

        /// <summary>Accept threads of this domain that have not ended (diagnostics: 0 after every Stop).</summary>
        public static int LiveAcceptThreads => Volatile.Read(ref liveAcceptThreads);

        readonly object gate = new object();
        HttpListener listener;
        Thread acceptThread;
        Action<IBridgeExchange> onRequest;
        volatile bool stopping;

        public int Port { get; private set; }
        public string Url => Port > 0 ? "http://127.0.0.1:" + Port + "/" : null;
        public bool IsListening { get { lock (gate) return listener != null && listener.IsListening && !stopping; } }

        public void Start(int port, Action<IBridgeExchange> onRequest)
        {
            if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            lock (gate)
            {
                if (listener != null) throw new InvalidOperationException("already started");
                var l = new HttpListener();
                l.Prefixes.Add("http://127.0.0.1:" + port + "/");
                l.IgnoreWriteExceptions = true;
                try { l.Start(); }
                catch
                {
                    try { l.Close(); } catch (Exception) { }
                    throw;
                }
                listener = l;
                Port = port;
                stopping = false;
                this.onRequest = onRequest;
                acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "HS2Next bridge accept " + port };
                acceptThread.Start(l);
            }
        }

        void AcceptLoop(object state)
        {
            Interlocked.Increment(ref liveAcceptThreads);
            try { Accept((HttpListener)state); }
            finally { Interlocked.Decrement(ref liveAcceptThreads); }
        }

        void Accept(HttpListener l)
        {
            int errors = 0;
            while (!stopping)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = l.GetContext();
                    errors = 0;
                }
                catch (Exception)
                {
                    if (stopping || !l.IsListening) return;
                    if (++errors > 3) Thread.Sleep(Math.Min(200, errors * 10));
                    continue;
                }
                var cb = onRequest;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    var ex = new Exchange(ctx);
                    try { cb?.Invoke(ex); }
                    catch (Exception)
                    {
                        ex.Respond(500, "text/plain; charset=utf-8", System.Text.Encoding.UTF8.GetBytes("internal error"), null);
                    }
                });
            }
        }

        public void Stop()
        {
            HttpListener l;
            Thread t;
            lock (gate)
            {
                stopping = true;
                l = listener;
                t = acceptThread;
                listener = null;
                acceptThread = null;
                Port = 0;
            }
            if (l != null)
            {
                try { l.Stop(); } catch (Exception) { }
                try { l.Close(); } catch (Exception) { }
            }
            if (t != null && t != Thread.CurrentThread) t.Join(2000);
        }

        public void Dispose() => Stop();

        sealed class Exchange : IBridgeExchange
        {
            readonly HttpListenerContext ctx;
            int responded;

            public Exchange(HttpListenerContext ctx) { this.ctx = ctx; }

            public string Method => ctx.Request.HttpMethod;
            public string Path => ctx.Request.Url?.AbsolutePath ?? ctx.Request.RawUrl;
            public string Header(string name) => ctx.Request.Headers[name];
            public bool RemoteIsLoopback
            {
                get
                {
                    try { var ep = ctx.Request.RemoteEndPoint; return ep != null && IPAddress.IsLoopback(ep.Address); }
                    catch (Exception) { return false; }
                }
            }
            public long ContentLength => ctx.Request.HasEntityBody ? ctx.Request.ContentLength64 : 0;
            public Stream Body => ctx.Request.InputStream;

            public void Respond(int status, string contentType, byte[] body, IReadOnlyList<KeyValuePair<string, string>> headers)
            {
                if (Interlocked.Exchange(ref responded, 1) != 0) return;
                try
                {
                    var r = ctx.Response;
                    r.StatusCode = status;
                    if (headers != null) foreach (var h in headers) r.AddHeader(h.Key, h.Value);
                    r.ContentType = contentType;
                    body = body ?? new byte[0];
                    r.ContentLength64 = body.Length;
                    r.OutputStream.Write(body, 0, body.Length);
                    r.OutputStream.Close();
                    r.Close();
                }
                catch (Exception)
                {
                    try { ctx.Response.Abort(); } catch (Exception) { }
                }
            }
        }
    }
}

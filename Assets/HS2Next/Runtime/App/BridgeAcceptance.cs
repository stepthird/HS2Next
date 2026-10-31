// The Phase 2 acceptance flow through the bridge only (A1–A14): a runtime class so that
// the Editor's BridgeTest.play and the player (Phase 2.5, -bridgeAccept: PlayerAcceptance) run the same steps over HTTP.
// BridgeClient is the small HTTP client both use: requests run on worker tasks (System.Net.Http.HttpClient) and
// coroutines poll them each frame, so the main thread keeps pumping the bridge.
// The flow uses bridge commands only (A1–A14): ping until ready, help, scene.new, chara.load, obj.transform, map.load,
// chara.anime / face / hands, camera.frame, chara.motion (paused, physics off), scene.doc → D1, bones.dump → B1, capture,
// scene.save, scene.new, one batch [scene.load wait:false, chara.motion paused] (the reloaded character never steps
// unpaused; node ids are kept by the file), scene polled until the map has loaded, scene.doc → D2 = D1, bones.dump → B2
// close to B1, a second capture. The AutoStep checks S1–S6, the error and load checks E1–E3 and the check context both
// hosts share are in BridgeAcceptance_Parts.cs.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HS2Next.App
{
    /// <summary>One HTTP reply of the bridge.</summary>
    public sealed class BridgeResponse
    {
        /// <summary>HTTP status, 0 when the request failed below HTTP (connection refused, timeout).</summary>
        public int Status;
        public string Text;
        /// <summary>The parsed body, or null when it is not a JSON object.</summary>
        public JObject Json;
        public readonly Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>The transport failure (exception message), else null.</summary>
        public string Error;
        public double Ms;
        /// <summary>True for a reply with "ok": true.</summary>
        public bool Ok => Json != null && Json.Value<bool?>("ok") == true;
        public string Code => Json?.Value<string>("code");

        public override string ToString() => Error != null ? "error: " + Error : Status + " " + (Text != null && Text.Length > 400 ? Text.Substring(0, 400) + "…" : Text);
    }

    /// <summary>A minimal client of the HS2Next bridge (POST JSON to the root URL).</summary>
    public sealed class BridgeClient : IDisposable
    {
        readonly HttpClient http;

        public BridgeClient(string url, string token = null)
        {
            Url = url.EndsWith("/", StringComparison.Ordinal) ? url : url + "/";
            Token = token;
            http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        }

        public string Url { get; }
        public string Token { get; }

        /// <summary>Posts one request (a JSON object text) on a worker task; never throws (failures in BridgeResponse.Error).</summary>
        public Task<BridgeResponse> Post(string json, IDictionary<string, string> headers = null, double timeoutSec = 600) =>
            Task.Run(async () =>
            {
                var r = new BridgeResponse();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec)))
                    using (var req = new HttpRequestMessage(HttpMethod.Post, Url))
                    {
                        req.Content = new StringContent(json ?? "{}", new UTF8Encoding(false), "application/json");
                        if (Token != null) req.Headers.TryAddWithoutValidation(HS2Next.Api.Bridge.BridgeProtocol.TokenHeader, Token);
                        if (headers != null) foreach (var kv in headers) req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                        using (var resp = await http.SendAsync(req, cts.Token).ConfigureAwait(false))
                        {
                            r.Status = (int)resp.StatusCode;
                            foreach (var h in resp.Headers) r.Headers[h.Key] = string.Join(",", h.Value);
                            foreach (var h in resp.Content.Headers) r.Headers[h.Key] = string.Join(",", h.Value);
                            r.Text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        }
                    }
                    try { r.Json = JObject.Parse(r.Text); } catch (Exception) { r.Json = null; }
                }
                catch (Exception e) { r.Error = e.GetType().Name + ": " + e.Message; }
                r.Ms = sw.Elapsed.TotalMilliseconds;
                return r;
            });

        /// <summary>A coroutine that waits (one frame per step) until <paramref name="task"/> completed.</summary>
        public static IEnumerator Wait(Task task)
        {
            while (!task.IsCompleted) yield return null;
        }

        /// <summary>Posts <paramref name="json"/> and waits for the reply in a coroutine; the reply goes to <paramref name="into"/>[0].</summary>
        public IEnumerator Call(string json, BridgeResponse[] into, double timeoutSec = 600, IDictionary<string, string> headers = null)
        {
            var t = Post(json, headers, timeoutSec);
            while (!t.IsCompleted) yield return null;
            into[0] = t.Result;
        }

        public void Dispose() => http.Dispose();
    }

    /// <summary>What <see cref="BridgeAcceptance.Run"/> works on.</summary>
    public sealed class AcceptanceOptions
    {
        public string Url = "http://127.0.0.1:" + HS2Next.Api.Bridge.BridgeProtocol.DefaultPort + "/";
        public string Token;
        /// <summary>The card, relative to the HS2 card folder of <see cref="Sex"/> (chara.load's path).</summary>
        public string CardPath = "BR-Chan HS2 Rev2.png";
        public string Sex = "female";
        public int Map = 10;
        public int AnimeGroup = 19, AnimeCategory = 0, AnimeNo = 0;
        public string ShotBefore = "accept_before", ShotAfter = "accept_after";
        public string SceneName = "bridge_accept";
        /// <summary>Seconds A1 waits for the app (a cold zipmod scan takes minutes).</summary>
        public double ReadySeconds = 600;
        public double MapSeconds = 120;
        /// <summary>Filled by Run: the character's node id (kept across the reload), D1 / D2 hashes, timings.</summary>
        public AcceptanceResult Result = new AcceptanceResult();
    }

    public sealed class AcceptanceResult
    {
        public string CharaKey;
        public string D1, D2;
        public double ReadySeconds;
        public string ShotBefore, ShotAfter;
        public bool Completed;
    }

    /// <summary>
    /// The Phase 2 acceptance through the bridge only: load a card, place it on a map, play an
    /// animation, save, reload, the same scene.doc. Every step reports through <c>check(ok, "A&lt;n&gt;: what  detail")</c>;
    /// a step whose precondition failed reports its checks as failed and the flow stops.
    /// </summary>
    public static partial class BridgeAcceptance
    {
        /// <summary>The 2.4 commands (B's five and Core's 30) that help must list (A2).</summary>
        public static readonly string[] Commands24 =
        {
            "ping", "help", "bridge.version", "batch", "log.tail",
            "scene", "scene.doc", "scene.new", "scene.save", "scene.load", "delete", "obj.transform", "maps", "map.load",
            "chara.load", "anime.groups", "anime.search", "chara.anime", "chara.speed", "chara.face", "chara.hands",
            "look.target", "chara.motion", "bones.dump", "face.dump", "fk.list", "fk.set", "fk.enable", "fk.capture",
            "pose.load", "camera.get", "camera.set", "camera.frame", "camera.state", "capture",
        };

        public static IEnumerator Run(AcceptanceOptions o, StringBuilder report, Action<bool, string> check)
        {
            var res = o.Result;
            using (var client = new BridgeClient(o.Url, o.Token))
            {
                var box = new BridgeResponse[1];
                BridgeResponse last = null;
                IEnumerator Call(object request, double timeout = 300)
                {
                    string json = request as string ?? Newtonsoft.Json.JsonConvert.SerializeObject(request);
                    yield return client.Call(json, box, timeout);
                    last = box[0];
                }
                bool Ok(string id, string what)
                {
                    bool ok = last != null && last.Ok;
                    check(ok, id + ": " + what + (ok ? "" : "  " + Short(last?.ToString())));
                    return ok;
                }
                void Info(string line) => report?.AppendLine("   " + line);

                // A1 ping until ready
                var sw = System.Diagnostics.Stopwatch.StartNew();
                bool ready = false;
                BridgeResponse ping = null;
                while (sw.Elapsed.TotalSeconds < o.ReadySeconds)
                {
                    yield return Call(new { cmd = "ping" }, 10);
                    ping = last;
                    if (ping.Ok && ping.Json["ready"] is JObject st && st.Properties().All(p => p.Value.Type == JTokenType.Boolean && (bool)p.Value) && st.Count > 0)
                    {
                        ready = true;
                        break;
                    }
                    if (ping.Json?.Value<bool?>("ok") == true && ping.Json["readyDetail"] is JObject det && det.Count > 0) Info("A1 waiting: " + det.ToString(Newtonsoft.Json.Formatting.None));
                    for (int i = 0; i < 15; i++) yield return null;
                }
                res.ReadySeconds = sw.Elapsed.TotalSeconds;
                check(ready, $"A1: ping until every ready stage is true ({res.ReadySeconds:F1} s; warm target < 15 s)  {Short(ping?.ToString())}");
                check(ping != null && ping.Json?.Value<bool?>("studio") == true, "A1: ping.studio true");
                check(ping != null && ping.Headers.ContainsKey(HS2Next.Api.Bridge.BridgeProtocol.BridgeHeader), "A1: HTTP header " + HS2Next.Api.Bridge.BridgeProtocol.BridgeHeader + " present");
                if (!ready) yield break;

                // A2 help
                yield return Call(new { cmd = "help" });
                var cmds = (last.Json?["commands"] as JArray)?.Select(x => (string)x["name"]).ToList() ?? new List<string>();
                check(cmds.Count >= 50, "A2: help lists ≥ 50 commands  " + cmds.Count);
                var missing = Commands24.Where(n => !cmds.Contains(n)).ToList();
                check(missing.Count == 0, "A2: every 2.4 command listed  " + string.Join(", ", missing));

                // A3 scene.new
                yield return Call(new { cmd = "scene.new" });
                Ok("A3", "scene.new");
                yield return Call(new { cmd = "scene" });
                check(last.Ok && (last.Json["objects"] as JArray)?.Count == 0 && (int?)last.Json["map"]?["no"] == -1, "A3: scene.objects empty, map.no −1  " + Short(last.Text));

                // A4 chara.load
                yield return Call(new { cmd = "chara.load", path = o.CardPath, sex = o.Sex });
                var added = last.Json?["added"]?[0];
                res.CharaKey = (string)added?["key"];
                check(last.Ok && (string)added?["state"] == "Built" && last.Json.Value<bool?>("imported") == true, "A4: chara.load: added[0].state Built, imported  " + Short(last.Text));
                if (res.CharaKey == null) yield break;
                string key = res.CharaKey;

                // A5 obj.transform
                yield return Call(new { cmd = "obj.transform", key, pos = new[] { 1, 0, 2 }, rot = new[] { 0, 30, 0 } });
                var wp = last.Json?["object"]?["worldPos"] as JArray;
                check(last.Ok && wp != null && Math.Abs((double)wp[0] - 1) <= 1e-4 && Math.Abs((double)wp[1]) <= 1e-4 && Math.Abs((double)wp[2] - 2) <= 1e-4,
                    "A5: obj.transform: worldPos = [1, 0, 2] ± 1e-4  " + wp?.ToString(Newtonsoft.Json.Formatting.None));

                // A6 map.load (wait)
                sw.Restart();
                yield return Call(new { cmd = "map.load", no = o.Map }, o.MapSeconds + 30);
                double mapS = sw.Elapsed.TotalSeconds;
                check(last.Ok && (string)last.Json["state"] == "Loaded" && mapS <= o.MapSeconds, $"A6: map.load {o.Map}: state Loaded within {o.MapSeconds:F0} s ({mapS:F1} s)  " + Short(last.Text));
                yield return Call(new { cmd = "scene" });
                check((string)last.Json?["map"]?["id"] == "illusion.hs2#" + o.Map, "A6: scene.map.id = illusion.hs2#" + o.Map + "  " + (string)last.Json?["map"]?["id"]);

                // A7 animation, face, hands, camera
                yield return Call(new { cmd = "chara.anime", key, group = o.AnimeGroup, category = o.AnimeCategory, no = o.AnimeNo, speed = 1 });
                Ok("A7", "chara.anime " + o.AnimeGroup + "/" + o.AnimeCategory + "/" + o.AnimeNo);
                yield return Call(new { cmd = "chara.face", key, eyebrow = 1, mouth = 3 });
                Ok("A7", "chara.face");
                yield return Call(new { cmd = "chara.hands", key, left = 2 });
                Ok("A7", "chara.hands");
                yield return Call(new { cmd = "camera.frame", key });
                Ok("A7", "camera.frame");
                yield return Call(new { cmd = "history" });
                var labels = (last.Json?["undo"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
                var expect = new[] { "Hands", "Face", "Animation", "Map", "Transform", "Add character" };
                check(labels.Take(expect.Length).SequenceEqual(expect), "A7: history.undo labels in order  " + string.Join(", ", labels));

                // A8 30 frames, then pause with physics off
                yield return Call(new { cmd = "ping" });
                int f0 = last.Json?.Value<int?>("frame") ?? 0, f1 = f0;
                sw.Restart();
                while (f1 - f0 < 30 && sw.Elapsed.TotalSeconds < 30)
                {
                    int target = Time.frameCount + 5;
                    while (Time.frameCount < target) yield return null;
                    yield return Call(new { cmd = "ping" });
                    f1 = last.Json?.Value<int?>("frame") ?? f1;
                }
                check(f1 - f0 >= 30, "A8: ping.frame advanced ≥ 30  " + (f1 - f0));
                yield return Call(new { cmd = "chara.motion", key, paused = true, physics = false, waitFrames = 2 });
                check(last.Ok && (last.Json.Value<double?>("time") ?? 0) > 0, "A8: chara.motion {paused, physics:false}: time > 0  " + Short(last.Text));

                // A9 D1, B1, capture before
                yield return Call(new { cmd = "scene.doc" });
                var d1 = last.Json;
                res.D1 = (string)d1?["sha256"];
                Ok("A9", "scene.doc → D1");
                yield return Call(new { cmd = "bones.dump", key, root = "all" });
                var b1 = last.Json;
                Ok("A9", "bones.dump all → B1 (" + ((b1?["bones"] as JArray)?.Count ?? 0) + " bones)");
                yield return Call(new { cmd = "capture", name = o.ShotBefore });
                res.ShotBefore = (string)last.Json?["path"];
                if (Ok("A9", "capture " + o.ShotBefore))
                {
                    var (w, h, fg, _) = Png(res.ShotBefore, null);
                    check(w == 1280 && h == 720 && fg >= 0.01, $"A9: PNG 1280 × 720 with a foreground ≥ 1 %  {w} × {h}, foreground {fg:P1}");
                }

                // A10 save
                yield return Call(new { cmd = "scene.save", name = o.SceneName, overwrite = true });
                string scenePath = (string)last.Json?["path"];
                check(last.Ok && scenePath != null && File.Exists(scenePath) && last.Json.Value<bool?>("dirty") == false, "A10: scene.save: the file exists, dirty false  " + Short(last.Text));

                // A11 new, then reload and pause in one pump; wait for the map
                yield return Call(new { cmd = "scene.new" });
                Ok("A11", "scene.new");
                yield return Call(new
                {
                    cmd = "batch",
                    cmds = new object[]
                    {
                        new { cmd = "scene.load", name = o.SceneName, wait = false },
                        new { cmd = "chara.motion", key, paused = true, physics = false },
                    },
                });
                Ok("A11", "batch [scene.load wait:false, chara.motion paused]");
                yield return Call(new { cmd = "chara.motion", key });
                double t0 = last.Json?.Value<double?>("time") ?? double.NaN;
                sw.Restart();
                JToken sc = null;
                while (sw.Elapsed.TotalSeconds < o.MapSeconds)
                {
                    yield return Call(new { cmd = "scene" });
                    sc = last.Json;
                    var stt = (string)sc?["map"]?["state"];
                    if (stt != "Loading" && stt != null && stt != "None") break;
                    for (int i = 0; i < 10; i++) yield return null;
                }
                yield return Call(new { cmd = "chara.motion", key });
                double t1 = last.Json?.Value<double?>("time") ?? double.NaN;
                var objs = sc?["objects"] as JArray;
                check(objs?.Count == 1 && (string)sc["map"]["state"] == "Loaded" && (string)objs[0]["state"] == "Built" && (string)objs[0]["key"] == key,
                    $"A11: nodes 1, map Loaded ({sw.Elapsed.TotalSeconds:F1} s), character Built  " + Short(sc?.ToString(Newtonsoft.Json.Formatting.None)));
                check(t0 == t1 && !double.IsNaN(t0), $"A11: chara.motion.time unchanged while the map loads  {t0} → {t1}");

                // A12 D2 = D1
                yield return Call(new { cmd = "scene.doc" });
                var d2 = last.Json;
                res.D2 = (string)d2?["sha256"];
                check(res.D1 != null && res.D1 == res.D2, "A12: D2 = D1 (sha256)  " + res.D1 + " / " + res.D2);
                foreach (var m in new[] { "title", "scene", "res" })
                    check(JToken.DeepEquals(d1?[m], d2?[m]), "A12: " + m + " equal" + (JToken.DeepEquals(d1?[m], d2?[m]) ? "" : "  " + FirstDiff((string)d1?[m]?.ToString(), (string)d2?[m]?.ToString())));
                var c1 = d1?["charas"] as JObject;
                var c2 = d2?["charas"] as JObject;
                foreach (var p in c1?.Properties() ?? Enumerable.Empty<JProperty>())
                    foreach (var part in new[] { "chara", "coord" })
                    {
                        string a = (string)p.Value?[part], b = (string)c2?[p.Name]?[part];
                        check(a != null && a == b, "A12: " + p.Name.Substring(0, 8) + " " + part + " text equal" + (a == b ? "" : "  " + FirstDiff(a, b)));
                    }

                // A13 B2 vs B1
                yield return Call(new { cmd = "bones.dump", key, root = "all" });
                var b2 = last.Json;
                Ok("A13", "bones.dump all → B2");
                CompareBones(b1, b2, check, report);

                // A14 capture after (504 × 704)
                yield return Call(new { cmd = "capture", name = o.ShotAfter, width = 504, height = 704 });
                res.ShotAfter = (string)last.Json?["path"];
                if (Ok("A14", "capture " + o.ShotAfter))
                {
                    var (w, h, fg, mad) = Png(res.ShotAfter, res.ShotBefore);
                    check(w == 504 && h == 704, $"A14: PNG 504 × 704  {w} × {h}");
                    Info($"A14: foreground {fg:P1}; mean absolute difference to the centre crop of {o.ShotBefore}: {mad:F2} (reported, not judged)");
                }
                res.Completed = true;
            }
        }

        static string Short(string s) => s == null ? "null" : s.Length > 300 ? s.Substring(0, 300) + "…" : s;

        static string FirstDiff(string a, string b)
        {
            if (a == null || b == null) return "missing: " + (a == null ? "before" : "after");
            int i = 0;
            while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
            int s = Math.Max(0, i - 80);
            return "first difference at " + i + ": …" + a.Substring(s, Math.Min(160, a.Length - s)) + "… vs …" + b.Substring(s, Math.Min(160, Math.Max(0, b.Length - s))) + "…";
        }

        /// <summary>A13: every transform of B1 in B2 by path; position ≤ 1e-4, angle ≤ 0.01°, scale ≤ 1e-5.</summary>
        static void CompareBones(JObject b1, JObject b2, Action<bool, string> check, StringBuilder report)
        {
            var map = new Dictionary<string, JToken>(StringComparer.Ordinal);
            foreach (var b in b2?["bones"] as JArray ?? new JArray()) map[(string)b["name"] + "|" + (string)b["path"]] = b;
            int n = 0, missing = 0, bad = 0;
            double maxPos = 0, maxAng = 0, maxScale = 0;
            var worst = new List<string>();
            foreach (var b in b1?["bones"] as JArray ?? new JArray())
            {
                n++;
                string k = (string)b["name"] + "|" + (string)b["path"];
                if (!map.TryGetValue(k, out var o)) { missing++; if (worst.Count < 10) worst.Add("missing " + k); continue; }
                double dp = MaxAbs(b["lp"], o["lp"]), ds = MaxAbs(b["ls"], o["ls"]), da = Angle(b["lq"], o["lq"]);
                maxPos = Math.Max(maxPos, dp); maxAng = Math.Max(maxAng, da); maxScale = Math.Max(maxScale, ds);
                if (dp > 1e-4 || da > 0.01 || ds > 1e-5)
                {
                    bad++;
                    if (worst.Count < 10) worst.Add($"{(string)b["path"]}: pos {dp:G3}, angle {da:G3}°, scale {ds:G3}");
                }
            }
            check(n > 0 && missing == 0, $"A13: every transform of B1 found in B2 by path  {n} bones, {missing} missing");
            check(n > 0 && bad == 0, $"A13: position ≤ 1e-4, angle ≤ 0.01°, scale ≤ 1e-5  max {maxPos:G3} / {maxAng:G3}° / {maxScale:G3}; {bad} over");
            foreach (var w in worst) report?.AppendLine("   A13 " + w);
        }

        static double MaxAbs(JToken a, JToken b)
        {
            if (!(a is JArray x) || !(b is JArray y) || x.Count != y.Count) return double.PositiveInfinity;
            double m = 0;
            for (int i = 0; i < x.Count; i++) m = Math.Max(m, Math.Abs(Num(x[i]) - Num(y[i])));
            return m;
        }

        static double Num(JToken t) => t == null || t.Type == JTokenType.Null ? double.NaN : (double)t;

        static double Angle(JToken a, JToken b)
        {
            if (!(a is JArray x) || !(b is JArray y) || x.Count != 4 || y.Count != 4) return double.PositiveInfinity;
            double dot = 0, na = 0, nb = 0;
            for (int i = 0; i < 4; i++) { dot += Num(x[i]) * Num(y[i]); na += Num(x[i]) * Num(x[i]); nb += Num(y[i]) * Num(y[i]); }
            double c = Math.Min(1, Math.Abs(dot) / Math.Sqrt(na * nb));
            return 2 * Math.Acos(c) * 180 / Math.PI;
        }

        /// <summary>
        /// Size and foreground fraction (pixels differing from the corner colour by more than 8 in a channel) of a PNG; with
        /// <paramref name="other"/>, the mean absolute channel difference of this image to the same-size centre crop of the other.
        /// </summary>
        public static (int w, int h, double foreground, double meanAbsDiff) Png(string path, string other)
        {
            if (path == null || !File.Exists(path)) return (0, 0, 0, double.NaN);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Texture2D tex2 = null;
            try
            {
                if (!tex.LoadImage(File.ReadAllBytes(path))) return (0, 0, 0, double.NaN);
                var px = tex.GetPixels32();
                int w = tex.width, h = tex.height;
                var c0 = px[0];
                long fg = 0;
                foreach (var p in px)
                    if (Math.Abs(p.r - c0.r) > 8 || Math.Abs(p.g - c0.g) > 8 || Math.Abs(p.b - c0.b) > 8) fg++;
                double mad = double.NaN;
                if (other != null && File.Exists(other))
                {
                    tex2 = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (tex2.LoadImage(File.ReadAllBytes(other)) && tex2.width >= w && tex2.height >= h)
                    {
                        var q = tex2.GetPixels32();
                        int ox = (tex2.width - w) / 2, oy = (tex2.height - h) / 2;
                        double sum = 0;
                        for (int y = 0; y < h; y++)
                            for (int x = 0; x < w; x++)
                            {
                                var a = px[y * w + x];
                                var b = q[(y + oy) * tex2.width + x + ox];
                                sum += Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b);
                            }
                        mad = sum / (3.0 * w * h);
                    }
                }
                return (w, h, fg / (double)px.Length, mad);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
                if (tex2 != null) UnityEngine.Object.DestroyImmediate(tex2);
            }
        }
    }
}

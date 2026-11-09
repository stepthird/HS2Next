// ScriptProbe's commands with the bridge on (row ScriptCheck.player, 17.3). On the player's main thread:
//   scriptprobe.jr1.arm / .disarm   for every capture from now on: in CaptureEvents.Starting, before its render, a control render with the test hook
//                                   off (CaptureService.RecalculateSkin false: each skinned renderer as the late point has it); in CaptureEvents.Rendered,
//                                   right after its render, a reference render with forceMatrixRecalculationPerRender forced on every enabled skinned
//                                   renderer. Both kept in memory by the capture's file path, with the frame, its delta and the frames since the view last drew
//   scriptprobe.jr1.judge          a capture's file read as numbers and deleted: the pixels over 2 levels against its reference and its control against the reference
//   scriptprobe.jr1.folder         the same for every armed moment whose file lies in a folder (a recording's frames)
//   scriptprobe.digest.arm / .read the digests of spec 12.1 at k x duration / n (k 1..n-1) of a recorded pass, each at the first late point whose Timeline time reaches it
//   scriptprobe.compile            a scene script compiled with its cast, rounds timed (JW1)
//   scriptprobe.parts              the script parts' tick called again with dt 0 (and the follow's late apply), rounds timed, with what they hold (JW3; idle: JW7)
//   scriptprobe.inline             one request run inline (RunNow), rounds timed (JW4 with the bridge on, JW9)
//   scriptprobe.play               a scene script played by the player directly, timed (JW5)
//   scriptprobe.yt1, .yt3          the thumbnails of a moving character and of JR6's fixture (Phase 6.10, YT1, YT3 (a)): see ScriptProbeThumbs.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using HS2Next.App;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using HS2Next.Scenes.Scripts;
using HS2Next.Screenshots;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Stp3.Test.ScriptProbe
{
    public sealed partial class ScriptProbePlugin
    {
        const int Levels = 2;

        static SceneRuntime Rt => AppCore.Current.Runtime;
        static Camera ViewCam => Rt.GetPart<SceneViewCamera>()?.Camera;
        static double R4(double v) => Math.Round(v, 4);
        static JArray V(Vector3 v) => new JArray(R6(v.x), R6(v.y), R6(v.z));
        static double R6(double v) => Math.Round(v, 6);

        void RegisterChecks(IPluginContext context)
        {
            var c = context.Commands;
            c.Register(new CommandSpec("scriptprobe.jr1.arm", "Arms the control and reference renders of every capture from now on (kept in memory by file path)."), _ => { Jr1Arm(); return new Dictionary<string, object> { ["armed"] = true }; });
            c.Register(new CommandSpec("scriptprobe.jr1.disarm", "Disarms them and forgets the moments kept."), _ => { Jr1Disarm(); return new Dictionary<string, object> { ["armed"] = false }; });
            c.Register(new CommandSpec("scriptprobe.jr1.judge", "A capture's file read as numbers and deleted, against its moment's reference and control.").WithTimeout(120)
                .Arg("path", ArgTypes.String, "the capture's file", required: true), a => Jr1Judge(a.Args.Require<string>("path")));
            c.Register(new CommandSpec("scriptprobe.jr1.folder", "Every armed moment whose file lies in a folder (a recording's frames), judged; the files deleted.").WithTimeout(600)
                .Arg("folder", ArgTypes.String, "the folder", required: true), a => Jr1Folder(a.Args.Require<string>("folder")));
            c.Register(new CommandSpec("scriptprobe.digest.arm", "Arms the digests of a recorded pass.").Arg("cast", ArgTypes.Object, "role -> node id", required: true)
                .Arg("duration", ArgTypes.Float, "the Timeline's duration", required: true).Arg("parts", ArgTypes.Int, "the pass in n parts: digests at k x duration / n, k 1..n-1"),
                a => DigestArm(a.Args.Get("cast", new Dictionary<string, object>()), a.Args.Get("duration", 0f), a.Args.Get("parts", 8)));
            c.Register(new CommandSpec("scriptprobe.digest.read", "The digests taken since the last arm; disarms.").WithFlags(CommandFlags.ReadOnly), _ => DigestRead());
            c.Register(new CommandSpec("scriptprobe.compile", "A scene script compiled with its cast, rounds timed.").WithTimeout(600).Arg("script", ArgTypes.String, "the scene script's id", required: true)
                .Arg("rounds", ArgTypes.Int, "rounds").WithFlags(CommandFlags.ReadOnly), a => Compile(a.Args.Require<string>("script"), a.Args.Get("rounds", 50)));
            c.Register(new CommandSpec("scriptprobe.parts", "The script parts' tick with dt 0 and the follow's late apply, rounds timed; what they hold.").WithTimeout(600).Arg("rounds", ArgTypes.Int, "rounds"),
                a => Parts(a.Args.Get("rounds", 1000)));
            c.Register(new CommandSpec("scriptprobe.inline", "One request run inline, rounds timed.").WithTimeout(600).Arg("request", ArgTypes.Object, "the request", required: true)
                .Arg("rounds", ArgTypes.Int, "rounds").Arg("warm", ArgTypes.Int, "rounds not kept"),
                a => TimeInline(JsonConvert.SerializeObject(a.Args.Get("request", new Dictionary<string, object>())), a.Args.Get("rounds", 1000), a.Args.Get("warm", 50)).ToObject<Dictionary<string, object>>());
            c.Register(new CommandSpec("scriptprobe.play", "A scene script played by the player directly (no record), timed.").WithTimeout(600).Arg("script", ArgTypes.String, "the scene script's id", required: true)
                .Arg("prepare", ArgTypes.String, "auto, always or never"), a => Play(a.Args.Require<string>("script"), a.Args.Get("prepare", ScriptPlayOptions.PrepareAuto)));
#if !FRAMES_ONLY
            RegisterMosaic(c);
            RegisterThumbs(c);
#endif
        }

        void UnloadChecks()
        {
            Jr1Disarm();
            DigestDisarm();
            Jr6Restore();
        }

        // ------------------------------------------------------------ renders in memory

        static Color32[] Decode(byte[] png)
        {
            if (png == null) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try { return t.LoadImage(png) ? t.GetPixels32() : null; }
            finally { Object.Destroy(t); }
        }

        static Color32[] ReadAndDelete(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            var px = Decode(File.ReadAllBytes(path));
            File.Delete(path);
            return px;
        }

        /// <summary>Pixels whose largest channel difference is over <see cref="Levels"/>; -1 when either is missing or the sizes differ.</summary>
        static int Diff(Color32[] a, Color32[] b, out int most)
        {
            most = 0;
            if (a == null || b == null || a.Length != b.Length) return -1;
            int n = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int d = Math.Max(Math.Abs(a[i].r - b[i].r), Math.Max(Math.Abs(a[i].g - b[i].g), Math.Abs(a[i].b - b[i].b)));
                if (d > Levels) n++;
                if (d > most) most = d;
            }
            return n;
        }

        static SkinnedMeshRenderer[] EnabledSkins() => Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Exclude).Where(s => s && s.enabled).ToArray();

        /// <summary>forceMatrixRecalculationPerRender on every enabled skinned renderer around <paramref name="a"/> (each flag put back).</summary>
        static void Forced(Action a)
        {
            var smrs = EnabledSkins();
            var saved = smrs.Select(s => s.forceMatrixRecalculationPerRender).ToArray();
            try { foreach (var s in smrs) s.forceMatrixRecalculationPerRender = true; a(); }
            finally { for (int i = 0; i < smrs.Length; i++) if (smrs[i]) smrs[i].forceMatrixRecalculationPerRender = saved[i]; }
        }

        static Color32[] ViewRender(int w, int h, string aa, bool frame) =>
            Decode(CaptureService.RenderSync(Rt, ViewCam, new CaptureRequest { Width = w, Height = h, Aa = aa, Frame = frame, MaxPixels = CaptureLimits.HardMaxPixels }).Png);

        static Color32[] ForcedRender(int w, int h, string aa, bool frame)
        {
            Color32[] px = null;
            Forced(() => px = ViewRender(w, h, aa, frame));
            return px;
        }

        static Color32[] HookOffRender(int w, int h, string aa, bool frame)
        {
            bool was = CaptureService.RecalculateSkin;
            CaptureService.RecalculateSkin = false;
            try { return ViewRender(w, h, aa, frame); }
            finally { CaptureService.RecalculateSkin = was; }
        }

        // ------------------------------------------------------------ JR1 in the player (JR4)

        sealed class Moment
        {
            public string Path;
            public int Frame, SinceDrawn = -1, Id;
            public float Dt;
            public Color32[] Control, Reference;
        }

        readonly Dictionary<string, Moment> moments = new Dictionary<string, Moment>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<int, Moment> momentsById = new Dictionary<int, Moment>();
        readonly List<string> handlerErrors = new List<string>();
        bool jr1Armed, inControl;
        int lastDrawFrame = -1;

        void OnEndCamera(ScriptableRenderContext c, Camera cam) { if (!inControl) lastDrawFrame = Time.frameCount; }

        void Jr1Arm()
        {
            Jr1Disarm();
            jr1Armed = true;
            lastDrawFrame = -1;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
            CaptureEvents.Starting += Jr1Starting;
            CaptureEvents.Rendered += Jr1Rendered;
        }

        void Jr1Disarm()
        {
            if (jr1Armed)
            {
                RenderPipelineManager.endCameraRendering -= OnEndCamera;
                CaptureEvents.Starting -= Jr1Starting;
                CaptureEvents.Rendered -= Jr1Rendered;
            }
            jr1Armed = false;
            moments.Clear();
            momentsById.Clear();
            handlerErrors.Clear();
        }

        void Jr1Starting(CaptureInfo info)
        {
            try
            {
                string aa = info.Kind == CaptureKind.Capture ? CaptureService.None : CaptureService.Smaa;
                var m = new Moment { Path = info.Path, Id = info.Id, Frame = Time.frameCount, Dt = Time.deltaTime, SinceDrawn = lastDrawFrame < 0 ? -1 : Time.frameCount - lastDrawFrame };
                inControl = true;
                try { m.Control = HookOffRender(info.Width, info.Height, aa, info.DrawsFrame); }
                finally { inControl = false; }
                momentsById[info.Id] = m;
                if (!string.IsNullOrEmpty(info.Path)) moments[info.Path] = m;
            }
            catch (Exception e) { handlerErrors.Add("starting: " + e.GetType().Name + ": " + e.Message); }
        }

        void Jr1Rendered(CaptureInfo info)
        {
            try
            {
                if (!momentsById.TryGetValue(info.Id, out var m)) return;
                string aa = info.Kind == CaptureKind.Capture ? CaptureService.None : CaptureService.Smaa;
                inControl = true;
                try { m.Reference = ForcedRender(info.Width, info.Height, aa, info.DrawsFrame); }
                finally { inControl = false; }
                if (string.IsNullOrEmpty(m.Path) && !string.IsNullOrEmpty(info.Path)) { m.Path = info.Path; moments[info.Path] = m; }
            }
            catch (Exception e) { handlerErrors.Add("rendered: " + e.GetType().Name + ": " + e.Message); }
        }

        Dictionary<string, object> JudgeOne(Moment m, string path)
        {
            var px = ReadAndDelete(path);
            int d = m == null ? -1 : Diff(px, m.Reference, out int most);
            int cdiff = m == null ? -1 : Diff(m.Control, m.Reference, out int cmost);
            return new Dictionary<string, object>
            {
                ["path"] = path, ["armed"] = m != null, ["read"] = px != null, ["capture"] = d, ["control"] = cdiff, ["frame"] = m?.Frame ?? -1, ["dtMs"] = R4((m?.Dt ?? 0f) * 1000.0),
                ["sinceDrawn"] = m?.SinceDrawn ?? -1, ["errors"] = handlerErrors.ToArray(), ["skinScope"] = CaptureService.LastSkinScopeCount,
            };
        }

        Dictionary<string, object> Jr1Judge(string path)
        {
            moments.TryGetValue(path ?? "", out var m);
            var r = JudgeOne(m, path);
            if (m != null) { moments.Remove(path); momentsById.Remove(m.Id); }
            return r;
        }

        Dictionary<string, object> Jr1Folder(string folder)
        {
            string full = Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            var mine = moments.Values.Where(m => m.Path != null && Path.GetFullPath(m.Path).StartsWith(full, StringComparison.OrdinalIgnoreCase)).OrderBy(m => m.Id).ToList();
            var list = new List<object>();
            foreach (var m in mine) { list.Add(JudgeOne(m, m.Path)); moments.Remove(m.Path); momentsById.Remove(m.Id); }
            return new Dictionary<string, object> { ["moments"] = list, ["errors"] = handlerErrors.ToArray() };
        }

        // ------------------------------------------------------------ digests (spec 12.1) at the frames of a recorded pass

        sealed class DigestRun
        {
            public Dictionary<string, string> Cast;
            public double Duration;
            public int Parts, Next = 1;
            public readonly JArray Taken = new JArray();
            public Action<int> Hook;
        }

        DigestRun digest;

        Dictionary<string, object> DigestArm(Dictionary<string, object> cast, float duration, int parts)
        {
            DigestDisarm();
            if (!(duration > 0f) || parts < 2) throw new BridgeException(BridgeCodes.BadArgs, "duration > 0 and parts >= 2");
            var d = new DigestRun { Cast = cast.ToDictionary(k => k.Key, k => Convert.ToString(k.Value)), Duration = duration, Parts = parts };
            var pb = Rt.GetPart<TimelinePlayback>();
            d.Hook = frame =>
            {
                try
                {
                    if (d.Next >= d.Parts || !FrameRecorder.IsRecording || pb == null) return;
                    double T = d.Next * d.Duration / d.Parts;
                    if (pb.Player.State == HS2Next.Scenes.Timeline.TimelinePlayState.Playing && pb.Player.Time >= T)
                    {
                        d.Taken.Add(TakeDigest(d.Cast, pb.Player.Time, d.Next, frame));
                        d.Next++;
                    }
                }
                catch (Exception e) { d.Taken.Add(new JObject { ["error"] = e.GetType().Name + ": " + e.Message, ["k"] = d.Next }); d.Next++; }
            };
            AppCore.Current.Bridge.Server.Frames.LateFrame += d.Hook;
            digest = d;
            return new Dictionary<string, object> { ["armed"] = true, ["times"] = Enumerable.Range(1, parts - 1).Select(k => k * (double)duration / parts).ToArray() };
        }

        void DigestDisarm()
        {
            var frames = AppCore.Current?.Bridge?.Server?.Frames;
            if (digest?.Hook != null && frames != null) frames.LateFrame -= digest.Hook;
            digest = null;
        }

        Dictionary<string, object> DigestRead()
        {
            var d = digest;
            DigestDisarm();
            return new Dictionary<string, object> { ["digests"] = d?.Taken ?? new JArray(), ["parts"] = d?.Parts ?? 0 };
        }

        static JObject InlineJ(string json)
        {
            try { return JObject.Parse(AppCore.Current.Bridge.Server.RunNow(json)); }
            catch (Exception e) { return new JObject { ["ok"] = false, ["error"] = e.Message }; }
        }

        /// <summary>One digest: per cast character the world position of every cf_J_* transform under its root and its holder's; the view camera; every
        /// slot's value; the stains of the cast characters with one (wet.status rows); ragdoll.status.</summary>
        static JObject TakeDigest(Dictionary<string, string> cast, double t, int k, int frame)
        {
            var o = new JObject { ["k"] = k, ["t"] = t, ["frame"] = frame };
            var bones = new JObject();
            var holders = new JObject();
            var stains = new JObject();
            foreach (var kv in cast)
            {
                var c = Rt.FindChara(kv.Value);
                if (c == null || !c.Root) continue;
                if (c.Holder) holders[kv.Key] = V(c.Holder.position);
                var b = new JObject();
                int n = 0;
                foreach (var tr in c.Root.GetComponentsInChildren<Transform>(true))
                {
                    if (!tr.name.StartsWith("cf_J_", StringComparison.Ordinal)) continue;
                    string name = tr.name;
                    if (b[name] != null) name += "#" + n;
                    b[name] = V(tr.position);
                    n++;
                }
                bones[kv.Key] = b;
                var ws = InlineJ("{\"cmd\":\"wet.status\",\"key\":\"" + kv.Value + "\"}");
                var row = (ws["characters"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x["key"] == kv.Value) ?? (ws["characters"] as JArray)?.OfType<JObject>().FirstOrDefault();
                if (row != null && row["current"] != null && row["current"].Type != JTokenType.Null) stains[kv.Key] = row;
            }
            o["bones"] = bones;
            o["holders"] = holders;
            var cam = Camera.main ?? ViewCam;
            if (cam) o["camera"] = new JObject { ["pos"] = V(cam.transform.position), ["rot"] = new JArray(R6(cam.transform.rotation.x), R6(cam.transform.rotation.y), R6(cam.transform.rotation.z), R6(cam.transform.rotation.w)), ["fov"] = R6(cam.fieldOfView) };
            o["slots"] = InlineJ("{\"cmd\":\"overrides.list\"}")["overrides"];
            o["stains"] = stains;
            var rag = InlineJ("{\"cmd\":\"ragdoll.status\"}");
            rag.Remove("id");
            o["ragdoll"] = rag;
            return o;
        }

        // ------------------------------------------------------------ times on the main thread

        static double Median(List<double> v) { v.Sort(); return v.Count == 0 ? double.NaN : v[v.Count / 2]; }
        static double P95(List<double> v) { v.Sort(); return v.Count == 0 ? double.NaN : v[Math.Min(v.Count - 1, (int)Math.Ceiling(0.95 * v.Count) - 1)]; }

        static SceneScript FindScript(string id) => AppCore.Current.Session.Model.Scripts?.Find(id) ?? throw new BridgeException(BridgeCodes.NotFound, "no scene script " + id);

        Dictionary<string, object> Compile(string id, int rounds)
        {
            var s = FindScript(id);
            var cast = new Dictionary<string, string>(s.Cast);
            var first = ScriptCompiler.Compile(s, cast);
            var ms = new List<double>(rounds);
            for (int i = 0; i < rounds; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                ScriptCompiler.Compile(s, cast);
                ms.Add((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
            }
            var tl = first.Timeline;
            return new Dictionary<string, object>
            {
                ["rounds"] = rounds, ["medianMs"] = R4(Median(ms)), ["maxMs"] = R4(ms.Max()), ["tracks"] = tl?.Tracks?.Count ?? 0, ["keys"] = tl?.Tracks?.Sum(t => t.Keys.Count) ?? 0,
                ["events"] = tl?.Events?.Count ?? 0, ["keysReplaced"] = first.KeysReplaced, ["requestsReplaced"] = first.RequestsReplaced, ["cast"] = cast.Count,
            };
        }

        Dictionary<string, object> Parts(int rounds)
        {
            var moves = Rt.GetPart<ScriptedMoves>();
            var follow = Rt.GetPart<ScriptedFollow>();
            var dof = Rt.GetPart<ScriptedDof>();
            var sp = Rt.GetPart<ScriptPlayback>();
            var us = new List<double>(rounds);
            for (int i = 0; i < rounds; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                moves?.Tick(0f);
                follow?.Tick(0f);
                dof?.Tick(0f);
                moves?.LateApply();
                follow?.LateApply();
                dof?.LateApply();
                us.Add((Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency);
            }
            var counts = sp?.SlotCounts() ?? (0, 0, 0);
            // whether the script part holds its scene subscription (a private flag: it subscribes only during a play or a recording it watches)
            var subField = typeof(ScriptPlayback).GetField("subscribed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            object subscribed = sp == null ? (object)false : subField == null ? "not read (no field)" : subField.GetValue(sp);
            return new Dictionary<string, object>
            {
                ["subscribed"] = subscribed,
                ["rounds"] = rounds, ["medianUs"] = R4(Median(us)), ["p95Us"] = R4(P95(us)), ["moves"] = moves?.Count ?? 0, ["follow"] = follow?.Active ?? false, ["dof"] = dof?.Holding ?? false,
                ["blending"] = dof?.Blending ?? false, ["slots"] = new[] { counts.moves, counts.follow, counts.dof }, ["registered"] = new[] { moves != null, follow != null, dof != null, sp != null },
                ["running"] = sp?.Running != null && !sp.Running.IsDone,
            };
        }

        Dictionary<string, object> Play(string id, string prepare)
        {
            var app = AppCore.Current;
            long t0 = Stopwatch.GetTimestamp();
            var run = ScriptPlayer.Play(app, id, new ScriptPlayOptions { Prepare = prepare });
            double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            var r = run.Result;
            return new Dictionary<string, object>
            {
                ["ms"] = R4(ms), ["done"] = run.IsDone, ["ok"] = r?.Error == null, ["error"] = r?.Error?.ToString(), ["setupSent"] = r?.SetupSent ?? 0, ["prepareSent"] = r?.PrepareSent ?? 0,
                ["failures"] = r == null ? 0 : r.Failures.Count,
            };
        }
    }
}

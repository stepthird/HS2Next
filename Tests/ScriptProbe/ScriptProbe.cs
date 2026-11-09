// The test plugin of the row ScriptCheck.player (CC0-1.0). A player started with -noBridge has no listener, so a check cannot reach it over HTTP: this
// plugin reads a job file named on the command line (-scriptProbeJob <path>), runs the job on the player's main thread once the app is ready, writes
// its result file and quits the player. With the bridge on, the commands of ScriptProbeChecks.cs keep renders in memory, take digests at the frames
// of a recorded pass and time work on the main thread. It changes nothing a scene saves. Nothing is written as a picture: a capture's file is read
// as numbers and deleted.
//   scriptprobe.frames   the frame cap: vsync count and target frame rate
//   jobs (no listener)   "frames": the frame meter on the start scene (the build for an older player has only this one); "jx6": see ScriptProbeJx6.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Stp3.Test.ScriptProbe
{
    public sealed partial class ScriptProbePlugin : IPlugin
    {
        public const string JobArg = "-scriptProbeJob";

        IPluginContext ctx;
        JObject job;
        string jobPath;
        Runner runner;

        public void Load(IPluginContext context)
        {
            ctx = context;
            context.Commands.Register(new CommandSpec("scriptprobe.frames", "The frame cap: vsync count and target frame rate.")
                .Arg("vsync", ArgTypes.Int, "QualitySettings.vSyncCount").Arg("fps", ArgTypes.Int, "Application.targetFrameRate"), c =>
            {
                if (c.Args.Has("vsync")) QualitySettings.vSyncCount = c.Args.Get("vsync", 0);
                if (c.Args.Has("fps")) Application.targetFrameRate = c.Args.Get("fps", -1);
                return new Dictionary<string, object>
                {
                    ["vsync"] = QualitySettings.vSyncCount, ["fps"] = Application.targetFrameRate, ["screen"] = Screen.width + "x" + Screen.height,
                    ["device"] = SystemInfo.graphicsDeviceType.ToString(),
                };
            });
#if !FRAMES_ONLY
            RegisterChecks(context);
#endif
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, JobArg);
            if (i >= 0 && i + 1 < args.Length)
            {
                jobPath = args[i + 1];
                try { job = JObject.Parse(File.ReadAllText(jobPath)); }
                catch (Exception e) { job = null; WriteResult(jobPath + ".result.json", new JObject { ["ok"] = false, ["error"] = "the job file was not read: " + e.Message }); }
                if (job != null) context.Events.AppReady += StartJob;
            }
        }

        public void Unload()
        {
            if (runner) UnityEngine.Object.Destroy(runner.gameObject);
#if !FRAMES_ONLY
            UnloadChecks();
#endif
        }

        // ------------------------------------------------------------ the job of a player with no listener

        string ResultPath => (string)job?["result"] ?? jobPath + ".result.json";

        void StartJob()
        {
            ctx.Events.AppReady -= StartJob;
            var go = new GameObject("scriptprobe_runner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<Runner>();
            string kind = (string)job["kind"] ?? "";
            IEnumerator body;
            if (kind == "frames") body = FramesJob();
#if !FRAMES_ONLY
            else if (kind == "jx6") body = Jx6Job();
#endif
            else body = Unknown(kind);
            runner.StartCoroutine(Wrap(body));
        }

        IEnumerator Unknown(string kind)
        {
            result["error"] = "unknown job kind \"" + kind + "\"";
            result["ok"] = false;
            yield break;
        }

        readonly JObject result = new JObject();

        /// <summary>Runs the job; an exception is its error. The result file is written, then the player quits when the job asks (default).</summary>
        IEnumerator Wrap(IEnumerator body)
        {
            result["kind"] = (string)job["kind"];
            result["started"] = DateTime.UtcNow.ToString("o");
            while (true)
            {
                object cur;
                try
                {
                    if (!body.MoveNext()) break;
                    cur = body.Current;
                }
                catch (Exception e)
                {
                    result["ok"] = false;
                    result["error"] = e.GetType().Name + ": " + e.Message;
                    result["trace"] = e.StackTrace;
                    break;
                }
                yield return cur;
            }
            if (result["ok"] == null) result["ok"] = true;
            result["ended"] = DateTime.UtcNow.ToString("o");
            WriteResult(ResultPath, result);
            if ((bool?)job["quit"] ?? true)
            {
                yield return null;
                Application.Quit(0);
            }
        }

        static void WriteResult(string path, JObject o)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, o.ToString(Formatting.Indented));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        /// <summary>The frame meter: the frame cap off, warm frames, then rounds of frames whose unscaled time is kept; per round the median and p95 (ms).</summary>
        IEnumerator FramesJob()
        {
            int warm = (int?)job["warm"] ?? 120, frames = (int?)job["frames"] ?? 600, rounds = (int?)job["rounds"] ?? 3;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            for (int i = 0; i < warm; i++) yield return null;
            var rs = new JArray();
            for (int r = 0; r < rounds; r++)
            {
                var dts = new List<double>(frames);
                for (int i = 0; i < frames; i++)
                {
                    yield return null;
                    dts.Add(Time.unscaledDeltaTime * 1000.0);
                }
                dts.Sort();
                rs.Add(new JObject { ["frames"] = frames, ["medianMs"] = dts[dts.Count / 2], ["p95Ms"] = dts[Math.Min(dts.Count - 1, (int)Math.Ceiling(0.95 * dts.Count) - 1)], ["meanMs"] = dts.Average() });
            }
            result["rounds"] = rs;
            result["screen"] = Screen.width + "x" + Screen.height;
            result["device"] = SystemInfo.graphicsDeviceType.ToString();
            result["unity"] = Application.unityVersion;
        }

        /// <summary>The job runner's object: coroutines in real frames, after every plugin loaded.</summary>
        public sealed class Runner : MonoBehaviour { }
    }
}

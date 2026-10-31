// The background and frame images of a realized scene (R17–R19; Phase 3.6 package C).
// State: `scene.background`, `scene.frame` (`file` refs or null); the part never writes the model.
//   load      res refs: the bytes of the scene's res store, decoded with LoadImage (main thread); hs2 / user refs: the file
//             under the HS2 root / HS2Next's UserData, decoded off the main thread by UnityWebRequestTexture in play mode
//             and the player (synchronously with LoadImage in edit mode or with SyncLoads); no mips, sRGB, not readable;
//             the image appears when it is loaded (Pending is true meanwhile)
//   missing   the reference is kept (R19), nothing is drawn, `scene.backgroundMissing` / `scene.frameMissing` (warning,
//             once per value until the realization is cleared); a decode failure: `scene.imageDecodeFailed` (warning)
//   drawing   LateApply hands the textures to HS2BackgroundFeature / HS2FrameFeature (static inputs, so the runtime that
//             applied last owns them); Clear and Dispose take them back
//   thumbnails the runtime's scene thumbnail renderer (SceneOps.ThumbnailRenderer, the view camera's CaptureScene) is
//             wrapped in HS2BackgroundFeature.CaptureScope, so scene thumbnails show the background (never the frame)
// Registered by SceneRuntime.AddStandardParts (package K's line) or, before that exists, by Ensure from the capture
// commands and the UI. RuntimeOf(camera) finds the runtime of a view camera (bloom mapping of ViewCapture).
using System;
using System.Collections.Generic;
using System.IO;
using HS2Next.Save;
using UnityEngine;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    public sealed class SceneImages : ISceneRuntimePart, IDisposable
    {
        public const string StateNone = "none", StateLoading = "loading", StateLoaded = "loaded", StateMissing = "missing", StateFailed = "failed";

        /// <summary>One image of the scene.</summary>
        public sealed class Slot
        {
            /// <summary>"background" or "frame" (the scene field).</summary>
            public readonly string Field;
            /// <summary>The reference being shown (a copy of the stored value), or null.</summary>
            public FileRef Ref { get; internal set; }
            public string State { get; internal set; } = StateNone;
            public Texture2D Texture { get; internal set; }
            public string Error { get; internal set; }
            /// <summary>"res:…", "hs2:…", "user:…" (FileRefs.Describe).</summary>
            public string Source { get; internal set; }
            public int Width => Texture ? Texture.width : 0;
            public int Height => Texture ? Texture.height : 0;
            /// <summary>Decode time of the last load (ms; the asynchronous loads: from the request to the texture).</summary>
            public double LoadMs { get; internal set; }

            internal string Key;
            internal int Generation;
            internal UnityWebRequest Request;
            internal float RequestedAt;

            internal Slot(string field) { Field = field; }
        }

        /// <summary>Force synchronous loads (default: synchronous outside play mode).</summary>
        public static bool SyncLoads;

        static readonly List<WeakReference<SceneImages>> instances = new List<WeakReference<SceneImages>>();

        SceneRuntime runtime;
        bool dirtyBg = true, dirtyFrame = true, disposed;
        Func<byte[]> innerThumb, wrappedThumb;

        public Slot Background { get; } = new Slot("background");
        public Slot Frame { get; } = new Slot("frame");
        public SceneRuntime Runtime => runtime;

        /// <summary>True while an image is loading.</summary>
        public bool Pending => Background.State == StateLoading || Frame.State == StateLoading || dirtyBg || dirtyFrame;

        /// <summary>The part of <paramref name="rt"/>, added when missing (after the standard parts).</summary>
        public static SceneImages Ensure(SceneRuntime rt)
        {
            if (rt == null || rt.IsDisposed) return null;
            var p = rt.GetPart<SceneImages>();
            if (p != null) return p;
            p = new SceneImages();
            rt.AddPart(p, p.Dispose);
            return p;
        }

        /// <summary>The runtime whose view camera is <paramref name="cam"/> (among runtimes with this part), or null.</summary>
        public static SceneRuntime RuntimeOf(Camera cam)
        {
            if (!cam) return null;
            lock (instances)
            {
                for (int i = instances.Count - 1; i >= 0; i--)
                {
                    if (!instances[i].TryGetTarget(out var s) || s.disposed || s.runtime == null || s.runtime.IsDisposed) { instances.RemoveAt(i); continue; }
                    if (s.runtime.GetPart<SceneViewCamera>()?.Camera == cam) return s.runtime;
                }
            }
            return null;
        }

        // ------------------------------------------------------------ ISceneRuntimePart

        public void Attach(SceneRuntime r)
        {
            runtime = r;
            lock (instances) instances.Add(new WeakReference<SceneImages>(this));
            dirtyBg = dirtyFrame = true;
            WrapThumbnail();
        }

        public void OnChanged(SceneChangeBatch b)
        {
            foreach (var c in b.Changes)
            {
                if (c.Kind != SceneChangeKind.SceneChanged) continue;
                if (c.Path == null) { dirtyBg = dirtyFrame = true; continue; }
                if (FieldPath.Overlaps("background", c.Path)) dirtyBg = true;
                if (FieldPath.Overlaps("frame", c.Path)) dirtyFrame = true;
            }
        }

        public void Apply()
        {
            if (runtime == null || disposed) return;
            var s = runtime.Model.Settings;
            if (dirtyBg) { dirtyBg = false; Load(Background, s.Background); }
            if (dirtyFrame) { dirtyFrame = false; Load(Frame, s.Frame); }
            Poll(Background);
            Poll(Frame);
        }

        public void LateApply()
        {
            if (runtime == null || disposed) return;
            Poll(Background);
            Poll(Frame);
            HS2BackgroundFeature.Texture = Background.Texture;
            HS2FrameFeature.Texture = Frame.Texture;
            WrapThumbnail();
        }

        public void Clear()
        {
            Release(Background);
            Release(Frame);
            TakeBack();
            dirtyBg = dirtyFrame = true;
        }

        public void OnLoadedRealized() { dirtyBg = dirtyFrame = true; }

        public void Dispose()
        {
            if (disposed) return;
            Clear();
            disposed = true;
            if (runtime?.Ops != null && wrappedThumb != null && runtime.Ops.ThumbnailRenderer == wrappedThumb) runtime.Ops.ThumbnailRenderer = innerThumb;
            wrappedThumb = innerThumb = null;
            lock (instances) instances.RemoveAll(w => !w.TryGetTarget(out var s) || s == this);
        }

        /// <summary>Loads what the model holds now (tests, the capture commands: no frame needed).</summary>
        public void ApplyNow()
        {
            dirtyBg = dirtyFrame = true;
            Apply();
            LateApply();
        }

        void TakeBack()
        {
            if (HS2BackgroundFeature.Texture != null && HS2BackgroundFeature.Texture == Background.Texture) HS2BackgroundFeature.Texture = null;
            if (HS2FrameFeature.Texture != null && HS2FrameFeature.Texture == Frame.Texture) HS2FrameFeature.Texture = null;
            // a released texture is destroyed: the features must not keep it
            if (!HS2BackgroundFeature.Texture) HS2BackgroundFeature.Texture = null;
            if (!HS2FrameFeature.Texture) HS2FrameFeature.Texture = null;
        }

        void WrapThumbnail()
        {
            var ops = runtime?.Ops;
            if (ops == null) return;
            var cur = ops.ThumbnailRenderer;
            if (cur == null || cur == wrappedThumb) return;
            innerThumb = cur;
            var inner = cur;
            wrappedThumb = () =>
            {
                using (HS2BackgroundFeature.CaptureScope()) return inner();
            };
            ops.ThumbnailRenderer = wrappedThumb;
        }

        // ------------------------------------------------------------ loading

        static string KeyOf(FileRef f) => f == null ? null : f.Kind + "|" + f.Value;

        void Load(Slot s, FileRef f)
        {
            string key = KeyOf(f);
            if (key == s.Key && (s.State == StateLoaded || s.State == StateLoading)) return;
            Release(s);
            s.Key = key;
            s.Generation++;
            s.Ref = f == null ? null : new FileRef { Kind = f.Kind, Value = f.Value };
            s.Source = FileRefs.Describe(f);
            if (f == null) { s.State = StateNone; return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            switch (f.Kind)
            {
                case FileRef.Kinds.Res:
                {
                    var bytes = runtime.Session?.Model?.File?.Res?.Get(f.Value);
                    if (bytes == null) { Missing(s, "the scene has no res entry " + f.Value); return; }
                    Decode(s, bytes);
                    s.LoadMs = sw.Elapsed.TotalMilliseconds;
                    return;
                }
                case FileRef.Kinds.Hs2:
                case FileRef.Kinds.User:
                {
                    string path = FileRefs.PathOf(f, runtime.Services?.Hs2Root, runtime.Services?.UserData);
                    if (path == null) { Missing(s, (f.Kind == FileRef.Kinds.Hs2 ? "no HS2 install or a bad path for " : "no UserData or a bad path for ") + f.Value); return; }
                    if (!File.Exists(path)) { Missing(s, "file not found: " + f.Value); return; }
                    if (SyncLoads || !Application.isPlaying)
                    {
                        byte[] bytes;
                        try { bytes = File.ReadAllBytes(path); }
                        catch (Exception e) { Failed(s, "cannot read " + f.Value + ": " + e.Message); return; }
                        Decode(s, bytes);
                        s.LoadMs = sw.Elapsed.TotalMilliseconds;
                        return;
                    }
                    var prm = DownloadedTextureParams.Default;
                    prm.mipmapChain = false;
                    prm.readable = false;
                    prm.linearColorSpace = false;
                    var rq = UnityWebRequestTexture.GetTexture(new Uri(path), prm);
                    rq.SendWebRequest();
                    s.Request = rq;
                    s.RequestedAt = Time.realtimeSinceStartup;
                    s.State = StateLoading;
                    return;
                }
                default:
                    Missing(s, "a file reference of an unknown kind");
                    return;
            }
        }

        void Poll(Slot s)
        {
            var rq = s.Request;
            if (rq == null || !rq.isDone) return;
            s.Request = null;
            try
            {
                if (rq.result != UnityWebRequest.Result.Success) { Failed(s, s.Ref?.Value + ": " + rq.error); return; }
                var t = DownloadHandlerTexture.GetContent(rq);
                if (!t) { Failed(s, s.Ref?.Value + " did not decode"); return; }
                t.name = "hs2next_" + s.Field;
                t.hideFlags = HideFlags.DontSave;
                t.wrapMode = TextureWrapMode.Clamp;
                s.Texture = t;
                s.State = StateLoaded;
                s.Error = null;
                s.LoadMs = (Time.realtimeSinceStartup - s.RequestedAt) * 1000.0;
            }
            catch (Exception e) { Failed(s, s.Ref?.Value + ": " + e.Message); }
            finally { rq.Dispose(); }
        }

        void Decode(Slot s, byte[] bytes)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, false) { name = "hs2next_" + s.Field, hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            bool ok;
            try { ok = t.LoadImage(bytes, true); }
            catch (Exception) { ok = false; }
            if (!ok)
            {
                Object.DestroyImmediate(t);
                Failed(s, (s.Ref?.Value ?? "the image") + " is not a readable PNG / JPEG");
                return;
            }
            s.Texture = t;
            s.State = StateLoaded;
            s.Error = null;
        }

        void Missing(Slot s, string why)
        {
            s.State = StateMissing;
            s.Error = why;
            runtime.ReportOnce(FindingSeverity.Warning, s.Field == "frame" ? "scene.frameMissing" : "scene.backgroundMissing", null,
                "the " + s.Field + " image is missing (" + why + "): the reference is kept, nothing is drawn");
        }

        void Failed(Slot s, string why)
        {
            s.State = StateFailed;
            s.Error = why;
            runtime.ReportOnce(FindingSeverity.Warning, "scene.imageDecodeFailed", null, "the " + s.Field + " image does not decode (" + why + ")");
        }

        void Release(Slot s)
        {
            if (s.Request != null) { s.Request.Abort(); s.Request.Dispose(); s.Request = null; }
            if (s.Texture)
            {
                if (HS2BackgroundFeature.Texture == s.Texture) HS2BackgroundFeature.Texture = null;
                if (HS2FrameFeature.Texture == s.Texture) HS2FrameFeature.Texture = null;
                Object.DestroyImmediate(s.Texture);
            }
            s.Texture = null;
            s.State = StateNone;
            s.Error = null;
            s.Key = null;
            s.Ref = null;
            s.Source = null;
        }
    }
}

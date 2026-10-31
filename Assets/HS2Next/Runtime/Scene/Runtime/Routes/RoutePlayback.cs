// Route playback on the scene clock (Phase 3.6 package R): a scene part
// (ISceneRuntimePart + ISceneTickPart, registered by SceneRuntime.AddStandardParts) that keeps every built route's
// runtime state and writes its child root's local pose.
//   State per route: Stopped | Playing(τ) | Ended, from `route.playing` in the model: on build / load playing → Playing(0)
//   (vanilla restarts a playing route at load); a change false → true → Playing(0); true → false → Stopped. Ended (no
//   loop, τ ≥ D) keeps `route.playing` true while the route stands at its end; before a save the ended routes are
//   written `playing = false` (one silent batch, origin Capture), so a reloaded file is stopped, as HS2's.
//   Apply (−5000, before the motion stages): dirty paths are rebuilt (τ continues; a non-loop route whose new total is
//   ≤ τ ends) and the pose written at the current τ; Tick(dt) (the clock's FrameDt once per frame, dt per step): τ += dt
//   for playing routes, evaluate, write the pose. Position = the evaluator's sample (route-local); rotation by orient:
//   none → the start point's rotation; xy → the look rotation of (look target − position) with the world up; y → its
//   world yaw with the start point's world pitch and roll; both smoothed (R6) by q ← slerp(q, target, 1 − 0.855^(60·dt))
//   (HS2's lag at 60 fps, frame-rate independent), exact with RouteSettings.OrientSmoothing false. Stopped: the start
//   point's pose; Ended: the end point with the last rotation.
// Findings: route.zeroSpeed, route.unknownEase, route.pointRotIgnored from the evaluator (reported once per route).
using System;
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Scenes.Routes;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>Process-wide route options (the UI keeps them in step with its options routes.*; defaults vanilla).</summary>
    public static class RouteSettings
    {
        static RouteTiming timing = RouteTiming.Hs2;
        static bool smoothing = true, pointLimit = true;
        static float lineWidth = 1f;

        /// <summary>Raised after a setting changed (paths are rebuilt for the timing).</summary>
        public static event Action Changed;

        /// <summary>routes.timing (R3): HS2's twice-padded length (default) or the curve length.</summary>
        public static RouteTiming Timing { get => timing; set { if (timing != value) { timing = value; Raise(); } } }
        /// <summary>routes.orientSmoothing (R6): HS2's lag (default) or the exact look direction.</summary>
        public static bool OrientSmoothing { get => smoothing; set { if (smoothing != value) { smoothing = value; Raise(); } } }
        /// <summary>routes.pointLimit (R9; vanilla routePointLimit): at most 11 points (default on).</summary>
        public static bool PointLimit { get => pointLimit; set { if (pointLimit != value) { pointLimit = value; Raise(); } } }
        /// <summary>routes.lineWidth (0.1–4, default 1; × 16 logical px).</summary>
        public static float LineWidth { get => lineWidth; set { if (lineWidth != value) { lineWidth = value; Raise(); } } }

        public static RouteOptions Options() => new RouteOptions { Timing = timing };

        /// <summary>Back to the defaults (tests).</summary>
        public static void Reset()
        {
            timing = RouteTiming.Hs2; smoothing = true; pointLimit = true; lineWidth = 1f;
            Raise();
        }

        static void Raise()
        {
            var h = Changed;
            if (h == null) return;
            foreach (Action d in h.GetInvocationList())
            {
                try { d(); } catch (Exception e) { Debug.LogWarning("route settings: Changed handler threw: " + e); }
            }
        }
    }

    public sealed class RoutePlayback : ISceneRuntimePart, ISceneTickPart, IDisposable
    {
        /// <summary>HS2's orient lag: the share of the angle left after 1/60 s (SmoothDamp from rest, smooth time 0.05 s).</summary>
        public const double OrientKeep = 0.855;

        SceneRuntime rt;
        readonly List<RouteRealization> routes = new List<RouteRealization>();
        bool settingsDirty;

        public void Attach(SceneRuntime r)
        {
            rt = r;
            r.Session.Events.Saving += OnSaving;
            RouteSettings.Changed += OnSettings;
        }

        public void Dispose()
        {
            if (rt != null) rt.Session.Events.Saving -= OnSaving;
            RouteSettings.Changed -= OnSettings;
            routes.Clear();
        }

        void OnSettings() => settingsDirty = true;

        internal void Register(RouteRealization r)
        {
            if (!routes.Contains(r)) routes.Add(r);
            r.PathDirty = true;
            r.Initialised = false;
            Sync(r);
            WritePose(r, 0f, false);
        }

        internal void Unregister(RouteRealization r) => routes.Remove(r);

        /// <summary>The route realizations this part drives (in the model or buried).</summary>
        public IReadOnlyList<RouteRealization> Routes => routes;

        bool Live(RouteRealization r) => r.State == RealizationState.Built && r.ChildRoot && !r.InGraveyard && rt.IsInModel(r);

        // ------------------------------------------------------------ ISceneRuntimePart

        public void OnChanged(SceneChangeBatch b) { }

        public void Apply()
        {
            if (settingsDirty)
            {
                settingsDirty = false;
                foreach (var r in routes) r.PathDirty = true;
            }
            for (int i = 0; i < routes.Count; i++)
            {
                var r = routes[i];
                if (!Live(r)) continue;
                if (Sync(r)) WritePose(r, 0f, false);
            }
        }

        public void LateApply() { }

        public void Clear() => routes.Clear();

        public void OnLoadedRealized() { }

        // ------------------------------------------------------------ ISceneTickPart

        public void Tick(float dt)
        {
            if (!(dt >= 0f) || float.IsInfinity(dt)) dt = 0f;
            for (int i = 0; i < routes.Count; i++)
            {
                var r = routes[i];
                if (!Live(r)) continue;
                Sync(r);
                if (r.PlayState == RoutePlayState.Playing && r.Path != null && r.Path.CanPlay)
                {
                    r.Time += dt;
                    EndIfFinished(r);
                }
                WritePose(r, dt, true);
            }
        }

        // ------------------------------------------------------------ state

        /// <summary>Rebuilds a dirty path and follows the model's `playing`; true when something changed.</summary>
        bool Sync(RouteRealization r)
        {
            var st = r.Route;
            if (st == null) return false;
            bool changed = false;
            if (r.PathDirty || r.Path == null)
            {
                r.PathDirty = false;
                r.Path = RoutePath.Build(st, RouteSettings.Options());
                r.PathVersion++;
                foreach (var f in r.Path.Findings)
                    rt.ReportOnce(f.Severity, f.Code, r.NodeId, f.Path + ": " + f.Message);
                changed = true;
                EndIfFinished(r);
            }
            if (!r.Initialised)
            {
                r.Initialised = true;
                r.ModelPlaying = st.Playing;
                if (st.Playing) Begin(r); else Halt(r);
                return true;
            }
            if (st.Playing != r.ModelPlaying)
            {
                r.ModelPlaying = st.Playing;
                if (st.Playing) Begin(r); else Halt(r);
                changed = true;
            }
            return changed;
        }

        /// <summary>A playing non-loop route whose τ reached its total ends (an ended route stays ended until Play).</summary>
        static void EndIfFinished(RouteRealization r)
        {
            var p = r.Path;
            if (r.PlayState != RoutePlayState.Playing || p == null || p.Mode != RoutePlayMode.Once) return;
            double d = p.TotalDuration;
            if (r.Time >= d) { r.Time = d; r.PlayState = RoutePlayState.Ended; }
        }

        void Begin(RouteRealization r)
        {
            r.PlayState = RoutePlayState.Playing;
            r.Time = 0;
            r.OrientRot = StartRotation(r);
            // HS2 runs one orient step in the play frame itself (LateUpdate, J2): one filter step of 1/60 s toward the τ 0 target
            var orient = r.Route?.OrientType ?? RouteOrient.None;
            if (orient != RouteOrient.None && RouteSettings.OrientSmoothing && r.Path != null && r.Path.CanPlay && TryLook(r, r.Path.Evaluate(0), orient, out var target))
                r.OrientRot = Quaternion.Slerp(r.OrientRot, target, (float)(1.0 - OrientKeep));
        }

        void Halt(RouteRealization r)
        {
            r.PlayState = RoutePlayState.Stopped;
            r.Time = 0;
            r.OrientRot = StartRotation(r);
        }

        /// <summary>Starts (or restarts) a route at τ 0 (the ops' Play; the model's `playing` is the caller's).</summary>
        public void Restart(RouteRealization r)
        {
            if (r == null) return;
            Sync(r);
            r.ModelPlaying = r.Route?.Playing ?? false;
            Begin(r);
            WritePose(r, 0f, false);
        }

        /// <summary>Stops a route (the start point's pose; the model's `playing` is the caller's).</summary>
        public void Stop(RouteRealization r)
        {
            if (r == null) return;
            Sync(r);
            r.ModelPlaying = r.Route?.Playing ?? false;
            Halt(r);
            WritePose(r, 0f, false);
        }

        /// <summary>The state word of the bridge and the UI: stopped, playing or ended.</summary>
        public static string StateName(RoutePlayState s) => s == RoutePlayState.Playing ? "playing" : s == RoutePlayState.Ended ? "ended" : "stopped";

        // ------------------------------------------------------------ pose

        static Quaternion StartRotation(RouteRealization r)
        {
            var pts = r.Route?.Points;
            return pts != null && pts.Count > 0 ? Quaternion.Euler(UnityObjects.V(pts[0].Rot)) : Quaternion.identity;
        }

        static Vector3 V(Vec3d v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);

        /// <summary>Writes the child root's local pose for the route's state; <paramref name="advance"/> runs the orient filter by dt.</summary>
        void WritePose(RouteRealization r, float dt, bool advance)
        {
            var root = r.ChildRoot;
            if (!root) return;
            var st = r.Route;
            var path = r.Path;
            if (st == null || path == null || r.PlayState == RoutePlayState.Stopped || !path.CanPlay)
            {
                var s0 = path != null ? path.Evaluate(0) : default;
                s0.Valid = false;
                r.Sample = s0;
                r.OrientRot = StartRotation(r);
                r.ApplyStartPose();
                return;
            }
            // an ended non-loop route stands at its end (also after an edit that lengthened the path)
            var s = path.Evaluate(r.PlayState == RoutePlayState.Ended && path.Mode == RoutePlayMode.Once ? double.MaxValue : r.Time);
            r.Sample = s;
            root.localPosition = V(s.Position);
            var orient = st.OrientType ?? RouteOrient.None;
            if (orient == RouteOrient.None) r.OrientRot = StartRotation(r);
            else if (r.PlayState == RoutePlayState.Playing && (advance || !RouteSettings.OrientSmoothing))
            {
                if (TryLook(r, s, orient, out var target))
                {
                    if (!RouteSettings.OrientSmoothing) r.OrientRot = target;
                    else if (dt > 0f)
                    {
                        float k = (float)(1.0 - Math.Pow(OrientKeep, 60.0 * dt));
                        r.OrientRot = Quaternion.Slerp(r.OrientRot, target, k);
                    }
                }
            }
            root.localRotation = r.OrientRot;
            root.localScale = Vector3.one;
        }

        /// <summary>The orient target as a local rotation of the child root (false when the look direction is degenerate).</summary>
        static bool TryLook(RouteRealization r, RouteSample s, RouteOrient orient, out Quaternion local)
        {
            local = r.OrientRot;
            var dirLocal = V(s.LookTarget) - V(s.Position);
            if (dirLocal.sqrMagnitude < 1e-12f) return false;
            var holder = r.Holder;
            var R = holder ? holder.rotation : Quaternion.identity;
            var dirWorld = R * dirLocal;
            var look = Quaternion.LookRotation(dirWorld, Vector3.up);
            if (orient == RouteOrient.Y)
            {
                var ws = (R * StartRotation(r)).eulerAngles;
                look = Quaternion.Euler(ws.x, look.eulerAngles.y, ws.z);
            }
            local = Quaternion.Inverse(R) * look;
            return true;
        }

        // ------------------------------------------------------------ save capture

        /// <summary>Ended routes are saved stopped (HS2's file has active false after the end).</summary>
        void OnSaving()
        {
            if (rt == null || rt.IsDisposed) return;
            var ended = new List<RouteRealization>();
            foreach (var r in routes)
                if (Live(r) && r.PlayState == RoutePlayState.Ended && r.Route != null && r.Route.Playing) ended.Add(r);
            if (ended.Count == 0) return;
            try
            {
                rt.Session.WriteSilently(ChangeOrigin.Capture, w =>
                {
                    foreach (var r in ended) w.Set(r.NodeId, "route.playing", false);
                });
            }
            catch (Exception e) { Debug.LogWarning("route playback: capture before save failed: " + e.Message); }
            foreach (var r in ended) r.ModelPlaying = false;
        }
    }
}

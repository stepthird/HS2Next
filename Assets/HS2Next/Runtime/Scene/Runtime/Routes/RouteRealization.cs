// The realization of a route node (replaces the static 2.2 class of
// SimpleRealizations.cs): the holder (the route's transform; point positions are route-local) and its ChildRoot, the
// only thing that moves while the route plays; riders hang under the ChildRoot. The playback state (stopped / playing /
// ended, the route time τ, the cached RoutePath, the orient filter) lives here and is driven by the RoutePlayback scene
// part on the scene clock; `route.*` changes mark the path dirty (rebuilt at the next apply, τ continues).
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Scenes.Routes;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>Runtime state of a route (not saved; `route.playing` in the model drives it).</summary>
    public enum RoutePlayState { Stopped, Playing, Ended }

    public sealed class RouteRealization : NodeRealization
    {
        internal RouteRealization(SceneRuntime runtime, TreeNode node) : base(runtime, node) { }

        /// <summary>Where children attach: the child root (the start point's pose while stopped, the path while playing).</summary>
        public Transform ChildRoot { get; private set; }

        public RouteNodeState Route => Node.State as RouteNodeState;

        // ------------------------------------------------------------ playback state (RoutePlayback)

        /// <summary>The evaluator built from the document (rebuilt when `route.*` or the timing option changes).</summary>
        public RoutePath Path { get; internal set; }
        /// <summary>Incremented on every path rebuild (line caches follow it).</summary>
        public int PathVersion { get; internal set; }
        internal bool PathDirty = true;
        public RoutePlayState PlayState { get; internal set; }
        /// <summary>The route time τ in scene seconds since Play (0 while stopped).</summary>
        public double Time { get; internal set; }
        /// <summary>The last evaluation (route-local; Valid false while stopped or with one point).</summary>
        public RouteSample Sample { get; internal set; }
        /// <summary>The model's `route.playing` as last seen (transitions false → true / true → false drive the state).</summary>
        internal bool ModelPlaying;
        internal bool Initialised;
        /// <summary>The child root's local rotation under orient (the smoothing filter's state).</summary>
        internal Quaternion OrientRot = Quaternion.identity;

        List<Vec3d> line;
        int lineVersion = -1;

        /// <summary>The line's route-local vertices for the current path (cached per <see cref="PathVersion"/>).</summary>
        public IReadOnlyList<Vec3d> LineVertices
        {
            get
            {
                if (line == null || lineVersion != PathVersion)
                {
                    line = RouteLine.Vertices(Route, Path);
                    lineVersion = PathVersion;
                }
                return line;
            }
        }

        // ------------------------------------------------------------ realization hooks

        protected internal override void BuildContent()
        {
            var t = new GameObject("ChildRoot").transform;
            t.SetParent(Holder, false);
            ChildRoot = t;
            PathDirty = true;
            Initialised = false;
            State = RealizationState.Built;
            var pb = Runtime.GetPart<RoutePlayback>();
            if (pb != null) pb.Register(this);
            else ApplyStartPose();
        }

        /// <summary>The child root at the start point's pose (stopped; also the pose without a playback part).</summary>
        internal void ApplyStartPose()
        {
            if (!ChildRoot) return;
            var pts = Route?.Points;
            var p0 = pts != null && pts.Count > 0 ? pts[0] : null;
            ChildRoot.localPosition = p0 != null ? UnityObjects.V(p0.Pos) : Vector3.zero;
            ChildRoot.localRotation = p0 != null ? Quaternion.Euler(UnityObjects.V(p0.Rot)) : Quaternion.identity;
            ChildRoot.localScale = Vector3.one;
        }

        protected internal override void DestroyContent()
        {
            Runtime.GetPart<RoutePlayback>()?.Unregister(this);
            if (ChildRoot) UnityObjects.Destroy(ChildRoot.gameObject);
            ChildRoot = null;
        }

        protected internal override void ReleaseContent() => Runtime.GetPart<RoutePlayback>()?.Unregister(this);

        protected internal override Transform ChildAttach(string point, out AttachNote note)
        {
            note = point != null ? new AttachNote(FindingSeverity.Info, "scene.pointIgnored", "attach point \"" + point + "\" under a route is ignored") : default;
            return ChildRoot ? ChildRoot : Holder;
        }

        protected internal override void ApplyChanges(IReadOnlyCollection<string> paths)
        {
            if (Touches(paths, "route")) PathDirty = true;
            if (Runtime.GetPart<RoutePlayback>() == null && Touches(paths, "route.points")) ApplyStartPose();
        }

        /// <summary>The line and the markers are view-only (drawn by the UI's gizmo pass): nothing to hide here.</summary>
        protected internal override void ApplyVisibility(bool visible) { }
    }
}

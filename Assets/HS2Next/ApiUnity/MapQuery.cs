// The map query (engine side of the plugin Api; experimental: it may change in any minor release): the loaded map's roots and
// the height of its surface under a point. The answer covers every map, also one whose meshes cannot be read and one without
// colliders. Characters and items are not the map. Alpha-cut and transparent surfaces count (water, glass, leaves).
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Api.Unity
{
    /// <summary>The loaded map and its surface. Main thread only. Implemented by the host.</summary>
    [Experimental("the map query may change in any minor release")]
    public interface IMapQuery
    {
        /// <summary>True while a map is loaded.</summary>
        bool Loaded { get; }
        /// <summary>Every root object of the map; empty when none is loaded.</summary>
        IReadOnlyList<GameObject> Roots { get; }
        /// <summary>
        /// The highest surface of the map under the (x, z) of <paramref name="point"/>, between <c>point.y + above</c> and
        /// <c>point.y - below</c>. A surface whose normal has a y below 0.3, up or down, is a wall and does not count. False when there
        /// is none or no map is loaded. Pass the point you stand at: a point under a roof finds the floor, a point above the roof finds
        /// the roof. The call waits for its answer, about a millisecond and longer for the first of a map, so it suits a click or a
        /// few points, not many points on every frame.
        /// </summary>
        /// <param name="point">The point, in world space.</param>
        /// <param name="hit">The answer.</param>
        /// <param name="above">How far above the point the search starts.</param>
        /// <param name="below">How far below the point it ends.</param>
        bool Surface(Vector3 point, out MapSurfaceHit hit, float above = 0f, float below = 60f);
        /// <summary>
        /// <see cref="Surface"/> for several points with one wait. Returns how many have an answer; <paramref name="hits"/> holds each
        /// point's answer at its index, and <see cref="MapSurfaceHit.Source"/> is null at a point with none. ArgumentException when
        /// <paramref name="hits"/> is shorter than <paramref name="points"/>.
        /// </summary>
        /// <param name="points">The points, in world space.</param>
        /// <param name="hits">Receives the answers.</param>
        /// <param name="above">How far above each point the search starts.</param>
        /// <param name="below">How far below each point it ends.</param>
        int Surfaces(IReadOnlyList<Vector3> points, MapSurfaceHit[] hits, float above = 0f, float below = 60f);
    }

    /// <summary>The map's surface at one point.</summary>
    [Experimental("the map query may change in any minor release")]
    public struct MapSurfaceHit
    {
        /// <summary>The surface's height (world y).</summary>
        public float Height;
        /// <summary>The surface's unit normal in world space, pointing up.</summary>
        public Vector3 Normal;
        /// <summary>How it was found: "gpu", "mesh", "terrain" or "collider"; null when there is no answer.</summary>
        public string Source;
    }
}

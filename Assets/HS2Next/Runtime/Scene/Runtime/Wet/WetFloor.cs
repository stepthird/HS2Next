// The height a character's drops land on, and what it was taken from.
//   map     the map's surface under the origin as it stands now, searched downwards; with no answer the feet, and the source says so
//   feet    the lower of the two toes, less 0.31 of the body's scale
//   fixed   the height the block holds
// Then the block's offset. Never a character's root, which can lie well below its sole, and never one root of the map. A query runs at a bake, at a start,
// when the map changes and while drops fall, when the origin moved more than 0.05 units, at most every 0.25 s of scene time.
using HS2Next.Api.Unity;
using HS2Next.Chara;
using HS2Next.Save;
using HS2Next.Scenes.Wet;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>What a floor's height came from, as <c>wet.status</c> and <c>wet.floor</c> give it.</summary>
    public static class WetFloorSources
    {
        public const string Map = "map", Feet = "feet", Fixed = "fixed", None = "none";
    }

    public sealed class WetFloor
    {
        /// <summary>How far the origin moves before the floor is asked again, and how often at most (scene seconds).</summary>
        public const float MoveLimit = 0.05f, QueryEvery = 0.25f;
        /// <summary>The toes stand this far above the sole, as a part of the body's scale.</summary>
        public const float ToeHeight = 0.31f;

        Vector3 asked;
        float sinceQuery = float.MaxValue;
        bool hasAsked;

        /// <summary>The floor now, offset included.</summary>
        public float Y { get; private set; }
        /// <summary>One of <see cref="WetFloorSources"/>.</summary>
        public string Source { get; private set; } = WetFloorSources.None;
        /// <summary>Queries of the map so far (tests).</summary>
        public int Queries { get; private set; }

        /// <summary>The height of <paramref name="v"/> for a character whose origin stands at <paramref name="origin"/>.</summary>
        public void Measure(SceneRuntime rt, CharaContext cc, WetFloorValues v, Vector3 origin, WetFrame frame, bool hasFrame)
        {
            float y;
            string source;
            if (v.AppliedMode == WetFloorValues.ModeFixed)
            {
                y = v.Height;
                source = WetFloorSources.Fixed;
            }
            else if (v.AppliedMode == WetFloorValues.ModeMap && MapHeight(rt, origin, out y))
            {
                source = WetFloorSources.Map;
                Queries++;
            }
            else if (Feet(cc, frame, hasFrame, out y)) source = WetFloorSources.Feet;
            else
            {
                y = Y - LastOffset;
                source = WetFloorSources.None;
            }
            LastOffset = v.AppliedOffset;
            Y = y + LastOffset;
            Source = source;
            asked = origin;
            hasAsked = true;
            sinceQuery = 0f;
        }

        /// <summary>The offset in <see cref="Y"/>.</summary>
        public float LastOffset { get; private set; }

        /// <summary>While drops fall: true when the floor should be measured again.</summary>
        public bool Due(float dt, Vector3 origin)
        {
            sinceQuery += dt;
            return !hasAsked || (sinceQuery >= QueryEvery && (origin - asked).sqrMagnitude > MoveLimit * MoveLimit);
        }

        static bool Feet(CharaContext cc, WetFrame frame, bool hasFrame, out float y)
        {
            y = 0f;
            if (cc?.BodyBones == null || !hasFrame) return false;
            bool any = false;
            float low = float.MaxValue;
            foreach (string n in new[] { "cf_J_Toes01_L", "cf_J_Toes01_R" })
                if (cc.BodyBones.TryGetValue(n, out var t) && t) { low = Mathf.Min(low, t.position.y); any = true; }
            if (!any) return false;
            y = low - ToeHeight * (float)frame.Scale;
            return true;
        }

        static bool MapHeight(SceneRuntime rt, Vector3 point, out float y)
        {
            y = 0f;
            var q = Query(rt);
            if (q == null || !q.Surface(point, out MapSurfaceHit hit)) return false;
            y = hit.Height;
            return true;
        }

        public static IMapQuery Query(SceneRuntime rt) => MapSurface.Of(rt);
    }
}

// The built hierarchy as the constraint graph sees it: every transform a compile names gets a small number, with its parent's and its
// depth, so "lies at or under" is a walk over two lists and the engine is asked once per transform. It holds for one compile: a tree
// move or a rebuild makes a new one.
using System.Collections.Generic;
using HS2Next.Scenes.Constraints;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class ConstraintTopology : IConstraintTree
    {
        readonly Dictionary<Transform, int> places = new Dictionary<Transform, int>();
        readonly List<int> parents = new List<int>();
        readonly List<int> depths = new List<int>();

        /// <summary>The place of a transform and of everything above it; <see cref="ConstraintGraph.NoPlace"/> for none.</summary>
        public int Place(Transform t)
        {
            if (!t) return ConstraintGraph.NoPlace;
            if (places.TryGetValue(t, out int p)) return p;
            int up = Place(t.parent);
            p = parents.Count;
            parents.Add(up);
            depths.Add(up < 0 ? 0 : depths[up] + 1);
            places[t] = p;
            return p;
        }

        public bool Under(int a, int b)
        {
            if (a < 0 || b < 0) return false;
            int up = depths[a] - depths[b];
            if (up < 0) return false;
            for (; up > 0; up--) a = parents[a];
            return a == b;
        }
    }
}

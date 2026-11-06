// The order of one frame's motion pass, without the engine. The vertices are the units M(x), the motion step of a character or of an
// item with motion, and the constraints K(k) whose target is a node or an IK target; a constraint with a bone target runs inside the
// step of its character. The edges say what must have run first:
//   E1 M(x) -> M(y)   y's holder lies under x's root: the parent moves first
//   E2 M(x) -> K(k)   x owns k's source (a bone target on y: M(x) -> M(y), unless x is y)                       droppable
//   E3 M(x) -> K(k)   x owns the frame of k's target: a node's holder parent; for an IK target the unit y hangs under, never y itself
//   E4 K(k) -> M(y)   k's target is y's IK target, or k writes a holder y's root lies under
//   E5 K(j) -> K(k)   k's source lies at or under what j writes (a bone target on y: K(j) -> M(y))              droppable
//   E6 K(a) -> K(b)   a and b write one target and a comes first in the list
//   E7 K(j) -> K(k)   k's target lies strictly under j's: a node under a node, an IK target of a character under a node
// Kahn's sort, deterministic: among ready vertices constraints first by list index, then units by pre-order, so the list decides
// only where no edge does. The edges that cannot be dropped never close a loop: take for a vertex its place's depth in the tree (a
// unit's root; a node's holder; a character's root for its IK target) and a rank (a constraint 0, a unit 1); E1, E3, E4 and E7 go
// strictly up in (depth, rank), and E6 keeps both and goes up in list index. So when no vertex is ready the remaining vertices hold a
// loop with a droppable edge in it. Loops among constraints were suspended before (ConstraintGraph), so this one has no order but no
// true loop either: among the droppable edges inside a strongly connected set the constraint with the lowest list index loses them
// and reads its source as the previous frame left it (lag 1). An edge on no loop is never dropped, so a chain that has an order
// never lags because of a loop elsewhere, and a dropped edge a later drop made needless comes back. Each round drops an edge, so the
// sort ends.
// Bone targets of one character are ordered among themselves by the same E5, E6 and E7, then by list index. A loop there would be a
// true loop, since a parent's write reaches every source under its child.
using System;
using System.Collections.Generic;

namespace HS2Next.Scenes.Constraints
{
    /// <summary>A unit of the pass: a character, or an item with motion.</summary>
    public struct ConstraintUnit
    {
        /// <summary>The transform the unit's motion moves things under.</summary>
        public int Root;
        /// <summary>The parent of the node's holder.</summary>
        public int HolderParent;

        public ConstraintUnit(int root, int holderParent) { Root = root; HolderParent = holderParent; }
    }

    /// <summary>The edges of the sort, by the name they carry in the documentation.</summary>
    public enum ConstraintEdgeKind { E1 = 1, E2, E3, E4, E5, E6, E7 }

    /// <summary>One edge: vertices are units first (a unit's index), then constraints (units + a link's position).</summary>
    public sealed class ConstraintEdge
    {
        public int From, To;
        public ConstraintEdgeKind Kind;
        /// <summary>The link a droppable edge belongs to; else -1.</summary>
        public int Link;
        /// <summary>True after the sort took the edge away.</summary>
        public bool Dropped;

        public bool Droppable => Kind == ConstraintEdgeKind.E2 || Kind == ConstraintEdgeKind.E5;
    }

    /// <summary>One step of the pass.</summary>
    public readonly struct ConstraintStep
    {
        public readonly bool IsUnit;
        /// <summary>A unit's index, or a link's position.</summary>
        public readonly int Index;

        public ConstraintStep(bool isUnit, int index) { IsUnit = isUnit; Index = index; }

        public override string ToString() => (IsUnit ? "M" : "K") + Index;
    }

    /// <summary>The sorted pass of a set of links and units.</summary>
    public sealed class ConstraintPlan
    {
        ConstraintPlan(int units, int links)
        {
            Units = units;
            Lag = new bool[links];
            Bones = new int[units][];
        }

        /// <summary>The number of units; a vertex below it is a unit.</summary>
        public int Units { get; }
        /// <summary>Every unit and every node or IK link, once, in run order.</summary>
        public ConstraintStep[] Steps { get; private set; }
        /// <summary>By link position: true when the link reads its source one frame behind.</summary>
        public bool[] Lag { get; }
        /// <summary>By unit: the positions of its bone links in run order (never null).</summary>
        public int[][] Bones { get; }
        /// <summary>Every edge of the sort.</summary>
        public List<ConstraintEdge> Edges { get; } = new List<ConstraintEdge>();

        /// <summary>The vertex of a link that has one.</summary>
        public int VertexOf(int position) => Units + position;

        /// <summary>The nearest unit whose root contains <paramref name="place"/>, or -1.</summary>
        public static int Owner(int place, IReadOnlyList<ConstraintUnit> units, IConstraintTree tree)
        {
            if (place == ConstraintGraph.NoPlace) return -1;
            int best = -1;
            for (int u = 0; u < units.Count; u++)
            {
                if (!tree.Under(place, units[u].Root)) continue;
                if (best < 0 || tree.Under(units[u].Root, units[best].Root)) best = u;
            }
            return best;
        }

        /// <summary>
        /// Sorts <paramref name="links"/> (in list order, none on a loop of <see cref="ConstraintGraph"/>) and
        /// <paramref name="units"/> (in model pre-order).
        /// </summary>
        public static ConstraintPlan Build(IReadOnlyList<ConstraintLink> links, IReadOnlyList<ConstraintUnit> units, IConstraintTree tree)
        {
            var plan = new ConstraintPlan(units.Count, links.Count);
            plan.AddEdges(links, units, tree);
            plan.Sort(links);
            plan.SortBones(links, tree);
            return plan;
        }

        void Add(int from, int to, ConstraintEdgeKind kind, int link = -1) =>
            Edges.Add(new ConstraintEdge { From = from, To = to, Kind = kind, Link = link });

        void AddEdges(IReadOnlyList<ConstraintLink> links, IReadOnlyList<ConstraintUnit> units, IConstraintTree tree)
        {
            int n = links.Count;
            var hangsUnder = new int[units.Count];
            for (int y = 0; y < units.Count; y++)
            {
                hangsUnder[y] = Owner(units[y].HolderParent, units, tree);
                if (hangsUnder[y] >= 0) Add(hangsUnder[y], y, ConstraintEdgeKind.E1);
            }
            var lastOn = new Dictionary<long, int>();
            for (int k = 0; k < n; k++)
            {
                var c = links[k];
                int owner = Owner(c.Source, units, tree);
                bool bone = c.Kind == ConstraintTargetKind.Bone;
                if (owner >= 0 && !(bone && owner == c.Unit)) Add(owner, bone ? c.Unit : VertexOf(k), ConstraintEdgeKind.E2, k);
                if (bone) continue;
                int frame = c.Kind == ConstraintTargetKind.Ik ? hangsUnder[c.Unit] : Owner(c.Frame, units, tree);
                if (frame >= 0) Add(frame, VertexOf(k), ConstraintEdgeKind.E3);
                if (c.Kind == ConstraintTargetKind.Ik) Add(VertexOf(k), c.Unit, ConstraintEdgeKind.E4);
                else
                    for (int y = 0; y < units.Count; y++)
                        if (tree.Under(units[y].Root, c.Target)) Add(VertexOf(k), y, ConstraintEdgeKind.E4);
                long key = c.Kind == ConstraintTargetKind.Ik ? ((long)(c.Unit + 1) << 32) | (uint)c.Ik : (uint)c.Target;
                if (lastOn.TryGetValue(key, out int a)) Add(VertexOf(a), VertexOf(k), ConstraintEdgeKind.E6);
                lastOn[key] = k;
            }
            for (int j = 0; j < n; j++)
            {
                var a = links[j];
                if (a.Kind == ConstraintTargetKind.Bone) continue;
                for (int k = 0; k < n; k++)
                {
                    if (k == j) continue;
                    var b = links[k];
                    if (ConstraintGraph.Leads(a, b, tree)) Add(VertexOf(j), b.Kind == ConstraintTargetKind.Bone ? b.Unit : VertexOf(k), ConstraintEdgeKind.E5, k);
                    if (a.Kind != ConstraintTargetKind.Node) continue;
                    if (b.Kind == ConstraintTargetKind.Node && b.Target != a.Target && tree.Under(b.Target, a.Target)) Add(VertexOf(j), VertexOf(k), ConstraintEdgeKind.E7);
                    if (b.Kind == ConstraintTargetKind.Ik && tree.Under(units[b.Unit].Root, a.Target)) Add(VertexOf(j), VertexOf(k), ConstraintEdgeKind.E7);
                }
            }
            // a droppable edge beside one that cannot be dropped decides nothing: taking it away would mark a lag and change no order
            var held = new HashSet<long>();
            foreach (var e in Edges)
                if (!e.Droppable) held.Add(((long)e.From << 32) | (uint)e.To);
            Edges.RemoveAll(e => e.Droppable && held.Contains(((long)e.From << 32) | (uint)e.To));
        }

        void Sort(IReadOnlyList<ConstraintLink> links)
        {
            int total = Units + links.Count;
            var outgoing = new List<ConstraintEdge>[total];
            for (int v = 0; v < total; v++) outgoing[v] = new List<ConstraintEdge>();
            foreach (var e in Edges) outgoing[e.From].Add(e);
            bool dropped = Walk(links, outgoing, true);
            if (!dropped) return;
            Restore(outgoing);
            Walk(links, outgoing, false);
        }

        // One Kahn walk. True when it had to break a loop; only with breakLoops may it.
        bool Walk(IReadOnlyList<ConstraintLink> links, List<ConstraintEdge>[] outgoing, bool breakLoops)
        {
            int total = outgoing.Length;
            var hasVertex = new bool[total];
            for (int u = 0; u < Units; u++) hasVertex[u] = true;
            int vertices = Units;
            for (int k = 0; k < links.Count; k++)
                if (links[k].Kind != ConstraintTargetKind.Bone) { hasVertex[VertexOf(k)] = true; vertices++; }
            var waits = new int[total];
            foreach (var e in Edges)
                if (!e.Dropped) waits[e.To]++;
            var done = new bool[total];
            var steps = new List<ConstraintStep>(vertices);
            bool broke = false;
            while (steps.Count < vertices)
            {
                int ready = -1;
                for (int v = Units; v < total && ready < 0; v++)
                    if (hasVertex[v] && !done[v] && waits[v] == 0) ready = v;
                for (int v = 0; v < Units && ready < 0; v++)
                    if (!done[v] && waits[v] == 0) ready = v;
                if (ready < 0)
                {
                    if (!breakLoops) throw new InvalidOperationException("constraint sort: a loop after the edges were restored");
                    Break(hasVertex, done, waits, outgoing);
                    broke = true;
                    continue;
                }
                done[ready] = true;
                steps.Add(ready < Units ? new ConstraintStep(true, ready) : new ConstraintStep(false, ready - Units));
                foreach (var e in outgoing[ready])
                    if (!e.Dropped) waits[e.To]--;
            }
            Steps = steps.ToArray();
            return broke;
        }

        // No vertex is ready: of the droppable edges inside a strongly connected set of what remains, the lowest link loses its own.
        void Break(bool[] hasVertex, bool[] done, int[] waits, List<ConstraintEdge>[] outgoing)
        {
            int total = hasVertex.Length;
            var next = new List<int>[total];
            for (int v = 0; v < total; v++)
            {
                next[v] = new List<int>();
                if (!hasVertex[v] || done[v]) continue;
                foreach (var e in outgoing[v])
                    if (!e.Dropped && !done[e.To]) next[v].Add(e.To);
            }
            var comp = Components.Of(total, next, out _);
            int pick = int.MaxValue;
            foreach (var e in Edges)
                if (e.Droppable && !e.Dropped && !done[e.From] && !done[e.To] && comp[e.From] == comp[e.To] && e.Link < pick) pick = e.Link;
            if (pick == int.MaxValue) throw new InvalidOperationException("constraint sort: a loop without a droppable edge");
            foreach (var e in Edges)
            {
                if (e.Link != pick || !e.Droppable || e.Dropped || done[e.From] || done[e.To] || comp[e.From] != comp[e.To]) continue;
                e.Dropped = true;
                waits[e.To]--;
            }
        }

        // A later drop can make an earlier one needless. Each dropped edge comes back when it closes no loop with what is kept, the
        // highest link first, so a lag stays only where an order would need it and the lowest index keeps it.
        void Restore(List<ConstraintEdge>[] outgoing)
        {
            var dropped = new List<ConstraintEdge>();
            foreach (var e in Edges)
                if (e.Dropped) dropped.Add(e);
            for (int link = Lag.Length - 1; link >= 0; link--)
                foreach (var e in dropped)
                    if (e.Link == link && !Reaches(e.To, e.From, outgoing)) e.Dropped = false;
            foreach (var e in dropped)
                if (e.Dropped) Lag[e.Link] = true;
        }

        static bool Reaches(int from, int to, List<ConstraintEdge>[] outgoing)
        {
            var seen = new bool[outgoing.Length];
            var stack = new Stack<int>();
            stack.Push(from);
            seen[from] = true;
            while (stack.Count > 0)
            {
                int v = stack.Pop();
                if (v == to) return true;
                foreach (var e in outgoing[v])
                {
                    if (e.Dropped || seen[e.To]) continue;
                    seen[e.To] = true;
                    stack.Push(e.To);
                }
            }
            return false;
        }

        void SortBones(IReadOnlyList<ConstraintLink> links, IConstraintTree tree)
        {
            var perUnit = new List<int>[Units];
            for (int k = 0; k < links.Count; k++)
                if (links[k].Kind == ConstraintTargetKind.Bone) (perUnit[links[k].Unit] ??= new List<int>()).Add(k);
            for (int u = 0; u < Units; u++)
            {
                var list = perUnit[u];
                if (list == null) { Bones[u] = Array.Empty<int>(); continue; }
                Bones[u] = Sequence(list, links, tree);
            }
        }

        // The bone links of one character: a link waits for those it reads through (E5), those before it on its bone (E6) and those on
        // a bone above its own (E7); among the ready ones the list decides.
        static int[] Sequence(List<int> list, IReadOnlyList<ConstraintLink> links, IConstraintTree tree)
        {
            int n = list.Count;
            var after = new List<int>[n];
            var waits = new int[n];
            for (int a = 0; a < n; a++) after[a] = new List<int>();
            for (int a = 0; a < n; a++)
                for (int b = 0; b < n; b++)
                {
                    if (a == b) continue;
                    var j = links[list[a]];
                    var k = links[list[b]];
                    bool same = j.Target == k.Target;
                    if (tree.Under(k.Source, j.Target) || (same && a < b) || (!same && tree.Under(k.Target, j.Target)))
                    {
                        after[a].Add(b);
                        waits[b]++;
                    }
                }
            var order = new int[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++)
            {
                int pick = -1;
                for (int a = 0; a < n && pick < 0; a++)
                    if (!done[a] && waits[a] == 0) pick = a;
                if (pick < 0) throw new InvalidOperationException("constraint sort: a loop among the bones of one character");
                done[pick] = true;
                order[i] = list[pick];
                foreach (int b in after[pick]) waits[b]--;
            }
            return order;
        }
    }
}

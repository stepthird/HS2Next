// The true loops among constraints, without the engine. A vertex is a constraint; j leads to k when k's source lies at or under what
// j writes: a node's holder, a bone, or the bone an IK target can move (a table that does not change with the limb's switches). A
// loop of that relation, a self-loop included, means a value depends on itself: at an edit the edit is refused, anywhere else the
// entry with the highest list index on the loop is suspended, and it comes back when the loop goes. "Under" comes from the built
// hierarchy through IConstraintTree, so a parent, an attach point and a character under a node all count without a table of their own.
using System;
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes.Constraints
{
    /// <summary>
    /// The built hierarchy as the graph sees it. A place is a small number that stands for one transform; a place that does not
    /// resolve is <see cref="ConstraintGraph.NoPlace"/> and never reaches this interface.
    /// </summary>
    public interface IConstraintTree
    {
        /// <summary>True when place <paramref name="a"/> lies at or under place <paramref name="b"/>.</summary>
        bool Under(int a, int b);
    }

    /// <summary>What a constraint writes.</summary>
    public enum ConstraintTargetKind { Node, Bone, Ik }

    /// <summary>A constraint with both ends resolved to places, in list order among its peers.</summary>
    public struct ConstraintLink
    {
        /// <summary>The entry's index in the list.</summary>
        public int Index;
        /// <summary>The place read.</summary>
        public int Source;
        public ConstraintTargetKind Kind;
        /// <summary>The transform written: a node's holder, or a bone. <see cref="ConstraintGraph.NoPlace"/> for an IK target.</summary>
        public int Target;
        /// <summary>What the graph takes as written: <see cref="Target"/>, or the bone an IK target can move.</summary>
        public int Writes;
        /// <summary>A node's frame: its holder's parent. Else <see cref="ConstraintGraph.NoPlace"/>.</summary>
        public int Frame;
        /// <summary>The unit of the character a bone or an IK target belongs to; else -1.</summary>
        public int Unit;
        /// <summary>Which IK target, an index of <see cref="IkTargets.Names"/>; else -1.</summary>
        public int Ik;

        public static ConstraintLink OfNode(int index, int source, int holder, int frame) => new ConstraintLink
        {
            Index = index, Source = source, Kind = ConstraintTargetKind.Node, Target = holder, Writes = holder, Frame = frame, Unit = -1, Ik = -1,
        };

        public static ConstraintLink OfBone(int index, int source, int bone, int unit) => new ConstraintLink
        {
            Index = index, Source = source, Kind = ConstraintTargetKind.Bone, Target = bone, Writes = bone, Frame = ConstraintGraph.NoPlace, Unit = unit, Ik = -1,
        };

        public static ConstraintLink OfIk(int index, int source, int ik, int writes, int unit) => new ConstraintLink
        {
            Index = index, Source = source, Kind = ConstraintTargetKind.Ik, Target = ConstraintGraph.NoPlace, Writes = writes, Frame = ConstraintGraph.NoPlace, Unit = unit, Ik = ik,
        };
    }

    /// <summary>An entry a loop suspends, and the loop: positions in the links, starting at the entry.</summary>
    public sealed class ConstraintCycle
    {
        public ConstraintCycle(int position, int[] rows) { Position = position; Rows = rows; }

        /// <summary>The entry's position in the links.</summary>
        public int Position { get; }
        /// <summary>The loop, in the order its values flow, without repeating the first.</summary>
        public int[] Rows { get; }
    }

    public static class ConstraintGraph
    {
        /// <summary>The place of what did not resolve.</summary>
        public const int NoPlace = -1;

        /// <summary>The bone a target's solve can move, by its index of <see cref="IkTargets.Names"/>; null for no such target.</summary>
        public static string WrittenBone(int ik)
        {
            switch (ik >= 0 && ik < IkTargets.Count ? IkTargets.Names[ik] : null)
            {
                case "Body":
                case "LeftThigh":
                case "RightThigh": return "cf_J_Hips";
                case "LeftShoulder":
                case "RightShoulder": return "cf_J_Spine01";
                case "LeftArmChain":
                case "LeftHand": return "cf_J_ArmUp00_L";
                case "RightArmChain":
                case "RightHand": return "cf_J_ArmUp00_R";
                case "LeftLegChain":
                case "LeftFoot": return "cf_J_LegUp00_L";
                case "RightLegChain":
                case "RightFoot": return "cf_J_LegUp00_R";
                default: return null;
            }
        }

        /// <summary>True when <paramref name="k"/>'s value depends on what <paramref name="j"/> writes.</summary>
        public static bool Leads(in ConstraintLink j, in ConstraintLink k, IConstraintTree tree) =>
            j.Writes != NoPlace && k.Source != NoPlace && tree.Under(k.Source, j.Writes);

        // The successors of each link.
        static List<int>[] Successors(IReadOnlyList<ConstraintLink> links, IConstraintTree tree)
        {
            int n = links.Count;
            var next = new List<int>[n];
            for (int j = 0; j < n; j++)
            {
                next[j] = new List<int>();
                var a = links[j];
                for (int k = 0; k < n; k++)
                {
                    var b = links[k];
                    if (Leads(a, b, tree)) next[j].Add(k);
                }
            }
            return next;
        }

        /// <summary>
        /// The entries to suspend so that no loop is left, each with its loop. In every strongly connected set the entry with the
        /// highest list index goes, then the rest is looked at again. Same input, same output.
        /// </summary>
        public static List<ConstraintCycle> Suspend(IReadOnlyList<ConstraintLink> links, IConstraintTree tree)
        {
            int n = links.Count;
            var result = new List<ConstraintCycle>();
            var next = Successors(links, tree);
            var pick = new int[n];
            var size = new int[n];
            while (true)
            {
                var comp = Components.Of(n, next, out int count);
                for (int c = 0; c < count; c++) { pick[c] = -1; size[c] = 0; }
                for (int i = 0; i < n; i++) size[comp[i]]++;
                for (int i = 0; i < n; i++)   // positions run in list order: the last one seen is the highest index
                    if (size[comp[i]] > 1 || next[i].Contains(i)) pick[comp[i]] = i;
                bool any = false;
                for (int c = 0; c < count; c++)
                {
                    if (pick[c] < 0) continue;
                    any = true;
                    result.Add(new ConstraintCycle(pick[c], Loop(pick[c], next)));
                }
                if (!any) break;
                for (int c = 0; c < count; c++)
                {
                    if (pick[c] < 0) continue;
                    int gone = pick[c];
                    next[gone].Clear();
                    for (int i = 0; i < n; i++) next[i].Remove(gone);
                }
            }
            result.Sort((a, b) => a.Position.CompareTo(b.Position));
            return result;
        }

        /// <summary>The shortest loop through the link at <paramref name="position"/>: positions, starting there; empty when it is on none.</summary>
        public static int[] LoopThrough(IReadOnlyList<ConstraintLink> links, int position, IConstraintTree tree) =>
            Loop(position, Successors(links, tree));

        // Breadth first from the vertex back to itself; among paths of one length the lowest positions come first.
        static int[] Loop(int start, List<int>[] next)
        {
            if (next[start].Contains(start)) return new[] { start };
            int n = next.Length;
            var from = new int[n];
            for (int i = 0; i < n; i++) from[i] = -1;
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                foreach (int w in next[v])
                {
                    if (w == start)
                    {
                        var rows = new List<int>();
                        for (int x = v; x != start; x = from[x]) rows.Add(x);
                        rows.Add(start);
                        rows.Reverse();
                        return rows.ToArray();
                    }
                    if (from[w] >= 0) continue;
                    from[w] = v;
                    queue.Enqueue(w);
                }
            }
            return Array.Empty<int>();
        }
    }

    /// <summary>Strongly connected sets of a small directed graph.</summary>
    internal static class Components
    {
        /// <summary>The set of each vertex (Tarjan's algorithm with an explicit stack); <paramref name="count"/> is the number of sets.</summary>
        public static int[] Of(int n, List<int>[] next, out int count)
        {
            var comp = new int[n];
            var index = new int[n];
            var low = new int[n];
            var on = new bool[n];
            var edge = new int[n];
            var stack = new int[n];
            var path = new int[n];
            for (int i = 0; i < n; i++) { index[i] = -1; comp[i] = -1; }
            int sp = 0, counter = 0, sets = 0;
            for (int root = 0; root < n; root++)
            {
                if (index[root] >= 0) continue;
                int depth = 0;
                path[0] = root;
                index[root] = low[root] = counter++;
                stack[sp++] = root;
                on[root] = true;
                while (depth >= 0)
                {
                    int v = path[depth];
                    if (edge[v] < next[v].Count)
                    {
                        int w = next[v][edge[v]++];
                        if (index[w] < 0)
                        {
                            index[w] = low[w] = counter++;
                            stack[sp++] = w;
                            on[w] = true;
                            path[++depth] = w;
                        }
                        else if (on[w] && index[w] < low[v]) low[v] = index[w];
                        continue;
                    }
                    if (low[v] == index[v])
                    {
                        int w;
                        do
                        {
                            w = stack[--sp];
                            on[w] = false;
                            comp[w] = sets;
                        } while (w != v);
                        sets++;
                    }
                    depth--;
                    if (depth >= 0 && low[v] < low[path[depth]]) low[path[depth]] = low[v];
                }
            }
            count = sets;
            return comp;
        }
    }
}

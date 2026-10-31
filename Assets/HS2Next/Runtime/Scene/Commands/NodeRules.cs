// Tree rules of the scene model: which kinds take children and
// through which attach points, which kinds move, the scale channel, cycles and the vanilla route rule. Point
// problems (unknown or non-offered name under a character, a point under any other parent, a character child
// without a point) fail with bad_args; a parent kind that takes no children with wrong_kind (opaque nodes keep
// their children but receive none).
using System;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    public static class NodeRules
    {
        /// <summary>Kinds whose transform has a scale channel (characters and items).</summary>
        public static bool HasScale(NodeKind k) => k == NodeKind.Chara || k == NodeKind.Item;

        public static bool IsUnitScale(Vec3 s) => s.Equals(Vec3.One);

        /// <summary>Kinds that may receive new children (characters through attach points).</summary>
        public static bool TakesChildren(NodeKind k) => k == NodeKind.Chara || k == NodeKind.Item || k == NodeKind.Folder || k == NodeKind.Route;

        /// <summary>
        /// Checks that <paramref name="parent"/> (null = root) takes a new child at <paramref name="point"/>; throws
        /// <see cref="CommandException"/>. <paramref name="relaxed"/> keeps a stored point as it is (duplicates beside
        /// their originals): only the parent kind is checked.
        /// </summary>
        public static void CheckParent(SceneModel m, TreeNode parent, string point, bool relaxed = false)
        {
            if (parent == null)
            {
                if (point != null && !relaxed) throw new CommandException(CommandCodes.BadArgs, "a root node has no attach point");
                return;
            }
            if (!TakesChildren(parent.Kind))
                throw new CommandException(CommandCodes.WrongKind, "a " + (parent.Doc.Kind ?? parent.Kind.ToString()) + " node takes no new children");
            if (relaxed) return;
            if (parent.Kind == NodeKind.Chara)
            {
                if (point == null) throw new CommandException(CommandCodes.BadArgs, "a child of a character needs an attach point");
                var t = m?.AttachPoints ?? AttachPointTable.Vanilla;
                if (!t.IsKnown(point)) throw new CommandException(CommandCodes.BadArgs, "unknown attach point \"" + point + "\"");
                if (!t.IsOffered(point)) throw new CommandException(CommandCodes.BadArgs, "attach point \"" + point + "\" is not offered");
            }
            else if (point != null) throw new CommandException(CommandCodes.BadArgs, "only character parents have attach points");
        }

        /// <summary>True when <paramref name="node"/> is <paramref name="ancestor"/> or below it.</summary>
        public static bool IsInSubtree(TreeNode node, TreeNode ancestor)
        {
            for (var n = node; n != null; n = n.Parent) if (n == ancestor) return true;
            return false;
        }

        /// <summary>True when the subtree of <paramref name="top"/> contains a route node.</summary>
        public static bool ContainsRoute(TreeNode top)
        {
            foreach (var n in SceneModel.SubtreeOf(top)) if (n.Kind == NodeKind.Route) return true;
            return false;
        }

        /// <summary>True when <paramref name="parent"/> is a route or lies inside a route's subtree.</summary>
        public static bool InsideRoute(TreeNode parent)
        {
            for (var n = parent; n != null; n = n.Parent) if (n.Kind == NodeKind.Route) return true;
            return false;
        }

        /// <summary>Vanilla route rule: a subtree with a route cannot go under or inside another route.</summary>
        public static void CheckRouteRule(TreeNode top, TreeNode parent)
        {
            if (parent != null && InsideRoute(parent) && ContainsRoute(top))
                throw new CommandException(CommandCodes.WrongKind, "a subtree with a route cannot be placed under a route");
        }

        /// <summary>Transform rules of a node of kind <paramref name="k"/>: finite values (checked by the writer elsewhere), unit scale without a scale channel.</summary>
        public static void CheckTransform(NodeKind k, NodeTransform t)
        {
            if (t == null) throw new CommandException(CommandCodes.BadArgs, "transform is null");
            if (!HasScale(k) && !IsUnitScale(t.Scale))
                throw new CommandException(CommandCodes.BadArgs, "a " + k.ToString().ToLowerInvariant() + " node has no scale channel: transform.scale stays (1, 1, 1)");
            foreach (var v in new[] { t.Pos, t.Rot, t.Scale })
                if (float.IsNaN(v.X) || float.IsInfinity(v.X) || float.IsNaN(v.Y) || float.IsInfinity(v.Y) || float.IsNaN(v.Z) || float.IsInfinity(v.Z))
                    throw new CommandException(CommandCodes.BadArgs, "non-finite transform value");
        }
    }
}

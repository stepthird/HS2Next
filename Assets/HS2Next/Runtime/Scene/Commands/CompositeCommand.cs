// A command made of child commands applied in order: each child is
// validated just before it applies (it may depend on an earlier one); when child k fails, children k−1..0 are
// reverted in reverse and the failure propagates, so the composite leaves no trace. Undo reverts the children
// in reverse. Transactions record their steps as one composite.
using System;
using System.Collections.Generic;

namespace HS2Next.Scenes
{
    public sealed class CompositeCommand : ISceneCommand
    {
        readonly List<ISceneCommand> children;

        public CompositeCommand(string label, IEnumerable<ISceneCommand> children)
        {
            Label = label ?? "Edit";
            this.children = children == null ? new List<ISceneCommand>() : new List<ISceneCommand>(children);
        }

        public string Label { get; }
        public IReadOnlyList<ISceneCommand> Children => children;

        public void Validate(SceneModel m)
        {
            if (children.Contains(null)) throw new CommandException(CommandCodes.BadArgs, "null child command");
        }

        public void Apply(SceneModel m, ChangeSink s)
        {
            int k = 0;
            try
            {
                for (; k < children.Count; k++)
                {
                    children[k].Validate(m);
                    children[k].Apply(m, s);
                }
            }
            catch (Exception)
            {
                var scratch = new ChangeSink();
                for (int i = k - 1; i >= 0; i--) children[i].Revert(m, scratch);
                throw;
            }
        }

        public void Revert(SceneModel m, ChangeSink s)
        {
            for (int i = children.Count - 1; i >= 0; i--) children[i].Revert(m, s);
        }

        /// <summary>Merges a composite of property sets on the same (target, path) list, child by child.</summary>
        public bool TryMerge(ISceneCommand next)
        {
            if (!(next is CompositeCommand c) || c.children.Count != children.Count) return false;
            for (int i = 0; i < children.Count; i++)
                if (!(children[i] is SetPropertyCommand a) || !(c.children[i] is SetPropertyCommand b) || !FieldCommand.SameTarget(a, b)) return false;
            for (int i = 0; i < children.Count; i++)
                if (!children[i].TryMerge(c.children[i])) return false;
            return true;
        }

        public long EstimatedBytes
        {
            get
            {
                long b = 64;
                foreach (var c in children) b += c.EstimatedBytes;
                return b;
            }
        }

        public IEnumerable<TreeNode> HeldNodes
        {
            get
            {
                foreach (var c in children)
                    foreach (var n in c.HeldNodes) yield return n;
            }
        }
    }
}

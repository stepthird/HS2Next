// The command contract of the scene bus and the sink commands report their
// changes to. A command validates against the current model without mutating it (CommandException on
// failure), applies atomically, and reverts exactly; the bus buffers the reported changes and delivers them as
// one batch after the operation.
using System.Collections.Generic;

namespace HS2Next.Scenes
{
    public interface ISceneCommand
    {
        /// <summary>History label ("Move", "Delete", …).</summary>
        string Label { get; }
        /// <summary>Checks the command against the current model; throws <see cref="CommandException"/>; no mutation.</summary>
        void Validate(SceneModel m);
        /// <summary>The mutation; atomic for primitive commands (validated before).</summary>
        void Apply(SceneModel m, ChangeSink s);
        /// <summary>Undoes <see cref="Apply"/>.</summary>
        void Revert(SceneModel m, ChangeSink s);
        /// <summary>Folds <paramref name="next"/> into this command when both are property sets of the same (target, path) set
        ///; true when merged.</summary>
        bool TryMerge(ISceneCommand next);
        /// <summary>History size estimate.</summary>
        long EstimatedBytes { get; }
        /// <summary>Nodes this command holds while they are out of the model (removed subtrees, an undone add).</summary>
        IEnumerable<TreeNode> HeldNodes { get; }
    }

    /// <summary>Collects the changes of one operation (delivered as one <see cref="SceneChangeBatch"/>).</summary>
    public sealed class ChangeSink
    {
        readonly List<SceneChange> changes = new List<SceneChange>();

        public IReadOnlyList<SceneChange> Changes => changes;
        public int Count => changes.Count;

        public void Add(SceneChange c) { if (c != null) changes.Add(c); }

        public void NodeAdded(TreeNode n) => changes.Add(new SceneChange(SceneChangeKind.NodeAdded, n.Id));
        public void NodeRemoved(TreeNode n) => changes.Add(new SceneChange(SceneChangeKind.NodeRemoved, n.Id));
        public void NodeMoved(TreeNode n, string oldParentId, string oldPoint, int oldIndex) =>
            changes.Add(new SceneChange(SceneChangeKind.NodeMoved, n.Id, null, oldParentId, oldPoint, oldIndex));
        public void NodeChanged(string nodeId, string path) => changes.Add(new SceneChange(SceneChangeKind.NodeChanged, nodeId, path));
        public void SceneChanged(string path) => changes.Add(new SceneChange(SceneChangeKind.SceneChanged, null, path));
        public void AppearanceChanged(string nodeId) => changes.Add(new SceneChange(SceneChangeKind.AppearanceChanged, nodeId));

        internal void AddRange(ChangeSink other) => changes.AddRange(other.changes);
        internal void Clear() => changes.Clear();
    }
}

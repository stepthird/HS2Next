// Change records of the scene model: one SceneChangeBatch per bus operation
// (execute, transaction update/commit/cancel, undo, redo) and per silent write group (capture, normalisation),
// delivered after the model is consistent. A removed subtree gives one NodeRemoved per node (top first, then
// pre-order); an added one NodeAdded per node in pre-order. Siblings renumbered by a structural change get no
// record of their own: listeners re-read the group.
using System;
using System.Collections.Generic;

namespace HS2Next.Scenes
{
    public enum SceneChangeKind
    {
        NodeAdded,
        NodeRemoved,
        /// <summary>Reparent or reorder; the record holds the old parent, point and index.</summary>
        NodeMoved,
        /// <summary>A node field written; <see cref="SceneChange.Path"/> is the path (a prefix means "anything below").</summary>
        NodeChanged,
        /// <summary>A scene field written (<see cref="SceneChange.NodeId"/> null).</summary>
        SceneChanged,
        /// <summary>A character's <see cref="TreeNode.Appearance"/> was replaced or its representation changed.</summary>
        AppearanceChanged,
    }

    public enum ChangeOrigin
    {
        /// <summary>A command executed (or a transaction committed).</summary>
        Command,
        Undo,
        Redo,
        /// <summary>A step inside an open transaction (not recorded yet).</summary>
        Preview,
        /// <summary>A transaction cancelled: its steps reverted.</summary>
        Cancel,
        /// <summary>Reserved: loading replaces the model and raises Cleared/Loaded instead of a batch.</summary>
        Load,
        /// <summary>Runtime-owned values written into the model (before save, duplicate, delete).</summary>
        Capture,
        /// <summary>Derived consistency (accessory map, active cameras).</summary>
        Normalize,
    }

    public sealed class SceneChange
    {
        public SceneChangeKind Kind { get; }
        /// <summary>The node, or null for <see cref="SceneChangeKind.SceneChanged"/>.</summary>
        public string NodeId { get; }
        /// <summary>Canonical field path (NodeChanged, SceneChanged), else null.</summary>
        public string Path { get; }
        /// <summary>NodeMoved: where the node was (parent id null = root).</summary>
        public string OldParentId { get; }
        public string OldPoint { get; }
        public int OldIndex { get; }

        public SceneChange(SceneChangeKind kind, string nodeId, string path = null, string oldParentId = null, string oldPoint = null, int oldIndex = -1)
        {
            Kind = kind;
            NodeId = nodeId;
            Path = path;
            OldParentId = oldParentId;
            OldPoint = oldPoint;
            OldIndex = oldIndex;
        }

        /// <summary>
        /// True when this change may affect the value at <paramref name="pathPrefix"/> of node <paramref name="nodeId"/>
        /// (null node id = the scene target; null path = anything): field writes whose path overlaps the prefix, and
        /// every structural or appearance change of that node.
        /// </summary>
        public bool Touches(string nodeId, string pathPrefix)
        {
            if (!string.Equals(NodeId, nodeId, StringComparison.Ordinal)) return false;
            if (Kind != SceneChangeKind.NodeChanged && Kind != SceneChangeKind.SceneChanged) return true;
            return pathPrefix == null || FieldPath.Overlaps(pathPrefix, Path);
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case SceneChangeKind.NodeChanged: return "changed " + NodeId + " " + Path;
                case SceneChangeKind.SceneChanged: return "scene " + Path;
                case SceneChangeKind.NodeMoved: return "moved " + NodeId + " from " + (OldParentId ?? "root") + (OldPoint == null ? "" : "/" + OldPoint) + "#" + OldIndex;
                default: return Kind + " " + NodeId;
            }
        }
    }

    public sealed class SceneChangeBatch
    {
        public ChangeOrigin Origin { get; }
        /// <summary>Who started the operation (User for undo/redo; System for captures and normalisation).</summary>
        public CommandSource Source { get; }
        public IReadOnlyList<SceneChange> Changes { get; }

        public SceneChangeBatch(ChangeOrigin origin, CommandSource source, IReadOnlyList<SceneChange> changes)
        {
            Origin = origin;
            Source = source;
            Changes = changes ?? Array.Empty<SceneChange>();
        }

        /// <summary>True when any change <see cref="SceneChange.Touches"/> the node (null = scene) and path prefix.</summary>
        public bool Touches(string nodeId, string pathPrefix)
        {
            foreach (var c in Changes) if (c.Touches(nodeId, pathPrefix)) return true;
            return false;
        }

        /// <summary>True when the batch adds, removes or moves a node.</summary>
        public bool IsStructural
        {
            get
            {
                foreach (var c in Changes)
                    if (c.Kind == SceneChangeKind.NodeAdded || c.Kind == SceneChangeKind.NodeRemoved || c.Kind == SceneChangeKind.NodeMoved) return true;
                return false;
            }
        }

        public override string ToString() => Origin + " (" + Changes.Count + " changes)";
    }
}

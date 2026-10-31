// One node of the runtime scene tree. The node's state IS its 2.1
// document object (SceneNode: envelope plus kind section); the tree node adds the links of the index (parent,
// attach point, ordered children), the cached effective visibility, the character appearance and the node's
// file-backed ext data. Doc.Parent and Doc.Order are kept equal to Parent/Point/Index by the model. Readers
// (UI, bridge, realization, plugins) treat Doc as read-only: every write goes through the command bus.
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    /// <summary>Node kinds of the model; <see cref="Opaque"/> = a kind this build does not know (kept, never built).</summary>
    public enum NodeKind { Chara, Item, Light, Folder, Route, Camera, Opaque }

    public sealed class TreeNode
    {
        internal SceneModel Owner;
        internal TreeNode parent;
        internal string point;
        internal int index;
        internal bool effectiveVisible = true;
        internal bool inModel;
        // Children grouped by attach point (rank order); the flat list is rebuilt on demand.
        internal readonly List<ChildGroup> Groups = new List<ChildGroup>();
        List<TreeNode> flatChildren;

        internal TreeNode(SceneNode doc, CharaAppearance appearance, ExtScopeData fileExt)
        {
            Doc = doc;
            Kind = KindOf(doc);
            Appearance = Kind == NodeKind.Chara ? appearance ?? new CharaAppearance() : null;
            FileExt = fileExt;
        }

        /// <summary>32 lowercase hex; never changes.</summary>
        public string Id => Doc.Id;
        public NodeKind Kind { get; }
        /// <summary>The document object (envelope + kind section; an <see cref="OpaqueNode"/> section for unknown kinds). Read-only for everyone but the commands.</summary>
        public SceneNode Doc { get; }
        /// <summary>The kind section (<c>Doc.State</c>).</summary>
        public NodeState State => Doc.State;
        /// <summary>Parent node; null for a root.</summary>
        public TreeNode Parent => parent;
        /// <summary>Attach point name (an AccessoryParentKey name) under a character; null otherwise.</summary>
        public string Point => point;
        /// <summary>Position in the sibling group (<see cref="SceneModel.GroupOf"/>); equals <c>Doc.Order</c>.</summary>
        public int Index => index;
        /// <summary>Own <c>visible</c> AND every ancestor's.</summary>
        public bool EffectiveVisible => effectiveVisible;
        /// <summary>The character appearance; null for other kinds. Never null for a character node.</summary>
        public CharaAppearance Appearance { get; internal set; }
        /// <summary>Node-scope file-backed ext data (from <see cref="SceneContainer.NodeExt"/>), or null.</summary>
        public ExtScopeData FileExt { get; internal set; }
        /// <summary>False while the node is held by a command (deleted, or an undone add).</summary>
        public bool InModel => inModel;

        /// <summary>Children in canonical order: grouped by attach point (none, AccessoryParentKey 1..54, unknown names
        /// ordinal), each group by <see cref="Index"/>.</summary>
        public IReadOnlyList<TreeNode> Children
        {
            get
            {
                if (flatChildren == null)
                {
                    flatChildren = new List<TreeNode>();
                    foreach (var g in Groups) flatChildren.AddRange(g.Nodes);
                }
                return flatChildren;
            }
        }

        /// <summary><c>Doc.Name</c>, or the session's default label.</summary>
        public string Label => Doc.Name ?? (Owner?.LabelProvider ?? DefaultLabelProvider.Instance).DefaultLabel(this);

        /// <summary>The model kind of a document node.</summary>
        public static NodeKind KindOf(SceneNode doc)
        {
            switch (doc?.State)
            {
                case CharaNodeState _: return NodeKind.Chara;
                case ItemNodeState _: return NodeKind.Item;
                case LightNodeState _: return NodeKind.Light;
                case FolderNodeState _: return NodeKind.Folder;
                case RouteNodeState _: return NodeKind.Route;
                case CameraNodeState _: return NodeKind.Camera;
                default: return NodeKind.Opaque;
            }
        }

        internal void ChildrenChanged() => flatChildren = null;

        public override string ToString() => Kind + ":" + (Id != null && Id.Length >= 8 ? Id.Substring(0, 8) : Id);
    }

    /// <summary>The children of one parent under one attach point, in sibling order.</summary>
    internal sealed class ChildGroup
    {
        public readonly string Point;
        public readonly int Rank;
        public readonly List<TreeNode> Nodes = new List<TreeNode>();

        public ChildGroup(string point)
        {
            Point = point;
            Rank = AttachPointTable.Rank(point);
        }

        /// <summary>Canonical group order: rank, then point name ordinal (only unknown names share rank 55).</summary>
        public int CompareTo(string point)
        {
            int r = AttachPointTable.Rank(point);
            int c = Rank.CompareTo(r);
            return c != 0 ? c : string.CompareOrdinal(Point, point);
        }
    }
}

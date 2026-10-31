// Structural commands of the bus: add (and duplicate), delete with
// reference clearing, reparent/reorder, and the appearance swap. They hold the node objects themselves while
// the nodes are out of the model (ids never change; redo re-inserts the same objects), and report one
// NodeAdded / NodeRemoved per node in pre-order (removal: top first).
using System;
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    /// <summary>Adds node subtrees; also the duplicate command.</summary>
    public sealed class AddNodesCommand : ISceneCommand
    {
        sealed class Top
        {
            public TreeNode Node;
            public string ParentId, Point;
            public int Index;
        }

        readonly List<Top> tops = new List<Top>();
        readonly List<TreeNode> all = new List<TreeNode>();
        readonly string error, errorCode;

        /// <summary>Builds the node objects at once from the specs (ids must be set; the session assigns missing ones).</summary>
        public AddNodesCommand(IReadOnlyList<NodeSpec> specs, string label = "Add") : this(specs, label, false) { }

        /// <summary>A duplicate: copies keep the stored attach points of their originals and become the selection.</summary>
        internal AddNodesCommand(IReadOnlyList<NodeSpec> specs, string label, bool duplicate)
        {
            Label = label;
            IsDuplicate = duplicate;
            RelaxedPoints = duplicate;
            try
            {
                if (specs == null || specs.Count == 0) throw new CommandException(CommandCodes.BadArgs, "nothing to add");
                foreach (var s in specs)
                {
                    var n = Build(s);
                    tops.Add(new Top { Node = n, ParentId = s.ParentId, Point = s.Point, Index = s.Index });
                }
            }
            catch (CommandException e)
            {
                errorCode = e.Code;
                error = e.Message;
            }
        }

        TreeNode Build(NodeSpec s)
        {
            if (s?.Doc?.State == null) throw new CommandException(CommandCodes.BadArgs, "a node spec needs a document with a kind section");
            if (s.Doc.State is OpaqueNode) throw new CommandException(CommandCodes.Unsupported, "nodes of an unknown kind cannot be added");
            var kind = TreeNode.KindOf(s.Doc);
            if (kind != NodeKind.Chara && s.Appearance != null) throw new CommandException(CommandCodes.BadArgs, "only character nodes have an appearance");
            var n = new TreeNode(s.Doc, s.Appearance, s.FileExt);
            n.Doc.Ui.IsChara = kind == NodeKind.Chara;
            all.Add(n);
            if (s.Children != null)
                foreach (var c in s.Children)
                {
                    var cn = Build(c);
                    SceneModel.LinkDetached(n, cn, c.Point, c.Index);
                }
            return n;
        }

        public string Label { get; }
        /// <summary>Ids of the top nodes in spec order.</summary>
        public IReadOnlyList<string> TopIds => tops.ConvertAll(t => t.Node.Id);
        public IReadOnlyList<TreeNode> TopNodes => tops.ConvertAll(t => t.Node);
        /// <summary>A duplicate: the copies become the selection whatever the source.</summary>
        internal readonly bool IsDuplicate;
        /// <summary>Copies keep the stored attach point of their originals (a duplicate creates no new attachment).</summary>
        internal readonly bool RelaxedPoints;

        public void Validate(SceneModel m)
        {
            if (error != null) throw new CommandException(errorCode, error);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in all)
            {
                if (!JsonText.IsId(n.Id)) throw new CommandException(CommandCodes.BadArgs, "not a node id: \"" + n.Id + "\"");
                if (!ids.Add(n.Id)) throw new CommandException(CommandCodes.BadArgs, "duplicate id " + n.Id + " in the batch");
                var there = m.Find(n.Id);
                if (there != null && there != n) throw new CommandException(CommandCodes.BadArgs, "id " + n.Id + " is already in the scene");
                if (there == n) throw new CommandException(CommandCodes.BadArgs, "node " + n.Id + " is already in the scene");
                var held = m.HeldLookup?.Invoke(n.Id);
                if (held != null && held != n) throw new CommandException(CommandCodes.BadArgs, "id " + n.Id + " belongs to a deleted node that undo can restore");
                NodeRules.CheckTransform(n.Kind, n.Doc.Transform);
                try { DocClone.Bytes(n.Doc); }
                catch (Exception e) { throw new CommandException(CommandCodes.BadArgs, "node " + n.Id + " cannot be saved: " + e.Message); }
            }
            foreach (var n in all)
                if (n.Parent != null) NodeRules.CheckParent(m, n.Parent, n.Point, RelaxedPoints);
            foreach (var n in all)
                if (n.Kind == NodeKind.Route && n.Parent != null && NodeRules.InsideRoute(n.Parent))
                    throw new CommandException(CommandCodes.WrongKind, "a route cannot be placed under a route");
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var t in tops)
            {
                TreeNode parent = null;
                if (t.ParentId != null)
                {
                    parent = m.Find(t.ParentId);
                    if (parent == null) throw new CommandException(CommandCodes.NotFound, "parent " + t.ParentId + " is not in the scene");
                }
                NodeRules.CheckParent(m, parent, t.Point, RelaxedPoints);
                NodeRules.CheckRouteRule(t.Node, parent);
                string key = (t.ParentId ?? "") + "\u0000" + (t.Point ?? "\u0001");
                counts.TryGetValue(key, out int added);
                int count = m.GroupOf(parent, t.Point).Count + added;
                if (t.Index < -1 || t.Index > count) throw new CommandException(CommandCodes.BadArgs, "index " + t.Index + " outside 0.." + count);
                counts[key] = added + 1;
            }
        }

        public void Apply(SceneModel m, ChangeSink s)
        {
            foreach (var t in tops)
            {
                var parent = t.ParentId == null ? null : m.Find(t.ParentId);
                m.Attach(t.Node, parent, t.Point, t.Index);
                foreach (var n in SceneModel.SubtreeOf(t.Node)) s.NodeAdded(n);
            }
        }

        public void Revert(SceneModel m, ChangeSink s)
        {
            for (int i = tops.Count - 1; i >= 0; i--)
            {
                var top = tops[i].Node;
                var sub = SceneModel.SubtreeOf(top);
                m.Detach(top);
                foreach (var n in sub) s.NodeRemoved(n);
            }
        }

        public bool TryMerge(ISceneCommand next) => false;

        public long EstimatedBytes => StructuralSize.Of(all);

        public IEnumerable<TreeNode> HeldNodes => all;
    }

    static class StructuralSize
    {
        /// <summary>4 KiB per node plus 256 KiB per character with a stored subtree.</summary>
        public static long Of(IEnumerable<TreeNode> nodes)
        {
            long b = 0;
            foreach (var n in nodes) b += 4096 + (n.Appearance?.Stored != null ? 256 * 1024 : 0);
            return b;
        }
    }

    /// <summary>Deletes nodes with their subtrees; references to them in `scene.effects` are cleared in the same command.</summary>
    public sealed class RemoveNodesCommand : ISceneCommand
    {
        sealed class Top
        {
            public TreeNode Node;
            public TreeNode Parent;
            public string Point;
            public int Index;
            public List<TreeNode> Subtree;
        }

        readonly List<string> ids;
        readonly List<Top> tops = new List<Top>();
        readonly List<TreeNode> held = new List<TreeNode>();
        readonly List<SetPropertyCommand> clears = new List<SetPropertyCommand>();

        public RemoveNodesCommand(IEnumerable<string> ids, string label = "Delete")
        {
            this.ids = ids == null ? new List<string>() : new List<string>(ids);
            Label = label;
        }

        public string Label { get; }
        public IReadOnlyList<string> Ids => ids;

        public void Validate(SceneModel m)
        {
            if (ids.Count == 0) throw new CommandException(CommandCodes.BadArgs, "nothing to delete");
            foreach (var id in ids)
                if (m.Find(id) == null) throw new CommandException(CommandCodes.NotFound, "node " + id + " is not in the scene");
        }

        public void Apply(SceneModel m, ChangeSink s)
        {
            // Top-most nodes in pre-order, positions recorded before anything is detached.
            var selected = new HashSet<TreeNode>();
            foreach (var id in ids) selected.Add(m.Find(id));
            tops.Clear();
            held.Clear();
            var removedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in m.PreOrder())
            {
                if (!selected.Contains(n)) continue;
                bool covered = false;
                for (var p = n.Parent; p != null; p = p.Parent) if (selected.Contains(p)) { covered = true; break; }
                if (covered) continue;
                var sub = SceneModel.SubtreeOf(n);
                tops.Add(new Top { Node = n, Parent = n.Parent, Point = n.Point, Index = n.Index, Subtree = sub });
                held.AddRange(sub);
                foreach (var x in sub) removedIds.Add(x.Id);
            }
            foreach (var t in tops)
            {
                m.Detach(t.Node);
                foreach (var x in t.Subtree) s.NodeRemoved(x);
            }
            clears.Clear();
            var fx = m.Settings.Effects;
            if (fx.DepthOfField.Focus != null && removedIds.Contains(fx.DepthOfField.Focus)) clears.Add(new SetPropertyCommand(null, "effects.depthOfField.focus", null));
            if (fx.SunShafts.Caster != null && removedIds.Contains(fx.SunShafts.Caster)) clears.Add(new SetPropertyCommand(null, "effects.sunShafts.caster", null));
            foreach (var c in clears)
            {
                c.Validate(m);
                c.Apply(m, s);
            }
        }

        public void Revert(SceneModel m, ChangeSink s)
        {
            var order = new List<Top>(tops);
            order.Sort((a, b) => a.Index.CompareTo(b.Index));
            foreach (var t in order)
            {
                m.Attach(t.Node, t.Parent, t.Point, t.Index);
                foreach (var x in t.Subtree) s.NodeAdded(x);
            }
            for (int i = clears.Count - 1; i >= 0; i--) clears[i].Revert(m, s);
        }

        public bool TryMerge(ISceneCommand next) => false;
        public long EstimatedBytes => StructuralSize.Of(held);
        public IEnumerable<TreeNode> HeldNodes => held;
    }

    /// <summary>Reparent or reorder. The transform is the caller's (keep-world is computed by the Unity layer).</summary>
    public sealed class MoveNodeCommand : ISceneCommand
    {
        readonly string id, parentId, point;
        readonly int index;
        readonly NodeTransform transform;
        NodeTransform checkedTransform;
        TreeNode node, oldParent;
        string oldPoint;
        int oldIndex;
        NodeParent oldLink;
        Vec3 oldPos, oldRot, oldScale;
        bool oldInherit, moved, transformChanged;

        /// <param name="index">Final index in the destination group after the node left its old group; −1 = end.</param>
        /// <param name="transform">The new local transform, or null to keep the stored one.</param>
        public MoveNodeCommand(string id, string parentId, string point, int index, NodeTransform transform = null, string label = null)
        {
            this.id = id;
            this.parentId = parentId;
            this.point = point;
            this.index = index;
            this.transform = transform;
            Label = label ?? "Move";
        }

        public string Label { get; }
        public string NodeId => id;

        bool IsReorder(TreeNode n) =>
            string.Equals(n.Parent?.Id, parentId, StringComparison.Ordinal) && string.Equals(n.Point, point, StringComparison.Ordinal);

        public void Validate(SceneModel m)
        {
            var n = m.Find(id) ?? throw new CommandException(CommandCodes.NotFound, "node " + id + " is not in the scene");
            TreeNode parent = null;
            if (parentId != null) parent = m.Find(parentId) ?? throw new CommandException(CommandCodes.NotFound, "parent " + parentId + " is not in the scene");
            if (IsReorder(n))
            {
                int count = m.GroupOf(parent, point).Count;
                if (index < -1 || index >= count) throw new CommandException(CommandCodes.BadArgs, "index " + index + " outside 0.." + (count - 1));
            }
            else
            {
                if (n.Kind == NodeKind.Opaque) throw new CommandException(CommandCodes.Unsupported, "nodes of an unknown kind cannot be re-parented");
                if (n.Kind == NodeKind.Route && !m.Rules.AllowRouteReparent) throw new CommandException(CommandCodes.WrongKind, "routes cannot be re-parented");
                if (parent != null && NodeRules.IsInSubtree(parent, n)) throw new CommandException(CommandCodes.BadArgs, "a node cannot move under itself");
                NodeRules.CheckParent(m, parent, point);
                NodeRules.CheckRouteRule(n, parent);
                int count = m.GroupOf(parent, point).Count;
                if (index < -1 || index > count) throw new CommandException(CommandCodes.BadArgs, "index " + index + " outside 0.." + count);
            }
            if (transform != null && checkedTransform == null)
            {
                NodeRules.CheckTransform(n.Kind, transform);
                checkedTransform = new NodeTransform
                {
                    Pos = Norm(transform.Pos), Rot = Norm(transform.Rot), Scale = Norm(transform.Scale), InheritScale = transform.InheritScale,
                };
            }
        }

        static float N(float f) => f == 0f ? 0f : f;
        static Vec3 Norm(Vec3 v) => new Vec3(N(v.X), N(v.Y), N(v.Z));

        public void Apply(SceneModel m, ChangeSink s)
        {
            node = m.Find(id);
            var parent = parentId == null ? null : m.Find(parentId);
            oldParent = node.Parent;
            oldPoint = node.Point;
            oldIndex = node.Index;
            oldLink = node.Doc.Parent;
            var t = node.Doc.Transform;
            oldPos = t.Pos; oldRot = t.Rot; oldScale = t.Scale; oldInherit = t.InheritScale;
            int count = IsReorder(node) ? m.GroupOf(parent, point).Count - 1 : m.GroupOf(parent, point).Count;
            int target = index < 0 ? count : index;
            moved = !(IsReorder(node) && target == oldIndex);
            if (moved)
            {
                m.Move(node, parent, point, target);
                s.NodeMoved(node, oldParent?.Id, oldPoint, oldIndex);
            }
            var nt = checkedTransform;
            transformChanged = nt != null && !(nt.Pos.Equals(t.Pos) && nt.Rot.Equals(t.Rot) && nt.Scale.Equals(t.Scale) && nt.InheritScale == t.InheritScale);
            if (transformChanged)
            {
                t.Pos = nt.Pos; t.Rot = nt.Rot; t.Scale = nt.Scale; t.InheritScale = nt.InheritScale;
                s.NodeChanged(id, "transform");
            }
        }

        public void Revert(SceneModel m, ChangeSink s)
        {
            if (moved)
            {
                var cur = node.Parent;
                string curPoint = node.Point;
                int curIndex = node.Index;
                m.Move(node, oldParent, oldPoint, oldIndex);
                node.Doc.Parent = oldLink;
                s.NodeMoved(node, cur?.Id, curPoint, curIndex);
            }
            if (transformChanged)
            {
                var t = node.Doc.Transform;
                t.Pos = oldPos; t.Rot = oldRot; t.Scale = oldScale; t.InheritScale = oldInherit;
                s.NodeChanged(id, "transform");
            }
        }

        public bool TryMerge(ISceneCommand next) => false;
        public long EstimatedBytes => 256;
        public IEnumerable<TreeNode> HeldNodes => Array.Empty<TreeNode>();
    }

    /// <summary>
    /// Replaces a character's appearance and normalises `state.accessories` against the new coordinate. With a
    /// character file to place (<c>placeFrom</c>: the first edit of a card-backed character, whose card was just imported),
    /// Apply also puts a copy of that subtree into the scene carrier under the node id (its resources copied) and makes it the
    /// appearance's stored subtree; Revert restores the carrier's entry for the id (removed when there was none), so an
    /// undone or cancelled first edit leaves no subtree behind; redo places the same copy again. Resources copied on apply
    /// stay (content-addressed; a save writes only the reachable ones).
    /// </summary>
    public sealed class SetAppearanceCommand : ISceneCommand
    {
        readonly string id;
        readonly CharaAppearance appearance;
        readonly CharaContainer placeFrom;
        CharaAppearance old;
        SortedDictionary<string, bool> mapBefore;
        bool changed, mapChanged;
        CharaSubtree placed, carrierBefore;
        bool carrierHad;

        /// <param name="placeFrom">A character file whose subtree is placed in the scene carrier and becomes
        /// <paramref name="appearance"/>'s stored subtree on apply (null = the appearance is used as given).</param>
        public SetAppearanceCommand(string id, CharaAppearance appearance, string label = "Appearance", CharaContainer placeFrom = null)
        {
            this.id = id;
            this.appearance = appearance;
            this.placeFrom = placeFrom;
            Label = label;
        }

        public string Label { get; }
        public CharaAppearance Appearance => appearance;

        public void Validate(SceneModel m)
        {
            var n = m.Find(id) ?? throw new CommandException(CommandCodes.NotFound, "node " + id + " is not in the scene");
            if (n.Kind != NodeKind.Chara) throw new CommandException(CommandCodes.WrongKind, "node " + id + " is not a character");
            if (appearance == null) throw new CommandException(CommandCodes.BadArgs, "appearance is null");
            if (placeFrom != null && placeFrom.Subtree == null) throw new CommandException(CommandCodes.BadArgs, "the character file to place has no subtree");
            if (placeFrom != null && m.File == null) throw new CommandException(CommandCodes.BadArgs, "the scene has no file carrier");
        }

        public void Apply(SceneModel m, ChangeSink s)
        {
            var n = m.Find(id);
            old = n.Appearance;
            changed = !ReferenceEquals(old, appearance);
            if (!changed) return;
            if (placeFrom != null)
            {
                carrierHad = m.File.Charas.TryGetValue(id, out carrierBefore);
                if (placed == null)
                {
                    m.File.PlaceCharacter(id, placeFrom);
                    placed = m.File.Charas[id];
                }
                else
                {
                    m.File.Charas[id] = placed;
                    m.File.Res.CopyFrom(placeFrom.Res, placed.ReferencedResources());
                }
                appearance.Stored = placed;
            }
            var state = (CharaNodeState)n.Doc.State;
            mapBefore = new SortedDictionary<string, bool>(state.State.Accessories, StringComparer.Ordinal);
            n.Appearance = appearance;
            s.AppearanceChanged(id);
            mapChanged = AccessoryNormalizer.Normalize(n);
            if (mapChanged) s.NodeChanged(id, "chara.state.accessories");
        }

        public void Revert(SceneModel m, ChangeSink s)
        {
            if (!changed) return;
            var n = m.Find(id);
            n.Appearance = old;
            if (placeFrom != null)
            {
                if (carrierHad) m.File.Charas[id] = carrierBefore;
                else m.File.Charas.Remove(id);
                carrierBefore = null;
            }
            s.AppearanceChanged(id);
            if (mapChanged)
            {
                ((CharaNodeState)n.Doc.State).State.Accessories = new SortedDictionary<string, bool>(mapBefore, StringComparer.Ordinal);
                s.NodeChanged(id, "chara.state.accessories");
            }
        }

        public bool TryMerge(ISceneCommand next) => false;
        public long EstimatedBytes => 256 + (appearance?.Stored != null || placeFrom != null ? 256 * 1024 : 0) + (old?.Stored != null ? 256 * 1024 : 0);
        public IEnumerable<TreeNode> HeldNodes => Array.Empty<TreeNode>();
    }
}

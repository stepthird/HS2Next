// Conversion between the scene model and its file model. Binding wraps
// the container's document objects into tree nodes (the model shares them; nothing is copied), so every byte
// the model does not change is written back as read. Preparing a save re-wraps the model into the container:
// the node list in pre-order, the settings, scene ext and unknown members, the title, the node-scope files
// and the character subtrees by node id. Orphan subtrees and node ext of missing nodes are dropped (2.1).
using System;
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    public static class SceneDocumentConverter
    {
        /// <summary>The scene.json document of the model: nodes in pre-order (shared objects), settings, the Timeline, the constraints,
        /// the ragdoll block, the scripts, scene ext, unknown members.</summary>
        public static SceneDocument ToDocument(SceneModel m)
        {
            var nodes = new List<SceneNode>(m.Count);
            foreach (var n in m.PreOrder()) nodes.Add(n.Doc);
            return new SceneDocument { Nodes = nodes, Scene = m.Settings, Timeline = m.Timeline, Constraints = m.Constraints, Ragdoll = m.Ragdoll, Scripts = m.Scripts, Ext = m.SceneExt,
                Unknown = m.RootUnknown };
        }

        /// <summary>
        /// Builds the tree of <paramref name="m"/> (already reset) from <paramref name="c"/>. A document
        /// built by hand must have a repaired tree: a missing parent, a parent that takes no children or a cycle throws
        /// <see cref="ArgumentException"/>.
        /// </summary>
        internal static void Bind(SceneContainer c, SceneModel m, FindingList findings)
        {
            CheckTree(c);
            var doc = c.Scene ?? new SceneDocument();
            m.File = c;
            m.Title = c.Title ?? "";
            m.Settings = doc.Scene ?? new SceneSettings();
            m.Timeline = doc.Timeline;
            m.Constraints = doc.Constraints != null && doc.Constraints.Count > 0 ? doc.Constraints : null;
            m.Ragdoll = doc.Ragdoll != null && !doc.Ragdoll.IsEmpty ? doc.Ragdoll : null;
            m.Scripts = doc.Scripts != null && doc.Scripts.Count > 0 ? doc.Scripts : null;
            m.SceneExt = doc.Ext ?? new ExtInline();
            m.RootUnknown = doc.Unknown;
            var docs = doc.Nodes ?? new List<SceneNode>();

            // Canonical pre-order and sibling numbers (a read document already has both).
            var ordered = SceneDocument.CanonicalOrder(docs, out var order);
            var nodes = new Dictionary<string, TreeNode>(StringComparer.Ordinal);
            foreach (var d in ordered)
            {
                // A character node without a subtree has no appearance (the reader reported scene.charaMissing); a node block
                // of the retired card reference namespace is an unknown block, kept and written back unchanged.
                CharaAppearance app = null;
                if (d.State is CharaNodeState)
                {
                    app = new CharaAppearance();
                    if (c.Charas.TryGetValue(d.Id, out var t)) app.Stored = t;
                }
                c.NodeExt.TryGetValue(d.Id, out var fileExt);
                var n = new TreeNode(d, app, fileExt);
                nodes[d.Id] = n;
                var parent = d.Parent == null ? null : nodes[d.Parent.Node];
                if (d.Order != order[d]) d.Order = order[d];
                m.Attach(n, parent, d.Parent?.Point, -1);
            }
        }

        /// <summary>Throws <see cref="ArgumentException"/> when the document's tree is not repaired: a node without id or kind
        /// section, a duplicate id, a missing parent, a parent that takes no children, a cycle.</summary>
        internal static void CheckTree(SceneContainer c)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            var docs = c.Scene?.Nodes ?? new List<SceneNode>();
            var byId = new Dictionary<string, SceneNode>(StringComparer.Ordinal);
            foreach (var d in docs)
            {
                if (d?.Id == null || d.State == null) throw new ArgumentException("scene document node without id or kind section");
                if (byId.ContainsKey(d.Id)) throw new ArgumentException("duplicate node id " + d.Id);
                byId[d.Id] = d;
            }
            var reachesRoot = new HashSet<string>(StringComparer.Ordinal);
            foreach (var d in docs)
            {
                var path = new HashSet<string>(StringComparer.Ordinal);
                for (var cur = d; cur != null && !reachesRoot.Contains(cur.Id);)
                {
                    if (!path.Add(cur.Id)) throw new ArgumentException("parent cycle at node " + cur.Id);
                    if (cur.Parent == null) break;
                    if (!byId.TryGetValue(cur.Parent.Node ?? "", out var p)) throw new ArgumentException("node " + cur.Id + ": parent " + cur.Parent.Node + " does not exist");
                    if (!p.State.TakesChildren) throw new ArgumentException("node " + cur.Id + ": a " + p.Kind + " node takes no children");
                    cur = p;
                }
                reachesRoot.UnionWith(path);
            }
        }

        /// <summary>
        /// Fills the container of <paramref name="m"/> for a save: document, title, node-scope files of
        /// the nodes in the model, character subtrees by node id.
        /// </summary>
        internal static SceneContainer Fill(SceneModel m)
        {
            var c = m.File;
            c.Scene = ToDocument(m);
            c.Title = m.Title ?? "";
            c.NodeExt.Clear();
            c.Charas.Clear();
            foreach (var n in m.PreOrder())
            {
                if (n.FileExt != null) c.NodeExt[n.Id] = n.FileExt;
                if (n.Kind == NodeKind.Chara && n.Appearance?.Stored != null) c.Charas[n.Id] = n.Appearance.Stored;
            }
            return c;
        }

        /// <summary>After a save: node-scope data the container created for nodes without one becomes theirs.</summary>
        internal static void Adopt(SceneModel m)
        {
            foreach (var n in m.PreOrder())
                if (n.FileExt == null && m.File.NodeExt.TryGetValue(n.Id, out var d)) n.FileExt = d;
        }
    }
}

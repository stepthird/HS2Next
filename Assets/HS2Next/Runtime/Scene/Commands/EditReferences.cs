// Reference documents of the Maker's per-row reset (decision F1): the first edit session of
// a character in a scene session (StudioOps.BeginEditSession) keeps a canonical clone of its documents (chara.json +
// coord.json); the row reset ↺ (StudioOps.ResetAppearance) sets a path back to the value it had then — HS2's edit-mode
// `defChaCtrl` (the character as loaded when the Maker opened). A card-backed node without documents is captured from the
// importer's result (what its first edit will import). References are not saved; they are forgotten when the node is
// removed (a committed / undone / redone NodeRemoved, or NodeForgotten) and cleared when the scene is replaced (Cleared:
// new / open) — the bound session's events. Values are read through FieldAccessor on a private holder session that owns
// the clone, so every appearance path (hair parts, clothes kinds, accessories["id"], saved-order lists) resolves as on the
// live documents; a returned value is a deep copy. Since 4.2 (Y13) every scene session owns
// its store (SceneSession.MakerReferences, bound to that session for its life), so two sessions in one process (tests, two
// hosts) never share references; MakerServices.References stays as the 4.1 accessor of the session the Maker ops ran on
// last. Unity-free. Main thread.
using System;
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    public sealed class EditReferences
    {
        sealed class Entry
        {
            public SceneSession Holder;
            public string HolderId;
        }

        readonly Dictionary<string, Entry> byNode = new Dictionary<string, Entry>(StringComparer.Ordinal);
        SceneSession bound;
        // the 4.1 accessor's empty store (MakerServices.References without a session): Attach selects the session's own store
        readonly bool forwarding;

        /// <summary>A store of no session (tests; <see cref="Attach"/> binds it).</summary>
        public EditReferences() { }

        /// <summary>The store of <paramref name="owner"/> (SceneSession.MakerReferences): bound to
        /// its events for the life of the session.</summary>
        internal EditReferences(SceneSession owner)
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Attach(owner);
        }

        EditReferences(bool forwarding) { this.forwarding = forwarding; }

        internal static EditReferences Forwarding() => new EditReferences(true);

        /// <summary>The scene session owning this store (its MakerReferences), or null for a free store.</summary>
        public SceneSession Owner { get; }

        /// <summary>Characters holding a reference.</summary>
        public int Count => byNode.Count;

        /// <summary>The session whose events forget / clear the references (null until <see cref="Attach"/>).</summary>
        public SceneSession Session => bound;

        /// <summary>True when node <paramref name="nodeId"/> holds a reference.</summary>
        public bool Has(string nodeId) => nodeId != null && byNode.ContainsKey(nodeId);

        /// <summary>
        /// Keeps a canonical clone of <paramref name="documents"/> as the reference of <paramref name="nodeId"/>, unless the node
        /// already holds one (the first session wins). False when nothing was captured (already held, or no documents).
        /// </summary>
        public bool Capture(string nodeId, CharaSubtree documents)
        {
            if (nodeId == null || documents == null || byNode.ContainsKey(nodeId)) return false;
            var holder = new SceneSession(new SessionOptions { Ids = new GuidIdSource() });
            var r = holder.AddChara(new CharaAppearance { Stored = documents.Clone() });
            if (r.Status != CommandStatus.Ok || r.NewIds == null || r.NewIds.Count == 0) return false;
            byNode[nodeId] = new Entry { Holder = holder, HolderId = r.NewIds[0] };
            return true;
        }

        /// <summary>
        /// The first-session capture of a character node of <paramref name="session"/> (binds to its events): its stored
        /// documents, or for a card-backed node the importer's result (CharaImport.Importer; nothing without one). False when
        /// nothing was captured.
        /// </summary>
        public bool CaptureNode(SceneSession session, TreeNode node, FindingList findings = null)
        {
            // a node of another session belongs in that session's own store
            if (session != null && (forwarding || (Owner != null && !ReferenceEquals(session, Owner))))
            {
                MakerServices.UseSession(session);
                return session.MakerReferences.CaptureNode(session, node, findings);
            }
            if (session != null) Attach(session);
            if (node == null || node.Kind != NodeKind.Chara || byNode.ContainsKey(node.Id)) return false;
            var stored = node.Appearance?.Stored;
            if (stored != null) return Capture(node.Id, stored);
            var card = node.Appearance?.Card;
            var importer = CharaImport.Importer;
            if (card == null || importer == null) return false;
            try
            {
                var cc = importer.Import(card, findings ?? new FindingList());
                return cc?.Subtree != null && Capture(node.Id, cc.Subtree);
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// The reference value at <paramref name="path"/> (a full `appearance.chara.…` / `appearance.coord.…` path) of node
        /// <paramref name="nodeId"/>, deep-copied; false when the node has no reference or the path does not resolve on it.
        /// </summary>
        public bool TryGet(string nodeId, string path, out object value)
        {
            value = null;
            if (nodeId == null || path == null || !byNode.TryGetValue(nodeId, out var e)) return false;
            if (!FieldAccessor.TryGet(e.Holder.Model, e.HolderId, path, out var v, out _) || FieldValue.IsAbsent(v)) return false;
            value = FieldSchema.Copy(v);
            return true;
        }

        /// <summary>The reference documents of a node (a copy), or null.</summary>
        public CharaSubtree DocumentsOf(string nodeId) =>
            nodeId != null && byNode.TryGetValue(nodeId, out var e) ? e.Holder.Model.Find(e.HolderId)?.Appearance?.Stored?.Clone() : null;

        /// <summary>Forgets the reference of one node.</summary>
        public void Forget(string nodeId) { if (nodeId != null) byNode.Remove(nodeId); }

        /// <summary>Forgets every reference.</summary>
        public void Clear() => byNode.Clear();

        /// <summary>
        /// Binds to <paramref name="session"/>'s events: a removed node (not a preview step) and a forgotten node lose their
        /// reference, a replaced scene clears all. Binding another session clears the references of the old one. A session's
        /// own store (<see cref="Owner"/>) stays bound to its owner: Attach with another session (the 4.1 call form
        /// `MakerServices.References.Attach(session)`) selects that session's store for MakerServices.References instead, as
        /// does Attach on the accessor's empty store.
        /// </summary>
        public void Attach(SceneSession session)
        {
            if (forwarding || (Owner != null && !ReferenceEquals(session, Owner)))
            {
                if (session != null) MakerServices.UseSession(session);
                return;
            }
            if (ReferenceEquals(session, bound)) return;
            Detach();
            Clear();
            bound = session;
            if (session == null) return;
            session.Events.Changed += OnChanged;
            session.Events.Cleared += Clear;
            session.Events.NodeForgotten += Forget;
        }

        /// <summary>Unbinds from the session's events (the references stay); a session's own store stays bound.</summary>
        public void Detach()
        {
            if (bound == null || Owner != null) return;
            bound.Events.Changed -= OnChanged;
            bound.Events.Cleared -= Clear;
            bound.Events.NodeForgotten -= Forget;
            bound = null;
        }

        void OnChanged(SceneChangeBatch b)
        {
            if (byNode.Count == 0 || b == null || b.Origin == ChangeOrigin.Preview) return;
            foreach (var c in b.Changes)
                if (c.Kind == SceneChangeKind.NodeRemoved) byNode.Remove(c.NodeId);
        }
    }
}

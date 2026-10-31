// The realization of one scene node: its holder GameObject (node
// transform, SceneNodeHolder marker) and the kind's content under it at local identity. SceneRuntime creates,
// parents, moves to the graveyard and revives holders; the kind classes build the content, apply changed field
// paths, give the attach transform of each child and apply visibility to their own renderers. Realization
// failures never fail commands: the node stays in the model and keeps its holder.
using System.Collections.Generic;
using HS2Next.Save;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>Realization states.</summary>
    public enum RealizationState
    {
        /// <summary>Holder exists, content not built yet.</summary>
        Pending,
        Built,
        /// <summary>A character whose appearance no registered builder understands (2.2: stored subtrees).</summary>
        Unbuildable,
        /// <summary>A character without appearance.</summary>
        Missing,
        /// <summary>Building threw or the content could not be loaded; the holder stays.</summary>
        Failed,
        /// <summary>An unknown-kind node or a node inside its subtree: no holder.</summary>
        NotBuilt,
    }

    public abstract class NodeRealization
    {
        protected NodeRealization(SceneRuntime runtime, TreeNode node)
        {
            Runtime = runtime;
            Node = node;
            NodeId = node.Id;
            Kind = node.Kind;
        }

        public SceneRuntime Runtime { get; }
        /// <summary>The model node (the same object across delete/undo; a new object after a load).</summary>
        public TreeNode Node { get; internal set; }
        public string NodeId { get; }
        public NodeKind Kind { get; }
        public RealizationState State { get; protected internal set; } = RealizationState.Pending;
        /// <summary>Why the build failed (or other state detail), else null.</summary>
        public string Detail { get; protected internal set; }

        public GameObject HolderObject { get; private set; }
        public Transform Holder => HolderObject ? HolderObject.transform : null;
        public bool HasHolder => HolderObject != null;
        /// <summary>Kinds with a scale channel: character and item; the others keep (1, 1, 1).</summary>
        public bool HasScaleChannel => Kind == NodeKind.Chara || Kind == NodeKind.Item;
        /// <summary>The holder sits in the inactive graveyard (deleted node kept for undo).</summary>
        public bool InGraveyard => HasHolder && Runtime.GraveyardRoot && Holder.IsChildOf(Runtime.GraveyardRoot);

        protected RuntimeServices Services => Runtime.Services;

        // ------------------------------------------------------------ holder

        internal void CreateHolder(Transform parent)
        {
            var go = new GameObject(HolderName());
            go.transform.SetParent(parent, false);
            var h = go.AddComponent<SceneNodeHolder>();
            h.NodeId = NodeId;
            HolderObject = go;
        }

        /// <summary>"&lt;kind&gt;:&lt;first 8 hex of the id&gt;", then " &lt;name&gt;" when the node has one.</summary>
        internal string HolderName()
        {
            string kind = Node.Kind == NodeKind.Opaque ? Node.Doc?.Kind ?? "node" : Node.Doc?.Kind ?? Kind.ToString().ToLowerInvariant();
            string id = NodeId ?? "";
            string n = kind + ":" + (id.Length > 8 ? id.Substring(0, 8) : id);
            return string.IsNullOrEmpty(Node.Doc?.Name) ? n : n + " " + Node.Doc.Name;
        }

        /// <summary>Destroys the holder with everything under it (child holders included) and releases the content.</summary>
        internal void DestroyHolder()
        {
            ReleaseContent();
            if (HolderObject) UnityObjects.Destroy(HolderObject);
            HolderObject = null;
        }

        // ------------------------------------------------------------ kind hooks

        /// <summary>Builds the content under the holder and applies the stored state (the holder is parented and posed).</summary>
        protected internal abstract void BuildContent();

        /// <summary>Destroys the content (not the holder, not child holders: the runtime parks them first).</summary>
        protected internal virtual void DestroyContent() { }

        /// <summary>Releases what the content holds outside the hierarchy before the holder is destroyed (render textures, card references).</summary>
        protected internal virtual void ReleaseContent() { }

        /// <summary>Applies changed field paths (canonical texts; a prefix means "anything below").</summary>
        protected internal virtual void ApplyChanges(IReadOnlyCollection<string> paths) { }

        /// <summary>
        /// The transform a child attached at <paramref name="point"/> hangs under, and a finding when it is a fallback
        ///.
        /// </summary>
        protected internal virtual Transform ChildAttach(string point, out AttachNote note)
        {
            note = point != null ? new AttachNote(FindingSeverity.Info, "scene.pointIgnored", "attach point \"" + point + "\" under a " + Kind.ToString().ToLowerInvariant() + " is ignored") : default;
            return Holder;
        }

        /// <summary>Own renderers: forceRenderingOff = !visible on every renderer down to the next holder.</summary>
        protected internal virtual void ApplyVisibility(bool visible) => VisibilityApplier.SetOwn(Holder, visible);

        /// <summary>The runtime moved the node's holder (reparent, revive).</summary>
        protected internal virtual void OnReparented() { }

        // ------------------------------------------------------------ helpers

        protected static bool Touches(IReadOnlyCollection<string> paths, string prefix)
        {
            foreach (var p in paths) if (FieldPath.Overlaps(p, prefix)) return true;
            return false;
        }

        /// <summary>The label of a bundle owner of this node: "&lt;kind&gt;:&lt;first 8 of the node id&gt;".</summary>
        protected string BundleOwnerLabel(string kind) => kind + ":" + (NodeId != null && NodeId.Length > 8 ? NodeId.Substring(0, 8) : NodeId);

        protected void Report(FindingSeverity s, string code, string message) => Runtime.Report(s, code, NodeId, message);
        protected void ReportOnce(FindingSeverity s, string code, string message) => Runtime.ReportOnce(s, code, NodeId, message);

        public override string ToString() => HolderName() + " " + State + (Detail != null ? " (" + Detail + ")" : "");
    }

    /// <summary>A finding about where a child was attached (fallbacks).</summary>
    public readonly struct AttachNote
    {
        public readonly FindingSeverity Severity;
        public readonly string Code;
        public readonly string Message;
        public AttachNote(FindingSeverity severity, string code, string message) { Severity = severity; Code = code; Message = message; }
        public bool IsEmpty => Code == null;
    }

    /// <summary>Unknown-kind nodes and the nodes of their subtrees: no holder, nothing built.</summary>
    public sealed class NotBuiltRealization : NodeRealization
    {
        internal NotBuiltRealization(SceneRuntime runtime, TreeNode node) : base(runtime, node) { State = RealizationState.NotBuilt; }
        protected internal override void BuildContent() { }
        protected internal override void ApplyVisibility(bool visible) { }
    }

    /// <summary>Conversions between the 2.1 value structs and Unity types, and object destruction.</summary>
    public static class UnityObjects
    {
        public static Vector3 V(Vec3 v) => new Vector3(v.X, v.Y, v.Z);
        public static Vec3 V(Vector3 v) => new Vec3(v.x, v.y, v.z);
        public static Quaternion Q(Quat4 q) => new Quaternion(q.X, q.Y, q.Z, q.W);
        public static Quat4 Q(Quaternion q) => new Quat4(q.x, q.y, q.z, q.w);
        public static Color C(Color4 c) => new Color(c.R, c.G, c.B, c.A);

        /// <summary>DestroyImmediate (deterministic in edit-mode batch runs and in play mode alike).</summary>
        public static void Destroy(Object o)
        {
            if (o) Object.DestroyImmediate(o);
        }

        /// <summary>
        /// Releases and destroys the render textures that the materials of the own renderers of <paramref name="start"/>
        /// (down to the next holder) reference: the composited skins and clothes of a character.
        /// </summary>
        public static int ReleaseRenderTextures(Transform start)
        {
            if (!start) return 0;
            var rts = new HashSet<RenderTexture>();
            foreach (var r in VisibilityApplier.OwnRenderers(start))
                foreach (var m in r.sharedMaterials)
                {
                    if (!m) continue;
                    foreach (var id in m.GetTexturePropertyNameIDs())
                        if (m.GetTexture(id) is RenderTexture rt && rt) rts.Add(rt);
                }
            foreach (var rt in rts)
            {
                if (RenderTexture.active == rt) RenderTexture.active = null;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
            return rts.Count;
        }
    }
}

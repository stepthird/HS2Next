// Typed main-thread events of the app for plugins. Each plugin has its own IAppEvents; its handlers run synchronously
// with the host's event, after the host's own handlers and in plugin load order. A handler that throws is counted as a
// fault of its plugin and does not stop the other handlers. While a scene event or a capture event is being delivered the
// scene answers busy to changes, so handlers record what happened and act later (the next Update, a scheduled frame action,
// or CallAsync / RequestAsync). A handler is never re-entered for the same plugin: an event raised while it runs is
// delivered right after it returns. Content is ids, numbers and file paths only. Part of the stable API (assembly
// HS2Next.Api).
using System;
using System.Collections.Generic;

namespace HS2Next.Api.Plugins
{
    /// <summary>The app's events for one plugin. Implemented by the host.</summary>
    public interface IAppEvents
    {
        /// <summary>Once, when the app finished starting (after every plugin loaded); once more after a runtime enable of the plugin.</summary>
        event Action AppReady;
        /// <summary>Before a new or loaded scene replaces the current one.</summary>
        event Action SceneCleared;
        /// <summary>After a new or loaded scene, and once for the initial scene (the same notification as <see cref="IPluginSceneLoaded"/>).</summary>
        event Action<SceneLoadInfo> SceneLoaded;
        /// <summary>Before a save converts the scene (ext handlers write after this).</summary>
        event Action SceneSaving;
        /// <summary>After a save, successful or not.</summary>
        event Action<SceneSaveResult> SceneSaved;
        /// <summary>Nodes were added (also by an undo that brings removed nodes back). Once per change that holds additions.</summary>
        event Action<NodeChangeInfo> NodesAdded;
        /// <summary>Nodes were removed (an undo may bring them back). Once per change that holds removals.</summary>
        event Action<NodeChangeInfo> NodesRemoved;
        /// <summary>Nodes were moved to another parent or reordered. Once per change that holds moves.</summary>
        event Action<NodeChangeInfo> NodesMoved;
        /// <summary>A removed node's id can never come back (no undo step holds it any more).</summary>
        event Action<string> NodeForgotten;
        /// <summary>A scene character was built or rebuilt.</summary>
        event Action<CharaBuiltInfo> CharaBuilt;
        /// <summary>The global pause changed (the argument is the new state).</summary>
        event Action<bool> PausedChanged;
        /// <summary>Steps ran while paused (a step command or the toolbar).</summary>
        event Action<StepInfo> SceneStepped;
        /// <summary>
        /// The app is quitting (or an Editor play session ends): flush what must be kept. Before Unload; a runtime disable
        /// does not raise it.
        /// </summary>
        event Action Quitting;

        /// <summary>
        /// A screenshot, a capture or a recorded frame is about to render (right before the render): hide the plugin's own
        /// overlay objects here.
        /// Exactly one <see cref="CaptureRendered"/> and one <see cref="CaptureFinished"/> follow. The scene answers busy to
        /// changes through Call or Request during the delivery: the capture renders the scene as it is, and a plugin changes
        /// its own objects directly.
        /// </summary>
        event Action<CaptureInfo> CaptureStarting;
        /// <summary>The render of a capture returned, in the same frame as <see cref="CaptureStarting"/> (also when it failed): show the overlays again here.</summary>
        event Action<CaptureInfo> CaptureRendered;
        /// <summary>
        /// A capture's file was written or the capture failed: for a screenshot and a recorded frame a few frames after the
        /// render, for the capture command in the same frame.
        /// </summary>
        event Action<CaptureResultInfo> CaptureFinished;
    }

    /// <summary>The result of a scene save.</summary>
    public sealed class SceneSaveResult
    {
        /// <summary>Creates the result (the host does; plugins read it).</summary>
        public SceneSaveResult(bool ok, long size, string error)
        {
            Ok = ok;
            Size = size;
            Error = error;
        }

        /// <summary>True when the file was written.</summary>
        public bool Ok { get; }
        /// <summary>Bytes written (0 on failure).</summary>
        public long Size { get; }
        /// <summary>The error message on failure, else null.</summary>
        public string Error { get; }
    }

    /// <summary>Where a scene change came from.</summary>
    public enum ChangeOrigin
    {
        /// <summary>A command ran (or a transaction committed).</summary>
        Command,
        /// <summary>An undo step.</summary>
        Undo,
        /// <summary>A redo step.</summary>
        Redo,
        /// <summary>A step inside an open transaction (a drag in progress).</summary>
        Preview,
        /// <summary>A transaction was cancelled and its steps reverted.</summary>
        Cancel,
        /// <summary>Runtime values written back into the scene (before a save, a duplicate or a delete).</summary>
        Capture,
        /// <summary>A derived consistency fix.</summary>
        Normalize,
    }

    /// <summary>One node of a change.</summary>
    public readonly struct NodeRef
    {
        /// <summary>Creates the reference (the host does; plugins read it).</summary>
        public NodeRef(string id, string kind, string parentId)
        {
            Id = id;
            Kind = kind;
            ParentId = parentId;
        }

        /// <summary>The node id.</summary>
        public string Id { get; }
        /// <summary>The node kind as the bridge names it (e.g. "item", "chara", "folder").</summary>
        public string Kind { get; }
        /// <summary>The parent's id after the change (before it for a removal), or null at the root.</summary>
        public string ParentId { get; }

        /// <summary>"kind id".</summary>
        public override string ToString() => Kind + " " + Id;
    }

    /// <summary>The nodes of one kind of record in one scene change.</summary>
    public sealed class NodeChangeInfo
    {
        /// <summary>Creates the change (the host does; plugins read it). A null list is empty.</summary>
        public NodeChangeInfo(IReadOnlyList<NodeRef> nodes, ChangeOrigin origin)
        {
            Nodes = nodes ?? Array.Empty<NodeRef>();
            Origin = origin;
        }

        /// <summary>In the order of the change (a subtree top first, then pre-order).</summary>
        public IReadOnlyList<NodeRef> Nodes { get; }
        /// <summary>Where the change came from.</summary>
        public ChangeOrigin Origin { get; }
    }

    /// <summary>A character that was built.</summary>
    public sealed class CharaBuiltInfo
    {
        /// <summary>Creates the notification (the host does; plugins read it).</summary>
        public CharaBuiltInfo(string nodeId, bool rebuild)
        {
            NodeId = nodeId;
            Rebuild = rebuild;
        }

        /// <summary>The character's node id.</summary>
        public string NodeId { get; }
        /// <summary>The character existed before (an edit or an outfit change rebuilt it).</summary>
        public bool Rebuild { get; }
    }

    /// <summary>Steps that ran while paused.</summary>
    public readonly struct StepInfo
    {
        /// <summary>Creates the notification (the host does; plugins read it).</summary>
        public StepInfo(int count, float dt)
        {
            Count = count;
            Dt = dt;
        }

        /// <summary>Steps run.</summary>
        public int Count { get; }
        /// <summary>Seconds per step.</summary>
        public float Dt { get; }
    }
}

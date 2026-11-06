// What the Timeline evaluator drives: a channel per track target (a node's model field path, or the scene-wide camera and
// depth of field), and the sink the scene runtime implements to lay the evaluated values over the scene and to run the
// timed commands. The evaluator never touches the model or the engine; everything it shows goes through the sink.
using System;

namespace HS2Next.Scenes.Timeline
{
    /// <summary>
    /// A runtime channel the Timeline drives: (node id or null, channel path). One object per channel for the life of a
    /// <see cref="TimelinePlayer"/>: a document change keeps the object of a channel that is still driven, so the sink may
    /// keep its own state for the channel in <see cref="Tag"/>.
    /// </summary>
    public sealed class TimelineChannel
    {
        internal TimelineChannel(string nodeId, string path, TimelineTrackType type, string modelPath)
        {
            NodeId = nodeId;
            Path = path;
            Type = type;
            Kind = TimelineTrackTypes.ValueKind(type);
            ModelPath = modelPath;
        }

        /// <summary>The node, or null for the scene-wide channels (<c>camera</c>, <c>effects.depthOfField</c>).</summary>
        public string NodeId { get; }
        /// <summary>
        /// The canonical channel path: <c>transform.pos</c>, <c>transform.rot</c>, <c>transform.scale</c>,
        /// <c>chara.fk.bones["&lt;bone&gt;"]</c>, <c>chara.anime.ref</c>, <c>.speed</c>, <c>.time</c>, <c>chara.face.eyes</c>,
        /// <c>.mouth</c>, <c>.eyesOpen</c>, <c>.mouthOpen</c>, <c>item.colors[i].main</c>, <c>light.color</c>, <c>camera</c>,
        /// <c>effects.depthOfField</c>.
        /// </summary>
        public string Path { get; }
        /// <summary>The track type that drives the channel.</summary>
        public TimelineTrackType Type { get; }
        public TimelineValueKind Kind { get; }
        /// <summary>
        /// The model path (of the node, or of the scene object for a scene-wide channel) whose edits suspend the channel
        /// while the Timeline is paused or ended (<c>camera.current</c> for the camera).
        /// </summary>
        public string ModelPath { get; }

        /// <summary>Free for the sink (for example its override slot of this channel); the evaluator never reads it.</summary>
        public object Tag;

        /// <summary>A value of this channel is shown (set and neither cleared nor released since).</summary>
        public bool HasValue { get; internal set; }
        /// <summary>The channel is suspended: an edit made while the Timeline was paused shows until the play head moves.</summary>
        public bool Suspended { get; internal set; }

        /// <summary>The key of a channel: node id (empty for the scene) and path.</summary>
        internal static string KeyOf(string nodeId, string path) => (nodeId ?? "") + "\n" + path;

        internal string Key => KeyOf(NodeId, Path);

        public override string ToString() => (NodeId == null ? "scene" : NodeId) + " " + Path;
    }

    /// <summary>A timed command ready to run: its id, time and the request to submit.</summary>
    public sealed class TimelineEventRun
    {
        internal TimelineEventRun(int index, string id, double t, string requestJson)
        {
            Index = index;
            Id = id;
            T = t;
            RequestJson = requestJson;
        }

        /// <summary>The event's index in the document's event list.</summary>
        public int Index { get; }
        public string Id { get; }
        /// <summary>The event's time (the stored float widened).</summary>
        public double T { get; }
        /// <summary>
        /// The bridge request: the event's <c>do</c> object with <c>undo: false</c> added when it carries no <c>undo</c>, and
        /// <c>id</c> = <c>timeline:&lt;event id&gt;</c>.
        /// </summary>
        public string RequestJson { get; }
    }

    /// <summary>
    /// Implemented by the scene runtime: lays the Timeline's values over the scene (one override slot per channel, owner
    /// Timeline, priority 0) and runs its timed commands. Called on the main thread, from the player's calls only.
    /// </summary>
    public interface ITimelineSink
    {
        /// <summary>
        /// The kind of the node with this id when it is in the model now, else null (deleted, forgotten, never there). A
        /// track whose node is missing, or of a kind its type does not take, holds no slot (the scene-wide channels never
        /// ask).
        /// </summary>
        NodeKind? KindOf(string nodeId);

        /// <summary>A number channel (animation speed and time, eye and mouth openness).</summary>
        void SetNumber(TimelineChannel channel, double value);
        /// <summary>An integer channel (eye and mouth pattern ids).</summary>
        void SetInteger(TimelineChannel channel, int value);
        /// <summary>A vector channel (position, scale).</summary>
        void SetVector(TimelineChannel channel, in TimelineVec3 value);
        /// <summary>A rotation channel (a node's rotation, an FK bone's local rotation), as a unit quaternion.</summary>
        void SetRotation(TimelineChannel channel, in TimelineQuat value);
        /// <summary>A colour channel, in the stored form (sRGB-encoded rgb, straight alpha, not clamped).</summary>
        void SetColor(TimelineChannel channel, in TimelineColor value);
        /// <summary>The camera.</summary>
        void SetCamera(TimelineChannel channel, in TimelineCameraValue value);
        /// <summary>The depth of field.</summary>
        void SetDepthOfField(TimelineChannel channel, in TimelineDofValue value);
        /// <summary>An animation row (loaded when it differs from the row playing).</summary>
        void SetAnimation(TimelineChannel channel, in TimelineAnimeRow row);

        /// <summary>The channel shows no Timeline value now (a track without keys, a suspended channel); its slot may stay.</summary>
        void Clear(TimelineChannel channel);
        /// <summary>The Timeline no longer drives the channel (its track was removed or disabled, or its target is gone).</summary>
        void Release(TimelineChannel channel);
        /// <summary>Every Timeline slot is released (stop, the Timeline removed).</summary>
        void ReleaseAll();

        /// <summary>A timed command crossed by the play head: submit its request to the bridge queue (no wait for the reply).</summary>
        void Fire(TimelineEventRun run);
    }
}

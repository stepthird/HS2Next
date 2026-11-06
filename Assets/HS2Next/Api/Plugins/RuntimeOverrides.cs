// Runtime overrides (experimental: they may change in any minor release). A plugin lays values over the scene for display
// without changing it: a slot stands for one value of the scene model (a node's position, rotation or scale, a character's
// FK bone rotation, animation row, speed or time, face patterns and openness, an item's colour, a light's colour, the
// camera, the depth of field) or for a character's bone or IK target, and, while it holds a value and wins, the scene shows
// that value instead of the model's. Nothing of it is saved, recorded in the undo history or marked as an unsaved change;
// the model keeps its own values, which show again when the slot is cleared or released. The core Timeline and the
// constraints lay their values through the same layer. Part of the Api (assembly HS2Next.Api).
using System;
using System.Collections.Generic;

namespace HS2Next.Api.Plugins
{
    /// <summary>
    /// The plugin's runtime overrides: slots that lay values over the scene for display, never saved and never in the undo
    /// history. Implemented by the host, one instance per plugin.
    /// <para>
    /// A slot belongs to one channel, a node id (null for the scene-wide channels) and a path. Among the slots of one channel
    /// that hold a value, the one with the highest priority wins; among equal priorities the one acquired last wins. The core
    /// Timeline's slots have priority 0, so a plugin slot of priority 0 acquired after the Timeline's wins over it, priority -1
    /// yields to the Timeline, and priority 1 wins whatever the order. The constraints' slots have priority 10: a plugin slot
    /// stays above a constraint with priority 11 or more. A plugin holds at most one slot per channel.
    /// </para>
    /// <para>
    /// Lifetime: a slot lives until the plugin releases it. The host releases every slot of the plugin when the plugin is
    /// unloaded, faults or is disabled; every slot of every owner when a new scene is made or a scene is loaded (acquire again
    /// in the scene-loaded handler); and a node's slots when the node is forgotten (its id can never come back). A node that is
    /// deleted keeps its slots, which show again when an undo brings the node back; a character rebuild keeps its slots and
    /// applies them again.
    /// </para>
    /// <para>
    /// Main thread only (InvalidOperationException otherwise), except <see cref="Available"/>. A setter of a numeric form
    /// allocates nothing and costs a few microseconds, so a plugin may set its slots every frame (e.g. in its update).
    /// </para>
    /// </summary>
    [Experimental("runtime overrides may change in any minor release")]
    public interface IRuntimeOverrides
    {
        /// <summary>True when the host has a scene runtime; false in a host without one, where <see cref="Acquire"/> returns null. Any thread.</summary>
        bool Available { get; }

        /// <summary>
        /// Acquires the plugin's slot of the channel (<paramref name="nodeId"/>, <paramref name="path"/>) with the priority
        /// <paramref name="priority"/> (higher wins; equal priorities: the later acquired wins). The slot starts without a value.
        /// When the plugin already holds a slot of that channel, that slot is returned with its priority set to
        /// <paramref name="priority"/>. Returns null when no scene runtime exists or when <paramref name="nodeId"/> names no node
        /// of the scene (also not a deleted one that an undo could bring back).
        /// <para>
        /// Paths (field paths of the scene model; the slot shows what the model value would be, in the model's units):
        /// <c>transform.pos</c> (Vector3) and <c>transform.rot</c> (Rotation) of every node with a transform (characters,
        /// items, lights, folders, routes, cameras); <c>transform.scale</c> (Vector3; the model's meaning) of characters and
        /// items; for characters <c>chara.fk.bones["&lt;bone&gt;"]</c> (Rotation: the bone's local rotation, shown only where
        /// the FK pose writes the bone: FK on, the bone's group on, the bone used, IK not solving it),
        /// <c>chara.anime.ref</c> (Animation: the row is loaded at the next scene update when it differs from the row playing),
        /// <c>chara.anime.speed</c> (Number, at least 0; NaN counts as 0), <c>chara.anime.time</c> (Number: the raw normalized
        /// time, not wrapped; the character is posed at that time at once, also at speed 0, and its animation holds that time
        /// while the slot has a value), <c>chara.face.eyes</c> and <c>chara.face.mouth</c> (Integer: the pattern id),
        /// <c>chara.face.eyesOpen</c> and <c>chara.face.mouthOpen</c> (Number), <c>chara.bone["&lt;address&gt;"].pos</c>,
        /// <c>.rot</c> and <c>.scale</c> (Vector3, Rotation, Vector3: the bone's local values, written after FK, with the
        /// advanced pose's corrections on top; the address is the one of the advanced pose: <c>&lt;name&gt;</c>,
        /// <c>hair:&lt;k&gt;/&lt;name&gt;</c>, <c>clothes:&lt;k&gt;/&lt;name&gt;</c>, <c>acc:&lt;id&gt;/&lt;name&gt;</c>, each of the last
        /// three with an optional <c>#n</c>), <c>chara.ik.targets.&lt;Name&gt;.pos</c> and <c>.rot</c> (Vector3, Rotation:
        /// local to the character's root; the 13 names of the IK targets, <c>.rot</c> on hands and feet only; no effect
        /// while the target's group is not applied); for items <c>item.colors[i].main</c> with i
        /// from 0 to 3 (Color); for lights <c>light.color</c> (Color); and, with a null node id, <c>camera</c> (Camera: above
        /// the scene's camera and an active camera node) and <c>effects.depthOfField</c> (DepthOfField: instead of the scene's
        /// depth of field). Transforms, colours, the face and the animation speed and time are shown at once and at every
        /// later apply of the model value; FK bones, bones and IK targets at the next motion step; the camera and the depth of
        /// field in the frame's late update, before rendering.
        /// </para>
        /// ArgumentException naming the path and the expected form for an unknown path, a path the node's kind does not have,
        /// a node path with a null node id and a scene-wide path with a node id.
        /// </summary>
        IOverrideSlot Acquire(string nodeId, string path, int priority = 0);

        /// <summary>
        /// The slots of every owner (the plugins and the core Timeline) that hold a value, for diagnostics: all of them with a
        /// null <paramref name="nodeId"/>, else those of that node. Order: the scene-wide channels first (the camera, then the
        /// depth of field), then the nodes in the scene tree's pre-order with deleted nodes last, each node's channels by path;
        /// within one channel by ascending priority, then in the order of acquisition. Empty when no scene runtime exists.
        /// </summary>
        IReadOnlyList<OverrideInfo> List(string nodeId = null);

        /// <summary>Releases every slot of this plugin and returns how many were released.</summary>
        int ReleaseAll();
    }

    /// <summary>
    /// One slot of a plugin: a value laid over one channel of the scene for display. Implemented by the host.
    /// <para>
    /// A setter returns false when the slot is released and true otherwise; a value equal bit for bit to the current one
    /// changes nothing. A setter whose form does not fit the slot's <see cref="Kind"/> throws ArgumentException naming the path
    /// and the expected form. A number that is not finite (NaN or an infinity) throws ArgumentException as well, except for
    /// <c>chara.anime.speed</c>, where NaN and negative values count as 0. Setters, <see cref="Clear"/> and <see cref="Release"/> run on the main thread only
    /// (InvalidOperationException otherwise); the read members answer on any thread and keep answering after the release.
    /// <see cref="IDisposable.Dispose"/> releases the slot.
    /// </para>
    /// </summary>
    [Experimental("runtime overrides may change in any minor release")]
    public interface IOverrideSlot : IDisposable
    {
        /// <summary>The node id; null for <c>camera</c> and <c>effects.depthOfField</c>.</summary>
        string NodeId { get; }

        /// <summary>The path in its canonical form (a bone name quoted as a JSON string, an index in decimal).</summary>
        string Path { get; }

        /// <summary>The shape of the slot's value.</summary>
        OverrideValueKind Kind { get; }

        /// <summary>The priority: higher wins over lower; among equal priorities the later acquired slot wins.</summary>
        int Priority { get; }

        /// <summary>True once the slot is released (by the plugin or by the host).</summary>
        bool IsReleased { get; }

        /// <summary>True while the slot holds a value.</summary>
        bool HasValue { get; }

        /// <summary>True when the slot's value is the one applied now: it holds a value and no other slot of the channel wins.</summary>
        bool IsWinning { get; }

        /// <summary>
        /// True when the node is in the scene (not deleted) and built; for the scene-wide channels, true while the scene
        /// exists.
        /// </summary>
        bool TargetExists { get; }

        /// <summary>Sets a Number value, or an Integer value (rounded to the nearest whole number).</summary>
        bool Set(double value);

        /// <summary>
        /// Sets a Vector3 value; a Rotation as Euler angles in degrees (the engine's convention: about Z, then X, then Y); a
        /// Color as red, green, blue with alpha 1; an Animation row as group, category, number (resolved as the
        /// <c>chara.anime</c> command resolves a row given without a source).
        /// </summary>
        bool Set(double x, double y, double z);

        /// <summary>
        /// Sets a Rotation as a quaternion (x, y, z, w; normalized by the host; ArgumentException for length 0), or a Color
        /// as red, green, blue, alpha.
        /// </summary>
        bool Set(double x, double y, double z, double w);

        /// <summary>
        /// Sets any value from its numbers: Number or Integer 1, Vector3 3, Rotation 3 (Euler angles) or 4 (a quaternion),
        /// Color 3 or 4, Animation 3 (group, category, number), Camera 8 (target x, y, z, rotation x, y, z in Euler degrees,
        /// distance, field of view), DepthOfField 7 (enabled as 0 or 1, focal size, aperture, blur, focus point x, y, z in
        /// world space). ArgumentNullException for null; ArgumentException for another length.
        /// </summary>
        bool Set(double[] values);

        /// <summary>
        /// Sets any value from its JSON form: Number and Integer a number; Vector3 <c>[x, y, z]</c>; Rotation <c>[x, y, z]</c>
        /// (Euler degrees) or <c>[x, y, z, w]</c> (a quaternion); Color <c>[r, g, b, a]</c> or <c>[r, g, b]</c>; Animation
        /// <c>{"source"?, "group", "category", "no"}</c>; Camera <c>{"target": [x, y, z], "rot": [x, y, z], "dist": d, "fov": f}</c>
        /// (all four); DepthOfField <c>{"enabled", "focus", "focalSize", "aperture", "blur"?, "focusTo"?, "focusBlend"?}</c>.
        /// A depth-of-field focus is <c>null</c> or <c>"pivot"</c> (the pivot of the camera as shown), <c>{"node": id}</c>
        /// (the node's point: a character between the eyes, else its head, else its transform), <c>{"node": id, "bone":
        /// name, "offset": [x, y, z]}</c> (the world position of that transform under the node plus the offset in world axes),
        /// <c>{"point": [x, y, z]}</c> (a world position) or <c>{"dist": d}</c> (d along the camera's forward axis); a node or
        /// bone that does not resolve acts as the pivot. With <c>focusTo</c> (a second focus) and <c>focusBlend</c> (0 to 1)
        /// the two resolved points are blended. <c>blur</c> above 0 is the largest blur radius on a 1080-line frame (scaled
        /// with the frame height); absent or 0 keeps the renderer's own. Colours are the stored values (sRGB-encoded red,
        /// green, blue, straight alpha, not clamped). ArgumentException for JSON of another shape.
        /// </summary>
        bool SetJson(string json);

        /// <summary>
        /// Removes the value: the next winning slot of the channel, or the model value, shows. The slot stays acquired. False
        /// when the slot is released.
        /// </summary>
        bool Clear();

        /// <summary>
        /// Releases the slot: its value no longer shows and its setters return false. Releasing a released slot does nothing.
        /// </summary>
        void Release();
    }

    /// <summary>The shape of a slot's value (see <see cref="IOverrideSlot"/> for the forms each setter takes).</summary>
    [Experimental("runtime overrides may change in any minor release")]
    public enum OverrideValueKind
    {
        /// <summary>One number: an animation speed or time, a face openness.</summary>
        Number = 0,
        /// <summary>One whole number: a face pattern id.</summary>
        Integer = 1,
        /// <summary>Three numbers: a position or a scale.</summary>
        Vector3 = 2,
        /// <summary>A rotation, as Euler angles in degrees or as a quaternion: a node's or a bone's local rotation, an IK target's rotation.</summary>
        Rotation = 3,
        /// <summary>A colour: red, green, blue (sRGB-encoded) and straight alpha, as the scene stores colours.</summary>
        Color = 4,
        /// <summary>The camera: target, rotation (Euler degrees), distance, field of view.</summary>
        Camera = 5,
        /// <summary>The depth of field: enabled, focus, focal size, aperture, blur.</summary>
        DepthOfField = 6,
        /// <summary>A character's animation row: group, category, number (and optionally its source).</summary>
        Animation = 7,
    }

    /// <summary>One slot that holds a value, as <see cref="IRuntimeOverrides.List"/> reports it. Created by the host.</summary>
    [Experimental("runtime overrides may change in any minor release")]
    public sealed class OverrideInfo
    {
        /// <summary>Creates the description (the host does; plugins read it).</summary>
        public OverrideInfo(string nodeId, string path, string owner, int priority, bool winning, bool effective, bool targetExists)
        {
            NodeId = nodeId;
            Path = path;
            Owner = owner;
            Priority = priority;
            Winning = winning;
            Effective = effective;
            TargetExists = targetExists;
        }

        /// <summary>The node id; null for the scene-wide channels.</summary>
        public string NodeId { get; }

        /// <summary>The path in its canonical form.</summary>
        public string Path { get; }

        /// <summary>The owner: <c>timeline</c> for the core Timeline, <c>constraints</c> for the constraints, else the plugin's GUID.</summary>
        public string Owner { get; }

        /// <summary>The slot's priority.</summary>
        public int Priority { get; }

        /// <summary>True when the slot's value is the one applied now.</summary>
        public bool Winning { get; }

        /// <summary>
        /// True when the slot wins and its value takes effect; false when it does not win or has no effect now (an FK bone
        /// whose group is not applied or that the body lacks, a bone address that does not resolve or a bone another stage
        /// writes later, an IK target whose group is not applied, a depth-of-field focus that does not resolve).
        /// </summary>
        public bool Effective { get; }

        /// <summary>True when the node is in the scene and built (the scene-wide channels: while the scene exists).</summary>
        public bool TargetExists { get; }

        /// <summary>"&lt;node&gt; &lt;path&gt; &lt;owner&gt; p&lt;priority&gt;" for logs, with "winning" when it wins.</summary>
        public override string ToString() => (NodeId ?? "scene") + " " + Path + " " + Owner + " p" + Priority + (Winning ? " winning" : "");
    }
}

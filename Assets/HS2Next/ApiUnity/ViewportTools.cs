// Viewport tools (engine side of the plugin Api; experimental: they may change in any minor release). While a tool runs the
// host offers it the viewport's pointer, wheel and keys first. In the default mode the camera, the shortcuts, the gizmos and
// the selection rest, and they are back as they were when the tool ends; with ToolOptions.PassThrough what the tool does not
// claim goes on as without a tool. Panels, menus and dialogs keep their input in both modes. A pick answers the surface
// under a ray: point, normal, part, UV and bone. Every callback runs on the main thread; a plugin's callbacks run inside its
// fault wrapper.
using System;
using UnityEngine;

namespace HS2Next.Api.Unity
{
    /// <summary>Starts and ends viewport tools and answers surface picks. Main thread only. Implemented by the host.</summary>
    [Experimental("the viewport tools may change in any minor release")]
    public interface IViewportTools
    {
        /// <summary>
        /// Starts <paramref name="tool"/>: a tool that runs ends first (<see cref="ToolEnd.Replaced"/>), then
        /// <see cref="ViewportTool.Started"/> runs. False when the host refuses now (a dialog is open, for example): the tool that
        /// runs stays and nothing is called on <paramref name="tool"/>. ArgumentNullException for a null tool.
        /// </summary>
        /// <param name="tool">The tool to start.</param>
        /// <param name="options">How it takes input; the default keeps what it does not claim from the camera and the shortcuts.</param>
        bool Begin(ViewportTool tool, ToolOptions options = default);
        /// <summary>
        /// Ends <paramref name="tool"/> with <see cref="ToolEnd.Finished"/>; nothing when it does not run. A tool may call this from
        /// its own callbacks.
        /// </summary>
        void End(ViewportTool tool);
        /// <summary>True while a tool runs, of this plugin or of anyone else.</summary>
        bool Running { get; }
        /// <summary>
        /// The surface under <paramref name="ray"/>, at any time, with or without a tool. Never null: <see cref="ToolPick.Hit"/> is
        /// false when the ray meets nothing the filter allows. Hidden renderers are not hit. A pick on a character takes a few
        /// milliseconds: fine on a press and during one, too slow for every hover.
        /// </summary>
        /// <param name="ray">The ray in world space.</param>
        /// <param name="filter">What may be hit; the default is every node and no map.</param>
        ToolPick Pick(Ray ray, ToolPickFilter filter = default);
    }

    /// <summary>How a tool takes input (<see cref="IViewportTools.Begin"/>).</summary>
    [Experimental("the viewport tools may change in any minor release")]
    public struct ToolOptions
    {
        /// <summary>
        /// True: input the tool does not claim goes on as without a tool (a press drags the camera, a key runs its shortcut, a click
        /// selects). False: that input does nothing while the tool runs.
        /// </summary>
        public bool PassThrough;
        /// <summary>What <see cref="ToolPointer.Hit"/> may hit. A tool with <see cref="ToolPickFilter.NodeId"/> set ends when that node is deleted.</summary>
        public ToolPickFilter Filter;
        /// <summary>A localisation key (a plugin's: of its own strings) shown in the status line while the tool runs, or null.</summary>
        public string Hint;
    }

    /// <summary>What a pick may hit.</summary>
    [Experimental("the viewport tools may change in any minor release")]
    public struct ToolPickFilter
    {
        /// <summary>Only this scene node; null: every node.</summary>
        public string NodeId;
        /// <summary>Only characters, with what they wear.</summary>
        public bool CharactersOnly;
        /// <summary>When no node is hit, the map's surface along the ray (<see cref="ToolPick.OnMap"/>).</summary>
        public bool MapSurface;
    }

    /// <summary>
    /// A tool of the viewport. Derive from it and override what the tool needs: nothing is abstract, and a later minor release may
    /// add virtual methods, never an abstract one. The host calls <see cref="Started"/> first and <see cref="Ended"/> last.
    /// </summary>
    [Experimental("the viewport tools may change in any minor release")]
    public abstract class ViewportTool
    {
        /// <summary>The tool runs: input comes from now on.</summary>
        public virtual void Started() { }
        /// <summary>
        /// A button went down over the viewport, with no press of the tool held. True: this press is the tool's and the pointer stays
        /// with the tool until <see cref="PointerUp"/> or <see cref="PointerCancel"/>; another button pressed meanwhile only shows in
        /// <see cref="ToolPointer.Buttons"/>. False: the press is not the tool's, and it gets no call for that press.
        /// </summary>
        public virtual bool PointerDown(in ToolPointer p) => false;
        /// <summary>The pointer moved: during a press the tool claimed, or over the viewport with no button held (hover).</summary>
        public virtual void PointerMove(in ToolPointer p) { }
        /// <summary>The button of the claimed press went up: the press is over.</summary>
        public virtual void PointerUp(in ToolPointer p) { }
        /// <summary>
        /// A claimed press ended without a release (Esc, the window lost focus, the pointer was taken): take back what the press did.
        /// The tool goes on running.
        /// </summary>
        public virtual void PointerCancel() { }
        /// <summary>The wheel turned over the viewport. True: the tool took it.</summary>
        /// <param name="notches">Notches turned, positive away from the user.</param>
        /// <param name="p">The pointer.</param>
        public virtual bool Wheel(float notches, in ToolPointer p) => false;
        /// <summary>
        /// A key went down. Esc does not come here (with a press held it cancels the press, else it ends the tool), nor do the repeats
        /// of a held key, nor keys that go to a text field or an open dialog. True: the tool took it.
        /// </summary>
        /// <param name="key">The name the key map uses: <c>Space</c>, <c>T</c>, <c>F1</c>, <c>Alpha1</c>.</param>
        /// <param name="mods">The modifiers held.</param>
        public virtual bool Key(string key, ToolMods mods) => false;
        /// <summary>
        /// Once per frame of the view while the tool runs: what it draws shows in that frame of the view, never in a capture or a
        /// recording. <paramref name="draw"/> is valid during this call only.
        /// </summary>
        public virtual void Draw(IToolDraw draw) { }
        /// <summary>The tool no longer runs: the last call. A press still held ends with it, with no other call.</summary>
        public virtual void Ended(ToolEnd reason) { }
    }

    /// <summary>The pointer at one event. Read <see cref="Hit"/> during the call it comes with.</summary>
    [Experimental("the viewport tools may change in any minor release")]
    public readonly struct ToolPointer
    {
        readonly LazyPick hit;

        /// <summary>A pointer event, for the host and for tests.</summary>
        /// <param name="panel">The position in the panel, in panel units.</param>
        /// <param name="ray">The ray through it, in world space.</param>
        /// <param name="button">The button that changed, or -1.</param>
        /// <param name="buttons">The buttons held now.</param>
        /// <param name="mods">The modifiers held.</param>
        /// <param name="hit">Answers <see cref="Hit"/>, at most once, on its first read; null: a miss.</param>
        public ToolPointer(Vector2 panel, Ray ray, int button, int buttons, ToolMods mods, Func<ToolPick> hit)
        {
            Panel = panel;
            Ray = ray;
            Button = button;
            Buttons = buttons;
            Mods = mods;
            this.hit = hit != null ? new LazyPick(hit) : null;
        }

        /// <summary>The position in the panel, in panel units.</summary>
        public Vector2 Panel { get; }
        /// <summary>The ray through the pointer, in world space.</summary>
        public Ray Ray { get; }
        /// <summary>The button of this event: 0 left, 1 right, 2 middle; -1 on a move.</summary>
        public int Button { get; }
        /// <summary>The buttons held now, as bits: 1 left, 2 right, 4 middle.</summary>
        public int Buttons { get; }
        /// <summary>The modifiers held.</summary>
        public ToolMods Mods { get; }
        /// <summary>
        /// The surface under the pointer by the tool's filter. The pick runs on the first read, so a tool that does not read this pays
        /// nothing, and later reads give the same object. Never null: <see cref="ToolPick.Hit"/> is false on a miss.
        /// </summary>
        public ToolPick Hit => hit != null ? hit.Value : new ToolPick();

        sealed class LazyPick
        {
            Func<ToolPick> source;
            ToolPick value;

            public LazyPick(Func<ToolPick> source) { this.source = source; }

            public ToolPick Value
            {
                get
                {
                    if (source != null)
                    {
                        var f = source;
                        source = null;
                        value = f();
                    }
                    return value ?? (value = new ToolPick());
                }
            }
        }
    }

    /// <summary>What a pick found. On a miss every member keeps its default.</summary>
    [Experimental("the viewport tools may change in any minor release")]
    public sealed class ToolPick
    {
        /// <summary>True when the ray met something the filter allows.</summary>
        public bool Hit;
        /// <summary>The scene node that owns the surface; null on the map.</summary>
        public string NodeId;
        /// <summary>The point, in world space.</summary>
        public Vector3 Point;
        /// <summary>The surface's unit normal at the point, in world space, towards the ray; zero when the answer has no triangle.</summary>
        public Vector3 Normal;
        /// <summary>The distance along the ray.</summary>
        public float Distance;
        /// <summary>The renderer hit; null on the map.</summary>
        public Renderer Renderer;
        /// <summary>The renderer's address on its character (the text a part's address uses in the scene file); null elsewhere.</summary>
        public string Part;
        /// <summary>The material slot of the triangle.</summary>
        public int Slot;
        /// <summary>The first UV set at the point.</summary>
        public Vector2 Uv;
        /// <summary>The triangle's index in the renderer's mesh.</summary>
        public int Triangle;
        /// <summary>The bone with the largest weight at the triangle's nearest vertex; null on a part with no bones.</summary>
        public Transform Bone;
        /// <summary>The point in the space of <see cref="Bone"/>.</summary>
        public Vector3 BoneLocal;
        /// <summary>The address of <see cref="Bone"/> (the text a bone's address uses in the scene file); null with no bone.</summary>
        public string BoneAddress;
        /// <summary>True when this is the map's surface: only the point, the normal and the distance are set.</summary>
        public bool OnMap;
        /// <summary>
        /// True when the answer comes from a triangle with its first UV set. False on a mesh that cannot be read, or a part with no UV:
        /// the point and the distance hold and the triangle's members do not.
        /// </summary>
        public bool Precise;
    }

    /// <summary>Why a tool ended.</summary>
    [Experimental("the viewport tools may change in any minor release")]
    public enum ToolEnd
    {
        /// <summary><see cref="IViewportTools.End"/> was called.</summary>
        Finished = 0,
        /// <summary>Esc with no press held, or the tool threw.</summary>
        Cancelled = 1,
        /// <summary>Another tool began.</summary>
        Replaced = 2,
        /// <summary>A new scene or a load, the deletion of the node its filter names, or the host left the scene's view.</summary>
        SceneChanged = 3,
        /// <summary>The UI closed, or the plugin that began the tool unloaded, was disabled or faulted (after its own Unload).</summary>
        UiClosed = 4,
    }

    /// <summary>The modifier keys held.</summary>
    [Flags]
    [Experimental("the viewport tools may change in any minor release")]
    public enum ToolMods
    {
        /// <summary>No modifier.</summary>
        None = 0,
        /// <summary>Ctrl.</summary>
        Ctrl = 1,
        /// <summary>Shift.</summary>
        Shift = 2,
        /// <summary>Alt.</summary>
        Alt = 4,
    }

    /// <summary>Draws over the view for one frame, in world space. Valid during <see cref="ViewportTool.Draw"/> only. Implemented by the host.</summary>
    [Experimental("the viewport tools may change in any minor release")]
    public interface IToolDraw
    {
        /// <summary>A line from <paramref name="a"/> to <paramref name="b"/>.</summary>
        /// <param name="a">One end, in world space.</param>
        /// <param name="b">The other end, in world space.</param>
        /// <param name="c">The colour.</param>
        void Line(Vector3 a, Vector3 b, Color c);
        /// <summary>A cross at <paramref name="p"/>.</summary>
        /// <param name="p">The centre, in world space.</param>
        /// <param name="size">The length of each arm, in world units.</param>
        /// <param name="c">The colour.</param>
        void Marker(Vector3 p, float size, Color c);
        /// <summary>A text at <paramref name="p"/>, drawn as it is.</summary>
        /// <param name="p">The anchor, in world space.</param>
        /// <param name="text">The text.</param>
        void Label(Vector3 p, string text);
    }
}

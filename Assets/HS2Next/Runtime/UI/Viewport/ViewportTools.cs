// The viewport tools, host side: one ToolHost per UiContext (ctx.Tools). While a tool runs, ViewportHost and ShortcutRouter ask
// this class first; nothing is written to the camera, the selection or the gizmos, so ending a tool restores everything by doing nothing. With no tool the
// routes read Running and go on as they always did.
//   default mode    the camera, the wheel, the shortcuts, the held keys, the gizmo and the selection rest; handles are hidden (Blocks)
//   PassThrough     what the tool does not claim goes on as without a tool; a claimed press keeps the gizmo out
//   a press         PointerDown decides on the first button only. Claimed: moves and the release of that button go to the tool, another button shows in
//                   Buttons. Not claimed: no call for that press until it ends; a hover is a move with no button held
//   keys            Esc never reaches Key (the router's order: popup, drag edit, tool), repeats neither: a repeat of a claimed key is swallowed; a key that
//                   goes to a text field, an IME composition or an open dialog never; a claimed key does not run a held action
//   ending          Finished by End, Cancelled by Esc with no press or a tool's exception, Replaced by another Begin, SceneChanged by a new or loaded scene,
//                   the deletion of the node the filter names and the Edit tab of the character inspector coming into view, UiClosed when the UI closes.
//                   Ended is the last call: a press still held ends with it, with no other call, and the pointer is let go
//   Begin refused   while a dialog is open or the Edit tab is in view: the tool that runs stays, nothing is called on the new one (Refusal says why)
//   drawing         IToolDraw records into the gizmo pass of the view camera (never in a capture); labels are UI elements over the view. ViewDraw takes what a
//                   panel shows with no tool running, in the same way
// A tool may end itself or begin another from any of its callbacks: every call reads the running tool anew afterwards. Tools run on the main thread.
using System;
using System.Collections.Generic;
using HS2Next.Api.Unity;
using HS2Next.Scenes;
using HS2Next.UI.Core;
using HS2Next.UI.Gizmos;
using HS2Next.UI.Panels;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Viewport
{
    public sealed class ToolHost : IViewportTools, IToolDraw
    {
        /// <summary>Why a Begin was refused: <see cref="Refusal"/>, localisation keys.</summary>
        public const string RefusedDialog = "tool.refused.dialog", RefusedEdit = "tool.refused.edit";
        const int MaxDraws = 4096;

        readonly UiContext ctx;
        readonly HashSet<string> keysHeld = new HashSet<string>(StringComparer.Ordinal);
        readonly List<Label> labels = new List<Label>();
        ViewportTool tool;
        ToolOptions options;
        bool pressHeld, replacing, subscribed;
        int pressButton;
        // the draw of one frame
        GizmoPass pass;
        Camera drawCam;
        int drawn, labelsUsed;

        internal ToolHost(UiContext ctx) { this.ctx = ctx; }

        /// <summary>The tool that runs, or null.</summary>
        public ViewportTool Current => tool;
        public bool Running => tool != null;
        /// <summary>True while a tool runs in the default mode: the camera, shortcuts, gizmos and selection rest.</summary>
        public bool Blocks => tool != null && !options.PassThrough;
        /// <summary>True while the tool holds a press it claimed.</summary>
        public bool PressHeld => pressHeld;
        /// <summary>The button of the held press.</summary>
        public int PressButton => pressButton;
        /// <summary>Why the last refused Begin was refused: a localisation key; null after a Begin that went through.</summary>
        public string Refusal { get; private set; }
        /// <summary>The running tool's hint for the status line, or null.</summary>
        public string HintText => tool != null && !string.IsNullOrEmpty(options.Hint) ? ctx.L10n.Get(options.Hint) : null;
        /// <summary>What shows in the view beside a tool, with or without one: called once per frame of the view camera, never for a capture.</summary>
        public event Action<IToolDraw> ViewDraw;

        // ------------------------------------------------------------ IViewportTools

        public bool Begin(ViewportTool t, ToolOptions o = default)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));
            if (replacing) return false;   // from an Ended(Replaced): the tool that replaces it has the view
            string why = RefusalNow();
            if (why != null) { Refusal = why; return false; }
            Refusal = null;
            if (tool != null)
            {
                replacing = true;
                try { Stop(ToolEnd.Replaced); }
                finally { replacing = false; }
            }
            tool = t;
            options = o;
            Subscribe(true);
            if (Blocks) ctx.Viewport?.ToolBegan();
            Announce();
            try { t.Started(); }
            catch (Exception e) { Fault(t, nameof(ViewportTool.Started), e); }
            return true;
        }

        public void End(ViewportTool t)
        {
            if (t != null && ReferenceEquals(t, tool)) Stop(ToolEnd.Finished);
        }

        public ToolPick Pick(Ray ray, ToolPickFilter filter = default) => ctx.Viewport?.Picker?.PickSurface(ray, filter) ?? new ToolPick();

        string RefusalNow()
        {
            if (ctx.Dialogs != null && ctx.Dialogs.IsModalOpen) return RefusedDialog;
            return CharaEditTab.Current != null ? RefusedEdit : null;
        }

        // ------------------------------------------------------------ ending

        void Stop(ToolEnd reason)
        {
            var t = tool;
            if (t == null) return;
            bool held = pressHeld;
            tool = null;
            options = default;
            pressHeld = false;
            keysHeld.Clear();
            Subscribe(false);
            if (held) ctx.Viewport?.ToolPressEnded();
            HideLabels(0);
            Announce();
            try { t.Ended(reason); }
            catch (Exception e) { Log(nameof(ViewportTool.Ended), e); }
        }

        void Fault(ViewportTool t, string call, Exception e)
        {
            Log(call, e);
            if (ReferenceEquals(t, tool)) Stop(ToolEnd.Cancelled);
        }

        static void Log(string call, Exception e) => Debug.LogError("[ui] a viewport tool threw in " + call + ": " + e);

        void Announce() => ctx.Shell?.StatusBar?.Refresh();

        /// <summary>The UI closes.</summary>
        public void Close() => Stop(ToolEnd.UiClosed);

        void Subscribe(bool on)
        {
            if (on == subscribed) return;
            subscribed = on;
            if (on)
            {
                ctx.SceneCleared += OnScene;
                ctx.SceneLoaded += OnLoaded;
                ctx.SceneChanged += OnChanged;
                Application.focusChanged += FocusChanged;
            }
            else
            {
                ctx.SceneCleared -= OnScene;
                ctx.SceneLoaded -= OnLoaded;
                ctx.SceneChanged -= OnChanged;
                Application.focusChanged -= FocusChanged;
            }
        }

        void OnScene() => Stop(ToolEnd.SceneChanged);

        void OnLoaded(SceneLoadedArgs a) => Stop(ToolEnd.SceneChanged);

        void OnChanged(SceneChangeBatch b)
        {
            string id = options.Filter.NodeId;
            if (id != null && ctx.Model.Find(id) == null) Stop(ToolEnd.SceneChanged);
        }

        /// <summary>The window's focus changed (Application.focusChanged): a press held does not survive losing it.</summary>
        public void FocusChanged(bool focused)
        {
            if (!focused) CancelPress();
        }

        /// <summary>Per frame while a tool runs: the Edit tab came into view.</summary>
        internal void Tick()
        {
            if (CharaEditTab.Current != null) Stop(ToolEnd.SceneChanged);
        }

        /// <summary>Esc with no press held: the tool ends with Cancelled. True when one ran.</summary>
        internal bool Cancel()
        {
            if (tool == null) return false;
            Stop(ToolEnd.Cancelled);
            return true;
        }

        // ------------------------------------------------------------ pointer

        ToolPointer Pointer(Vector2 panel, int button, int buttons, EventModifiers mods)
        {
            var ray = ctx.Viewport.RayAt(panel);
            var filter = options.Filter;
            return new ToolPointer(panel, ray, button, buttons, (ToolMods)HS2Next.UI.Input.ShortcutRouter.Mods(mods), () => Pick(ray, filter));
        }

        /// <summary>The first button of a press over the viewport. True when the tool took it; <see cref="PressHeld"/> says whether it still holds it.</summary>
        internal bool Press(Vector2 panel, int button, int buttons, EventModifiers mods)
        {
            var t = tool;
            var p = Pointer(panel, button, buttons, mods);
            bool took;
            try { took = t.PointerDown(in p); }
            catch (Exception e) { Fault(t, nameof(ViewportTool.PointerDown), e); return false; }
            if (took && ReferenceEquals(t, tool))
            {
                pressHeld = true;
                pressButton = button;
            }
            return took;
        }

        /// <summary>A move during the held press, or a hover.</summary>
        internal void Move(Vector2 panel, int buttons, EventModifiers mods)
        {
            var t = tool;
            var p = Pointer(panel, -1, buttons, mods);
            try { t.PointerMove(in p); }
            catch (Exception e) { Fault(t, nameof(ViewportTool.PointerMove), e); }
        }

        /// <summary>The held press's button went up.</summary>
        internal void Release(Vector2 panel, int buttons, EventModifiers mods)
        {
            var t = tool;
            pressHeld = false;
            var p = Pointer(panel, pressButton, buttons, mods);
            try { t.PointerUp(in p); }
            catch (Exception e) { Fault(t, nameof(ViewportTool.PointerUp), e); }
        }

        /// <summary>The held press ends without a release; the tool goes on. True when one was held.</summary>
        public bool CancelPress()
        {
            if (!pressHeld) return false;
            var t = tool;
            pressHeld = false;
            ctx.Viewport?.ToolPressEnded();
            try { t.PointerCancel(); }
            catch (Exception e) { Fault(t, nameof(ViewportTool.PointerCancel), e); }
            return true;
        }

        internal bool Wheel(float notches, Vector2 panel, int buttons, EventModifiers mods)
        {
            var t = tool;
            var p = Pointer(panel, -1, buttons, mods);
            try { return t.Wheel(notches, in p); }
            catch (Exception e) { Fault(t, nameof(ViewportTool.Wheel), e); return false; }
        }

        // ------------------------------------------------------------ keys

        /// <summary>A key went down (no repeat, no Esc). True when the tool took it.</summary>
        internal bool Key(string key, KeyMods mods)
        {
            var t = tool;
            bool took;
            try { took = t.Key(key, (ToolMods)mods); }
            catch (Exception e) { Fault(t, nameof(ViewportTool.Key), e); return false; }
            if (took && ReferenceEquals(t, tool)) keysHeld.Add(key);
            return took;
        }

        /// <summary>The tool took this key and it is still down.</summary>
        internal bool Holds(string key) => keysHeld.Contains(key);

        internal void KeyUp(string key) => keysHeld.Remove(key);

        /// <summary>Takes the keys the tool holds out of a list of keys down.</summary>
        internal void LeaveHeld(List<string> down)
        {
            if (keysHeld.Count > 0) down.RemoveAll(keysHeld.Contains);
        }

        /// <summary>No key is down (a key released while the window had no focus sends no key up).</summary>
        internal void ForgetKeys() => keysHeld.Clear();

        // ------------------------------------------------------------ drawing

        /// <summary>Fills the gizmo pass with what the tool and <see cref="ViewDraw"/> show in this frame of the view.</summary>
        internal void Draw(GizmoPass into, Camera cam)
        {
            var t = tool;
            if (t == null && ViewDraw == null) return;
            pass = into;
            drawCam = cam;
            drawn = labelsUsed = 0;
            try
            {
                if (t != null)
                {
                    try { t.Draw(this); }
                    catch (Exception e) { Fault(t, nameof(ViewportTool.Draw), e); }
                }
                ViewDraw?.Invoke(this);
            }
            finally
            {
                pass = null;
                drawCam = null;
                LastDrawn = drawn;
                HideLabels(labelsUsed);
            }
        }

        /// <summary>Items drawn in the last frame of the view (tests).</summary>
        public int LastDrawn { get; private set; }

        void IToolDraw.Line(Vector3 a, Vector3 b, Color c) => Line(a, b, c);

        void Line(Vector3 a, Vector3 b, Color c)
        {
            if (pass == null || drawn >= MaxDraws) return;
            var d = b - a;
            float len = d.magnitude;
            if (len <= 0) return;
            pass.Add(new GizmoDraw { Mesh = GizmoMeshes.Line, Matrix = Matrix4x4.TRS(a, Quaternion.LookRotation(d), new Vector3(1, 1, len)), Color = c });
            drawn++;
        }

        void IToolDraw.Marker(Vector3 p, float size, Color c)
        {
            Line(p - Vector3.right * size, p + Vector3.right * size, c);
            Line(p - Vector3.up * size, p + Vector3.up * size, c);
            Line(p - Vector3.forward * size, p + Vector3.forward * size, c);
        }

        void IToolDraw.Label(Vector3 p, string text)
        {
            var vp = ctx.Viewport?.Element;
            if (pass == null || vp == null || !drawCam) return;
            var sp = drawCam.WorldToScreenPoint(p);
            if (sp.z <= 0) return;
            if (labelsUsed == labels.Count) labels.Add(NewLabel(labelsUsed));
            var l = labels[labelsUsed++];
            if (l.parent != vp) vp.Add(l);
            float s = ctx.Viewport.PanelScale;
            var origin = vp.worldBound.position;
            l.text = text;
            l.style.left = sp.x / s - origin.x + 6;
            l.style.top = (Screen.height - sp.y) / s - origin.y - 16;
            l.style.display = DisplayStyle.Flex;
        }

        static Label NewLabel(int i)
        {
            var l = new Label { name = "tool-label-" + i, pickingMode = PickingMode.Ignore };
            var s = l.style;
            s.position = Position.Absolute;
            s.fontSize = 11;
            s.color = Color.white;
            s.paddingLeft = s.paddingRight = 2;
            s.backgroundColor = new Color(0f, 0f, 0f, 0.45f);
            return l;
        }

        void HideLabels(int from)
        {
            for (int i = from; i < labels.Count; i++) labels[i].style.display = DisplayStyle.None;
        }
    }
}

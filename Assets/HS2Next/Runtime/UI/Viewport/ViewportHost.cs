// The viewport (spike S1d / S3a): the transparent, focusable centre element of the layout.
// V1 composition: on GeometryChangedEvent (and every frame, cheaply) the view camera's rect = the element's world bound
// converted panel → screen (× panel scale, y flipped, ÷ screen size); a full-screen clear camera (depth below the view
// camera, culling mask 0, solid --hs-bg-0, no post / shadows / AA) clears the pixels outside the rect that no opaque
// element covers (S3a). The element stops NavigationMoveEvent (arrow keys would move the focus away, S1d). Pointer
// routing: (1) gizmo handle → gizmo drag with pointer capture; (2) otherwise a pending click that becomes a
// camera drag after 4 logical px or stays a click (pick on pointer up). Wheel → camera dolly / Ctrl+wheel FOV. Overlays
// (absolute children): view info at the top right — the field of view readout ("FOV 23°"; its tooltip names the keys of
// the active key map; a click opens a small popup with the field-of-view slider, the wheel over it changes the value, a
// double click resets it to 23; it edits the free camera, or the camera object the view looks through), the active camera
// line and "Release".
using System;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Gizmos;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace HS2Next.UI.Viewport
{
    public sealed class ViewportHost
    {
        public const float ClickSlop = 4f;
        readonly UiContext ctx;
        public VisualElement Element { get; }
        /// <summary>The active camera line ("Camera: <name>"; empty for the free camera).</summary>
        public Label Info { get; }
        /// <summary>The field of view readout (click: the popup slider, wheel: change, double click: reset).</summary>
        public Label FovLabel { get; }
        /// <summary>The field-of-view popup while it is open (tests), else null.</summary>
        public VisualElement FovPopup { get; private set; }
        VisualElement fovBlocker, fovKeyRoot;
        EventCallback<KeyDownEvent> fovKeys;
        // a run of wheel steps on a camera object: one UI edit, ended by a pause, the pointer leaving the readout, a key or a click
        public const int WheelRunIdleMs = 600;
        UiEdit wheelRun;
        VisualElement wheelRunRoot;
        IVisualElementScheduledItem wheelRunEnd;
        readonly EventCallback<PointerDownEvent> wheelRunDown;
        readonly EventCallback<KeyDownEvent> wheelRunKey;
        /// <summary>A run of wheel steps over the readout is open (its entry is written when it ends; tests).</summary>
        public bool WheelRunOpen => wheelRun != null && wheelRun.IsOpen;
        public Button ReleaseButton { get; }
        public CameraController Camera { get; }
        public ViewportPicker Picker { get; }
        public TransformTool Gizmo { get; internal set; }
        Camera clearCam;
        /// <summary>Pointer inside the viewport element (from enter / leave; checked with panel.Pick each frame).</summary>
        public bool PointerOver { get; internal set; }
        /// <summary>Rect writes so far (tests).</summary>
        public int RectSyncs { get; private set; }

        // pointer state
        int downButtonMask;
        Vector2 downPos, lastPos;
        bool pending, cameraDrag;
        int capturedPointer = -1;

        public ViewportHost(UiContext ctx)
        {
            this.ctx = ctx;
            Element = new VisualElement { name = "viewport", focusable = true, pickingMode = PickingMode.Position };
            Element.AddToClassList("hs-viewport");
            Element.tabIndex = 0;
            Element.RegisterCallback<GeometryChangedEvent>(_ => SyncRect());
            Element.RegisterCallback<NavigationMoveEvent>(e =>
            {
                e.StopPropagation();
                Element.panel?.focusController?.IgnoreEvent(e);
            });
            Element.RegisterCallback<PointerEnterEvent>(_ => PointerOver = true);
            Element.RegisterCallback<PointerLeaveEvent>(_ => { if (capturedPointer < 0) PointerOver = false; });
            Element.RegisterCallback<PointerDownEvent>(OnDown);
            Element.RegisterCallback<PointerMoveEvent>(OnMove);
            Element.RegisterCallback<PointerUpEvent>(OnUp);
            Element.RegisterCallback<PointerCaptureOutEvent>(_ => OnCaptureLost());
            Element.RegisterCallback<WheelEvent>(OnWheel);
            // the top-right box: the field of view readout, the active camera line, «Release»
            var infoBox = new VisualElement { name = "viewport-info-box", pickingMode = PickingMode.Ignore };
            infoBox.AddToClassList("hs-viewport__info");
            infoBox.style.alignItems = Align.FlexEnd;
            Element.Add(infoBox);
            FovLabel = new Label { name = "viewport-fov", pickingMode = PickingMode.Position };
            FovLabel.AddToClassList("hs-viewport__fov");
            FovLabel.RegisterCallback<PointerDownEvent>(OnFovDown);
            FovLabel.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
            FovLabel.RegisterCallback<WheelEvent>(OnFovWheel);
            FovLabel.RegisterCallback<PointerLeaveEvent>(_ => EndWheelRun());
            wheelRunDown = _ => EndWheelRun();
            wheelRunKey = _ => EndWheelRun();
            infoBox.Add(FovLabel);
            Info = new Label { name = "viewport-info", pickingMode = PickingMode.Ignore };
            infoBox.Add(Info);
            ReleaseButton = new Button(() => ctx.Registry.Run(ActionIds.CameraRelease, "viewport")) { name = "viewport-release" };
            ReleaseButton.AddToClassList("hs-viewport__release");
            ReleaseButton.style.marginTop = 2;
            Loc.Bind(ReleaseButton, "viewport.release");
            infoBox.Add(ReleaseButton);
            Camera = new CameraController(ctx, this);
            Picker = new ViewportPicker(ctx);
        }

        public Camera ViewCamera => ctx.Runtime?.GetPart<SceneViewCamera>()?.Camera;

        public float PanelScale => Element.panel != null ? Element.panel.scaledPixelsPerPoint : 1f;

        /// <summary>The camera rect the element asks for (normalised, y up).</summary>
        public Rect ExpectedRect()
        {
            if (Element.panel == null) return new Rect(0, 0, 1, 1);
            var wb = Element.worldBound;
            float s = PanelScale, sw = Screen.width, sh = Screen.height;
            if (wb.width <= 0 || wb.height <= 0 || sw <= 0 || sh <= 0) return new Rect(0, 0, 1, 1);
            return new Rect(wb.x * s / sw, 1f - wb.yMax * s / sh, wb.width * s / sw, wb.height * s / sh);
        }

        public void SyncRect()
        {
            var cam = ViewCamera;
            if (!cam) return;
            var r = ExpectedRect();
            if (r != cam.rect) { cam.rect = r; RectSyncs++; }
            EnsureClearCamera(cam);
        }

        void EnsureClearCamera(Camera view)
        {
            if (clearCam) { clearCam.depth = view.depth - 1; return; }
            var go = new GameObject("UI Clear Camera");
            go.transform.SetParent(ctx.Host ? ctx.Host.transform : null, false);
            clearCam = go.AddComponent<Camera>();
            clearCam.clearFlags = CameraClearFlags.SolidColor;
            clearCam.backgroundColor = new Color32(24, 25, 29, 255);   // --hs-bg-0
            clearCam.cullingMask = 0;
            clearCam.depth = view.depth - 1;
            clearCam.rect = new Rect(0, 0, 1, 1);
            clearCam.allowHDR = false;
            clearCam.allowMSAA = false;
            var d = clearCam.GetUniversalAdditionalCameraData();
            d.renderPostProcessing = false;
            d.renderShadows = false;
            d.antialiasing = AntialiasingMode.None;
            d.requiresColorOption = CameraOverrideOption.Off;
            d.requiresDepthOption = CameraOverrideOption.Off;
        }

        /// <summary>Screen position (pixels, y up) of a panel point.</summary>
        public Vector2 PanelToScreen(Vector2 p)
        {
            float s = PanelScale;
            return new Vector2(p.x * s, Screen.height - p.y * s);
        }

        /// <summary>Panel point of a screen position (pixels, y up). ScreenToPanel divides by the scale and does not flip y (S1d).</summary>
        public Vector2 ScreenToPanel(Vector2 screen)
        {
            if (Element.panel == null) return screen;
            return RuntimePanelUtils.ScreenToPanel(Element.panel, new Vector2(screen.x, Screen.height - screen.y));
        }

        /// <summary>A pick ray through a panel point.</summary>
        public Ray RayAt(Vector2 panelPoint)
        {
            var cam = ViewCamera;
            return cam ? cam.ScreenPointToRay(PanelToScreen(panelPoint)) : new Ray();
        }

        // ---------------- pointer routing

        void OnDown(PointerDownEvent e)
        {
            Element.Focus();
            int bit = 1 << e.button;
            bool first = downButtonMask == 0;
            downButtonMask |= bit;
            lastPos = e.position;
            if (ctx.Tools.Running && ToolDown(e, first)) return;   // Phase 6.6
            if (first)
            {
                downPos = e.position;
                if (e.button == 0 && Gizmo != null && Gizmo.TryBegin(e.position, e.modifiers))
                {
                    Capture(e.pointerId);
                    e.StopPropagation();
                    return;
                }
                if (e.button == 0 && e.clickCount == 2) { Camera.FrameAt(e.position); downButtonMask = 0; return; }
                pending = true;
                cameraDrag = false;
                Capture(e.pointerId);
            }
            else if (pending || cameraDrag) { cameraDrag = true; pending = false; Camera.BeginDrag(downButtonMask, e.modifiers); }
            e.StopPropagation();
        }

        /// <summary>A press while a tool runs: true when the route ends here. A press the default mode leaves alone keeps the pointer until its release, which does nothing.</summary>
        bool ToolDown(PointerDownEvent e, bool first)
        {
            var tools = ctx.Tools;
            if (tools.PressHeld) { e.StopPropagation(); return true; }   // it shows in Buttons
            if (!first) return false;
            bool blocks = tools.Blocks;
            downPos = e.position;
            if (!tools.Press(e.position, e.button, downButtonMask, e.modifiers) && !blocks) return false;
            pending = cameraDrag = false;
            Capture(e.pointerId);
            e.StopPropagation();
            return true;
        }

        void Capture(int id)
        {
            capturedPointer = id;
            Element.CapturePointer(id);
        }

        void OnMove(PointerMoveEvent e)
        {
            Vector2 p = e.position;
            Vector2 delta = p - lastPos;
            lastPos = p;
            if (Gizmo != null && Gizmo.Dragging) { Gizmo.Drag(p, e.modifiers); return; }
            if (ctx.Tools.Running && ToolMove(e)) return;   // Phase 6.6
            if (downButtonMask == 0) { Gizmo?.Hover(p); return; }
            if (pending && (p - downPos).magnitude >= ClickSlop)
            {
                pending = false;
                cameraDrag = true;
                Camera.BeginDrag(downButtonMask, e.modifiers);
                delta = p - downPos;
            }
            if (cameraDrag) Camera.Drag(downButtonMask, delta, e.modifiers);
        }

        /// <summary>A move while a tool runs: the held press's, or a hover with no button down (a cancelled press leaves its button down). True when the route ends here.</summary>
        bool ToolMove(PointerMoveEvent e)
        {
            var tools = ctx.Tools;
            if (tools.PressHeld) { tools.Move(e.position, downButtonMask, e.modifiers); return true; }
            if (downButtonMask == 0 && e.pressedButtons == 0) tools.Move(e.position, 0, e.modifiers);
            return tools.Blocks;
        }

        void OnUp(PointerUpEvent e)
        {
            int bit = 1 << e.button;
            if (ctx.Tools.PressHeld)   // Phase 6.6
            {
                downButtonMask &= ~bit;
                if (e.button == ctx.Tools.PressButton) ctx.Tools.Release(e.position, downButtonMask, e.modifiers);
                if (downButtonMask == 0) Release();
                return;
            }
            if (Gizmo != null && Gizmo.Dragging)
            {
                Gizmo.End();
                downButtonMask = 0;
                Release();
                return;
            }
            downButtonMask &= ~bit;
            if (pending && e.button == 0) Picker.Click(this, e.position, e.modifiers);
            else if (pending && e.button == 1) ctx.Host?.ShowViewportMenu(e.position);
            if (downButtonMask == 0)
            {
                if (cameraDrag) Camera.EndDrag();
                pending = cameraDrag = false;
                Release();
            }
        }

        void Release()
        {
            if (capturedPointer >= 0 && Element.HasPointerCapture(capturedPointer)) Element.ReleasePointer(capturedPointer);
            capturedPointer = -1;
        }

        /// <summary>A tool that takes the view began: a click or a camera drag under way ends here.</summary>
        internal void ToolBegan()
        {
            if (cameraDrag) Camera.EndDrag();
            pending = cameraDrag = false;
        }

        /// <summary>The press a tool held ended without its release: the pointer is let go.</summary>
        internal void ToolPressEnded()
        {
            downButtonMask = 0;
            Release();
        }

        void OnCaptureLost()
        {
            if (ctx.Tools.PressHeld)   // Phase 6.6: the end of an earlier capture can arrive during the next press
            {
                if (capturedPointer >= 0 && Element.HasPointerCapture(capturedPointer)) return;
                ctx.Tools.CancelPress();
            }
            capturedPointer = -1;
            if (Gizmo != null && Gizmo.Dragging) Gizmo.Cancel();
            if (cameraDrag) Camera.EndDrag();
            pending = cameraDrag = false;
            downButtonMask = 0;
        }

        /// <summary>Esc during a gizmo drag: cancel (revert). True when something was cancelled.</summary>
        public bool CancelDrag()
        {
            if (ctx.Tools.CancelPress()) return true;   // Phase 6.6: a tool's press
            if (Gizmo != null && Gizmo.Dragging) { Gizmo.Cancel(); downButtonMask = 0; Release(); return true; }
            return false;
        }

        void OnWheel(WheelEvent e)
        {
            float notches = -e.delta.y / 3f;   // UI Toolkit wheel delta: 3 per notch, down positive
            if (Mathf.Abs(e.delta.y) > 0 && Mathf.Abs(e.delta.y) < 1) notches = -Mathf.Sign(e.delta.y);
            var mods = Input.ShortcutRouter.Mods(e.modifiers);
            if (ctx.Tools.Running)   // Phase 6.6
            {
                bool blocks = ctx.Tools.Blocks;
                if (ctx.Tools.Wheel(notches, e.mousePosition, downButtonMask, e.modifiers) || blocks) { e.StopPropagation(); return; }
            }
            var resolver = ctx.Keys.Resolver;
            if (resolver != null && mods != KeyMods.None)
            {
                string key = notches > 0 ? KeyNames.WheelUp : KeyNames.WheelDown;
                var r = resolver.Resolve(ctx.Host.Router.Facts(key, mods, false, Element));
                if (r.Outcome == ShortcutOutcome.Trigger || r.Outcome == ShortcutOutcome.Held)
                {
                    ctx.Registry.RunWith(r.ActionId, new ActionInvocation { Source = "wheel", DeltaTime = Mathf.Abs(notches) * 0.2, Speed = 1 }, out _);
                    e.StopPropagation();
                    return;
                }
            }
            Camera.Wheel(notches);
            e.StopPropagation();
        }

        // ---------------- per frame

        public void Update(float dt)
        {
            SyncRect();
            // the pointer-over flag from a pick (enter / leave miss moves over overlays)
            var panel = Element.panel;
            if (panel != null && capturedPointer < 0)
            {
                var mp = (Vector2)ctx.Host.Router.Source.MousePosition;
                var pp = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(mp.x, Screen.height - mp.y));
                var picked = panel.Pick(pp);
                PointerOver = picked != null && (picked == Element || Element.Contains(picked));
            }
            Camera.Update(dt);
            if (ctx.Tools.Running) ctx.Tools.Tick();   // Phase 6.6
            UpdateInfo();
        }

        float infoAt;
        /// <summary>Refreshes the view info now (else every 0.25 s; tests call it).</summary>
        public void UpdateInfo(bool now = false)
        {
            if (!now && Time.unscaledTime < infoAt) return;
            infoAt = Time.unscaledTime + 0.25f;
            var vc = ctx.Runtime?.GetPart<SceneViewCamera>();
            var cam = vc?.Camera;
            string active = vc?.ActiveCameraId;
            string fov = cam ? ctx.L10n.Format("viewport.fov", ViewFov.Degrees(ViewFov.Current(ctx))) : "";
            if (FovLabel.text != fov) FovLabel.text = fov;
            FovLabel.style.display = cam ? DisplayStyle.Flex : DisplayStyle.None;
            string tip = cam ? ViewFov.Tooltip(ctx, readout: true) : null;
            if (FovLabel.tooltip != tip) FovLabel.tooltip = tip;
            string text = "";
            if (active != null)
            {
                var n = ctx.Model.Find(active);
                text = ctx.L10n.Format("viewport.activeCamera", n != null ? n.Label : active);
            }
            Info.text = text;
            Info.style.display = text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            ReleaseButton.style.display = active != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (FovPopup != null)
            {
                var title = FovPopup.Q<Label>("viewport-fov-popup-title");
                string t = PopupTitle();
                if (title != null && title.text != t) title.text = t;
            }
        }

        // ---------------- the field of view readout and its popup

        void OnFovDown(PointerDownEvent e)
        {
            e.StopPropagation();
            EndWheelRun();
            if (e.button != 0) return;
            if (e.clickCount >= 2) { ViewFov.Reset(ctx); CloseFovPopup(); }
            else ToggleFovPopup();
            UpdateInfo(true);
        }

        void OnFovWheel(WheelEvent e)
        {
            e.StopPropagation();
            FovWheel(e.delta.y);
        }

        /// <summary>A wheel over the readout: up narrows, down widens, <see cref="ViewFov.WheelStep"/> a notch. On a camera
        /// object a run of steps is one entry "Camera": the run ends after <see cref="WheelRunIdleMs"/> ms without a notch, when
        /// the pointer leaves the readout, or at the next key or click anywhere (before that event is handled).</summary>
        public void FovWheel(float deltaY)
        {
            float notches = -deltaY / 3f;   // UI Toolkit wheel delta: 3 per notch, down positive
            if (Mathf.Abs(deltaY) > 0 && Mathf.Abs(deltaY) < 1) notches = -Mathf.Sign(deltaY);
            if (notches == 0) return;
            string target = ViewFov.Target(ctx);
            if (target != null && !WheelRunOpen)
            {
                wheelRun = ctx.BeginEdit(OpLabels.Camera);
                wheelRunRoot = Element.panel?.visualTree;
                wheelRunRoot?.RegisterCallback(wheelRunDown, TrickleDown.TrickleDown);
                wheelRunRoot?.RegisterCallback(wheelRunKey, TrickleDown.TrickleDown);
            }
            ViewFov.Step(ctx, -notches * ViewFov.WheelStep);
            if (WheelRunOpen)
            {
                wheelRunEnd?.Pause();
                wheelRunEnd = FovLabel.schedule.Execute(EndWheelRun).StartingIn(WheelRunIdleMs);
            }
            UpdateInfo(true);
        }

        /// <summary>Writes the open run of wheel steps as one entry (nothing when none is open).</summary>
        public void EndWheelRun()
        {
            wheelRunEnd?.Pause();
            wheelRunEnd = null;
            if (wheelRunRoot != null)
            {
                wheelRunRoot.UnregisterCallback(wheelRunDown, TrickleDown.TrickleDown);
                wheelRunRoot.UnregisterCallback(wheelRunKey, TrickleDown.TrickleDown);
                wheelRunRoot = null;
            }
            var e = wheelRun;
            wheelRun = null;
            if (e != null && e.IsOpen) ctx.ShowResult(e.Commit());
        }

        string PopupTitle()
        {
            string id = ViewFov.Target(ctx);
            var n = id != null ? ctx.Model.Find(id) : null;
            return ctx.L10n.Format("viewfov.popup.title", id == null ? ctx.L10n.Get("camera.free") : n != null ? n.Label : id);
        }

        /// <summary>Opens the popup with the field-of-view slider below the readout, or closes it when it is open.</summary>
        public void ToggleFovPopup()
        {
            EndWheelRun();
            if (FovPopup != null) { CloseFovPopup(); return; }
            var layer = ctx.Shell?.Overlay;
            if (layer == null) return;
            // a click outside closes it; a double click on the readout (its second click lands here) resets the value
            fovBlocker = new VisualElement { name = "viewport-fov-blocker" };
            fovBlocker.style.position = Position.Absolute;
            fovBlocker.style.left = 0; fovBlocker.style.top = 0; fovBlocker.style.right = 0; fovBlocker.style.bottom = 0;
            fovBlocker.RegisterCallback<PointerDownEvent>(e =>
            {
                e.StopPropagation();
                if (e.clickCount >= 2 && FovLabel.worldBound.Contains(e.position)) ViewFov.Reset(ctx);
                CloseFovPopup();
                UpdateInfo(true);
            });
            fovBlocker.RegisterCallback<WheelEvent>(e =>
            {
                e.StopPropagation();
                if (FovLabel.worldBound.Contains(e.mousePosition)) FovWheel(e.delta.y);
            });
            var box = new VisualElement { name = "viewport-fov-popup", focusable = true };
            box.AddToClassList("hs-menu");
            box.AddToClassList("hs-viewport__fov-popup");
            box.style.position = Position.Absolute;
            var title = new Label(PopupTitle()) { name = "viewport-fov-popup-title" };
            title.AddToClassList("hs-text--dim");
            box.Add(title);
            var row = new FovRow(ctx, "viewport-fov-row");
            row.style.width = 260;
            box.Add(row);
            // Esc closes it wherever the focus is (Enter too, unless the number field takes it as its commit)
            fovKeys = e =>
            {
                bool esc = e.keyCode == KeyCode.Escape, enter = e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter;
                if (!esc && !enter) return;
                var fe = box.panel?.focusController?.focusedElement as VisualElement;
                if (enter && (fe == null || !(fe == box || box.Contains(fe)) || fe == row.Slider.Field || row.Slider.Field.Contains(fe))) return;
                e.StopPropagation();
                CloseFovPopup();
            };
            fovKeyRoot = layer.panel?.visualTree;
            fovKeyRoot?.RegisterCallback(fovKeys, TrickleDown.TrickleDown);
            var wb = FovLabel.worldBound;
            box.style.left = Mathf.Max(0, wb.xMax - 272);
            box.style.top = wb.yMax + 2;
            box.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float maxX = layer.layout.width - box.layout.width - 2, maxY = layer.layout.height - box.layout.height - 2;
                if (maxX > 0 && box.layout.x > maxX + 0.5f) box.style.left = maxX;
                if (maxY > 0 && box.layout.y > maxY + 0.5f) box.style.top = maxY;
            });
            layer.Add(fovBlocker);
            layer.Add(box);
            FovPopup = box;
            box.Focus();
        }

        public void CloseFovPopup()
        {
            if (fovKeyRoot != null && fovKeys != null) fovKeyRoot.UnregisterCallback(fovKeys, TrickleDown.TrickleDown);
            fovKeyRoot = null;
            fovKeys = null;
            fovBlocker?.RemoveFromHierarchy();
            FovPopup?.RemoveFromHierarchy();
            fovBlocker = null;
            FovPopup = null;
        }

        public void Dispose()
        {
            ctx.Tools.Close();
            EndWheelRun();
            CloseFovPopup();
            if (clearCam) UnityEngine.Object.Destroy(clearCam.gameObject);
        }
    }
}

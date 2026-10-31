// The viewport (spike S1d / S3a): the transparent, focusable centre element of the layout.
// V1 composition: on GeometryChangedEvent (and every frame, cheaply) the view camera's rect = the element's world bound
// converted panel → screen (× panel scale, y flipped, ÷ screen size); a full-screen clear camera (depth below the view
// camera, culling mask 0, solid --hs-bg-0, no post / shadows / AA) clears the pixels outside the rect that no opaque
// element covers (S3a). The element stops NavigationMoveEvent (arrow keys would move the focus away, S1d). Pointer
// routing: (1) gizmo handle → gizmo drag with pointer capture; (2) otherwise a pending click that becomes a
// camera drag after 4 logical px or stays a click (pick on pointer up). Wheel → camera dolly / Ctrl+wheel FOV. Overlays
// (absolute children): view info (FOV, active camera with "Release").
using System;
using HS2Next.Scenes.Runtime;
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
        public Label Info { get; }
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
            Info = new Label { name = "viewport-info", pickingMode = PickingMode.Ignore };
            Info.AddToClassList("hs-viewport__info");
            Element.Add(Info);
            ReleaseButton = new Button(() => ctx.Registry.Run(ActionIds.CameraRelease, "viewport")) { name = "viewport-release" };
            ReleaseButton.AddToClassList("hs-viewport__release");
            ReleaseButton.style.position = Position.Absolute;
            ReleaseButton.style.right = 8; ReleaseButton.style.top = 22;
            Loc.Bind(ReleaseButton, "viewport.release");
            Element.Add(ReleaseButton);
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

        void OnUp(PointerUpEvent e)
        {
            int bit = 1 << e.button;
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

        void OnCaptureLost()
        {
            capturedPointer = -1;
            if (Gizmo != null && Gizmo.Dragging) Gizmo.Cancel();
            if (cameraDrag) Camera.EndDrag();
            pending = cameraDrag = false;
            downButtonMask = 0;
        }

        /// <summary>Esc during a gizmo drag: cancel (revert). True when something was cancelled.</summary>
        public bool CancelDrag()
        {
            if (Gizmo != null && Gizmo.Dragging) { Gizmo.Cancel(); downButtonMask = 0; Release(); return true; }
            return false;
        }

        void OnWheel(WheelEvent e)
        {
            float notches = -e.delta.y / 3f;   // UI Toolkit wheel delta: 3 per notch, down positive
            if (Mathf.Abs(e.delta.y) > 0 && Mathf.Abs(e.delta.y) < 1) notches = -Mathf.Sign(e.delta.y);
            var mods = Input.ShortcutRouter.Mods(e.modifiers);
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
            UpdateInfo();
        }

        float infoAt;
        void UpdateInfo()
        {
            if (Time.unscaledTime < infoAt) return;
            infoAt = Time.unscaledTime + 0.25f;
            var vc = ctx.Runtime?.GetPart<SceneViewCamera>();
            var cam = vc?.Camera;
            string active = vc?.ActiveCameraId;
            string text = cam ? "FOV " + cam.fieldOfView.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : "";
            if (active != null)
            {
                var n = ctx.Model.Find(active);
                text += "\n" + ctx.L10n.Format("viewport.activeCamera", n != null ? n.Label : active);
            }
            Info.text = text;
            ReleaseButton.style.display = active != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void Dispose()
        {
            if (clearCam) UnityEngine.Object.Destroy(clearCam.gameObject);
        }
    }
}

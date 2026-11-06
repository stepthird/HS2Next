// The field of view of the current view, in one place for the cameras panel's «Field of view» row, the viewport's readout
// (its tooltip, the popup slider, the wheel over it and a double click) and the Camera menu's rows. The current view is the
// free camera, or the camera object the view looks through. The free camera's field of view is part of the scene's view
// state (`camera.current`): it is written as the camera keys write it (not recorded in the history, at most once a frame).
// A camera object's field of view is its `camera.fov`, written through the camera ops: one history entry "Camera" per drag,
// per click, per typed value and per run of wheel steps over the readout. Values are clamped to [1, 60]; reset = 23. The camera
// keys keep their behaviour: they change the free camera only (none of them works while the view looks through a camera
// object, as in the game). The tooltip names the keys of the active key map.
using System;
using System.Globalization;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Viewport
{
    public static class ViewFov
    {
        public const float Min = CameraController.FovMin, Max = CameraController.FovMax, Default = CameraController.FovDefault;
        /// <summary>One Camera menu row «Widen» / «Narrow» (degrees).</summary>
        public const float MenuStep = 5f;
        /// <summary>One wheel notch over the readout (degrees): the rate of Ctrl+wheel (15 °/s for 0.2 s a notch).</summary>
        public const float WheelStep = CameraController.KeyFov * 0.2f;

        /// <summary>The camera object the view looks through, or null for the free camera.</summary>
        public static string Target(UiContext ctx) => ctx?.Runtime?.GetPart<SceneViewCamera>()?.ActiveCameraId;

        /// <summary>The field of view the view shows now (a camera object without its own value shows the free camera's).</summary>
        public static float Current(UiContext ctx)
        {
            string id = Target(ctx);
            if (id != null)
            {
                float? f = (ctx.Model.Find(id)?.State as CameraNodeState)?.Fov;
                if (f.HasValue) return f.Value;
                var cam = ctx.Runtime?.GetPart<SceneViewCamera>()?.Camera;
                if (cam) return cam.fieldOfView;
            }
            var cc = ctx?.Viewport?.Camera;
            if (cc != null) return cc.Current.Fov;
            CameraView v = null;
            try { v = FieldAccessor.Get(ctx.Model, null, "camera.current") as CameraView; } catch (Exception) { }
            return v?.Fov ?? Default;
        }

        public static float Clamp(float v) => float.IsNaN(v) ? Default : Mathf.Clamp(v, Min, Max);

        /// <summary>Writes the current view's field of view: the free camera's view state, or the camera object's
        /// `camera.fov` (one entry "Camera", or a part of the UI edit that is open).</summary>
        public static OpResult Set(UiContext ctx, float fov)
        {
            fov = Clamp(fov);
            string id = Target(ctx);
            if (id != null) return ctx.ShowResult(ctx.Ops.SetCameraFov(new[] { id }, fov, UiContext.User()));
            var cc = ctx.Viewport?.Camera;
            if (cc != null) { cc.SetFov(fov); return OpResult.Success(); }
            var v = (FieldAccessor.Get(ctx.Model, null, "camera.current") as CameraView) ?? SceneViewCamera.ResetView();
            var nv = new CameraView { Pivot = v.Pivot, Rot = v.Rot, Distance = v.Distance, Fov = fov };
            return OpResult.From(ctx.Session.Set(null, "camera.current", nv, ExecuteOptions.NotRecorded));
        }

        /// <summary>Changes the field of view by <paramref name="delta"/> degrees.</summary>
        public static OpResult Step(UiContext ctx, float delta) => Set(ctx, Current(ctx) + delta);

        public static OpResult Reset(UiContext ctx) => Set(ctx, Default);

        public static string Degrees(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        /// <summary>What the value is, the keys of the active key map, and (for the readout) what the mouse does.</summary>
        public static string Tooltip(UiContext ctx, bool readout)
        {
            string id = Target(ctx);
            string f = Degrees(Current(ctx));
            string head;
            if (id != null)
            {
                var n = ctx.Model.Find(id);
                head = L10n.Format("viewfov.tip.camera", n != null ? n.Label : id, f);
            }
            else head = L10n.Format("viewfov.tip.free", f);
            string Keys(string action) { string t = ctx.Keys?.ChordText(action) ?? ""; return t.Length > 0 ? t : L10n.Get("keys.unbound"); }
            string keys = L10n.Format("viewfov.tip.keys", Keys(ActionIds.CameraFovDown), Keys(ActionIds.CameraFovUp), Keys(ActionIds.CameraFovReset));
            if (id != null) keys += "\n" + L10n.Get("viewfov.tip.keysFreeOnly");
            string tail = readout ? L10n.Format("viewfov.tip.readout", Degrees(Default)) : L10n.Get("viewfov.tip.row");
            return head + "\n" + keys + "\n" + tail;
        }
    }

    /// <summary>A «Field of view» slider row for the current view (1–60, ↺ = 23): live while dragging; a drag on a camera
    /// object is one history entry. Follows the view (keys, the other row, a camera switch) when not dragging.</summary>
    public sealed class FovRow : VisualElement
    {
        readonly UiContext ctx;
        public readonly NumberSlider Slider;
        UiEdit edit;
        float shownValue = float.NaN;
        // a drag on the free camera has no edit to cancel: the view state it wrote goes back here on Esc
        float pressValue;
        VisualElement freeEscRoot;
        EventCallback<KeyDownEvent> freeEsc;

        public FovRow(UiContext ctx, string name)
        {
            this.ctx = ctx;
            this.name = name;
            AddToClassList("hs-fov-row");
            Slider = new NumberSlider(L10n.Get("viewfov.label"), ViewFov.Min, ViewFov.Max)
            {
                name = name + "-slider", Format = "0.#", TypedMin = ViewFov.Min, TypedMax = ViewFov.Max, DefaultValue = () => ViewFov.Default,
            };
            Slider.LabelElement.name = name + "-label";
            Slider.LabelElement.style.width = StyleKeyword.Auto;
            Slider.LabelElement.style.minWidth = 0;
            Slider.LabelElement.style.marginRight = 6;
            Slider.Field.style.width = 48;
            Slider.ResetButton.name = name + "-reset";
            Loc.Bind(Slider.LabelElement, "viewfov.label");
            Loc.Tooltip(Slider.ResetButton, "viewfov.reset", ViewFov.Degrees(ViewFov.Default));
            Slider.Edited += OnEdited;
            Add(Slider);
            schedule.Execute(Refresh).Every(100);
            Refresh();
            RegisterCallback<DetachFromPanelEvent>(_ => UnhookFreeEsc());
        }

        void OnEdited(EditPhase phase, double value)
        {
            float v = ViewFov.Clamp((float)value);
            switch (phase)
            {
                case EditPhase.Begin:
                    edit?.Cancel();
                    edit = ViewFov.Target(ctx) != null ? ctx.BeginEdit(OpLabels.Camera) : null;
                    if (edit == null) HookFreeEsc();
                    break;
                case EditPhase.Preview:
                    if (edit != null && !edit.IsOpen) break;   // Esc took the drag back: a late preview writes nothing
                    ViewFov.Set(ctx, v);
                    break;
                case EditPhase.End:
                    UnhookFreeEsc();
                    var e = edit;
                    edit = null;
                    if (e != null) ctx.ShowResult(e.Commit());
                    Refresh(true);
                    break;
                case EditPhase.Commit:
                    ViewFov.Set(ctx, v);
                    Refresh(true);
                    break;
                case EditPhase.Typed:
                    ViewFov.Set(ctx, v);
                    Refresh(true);
                    break;
            }
        }

        void HookFreeEsc()
        {
            UnhookFreeEsc();
            pressValue = ViewFov.Current(ctx);
            var at = panel;
            freeEscRoot = at?.visualTree;
            if (freeEscRoot == null) return;
            freeEsc = e =>
            {
                if (e.keyCode != KeyCode.Escape || e.modifiers != EventModifiers.None) return;
                ViewFov.Set(ctx, pressValue);
                // only this row's own pointer; the row may have left the panel already (a popup's own Esc comes first)
                for (int id = 0; id < PointerId.maxPointers; id++)
                    if (at.GetCapturingElement(id) is VisualElement holder && (holder == Slider || Slider.Contains(holder))) at.ReleasePointer(id);
                Slider.End();
                UnhookFreeEsc();
                e.StopPropagation();
            };
            freeEscRoot.RegisterCallback(freeEsc, TrickleDown.TrickleDown);
        }

        void UnhookFreeEsc()
        {
            if (freeEscRoot != null && freeEsc != null) freeEscRoot.UnregisterCallback(freeEsc, TrickleDown.TrickleDown);
            freeEscRoot = null;
            freeEsc = null;
        }

        /// <summary>Shows the current view's value and its tooltip (not while dragging). Tests call it.</summary>
        public void Refresh() => Refresh(false);

        void Refresh(bool force)
        {
            if (!force && (Slider.Dragging || Typing())) return;
            float f = ViewFov.Current(ctx);
            if (force || !(Mathf.Abs(f - shownValue) < 1e-4f)) { shownValue = f; Slider.SetDisplay(f, false); }
            string tip = ViewFov.Tooltip(ctx, readout: false);
            if (tip != tooltip) tooltip = tip;
        }

        bool Typing()
        {
            var fe = Slider.Field.panel?.focusController?.focusedElement as VisualElement;
            return fe != null && (fe == Slider.Field || Slider.Field.Contains(fe));
        }
    }
}

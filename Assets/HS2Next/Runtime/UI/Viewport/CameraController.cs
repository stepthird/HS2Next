// The camera controller (U16): writes the view (`scene.camera.current`, view state, never
// history) through the bus with Record None, coalesced to one write per frame. Disabled while a camera node is active
// (the overlay offers "Release"). Mouse (both presets): left drag orbit (0.25 °/logical px × camera.rotSpeedX/Y ×
// camera.speed), right drag vertical = pivot y / horizontal = dolly, middle drag pan in the screen plane, left + right pan
// along camera right / forward, wheel dolly d ×= 1.1^(−notches) (≥ 0.1; option camera.wheelDolly), Alt + left / middle /
// right = orbit / pan / dolly (camera.requireAlt: only with Alt), double-click frames the object under the cursor.
// Pan / dolly rate m = 0.0025 × max(d, 1) per px × camera.speed; camera.constantSpeed (hs2studio) = vanilla constants
// (0.05 per mouse-axis unit, one unit = 10 logical px). Keys (held, frame-rate independent): move 1 unit/s ×
// camera.keySpeed (Shift ×5, Ctrl ×0.2 from the resolver), orbit 10 × rot speed °/s, roll 10 °/s, FOV 15 °/s in [1, 60].
using System;
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Viewport
{
    public sealed class CameraController
    {
        public const float OrbitPerPx = 0.25f, PanPerPx = 0.0025f, ConstantPerPx = 0.005f, WheelFactor = 1.1f, MinDistance = 0.1f;
        public const float KeyMove = 1f, KeyOrbit = 10f, KeyRoll = 10f, KeyFov = 15f, FovMin = 1f, FovMax = 60f, FovDefault = 23f;
        readonly UiContext ctx;
        readonly ViewportHost vp;
        Vector3 pivot, rot;
        float distance, fov;
        bool dirty;
        /// <summary>A mouse camera drag is running (the pivot marker shows).</summary>
        public bool Dragging { get; private set; }
        /// <summary>View writes so far (tests: at most one per frame).</summary>
        public int Writes { get; private set; }
        int lastWriteFrame = -1;

        public CameraController(UiContext ctx, ViewportHost vp)
        {
            this.ctx = ctx;
            this.vp = vp;
        }

        public bool Enabled => ctx.Runtime?.GetPart<SceneViewCamera>()?.ActiveCameraId == null;
        public Vector3 Pivot => pivot;

        // ---------------- model view

        void Read()
        {
            if (dirty) return;
            CameraView v = null;
            try { v = FieldAccessor.Get(ctx.Model, null, "camera.current") as CameraView; } catch (Exception) { }
            v ??= SceneViewCamera.ResetView();
            pivot = new Vector3(v.Pivot.X, v.Pivot.Y, v.Pivot.Z);
            rot = new Vector3(v.Rot.X, v.Rot.Y, v.Rot.Z);
            distance = v.Distance;
            fov = v.Fov;
        }

        /// <summary>The current view (model value, or the working copy while a write is pending).</summary>
        public CameraView Current
        {
            get
            {
                Read();
                return new CameraView { Pivot = new Vec3(pivot.x, pivot.y, pivot.z), Rot = new Vec3(rot.x, rot.y, rot.z), Distance = distance, Fov = fov };
            }
        }

        void Changed() => dirty = true;

        /// <summary>Writes a pending change (once per frame; UiHost.Update).</summary>
        public void Flush()
        {
            if (!dirty) return;
            if (lastWriteFrame == Time.frameCount) return;
            dirty = false;
            lastWriteFrame = Time.frameCount;
            var v = new CameraView { Pivot = new Vec3(pivot.x, pivot.y, pivot.z), Rot = new Vec3(rot.x, rot.y, rot.z), Distance = distance, Fov = fov };
            var r = ctx.Session.Set(null, "camera.current", v, ExecuteOptions.NotRecorded);
            if (r.Ok) Writes++;
        }

        /// <summary>Sets the whole view at once (tests, frame, reset).</summary>
        public void SetView(Vector3 newPivot, Vector3 newRot, float newDistance, float newFov)
        {
            Read();
            pivot = newPivot; rot = newRot; distance = Mathf.Max(MinDistance, newDistance); fov = Mathf.Clamp(newFov, FovMin, FovMax);
            Changed();
        }

        float Speed => Opt(OptionDefs.CameraSpeed, 1f);
        float Opt(string key, float def) { float v = ctx.Options.Float(key); return v > 0 ? v : def; }
        bool Constant => ctx.Options.Bool(OptionDefs.CameraConstantSpeed);
        float PanRate => Constant ? ConstantPerPx * Speed : PanPerPx * Mathf.Max(distance, 1f) * Speed;

        Quaternion Rotation => Quaternion.Euler(rot);

        // ---------------- mouse

        public void BeginDrag(int buttons, EventModifiers mods)
        {
            if (!Enabled) return;
            Dragging = true;
        }

        public void EndDrag()
        {
            Dragging = false;
            Flush();
        }

        /// <summary>A drag step: buttons = bit mask (1 left, 2 right, 4 middle), delta in logical px (y down).</summary>
        public void Drag(int buttons, Vector2 delta, EventModifiers mods)
        {
            if (!Enabled) return;
            bool alt = (mods & EventModifiers.Alt) != 0;
            if (ctx.Options.Bool(OptionDefs.CameraRequireAlt) && !alt) return;
            Read();
            float invert = ctx.Options.Bool(OptionDefs.CameraInvertY) ? -1f : 1f;
            bool left = (buttons & 1) != 0, right = (buttons & 2) != 0, middle = (buttons & 4) != 0;
            if (left && right) PanForward(delta);
            else if (left) Orbit(delta.x * OrbitPerPx * Opt(OptionDefs.CameraRotSpeedX, 1f) * Speed, delta.y * invert * OrbitPerPx * Opt(OptionDefs.CameraRotSpeedY, 1f) * Speed);
            else if (middle) PanScreen(delta);
            else if (right)
            {
                if (alt) { distance = Mathf.Max(MinDistance, distance + (delta.x + delta.y) * PanRate); }
                else
                {
                    pivot.y -= delta.y * PanRate;
                    distance = Mathf.Max(MinDistance, distance + delta.x * PanRate);
                }
            }
            Changed();
        }

        /// <summary>yaw += dYaw, pitch += dPitch (degrees).</summary>
        public void Orbit(float dYaw, float dPitch)
        {
            Read();
            rot.y += dYaw;
            rot.x += dPitch;
            Changed();
        }

        void PanScreen(Vector2 d)
        {
            var q = Rotation;
            float m = PanRate;
            pivot += q * Vector3.left * (d.x * m) + q * Vector3.up * (d.y * m);
        }

        void PanForward(Vector2 d)
        {
            var q = Rotation;
            float m = PanRate;
            var fwd = q * Vector3.forward;
            pivot += q * Vector3.right * (d.x * m) + fwd * (-d.y * m);
        }

        public void Wheel(float notches)
        {
            if (!Enabled || notches == 0 || !ctx.Options.Bool(OptionDefs.CameraWheelDolly)) return;
            Read();
            distance = Mathf.Max(MinDistance, distance * Mathf.Pow(WheelFactor, -notches));
            Changed();
        }

        // ---------------- keys (held actions) and commands

        float KeySpeed => Opt(OptionDefs.CameraKeySpeed, 1f);

        public void Move(Vector3 cameraDir, float dt, double speed)
        {
            if (!Enabled) return;
            Read();
            float s = KeyMove * KeySpeed * (float)speed * dt;
            var q = Rotation;
            if (cameraDir == Vector3.up || cameraDir == Vector3.down) pivot += cameraDir * s;
            else pivot += q * cameraDir * s;
            Changed();
        }

        public void Dolly(float sign, float dt, double speed)
        {
            if (!Enabled) return;
            Read();
            distance = Mathf.Max(MinDistance, distance + sign * KeyMove * KeySpeed * (float)speed * dt);
            Changed();
        }

        public void OrbitKey(float yawSign, float pitchSign, float dt, double speed)
        {
            if (!Enabled) return;
            float r = KeyOrbit * (float)speed * dt;
            Orbit(yawSign * r * Opt(OptionDefs.CameraRotSpeedX, 1f), pitchSign * r * Opt(OptionDefs.CameraRotSpeedY, 1f));
        }

        public void Roll(float sign, float dt, double speed)
        {
            if (!Enabled) return;
            Read();
            rot.z += sign * KeyRoll * (float)speed * dt;
            Changed();
        }

        public void Fov(float sign, float dt, double speed)
        {
            if (!Enabled) return;
            Read();
            fov = Mathf.Clamp(fov + sign * KeyFov * (float)speed * dt, FovMin, FovMax);
            Changed();
        }

        public void Reset()
        {
            if (!Enabled) return;
            var v = SceneViewCamera.ResetView();
            SetView(new Vector3(v.Pivot.X, v.Pivot.Y, v.Pivot.Z), new Vector3(v.Rot.X, v.Rot.Y, v.Rot.Z), v.Distance, v.Fov);
        }

        /// <summary>Sets the field of view, clamped to [FovMin, FovMax] (the cameras panel's row and the viewport's readout;
        /// written with the next flush, as the keys are).</summary>
        public void SetFov(float value)
        {
            if (!Enabled || float.IsNaN(value)) return;
            Read();
            fov = Mathf.Clamp(value, FovMin, FovMax);
            Changed();
        }

        public void ResetAngles() { if (!Enabled) return; Read(); rot.x = 0; rot.y = 0; Changed(); }
        public void ResetRoll() { if (!Enabled) return; Read(); rot.z = 0; Changed(); }
        public void ResetFov() { if (!Enabled) return; Read(); fov = FovDefault; Changed(); }

        /// <summary>Frames nodes: pivot to their bounds' centre; with <paramref name="fit"/> the distance fits the bounds
        /// (angles kept), else the distance is kept (hs2studio: pivot to the primary, vanilla C / F).</summary>
        public bool Frame(IEnumerable<string> ids, bool fit = true)
        {
            if (!Enabled) return false;
            var b = NodeBounds.Of(ctx, ids, out bool any);
            if (!any) return false;
            Read();
            pivot = b.center;
            if (fit)
            {
                float radius = Mathf.Max(0.5f, b.extents.magnitude);
                float half = Mathf.Max(1f, fov) * 0.5f * Mathf.Deg2Rad;
                distance = Mathf.Max(MinDistance, radius / Mathf.Sin(half) * 1.1f);
            }
            Changed();
            return true;
        }

        /// <summary>Double-click: frame the object under the cursor.</summary>
        public void FrameAt(Vector2 panelPoint)
        {
            var hit = vp.Picker.Pick(vp, panelPoint);
            if (hit.NodeId != null) Frame(new[] { hit.NodeId });
        }

        public void Update(float dt) => Flush();
    }

    /// <summary>World bounds of nodes from their realized renderers (holder position when a node has none).</summary>
    public static class NodeBounds
    {
        public static Bounds Of(UiContext ctx, IEnumerable<string> ids, out bool any)
        {
            any = false;
            var b = new Bounds();
            if (ids == null || ctx.Runtime == null) return b;
            foreach (var id in ids)
            {
                var h = ctx.Runtime.HolderOf(id);
                if (!h) continue;
                bool got = false;
                foreach (var r in h.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                    if (!any && !got) { b = r.bounds; any = got = true; }
                    else { b.Encapsulate(r.bounds); got = true; }
                }
                if (!got)
                {
                    if (!any) { b = new Bounds(h.position, Vector3.one); any = true; }
                    else b.Encapsulate(h.position);
                }
            }
            return b;
        }
    }
}

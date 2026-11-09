// The depth of field of the dof command: a scene part that holds the scene's `effects.depthOfField` slot (owner "dof", priority 1: above the Timeline's dof
// track). Its value starts as the value shown when the command first takes the slot (the Timeline's, else the scene's) and each change sets only what it
// gives. A focus given with a blend goes from the focus shown (the point shown while another blend runs) to the new one over the blend's seconds of the
// scene clock, eased linear (u), in (u^2), out (1 - (1 - u)^2) or inout (u^2 (3 - 2u)); at the end the new focus is the focus. Release hands the depth
// of field back to the Timeline or the scene; the scene's own depth of field is never written. A slot the layer released (the scene cleared) is taken
// again, from the value shown then, at the next change. A scene with no dof command does nothing: the tick returns at its first branch.
using System;
using HS2Next.Save;
using HS2Next.Scenes.Overrides;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>One dof command's change: only what is given changes.</summary>
    public sealed class DofChange
    {
        public bool? On;
        /// <summary>The focal size and the aperture (clamped by the caller).</summary>
        public double? Size, Aperture;
        /// <summary>The range they came from, when they came from one (for the reply).</summary>
        public double? Sharp, Full;
        /// <summary>The largest blur on a 1080-line frame; 0 or less: the renderer's.</summary>
        public double? Blur;
        /// <summary>A new focus (null = the pivot) when <see cref="HasFocus"/>.</summary>
        public bool HasFocus;
        public OverrideFocus Focus;
        /// <summary>Seconds of the blend to the new focus; 0 or less: at once.</summary>
        public double Blend;
        public string Ease = ScriptedDof.EaseInOut;
    }

    public sealed class ScriptedDof : ISceneRuntimePart, ISceneTickPart, IScriptCommandSlots, IDisposable
    {
        /// <summary>The override owner of the dof command's slot.</summary>
        public const string Owner = "dof";
        /// <summary>Above the Timeline's dof track (0), below the constraints (10).</summary>
        public const int Priority = 1;
        public const string EaseLinear = "linear", EaseIn = "in", EaseOut = "out", EaseInOut = "inout";
        public static readonly string[] Eases = { EaseLinear, EaseIn, EaseOut, EaseInOut };

        SceneRuntime rt;
        OverrideSlot slot;
        bool enabled;
        double size, aperture, blur;
        OverrideFocus focus = OverrideFocus.Pivot, to;
        double blendSeconds, blendElapsed;
        string ease = EaseInOut;
        double? sharp, full;

        /// <summary>The scene's part, added when a runtime has none.</summary>
        public static ScriptedDof Of(SceneRuntime rt)
        {
            if (rt == null) return null;
            var p = rt.GetPart<ScriptedDof>();
            if (p != null) return p;
            p = new ScriptedDof();
            rt.AddPart(p);
            return p;
        }

        /// <summary>True while the part holds the depth of field.</summary>
        public bool Holding => slot != null && !slot.IsReleased;
        /// <summary>True while a blend to a new focus runs.</summary>
        public bool Blending => Holding && to != null;
        /// <summary>The value the slot holds, or null.</summary>
        public OverrideDof Value => Holding ? slot.Dof : null;
        /// <summary>The focus the value has or blends to (null when not held).</summary>
        public OverrideFocus Focus => Holding ? to ?? focus : null;
        /// <summary>The range the focal size and aperture came from, when they came from one.</summary>
        public double? Sharp => Holding ? sharp : null;
        public double? Full => Holding ? full : null;

        /// <summary>An ease of a blend at u in [0, 1].</summary>
        public static double Ease(string name, double u)
        {
            u = u < 0 ? 0 : u > 1 ? 1 : u;
            switch (name)
            {
                case EaseLinear: return u;
                case EaseIn: return u * u;
                case EaseOut: return 1 - (1 - u) * (1 - u);
                default: return u * u * (3 - 2 * u);
            }
        }

        /// <summary>The depth of field shown now: the winning slot's value, else the scene's block (blur 0: the renderer's).</summary>
        public static OverrideDof Shown(SceneRuntime rt)
        {
            var w = rt?.Overrides.WinnerOf(null, OverrideChannel.DepthOfField);
            if (w != null && w.Dof != null) return w.Dof;
            var d = rt?.Model.Settings.Effects.DepthOfField;
            if (d == null) return new OverrideDof(false, 0, 0, 0, OverrideFocus.Pivot);
            var f = d.Focus != null && JsonText.IsId(d.Focus) ? OverrideFocus.OfNode(d.Focus) : OverrideFocus.Pivot;
            return new OverrideDof(d.Enabled, d.FocalSize, d.Aperture, 0, f);
        }

        /// <summary>Applies a change, taking the slot first when the part does not hold it. Null when the layer refuses the slot.</summary>
        public OverrideDof Change(DofChange c)
        {
            if (rt == null || c == null) return null;
            if (!Holding)
            {
                var shown = Shown(rt);
                slot = rt.Overrides.Acquire(Owner, null, "effects.depthOfField", Priority);
                if (slot == null) return null;
                enabled = shown.Enabled;
                size = shown.FocalSize;
                aperture = shown.Aperture;
                blur = shown.Blur;
                focus = shown.FocusTo != null ? PointShown() ?? shown.Focus : shown.Focus;
                to = null;
                sharp = full = null;
            }
            if (c.On.HasValue) enabled = c.On.Value;
            if (c.Size.HasValue) { size = c.Size.Value; sharp = c.Sharp; full = c.Full; }
            if (c.Aperture.HasValue) { aperture = c.Aperture.Value; sharp = c.Sharp; full = c.Full; }
            if (c.Blur.HasValue) blur = c.Blur.Value;
            if (c.HasFocus)
            {
                var f = c.Focus ?? OverrideFocus.Pivot;
                if (c.Blend > 0)
                {
                    if (to != null) focus = PointShown() ?? to;   // from where the focus is now
                    to = f;
                    blendSeconds = c.Blend;
                    blendElapsed = 0;
                    ease = c.Ease ?? EaseInOut;
                }
                else
                {
                    focus = f;
                    to = null;
                }
            }
            Push();
            return slot.Dof;
        }

        // The resolved focus point of the last late apply, as a point focus; null when the effects part has none.
        OverrideFocus PointShown()
        {
            var fx = rt.GetPart<SceneEffects>();
            if (fx == null) return null;
            var p = fx.Dof.FocusPoint;
            return OverrideFocus.OfPoint(p.x, p.y, p.z);
        }

        void Push()
        {
            double w = to != null ? Ease(ease, blendSeconds > 0 ? blendElapsed / blendSeconds : 1) : 0;
            slot.SetDof(new OverrideDof(enabled, size, aperture, blur, focus, to, w));
        }

        /// <summary>Hands the depth of field back (the Timeline's dof track or the scene's block shows); true when the part held it.</summary>
        public bool Release()
        {
            bool had = Holding;
            if (slot != null && !slot.IsReleased) slot.Release();
            slot = null;
            to = null;
            sharp = full = null;
            return had;
        }

        /// <summary>Hands the depth of field back (IScriptCommandSlots); 1 when the part held it.</summary>
        public int ReleaseAll(bool writeShown) => Release() ? 1 : 0;

        // ------------------------------------------------------------ ISceneTickPart, ISceneRuntimePart

        public void Tick(float dt)
        {
            if (slot == null) return;
            if (slot.IsReleased)
            {
                Release();
                return;
            }
            if (to == null) return;
            if (dt > 0f && !float.IsInfinity(dt)) blendElapsed += dt;
            if (blendElapsed >= blendSeconds)
            {
                focus = to;
                to = null;
            }
            Push();
        }

        public void Attach(SceneRuntime r) { rt = r; }
        public void OnChanged(SceneChangeBatch b) { }
        public void Apply() { }
        public void LateApply() { }
        public void Clear() { }
        public void OnLoadedRealized() { }

        public void Dispose() => Release();
    }
}

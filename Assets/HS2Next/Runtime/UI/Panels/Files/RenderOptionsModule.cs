// The UI options of the app's render settings (Phase 3.6 integrator): keeps
//   render.backgroundColor  ("" = the settings file's render.backgroundColor, else "#RRGGBB") → RenderOptions.backgroundColor
//                           (the view's clear colour; the scene panel's "Clear colour…" opens the options at this row)
//   render.sceneShaderTime  (all / captures / off) → SceneShaderTime.Scope (the shader time of the global pause)
//   render.mosaicWide       (bool, default on) → MosaicWide.Enabled (the wide screen-space mosaic, 5.M)
// in step with the options at UI start and on every change. The bridge (app.options render, scene.pause {shaderTime}) can
// still change both at run time without touching the options.
using HS2Next.UI.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HS2Next.UI.Panels
{
    public sealed class RenderOptionsModule : IUiModule
    {
        public int Order => 126;

        /// <summary>The settings file's clear colour (taken before the option first applies; "" restores it).</summary>
        static Color? fileColour;

        public void Register(UiContext ctx)
        {
            if (ctx.Options == null) return;
            if (fileColour == null) fileColour = RenderOptions.BackgroundColor;
            Apply(ctx, OptionDefs.RenderBackgroundColor);
            Apply(ctx, OptionDefs.RenderSceneShaderTime);
            Apply(ctx, OptionDefs.RenderMosaicWide);
            ctx.Options.Changed += k => Apply(ctx, k);
        }

        /// <summary>The colour an option value stands for ("" or unreadable = the settings file's colour).</summary>
        public static Color ColourOf(string value)
        {
            var c = fileColour ?? RenderOptions.BackgroundColor;
            if (!string.IsNullOrEmpty(value) && ColorUtility.TryParseHtmlString(value, out var p)) { c = p; c.a = 1f; }
            return c;
        }

        static void Apply(UiContext ctx, string key)
        {
            if (key == OptionDefs.RenderBackgroundColor)
            {
                var c = ColourOf(ctx.Options.String(key));
                if (c == RenderOptions.BackgroundColor) return;
                try { RenderOptions.Apply(new JObject { ["backgroundColor"] = new JArray(c.r, c.g, c.b, c.a) }, "options"); }
                catch (System.FormatException e) { Debug.LogWarning("[ui] render.backgroundColor: " + e.Message); }
            }
            else if (key == OptionDefs.RenderSceneShaderTime)
            {
                if (SceneShaderTime.TryParseScope(ctx.Options.String(key), out var scope)) SceneShaderTime.Scope = scope;
            }
            else if (key == OptionDefs.RenderMosaicWide) MosaicWide.Enabled = ctx.Options.Bool(key);
        }
    }
}

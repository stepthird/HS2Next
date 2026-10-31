// Screen-effect commands of Phase 3.3 (package B): effects.state (the 12 blocks' state
// values as scene.json writes them, and what SceneEffects realized: grading mode / LUT / base grade / strip hash, SSAO,
// URP bloom, DOF focus and CoC slope, vignette, the camera probe, fog's RenderSettings, the sun-shaft caster, self
// shadow), effects.set (paths under `effects.`, validated all-or-nothing before any is applied, one history entry) and
// effects.reset (blocks back to ScreenEffectDefine's defaults, one entry). Blocks 11–12 (ambient, sky) are package S's:
// their realized members are added by S through RealizedExtensions.
using System;
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace HS2Next.App.Commands
{
    public static class EffectCommands
    {
        /// <summary>The commands of this file.</summary>
        public static readonly IReadOnlyList<string> Names = new[] { "effects.state", "effects.set", "effects.reset" };

        /// <summary>Extra members of effects.state's `realized` (package S adds `ambient` and `sky`).</summary>
        public static event Action<CoreContext, BridgeReply> RealizedExtensions;

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("effects.state", "The screen-effect blocks: state values (as scene.json) and what the runtime realized (grading, SSAO, bloom, DOF focus, vignette, probe, fog RenderSettings, sun-shaft caster, self shadow).", "effects")
                .Arg("blocks", ArgTypes.ArrayOf(ArgTypes.String), "block names (" + string.Join(", ", SceneEffects.BlockNames) + "); default all")
                .Arg("timing", ArgTypes.Bool, "bake / read / frame milliseconds", defaultValue: false)
                .Return("effects", ArgTypes.Object, "{<block>: state values}").Return("realized", ArgTypes.Object, "per block")
                .Return("findings", ArgTypes.ArrayOf(ArgTypes.Object), "effects.* and fog.* findings of the scene")
                .Return("timing", ArgTypes.Object, "with timing", false)
                .WithFlags(CommandFlags.ReadOnly).WithTimeout(60), State);

            CommandKit.Register(r, app, CommandKit.Spec("effects.set", "Sets effect values by path under effects. (validated before any is applied; one undo entry).", "effects")
                .Arg("set", ArgTypes.Object, "{<path under effects.>: <value>}: \"bloom.intensity\": 3, \"colorGrading.lut\": \"illusion.hs2#3\", \"vignette.enabled\": false, \"depthOfField.focus\": \"<node id>\", ...", required: true)
                .Arg("label", ArgTypes.String, "history label (default \"Effects\")")
                .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "paths written").Return("noop", ArgTypes.ArrayOf(ArgTypes.String), "paths that already had the value")
                .Example("{\"cmd\":\"effects.set\",\"set\":{\"bloom.intensity\":3,\"vignette.intensity\":0.5}}")
                .WithFlags(CommandFlags.Mutates).WithTimeout(60), Set);

            CommandKit.Register(r, app, CommandKit.Spec("effects.reset", "Resets blocks to HS2's defaults (ScreenEffectDefine; the panel's default buttons); one undo entry.", "effects")
                .Arg("blocks", ArgTypes.ArrayOf(ArgTypes.String), "block names; default all")
                .Arg("label", ArgTypes.String, "history label (default \"Reset effects\")")
                .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "blocks whose values changed")
                .WithFlags(CommandFlags.Mutates).WithTimeout(60), Reset);
        }

        static SceneEffects Part(CoreContext c) => c.Runtime?.GetPart<SceneEffects>();

        static List<string> Blocks(CoreContext c, string arg)
        {
            var res = new List<string>();
            if (!c.Args.Has(arg)) { res.AddRange(SceneEffects.BlockNames); return res; }
            if (c.Args.Kind(arg) != ArgKind.Array) throw CommandKit.BadArgs(arg, ArgTypes.ArrayOf(ArgTypes.String));
            foreach (var b in c.Args.Get<string[]>(arg))
            {
                if (SceneEffects.BlockIndex(b) < 0) throw CommandKit.BadArgs(arg, ArgTypes.Enum(SceneEffects.BlockNames), "\"" + b + "\" is not an effect block");
                if (!res.Contains(b)) res.Add(b);
            }
            return res;
        }

        static double[] V3(Vector3? v) => v.HasValue ? CommandKit.D3(v.Value) : null;

        // ------------------------------------------------------------ effects.state

        static object State(CoreContext c)
        {
            var blocks = Blocks(c, "blocks");
            c.Runtime.ApplyNow();
            var part = Part(c);
            part?.LateApply();
            var model = c.Session.Model;
            var fx = model.Settings.Effects;
            var values = new BridgeReply();
            foreach (var b in blocks) values[b] = new RawJson(FieldAccessor.GetJson(model, null, "effects." + b));

            var real = new BridgeReply();
            if (part != null)
            {
                var vol = part.Volume;
                foreach (var b in blocks)
                {
                    switch (b)
                    {
                        case "colorGrading":
                        {
                            var bg = part.BaseGradeOverride ?? SceneEffects.BaseGradeOf(c.Services?.Hs2Root);
                            real[b] = new BridgeReply().With("mode", part.GradingMode).With("lut", part.LutRef).With("lutFound", part.LutFound)
                                .With("baseGrade", new BridgeReply().With("source", bg.Source).With("ok", bg.Ok).With("route", bg.Route).With("error", bg.Error))
                                .With("lutHash", part.StripHash).With("contribution", (double)vol.Lookup.contribution.value)
                                .With("tonemapping", vol.Tonemap.mode.value.ToString()).With("profile", vol.ProfileSource);
                            break;
                        }
                        case "ambientOcclusion":
                        {
                            var ao = part.AmbientOcclusion;
                            var inp = HS2Next.HS2AoFeature.Inputs;
                            var urpSsao = AmbientOcclusionControl.FindFeature();
                            real[b] = new BridgeReply().With("active", inp.Active && ao.Feature).With("intensity", (double)inp.Intensity)
                                .With("radius", (double)inp.Radius).With("thickness", (double)inp.Thickness).With("afterOpaque", true).With("depthSource", "Depth")
                                .With("pass", "HS2AoFeature").With("source", ao.Source).With("urpSsaoActive", urpSsao ? (object)urpSsao.isActive : null);
                            break;
                        }
                        case "bloom":
                        {
                            var bl = vol.Bloom;
                            real[b] = new BridgeReply().With("active", bl.intensity.value > 0f).With("threshold", (double)bl.threshold.value).With("intensity", (double)bl.intensity.value)
                                .With("scatter", (double)bl.scatter.value).With("clamp", (double)bl.clamp.value).With("tint", CommandKit.C(bl.tint.value))
                                .With("maxIterations", bl.maxIterations.value).With("levels", part.Bloom.Levels).With("highQuality", bl.highQualityFiltering.value);
                            break;
                        }
                        case "depthOfField":
                        {
                            var d = part.Dof;
                            var fnode = d.FocusKey != null ? model.Find(d.FocusKey) : null;
                            string kind = fnode != null ? CommandKit.KindName(fnode) : null;
                            real[b] = new BridgeReply().With("active", d.Active)
                                .With("focus", new BridgeReply().With("key", d.FocusKey).With("kind", kind).With("resolved", d.FocusKind).With("point", CommandKit.D3(d.FocusPoint)))
                                .With("coc", new BridgeReply().With("A", float.IsInfinity(d.A) ? (object)null : (double)d.A).With("focalSize", (double)d.FocalSize))
                                .With("referenceHeight", d.ReferenceHeight).With("drawn", false);
                            break;
                        }
                        case "vignette":
                            real[b] = new BridgeReply().With("active", vol.Vignette.intensity.value > 0f).With("intensity", (double)vol.Vignette.intensity.value)
                                .With("smoothness", (double)vol.Vignette.smoothness.value).With("rounded", vol.Vignette.rounded.value).With("color", CommandKit.C(vol.Vignette.color.value));
                            break;
                        case "ssr":
                        {
                            var ss = SsrControl.Last;
                            var rr = new BridgeReply().With("drawn", ss.Drawn).With("option", RenderOptions.Ssr).With("sceneFlag", fx.Ssr.Enabled)
                                .With("receivers", ss.Receivers);
                            if (ss.Reason != null) rr["reason"] = ss.Reason;
                            real[b] = rr;
                            break;
                        }
                        case "reflectionProbe":
                        {
                            var p = part.Probe;
                            real[b] = new BridgeReply().With("active", p.Active).With("ref", part.ProbeRef).With("cubemap", p.CubemapName)
                                .With("intensity", p.Probe ? (double)p.Probe.intensity : (double?)null).With("importance", p.Probe ? p.Probe.importance : (int?)null);
                            break;
                        }
                        case "fog":
                            // drawn by HS2Next's own pass (opaque pixels) and the fitted transparent shaders' term (gated per material
                            // by the original's fog modes); RenderSettings.fog stays false
                            real[b] = new BridgeReply().With("drawn", HS2Next.HS2FogFeature.Draws)
                                .With("pass", new BridgeReply().With("opaque", HS2Next.HS2FogFeature.Draws).With("transparents", HS2Next.HS2FogFeature.Draws))
                                .With("mode", HS2Next.HS2FogFeature.Current.Mode)
                                .With("renderSettings", new BridgeReply().With("fog", RenderSettings.fog)
                                .With("mode", RenderSettings.fogMode.ToString()).With("color", CommandKit.C(RenderSettings.fogColor)).With("density", (double)RenderSettings.fogDensity)
                                .With("start", (double)RenderSettings.fogStartDistance).With("end", (double)RenderSettings.fogEndDistance));
                            break;
                        case "sunShafts":
                        {
                            // evaluated for the view camera (the feature's own last frame may be a capture's)
                            var vcam = c.Runtime.GetPart<SceneViewCamera>()?.Camera;
                            var f = HS2Next.HS2SunShaftsFeature.Evaluate(vcam, HS2Next.HS2SunShaftsFeature.Inputs, vcam ? vcam.pixelWidth : 0, vcam ? vcam.pixelHeight : 0);
                            real[b] = new BridgeReply().With("drawn", f.Drawn)
                                .With("caster", new BridgeReply().With("key", part.CasterKey).With("kind", part.CasterKind).With("resolved", part.CasterKind).With("point", V3(part.CasterPoint)))
                                .With("sunViewport", new[] { (double)f.Sun.x, (double)f.Sun.y }).With("behind", f.Behind).With("resolution", new[] { f.Width, f.Height });
                            break;
                        }
                        case "shadows":
                        {
                            var vc = c.Runtime.GetPart<SceneViewCamera>();
                            real[b] = new BridgeReply().With("renderShadows", vc != null && vc.Setup.RenderShadows);
                            break;
                        }
                    }
                }
            }
            real["renderer"] = new BridgeReply().With("postProcessing", SceneEffects.RendererPostProcessing).With("ssao", AmbientOcclusionControl.FindFeature() != null)
                .With("pipeline", GraphicsSettings.currentRenderPipeline ? GraphicsSettings.currentRenderPipeline.name : null);
            RealizedExtensions?.Invoke(c, real);
            var r = new BridgeReply().With("effects", values).With("realized", real);
            r["findings"] = CommandKit.Findings(c.Session.Findings, f => f.Code != null && (f.Code.StartsWith("effects.", StringComparison.Ordinal) || f.Code.StartsWith("fog.", StringComparison.Ordinal)));
            if (c.Args.Get("timing", false))
            {
                var bg = SceneEffects.BaseGradeOf(c.Services?.Hs2Root);
                var t = new BridgeReply().With("bakeMs", part?.LastBakeMs ?? 0.0).With("baseGradeMs", bg.Milliseconds);
                FrameTimingManager.CaptureFrameTimings();
                var ft = new FrameTiming[1];
                if (FrameTimingManager.GetLatestTimings(1, ft) > 0) t["gpuFrameMs"] = ft[0].gpuFrameTime; else t["gpuFrameMs"] = null;
                t["cpuFrameMs"] = Time.unscaledDeltaTime * 1000.0;
                r["timing"] = t;
            }
            return r;
        }

        // ------------------------------------------------------------ effects.set

        static object Set(CoreContext c)
        {
            if (c.Args.Kind("set") != ArgKind.Object) throw CommandKit.BadArgs("set", ArgTypes.Object);
            var set = c.Args.Nested("set");
            if (set == null || set.Names.Count == 0) throw CommandKit.BadArgs("set", ArgTypes.Object, "set names no field");
            var model = c.Session.Model;
            var plan = new List<(string path, object value)>();
            foreach (var name in set.Names)
            {
                string rel = name.StartsWith("effects.", StringComparison.Ordinal) ? name.Substring(8) : name;
                string full = FieldPath.Canonical("effects." + rel);
                if (full == null) throw CommandKit.BadArgs("set." + name, "a path under effects.", "\"" + name + "\" is not a field path");
                string block = SceneEffects.SecondSegment(full);
                if (block == null || SceneEffects.BlockIndex(block) < 0) throw CommandKit.BadArgs("set." + name, ArgTypes.Enum(SceneEffects.BlockNames), "\"" + name + "\" names no effect block");
                object v;
                try { v = FieldJson.FromText(set.RawJson(name), FieldAccessor.LeafType(model, null, full)); }
                catch (CommandException e) { throw new BridgeException(e.Code == CommandCodes.NotFound ? BridgeCodes.BadArgs : e.Code, "set." + name + ": " + e.Message, new BridgeErrorDetail { Arg = "set." + name }); }
                catch (Exception e) { throw CommandKit.BadArgs("set." + name, "the save-format JSON form of " + full, e.Message); }
                plan.Add((full, v));
            }
            var r = CommandKit.Check(c, CommandKit.Ops(c).SetEffects(plan, CommandKit.Exec(c, c.Args.Get<string>("label"))));
            return new BridgeReply().With("changed", r.Changed).With("noop", r.NoOp);
        }

        // ------------------------------------------------------------ effects.reset

        /// <summary>A block with ScreenEffectDefine's defaults (a fresh document object).</summary>
        public static DocObject DefaultBlock(string name) => StudioOps.DefaultEffectBlock(name);

        static object Reset(CoreContext c)
        {
            var blocks = Blocks(c, "blocks");
            var r = CommandKit.Check(c, CommandKit.Ops(c).ResetEffects(blocks, CommandKit.Exec(c, c.Args.Get<string>("label"))));
            return new BridgeReply().With("changed", r.Changed);
        }
    }
}

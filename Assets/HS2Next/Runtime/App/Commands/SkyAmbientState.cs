// The realized sky / ambient / quality state of Phase 3.3 package S: effects.state's
// realized.ambient and realized.sky (through EffectCommands.RealizedExtensions; Hook is called once by
// CoreCommands.RegisterAll) and scene.lights' studio.ambient / studio.sky.
//   ambient  mode (the active scene's own ambient mode: Trilight without a map), realizedMode (RenderSettings.ambientMode:
//            Custom while the rule hs2 holds the probe), sky / equator / ground / intensity (RenderSettings), probe (the 27
//            coefficients of RenderSettings.ambientProbe, c × 9 + k), rule, source (colours / flat / saved / unity), holds,
//            reasserted, saved {ok, error, node, unityVersion, mode, ms} (a map's file RenderSettings), reflection
//   sky      enabled, pattern, found, clearFlags (the view camera's), background, skybox {material, shader}, sunEuler,
//            sunForward, feed (the sun feed ran), aa {mode, quality, taaQuality, experimental}, lightFalloff {mode, global}
using HS2Next.Api.Bridge;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HS2Next.App.Commands
{
    public static class SkyAmbientState
    {
        static bool hooked;

        /// <summary>Adds realized.ambient / realized.sky to effects.state (idempotent).</summary>
        public static void Hook()
        {
            if (hooked) return;
            hooked = true;
            EffectCommands.RealizedExtensions += (c, real) =>
            {
                real["ambient"] = Ambient(c);
                real["sky"] = Sky(c);
            };
        }

        static SceneSky Part(CoreContext c) => c.Runtime?.GetPart<SceneSky>();

        public static BridgeReply Ambient(CoreContext c)
        {
            var part = Part(c);
            var a = part?.Ambient;
            var sh = RenderSettings.ambientProbe;
            var saved = a?.Saved;
            return new BridgeReply()
                .With("mode", a != null ? a.SceneMode.ToString() : RenderSettings.ambientMode.ToString())
                .With("realizedMode", RenderSettings.ambientMode.ToString())
                .With("sky", CommandKit.C(RenderSettings.ambientSkyColor)).With("equator", CommandKit.C(RenderSettings.ambientEquatorColor))
                .With("ground", CommandKit.C(RenderSettings.ambientGroundColor)).With("intensity", (double)RenderSettings.ambientIntensity)
                .With("probe", AmbientSh.ToArray(sh))
                .With("rule", a?.Rule ?? RenderOptions.Ambient).With("source", a?.Source).With("holds", a != null && a.Holds())
                .With("reasserted", a?.Reasserted ?? 0)
                .With("saved", saved == null ? null : new BridgeReply().With("ok", saved.Ok).With("error", saved.Error).With("node", saved.Node)
                    .With("unityVersion", saved.UnityVersion).With("mode", saved.Ok ? saved.Mode.ToString() : null).With("ms", saved.Ms))
                .With("reflection", new BridgeReply().With("mode", RenderSettings.defaultReflectionMode.ToString())
                    .With("cubemap", RenderSettings.customReflectionTexture ? RenderSettings.customReflectionTexture.name : null)
                    .With("intensity", (double)RenderSettings.reflectionIntensity)
                    .With("defaultTexture", ReflectionProbe.defaultTexture ? ReflectionProbe.defaultTexture.name : null));
        }

        public static BridgeReply Sky(CoreContext c)
        {
            var part = Part(c);
            var st = c.Session?.Model?.Settings?.Effects?.Sky;
            var view = c.Runtime?.GetPart<SceneViewCamera>();
            var cam = view?.Camera;
            var sk = RenderSettings.skybox;
            var sun = part?.SkySun;
            UniversalAdditionalCameraData d = null;
            if (cam) cam.TryGetComponent(out d);
            return new BridgeReply()
                .With("enabled", st?.Enabled ?? false).With("pattern", st?.Pattern).With("found", part?.PatternFound ?? false)
                .With("clearFlags", cam ? cam.clearFlags.ToString() : view?.Setup.ClearFlags.ToString())
                .With("background", CommandKit.C(cam ? cam.backgroundColor : view?.Setup.Background ?? Color.black))
                .With("skybox", sk ? new BridgeReply().With("material", sk.name).With("shader", sk.shader ? sk.shader.name : null).With("owned", part != null && sk == part.SkyMaterial) : null)
                .With("sunEuler", sun ? CommandKit.D3(sun.eulerAngles) : null).With("sunForward", sun ? CommandKit.D3(sun.forward) : null)
                .With("feed", part != null && part.FeedCount > 0)
                .With("aa", new BridgeReply().With("option", RenderOptions.Aa).With("mode", d ? d.antialiasing.ToString() : null)
                    .With("quality", d ? d.antialiasingQuality.ToString() : null).With("taaQuality", d ? d.taaSettings.quality.ToString() : null)
                    .With("experimental", RenderOptions.Aa == "taa"))
                .With("lightFalloff", new BridgeReply().With("mode", LightFalloff.Mode).With("global", (double)LightFalloff.GlobalValue));
        }

        /// <summary>scene.lights' studio.ambient: the rule and where the probe came from.</summary>
        public static BridgeReply StudioAmbient(CoreContext c)
        {
            var a = Part(c)?.Ambient;
            return new BridgeReply().With("rule", a?.Rule ?? RenderOptions.Ambient).With("source", a?.Source)
                .With("mode", a?.SceneMode.ToString()).With("holds", a != null && a.Holds());
        }

        /// <summary>scene.lights' studio.sky: the stored state and the realized pattern.</summary>
        public static BridgeReply StudioSky(CoreContext c)
        {
            var st = c.Session?.Model?.Settings?.Effects?.Sky;
            if (st == null) return null;
            var part = Part(c);
            var sk = RenderSettings.skybox;
            return new BridgeReply().With("enabled", st.Enabled).With("pattern", st.Pattern).With("found", part?.PatternFound ?? false)
                .With("skybox", sk ? sk.name : null).With("sunEuler", part?.SkySun ? CommandKit.D3(part.SkySun.eulerAngles) : null);
        }
    }
}

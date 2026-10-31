// The scene panel: map, the 12 screen-effect blocks (
// "whole" sliders; the default buttons reset to the model defaults), chara light, camera slots, sound;
// background / frame are Phase 3.6 placeholders (no rows). Paths are scene paths (FieldTarget.Scene).
using System.Collections.Generic;

namespace HS2Next.UI.Core
{
    public static partial class InspectorSchemas
    {
        /// <summary>Effect block ids in the vanilla order (also the l10n keys "inspector.effects.&lt;id&gt;").</summary>
        public static readonly IReadOnlyList<string> EffectBlocks = new[]
            { "colorGrading", "ambientOcclusion", "bloom", "depthOfField", "vignette", "ssr", "reflectionProbe", "fog", "sunShafts", "shadows", "ambient", "sky" };

        static List<SchemaSection> BuildScene()
        {
            var l = new List<SchemaSection>();
            const FieldTarget S = FieldTarget.Scene;

            // Map (pose fields "0.000", unparsable restores, rotation % 360 at input)
            var map = new SchemaSection("map", "inspector.scene.section.map");
            var m = new B(map, "inspector.scene.map.", OpLabels.Map, S);
            m.Ref("map.ref", "map");
            var mp = m.Vec3("map.pos", "pos", new[] { 0.0, 0.0, 0.0 });
            mp.OpLabel = OpLabels.MapPose;
            mp.Format = "0.000";
            mp.Gate = SchemaGates.MapLoaded;
            var mr = m.Vec3("map.rot", "rot", new[] { 0.0, 0.0, 0.0 });
            mr.OpLabel = OpLabels.MapPose;
            mr.Format = "0.000";
            mr.Wrap360 = true;
            mr.Gate = SchemaGates.MapLoaded;
            var mo = m.Bool("map.options", "options");
            mo.OpLabel = OpLabels.MapOptions;
            mo.Gate = SchemaGates.MapOptions;
            var ml = m.Bool("map.lights", "lights");
            ml.OpLabel = OpLabels.MapLights;
            ml.Gate = SchemaGates.MapLights;
            l.Add(map);

            // Screen effects, one section per block (vanilla order)
            SchemaSection Block(string id, out B b)
            {
                var sec = new SchemaSection("effects." + id, "inspector.effects." + id);
                b = new B(sec, "inspector.effects." + id + ".", OpLabels.Effects, S);
                l.Add(sec);
                return sec;
            }
            string P(string id, string member) => "effects." + id + "." + member;

            Block("colorGrading", out var cg);
            cg.Ref(P("colorGrading", "lut"), "lut");
            cg.FloatClamped(P("colorGrading", "blend"), "blend", 0, 1, 0.0);
            cg.Int(P("colorGrading", "saturation"), "saturation", -100, 100, 15);
            cg.Int(P("colorGrading", "brightness"), "brightness", -100, 100, -20);
            cg.Int(P("colorGrading", "contrast"), "contrast", -100, 100, 40);

            Block("ambientOcclusion", out var ao);
            ao.Bool(P("ambientOcclusion", "enabled"), "enabled");
            ao.FloatClamped(P("ambientOcclusion", "intensity"), "intensity", 0, 1, 0.2);
            ao.FloatClamped(P("ambientOcclusion", "thickness"), "thickness", 0, 10, 2.0);
            ao.Color(P("ambientOcclusion", "color"), "color", false, new[] { 0.0, 0.0, 0.0, 1.0 });

            Block("bloom", out var bl);
            bl.Bool(P("bloom", "enabled"), "enabled");
            bl.FloatClamped(P("bloom", "intensity"), "intensity", 0, 30, 2.0);
            bl.FloatClamped(P("bloom", "threshold"), "threshold", 0, 2, 1.0);
            bl.FloatClamped(P("bloom", "softKnee"), "softKnee", 0, 1, 0.76);
            bl.Bool(P("bloom", "clamp"), "clamp");
            bl.FloatClamped(P("bloom", "diffusion"), "diffusion", 1, 10, 7.0);
            bl.Color(P("bloom", "color"), "color", false, Byte(191, 191, 191));

            Block("depthOfField", out var dof);
            dof.Bool(P("depthOfField", "enabled"), "enabled");
            dof.Node(P("depthOfField", "focus"), "focus");
            dof.FloatClamped(P("depthOfField", "focalSize"), "focalSize", 0, 2, 0.4);
            dof.FloatClamped(P("depthOfField", "aperture"), "aperture", 0, 1, 0.6);

            Block("vignette", out var vi);
            vi.Bool(P("vignette", "enabled"), "enabled");
            vi.FloatClamped(P("vignette", "intensity"), "intensity", 0, 1, 0.35);

            Block("ssr", out var ssr);
            ssr.Bool(P("ssr", "enabled"), "enabled");

            Block("reflectionProbe", out var rp);
            rp.Bool(P("reflectionProbe", "enabled"), "enabled");
            rp.Ref(P("reflectionProbe", "cubemap"), "cubemap");
            rp.FloatClamped(P("reflectionProbe", "intensity"), "intensity", 0, 2, 1.0);

            Block("fog", out var fog);
            fog.Bool(P("fog", "enabled"), "enabled");
            fog.Bool(P("fog", "excludeFarPixels"), "excludeFarPixels");
            fog.FloatClamped(P("fog", "height"), "height", 1, 100, 50.0, whole: true);
            fog.FloatClamped(P("fog", "heightDensity"), "heightDensity", 0.001, 10, 0.01);
            fog.Color(P("fog", "color"), "color", false, Byte(138, 168, 203));
            var fd = fog.FloatClamped(P("fog", "density"), "density", 0, 0.001, 0.0005);
            fd.Format = "0.00000";

            Block("sunShafts", out var sun);
            sun.Bool(P("sunShafts", "enabled"), "enabled");
            sun.Node(P("sunShafts", "caster"), "caster");
            sun.Color(P("sunShafts", "thresholdColor"), "thresholdColor", false, Byte(128, 128, 128));
            sun.Color(P("sunShafts", "color"), "color", false, new[] { 1.0, 1.0, 1.0, 1.0 });
            sun.FloatClamped(P("sunShafts", "distanceFalloff"), "distanceFalloff", 0.1, 1, 0.75);
            sun.FloatClamped(P("sunShafts", "blurSize"), "blurSize", 1, 10, 5.0, whole: true);
            sun.FloatClamped(P("sunShafts", "intensity"), "intensity", 0, 10, 5.0, whole: true);

            Block("shadows", out var sh);
            sh.Bool(P("shadows", "enabled"), "enabled");

            Block("ambient", out var am);
            am.Color(P("ambient", "sky"), "sky", false, Byte(170, 188, 243));
            am.Color(P("ambient", "equator"), "equator", false, Byte(185, 195, 205));
            am.Color(P("ambient", "ground"), "ground", false, Byte(204, 109, 41));

            Block("sky", out var sky);
            sky.Bool(P("sky", "enabled"), "enabled").OpLabel = OpLabels.Sky;
            sky.Ref(P("sky", "pattern"), "pattern").OpLabel = OpLabels.Sky;

            foreach (var sec in l)
                if (sec.Id.StartsWith("effects.", System.StringComparison.Ordinal))
                    new B(sec, "inspector.effects.", OpLabels.ResetEffects, S).Button("ops.ResetEffects:" + sec.Id.Substring(8), "reset", OpLabels.ResetEffects);

            // Chara light (intensity 0.1–2 clamped, rot x / y 0–359 whole clamped, resets 1 / 0)
            var cl = new SchemaSection("charaLight", "inspector.scene.section.charaLight");
            var c = new B(cl, "inspector.scene.charaLight.", OpLabels.CharaLight, S);
            c.Color("charaLight.color", "color", false);
            c.FloatClamped("charaLight.intensity", "intensity", 0.1, 2, 1.0);
            c.FloatClamped("charaLight.rot[0]", "rotX", 0, 359, 0.0, whole: true);
            c.FloatClamped("charaLight.rot[1]", "rotY", 0, 359, 0.0, whole: true);
            c.Bool("charaLight.shadow", "shadow");
            l.Add(cl);

            // Camera slots (ten rows: Save = one entry "Camera slot", Load = view only)
            var cs = new SchemaSection("cameraSlots", "inspector.scene.section.cameraSlots");
            var cb = new B(cs, "inspector.scene.cameraSlots.", OpLabels.CameraSlot, S);
            cb.Button("ops.SaveCameraSlot:{k}", "save", OpLabels.CameraSlot);
            cb.Button("ops.LoadCameraSlot:{k}", "load", "");
            l.Add(cs);

            // Sound (stored; no playback in 3.5, Q8)
            var so = new SchemaSection("sound", "inspector.scene.section.sound");
            var sb = new B(so, "inspector.scene.sound.", OpLabels.Sound, S);
            sb.Ref("sound.bgm.ref", "bgm");
            sb.Enum("sound.bgm.repeat", "bgmRepeat", new[] { "none", "all" }, "enum.soundRepeat.");
            sb.Bool("sound.bgm.play", "bgmPlay");
            sb.File("sound.userAudio.file", "userAudio");
            sb.Enum("sound.userAudio.repeat", "userAudioRepeat", new[] { "none", "all" }, "enum.soundRepeat.");
            sb.Bool("sound.userAudio.play", "userAudioPlay");
            l.Add(so);
            return l;
        }

        static double[] Byte(int r, int g, int b) => new[] { r / 255.0, g / 255.0, b / 255.0, 1.0 };
    }
}

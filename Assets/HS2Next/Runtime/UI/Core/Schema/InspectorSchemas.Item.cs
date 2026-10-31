// Item, light, camera and folder inspectors. Item: the HS2 fields
// with T5 (emission picker keeps each item's intensity, alpha 1), T6 (no clamp copy), T21, T22 (every field to every
// selected item; typed emission power 0–9 instead of HS2's 0–1). Per-item defaults ("@name") are ItemFeatures.defaults
// members (3.1 impl). Light: the range reset is 30 (Q-a). Camera: U34 (FOV override 1–60).
using System.Collections.Generic;

namespace HS2Next.UI.Core
{
    public static partial class InspectorSchemas
    {
        static List<SchemaSection> BuildItem()
        {
            var l = new List<SchemaSection>();

            var colors = new SchemaSection("colors", "inspector.item.section.colors");
            var c = new B(colors, "inspector.item.", OpLabels.ItemColour);
            for (int k = 0; k < 3; k++)
            {
                var main = c.Color("item.colors[" + k + "].main", "color" + (k + 1), false, "@color[" + k + "]");
                main.Gate = SchemaGates.ItemColor(k);
                var met = c.FloatClamped("item.colors[" + k + "].metallic", "metallic" + (k + 1), 0, 1, "@metallic[" + k + "]");
                met.Gate = SchemaGates.ItemMetallic(k);
                var glo = c.FloatClamped("item.colors[" + k + "].glossiness", "glossiness" + (k + 1), 0, 1, "@glossiness[" + k + "]");
                glo.Gate = SchemaGates.ItemMetallic(k);
            }
            c.Color("item.colors[3].main", "color4", true, "@glass").Gate = SchemaGates.ItemGlass;
            l.Add(colors);

            var patterns = new SchemaSection("patterns", "inspector.item.section.patterns");
            var p = new B(patterns, "inspector.item.pattern.", OpLabels.ItemPattern);
            for (int k = 0; k < 3; k++)
            {
                string pre = "item.colors[" + k + "].pattern.";
                string n = (k + 1).ToString();
                string gate = SchemaGates.ItemPattern(k);
                p.Ref(pre + "item", "item" + n).Gate = gate;
                p.File(pre + "file", "file" + n).Gate = gate;
                p.Color(pre + "color", "color" + n, true, "@patternColor[" + k + "]").Gate = gate;
                var clamp = p.Bool(pre + "clamp", "clamp" + n);
                clamp.Inverted = true;
                clamp.Gate = gate;
                // uv = (scale U, scale V, offset U, offset V): HS2 US / VS 0.01–20, UT / VT −1–1
                var us = p.FloatClamped(pre + "uv", "us" + n, 0.01, 20, "@uv[" + k + "]");
                us.Component = 0;
                us.Gate = gate;
                var vs = p.FloatClamped(pre + "uv", "vs" + n, 0.01, 20, "@uv[" + k + "]");
                vs.Component = 1;
                vs.Gate = gate;
                var ut = p.FloatClamped(pre + "uv", "ut" + n, -1, 1, "@uv[" + k + "]");
                ut.Component = 2;
                ut.Gate = gate;
                var vt = p.FloatClamped(pre + "uv", "vt" + n, -1, 1, "@uv[" + k + "]");
                vt.Component = 3;
                vt.Gate = gate;
                p.FloatClamped(pre + "rot", "rot" + n, -1, 1).Gate = gate;   // no default button (HS2)
            }
            l.Add(patterns);

            var other = new SchemaSection("material", "inspector.item.section.material");
            var o = new B(other, "inspector.item.", OpLabels.ItemAlpha);
            o.FloatClamped("item.alpha", "alpha", 0, 1).Gate = SchemaGates.ItemAlpha;
            o.Add(new FieldSpec
            {
                Path = "item.emission.color", Kind = FieldKind.ColorHdr, LabelKey = "inspector.item.emissionColor", ColorAlpha = false,
                IntensityPath = "item.emission.intensity", Default = "@emission", Gate = SchemaGates.ItemEmissionColor, OpLabel = OpLabels.ItemEmission,
            });
            var pow = o.FloatClamped("item.emission.power", "emissionPower", 0, 9, "@emission.power");
            pow.Gate = SchemaGates.ItemEmissionPower;
            pow.OpLabel = OpLabels.ItemEmission;
            var lc = o.FloatClamped("item.lightCancel", "lightCancel", 0, 1, "@lightCancel");
            lc.Gate = SchemaGates.ItemLightCancel;
            lc.OpLabel = OpLabels.ItemEmission;   // StudioOps.Labels.ForItemPath("lightCancel")
            var panel = o.File("item.panel", "panel");
            panel.Gate = SchemaGates.ItemPanel;
            panel.OpLabel = OpLabels.ItemPanel;
            // panel items (colour, UT/VT/US/VS/rot, clamp", iff PanelComponent): the image's colour,
            // uv, rotation and clamp are colors[0]; per-item defaults from ItemFeatures.Default.Panel (US / VS 1,
            // UT / VT 0); labels as StudioOps.Labels.ForItemPath (colour -> "Item colour", pattern members -> "Item pattern")
            var pc = o.Color("item.colors[0].main", "panelColor", false, "@panel.color");
            pc.Gate = SchemaGates.ItemPanel;
            pc.OpLabel = OpLabels.ItemColour;
            string[] uvNames = { "panelUs", "panelVs", "panelUt", "panelVt" };
            for (int k = 0; k < 4; k++)
            {
                var uv = k < 2 ? o.FloatClamped("item.colors[0].pattern.uv", uvNames[k], 0.01, 20, "@panel.uv")
                               : o.FloatClamped("item.colors[0].pattern.uv", uvNames[k], -1, 1, "@panel.uv");
                uv.Component = k;
                uv.Gate = SchemaGates.ItemPanel;
                uv.OpLabel = OpLabels.ItemPattern;
            }
            var pr = o.FloatClamped("item.colors[0].pattern.rot", "panelRot", -1, 1);
            pr.Gate = SchemaGates.ItemPanel;
            pr.OpLabel = OpLabels.ItemPattern;
            var pcl = o.Bool("item.colors[0].pattern.clamp", "panelClamp");
            pcl.Inverted = true;
            pcl.Gate = SchemaGates.ItemPanel;
            pcl.OpLabel = OpLabels.ItemPattern;
            l.Add(other);

            var motion = new SchemaSection("motion", "inspector.item.section.motion");
            var m = new B(motion, "inspector.item.", OpLabels.ItemFk);
            var fkOn = m.Bool("item.fk.enabled", "fk");
            fkOn.Gate = SchemaGates.ItemFk;
            var dyn = m.Bool("item.dynamicBones", "dynamicBones");
            dyn.Gate = SchemaGates.ItemDynamicBones;
            dyn.DisabledBy = "item.fk.enabled";
            dyn.OpLabel = OpLabels.ItemDynamicBones;
            var fkBone = m.Vec3("item.fk.bones[\"{bone}\"]", "fkBone");
            fkBone.Gate = SchemaGates.ItemFk;
            var opt = m.Bool("item.options[{k}]", "option");
            opt.Gate = SchemaGates.ItemOptions;
            opt.OpLabel = OpLabels.ItemOptions;
            var optAll = m.Button("ops.SetItemOptionsAll", "optionsAll", OpLabels.ItemOptions);
            optAll.Gate = SchemaGates.ItemOptions;
            var ap = m.Index("item.anime.pattern", "animePattern");
            ap.Gate = SchemaGates.ItemAnimePatterns;
            ap.OpLabel = OpLabels.ItemAnimation;
            var asp = m.FloatClamped("item.anime.speed", "animeSpeed", 0, 3, 1.0);
            asp.Gate = SchemaGates.ItemAnimated;
            asp.OpLabel = OpLabels.ItemAnimation;
            m.Button("ops.RestartItemAnimation", "animeRestart", "").Gate = SchemaGates.ItemAnimated;
            l.Add(motion);
            return l;
        }

        static List<SchemaSection> BuildLight()
        {
            var s = new SchemaSection("light", "inspector.light.section");
            var b = new B(s, "inspector.light.", OpLabels.Light);
            b.Color("light.color", "color", false);
            b.FloatClamped("light.intensity", "intensity", 0.1, 2, 1.0);
            var rp = b.Float("light.range", "range", 0.1, 100, 30.0, typedMin: 0.1);
            rp.Gate = SchemaGates.LightPoint;
            var rs = b.Float("light.range", "range", 0.5, 100, 30.0, typedMin: 0.5);
            rs.Gate = SchemaGates.LightSpot;
            b.FloatClamped("light.spotAngle", "spotAngle", 1, 179, 30.0, whole: true).Gate = SchemaGates.LightSpot;
            b.Bool("light.shadow", "shadow");
            b.Bool("light.enabled", "enabled");
            b.Bool("light.showTarget", "showTarget");
            return new List<SchemaSection> { s };
        }

        static List<SchemaSection> BuildCamera()
        {
            var s = new SchemaSection("camera", "inspector.camera.section");
            var b = new B(s, "inspector.camera.", OpLabels.Camera);
            var act = b.Bool("camera.active", "active");
            act.Applies = Applies.Primary;
            act.OpLabel = OpLabels.ActiveCamera;
            var fov = b.FloatClamped("camera.fov", "fov", 1, 60);
            fov.Nullable = true;
            b.Button("ops.SetCameraFov:null", "fovFromView", OpLabels.Camera);
            b.Button("ops.CameraFromView", "setFromView", OpLabels.Camera);
            b.Button("ops.SetActiveCamera", "lookThrough", OpLabels.ActiveCamera).Applies = Applies.Primary;
            b.Button("ops.ReleaseCamera", "free", OpLabels.ReleaseCamera);   // v0.3 trial 2: back to the free camera
            return new List<SchemaSection> { s };
        }

        // Folder: the header's name only.
        static List<SchemaSection> BuildFolder() => new List<SchemaSection>();
    }
}

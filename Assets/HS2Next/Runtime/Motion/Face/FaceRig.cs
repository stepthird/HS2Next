// Expression data of one head, read once from the head prefab's FaceBlendShape component
//.
//
// Two compat assemblies carry a field-only `FaceBlendShape` class (Assets/Compat/IL and
// Assets/Compat/AssemblyCSharp); the bundles reference either one. Both have the same field names, so
// the fields are read by reflection, once at attach (never per frame). The component itself does
// nothing at runtime; everything it describes is driven by ExpressionStage.
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace HS2Next.Motion.Face
{
    /// <summary>One driven renderer of a control: `FBSTargetInfo` (ObjTarget + PtnSet).</summary>
    public sealed class FbsTargetData
    {
        /// <summary>Name of `ObjTarget` ("o_head", "o_eyelashes", ...; "(null)" when the reference is missing).</summary>
        public string Name;
        /// <summary>The SkinnedMeshRenderer of `ObjTarget` (null when missing: the target is then skipped).</summary>
        public SkinnedMeshRenderer Renderer;
        /// <summary>Blend-shape index per pattern (`PtnSet[p].Close` / `.Open`); −1 = no shape.</summary>
        public int[] Close = Array.Empty<int>(), Open = Array.Empty<int>();

        public int PatternCount => Close.Length;
        public int BlendShapeCount => Renderer && Renderer.sharedMesh ? Renderer.sharedMesh.blendShapeCount : 0;
        public override string ToString() => $"{Name} ({PatternCount} patterns, {BlendShapeCount} shapes)";
    }

    /// <summary>One control (`FBSCtrlEyebrow`, `FBSCtrlEyes`, `FBSCtrlMouth`) as serialized.</summary>
    public sealed class FbsControlData
    {
        public FbsTargetData[] Targets = Array.Empty<FbsTargetData>();
        public float OpenMin, OpenMax = 1f, FixedRate = -0.1f;
        /// <summary>Eyebrow only (`SyncBlink`, default true).</summary>
        public bool SyncBlink = true;
        // Mouth only.
        public bool UseAdjustWidthScale;
        public Transform ObjAdjustWidthScale;
        public float RandTimeMin = 0.1f, RandTimeMax = 0.2f, RandScaleMin = 0.65f, RandScaleMax = 1f, OpenRefValue = 0.2f;

        /// <summary>`GetMaxPtn`: the PtnSet length of the first target, 0 without targets.</summary>
        public int PatternCount => Targets.Length > 0 ? Targets[0].PatternCount : 0;
    }

    /// <summary>All expression data of one head prefab.</summary>
    public sealed class FaceRig
    {
        /// <summary>"FaceBlendShape (assembly)" of the component that was read.</summary>
        public string ComponentType;
        /// <summary>`FBSBlinkControl`.</summary>
        public int BlinkFrequency = 30;
        public float BaseSpeed = 0.15f;
        public FbsControlData Eyebrow = new FbsControlData(), Eyes = new FbsControlData(), Mouth = new FbsControlData();
        /// <summary>`EyeLookController` reference set on the prefab (null on every vanilla head).</summary>
        public bool HasEyeLookController;
        public float EyeLookUpCorrect = 0.1f, EyeLookDownCorrect = 0.3f, EyeLookSideCorrect = 0.1f;

        /// <summary>The FaceBlendShape component on the head root (either compat assembly), or null.</summary>
        public static MonoBehaviour FindComponent(GameObject head)
        {
            if (!head) return null;
            foreach (var mb in head.GetComponents<MonoBehaviour>())
                if (mb && mb.GetType().Name == "FaceBlendShape") return mb;
            return null;
        }

        /// <summary>Reads the serialized fields of a FaceBlendShape component.</summary>
        public static FaceRig Read(MonoBehaviour fbs)
        {
            if (!fbs) throw new ArgumentNullException(nameof(fbs));
            var rig = new FaceRig { ComponentType = $"{fbs.GetType().Name} ({fbs.GetType().Assembly.GetName().Name})" };
            var blink = F(fbs, "BlinkCtrl");
            if (blink != null)
            {
                rig.BlinkFrequency = I(blink, "BlinkFrequency", 30);
                rig.BaseSpeed = Fl(blink, "BaseSpeed", 0.15f);
            }
            rig.Eyebrow = Control(F(fbs, "EyebrowCtrl"));
            rig.Eyes = Control(F(fbs, "EyesCtrl"));
            rig.Mouth = Control(F(fbs, "MouthCtrl"));
            var elc = F(fbs, "EyeLookController") as UnityEngine.Object;
            rig.HasEyeLookController = elc != null;   // Unity null check (missing references are fake-null)
            rig.EyeLookUpCorrect = Fl(fbs, "EyeLookUpCorrect", 0.1f);
            rig.EyeLookDownCorrect = Fl(fbs, "EyeLookDownCorrect", 0.3f);
            rig.EyeLookSideCorrect = Fl(fbs, "EyeLookSideCorrect", 0.1f);
            return rig;
        }

        static FbsControlData Control(object c)
        {
            var d = new FbsControlData();
            if (c == null) return d;
            d.OpenMin = Fl(c, "OpenMin", 0f);
            d.OpenMax = Fl(c, "OpenMax", 1f);
            d.FixedRate = Fl(c, "FixedRate", -0.1f);
            d.SyncBlink = I(c, "SyncBlink", 1) != 0;
            d.UseAdjustWidthScale = I(c, "useAjustWidthScale", 0) != 0;
            var obj = F(c, "objAdjustWidthScale") as GameObject;
            d.ObjAdjustWidthScale = obj != null ? obj.transform : null;
            d.RandTimeMin = Fl(c, "randTimeMin", 0.1f);
            d.RandTimeMax = Fl(c, "randTimeMax", 0.2f);
            d.RandScaleMin = Fl(c, "randScaleMin", 0.65f);
            d.RandScaleMax = Fl(c, "randScaleMax", 1f);
            d.OpenRefValue = Fl(c, "openRefValue", 0.2f);
            if (F(c, "FBSTarget") is IList list)
            {
                d.Targets = new FbsTargetData[list.Count];
                for (int i = 0; i < list.Count; i++) d.Targets[i] = Target(list[i]);
            }
            return d;
        }

        static FbsTargetData Target(object t)
        {
            var d = new FbsTargetData();
            var go = F(t, "ObjTarget") as GameObject;
            if (go != null)
            {
                d.Name = go.name;
                d.Renderer = go.GetComponent<SkinnedMeshRenderer>();
            }
            else d.Name = "(null)";
            if (F(t, "PtnSet") is IList ptn)
            {
                d.Close = new int[ptn.Count]; d.Open = new int[ptn.Count];
                for (int i = 0; i < ptn.Count; i++)
                {
                    d.Close[i] = ptn[i] == null ? -1 : I(ptn[i], "Close", -1);
                    d.Open[i] = ptn[i] == null ? -1 : I(ptn[i], "Open", -1);
                }
            }
            return d;
        }

        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        static object F(object o, string name) => o?.GetType().GetField(name, Flags)?.GetValue(o);
        static float Fl(object o, string name, float def) { var v = F(o, name); return v == null ? def : Convert.ToSingle(v); }
        static int I(object o, string name, int def) { var v = F(o, name); return v == null ? def : Convert.ToInt32(v); }
    }
}

// Attaches the neck look and the eye look to a freshly built character: where the components live, the data model,
// Init, the target helper objects and the Studio defaults
// for a character added from a card.
//
// The data comes from the deserialised compat holders: NeckLookControllerVer2 + NeckLookCalcVer2 on cf_J_Neck
// of p_cf_anim, EyeLookController + EyeLookCalc on the root of p_cf_head_bone. Two assemblies carry these
// classes (IL and Assembly-CSharp compat stubs); whichever type the bundle bound is read by field name through
// reflection, once per character, so both work. The holders are never driven.
using System;
using System.Collections;
using System.Reflection;
using HS2Next.Chara;
using UnityEngine;

namespace HS2Next.Motion.Look
{
    public static class LookSetup
    {
        /// <summary>Master switch for this stage family.</summary>
        public static bool Enabled = true;
        /// <summary>true: Studio "add from card" defaults (neck pattern 3 ANIMATION, eye pattern 0); false: game load defaults (neck 0 FORWARD, eye 0).</summary>
        public static bool StudioDefaults = true;

        public static void Attach(CharaContext ctx)
        {
            if (!Enabled || ctx?.Motion == null || ctx.Anim == null || ctx.HeadBone == null) return;
            var neckCtrl = FindComponent(ctx.Anim.transform, "NeckLookControllerVer2");
            var eyeCtrl = FindComponent(ctx.HeadBone.transform, "EyeLookController");

            NeckLookStage neck = null;
            if (neckCtrl != null)
            {
                var calc = Get<MonoBehaviour>(neckCtrl, "neckLookScript") ?? FindOn(neckCtrl.gameObject, "NeckLookCalcVer2");
                if (calc != null) neck = ReadNeck(neckCtrl, calc);
            }
            EyeLookStage eyes = null;
            if (eyeCtrl != null)
            {
                var calc = Get<MonoBehaviour>(eyeCtrl, "eyeLookScript") ?? FindOn(eyeCtrl.gameObject, "EyeLookCalc");
                if (calc != null) eyes = ReadEyes(ctx, eyeCtrl, calc);
            }
            if (neck == null && eyes == null) { ctx.Log?.Add("look: no neck/eye look components"); return; }

            var targets = new LookTargets(neck, eyes);
            var prep = FindComponent(ctx.Anim.transform, "Preparation");
            targets.CreateObjects(ctx, prep != null ? Get<Transform>(prep, "_lookAtTarget") : null);

            var look = ctx.Root != null ? (ctx.Root.GetComponent<CharaLook>() ?? ctx.Root.AddComponent<CharaLook>()) : null;
            look?.Bind(neck, eyes, targets);

            // At load, then as Studio adds a character: targets = camera (type 0), neck rate 1.
            targets.ChangeLookNeckTarget(0);
            targets.ChangeLookEyesTarget(0);
            if (look != null)
            {
                look.SetNeckPattern(StudioDefaults ? 3 : 0);
                look.SetEyePattern(0, force: true);
                look.NeckPatternBeforeFk = look.NeckPattern;
            }
            else
            {
                if (neck != null) { neck.PtnNo = StudioDefaults ? 3 : 0; neck.Rate = 1f; }
                if (eyes != null) eyes.PtnNo = 0;
            }

            if (neck != null) ctx.Motion.Add(neck);
            if (eyes != null) ctx.Motion.Add(eyes);
            ctx.Log?.Add("look: " +
                (neck != null ? $"neck {neck.PatternCount} patterns, {neck.Bones.Length} bones, ptn {neck.PtnNo}{(neck.Initialised ? "" : " (NOT initialised)")} [{neck.Source}]" : "neck none") + "; " +
                (eyes != null ? $"eyes {eyes.PatternCount} patterns, {eyes.Eyes.Length} eyes, ptn {eyes.PtnNo}{(eyes.Initialised ? "" : " (NOT initialised)")} [{eyes.Source}]" : "eyes none") +
                $"; targets camera, look target {(targets.LookAtTarget != null ? "found" : "none")}");
        }

        // ---------------------------------------------------------------- neck

        static NeckLookStage ReadNeck(MonoBehaviour ctrl, MonoBehaviour calc)
        {
            var s = new NeckLookStage
            {
                Source = calc.GetType().Assembly.GetName().Name,
                Controller = ctrl.transform,
                PtnNo = Get<int>(ctrl, "ptnNo"),
                Rate = Get(ctrl, "rate", 1f),
                Target = Get<Transform>(ctrl, "target"),
                CalcEnabled = Get<byte>(calc, "isEnabled") != 0,
                Aim = Get<Transform>(calc, "transformAim"),
                CalcAngle = Get<Transform>(calc, "boneCalcAngle"),
                ChangeTypeLeapTime = Get(calc, "changeTypeLeapTime", 1f),
                CalcLerp = Get(calc, "calcLerp", 1f),
                SkipCalc = Get<byte>(calc, "skipCalc") != 0,
            };
            var curve = Get<AnimationCurve>(calc, "changeTypeLerpCurve");
            if (curve != null && curve.length > 0)
                s.Curve = new AnimationCurve(curve.keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
            var bones = Get<IList>(calc, "aBones");
            s.Bones = new NeckLookStage.Bone[bones?.Count ?? 0];
            for (int i = 0; i < s.Bones.Length; i++)
            {
                var o = bones[i];
                s.Bones[i] = new NeckLookStage.Bone
                {
                    Name = Get<string>(o, "name"),
                    Reference = Get<Transform>(o, "referenceCalc"),
                    NeckBone = Get<Transform>(o, "neckBone"),
                    ControlBone = Get<Transform>(o, "controlBone"),
                };
            }
            var states = Get<IList>(calc, "neckTypeStates");
            s.States = new NeckLookStage.TypeState[states?.Count ?? 0];
            for (int i = 0; i < s.States.Length; i++)
            {
                var o = states[i];
                var ps = Get<IList>(o, "aParam");
                var st = new NeckLookStage.TypeState
                {
                    Name = Get<string>(o, "name"),
                    LeapSpeed = Get<float>(o, "leapSpeed"),
                    HAngleLimit = Get<float>(o, "hAngleLimit"),
                    VAngleLimit = Get<float>(o, "vAngleLimit"),
                    LimitBreakCorrection = Get<float>(o, "limitBreakCorrectionValue"),
                    LimitAway = Get<float>(o, "limitAway"),
                    SerializedLimitBreak = Get<byte>(o, "isLimitBreakBackup") != 0,
                    LookType = Get<int>(o, "lookType"),
                    Params = new NeckLookStage.LimitParam[ps?.Count ?? 0],
                };
                for (int k = 0; k < st.Params.Length; k++)
                {
                    var p = ps[k];
                    st.Params[k] = new NeckLookStage.LimitParam
                    {
                        Name = Get<string>(p, "name"),
                        Up = Get<float>(p, "upBendingAngle"), Down = Get<float>(p, "downBendingAngle"),
                        Min = Get<float>(p, "minBendingAngle"), Max = Get<float>(p, "maxBendingAngle"),
                    };
                }
                s.States[i] = st;
            }
            s.CaptureRest();
            s.Init();
            return s;
        }

        // ---------------------------------------------------------------- eyes

        static EyeLookStage ReadEyes(CharaContext ctx, MonoBehaviour ctrl, MonoBehaviour calc)
        {
            var s = new EyeLookStage
            {
                Source = calc.GetType().Assembly.GetName().Name,
                PtnNo = Get<int>(ctrl, "ptnNo"),
                Target = Get<Transform>(ctrl, "target"),
                RootNode = Get<Transform>(calc, "rootNode") ?? calc.transform,
                HeadLookVector = Get(calc, "headLookVector", Vector3.forward),
                HeadUpVector = Get(calc, "headUpVector", Vector3.up),
                SorasiRate = Get(calc, "sorasiRate", 1.5f),
                TargetObj = Get<GameObject>(calc, "targetObj"),
            };
            var eyes = Get<IList>(calc, "eyeObjs");
            s.Eyes = new EyeLookStage.Eye[eyes?.Count ?? 0];
            for (int i = 0; i < s.Eyes.Length; i++)
                s.Eyes[i] = new EyeLookStage.Eye { Transform = Get<Transform>(eyes[i], "eyeTransform"), LR = Get<int>(eyes[i], "eyeLR") };
            var states = Get<IList>(calc, "eyeTypeStates");
            s.States = new EyeLookStage.TypeState[states?.Count ?? 0];
            for (int i = 0; i < s.States.Length; i++)
            {
                var o = states[i];
                s.States[i] = new EyeLookStage.TypeState
                {
                    Threshold = Get<float>(o, "thresholdAngleDifference"),
                    BendingMultiplier = Get<float>(o, "bendingMultiplier"),
                    MaxAngleDifference = Get<float>(o, "maxAngleDifference"),
                    Up = Get<float>(o, "upBendingAngle"), Down = Get<float>(o, "downBendingAngle"),
                    Min = Get<float>(o, "minBendingAngle"), Max = Get<float>(o, "maxBendingAngle"),
                    LeapSpeed = Get<float>(o, "leapSpeed"),
                    ForntTagDis = Get<float>(o, "forntTagDis"),
                    NearDis = Get<float>(o, "nearDis"),
                    HAngleLimit = Get<float>(o, "hAngleLimit"),
                    VAngleLimit = Get<float>(o, "vAngleLimit"),
                    LookType = Get<int>(o, "lookType"),
                };
            }

            // references from the prefab rest pose (before parenting and face shapes).
            var rel = new Quaternion[s.Eyes.Length];
            var orig = new Quaternion[s.Eyes.Length];
            var prefab = LoadHeadBonePrefab(ctx);
            var hb = ctx.HeadBone.transform;
            string rootPath = PathFrom(hb, s.RootNode);
            var pRoot = prefab != null && rootPath != null ? FindPath(prefab.transform, rootPath) : null;
            int fromPrefab = 0;
            for (int i = 0; i < s.Eyes.Length; i++)
            {
                var eye = s.Eyes[i].Transform;
                if (eye == null) { rel[i] = Quaternion.identity; orig[i] = Quaternion.identity; continue; }
                string ep = PathFrom(hb, eye);
                var pEye = pRoot != null && ep != null ? FindPath(prefab.transform, ep) : null;
                if (pEye != null)
                {
                    var pParent = pEye.parent != null ? pEye.parent.rotation : Quaternion.identity;
                    rel[i] = Quaternion.Inverse(pParent) * pRoot.rotation;
                    orig[i] = pEye.localRotation;
                    fromPrefab++;
                }
                else
                {
                    var parent = eye.parent != null ? eye.parent.rotation : Quaternion.identity;
                    rel[i] = Quaternion.Inverse(parent) * s.RootNode.rotation;
                    orig[i] = eye.localRotation;
                }
            }
            s.ReferencesFromPrefab = fromPrefab;
            if (fromPrefab < s.Eyes.Length) ctx.Log?.Add($"look: eye references from the live pose for {s.Eyes.Length - fromPrefab} eye(s) (prefab rest pose not found)");
            s.Init(rel, orig);
            return s;
        }

        static GameObject LoadHeadBonePrefab(CharaContext ctx)
        {
            try { return ctx.Catalog?.LoadAsset<GameObject>("chara/oo_base.unity3d", "p_cf_head_bone"); }
            catch (Exception) { return null; }
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>First component under root (depth first, inactive included) whose type name is typeName.</summary>
        static MonoBehaviour FindComponent(Transform root, string typeName)
        {
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && mb.GetType().Name == typeName) return mb;
            return null;
        }

        static MonoBehaviour FindOn(GameObject go, string typeName)
        {
            foreach (var mb in go.GetComponents<MonoBehaviour>())
                if (mb != null && mb.GetType().Name == typeName) return mb;
            return null;
        }

        const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        static T Get<T>(object o, string field, T def = default)
        {
            if (o == null) return def;
            var f = o.GetType().GetField(field, Fields);
            if (f == null) return def;
            var v = f.GetValue(o);
            if (v is UnityEngine.Object uo && uo == null) return def;   // destroyed / missing reference
            return v is T t ? t : def;
        }

        static string PathFrom(Transform root, Transform t)
        {
            if (t == null) return null;
            if (t == root) return "";
            string p = t.name;
            for (var x = t.parent; x != null; x = x.parent)
            {
                if (x == root) return p;
                p = x.name + "/" + p;
            }
            return null;
        }

        static Transform FindPath(Transform root, string path) => path.Length == 0 ? root : root.Find(path);
    }
}

// Studio light node realization. The light
// list prefab under the holder at local identity, URP fallback on its meshes (the directional prefab's arrow `Model`),
// then on its first Light:
//   target   culling mask = prefab mask XOR (Map | MapNoShadow) for row target Chara, XOR Chara for Map, unchanged for All
//            (HS2's AddObjectLight rule, quirk Q-b reproduced: data-driven); rendering layers = the mask
//            (RenderingLayers.ApplyToLight).
//   values   folded like the bundle lights: colour = (r·i, g·i, b·i, 1) with i = `intensity` (gamma rgb of `color`,
//            unclamped), intensity 1; the state's colour and intensity are kept on BundleLightOriginal (updated on every
//            apply), so MainLight.Brightness and the reports read HS2's values. `range`, `spotAngle` as stored (no
//            clamping: the 3.5 UI clamps), shadows Soft / None, `enabled`. Shadow strength stays the prefab's (0.8); bias
//            from URP's pipeline settings (Built-in biases are not portable, U14); additional-light shadow tier High.
//   helpers  `showTarget`: LightHelpers (directional arrow, point circles, spot cone) on the view-only helper layer.
//   main     StudioLights (the `studio` MainLight source) refreshes after build, delete, rebuild and value changes.
// A missing row keeps the holder only (`light.missing`; vanilla drops such lights). The node's `visible` has no effect.
using System.Collections.Generic;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    public sealed class LightRealization : NodeRealization
    {
        internal LightRealization(SceneRuntime runtime, TreeNode node) : base(runtime, node) { }

        public GameObject Instance { get; private set; }
        public StudioLight Row { get; private set; }
        public Light Light { get; private set; }
        /// <summary>The prefab light's culling mask before the target rule.</summary>
        public int PrefabCullingMask { get; private set; }
        /// <summary>The helper objects (null until built).</summary>
        public LightHelpers Helpers { get; private set; }

        LightNodeState St => Node.State as LightNodeState;

        /// <summary>HS2's target rule (AddObjectLight): Chara → mask ^ (Map | MapNoShadow), Map → mask ^ Chara, All → mask.</summary>
        public static int TargetMask(int prefabMask, StudioLightTarget target)
        {
            switch (target)
            {
                case StudioLightTarget.Chara: return prefabMask ^ (1 << RenderingLayers.Map | 1 << RenderingLayers.MapNoShadow);
                case StudioLightTarget.Map: return prefabMask ^ (1 << RenderingLayers.Chara);
                default: return prefabMask;
            }
        }

        protected internal override void BuildContent()
        {
            var st = St;
            StudioLight row = null;
            if (st == null || !ContentId.TryParse(st.Ref, out var id) || id.IsEmpty || Services.Db == null || !Services.Db.TryGetLight(id, out row))
            {
                State = RealizationState.Failed;
                Detail = "light row " + st?.Ref + " missing";
                ReportOnce(FindingSeverity.Warning, "light.missing", "light " + st?.Ref + " is not in the light list: the node is kept with its holder only");
                return;
            }
            Row = row;
            var prefab = Services.Catalog.LoadAsset<GameObject>(row);
            if (!prefab)
            {
                State = RealizationState.Failed;
                Detail = "prefab " + row.BundlePath + "/" + row.FileName + " not loadable";
                Report(FindingSeverity.Error, "light.loadFailed", Detail);
                return;
            }
            var go = Object.Instantiate(prefab, Holder, false);
            go.name = prefab.name;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            Instance = go;
            ShaderMap.ApplyUrp(go);
            Light = go.GetComponentInChildren<Light>(true);
            if (Light)
            {
                PrefabCullingMask = Light.cullingMask;
                Light.cullingMask = TargetMask(PrefabCullingMask, row.TargetKind);
                RenderingLayers.ApplyToLight(Light);
                var data = Light.GetComponent<UniversalAdditionalLightData>();
                data.usePipelineSettings = true;
                // URP refuses this setter outside play mode (InvalidOperationException in edit-mode batch tests)
                if (Application.isPlaying) data.additionalLightsShadowResolutionTier = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierHigh;
                if (!Light.TryGetComponent<BundleLightOriginal>(out var o)) { o = Light.gameObject.AddComponent<BundleLightOriginal>(); o.Light = Light; }
                Helpers = new LightHelpers(this);
            }
            State = RealizationState.Built;
            ApplyValues();
        }

        void ApplyValues()
        {
            var st = St;
            if (!Light || st == null) return;
            var o = BundleLights.OriginalOf(Light);
            if (o)
            {
                o.Color = new Color(st.Color.R, st.Color.G, st.Color.B, st.Color.A);
                o.Intensity = st.Intensity;
            }
            Light.color = BundleLights.Folded(new Color(st.Color.R, st.Color.G, st.Color.B, 1f), st.Intensity);
            Light.intensity = 1f;
            Light.range = st.Range;
            Light.spotAngle = st.SpotAngle;
            Light.shadows = st.Shadow ? LightShadows.Soft : LightShadows.None;
            Light.enabled = st.Enabled;
            Helpers?.Apply(st);
            StudioLights.Changed(Runtime);
        }

        protected internal override void DestroyContent()
        {
            Helpers?.Dispose();
            Helpers = null;
            if (Instance) UnityObjects.Destroy(Instance);
            Instance = null; Light = null; Row = null;
            StudioLights.Changed(Runtime);
        }

        protected internal override void ApplyChanges(IReadOnlyCollection<string> paths)
        {
            if (Touches(paths, "light.ref"))
            {
                Runtime.RebuildContent(this);
                return;
            }
            if (Touches(paths, "light")) ApplyValues();
        }

        // Lights take no children; `visible` has no effect (vanilla).
        protected internal override void ApplyVisibility(bool visible) { }
    }
}

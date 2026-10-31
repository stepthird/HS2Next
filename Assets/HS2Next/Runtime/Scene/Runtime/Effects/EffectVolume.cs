// The effect Volume of a scene runtime (Phase 3.3 package B): one GameObject
// "Effects" under the runtime's View root on ViewSetup.EffectVolumeLayer (8, the view and capture cameras' volume mask)
// with a global Volume (priority 10, weight 1) whose profile is an owned runtime copy of the shipped template
// Resources/HS2NextEffects (every override present; components copied too, so the template asset is never written).
// Without the template (it cannot be loaded) a profile is built in code with the same overrides.
// Overrides: ColorLookup (the baked strip, contribution 1), Tonemapping (None in the hs2 grading mode, ACES in hdr),
// ColorAdjustments (neutral in hs2; the user sliders in hdr), Bloom, Vignette (black, centre, smoothness 0.15, not rounded).
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    public sealed class EffectVolume
    {
        public const string TemplateResource = "HS2NextEffects";
        public const float Priority = 10f;

        public GameObject Root { get; private set; }
        public Volume Volume { get; private set; }
        public VolumeProfile Profile { get; private set; }
        /// <summary>"template" (copied from the shipped asset) or "code" (built at run time).</summary>
        public string ProfileSource { get; private set; }

        public ColorLookup Lookup { get; private set; }
        public Tonemapping Tonemap { get; private set; }
        public ColorAdjustments Adjustments { get; private set; }
        public Bloom Bloom { get; private set; }
        public Vignette Vignette { get; private set; }

        public EffectVolume(Transform parent)
        {
            Root = new GameObject("Effects") { layer = ViewSetup.EffectVolumeLayer };
            if (parent) Root.transform.SetParent(parent, false);
            Volume = Root.AddComponent<Volume>();
            Volume.isGlobal = true;
            Volume.priority = Priority;
            Volume.weight = 1f;

            var template = Resources.Load<VolumeProfile>(TemplateResource);
            if (template)
            {
                Profile = Object.Instantiate(template);
                for (int i = 0; i < Profile.components.Count; i++)
                    if (Profile.components[i]) Profile.components[i] = Object.Instantiate(Profile.components[i]);
                ProfileSource = "template";
            }
            else
            {
                Profile = ScriptableObject.CreateInstance<VolumeProfile>();
                ProfileSource = "code";
            }
            Profile.name = "HS2Next effects (runtime)";
            Profile.hideFlags = HideFlags.DontSave;
            Lookup = Get<ColorLookup>();
            Tonemap = Get<Tonemapping>();
            Adjustments = Get<ColorAdjustments>();
            Bloom = Get<Bloom>();
            Vignette = Get<Vignette>();
            Volume.sharedProfile = Profile;

            // fixed values (HS2's Studio profile)
            Vignette.color.Override(Color.black);
            Vignette.center.Override(new Vector2(0.5f, 0.5f));
            Vignette.smoothness.Override(0.15f);
            Vignette.rounded.Override(false);
            Bloom.filter.Override(BloomFilterMode.Gaussian);
            Bloom.downscale.Override(BloomDownscaleMode.Half);
            Bloom.dirtIntensity.Override(0f);
            Lookup.contribution.Override(0f);
            Tonemap.mode.Override(TonemappingMode.None);
            SetNeutralAdjustments();
        }

        T Get<T>() where T : VolumeComponent
        {
            if (!Profile.TryGet<T>(out var c)) c = Profile.Add<T>(false);
            c.hideFlags = HideFlags.DontSave;
            c.active = true;
            return c;
        }

        public void SetNeutralAdjustments()
        {
            Adjustments.postExposure.Override(0f);
            Adjustments.contrast.Override(0f);
            Adjustments.saturation.Override(0f);
            Adjustments.hueShift.Override(0f);
            Adjustments.colorFilter.Override(Color.white);
        }

        public void Destroy()
        {
            if (Profile)
            {
                foreach (var c in Profile.components) if (c) Kill(c);
                Kill(Profile);
            }
            Kill(Root);
            Profile = null;
            Root = null;
        }

        static void Kill(Object o)
        {
            if (!o) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }
}

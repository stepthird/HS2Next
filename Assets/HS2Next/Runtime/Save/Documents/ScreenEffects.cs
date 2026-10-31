// scene.json `scene.effects`: the 12 screen-effect blocks with their 45 values;
// defaults from ScreenEffectDefine. HS2 byte colours are float32(c/255) with alpha 1. List references that HS2
// stored as dropdown indices are refs; object references (focus, sun caster) are node ids. Ranges are UI notes only.
namespace HS2Next.Save
{
    /// <summary>`effects`: blocks in schema order.</summary>
    public sealed class ScreenEffects : DocObject
    {
        public ColorGradingEffect ColorGrading = new ColorGradingEffect();
        public AmbientOcclusionEffect AmbientOcclusion = new AmbientOcclusionEffect();
        public BloomEffect Bloom = new BloomEffect();
        public DepthOfFieldEffect DepthOfField = new DepthOfFieldEffect();
        public VignetteEffect Vignette = new VignetteEffect();
        public SsrEffect Ssr = new SsrEffect();
        public ReflectionProbeEffect ReflectionProbe = new ReflectionProbeEffect();
        public FogEffect Fog = new FogEffect();
        public SunShaftsEffect SunShafts = new SunShaftsEffect();
        public ShadowsEffect Shadows = new ShadowsEffect();
        public AmbientLightEffect Ambient = new AmbientLightEffect();
        public SkyEffect Sky = new SkyEffect();

        internal override void ReadMembers(DocReader r)
        {
            ColorGrading.ReadFrom(r.Obj("colorGrading"));
            AmbientOcclusion.ReadFrom(r.Obj("ambientOcclusion"));
            Bloom.ReadFrom(r.Obj("bloom"));
            DepthOfField.ReadFrom(r.Obj("depthOfField"));
            Vignette.ReadFrom(r.Obj("vignette"));
            Ssr.ReadFrom(r.Obj("ssr"));
            ReflectionProbe.ReadFrom(r.Obj("reflectionProbe"));
            Fog.ReadFrom(r.Obj("fog"));
            SunShafts.ReadFrom(r.Obj("sunShafts"));
            Shadows.ReadFrom(r.Obj("shadows"));
            Ambient.ReadFrom(r.Obj("ambient"));
            Sky.ReadFrom(r.Obj("sky"));
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ColorGrading.WriteTo(w, "colorGrading");
            AmbientOcclusion.WriteTo(w, "ambientOcclusion");
            Bloom.WriteTo(w, "bloom");
            DepthOfField.WriteTo(w, "depthOfField");
            Vignette.WriteTo(w, "vignette");
            Ssr.WriteTo(w, "ssr");
            ReflectionProbe.WriteTo(w, "reflectionProbe");
            Fog.WriteTo(w, "fog");
            SunShafts.WriteTo(w, "sunShafts");
            Shadows.WriteTo(w, "shadows");
            Ambient.WriteTo(w, "ambient");
            Sky.WriteTo(w, "sky");
        }
    }

    /// <summary>`colorGrading` (HS2 #1–5).</summary>
    public sealed class ColorGradingEffect : DocObject
    {
        /// <summary>Filter list row; null = none.</summary>
        public string Lut = DocDefaults.NoneRef;
        /// <summary>0–1.</summary>
        public float Blend;
        /// <summary>−100–100.</summary>
        public int Saturation = 15, Brightness = -20, Contrast = 40;

        internal override void ReadMembers(DocReader r)
        {
            Lut = r.RefN("lut", Lut);
            Blend = r.F32("blend", Blend);
            Saturation = r.Int("saturation", Saturation);
            Brightness = r.Int("brightness", Brightness);
            Contrast = r.Int("contrast", Contrast);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Ref("lut", Lut);
            w.F32("blend", Blend);
            w.Int("saturation", Saturation);
            w.Int("brightness", Brightness);
            w.Int("contrast", Contrast);
        }
    }

    /// <summary>`ambientOcclusion` (#6–9).</summary>
    public sealed class AmbientOcclusionEffect : DocObject
    {
        public bool Enabled = true;
        /// <summary>0–1.</summary>
        public float Intensity = 0.2f;
        /// <summary>0–10 (`aoThicknessModeifier`).</summary>
        public float Thickness = 2f;
        public Color4 Color = new Color4(0f, 0f, 0f, 1f);

        internal override void ReadMembers(DocReader r)
        {
            Enabled = r.Bool("enabled", Enabled);
            Intensity = r.F32("intensity", Intensity);
            Thickness = r.F32("thickness", Thickness);
            Color = r.Col("color", Color);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("enabled", Enabled);
            w.F32("intensity", Intensity);
            w.F32("thickness", Thickness);
            w.Col("color", Color);
        }
    }

    /// <summary>`bloom` (#10–16).</summary>
    public sealed class BloomEffect : DocObject
    {
        public bool Enabled = true;
        /// <summary>0–30, 0–2, 0–1.</summary>
        public float Intensity = 2f, Threshold = 1f, SoftKnee = 0.76f;
        /// <summary>Sets the clamp override flag.</summary>
        public bool Clamp = true;
        /// <summary>1–10.</summary>
        public float Diffusion = 7f;
        public Color4 Color = DocDefaults.Byte(191, 191, 191);

        internal override void ReadMembers(DocReader r)
        {
            Enabled = r.Bool("enabled", Enabled);
            Intensity = r.F32("intensity", Intensity);
            Threshold = r.F32("threshold", Threshold);
            SoftKnee = r.F32("softKnee", SoftKnee);
            Clamp = r.Bool("clamp", Clamp);
            Diffusion = r.F32("diffusion", Diffusion);
            Color = r.Col("color", Color);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("enabled", Enabled);
            w.F32("intensity", Intensity);
            w.F32("threshold", Threshold);
            w.F32("softKnee", SoftKnee);
            w.Bool("clamp", Clamp);
            w.F32("diffusion", Diffusion);
            w.Col("color", Color);
        }
    }

    /// <summary>`depthOfField` (#17–20).</summary>
    public sealed class DepthOfFieldEffect : DocObject
    {
        public bool Enabled;
        /// <summary>Focus node id; null = the camera pivot. Any node kind is allowed.</summary>
        public string Focus;
        /// <summary>0–2, 0–1.</summary>
        public float FocalSize = 0.4f, Aperture = 0.6f;

        internal override void ReadMembers(DocReader r)
        {
            Enabled = r.Bool("enabled", Enabled);
            Focus = r.IdN("focus");
            FocalSize = r.F32("focalSize", FocalSize);
            Aperture = r.F32("aperture", Aperture);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("enabled", Enabled);
            w.Id("focus", Focus);
            w.F32("focalSize", FocalSize);
            w.F32("aperture", Aperture);
        }
    }

    /// <summary>`vignette` (#21–22).</summary>
    public sealed class VignetteEffect : DocObject
    {
        public bool Enabled = true;
        /// <summary>0–1.</summary>
        public float Intensity = 0.35f;

        internal override void ReadMembers(DocReader r)
        {
            Enabled = r.Bool("enabled", Enabled);
            Intensity = r.F32("intensity", Intensity);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("enabled", Enabled);
            w.F32("intensity", Intensity);
        }
    }

    /// <summary>`ssr` (#23).</summary>
    public sealed class SsrEffect : DocObject
    {
        public bool Enabled = true;

        internal override void ReadMembers(DocReader r)
        {
            Enabled = r.Bool("enabled", Enabled);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("enabled", Enabled);
        }
    }

    /// <summary>`reflectionProbe` (#24–26).</summary>
    public sealed class ReflectionProbeEffect : DocObject
    {
        public bool Enabled;
        /// <summary>Probe list row; null = none.</summary>
        public string Cubemap = DocDefaults.NoneRef;
        /// <summary>0–2.</summary>
        public float Intensity = 1f;

        internal override void ReadMembers(DocReader r)
        {
            Enabled = r.Bool("enabled", Enabled);
            Cubemap = r.RefN("cubemap", Cubemap);
            Intensity = r.F32("intensity", Intensity);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("enabled", Enabled);
            w.Ref("cubemap", Cubemap);
            w.F32("intensity", Intensity);
        }
    }

    /// <summary>`fog` (#27–32).</summary>
    public sealed class FogEffect : DocObject
    {
        public bool Enabled, ExcludeFarPixels;
        /// <summary>1–100, 0.001–10.</summary>
        public float Height = 50f, HeightDensity = 0.01f;
        public Color4 Color = DocDefaults.Byte(138, 168, 203);
        /// <summary>0–0.001.</summary>
        public float Density = 0.0005f;

        internal override void ReadMembers(DocReader r)
        {
            Enabled = r.Bool("enabled", Enabled);
            ExcludeFarPixels = r.Bool("excludeFarPixels", ExcludeFarPixels);
            Height = r.F32("height", Height);
            HeightDensity = r.F32("heightDensity", HeightDensity);
            Color = r.Col("color", Color);
            Density = r.F32("density", Density);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("enabled", Enabled);
            w.Bool("excludeFarPixels", ExcludeFarPixels);
            w.F32("height", Height);
            w.F32("heightDensity", HeightDensity);
            w.Col("color", Color);
            w.F32("density", Density);
        }
    }

    /// <summary>`sunShafts` (#33–39).</summary>
    public sealed class SunShaftsEffect : DocObject
    {
        public bool Enabled;
        /// <summary>Source node id; null = none.</summary>
        public string Caster;
        public Color4 ThresholdColor = DocDefaults.Byte(128, 128, 128);
        public Color4 Color = Color4.White;
        /// <summary>0.1–1, 1–10, 0–10.</summary>
        public float DistanceFalloff = 0.75f, BlurSize = 5f, Intensity = 5f;

        internal override void ReadMembers(DocReader r)
        {
            Enabled = r.Bool("enabled", Enabled);
            Caster = r.IdN("caster");
            ThresholdColor = r.Col("thresholdColor", ThresholdColor);
            Color = r.Col("color", Color);
            DistanceFalloff = r.F32("distanceFalloff", DistanceFalloff);
            BlurSize = r.F32("blurSize", BlurSize);
            Intensity = r.F32("intensity", Intensity);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("enabled", Enabled);
            w.Id("caster", Caster);
            w.Col("thresholdColor", ThresholdColor);
            w.Col("color", Color);
            w.F32("distanceFalloff", DistanceFalloff);
            w.F32("blurSize", BlurSize);
            w.F32("intensity", Intensity);
        }
    }

    /// <summary>`shadows` (#40, "self shadow").</summary>
    public sealed class ShadowsEffect : DocObject
    {
        public bool Enabled = true;

        internal override void ReadMembers(DocReader r)
        {
            Enabled = r.Bool("enabled", Enabled);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("enabled", Enabled);
        }
    }

    /// <summary>`ambient` (#41–43): environment lighting colours.</summary>
    public sealed class AmbientLightEffect : DocObject
    {
        public Color4 Sky = DocDefaults.Byte(170, 188, 243);
        public Color4 Equator = DocDefaults.Byte(185, 195, 205);
        public Color4 Ground = DocDefaults.Byte(204, 109, 41);

        internal override void ReadMembers(DocReader r)
        {
            Sky = r.Col("sky", Sky);
            Equator = r.Col("equator", Equator);
            Ground = r.Col("ground", Ground);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Col("sky", Sky);
            w.Col("equator", Equator);
            w.Col("ground", Ground);
        }
    }

    /// <summary>`sky` (#44–45).</summary>
    public sealed class SkyEffect : DocObject
    {
        public bool Enabled;
        /// <summary>StudioSky list row; null = none.</summary>
        public string Pattern = DocDefaults.NoneRef;

        internal override void ReadMembers(DocReader r)
        {
            Enabled = r.Bool("enabled", Enabled);
            Pattern = r.RefN("pattern", Pattern);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("enabled", Enabled);
            w.Ref("pattern", Pattern);
        }
    }
}

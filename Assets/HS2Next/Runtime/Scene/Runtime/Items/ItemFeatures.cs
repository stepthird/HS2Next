// What a built Studio item offers (HS2's inspector gates) and the per-item values
// of HS2's default buttons. Computed once per build from the root ItemComponent / ParticleComponent / PanelComponent /
// IconComponent / SEComponent, the writer's original-shader record and the instance; read by item.state and the 3.5 UI.
using System.Collections.Generic;
using HS2Next.Content.Studio;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class ItemFeatures
    {
        /// <summary>colour k (0..2) rows: some slot isColor&lt;k+1&gt;; k = 0 also with particleColor1 systems.</summary>
        public readonly bool[] Color = new bool[3];
        /// <summary>info[k].useMetallic.</summary>
        public readonly bool[] Metallic = new bool[3];
        /// <summary>some slot isPattern&lt;k+1&gt;.</summary>
        public readonly bool[] Pattern = new bool[3];
        public bool Glass, Alpha, Emission, EmissionColor, EmissionPower, LightCancel;
        public bool Panel, Particle, Icon;
        /// <summary>HS2's `check` (a colour / emission / alpha flag) and `checkGlass`.</summary>
        public bool Check, CheckGlass;
        /// <summary>FK bone names of the row found in the instance (depth-first, the root first) and the missing ones.</summary>
        public readonly List<string> FkBones = new List<string>();
        public readonly List<string> FkMissing = new List<string>();
        public bool Fk => FkBones.Count > 0;
        public int DynamicBones, Options, Lights;
        /// <summary>animeInfos: name and whether the entry is valid (name and state).</summary>
        public readonly List<(string name, string state, bool valid)> AnimePatterns = new List<(string, string, bool)>();
        public bool AnimePatternsShown;
        /// <summary>The first active Animator exists, isAnime is set and it has a controller.</summary>
        public bool Animated;
        public SoundInfo Sound;
        public readonly Defaults Default = new Defaults();

        public sealed class PanelDefaults
        {
            public Color4 Color = Color4.White;
            public Vec4 Uv = new Vec4(0f, 0f, 1f, 1f);
            public bool Clamp = true;
            public float Rot;
        }

        public sealed class SoundInfo
        {
            public string Clip;
            public bool Loop;
            public int SoundType, Rolloff;
            public float Min, Max, Volume;
        }

        /// <summary>The values of HS2's default buttons.</summary>
        public sealed class Defaults
        {
            public readonly Color4[] Color = { Color4.White, Color4.White, Color4.White };
            public readonly float[] Metallic = new float[3], Glossiness = new float[3];
            public readonly Color4[] PatternColor = { Color4.White, Color4.White, Color4.White };
            public readonly Vec4[] PatternUv = { new Vec4(0f, 0f, 1f, 1f), new Vec4(0f, 0f, 1f, 1f), new Vec4(0f, 0f, 1f, 1f) };
            public readonly bool[] PatternClamp = { true, true, true };
            public readonly float[] PatternRot = new float[3];
            /// <summary>PanelComponent defaults (panel items), else null.</summary>
            public PanelDefaults Panel;
            /// <summary>ParticleComponent.defColor01 (particle items), else null.</summary>
            public Color4? ParticleColor;
            public Color4 Glass = Color4.White;
            public ItemEmission Emission = new ItemEmission();
            public float LightCancel;
        }

        public static ItemFeatures Compute(ItemRealization it, ItemMaterialWriter w)
        {
            var f = new ItemFeatures();
            var go = it.Instance;
            if (!go) return f;
            var ic = it.Component;
            var pc = go.GetComponent<Studio.ParticleComponent>();
            var panel = go.GetComponent<Studio.PanelComponent>();
            if (ic != null)
            {
                f.Check = ItemDefaults.Check(ic);
                f.CheckGlass = ItemDefaults.CheckGlass(ic);
                f.Color[0] = ItemDefaults.AnySlot(ic, mi => mi.isColor1 != 0);
                f.Color[1] = ItemDefaults.AnySlot(ic, mi => mi.isColor2 != 0);
                f.Color[2] = ItemDefaults.AnySlot(ic, mi => mi.isColor3 != 0);
                f.Pattern[0] = ItemDefaults.AnySlot(ic, mi => mi.isPattern1 != 0);
                f.Pattern[1] = ItemDefaults.AnySlot(ic, mi => mi.isPattern2 != 0);
                f.Pattern[2] = ItemDefaults.AnySlot(ic, mi => mi.isPattern3 != 0);
                for (int k = 0; k < 3; k++) f.Metallic[k] = ic.info != null && k < ic.info.Count && ic.info[k] != null && ic.info[k].useMetallic != 0;
                f.Glass = f.CheckGlass;
                f.Alpha = ItemDefaults.AnySlot(ic, mi => mi.isAlpha != 0);
                f.Emission = ItemDefaults.AnySlot(ic, mi => mi.isEmission != 0);
                if (ic.rendererInfos != null)
                    foreach (var ri in ic.rendererInfos)
                    {
                        if (ri == null || !ri.renderer || ri.materials == null) continue;
                        var e = w?.EntryOf(ri.renderer);
                        if (e == null) continue;
                        bool normalOrAlpha = false;
                        foreach (var mi in ri.materials)
                            if (mi != null && (mi.isColor1 != 0 || mi.isColor2 != 0 || mi.isColor3 != 0 || mi.isEmission != 0 || mi.isAlpha != 0)) normalOrAlpha = true;
                        for (int i = 0; i < e.Slots.Length; i++)
                        {
                            var s = e.Slots[i];
                            if (s == null) continue;
                            var mi = i < ri.materials.Count ? ri.materials[i] : null;
                            if (mi != null && mi.isEmission != 0)
                            {
                                f.EmissionColor |= s.HasEmissionColor;
                                f.EmissionPower |= s.HasEmissionStrength;
                            }
                            if (normalOrAlpha && s.HasLightCancel) f.LightCancel = true;
                        }
                    }
                f.Options = ic.optionInfos?.Count ?? 0;
                if (ic.animeInfos != null)
                    foreach (var a in ic.animeInfos)
                    {
                        bool valid = a != null && !string.IsNullOrEmpty(a.name) && !string.IsNullOrEmpty(a.state);
                        f.AnimePatterns.Add((a?.name, a?.state, valid));
                        f.AnimePatternsShown |= valid;
                    }
                f.Animated = ic.isAnime != 0 && it.Animator && it.Animator.runtimeAnimatorController != null;
            }
            if (pc != null)
            {
                f.Particle = true;
                if (pc.particleColor1 != null && pc.particleColor1.Count > 0) f.Color[0] = true;
            }
            f.Panel = panel != null;
            var icon = go.GetComponent<Studio.IconComponent>();
            f.Icon = icon != null && icon.renderer;
            var se = go.GetComponent<Studio.Sound.SEComponent>();
            if (se != null)
                f.Sound = new SoundInfo
                {
                    Clip = se._clip ? se._clip.name : null, Loop = se._isLoop != 0, SoundType = se._soundType, Rolloff = se._type,
                    Min = se._rolloffDistance?.min ?? 0f, Max = se._rolloffDistance?.max ?? 0f, Volume = se._volume,
                };
            f.DynamicBones = it.DynamicBones.Count;
            f.Lights = go.GetComponentsInChildren<Light>(true).Length;
            if (it.Row?.Bones != null)
                foreach (var name in it.Row.Bones)
                {
                    if (string.IsNullOrEmpty(name) || f.FkBones.Contains(name) || f.FkMissing.Contains(name)) continue;
                    if (ItemDefaults.FindBone(go.transform, name)) f.FkBones.Add(name); else f.FkMissing.Add(name);
                }

            // default buttons
            var d = f.Default;
            if (ic?.info != null)
                for (int k = 0; k < 3 && k < ic.info.Count; k++)
                {
                    var info = ic.info[k];
                    if (info == null) continue;
                    d.Color[k] = C(info.defColor);
                    d.Metallic[k] = info.defMetallic;
                    d.Glossiness[k] = info.defGlossiness;
                    d.PatternColor[k] = C(info.defColorPattern);
                    d.PatternUv[k] = new Vec4(info.defUV.x, info.defUV.y, info.defUV.z, info.defUV.w);
                    d.PatternClamp[k] = info.defClamp != 0;
                    d.PatternRot[k] = info.defRot;
                }
            if (pc != null) { d.Color[0] = C(pc.defColor01); d.ParticleColor = C(pc.defColor01); }
            if (panel != null)
                d.Panel = new PanelDefaults
                {
                    Color = C(panel.defColor), Uv = new Vec4(panel.defUV.x, panel.defUV.y, panel.defUV.z, panel.defUV.w),
                    Clamp = panel.defClamp != 0, Rot = panel.defRot,
                };
            if (ic != null)
            {
                var md = w != null && w.HasDefaults ? w.Defaults : new MaterialDefaults
                {
                    Glass = ic.defGlass, EmissionColor = ic.defEmissionColor, EmissionStrength = ic.defEmissionStrength, LightCancel = ic.defLightCancel,
                };
                d.Glass = C(md.Glass);
                d.Emission = ItemDefaults.SplitEmission(md.EmissionColor);
                d.Emission.Power = md.EmissionStrength;
                d.LightCancel = md.LightCancel;
            }
            return f;
        }

        static Color4 C(Color c) => new Color4(c.r, c.g, c.b, c.a);
    }
}

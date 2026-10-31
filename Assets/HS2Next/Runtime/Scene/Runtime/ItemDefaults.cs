// The initial state of a NEW item node,
// computed from the prefab asset before the add command exists, so the command carries the full state and redo / save
// reproduce it. Read-only on the prefab: its ItemComponent, ParticleComponent, PanelComponent, shared materials and
// Transforms; no instance, before any URP mapping. Scene loads never use it (the stored state is the state).
//
//   gate (HS2 `check`): the colour block runs only when the root ItemComponent has a slot flagged isColor1/2/3, isEmission
//         or isAlpha; otherwise every colour field keeps its constructor default
//   colour block: colors[0..2] from info[i] (main defColor, pattern colour defColorPattern, metallic / glossiness, clamp /
//         uv / rot; a missing info entry keeps the defaults); colors[3].main = the re-read glass default; emission =
//         the re-read emission colour with alpha 1, split (intensity = log2 of the largest rgb
//         component above 1); power = the re-read _EmissionStrength; lightCancel = the re-read _LightCancel
//   re-read (MaterialDefaults, also used by the realization for the default buttons):
//         glass: the first renderer entry (rendererInfos order, renderer and MaterialInfo list present) with an isGlass
//                slot; each of its isGlass slots in order overwrites the default with _Color4, or _Color when the shader
//                lacks _Color4: the LAST glass slot wins
//         emission: over entries with an isEmission slot, over their isEmission slots in order, each of _EmissionColor,
//                _EmissionStrength, _LightCancel from the first slot whose shader declares it
//   glass-only items (glass flags, gate false): colors[3].main = the re-read glass default (T2; HS2 leaves white)
//   alpha 1 (the prefab's ItemComponent.alpha is not copied)
//   ParticleComponent: colour 0 main = defColor01 (independent of the gate)
//   PanelComponent: colour 0 main / uv / clamp / rot = its defaults
//   options: one true per optionInfos entry (only with an ItemComponent)
//   fk: bones[name] = the prefab's local Euler angles of every bone of the row's bone list found in the prefab (first
//       transform with exactly that name, depth-first, the root first); fk.enabled false
//   group / category = the row's
using System;
using System.Collections.Generic;
using HS2Next.Content.Studio;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public static class ItemDefaults
    {
        public static ItemNodeState FromPrefab(GameObject prefab, StudioItem row)
        {
            var s = new ItemNodeState();
            if (row != null)
            {
                s.Ref = row.Key.ToString();
                s.Group = row.Group;
                s.Category = row.Category;
            }
            if (!prefab) return s;
            var ic = prefab.GetComponent<Studio.ItemComponent>();
            if (ic != null)
            {
                var md = MaterialDefaults.Read(ic, SharedMaterial);
                if (Check(ic))
                {
                    for (int i = 0; i < 3; i++)
                    {
                        var info = ic.info != null && i < ic.info.Count ? ic.info[i] : null;
                        if (info == null) continue;
                        var c = s.Colors[i];
                        c.Main = C(info.defColor);
                        c.Metallic = info.defMetallic;
                        c.Glossiness = info.defGlossiness;
                        c.Pattern.Color = C(info.defColorPattern);
                        c.Pattern.Clamp = info.defClamp != 0;
                        c.Pattern.Uv = new Vec4(info.defUV.x, info.defUV.y, info.defUV.z, info.defUV.w);
                        c.Pattern.Rot = info.defRot;
                    }
                    s.Colors[3].Main = C(md.Glass);
                    s.Emission = SplitEmission(md.EmissionColor);
                    s.Emission.Power = md.EmissionStrength;
                    s.LightCancel = md.LightCancel;
                }
                else if (CheckGlass(ic)) s.Colors[3].Main = C(md.Glass);
                if (ic.optionInfos != null && ic.optionInfos.Count > 0)
                    for (int i = 0; i < ic.optionInfos.Count; i++) s.Options.Add(true);
            }

            var particle = prefab.GetComponent<Studio.ParticleComponent>();
            if (particle != null) s.Colors[0].Main = C(particle.defColor01);
            var panel = prefab.GetComponent<Studio.PanelComponent>();
            if (panel != null)
            {
                var c0 = s.Colors[0];
                c0.Main = C(panel.defColor);
                c0.Pattern.Uv = new Vec4(panel.defUV.x, panel.defUV.y, panel.defUV.z, panel.defUV.w);
                c0.Pattern.Clamp = panel.defClamp != 0;
                c0.Pattern.Rot = panel.defRot;
            }

            if (row?.Bones != null)
                foreach (var name in row.Bones)
                {
                    if (string.IsNullOrEmpty(name) || s.Fk.Bones.ContainsKey(name)) continue;
                    var b = FindBone(prefab.transform, name);
                    if (b) s.Fk.Bones[name] = UnityObjects.V(b.localEulerAngles);
                }
            return s;
        }

        static Material SharedMaterial(Renderer r, int slot)
        {
            var m = r.sharedMaterials;
            return slot < m.Length ? m[slot] : null;
        }

        // ------------------------------------------------------------ HS2's flags

        /// <summary>HS2's `check`: some MaterialInfo slot flagged isColor1/2/3, isEmission or isAlpha.</summary>
        public static bool Check(Studio.ItemComponent ic) =>
            AnySlot(ic, mi => mi.isColor1 != 0 || mi.isColor2 != 0 || mi.isColor3 != 0 || mi.isEmission != 0 || mi.isAlpha != 0);

        /// <summary>HS2's `checkGlass`: some MaterialInfo slot flagged isGlass.</summary>
        public static bool CheckGlass(Studio.ItemComponent ic) => AnySlot(ic, mi => mi.isGlass != 0);

        /// <summary>True when any MaterialInfo of any renderer entry satisfies <paramref name="f"/>.</summary>
        public static bool AnySlot(Studio.ItemComponent ic, System.Func<Studio.ItemComponent.MaterialInfo_S, bool> f)
        {
            if (ic == null || ic.rendererInfos == null) return false;
            foreach (var ri in ic.rendererInfos)
            {
                if (ri?.materials == null) continue;
                foreach (var mi in ri.materials) if (mi != null && f(mi)) return true;
            }
            return false;
        }

        /// <summary>The first transform named exactly <paramref name="name"/> in a depth-first search of <paramref name="root"/>, the root first (inactive included), or null.</summary>
        public static Transform FindBone(Transform root, string name)
        {
            if (!root) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var t = FindBone(root.GetChild(i), name);
                if (t) return t;
            }
            return null;
        }

        /// <summary>The save format's split of an HDR emission colour: alpha 1; intensity = log2 of the largest rgb component when above 1.</summary>
        public static ItemEmission SplitEmission(Color emission)
        {
            var e = new ItemEmission();
            float max = Mathf.Max(emission.r, Mathf.Max(emission.g, emission.b));
            if (max > 1f)
            {
                e.Intensity = Mathf.Log(max, 2f);
                e.Color = new Color4(emission.r / max, emission.g / max, emission.b / max, 1f);
            }
            else
            {
                e.Intensity = 0f;
                e.Color = new Color4(emission.r, emission.g, emission.b, 1f);
            }
            return e;
        }

        static Color4 C(Color c) => new Color4(c.r, c.g, c.b, c.a);
    }

    /// <summary>
    /// HS2's re-read of the glass and emission defaults from an item's materials (`SetGlass`, `SetEmission`
    /// 3 and 4), on the prefab's shared materials (new add) or on the instance's owned copies before the URP swap (the
    /// realization's default buttons). Declarations are read from the shader (Shader.FindPropertyIndex), values from the
    /// material.
    /// </summary>
    public struct MaterialDefaults
    {
        public Color Glass;
        public Color EmissionColor;
        public float EmissionStrength, LightCancel;
        public bool GlassFound, EmissionColorFound, EmissionStrengthFound, LightCancelFound;

        public static bool Declares(Material m, string property) => m && m.shader && m.shader.FindPropertyIndex(property) >= 0;

        /// <summary>
        /// The slot count of a renderer entry's renderer, or −1 when there is none: null, or a reference Unity reports alive
        /// but whose native object is missing (a mod prefab referencing a renderer outside its hierarchy; HS2 throws there,
        /// I6 / T18: such entries are skipped like null ones).
        /// </summary>
        public static int SlotCount(Renderer r)
        {
            if (!r) return -1;
            try { return r.sharedMaterials.Length; }
            catch (Exception) { return -1; }
        }

        /// <summary>The re-read defaults; <paramref name="material"/> gives the material of (renderer, slot) or null.</summary>
        public static MaterialDefaults Read(Studio.ItemComponent ic, System.Func<Renderer, int, Material> material)
        {
            var d = new MaterialDefaults
            {
                Glass = ic.defGlass, EmissionColor = ic.defEmissionColor,
                EmissionStrength = ic.defEmissionStrength, LightCancel = ic.defLightCancel,
            };
            if (ic.rendererInfos == null) return d;
            // glass: the first entry with a renderer, a MaterialInfo list and an isGlass slot; its last glass slot wins
            foreach (var ri in ic.rendererInfos)
            {
                if (ri == null || ri.materials == null || SlotCount(ri.renderer) < 0) continue;
                bool any = false;
                foreach (var mi in ri.materials) if (mi != null && mi.isGlass != 0) { any = true; break; }
                if (!any) continue;
                int n = SlotCount(ri.renderer);
                for (int i = 0; i < n && i < ri.materials.Count; i++)
                {
                    var mi = ri.materials[i];
                    if (mi == null || mi.isGlass == 0) continue;
                    var m = material(ri.renderer, i);
                    if (!m) continue;
                    if (Declares(m, "_Color4")) { d.Glass = m.GetColor("_Color4"); d.GlassFound = true; }
                    else if (Declares(m, "_Color")) { d.Glass = m.GetColor("_Color"); d.GlassFound = true; }
                }
                break;
            }
            // emission: each property from the first isEmission slot whose shader declares it
            foreach (var ri in ic.rendererInfos)
            {
                int n = ri == null || ri.materials == null ? -1 : SlotCount(ri.renderer);
                if (n < 0) continue;
                for (int i = 0; i < n && i < ri.materials.Count; i++)
                {
                    var mi = ri.materials[i];
                    if (mi == null || mi.isEmission == 0) continue;
                    var m = material(ri.renderer, i);
                    if (!m) continue;
                    if (!d.EmissionColorFound && Declares(m, "_EmissionColor")) { d.EmissionColor = m.GetColor("_EmissionColor"); d.EmissionColorFound = true; }
                    if (!d.EmissionStrengthFound && Declares(m, "_EmissionStrength")) { d.EmissionStrength = m.GetFloat("_EmissionStrength"); d.EmissionStrengthFound = true; }
                    if (!d.LightCancelFound && Declares(m, "_LightCancel")) { d.LightCancel = m.GetFloat("_LightCancel"); d.LightCancelFound = true; }
                }
            }
            return d;
        }
    }
}

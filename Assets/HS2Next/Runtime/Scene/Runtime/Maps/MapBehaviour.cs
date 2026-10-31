// The MapComponent's option and light blocks and the sea, as HS2 Studio's
// MapComponent.VisibleOption / VisibleLight / SetupSea:
//   a flag applies to every block of its kind: true → SetActive(false) on each non-null objectsOff entry, then
//   SetActive(true) on each non-null objectsOn entry; false the reverse; an object already in that state is skipped
//   (SetActiveIfDifferent). Applied after every load (post-load step 9) and after every change of a flag (not on pose
//   changes). The flags belong to the scene (scene.map.options / lights) and survive a map change (HS2 quirk Q-g).
//   Features (the panel toggles' gates, HS2's IsOption / IsLight): the component's optionInfos / lightInfos are non-empty.
//   Sea: one frame after the load, every non-null renderersSea entry's material copies get USINGWATERVOLUME disabled (HS2
//   makes instance copies in a LateUpdate; HS2Next's copies are the map's owned copies, MapMaterials).
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class MapBehaviour
    {
        public const string SeaKeyword = "USINGWATERVOLUME";

        /// <summary>HS2's IsOption / IsLight of the found component.</summary>
        public bool FeatureOption { get; private set; }
        public bool FeatureLight { get; private set; }
        /// <summary>Null objectsOn / objectsOff entries of all blocks (counted once per load).</summary>
        public int NullRefs { get; private set; }
        /// <summary>SetActive calls made by the last <see cref="Apply"/> (0 when everything already was in state).</summary>
        public int LastChanges { get; private set; }
        /// <summary>The flags last applied.</summary>
        public bool OptionOn { get; private set; } = true;
        public bool LightOn { get; private set; } = true;
        /// <summary>Sea renderers whose copies got the keyword disabled; material slots changed.</summary>
        public int SeaRenderers { get; private set; }
        public int SeaSlots { get; private set; }
        public bool SeaDone { get; private set; }

        Studio.MapComponent component;

        /// <summary>Takes the component found by MapStructure (null: no blocks) and counts its features and null entries.</summary>
        public void Bind(Studio.MapComponent c)
        {
            component = c;
            FeatureOption = c && c.optionInfos != null && c.optionInfos.Count > 0;
            FeatureLight = c && c.lightInfos != null && c.lightInfos.Count > 0;
            NullRefs = CountNull(c ? c.optionInfos : null) + CountNull(c ? c.lightInfos : null);
            SeaRenderers = SeaSlots = 0;
            SeaDone = false;
        }

        public void Unbind()
        {
            component = null;
            FeatureOption = FeatureLight = false;
            NullRefs = 0;
            SeaRenderers = SeaSlots = 0;
            SeaDone = false;
        }

        static int CountNull(List<Studio.MapComponent.OptionInfo_S> infos)
        {
            int n = 0;
            if (infos == null) return 0;
            foreach (var i in infos)
            {
                if (i == null) continue;
                if (i.objectsOn != null) foreach (var g in i.objectsOn) if (!g) n++;
                if (i.objectsOff != null) foreach (var g in i.objectsOff) if (!g) n++;
            }
            return n;
        }

        /// <summary>Applies both flags to every block of the bound component; returns the SetActive calls made.</summary>
        public int Apply(bool option, bool light)
        {
            OptionOn = option;
            LightOn = light;
            int n = 0;
            if (component)
            {
                n += ApplyAll(component.optionInfos, option);
                n += ApplyAll(component.lightInfos, light);
            }
            LastChanges = n;
            return n;
        }

        /// <summary>HS2's Visible setter on every block of a kind; returns the SetActive calls made.</summary>
        public static int ApplyAll(List<Studio.MapComponent.OptionInfo_S> infos, bool visible)
        {
            int n = 0;
            if (infos == null) return 0;
            foreach (var i in infos) n += ApplyBlock(i, visible);
            return n;
        }

        /// <summary>One block: visible → off-list deactivated, then on-list activated; hidden → the reverse.</summary>
        public static int ApplyBlock(Studio.MapComponent.OptionInfo_S info, bool visible)
        {
            if (info == null) return 0;
            return Set(info.objectsOff, !visible) + Set(info.objectsOn, visible);
        }

        static int Set(List<GameObject> objs, bool active)
        {
            int n = 0;
            if (objs == null) return 0;
            foreach (var g in objs)
                if (g && g.activeSelf != active) { g.SetActive(active); n++; }
            return n;
        }

        /// <summary>True when the material's keyword list names <paramref name="kw"/> (declared by its shader or not: a bundle
        /// keyword the replacement does not declare stays in the list, as HS2 keeps it on its material).</summary>
        public static bool HasKeyword(Material m, string kw) => m && System.Array.IndexOf(m.shaderKeywords, kw) >= 0;

        /// <summary>The sea step (one frame after the load): USINGWATERVOLUME off on the owned copies of the sea renderers' slots.</summary>
        public void ApplySea(IReadOnlyList<Renderer> sea, MapMaterials mats)
        {
            SeaDone = true;
            if (sea == null) return;
            var done = new HashSet<Material>();
            foreach (var r in sea)
            {
                if (!r) continue;
                SeaRenderers++;
                var ms = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < ms.Length; i++)
                {
                    var m = ms[i];
                    if (!m) continue;
                    if (mats != null && !mats.IsOwned(m)) { m = mats.Copy(m); ms[i] = m; changed = true; }
                    if (done.Add(m))
                    {
                        m.DisableKeyword(SeaKeyword);
                        // a keyword the replacement does not declare: drop it from the list itself
                        if (HasKeyword(m, SeaKeyword)) m.shaderKeywords = System.Array.FindAll(m.shaderKeywords, k => k != SeaKeyword);
                        SeaSlots++;
                    }
                }
                if (changed) r.sharedMaterials = ms;
            }
        }
    }
}

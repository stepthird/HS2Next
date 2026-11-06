// What of a materialOverrides array needs more than writing values again: a `shader` item's shader name, and every `copy`, `name`
// and `renderer` item other than `Enabled`. Two arrays with equal text differ only in values (a `float`, `color`, `vector`, `keyword`,
// `texture`, a renderer's `Enabled`, a shader item's render queue), so the change can be applied in place; any other change
// reloads the part. Items of the types that are never applied (`cubemap`, `projector`, unknown) count as values. Unity-free.
// A character's items are written before the URP mapping, which reads the material: OfMapped also holds what of an array the mapping
// decides on. A value pass does not run the mapping, so two arrays with equal OfMapped text get the same decisions by construction,
// and a change of that text reloads the part or rebuilds the Character object. It may reload where nothing would differ (a queue item
// in the material's own band): that costs a reload, as every array change did in 1.2.0, never a wrong picture. Items and the map
// are written after their mapping, so a fresh build and a pass agree there and they keep Of.
using System.Collections.Generic;
using System.Text;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    public static class MeStructure
    {
        /// <summary>The canonical text of the structural members of <paramref name="list"/>, in array order; "" when it has none
        /// (a null list too).</summary>
        public static string Of(IReadOnlyList<MaterialOverride> list)
        {
            if (list == null) return "";
            bool any = false;
            for (int i = 0; i < list.Count && !any; i++) any = IsStructural(list[i]);
            if (!any) return "";   // the common case costs no allocation
            return new UTF8Encoding(false).GetString(CanonicalJsonWriter.ToBytes(SceneDocument.EntryName, w =>
            {
                w.StartArray();
                foreach (var item in list)
                {
                    if (!IsStructural(item)) continue;
                    if (item is MeShader s)
                    {
                        w.StartObject();
                        w.Str("material", s.Material);
                        w.Str("shader", s.Shader);
                        w.EndObject();
                    }
                    else item.WriteTo(w);
                }
                w.EndArray();
            }));
        }

        /// <summary>The structure of a character's array: <see cref="Of"/> and, per item, what the URP mapping reads. Compared only with
        /// itself: nothing here is a stored original, so equal text proves equal decisions without trusting one.</summary>
        public static string OfMapped(IReadOnlyList<MaterialOverride> list)
        {
            string text = Of(list);
            if (list == null) return text;
            StringBuilder sb = null;
            foreach (var item in list)
            {
                string input = MappingInput(item);
                if (input != null) (sb ?? (sb = new StringBuilder(text))).Append('\n').Append(input);
            }
            return sb == null ? text : sb.ToString();
        }

        /// <summary>The band of a render queue as the mapping reads it: under the alpha-test queues (2450), up to the last queue that
        /// receives realtime shadows and takes no fog of its own (2500), the rest of the alpha-test queues (3000), the transparent ones.</summary>
        public static int QueueBand(int queue) => queue < 2450 ? 0 : queue <= 2500 ? 1 : queue < 3000 ? 2 : 3;

        // What one item gives the mapping; null for nothing. ShaderMap: Swap, IsCutout and Fallback read the queue; StandardInputs and Fallback
        // whether _BumpMap, _SpecGlossMap, _MetallicGlossMap hold an image and whether _EmissionColor is above 0; UnrealInputs whether
        // _MainTex holds one; ResolveFor whether _Cull or _CullMode is under 0.5, which picks the two-sided replacement.
        static string MappingInput(MaterialOverride item)
        {
            switch (item)
            {
                case MeShader s when s.RenderQueue.HasValue:
                    return "queue\t" + s.Material + "\t" + QueueBand(s.RenderQueue.Value);
                case MeTexture t when t.Image != null && (t.Property == "BumpMap" || t.Property == "SpecGlossMap" || t.Property == "MetallicGlossMap" || t.Property == "MainTex"):
                    return "image\t" + t.Material + "\t" + t.Property;
                case MeColor c when c.Property == "EmissionColor":
                    return "emission\t" + c.Material + "\t" + (c.Value.R > 0f || c.Value.G > 0f || c.Value.B > 0f ? 1 : 0);
                case MeFloat f when f.Property == "Cull" || f.Property == "CullMode":
                    return "cull\t" + f.Material + "\t" + f.Property + "\t" + (f.ValueText == null && f.Value < 0.5f ? 1 : 0);
                default:
                    return null;
            }
        }

        /// <summary>True for an item <see cref="Of"/> holds: a shader name, or a `copy`, `name` or other `renderer` item.</summary>
        public static bool IsStructural(MaterialOverride item)
        {
            switch (item)
            {
                case MeShader s: return s.Shader != null;
                case MeRenderer r: return r.Property != MeKey.EnabledProperty;
                case MeCopy _:
                case MeName _: return true;
                default: return false;
            }
        }
    }
}

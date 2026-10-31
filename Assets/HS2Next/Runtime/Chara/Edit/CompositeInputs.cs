// The inputs of one compositing pass: the creator (TexCreator: create material + RT),
// the draw material and property its RT is bound to, and the texture inputs as (list row, bundle column, asset column)
// references besides the plain values the units write into the create material. Units write the record and mark it;
// the composite step rebuilds every marked creator once per flush. The references make it possible for 5.2
// to release the inputs of characters that are not being edited and to load them again on the next edit.
using System.Collections.Generic;
using HS2Next.Content;
using UnityEngine;

namespace HS2Next.Chara.Edit
{
    public sealed class CompositeInputs
    {
        /// <summary>"create_skin_body", "create_skin detail_body", "create_skin_face", "create_skin detail_face",
        /// "create_clothes &lt;kind&gt;.&lt;group&gt;", "create_clothes detail &lt;kind&gt;.&lt;group&gt;".</summary>
        public string Name;
        public TexCreator Creator;
        /// <summary>The materials the RT is bound to and the property (skin: the draw material; clothes: the group's materials).</summary>
        public readonly List<Material> Draw = new List<Material>();
        public string DrawProperty = "_MainTex";
        /// <summary>Texture inputs by create-material property: (row, bundle column, asset column); row null = a fixed asset.</summary>
        public readonly SortedDictionary<string, (ListEntry row, string bundle, string column)> Textures =
            new SortedDictionary<string, (ListEntry, string, string)>(System.StringComparer.Ordinal);
        /// <summary>Marked by a unit that wrote an input; cleared by the rebuild.</summary>
        public bool Dirty;
        /// <summary>Rebuilds since the build (the build's own included).</summary>
        public int Rebuilds;
        /// <summary>CPU milliseconds of the last rebuild through EditContext.Rebuild (submit time; the GPU work is not
        /// waited for).</summary>
        public double LastRebuildMs;

        /// <summary>Values of create-material properties as the creator was made (Phase 4.2:
        /// the body paints' `_Texture5/6UV` and `_Texture5/6Rotator`, written back when a paint's region row does not resolve,
        /// so the result equals a fresh build whatever came before). Vectors as stored; a float property is held in x.</summary>
        public readonly Dictionary<string, Vector4> Initial = new Dictionary<string, Vector4>(System.StringComparer.Ordinal);

        public CompositeInputs(string name, TexCreator creator) { Name = name; Creator = creator; }

        /// <summary>Records the create material's current values of <paramref name="vectors"/> and <paramref name="floats"/> in
        /// <see cref="Initial"/> (call right after the creator is made, before any write).</summary>
        public void CaptureInitial(IEnumerable<string> vectors, IEnumerable<string> floats)
        {
            var m = Creator?.Mat;
            if (!m) return;
            if (vectors != null) foreach (var p in vectors) if (m.HasProperty(p)) Initial[p] = m.GetVector(p);
            if (floats != null) foreach (var p in floats) if (m.HasProperty(p)) Initial[p] = new Vector4(m.GetFloat(p), 0, 0, 0);
        }

        /// <summary>Writes the initial value of a vector property back (false when none was captured).</summary>
        public bool RestoreVector(string property)
        {
            if (!Creator?.Mat || !Initial.TryGetValue(property, out var v)) return false;
            Creator.Mat.SetVector(property, v);
            return true;
        }

        /// <summary>Writes the initial value of a float property back (false when none was captured).</summary>
        public bool RestoreFloat(string property)
        {
            if (!Creator?.Mat || !Initial.TryGetValue(property, out var v)) return false;
            Creator.Mat.SetFloat(property, v.x);
            return true;
        }

        public void Ref(string property, ListEntry row, string bundle, string column) => Textures[property] = (row, bundle, column);

        /// <summary>Rebuilds the RT (clears first) and binds it to the draw materials; returns the RT.</summary>
        public RenderTexture Rebuild()
        {
            Dirty = false;
            Rebuilds++;
            var rt = Creator.Rebuild();
            foreach (var m in Draw) if (m) m.SetTexture(DrawProperty, rt);
            return rt;
        }

        public override string ToString() => $"{Name} ({(Creator?.Target ? Creator.Target.width + "x" + Creator.Target.height : "no RT")}, {Textures.Count} texture inputs, {Rebuilds} rebuilds)";
    }
}

// The value a material edit replaced, per written property, so an edit can go back to it in place.
// One per edited object: the Character object, a hair part, a clothes part, an accessory slot, an item, the map. An entry
// exists per (material or renderer, member kind, property) and holds Pristine, the value before our first write, and Last, what
// our last write left there.
//   before a write   no entry: Pristine = the value now; a value other than Last: another writer changed it, so it is new
//                    input and becomes Pristine
//   after a write    Last = the value read back; the entry counts as seen in this pass
//   sweep            an entry of the swept kinds the pass did not see: when the value still is Last, Pristine is written; the
//                    entry goes. A value that is not Last belongs to the writer who changed it, and stays
// So a colour pass, a gloss edit, an item's colour write or a plugin's write under an edit become the new pristine value, and a
// removal shows them. Comparison is exact on what the Get functions return (per component, by bits). Nothing here is stored in
// a file. Reads are Get calls on a material the caller already asked HasProperty on.
// A colour read and written back through the linear conversion can move by an ulp, so a colour goes back through
// WriteColorExactly, which leaves the same value on the Get side. Entries recorded by a build come before the URP mapping,
// which reads and rewrites a fallback's colours: such an entry's first comparison allows those ulps (Near), and a colour going
// back on a fallback takes the fallback's one write.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HS2Next
{
    /// <summary>What a baseline entry holds.</summary>
    [Flags]
    public enum MaterialMember
    {
        None = 0, Float = 1, Color = 2, Vector = 4, Keyword = 8, Texture = 16, Offset = 32, Scale = 64, Queue = 128, Enabled = 256,
        /// <summary>Every kind a value pass writes, the renderer's enabled flag left out.</summary>
        Values = Float | Color | Vector | Keyword | Texture | Offset | Scale | Queue,
        All = Values | Enabled,
    }

    /// <summary>The baseline of one edited object.</summary>
    public sealed class MaterialBaseline
    {
        /// <summary>One stored value: a number, up to four numbers, or a texture reference.</summary>
        public struct Value
        {
            public Vector4 Numbers;
            public Texture Texture;
            // (a texture compares as the engine does: a destroyed one equals none, so an image released under an edit is not new input)
            public bool Is(Value o) => Bits(Numbers.x, o.Numbers.x) && Bits(Numbers.y, o.Numbers.y) && Bits(Numbers.z, o.Numbers.z) && Bits(Numbers.w, o.Numbers.w) &&
                                       Texture == o.Texture;
            public bool Near(Value o) => Close(Numbers.x, o.Numbers.x) && Close(Numbers.y, o.Numbers.y) && Close(Numbers.z, o.Numbers.z) && Close(Numbers.w, o.Numbers.w) &&
                                         Texture == o.Texture;
            static bool Bits(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
            static bool Close(float a, float b) => Bits(a, b) || Math.Abs(a - b) <= 1e-5f * Math.Max(1f, Math.Abs(a));
            /// <summary>A float value.</summary>
            public float Float => Numbers.x;
            /// <summary>A colour value.</summary>
            public Color Color => new Color(Numbers.x, Numbers.y, Numbers.z, Numbers.w);
            /// <summary>A vector value.</summary>
            public Vector4 Vector => Numbers;
            /// <summary>An offset or a scale.</summary>
            public Vector2 Vector2 => new Vector2(Numbers.x, Numbers.y);
            /// <summary>A keyword or the enabled flag.</summary>
            public bool Flag => Numbers.x != 0;
            /// <summary>A render queue.</summary>
            public int Queue => (int)Numbers.x;
        }

        sealed class Entry
        {
            public Object Target;
            public MaterialMember Kind;
            public string Property;
            public Value Pristine, Last;
            public int SeenIn;
            /// <summary>Recorded by a build, before the URP mapping.</summary>
            public bool FromBuild;
        }

        readonly Dictionary<(Object, MaterialMember, string), Entry> entries = new Dictionary<(Object, MaterialMember, string), Entry>();
        int pass;

        /// <summary>The object is being built: entries come before the URP mapping.</summary>
        public bool Building;

        /// <summary>The entries held now.</summary>
        public int Count => entries.Count;

        /// <summary>A pass starts: nothing is seen yet.</summary>
        public void Begin() => pass++;

        /// <summary>Before a write of <paramref name="kind"/> on <paramref name="property"/>.</summary>
        public void Before(Material m, MaterialMember kind, string property) => BeforeCore(m, kind, property);

        /// <summary>After that write.</summary>
        public void After(Material m, MaterialMember kind, string property) => AfterCore(m, kind, property);

        /// <summary>Before a write of the renderer's enabled flag.</summary>
        public void Before(Renderer r) => BeforeCore(r, MaterialMember.Enabled, "");

        /// <summary>After that write.</summary>
        public void After(Renderer r) => AfterCore(r, MaterialMember.Enabled, "");

        void BeforeCore(Object target, MaterialMember kind, string property)
        {
            if (!TryRead(target, kind, property, out var cur)) return;
            var key = (target, kind, property);
            if (!entries.TryGetValue(key, out var e))
            {
                entries[key] = new Entry { Target = target, Kind = kind, Property = property, Pristine = cur, Last = cur, SeenIn = pass - 1, FromBuild = Building };
                return;
            }
            if (Holds(e, cur)) return;
            e.Pristine = cur;
            e.FromBuild = false;
        }

        void AfterCore(Object target, MaterialMember kind, string property)
        {
            if (!entries.TryGetValue((target, kind, property), out var e) || !TryRead(target, kind, property, out var cur)) return;
            e.Last = cur;
            e.SeenIn = pass;
        }

        // The value still is our last write. An entry of a build gets one comparison across the URP mapping.
        static bool Holds(Entry e, Value cur)
        {
            if (e.FromBuild && e.Kind == MaterialMember.Color && e.Last.Near(cur)) { e.Last = cur; return true; }
            return e.Last.Is(cur);
        }

        /// <summary>The value to go back to for a written property; false when no write of it is recorded.</summary>
        public bool TryGet(Object target, MaterialMember kind, string property, out Value pristine)
        {
            pristine = default;
            if (!entries.TryGetValue((target, kind, property ?? ""), out var e)) return false;
            pristine = e.Pristine;
            return true;
        }

        /// <summary>True when an entry of a float <paramref name="property"/> of <paramref name="target"/> is held.</summary>
        public bool Holds(Object target, string property) => target && entries.ContainsKey((target, MaterialMember.Float, property));

        /// <summary>Entries of <paramref name="kinds"/> this pass did not see go back to their pristine value and leave.</summary>
        public void Sweep(MaterialMember kinds)
        {
            List<(Object, MaterialMember, string)> gone = null;
            foreach (var kv in entries)
            {
                var e = kv.Value;
                if (e.SeenIn == pass || (e.Kind & kinds) == 0) continue;
                (gone ?? (gone = new List<(Object, MaterialMember, string)>())).Add(kv.Key);
                if (e.Target && TryRead(e.Target, e.Kind, e.Property, out var cur) && Holds(e, cur))
                {
                    Restore(e);
                    if (e.Kind == MaterialMember.Queue) QueueBack((Material)e.Target);
                }
            }
            if (gone != null) foreach (var k in gone) entries.Remove(k);
        }

        // A queue went back: the shadow switch and the fog stamp follow it, unless an item on one of them ran in this pass and decides it.
        void QueueBack(Material m)
        {
            bool Ran(MaterialMember kind, string property) => entries.TryGetValue((m, kind, property), out var e) && e.SeenIn == pass;
            MaterialWrites.FollowQueue(m, !Ran(MaterialMember.Float, HS2FogFeature.ModesProperty), !Ran(MaterialMember.Keyword, ShaderMap.ReceiveShadowsOff));
        }

        /// <summary>Entries of destroyed materials and renderers go (the object was swapped; a head swap leaves the body's).</summary>
        public void Prune()
        {
            List<(Object, MaterialMember, string)> gone = null;
            foreach (var kv in entries)
                if (!kv.Value.Target) (gone ?? (gone = new List<(Object, MaterialMember, string)>())).Add(kv.Key);
            if (gone != null) foreach (var k in gone) entries.Remove(k);
        }

        /// <summary>Forgets everything.</summary>
        public void Clear() => entries.Clear();

        /// <summary>A render texture was replaced by another of a new size: a value that names the old one names the new one.</summary>
        public void ReplaceTexture(Texture from, Texture to)
        {
            foreach (var e in entries.Values)
            {
                if (e.Kind != MaterialMember.Texture) continue;
                if (ReferenceEquals(e.Pristine.Texture, from)) e.Pristine.Texture = to;
            }
        }

        static void Restore(Entry e)
        {
            var v = e.Pristine;
            if (e.Target is Renderer r) { if (e.Kind == MaterialMember.Enabled) r.enabled = v.Flag; return; }
            var m = e.Target as Material;
            if (!m) return;
            if (e.Kind == MaterialMember.Keyword) { if (v.Flag) m.EnableKeyword(e.Property); else m.DisableKeyword(e.Property); return; }
            if (e.Kind == MaterialMember.Queue) { m.renderQueue = v.Queue; return; }
            // the fallback fixed this value, whatever the original held
            if (MaterialWrites.FallbackFixed(m, e.Property) || !m.HasProperty(e.Property)) return;
            switch (e.Kind)
            {
                case MaterialMember.Float:
                    if (PropertyType(m, e.Property) == ShaderPropertyType.Int) m.SetInteger(e.Property, (int)v.Float); else m.SetFloat(e.Property, v.Float);
                    break;
                case MaterialMember.Color:
                    MaterialWrites.RestoreColor(m, e.Property, v.Color, e.FromBuild);
                    break;
                case MaterialMember.Vector:
                    m.SetVector(e.Property, v.Vector);
                    break;
                case MaterialMember.Texture:
                    m.SetTexture(e.Property, v.Texture);
                    if (MaterialWrites.MirrorsBaseMap(m, e.Property)) m.SetTexture("_BaseMap", v.Texture);
                    break;
                case MaterialMember.Offset:
                    m.SetTextureOffset(e.Property, v.Vector2);
                    if (MaterialWrites.MirrorsBaseMap(m, e.Property)) m.SetTextureOffset("_BaseMap", v.Vector2);
                    break;
                case MaterialMember.Scale:
                    m.SetTextureScale(e.Property, v.Vector2);
                    if (MaterialWrites.MirrorsBaseMap(m, e.Property)) m.SetTextureScale("_BaseMap", v.Vector2);
                    break;
            }
        }

        static ShaderPropertyType? PropertyType(Material m, string property)
        {
            var sh = m.shader;
            if (!sh) return null;
            int i = sh.FindPropertyIndex(property);
            return i >= 0 ? sh.GetPropertyType(i) : (ShaderPropertyType?)null;
        }

        /// <summary>The value on the material or renderer now, read the way the kind is written; false when the target has no
        /// such value of that type (a float item on a colour property reads nothing, so nothing can go back).</summary>
        public static bool TryRead(Object target, MaterialMember kind, string property, out Value value)
        {
            value = default;
            if (target is Renderer r)
            {
                if (kind != MaterialMember.Enabled || !r) return false;
                value.Numbers.x = r.enabled ? 1 : 0;
                return true;
            }
            var m = target as Material;
            if (!m) return false;
            switch (kind)
            {
                case MaterialMember.Keyword:
                    value.Numbers.x = m.IsKeywordEnabled(property) ? 1 : 0;
                    return true;
                case MaterialMember.Queue:
                    value.Numbers.x = m.renderQueue;
                    return true;
            }
            var type = PropertyType(m, property);
            if (type == null) return false;
            switch (kind)
            {
                case MaterialMember.Float:
                    if (type == ShaderPropertyType.Int) { value.Numbers.x = m.GetInteger(property); return true; }
                    if (type != ShaderPropertyType.Float && type != ShaderPropertyType.Range) return false;
                    value.Numbers.x = m.GetFloat(property);
                    return true;
                case MaterialMember.Color:
                    if (type != ShaderPropertyType.Color && type != ShaderPropertyType.Vector) return false;
                    value.Numbers = (Vector4)m.GetColor(property);
                    return true;
                case MaterialMember.Vector:
                    if (type != ShaderPropertyType.Color && type != ShaderPropertyType.Vector) return false;
                    value.Numbers = m.GetVector(property);
                    return true;
                case MaterialMember.Texture:
                    if (type != ShaderPropertyType.Texture) return false;
                    value.Texture = m.GetTexture(property);
                    return true;
                case MaterialMember.Offset:
                    if (type != ShaderPropertyType.Texture) return false;
                    value.Numbers = m.GetTextureOffset(property);
                    return true;
                case MaterialMember.Scale:
                    if (type != ShaderPropertyType.Texture) return false;
                    value.Numbers = m.GetTextureScale(property);
                    return true;
            }
            return false;
        }
    }
}

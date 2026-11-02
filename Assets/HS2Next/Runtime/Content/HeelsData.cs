// Heel data of shoes, with the Heelz plugin's semantics (behaviour only): a zipmod whose
// manifest.xml root has <AI_HeelsData> lists <heel id="n"> entries for its female shoe rows (category 247): a body lift
// (<root vec="x,y,z"/>, only y is used) and per foot joint (<foot01> ankle, <foot02> foot, <toes01> toes) a move, a roll with
// optional min / max clamps, a scale and a "fixed" flag. Entries register in manifest order; an id that is no female shoe
// row of the zipmod, or one registered twice, ends the registration of that manifest. Vectors parse as the plugin parses
// them ("x,y,z", three floats, more ignored; anything else gives the default).
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace HS2Next.Content
{
    public static class HeelsData
    {
        public const string Element = "AI_HeelsData";
        public const int FemaleShoesCategory = 247;

        /// <summary>One foot joint's values.</summary>
        public struct Joint
        {
            /// <summary>"fixed": the joint first takes its parent's world rotation.</summary>
            public bool Fixed;
            public Vector3 Move, Roll, Scale, RollMin, RollMax;
        }

        /// <summary>One shoe's heel data.</summary>
        public sealed class Heel
        {
            /// <summary>The root offset; its y lifts the body.</summary>
            public Vector3 Root;
            /// <summary>foot01, foot02, toes01 (a missing element leaves the joint all zero, scale included, as in the plugin).</summary>
            public Joint Ankle, Foot, Toes;
        }

        static readonly Dictionary<ZipmodInfo, Dictionary<int, Heel>> cache = new Dictionary<ZipmodInfo, Dictionary<int, Heel>>();

        /// <summary>The heel data of <paramref name="shoes"/> (a female shoe row of a zipmod with heel data), or null.</summary>
        public static Heel For(ListEntry shoes)
        {
            var z = shoes?.Zipmod;
            if (z == null || z.Guid == null || shoes.Category != FemaleShoesCategory || !z.HasManifestElement(Element)) return null;
            if (!cache.TryGetValue(z, out var table))
            {
                var ids = new HashSet<int>();
                foreach (var e in z.Entries) if (e.Category == FemaleShoesCategory) ids.Add(e.Key.Id);
                cache[z] = table = Read(z.ReadManifestElement(Element), ids);
            }
            return table.TryGetValue(shoes.Key.Id, out var h) ? h : null;
        }

        /// <summary>The registered heels of one element's outer XML, given the zipmod's female shoe ids.</summary>
        public static Dictionary<int, Heel> Read(string outerXml, ICollection<int> shoeIds)
        {
            var table = new Dictionary<int, Heel>();
            if (string.IsNullOrEmpty(outerXml)) return table;
            var doc = new System.Xml.XmlDocument();
            try { doc.LoadXml(outerXml); }
            catch (System.Xml.XmlException) { return table; }
            if (doc.DocumentElement == null) return table;
            foreach (System.Xml.XmlNode node in doc.DocumentElement.ChildNodes)
            {
                if (!(node is System.Xml.XmlElement e) || e.Name != "heel") continue;
                string id = e.HasAttribute("id") ? e.GetAttribute("id") : "-1";
                // the plugin throws on an id that is no integer (its whole load stops); here that manifest stops
                if (!int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) break;
                if (n <= -1) continue;
                if (!shoeIds.Contains(n) || table.ContainsKey(n)) break;
                table[n] = Parse(e);
            }
            return table;
        }

        static Heel Parse(System.Xml.XmlElement e)
        {
            var h = new Heel { Root = Vec(Child(e, "root"), "vec", Vector3.zero) };
            var a = Child(e, "foot01"); if (a != null) h.Ankle = JointOf(a);
            var f = Child(e, "foot02"); if (f != null) h.Foot = JointOf(f);
            var t = Child(e, "toes01"); if (t != null) h.Toes = JointOf(t);
            return h;
        }

        static Joint JointOf(System.Xml.XmlElement p)
        {
            var roll = Child(p, "roll");
            return new Joint
            {
                Move = Vec(Child(p, "move"), "vec", Vector3.zero),
                Scale = Vec(Child(p, "scale"), "vec", Vector3.one),
                Roll = Vec(roll, "vec", Vector3.zero),
                RollMin = Vec(roll, "min", Vector3.zero),
                RollMax = Vec(roll, "max", Vector3.zero),
                Fixed = p.HasAttribute("fixed"),
            };
        }

        static System.Xml.XmlElement Child(System.Xml.XmlElement e, string name)
        {
            foreach (System.Xml.XmlNode n in e.ChildNodes) if (n is System.Xml.XmlElement c && c.Name == name) return c;
            return null;
        }

        /// <summary>"x,y,z" as the plugin parses it: three comma-separated floats (more ignored), else the default.</summary>
        public static Vector3 Vec(System.Xml.XmlElement e, string attr, Vector3 def)
        {
            if (e == null || !e.HasAttribute(attr)) return def;
            var p = e.GetAttribute(attr).Split(',');
            if (p.Length < 3) return def;
            const NumberStyles S = NumberStyles.Float | NumberStyles.AllowThousands;
            var c = CultureInfo.InvariantCulture;
            return float.TryParse(p[0], S, c, out float x) && float.TryParse(p[1], S, c, out float y) && float.TryParse(p[2], S, c, out float z)
                ? new Vector3(x, y, z) : def;
        }

        /// <summary>Forgets the parsed tables (a rescan replaces the zipmods).</summary>
        public static void Clear() => cache.Clear();
    }
}

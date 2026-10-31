// MoreAccessories (`moreAccessories`): accessory slots 20+ as an XML string.
using System;
using System.Collections.Generic;
using System.Xml;
using HS2Next.IO;
using UnityEngine;

namespace HS2Next.Data.Ext
{
    public class MoreAccessoriesData
    {
        public const int FirstSlot = 20, EmptyType = 350;

        public int Version;
        /// <summary>The root's `version` attribute (informational, e.g. "1.2.2"); null when absent.</summary>
        public string XmlVersion;
        /// <summary>The original XML text.</summary>
        public string Xml;
        /// <summary>Element i (document order) is accessory slot <see cref="FirstSlot"/> + i; empty slots included.</summary>
        public List<AccessoryPart> Parts = new List<AccessoryPart>();

        /// <summary>Number of parts with a type other than 350.</summary>
        public int UsedCount { get { int n = 0; foreach (var p in Parts) if (!p.IsEmpty) n++; return n; } }

        /// <summary>One extra slot (= ChaFileAccessory.PartsInfo fields that MoreAccessories stores). Defaults.</summary>
        public class AccessoryPart
        {
            /// <summary>Absolute accessory slot (≥ 20).</summary>
            public int Slot;
            /// <summary>Category 350 (none) or 351..363.</summary>
            public int Type = EmptyType;
            /// <summary>List id (mod items: the zipmod-local id; resolve with property accessory&lt;Slot&gt;).</summary>
            public int Id;
            public string ParentKey;
            /// <summary>[correction j (0,1), k (0 position, 1 rotation, 2 scale)].</summary>
            public Vector3[,] AddMove = DefaultMove();
            public Color[] Color = { UnityEngine.Color.white, UnityEngine.Color.white, UnityEngine.Color.white, UnityEngine.Color.white };
            public float[] GlossPower = new float[4], MetallicPower = new float[4], SmoothnessPower = new float[4];
            public int HideCategory;
            /// <summary>As read. Quirk: MoreAccessories 1.2.2 writes the hideCategory value here.</summary>
            public int HideTiming = 1;
            public bool NoShake;
            /// <summary>Studio visibility; null when not stored (cards never store it).</summary>
            public bool? Show;
            /// <summary>Attributes that were expected (type ≠ 350) but missing and defaulted.</summary>
            public int MissingAttributes;

            public bool IsEmpty => Type == EmptyType || Type < 351 || Type > 363;

            static Vector3[,] DefaultMove()
            {
                var m = new Vector3[2, 3];
                for (int j = 0; j < 2; j++) m[j, 2] = Vector3.one;
                return m;
            }
        }

        public static MoreAccessoriesData Decode(int version, object data, ExtContext ctx)
        {
            var xml = data.Get("additionalAccessories").Str();
            var r = new MoreAccessoriesData { Version = version, Xml = xml };
            if (version != 0) ctx.Warn(PluginIds.MoreAccessories, $"unknown version {version}");
            if (string.IsNullOrEmpty(xml)) return r;
            var doc = new XmlDocument();
            try { doc.LoadXml(xml); }
            catch (XmlException e) { ctx.Warn(PluginIds.MoreAccessories, "XML not readable: " + e.Message); return r; }
            var root = doc.DocumentElement;
            if (root == null) return r;
            if (root.HasAttribute("version")) r.XmlVersion = root.GetAttribute("version");
            int i = 0;
            foreach (XmlNode n in root.ChildNodes)
            {
                if (!(n is XmlElement e) || e.Name != "accessory") continue;
                var part = new AccessoryPart { Slot = FirstSlot + i++ };
                part.Type = Int(e, "type", EmptyType, part, true);
                if (e.HasAttribute("show")) part.Show = Bool(e, "show", true, part, false);
                if (part.Type != EmptyType)
                {
                    part.Id = Int(e, "id", 0, part, true);
                    if (e.HasAttribute("parentKey")) part.ParentKey = e.GetAttribute("parentKey"); else part.MissingAttributes++;
                    for (int j = 0; j < 2; j++)
                        for (int k = 0; k < 3; k++)
                        {
                            var d = part.AddMove[j, k];
                            string b = "addMove" + j + k;
                            part.AddMove[j, k] = new Vector3(Float(e, b + "x", d.x, part), Float(e, b + "y", d.y, part), Float(e, b + "z", d.z, part));
                        }
                    for (int l = 0; l < 4; l++)
                    {
                        var c = part.Color[l];
                        string b = "color" + l;
                        part.Color[l] = new Color(Float(e, b + "r", c.r, part), Float(e, b + "g", c.g, part), Float(e, b + "b", c.b, part), Float(e, b + "a", c.a, part));
                        part.GlossPower[l] = Float(e, "glossPower" + l, 0, part);
                        part.MetallicPower[l] = Float(e, "metallicPower" + l, 0, part);
                        part.SmoothnessPower[l] = Float(e, "smoothnessPower" + l, 0, part);
                    }
                    part.HideCategory = Int(e, "hideCategory", 0, part, true);
                    part.HideTiming = Int(e, "hideTiming", 1, part, true);
                    part.NoShake = Bool(e, "noShake", false, part, true);
                    if (part.MissingAttributes > 0) ctx.Count("moreAccessories.missingAttributes", part.MissingAttributes);
                }
                r.Parts.Add(part);
            }
            return r;
        }

        static float Float(XmlElement e, string name, float def, AccessoryPart p)
        {
            if (!e.HasAttribute(name)) { p.MissingAttributes++; return def; }
            try { return XmlConvert.ToSingle(e.GetAttribute(name)); } catch (Exception) { p.MissingAttributes++; return def; }
        }

        static int Int(XmlElement e, string name, int def, AccessoryPart p, bool required)
        {
            if (!e.HasAttribute(name)) { if (required) p.MissingAttributes++; return def; }
            try { return XmlConvert.ToInt32(e.GetAttribute(name)); } catch (Exception) { p.MissingAttributes++; return def; }
        }

        static bool Bool(XmlElement e, string name, bool def, AccessoryPart p, bool required)
        {
            if (!e.HasAttribute(name)) { if (required) p.MissingAttributes++; return def; }
            try { return XmlConvert.ToBoolean(e.GetAttribute(name)); } catch (Exception) { p.MissingAttributes++; return def; }
        }
    }
}

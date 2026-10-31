// ABMX bone modifiers (`KKABMPlugin.ABMData`): data["boneData"] = LZ4 (or plain)
// MessagePack array of BoneModifier arrays. Character cards only; only PluginData version 2 is read.
using System.Collections.Generic;
using HS2Next.IO;
using UnityEngine;

namespace HS2Next.Data.Ext
{
    public class AbmxData
    {
        public const int SupportedVersion = 2;

        public int Version;
        public List<BoneModifier> Modifiers = new List<BoneModifier>();

        /// <summary>BoneLocation: 0 Unknown (body first, then accessories 0..19), 1 BodyTop, 10..29 = accessory slot + 10.</summary>
        public enum BoneLocation { Unknown = 0, BodyTop = 1, Accessory0 = 10, Accessory19 = 29 }

        public class BoneModifier
        {
            public string BoneName;
            /// <summary>BoneLocation value; <see cref="BoneLocation.Unknown"/> when index 2 is absent.</summary>
            public int Location;
            /// <summary>True when the array had index 2.</summary>
            public bool HasLocation;
            /// <summary>At least one element; HS2 uses element 0 only (<see cref="Data"/>).</summary>
            public List<BoneModifierData> CoordinateModifiers = new List<BoneModifierData>();

            public BoneModifierData Data => CoordinateModifiers.Count > 0 ? CoordinateModifiers[0] : new BoneModifierData();
            /// <summary>Accessory slot 0..19 for locations 10..29, else -1.</summary>
            public int AccessorySlot => Location >= 10 && Location <= 29 ? Location - 10 : -1;
            public override string ToString() => $"{BoneName} loc {Location}: {Data}";
        }

        /// <summary>One modifier set (defaults = identity; trailing fields missing in old data keep the default).</summary>
        public class BoneModifierData
        {
            public Vector3 Scale = Vector3.one;
            public float Length = 1;
            public Vector3 Position = Vector3.zero;
            /// <summary>Euler degrees.</summary>
            public Vector3 Rotation = Vector3.zero;
            /// <summary>Number of array elements read (2 = old format without position/rotation).</summary>
            public int FieldCount;

            /// <summary>Exact comparison (Unity's Vector3 == is approximate; 18 card modifiers differ from 1 by less than 1e-5).</summary>
            public bool IsIdentity => Scale.x == 1 && Scale.y == 1 && Scale.z == 1 && Length == 1 &&
                Position.x == 0 && Position.y == 0 && Position.z == 0 && Rotation.x == 0 && Rotation.y == 0 && Rotation.z == 0;
            public override string ToString() => $"scale {Scale} length {Length} pos {Position} rot {Rotation}";
        }

        public static AbmxData Decode(int version, object data, ExtContext ctx)
        {
            if (version != SupportedVersion) { ctx.Warn(PluginIds.Abmx, $"version {version} not supported (only {SupportedVersion}); ignored"); return null; }
            var r = new AbmxData { Version = version };
            var list = ctx.Nested(data.Get("boneData"), PluginIds.Abmx, "boneData");
            if (list == null) return r;
            if (!(list is List<object> items)) { ctx.Warn(PluginIds.Abmx, "boneData is not an array"); return r; }
            foreach (var it in items)
            {
                if (!(it is List<object> a) || a.Count < 2 || !(a[0] is string name)) { ctx.Warn(PluginIds.Abmx, "malformed BoneModifier skipped"); continue; }
                var m = new BoneModifier { BoneName = name };
                if (a.Count > 2 && a[2] != null) { m.HasLocation = true; m.Location = V.I(a[2], 0); }
                foreach (var cm in a[1].Items())
                {
                    var d = new BoneModifierData();
                    if (cm is List<object> f)
                    {
                        d.FieldCount = f.Count;
                        if (f.Count > 0) d.Scale = V.V3(f[0], Vector3.one);
                        if (f.Count > 1) d.Length = V.F(f[1], 1);
                        if (f.Count > 2) d.Position = V.V3(f[2], Vector3.zero);
                        if (f.Count > 3) d.Rotation = V.V3(f[3], Vector3.zero);
                    }
                    m.CoordinateModifiers.Add(d);
                }
                if (m.CoordinateModifiers.Count == 0) m.CoordinateModifiers.Add(new BoneModifierData());
                r.Modifiers.Add(m);
            }
            return r;
        }
    }
}

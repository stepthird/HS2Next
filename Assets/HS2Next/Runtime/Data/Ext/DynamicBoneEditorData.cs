// DynamicBoneEditor (`com.deathweasel.bepinex.dynamicboneeditor`): accessory DynamicBone
// parameter overrides. data["AccessoryDynamicBoneData"] = plain MessagePack array of maps (or nil).
using System.Collections.Generic;
using HS2Next.IO;

namespace HS2Next.Data.Ext
{
    public class DynamicBoneEditorData
    {
        public int Version;
        public List<Entry> Entries = new List<Entry>();

        /// <summary>One DynamicBone of one accessory; null fields are not overridden.</summary>
        public class Entry
        {
            /// <summary>Always 0 in HS2.</summary>
            public int CoordinateIndex;
            /// <summary>Accessory slot (≥ 20 = MoreAccessories slot).</summary>
            public int Slot;
            /// <summary>Name of the DynamicBone's m_Root transform (selects the component).</summary>
            public string BoneName;
            /// <summary>0 None, 1 X, 2 Y, 3 Z.</summary>
            public int? FreezeAxis, FreezeAxisOriginal;
            public float? Weight, Damping, Elasticity, Stiffness, Inertia, Radius;
            /// <summary>Prefab values at first edit (UI reset only).</summary>
            public float? WeightOriginal, DampingOriginal, ElasticityOriginal, StiffnessOriginal, InertiaOriginal, RadiusOriginal;
        }

        public static DynamicBoneEditorData Decode(int version, object data, ExtContext ctx)
        {
            if (version != 0) ctx.Warn(PluginIds.DynamicBoneEditor, $"unknown version {version}");
            var r = new DynamicBoneEditorData { Version = version };
            foreach (var o in ctx.Nested(data.Get("AccessoryDynamicBoneData"), PluginIds.DynamicBoneEditor, "AccessoryDynamicBoneData").Items())
            {
                if (!(o is Dictionary<object, object>)) continue;
                r.Entries.Add(new Entry
                {
                    CoordinateIndex = V.I(o.Get("CoordinateIndex"), 0), Slot = V.I(o.Get("Slot"), 0), BoneName = o.Get("BoneName").Str(),
                    FreezeAxis = V.IN(o.Get("FreezeAxis")), FreezeAxisOriginal = V.IN(o.Get("FreezeAxisOriginal")),
                    Weight = V.FN(o.Get("Weight")), Damping = V.FN(o.Get("Damping")), Elasticity = V.FN(o.Get("Elasticity")),
                    Stiffness = V.FN(o.Get("Stiffness")), Inertia = V.FN(o.Get("Inertia")), Radius = V.FN(o.Get("Radius")),
                    WeightOriginal = V.FN(o.Get("WeightOriginal")), DampingOriginal = V.FN(o.Get("DampingOriginal")),
                    ElasticityOriginal = V.FN(o.Get("ElasticityOriginal")), StiffnessOriginal = V.FN(o.Get("StiffnessOriginal")),
                    InertiaOriginal = V.FN(o.Get("InertiaOriginal")), RadiusOriginal = V.FN(o.Get("RadiusOriginal")),
                });
            }
            return r;
        }
    }
}

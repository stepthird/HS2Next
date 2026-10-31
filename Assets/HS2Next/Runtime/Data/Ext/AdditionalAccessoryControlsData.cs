// Additional Accessory Controls (`orange.spork.additionalaccessorycontrolsplugin`).
// Character cards: accessoryData + overrideData; coordinate cards: coordinateAccessoryData + coordinateOverrideData.
// Both are plain MessagePack blobs (arrays, int keys); the raw blobs are kept for round-trip.
using System.Collections.Generic;
using HS2Next.IO;

namespace HS2Next.Data.Ext
{
    public class AdditionalAccessoryControlsData
    {
        /// <summary>AdditionalAccessoryVisibilityRules.</summary>
        public enum Rule
        {
            NONE = 0, STARTUP = 1, H_START = 2, H_END = 3, STUDIO_LOAD = 4,
            TOP = 10, BOT = 11, INNER_TOP = 12, INNER_BOT = 13, PANTYHOSE = 14, GLOVE = 15, SOCK = 16, SHOE = 17,
            ACCESSORY_LINK = 20, ACCESSORY_INVERSE_LINK = 21,
            HAIR = 30, NOSE = 31, EAR = 32, HAND = 33, FOOT = 34, EYELASH = 35,
        }

        /// <summary>AdditionalAccessoryVisibilityRulesModifiers (stored as decimal strings; for links the string is the 1-based slot).</summary>
        public enum Modifier
        {
            NONE = 0, SHOW = 1, HIDE = 2, ON = 10, HALF = 11, OFF = 12,
            HAIR_FRONT = 30, HAIR_BACK = 31, HAIR_SIDE = 32, HAIR_EXT = 33, HAIR_ACC = 34, LEFT = 40, RIGHT = 41, ALL = 50,
        }

        public int Version;
        /// <summary>One entry per accessory slot (normally 20; more with MoreAccessories).</summary>
        public List<SlotData> Slots = new List<SlotData>();
        public List<CoordinateRule> SuppressedRules = new List<CoordinateRule>();
        public List<CoordinateRule> OverrideRules = new List<CoordinateRule>();
        /// <summary>The original blobs (character: accessoryData/overrideData; coordinate: coordinateAccessoryData/coordinateOverrideData).</summary>
        public byte[] AccessoryDataRaw, OverrideDataRaw;

        public int RuleCount { get { int n = 0; foreach (var s in Slots) n += s.VisibilityRules.Count; return n; } }

        public class SlotData
        {
            public bool IsEmpty = true;
            /// <summary>0..19 base slots; ≥ 20 MoreAccessories slots.</summary>
            public int SlotNumber;
            public int OriginalSlotNumber = -1;
            /// <summary>Keep this accessory when another coordinate is loaded.</summary>
            public bool CharacterAccessory;
            /// <summary>Display name at save time (informational).</summary>
            public string AccessoryName;
            public bool AutoMatchBackHairColor;
            public List<VisibilityRule> VisibilityRules = new List<VisibilityRule>();
            /// <summary>"&lt;transform path&gt;" or "&lt;path&gt;|v0|v1|..." (skinned vertex); null = none.</summary>
            public string AdvancedParent;
        }

        public struct VisibilityRule
        {
            public Rule Rule;
            /// <summary>Decimal of <see cref="Modifier"/>, or the 1-based linked slot for ACCESSORY_LINK / ACCESSORY_INVERSE_LINK.</summary>
            public string Modifier;
            public override string ToString() => $"{Rule}:{Modifier}";
        }

        public class CoordinateRule
        {
            public Rule Rule;
            public string RuleModifier;
            public Rule OverrideRule;
            public string OverrideRuleModifier;
        }

        public static AdditionalAccessoryControlsData Decode(int version, object data, ExtContext ctx)
        {
            const string P = PluginIds.AdditionalAccessoryControls;
            if (version != 0) ctx.Warn(P, $"unknown version {version}");
            var r = new AdditionalAccessoryControlsData { Version = version };
            string ka = ctx.IsCoordinate ? "coordinateAccessoryData" : "accessoryData";
            string ko = ctx.IsCoordinate ? "coordinateOverrideData" : "overrideData";
            r.AccessoryDataRaw = data.Get(ka) as byte[];
            r.OverrideDataRaw = data.Get(ko) as byte[];
            foreach (var s in ctx.Nested(data.Get(ka), P, ka).Items())
            {
                if (!(s is List<object> a)) continue;
                var slot = new SlotData
                {
                    IsEmpty = V.B(a.At(0), true), SlotNumber = V.I(a.At(1), r.Slots.Count), OriginalSlotNumber = V.I(a.At(2), -1),
                    CharacterAccessory = V.B(a.At(3), false), AccessoryName = a.At(4).Str(), AutoMatchBackHairColor = V.B(a.At(5), false),
                    AdvancedParent = V.SN(a.At(7)),
                };
                foreach (var rule in a.At(6).Items())
                    if (rule is List<object> ra) slot.VisibilityRules.Add(new VisibilityRule { Rule = (Rule)V.I(ra.At(0), 0), Modifier = Mod(ra.At(1)) });
                r.Slots.Add(slot);
            }
            var ov = ctx.Nested(data.Get(ko), P, ko);
            ReadRules(ov.At(0), r.SuppressedRules);
            ReadRules(ov.At(1), r.OverrideRules);
            return r;
        }

        static void ReadRules(object list, List<CoordinateRule> into)
        {
            foreach (var x in list.Items())
                if (x is List<object> a)
                    into.Add(new CoordinateRule { Rule = (Rule)V.I(a.At(0), 0), RuleModifier = Mod(a.At(1)), OverrideRule = (Rule)V.I(a.At(2), 0), OverrideRuleModifier = Mod(a.At(3)) });
        }

        static string Mod(object o) => o is string s ? s : o is long l ? l.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
    }
}

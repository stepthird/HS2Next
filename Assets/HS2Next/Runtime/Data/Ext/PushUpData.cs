// PushUp (`mikke.pushUpAI`): flat map, Bra set (no prefix, inner top) and Top set
// (prefix TOP_, top). Same keys on character and coordinate cards. The plugin is not installed in the
// reference game: the importer should store the values but keep the effect off by default.
using System;
using System.Collections.Generic;
using HS2Next.IO;

namespace HS2Next.Data.Ext
{
    public class PushUpData
    {
        public int Version;
        /// <summary>Inner top (clothes kind 2). Missing keys take <see cref="PushUpSet.BraDefaults"/>.</summary>
        public PushUpSet Bra = PushUpSet.BraDefaults();
        /// <summary>Top (clothes kind 0). Missing keys take <see cref="PushUpSet.TopDefaults"/> (all zero/false).</summary>
        public PushUpSet Top = PushUpSet.TopDefaults();
        /// <summary>Keys that 2.1.4 does not read (the "advanced" PUSH_* / USE_ADVANCED keys of other builds), as read.</summary>
        public Dictionary<string, object> OtherKeys = new Dictionary<string, object>(StringComparer.Ordinal);
        /// <summary>Number of the 22 known keys present.</summary>
        public int KnownKeyCount;

        /// <summary>True when both sets equal their defaults (missing keys count as defaults).</summary>
        public bool IsDefault => Bra.Equals(PushUpSet.BraDefaults()) && Top.Equals(PushUpSet.TopDefaults());

        public class PushUpSet : IEquatable<PushUpSet>
        {
            public float Firmness, Lift, PushTogether, Squeeze, CenterNipples, Corset;
            public bool FlattenNipples, EnablePushup, HideNipples, HideAccessories, CorsetHalf;

            /// <summary>Config defaults of the plugin (ints /100): enabled, firmness 0.9, lift 0.6, push together 0.65, squeeze 0.6, centering 1, flatten true.</summary>
            public static PushUpSet BraDefaults() => new PushUpSet
            {
                Firmness = 90 / 100f, Lift = 60 / 100f, PushTogether = 65 / 100f, Squeeze = 60 / 100f, CenterNipples = 100 / 100f,
                FlattenNipples = true, EnablePushup = true,
            };
            public static PushUpSet TopDefaults() => new PushUpSet();

            public bool Equals(PushUpSet o) => o != null && Firmness == o.Firmness && Lift == o.Lift && PushTogether == o.PushTogether &&
                Squeeze == o.Squeeze && CenterNipples == o.CenterNipples && Corset == o.Corset && FlattenNipples == o.FlattenNipples &&
                EnablePushup == o.EnablePushup && HideNipples == o.HideNipples && HideAccessories == o.HideAccessories && CorsetHalf == o.CorsetHalf;
            public override bool Equals(object obj) => Equals(obj as PushUpSet);
            public override int GetHashCode() => Firmness.GetHashCode() ^ Lift.GetHashCode() * 7 ^ (EnablePushup ? 1 : 0);
        }

        static readonly string[] FloatKeys = { "FIRMNESS", "LIFT", "PUSH_TOGETHER", "SQUEEZE", "CENTER_NIPPLES", "CORSET" };
        static readonly string[] BoolKeys = { "FLATTEN_NIPPLES", "ENABLE_PUSHUP", "HIDE_NIPPLES", "HIDE_ACCESSORIES", "CORSET_HALF" };

        public static PushUpData Decode(int version, object data, ExtContext ctx)
        {
            if (version != 2 && version != 3) ctx.Warn(PluginIds.PushUp, $"unknown version {version} (read anyway: the plugin only looks keys up)");
            var r = new PushUpData { Version = version };
            if (!(data is Dictionary<object, object> map)) return r;
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var k in FloatKeys) { known.Add(k); known.Add("TOP_" + k); }
            foreach (var k in BoolKeys) { known.Add(k); known.Add("TOP_" + k); }
            foreach (var kv in map)
            {
                if (!(kv.Key is string key)) continue;
                if (!known.Contains(key)) { r.OtherKeys[key] = kv.Value; continue; }
                r.KnownKeyCount++;
                if (kv.Value is double) ctx.Warn(PluginIds.PushUp, $"{key} is float64 (the plugin's unboxing cast would fail)");
            }
            Read(map, "", r.Bra);
            Read(map, "TOP_", r.Top);
            return r;
        }

        static void Read(Dictionary<object, object> m, string p, PushUpSet s)
        {
            s.Firmness = V.F(m.Get(p + "FIRMNESS"), s.Firmness);
            s.Lift = V.F(m.Get(p + "LIFT"), s.Lift);
            s.PushTogether = V.F(m.Get(p + "PUSH_TOGETHER"), s.PushTogether);
            s.Squeeze = V.F(m.Get(p + "SQUEEZE"), s.Squeeze);
            s.CenterNipples = V.F(m.Get(p + "CENTER_NIPPLES"), s.CenterNipples);
            s.Corset = V.F(m.Get(p + "CORSET"), s.Corset);
            s.FlattenNipples = V.B(m.Get(p + "FLATTEN_NIPPLES"), s.FlattenNipples);
            s.EnablePushup = V.B(m.Get(p + "ENABLE_PUSHUP"), s.EnablePushup);
            s.HideNipples = V.B(m.Get(p + "HIDE_NIPPLES"), s.HideNipples);
            s.HideAccessories = V.B(m.Get(p + "HIDE_ACCESSORIES"), s.HideAccessories);
            s.CorsetHalf = V.B(m.Get(p + "CORSET_HALF"), s.CorsetHalf);
        }
    }
}

// Shared helpers for the plugin data decoders: plugin ids, the per-decode context (diagnostics,
// counters, nested-document decoding) and small value readers.
using System;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.IO;
using UnityEngine;

namespace HS2Next.Data.Ext
{
    /// <summary>KKEx plugin data ids.</summary>
    public static class PluginIds
    {
        public const string Resolver = Card.ResolverId;
        public const string ResolverLegacy = Card.LegacyResolverId;
        public const string MoreAccessories = "moreAccessories";
        public const string Abmx = "KKABMPlugin.ABMData";
        public const string Uncensor = "com.deathweasel.bepinex.uncensorselector";
        public const string MaterialEditor = "com.deathweasel.bepinex.materialeditor";
        public const string Ksox = "KSOX";
        public const string Kcox = "KCOX";
        public const string PushUp = "mikke.pushUpAI";
        public const string DynamicBoneEditor = "com.deathweasel.bepinex.dynamicboneeditor";
        public const string InvisibleBody = "KK_InvisibleBody";
        public const string AdditionalAccessoryControls = "orange.spork.additionalaccessorycontrolsplugin";

        /// <summary>Ids with a typed decoder; every other id is kept as <see cref="OpaqueExt"/>.</summary>
        public static readonly HashSet<string> Native = new HashSet<string>(StringComparer.Ordinal)
        {
            Resolver, ResolverLegacy, MoreAccessories, Abmx, Uncensor, MaterialEditor, Ksox, Kcox, PushUp,
            DynamicBoneEditor, InvisibleBody, AdditionalAccessoryControls,
        };
    }

    /// <summary>State of one decode: diagnostics and counters, shared by all plugin decoders.</summary>
    public class ExtContext
    {
        public bool IsCoordinate;
        public readonly List<string> Diagnostics = new List<string>();
        public readonly Dictionary<string, int> Counts = new Dictionary<string, int>(StringComparer.Ordinal);

        public void Warn(string plugin, string message) => Diagnostics.Add($"{plugin}: {message}");
        public void Count(string key, int n = 1) { Counts.TryGetValue(key, out int v); Counts[key] = v + n; }

        /// <summary>Decodes a nested document stored in a bin value (plain or LZ4). A non-bin value is returned as is;
        /// nil gives null. Errors are logged and give null.</summary>
        public object Nested(object v, string plugin, string key)
        {
            if (!(v is byte[] bin)) return v;
            try
            {
                var o = NestedMsgPack.Read(bin, out var enc);
                Count($"nested.{plugin}.{enc}");
                return o;
            }
            catch (Exception e)
            {
                Warn(plugin, $"{key}: nested document not readable ({e.GetType().Name}: {e.Message})");
                return null;
            }
        }
    }

    /// <summary>Value readers for MessagePack object trees.</summary>
    public static class V
    {
        public static float F(object o, float def) => o is float f ? f : o is double d ? (float)d : o is long l ? l : o is ulong u ? u : def;
        public static float? FN(object o) => o is float || o is double || o is long || o is ulong ? F(o, 0) : (float?)null;
        public static int I(object o, int def) => o is long || o is ulong || o is float || o is double ? o.Int(def) : def;
        public static int? IN(object o) => o is long || o is ulong ? o.Int() : (int?)null;
        public static bool B(object o, bool def) => o is bool b ? b : def;
        public static bool? BN(object o) => o is bool b ? b : (bool?)null;
        /// <summary>String, or null for nil/blank/non-string.</summary>
        public static string SN(object o) => o is string s && s.Trim().Length > 0 ? s : null;

        public static Vector2? V2N(object o) => o is List<object> l && l.Count >= 2 ? new Vector2(F(l[0], 0), F(l[1], 0)) : (Vector2?)null;
        public static Vector3 V3(object o, Vector3 def) =>
            o is List<object> l && l.Count >= 3 ? new Vector3(F(l[0], def.x), F(l[1], def.y), F(l[2], def.z)) : def;
        public static Vector4? V4N(object o) =>
            o is List<object> l && l.Count >= 4 ? new Vector4(F(l[0], 0), F(l[1], 0), F(l[2], 0), F(l[3], 0)) : (Vector4?)null;
        public static Color? ColN(object o) =>
            o is List<object> l && l.Count >= 3 ? new Color(F(l[0], 0), F(l[1], 0), F(l[2], 0), l.Count > 3 ? F(l[3], 1) : 1) : (Color?)null;

        public static float ParseFloat(string s, float def) =>
            s != null && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : def;
    }
}

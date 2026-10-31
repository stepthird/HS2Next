// The argument view a handler reads (HS2Next.Api.Bridge.ICommandArgs) over one request object or a nested object
// of it. Conversions are strict: an int must be an integral number, a bool a
// JSON boolean, an enum a name or a defined number; arrays, lists and string-keyed dictionaries convert element by
// element with the same rules; other types go through the serializer and its converters (UnityEngine vectors in
// the Unity layer). Every failure is a BridgeException(bad_args) naming the argument path and the expected type.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using HS2Next.Api.Bridge;
using HS2Next.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HS2Next.Bridge
{
    sealed class ArgsView : ICommandArgs
    {
        readonly JObject obj;
        readonly string prefix;
        readonly List<string> names = new List<string>();
        readonly List<string> unknown = new List<string>();

        /// <param name="spec">The command (top level: unknown arguments are computed against it); null for nested views.</param>
        internal ArgsView(JObject obj, CommandSpec spec, string prefix = "")
        {
            this.obj = obj ?? new JObject();
            this.prefix = prefix ?? "";
            foreach (var p in this.obj.Properties())
            {
                names.Add(p.Name);
                if (spec != null && !spec.AllowExtraArgs && !EnvelopeFields.Contains(p.Name) && spec.FindArg(p.Name) == null) unknown.Add(p.Name);
            }
        }

        internal JObject Object => obj;

        public IReadOnlyList<string> Names => names;
        public IReadOnlyList<string> Unknown => unknown;
        public string Json => obj.ToString(Formatting.None);

        JToken Token(string name) => name != null && obj.TryGetValue(name, StringComparison.Ordinal, out var t) ? t : null;

        public bool Has(string name)
        {
            var t = Token(name);
            return t != null && t.Type != JTokenType.Null && t.Type != JTokenType.Undefined;
        }

        public ArgKind Kind(string name)
        {
            var t = Token(name);
            if (t == null) return ArgKind.Missing;
            switch (t.Type)
            {
                case JTokenType.Null:
                case JTokenType.Undefined: return ArgKind.Null;
                case JTokenType.Boolean: return ArgKind.Bool;
                case JTokenType.Integer: return ArgKind.Integer;
                case JTokenType.Float: return ArgKind.Float;
                case JTokenType.String: return ArgKind.String;
                case JTokenType.Array: return ArgKind.Array;
                case JTokenType.Object: return ArgKind.Object;
                default: return ArgKind.String;
            }
        }

        public T Get<T>(string name, T fallback = default) => TryGet(name, out T v) ? v : fallback;

        public bool TryGet<T>(string name, out T value)
        {
            if (!Has(name)) { value = default; return false; }
            value = (T)ConvertTo(Token(name), typeof(T), prefix + name);
            return true;
        }

        public T Require<T>(string name)
        {
            if (!TryGet(name, out T v)) throw new BridgeException(BridgeError.Missing(prefix + name, ArgTypes.Infer(typeof(T))));
            return v;
        }

        public TDto Bind<TDto>() where TDto : new()
        {
            var dto = new TDto();
            object boxed = dto;   // structs: set members on one box
            foreach (var m in CommandSpec.ArgMembers(typeof(TDto)))
            {
                string n = CommandSpec.ArgName(m);
                if (!Has(n))
                {
                    var a = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<BridgeArgAttribute>(m);
                    if (a != null && a.Required)
                        throw new BridgeException(BridgeError.Missing(prefix + n, a.Type ?? ArgTypes.Infer(CommandSpec.MemberType(m))));
                    continue;
                }
                var v = ConvertTo(Token(n), CommandSpec.MemberType(m), prefix + n);
                if (m is System.Reflection.FieldInfo f) f.SetValue(boxed, v);
                else ((System.Reflection.PropertyInfo)m).SetValue(boxed, v);
            }
            return (TDto)boxed;
        }

        public ICommandArgs Nested(string name)
        {
            var t = Token(name);
            if (t == null || t.Type == JTokenType.Null || t.Type == JTokenType.Undefined) return null;
            if (!(t is JObject o)) throw new BridgeException(BridgeError.BadArgs(prefix + name, ArgTypes.Object, Mismatch(prefix + name, ArgTypes.Object, t)));
            return new ArgsView(o, null, prefix + name + ".");
        }

        public string RawJson(string name) => Token(name)?.ToString(Formatting.None);

        // ------------------------------------------------------------------ conversion

        static string Mismatch(string path, string expected, JToken t) =>
            "argument '" + path + "': expected " + expected + ", got " + Describe(t);

        static string Describe(JToken t)
        {
            switch (t.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float: return "number " + t.ToString(Formatting.None);
                case JTokenType.String:
                    var s = (string)t;
                    return "string \"" + (s.Length > 40 ? s.Substring(0, 40) + "…" : s) + "\"";
                case JTokenType.Boolean: return (bool)t ? "true" : "false";
                case JTokenType.Array: return "array of " + ((JArray)t).Count;
                case JTokenType.Object: return "object";
                default: return t.Type.ToString().ToLowerInvariant();
            }
        }

        static BridgeException Bad(string path, Type t, JToken tok, string expected = null)
        {
            expected = expected ?? ArgTypes.Infer(t);
            return new BridgeException(BridgeError.BadArgs(path, expected, Mismatch(path, expected, tok)));
        }

        /// <summary>Converts a token to <paramref name="t"/> with the strict rules of the class comment.</summary>
        internal static object ConvertTo(JToken tok, Type t, string path)
        {
            if (t == typeof(RawJson)) return new RawJson(tok.ToString(Formatting.None));
            if (t == typeof(object)) return BridgeJson.ToPlain(tok);
            var under = Nullable.GetUnderlyingType(t);
            if (under != null)
            {
                if (tok.Type == JTokenType.Null || tok.Type == JTokenType.Undefined) return null;
                t = under;
            }
            if (t == typeof(string))
            {
                switch (tok.Type)
                {
                    case JTokenType.String: return (string)tok;
                    case JTokenType.Integer:
                    case JTokenType.Float: return Convert.ToString(((JValue)tok).Value, CultureInfo.InvariantCulture);
                    case JTokenType.Null: return null;
                    default: throw Bad(path, t, tok);
                }
            }
            if (t == typeof(bool))
            {
                if (tok.Type == JTokenType.Boolean) return (bool)tok;
                throw Bad(path, t, tok);
            }
            if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) || t == typeof(uint) ||
                t == typeof(ushort) || t == typeof(sbyte) || t == typeof(ulong))
                return Integral(tok, t, path);
            if (t == typeof(float) || t == typeof(double) || t == typeof(decimal))
            {
                if (tok.Type != JTokenType.Integer && tok.Type != JTokenType.Float) throw Bad(path, t, tok);
                double d = Convert.ToDouble(((JValue)tok).Value, CultureInfo.InvariantCulture);
                if (t == typeof(float))
                {
                    float f = (float)d;
                    if (float.IsInfinity(f) && !double.IsInfinity(d)) throw Bad(path, t, tok, "float (within float32 range)");
                    return f;
                }
                if (t == typeof(decimal)) return Convert.ToDecimal(((JValue)tok).Value, CultureInfo.InvariantCulture);
                return d;
            }
            if (t.IsEnum)
            {
                if (tok.Type == JTokenType.String)
                {
                    string s = (string)tok;
                    foreach (var n in Enum.GetNames(t))
                        if (string.Equals(n, s, StringComparison.OrdinalIgnoreCase)) return Enum.Parse(t, n);
                    throw Bad(path, t, tok);
                }
                if (tok.Type == JTokenType.Integer)
                {
                    object raw = Integral(tok, Enum.GetUnderlyingType(t), path);
                    if (Enum.IsDefined(t, raw)) return Enum.ToObject(t, raw);
                }
                throw Bad(path, t, tok);
            }
            if (t == typeof(ContentId))
            {
                if (tok.Type == JTokenType.String && ContentId.TryParse((string)tok, out var id)) return id;
                throw Bad(path, t, tok, ArgTypes.ContentId + " (\"<guid>#<id>\")");
            }
            if (t.IsArray && t.GetArrayRank() == 1)
            {
                if (!(tok is JArray arr)) throw Bad(path, t, tok);
                var et = t.GetElementType();
                var result = Array.CreateInstance(et, arr.Count);
                for (int i = 0; i < arr.Count; i++) result.SetValue(ConvertTo(arr[i], et, path + "[" + i + "]"), i);
                return result;
            }
            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition();
                var ga = t.GetGenericArguments();
                if (ga.Length == 1 && (def == typeof(List<>) || def == typeof(IList<>) || def == typeof(IReadOnlyList<>) ||
                                       def == typeof(IEnumerable<>) || def == typeof(ICollection<>) || def == typeof(IReadOnlyCollection<>)))
                {
                    if (!(tok is JArray arr)) throw Bad(path, t, tok);
                    var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(ga[0]));
                    for (int i = 0; i < arr.Count; i++) list.Add(ConvertTo(arr[i], ga[0], path + "[" + i + "]"));
                    return list;
                }
                if (ga.Length == 2 && ga[0] == typeof(string) &&
                    (def == typeof(Dictionary<,>) || def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>)))
                {
                    if (!(tok is JObject o)) throw Bad(path, t, tok);
                    var dict = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(ga), StringComparer.Ordinal);
                    foreach (var p in o.Properties()) dict[p.Name] = ConvertTo(p.Value, ga[1], path + "." + p.Name);
                    return dict;
                }
            }
            try
            {
                return tok.ToObject(t, BridgeJson.Serializer);
            }
            catch (Exception e) when (e is JsonException || e is ArgumentException || e is FormatException || e is InvalidCastException ||
                                      e is OverflowException || e is InvalidOperationException)
            {
                throw Bad(path, t, tok);
            }
        }

        static object Integral(JToken tok, Type t, string path)
        {
            BigInteger v;
            if (tok.Type == JTokenType.Integer)
            {
                var raw = ((JValue)tok).Value;
                v = raw is BigInteger b ? b : new BigInteger(Convert.ToInt64(raw, CultureInfo.InvariantCulture));
            }
            else if (tok.Type == JTokenType.Float)
            {
                double d = Convert.ToDouble(((JValue)tok).Value, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d) || Math.Floor(d) != d || Math.Abs(d) > 9.007199254740992E15) throw Bad(path, t, tok);
                v = new BigInteger(d);
            }
            else throw Bad(path, t, tok);
            BigInteger min, max;
            if (t == typeof(int)) { min = int.MinValue; max = int.MaxValue; }
            else if (t == typeof(long)) { min = long.MinValue; max = long.MaxValue; }
            else if (t == typeof(short)) { min = short.MinValue; max = short.MaxValue; }
            else if (t == typeof(byte)) { min = byte.MinValue; max = byte.MaxValue; }
            else if (t == typeof(sbyte)) { min = sbyte.MinValue; max = sbyte.MaxValue; }
            else if (t == typeof(ushort)) { min = ushort.MinValue; max = ushort.MaxValue; }
            else if (t == typeof(uint)) { min = uint.MinValue; max = uint.MaxValue; }
            else { min = ulong.MinValue; max = ulong.MaxValue; }
            if (v < min || v > max) throw Bad(path, t, tok, ArgTypes.Int + " (" + min + ".." + max + ")");
            if (t == typeof(int)) return (int)v;
            if (t == typeof(long)) return (long)v;
            if (t == typeof(short)) return (short)v;
            if (t == typeof(byte)) return (byte)v;
            if (t == typeof(sbyte)) return (sbyte)v;
            if (t == typeof(ushort)) return (ushort)v;
            if (t == typeof(uint)) return (uint)v;
            return (ulong)v;
        }
    }
}

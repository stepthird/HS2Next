// JSON form of field values: the forms (v3 as
// [x, y, z], colours and quaternions as 4 floats, refs and ids as strings, maps as objects with ordinal keys,
// document objects exactly as scene.json writes them), so the bridge and plugins can read and write any path.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Save;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes
{
    public static class FieldJson
    {
        /// <summary>The JSON of a field value; null (C#) for <see cref="FieldValue.Absent"/>.</summary>
        public static JToken ToJson(object v)
        {
            switch (v)
            {
                case null: return JValue.CreateNull();
                case bool b: return new JValue(b);
                case int i: return new JValue((long)i);
                case float f: return new JValue(D(f));
                case string s: return new JValue(s);
                case Vec2 a: return Floats(a.X, a.Y);
                case Vec3 a: return Floats(a.X, a.Y, a.Z);
                case Vec4 a: return Floats(a.X, a.Y, a.Z, a.W);
                case Color4 a: return Floats(a.R, a.G, a.B, a.A);
                case Quat4 a: return Floats(a.X, a.Y, a.Z, a.W);
                // a node's ui writes the character members only for characters (its own flag), as scene.json does
                case NodeUi u: return DocReader.Parse(CanonicalJsonWriter.ToBytes(SceneDocument.EntryName, u.WriteTo), SceneDocument.EntryName);
                case DocObject d: return DocReader.Parse(DocClone.Bytes(d), SceneDocument.EntryName);
                case IDictionary m:
                {
                    var keys = new List<string>();
                    foreach (var k in m.Keys) keys.Add((string)k);
                    keys.Sort(string.CompareOrdinal);
                    var o = new JObject();
                    foreach (var k in keys) o[k] = ToJson(m[k]);
                    return o;
                }
                case IEnumerable e:
                {
                    var a = new JArray();
                    foreach (var x in e) a.Add(ToJson(x));
                    return a;
                }
                default:
                    if (FieldValue.IsAbsent(v)) return null;
                    throw new ArgumentException("not a field value: " + v.GetType().Name);
            }
        }

        /// <summary>Canonical text of a value ("null" for null and Absent).</summary>
        public static string ToText(object v) => CanonicalJsonWriter.ToText(ToJson(v) ?? JValue.CreateNull());

        static JArray Floats(params float[] f)
        {
            var a = new JArray();
            foreach (var x in f) a.Add(new JValue(D(x)));
            return a;
        }

        /// <summary>The double whose JSON text is the float's canonical text T(x), so the token writes
        /// exactly as scene.json does ("0.1", not "0.10000000149011612"). Non-finite values throw.</summary>
        static double D(float f) => double.Parse(JsonText.F32(f), NumberStyles.Float, CultureInfo.InvariantCulture);

        /// <summary>
        /// A value of <paramref name="type"/> from JSON (the inverse of <see cref="ToJson"/>); throws
        /// <see cref="CommandException"/> (bad_args) when the JSON does not have that form. The value rules of a set
        /// (refs, enums, lengths) are checked by the set, not here.
        /// </summary>
        public static object FromJson(JToken t, Type type)
        {
            try { return Convert(t, type); }
            catch (CommandException) { throw; }
            catch (Exception e) { throw new CommandException(CommandCodes.BadArgs, "JSON does not fit " + type.Name + ": " + e.Message); }
        }

        /// <summary>Parses JSON text and converts it.</summary>
        public static object FromText(string json, Type type)
        {
            var t = DocReader.TryParseValue(json, out string error);
            if (t == null) throw new CommandException(CommandCodes.BadArgs, "bad JSON: " + error);
            return FromJson(t, type);
        }

        static CommandException Bad(Type type, JToken t) =>
            new CommandException(CommandCodes.BadArgs, "expected " + type.Name + ", found " + (t == null ? "nothing" : t.Type.ToString()));

        static float F(JToken t)
        {
            if (t.Type != JTokenType.Float && t.Type != JTokenType.Integer) throw Bad(typeof(float), t);
            return (float)System.Convert.ToDouble(((JValue)t).Value, CultureInfo.InvariantCulture);
        }

        static float[] Fs(JToken t, int n)
        {
            if (!(t is JArray a) || a.Count != n) throw Bad(typeof(float[]), t);
            var r = new float[n];
            for (int i = 0; i < n; i++) r[i] = F(a[i]);
            return r;
        }

        static object Convert(JToken t, Type type)
        {
            if (t == null) throw Bad(type, null);
            if (t.Type == JTokenType.Null)
            {
                if (type.IsValueType && Nullable.GetUnderlyingType(type) == null) throw Bad(type, t);
                return null;
            }
            if (type == typeof(bool)) return t.Type == JTokenType.Boolean ? (object)(bool)t : throw Bad(type, t);
            if (type == typeof(int))
            {
                if (t.Type != JTokenType.Integer) throw Bad(type, t);
                return checked((int)(long)t);
            }
            if (type == typeof(float) || type == typeof(float?)) return F(t);
            if (type == typeof(string)) return t.Type == JTokenType.String ? (object)(string)t : throw Bad(type, t);
            if (type == typeof(Vec2)) { var f = Fs(t, 2); return new Vec2(f[0], f[1]); }
            if (type == typeof(Vec3) || type == typeof(Vec3?)) { var f = Fs(t, 3); return new Vec3(f[0], f[1], f[2]); }
            if (type == typeof(Vec4)) { var f = Fs(t, 4); return new Vec4(f[0], f[1], f[2], f[3]); }
            if (type == typeof(Color4)) { var f = Fs(t, 4); return new Color4(f[0], f[1], f[2], f[3]); }
            if (type == typeof(Quat4)) { var f = Fs(t, 4); return new Quat4(f[0], f[1], f[2], f[3]); }
            if (type.IsArray)
            {
                if (!(t is JArray a)) throw Bad(type, t);
                var et = type.GetElementType();
                var r = Array.CreateInstance(et, a.Count);
                for (int i = 0; i < a.Count; i++) r.SetValue(Convert(a[i], et), i);
                return r;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                if (!(t is JArray a)) throw Bad(type, t);
                var et = type.GetGenericArguments()[0];
                var r = (IList)Activator.CreateInstance(type);
                foreach (var e in a) r.Add(Convert(e, et));
                return r;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SortedDictionary<,>))
            {
                if (!(t is JObject o)) throw Bad(type, t);
                var vt = type.GetGenericArguments()[1];
                var r = (IDictionary)Activator.CreateInstance(type, StringComparer.Ordinal);
                foreach (var p in o.Properties()) r[p.Name] = Convert(p.Value, vt);
                return r;
            }
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null) return Convert(t, underlying);   // int?, bool?, Vec2?, Color4?, Vec4? (null handled above)
            if (type == typeof(MaterialOverride))
            {
                // MaterialEditor items (character documents): dispatch on the `type` member like the reader.
                if (!(t is JObject mo)) throw Bad(type, t);
                return MaterialOverride.Read(DocReader.ForDocument(CanonicalJsonWriter.ToBytes(mo), new DocReadContext(SceneDocument.EntryName)));
            }
            if (typeof(DocObject).IsAssignableFrom(type) && type != typeof(SceneNode) && type != typeof(OpaqueNode) && !type.IsAbstract)
            {
                if (!(t is JObject o)) throw Bad(type, t);
                var bytes = CanonicalJsonWriter.ToBytes(o);
                var d = Blank(type, o);
                if (d is NodeUi u) u.IsChara = true;
                d.ReadFrom(DocReader.ForDocument(bytes, new DocReadContext(SceneDocument.EntryName)));
                return d;
            }
            throw new CommandException(CommandCodes.BadArgs, "no JSON form for " + type.Name);
        }

        // A fresh object for JSON (the character document classes without a parameterless constructor: the set fits
        // the value to the destination's shape, FieldSchema.Coerce).
        static DocObject Blank(Type type, JObject o)
        {
            if (type == typeof(ItemPower)) return new ItemPower(0f);
            if (type == typeof(ItemColor)) return new ItemColor(default);
            if (type == typeof(ItemLayout)) return new ItemLayout(default, default, o["tilt"] != null);
            if (type == typeof(ClothesPart)) return new ClothesPart(ClothesKind.Top);
            return (DocObject)Activator.CreateInstance(type);
        }
    }
}

// The request helpers of a plugin's context (IPluginContext.Request / RequestAsync): the envelope {"cmd": command, ...args}
// is built here from an argument object (null, an object whose public properties and fields become the arguments, an
// IDictionary, a JObject) or a CommandRequest, and sent as Call / CallAsync send envelope text, so the reply text equals
// Call's for the same envelope. The arguments are written as a bridge result is written: member names camelCase as
// written (dictionary keys as they are), BridgeIgnore and BridgeArg(Name) honoured, enums by name, floats in round-trip
// form with the invariant culture, NaN and infinities as null, RawJson verbatim, ContentId as "guid#id", the engine's
// vectors, quaternions and colours as number arrays; nulls are written (an explicit null argument). The reply is wrapped
// in a CommandReply: Ok, Code, Error, Warnings, the exact text, and typed reads of its members, whose object values are
// plain .NET values (string, long, double, bool, null, List of object, Dictionary of string to object).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using HS2Next.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace HS2Next.App.Plugins
{
    public static class PluginRequests
    {
        /// <summary>The envelope members a Request may carry besides the arguments (cmd is refused).</summary>
        static readonly string[] EnvelopeNames = { "id", "strict", "undo", "waitFrames", "timeout", "as" };

        static readonly JsonSerializer Writer = JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = new ArgResolver(),
            NullValueHandling = NullValueHandling.Include,
            ReferenceLoopHandling = ReferenceLoopHandling.Error,
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Double,
            Culture = CultureInfo.InvariantCulture,
            MaxDepth = 128,
            Converters = { new RawJsonWriter(), new ContentIdWriter(), new StringEnumConverter(), new EngineValueWriter() },
        });

        static readonly JsonSerializer Reader = JsonSerializer.Create(new JsonSerializerSettings
        {
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Double,
            Culture = CultureInfo.InvariantCulture,
            MaxDepth = 128,
            Converters = { new PlainObjectReader() },
        });

        /// <summary>The envelope text of <paramref name="command"/> with <paramref name="args"/> (ArgumentException for misuse).</summary>
        public static string Envelope(string command, object args)
        {
            if (string.IsNullOrEmpty(command)) throw new ArgumentException("a request needs a command name", nameof(command));
            var env = new JObject { ["cmd"] = command };
            if (args != null)
            {
                JObject o;
                if (args is JObject jo) o = jo;
                else if (args is IEnumerable<KeyValuePair<string, object>> kvs)
                {
                    o = new JObject();
                    foreach (var kv in kvs) o[kv.Key ?? ""] = Value(kv.Value);
                }
                else if (args is IDictionary dict)
                {
                    o = new JObject();
                    foreach (DictionaryEntry e in dict) o[System.Convert.ToString(e.Key, CultureInfo.InvariantCulture) ?? ""] = Value(e.Value);
                }
                else if (args is JToken || args is string || args is bool || args is char || IsNumber(args) || args is IEnumerable)
                    throw new ArgumentException("arguments are an object; use Call for envelope text", nameof(args));
                else o = JToken.FromObject(args, Writer) as JObject ?? throw new ArgumentException("arguments are an object; use Call for envelope text", nameof(args));
                foreach (var p in o.Properties())
                {
                    if (p.Name == "cmd") throw new ArgumentException("'cmd' cannot be an argument: it is the command name", nameof(args));
                    env[p.Name] = p.Value.DeepClone();
                }
            }
            return Text(env);
        }

        /// <summary>The envelope text of a built request (its arguments in order, then its envelope members).</summary>
        public static string Envelope(CommandRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var env = new JObject { ["cmd"] = request.Command };
            foreach (var kv in request.Args)
            {
                if (kv.Key == "cmd" || Array.IndexOf(EnvelopeNames, kv.Key) >= 0)
                    throw new ArgumentException("'" + kv.Key + "' is an envelope member: set it through the request's properties");
                env[kv.Key] = Value(kv.Value);
            }
            if (request.Id != null) env["id"] = Value(request.Id);
            if (request.Strict.HasValue) env["strict"] = request.Strict.Value;
            if (request.Undo.HasValue) env["undo"] = request.Undo.Value;
            if (request.WaitFrames != 0) env["waitFrames"] = request.WaitFrames;
            if (request.Timeout.HasValue) env["timeout"] = request.Timeout.Value;
            return Text(env);
        }

        static bool IsNumber(object o) => o is byte || o is sbyte || o is short || o is ushort || o is int || o is uint || o is long || o is ulong ||
                                          o is float || o is double || o is decimal;

        /// <summary>One value as the arguments are written (null stays a JSON null).</summary>
        public static JToken Value(object v) => v == null ? JValue.CreateNull() : v is JToken t ? t.DeepClone() : JToken.FromObject(v, Writer);

        static string Text(JObject env)
        {
            var sw = new StringWriter(CultureInfo.InvariantCulture);
            using (var w = new FiniteWriter(sw) { Formatting = Formatting.None }) env.WriteTo(w);
            return sw.ToString();
        }

        /// <summary>A plain .NET value of a token (objects as Dictionary, arrays as List, integers as long, floats as double).</summary>
        public static object Plain(JToken t)
        {
            switch (t?.Type ?? JTokenType.Null)
            {
                case JTokenType.Object:
                    var d = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (var p in ((JObject)t).Properties()) d[p.Name] = Plain(p.Value);
                    return d;
                case JTokenType.Array:
                    var l = new List<object>();
                    foreach (var e in (JArray)t) l.Add(Plain(e));
                    return l;
                case JTokenType.Integer:
                    var v = ((JValue)t).Value;
                    return v is System.Numerics.BigInteger ? (object)(double)(System.Numerics.BigInteger)v : System.Convert.ToInt64(v, CultureInfo.InvariantCulture);
                case JTokenType.Float: return System.Convert.ToDouble(((JValue)t).Value, CultureInfo.InvariantCulture);
                case JTokenType.Boolean: return (bool)t;
                case JTokenType.String: return (string)t;
                case JTokenType.Null:
                case JTokenType.Undefined: return null;
                default: return t.ToString(Formatting.None);
            }
        }

        /// <summary>A token converted to <typeparamref name="T"/> (object = the plain value); FormatException naming <paramref name="what"/>.</summary>
        public static T ToType<T>(JToken t, string what)
        {
            try
            {
                if (typeof(T) == typeof(object)) return (T)Plain(t);
                if (typeof(T) == typeof(JToken)) return (T)(object)t.DeepClone();
                return t.ToObject<T>(Reader);
            }
            catch (Exception e) when (e is JsonException || e is InvalidCastException || e is FormatException || e is OverflowException || e is ArgumentException)
            {
                throw new FormatException(what + ": a " + t.Type.ToString().ToLowerInvariant() + " value does not convert to " + Name(typeof(T)) + " (" + e.Message + ")", e);
            }
        }

        static string Name(Type t)
        {
            if (!t.IsGenericType) return t.Name;
            var a = t.GetGenericArguments();
            var n = new List<string>();
            foreach (var x in a) n.Add(Name(x));
            return t.Name.Substring(0, t.Name.IndexOf('`')) + "<" + string.Join(", ", n) + ">";
        }

        // ------------------------------------------------------------ JSON parts

        /// <summary>The bridge's result naming: camelCase as written, dictionary keys untouched, BridgeIgnore / BridgeArg(Name).</summary>
        sealed class ArgResolver : DefaultContractResolver
        {
            public ArgResolver() { NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false, OverrideSpecifiedNames = false }; }

            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                var p = base.CreateProperty(member, memberSerialization);
                if (member.GetCustomAttribute<BridgeIgnoreAttribute>() != null) p.Ignored = true;
                var name = member.GetCustomAttribute<BridgeArgAttribute>()?.Name;
                if (!string.IsNullOrEmpty(name)) p.PropertyName = name;
                return p;
            }
        }

        sealed class RawJsonWriter : JsonConverter
        {
            public override bool CanConvert(Type objectType) => objectType == typeof(RawJson);
            public override bool CanRead => false;
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => writer.WriteRawValue(((RawJson)value).Text);
            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) => throw new NotSupportedException();
        }

        sealed class ContentIdWriter : JsonConverter
        {
            public override bool CanConvert(Type objectType) => objectType == typeof(ContentId) || objectType == typeof(ContentId?);
            public override bool CanRead => false;
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var id = (ContentId)value;
                if (id.IsEmpty) writer.WriteNull();
                else writer.WriteValue(id.ToString());
            }
            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) => throw new NotSupportedException();
        }

        /// <summary>The engine's value types as the bridge writes them: vectors [x, y(, z(, w))], quaternions [x, y, z, w], colours [r, g, b, a].</summary>
        sealed class EngineValueWriter : JsonConverter
        {
            public override bool CanConvert(Type t) => t == typeof(UnityEngine.Vector2) || t == typeof(UnityEngine.Vector3) || t == typeof(UnityEngine.Vector4) ||
                                                       t == typeof(UnityEngine.Quaternion) || t == typeof(UnityEngine.Color) || t == typeof(UnityEngine.Color32) ||
                                                       t == typeof(UnityEngine.Vector2Int) || t == typeof(UnityEngine.Vector3Int);
            public override bool CanRead => false;
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                writer.WriteStartArray();
                switch (value)
                {
                    case UnityEngine.Vector2 v: writer.WriteValue(v.x); writer.WriteValue(v.y); break;
                    case UnityEngine.Vector3 v: writer.WriteValue(v.x); writer.WriteValue(v.y); writer.WriteValue(v.z); break;
                    case UnityEngine.Vector4 v: writer.WriteValue(v.x); writer.WriteValue(v.y); writer.WriteValue(v.z); writer.WriteValue(v.w); break;
                    case UnityEngine.Quaternion q: writer.WriteValue(q.x); writer.WriteValue(q.y); writer.WriteValue(q.z); writer.WriteValue(q.w); break;
                    case UnityEngine.Color c: writer.WriteValue(c.r); writer.WriteValue(c.g); writer.WriteValue(c.b); writer.WriteValue(c.a); break;
                    case UnityEngine.Color32 c: writer.WriteValue(c.r); writer.WriteValue(c.g); writer.WriteValue(c.b); writer.WriteValue(c.a); break;
                    case UnityEngine.Vector2Int v: writer.WriteValue(v.x); writer.WriteValue(v.y); break;
                    case UnityEngine.Vector3Int v: writer.WriteValue(v.x); writer.WriteValue(v.y); writer.WriteValue(v.z); break;
                }
                writer.WriteEndArray();
            }
            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) => throw new NotSupportedException();
        }

        /// <summary>Reads values typed object as plain .NET values (so List&lt;object&gt; holds dictionaries, lists and primitives).</summary>
        sealed class PlainObjectReader : JsonConverter
        {
            public override bool CanConvert(Type objectType) => objectType == typeof(object);
            public override bool CanWrite => false;
            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) => Plain(JToken.ReadFrom(reader));
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => throw new NotSupportedException();
        }

        sealed class FiniteWriter : JsonTextWriter
        {
            public FiniteWriter(TextWriter w) : base(w) { }
            public override void WriteValue(float value) { if (float.IsNaN(value) || float.IsInfinity(value)) WriteNull(); else base.WriteValue(value); }
            public override void WriteValue(double value) { if (double.IsNaN(value) || double.IsInfinity(value)) WriteNull(); else base.WriteValue(value); }
            public override void WriteValue(float? value) { if (value.HasValue) WriteValue(value.Value); else WriteNull(); }
            public override void WriteValue(double? value) { if (value.HasValue) WriteValue(value.Value); else WriteNull(); }
        }
    }

    /// <summary>The typed view of one reply text (ICommandReply). Immutable; any thread.</summary>
    public sealed class CommandReply : ICommandReply
    {
        readonly JObject root;
        readonly string[] warnings;

        public CommandReply(string json)
        {
            Json = json ?? "null";
            try
            {
                using (var r = new JsonTextReader(new StringReader(Json)) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double })
                    root = JToken.ReadFrom(r) as JObject;
            }
            catch (JsonException) { root = null; }
            root ??= new JObject { ["ok"] = false, ["error"] = "the reply is not a JSON object", ["code"] = BridgeCodes.Internal };
            Ok = root["ok"]?.Type == JTokenType.Boolean && (bool)root["ok"];
            Command = root["cmd"]?.Type == JTokenType.String ? (string)root["cmd"] : null;
            Code = Ok ? null : Str("code") ?? BridgeCodes.Internal;
            Error = Ok ? null : Str("error") ?? "";
            var w = new List<string>();
            if (root["warnings"] is JArray a) foreach (var x in a) w.Add(x.Type == JTokenType.String ? (string)x : x.ToString(Formatting.None));
            warnings = w.ToArray();
        }

        string Str(string name) => root[name]?.Type == JTokenType.String ? (string)root[name] : null;

        public bool Ok { get; }
        public string Command { get; }
        public string Code { get; }
        public string Error { get; }
        public IReadOnlyList<string> Warnings => warnings;
        public string Json { get; }

        public bool Has(string member) => member != null && root.TryGetValue(member, StringComparison.Ordinal, out var t) && t.Type != JTokenType.Null;

        public T Get<T>(string member, T fallback = default)
        {
            if (member == null || !root.TryGetValue(member, StringComparison.Ordinal, out var t) || t.Type == JTokenType.Null) return fallback;
            return PluginRequests.ToType<T>(t, "the member " + member);
        }

        public string GetJson(string member) =>
            member != null && root.TryGetValue(member, StringComparison.Ordinal, out var t) ? t.ToString(Formatting.None) : null;

        public T As<T>() => PluginRequests.ToType<T>(root, "the reply of " + (Command ?? "the request"));

        public void ThrowIfError()
        {
            if (Ok) return;
            throw new BridgeException(Code, Error, Detail());
        }

        BridgeErrorDetail Detail()
        {
            if (!(root["detail"] is JObject d)) return null;
            var r = new BridgeErrorDetail();
            foreach (var p in d.Properties())
            {
                switch (p.Name)
                {
                    case "arg": r.Arg = p.Value.Type == JTokenType.String ? (string)p.Value : p.Value.ToString(Formatting.None); break;
                    case "expected": r.Expected = p.Value.Type == JTokenType.String ? (string)p.Value : p.Value.ToString(Formatting.None); break;
                    case "phase": r.Phase = p.Value.Type == JTokenType.String ? (string)p.Value : p.Value.ToString(Formatting.None); break;
                    case "candidates":
                        var l = new List<string>();
                        if (p.Value is JArray ca) foreach (var x in ca) l.Add(x.Type == JTokenType.String ? (string)x : x.ToString(Formatting.None));
                        r.Candidates = l;
                        break;
                    default:
                        (r.Extra ??= new Dictionary<string, object>(StringComparer.Ordinal))[p.Name] = PluginRequests.Plain(p.Value);
                        break;
                }
            }
            return r;
        }

        public override string ToString() => Json;
    }
}

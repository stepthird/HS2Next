// The bridge's JSON layer (the save format's library,
// Newtonsoft.Json 13): strict request parsing (one object, duplicate members rejected, dates left as strings), result
// serialization (DTO members camelCase as written, [BridgeIgnore] / [BridgeArg(Name)] honoured, null members left
// out, enums by name, floats in round-trip form, RawJson verbatim, ContentId as "guid#id"), and the converter list
// that the Unity layer extends with UnityEngine vector, quaternion and colour converters. Newtonsoft types never leave
// this assembly.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HS2Next.Api.Bridge;
using HS2Next.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace HS2Next.Bridge
{
    public static class BridgeJson
    {
        static readonly object gate = new object();
        static readonly List<JsonConverter> extra = new List<JsonConverter>();
        static JsonSerializer serializer;

        static readonly JsonLoadSettings LoadSettings = new JsonLoadSettings
        {
            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
            CommentHandling = CommentHandling.Ignore,
            LineInfoHandling = LineInfoHandling.Ignore,
        };

        /// <summary>
        /// Adds a converter used for arguments (Get / Bind of types the bridge does not convert itself) and results.
        /// Idempotent per converter type. Call before requests arrive (the Unity layer does it at start).
        /// </summary>
        public static void AddConverter(JsonConverter converter)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            lock (gate)
            {
                foreach (var c in extra) if (c.GetType() == converter.GetType()) return;
                extra.Add(converter);
                serializer = null;
            }
        }

        internal static JsonSerializer Serializer
        {
            get
            {
                lock (gate)
                {
                    if (serializer != null) return serializer;
                    var s = new JsonSerializerSettings
                    {
                        ContractResolver = new BridgeContractResolver(),
                        NullValueHandling = NullValueHandling.Ignore,
                        ReferenceLoopHandling = ReferenceLoopHandling.Error,
                        DateParseHandling = DateParseHandling.None,
                        FloatParseHandling = FloatParseHandling.Double,
                        FloatFormatHandling = FloatFormatHandling.String,
                        Formatting = Formatting.None,
                        MaxDepth = 128,
                    };
                    s.Converters.Add(new RawJsonConverter());
                    s.Converters.Add(new ContentIdConverter());
                    s.Converters.Add(new StringEnumConverter());
                    foreach (var c in extra) s.Converters.Add(c);
                    serializer = JsonSerializer.Create(s);
                    return serializer;
                }
            }
        }

        /// <summary>Parses one JSON value (nothing but whitespace or comments after it). Throws JsonException.</summary>
        internal static JToken Parse(string text)
        {
            using (var r = new JsonTextReader(new StringReader(text))
            {
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double,
                MaxDepth = 128,
            })
            {
                var tok = JToken.ReadFrom(r, LoadSettings);
                while (r.Read())
                    if (r.TokenType != JsonToken.Comment) throw new JsonReaderException("unexpected content after the JSON value");
                return tok;
            }
        }

        /// <summary>A handler result as a JSON token (null stays null).</summary>
        internal static JToken FromResult(object result)
        {
            if (result == null) return null;
            if (result is RawJson raw) return Parse(raw.Text);
            if (result is JToken t) return t;
            return JToken.FromObject(result, Serializer);
        }

        /// <summary>Compact text of a token; NaN and infinities are written as null.</summary>
        internal static string Write(JToken t)
        {
            if (t == null) return "null";
            var sw = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            using (var w = new FiniteJsonWriter(sw) { Formatting = Formatting.None }) t.WriteTo(w);
            return sw.ToString();
        }

        /// <summary>A plain .NET value of a token (objects as Dictionary, arrays as List, integers as long).</summary>
        internal static object ToPlain(JToken t)
        {
            switch (t?.Type ?? JTokenType.Null)
            {
                case JTokenType.Object:
                    var d = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (var p in ((JObject)t).Properties()) d[p.Name] = ToPlain(p.Value);
                    return d;
                case JTokenType.Array:
                    var l = new List<object>();
                    foreach (var e in (JArray)t) l.Add(ToPlain(e));
                    return l;
                case JTokenType.Integer:
                    var v = ((JValue)t).Value;
                    return v is long || v is int ? Convert.ToInt64(v) : v;
                case JTokenType.Float: return Convert.ToDouble(((JValue)t).Value, System.Globalization.CultureInfo.InvariantCulture);
                case JTokenType.Boolean: return (bool)t;
                case JTokenType.String: return (string)t;
                case JTokenType.Null:
                case JTokenType.Undefined: return null;
                default: return t.ToString(Formatting.None);
            }
        }
    }

    /// <summary>
    /// The reply writer: floats keep their CLR type (float = shortest round-trip float32 text, double = round-trip
    /// double text, ".0" on integral values); NaN and infinities become null (JSON has no such numbers).
    /// </summary>
    sealed class FiniteJsonWriter : JsonTextWriter
    {
        public FiniteJsonWriter(TextWriter w) : base(w) { }
        public override void WriteValue(float value) { if (float.IsNaN(value) || float.IsInfinity(value)) WriteNull(); else base.WriteValue(value); }
        public override void WriteValue(double value) { if (double.IsNaN(value) || double.IsInfinity(value)) WriteNull(); else base.WriteValue(value); }
        public override void WriteValue(float? value) { if (value.HasValue) WriteValue(value.Value); else WriteNull(); }
        public override void WriteValue(double? value) { if (value.HasValue) WriteValue(value.Value); else WriteNull(); }
    }

    /// <summary>camelCase member names as written (dictionary keys untouched); [BridgeIgnore] and [BridgeArg(Name)] honoured.</summary>
    sealed class BridgeContractResolver : DefaultContractResolver
    {
        public BridgeContractResolver()
        {
            NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false, OverrideSpecifiedNames = false };
        }

        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var p = base.CreateProperty(member, memberSerialization);
            if (member.GetCustomAttribute<BridgeIgnoreAttribute>() != null) p.Ignored = true;
            var name = member.GetCustomAttribute<BridgeArgAttribute>()?.Name;
            if (!string.IsNullOrEmpty(name)) p.PropertyName = name;
            return p;
        }
    }

    sealed class RawJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(RawJson);
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => writer.WriteRawValue(((RawJson)value).Text);
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) =>
            new RawJson(JToken.ReadFrom(reader).ToString(Formatting.None));
    }

    sealed class ContentIdConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(ContentId) || objectType == typeof(ContentId?);
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var id = (ContentId)value;
            if (id.IsEmpty) writer.WriteNull();
            else writer.WriteValue(id.ToString());
        }
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return objectType == typeof(ContentId) ? default(ContentId) : (object)null;
            if (reader.TokenType == JsonToken.String && ContentId.TryParse((string)reader.Value, out var id)) return id;
            throw new JsonSerializationException("expected \"<guid>#<id>\"");
        }
    }
}

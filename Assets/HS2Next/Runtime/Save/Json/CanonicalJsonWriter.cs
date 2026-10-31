// The one JSON writer of the save format. Wraps
// Newtonsoft's JsonTextWriter (indented, 2 spaces, LF) and adds what the spec fixes on top of it:
// float32 canonical text with the NonFinite check and its JSON path, one-line number/bool arrays,
// validated ids/refs/res names, ordinal-sorted maps, leftovers (unknown members) and `ext`, and a
// duplicate-member guard. Raw tokens (unknown members, ext JSON) go through Token(), which writes
// them in the same canonical layout, so canonical input comes back byte-identical. Every `ref`
// written (and every animeRef source reported through RefGuid) is collected in ReferencedGuids for
// the manifest's `requires` list. No reflection serialisation.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using HS2Next.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save
{
    public sealed class CanonicalJsonWriter : IDisposable
    {
        sealed class Frame
        {
            public bool IsArray;
            public int Index = -1;          // array: index of the element being written
            public string Name;             // object: current member name
            public HashSet<string> Names;   // object: members written so far
        }

        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        readonly TextWriter text;
        readonly JsonTextWriter jw;
        readonly List<Frame> stack = new List<Frame>();
        bool finished;

        /// <summary>Entry name for error locations ("chara.json"); may be null.</summary>
        public string Entry { get; }

        /// <summary>GUIDs of every `ref` written and every GUID passed to <see cref="RefGuid"/> (as spelled; deduplicated ordinally).</summary>
        public HashSet<string> ReferencedGuids { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Writes to <paramref name="textWriter"/>, whose NewLine should be "\n" (the static helpers set it up).</summary>
        public CanonicalJsonWriter(TextWriter textWriter, string entry = null)
        {
            text = textWriter;
            Entry = entry;
            jw = new JsonTextWriter(textWriter)
            {
                Formatting = Formatting.Indented,
                Indentation = 2,
                IndentChar = ' ',
                CloseOutput = false,
                Culture = CultureInfo.InvariantCulture,
            };
        }

        /// <summary>
        /// Runs <paramref name="body"/> (which writes exactly one root value, normally an object) and returns the
        /// UTF-8 bytes without BOM, ending with one LF. GUIDs of written refs are added to <paramref name="guids"/>.
        /// </summary>
        public static byte[] ToBytes(string entry, Action<CanonicalJsonWriter> body, ISet<string> guids = null)
        {
            var ms = new MemoryStream();
            using (var sw = new StreamWriter(ms, Utf8, 1 << 16, true) { NewLine = "\n" })
            using (var w = new CanonicalJsonWriter(sw, entry))
            {
                body(w);
                w.Finish();
                if (guids != null) foreach (var g in w.ReferencedGuids) guids.Add(g);
            }
            return ms.ToArray();
        }

        /// <summary>
        /// <see cref="ToBytes(string, Action{CanonicalJsonWriter}, ISet{string})"/> for a core document of a save: an
        /// exception other than <see cref="SaveFormatException"/> thrown while <paramref name="body"/> writes (a model
        /// the document classes do not guard against, e.g. a null sub-object) becomes <see cref="SaveErrorCode.BadDocument"/>
        /// at the JSON path where writing stopped, with the original exception as the inner exception.
        /// </summary>
        public static byte[] ToDocumentBytes(string entry, Action<CanonicalJsonWriter> body, ISet<string> guids = null)
        {
            var ms = new MemoryStream();
            using (var sw = new StreamWriter(ms, Utf8, 1 << 16, true) { NewLine = "\n" })
            using (var w = new CanonicalJsonWriter(sw, entry))
            {
                try
                {
                    body(w);
                    w.Finish();
                }
                catch (Exception e) when (!(e is SaveFormatException))
                {
                    throw new SaveFormatException(SaveErrorCode.BadDocument, "unexpected " + e.GetType().Name + " while writing: " + e.Message, entry, w.Path, 0, e);
                }
                if (guids != null) foreach (var g in w.ReferencedGuids) guids.Add(g);
            }
            return ms.ToArray();
        }

        /// <summary>Canonical text of one value (no trailing LF), e.g. for ext JSON handed to plugins.</summary>
        public static string ToText(JToken token)
        {
            var sw = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
            using (var w = new CanonicalJsonWriter(sw))
            {
                w.Token(token);
                w.jw.Flush();
            }
            return sw.ToString();
        }

        /// <summary>Canonical document bytes of a token (UTF-8, trailing LF).</summary>
        public static byte[] ToBytes(JToken token, string entry = null) => ToBytes(entry, w => w.Token(token));

        /// <summary>Ends the document: one LF after the root value, then flush. Called by <see cref="ToBytes(string, Action{CanonicalJsonWriter}, ISet{string})"/>.</summary>
        public void Finish()
        {
            if (finished) return;
            if (stack.Count != 0) throw new InvalidOperationException("unclosed object or array at " + Path);
            jw.Flush();
            text.Write('\n');
            text.Flush();
            finished = true;
        }

        public void Dispose()
        {
            ((IDisposable)jw).Dispose();
        }

        // ---------- structure ----------

        /// <summary>JSON path of the value being written ("face.shape[3]"; "" at the root).</summary>
        public string Path
        {
            get
            {
                var sb = new StringBuilder();
                foreach (var f in stack)
                {
                    if (f.IsArray) { if (f.Index >= 0) sb.Append('[').Append(f.Index).Append(']'); }
                    else if (f.Name != null) { if (sb.Length > 0) sb.Append('.'); sb.Append(f.Name); }
                }
                return sb.ToString();
            }
        }

        Frame Top => stack.Count == 0 ? null : stack[stack.Count - 1];

        void BeforeValue()
        {
            var t = Top;
            if (t != null && t.IsArray) t.Index++;
        }

        public void StartObject()
        {
            BeforeValue();
            jw.WriteStartObject();
            stack.Add(new Frame { Names = new HashSet<string>(StringComparer.Ordinal) });
        }

        public void EndObject()
        {
            if (Top == null || Top.IsArray) throw new InvalidOperationException("EndObject outside an object at " + Path);
            jw.WriteEndObject();
            stack.RemoveAt(stack.Count - 1);
        }

        public void StartArray()
        {
            BeforeValue();
            jw.WriteStartArray();
            stack.Add(new Frame { IsArray = true });
        }

        public void EndArray()
        {
            if (Top == null || !Top.IsArray) throw new InvalidOperationException("EndArray outside an array at " + Path);
            jw.WriteEndArray();
            stack.RemoveAt(stack.Count - 1);
        }

        /// <summary>Writes a member name. A name already written in this object throws (a writer bug).</summary>
        public void Name(string name)
        {
            var t = Top;
            if (t == null || t.IsArray) throw new InvalidOperationException("member name outside an object: " + name);
            if (!t.Names.Add(name)) throw new InvalidOperationException("duplicate member '" + name + "' at " + Path);
            t.Name = name;
            jw.WritePropertyName(name);
        }

        /// <summary>True if the current object already has member <paramref name="name"/>.</summary>
        public bool HasWritten(string name) => Top != null && !Top.IsArray && Top.Names.Contains(name);

        SaveFormatException Bad(SaveErrorCode code, string detail, string path = null) =>
            new SaveFormatException(code, detail, Entry, path ?? Path);

        // ---------- scalars ----------

        public void Null() { BeforeValue(); jw.WriteNull(); }
        public void Bool(bool v) { BeforeValue(); jw.WriteValue(v); }
        public void Int(int v) { BeforeValue(); jw.WriteValue(v); }
        public void Int64(long v) { BeforeValue(); jw.WriteValue(v); }

        /// <summary>A string; null writes null.</summary>
        public void Str(string v) { BeforeValue(); if (v == null) jw.WriteNull(); else jw.WriteValue(v); }

        /// <summary>float32 canonical text; NaN/±Infinity throw <see cref="SaveErrorCode.NonFinite"/> with the JSON path.</summary>
        public void F32(float v)
        {
            BeforeValue();
            if (JsonText.IsNonFinite(v)) throw Bad(SaveErrorCode.NonFinite, "non-finite float " + v.ToString(CultureInfo.InvariantCulture));
            jw.WriteRawValue(JsonText.F32(v));
        }

        public void BoolN(bool? v) { if (v.HasValue) Bool(v.Value); else Null(); }
        public void IntN(int? v) { if (v.HasValue) Int(v.Value); else Null(); }
        public void Int64N(long? v) { if (v.HasValue) Int64(v.Value); else Null(); }
        public void F32N(float? v) { if (v.HasValue) F32(v.Value); else Null(); }

        // ---------- one-line arrays ----------

        void RawLine(StringBuilder sb)
        {
            jw.WriteRawValue(sb.ToString());
        }

        string F32At(float v, int i)
        {
            if (JsonText.IsNonFinite(v))
                throw Bad(SaveErrorCode.NonFinite, "non-finite float " + v.ToString(CultureInfo.InvariantCulture), Path + "[" + i + "]");
            return JsonText.F32(v);
        }

        void Floats(float a, float b) { BeforeValue(); RawLine(new StringBuilder("[").Append(F32At(a, 0)).Append(", ").Append(F32At(b, 1)).Append(']')); }
        void Floats(float a, float b, float c) { BeforeValue(); RawLine(new StringBuilder("[").Append(F32At(a, 0)).Append(", ").Append(F32At(b, 1)).Append(", ").Append(F32At(c, 2)).Append(']')); }
        void Floats(float a, float b, float c, float d) { BeforeValue(); RawLine(new StringBuilder("[").Append(F32At(a, 0)).Append(", ").Append(F32At(b, 1)).Append(", ").Append(F32At(c, 2)).Append(", ").Append(F32At(d, 3)).Append(']')); }

        public void V2(Vec2 v) => Floats(v.X, v.Y);
        public void V3(Vec3 v) => Floats(v.X, v.Y, v.Z);
        public void V4(Vec4 v) => Floats(v.X, v.Y, v.Z, v.W);
        public void Euler(Vec3 v) => Floats(v.X, v.Y, v.Z);
        public void Col(Color4 v) => Floats(v.R, v.G, v.B, v.A);
        public void Quat(Quat4 v) => Floats(v.X, v.Y, v.Z, v.W);

        public void V2N(Vec2? v) { if (v.HasValue) V2(v.Value); else Null(); }
        public void V3N(Vec3? v) { if (v.HasValue) V3(v.Value); else Null(); }
        public void V4N(Vec4? v) { if (v.HasValue) V4(v.Value); else Null(); }
        public void EulerN(Vec3? v) { if (v.HasValue) Euler(v.Value); else Null(); }
        public void ColN(Color4? v) { if (v.HasValue) Col(v.Value); else Null(); }
        public void QuatN(Quat4? v) { if (v.HasValue) Quat(v.Value); else Null(); }

        /// <summary>f32[] on one line; null writes null, empty writes [].</summary>
        public void F32Array(IReadOnlyList<float> a)
        {
            if (a == null) { Null(); return; }
            BeforeValue();
            var sb = new StringBuilder("[");
            for (int i = 0; i < a.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(F32At(a[i], i));
            }
            RawLine(sb.Append(']'));
        }

        /// <summary>int[] on one line; null writes null.</summary>
        public void IntArray(IReadOnlyList<int> a)
        {
            if (a == null) { Null(); return; }
            BeforeValue();
            var sb = new StringBuilder("[");
            for (int i = 0; i < a.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(a[i].ToString(CultureInfo.InvariantCulture));
            }
            RawLine(sb.Append(']'));
        }

        /// <summary>bool[] on one line; null writes null.</summary>
        public void BoolArray(IReadOnlyList<bool> a)
        {
            if (a == null) { Null(); return; }
            BeforeValue();
            var sb = new StringBuilder("[");
            for (int i = 0; i < a.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(a[i] ? "true" : "false");
            }
            RawLine(sb.Append(']'));
        }

        /// <summary>str[]: one element per line (strings are not one-line elements); null writes null. A null element
        /// fails with BadDocument (a `str[]` holds strings only).</summary>
        public void StrArray(IReadOnlyList<string> a)
        {
            if (a == null) { Null(); return; }
            StartArray();
            foreach (var s in a)
            {
                if (s == null) { BeforeValue(); throw Bad(SaveErrorCode.BadDocument, "string array element is null"); }
                Str(s);
            }
            EndArray();
        }

        // ---------- typed strings ----------

        /// <summary>A `ref` text "&lt;guid&gt;#&lt;id&gt;" written verbatim (validated); null writes null. Records the GUID.</summary>
        public void Ref(string refText)
        {
            if (refText == null) { Null(); return; }
            if (!JsonText.IsRef(refText)) { BeforeValue(); throw Bad(SaveErrorCode.BadDocument, "not a ref: \"" + refText + "\""); }
            ReferencedGuids.Add(JsonText.RefGuid(refText));
            Str(refText);
        }

        /// <summary>A `ref` from a <see cref="ContentId"/>; the empty id writes null. Records the GUID.</summary>
        public void Ref(ContentId id)
        {
            if (id.IsEmpty) { Null(); return; }
            ReferencedGuids.Add(id.SourceGuid);
            Str(id.ToString());
        }

        /// <summary>Records a GUID for `requires` without writing anything (animeRef `source`).</summary>
        public void RefGuid(string guid)
        {
            if (!string.IsNullOrEmpty(guid)) ReferencedGuids.Add(guid);
        }

        /// <summary>An `id` (32 lowercase hex, validated); null writes null.</summary>
        public void Id(string id)
        {
            if (id == null) { Null(); return; }
            if (!JsonText.IsId(id)) { BeforeValue(); throw Bad(SaveErrorCode.BadDocument, "not an id: \"" + id + "\""); }
            Str(id);
        }

        /// <summary>A `res` name "res/&lt;sha256&gt;.&lt;ext&gt;" (validated); null writes null.</summary>
        public void Res(string resName)
        {
            if (resName == null) { Null(); return; }
            if (!JsonText.IsResName(resName)) { BeforeValue(); throw Bad(SaveErrorCode.BadDocument, "not a res name: \"" + resName + "\""); }
            Str(resName);
        }

        /// <summary>A `time` "yyyy-MM-ddTHH:mm:ssZ" (UTC).</summary>
        public void Time(DateTime t) => Str(JsonText.Time(t));

        // ---------- member shortcuts: Name(name) + value ----------

        public void Null(string name) { Name(name); Null(); }
        public void Bool(string name, bool v) { Name(name); Bool(v); }
        public void Int(string name, int v) { Name(name); Int(v); }
        public void Int64(string name, long v) { Name(name); Int64(v); }
        public void Str(string name, string v) { Name(name); Str(v); }
        public void F32(string name, float v) { Name(name); F32(v); }
        public void BoolN(string name, bool? v) { Name(name); BoolN(v); }
        public void IntN(string name, int? v) { Name(name); IntN(v); }
        public void Int64N(string name, long? v) { Name(name); Int64N(v); }
        public void F32N(string name, float? v) { Name(name); F32N(v); }
        public void V2(string name, Vec2 v) { Name(name); V2(v); }
        public void V3(string name, Vec3 v) { Name(name); V3(v); }
        public void V4(string name, Vec4 v) { Name(name); V4(v); }
        public void Euler(string name, Vec3 v) { Name(name); Euler(v); }
        public void Col(string name, Color4 v) { Name(name); Col(v); }
        public void Quat(string name, Quat4 v) { Name(name); Quat(v); }
        public void V2N(string name, Vec2? v) { Name(name); V2N(v); }
        public void V3N(string name, Vec3? v) { Name(name); V3N(v); }
        public void V4N(string name, Vec4? v) { Name(name); V4N(v); }
        public void EulerN(string name, Vec3? v) { Name(name); EulerN(v); }
        public void ColN(string name, Color4? v) { Name(name); ColN(v); }
        public void QuatN(string name, Quat4? v) { Name(name); QuatN(v); }
        public void F32Array(string name, IReadOnlyList<float> a) { Name(name); F32Array(a); }
        public void IntArray(string name, IReadOnlyList<int> a) { Name(name); IntArray(a); }
        public void BoolArray(string name, IReadOnlyList<bool> a) { Name(name); BoolArray(a); }
        public void StrArray(string name, IReadOnlyList<string> a) { Name(name); StrArray(a); }
        public void Ref(string name, string refText) { Name(name); Ref(refText); }
        public void Ref(string name, ContentId id) { Name(name); Ref(id); }
        public void Id(string name, string id) { Name(name); Id(id); }
        public void Res(string name, string resName) { Name(name); Res(resName); }
        public void Time(string name, DateTime t) { Name(name); Time(t); }
        public void Token(string name, JToken t) { Name(name); Token(t); }

        /// <summary>Starts member <paramref name="name"/> as an object (end with <see cref="EndObject"/>).</summary>
        public void StartObject(string name) { Name(name); StartObject(); }

        /// <summary>Starts member <paramref name="name"/> as an array (end with <see cref="EndArray"/>).</summary>
        public void StartArray(string name) { Name(name); StartArray(); }

        // ---------- maps, leftovers, ext ----------

        /// <summary>A `map&lt;T&gt;` value: keys sorted by <see cref="string.CompareOrdinal(string, string)"/>; null writes {}.</summary>
        public void Map<T>(IEnumerable<KeyValuePair<string, T>> map, Action<CanonicalJsonWriter, T> value)
        {
            StartObject();
            if (map != null)
            {
                var items = new List<KeyValuePair<string, T>>(map);
                items.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
                foreach (var kv in items)
                {
                    Name(kv.Key);
                    value(this, kv.Value);
                }
            }
            EndObject();
        }

        public void Map<T>(string name, IEnumerable<KeyValuePair<string, T>> map, Action<CanonicalJsonWriter, T> value) { Name(name); Map(map, value); }

        /// <summary>Writes unknown members after the known ones; names already written in this object are skipped. Null is allowed.</summary>
        public void Leftovers(JsonLeftovers leftovers)
        {
            if (leftovers == null) return;
            foreach (var m in leftovers.Members)
            {
                if (HasWritten(m.Key)) continue;
                Name(m.Key);
                Token(m.Value);
            }
        }

        /// <summary>
        /// Writes member <c>ext</c> (namespaces in ordinal order) when it holds at least one namespace.
        /// The 1 MiB limit is enforced where inline data is produced (<see cref="ExtInline.Set"/>, the Api's SetJson);
        /// what the holder contains — an oversized block read from a file included — is written back as it is.
        /// </summary>
        public void Ext(ExtInline ext)
        {
            if (ext == null || ext.IsEmpty) return;
            Name("ext");
            StartObject();
            foreach (var kv in ext)
            {
                Name(kv.Key);
                Token(kv.Value);
            }
            EndObject();
        }

        // ---------- raw tokens ----------

        static bool IsLineElement(JToken t) =>
            t.Type == JTokenType.Integer || t.Type == JTokenType.Float || t.Type == JTokenType.Boolean || t.Type == JTokenType.Null;

        string ScalarText(JToken t, string path)
        {
            switch (t.Type)
            {
                case JTokenType.Null: return "null";
                case JTokenType.Boolean: return (bool)t ? "true" : "false";
                case JTokenType.Integer:
                    var v = ((JValue)t).Value;
                    return v is BigInteger bi ? bi.ToString(CultureInfo.InvariantCulture) : Convert.ToInt64(v, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                case JTokenType.Float:
                    double d = Convert.ToDouble(((JValue)t).Value, CultureInfo.InvariantCulture);
                    if (double.IsNaN(d) || double.IsInfinity(d)) throw Bad(SaveErrorCode.NonFinite, "non-finite number", path);
                    return JsonText.F64(d);
                default: throw new InvalidOperationException("not a line element: " + t.Type);
            }
        }

        /// <summary>
        /// Writes a raw token canonically: objects member by member in stored order, arrays of numbers/bools/null
        /// on one line, other arrays one element per line, floats per <see cref="JsonText.F64"/>.
        /// </summary>
        public void Token(JToken t)
        {
            if (t == null) { Null(); return; }
            switch (t.Type)
            {
                case JTokenType.Object:
                    StartObject();
                    foreach (var p in ((JObject)t).Properties())
                    {
                        Name(p.Name);
                        Token(p.Value);
                    }
                    EndObject();
                    return;
                case JTokenType.Array:
                    var arr = (JArray)t;
                    bool line = arr.Count > 0;
                    foreach (var e in arr) if (!IsLineElement(e)) { line = false; break; }
                    if (line)
                    {
                        BeforeValue();
                        string basePath = Path;
                        var sb = new StringBuilder("[");
                        for (int i = 0; i < arr.Count; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            sb.Append(ScalarText(arr[i], basePath + "[" + i + "]"));
                        }
                        RawLine(sb.Append(']'));
                        return;
                    }
                    StartArray();
                    foreach (var e in arr) Token(e);
                    EndArray();
                    return;
                case JTokenType.Null:
                case JTokenType.Undefined:
                    Null();
                    return;
                case JTokenType.Boolean:
                    Bool((bool)t);
                    return;
                case JTokenType.Integer:
                case JTokenType.Float:
                    BeforeValue();
                    jw.WriteRawValue(ScalarText(t, Path));
                    return;
                case JTokenType.String:
                    Str((string)t);
                    return;
                case JTokenType.Date:
                case JTokenType.Guid:
                case JTokenType.Uri:
                case JTokenType.TimeSpan:
                case JTokenType.Bytes:
                    // Never produced by DocReader (DateParseHandling.None); a token built in code keeps Newtonsoft's text.
                    BeforeValue();
                    t.WriteTo(jw);
                    return;
                default:
                    throw new InvalidOperationException("cannot write token type " + t.Type + " at " + Path);
            }
        }
    }
}

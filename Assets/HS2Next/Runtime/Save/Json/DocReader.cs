// Strict typed reading of the save documents. Parse() loads one
// JSON document with the reader settings (no date parsing, doubles, duplicate members are
// errors, BOM tolerated, NaN/Infinity rejected with their path). DocReader wraps one JSON object:
// typed getters take the member name and the "absent" value (or none = required), record the
// member as known, and fail with the JSON path and line on a type error — or, with Lenient, report
// an error finding and return the absent value. What the getters did not touch is captured by
// Leftovers() and the `ext` member by Ext(). DocValue does the same conversions on a
// single token (array elements, map values).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save
{
    /// <summary>Settings and sinks shared by every reader of one document.</summary>
    public sealed class DocReadContext
    {
        /// <summary>Entry name used in paths of errors and findings ("chara.json", "charas/&lt;id&gt;/coord/coord.json").</summary>
        public readonly string Entry;
        /// <summary>LoadOptions.Lenient: type errors in known members become error findings and the absent value is used.</summary>
        public readonly bool Lenient;
        public readonly FindingList Findings;
        /// <summary>Code of type and required-member errors: <see cref="SaveErrorCode.BadDocument"/> or <see cref="SaveErrorCode.BadManifest"/>.</summary>
        public readonly SaveErrorCode ErrorCode;

        public DocReadContext(string entry, bool lenient = false, FindingList findings = null, SaveErrorCode errorCode = SaveErrorCode.BadDocument)
        {
            Entry = entry;
            Lenient = lenient;
            Findings = findings ?? new FindingList();
            ErrorCode = errorCode;
        }

        /// <summary>"entry:path" for findings.</summary>
        public string Where(string jsonPath) => SaveFormatException.Location(Entry, jsonPath);
    }

    /// <summary>A type error in a known member: the load fails unless the reader is lenient.</summary>
    public sealed class DocTypeException : SaveFormatException
    {
        public DocTypeException(DocReadContext ctx, JToken at, string path, string detail)
            : base(ctx.ErrorCode, detail, ctx.Entry, path, LineOf(at)) { }

        internal static int LineOf(JToken t) => t is IJsonLineInfo li && li.HasLineInfo() ? li.LineNumber : 0;
    }

    public sealed class DocReader
    {
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        static readonly JsonLoadSettings LoadSettings = new JsonLoadSettings
        {
            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
            CommentHandling = CommentHandling.Ignore,
            LineInfoHandling = LineInfoHandling.Load,
        };

        // ---------- parsing ----------

        /// <summary>
        /// Parses one JSON document (UTF-8, BOM tolerated) whose root must be an object. Syntax errors, duplicate
        /// members, a non-object root or content after it throw <paramref name="code"/>; NaN/Infinity anywhere
        /// throw <see cref="SaveErrorCode.NonFinite"/>. Both carry the JSON path and line.
        /// </summary>
        public static JObject Parse(byte[] utf8, string entry, SaveErrorCode code = SaveErrorCode.BadDocument)
        {
            string text;
            try
            {
                int skip = utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF ? 3 : 0;
                text = StrictUtf8.GetString(utf8, skip, utf8.Length - skip);
            }
            catch (ArgumentException e)
            {
                throw new SaveFormatException(code, "invalid UTF-8", entry, null, 0, e);
            }
            var token = ParseText(text, entry, code);
            if (token.Type != JTokenType.Object)
                throw new SaveFormatException(code, "root is " + Describe(token) + ", expected an object", entry, "", 1);
            return (JObject)token;
        }

        /// <summary>Parses exactly one JSON value of any type with the settings (errors as in <see cref="Parse"/>).</summary>
        public static JToken ParseText(string text, string entry, SaveErrorCode code = SaveErrorCode.BadDocument)
        {
            JToken token;
            try
            {
                using (var jr = new JsonTextReader(new StringReader(text))
                {
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Double,
                    MaxDepth = 64,
                })
                {
                    if (!jr.Read()) throw new SaveFormatException(code, "empty document", entry, "", 1);
                    while (jr.TokenType == JsonToken.Comment) if (!jr.Read()) throw new SaveFormatException(code, "empty document", entry, "", 1);
                    token = JToken.ReadFrom(jr, LoadSettings);
                    while (jr.Read())
                        if (jr.TokenType != JsonToken.Comment)
                            throw new SaveFormatException(code, "content after the root value", entry, jr.Path, jr.LineNumber);
                }
            }
            catch (JsonReaderException e)
            {
                throw new SaveFormatException(code, e.Message, entry, e.Path, e.LineNumber, e);
            }
            CheckFinite(token, entry);
            return token;
        }

        /// <summary>
        /// Validation for text handed in through the Api (ext SetJson): one JSON value with the settings.
        /// Returns null and the reason on failure.
        /// </summary>
        public static JToken TryParseValue(string text, out string error)
        {
            error = null;
            if (text == null) { error = "null text"; return null; }
            try { return ParseText(text, null); }
            catch (SaveFormatException e) { error = e.Message; return null; }
        }

        static void CheckFinite(JToken t, string entry)
        {
            switch (t.Type)
            {
                case JTokenType.Object:
                    foreach (var p in ((JObject)t).Properties()) CheckFinite(p.Value, entry);
                    break;
                case JTokenType.Array:
                    foreach (var e in (JArray)t) CheckFinite(e, entry);
                    break;
                case JTokenType.Float:
                    double d = Convert.ToDouble(((JValue)t).Value, CultureInfo.InvariantCulture);
                    if (double.IsNaN(d) || double.IsInfinity(d))
                        throw new SaveFormatException(SaveErrorCode.NonFinite, "non-finite number", entry, t.Path, DocTypeException.LineOf(t));
                    break;
            }
        }

        internal static string Describe(JToken t)
        {
            switch (t.Type)
            {
                case JTokenType.Object: return "an object";
                case JTokenType.Array: return "an array";
                case JTokenType.Null: return "null";
                case JTokenType.String: return "a string";
                case JTokenType.Integer: return "an integer";
                case JTokenType.Float: return "a number";
                case JTokenType.Boolean: return "a boolean";
                default: return t.Type.ToString();
            }
        }

        internal static string Join(string path, string name)
        {
            bool plain = name.Length > 0;
            foreach (char c in name)
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '$')) { plain = false; break; }
            if (!plain) return path + "['" + name.Replace("'", "\\'") + "']";
            return string.IsNullOrEmpty(path) ? name : path + "." + name;
        }

        // ---------- reader over one object ----------

        readonly JObject obj;
        // The empty reader of an absent member has no token of its own: its path is the parent's path plus the member
        // name, computed only when a finding or an error needs it. (JToken.Path of an array element is linear in its
        // index, so computing it eagerly for every absent member made reading a large scene quadratic.)
        readonly DocReader pathParent;
        readonly string pathMember;
        string pathCache;
        // Members a getter has touched (the rest are leftovers); allocated on first use, never for an empty object.
        HashSet<string> known;

        public DocReader(JObject obj, DocReadContext ctx) : this(obj, ctx, null, null) { }

        DocReader(JObject obj, DocReadContext ctx, DocReader pathParent, string pathMember)
        {
            this.obj = obj ?? throw new ArgumentNullException(nameof(obj));
            Context = ctx ?? throw new ArgumentNullException(nameof(ctx));
            this.pathParent = pathParent;
            this.pathMember = pathMember;
        }

        /// <summary>Reader over the root object of a parsed document.</summary>
        public static DocReader ForDocument(byte[] utf8, DocReadContext ctx) => new DocReader(Parse(utf8, ctx.Entry, ctx.ErrorCode), ctx);

        public JObject Object => obj;
        public DocReadContext Context { get; }

        /// <summary>JSON path of this object ("" for the root); computed on demand.</summary>
        public string Path => pathParent == null ? obj.Path : (pathCache ??= Join(pathParent.Path, pathMember));

        void Mark(string name)
        {
            if (obj.Count == 0) return;   // nothing to leave over (the empty reader of an absent member)
            (known ??= new HashSet<string>(StringComparer.Ordinal)).Add(name);
        }

        /// <summary>True if the member exists (also when it is null). Does not mark it as known.</summary>
        public bool Has(string name) => obj.TryGetValue(name, out _);

        /// <summary>True if the member exists and is null.</summary>
        public bool IsNull(string name) => obj.TryGetValue(name, out var t) && t.Type == JTokenType.Null;

        /// <summary>Marks members as known without reading them (they will not be leftovers).</summary>
        public void Consume(params string[] names)
        {
            foreach (var n in names) Mark(n);
        }

        /// <summary>Member names in read order.</summary>
        public IEnumerable<string> MemberNames
        {
            get { foreach (var p in obj.Properties()) yield return p.Name; }
        }

        /// <summary>Reports a finding located at member <paramref name="member"/> of this object (null = the object itself).</summary>
        public void Report(FindingSeverity severity, string code, string member, string message) =>
            Context.Findings.Add(severity, code, Context.Where(member == null ? Path : Join(Path, member)), message);

        DocTypeException TypeError(string member, JToken at, string detail) =>
            new DocTypeException(Context, at ?? obj, Join(Path, member), detail);

        SaveFormatException Missing(string name) =>
            new SaveFormatException(Context.ErrorCode, "missing required member '" + name + "'", Context.Entry, Join(Path, name), DocTypeException.LineOf(obj));

        /// <summary>The member as a <see cref="DocValue"/> (marked known), or null when absent.</summary>
        public DocValue Value(string name)
        {
            Mark(name);
            return obj.TryGetValue(name, out var t) ? new DocValue(t, Context) : null;
        }

        /// <summary>A required member of any type as a <see cref="DocValue"/> (missing → error even when lenient).</summary>
        public DocValue ValueReq(string name)
        {
            Mark(name);
            if (!obj.TryGetValue(name, out var t)) throw Missing(name);
            return new DocValue(t, Context);
        }

        /// <summary>A detached copy of the member's raw token (marked known), or null when absent.</summary>
        public JToken Raw(string name)
        {
            Mark(name);
            return obj.TryGetValue(name, out var t) ? t.DeepClone() : null;
        }

        /// <summary>A detached copy of the whole object; every member is marked known (opaque nodes, unknown ME types).</summary>
        public JObject Detach()
        {
            foreach (var p in obj.Properties()) Mark(p.Name);
            return (JObject)obj.DeepClone();
        }

        // Optional member: absent → absent value; type error → strict: throw, lenient: finding + absent.
        T Opt<T>(string name, T absent, Func<DocValue, T> conv)
        {
            Mark(name);
            if (!obj.TryGetValue(name, out var t)) return absent;
            try { return conv(new DocValue(t, Context)); }
            catch (DocTypeException e) when (Context.Lenient)
            {
                Context.Findings.Error("json.typeError", SaveFormatException.Location(e.Entry, e.JsonPath), e.Detail + " (absent value used)");
                return absent;
            }
        }

        // Required member: absent → error even when lenient (there is no absent value).
        T Req<T>(string name, Func<DocValue, T> conv)
        {
            Mark(name);
            if (!obj.TryGetValue(name, out var t)) throw Missing(name);
            return conv(new DocValue(t, Context));
        }

        // ---------- scalars ----------

        public bool Bool(string name) => Req(name, v => v.AsBool());
        public bool Bool(string name, bool absent) => Opt(name, absent, v => v.AsBool());
        public bool? BoolN(string name, bool? absent = null) => Opt(name, absent, v => v.IsNull ? (bool?)null : v.AsBool());

        public int Int(string name) => Req(name, v => v.AsInt());
        public int Int(string name, int absent) => Opt(name, absent, v => v.AsInt());
        public int? IntN(string name, int? absent = null) => Opt(name, absent, v => v.IsNull ? (int?)null : v.AsInt());

        public long Int64(string name) => Req(name, v => v.AsInt64());
        public long Int64(string name, long absent) => Opt(name, absent, v => v.AsInt64());
        public long? Int64N(string name, long? absent = null) => Opt(name, absent, v => v.IsNull ? (long?)null : v.AsInt64());

        public float F32(string name) => Req(name, v => v.AsF32());
        public float F32(string name, float absent) => Opt(name, absent, v => v.AsF32());
        public float? F32N(string name, float? absent = null) => Opt(name, absent, v => v.IsNull ? (float?)null : v.AsF32());

        /// <summary>A non-null string (JSON null is a type error).</summary>
        public string Str(string name) => Req(name, v => v.AsStr());
        public string Str(string name, string absent) => Opt(name, absent, v => v.AsStr());
        /// <summary>A string or null.</summary>
        public string StrN(string name, string absent = null) => Opt(name, absent, v => v.AsStrN());

        // ---------- value types ----------

        public Vec2 V2(string name, Vec2 absent) => Opt(name, absent, v => v.AsV2());
        public Vec3 V3(string name, Vec3 absent) => Opt(name, absent, v => v.AsV3());
        public Vec4 V4(string name, Vec4 absent) => Opt(name, absent, v => v.AsV4());
        public Vec3 Euler(string name, Vec3 absent) => Opt(name, absent, v => v.AsV3());
        public Color4 Col(string name, Color4 absent) => Opt(name, absent, v => v.AsCol());
        public Quat4 Quat(string name, Quat4 absent) => Opt(name, absent, v => v.AsQuat());

        public Vec2 V2(string name) => Req(name, v => v.AsV2());
        public Vec3 V3(string name) => Req(name, v => v.AsV3());
        public Vec4 V4(string name) => Req(name, v => v.AsV4());
        public Vec3 Euler(string name) => Req(name, v => v.AsV3());
        public Color4 Col(string name) => Req(name, v => v.AsCol());
        public Quat4 Quat(string name) => Req(name, v => v.AsQuat());

        public Vec2? V2N(string name, Vec2? absent = null) => Opt(name, absent, v => v.IsNull ? (Vec2?)null : v.AsV2());
        public Vec3? V3N(string name, Vec3? absent = null) => Opt(name, absent, v => v.IsNull ? (Vec3?)null : v.AsV3());
        public Vec4? V4N(string name, Vec4? absent = null) => Opt(name, absent, v => v.IsNull ? (Vec4?)null : v.AsV4());
        public Vec3? EulerN(string name, Vec3? absent = null) => Opt(name, absent, v => v.IsNull ? (Vec3?)null : v.AsV3());
        public Color4? ColN(string name, Color4? absent = null) => Opt(name, absent, v => v.IsNull ? (Color4?)null : v.AsCol());
        public Quat4? QuatN(string name, Quat4? absent = null) => Opt(name, absent, v => v.IsNull ? (Quat4?)null : v.AsQuat());

        // ---------- arrays of scalars (length -1 = any; n = exactly n, else a type error) ----------

        public float[] F32Array(string name, float[] absent, int length = -1) => Opt(name, absent, v => v.AsF32Array(length));
        public int[] IntArray(string name, int[] absent, int length = -1) => Opt(name, absent, v => v.AsIntArray(length));
        public bool[] BoolArray(string name, bool[] absent, int length = -1) => Opt(name, absent, v => v.AsBoolArray(length));
        public string[] StrArray(string name, string[] absent, int length = -1) => Opt(name, absent, v => v.AsStrArray(length));

        // ---------- typed strings ----------

        /// <summary>A `ref` text, verbatim (validated as non-empty "&lt;guid&gt;#&lt;id&gt;"); JSON null is a type error.</summary>
        public string Ref(string name) => Req(name, v => v.AsRef());
        public string Ref(string name, string absent) => Opt(name, absent, v => v.AsRef());
        /// <summary>A `ref?`: the text or null.</summary>
        public string RefN(string name, string absent = null) => Opt(name, absent, v => v.IsNull ? null : v.AsRef());

        /// <summary>An `id`; uppercase hex is lowercased and reported.</summary>
        public string Id(string name) => Req(name, v => v.AsId());
        public string IdN(string name, string absent = null) => Opt(name, absent, v => v.IsNull ? null : v.AsId());

        /// <summary>A `res` name "res/&lt;sha256&gt;.&lt;ext&gt;".</summary>
        public string Res(string name) => Req(name, v => v.AsRes());
        public string Res(string name, string absent) => Opt(name, absent, v => v.AsRes());
        public string ResN(string name, string absent = null) => Opt(name, absent, v => v.IsNull ? null : v.AsRes());

        /// <summary>A `time` "yyyy-MM-ddTHH:mm:ssZ" (UTC).</summary>
        public DateTime Time(string name) => Req(name, v => v.AsTime());
        public DateTime Time(string name, DateTime absent) => Opt(name, absent, v => v.AsTime());

        /// <summary>
        /// An enum string: returns the stored string even when it is not in <paramref name="known"/> (then a
        /// warning <c>json.unknownEnum</c> is reported); the caller's typed view maps unknown strings to the absent value.
        /// </summary>
        public string EnumStr(string name, string absent, ICollection<string> known)
        {
            string s = Opt(name, absent, v => v.AsStr());
            if (s != null && known != null && !known.Contains(s) && Has(name))
                Report(FindingSeverity.Warning, "json.unknownEnum", name, "unknown value \"" + s + "\" kept");
            return s;
        }

        /// <summary>Nullable enum string (null allowed).</summary>
        public string EnumStrN(string name, string absent, ICollection<string> known)
        {
            string s = Opt(name, absent, v => v.AsStrN());
            if (s != null && known != null && !known.Contains(s) && Has(name))
                Report(FindingSeverity.Warning, "json.unknownEnum", name, "unknown value \"" + s + "\" kept");
            return s;
        }

        // ---------- nested objects, arrays, maps ----------

        /// <summary>
        /// A non-null object member. Absent (or a type error when lenient) → a reader over an empty object, so every
        /// nested getter returns its absent value.
        /// </summary>
        public DocReader Obj(string name)
        {
            var r = Opt(name, null, v => v.AsObj());
            // The empty reader's path is computed only if a finding or an error asks for it.
            return r ?? new DocReader(new JObject(), Context, this, name);
        }

        /// <summary>A required non-null object member.</summary>
        public DocReader ObjReq(string name) => Req(name, v => v.AsObj());

        /// <summary>An `object?` member: null when absent or JSON null.</summary>
        public DocReader ObjN(string name) => Opt(name, null, v => v.AsObjN());

        /// <summary>An array of non-null objects; absent → empty list.</summary>
        public List<DocReader> ObjArray(string name, int length = -1) =>
            Opt(name, null, v => v.AsArray(e => e.AsObj(), length)) ?? new List<DocReader>();

        /// <summary>
        /// An array whose elements <paramref name="element"/> converts; absent (or a lenient type error) →
        /// <paramref name="absent"/> (a new empty list when null).
        /// </summary>
        public List<T> Array<T>(string name, Func<DocValue, T> element, List<T> absent = null, int length = -1) =>
            Opt(name, null, v => v.AsArray(element, length)) ?? absent ?? new List<T>();

        /// <summary>A nullable array (`T[]?`): null when absent or JSON null.</summary>
        public List<T> ArrayN<T>(string name, Func<DocValue, T> element, int length = -1) =>
            Opt(name, null, v => v.IsNull ? null : v.AsArray(element, length));

        /// <summary>A `map&lt;T&gt;` (keys ordinal-sorted); absent → empty.</summary>
        public SortedDictionary<string, T> Map<T>(string name, Func<DocValue, T> element) =>
            Opt(name, null, v => v.AsMap(element)) ?? new SortedDictionary<string, T>(StringComparer.Ordinal);

        // ---------- ext and leftovers ----------

        /// <summary>
        /// The <c>ext</c> member: an object whose members are namespaces (no two equal ignoring
        /// case). Absent → empty. Values are detached copies. Never null. A value over the 1 MiB limit is kept (it is
        /// preserved like any unknown data) and reported as warning <c>ext.inlineTooLarge</c>.
        /// </summary>
        public ExtInline Ext()
        {
            var ext = Opt("ext", null, v =>
            {
                if (v.Token.Type != JTokenType.Object) throw v.Error("an object of namespaces");
                var e = new ExtInline();
                foreach (var p in ((JObject)v.Token).Properties())
                {
                    if (!ExtNamespaces.IsValid(p.Name))
                        throw new DocTypeException(Context, p, Join(v.Path, p.Name), "invalid ext namespace '" + p.Name + "'");
                    foreach (var k in e.Namespaces)
                        if (string.Equals(k, p.Name, StringComparison.OrdinalIgnoreCase))
                            throw new DocTypeException(Context, p, Join(v.Path, p.Name), "ext namespaces differ only in case: " + k + ", " + p.Name);
                    e.SetWithoutSizeCheck(p.Name, p.Value.DeepClone());
                    int size = ExtInline.CanonicalSize(p.Value);
                    if (size > ExtInline.MaxBlockBytes)
                        Context.Findings.Warning("ext.inlineTooLarge", Context.Where(Join(v.Path, p.Name)),
                            "inline ext block of " + size + " bytes is over the 1 MiB limit; kept and written back unchanged");
                }
                return e;
            });
            return ext ?? new ExtInline();
        }

        /// <summary>
        /// Unknown members: every member no getter touched, as detached tokens in read order. <c>ext</c> is known
        /// once <see cref="Ext"/> was called; on objects without `ext` in their schema an `ext` member is a leftover.
        /// Call after reading the known members. Null when there are none.
        /// </summary>
        public JsonLeftovers Leftovers()
        {
            JsonLeftovers l = null;
            foreach (var p in obj.Properties())
            {
                if (known != null && known.Contains(p.Name)) continue;
                l ??= new JsonLeftovers();
                l.Add(p.Name, p.Value.DeepClone());
            }
            return l;
        }
    }

    /// <summary>One JSON value with its path: the conversions behind the DocReader getters, for array elements and map values.</summary>
    public sealed class DocValue
    {
        public readonly JToken Token;
        public readonly DocReadContext Context;

        public DocValue(JToken token, DocReadContext ctx)
        {
            Token = token;
            Context = ctx;
        }

        public string Path => Token.Path;
        public bool IsNull => Token.Type == JTokenType.Null;
        public JTokenType Type => Token.Type;

        /// <summary>A type error at this value ("expected X, found Y").</summary>
        public DocTypeException Error(string expected) =>
            new DocTypeException(Context, Token, Path, "expected " + expected + ", found " + DocReader.Describe(Token));

        /// <summary>A detached copy of the token.</summary>
        public JToken Detach() => Token.DeepClone();

        public bool AsBool()
        {
            if (Token.Type == JTokenType.Boolean) return (bool)Token;
            throw Error("a boolean");
        }

        public long AsInt64()
        {
            if (Token.Type == JTokenType.Integer)
            {
                var v = ((JValue)Token).Value;
                if (v is BigInteger) throw Error("an int64");
                return Convert.ToInt64(v, CultureInfo.InvariantCulture);
            }
            if (Token.Type == JTokenType.Float)
            {
                // an integral float token (3.0) is accepted.
                double d = Convert.ToDouble(((JValue)Token).Value, CultureInfo.InvariantCulture);
                if (d == Math.Floor(d) && d >= long.MinValue && d < 9.2233720368547758E+18) return (long)d;
            }
            throw Error("an integer");
        }

        public int AsInt()
        {
            if (Token.Type == JTokenType.Integer || Token.Type == JTokenType.Float)
            {
                long v;
                try { v = AsInt64(); }
                catch (DocTypeException) { throw Error("an int32"); }
                if (v >= int.MinValue && v <= int.MaxValue) return (int)v;
            }
            throw Error("an int32");
        }

        /// <summary>float32: number → double → float; integers accepted; outside the float range → NonFinite.</summary>
        public float AsF32()
        {
            double d;
            if (Token.Type == JTokenType.Float) d = Convert.ToDouble(((JValue)Token).Value, CultureInfo.InvariantCulture);
            else if (Token.Type == JTokenType.Integer) d = (double)(((JValue)Token).Value is BigInteger bi ? (double)bi : Convert.ToInt64(((JValue)Token).Value, CultureInfo.InvariantCulture));
            else throw Error("a number");
            float f = (float)d;
            if (float.IsNaN(f) || float.IsInfinity(f))
                throw new SaveFormatException(SaveErrorCode.NonFinite, "number outside the float32 range", Context.Entry, Path, DocTypeException.LineOf(Token));
            return f;
        }

        public string AsStr()
        {
            if (Token.Type == JTokenType.String) return (string)Token;
            throw Error("a string");
        }

        public string AsStrN() => IsNull ? null : AsStr();

        JArray Arr(int length, string what)
        {
            if (Token.Type != JTokenType.Array) throw Error(what);
            var a = (JArray)Token;
            if (length >= 0 && a.Count != length) throw new DocTypeException(Context, Token, Path, "expected " + what + ", found " + a.Count + " elements");
            return a;
        }

        float El(JArray a, int i) => new DocValue(a[i], Context).AsF32();

        public Vec2 AsV2() { var a = Arr(2, "2 numbers"); return new Vec2(El(a, 0), El(a, 1)); }
        public Vec3 AsV3() { var a = Arr(3, "3 numbers"); return new Vec3(El(a, 0), El(a, 1), El(a, 2)); }
        public Vec4 AsV4() { var a = Arr(4, "4 numbers"); return new Vec4(El(a, 0), El(a, 1), El(a, 2), El(a, 3)); }
        public Color4 AsCol() { var a = Arr(4, "4 numbers (colour)"); return new Color4(El(a, 0), El(a, 1), El(a, 2), El(a, 3)); }
        public Quat4 AsQuat() { var a = Arr(4, "4 numbers (quaternion)"); return new Quat4(El(a, 0), El(a, 1), El(a, 2), El(a, 3)); }

        public float[] AsF32Array(int length = -1)
        {
            var a = Arr(length, length >= 0 ? length + " numbers" : "an array of numbers");
            var r = new float[a.Count];
            for (int i = 0; i < r.Length; i++) r[i] = El(a, i);
            return r;
        }

        public int[] AsIntArray(int length = -1)
        {
            var a = Arr(length, length >= 0 ? length + " integers" : "an array of integers");
            var r = new int[a.Count];
            for (int i = 0; i < r.Length; i++) r[i] = new DocValue(a[i], Context).AsInt();
            return r;
        }

        public bool[] AsBoolArray(int length = -1)
        {
            var a = Arr(length, length >= 0 ? length + " booleans" : "an array of booleans");
            var r = new bool[a.Count];
            for (int i = 0; i < r.Length; i++) r[i] = new DocValue(a[i], Context).AsBool();
            return r;
        }

        public string[] AsStrArray(int length = -1)
        {
            var a = Arr(length, length >= 0 ? length + " strings" : "an array of strings");
            var r = new string[a.Count];
            for (int i = 0; i < r.Length; i++) r[i] = new DocValue(a[i], Context).AsStr();
            return r;
        }

        public List<T> AsArray<T>(Func<DocValue, T> element, int length = -1)
        {
            var a = Arr(length, length >= 0 ? "an array of " + length : "an array");
            var r = new List<T>(a.Count);
            foreach (var e in a) r.Add(element(new DocValue(e, Context)));
            return r;
        }

        public SortedDictionary<string, T> AsMap<T>(Func<DocValue, T> element)
        {
            if (Token.Type != JTokenType.Object) throw Error("an object (map)");
            var m = new SortedDictionary<string, T>(StringComparer.Ordinal);
            foreach (var p in ((JObject)Token).Properties()) m[p.Name] = element(new DocValue(p.Value, Context));
            return m;
        }

        public string AsRef()
        {
            string s = AsStr();
            if (!JsonText.IsRef(s)) throw Error("a ref \"<guid>#<id>\"");
            return s;
        }

        public string AsRefN() => IsNull ? null : AsRef();

        public string AsId()
        {
            string s = AsStr();
            if (JsonText.IsId(s)) return s;
            if (JsonText.IsIdAnyCase(s))
            {
                Context.Findings.Info("json.idUppercase", Context.Where(Path), "id lowercased");
                return s.ToLowerInvariant();
            }
            throw Error("an id (32 hex digits)");
        }

        public string AsIdN() => IsNull ? null : AsId();

        public string AsRes()
        {
            string s = AsStr();
            if (!JsonText.IsResName(s)) throw Error("a res name \"res/<sha256>.<ext>\"");
            return s;
        }

        public string AsResN() => IsNull ? null : AsRes();

        public DateTime AsTime()
        {
            string s = AsStr();
            if (!JsonText.TryParseTime(s, out var t)) throw Error("a time \"yyyy-MM-ddTHH:mm:ssZ\"");
            return t;
        }

        public DocReader AsObj()
        {
            if (Token.Type != JTokenType.Object) throw Error("an object");
            return new DocReader((JObject)Token, Context);
        }

        public DocReader AsObjN() => IsNull ? null : AsObj();
    }
}

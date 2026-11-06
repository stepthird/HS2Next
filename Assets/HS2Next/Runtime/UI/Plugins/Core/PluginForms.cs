// The declarative forms of plugins, Unity-free part: the checks a form passes when a plugin registers it (every refusal is
// an ArgumentException and registers nothing), the host's own copy of the form (later changes to the plugin's objects have
// no effect), the JSON shape of each row kind's value, the envelopes of the read, write, button and choices calls and the
// reading of their replies. A row kind has the name and the value of the host's own row kind (FieldKind), so a plugin row
// draws the widget of the host row of the same kind; the hair-bundle kind edits the host's own data and is refused. The
// form view, the registry of drawn forms and the host widgets are in the Studio part of this folder; the plugin host's
// IPluginForms is in the plugin engine.
using System;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Api.Ui;
using HS2Next.UI.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HS2Next.UI.Plugins
{
    /// <summary>One choice of a row (the copy of a UiChoice, or a choice of a read or choices reply).</summary>
    public sealed class FormChoice
    {
        /// <summary>The value the protocol carries.</summary>
        public string Value;
        /// <summary>A key of the plugin's strings (choices of the form), or the label text of a reply (null = the value).</summary>
        public string Label;
        public override string ToString() => Value + (Label != null ? " (" + Label + ")" : "");
    }

    /// <summary>The host's copy of one UiRow.</summary>
    public sealed class RowDef
    {
        public string Key;
        public RowKind Kind;
        public string LabelKey, TooltipKey;
        public double? Min, Max, TypedMin, TypedMax, Grid;
        public bool Whole, GridTruncate, Wrap360, Nullable, ReadOnly, ColorAlpha = true;
        /// <summary>The reset value as JSON (null = no reset).</summary>
        public JToken Default;
        public string Format;
        public double Scale = 1;
        public List<FormChoice> Choices = new List<FormChoice>();
        public string ChoicesCommand, Command;
        /// <summary>The further arguments of a button's command (null = none).</summary>
        public JObject CommandArgs;
        public int CategoryFemale = -1, CategoryMale = -1;
        public string PushSource;

        /// <summary>True when the row writes values (every kind but a button, unless read-only).</summary>
        public bool Editable => PluginFormRules.IsEditable(Kind, ReadOnly);

        /// <summary>The host row of the same kind and settings (no model path: the value comes from the read command); the
        /// number arithmetic (display scale, typed clamp, grid) is the host's.</summary>
        public FieldSpec ToFieldSpec(string viewPath = null)
        {
            var s = new FieldSpec
            {
                Path = viewPath, Kind = PluginFormRules.FieldKindOf(Kind), LabelKey = LabelKey, TooltipKey = TooltipKey,
                Min = Min, Max = Max, TypedMin = TypedMin, TypedMax = TypedMax, Whole = Whole || Kind == RowKind.Int,
                Format = Format, Scale = Scale == 0 ? 1 : Scale, Grid = Grid, GridTruncate = GridTruncate, Wrap360 = Wrap360,
                Nullable = Nullable, ReadOnly = ReadOnly, ColorAlpha = ColorAlpha, CategoryFemale = CategoryFemale,
                CategoryMale = CategoryMale, PushSource = PushSource,
            };
            if (Kind == RowKind.Enum)
            {
                var values = new List<string>();
                foreach (var c in Choices) values.Add(c.Value);
                s.Choices = values;
            }
            return s;
        }

        public override string ToString() => Key + " (" + Kind + ")";
    }

    /// <summary>The host's copy of a registered form: where it is drawn and its rows.</summary>
    public sealed class FormDef
    {
        /// <summary>The plugin's GUID.</summary>
        public string Guid;
        /// <summary>The id the plugin registered (the "form" of the protocol).</summary>
        public string Id;
        /// <summary>"&lt;guid&gt;/&lt;id&gt;": the panel or section id in the UI registry.</summary>
        public string FullId;
        public string TitleKey;
        /// <summary>True: a panel of <see cref="Place"/> (a region); false: a section of the inspector of the node kind <see cref="Place"/>.</summary>
        public bool IsPanel;
        public string Place;
        public int Order;
        public string ReadCommand, WriteCommand;
        public bool Undo = true;
        public List<RowDef> Rows = new List<RowDef>();

        /// <summary>The element name of the form ("plugin-form-&lt;guid&gt;/&lt;id&gt;").</summary>
        public string ElementName => PluginFormRules.ElementPrefix + FullId;

        /// <summary>The element name of a row ("plugin-form-&lt;guid&gt;/&lt;id&gt;-&lt;key&gt;").</summary>
        public string RowName(string key) => ElementName + "-" + key;

        public RowDef Row(string key)
        {
            foreach (var r in Rows) if (r.Key == key) return r;
            return null;
        }

        public override string ToString() => FullId + (IsPanel ? " panel@" : " section@") + Place + " (" + Rows.Count + " rows)";
    }

    /// <summary>The registration rules of plugin forms, and the copy the host keeps.</summary>
    public static class PluginFormRules
    {
        public const string ElementPrefix = "plugin-form-";
        /// <summary>The regions a panel form may use.</summary>
        public static readonly IReadOnlyList<string> Regions = LayoutRegions.All;
        /// <summary>The node kinds an inspector section form may use.</summary>
        public static readonly IReadOnlyList<string> Kinds = new[] { "chara", "item", "light", "camera", "folder", "route" };
        /// <summary>The host's push sources (the push grids of its own rows).</summary>
        public static readonly IReadOnlyList<string> PushSourceNames = new[]
        {
            PushSources.FacePreset, PushSources.Layout(PushSources.MoleLayoutCategory), PushSources.Layout(PushSources.FacePaintLayoutCategory),
            PushSources.Colour(PushSources.SkinColourCategory), PushSources.Colour(PushSources.HairColourCategory),
        };

        /// <summary>The host row kind of a plugin row kind (the same name and value).</summary>
        public static FieldKind FieldKindOf(RowKind kind) => (FieldKind)(int)kind;

        /// <summary>A row of this kind writes values (every kind but a button, unless read-only).</summary>
        public static bool IsEditable(RowKind kind, bool readOnly) => !readOnly && kind != RowKind.Button;

        /// <summary>The command lies in one of the plugin's command prefixes (longer than the prefix), as its registry requires.</summary>
        public static bool CommandAllowed(string name, IReadOnlyList<string> prefixes)
        {
            if (string.IsNullOrEmpty(name) || prefixes == null) return false;
            foreach (var p in prefixes)
                if (!string.IsNullOrEmpty(p) && name.StartsWith(p, StringComparison.Ordinal) && name.Length > p.Length) return true;
            return false;
        }

        /// <summary>The merge key of a row's typed commits ("&lt;guid&gt;/&lt;form&gt;/&lt;node&gt;/&lt;key&gt;"; no node = empty).</summary>
        public static string MergeKey(string guid, string form, string node, string key) => guid + "/" + form + "/" + (node ?? "") + "/" + key;

        static ArgumentException Refuse(string formId, string why, string param = "form") =>
            new ArgumentException("plugin form " + (string.IsNullOrEmpty(formId) ? "(no id)" : formId) + ": " + why, param);

        /// <summary>
        /// Checks a form registration and returns the host's copy; ArgumentException (registering nothing) for a refused one.
        /// <paramref name="taken"/> answers whether a full id ("&lt;guid&gt;/&lt;id&gt;") is registered already.
        /// </summary>
        public static FormDef Validate(string guid, IReadOnlyList<string> prefixes, bool panel, string place, string id, string titleKey, UiForm form, int order,
            Func<string, bool> taken = null)
        {
            if (string.IsNullOrEmpty(id)) throw Refuse(id, "a form needs an id", nameof(id));
            string full = guid + "/" + id;
            if (taken != null && taken(full)) throw Refuse(id, "the id is registered already", nameof(id));
            if (panel)
            {
                if (!LayoutRegions.IsRegion(place)) throw Refuse(id, "not a region (left, right, bottom): " + (place ?? "null"), "region");
            }
            else if (!In(Kinds, place))
                throw Refuse(id, "not a node kind (chara, item, light, camera, folder, route): " + (place ?? "null"), "kind");
            if (form == null) throw new ArgumentNullException(nameof(form), "plugin form " + id + ": the form is null");
            if (string.IsNullOrEmpty(form.ReadCommand)) throw Refuse(id, "the read command is empty");
            Command(id, "the read command", form.ReadCommand, prefixes);
            if (!string.IsNullOrEmpty(form.WriteCommand)) Command(id, "the write command", form.WriteCommand, prefixes);
            var def = new FormDef
            {
                Guid = guid, Id = id, FullId = full, TitleKey = titleKey, IsPanel = panel, Place = place, Order = order,
                ReadCommand = form.ReadCommand, WriteCommand = string.IsNullOrEmpty(form.WriteCommand) ? null : form.WriteCommand, Undo = form.Undo,
            };
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var rows = form.Rows ?? (IList<UiRow>)Array.Empty<UiRow>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r == null) throw Refuse(id, "row " + i + " is null");
                if (string.IsNullOrEmpty(r.Key)) throw Refuse(id, "row " + i + " has no key");
                if (!keys.Add(r.Key)) throw Refuse(id, "the row key " + r.Key + " is used twice");
                def.Rows.Add(Row(id, r, prefixes));
            }
            if (def.WriteCommand == null)
                foreach (var r in def.Rows)
                    if (r.Editable) throw Refuse(id, "the row " + r.Key + " is editable and the form has no write command");
            return def;
        }

        static void Command(string formId, string what, string name, IReadOnlyList<string> prefixes)
        {
            if (prefixes == null || prefixes.Count == 0) throw Refuse(formId, "the plugin has no command prefixes, so " + what + " " + name + " cannot be its command");
            if (!CommandAllowed(name, prefixes))
                throw Refuse(formId, what + " " + name + " is outside the plugin's command prefixes (" + string.Join(", ", prefixes) + ")");
        }

        static RowDef Row(string formId, UiRow r, IReadOnlyList<string> prefixes)
        {
            string at = "the row " + r.Key;
            if (!Enum.IsDefined(typeof(RowKind), r.Kind)) throw Refuse(formId, at + ": not a row kind (" + (int)r.Kind + ")");
            if (r.Kind == RowKind.HairBundles) throw Refuse(formId, at + ": the kind HairBundles is reserved (it edits the host's own hair bundles)");
            if (r.Min.HasValue && r.Max.HasValue && r.Min.Value > r.Max.Value) throw Refuse(formId, at + ": Min " + Num(r.Min.Value) + " is above Max " + Num(r.Max.Value));
            var d = new RowDef
            {
                Key = r.Key, Kind = r.Kind, LabelKey = r.LabelKey, TooltipKey = r.TooltipKey, Min = r.Min, Max = r.Max, TypedMin = r.TypedMin,
                TypedMax = r.TypedMax, Whole = r.Whole || r.Kind == RowKind.Int, Format = r.Format, Scale = r.Scale, Grid = r.Grid,
                GridTruncate = r.GridTruncate, Wrap360 = r.Wrap360, Nullable = r.Nullable, ReadOnly = r.ReadOnly, ColorAlpha = r.ColorAlpha,
                ChoicesCommand = string.IsNullOrEmpty(r.ChoicesCommand) ? null : r.ChoicesCommand,
                Command = string.IsNullOrEmpty(r.Command) ? null : r.Command,
                CategoryFemale = r.CategoryFemale, CategoryMale = r.CategoryMale, PushSource = r.PushSource,
            };
            if (r.Choices != null)
                foreach (var c in r.Choices)
                    if (c != null) d.Choices.Add(new FormChoice { Value = c.Value ?? "", Label = c.LabelKey });
            switch (r.Kind)
            {
                case RowKind.Enum:
                    if (d.Choices.Count == 0) throw Refuse(formId, at + ": an Enum row needs choices");
                    break;
                case RowKind.Button:
                    if (d.Command == null) throw Refuse(formId, at + ": a Button row needs a command");
                    Command(formId, at + "'s command", d.Command, prefixes);
                    break;
                case RowKind.PartList:
                    if (d.CategoryFemale < 0 && d.CategoryMale < 0) throw Refuse(formId, at + ": a PartList row needs a list category");
                    break;
                case RowKind.Push:
                    if (!In(PushSourceNames, d.PushSource))
                        throw Refuse(formId, at + ": not a host push source: " + (d.PushSource ?? "null") + " (" + string.Join(", ", PushSourceNames) + ")");
                    break;
            }
            if (d.ChoicesCommand != null) Command(formId, at + "'s choices command", d.ChoicesCommand, prefixes);
            if (r.CommandArgs != null)
            {
                d.CommandArgs = new JObject();
                foreach (var kv in r.CommandArgs)
                    if (kv.Key != null) d.CommandArgs[kv.Key] = ToJson(formId, at + "'s command argument " + kv.Key, kv.Value);
            }
            if (r.Default != null) d.Default = ToJson(formId, at + "'s default", r.Default);
            return d;
        }

        static JToken ToJson(string formId, string what, object v)
        {
            if (v == null) return JValue.CreateNull();
            if (v is JToken t) return t.DeepClone();
            try { return JToken.FromObject(v); }
            catch (Exception e) when (e is JsonException || e is ArgumentException || e is InvalidOperationException)
            {
                throw Refuse(formId, what + " is not a JSON value (" + e.GetType().Name + ")");
            }
        }

        static bool In(IReadOnlyList<string> list, string v)
        {
            if (v == null) return false;
            foreach (var x in list) if (x == v) return true;
            return false;
        }

        static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>The value shapes of the row kinds (JSON), and the reading of numbers, vectors and colours.</summary>
    public static class FormValues
    {
        /// <summary>
        /// True when <paramref name="v"/> has the shape of <paramref name="kind"/>: Float / Int a number; Bool a bool; Color
        /// [r, g, b, a]; ColorHdr {"color": [r, g, b, a], "intensity": n}; Vec2 / Vec3 arrays of 2 / 3 numbers; Enum a string;
        /// Ref, Node, File, Text, AttachPoint, PartList a string or null; Button and Push have no value (anything). Otherwise
        /// <paramref name="problem"/> says what was found.
        /// </summary>
        public static bool Fits(RowKind kind, JToken v, out string problem)
        {
            problem = null;
            var t = v?.Type ?? JTokenType.Null;
            switch (kind)
            {
                case RowKind.Button:
                case RowKind.Push:
                case RowKind.HairBundles:
                    return true;
                case RowKind.Float:
                case RowKind.Int:
                    if (t == JTokenType.Integer || t == JTokenType.Float) return true;
                    problem = "a number was expected, found " + Describe(v);
                    return false;
                case RowKind.Bool:
                    if (t == JTokenType.Boolean) return true;
                    problem = "a bool was expected, found " + Describe(v);
                    return false;
                case RowKind.Color:
                    if (Numbers(v, 4) != null) return true;
                    problem = "[r, g, b, a] was expected, found " + Describe(v);
                    return false;
                case RowKind.ColorHdr:
                    if (v is JObject o && Numbers(o["color"], 4) != null && IsNumber(o["intensity"])) return true;
                    problem = "{\"color\": [r, g, b, a], \"intensity\": n} was expected, found " + Describe(v);
                    return false;
                case RowKind.Vec2:
                case RowKind.Vec3:
                    int n = kind == RowKind.Vec2 ? 2 : 3;
                    if (Numbers(v, n) != null) return true;
                    problem = "an array of " + n + " numbers was expected, found " + Describe(v);
                    return false;
                case RowKind.Enum:
                    if (t == JTokenType.String) return true;
                    problem = "a choice's value (a string) was expected, found " + Describe(v);
                    return false;
                default:
                    if (t == JTokenType.String || t == JTokenType.Null) return true;
                    problem = "a string or null was expected, found " + Describe(v);
                    return false;
            }
        }

        static bool IsNumber(JToken v) => v != null && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float);

        /// <summary>The numbers of an array of exactly <paramref name="count"/> numbers, or null.</summary>
        public static double[] Numbers(JToken v, int count)
        {
            if (!(v is JArray a) || a.Count != count) return null;
            var d = new double[count];
            for (int i = 0; i < count; i++)
            {
                if (!IsNumber(a[i])) return null;
                d[i] = (double)a[i];
            }
            return d;
        }

        /// <summary>A number of the reply (null when it is not one).</summary>
        public static double? Number(JToken v) => IsNumber(v) ? (double)v : (double?)null;

        /// <summary>A number as the protocol writes it (a whole number for whole rows).</summary>
        public static JToken Number(double v, bool whole) => whole ? new JValue((long)Math.Round(v, MidpointRounding.AwayFromZero)) : new JValue(v);

        /// <summary>An array of numbers ([x, y], [x, y, z], [r, g, b, a]).</summary>
        public static JToken Array(params double[] v)
        {
            var a = new JArray();
            foreach (var x in v) a.Add(new JValue(x));
            return a;
        }

        /// <summary>{"color": [r, g, b, a], "intensity": n}.</summary>
        public static JToken Hdr(double r, double g, double b, double a, double intensity) =>
            new JObject { ["color"] = Array(r, g, b, a), ["intensity"] = new JValue(intensity) };

        /// <summary>A string, or JSON null.</summary>
        public static JToken Text(string s) => s == null ? (JToken)JValue.CreateNull() : new JValue(s);

        /// <summary>Both values are the same JSON (null and absent are the same).</summary>
        public static bool Same(JToken a, JToken b)
        {
            bool an = a == null || a.Type == JTokenType.Null, bn = b == null || b.Type == JTokenType.Null;
            if (an || bn) return an && bn;
            if (IsNumber(a) && IsNumber(b)) return (double)a == (double)b;
            return JToken.DeepEquals(a, b);
        }

        /// <summary>A short description of a value for the log.</summary>
        public static string Describe(JToken v)
        {
            if (v == null) return "nothing";
            string s = v.ToString(Formatting.None);
            return v.Type.ToString().ToLowerInvariant() + " " + (s.Length > 60 ? s.Substring(0, 60) + "…" : s);
        }
    }

    /// <summary>A read reply: the values, the hidden and disabled rows, the choices that replace a row's own, or the error.</summary>
    public sealed class FormReadResult
    {
        public bool Ok;
        /// <summary>The error text of a failed read (code and message).</summary>
        public string Error;
        public JObject Values = new JObject();
        public readonly HashSet<string> Hidden = new HashSet<string>(StringComparer.Ordinal);
        public readonly HashSet<string> Disabled = new HashSet<string>(StringComparer.Ordinal);
        public readonly Dictionary<string, List<FormChoice>> Choices = new Dictionary<string, List<FormChoice>>(StringComparer.Ordinal);
    }

    /// <summary>The envelopes the host sends to a form's commands and the reading of the replies.</summary>
    public static class FormProtocol
    {
        public const string Preview = "preview", Commit = "commit", Cancel = "cancel", Undo = "undo", Redo = "redo";

        /// <summary>The protocol name of an edit phase.</summary>
        public static string PhaseName(EditPhase p) => p == EditPhase.Preview ? Preview : p == EditPhase.Cancel ? Cancel : Commit;

        static JObject Base(string cmd, string form, string node)
        {
            var o = new JObject { ["cmd"] = cmd, ["form"] = form };
            o["node"] = node == null ? JValue.CreateNull() : new JValue(node);
            return o;
        }

        /// <summary>{"cmd": read, "form", "node"}.</summary>
        public static string Read(string cmd, string form, string node) => Base(cmd, form, node).ToString(Formatting.None);

        /// <summary>{"cmd": write, "form", "node", "key", "value", "phase", "undo": false}.</summary>
        public static string Write(string cmd, string form, string node, string key, JToken value, string phase)
        {
            var o = Base(cmd, form, node);
            o["key"] = key;
            o["value"] = value?.DeepClone() ?? JValue.CreateNull();
            o["phase"] = phase;
            o["undo"] = false;
            return o.ToString(Formatting.None);
        }

        /// <summary>{"cmd": row.Command, "form", "node", "key"} with the row's further arguments first (they cannot replace the four).</summary>
        public static string Button(RowDef row, string form, string node)
        {
            var o = new JObject();
            if (row.CommandArgs != null) foreach (var p in row.CommandArgs.Properties()) o[p.Name] = p.Value.DeepClone();
            var b = Base(row.Command, form, node);
            foreach (var p in b.Properties()) o[p.Name] = p.Value;
            o["key"] = row.Key;
            return o.ToString(Formatting.None);
        }

        /// <summary>{"cmd": row.ChoicesCommand, "form", "node", "key"}.</summary>
        public static string Choices(string cmd, string form, string node, string key)
        {
            var o = Base(cmd, form, node);
            o["key"] = key;
            return o.ToString(Formatting.None);
        }

        static JObject Parse(string reply)
        {
            try
            {
                using (var r = new JsonTextReader(new System.IO.StringReader(reply ?? "")) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double })
                    return JToken.ReadFrom(r) as JObject;
            }
            catch (JsonException) { return null; }
        }

        /// <summary>Ok, or the reply's error text ("code: message"); a reply that is not an object is an error too.</summary>
        public static bool Succeeded(string reply, out string error, out JObject parsed)
        {
            parsed = Parse(reply);
            if (parsed == null) { error = "the reply is not a JSON object"; return false; }
            if (parsed["ok"]?.Type == JTokenType.Boolean && (bool)parsed["ok"]) { error = null; return true; }
            string code = (string)parsed["code"], msg = (string)parsed["error"];
            error = (code ?? "error") + (string.IsNullOrEmpty(msg) ? "" : ": " + msg);
            return false;
        }

        /// <summary>A read reply (its values object is required).</summary>
        public static FormReadResult ParseRead(string reply)
        {
            var r = new FormReadResult();
            if (!Succeeded(reply, out var error, out var o)) { r.Error = error; return r; }
            if (!(o["values"] is JObject values)) { r.Error = "the read reply has no values object"; return r; }
            r.Ok = true;
            r.Values = values;
            Keys(o["hidden"], r.Hidden);
            Keys(o["disabled"], r.Disabled);
            if (o["choices"] is JObject ch)
                foreach (var p in ch.Properties()) r.Choices[p.Name] = ParseChoices(p.Value);
            return r;
        }

        static void Keys(JToken t, HashSet<string> into)
        {
            if (!(t is JArray a)) return;
            foreach (var x in a) if (x.Type == JTokenType.String) into.Add((string)x);
        }

        /// <summary>[{value, label}] (a label may be absent; a plain string is a value labelled by itself).</summary>
        public static List<FormChoice> ParseChoices(JToken t)
        {
            var l = new List<FormChoice>();
            if (!(t is JArray a)) return l;
            foreach (var x in a)
            {
                if (x.Type == JTokenType.String) l.Add(new FormChoice { Value = (string)x });
                else if (x is JObject c)
                {
                    var v = c["value"];
                    l.Add(new FormChoice
                    {
                        Value = v == null || v.Type == JTokenType.Null ? null : v.Type == JTokenType.String ? (string)v : v.ToString(Formatting.None),
                        Label = c["label"]?.Type == JTokenType.String ? (string)c["label"] : null,
                    });
                }
            }
            return l;
        }

        /// <summary>A choices reply ({choices: [{value, label}]}), or the error.</summary>
        public static List<FormChoice> ParseChoicesReply(string reply, out string error)
        {
            if (!Succeeded(reply, out error, out var o)) return null;
            if (!(o["choices"] is JArray)) { error = "the choices reply has no choices array"; return null; }
            return ParseChoices(o["choices"]);
        }
    }
}

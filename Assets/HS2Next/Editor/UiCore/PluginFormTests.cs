// PluginFormTests (KU1, Mono.ui): the declarative forms of plugins, Unity-free part. The plugin row kinds have the names and
// values of the host's row kinds (FieldKind) in the same order; every registration rule refuses with an ArgumentException
// and registers nothing; a valid form is copied (later changes to the plugin's objects have no effect); the value shapes of
// each kind; the read, write, button and choices envelopes and the reading of their replies; the merge key; and the
// localiser's fallback for a label key that no table has (the key itself is shown).
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Api.Ui;
using HS2Next.UI.Plugins;
using Newtonsoft.Json.Linq;

namespace HS2Next.UI.Core.Tests
{
    using HS2Next.Save.Tests;
    using HS2Next.UI.Core;

    public static class PluginFormTests
    {
        const string G = "test.forms";
        static readonly IReadOnlyList<string> Prefixes = new[] { "tf." };

        public static void Run(TestRunner t)
        {
            Mapping(t);
            Valid(t);
            Refusals(t);
            Copy(t);
            Shapes(t);
            Envelopes(t);
            Replies(t);
            Fallback(t);
        }

        static void Mapping(TestRunner t)
        {
            var rk = Enum.GetNames(typeof(RowKind));
            var fk = Enum.GetNames(typeof(FieldKind));
            t.Equal(17, rk.Length, "RowKind has 17 names");
            t.Check(rk.SequenceEqual(fk), "RowKind and FieldKind have the same names in the same order: " + string.Join(",", rk) + " / " + string.Join(",", fk));
            bool values = true;
            foreach (RowKind k in Enum.GetValues(typeof(RowKind)))
            {
                var f = PluginFormRules.FieldKindOf(k);
                if ((int)f != (int)k || f.ToString() != k.ToString()) values = false;
            }
            t.Check(values, "every RowKind maps to the FieldKind of the same name and value (0-16)");
        }

        static UiForm Full()
        {
            var f = new UiForm("tf.read", "tf.write");
            f.Rows.Add(UiRow.Float("f", "k.f", 0, 10, 5, "0.0"));
            f.Rows.Add(UiRow.Int("i", "k.i", 0, 20, 3));
            f.Rows.Add(UiRow.Bool("b", "k.b"));
            f.Rows.Add(UiRow.Color("c", "k.c", false));
            f.Rows.Add(new UiRow { Key = "h", Kind = RowKind.ColorHdr, LabelKey = "k.h" });
            f.Rows.Add(UiRow.Vec2("v2", "k.v2"));
            f.Rows.Add(UiRow.Vec3("v3", "k.v3"));
            f.Rows.Add(UiRow.Enum("e", "k.e", new UiChoice("soft", "k.soft"), new UiChoice("hard", "k.hard")));
            f.Rows.Add(UiRow.Ref("r", "k.r", "tf.choices"));
            f.Rows.Add(UiRow.File("file", "k.file"));
            f.Rows.Add(UiRow.Text("t", "k.t"));
            f.Rows.Add(UiRow.Node("n", "k.n"));
            var btn = UiRow.Button("btn", "k.btn", "tf.button");
            btn.CommandArgs = new Dictionary<string, object> { ["extra"] = 2 };
            f.Rows.Add(btn);
            f.Rows.Add(UiRow.PartList("pl", "k.pl", 1, -1));
            f.Rows.Add(UiRow.Push("push", "k.push", "colour:336"));
            f.Rows.Add(UiRow.AttachPoint("ap", "k.ap"));
            return f;
        }

        static FormDef Panel(UiForm f, string region = "bottom", string id = "p", Func<string, bool> taken = null) =>
            PluginFormRules.Validate(G, Prefixes, true, region, id, "k.title", f, 3, taken);

        static FormDef Section(UiForm f, string kind = "item", string id = "s") =>
            PluginFormRules.Validate(G, Prefixes, false, kind, id, "k.title", f, 0);

        static void Valid(TestRunner t)
        {
            var d = Panel(Full());
            t.Check(d != null && d.IsPanel && d.Place == "bottom" && d.FullId == G + "/p" && d.Order == 3 && d.TitleKey == "k.title" && d.Undo,
                "a panel with one row of each of the 16 drawn kinds is accepted: " + d);
            t.Equal(16, d?.Rows.Count ?? 0, "the copy holds the 16 rows");
            t.Equal("plugin-form-" + G + "/p", d?.ElementName, "the form's element name");
            t.Equal("plugin-form-" + G + "/p-f", d?.RowName("f"), "a row's element name");
            foreach (var r in new[] { "left", "right", "bottom" }) t.Check(Panel(Full(), r) != null, "region " + r + " is accepted");
            foreach (var k in new[] { "chara", "item", "light", "camera", "folder", "route" }) t.Check(Section(Full(), k)?.Place == k, "section kind " + k + " is accepted");
            var ro = new UiForm("tf.read");
            ro.Rows.Add(new UiRow { Key = "x", Kind = RowKind.Float, ReadOnly = true });
            ro.Rows.Add(UiRow.Button("go", "k.go", "tf.go"));
            t.Check(Panel(ro) != null, "a form without a write command is accepted when no row is editable (read-only rows, buttons)");
            t.Check(Panel(new UiForm("tf.read") { Rows = null }) != null, "a form with no rows (Rows null) is accepted");
            foreach (var src in new[] { "facePreset", "layout:6", "layout:7", "colour:336", "colour:305" })
            {
                var f = new UiForm("tf.read", "tf.write");
                f.Rows.Add(UiRow.Push("p", "k", src));
                t.Check(Panel(f) != null, "the host push source " + src + " is accepted");
            }
            var m = new UiForm("tf.read", "tf.write");
            m.Rows.Add(UiRow.PartList("pm", "k", -1, 3));
            t.Check(Panel(m) != null, "a PartList row with only a male category is accepted");
            var i = d.Row("i");
            t.Check(i != null && i.Whole && i.Min == 0 && i.Max == 20 && i.Default?.Type == JTokenType.Integer && (int)i.Default == 3, "Int(): whole, range and default copied");
            var f0 = d.Row("f");
            t.Check(f0 != null && f0.Format == "0.0" && (double)f0.Default == 5, "Float(): format and default copied");
            var spec = f0.ToFieldSpec();
            t.Check(spec.Kind == FieldKind.Float && spec.Min == 0 && spec.Max == 10 && spec.LabelKey == "k.f", "a row's host spec has its kind, range and label");
            var btn = d.Row("btn");
            t.Check(btn.CommandArgs != null && (int)btn.CommandArgs["extra"] == 2 && !btn.Editable, "Button(): command arguments copied; a button is not editable");
            t.Check(d.Row("c").ColorAlpha == false && d.Row("h").ColorAlpha, "Color(alpha: false) is kept; ColorAlpha defaults to true");
            t.Check(d.Row("e").Choices.Select(c => c.Value).SequenceEqual(new[] { "soft", "hard" }) && d.Row("e").Choices[0].Label == "k.soft", "Enum(): the choices in their order with their label keys");
            var sc = Section(Full());
            t.Check(sc != null && !sc.IsPanel && sc.Place == "item", "an item section is accepted");
        }

        static void Refuses(TestRunner t, string what, Func<FormDef> register, string mustContain = null)
        {
            var e = t.Throws<ArgumentException>(() => register(), "refused: " + what);
            if (e != null && mustContain != null) t.Check(e.Message.Contains(mustContain), "the refusal of " + what + " says why: " + e.Message);
        }

        static UiForm With(params UiRow[] rows)
        {
            var f = new UiForm("tf.read", "tf.write");
            foreach (var r in rows) f.Rows.Add(r);
            return f;
        }

        static void Refusals(TestRunner t)
        {
            Refuses(t, "an empty id", () => Panel(Full(), id: ""), "needs an id");
            Refuses(t, "a null id", () => Panel(Full(), id: null), "needs an id");
            Refuses(t, "a duplicate id", () => Panel(Full(), id: "p", taken: full => full == G + "/p"), "registered already");
            Refuses(t, "a region outside left / right / bottom", () => Panel(Full(), "top"), "not a region");
            Refuses(t, "a null region", () => Panel(Full(), null), "not a region");
            Refuses(t, "a node kind outside the list", () => Section(Full(), "scene"), "not a node kind");
            t.Throws<ArgumentNullException>(() => Panel(null), "refused: a null form (ArgumentNullException, an ArgumentException)");
            Refuses(t, "an empty read command", () => Panel(new UiForm("", "tf.write")), "read command is empty");
            Refuses(t, "a read command outside the prefixes", () => Panel(new UiForm("other.read", "tf.write")), "outside the plugin's command prefixes");
            Refuses(t, "a read command equal to the prefix", () => Panel(new UiForm("tf.", "tf.write")), "outside");
            Refuses(t, "a write command outside the prefixes", () => Panel(new UiForm("tf.read", "x.write")), "the write command");
            Refuses(t, "any command when the plugin has no prefixes",
                () => PluginFormRules.Validate(G, new string[0], true, "left", "p", null, new UiForm("tf.read"), 0), "no command prefixes");
            Refuses(t, "a null row", () => Panel(With(UiRow.Bool("a", "k"), null)), "is null");
            Refuses(t, "an empty row key", () => Panel(With(UiRow.Bool("", "k"))), "has no key");
            Refuses(t, "a duplicate row key", () => Panel(With(UiRow.Bool("a", "k"), UiRow.Text("a", "k"))), "used twice");
            Refuses(t, "Min above Max", () => Panel(With(UiRow.Float("f", "k", 2, 1))), "above Max");
            Refuses(t, "an Enum row without choices (null)", () => Panel(With(new UiRow { Key = "e", Kind = RowKind.Enum })), "needs choices");
            Refuses(t, "an Enum row without choices (empty)", () => Panel(With(UiRow.Enum("e", "k"))), "needs choices");
            Refuses(t, "a Button row without a command", () => Panel(With(new UiRow { Key = "b", Kind = RowKind.Button })), "needs a command");
            Refuses(t, "a Button command outside the prefixes", () => Panel(With(UiRow.Button("b", "k", "x.go"))), "outside");
            Refuses(t, "a choices command outside the prefixes", () => Panel(With(UiRow.Ref("r", "k", "x.choices"))), "choices command");
            var noWrite = new UiForm("tf.read");
            noWrite.Rows.Add(UiRow.Float("f", "k", 0, 1));
            Refuses(t, "an editable row without a write command", () => Panel(noWrite), "no write command");
            Refuses(t, "a PartList row without a category", () => Panel(With(UiRow.PartList("pl", "k", -1, -1))), "needs a list category");
            Refuses(t, "a Push row with another source", () => Panel(With(UiRow.Push("p", "k", "layout:5"))), "not a host push source");
            Refuses(t, "a Push row without a source", () => Panel(With(UiRow.Push("p", "k", null))), "not a host push source");
            Refuses(t, "a HairBundles row", () => Panel(With(new UiRow { Key = "hair", Kind = RowKind.HairBundles })), "reserved");
            Refuses(t, "an undefined row kind", () => Panel(With(new UiRow { Key = "x", Kind = (RowKind)99 })), "not a row kind");
        }

        static void Copy(TestRunner t)
        {
            var f = Full();
            var d = Panel(f);
            f.Rows.Add(UiRow.Bool("late", "k"));
            f.Rows[0].Key = "renamed";
            f.Rows[0].Max = 99;
            ((UiChoice[])f.Rows[7].Choices)[0].Value = "changed";
            f.ReadCommand = "tf.other";
            f.Undo = false;
            t.Check(d.Rows.Count == 16 && d.Rows[0].Key == "f" && d.Rows[0].Max == 10 && d.Row("e").Choices[0].Value == "soft" && d.ReadCommand == "tf.read" && d.Undo,
                "the host's copy does not follow later changes of the form, its rows or their choices");
            var u = new UiForm("tf.read", "tf.write") { Undo = false };
            t.Check(!Panel(u).Undo, "UiForm.Undo false is copied");
        }

        static void Fits(TestRunner t, RowKind k, string json, bool expect)
        {
            JToken v = json == null ? null : JToken.Parse(json);
            bool ok = FormValues.Fits(k, v, out var problem);
            t.Check(ok == expect, k + " " + (json ?? "absent") + (expect ? " fits" : " does not fit (" + problem + ")"));
        }

        static void Shapes(TestRunner t)
        {
            Fits(t, RowKind.Float, "1.5", true); Fits(t, RowKind.Float, "2", true); Fits(t, RowKind.Float, "\"1\"", false); Fits(t, RowKind.Float, "null", false);
            Fits(t, RowKind.Int, "3", true); Fits(t, RowKind.Int, "true", false);
            Fits(t, RowKind.Bool, "true", true); Fits(t, RowKind.Bool, "1", false);
            Fits(t, RowKind.Color, "[1, 0.5, 0, 1]", true); Fits(t, RowKind.Color, "[1, 0.5, 0]", false); Fits(t, RowKind.Color, "[1, \"x\", 0, 1]", false);
            Fits(t, RowKind.ColorHdr, "{\"color\": [1, 1, 1, 1], \"intensity\": 2}", true); Fits(t, RowKind.ColorHdr, "[1, 1, 1, 1]", false);
            Fits(t, RowKind.ColorHdr, "{\"color\": [1, 1, 1, 1]}", false);
            Fits(t, RowKind.Vec2, "[1, 2]", true); Fits(t, RowKind.Vec2, "[1, 2, 3]", false);
            Fits(t, RowKind.Vec3, "[1, 2, 3]", true); Fits(t, RowKind.Vec3, "{\"x\": 1}", false);
            Fits(t, RowKind.Enum, "\"soft\"", true); Fits(t, RowKind.Enum, "0", false);
            foreach (var k in new[] { RowKind.Ref, RowKind.Node, RowKind.File, RowKind.Text, RowKind.AttachPoint, RowKind.PartList })
            {
                Fits(t, k, "\"a\"", true); Fits(t, k, "null", true); Fits(t, k, "1", false);
            }
            Fits(t, RowKind.Button, "1", true); Fits(t, RowKind.Push, null, true);
            t.Equal("[1.0,2.5]", FormValues.Array(1, 2.5).ToString(Newtonsoft.Json.Formatting.None), "a vector value is an array of numbers");
            t.Equal("3", FormValues.Number(2.6, true).ToString(Newtonsoft.Json.Formatting.None), "a whole number is written rounded");
            t.Equal("{\"color\":[1.0,0.0,0.0,1.0],\"intensity\":2.0}", FormValues.Hdr(1, 0, 0, 1, 2).ToString(Newtonsoft.Json.Formatting.None), "an HDR colour value");
            t.Check(FormValues.Same(JToken.Parse("1"), JToken.Parse("1.0")) && FormValues.Same(null, JToken.Parse("null")) && !FormValues.Same(JToken.Parse("[1,2]"), JToken.Parse("[1,3]")),
                "Same: numbers by value, null = absent, arrays by content");
        }

        static void Envelopes(TestRunner t)
        {
            t.Equal("{\"cmd\":\"tf.read\",\"form\":\"p\",\"node\":null}", FormProtocol.Read("tf.read", "p", null), "the read envelope of a panel (node null)");
            t.Equal("{\"cmd\":\"tf.read\",\"form\":\"s\",\"node\":\"n1\"}", FormProtocol.Read("tf.read", "s", "n1"), "the read envelope of a section (the node)");
            var w = JObject.Parse(FormProtocol.Write("tf.write", "p", null, "f", new JValue(2.5), FormProtocol.Commit));
            t.Check((string)w["cmd"] == "tf.write" && (string)w["form"] == "p" && w["node"].Type == JTokenType.Null && (string)w["key"] == "f" && (double)w["value"] == 2.5 &&
                    (string)w["phase"] == "commit" && w["undo"].Type == JTokenType.Boolean && !(bool)w["undo"],
                "the write envelope: cmd, form, node, key, value, phase, undo false — " + w.ToString(Newtonsoft.Json.Formatting.None));
            t.Check(new[] { EditPhase.Preview, EditPhase.Commit, EditPhase.Cancel }.Select(FormProtocol.PhaseName).SequenceEqual(new[] { "preview", "commit", "cancel" }) &&
                    FormProtocol.Undo == "undo" && FormProtocol.Redo == "redo", "the phase names preview, commit, cancel, undo, redo");
            var d = Panel(Full());
            var b = JObject.Parse(FormProtocol.Button(d.Row("btn"), "p", "n2"));
            t.Check((string)b["cmd"] == "tf.button" && (string)b["form"] == "p" && (string)b["node"] == "n2" && (string)b["key"] == "btn" && (int)b["extra"] == 2,
                "the button envelope with the row's further arguments: " + b.ToString(Newtonsoft.Json.Formatting.None));
            var row = new RowDef { Key = "k", Command = "tf.go", CommandArgs = new JObject { ["cmd"] = "evil", ["key"] = "other", ["a"] = 1 } };
            var b2 = JObject.Parse(FormProtocol.Button(row, "p", null));
            t.Check((string)b2["cmd"] == "tf.go" && (string)b2["key"] == "k" && (int)b2["a"] == 1, "further arguments cannot replace cmd, form, node or key");
            t.Equal("{\"cmd\":\"tf.choices\",\"form\":\"p\",\"node\":null,\"key\":\"r\"}", FormProtocol.Choices("tf.choices", "p", null, "r"), "the choices envelope");
            t.Equal(G + "/p/n1/f", PluginFormRules.MergeKey(G, "p", "n1", "f"), "the merge key <guid>/<form>/<node>/<key>");
            t.Equal(G + "/p//f", PluginFormRules.MergeKey(G, "p", null, "f"), "the merge key of a panel row (no node)");
        }

        static void Replies(TestRunner t)
        {
            var r = FormProtocol.ParseRead("{\"ok\":true,\"cmd\":\"tf.read\",\"values\":{\"f\":2,\"t\":\"x\"},\"hidden\":[\"b\"],\"disabled\":[\"t\",3],\"choices\":{\"e\":[{\"value\":\"a\",\"label\":\"A\"},\"b\"]}}");
            t.Check(r.Ok && (int)r.Values["f"] == 2 && r.Hidden.SetEquals(new[] { "b" }) && r.Disabled.SetEquals(new[] { "t" }) && r.Choices["e"].Count == 2 &&
                    r.Choices["e"][0].Value == "a" && r.Choices["e"][0].Label == "A" && r.Choices["e"][1].Value == "b" && r.Choices["e"][1].Label == null,
                "a read reply: values, hidden, disabled (strings only), choices ({value, label} or a plain string)");
            var e = FormProtocol.ParseRead("{\"ok\":false,\"cmd\":\"tf.read\",\"error\":\"no such thing\",\"code\":\"not_found\"}");
            t.Check(!e.Ok && e.Error == "not_found: no such thing", "a failed read gives its code and message: " + e.Error);
            var nv = FormProtocol.ParseRead("{\"ok\":true,\"cmd\":\"tf.read\"}");
            t.Check(!nv.Ok && nv.Error.Contains("no values"), "a read reply without values is an error: " + nv.Error);
            var bad = FormProtocol.ParseRead("not json");
            t.Check(!bad.Ok && bad.Error.Contains("not a JSON object"), "a reply that is not JSON is an error");
            t.Check(FormProtocol.Succeeded("{\"ok\":true}", out _, out _) && !FormProtocol.Succeeded("{\"ok\":false,\"code\":\"busy\",\"error\":\"later\"}", out var err, out _) && err == "busy: later",
                "a write reply: ok, or code: message");
            var ch = FormProtocol.ParseChoicesReply("{\"ok\":true,\"choices\":[{\"value\":\"x\",\"label\":\"X\"},{\"value\":null}]}", out var cerr);
            t.Check(ch != null && cerr == null && ch.Count == 2 && ch[1].Value == null, "a choices reply (a null value is a choice too)");
            t.Check(FormProtocol.ParseChoicesReply("{\"ok\":true}", out cerr) == null && cerr.Contains("no choices"), "a choices reply without choices is an error");
        }

        static void Fallback(TestRunner t)
        {
            var l = new Localizer();
            l.Load(L10nTable.Parse("key\ten\tja\tzh-Hans\tnote\nplugin.test.forms.known\tKnown\t既知\t已知\t\n"));
            l.SetLanguage("ja");
            string missing = null;
            l.Missing += (k, lang) => missing = k + "@" + lang;
            t.Equal("plugin.test.forms.nolabel", l.Get("plugin.test.forms.nolabel"), "a label key no table has shows the key itself");
            t.Equal("plugin.test.forms.nolabel@ja", missing, "and is reported once as missing (key, language)");
            t.Equal("既知", l.Get("plugin.test.forms.known"), "a known key shows the current language's text");
        }
    }
}

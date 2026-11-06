// The form probe (CC0-1.0): a test plugin of the declarative forms and the host widgets. A bottom panel form "panel" with one
// row of each of the 16 drawn row kinds (and a text row whose label key is missing from its strings), an item inspector
// section "section" with per-node values, a panel whose read command defers (refused inline), a HairBundles registration
// (refused), and through IPluginUi a panel "widgets" built from the host widget factories. The values live in the probe
// as JSON texts; formprobe.write applies every phase and logs it; formprobe.state reports counters, the refusals and the
// widget callbacks; formprobe.dialog / .menu / .toast open the host's dialogs, menus and toasts. Every reply is ids, numbers
// and short texts.
using System;
using System.Collections.Generic;
using System.Text;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using HS2Next.Api.Ui;
using HS2Next.Api.Unity;
using UnityEngine;
using UnityEngine.UIElements;

namespace Stp3.Test.FormProbe
{
    public sealed class FormProbePlugin : IPlugin
    {
        const string P = "plugin.stp3.test.formprobe.";

        IPluginContext ctx;
        /// <summary>"form|node" → key → value as JSON text ("" node = a panel).</summary>
        readonly Dictionary<string, Dictionary<string, string>> values = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        readonly List<string> writes = new List<string>();
        readonly List<string> widgetEvents = new List<string>();
        readonly List<string> refusals = new List<string>();
        string hidden = "[]", disabled = "[]", choices = "{}";
        string failRead, failWrite;
        int reads, buttons, choiceCalls, menuRuns, deferredReads;
        int dialogClosed = -2;
        string confirmed = "unset", prompted = "unset";
        bool formsAvailable, widgetsAvailable, widgetsBuilt;
        VisualElement widgetsRoot;
        IUiWidget numberWidget;

        public void Load(IPluginContext context)
        {
            ctx = context;
            var c = ctx.Commands;
            c.Register(Spec("formprobe.read", "Answers a form's values.", CommandFlags.ReadOnly), Read);
            c.Register(Spec("formprobe.write", "Takes a form's edit (every phase) and logs it.", CommandFlags.None), Write);
            c.Register(Spec("formprobe.choices", "Answers the reference row's choices.", CommandFlags.ReadOnly), Choices);
            c.Register(Spec("formprobe.button", "Counts the button row's presses.", CommandFlags.None), Button);
            c.Register(Spec("formprobe.readdefer", "A read command that may answer later (refused inline).", CommandFlags.ReadOnly | CommandFlags.Defers), DeferRead);
            c.Register(Spec("formprobe.state", "The probe's counters, refusals and widget events.", CommandFlags.ReadOnly), State);
            c.Register(Spec("formprobe.log", "The writes received (form|node|key|phase|value), optionally cleared.", CommandFlags.None), Log);
            c.Register(Spec("formprobe.set", "Sets a value (JSON) of a form and node; refresh: ctx.Forms.Refresh.", CommandFlags.None), Set);
            c.Register(Spec("formprobe.flags", "Sets the hidden, disabled and choices members of every read.", CommandFlags.None), Flags);
            c.Register(Spec("formprobe.fail", "Makes the read or the write of a form fail (null: none).", CommandFlags.None), Fail);
            c.Register(Spec("formprobe.remove", "Removes a form (ctx.Forms.Remove).", CommandFlags.None), Remove);
            c.Register(Spec("formprobe.dialog", "Opens a host dialog, confirm or prompt.", CommandFlags.None), Dialog);
            c.Register(Spec("formprobe.menu", "Opens a host popup menu of two entries and a separator.", CommandFlags.None), Menu);
            c.Register(Spec("formprobe.toast", "Shows a host toast.", CommandFlags.None), Toast);
            c.Register(Spec("formprobe.widget", "Shows a value in the number widget without its callback.", CommandFlags.None), SetWidget);

            Panel().Add("f", "2.5").Add("i", "4").Add("b", "true").Add("c", "[1,0.5,0.25,1]").Add("h", "{\"color\":[1,1,1,1],\"intensity\":1.5}")
                .Add("v2", "[1,2]").Add("v3", "[1,2,3]").Add("e", "\"hard\"").Add("r", "\"r2\"").Add("file", "null").Add("t", "\"hello\"")
                .Add("n", "null").Add("pl", "null").Add("ap", "\"N_Wrist_L\"").Add("nolabel", "\"x\"");
            Values("deferred", "")["t"] = "\"d\"";

            var forms = ctx.Forms;
            formsAvailable = forms.Available;
            forms.Panel("panel", P + "panel", "bottom", PanelForm());
            forms.Section("item", "section", P + "section", SectionForm());
            forms.Panel("deferred", P + "deferred", "bottom", new UiForm("formprobe.readdefer", "formprobe.write") { Rows = { UiRow.Text("t", P + "t") } });
            Try("hair", () => forms.Panel("hair", P + "hair", "bottom", new UiForm("formprobe.read", "formprobe.write") { Rows = { new UiRow { Key = "hb", Kind = RowKind.HairBundles, LabelKey = P + "hb" } } }));

            var ui = ctx.Unity()?.Ui;
            if (ui != null)
            {
                widgetsAvailable = ui.Widgets != null;
                ui.RegisterPanel("widgets", P + "widgets", "bottom", BuildWidgets);
            }
        }

        public void Unload() { widgetsRoot = null; numberWidget = null; }

        static CommandSpec Spec(string name, string doc, CommandFlags flags)
        {
            var s = new CommandSpec(name, doc) { AllowExtraArgs = true };
            return flags != CommandFlags.None ? s.WithFlags(flags) : s;
        }

        void Try(string what, Action a)
        {
            try { a(); refusals.Add(what + ": accepted"); }
            catch (ArgumentException e) { refusals.Add(what + ": " + e.Message); }
        }

        sealed class Adder
        {
            readonly Dictionary<string, string> d;
            public Adder(Dictionary<string, string> d) { this.d = d; }
            public Adder Add(string k, string json) { d[k] = json; return this; }
        }

        Adder Panel() => new Adder(Values("panel", ""));

        Dictionary<string, string> Values(string form, string node)
        {
            string k = form + "|" + (node ?? "");
            if (!values.TryGetValue(k, out var d))
            {
                values[k] = d = new Dictionary<string, string>(StringComparer.Ordinal);
                if (form == "section") { d["w"] = "1"; d["flag"] = "false"; d["note"] = "\"" + (node ?? "") + "\""; }
            }
            return d;
        }

        static UiForm PanelForm()
        {
            var f = new UiForm("formprobe.read", "formprobe.write");
            f.Rows.Add(UiRow.Float("f", P + "f", 0, 10, 5, "0.00"));
            f.Rows.Add(UiRow.Int("i", P + "i", 0, 20, 3));
            f.Rows.Add(UiRow.Bool("b", P + "b"));
            f.Rows.Add(UiRow.Color("c", P + "c"));
            f.Rows.Add(new UiRow { Key = "h", Kind = RowKind.ColorHdr, LabelKey = P + "h" });
            f.Rows.Add(UiRow.Vec2("v2", P + "v2"));
            f.Rows.Add(UiRow.Vec3("v3", P + "v3"));
            f.Rows.Add(UiRow.Enum("e", P + "e", new UiChoice("soft", P + "soft"), new UiChoice("hard", P + "hard")));
            f.Rows.Add(UiRow.Ref("r", P + "r", "formprobe.choices"));
            f.Rows.Add(UiRow.File("file", P + "file"));
            f.Rows.Add(UiRow.Text("t", P + "t"));
            f.Rows.Add(UiRow.Node("n", P + "n"));
            var b = UiRow.Button("btn", P + "btn", "formprobe.button");
            b.CommandArgs = new Dictionary<string, object> { ["times"] = 1 };
            f.Rows.Add(b);
            f.Rows.Add(UiRow.PartList("pl", P + "pl", 314, 314));
            f.Rows.Add(UiRow.Push("push", P + "push", "colour:336"));
            f.Rows.Add(UiRow.AttachPoint("ap", P + "ap"));
            f.Rows.Add(UiRow.Text("nolabel", P + "nolabel"));
            return f;
        }

        static UiForm SectionForm()
        {
            var f = new UiForm("formprobe.read", "formprobe.write");
            f.Rows.Add(UiRow.Float("w", P + "w", 0, 10));
            f.Rows.Add(UiRow.Bool("flag", P + "flag"));
            f.Rows.Add(UiRow.Text("note", P + "note"));
            return f;
        }

        static string Q(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (char ch in s)
            {
                if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
                else if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4"));
                else sb.Append(ch);
            }
            return sb.Append('"').ToString();
        }

        static string Obj(Dictionary<string, string> d)
        {
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var kv in d)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(Q(kv.Key)).Append(':').Append(kv.Value);
            }
            return sb.Append('}').ToString();
        }

        // ------------------------------------------------------------ the form commands

        object Read(ICommandContext c)
        {
            reads++;
            string form = c.Args.Get<string>("form"), node = c.Args.Get<string>("node");
            if (failRead != null && failRead == form) throw new BridgeException(BridgeError.NotFound("form " + form + " fails on purpose"));
            return new BridgeReply().With("values", new RawJson(Obj(Values(form, node)))).With("hidden", new RawJson(hidden)).With("disabled", new RawJson(disabled))
                .With("choices", new RawJson(choices));
        }

        object Write(ICommandContext c)
        {
            string form = c.Args.Get<string>("form"), node = c.Args.Get<string>("node"), key = c.Args.Get<string>("key"), phase = c.Args.Get<string>("phase");
            string value = c.Args.RawJson("value") ?? "null";
            bool undoFlag = c.Args.Get("undo", true);
            writes.Add(form + "|" + (node ?? "") + "|" + key + "|" + phase + "|" + value + (undoFlag ? "|undo?" : ""));
            if (failWrite != null && failWrite == form) throw new BridgeException(BridgeError.Unsupported("writes of " + form + " fail on purpose"));
            Values(form, node)[key] = value;
            return new BridgeReply().With("key", key);
        }

        object Choices(ICommandContext c)
        {
            choiceCalls++;
            return new BridgeReply().With("choices", new RawJson("[{\"value\":\"r1\",\"label\":\"" + P + "r1\"},{\"value\":\"r2\",\"label\":\"Second\"}]"));
        }

        object Button(ICommandContext c)
        {
            buttons += c.Args.Get("times", 1);
            return new BridgeReply().With("buttons", buttons);
        }

        object DeferRead(ICommandContext c)
        {
            deferredReads++;
            return new BridgeReply().With("values", new RawJson("{}"));
        }

        object State(ICommandContext c)
        {
            var r = new BridgeReply()
                .With("formsAvailable", formsAvailable).With("formsAvailableNow", ctx.Forms.Available).With("widgetsAvailable", widgetsAvailable)
                .With("widgetsBuilt", widgetsBuilt).With("reads", reads).With("writes", writes.Count).With("buttons", buttons).With("choiceCalls", choiceCalls)
                .With("deferredReads", deferredReads).With("refusals", refusals).With("widgetEvents", widgetEvents).With("menuRuns", menuRuns)
                .With("dialogClosed", dialogClosed).With("confirmed", confirmed).With("prompted", prompted).With("history", ctx.History.Count);
            string form = c.Args.Get<string>("form");
            if (form != null) r.With("values", new RawJson(Obj(Values(form, c.Args.Get<string>("node")))));
            return r;
        }

        object Log(ICommandContext c)
        {
            var l = new List<string>(writes);
            if (c.Args.Get("clear", false)) writes.Clear();
            return new BridgeReply().With("writes", l);
        }

        object Set(ICommandContext c)
        {
            string form = c.Args.Require<string>("form"), node = c.Args.Get<string>("node"), key = c.Args.Require<string>("key");
            Values(form, node)[key] = c.Args.RawJson("value") ?? "null";
            if (c.Args.Get("refresh", false)) ctx.Forms.Refresh(form, node);
            return new BridgeReply().With("key", key);
        }

        object Flags(ICommandContext c)
        {
            hidden = c.Args.RawJson("hidden") ?? "[]";
            disabled = c.Args.RawJson("disabled") ?? "[]";
            choices = c.Args.RawJson("choices") ?? "{}";
            return new BridgeReply().With("set", true);
        }

        object Fail(ICommandContext c)
        {
            failRead = c.Args.Get<string>("read");
            failWrite = c.Args.Get<string>("write");
            return new BridgeReply().With("read", failRead).With("write", failWrite);
        }

        object Remove(ICommandContext c) => new BridgeReply().With("removed", ctx.Forms.Remove(c.Args.Require<string>("id")));

        // ------------------------------------------------------------ the host widgets

        void Ev(string what) => widgetEvents.Add(what);

        static string F(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        VisualElement BuildWidgets()
        {
            widgetsBuilt = true;
            var w = ctx.Unity().Ui.Widgets;
            var root = new VisualElement { name = "formprobe-widgets" };
            root.Add(w.Button(P + "w.button", () => Ev("button")).Element);
            root.Add(w.Toggle(P + "w.toggle", true, b => Ev("toggle " + b)).Element);
            numberWidget = w.Number(P + "w.number", 0, 10, 3, (v, p) => Ev("number " + F(v) + " " + p));
            root.Add(numberWidget.Element);
            root.Add(w.Vector(P + "w.vector", 3, new Vector4(1, 2, 3, 0), (v, p) => Ev("vector " + F(v.x) + "," + F(v.y) + "," + F(v.z) + " " + p)).Element);
            root.Add(w.Choice(P + "w.choice", new[] { P + "soft", P + "hard" }, 1, i => Ev("choice " + i)).Element);
            root.Add(w.Color(P + "w.color", new Color(1, 0, 0, 1), true, (col, p) => Ev("color " + ColorUtility.ToHtmlStringRGBA(col) + " " + p)).Element);
            root.Add(w.Ref(P + "w.ref", () => new[] { new KeyValuePair<string, string>("a", "Alpha"), new KeyValuePair<string, string>("b", "Beta") }, "b", v => Ev("ref " + v)).Element);
            root.Add(w.File(P + "w.file", null, p => Ev("file " + p)).Element);
            root.Add(w.Text(P + "w.text", "abc", t => Ev("text " + t)).Element);
            root.Add(w.AttachPoint(P + "w.attach", "N_Wrist_L", k => Ev("attach " + k)).Element);
            widgetsRoot = root;
            return root;
        }

        object SetWidget(ICommandContext c)
        {
            if (numberWidget == null) return new BridgeReply().With("set", false);
            numberWidget.SetValue(c.Args.Get("value", 0.0));
            return new BridgeReply().With("set", true);
        }

        object Dialog(ICommandContext c)
        {
            var w = ctx.Unity()?.Ui?.Widgets;
            if (w == null) return new BridgeReply().With("shown", false);
            string kind = c.Args.Get("kind", "dialog");
            if (kind == "confirm") w.Confirm(P + "d.title", P + "d.message", ok => confirmed = ok ? "ok" : "cancel");
            else if (kind == "prompt") w.Prompt(P + "d.title", "typed", s => prompted = s ?? "(cancelled)");
            else w.Dialog(P + "d.title", P + "d.message", new[] { P + "d.first", P + "d.second" }, i => dialogClosed = i,
                new Label("probe dialog content") { name = "formprobe-dialog-content" });
            return new BridgeReply().With("shown", true);
        }

        object Menu(ICommandContext c)
        {
            var w = ctx.Unity()?.Ui?.Widgets;
            if (w == null) return new BridgeReply().With("shown", false);
            w.Menu(new[]
            {
                new UiMenuItem(P + "m.first", () => menuRuns++) { Checked = true },
                new UiMenuItem { Separator = true },
                new UiMenuItem(P + "m.second", () => menuRuns += 10) { Enabled = false },
            }, widgetsRoot);
            return new BridgeReply().With("shown", true);
        }

        object Toast(ICommandContext c)
        {
            var w = ctx.Unity()?.Ui?.Widgets;
            if (w == null) return new BridgeReply().With("shown", false);
            w.Toast(c.Args.Get("text", "probe toast"), c.Args.Get("level", "info") == "warning" ? ToastLevel.Warning : ToastLevel.Info);
            return new BridgeReply().With("shown", true);
        }
    }
}

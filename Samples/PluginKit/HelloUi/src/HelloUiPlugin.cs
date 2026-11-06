// Sample "hello UI" of the HS2Next plugin kit (CC0-1.0: dedicated to the public domain).
// An engine-free plugin with two forms that the app draws with its own widgets: the bottom panel "settings" (a slider, a
// toggle, a colour, a choice, a text and a button) and the inspector section "tag" on Studio items (one slider per item).
// The app reads the values through helloui.read and writes them through helloui.write; it records one undo entry per
// committed edit itself (UiForm.Undo is true), so the write command records none. The values live in memory only: a plugin
// that keeps them with the scene writes an ext block (see the guide).
using System;
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using HS2Next.Api.Ui;

namespace Stp3.Samples.HelloUi
{
    public sealed class HelloUiPlugin : IPlugin
    {
        const string L = "plugin.stp3.sample.helloui.";   // the plugin's string keys (strings.tsv)
        IPluginContext ctx;
        double size = 1;
        bool enabled = true;
        double[] tint = { 1, 1, 1, 1 };
        string mode = "soft";
        string note = "";
        readonly Dictionary<string, double> weights = new Dictionary<string, double>();   // the tag section: node id -> weight

        public void Load(IPluginContext context)
        {
            ctx = context;
            ctx.Commands.Register(new CommandSpec("helloui.read", "The values of one of the sample's forms.")
                .Arg("form", ArgTypes.String, "settings or tag", required: true)
                .Arg("node", ArgTypes.String, "the inspected item (the tag section)")
                .Return("values", ArgTypes.Object, "row key -> value")
                .WithFlags(CommandFlags.ReadOnly), Read);
            ctx.Commands.Register(new CommandSpec("helloui.write", "Writes one value of one of the sample's forms.")
                .Arg("form", ArgTypes.String, "settings or tag", required: true)
                .Arg("node", ArgTypes.String, "the inspected item (the tag section)")
                .Arg("key", ArgTypes.String, "the row", required: true)
                .Arg("value", ArgTypes.Any, "the value")
                .Arg("phase", ArgTypes.String, "preview, commit, cancel, undo or redo", defaultValue: "commit"), Write);
            ctx.Commands.Register(new CommandSpec("helloui.reset", "Puts the panel's values back to their defaults.")
                .Arg("form", ArgTypes.String, "the form of the button")
                .Arg("node", ArgTypes.String, "the inspected item")
                .Arg("key", ArgTypes.String, "the button's row"), Reset);

            var settings = new UiForm("helloui.read", "helloui.write");
            settings.Rows.Add(UiRow.Float("size", L + "size", 0, 10, defaultValue: 1));
            settings.Rows.Add(UiRow.Bool("enabled", L + "enabled"));
            settings.Rows.Add(UiRow.Color("tint", L + "tint", alpha: true));
            settings.Rows.Add(UiRow.Enum("mode", L + "mode", new UiChoice("soft", L + "mode.soft"), new UiChoice("hard", L + "mode.hard")));
            settings.Rows.Add(UiRow.Text("note", L + "note"));
            settings.Rows.Add(UiRow.Button("reset", L + "reset", "helloui.reset"));
            ctx.Forms.Panel("settings", L + "panel", "bottom", settings);

            var tag = new UiForm("helloui.read", "helloui.write");
            tag.Rows.Add(UiRow.Float("weight", L + "weight", 0, 1, defaultValue: 0));
            ctx.Forms.Section("item", "tag", L + "section", tag);
        }

        object Read(ICommandContext c)
        {
            var values = new Dictionary<string, object>();
            if (c.Args.Get("form", "") == "tag")
                values["weight"] = weights.TryGetValue(c.Args.Get("node", ""), out var w) ? w : 0.0;
            else
            {
                values["size"] = size;
                values["enabled"] = enabled;
                values["tint"] = tint;
                values["mode"] = mode;
                values["note"] = note;
            }
            return new BridgeReply().With("values", values);
        }

        // Every phase carries the value to show now: preview while a slider moves, commit at the end, cancel the value before
        // the drag, undo / redo the values of the app's undo entry. Not flagged Mutates: it changes the plugin's state, not
        // the scene (the undo / redo writes run while the scene applies the step).
        object Write(ICommandContext c)
        {
            string key = c.Args.Get("key", "");
            switch (c.Args.Get("form", "") + "/" + key)
            {
                case "settings/size": size = Math.Max(0, Math.Min(10, c.Args.Get("value", size))); break;
                case "settings/enabled": enabled = c.Args.Get("value", enabled); break;
                case "settings/tint":
                    var t = c.Args.Get<double[]>("value");
                    if (t == null || t.Length != 4) throw new BridgeException(BridgeError.BadArgs("value", "[r, g, b, a]"));
                    tint = t;
                    break;
                case "settings/mode":
                    string m = c.Args.Get("value", "");
                    if (m != "soft" && m != "hard") throw new BridgeException(BridgeError.BadArgs("value", "soft or hard"));
                    mode = m;
                    break;
                case "settings/note": note = c.Args.Get("value", "") ?? ""; break;
                case "tag/weight":
                    string node = c.Args.Get("node", "");
                    if (node.Length == 0) throw new BridgeException(BridgeError.Missing("node", ArgTypes.String));
                    weights[node] = Math.Max(0, Math.Min(1, c.Args.Get("value", 0.0)));
                    break;
                default: throw new BridgeException(BridgeError.BadArgs("key", "a row of the form", "no row " + key));
            }
            return new BridgeReply();
        }

        // The button of the panel calls this; any client may call it too. After a button the app reads the form again by
        // itself; Refresh makes it read again also when the command came from elsewhere.
        object Reset(ICommandContext c)
        {
            size = 1;
            enabled = true;
            tint = new double[] { 1, 1, 1, 1 };
            mode = "soft";
            note = "";
            ctx.Forms.Refresh("settings");
            return new BridgeReply();
        }

        // The host removes the commands and the forms; the values are this instance's fields and go with it.
        public void Unload() { }
    }
}

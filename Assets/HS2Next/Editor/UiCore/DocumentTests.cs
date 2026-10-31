// LayoutTests and OptionsTests: layout.json round trip, unknown panel ids kept, defaults
// for missing panels, version migration, invalid files; options.json round trip, precedence (command line > options.json
// > settings ui block > defaults), unknown members kept, coercion and clamping.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace HS2Next.UI.Core.Tests
{
    using HS2Next.Save.Tests;
    using HS2Next.UI.Core;

    public static class LayoutTests
    {
        public static void Run(TestRunner t)
        {
            var d = LayoutDocument.Default();
            t.Check(d.Region("left").Panels.SequenceEqual(new[] { "workspace", "inspector" }), "layout: default left = workspace and inspector as tabs (version 2)");
            t.Check(d.Region("right").Panels.SequenceEqual(new[] { "add.chara", "add.item", "add.anime", "add.map", "add.light", "add.pose", "scene", "history" }), "layout: default right = the add browsers, scene and history (version 2)");
            t.Check(d.Region("bottom").Panels.Count == 0 && d.Region("bottom").Active == null, "layout: default bottom empty");
            t.Check(d.Region("left").Size == 360 && d.Region("right").Size == 380 && d.Region("bottom").Size == 300, "layout: default sizes 360 / 380 / 300");
            t.Check(LayoutRegions.MinSize("left") == 300 && LayoutRegions.MinSize("right") == 300 && LayoutRegions.MinSize("bottom") == 160, "layout: minimum sizes 300 / 300 / 160");
            t.Equal("workspace", d.Region("left").Active, "layout: workspace active on the left");
            t.Equal("add.chara", d.Region("right").Active, "layout: add.chara active on the right");
            t.Equal(0, d.Normalize().Count, "layout: the default needs no fixes");
            t.Check(d.ToJson().Contains("\"version\": 2") && !d.IsOutdated, "layout: written as version 2");

            // round trip with changes and unknown members
            d.Region("left").Size = 312.5;
            d.Region("bottom").Collapsed = true;
            d.Move("history", "bottom");
            d.Hidden.Add("add.pose");
            string json = d.ToJson();
            var back = LayoutDocument.Parse(json);
            t.Equal(json, back.ToJson(), "layout: round trip byte-identical");
            t.Check(back.Region("bottom").Panels.SequenceEqual(new[] { "history" }) && back.Region("bottom").Active == "history" && !back.Region("right").Panels.Contains("history"), "layout: move to region");
            t.Equal("bottom", back.RegionOf("history"), "layout: region of a panel");
            t.Check(!back.IsOutdated, "layout: a version 2 file is not migrated");

            var withUnknown = JObject.Parse(json);
            withUnknown["regions"]["left"]["panels"] = new JArray("workspace", "plugin.panel");
            withUnknown["regions"]["left"]["future"] = "x";
            withUnknown["zoom"] = 2;
            var u = LayoutDocument.Parse(withUnknown.ToString());
            u.Normalize();
            string uj = u.ToJson();
            t.Check(u.Region("left").Panels.Contains("plugin.panel"), "layout: unknown panel ids kept");
            t.Check(uj.Contains("\"future\"") && uj.Contains("\"zoom\""), "layout: unknown members kept (region and top level)");

            // missing panels go to their default region; duplicates and bad sizes fixed
            var m = LayoutDocument.Parse("{\"version\":2,\"regions\":{\"right\":{\"size\":50,\"panels\":[\"inspector\",\"inspector\"],\"active\":\"gone\"}}}");
            var fixes = m.Normalize();
            t.Check(m.Region("right").Size == LayoutRegions.MinSize("right"), "layout: size raised to the minimum");
            t.Check(m.Region("right").Panels.SequenceEqual(new[] { "inspector", "add.chara", "add.item", "add.anime", "add.map", "add.light", "add.pose", "scene", "history" }),
                "layout: duplicate removed, a moved panel stays where the file put it, the missing panels appended to their default region (" + string.Join(",", m.Region("right").Panels) + ")");
            t.Check(m.RegionOf("inspector") == "right" && m.Region("left").Panels.SequenceEqual(new[] { "workspace" }), "layout: the inspector is not added twice");
            t.Check(m.Region("bottom").Panels.Count == 0 && m.Region("left") != null && m.Region("left").Size == 360, "layout: missing regions and panels restored");
            t.Equal("inspector", m.Region("right").Active, "layout: an unknown active tab falls back to the first");
            t.Check(fixes.Count >= 4, "layout: fixes reported (" + string.Join("; ", fixes) + ")");
            var reg = new[] { new PanelDefault("workspace", "left"), new PanelDefault("custom", "right") };
            var c = LayoutDocument.Parse("{\"version\":2,\"regions\":{}}");
            c.Normalize(reg);
            t.Check(c.RegionOf("custom") == "right" && c.RegionOf("inspector") == null, "layout: Normalize uses the registered panels");

            // version 0 (draft shape) and version 1 (the bottom-browser layout) are replaced once by the version 2 default,
            // keeping hidden panels and unknown top-level members; newer versions read; garbage gives null
            var problems = new List<string>();
            var v0 = LayoutDocument.Parse("{\"panels\":{\"workspace\":\"right\",\"inspector\":\"left\",\"scene\":\"nowhere\"},\"sizes\":{\"left\":322}}", problems);
            v0.Normalize();
            t.Check(v0.Version == 0 && v0.IsOutdated && v0.RegionOf("workspace") == "right" && v0.RegionOf("inspector") == "left" && v0.RegionOf("scene") == "bottom" && v0.Region("left").Size == 322,
                "layout: version 0 read (" + string.Join("; ", problems) + ")");
            var v0m = v0.MigrateToCurrent();
            t.Check(!v0m.IsOutdated && v0m.RegionOf("workspace") == "left" && v0m.RegionOf("inspector") == "left" && v0m.RegionOf("scene") == "right" && v0m.Region("left").Size == 360,
                "layout: version 0 replaced by the version 2 default");
            t.Check(v0m.ToJson().Contains("\"version\": 2") && !v0m.ToJson().Contains("\"sizes\""), "layout: written as version 2 without the draft members");
            var v1 = LayoutDocument.Parse("{\"version\":1,\"regions\":{\"left\":{\"size\":250,\"panels\":[\"workspace\"],\"active\":\"workspace\"},\"right\":{\"size\":300,\"panels\":[\"inspector\"],\"active\":\"inspector\"},"
                + "\"bottom\":{\"size\":280,\"collapsed\":true,\"panels\":[\"add.chara\",\"add.item\",\"add.anime\",\"add.map\",\"add.light\",\"add.pose\",\"scene\",\"history\",\"plugin.x\"],\"active\":\"add.item\"}},"
                + "\"hidden\":[\"history\",\"plugin.y\"],\"zoom\":3}");
            t.Check(v1 != null && v1.Version == 1 && v1.IsOutdated, "layout: a version 1 file is outdated");
            var v1m = v1.MigrateToCurrent();
            string v1j = v1m.ToJson();
            t.Check(v1m.Region("left").Panels.SequenceEqual(new[] { "workspace", "inspector" }) && v1m.Region("right").Panels.Count == 8 && v1m.Region("bottom").Panels.Count == 0
                    && !v1m.Region("bottom").Collapsed && v1m.Region("left").Size == 360 && v1m.Region("right").Size == 380 && v1m.Region("right").Active == "add.chara",
                "layout: version 1 replaced by the version 2 default (regions, sizes, active tabs)");
            t.Check(v1m.Hidden.SequenceEqual(new[] { "history", "plugin.y" }), "layout: version 1 migration keeps the hidden panels (" + string.Join(",", v1m.Hidden) + ")");
            t.Check(v1j.Contains("\"zoom\"") && v1j.Contains("\"version\": 2"), "layout: version 1 migration keeps unknown top-level members, writes version 2");
            var again = LayoutDocument.Parse(v1j);
            t.Check(again != null && !again.IsOutdated && again.ToJson() == v1j, "layout: the migrated file reads back as version 2 (migrated once)");
            problems.Clear();
            var v9 = LayoutDocument.Parse("{\"version\":9,\"regions\":{\"left\":{\"size\":300,\"panels\":[\"workspace\"]}}}", problems);
            t.Check(v9 != null && v9.Region("left").Size == 300 && problems.Any(p => p.Contains("newer")), "layout: a newer version is read with a note");
            problems.Clear();
            t.Check(LayoutDocument.Parse("{not json", problems) == null && problems.Count == 1, "layout: unreadable → null (ui.layoutInvalid, defaults)");
            var r = d.Clone();
            r.Region("left").Panels.Clear();
            t.Check(d.Region("left").Panels.Count == 2, "layout: Clone is deep");
        }
    }

    public static class OptionsTests
    {
        public static void Run(TestRunner t)
        {
            t.Check(OptionDefs.All.Count >= 35 && OptionDefs.All.Select(o => o.Key).Distinct().Count() == OptionDefs.All.Count, "options: " + OptionDefs.All.Count + " distinct keys");
            t.Check(OptionDefs.All.All(o => o.Key.IndexOf('.') > 0 && o.Section != null && o.Default != null), "options: keys are <section>.<name> with a section and a default");
            t.Check(OptionDefs.All.All(o => o.Coerce(o.Default) != null && Equals(o.Coerce(o.Default), o.Default)), "options: every default is valid for its kind");

            // defaults
            var L = new OptionLayers();
            t.Equal("en", L.GetString(OptionDefs.Language), "defaults: language en");
            t.Equal(100, L.GetInt(OptionDefs.UiScale), "defaults: UI scale 100");
            t.Equal("default", L.GetString(OptionDefs.KeyPreset), "defaults: key preset default (U2)");
            t.Check(!L.GetBool(OptionDefs.ConfirmDelete), "defaults: confirm delete off (U17)");
            t.Equal(96, L.GetInt(OptionDefs.GizmoSize), "defaults: gizmo size 96 px");
            t.Equal(15.0, L.GetFloat(OptionDefs.GizmoSnapRotate), "defaults: rotate snap 15");
            t.Equal("pivot", L.GetString(OptionDefs.AddPlacement), "defaults: placement at the pivot");
            t.Check(L.GetBool(OptionDefs.CameraWheelDolly) && !L.GetBool(OptionDefs.CameraRequireAlt), "defaults: wheel dolly on, require Alt off");
            t.Equal("selected", L.GetString(OptionDefs.ViewLightHelpers), "defaults: light helpers for the selected light");

            // precedence: command line > options.json > settings ui block > defaults
            L.Settings = JObject.Parse("{\"language\":\"ja\",\"camera\":{\"speed\":2,\"rotSpeedX\":3},\"gizmo\":{\"size\":120}}");
            t.Equal("ja", L.Get(OptionDefs.Language, out string src1), "precedence: settings alias language → ja");
            t.Equal("settings", src1, "precedence: source settings");
            t.Equal(2.0, L.GetFloat(OptionDefs.CameraSpeed), "precedence: settings nested shape");
            L.File = OptionsDocument.Parse("{\"version\":1,\"general\":{\"language\":\"zh-Hans\"},\"camera\":{\"speed\":4}}");
            t.Equal("zh-Hans", L.Get(OptionDefs.Language, out string src2), "precedence: options.json over settings");
            t.Equal("file", src2, "precedence: source file");
            t.Equal(3.0, L.GetFloat(OptionDefs.CameraRotSpeedX), "precedence: a member the file lacks comes from settings");
            L.CommandLine[OptionDefs.Language] = "en";
            L.CommandLine[OptionDefs.CameraSpeed] = "abc";
            t.Equal("en", L.Get(OptionDefs.Language, out string src3), "precedence: command line over everything");
            t.Equal("commandLine", src3, "precedence: source command line");
            t.Equal(4.0, L.GetFloat(OptionDefs.CameraSpeed), "precedence: an unreadable command-line value falls through");
            L.CommandLine.Clear();
            L.Settings = null;
            t.Equal(96, L.GetInt(OptionDefs.GizmoSize), "precedence: without settings the default returns");

            // coercion and clamping
            var o = new OptionsDocument();
            o.Set(OptionDefs.UiScale, 500);
            t.Equal(200, (int)o.Get(OptionDefs.UiScale), "coerce: UI scale clamped to 200");
            o.Set(OptionDefs.CameraSpeed, 0.0);
            t.Equal(0.1, (double)o.Get(OptionDefs.CameraSpeed), "coerce: camera speed clamped to 0.1");
            t.Throws<ArgumentException>(() => o.Set(OptionDefs.AddPlacement, "ceiling"), "coerce: an unknown choice is refused");
            t.Throws<ArgumentException>(() => o.Set("nosection", 1), "coerce: keys need a section");
            o.Set(OptionDefs.KeyPreset, "hs2studio");
            o.Set(OptionDefs.KeyPreset, null);
            t.Check(!o.Has(OptionDefs.KeyPreset), "set null removes");
            var probs = new List<string>();
            var bad = OptionsDocument.Parse("{\"version\":1,\"general\":{\"uiScale\":\"big\",\"language\":\"ja\"}}", probs);
            t.Check(bad.Get(OptionDefs.UiScale) == null && (string)bad.Get(OptionDefs.Language) == "ja" && probs.Count == 1, "coerce: unreadable values ignored and reported (" + string.Join("; ", probs) + ")");
            probs.Clear();
            OptionsDocument.Parse("{oops", probs);
            t.Equal(1, probs.Count, "parse: unreadable file reported (defaults used)");

            // round trip with unknown members and the verbatim rendering section
            string src = "{\"version\":1,\"general\":{\"language\":\"ja\",\"futureFlag\":true},\"rendering\":{\"aa\":\"taa\",\"grading\":{\"mode\":2}},\"plugin\":{\"x\":[1,2]}}";
            var doc = OptionsDocument.Parse(src);
            doc.Set(OptionDefs.GizmoSize, 128);
            var re = JObject.Parse(doc.ToJson());
            t.Check((bool)re["general"]["futureFlag"] && (string)re["rendering"]["aa"] == "taa" && (int)re["rendering"]["grading"]["mode"] == 2 && re["plugin"] != null,
                "round trip: unknown members and rendering kept");
            t.Equal(128, (int)re["gizmo"]["size"], "round trip: the set value written");
            t.Equal(doc.ToJson(), OptionsDocument.Parse(doc.ToJson()).ToJson(), "round trip: stable");
            t.Equal("taa", (string)doc.Section("rendering")["aa"], "rendering section readable");
            var eff = new OptionLayers { File = doc }.Effective();
            t.Equal(OptionDefs.All.Count, eff.Count, "effective: every option");
            t.Equal(128, (int)eff[OptionDefs.GizmoSize], "effective: file value");

            // label keys of choices
            var tb = UiKit.Strings(t);
            var missing = new List<string>();
            foreach (var def in OptionDefs.All.Where(x => x.Kind == OptionKind.Choice))
                foreach (var ch in def.Choices)
                {
                    string k = def.ChoiceLabelKey(ch);
                    if (k != null && !tb.Has(k)) missing.Add(k);
                }
            t.Check(missing.Count == 0, "labels: every option choice has its label (" + string.Join(", ", missing) + ")");

            // v0.3 trial 2 (package U2): the thumbnail size of each grid kind, a view-state option in px (the View section)
            var kinds = OptionDefs.MakerThumbKinds;
            t.Check(kinds.SequenceEqual(new[] { "face", "body", "hair", "clothes", "accessories" }), "thumb sizes: the Maker kinds face, body, hair, clothes, accessories");
            var tc = OptionDefs.Get(OptionDefs.ThumbSizeChara);
            t.Check(tc.Kind == OptionKind.Int && Equals(tc.Default, 126) && tc.Min == 63 && tc.Max == 189 && tc.Section == "view" && OptionDefs.ThumbSizeChara == "view.thumbSize.chara",
                "thumb sizes: view.thumbSize.chara 126 px (63–189), View section");
            t.Check(kinds.All(k => OptionDefs.TryGet(OptionDefs.ThumbSizeKey(k), out var d) && d.Kind == OptionKind.Int && Equals(d.Default, 72) && d.Min == 48 && d.Max == 128 && d.Section == "view"),
                "thumb sizes: view.thumbSize.<kind> 72 px (48–128) for every Maker kind, View section");
            var td = new OptionsDocument();
            td.Set(OptionDefs.ThumbSizeKey("clothes"), 500);
            td.Set(OptionDefs.ThumbSizeKey("face"), 100);
            var tj = JObject.Parse(td.ToJson());
            t.Check((int)td.Get(OptionDefs.ThumbSizeKey("clothes")) == 128 && (int)tj["view"]["thumbSize.face"] == 100 && (int)OptionsDocument.Parse(td.ToJson()).Get(OptionDefs.ThumbSizeKey("face")) == 100,
                "thumb sizes: clamped (500 → 128) and kept in options.json's view section (thumbSize.face 100)");
            t.Check(kinds.Concat(new[] { "chara" }).All(k => tb.Has("option." + OptionDefs.ThumbSizeKey(k))), "thumb sizes: every row has its label");
        }
    }
}

// UI1's v0.3 trial checks (runs first in UI1, Editor play and the app player with -ui): read through ui.state /
// ui.query (the containment count in-process, as UI1 reads geometry):
//   the layout file at the start (an older layout.json replaced once, with its toast); after ui.resetLayout the sidebar
//   layout (left workspace | inspector, workspace shown; right the add browsers, scene and history, add.chara shown;
//   bottom empty and collapsed; layout version 2; left 360 / right 380 when the window has room; viewport ≥ 320 × 240);
//   every tab inside its region's strip; the language button reads "Language" in en, ja and zh-Hans; File › Options: the
//   dimmed layer covers the whole root, the box lies inside the window and no visible part of the options lies outside
//   the box; in the player only, a window resize (Screen.SetResolution to 1280 × 800 windowed, then back): the regions
//   relaid out to the new size (left + viewport + right across the whole root, the status bar at its bottom, every tab
//   inside its strip). The end of UI1 checks that a viewport click on the character shows the inspector tab.
// v0.3 trial 2 (package U2): every row of File › Options keeps its label and control whole (each visible text against the room
// it has: a number field's text against its input, a label's or a choice's text against its width or, wrapped, its height),
// and the character's Transform fields in the inspector have room for -123.456 (read in-process, as the containment count).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HS2Next.UI;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.App
{
    public static partial class BridgeAcceptance
    {
        static Rect JRect(JToken t) => t is JArray a && a.Count == 4 ? new Rect((float)a[0], (float)a[1], (float)a[2], (float)a[3]) : Rect.zero;

        static bool RectInside(Rect inner, Rect outer, float tol = 0.5f) =>
            inner.xMin >= outer.xMin - tol && inner.yMin >= outer.yMin - tol && inner.xMax <= outer.xMax + tol && inner.yMax <= outer.yMax + tol;

        static string RectText(Rect r) => $"[{r.x:0.#}, {r.y:0.#}, {r.width:0.#} x {r.height:0.#}]";

        /// <summary>Visible descendants of a box that lie outside it (clipped by the scroll viewports below the box).</summary>
        static int OutsideBox(VisualElement box, List<string> examples)
        {
            var bb = box.worldBound;
            int n = 0;
            foreach (var e in box.Query<VisualElement>().ToList())
            {
                if (e == box) continue;
                bool shown = true;
                var r = e.worldBound;
                for (var p = e; p != null && p != box; p = p.hierarchy.parent)
                    if (p.resolvedStyle.display == DisplayStyle.None || p.resolvedStyle.visibility == Visibility.Hidden) { shown = false; break; }
                if (!shown) continue;
                for (var p = e.hierarchy.parent; p != null && p != box; p = p.hierarchy.parent)
                {
                    if (!p.ClassListContains(ScrollView.viewportUssClassName)) continue;
                    var b = p.worldBound;
                    float x0 = Mathf.Max(r.xMin, b.xMin), y0 = Mathf.Max(r.yMin, b.yMin), x1 = Mathf.Min(r.xMax, b.xMax), y1 = Mathf.Min(r.yMax, b.yMax);
                    r = x1 <= x0 || y1 <= y0 ? Rect.zero : Rect.MinMaxRect(x0, y0, x1, y1);
                    if (r == Rect.zero) break;
                }
                if (r.width <= 0.5f || r.height <= 0.5f || RectInside(r, bb)) continue;
                n++;
                if (examples.Count < 4) examples.Add((string.IsNullOrEmpty(e.name) ? e.GetType().Name : e.name) + " " + RectText(r));
            }
            return n;
        }

        /// <summary>The visible texts of <paramref name="row"/> that do not fit the room they have (v0.3 trial 2): a text inside a
        /// text input against the input's content box less the text's padding, another one-line text against its own content
        /// width, a wrapping text's height at its width.</summary>
        static List<string> ClippedTexts(VisualElement row)
        {
            var bad = new List<string>();
            foreach (var te in row.Query<TextElement>().ToList())
            {
                if (string.IsNullOrEmpty(te.text) || te.panel == null) continue;
                bool shown = true;
                for (var p = (VisualElement)te; p != null && p != row.hierarchy.parent; p = p.hierarchy.parent)
                    if (p.resolvedStyle.display == DisplayStyle.None || p.resolvedStyle.visibility == Visibility.Hidden) { shown = false; break; }
                if (!shown) continue;
                var rs = te.resolvedStyle;
                float need = te.MeasureTextSize(te.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
                VisualElement input = null;
                for (var p = te.hierarchy.parent; p != null && p != row; p = p.hierarchy.parent) if (p.ClassListContains("unity-base-text-field__input")) { input = p; break; }
                if (input != null)
                {
                    var ir = input.resolvedStyle;
                    float room = ir.width - ir.paddingLeft - ir.paddingRight - ir.borderLeftWidth - ir.borderRightWidth - rs.paddingLeft - rs.paddingRight;
                    if (need > room + 0.5f) bad.Add($"'{te.text}' {need:0.#} > input {room:0.#} px");
                    continue;
                }
                float cw = rs.width - rs.paddingLeft - rs.paddingRight - rs.borderLeftWidth - rs.borderRightWidth;
                if (rs.whiteSpace == WhiteSpace.Normal || rs.whiteSpace == WhiteSpace.PreWrap)
                {
                    float h = te.MeasureTextSize(te.text, Mathf.Max(1, cw), VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined).y;
                    float ch = rs.height - rs.paddingTop - rs.paddingBottom - rs.borderTopWidth - rs.borderBottomWidth;
                    if (h > ch + 1f) bad.Add($"'{te.text}' wrapped {h:0.#} > {ch:0.#} px tall");
                }
                else if (need > cw + 0.5f) bad.Add($"'{te.text}' {need:0.#} > {cw:0.#} px");
            }
            return bad;
        }

        /// <summary>The regions, viewport, toolbar / status bar and tab strips of ui.state against its root (a check line).</summary>
        static IEnumerator LayoutFits(AcceptanceCtx c, string what)
        {
            yield return c.Call(new { cmd = "ui.state" });
            var regs = c.J["layout"]?["regions"];
            var root = JRect(c.J["root"]);
            var vp = JRect(c.J["viewport"]?["worldBound"]);
            var left = JRect(regs?["left"]?["worldBound"]);
            var right = JRect(regs?["right"]?["worldBound"]);
            yield return c.Call(new { cmd = "ui.query", name = "#statusbar" });
            var status = JRect(c.J["elements"]?[0]?["worldBound"]);
            yield return c.Call(new { cmd = "ui.query", name = "#toolbar" });
            var toolbar = JRect(c.J["elements"]?[0]?["worldBound"]);
            yield return c.Call(new { cmd = "ui.query", name = ".hs-tab", all = true });
            int tabsOut = 0, tabs = 0;
            foreach (var e in (c.J["elements"] as JArray) ?? new JArray())
            {
                var tb = JRect(e["worldBound"]);
                if (tb.width <= 0) continue;
                tabs++;
                var strip = RectInside(tb, JRect(regs?["left"]?["strip"])) || RectInside(tb, JRect(regs?["right"]?["strip"])) || RectInside(tb, JRect(regs?["bottom"]?["strip"]));
                if (!strip) tabsOut++;
            }
            bool fits = Mathf.Abs(left.xMin - root.xMin) < 1 && Mathf.Abs(right.xMax - root.xMax) < 1 && left.xMax <= vp.xMin + 0.5f && vp.xMax <= right.xMin + 0.5f
                        && vp.width >= 319.5f && vp.height >= 239.5f && Mathf.Abs(status.yMax - root.yMax) < 1 && RectInside(toolbar, root) && RectInside(status, root);
            c.Check(fits && tabs > 0 && tabsOut == 0,
                $"{what}: root {RectText(root)} = left {RectText(left)} + viewport {RectText(vp)} + right {RectText(right)}, status bar {RectText(status)}; {tabs} tabs, {tabsOut} outside their strip");
        }

        public static IEnumerator UiV03(AcceptanceCtx c, UiHost host)
        {
            var ui = host.Context;
            // the layout file read at the start
            yield return c.Call(new { cmd = "ui.state" });
            int migrated = (int?)c.J["layout"]?["migratedFrom"] ?? -2;
            int version = (int?)c.J["layout"]?["version"] ?? -1;
            var toasts = (c.J["toasts"] as JArray)?.Select(t => (string)t).ToList() ?? new List<string>();
            c.Info($"UI1 v0.3: layout version {version}; an older layout.json replaced at the start: {(migrated >= 0 ? "version " + migrated : "no")}");
            if (migrated >= 0)
                c.Check(ui.Toasts.History.Count(t => t.Contains(L10n.Get("msg.layoutUpdated").Substring(0, 12))) == 1,
                    "UI1 v0.3 the older layout.json (version " + migrated + ") was replaced once, with one toast");
            c.Check(version == 2, "UI1 v0.3 layout version 2");

            // the default sidebar layout
            yield return c.Call(new { cmd = "ui.action", action = "ui.resetLayout" });
            yield return c.Frames(4);
            yield return c.Call(new { cmd = "ui.state" });
            var regs = c.J["layout"]?["regions"];
            List<string> Panels(string r) => (regs?[r]?["panels"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
            c.Check(Panels("left").SequenceEqual(new[] { "workspace", "inspector" }) && (string)regs?["left"]?["active"] == "workspace",
                "UI1 v0.3 left = workspace | inspector, workspace shown", AcceptanceCtx.Short(regs?["left"]?.ToString(Newtonsoft.Json.Formatting.None)));
            c.Check(Panels("right").SequenceEqual(new[] { "add.chara", "add.item", "add.anime", "add.map", "add.light", "add.pose", "scene", "history", "cameras" }) && (string)regs?["right"]?["active"] == "add.chara",
                "UI1 v0.3 right = the add browsers, scene, history and the cameras (v0.3 trial 2), add.chara shown", AcceptanceCtx.Short(string.Join(", ", Panels("right"))));
            c.Check(Panels("bottom").Count == 0 && (bool?)regs?["bottom"]?["collapsed"] == true, "UI1 v0.3 bottom empty and collapsed");
            var root = JRect(c.J["root"]);
            double ls = (double?)regs?["left"]?["shown"] ?? 0, rs = (double?)regs?["right"]?["shown"] ?? 0;
            if (root.width >= 360 + 380 + 320 + 4)
                c.Check(Math.Abs(ls - 360) < 1.5 && Math.Abs(rs - 380) < 1.5, $"UI1 v0.3 left {ls:0.#} px, right {rs:0.#} px (360 / 380; root {root.width:0} px wide)");
            else c.Info($"UI1 v0.3 left {ls:0.#} px, right {rs:0.#} px: the window ({root.width:0} px) has no room for 360 / 380 beside a 320 px viewport");
            yield return LayoutFits(c, "UI1 v0.3 the regions fill the window");

            // the language button
            var labels = new List<string>();
            foreach (var lang in new[] { "en", "ja", "zh-Hans" })
            {
                yield return c.Call(new { cmd = "ui.action", action = "ui.language", args = new { language = lang } });
                yield return c.Frames(2);
                yield return c.Call(new { cmd = "ui.query", name = "#tb-language" });
                labels.Add(lang + " '" + (string)c.J["elements"]?[0]?["text"] + "'");
                c.Check((string)c.J["elements"]?[0]?["text"] == "Language", "UI1 v0.3 the language button reads \"Language\" in " + lang);
            }
            yield return c.Call(new { cmd = "ui.action", action = "ui.language", args = new { language = "en" } });
            yield return c.Frames(2);

            // File › Options inside its box
            yield return c.Call(new { cmd = "ui.action", action = "file.options" });
            yield return c.Frames(4);
            yield return c.Call(new { cmd = "ui.state" });
            string modal = (string)c.J["modal"];
            var rootNow = JRect(c.J["root"]);
            yield return c.Call(new { cmd = "ui.query", name = ".hs-dialog-layer" });
            var layer = JRect(c.J["elements"]?[0]?["worldBound"]);
            yield return c.Call(new { cmd = "ui.query", name = ".hs-dialog" });
            var box = JRect(c.J["elements"]?[0]?["worldBound"]);
            var boxEl = ui.Shell.DialogLayer.Q(className: Dialogs.DialogClass);
            var ex = new List<string>();
            int outside = boxEl != null ? OutsideBox(boxEl, ex) : -1;
            c.Check(modal == "options" && Mathf.Abs(layer.x - rootNow.x) < 0.5f && Mathf.Abs(layer.y - rootNow.y) < 0.5f && Mathf.Abs(layer.width - rootNow.width) < 0.5f && Mathf.Abs(layer.height - rootNow.height) < 0.5f,
                $"UI1 v0.3 options: the dimmed layer {RectText(layer)} covers the root {RectText(rootNow)} (modal {modal})");
            c.Check(RectInside(box, rootNow) && box.width <= Mathf.Min(760, 0.92f * rootNow.width) + 0.5f && box.height <= 0.85f * rootNow.height + 0.5f && outside == 0,
                $"UI1 v0.3 options: box {RectText(box)} inside the window (≤ min(760, 92 %) x 85 %); {outside} visible parts outside the box{(ex.Count > 0 ? ": " + string.Join("; ", ex) : "")}");
            {
                // v0.3 trial 2: every option row keeps its label and control whole
                var op = HS2Next.UI.Panels.OptionsPanel.Open;
                int rows = 0;
                var clipped = new List<string>();
                foreach (var d in OptionDefs.All)
                {
                    var row = op?.Q("option-" + d.Key);
                    if (row == null) continue;
                    rows++;
                    var bad = ClippedTexts(row);
                    if (bad.Count > 0) clipped.Add(d.Key + ": " + string.Join("; ", bad));
                }
                c.Check(op != null && rows == OptionDefs.All.Count && clipped.Count == 0,
                    $"UI1 v0.3 trial 2 options: {rows} rows, none with a clipped label or control" + (clipped.Count > 0 ? ": " + AcceptanceCtx.Short(string.Join(" | ", clipped)) : ""));
            }
            yield return c.Call(new { cmd = "ui.input", frames = 2, events = new object[] { new { type = "key", key = "Escape" } } });
            yield return c.Call(new { cmd = "ui.state" });
            c.Check(c.J["modal"] == null || c.J["modal"].Type == JTokenType.Null, "UI1 v0.3 Esc closes the options");

            // a window resize (the player only: the Editor's game view does not take Screen.SetResolution)
            if (!Application.isEditor)
            {
                int w0 = Screen.width, h0 = Screen.height;
                var mode0 = Screen.fullScreenMode;
                int tw = w0 == 1280 ? 1440 : 1280, th = h0 == 800 ? 900 : 800;
                yield return c.Call(new { cmd = "ui.state" });
                int rebuilds0 = (int?)c.J["layout"]?["resizeRebuilds"] ?? 0;
                Screen.SetResolution(tw, th, FullScreenMode.Windowed);
                for (int i = 0; i < 120 && (Screen.width != tw || Screen.height != th); i++) yield return c.Frames(1);
                if (Screen.width != tw || Screen.height != th)
                    c.Warn($"UI1 v0.3 resize: Screen.SetResolution({tw}, {th}) was not applied in this player ({Screen.width}x{Screen.height}, batch mode {Application.isBatchMode}); the relayout after a size change is checked by UiShellTest T7 (panel scale)");
                else
                {
                    yield return c.Frames(6);
                    yield return c.Call(new { cmd = "ui.state" });
                    int rebuilds = (int?)c.J["layout"]?["resizeRebuilds"] ?? 0;
                    c.Check(rebuilds > rebuilds0, $"UI1 v0.3 resize {w0}x{h0} → {tw}x{th}: the shell relaid the regions out ({rebuilds - rebuilds0} rebuild(s); panel scale {(double?)c.J["panelScale"]:0.###})");
                    yield return LayoutFits(c, $"UI1 v0.3 resize {tw}x{th}: the regions fill the window");
                    Screen.SetResolution(w0, h0, mode0);
                    for (int i = 0; i < 120 && (Screen.width != w0 || Screen.height != h0); i++) yield return c.Frames(1);
                    yield return c.Frames(6);
                    yield return LayoutFits(c, $"UI1 v0.3 back to {w0}x{h0}: the regions fill the window");
                }
            }
        }

        /// <summary>The end of UI1: a viewport click on the character showed the inspector tab (workspace and inspector share
        /// the left region).</summary>
        static IEnumerator UiV03PickShowsInspector(AcceptanceCtx c)
        {
            yield return c.Call(new { cmd = "ui.state" });
            var left = c.J["layout"]?["regions"]?["left"];
            string pick = (string)c.J["viewport"]?["pick"];
            c.Check(pick != null && (string)left?["active"] == "inspector",
                $"UI1 v0.3 a viewport click on the character (pick {pick}) shows the inspector tab (left active {(string)left?["active"]})");
            // v0.3 trial 2: the Transform fields have room for an 8-character value (-123.456)
            yield return c.Frames(3);
            var insp = UiHost.Current?.Context?.Shell?.PanelRoot("inspector");
            var parts = new List<string>();
            int fit = 0, inputs = 0;
            foreach (var path in new[] { "transform.pos", "transform.rot", "transform.scale" })
                foreach (var input in insp?.Q("field-" + path)?.Query(className: "unity-base-text-field__input").ToList() ?? new List<VisualElement>())
                {
                    var te = input.Q<TextElement>();
                    if (te == null || input.resolvedStyle.width <= 0) continue;
                    inputs++;
                    var ir = input.resolvedStyle;
                    float room = ir.width - ir.paddingLeft - ir.paddingRight - ir.borderLeftWidth - ir.borderRightWidth - te.resolvedStyle.paddingLeft - te.resolvedStyle.paddingRight;
                    float need = te.MeasureTextSize("-123.456", 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
                    if (room + 0.5f >= need) fit++;
                    parts.Add($"{room:0}/{need:0}");
                }
            c.Check(inputs == 9 && fit == 9, $"UI1 v0.3 trial 2 the character's Transform fields have room for -123.456 ({fit} of {inputs}; room / text px {string.Join(", ", parts)})");
        }
    }
}

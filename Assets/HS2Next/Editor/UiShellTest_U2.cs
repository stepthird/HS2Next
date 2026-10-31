// UiShellTest.play groups for the second v0.3 trial's UI fixes (package U2; helpers of UiShellTest_V03.cs):
//   U2M measures (information lines only, the numbers before and after the fixes): every row of File › Options in en, ja
//     and zh-Hans — each visible text against the room it has (a label or a choice's text against its content width, a
//     number field's text against its input's content width, a wrapping label's height) and every part of the row inside
//     the options' scroll view horizontally — with the details of the render size row (capture.maxPixels); the inspector's
//     Transform rows of an item in the left sidebar: the number fields' input widths against the width of "-123.456".
//   U2 checks: (4) no option row with a clipped label or control in en, ja and zh-Hans, the render size cap a typed field
//     that clamps; (3) the inspector's Transform fields show -123.456 whole in the 360 px sidebar, at the region's minimum
//     (300 px) and in a 247 px column (the fields wrap onto more lines there) and stand beside the label in a wide row; (1)
//     the character grid's cell states through synthesised pointer and key events (hover, pressed, selected with the
//     keyboard-focus ring, the hint in the tooltips and the footer, a single click adds nothing, Enter adds), (2) its size
//     slider changes the cell width and the view-state option view.thumbSize.chara (written to options.json, followed when
//     set elsewhere).
// Events are synthesised through the panel (UiCommands.Send, as ui.input does).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HS2Next.Content.Studio;
using HS2Next.UI;
using HS2Next.UI.Core;
using HS2Next.UI.Binding;
using HS2Next.UI.Panels;
using HS2Next.UI.Widgets;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class UiShellTest
{
    [UnityEditor.InitializeOnLoadMethod]
    static void RegisterU2Groups()
    {
        if (!Groups.Any(g => g.id == "U2M")) Groups.Add(("U2M", U2Measure));
        if (!Groups.Any(g => g.id == "U2")) Groups.Add(("U2", U2Checks));
    }

    /// <summary>The 8-character sample a transform field must show whole.</summary>
    public const string U2Sample = "-123.456";

    static float ContentWidth(VisualElement e)
    {
        var rs = e.resolvedStyle;
        return rs.width - rs.paddingLeft - rs.paddingRight - rs.borderLeftWidth - rs.borderRightWidth;
    }

    static float ContentHeight(VisualElement e)
    {
        var rs = e.resolvedStyle;
        return rs.height - rs.paddingTop - rs.paddingBottom - rs.borderTopWidth - rs.borderBottomWidth;
    }

    /// <summary>The single-line width of <paramref name="text"/> in <paramref name="te"/>'s font (logical px).</summary>
    static float TextWidth(TextElement te, string text) =>
        te.MeasureTextSize(text ?? "", 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;

    /// <summary>The horizontal part of an element drawn inside every clipping ancestor up to <paramref name="stop"/> (the stop's
    /// own clipping counted when <paramref name="withStop"/>).</summary>
    static float DrawnWidth(VisualElement e, VisualElement stop, bool withStop)
    {
        var r = e.worldBound;
        float x0 = r.xMin, x1 = r.xMax;
        for (var p = e.hierarchy.parent; p != null; p = p.hierarchy.parent)
        {
            if (p == stop && !withStop) break;
            if (Clips(p)) { var b = p.worldBound; x0 = Mathf.Max(x0, b.xMin); x1 = Mathf.Min(x1, b.xMax); }
            if (p == stop) break;
        }
        return Mathf.Max(0, x1 - x0);
    }

    static VisualElement AncestorWithClass(VisualElement e, string cls, VisualElement stop)
    {
        for (var p = e.hierarchy.parent; p != null && p != stop; p = p.hierarchy.parent)
            if (p.ClassListContains(cls)) return p;
        return null;
    }

    const string TextInputClass = "unity-base-text-field__input";

    /// <summary>The width a number / text input has for its text: its content box less its text element's padding.</summary>
    static float InputRoom(VisualElement input, TextElement te = null)
    {
        te ??= input.Q<TextElement>();
        float room = ContentWidth(input);
        if (te != null) room -= te.resolvedStyle.paddingLeft + te.resolvedStyle.paddingRight;
        return room;
    }

    /// <summary>The visible texts of <paramref name="row"/> that do not fit the room they have, and the parts of the row cut
    /// horizontally by the clipping ancestors up to and including <paramref name="viewport"/>.</summary>
    static List<string> U2Clipped(VisualElement row, VisualElement viewport)
    {
        var bad = new List<string>();
        foreach (var te in row.Query<TextElement>().ToList())
        {
            if (!Shown(te, row) || string.IsNullOrEmpty(te.text)) continue;
            var rs = te.resolvedStyle;
            float need = TextWidth(te, te.text);
            var input = AncestorWithClass(te, TextInputClass, row);
            string what = (string.IsNullOrEmpty(te.name) ? te.GetType().Name : te.name) + " '" + (te.text.Length > 24 ? te.text.Substring(0, 24) + "…" : te.text) + "'";
            if (input != null)
            {
                float room = InputRoom(input, te);
                if (need > room + 0.5f) bad.Add($"{what} input {room:0.#} < text {need:0.#}");
                continue;
            }
            bool wraps = rs.whiteSpace == WhiteSpace.Normal || rs.whiteSpace == WhiteSpace.PreWrap;
            float cw = ContentWidth(te);
            if (wraps)
            {
                float h = te.MeasureTextSize(te.text, Mathf.Max(1, cw), VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined).y;
                if (h > ContentHeight(te) + 1f) bad.Add($"{what} wrapped height {ContentHeight(te):0.#} < {h:0.#}");
            }
            else if (need > cw + 0.5f) bad.Add($"{what} width {cw:0.#} < text {need:0.#}");
        }
        foreach (var e in row.Query<VisualElement>().ToList())
        {
            if (e == row || !Shown(e, row)) continue;
            var wb = e.worldBound;
            if (wb.width <= 0.5f || wb.height <= 0.5f) continue;
            if (e is TextElement && AncestorWithClass(e, TextInputClass, row) != null) continue;   // input texts: measured above
            float d = DrawnWidth(e, viewport, true);
            if (d + 0.5f < wb.width) bad.Add($"{(string.IsNullOrEmpty(e.name) ? e.GetType().Name : e.name)} cut horizontally ({d:0.#} of {wb.width:0.#} px drawn)");
        }
        return bad;
    }

    /// <summary>One option row's parts: label text / label width, the control, its input, the reset button.</summary>
    static string U2RowDetails(VisualElement row)
    {
        var parts = new List<string> { "row " + R(row.worldBound) };
        var label = row.Q<Label>(className: "unity-base-field__label") ?? row.Q<Label>();
        if (label != null) parts.Add($"label '{label.text}' {R(label.worldBound)} text {TextWidth(label, label.text):0.#} px, white-space {label.resolvedStyle.whiteSpace}");
        foreach (var input in row.Query(className: TextInputClass).ToList())
        {
            var te = input.Q<TextElement>();
            parts.Add($"input {R(input.worldBound)} content {ContentWidth(input):0.#} px, text '{te?.text}' {(te != null ? TextWidth(te, te.text) : 0):0.#} px");
        }
        var drag = row.Q(className: "unity-base-slider__drag-container");
        if (drag != null) parts.Add("slider track " + R(drag.worldBound));
        var popup = row.Q(className: "unity-base-popup-field__input");
        if (popup != null) parts.Add("choice " + R(popup.worldBound));
        var reset = row.Q<Button>(className: "hs-icon-button");
        if (reset != null) parts.Add("reset " + R(reset.worldBound));
        return string.Join("; ", parts);
    }

    /// <summary>Information lines: every part of <paramref name="root"/> with its classes, bound and box values.</summary>
    static void U2Dump(Ctx c, string what, VisualElement root, int max = 24)
    {
        int n = 0;
        foreach (var e in root.Query<VisualElement>().ToList())
        {
            if (n++ >= max) break;
            var rs = e.resolvedStyle;
            c.Info($"U2M {what} part {e.GetType().Name} '{e.name}' [{string.Join(" ", e.GetClasses())}] {R(e.worldBound)} dir {rs.flexDirection} wrap {rs.flexWrap} basis {rs.flexBasis} grow {rs.flexGrow:0.#} shrink {rs.flexShrink:0.#} min-w {rs.minWidth} width {rs.width:0.#} padding {rs.paddingLeft:0.#}/{rs.paddingRight:0.#} border {rs.borderLeftWidth:0.#}/{rs.borderRightWidth:0.#} margin {rs.marginLeft:0.#}/{rs.marginRight:0.#} ws {rs.whiteSpace}{(e is TextElement t ? " text '" + t.text + "'" : "")}");
        }
    }

    /// <summary>Opens File › Options in <paramref name="lang"/> and returns (rows checked, rows with problems by key).</summary>
    static IEnumerator U2OptionsRows(Ctx c, string lang, List<(string key, List<string> bad, string details)> into)
    {
        var ui = c.Ui;
        c.Host.SetLanguage(lang, save: false);
        yield return Frames(2);
        OptionsPanel.Show(ui);
        yield return Frames(6);
        var op = OptionsPanel.Open;
        var scroll = op?.Q<ScrollView>("options-scroll");
        if (op == null || scroll == null) { c.Info("U2M options " + lang + ": not open"); yield break; }
        var viewport = scroll.contentViewport;
        foreach (var d in OptionDefs.All)
        {
            var row = op.Q("option-" + d.Key);
            if (row == null) { into.Add((d.Key, new List<string> { "no row" }, "")); continue; }
            into.Add((d.Key, U2Clipped(row, viewport), U2RowDetails(row)));
            if (lang == "en" && c.Group == "U2M" && (d.Key == OptionDefs.RenderSceneShaderTime || d.Key == OptionDefs.RenderBackgroundColor)) U2Dump(c, "options " + d.Key, row);
        }
        ui.Dialogs.CloseCurrent();
        yield return Frames(2);
    }

    // ---------------- U2M: measurements (information)

    static IEnumerator U2Measure(Ctx c)
    {
        var ui = c.Ui;
        var root = c.Host.Root;
        c.Info($"U2M window {Screen.width}x{Screen.height}, panel scale {root.panel.scaledPixelsPerPoint:0.###}, root {R(root.worldBound)}");
        // File › Options: every row in three languages
        foreach (var lang in new[] { "en", "ja", "zh-Hans" })
        {
            var rows = new List<(string key, List<string> bad, string details)>();
            yield return U2OptionsRows(c, lang, rows);
            var badRows = rows.Where(r => r.bad.Count > 0).ToList();
            c.Info($"U2M options {lang}: {rows.Count} rows, {badRows.Count} with a clipped label or control");
            foreach (var r in badRows) c.Info($"U2M options {lang} {r.key}: {string.Join("; ", r.bad)}");
            foreach (var key in new[] { OptionDefs.CaptureMaxPixels, OptionDefs.TimeStepDt, OptionDefs.RenderSceneShaderTime })
            {
                var mp = rows.FirstOrDefault(r => r.key == key);
                c.Info($"U2M options {lang} {key}: {mp.details}");
            }
        }
        c.Host.SetLanguage("en", save: false);
        yield return Frames(2);

        // the inspector's Transform rows of an item in the left sidebar
        ui.Ops.NewScene();
        yield return Frames(2);
        var add = ui.Ops.AddItem(StudioDb.FallbackItem, null, null, -1, UiContext.User());
        string id = add.NewIds.FirstOrDefault();
        if (id == null) { c.Info("U2M transform: no item added (" + add + ")"); yield break; }
        ui.Ops.Select(new[] { id });
        ui.Shell.FocusPanel(UiShell.InspectorId);
        yield return Frames(6);
        var insp = ui.Shell.PanelRoot(UiShell.InspectorId);
        c.Info($"U2M inspector: region {ui.Shell.RegionOf(UiShell.InspectorId)}, panel {R(insp?.worldBound ?? Rect.zero)}");
        foreach (var path in new[] { "transform.pos", "transform.rot", "transform.scale" })
        {
            var row = insp?.Q("field-" + path);
            if (row == null) { c.Info("U2M transform " + path + ": no row"); continue; }
            var inputs = row.Query(className: TextInputClass).ToList();
            var parts = new List<string>();
            var ys = new HashSet<int>();
            foreach (var input in inputs)
            {
                var te = input.Q<TextElement>();
                float need = te != null ? TextWidth(te, U2Sample) : 0;
                parts.Add($"{R(input.worldBound)} room {InputRoom(input):0.#} / '{U2Sample}' {need:0.#} px ({(need > 0 ? InputRoom(input) / need * U2Sample.Length : 0):0.#} characters)");
                ys.Add(Mathf.RoundToInt(input.worldBound.y));
            }
            c.Info($"U2M transform {path}: row {R(row.worldBound)}, {inputs.Count} inputs on {ys.Count} line(s): {string.Join("; ", parts)}");
            if (path == "transform.pos")
            {
                // the parts of the row (where its width goes)
                U2Dump(c, "transform", row);
            }
        }
        ui.Ops.NewScene();
        yield return Frames(2);
    }

    // ---------------- U2: checks

    /// <summary>The number inputs of a transform row: (room for text, the width of <see cref="U2Sample"/>, the input's top).</summary>
    static List<(float room, float need, float y, string text, float textW)> U2Inputs(VisualElement row)
    {
        var l = new List<(float, float, float, string, float)>();
        if (row == null) return l;
        foreach (var input in row.Query(className: TextInputClass).ToList())
        {
            var te = input.Q<TextElement>();
            if (te == null) continue;
            l.Add((InputRoom(input, te), TextWidth(te, U2Sample), input.worldBound.y, te.text, TextWidth(te, te.text)));
        }
        return l;
    }

    static string U2InputText(List<(float room, float need, float y, string text, float textW)> l) =>
        string.Join(", ", l.Select(x => $"'{x.text}' {x.textW:0.#}/{x.room:0.#} px"));

    static int Lines(List<(float room, float need, float y, string text, float textW)> l) => l.Select(x => Mathf.RoundToInt(x.y)).Distinct().Count();

    static IEnumerator U2Checks(Ctx c)
    {
        var ui = c.Ui;
        var root = c.Host.Root;
        ui.Shell.ResetLayout();
        yield return Frames(4);

        // ---- (4) File › Options: no clipped label or control, in three languages
        foreach (var lang in new[] { "en", "ja", "zh-Hans" })
        {
            var rows = new List<(string key, List<string> bad, string details)>();
            yield return U2OptionsRows(c, lang, rows);
            var badRows = rows.Where(r => r.bad.Count > 0).ToList();
            c.Check(rows.Count == OptionDefs.All.Count && badRows.Count == 0,
                $"U2-4 options ({lang}): {rows.Count} rows, none with a clipped label or control" + (badRows.Count > 0 ? ": " + string.Join(" | ", badRows.Select(r => r.key + ": " + string.Join("; ", r.bad))) : ""));
            var mp = rows.FirstOrDefault(r => r.key == OptionDefs.CaptureMaxPixels);
            c.Check(mp.bad != null && mp.bad.Count == 0, $"U2-4 options ({lang}): the render size cap row shows its label and its value whole ({mp.details})");
        }
        c.Host.SetLanguage("en", save: false);
        yield return Frames(2);
        OptionsPanel.Show(ui);
        yield return Frames(4);
        {
            var f = OptionsPanel.Open != null && OptionsPanel.Open.Controls.TryGetValue(OptionDefs.CaptureMaxPixels, out var ce) ? ce as IntegerField : null;
            c.Check(f != null && f.value == ui.Options.Int(OptionDefs.CaptureMaxPixels), "U2-4 the render size cap is a typed number field showing the option (" + f?.value + ")");
            if (f != null)
            {
                f.value = 99999999;
                yield return Frames(1);
                c.Check(ui.Options.Int(OptionDefs.CaptureMaxPixels) == OptionDefs.CaptureHardMaxPixels && f.value == OptionDefs.CaptureHardMaxPixels,
                    $"U2-4 typing 99,999,999 stores and shows the cap's maximum ({ui.Options.Int(OptionDefs.CaptureMaxPixels)} / {f.value})");
                ui.Options.Set(OptionDefs.CaptureMaxPixels, null);
                yield return Frames(1);
            }
        }
        while (ui.Dialogs.IsModalOpen) ui.Dialogs.CloseCurrent();
        yield return Frames(2);

        // ---- (3) the inspector's Transform fields
        ui.Ops.NewScene();
        yield return Frames(2);
        var add = ui.Ops.AddItem(StudioDb.FallbackItem, null, null, -1, UiContext.User());
        string id = add.NewIds.FirstOrDefault();
        c.Check(id != null, "U2-3 an item added (" + add + ")");
        if (id != null)
        {
            ui.Ops.Select(new[] { id });
            ui.Shell.FocusPanel(UiShell.InspectorId);
            yield return Frames(6);
            var insp = ui.Shell.PanelRoot(UiShell.InspectorId);
            var pos = insp?.Q<FieldRow>("field-transform.pos");
            var vf = pos?.Control as VectorField;
            c.Check(vf != null && ui.Shell.RegionOf(UiShell.InspectorId) == LayoutRegions.Left, "U2-3 the inspector in the left sidebar shows the position row");
            if (vf != null)
            {
                // typed values of eight characters (one entry each, merged per field)
                float[] typed = { -123.456f, 123.456f, -0.12345f };
                for (int k = 0; k < 3; k++)
                {
                    // the row is looked up again for each value (a write may rebuild the inspector)
                    var v = ui.Shell.PanelRoot(UiShell.InspectorId)?.Q<FieldRow>("field-transform.pos")?.Control as VectorField;
                    if (v != null) v.Fields[k].value = typed[k];
                    yield return Frames(2);
                }
                var rot = ui.Shell.PanelRoot(UiShell.InspectorId)?.Q<FieldRow>("field-transform.rot")?.Control as VectorField;
                if (rot != null) { rot.Fields[0].value = -123.456f; yield return Frames(1); }
                insp = ui.Shell.PanelRoot(UiShell.InspectorId);
                yield return Frames(3);
                var p = ui.Model.Find(id).Doc.Transform.Pos;
                c.Check(Mathf.Abs(p.X + 123.456f) < 1e-3f && Mathf.Abs(p.Y - 123.456f) < 1e-3f && Mathf.Abs(p.Z + 0.12345f) < 1e-4f, $"U2-3 typed position stored ({p.X}, {p.Y}, {p.Z})");
                float sidebar = ui.Shell.Regions[LayoutRegions.Left].Pane.worldBound.width;
                foreach (var path in new[] { "transform.pos", "transform.rot", "transform.scale" })
                {
                    var row = insp.Q("field-" + path);
                    var l = U2Inputs(row);
                    var label = row?.Q<Label>(className: "hs-vector__label");
                    bool below = label != null && l.Count > 0 && l.All(x => x.y >= label.worldBound.yMax - 1f);
                    c.Check(l.Count == 3 && l.All(x => x.room + 0.5f >= x.need && x.textW <= x.room + 0.5f) && below && Lines(l) == 1,
                        $"U2-3 {path} in the {sidebar:0} px sidebar: every field has room for '{U2Sample}' ({string.Join(", ", l.Select(x => x.room.ToString("0.#") + " ≥ " + x.need.ToString("0.#")))} px) and shows its value whole ({U2InputText(l)}); the three fields on {Lines(l)} line{(below ? " below the label" : " beside the label")} (row {row?.worldBound.height:0} px tall)");
                }
                // the region at its minimum width
                var left = ui.Shell.Regions[LayoutRegions.Left];
                ui.Shell.SetSize(left, 300);
                yield return Frames(6);
                insp = ui.Shell.PanelRoot(UiShell.InspectorId);
                {
                    var l = U2Inputs(insp?.Q("field-transform.pos"));
                    c.Check(l.Count == 3 && l.All(x => x.room + 0.5f >= x.need && x.textW <= x.room + 0.5f),
                        $"U2-3 in the sidebar at its minimum ({left.Pane.worldBound.width:0} px) the position fields still show '{U2Sample}' whole ({U2InputText(l)}; {Lines(l)} line(s))");
                }
                ui.Shell.SetSize(left, 360);
                yield return Frames(4);
            }
            // the same row in a 247 px column (the narrowest inspector seen in the player) and in a wide one
            var posSpec = InspectorSchemas.Transform.Fields.First(f => f.Path == "transform.pos");
            foreach (float w in new[] { 247f, 560f })
            {
                var box = new VisualElement();
                box.style.width = w;
                box.style.position = Position.Absolute;
                box.style.left = 10; box.style.top = 10;
                var r = FieldWidgets.Create(ui, posSpec, new FieldContext { Selector = TargetSelector.Selection() });
                box.Add(r);
                ui.Shell.Overlay.Add(box);
                yield return Frames(4);
                var l = U2Inputs(r);
                var label = r.Q<Label>(className: "hs-vector__label");
                bool beside = label != null && l.Count > 0 && l.All(x => x.y < label.worldBound.yMax - 1f);
                if (w < 300)
                    c.Check(l.Count == 3 && Lines(l) >= 2 && l.All(x => x.room + 0.5f >= x.need && x.textW <= x.room + 0.5f),
                        $"U2-3 in a {w:0} px column the fields wrap ({Lines(l)} lines) and each still shows '{U2Sample}' whole ({U2InputText(l)})");
                else
                    c.Check(l.Count == 3 && Lines(l) == 1 && beside && l.All(x => x.textW <= x.room + 0.5f),
                        $"U2-3 in a {w:0} px row the three fields stand beside the label on one line ({U2InputText(l)})");
                box.RemoveFromHierarchy();
            }
        }
        ui.Ops.NewScene();
        yield return Frames(2);

        // ---- (1) the character grid's cell states, (2) its size slider
        ui.Shell.FocusPanel(CharaBrowser.PanelId);
        yield return Frames(3);
        var cb = CharaBrowser.Current;
        c.Check(cb != null, "U2-1 character browser shown");
        if (cb == null) yield break;
        cb.ShowSex(1);
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => { cb.Pump(); return cb.Ready && cb.Index.Indices.Count >= 3; }, 60, ok);
        for (int i = 0; i < 6; i++) { cb.Pump(); yield return null; }
        var grid = cb.Grid;
        grid.Select(-1);
        yield return Frames(2);
        var c1 = grid.CellAt(1);
        var c2 = grid.CellAt(2);
        c.Check(ok[0] && c1 != null && c2 != null, $"U2-1 the grid shows cards ({cb.Index?.Indices.Count} cards, {grid.Columns} columns)");
        if (c1 == null || c2 == null) yield break;
        string hint = L10n.Get(CharaBrowser.HintKey);
        c.Check(!string.IsNullOrEmpty(hint) && hint != CharaBrowser.HintKey && c1.tooltip != null && c1.tooltip.EndsWith(hint, StringComparison.Ordinal) && cb.HintLabel.text == hint,
            $"U2-1 the hint (what a click / double click / Enter does) ends every cell's tooltip and fills the footer line: '{hint}'");
        // hover
        Ev(c, new { type = "pointer", phase = "move", x = c1.worldBound.center.x, y = c1.worldBound.center.y, button = 0 });
        yield return Frames(1);
        bool hover1 = c1.ClassListContains(ThumbGrid<CardEntry>.HoverClass);
        Ev(c, new { type = "pointer", phase = "move", x = c2.worldBound.center.x, y = c2.worldBound.center.y, button = 0 });
        yield return Frames(1);
        c.Check(hover1 && !c1.ClassListContains(ThumbGrid<CardEntry>.HoverClass) && c2.ClassListContains(ThumbGrid<CardEntry>.HoverClass),
            "U2-1 hover: the cell under the pointer is marked (hs-card--hover), and only that cell");
        // press and release: pressed while held, then selected with the keyboard-focus ring; a single click adds nothing
        int nodes0 = ui.Model.Roots.Count;
        Ev(c, new { type = "pointer", phase = "down", x = c1.worldBound.center.x, y = c1.worldBound.center.y, button = 0, clicks = 1 });
        yield return Frames(1);
        bool pressed = c1.ClassListContains(ThumbGrid<CardEntry>.PressedClass);
        Ev(c, new { type = "pointer", phase = "up", x = c1.worldBound.center.x, y = c1.worldBound.center.y, button = 0, clicks = 1 });
        yield return Frames(2);
        var frame = c1.Q(className: ThumbGrid<CardEntry>.FrameClass);
        var text = c1.Q<Label>(className: "hs-card__text");
        var ring = c1.resolvedStyle.borderTopColor;
        c.Check(pressed && !c1.ClassListContains(ThumbGrid<CardEntry>.PressedClass), "U2-1 pressed: hs-card--pressed while the button is held, gone after the release");
        c.Check(grid.SelectedPos == 1 && c1.ClassListContains(ThumbGrid<CardEntry>.SelectedClass) && grid.HasKeyboardFocus && !c2.ClassListContains(ThumbGrid<CardEntry>.SelectedClass),
            "U2-1 a click selects the card (hs-card--selected) and gives the grid the keyboard focus (hs-thumb-grid--focused)");
        c.Check(ring.a > 0.9f && ring.r > 0.8f && ring.g > 0.8f && ring.b > 0.8f && text != null && text.resolvedStyle.backgroundColor.a > 0.9f && (frame == null || frame.resolvedStyle.borderTopWidth < 0.5f),
            $"U2-1 the selected card shows the bright focus ring ({ring}) and a lit name line ({text?.resolvedStyle.backgroundColor}); no applied frame in the character grid");
        c.Check(ui.Model.Roots.Count == nodes0 && cb.Q<Button>("chara-add").enabledSelf && cb.HintLabel.text == L10n.Format("browser.chara.selected", cb.SelectedEntry?.DisplayName),
            $"U2-1 a single click adds nothing (nodes {nodes0} → {ui.Model.Roots.Count}); «Add» is enabled and the footer names the card: '{cb.HintLabel.text}'");
        // keyboard: the arrow moves the selection, Enter adds the selected card (as a double click does)
        var fe = c.Host.Router.Focused;
        c.Info("U2-1 focused before the keys: " + (fe == null ? "none" : fe.GetType().Name + " '" + fe.name + "'"));
        Ev(c, new { type = "key", key = "RightArrow" });
        yield return Frames(2);
        c.Check(grid.SelectedPos == 2 && c2.ClassListContains(ThumbGrid<CardEntry>.SelectedClass) && !c1.ClassListContains(ThumbGrid<CardEntry>.SelectedClass),
            "U2-1 Right arrow moves the selection to the next card (" + grid.SelectedPos + ")");
        int activated = 0;
        Action<CardEntry> onAct = _ => activated++;
        grid.Activated += onAct;
        Ev(c, new { type = "key", key = "Enter" });
        yield return Frames(4);
        grid.Activated -= onAct;
        c.Check(activated == 1 && ui.Model.Roots.Count == nodes0 + 1, $"U2-1 Enter on the focused grid adds the selected card (activations {activated}, nodes {nodes0} → {ui.Model.Roots.Count})");
        ui.Ops.NewScene();
        yield return Frames(2);

        // (2) the size slider: cell width, the option, options.json, a change made elsewhere
        float w0 = grid.CellWidth;
        int cols0 = grid.Columns;
        cb.ZoomSlider.value = 160;
        yield return Frames(4);
        var cell = grid.CellAt(0);
        c.Check(Mathf.Abs(grid.CellWidth - 160) < 0.5f && cell != null && Mathf.Abs(cell.resolvedStyle.width - 160) < 0.5f && ui.Options.Int(OptionDefs.ThumbSizeChara) == 160,
            $"U2-2 the slider sets the card width {w0:0} → {grid.CellWidth:0} px (cell {cell?.resolvedStyle.width:0} px, columns {cols0} → {grid.Columns}) and the option view.thumbSize.chara ({ui.Options.Int(OptionDefs.ThumbSizeChara)})");
        ui.Options.Save();
        string json = System.IO.File.Exists(ui.Options.FilePath) ? System.IO.File.ReadAllText(ui.Options.FilePath) : "";
        int stored = -1;
        try { stored = (int)(JObject.Parse(json)["view"]?["thumbSize.chara"] ?? -1); } catch (Exception) { }
        c.Check(stored == 160, "U2-2 options.json keeps the size (view.thumbSize.chara " + stored + ")");
        ui.Options.Set(OptionDefs.ThumbSizeChara, 100);
        yield return Frames(3);
        c.Check(Mathf.Abs(cb.ZoomSlider.value - 100) < 0.5f && Mathf.Abs(grid.CellWidth - 100) < 0.5f, $"U2-2 the option set elsewhere (options panel) moves the slider and the grid ({cb.ZoomSlider.value:0} / {grid.CellWidth:0})");
        ui.Options.Set(OptionDefs.ThumbSizeChara, null);
        yield return Frames(3);
        c.Check(Mathf.Abs(grid.CellWidth - OptionDefs.CharaThumbDefault) < 0.5f, "U2-2 the option's reset gives the default " + OptionDefs.CharaThumbDefault + " px (" + grid.CellWidth.ToString("0") + ")");
    }
}

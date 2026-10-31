// UiShellTest.play groups for the v0.3 trial fixes: TM measures (information lines only: the options dialog's
// layer and box against the window and the visible descendants that lie outside the box, the facet list heights of the
// item and animation browsers, the regions and their tab strips, the language labels, the window flag) so the numbers
// before and after the fixes come from one place; T checks the fixed behaviour (see V03Checks). Events are synthesised
// through the panel (UiCommands.Send, as ui.input does).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Content.Studio;
using HS2Next.UI;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class UiShellTest
{
    static string R(Rect r) => $"[{r.x:0.#}, {r.y:0.#}, {r.width:0.#} x {r.height:0.#}]";

    static bool Inside(Rect inner, Rect outer, float tol = 0.5f) =>
        inner.xMin >= outer.xMin - tol && inner.yMin >= outer.yMin - tol && inner.xMax <= outer.xMax + tol && inner.yMax <= outer.yMax + tol;

    static System.Reflection.PropertyInfo computedStyleProp;

    /// <summary>The element clips its children (overflow hidden in its computed style; UI Toolkit's resolved style has no
    /// overflow, so the internal computed style is read; a scroll view's viewport when that is not available).</summary>
    static bool Clips(VisualElement e)
    {
        try
        {
            computedStyleProp ??= typeof(VisualElement).GetProperty("computedStyle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            var cs = computedStyleProp?.GetValue(e);
            var ov = cs?.GetType().GetProperty("overflow")?.GetValue(cs);
            if (ov != null) return ov.ToString() != "Visible";
        }
        catch (Exception) { }
        return e.ClassListContains(ScrollView.viewportUssClassName);
    }

    /// <summary>The part of an element that is drawn: its world bound clipped by every clipping ancestor between it and
    /// <paramref name="stop"/> (the stop element's own clipping is not counted). Empty when nothing of it shows.</summary>
    static Rect Drawn(VisualElement e, VisualElement stop)
    {
        var r = e.worldBound;
        for (var p = e.hierarchy.parent; p != null && p != stop; p = p.hierarchy.parent)
        {
            if (!Clips(p)) continue;
            var b = p.worldBound;
            float x0 = Mathf.Max(r.xMin, b.xMin), y0 = Mathf.Max(r.yMin, b.yMin), x1 = Mathf.Min(r.xMax, b.xMax), y1 = Mathf.Min(r.yMax, b.yMax);
            if (x1 <= x0 || y1 <= y0) return Rect.zero;
            r = Rect.MinMaxRect(x0, y0, x1, y1);
        }
        return r;
    }

    static bool Shown(VisualElement e, VisualElement stop)
    {
        for (var x = e; x != null && x != stop; x = x.hierarchy.parent)
            if (x.resolvedStyle.display == DisplayStyle.None || x.resolvedStyle.visibility == Visibility.Hidden) return false;
        return true;
    }

    /// <summary>The visible descendants of a box drawn outside it: count, the largest overflow per side, examples.</summary>
    static (int count, float left, float top, float right, float bottom, List<string> examples) Outside(VisualElement box)
    {
        var bb = box.worldBound;
        int n = 0;
        float l = 0, t = 0, rr = 0, b = 0;
        var ex = new List<string>();
        foreach (var e in box.Query<VisualElement>().ToList())
        {
            if (e == box || !Shown(e, box)) continue;
            var d = Drawn(e, box);
            if (d.width <= 0.5f || d.height <= 0.5f) continue;
            if (Inside(d, bb)) continue;
            n++;
            l = Mathf.Max(l, bb.xMin - d.xMin); t = Mathf.Max(t, bb.yMin - d.yMin);
            rr = Mathf.Max(rr, d.xMax - bb.xMax); b = Mathf.Max(b, d.yMax - bb.yMax);
            if (ex.Count < 5) ex.Add((string.IsNullOrEmpty(e.name) ? e.GetType().Name : e.name) + " " + R(d));
        }
        return (n, l, t, rr, b, ex);
    }

    // ---------------- TM: measurements (information)

    static IEnumerator V03Measure(Ctx c)
    {
        var ui = c.Ui;
        var root = c.Host.Root;
        float s = root.panel.scaledPixelsPerPoint;
        c.Info($"TM window {Screen.width}x{Screen.height}, panel scale {s:0.###}, root {R(root.worldBound)}");
#if UNITY_EDITOR
        c.Info("TM ProjectSettings resizableWindow " + UnityEditor.PlayerSettings.resizableWindow + ", fullScreenMode " + UnityEditor.PlayerSettings.fullScreenMode + ", default " + UnityEditor.PlayerSettings.defaultScreenWidth + "x" + UnityEditor.PlayerSettings.defaultScreenHeight);
#endif
        // regions and tab strips
        foreach (var name in LayoutRegions.All)
        {
            var reg = ui.Shell.Regions[name];
            var tabs = reg.Strip.Children().ToList();
            int clipped = tabs.Count(t => !Inside(t.worldBound, reg.Strip.worldBound));
            c.Info($"TM region {name}: pane {R(reg.Pane.worldBound)}, collapsed {reg.Collapsed}, panels [{string.Join(", ", reg.Panels)}] active {reg.Active}; strip {R(reg.Strip.worldBound)}, {tabs.Count} tabs, {clipped} outside the strip");
        }
        c.Info($"TM viewport {R(ui.Viewport.Element.worldBound)}, toolbar {R(root.Q("toolbar").worldBound)}, status bar {R(root.Q("statusbar").worldBound)}");

        // the options dialog
        OptionsPanel.Show(ui);
        for (int i = 0; i < 4; i++) yield return PlayModeHarness.WaitFrames(1);
        var layer = ui.Shell.DialogLayer;
        var box = layer.Q(className: "hs-dialog");
        c.Info($"TM options: layer {R(layer.worldBound)} vs root {R(root.worldBound)} (covers {Inside(root.worldBound, layer.worldBound)}; last child {root.hierarchy.childCount > 0 && root.hierarchy[root.hierarchy.childCount - 1] == layer})");
        if (box != null)
        {
            var o = Outside(box);
            var panel = box.Q("options-panel");
            var scroll = box.Q<ScrollView>("options-scroll");
            c.Info($"TM options: box {R(box.worldBound)} (inside the window {Inside(box.worldBound, root.worldBound)}); options panel {R(panel?.worldBound ?? Rect.zero)}, scroll {R(scroll?.worldBound ?? Rect.zero)}");
            c.Info($"TM options: {o.count} visible descendants outside the box (left {o.left:0.#}, top {o.top:0.#}, right {o.right:0.#}, bottom {o.bottom:0.#} px){(o.examples.Count > 0 ? ": " + string.Join("; ", o.examples) : "")}");
        }
        ui.Dialogs.CloseCurrent();
        yield return PlayModeHarness.WaitFrames(2);

        // the facet lists of the item and animation browsers
        foreach (var (panelId, browserName) in new[] { ("add.item", "items-browser"), ("add.anime", "anime-browser") })
        {
            ui.Shell.FocusPanel(panelId);
            for (int i = 0; i < 4; i++) yield return PlayModeHarness.WaitFrames(1);
            VisualElement br = panelId == "add.item" ? (VisualElement)root.Q<ListBrowser<StudioItem>>(browserName) : root.Q<ListBrowser<StudioAnime>>(browserName);
            if (br == null) { c.Info("TM " + panelId + ": no browser"); continue; }
            ListView groups = (br as ListBrowser<StudioItem>)?.Groups ?? (br as ListBrowser<StudioAnime>)?.Groups;
            ListView cats = (br as ListBrowser<StudioItem>)?.Categories ?? (br as ListBrowser<StudioAnime>)?.Categories;
            var list = (br as ListBrowser<StudioItem>)?.List ?? (br as ListBrowser<StudioAnime>)?.List;
            // a group selected so the category list shows
            var keys = (IList<string>)groups.itemsSource;
            string g0 = keys.FirstOrDefault(k => k != null);
            if (g0 != null) { (br as ListBrowser<StudioItem>)?.SelectGroup(g0); (br as ListBrowser<StudioAnime>)?.SelectGroup(g0); }
            for (int i = 0; i < 4; i++) yield return PlayModeHarness.WaitFrames(1);
            var body = br.Q(className: "hs-browser__body");
            c.Info($"TM {panelId}: region {ui.Shell.RegionOf(panelId)}, body {R(body?.worldBound ?? Rect.zero)}; groups {R(groups.worldBound)} = {groups.worldBound.height / 20f:0.#} rows of {keys.Count}; categories {R(cats.worldBound)} = {cats.worldBound.height / 20f:0.#} rows of {((IList<string>)cats.itemsSource).Count}; list {R(list.worldBound)}");
            (br as ListBrowser<StudioItem>)?.SelectGroup(null); (br as ListBrowser<StudioAnime>)?.SelectGroup(null);
        }
        // the other browsers' parts
        foreach (var (panelId, part) in new[] { ("add.chara", "chara-grid"), ("add.chara", "chara-folders"), ("add.chara", "chara-header"), ("add.chara", "chara-footer"), ("add.map", "maps-list"), ("add.light", "lights-list"), ("add.pose", "pose-grid") })
        {
            ui.Shell.FocusPanel(panelId);
            for (int i = 0; i < 3; i++) yield return PlayModeHarness.WaitFrames(1);
            var e = root.Q(part);
            var pr = ui.Shell.PanelRoot(panelId);
            int outside = 0;
            if (pr != null && e != null)
                foreach (var d in e.Query<VisualElement>().ToList())
                {
                    if (!Shown(d, pr)) continue;
                    var dr = Drawn(d, pr);
                    if (dr.width > 0.5f && dr.height > 0.5f && !Inside(dr, pr.worldBound)) outside++;
                }
            c.Info($"TM {panelId} {part}: {R(e?.worldBound ?? Rect.zero)} in the panel {R(pr?.worldBound ?? Rect.zero)}; {outside} visible parts outside the panel");
        }

        // language labels
        foreach (var lang in new[] { "en", "ja", "zh-Hans" })
        {
            c.Host.SetLanguage(lang, save: false);
            yield return PlayModeHarness.WaitFrames(1);
            c.Info($"TM language {lang}: toolbar.language '{root.Q<Button>("tb-language")?.text}', option.general.language '{L10n.Get("option.general.language")}'");
        }
        c.Host.SetLanguage("en", save: false);
        yield return PlayModeHarness.WaitFrames(1);
        ui.Shell.ResetLayout();
        for (int i = 0; i < 3; i++) yield return PlayModeHarness.WaitFrames(1);
    }
}

// MaterialEditTest, the Materials view and the hair bone rows (package U). Groups of the play row, on the
// app with the UI, driven through ui.input and ui.query and judged by element state, the history and the document: numbers only, nothing is
// rendered, captured or opened. Every item is synthetic; the target cards hold none.
//   MU1   nothing of the tab stands before it is shown, no refresh while it is closed, the note on two selected
//   MU2   the walk renderer, material, property on the character, a hair part, a clothes kind, an accessory; the selection survives a rebuild of
//         the inspector
//   MU3   one row of each type writes the right item: float, colour, texture offset and tiling, keyword, queue, Enabled; ↺ takes it away
//   MU4   an item of each status: its line, its reason, Remove; a read-only item has none
//   MU5   the texture row: the pick, the toast, Remove image; no path in the document
//   MU6   the shader list, its search, a pick, ↺
//   MU7   the item's section: collapsed, built when opened, a row writes item.materialOverrides, the note over a colour slot
//   MU8   the map's section: shown with a map, its materials, the mode Renderers, the search of two characters, the cap
//   MU9   the view's text carries no community plugin's name; the literal strings of its source neither
//   MU10  the three buttons of the Edit tab: tab and target
//   MU11  the tab header at panel widths 360 and 300 in three languages, through ui.query
//   MW3   first show of the tab, of the item's section
//   ML7   Esc during a drag: slider, label, vector row, colour picker on a character, an item, the map, and on the hair bone rows; as FE3
//   MO7   a member's rows are off with the mosaic's tooltip; a stale write is refused
//   MJU   the hair bone section: nothing read before it is opened, Add bone, a typed value, ↺, the entries found nowhere, an entry acting on an accessory
// Headless, in RunFull: MJ2 a hair modifier: edit, remove, save, load, the other entries byte-equal; MJ4 a modifier and a correction on one bone.
// RunHairCorpus: MJ5 on every card with such a modifier.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HS2Next;
using HS2Next.App;
using HS2Next.Chara.Ext;
using HS2Next.Chara.Mosaic;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Bridge;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using HS2Next.UI.Widgets;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using static MakerTestKit;

public static partial class MaterialEditTest
{
    const double ShowBarMs = 100;

    static string muChara, muMale, muItem;
    static bool muReady;

    static UiContext MuUi => UiHost.Current?.Context;
    static IPanel MuPanel => UiHost.Current?.Root?.panel;
    static VisualElement MuTree => MuPanel?.visualTree;
    static InspectorPanel MuInsp => MuTree?.Q<InspectorPanel>();
    static VisualElement MuQ(string name) => MuTree?.Q(name);
    static int MuUndos => MuUi.Session.Commands.UndoList.Count;
    static string MuTop => MuUi.Session.Commands.UndoList.Count > 0 ? MuUi.Session.Commands.UndoList[0].Label : null;
    static IEnumerator MuFrames(int n) => PlayModeHarness.WaitFrames(n);

    static partial void PlayGroups(List<(string id, string title, Func<IEnumerator> run)> groups)
    {
        groups.Add(("MU1", "the tab: first show, closed", MU1));
        groups.Add(("MU2", "the walk, the selection", MU2));
        groups.Add(("MU3", "a row of each type", MU3));
        groups.Add(("MU4", "not applied", MU4));
        groups.Add(("MU5", "the texture row", MU5));
        groups.Add(("MU6", "the shader row", MU6));
        groups.Add(("MU7", "the item's section", MU7));
        groups.Add(("MU8", "the map's section", MU8));
        groups.Add(("MU9", "no plugin's name", MU9));
        groups.Add(("MU10", "the Edit tab's buttons", MU10));
        groups.Add(("MU11", "the tab header", MU11));
        groups.Add(("MW3", "first show", MW3));
        groups.Add(("ML7", "Esc during a drag", ML7));
        groups.Add(("ML12", "a drag that reloads the part", ML12));
        groups.Add(("MO7", "mosaic rows", MO7));
        groups.Add(("MJU", "the hair bone section", MJU));
    }

    // ------------------------------------------------------------ helpers

    static string MuKey(JObject r) => (string)r?["key"] ?? (string)r?["added"]?[0]?["key"];

    static IEnumerator MuPrepare()
    {
        if (muReady) yield break;
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => UiHost.Current != null && UiHost.Current.IsReady, 60, ok);
        if (!Check("UI ready", ok[0])) yield break;
        yield return OkCall(new { cmd = "scene.new" }, "scene.new");
        var t = TestTargets.Load().visual;
        string Rel(string p)
        {
            foreach (var pre in new[] { "chara/female/", "chara/male/" })
                if (p.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) return p.Substring(pre.Length);
            return p;
        }
        yield return OkCall(new { cmd = "chara.load", path = Rel(t.female), sex = "female" }, "chara.load the female target");
        muChara = MuKey(last);
        yield return OkCall(new { cmd = "chara.load", path = Rel(t.male), sex = "male" }, "chara.load the male target");
        muMale = MuKey(last);
        yield return OkCall(new { cmd = "item.add", no = SphereRow }, "item.add");
        muItem = MuKey(last);
        yield return PlayModeHarness.WaitUntil(() => App.Runtime.FindChara(muChara)?.State == RealizationState.Built && App.Runtime.FindChara(muMale)?.State == RealizationState.Built && App.Runtime.FindItem(muItem)?.State == RealizationState.Built, 180, ok);
        if (!Check("everything built", ok[0])) yield break;
        yield return OkCall(new { cmd = "scene.pause", paused = true }, "the global pause");
        MuUi.Shell.ResetLayout();
        yield return MuFrames(3);
        muReady = true;
    }

    static MaterialEditView MuTab => MuInsp?.Chara != null && MuInsp.Chara.TabBodies.TryGetValue(MaterialEditView.TabId, out var b) ? b.Q<MaterialEditView>() : null;

    static IEnumerator MuReveal(VisualElement e)
    {
        bool any = false;
        for (var x = e?.hierarchy.parent; x != null; x = x.hierarchy.parent)
            if (x is ScrollView sv) { sv.ScrollTo(e); any = true; }
        if (any) yield return MuFrames(3);
    }

    static IEnumerator MuClick(VisualElement e, string what)
    {
        yield return MuReveal(e);
        if (!Check("the element to click: " + what, e != null && e.panel != null)) yield break;
        var at = e.worldBound.center;
        yield return Call(new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(new JObject { ["type"] = "pointer", ["x"] = at.x, ["y"] = at.y, ["phase"] = "click", ["button"] = 0 }), ["frames"] = 2 });
        Check("click " + what, LastOk, LastOk ? "" : Short(lastText));
        yield return MuFrames(2);
    }

    /// <summary>Selects the character, shows the inspector and its Materials tab on an array.</summary>
    static IEnumerator MuOpenTab(string id, string target = "character")
    {
        yield return OkCall(new { cmd = "select", keys = new[] { id } }, "select the character");
        yield return OkCall(new { cmd = "ui.panel", panel = "inspector", op = "show" }, "show the inspector");
        yield return MuFrames(3);
        MuInsp?.Refresh(true);
        yield return MuFrames(2);
        MuInsp?.Chara?.ShowTab(MaterialEditView.TabId);
        yield return MuFrames(3);
        MuTab?.SetTarget(target);
        MuTab?.SetEditedOnly(false);
        MuTab?.SetQuery("");
        yield return MuFrames(3);
        MuTab?.Refresh();
        Check("the tab is open on " + target, MuTab != null && MuTab.Built && MuTab.TargetText == target, MuTab?.TargetText);
    }

    // Opens every renderer's line of the list.
    static void MuOpenAll(MaterialEditView v)
    {
        foreach (var r in v.Rows.Where(x => x.IsRenderer).ToList())
            if (!v.IsOpenFor(r.Renderer)) v.Reveal(r.Renderer.Key);
    }

    // The first material of the view whose rows pass, selected; null when none.
    static string MuPick(MaterialEditView v, Func<MaterialEditView, bool> ok)
    {
        MuOpenAll(v);
        foreach (var m in v.Rows.Where(x => x.Material != null).Select(x => x.Material).Distinct().ToList())
        {
            v.SelectMaterial(m);
            if (v.Selection != null && !v.Selection.Protected && v.Selection.Editable.Count > 0 && ok(v)) return m;
        }
        return null;
    }

    static List<MaterialOverride> MuArray(MaterialEditView v) => MuUi.Ops.MaterialArray(v.Target.Address);
    static string MuText(MaterialEditView v) => FieldJson.ToText(MuArray(v));

    // A float row the slider can move in both directions.
    static MaterialFloatRow MuFloat(MaterialEditView v) => v.ValueRows.OfType<MaterialFloatRow>().FirstOrDefault(r => r.OffKey == null && r.Number.Slider != null && !r.Number.Whole && r.Property != "dirty");
    static T MuRow<T>(MaterialEditView v) where T : MaterialRow => v.ValueRows.OfType<T>().FirstOrDefault(r => r.OffKey == null);
    static MaterialTextureRow MuTexture(MaterialEditView v) => v.ValueRows.OfType<MaterialTextureRow>().FirstOrDefault(r => r.OffKey == null && r.Offset != null);

    static IEnumerator MuUndoAll(int to)
    {
        while (MuUndos > to) { MuUi.Session.Commands.Undo(); MuUi.Runtime?.ApplyNow(); }
        yield return MuFrames(3);
    }

    // ------------------------------------------------------------ MU1

    static IEnumerator MU1()
    {
        yield return MuPrepare();
        yield return OkCall(new { cmd = "select", keys = new[] { muChara } }, "select the character");
        yield return OkCall(new { cmd = "ui.panel", panel = "inspector", op = "show" }, "show the inspector");
        CharaInspector.LastTab = "anime";
        MuInsp.Refresh(true);
        yield return MuFrames(3);
        var ch = MuInsp.Chara;
        var view = MuTab;
        Check("the tab's button stands after Advanced pose", MuQ("tab-" + MaterialEditView.TabId) != null && MuQ("tab-advancedPose")?.parent?.IndexOf(MuQ("tab-" + MaterialEditView.TabId)) == MuQ("tab-advancedPose")?.parent?.IndexOf(MuQ("tab-advancedPose")) + 1);
        Check("before it is first shown nothing of it stands", view != null && view.panel == null && !view.Built && MuQ("mat-list") == null, view == null ? "no view" : "built " + view.Built);
        yield return PlayModeHarness.WaitRealtime(1.2);
        Check("a tab never shown reads nothing", view.Refreshes == 0, view.Refreshes.ToString());
        yield return MuClick(MuQ("tab-" + MaterialEditView.TabId), "the tab's button");
        Check("a click shows the tab and builds it", ch.CurrentTab == MaterialEditView.TabId && view.Built && view.Refreshes > 0, ch.CurrentTab);
        yield return Call(new { cmd = "ui.query", name = "mat-list" });
        Check("ui.query finds the list, visible", LastOk && last["elements"]?[0]?.Value<bool>("visible") == true, Short(lastText));
        yield return PlayModeHarness.WaitRealtime(1.2);
        int open = view.Refreshes;
        Check("it refreshes while shown, at most twice a second", open >= 2 && open <= 6, open.ToString());
        ch.ShowTab("anime");
        yield return MuFrames(2);
        int closed = view.Refreshes;
        yield return PlayModeHarness.WaitRealtime(1.2);
        Check("no refresh while closed", view.Refreshes == closed && view.panel == null, closed + " -> " + view.Refreshes);
        ch.ShowTab(MaterialEditView.TabId);
        yield return MuFrames(3);
        Check("shown again: a refresh, the same view", view.Refreshes > closed && view.panel != null);
        yield return OkCall(new { cmd = "select", keys = new[] { muChara, muMale } }, "select two");
        yield return MuFrames(3);
        MuInsp.Refresh(true);
        yield return MuFrames(3);
        MuTab?.Refresh();
        yield return MuFrames(2);
        var note = MuQ("mat-primary-only");
        Check("two selected: the note, the primary's tab", note != null && note.resolvedStyle.display == DisplayStyle.Flex && MuTab?.Target.Owner == muChara);
        yield return OkCall(new { cmd = "select", keys = new[] { muChara } }, "select one");
        yield return MuFrames(3);
    }

    // ------------------------------------------------------------ MU2

    static IEnumerator MU2()
    {
        yield return MuPrepare();
        yield return MuOpenTab(muChara);
        var v = MuTab;
        if (v == null) yield break;
        var choices = MuQ("mat-target") as DropdownField;
        Check("the selector lists the character first", choices != null && choices.choices.Count > 1 && choices.value == L10n.Get("inspector.materials.character"), choices?.value);
        var targets = new List<string> { "character" };
        var real = App.Runtime.FindChara(muChara);
        for (int k = 0; k < 4; k++) if (real.Context.Hair[k]) { targets.Add("hair:" + k); break; }
        for (int k = 0; k < 8; k++) if (real.Context.Clothes[k]) { targets.Add("clothes:" + k); break; }
        foreach (var s in real.Context.Accessories) if (s != null && s.Object) { targets.Add("acc:" + real.AccessoryIdOf(s)); break; }
        Info("targets walked: " + string.Join(", ", targets.Select(x => x.Split(':')[0])));
        foreach (var t in targets)
        {
            v.SetTarget(t);
            yield return MuFrames(3);
            v.Refresh();
            string kind = t.Split(':')[0];
            var rend = v.Rows.FirstOrDefault(r => r.IsRenderer && !r.Renderer.Member && !r.Renderer.DrawsBody && r.Renderer.Materials.Count > 0);
            if (!Check(kind + ": the list holds a renderer with materials", rend != null, v.Rows.Count + " rows")) continue;
            string key = rend.Renderer.Key;
            yield return MuFrames(2);
            v.List.ScrollToItem(v.Rows.ToList().IndexOf(rend));
            yield return MuFrames(3);
            var line = MuQ("mat-renderer-" + key);
            yield return MuClick(line, kind + ": the renderer's line");
            Check(kind + ": a click opens the renderer", v.IsOpenFor(v.Rows.First(r => r.IsRenderer && r.Renderer.Key == key).Renderer), "rows " + v.Rows.Count);
            var mat = v.Rows.FirstOrDefault(r => r.Material != null && r.Renderer?.Key == key);
            if (!Check(kind + ": a material line under it", mat != null)) continue;
            v.List.ScrollToItem(v.Rows.ToList().IndexOf(mat));
            yield return MuFrames(3);
            yield return MuClick(MuQ("mat-material-" + mat.Material), kind + ": the material's line");
            Check(kind + ": a click selects the material", v.SelectedMaterial == mat.Material && v.ValueRows.Count > 2, v.SelectedMaterial + ", " + v.ValueRows.Count + " rows");
            yield return Call(new { cmd = "ui.query", name = "mat-name" });
            Check(kind + ": ui.query shows the name and the slots", LastOk && ((string)last["elements"]?[0]?["text"] ?? "").StartsWith(mat.Material, StringComparison.Ordinal), Short(lastText));
            Check(kind + ": a row per shown property, none hidden unless named", v.ValueRows.Where(r => r.Property != null).All(r => r.name.StartsWith("mat-prop-", StringComparison.Ordinal) || r.name.StartsWith("mat-kw-", StringComparison.Ordinal)));
        }
        // the selection survives a rebuild of the inspector
        v.SetTarget(targets[targets.Count > 2 ? 2 : 1]);
        yield return MuFrames(3);
        v.Refresh();
        string picked = MuPick(v, x => x.ValueRows.Count > 2);
        string target = v.TargetText;
        if (!Check("a material for the rebuild", picked != null)) yield break;
        MuInsp.Refresh(true);
        yield return MuFrames(4);
        var w = MuTab;
        Check("a rebuild of the inspector made a new view", w != null && w != v);
        w?.Refresh();
        yield return MuFrames(2);
        Check("the target, the material and the rows survive it", w != null && w.TargetText == target && w.SelectedMaterial == picked && w.ValueRows.Count > 2, w?.TargetText + " / " + w?.SelectedMaterial);
        w?.SetTarget("character");
        yield return MuFrames(2);
    }

    // ------------------------------------------------------------ MU3

    static string MuClothes()
    {
        var real = App.Runtime.FindChara(muChara);
        for (int k = 0; k < 8; k++) if (real.Context.Clothes[k]) return "clothes:" + k;
        return "character";
    }

    static IEnumerator MU3()
    {
        yield return MuPrepare();
        yield return MuOpenTab(muChara, MuClothes());
        var v = MuTab;
        if (v == null) yield break;
        string mat = MuPick(v, x => MuFloat(x) != null && MuRow<MaterialColorRow>(x) != null);
        if (!Check("a material with a float and a colour", mat != null)) yield break;
        string before = MuText(v);
        int u0 = MuUndos;

        var fr = MuFloat(v);
        double was = fr.Number.Value, to = was > 0.5 ? was - 0.25 : was + 0.25;
        fr.Number.Raise(EditPhase.Typed, to);
        yield return MuFrames(2);
        var fi = MuArray(v).OfType<MeFloat>().FirstOrDefault(i => i.Property == fr.Property);
        Check("a float row writes a float item", fi != null && fi.Material == mat && Mathf.Abs(fi.Value - (float)to) < 1e-5f && MuUndos == u0 + 1 && MuTop == OpLabels.MaterialEdit, fi?.Value + ", entries " + (MuUndos - u0) + ", " + MuTop);
        Check("the new item keeps what the material held as its original", fi?.Original != null && Mathf.Abs(fi.Original.Value - (float)was) < 1e-5f, fi?.Original?.ToString());
        v.Refresh();
        Check("the row marks the item", fr.Marked);
        fr.Number.Raise(EditPhase.Typed, to + 0.01);
        yield return MuFrames(2);
        Check("a second typed value chains into the same entry", MuUndos == u0 + 1, (MuUndos - u0).ToString());
        fr.ResetItem();
        yield return MuFrames(2);
        Check("↺: the item leaves, one entry «Reset material edit», the value back", MuArray(v).OfType<MeFloat>().All(i => i.Property != fr.Property) && MuTop == OpLabels.MaterialReset && Mathf.Abs((float)fr.Number.Value - (float)was) < 1e-5f, fr.Number.Value + " / " + was);

        var cr = MuRow<MaterialColorRow>(v);
        int u1 = MuUndos;
        Check("the picker opens", cr.OpenPicker());
        yield return MuFrames(3);
        var hex = cr.Picker?.Q<TextField>("hex");
        if (Check("the picker's hex field", hex != null))
        {
            hex.value = "336699";
            yield return MuFrames(2);
            cr.Picker?.Close(true);
            yield return MuFrames(2);
            var ci = MuArray(v).OfType<MeColor>().FirstOrDefault(i => i.Property == cr.Property);
            Check("a colour row writes a colour item, one entry", ci != null && Mathf.Abs(ci.Value.R - 0.2f) < 0.01f && Mathf.Abs(ci.Value.B - 0.6f) < 0.01f && MuUndos == u1 + 1 && MuTop == OpLabels.MaterialEdit, (ci == null ? "none" : ci.Value.R + "," + ci.Value.B) + ", entries " + (MuUndos - u1));
            cr.ResetItem();
            yield return MuFrames(2);
        }
        string tmat = MuPick(v, x => MuTexture(x) != null);
        if (Check("a material with a texture row", tmat != null))
        {
            var tr = MuTexture(v);
            int u2 = MuUndos;
            tr.Offset.Fields[0].value = 0.25f;
            yield return MuFrames(2);
            var ti = MuArray(v).OfType<MeTexture>().FirstOrDefault(i => i.Property == tr.Property);
            Check("the Offset row writes a texture item's offset", ti != null && ti.Offset.HasValue && Mathf.Abs(ti.Offset.Value.X - 0.25f) < 1e-5f && ti.Image == null && MuUndos == u2 + 1, ti?.Offset?.X.ToString());
            tr.Tiling.Fields[1].value = 2f;
            yield return MuFrames(2);
            ti = MuArray(v).OfType<MeTexture>().FirstOrDefault(i => i.Property == tr.Property);
            Check("the Tiling row writes the same item's scale", ti != null && ti.Scale.HasValue && Mathf.Abs(ti.Scale.Value.Y - 2f) < 1e-5f, ti?.Scale?.Y.ToString());
            tr.ResetItem();
            yield return MuFrames(2);
        }
        var q = v.ValueRows.OfType<MaterialQueueRow>().First();
        double queue = q.Number.Value;
        q.Number.Raise(EditPhase.Typed, queue + 10);
        yield return MuFrames(2);
        var si = MuArray(v).OfType<MeShader>().FirstOrDefault();
        Check("the queue row writes a shader item's renderQueue", si != null && si.RenderQueue == (int)queue + 10 && si.Shader == null, si?.RenderQueue?.ToString());
        q.ResetItem();
        yield return MuFrames(2);
        Check("↺ on the queue: the item leaves", !MuArray(v).OfType<MeShader>().Any());
        string kmat = MuPick(v, x => MuRow<MaterialKeywordRow>(x) != null);
        if (kmat != null)
        {
            var kr = MuRow<MaterialKeywordRow>(v);
            bool on = kr.Check.value;
            kr.Check.value = !on;
            yield return MuFrames(2);
            var ki = MuArray(v).OfType<MeKeyword>().FirstOrDefault(i => i.Property == kr.Property);
            Check("a keyword row writes a keyword item", ki != null && ki.Value == !on && ki.Original == on, kr.Property);
            kr.ResetItem();
            yield return MuFrames(2);
        }
        else Info("no keyword row on this target");
        // the queue's edit reloaded the part; the list follows at its next refresh, and this test reads the renderer itself
        v.Refresh();
        yield return MuFrames(1);
        var rend = v.Rows.First(r => r.IsRenderer && !r.Renderer.Member && !r.Renderer.DrawsBody).Renderer;
        bool enabled = rend.Renderer.enabled;
        v.SetRendererEnabled(rend, !enabled);
        yield return MuFrames(2);
        var ri = MuArray(v).OfType<MeRenderer>().FirstOrDefault(i => i.Renderer == rend.Key);
        Check("the renderer's toggle writes a renderer item", ri != null && ri.Value == (enabled ? 0 : 1) && rend.Renderer.enabled == !enabled, ri?.Value.ToString());
        v.SetRendererEnabled(rend, enabled);
        yield return MuFrames(2);
        Check("back to the value it had: the item leaves", !MuArray(v).OfType<MeRenderer>().Any() && rend.Renderer.enabled == enabled);
        Check("after all of it the array is as it was", MuText(v) == before, Short(MuText(v)));
        yield return MuUndoAll(u0);
    }

    // ------------------------------------------------------------ MU4

    static IEnumerator MU4()
    {
        yield return MuPrepare();
        yield return MuOpenTab(muChara, MuClothes());
        var v = MuTab;
        if (v == null) yield break;
        string mat = MuPick(v, x => MuFloat(x) != null && MuTexture(x) != null);
        if (!Check("a material for the items", mat != null)) yield break;
        string prop = MuFloat(v).Property, tex = MuTexture(v).Property;
        int u0 = MuUndos;
        var items = new List<MaterialOverride>
        {
            new MeShader { Material = mat, Shader = "Shader/Not Here" },
            new MeRenderer { Renderer = "no_such_renderer", Property = "Enabled", Value = 0 },
            new MeFloat { Material = mat, Property = "NoSuchProperty", Value = 1f },
            new MeFloat { Material = "no_such_material", Property = "Metallic", Value = 1f },
            new MeFloat { Material = "c_m_mnpb", Property = "Metallic", Value = 1f },
            new MeFloat { Material = mat, Property = prop, ValueText = "text" },
            new MeTexture { Material = mat, Property = tex, Image = "res/" + new string('0', 64) + ".png" },
            new MeCubemap { Material = mat, Property = "Cube" },
        };
        var r = MuUi.Ops.EditAppearance(new[] { muChara }, new (string, object)[] { (v.Target.Address.ArrayPath, items) }, UiContext.User("Material edit"), allowRebuild: true);
        Check("the array of a hand-made file stands", r.Ok, r.Code + " " + r.Message);
        MuUi.Runtime?.ApplyNow();
        yield return MuFrames(4);
        v = MuTab;
        v.Refresh();
        yield return MuFrames(2);
        var seen = v.Stale.Select(s => s.state.Status).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        Info("statuses: " + string.Join(", ", v.Stale.Select(s => s.item.Type + "=" + s.state.Status)));
        foreach (var st in new[] { MaterialStatus.ShaderUnavailable, MaterialStatus.ObjectMissing, MaterialStatus.NotOnShader, MaterialStatus.MaterialMissing, MaterialStatus.MosaicProtected, MaterialStatus.BadValue, MaterialStatus.Undecodable, MaterialStatus.NotImplemented })
            Check("an item reads " + st, seen.Contains(st), string.Join(", ", seen));
        var fold = MuQ("mat-notapplied") as Foldout;
        if (!Check("the section stands with its count", fold != null && fold.resolvedStyle.display == DisplayStyle.Flex && fold.text == L10n.Format("inspector.materials.notApplied", v.Stale.Count), fold?.text)) yield break;
        Check("its lines are not built before it is opened", MuQ("mat-notapplied-list") == null);
        fold.value = true;
        yield return MuFrames(3);
        var whys = MuTree.Query<Label>("mat-notapplied-why").ToList();
        Check("a line per item, each with its reason", whys.Count == v.Stale.Count && whys.Select(l => l.text).SequenceEqual(v.Stale.Select(s => MaterialEditView.Why(s.state))), whys.Count + " / " + v.Stale.Count);
        Check("no line shows a key for a text", whys.All(l => !l.text.StartsWith("inspector.", StringComparison.Ordinal)), string.Join(" | ", whys.Select(l => l.text)));
        foreach (var st in new[] { MaterialStatus.Driven, MaterialStatus.FallbackFixed })
            Check("the reason " + st + " has a text", !MaterialEditView.Why(new MaterialItemState(st)).StartsWith("inspector.", StringComparison.Ordinal));
        Check("a read-only type has no Remove", MuTree.Query<Button>().ToList().Count(b => b.name != null && b.name.StartsWith("mat-notapplied-remove-", StringComparison.Ordinal)) == v.Stale.Count(s => MaterialEdits.IsEditable(s.item)));
        int n = MuArray(v).Count, u1 = MuUndos, stale = v.Stale.Count;
        var first = v.Stale.First(s => MaterialEdits.IsEditable(s.item));
        int index = v.Stale.ToList().FindIndex(s => ReferenceEquals(s.item, first.item));
        yield return MuClick(MuQ("mat-notapplied-remove-" + index), "Remove");
        yield return MuFrames(3);
        Check("Remove takes the item away: one entry, the line gone", MuArray(v).Count == n - 1 && MuUndos == u1 + 1 && MuTop == OpLabels.MaterialClear && v.Stale.Count == stale - 1, MuArray(v).Count + " / " + n + ", " + v.Stale.Count);
        fold.value = false;
        yield return MuUndoAll(u0);
        MuInsp.Refresh(true);
        yield return MuFrames(3);
    }

    // ------------------------------------------------------------ MU5

    static IEnumerator MU5()
    {
        yield return MuPrepare();
        yield return MuOpenTab(muChara, MuClothes());
        var v = MuTab;
        if (v == null) yield break;
        string mat = MuPick(v, x => MuTexture(x) != null);
        if (!Check("a material with a texture row", mat != null)) yield break;
        var row = MuTexture(v);
        string folder = Path.Combine(OutDir, "mu5_marked_folder");
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "mu5.png");
        File.WriteAllBytes(file, TestPng(new Color(0.2f, 0.4f, 0.6f, 1f)));
        int u0 = MuUndos, toasts = MuUi.Toasts.History.Count;
        row.Pick(file);
        Check("the pick shows the toast at once", MuUi.Toasts.History.Skip(toasts).Any(t => t.Contains(L10n.Get("inspector.materials.importing"))), string.Join(" | ", MuUi.Toasts.History.Skip(toasts)));
        yield return MuFrames(5);
        var item = MuArray(v).OfType<MeTexture>().FirstOrDefault(i => i.Property == row.Property);
        Check("the image is in the store and named by the item, one entry «Material texture»", item?.Image != null && App.Session.Model.File.Res.Contains(item.Image) && MuUndos == u0 + 1 && MuTop == OpLabels.MaterialTexture, item?.Image + ", entries " + (MuUndos - u0) + ", " + MuTop);
        string doc = FieldJson.ToText(MuArray(v));
        Check("no path in the document", doc.IndexOf("mu5", StringComparison.OrdinalIgnoreCase) < 0 && doc.IndexOf(":\\", StringComparison.Ordinal) < 0, Short(doc));
        var tex = v.Selection?.Shown?.GetTexture("_" + row.Property);
        Check("the material shows the 8 x 8 image", tex && tex.width == 8 && tex.height == 8, tex ? tex.width + "x" + tex.height : "none");
        v.Refresh();
        yield return MuFrames(2);
        Check("the row names the image, and offers Remove image", row.RemoveImage.resolvedStyle.display == DisplayStyle.Flex && row.File.FileLabel.text == Path.GetFileName(item?.Image), row.File.FileLabel.text);
        Check("the row's tooltip holds no path", (row.File.FileLabel.tooltip ?? "").IndexOf("mu5", StringComparison.OrdinalIgnoreCase) < 0, row.File.FileLabel.tooltip);
        yield return MuClick(row.RemoveImage, "Remove image");
        yield return MuFrames(3);
        Check("Remove image: nothing left in the item takes the item away", MuArray(v).OfType<MeTexture>().All(i => i.Property != row.Property), Short(MuText(v)));
        string bad = Path.Combine(folder, "mu5.txt.png");
        File.WriteAllText(bad, "not an image");
        int u1 = MuUndos;
        var r = row.Import(bad);
        Check("a file that is no image is refused, no entry", !r.Ok && r.Code == "bad_args" && MuUndos == u1, r.Code);
        yield return MuUndoAll(u0);
        Directory.Delete(folder, true);
    }

    // ------------------------------------------------------------ MU6

    static IEnumerator MU6()
    {
        yield return MuPrepare();
        yield return MuOpenTab(muChara, MuClothes());
        var v = MuTab;
        if (v == null) yield break;
        string mat = MuPick(v, x => x.ValueRows.Count > 2);
        if (!Check("a material", mat != null)) yield break;
        var row = v.ValueRows.OfType<MaterialShaderRow>().First();
        string live = v.Selection.Shown.shader.name;
        var cand = MaterialShaders.All.First(c => c.Name != live && c.Name.IndexOf("AIT", StringComparison.Ordinal) >= 0);
        var list = row.Picker.Choices();
        Check("the list holds every candidate, by family", list.Count == MaterialShaders.All.Count && list.All(c => c.label.Contains(" / ")), list.Count + " / " + MaterialShaders.All.Count);
        Check("no mosaic shader in the list", list.All(c => !MaterialGuard.Shader(c.value)));
        yield return MuClick(row.Picker.Button, "the shader row");
        var box = MuQ("ref-popup");
        Check("it opens a searchable list", box != null && box.Q<TextField>("ref-search") != null);
        var search = box?.Q<TextField>("ref-search");
        if (search != null)
        {
            search.value = cand.Label;
            yield return MuFrames(3);
            var shown = box.Q<ListView>("ref-list");
            Check("the search narrows it", shown != null && shown.itemsSource.Count >= 1 && shown.itemsSource.Count < list.Count, shown?.itemsSource.Count.ToString());
            yield return Call(new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(new JObject { ["type"] = "key", ["key"] = "Escape" }), ["frames"] = 1 });
            MuQ("ref-blocker")?.RemoveFromHierarchy();
            MuQ("ref-popup")?.RemoveFromHierarchy();
        }
        int u0 = MuUndos;
        string before = MuText(v);
        var r = row.Pick(cand.Name);
        App.Runtime.ApplyNow();
        yield return MuFrames(4);
        v = MuTab;
        v.Refresh();
        yield return MuFrames(2);
        var item = MuArray(v).OfType<MeShader>().FirstOrDefault();
        Check("a pick stores the HS2Next name, one entry «Material shader»", r.Ok && item != null && item.Shader == cand.Name && MuUndos == u0 + 1 && MuTop == OpLabels.MaterialShader, r.Code + " " + item?.Shader + ", " + MuTop);
        Check("the material draws it, the row shows it", v.Selection?.Shown?.shader.name == cand.Name && v.ValueRows.OfType<MaterialShaderRow>().First().Picker.Value == cand.Name, v.Selection?.Shown?.shader.name);
        var fresh = v.ValueRows.OfType<MaterialShaderRow>().First();
        fresh.ResetItem();
        App.Runtime.ApplyNow();
        yield return MuFrames(4);
        v = MuTab;
        v.Refresh();
        yield return MuFrames(2);
        Check("↺: the shader back, the item gone", v.Selection?.Shown?.shader.name == live && MuText(v) == before, v.Selection?.Shown?.shader.name);
        var cases = new[] { "AIT/mnpb", MosaicSet.ShaderName };
        foreach (var name in cases)
        {
            int u1 = MuUndos;
            var refused = v.ValueRows.OfType<MaterialShaderRow>().First().Pick(name);
            Check("a mosaic shader is refused (" + name + ")", !refused.Ok && refused.Code == "unsupported" && MuUndos == u1 && refused.Warnings.Any(w => w.Contains("me.mosaicProtected")), refused.Code);
        }
        yield return MuUndoAll(u0);
    }

    // ------------------------------------------------------------ MU7

    static IEnumerator MU7()
    {
        yield return MuPrepare();
        yield return OkCall(new { cmd = "select", keys = new[] { muItem } }, "select the item");
        yield return OkCall(new { cmd = "ui.panel", panel = "inspector", op = "show" }, "show the inspector");
        yield return MuFrames(3);
        MuInsp.Refresh(true);
        yield return MuFrames(3);
        var fo = MuQ("section-" + InspectorSchemas.ItemMaterialEditSection) as Foldout;
        if (!Check("the section stands at the end of the item's sections", fo != null && fo.parent.IndexOf(fo) > fo.parent.IndexOf(MuQ("section-motion")))) yield break;
        Check("collapsed, nothing built", !fo.value && fo.Q<MaterialEditView>() == null, fo.value.ToString());
        fo.value = true;
        yield return MuFrames(4);
        var v = fo.Q<MaterialEditView>();
        if (!Check("opened: the view", v != null && v.Built)) yield break;
        v.Refresh();
        Check("no target selector on an item", v.Q("mat-target") == null);
        string mat = MuPick(v, x => MuFloat(x) != null || MuRow<MaterialColorRow>(x) != null);
        if (!Check("the item's material", mat != null, v.Rows.Count + " rows")) yield break;
        int u0 = MuUndos;
        var cr = v.ValueRows.OfType<MaterialColorRow>().FirstOrDefault(r => r.Property == "Color");
        var over = MuQ("mat-over-colour");
        Check("no note before an edit of a colour slot", over != null && over.resolvedStyle.display == DisplayStyle.None);
        if (cr != null)
        {
            cr.OpenPicker();
            yield return MuFrames(3);
            var hex = cr.Picker?.Q<TextField>("hex");
            if (hex != null) hex.value = "884422";
            yield return MuFrames(2);
            cr.Picker?.Close(true);
            yield return MuFrames(3);
            v.Refresh();
            yield return MuFrames(2);
            var state = App.Session.Model.Find(muItem).State as ItemNodeState;
            Check("the row writes item.materialOverrides, one entry", state?.MaterialOverrides.OfType<MeColor>().Any(i => i.Property == "Color") == true && MuUndos == u0 + 1, MuUndos - u0 + "");
            Check("the note: an edit is drawn over this colour", over.resolvedStyle.display == DisplayStyle.Flex);
        }
        else Info("the item's material has no Color row");
        var kept = v.SelectedMaterial;
        MuInsp.Refresh(true);
        yield return MuFrames(4);
        fo = MuQ("section-" + InspectorSchemas.ItemMaterialEditSection) as Foldout;
        var w = fo?.Q<MaterialEditView>();
        Check("a rebuild keeps the section open, the material selected", fo != null && fo.value && w != null && w.SelectedMaterial == kept, w?.SelectedMaterial);
        if (w != null) yield return MuClick(w.Q("mat-reset-all"), "Reset all");
        yield return MuFrames(3);
        Check("Reset all empties the array", (App.Session.Model.Find(muItem).State as ItemNodeState)?.MaterialOverrides.Count == 0);
        yield return MuUndoAll(u0);
        if (fo != null) fo.value = false;
        yield return MuFrames(2);
    }

    // ------------------------------------------------------------ MU8

    static IEnumerator MuShowScene()
    {
        yield return OkCall(new { cmd = "ui.panel", panel = "scene", op = "show" }, "show the scene panel");
        yield return MuFrames(3);
    }

    static Foldout MuMapFold => MuQ("section-" + InspectorSchemas.MapMaterialsSection) as Foldout;

    static IEnumerator MU8()
    {
        yield return MuPrepare();
        yield return MuShowScene();
        var fo = MuMapFold;
        if (!Check("the section stands in the scene panel", fo != null)) yield break;
        Check("no map: not shown", fo.resolvedStyle.display == DisplayStyle.None);
        // -mw4 <ref> -mw4LoadSec <s>: the opt-in run on the largest row, as I's MW4
        yield return LoadMap(Arg("-mw4", LargestVanilla), "MU8", double.Parse(Arg("-mw4LoadSec", "240"), System.Globalization.CultureInfo.InvariantCulture));
        yield return Settled();
        yield return MuFrames(4);
        Check("a map loaded: shown, collapsed, nothing built", fo.resolvedStyle.display == DisplayStyle.Flex && !fo.value && fo.Q<MaterialEditView>() == null);
        // a renderer alone with its material joins the mosaic set: its material is what the first show must not have to ask about
        var byName = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var x in MapTarget.Renderers)
            if (x) foreach (var m in x.sharedMaterials) if (m) { string n = MeKey.Format(m.name); byName[n] = byName.TryGetValue(n, out int c) ? c + 1 : 1; }
        var alone = MapTarget.Renderers.FirstOrDefault(x => x && x.sharedMaterials.Length == 1 && x.sharedMaterials[0] && byName[MeKey.Format(x.sharedMaterials[0].name)] == 1);
        string held = null;
        if (Check("a renderer alone with its material", alone != null) && Check("the set takes it", MosaicSet.Add(alone))) held = MeKey.Format(alone.sharedMaterials[0].name);
        fo.value = true;
        yield return MuFrames(4);
        var v = fo.Q<MaterialEditView>();
        if (!Check("opened: the view", v != null && v.Built)) yield break;
        Check("the map's list: materials by name", v.Rows.Count > 0 && v.Rows.All(r => r.Material != null), v.Rows.Count + " rows");
        MuTime("MW4 the map's section, first open with " + v.Rows.Count + " materials on " + MapTarget.Renderers.Count + " renderers", v.FirstShowMs, v.FirstShowCollections);
        Check("the first show walks no member", MapTarget.MemberMilliseconds == 0, "members " + MapTarget.MemberMilliseconds.ToString("0.0") + " ms, name index " + MapTarget.IndexMilliseconds.ToString("0.0") + " ms");
        if (held != null)
        {
            v.SelectMaterial(held);
            yield return MuFrames(3);
            Info("MW4 the first selection walks the members: " + MapTarget.MemberMilliseconds.ToString("0.0") + " ms");
            Check("a selected material a member holds: every row off with the mosaic's key, the note", v.Selection != null && v.Selection.Protected && v.ValueRows.Count > 0 && v.ValueRows.All(r => r.OffKey == "inspector.materials.mosaic" && !r.enabledSelf) && v.Q("mat-off").resolvedStyle.display == DisplayStyle.Flex, v.ValueRows.Count(r => r.OffKey == null) + " of " + v.ValueRows.Count + " on");
            Check("selecting it counted the members", MapTarget.MemberMilliseconds > 0);
            v.SelectMaterial(null);
            yield return MuFrames(2);
        }
        Check("each material with its user count", v.Rows.All(r => r.Slots > 0));
        var mode = v.Q<SegmentedButtons>("mat-mode");
        Check("the modes", mode != null && mode.Index == 0);
        string mat = null;
        foreach (var r in v.Rows.ToList())
        {
            v.SelectMaterial(r.Material);
            if (MuFloat(v) != null) { mat = r.Material; break; }
        }
        Check("a material with no member keeps its rows on", mat != null && mat != held && v.Selection != null && !v.Selection.Protected, mat);
        if (Check("a material with a float row", mat != null))
        {
            int u0 = MuUndos;
            var fr = MuFloat(v);
            double was = fr.Number.Value;
            fr.Number.Raise(EditPhase.Typed, was > 0.5 ? was - 0.2 : was + 0.2);
            yield return MuFrames(3);
            Check("a row writes scene.map.materialOverrides, one entry", MapArray.OfType<MeFloat>().Any(i => i.Material == mat && i.Property == fr.Property) && MuUndos == u0 + 1, FieldJson.ToText(MapArray));
            v.Refresh();
            v.SetEditedOnly(true);
            yield return MuFrames(2);
            Check("Edited only lists that material", v.Rows.Count == 1 && v.Rows[0].Material == mat, v.Rows.Count.ToString());
            v.SetEditedOnly(false);
            yield return MuUndoAll(u0);
            Check("undo empties the array", MapArray.Count == 0);
        }
        yield return MuClick(mode?.Buttons[1], "the mode Renderers");
        Check("Renderers: no row until a search", v.Rows.Count == 0 && v.Q("mat-search-hint").resolvedStyle.display == DisplayStyle.Flex, v.Rows.Count.ToString());
        v.SetQuery("a");
        yield return MuFrames(2);
        Check("one character: no row, the hint", v.Rows.Count == 0 && v.Q("mat-search-hint").resolvedStyle.display == DisplayStyle.Flex);
        var all = MapTarget.Renderers;
        string common = MuCommonPart(all);
        v.SetQuery(common);
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => v.NamesRead >= v.NamesTotal && v.NamesTotal > 0 && v.Rows.Count > 0, 10, ok);
        Check("two characters: names read, rows listed, at most " + MaterialEditView.ResultCap, ok[0] && v.Rows.Count <= MaterialEditView.ResultCap && v.Rows.All(r => r.IsRenderer), v.NamesRead + " / " + v.NamesTotal + " names, " + v.Rows.Count + " rows for \"" + common + "\" of " + all.Count);
        int matching = all.Count(r => r && MapTarget.PathOf(r).IndexOf(common, StringComparison.OrdinalIgnoreCase) >= 0);
        Check("more matches than the cap are cut", matching <= MaterialEditView.ResultCap ? !v.Capped : v.Capped && v.Rows.Count == MaterialEditView.ResultCap, matching + " match, capped " + v.Capped);
        Check("a renderer's path is what a `renderer` item names", v.Rows.Count > 0 && v.Rows[0].Renderer.Key == MapTarget.PathOf(v.Rows[0].Renderer.Renderer));
        v.SetQuery("");
        v.SetRenderersMode(false);
        yield return MuFrames(2);
        Check("back to the materials", v.Rows.Count > 0 && v.Rows.All(r => r.Material != null));
    }

    // The renderer path text the most renderers share, of two letters and more: what a search with many hits types.
    static string MuCommonPart(IReadOnlyList<Renderer> all)
    {
        var count = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in all.Take(2000))
        {
            string p = r ? (r.name ?? "").ToLowerInvariant() : "";
            var seen = new HashSet<string>();
            for (int i = 0; i + 2 <= p.Length; i++)
            {
                string s = p.Substring(i, 2);
                if (char.IsLetter(s[0]) && char.IsLetter(s[1]) && seen.Add(s)) count[s] = count.TryGetValue(s, out int n) ? n + 1 : 1;
            }
        }
        return count.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault() ?? "o_";
    }

    // ------------------------------------------------------------ MU9

    static IEnumerator MU9()
    {
        yield return MuPrepare();
        yield return MuOpenTab(muChara, MuClothes());
        var v = MuTab;
        if (v == null) yield break;
        MuPick(v, x => x.ValueRows.Count > 2);
        yield return MuFrames(2);
        var bad = new List<string>();
        foreach (var e in v.Query<VisualElement>().ToList())
        {
            string t = (e as TextElement)?.text;
            if (MuNames(t)) bad.Add(e.name + ": " + t);
            if (MuNames(e.tooltip)) bad.Add(e.name + " tooltip: " + e.tooltip);
        }
        Check("no text of the view names the plugin", bad.Count == 0, string.Join(" | ", bad.Take(5)));
        var literal = new Regex("\"(?:[^\"\\\\\\n]|\\\\.)*\"", RegexOptions.Compiled);
        string folder = Path.Combine(ContentSettings.ProjectRoot, "Assets", "HS2Next", "Runtime", "UI", "Panels", "Inspector", "Materials");
        int files = 0, strings = 0;
        var found = new List<string>();
        foreach (var f in Directory.GetFiles(folder, "*.cs"))
        {
            files++;
            foreach (Match m in literal.Matches(File.ReadAllText(f)))
            {
                strings++;
                if (MuNames(m.Value)) found.Add(Path.GetFileName(f) + ": " + m.Value);
            }
        }
        Check("no literal string of the view's source names it (" + files + " files, " + strings + " strings)", found.Count == 0 && files > 0, string.Join(" | ", found.Take(5)));
    }

    static bool MuNames(string t) => t != null && (t.IndexOf("MaterialEditor", StringComparison.OrdinalIgnoreCase) >= 0 || t.IndexOf("Material Editor", StringComparison.OrdinalIgnoreCase) >= 0);

    // ------------------------------------------------------------ MU10

    static IEnumerator MuEditShow(object args)
    {
        yield return OkCall(new { cmd = "ui.action", action = "edit.show", args }, "edit.show");
        yield return MuFrames(4);
    }

    static IEnumerator MU10()
    {
        yield return MuPrepare();
        yield return MuOpenTab(muChara);
        // a page of accessories needs the character's own documents
        var pick = MuTab;
        string mat = MuPick(pick, x => MuFloat(x) != null);
        if (mat != null)
        {
            var fr = MuFloat(pick);
            double was = fr.Number.Value;
            fr.Number.Raise(EditPhase.Typed, was > 0.5 ? was - 0.25 : was + 0.25);
            yield return MuFrames(2);
            fr.ResetItem();
            yield return MuFrames(3);
        }
        Check("the character holds its documents now", App.Session.Model.Find(muChara).Appearance?.Stored != null);
        MuInsp.Refresh(true);
        yield return MuFrames(3);
        yield return MuEditShow(new { node = "hair", part = "back" });
        yield return MuClick(MuQ("hair-materials-back"), "«Edit materials» of the hair part");
        Check("hair: the tab on the part", MuInsp.Chara.CurrentTab == MaterialEditView.TabId && MuTab?.TargetText == "hair:0", MuInsp.Chara.CurrentTab + " / " + MuTab?.TargetText);
        var real = App.Runtime.FindChara(muChara);
        int kind = Enumerable.Range(0, 8).First(k => real.Context.Clothes[k]);
        yield return MuEditShow(new { node = "clothes", kind = InspectorSchemas.ClothesEditKinds[kind] });
        yield return MuClick(MuQ("clothes-materials-" + InspectorSchemas.ClothesEditKinds[kind]), "«Edit materials» of the clothes kind");
        Check("clothes: the tab on the kind", MuInsp.Chara.CurrentTab == MaterialEditView.TabId && MuTab?.TargetText == "clothes:" + kind, MuInsp.Chara.CurrentTab + " / " + MuTab?.TargetText);
        var slot = real.Context.Accessories.FirstOrDefault(s => s != null && s.Object);
        if (slot == null)
        {
            yield return OkCall(new { cmd = "chara.edit.accessory", key = muChara, op = "add", category = 350 + 1, item = "0" }, "add an accessory");
            MuUi.Runtime.ApplyNow();
            yield return MuFrames(4);
            slot = App.Runtime.FindChara(muChara).Context.Accessories.FirstOrDefault(s => s != null && s.Object);
        }
        if (Check("an accessory to open", slot != null))
        {
            yield return MuEditShow(new { node = "accessories", slot = slot.Slot });
            string id = App.Runtime.FindChara(muChara).AccessoryIdOf(slot);
            yield return MuClick(MuQ("acc-materials-" + InspectorSchemas.AccessorySlotLabel(slot.Slot)), "«Edit materials» of the accessory");
            Check("accessory: the tab on the slot", MuInsp.Chara.CurrentTab == MaterialEditView.TabId && MuTab?.TargetText == "acc:" + id, MuInsp.Chara.CurrentTab + " / " + MuTab?.TargetText);
        }
        MuTab?.SetTarget("character");
        yield return MuFrames(2);
    }

    // ------------------------------------------------------------ MU11

    static IEnumerator MU11()
    {
        yield return MuPrepare();
        yield return MuOpenTab(muChara);
        var left = MuUi.Shell.Regions[LayoutRegions.Left];
        var host = UiHost.Current;
        foreach (float width in new[] { 360f, 300f })
        {
            MuUi.Shell.SetSize(left, width);
            yield return MuFrames(4);
            foreach (var lang in new[] { "en", "ja", "zh-Hans" })
            {
                host.SetLanguage(lang, save: false);
                yield return MuFrames(4);
                MuInsp.Refresh(true);
                yield return MuFrames(4);
                string tag = width.ToString("0") + " px " + lang;
                yield return Call(new { cmd = "ui.query", name = "tabs" });
                var strip = last["elements"]?[0];
                var sb = strip?["worldBound"]?.ToObject<float[]>();
                yield return Call(new { cmd = "ui.query", name = ".hs-inspector__tab", all = true });
                var tabs = (last["elements"] as JArray)?.OfType<JObject>().Where(e => ((string)e["name"]).StartsWith("tab-", StringComparison.Ordinal)).ToList();
                if (!Check(tag + ": the strip and its tabs", sb != null && tabs != null && tabs.Count >= 13, tabs?.Count.ToString())) continue;
                var bounds = tabs.Select(e => e["worldBound"].ToObject<float[]>()).ToList();
                var lines = bounds.Select(b => Mathf.Round(b[1])).Distinct().OrderBy(y => y).ToList();
                var outside = new List<string>();
                var notShown = new List<string>();
                var cut = new List<string>();
                for (int i = 0; i < tabs.Count; i++)
                {
                    var b = bounds[i];
                    string name = (string)tabs[i]["name"];
                    if (b[2] < 1 || b[0] < sb[0] - 0.5f || b[0] + b[2] > sb[0] + sb[2] + 0.5f || b[1] < sb[1] - 0.5f || b[1] + b[3] > sb[1] + sb[3] + 0.5f) outside.Add(name);
                    if (tabs[i].Value<bool>("visible") != true) notShown.Add(name);
                    var el = MuQ(name) as Button;
                    if (el != null)
                    {
                        var need = el.MeasureTextSize(el.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                        float room = b[2] - el.resolvedStyle.paddingLeft - el.resolvedStyle.paddingRight - el.resolvedStyle.borderLeftWidth - el.resolvedStyle.borderRightWidth;
                        if (need.x > room + 0.5f) cut.Add(name + " " + need.x.ToString("0.#") + " > " + room.ToString("0.#"));
                    }
                }
                float strip0 = sb[2];
                Info($"MU11 {tag}: strip {strip0:0} x {sb[3]:0}, {tabs.Count} tabs on {lines.Count} lines, Materials tab {bounds[tabs.FindIndex(e => (string)e["name"] == "tab-materials")][2]:0.#} px wide");
                Check(tag + ": every tab inside the strip, visible", outside.Count == 0 && notShown.Count == 0, string.Join(", ", outside.Concat(notShown)));
                Check(tag + ": no tab's label cut", cut.Count == 0, string.Join(", ", cut));
                var mat = MuQ("tab-materials");
                if (mat != null)
                {
                    yield return MuClick(mat, tag + ": the Materials tab");
                    Check(tag + ": a click reaches the tab", MuInsp.Chara.CurrentTab == MaterialEditView.TabId, MuInsp.Chara.CurrentTab);
                }
            }
        }
        host.SetLanguage("en", save: false);
        MuUi.Shell.SetSize(left, 360f);
        yield return MuFrames(4);
    }

    // ------------------------------------------------------------ MW3

    static void MuTime(string what, double ms, int gcs)
    {
        if (ms > ShowBarMs && gcs > 0) Warn(what, ms.ToString("0.0") + " ms with " + gcs + " collections in the window");
        else Check(what + ": " + ms.ToString("0.0") + " ms (bar " + ShowBarMs + ")", ms >= 0 && ms <= ShowBarMs);
    }

    static IEnumerator MW3()
    {
        yield return MuPrepare();
        yield return OkCall(new { cmd = "select", keys = new[] { muChara } }, "select the character");
        yield return OkCall(new { cmd = "ui.panel", panel = "inspector", op = "show" }, "show the inspector");
        var real = App.Runtime.FindChara(muChara);
        var rends = MaterialEditorApplier.ObjectRenderers(real.Context, (HS2Next.Data.Ext.MaterialEditorData.ObjectType.Character, 0));
        Info("Character renderers: " + (rends?.Count ?? 0));
        var runs = new List<double>();
        for (int i = 0; i < 5; i++)
        {
            CharaInspector.LastTab = "anime";
            MuInsp.Refresh(true);
            yield return MuFrames(3);
            var v = MuTab;
            v.SetTarget("character");
            MuInsp.Chara.ShowTab(MaterialEditView.TabId);
            yield return MuFrames(3);
            runs.Add(v.FirstShowMs);
            if (i == 0) MuTime("MW3 the Materials tab, first show of the session", v.FirstShowMs, v.FirstShowCollections);
            else if (i == 4) MuTime("MW3 the Materials tab, first show of a fresh view", v.FirstShowMs, v.FirstShowCollections);
        }
        Info("MW3 tab first shows (ms): " + string.Join(", ", runs.Select(x => x.ToString("0.0"))));
        yield return OkCall(new { cmd = "select", keys = new[] { muItem } }, "select the item");
        yield return MuFrames(3);
        MuInsp.Refresh(true);
        yield return MuFrames(3);
        var fo = MuQ("section-" + InspectorSchemas.ItemMaterialEditSection) as Foldout;
        if (fo != null)
        {
            fo.value = false;
            yield return MuFrames(1);
            MuInsp.Refresh(true);
            yield return MuFrames(3);
            fo = MuQ("section-" + InspectorSchemas.ItemMaterialEditSection) as Foldout;
            fo.value = true;
            yield return MuFrames(3);
            var v = fo.Q<MaterialEditView>();
            if (Check("the item's view", v != null)) MuTime("MW3 the item's section, first open", v.FirstShowMs, v.FirstShowCollections);
            fo.value = false;
        }
        yield return OkCall(new { cmd = "select", keys = new[] { muChara } }, "select the character");
        yield return MuFrames(3);
    }

    // ------------------------------------------------------------ ML7

    static void MuRaw(EventType et, Vector2 p, Vector2 delta = default)
    {
        var se = new Event { type = et, mousePosition = p, delta = delta, button = 0, clickCount = 1, modifiers = EventModifiers.None };
        var tree = MuTree;
        if (et == EventType.MouseDown) using (var e = PointerDownEvent.GetPooled(se)) tree.SendEvent(e);
        else if (et == EventType.MouseUp) using (var e = PointerUpEvent.GetPooled(se)) tree.SendEvent(e);
        else using (var e = PointerMoveEvent.GetPooled(se)) tree.SendEvent(e);
    }

    static void MuEsc() => UiCommands.Send(UiHost.Current, JObject.FromObject(new { type = "key", key = "Escape" }), out _);

    static VisualElement MuCapturing()
    {
        for (int id = 0; id < PointerId.maxPointers; id++) if (MuPanel.GetCapturingElement(id) is VisualElement v) return v;
        return null;
    }

    static string MuName(VisualElement v) => v == null ? "none" : (v.name ?? "") + " (" + v.GetType().Name + ")";
    static bool MuOpenEdit => MuUi.ActiveEdit != null && MuUi.ActiveEdit.IsOpen;

    /// <summary>The array and the values as one text: what must come back.</summary>
    delegate string MuRead();

    static IEnumerator MuEscNumber(string what, NumberSlider ns, bool onSlider, MuRead read, MuRead array, string entry)
    {
        VisualElement grip = onSlider ? (ns.Slider?.Q("unity-drag-container") ?? (VisualElement)ns.Slider) : ns.LabelElement;
        if (!Check(what + ": the element to press", grip != null)) yield break;
        yield return MuReveal(grip);
        Vector2 at = grip.worldBound.center;
        var picked = MuPanel.Pick(at) as VisualElement;
        if (!Check(what + ": it is under the pointer", picked != null && (picked == grip || grip.Contains(picked)), MuName(picked) + " at " + at)) yield break;
        string v0 = read(), a0 = array();
        int u0 = MuUndos;
        MuRaw(EventType.MouseDown, at);
        for (int i = 1; i <= 3; i++) { MuRaw(EventType.MouseDrag, at + new Vector2(i * 9, 0), new Vector2(9, 0)); yield return MuFrames(1); }
        Check(what + ": the drag runs and writes", ns.Dragging && MuCapturing() != null && array() != a0, ns.Dragging + ", " + MuName(MuCapturing()));
        MuEsc();
        Check(what + " Esc: no pointer held, no drag, no open edit", MuCapturing() == null && !ns.Dragging && !MuOpenEdit, MuName(MuCapturing()) + ", dragging " + ns.Dragging);
        Check(what + " Esc: the array and the values as at the press, no entry", array() == a0 && read() == v0 && MuUndos == u0, Short(read()) + " / " + Short(v0) + ", entries " + (MuUndos - u0));
        yield return MuFrames(3);
        MuRaw(EventType.MouseDrag, at + new Vector2(30, 0), new Vector2(3, 0));
        MuRaw(EventType.MouseDrag, at + new Vector2(40, 0), new Vector2(10, 0));
        yield return MuFrames(1);
        Check(what + " moves after Esc: nothing", array() == a0 && read() == v0 && MuUndos == u0, Short(array()));
        MuRaw(EventType.MouseUp, at + new Vector2(40, 0));
        yield return MuFrames(2);
        Check(what + " the release after Esc: nothing", array() == a0 && read() == v0 && MuUndos == u0 && !ns.Dragging, Short(array()));
        MuRaw(EventType.MouseDown, at);
        for (int i = 1; i <= 2; i++) { MuRaw(EventType.MouseDrag, at + new Vector2(i * 9, 0), new Vector2(9, 0)); yield return MuFrames(1); }
        MuRaw(EventType.MouseUp, at + new Vector2(18, 0));
        yield return MuFrames(3);
        Check(what + " a new drag afterwards: one entry", MuUndos == u0 + 1 && array() != a0 && MuTop == entry, (MuUndos - u0) + " / " + MuTop);
        yield return MuUndoAll(u0);
        Check(what + " undo: as at the press", array() == a0 && read() == v0, Short(array()));
    }

    static IEnumerator MuEscVector(string what, VectorField vf, int c, MuRead read, MuRead array, string entry)
    {
        var f = vf.Fields[c];
        var label = f.labelElement;
        yield return MuReveal(label);
        Vector2 at = label.worldBound.center;
        if (!Check(what + ": the axis label is under the pointer", MuPanel.Pick(at) == label, MuName(MuPanel.Pick(at) as VisualElement) + " at " + at)) yield break;
        string v0 = read(), a0 = array();
        int u0 = MuUndos;
        MuRaw(EventType.MouseDown, at);
        Check(what + ": the press: the label holds the pointer, an edit is open", MuCapturing() == label && vf.Dragging && MuOpenEdit, MuName(MuCapturing()));
        for (int i = 1; i <= 3; i++) { MuRaw(EventType.MouseDrag, at + new Vector2(i, 0), new Vector2(6, 0)); yield return MuFrames(1); }
        Check(what + ": the drag writes", array() != a0, Short(array()));
        MuEsc();
        Check(what + " Esc: no pointer held, no drag, no open edit", MuCapturing() == null && !vf.Dragging && !MuOpenEdit, MuName(MuCapturing()) + ", dragging " + vf.Dragging);
        Check(what + " Esc: the array and the values as at the press, no entry", array() == a0 && read() == v0 && MuUndos == u0, Short(array()) + ", entries " + (MuUndos - u0));
        yield return MuFrames(3);
        string text0 = f.text;
        MuRaw(EventType.MouseDrag, at + new Vector2(1, 0), new Vector2(4, 0));
        MuRaw(EventType.MouseDrag, at + new Vector2(300, 200), new Vector2(4, 0));
        MuRaw(EventType.MouseDrag, at, new Vector2(4, 0));
        yield return MuFrames(1);
        Check(what + " moves after Esc: nothing", array() == a0 && MuUndos == u0 && f.text == text0, Short(array()) + ", text " + f.text + " / " + text0);
        MuRaw(EventType.MouseUp, at);
        yield return MuFrames(1);
        f.Focus();
        yield return MuFrames(1);
        f.Blur();
        yield return MuFrames(2);
        Check(what + " the release and a later blur: nothing", array() == a0 && read() == v0 && MuUndos == u0, Short(array()) + ", entries " + (MuUndos - u0));
        MuRaw(EventType.MouseDown, at);
        for (int i = 1; i <= 2; i++) { MuRaw(EventType.MouseDrag, at + new Vector2(i, 0), new Vector2(6, 0)); yield return MuFrames(1); }
        MuRaw(EventType.MouseUp, at);
        yield return MuFrames(2);
        f.Blur();
        yield return MuFrames(1);
        Check(what + " a new drag afterwards: one entry", MuUndos == u0 + 1 && array() != a0 && MuTop == entry, (MuUndos - u0) + " / " + MuTop);
        yield return MuUndoAll(u0);
        Check(what + " undo: as at the press", array() == a0 && read() == v0, Short(array()));
    }

    static IEnumerator MuEscPicker(string what, MaterialColorRow row, MuRead read, MuRead array)
    {
        yield return MuReveal(row);
        string v0 = read(), a0 = array();
        int u0 = MuUndos;
        Check(what + ": the picker opens", row.OpenPicker());
        yield return MuFrames(4);
        var sv = row.Picker?.Q(className: "hs-colorpicker__sv");
        if (!Check(what + ": the picker's square", sv != null)) yield break;
        Vector2 at = sv.worldBound.center;
        MuRaw(EventType.MouseDown, at);
        MuRaw(EventType.MouseDrag, at + new Vector2(12, 8), new Vector2(12, 8));
        yield return MuFrames(1);
        Check(what + ": the square's drag writes", array() != a0 && MuCapturing() == sv, Short(array()) + ", " + MuName(MuCapturing()));
        MuEsc();
        Check(what + " Esc: closed, the array and the value back, no entry", row.Picker == null && array() == a0 && read() == v0 && MuUndos == u0, (row.Picker == null) + ", " + Short(array()) + ", entries " + (MuUndos - u0));
        MuRaw(EventType.MouseDrag, at + new Vector2(20, 10), new Vector2(8, 2));
        MuRaw(EventType.MouseUp, at + new Vector2(20, 10));
        yield return MuFrames(2);
        Check(what + ": moves and the release after Esc: nothing", array() == a0 && read() == v0 && MuUndos == u0, Short(array()));
        row.Picker?.Close(false);
        yield return MuFrames(2);
    }

    // Slider, label, vector row and picker on the view's selected material.
    static IEnumerator MuEscRows(string where, MaterialEditView v)
    {
        var fr = MuFloat(v);
        MuRead array = () => MuText(v);
        if (Check(where + ": a float row", fr != null))
        {
            MuRead value = () => { MaterialBaseline.TryRead(v.Selection.Shown, MaterialMember.Float, "_" + fr.Property, out var x); return x.Float.ToString("R"); };
            yield return MuEscNumber(where + ", slider", fr.Number, true, value, array, OpLabels.MaterialEdit);
            yield return MuEscNumber(where + ", label", fr.Number, false, value, array, OpLabels.MaterialEdit);
        }
        var tr = MuTexture(v);
        if (tr != null)
        {
            MuRead value = () => { MaterialBaseline.TryRead(v.Selection.Shown, MaterialMember.Offset, "_" + tr.Property, out var x); return x.Vector2.ToString("R"); };
            yield return MuEscVector(where + ", vector row", tr.Offset, 0, value, array, OpLabels.MaterialEdit);
        }
        else Info(where + ": no texture row on this material");
        var cr = MuRow<MaterialColorRow>(v);
        if (cr != null)
        {
            MuRead value = () => { MaterialBaseline.TryRead(v.Selection.Shown, MaterialMember.Color, "_" + cr.Property, out var x); return x.Color.ToString("R"); };
            yield return MuEscPicker(where + ", colour picker", cr, value, array);
        }
        else Info(where + ": no colour row on this material");
    }

    // A character's array holds what the URP mapping reads, so a queue drag across 2500 and an emission drag from black reload the part under the pointer.
    static IEnumerator MuReloadDrag(string what, NumberSlider ns, MuRead read, MuRead array)
    {
        VisualElement grip = ns.Slider?.Q("unity-drag-container") ?? (VisualElement)ns.Slider;
        if (!Check(what + ": the slider", grip != null)) yield break;
        yield return MuReveal(grip);
        Vector2 at = grip.worldBound.center + new Vector2(-grip.worldBound.width * 0.2f, 0);
        var real = App.Runtime.FindChara(muChara);
        string v0 = read(), a0 = array();
        int u0 = MuUndos, steps = 0, swaps = 0, r0 = real.EditRebuilds;
        var seen = new HashSet<string>();
        MuRaw(EventType.MouseDown, at);
        float x = 0, step = grip.worldBound.width / 24f;
        while (steps < 18)
        {
            x += step;
            MuRaw(EventType.MouseDrag, at + new Vector2(x, 0), new Vector2(step, 0));
            yield return MuFrames(1);
            steps++;
            seen.Add(array());
            var c = App.Runtime.FindChara(muChara);
            if (c?.LastEditFlush != null && c.LastEditFlush.AnySwap) swaps++;
        }
        Info($"{what}: {steps} steps, {seen.Count} different arrays, {swaps} flushes with a reload, {App.Runtime.FindChara(muChara).EditRebuilds - r0} rebuilds; {v0} -> {read()}");
        Check(what + ": a reload happened under the drag", swaps > 0, swaps + " flushes");
        Check(what + ": the row is alive and holds the pointer", ns.panel != null && ns.Dragging && MuCapturing() != null, ns.Dragging + ", " + MuName(MuCapturing()));
        Check(what + ": the drag went on after the reload", seen.Count >= 8, seen.Count + " arrays");
        MuRaw(EventType.MouseUp, at + new Vector2(x, 0));
        yield return MuFrames(3);
        Check(what + ": one entry", MuUndos == u0 + 1 && MuTop == OpLabels.MaterialEdit, (MuUndos - u0) + " / " + MuTop);
        yield return MuUndoAll(u0);
        yield return MuFrames(3);
        Check(what + " undo: as at the press", array() == a0 && read() == v0, Short(read()) + " / " + Short(v0));
    }

    static IEnumerator ML12()
    {
        yield return MuPrepare();
        yield return MuOpenTab(muChara, MuClothes());
        var v = MuTab;
        string mat = v != null ? MuPick(v, x => MuRow<MaterialQueueRow>(x) != null) : null;
        if (!Check("ML12 a clothes material with a queue row", mat != null)) yield break;
        v.Refresh();
        var row = MuRow<MaterialQueueRow>(v);
        var obj = (HS2Next.Data.Ext.MaterialEditorData.ObjectType.Clothing, v.Target.Address.Slot);
        MuRead state = () =>
        {
            var m = HS2Next.Chara.Ext.MaterialEditorApplier.MaterialsNamed(App.Runtime.FindChara(muChara).Context, obj, mat).FirstOrDefault();
            return m ? m.renderQueue + " " + m.IsKeywordEnabled(ShaderMap.ReceiveShadowsOff) : "none";
        };
        MuRead array = () => MuText(v);
        yield return MuReloadDrag("ML12 queue", row.Number, state, array);
        v = MuTab;
        v.Refresh();
        row = MuRow<MaterialQueueRow>(v);
        if (Check("ML12 the row after the drag", row != null)) yield return MuEscNumber("ML12 queue, Esc", row.Number, true, state, array, OpLabels.MaterialEdit);

        // an emission colour from black
        v = MuTab;
        Func<MaterialEditView, MaterialColorRow> black = x => x.ValueRows.OfType<MaterialColorRow>().FirstOrDefault(r => r.OffKey == null && r.Property == "EmissionColor" && r.Swatch.Value.maxColorComponent == 0f);
        string emat = MuPick(v, x => black(x) != null);
        if (emat == null) { Info("ML12 no clothes material with a black emission colour row: that drag is not covered here"); yield break; }
        v.Refresh();
        var cr = black(v);
        var eobj = obj;
        MuRead colour = () =>
        {
            var m = HS2Next.Chara.Ext.MaterialEditorApplier.MaterialsNamed(App.Runtime.FindChara(muChara).Context, eobj, emat).FirstOrDefault();
            return m ? m.GetColor("_EmissionColor").ToString("R") : "none";
        };
        yield return MuEscPicker("ML12 emission", cr, colour, array);
        v = MuTab;
        v.Refresh();
        cr = black(v);
        if (!Check("ML12 the emission row after Esc", cr != null)) yield break;
        string a0 = array();
        int u0 = MuUndos, swaps = 0;
        Check("ML12 emission: the picker opens", cr.OpenPicker());
        yield return MuFrames(4);
        var sv = cr.Picker?.Q(className: "hs-colorpicker__sv");
        if (!Check("ML12 emission: the picker's square", sv != null)) yield break;
        Vector2 at = sv.worldBound.center;
        MuRaw(EventType.MouseDown, at);
        for (int i = 1; i <= 8; i++)
        {
            MuRaw(EventType.MouseDrag, at + new Vector2(i * 3, -i * 2), new Vector2(3, -2));
            yield return MuFrames(1);
            var c = App.Runtime.FindChara(muChara);
            if (c?.LastEditFlush != null && c.LastEditFlush.AnySwap) swaps++;
        }
        Check("ML12 emission: a reload happened under the drag", swaps > 0, swaps + " flushes");
        Check("ML12 emission: the picker is alive and holds the pointer", cr.Picker != null && MuCapturing() == sv, MuName(MuCapturing()));
        string during = array();
        MuRaw(EventType.MouseUp, at + new Vector2(24, -16));
        yield return MuFrames(2);
        cr.Picker?.Close(true);
        yield return MuFrames(3);
        Check("ML12 emission: one entry, the array written", MuUndos == u0 + 1 && array() != a0 && array() == during, (MuUndos - u0) + " entries");
        yield return MuUndoAll(u0);
        yield return MuFrames(3);
        Check("ML12 emission undo: as at the press", array() == a0, Short(array()));
    }

    static IEnumerator ML7()
    {
        yield return MuPrepare();
        yield return MuOpenTab(muChara, MuClothes());
        var v = MuTab;
        if (v != null && Check("a material on the character", MuPick(v, x => MuFloat(x) != null && MuTexture(x) != null) != null || MuPick(v, x => MuFloat(x) != null) != null))
        {
            v.Refresh();
            yield return MuEscRows("character", v);
        }

        // a drag that ends where it began, and a drag on a new item: no entry and an entry
        v = MuTab;
        var fr = v != null ? MuFloat(v) : null;
        if (Check("a float row for a drag back", fr != null))
        {
            string a0 = MuText(v);
            int u0 = MuUndos;
            double was = fr.Number.Value;
            fr.Number.Begin();
            fr.Number.Raise(EditPhase.Preview, Math.Min(fr.Number.Max.Value, was + 0.1));
            yield return MuFrames(2);
            Check("a drag writes a new item", MuText(v) != a0);
            fr.Number.Raise(EditPhase.Preview, was);
            yield return MuFrames(2);
            fr.Number.End();
            yield return MuFrames(2);
            Check("a drag that ends where it began: the array as at the press, no entry", MuText(v) == a0 && MuUndos == u0, Short(MuText(v)) + ", entries " + (MuUndos - u0));
        }

        // the item
        yield return OkCall(new { cmd = "select", keys = new[] { muItem } }, "select the item");
        yield return MuFrames(3);
        MuInsp.Refresh(true);
        yield return MuFrames(3);
        var fo = MuQ("section-" + InspectorSchemas.ItemMaterialEditSection) as Foldout;
        if (fo != null)
        {
            fo.value = true;
            yield return MuFrames(4);
            var iv = fo.Q<MaterialEditView>();
            if (iv != null && Check("a material on the item", MuPick(iv, x => MuFloat(x) != null) != null))
            {
                iv.Refresh();
                yield return MuEscRows("item", iv);
            }
            fo.value = false;
            yield return MuFrames(2);
        }

        // the map
        yield return MuShowScene();
        if (MapPart.State != MapState.Loaded)
        {
            yield return LoadMap(LargestVanilla, "ML7");
            yield return Settled();
        }
        var mf = MuMapFold;
        if (mf != null)
        {
            mf.value = true;
            yield return MuFrames(4);
            var mv = mf.Q<MaterialEditView>();
            if (mv != null)
            {
                mv.SetRenderersMode(false);
                mv.SetQuery("");
                yield return MuFrames(2);
                string found = null;
                foreach (var r in mv.Rows.ToList())
                {
                    mv.SelectMaterial(r.Material);
                    if (MuFloat(mv) != null) { found = r.Material; break; }
                }
                if (Check("a material on the map", found != null))
                {
                    mv.Refresh();
                    yield return MuEscRows("map", mv);
                }
            }
            mf.value = false;
            yield return MuFrames(2);
        }

        // the hair bone rows
        yield return OkCall(new { cmd = "ui.panel", panel = "inspector", op = "show" }, "show the inspector");
        yield return OkCall(new { cmd = "select", keys = new[] { muChara } }, "select the character");
        yield return MuFrames(3);
        MuInsp.Refresh(true);
        yield return MuFrames(3);
        yield return MuEditShow(new { node = "hair", section = InspectorSchemas.HairBoneModsSection });
        var hb = (MuQ("edit-section-" + InspectorSchemas.HairBoneModsSection) as Foldout)?.Q<HairBoneSection>();
        if (Check("the section of the hair bones", hb != null && hb.Built))
        {
            hb.Refresh();
            var real = App.Runtime.FindChara(muChara);
            string bone = HairBones.RootName;
            hb.AddBone(bone);
            yield return MuFrames(3);
            var row = hb.Rows.FirstOrDefault(r => r.Bone == bone);
            if (Check("a row for the hair root", row != null))
            {
                var parts = new VisualElement[] { row.Scale, row.Length, row.Position, row.Rotation };
                bool stacked = true;
                for (int i = 1; i < parts.Length; i++) if (parts[i].worldBound.yMin < parts[i - 1].worldBound.yMax - 1) stacked = false;
                float width = hb.worldBound.width;
                Check("the bone's rows stand one under the other, at the section's width", stacked && parts.All(x => x.worldBound.width >= width * 0.9f), string.Join(", ", parts.Select(x => x.worldBound.ToString())) + " in " + width);
                MuRead array = () => FieldJson.ToText(MuUi.Ops.BoneModArray(muChara));
                MuRead none = () => "";
                yield return MuEscNumber("hair bone, length, slider", row.Length, true, none, array, OpLabels.HairBoneAdjust);
                yield return MuEscNumber("hair bone, length, label", row.Length, false, none, array, OpLabels.HairBoneAdjust);
                yield return MuEscVector("hair bone, scale", row.Scale, 1, none, array, OpLabels.HairBoneAdjust);
                yield return MuEscVector("hair bone, position", row.Position, 0, none, array, OpLabels.HairBoneAdjust);
                yield return MuEscVector("hair bone, rotation", row.Rotation, 2, none, array, OpLabels.HairBoneAdjust);
                string a0 = array();
                int u0 = MuUndos;
                row.Length.Begin();
                row.Length.Raise(EditPhase.Preview, 1.3);
                yield return MuFrames(2);
                Check("a hair drag writes", array() != a0);
                row.Length.Raise(EditPhase.Preview, 1.0);
                yield return MuFrames(2);
                row.Length.End();
                yield return MuFrames(2);
                Check("a hair drag that ends where it began: no entry, the array as at the press", array() == a0 && MuUndos == u0, Short(array()) + ", entries " + (MuUndos - u0));
            }
        }
    }

    // ------------------------------------------------------------ MO7

    static IEnumerator MO7()
    {
        yield return MuPrepare();
        yield return MuOpenTab(muMale);
        var v = MuTab;
        if (v == null) yield break;
        var member = v.Rows.FirstOrDefault(r => r.IsRenderer && r.Renderer.Member);
        if (member == null)
        {
            yield return MuOpenTab(muChara);
            v = MuTab;
            member = v?.Rows.FirstOrDefault(r => r.IsRenderer && r.Renderer.Member);
        }
        if (!Check("a member among the Character renderers", member != null, v?.Rows.Count + " rows")) yield break;
        Info("member: " + member.Renderer.Key);
        v.List.ScrollToItem(v.Rows.ToList().IndexOf(member));
        yield return MuFrames(3);
        var line = MuTree.Query<MaterialListLine>().ToList().FirstOrDefault(l => l.Row == member);
        if (Check("its line", line != null))
        {
            Check("the toggle of a member is off, with the mosaic's tooltip", !line.Check.enabledSelf && line.Check.tooltip == L10n.Get("inspector.materials.mosaic"), line.Check.tooltip);
            yield return Call(new { cmd = "ui.query", name = "mat-renderer-" + member.Renderer.Key });
        }
        v.Reveal(member.Renderer.Key);
        yield return MuFrames(2);
        var mat = v.Rows.FirstOrDefault(r => r.Material != null && r.Renderer == v.Rows.First(x => x.IsRenderer && x.Renderer.Key == member.Renderer.Key).Renderer);
        if (!Check("a material of the member", mat != null)) yield break;
        v.SelectMaterial(mat.Material);
        yield return MuFrames(3);
        bool only = v.Selection != null && v.Selection.Protected;
        Info("material " + mat.Material + ": only members hold it: " + only);
        if (Check("only a member holds it", only))
        {
            Check("every row is off with the mosaic's tooltip key", v.ValueRows.Count > 0 && v.ValueRows.All(r => r.OffKey == "inspector.materials.mosaic" && !r.enabledSelf), v.ValueRows.Count(r => r.OffKey == null) + " of " + v.ValueRows.Count + " on");
            var note = v.Q("mat-off");
            Check("the note stands", note != null && note.resolvedStyle.display == DisplayStyle.Flex);
            yield return Call(new { cmd = "ui.query", name = "mat-shader" });
            Check("ui.query: the shader row is off", LastOk && last["elements"]?[0]?.Value<bool>("enabled") == false && (string)last["elements"]?[0]?["tooltip"] == L10n.Get("inspector.materials.mosaic"), Short(lastText));
            var stale = v.ValueRows.OfType<MaterialFloatRow>().FirstOrDefault();
            int u0 = MuUndos;
            string a0 = MuText(v);
            if (stale != null)
            {
                stale.Number.Raise(EditPhase.Typed, stale.Number.Value + 0.1);
                yield return MuFrames(2);
                Check("a stale write goes to the op and is refused", stale.LastResult != null && !stale.LastResult.Ok && stale.LastResult.Code == "unsupported" && stale.LastResult.Warnings.Any(w => w.Contains("me.mosaicProtected")) && MuUndos == u0 && MuText(v) == a0, stale.LastResult?.Code);
            }
            var r = v.SetRendererEnabled(member.Renderer, !member.Renderer.Renderer.enabled);
            Check("the Enabled of a member is refused", !r.Ok && r.Code == "unsupported" && MuUndos == u0, r.Code);
        }
    }

    // ------------------------------------------------------------ MJU

    static IEnumerator MJU()
    {
        yield return MuPrepare();
        yield return MuOpenTab(muChara);
        var pick = MuTab;
        if (MuPick(pick, x => MuFloat(x) != null) != null)
        {
            var fr = MuFloat(pick);
            double was = fr.Number.Value;
            fr.Number.Raise(EditPhase.Typed, was > 0.5 ? was - 0.25 : was + 0.25);
            yield return MuFrames(2);
            fr.ResetItem();
            yield return MuFrames(3);
        }
        MuInsp.Refresh(true);
        yield return MuFrames(3);
        yield return MuEditShow(new { node = "hair", part = "back" });
        var fold = MuQ("edit-section-" + InspectorSchemas.HairBoneModsSection) as Foldout;
        if (!Check("the section stands after the part's group", fold != null && fold.parent.IndexOf(fold) > fold.parent.IndexOf(MuQ("hair-part-host")))) yield break;
        if (fold.Q<HairBoneSection>() == null) Check("collapsed, nothing built", !fold.value);
        fold.value = true;
        yield return MuFrames(4);
        var hb = fold.Q<HairBoneSection>();
        if (!Check("opened: the section", hb != null && hb.Built)) yield break;
        var real = App.Runtime.FindChara(muChara);
        var cc = real.Context;
        var names = HairBones.Names(cc);
        var choices = hb.AddPicker.Choices();
        Check("Add bone lists the hair class and nothing else", choices.Count > 0 && choices.All(c => names.Contains(c.value)), choices.Count + " of " + names.Count);
        int u0 = MuUndos;
        string bone = choices.Last().value;
        var other = new BodyBoneMod { Bone = "cf_J_Kosi01", Location = BodyBoneMod.LocationBodyTop, Scale = new Vec3(1.01f, 1f, 1f) };
        string accBone = HairBones.AccessoryBones(cc).Keys.FirstOrDefault(n => !names.Contains(n) && !AbmxApplier.Find(AbmxApplier.BodyDictionary(cc), n));
        var list = new List<BodyBoneMod> { other, new BodyBoneMod { Bone = "no_such_bone", Location = BodyBoneMod.LocationBodyTop, Scale = new Vec3(2f, 2f, 2f) } };
        if (accBone != null) list.Add(new BodyBoneMod { Bone = accBone, Location = BodyBoneMod.LocationUnknown, Length = 1.1f });
        var r = MuUi.Ops.EditAppearance(new[] { muChara }, new (string, object)[] { (BoneModEdits.Path, list) }, UiContext.User("test"), allowRebuild: true);
        Check("entries of a hand-made file stand", r.Ok, r.Code + " " + r.Message);
        MuUi.Runtime.ApplyNow();
        yield return MuFrames(4);
        hb.Refresh();
        yield return MuFrames(2);
        Check("an entry of another class has no row", hb.Rows.All(x => x.Bone != other.Bone));
        Check("the entry found nowhere is listed, with Remove", hb.Stale.Shown.Contains("no_such_bone") && MuQ("hair-bones-stale-remove-0") != null, string.Join(", ", hb.Stale.Shown));
        Check("an entry acting on an accessory is not listed as found nowhere", accBone == null || !hb.Stale.Shown.Contains(accBone), accBone ?? "no accessory bone here");
        Check("the list holds nothing else", hb.Stale.Shown.Count == 1, string.Join(", ", hb.Stale.Shown));
        Info("MJU accessory bone tried: " + (accBone != null));
        string Others() => FieldJson.ToText(MuUi.Ops.BoneModArray(muChara).Where(m => m.Bone == other.Bone || m.Bone == accBone).ToList());
        string kept = Others();
        hb.AddBone(bone);
        yield return MuFrames(3);
        var row = hb.Rows.FirstOrDefault(x => x.Bone == bone);
        if (Check("Add bone makes a row before there is an entry", row != null && MuUi.Ops.BoneModArray(muChara).All(m => m.Bone != bone)))
        {
            int u1 = MuUndos;
            yield return MuReveal(row);
            row.Scale.Fields[0].value = 1.2f;
            yield return MuFrames(3);
            var e = MuUi.Ops.BoneModArray(muChara).FirstOrDefault(m => m.Bone == bone);
            Check("a typed value stores the entry, bodyTop, one entry «Hair bone adjustment»", e != null && Mathf.Abs(e.Scale.X - 1.2f) < 1e-5f && e.Location == BodyBoneMod.LocationBodyTop && MuUndos == u1 + 1 && MuTop == OpLabels.HairBoneAdjust, e?.Scale.X + ", " + MuTop);
            row.Length.Raise(EditPhase.Typed, 1.25);
            yield return MuFrames(3);
            e = MuUi.Ops.BoneModArray(muChara).FirstOrDefault(m => m.Bone == bone);
            Check("Length writes the same entry; typed values chain", e != null && Mathf.Abs(e.Length - 1.25f) < 1e-5f && Mathf.Abs(e.Scale.X - 1.2f) < 1e-5f && MuUndos == u1 + 1, e?.Length + ", entries " + (MuUndos - u1));
            Check("the other entries byte-equal", Others() == kept);
            yield return MuClick(row.ResetButton, "↺ of the bone");
            yield return MuFrames(3);
            Check("↺: the entry leaves, the others byte-equal", MuUi.Ops.BoneModArray(muChara).All(m => m.Bone != bone) && Others() == kept, Short(FieldJson.ToText(MuUi.Ops.BoneModArray(muChara))));
        }
        yield return MuClick(MuQ("hair-bones-stale-remove-0"), "Remove of the entry found nowhere");
        yield return MuFrames(3);
        Check("Remove takes the entry away, the others byte-equal", MuUi.Ops.BoneModArray(muChara).All(m => m.Bone != "no_such_bone") && Others() == kept && hb.Stale.Shown.Count == 0, string.Join(", ", hb.Stale.Shown));
        yield return MuUndoAll(u0);
        fold.value = false;
        yield return MuFrames(2);
    }

    // ------------------------------------------------------------ MJ2, MJ4 (headless)

    const float HairDt = 1f / 60f;

    static partial void FullHair(McHost h)
    {
        if (Want("MJ2")) Guard("MJ2 a hair modifier", () => Mj2(h));
        if (Want("MJ4")) Guard("MJ4 a modifier and a correction", () => Mj4(h));
    }

    static string HairCharacter(McHost h, string what)
    {
        string key = LoadTarget(h);
        h.Ok(new { cmd = "chara.anime", key, group = 19, category = 0, no = 0, time = 0.5f, speed = 0f }, what + ": the idle row");
        h.Ok(new { cmd = "chara.motion", key, autoStep = false, physics = false, reset = true }, what + ": motion by hand");
        return key;
    }

    static void HairStep(McHost h, string key, int n = 2)
    {
        h.Rt.ApplyNow();
        for (int i = 0; i < n; i++) Real(h, key).Motion.Step(HairDt);
    }

    static Vector3 Near3(Vector3 v) => new Vector3(Round(v.x), Round(v.y), Round(v.z));

    static void Mj2(McHost h)
    {
        string key = HairCharacter(h, "MJ2");
        var cc = Ctx(h, key);
        var bones = HairBones.Of(cc);
        if (!Check("MJ2 the hair class stands", bones.Count > 0 && bones[0].name == HairBones.RootName, bones.Count + " bones")) return;
        Info("MJ2 hair bones: " + bones.Count);
        string bone = HairBones.RootName;
        HairStep(h, key);
        var scale0 = HairBones.Find(Ctx(h, key), bone).localScale;
        // a modifier of another class stands beside it and must not move
        var body = new BodyBoneMod { Bone = "cf_J_Kosi01", Location = BodyBoneMod.LocationBodyTop, Scale = new Vec3(1.01f, 1f, 1f) };
        var r = h.App.Ops.EditAppearance(new[] { key }, new (string, object)[] { (BoneModEdits.Path, new List<BodyBoneMod> { body }) }, new ExecuteOptions { Source = CommandSource.User, Label = "test" }, allowRebuild: true);
        if (!Check("MJ2 another entry stands", r.Ok, r.Code + " " + r.Message)) return;
        h.Rt.ApplyNow();
        string Others() => FieldJson.ToText(h.App.Ops.BoneModArray(key).Where(m => m.Bone != bone).ToList());
        string others = Others();
        var set = h.Ok(new { cmd = "chara.edit.boneMods.set", key, bone, scale = new[] { 1.25f, 1.25f, 1.25f } }, "MJ2 chara.edit.boneMods.set");
        HairStep(h, key);
        var t = HairBones.Find(Ctx(h, key), bone);
        Check("MJ2 the modifier reaches the bone in its stage", Mathf.Abs(t.localScale.x - scale0.x * 1.25f) < 1e-3f, t.localScale + " from " + scale0);
        Check("MJ2 the other entries byte-equal", Others() == others);
        var list = h.Call(new { cmd = "chara.edit.boneMods", key });
        Check("MJ2 the read lists the bone, found", ((JArray)list["mods"]).Any(m => (string)m["bone"] == bone && m.Value<bool>("found")), Short(list.ToString()));
        Check("MJ2 a new entry's location", h.App.Ops.BoneModArray(key).First(m => m.Bone == bone).Location == BodyBoneMod.LocationBodyTop);
        var save = h.Ok(new { cmd = "scene.save", name = "mj2", overwrite = true, thumb = "placeholder", clock = "2026-11-05T00:00:00Z" }, "MJ2 scene.save");
        h.Ok(new { cmd = "scene.new" }, "MJ2 scene.new");
        h.Rt.ApplyNow();
        h.Ok(new { cmd = "scene.load", path = (string)save["path"] }, "MJ2 scene.load");
        h.Rt.ApplyNow();
        h.Ok(new { cmd = "chara.motion", key, autoStep = false, physics = false, reset = true }, "MJ2 motion by hand after the load");
        HairStep(h, key);
        t = HairBones.Find(Ctx(h, key), bone);
        Check("MJ2 after a load: the entry and its effect", h.App.Ops.BoneModArray(key).Any(m => m.Bone == bone) && Mathf.Abs(t.localScale.x - scale0.x * 1.25f) < 1e-3f, t.localScale.ToString());
        Check("MJ2 after a load: the other entries byte-equal", Others() == others);
        h.Ok(new { cmd = "chara.edit.boneMods.clear", key, bone }, "MJ2 chara.edit.boneMods.clear");
        HairStep(h, key);
        t = HairBones.Find(Ctx(h, key), bone);
        Check("MJ2 removed: the bone back, the others byte-equal", Near3(t.localScale) == Near3(scale0) && Others() == others, t.localScale + " / " + scale0);
        h.Ok(new { cmd = "scene.new" }, "MJ2 the scene goes");
    }

    /// <summary>
    /// MJ5 (T's MY4, row MaterialEditTest.corpus): every card's modifier on a bone that names hair, on the built card: does it stand in the hair class?
    /// Numbers and bone names only; cards by hash. Report Spike/out/maker/material_edit_hair_corpus_report.txt.
    /// </summary>
    public static void RunHairCorpus()
    {
        Reset();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var seed = HS2Next.Motion.Face.FaceSetup.Seed;
        var head = new System.Text.StringBuilder();
        var gd = CardResolveTest.BuildDb(head);
        var importer = new HS2Next.Import.Hs2Importer(gd, new HS2Next.Import.ImportOptions { Clock = () => new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc) });
        var cards = new List<(string rel, string hash, string sex, List<string> bones)>();
        int read = 0;
        MaterialEditTest_Corpus.Walk(importer, int.MaxValue, f =>
        {
            read++;
            if (f.Subtree == null) return;
            var bones = f.Subtree.Chara.Body.BoneMods.Select(m => m.Bone).Where(b => b != null && b.IndexOf("hair", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (bones.Count > 0) cards.Add((f.Rel, f.Hash, f.Group, bones));
        }, (hash, why) => Info("refused " + hash + " " + why));
        gd.UnloadAll();
        Info($"MJ5 files read {read}: {cards.Count} cards with {cards.Sum(c => c.bones.Count)} modifiers on bones that name hair");
        var h = NewHost();
        try
        {
            int inClass = 0, outside = 0, elsewhere = 0, nowhere = 0;
            var byBone = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var card in cards)
            {
                string rel = card.rel;
                foreach (var pre in new[] { "chara/female/", "chara/male/" })
                    if (rel.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) rel = rel.Substring(pre.Length);
                var r = h.Call(new { cmd = "chara.load", path = rel, sex = card.sex });
                string key = MuKey(r);
                bool built = r.Value<bool?>("ok") == true && key != null;
                if (!Check("MJ5 the card builds: " + card.hash, built, built ? "" : (string)r["code"])) continue;
                h.Rt.ApplyNow();
                var cc = Ctx(h, key);
                var names = HairBones.Names(cc);
                var body = AbmxApplier.BodyDictionary(cc);
                var accessories = HairBones.AccessoryBones(cc);
                foreach (var b in card.bones)
                {
                    if (names.Contains(b)) { inClass++; continue; }
                    bool found = AbmxApplier.Find(body, b);
                    bool onAccessory = !found && AbmxApplier.Find(accessories, b);
                    if (found) outside++;
                    else if (onAccessory) elsewhere++;
                    else nowhere++;
                    string k = b + (found ? " (on the body, outside the class)" : onAccessory ? " (on an accessory)" : " (found nowhere)");
                    byBone.TryGetValue(k, out int n);
                    byBone[k] = n + 1;
                }
                h.Call(new { cmd = "scene.new" });
                h.Rt.ApplyNow();
            }
            foreach (var kv in byBone) Info("MJ5 outside the class: " + kv.Key + " " + kv.Value);
            Info($"MJ5 in the hair class {inClass}, on the body outside it {outside}, on an accessory {elsewhere}, found nowhere {nowhere}");
            Check("MJ5 every modifier that resolves on its card stands in the hair class", outside == 0, outside + " outside");
        }
        finally { h.Dispose(); HS2Next.Motion.Face.FaceSetup.Seed = seed; }
        Finish("material_edit_hair_corpus_report.txt", "MaterialEditTest.RunHairCorpus: MJ5", sw, "MATERIAL EDIT TEST");
    }

    static void Mj4(McHost h)
    {
        string key = HairCharacter(h, "MJ4");
        var r = Real(h, key);
        var rows = h.Call(new { cmd = "pose.bones", key, limit = 2000 })["bones"] as JArray;
        var names = HairBones.Names(Ctx(h, key));
        var row = rows?.FirstOrDefault(b => names.Contains((string)b["name"]) && b.Value<bool?>("protected") != true && b.Value<bool?>("physics") != true && b.Value<bool?>("shown") != false);
        if (!Check("MJ4 a hair bone a correction reaches", row != null, rows?.Count + " rows")) return;
        string address = (string)row["bone"], bone = (string)row["name"];
        Info("MJ4 the bone: " + address);
        HairStep(h, key);
        var t = HairBones.Find(Ctx(h, key), bone);
        var scale0 = t.localScale;
        h.Ok(new { cmd = "chara.edit.boneMods.set", key, bone, scale = new[] { 1.2f, 1.2f, 1.2f } }, "MJ4 the modifier");
        HairStep(h, key);
        t = HairBones.Find(Ctx(h, key), bone);
        var mod = t.localScale;
        Check("MJ4 the modifier acts", Mathf.Abs(mod.x - scale0.x * 1.2f) < 1e-3f, mod + " from " + scale0);
        h.Ok(new { cmd = "pose.bones.set", key, bone = address, scale = new[] { 1.5f, 1.5f, 1.5f } }, "MJ4 the correction");
        HairStep(h, key);
        t = HairBones.Find(Ctx(h, key), bone);
        Check("MJ4 the correction sits on top of it", Mathf.Abs(t.localScale.x - mod.x * 1.5f) < 2e-3f, t.localScale + " from " + mod);
        h.Ok(new { cmd = "chara.edit.boneMods.clear", key, bone }, "MJ4 the modifier goes");
        HairStep(h, key);
        t = HairBones.Find(Ctx(h, key), bone);
        Check("MJ4 removing the modifier leaves the correction", Mathf.Abs(t.localScale.x - scale0.x * 1.5f) < 2e-3f, t.localScale + " from " + scale0);
        h.Ok(new { cmd = "chara.edit.boneMods.set", key, bone, scale = new[] { 1.2f, 1.2f, 1.2f } }, "MJ4 the modifier again");
        h.Ok(new { cmd = "pose.bones.clear", key, bone = address }, "MJ4 the correction goes");
        HairStep(h, key);
        t = HairBones.Find(Ctx(h, key), bone);
        Check("MJ4 removing the correction leaves the modifier", Mathf.Abs(t.localScale.x - scale0.x * 1.2f) < 1e-3f, t.localScale + " from " + scale0);
        h.Ok(new { cmd = "scene.new" }, "MJ4 the scene goes");
    }
}

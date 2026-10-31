// TS (v0.3 trial 2, package G; the head-type list crash): every thumbnail list of every Maker page scrolled from top to
// bottom through the bridge's ui.* commands, for the female and the male SFW target, in Editor play (BridgeTest.RunPlayThumbs)
// and in the app player (-bridgeAccept with -ui -acceptOnly TS). For each character: file.new, add.female / add.male, then
// every page of the Face, Body, Hair (each part), Clothes (each kind) and Accessories nodes through ui.action edit.show
// (once per section, which opens the section's foldout); every shown Maker list with thumbnails (part lists, push grids, a
// clothes pattern list opened with its button, the accessory add panel's list of each of the 13 types) is expanded with its
// ▾ button, brought into view and scrolled with ui.input wheel events (about a viewport per event) until its scroller is at
// the end, waiting after each step until ui.state thumbs.pending is 0. Checks: every list reached its end, nothing pending at
// the end of a list and of the run, no texture fell back to RGBA32 for a block size that is not a multiple of four, no
// "Streaming Size differs" / failed asynchronous texture read / mip assertion / device loss line in the log, and the process
// is alive (the trial player crashed on the first of them). Numbers per character: lists, cells, wheel events, thumbnails
// loaded from bundles / disk, refused by their header, skipped by the guard's records, unchecked (LZMA), failed by reason.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2Next.Content;
using HS2Next.UI;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using HS2Next.UI.Panels.Maker;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.App
{
    public static partial class BridgeAcceptance
    {
        sealed class TsState
        {
            public UiHost Host;
            public UiContext Ui;
            public int Fallbacks, StreamSize, ReadFailed, MipAsserts, DeviceLost;
            public readonly List<string> Bad = new List<string>();
            public int Lists, Cells, Wheels, NotAtEnd, Fallback, PendingLeft;
            public readonly HashSet<MakerThumbList> Done = new HashSet<MakerThumbList>();
        }

        /// <summary>Wheel steps per list at most (a 3,192-row list of 4 columns needs about 100 at 8 rows per step).</summary>
        public static int TsMaxWheels = 1500;

        public static IEnumerator ThumbScroll(AcceptanceCtx c)
        {
            c.Id = "TS";
            yield return c.Call(new { cmd = "ui.state" });
            if (c.Last == null || !c.Last.Ok || (bool?)c.J["enabled"] != true)
            {
                c.Warn("TS skipped: the Studio UI is not running (start with -ui; " + AcceptanceCtx.Short(c.Last?.ToString()) + ")");
                yield break;
            }
            var s = new TsState { Host = UiHost.Current };
            s.Ui = s.Host?.Context;
            if (!c.Check(s.Ui != null && s.Ui.App?.Catalog != null, "UiHost and the content catalog in the app's process")) yield break;
            Application.LogCallback onLog = (m, st, t) =>
            {
                if (m == null) return;
                bool bad = false;
                if (m.Contains("requires multiple-of-four dimensions")) { s.Fallbacks++; bad = true; }
                else if (m.Contains("Streaming Size differs")) { s.StreamSize++; bad = true; }
                else if (m.Contains("async texture load: failed") || m.Contains("Could not open file archive")) { s.ReadFailed++; bad = true; }
                else if (t == LogType.Assert && m.Contains("mipLevel")) { s.MipAsserts++; bad = true; }
                else if (m.Contains("Device removed") || m.Contains("887a0005") || m.Contains("887a0006")) { s.DeviceLost++; bad = true; }
                if (bad) lock (s.Bad) if (s.Bad.Count < 12) s.Bad.Add(AcceptanceCtx.Short(m));
            };
            Application.logMessageReceivedThreaded += onLog;
            var total = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                string female = Path.Combine(AppCore.Current.Paths.Hs2CharaDir(1) ?? "", c.FemaleRel ?? "");
                string male = Path.Combine(AppCore.Current.Paths.Hs2CharaDir(0) ?? "", Ui2MaleRel ?? "");
                yield return TsChara(c, s, "female", female);
                c.Info($"TS: female {total.Elapsed.TotalSeconds:F1} s");
                total.Restart();
                yield return TsChara(c, s, "male", male);
                c.Info($"TS: male {total.Elapsed.TotalSeconds:F1} s");
            }
            finally { Application.logMessageReceivedThreaded -= onLog; }
            c.Id = "TS";
            yield return c.Call(new { cmd = "ui.state" });
            var th = c.J["thumbs"] as JObject;
            c.Info("TS: ui.state thumbs at the end: " + (th?.ToString(Newtonsoft.Json.Formatting.None) ?? "(none)"));
            c.Check(th != null && (int?)th["pending"] == 0, $"nothing pending at the end ({th?["pending"]})");
            c.Check(s.NotAtEnd == 0, $"every list scrolled to its end ({s.Lists} lists, {s.NotAtEnd} not at the end)");
            c.Check(s.Fallbacks == 0, $"no texture fell back to RGBA32 for a size that is not a multiple of four ({s.Fallbacks})", string.Join(" | ", s.Bad));
            c.Check(s.StreamSize == 0 && s.ReadFailed == 0, $"no streaming size mismatch and no failed asynchronous texture read ({s.StreamSize}, {s.ReadFailed})", string.Join(" | ", s.Bad));
            c.Check(s.MipAsserts == 0, $"no mip assertion ({s.MipAsserts})", string.Join(" | ", s.Bad));
            c.Check(s.DeviceLost == 0 && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null, $"no device loss ({s.DeviceLost}; graphics {SystemInfo.graphicsDeviceType})");
            if (s.Fallback > 0) c.Warn($"TS: {s.Fallback} lists needed the in-process scroll after the wheel did not move them");
        }

        static IEnumerator TsChara(AcceptanceCtx c, TsState s, string sex, string card)
        {
            var ui = s.Ui;
            c.Id = "TS." + sex;
            yield return c.Call(new { cmd = "ui.action", action = "file.new", args = new { force = true } });
            c.CheckOk("ui.action file.new {force}");
            yield return c.Call(new { cmd = "ui.action", action = "add." + sex, args = new { path = card } }, 180);
            string id = (string)c.J["result"];
            if (!c.Check(c.Last != null && c.Last.Ok && !string.IsNullOrEmpty(id), $"ui.action add.{sex} {{path}} → a new character", AcceptanceCtx.Short(c.Last?.ToString()))) yield break;
            for (int i = 0; i < 600 && ui.Runtime.FindChara(id)?.State != HS2Next.Scenes.Runtime.RealizationState.Built; i++) yield return c.Frames(1);
            if (!c.Check(ui.Runtime.FindChara(id)?.State == HS2Next.Scenes.Runtime.RealizationState.Built, "the character is built")) yield break;
            yield return c.Call(new { cmd = "select", key = id });
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "ui.state" });
            var th0 = TsThumbs(c);
            int lists0 = s.Lists, cells0 = s.Cells, wheels0 = s.Wheels;
            s.Done.Clear();
            foreach (var node in new[] { "face", "body", "hair", "clothes", "accessories" })
            {
                var pages = InspectorSchemas.CharaEditPagesOf(node);
                IEnumerable<(string page, string part, string kind)> views =
                    node == "hair" ? InspectorSchemas.HairParts.Select(p => (pages[0], p, (string)null)) :
                    node == "clothes" ? InspectorSchemas.ClothesEditKinds.Select(k => (pages[0], (string)null, k)) :
                    pages.Select(p => (p, (string)null, (string)null));
                foreach (var (page, part, kind) in views.ToList())
                {
                    string where = node + "/" + page + (part != null ? "/" + part : "") + (kind != null ? "/" + kind : "");
                    var sections = InspectorSchemas.CharaEditSections(node, page).Select(x => x.Id).ToList();
                    if (sections.Count == 0) sections.Add(null);
                    foreach (var section in sections)
                    {
                        yield return c.Call(new { cmd = "ui.action", action = "edit.show", args = new { node, page, section, part, kind } });
                        if (c.Last == null || !c.Last.Ok) { c.Info($"TS.{sex}: edit.show {where} {section}: {AcceptanceCtx.Short(c.Last?.ToString())}"); continue; }
                        yield return c.Frames(4);
                        var tab = s.Host.Root.Q<CharaEditTab>();
                        if (tab == null) continue;
                        foreach (var list in TsShownLists(tab).ToList()) yield return TsScroll(c, s, list, where + " " + TsName(list));
                        if (node == "clothes") yield return TsPatternLists(c, s, tab, where);
                    }
                    if (node == "accessories") yield return TsAccessoryAdd(c, s, where);
                }
            }
            yield return c.Call(new { cmd = "ui.state" });
            var th1 = TsThumbs(c);
            c.Info($"TS.{sex}: {s.Lists - lists0} lists, {s.Cells - cells0} cells, {s.Wheels - wheels0} wheel events; thumbnails " + TsDelta(th0, th1));
            c.Check(s.Lists - lists0 >= 20, $"{sex}: at least 20 thumbnail lists were scrolled ({s.Lists - lists0})");
        }

        static JObject TsThumbs(AcceptanceCtx c) => c.J["thumbs"] as JObject;

        static string TsDelta(JObject a, JObject b)
        {
            if (b == null) return "(no ui.state thumbs)";
            string D(string k) => ((int?)b[k] ?? 0) - ((int?)a?[k] ?? 0) + "";
            return $"from bundles {D("fromBundles")}, from disk {D("fromDisk")}, loose {D("fromLoose")}, refused by the header {D("headerRejected")}, " +
                   $"guard-skipped {D("guardSkipped")}, unchecked {D("headerUnchecked")}, mip<1 after load {D("badMipRejected")}, failed {D("failed")} " +
                   $"({b["failReasons"]?.ToString(Newtonsoft.Json.Formatting.None)}), unloads deferred {D("unloadsDeferred")}, open bundles {b["openBundles"]}";
        }

        static string TsName(MakerThumbList l) => string.IsNullOrEmpty(l.name) ? l.GetType().Name : l.name;

        // the Maker lists with image cells shown on the tab now
        static IEnumerable<MakerThumbList> TsShownLists(VisualElement root) =>
            root.Query<MakerThumbList>().ToList().Where(l => l.panel != null && l.resolvedStyle.display != DisplayStyle.None && TsVisible(l) && l.Grid?.Source != null);

        static bool TsVisible(VisualElement e)
        {
            for (var x = e; x != null; x = x.hierarchy.parent)
                if (x.resolvedStyle.display == DisplayStyle.None || x.resolvedStyle.visibility == Visibility.Hidden) return false;
            return true;
        }

        // the first pattern button of the shown kind opens the pattern list (category 348; the same rows for every colour set)
        static IEnumerator TsPatternLists(AcceptanceCtx c, TsState s, VisualElement tab, string where)
        {
            var row = tab.Query<ClothesPatternRow>().ToList().FirstOrDefault(TsVisible);
            if (row == null) yield break;
            var button = row.Q<Button>();
            if (button == null) yield break;
            yield return TsClick(c, s, button, where + " pattern button");
            yield return c.Frames(4);
            foreach (var list in TsShownLists(row).ToList()) yield return TsScroll(c, s, list, where + " pattern list");
            yield return TsClick(c, s, button, where + " pattern button (close)");
        }

        // «＋» opens the add panel; each of the 13 types shows its item list (no pick: the panel is closed afterwards)
        static IEnumerator TsAccessoryAdd(AcceptanceCtx c, TsState s, string where)
        {
            var tab = s.Host.Root.Q<CharaEditTab>();
            var add = tab?.Q("acc-slot-add");
            if (add == null) { c.Info($"TS: {where}: no «＋» row"); yield break; }
            yield return TsClick(c, s, add, where + " «＋»");
            yield return c.Frames(4);
            for (int k = InspectorSchemas.AccessoryCategoryFirst; k <= InspectorSchemas.AccessoryCategoryLast; k++)
            {
                int t = k - InspectorSchemas.AccessoryCategoryNone;
                var b = tab.Q<Button>("acc-add-type-" + t);
                if (b == null || !TsVisible(b)) { c.Info($"TS: {where}: no type button acc-add-type-{t}"); continue; }
                yield return TsClick(c, s, b, where + " type " + t);
                yield return c.Frames(4);
                var list = tab.Query<PartList>("acc-add-list").ToList().FirstOrDefault(TsVisible);
                if (list != null) { s.Done.Remove(list); yield return TsScroll(c, s, list, where + " add list of category " + k); }
            }
            var cancel = tab.Q<Button>("acc-add-cancel");
            if (cancel != null && TsVisible(cancel)) yield return TsClick(c, s, cancel, where + " add panel cancel");
        }

        static IEnumerator TsClick(AcceptanceCtx c, TsState s, VisualElement e, string what)
        {
            var p = new Vector2[1];
            yield return RevealPastToasts(c, s.Host, e, what, p);
            yield return c.Call(new { cmd = "ui.input", frames = 3, events = new object[] { new { type = "pointer", phase = "click", x = p[0].x, y = p[0].y, button = 0 } } });
        }

        static IEnumerator TsWaitIdle(AcceptanceCtx c, int[] pending)
        {
            for (int i = 0; i < 120; i++)
            {
                yield return c.Call(new { cmd = "ui.state" });
                int p = (int?)c.J["thumbs"]?["pending"] ?? 0;
                pending[0] = p;
                if (p == 0) yield break;
                yield return c.Frames(5);
            }
        }

        static IEnumerator TsScroll(AcceptanceCtx c, TsState s, MakerThumbList list, string what)
        {
            if (list == null || !s.Done.Add(list)) yield break;
            if (list.Items.Count == 0) yield break;
            var expand = list.Q<Button>("partlist-expand");
            bool expandedHere = expand != null && list is PartList pl && !pl.Expanded && TsVisible(expand);
            if (expandedHere) yield return TsClick(c, s, expand, what + " ▾");
            var view = list.Grid.List;
            var sv = view?.Q<ScrollView>();
            var sc = sv?.verticalScroller;
            if (sc == null) { c.Info($"TS: {what}: no scroll view"); yield break; }
            var p = new Vector2[1];
            yield return RevealPastToasts(c, s.Host, view, what, p);
            s.Lists++;
            s.Cells += list.Items.Count;
            var pending = new int[1];
            yield return TsWaitIdle(c, pending);
            float delta = 3f;
            int wheels = 0, stuck = 0;
            while (sc.value < sc.highValue - 0.5f && wheels < TsMaxWheels)
            {
                float before = sc.value;
                p[0] = view.worldBound.center;
                yield return c.Call(new { cmd = "ui.input", frames = 2, events = new object[] { new { type = "wheel", x = p[0].x, y = p[0].y, delta } } });
                wheels++;
                s.Wheels++;
                float moved = sc.value - before;
                if (moved > 0.5f)
                {
                    stuck = 0;
                    // about one viewport per wheel event
                    float page = Mathf.Max(20f, sv.contentViewport.layout.height * 0.9f);
                    delta = Mathf.Clamp(delta * page / moved, 1f, 200f);
                }
                else if (++stuck >= 3)
                {
                    // the wheel did not move it (covered, or not over the list): the scroller in-process, counted
                    s.Fallback++;
                    sc.value = Mathf.Min(sc.highValue, sc.value + Mathf.Max(20f, sv.contentViewport.layout.height * 0.9f));
                    stuck = 0;
                }
                yield return TsWaitIdle(c, pending);
            }
            yield return TsWaitIdle(c, pending);
            bool atEnd = sc.value >= sc.highValue - 0.5f;
            if (!atEnd) s.NotAtEnd++;
            if (pending[0] != 0) s.PendingLeft++;
            c.Check(atEnd && pending[0] == 0, $"{what}: {list.Items.Count} cells scrolled to the end with {wheels} wheel events, nothing pending",
                $"scroller {sc.value:0} of {sc.highValue:0}, pending {pending[0]}");
            if (expandedHere && TsVisible(expand)) yield return TsClick(c, s, expand, what + " ▾ (folded again)");
        }
    }
}

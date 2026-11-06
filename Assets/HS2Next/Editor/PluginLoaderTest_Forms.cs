// The play row's checks of the declarative forms, the host widgets, the capture events, the Options plugin toggle and the
// skin-layer / body-part results that need the graphics device (PluginLoaderTest.play, session 1; every static KU<n>, KE<n>,
// KD<n>, KB<n>(PlayCtx) of this partial class runs after PL13, in that order). Numbers, names, element state and counts only:
// the UI is read through ui.state / ui.query, no screenshot or capture is opened (files by size), characters are the clothed
// visual targets of Tools/targets.json.
//   KU2  forms without the UI: a second plugin host (no app, no UI) loads the form probe alone: forms validated and counted
//        (plugin.list ui.forms 3, Available false, the HairBundles registration refused); the play host's count equals
//   KU3  every drawn row kind of the probe's panel in ui.query with the read values (and a label key the strings lack shown as
//        the key); the read round trip (median and max over 100 reads)
//   KU4  edits: a label drag through ui.input = preview writes, one commit, one undo entry (the row's label), undo / redo
//        write the old / new value; Esc during a drag = cancel with the value before it, no entry; two typed values = one
//        merged entry; previews of one frame = one write (the frame's last value follows in the next frame)
//   KU5  the item section: per-node values, read again when the inspected item changes, an edit writes that node only
//   KU6  Refresh reads at once; a new scene reads again
//   KU7  a failed read shows its error in place of the rows; a deferring read command is refused inline; a value of the
//        wrong shape marks the row and logs one warning; a failed write is a toast
//   KU8  the IUiWidgets factories (values, a callback through ui.input, SetValue without a callback), a dialog, a confirm, a
//        prompt, a menu and a toast through ui.query / ui.input
//   KU9  the HairBundles row refused at registration (the message), the other forms counted
//   KU10 plugin.disable removes the forms and the open tab; plugin.enable brings them back
//   KE1-KE4, KE6, KE7 the capture events through the history probe: ui.action capture.screenshot (origin Ui), the screenshot
//        command (origin Command, supersampled render size), the capture command (Kind Capture, path and frame of the reply,
//        CaptureService.Saved not raised), a refused screenshot raises none, a Request inside CaptureStarting answers busy, the
//        probe's own object hidden for the render and shown again at CaptureRendered
//   KD7  the Options toggle of the form probe: off = disabled at once, on = loaded; no "after a restart" mark
//   KB1, KB5, KB7 (package B's device checks with the body probe): a BodyMesh PNG layer drawn in the one build; layer + body
//        copy + normals in one pass; a layer set outside builds re-composited without a rebuild and removed again
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using HS2Next.App;
using HS2Next.App.Plugins;
using HS2Next.UI;
using HS2Next.UI.Plugins;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;

public static partial class PluginLoaderTest
{
    const string FormGuid = "stp3.test.formprobe", HistoryGuid = "stp3.test.historyprobe", BodyGuid = "stp3.test.bodyprobe";
    const string FormPanel = FormGuid + "/panel", FormBase = "plugin-form-" + FormPanel;
    const string FP = "plugin.stp3.test.formprobe.";
    const string WidgetBase = "plugin-widget-" + FP;

    // ---------------------------------------------------------------- the main thread from the worker

    static readonly ConcurrentQueue<Action> mainQueue = new ConcurrentQueue<Action>();

    /// <summary>Runs queued main-thread work (the editor update of the play row).</summary>
    static void PumpMain()
    {
        for (int i = 0; i < 16 && mainQueue.TryDequeue(out var a); i++)
        {
            try { a(); }
            catch (Exception) { }
        }
    }

    /// <summary>Runs <paramref name="f"/> on the main thread and waits for its result (the exception it threw is rethrown).</summary>
    static T OnMain<T>(Func<T> f, int timeoutMs = 60000)
    {
        T result = default;
        Exception err = null;
        using (var done = new ManualResetEventSlim(false))
        {
            mainQueue.Enqueue(() =>
            {
                try { result = f(); }
                catch (Exception e) { err = e; }
                finally { done.Set(); }
            });
            if (!done.Wait(timeoutMs)) throw new TimeoutException("the main thread did not run the check within " + timeoutMs + " ms");
        }
        if (err != null) throw new Exception("on the main thread: " + err.GetType().Name + ": " + err.Message, err);
        return result;
    }

    // ---------------------------------------------------------------- UI helpers (HTTP)

    static JObject UiQ(PlayCtx c, string name)
    {
        var r = c.Call(new { cmd = "ui.query", name });
        return (r["elements"] as JArray)?.OfType<JObject>().FirstOrDefault();
    }

    static List<JObject> UiAll(PlayCtx c, string sel) =>
        (c.Call(new { cmd = "ui.query", name = sel, all = true })["elements"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();

    /// <summary>The text is one of the languages' strings (the session's UI language is the settings').</summary>
    static bool Lx(string text, params string[] options) => text != null && options.Contains(text);

    static bool Shown(JObject e) => e != null && (bool?)e["visible"] == true;

    static bool HasClass(JObject e, string cls) => (e?["classes"] as JArray)?.Any(x => (string)x == cls) == true;

    static string S(JToken t) => t == null ? "null" : t.Type == JTokenType.String ? (string)t : t.ToString(Formatting.None);

    static void Click(PlayCtx c, string target, int frames = 2) =>
        c.Call(new { cmd = "ui.input", frames, events = new object[] { new { type = "pointer", target, phase = "click", button = 0, clicks = 1 } } });

    static void Key(PlayCtx c, string key, int frames = 2) =>
        c.Call(new { cmd = "ui.input", frames, events = new object[] { new { type = "key", key } } });

    static JObject FormState(PlayCtx c, string form = null, string node = null) =>
        form == null ? c.Call(new { cmd = "formprobe.state" }) : c.Call(new { cmd = "formprobe.state", form, node });

    static List<string> Writes(PlayCtx c, bool clear = false) =>
        (c.Call(new { cmd = "formprobe.log", clear })["writes"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();

    static List<string> UndoLabels(PlayCtx c) => (c.Call(new { cmd = "history" })["undo"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();

    static double? Num(JObject e) => e?["value"]?.Type == JTokenType.Float || e?["value"]?.Type == JTokenType.Integer ? (double)e["value"] : (double?)null;

    static bool Near(double? a, double b) => a.HasValue && Math.Abs(a.Value - b) < 1e-4;

    /// <summary>Waits until <paramref name="ok"/> holds (polls each few frames), true when it did.</summary>
    static bool Until(PlayCtx c, Func<bool> ok, double sec = 10)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < sec)
        {
            if (ok()) return true;
            c.WaitFrames(2);
        }
        return ok();
    }

    static void ShowPanel(PlayCtx c, string id)
    {
        c.Call(new { cmd = "ui.panel", panel = id, op = "show" });
        c.WaitFrames(4);
    }

    // ---------------------------------------------------------------- KU2 forms without the UI

    static void KU2(PlayCtx c)
    {
        string dir = c.PluginsDir, ud = Path.Combine(c.OutDir, "ku2_userdata");
        var r = OnMain(() =>
        {
            var others = new List<string>();
            foreach (var d in Directory.GetDirectories(dir))
            {
                string m = Path.Combine(d, "manifest.json");
                if (!File.Exists(m)) continue;
                string g = (string)JObject.Parse(File.ReadAllText(m))["guid"];
                if (g != null && g != FormGuid) others.Add(g);
            }
            var h2 = new PluginHost(null, ud);
            var launch = PluginLaunch.Resolve(dir, false, null, true, null, others, ContentSettings_ProjectRoot());
            h2.Load(launch, false);
            var o = new JObject
            {
                ["list"] = h2.ListJson(),
                ["state"] = JObject.Parse(h2.Bridge.RunNow("{\"cmd\":\"formprobe.state\"}")),
            };
            h2.Shutdown();
            o["after"] = h2.Find(FormGuid)?.State;
            return o;
        });
        var p = PluginOf(r["list"] as JObject, FormGuid);
        var st = r["state"] as JObject;
        c.Check((string)p?["state"] == "loaded" && (int?)p?["ui"]?["forms"] == 3, "KU2 a second host without the UI loads the form probe: forms validated and counted (ui.forms 3)",
            PlayCtx.Short(p?["ui"]?.ToString(Formatting.None)) + " state " + p?["state"]);
        c.Check((bool?)st?["formsAvailable"] == false && (bool?)st?["widgetsAvailable"] == false, "KU2 without the UI: IPluginForms.Available false, no IPluginUi (no widgets)", PlayCtx.Short(S(st)));
        var refusals = (st?["refusals"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
        c.Check(refusals.Any(x => x.StartsWith("hair:", StringComparison.Ordinal) && x.Contains("reserved")), "KU2 the HairBundles row is refused there too", string.Join(" | ", refusals));
        c.Check((string)r["after"] == "unloaded", "KU2 the second host's shutdown unloads it", S(r["after"]));
        var main = PluginOf(c.Call(new { cmd = "plugin.list" }), FormGuid);
        c.Check((string)main?["state"] == "loaded" && (int?)main?["ui"]?["forms"] == 3, "KU2 the play host counts the same 3 forms", PlayCtx.Short(main?["ui"]?.ToString(Formatting.None)));
        var ms = FormState(c);
        c.Check((bool?)ms["formsAvailable"] == true && (bool?)ms["widgetsAvailable"] == true, "KU2 with the UI: Available true, IPluginUi.Widgets set", PlayCtx.Short(c.LastText));
    }

    static string ContentSettings_ProjectRoot() => HS2Next.Content.ContentSettings.ProjectRoot;

    // ---------------------------------------------------------------- KU3 every drawn kind

    static void KU3(PlayCtx c)
    {
        var ui = c.Ok(new { cmd = "ui.state" }, "ui.state");
        var panel = (ui["panels"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x["id"] == FormPanel);
        c.Check(panel != null && (string)panel["region"] == "bottom", "KU3 the form panel is in ui.state in the bottom region", PlayCtx.Short(panel?.ToString(Formatting.None)));
        ShowPanel(c, FormPanel);
        var tab = UiQ(c, "tab-" + FormPanel);
        c.Check(tab != null && (c.LastText.Contains("Form probe panel") || c.LastText.Contains("フォーム試験パネル")), "KU3 its tab shows the plugin's title string", PlayCtx.Short(c.LastText));
        var root = UiQ(c, FormBase);
        c.Check(Shown(root) && !Shown(UiQ(c, FormBase + "-error")), "KU3 the form is drawn without an error line", PlayCtx.Short(S(root)));
        string R(string key) => FormBase + "-" + key;
        var f = UiQ(c, R("f-field"));
        c.Check(Near(Num(f), 2.5) && Shown(UiQ(c, R("f-slider"))) && Shown(UiQ(c, R("f-reset"))) && Lx((string)UiQ(c, R("f-label"))?["text"], "Size", "サイズ"),
            "KU3 Float: the slider row shows 2.5, its slider, its reset (default 5) and the label string", "field " + S(f?["value"]));
        c.Check(Near(Num(UiQ(c, R("i-field"))), 4), "KU3 Int: 4", S(UiQ(c, R("i-field"))?["value"]));
        c.Check((bool?)UiQ(c, R("b-toggle"))?["value"] == true, "KU3 Bool: on", PlayCtx.Short(c.LastText));
        var sw = UiQ(c, R("c-swatch"));
        c.Check((string)sw?["tooltip"] == "#FF8040FF", "KU3 Color: the swatch shows [1, 0.5, 0.25, 1] (#FF8040FF)", S(sw?["tooltip"]));
        var hs = UiQ(c, R("h-swatch"));
        c.Check(((string)hs?["tooltip"] ?? "").StartsWith("#FFFFFFFF", StringComparison.Ordinal) && ((string)hs?["tooltip"] ?? "").Contains("1.5"), "KU3 ColorHdr: white, intensity 1.5", S(hs?["tooltip"]));
        c.Check(Near(Num(UiQ(c, R("v2-c0"))), 1) && Near(Num(UiQ(c, R("v2-c1"))), 2), "KU3 Vec2: [1, 2]");
        c.Check(Near(Num(UiQ(c, R("v3-c0"))), 1) && Near(Num(UiQ(c, R("v3-c1"))), 2) && Near(Num(UiQ(c, R("v3-c2"))), 3), "KU3 Vec3: [1, 2, 3]");
        var e0 = UiQ(c, R("e-seg-0"));
        var e1 = UiQ(c, R("e-seg-1"));
        c.Check(HasClass(e1, "hs-segmented__button--on") && !HasClass(e0, "hs-segmented__button--on") && Lx((string)e0?["text"], "Soft", "やわらかい"),
            "KU3 Enum: segmented buttons, \"hard\" marked, labels from the strings", S(e0?["text"]) + " / " + S(e1?["classes"]));
        var rf = UiQ(c, R("r-ref"));
        c.Check((string)rf?["text"] == "Second" && (string)rf?["tooltip"] == "r2", "KU3 Ref: r2 shown with the choices command's label", S(rf?["text"]));
        c.Check(UiQ(c, R("file-file")) != null && UiQ(c, R("file-browse")) != null, "KU3 File: the picker (no file)", S(UiQ(c, R("file-file"))?["text"]));
        c.Check((string)UiQ(c, R("t-text"))?["value"] == "hello", "KU3 Text: hello", PlayCtx.Short(c.LastText));
        c.Check(UiQ(c, R("n-ref")) != null, "KU3 Node: the node picker", S(UiQ(c, R("n-ref"))?["text"]));
        c.Check(Lx((string)UiQ(c, R("btn-button"))?["text"], "Count up", "カウント"), "KU3 Button: labelled from the strings");
        var cells = UiAll(c, ".hs-card");
        int parts = cells.Count(x => ((string)x["name"] ?? "").StartsWith("part-", StringComparison.Ordinal));
        int pushes = cells.Count(x => ((string)x["name"] ?? "").StartsWith("push-colour:336-", StringComparison.Ordinal));
        c.Check(Shown(UiQ(c, R("pl-widget"))) && parts > 0, "KU3 PartList: the Maker list of category 314 is drawn (" + parts + " cells bound)");
        c.Check(Shown(UiQ(c, R("push-widget"))) && pushes > 0, "KU3 Push: the colour:336 push grid is drawn (" + pushes + " chips bound)");
        var ap = UiQ(c, "acc-parent-N_Wrist_L");
        c.Check(HasClass(ap, "hs-acc-parent__key--current") && ((string)UiQ(c, R("ap-current"))?["text"] ?? "").Contains("N_Wrist_L"), "KU3 AttachPoint: N_Wrist_L marked",
            S(UiQ(c, R("ap-current"))?["text"]));
        var nl = UiQ(c, R("nolabel-text"));
        c.Check((string)nl?["label"] == FP + "nolabel", "KU3 a label key the strings lack shows the key itself (the localiser's fallback)", S(nl?["label"]));
        // the read round trip on the main thread (the view's own read: envelope, inline call, parse, apply)
        var t = OnMain(() =>
        {
            var view = UiHost.Current?.Root?.Q<PluginFormView>(FormBase);
            if (view == null) return null;
            var times = new List<double>();
            for (int i = 0; i < 100; i++)
            {
                var w = Stopwatch.StartNew();
                view.ReadNow();
                times.Add(w.Elapsed.TotalMilliseconds * 1000);
            }
            times.Sort();
            return new JObject { ["median"] = Math.Round(times[50], 1), ["max"] = Math.Round(times[99], 1), ["error"] = view.ReadError };
        });
        c.Check(t != null && (string)t["error"] == null, "KU3 100 reads of the 17-row form without an error", S(t));
        c.Info("KU3 form read round trip: median " + t?["median"] + " µs, max " + t?["max"] + " µs over 100 reads (17 rows)");
    }

    // ---------------------------------------------------------------- KU4 edits and undo

    static (double x, double y) Center(JObject e)
    {
        var wb = e?["worldBound"] as JArray;
        if (wb == null || wb.Count < 4) return (0, 0);
        return ((double)wb[0] + (double)wb[2] / 2, (double)wb[1] + (double)wb[3] / 2);
    }

    static double StateNum(PlayCtx c, string form, string node, string key)
    {
        var v = FormState(c, form, node)["values"]?[key];
        return v != null && (v.Type == JTokenType.Float || v.Type == JTokenType.Integer) ? (double)v : double.NaN;
    }

    static void KU4(PlayCtx c)
    {
        ShowPanel(c, FormPanel);
        string label = FormBase + "-f-label";
        var l = UiQ(c, label);
        var (x, y) = Center(l);
        Writes(c, true);
        int undo0 = UndoLabels(c).Count;
        double before = StateNum(c, "panel", null, "f");
        c.Call(new
        {
            cmd = "ui.input", frames = 3,
            events = new object[]
            {
                new { type = "pointer", phase = "down", x, y, button = 0 },
                new { type = "pointer", phase = "move", x = x + 15, y, button = 0 },
                new { type = "pointer", phase = "move", x = x + 30, y, button = 0 },
                new { type = "pointer", phase = "move", x = x + 45, y, button = 0 },
                new { type = "pointer", phase = "up", x = x + 45, y, button = 0 },
            },
        });
        var w = Writes(c).Where(s => s.StartsWith("panel||f|", StringComparison.Ordinal)).ToList();
        int previews = w.Count(s => s.Split('|')[3] == "preview"), commits = w.Count(s => s.Split('|')[3] == "commit");
        double after = StateNum(c, "panel", null, "f");
        var undo1 = UndoLabels(c);
        c.Check(previews >= 1 && commits == 1 && w.Last().Split('|')[3] == "commit" && w.All(s => !s.EndsWith("|undo?", StringComparison.Ordinal)),
            "KU4 a label drag through ui.input: preview writes, then one commit (every write says undo false)", string.Join(" ; ", w));
        c.Check(after != before && undo1.Count == undo0 + 1 && Lx(undo1.FirstOrDefault(), "Size", "サイズ"), "KU4 the commit changed the value and recorded one undo entry with the row's label",
            $"{before} -> {after}; entries {undo0} -> {undo1.Count}, newest \"{undo1.FirstOrDefault()}\"");
        c.WaitFrames(3);
        c.Check(Near(Num(UiQ(c, FormBase + "-f-field")), after), "KU4 the form reads again after the commit", S(UiQ(c, FormBase + "-f-field")?["value"]));
        Writes(c, true);
        c.Ok(new { cmd = "undo" }, "undo");
        var uw = Writes(c, true);
        c.Check(uw.Count == 1 && uw[0].Split('|')[3] == "undo" && Near(double.Parse(uw[0].Split('|')[4], System.Globalization.CultureInfo.InvariantCulture), before) && Near(StateNum(c, "panel", null, "f"), before),
            "KU4 undo writes the value read before the edit (phase undo)", string.Join(" ; ", uw));
        c.WaitFrames(3);
        c.Check(Near(Num(UiQ(c, FormBase + "-f-field")), before), "KU4 the form reads again after the undo", S(UiQ(c, FormBase + "-f-field")?["value"]));
        c.Ok(new { cmd = "redo" }, "redo");
        var rw = Writes(c, true);
        c.Check(rw.Count == 1 && rw[0].Split('|')[3] == "redo" && Near(StateNum(c, "panel", null, "f"), after), "KU4 redo writes the committed value (phase redo)", string.Join(" ; ", rw));
        // Esc during a drag: cancel with the value before it, no entry
        c.WaitFrames(3);
        double b2 = StateNum(c, "panel", null, "f");
        int undo2 = UndoLabels(c).Count;
        c.Call(new
        {
            cmd = "ui.input", frames = 3,
            events = new object[]
            {
                new { type = "pointer", phase = "down", x, y, button = 0 },
                new { type = "pointer", phase = "move", x = x - 20, y, button = 0 },
                new { type = "pointer", phase = "move", x = x - 40, y, button = 0 },
                (object)new { type = "key", key = "Escape" },
                new { type = "pointer", phase = "up", x = x - 40, y, button = 0 },
            },
        });
        var ew = Writes(c, true);
        var last = ew.LastOrDefault()?.Split('|');
        c.Check(ew.Count(s => s.Split('|')[3] == "cancel") == 1 && ew.All(s => s.Split('|')[3] != "commit") && last != null && last[3] == "cancel" &&
                Near(double.Parse(last[4], System.Globalization.CultureInfo.InvariantCulture), b2) && UndoLabels(c).Count == undo2 && Near(StateNum(c, "panel", null, "f"), b2),
            "KU4 Esc during a drag: one cancel with the value before the drag, no commit, no entry", string.Join(" ; ", ew));
        // typed values: two commits, one merged entry; undo returns to the value before the first
        int undo3 = UndoLabels(c).Count;
        OnMain(() =>
        {
            var field = UiHost.Current.Root.Q<FloatField>(FormBase + "-f-field");
            field.value = 7f;
            field.value = 8f;
            return true;
        });
        var tw = Writes(c, true);
        c.Check(tw.Count(s => s.Split('|')[3] == "commit") == 2 && UndoLabels(c).Count == undo3 + 1 && Near(StateNum(c, "panel", null, "f"), 8),
            "KU4 two typed values: two commits, one undo entry (merged by <guid>/<form>/<node>/<key>)", string.Join(" ; ", tw) + "; entries " + undo3 + " -> " + UndoLabels(c).Count);
        c.Ok(new { cmd = "undo" }, "undo the typed values");
        c.Check(Near(StateNum(c, "panel", null, "f"), b2), "KU4 one undo returns to the value before the first typed one", StateNum(c, "panel", null, "f") + " vs " + b2);
        Writes(c, true);
        // previews of one frame: one write now, the frame's last value in the next frame
        OnMain(() =>
        {
            var ns = UiHost.Current.Root.Q<HS2Next.UI.Widgets.NumberSlider>(FormBase + "-f-widget");
            ns.Begin();
            ns.Raise(HS2Next.UI.Widgets.EditPhase.Preview, 1);
            ns.Raise(HS2Next.UI.Widgets.EditPhase.Preview, 2);
            ns.Raise(HS2Next.UI.Widgets.EditPhase.Preview, 3);
            return true;
        });
        c.WaitFrames(3);
        OnMain(() =>
        {
            UiHost.Current.Root.Q<HS2Next.UI.Widgets.NumberSlider>(FormBase + "-f-widget").End();
            return true;
        });
        var pw = Writes(c, true).Where(s => s.Split('|')[2] == "f").ToList();
        var pv = pw.Where(s => s.Split('|')[3] == "preview").Select(s => s.Split('|')[4]).ToList();
        c.Check(pv.Count == 2 && pv[0] == "1.0" && pv[1] == "3.0" && pw.Last().Split('|')[3] == "commit", "KU4 three previews in one frame: one write, then the frame's last value in the next frame",
            string.Join(" ; ", pw));
        c.Ok(new { cmd = "undo" }, "undo the preview test");
        KU4Picks(c);
    }

    /// <summary>Scrolls the panel form to a row (main thread) and waits for the layout.</summary>
    static void ScrollToRow(PlayCtx c, string key, string element = null)
    {
        OnMain(() =>
        {
            var root = UiHost.Current.Root;
            root.Q<ScrollView>(FormBase + "-scroll")?.ScrollTo(root.Q(element ?? FormBase + "-" + key));
            return true;
        });
        c.WaitFrames(4);
    }

    static string LastWrite(PlayCtx c, string key) => Writes(c).LastOrDefault(s => s.Split('|')[2] == key);

    /// <summary>KU4 picks through ui.input: the Maker list's pick and the push grid's push go to the form (the host's own
    /// change and push ops do not run), the attach point and a choice; each is one commit and one undo entry.</summary>
    static void KU4Picks(PlayCtx c)
    {
        Writes(c, true);
        // the Maker list (PartList) through its pick override
        ScrollToRow(c, "pl");
        var cells = UiAll(c, ".hs-card").Where(x => ((string)x["name"] ?? "").StartsWith("part-", StringComparison.Ordinal) && Shown(x)).ToList();
        var cell = cells.Skip(1).FirstOrDefault() ?? cells.FirstOrDefault();
        string pick = ((string)cell?["name"] ?? "").Length > 5 ? ((string)cell["name"]).Substring(5) : null;
        int undo0 = UndoLabels(c).Count;
        if (c.Check(pick != null, "KU4 the Maker list shows cells (" + cells.Count + " visible)"))
        {
            var (px, py) = Center(cell);
            c.Call(new
            {
                cmd = "ui.input", frames = 2,
                events = new object[]
                {
                    new { type = "pointer", phase = "move", x = px, y = py, button = 0 },
                    new { type = "pointer", phase = "down", x = px, y = py, button = 0, clicks = 1 },
                    new { type = "pointer", phase = "up", x = px, y = py, button = 0, clicks = 1 },
                },
            });
            bool sent = Until(c, () => LastWrite(c, "pl") != null, 15);
            var w = LastWrite(c, "pl");
            c.WaitFrames(4);
            var marked = UiQ(c, "part-" + pick);
            c.Check(sent && w == "panel||pl|commit|\"" + pick + "\"" && UndoLabels(c).Count == undo0 + 1 && HasClass(marked, "hs-card--current"),
                "KU4 PartList: a click on a cell sends one commit with its reference (the pick override), one undo entry, and the read marks the cell",
                (w ?? "no write") + "; entries " + undo0 + " -> " + UndoLabels(c).Count + "; current " + HasClass(marked, "hs-card--current"));
            c.Ok(new { cmd = "undo" }, "undo the list pick");
            c.Check(LastWrite(c, "pl") == "panel||pl|undo|null", "KU4 PartList: undo writes the value before (null)", LastWrite(c, "pl"));
        }
        // the push grid through its push override
        ScrollToRow(c, "push");
        var chips = UiAll(c, ".hs-card").Where(x => ((string)x["name"] ?? "").StartsWith("push-colour:336-", StringComparison.Ordinal) && Shown(x)).ToList();
        var chip = chips.Skip(2).FirstOrDefault() ?? chips.FirstOrDefault();
        string push = chip == null ? null : ((string)chip["name"]).Substring("push-colour:336-".Length);
        int undo1 = UndoLabels(c).Count;
        if (c.Check(push != null, "KU4 the push grid shows chips (" + chips.Count + " visible)"))
        {
            Click(c, (string)chip["name"], 3);
            bool sent = Until(c, () => LastWrite(c, "push") != null, 10);
            var w = LastWrite(c, "push");
            c.Check(sent && w == "panel||push|commit|\"" + push + "\"" && UndoLabels(c).Count == undo1 + 1 && Lx(UndoLabels(c).FirstOrDefault(), "Skin swatch", "肌の色見本"),
                "KU4 Push: a click on a chip sends one commit with its reference (the push override), one undo entry with the row's label",
                (w ?? "no write") + "; entries " + undo1 + " -> " + UndoLabels(c).Count);
        }
        // the attach point (its keys pick into the form) and a choice
        ScrollToRow(c, "ap", "acc-parent-N_Wrist_R");
        Click(c, "acc-parent-N_Wrist_R", 3);
        Until(c, () => LastWrite(c, "ap") != null, 5);
        c.WaitFrames(3);
        var key = UiQ(c, "acc-parent-N_Wrist_R");
        c.Check(LastWrite(c, "ap") == "panel||ap|commit|\"N_Wrist_R\"" && HasClass(key, "hs-acc-parent__key--current"),
            "KU4 AttachPoint: a key click sends one commit and the read marks it", (LastWrite(c, "ap") ?? "no write") + "; key visible " + Shown(key) + " " + S(key?["worldBound"]));
        ScrollToRow(c, "e");
        Click(c, FormBase + "-e-seg-0", 3);
        c.WaitFrames(3);
        c.Check(LastWrite(c, "e") == "panel||e|commit|\"soft\"" && HasClass(UiQ(c, FormBase + "-e-seg-0"), "hs-segmented__button--on"),
            "KU4 Enum: a segment click sends one commit with the choice's value", LastWrite(c, "e"));
        Writes(c, true);
    }

    // ---------------------------------------------------------------- KU5 the item section

    static void KU5(PlayCtx c)
    {
        string a = (string)c.Ok(new { cmd = "item.add", no = 399 }, "item.add A")["added"]?[0]?["key"];
        string b = (string)c.Ok(new { cmd = "item.add", no = 399 }, "item.add B")["added"]?[0]?["key"];
        c.Ok(new { cmd = "formprobe.set", form = "section", node = a, key = "w", value = 3 }, "w of A = 3");
        c.Ok(new { cmd = "formprobe.set", form = "section", node = b, key = "w", value = 7 }, "w of B = 7");
        const string sb = "plugin-form-" + FormGuid + "/section";
        c.Ok(new { cmd = "select", keys = new[] { a } }, "select A");
        c.Ok(new { cmd = "ui.panel", panel = "inspector", op = "show" }, "show the inspector");
        bool okA = Until(c, () => Near(Num(UiQ(c, sb + "-w-field")), 3));
        c.Check(okA && (string)UiQ(c, sb + "-note-text")?["value"] == a, "KU5 the item section shows A's values (w 3, note = its id)", S(UiQ(c, sb + "-w-field")?["value"]) + " " + S(UiQ(c, sb + "-note-text")?["value"]));
        int reads0 = (int)FormState(c)["reads"];
        c.Ok(new { cmd = "select", keys = new[] { b } }, "select B");
        bool okB = Until(c, () => Near(Num(UiQ(c, sb + "-w-field")), 7));
        c.Check(okB && (string)UiQ(c, sb + "-note-text")?["value"] == b && (int)FormState(c)["reads"] > reads0, "KU5 another item selected: the section reads again (B's values)",
            S(UiQ(c, sb + "-w-field")?["value"]));
        Writes(c, true);
        OnMain(() =>
        {
            UiHost.Current.Root.Q<FloatField>(sb + "-w-field").value = 5f;
            return true;
        });
        var w = Writes(c, true);
        c.Check(w.Count == 1 && w[0].StartsWith("section|" + b + "|w|commit|5", StringComparison.Ordinal) && Near(StateNum(c, "section", a, "w"), 3) && Near(StateNum(c, "section", b, "w"), 5),
            "KU5 an edit in the section writes the inspected node only", string.Join(" ; ", w));
        c.Ok(new { cmd = "undo" }, "undo the section edit");
        c.Check(Near(StateNum(c, "section", b, "w"), 7), "KU5 its undo writes B's value back", S(StateNum(c, "section", b, "w")));
        c.Ok(new { cmd = "delete", key = a }, "delete A");
        c.Ok(new { cmd = "delete", key = b }, "delete B");
    }

    // ---------------------------------------------------------------- KU6 Refresh

    static void KU6(PlayCtx c)
    {
        ShowPanel(c, FormPanel);
        string tf = FormBase + "-t-text";
        c.Ok(new { cmd = "formprobe.set", form = "panel", key = "t", value = "changed", refresh = true }, "formprobe.set t with Refresh");
        bool ok = Until(c, () => (string)UiQ(c, tf)?["value"] == "changed", 5);
        c.Check(ok, "KU6 Refresh reads the form again (the text shows \"changed\")", S(UiQ(c, tf)?["value"]));
        c.Ok(new { cmd = "formprobe.set", form = "panel", key = "t", value = "quiet" }, "formprobe.set t without Refresh");
        c.WaitFrames(5);
        bool stale = (string)UiQ(c, tf)?["value"] == "changed";
        c.Ok(new { cmd = "scene.new" }, "scene.new");
        bool fresh = Until(c, () => (string)UiQ(c, tf)?["value"] == "quiet", 5);
        c.Check(stale && fresh, "KU6 without Refresh the form keeps its values; a new scene reads it again", $"stale {stale}, after scene.new {S(UiQ(c, tf)?["value"])}");
        c.Ok(new { cmd = "formprobe.set", form = "panel", key = "t", value = "hello", refresh = true }, "restore t");
    }

    // ---------------------------------------------------------------- KU7 errors

    static void KU7(PlayCtx c)
    {
        ShowPanel(c, FormPanel);
        c.Ok(new { cmd = "formprobe.fail", read = "panel" }, "formprobe.fail read panel");
        c.Ok(new { cmd = "formprobe.set", form = "panel", key = "t", value = "hello", refresh = true }, "refresh");
        bool shown = Until(c, () => Shown(UiQ(c, FormBase + "-error")), 5);
        var err = UiQ(c, FormBase + "-error");
        var rows = UiQ(c, FormBase + "-rows");
        c.Check(shown && ((string)err?["text"] ?? "").Contains("fails on purpose") && !Shown(rows) && (bool?)rows?["enabled"] == false,
            "KU7 a failed read shows its error text in place of the rows (hidden, disabled)", S(err?["text"]));
        c.Ok(new { cmd = "formprobe.fail" }, "formprobe.fail none");
        c.Ok(new { cmd = "formprobe.set", form = "panel", key = "t", value = "hello", refresh = true }, "refresh");
        bool back = Until(c, () => !Shown(UiQ(c, FormBase + "-error")) && Shown(UiQ(c, FormBase + "-rows")), 5);
        c.Check(back, "KU7 the next good read shows the rows again");
        ShowPanel(c, FormGuid + "/deferred");
        var de = UiQ(c, "plugin-form-" + FormGuid + "/deferred-error");
        c.Check(((string)de?["text"] ?? "").Contains("cannot be called inline") && (int)FormState(c)["deferredReads"] == 0,
            "KU7 a read command that may answer later is refused inline (the form shows the refusal; the handler never ran)", S(de?["text"]));
        ShowPanel(c, FormPanel);
        var tail = c.Call(new { cmd = "log.tail", n = 1 });
        int since = (int?)(tail["entries"] as JArray)?.LastOrDefault()?["seq"] ?? 0;
        c.Ok(new { cmd = "formprobe.set", form = "panel", key = "f", value = "oops", refresh = true }, "f = \"oops\"");
        bool wrong = Until(c, () => HasClass(UiQ(c, FormBase + "-f"), "hs-plugin-form__row--wrong"), 5);
        c.Ok(new { cmd = "formprobe.set", form = "panel", key = "t", value = "hello", refresh = true }, "read again with the wrong value");
        c.WaitFrames(4);
        var warn = c.Call(new { cmd = "log.tail", n = 200, level = "warning", since });
        int lines = (warn["entries"] as JArray)?.OfType<JObject>().Count(e => ((string)e["message"] ?? "").Contains("[plugin " + FormGuid + "] form panel row f: a number was expected")) ?? 0;
        c.Check(wrong && lines == 1, "KU7 a value of the wrong shape marks the row (\"—\") and is logged once in the plugin's log", "wrong " + wrong + ", warnings " + lines);
        c.Ok(new { cmd = "formprobe.set", form = "panel", key = "f", value = 2.5, refresh = true }, "restore f");
        c.Ok(new { cmd = "formprobe.fail", write = "panel" }, "formprobe.fail write panel");
        OnMain(() =>
        {
            var t = UiHost.Current.Root.Q<Toggle>(FormBase + "-b-toggle");
            t.value = !t.value;
            return true;
        });
        var ts = c.Call(new { cmd = "ui.state" })["toasts"] as JArray;
        c.Check(ts?.Any(x => ((string)x ?? "").Contains("writes of panel fail on purpose")) == true, "KU7 a failed write is shown as a toast", PlayCtx.Short(S(ts)));
        c.Ok(new { cmd = "formprobe.fail" }, "formprobe.fail none");
        c.Ok(new { cmd = "formprobe.set", form = "panel", key = "b", value = true, refresh = true }, "restore b");
    }

    // ---------------------------------------------------------------- KU8 widgets, dialogs, menus, toasts

    static void KU8(PlayCtx c)
    {
        ShowPanel(c, FormGuid + "/widgets");
        var st = FormState(c);
        c.Check((bool?)st["widgetsBuilt"] == true && (bool?)st["widgetsAvailable"] == true, "KU8 the probe's IPluginUi panel is built with IPluginUi.Widgets", PlayCtx.Short(c.LastText));
        string W(string k) => WidgetBase + k;
        c.Check(Near(Num(UiQ(c, W("w.number-field"))), 3) && Near(Num(UiQ(c, W("w.vector-c2"))), 3) && (bool?)UiQ(c, W("w.toggle-toggle"))?["value"] == true &&
                HasClass(UiQ(c, W("w.choice-seg-1")), "hs-segmented__button--on") && (string)UiQ(c, W("w.color-swatch"))?["tooltip"] == "#FF0000FF" &&
                (string)UiQ(c, W("w.ref-ref"))?["text"] == "Beta" && (string)UiQ(c, W("w.text-text"))?["value"] == "abc" && UiQ(c, W("w.file-file")) != null &&
                Lx((string)UiQ(c, W("w.button-button"))?["text"], "Press", "押す") && UiQ(c, W("w.attach-picker")) != null,
            "KU8 the ten factories make host widgets showing their values (number, vector, toggle, choice, colour, reference, file, text, button, attach point)");
        int ev0 = (st["widgetEvents"] as JArray)?.Count ?? 0;
        Click(c, W("w.button-button"));
        Click(c, W("w.toggle-toggle"));
        var evs = (FormState(c)["widgetEvents"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
        c.Check(evs.Skip(ev0).Contains("button") && evs.Skip(ev0).Contains("toggle False"), "KU8 the callbacks run through ui.input (button, toggle)", string.Join(" | ", evs.Skip(ev0)));
        int ev1 = evs.Count;
        c.Ok(new { cmd = "formprobe.widget", value = 7 }, "formprobe.widget 7");
        c.WaitFrames(2);
        c.Check(Near(Num(UiQ(c, W("w.number-field"))), 7) && ((FormState(c)["widgetEvents"] as JArray)?.Count ?? 0) == ev1, "KU8 SetValue shows a value without a callback");
        c.Ok(new { cmd = "formprobe.dialog" }, "formprobe.dialog");
        c.WaitFrames(3);
        c.Check(Shown(UiQ(c, "dialog-plugin-" + FormGuid)) && UiQ(c, "formprobe-dialog-content") != null && Lx((string)UiQ(c, "dialog-button-1")?["text"], "Second", "2 番目"),
            "KU8 a host dialog with the plugin's content and its button labels");
        Click(c, "dialog-button-1");
        c.Check((int)FormState(c)["dialogClosed"] == 1 && !Shown(UiQ(c, "dialog-plugin-" + FormGuid)), "KU8 the dialog's callback gets the button index (1)", S(FormState(c)["dialogClosed"]));
        c.Ok(new { cmd = "formprobe.dialog", kind = "confirm" }, "formprobe.dialog confirm");
        c.WaitFrames(3);
        Click(c, "dialog-button-ok");
        c.Check((string)FormState(c)["confirmed"] == "ok", "KU8 Confirm: OK gives true", S(FormState(c)["confirmed"]));
        c.Ok(new { cmd = "formprobe.dialog", kind = "prompt" }, "formprobe.dialog prompt");
        c.WaitFrames(3);
        Click(c, "dialog-button-ok");
        c.Check((string)FormState(c)["prompted"] == "typed", "KU8 Prompt: OK gives the text", S(FormState(c)["prompted"]));
        c.Ok(new { cmd = "formprobe.menu" }, "formprobe.menu");
        c.WaitFrames(3);
        var m0 = UiQ(c, "menu-item-plugin-" + FormGuid + "-0");
        var m2 = UiQ(c, "menu-item-plugin-" + FormGuid + "-2");
        c.Check(Shown(UiQ(c, "plugin-menu-" + FormGuid)) && m0 != null && HasClass(m2, "hs-menu__item--disabled"), "KU8 a host popup menu (an entry, a separator, a disabled entry)");
        Click(c, "menu-item-plugin-" + FormGuid + "-0");
        c.Check((int)FormState(c)["menuRuns"] == 1, "KU8 a menu entry runs its action", S(FormState(c)["menuRuns"]));
        c.Ok(new { cmd = "formprobe.toast", text = "form probe toast 61" }, "formprobe.toast");
        var ts = c.Call(new { cmd = "ui.state" })["toasts"] as JArray;
        c.Check(ts?.Any(x => (string)x == "Info: form probe toast 61") == true, "KU8 a host toast", PlayCtx.Short(S(ts)));
    }

    // ---------------------------------------------------------------- KU9 HairBundles

    static void KU9(PlayCtx c)
    {
        var refusals = (FormState(c)["refusals"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
        var hair = refusals.FirstOrDefault(x => x.StartsWith("hair:", StringComparison.Ordinal));
        c.Check(hair != null && hair.Contains("HairBundles is reserved"), "KU9 a HairBundles row is refused at registration", hair ?? string.Join(" | ", refusals));
        var p = PluginOf(c.Call(new { cmd = "plugin.list" }), FormGuid);
        var ui = c.Call(new { cmd = "ui.state" });
        bool none = (ui["panels"] as JArray)?.OfType<JObject>().All(x => (string)x["id"] != FormGuid + "/hair") == true;
        c.Check((int?)p?["ui"]?["forms"] == 3 && none, "KU9 nothing of the refused form is registered (3 forms, no hair panel)", PlayCtx.Short(p?["ui"]?.ToString(Formatting.None)));
    }

    // ---------------------------------------------------------------- KU10 unload

    static bool HasPanel(PlayCtx c, string id) => (c.Call(new { cmd = "ui.state" })["panels"] as JArray)?.OfType<JObject>().Any(x => (string)x["id"] == id) == true;

    static void KU10(PlayCtx c)
    {
        ShowPanel(c, FormPanel);
        c.Check(Shown(UiQ(c, FormBase)) && UiQ(c, "tab-" + FormPanel) != null, "KU10 the form panel's tab is open");
        var d = c.Call(new { cmd = "plugin.disable", guid = FormGuid });
        if (!c.Check(PlayCtx.IsOk(d), "KU10 plugin.disable of the form probe", PlayCtx.Short(c.LastText))) return;
        c.WaitFrames(3);
        c.Check(!HasPanel(c, FormPanel) && !HasPanel(c, FormGuid + "/widgets") && UiQ(c, FormBase) == null && UiQ(c, "tab-" + FormPanel) == null,
            "KU10 the forms leave with the plugin: no panel in ui.state, the open tab and the form element gone");
        var p = PluginOf(c.Call(new { cmd = "plugin.list" }), FormGuid);
        c.Check((string)p?["state"] == "disabled" && ((int?)p?["ui"]?["forms"] ?? 0) == 0, "KU10 plugin.list: disabled, no forms", PlayCtx.Short(p?.ToString(Formatting.None)));
        var e = c.Call(new { cmd = "plugin.enable", guid = FormGuid });
        c.Check(PlayCtx.IsOk(e), "KU10 plugin.enable of the form probe", PlayCtx.Short(c.LastText));
        c.WaitFrames(3);
        p = PluginOf(c.Call(new { cmd = "plugin.list" }), FormGuid);
        c.Check((string)p?["state"] == "loaded" && (int?)p?["ui"]?["forms"] == 3 && HasPanel(c, FormPanel), "KU10 enabled again: loaded, 3 forms, the panel registered again",
            PlayCtx.Short(p?["ui"]?.ToString(Formatting.None)));
    }

    // ---------------------------------------------------------------- KE capture events (history probe)

    static JObject Cap(PlayCtx c) => c.Call(new { cmd = "historyprobe.state" })["capture"] as JObject;

    static bool WaitFinished(PlayCtx c, int before, double sec = 20) => Until(c, () => ((int?)Cap(c)?["finished"] ?? 0) > before, sec);

    static void KE1(PlayCtx c)
    {
        var c0 = Cap(c);
        if (!c.Check(c0 != null, "KE1 the history probe answers", PlayCtx.Short(c.LastText))) return;
        int f0 = (int)c0["finished"];
        string seq0 = (string)c0["sequence"];
        int frame0 = c.Frame();
        c.Ok(new { cmd = "ui.action", action = "capture.screenshot" }, "ui.action capture.screenshot");
        bool done = WaitFinished(c, f0);
        int frame1 = c.Frame();
        var k = Cap(c);
        string seq = ((string)k["sequence"]).Substring(seq0.Length);
        string path = (string)k["resultPath"];
        long size = path != null && File.Exists(path) ? new FileInfo(path).Length : -1;
        c.Check(done && seq == "SRF" && (string)k["origin"] == "Ui" && (string)k["kind"] == "Screenshot" && (bool)k["ok"] && (bool)k["sameInstance"],
            "KE1 ui.action capture.screenshot: Starting, Rendered, Finished once each in that order; origin Ui, kind Screenshot, ok, one CaptureInfo", PlayCtx.Short(k.ToString(Formatting.None)));
        c.Check(size > 0 && size == (long)k["bytes"] && string.Equals(Path.GetFullPath(path), Path.GetFullPath((string)k["path"]), StringComparison.OrdinalIgnoreCase),
            "KE1 Path = the written file, Bytes = its size", path + " " + size + " B");
        c.Check((int)k["width"] > 0 && (int)k["renderWidth"] == (int)k["width"] * (int)k["supersample"] && (int)k["renderHeight"] == (int)k["height"] * (int)k["supersample"],
            "KE1 sizes: " + k["width"] + " × " + k["height"] + ", supersample " + k["supersample"] + ", render " + k["renderWidth"] + " × " + k["renderHeight"]);
        c.Info("KE1 frames from the request to the observed CaptureFinished: " + (frame1 - frame0) + " (render frame " + k["frameNumber"] + ")");
    }

    static void KE2(PlayCtx c)
    {
        int f0 = (int)Cap(c)["finished"];
        var r = c.Call(new { cmd = "screenshot", mode = "custom", width = 320, height = 180, supersample = 2 });
        bool done = WaitFinished(c, f0);
        var k = Cap(c);
        c.Check(PlayCtx.IsOk(r) && done && (string)k["origin"] == "Command" && (string)k["kind"] == "Screenshot" && (int)k["width"] == 320 && (int)k["height"] == 180 &&
                (int)k["renderWidth"] == 640 && (int)k["renderHeight"] == 360 && (bool)k["ok"],
            "KE2 the screenshot command: origin Command, 320 × 180, render 640 × 360, ok", PlayCtx.Short(k.ToString(Formatting.None)));
    }

    static void KE3(PlayCtx c)
    {
        int f0 = (int)Cap(c)["finished"];
        string last0 = OnMain(() => HS2Next.UI.Panels.ScreenshotModule.LastFile);
        var r = c.Call(new { cmd = "capture", width = 320, height = 180 });
        bool done = WaitFinished(c, f0);
        var k = Cap(c);
        string last1 = OnMain(() => HS2Next.UI.Panels.ScreenshotModule.LastFile);
        string rp = (string)r["path"];
        c.Check(PlayCtx.IsOk(r) && done && (string)k["kind"] == "Capture" && (string)k["origin"] == "Command" && rp != null &&
                string.Equals(Path.GetFullPath(rp), Path.GetFullPath((string)k["resultPath"] ?? ""), StringComparison.OrdinalIgnoreCase) && (int)k["frameNumber"] == (int)r["frame"],
            "KE3 the capture command: kind Capture, origin Command, Path = the reply's path, FrameNumber = the reply's frame", PlayCtx.Short(k.ToString(Formatting.None)) + " reply " + PlayCtx.Short(S(r)));
        c.Check(last0 == last1, "KE3 CaptureService.Saved is not raised by the capture command (the screenshot module's last file unchanged)", (last0 ?? "null") + " / " + (last1 ?? "null"));
    }

    static void KE4(PlayCtx c)
    {
        var c0 = Cap(c);
        var r = c.Call(new { cmd = "screenshot", mode = "custom", width = 320, height = 180, supersample = 3 });
        c.WaitFrames(5);
        var k = Cap(c);
        c.Check(!PlayCtx.IsOk(r) && (int)k["starting"] == (int)c0["starting"] && (int)k["finished"] == (int)c0["finished"],
            "KE4 a refused screenshot (supersample 3) raises no event", "code " + r["code"]);
    }

    static void KE6(PlayCtx c)
    {
        int f0 = (int)Cap(c)["finished"];
        c.Ok(new { cmd = "historyprobe.arm", what = "requestInCapture" }, "historyprobe.arm requestInCapture");
        var r = c.Call(new { cmd = "capture", width = 320, height = 180 });
        WaitFinished(c, f0);
        var k = Cap(c);
        c.Check(PlayCtx.IsOk(r) && (string)k["requestInCapture"] == "busy", "KE6 a Request of obj.transform inside CaptureStarting answers busy", S(k["requestInCapture"]));
        c.Check((bool)k["hiddenInRender"] && (bool)k["shownAtRendered"], "KE7 the probe hid its own object in CaptureStarting and showed it again at CaptureRendered",
            "hidden " + k["hiddenInRender"] + ", shown " + k["shownAtRendered"]);
    }

    // ---------------------------------------------------------------- KD7 the Options toggle

    static void KD7(PlayCtx c)
    {
        string toggle = "setting-plugin-" + FormGuid;
        c.Ok(new { cmd = "ui.action", action = "file.options" }, "ui.action file.options");
        c.WaitFrames(4);
        Click(c, "options-tab-automation", 4);
        var t = UiQ(c, toggle);
        if (!c.Check(Shown(t) && (bool?)t?["value"] == true, "KD7 the Options' Automation section shows the form probe's toggle on", PlayCtx.Short(S(t)))) { Key(c, "Escape"); return; }
        Click(c, toggle, 4);
        var p = PluginOf(c.Call(new { cmd = "plugin.list" }), FormGuid);
        t = UiQ(c, toggle);
        var restart = UiQ(c, "settings-restart-automation");
        c.Check((string)p?["state"] == "disabled" && ((string)t?["label"] ?? "").Contains("(disabled)") && !Shown(restart),
            "KD7 off: disabled at once, the toggle says so, no \"after a restart\" mark", "state " + p?["state"] + ", label " + S(t?["label"]) + ", restart shown " + Shown(restart));
        Click(c, toggle, 4);
        p = PluginOf(c.Call(new { cmd = "plugin.list" }), FormGuid);
        t = UiQ(c, toggle);
        c.Check((string)p?["state"] == "loaded" && ((string)t?["label"] ?? "").Contains("(loaded)") && (int?)p?["ui"]?["forms"] == 3 && !Shown(UiQ(c, "settings-restart-automation")),
            "KD7 on: loaded at once with its forms", "state " + p?["state"] + ", label " + S(t?["label"]));
        Click(c, "dialog-button-close", 3);
    }

    // ---------------------------------------------------------------- KB device checks (body probe, package B)

    static string FemaleCard(PlayCtx c)
    {
        var t = JObject.Parse(File.ReadAllText(Path.Combine(c.ProjectRoot, "Tools", "targets.json")));
        string rel = (string)t["visual"]?["female"] ?? "";
        const string prefix = "chara/female/";
        return rel.StartsWith(prefix, StringComparison.Ordinal) ? rel.Substring(prefix.Length) : rel;
    }

    static string KbLoad(PlayCtx c, string what)
    {
        var r = c.Call(new { cmd = "chara.load", path = FemaleCard(c), sex = "female" }, 180000);
        var a = r["added"]?[0] as JObject;
        if (!c.Check(PlayCtx.IsOk(r) && (string)a?["state"] == "Built", what + ": chara.load BR-Chan built", PlayCtx.Short(c.LastText))) return null;
        return (string)a["key"];
    }

    static string BodySha(PlayCtx c, string key)
    {
        var r = c.Call(new { cmd = "chara.edit.rtstats", key, part = "body" }, 120000);
        return (string)(r["creators"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x["name"] == "colour")?["sha256"];
    }

    static (int layers, int rebuilds, int flushes, JObject info) BodyInfo(PlayCtx c, string key)
    {
        var info = c.Call(new { cmd = "chara.edit.info", key });
        var cr = (info["creators"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x["name"] == "create_skin_body");
        return ((int?)cr?["layers"] ?? -1, (int?)cr?["rebuilds"] ?? -1, (int?)info["flushes"] ?? -1, info);
    }

    static JObject Body(PlayCtx c) => c.Call(new { cmd = "bodyprobe.state" });

    static string KbA, KbFree;
    static (int layers, int rebuilds, int flushes, JObject info) KbI0;

    static void KB1(PlayCtx c)
    {
        c.Ok(new { cmd = "bodyprobe.config" }, "bodyprobe.config {} (inert)");
        string k1 = KbLoad(c, "KB1 plugin-free");
        if (k1 == null) return;
        KbFree = k1;
        KbA = BodySha(c, k1);
        KbI0 = BodyInfo(c, k1);
        c.Ok(new { cmd = "bodyprobe.config", png = true }, "bodyprobe.config png");
        string k2 = KbLoad(c, "KB1 with the PNG layer");
        if (k2 == null) return;
        string b = BodySha(c, k2);
        var i1 = BodyInfo(c, k2);
        var st = Body(c);
        c.Check(KbA != null && b != null && b != KbA, "KB1 the drawn body colour differs with the plugin's layer (read-back SHA-256)", (KbA ?? "null").Substring(0, Math.Min(12, (KbA ?? "null").Length)) + " vs " + (b ?? "null").Substring(0, Math.Min(12, (b ?? "null").Length)));
        c.Check(i1.layers == KbI0.layers + 1 && i1.rebuilds == KbI0.rebuilds && i1.flushes == KbI0.flushes, "KB1 layers +1, the body creator's rebuilds and the flushes as the plugin-free build",
            $"layers {KbI0.layers} -> {i1.layers}, rebuilds {KbI0.rebuilds} / {i1.rebuilds}, flushes {KbI0.flushes} / {i1.flushes}");
        c.Check((int?)st["builds"]?[k2] == 1 && st["rebuilds"]?[k2] == null, "KB1 one CharaBuilt, no rebuild", PlayCtx.Short(c.LastText));
        c.Ok(new { cmd = "delete", key = k2 }, "delete");
    }

    static void KB5(PlayCtx c)
    {
        if (KbA == null) { c.Check(false, "KB5 needs KB1's plugin-free numbers"); return; }
        c.Ok(new { cmd = "bodyprobe.config", png = true, body = true, normals = "asset" }, "bodyprobe.config png + body + normals");
        string k3 = KbLoad(c, "KB5");
        if (k3 == null) return;
        var st = Body(c);
        string sha = BodySha(c, k3);
        var i = BodyInfo(c, k3);
        var bust = c.Call(new { cmd = "chara.edit.bust", key = k3 });
        var src = bust["normals"]?["source"] as JObject;
        c.Check((int?)st["builds"]?[k3] == 1 && st["rebuilds"]?[k3] == null && (int?)st["bodyMeshRuns"] == 1, "KB5 one build pass: CharaBuilt once, no rebuild, BodyMesh ran once", PlayCtx.Short(S(st)));
        c.Check(sha != null && sha != KbA && i.flushes == KbI0.flushes && i.layers == KbI0.layers + 1, "KB5 the layer composited and the copy drawn in that pass (flushes as plugin-free)",
            $"flushes {i.flushes}/{KbI0.flushes}, layers {i.layers}/{KbI0.layers}");
        c.Check((bool?)bust["normals"]?["applied"] == true && (string)src?["kind"] == "replaced" && (string)src?["hook"] == BodyGuid + "/parts" && (string)src?["guid"] == "o_body_cf",
            "KB5 the supplied normals blended (source replaced, hook stp3.test.bodyprobe/parts, o_body_cf)", PlayCtx.Short(S(bust["normals"]?["source"])));
        c.Ok(new { cmd = "delete", key = k3 }, "delete");
    }

    static void KB7(PlayCtx c)
    {
        c.Ok(new { cmd = "bodyprobe.config" }, "bodyprobe.config {}");
        string k4 = KbLoad(c, "KB7");
        if (k4 == null) return;
        string sc = BodySha(c, k4);
        int l0 = BodyInfo(c, k4).layers;
        var set = c.Call(new { cmd = "bodyprobe.layer", node = k4, op = "set", layer = "outside", form = "png" });
        c.WaitFrames(2);
        string s1 = BodySha(c, k4);
        var i1 = BodyInfo(c, k4);
        var units = (i1.info["lastFlush"]?["units"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
        c.Check((bool?)set["result"] == true && (set["ids"] as JArray)?.Select(x => (string)x).SequenceEqual(new[] { "outside" }) == true,
            "KB7 SkinLayers(node).Set outside a build", PlayCtx.Short(S(set)));
        c.Check(s1 != null && s1 != sc && i1.layers == l0 + 1 && units.Contains("ext.textures") && Body(c)["rebuilds"]?[k4] == null,
            "KB7 the next flush re-composites (drawn colour changed, layers +1, units ext.textures), no rebuild", $"layers {l0} -> {i1.layers}; units {string.Join(",", units)}");
        var rem = c.Call(new { cmd = "bodyprobe.layer", node = k4, op = "remove", layer = "outside" });
        c.WaitFrames(2);
        string s2 = BodySha(c, k4);
        c.Check((bool?)rem["result"] == true && s2 == sc && BodyInfo(c, k4).layers == l0, "KB7 Remove: back to the drawn colour and the layers before", (s2 == sc) + ", layers " + BodyInfo(c, k4).layers);
        c.Ok(new { cmd = "delete", key = k4 }, "delete");
        if (KbFree != null) c.Call(new { cmd = "delete", key = KbFree });
        c.Ok(new { cmd = "bodyprobe.config" }, "bodyprobe.config {} (inert again)");
    }
}

// The ui.* bridge commands (U31; owner core, Since 0.2.0; HS2Next-only names):
//   ui.state       {enabled, language, preset, tool, space, pivot, snap, layout {regions {size, collapsed, panels, active,
//                   shown, worldBound, strip}, version, migratedFrom, resizeRebuilds, autoShows}, screen, root, panels [...], focus, modal,
//                   selection {selected, primary}, sub, imeComposing, fps, firstFrameMs, gizmo {...}, viewport {...}, toasts}
//                   (4.3: gizmo.handles [{sub, kind marker / axis, panel [x, y]}] — the hair bundle handles)
//                   (4.6: makerFile {node, primary, multiDisabled, preview {width, height, framing, renderedAtFrame, …},
//                   loadDialog {open, sex, flags, selected, loadEnabled, …}, thumbnailFraming, loadFlags} — the File page)
//                   (v0.3 trial 2: thumbs {pending, headerChecks, assetLoads, fromBundles, fromDisk, failed, failReasons,
//                   headerRejected, headerUnchecked, guardSkipped, unloadsDeferred, openBundles, …} — the Maker list thumbnails)
//   ui.action      action (the spec's `id`: an envelope field), args → {ran, result}; unknown id → bad_args; CanExecute false → unavailable
//   ui.panel       panel (the spec's `id`), op show / hide / focus / move, region → {panel}
//   ui.query       name ("name", "#name" or ".class"), all → [{name, type, text, value, enabled, visible, worldBound, screenRect}]
//   ui.input       events [{type key / text / pointer / wheel, key, mods, text, x, y (panel px) or target + ox / oy, button,
//                   phase down / move / up, delta}], frames → {sent, consumed}; one event per frame, then `frames` frames;
//                   Delete / Backspace / Ctrl+A edit a focused TextField (4.2)
//   ui.screenshot  name → {path} (the window with the UI, ScreenCapture at the end of a frame; needs graphics)
// Registered with the Core commands (CoreCommands: one Group line and the name list) and answer `unavailable` while no UI
// runs (-noUi, test hosts). Events are synthesised through the panel (visualTree.SendEvent with pooled events), never the OS.
// Phase 5.3: gate None. While the setup host runs (the first-run guide, the progress page of a long scan) ui.state answers
// {enabled, mode "setup", page, pagesSeen, outcome, language, focus, modal, toasts}, ui.query and ui.input work on its panel,
// ui.action takes an element name of the guide (a button is pressed; a text field gets args.value; a toggle args.value) or
// ui.language {language}, ui.panel answers unavailable and ui.screenshot captures the window; while the app starts with the
// UI on and no panel is up yet the commands answer not_ready.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HS2Next.Api.Bridge;
using HS2Next.App;
using HS2Next.App.Commands;
using HS2Next.UI.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Bridge
{
    public static partial class UiCommands
    {
        /// <summary>The command names (CoreCommands.AllNames).</summary>
        public static readonly IReadOnlyList<string> Names = new[] { "ui.state", "ui.action", "ui.panel", "ui.query", "ui.input", "ui.screenshot" };

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("ui.state", "The Studio UI's state: language, key preset, tool / space / pivot / snap, layout and panels, focus, modal dialog, selection, IME composition, FPS (unavailable without the UI).", "ui")
                .Return("enabled", ArgTypes.Bool, "").Return("layout", ArgTypes.Object, "regions {size, collapsed, panels, active}")
                .Return("panels", ArgTypes.ArrayOf(ArgTypes.Object), "{id, region, visible, active}").Return("focus", ArgTypes.String, "focused element name or null")
                .Return("modal", ArgTypes.String, "open dialog id or null")
                .WithGate(ReadyGate.None).WithFlags(CommandFlags.ReadOnly | CommandFlags.Experimental), State);
            CommandKit.Register(r, app, CommandKit.Spec("ui.action", "Runs a UI action by id (the same behaviour as its key, menu and toolbar button).", "ui")
                .Arg("action", ArgTypes.String, "an action id (edit.undo, tool.move, file.saveAs, add.female, …); `id` is the envelope's request id", true)
                .Arg("args", ArgTypes.Object, "action arguments (file.saveAs {name|path}, file.open {path}, add.female {path}, add.item {ref}, ui.language {language}, edit.show {node, page, section, part (the hair part), kind (the clothes kind), slot (the accessory build slot, 0-based)}, …)")
                .Return("ran", ArgTypes.Bool, "").Return("result", ArgTypes.Any, "the action's result")
                .Example("{\"cmd\":\"ui.action\",\"action\":\"tool.rotate\"}")
                .WithGate(ReadyGate.None).WithFlags(CommandFlags.Mutates | CommandFlags.Experimental), Action);
            CommandKit.Register(r, app, CommandKit.Spec("ui.panel", "Shows, hides, focuses or moves a panel of the fixed layout.", "ui")
                .Arg("panel", ArgTypes.String, "panel id (workspace, inspector, add.chara, …, scene, history)", true)
                .Arg("op", ArgTypes.Enum("show", "hide", "focus", "move"), "show (and focus) / hide / focus / move to region", true)
                .Arg("region", ArgTypes.Enum("left", "right", "bottom"), "for move")
                .Return("panel", ArgTypes.Object, "{id, region, visible, active}")
                .WithGate(ReadyGate.None).WithFlags(CommandFlags.Mutates | CommandFlags.Experimental), Panel);
            CommandKit.Register(r, app, CommandKit.Spec("ui.query", "Finds UI elements by name (\"name\" or \"#name\") or class (\".class\"): text, value, enabled, visible, bounds.", "ui")
                .Arg("name", ArgTypes.String, "element name, #name or .class", true)
                .Arg("all", ArgTypes.Bool, "every match (default: the first)", false, false)
                .Return("elements", ArgTypes.ArrayOf(ArgTypes.Object), "{name, type, text, value, enabled, visible, tooltip, worldBound [x, y, w, h] (panel px), screenRect [x, y, w, h] (window px, y up)}")
                .WithGate(ReadyGate.None).WithFlags(CommandFlags.ReadOnly | CommandFlags.Experimental), Query);
            CommandKit.Register(r, app, CommandKit.Spec("ui.input", "Synthesises key, text, pointer and wheel events through the UI panel (one event per frame), then waits `frames` frames. Key events Delete / Backspace / Ctrl+A edit a focused text field as typing does.", "ui")
                .Arg("events", ArgTypes.ArrayOf(ArgTypes.Object), "[{type: key|text|pointer|wheel, key, mods [ctrl, shift, alt], text, x, y (panel px) or target (element name) + ox, oy, button (0 left, 1 right, 2 middle), phase down|move|up, delta}]", true)
                .Arg("frames", ArgTypes.Int, "frames to wait after the last event", false, 1)
                .Return("sent", ArgTypes.Int, "").Return("consumed", ArgTypes.Int, "events whose propagation was stopped (a handler took them)")
                .WithGate(ReadyGate.None).WithFlags(CommandFlags.Mutates | CommandFlags.Defers | CommandFlags.Experimental), Input);
            CommandKit.Register(r, app, CommandKit.Spec("ui.screenshot", "A screenshot of the window with the UI (shots/ui_<name>.png in the output root; needs graphics).", "ui")
                .Arg("name", ArgTypes.String, "file name stem", false, "ui")
                .Return("path", ArgTypes.Path, "")
                .WithGate(ReadyGate.None).WithFlags(CommandFlags.ReadOnly | CommandFlags.Defers | CommandFlags.Experimental), Screenshot);
        }

        static UiHost Need(CoreContext c)
        {
            var h = Host;
            if (h == null || h.Context == null || !h.IsReady)
            {
                var app = c?.App;
                // the UI is on for this app but its panel is not up yet (the boot runs, or a stage failed before the scene)
                if (app != null && app.Settings?.Ui?.Run == true && !app.IsTestHost && app.Stage != AppStage.Ready)
                    throw new BridgeException(new BridgeError(BridgeCodes.NotReady, "the UI is not up yet (stage " + app.StageName + (app.Stage == AppStage.Failed ? ", failed: " + app.Error : "") + ")"));
                throw new BridgeException(new BridgeError(CommandKit.Unavailable, "the Studio UI is not running (-noUi, a headless run, or still starting)"));
            }
            return h;
        }

        // ---------------------------------------------------------------- ui.state

        static object State(CoreContext c)
        {
            if (SetupActive) return SetupState();
            var h = Host;
            if (h == null || h.Context == null || !h.IsReady) return new BridgeReply().With("enabled", false);
            var ui = h.Context;
            var regions = new Dictionary<string, object>();
            var panels = new List<object>();
            foreach (var kv in ui.Shell.Regions)
            {
                var r = kv.Value;
                var lr = ui.Shell.Layout.Region(kv.Key);
                regions[kv.Key] = new Dictionary<string, object>
                {
                    ["size"] = lr?.Size, ["collapsed"] = r.Collapsed, ["panels"] = r.Panels.ToList(), ["active"] = r.Active,
                    // the region as laid out now (a small window shrinks it below the stored size) and its tab strip
                    ["shown"] = Math.Round(ui.Shell.SizeOf(r), 2), ["worldBound"] = Rect(r.Pane.worldBound), ["strip"] = Rect(r.Strip.worldBound),
                };
                foreach (var p in r.Panels) panels.Add(new Dictionary<string, object> { ["id"] = p, ["region"] = kv.Key, ["visible"] = !r.Collapsed && r.Active == p, ["active"] = r.Active == p });
            }
            foreach (var p in ui.Shell.Layout.Hidden) panels.Add(new Dictionary<string, object> { ["id"] = p, ["region"] = null, ["visible"] = false, ["active"] = false });
            var f = h.Router.Focused;
            var vp = ui.Viewport;
            var cam = vp.ViewCamera;
            var gz = vp.Gizmo;
            return new BridgeReply()
                .With("enabled", true)
                .With("language", ui.L10n.Language)
                .With("preset", ui.Keys.PresetName)
                .With("keyboard", ui.Keys.Layout.ToString())
                .With("tool", ui.Tool.ToString().ToLowerInvariant())
                .With("space", ui.Space.ToString().ToLowerInvariant())
                .With("pivot", ui.Pivot == GizmoPivot.Individual ? "individual" : "centre")
                .With("snap", ui.Snap)
                .With("layout", new Dictionary<string, object>
                {
                    ["regions"] = regions, ["version"] = LayoutDocument.CurrentVersion, ["migratedFrom"] = ui.Shell.MigratedFromVersion,
                    ["resizeRebuilds"] = ui.Shell.ResizeRebuilds, ["autoShows"] = ui.Shell.AutoShows,
                })
                .With("screen", new[] { Screen.width, Screen.height })
                .With("root", Rect(h.Root != null ? h.Root.worldBound : default))
                .With("panels", panels)
                .With("focus", f == null ? null : string.IsNullOrEmpty(f.name) ? f.GetType().Name : f.name)
                .With("focusKind", h.Router.FocusKindOf(f).ToString())
                .With("modal", ui.Dialogs.Current?.Spec.Id)
                .With("menuOpen", ui.Menus != null && ui.Menus.IsOpen)
                .With("selection", new Dictionary<string, object> { ["selected"] = ui.Selection.Ids.ToList(), ["primary"] = ui.Selection.Primary })
                .With("sub", ui.SubSelection)
                .With("imeComposing", h.Router.ImeComposing)
                .With("fps", Math.Round(ui.Shell.StatusBar.FpsValue, 1))
                .With("firstFrameMs", Math.Round(h.FirstFrameMs, 1))
                .With("panelScale", vp.PanelScale)
                .With("userScale", h.UserScaleApplied)
                .With("file", ui.CurrentFile)
                .With("dirty", ui.Session.Commands.IsDirty)
                .With("viewport", new Dictionary<string, object>
                {
                    ["worldBound"] = Rect(vp.Element.worldBound),
                    ["cameraRect"] = cam ? new[] { cam.rect.x, cam.rect.y, cam.rect.width, cam.rect.height } : null,
                    ["pointerOver"] = vp.PointerOver,
                    ["pick"] = vp.Picker.Last.NodeId, ["pickMs"] = Math.Round(vp.Picker.Last.Ms, 3),
                })
                .With("gizmo", new Dictionary<string, object>
                {
                    ["visible"] = gz.Visible, ["tool"] = gz.ShownTool.ToString().ToLowerInvariant(), ["dragging"] = gz.Dragging,
                    ["hover"] = gz.HoverHandle.ToString(), ["pivot"] = V(gz.PivotWorld), ["length"] = gz.LengthWorld,
                    ["pivotPanel"] = V2(gz.ToPanel(gz.PivotWorld)),
                    // Phase 4.3 package W: hair bundle markers and the shown handle's axis tips
                    ["handles"] = gz.HairHandleStates(),
                })
                // Phase 4.6 package W: the Edit tab's File page (thumbnail preview, character load dialog, the two view-state options)
                .With("makerFile", HS2Next.UI.Panels.CharaEditTab.FileUiState(ui))
                // v0.3 trial 2: the Maker lists' part thumbnails (pending loads, sources, refusals; null before a page used them)
                .With("thumbs", HS2Next.UI.Panels.Maker.PartThumbnails.UiState(ui.App?.Catalog))
                // v0.3 trial 2 package R: the pose browser's scan (list shown, names filled in, index hits; null before its first show)
                .With("poses", HS2Next.UI.Panels.PoseBrowser.UiState())
                .With("keys", h.Router.Log.Skip(Math.Max(0, h.Router.Log.Count - 8)).ToList())
                .With("toasts", ui.Toasts.History.Skip(Math.Max(0, ui.Toasts.History.Count - 5)).ToList());
        }

        static double[] Rect(Rect r) => new[] { Math.Round(r.x, 2), Math.Round(r.y, 2), Math.Round(r.width, 2), Math.Round(r.height, 2) };
        static double[] V(Vector3 v) => new double[] { v.x, v.y, v.z };
        static double[] V2(Vector3 v) => new double[] { Math.Round(v.x, 2), Math.Round(v.y, 2) };

        // ---------------------------------------------------------------- ui.action

        static object Action(CoreContext c)
        {
            if (SetupActive) return SetupAction(c);
            var h = Need(c);
            var ui = h.Context;
            string id = c.Args.Require<string>("action");
            var args = new Dictionary<string, object>(StringComparer.Ordinal);
            if (c.Args.Has("args"))
            {
                var raw = c.Args.RawJson("args");
                if (!string.IsNullOrEmpty(raw) && JToken.Parse(raw) is JObject o)
                    foreach (var p in o.Properties()) args[p.Name] = Plain(p.Value);
            }
            if (!ui.Registry.Actions.Contains(id))
            {
                var known = ui.Registry.Actions.All.Select(a => a.Id).Where(x => x.StartsWith(id.Split('.')[0], StringComparison.Ordinal)).Take(20).ToList();
                throw CommandKit.BadArgs("action", "a registered action id", "unknown action \"" + id + "\"" + (known.Count > 0 ? " (" + string.Join(", ", known) + ")" : ""));
            }
            var st = ui.Registry.RunWith(id, new ActionInvocation { Source = "bridge", Args = args }, out var result);
            if (st == ActionRunStatus.Unavailable) throw new BridgeException(new BridgeError(CommandKit.Unavailable, "action " + id + " is not available now"));
            if (st == ActionRunStatus.NoBehaviour) throw CommandKit.Unsupported("action " + id + " has no behaviour");
            return new BridgeReply().With("ran", st == ActionRunStatus.Ran).With("result", Plainify(result));
        }

        static object Plain(JToken t)
        {
            switch (t.Type)
            {
                case JTokenType.String: return (string)t;
                case JTokenType.Integer: return (long)t;
                case JTokenType.Float: return (double)t;
                case JTokenType.Boolean: return (bool)t;
                case JTokenType.Null: return null;
                case JTokenType.Array: return t.Select(Plain).ToList();
                case JTokenType.Object: return ((JObject)t).Properties().ToDictionary(p => p.Name, p => Plain(p.Value));
                default: return t.ToString();
            }
        }

        static object Plainify(object o)
        {
            switch (o)
            {
                case null: return null;
                case string s: return s;
                case bool b: return b;
                case int or long or float or double: return o;
                case Scenes.Runtime.OpResult r: return new Dictionary<string, object> { ["ok"] = r.Ok, ["status"] = r.Status.ToString(), ["code"] = r.Code, ["message"] = r.Message, ["newIds"] = r.NewIds.ToList() };
                default: return o.ToString();
            }
        }

        // ---------------------------------------------------------------- ui.panel

        static object Panel(CoreContext c)
        {
            if (SetupActive) throw new BridgeException(new BridgeError(CommandKit.Unavailable, "the setup guide runs: there are no panels yet"));
            var ui = Need(c).Context;
            string id = c.Args.Require<string>("panel");
            string op = c.Args.Require<string>("op");
            if (!ui.Registry.Panels.TryGet(id, out _))
                throw CommandKit.NotFound("no panel \"" + id + "\"", "panel", ui.Registry.Panels.All.Select(p => p.Id).ToList());
            switch (op)
            {
                case "show": ui.Shell.SetPanelVisible(id, true); ui.Shell.FocusPanel(id); break;
                case "hide": ui.Shell.SetPanelVisible(id, false); break;
                case "focus": ui.Shell.FocusPanel(id); break;
                case "move":
                    string region = c.Args.Require<string>("region");
                    if (!LayoutRegions.IsRegion(region)) throw CommandKit.BadArgs("region", ArgTypes.Enum("left", "right", "bottom"));
                    ui.Shell.MovePanel(id, region);
                    break;
                default: throw CommandKit.BadArgs("op", ArgTypes.Enum("show", "hide", "focus", "move"));
            }
            string reg = ui.Shell.RegionOf(id);
            var r = reg != null ? ui.Shell.Regions[reg] : null;
            return new BridgeReply().With("panel", new Dictionary<string, object> { ["id"] = id, ["region"] = reg, ["visible"] = r != null && !r.Collapsed && r.Active == id, ["active"] = r != null && r.Active == id });
        }

        // ---------------------------------------------------------------- ui.query

        static object Query(CoreContext c)
        {
            string name = c.Args.Require<string>("name");
            bool all = c.Args.Get("all", false);
            if (SetupActive)
            {
                var sl = Find(Setup.Root, name, all).Select(e => Describe(Setup.Focused, e)).ToList();
                return new BridgeReply().With("elements", sl).With("count", sl.Count).With("mode", "setup");
            }
            var h = Need(c);
            var list = Find(h.Root, name, all).Select(e => Describe(h.Router.Focused, e)).ToList();
            return new BridgeReply().With("elements", list).With("count", list.Count);
        }

        /// <summary>Elements by "name", "#name" or ".class" (the first, or all).</summary>
        public static List<VisualElement> Find(VisualElement root, string sel, bool all)
        {
            var l = new List<VisualElement>();
            if (root == null || string.IsNullOrEmpty(sel)) return l;
            bool cls = sel.StartsWith(".", StringComparison.Ordinal);
            string key = sel.TrimStart('#', '.');
            var q = cls ? root.Query(className: key) : root.Query(name: key);
            q.ForEach(e => { if (all || l.Count == 0) l.Add(e); });
            return l;
        }

        static Dictionary<string, object> Describe(VisualElement focused, VisualElement e)
        {
            string text = e is TextElement te ? te.text : e is Foldout fo ? fo.text : null;
            object value = null;
            var vp = e.GetType().GetProperty("value");
            if (vp != null && vp.GetIndexParameters().Length == 0)
            {
                try
                {
                    var v = vp.GetValue(e);
                    value = v is float f ? (object)(double)f : v is Vector3 v3 ? new double[] { v3.x, v3.y, v3.z } : v is Color col ? new double[] { col.r, col.g, col.b, col.a } : v is string || v is bool || v is int || v is double || v is long ? v : v?.ToString();
                }
                catch (Exception) { }
            }
            string label = null;
            var lp = e.GetType().GetProperty("label");
            if (lp != null && lp.PropertyType == typeof(string)) { try { label = (string)lp.GetValue(e); } catch (Exception) { } }
            var wb = e.worldBound;
            float s = e.panel != null ? e.panel.scaledPixelsPerPoint : 1f;
            return new Dictionary<string, object>
            {
                ["name"] = e.name, ["type"] = e.GetType().Name, ["text"] = text, ["label"] = label, ["value"] = value,
                ["enabled"] = e.enabledInHierarchy, ["visible"] = IsVisible(e), ["focused"] = focused == e,
                ["classes"] = e.GetClasses().ToList(),
                ["worldBound"] = Rect(wb), ["screenRect"] = new[] { Math.Round(wb.x * s, 1), Math.Round(Screen.height - wb.yMax * s, 1), Math.Round(wb.width * s, 1), Math.Round(wb.height * s, 1) },
                ["children"] = e.childCount,
                ["tooltip"] = string.IsNullOrEmpty(e.tooltip) ? null : e.tooltip,
            };
        }

        static bool IsVisible(VisualElement e)
        {
            if (e.panel == null) return false;
            for (var x = e; x != null; x = x.hierarchy.parent)
                if (x.resolvedStyle.display == DisplayStyle.None || x.resolvedStyle.visibility == Visibility.Hidden || !x.visible) return false;
            var wb = e.worldBound;
            return wb.width > 0 && wb.height > 0;
        }

        // ---------------------------------------------------------------- ui.input

        static object Input(CoreContext c)
        {
            bool setup = SetupActive;
            var sh = setup ? Setup : null;
            var h = setup ? null : Need(c);
            var raw = c.Args.RawJson("events");
            JArray events;
            try { events = JArray.Parse(raw ?? "[]"); }
            catch (Exception) { throw CommandKit.BadArgs("events", ArgTypes.ArrayOf(ArgTypes.Object)); }
            int frames = Math.Max(0, c.Args.Get("frames", 1));
            var d = c.Ctx.Defer();
            var frameSvc = c.Frames;
            int sent = 0, consumed = 0;
            var errors = new List<string>();
            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i] as JObject;
                int k = i;
                frameSvc.AtUpdate(k + 1, () =>
                {
                    if (d.IsCompleted || d.IsAbandoned || (setup ? sh == null || sh.Closed : Host == null)) return;
                    try
                    {
                        if (ev == null) { errors.Add("event " + k + " is not an object"); return; }
                        bool took;
                        if (setup ? SendTo(sh.Root, sh.Focused, KeyboardLayout.Us, null, ev, out took) : Send(h, ev, out took)) sent++;
                        if (took) consumed++;
                    }
                    catch (Exception e) { errors.Add("event " + k + ": " + e.Message); }
                });
            }
            frameSvc.AtUpdate(events.Count + 1 + frames, () =>
            {
                if (d.IsCompleted || d.IsAbandoned) return;
                if (errors.Count > 0 && sent == 0) { d.Fail(BridgeError.BadArgs("events", ArgTypes.ArrayOf(ArgTypes.Object), string.Join("; ", errors))); return; }
                d.Complete(new BridgeReply().With("sent", sent).With("consumed", consumed).With("errors", errors));
            });
            return null;
        }

        static EventModifiers Mods(JToken t)
        {
            var m = EventModifiers.None;
            if (t is JArray a)
                foreach (var x in a)
                    switch (((string)x ?? "").ToLowerInvariant())
                    {
                        case "ctrl": case "control": m |= EventModifiers.Control; break;
                        case "shift": m |= EventModifiers.Shift; break;
                        case "alt": m |= EventModifiers.Alt; break;
                    }
            else if (t != null && t.Type == JTokenType.String)
                foreach (var x in ((string)t).Split('+')) m |= Mods(new JArray(x));
            return m;
        }

        /// <summary>Sends one synthetic event through the panel (true when sent; took = a handler stopped its propagation).</summary>
        public static bool Send(UiHost h, JObject ev, out bool took) =>
            SendTo(h.Root, h.Router.Focused, h.Context.Keys.Layout, () => h.Router.Ran, ev, out took);

        /// <summary>Sends one synthetic event to a panel's root (<paramref name="focused"/> takes key and text events; <paramref name="ran"/>
        /// counts the shortcut actions run, or null).</summary>
        public static bool SendTo(VisualElement root, VisualElement focused, KeyboardLayout layout, Func<int> ran, JObject ev, out bool took)
        {
            took = false;
            var panel = root?.panel;
            if (panel == null) return false;
            string type = ((string)ev["type"] ?? "").ToLowerInvariant();
            var mods = Mods(ev["mods"]);
            int ranBefore = ran?.Invoke() ?? 0;
            switch (type)
            {
                case "key":
                {
                    string key = (string)ev["key"];
                    if (!Enum.TryParse(KeyNames.ToUiToolkit(KeyNames.Canonical(key) ?? key, layout), out KeyCode kc)) throw new ArgumentException("unknown key " + key);
                    string phase = ((string)ev["phase"] ?? "press").ToLowerInvariant();
                    var target = (focused ?? root);
                    var textField = IsTextEditKey(kc, mods) ? FocusedTextField(focused) : null;
                    if ((phase == "down" || phase == "press") && textField != null) took |= SendTextEditKey(target, textField, kc, mods);
                    else if (phase == "down" || phase == "press")
                        using (var e = KeyDownEvent.GetPooled('\0', kc, mods)) { target.SendEvent(e); took |= e.isPropagationStopped; }
                    if (phase == "up" || phase == "press")
                        using (var e = KeyUpEvent.GetPooled('\0', kc, mods)) target.SendEvent(e);
                    took |= ran != null && ran() > ranBefore;
                    return true;
                }
                case "text":
                {
                    var target = (focused ?? root);
                    foreach (char ch in (string)ev["text"] ?? "")
                        using (var e = KeyDownEvent.GetPooled(ch, KeyCode.None, mods)) target.SendEvent(e);
                    return true;
                }
                case "pointer":
                case "wheel":
                {
                    Vector2 pos;
                    if (ev["target"] != null)
                    {
                        var t = Find(root, (string)ev["target"], false).FirstOrDefault();
                        if (t == null) throw new ArgumentException("no element " + ev["target"]);
                        var wb = t.worldBound;
                        pos = ev["ox"] != null || ev["oy"] != null ? new Vector2(wb.xMin + (float)(ev["ox"] ?? 0), wb.yMin + (float)(ev["oy"] ?? 0)) : wb.center;
                    }
                    else pos = new Vector2((float)(ev["x"] ?? 0), (float)(ev["y"] ?? 0));
                    if (type == "wheel")
                    {
                        float dy = (float)(ev["delta"] ?? 0);
                        var se = new Event { type = EventType.ScrollWheel, mousePosition = pos, delta = new Vector2(0, dy), modifiers = mods };
                        using (var e = WheelEvent.GetPooled(se)) { panel.visualTree.SendEvent(e); }
                        return true;
                    }
                    int button = (int)(ev["button"] ?? 0);
                    string phase = ((string)ev["phase"] ?? "click").ToLowerInvariant();
                    int clicks = (int)(ev["clicks"] ?? 1);
                    void Do(EventType et)
                    {
                        var se = new Event { type = et, mousePosition = pos, button = button, clickCount = clicks, modifiers = mods };
                        if (et == EventType.MouseDown) using (var e = PointerDownEvent.GetPooled(se)) panel.visualTree.SendEvent(e);
                        else if (et == EventType.MouseUp) using (var e = PointerUpEvent.GetPooled(se)) panel.visualTree.SendEvent(e);
                        else using (var e = PointerMoveEvent.GetPooled(se)) panel.visualTree.SendEvent(e);
                    }
                    if (phase == "down") Do(EventType.MouseDown);
                    else if (phase == "move") Do(EventType.MouseDrag);
                    else if (phase == "up") Do(EventType.MouseUp);
                    else { Do(EventType.MouseDown); Do(EventType.MouseUp); }
                    return true;
                }
                default: throw new ArgumentException("unknown event type \"" + type + "\" (key, text, pointer, wheel)");
            }
        }

        // ---- text-editing keys (Phase 4.2 package W): Delete / Backspace and Ctrl+A on a focused
        // TextField edit it as a typed key does. A KeyDownEvent pooled from a key code alone carries no IMGUI event, which UI
        // Toolkit's text editor needs for its editing commands (4.1's UI4: the keys reached the field without editing it), so
        // the event is built from an IMGUI key event; when the field's text and selection are still unchanged afterwards, the
        // edit is applied through the field itself (selection, then the text: a ChangeEvent follows as after typing).
        static bool IsTextEditKey(KeyCode kc, EventModifiers mods) =>
            kc == KeyCode.Backspace || kc == KeyCode.Delete || (kc == KeyCode.A && (mods & (EventModifiers.Control | EventModifiers.Command)) != 0);

        /// <summary>The TextField holding the focus (the focused element or its ancestor), or null.</summary>
        static TextField FocusedTextField(VisualElement focused) => focused as TextField ?? focused?.GetFirstAncestorOfType<TextField>();

        /// <summary>Sends a text-editing key to a focused TextField (true when the field's text or selection changed).</summary>
        static bool SendTextEditKey(VisualElement target, TextField tf, KeyCode kc, EventModifiers mods)
        {
            string before = tf.value ?? "";
            int c0 = tf.cursorIndex, s0 = tf.selectIndex;
            var imgui = new Event { type = EventType.KeyDown, keyCode = kc, modifiers = mods, character = '\0' };
            using (var e = KeyDownEvent.GetPooled(imgui)) target.SendEvent(e);
            if ((tf.value ?? "") != before || tf.cursorIndex != c0 || tf.selectIndex != s0) return true;   // the field edited itself
            if (kc == KeyCode.A) { tf.SelectAll(); return true; }
            if (tf.isReadOnly) return false;
            int a = Mathf.Clamp(Math.Min(c0, s0), 0, before.Length), b = Mathf.Clamp(Math.Max(c0, s0), 0, before.Length);
            string text;
            int cur = a;
            if (b > a) text = before.Remove(a, b - a);
            else if (kc == KeyCode.Backspace) { if (a == 0) return false; text = before.Remove(a - 1, 1); cur = a - 1; }
            else { if (a >= before.Length) return false; text = before.Remove(a, 1); }
            tf.value = text;
            tf.SelectRange(cur, cur);
            return true;
        }

        // ---------------------------------------------------------------- ui.screenshot

        static object Screenshot(CoreContext c)
        {
            MonoBehaviour h = SetupActive ? (MonoBehaviour)Setup : Need(c);
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw CommandKit.Unsupported("no graphics device (-nographics): no screenshot");
            if (Application.isBatchMode && Application.isEditor)
                throw CommandKit.Unsupported("ui.screenshot needs a window: an Editor batch session never reaches the end of a frame (use the player)");
            string name = c.Args.Get("name", "ui");
            foreach (char ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            string full = c.Output.Resolve("shots/ui_" + name + ".png", overwrite: true);
            var d = c.Ctx.Defer();
            h.StartCoroutine(Shot(d, full));
            return null;
        }

        static IEnumerator Shot(IDeferredReply d, string full)
        {
            yield return new WaitForEndOfFrame();
            if (d.IsCompleted || d.IsAbandoned) yield break;
            try
            {
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                var png = tex.EncodeToPNG();
                int w = tex.width, hh = tex.height;
                UnityEngine.Object.Destroy(tex);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllBytes(full, png);
                d.Complete(new BridgeReply().With("path", full).With("width", w).With("height", hh));
            }
            catch (Exception e) { d.Fail(new BridgeError(BridgeCodes.Internal, "screenshot failed: " + e.Message)); }
        }

        static partial void TickPendingImpl(UiHost h) { }
    }
}

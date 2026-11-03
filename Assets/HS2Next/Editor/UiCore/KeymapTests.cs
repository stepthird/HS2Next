// KeymapTests: chord parse / format round trip; both presets load and bind only manifest
// actions; no conflict within a scope; every manifest action has a label key; Appendix C spot checks; overrides
// (keymap.json) round trip; the resolution table (text focus, IME composing, modal, scopes, exact modifiers, repeat,
// two-stroke, held actions with speed modifiers, modifier holds; ≥ 60 cases).
using System;
using System.Collections.Generic;
using System.Linq;

namespace HS2Next.UI.Core.Tests
{
    using HS2Next.Save.Tests;
    using HS2Next.UI.Core;

    public static class KeymapTests
    {
        public static void Run(TestRunner t)
        {
            Chords(t);
            var presets = Presets(t);
            if (presets == null) return;
            AppendixC(t, presets["default"], presets["hs2studio"]);
            Overrides(t, presets["default"]);
            Resolution(t, presets["default"], presets["hs2studio"]);
            Held(t, presets["default"], presets["hs2studio"]);
            Registry(t);
        }

        static void Chords(TestRunner t)
        {
            foreach (var s in new[] { "Ctrl+Z", "Ctrl+Shift+Z", "Ctrl+Shift+Alt+F12", "Shift+Space", "Alpha1", "Keypad5", "Ctrl+WheelUp", "WheelDown",
                         "Ctrl+K Ctrl+C", "Ctrl", "Shift", "Ctrl+Shift", "Semicolon", "Backslash", "Ctrl+Plus", "UpArrow", "Alt+LeftArrow" })
            {
                bool ok = Chord.TryParse(s, out var c, out string err);
                t.Check(ok, "chord: parse " + s + (ok ? "" : ": " + err));
                if (ok) t.Equal(s, c.ToString(), "chord: format round trip " + s);
            }
            // aliases and modifier order normalise
            var alias = new Dictionary<string, string>
            {
                ["shift+ctrl+z"] = "Ctrl+Shift+Z", ["Control+Del"] = "Ctrl+Delete", ["Esc"] = "Escape", ["Enter"] = "Return",
                ["ctrl+up"] = "Ctrl+UpArrow", ["↓"] = "DownArrow", ["PgUp"] = "PageUp", ["Num5"] = "Keypad5", ["Num 8"] = "Keypad8",
                ["1"] = "Alpha1", ["Ctrl+0"] = "Ctrl+Alpha0", [";"] = "Semicolon", ["="] = "Equals", ["Ctrl++"] = "Ctrl+Plus",
                ["Ctrl+Wheel Up"] = "Ctrl+WheelUp", ["alt+shift+h"] = "Shift+Alt+H", ["  Ctrl+S  "] = "Ctrl+S", ["Ctrl+k  ctrl+c"] = "Ctrl+K Ctrl+C",
            };
            foreach (var kv in alias)
            {
                bool ok = Chord.TryParse(kv.Key, out var c, out string err);
                t.Check(ok && c.ToString() == kv.Value, "chord: \"" + kv.Key + "\" → " + kv.Value + (ok ? " (got " + c + ")" : ": " + err));
            }
            foreach (var bad in new[] { "", "Ctrl+", "Ctrl+Ctrl+Z", "Z+Ctrl", "A+B", "Hyper+Z", "Ctrl+LeftShift", "Foo", "Ctrl+K Ctrl+C Ctrl+D", "Ctrl Ctrl+C" })
                t.Check(!Chord.TryParse(bad, out _, out _), "chord: \"" + bad + "\" is rejected");
            t.Equal("Ctrl+↑", Chord.Parse("Ctrl+UpArrow").ToDisplay(), "chord: display arrow");
            t.Equal("Num 5", Chord.Parse("Keypad5").ToDisplay(), "chord: display keypad");
            t.Equal("Ctrl+K, Ctrl+C", Chord.Parse("Ctrl+K Ctrl+C").ToDisplay(), "chord: display two strokes");
            t.Equal("1", Chord.Parse("Alpha1").ToDisplay(), "chord: display digit");
            // JIS keycaps of the stored (legacy Input) KeyCodes (Spike S8)
            var jis = new Dictionary<string, string> { ["Semicolon"] = ";", ["Quote"] = ":", ["Backslash"] = "]", ["RightBracket"] = "[", ["LeftBracket"] = "@", ["Equals"] = "^", ["JisBackslash"] = "¥ / ろ", ["Period"] = ".", ["Slash"] = "/", ["Minus"] = "-", ["Comma"] = "," };
            foreach (var kv in jis) t.Equal(kv.Value, Chord.Parse(kv.Key).ToDisplay(KeyboardLayout.Jis), "chord: JIS label of " + kv.Key);
            t.Equal("=", Chord.Parse("Equals").ToDisplay(), "chord: US label of Equals");
            t.Equal("Ctrl+:", Chord.Parse("Ctrl+Quote").ToDisplay(KeyboardLayout.Jis), "chord: JIS label with a modifier");
            t.Equal("JisBackslash", Chord.Parse("¥").ToString(), "chord: ¥ alias = JisBackslash");
            UiToolkitMapping(t);
            t.Equal("Ctrl+↑", Chord.Parse("Ctrl+UpArrow").ToDisplay(KeyboardLayout.Jis), "chord: unmeasured keys keep their label on JIS");
            t.Check(Chord.Parse("Ctrl").IsModifierOnly && !Chord.Parse("Ctrl+A").IsModifierOnly, "chord: modifier-only");
            t.Check(Chord.Parse("ctrl+z") == Chord.Parse("Ctrl+Z") && Chord.Parse("Z") != Chord.Parse("Ctrl+Z"), "chord: equality");
            int all = 0, rt = 0;
            foreach (var k in KeyNames.All)
            {
                all++;
                if (Chord.TryParse("Ctrl+" + k, out var c, out _) && c.ToString() == "Ctrl+" + k) rt++;
            }
            t.Equal(all, rt, "chord: every key name round-trips (" + all + ")");
        }

        /// <summary>UI Toolkit keyCode → stored key (one check per row) and the inverse.</summary>
        static void UiToolkitMapping(TestRunner t)
        {
            var s8 = new (string jis, string uitk, string legacy)[]
            {
                ("; (re) 0x27", "Equals", "Semicolon"),
                (": (ke) 0x28", "Semicolon", "Quote"),
                ("] (mu) 0x2B", "RightBracket", "Backslash"),
                ("[ 0x1B", "LeftBracket", "RightBracket"),
                ("@ 0x1A", "BackQuote", "LeftBracket"),
                ("^ (he) 0x0D", "Quote", "Equals"),
                ("yen 0x7D", "Backslash", KeyNames.JisBackslash),
                ("\\ (ro) 0x73", "Backslash", KeyNames.JisBackslash),
                ("/ 0x35", "Slash", "Slash"),
                (". 0x34", "Period", "Period"),
                ("- 0x0C", "Minus", "Minus"),
                (", 0x33", "Comma", "Comma"),
            };
            foreach (var r in s8)
            {
                t.Equal(r.legacy, KeyNames.FromUiToolkit(r.uitk, KeyboardLayout.Jis), "S8 " + r.jis + ": UI Toolkit " + r.uitk + " → " + r.legacy);
                t.Equal(r.uitk, KeyNames.ToUiToolkit(r.legacy, KeyboardLayout.Jis), "S8 " + r.jis + ": inverse " + r.legacy + " → " + r.uitk);
            }
            foreach (var k in new[] { "Equals", "Semicolon", "Backslash", "Quote", "W", "UpArrow", "Alpha1", "F2" })
                t.Equal(k, KeyNames.FromUiToolkit(k, KeyboardLayout.Us), "US layout: " + k + " unchanged");
            foreach (var k in new[] { "W", "UpArrow", "Alpha1", "Keypad5", "Return", "Space", "F11" })
                t.Equal(k, KeyNames.FromUiToolkit(k, KeyboardLayout.Jis), "JIS layout: non-OEM key " + k + " unchanged");
            t.Equal("Backslash", KeyNames.ToUiToolkit(KeyNames.JisBackslash, KeyboardLayout.Us), "inverse of JisBackslash on US");
            t.Check(!KeyNames.IsPollable(KeyNames.JisBackslash) && !KeyNames.IsPollable(KeyNames.WheelUp) && KeyNames.IsPollable("Semicolon"), "pollable: JisBackslash and the wheel are not");
            t.Equal(null, KeyNames.FromUiToolkit(null, KeyboardLayout.Jis), "null stays null");
            // the vanilla JIS keys reach their hs2studio actions through the mapping (K5 physical keys)
            var h = new ShortcutResolver(Keymap.FromJson(UiKit.ReadText(t, "Keymaps/hs2studio.json")));
            var phys = new (string uitk, string action)[]
            {
                ("Equals", ActionIds.CameraFovDown), ("Semicolon", ActionIds.CameraFovReset), ("RightBracket", ActionIds.CameraFovUp),
                ("Period", ActionIds.CameraRollPlus), ("Slash", ActionIds.CameraRollReset), ("Backslash", ActionIds.CameraRollMinus),
            };
            foreach (var p in phys)
            {
                var r = h.Resolve(new KeyInput(KeyNames.FromUiToolkit(p.uitk, KeyboardLayout.Jis)) { PointerOverViewport = true });
                t.Check(r.ActionId == p.action, "hs2studio JIS: UI Toolkit " + p.uitk + " → " + p.action + " (got " + r + ")");
            }
        }

        static Dictionary<string, Keymap> Presets(TestRunner t)
        {
            var d = new Dictionary<string, Keymap>();
            foreach (var name in Keymap.PresetNames)
            {
                var problems = new List<string>();
                Keymap k;
                try { k = Keymap.FromJson(UiKit.ReadText(t, "Keymaps/" + name + ".json"), problems); }
                catch (Exception e) { t.Check(false, name + " loads: " + e.Message); return null; }
                t.Check(problems.Count == 0, name + ": no parse problems (" + string.Join("; ", problems) + ")");
                t.Equal(name, k.Name, name + ": name");
                var v = k.Validate();
                t.Check(v.Count == 0, name + ": valid, manifest ids only (" + string.Join("; ", v) + ")");
                var c = k.Conflicts();
                t.Check(c.Count == 0, name + ": no conflicts within a scope (" + string.Join("; ", c) + ")");
                var back = Keymap.FromJson(k.ToJson());
                t.Check(k.DiffTo(back).Bindings.Count == 0 && back.Notes.Count == k.Notes.Count, name + ": JSON round trip");
                t.Check(k.Notes.Keys.All(ActionManifest.Contains), name + ": notes name manifest actions");
                d[name] = k;
            }
            var noLabel = ActionManifest.All.Where(a => string.IsNullOrEmpty(a.LabelKey)).ToList();
            t.Equal(0, noLabel.Count, "manifest: every action has a label key");
            t.Check(ActionManifest.All.Count >= 100, "manifest: " + ActionManifest.All.Count + " actions");
            // a conflict is found when present
            var bad = d["default"].Clone("bad");
            bad.Set(ActionIds.ToolMove, "Q");
            t.Check(bad.Conflicts().Any(x => x.Chord.ToString() == "Q" && x.Scope == ActionScope.Global), "conflict: tool.move = Q collides with tool.select");
            var prefix = d["default"].Clone("bad2");
            prefix.Set(ActionIds.HelpAbout, "Ctrl+N Ctrl+A");
            t.Check(prefix.Conflicts().Any(x => x.What == "prefix"), "conflict: a two-stroke chord starting with Ctrl+N collides with file.new");
            var cross = d["default"].Clone("ok");
            cross.Set(ActionIds.CameraNextObject, "Q");   // Viewport vs Global tool.select: precedence, not a conflict
            t.Check(cross.Conflicts().Count == 0, "conflict: bindings of different scopes do not conflict");
            var inv = new Keymap("inv");
            inv.Set("no.such", "Z");
            inv.Set(ActionIds.Undo, "Ctrl");
            inv.Set(ActionIds.CameraMoveLeft, "Ctrl+K Ctrl+L");
            t.Equal(3, inv.Validate().Count, "validate: unknown id, modifier-only trigger, two-stroke held");
            return d;
        }

        static string Chords(Keymap k, string id) => string.Join(", ", k.ChordsOf(id).Select(c => c.ToString()));

        static void AppendixC(TestRunner t, Keymap d, Keymap h)
        {
            var expect = new (string id, string def, string hs2)[]
            {
                (ActionIds.Undo, "Ctrl+Z", "Z, Ctrl+Z"),
                (ActionIds.Redo, "Ctrl+Y, Ctrl+Shift+Z", "Shift+Z, Ctrl+Y, Ctrl+Shift+Z"),
                (ActionIds.Duplicate, "Ctrl+D", "Ctrl+D"),
                (ActionIds.Delete, "Delete", "Delete"),
                (ActionIds.SelectAll, "Ctrl+A", "Ctrl+A"),
                (ActionIds.Deselect, "Escape", "Escape"),
                (ActionIds.Rename, "F2", ""),
                (ActionIds.CopyTransform, "Ctrl+Shift+C", ""),
                (ActionIds.PasteTransform, "Ctrl+Shift+V", ""),
                (ActionIds.MoveToPivot, "Shift+C", "C"),
                (ActionIds.ToolSelect, "Q", ""),
                (ActionIds.ToolMove, "W", "W"),
                (ActionIds.ToolRotate, "E", "E"),
                (ActionIds.ToolScale, "R", "R"),
                (ActionIds.ToolSpace, "X", ""),
                (ActionIds.ToolPivot, "Period", ""),
                (ActionIds.ToolSnapToggle, "Shift+S", ""),
                (ActionIds.ViewGizmos, "Ctrl+Shift+G", "Q"),
                (ActionIds.ViewPlanes, "", "J"),
                (ActionIds.ViewCentre, "", "K"),
                (ActionIds.ViewPanels, "Shift+Space", "Shift+Space"),
                (ActionIds.ViewHideSelected, "H", ""),
                (ActionIds.ViewShowAll, "Alt+H", ""),
                (ActionIds.CameraFrame, "F", "F"),
                (ActionIds.CameraReset, "Home", "A"),
                (ActionIds.CameraResetAngles, "Shift+Home", "Keypad5"),
                (ActionIds.CameraRollReset, "", "Slash"),
                (ActionIds.CameraFovReset, "Ctrl+Home", "Quote"),   // default: Ctrl+Home since the field-of-view work after 1.1.0
                (ActionIds.CameraMoveForward, "UpArrow", "UpArrow"),
                (ActionIds.CameraMoveUp, "PageUp", "PageUp"),
                (ActionIds.CameraDollyIn, "Ctrl+UpArrow", "Home"),
                (ActionIds.CameraDollyOut, "Ctrl+DownArrow", "End"),
                (ActionIds.CameraOrbitUp, "Alt+UpArrow", "Keypad8"),
                (ActionIds.CameraOrbitRight, "Alt+RightArrow", "Keypad6"),
                (ActionIds.CameraRollPlus, "", "Period"),
                (ActionIds.CameraRollMinus, "", "JisBackslash"),
                (ActionIds.CameraFovDown, "Ctrl+WheelUp", "Semicolon"),
                (ActionIds.CameraFovUp, "Ctrl+WheelDown", "Backslash"),
                (ActionIds.CameraSlotLoad(0), "Alpha1", "Alpha1"),
                (ActionIds.CameraSlotLoad(9), "Alpha0", "Alpha0"),
                (ActionIds.CameraSlotSave(0), "Ctrl+Alpha1", ""),
                (ActionIds.CameraSlotSave(9), "Ctrl+Alpha0", ""),
                (ActionIds.CameraNextObject, "", "H"),
                (ActionIds.FileNew, "Ctrl+N", ""),
                (ActionIds.FileOpen, "Ctrl+O", ""),
                (ActionIds.FileSave, "Ctrl+S", "Ctrl+S"),
                (ActionIds.FileSaveAs, "Ctrl+Shift+S", ""),
                (ActionIds.HelpKeys, "F1", "F1, F2"),
                (ActionIds.AppQuit, "", ""),
                (ActionIds.ClickParent, "", "P"),
                (ActionIds.ClickParentAll, "", "Ctrl+P"),
                (ActionIds.ClickDetach, "", "O"),
                (ActionIds.ClickDetachAll, "", "Ctrl+O"),
                (ActionIds.ClickCopyTransform, "", "Ctrl+X"),
                (ActionIds.GizmoSnapHold, "Ctrl", "V"),
                (ActionIds.GizmoPrecisionHold, "Shift", "Shift"),
                (ActionIds.GizmoResize, "", "B"),
                // Phase 3.6
                (ActionIds.TimePause, "Space", "Space"),
                (ActionIds.TimeStep, "", ""),
                (ActionIds.CaptureScreenshot, "F11", "F11"),
                (ActionIds.CaptureDialog, "Shift+F11", "Shift+F11"),
                (ActionIds.RoutePlay, "", ""),
                (ActionIds.ViewRoutes, "", ""),
            };
            int bad = 0;
            foreach (var e in expect)
            {
                string gd = Chords(d, e.id), gh = Chords(h, e.hs2 == null ? e.id : e.id);
                if (gd != e.def) { bad++; t.Check(false, "appendix C default " + e.id + ": " + gd + " (expected " + e.def + ")"); }
                if (gh != e.hs2) { bad++; t.Check(false, "appendix C hs2studio " + e.id + ": " + gh + " (expected " + e.hs2 + ")"); }
            }
            t.Check(bad == 0, "appendix C: " + expect.Length + " actions × 2 presets as the table (H proposal of K1-K10: no binding change)");
            // H's proposal rows 1, 3, 4, 8 and the K-facts are documented in the preset's notes
            foreach (var id in new[] { ActionIds.Redo, ActionIds.Deselect, ActionIds.SelectAll, ActionIds.MoveToPivot, ActionIds.TreePrev, ActionIds.CameraRollMinus,
                         ActionIds.CameraFovDown, ActionIds.CameraFovReset, ActionIds.CameraRollPlus, ActionIds.CameraRollReset, ActionIds.GizmoSnapHold, ActionIds.ToolMove,
                         ActionIds.TimePause, ActionIds.CaptureScreenshot })
                t.Check(h.Notes.ContainsKey(id), "hs2studio: note for " + id + " (H proposal / K1-K6)");
            t.Check(h.Notes.TryGetValue(ActionIds.Deselect, out var dn) && dn.Contains("U18"), "hs2studio: Esc keeps U18 (never quits) against vanilla's quit dialog");
            // the vanilla key list is complete in hs2studio
            foreach (var key in new[] { "Shift+Z", "Z", "F", "C", "Ctrl+S", "Ctrl+D", "Delete", "W", "E", "R", "Q", "J", "K", "F2", "H", "A", "Keypad5", "Slash", "Semicolon",
                         "Home", "End", "LeftArrow", "RightArrow", "UpArrow", "DownArrow", "PageUp", "PageDown", "Period", "Backslash", "Keypad8", "Keypad2", "Keypad4", "Keypad6",
                         "Quote", "Backslash", "JisBackslash", "Alpha1", "Alpha0", "F11" })
                t.Check(h.ActionsFor(Chord.Parse(key).First).Any(), "hs2studio: vanilla key " + key + " is bound");
        }

        static void Overrides(TestRunner t, Keymap preset)
        {
            var user = preset.Clone();
            user.Set(ActionIds.Rename, "F2", "Ctrl+R");
            user.Set(ActionIds.ToolSpace);   // unbound
            user.Set(ActionIds.CameraNextObject, "Ctrl+H");
            var diff = preset.DiffTo(user);
            t.Equal(3, diff.Bindings.Count, "overrides: only the three changed actions are stored");
            t.Equal("default", diff.Preset, "overrides: preset name");
            var json = diff.ToJson().Replace("\"bindings\"", "\"future\": {\"x\": 1}, \"bindings\"");
            var back = KeymapOverrides.FromJson(json);
            t.Check(back.ToJson().Contains("\"future\""), "overrides: unknown members kept");
            var applied = preset.WithOverrides(back);
            t.Equal("F2, Ctrl+R", Chords(applied, ActionIds.Rename), "overrides: changed chords applied");
            t.Equal("", Chords(applied, ActionIds.ToolSpace), "overrides: [] unbinds");
            t.Equal("Ctrl+Z", Chords(applied, ActionIds.Undo), "overrides: others keep the preset");
            var probs = new List<string>();
            KeymapOverrides.FromJson("{\"version\":1,\"preset\":\"default\",\"bindings\":{\"edit.undo\":[\"Ctrl+\", 3]}}", probs);
            t.Equal(2, probs.Count, "overrides: bad chords reported (" + string.Join("; ", probs) + ")");
        }

        // ---------------- resolution ----------------

        sealed class Case
        {
            public string Name, Preset, Key;
            public KeyMods Mods;
            public bool Repeat, Ime, Modal, Pointer, ViewportFocus;
            public FocusKind Focus;
            public ActionScope Scope = ActionScope.Global;
            public string Pending;
            public ShortcutOutcome Outcome;
            public string Action;
        }

        static Case C(string name, string preset, string key, ShortcutOutcome outcome, string action = null, KeyMods mods = KeyMods.None,
            bool repeat = false, bool ime = false, bool modal = false, bool pointer = false, FocusKind focus = FocusKind.None,
            ActionScope scope = ActionScope.Global, bool vpFocus = false, string pending = null) =>
            new Case { Name = name, Preset = preset, Key = key, Outcome = outcome, Action = action, Mods = mods, Repeat = repeat, Ime = ime, Modal = modal,
                Pointer = pointer, Focus = focus, Scope = scope, ViewportFocus = vpFocus, Pending = pending };

        static void Resolution(TestRunner t, Keymap d, Keymap h)
        {
            const string D = "default", H = "hs2studio";
            const KeyMods Ctrl = KeyMods.Ctrl, Shift = KeyMods.Shift, Alt = KeyMods.Alt, CS = KeyMods.Ctrl | KeyMods.Shift;
            var T = ShortcutOutcome.Trigger;
            var N = ShortcutOutcome.None;
            var Hd = ShortcutOutcome.Held;
            var Bl = ShortcutOutcome.Blocked;
            var cases = new List<Case>
            {
                // plain triggers, exact modifiers (rule 5)
                C("undo", D, "Z", T, ActionIds.Undo, Ctrl),
                C("Z alone does not undo (default)", D, "Z", N),
                C("Z undoes (hs2studio)", H, "Z", T, ActionIds.Undo),
                C("Ctrl+Z undoes (hs2studio)", H, "Z", T, ActionIds.Undo, Ctrl),
                C("Shift+Z redoes (hs2studio)", H, "Z", T, ActionIds.Redo, Shift),
                C("Ctrl+Shift+Z redoes (default)", D, "Z", T, ActionIds.Redo, CS),
                C("Ctrl+Y redoes", D, "Y", T, ActionIds.Redo, Ctrl),
                C("Alt+Z nothing (exact match)", D, "Z", N, null, Alt),
                C("Ctrl+Alt+Z nothing", H, "Z", N, null, Ctrl | Alt),
                C("Ctrl+C is not move-to-pivot (hs2, exact)", H, "C", N, null, Ctrl),
                C("C moves to pivot (hs2)", H, "C", T, ActionIds.MoveToPivot),
                C("Shift+C moves to pivot (default)", D, "C", T, ActionIds.MoveToPivot, Shift),
                C("C alone nothing (default)", D, "C", N),
                C("W move tool", D, "W", T, ActionIds.ToolMove),
                C("Shift+W nothing", D, "W", N, null, Shift),
                C("Q select tool (default)", D, "Q", T, ActionIds.ToolSelect),
                C("Q gizmos (hs2)", H, "Q", T, ActionIds.ViewGizmos),
                C("Ctrl+Shift+G gizmos (default)", D, "G", T, ActionIds.ViewGizmos, CS),
                C("Delete", D, "Delete", T, ActionIds.Delete),
                C("Ctrl+D duplicate", H, "D", T, ActionIds.Duplicate, Ctrl),
                C("Ctrl+S save", D, "S", T, ActionIds.FileSave, Ctrl),
                C("Ctrl+Shift+S save as", D, "S", T, ActionIds.FileSaveAs, CS),
                C("Ctrl+Shift+S nothing (hs2)", H, "S", N, null, CS),
                C("F1 help (default)", D, "F1", T, ActionIds.HelpKeys),
                C("F2 help (hs2)", H, "F2", T, ActionIds.HelpKeys),
                C("F2 rename (default)", D, "F2", T, ActionIds.Rename),
                C("Esc deselects", D, "Escape", T, ActionIds.Deselect),
                C("Esc deselects (hs2: never quits, U18)", H, "Escape", T, ActionIds.Deselect),
                C("Shift+Space panels", D, "Space", T, ActionIds.ViewPanels, Shift),
                C("Space alone pauses (3.6 R21; nothing in 3.5)", D, "Space", T, ActionIds.TimePause),
                C("unbound key", D, "F9", N),
                C("modifier key alone", D, "LeftControl", N, null, Ctrl),
                // scopes (rule 4)
                C("F in Global scope does not reach Viewport actions", D, "F", N),
                C("F frames with the pointer over the viewport", D, "F", T, ActionIds.CameraFrame, pointer: true),
                C("F frames with the viewport focused", D, "F", T, ActionIds.CameraFrame, vpFocus: true),
                C("H hides (Global) in default", D, "H", T, ActionIds.ViewHideSelected),
                C("H next camera over the viewport (hs2)", H, "H", T, ActionIds.CameraNextObject, pointer: true),
                C("H nothing away from the viewport (hs2)", H, "H", N),
                C("1 loads slot 0 over the viewport", D, "Alpha1", T, ActionIds.CameraSlotLoad(0), pointer: true),
                C("0 loads slot 9 over the viewport", H, "Alpha0", T, ActionIds.CameraSlotLoad(9), pointer: true),
                C("Ctrl+1 saves slot 0 (default)", D, "Alpha1", T, ActionIds.CameraSlotSave(0), Ctrl, pointer: true),
                C("Ctrl+1 nothing (hs2)", H, "Alpha1", N, null, Ctrl, pointer: true),
                C("Up arrow in the tree = previous row", D, "UpArrow", T, ActionIds.TreePrev, scope: ActionScope.Tree, pointer: true),
                C("Up arrow over the viewport = held camera move", D, "UpArrow", Hd, ActionIds.CameraMoveForward, pointer: true),
                C("Up arrow elsewhere = nothing", D, "UpArrow", N),
                C("Right arrow in the tree expands", H, "RightArrow", T, ActionIds.TreeExpand, scope: ActionScope.Tree),
                C("hs2: Up arrow over the viewport moves the camera (H proposal 2)", H, "UpArrow", Hd, ActionIds.CameraMoveForward, pointer: true),
                C("hs2: Up arrow with the tree focused = tree.prev", H, "UpArrow", T, ActionIds.TreePrev, scope: ActionScope.Tree, pointer: true),
                C("hs2: Up arrow, viewport focused = camera", H, "UpArrow", Hd, ActionIds.CameraMoveForward, vpFocus: true),
                C("hs2: Ctrl+Y redoes (HS2Next-only)", H, "Y", T, ActionIds.Redo, Ctrl),
                C("hs2: Ctrl+A selects all (HS2Next-only)", H, "A", T, ActionIds.SelectAll, Ctrl, pointer: true),
                C("hs2: JIS yen / ro key rolls minus", H, "JisBackslash", Hd, ActionIds.CameraRollMinus, pointer: true),
                C("hs2: Slash resets roll", H, "Slash", T, ActionIds.CameraRollReset, pointer: true),
                C("hs2: JIS ] key (legacy Backslash) widens the FOV", H, "Backslash", Hd, ActionIds.CameraFovUp, pointer: true),
                C("W in the tree still switches the tool (Global)", D, "W", T, ActionIds.ToolMove, scope: ActionScope.Tree),
                C("Inspector scope falls back to Global", D, "Z", T, ActionIds.Undo, Ctrl, scope: ActionScope.Inspector),
                C("A resets the view (hs2, viewport)", H, "A", T, ActionIds.CameraReset, pointer: true),
                C("Home resets the view (default, viewport)", D, "Home", T, ActionIds.CameraReset, pointer: true),
                C("Home is a held dolly (hs2)", H, "Home", Hd, ActionIds.CameraDollyIn, pointer: true),
                C("Keypad5 resets angles (hs2)", H, "Keypad5", T, ActionIds.CameraResetAngles, pointer: true),
                C("JIS : key (legacy Quote) resets FOV (hs2)", H, "Quote", T, ActionIds.CameraFovReset, pointer: true),
                C("Period toggles the pivot (default, Global)", D, "Period", T, ActionIds.ToolPivot, pointer: true),
                C("Period rolls (hs2, viewport)", H, "Period", Hd, ActionIds.CameraRollPlus, pointer: true),
                C("Ctrl+Wheel up narrows the FOV (default)", D, KeyNames.WheelUp, Hd, ActionIds.CameraFovDown, Ctrl, pointer: true),
                C("Wheel up alone is not a shortcut (wheel dolly is the controller's)", D, KeyNames.WheelUp, N, null, pointer: true),
                // text focus (rule 2)
                C("text: W types", D, "W", N, null, focus: FocusKind.TextField),
                C("text: Ctrl+Z is the field's", D, "Z", N, null, Ctrl, focus: FocusKind.TextField),
                C("text: Delete is the field's", D, "Delete", N, null, focus: FocusKind.TextField),
                C("text: Ctrl+S does not save", D, "S", N, null, Ctrl, focus: FocusKind.TextField),
                C("text: Esc blurs", D, "Escape", T, ActionIds.TextBlur, focus: FocusKind.TextField),
                C("text: Esc blurs (hs2)", H, "Escape", T, ActionIds.TextBlur, focus: FocusKind.TextField),
                C("text over the viewport: arrows are the field's", D, "UpArrow", N, null, focus: FocusKind.TextField, pointer: true),
                C("text: Enter is the field's", D, "Return", N, null, focus: FocusKind.TextField),
                C("other control focused: shortcuts work", D, "W", T, ActionIds.ToolMove, focus: FocusKind.Control),
                // IME (rule 3)
                C("IME composing: W", D, "W", N, null, ime: true),
                C("IME composing: Enter", D, "Return", N, null, ime: true),
                C("IME composing: Esc", D, "Escape", N, null, ime: true, focus: FocusKind.TextField),
                C("IME composing in a modal: Enter does not accept", D, "Return", N, null, ime: true, modal: true, focus: FocusKind.TextField),
                C("IME composing: Ctrl+Z", D, "Z", N, null, Ctrl, ime: true),
                // modal (rule 1)
                C("modal: Esc cancels", D, "Escape", T, ActionIds.DialogCancel, modal: true),
                C("modal: Enter accepts", D, "Return", T, ActionIds.DialogAccept, modal: true),
                C("modal: keypad Enter accepts", D, "KeypadEnter", T, ActionIds.DialogAccept, modal: true),
                C("modal: W swallowed", D, "W", Bl, null, modal: true),
                C("modal: Ctrl+Z swallowed", D, "Z", Bl, null, Ctrl, modal: true),
                C("modal: viewport keys swallowed", H, "A", Bl, null, modal: true, pointer: true),
                C("modal text field: letters reach the field", D, "W", N, null, modal: true, focus: FocusKind.TextField),
                C("modal text field: Enter accepts", D, "Return", T, ActionIds.DialogAccept, modal: true, focus: FocusKind.TextField),
                C("modal: Shift+Enter swallowed", D, "Return", Bl, null, Shift, modal: true),
                C("modal: repeat swallowed", D, "Return", Bl, null, repeat: true, modal: true),
                // Phase 3.6: pause, screenshot, dialog in both presets
                C("Space pauses (default)", D, "Space", T, ActionIds.TimePause),
                C("Space pauses (hs2)", H, "Space", T, ActionIds.TimePause),
                C("Shift+Space is panels, not pause", D, "Space", T, ActionIds.ViewPanels, Shift),
                C("text: Space is the field's", D, "Space", N, null, focus: FocusKind.TextField),
                C("repeat: Space does not re-toggle the pause", D, "Space", Bl, null, repeat: true),
                C("F11 screenshot (default)", D, "F11", T, ActionIds.CaptureScreenshot),
                C("F11 screenshot (hs2)", H, "F11", T, ActionIds.CaptureScreenshot),
                C("Shift+F11 screenshot dialog", H, "F11", T, ActionIds.CaptureDialog, Shift),
                C("text: F11 does nothing", D, "F11", N, null, focus: FocusKind.TextField),
                C("modal: F11 swallowed", D, "F11", Bl, null, modal: true),
                // repeat (rule 6)
                C("repeat: tool key does not re-trigger", D, "W", Bl, null, repeat: true),
                C("repeat: undo repeats", D, "Z", T, ActionIds.Undo, Ctrl, repeat: true),
                C("repeat: hs2 Z undo repeats", H, "Z", T, ActionIds.Undo, repeat: true),
                C("repeat: held camera key stays held", D, "LeftArrow", Hd, ActionIds.CameraMoveLeft, repeat: true, pointer: true),
                C("repeat: delete does not repeat", D, "Delete", Bl, null, repeat: true),
                C("repeat: tree navigation repeats", D, "DownArrow", T, ActionIds.TreeNext, repeat: true, scope: ActionScope.Tree),
                C("repeat: unbound stays none", D, "F9", N, null, repeat: true),
                // modifier actions are never key-press triggers
                C("hs2: P press is not an action", H, "P", N),
                C("hs2: V press is not an action", H, "V", N, null, pointer: true),
                C("hs2: B press is not an action", H, "B", N, null, pointer: true),
            };
            var dm = new ShortcutResolver(d);
            var hm = new ShortcutResolver(h);
            int fails = 0;
            foreach (var c in cases)
            {
                var r = (c.Preset == "default" ? dm : hm).Resolve(Input(c));
                bool ok = r.Outcome == c.Outcome && (c.Action == null || r.ActionId == c.Action) && (c.Action != null || r.ActionId == null || c.Outcome == ShortcutOutcome.Pending);
                if (!ok) { fails++; t.Check(false, "resolve [" + c.Preset + "] " + c.Name + ": got " + r + ", expected " + c.Outcome + " " + c.Action); }
            }
            // two-stroke chords (a test keymap)
            var two = d.Clone("two");
            two.Set(ActionIds.HelpAbout, "Ctrl+K Ctrl+A");
            two.Set(ActionIds.UiResetLayout, "Ctrl+K R");
            var tr = new ShortcutResolver(two);
            var p1 = tr.Resolve(new KeyInput("K", KeyMods.Ctrl));
            bool ok2 = p1.Outcome == ShortcutOutcome.Pending;
            var p2 = tr.Resolve(new KeyInput("A", KeyMods.Ctrl) { Pending = p1.Stroke });
            ok2 &= p2.Outcome == ShortcutOutcome.Trigger && p2.ActionId == ActionIds.HelpAbout;
            var p3 = tr.Resolve(new KeyInput("R") { Pending = p1.Stroke });
            ok2 &= p3.ActionId == ActionIds.UiResetLayout;
            var p4 = tr.Resolve(new KeyInput("Q") { Pending = p1.Stroke });
            ok2 &= p4.Outcome == ShortcutOutcome.Blocked;
            var p5 = tr.Resolve(new KeyInput("K", KeyMods.Ctrl) { Focus = FocusKind.TextField });
            ok2 &= p5.Outcome == ShortcutOutcome.None;
            t.Check(ok2, "resolve: two-stroke chords (pending, complete, other second stroke, miss, text focus): " + p1 + " / " + p2 + " / " + p3 + " / " + p4 + " / " + p5);
            // TextAllowed (a custom manifest lookup)
            var info = new ActionInfo(ActionIds.FileSave, ActionScope.Global, ActionKind.Trigger, textAllowed: true);
            var ta = new ShortcutResolver(d, id => id == ActionIds.FileSave ? info : ActionManifest.TryGet(id, out var i) ? i : null);
            var r6 = ta.Resolve(new KeyInput("S", KeyMods.Ctrl) { Focus = FocusKind.TextField });
            t.Check(r6.Outcome == ShortcutOutcome.Trigger && r6.ActionId == ActionIds.FileSave, "resolve: a TextAllowed action runs inside a text field (" + r6 + ")");
            int total = cases.Count + 6;
            t.Check(fails == 0, "resolve: " + total + " resolution cases (" + fails + " failed)");
            t.Check(total >= 60, "resolve: at least 60 cases (" + total + ")");
            t.Info("resolver cases: " + total);
        }

        static KeyInput Input(Case c) => new KeyInput(c.Key, c.Mods)
        {
            Repeat = c.Repeat, ImeComposing = c.Ime, ModalOpen = c.Modal, PointerOverViewport = c.Pointer, Focus = c.Focus,
            FocusScope = c.Scope, ViewportFocused = c.ViewportFocus,
        };

        static void Held(TestRunner t, Keymap d, Keymap h)
        {
            var dm = new ShortcutResolver(d);
            var hm = new ShortcutResolver(h);
            string Show(List<HeldAction> l) => string.Join(", ", l.Select(x => x.ToString()));
            List<HeldAction> R(ShortcutResolver r, KeyMods m, bool over, params string[] keys) =>
                r.ResolveHeld(keys, m, FocusKind.None, ActionScope.Global, over, false);
            var a = R(dm, KeyMods.None, true, "UpArrow", "LeftArrow");
            t.Check(a.Count == 2 && a.All(x => x.Speed == 1) && a.Any(x => x.ActionId == ActionIds.CameraMoveForward), "held: two arrows (" + Show(a) + ")");
            var b = R(dm, KeyMods.Shift, true, "UpArrow");
            t.Check(b.Count == 1 && b[0].ActionId == ActionIds.CameraMoveForward && b[0].Speed == 5, "held: Shift ×5 (" + Show(b) + ")");
            var c = R(dm, KeyMods.Ctrl, true, "UpArrow");
            t.Check(c.Count == 1 && c[0].ActionId == ActionIds.CameraDollyIn && c[0].Speed == 1, "held: Ctrl+Up = dolly in, not a slow pan (" + Show(c) + ")");
            var e = R(dm, KeyMods.Ctrl, true, "PageUp");
            t.Check(e.Count == 1 && e[0].ActionId == ActionIds.CameraMoveUp && Math.Abs(e[0].Speed - 0.2) < 1e-12, "held: Ctrl ×0.2 where unbound (" + Show(e) + ")");
            var f = R(dm, KeyMods.Ctrl | KeyMods.Shift, true, "UpArrow");
            t.Check(f.Count == 1 && f[0].ActionId == ActionIds.CameraDollyIn && f[0].Speed == 5, "held: Ctrl+Shift+Up = dolly ×5 (" + Show(f) + ")");
            var g = R(dm, KeyMods.Alt, true, "UpArrow");
            t.Check(g.Count == 1 && g[0].ActionId == ActionIds.CameraOrbitUp, "held: Alt+Up orbits (" + Show(g) + ")");
            var n = R(dm, KeyMods.None, false, "UpArrow");
            t.Check(n.Count == 0, "held: nothing away from the viewport");
            var tx = dm.ResolveHeld(new[] { "UpArrow" }, KeyMods.None, FocusKind.TextField, ActionScope.Global, true, false);
            t.Check(tx.Count == 0, "held: nothing while a text field has focus");
            var md = dm.ResolveHeld(new[] { "UpArrow" }, KeyMods.None, FocusKind.None, ActionScope.Global, true, false, modalOpen: true);
            t.Check(md.Count == 0, "held: nothing while a modal is open");
            var tree = dm.ResolveHeld(new[] { "UpArrow" }, KeyMods.None, FocusKind.None, ActionScope.Tree, true, false);
            t.Check(tree.Count == 0, "held: tree focus owns the arrows (trigger tree.prev wins)");
            var hs = R(hm, KeyMods.None, true, "Home", "Keypad8", "Semicolon");
            t.Check(hs.Select(x => x.ActionId).OrderBy(x => x).SequenceEqual(new[] { ActionIds.CameraDollyIn, ActionIds.CameraFovDown, ActionIds.CameraOrbitUp }.OrderBy(x => x)),
                "held: hs2 Home / Keypad8 / Semicolon (the JIS ; key) (" + Show(hs) + ")");
            var trig = R(hm, KeyMods.None, true, "A");
            t.Check(trig.Count == 0, "held: a trigger key is not held");
            // modifier holds
            t.Check(hm.IsModifierHeld(ActionIds.ClickParent, new[] { "P" }, KeyMods.None), "modifier: P held");
            t.Check(!hm.IsModifierHeld(ActionIds.ClickParent, new[] { "P" }, KeyMods.Ctrl), "modifier: P+Ctrl is not P (exact)");
            t.Check(hm.IsModifierHeld(ActionIds.ClickParentAll, new[] { "P" }, KeyMods.Ctrl), "modifier: P+Ctrl held");
            t.Check(hm.IsModifierHeld(ActionIds.ClickCopyTransform, new[] { "X" }, KeyMods.Ctrl), "modifier: X+Ctrl held");
            t.Check(!hm.IsModifierHeld(ActionIds.ClickCopyTransform, new[] { "X" }, KeyMods.None), "modifier: X alone is nothing");
            t.Check(hm.IsModifierHeld(ActionIds.GizmoSnapHold, new[] { "V" }, KeyMods.None), "modifier: V snap (hs2)");
            t.Check(dm.IsModifierHeld(ActionIds.GizmoSnapHold, new string[0], KeyMods.Ctrl), "modifier: Ctrl snap (default)");
            t.Check(dm.IsModifierHeld(ActionIds.GizmoSnapHold, new string[0], KeyMods.Ctrl | KeyMods.Shift) && dm.IsModifierHeld(ActionIds.GizmoPrecisionHold, new string[0], KeyMods.Ctrl | KeyMods.Shift),
                "modifier: Ctrl+Shift = snap and precision together");
            t.Check(!dm.IsModifierHeld(ActionIds.GizmoSnapHold, new[] { "V" }, KeyMods.None), "modifier: V is nothing in default");
        }

        static void Registry(TestRunner t)
        {
            var reg = new ActionRegistry();
            int ran = 0;
            bool can = true;
            reg.Register(ActionIds.Undo, call => { ran++; return "ok"; }, () => can);
            reg.Register(new ActionDef(ActionIds.Redo, null));
            t.Check(reg.Run(ActionIds.Undo, null, out var res) == ActionRunStatus.Ran && ran == 1 && (string)res == "ok", "registry: run");
            can = false;
            t.Equal(ActionRunStatus.Unavailable, reg.Run(ActionIds.Undo, null, out _), "registry: CanExecute false = unavailable");
            t.Equal(ActionRunStatus.Unknown, reg.Run(ActionIds.Delete, null, out _), "registry: not registered = unknown");
            t.Equal(ActionRunStatus.NoBehaviour, reg.Run(ActionIds.Redo, null, out _), "registry: no Execute");
            t.Throws<ArgumentException>(() => reg.Register(new ActionDef(new ActionInfo("plugin.x", ActionScope.Global), null)), "registry: non-manifest ids refused");
            reg.AllowUnlisted = true;
            reg.Register(new ActionDef(new ActionInfo("plugin.x", ActionScope.Global), null));
            t.Check(reg.Contains("plugin.x") && reg.All.Count() == 3, "registry: AllowUnlisted (5.1 plugins)");
            var call2 = new ActionInvocation { Args = new Dictionary<string, object> { ["path"] = "x.png" } };
            t.Equal("x.png", (string)call2.Arg("path"), "registry: invocation args");
            t.Equal(ActionScope.Viewport, ActionManifest.Get(ActionIds.CameraFrame).Scope, "manifest: camera.frame is Viewport");
            t.Equal(ActionKind.Held, ActionManifest.Get(ActionIds.CameraMoveForward).Kind, "manifest: camera moves are Held");
            t.Equal("camera", ActionManifest.Get(ActionIds.CameraSlotLoad(3)).Group, "manifest: group");
        }
    }
}

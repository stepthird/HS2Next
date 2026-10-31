// UI3 of the bridge acceptance (Phase 4.0 integrator T): the Maker's
// Edit tab driven through the bridge's ui.* commands in Editor play (BridgeTest.RunPlayUi / RunPlayUi3) and in the app
// player (-bridgeAccept with -ui). Without a running UI the case is skipped with a warning (ui.state enabled false).
//   file.new; add.female (BR-Chan); the character inspector's Edit tab (ui.input click on tab-edit), Face › Shape;
//   a pointer drag on the first slider's knob → one history entry "Edit face", the stored value moved inside 0..1;
//   150 typed into its number field → stored 1.5 (Q23: typed −100..200, stored unclamped) and the face bones moved;
//   ui.action edit.undo → the dragged value and its bones; edit.redo → 1.5 and the typed bones; file.saveAs → file.new →
//   file.open → the value 1.5, the same bones and the Edit tab showing 150. Bones are compared through bones.dump (root
//   head, exact doubles) on the bones the typed edit moved, minus bones that move between two dumps without an edit
//   (animation, look) and the hair / accessory joints (dynamic bones: physics history). Also reported once: the edit flush of the typed value (chara.edit.info), the player numbers of
//   when the case runs in the player.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2Next.Scenes;
using HS2Next.UI;
using HS2Next.UI.Panels;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.App
{
    public static partial class BridgeAcceptance
    {
        public static IEnumerator Ui3(AcceptanceCtx c)
        {
            c.Id = "UI3";
            yield return c.Call(new { cmd = "ui.state" });
            if (c.Last == null || !c.Last.Ok || (bool?)c.J["enabled"] != true)
            {
                c.Warn("UI3 skipped: the Studio UI is not running (start with -ui; " + AcceptanceCtx.Short(c.Last?.ToString()) + ")");
                yield break;
            }
            var host = UiHost.Current;
            var ui = host?.Context;
            if (!c.Check(ui != null, "UiHost in the app's process")) yield break;

            // a new scene with BR-Chan
            yield return c.Call(new { cmd = "ui.action", action = "file.new", args = new { force = true } });
            c.CheckOk("ui.action file.new {force}");
            string card = Path.Combine(AppCore.Current.Paths.Hs2CharaDir(1) ?? "", c.FemaleRel ?? "");
            yield return c.Call(new { cmd = "ui.action", action = "add.female", args = new { path = card } }, 180);
            string id = (string)c.J["result"];
            if (!c.Check(c.Last != null && c.Last.Ok && !string.IsNullOrEmpty(id), "ui.action add.female {path} → a new character", AcceptanceCtx.Short(c.Last?.ToString()))) yield break;
            for (int i = 0; i < 600 && ui.Runtime.FindChara(id)?.State != HS2Next.Scenes.Runtime.RealizationState.Built; i++) yield return c.Frames(1);
            if (!c.Check(ui.Runtime.FindChara(id)?.State == HS2Next.Scenes.Runtime.RealizationState.Built, "the character is built")) yield break;
            if (!c.Check(ui.Model.Find(id)?.Appearance?.Stored != null, "the character has stored documents (imported on add)")) yield break;

            // the Edit tab, Face › Shape
            var row = new MakerSliderRow[1];
            yield return Ui3OpenShape(c, host, id, row);
            if (row[0] == null) yield break;
            var r = row[0];
            string path = r.Path;
            c.Info($"UI3: the first slider of Face › Shape is {path} ({r.Control.LabelElement?.text})");
            float v0 = Ui3Value(ui, id, path);

            // a drag on the slider's knob: one transaction
            int undo0 = ui.Session.Commands.UndoList.Count;
            var knob = r.Control.Slider?.Q("unity-dragger");
            if (!c.Check(knob != null, "the slider's knob (unity-dragger)")) yield break;
            var kp = new Vector2[1];
            yield return RevealPastToasts(c, host, knob, "the slider knob", kp);
            float track = r.Control.Slider.Q("unity-drag-container")?.worldBound.width ?? 100f;
            float dx = (v0 > 0.5f ? -1 : 1) * Mathf.Max(8f, track * 0.2f);
            var p0 = kp[0];
            yield return c.Call(new
            {
                cmd = "ui.input", frames = 3,
                events = new object[]
                {
                    new { type = "pointer", phase = "down", x = p0.x, y = p0.y, button = 0 },
                    new { type = "pointer", phase = "move", x = p0.x + dx / 3, y = p0.y, button = 0 },
                    new { type = "pointer", phase = "move", x = p0.x + 2 * dx / 3, y = p0.y, button = 0 },
                    new { type = "pointer", phase = "move", x = p0.x + dx, y = p0.y, button = 0 },
                    new { type = "pointer", phase = "up", x = p0.x + dx, y = p0.y, button = 0 },
                },
            });
            c.CheckOk("ui.input drag on the slider knob");
            yield return c.Frames(3);
            float v1 = Ui3Value(ui, id, path);
            var list = ui.Session.Commands.UndoList;
            c.Check(v1 != v0 && v1 >= 0f && v1 <= 1f, $"the drag moved the stored value inside 0..1 ({v0} → {v1})");
            c.Check(list.Count == undo0 + 1 && list[0].Label == OpLabelsEditFace, $"the drag is one history entry \"{OpLabelsEditFace}\" ({list.Count - undo0} entries, last \"{(list.Count > 0 ? list[0].Label : "")}\")");
            c.Check(!r.Dragging, "no drag transaction left open");
            var bonesDrag = new Dictionary<string, string>[1];
            yield return Ui3Bones(c, id, bonesDrag);

            // 150 typed into the number field → stored 1.5
            var field = r.Control.Field;
            var fp = new Vector2[1];
            yield return RevealPastToasts(c, host, field, "the slider's number field", fp);
            int undo1 = ui.Session.Commands.UndoList.Count;
            yield return c.Call(new
            {
                cmd = "ui.input", frames = 3,
                events = new object[]
                {
                    new { type = "pointer", phase = "click", x = fp[0].x, y = fp[0].y, button = 0 },
                    new { type = "key", key = "A", mods = new[] { "ctrl" } },
                    new { type = "text", text = "150" },
                    new { type = "key", key = "Return" },
                },
            });
            c.CheckOk("ui.input: 150 typed into the number field");
            yield return c.Frames(3);
            float v2 = Ui3Value(ui, id, path);
            c.Check(v2 == 1.5f, $"typed 150 → stored {v2} (1.5 expected: −100..200 typed, stored unclamped)");
            c.Check(ui.Session.Commands.UndoList.Count == undo1 + 1, $"the typed value is one history entry ({ui.Session.Commands.UndoList.Count - undo1})");
            c.Check(Math.Abs(r.Control.Value - 150) < 1e-6, $"the row shows {r.Control.Value} (150 expected; the knob at the end)");
            yield return c.Call(new { cmd = "chara.edit.info", key = id });
            if (c.Last != null && c.Last.Ok)
                c.Info($"UI3 timing ({(Application.isEditor ? "Editor" : "player")}): the typed value's flush {(double?)c.J["lastFlush"]?["ms"]:0.000} ms, units {AcceptanceCtx.Short(c.J["lastFlush"]?["units"]?.ToString(Newtonsoft.Json.Formatting.None))}; editFlushes {(int?)c.J["editFlushes"]}, editRebuilds {(int?)c.J["editRebuilds"]}");
            var bonesTyped = new Dictionary<string, string>[1];
            yield return Ui3Bones(c, id, bonesTyped);
            var bonesTyped2 = new Dictionary<string, string>[1];
            yield return c.Frames(10);
            yield return Ui3Bones(c, id, bonesTyped2);
            if (bonesDrag[0] == null || bonesTyped[0] == null || bonesTyped2[0] == null) { c.Check(false, "bones.dump of the head"); yield break; }
            var moving = new HashSet<string>(bonesTyped[0].Keys.Where(k => !bonesTyped2[0].TryGetValue(k, out var v) || v != bonesTyped[0][k]), StringComparer.Ordinal);
            // hair and accessory joints under the head are dynamic bones: their values carry the physics history (left out)
            bool Physics(string k) => k.Contains("/N_hair_Root/") || k.Contains("/ca_slot");
            var all = bonesTyped[0].Keys.Where(k => !moving.Contains(k) && (!bonesDrag[0].TryGetValue(k, out var v) || v != bonesTyped[0][k])).ToList();
            var compared = all.Where(k => !Physics(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
            c.Info($"UI3: the typed value moved {compared.Count} head bones ({string.Join(", ", compared.Take(8))}{(compared.Count > 8 ? ", …" : "")}); left out: {moving.Count} bones that move without an edit, {all.Count - compared.Count} hair / accessory physics joints");
            if (!c.Check(compared.Count > 0, "the typed value moves face bones")) yield break;

            // undo / redo
            yield return c.Call(new { cmd = "ui.action", action = "edit.undo" });
            c.CheckOk("ui.action edit.undo");
            yield return c.Frames(3);
            c.Check(Ui3Value(ui, id, path) == v1, $"undo → the dragged value {Ui3Value(ui, id, path)} ({v1})");
            var bonesUndo = new Dictionary<string, string>[1];
            yield return Ui3Bones(c, id, bonesUndo);
            c.Check(Ui3Same(bonesUndo[0], bonesDrag[0], compared, out string du), "undo → the bones of the dragged value", du);
            yield return c.Call(new { cmd = "ui.action", action = "edit.redo" });
            c.CheckOk("ui.action edit.redo");
            yield return c.Frames(3);
            c.Check(Ui3Value(ui, id, path) == 1.5f, $"redo → {Ui3Value(ui, id, path)} (1.5)");
            var bonesRedo = new Dictionary<string, string>[1];
            yield return Ui3Bones(c, id, bonesRedo);
            c.Check(Ui3Same(bonesRedo[0], bonesTyped[0], compared, out string dr), "redo → the bones of the typed value", dr);

            // save, new, open
            yield return c.Call(new { cmd = "ui.action", action = "file.saveAs", args = new { name = "ui3_acceptance", overwrite = true } }, 120);
            string file = (string)c.J["result"];
            if (!c.Check(c.Last != null && c.Last.Ok && file != null && File.Exists(file), "ui.action file.saveAs {name} → " + file)) yield break;
            yield return c.Call(new { cmd = "ui.action", action = "file.new", args = new { force = true } });
            c.CheckOk("ui.action file.new {force}");
            yield return c.Call(new { cmd = "ui.action", action = "file.open", args = new { path = file } }, 180);
            c.CheckOk("ui.action file.open {path}");
            for (int i = 0; i < 600 && ui.Runtime.FindChara(id)?.State != HS2Next.Scenes.Runtime.RealizationState.Built; i++) yield return c.Frames(1);
            if (!c.Check(ui.Runtime.FindChara(id)?.State == HS2Next.Scenes.Runtime.RealizationState.Built, "the reopened character is built (same node id)")) yield break;
            yield return c.Frames(3);
            c.Check(Ui3Value(ui, id, path) == 1.5f, $"reopened: {path} = {Ui3Value(ui, id, path)} (1.5)");
            var bonesOpen = new Dictionary<string, string>[1];
            yield return Ui3Bones(c, id, bonesOpen);
            c.Check(Ui3Same(bonesOpen[0], bonesTyped[0], compared, out string dopen), "reopened: the bones equal the typed value's", dopen);
            yield return c.Call(new { cmd = "select", key = id });
            var row2 = new MakerSliderRow[1];
            yield return Ui3OpenShape(c, host, id, row2, path);
            if (row2[0] != null) c.Check(Math.Abs(row2[0].Control.Value - 150) < 1e-6, $"reopened: the Edit tab shows {row2[0].Control.Value} (150)");
        }

        const string OpLabelsEditFace = HS2Next.UI.Core.OpLabels.EditFace;

        /// <summary>Selects the character, opens the inspector's Edit tab and the Face node, and gives the slider row of
        /// <paramref name="path"/> (the first Maker slider when null).</summary>
        static IEnumerator Ui3OpenShape(AcceptanceCtx c, UiHost host, string id, MakerSliderRow[] row, string path = null)
        {
            var ui = host.Context;
            if (ui.Selection.Primary != id) yield return c.Call(new { cmd = "select", key = id });
            yield return c.Call(new { cmd = "ui.panel", panel = "inspector", op = "focus" });
            yield return c.Frames(6);
            var tab = host.Root.Q<Button>("tab-" + CharaEditTab.TabId);
            if (!c.Check(tab != null, "the character inspector's Edit tab")) yield break;
            var tp = new Vector2[1];
            yield return RevealPastToasts(c, host, tab, "the Edit tab", tp);
            yield return c.Call(new { cmd = "ui.input", frames = 4, events = new object[] { new { type = "pointer", phase = "click", x = tp[0].x, y = tp[0].y, button = 0 } } });
            var body = host.Root.Q<CharaEditTab>();
            if (!c.Check(body != null && body.panel != null, "the Edit tab is shown")) yield break;
            if (body.CurrentNode != "face")
            {
                var fb = host.Root.Q<Button>("edit-tree-face");
                if (!c.Check(fb != null, "the Edit tab's Face node")) yield break;
                var fpt = new Vector2[1];
                yield return RevealPastToasts(c, host, fb, "the Face node", fpt);
                yield return c.Call(new { cmd = "ui.input", frames = 4, events = new object[] { new { type = "pointer", phase = "click", x = fpt[0].x, y = fpt[0].y, button = 0 } } });
            }
            c.Check(body.CurrentNode == "face", "Edit › Face is open (" + body.CurrentNode + ")");
            c.Check(body.HasSession, "the Edit tab holds the character's edit session");
            yield return c.Frames(3);
            row[0] = path == null ? body.Query<MakerSliderRow>().First() : body.Query<MakerSliderRow>().Where(x => x.Path == path).First();
            c.Check(row[0] != null && row[0].worldBound.width > 0, "Face › Shape shows the slider row " + (row[0]?.Path ?? path ?? "(first)"));
        }

        static float Ui3Value(HS2Next.UI.UiContext ui, string id, string path)
        {
            try { return FieldAccessor.Get(ui.Model, id, path) is float f ? f : float.NaN; } catch (Exception) { return float.NaN; }
        }

        /// <summary>bones.dump of the head: name path → "lp|lq|ls" as the exact doubles of the reply.</summary>
        static IEnumerator Ui3Bones(AcceptanceCtx c, string id, Dictionary<string, string>[] into)
        {
            yield return c.Call(new { cmd = "bones.dump", key = id, root = "head" }, 120);
            if (c.Last == null || !c.Last.Ok || !(c.J["bones"] is JArray a)) { into[0] = null; yield break; }
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var b in a)
            {
                string k = (string)b["path"] ?? (string)b["name"];
                if (k == null) continue;
                d[k] = (b["lp"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "") + "|" + (b["lq"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "") + "|" + (b["ls"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "");
            }
            into[0] = d;
        }

        static bool Ui3Same(Dictionary<string, string> a, Dictionary<string, string> b, List<string> keys, out string detail)
        {
            detail = "";
            if (a == null || b == null) { detail = "no dump"; return false; }
            var diff = keys.Where(k => !a.TryGetValue(k, out var x) || !b.TryGetValue(k, out var y) || x != y).ToList();
            if (diff.Count == 0) { detail = keys.Count + " bones equal"; return true; }
            string k0 = diff[0];
            detail = $"{diff.Count} of {keys.Count} differ, first {k0}: {(a.TryGetValue(k0, out var va) ? va : "-")} vs {(b.TryGetValue(k0, out var vb) ? vb : "-")}";
            return false;
        }
    }
}

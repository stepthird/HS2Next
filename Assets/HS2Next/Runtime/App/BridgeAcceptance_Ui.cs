// UI1 of the bridge acceptance (Phase 3.5 package A): the Studio UI driven through the bridge's
// ui.* commands in Editor play (BridgeTest; the UI is on with -ui or BridgeTest.RunPlayUi) and in the app player
// (-bridgeAccept with -ui). Without a running UI the case is skipped with a warning (ui.state enabled false).
//   ui.action file.new {force}; ui.action add.female {path} (the acceptance card); ui.input pointer drag on the move-X handle
//   (screen points from the gizmo's projection of the selected character's pivot) → scene.doc position changed by the
//   expected delta (the axis parameter under the cursor, 1e-3) and history +1; ui.action edit.undo → back; a light added by
//   ui.action add.light, its intensity typed into the inspector row (ui.input click on the numeric field, text "1.5",
//   Return) → model 1.5; the State tab (ui.input click on tab-state) and the top's "half" button → chara.clothes states[0]
//   1 and chara.clothesDump numbers (no capture); ui.action file.saveAs {name} → the file exists → ui.action file.open
//   {path} → the same scene.doc (without the members SaveCapture takes from the running character: anime.time, look.neckFix, look.eyeRotations); ui.action ui.language ja → ui.query #tb-file text changes (back to en); timing lines:
//   first UI frame, item filter (when C's browser reports one), pick.
// The geometry (handle and field positions) is read in-process from the UI host (the case runs inside the app's process
// in both hosts); every change is made through the bridge. The v0.3 trial checks (BridgeAcceptance_UiV03.cs) run first;
// the final pick also checks that a viewport click shows the inspector tab.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using HS2Next.UI;
using HS2Next.UI.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.App
{
    public static partial class BridgeAcceptance
    {
        public static IEnumerator Ui(AcceptanceCtx c)
        {
            c.Id = "UI1";
            yield return c.Call(new { cmd = "ui.state" });
            if (c.Last == null || !c.Last.Ok || (bool?)c.J["enabled"] != true)
            {
                c.Warn("UI1 skipped: the Studio UI is not running (start with -ui; " + AcceptanceCtx.Short(c.Last?.ToString()) + ")");
                yield break;
            }
            var host = UiHost.Current;
            var ui = host?.Context;
            if (!c.Check(ui != null, "UiHost in the app's process")) yield break;
            c.Info($"UI1 timing: first UI frame {(double?)c.J["firstFrameMs"]:0} ms after the scene stage; panel scale {(double?)c.J["panelScale"]}, preset {(string)c.J["preset"]}");
            yield return UiV03(c, host);   // the v0.3 trial fixes: sidebar layout, options box, language button, resize (BridgeAcceptance_UiV03.cs)

            // new scene, add the card through the UI action
            yield return c.Call(new { cmd = "ui.action", action = "file.new", args = new { force = true } });
            c.CheckOk("ui.action file.new {force}");
            string card = Path.Combine(AppCore.Current.Paths.Hs2CharaDir(1) ?? "", c.FemaleRel ?? "");
            yield return c.Call(new { cmd = "ui.action", action = "add.female", args = new { path = card } }, 180);
            string id = (string)c.J["result"];
            if (!c.Check(c.Last != null && c.Last.Ok && !string.IsNullOrEmpty(id), "ui.action add.female {path} → a new character", AcceptanceCtx.Short(c.Last?.ToString()))) yield break;
            for (int i = 0; i < 600 && ui.Runtime.FindChara(id)?.State != HS2Next.Scenes.Runtime.RealizationState.Built; i++) yield return c.Frames(1);
            c.Check(ui.Runtime.FindChara(id)?.State == HS2Next.Scenes.Runtime.RealizationState.Built, "the character is built");
            c.Check(ui.Selection.Primary == id, "the new character is selected (U14)");

            // gizmo drag on move X
            yield return c.Call(new { cmd = "ui.action", action = "tool.move" });
            ui.Viewport.Camera.Frame(new[] { id });
            yield return c.Frames(4);
            var g = ui.Viewport.Gizmo;
            var holder = ui.Runtime.HolderOf(id);
            var start = ui.Model.Find(id).Doc.Transform.Pos;
            float L = ui.Options.Int(OptionDefs.GizmoSize) * g.WorldPerPx(holder.position);
            var axis = ui.Space == GizmoSpace.World || ui.Space == GizmoSpace.Auto ? Vector3.right : holder.rotation * Vector3.right;
            Vector2 a0 = g.ToPanel(holder.position), aT = g.ToPanel(holder.position + axis * L);
            Vector2 grab = Vector2.Lerp(a0, aT, 0.6f), end = grab + (aT - a0).normalized * 50f;
            var hit = g.HitTest(grab);
            c.Check(hit.Kind == HS2Next.UI.Gizmos.HandleKind.MoveAxis && hit.Axis == 0, "the move-X handle under the grab point (" + hit + ")");
            int undo0 = ui.Session.Commands.UndoList.Count;
            float expect = AxisParam(ui.Viewport.RayAt(end), holder.position, axis) - AxisParam(ui.Viewport.RayAt(grab), holder.position, axis);
            yield return c.Call(new
            {
                cmd = "ui.input", frames = 2,
                events = new object[]
                {
                    new { type = "pointer", phase = "down", x = grab.x, y = grab.y, button = 0 },
                    new { type = "pointer", phase = "move", x = (grab.x + end.x) / 2, y = (grab.y + end.y) / 2, button = 0 },
                    new { type = "pointer", phase = "move", x = end.x, y = end.y, button = 0 },
                    new { type = "pointer", phase = "up", x = end.x, y = end.y, button = 0 },
                },
            });
            c.CheckOk("ui.input drag on the move-X handle");
            var pos = ui.Model.Find(id).Doc.Transform.Pos;
            c.Check(Mathf.Abs(pos.X - start.X - expect) < 1e-3f && Mathf.Abs(pos.Y - start.Y) < 1e-4f && Mathf.Abs(pos.Z - start.Z) < 1e-4f,
                $"scene.doc position moved along X by {pos.X - start.X:0.####} (expected {expect:0.####}; y {pos.Y - start.Y:0.#####}, z {pos.Z - start.Z:0.#####})");
            c.Check(ui.Session.Commands.UndoList.Count == undo0 + 1 && ui.Session.Commands.UndoList[0].Label == "Transform", "history +1 \"Transform\"");
            yield return c.Call(new { cmd = "ui.action", action = "edit.undo" });
            pos = ui.Model.Find(id).Doc.Transform.Pos;
            c.Check(c.Last != null && c.Last.Ok && Mathf.Abs(pos.X - start.X) < 1e-5f, "ui.action edit.undo → the position is back");

            // a light: intensity typed into the inspector's numeric field
            var lightRow = ui.App.Db?.Lights.Enumerate().FirstOrDefault();
            if (lightRow != null)
            {
                yield return c.Call(new { cmd = "ui.action", action = "add.light", args = new { @ref = lightRow.Key.ToString() } });
                c.CheckOk("ui.action add.light {ref}");
                yield return c.Call(new { cmd = "ui.panel", panel = "inspector", op = "focus" });
                yield return c.Frames(6);
                var row = host.Root.Q("field-light.intensity");
                var field = row?.Q<FloatField>("field") ?? row?.Q<FloatField>();
                if (c.Check(field != null && field.worldBound.width > 0, "the inspector shows the light's intensity field"))
                {
                    var pt = new Vector2[1];
                    yield return Reveal(c, host, field, "the intensity field", pt);
                    var p = pt[0];
                    yield return c.Call(new
                    {
                        cmd = "ui.input", frames = 2,
                        events = new object[]
                        {
                            new { type = "pointer", phase = "click", x = p.x, y = p.y, button = 0 },
                            new { type = "key", key = "A", mods = new[] { "ctrl" } },
                            new { type = "text", text = "1.5" },
                            new { type = "key", key = "Return" },
                        },
                    });
                    string lid = ui.Selection.Primary;
                    object v = null;
                    try { v = HS2Next.Scenes.FieldAccessor.Get(ui.Model, lid, "light.intensity"); } catch (Exception) { }
                    c.Check(v is float f && Mathf.Abs(f - 1.5f) < 1e-5f, "typed 1.5 → light.intensity " + v);
                }
            }
            else c.Warn("UI1 light: no Studio light rows");

            // State tab: top half
            yield return c.Call(new { cmd = "select", key = id });
            yield return c.Call(new { cmd = "ui.panel", panel = "inspector", op = "focus" });
            yield return c.Frames(6);
            var tab = host.Root.Q<Button>("tab-state");
            if (c.Check(tab != null, "the character inspector's State tab"))
            {
                var tpt = new Vector2[1];
                yield return Reveal(c, host, tab, "the State tab", tpt);
                var tp = tpt[0];
                yield return c.Call(new { cmd = "ui.input", frames = 4, events = new object[] { new { type = "pointer", phase = "click", x = tp.x, y = tp.y, button = 0 } } });
                var half = host.Root.Q("field-chara.state.clothes[0]")?.Q<Button>("seg-1");
                if (c.Check(half != null && half.worldBound.width > 0, "the top's \"half\" button"))
                {
                    var hpt = new Vector2[1];
                    yield return Reveal(c, host, half, "the half button", hpt);
                    var hp = hpt[0];
                    yield return c.Call(new { cmd = "ui.input", frames = 4, events = new object[] { new { type = "pointer", phase = "click", x = hp.x, y = hp.y, button = 0 } } });
                    yield return c.Call(new { cmd = "chara.clothes", key = id });
                    var st = c.J["states"] as JArray;
                    c.Check(st != null && (int)st[0] == 1, "chara.clothes states[0] = 1 (half) after the State tab click (" + AcceptanceCtx.Short(c.Last?.ToString()) + ")");
                    yield return c.Call(new { cmd = "chara.clothesDump", key = id });
                    c.Check(c.Last != null && c.Last.Ok && (int?)c.J["states"]?[0] == 1, "chara.clothesDump states[0] = 1 (numbers only)");
                }
            }

            // save as / open: the same document
            yield return c.Call(new { cmd = "scene.doc" });
            string before = Digest(c.J);
            var beforeJ = DocOf(c.J);
            yield return c.Call(new { cmd = "ui.action", action = "file.saveAs", args = new { name = "ui1_acceptance", overwrite = true } }, 120);
            string path = (string)c.J["result"];
            c.Check(c.Last != null && c.Last.Ok && path != null && File.Exists(path), "ui.action file.saveAs {name} → " + path);
            if (path != null && File.Exists(path))
            {
                yield return c.Call(new { cmd = "ui.action", action = "file.new", args = new { force = true } });
                yield return c.Call(new { cmd = "ui.action", action = "file.open", args = new { path } }, 180);
                c.CheckOk("ui.action file.open {path}");
                yield return c.Frames(10);
                yield return c.Call(new { cmd = "scene.doc" });
                string after = Digest(c.J);
                string diff = before == after ? "" : FirstDiff(Strip(beforeJ), Strip(DocOf(c.J)), "");
                c.Check(before != null && before == after, "the opened scene.doc equals the saved one", diff);
            }

            // language switch
            yield return c.Call(new { cmd = "ui.query", name = "#tb-file" });
            string en = (string)c.J["elements"]?[0]?["text"];
            yield return c.Call(new { cmd = "ui.action", action = "ui.language", args = new { language = "ja" } });
            yield return c.Frames(2);
            yield return c.Call(new { cmd = "ui.query", name = "#tb-file" });
            string ja = (string)c.J["elements"]?[0]?["text"];
            c.Check(en != null && ja != null && en != ja, $"language switch: toolbar File '{en}' → '{ja}' ({host.LastLanguageSwitchMs:0.0} ms)");
            yield return c.Call(new { cmd = "ui.action", action = "ui.language", args = new { language = "en" } });

            // pick timing (a click into the viewport on the character); with the workspace shown and nothing selected the
            // click shows the inspector tab (they share the left region)
            yield return c.Call(new { cmd = "ui.panel", panel = "workspace", op = "show" });
            yield return c.Call(new { cmd = "ui.action", action = "edit.deselect" });
            ui.Viewport.Camera.Frame(new[] { id });
            yield return c.Frames(3);
            var cp = g.ToPanel(HS2Next.UI.Viewport.NodeBounds.Of(ui, new[] { id }, out _).center);
            yield return c.Call(new { cmd = "ui.input", frames = 2, events = new object[] { new { type = "pointer", phase = "click", x = cp.x, y = cp.y, button = 0 } } });
            yield return c.Call(new { cmd = "ui.state" });
            c.Info($"UI1 timing: pick {(double?)c.J["viewport"]?["pickMs"]:0.00} ms (hit {(string)c.J["viewport"]?["pick"]}), language switch {host.LastLanguageSwitchMs:0.0} ms");
            yield return UiV03PickShowsInspector(c);
            yield return UiR2Silhouette(c, id);   // v0.3 trial 2 package R: the silhouette draws (BridgeAcceptance_UiR2.cs)
        }

        /// <summary>scene.doc's document (its "scene" member is the canonical JSON text).</summary>
        static JToken DocOf(JObject reply)
        {
            var t = reply?["scene"] ?? reply?["doc"];
            if (t != null && t.Type == JTokenType.String) { try { return JToken.Parse((string)t); } catch (Exception) { } }
            return t;
        }

        /// <summary>The members SaveCapture writes from the running characters / items: they advance
        /// between two reads while the animation and the look run.</summary>
        static System.Collections.Generic.List<JToken> Captured(JToken d) =>
            d.SelectTokens("$..anime.time").Concat(d.SelectTokens("$..look.neckFix")).Concat(d.SelectTokens("$..look.eyeRotations")).ToList();

        static JToken Strip(JToken d)
        {
            if (d == null) return null;
            d = d.DeepClone();
            foreach (var t in Captured(d)) t.Parent?.Remove();
            return d;
        }

        /// <summary>The first differing JSON path (diagnostics).</summary>
        static string FirstDiff(JToken a, JToken b, string path)
        {
            if (a == null || b == null) return path + ": " + (a == null ? "missing before" : "missing after");
            if (a.Type != b.Type) return path + ": " + a.Type + " vs " + b.Type;
            if (a is JObject oa && b is JObject ob)
            {
                foreach (var p in oa.Properties()) { var d = FirstDiff(p.Value, ob[p.Name], path + "." + p.Name); if (d != null) return d; }
                foreach (var p in ob.Properties()) if (oa[p.Name] == null) return path + "." + p.Name + ": only after";
                return null;
            }
            if (a is JArray aa && b is JArray ab)
            {
                if (aa.Count != ab.Count) return path + ": " + aa.Count + " vs " + ab.Count + " elements";
                for (int i = 0; i < aa.Count; i++) { var d = FirstDiff(aa[i], ab[i], path + "[" + i + "]"); if (d != null) return d; }
                return null;
            }
            return JToken.DeepEquals(a, b) ? null : path + ": " + AcceptanceCtx.Short(a.ToString(Newtonsoft.Json.Formatting.None)) + " vs " + AcceptanceCtx.Short(b.ToString(Newtonsoft.Json.Formatting.None));
        }

        /// <summary>
        /// Brings an element into view before a click (the inspector's ScrollView: at the player's panel scale the clothes rows
        /// are below the visible part, so a click on their world bound hit another element), waits for the layout, and gives
        /// its centre; the element under that point is reported when it is not the element (or inside it).
        /// </summary>
        static IEnumerator Reveal(AcceptanceCtx c, UiHost host, VisualElement e, string what, Vector2[] point)
        {
            for (var x = e.hierarchy.parent; x != null; x = x.hierarchy.parent)
                if (x is ScrollView sv) { sv.ScrollTo(e); break; }
            yield return c.Frames(3);
            point[0] = e.worldBound.center;
            var picked = host.Root?.panel?.Pick(point[0]);
            bool hit = picked != null && (picked == e || e.Contains(picked));
            if (!hit) c.Info($"UI1: {what} at {point[0]} is covered by {(picked == null ? "nothing" : (picked.name ?? "") + " (" + picked.GetType().Name + ")")}");
        }

        static float AxisParam(Ray r, Vector3 p0, Vector3 a)
        {
            var w = r.origin - p0;
            float b = Vector3.Dot(r.direction, a), d = Vector3.Dot(w, a), e = Vector3.Dot(w, r.direction);
            float den = 1 - b * b;
            return Mathf.Abs(den) < 1e-6f ? d : (d - b * e) / den;
        }

        /// <summary>A digest of scene.doc's document without the runtime-captured animation clock (chara.anime.time advances
        /// while the character plays; save captures it).</summary>
        static string Digest(JObject reply)
        {
            var doc = DocOf(reply);
            if (doc == null) return null;
            doc = doc.DeepClone();
            foreach (var t in Captured(doc)) t.Parent?.Remove();
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(doc.ToString(Newtonsoft.Json.Formatting.None)))).Replace("-", "");
        }
    }
}

// C26 of BridgeCoreTest: the Phase 3.5 scene / selection commands of package B (
// 2g), further methods of the partial BridgeCoreTest (-only C26): registration (CoreCommands.Phase35Names), select /
// selection and the sub-selection (view state, no history), chara.clothes (shapes, errors, cycling result stored, one entry,
// undo / redo restore scene.doc), chara.clothesDump (numbers only: object activity flags and mask values; no capture is
// taken or viewed), chara.accessories, chara.state, obj.rename, obj.reorder, chara.change (BR-Chan -> HERO107 and undo) and
// scene.import (new ids, one entry, selected). The SFW targets of Tools/targets.json only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;

public static partial class BridgeCoreTest
{
    static void C26(Ctx c)
    {
        var app = c.Host();
        var reg = app.BridgeServices.Commands;
        var names = HS2Next.App.Commands.CoreCommands.Phase35Names;
        c.Check(names.SequenceEqual(HS2Next.App.Commands.UiSceneCommands.Names) && names.Count == 10 && names.All(n => reg.Find(n) != null),
            "Phase35Names = the 10 commands of UiSceneCommands, all registered", string.Join(", ", names.Where(n => reg.Find(n) == null)));
        c.Ok(new { cmd = "scene.new" }, "scene.new");
        string chara = W1LoadFemale(c);
        if (chara == null) return;
        string item = W1AddItem(c), item2 = W1AddItem(c, "item.add 399 (second)");
        c.Frames(2);
        C26Select(c, chara, item, item2);
        C26Clothes(c, chara);
        C26Accessories(c, chara);
        C26State(c, chara);
        C26RenameReorder(c, item, item2);
        C26Change(c, chara);
        C26Import(c);
    }

    static void C26Select(Ctx c, string chara, string item, string item2)
    {
        int n0 = W1UndoCount(c);
        var s = c.Ok(new { cmd = "select", keys = new[] { item, chara } }, "select keys [item, chara]");
        c.Check(s["selected"] is JArray a && a.Select(x => (string)x).SequenceEqual(new[] { item, chara }) && (string)s["primary"] == item && s["sub"]?.Type == JTokenType.Null,
            "select: selected in order, primary = the first, sub null", Ctx.Short(s.ToString()));
        var s2 = c.Ok(new { cmd = "select", key = item2, add = true }, "select add");
        c.Check((s2["selected"] as JArray)?.Count == 3 && (string)s2["primary"] == item, "select add appends (primary kept)", Ctx.Short(s2.ToString()));
        var sel = c.Ok(new { cmd = "selection" }, "selection");
        c.Check((sel["selected"] as JArray)?.Count == 3 && c.App.Session.Selection.Count == 3, "selection = the session's SelectionModel");
        var sub = c.Ok(new { cmd = "select", sub = chara + "/ik/LeftHand" }, "select sub <chara>/ik/LeftHand");
        c.Check((string)sub["sub"] == chara + "/ik/LeftHand" && (sub["selected"] as JArray)?.Count == 1 && (string)sub["primary"] == chara,
            "select sub: the sub-selection set and its character the sole selection", Ctx.Short(sub.ToString()));
        c.Check(c.App.Ops.SubSelection == chara + "/ik/LeftHand", "StudioOps.SubSelection holds it (shared with the UI)");
        c.Ok(new { cmd = "select", sub = chara + "/fk/cf_J_Head" }, "select sub fk bone");
        c.Ok(new { cmd = "select", sub = chara + "/look" }, "select sub look");
        c.Fails(new { cmd = "select", sub = chara + "/ik/Nose" }, "bad_args", "select sub with an unknown IK target → bad_args");
        c.Fails(new { cmd = "select", sub = "nonsense" }, "bad_args", "select sub without a kind → bad_args");
        c.Fails(new { cmd = "select", sub = item + "/look" }, "wrong_kind", "select sub on an item → wrong_kind");
        c.Fails(new { cmd = "select", key = "nosuchnode" }, "not_found", "select an unknown key → not_found");
        c.Fails(new { cmd = "select" }, "bad_args", "select without key / keys / sub → bad_args");
        var other = c.Ok(new { cmd = "select", key = item }, "select another node");
        c.Check(other["sub"]?.Type == JTokenType.Null, "selecting another node clears the sub-selection", Ctx.Short(other.ToString()));
        c.Ok(new { cmd = "select", sub = chara + "/look" }, "select sub look again");
        var clr = c.Ok(new Dictionary<string, object> { ["cmd"] = "select", ["sub"] = null }, "select sub null");
        c.Check(clr["sub"]?.Type == JTokenType.Null && (string)clr["primary"] == chara, "select sub null clears only the sub-selection", Ctx.Short(clr.ToString()));
        var none = c.Ok(new { cmd = "select", keys = new string[0] }, "select keys []");
        c.Check((none["selected"] as JArray)?.Count == 0 && none["primary"]?.Type == JTokenType.Null, "select keys [] clears the selection");
        c.Check(W1UndoCount(c) == n0, "select / selection record no history entry", "" + W1UndoCount(c));
    }

    static void C26Clothes(Ctx c, string chara)
    {
        var r0 = c.Ok(new { cmd = "chara.clothes", key = chara }, "chara.clothes (read)");
        var states = (r0["states"] as JArray)?.Select(x => (int)x).ToArray();
        var sup = r0["supported"] as JArray;
        c.Check(states != null && states.Length == 8 && states.All(x => x == 0) && sup?.Count == 8 && (r0["realized"] as JArray)?.Count == 8 && (r0["changed"] as JArray)?.Count == 0,
            "chara.clothes read: 8 states (fresh = on), 8 supported lists, realized, changed []", Ctx.Short(r0.ToString()));
        var real = c.App.Runtime.FindChara(chara);
        var rules = real?.ClothesStates;
        c.Check(rules != null && rules.Known && Enumerable.Range(0, 8).All(k => ((JArray)sup[k]).Select(x => (int)x).SequenceEqual(rules.Supported(k))),
            "supported = CharaRealization.ClothesStates of the built outfit", rules?.ToString());
        c.Info("BR-Chan outfit: " + rules);
        c.Fails(new { cmd = "chara.clothes", key = chara, all = 3 }, "bad_args", "chara.clothes all 3 → bad_args");
        c.Fails(new { cmd = "chara.clothes", key = chara, all = 0, states = new { top = 1 } }, "bad_args", "chara.clothes all and states → bad_args");
        c.Fails(new { cmd = "chara.clothes", key = chara, states = new { hat = 1 } }, "bad_args", "chara.clothes states with an unknown kind → bad_args");
        c.Fails(new { cmd = "chara.clothes", key = chara, states = new { top = "half" } }, "bad_args", "chara.clothes states with a non-integer state → bad_args");
        c.Fails(new { cmd = "chara.clothes", key = chara, states = new { top = 7 } }, "bad_args", "chara.clothes state 7 → bad_args");
        c.Fails(new { cmd = "chara.clothes", key = W1AddItem(c, "item.add (wrong kind)"), all = 0 }, "wrong_kind", "chara.clothes on an item → wrong_kind");
        c.Ok(new { cmd = "undo" }, "undo the item add");
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var r1 = c.Ok(new { cmd = "chara.clothes", key = chara, states = new Dictionary<string, object> { ["top"] = 1 } }, "chara.clothes states {top: 1}");
        var want = rules.Apply(new int[8], 0, 1);
        var got = (r1["states"] as JArray)?.Select(x => (int)x).ToArray();
        c.Check(got != null && got.SequenceEqual(want), "stored states = ClothesStateSet.Apply (HS2's cycling / couplings computed in the op)", string.Join(",", got ?? new int[0]) + " want " + string.Join(",", want));
        c.Check(W1UndoCount(c) == (want.SequenceEqual(new int[8]) ? n0 : n0 + 1) && (want.SequenceEqual(new int[8]) || W1TopLabel(c) == "Clothes"), "one entry \"Clothes\"", W1UndoCount(c) + " " + W1TopLabel(c));
        var d1 = c.Ok(new { cmd = "chara.clothesDump", key = chara }, "chara.clothesDump");
        var k0 = d1["kinds"]?[0];
        c.Check(d1["states"] is JArray && (d1["supported"] as JArray)?.Count == 8 && d1["flags"]?["notBot"] != null && (d1["kinds"] as JArray)?.Count == 8
                && d1["masks"]?["body"] != null && d1["masks"]?["bra"] != null && d1["masks"]?["panst"] != null,
            "clothesDump shape: states, supported, flags, kinds [8], masks {body, bra, innerTB, innerB, panst}", Ctx.Short(d1.ToString()));
        if (k0 != null && (bool)k0["present"] && want[0] == 1)
            c.Check(k0["topHalf"]?.Type != JTokenType.Boolean || (bool)k0["topHalf"] == true, "top half: the top's half object active (numbers only)", Ctx.Short(k0.ToString()));
        var all = c.Ok(new { cmd = "chara.clothes", key = chara, all = 2 }, "chara.clothes all 2 (off)");
        var d2 = c.Ok(new { cmd = "chara.clothesDump", key = chara }, "chara.clothesDump after all off");
        bool offOk = true;
        var allStates = (all["states"] as JArray).Select(x => (int)x).ToArray();
        for (int k = 0; k < 8; k++)
        {
            var kk = d2["kinds"][k];
            if ((bool)kk["present"] && allStates[k] == 2 && kk["active"]?.Type == JTokenType.Boolean && (bool)kk["active"]) { offOk = false; c.Info("kind " + k + " still active: " + kk); }
        }
        c.Check(offOk, "all off: every present part whose stored state is off is inactive");
        var bodyA = d2["masks"]?["body"]?["_alpha_a"];
        if (bodyA != null && bodyA.Type != JTokenType.Null && rules.IsStateKind(0))
            c.Check(Math.Abs((double)bodyA) < 1e-6 && Math.Abs((double)d2["masks"]["body"]["_alpha_b"]) < 1e-6, "top off: body _alpha_a = _alpha_b = 0", Ctx.Short(d2["masks"].ToString()));
        c.Ok(new { cmd = "undo" }, "undo all off");
        c.Ok(new { cmd = "undo" }, "undo top half");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
        var d3 = c.Ok(new { cmd = "chara.clothesDump", key = chara }, "chara.clothesDump after undo");
        bool onOk = (d3["kinds"] as JArray).All(kk => !(bool)kk["present"] || kk["active"]?.Type != JTokenType.Boolean || (bool)kk["active"] || ((string)kk["kind"] == "bottom" && (bool)d3["flags"]["notBot"]) || ((string)kk["kind"] == "bra" && (bool)d3["flags"]["notInnerT"]) || ((string)kk["kind"] == "shorts" && (bool)d3["flags"]["notInnerB"]));
        var a3 = d3["masks"]?["body"]?["_alpha_a"];
        bool alphaOk = a3 == null || a3.Type == JTokenType.Null || Math.Abs((double)a3 - 1) < 1e-6;
        c.Check(onOk && alphaOk, "after undo: parts active again, body _alpha_a = 1", Ctx.Short(d3["masks"]?.ToString()));
        c.Ok(new { cmd = "redo" }, "redo top half");
        var r4 = c.Ok(new { cmd = "chara.clothes", key = chara }, "chara.clothes read after redo");
        c.Check((r4["states"] as JArray).Select(x => (int)x).SequenceEqual(want), "redo restores the coupled states");
        c.Ok(new { cmd = "undo" }, "undo again");
    }

    static void C26Accessories(Ctx c, string chara)
    {
        var r0 = c.Ok(new { cmd = "chara.accessories", key = chara }, "chara.accessories (read)");
        var map = r0["accessories"] as JObject;
        c.Check(map != null, "chara.accessories read: accessories {id: bool}", Ctx.Short(r0.ToString()));
        if (map == null || map.Count == 0) { c.Warn("BR-Chan wears no accessory: show / all not exercised"); return; }
        int n0 = W1UndoCount(c);
        var off = c.Ok(new { cmd = "chara.accessories", key = chara, all = false }, "chara.accessories all false");
        c.Check(((JObject)off["accessories"]).Properties().All(p => (bool)p.Value == false) && (off["changed"] as JArray)?.Count == map.Count && W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Accessories",
            "all false: every accessory hidden, changed = all, one entry \"Accessories\"", Ctx.Short(off.ToString()));
        string first = map.Properties().First().Name;
        var on = c.Ok(new { cmd = "chara.accessories", key = chara, show = new Dictionary<string, object> { [first] = true } }, "chara.accessories show {id: true}");
        c.Check((bool)on["accessories"][first] && (on["changed"] as JArray)?.Count == 1, "show by id", Ctx.Short(on.ToString()));
        c.Fails(new { cmd = "chara.accessories", key = chara, show = new Dictionary<string, object> { ["nosuchaccessory"] = true } }, "not_found", "show an id the character does not wear → not_found");
        c.Fails(new { cmd = "chara.accessories", key = chara, show = new Dictionary<string, object> { [first] = 1 } }, "bad_args", "show with a non-bool → bad_args");
        var real = c.App.Runtime.FindChara(chara);
        var slot = real?.Context?.Accessories?.FirstOrDefault(s => s != null && s.Object && !s.IsEmpty && real.AccessoryIdOf(s) == first);
        if (slot != null) c.Check(slot.Object.activeSelf, "the shown accessory's object is active");
        c.Ok(new { cmd = "undo" }, "undo show");
        c.Ok(new { cmd = "undo" }, "undo all false");
    }

    static void C26State(Ctx c, string chara)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var r = c.Ok(new { cmd = "chara.state", key = chara, set = new Dictionary<string, object> { ["wet"] = 0.5, ["gloss"] = 0.25, ["liquid[1]"] = 2, ["tears"] = 0.5, ["son.length"] = 1.5 } }, "chara.state wet, gloss, liquid[1], tears, son.length");
        var ch = (r["changed"] as JArray)?.Select(x => (string)x).ToList();
        c.Check(ch != null && ch.Count == 5 && (r["noop"] as JArray)?.Count == 0 && W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Character state",
            "chara.state: 5 members changed, one entry \"Character state\"", Ctx.Short(r.ToString()));
        var st = (c.App.Session.Model.Find(chara).State as CharaNodeState);
        c.Check(st.State.Wet == 0.5f && st.State.Gloss == 0.25f && st.State.Liquid[1] == 2 && st.Face.Tears == 0.5f && st.State.Son.Length == 1.5f, "the model holds the values (no clamping)");
        var real = c.App.Runtime.FindChara(chara);
        var body = real?.Assembler?.LiveBodyDraw();
        if (body && body.HasProperty("_ExGloss")) c.Check(Math.Abs(body.GetFloat("_ExGloss") - 0.5f) < 1e-6, "body _ExGloss = wet", "" + body.GetFloat("_ExGloss"));
        var again = c.Ok(new { cmd = "chara.state", key = chara, set = new Dictionary<string, object> { ["wet"] = 0.5 } }, "chara.state wet again");
        c.Check((again["noop"] as JArray)?.Select(x => (string)x).SequenceEqual(new[] { "wet" }) == true && W1UndoCount(c) == n0 + 1, "same value: noop, no entry", Ctx.Short(again.ToString()));
        c.Fails(new { cmd = "chara.state", key = chara, set = new { hat = 1 } }, "bad_args", "chara.state unknown member → bad_args");
        c.Fails(new { cmd = "chara.state", key = chara, set = new { wet = "x" } }, "bad_args", "chara.state wrong type → bad_args");
        c.Fails(new { cmd = "chara.state", key = chara }, "bad_args", "chara.state without set → bad_args");
        c.Ok(new { cmd = "undo" }, "undo chara.state");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
    }

    static void C26RenameReorder(Ctx c, string item, string item2)
    {
        int n0 = W1UndoCount(c);
        var r = c.Ok(new { cmd = "obj.rename", key = item, name = "Box C26" }, "obj.rename");
        c.Check((string)r["name"] == "Box C26" && (string)r["label"] == "Box C26" && W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Rename", "obj.rename: name, label, one entry \"Rename\"", Ctx.Short(r.ToString()));
        var d = c.Ok(new Dictionary<string, object> { ["cmd"] = "obj.rename", ["key"] = item, ["name"] = null }, "obj.rename null");
        c.Check(d["name"]?.Type == JTokenType.Null && !string.IsNullOrEmpty((string)d["label"]) && (string)d["label"] != "Box C26", "obj.rename null: the default name", Ctx.Short(d.ToString()));
        c.Fails(new { cmd = "obj.rename", key = item }, "bad_args", "obj.rename without name → bad_args");
        var n = c.App.Session.Model.Find(item2);
        int before = n.Index;
        var o = c.Ok(new { cmd = "obj.reorder", key = item2, index = 0 }, "obj.reorder index 0");
        c.Check((int)o["index"] == 0 && W1TopLabel(c) == "Reorder", "obj.reorder: index 0, one entry \"Reorder\"", Ctx.Short(o.ToString()) + " (was " + before + ")");
        c.Fails(new { cmd = "obj.reorder", key = item2, index = -1 }, "bad_args", "obj.reorder -1 → bad_args");
        c.Fails(new { cmd = "obj.reorder", key = item2 }, "bad_args", "obj.reorder without index → bad_args");
        c.Ok(new { cmd = "undo" }, "undo reorder");
        c.Check(c.App.Session.Model.Find(item2).Index == before, "undo restores the index");
    }

    static void C26Change(Ctx c, string chara)
    {
        c.Ok(new { cmd = "obj.transform", key = chara, pos = new[] { 1.5, 0, -2 } }, "obj.transform before the change");
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var r = c.Ok(new { cmd = "chara.change", key = chara, path = c.MaleRel, sex = "male" }, "chara.change BR-Chan → HERO107");
        c.Check((r["changed"] as JArray)?.Select(x => (string)x).SequenceEqual(new[] { chara }) == true && W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Change character",
            "chara.change: changed [id], one entry \"Change character\"", Ctx.Short(r.ToString()));
        c.Frames(1);
        var real = c.App.Runtime.FindChara(chara);
        var n = c.App.Session.Model.Find(chara);
        c.Check(n != null && real != null && real.State == RealizationState.Built && real.Sex == 0, "the same node is built as the male card", real?.State + " sex " + real?.Sex);
        c.Check(n != null && Math.Abs(n.Doc.Transform.Pos.X - 1.5f) < 1e-6 && Math.Abs(n.Doc.Transform.Pos.Z + 2f) < 1e-6, "the node keeps its transform");
        c.Fails(new { cmd = "chara.change", key = chara, path = "nosuchcard.png", sex = "female" }, "not_found", "chara.change with a missing card → not_found");
        c.Ok(new { cmd = "undo" }, "undo chara.change");
        c.Frames(1);
        real = c.App.Runtime.FindChara(chara);
        c.Check(real != null && real.State == RealizationState.Built && real.Sex == 1 && W1DocSha(c) == sha0, "undo: the female appearance is back, scene.doc as before", real?.State + " sex " + real?.Sex);
    }

    static void C26Import(Ctx c)
    {
        var save = c.Ok(new { cmd = "scene.save", name = "c26_import", overwrite = true, thumb = "placeholder" }, "scene.save c26_import");
        int count = c.App.Session.Model.Count;
        c.Ok(new { cmd = "scene.new" }, "scene.new");
        string keep = W1AddItem(c, "item.add in the target scene");
        int n0 = W1UndoCount(c);
        var r = c.Ok(new { cmd = "scene.import", path = "c26_import" }, "scene.import c26_import");
        var added = (r["added"] as JArray)?.Select(x => (string)x).ToList();
        c.Check(added != null && added.Count > 0 && (int)r["nodes"] == count && c.App.Session.Model.Count == count + 1, "scene.import: every node of the file added", Ctx.Short(r.ToString()) + " file nodes " + count);
        c.Check(W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Import scene", "one entry \"Import scene\"", W1UndoCount(c) + " " + W1TopLabel(c));
        c.Check(added != null && c.App.Session.Selection.Ids.SequenceEqual(added), "the imported top nodes are selected");
        c.Check(added != null && added.All(id => c.App.Session.Model.Find(id)?.Parent == null) && c.App.Session.Model.Find(keep) != null, "added at the root; the scene's own node kept");
        var charas = c.App.Session.Model.PreOrder().Where(x => x.Kind == NodeKind.Chara).ToList();
        c.Frames(1);
        c.Check(charas.Count == 1 && c.App.Runtime.FindChara(charas[0].Id)?.State == RealizationState.Built, "the imported character is built from its documents");
        c.Fails(new { cmd = "scene.import", path = "nosuchscene" }, "not_found", "scene.import of a missing file → not_found");
        c.Ok(new { cmd = "undo" }, "undo scene.import");
        c.Check(c.App.Session.Model.Count == 1, "undo removes the imported nodes", "" + c.App.Session.Model.Count);
        try { if (save["path"] != null) File.Delete((string)save["path"]); } catch (Exception) { }
    }
}

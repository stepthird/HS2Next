// CT1: the mosaic rule at the constraint commands (Phase 6.5 package C). A bone the guard names, as a target of
// constraint.add and of constraint.set, answers unsupported with the rule's text, stores nothing and adds no history entry; so does a
// batch member. The same bone as a source, a shared bone of the body, a bone the guard does not name, a node and an IK target stay allowed.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Chara.Mosaic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static MakerTestKit;
using Object = UnityEngine.Object;

public static partial class MosaicTest
{
    const string CtText = "mosaic parts are not editable";

    static partial void ConstraintCommandGroups()
    {
        if (Want("CT1")) Guard("CT1 constraint targets refused", Ct1);
    }

    static bool CtRefused(AmHost h, object q, string what)
    {
        string before = h.Session.Model.Constraints?.ToJson();
        int undo = h.Session.Commands.UndoList.Count;
        var r = h.Call(q);
        bool ok = Check("CT1 " + what + " -> unsupported, the rule's text", r.Value<bool?>("ok") == false && r.Value<string>("code") == "unsupported" && (string)r["error"] == CtText, Short(r.ToString()));
        ok &= Check("CT1 " + what + ": nothing stored, no entry", h.Session.Model.Constraints?.ToJson() == before && h.Session.Commands.UndoList.Count == undo);
        return ok;
    }

    static void Ct1()
    {
        var trash = new List<Object>();
        using (var h = NewAmHost())
        {
            string key = AmLoad(h, male: false);
            var r = h.Rt.FindChara(key);
            var ctx = r.Context;
            var slot = ctx.Accessories.FirstOrDefault(x => x != null && x.Object && r.AccessoryIdOf(x) != null);
            if (!Check("CT1 an accessory on the target", slot != null)) return;
            string id = r.AccessoryIdOf(slot);
            var top = slot.Object.transform;
            var folder = h.Session.AddFolder();
            var other = h.Session.AddFolder();
            h.Rt.ApplyNow();
            string node = folder.NewId;

            // a part that keeps the mosaic in place: a member by name, a member by its material, and the parts around them
            var part = AmChild(top, "am_part");
            AmChild(part, "o_gomu", renderer: true);
            var viaShader = AmChild(part, "am_shader", renderer: true);
            var mat = new Material(Shader.Find(MosaicSet.ShaderName));
            trash.Add(mat);
            viaShader.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var arm = AmChild(top, "am_arm");
            AmSkinned(top, "o_dan_f", arm, trash);
            AmChild(top, "am_side");
            string A(string n) => "acc:" + id + "/" + n;
            var guarded = new[] { "o_gomu", "am_shader", "am_part", "o_dan_f", "am_arm", top.name };
            foreach (var n in guarded)
                CtRefused(h, new { cmd = "constraint.add", source = new { key = node }, target = new { key, bone = A(n) }, components = new[] { "rotation" } }, "add on " + (n == top.name ? "the slot object" : n));

            // set: an entry on a free bone, retargeted onto a guarded one
            var free = h.Ok(new { cmd = "constraint.add", source = new { key = node }, target = new { key, bone = "cf_J_Kosi02" }, components = new[] { "rotation" } }, "constraint.add a shared bone of the body");
            string entry = (string)free["constraint"];
            foreach (var n in guarded)
                CtRefused(h, new { cmd = "constraint.set", constraint = entry, target = new { key, bone = A(n) } }, "set onto " + (n == top.name ? "the slot object" : n));

            // a batch member, and the entry that stays
            var batch = h.Call(new { cmd = "batch", cmds = new object[] { new { cmd = "constraint.add", source = new { key = node }, target = new { key, bone = A("o_gomu") }, components = new[] { "rotation" } } } });
            Check("CT1 a batch member -> unsupported", batch.Value<bool?>("ok") == false && (string)batch["results"]?[0]?["code"] == "unsupported" && h.Session.Model.Constraints.Count == 1, Short(batch.ToString()));

            // what stays allowed
            h.Ok(new { cmd = "constraint.add", source = new { key, bone = A("o_gomu") }, target = new { key = other.NewId }, components = new[] { "position" } }, "constraint.add a guarded bone as a source");
            h.Ok(new { cmd = "constraint.add", source = new { key = node }, target = new { key, bone = A("am_side") }, components = new[] { "rotation" } }, "constraint.add a sibling without a member");
            h.Ok(new { cmd = "constraint.add", source = new { key = node }, target = new { key }, components = new[] { "position" } }, "constraint.add the character's node");
            h.Ok(new { cmd = "constraint.add", source = new { key = node }, target = new { key, ik = "LeftFoot" }, components = new[] { "position" } }, "constraint.add an IK target");
            foreach (var n in new[] { "cf_J_Kokan", "cf_J_LegUp00_L", "cm_J_dan100_00" })
            {
                Check($"CT1 {n}: not guarded", !MosaicSet.GuardsBone(ctx, ctx.BodyBones.TryGetValue(n, out var b) ? b : null));
                h.Ok(new { cmd = "constraint.add", source = new { key = node }, target = new { key, bone = n }, components = new[] { "rotation" } }, "constraint.add " + n);
            }
            h.Ok(new { cmd = "constraint.clear" }, "constraint.clear");
            foreach (var o in trash) Object.DestroyImmediate(o);
        }
    }
}

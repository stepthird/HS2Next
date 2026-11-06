// ConstraintTest, the plan and what it survives (Phase 6.5 package E), headless on the SFW targets, by numbers.
// The list is edited through the edit command, as the operations do.
//   CO2   the chain across two characters, in one frame: a node on A's bone, B's hand on that node, a node on B's bone
//   CB1   a rebuild: entries on hair, clothes, accessory, body and head bones and on an IK target act on the new body
//   CB2   a body that lacks a name: targetMissing, a finding, the entry saved; undo: active
//   CB3   a coordinate change, a head swap, an accessory's removal and undo
//   CB4   a delete and its undo: the target back on the model; a deleted character is not stepped by the pass
//   CB5   a forgotten node: the entry stays and a retarget works
//   CB9   an item's bone as a source: by name, by #n, stale after a change of the ref
//   CB10  a character not built at load: pending, then active, no finding
//   CB11  values alone compile nothing: a weight, an offset, a label; a stack sets one value
//   CG11  a loop on the built hierarchy: a tree move suspends the highest index, its undo brings it back; what an edit asks before it runs
//   CM9   the arithmetic against the engine: rotations and each component on a node, a bone and an IK target, read back from the transforms
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using HS2Next.Content;
using HS2Next.Motion;
using HS2Next.Motion.Ik;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class ConstraintTest
{
    const string SaveClockE = "2026-11-06T00:00:00Z";

    static void Headless_C_Compile()
    {
        Group("CO2", "the chain across characters in one frame", CO2);
        Group("CB1", "a rebuild", CB1);
        Group("CB2", "a body that lacks a name", CB2);
        Group("CB3", "a coordinate, a head, an accessory", CB3);
        Group("CB4", "delete and undo", CB4);
        Group("CB5", "a forgotten node", CB5);
        Group("CB9", "an item's bone as a source", CB9);
        Group("CB10", "a character not built at load", CB10);
        Group("CB11", "values alone, a stack", CB11);
        Group("CG11", "a loop on the built hierarchy", CG11);
        Group("CM9", "the arithmetic against the engine", CM9);
        Group("CW5", "an empty list costs nothing", CW5);
        Group("CW6", "a compile's time", CW6);
    }

    // ------------------------------------------------------------ helpers

    static int entryNumber;

    static ConstraintEntry CEntry(ConstraintEnd source, ConstraintEnd target, params string[] components) => new ConstraintEntry
    {
        Id = (0xc0000000L + ++entryNumber).ToString("x32"),
        Source = source,
        Target = target,
        Components = components.ToList(),
    };

    static void Put(Host h, string what, params ConstraintEntry[] entries)
    {
        var r = h.Session.Commands.Execute(ConstraintEditCommand.ReplaceList(new ConstraintList(entries), ConstraintEditCommand.LabelAdd));
        Check(what, r.Ok, r.Ok ? "" : r.Code + ": " + r.Message);
        h.Rt.ApplyNow();
    }

    static ConstraintPart Part(Host h) => h.Rt.GetPart<ConstraintPart>();
    static string StateOf(Host h, ConstraintEntry e) => Part(h).RowOf(e.Id)?.State;

    static string LoadChara(Host h, string rel, string sex, string what)
    {
        var k = Key(h.Ok(new { cmd = "chara.load", path = rel, sex }, what));
        h.Ok(new { cmd = "chara.motion", key = k, physics = false }, what + ": physics off");
        return k;
    }

    static string Node(Host h, string what) => Key(h.Ok(new { cmd = "item.add", no = 399 }, what));

    static void Place(Host h, string id, Vector3 pos, Vector3 rot = default)
    {
        h.Session.Set(id, "transform.pos", new Vec3(pos.x, pos.y, pos.z));
        h.Session.Set(id, "transform.rot", new Vec3(rot.x, rot.y, rot.z));
        h.Rt.ApplyNow();
    }

    static int ConstraintFinding(Host h, string code, string id)
    {
        foreach (var x in h.Rt.Findings) if (x.Code == code && x.NodeId == id) return x.Count;
        return 0;
    }

    static float Dist(Transform a, Transform b) => (a.position - b.position).magnitude;

    // ------------------------------------------------------------ CO2

    static void CO2()
    {
        var h = NewHost();
        try
        {
            var a = LoadChara(h, femaleRel, "female", "A");
            var b = LoadChara(h, femaleRel, "female", "B");
            foreach (var k in new[] { a, b }) h.Ok(new { cmd = "chara.anime", key = k, group = 19, category = 0, no = 0, time = 0f, speed = 1f }, "the row");
            h.Ok(new { cmd = "ik.copy", key = b, from = "current", enable = true }, "IK on B");
            h.Frames(2);
            var ra = h.Rt.FindChara(a);
            var rb = h.Rt.FindChara(b);
            var handA = AdvancedPoseResolver.Resolve(ra, "cf_J_Hand_R");
            var handB = AdvancedPoseResolver.Resolve(rb, "cf_J_Hand_L");
            Place(h, b, handA.position - handB.position);
            h.Frames(1);
            string n1 = Node(h, "N1"), n2 = Node(h, "N2");
            var e1 = CEntry(new ConstraintEnd(a, "cf_J_Hand_R"), new ConstraintEnd(n1), ConstraintComponents.Parent);
            var e2 = CEntry(new ConstraintEnd(n1), new ConstraintEnd(b, ik: "LeftHand"), ConstraintComponents.Position);
            var e3 = CEntry(new ConstraintEnd(b, "cf_J_Hand_L"), new ConstraintEnd(n2), ConstraintComponents.Parent);
            // the list against the order of the frame: the sort decides, not the list
            Put(h, "the chain, its last link first", e3, e2, e1);
            var part = Part(h);
            Check("every entry is active", new[] { e1, e2, e3 }.All(e => StateOf(h, e) == ConstraintStates.Active), string.Join(",", new[] { e1, e2, e3 }.Select(e => StateOf(h, e))));
            Check("the pass is on", part.PassActive && h.Rt.Pass != null);
            Check("no lag on the chain", part.Lagged.Count == 0, string.Join(",", part.Lagged));
            string order = string.Join(" ", part.Order.Select(o => o.Unit == a ? "A" : o.Unit == b ? "B" : o.Unit == e1.Id ? "K1" : o.Unit == e2.Id ? "K2" : o.Unit == e3.Id ? "K3" : "?"));
            Check("A, then its node, then B's target, B, its node", order == "A K1 K2 B K3", order);

            var ik = IkDriver.Of(rb).Stage;
            var h1 = h.Rt.HolderOf(n1);
            var h2 = h.Rt.HolderOf(n2);
            int hand = Array.IndexOf(IkTargets.Names, "LeftHand");
            double worst1 = 0, worst2 = 0, worstHand = 0, worstReached = 0;
            int clamped = 0;
            for (int i = 0; i < 120; i++)
            {
                h.Frames(1);
                worst1 = Math.Max(worst1, Dist(h1, handA));
                worst2 = Math.Max(worst2, Dist(h2, handB));
                float residual = ik.LastSolve.Residual[hand];
                if (ik.LastSolve.Clamped[hand]) clamped++;
                else worstReached = Math.Max(worstReached, Dist(handB, h1));
                worstHand = Math.Max(worstHand, Math.Abs(Dist(handB, h1) - residual));
            }
            Measure($"CO2: N1 to A's hand {worst1:G3}, N2 to B's hand {worst2:G3}, B's hand to N1 less the solve's residual {worstHand:G3}, on frames it reaches {worstReached:G3}, clamped frames {clamped} of 120");
            Check("N1 stands on A's final bone in the frame", worst1 <= 1e-5, worst1.ToString("G3"));
            Check("N2 stands on B's final bone in the frame", worst2 <= 1e-5, worst2.ToString("G3"));
            Check("B's hand stands on N1 within the solve's residual, in the frame", worstHand <= 1e-4, worstHand.ToString("G3"));
            Check("on the frames the arm reaches, the hand is on N1", worstReached <= 1e-3, worstReached.ToString("G3"));
            Check("the arm reaches N1 on some frames", clamped < 120, clamped.ToString());
            Check("the target moved with the character at all", ik.TargetOverrides[hand].HasPos);
            Check("a solve ran on the driven target", ik.LastSolve.Valid);
            Check("the model's target is not written", !ik.Targets[hand].Pos.Equals(ik.TargetOverrides[hand].Pos));
        }
        finally { h.Dispose(); }
    }

    // ------------------------------------------------------------ CB1

    // an address of each kind of end on this body, the mosaic rule's bones aside
    static List<(string kind, string address)> Addresses(CharaRealization r)
    {
        var rows = AdvancedPoseResolver.Bones(r);
        var l = new List<(string, string)>();
        foreach (var (kind, start) in new[] { ("body", "body"), ("head", "head"), ("hair", "hair:"), ("clothes", "clothes:"), ("accessory", "acc:") })
        {
            var row = kind == "body" ? rows.FirstOrDefault(x => x.Address == "cf_J_Hand_Index02_R") :
                rows.FirstOrDefault(x => x.Part.StartsWith(start, StringComparison.Ordinal) && x.Parent != null && !AdvancedPoseGuard.Bone(r, x.Bone));
            if (row != null) l.Add((kind, row.Address));
        }
        return l;
    }

    static void CB1()
    {
        var h = NewHost();
        try
        {
            var a = LoadChara(h, femaleRel, "female", "the character");
            h.Ok(new { cmd = "ik.copy", key = a, from = "current", enable = true }, "IK on");
            h.Frames(2);
            var f = Node(h, "the source");
            var g = Node(h, "the IK source");
            Place(h, f, new Vector3(3, 7, 2), new Vector3(20, 40, 60));
            var r = h.Rt.FindChara(a);
            var hand = AdvancedPoseResolver.Resolve(r, "cf_J_Hand_L");
            Place(h, g, hand.position + new Vector3(0.1f, 0.1f, 0f));
            var ends = Addresses(r);
            Info("ends: " + string.Join(", ", ends.Select(x => x.kind + " " + x.address)));
            foreach (var k in new[] { "body", "head", "hair", "clothes", "accessory" })
                if (!ends.Any(x => x.kind == k)) Warn("no " + k + " bone on the target");
            var entries = ends.Select(x => CEntry(new ConstraintEnd(f), new ConstraintEnd(a, x.address), ConstraintComponents.Rotation)).ToList();
            var ikEntry = CEntry(new ConstraintEnd(g), new ConstraintEnd(a, ik: "LeftHand"), ConstraintComponents.Position);
            entries.Add(ikEntry);
            Put(h, "an entry on each", entries.ToArray());
            var src = h.Rt.HolderOf(f);
            var ikSrc = h.Rt.HolderOf(g);
            int ikHand = Array.IndexOf(IkTargets.Names, "LeftHand");

            void Acts(string when)
            {
                h.Rt.ApplyNow();
                var c = h.Rt.FindChara(a);
                h.Frames(3);
                foreach (var e in entries) Check(when + ": active: " + (e.Target.Bone ?? e.Target.Ik), StateOf(h, e) == ConstraintStates.Active, StateOf(h, e) + " " + Part(h).RowOf(e.Id)?.Detail);
                for (int i = 0; i < ends.Count; i++)
                {
                    var t = AdvancedPoseResolver.Resolve(c, ends[i].address);
                    if (!Check(when + ": " + ends[i].kind + " resolves", t)) continue;
                    float turn = Turn(t.rotation, src.rotation);
                    Check(when + ": " + ends[i].kind + " holds the source's rotation", turn < 5e-5f, ends[i].address + " " + turn.ToString("G3"));
                }
                var ik = IkDriver.Of(c).Stage;
                var bone = AdvancedPoseResolver.Resolve(c, "cf_J_Hand_L");
                Check(when + ": the target is on this body's solve", ik.TargetOverrides[ikHand].HasPos);
                Check(when + ": the hand reaches the IK source", Dist(bone, ikSrc) <= ik.LastSolve.Residual[ikHand] + 1e-4f, Dist(bone, ikSrc).ToString("G3"));
            }

            Acts("before");
            var root0 = r.Root;
            int compiles = Part(h).Compiles;
            h.Ok(new { cmd = "chara.change", key = a, path = femaleRel, sex = "female" }, "chara.change to the same card");
            h.Ok(new { cmd = "chara.motion", key = a, physics = false }, "physics off again");
            h.Ok(new { cmd = "ik.copy", key = a, from = "current", enable = true }, "IK on again");
            h.Rt.ApplyNow();
            Check("a new root", h.Rt.FindChara(a).Root != root0);
            Check("the build compiled the plan in its Apply", Part(h).Compiles > compiles);
            Acts("after chara.change");
            var root1 = h.Rt.FindChara(a).Root;
            Check("a rebuild request is taken", h.Rt.FindChara(a).RequestPluginRebuild("constraint.test"));
            h.Rt.ApplyNow();
            h.Ok(new { cmd = "chara.motion", key = a, physics = false }, "physics off again");
            Check("a new root again", h.Rt.FindChara(a).Root != root1);
            Acts("after a rebuild request");
        }
        finally { h.Dispose(); }
    }

    // ------------------------------------------------------------ CB2

    static Dictionary<string, byte[]> Zip(string path)
    {
        var d = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using (var ms = new MemoryStream(File.ReadAllBytes(path)))
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
            foreach (var e in zip.Entries)
                using (var s = e.Open())
                using (var o = new MemoryStream())
                {
                    s.CopyTo(o);
                    d[e.FullName] = o.ToArray();
                }
        return d;
    }

    static void CB2()
    {
        var h = NewHost();
        try
        {
            var a = LoadChara(h, femaleRel, "female", "the character");
            var m = LoadChara(h, maleRel, "male", "the other target, aside");
            var ra = h.Rt.FindChara(a);
            var rm = h.Rt.FindChara(m);
            string only = AdvancedPoseResolver.Bones(ra).Where(x => !AdvancedPoseGuard.Bone(ra, x.Bone)).Select(x => x.Address).FirstOrDefault(x => !AdvancedPoseResolver.Resolve(rm, x));
            h.Ok(new { cmd = "delete", key = m }, "the other target goes");
            if (!Check("an address only this body has", only != null, only)) return;
            var f = Node(h, "the source");
            Place(h, f, new Vector3(1, 2, 3), new Vector3(10, 20, 30));
            var e = CEntry(new ConstraintEnd(f), new ConstraintEnd(a, only), ConstraintComponents.Rotation);
            var keep = CEntry(new ConstraintEnd(f), new ConstraintEnd(a, "cf_J_Hand_Index02_L"), ConstraintComponents.Rotation);
            Put(h, "an entry on that name and one on a common name", e, keep);
            h.Frames(2);
            Check("active on this body", StateOf(h, e) == ConstraintStates.Active, StateOf(h, e));
            Check("no finding yet", ConstraintFinding(h, ConstraintFindings.Stale, a) == 0);

            h.Ok(new { cmd = "chara.change", key = a, path = maleRel, sex = "male" }, "a body that lacks the name");
            h.Ok(new { cmd = "chara.motion", key = a, physics = false }, "physics off");
            h.Rt.ApplyNow();
            Check("targetMissing", StateOf(h, e) == ConstraintStates.TargetMissing, StateOf(h, e));
            Check("the row names the address", Part(h).RowOf(e.Id).Detail == only, Part(h).RowOf(e.Id).Detail);
            Check("the common name acts", StateOf(h, keep) == ConstraintStates.Active, StateOf(h, keep));
            Check("the finding once", ConstraintFinding(h, ConstraintFindings.Stale, a) == 1, "" + ConstraintFinding(h, ConstraintFindings.Stale, a));
            Check("the finding names the bone", h.Rt.Findings.Any(x => x.Code == ConstraintFindings.Stale && x.NodeId == a && x.Message.Contains(only)));
            h.Frames(5);
            Place(h, f, new Vector3(1, 2, 4), new Vector3(10, 20, 30));
            Check("no second finding from what follows", ConstraintFinding(h, ConstraintFindings.Stale, a) == 1, "" + ConstraintFinding(h, ConstraintFindings.Stale, a));
            Check("the entry stays in the list", h.Session.Model.Constraints.Find(e.Id) != null);
            string path = (string)h.Ok(new { cmd = "scene.save", name = "cb2", overwrite = true, thumb = "placeholder", clock = SaveClockE }, "save with the stale entry")["path"];
            Check("saved in the file", Encoding.UTF8.GetString(Zip(path)["scene.json"]).IndexOf(e.Id, StringComparison.Ordinal) >= 0);

            h.Ok(new { cmd = "undo" }, "undo the move of the source");
            h.Ok(new { cmd = "undo" }, "undo the change of body");
            h.Rt.ApplyNow();
            var name = h.Session.Model.Find(a) != null ? AdvancedPoseResolver.Resolve(h.Rt.FindChara(a), only) : null;
            if (Check("the first body is back", name != null))
                Check("active again, no step of the user", StateOf(h, e) == ConstraintStates.Active, StateOf(h, e));
        }
        finally { h.Dispose(); }
    }

    // ------------------------------------------------------------ CB3

    static string OtherRowOf(Host h, string key, string path, string current)
    {
        var l = h.Call(new { cmd = "chara.edit.list", key, path, limit = 10000 });
        return (l["rows"] as JArray)?.Select(x => (string)x["ref"]).FirstOrDefault(x => x != current && x.StartsWith("illusion.hs2#", StringComparison.Ordinal));
    }

    static string EditGet(Host h, string key, string path) => (string)h.Call(new { cmd = "chara.edit.get", key, paths = new[] { path } })["values"]?[path];

    static void CB3()
    {
        var h = NewHost();
        try
        {
            var a = LoadChara(h, femaleRel, "female", "the character");
            h.Frames(2);
            var r = h.Rt.FindChara(a);
            var f = Node(h, "the source");
            Place(h, f, new Vector3(1, 2, 3), new Vector3(10, 20, 30));
            var src = h.Rt.HolderOf(f);

            // a coordinate: the clothes' bones go or come, the same frame says so
            var ends = Addresses(r);
            var clothes = ends.FirstOrDefault(x => x.kind == "clothes").address;
            var acc = ends.FirstOrDefault(x => x.kind == "accessory").address;
            // the head's deepest bone: the head's own object, where the top bones stay with the body
            var head = AdvancedPoseResolver.Bones(r, AdvancedPoseResolver.HeadPart).Where(x => !AdvancedPoseGuard.Bone(r, x.Bone)).OrderBy(x => x.Depth).LastOrDefault()?.Address;
            var body = CEntry(new ConstraintEnd(f), new ConstraintEnd(a, "cf_J_Hand_Index02_L"), ConstraintComponents.Rotation);
            var list = new List<ConstraintEntry> { body };
            ConstraintEntry eClothes = null, eAcc = null, eHead = null;
            if (clothes != null) list.Add(eClothes = CEntry(new ConstraintEnd(f), new ConstraintEnd(a, clothes), ConstraintComponents.Rotation));
            if (acc != null) list.Add(eAcc = CEntry(new ConstraintEnd(f), new ConstraintEnd(a, acc), ConstraintComponents.Rotation));
            if (head != null) list.Add(eHead = CEntry(new ConstraintEnd(f), new ConstraintEnd(a, head), ConstraintComponents.Rotation));
            Put(h, "entries on a part", list.ToArray());
            h.Frames(2);
            foreach (var e in list) Check("active: " + e.Target.Bone, StateOf(h, e) == ConstraintStates.Active, StateOf(h, e));

            void Holds(string when, ConstraintEntry e)
            {
                h.Frames(2);
                var t = AdvancedPoseResolver.Resolve(h.Rt.FindChara(a), e.Target.Bone);
                bool active = StateOf(h, e) == ConstraintStates.Active;
                Check(when + ": active on a bone, or missing without one", active == (t != null) && (active || StateOf(h, e) == ConstraintStates.TargetMissing), StateOf(h, e));
                if (t) Check(when + ": on this bone", Turn(t.rotation, src.rotation) < 5e-5f, e.Target.Bone + " " + Turn(t.rotation, src.rotation).ToString("G3"));
            }

            // a coordinate card
            var file = Path.Combine(h.App.Paths.Hs2UserData ?? "", "coordinate", "female", "ガーリーブラウス.png");
            if (!File.Exists(file)) Warn("the SFW coordinate card is missing: not run");
            else
            {
                h.Ok(new { cmd = "chara.edit.coordinate", key = a, file = "ガーリーブラウス.png", mode = "clothes" }, "a coordinate");
                if (eClothes != null) Holds("after the coordinate", eClothes);
                Holds("after the coordinate", body);
                h.Ok(new { cmd = "undo" }, "undo the coordinate");
                if (eClothes != null) Holds("after its undo", eClothes);
            }

            // a head swap
            string headRef = EditGet(h, a, "chara.face.head");
            string other = OtherRowOf(h, a, "chara.face.head", headRef);
            if (other == null) Warn("no other head row");
            else
            {
                var old = AdvancedPoseParts.Renderer(r, "head/o_head");
                var oldBone = head != null ? AdvancedPoseResolver.Resolve(r, head) : null;
                h.Ok(new { cmd = "chara.edit.item", key = a, path = "chara.face.head", @ref = other }, "a head swap");
                h.Frames(2);
                Check("the head is another object", AdvancedPoseParts.Renderer(r, "head/o_head") != old);
                if (head != null) Info("the head's bone " + head + (AdvancedPoseResolver.Resolve(r, head) != oldBone ? " is a new transform" : " stays"));
                if (eHead != null) Holds("after the head swap", eHead);
                Holds("after the head swap", body);
                h.Ok(new { cmd = "undo" }, "undo the head swap");
                if (eHead != null) Holds("after its undo", eHead);
            }

            // an accessory taken away
            if (eAcc != null)
            {
                string id = eAcc.Target.Bone.Substring(4, eAcc.Target.Bone.IndexOf('/') - 4);
                var slot = r.Context.Accessories.FirstOrDefault(s => s != null && r.AccessoryIdOf(s) == id);
                if (Check("the accessory's slot", slot != null))
                {
                    var c = h.Ok(new { cmd = "chara.edit.accessory", key = a, op = "remove", accessory = id }, "remove the accessory");
                    h.Rt.ApplyNow();
                    Check("the entry stays, targetMissing", StateOf(h, eAcc) == ConstraintStates.TargetMissing, StateOf(h, eAcc));
                    Check("the others act", StateOf(h, body) == ConstraintStates.Active, StateOf(h, body));
                    h.Ok(new { cmd = "undo" }, "undo the removal");
                    h.Rt.ApplyNow();
                    Holds("after the accessory's undo", eAcc);
                    Check("active again", StateOf(h, eAcc) == ConstraintStates.Active, StateOf(h, eAcc));
                }
            }
            else Warn("no accessory bone on the target");
        }
        finally { h.Dispose(); }
    }

    // ------------------------------------------------------------ CB4

    static void CB4()
    {
        var h = NewHost();
        try
        {
            var a = LoadChara(h, femaleRel, "female", "a character");
            var s = Node(h, "the source");
            var t = Node(h, "the target");
            Place(h, t, new Vector3(1, 2, 3));
            Place(h, s, new Vector3(5, 6, 7));
            var e = CEntry(new ConstraintEnd(s), new ConstraintEnd(t), ConstraintComponents.Position);
            var other = CEntry(new ConstraintEnd(a, "cf_J_Hand_R"), new ConstraintEnd(Node(h, "another target")), ConstraintComponents.Position);
            Put(h, "an entry", e, other);
            h.Frames(1);
            var ht = h.Rt.HolderOf(t);
            Check("the target stands on the source", (ht.position - new Vector3(5, 6, 7)).magnitude < 1e-5f, ht.position.ToString("F4"));

            h.Ok(new { cmd = "delete", key = s }, "delete the source");
            h.Frames(1);
            Check("sourceMissing", StateOf(h, e) == ConstraintStates.SourceMissing, StateOf(h, e));
            Check("the entry stays", h.Session.Model.Constraints.Find(e.Id) != null);
            Check("the target shows its model value", (ht.position - new Vector3(1, 2, 3)).magnitude < 1e-5f, ht.position.ToString("F4"));
            Check("no value is left on its channel", h.Rt.Overrides.WinnerOf(t, HS2Next.Scenes.Overrides.OverrideChannel.TransformPos) == null);
            Check("the pass stays for the other entry", Part(h).PassActive);
            h.Ok(new { cmd = "undo" }, "undo the delete");
            h.Frames(1);
            Check("active with no step of the user", StateOf(h, e) == ConstraintStates.Active, StateOf(h, e));
            Check("the target stands on the source again", (ht.position - new Vector3(5, 6, 7)).magnitude < 1e-5f, ht.position.ToString("F4"));

            h.Ok(new { cmd = "delete", key = t }, "delete the target");
            h.Frames(1);
            Check("targetMissing", StateOf(h, e) == ConstraintStates.TargetMissing, StateOf(h, e));
            h.Ok(new { cmd = "undo" }, "undo the delete");
            h.Frames(1);
            Check("active", StateOf(h, e) == ConstraintStates.Active, StateOf(h, e));
            Check("the revived holder shows the slot", (h.Rt.HolderOf(t).position - new Vector3(5, 6, 7)).magnitude < 1e-5f);

            // a deleted character stands in the graveyard while the pass runs
            var c = LoadChara(h, femaleRel, "female", "a second character");
            h.Frames(2);
            var rc = h.Rt.FindChara(c);
            var motion = rc.Motion;
            int before = motion.StepCount;
            h.Frames(3);
            Check("the pass steps it", motion.StepCount == before + 3, (motion.StepCount - before).ToString());
            h.Ok(new { cmd = "delete", key = c }, "delete the character");
            h.Frames(1);
            before = motion.StepCount;
            h.Frames(10);
            Check("the pass does not step it in the graveyard", motion.StepCount == before, (motion.StepCount - before).ToString());
            Check("it is no step of the plan", !Part(h).Order.Any(o => o.Unit == c));
            h.Ok(new { cmd = "undo" }, "undo the delete");
            h.Frames(3);
            Check("stepped again after the undo", motion.StepCount >= before + 3, (motion.StepCount - before).ToString());
            Check("a step of the plan again", Part(h).Order.Any(o => o.Unit == c));
        }
        finally { h.Dispose(); }
    }

    // ------------------------------------------------------------ CB5

    static void CB5()
    {
        var h = NewHost();
        try
        {
            var s = Node(h, "the source");
            var t = Node(h, "the target");
            var n = Node(h, "another source");
            Place(h, s, new Vector3(5, 6, 7));
            Place(h, n, new Vector3(8, 9, 10));
            Place(h, t, new Vector3(1, 2, 3));
            var e = CEntry(new ConstraintEnd(s), new ConstraintEnd(t), ConstraintComponents.Position);
            Put(h, "an entry", e);
            h.Frames(1);
            h.Ok(new { cmd = "delete", key = s }, "delete the source");
            h.Session.Commands.ClearHistory();
            h.Frames(1);
            Check("the entry stays, inactive", h.Session.Model.Constraints.Find(e.Id) != null && StateOf(h, e) == ConstraintStates.SourceMissing, StateOf(h, e));
            Check("no pass with no active entry", !Part(h).PassActive && h.Rt.Pass == null);
            var moved = h.Session.Model.Constraints.Find(e.Id).Clone();
            moved.Source = new ConstraintEnd(n);
            var r = h.Session.Commands.Execute(ConstraintEditCommand.ReplaceEntry(moved));
            Check("a retarget", r.Ok, r.Ok ? "" : r.Code + ": " + r.Message);
            h.Frames(1);
            Check("active on the new source", StateOf(h, moved) == ConstraintStates.Active, StateOf(h, moved));
            Check("the target follows it", (h.Rt.HolderOf(t).position - new Vector3(8, 9, 10)).magnitude < 1e-5f, h.Rt.HolderOf(t).position.ToString("F4"));
        }
        finally { h.Dispose(); }
    }

    // ------------------------------------------------------------ CB9

    static void CB9()
    {
        var h = NewHost();
        try
        {
            const int Bed = 1854, Tent = 1821;
            var it = Key(h.Ok(new { cmd = "item.add", no = Bed }, "an item"));
            h.Rt.ApplyNow();
            var t = Node(h, "the target");
            var real = h.Rt.FindItem(it);
            if (!Check("the item is built", real != null && real.State == RealizationState.Built && real.Instance)) return;
            // two transforms of one name under the instance, as a prefab can have them, and one of a name of its own
            var top = real.Instance.transform;
            var first = new GameObject("ct_twin").transform;
            first.SetParent(top, false);
            first.localPosition = new Vector3(1, 0, 0);
            var second = new GameObject("ct_twin").transform;
            second.SetParent(top, false);
            second.localPosition = new Vector3(0, 1, 0);

            var byName = CEntry(new ConstraintEnd(it, "ct_twin"), new ConstraintEnd(t), ConstraintComponents.Position);
            Put(h, "by name", byName);
            h.Frames(1);
            Check("active", StateOf(h, byName) == ConstraintStates.Active, StateOf(h, byName) + " " + Part(h).RowOf(byName.Id)?.Detail);
            Check("the first of the name", Dist(h.Rt.HolderOf(t), first) < 1e-5f);

            var byN = CEntry(new ConstraintEnd(it, "ct_twin#1"), new ConstraintEnd(t), ConstraintComponents.Position);
            Put(h, "by #n", byN);
            h.Frames(1);
            Check("the second of the name", Dist(h.Rt.HolderOf(t), second) < 1e-5f);

            var beyond = CEntry(new ConstraintEnd(it, "ct_twin#2"), new ConstraintEnd(t), ConstraintComponents.Position);
            Put(h, "no third", beyond);
            Check("sourceMissing for an occurrence the item lacks", StateOf(h, beyond) == ConstraintStates.SourceMissing, StateOf(h, beyond));

            Put(h, "a bone target on an item", CEntry(new ConstraintEnd(t), new ConstraintEnd(it, "ct_twin"), ConstraintComponents.Position));
            Check("state bad", Part(h).Rows[0].State == ConstraintStates.Bad, Part(h).Rows[0].State);
            Check("the finding", ConstraintFinding(h, ConstraintFindings.BadEntry, it) >= 1);

            Put(h, "by name again", byName);
            h.Frames(1);
            Check("active again", StateOf(h, byName) == ConstraintStates.Active, StateOf(h, byName));
            int stale = ConstraintFinding(h, ConstraintFindings.Stale, it);
            h.Session.Set(it, "item.ref", ContentId.Vanilla(Tent).ToString());
            h.Rt.ApplyNow();
            Check("stale after a change of the ref", StateOf(h, byName) == ConstraintStates.SourceMissing, StateOf(h, byName));
            Check("the finding", ConstraintFinding(h, ConstraintFindings.Stale, it) == stale + 1, "" + ConstraintFinding(h, ConstraintFindings.Stale, it));
            var fresh = h.Rt.FindItem(it).Instance.transform;
            var inner = fresh.childCount > 0 ? fresh.GetChild(0) : fresh;
            var byNew = CEntry(new ConstraintEnd(it, inner.name), new ConstraintEnd(t), ConstraintComponents.Position);
            Put(h, "a name of the new content", byNew);
            h.Frames(1);
            Check("a retarget by name acts", StateOf(h, byNew) == ConstraintStates.Active, StateOf(h, byNew));
        }
        finally { h.Dispose(); }
    }

    // ------------------------------------------------------------ CB11

    static void CB11()
    {
        var h = NewHost();
        try
        {
            string s1 = Node(h, "a source"), s2 = Node(h, "another source"), t = Node(h, "the target");
            Place(h, t, new Vector3(0, 0, 0));
            Place(h, s1, new Vector3(4, 0, 0));
            Place(h, s2, new Vector3(0, 8, 0));
            var e = CEntry(new ConstraintEnd(s1), new ConstraintEnd(t), ConstraintComponents.Position);
            Put(h, "an entry", e);
            h.Frames(1);
            var part = Part(h);
            var ht = h.Rt.HolderOf(t);
            Check("weight 1: on the source", (ht.position - new Vector3(4, 0, 0)).magnitude < 1e-5f, ht.position.ToString("F4"));

            int compiles = part.Compiles, version = part.Version;
            void Edit(string what, Action<ConstraintEntry> change)
            {
                var c = h.Session.Model.Constraints.Find(e.Id).Clone();
                change(c);
                var r = h.Session.Commands.Execute(ConstraintEditCommand.ReplaceEntry(c));
                Check(what, r.Ok, r.Ok ? "" : r.Code + ": " + r.Message);
                h.Frames(1);
            }
            Edit("a weight of 0.5", c => c.Weight = 0.5f);
            Check("it blends with the model", (ht.position - new Vector3(2, 0, 0)).magnitude < 1e-5f, ht.position.ToString("F4"));
            Edit("an offset", c => c.Offset = new ConstraintOffset(new Vec3(0, 2, 0), Vec3.Zero, Vec3.One));
            Check("the offset acts", (ht.position - new Vector3(2, 1, 0)).magnitude < 1e-5f, ht.position.ToString("F4"));
            Edit("a label", c => c.Label = "row");
            Check("values alone compile nothing", part.Compiles == compiles && part.Version == version, (part.Compiles - compiles).ToString());
            h.Ok(new { cmd = "undo" }, "undo the label");
            h.Ok(new { cmd = "undo" }, "undo the offset");
            h.Frames(1);
            Check("undo restores the value, with no compile", (ht.position - new Vector3(2, 0, 0)).magnitude < 1e-5f && part.Compiles == compiles, ht.position.ToString("F4"));

            Edit("a weight of 0", c => c.Weight = 0f);
            Check("that compiles", part.Compiles == compiles + 1);
            Check("state off", StateOf(h, e) == ConstraintStates.Off, StateOf(h, e));
            Check("the model shows in that frame", ht.position.magnitude < 1e-5f, ht.position.ToString("F4"));
            Check("no pass without an active entry", !part.PassActive);
            Edit("on again", c => c.Weight = 1f);
            Check("the pass again", part.PassActive && StateOf(h, e) == ConstraintStates.Active);
            Edit("switched off", c => c.On = false);
            Check("off shows the model", ht.position.magnitude < 1e-5f && StateOf(h, e) == ConstraintStates.Off);
            Edit("switched on", c => c.On = true);

            // a stack: 1 then 0.5 is the midpoint; the list decides; the slot is set once a frame and not at all at rest
            var first = CEntry(new ConstraintEnd(s1), new ConstraintEnd(t), ConstraintComponents.Position);
            var second = CEntry(new ConstraintEnd(s2), new ConstraintEnd(t), ConstraintComponents.Position);
            second.Weight = 0.5f;
            Put(h, "a stack", first, second);
            h.Frames(1);
            Check("1 then 0.5 is the midpoint", (ht.position - new Vector3(2, 4, 0)).magnitude < 1e-5f, ht.position.ToString("F4"));
            int values = h.Rt.Overrides.ValueVersion;
            h.Frames(10);
            Check("a resting stack sets nothing", h.Rt.Overrides.ValueVersion == values, (h.Rt.Overrides.ValueVersion - values).ToString());
            h.Session.Set(s1, "transform.pos", new Vec3(6, 0, 0));
            values = h.Rt.Overrides.ValueVersion;
            h.Frames(1);
            Check("a moved source sets the channel once", h.Rt.Overrides.ValueVersion == values + 1, (h.Rt.Overrides.ValueVersion - values).ToString());
            Check("and the stack follows in the frame", (ht.position - new Vector3(3, 4, 0)).magnitude < 1e-5f, ht.position.ToString("F4"));
            Put(h, "the other order", second, first);
            h.Frames(1);
            Check("the later entry at 1 covers the earlier", (ht.position - new Vector3(6, 0, 0)).magnitude < 1e-5f, ht.position.ToString("F4"));
        }
        finally { h.Dispose(); }
    }

    // ------------------------------------------------------------ CG11

    static void CG11()
    {
        var h = NewHost();
        try
        {
            string a = Node(h, "a"), b = Node(h, "b"), c = Node(h, "c"), d = Node(h, "d"), bChild = Node(h, "under b");
            h.Ok(new { cmd = "item.parent", key = bChild, parent = b }, "a child of b");
            var e1 = CEntry(new ConstraintEnd(a), new ConstraintEnd(c), ConstraintComponents.Position);
            var e2 = CEntry(new ConstraintEnd(d), new ConstraintEnd(a), ConstraintComponents.Position);
            var e3 = CEntry(new ConstraintEnd(bChild), new ConstraintEnd(d), ConstraintComponents.Position);
            Put(h, "three entries, no loop", e1, e2, e3);
            var part = Part(h);
            Check("every entry is active", part.Active == 3, part.Active.ToString());

            // what the operation asks before an edit: the reversed pair stands on a loop, a free pair on none
            var back = CEntry(new ConstraintEnd(c), new ConstraintEnd(a), ConstraintComponents.Position);
            var list = new ConstraintList(new[] { e1, e2, e3, back });
            var loop = part.LoopOf(list, back.Id);
            Check("a reversed pair closes a loop", loop.Length == 2 && loop[0] == 3 && loop[1] == 0, string.Join(",", loop));
            Check("no loop through the others of the list", part.LoopOf(new ConstraintList(new[] { e1, e2, e3 }), e3.Id).Length == 0);
            var self = CEntry(new ConstraintEnd(a), new ConstraintEnd(a), ConstraintComponents.Position);
            Check("a self-loop is its own row", part.LoopOf(new ConstraintList(new[] { self }), self.Id).Length == 1);
            Check("the row's text names the ends", ConstraintResolver.RowText(h.Rt, e1).Contains("\u2192"), ConstraintResolver.RowText(h.Rt, e1));

            // the tree move that closes one: never refused, the highest index is suspended
            h.Ok(new { cmd = "item.parent", key = b, parent = c }, "b goes under what e1 writes");
            h.Rt.ApplyNow();
            Check("the highest index is suspended", StateOf(h, e3) == ConstraintStates.Cycle, StateOf(h, e3));
            Check("the row names the loop", (part.RowOf(e3.Id).Detail ?? "").Contains(" -> "), part.RowOf(e3.Id).Detail);
            Check("the others run", StateOf(h, e1) == ConstraintStates.Active && StateOf(h, e2) == ConstraintStates.Active && part.Active == 2);
            Check("the finding, once", ConstraintFinding(h, ConstraintFindings.Cycle, d) == 1, ConstraintFinding(h, ConstraintFindings.Cycle, d).ToString());
            Check("the entry stays in the list", h.Session.Model.Constraints.Find(e3.Id) != null);
            h.Frames(2);
            Check("no second finding from frames", ConstraintFinding(h, ConstraintFindings.Cycle, d) == 1);
            h.Ok(new { cmd = "undo" }, "undo the move");
            h.Rt.ApplyNow();
            Check("active again", StateOf(h, e3) == ConstraintStates.Active && part.Active == 3, StateOf(h, e3));

            // an off entry neither stands on a loop at a compile nor suspends one that runs
            var offEntry = back.Clone();
            offEntry.On = false;
            Put(h, "the closing entry off", e1, offEntry);
            Check("off, and nothing suspended", StateOf(h, offEntry) == ConstraintStates.Off && StateOf(h, e1) == ConstraintStates.Active);
        }
        finally { h.Dispose(); }
    }

    // ------------------------------------------------------------ CM9

    static HS2Next.Scenes.Routes.Vec3d D(Vector3 v) => new HS2Next.Scenes.Routes.Vec3d(v.x, v.y, v.z);
    static Quaternion U(HS2Next.Scenes.Routes.Quatd q) => new Quaternion((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);

    // Quaternion.Angle answers 0 for anything under about 0.16 degrees, so these go through what a rotation does to the axes (about radians)
    static float Turn(Quaternion a, Quaternion b) => Mathf.Max((a * Vector3.right - b * Vector3.right).magnitude,
        Mathf.Max((a * Vector3.up - b * Vector3.up).magnitude, (a * Vector3.forward - b * Vector3.forward).magnitude));

    static float Aim(Vector3 a, Vector3 b) => Vector3.Cross(a.normalized, b.normalized).magnitude;

    static void CM9()
    {
        var rnd = new System.Random(659);
        float R(float a, float b) => a + (b - a) * (float)rnd.NextDouble();
        Vector3 V3(float a, float b) => new Vector3(R(a, b), R(a, b), R(a, b));
        float worstLook = 0, worstEuler = 0, worstBack = 0;
        for (int i = 0; i < 2000; i++)
        {
            var f = V3(-1, 1);
            var up = V3(-1, 1);
            if (f.magnitude < 0.1f || Vector3.Cross(f.normalized, up.normalized).magnitude < 0.05f) continue;
            worstLook = Mathf.Max(worstLook, Turn(Quaternion.LookRotation(f, up), U(HS2Next.Scenes.Constraints.ConstraintMath.Look(D(f), D(up)))));
            var e = V3(-180, 180);
            var q = Quaternion.Euler(e);
            worstEuler = Mathf.Max(worstEuler, Turn(q, U(HS2Next.Scenes.Routes.Quatd.Euler(e.x, e.y, e.z))));
            var back = HS2Next.Scenes.Constraints.ConstraintMath.ToEuler(new HS2Next.Scenes.Routes.Quatd(q.x, q.y, q.z, q.w));
            worstBack = Mathf.Max(worstBack, Turn(q, Quaternion.Euler((float)back.X, (float)back.Y, (float)back.Z)));
        }
        Measure($"CM9: look {worstLook:G3}, euler {worstEuler:G3}, euler back {worstBack:G3} against the engine, on the axes");
        Check("a look is the engine's", worstLook <= 1e-5f, worstLook.ToString("G3"));
        Check("euler angles are the engine's", worstEuler <= 1e-5f, worstEuler.ToString("G3"));
        Check("a rotation to euler and back is the engine's", worstBack <= 1e-4f, worstBack.ToString("G3"));

        var h = NewHost();
        try
        {
            var a = LoadChara(h, femaleRel, "female", "the character");
            h.Ok(new { cmd = "ik.copy", key = a, from = "current", enable = true }, "IK on");
            string s = Node(h, "the source"), parent = Node(h, "a parent"), t = Node(h, "the target"), loose = Node(h, "a target that does not inherit");
            h.Ok(new { cmd = "item.parent", key = t, parent }, "the target under the parent");
            h.Ok(new { cmd = "item.parent", key = loose, parent }, "the other under it too");
            h.Session.Set(loose, "transform.inheritScale", false);
            // a parent turned and scaled evenly; a source turned and scaled, away from every axis
            Place(h, parent, new Vector3(2, 3, 4), new Vector3(30, 50, 70));
            h.Session.Set(parent, "transform.scale", new Vec3(1.5f, 1.5f, 1.5f));
            Place(h, s, new Vector3(-3, 6, 2), new Vector3(10, 200, 40));
            h.Session.Set(s, "transform.scale", new Vec3(0.5f, 2f, 3f));
            h.Rt.ApplyNow();
            var hs = h.Rt.HolderOf(s);
            var ht = h.Rt.HolderOf(t);
            var hl = h.Rt.HolderOf(loose);
            var off = new ConstraintOffset(new Vec3(1, 2, 3), new Vec3(15, 25, 35), new Vec3(2, 1, 0.5f));
            var r = h.Rt.FindChara(a);
            var bone = AdvancedPoseResolver.Resolve(r, "cf_J_Hand_Index02_R");
            var root = r.Root.transform;

            void Case(string what, ConstraintEnd target, Action check, ConstraintOffset offset, params string[] components)
            {
                var e = CEntry(new ConstraintEnd(s), target, components);
                if (offset != null) e.Offset = offset;
                Put(h, what, e);
                h.Frames(2);
                if (Check(what + ": active", StateOf(h, e) == ConstraintStates.Active, StateOf(h, e) + " " + Part(h).RowOf(e.Id)?.Detail)) check();
            }
            var offRot = Quaternion.Euler(15, 25, 35);
            void Pos(string what, Vector3 got, Vector3 want) => Check(what, (got - want).magnitude < 2e-4f, got.ToString("F5") + " against " + want.ToString("F5"));
            void Rot(string what, Quaternion got, Quaternion want) => Check(what, Turn(got, want) < 5e-5f, Turn(got, want).ToString("G3"));
            void Scale(string what, Vector3 got, Vector3 want) => Check(what, (got - want).magnitude < 2e-4f, got.ToString("F5") + " against " + want.ToString("F5"));
            Vector3 Mul(Vector3 x, Vector3 y) => Vector3.Scale(x, y);
            var offScale = new Vector3(2, 1, 0.5f);
            var offPos = new Vector3(1, 2, 3);

            Case("position on a node under a parent", new ConstraintEnd(t), () => Pos("world axes", ht.position, hs.position + offPos), off, ConstraintComponents.Position);
            Case("rotation on a node", new ConstraintEnd(t), () => Rot("source times offset", ht.rotation, hs.rotation * offRot), off, ConstraintComponents.Rotation);
            Case("scale on a node that inherits", new ConstraintEnd(t), () => Scale("the world scale", ht.lossyScale, Mul(hs.lossyScale, offScale)), off, ConstraintComponents.Scale);
            Case("scale on a node that does not", new ConstraintEnd(loose), () => Scale("the world scale", hl.lossyScale, Mul(hs.lossyScale, offScale)), off, ConstraintComponents.Scale);
            Case("look at on a node", new ConstraintEnd(t), () =>
            {
                Check("+Z points at the source", Aim(ht.forward, hs.position - ht.position) < 5e-5f, Aim(ht.forward, hs.position - ht.position).ToString("G3"));
                Check("its right stays level", Mathf.Abs(ht.right.y) < 1e-4f, ht.right.y.ToString("G3"));
            }, null, ConstraintComponents.LookAt);
            Case("parent on a node", new ConstraintEnd(t), () =>
            {
                Pos("the source's frame", ht.position, hs.TransformPoint(offPos));
                Rot("the source's rotation", ht.rotation, hs.rotation * offRot);
            }, off, ConstraintComponents.Parent);
            Case("position and look at", new ConstraintEnd(t), () =>
            {
                Pos("the position", ht.position, hs.position + offPos);
                Check("the look aims from there", Aim(ht.forward, hs.position - ht.position) < 5e-5f, Aim(ht.forward, hs.position - ht.position).ToString("G3"));
            }, new ConstraintOffset(new Vec3(1, 2, 3), Vec3.Zero, Vec3.One), ConstraintComponents.Position, ConstraintComponents.LookAt);

            Case("position on a bone", new ConstraintEnd(a, "cf_J_Hand_Index02_R"), () => Pos("world axes", bone.position, hs.position + offPos), off, ConstraintComponents.Position);
            Case("rotation on a bone", new ConstraintEnd(a, "cf_J_Hand_Index02_R"), () => Rot("source times offset", bone.rotation, hs.rotation * offRot), off, ConstraintComponents.Rotation);
            Case("parent on a bone", new ConstraintEnd(a, "cf_J_Hand_Index02_R"), () =>
            {
                Pos("the source's frame", bone.position, hs.TransformPoint(offPos));
                Rot("the source's rotation", bone.rotation, hs.rotation * offRot);
            }, off, ConstraintComponents.Parent);
            Case("look at on a bone", new ConstraintEnd(a, "cf_J_Hand_Index02_R"), () =>
                Check("+Z points at the source", Aim(bone.forward, hs.position - bone.position) < 5e-5f, Aim(bone.forward, hs.position - bone.position).ToString("G3")), null, ConstraintComponents.LookAt);

            // an IK target holds root-local values, as the solve reads them back
            var ik = IkDriver.Of(r).Stage;
            int hand = Array.IndexOf(IkTargets.Names, "LeftHand");
            int chain = Array.IndexOf(IkTargets.Names, "LeftArmChain");
            Case("position and rotation on a hand's target", new ConstraintEnd(a, ik: "LeftHand"), () =>
            {
                Pos("the point in the world", root.TransformPoint(ik.TargetOverrides[hand].Pos), hs.position + offPos);
                Rot("the rotation in the world", root.rotation * Quaternion.Euler(ik.TargetOverrides[hand].Rot), hs.rotation * offRot);
            }, off, ConstraintComponents.Position, ConstraintComponents.Rotation);
            Case("parent on a target without rotation writes the position", new ConstraintEnd(a, ik: "LeftArmChain"), () =>
            {
                Pos("the point in the world", root.TransformPoint(ik.TargetOverrides[chain].Pos), hs.TransformPoint(offPos));
                Check("no rotation is laid over it", !ik.TargetOverrides[chain].HasRot);
            }, off, ConstraintComponents.Parent);

            // the offset a command takes keeps the target where it stands
            var keep = CEntry(new ConstraintEnd(s), new ConstraintEnd(t), ConstraintComponents.Parent);
            Put(h, "nothing on the target", CEntry(new ConstraintEnd(t), new ConstraintEnd(Node(h, "elsewhere")), ConstraintComponents.Position));
            h.Frames(1);
            var p0 = ht.position;
            var q0 = ht.rotation;
            Check("an offset is taken", Part(h).TryTakeOffset(keep, out var taken));
            keep.Offset = taken;
            Put(h, "the entry with its offset", keep);
            h.Frames(2);
            Pos("the target stays: position", ht.position, p0);
            Rot("the target stays: rotation", ht.rotation, q0);
            var missing = CEntry(new ConstraintEnd(s), new ConstraintEnd(a, "no_such_bone"), ConstraintComponents.Position);
            Check("no offset for an end that does not resolve", !Part(h).TryTakeOffset(missing, out _));
        }
        finally { h.Dispose(); }
    }

    // ------------------------------------------------------------ CW5, CW6

    static void CW5()
    {
        var h = NewHost();
        try
        {
            LoadChara(h, femaleRel, "female", "a character");
            h.Frames(2);
            var part = Part(h);
            Check("the part is there", part != null);
            Check("no pass, no driver, no slot", h.Rt.Pass == null && !part.PassActive && h.Rt.Root.GetComponent<MotionPassDriver>() == null && h.Rt.Overrides.Count == 0);
            const int N = 100000;
            for (int i = 0; i < 1000; i++) { part.Apply(); part.LateApply(); }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < N; i++) { part.Apply(); part.LateApply(); }
            double us = sw.Elapsed.TotalMilliseconds * 1000 / N;
            Measure($"CW5: Apply + LateApply on an empty list {us:F4} us");
            Check("at most 1 us a frame", us <= 1, us.ToString("F4"));
        }
        finally { h.Dispose(); }
    }

    static void CW6()
    {
        var h = NewHost();
        try
        {
            var a = LoadChara(h, femaleRel, "female", "A");
            var b = LoadChara(h, femaleRel, "female", "B");
            h.Frames(1);
            var folders = new List<string>();
            for (int i = 0; i < 400; i++)
            {
                var r = h.Session.AddFolder();
                folders.Add(r.NewIds[0]);
            }
            var part = Part(h);
            var bones = AdvancedPoseResolver.Bones(h.Rt.FindChara(a), AdvancedPoseResolver.BodyPart).Where(x => !AdvancedPoseGuard.Bone(h.Rt.FindChara(a), x.Bone)).Select(x => x.Address).Skip(30).ToList();
            foreach (int n in new[] { 64, 256 })
            {
                // the shape of the large case: IK channels, bones, nodes, each source on the other character or a free node
                var list = new List<ConstraintEntry>();
                for (int i = 0; i < n; i++)
                {
                    var source = i % 3 == 0 ? new ConstraintEnd(b, "cf_J_Hand_R") : new ConstraintEnd(folders[i]);
                    if (i < 13) list.Add(CEntry(source, new ConstraintEnd(a, ik: IkTargets.Names[i]), ConstraintComponents.Position));
                    else if (i < 13 + n * 3 / 8) list.Add(CEntry(source, new ConstraintEnd(a, bones[i % bones.Count]), ConstraintComponents.Rotation));
                    else list.Add(CEntry(source, new ConstraintEnd(folders[200 + i % 180]), ConstraintComponents.Parent));
                }
                Put(h, n + " entries", list.ToArray());
                h.Frames(1);
                Check(n + " entries are active", part.Active == n, part.Active + ", " + string.Join(",", part.Rows.Where(x => x.State != ConstraintStates.Active).Take(4).Select(x => x.State + " " + x.Detail)));
                var times = new List<double>();
                for (int k = 0; k < 15; k++)
                {
                    part.OnLoadedRealized();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    part.Apply();
                    times.Add(sw.Elapsed.TotalMilliseconds);
                }
                times.Sort();
                double median = times[times.Count / 2], bar = n == 64 ? 2 : 16;
                Measure($"CW6: compile of {n} entries, median {median:F3} ms, best {times[0]:F3}, worst {times[times.Count - 1]:F3}");
                Check("compile of " + n + " within " + bar + " ms", median <= bar, median.ToString("F3"));
            }
        }
        finally { h.Dispose(); }
    }

    // ------------------------------------------------------------ CB10

    static void CB10()
    {
        var h = NewHost();
        try
        {
            var a = LoadChara(h, femaleRel, "female", "the character");
            var f = Node(h, "the source");
            var e = CEntry(new ConstraintEnd(f), new ConstraintEnd(a, "cf_J_Hand_R"), ConstraintComponents.Rotation);
            var ik = CEntry(new ConstraintEnd(f), new ConstraintEnd(a, ik: "LeftHand"), ConstraintComponents.Position);
            var node = CEntry(new ConstraintEnd(a, "cf_J_Head"), new ConstraintEnd(f), ConstraintComponents.Position);
            Put(h, "entries on the character", e, ik, node);
            h.Frames(1);
            string path = (string)h.Ok(new { cmd = "scene.save", name = "cb10", overwrite = true, thumb = "placeholder", clock = SaveClockE }, "save")["path"];
            int findings = h.Rt.Findings.Count(x => x.Code.StartsWith("constraint.", StringComparison.Ordinal));

            var loaded = h.Session.Load(path);
            Check("the load", loaded.Value != null);
            Check("the list is there", h.Session.Model.Constraints?.Count == 3);
            Check("pending before the character is built", StateOf(h, e) == ConstraintStates.Pending, StateOf(h, e));
            Check("pending for the IK target too", StateOf(h, ik) == ConstraintStates.Pending, StateOf(h, ik));
            Check("no finding yet", h.Rt.Findings.Count(x => x.Code.StartsWith("constraint.", StringComparison.Ordinal)) == 0);
            h.Rt.ApplyNow();
            Check("the character is built", h.Rt.FindChara(a)?.State == RealizationState.Built);
            foreach (var x in new[] { e, ik, node }) Check("active", StateOf(h, x) == ConstraintStates.Active, StateOf(h, x) + " " + Part(h).RowOf(x.Id)?.Detail);
            Check("the pass is on", Part(h).PassActive);
            Check("no finding", h.Rt.Findings.Count(x => x.Code.StartsWith("constraint.", StringComparison.Ordinal)) == 0, findings.ToString());
            h.Frames(2);

            // a new scene ends it all
            h.Ok(new { cmd = "scene.new" }, "scene.new");
            h.Rt.ApplyNow();
            Check("no pass, no plan after a new scene", !Part(h).PassActive && h.Rt.Pass == null && Part(h).Rows.Count == 0);
            Check("no slot of the owner is left", h.Rt.Overrides.Count == 0, "" + h.Rt.Overrides.Count);
        }
        finally { h.Dispose(); }
    }
}

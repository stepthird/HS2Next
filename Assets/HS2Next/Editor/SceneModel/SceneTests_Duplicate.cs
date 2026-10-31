// M4: top-most normalisation, new ids for nodes and route points, deep
// copies, character subtrees cloned with equal bytes and the same accessory ids, shared card sources, node inline
// and file ext copied, references (focus, caster, active camera) not copied, placement after the original, and
// opaque nodes in the subtree refused.
using System;
using System.Collections.Generic;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Api.Save;
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_Duplicate
    {
        public static void Run(TestRunner t)
        {
            var s = Kit.GoldenSession(t, withSubtree: true);
            int before = s.Model.Count;
            var r = s.Duplicate(new[] { Kit.GoldenChara, Kit.GoldenItem, Kit.GoldenLight });
            Kit.Ok(t, r, "duplicate a character with selected descendants");
            t.Equal(1, r.NewIds.Count, "top-most normalisation: one copy");
            t.Equal(before + 4, s.Model.Count, "the character's subtree copied once (4 nodes)");
            var copy = s.Model.Find(r.NewId);
            var orig = s.Model.Find(Kit.GoldenChara);
            t.Check(copy != null && copy.Parent == null && copy.Index == orig.Index + 1, "placed right after the original");
            t.Check(s.Selection.Count == 1 && s.Selection.Primary == r.NewId, "the copy is selected");
            var ids = new HashSet<string>();
            foreach (var n in s.Model.Subtree(copy)) ids.Add(n.Id);
            bool fresh = ids.Count == 4;
            foreach (var n in s.Model.Subtree(orig)) fresh &= !ids.Contains(n.Id);
            t.Check(fresh, "every copied node has a new id");
            t.Equal(copy.Children.Count, orig.Children.Count, "children copied");
            t.Check(copy.Children.Count == 2 && copy.Children[0].Point == "N_Hair_pony" && copy.Children[1].Point == "N_Hand_R", "attach points kept");
            t.Check(copy.Doc.Name == orig.Doc.Name && copy.Doc.Transform.Pos.Equals(orig.Doc.Transform.Pos), "names and transforms kept");

            // character subtree: cloned, equal bytes, same accessory ids, ext copied
            var a = orig.Appearance.Stored;
            var b = copy.Appearance.Stored;
            t.Check(a != null && b != null && !ReferenceEquals(a, b), "stored subtree cloned (new object)");
            t.Check(Kit.Same(CanonicalJsonWriter.ToBytes(CharaDocument.EntryName, a.Chara.Write), CanonicalJsonWriter.ToBytes(CharaDocument.EntryName, b.Chara.Write)) &&
                    Kit.Same(CanonicalJsonWriter.ToBytes(CoordDocument.EntryName, a.Coord.Write), CanonicalJsonWriter.ToBytes(CoordDocument.EntryName, b.Coord.Write)),
                "cloned subtree bytes equal");
            t.Equal(string.Join(",", orig.Appearance.WornAccessoryIds()), string.Join(",", copy.Appearance.WornAccessoryIds()), "same accessory ids");
            var ma = ((CharaNodeState)orig.State).State.Accessories;
            var mb = ((CharaNodeState)copy.State).State.Accessories;
            t.Check(ma.Count == 2 && mb.Count == 2 && !ReferenceEquals(ma, mb) && mb[Kit.GoldenItem] == ma[Kit.GoldenItem], "state.accessories copied and still valid");
            t.Check(copy.Doc.Ext.Contains("example.plugin") && !ReferenceEquals(copy.Doc.Ext, orig.Doc.Ext), "node inline ext copied");

            // deep copy: changing the copy leaves the original unchanged
            string origJson = s.NodeJson(Kit.GoldenChara);
            s.Set(copy.Id, "chara.face.eyes", 42);
            s.Set(copy.Id, "chara.fk.bones[\"cf_J_Head\"]", new Vec3(9, 9, 9));
            s.Set(copy.Children[1].Id, "item.colors[0].main", new Color4(0, 1, 0, 1));
            t.Equal(origJson, s.NodeJson(Kit.GoldenChara), "the original's bytes are unchanged by edits of the copy");
            t.Check(s.NodeJson(Kit.GoldenItem) != s.NodeJson(copy.Children[1].Id), "copied item edited independently");

            // references are not copied
            var fx = s.Model.Settings.Effects;
            t.Check(fx.DepthOfField.Focus == Kit.GoldenItem && fx.SunShafts.Caster == Kit.GoldenLight, "focus and caster still name the originals");
            s.SetActiveCamera(Kit.GoldenCamera);
            var rc = s.Duplicate(new[] { Kit.GoldenCamera });
            t.Check(rc.Ok && !((CameraNodeState)s.Model.Find(rc.NewId).State).Active && ((CameraNodeState)s.Model.Find(Kit.GoldenCamera).State).Active, "the copy of an active camera is inactive");
            t.Check(s.Model.Find(rc.NewId).Parent?.Id == Kit.GoldenChara && s.Model.Find(rc.NewId).Point == "N_Hair_pony" &&
                    s.Model.Find(rc.NewId).Index == s.Model.Find(Kit.GoldenCamera).Index + 1, "a child copy stays at its parent's point, after the original");

            // route points get new ids
            var route = s.Model.Find(Kit.GoldenRoute);
            var rr = s.Duplicate(new[] { Kit.GoldenRoute });
            var rcopy = (RouteNodeState)s.Model.Find(rr.NewId).State;
            var rorig = (RouteNodeState)route.State;
            bool newPoints = rcopy.Points.Count == rorig.Points.Count;
            var pointIds = new HashSet<string>();
            for (int i = 0; newPoints && i < rcopy.Points.Count; i++)
                newPoints = rcopy.Points[i].Id != rorig.Points[i].Id && pointIds.Add(rcopy.Points[i].Id) && rcopy.Points[i].Pos.Equals(rorig.Points[i].Pos);
            t.Check(newPoints, "route points copied with new ids");

            // two tops in one group: each copy right after its original
            var s2 = Kit.Session();
            var f = new List<string>();
            for (int i = 0; i < 4; i++) f.Add(s2.AddFolder().NewId);
            var r2 = s2.Duplicate(new[] { f[2], f[0] });
            var order = new List<string>();
            foreach (var n in s2.Model.Roots) order.Add(n.Id);
            t.Check(r2.NewIds.Count == 2 && order.Count == 6 && order[0] == f[0] && order[1] == r2.NewIds[0] && order[2] == f[1] && order[3] == f[2] && order[4] == r2.NewIds[1] && order[5] == f[3],
                "each copy lands right after its original (" + string.Join(",", order) + ")");
            Kit.Ok(t, s2.Commands.Undo(), "undo the duplicate");
            t.Equal(4, s2.Model.Count, "undo removes the copies");
            Kit.Ok(t, s2.Commands.Redo(), "redo the duplicate");
            t.Check(s2.Model.Find(r2.NewIds[0]) != null && s2.Model.Find(r2.NewIds[1]) != null, "redo restores the same ids");

            // card sources are shared
            var card = Kit.Card("dup", "UserData/chara/female/dup.png", 2);
            var s3 = Kit.Session(3);
            var rch = s3.AddChara(new CharaAppearance { Card = card });
            var rd = s3.Duplicate(new[] { rch.NewId });
            t.Check(ReferenceEquals(s3.Model.Find(rd.NewId).Appearance.Card, card), "card source shared by the copy");
            t.Check(!ReferenceEquals(s3.Model.Find(rd.NewId).Appearance, s3.Model.Find(rch.NewId).Appearance), "appearance objects distinct");

            // node file ext copied
            var ext = new ExtScopeData(ExtScope.Node);
            ext.GetOrAdd("example.files").Files["data.bin"] = new byte[] { 1, 2, 3 };
            var add = s3.AddNodes(new[] { new NodeSpec { Doc = NodeDefaults.Folder(null), FileExt = ext } });
            var rf = s3.Duplicate(new[] { add.NewId });
            var fe = s3.Model.Find(rf.NewId).FileExt;
            t.Check(fe != null && !ReferenceEquals(fe, ext) && fe.Get("example.files")?.Files["data.bin"].Length == 3, "node file ext copied");

            // opaque in the subtree
            Kit.Fails(t, s.Duplicate(new[] { Kit.GoldenFolder }), CommandCodes.Unsupported, "a subtree holding an unknown kind");
            Kit.Fails(t, s.Duplicate(new[] { "ffffffffffffffffffffffffffffffff" }), CommandCodes.NotFound, "a missing id");

            // 15.21: a copy keeps the stored (kept) point case
            var s4 = Kit.Session(4);
            s4.Bind(new SceneContainer { Scene = Kit.ReadScene(Kit.Fixture(t, "case_15_21.json")) });
            var d1 = s4.Duplicate(new[] { "c0000000000000000000000000000002" });
            var d2 = s4.Duplicate(new[] { "c0000000000000000000000000000006" });
            t.Check(d1.Ok && s4.Model.Find(d1.NewId).Point == null && d2.Ok && s4.Model.Find(d2.NewId).Point == "N_Future", "copies keep a kept point case (15.21, unknown point)");
            var d3 = s4.Duplicate(new[] { "c0000000000000000000000000000001" });
            t.Check(d3.Ok && s4.Model.Find(d3.NewId).Children.Count == 4, "a character with kept point cases is copied whole");
            Kit.Valid(t, s, "duplicate");
            Kit.Valid(t, s4, "duplicate 15.21");
        }
    }
}

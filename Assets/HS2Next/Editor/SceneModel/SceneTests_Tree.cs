// M1: tree rules — every kind as parent and child (move and
// add), the 54 vanilla attach points, unknown and non-offered points, points under the wrong parent, cycles at
// depth 1/2/5, routes under routes, opaque parents, the two 15.21 cases kept on bind and normalised on move,
// reorder renumbering, and effective visibility over 3-level chains. Validate() after each operation.
using System;
using System.Collections.Generic;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_Tree
    {
        static readonly NodeKind[] Kinds = { NodeKind.Chara, NodeKind.Item, NodeKind.Light, NodeKind.Folder, NodeKind.Route, NodeKind.Camera };

        internal static string Add(SceneSession s, NodeKind k, string parent = null, string point = null)
        {
            CommandResult r;
            switch (k)
            {
                case NodeKind.Chara: r = s.AddChara(null, parent, point); break;
                case NodeKind.Item: r = s.AddItem(new ItemNodeState { Ref = "test.mod#1" }, parent, point); break;
                case NodeKind.Light: r = s.AddLight("test.mod#2", parent, point); break;
                case NodeKind.Folder: r = s.AddFolder(parent, point); break;
                case NodeKind.Route: r = s.AddRoute(parent, point); break;
                default: r = s.AddCamera(parent, point); break;
            }
            return r.Status == CommandStatus.Ok ? r.NewId : null;
        }

        static string PointFor(NodeKind parent) => parent == NodeKind.Chara ? "N_Hand_R" : null;

        static bool Takes(NodeKind k) => k == NodeKind.Chara || k == NodeKind.Item || k == NodeKind.Folder || k == NodeKind.Route;

        public static void Run(TestRunner t)
        {
            KindMatrix(t);
            AttachPoints(t);
            Cycles(t);
            Routes(t);
            Opaque(t);
            Case1521(t);
            OpaqueOrder(t);
            Reorder(t);
            Visibility(t);
        }

        static void KindMatrix(TestRunner t)
        {
            foreach (var p in Kinds)
                foreach (var c in Kinds)
                {
                    // Move (reparent) an existing root under a parent of kind p.
                    var s = Kit.Session();
                    string pid = Add(s, p), cid = Add(s, c);
                    var r = s.Move(cid, pid, PointFor(p), -1);
                    string what = "move " + c + " under " + p;
                    if (!Takes(p)) Kit.Fails(t, r, CommandCodes.WrongKind, what);
                    else if (c == NodeKind.Route) Kit.Fails(t, r, CommandCodes.WrongKind, what + " (routes are not re-parented)");
                    else
                    {
                        Kit.Ok(t, r, what);
                        t.Check(s.Model.Find(cid)?.Parent?.Id == pid && s.Model.Find(cid).Point == PointFor(p), what + ": linked");
                    }
                    Kit.Valid(t, s, what);

                    // Add a new child under a parent of kind p.
                    s = Kit.Session();
                    pid = Add(s, p);
                    var before = s.Model.Count;
                    string nid = Add(s, c, pid, PointFor(p));
                    what = "add " + c + " under " + p;
                    if (!Takes(p)) t.Check(nid == null && s.Model.Count == before, what + " refused");
                    else if (c == NodeKind.Route && p == NodeKind.Route) t.Check(nid == null, what + " refused (route under route)");
                    else t.Check(nid != null && s.Model.Find(nid)?.Parent?.Id == pid, what);
                    Kit.Valid(t, s, what);
                }
            // codes of the add refusals
            var s2 = Kit.Session();
            string light = Add(s2, NodeKind.Light), cam = Add(s2, NodeKind.Camera), route = Add(s2, NodeKind.Route);
            Kit.Fails(t, s2.AddFolder(light), CommandCodes.WrongKind, "add under a light");
            Kit.Fails(t, s2.AddFolder(cam), CommandCodes.WrongKind, "add under a camera");
            Kit.Fails(t, s2.AddRoute(route), CommandCodes.WrongKind, "add a route under a route");
            Kit.Fails(t, s2.AddFolder("ffffffffffffffffffffffffffffffff"), CommandCodes.NotFound, "add under a missing parent");
            Kit.Fails(t, s2.AddFolder(null, "N_Hand_R"), CommandCodes.BadArgs, "a root with a point");
            // the scale channel
            var f = Add(s2, NodeKind.Folder);
            Kit.Fails(t, s2.Set(f, "transform.scale", new Vec3(2, 2, 2)), CommandCodes.BadArgs, "folder scale (no scale channel)");
            Kit.Fails(t, s2.Set(light, "transform.scale", new Vec3(1, 2, 1)), CommandCodes.BadArgs, "light scale (no scale channel)");
            var item = Add(s2, NodeKind.Item);
            Kit.Ok(t, s2.Set(item, "transform.scale", new Vec3(2, 2, 2)), "item scale");
            Kit.Fails(t, s2.Move(f, null, null, 0, new NodeTransform { Scale = new Vec3(3, 3, 3) }), CommandCodes.BadArgs, "move with a scale on a folder");
        }

        static void AttachPoints(TestRunner t)
        {
            var s = Kit.Session();
            string chara = Add(s, NodeKind.Chara);
            int ok = 0;
            var ids = new List<string>();
            foreach (var p in DocEnum.AttachPoints)
            {
                string item = Add(s, NodeKind.Item);
                var r = s.Move(item, chara, p, -1);
                if (r.Status == CommandStatus.Ok) ok++;
                else t.Check(false, "attach at " + p + ": " + r);
                ids.Add(item);
            }
            t.Equal(54, ok, "all 54 vanilla points accept a child");
            Kit.Valid(t, s, "54 points");
            // children grouped by point value
            var kids = s.Model.Find(chara).Children;
            bool ordered = kids.Count == 54;
            for (int i = 0; ordered && i < 54; i++) ordered = kids[i].Point == DocEnum.AttachPoints[i];
            t.Check(ordered, "children grouped in AccessoryParentKey order");
            string extra = Add(s, NodeKind.Item);
            Kit.Fails(t, s.Move(extra, chara, "N_Future", -1), CommandCodes.BadArgs, "unknown point");
            Kit.Fails(t, s.Move(extra, chara, null, -1), CommandCodes.BadArgs, "character child without a point");
            string folder = Add(s, NodeKind.Folder);
            Kit.Fails(t, s.Move(extra, folder, "N_Hand_R", -1), CommandCodes.BadArgs, "point under a folder");

            // a table that offers only the hair group
            var findings = new FindingList();
            var table = AttachPointTable.FromGroups(new[] { new AttachPointGroup(0, "hair", 1, 5), new AttachPointGroup(1, "beyond", 50, 60) }, findings);
            t.Check(findings.Has("attach.unknownIndex"), "a range beyond 54 is reported");
            t.Check(table.IsKnown("N_Hand_R") && !table.IsOffered("N_Hand_R") && table.IsOffered("N_Hair_pony") && table.IsOffered("N_Ana") && !table.IsKnown("N_Future"),
                "known vs offered");
            t.Equal(48, table.ValueOf("N_Hand_R"), "point value");
            var s2 = Kit.Session(2, new SessionOptions { AttachPoints = table });
            string c2 = Add(s2, NodeKind.Chara), i2 = Add(s2, NodeKind.Item);
            Kit.Fails(t, s2.Move(i2, c2, "N_Hand_R", -1), CommandCodes.BadArgs, "known but not offered point");
            Kit.Ok(t, s2.Move(i2, c2, "N_Hair_twin_L", -1), "offered point");
            Kit.Valid(t, s2, "custom attach table");
        }

        static void Cycles(TestRunner t)
        {
            var s = Kit.Session();
            var chain = new List<string> { Add(s, NodeKind.Folder) };
            for (int i = 1; i <= 5; i++) chain.Add(Add(s, NodeKind.Folder, chain[i - 1]));
            Kit.Fails(t, s.Move(chain[0], chain[0], null, -1), CommandCodes.BadArgs, "move under itself");
            Kit.Fails(t, s.Move(chain[0], chain[1], null, -1), CommandCodes.BadArgs, "cycle at depth 1");
            Kit.Fails(t, s.Move(chain[0], chain[2], null, -1), CommandCodes.BadArgs, "cycle at depth 2");
            Kit.Fails(t, s.Move(chain[0], chain[5], null, -1), CommandCodes.BadArgs, "cycle at depth 5");
            Kit.Ok(t, s.Move(chain[5], chain[0], null, -1), "move a descendant up");
            Kit.Valid(t, s, "cycles");
        }

        static void Routes(TestRunner t)
        {
            var s = Kit.Session();
            string r1 = Add(s, NodeKind.Route);
            string inner = Add(s, NodeKind.Folder, r1);
            var g = new NodeSpec { Doc = NodeDefaults.Folder(null), Children = new[] { new NodeSpec { Doc = NodeDefaults.Route(null, s.NewId()) } } };
            var add = s.AddNodes(new[] { g });
            Kit.Ok(t, add, "add a folder holding a route");
            string gid = add.NewId;
            Kit.Fails(t, s.Move(gid, r1, null, -1), CommandCodes.WrongKind, "subtree with a route under a route");
            Kit.Fails(t, s.Move(gid, inner, null, -1), CommandCodes.WrongKind, "subtree with a route inside a route's subtree");
            string plain = Add(s, NodeKind.Folder);
            Kit.Ok(t, s.Move(plain, inner, null, -1), "a subtree without a route inside a route");
            Kit.Fails(t, s.AddNodes(new[] { new NodeSpec { Doc = NodeDefaults.Route(null, s.NewId()), ParentId = inner } }), CommandCodes.WrongKind, "add a route inside a route's subtree");
            var nested = new NodeSpec { Doc = NodeDefaults.Route(null, s.NewId()), Children = new[] { new NodeSpec { Doc = NodeDefaults.Route(null, s.NewId()) } } };
            Kit.Fails(t, s.AddNodes(new[] { nested }), CommandCodes.WrongKind, "add a route holding a route");
            Kit.Valid(t, s, "routes");

            var o = new SessionOptions { Rules = new TreeRules { AllowRouteReparent = true } };
            var s2 = Kit.Session(3, o);
            string a = Add(s2, NodeKind.Route), b = Add(s2, NodeKind.Route), f = Add(s2, NodeKind.Folder);
            Kit.Fails(t, s2.Move(b, a, null, -1), CommandCodes.WrongKind, "switch on: route under route");
            Kit.Ok(t, s2.Move(b, f, null, -1), "switch on: route under a folder");
            Kit.Ok(t, s.Reorder(r1, s.Model.Roots.Count - 1), "routes reorder");
            Kit.Valid(t, s2, "route switch");
        }

        static void Opaque(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            var op = s.Model.Find(Kit.GoldenOpaque);
            t.Check(op != null && op.Kind == NodeKind.Opaque && op.Children.Count == 1, "golden: opaque node with one child kept");
            string f = Add(s, NodeKind.Folder);
            Kit.Fails(t, s.Move(f, Kit.GoldenOpaque, null, -1), CommandCodes.WrongKind, "opaque parent refuses a new child");
            Kit.Fails(t, s.AddFolder(Kit.GoldenOpaque), CommandCodes.WrongKind, "opaque parent refuses an added child");
            Kit.Fails(t, s.Move(Kit.GoldenOpaque, null, null, -1), CommandCodes.Unsupported, "opaque node is not re-parented");
            Kit.Fails(t, s.Set(Kit.GoldenOpaque, "name", "x"), CommandCodes.Unsupported, "opaque node fields are not settable");
            Kit.Fails(t, s.Duplicate(new[] { Kit.GoldenOpaque }), CommandCodes.Unsupported, "opaque node is not duplicated");
            Kit.Ok(t, s.Move(Kit.GoldenOpaqueChild, null, null, -1), "a child moves out of an opaque node");
            Kit.Ok(t, s.Reorder(Kit.GoldenOpaque, 0), "opaque node reorders");
            Kit.Valid(t, s, "opaque");
            t.Check(s.Commands.Undo().Ok && s.Commands.Undo().Ok && s.Commands.Undo().Ok, "undo the opaque edits");
            t.Check(Kit.Same(Kit.Golden(t, "scene.json"), Kit.Bytes(s)), "opaque edits undone: golden bytes " + Kit.Diff(Kit.Golden(t, "scene.json"), Kit.Bytes(s)));
            var del = s.Delete(new[] { Kit.GoldenOpaque });
            Kit.Ok(t, del, "an opaque node is deleted with its subtree");
            t.Check(s.Model.Find(Kit.GoldenOpaqueChild) == null, "its child went with it");
            Kit.Valid(t, s, "opaque delete");
        }

        static void Case1521(TestRunner t)
        {
            // a character child without a point, one at an unknown point, and a point under a folder (kept by the reader,
            // 3): fixture Tests/SceneModel/case_15_21.json
            var doc = Kit.ReadScene(Kit.Fixture(t, "case_15_21.json"));
            var canonical = DocIo.Bytes(doc);
            var s = Kit.Session();
            s.Bind(new SceneContainer { Scene = Kit.ReadScene(canonical) });
            var x = s.Model.Find("c0000000000000000000000000000002");
            var y = s.Model.Find("c0000000000000000000000000000004");
            t.Check(x != null && x.Parent?.Kind == NodeKind.Chara && x.Point == null, "15.21: character child without a point kept on bind");
            t.Check(y != null && y.Parent?.Kind == NodeKind.Folder && y.Point == "N_Hand_R", "15.21: point under a folder kept on bind");
            t.Check(Kit.Same(canonical, Kit.Bytes(s)), "15.21 cases written back unchanged");
            Kit.Valid(t, s, "15.21 bind");
            Kit.Ok(t, s.Reorder("c0000000000000000000000000000005", 0), "15.21: reorder inside the kept group");
            Kit.Ok(t, s.Move(x.Id, x.Parent.Id, "N_Hand_L", -1), "15.21: chara child moved to a point (normalised)");
            t.Check(x.Point == "N_Hand_L", "normalised point");
            Kit.Ok(t, s.Move(y.Id, y.Parent.Id, null, -1), "15.21: folder child moved to no point (normalised)");
            t.Check(y.Point == null && y.Doc.Parent.Point == null, "normalised: no point");
            Kit.Valid(t, s, "15.21 moves");
            var u = s.Model.Find("c0000000000000000000000000000006");
            t.Check(u != null && u.Point == "N_Future" && !s.Model.AttachPoints.IsKnown("N_Future"), "an unknown point name is kept");
            t.Check(s.Reorder(u.Id, 0).Ok, "a node at an unknown point may reorder (no error)");
            Kit.Fails(t, s.AddFolder(u.Parent.Id, "N_Future"), CommandCodes.BadArgs, "no new attachment at an unknown point");
        }

        static void OpaqueOrder(TestRunner t)
        {
            // an opaque node whose raw object has no `order` gets one, after `kind`, when renumbering changes it
            var s = Kit.Session(9);
            s.Bind(new SceneContainer { Scene = Kit.ReadScene(Kit.Fixture(t, "opaque_without_order.json")) });
            string op = "b0000000000000000000000000000002", sib = "b0000000000000000000000000000003";
            t.Check(!s.NodeJson(op).Contains("\"order\""), "opaque node read without `order`");
            Kit.Ok(t, s.Reorder(sib, 0), "reorder its sibling");
            var j = Newtonsoft.Json.Linq.JObject.Parse(s.NodeJson(op));
            var names = new List<string>();
            foreach (var p in j.Properties()) names.Add(p.Name);
            t.Check(names.IndexOf("order") == names.IndexOf("kind") + 1 && (int)j["order"] == 1, "`order` inserted after `kind` (" + string.Join(",", names) + ")");
            Kit.Valid(t, s, "opaque order");
        }

        static void Reorder(TestRunner t)
        {
            var s = Kit.Session();
            var ids = new List<string>();
            for (int i = 0; i < 5; i++) ids.Add(Add(s, NodeKind.Folder));
            Kit.Ok(t, s.Reorder(ids[0], 3), "reorder 0 → 3");
            var expect = new[] { ids[1], ids[2], ids[3], ids[0], ids[4] };
            bool same = true;
            for (int i = 0; i < 5; i++) same &= s.Model.Roots[i].Id == expect[i] && s.Model.Roots[i].Doc.Order == i && s.Model.Roots[i].Index == i;
            t.Check(same, "renumbered 0..4 in the new order");
            Kit.NoOp(t, s.Reorder(ids[0], 3), "reorder to the same index is NoOp");
            Kit.Fails(t, s.Reorder(ids[0], 5), CommandCodes.BadArgs, "reorder index out of range");
            Kit.Ok(t, s.Reorder(ids[4], 0), "reorder last → first");
            Kit.Ok(t, s.Commands.Undo(), "undo reorder");
            Kit.Ok(t, s.Commands.Undo(), "undo reorder");
            same = true;
            for (int i = 0; i < 5; i++) same &= s.Model.Roots[i].Id == ids[i] && s.Model.Roots[i].Doc.Order == i;
            t.Check(same, "undo restores the order");
            Kit.Valid(t, s, "reorder");
        }

        static void Visibility(TestRunner t)
        {
            for (int mask = 0; mask < 8; mask++)
            {
                var s = Kit.Session();
                string a = Add(s, NodeKind.Folder), b = Add(s, NodeKind.Folder, a), c = Add(s, NodeKind.Item, b);
                bool va = (mask & 1) != 0, vb = (mask & 2) != 0, vc = (mask & 4) != 0;
                s.Set(a, "visible", va);
                s.Set(b, "visible", vb);
                s.Set(c, "visible", vc);
                var na = s.Model.Find(a); var nb = s.Model.Find(b); var nc = s.Model.Find(c);
                t.Check(na.EffectiveVisible == va && nb.EffectiveVisible == (va && vb) && nc.EffectiveVisible == (va && vb && vc),
                    "effective visibility of a 3-level chain, flags " + va + "/" + vb + "/" + vc);
                Kit.Valid(t, s, "visibility " + mask);
            }
            // a move changes the effective visibility of the moved subtree
            var s2 = Kit.Session();
            string hidden = Add(s2, NodeKind.Folder), shown = Add(s2, NodeKind.Folder), item = Add(s2, NodeKind.Item, shown);
            s2.Set(hidden, "visible", false);
            t.Check(s2.Model.Find(item).EffectiveVisible, "visible under a visible folder");
            s2.Move(item, hidden, null, -1);
            t.Check(!s2.Model.Find(item).EffectiveVisible && s2.Model.Find(item).Doc.Visible, "hidden under a hidden folder, own flag kept");
            s2.Commands.Undo();
            t.Check(s2.Model.Find(item).EffectiveVisible, "undo of the move restores visibility");
            Kit.Valid(t, s2, "visibility moves");
        }
    }
}

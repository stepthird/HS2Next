// The runtime override layer's Unity-free core (TO1, TO2): winners by priority,
// then by acquisition; one slot per owner and channel; Clear, Release, an owner's release, the scene clear and a forgotten
// node; bit-equal values change nothing; released slots keep their read members; the thread rule; the model-change hook
// and the driving rule (TO1). Every path by node kind with the refusals, the value forms of every kind and their
// refusals (TO2). A recording host stands in for the scene runtime.
using System;
using System.Collections.Generic;
using System.Threading;
using HS2Next.Api.Plugins;
using HS2Next.Save;
using HS2Next.Scenes.Overrides;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save.Tests;

    public static class SceneTests_Overrides
    {
        public static void Run(TestRunner t)
        {
            Winners(t);
            OneSlotPerOwner(t);
            ClearAndRelease(t);
            BitEqual(t);
            SessionEvents(t);
            ModelHook(t);
            Driving(t);
            ThreadRule(t);
            Listing(t);
            PathsByKind(t);
            Refusals(t);
            Forms(t);
            JsonForms(t);
        }

        // ------------------------------------------------------------ a recording host

        sealed class Host : IOverrideHost
        {
            public readonly Dictionary<string, NodeKind> Kinds = new Dictionary<string, NodeKind>(StringComparer.Ordinal);
            public readonly HashSet<string> Built = new HashSet<string>(StringComparer.Ordinal);
            public readonly List<string> Order = new List<string>();
            public readonly List<string> Applied = new List<string>();
            public bool Effective = true;
            public SceneModel Model;

            public string Add(string id, NodeKind k) { Kinds[id] = k; Built.Add(id); Order.Add(id); return id; }

            public bool TryKindOf(string nodeId, out NodeKind kind)
            {
                var n = Model?.Find(nodeId);
                if (n != null) { kind = n.Kind; return true; }
                return Kinds.TryGetValue(nodeId, out kind);
            }

            public bool TargetExists(string nodeId) => Model != null ? Model.Find(nodeId) != null : Built.Contains(nodeId);
            public void Apply(OverrideChannelState c) => Applied.Add((c.NodeId ?? "scene") + " " + c.Path.Text + " -> " + (c.Winner == null ? "model" : c.Winner.Owner + " " + c.Winner.ValueJson()));
            public bool IsEffective(OverrideSlot winner) => Effective;
            public IEnumerable<string> NodeOrder()
            {
                if (Model == null) return Order;
                var l = new List<string>();
                foreach (var n in Model.PreOrder()) l.Add(n.Id);
                return l;
            }
        }

        const string A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", C = "cccccccccccccccccccccccccccccccc";

        static (OverrideLayer, Host) New()
        {
            var h = new Host();
            h.Add(A, NodeKind.Item);
            h.Add(B, NodeKind.Chara);
            h.Add(C, NodeKind.Light);
            return (new OverrideLayer(h), h);
        }

        // ------------------------------------------------------------ TO1

        static void Winners(TestRunner t)
        {
            var (l, h) = New();
            var tl = l.Acquire("timeline", A, "transform.pos");
            var p0 = l.Acquire("plugin.zero", A, "transform.pos");
            var pm = l.Acquire("plugin.minus", A, "transform.pos", -1);
            var pp = l.Acquire("plugin.plus", A, "transform.pos", 1);
            t.Check(tl != null && p0 != null && pm != null && pp != null && l.Count == 4, "TO1 four owners acquire one channel");
            t.Check(!tl.HasValue && !tl.IsWinning && l.WinnerOf(A, OverrideChannel.TransformPos) == null, "TO1 slots start without a value; no winner");
            pp.Set(9, 9, 9);
            tl.Set(1, 2, 3);
            pm.Set(-1, -1, -1);
            t.Check(pp.IsWinning && !tl.IsWinning && !pm.IsWinning, "TO1 priority 1 wins over 0 and -1, whatever the order");
            pp.Clear();
            t.Check(tl.IsWinning && !pm.IsWinning, "TO1 priority 0 wins over -1 (-1 yields to the Timeline)");
            p0.Set(4, 5, 6);
            t.Check(p0.IsWinning && !tl.IsWinning, "TO1 equal priority 0: the later acquired wins");
            var w = l.WinnerOf(A, OverrideChannel.TransformPos);
            t.Check(w == p0 && w[0] == 4 && w[1] == 5 && w[2] == 6, "TO1 WinnerOf gives the winning slot and its value");
            p0.Release();
            t.Check(tl.IsWinning, "TO1 the winner released: the next slot wins");
            t.Equal(A + " transform.pos -> timeline [1,2,3]", h.Applied[h.Applied.Count - 1], "TO1 the host applies the new winner");
            tl.Release(); pm.Release(); pp.Release();
            t.Check(l.Count == 0 && l.WinnerOf(A, OverrideChannel.TransformPos) == null && !l.HasNode(A), "TO1 everything released: no slot, no winner");
            t.Check(h.Applied[h.Applied.Count - 1].EndsWith("-> model"), "TO1 the last release applies the model value");
        }

        static void OneSlotPerOwner(TestRunner t)
        {
            var (l, _) = New();
            var a = l.Acquire("timeline", A, "transform.rot");
            var b = l.Acquire("timeline", A, "transform.rot", 5);
            t.Check(ReferenceEquals(a, b) && a.Priority == 5 && l.Count == 1, "TO1 an owner's second Acquire of a channel returns its slot with the new priority");
            var c = l.Acquire("timeline", A, "transform.pos");
            t.Check(!ReferenceEquals(a, c) && l.Count == 2, "TO1 another path of the node is another channel");
            var d = l.Acquire("timeline", B, "chara.fk.bones[\"cf_J_Neck\"]");
            var e = l.Acquire("timeline", B, "chara.fk.bones[\"cf_J_Head\"]");
            t.Check(d != e && d.Path == "chara.fk.bones[\"cf_J_Neck\"]" && l.Count == 4, "TO1 two FK bones of a character are two channels");
            int v = l.Version;
            l.Acquire("timeline", A, "transform.rot", 5);
            t.Check(l.Version == v, "TO1 re-acquiring with the same priority changes nothing");
        }

        static void ClearAndRelease(TestRunner t)
        {
            var (l, h) = New();
            var s = l.Acquire("plugin.x", A, "item.colors[1].main");
            t.Check(s.Set(0.5, 0.25, 0.125) && s.HasValue && s[3] == 1, "TO1 a colour from 3 numbers has alpha 1");
            int applied = h.Applied.Count;
            t.Check(s.Clear() && !s.HasValue && !s.IsWinning && !s.IsReleased && l.Count == 1, "TO1 Clear removes the value, the slot stays acquired");
            t.Check(h.Applied.Count == applied + 1 && h.Applied[applied].EndsWith("-> model"), "TO1 Clear applies the model value");
            t.Check(s.Clear(), "TO1 Clear without a value: true");
            s.Set(1, 1, 1, 1);
            s.Release();
            t.Check(s.IsReleased && !s.HasValue && !s.IsWinning && l.Count == 0, "TO1 Release: released, no value, not winning");
            t.Check(!s.Set(1, 2, 3) && !s.Set(1, 2, 3, 4) && !s.Set(new double[] { 1, 2, 3 }) && !s.SetJson("[1,2,3]") && !s.Clear(), "TO1 every setter and Clear of a released slot answer false");
            t.Check(s.NodeId == A && s.Path == "item.colors[1].main" && s.Kind == OverrideValueKind.Color && s.Priority == 0, "TO1 a released slot keeps its read members");
            s.Release();
            s.Dispose();
            t.Check(s.IsReleased && l.Count == 0, "TO1 Release and Dispose of a released slot do nothing");
            // an owner's release
            var a1 = l.Acquire("plugin.y", A, "transform.pos"); a1.Set(1, 1, 1);
            var a2 = l.Acquire("plugin.y", B, "chara.anime.speed"); a2.Set(2);
            var a3 = l.Acquire("timeline", B, "chara.anime.speed"); a3.Set(3);
            t.Check(a3.IsWinning && !a2.IsWinning, "TO1 equal priority: the Timeline's later slot wins over the plugin's");
            t.Equal(2, l.ReleaseOwner("plugin.y"), "TO1 ReleaseOwner counts the owner's slots");
            t.Check(a1.IsReleased && a2.IsReleased && !a3.IsReleased && a3.IsWinning && l.Count == 1, "TO1 the owner's slots go, the others stay and win");
            t.Equal(0, l.ReleaseOwner("plugin.y"), "TO1 a second ReleaseOwner releases nothing");
            // a node's release (forgotten) and the scene's
            var b1 = l.Acquire("plugin.z", A, "transform.scale"); b1.Set(2, 2, 2);
            int n = h.Applied.Count;
            t.Equal(1, l.ReleaseNode(A), "TO1 ReleaseNode releases the node's slots");
            t.Check(b1.IsReleased && !a3.IsReleased && h.Applied.Count == n, "TO1 the other nodes keep theirs; nothing is applied for a forgotten node");
            var c1 = l.Acquire("plugin.z", null, "camera"); c1.Set(new double[] { 0, 1, 0, 10, 180, 0, 5, 30 });
            n = h.Applied.Count;
            t.Equal(2, l.ReleaseEverything(), "TO1 ReleaseEverything releases every slot of every owner");
            t.Check(l.Count == 0 && a3.IsReleased && c1.IsReleased && h.Applied.Count == n, "TO1 the scene clear: no slot left, nothing applied");
        }

        static void BitEqual(TestRunner t)
        {
            var (l, h) = New();
            var s = l.Acquire("timeline", A, "transform.pos");
            s.Set(1.5, -2, 3);
            int v = l.Version, vv = l.ValueVersion, n = h.Applied.Count;
            t.Check(s.Set(1.5, -2, 3) && s.Set(new[] { 1.5, -2.0, 3 }) && s.SetJson("[1.5, -2, 3]"), "TO1 a bit-equal value: true");
            t.Check(l.Version == v && l.ValueVersion == vv && h.Applied.Count == n, "TO1 a bit-equal value changes nothing (versions, applications)");
            s.Set(1.5, -2, 3.0000000001);
            t.Check(l.ValueVersion == vv + 1 && l.Version == v && h.Applied.Count == n + 1, "TO1 a new value: ValueVersion moves, Version (the mark) does not, the host applies");
            var r = l.Acquire("timeline", A, "transform.rot");
            r.Set(10, 20, 30);
            vv = l.ValueVersion;
            t.Check(r.Set(10, 20, 30) && l.ValueVersion == vv, "TO1 a bit-equal Euler rotation changes nothing");
            var q = l.Acquire("timeline", B, "chara.face.eyes");
            q.Set(2.4);
            vv = l.ValueVersion;
            t.Check(q.Set(1.6) && l.ValueVersion == vv && q[0] == 2, "TO1 an Integer rounded to the same whole number changes nothing");
            var lower = l.Acquire("plugin.low", A, "transform.pos", -5);
            n = h.Applied.Count;
            lower.Set(7, 7, 7);
            t.Check(h.Applied.Count == n && !lower.IsWinning, "TO1 a value of a slot that does not win is not applied");
        }

        static void SessionEvents(TestRunner t)
        {
            var s = Kit.Session(31);
            var h = new Host { Model = s.Model };
            var l = new OverrideLayer(h);
            l.Attach(s.Events);
            string item = s.AddItem(new ItemNodeState { Ref = "test.mod#1" }).NewId;
            string folder = s.AddFolder().NewId;
            var a = l.Acquire("timeline", item, "transform.pos");
            var b = l.Acquire("plugin.x", folder, "transform.rot");
            a.Set(1, 2, 3); b.Set(0, 90, 0);
            t.Check(a.TargetExists && b.TargetExists, "TO1 TargetExists for nodes in the model");
            Kit.Ok(t, s.Delete(new[] { item }), "delete the item");
            t.Check(!a.IsReleased && a.HasValue && !a.TargetExists, "TO1 a deleted node keeps its slot (TargetExists false)");
            h.Kinds[item] = NodeKind.Item;   // the runtime finds a deleted node's kind in its graveyard
            t.Check(l.Acquire("plugin.y", item, "transform.pos") != null, "TO1 a deleted node can be acquired (an undo can bring it back)");
            Kit.Ok(t, s.Commands.Undo(), "undo the delete");
            t.Check(a.TargetExists && a.IsWinning && l.WinnerOf(item, OverrideChannel.TransformPos) != null, "TO1 undo: the target exists again, the slots are still there");
            t.Check(l.Acquire("plugin.y", "ffffffffffffffffffffffffffffffff", "transform.pos") == null, "TO1 Acquire of an id that names no node: null");
            Kit.Ok(t, s.Delete(new[] { item }), "delete the item again");
            s.Commands.ClearHistory();
            t.Check(a.IsReleased && !b.IsReleased && !l.HasNode(item), "TO1 NodeForgotten releases the node's slots only");
            s.NewScene();
            t.Check(b.IsReleased && l.Count == 0, "TO1 a new scene (Cleared) releases every slot");
            l.Detach();
        }

        static void ModelHook(TestRunner t)
        {
            var s = Kit.Session(32);
            var h = new Host { Model = s.Model };
            var l = new OverrideLayer(h);
            l.Attach(s.Events);
            s.Events.Changed += l.NotifyModelChanged;
            string item = s.AddItem(new ItemNodeState { Ref = "test.mod#1" }).NewId;
            var pos = l.Acquire("timeline", item, "transform.pos");
            var col = l.Acquire("timeline", item, "item.colors[0].main");
            var cam = l.Acquire("timeline", null, "camera");
            var dof = l.Acquire("plugin.x", null, "effects.depthOfField");
            var hits = new List<OverrideSlot>();
            l.ModelChanged += x => hits.Add(x);
            Kit.Ok(t, s.Set(item, "transform.pos", new Vec3(1, 2, 3)), "move the item");
            t.Check(hits.Count == 1 && hits[0] == pos, "TO1 ModelChanged: a model edit of transform.pos names its slot");
            hits.Clear();
            Kit.Ok(t, s.Set(item, "transform.rot", new Vec3(0, 10, 0)), "turn the item");
            t.Check(hits.Count == 0, "TO1 ModelChanged: another channel of the node names nothing");
            s.WriteSilently(ChangeOrigin.Capture, w => w.Set(item, "transform.pos", new Vec3(4, 5, 6)));
            t.Check(hits.Count == 0, "TO1 ModelChanged: a save capture names nothing");
            Kit.Ok(t, s.Commands.Undo(), "undo the turn");
            Kit.Ok(t, s.Commands.Undo(), "undo the move");
            t.Check(hits.Count == 1 && hits[0] == pos, "TO1 ModelChanged: an undo of the channel's edit names its slot");
            hits.Clear();
            Kit.Ok(t, s.Set(null, "camera.current", new CameraView { Pivot = new Vec3(0, 1, 0), Rot = new Vec3(10, 20, 0), Distance = 5, Fov = 30 }), "move the view");
            t.Check(hits.Count == 1 && hits[0] == cam, "TO1 ModelChanged: scene.camera.current names the camera slot");
            hits.Clear();
            Kit.Ok(t, s.Set(null, "effects.depthOfField.aperture", 0.5f), "change the depth of field");
            t.Check(hits.Count == 1 && hits[0] == dof, "TO1 ModelChanged: effects.depthOfField names its slot");
            t.Check(col != null, "TO1 the colour slot was acquired");
            s.Events.Changed -= l.NotifyModelChanged;
            l.Detach();
        }

        static void Driving(TestRunner t)
        {
            var (l, _) = New();
            var tl = l.Acquire(OverrideLayer.TimelineOwner, null, "camera");
            tl.Set(new double[] { 0, 0, 0, 0, 0, 0, 10, 23 });
            t.Equal("timeline", l.CameraDrivenBy(), "TO1 the winning owner drives by default");
            bool playing = false;
            l.IsDriving = o => o != OverrideLayer.TimelineOwner || playing;
            t.Check(l.CameraDrivenBy() == null && l.DrivenBy(null, "camera") == null, "TO1 a paused Timeline does not drive the camera");
            playing = true;
            t.Equal("timeline", l.CameraDrivenBy(), "TO1 a playing Timeline drives the camera");
            playing = false;
            var p = l.Acquire("plugin.x", null, "camera");
            p.Set(new double[] { 1, 1, 1, 0, 0, 0, 5, 30 });
            t.Equal("plugin.x", l.CameraDrivenBy(), "TO1 a plugin's winning slot drives the camera");
            t.Equal("Timeline", l.OwnerName("timeline"), "TO1 the Timeline's display name");
            l.SetOwnerName("plugin.x", "Probe");
            t.Equal("Probe", l.OwnerName("plugin.x"), "TO1 an owner's display name");
        }

        static void ThreadRule(TestRunner t)
        {
            var (l, _) = New();
            var s = l.Acquire("timeline", A, "transform.pos");
            s.Set(1, 2, 3);
            Exception setErr = null, acqErr = null, relErr = null;
            bool readOk = false;
            var th = new Thread(() =>
            {
                try { s.Set(4, 5, 6); } catch (Exception e) { setErr = e; }
                try { l.Acquire("x", A, "transform.rot"); } catch (Exception e) { acqErr = e; }
                try { s.Release(); } catch (Exception e) { relErr = e; }
                readOk = s.HasValue && s.IsWinning && s.NodeId == A && s.Path == "transform.pos" && !s.IsReleased;
            });
            th.Start();
            th.Join();
            t.Check(setErr is InvalidOperationException && acqErr is InvalidOperationException && relErr is InvalidOperationException, "TO1 setters, Acquire and Release off the main thread: InvalidOperationException");
            t.Check(readOk && s[0] == 1, "TO1 read members answer on another thread; the value is unchanged");
        }

        static void Listing(TestRunner t)
        {
            var (l, _) = New();
            var c1 = l.Acquire("timeline", C, "light.color"); c1.Set(1, 0, 0);
            var b1 = l.Acquire("plugin.x", B, "chara.fk.bones[\"cf_J_Neck\"]", 2); b1.Set(0, 0, 10);
            var b0 = l.Acquire("timeline", B, "chara.fk.bones[\"cf_J_Neck\"]"); b0.Set(0, 0, 20);
            var b2 = l.Acquire("timeline", B, "chara.anime.speed");
            var cam = l.Acquire("plugin.x", null, "camera"); cam.Set(new double[] { 0, 0, 0, 0, 0, 0, 1, 20 });
            var all = l.Slots();
            t.Check(all.Count == 5 && all[0] == cam && all[1] == b2 && all[2] == b0 && all[3] == b1 && all[4] == c1,
                "TO1 Slots: scene channels first, then nodes in pre-order, by path, then priority (" + string.Join(" | ", all) + ")");
            var info = l.List();
            t.Check(info.Count == 4 && info[1].NodeId == B && info[1].Owner == "timeline" && !info[1].Winning && info[2].Winning && info[2].Priority == 2,
                "TO1 List: slots with a value only, winning marked");
            t.Equal(2, l.List(B).Count, "TO1 List of one node");
            t.Equal(2, l.Slots(null, "plugin.x").Count, "TO1 Slots of one owner");
        }

        // ------------------------------------------------------------ TO2

        static void PathsByKind(TestRunner t)
        {
            var h = new Host();
            var ids = new Dictionary<NodeKind, string>();
            int k = 0;
            foreach (NodeKind kind in new[] { NodeKind.Chara, NodeKind.Item, NodeKind.Light, NodeKind.Folder, NodeKind.Route, NodeKind.Camera })
                ids[kind] = h.Add(new string((char)('a' + k++), 32), kind);
            var l = new OverrideLayer(h);
            var table = new (string path, OverrideValueKind kind, NodeKind[] kinds)[]
            {
                ("transform.pos", OverrideValueKind.Vector3, new[] { NodeKind.Chara, NodeKind.Item, NodeKind.Light, NodeKind.Folder, NodeKind.Route, NodeKind.Camera }),
                ("transform.rot", OverrideValueKind.Rotation, new[] { NodeKind.Chara, NodeKind.Item, NodeKind.Light, NodeKind.Folder, NodeKind.Route, NodeKind.Camera }),
                ("transform.scale", OverrideValueKind.Vector3, new[] { NodeKind.Chara, NodeKind.Item }),
                ("chara.fk.bones[\"cf_J_ArmUp00_R\"]", OverrideValueKind.Rotation, new[] { NodeKind.Chara }),
                ("chara.anime.ref", OverrideValueKind.Animation, new[] { NodeKind.Chara }),
                ("chara.anime.speed", OverrideValueKind.Number, new[] { NodeKind.Chara }),
                ("chara.anime.time", OverrideValueKind.Number, new[] { NodeKind.Chara }),
                ("chara.face.eyes", OverrideValueKind.Integer, new[] { NodeKind.Chara }),
                ("chara.face.mouth", OverrideValueKind.Integer, new[] { NodeKind.Chara }),
                ("chara.face.eyesOpen", OverrideValueKind.Number, new[] { NodeKind.Chara }),
                ("chara.face.mouthOpen", OverrideValueKind.Number, new[] { NodeKind.Chara }),
                ("item.colors[0].main", OverrideValueKind.Color, new[] { NodeKind.Item }),
                ("item.colors[3].main", OverrideValueKind.Color, new[] { NodeKind.Item }),
                ("light.color", OverrideValueKind.Color, new[] { NodeKind.Light }),
            };
            int ok = 0, refused = 0;
            var bad = new List<string>();
            foreach (var row in table)
                foreach (var kv in ids)
                {
                    bool expect = Array.IndexOf(row.kinds, kv.Key) >= 0;
                    try
                    {
                        var s = l.Acquire("timeline", kv.Value, row.path);
                        if (!expect || s == null || s.Kind != row.kind || s.Path != row.path) bad.Add(row.path + " on " + kv.Key + " acquired");
                        else ok++;
                    }
                    catch (ArgumentException e)
                    {
                        if (expect || e.Message.IndexOf(row.path, StringComparison.Ordinal) < 0) bad.Add(row.path + " on " + kv.Key + ": " + e.Message);
                        else refused++;
                    }
                }
            t.Check(bad.Count == 0, "TO2 every node path by node kind (" + ok + " acquired, " + refused + " refused)" + (bad.Count > 0 ? ": " + string.Join("; ", bad) : ""));
            t.Equal(25, ok, "TO2 the accepted (path, kind) pairs");
            var cam = l.Acquire("timeline", null, "camera");
            var dof = l.Acquire("timeline", null, "effects.depthOfField");
            t.Check(cam.Kind == OverrideValueKind.Camera && cam.NodeId == null && dof.Kind == OverrideValueKind.DepthOfField && dof.TargetExists, "TO2 the scene channels: camera, effects.depthOfField (TargetExists while the scene exists)");
            t.Equal("chara.fk.bones[\"cf_J_Neck\"]", l.Acquire("x", ids[NodeKind.Chara], "chara.fk.bones[\"cf_J_Neck\"]").Path, "TO2 an FK bone path in its canonical form");
            t.Check(OverridePath.Parse("item.colors[2].main").Index == 2 && OverridePath.Parse("chara.fk.bones[\"cf_J_Neck\"]").Bone == "cf_J_Neck", "TO2 the colour index and the bone name are parsed");
            t.Check(OverridePath.Parse("camera").ModelPath == "camera.current" && OverridePath.Parse("transform.pos").TouchedBy("transform") &&
                    !OverridePath.Parse("transform.pos").TouchedBy("transform.rot") && OverridePath.Parse("item.colors[1].main").TouchedBy("item.colors[1]") &&
                    !OverridePath.Parse("item.colors[1].main").TouchedBy("item.colors[1].metallic") && OverridePath.Parse("chara.fk.bones[\"b\"]").TouchedBy("chara.fk"),
                "TO2 channel matching by model path (prefix at a step boundary)");
        }

        static void Refusals(TestRunner t)
        {
            var (l, _) = New();
            string Msg(Action a) { try { a(); return null; } catch (ArgumentException e) { return e.Message; } catch (Exception e) { return "other " + e.GetType().Name; } }
            string m;
            m = Msg(() => l.Acquire("x", A, "transform.position"));
            t.Check(m != null && m.Contains("transform.position"), "TO2 an unknown path is refused, naming it: " + m);
            m = Msg(() => l.Acquire("x", A, "chara.fk.bones"));
            t.Check(m != null && m.Contains("chara.fk.bones"), "TO2 chara.fk.bones without a bone is refused");
            m = Msg(() => l.Acquire("x", A, "item.colors[4].main"));
            t.Check(m != null && m.Contains("0 to 3"), "TO2 item.colors[4] is refused");
            m = Msg(() => l.Acquire("x", A, "transform..pos"));
            t.Check(m != null && m.Contains("not a field path"), "TO2 a malformed path is refused");
            m = Msg(() => l.Acquire("x", A, "chara.anime.speed"));
            t.Check(m != null && m.Contains("chara.anime.speed") && m.Contains("item"), "TO2 a chara path on an item is refused, naming the path and the kind");
            m = Msg(() => l.Acquire("x", null, "transform.pos"));
            t.Check(m != null && m.Contains("needs a node id"), "TO2 a node path with a null node id is refused");
            m = Msg(() => l.Acquire("x", A, "camera"));
            t.Check(m != null && m.Contains("scene-wide"), "TO2 a scene path with a node id is refused");
            m = Msg(() => l.Acquire("x", A, null));
            t.Check(m != null, "TO2 a null path is refused");
            m = Msg(() => l.Acquire(null, A, "transform.pos"));
            t.Check(m != null, "TO2 a null owner is refused");
            t.Check(l.Count == 0, "TO2 refusals acquire nothing");
        }

        static void Forms(TestRunner t)
        {
            var (l, _) = New();
            string Msg(Action a) { try { a(); return null; } catch (ArgumentException e) { return e.Message; } catch (Exception e) { return "other " + e.GetType().Name; } }
            // Vector3
            var v = l.Acquire("x", A, "transform.pos");
            t.Check(Msg(() => v.Set(1)) != null && Msg(() => v.Set(1, 2, 3, 4)) != null && Msg(() => v.Set(new double[] { 1, 2 })) != null, "TO2 Vector3 refuses 1, 4 and 2 numbers");
            t.Check(Msg(() => v.Set(double.NaN, 0, 0)) != null && Msg(() => v.Set(new double[0])) != null, "TO2 Vector3 refuses NaN and an empty array");
            t.Check(new Func<bool>(() => { try { v.Set((double[])null); return false; } catch (ArgumentNullException) { return true; } })(), "TO2 Set(null) throws ArgumentNullException");
            string m = Msg(() => v.Set(1));
            t.Check(m != null && m.Contains("transform.pos") && m.Contains("x, y, z"), "TO2 the refusal names the path and the expected form: " + m);
            // Rotation
            var r = l.Acquire("x", A, "transform.rot");
            r.Set(90, 0, 0);
            double h = Math.Sqrt(0.5);
            t.Check(Near(r[0], h) && Near(r[1], 0) && Near(r[2], 0) && Near(r[3], h), "TO2 Rotation Euler (90, 0, 0) = quaternion (0.7071, 0, 0, 0.7071)");
            r.Set(30, 60, 45);
            Q(30, 0, 0, out var qx); Q(0, 60, 0, out var qy); Q(0, 0, 45, out var qz);
            var e = Mul(Mul(qy, qx), qz);
            t.Check(Near(r[0], e[0]) && Near(r[1], e[1]) && Near(r[2], e[2]) && Near(r[3], e[3]), "TO2 Rotation Euler (30, 60, 45) = qy · qx · qz (Z, then X, then Y)");
            r.Set(0, 0, 0, 2);
            t.Check(r[3] == 1 && r[0] == 0, "TO2 a quaternion is normalised");
            t.Check(Msg(() => r.Set(0, 0, 0, 0)) != null && Msg(() => r.Set(new double[] { 0, 0, 0, 0 })) != null, "TO2 a quaternion of length 0 is refused");
            t.Check(Msg(() => r.Set(1)) != null && Msg(() => r.Set(new double[] { 1, 2 })) != null, "TO2 Rotation refuses 1 and 2 numbers");
            // Color
            var c = l.Acquire("x", A, "item.colors[0].main");
            c.Set(new double[] { 0.1, 0.2, 0.3 });
            t.Check(c[0] == 0.1 && c[3] == 1, "TO2 Color [r, g, b]: alpha 1");
            c.Set(2, -1, 0.5, 0.25);
            t.Check(c[0] == 2 && c[1] == -1 && c[3] == 0.25, "TO2 Color values are kept as stored (not clamped)");
            t.Check(Msg(() => c.Set(1)) != null && Msg(() => c.Set(new double[] { 1, 2, 3, 4, 5 })) != null, "TO2 Color refuses 1 and 5 numbers");
            // Number, Integer
            var sp = l.Acquire("x", B, "chara.anime.speed");
            sp.Set(double.NaN);
            t.Check(sp[0] == 0, "TO2 animation speed NaN counts as 0");
            sp.Set(-2);
            t.Check(sp[0] == 0, "TO2 animation speed below 0 counts as 0");
            sp.Set(new double[] { 1.5 });
            t.Check(sp[0] == 1.5, "TO2 Number from one number in an array");
            sp.Set(double.NegativeInfinity);
            t.Check(sp[0] == 0, "TO2 animation speed minus infinity counts as 0");
            sp.Set(1.5);
            t.Check(Msg(() => sp.Set(double.PositiveInfinity)) != null && sp[0] == 1.5, "TO2 animation speed refuses plus infinity and keeps its value");
            t.Check(Msg(() => sp.Set(1, 2, 3)) != null && Msg(() => sp.Set(new double[] { 1, 2 })) != null, "TO2 Number refuses 3 and 2 numbers");
            var tm = l.Acquire("x", B, "chara.anime.time");
            t.Check(Msg(() => tm.Set(double.NaN)) != null, "TO2 animation time refuses NaN");
            tm.Set(2.75);
            t.Check(tm[0] == 2.75, "TO2 animation time is kept raw (not wrapped)");
            var eyes = l.Acquire("x", B, "chara.face.eyes");
            eyes.Set(2.5);
            t.Check(eyes[0] == 3 && eyes.Kind == OverrideValueKind.Integer, "TO2 Integer rounds to the nearest whole number (2.5 -> 3)");
            // Animation
            var an = l.Acquire("x", B, "chara.anime.ref");
            an.Set(0, 1, 7);
            t.Check(an[0] == 0 && an[1] == 1 && an[2] == 7 && an.AnimeSource == null, "TO2 Animation Set(group, category, no) without a source");
            t.Check(Msg(() => an.Set(1)) != null && Msg(() => an.Set(1, 2, 3, 4)) != null, "TO2 Animation refuses 1 and 4 numbers");
            // Camera
            var cam = l.Acquire("x", null, "camera");
            cam.Set(new double[] { 1, 2, 3, 90, 0, 0, 10, 23 });
            t.Check(cam[0] == 1 && cam[2] == 3 && Near(cam[3], h) && Near(cam[6], h) && cam[7] == 10 && cam[8] == 23, "TO2 Camera from 8 numbers (target, Euler rot, dist, fov)");
            t.Check(Msg(() => cam.Set(1, 2, 3)) != null && Msg(() => cam.Set(new double[7])) != null && Msg(() => cam.Set(1)) != null, "TO2 Camera refuses 3, 7 and 1 numbers");
            cam.SetCamera(0, 0, 0, 0, 0, 0, 3, 5, 20);
            t.Check(cam[6] == 1 && cam[7] == 5, "TO2 Camera typed setter normalises the quaternion");
            // Depth of field
            var d = l.Acquire("x", null, "effects.depthOfField");
            d.Set(new double[] { 1, 0.2, 0.5, 3, 1, 2, 3 });
            t.Check(d.Dof != null && d.Dof.Enabled && d.Dof.FocalSize == 0.2 && d.Dof.Aperture == 0.5 && d.Dof.Blur == 3 && d.Dof.Focus.Kind == OverrideFocusKind.Point && d.Dof.Focus.Z == 3,
                "TO2 DepthOfField from 7 numbers (enabled, size, aperture, blur, focus point)");
            t.Check(Msg(() => d.Set(1)) != null && Msg(() => d.Set(new double[8])) != null, "TO2 DepthOfField refuses 1 and 8 numbers");
        }

        static void JsonForms(TestRunner t)
        {
            var (l, _) = New();
            string Msg(Action a) { try { a(); return null; } catch (ArgumentException e) { return e.Message; } catch (Exception e) { return "other " + e.GetType().Name; } }
            var n = l.Acquire("x", B, "chara.face.mouthOpen");
            n.SetJson("0.75");
            t.Check(n[0] == 0.75, "TO2 Number JSON");
            t.Check(Msg(() => n.SetJson("[0.5]")) != null && Msg(() => n.SetJson("\"a\"")) != null && Msg(() => n.SetJson("{")) != null, "TO2 Number refuses an array, a string and bad JSON");
            var v = l.Acquire("x", A, "transform.scale");
            v.SetJson("[1, 2, 3]");
            t.Check(v[1] == 2 && Msg(() => v.SetJson("[1, 2]")) != null && Msg(() => v.SetJson("{\"x\":1}")) != null, "TO2 Vector3 JSON [x, y, z]; refuses 2 numbers and an object");
            var r = l.Acquire("x", A, "transform.rot");
            r.SetJson("[0, 0, 0, 1]");
            t.Check(r[3] == 1 && r.SetJson("[0, 180, 0]") && Near(r[1], 1), "TO2 Rotation JSON [x, y, z, w] and Euler [x, y, z]");
            var c = l.Acquire("x", C, "light.color");
            c.SetJson("[1, 0.5, 0]");
            t.Check(c[3] == 1 && c.SetJson("[1, 0.5, 0, 0.5]") && c[3] == 0.5, "TO2 Color JSON [r, g, b] and [r, g, b, a]");
            var an = l.Acquire("x", B, "chara.anime.ref");
            an.SetJson("{\"source\": \"illusion.hs2\", \"group\": 1, \"category\": 2, \"no\": 3}");
            t.Check(an.AnimeSource == "illusion.hs2" && an[2] == 3, "TO2 Animation JSON with a source");
            an.SetJson("{\"group\": 4, \"category\": 5, \"no\": 6}");
            t.Check(an.AnimeSource == null && an[0] == 4, "TO2 Animation JSON without a source");
            t.Check(Msg(() => an.SetJson("{\"group\": 4, \"category\": 5}")) != null && Msg(() => an.SetJson("{\"group\": 4, \"category\": 5, \"no\": 6, \"x\": 1}")) != null,
                "TO2 Animation refuses a missing member and an unknown one");
            var cam = l.Acquire("x", null, "camera");
            cam.SetJson("{\"target\": [0, 10, 0], \"rot\": [15, 180, 0], \"dist\": 25, \"fov\": 23}");
            t.Check(cam[1] == 10 && cam[7] == 25 && cam[8] == 23, "TO2 Camera JSON");
            t.Check(Msg(() => cam.SetJson("{\"target\": [0, 10, 0], \"rot\": [15, 180, 0], \"dist\": 25}")) != null, "TO2 Camera JSON needs all four members");
            var d = l.Acquire("x", null, "effects.depthOfField");
            d.SetJson("{\"enabled\": true, \"focus\": null, \"focalSize\": 0.1, \"aperture\": 0.6}");
            t.Check(d.Dof.Focus.Kind == OverrideFocusKind.Pivot && d.Dof.Blur == 0 && d.Dof.FocusTo == null, "TO2 DepthOfField JSON with the pivot (null), no blur");
            d.SetJson("{\"enabled\": true, \"focus\": \"pivot\", \"focalSize\": 0.1, \"aperture\": 0.6, \"blur\": 4}");
            t.Check(d.Dof.Focus.Kind == OverrideFocusKind.Pivot && d.Dof.Blur == 4, "TO2 DepthOfField \"pivot\" and blur");
            d.SetJson("{\"enabled\": false, \"focus\": {\"node\": \"" + B + "\"}, \"focalSize\": 0, \"aperture\": 0}");
            t.Check(d.Dof.Focus.Kind == OverrideFocusKind.Node && d.Dof.Focus.Node == B && !d.Dof.Enabled, "TO2 DepthOfField focus {node}");
            d.SetJson("{\"enabled\": 1, \"focus\": {\"node\": \"" + B + "\", \"bone\": \"cf_J_Head\", \"offset\": [0, 0.1, 0]}, \"focalSize\": 0.2, \"aperture\": 0.5}");
            t.Check(d.Dof.Focus.Kind == OverrideFocusKind.NodeBone && d.Dof.Focus.Bone == "cf_J_Head" && d.Dof.Focus.Y == 0.1 && d.Dof.Enabled, "TO2 DepthOfField focus {node, bone, offset}");
            d.SetJson("{\"enabled\": true, \"focus\": {\"point\": [1, 2, 3]}, \"focalSize\": 0.2, \"aperture\": 0.5, \"focusTo\": {\"dist\": 7}, \"focusBlend\": 0.25}");
            t.Check(d.Dof.Focus.Kind == OverrideFocusKind.Point && d.Dof.FocusTo.Kind == OverrideFocusKind.Dist && d.Dof.FocusTo.Dist == 7 && d.Dof.FocusBlend == 0.25,
                "TO2 DepthOfField focus {point}, focusTo {dist}, focusBlend");
            t.Check(Msg(() => d.SetJson("{\"enabled\": true, \"focus\": {\"nodes\": \"x\"}, \"focalSize\": 0.2, \"aperture\": 0.5}")) != null &&
                    Msg(() => d.SetJson("{\"enabled\": true, \"focalSize\": 0.2, \"aperture\": 0.5, \"focusBlend\": 2, \"focusTo\": null}")) != null &&
                    Msg(() => d.SetJson("{\"enabled\": true, \"focus\": null, \"aperture\": 0.5}")) != null &&
                    Msg(() => d.SetJson("[1, 0.2, 0.5, 3, 1, 2, 3]")) != null,
                "TO2 DepthOfField JSON refusals (unknown focus form, blend outside 0-1, missing focalSize, an array)");
            t.Check(d.Dof.Focus.Kind == OverrideFocusKind.Point, "TO2 a refused value leaves the slot's value as it was");
        }

        // ------------------------------------------------------------ arithmetic

        static bool Near(double a, double b) => Math.Abs(a - b) < 1e-12;

        static void Q(double ex, double ey, double ez, out double[] q)
        {
            double r = Math.PI / 360.0;
            if (ex != 0) q = new[] { Math.Sin(ex * r), 0, 0, Math.Cos(ex * r) };
            else if (ey != 0) q = new[] { 0, Math.Sin(ey * r), 0, Math.Cos(ey * r) };
            else q = new[] { 0, 0, Math.Sin(ez * r), Math.Cos(ez * r) };
        }

        static double[] Mul(double[] a, double[] b) => new[]
        {
            a[3] * b[0] + a[0] * b[3] + a[1] * b[2] - a[2] * b[1],
            a[3] * b[1] - a[0] * b[2] + a[1] * b[3] + a[2] * b[0],
            a[3] * b[2] + a[0] * b[1] - a[1] * b[0] + a[2] * b[3],
            a[3] * b[3] - a[0] * b[0] - a[1] * b[1] - a[2] * b[2],
        };
    }
}

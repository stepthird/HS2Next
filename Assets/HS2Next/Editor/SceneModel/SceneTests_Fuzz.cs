// M6: undo/redo fuzz. Per seed 300 random operations (adds of every kind,
// delete, duplicate, move, reorder, sets on random valid and invalid paths with random values, list and map
// operations, transactions with commit or cancel, the composite operations, undo and redo interleaved), starting
// from the golden scene (with its character subtree) or from an empty one. After every operation the model must
// pass Validate(); the state (canonical scene.json bytes, title, and the identity of every stored character
// subtree) is recorded per history position: a failed or NoOp operation leaves it unchanged, undo and redo return
// the recorded state, undo-all gives the initial bytes (subtree contents compared too), redo-all the final ones,
// and a random walk meets the recorded state at every position. HS2N_SCENE_FUZZ=n sets the seed count.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_Fuzz
    {
        public const int DefaultSeeds = 200;
        public const int Ops = 300;

        public static void Run(TestRunner t)
        {
            int seeds = int.TryParse(Environment.GetEnvironmentVariable("HS2N_SCENE_FUZZ"), out int n) && n > 0 ? n : DefaultSeeds;
            var golden = Kit.GoldenContainer(t, true);
            var sw = Stopwatch.StartNew();
            int failedSeeds = 0;
            long ops = 0, entries = 0, errors = 0;
            for (int seed = 1; seed <= seeds; seed++)
            {
                var run = new FuzzRun(t, (ulong)seed, seed % 2 == 1 ? golden : null);
                bool ok = run.Go();
                if (!ok) failedSeeds++;
                ops += run.OpsDone;
                entries += run.MaxDepth;
                errors += run.Errors;
                if (failedSeeds >= 5) { t.Info("stopping after 5 failed seeds"); break; }
            }
            t.Check(failedSeeds == 0, "fuzz: " + seeds + " seeds × " + Ops + " operations, " + failedSeeds + " seeds failed");
            t.Info("fuzz: " + seeds + " seeds, " + ops + " operations (" + errors + " refused with an error code), deepest history total " + entries +
                   ", " + sw.ElapsedMilliseconds + " ms");
        }

        sealed class State
        {
            public byte[] Scene;
            public string Rest;

            public bool SameAs(State o) => o != null && Rest == o.Rest && Kit.Same(Scene, o.Scene);
        }

        sealed class FuzzRun
        {
            readonly TestRunner t;
            readonly ulong seed;
            readonly DocFuzz g;
            readonly SceneSession s;
            readonly List<State> states = new List<State>();
            readonly string initialContent;
            readonly byte[] initialBytes;
            public int OpsDone, MaxDepth, Errors;
            string lastOp = "start";
            bool failed;

            static readonly string[] BoneKeys = { "cf_J_Head", "cf_J_Neck", "c_J_hair_F01", "cf_J_Hand_R", "x" };

            public FuzzRun(TestRunner t, ulong seed, SceneContainer golden)
            {
                this.t = t;
                this.seed = seed;
                g = new DocFuzz(seed * 7919 + 3, extras: false);
                var o = new SessionOptions { History = new HistoryLimits { MaxEntries = int.MaxValue, MaxBytes = long.MaxValue } };
                s = Kit.Session(seed, o);
                if (golden != null)
                {
                    // a private copy of the golden container (subtrees are shared objects otherwise)
                    var c = new SceneContainer { Scene = Kit.ReadScene(DocIo.Bytes(golden.Scene)), Title = golden.Title };
                    foreach (var kv in golden.Charas) c.Charas[kv.Key] = kv.Value.Clone();
                    s.Bind(c);
                }
                initialBytes = Kit.Bytes(s);
                initialContent = Kit.StateHash(s);
                states.Add(Capture());
            }

            State Capture()
            {
                var sb = new StringBuilder(s.Model.Title).Append('|');
                foreach (var n in s.Model.PreOrder())
                    if (n.Appearance != null)
                        sb.Append(n.Id).Append('=').Append(n.Appearance.Stored == null ? 0 : RuntimeHelpers.GetHashCode(n.Appearance.Stored))
                          .Append('/').Append(n.Appearance.Card == null ? 0 : RuntimeHelpers.GetHashCode(n.Appearance.Card)).Append(';');
                return new State { Scene = Kit.Bytes(s), Rest = sb.ToString() };
            }

            void Fail(string what)
            {
                if (!failed) t.Check(false, "fuzz seed " + seed + ", op " + OpsDone + " (" + lastOp + "): " + what);
                failed = true;
            }

            public bool Go()
            {
                for (int i = 0; i < Ops && !failed; i++)
                {
                    OpsDone++;
                    Step();
                }
                if (!failed) Finish();
                if (!failed) t.Check(true, "fuzz seed " + seed);
                return !failed;
            }

            void Step()
            {
                int depth = s.Commands.UndoList.Count;
                var before = states[depth];
                int k = g.Int(100);
                if (k < 10) { Undo(); return; }
                if (k < 16) { Redo(); return; }
                CommandResult r;
                try { r = Random(k); }
                catch (Exception e) { Fail("threw " + e); return; }
                if (r != null && r.Status == CommandStatus.Error)
                {
                    Errors++;
                    if (r.Code == CommandCodes.Internal) Fail("internal error: " + r.Message);
                }
                try { s.Model.Validate(); }
                catch (Exception e) { Fail(e.Message); return; }
                var after = Capture();
                int nd = s.Commands.UndoList.Count;
                if (nd == depth + 1)
                {
                    states.RemoveRange(depth + 1, states.Count - depth - 1);
                    states.Add(after);
                    if (s.Commands.CanRedo) Fail("redo branch not disposed");
                    MaxDepth = Math.Max(MaxDepth, nd);
                }
                else if (nd == depth)
                {
                    if (!after.SameAs(before)) Fail("state changed without a history entry (result " + r + ")");
                }
                else Fail("history depth " + depth + " → " + nd);
            }

            void Undo()
            {
                lastOp = "undo";
                int depth = s.Commands.UndoList.Count;
                var r = s.Commands.Undo();
                if (depth == 0) { if (r.Status != CommandStatus.NoOp) Fail("undo of nothing: " + r); return; }
                if (r.Status != CommandStatus.Ok) { Fail("undo: " + r); return; }
                Check(depth - 1);
            }

            void Redo()
            {
                lastOp = "redo";
                int depth = s.Commands.UndoList.Count;
                bool can = s.Commands.CanRedo;
                var r = s.Commands.Redo();
                if (!can) { if (r.Status != CommandStatus.NoOp) Fail("redo of nothing: " + r); return; }
                if (r.Status != CommandStatus.Ok) { Fail("redo: " + r); return; }
                Check(depth + 1);
            }

            void Check(int position)
            {
                try { s.Model.Validate(); }
                catch (Exception e) { Fail(e.Message); return; }
                if (s.Commands.UndoList.Count != position) { Fail("history position " + s.Commands.UndoList.Count + ", expected " + position); return; }
                if (!Capture().SameAs(states[position])) Fail("state at history position " + position + " differs from the recorded one");
            }

            void Finish()
            {
                // undo all and redo all check their end points (and Validate on the way); the random walk checks every position it meets
                lastOp = "undo all";
                int top = states.Count - 1;
                while (s.Commands.CanUndo && !failed)
                {
                    if (!s.Commands.Undo().Ok) Fail("undo failed at " + s.Commands.UndoList.Count);
                    try { s.Model.Validate(); }
                    catch (Exception e) { Fail(e.Message); }
                }
                if (failed) return;
                Check(0);
                if (failed) return;
                if (!Kit.Same(initialBytes, Kit.Bytes(s))) { Fail("undo all: bytes differ from the initial ones " + Kit.Diff(initialBytes, Kit.Bytes(s))); return; }
                if (Kit.StateHash(s) != initialContent) { Fail("undo all: subtree contents differ"); return; }
                lastOp = "redo all";
                while (s.Commands.CanRedo && !failed)
                {
                    if (!s.Commands.Redo().Ok) Fail("redo failed at " + s.Commands.UndoList.Count);
                    try { s.Model.Validate(); }
                    catch (Exception e) { Fail(e.Message); }
                }
                if (failed) return;
                if (s.Commands.UndoList.Count != top) { Fail("redo all reached " + s.Commands.UndoList.Count + " of " + top); return; }
                Check(top);
                lastOp = "random walk";
                for (int i = 0; i < 60 && !failed; i++)
                {
                    if (g.Bool() && s.Commands.CanUndo) { s.Commands.Undo(); Check(s.Commands.UndoList.Count); }
                    else if (s.Commands.CanRedo) { s.Commands.Redo(); Check(s.Commands.UndoList.Count); }
                }
            }

            // ---------------- operations ----------------

            TreeNode AnyNode()
            {
                var all = s.Model.PreOrder();
                return all.Count == 0 ? null : all[g.Int(all.Count)];
            }

            string AnyId() => g.Chance(0.05) ? "ffffffffffffffffffffffffffffff" + g.Hex(2) : AnyNode()?.Id;

            T Pick<T>(IList<T> items) => items[g.Int(items.Count)];

            static readonly NodeKind[] Kinds = { NodeKind.Chara, NodeKind.Item, NodeKind.Light, NodeKind.Folder, NodeKind.Route, NodeKind.Camera };

            (string parent, string point, int index) Place()
            {
                if (s.Model.Count == 0 || g.Chance(0.4)) return (null, g.Chance(0.03) ? "N_Head" : null, g.Range(-1, s.Model.Roots.Count + 1));
                var p = AnyNode();
                string point = null;
                if (p.Kind == NodeKind.Chara) point = g.Chance(0.9) ? Pick(DocEnum.AttachPoints) : g.Chance(0.5) ? null : "N_Future";
                else if (g.Chance(0.05)) point = "N_Hand_R";
                return (p.Id, point, g.Range(-1, s.Model.GroupOf(p, point).Count + 1));
            }

            CommandResult Random(int k)
            {
                if (k < 28) return Add();
                if (k < 33) { lastOp = "delete"; return s.Delete(Ids(g.Range(1, 3))); }
                if (k < 37) { lastOp = "duplicate"; return s.Duplicate(Ids(g.Range(1, 2))); }
                if (k < 45) return Move();
                if (k < 49) { var n = AnyNode(); lastOp = "reorder"; return n == null ? null : s.Reorder(n.Id, g.Range(-1, s.Model.GroupOf(n.Parent, n.Point).Count)); }
                if (k < 76)
                {
                    var (id, path, value) = SetPathValue();
                    lastOp = "set " + (id ?? "scene") + " " + path;
                    return s.Set(id, path, value);
                }
                if (k < 82) return ListOp();
                if (k < 85) return MapOp();
                if (k < 93) return Tx();
                return Composite();
            }

            List<string> Ids(int n)
            {
                var l = new List<string>();
                for (int i = 0; i < n; i++) { var id = AnyId(); if (id != null) l.Add(id); }
                return l;
            }

            CommandResult Add()
            {
                var kind = Pick(Kinds);
                var (p, point, index) = Place();
                lastOp = "add " + kind + " under " + (p ?? "root") + "/" + point + "#" + index;
                switch (kind)
                {
                    case NodeKind.Chara:
                        var app = g.Chance(0.5) ? new CharaAppearance { Card = Kit.Card("fuzz" + g.Int(3), "UserData/chara/fuzz" + g.Int(3) + ".png", g.Int(4)) } : null;
                        return s.AddChara(app, p, point, index);
                    case NodeKind.Item: return s.AddItem(new ItemNodeState { Ref = g.Ref(), Alpha = g.F32() }, p, point, index);
                    case NodeKind.Light: return s.AddLight(g.Ref(), p, point, index);
                    case NodeKind.Folder: return s.AddFolder(p, point, index);
                    case NodeKind.Route: return s.AddRoute(p, point, index);
                    default: return s.AddCamera(p, point, index);
                }
            }

            NodeTransform Transform(NodeKind k) => new NodeTransform
            {
                Pos = g.V3(), Rot = g.V3(), Scale = NodeRules.HasScale(k) || g.Chance(0.1) ? g.V3() : Vec3.One, InheritScale = g.Bool(),
            };

            CommandResult Move()
            {
                var n = AnyNode();
                if (n == null) return null;
                var (p, point, index) = Place();
                lastOp = "move " + n + " under " + (p ?? "root") + "/" + point + "#" + index;
                return s.Move(n.Id, p, point, index, g.Chance(0.3) ? Transform(n.Kind) : null);
            }

            (string, string, object) SetPathValue()
            {
                var n = g.Chance(0.15) ? null : AnyNode();
                if (n == null) return SceneSet();
                if (g.Chance(0.25))
                {
                    switch (g.Int(6))
                    {
                        case 0: return (n.Id, "transform.pos", g.V3());
                        case 1: return (n.Id, "transform.rot", g.V3());
                        case 2: return (n.Id, "transform.scale", g.Chance(0.5) ? Vec3.One : g.V3());
                        case 3: return (n.Id, "visible", g.Bool());
                        case 4: return (n.Id, "name", g.StrN());
                        default: return (n.Id, "transform", Transform(n.Kind));
                    }
                }
                switch (n.Kind)
                {
                    case NodeKind.Chara:
                        switch (g.Int(16))
                        {
                            case 0: return (n.Id, "chara.face.eyes", g.AnyInt());
                            case 1: return (n.Id, "chara.face.eyesOpen", g.F32());
                            case 2: return (n.Id, "chara.anime.speed", g.Chance(0.1) ? (object)float.NaN : g.F32());
                            case 3: return (n.Id, "chara.anime.ref", g.Chance(0.3) ? null : new AnimeRef(g.Str(), g.AnyInt(), g.AnyInt(), g.AnyInt()));
                            case 4: return (n.Id, "chara.hands", g.Ints(g.Chance(0.9) ? 2 : 3));
                            case 5: return (n.Id, "chara.hands[" + g.Int(3) + "]", g.AnyInt());
                            case 6: return (n.Id, "chara.correction", g.Bools(8));
                            case 7: return (n.Id, "chara.fk.bones[" + FieldPath.Quote(Pick(BoneKeys)) + "]", g.V3());
                            case 8: return (n.Id, "chara.look.neckFix[" + FieldPath.Quote(Pick(BoneKeys)) + "]", g.Quat());
                            case 9: return (n.Id, "chara.look.eyeRotations", g.Chance(0.4) ? null : new EyeRotations { Left = g.Quat(), Right = g.Quat() });
                            case 10:
                                var ids = n.Appearance?.WornAccessoryIds();
                                string acc = ids != null && ids.Count > 0 && g.Chance(0.8) ? ids[g.Int(ids.Count)] : g.Id();
                                return (n.Id, "chara.state.accessories[" + FieldPath.Quote(acc) + "]", g.Bool());
                            case 11: return (n.Id, "chara.ik.targets." + Pick(IkTargets.Names), g.Chance(0.3) ? null : new IkTarget { Pos = g.V3(), Rot = g.V3() });
                            case 12: return (n.Id, "chara.ik.targets.Body.pos", g.V3());
                            case 13: return (n.Id, "chara.voice.repeat", Pick(new[] { "none", "all", "select", "loud" }));
                            case 14: return (n.Id, "chara.state.clothes", g.Ints(8));
                            default: return (n.Id, "chara.face", new CharaFaceState { Eyes = g.AnyInt(), Tears = g.F32(), Blink = g.Bool() });
                        }
                    case NodeKind.Item:
                        switch (g.Int(8))
                        {
                            case 0: return (n.Id, "item.colors[" + g.Int(5) + "].main", g.Col());
                            case 1: return (n.Id, "item.alpha", g.F32());
                            case 2: return (n.Id, "item.options", g.List(g.Bool, 4));
                            case 3: return (n.Id, "item.ref", g.Chance(0.1) ? "bad" : g.Ref());
                            case 4: return (n.Id, "item.anime.speed", g.F32());
                            case 5: return (n.Id, "item.fk.bones[" + FieldPath.Quote(Pick(BoneKeys)) + "]", g.V3());
                            case 6: return (n.Id, "item.panel", g.Chance(0.5) ? null : FileRef.FromUser(g.Str()));
                            default: return (n.Id, "item.colors[" + g.Int(4) + "]", new ItemColorSlot { Main = g.Col(), Metallic = g.F32() });
                        }
                    case NodeKind.Light:
                        switch (g.Int(4))
                        {
                            case 0: return (n.Id, "light.color", g.Col());
                            case 1: return (n.Id, "light.intensity", g.F32());
                            case 2: return (n.Id, "light.enabled", g.Bool());
                            default: return (n.Id, "light.ref", g.Ref());
                        }
                    case NodeKind.Camera:
                        return g.Bool() ? (n.Id, "camera.fov", g.Chance(0.3) ? null : (object)g.F32()) : (n.Id, "camera.active", (object)g.Bool());
                    case NodeKind.Route:
                        var pts = ((RouteNodeState)n.State).Points.Count;
                        switch (g.Int(5))
                        {
                            case 0: return (n.Id, "route.orient", Pick(new[] { "none", "xy", "y", "z" }));
                            case 1: return (n.Id, "route.points[" + g.Int(pts + 1) + "].pos", g.V3());
                            case 2: return (n.Id, "route.points[" + g.Int(pts) + "].ease", Pick(new[] { "linear", "easeInQuad", "spring", "wobble" }));
                            case 3: return (n.Id, "route.loop", g.Bool());
                            default: return (n.Id, "route.points[" + g.Int(pts) + "].aid", g.Chance(0.4) ? null : new RouteAid { Pos = g.V3(), Init = g.Bool() });
                        }
                    case NodeKind.Opaque: return (n.Id, "name", "x");
                    default: return (n.Id, "name", g.StrN());
                }
            }

            (string, string, object) SceneSet()
            {
                switch (g.Int(10))
                {
                    case 0: return (null, "effects.bloom.intensity", g.F32());
                    case 1: return (null, "map.pos", g.V3());
                    case 2: return (null, "map.ref", g.Chance(0.3) ? null : g.Ref());
                    case 3: return (null, "title", g.Str());
                    case 4: return (null, "charaLight.color", g.Col());
                    case 5: return (null, "effects.depthOfField.focus", g.Chance(0.3) ? null : AnyId());
                    case 6: return (null, "effects.sunShafts.caster", g.Chance(0.3) ? null : AnyId());
                    case 7: return (null, "camera.slots[" + g.Int(12) + "]", g.Chance(0.4) ? null : new CameraView { Pivot = g.V3(), Distance = g.F32(), Fov = g.F32() });
                    case 8: return (null, "sound.bgm.repeat", Pick(new[] { "none", "all", "select" }));
                    default: return (null, "effects.fog", new FogEffect { Enabled = g.Bool(), Density = g.F32(), Color = g.Col() });
                }
            }

            CommandResult ListOp()
            {
                var n = AnyNode();
                lastOp = "list op";
                if (n?.Kind == NodeKind.Route)
                {
                    int count = ((RouteNodeState)n.State).Points.Count;
                    switch (g.Int(3))
                    {
                        case 0: return s.Commands.Execute(new ListInsertCommand(n.Id, "route.points", g.Int(count + 2), new RoutePoint { Id = g.Chance(0.9) ? s.NewId() : null, Pos = g.V3() }));
                        case 1: return s.Commands.Execute(new ListRemoveCommand(n.Id, "route.points", g.Int(count + 1)));
                        default: return s.Commands.Execute(new ListMoveCommand(n.Id, "route.points", g.Int(count), g.Int(count)));
                    }
                }
                if (n?.Kind == NodeKind.Chara)
                {
                    int count = ((CharaNodeState)n.State).Voice.List.Count;
                    return g.Bool() ? s.Commands.Execute(new ListInsertCommand(n.Id, "chara.voice.list", g.Int(count + 1), new AnimeRef(g.Str(), 0, 1, g.AnyInt())))
                                    : s.Commands.Execute(new ListRemoveCommand(n.Id, "chara.voice.list", g.Int(count + 1)));
                }
                if (n?.Kind == NodeKind.Item)
                    return s.Commands.Execute(new ListInsertCommand(n.Id, "item.options", g.Int(((ItemNodeState)n.State).Options.Count + 1), g.Bool()));
                int slots = s.Model.Settings.Camera.Slots.Count;
                return g.Bool() ? s.Commands.Execute(new ListInsertCommand(null, "camera.slots", g.Int(slots + 1), null))
                                : s.Commands.Execute(new ListRemoveCommand(null, "camera.slots", g.Int(slots + 1)));
            }

            CommandResult MapOp()
            {
                var n = AnyNode();
                lastOp = "map remove";
                if (n == null) return null;
                if (n.Kind == NodeKind.Chara)
                {
                    var cs = (CharaNodeState)n.State;
                    switch (g.Int(3))
                    {
                        case 0: return s.Commands.Execute(new MapRemoveCommand(n.Id, "chara.fk.bones", First(cs.Fk.Bones.Keys)));
                        case 1: return s.Commands.Execute(new MapRemoveCommand(n.Id, "chara.look.neckFix", First(cs.Look.NeckFix.Keys)));
                        default: return s.Commands.Execute(new MapRemoveCommand(n.Id, "chara.state.accessories", First(cs.State.Accessories.Keys)));
                    }
                }
                if (n.Kind == NodeKind.Item) return s.Commands.Execute(new MapRemoveCommand(n.Id, "item.fk.bones", First(((ItemNodeState)n.State).Fk.Bones.Keys)));
                return s.Commands.Execute(new MapRemoveCommand(n.Id, "chara.fk.bones", "x"));
            }

            string First(IEnumerable<string> keys)
            {
                var l = new List<string>(keys);
                return l.Count == 0 || g.Chance(0.1) ? "missing" : l[g.Int(l.Count)];
            }

            CommandResult Tx()
            {
                lastOp = "transaction";
                var tx = s.Commands.Begin("fuzz tx");
                if (!tx.IsOpen) { Fail("Begin failed: " + tx.Failure); return null; }
                int steps = g.Range(1, 6);
                for (int i = 0; i < steps; i++)
                {
                    switch (g.Int(4))
                    {
                        case 0:
                        case 1:
                            var (id, path, value) = SetPathValue();
                            if (path == "camera.active") break;
                            tx.Set(id, path, value);
                            break;
                        case 2:
                            var n = AnyNode();
                            if (n != null) { var (p, point, index) = Place(); tx.Execute(new MoveNodeCommand(n.Id, p, point, index)); }
                            break;
                        default:
                            if (g.Bool()) tx.Execute(new RemoveNodesCommand(Ids(1)));
                            else tx.Execute(new AddNodesCommand(new[] { new NodeSpec { Doc = NodeDefaults.Folder(s.NewId()) } }));
                            break;
                    }
                }
                if (g.Chance(0.3)) { tx.Cancel(); return null; }
                return tx.Commit();
            }

            CommandResult Composite()
            {
                var n = AnyNode();
                switch (g.Int(4))
                {
                    case 0: lastOp = "animation"; return s.SetAnimation(n?.Id, g.Chance(0.2) ? null : new AnimeRef(g.Str(), g.AnyInt(), 0, 1));
                    case 1: lastOp = "map"; return s.SetMap(g.Chance(0.2) ? null : g.Ref());
                    case 2: lastOp = "active camera"; return s.SetActiveCamera(g.Chance(0.2) ? null : n?.Id);
                    default:
                        lastOp = "appearance";
                        return s.SetAppearance(n?.Id, new CharaAppearance { Card = Kit.Card("fuzz" + g.Int(4), "UserData/chara/f.png", g.Int(5)) });
                }
            }
        }
    }
}

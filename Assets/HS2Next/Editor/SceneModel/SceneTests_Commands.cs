// M2: do/undo/redo of every command kind with canonical bytes compared,
// NoOp on equal values (−0 included), the value rules, transactions (collapse, cancel, nesting, undo while
// open), merging by merge key, history limits (entries and bytes), redo-branch disposal and NodeForgotten,
// history cleared by new/bind, unrecorded view paths, selection restored by undo/redo, and failure atomicity of
// composites.
using System;
using System.Collections.Generic;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_Commands
    {
        public static void Run(TestRunner t)
        {
            SetLeafTypes(t);
            ListAndMap(t);
            Structural(t);
            NoOps(t);
            ValueRules(t);
            Transactions(t);
            Merging(t);
            Limits(t);
            Forgotten(t);
            ClearOnLoad(t);
            ViewState(t);
            SelectionContext(t);
            Atomicity(t);
        }

        /// <summary>Executes <paramref name="op"/>, then undo and redo, comparing canonical bytes.</summary>
        internal static void DoUndoRedo(TestRunner t, SceneSession s, string what, Func<CommandResult> op)
        {
            var before = Kit.StateHash(s);
            var r = op();
            if (r.Status != CommandStatus.Ok) { t.Check(false, what + ": " + r); return; }
            var after = Kit.StateHash(s);
            t.Check(before != after, what + ": changes the model");
            Kit.Ok(t, s.Commands.Undo(), what + ": undo");
            t.Check(Kit.StateHash(s) == before, what + ": undo restores the bytes");
            Kit.Ok(t, s.Commands.Redo(), what + ": redo");
            t.Check(Kit.StateHash(s) == after, what + ": redo restores the bytes");
            Kit.Valid(t, s, what);
        }

        static void SetLeafTypes(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            string c = Kit.GoldenChara, item = Kit.GoldenItem, cam = Kit.GoldenCamera;
            DoUndoRedo(t, s, "set bool", () => s.Set(c, "visible", false));
            DoUndoRedo(t, s, "set int", () => s.Set(c, "chara.face.eyes", 7));
            DoUndoRedo(t, s, "set float", () => s.Set(c, "chara.anime.speed", 2.5f));
            DoUndoRedo(t, s, "set int as float", () => s.Set(c, "chara.anime.pattern", 1));
            DoUndoRedo(t, s, "set string", () => s.Set(c, "name", "名前 \"x\""));
            DoUndoRedo(t, s, "set null string", () => s.Set(Kit.GoldenFolder, "name", null));
            DoUndoRedo(t, s, "set Vec3", () => s.Set(c, "transform.pos", new Vec3(1, 2, 3)));
            DoUndoRedo(t, s, "set Vec4", () => s.Set(item, "item.colors[0].pattern.uv", new Vec4(0.5f, 0.5f, 2, 2)));
            DoUndoRedo(t, s, "set Color4", () => s.Set(Kit.GoldenLight, "light.color", new Color4(1, 0, 0, 1)));
            DoUndoRedo(t, s, "set Quat4 map value", () => s.Set(c, "chara.look.neckFix[\"cf_J_Head\"]", new Quat4(0, 0, 0, 1)));
            DoUndoRedo(t, s, "add a map key", () => s.Set(c, "chara.fk.bones[\"cf_J_Neck\"]", new Vec3(1, 2, 3)));
            DoUndoRedo(t, s, "set int[]", () => s.Set(c, "chara.hands", new[] { 2, 4 }));
            DoUndoRedo(t, s, "set int[] element", () => s.Set(c, "chara.hands[0]", 5));
            DoUndoRedo(t, s, "set bool[]", () => s.Set(c, "chara.correction", new bool[8]));
            DoUndoRedo(t, s, "set float[]", () => s.Set(c, "chara.anime.params", new[] { 0.25f, 0.5f }));
            DoUndoRedo(t, s, "set float?", () => s.Set(cam, "camera.fov", 40f));
            DoUndoRedo(t, s, "set float? null", () => s.Set(cam, "camera.fov", null));
            DoUndoRedo(t, s, "set map", () => s.Set(c, "chara.fk.bones", new SortedDictionary<string, Vec3>(StringComparer.Ordinal) { ["a"] = new Vec3(1, 0, 0) }));
            DoUndoRedo(t, s, "set List<bool>", () => s.Set(item, "item.options", new List<bool> { true, false, true }));
            DoUndoRedo(t, s, "set List<RoutePoint>", () => s.Set(Kit.GoldenRoute, "route.points", new List<RoutePoint> { new RoutePoint { Id = "e0000000000000000000000000000001" } }));
            DoUndoRedo(t, s, "set DocObject", () => s.Set(c, "chara.face", new CharaFaceState { Eyes = 3, Tears = 0.5f }));
            DoUndoRedo(t, s, "set DocObject null", () => s.Set(c, "chara.look.eyeRotations", null));
            DoUndoRedo(t, s, "set FileRef", () => s.Set(item, "item.panel", FileRef.FromUser("panels/a.png")));
            DoUndoRedo(t, s, "set ItemColorSlot element", () => s.Set(item, "item.colors[1]", new ItemColorSlot { Metallic = 0.5f }));
            DoUndoRedo(t, s, "set IK target", () => s.Set(c, "chara.ik.targets.RightFoot", new IkTarget { Pos = new Vec3(0, 1, 0) }));
            DoUndoRedo(t, s, "set scene float", () => s.Set(null, "effects.bloom.intensity", 3f));
            DoUndoRedo(t, s, "set scene ref", () => s.Set(null, "map.ref", "test.mod#10"));
            DoUndoRedo(t, s, "set scene null ref", () => s.Set(null, "map.ref", null));
            DoUndoRedo(t, s, "set title", () => s.Set(null, "title", "a title"));
            DoUndoRedo(t, s, "set scene id ref", () => s.Set(null, "effects.depthOfField.focus", Kit.GoldenFolder));
            DoUndoRedo(t, s, "set camera slot", () => s.Set(null, "camera.slots[2]", new CameraView { Distance = 5, Fov = 30 }));
            DoUndoRedo(t, s, "set enum", () => s.Set(Kit.GoldenRoute, "route.orient", "y"));
            DoUndoRedo(t, s, "set animation (composite)", () => s.SetAnimation(c, new AnimeRef("test.mod", 1, 2, 3)));
            DoUndoRedo(t, s, "set map (composite)", () => s.SetMap("test.mod#11"));
            t.Equal("a title", s.Model.Title, "title after redo");
        }

        static void ListAndMap(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            string r = Kit.GoldenRoute, c = Kit.GoldenChara;
            DoUndoRedo(t, s, "list insert", () => s.Commands.Execute(new ListInsertCommand(r, "route.points", 1, new RoutePoint { Id = s.NewId(), Pos = new Vec3(1, 1, 1) })));
            DoUndoRedo(t, s, "list remove", () => s.Commands.Execute(new ListRemoveCommand(r, "route.points", 2)));
            DoUndoRedo(t, s, "list move", () => s.Commands.Execute(new ListMoveCommand(r, "route.points", 0, 2)));
            DoUndoRedo(t, s, "voice list insert", () => s.Commands.Execute(new ListInsertCommand(c, "chara.voice.list", 0, new AnimeRef("test.mod", 0, 0, 1))));
            DoUndoRedo(t, s, "camera slot list insert", () => s.Commands.Execute(new ListInsertCommand(null, "camera.slots", 0, null)));
            DoUndoRedo(t, s, "map remove", () => s.Commands.Execute(new MapRemoveCommand(c, "chara.fk.bones", "cf_J_Head")));
            Kit.Fails(t, s.Commands.Execute(new MapRemoveCommand(c, "chara.fk.bones", "nope")), CommandCodes.NotFound, "map remove of a missing key");
            Kit.Fails(t, s.Commands.Execute(new ListRemoveCommand(r, "route.points", 99)), CommandCodes.NotFound, "list remove out of range");
            Kit.Fails(t, s.Commands.Execute(new ListInsertCommand(r, "route.points", 0, new RoutePoint { Id = "4e4e4e4e4e4e4e4e4e4e4e4e4e4e4e4e" })), CommandCodes.BadArgs, "duplicate route point id");
            Kit.Fails(t, s.Commands.Execute(new ListInsertCommand(r, "route.points", 0, new RoutePoint())), CommandCodes.BadArgs, "route point without id");
            Kit.Fails(t, s.Commands.Execute(new ListInsertCommand(c, "chara.face", 0, 1)), CommandCodes.BadArgs, "insert into a non-list");
            // a route keeps its start point
            var s2 = Kit.Session();
            string route = s2.AddRoute().NewId;
            Kit.Fails(t, s2.Commands.Execute(new ListRemoveCommand(route, "route.points", 0)), CommandCodes.BadArgs, "remove the last route point");
            Kit.Fails(t, s2.Set(route, "route.points", new List<RoutePoint>()), CommandCodes.BadArgs, "empty route point list");
        }

        static void Structural(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            DoUndoRedo(t, s, "add folder", () => s.AddFolder());
            DoUndoRedo(t, s, "add nested nodes", () => s.AddNodes(new[] { new NodeSpec { Doc = NodeDefaults.Folder(null), Children = new[] { new NodeSpec { Doc = NodeDefaults.Item(null, new ItemNodeState { Ref = "test.mod#1" }) } } } }));
            DoUndoRedo(t, s, "move", () => s.Move(Kit.GoldenItem, Kit.GoldenChara, "N_Hand_L", 0, new NodeTransform { Pos = new Vec3(1, 1, 1) }));
            // delete clears effect references into the removed subtree (the item is the DOF focus, its light the sun caster)
            var fx = s.Model.Settings.Effects;
            t.Check(fx.DepthOfField.Focus == Kit.GoldenItem && fx.SunShafts.Caster == Kit.GoldenLight, "golden: focus and caster set");
            DoUndoRedo(t, s, "delete with references", () => s.Delete(new[] { Kit.GoldenItem }));
            s.Commands.Undo();
            t.Check(fx.DepthOfField.Focus == Kit.GoldenItem && fx.SunShafts.Caster == Kit.GoldenLight, "undo of delete restores focus and caster");
            s.Commands.Redo();
            t.Check(s.Model.Settings.Effects.DepthOfField.Focus == null && s.Model.Settings.Effects.SunShafts.Caster == null, "delete cleared focus and caster");
            var card = Kit.Card("appearance test", "UserData/chara/female/a.png", 3);
            DoUndoRedo(t, s, "appearance", () => s.SetAppearance(Kit.GoldenChara, new CharaAppearance { Card = card }));
            DoUndoRedo(t, s, "active camera", () => s.SetActiveCamera(Kit.GoldenCamera));
            Kit.Fails(t, s.SetAppearance(Kit.GoldenFolder, new CharaAppearance()), CommandCodes.WrongKind, "appearance on a folder");
            DoUndoRedo(t, s, "delete several", () => s.Delete(new[] { Kit.GoldenFolder, Kit.GoldenCamera, Kit.GoldenRoute }));
            Kit.Fails(t, s.Delete(new[] { "ffffffffffffffffffffffffffffffff" }), CommandCodes.NotFound, "delete a missing node");
        }

        static void NoOps(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            int h = s.Commands.UndoList.Count;
            var pos = s.Model.Find(Kit.GoldenChara).Doc.Transform.Pos;
            Kit.NoOp(t, s.Set(Kit.GoldenChara, "transform.pos", pos), "set the same Vec3");
            Kit.NoOp(t, s.Set(Kit.GoldenChara, "transform.pos", new Vec3(-0f, pos.Y, pos.Z)), "set −0 where 0 is stored");
            Kit.NoOp(t, s.Set(Kit.GoldenChara, "chara.face.tears", -0f), "set −0 float where 0 is stored");
            Kit.NoOp(t, s.Set(Kit.GoldenChara, "chara.voice.repeat", "shuffle"), "set the stored unknown enum string again");
            Kit.NoOp(t, s.Set(Kit.GoldenChara, "chara.face", DocClone.Clone(((CharaNodeState)s.Model.Find(Kit.GoldenChara).State).Face)), "set an equal object");
            Kit.NoOp(t, s.Set(Kit.GoldenItem, "item.fk.bones", new SortedDictionary<string, Vec3>(((ItemNodeState)s.Model.Find(Kit.GoldenItem).State).Fk.Bones)), "set an equal map");
            Kit.NoOp(t, s.SetActiveCamera(null), "deactivate when none is active");
            t.Equal(h, s.Commands.UndoList.Count, "NoOps record nothing");
            var stored = s.Model.Find(Kit.GoldenChara).Doc.Transform.Pos;
            t.Check(BitConverter.ToInt32(BitConverter.GetBytes(stored.X), 0) == 0, "stored −0 normalised (+0 kept)");
            // −0 normalisation on write
            Kit.Ok(t, s.Set(Kit.GoldenChara, "transform.pos", new Vec3(-0f, 5, -0f)), "set a vector with −0");
            var p = s.Model.Find(Kit.GoldenChara).Doc.Transform.Pos;
            t.Check(BitConverter.ToInt32(BitConverter.GetBytes(p.X), 0) == 0 && BitConverter.ToInt32(BitConverter.GetBytes(p.Z), 0) == 0, "−0 written as +0");
        }

        static void ValueRules(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            string c = Kit.GoldenChara;
            Kit.Fails(t, s.Set(c, "chara.anime.speed", float.NaN), CommandCodes.BadArgs, "NaN");
            Kit.Fails(t, s.Set(c, "transform.pos", new Vec3(float.PositiveInfinity, 0, 0)), CommandCodes.BadArgs, "infinite component");
            Kit.Fails(t, s.Set(c, "chara.hands", new[] { 1, 2, 3 }), CommandCodes.BadArgs, "wrong length hands");
            Kit.Fails(t, s.Set(c, "chara.correction", new bool[7]), CommandCodes.BadArgs, "wrong length correction");
            Kit.Fails(t, s.Set(c, "chara.anime.params", new float[1]), CommandCodes.BadArgs, "wrong length params");
            Kit.Fails(t, s.Set(c, "chara.state.clothes", new int[7]), CommandCodes.BadArgs, "wrong length clothes");
            Kit.Fails(t, s.Set(c, "chara.state.liquid", new int[6]), CommandCodes.BadArgs, "wrong length liquid");
            Kit.Fails(t, s.Set(null, "charaLight.rot", new float[3]), CommandCodes.BadArgs, "wrong length charaLight.rot");
            Kit.Fails(t, s.Set(Kit.GoldenItem, "item.colors", new ItemColorSlot[3]), CommandCodes.BadArgs, "wrong length colors");
            Kit.Fails(t, s.Set(Kit.GoldenItem, "item.ref", "no hash sign"), CommandCodes.BadArgs, "bad ref");
            Kit.Fails(t, s.Set(Kit.GoldenItem, "item.ref", null), CommandCodes.BadArgs, "null ref where required");
            Kit.Fails(t, s.Set(null, "effects.colorGrading.lut", "x#"), CommandCodes.BadArgs, "bad optional ref");
            Kit.Fails(t, s.Set(Kit.GoldenRoute, "route.orient", "spin"), CommandCodes.BadArgs, "unknown enum");
            Kit.Fails(t, s.Set(null, "sound.bgm.repeat", "select"), CommandCodes.BadArgs, "unknown enum (bgm)");
            Kit.Fails(t, s.Set(Kit.GoldenRoute, "route.points[0].ease", "easeSideways"), CommandCodes.BadArgs, "unknown ease");
            Kit.Fails(t, s.Set(null, "effects.depthOfField.focus", "ffffffffffffffffffffffffffffffff"), CommandCodes.BadArgs, "unknown node id");
            Kit.Fails(t, s.Set(null, "effects.sunShafts.caster", "not an id"), CommandCodes.BadArgs, "malformed node id");
            Kit.Fails(t, s.Set(c, "visible", 1), CommandCodes.BadArgs, "wrong type");
            Kit.Fails(t, s.Set(c, "chara.face.eyes", 1.5f), CommandCodes.BadArgs, "float for int");
            Kit.Fails(t, s.Set(c, "chara.face", null), CommandCodes.BadArgs, "null object where required");
            Kit.Fails(t, s.Set(c, "chara.face", new CharaLookState()), CommandCodes.BadArgs, "object of the wrong type");
            Kit.Fails(t, s.Set(c, "chara.anime", new CharaAnimeState { Params = new float[3] }), CommandCodes.BadArgs, "object with a wrong-length array");
            Kit.Fails(t, s.Set(c, "chara.voice", new CharaVoiceState { Repeat = "loud" }), CommandCodes.BadArgs, "object with an unknown enum");
            Kit.Fails(t, s.Set(c, "chara.ik.targets.RightFoot.pos", new Vec3(1, 1, 1)), CommandCodes.NotFound, "path through a null IK target");
            Kit.Fails(t, s.Set(Kit.GoldenItem, "item.options[99]", true), CommandCodes.NotFound, "index out of range");
            Kit.Fails(t, s.Set(c, "chara.face.nope", 1), CommandCodes.BadArgs, "unknown member");
            Kit.Fails(t, s.Set(c, "item.alpha", 1f), CommandCodes.WrongKind, "kind section of another kind");
            Kit.Fails(t, s.Set(c, "id", "00000000000000000000000000000000"), CommandCodes.BadArgs, "id is not settable");
            Kit.Fails(t, s.Set(c, "parent", null), CommandCodes.BadArgs, "parent is not settable");
            Kit.Fails(t, s.Set(c, "order", 3), CommandCodes.BadArgs, "order is not settable");
            Kit.Fails(t, s.Set(c, "ext", null), CommandCodes.BadArgs, "ext is not settable");
            Kit.Fails(t, s.Set(c, "chara", new CharaNodeState()), CommandCodes.BadArgs, "a kind section as a whole");
            Kit.Fails(t, s.Set(c, "appearance.face", 1), CommandCodes.Unsupported, "reserved root appearance");
            Kit.Fails(t, s.Set(c, "unknown", null), CommandCodes.BadArgs, "unknown members are not addressable");
            Kit.Fails(t, s.Set(Kit.GoldenFolder, "ui.expandedGroups", new List<int> { 1 }), CommandCodes.WrongKind, "attach groups on a folder");
            Kit.Fails(t, s.Set(Kit.GoldenRoute, "route.points[0].id", "e0000000000000000000000000000009"), CommandCodes.BadArgs, "route point ids are not settable");
            Kit.Fails(t, s.Set(Kit.GoldenItem, "item.panel.res", "x"), CommandCodes.BadArgs, "no paths into a file reference");
            Kit.Fails(t, s.Set(c, "chara.face..eyes", 1), CommandCodes.BadArgs, "malformed path");
            Kit.Fails(t, s.Set("ffffffffffffffffffffffffffffffff", "visible", true), CommandCodes.NotFound, "missing node");
            Kit.Fails(t, s.Set(null, "nodes", null), CommandCodes.BadArgs, "unknown scene member");
            Kit.Fails(t, s.Set(null, "title", null), CommandCodes.BadArgs, "null title");
            Kit.Fails(t, s.Set(c, "chara.fk.bones[\"x\"]", FieldValue.Absent), CommandCodes.BadArgs, "Absent is not a value");
            t.Equal(0, s.Commands.UndoList.Count, "failed sets record nothing");
            Kit.Valid(t, s, "value rules");
        }

        static void Transactions(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            var rec = new EventRecorder(s);
            var before = Kit.Bytes(s);
            using (var tx = s.Commands.Begin("Drag"))
            {
                t.Check(tx.IsOpen && s.Commands.ActiveTransaction == tx, "transaction open");
                for (int i = 1; i <= 10; i++) tx.Set(Kit.GoldenChara, "transform.pos", new Vec3(i, 0, 0));
                tx.Set(Kit.GoldenItem, "transform.pos", new Vec3(0, 1, 0));
                tx.Set(Kit.GoldenChara, "transform.pos", new Vec3(20, 0, 0));
                var nested = s.Commands.Begin("Nested");
                t.Check(!nested.IsOpen && nested.Failure?.Code == CommandCodes.Busy, "nested Begin → busy");
                Kit.Fails(t, s.Commands.Undo(), CommandCodes.Busy, "undo during a transaction");
                Kit.Fails(t, s.Commands.Redo(), CommandCodes.Busy, "redo during a transaction");
                t.Equal(2, tx.StepCount, "sets on the same path collapse (two targets)");
                t.Equal(12, rec.Batches.Count, "one Preview batch per applied step");
                var r = tx.Commit();
                Kit.Ok(t, r, "commit");
                t.Check(r.Command is CompositeCommand cc && cc.Children.Count == 2, "one composite of two collapsed sets");
            }
            t.Equal(1, s.Commands.UndoList.Count, "exactly one history entry");
            t.Equal("Drag", s.Commands.UndoList[0].Label, "entry label");
            t.Check(rec.Batches[rec.Batches.Count - 1].Origin == ChangeOrigin.Command, "commit delivers an origin Command batch");
            Kit.Ok(t, s.Commands.Undo(), "undo the transaction");
            t.Check(Kit.Same(before, Kit.Bytes(s)), "undo of a transaction restores the bytes");
            Kit.Ok(t, s.Commands.Redo(), "redo the transaction");
            t.Equal(20f, s.Model.Find(Kit.GoldenChara).Doc.Transform.Pos.X, "redo applies the last value");

            // cancel and dispose
            var mid = Kit.Bytes(s);
            int entries = s.Commands.UndoList.Count;
            var tx2 = s.Commands.Begin("Cancelled");
            tx2.Set(Kit.GoldenChara, "chara.face.eyes", 9);
            tx2.Execute(new RemoveNodesCommand(new[] { Kit.GoldenCamera }));
            tx2.Set(null, "effects.bloom.intensity", 9f);
            rec.Clear();
            tx2.Cancel();
            t.Check(Kit.Same(mid, Kit.Bytes(s)), "cancel restores the bytes");
            t.Check(rec.Batches.Count == 1 && rec.Batches[0].Origin == ChangeOrigin.Cancel, "one Cancel batch");
            t.Equal(entries, s.Commands.UndoList.Count, "cancel records nothing");
            using (var tx3 = s.Commands.Begin("Disposed")) tx3.Set(Kit.GoldenChara, "chara.face.eyes", 5);
            t.Check(Kit.Same(mid, Kit.Bytes(s)) && s.Commands.ActiveTransaction == null, "dispose without commit cancels");
            // net nothing
            var tx4 = s.Commands.Begin("Nothing");
            tx4.Set(Kit.GoldenChara, "chara.face.eyes", 5);
            tx4.Set(Kit.GoldenChara, "chara.face.eyes", ((CharaNodeState)s.Model.Find(Kit.GoldenChara).State).Face.Eyes == 5 ? 2 : 2);
            tx4.Set(Kit.GoldenChara, "chara.face.eyes", 2);
            Kit.NoOp(t, tx4.Commit(), "a transaction whose net effect is nothing");
            t.Equal(entries, s.Commands.UndoList.Count, "net-nothing transaction records nothing");
            // view state inside a transaction applies at once and stays out of it
            var tx5 = s.Commands.Begin("View");
            tx5.Set(Kit.GoldenChara, "ui.expanded", false);
            tx5.Set(Kit.GoldenChara, "chara.face.mouth", 1);
            tx5.Cancel();
            t.Check(!s.Model.Find(Kit.GoldenChara).Doc.Ui.Expanded, "view-state set survives the cancel (never part of it)");
            // a barrier keeps the later set
            var tx6 = s.Commands.Begin("Barrier");
            tx6.Set(Kit.GoldenChara, "chara.face.eyes", 3);
            tx6.Set(Kit.GoldenChara, "chara.face", new CharaFaceState { Eyes = 4 });
            tx6.Set(Kit.GoldenChara, "chara.face.eyes", 6);
            t.Equal(3, tx6.StepCount, "an overlapping path is a collapse barrier");
            Kit.Ok(t, tx6.Commit(), "commit with a barrier");
            Kit.Ok(t, s.Commands.Undo(), "undo the barrier transaction");
            Kit.Ok(t, s.Commands.Redo(), "redo the barrier transaction");
            t.Check(((CharaNodeState)s.Model.Find(Kit.GoldenChara).State).Face.Eyes == 6, "redo replays the steps in order");
            Kit.Valid(t, s, "transactions");
        }

        static void Merging(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            var k = new ExecuteOptions { MergeKey = "eyes" };
            s.Set(Kit.GoldenChara, "chara.face.eyes", 1, k);
            s.Set(Kit.GoldenChara, "chara.face.eyes", 2, k);
            s.Set(Kit.GoldenChara, "chara.face.eyes", 3, k);
            t.Equal(1, s.Commands.UndoList.Count, "equal merge keys merge");
            s.Set(Kit.GoldenChara, "chara.face.eyes", 4, new ExecuteOptions { MergeKey = "other" });
            t.Equal(2, s.Commands.UndoList.Count, "another key does not merge");
            s.Set(Kit.GoldenChara, "chara.face.eyes", 5);
            s.Set(Kit.GoldenChara, "chara.face.eyes", 6);
            t.Equal(4, s.Commands.UndoList.Count, "no key, no merge");
            s.Set(Kit.GoldenChara, "chara.face.mouth", 1, k);
            s.Set(Kit.GoldenChara, "chara.face.eyes", 7, k);
            t.Equal(6, s.Commands.UndoList.Count, "same key on another path does not merge");
            s.Commands.Undo();
            s.Set(Kit.GoldenChara, "chara.face.eyes", 8, k);
            t.Equal(6, s.Commands.UndoList.Count, "undo seals the entry (no merge after it)");
            s.Commands.MarkSaved();
            s.Set(Kit.GoldenChara, "chara.face.eyes", 9, k);
            t.Equal(7, s.Commands.UndoList.Count, "save seals the entry");
            var golden = Kit.GoldenSession(t);
            var before = ((CharaNodeState)golden.Model.Find(Kit.GoldenChara).State).Face.Eyes;
            golden.Set(Kit.GoldenChara, "chara.face.eyes", 11, k);
            golden.Set(Kit.GoldenChara, "chara.face.eyes", 12, k);
            golden.Commands.Undo();
            t.Equal(before, ((CharaNodeState)golden.Model.Find(Kit.GoldenChara).State).Face.Eyes, "undo of a merged entry restores the first old value");
        }

        static void Limits(TestRunner t)
        {
            var s = Kit.Session();
            string f = s.AddFolder().NewId;
            s.Commands.ClearHistory();
            for (int i = 1; i <= 201; i++) s.Set(f, "name", "n" + i);
            t.Equal(200, s.Commands.UndoList.Count, "201st entry evicts the first");
            while (s.Commands.CanUndo) s.Commands.Undo();
            t.Equal("n1", s.Model.Find(f).Doc.Name, "the oldest entry left is the second set");
            // byte cap
            var s2 = Kit.Session(2, new SessionOptions { History = new HistoryLimits { MaxBytes = 300_000 } });
            string g = s2.AddFolder().NewId;
            for (int i = 0; i < 10; i++) s2.Set(g, "name", new string((char)('a' + i), 50_000));
            long total = 0;
            foreach (var e in s2.Commands.UndoList) total += e.EstimatedBytes;
            t.Check(total <= 300_000 && s2.Commands.UndoList.Count < 11, "byte cap keeps the history below the limit (" + total + " B, " + s2.Commands.UndoList.Count + " entries)");
            var s3 = Kit.Session(3);
            string h = s3.AddFolder().NewId;
            for (int i = 0; i < 10; i++) s3.Set(h, "name", "x" + i);
            s3.Commands.Limits = new HistoryLimits { MaxEntries = 2 };
            t.Equal(2, s3.Commands.UndoList.Count, "setting smaller limits evicts at once");
            Kit.Valid(t, s2, "limits");
        }

        static void Forgotten(TestRunner t)
        {
            var s = Kit.Session();
            var rec = new EventRecorder(s);
            string a = s.AddFolder().NewId;
            string b = s.AddFolder().NewId;
            string child = s.AddItem(new ItemNodeState { Ref = "test.mod#1" }, b).NewId;
            s.Delete(new[] { b });
            t.Equal(0, rec.Forgotten.Count, "a delete holds its nodes");
            s.Commands.Undo();   // delete undone
            s.Commands.Undo();   // add item undone: the item is held by the redo entry
            rec.Clear();
            s.Set(a, "name", "branch");   // disposes the redo branch (add item, delete)
            t.Check(rec.Forgotten.Count == 1 && rec.Forgotten[0] == child, "redo branch disposal forgets exactly the held-out node (" + string.Join(",", rec.Forgotten) + ")");
            // eviction of an add entry while a later delete holds the node: not forgotten
            var s2 = Kit.Session(2, new SessionOptions { History = new HistoryLimits { MaxEntries = 2 } });
            var rec2 = new EventRecorder(s2);
            string x = s2.AddFolder().NewId;
            s2.Delete(new[] { x });
            s2.AddFolder();
            t.Equal(0, rec2.Forgotten.Count, "evicting the add entry keeps the node held by the delete");
            s2.AddFolder();
            t.Check(rec2.Forgotten.Count == 1 && rec2.Forgotten[0] == x, "evicting the delete forgets the node");
            // unrecorded delete: forgotten at once, history cleared
            var s3 = Kit.Session(3);
            var rec3 = new EventRecorder(s3);
            string y = s3.AddFolder().NewId;
            s3.Delete(new[] { y }, ExecuteOptions.NotRecorded);
            t.Check(rec3.Forgotten.Count == 1 && rec3.Forgotten[0] == y && !s3.Commands.CanUndo, "unrecorded delete: forgotten, history cleared");
            // ClearHistory
            string z = s3.AddFolder().NewId;
            s3.Delete(new[] { z });
            rec3.Clear();
            s3.Commands.ClearHistory();
            t.Check(rec3.Forgotten.Count == 1 && rec3.Forgotten[0] == z, "ClearHistory forgets nodes held only by history");
        }

        static void ClearOnLoad(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            s.Set(Kit.GoldenChara, "chara.face.eyes", 3);
            t.Check(s.Commands.CanUndo, "history before New");
            s.NewScene();
            t.Check(!s.Commands.CanUndo && !s.Commands.CanRedo && s.Model.Count == 0 && !s.Commands.IsDirty, "NewScene clears model and history");
            s.AddFolder();
            s.Bind(Kit.GoldenContainer(t, false));
            t.Check(!s.Commands.CanUndo && s.Model.Count == 8 && !s.Commands.IsDirty, "Bind clears history");
            s.Set(Kit.GoldenChara, "chara.face.eyes", 3);
            s.Commands.Undo();
            t.Check(s.Commands.CanRedo, "redo available");
            s.Bind(Kit.GoldenContainer(t, false));
            t.Check(!s.Commands.CanRedo, "Bind clears redo too");
        }

        static void ViewState(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            long v = s.Commands.Version;
            Kit.Ok(t, s.Set(Kit.GoldenChara, "ui.expanded", false), "ui.expanded");
            Kit.Ok(t, s.Set(Kit.GoldenChara, "ui.expandedGroups", new List<int> { 1 }), "ui.expandedGroups");
            Kit.Ok(t, s.Set(null, "camera.current", new CameraView { Distance = 12, Fov = 30 }), "camera.current");
            Kit.Ok(t, s.Set(null, "camera.current.distance", 15f), "camera.current.distance");
            Kit.Ok(t, s.Commands.Execute(new ListInsertCommand(Kit.GoldenChara, "ui.expandedPoints", 0, "N_Head")), "ui list insert");
            t.Equal(0, s.Commands.UndoList.Count, "view state leaves history unchanged");
            t.Equal(v + 5, s.Commands.Version, "view state bumps the version");
            t.Check(s.Commands.IsDirty, "view state makes the scene dirty");
            s.Set(null, "camera.slots[0]", new CameraView { Distance = 3, Fov = 20 });
            t.Equal(1, s.Commands.UndoList.Count, "camera slots are recorded");
        }

        static void SelectionContext(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            s.Selection.Set(new[] { Kit.GoldenItem, Kit.GoldenFolder });
            Kit.Ok(t, s.Delete(new[] { Kit.GoldenItem }), "delete a selected node");
            t.Check(s.Selection.Count == 1 && s.Selection.Primary == Kit.GoldenFolder, "removed ids leave the selection");
            s.Commands.Undo();
            t.Check(s.Selection.Count == 2 && s.Selection.Primary == Kit.GoldenItem, "undo restores the selection before");
            s.Commands.Redo();
            t.Check(s.Selection.Count == 1 && s.Selection.Primary == Kit.GoldenFolder, "redo restores the selection after");
            var r = s.AddFolder();
            t.Check(s.Selection.Count == 1 && s.Selection.Primary == r.NewId, "a user add selects the new node");
            var rb = s.AddFolder(o: ExecuteOptions.From(CommandSource.Bridge));
            t.Check(s.Selection.Primary == r.NewId && rb.Ok, "a bridge add leaves the selection");
            s.Commands.Undo();
            s.Commands.Undo();
            t.Check(s.Selection.Count == 1 && s.Selection.Primary == Kit.GoldenFolder, "undo of the adds restores the selection");
            t.Check(!s.Selection.Add("ffffffffffffffffffffffffffffffff"), "ids outside the model are not selectable");
            Kit.Valid(t, s, "selection");
        }

        static void Atomicity(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            var rec = new EventRecorder(s);
            var before = Kit.Bytes(s);
            int entries = s.Commands.UndoList.Count;
            var comp = new CompositeCommand("Failing", new ISceneCommand[]
            {
                new SetPropertyCommand(Kit.GoldenChara, "chara.face.eyes", 9),
                new RemoveNodesCommand(new[] { Kit.GoldenCamera }),
                new SetPropertyCommand(Kit.GoldenChara, "chara.face.eyes", float.NaN),
                new SetPropertyCommand(Kit.GoldenChara, "chara.face.mouth", 3),
            });
            Kit.Fails(t, s.Commands.Execute(comp), CommandCodes.BadArgs, "a composite whose third child fails");
            t.Check(Kit.Same(before, Kit.Bytes(s)), "failure leaves the bytes unchanged");
            t.Equal(entries, s.Commands.UndoList.Count, "failure leaves history unchanged");
            t.Equal(0, rec.Log.Count, "failure delivers no events");
            t.Check(s.Model.Find(Kit.GoldenCamera) != null, "the removed node is back");
            Kit.Valid(t, s, "atomicity");
        }
    }
}

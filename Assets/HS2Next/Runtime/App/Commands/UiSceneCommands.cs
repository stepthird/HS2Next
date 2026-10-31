// Phase 3.5 package B: the bridge side of the operations layer.
// CommandKit.Check(CoreContext, OpResult) turns a StudioOps result back into the bridge error the handlers threw before the
// extraction (same code, message and detail: arg, expected, candidates, extra) after forwarding the op's warnings;
// CommandKit.Ops(c) is the app's StudioOps. The commands (CoreCommands.Phase35Names, owner core, Since 0.2.0):
//   select / selection      the session's node selection and the sub-selection (view state, not recorded)
//   chara.clothes           clothes states with HS2's cycling and couplings computed in the op (one entry "Clothes")
//   chara.clothesDump       number-only dump of the clothes realization (object activity, mask values; HS2 bridge shape, K12)
//   chara.accessories       accessory visibility by id or build slot, or all (one entry "Accessories")
//   chara.state             liquid, nipple, wet, gloss, son, silhouette, tears, cheek (one entry "Character state")
//   chara.change            replaces the appearance of characters (one entry "Change character")
//   scene.import            merges the nodes of another scene file (one entry "Import scene")
//   obj.rename / obj.reorder  name and sibling index (one entry each)
using System;
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;

namespace HS2Next.App.Commands
{
    public static partial class CommandKit
    {
        /// <summary>The operations layer of the handler's app (StudioOps.For(c.Runtime)).</summary>
        public static StudioOps Ops(CoreContext c) => StudioOps.For(c.Runtime);

        /// <summary>
        /// Forwards the warnings of <paramref name="r"/> to the reply, then throws its error as a bridge error (Ok and NoOp return).
        /// <paramref name="arg"/> is used when the op named no argument.
        /// </summary>
        public static OpResult Check(CoreContext c, OpResult r, string arg = null)
        {
            if (r == null) throw new BridgeException(BridgeCodes.Internal, "no result");
            foreach (var w in r.Warnings) c.Warn(w);
            if (r.Status != CommandStatus.Error) return r;
            string a = r.Arg ?? arg;
            BridgeErrorDetail d = null;
            if (a != null || r.Expected != null || (r.Candidates != null && r.Candidates.Count > 0) || (r.Extra != null && r.Extra.Count > 0))
                d = new BridgeErrorDetail { Arg = a, Expected = r.Expected, Candidates = r.Candidates, Extra = r.Extra };
            throw new BridgeException(r.Code ?? BridgeCodes.Internal, r.Message, d);
        }
    }

    public static class UiSceneCommands
    {
        /// <summary>The commands of this file (CoreCommands.Phase35Names).</summary>
        public static readonly IReadOnlyList<string> Names = new[]
        {
            "select", "selection", "chara.clothes", "chara.clothesDump", "chara.accessories", "chara.state", "chara.change", "scene.import",
            "obj.rename", "obj.reorder",
        };

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("select", "Sets the node selection (view state, not recorded): keys replace it (empty clears), add appends; sub selects one sub-object (FK bone, IK target, look target) and its character alone.", "ui scene")
                .Arg("key", ArgTypes.Node, "one node")
                .Arg("keys", ArgTypes.ArrayOf(ArgTypes.Node), "the nodes (empty = clear)")
                .Arg("add", ArgTypes.Bool, "append to the selection instead of replacing it", false, false)
                .Arg("sub", ArgTypes.String, "\"<id>/fk/<bone>\", \"<id>/ik/<Target>\" or \"<id>/look\"; null clears the sub-selection")
                .Return("selected", ArgTypes.ArrayOf(ArgTypes.String), "ids in order (primary first)").Return("primary", ArgTypes.String, "")
                .Return("sub", ArgTypes.String, "the sub-selection or null")
                .Example("{\"cmd\":\"select\",\"keys\":[\"<id>\"]}")
                .WithFlags(CommandFlags.Mutates), Select);
            CommandKit.Register(r, app, CommandKit.Spec("selection", "The node selection and the sub-selection.", "ui scene")
                .Return("selected", ArgTypes.ArrayOf(ArgTypes.String), "").Return("primary", ArgTypes.String, "").Return("sub", ArgTypes.String, "")
                .WithFlags(CommandFlags.ReadOnly), Selection);
            CommandKit.Register(r, app, CommandKit.Spec("chara.clothes", "Clothes states of characters with HS2's cycling and couplings (the stored values are the coupled ones; one undo entry); without all / states only reads.", "ui scene")
                .Arg("key", ArgTypes.Node, "a character").Arg("keys", ArgTypes.ArrayOf(ArgTypes.Node), "several characters (one entry)")
                .Arg("all", ArgTypes.Int, "0 on, 1 half, 2 off for the eight kinds in order (HS2's All)")
                .Arg("states", ArgTypes.Object, "{kind: state}: kind = top, bottom, bra, shorts, gloves, pantyhose, socks, shoes or 0-7; state 0–2")
                .Arg("next", ArgTypes.Bool, "cycling direction for an unsupported state (true = +1)", false, true)
                .Return("states", ArgTypes.ArrayOf(ArgTypes.Int), "the 8 stored states (first key)")
                .Return("supported", ArgTypes.ArrayOf(ArgTypes.Any), "8 x the supported states ([] = not a state kind; all when not built)")
                .Return("realized", ArgTypes.ArrayOf(ArgTypes.Int), "the 8 states as shown (null when not built)")
                .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "kind names that changed (any key)")
                .Return("keys", ArgTypes.Object, "with keys: {<id>: {states, supported, realized}}", false)
                .Example("{\"cmd\":\"chara.clothes\",\"key\":\"<id>\",\"states\":{\"top\":1}}")
                .WithFlags(CommandFlags.Mutates), Clothes);
            CommandKit.Register(r, app, CommandKit.Spec("chara.clothesDump", "Number-only dump of a built character's clothes realization: states, supported states, one-piece flags, part and def / half object activity, mask values (HS2 Studio Bridge shape).", "ui scene")
                .Arg("key", ArgTypes.Node, "a built character", true)
                .Return("states", ArgTypes.ArrayOf(ArgTypes.Int), "").Return("supported", ArgTypes.ArrayOf(ArgTypes.Any), "")
                .Return("flags", ArgTypes.Object, "{notBot, notInnerT, notInnerB}")
                .Return("kinds", ArgTypes.ArrayOf(ArgTypes.Object), "{kind, id, present, active, topDef, topHalf, botDef, botHalf, opt1, opt2}")
                .Return("masks", ArgTypes.Object, "{body {_alpha_a, _alpha_b, _alpha_d}, bra {_alpha_a, _alpha_b}, innerTB, innerB, panst {_alpha_d}}")
                .WithFlags(CommandFlags.ReadOnly | CommandFlags.Experimental), ClothesDump);
            CommandKit.Register(r, app, CommandKit.Spec("chara.accessories", "Accessory visibility of characters by accessory id or build slot (the slot the Studio State tab and the Accessories page name, 0-based), or all (one undo entry); without show / all only reads.", "ui scene")
                .Arg("key", ArgTypes.Node, "a character").Arg("keys", ArgTypes.ArrayOf(ArgTypes.Node), "several characters (one entry)")
                .Arg("all", ArgTypes.Bool, "every worn accessory")
                .Arg("show", ArgTypes.Object, "{<accessory id> or <build slot number (0-based; the slot table of the first key)>: bool}")
                .Return("accessories", ArgTypes.Object, "{<id>: bool} (first key)").Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "accessory ids that changed")
                .WithFlags(CommandFlags.Mutates), Accessories);
            CommandKit.Register(r, app, CommandKit.Spec("chara.state", "Other state members of characters (liquid, nipple, wet, gloss, son, silhouette; tears, cheek of the face), stored as given (one undo entry).", "ui scene")
                .Arg("key", ArgTypes.Node, "a character").Arg("keys", ArgTypes.ArrayOf(ArgTypes.Node), "several characters (one entry)")
                .Arg("set", ArgTypes.Object, "{liquid: [5 levels 0-2] | \"liquid[i]\": level, nipple, wet, gloss, son: {visible, length} | \"son.length\", silhouette: {enabled, color} | \"silhouette.color\": [r,g,b,a], tears, cheek}", true)
                .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "members that changed (as given)").Return("noop", ArgTypes.ArrayOf(ArgTypes.String), "members that already had the value")
                .Example("{\"cmd\":\"chara.state\",\"key\":\"<id>\",\"set\":{\"wet\":0.5,\"gloss\":0.3}}")
                .WithFlags(CommandFlags.Mutates), State);
            CommandKit.Register(r, app, CommandKit.Spec("chara.change", "Replaces the appearance of characters with a card (imported) or a .hs2nchara file; the nodes keep id, transform, children, animation, FK / IK, face, look, state and name (one undo entry).", "ui scene")
                .Arg("key", ArgTypes.Node, "a character").Arg("keys", ArgTypes.ArrayOf(ArgTypes.Node), "several characters")
                .Arg("path", ArgTypes.Path, "a card or .hs2nchara: absolute, a relative .hs2nchara under <UserData>/chara, a relative card under <hs2Root>/UserData/chara/female or /male (sex)", true)
                .Arg("sex", ArgTypes.Any, "0 / \"male\" or 1 / \"female\": the HS2 card folder of a relative card path", false, "female")
                .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "the node ids").Return("findings", ArgTypes.ArrayOf(ArgTypes.Object), "the import's warnings and errors")
                .WithFlags(CommandFlags.Mutates | CommandFlags.Slow).WithTimeout(300), Change);
            CommandKit.Register(r, app, CommandKit.Spec("scene.import", "Adds the nodes of another scene file at the root with new ids (its scene settings are ignored; one undo entry); the imported top nodes are selected.", "ui scene")
                .Arg("path", ArgTypes.Path, "the .hs2nscene: absolute or relative under <UserData>/scene", true)
                .Return("added", ArgTypes.ArrayOf(ArgTypes.String), "the new top node ids").Return("nodes", ArgTypes.Int, "nodes added (with descendants)")
                .Return("findings", ArgTypes.ArrayOf(ArgTypes.Object), "the file's load findings")
                .WithFlags(CommandFlags.Mutates | CommandFlags.Slow).WithTimeout(300), Import);
            CommandKit.Register(r, app, CommandKit.Spec("obj.rename", "Renames a node (one undo entry \"Rename\"); null or \"\" = the default name of its kind.", "ui scene")
                .Arg("key", ArgTypes.Node, "the node", true).Arg("name", ArgTypes.String, "the new name (null = default)")
                .Return("key", ArgTypes.String, "").Return("name", ArgTypes.String, "the stored name (null = default)").Return("label", ArgTypes.String, "the shown name")
                .WithFlags(CommandFlags.Mutates), Rename);
            CommandKit.Register(r, app, CommandKit.Spec("obj.reorder", "Moves a node to an index among its siblings (one undo entry \"Reorder\").", "ui scene")
                .Arg("key", ArgTypes.Node, "the node", true).Arg("index", ArgTypes.Int, "the new sibling index (clamped by the model to the sibling count)", true)
                .Return("key", ArgTypes.String, "").Return("index", ArgTypes.Int, "the node's index after the move")
                .WithFlags(CommandFlags.Mutates), Reorder);
        }

        // ------------------------------------------------------------ helpers

        /// <summary>The character nodes of key / keys (one of them; wrong_kind for others); <paramref name="karg"/> names the one given.</summary>
        static List<TreeNode> CharaKeys(CoreContext c, out string karg)
        {
            var l = CommandKit.Keys(c, CommandKit.KeyKind.Chara, out bool many);
            karg = many ? "keys" : "key";
            return l;
        }

        static BridgeReply SelectionReply(CoreContext c)
        {
            var sel = c.Session.Selection;
            return new BridgeReply().With("selected", new List<string>(sel.Ids)).With("primary", sel.Primary).With("sub", CommandKit.Ops(c).SubSelection);
        }

        // ------------------------------------------------------------ select / selection

        static object Select(CoreContext c)
        {
            var ids = new List<string>();
            bool hasKey = c.Args.Has("key"), hasKeys = c.Args.Has("keys");
            if (hasKey && hasKeys) throw CommandKit.BadArgs("key", "one of key, keys", "give key or keys, not both");
            if (hasKey) ids.Add(CommandKit.Node(c, "key").Id);
            if (hasKeys) ids.AddRange(CommandKit.Ids(CommandKit.KeyList(c, "keys", CommandKit.KeyKind.Any, CommandKit.KeysRule.KeepDuplicates | CommandKit.KeysRule.AllowEmpty)));
            string sub = CommandKit.OptString(c, "sub");
            if (!hasKey && !hasKeys && sub == null && c.Args.Kind("sub") != ArgKind.Null) throw new BridgeException(BridgeError.Missing("keys", "one of key, keys, sub"));
            bool add = c.Args.Get("add", false);
            var ops = CommandKit.Ops(c);
            if (sub != null && (hasKey || hasKeys)) throw CommandKit.BadArgs("sub", "sub alone", "sub selects its character alone: give sub without key / keys");
            if (sub == null && !hasKey && !hasKeys) CommandKit.Check(c, ops.Select(c.Session.Selection.Ids, false, null));   // sub: null clears only the sub-selection
            else CommandKit.Check(c, ops.Select(ids, add, sub));
            return SelectionReply(c);
        }

        static object Selection(CoreContext c) => SelectionReply(c);

        // ------------------------------------------------------------ chara.clothes / chara.clothesDump

        static object Clothes(CoreContext c)
        {
            var nodes = CharaKeys(c, out string karg);
            bool hasAll = c.Args.Has("all"), hasStates = c.Args.Has("states");
            if (hasAll && hasStates) throw CommandKit.BadArgs("all", "one of all, states", "give all or states, not both");
            bool next = c.Args.Get("next", true);
            var ops = CommandKit.Ops(c);
            OpResult res = null;
            if (hasAll)
            {
                if (c.Args.Kind("all") != ArgKind.Integer) throw CommandKit.BadArgs("all", "0–2 (on, half, off)");
                res = CommandKit.Check(c, ops.SetClothesAll(CommandKit.Ids(nodes), c.Args.Get<int>("all"), CommandKit.Exec(c, StudioOps.Labels.Clothes)));
            }
            else if (hasStates)
            {
                if (c.Args.Kind("states") != ArgKind.Object) throw CommandKit.BadArgs("states", ArgTypes.Object);
                var st = c.Args.Nested("states");
                var req = new List<(int, int)>();
                foreach (var name in st.Names)
                {
                    int k = ClothesStateSet.KindOf(name);
                    if (k < 0) throw CommandKit.BadArgs("states." + name, "top, bottom, bra, shorts, gloves, pantyhose, socks, shoes or 0–7", "\"" + name + "\" is not a clothes kind");
                    if (st.Kind(name) != ArgKind.Integer) throw CommandKit.BadArgs("states." + name, "0–2 (on, half, off)");
                    req.Add((k, st.Get<int>(name)));
                }
                if (req.Count == 0) throw CommandKit.BadArgs("states", ArgTypes.Object, "states names no kind");
                res = CommandKit.Check(c, ops.SetClothesStates(CommandKit.Ids(nodes), req, next, CommandKit.Exec(c, StudioOps.Labels.Clothes)));
            }
            else c.Runtime.ApplyNow();
            BridgeReply One(TreeNode n)
            {
                var cur = c.Session.Model.Find(n.Id) ?? n;
                var stt = (cur.State as CharaNodeState)?.State;
                var rules = ops.ClothesStatesOf(n.Id);
                var sup = new List<object>();
                for (int k = 0; k < ClothesStateSet.KindCount; k++) sup.Add(rules.Known ? (object)new List<int>(rules.Supported(k)) : new List<int> { 0, 1, 2 });
                var real = c.Runtime.FindChara(n.Id);
                var rz = real != null && real.State == RealizationState.Built && real.StateRealizer != null ? new List<int>(real.StateRealizer.Realized) : null;
                return new BridgeReply().With("states", stt != null ? new List<int>(stt.Clothes) : null).With("supported", sup).With("realized", rz);
            }
            var r = One(nodes[0]);
            r["changed"] = res != null ? res.Changed : new List<string>();
            if (karg == "keys")
            {
                var keys = new BridgeReply();
                foreach (var n in nodes) keys[n.Id] = One(n);
                r["keys"] = keys;
            }
            return r;
        }

        static object ClothesDump(CoreContext c)
        {
            var real = CommandKit.Built(c);
            var n = c.Session.Model.Find(real.NodeId);
            var st = (n?.State as CharaNodeState)?.State;
            var rules = real.ClothesStates;
            var ctx = real.Context;
            var sup = new List<object>();
            for (int k = 0; k < ClothesStateSet.KindCount; k++) sup.Add(new List<int>(rules.Supported(k)));
            var kinds = new List<object>();
            for (int k = 0; k < ClothesStateSet.KindCount; k++)
            {
                var o = ctx?.Clothes != null && k < ctx.Clothes.Length ? ctx.Clothes[k] : null;
                var cmp = o ? o.GetComponent<AIChara.CmpClothes>() : null;
                var e = ctx?.ClothesEntries != null && k < ctx.ClothesEntries.Length ? ctx.ClothesEntries[k] : null;
                object Act(UnityEngine.GameObject g) => g ? (object)g.activeSelf : null;
                List<object> Opts(List<UnityEngine.GameObject> l)
                {
                    var r = new List<object>();
                    if (l != null) foreach (var g in l) r.Add(g ? (object)g.activeSelf : null);
                    return r;
                }
                kinds.Add(new BridgeReply().With("kind", ClothesStateSet.KindNames[k]).With("id", e != null ? (object)e.Key.Id : null)
                    .With("present", (bool)o).With("active", o ? (object)o.activeSelf : null)
                    .With("topDef", cmp != null ? Act(cmp.objTopDef) : null).With("topHalf", cmp != null ? Act(cmp.objTopHalf) : null)
                    .With("botDef", cmp != null ? Act(cmp.objBotDef) : null).With("botHalf", cmp != null ? Act(cmp.objBotHalf) : null)
                    .With("opt1", cmp != null ? Opts(cmp.objOpt01) : new List<object>()).With("opt2", cmp != null ? Opts(cmp.objOpt02) : new List<object>()));
            }
            var masks = new BridgeReply();
            var targets = real.Assembler?.ClothesMaskTargets();
            object Val(int t, string p)
            {
                if (targets == null || t >= targets.Count) return null;
                foreach (var m in targets[t].mats) if (m && m.HasProperty(p)) return (double)m.GetFloat(p);
                return null;
            }
            masks["body"] = new BridgeReply().With("_alpha_a", Val(0, "_alpha_a")).With("_alpha_b", Val(0, "_alpha_b")).With("_alpha_d", Val(0, "_alpha_d"));
            masks["bra"] = new BridgeReply().With("_alpha_a", Val(1, "_alpha_a")).With("_alpha_b", Val(1, "_alpha_b"));
            masks["innerTB"] = new BridgeReply().With("_alpha_d", Val(2, "_alpha_d"));
            masks["innerB"] = new BridgeReply().With("_alpha_d", Val(3, "_alpha_d"));
            masks["panst"] = new BridgeReply().With("_alpha_d", Val(4, "_alpha_d"));
            var sr = real.StateRealizer;
            return new BridgeReply()
                .With("states", st != null ? new List<int>(st.Clothes) : null)
                .With("supported", sup)
                .With("flags", new BridgeReply().With("notBot", rules.NotBot).With("notInnerT", rules.NotInnerT).With("notInnerB", rules.NotInnerB))
                .With("kinds", kinds)
                .With("masks", masks)
                .With("realized", sr != null ? new List<int>(sr.Realized) : null)
                .With("types", rules.ToString())
                .With("maskRenderersMissing", real.Assembler != null ? new List<string>(real.Assembler.MaskRenderersMissing) : new List<string>())
                .With("lastMs", sr != null ? Math.Round(sr.LastClothesMs, 4) : 0.0)
                .With("silhouette", sr != null && sr.SilhouetteOn);
        }

        // ------------------------------------------------------------ chara.accessories

        static object Accessories(CoreContext c)
        {
            var nodes = CharaKeys(c, out _);
            bool hasAll = c.Args.Has("all"), hasShow = c.Args.Has("show");
            if (hasAll && hasShow) throw CommandKit.BadArgs("all", "one of all, show", "give all or show, not both");
            var ops = CommandKit.Ops(c);
            OpResult res = null;
            if (hasAll)
            {
                if (c.Args.Kind("all") != ArgKind.Bool) throw CommandKit.BadArgs("all", ArgTypes.Bool);
                res = CommandKit.Check(c, ops.SetAccessoriesAll(CommandKit.Ids(nodes), c.Args.Get<bool>("all"), CommandKit.Exec(c, StudioOps.Labels.Accessories)));
            }
            else if (hasShow)
            {
                if (c.Args.Kind("show") != ArgKind.Object) throw CommandKit.BadArgs("show", ArgTypes.Object);
                var sh = c.Args.Nested("show");
                if (sh.Names.Count == 0) throw CommandKit.BadArgs("show", ArgTypes.Object, "show names no accessory");
                // a build slot number names the accessory of that slot (the slot table; per character: resolved on the first key)
                var map = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (var name in sh.Names)
                {
                    if (sh.Kind(name) != ArgKind.Bool) throw CommandKit.BadArgs("show." + name, ArgTypes.Bool);
                    string id = name;
                    if (int.TryParse(name, out int slot))
                    {
                        id = StudioOps.AccessoryIdOfSlot(nodes[0], slot);
                        if (id == null) throw CommandKit.NotFound("character " + nodes[0].Id + " has no accessory in build slot " + slot + " (0-based)", "show." + name);
                    }
                    map[id] = sh.Get<bool>(name);
                }
                res = CommandKit.Check(c, ops.SetAccessories(CommandKit.Ids(nodes), map, CommandKit.Exec(c, StudioOps.Labels.Accessories)));
            }
            else c.Runtime.ApplyNow();
            var cur = c.Session.Model.Find(nodes[0].Id) ?? nodes[0];
            var acc = new BridgeReply();
            var stt = (cur.State as CharaNodeState)?.State;
            if (stt != null) foreach (var kv in stt.Accessories) acc[kv.Key] = kv.Value;
            return new BridgeReply().With("accessories", acc).With("changed", res != null ? res.Changed : new List<string>());
        }

        // ------------------------------------------------------------ chara.state

        static object State(CoreContext c)
        {
            var nodes = CharaKeys(c, out _);
            if (c.Args.Kind("set") != ArgKind.Object) throw CommandKit.BadArgs("set", ArgTypes.Object);
            var set = c.Args.Nested("set");
            if (set == null || set.Names.Count == 0) throw CommandKit.BadArgs("set", ArgTypes.Object, "set names no member");
            var sets = new List<(string, object)>();
            var nameOf = new Dictionary<string, string>(StringComparer.Ordinal);
            var model = c.Session.Model;
            foreach (var name in set.Names)
            {
                string full = StudioOps.StatePath(name);
                if (full == null)
                    throw new BridgeException(BridgeCodes.BadArgs, "set." + name + ": not a settable state member (" + string.Join(", ", StudioOps.StateMembers) + ")",
                        new BridgeErrorDetail { Arg = "set." + name, Expected = "a character state member", Candidates = StudioOps.StateMembers });
                object v;
                try { v = FieldJson.FromText(set.RawJson(name), FieldAccessor.LeafType(model, nodes[0].Id, full)); }
                catch (CommandException e) { throw new BridgeException(e.Code == CommandCodes.NotFound ? BridgeCodes.BadArgs : e.Code, "set." + name + ": " + e.Message, new BridgeErrorDetail { Arg = "set." + name }); }
                catch (Exception e) { throw CommandKit.BadArgs("set." + name, "the save-format JSON form of " + full, e.Message); }
                sets.Add((name, v));
                nameOf[full] = name;
            }
            var res = CommandKit.Check(c, CommandKit.Ops(c).SetState(CommandKit.Ids(nodes), sets, CommandKit.Exec(c, StudioOps.Labels.CharacterState)));
            var changed = new List<string>();
            var noop = new List<string>();
            foreach (var p in res.Changed) changed.Add(nameOf.TryGetValue(p, out var nm) ? nm : p);
            foreach (var (name, _) in sets)
                if (!changed.Contains(name) && !noop.Contains(name)) noop.Add(name);
            return new BridgeReply().With("changed", changed).With("noop", noop);
        }

        // ------------------------------------------------------------ chara.change / scene.import

        static object Change(CoreContext c)
        {
            var nodes = CharaKeys(c, out _);
            string path = c.Args.Require<string>("path");
            int sex = CommandKit.Sex(c, "sex", CharaCommands.SexOf(nodes[0]) == 0 ? 0 : 1);
            bool hs2n = path.EndsWith(CharaCommands.CharaExtension, StringComparison.OrdinalIgnoreCase);
            string root = hs2n ? c.Paths.CharaDir : c.Paths.Hs2CharaDir(sex);
            string full = FileRoots.ForRead(c, "path", path, root, new[] { hs2n ? CharaCommands.CharaExtension : CharaCommands.CardExtension }, null);
            var res = CommandKit.Check(c, CommandKit.Ops(c).ChangeCharacter(CommandKit.Ids(nodes), full, null, CommandKit.Exec(c, StudioOps.Labels.ChangeCharacter)), "path");
            foreach (var n in nodes)
            {
                var r = c.Runtime.FindChara(n.Id);
                if (r == null || r.State != RealizationState.Built)
                    c.Warn("character " + n.Id + " changed but not built (" + (r?.State.ToString() ?? "NotRealized") + (r?.Detail != null ? ": " + r.Detail : "") + ")");
            }
            return new BridgeReply().With("changed", CommandKit.Ids(nodes)).With("findings", CommandKit.Findings(res.Value as FindingList, infos: false));
        }

        static object Import(CoreContext c)
        {
            string text = c.Args.Require<string>("path");
            string full = FileRoots.ForRead(c, "path", text, c.Paths.SceneDir, new[] { SceneCommands.SceneExt }, SceneCommands.SceneExt);
            int before = c.Session.Model.Count;
            var res = CommandKit.Check(c, CommandKit.Ops(c).ImportScene(full, CommandKit.Exec(c, StudioOps.Labels.ImportScene)), "path");
            return new BridgeReply().With("added", new List<string>(res.NewIds)).With("nodes", c.Session.Model.Count - before)
                .With("findings", CommandKit.Findings(res.Value as FindingList, infos: false));
        }

        // ------------------------------------------------------------ obj.rename / obj.reorder

        static object Rename(CoreContext c)
        {
            var n = CommandKit.Node(c, "key");
            var k = c.Args.Kind("name");
            if (k == ArgKind.Missing) throw new BridgeException(BridgeError.Missing("name", ArgTypes.String));
            if (k != ArgKind.Null && k != ArgKind.String) throw CommandKit.BadArgs("name", ArgTypes.String);
            CommandKit.Check(c, CommandKit.Ops(c).Rename(n.Id, k == ArgKind.Null ? null : c.Args.Get<string>("name"), CommandKit.Exec(c, StudioOps.Labels.Rename)));
            var cur = c.Session.Model.Find(n.Id) ?? n;
            return new BridgeReply().With("key", cur.Id).With("name", cur.Doc.Name).With("label", cur.Label);
        }

        static object Reorder(CoreContext c)
        {
            var n = CommandKit.Node(c, "key");
            if (c.Args.Kind("index") != ArgKind.Integer) throw CommandKit.BadArgs("index", ArgTypes.Int);
            int index = c.Args.Get<int>("index");
            if (index < 0) throw CommandKit.BadArgs("index", "an index ≥ 0");
            CommandKit.Check(c, CommandKit.Ops(c).Reorder(n.Id, index, CommandKit.Exec(c, StudioOps.Labels.Reorder)));
            var cur = c.Session.Model.Find(n.Id) ?? n;
            return new BridgeReply().With("key", cur.Id).With("index", cur.Index);
        }
    }
}

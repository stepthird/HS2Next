// The commands of Chara to Item (group "bake", owner core, Since 1.3.0, experimental): bakes of characters into items, the store of bakes, what a bake item holds.
//   bake.parts, bake.make, bake.list, bake.rename, bake.delete, bake.purge, bake.keep, bake.status, bake.import
// `key` names a node; `id` stays the envelope's: a bake's identity is `bake`, a store row is `no`. What changes the scene goes through StudioOps (one history entry each,
// `undo: false` and a client's batch as every command); the store is not the scene and its changes make no entry. A node of the wrong kind answers bad_args; a capture
// without a graphics device, nothing visible (bake.empty) and a busy store (bake.busy) answer unavailable; over a limit bad_args with the limit's name in the text.
using System;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Api.Bridge;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Bake;
using HS2Next.Scenes.Runtime;

namespace HS2Next.App.Commands
{
    public static class BakeCommands
    {
        /// <summary>The commands of this file, in the order of the spec's table.</summary>
        public static readonly string[] Names =
        {
            "bake.parts", "bake.make", "bake.list", "bake.rename", "bake.delete", "bake.purge", "bake.keep", "bake.status", "bake.import",
        };

        /// <summary>CommandSpec.Since of these commands.</summary>
        public const string Since68 = "1.3.0";
        /// <summary>Their help group.</summary>
        public const string GroupName = "bake";

        const string NoDoc = "a store row's number";
        const string BakeDoc = "a bake's identity (32 hex digits), in place of no";

        static CommandSpec Spec(string name, string summary, CommandFlags flags) =>
            CommandKit.Spec(name, summary, GroupName).WithSince(Since68).WithFlags(flags | CommandFlags.Experimental);

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, Spec("bake.parts", "The parts a character has that a bake can take: body, head, hair0-hair3, clothes0-clothes7, acc<slot>, other.", CommandFlags.ReadOnly)
                    .Arg("key", ArgTypes.Node, "a built character", true)
                    .Return("parts", ArgTypes.ArrayOf(ArgTypes.Object), "{part, label, item, renderers}: item the name of the part's object, null for other; renderers how many would be baked")
                    .Example("{\"cmd\":\"bake.parts\",\"key\":\"<id>\"}"),
                Parts);
            CommandKit.Register(r, app, Spec("bake.make", "Bakes characters as drawn into items with FK, in one history entry: the whole character, the chosen parts as one item, or one item per part. Each bake goes into the scene and, with store, into the store of bakes; the characters are hidden, not deleted. Needs a graphics device.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, "a character")
                    .Arg("keys", ArgTypes.ArrayOf(ArgTypes.Node), "several characters, in place of key")
                    .Arg("parts", ArgTypes.ArrayOf(ArgTypes.String), "part keys of bake.parts; empty: the whole character. A part a character lacks is skipped", false, new string[0])
                    .Arg("split", ArgTypes.Bool, "one item per part", false, false)
                    .Arg("name", ArgTypes.String, "the name; empty: the character, the parts and the time. One item per part adds the part, several characters add the character", false, "")
                    .Arg("fk", ArgTypes.ArrayOf(ArgTypes.String), "the FK groups whose bones the item takes: Hair, Neck, Breast, Body, RightHand, LeftHand, Skirt (ignoring case)", false, StudioOps.BakeFkGroupNames)
                    .Arg("insides", ArgTypes.Bool, "a part without the body or the head gets inside faces", false, true)
                    .Arg("centre", ArgTypes.Bool, "a part bake's origin at the centre of its surface", false, true)
                    .Arg("hide", ArgTypes.Bool, "hide the characters", false, true)
                    .Arg("store", ArgTypes.Bool, "also keep the bakes in the store; false: in the scene only", false, true)
                    .Return("items", ArgTypes.ArrayOf(ArgTypes.String), "the new items' keys")
                    .Return("bakes", ArgTypes.ArrayOf(ArgTypes.Object), "{bake, no, main, bytes}: no the store row or null, main the main blob's resource, bytes of every blob")
                    .Return("ms", ArgTypes.Float, "milliseconds the bake took")
                    .Example("{\"cmd\":\"bake.make\",\"key\":\"<id>\",\"parts\":[\"clothes0\",\"clothes7\"],\"split\":true}"),
                Make);
            CommandKit.Register(r, app, Spec("bake.list", "The store's bakes.", CommandFlags.ReadOnly)
                    .Arg("q", ArgTypes.String, "text the name or the number holds (ignoring case); empty: every row", false, "")
                    .Arg("deleted", ArgTypes.Bool, "also the deleted rows", false, false)
                    .Return("rows", ArgTypes.ArrayOf(ArgTypes.Object), "{no, bake, name, created, kind, parts, fk, bytes, deleted}: created in UTC, parts the parts' keys or empty, fk how many FK bones")
                    .Return("lastNumber", ArgTypes.Int, "the last number given; numbers are never given again")
                    .Example("{\"cmd\":\"bake.list\",\"q\":\"shoes\"}"),
                List);
            CommandKit.Register(r, app, Spec("bake.rename", "Renames a bake in the store; the scene's items of that bake that still carry the old name take the new one in one history entry. Undo renames the items back; the store keeps the new name.", CommandFlags.Mutates)
                    .Arg("no", ArgTypes.Int, NoDoc)
                    .Arg("bake", ArgTypes.String, BakeDoc)
                    .Arg("name", ArgTypes.String, "the new name", true)
                    .Return("name", ArgTypes.String, "the new name")
                    .Return("items", ArgTypes.ArrayOf(ArgTypes.String), "the items renamed")
                    .Example("{\"cmd\":\"bake.rename\",\"no\":73100074,\"name\":\"Shoes\"}"),
                Rename);
            CommandKit.Register(r, app, Spec("bake.delete", "Hides a bake from the store's list, or with restore shows it again; no file is touched. No history.", CommandFlags.Mutates)
                    .Arg("no", ArgTypes.Int, NoDoc)
                    .Arg("bake", ArgTypes.String, BakeDoc)
                    .Arg("restore", ArgTypes.Bool, "show a deleted bake again", false, false)
                    .Return("deleted", ArgTypes.Bool, "the row is deleted now")
                    .Example("{\"cmd\":\"bake.delete\",\"no\":73100074}"),
                Delete);
            CommandKit.Register(r, app, Spec("bake.purge", "Removes the deleted bakes from the store and every stored blob no remaining bake lists; what a scene holds is never touched. Refused while a bake is being written or imported. No history.", CommandFlags.Mutates)
                    .Return("rows", ArgTypes.Int, "rows removed")
                    .Return("blobs", ArgTypes.Int, "blob files removed")
                    .Return("bytes", ArgTypes.Int, "their bytes")
                    .Example("{\"cmd\":\"bake.purge\"}"),
                Purge);
            CommandKit.Register(r, app, Spec("bake.keep", "Keeps a bake item's bake in the store, as a new row named as the item; a bake the store has already: nothing new. No history.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, "an item drawn from a bake", true)
                    .Return("kept", ArgTypes.Bool, "a new row was made")
                    .Return("no", ArgTypes.Int, "the row")
                    .Example("{\"cmd\":\"bake.keep\",\"key\":\"<id>\"}"),
                Keep);
            CommandKit.Register(r, app, Spec("bake.status", "What an item drawn from a bake holds.", CommandFlags.ReadOnly)
                    .Arg("key", ArgTypes.Node, "an item drawn from a bake", true)
                    .Return("bake", ArgTypes.String, "the bake's identity")
                    .Return("main", ArgTypes.String, "the main blob's resource")
                    .Return("data", ArgTypes.ArrayOf(ArgTypes.String), "the mesh and texture blobs' resources")
                    .Return("bytes", ArgTypes.Int, "bytes of those blobs in the scene")
                    .Return("nodes", ArgTypes.Int, "nodes; null when the main cannot be read", false)
                    .Return("renderers", ArgTypes.Int, "renderers", false)
                    .Return("meshes", ArgTypes.Int, "meshes", false)
                    .Return("textures", ArgTypes.Int, "textures", false)
                    .Return("materials", ArgTypes.Int, "materials", false)
                    .Return("fk", ArgTypes.ArrayOf(ArgTypes.String), "the item's FK bones")
                    .Return("correction", ArgTypes.ArrayOf(ArgTypes.Bool), "the eight joint-correction categories, or null")
                    .Return("members", ArgTypes.ArrayOf(ArgTypes.String), "the renderers drawn with the mosaic, by path under the item")
                    .Return("parts", ArgTypes.ArrayOf(ArgTypes.String), "the parts baked, or null for everything drawn")
                    .Return("kind", ArgTypes.String, "chara, parts, item, piece or imported")
                    .Return("source", ArgTypes.Object, "{kind, name, ...}: what was baked")
                    .Return("no", ArgTypes.Int, "the store row it was added from, or null")
                    .Return("piece", ArgTypes.Object, "{steps, source}: for a piece the steps of its cut and its source bake; null otherwise")
                    .Example("{\"cmd\":\"bake.status\",\"key\":\"<id>\"}"),
                Status);
            CommandKit.Register(r, app, Spec("bake.import", "Imports the old bakes of a folder, read in place and never written, as new bakes in the store under their old numbers; a bake whose number the store holds is skipped, so a second run adds nothing. Needs a graphics device. No history.", CommandFlags.Mutates)
                    .Arg("path", ArgTypes.Path, "the folder of the old bakes, absolute or under the HS2 install's UserData; absent: UserData/CharaToItem there")
                    .Return("imported", ArgTypes.ArrayOf(ArgTypes.Object), "{no, name, renderers, textures, findings}: findings as {severity, code, path, message}")
                    .Return("skipped", ArgTypes.ArrayOf(ArgTypes.Object), "{no, file, code}: the old bakes not imported and why")
                    .Example("{\"cmd\":\"bake.import\"}"),
                Import);
        }

        // ------------------------------------------------------------ arguments

        // a store row by no or bake
        static (int? no, string bake) RowOf(CoreContext c)
        {
            int? no = CommandKit.OptInt(c, "no");
            string bake = CommandKit.OptString(c, "bake");
            if (no != null && bake != null) throw CommandKit.BadArgs("no", "no or bake", "no and bake cannot stand together");
            if (no == null && bake == null) throw new BridgeException(BridgeError.Missing("no", ArgTypes.Int));
            if (bake != null && !JsonText.IsId(bake)) throw CommandKit.BadArgs("bake", "32 hex digits", "\"" + bake + "\" is not a bake's identity");
            return (no, bake);
        }

        static bool[] FkGroups(CoreContext c)
        {
            var names = CommandKit.OptStrings(c, "fk");
            if (names == null) return null;
            var on = new bool[7];
            foreach (var s in names)
            {
                int i = Array.FindIndex(StudioOps.BakeFkGroupNames, g => string.Equals(g, s?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (i < 0) throw new BridgeException(BridgeCodes.BadArgs, "unknown FK group \"" + s + "\"", new BridgeErrorDetail { Arg = "fk", Expected = "FK group names", Candidates = StudioOps.BakeFkGroupNames });
                on[i] = true;
            }
            return on;
        }

        static string Created(DateTime d) => d.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        internal static object SourceRow(BakeMainSource s)
        {
            if (s == null) return null;
            var o = new BridgeReply { ["kind"] = s.Kind, ["name"] = s.Name };
            if (s.Sex != null) o["sex"] = s.Sex.Value;
            if (s.Ref != null) o["ref"] = s.Ref;
            if (s.Group != null) o["group"] = s.Group.Value;
            if (s.Category != null) o["category"] = s.Category.Value;
            return o;
        }

        // ------------------------------------------------------------ handlers

        static object Parts(CoreContext c)
        {
            var n = CommandKit.Node(c, "key");
            var r = CommandKit.Check(c, CommandKit.Ops(c).BakeParts(n.Id));
            var list = new List<object>();
            foreach (var p in (List<BakePartRow>)r.Value)
                list.Add(new BridgeReply { ["part"] = p.Part, ["label"] = p.Label, ["item"] = p.Item, ["renderers"] = p.Renderers });
            return new BridgeReply().With("parts", list);
        }

        static object Make(CoreContext c)
        {
            var nodes = CommandKit.Keys(c, CommandKit.KeyKind.Any, out _);
            var opt = new BakeMakeOptions
            {
                Parts = CommandKit.OptStrings(c, "parts"),
                Split = CommandKit.OptBool(c, "split") ?? false,
                Name = CommandKit.OptString(c, "name"),
                FkGroups = FkGroups(c),
                Insides = CommandKit.OptBool(c, "insides") ?? true,
                Centre = CommandKit.OptBool(c, "centre") ?? true,
                Hide = CommandKit.OptBool(c, "hide") ?? true,
                Store = CommandKit.OptBool(c, "store") ?? true,
            };
            var r = CommandKit.Check(c, CommandKit.Ops(c).BakeMake(CommandKit.Ids(nodes), opt, CommandKit.Exec(c, StudioOps.Labels.BakeMake)));
            var made = (BakeMakeResult)r.Value;
            var bakes = new List<object>();
            foreach (var b in made.Bakes) bakes.Add(new BridgeReply { ["bake"] = b.Bake, ["no"] = b.No, ["main"] = b.Main, ["bytes"] = b.Bytes });
            return new BridgeReply().With("items", new List<string>(made.Items)).With("bakes", bakes).With("ms", Math.Round(made.Ms, 1));
        }

        static object List(CoreContext c)
        {
            var r = CommandKit.Check(c, CommandKit.Ops(c).BakeList(CommandKit.OptString(c, "q"), CommandKit.OptBool(c, "deleted") ?? false));
            var res = (BakeListResult)r.Value;
            var rows = new List<object>();
            foreach (var x in res.Rows)
                rows.Add(new BridgeReply
                {
                    ["no"] = x.Number, ["bake"] = x.Id, ["name"] = x.Name, ["created"] = Created(x.Created), ["kind"] = x.Kind, ["parts"] = x.Parts ?? "",
                    ["fk"] = x.Fk?.Count ?? 0, ["bytes"] = x.Bytes, ["deleted"] = x.Deleted,
                });
            return new BridgeReply().With("rows", rows).With("lastNumber", res.LastNumber);
        }

        static object Rename(CoreContext c)
        {
            var (no, bake) = RowOf(c);
            string name = CommandKit.OptString(c, "name");
            if (name == null) throw new BridgeException(BridgeError.Missing("name", ArgTypes.String));
            var r = CommandKit.Check(c, CommandKit.Ops(c).BakeRename(no, bake, name, CommandKit.Exec(c, StudioOps.Labels.BakeRename)));
            var res = (BakeRenameResult)r.Value;
            return new BridgeReply().With("name", res.Name).With("items", new List<string>(res.Items));
        }

        static object Delete(CoreContext c)
        {
            var (no, bake) = RowOf(c);
            var r = CommandKit.Check(c, CommandKit.Ops(c).BakeDelete(no, bake, CommandKit.OptBool(c, "restore") ?? false));
            return new BridgeReply().With("deleted", r.Value is bool b && b);
        }

        static object Purge(CoreContext c)
        {
            var r = CommandKit.Check(c, CommandKit.Ops(c).BakePurge());
            var p = (BakePurgeResult)r.Value;
            return new BridgeReply().With("rows", p.Rows).With("blobs", p.Blobs).With("bytes", p.Bytes);
        }

        static object Keep(CoreContext c)
        {
            var n = CommandKit.Node(c, "key");
            var r = CommandKit.Check(c, CommandKit.Ops(c).BakeKeep(n.Id));
            var k = (BakeKeepResult)r.Value;
            return new BridgeReply().With("kept", k.Kept).With("no", k.No);
        }

        static object Status(CoreContext c)
        {
            var n = CommandKit.Node(c, "key");
            var r = CommandKit.Check(c, CommandKit.Ops(c).BakeStatus(n.Id));
            var s = (BakeStatusInfo)r.Value;
            int? Count(int v) => v < 0 ? (int?)null : v;
            return new BridgeReply
            {
                ["bake"] = s.Bake,
                ["main"] = s.Main,
                ["data"] = new List<string>(s.Data),
                ["bytes"] = s.Bytes,
                ["nodes"] = Count(s.Nodes),
                ["renderers"] = Count(s.Renderers),
                ["meshes"] = Count(s.Meshes),
                ["textures"] = Count(s.Textures),
                ["materials"] = Count(s.Materials),
                ["fk"] = new List<string>(s.Fk),
                ["correction"] = s.Correction,
                ["members"] = new List<string>(s.Members),
                ["parts"] = s.Parts,
                ["kind"] = s.Kind,
                ["source"] = SourceRow(s.Source),
                ["no"] = s.No,
                ["piece"] = s.PieceSteps != null ? new BridgeReply { ["steps"] = s.PieceSteps.Value, ["source"] = s.PieceSource } : null,
            };
        }

        static object Import(CoreContext c)
        {
            var r = CommandKit.Check(c, CommandKit.Ops(c).BakeImport(CommandKit.OptString(c, "path", ArgTypes.Path)));
            var res = (OldBakeImportResult)r.Value;
            var imported = new List<object>();
            foreach (var x in res.Imported)
                imported.Add(new BridgeReply { ["no"] = x.No, ["name"] = x.Name, ["renderers"] = x.Renderers, ["textures"] = x.Textures, ["findings"] = CommandKit.Findings(x.Findings) });
            var skipped = new List<object>();
            foreach (var x in res.Skipped) skipped.Add(new BridgeReply { ["no"] = x.No, ["file"] = x.File, ["code"] = x.Code });
            return new BridgeReply().With("imported", imported).With("skipped", skipped);
        }
    }
}

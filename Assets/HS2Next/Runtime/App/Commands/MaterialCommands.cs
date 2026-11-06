// The commands of material editing (group "materials", owner core, Since 1.2.0, experimental): the materialOverrides arrays of a
// character and its parts, of an item and of the map, and the character's hair bone modifiers.
//   materials.shaders                       the shaders a material can take, and the size of the scene's images
//   chara.edit.materials.items, .set, .clear   the items of a character's arrays, with what each does now; edits; removals
//   item.materials.items, .set, .clear      the same on an item
//   map.materials                           the loaded map's materials by name
//   map.materials.items, .set, .clear       the same on the map
//   chara.edit.boneMods, .set, .clear       the character's bone modifiers, editing those of the hair bones
// Writes go through StudioOps (one history entry each, `undo: false` and a client's batch as every property command); `merge` chains
// the edits of one item into one entry. They need a built target and a loaded map. An edit is the stored form of an item, given as the
// arguments of the request or as an element of `items`; a member given as null clears it. A texture's `file` is read relative to UserData
// or by an absolute path; no reply and no document keeps that path.
using System;
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HS2Next.App.Commands
{
    public static class MaterialCommands
    {
        /// <summary>The commands of this file (CoreCommands.Phase64Names).</summary>
        public static readonly string[] Names =
        {
            "materials.shaders",
            "chara.edit.materials.items", "chara.edit.materials.set", "chara.edit.materials.clear",
            "item.materials.items", "item.materials.set", "item.materials.clear",
            "map.materials", "map.materials.items", "map.materials.set", "map.materials.clear",
            "chara.edit.boneMods", "chara.edit.boneMods.set", "chara.edit.boneMods.clear",
        };

        /// <summary>CommandSpec.Since of these commands.</summary>
        public const string Since64 = "1.2.0";
        /// <summary>Their help group.</summary>
        public const string GroupName = "materials";

        const string EditDoc = "one edit: type, then by type material, property, value (float, color, keyword), renderer, property, value (renderer), material, property, file, image, offset, scale (texture), material, shader, renderQueue (shader)";
        const string V3Doc = "[x, y, z]";

        static readonly string[] EditMembers = { "type", "material", "property", "renderer", "value", "file", "image", "offset", "scale", "shader", "renderQueue" };
        const string TypeExpected = "float, color, keyword, renderer, texture or shader";

        static CommandSpec Spec(string name, string summary, CommandFlags flags) =>
            CommandKit.Spec(name, summary, GroupName).WithSince(Since64).WithFlags(flags | CommandFlags.Experimental);

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, Spec("materials.shaders", "The shaders a material edit can pick, and the size of the images the scene holds.", CommandFlags.ReadOnly)
                    .Arg("filter", ArgTypes.String, "only names that hold this text, ignoring case")
                    .Return("shaders", ArgTypes.ArrayOf(ArgTypes.Object), "{name (as an item stores it), family, label (the name shown), queue (its default render queue), keywords (the local keywords it declares)}")
                    .Return("count", ArgTypes.Int, "the shaders listed")
                    .Return("resBytes", ArgTypes.Int, "the bytes of the images in the scene's store")
                    .Example("{\"cmd\":\"materials.shaders\",\"filter\":\"Clothes\"}"),
                Shaders);

            // ---- a character
            CommandKit.Register(r, app, Spec("chara.edit.materials.items", "The items of a built character's material arrays and what each does now.", CommandFlags.ReadOnly)
                    .Arg("key", ArgTypes.Node, "a built character", true)
                    .Arg("target", ArgTypes.String, "one array: character, hair:<part or 0..3>, clothes:<kind or 0..7> or acc:<id> (default: all of them)")
                    .Return("targets", ArgTypes.ArrayOf(ArgTypes.Object), "{target, items: [{index, type, key (null for an unknown type), editable, status, why (null when it applies), then the item's stored members}]}")
                    .Example("{\"cmd\":\"chara.edit.materials.items\",\"key\":\"<id>\",\"target\":\"hair:back\"}"),
                CharaItems);
            CommandKit.Register(r, app, EditArgs(Spec("chara.edit.materials.set", "Edits a material array of a built character (one history entry; all the edits or none). A value equal to what the material holds without the item removes the item; a member given as null clears it. A card-backed character is imported first. Mosaic parts answer unsupported.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, "a built character", true)
                    .Arg("target", ArgTypes.String, "the array: character, hair:<part or 0..3>, clothes:<kind or 0..7> or acc:<id>", true))
                    .Return("target", ArgTypes.String, "the array")
                    .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "the keys of the items set, as <type>/<material or renderer>/<property>")
                    .Return("removed", ArgTypes.ArrayOf(ArgTypes.String), "the keys of the items that left")
                    .Return("items", ArgTypes.ArrayOf(ArgTypes.Object), "the items the edits name, as chara.edit.materials.items lists them")
                    .Example("{\"cmd\":\"chara.edit.materials.set\",\"key\":\"<id>\",\"target\":\"clothes:top\",\"type\":\"float\",\"material\":\"<material>\",\"property\":\"Metallic\",\"value\":0.4}")
                    .WithTimeout(60),
                CharaSet);
            CommandKit.Register(r, app, ClearArgs(Spec("chara.edit.materials.clear", "Removes items of a material array of a built character (one history entry): one item, the items of a material, or all of them. Read-only items stay; what they replaced shows again.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, "a built character", true)
                    .Arg("target", ArgTypes.String, "the array: character, hair:<part or 0..3>, clothes:<kind or 0..7> or acc:<id>", true))
                    .Example("{\"cmd\":\"chara.edit.materials.clear\",\"key\":\"<id>\",\"target\":\"character\",\"all\":true}"),
                CharaClear);

            // ---- an item
            CommandKit.Register(r, app, Spec("item.materials.items", "The items of a built item's material array and what each does now.", CommandFlags.ReadOnly)
                    .Arg("key", ArgTypes.Node, "a built item", true)
                    .Return("targets", ArgTypes.ArrayOf(ArgTypes.Object), "one {target, items, passMs (the time of the array's last pass, ms)}, items as chara.edit.materials.items")
                    .Example("{\"cmd\":\"item.materials.items\",\"key\":\"<id>\"}"),
                ItemItems);
            CommandKit.Register(r, app, EditArgs(Spec("item.materials.set", "Edits the material array of a built item (one history entry; all the edits or none). A material edit sits on top of the item's colours and patterns. A renderer is named by its path from the item. Mosaic parts answer unsupported.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, "a built item", true))
                    .Return("target", ArgTypes.String, "item")
                    .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "the keys of the items set")
                    .Return("removed", ArgTypes.ArrayOf(ArgTypes.String), "the keys of the items that left")
                    .Return("items", ArgTypes.ArrayOf(ArgTypes.Object), "the items the edits name")
                    .Example("{\"cmd\":\"item.materials.set\",\"key\":\"<id>\",\"type\":\"color\",\"material\":\"<material>\",\"property\":\"Color\",\"value\":[1,0,0,1]}")
                    .WithTimeout(60),
                ItemSet);
            CommandKit.Register(r, app, ClearArgs(Spec("item.materials.clear", "Removes items of the material array of a built item (one history entry).", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, "a built item", true))
                    .Example("{\"cmd\":\"item.materials.clear\",\"key\":\"<id>\",\"all\":true}"),
                ItemClear);

            // ---- the map
            CommandKit.Register(r, app, Spec("map.materials", "The loaded map's materials by name: each is one copy shared by its renderers.", CommandFlags.ReadOnly)
                    .Arg("filter", ArgTypes.String, "only names that hold this text, ignoring case")
                    .Arg("props", ArgTypes.Bool, "with every property the shader declares, in chara.edit.materials' forms", false, false)
                    .Arg("limit", ArgTypes.Int, "at most this many rows", false, 2000)
                    .Return("materials", ArgTypes.ArrayOf(ArgTypes.Object), "{name, shader, shaderOriginal, queue, keywords, renderers (the renderers that use it), props (with props)}")
                    .Return("count", ArgTypes.Int, "the materials that match, before the limit")
                    .Example("{\"cmd\":\"map.materials\",\"filter\":\"water\"}"),
                MapList);
            CommandKit.Register(r, app, Spec("map.materials.items", "The items of the map's material array and what each does now.", CommandFlags.ReadOnly)
                    .Return("targets", ArgTypes.ArrayOf(ArgTypes.Object), "one {target, items, passMs (the time of the array's last pass, ms; null with no map)}, items as chara.edit.materials.items")
                    .Example("{\"cmd\":\"map.materials.items\"}"),
                MapItems);
            CommandKit.Register(r, app, EditArgs(Spec("map.materials.set", "Edits the material array of the loaded map (one history entry; all the edits or none). A renderer is named by its path from the map's root. The array belongs to this map: loading another one clears it.", CommandFlags.Mutates))
                    .Return("target", ArgTypes.String, "map")
                    .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "the keys of the items set")
                    .Return("removed", ArgTypes.ArrayOf(ArgTypes.String), "the keys of the items that left")
                    .Return("items", ArgTypes.ArrayOf(ArgTypes.Object), "the items the edits name")
                    .Example("{\"cmd\":\"map.materials.set\",\"type\":\"float\",\"material\":\"<material>\",\"property\":\"Glossiness\",\"value\":0.2}")
                    .WithTimeout(60),
                MapSet);
            CommandKit.Register(r, app, ClearArgs(Spec("map.materials.clear", "Removes items of the material array of the loaded map (one history entry).", CommandFlags.Mutates))
                    .Example("{\"cmd\":\"map.materials.clear\",\"all\":true}"),
                MapClear);

            // ---- hair bone modifiers
            CommandKit.Register(r, app, Spec("chara.edit.boneMods", "The bone modifiers of a built character, stored with the character, and the bones of its hair.", CommandFlags.ReadOnly)
                    .Arg("key", ArgTypes.Node, "a built character", true)
                    .Arg("hair", ArgTypes.Bool, "only the modifiers of the hair bones and those whose bone is found nowhere", false, true)
                    .Return("mods", ArgTypes.ArrayOf(ArgTypes.Object), "{bone, location, scale, length, position, rotation, found (the bone resolves on this character)}")
                    .Return("bones", ArgTypes.ArrayOf(ArgTypes.String), "the names of the hair bones: the hair root and what lies under it")
                    .Example("{\"cmd\":\"chara.edit.boneMods\",\"key\":\"<id>\"}"),
                BoneMods);
            CommandKit.Register(r, app, Spec("chara.edit.boneMods.set", "Changes the modifier of a hair bone (one history entry): the members given, the others keep their value. A member given as null goes back to no effect; a modifier with no effect removes the entry. Every other modifier stays as it is. A bone that keeps the mosaic in place answers unsupported.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, "a built character", true)
                    .Arg("bone", ArgTypes.String, "a hair bone's name", true)
                    .Arg("scale", ArgTypes.Vec3, V3Doc)
                    .Arg("length", ArgTypes.Float, "the bone's length factor")
                    .Arg("position", ArgTypes.Vec3, V3Doc)
                    .Arg("rotation", ArgTypes.Euler, "Euler degrees")
                    .Arg("merge", ArgTypes.Bool, "join the history entry of the previous edit of this bone", false, false)
                    .Return("bone", ArgTypes.String, "the bone")
                    .Return("removed", ArgTypes.Bool, "the entry left")
                    .Example("{\"cmd\":\"chara.edit.boneMods.set\",\"key\":\"<id>\",\"bone\":\"N_hair_Root\",\"length\":1.1}"),
                BoneSet);
            CommandKit.Register(r, app, Spec("chara.edit.boneMods.clear", "Removes bone modifiers (one history entry): the entries of one bone, or of every hair bone.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, "a built character", true)
                    .Arg("bone", ArgTypes.String, "one bone's name, a stored entry that is found nowhere included")
                    .Arg("all", ArgTypes.Bool, "true: every hair bone")
                    .Return("removed", ArgTypes.Int, "the bones whose entries left")
                    .Example("{\"cmd\":\"chara.edit.boneMods.clear\",\"key\":\"<id>\",\"all\":true}"),
                BoneClear);
        }

        static CommandSpec EditArgs(CommandSpec s) =>
            s.Arg("type", ArgTypes.Enum("float", "color", "keyword", "renderer", "texture", "shader"), "one edit: the item's type")
                .Arg("material", ArgTypes.String, "the material's name, as stored; every material of that name on the target")
                .Arg("property", ArgTypes.String, "the property as stored, without the first underscore of the shader's name; a renderer's property is Enabled")
                .Arg("renderer", ArgTypes.String, "a `renderer` item: the renderer's name on a character, its path on an item or the map")
                .Arg("value", ArgTypes.Any, "float: a number; color: [r, g, b, a]; keyword: true or false; renderer: 0 or 1")
                .Arg("file", ArgTypes.Path, "texture: a PNG or JPEG to take into the scene, at most 8192 pixels on a side and 64 MiB: relative to UserData, or an absolute path")
                .Arg("image", ArgTypes.String, "texture: the res name of an image the scene holds; null clears it")
                .Arg("offset", ArgTypes.Any, "texture: [x, y]; null clears it")
                .Arg("scale", ArgTypes.Any, "texture: [x, y]; null clears it")
                .Arg("shader", ArgTypes.String, "shader: a name of materials.shaders; null clears it")
                .Arg("renderQueue", ArgTypes.Int, "shader: 0..5000; null clears it")
                .Arg("items", ArgTypes.ArrayOf(ArgTypes.Object), "several edits instead of one, each as those arguments")
                .Arg("merge", ArgTypes.Bool, "with one edit: join the history entry of the previous edit of this item", false, false);

        static CommandSpec ClearArgs(CommandSpec s) =>
            s.Arg("item", ArgTypes.Object, "one item's key: {type, material, property}, {type: shader, material} or {type: renderer, renderer, property}")
                .Arg("material", ArgTypes.String, "every editable item of this material")
                .Arg("all", ArgTypes.Bool, "true: every editable item of the array")
                .Return("removed", ArgTypes.Int, "items removed");

        // ------------------------------------------------------------ shared

        static MaterialEditTarget CharaTarget(CoreContext c, TreeNode n, string text)
        {
            if (!MaterialEditTarget.TryParse(n.Id, text, out var t, out string why)) throw CommandKit.BadArgs("target", "character, hair:<part or 0..3>, clothes:<kind or 0..7> or acc:<id>", why);
            return t;
        }

        static TreeNode ItemNode(CoreContext c) => CommandKit.KeyKind.Item.Check(CommandKit.Node(c, "key"), "key");

        // An edit from the arguments or from an element of `items`: `prefix` names its arguments in a refusal.
        static MaterialEditRequest Edit(CoreContext c, JObject o, string prefix)
        {
            string Arg(string member) => prefix == null ? member : prefix + "." + member;
            var q = new MaterialEditRequest { Arg = prefix };
            var type = o["type"];
            if (type == null || type.Type == JTokenType.Null) throw new BridgeException(BridgeError.Missing(Arg("type"), TypeExpected));
            if (type.Type != JTokenType.String) throw CommandKit.BadArgs(Arg("type"), TypeExpected);
            q.Type = (string)type;
            q.Material = Text(o, "material", Arg);
            q.Property = Text(o, "property", Arg);
            q.Renderer = Text(o, "renderer", Arg);
            var value = o["value"];
            switch (q.Type)
            {
                case "float":
                    q.Number = Number(value, Arg("value"));
                    Only(o, Arg, "material", "property", "value");
                    break;
                case "color":
                    q.Color = ColorOf(value, Arg("value"));
                    Only(o, Arg, "material", "property", "value");
                    break;
                case "keyword":
                    if (value == null || value.Type != JTokenType.Boolean) throw CommandKit.BadArgs(Arg("value"), ArgTypes.Bool);
                    q.Switch = (bool)value;
                    Only(o, Arg, "material", "property", "value");
                    break;
                case "renderer":
                    if (value == null || value.Type != JTokenType.Integer) throw CommandKit.BadArgs(Arg("value"), "0 or 1");
                    q.Enabled = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, (long)value));
                    Only(o, Arg, "renderer", "property", "value");
                    break;
                case "texture":
                    if (o.TryGetValue("file", out var file))
                    {
                        if (file.Type != JTokenType.String) throw CommandKit.BadArgs(Arg("file"), ArgTypes.Path);
                        q.File = ImageFile(c, (string)file, Arg("file"));
                    }
                    if (o.TryGetValue("image", out var image))
                    {
                        if (image.Type != JTokenType.Null && image.Type != JTokenType.String) throw CommandKit.BadArgs(Arg("image"), "a res name or null");
                        q.WithImage(image.Type == JTokenType.Null ? null : (string)image);
                    }
                    if (o.TryGetValue("offset", out var offset)) q.WithOffset(V2(offset, Arg("offset")));
                    if (o.TryGetValue("scale", out var scale)) q.WithScale(V2(scale, Arg("scale")));
                    Only(o, Arg, "material", "property", "file", "image", "offset", "scale");
                    break;
                case "shader":
                    if (o.TryGetValue("shader", out var shader))
                    {
                        if (shader.Type != JTokenType.Null && shader.Type != JTokenType.String) throw CommandKit.BadArgs(Arg("shader"), "a shader's name or null");
                        q.WithShader(shader.Type == JTokenType.Null ? null : (string)shader);
                    }
                    if (o.TryGetValue("renderQueue", out var queue))
                    {
                        if (queue.Type != JTokenType.Null && queue.Type != JTokenType.Integer) throw CommandKit.BadArgs(Arg("renderQueue"), MaterialEdits.MinQueue + ".." + MaterialEdits.MaxQueue + " or null");
                        q.WithQueue(queue.Type == JTokenType.Null ? (int?)null : (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, (long)queue)));
                    }
                    Only(o, Arg, "material", "shader", "renderQueue");
                    break;
            }
            return q;
        }

        // The image file of an edit: a path FileRoots accepts. The refusals of that check keep their codes, and lose the path.
        static string ImageFile(CoreContext c, string text, string arg)
        {
            try { return FileRoots.ForRead(c, arg, text, c.Paths.UserData, MaterialTextureImport.Extensions, null); }
            catch (BridgeException e) when (e.Code == BridgeCodes.NotFound)
            {
                throw new BridgeException(BridgeError.NotFound("the file does not exist", arg));
            }
            catch (BridgeException e) when (e.Code == BridgeCodes.IoDenied)
            {
                throw new BridgeException(BridgeError.IoDenied("only .png, .jpg and .jpeg files are read, by an absolute path or a path under UserData", arg));
            }
        }

        static string Text(JObject o, string member, Func<string, string> arg)
        {
            var t = o[member];
            if (t == null || t.Type == JTokenType.Null) return null;
            if (t.Type != JTokenType.String) throw CommandKit.BadArgs(arg(member), ArgTypes.String);
            return (string)t;
        }

        static float Number(JToken t, string arg)
        {
            if (t == null || (t.Type != JTokenType.Float && t.Type != JTokenType.Integer)) throw CommandKit.BadArgs(arg, ArgTypes.Float);
            return CommandKit.Finite((float)(double)t, arg);
        }

        static Color4 ColorOf(JToken t, string arg)
        {
            if (!(t is JArray a) || a.Count != 4) throw CommandKit.BadArgs(arg, "[r, g, b, a]");
            return new Color4(Number(a[0], arg + "[0]"), Number(a[1], arg + "[1]"), Number(a[2], arg + "[2]"), Number(a[3], arg + "[3]"));
        }

        static Vec2? V2(JToken t, string arg)
        {
            if (t.Type == JTokenType.Null) return null;
            if (!(t is JArray a) || a.Count != 2) throw CommandKit.BadArgs(arg, "[x, y] or null");
            return new Vec2(Number(a[0], arg + "[0]"), Number(a[1], arg + "[1]"));
        }

        // A member of another type's edit is a mistake, not something to leave out quietly.
        static void Only(JObject o, Func<string, string> arg, params string[] allowed)
        {
            foreach (var m in EditMembers)
                if (m != "type" && o[m] != null && Array.IndexOf(allowed, m) < 0) throw CommandKit.BadArgs(arg(m), string.Join(", ", allowed), m + " does not go with this type");
        }

        // The edits of a request: its arguments, or the elements of `items`.
        static List<MaterialEditRequest> Edits(CoreContext c, out bool one)
        {
            var edits = new List<MaterialEditRequest>();
            one = !c.Args.Has("items");
            if (one)
            {
                var o = new JObject();
                var whole = JObject.Parse(c.Args.Json);
                foreach (var m in EditMembers)
                    if (whole.TryGetValue(m, out var t)) o[m] = t;
                edits.Add(Edit(c, o, null));
                return edits;
            }
            foreach (var m in EditMembers)
                if (c.Args.Kind(m) != ArgKind.Missing) throw CommandKit.BadArgs(m, "items", m + " goes with one edit; in items each element holds its own");
            if (c.Args.Kind("items") != ArgKind.Array) throw CommandKit.BadArgs("items", ArgTypes.ArrayOf(ArgTypes.Object));
            var list = JArray.Parse(c.Args.RawJson("items"));
            if (list.Count == 0) throw CommandKit.BadArgs("items", "at least one edit", "no edits");
            for (int i = 0; i < list.Count; i++)
            {
                string arg = "items[" + i + "]";
                if (!(list[i] is JObject e)) throw CommandKit.BadArgs(arg, ArgTypes.Object);
                foreach (var p in e.Properties())
                    if (Array.IndexOf(EditMembers, p.Name) < 0)
                    {
                        if (c.Ctx.Strict) throw CommandKit.BadArgs(arg + "." + p.Name, string.Join(", ", EditMembers), "unknown member '" + p.Name + "'");
                        c.Warn("unknown member '" + p.Name + "' of " + arg + " ignored");
                    }
                edits.Add(Edit(c, e, arg));
            }
            return edits;
        }

        static ExecuteOptions Options(CoreContext c, string label, string mergeKey)
        {
            var o = CommandKit.Exec(c, label);
            if (mergeKey != null) o.MergeKey = mergeKey;
            return o;
        }

        static object SetCommand(CoreContext c, MaterialEditTarget t)
        {
            var edits = Edits(c, out bool one);
            bool merge = CommandKit.OptBool(c, "merge") ?? false;
            if (merge && !one) throw CommandKit.BadArgs("merge", "one edit", "merge chains the edits of one item");
            string mergeKey = merge ? "materials:" + (t.NodeId ?? "map") + ":" + t.Text + ":" + edits[0].Key : null;
            var ops = CommandKit.Ops(c);
            var res = CommandKit.Check(c, ops.SetMaterials(t, edits, Options(c, StudioOps.Labels.MaterialEdit, mergeKey)));
            var removed = res.Value as List<string> ?? new List<string>();
            var rows = new List<object>();
            var read = ops.MaterialStatusRead(t, out _);
            var list = ops.MaterialArray(t);
            for (int i = 0; i < list.Count; i++)
                foreach (var e in edits)
                    if (e.Key.Matches(list[i])) { rows.Add(Row(read, i, list[i])); break; }
            return new BridgeReply().With("target", t.Text).With("changed", new List<string>(res.Changed)).With("removed", removed).With("items", rows);
        }

        static object ClearCommand(CoreContext c, MaterialEditTarget t)
        {
            bool all = AdvancedPoseCommands.All(c, "item", "material");
            MeKey? key = null;
            string material = null;
            if (!all)
            {
                if (c.Args.Has("item")) key = KeyArg(c);
                else material = CommandKit.OptString(c, "material");
            }
            var res = CommandKit.Check(c, CommandKit.Ops(c).ClearMaterial(t, key, material, CommandKit.Exec(c, StudioOps.Labels.MaterialClear)));
            return new BridgeReply().With("removed", (res.Value as List<string>)?.Count ?? 0);
        }

        static MeKey KeyArg(CoreContext c)
        {
            if (c.Args.Kind("item") != ArgKind.Object) throw CommandKit.BadArgs("item", ArgTypes.Object);
            var o = JObject.Parse(c.Args.RawJson("item"));
            string Member(string name) => o[name] != null && o[name].Type == JTokenType.String ? (string)o[name] : null;
            string type = Member("type");
            if (type == null) throw CommandKit.BadArgs("item.type", TypeExpected);
            if (MaterialOverride.Rank(type) < 0) throw CommandKit.BadArgs("item.type", TypeExpected, "no type \"" + type + "\"");
            if (type == "shader")
            {
                if (Member("material") == null) throw CommandKit.BadArgs("item.material", ArgTypes.String);
                return MeKey.Shader(Member("material"));
            }
            if (type == "renderer")
            {
                if (Member("renderer") == null) throw CommandKit.BadArgs("item.renderer", ArgTypes.String);
                return MeKey.Renderer(Member("renderer"), Member("property") ?? MeKey.EnabledProperty);
            }
            if (Member("material") == null) throw CommandKit.BadArgs("item.material", ArgTypes.String);
            if (Member("property") == null) throw CommandKit.BadArgs("item.property", ArgTypes.String);
            return MeKey.Property(type, Member("material"), Member("property"));
        }

        static BridgeReply KeyReply(MaterialOverride item)
        {
            if (!MeKey.TryOf(item, out var k)) return null;
            var r = new BridgeReply().With("type", k.Type);
            switch (item)
            {
                case MeRenderer x: return r.With("renderer", x.Renderer).With("property", x.Property);
                case MeShader x: return r.With("material", x.Material);
                case MeCopy x: return r.With("material", x.Material).With("copyName", x.CopyName);
                case MeName x: return r.With("renderer", x.Renderer).With("material", x.Material);
                case MeProjector x: return r.With("projector", x.Projector).With("property", x.Property);
                case MeMaterialProperty x: return r.With("material", x.Material).With("property", x.Property);
                default: return r;
            }
        }

        // The line of a status (the Materials view shows it in the viewer's language).
        static string Why(MaterialItemState s)
        {
            switch (s.Status)
            {
                case MaterialStatus.Applied: return null;
                case MaterialStatus.NotOnShader: return "not on this shader";
                case MaterialStatus.ShaderUnavailable: return "this shader is not available";
                case MaterialStatus.MaterialMissing: return s.Note == MaterialStatus.DrawsBody ? "this part draws the body's material" : "no such material on this object";
                case MaterialStatus.ObjectMissing: return "no such part";
                case MaterialStatus.MosaicProtected: return "mosaic parts and their materials cannot be edited";
                case MaterialStatus.NotImplemented: return "not supported";
                case MaterialStatus.FallbackFixed: return "the general fallback fixes this value";
                case MaterialStatus.Driven: return "the map animates this value";
                case MaterialStatus.BadValue: return "stored as text, not a number";
                case MaterialStatus.Undecodable: return "the image cannot be read";
                default: return s.Status;
            }
        }

        // One item of a list: what it is, what it does now, then its stored members.
        static BridgeReply Row(Func<MaterialOverride, MaterialItemState> read, int index, MaterialOverride item)
        {
            var state = read != null ? read(item) : new MaterialItemState(MaterialStatus.ObjectMissing);
            var r = new BridgeReply().With("index", index).With("type", item.Type).With("key", KeyReply(item)).With("editable", MaterialEdits.IsEditable(item))
                .With("status", state.Status).With("why", Why(state));
            var stored = JObject.Parse(FieldJson.ToText(item));
            if (item is MeUnknown) return r.With("raw", new RawJson(stored.ToString(Newtonsoft.Json.Formatting.None)));
            foreach (var p in stored.Properties())
                if (p.Name != "type" && !r.ContainsKey(p.Name)) r[p.Name] = new RawJson(p.Value.ToString(Newtonsoft.Json.Formatting.None));
            return r;
        }

        static BridgeReply TargetReply(StudioOps ops, MaterialEditTarget t, Func<MaterialOverride, MaterialItemState> read)
        {
            var list = ops.MaterialArray(t);
            var rows = new List<object>(list.Count);
            for (int i = 0; i < list.Count; i++) rows.Add(Row(read, i, list[i]));
            var r = new BridgeReply().With("target", t.Text).With("items", rows);
            if (t.Kind == MaterialTargetKind.Item || t.Kind == MaterialTargetKind.Map) r["passMs"] = ops.MaterialPassMilliseconds(t);
            return r;
        }

        // A target's list; a target that is not built answers its refusal.
        static BridgeReply BuiltTargetReply(CoreContext c, MaterialEditTarget t)
        {
            var ops = CommandKit.Ops(c);
            var read = ops.MaterialStatusRead(t, out var fail);
            if (read == null) CommandKit.Check(c, fail);
            return TargetReply(ops, t, read);
        }

        // ------------------------------------------------------------ materials.shaders

        static object Shaders(CoreContext c)
        {
            string filter = CommandKit.OptString(c, "filter");
            var rows = new List<object>();
            foreach (var s in MaterialShaders.All)
            {
                if (filter != null && s.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                rows.Add(new BridgeReply().With("name", s.Name).With("family", s.Family).With("label", s.Label).With("queue", s.Queue).With("keywords", new List<string>(s.Keywords)));
            }
            return new BridgeReply().With("shaders", rows).With("count", rows.Count).With("resBytes", c.Session.Model.File?.Res?.TotalBytes ?? 0L);
        }

        // ------------------------------------------------------------ a character

        static object CharaItems(CoreContext c)
        {
            var r = CommandKit.Built(c);
            string text = CommandKit.OptString(c, "target");
            var rows = new List<object>();
            if (text != null) rows.Add(BuiltTargetReply(c, CharaTarget(c, r.Node, text)));
            else
                foreach (var t in CommandKit.Ops(c).MaterialTargetsOf(r.Node.Id)) rows.Add(BuiltTargetReply(c, t));
            return new BridgeReply().With("targets", rows);
        }

        static object CharaSet(CoreContext c)
        {
            var n = CommandKit.Chara(c);
            return SetCommand(c, CharaTarget(c, n, CommandKit.OptString(c, "target", "a target") ?? throw new BridgeException(BridgeError.Missing("target", ArgTypes.String))));
        }

        static object CharaClear(CoreContext c)
        {
            var n = CommandKit.Chara(c);
            return ClearCommand(c, CharaTarget(c, n, CommandKit.OptString(c, "target", "a target") ?? throw new BridgeException(BridgeError.Missing("target", ArgTypes.String))));
        }

        // ------------------------------------------------------------ an item

        static object ItemItems(CoreContext c)
        {
            var n = ItemNode(c);
            c.Runtime.ApplyNow();
            return new BridgeReply().With("targets", new List<object> { BuiltTargetReply(c, MaterialEditTarget.Item(n.Id)) });
        }

        static object ItemSet(CoreContext c) => SetCommand(c, MaterialEditTarget.Item(ItemNode(c).Id));
        static object ItemClear(CoreContext c) => ClearCommand(c, MaterialEditTarget.Item(ItemNode(c).Id));

        // ------------------------------------------------------------ the map

        static object MapList(CoreContext c)
        {
            string filter = CommandKit.OptString(c, "filter");
            bool props = CommandKit.OptBool(c, "props") ?? false;
            int limit = CommandKit.OptInt(c, "limit") ?? 2000;
            if (limit < 0) throw CommandKit.BadArgs("limit", "a number from 0");
            c.Runtime.ApplyNow();
            var ops = CommandKit.Ops(c);
            var rows = new List<object>();
            int count = 0;
            foreach (var m in ops.MapMaterialList())
            {
                if (filter != null && m.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                count++;
                if (rows.Count >= limit) continue;
                var mat = m.Material;
                string shader = mat && mat.shader ? mat.shader.name : null;
                var row = new BridgeReply().With("name", m.Name).With("shader", shader).With("shaderOriginal", ops.MapShaderOriginal(mat) ?? shader)
                    .With("queue", mat ? mat.renderQueue : -1).With("keywords", mat ? Sorted(mat.shaderKeywords) : new List<string>()).With("renderers", m.Slots);
                if (props) row["props"] = CharaEditCommands.PropsOf(mat, null);
                rows.Add(row);
            }
            return new BridgeReply().With("materials", rows).With("count", count);
        }

        static List<string> Sorted(string[] a)
        {
            var l = new List<string>(a);
            l.Sort(StringComparer.Ordinal);
            return l;
        }

        static object MapItems(CoreContext c)
        {
            c.Runtime.ApplyNow();
            var ops = CommandKit.Ops(c);
            var map = MaterialEditTarget.Map();
            return new BridgeReply().With("targets", new List<object> { TargetReply(ops, map, ops.MaterialStatusRead(map, out _)) });
        }

        static object MapSet(CoreContext c) => SetCommand(c, MaterialEditTarget.Map());
        static object MapClear(CoreContext c) => ClearCommand(c, MaterialEditTarget.Map());

        // ------------------------------------------------------------ hair bone modifiers

        static object BoneMods(CoreContext c)
        {
            var r = CommandKit.Built(c);
            bool hair = CommandKit.OptBool(c, "hair") ?? true;
            var ctx = r.Context;
            var names = HairBones.Names(ctx);
            var body = HS2Next.Chara.Ext.AbmxApplier.BodyDictionary(ctx);
            Dictionary<string, Transform> accessories = null;
            var mods = new List<object>();
            foreach (var m in CommandKit.Ops(c).BoneModArray(r.Node.Id))
            {
                bool found = HS2Next.Chara.Ext.AbmxApplier.Find(body, m.Bone) || names.Contains(m.Bone);
                // an entry without a location goes on to the accessories when the body lacks the bone
                if (!found && m.Location != BodyBoneMod.LocationBodyTop)
                    found = HS2Next.Chara.Ext.AbmxApplier.Find(accessories = accessories ?? HairBones.AccessoryBones(ctx), m.Bone);
                if (hair && found && !names.Contains(m.Bone)) continue;
                mods.Add(new BridgeReply().With("bone", m.Bone).With("location", m.Location).With("scale", CommandKit.D3(m.Scale)).With("length", (double)m.Length)
                    .With("position", CommandKit.D3(m.Position)).With("rotation", CommandKit.D3(m.Rotation)).With("found", found));
            }
            var bones = new List<string>();
            foreach (var t in HairBones.Of(ctx)) if (!bones.Contains(t.name)) bones.Add(t.name);
            return new BridgeReply().With("mods", mods).With("bones", bones);
        }

        // A member of the set command: absent keeps it, null takes it back to no effect.
        static Vec3? BoneV3(CoreContext c, string arg, Vec3 none, ref bool any)
        {
            var k = c.Args.Kind(arg);
            if (k == ArgKind.Missing) return null;
            any = true;
            return k == ArgKind.Null ? none : CommandKit.OptVec3(c, arg, V3Doc);
        }

        static object BoneSet(CoreContext c)
        {
            var n = CommandKit.Chara(c);
            string bone = CommandKit.OptString(c, "bone", "a bone's name");
            if (string.IsNullOrEmpty(bone)) throw new BridgeException(BridgeError.Missing("bone", ArgTypes.String));
            bool any = false;
            var scale = BoneV3(c, "scale", Vec3.One, ref any);
            var position = BoneV3(c, "position", Vec3.Zero, ref any);
            var rotation = BoneV3(c, "rotation", Vec3.Zero, ref any);
            float? length = null;
            var lk = c.Args.Kind("length");
            if (lk != ArgKind.Missing)
            {
                any = true;
                length = lk == ArgKind.Null ? 1f : CommandKit.Finite(CommandKit.OptFloat(c, "length").Value, "length");
            }
            if (!any) throw CommandKit.BadArgs("bone", "a member to set", "give scale, length, position or rotation (null goes back to no effect)");
            bool merge = CommandKit.OptBool(c, "merge") ?? false;
            var res = CommandKit.Check(c, CommandKit.Ops(c).SetHairBoneMod(n.Id, bone, scale, length, position, rotation, Options(c, StudioOps.Labels.HairBoneAdjust, merge ? "boneMods:" + n.Id + ":" + bone : null)));
            return new BridgeReply().With("bone", bone).With("removed", (res.Value as List<string>)?.Count > 0);
        }

        static object BoneClear(CoreContext c)
        {
            var n = CommandKit.Chara(c);
            bool all = AdvancedPoseCommands.All(c, "bone");
            string bone = all ? null : CommandKit.OptString(c, "bone", "a bone's name");
            var res = CommandKit.Check(c, CommandKit.Ops(c).ClearHairBoneMods(n.Id, bone, CommandKit.Exec(c, StudioOps.Labels.HairBoneAdjust)));
            return new BridgeReply().With("removed", (res.Value as List<string>)?.Count ?? 0);
        }
    }
}

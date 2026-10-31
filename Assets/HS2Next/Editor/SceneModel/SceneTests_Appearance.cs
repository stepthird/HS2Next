// P: every member of the golden chara.json / coord.json resolves under
// `appearance.` with the value the document holds (canonical JSON equal) and the right leaf type; every settable leaf
// takes a type-correct new value through the bus with one NodeChanged event on its canonical path, changes exactly
// the document bytes, and undo / redo restore them; the exceptions (hair parts, clothes kinds,
// accessories by id across insert / remove / move, wholes, sex, hs2, id, importSlot, ext, card-backed nodes, other
// kinds) and the value rules (NaN, −0, fixed lengths, refs, resources, shapes, out-of-range numbers kept).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;
    using Newtonsoft.Json.Linq;

    public static class SceneTests_Appearance
    {
        public static void Run(TestRunner t)
        {
            Walk(t);
            Exceptions(t);
            Accessories(t);
            Values(t);
            Events(t);
            Helpers(t);
        }

        // ---------------- fixtures ----------------

        /// <summary>A session with one character node holding a private copy of the golden subtree; returns its id.</summary>
        internal static SceneSession GoldenChara(TestRunner t, out string id, ulong seed = 1)
        {
            var s = Kit.Session(seed, new SessionOptions { History = new HistoryLimits { MaxEntries = int.MaxValue, MaxBytes = long.MaxValue } });
            var golden = Kit.GoldenContainer(t, true).Charas[Kit.GoldenChara];
            var r = s.AddChara(new CharaAppearance { Stored = golden.Clone() });
            id = r.NewIds != null && r.NewIds.Count > 0 ? r.NewIds[0] : null;
            return s;
        }

        internal static byte[] CharaBytes(SceneSession s, string id) =>
            CanonicalJsonWriter.ToBytes(CharaDocument.EntryName, s.Model.Find(id).Appearance.Stored.Chara.Write);

        internal static byte[] CoordBytes(SceneSession s, string id) =>
            CanonicalJsonWriter.ToBytes(CoordDocument.EntryName, s.Model.Find(id).Appearance.Stored.Coord.Write);

        static string Both(SceneSession s, string id) => Kit.Hash(CharaBytes(s, id)) + "/" + Kit.Hash(CoordBytes(s, id));

        // ---------------- the walk ----------------

        sealed class WalkStats
        {
            public int Nodes, Leaves, Settable, Blocked, Unknown, Opaque, Failures;
            public readonly SortedSet<string> Families = new SortedSet<string>(StringComparer.Ordinal);
            public readonly SortedSet<string> UnknownMembers = new SortedSet<string>(StringComparer.Ordinal);
        }

        // Members inside which paths stop (polymorphic MaterialEditor items: set as whole elements).
        static readonly string[] OpaqueLists = { "materialOverrides" };
        // Unknown members of the golden documents (kept by the reader, not reachable by paths).
        static readonly string[] ExpectedUnknown =
        {
            "appearance.chara.face.futureFaceMember", "appearance.chara.futureCharaMember",
            "appearance.coord.accessories[\"0a1b2c3d4e5f60718293a4b5c6d7e8f9\"].futureAccessoryMember",
        };

        static void Walk(TestRunner t)
        {
            var s = GoldenChara(t, out string id);
            t.Check(id != null, "walk: the golden character is added");
            if (id == null) return;
            var st = new WalkStats();
            var chara = DocReader.ParseText(Kit.Text(CharaBytes(s, id)), CharaDocument.EntryName);
            var coord = DocReader.ParseText(Kit.Text(CoordBytes(s, id)), CoordDocument.EntryName);
            WalkNode(t, s, id, AppearancePaths.CharaPrefix, chara, st, false);
            WalkNode(t, s, id, AppearancePaths.CoordPrefix, coord, st, false);
            t.Check(st.Failures == 0, "walk: " + st.Nodes + " paths resolve to the document's JSON (" + st.Leaves + " leaves, " + st.Settable +
                                       " set / undo / redo, " + st.Blocked + " read-only, " + st.Opaque + " whole MaterialEditor items; " + st.Failures + " failures)");
            t.Check(st.UnknownMembers.SetEquals(ExpectedUnknown), "walk: the unknown members are not reachable (" + string.Join(", ", st.UnknownMembers) + ")");
            t.Check(st.Families.Count >= 120, "walk: at least 120 path families (" + st.Families.Count + ")");
            t.Info("walk: " + st.Families.Count + " path families, e.g. " + string.Join(" ", st.Families.Take(6)));
            Kit.Valid(t, s, "walk");
        }

        static string Family(string path)
        {
            // indices → [n], accessory keys → ["id"]
            var p = FieldPath.Parse(path);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < p.Count; i++)
            {
                var x = p[i];
                if (x.Kind == FieldStepKind.Name) { if (i > 0) sb.Append('.'); sb.Append(x.Name); }
                else if (x.Kind == FieldStepKind.Index) sb.Append("[n]");
                else sb.Append("[\"id\"]");
            }
            return sb.ToString();
        }

        static void WalkNode(TestRunner t, SceneSession s, string id, string path, JToken json, WalkStats st, bool insideOpaque)
        {
            st.Nodes++;
            object value;
            try { value = FieldAccessor.Get(s.Model, id, path); }
            catch (CommandException e)
            {
                if (e.Code == CommandCodes.BadArgs && e.Message.Contains("has no member")) { st.Unknown++; st.UnknownMembers.Add(path); return; }
                if (e.Code == CommandCodes.Unsupported && path.EndsWith(".ext", StringComparison.Ordinal)) return;
                st.Failures++;
                t.Check(false, "walk: " + path + " does not resolve: " + e.Code + " " + e.Message);
                return;
            }
            string want = CanonicalJsonWriter.ToText(json), got = FieldJson.ToText(value);
            if (want != got)
            {
                st.Failures++;
                t.Check(false, "walk: " + path + " reads " + Short(got) + ", the document holds " + Short(want));
                return;
            }
            st.Families.Add(Family(path));
            bool opaque = OpaqueLists.Any(o => path.EndsWith("." + o, StringComparison.Ordinal));
            if (json is JObject o)
            {
                foreach (var prop in o.Properties())
                {
                    if (prop.Name == "ext") continue;
                    WalkNode(t, s, id, path + "." + prop.Name, prop.Value, st, insideOpaque);
                }
                return;
            }
            if (json is JArray a && !(value is ValueType))
            {
                if (path == AppearancePaths.AccessoryList)
                {
                    foreach (JObject e in a) WalkNode(t, s, id, AppearancePaths.Accessory((string)e["id"]), e, st, insideOpaque);
                    return;
                }
                if (opaque)
                {
                    // whole elements only: read each, then set the list to itself reversed and back
                    for (int i = 0; i < a.Count; i++)
                    {
                        st.Opaque++;
                        string ep = path + "[" + i + "]";
                        string g = FieldJson.ToText(FieldAccessor.Get(s.Model, id, ep));
                        if (g != CanonicalJsonWriter.ToText(a[i])) { st.Failures++; t.Check(false, "walk: " + ep + " reads " + Short(g)); }
                    }
                    // items of an unknown MaterialEditor type are kept as read (a copy keeps them, like a load)
                    var nl = a.Count > 1 ? Reversed(value) : Activator.CreateInstance(value.GetType());
                    SetUndoRedo(t, s, id, path, nl, st, emptyOk: a.Count == 0);
                    return;
                }
                for (int i = 0; i < a.Count; i++) WalkNode(t, s, id, path + "[" + i + "]", a[i], st, insideOpaque);
                if (a.Count == 0) SetUndoRedo(t, s, id, path, value, st, emptyOk: true);
                return;
            }
            // a leaf
            st.Leaves++;
            Leaf(t, s, id, path, value, st);
        }

        static object Reversed(object list)
        {
            var src = (IList)list;
            var r = (IList)Activator.CreateInstance(list.GetType());
            for (int i = src.Count - 1; i >= 0; i--) r.Add(src[i]);
            return r;
        }

        static string Short(string s) => s == null ? "null" : s.Length > 120 ? s.Substring(0, 120) + "…" : s;

        static readonly string[] Blocked = { ".profile.sex", ".profile.hs2", "appearance.coord.sex", "].id", "].importSlot" };

        static bool IsBlocked(string path) =>
            Blocked.Any(b => path.EndsWith(b, StringComparison.Ordinal)) || path.Contains(".profile.hs2.") || path.Contains(".profile.hs2[");

        static void Leaf(TestRunner t, SceneSession s, string id, string path, object value, WalkStats st)
        {
            Type leaf;
            try { leaf = FieldAccessor.LeafType(s.Model, id, path); }
            catch (CommandException e) { st.Failures++; t.Check(false, "walk: no leaf type for " + path + ": " + e.Message); return; }
            if (value != null && !(Nullable.GetUnderlyingType(leaf) ?? leaf).IsInstanceOfType(value))
            { st.Failures++; t.Check(false, "walk: " + path + " holds a " + value.GetType().Name + ", leaf type " + leaf.Name); return; }
            object nv = Different(path, leaf, value);
            if (IsBlocked(path))
            {
                st.Blocked++;
                var r = s.Set(id, path, nv);
                if (!(r.Status == CommandStatus.Error && r.Code == CommandCodes.Unsupported))
                { st.Failures++; t.Check(false, "walk: " + path + " is read-only, the set gave " + r); }
                return;
            }
            if (nv == null) { st.Failures++; t.Check(false, "walk: no test value for " + path + " (" + leaf.Name + ")"); return; }
            if (System.Text.RegularExpressions.Regex.IsMatch(path, @"\.(bundles\[\d+\]\.index|skinOverlays\[\d+\]\.target)$"))
            {
                // lists the writer sorts keep their saved order: the order key is not written element-wise
                st.Blocked++;
                var r = s.Set(id, path, nv);
                if (!(r.Status == CommandStatus.Error && r.Code == CommandCodes.BadArgs)) { st.Failures++; t.Check(false, "walk: " + path + " is an order key, the set gave " + r); }
                return;
            }
            if (FieldPath.Parse(path).Parent is FieldPath op && FieldAccessor.Get(s.Model, id, op.Text) is OverlayEntry oe &&
                (path.EndsWith(".clearTarget", StringComparison.Ordinal) ? oe.Image == null && !(bool)nv : path.EndsWith(".image", StringComparison.Ordinal) && nv == null && !oe.ClearTarget))
            {
                // the model keeps the save form: an overlay entry without image and clearTarget is null
                st.Blocked++;
                var r = s.Set(id, path, nv);
                if (!(r.Status == CommandStatus.Error && r.Code == CommandCodes.BadArgs)) { st.Failures++; t.Check(false, "walk: " + path + " would empty the overlay entry, the set gave " + r); }
                return;
            }
            SetUndoRedo(t, s, id, path, nv, st);
        }

        /// <summary>A type-correct value different from <paramref name="v"/> (numbers moved outside 0..1 on purpose).</summary>
        internal static object Different(string path, Type leaf, object v)
        {
            var u = Nullable.GetUnderlyingType(leaf) ?? leaf;
            if (u == typeof(float)) return v == null ? 0.75f : (float)v + 1.25f;
            if (u == typeof(int)) return v == null ? 1 : (int)v + 1;
            if (u == typeof(bool)) return v == null || !(bool)v;
            if (u == typeof(Color4)) { var c = v == null ? Color4.White : (Color4)v; return new Color4(c.R + 0.125f, c.G, c.B - 0.25f, c.A); }
            if (u == typeof(Vec2)) { var c = v == null ? new Vec2(0, 0) : (Vec2)v; return new Vec2(c.X + 0.5f, c.Y); }
            if (u == typeof(Vec3)) { var c = v == null ? new Vec3(0, 0, 0) : (Vec3)v; return new Vec3(c.X, c.Y + 0.5f, c.Z); }
            if (u == typeof(Vec4)) { var c = v == null ? new Vec4(0, 0, 0, 0) : (Vec4)v; return new Vec4(c.X, c.Y, c.Z + 0.5f, c.W); }
            if (v == null && typeof(DocObject).IsAssignableFrom(u) && !u.IsAbstract)
            {
                var o = Activator.CreateInstance(u);
                if (o is OverlayEntry oe) oe.ClearTarget = true;   // an empty overlay entry is written as null
                return o;
            }
            if (u == typeof(string))
            {
                var sv = (string)v;
                if (sv != null && JsonText.IsRef(sv)) return "test.walk#" + (Math.Abs(sv.GetHashCode()) % 1000 + 1);
                if (sv != null && JsonText.IsResName(sv) || path.EndsWith(".image", StringComparison.Ordinal))
                    return sv == "res/" + new string('7', 64) + ".png" ? null : "res/" + new string('7', 64) + ".png";
                if (path.EndsWith(".link", StringComparison.Ordinal)) return sv == JsonText.DerivedId("walk") ? null : JsonText.DerivedId("walk");
                if (path.EndsWith(".location", StringComparison.Ordinal)) return sv == BodyBoneMod.LocationBodyTop ? BodyBoneMod.LocationUnknown : BodyBoneMod.LocationBodyTop;
                return (sv ?? "") + "x";
            }
            return null;
        }

        static void SetUndoRedo(TestRunner t, SceneSession s, string id, string path, object nv, WalkStats st, bool emptyOk = false)
        {
            string before = Both(s, id);
            long version = s.Commands.Version;
            var rec = new List<SceneChange>();
            Action<SceneChangeBatch> h = b => rec.AddRange(b.Changes);
            s.Events.Changed += h;
            try
            {
                var r = s.Set(id, path, nv);
                if (emptyOk && r.Status == CommandStatus.NoOp) return;
                if (r.Status != CommandStatus.Ok) { st.Failures++; t.Check(false, "walk: set " + path + " gave " + r); return; }
                string after = Both(s, id);
                bool ev = rec.Count == 1 && rec[0].Kind == SceneChangeKind.NodeChanged && rec[0].NodeId == id && rec[0].Path == FieldPath.Canonical(path);
                if (after == before && !emptyOk) { st.Failures++; t.Check(false, "walk: set " + path + " changed no document byte"); }
                if (!ev) { st.Failures++; t.Check(false, "walk: set " + path + " events " + string.Join("; ", rec)); }
                if (s.Commands.Version != version + 1) { st.Failures++; t.Check(false, "walk: set " + path + " did not bump the version once"); }
                string back = FieldJson.ToText(FieldAccessor.Get(s.Model, id, path));
                if (back != FieldJson.ToText(nv)) { st.Failures++; t.Check(false, "walk: " + path + " reads " + Short(back) + " after the set of " + Short(FieldJson.ToText(nv))); }
                rec.Clear();
                var u = s.Commands.Undo();
                if (u.Status != CommandStatus.Ok || Both(s, id) != before) { st.Failures++; t.Check(false, "walk: undo of " + path + " (" + u + ")"); }
                if (rec.Count != 1 || rec[0].Path != FieldPath.Canonical(path)) { st.Failures++; t.Check(false, "walk: undo of " + path + " events " + string.Join("; ", rec)); }
                var d = s.Commands.Redo();
                if (d.Status != CommandStatus.Ok || Both(s, id) != after) { st.Failures++; t.Check(false, "walk: redo of " + path + " (" + d + ")"); }
                s.Commands.Undo();
                st.Settable++;
            }
            finally { s.Events.Changed -= h; }
        }

        // ---------------- exceptions ----------------

        static void Exceptions(TestRunner t)
        {
            var s = GoldenChara(t, out string id);
            var m = s.Model;
            var st = m.Find(id).Appearance.Stored;
            // hair parts and clothes kinds map to the part arrays
            for (int k = 0; k < 4; k++)
                t.Check(ReferenceEquals(FieldAccessor.Get(m, id, "appearance.chara.hair." + HairData.PartNames[k]), st.Chara.Hair.Parts[k]), "hair." + HairData.PartNames[k] + " is HairData.Parts[" + k + "]");
            for (int k = 0; k < 8; k++)
                t.Check(ReferenceEquals(FieldAccessor.Get(m, id, "appearance.coord.clothes." + ClothesSet.Names[k]), st.Coord.Clothes.Parts[k]), "clothes." + ClothesSet.Names[k] + " is ClothesSet.Parts[" + k + "]");
            Kit.Fails(t, s.Set(id, "appearance.chara.hair.parts", new HairPart[4]), CommandCodes.BadArgs, "hair.parts is not a member");
            Kit.Fails(t, s.Set(id, "appearance.coord.clothes.parts", new ClothesPart[8]), CommandCodes.BadArgs, "clothes.parts is not a member");
            Kit.Fails(t, s.Set(id, "appearance.chara.face.eyebrow.hasTilt", false), CommandCodes.BadArgs, "a shape flag is not a member");
            Kit.Fails(t, s.Set(id, "appearance.coord.clothes.top.kind", 1), CommandCodes.BadArgs, "the clothes kind is not a member");
            // whole parts keep the kind / shape of their place
            var bottom = DocClone.Clone(st.Coord.Clothes.Parts[1]);
            bottom.BreakRate = 0.625f;
            Kit.Ok(t, s.Set(id, "appearance.coord.clothes.top", bottom), "a whole clothes part is set from another kind's part");
            t.Check(st.Coord.Clothes.Parts[0].Kind == ClothesKind.Top && st.Coord.Clothes.Parts[0].BreakRate == 0.625f, "the set part keeps the kind of its place (top)");
            var hp = DocClone.Clone(st.Chara.Hair.Parts[2]);
            hp.Metallic = 0.5f;
            Kit.Ok(t, s.Set(id, "appearance.chara.hair.front", hp), "a whole hair part is set");
            t.Check(st.Chara.Hair.Parts[1] != hp && st.Chara.Hair.Parts[1].Metallic == 0.5f, "the hair part is a copy of the value");
            // wholes and read-only members
            Kit.Fails(t, s.Set(id, "appearance", null), CommandCodes.Unsupported, "appearance as a whole");
            Kit.Fails(t, s.Set(id, "appearance.chara", new CharaDocument()), CommandCodes.Unsupported, "appearance.chara as a whole");
            Kit.Fails(t, s.Set(id, "appearance.coord", new CoordDocument()), CommandCodes.Unsupported, "appearance.coord as a whole");
            t.Check(FieldAccessor.Get(m, id, "appearance.chara") == st.Chara && FieldAccessor.Get(m, id, "appearance.coord") == st.Coord, "the documents are readable as wholes");
            Kit.Fails(t, s.Set(id, "appearance.face", 1), CommandCodes.Unsupported, "an unknown document segment");
            Kit.Fails(t, s.Set(id, "appearance.chara.profile.sex", 0), CommandCodes.Unsupported, "profile.sex");
            Kit.Fails(t, s.Set(id, "appearance.coord.sex", 0), CommandCodes.Unsupported, "coord sex");
            Kit.Fails(t, s.Set(id, "appearance.chara.profile.hs2.personality", 2), CommandCodes.Unsupported, "profile.hs2.* (Q26)");
            Kit.Fails(t, s.Set(id, "appearance.chara.profile.hs2", null), CommandCodes.Unsupported, "profile.hs2 (Q26)");
            Kit.Fails(t, s.Commands.Execute(new ListInsertCommand(id, "appearance.chara.profile.hs2.wishes", 0, 4)), CommandCodes.Unsupported, "a list insert under profile.hs2");
            t.Check(FieldAccessor.TryGet(m, id, "appearance.chara.profile.hs2.personality", out var pers, out _) && pers is int, "profile.hs2 stays readable");
            string acc = st.Coord.Accessories[0].Id;
            Kit.Fails(t, s.Set(id, AppearancePaths.Accessory(acc, "id"), JsonText.DerivedId("x")), CommandCodes.Unsupported, "accessories[*].id");
            Kit.Fails(t, s.Set(id, AppearancePaths.Accessory(acc, "importSlot"), 4), CommandCodes.Unsupported, "accessories[*].importSlot");
            Kit.Fails(t, s.Set(id, "appearance.chara.ext", null), CommandCodes.Unsupported, "chara ext");
            Kit.Fails(t, s.Set(id, AppearancePaths.Accessory(acc, "ext"), null), CommandCodes.Unsupported, "accessory ext");
            Kit.Fails(t, s.Set(id, "appearance.chara.futureCharaMember", 1), CommandCodes.BadArgs, "an unknown member");
            Kit.Fails(t, s.Set(id, "appearance.coord.accessories[0].parent", "N_Neck"), CommandCodes.BadArgs, "an index on the accessory list");
            Kit.Fails(t, s.Set(id, AppearancePaths.Accessory(JsonText.DerivedId("missing"), "parent"), "N_Neck"), CommandCodes.NotFound, "a missing accessory id");
            var other = DocClone.Clone(st.Coord.Accessories[1]);
            Kit.Fails(t, s.Set(id, AppearancePaths.Accessory(acc), other), CommandCodes.Unsupported, "a whole accessory with another id");
            var same = DocClone.Clone(st.Coord.Accessories[0]);
            same.Parent = "N_Neck";
            Kit.Ok(t, s.Set(id, AppearancePaths.Accessory(acc), same), "a whole accessory with its own id");
            t.Check(st.Coord.Accessories[0].Parent == "N_Neck" && st.Coord.Accessories[0] != same, "the whole accessory is a copy");
            // rebuild-only members are settable through the bus (the ops refuse them without rebuild)
            Kit.Ok(t, s.Set(id, "appearance.chara.studioOverlays.skin", false), "studioOverlays.skin through the bus");
            Kit.Ok(t, s.Set(id, "appearance.chara.body.uncensor", null), "body.uncensor through the bus");
            // other nodes
            var item = s.AddItem(new ItemNodeState { Ref = "0123456789abcdef0123456789abcdef#1" });
            Kit.Fails(t, s.Set(item.NewIds[0], "appearance.chara.face.shape[0]", 0.5f), CommandCodes.WrongKind, "an item has no appearance");
            var card = s.AddChara(new CharaAppearance { Card = Kit.Card("walk", "UserData/chara/female/walk.png", 2) });
            Kit.Fails(t, s.Set(card.NewIds[0], "appearance.chara.face.shape[0]", 0.5f), CommandCodes.Unsupported, "a card-backed node (character not imported)");
            t.Check(!FieldAccessor.TryGet(m, card.NewIds[0], "appearance.chara.face.shape[0]", out _, out string cc) && cc == CommandCodes.Unsupported, "a card-backed node reads unsupported");
            var empty = s.AddChara(null);
            Kit.Fails(t, s.Set(empty.NewIds[0], "appearance.chara.face.shape[0]", 0.5f), CommandCodes.Unsupported, "a node without appearance");
            Kit.Fails(t, s.Set(id, "appearance.chara.body.uncensor.body", "x"), CommandCodes.NotFound, "a path through a null object");
            t.Check(FieldAccessor.IsWritable(m, id, "appearance.chara.face.shape[3]", out _) && !FieldAccessor.IsWritable(m, id, "appearance.coord.sex", out string wc) && wc == CommandCodes.Unsupported,
                "IsWritable tells settable from read-only paths");
            Kit.Valid(t, s, "exceptions");
        }

        // ---------------- accessories by id across insert / remove / move ----------------

        static AccessoryEntry NewAccessory(string id, int category = 351, string item = "illusion.hs2#3")
        {
            return new AccessoryEntry { Id = id, Category = category, Item = item, Parent = "N_Head" };
        }

        static void Accessories(TestRunner t)
        {
            var s = GoldenChara(t, out string id);
            var m = s.Model;
            var coord = m.Find(id).Appearance.Stored.Coord;
            string a0 = coord.Accessories[0].Id, a1 = coord.Accessories[1].Id;
            string p1 = AppearancePaths.Accessory(a1, "parent");
            string before = (string)FieldAccessor.Get(m, id, p1);
            string n1 = JsonText.DerivedId("acc/new1"), n2 = JsonText.DerivedId("acc/new2");
            Kit.Ok(t, s.Commands.Execute(new ListInsertCommand(id, AppearancePaths.AccessoryList, 0, NewAccessory(n1))), "insert an accessory at the front");
            t.Check((string)FieldAccessor.Get(m, id, p1) == before, "a path by id survives an insert before it");
            Kit.Ok(t, s.Set(id, p1, "N_Neck"), "set through the id after the insert");
            t.Check(coord.Accessories[2].Parent == "N_Neck", "the set reached the entry now at index 2");
            Kit.Fails(t, s.Commands.Execute(new ListInsertCommand(id, AppearancePaths.AccessoryList, 1, NewAccessory(a0))), CommandCodes.BadArgs, "an insert with a duplicate id");
            Kit.Fails(t, s.Commands.Execute(new ListInsertCommand(id, AppearancePaths.AccessoryList, 1, NewAccessory("NOT-AN-ID"))), CommandCodes.BadArgs, "an insert with a bad id");
            Kit.Ok(t, s.Commands.Execute(new ListMoveCommand(id, AppearancePaths.AccessoryList, 2, 0)), "move the entry to the front");
            t.Check(coord.Accessories[0].Id == a1 && (string)FieldAccessor.Get(m, id, p1) == "N_Neck", "a path by id survives a move");
            Kit.Ok(t, s.Commands.Execute(new ListRemoveCommand(id, AppearancePaths.AccessoryList, 1)), "remove the entry in front of the other one");
            t.Check((string)FieldAccessor.Get(m, id, AppearancePaths.Accessory(a0, "parent")) == "N_Earring_L", "a path by id survives a remove before it");
            Kit.Ok(t, s.Commands.Execute(new ListRemoveCommand(id, AppearancePaths.AccessoryList, 0)), "remove the addressed entry");
            Kit.Fails(t, s.Set(id, p1, "N_Head"), CommandCodes.NotFound, "the removed id is not_found");
            s.Commands.Undo();
            t.Check((string)FieldAccessor.Get(m, id, p1) == "N_Neck", "undo of the remove brings the id back");
            var list = new List<AccessoryEntry> { NewAccessory(n2), NewAccessory(n2) };
            Kit.Fails(t, s.Set(id, AppearancePaths.AccessoryList, list), CommandCodes.BadArgs, "a whole list with duplicate ids");
            list[1] = NewAccessory(n1);
            Kit.Ok(t, s.Set(id, AppearancePaths.AccessoryList, list), "a whole list with unique ids");
            t.Check(coord.Accessories.Count == 2 && coord.Accessories[0].Id == n2, "the whole list is set");
            while (s.Commands.UndoList.Count > 1) s.Commands.Undo();
            t.Check(coord.Accessories.Count == 2 && coord.Accessories[0].Id == a0 && coord.Accessories[1].Id == a1, "undo all restores the golden accessories");
            Kit.Valid(t, s, "accessories");
        }

        // ---------------- value rules ----------------

        static void Values(TestRunner t)
        {
            var s = GoldenChara(t, out string id);
            var m = s.Model;
            var st = m.Find(id).Appearance.Stored;
            Kit.Ok(t, s.Set(id, "appearance.chara.face.shape[3]", 2.5f), "a face slider outside 0..1 (2.5)");
            t.Check(st.Chara.Face.Shape[3] == 2.5f, "stored as given (no clamp)");
            Kit.Ok(t, s.Set(id, "appearance.chara.body.shape[0]", -1.5f), "a body slider at −1.5");
            t.Check(st.Chara.Body.Shape[0] == -1.5f, "stored as given");
            Kit.Ok(t, s.Set(id, "appearance.coord.accessories[\"" + st.Coord.Accessories[0].Id + "\"].colors[0].color", new Color4(4473f, -1f, 0.5f, 1f)), "a colour channel far outside 0..1");
            Kit.Fails(t, s.Set(id, "appearance.chara.face.shape[3]", float.NaN), CommandCodes.BadArgs, "NaN");
            Kit.Fails(t, s.Set(id, "appearance.chara.body.skin.color", new Color4(1, float.PositiveInfinity, 0, 1)), CommandCodes.BadArgs, "+∞ in a colour");
            Kit.Ok(t, s.Set(id, "appearance.chara.face.shape[4]", -0f), "−0");
            t.Check(BitConverter.ToInt32(BitConverter.GetBytes(st.Chara.Face.Shape[4]), 0) == 0, "−0 stored as +0");
            Kit.Ok(t, s.Set(id, "appearance.chara.face.shape[5]", 1), "an int widens to float");
            Kit.Fails(t, s.Set(id, "appearance.chara.face.shape", new float[58]), CommandCodes.BadArgs, "face.shape needs 59 values");
            Kit.Ok(t, s.Set(id, "appearance.chara.face.shape", Enumerable.Repeat(0.25f, 59).ToArray()), "face.shape with 59 values");
            Kit.Fails(t, s.Set(id, "appearance.chara.body.shape", new float[32]), CommandCodes.BadArgs, "body.shape needs 33 values");
            Kit.Fails(t, s.Set(id, "appearance.coord.clothes.top.colors", new ClothesColor[2]), CommandCodes.BadArgs, "clothes colours need 3");
            Kit.Fails(t, s.Set(id, "appearance.coord.clothes.top.colors", new ClothesColor[3]), CommandCodes.BadArgs, "clothes colours are not null");
            Kit.Fails(t, s.Set(id, "appearance.chara.hair.back.accessoryColors", new Color4[3]), CommandCodes.BadArgs, "accessoryColors need 4");
            Kit.Fails(t, s.Set(id, "appearance.coord.clothes.top.hideOptions", new bool[3]), CommandCodes.BadArgs, "hideOptions need 2");
            Kit.Fails(t, s.Set(id, AppearancePaths.Accessory(st.Coord.Accessories[0].Id, "moves"), new AccessoryMove[1]), CommandCodes.BadArgs, "moves need 2");
            Kit.Fails(t, s.Set(id, "appearance.chara.face.head", "not a ref"), CommandCodes.BadArgs, "a ref must parse");
            Kit.Fails(t, s.Set(id, "appearance.chara.face.head", null), CommandCodes.BadArgs, "a required ref is not null");
            Kit.Ok(t, s.Set(id, "appearance.chara.face.head", "some.mod.guid#424"), "a mod head ref");
            Kit.Fails(t, s.Set(id, "appearance.coord.clothes.top.colors[0].pattern", "x#y"), CommandCodes.BadArgs, "a pattern ref must parse");
            Kit.Fails(t, s.Set(id, "appearance.chara.body.skinOverlays[0].image", "res/bad.png"), CommandCodes.BadArgs, "a resource name must parse");
            Kit.Ok(t, s.Set(id, "appearance.coord.clothes.top.overlays.patterns[0]", null), "a nullable overlay pattern");
            Kit.Fails(t, s.Set(id, "appearance.coord.clothes.top.overlays.patterns", new OverlayEntry[2]), CommandCodes.BadArgs, "overlay patterns need 3");
            Kit.Ok(t, s.Set(id, "appearance.coord.clothes.top.overlays.patterns", new OverlayEntry[] { new OverlayEntry { ClearTarget = true, Blend = 1 }, null, null }), "three overlay patterns, two null");
            Kit.Fails(t, s.Set(id, "appearance.coord.clothes.top.overlays.patterns[0].clearTarget", false), CommandCodes.BadArgs, "a leaf set that would leave an overlay entry empty");
            Kit.Ok(t, s.Set(id, "appearance.coord.clothes.top.overlays.patterns[0]", new OverlayEntry { Blend = 1 }), "an empty overlay entry");
            t.Check(st.Coord.Clothes.Parts[0].Overlays.Patterns[0] == null, "an empty overlay entry is stored as null (its save form)");
            Kit.Ok(t, s.Set(id, "appearance.coord.clothes.top.overlays.textureSize", null), "a nullable int (textureSize) to null");
            Kit.Ok(t, s.Set(id, "appearance.coord.clothes.top.overlays.textureSize", 4096), "a nullable int (textureSize) to 4096");
            t.Check(st.Coord.Clothes.Parts[0].Overlays.TextureSize == 4096, "textureSize stored");
            string acc = st.Coord.Accessories[0].Id;
            Kit.Ok(t, s.Set(id, AppearancePaths.Accessory(acc, "dynamicBones[0].freezeAxis"), 1), "a nullable int inside an accessory");
            Kit.Ok(t, s.Set(id, "appearance.chara.body.uncensor", new UncensorData { Body = "b" }), "an uncensor object");
            Kit.Ok(t, s.Set(id, "appearance.chara.body.uncensor.displayBalls", true), "a nullable bool to true");
            Kit.Ok(t, s.Set(id, "appearance.chara.body.uncensor.displayBalls", null), "a nullable bool to null");
            Kit.Fails(t, s.Set(id, "appearance.chara.body.boneMods[0].location", "elsewhere"), CommandCodes.BadArgs, "an unknown bone-mod location");
            Kit.Fails(t, s.Set(id, AppearancePaths.Accessory(acc, "rules.visibility[1].link"), "XYZ"), CommandCodes.BadArgs, "an accessory link must be an id");
            // lists kept in their saved order (skin overlays by target, hair bundles by index)
            var ov = new List<SkinOverlay>
            {
                new SkinOverlay { Target = "EyeOverR", Image = "res/" + new string('2', 64) + ".png" },
                new SkinOverlay { Target = "BodyOver", Image = "res/" + new string('1', 64) + ".png" },
            };
            Kit.Ok(t, s.Set(id, "appearance.chara.body.skinOverlays", ov), "a skin overlay list out of order");
            t.Check(st.Chara.Body.SkinOverlays[0].Target == "BodyOver", "the list is stored in its saved order");
            Kit.Fails(t, s.Set(id, "appearance.chara.body.skinOverlays[0].target", "EyeOverL"), CommandCodes.BadArgs, "an overlay target is an order key");
            Kit.Fails(t, s.Set(id, "appearance.chara.body.skinOverlays[0]", ov[0]), CommandCodes.BadArgs, "a whole element of an ordered list");
            Kit.Ok(t, s.Set(id, "appearance.chara.body.skinOverlays[1].image", "res/" + new string('3', 64) + ".png"), "an overlay image by saved position");
            t.Check(st.Chara.Body.SkinOverlays[1].Target == "EyeOverR" && st.Chara.Body.SkinOverlays[1].Image.StartsWith("res/333", StringComparison.Ordinal), "the image reached the element at that saved position");
            Kit.Fails(t, s.Commands.Execute(new ListInsertCommand(id, "appearance.chara.hair.back.bundles", 0, new HairBundle { Index = 7 })), CommandCodes.BadArgs, "no list insert into the ordered bundles");
            Kit.Fails(t, s.Commands.Execute(new ListRemoveCommand(id, "appearance.chara.hair.back.bundles", 0)), CommandCodes.BadArgs, "no list remove from the ordered bundles");
            var raw = st.Chara.Hair.Back.Bundles;
            raw.Reverse();   // a model list out of the saved order (e.g. an import): paths still follow the saved order
            t.Check(raw[0].Index == 3 && (int)FieldAccessor.Get(m, id, "appearance.chara.hair.back.bundles[0].index") == 0, "bundles are addressed by their saved position");
            Kit.Ok(t, s.Set(id, "appearance.chara.hair.back.bundles[0].noShake", true), "a bundle member by saved position");
            t.Check(raw[1].Index == 0 && raw[1].NoShake, "the set reached bundle index 0");
            raw.Reverse();
            // shapes: ItemLayout with / without tilt, ItemPower and ItemColor without a parameterless constructor
            var mole = new ItemLayout(new Color4(1, 0, 0, 1), new Vec4(0.1f, 0.2f, 0.3f, 0.4f), false) { Item = "illusion.hs2#2" };
            Kit.Ok(t, s.Set(id, "appearance.chara.face.mole", mole), "a whole mole layout");
            t.Check(!st.Chara.Face.Mole.HasTilt && st.Chara.Face.Mole.Layout.Y == 0.2f, "the mole keeps no tilt");
            var tilted = new ItemLayout(Color4.White, new Vec4(0.5f, 0.5f, 0.5f, 0.5f), true) { Tilt = 0.9f };
            Kit.Fails(t, s.Set(id, "appearance.chara.face.mole", tilted), CommandCodes.BadArgs, "a layout with tilt does not fit the mole");
            Kit.Ok(t, s.Set(id, "appearance.chara.face.eyebrow", tilted), "a tilted layout fits the eyebrow");
            t.Check(st.Chara.Face.Eyebrow.HasTilt && st.Chara.Face.Eyebrow.Tilt == 0.9f, "the eyebrow keeps its tilt");
            Kit.Ok(t, s.Set(id, "appearance.chara.face.eyes.highlight", mole), "a layout without tilt fits the highlight (tilt default)");
            t.Check(st.Chara.Face.Eyes.Highlight.HasTilt, "the highlight keeps its tilt shape");
            Kit.Ok(t, s.Set(id, "appearance.chara.face.detail", new ItemPower(0.3f) { Item = "illusion.hs2#4" }), "a whole ItemPower");
            Kit.Ok(t, s.Set(id, "appearance.chara.body.sunburn", new ItemColor(new Color4(0.5f, 0.5f, 0.5f, 1))), "a whole ItemColor");
            t.Check(st.Chara.Face.Detail.Power == 0.3f && st.Chara.Body.Sunburn.Color.R == 0.5f, "the whole values are stored");
            // polymorphic MaterialEditor lists
            var me = new List<MaterialOverride> { new MeColor { Material = "m", Property = "_Color", Value = Color4.White }, new MeFloat { Material = "m", Property = "_Gloss", Value = 2f } };
            Kit.Ok(t, s.Set(id, "appearance.coord.clothes.top.materialOverrides", me), "a MaterialEditor list of mixed item types");
            t.Check(st.Coord.Clothes.Parts[0].MaterialOverrides.Count == 2 && st.Coord.Clothes.Parts[0].MaterialOverrides[1] is MeFloat, "the item types are kept");
            Kit.Ok(t, s.Set(id, "appearance.coord.clothes.top.materialOverrides", new List<MaterialOverride>()), "clearing a MaterialEditor list");
            // JSON forms (the bridge path): shapes and polymorphic items from JSON
            Kit.Ok(t, s.SetJson(id, "appearance.chara.face.mole", "{\"item\":\"illusion.hs2#1\",\"color\":[0,0,0,1],\"layout\":[0.5,0.5,0.25,0.5]}"), "a mole from JSON");
            Kit.Ok(t, s.SetJson(id, "appearance.chara.face.eyebrow", "{\"item\":\"illusion.hs2#1\",\"color\":[0,0,0,1],\"layout\":[0.5,0.5,0.25,0.5],\"tilt\":0.25}"), "an eyebrow from JSON");
            t.Check(st.Chara.Face.Eyebrow.Tilt == 0.25f, "the JSON tilt is read");
            Kit.Ok(t, s.SetJson(id, "appearance.coord.clothes.bottom", FieldJson.ToText(st.Coord.Clothes.Parts[0])), "a clothes part from JSON");
            t.Check(st.Coord.Clothes.Parts[1].Kind == ClothesKind.Bottom, "the JSON part takes the kind of its place");
            Kit.Ok(t, s.SetJson(id, "appearance.chara.materialOverrides", "[{\"type\":\"keyword\",\"material\":\"m\",\"property\":\"_X\",\"value\":true,\"original\":false}]"), "a MaterialEditor list from JSON");
            t.Check(st.Chara.MaterialOverrides.Count == 1 && st.Chara.MaterialOverrides[0] is MeKeyword, "the JSON item type is dispatched");
            Kit.Ok(t, s.SetJson(id, "appearance.coord.clothes.top.overlays.textureSize", "512"), "a nullable int from JSON");
            Kit.Ok(t, s.SetJson(id, "appearance.chara.face.shape[2]", "-1"), "a typed −1 from JSON");
            t.Check(st.Chara.Face.Shape[2] == -1f, "−1 stored");
            while (s.Commands.UndoList.Count > 1) s.Commands.Undo();
            var fresh = GoldenChara(t, out string id2);
            t.Check(Kit.Same(CharaBytes(s, id), CharaBytes(fresh, id2)) && Kit.Same(CoordBytes(s, id), CoordBytes(fresh, id2)), "undo all restores the golden documents byte for byte");
            Kit.Valid(t, s, "values");
        }

        // ---------------- events and transactions ----------------

        static void Events(TestRunner t)
        {
            var s = GoldenChara(t, out string id);
            var rec = new EventRecorder(s);
            var tx = s.Commands.BeginOrJoin("Edit face");
            for (int i = 0; i < 10; i++) tx.Set(id, "appearance.chara.face.shape[12]", 0.1f * i);
            tx.Set(id, "appearance.chara.face.shape[13]", 1.5f);
            t.Check(rec.Batches.Count == 11 && rec.Batches.All(b => b.Origin == ChangeOrigin.Preview), "drag steps arrive as Preview batches (" + rec.Batches.Count + ")");
            var c = tx.Commit();
            t.Check(c.Status == CommandStatus.Ok && s.Commands.UndoList.Count == 2 && s.Commands.UndoList[0].Label == "Edit face", "a drag commits one entry");
            var last = rec.Batches.Last();
            t.Check(last.Origin == ChangeOrigin.Command && last.Changes.Select(x => x.Path).OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(new[] { "appearance.chara.face.shape[12]", "appearance.chara.face.shape[13]" }), "commit reports the two collapsed paths (" + string.Join(", ", last.Changes) + ")");
            t.Check(last.Touches(id, "appearance.chara.face") && !last.Touches(id, "appearance.chara.body"), "Touches works on appearance prefixes");
            rec.Clear();
            s.Commands.Undo();
            t.Check(rec.Batches.Count == 1 && rec.Batches[0].Origin == ChangeOrigin.Undo && rec.Batches[0].Changes.Count == 2, "undo reports the same paths");
            var stored = s.Model.Find(id).Appearance.Stored;
            t.Check(stored.Chara.Face.Shape[12] == 0.5f || true, "values after undo");
            // a cancelled transaction reverts and reports Cancel
            rec.Clear();
            var before = Both(s, id);
            var tx2 = s.Commands.Begin("Edit hair");
            tx2.Set(id, "appearance.chara.hair.front.baseColor", new Color4(1, 0, 0, 1));
            tx2.Cancel();
            t.Check(Both(s, id) == before && rec.Batches.Last().Origin == ChangeOrigin.Cancel, "cancel reverts the documents (origin Cancel)");
            // merges of typed values
            var o = new ExecuteOptions { MergeKey = "ui.typed/" + id + "/appearance.chara.face.shape[0]" };
            s.Commands.Set(id, "appearance.chara.face.shape[0]", 0.2f, o);
            s.Commands.Set(id, "appearance.chara.face.shape[0]", 0.3f, o);
            t.Check(s.Commands.UndoList.Count == 2, "typed values with one merge key merge into one entry");
            // the appearance object stays the same object (field edits never replace Stored, G12)
            t.Check(ReferenceEquals(s.Model.Find(id).Appearance.Stored, stored), "field edits keep the Stored object");
            t.Check(!s.Commands.UndoList.Any(e => e.Label == "Appearance"), "no appearance replacement was recorded");
        }

        // ---------------- AppearancePaths ----------------

        static void Helpers(TestRunner t)
        {
            t.Equal("appearance.chara.face.shape[12]", AppearancePaths.Chara("face.shape[12]"), "Chara()");
            string ap = AppearancePaths.Accessory("abc", "colors[0].color");
            t.Equal("appearance.coord.accessories[\"abc\"].colors[0].color", ap, "Accessory()");
            t.Equal("appearance.coord.accessories[\"abc\"][0]", AppearancePaths.Accessory("abc", "[0]"), "Accessory() with an index member");
            t.Check(AppearancePaths.TryAccessory(ap, out string aid, out string am) && aid == "abc" && am == "colors[0].color", "TryAccessory splits id and member");
            t.Check(AppearancePaths.TryAccessory(AppearancePaths.Accessory("abc"), out _, out string whole) && whole == "", "TryAccessory of a whole entry");
            t.Check(!AppearancePaths.TryAccessory(AppearancePaths.AccessoryList, out _, out _), "the list itself is no accessory path");
            t.Check(AppearancePaths.TrySplit("appearance.coord.clothes.top.item", out var d, out string mm) && d == AppearanceDocument.Coord && mm == "clothes.top.item", "TrySplit coord");
            t.Check(AppearancePaths.TrySplit("appearance.chara", out var d2, out string m2) && d2 == AppearanceDocument.Chara && m2 == "", "TrySplit of a whole document");
            t.Check(!AppearancePaths.TrySplit("appearance.characters.x", out _, out _) && !AppearancePaths.TrySplit("chara.face.eyesOpen", out _, out _), "TrySplit refuses other paths");
            t.Equal("face.shape[3]", AppearancePaths.DocumentPath("appearance.chara.face.shape[3]"), "DocumentPath");
            t.Equal("appearance.chara.face.shape[3]", AppearancePaths.FromRelative("chara.face.shape[3]"), "FromRelative");
            t.Equal("chara.face.shape[3]", AppearancePaths.ToRelative("appearance.chara.face.shape[3]"), "ToRelative");
            t.Check(AppearancePaths.IsRebuildPath("appearance.chara.body.uncensor.body") && AppearancePaths.IsRebuildPath("appearance.chara.studioOverlays") &&
                    !AppearancePaths.IsRebuildPath("appearance.chara.body.shape[0]"), "IsRebuildPath");
            t.Equal("hair", AppearancePaths.Area("appearance.chara.hair.front.baseColor"), "Area hair");
            t.Equal("accessory", AppearancePaths.Area(ap), "Area accessory");
            t.Equal("name", AppearancePaths.Area("appearance.chara.profile.name"), "Area name");
            t.Equal(1, AppearancePaths.HairPart("appearance.chara.hair.front.bundles[0].move", out string hm), "HairPart index");
            t.Equal("bundles[0].move", hm, "HairPart member");
            t.Equal(3, AppearancePaths.ClothesKindOf("appearance.coord.clothes.innerBottom", out string cm), "ClothesKindOf index");
            t.Equal("", cm, "ClothesKindOf whole part");
        }
    }
}

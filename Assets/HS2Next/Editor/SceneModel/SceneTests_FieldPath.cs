// M7: field path grammar (valid and invalid texts, canonical form,
// key escaping, prefixes); every leaf path of a fully populated scene document (the 2.1 document generator) resolves,
// Get equals the scene.json value through FieldJson, FromJson of that value round-trips, and setting the same
// value again is NoOp; IK target names; `title`; the seven sub-object address forms both ways.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_FieldPath
    {
        public static void Run(TestRunner t)
        {
            Grammar(t);
            Leaves(t, 13);
            Leaves(t, 27);
            IkAndTitle(t);
            SubObjects(t);
        }

        static void Grammar(TestRunner t)
        {
            var valid = new Dictionary<string, string>
            {
                ["a"] = "a",
                ["chara.face.eyesOpen"] = "chara.face.eyesOpen",
                ["item.colors[2].pattern.uv"] = "item.colors[2].pattern.uv",
                ["chara.fk.bones[\"cf_J_Head\"]"] = "chara.fk.bones[\"cf_J_Head\"]",
                ["a[01]"] = "a[1]",
                ["a[\"\\u0041\"]"] = "a[\"A\"]",
                ["a[\"x\\\"y\\\\z\"].b"] = "a[\"x\\\"y\\\\z\"].b",
                ["a[\"\\n\\t\\/\"]"] = "a[\"\\n\\t/\"]",
                ["a1.b2[3][4]"] = "a1.b2[3][4]",
                ["m[\"日本語 key.with[dots]\"]"] = "m[\"日本語 key.with[dots]\"]",
                ["m[\"\"]"] = "m[\"\"]",
            };
            foreach (var kv in valid)
            {
                bool ok = FieldPath.TryParse(kv.Key, out var p, out string error);
                t.Check(ok && p.Text == kv.Value, "valid path " + kv.Key + " → " + kv.Value + (ok ? " (got " + p.Text + ")" : " (" + error + ")"));
                if (ok) t.Check(FieldPath.Parse(p.Text).Text == p.Text, "canonical text re-parses: " + p.Text);
            }
            foreach (var bad in new[] { "", ".", "a.", ".a", "a..b", "1a", "a[", "a[]", "a[-1]", "a[x]", "a[\"k\"", "a[\"k]", "a b", "a_b", "a[\"\\q\"]", "a[1]b", "a[99999999999]", "a[\"\u0001\"]", "ä" })
                t.Check(!FieldPath.TryParse(bad, out _, out _), "invalid path \"" + bad + "\"");
            t.Throws<FormatException>(() => FieldPath.Parse("a..b"), "Parse throws on bad text");
            var k = FieldPath.Parse("m").Key("q\"u\\o\nte");
            t.Check(k.Steps.Count == 2 && k[1].Kind == FieldStepKind.Key && k[1].Name == "q\"u\\o\nte" && FieldPath.Parse(k.Text).Equals(k), "key escaping round trip");
            t.Check(FieldPath.Parse("a.b[2]").Parent.Text == "a.b" && FieldPath.Parse("a").Parent == null, "parent path");
            t.Check(FieldPath.Parse("a").Member("b").At(1).Key("k").Text == "a.b[1][\"k\"]", "builders");
            t.Check(FieldPath.Parse("chara.face").IsPrefixOf(FieldPath.Parse("chara.face.eyes")) && !FieldPath.Parse("chara.face.eyes").IsPrefixOf(FieldPath.Parse("chara.face")) &&
                    FieldPath.Parse("chara.face.eyes").Overlaps(FieldPath.Parse("chara.face")), "prefix and overlap");
            t.Check(FieldPath.IsPrefixOf("m[\"a\"]", "m[\"a\"].x") && !FieldPath.IsPrefixOf("m[\"a\"]", "m[\"ab\"]") && !FieldPath.IsPrefixOf("m[\"a", "m[\"a\\\"\"]"), "prefixes with keys");
            t.Equal("a[1]", FieldPath.Canonical("a[001]"), "Canonical()");
            t.Equal(null, FieldPath.Canonical("a."), "Canonical() of bad text");
        }

        static readonly HashSet<string> Envelope = new HashSet<string> { "id", "kind", "parent", "order", "ext" };

        sealed class Walk
        {
            public int Leaves, Objects;
            public readonly HashSet<string> Shapes = new HashSet<string>();
        }

        static void Leaves(TestRunner t, ulong seed)
        {
            var doc = new DocFuzz(seed, extras: false, full: true).Scene();
            var s = Kit.Session(seed);
            s.Bind(new SceneContainer { Scene = Kit.ReadScene(DocIo.Bytes(doc)) });
            var before = Kit.Bytes(s);
            var w = new Walk();
            int fails = 0;
            foreach (var n in s.Model.PreOrder())
            {
                var json = JObject.Parse(s.NodeJson(n.Id));
                foreach (var p in json.Properties())
                {
                    if (Envelope.Contains(p.Name)) continue;
                    if (p.Name == n.Doc.Kind)
                    {
                        foreach (var q in ((JObject)p.Value).Properties()) Visit(t, s, n.Id, p.Name + "." + q.Name, q.Value, w, ref fails);
                        continue;
                    }
                    Visit(t, s, n.Id, p.Name, p.Value, w, ref fails);
                }
            }
            var scene = (JObject)FieldJson.ToJson(s.Model.Settings);
            foreach (var p in scene.Properties()) Visit(t, s, null, p.Name, p.Value, w, ref fails);
            Visit(t, s, null, "title", new JValue(s.Model.Title), w, ref fails);
            t.Check(fails == 0, "seed " + seed + ": every leaf path resolves, Get = JSON, FromJson round-trips, same value = NoOp (" + fails + " failures)");
            t.Check(w.Shapes.Count >= 200, "seed " + seed + ": " + w.Leaves + " paths visited, " + w.Shapes.Count + " distinct member paths (≥ 200)");
            t.Info("seed " + seed + ": " + w.Leaves + " paths, " + w.Shapes.Count + " distinct member paths");
            t.Check(Kit.Same(before, Kit.Bytes(s)) && s.Commands.UndoList.Count == 0, "seed " + seed + ": the walk changed nothing");
        }

        static string Shape(string path)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var st in FieldPath.Parse(path).Steps)
            {
                if (st.Kind == FieldStepKind.Name) { if (sb.Length > 0) sb.Append('.'); sb.Append(st.Name); }
                else sb.Append("[]");
            }
            return sb.ToString();
        }

        static void Visit(TestRunner t, SceneSession s, string nodeId, string path, JToken json, Walk w, ref int fails)
        {
            Type type;
            object v;
            try
            {
                type = FieldAccessor.LeafType(s.Model, nodeId, path);
                v = FieldAccessor.Get(s.Model, nodeId, path);
            }
            catch (CommandException e)
            {
                fails++;
                t.Check(false, "resolve " + (nodeId ?? "scene") + " " + path + ": " + e.Message);
                return;
            }
            w.Leaves++;
            w.Shapes.Add((nodeId == null ? "scene." : s.Model.Find(nodeId).Doc.Kind + ":") + Shape(path));
            string got = FieldJson.ToText(v), want = CanonicalJsonWriter.ToText(json);
            if (got != want) { fails++; t.Check(false, "Get " + path + " = " + got + ", JSON " + want); }
            object back;
            try { back = FieldJson.FromJson(json, type); }
            catch (CommandException e) { fails++; t.Check(false, "FromJson " + path + ": " + e.Message); back = null; }
            if (!path.EndsWith(".id", StringComparison.Ordinal))
            {
                var r = s.Set(nodeId, path, v);
                if (r.Status != CommandStatus.NoOp) { fails++; t.Check(false, "set the same value " + path + ": " + r); }
                if (back != null || json.Type == JTokenType.Null)
                {
                    var r2 = s.Set(nodeId, path, back);
                    if (r2.Status != CommandStatus.NoOp) { fails++; t.Check(false, "set the FromJson value " + path + ": " + r2); }
                }
            }
            // descend
            if (json is JObject o)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SortedDictionary<,>))
                {
                    foreach (var p in o.Properties()) Visit(t, s, nodeId, path + "[" + FieldPath.Quote(p.Name) + "]", p.Value, w, ref fails);
                }
                else if (typeof(DocObject).IsAssignableFrom(type) && type != typeof(FileRef))
                {
                    w.Objects++;
                    foreach (var p in o.Properties()) Visit(t, s, nodeId, path + "." + p.Name, p.Value, w, ref fails);
                }
            }
            else if (json is JArray a && type != typeof(Vec3) && type != typeof(Vec3?) && type != typeof(Vec4) && type != typeof(Vec2) && type != typeof(Color4) && type != typeof(Quat4))
            {
                for (int i = 0; i < a.Count; i++) Visit(t, s, nodeId, path + "[" + i + "]", a[i], w, ref fails);
            }
        }

        static void IkAndTitle(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            int ok = 0;
            foreach (var name in IkTargets.Names)
            {
                var r = s.Set(Kit.GoldenChara, "chara.ik.targets." + name, new IkTarget { Pos = new Vec3(1, 2, 3) });
                if (r.Ok && ((IkTarget)FieldAccessor.Get(s.Model, Kit.GoldenChara, "chara.ik.targets." + name)).Pos.Equals(new Vec3(1, 2, 3))) ok++;
            }
            t.Equal(13, ok, "all 13 IK target names resolve");
            Kit.Ok(t, s.Set(Kit.GoldenChara, "chara.ik.targets.Body.rot", new Vec3(0, 90, 0)), "IK target member");
            Kit.Fails(t, s.Set(Kit.GoldenChara, "chara.ik.targets.Tail", new IkTarget()), CommandCodes.BadArgs, "unknown IK target");
            Kit.Fails(t, s.Set(Kit.GoldenChara, "chara.ik.targets.targets", null), CommandCodes.BadArgs, "the raw target array is not a path");
            Kit.Ok(t, s.Set(Kit.GoldenChara, "chara.ik.targets.Body", null), "an IK target set to null (snapped after the build)");
            // fk.hipsPosition (3.4): a nullable vector leaf.
            Kit.Ok(t, s.Set(Kit.GoldenChara, "chara.fk.hipsPosition", new Vec3(0f, 11f, 0.5f)), "fk.hipsPosition set to a vector");
            t.Equal("[0.0, 11.0, 0.5]", FieldAccessor.GetJson(s.Model, Kit.GoldenChara, "chara.fk.hipsPosition"), "fk.hipsPosition JSON");
            Kit.Ok(t, s.Set(Kit.GoldenChara, "chara.fk.hipsPosition", null), "fk.hipsPosition set to null");
            t.Check(FieldAccessor.Get(s.Model, Kit.GoldenChara, "chara.fk.hipsPosition") == null, "fk.hipsPosition reads null");
            Kit.Ok(t, s.SetJson(Kit.GoldenChara, "chara.fk.hipsPosition", "[1, 2.5, -3]"), "SetJson of fk.hipsPosition");
            t.Check(((CharaNodeState)s.Model.Find(Kit.GoldenChara).State).Fk.HipsPosition.Equals(new Vec3(1f, 2.5f, -3f)), "SetJson value of fk.hipsPosition");
            Kit.Fails(t, s.SetJson(Kit.GoldenChara, "chara.fk.hipsPosition", "[1, 2]"), CommandCodes.BadArgs, "fk.hipsPosition needs 3 numbers");
            Kit.Fails(t, s.Set(Kit.GoldenChara, "chara.fk.hipsPosition", new Vec3(float.NaN, 0f, 0f)), CommandCodes.BadArgs, "fk.hipsPosition refuses NaN");
            Kit.Ok(t, s.SetJson(Kit.GoldenChara, "chara.fk.hipsPosition", "null"), "SetJson null of fk.hipsPosition");
            Kit.Ok(t, s.Set(null, "title", "タイトル"), "title");
            t.Equal("タイトル", (string)FieldAccessor.Get(s.Model, null, "title"), "title get");
            t.Equal("\"タイトル\"", FieldAccessor.GetJson(s.Model, null, "title"), "title JSON");
            t.Check(FieldAccessor.Get(s.Model, Kit.GoldenChara, "chara.fk.bones[\"nope\"]") == FieldValue.Absent, "a missing map key reads as Absent");
            t.Equal("null", FieldAccessor.GetJson(s.Model, Kit.GoldenChara, "chara.fk.bones[\"nope\"]"), "Absent as JSON null");
            t.Check(FieldAccessor.TryGet(s.Model, Kit.GoldenChara, "chara.nope", out _, out string code) == false && code == CommandCodes.BadArgs, "TryGet reports the code");
            t.Check(FieldAccessor.IsViewPath(Kit.GoldenChara, "ui.expanded") && FieldAccessor.IsViewPath(null, "camera.current.fov") && !FieldAccessor.IsViewPath(null, "camera.slots"), "view paths");
            Kit.Ok(t, s.SetJson(Kit.GoldenChara, "transform.pos", "[1.5, 2.0, -3.25]"), "SetJson of a vector");
            t.Check(s.Model.Find(Kit.GoldenChara).Doc.Transform.Pos.Equals(new Vec3(1.5f, 2, -3.25f)), "SetJson value");
            Kit.Ok(t, s.SetJson(Kit.GoldenChara, "chara.face", "{\"eyes\": 3}"), "SetJson of an object (absent members take their defaults)");
            Kit.Fails(t, s.SetJson(Kit.GoldenChara, "chara.face.eyes", "\"three\""), CommandCodes.BadArgs, "SetJson of the wrong JSON type");
            Kit.Fails(t, s.SetJson(Kit.GoldenChara, "chara.face.eyes", "{"), CommandCodes.BadArgs, "SetJson of bad JSON");
            t.Equal("[1.5, 2.0, -3.25]", FieldAccessor.GetJson(s.Model, Kit.GoldenChara, "transform.pos"), "GetJson of a vector");
            t.Equal("0.1", FieldJson.ToText(0.1f), "float JSON uses the float's canonical text");
        }

        static void SubObjects(TestRunner t)
        {
            var s = Kit.GoldenSession(t, withSubtree: true);
            string c = Kit.GoldenChara, r = Kit.GoldenRoute, item = Kit.GoldenItem;
            var forms = new[]
            {
                new[] { c + "/fk/cf_J_Head", c, "chara.fk.bones[\"cf_J_Head\"]" },
                new[] { c + "/ik/LeftHand", c, "chara.ik.targets.LeftHand" },
                new[] { c + "/look", c, "chara.look.target" },
                new[] { c + "/acc/3f2a6b1c9d8e4f7a0b1c2d3e4f5a6b7c", c, "chara.state.accessories[\"3f2a6b1c9d8e4f7a0b1c2d3e4f5a6b7c\"]" },
                new[] { r + "/points/5f5f5f5f5f5f5f5f5f5f5f5f5f5f5f5f", r, "route.points[1]" },
                new[] { r + "/points/5f5f5f5f5f5f5f5f5f5f5f5f5f5f5f5f/aid", r, "route.points[1].aid" },
                new[] { item + "/fk/bone_a", item, "item.fk.bones[\"bone_a\"]" },
            };
            foreach (var f in forms)
            {
                bool ok = SubObjectPath.TryParse(s.Model, f[0], out string id, out string path);
                t.Check(ok && id == f[1] && path == f[2], "address " + f[0] + " → " + f[2] + (ok ? " (got " + path + ")" : ""));
                t.Equal(f[0], SubObjectPath.Format(s.Model, f[1], f[2]), "field path " + f[2] + " → address");
                t.Check(FieldAccessor.TryGet(s.Model, id ?? f[1], f[2], out _, out _) || f[0].Contains("/fk/bone_a"), "the field path of " + f[0] + " resolves");
            }
            t.Check(!SubObjectPath.TryParse(s.Model, c + "/acc/ffffffffffffffffffffffffffffffff", out _, out _), "an accessory outside the known coordinate");
            t.Check(!SubObjectPath.TryParse(s.Model, r + "/points/ffffffffffffffffffffffffffffffff", out _, out _), "a missing route point");
            t.Check(!SubObjectPath.TryParse(s.Model, c + "/ik/Tail", out _, out _), "an unknown IK target");
            t.Check(!SubObjectPath.TryParse(s.Model, Kit.GoldenFolder + "/look", out _, out _), "look on a folder");
            t.Check(!SubObjectPath.TryParse(s.Model, "ffffffffffffffffffffffffffffffff/look", out _, out _), "a missing node");
            t.Equal(null, SubObjectPath.Format(s.Model, c, "chara.fk.bones[\"a/b\"]"), "a bone name with '/' has only the field path");
            t.Equal(null, SubObjectPath.Format(s.Model, c, "chara.face.eyes"), "no address for ordinary fields");
        }
    }
}

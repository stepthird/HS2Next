// D2 tests: the core-member / enum / node-kind part of T5 — unknown members on
// every object of the schema, unknown enum strings at every enum member and unknown node kinds (with a subtree)
// survive load → save unchanged — plus the "absent" values (HS2 MemberInit defaults, typed in
// here from the spec tables), the reader's normalisations (tree repair, sibling order, duplicate ids, empty
// routes), the writer's ordering rules, required members and type errors of the documents.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save.Tests
{
    public static class DocTests_T5Preservation
    {
        public static void Run(TestRunner t)
        {
            UnknownMembersEverywhere(t);
            UnknownEnumStrings(t);
            UnknownNodeKinds(t);
        }

        // Adds an unknown member to every object of a full document (maps and ext values excluded) and expects the
        // written text to equal the edited input.
        static void UnknownMembersEverywhere(TestRunner t)
        {
            Every(t, "chara", DocIo.Bytes(new DocFuzz(21, extras: false, full: true).Chara()), b => DocIo.Bytes(DocIo.Chara(b)));
            Every(t, "coord", DocIo.Bytes(new DocFuzz(22, extras: false, full: true).Coord()), b => DocIo.Bytes(DocIo.Coord(b)));
            Every(t, "scene", DocIo.Bytes(new DocFuzz(23, extras: false, full: true).Scene()), b => DocIo.Bytes(DocIo.Scene(b)));
        }

        static void Every(TestRunner t, string what, byte[] full, Func<byte[], byte[]> roundTrip)
        {
            var root = DocReader.Parse(full, what);
            int n = 0;
            AddUnknown(root, "", ref n);
            var input = CanonicalJsonWriter.ToBytes(root, what);
            byte[] output;
            try { output = roundTrip(input); }
            catch (Exception e) { t.Check(false, what + ": unknown members on every object: threw " + e.Message); return; }
            bool same = DocIo.Same(input, output);
            t.Check(same, what + ": unknown member on each of " + n + " objects kept" + (same ? "" : " (" + DocIo.FirstDiff(input, output) + ")"));
        }

        static void AddUnknown(JToken t, string path, ref int n)
        {
            if (t is JObject o)
            {
                bool map = path.EndsWith(".neckFix") || path.EndsWith(".bones") || path.EndsWith("state.accessories") || path.EndsWith("ext");
                foreach (var p in new List<JProperty>(o.Properties()))
                    if (!map) AddUnknown(p.Value, (path.Length == 0 ? "" : path + ".") + p.Name, ref n);
                if (map) return;
                var value = new JObject { ["k"] = new JArray(1, "xé", true, null, 0.5), ["n"] = new JObject() };
                var ext = o.Property("ext");
                if (ext != null) ext.AddBeforeSelf(new JProperty("zzFuture", value));
                else o.Add("zzFuture", value);
                n++;
            }
            else if (t is JArray a)
                foreach (var e in a) AddUnknown(e, path + "[]", ref n);
        }

        static void UnknownEnumStrings(TestRunner t)
        {
            // chara: overlay target, bone-mod location, ME renderer and projector properties.
            var c = new CharaDocument();
            c.Body.SkinOverlays.Add(new SkinOverlay { Target = "zzFutureTarget", Image = "res/" + new string('a', 64) + ".png" });
            c.Body.BoneMods.Add(new BodyBoneMod { Bone = "b", Location = "zzFutureLocation" });
            c.MaterialOverrides.Add(new MeRenderer { Renderer = "r", Property = "zzFutureRendererProperty", Value = 1 });
            c.MaterialOverrides.Add(new MeProjector { Projector = "p", Property = "zzFutureProjectorProperty", Value = "1" });
            var bc = DocIo.Bytes(c);
            var fc = new FindingList();
            var c2 = DocIo.Chara(bc, fc);
            t.Equal(4, fc.CountOf("json.unknownEnum"), "chara: 4 unknown enum strings reported");
            t.Check(c2.Body.SkinOverlays[0].TargetType == null && c2.Body.BoneMods[0].LocationType == null &&
                ((MeRenderer)c2.MaterialOverrides[0]).PropertyType == null && ((MeProjector)c2.MaterialOverrides[1]).PropertyType == null, "chara: typed views of unknown strings are null");
            t.Check(DocIo.Same(bc, DocIo.Bytes(c2)), "chara: unknown enum strings written back");

            // scene: voice repeat, route orient / ease / connection, bgm and user-audio repeat.
            var s = new SceneDocument();
            var chara = new CharaNodeState();
            chara.Voice.Repeat = "zzShuffle";
            s.Nodes.Add(new SceneNode(Hex(1), chara));
            var route = new RouteNodeState { Orient = "zzSpiral" };
            route.Points.Add(new RoutePoint { Id = Hex(2), Ease = "zzEaseFuture", Connection = "zzBezier" });
            s.Nodes.Add(new SceneNode(Hex(3), route));
            s.Scene.Sound.Bgm.Repeat = "zzShuffle";
            s.Scene.Sound.UserAudio.Repeat = "zzOnce";
            var bs = DocIo.Bytes(s);
            var fs = new FindingList();
            var s2 = DocIo.Scene(bs, fs);
            t.Equal(6, fs.CountOf("json.unknownEnum"), "scene: 6 unknown enum strings reported");
            var r2 = (RouteNodeState)s2.Find(Hex(3)).State;
            t.Check(((CharaNodeState)s2.Find(Hex(1)).State).Voice.RepeatType == null && r2.OrientType == null && r2.Points[0].EaseType == null &&
                r2.Points[0].ConnectionType == null && s2.Scene.Sound.Bgm.RepeatType == null && s2.Scene.Sound.UserAudio.RepeatType == null, "scene: typed views of unknown strings are null");
            t.Check(DocIo.Same(bs, DocIo.Bytes(s2)), "scene: unknown enum strings written back");

            // Editing the typed view replaces the string.
            r2.OrientType = RouteOrient.Y;
            t.Equal("y", r2.Orient, "typed view setter writes the camel-case string");
            r2.Points[0].EaseType = RouteEase.easeOutBounce;
            t.Equal("easeOutBounce", r2.Points[0].Ease, "ease setter writes the member name");

            // Unknown ME type keeps its place among known items.
            var me = "{\"materialOverrides\":[{\"type\":\"copy\",\"material\":\"m\",\"copyName\":\"m.MECopy1\"},{\"type\":\"zzFuture\",\"x\":[1,2]},{\"type\":\"keyword\",\"material\":\"m\",\"property\":\"P\",\"value\":true,\"original\":false}]}";
            var cm = DocIo.Chara(DocIo.Utf8(me));
            t.Check(cm.MaterialOverrides.Count == 3 && cm.MaterialOverrides[1] is MeUnknown mu && mu.TypeName == "zzFuture" && cm.MaterialOverrides[2] is MeKeyword,
                "unknown ME type read in place");
            var again = DocIo.Chara(DocIo.Bytes(cm));
            t.Check(again.MaterialOverrides[1] is MeUnknown mu2 && JToken.DeepEquals(mu2.Raw, JObject.Parse("{\"type\":\"zzFuture\",\"x\":[1,2]}")), "unknown ME type written back unchanged");
        }

        static void UnknownNodeKinds(TestRunner t)
        {
            // An unknown node with a two-level subtree; its child is a known kind, the grandchild unknown again.
            string json = "{\"nodes\":[" +
                "{\"id\":\"" + Hex(1) + "\",\"kind\":\"zzEmitter\",\"parent\":null,\"order\":0,\"rate\":5,\"zzEmitter\":{\"a\":[1.5]},\"ext\":{\"x.y\":1}}," +
                "{\"id\":\"" + Hex(2) + "\",\"kind\":\"folder\",\"parent\":{\"node\":\"" + Hex(1) + "\",\"point\":null},\"order\":0,\"folder\":{}}," +
                "{\"id\":\"" + Hex(3) + "\",\"kind\":\"zzGizmo\",\"parent\":{\"node\":\"" + Hex(2) + "\",\"point\":null},\"order\":0}" +
                "]}";
            var f = new FindingList();
            var s = DocIo.Scene(DocIo.Utf8(json), f);
            t.Equal(2, f.CountOf("scene.unknownKind"), "two unknown kinds reported");
            t.Check(s.Nodes.Count == 3 && s.Nodes[0].State is OpaqueNode && s.Nodes[1].Kind == "folder" && s.Nodes[2].State is OpaqueNode, "subtree of an unknown node kept in order");
            t.Equal(0, f.CountOf("scene.badParent"), "an unknown node takes children");
            var b = DocIo.Bytes(s);
            var s2 = DocIo.Scene(b);
            t.Check(DocIo.Same(b, DocIo.Bytes(s2)), "unknown node kinds round-trip");
            var raw = ((OpaqueNode)s2.Nodes[0].State).Raw;
            t.Check(raw["rate"]?.Value<int>() == 5 && raw["zzEmitter"] != null && raw["ext"] == null && s2.Nodes[0].Ext.Contains("x.y"), "unknown node members kept; ext separate");
            t.Check(DocIo.Text(b).Contains("\"kind\": \"zzGizmo\",\n      \"parent\": {"), "an unknown node without other members is written as read");

            // Tree repair moves an unknown node to the root: its raw parent follows.
            string orphan = "{\"nodes\":[{\"id\":\"" + Hex(4) + "\",\"kind\":\"zzGizmo\",\"parent\":{\"node\":\"" + Hex(9) + "\",\"point\":null},\"order\":3,\"v\":1}]}";
            var f2 = new FindingList();
            var so = DocIo.Scene(DocIo.Utf8(orphan), f2);
            var text = DocIo.Text(DocIo.Bytes(so));
            t.Check(f2.Has("scene.badParent") && text.Contains("\"parent\": null,\n      \"order\": 0,\n      \"v\": 1"), "unknown node with a missing parent → root, order renumbered");
        }

        internal static string Hex(int n) => n.ToString("x32");
    }

    public static class DocTests_AbsentValues
    {
        static void Expect(TestRunner t, JObject doc, string path, string json)
        {
            var actual = doc.SelectToken(path);
            var expected = JToken.Parse(json);
            bool ok = actual != null && JToken.DeepEquals(actual, expected);
            t.Check(ok, "absent " + path + " = " + json + (ok ? "" : " (got " + (actual?.ToString(Newtonsoft.Json.Formatting.None) ?? "nothing") + ")"));
        }

        static JObject Written(byte[] b) => DocReader.Parse(b, "t.json");

        public static void Run(TestRunner t)
        {
            // chara.json: every value below is the spec's "absent" column = HS2 MemberInit.
            var c = Written(DocIo.Bytes(DocIo.Chara(DocIo.Utf8("{}"))));
            Expect(t, c, "profile", "{\"name\":\"\",\"sex\":0,\"futanari\":false,\"hs2\":null}");
            Expect(t, c, "face.head", "\"illusion.hs2#0\"");
            Expect(t, c, "face.skin", "\"illusion.hs2#0\"");
            Expect(t, c, "face.detail", "{\"item\":\"illusion.hs2#0\",\"power\":1.0}");
            var shape = new JArray();
            for (int i = 0; i < 59; i++) shape.Add(0.5);
            Expect(t, c, "face.shape", shape.ToString());
            Expect(t, c, "face.eyebrow", "{\"item\":\"illusion.hs2#0\",\"color\":[0.05,0.05,0.05,1.0],\"layout\":[0.5,0.375,0.666,0.666],\"tilt\":0.5}");
            Expect(t, c, "face.eyes.linked", "true");
            Expect(t, c, "face.eyes.offsetY", "0.5");
            Expect(t, c, "face.eyes.shadowScale", "0.5");
            foreach (var side in new[] { "left", "right" })
                Expect(t, c, "face.eyes." + side, "{\"white\":[0.846,0.846,0.846,1.0],\"iris\":{\"item\":\"illusion.hs2#0\",\"color\":[0.33,0.33,0.33,1.0],\"size\":[0.666,0.666],\"emission\":0.0}," +
                    "\"pupil\":{\"item\":\"illusion.hs2#0\",\"color\":[0.0,0.0,0.0,1.0],\"size\":[0.8333,0.8333]}}");
            Expect(t, c, "face.eyes.highlight", "{\"item\":\"illusion.hs2#0\",\"color\":[1.0,1.0,1.0,1.0],\"layout\":[0.5,0.5,0.5,0.5],\"tilt\":0.5}");
            Expect(t, c, "face.eyelashes", "{\"item\":\"illusion.hs2#0\",\"color\":[0.19,0.19,0.19,1.0]}");
            Expect(t, c, "face.mole", "{\"item\":\"illusion.hs2#0\",\"color\":[0.0,0.0,0.0,1.0],\"layout\":[0.5,0.5,0.25,0.5]}");
            foreach (var m in new[] { "eyeshadow", "cheek", "lip" })
                Expect(t, c, "face.makeup." + m, "{\"item\":\"illusion.hs2#0\",\"color\":[1.0,1.0,1.0,1.0],\"gloss\":0.0}");
            string paint = "{\"item\":\"illusion.hs2#0\",\"color\":[1.0,0.0,0.0,1.0],\"gloss\":0.5,\"metallic\":0.5,\"layoutItem\":\"illusion.hs2#0\",\"layout\":[0.5,0.5,0.5,0.5],\"rotation\":0.5}";
            Expect(t, c, "face.makeup.paint", "[" + paint + "," + paint + "]");
            Expect(t, c, "face.beard", "{\"item\":\"illusion.hs2#0\",\"color\":[0.19,0.19,0.19,1.0]}");
            var bshape = new JArray();
            for (int i = 0; i < 32; i++) bshape.Add(0.5);
            bshape.Add(0.0);
            Expect(t, c, "body.shape", bshape.ToString());
            Expect(t, c, "body.bust", "{\"softness\":0.5,\"weight\":0.5}");
            Expect(t, c, "body.skin", "{\"item\":\"illusion.hs2#0\",\"color\":[0.8,0.7,0.64,1.0],\"gloss\":0.7,\"metallic\":0.0}");
            Expect(t, c, "body.detail", "{\"item\":\"illusion.hs2#0\",\"power\":0.5}");
            Expect(t, c, "body.sunburn", "{\"item\":\"illusion.hs2#0\",\"color\":[1.0,1.0,1.0,1.0]}");
            Expect(t, c, "body.paint", "[" + paint + "," + paint + "]");
            Expect(t, c, "body.nipple", "{\"item\":\"illusion.hs2#0\",\"color\":[0.76,0.52,0.52,1.0],\"gloss\":0.6,\"areola\":0.7}");
            Expect(t, c, "body.underhair", "{\"item\":\"illusion.hs2#0\",\"color\":[0.05,0.05,0.05,1.0]}");
            Expect(t, c, "body.nail", "{\"color\":[1.0,0.92,0.92,1.0],\"gloss\":0.6}");
            Expect(t, c, "body.visible", "true");
            Expect(t, c, "body.uncensor", "null");
            Expect(t, c, "body.skinOverlays", "[]");
            Expect(t, c, "body.boneMods", "[]");
            Expect(t, c, "hair.shaderType", "0");
            Expect(t, c, "hair.editor", "{\"sameSetting\":true,\"autoSetting\":true,\"ctrlTogether\":false}");
            foreach (var part in HairData.PartNames)
                Expect(t, c, "hair." + part, "{\"item\":\"illusion.hs2#0\",\"baseColor\":[0.2,0.2,0.2,1.0],\"topColor\":[0.039,0.039,0.039,1.0],\"underColor\":[0.565,0.565,0.565,1.0]," +
                    "\"specular\":[0.3,0.3,0.3,1.0],\"metallic\":0.0,\"smoothness\":0.0,\"accessoryColors\":[[1.0,1.0,1.0,1.0],[1.0,1.0,1.0,1.0],[1.0,1.0,1.0,1.0],[1.0,1.0,1.0,1.0]]," +
                    "\"bundles\":[],\"mesh\":{\"item\":\"illusion.hs2#0\",\"color\":[1.0,1.0,1.0,1.0],\"layout\":[1.0,1.0,0.0,0.0]},\"materialOverrides\":[]}");
            Expect(t, c, "materialOverrides", "[]");
            Expect(t, c, "studioOverlays", "{\"skin\":true,\"eyes\":true,\"clothes\":true}");
            t.Check(c["ext"] == null, "absent ext is not written");
            var hs2 = Written(DocIo.Bytes(DocIo.Chara(DocIo.Utf8("{\"profile\":{\"hs2\":{\"parameter2\":{}}},\"body\":{\"uncensor\":{}}}"))));
            Expect(t, hs2, "profile.hs2", "{\"personality\":0,\"birthMonth\":1,\"birthDay\":1,\"voiceRate\":0.5,\"wishes\":[],\"parameter2\":{\"personality\":0,\"voiceRate\":0.5,\"trait\":0,\"mind\":0,\"hAttribute\":0}}");
            Expect(t, hs2, "body.uncensor", "{\"body\":null,\"penis\":null,\"balls\":null,\"displayBalls\":null,\"legacy\":null}");

            // coord.json.
            var o = Written(DocIo.Bytes(DocIo.Coord(DocIo.Utf8("{}"))));
            Expect(t, o, "name", "\"\"");
            Expect(t, o, "sex", "1");
            string color = "{\"base\":[1.0,1.0,1.0,1.0],\"pattern\":\"illusion.hs2#0\",\"patternColor\":[1.0,1.0,1.0,1.0],\"layout\":[1.0,1.0,0.0,0.0],\"rotation\":0.5,\"gloss\":0.5,\"metallic\":0.0}";
            foreach (var kind in ClothesSet.Names)
                Expect(t, o, "clothes." + kind, "{\"item\":\"illusion.hs2#0\",\"colors\":[" + color + "," + color + "," + color + "],\"breakRate\":0.0,\"hideOptions\":[false,false]," +
                    "\"overlays\":null,\"materialOverrides\":[],\"pushUp\":null}");
            Expect(t, o, "accessories", "[]");
            Expect(t, o, "ruleOverrides", "null");
            var acc = Written(DocIo.Bytes(DocIo.Coord(DocIo.Utf8("{\"accessories\":[{\"id\":\"" + DocTests_T5Preservation.Hex(7) + "\",\"category\":352,\"item\":\"test.mod#1\",\"rules\":{}," +
                "\"dynamicBones\":[{\"root\":\"r\"}]}],\"ruleOverrides\":{},\"clothes\":{\"top\":{\"pushUp\":{},\"overlays\":{}},\"innerTop\":{\"pushUp\":{}},\"gloves\":{\"pushUp\":{}}}}"))));
            string move = "{\"position\":[0.0,0.0,0.0],\"rotation\":[0.0,0.0,0.0],\"scale\":[1.0,1.0,1.0]}";
            string accColor = "{\"color\":[1.0,1.0,1.0,1.0],\"gloss\":0.5,\"metallic\":0.5,\"smoothness\":0.5}";
            string dbeNulls = "\"freezeAxis\":null,\"weight\":null,\"damping\":null,\"elasticity\":null,\"stiffness\":null,\"inertia\":null,\"radius\":null";
            Expect(t, acc, "accessories[0]", "{\"id\":\"" + DocTests_T5Preservation.Hex(7) + "\",\"category\":352,\"item\":\"test.mod#1\",\"parent\":\"\",\"moves\":[" + move + "," + move + "]," +
                "\"colors\":[" + accColor + "," + accColor + "," + accColor + "," + accColor + "],\"hideCategory\":0,\"hideTiming\":1,\"noShake\":false,\"importSlot\":null," +
                "\"boneMods\":[],\"materialOverrides\":[],\"dynamicBones\":[{\"root\":\"r\"," + dbeNulls + ",\"original\":{" + dbeNulls + "}}]," +
                "\"rules\":{\"visibility\":[],\"characterAccessory\":false,\"matchHairColor\":false,\"advancedParent\":null,\"name\":null}}");
            Expect(t, acc, "ruleOverrides", "{\"suppressed\":[],\"overrides\":[]}");
            Expect(t, acc, "clothes.top.overlays", "{\"main\":null,\"colorMask\":null,\"patterns\":[null,null,null],\"sourceReplacement\":null,\"textureSize\":null}");
            string topSet = "{\"enabled\":false,\"firmness\":0.0,\"lift\":0.0,\"pushTogether\":0.0,\"squeeze\":0.0,\"centerNipples\":0.0,\"corset\":0.0,\"flattenNipples\":false,\"hideNipples\":false,\"hideAccessories\":false,\"corsetHalf\":false}";
            Expect(t, acc, "clothes.top.pushUp", topSet);
            Expect(t, acc, "clothes.innerTop.pushUp", "{\"enabled\":true,\"firmness\":0.9,\"lift\":0.6,\"pushTogether\":0.65,\"squeeze\":0.6,\"centerNipples\":1.0,\"corset\":0.0,\"flattenNipples\":true,\"hideNipples\":false,\"hideAccessories\":false,\"corsetHalf\":false}");
            Expect(t, acc, "clothes.gloves.pushUp", topSet);

            // scene.json: the scene block with the 45 effect values, and a minimal node of each kind.
            var s = Written(DocIo.Bytes(DocIo.Scene(DocIo.Utf8("{}"))));
            Expect(t, s, "nodes", "[]");
            Expect(t, s, "scene.map", "{\"ref\":null,\"pos\":[0.0,0.0,0.0],\"rot\":[0.0,0.0,0.0],\"options\":true,\"lights\":true}");
            Expect(t, s, "scene.effects.colorGrading", "{\"lut\":\"illusion.hs2#0\",\"blend\":0.0,\"saturation\":15,\"brightness\":-20,\"contrast\":40}");
            Expect(t, s, "scene.effects.ambientOcclusion", "{\"enabled\":true,\"intensity\":0.2,\"thickness\":2.0,\"color\":[0.0,0.0,0.0,1.0]}");
            Expect(t, s, "scene.effects.bloom", "{\"enabled\":true,\"intensity\":2.0,\"threshold\":1.0,\"softKnee\":0.76,\"clamp\":true,\"diffusion\":7.0,\"color\":[0.7490196,0.7490196,0.7490196,1.0]}");
            Expect(t, s, "scene.effects.depthOfField", "{\"enabled\":false,\"focus\":null,\"focalSize\":0.4,\"aperture\":0.6}");
            Expect(t, s, "scene.effects.vignette", "{\"enabled\":true,\"intensity\":0.35}");
            Expect(t, s, "scene.effects.ssr", "{\"enabled\":true}");
            Expect(t, s, "scene.effects.reflectionProbe", "{\"enabled\":false,\"cubemap\":\"illusion.hs2#0\",\"intensity\":1.0}");
            Expect(t, s, "scene.effects.fog", "{\"enabled\":false,\"excludeFarPixels\":false,\"height\":50.0,\"heightDensity\":0.01,\"color\":[0.5411765,0.65882355,0.796078444,1.0],\"density\":0.0005}");
            Expect(t, s, "scene.effects.sunShafts", "{\"enabled\":false,\"caster\":null,\"thresholdColor\":[0.5019608,0.5019608,0.5019608,1.0],\"color\":[1.0,1.0,1.0,1.0],\"distanceFalloff\":0.75,\"blurSize\":5.0,\"intensity\":5.0}");
            Expect(t, s, "scene.effects.shadows", "{\"enabled\":true}");
            Expect(t, s, "scene.effects.ambient", "{\"sky\":[0.6666667,0.7372549,0.9529412,1.0],\"equator\":[0.7254902,0.7647059,0.8039216,1.0],\"ground\":[0.8,0.427450985,0.160784319,1.0]}");
            Expect(t, s, "scene.effects.sky", "{\"enabled\":false,\"pattern\":\"illusion.hs2#0\"}");
            Expect(t, s, "scene.camera", "{\"current\":null,\"slots\":[null,null,null,null,null,null,null,null,null,null]}");
            Expect(t, s, "scene.charaLight", "{\"color\":[1.0,1.0,1.0,1.0],\"intensity\":1.0,\"rot\":[0.0,0.0],\"shadow\":true}");
            Expect(t, s, "scene.sound", "{\"bgm\":{\"ref\":\"illusion.hs2#0\",\"play\":false,\"repeat\":\"all\"},\"userAudio\":{\"file\":null,\"play\":false,\"repeat\":\"all\"}}");
            Expect(t, s, "scene.background", "null");
            Expect(t, s, "scene.frame", "null");
            // The byte colours are float32(c / 255), e.g. 191 → 0.749019623.
            t.Equal(BitConverter.ToInt32(BitConverter.GetBytes((float)(191 / 255.0)), 0), BitConverter.ToInt32(BitConverter.GetBytes(new BloomEffect().Color.R), 0), "bloom grey = float32(191/255)");
            int byteMismatch = 0;
            for (int i = 0; i < 256; i++)
                if (BitConverter.ToInt32(BitConverter.GetBytes((float)(i / 255.0)), 0) != BitConverter.ToInt32(BitConverter.GetBytes(DocDefaults.Byte(i, 0, 0).R), 0)) byteMismatch++;
            t.Equal(0, byteMismatch, "c / 255f equals float32(c / 255) for every byte");

            string id = DocTests_T5Preservation.Hex(1);
            var n = Written(DocIo.Bytes(DocIo.Scene(DocIo.Utf8("{\"nodes\":[" +
                "{\"id\":\"" + id + "\",\"kind\":\"chara\",\"chara\":{}}," +
                "{\"id\":\"" + DocTests_T5Preservation.Hex(2) + "\",\"kind\":\"item\",\"item\":{\"ref\":\"test.mod#1\"}}," +
                "{\"id\":\"" + DocTests_T5Preservation.Hex(3) + "\",\"kind\":\"light\",\"light\":{\"ref\":\"test.mod#2\"}}," +
                "{\"id\":\"" + DocTests_T5Preservation.Hex(4) + "\",\"kind\":\"folder\",\"folder\":{}}," +
                "{\"id\":\"" + DocTests_T5Preservation.Hex(5) + "\",\"kind\":\"route\",\"route\":{\"points\":[{\"id\":\"" + DocTests_T5Preservation.Hex(6) + "\"}]}}," +
                "{\"id\":\"" + DocTests_T5Preservation.Hex(7) + "\",\"kind\":\"camera\",\"camera\":{}}]}"))));
            string transform = "{\"pos\":[0.0,0.0,0.0],\"rot\":[0.0,0.0,0.0],\"scale\":[1.0,1.0,1.0],\"inheritScale\":false}";
            Expect(t, n, "nodes[0].name", "null");
            Expect(t, n, "nodes[0].parent", "null");
            Expect(t, n, "nodes[0].order", "0");
            Expect(t, n, "nodes[0].transform", transform);
            Expect(t, n, "nodes[0].visible", "true");
            Expect(t, n, "nodes[0].ui", "{\"expanded\":false,\"expandedGroups\":[],\"expandedPoints\":[]}");
            Expect(t, n, "nodes[1].ui", "{\"expanded\":false}");
            Expect(t, n, "nodes[0].chara.anime", "{\"ref\":{\"source\":\"illusion.hs2\",\"group\":0,\"category\":0,\"no\":0},\"speed\":1.0,\"pattern\":0.0,\"time\":0.0,\"forceLoop\":false,\"optionVisible\":true,\"params\":[0.0,0.0]}");
            Expect(t, n, "nodes[0].chara.hands", "[0,0]");
            Expect(t, n, "nodes[0].chara.face", "{\"eyebrow\":0,\"eyes\":0,\"eyesOpen\":1.0,\"blink\":true,\"mouth\":0,\"mouthOpen\":0.0,\"lipSync\":true,\"tears\":0.0,\"cheek\":0.0," +
                "\"eyebrowOpenMax\":1.0,\"mouthOpenMin\":0.0,\"mouthOpenMax\":1.0,\"mouthFixed\":false,\"mouthAdjustWidth\":true,\"hideHighlight\":false}");
            Expect(t, n, "nodes[0].chara.look", "{\"eyes\":0,\"neck\":3,\"target\":[0.0,0.0,0.25],\"neckFix\":{},\"eyeRotations\":null}");
            Expect(t, n, "nodes[0].chara.fk", "{\"enabled\":false,\"groups\":{\"hair\":false,\"neck\":true,\"breast\":false,\"body\":true,\"rightHand\":false,\"leftHand\":false,\"skirt\":false},\"bones\":{},\"hipsOffset\":0.0,\"hipsPosition\":null}");
            Expect(t, n, "nodes[0].chara.ik", "{\"enabled\":false,\"groups\":{\"body\":true,\"rightLeg\":true,\"leftLeg\":true,\"rightArm\":true,\"leftArm\":true},\"targets\":{}}");
            Expect(t, n, "nodes[0].chara.correction", "[false,false,false,false,false,false,false,false]");
            Expect(t, n, "nodes[0].chara.state", "{\"clothes\":[0,0,0,0,0,0,0,0],\"accessories\":{},\"liquid\":[0,0,0,0,0],\"nipple\":0.0,\"wet\":0.0,\"gloss\":0.0," +
                "\"son\":{\"visible\":false,\"length\":1.0},\"silhouette\":{\"enabled\":false,\"color\":[0.0,0.0,1.0,1.0]}}");
            Expect(t, n, "nodes[0].chara.voice", "{\"list\":[],\"repeat\":\"none\"}");
            string itemColor = "{\"main\":[1.0,1.0,1.0,1.0],\"metallic\":0.0,\"glossiness\":0.0,\"pattern\":{\"item\":null,\"file\":null,\"color\":[1.0,1.0,1.0,1.0],\"clamp\":true,\"uv\":[0.0,0.0,1.0,1.0],\"rot\":0.0}}";
            Expect(t, n, "nodes[1].item", "{\"ref\":\"test.mod#1\",\"group\":0,\"category\":0,\"anime\":{\"pattern\":0,\"speed\":1.0,\"time\":0.0},\"colors\":[" + itemColor + "," + itemColor + "," + itemColor + "," + itemColor + "]," +
                "\"alpha\":1.0,\"emission\":{\"color\":[1.0,1.0,1.0,1.0],\"intensity\":0.0,\"power\":0.0},\"lightCancel\":0.0,\"panel\":null,\"fk\":{\"enabled\":false,\"bones\":{}},\"dynamicBones\":true,\"options\":[]}");
            Expect(t, n, "nodes[2].light", "{\"ref\":\"test.mod#2\",\"color\":[1.0,1.0,1.0,1.0],\"intensity\":1.0,\"range\":10.0,\"spotAngle\":30.0,\"shadow\":true,\"enabled\":true,\"showTarget\":true}");
            Expect(t, n, "nodes[3].folder", "{}");
            Expect(t, n, "nodes[4].route", "{\"playing\":false,\"loop\":true,\"showLine\":true,\"orient\":\"none\",\"color\":[0.0,0.0,1.0,1.0],\"points\":[{\"id\":\"" + DocTests_T5Preservation.Hex(6) + "\"," +
                "\"pos\":[0.0,0.0,0.0],\"rot\":[0.0,0.0,0.0],\"speed\":2.0,\"ease\":\"linear\",\"connection\":\"line\",\"link\":false,\"aid\":null}]}");
            Expect(t, n, "nodes[5].camera", "{\"active\":false,\"fov\":null}");
        }
    }

    public static class DocTests_ReaderRules
    {
        static string H(int n) => DocTests_T5Preservation.Hex(n);

        static string Node(int id, string kind, int? parent = null, string point = null, int order = 0, string section = "{}") =>
            "{\"id\":\"" + H(id) + "\",\"kind\":\"" + kind + "\",\"parent\":" + (parent == null ? "null" : "{\"node\":\"" + H(parent.Value) + "\",\"point\":" + (point == null ? "null" : "\"" + point + "\"") + "}") +
            ",\"order\":" + order + ",\"" + kind + "\":" + section + "}";

        public static void Run(TestRunner t)
        {
            // Canonical order: pre-order, siblings by (order, position), character children grouped by attach point.
            string item = "{\"ref\":\"test.mod#1\"}";
            string json = "{\"nodes\":[" +
                Node(5, "item", 1, "N_Hand_R", 7, item) + "," + Node(4, "item", 1, "N_Hair_pony", 9, item) + "," + Node(6, "item", 1, "N_Unknown", 0, item) + "," +
                Node(3, "folder", null, null, 5) + "," + Node(1, "chara", null, null, 5) + "," + Node(2, "item", 1, "N_Hand_R", 2, item) + "," + Node(7, "folder", 3, null, -4) + "]}";
            var s = DocIo.Scene(DocIo.Utf8(json));
            var ids = new List<string>();
            foreach (var n in s.Nodes) ids.Add(n.Id.Substring(31) + ":" + n.Order);
            t.Equal("3:0,7:0,1:1,4:0,2:0,5:1,6:0", string.Join(",", ids), "nodes sorted into canonical pre-order and renumbered");
            var s2 = DocIo.Scene(DocIo.Bytes(s));
            var ids2 = new List<string>();
            foreach (var n in s2.Nodes) ids2.Add(n.Id.Substring(31) + ":" + n.Order);
            t.Equal(string.Join(",", ids), string.Join(",", ids2), "canonical order is stable");
            // The writer normalises a model that was built in any order.
            var built = new SceneDocument();
            built.Nodes.Add(new SceneNode(H(2), new FolderNodeState()) { Parent = new NodeParent(H(1)), Order = 10 });
            built.Nodes.Add(new SceneNode(H(1), new FolderNodeState()) { Order = 3 });
            var bt = DocIo.Text(DocIo.Bytes(built));
            t.Check(bt.IndexOf(H(1), StringComparison.Ordinal) < bt.IndexOf("\"id\": \"" + H(2), StringComparison.Ordinal) && !bt.Contains("\"order\": 10") && !bt.Contains("\"order\": 3"), "writer emits pre-order with sibling numbers");

            // Tree repair: a missing parent, a light as parent, a cycle.
            var f = new FindingList();
            string bad = "{\"nodes\":[" + Node(1, "light", null, null, 0, item) + "," + Node(2, "folder", 1) + "," + Node(3, "folder", 9) + "," +
                Node(4, "folder", 5) + "," + Node(5, "folder", 4) + "]}";
            var sb = DocIo.Scene(DocIo.Utf8(bad), f);
            t.Equal(3, f.CountOf("scene.badParent"), "light parent, missing parent and cycle reported");
            t.Check(sb.Find(H(2)).Parent == null && sb.Find(H(3)).Parent == null && (sb.Find(H(4)).Parent == null) != (sb.Find(H(5)).Parent == null), "repaired nodes moved to the root; one cycle member keeps its parent");

            // A very deep chain (hostile input) neither overflows the stack nor takes quadratic time; a long cycle is cut once.
            var deep = new System.Text.StringBuilder("{\"nodes\":[");
            const int depth = 20000;
            for (int i = 1; i <= depth; i++)
                deep.Append(i > 1 ? "," : "").Append("{\"id\":\"").Append(H(i)).Append("\",\"kind\":\"folder\",\"parent\":")
                    .Append(i == 1 ? "{\"node\":\"" + H(depth) + "\",\"point\":null}" : "{\"node\":\"" + H(i - 1) + "\",\"point\":null}").Append(",\"folder\":{}}");
            deep.Append("]}");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var fd = new FindingList();
            var sd = DocIo.Scene(DocIo.Utf8(deep.ToString()), fd);
            long readMs = sw.ElapsedMilliseconds;
            var deepBytes = DocIo.Bytes(sd);
            t.Info("20,000-node scene: read " + readMs + " ms, write " + (sw.ElapsedMilliseconds - readMs) + " ms");
            t.Check(fd.CountOf("scene.badParent") == 1 && sd.Nodes[0].Id == H(1) && sd.Nodes[depth - 1].Id == H(depth) && DocIo.Same(deepBytes, DocIo.Bytes(DocIo.Scene(deepBytes))),
                "20,000-node cyclic chain: one cut, pre-order kept, round trip (" + sw.ElapsedMilliseconds + " ms)");
            // Every optional member of these nodes is absent: the empty readers' JSON paths must stay lazy (spec 15.19).
            t.Check(readMs < 1000, "20,000-node minimal scene reads in under 1 s (" + readMs + " ms)");

            // Duplicate node id → BadDocument; duplicate accessory id → a derived new id + finding.
            t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Scene(DocIo.Utf8("{\"nodes\":[" + Node(1, "folder") + "," + Node(1, "folder") + "]}")), "duplicate node id");
            var fa = new FindingList();
            string dup = H(0xabc);
            string accs = "{\"accessories\":[{\"id\":\"" + dup + "\",\"category\":351,\"item\":\"a#1\"},{\"id\":\"" + dup.ToUpperInvariant() + "\",\"category\":351,\"item\":\"a#2\"}," +
                "{\"id\":\"" + dup + "\",\"category\":351,\"item\":\"a#3\"}]}";
            var accBytes = DocIo.Utf8(accs);
            var co = DocIo.Coord(accBytes, fa);
            t.Check(fa.Has("json.idUppercase") && fa.CountOf("coord.duplicateAccessoryId") == 2 && co.Accessories[0].Id == dup && co.Accessories[1].Id != dup && JsonText.IsId(co.Accessories[1].Id),
                "uppercase id lowercased; the later duplicate accessories get new ids");
            t.Check(co.Accessories[2].Id != dup && co.Accessories[2].Id != co.Accessories[1].Id && JsonText.IsId(co.Accessories[2].Id), "two duplicates get two different ids");
            string dupId;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                dupId = JsonText.Hex(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(dup + "/dup/1"))).Substring(0, 32);
            t.Equal(dupId, co.Accessories[1].Id, "duplicate id = hex of the first 16 bytes of SHA-256(\"<old id>/dup/<index>\")");
            t.Equal(dupId, CoordDocument.DuplicateId(dup, 1), "CoordDocument.DuplicateId");
            t.Check(DocIo.Same(DocIo.Bytes(co), DocIo.Bytes(DocIo.Coord(accBytes))), "a coord with duplicate accessory ids read twice gives the same bytes");

            // A route without points gets a start point at the origin whose id is derived from the route node's id:
            // the first 16 bytes of SHA-256("<routeId>/route/start"), so reading the same file twice gives the same bytes.
            var fr = new FindingList();
            var emptyRoute = DocIo.Utf8("{\"nodes\":[" + Node(1, "route") + "]}");
            var sr = DocIo.Scene(emptyRoute, fr);
            var route = (RouteNodeState)sr.Nodes[0].State;
            t.Check(fr.Has("scene.routeEmpty") && route.Points.Count == 1 && route.Points[0].Pos.Equals(Vec3.Zero) && JsonText.IsId(route.Points[0].Id), "empty route gets a start point");
            string startId;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                startId = JsonText.Hex(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(H(1) + "/route/start"))).Substring(0, 32);
            t.Equal(startId, route.Points[0].Id, "start point id = hex of the first 16 bytes of SHA-256(\"<routeId>/route/start\")");
            t.Equal(startId, RouteNodeState.StartPointId(H(1)), "RouteNodeState.StartPointId");
            var emptyBytes = DocIo.Bytes(sr);
            t.Check(DocIo.Same(emptyBytes, DocIo.Bytes(DocIo.Scene(emptyRoute))), "an empty route read twice gives the same bytes");
            t.Check(DocIo.Same(emptyBytes, DocIo.Bytes(DocIo.Scene(emptyBytes))), "the added start point is stable on the next round trip");
            var upper = DocIo.Scene(DocIo.Utf8("{\"nodes\":[{\"id\":\"" + H(1).ToUpperInvariant() + "\",\"kind\":\"route\",\"route\":{\"points\":[]}}]}"));
            t.Equal(startId, ((RouteNodeState)upper.Nodes[0].State).Points[0].Id, "start point id derived from the lowercased node id");
            var other = DocIo.Scene(DocIo.Utf8("{\"nodes\":[" + Node(2, "route") + "]}"));
            t.Check(((RouteNodeState)other.Nodes[0].State).Points[0].Id != startId, "start point ids differ between routes");

            // focus / caster naming no node: kept, reported.
            var fn = new FindingList();
            var sf = DocIo.Scene(DocIo.Utf8("{\"scene\":{\"effects\":{\"depthOfField\":{\"focus\":\"" + H(9) + "\"},\"sunShafts\":{\"caster\":\"" + H(8) + "\"}}}}"), fn);
            t.Check(fn.CountOf("scene.unknownNodeRef") == 2 && sf.Scene.Effects.DepthOfField.Focus == H(9), "dangling focus and caster kept and reported");

            // Writer ordering rules: wishes ascending, bundles by index, overlays by TexType value, ui lists sorted.
            var c = new CharaDocument();
            c.Profile.Hs2 = new Hs2Profile { Wishes = new List<int> { 9, -1, 3 } };
            c.Hair.Back.Bundles.Add(new HairBundle { Index = 5 });
            c.Hair.Back.Bundles.Add(new HairBundle { Index = 1 });
            string img = "res/" + new string('b', 64) + ".png";
            foreach (var target in new[] { "zzFuture", "FaceDetailUnder", "EyebrowUnder", "BodyOver" }) c.Body.SkinOverlays.Add(new SkinOverlay { Target = target, Image = img });
            var cr = DocIo.Chara(DocIo.Bytes(c));
            t.Equal("-1,3,9", string.Join(",", cr.Profile.Hs2.Wishes), "wishes written ascending");
            t.Equal(1, cr.Hair.Back.Bundles[0].Index, "bundles written by index");
            var targets = new List<string>();
            foreach (var o in cr.Body.SkinOverlays) targets.Add(o.Target);
            t.Equal("BodyOver,EyebrowUnder,FaceDetailUnder,zzFuture", string.Join(",", targets), "skin overlays written in TexType value order, unknown last");
            var ui = new SceneDocument();
            var cn = new SceneNode(H(1), new CharaNodeState());
            cn.Ui.ExpandedGroups.AddRange(new[] { 8, 0, 3 });
            cn.Ui.ExpandedPoints.AddRange(new[] { "N_Neck", "N_Hand_L", "N_Ana" });
            ui.Nodes.Add(cn);
            var uir = DocIo.Scene(DocIo.Bytes(ui)).Nodes[0].Ui;
            t.Equal("0,3,8|N_Ana,N_Hand_L,N_Neck", string.Join(",", uir.ExpandedGroups) + "|" + string.Join(",", uir.ExpandedPoints), "ui lists written sorted");

            // An overlay entry with neither image nor clearTarget is never written.
            var coord = new CoordDocument();
            coord.Clothes[ClothesKind.Top].Overlays = new ClothesOverlaySet { Main = new OverlayEntry { Blend = 1 }, ColorMask = new OverlayEntry { ClearTarget = true } };
            var ov = DocIo.Coord(DocIo.Bytes(coord)).Clothes[ClothesKind.Top].Overlays;
            t.Check(ov.Main == null && ov.ColorMask != null && ov.ColorMask.ClearTarget && ov.ColorMask.Image == null, "empty overlay entry written as null; clear-only entry kept");

            // File objects: exactly one known member; unknown-only objects kept.
            t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Scene(DocIo.Utf8("{\"scene\":{\"frame\":{\"user\":\"a\",\"hs2\":\"b\"}}}")), "file object with two members");
            var ff = new FindingList();
            var sfile = DocIo.Scene(DocIo.Utf8("{\"scene\":{\"frame\":{\"url\":\"https://example.invalid/x.png\"}}}"), ff);
            t.Check(ff.Has("file.unknownKind") && sfile.Scene.Frame.Kind == FileRef.Kinds.None && DocIo.Text(DocIo.Bytes(sfile)).Contains("\"url\": \"https://example.invalid/x.png\""), "file object of an unknown kind kept");

            // Required members and type errors (strict), the lenient fallback, fixed lengths.
            t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Chara(DocIo.Utf8("{\"materialOverrides\":[{\"type\":\"float\",\"material\":\"m\",\"property\":\"p\",\"value\":1.0}]}")), "ME member 'original' is required");
            t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Chara(DocIo.Utf8("{\"materialOverrides\":[{\"type\":\"float\",\"material\":\"m\",\"property\":\"p\",\"value\":true,\"original\":null}]}")), "ME float value must be a number or a string");
            t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Coord(DocIo.Utf8("{\"accessories\":[{\"id\":\"" + H(1) + "\",\"category\":351}]}")), "accessory item is required");
            t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Scene(DocIo.Utf8("{\"nodes\":[{\"id\":\"" + H(1) + "\",\"kind\":\"item\"}]}")), "the kind section is required");
            t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Coord(DocIo.Utf8("{\"clothes\":{\"top\":{\"hideOptions\":[true,false,true]}}}")), "fixed-length array with another length");
            t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Chara(DocIo.Utf8("{\"face\":{\"head\":\"no-hash\"}}")), "malformed ref");
            t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Chara(DocIo.Utf8("{\"face\":{\"shape\":\"x\"}}")), "type error is fatal when strict");
            var fl = new FindingList();
            var lenient = DocIo.Chara(DocIo.Utf8("{\"face\":{\"shape\":\"x\",\"head\":\"test.mod#4\"}}"), fl, lenient: true);
            t.Check(fl.Has("json.typeError") && lenient.Face.Shape.Length == 59 && lenient.Face.Head == "test.mod#4", "lenient: absent value used, rest read");
            t.ThrowsCode(SaveErrorCode.NonFinite, () => DocIo.Chara(DocIo.Utf8("{\"body\":{\"bust\":{\"weight\":1e39}}}")), "float outside the float32 range");
            var nan = new CharaDocument();
            nan.Face.Shape[2] = float.NaN;
            var e = t.ThrowsCode(SaveErrorCode.NonFinite, () => DocIo.Bytes(nan), "NaN in the model is refused on write");
            t.Equal("face.shape[2]", e?.JsonPath, "NonFinite path");

            // A member that the schema shape at that place does not have is an unknown member (the mole has no tilt).
            var mole = DocIo.Chara(DocIo.Utf8("{\"face\":{\"mole\":{\"tilt\":0.25}}}"));
            t.Check(mole.Face.Mole.Unknown?.Get("tilt") != null && DocIo.Text(DocIo.Bytes(mole)).Contains("\"tilt\": 0.25"), "mole tilt kept as an unknown member");

            // The writer refuses models that would give an unreadable file: null required members, wrong fixed lengths.
            var noName = new CharaDocument();
            noName.Profile.Name = null;
            t.Equal("profile.name", t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Bytes(noName), "null required string refused on write")?.JsonPath, "path of the null member");
            var badHands = new SceneDocument();
            badHands.Nodes.Add(new SceneNode(H(1), new CharaNodeState { Hands = new[] { 1, 2, 3 } }));
            t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Bytes(badHands), "fixed-length array of another length refused on write");
            var noId = new CoordDocument();
            noId.Accessories.Add(new AccessoryEntry { Category = 351, Item = "a#1" });
            t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Bytes(noId), "accessory without id refused on write");
            var dupNodes = new SceneDocument();
            dupNodes.Nodes.Add(new SceneNode(H(1), new FolderNodeState()));
            dupNodes.Nodes.Add(new SceneNode(H(1), new FolderNodeState()));
            t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Bytes(dupNodes), "duplicate node id refused on write");

            // Null elements of variable-length lists and null nodes: BadDocument with the element's path, not a
            // NullReferenceException.
            var nullAcc = new CoordDocument();
            nullAcc.Accessories.Add(new AccessoryEntry { Id = H(1), Category = 351, Item = "a#1" });
            nullAcc.Accessories.Add(null);
            t.Equal("accessories[1]", t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Bytes(nullAcc), "null accessory refused on write")?.JsonPath, "path of the null accessory");
            var nullMe = new CharaDocument();
            nullMe.Hair.Front.MaterialOverrides.Add(null);
            t.Equal("hair.front.materialOverrides[0]", t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Bytes(nullMe), "null MaterialEditor item refused on write")?.JsonPath, "path of the null ME item");
            var nullBundle = new CharaDocument();
            nullBundle.Body.BoneMods.Add(new BodyBoneMod { Bone = "cf_J_Spine01" });
            nullBundle.Body.BoneMods.Add(null);
            t.Equal("body.boneMods[1]", t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Bytes(nullBundle), "null bone modifier refused on write")?.JsonPath, "path of the null bone modifier");
            var nullNode = new SceneDocument();
            nullNode.Nodes.Add(new SceneNode(H(1), new FolderNodeState()));
            nullNode.Nodes.Add(null);
            t.Equal("nodes[1]", t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Bytes(nullNode), "null scene node refused on write")?.JsonPath, "path of the null node");
            var nullNodeId = new SceneDocument();
            nullNodeId.Nodes.Add(new SceneNode(null, new FolderNodeState()));
            t.Equal("nodes[0].id", t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Bytes(nullNodeId), "node without id refused on write")?.JsonPath, "path of the null node id");
            var nullOptions = new SceneDocument();
            nullOptions.Nodes.Add(new SceneNode(H(1), new ItemNodeState { Ref = "test.mod#1", Options = null }));
            t.Equal("nodes[0].item.options", t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Bytes(nullOptions), "null item options refused on write")?.JsonPath, "path of the null options list");
            var nullPoint = new SceneDocument();
            var pn = new SceneNode(H(1), new CharaNodeState());
            pn.Ui.ExpandedPoints.Add("N_Neck");
            pn.Ui.ExpandedPoints.Add(null);
            nullPoint.Nodes.Add(pn);
            t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Bytes(nullPoint), "null string in a str[] refused on write");
            // Anything a document class does not guard against becomes BadDocument in a save (ToDocumentBytes).
            var noFace = new CharaDocument();
            noFace.Face = null;
            var ue = t.ThrowsCode(SaveErrorCode.BadDocument, () => CanonicalJsonWriter.ToDocumentBytes(CharaDocument.EntryName, noFace.Write), "null sub-object → BadDocument in ToDocumentBytes");
            t.Check(ue != null && ue.Entry == CharaDocument.EntryName && ue.InnerException is NullReferenceException, "the original exception is kept as the inner exception");

            // −0 is written as 0.0 and reads back as +0.
            var nz = new CharaDocument();
            nz.Face.Detail.Power = -0f;
            var nzr = DocIo.Chara(DocIo.Bytes(nz));
            t.Check(BitConverter.ToInt32(BitConverter.GetBytes(nzr.Face.Detail.Power), 0) == 0, "−0 → +0");
        }
    }
}

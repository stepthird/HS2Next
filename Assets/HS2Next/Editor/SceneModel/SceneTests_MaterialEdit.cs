// The editing model of materialOverrides, without a material. MM1 keys of every type. MM2 an edit touches one element: the others are the
// same objects, one member changes. MM3 where a new item goes: after its type's run, an unsorted array stays, unknown items are
// stepped over. MM4 numbers kept as text. MM5 removal, repeated keys, read-only items. MM6 the baseline: a value equal to it removes the
// item or makes none, no-op items stay, a value that is already stored is no change. MM7 the structural text. MM8 the arrays on the bus:
// one entry, undo and redo, merges, `undo: false`, a batch, SetMap. MJ1 the same rules on body.boneMods. Synthetic names only.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_MaterialEdit
    {
        const string Res1 = "res/0000000000000000000000000000000000000000000000000000000000000001.png";
        const string Res2 = "res/0000000000000000000000000000000000000000000000000000000000000002.jpg";
        const string Item = "item.materialOverrides", Map = "map.materialOverrides";

        public static void Run(TestRunner t)
        {
            MM1(t);
            MM2(t);
            MM3(t);
            MM4(t);
            MM5(t);
            MM6(t);
            MM6Drag(t);
            MM7(t);
            MM8Entries(t);
            MM8Merges(t);
            MM8NotRecordedAndBatch(t);
            MM8SetMap(t);
            MJ1(t);
        }

        static string Text(object v) => FieldJson.ToText(v);
        static MeFloat F(string material, string property, float value, float? original = null) => new MeFloat { Material = material, Property = property, Value = value, Original = original };
        static MeColor C(string material, string property, Color4 value) => new MeColor { Material = material, Property = property, Value = value };
        static MeUnknown U(string name) => new MeUnknown { TypeName = name, Raw = JObject.Parse("{\"type\": \"" + name + "\", \"a\": 1}") };

        /// <summary>One item of every type, in apply order, then an unknown one.</summary>
        static List<MaterialOverride> All() => new List<MaterialOverride>
        {
            new MeCopy { Material = "m", CopyName = "m.MECopy1" },
            new MeName { Renderer = "r", Material = "m", Value = "n", Original = "m" },
            new MeShader { Material = "m", Shader = "AIT/x", ShaderOriginal = "AIT/y", RenderQueue = 3000, RenderQueueOriginal = 2000 },
            new MeShader { Material = "q", RenderQueue = 2450 },
            new MeRenderer { Renderer = "r", Property = "Enabled", Value = 0, Original = 1 },
            new MeRenderer { Renderer = "r", Property = "ShadowCastingMode", Value = 1, Original = 0 },
            F("m", "Metallic", 0.5f, 0.25f),
            new MeKeyword { Material = "m", Property = "DITHERSWITCH_ON", Value = true, Original = false },
            C("m", "Color", new Color4(1f, 0f, 0f, 1f)),
            new MeVector { Material = "m", Property = "Offsets", Value = new Vec4(1f, 2f, 3f, 4f) },
            new MeTexture { Material = "m", Property = "MainTex", Image = Res1, Offset = new Vec2(0.5f, 0f), Scale = new Vec2(2f, 2f) },
            new MeCubemap { Material = "m", Property = "Cube", Image = Res2 },
            new MeProjector { Projector = "p", Property = "FieldOfView", Value = "60", Original = "45" },
            U("future"),
        };

        // ---------------- MM1 ----------------

        static void MM1(TestRunner t)
        {
            bool Key(MaterialOverride item, string type, string target, string member)
            {
                bool ok = MeKey.TryOf(item, out var k);
                return ok && k.Type == type && k.Target == target && k.Member == member && k.Matches(item) && k == new MeKey(type, target, member);
            }
            var all = All();
            t.Check(Key(all[0], "copy", "m", "m.MECopy1"), "MM1: copy is (type, material, copyName)");
            t.Check(Key(all[1], "name", "r", "m"), "MM1: name is (type, renderer, material)");
            t.Check(Key(all[2], "shader", "m", null), "MM1: shader is (type, material)");
            t.Check(Key(all[4], "renderer", "r", "Enabled"), "MM1: renderer is (type, renderer, property)");
            t.Check(Key(all[6], "float", "m", "Metallic") && Key(all[7], "keyword", "m", "DITHERSWITCH_ON") && Key(all[8], "color", "m", "Color"), "MM1: float, keyword, color are (type, material, property)");
            t.Check(Key(all[9], "vector", "m", "Offsets") && Key(all[10], "texture", "m", "MainTex") && Key(all[11], "cubemap", "m", "Cube"), "MM1: vector, texture, cubemap too");
            t.Check(Key(all[12], "projector", "p", "FieldOfView"), "MM1: projector is (type, projector, property)");
            t.Check(!MeKey.TryOf(all[13], out _), "MM1: an unknown type has no key");
            t.Check(!MeKey.Property("float", "m", "x").Matches(all[13]), "MM1: and no key matches it");
            t.Check(!MeKey.TryOf(null, out _), "MM1: nor a null");
            t.Check(MeKey.Shader("m").Equals(MeKey.TryOf(new MeShader { Material = "m", RenderQueue = 1 }, out var q) ? q : default), "MM1: a queue-only shader item has the shader key");
            t.Check(MeKey.Enabled("r") == MeKey.Renderer("r", "Enabled"), "MM1: the Enabled key");

            // The stored text, as it stands: nothing added or stripped, case and marks matter.
            var dirty = F("m", "_dirty", 1f);
            t.Check(Key(dirty, "float", "m", "_dirty"), "MM1: `_dirty` is the stored text (the property __dirty)");
            t.Check(!MeKey.Property("float", "m", "dirty").Matches(dirty), "MM1: and `dirty` is another key");
            t.Check(!MeKey.Property("float", "M", "_dirty").Matches(dirty), "MM1: names compare ordinally");
            t.Check(MeKey.Property("float", "m", "a") != MeKey.Property("color", "m", "a") && MeKey.Property("float", "m", "a") != MeKey.Property("float", "n", "a"), "MM1: type and material are part of the key");
            t.Equal(MeKey.Property("float", "m", "a").GetHashCode(), new MeKey("float", "m", "a").GetHashCode(), "MM1: equal keys hash alike");
            t.Check(MeKey.Shader("m") == new MeKey("shader", "m", null) && !MeKey.Shader("m").Equals((object)"x"), "MM1: equality by value");
            t.Equal("float/m/a", MeKey.Property("float", "m", "a").ToString(), "MM1: the text of a key");
            t.Equal("shader/m", MeKey.Shader("m").ToString(), "MM1: the text of a key without a member");

            // The formatter.
            t.Equal("cf_m_skin_body_00", MeKey.Format("cf_m_skin_body_00 (Instance)"), "MM1: Format removes (Instance)");
            t.Equal("a", MeKey.Format("a Instance"), "MM1: Format removes ` Instance`");
            t.Equal("a", MeKey.Format("  a (Instance) (Instance) "), "MM1: Format trims, every mark");
            t.Equal("", MeKey.Format(null), "MM1: Format of null");
            t.Equal("Instance", MeKey.Format("Instance"), "MM1: a name that only is the word stays");

            // Editable types (read-only ones are kept, listed and applied as they stand).
            string[] yes = { "float", "color", "keyword", "texture", "shader" };
            string[] no = { "vector", "copy", "name", "cubemap", "projector" };
            foreach (var y in yes) t.Check(MaterialEdits.IsEditable(new MeKey(y, "m", "a")), "MM1: " + y + " is editable");
            foreach (var n in no) t.Check(!MaterialEdits.IsEditable(new MeKey(n, "m", "a")), "MM1: " + n + " is read-only");
            t.Check(MaterialEdits.IsEditable(MeKey.Enabled("r")), "MM1: a renderer's Enabled is editable");
            foreach (var p in new[] { "ShadowCastingMode", "ReceiveShadows", "RecalculateNormals", "UpdateWhenOffscreen" })
                t.Check(!MaterialEdits.IsEditable(MeKey.Renderer("r", p)), "MM1: " + p + " is read-only");
            t.Equal(7, all.Count(MaterialEdits.IsEditable), "MM1: 7 of the sample are editable");
            t.Check(!MaterialEdits.IsEditable(all[13]), "MM1: an unknown item is read-only");
        }

        // ---------------- MM2 ----------------

        static void MM2(TestRunner t)
        {
            var list = All();
            string before = Text(list);

            void Touches(int index, MaterialEdit edit, string what, Func<MaterialOverride, bool> check)
            {
                var old = All();
                string oldText = Text(old);
                var r = MaterialEdits.Set(old, edit);
                t.Check(!ReferenceEquals(r, old) && r.Count == old.Count, "MM2: " + what + ": a new list of the same length");
                t.Equal(oldText, Text(old), "MM2: " + what + ": the old list and its elements unchanged");
                int same = 0;
                for (int i = 0; i < old.Count; i++) if (ReferenceEquals(old[i], r[i])) same++;
                t.Equal(old.Count - 1, same, "MM2: " + what + ": every other element is the same object");
                t.Check(!ReferenceEquals(old[index], r[index]) && r[index].GetType() == old[index].GetType() && check(r[index]), "MM2: " + what + ": element " + index + " is a changed copy in its place");
                // Only the named member differs in the element's canonical form.
                var a = (JObject)FieldJson.ToJson(old[index]);
                var b = (JObject)FieldJson.ToJson(r[index]);
                t.Equal(a.Properties().Select(p => p.Name).Aggregate((x, y) => x + "," + y), b.Properties().Select(p => p.Name).Aggregate((x, y) => x + "," + y), "MM2: " + what + ": the same members in the same order");
            }

            Touches(6, MaterialEdit.Float("m", "Metallic", 0.75f, 0.1f), "float", e => ((MeFloat)e).Value == 0.75f && ((MeFloat)e).Original == 0.25f);
            Touches(8, MaterialEdit.ColorOf("m", "Color", new Color4(0f, 2f, 0f, 1f), new Color4(1f, 1f, 1f, 1f)), "color", e => ((MeColor)e).Value.Equals(new Color4(0f, 2f, 0f, 1f)));
            Touches(7, MaterialEdit.Keyword("m", "DITHERSWITCH_ON", false, true), "keyword", e => !((MeKeyword)e).Value && !((MeKeyword)e).Original);
            Touches(4, MaterialEdit.RendererEnabled("r", 1, 0), "renderer", e => ((MeRenderer)e).Value == 1 && ((MeRenderer)e).Original == 1);
            Touches(10, MaterialEdit.Texture("m", "MainTex").WithImage(Res2), "texture image", e => ((MeTexture)e).Image == Res2 && ((MeTexture)e).Offset.Value.Equals(new Vec2(0.5f, 0f)));
            Touches(10, MaterialEdit.Texture("m", "MainTex").WithScale(new Vec2(3f, 3f)), "texture scale", e => ((MeTexture)e).Scale.Value.X == 3f && ((MeTexture)e).Image == Res1);
            Touches(10, MaterialEdit.Texture("m", "MainTex").WithOffset(null), "texture offset cleared", e => !((MeTexture)e).Offset.HasValue && ((MeTexture)e).Image == Res1);
            Touches(2, MaterialEdit.ShaderItem("m").WithShader("HS2Next/x", "AIT/z"), "shader name", e => ((MeShader)e).Shader == "HS2Next/x" && ((MeShader)e).ShaderOriginal == "AIT/y" && ((MeShader)e).RenderQueue == 3000);
            Touches(2, MaterialEdit.ShaderItem("m").WithQueue(3100, 2000), "queue", e => ((MeShader)e).RenderQueue == 3100 && ((MeShader)e).Shader == "AIT/x");
            Touches(3, MaterialEdit.ShaderItem("q").WithQueue(2451, 2000), "a queue-only item", e => ((MeShader)e).RenderQueue == 2451 && ((MeShader)e).Shader == null);

            // Exact text of a changed element: the stored original stays.
            var one = MaterialEdits.Set(new List<MaterialOverride> { F("m", "a", 1f, 0.5f) }, MaterialEdit.Float("m", "a", 2f, 0.25f));
            t.Equal("[{\"type\":\"float\",\"material\":\"m\",\"property\":\"a\",\"value\":2.0,\"original\":0.5}]", Compact(one), "MM2: value changed, original kept");

            // New items: members from the edit, originals from the baseline.
            var empty = new List<MaterialOverride>();
            t.Equal("[{\"type\":\"float\",\"material\":\"m\",\"property\":\"a\",\"value\":2.0,\"original\":0.25}]", Compact(MaterialEdits.Set(empty, MaterialEdit.Float("m", "a", 2f, 0.25f))), "MM2: a new float");
            t.Equal("[{\"type\":\"float\",\"material\":\"m\",\"property\":\"a\",\"value\":2.0,\"original\":null}]", Compact(MaterialEdits.Set(empty, MaterialEdit.Float("m", "a", 2f))), "MM2: a new float with no baseline");
            var col = (MeColor)MaterialEdits.Set(empty, MaterialEdit.ColorOf("m", "a", new Color4(1f, 0f, 0f, 1f), new Color4(0f, 0f, 1f, 1f)))[0];
            t.Check(col.Value.R == 1f && col.Original.Value.B == 1f, "MM2: a new color");
            var key = (MeKeyword)MaterialEdits.Set(empty, MaterialEdit.Keyword("m", "K", true, false))[0];
            t.Check(key.Value && !key.Original, "MM2: a new keyword");
            var ren = (MeRenderer)MaterialEdits.Set(empty, MaterialEdit.RendererEnabled("r", 0, 1))[0];
            t.Check(ren.Renderer == "r" && ren.Property == "Enabled" && ren.Value == 0 && ren.Original == 1, "MM2: a new renderer item");
            var tex = (MeTexture)MaterialEdits.Set(empty, MaterialEdit.Texture("m", "a").WithImage(Res1).WithOffset(new Vec2(1f, 0f), new Vec2(0f, 0f)))[0];
            t.Check(tex.Image == Res1 && tex.Offset.Value.X == 1f && tex.OffsetOriginal.Value.X == 0f && !tex.Scale.HasValue && !tex.ScaleOriginal.HasValue, "MM2: a new texture: originals only for what is set");
            var img = (MeTexture)MaterialEdits.Set(empty, MaterialEdit.Texture("m", "a").WithImage(Res1))[0];
            t.Check(img.Image == Res1 && !img.Offset.HasValue && !img.OffsetOriginal.HasValue, "MM2: an image alone has no original in the schema");
            var sh = (MeShader)MaterialEdits.Set(empty, MaterialEdit.ShaderItem("m").WithShader("HS2Next/x", "AIT/a"))[0];
            t.Check(sh.Shader == "HS2Next/x" && sh.ShaderOriginal == "AIT/a" && !sh.RenderQueue.HasValue && !sh.RenderQueueOriginal.HasValue, "MM2: a new shader item");
            var qu = (MeShader)MaterialEdits.Set(empty, MaterialEdit.ShaderItem("m").WithQueue(3000, 2000))[0];
            t.Check(qu.Shader == null && qu.ShaderOriginal == null && qu.RenderQueue == 3000 && qu.RenderQueueOriginal == 2000, "MM2: a new queue-only item");
            t.Equal(0, empty.Count, "MM2: the list given stays empty");
            t.Check(ReferenceEquals(MaterialEdits.Set(empty, MaterialEdit.Texture("m", "a").WithImage(null)), empty), "MM2: a new item that holds nothing is no item");
            t.Equal(1, MaterialEdits.Set(null, MaterialEdit.Float("m", "a", 2f)).Count, "MM2: a null list is empty");

            // A null original is filled the first time its member is written; a stored one stays.
            var f0 = (MeFloat)MaterialEdits.Set(new List<MaterialOverride> { F("m", "a", 1f) }, MaterialEdit.Float("m", "a", 2f, 0.5f))[0];
            t.Check(f0.Original == 0.5f, "MM2: a float's null original filled");
            var c0 = (MeColor)MaterialEdits.Set(new List<MaterialOverride> { C("m", "a", Color4.White) }, MaterialEdit.ColorOf("m", "a", Color4.Black, new Color4(0f, 1f, 0f, 1f)))[0];
            t.Check(c0.Original.Value.G == 1f, "MM2: a color's null original filled");
            var r0 = (MeRenderer)MaterialEdits.Set(new List<MaterialOverride> { new MeRenderer { Renderer = "r", Property = "Enabled", Value = 0 } }, MaterialEdit.RendererEnabled("r", 1, 0))[0];
            t.Check(r0.Value == 1 && r0.Original == 0, "MM2: a renderer's null original filled");
            var t0 = (MeTexture)MaterialEdits.Set(new List<MaterialOverride> { new MeTexture { Material = "m", Property = "a", Image = Res1 } }, MaterialEdit.Texture("m", "a").WithScale(new Vec2(2f, 2f), new Vec2(1f, 1f)))[0];
            t.Check(t0.ScaleOriginal.Value.X == 1f && !t0.OffsetOriginal.HasValue, "MM2: a texture member's null original filled, the other left");
            var s0 = (MeShader)MaterialEdits.Set(new List<MaterialOverride> { new MeShader { Material = "m", RenderQueue = 3000 } }, MaterialEdit.ShaderItem("m").WithShader("HS2Next/x", "AIT/a"))[0];
            t.Check(s0.ShaderOriginal == "AIT/a" && !s0.RenderQueueOriginal.HasValue, "MM2: a shader's null original filled, the queue's left");
            var cleared = (MeTexture)MaterialEdits.Set(new List<MaterialOverride> { new MeTexture { Material = "m", Property = "a", Image = Res1, Offset = new Vec2(1f, 1f), OffsetOriginal = new Vec2(0f, 0f) } }, MaterialEdit.Texture("m", "a").WithOffset(null, new Vec2(5f, 5f)))[0];
            t.Check(!cleared.Offset.HasValue && cleared.OffsetOriginal.Value.X == 0f, "MM2: a cleared member keeps its original as read");

            // A member not named stays; a member given as null clears.
            var tt = new MeTexture { Material = "m", Property = "a", Image = Res1, Offset = new Vec2(1f, 2f), Scale = new Vec2(3f, 4f) };
            var kept = (MeTexture)MaterialEdits.Set(new List<MaterialOverride> { tt }, MaterialEdit.Texture("m", "a").WithImage(null))[0];
            t.Check(kept.Image == null && kept.Offset.HasValue && kept.Scale.HasValue, "MM2: image null clears the image alone");

            // Unknown members of the element are kept by the copy.
            var withUnknown = (MeFloat)FieldJson.FromText("{\"type\":\"float\",\"material\":\"m\",\"property\":\"a\",\"value\":1.0,\"original\":null,\"future\":{\"x\":1}}", typeof(MaterialOverride));
            var u = MaterialEdits.Set(new List<MaterialOverride> { withUnknown }, MaterialEdit.Float("m", "a", 2f));
            t.Check(Compact(u).Contains("\"future\":{\"x\":1}"), "MM2: an unknown member of the element stays");

            // What cannot be stored.
            void Bad(MaterialEdit e, string what)
            {
                t.Check(e.Problem != null, "MM2: " + what + " has a problem");
                var ex = Catch(() => MaterialEdits.Set(new List<MaterialOverride>(), e));
                t.Check(ex != null && ex.Code == CommandCodes.BadArgs, "MM2: " + what + " is bad_args (" + ex?.Message + ")");
            }
            Bad(MaterialEdit.Float("m", "a", float.NaN), "a NaN");
            Bad(MaterialEdit.Float("m", "a", float.PositiveInfinity), "an infinity");
            Bad(MaterialEdit.Float("", "a", 1f), "no material");
            Bad(MaterialEdit.Float("m", null, 1f), "no property");
            Bad(MaterialEdit.ColorOf("m", "a", new Color4(float.NaN, 0f, 0f, 1f)), "a NaN in a color");
            Bad(MaterialEdit.Keyword("m", "", true), "no keyword");
            Bad(MaterialEdit.RendererEnabled("r", 2), "Enabled 2");
            Bad(MaterialEdit.RendererEnabled("", 1), "no renderer");
            Bad(MaterialEdit.Texture("m", "a"), "a texture edit that names nothing");
            Bad(MaterialEdit.Texture("m", "a").WithImage("folder/x.png"), "an image that is no res name");
            Bad(MaterialEdit.Texture("m", "a").WithOffset(new Vec2(float.NaN, 0f)), "a NaN offset");
            Bad(MaterialEdit.Texture("m", "a").WithScale(new Vec2(0f, float.NegativeInfinity)), "an infinite scale");
            Bad(MaterialEdit.ShaderItem("m"), "a shader edit that names nothing");
            Bad(MaterialEdit.ShaderItem("m").WithShader(""), "an empty shader name");
            Bad(MaterialEdit.ShaderItem("m").WithQueue(-1), "queue -1");
            Bad(MaterialEdit.ShaderItem("m").WithQueue(5001), "queue 5001");
            t.Check(MaterialEdit.ShaderItem("m").WithQueue(0).Problem == null && MaterialEdit.ShaderItem("m").WithQueue(5000).Problem == null && MaterialEdit.ShaderItem("m").WithQueue(null).Problem == null, "MM2: queue 0, 5000 and null are fine");
            t.Check(MaterialEdit.ColorOf("m", "a", new Color4(-3f, 40f, 0f, 2f)).Problem == null, "MM2: a color is not clamped");
            t.Check(MaterialEdit.Float("m", "a", -1e9f).Problem == null, "MM2: nor a float");

            // The limit: an edit of an existing item is fine at the limit, a new item is not; a file's larger array stays editable.
            var big = new List<MaterialOverride>();
            for (int i = 0; i < MaterialEdits.MaxItems; i++) big.Add(F("m", "p" + i, 1f));
            t.Equal(4096, big.Count, "MM2: the limit is 4,096");
            var over = Catch(() => MaterialEdits.Set(big, MaterialEdit.Float("m", "new", 2f)));
            t.Check(over != null && over.Code == CommandCodes.BadArgs, "MM2: one more is bad_args");
            t.Equal(4096, MaterialEdits.Set(big, MaterialEdit.Float("m", "p7", 2f)).Count, "MM2: an edit at the limit");
            t.Equal(4095, MaterialEdits.Remove(big, MeKey.Property("float", "m", "p7")).Count, "MM2: a removal at the limit");
            t.Check(ReferenceEquals(MaterialEdits.Set(big, MaterialEdit.Float("m", "new", 2f, 2f)), big), "MM2: an edit that adds nothing passes at the limit");
            t.Equal(before, Text(list), "MM2: the sample is as it was");
        }

        static string Compact(object v) => FieldJson.ToJson(v).ToString(Newtonsoft.Json.Formatting.None);

        static CommandException Catch(Action a)
        {
            try { a(); return null; }
            catch (CommandException e) { return e; }
        }

        // ---------------- MM3 ----------------

        static string Types(List<MaterialOverride> l) => string.Join(",", l.Select(x => x.Type));

        static void MM3(TestRunner t)
        {
            t.Equal(0, MaterialEdits.InsertIndex(new List<MaterialOverride>(), MaterialOverride.Rank("float")), "MM3: an empty list");

            // Every editable type lands after its run in an array holding every type.
            var all = All();
            t.Equal("copy,name,shader,shader,renderer,renderer,float,keyword,color,vector,texture,cubemap,projector,future", Types(all), "MM3: the sample is in apply order");
            t.Equal("copy,name,shader,shader,renderer,renderer,float,float,keyword,color,vector,texture,cubemap,projector,future", Types(MaterialEdits.Set(all, MaterialEdit.Float("m", "new", 1f))), "MM3: a float after the floats");
            t.Equal("copy,name,shader,shader,renderer,renderer,float,keyword,color,color,vector,texture,cubemap,projector,future", Types(MaterialEdits.Set(all, MaterialEdit.ColorOf("m", "new", Color4.Black))), "MM3: a color after the colors");
            t.Equal("copy,name,shader,shader,renderer,renderer,float,keyword,keyword,color,vector,texture,cubemap,projector,future", Types(MaterialEdits.Set(all, MaterialEdit.Keyword("m", "new", true))), "MM3: a keyword after the keywords");
            t.Equal("copy,name,shader,shader,renderer,renderer,renderer,float,keyword,color,vector,texture,cubemap,projector,future", Types(MaterialEdits.Set(all, MaterialEdit.RendererEnabled("s", 1))), "MM3: a renderer after the renderers");
            t.Equal("copy,name,shader,shader,renderer,renderer,float,keyword,color,vector,texture,texture,cubemap,projector,future", Types(MaterialEdits.Set(all, MaterialEdit.Texture("m", "new").WithImage(Res1))), "MM3: a texture after the textures");
            t.Equal("copy,name,shader,shader,shader,renderer,renderer,float,keyword,color,vector,texture,cubemap,projector,future", Types(MaterialEdits.Set(all, MaterialEdit.ShaderItem("n").WithQueue(10))), "MM3: a shader after the shaders");

            // No run of that type: before the first item of a higher rank, else at the end.
            var l = new List<MaterialOverride> { new MeCopy { Material = "m", CopyName = "c" }, C("m", "a", Color4.Black) };
            t.Equal("copy,float,color", Types(MaterialEdits.Set(l, MaterialEdit.Float("m", "b", 1f))), "MM3: before the first higher rank");
            t.Equal("copy,color,texture", Types(MaterialEdits.Set(l, MaterialEdit.Texture("m", "b").WithImage(Res1))), "MM3: at the end when none is higher");
            t.Equal("copy,shader,color", Types(MaterialEdits.Set(l, MaterialEdit.ShaderItem("m").WithQueue(5))), "MM3: a shader before the color");

            // An array out of order stays as it is: only the new element is placed.
            var mixed = new List<MaterialOverride> { C("m", "c1", Color4.Black), F("m", "f1", 1f), C("m", "c2", Color4.Black), F("m", "f2", 1f), new MeShader { Material = "m", RenderQueue = 1 } };
            var r = MaterialEdits.Set(mixed, MaterialEdit.Float("m", "f3", 1f));
            t.Equal("color,float,color,float,float,shader", Types(r), "MM3: after the last float, the others not moved");
            t.Check(mixed.All(x => r.Contains(x)), "MM3: and they are the same objects");
            t.Equal("color,float,color,color,float,shader", Types(MaterialEdits.Set(mixed, MaterialEdit.ColorOf("m", "c3", Color4.Black))), "MM3: after the last color");
            var noRun = new List<MaterialOverride> { C("m", "c1", Color4.Black), new MeShader { Material = "m", RenderQueue = 1 } };
            t.Equal("float,color,shader", Types(MaterialEdits.Set(noRun, MaterialEdit.Float("m", "f", 1f))), "MM3: before the FIRST higher rank, in an unsorted array");
            t.Equal("color,shader,shader,keyword", Types(MaterialEdits.Set(new List<MaterialOverride> { C("m", "c", Color4.Black), new MeShader { Material = "m", RenderQueue = 1 }, new MeKeyword { Material = "m", Property = "k" } }, MaterialEdit.ShaderItem("n").WithQueue(9))), "MM3: a second shader goes by its run, wherever that is");

            // Unknown items are stepped over and never moved.
            var unk = new List<MaterialOverride> { U("a"), F("m", "f", 1f), U("b"), C("m", "c", Color4.Black), U("c") };
            t.Equal("a,float,float,b,color,c", Types(MaterialEdits.Set(unk, MaterialEdit.Float("m", "g", 1f))), "MM3: after the last float, an unknown in the run's way stays");
            t.Equal("a,float,b,keyword,color,c", Types(MaterialEdits.Set(unk, MaterialEdit.Keyword("m", "k", true))), "MM3: before the first higher rank, past the unknown");
            t.Equal("a,float,b,color,c,texture", Types(MaterialEdits.Set(unk, MaterialEdit.Texture("m", "t").WithImage(Res1))), "MM3: at the end, after a trailing unknown");
            var onlyUnknown = new List<MaterialOverride> { U("a"), U("b") };
            t.Equal("a,b,float", Types(MaterialEdits.Set(onlyUnknown, MaterialEdit.Float("m", "f", 1f))), "MM3: only unknown items: at the end");
            t.Equal(MaterialOverride.Rank("copy") + 1, MaterialOverride.Rank("name"), "MM3: the rank table");
            t.Equal(-1, MaterialOverride.Rank("future"), "MM3: an unknown rank");

            // Apply order for items and the map: the rank sort, stable; unknown left out.
            var order = MaterialEdits.InApplyOrder(new List<MaterialOverride> { C("m", "c1", Color4.Black), U("x"), F("m", "f1", 1f), C("m", "c2", Color4.Black), new MeShader { Material = "m", RenderQueue = 1 }, F("m", "f2", 1f) });
            t.Equal("shader,float,float,color,color", Types(order), "MM3: by rank");
            t.Equal("f1,f2", string.Join(",", order.OfType<MeFloat>().Select(x => x.Property)), "MM3: array order inside a type");
            t.Equal("c1,c2", string.Join(",", order.OfType<MeColor>().Select(x => x.Property)), "MM3: for every type");
            t.Equal(0, MaterialEdits.InApplyOrder(null).Count, "MM3: of a null list");
        }

        // ---------------- MM4 ----------------

        static void MM4(TestRunner t)
        {
            var text = new MeFloat { Material = "m", Property = "a", ValueText = "1,5", OriginalText = "2,5" };
            var other = new MeFloat { Material = "m", Property = "b", ValueText = "x", Original = 1f };
            var ren = new MeRenderer { Renderer = "r", Property = "Enabled", ValueText = "on", OriginalText = "off" };
            var list = new List<MaterialOverride> { text, other, ren };
            string before = Text(list);

            var r = MaterialEdits.Set(list, MaterialEdit.Float("m", "a", 3f, 9f));
            var e = (MeFloat)r[0];
            t.Check(e.Value == 3f && e.ValueText == null, "MM4: a number written clears ValueText");
            t.Check(e.OriginalText == "2,5" && !e.Original.HasValue, "MM4: OriginalText stays, and no number is put beside it");
            t.Equal("{\"type\":\"float\",\"material\":\"m\",\"property\":\"a\",\"value\":3.0,\"original\":\"2,5\"}", Compact(e), "MM4: the element's text");
            t.Check(ReferenceEquals(r[1], other) && ReferenceEquals(r[2], ren), "MM4: untouched text items are the same objects");
            t.Equal(Compact(new List<MaterialOverride> { other, ren }), Compact(r.Skip(1).ToList()), "MM4: and byte-equal");
            t.Equal(before, Text(list), "MM4: the old list holds its text");

            var rr = (MeRenderer)MaterialEdits.Set(list, MaterialEdit.RendererEnabled("r", 1, 0))[2];
            t.Check(rr.Value == 1 && rr.ValueText == null && rr.OriginalText == "off" && !rr.Original.HasValue, "MM4: the same for a renderer item");

            // A value kept as text is never equal to a number: the same digits still write.
            var same = new MeFloat { Material = "m", Property = "a", Value = 0f, ValueText = "0" };
            var w = MaterialEdits.Set(new List<MaterialOverride> { same }, MaterialEdit.Float("m", "a", 0f));
            t.Check(((MeFloat)w[0]).ValueText == null, "MM4: a number over text is a change");
            var back = MaterialEdits.Set(new List<MaterialOverride> { same }, MaterialEdit.Float("m", "a", 0f, 0f));
            t.Equal(0, back.Count, "MM4: a number equal to the baseline removes a text item");

            // A text original is not filled.
            var hasText = new MeFloat { Material = "m", Property = "a", Value = 1f, OriginalText = "t" };
            var f = (MeFloat)MaterialEdits.Set(new List<MaterialOverride> { hasText }, MaterialEdit.Float("m", "a", 2f, 5f))[0];
            t.Check(!f.Original.HasValue && f.OriginalText == "t", "MM4: a text original is an original");
        }

        // ---------------- MM5 ----------------

        static void MM5(TestRunner t)
        {
            var all = All();
            string before = Text(all);

            var r = MaterialEdits.Remove(all, MeKey.Property("float", "m", "Metallic"));
            t.Equal(all.Count - 1, r.Count, "MM5: one item leaves");
            t.Check(all.Where((x, i) => i != 6).SequenceEqual(r), "MM5: the others are the same objects, in order");
            t.Equal(before, Text(all), "MM5: the old list is untouched");
            t.Check(ReferenceEquals(MaterialEdits.Remove(all, MeKey.Property("float", "m", "none")), all), "MM5: nothing to remove: the same list");
            t.Check(MaterialEdits.Remove(null, MeKey.Property("float", "m", "a")) == null, "MM5: of a null list");

            // A repeated key: an edit replaces the last one, a removal removes all.
            var a = F("m", "a", 1f);
            var b = F("m", "b", 2f);
            var c = F("m", "a", 3f);
            var dup = new List<MaterialOverride> { a, b, c };
            t.Equal(2, MaterialEdits.LastIndex(dup, MeKey.Property("float", "m", "a")), "MM5: the last index");
            t.Check(ReferenceEquals(MaterialEdits.Find(dup, MeKey.Property("float", "m", "a")), c), "MM5: Find gives the last one, the one that wins at apply");
            t.Equal(-1, MaterialEdits.LastIndex(dup, MeKey.Property("float", "m", "z")), "MM5: no index");
            t.Check(MaterialEdits.Find(dup, MeKey.Property("float", "m", "z")) == null && MaterialEdits.LastIndex(null, MeKey.Shader("m")) == -1, "MM5: nothing found");
            var e = MaterialEdits.Set(dup, MaterialEdit.Float("m", "a", 9f, 0f));
            t.Check(ReferenceEquals(e[0], a) && ReferenceEquals(e[1], b) && !ReferenceEquals(e[2], c) && ((MeFloat)e[2]).Value == 9f, "MM5: an edit replaces the last one");
            var gone = MaterialEdits.Remove(dup, MeKey.Property("float", "m", "a"));
            t.Check(gone.Count == 1 && ReferenceEquals(gone[0], b), "MM5: a removal removes all with the key");
            var toBase = MaterialEdits.Set(dup, MaterialEdit.Float("m", "a", 0f, 0f));
            t.Check(toBase.Count == 1 && ReferenceEquals(toBase[0], b), "MM5: back to the baseline removes all with the key");

            // Read-only: refused, kept.
            foreach (var k in new[] { new MeKey("copy", "m", "m.MECopy1"), new MeKey("name", "r", "m"), new MeKey("vector", "m", "Offsets"), new MeKey("cubemap", "m", "Cube"), new MeKey("projector", "p", "FieldOfView"), MeKey.Renderer("r", "ShadowCastingMode") })
            {
                var ex = Catch(() => MaterialEdits.Remove(all, k));
                t.Check(ex != null && ex.Code == CommandCodes.Unsupported, "MM5: removing a read-only item is unsupported: " + k);
            }

            // Reset all.
            var cleared = MaterialEdits.Clear(all);
            t.Equal("copy,name,renderer,vector,cubemap,projector,future", Types(cleared), "MM5: Reset all keeps the read-only items");
            t.Check(cleared.All(x => all.Contains(x)), "MM5: as the same objects");
            t.Equal(before, Text(all), "MM5: the old list is untouched");
            t.Check(ReferenceEquals(MaterialEdits.Clear(cleared), cleared), "MM5: nothing editable left: the same list");
            t.Equal(0, MaterialEdits.Clear(new List<MaterialOverride> { F("m", "a", 1f) }).Count, "MM5: all editable: empty");
            t.Check(MaterialEdits.Clear(null) == null, "MM5: of a null list");

            // Of one material.
            var two = new List<MaterialOverride> { F("m", "a", 1f), F("n", "a", 1f), new MeShader { Material = "m", RenderQueue = 1 }, new MeCopy { Material = "m", CopyName = "c" }, new MeRenderer { Renderer = "m", Property = "Enabled", Value = 0 }, new MeVector { Material = "m", Property = "v" } };
            var one = MaterialEdits.ClearMaterial(two, "m");
            t.Equal("float,copy,renderer,vector", Types(one), "MM5: one material's editable items go; a renderer of that name and read-only items stay");
            t.Equal("n", ((MeFloat)one[0]).Material, "MM5: the other material's item stays");
            t.Check(ReferenceEquals(MaterialEdits.ClearMaterial(two, "z"), two), "MM5: an unknown material: the same list");
            t.Check(MaterialEdits.ClearMaterial(All(), "m").Count(x => x is MeFloat) == 0, "MM5: on the sample");
        }

        // ---------------- MM6 ----------------

        static void MM6(TestRunner t)
        {
            var empty = new List<MaterialOverride>();
            // No item and a value equal to the baseline: nothing to store, the same list.
            t.Check(ReferenceEquals(MaterialEdits.Set(empty, MaterialEdit.Float("m", "a", 1f, 1f)), empty), "MM6: float equal to the baseline, no item: the same list");
            t.Check(ReferenceEquals(MaterialEdits.Set(empty, MaterialEdit.ColorOf("m", "a", Color4.White, Color4.White)), empty), "MM6: color");
            t.Check(ReferenceEquals(MaterialEdits.Set(empty, MaterialEdit.Keyword("m", "a", true, true)), empty), "MM6: keyword");
            t.Check(ReferenceEquals(MaterialEdits.Set(empty, MaterialEdit.RendererEnabled("r", 1, 1)), empty), "MM6: renderer");
            t.Check(ReferenceEquals(MaterialEdits.Set(empty, MaterialEdit.ShaderItem("m").WithQueue(2000, 2000)), empty), "MM6: a queue alone");
            t.Equal(1, MaterialEdits.Set(empty, MaterialEdit.Float("m", "a", 1.0000001f, 1f)).Count, "MM6: exact, per f32");
            t.Equal(1, MaterialEdits.Set(empty, MaterialEdit.ColorOf("m", "a", new Color4(1f, 1f, 1f, 0.5f), Color4.White)).Count, "MM6: per component");
            t.Equal(1, MaterialEdits.Set(empty, MaterialEdit.Float("m", "a", 1f)).Count, "MM6: with no baseline (the property is absent) the item is stored");
            t.Equal(1, MaterialEdits.Set(empty, MaterialEdit.ShaderItem("m").WithShader("HS2Next/x", "HS2Next/x")).Count, "MM6: a shader name has no baseline rule");
            t.Equal(1, MaterialEdits.Set(empty, MaterialEdit.Texture("m", "a").WithOffset(new Vec2(0f, 0f), new Vec2(0f, 0f))).Count, "MM6: nor a texture's offset");

            // An item whose value comes back to the baseline leaves; the others stay.
            var other = F("m", "b", 2f);
            var list = new List<MaterialOverride> { F("m", "a", 5f, 3f), other };
            var r = MaterialEdits.Set(list, MaterialEdit.Float("m", "a", 4f, 4f));
            t.Check(r.Count == 1 && ReferenceEquals(r[0], other), "MM6: the baseline removes the item, not the stored original");
            var keep = MaterialEdits.Set(list, MaterialEdit.Float("m", "a", 3f, 4f));
            t.Check(keep.Count == 2 && ((MeFloat)keep[0]).Value == 3f, "MM6: a value equal to the stored original stays when the baseline differs");
            t.Equal(0, MaterialEdits.Set(new List<MaterialOverride> { C("m", "a", Color4.Black) }, MaterialEdit.ColorOf("m", "a", Color4.White, Color4.White)).Count, "MM6: a color");
            t.Equal(0, MaterialEdits.Set(new List<MaterialOverride> { new MeKeyword { Material = "m", Property = "a", Value = true } }, MaterialEdit.Keyword("m", "a", false, false)).Count, "MM6: a keyword");
            t.Equal(0, MaterialEdits.Set(new List<MaterialOverride> { new MeRenderer { Renderer = "r", Property = "Enabled", Value = 0, Original = 1 } }, MaterialEdit.RendererEnabled("r", 1, 1)).Count, "MM6: a renderer");

            // The queue: a member, so the item stays while it holds a shader.
            var both = new List<MaterialOverride> { new MeShader { Material = "m", Shader = "HS2Next/x", ShaderOriginal = "AIT/a", RenderQueue = 3000, RenderQueueOriginal = 2000 } };
            var q = MaterialEdits.Set(both, MaterialEdit.ShaderItem("m").WithQueue(2500, 2500));
            t.Check(q.Count == 1 && ((MeShader)q[0]).Shader == "HS2Next/x" && !((MeShader)q[0]).RenderQueue.HasValue && ((MeShader)q[0]).RenderQueueOriginal == 2000, "MM6: a queue back at its baseline leaves the item with its shader");
            var only = new List<MaterialOverride> { new MeShader { Material = "m", RenderQueue = 3000 } };
            t.Equal(0, MaterialEdits.Set(only, MaterialEdit.ShaderItem("m").WithQueue(2500, 2500)).Count, "MM6: a queue-only item goes");
            var named = MaterialEdits.Set(new List<MaterialOverride>(), MaterialEdit.ShaderItem("m").WithShader("HS2Next/x", "AIT/a").WithQueue(2500, 2500));
            t.Check(named.Count == 1 && !((MeShader)named[0]).RenderQueue.HasValue && !((MeShader)named[0]).RenderQueueOriginal.HasValue, "MM6: a new item with a queue at its baseline stores the name alone");

            // Items left with nothing go.
            var tex = new List<MaterialOverride> { new MeTexture { Material = "m", Property = "a", Image = Res1, Offset = new Vec2(1f, 1f) } };
            t.Equal(1, MaterialEdits.Set(tex, MaterialEdit.Texture("m", "a").WithImage(null)).Count, "MM6: an image removed leaves the offset");
            t.Equal(0, MaterialEdits.Set(tex, MaterialEdit.Texture("m", "a").WithImage(null).WithOffset(null)).Count, "MM6: nothing left: the item goes");
            t.Equal(0, MaterialEdits.Set(new List<MaterialOverride> { new MeShader { Material = "m", Shader = "s" } }, MaterialEdit.ShaderItem("m").WithShader(null)).Count, "MM6: a shader item emptied");
            t.Equal(1, MaterialEdits.Set(new List<MaterialOverride> { new MeShader { Material = "m", Shader = "s", RenderQueue = 1 } }, MaterialEdit.ShaderItem("m").WithShader(null)).Count, "MM6: a shader item keeps its queue");

            // No-op items of a file stay until someone edits them: an edit next to one leaves it, and so does writing its own value.
            var noop = F("m", "a", 1f, 1f);
            var imported = new List<MaterialOverride> { noop, F("m", "b", 2f, 3f) };
            var e = MaterialEdits.Set(imported, MaterialEdit.Float("m", "b", 9f, 3f));
            t.Check(e.Count == 2 && ReferenceEquals(e[0], noop), "MM6: an edit of another item leaves a no-op item");
            t.Check(ReferenceEquals(MaterialEdits.Set(imported, MaterialEdit.Float("m", "a", 1f, 1f)), imported), "MM6: its own value again is no change, baseline or not");
            t.Equal(1, MaterialEdits.Set(imported, MaterialEdit.Float("m", "a", 4f, 4f)).Count, "MM6: an edit to the baseline removes it");

            // A value already stored is no change.
            var stored = new List<MaterialOverride> { F("m", "a", 2f), C("m", "c", Color4.Black), new MeKeyword { Material = "m", Property = "k", Value = true }, new MeRenderer { Renderer = "r", Property = "Enabled", Value = 0 }, new MeTexture { Material = "m", Property = "t", Image = Res1, Offset = new Vec2(1f, 2f) }, new MeShader { Material = "m", Shader = "s", RenderQueue = 5 } };
            t.Check(ReferenceEquals(MaterialEdits.Set(stored, MaterialEdit.Float("m", "a", 2f, 0f)), stored), "MM6: float stored");
            t.Check(ReferenceEquals(MaterialEdits.Set(stored, MaterialEdit.ColorOf("m", "c", Color4.Black)), stored), "MM6: color stored");
            t.Check(ReferenceEquals(MaterialEdits.Set(stored, MaterialEdit.Keyword("m", "k", true)), stored), "MM6: keyword stored");
            t.Check(ReferenceEquals(MaterialEdits.Set(stored, MaterialEdit.RendererEnabled("r", 0)), stored), "MM6: Enabled stored");
            t.Check(ReferenceEquals(MaterialEdits.Set(stored, MaterialEdit.Texture("m", "t").WithImage(Res1).WithOffset(new Vec2(1f, 2f)).WithScale(null)), stored), "MM6: texture members stored");
            t.Check(ReferenceEquals(MaterialEdits.Set(stored, MaterialEdit.ShaderItem("m").WithShader("s").WithQueue(5)), stored), "MM6: shader members stored");
            t.Check(!ReferenceEquals(MaterialEdits.Set(stored, MaterialEdit.Texture("m", "t").WithOffset(new Vec2(1f, 3f))), stored), "MM6: a new member is a change");
        }

        // Each step of a drag starts from the array at the press, so the result depends only on the last value.
        static void MM6Drag(TestRunner t)
        {
            var other = F("m", "z", 7f);
            var press = new List<MaterialOverride> { F("m", "a", 5f, 3f), other };
            var pressText = Text(press);

            // Through the baseline and on: the item keeps its place and its stored original.
            MaterialEdits.Set(press, MaterialEdit.Float("m", "a", 4f, 4f));
            var through = MaterialEdits.Set(press, MaterialEdit.Float("m", "a", 6f, 4f));
            t.Check(through.Count == 2 && ((MeFloat)through[0]).Value == 6f && ((MeFloat)through[0]).Original == 3f, "MM6 drag: past the baseline the imported item keeps its place and original");
            t.Equal(pressText, Text(press), "MM6 drag: the array at the press is untouched");

            // Ending where it began: the same list, so no entry.
            t.Check(ReferenceEquals(MaterialEdits.Set(press, MaterialEdit.Float("m", "a", 5f, 4f)), press), "MM6 drag: a drag back to the start is the array at the press");

            // A no-op item (value = baseline) dragged away and back stays.
            var noop = new List<MaterialOverride> { F("m", "a", 4f, 4f) };
            t.Equal(0, MaterialEdits.Set(new List<MaterialOverride> { F("m", "b", 1f) }, MaterialEdit.Float("m", "b", 4f, 4f)).Count, "MM6 drag: (a value at the baseline otherwise removes)");
            t.Check(ReferenceEquals(MaterialEdits.Set(noop, MaterialEdit.Float("m", "a", 4f, 4f)), noop), "MM6 drag: a no-op item dragged back to its value stays");

            // A new item: the press had none, so the baseline at the end means nothing was added.
            var none = new List<MaterialOverride> { other };
            t.Equal(2, MaterialEdits.Set(none, MaterialEdit.Float("m", "a", 1f, 4f)).Count, "MM6 drag: a step away adds it");
            t.Check(ReferenceEquals(MaterialEdits.Set(none, MaterialEdit.Float("m", "a", 4f, 4f)), none), "MM6 drag: the last step on the baseline leaves the array at the press");
        }

        // ---------------- MM7 ----------------

        static void MM7Mapped(TestRunner t)
        {
            string M(params MaterialOverride[] l) => MeStructure.OfMapped(l);
            MeShader Q(int q) => new MeShader { Material = "m", RenderQueue = q };
            t.Equal("", MeStructure.OfMapped(null), "MM7 mapped: null");
            t.Equal("", M(), "MM7 mapped: empty");
            t.Equal("", M(F("m", "a", 1f), C("m", "b", Color4.White), new MeRenderer { Renderer = "r", Property = "Enabled", Value = 0 }), "MM7 mapped: values the mapping does not read");
            // the queue: each band's edges
            foreach (var (a, b) in new[] { (2449, 2450), (2500, 2501), (2999, 3000) }) t.Check(M(Q(a)) != M(Q(b)), "MM7 mapped: " + a + " and " + b + " are two bands");
            foreach (var (a, b) in new[] { (0, 2449), (2450, 2500), (2501, 2999), (3000, 5000) }) t.Equal(M(Q(a)), M(Q(b)), "MM7 mapped: " + a + " and " + b + " are one band");
            t.Check(M(Q(2000)).Length > 0, "MM7 mapped: an item differs from none in any band");
            t.Equal(M(Q(2000)), M(new MeShader { Material = "m", RenderQueue = 2000, RenderQueueOriginal = 2600 }), "MM7 mapped: the stored original is not read");
            t.Check(M(Q(2000)) != M(new MeShader { Material = "n", RenderQueue = 2000 }), "MM7 mapped: the material's name is");
            t.Equal("", M(new MeShader { Material = "m", ShaderOriginal = "o", RenderQueueOriginal = 3000 }), "MM7 mapped: no queue, nothing");
            t.Equal("", MeStructure.Of(new MaterialOverride[] { Q(3000) }), "MM7 mapped: Of stays without the queue, for items and the map");
            // images
            foreach (var p in new[] { "BumpMap", "SpecGlossMap", "MetallicGlossMap", "MainTex" })
            {
                var one = new MeTexture { Material = "m", Property = p, Image = Res1 };
                t.Check(M(one).Length > 0, "MM7 mapped: an image on " + p);
                t.Equal(M(one), M(new MeTexture { Material = "m", Property = p, Image = Res2, Scale = new Vec2(2f, 2f) }), "MM7 mapped: which image does not matter on " + p);
                t.Equal("", M(new MeTexture { Material = "m", Property = p, Offset = new Vec2(0.5f, 0f), Scale = new Vec2(2f, 2f) }), "MM7 mapped: offset or scale alone adds nothing on " + p);
            }
            t.Equal("", M(new MeTexture { Material = "m", Property = "DetailMask", Image = Res1 }), "MM7 mapped: an image the mapping does not read");
            // emission
            string black = M(C("m", "EmissionColor", new Color4(0f, 0f, 0f, 1f)));
            t.Check(black.Length > 0, "MM7 mapped: an emission colour of 0 differs from none");
            t.Check(black != M(C("m", "EmissionColor", new Color4(0f, 0.01f, 0f, 1f))), "MM7 mapped: emission 0 and above");
            t.Equal(M(C("m", "EmissionColor", new Color4(0.5f, 0f, 0f, 1f))), M(C("m", "EmissionColor", new Color4(0f, 0f, 2f, 0f))), "MM7 mapped: above 0 is one state");
            t.Equal(black, M(C("m", "EmissionColor", new Color4(0f, 0f, 0f, 0f))), "MM7 mapped: alpha is not read");
            // the cull ResolveFor reads
            foreach (var p in new[] { "Cull", "CullMode" })
            {
                t.Check(M(F("m", p, 0f)) != M(F("m", p, 2f)), "MM7 mapped: " + p + " under and over 0.5");
                t.Equal(M(F("m", p, 0f)), M(F("m", p, 0.25f)), "MM7 mapped: " + p + " under 0.5 is one state");
            }
        }

        static void MM7(TestRunner t)
        {
            MM7Mapped(t);
            t.Equal("", MeStructure.Of(null), "MM7: null");
            t.Equal("", MeStructure.Of(new List<MaterialOverride>()), "MM7: empty");
            var values = new List<MaterialOverride>
            {
                F("m", "a", 1f), C("m", "b", Color4.Black), new MeKeyword { Material = "m", Property = "k", Value = true }, new MeVector { Material = "m", Property = "v" },
                new MeTexture { Material = "m", Property = "t", Image = Res1 }, new MeShader { Material = "m", RenderQueue = 3000 },
                new MeRenderer { Renderer = "r", Property = "Enabled", Value = 0 }, new MeCubemap { Material = "m", Property = "c" }, new MeProjector { Projector = "p", Property = "FieldOfView", Value = "1" }, U("x"),
            };
            t.Equal("", MeStructure.Of(values), "MM7: an array of values has no structure");
            t.Check(values.All(x => !MeStructure.IsStructural(x)), "MM7: none of them is structural");

            var sh = new MeShader { Material = "m", Shader = "s", ShaderOriginal = "o", RenderQueue = 1, RenderQueueOriginal = 2 };
            var ren = new MeRenderer { Renderer = "r", Property = "ReceiveShadows", Value = 0 };
            var copy = new MeCopy { Material = "m", CopyName = "c" };
            var name = new MeName { Renderer = "r", Material = "m", Value = "v" };
            foreach (var x in new MaterialOverride[] { sh, ren, copy, name }) t.Check(MeStructure.IsStructural(x), "MM7: " + x.Type + " is structural");
            string text = MeStructure.Of(new List<MaterialOverride> { sh, ren, copy, name });
            t.Check(text.Length > 0 && text.Contains("\"shader\": \"s\"") && text.Contains("ReceiveShadows") && text.Contains("\"copyName\": \"c\""), "MM7: the structural items are in the text: " + text);

            string Of(params MaterialOverride[] l) => MeStructure.Of(l);
            string one = Of(sh);
            t.Equal(one, Of(new MeShader { Material = "m", Shader = "s", ShaderOriginal = "other", RenderQueue = 9, RenderQueueOriginal = 8 }), "MM7: queue and originals are not in a shader's text");
            t.Equal(one, Of(F("m", "a", 1f), sh, C("m", "b", Color4.Black)), "MM7: values around it change nothing");
            t.Equal(one, Of(F("m", "a", 2f), sh), "MM7: nor a value's change");
            t.Check(one != Of(new MeShader { Material = "m", Shader = "t" }), "MM7: the shader's name is");
            t.Check(one != Of(new MeShader { Material = "n", Shader = "s" }), "MM7: and its material");
            t.Check(Of(F("m", "a", 1f)) != Of(F("m", "a", 1f), sh), "MM7: a shader name added is a structural change");
            t.Equal("", Of(new MeShader { Material = "m", RenderQueue = 1 }), "MM7: a queue added is not");
            t.Check(Of(ren) != Of(new MeRenderer { Renderer = "r", Property = "ReceiveShadows", Value = 1 }), "MM7: a read-only renderer's value is");
            t.Equal("", Of(new MeRenderer { Renderer = "r", Property = "Enabled", Value = 1 }), "MM7: Enabled is not");
            t.Check(Of(copy) != Of(new MeCopy { Material = "m", CopyName = "d" }), "MM7: a copy's name is");
            t.Check(Of(name) != Of(new MeName { Renderer = "r", Material = "m", Value = "w" }), "MM7: a name item's value is");
            t.Check(Of(sh, copy) != Of(copy, sh), "MM7: order counts");

            // Through the edits: only a shader name changes the text.
            var list = new List<MaterialOverride> { sh, F("m", "a", 1f) };
            string s0 = MeStructure.Of(list);
            t.Equal(s0, MeStructure.Of(MaterialEdits.Set(list, MaterialEdit.Float("m", "a", 2f))), "MM7: a float edit");
            t.Equal(s0, MeStructure.Of(MaterialEdits.Set(list, MaterialEdit.ColorOf("m", "c", Color4.White))), "MM7: a color added");
            t.Equal(s0, MeStructure.Of(MaterialEdits.Set(list, MaterialEdit.ShaderItem("m").WithQueue(9))), "MM7: a queue edit");
            t.Equal(s0, MeStructure.Of(MaterialEdits.Remove(list, MeKey.Property("float", "m", "a"))), "MM7: a removal of a value");
            t.Check(s0 != MeStructure.Of(MaterialEdits.Set(list, MaterialEdit.ShaderItem("m").WithShader("u"))), "MM7: a shader edit");
            t.Check(s0 != MeStructure.Of(MaterialEdits.Remove(list, MeKey.Shader("m"))), "MM7: a shader removed");
        }

        // ---------------- MM8 ----------------

        const string LabelEdit = "Material edit";

        static List<MaterialOverride> ItemList(SceneSession s) => ((ItemNodeState)s.Model.Find(Kit.GoldenItem).Doc.State).MaterialOverrides;
        static List<MaterialOverride> MapList(SceneSession s) => s.Model.Settings.Map.MaterialOverrides;

        static CommandResult Op(SceneSession s, string nodeId, string path, List<MaterialOverride> value, ExecuteOptions o = default)
        {
            using (var tx = s.Commands.BeginOrJoin(LabelEdit, o))
            {
                var r = tx.Set(nodeId, path, value);
                if (r.Status == CommandStatus.Error) return r;
                return tx.Commit();
            }
        }

        static void MM8Entries(TestRunner t)
        {
            // Item and map, then the character's arrays, through the same path form.
            foreach (var (name, id, path, get) in new (string, string, string, Func<SceneSession, List<MaterialOverride>>)[]
            {
                ("item", Kit.GoldenItem, Item, ItemList),
                ("map", null, Map, MapList),
            })
            {
                var s = Kit.GoldenSession(t);
                t.Check(get(s) != null && get(s).Count == 0, "MM8 " + name + ": starts empty, not null");
                var bytes0 = Kit.Bytes(s);
                t.Check(!Kit.Text(bytes0).Contains("materialOverrides"), "MM8 " + name + ": no member in the document at first");
                var rec = new EventRecorder(s);
                var states = new List<string> { Text(get(s)) };
                int n0 = s.Commands.UndoList.Count;
                void Step(Func<List<MaterialOverride>, List<MaterialOverride>> edit, string what)
                {
                    int n = s.Commands.UndoList.Count;
                    rec.Clear();
                    Kit.Ok(t, Op(s, id, path, edit(get(s))), "MM8 " + name + ": " + what);
                    t.Check(s.Commands.UndoList.Count == n + 1 && s.Commands.UndoList[0].Label == LabelEdit, "MM8 " + name + ": " + what + " is one entry");
                    var changes = rec.Batches.SelectMany(b => b.Changes).ToList();
                    t.Check(changes.Count > 0 && changes.All(c => FieldPath.Overlaps(path, c.Path)), "MM8 " + name + ": " + what + " reports the array's path");
                    states.Add(Text(get(s)));
                }
                Step(l => MaterialEdits.Set(l, MaterialEdit.Float("m", "a", 1f, 0f)), "a float");
                Step(l => MaterialEdits.Set(l, MaterialEdit.ColorOf("m", "b", new Color4(1f, 0f, 0f, 1f), Color4.White)), "a color");
                Step(l => MaterialEdits.Set(l, MaterialEdit.Keyword("m", "K", true, false)), "a keyword");
                Step(l => MaterialEdits.Set(l, MaterialEdit.RendererEnabled("a/b[1]/c", 0, 1)), "a renderer by path");
                Step(l => MaterialEdits.Set(l, MaterialEdit.Texture("m", "MainTex").WithImage(Res1).WithScale(new Vec2(2f, 2f), new Vec2(1f, 1f))), "a texture");
                Step(l => MaterialEdits.Set(l, MaterialEdit.ShaderItem("m").WithShader("HS2Next/x", "AIT/a").WithQueue(3000, 2000)), "a shader");
                Step(l => MaterialEdits.Set(l, MaterialEdit.Float("m", "a", 2f, 0f)), "an edit");
                Step(l => MaterialEdits.Remove(l, MeKey.Enabled("a/b[1]/c")), "a removal");
                t.Equal("shader,float,keyword,color,texture", Types(get(s)), "MM8 " + name + ": the array in type runs");
                Kit.Valid(t, s, "MM8 " + name);

                // The document holds it, and the save would write it.
                var bytes1 = Kit.Bytes(s);
                t.Check(Kit.Text(bytes1).Contains("materialOverrides"), "MM8 " + name + ": the document holds the member");
                string read = FieldAccessor.GetJson(s.Model, id, path);
                t.Equal(Compact(get(s)), JToken.Parse(read).ToString(Newtonsoft.Json.Formatting.None), "MM8 " + name + ": the field path reads the array");

                // Undo and redo walk every state.
                int steps = states.Count - 1;
                bool undoOk = true, redoOk = true;
                for (int i = steps - 1; i >= 0; i--)
                {
                    Kit.Ok(t, s.Commands.Undo(), "MM8 " + name + ": undo " + i);
                    if (Text(get(s)) != states[i]) undoOk = false;
                }
                t.Check(undoOk, "MM8 " + name + ": undo restores the JSON at every step");
                t.Check(get(s).Count == 0 && s.Commands.UndoList.Count == n0 && Kit.Same(bytes0, Kit.Bytes(s)), "MM8 " + name + ": all undone, byte-equal to the start");
                for (int i = 0; i < steps; i++)
                {
                    Kit.Ok(t, s.Commands.Redo(), "MM8 " + name + ": redo " + i);
                    if (Text(get(s)) != states[i + 1]) redoOk = false;
                }
                t.Check(redoOk, "MM8 " + name + ": redo restores the JSON at every step");
                t.Check(Kit.Same(bytes1, Kit.Bytes(s)), "MM8 " + name + ": redone, byte-equal to the end");
                Kit.Valid(t, s, "MM8 " + name + " after undo and redo");

                // The whole array cleared: the member leaves the document.
                Kit.Ok(t, Op(s, id, path, MaterialEdits.Clear(get(s))), "MM8 " + name + ": Reset all");
                t.Check(Kit.Same(bytes0, Kit.Bytes(s)), "MM8 " + name + ": empty again, byte-equal to one that never had it");

                // The same value again records nothing.
                Kit.Ok(t, Op(s, id, path, MaterialEdits.Set(get(s), MaterialEdit.Float("m", "a", 1f))), "MM8 " + name + ": one more");
                int nn = s.Commands.UndoList.Count;
                var same = Op(s, id, path, get(s));
                t.Check(same.Status != CommandStatus.Error && s.Commands.UndoList.Count == nn, "MM8 " + name + ": the same array records nothing (" + same + ")");
            }

            // An array holding what the app cannot read can still be edited: the unknown item and the text stay, byte for byte.
            var u = Kit.GoldenSession(t);
            var hold = new List<MaterialOverride> { U("future"), new MeFloat { Material = "m", Property = "t", ValueText = "x" }, new MeProjector { Projector = "p", Property = "SomethingNew", Value = "1" } };
            Kit.Ok(t, Op(u, Kit.GoldenItem, Item, hold), "MM8 unknown: set on an item");
            Kit.Ok(t, Op(u, null, Map, hold), "MM8 unknown: set on the map");
            string kept = Compact(hold);
            t.Equal(kept, Compact(ItemList(u)), "MM8 unknown: stored as given on the item");
            t.Equal(kept, Compact(MapList(u)), "MM8 unknown: on the map");
            Kit.Ok(t, Op(u, Kit.GoldenItem, Item, MaterialEdits.Set(ItemList(u), MaterialEdit.Float("m", "a", 1f))), "MM8 unknown: an edit beside them");
            t.Check(Compact(ItemList(u)).Contains("\"type\":\"future\"") && Compact(ItemList(u)).Contains("\"value\":\"x\"") && Compact(ItemList(u)).Contains("SomethingNew"), "MM8 unknown: they stay");

            // What the writer refuses is a bad_args and no change.
            var b = Kit.GoldenSession(t);
            string before = Compact(ItemList(b));
            Kit.Fails(t, Op(b, Kit.GoldenItem, Item, new List<MaterialOverride> { new MeTexture { Material = "m", Property = "a", Image = "no res name" } }), CommandCodes.BadArgs, "MM8: an image that is no res name");
            Kit.Fails(t, Op(b, Kit.GoldenItem, Item, new List<MaterialOverride> { new MeFloat { Material = "m", Property = "a", Value = float.NaN } }), CommandCodes.BadArgs, "MM8: a NaN");
            Kit.Fails(t, Op(b, Kit.GoldenItem, Item, null), CommandCodes.BadArgs, "MM8: null is not an array");
            t.Equal(before, Compact(ItemList(b)), "MM8: and nothing changed");

            // The arrays of a non-item.
            var w = Kit.GoldenSession(t);
            Kit.Fails(t, w.Set(Kit.GoldenLight, Item, new List<MaterialOverride>()), CommandCodes.WrongKind, "MM8: item.materialOverrides on a light");

            // Character arrays: the same forms on the appearance paths.
            var c = Kit.GoldenSession(t, true);
            const string top = "appearance.chara.materialOverrides", hair = "appearance.chara.hair.back.materialOverrides", clothes = "appearance.coord.clothes.top.materialOverrides";
            var st = (List<MaterialOverride>)FieldAccessor.Get(c.Model, Kit.GoldenChara, top);
            var before2 = Text(st);
            foreach (var p in new[] { top, hair, clothes })
            {
                var cur = (List<MaterialOverride>)FieldAccessor.Get(c.Model, Kit.GoldenChara, p);
                string was = Text(cur);
                Kit.Ok(t, Op(c, Kit.GoldenChara, p, MaterialEdits.Set(cur, MaterialEdit.Float("m", "new", 1f, 0f))), "MM8 character: an edit on " + p);
                var now = (List<MaterialOverride>)FieldAccessor.Get(c.Model, Kit.GoldenChara, p);
                t.Check(now.OfType<MeFloat>().Any(x => x.Property == "new"), "MM8 character: stored on " + p);
                Kit.Ok(t, c.Commands.Undo(), "MM8 character: undo on " + p);
                t.Equal(was, Text((List<MaterialOverride>)FieldAccessor.Get(c.Model, Kit.GoldenChara, p)), "MM8 character: restored on " + p);
            }
            t.Equal(before2, Text((List<MaterialOverride>)FieldAccessor.Get(c.Model, Kit.GoldenChara, top)), "MM8 character: as at first");
        }

        static void MM8Merges(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            string before = Text(ItemList(s));
            int n = s.Commands.UndoList.Count;
            string key = "ui.typed/" + Kit.GoldenItem + "/item/" + MeKey.Property("float", "m", "a");
            var o = new ExecuteOptions { MergeKey = key, Source = CommandSource.Bridge, Label = LabelEdit };
            for (int i = 1; i <= 3; i++)
                Kit.Ok(t, s.Set(Kit.GoldenItem, Item, MaterialEdits.Set(ItemList(s), MaterialEdit.Float("m", "a", i, 0f)), o), "MM8 merge: set " + i);
            t.Check(s.Commands.UndoList.Count == n + 1 && s.Commands.UndoList[0].Label == LabelEdit && ((MeFloat)ItemList(s)[0]).Value == 3f, "MM8 merge: three sets with one key are one entry");
            string merged = Text(ItemList(s));
            Kit.Ok(t, s.Commands.Undo(), "MM8 merge: undo");
            t.Equal(before, Text(ItemList(s)), "MM8 merge: undo restores the array before the first");
            Kit.Ok(t, s.Commands.Redo(), "MM8 merge: redo");
            t.Equal(merged, Text(ItemList(s)), "MM8 merge: redo gives the last");

            // Another key: another entry. A save seals the entry before it.
            int m = s.Commands.UndoList.Count;
            Kit.Ok(t, s.Set(Kit.GoldenItem, Item, MaterialEdits.Set(ItemList(s), MaterialEdit.Float("m", "b", 1f)), new ExecuteOptions { MergeKey = "other", Label = LabelEdit }), "MM8 merge: another key");
            t.Equal(m + 1, s.Commands.UndoList.Count, "MM8 merge: another key is its own entry");
            s.Commands.MarkSaved();
            Kit.Ok(t, s.Set(Kit.GoldenItem, Item, MaterialEdits.Set(ItemList(s), MaterialEdit.Float("m", "b", 2f)), new ExecuteOptions { MergeKey = "other", Label = LabelEdit }), "MM8 merge: after a save");
            t.Equal(m + 2, s.Commands.UndoList.Count, "MM8 merge: a set after a save starts a new entry");

            // A drag: a UI edit opens one transaction, each step sets the array, the commit is one entry; a cancel leaves nothing.
            var d = Kit.GoldenSession(t);
            Kit.Ok(t, Op(d, null, Map, MaterialEdits.Set(MapList(d), MaterialEdit.Float("m", "a", 5f, 3f))), "MM8 drag: an item");
            string press = Text(MapList(d));
            var atPress = new List<MaterialOverride>(MapList(d));
            int dn = d.Commands.UndoList.Count;
            using (var tx = d.Commands.BeginOrJoin(LabelEdit))
            {
                for (int i = 1; i <= 120; i++) Kit.Ok(t, tx.Set(null, Map, MaterialEdits.Set(atPress, MaterialEdit.Float("m", "a", 5f + i, 3f))), "MM8 drag: step " + i);
                Kit.Ok(t, tx.Commit(), "MM8 drag: commit");
            }
            t.Check(d.Commands.UndoList.Count == dn + 1 && ((MeFloat)MapList(d)[0]).Value == 125f, "MM8 drag: 120 steps are one entry");
            Kit.Ok(t, d.Commands.Undo(), "MM8 drag: undo");
            t.Equal(press, Text(MapList(d)), "MM8 drag: undo gives the array at the press");
            atPress = new List<MaterialOverride>(MapList(d));
            using (var tx = d.Commands.BeginOrJoin(LabelEdit))
            {
                Kit.Ok(t, tx.Set(null, Map, MaterialEdits.Set(atPress, MaterialEdit.Float("m", "a", 9f, 3f))), "MM8 drag: a step to cancel");
                tx.Cancel();
            }
            t.Equal(press, Text(MapList(d)), "MM8 drag: a cancelled drag restores the array");
            t.Equal(dn, d.Commands.UndoList.Count, "MM8 drag: and leaves no entry");
            using (var tx = d.Commands.BeginOrJoin(LabelEdit))
            {
                Kit.Ok(t, tx.Set(null, Map, MaterialEdits.Set(atPress, MaterialEdit.Float("m", "a", 9f, 3f))), "MM8 drag: away");
                var back = MaterialEdits.Set(atPress, MaterialEdit.Float("m", "a", 5f, 3f));
                t.Check(ReferenceEquals(back, atPress), "MM8 drag: the last step is the array at the press");
                Kit.Ok(t, tx.Set(null, Map, back), "MM8 drag: back");
                tx.Commit();
            }
            t.Equal(press, Text(MapList(d)), "MM8 drag: a drag back to the start leaves the array equal");
            t.Equal(dn, d.Commands.UndoList.Count, "MM8 drag: and adds no entry");
        }

        static void MM8NotRecordedAndBatch(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            Kit.Ok(t, Op(s, Kit.GoldenItem, Item, MaterialEdits.Set(ItemList(s), MaterialEdit.Float("m", "a", 1f))), "MM8 undo:false: a recorded set");
            int n = s.Commands.UndoList.Count;
            long v = s.Commands.Version;
            Kit.Ok(t, Op(s, Kit.GoldenItem, Item, MaterialEdits.Set(ItemList(s), MaterialEdit.Float("m", "b", 1f)), ExecuteOptions.NotRecorded), "MM8 undo:false: through a transaction");
            Kit.Ok(t, s.Set(null, Map, MaterialEdits.Set(MapList(s), MaterialEdit.Float("m", "c", 1f)), ExecuteOptions.NotRecorded), "MM8 undo:false: on the bus");
            t.Check(ItemList(s).Count == 2 && MapList(s).Count == 1, "MM8 undo:false: applied");
            t.Check(s.Commands.UndoList.Count == n && s.Commands.UndoList[0].Label == LabelEdit, "MM8 undo:false: nothing recorded, the history kept");
            t.Check(s.Commands.Version > v && s.Commands.IsDirty, "MM8 undo:false: the version rises, the scene is modified");

            // A client batch: edits of both arrays join its one entry.
            var b = Kit.GoldenSession(t);
            string item0 = Text(ItemList(b)), map0 = Text(MapList(b));
            int bn = b.Commands.UndoList.Count;
            using (var tx = b.Commands.Begin("batch", new ExecuteOptions { Source = CommandSource.Bridge }))
            {
                Kit.Ok(t, Op(b, Kit.GoldenItem, Item, MaterialEdits.Set(ItemList(b), MaterialEdit.Float("m", "a", 1f))), "MM8 batch: the item");
                Kit.Ok(t, Op(b, Kit.GoldenItem, Item, MaterialEdits.Set(ItemList(b), MaterialEdit.ColorOf("m", "b", Color4.Black))), "MM8 batch: again");
                Kit.Ok(t, Op(b, null, Map, MaterialEdits.Set(MapList(b), MaterialEdit.Float("m", "c", 1f))), "MM8 batch: the map");
                Kit.Ok(t, tx.Commit(), "MM8 batch: commit");
            }
            t.Check(b.Commands.UndoList.Count == bn + 1 && b.Commands.UndoList[0].Label == "batch", "MM8 batch: one entry");
            string item1 = Text(ItemList(b)), map1 = Text(MapList(b));
            Kit.Ok(t, b.Commands.Undo(), "MM8 batch: undo");
            t.Check(Text(ItemList(b)) == item0 && Text(MapList(b)) == map0, "MM8 batch: undo restores both arrays");
            Kit.Ok(t, b.Commands.Redo(), "MM8 batch: redo");
            t.Check(Text(ItemList(b)) == item1 && Text(MapList(b)) == map1, "MM8 batch: redo restores both arrays");
        }

        static void MM8SetMap(TestRunner t)
        {
            // Another map: one entry clears the array; undo brings it and the ref back.
            var s = Kit.GoldenSession(t);
            Kit.Ok(t, s.SetMap("test.mod#1"), "MM8 map: a map");
            Kit.Ok(t, Op(s, null, Map, MaterialEdits.Set(MapList(s), MaterialEdit.Float("m", "a", 1f, 0f))), "MM8 map: an edit");
            string edited = Text(MapList(s));
            int n = s.Commands.UndoList.Count;
            Kit.Ok(t, s.SetMap("test.mod#2"), "MM8 map: another map");
            t.Check(s.Model.Settings.Map.Ref == "test.mod#2" && MapList(s).Count == 0, "MM8 map: the array is empty");
            t.Equal(n + 1, s.Commands.UndoList.Count, "MM8 map: one entry");
            Kit.Ok(t, s.Commands.Undo(), "MM8 map: undo");
            t.Check(s.Model.Settings.Map.Ref == "test.mod#1" && Text(MapList(s)) == edited, "MM8 map: undo brings the ref and the array back");
            Kit.Ok(t, s.Commands.Redo(), "MM8 map: redo");
            t.Check(s.Model.Settings.Map.Ref == "test.mod#2" && MapList(s).Count == 0, "MM8 map: redo clears it again");
            Kit.Ok(t, s.Commands.Undo(), "MM8 map: undo again");

            // No map at all: the same.
            Kit.Ok(t, s.SetMap(null), "MM8 map: no map");
            t.Check(s.Model.Settings.Map.Ref == null && MapList(s).Count == 0, "MM8 map: null clears the array");
            Kit.Ok(t, s.Commands.Undo(), "MM8 map: undo");
            t.Equal(edited, Text(MapList(s)), "MM8 map: and undo restores it");

            // The same ref changes nothing of the array; a pose edit neither.
            Kit.NoOp(t, s.SetMap("test.mod#1"), "MM8 map: the same ref at its place is no change");
            t.Equal(edited, Text(MapList(s)), "MM8 map: the array stays on the same ref");
            Kit.Ok(t, s.Set(null, "map.pos", new Vec3(0f, 2f, 0f)), "MM8 map: a moved map");
            Kit.Ok(t, s.SetMap("test.mod#1"), "MM8 map: the same ref, its pose reset");
            t.Check(s.Model.Settings.Map.Pos.Equals(Vec3.Zero) && Text(MapList(s)) == edited, "MM8 map: the composite runs, and leaves the array");
            Kit.Ok(t, s.Set(null, "map.pos", new Vec3(1f, 0f, 0f)), "MM8 map: a pose edit");
            t.Equal(edited, Text(MapList(s)), "MM8 map: and on a pose edit");

            // With an empty array nothing is added to the composite: the entry is as before.
            var e = Kit.GoldenSession(t);
            var r1 = e.SetMap("test.mod#1");
            Kit.Ok(t, r1, "MM8 map: empty");
            t.Equal(3, ((CompositeCommand)r1.Command).Children.Count, "MM8 map: an empty array adds no step");
            var f = Kit.GoldenSession(t);
            Kit.Ok(t, Op(f, null, Map, MaterialEdits.Set(MapList(f), MaterialEdit.Float("m", "a", 1f))), "MM8 map: an edit");
            var r2 = f.SetMap("test.mod#1");
            Kit.Ok(t, r2, "MM8 map: a map after it");
            t.Equal(4, ((CompositeCommand)r2.Command).Children.Count, "MM8 map: one more step with an array");
        }

        // ---------------- MJ1 ----------------

        static BodyBoneMod B(string bone, string location = BodyBoneMod.LocationUnknown, float length = 1f, Vec3? scale = null) =>
            new BodyBoneMod { Bone = bone, Location = location, Length = length, Scale = scale ?? Vec3.One };

        static void MJ1(TestRunner t)
        {
            var a = B("a", BodyBoneMod.LocationUnknown, 2f);
            var hair = B("N_hair_Root", BodyBoneMod.LocationBodyTop, 1f, new Vec3(1.5f, 1.5f, 1.5f));
            var c = B("c", BodyBoneMod.LocationBodyTop, 3f);
            var list = new List<BodyBoneMod> { a, hair, c };
            string before = Text(list);
            var up = new Vec3(0f, 1f, 0f);

            t.Equal("appearance.chara.body.boneMods", BoneModEdits.Path, "MJ1: the path");
            t.Check(BoneModEdits.IsIdentity(new BodyBoneMod { Bone = "x" }), "MJ1: a new entry has no effect");
            t.Check(!BoneModEdits.IsIdentity(hair) && !BoneModEdits.IsIdentity(a), "MJ1: a value has");
            t.Check(!BoneModEdits.IsIdentity(new BodyBoneMod { Bone = "x", Position = up }) && !BoneModEdits.IsIdentity(new BodyBoneMod { Bone = "x", Rotation = up }), "MJ1: every member counts");

            // Replace in place.
            var r = BoneModEdits.Set(list, "N_hair_Root", null, 2f);
            t.Check(!ReferenceEquals(r, list) && r.Count == 3, "MJ1: a new list");
            t.Check(ReferenceEquals(r[0], a) && ReferenceEquals(r[2], c) && !ReferenceEquals(r[1], hair), "MJ1: the other entries are the same objects, in their places");
            t.Check(r[1].Length == 2f && r[1].Scale.X == 1.5f && r[1].Location == BodyBoneMod.LocationBodyTop, "MJ1: one member changed, the others kept");
            t.Equal(before, Text(list), "MJ1: the old list is untouched");
            var all4 = BoneModEdits.Set(list, "c", new Vec3(2f, 2f, 2f), 4f, up, new Vec3(0f, 0f, 370f));
            t.Check(all4[2].Scale.X == 2f && all4[2].Length == 4f && all4[2].Position.Equals(up) && all4[2].Rotation.Z == 370f, "MJ1: all four members");

            // Append, with the location bodyTop.
            var n = BoneModEdits.Set(list, "ct_hairB", null, null, up);
            t.Check(n.Count == 4 && n[3].Bone == "ct_hairB" && n[3].Location == BodyBoneMod.LocationBodyTop && n[3].Position.Equals(up) && n[3].Scale.Equals(Vec3.One) && n[3].Length == 1f, "MJ1: a new entry at the end, bodyTop");
            t.Check(ReferenceEquals(n[0], a) && ReferenceEquals(n[1], hair) && ReferenceEquals(n[2], c), "MJ1: the others not touched");

            // Identity removes.
            var onlyLen = new List<BodyBoneMod> { a, B("x", BodyBoneMod.LocationBodyTop, 2f) };
            var back = BoneModEdits.Set(onlyLen, "x", null, 1f);
            t.Check(back.Count == 1 && ReferenceEquals(back[0], a), "MJ1: back to no effect removes the entry");
            t.Check(ReferenceEquals(BoneModEdits.Set(list, "none", Vec3.One, 1f, Vec3.Zero, Vec3.Zero), list), "MJ1: no effect on no entry is no change");
            t.Check(ReferenceEquals(BoneModEdits.Set(list, "a", null, 2f), list), "MJ1: a value already stored is no change");
            t.Check(ReferenceEquals(BoneModEdits.Set(list, "a"), list), "MJ1: nothing named is no change");

            // A file's no-effect entry stays until edited; a repeated bone: the last is edited, a removal removes all.
            var noop = B("n");
            var kept = BoneModEdits.Set(new List<BodyBoneMod> { noop, a }, "a", null, 5f);
            t.Check(ReferenceEquals(kept[0], noop), "MJ1: an entry with no effect stays beside an edit");
            var first = B("d", BodyBoneMod.LocationUnknown, 2f);
            var last = B("d", BodyBoneMod.LocationBodyTop, 3f);
            var dup = new List<BodyBoneMod> { first, a, last };
            t.Equal(2, BoneModEdits.LastIndex(dup, "d"), "MJ1: the last entry of a bone");
            t.Equal(-1, BoneModEdits.LastIndex(dup, "z"), "MJ1: none");
            t.Equal(-1, BoneModEdits.LastIndex(null, "z"), "MJ1: of a null list");
            var e = BoneModEdits.Set(dup, "d", null, 9f);
            t.Check(ReferenceEquals(e[0], first) && e[2].Length == 9f && e[2].Location == BodyBoneMod.LocationBodyTop, "MJ1: an edit changes the last, keeps its location");
            var rm = BoneModEdits.Remove(dup, "d");
            t.Check(rm.Count == 1 && ReferenceEquals(rm[0], a), "MJ1: a removal takes all of the bone");
            t.Check(ReferenceEquals(BoneModEdits.Remove(dup, "z"), dup), "MJ1: nothing to remove: the same list");
            t.Check(BoneModEdits.Remove(null, "z") == null, "MJ1: of a null list");

            // Only the hair class.
            var h = BoneModEdits.RemoveWhere(list, b => b.StartsWith("N_hair", StringComparison.Ordinal));
            t.Check(h.Count == 2 && ReferenceEquals(h[0], a) && ReferenceEquals(h[1], c), "MJ1: a class of bones leaves, the others stay the same objects");
            t.Check(ReferenceEquals(BoneModEdits.RemoveWhere(list, b => false), list), "MJ1: an empty class: the same list");

            // Byte equality of the rest, numbers included: a value read from text comes back with the same text.
            var read = (BodyBoneMod)FieldJson.FromText("{\"bone\":\"t\",\"location\":\"unknown\",\"scale\":[0.1,0.2,0.3],\"length\":0.7,\"position\":[1.5,0.0,-2.25],\"rotation\":[10.0,20.0,30.0],\"future\":1}", typeof(BodyBoneMod));
            var withRead = new List<BodyBoneMod> { read, a };
            string readText = Compact(read);
            var w = BoneModEdits.Set(withRead, "a", null, 8f);
            t.Equal(readText, Compact(w[0]), "MJ1: another entry is byte-equal, its unknown member included");
            var own = BoneModEdits.Set(withRead, "t", null, 0.5f);
            t.Check(Compact(own[0]).Contains("\"future\":1") && own[0].Scale.X == 0.1f, "MJ1: the edited entry keeps what it does not name");

            // What cannot be stored.
            void Bad(Action f, string what)
            {
                var ex = Catch(f);
                t.Check(ex != null && ex.Code == CommandCodes.BadArgs, "MJ1: " + what + " is bad_args");
            }
            Bad(() => BoneModEdits.Set(list, "", null, 2f), "an empty name");
            Bad(() => BoneModEdits.Set(list, null, null, 2f), "no name");
            Bad(() => BoneModEdits.Set(list, "a", null, float.NaN), "a NaN length");
            Bad(() => BoneModEdits.Set(list, "a", new Vec3(float.NaN, 0f, 0f)), "a NaN scale");
            Bad(() => BoneModEdits.Set(list, "a", null, null, new Vec3(0f, float.PositiveInfinity, 0f)), "an infinite position");
            Bad(() => BoneModEdits.Set(list, "a", null, null, null, new Vec3(0f, 0f, float.NegativeInfinity)), "an infinite rotation");
            t.Equal(1, BoneModEdits.Set(null, "a", null, 2f).Count, "MJ1: a null list is empty");

            // On the bus, the array set as a whole; undo restores it.
            var s = Kit.GoldenSession(t, true);
            string B0() => FieldAccessor.GetJson(s.Model, Kit.GoldenChara, BoneModEdits.Path);
            var cur = (List<BodyBoneMod>)FieldAccessor.Get(s.Model, Kit.GoldenChara, BoneModEdits.Path);
            string start = B0();
            int entries = s.Commands.UndoList.Count;
            Kit.Ok(t, s.Set(Kit.GoldenChara, BoneModEdits.Path, BoneModEdits.Set(cur, "ct_hairF", null, 1.5f)), "MJ1: on the bus");
            t.Equal(entries + 1, s.Commands.UndoList.Count, "MJ1: one entry");
            t.Check(B0().Contains("ct_hairF") && B0().Contains("bodyTop"), "MJ1: stored");
            Kit.Ok(t, s.Commands.Undo(), "MJ1: undo");
            t.Equal(start, B0(), "MJ1: undo restores the array");
        }
    }
}

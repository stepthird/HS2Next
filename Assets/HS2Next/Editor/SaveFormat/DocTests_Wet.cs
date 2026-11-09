// The wet block of a character node (scene.json member `chara.wet`, format revision 5). WM1 the block: defaults, schema order, write -> read -> write
// byte-stable, empty = no member, limits; WF1 a scene without a block keeps its bytes and its revision, on the committed files of every revision; WF2 the writer: revision 5
// exactly when a block is not empty, clearing it gives the bytes before; WF3 the reader: unknown members kept at every level, every wrong type and every null of a known member
// fails; WF4 a missing picture, the excess over a limit and a value out of range are kept and reported; WF5 the empty migration 4 -> 5, revisions 1 to 4 load with
// no finding; WF6's round trip as far as it runs here (a revision-4 registry over this reader, the member moved to the unknown members as 1.3 holds it; the
// run on 1.3's own save layer is recorded); WF7 save -> load -> save byte-identical, a stopped and a running stain; WF9 a map is an 8-bit gray
// PNG that holds the distances it was made from; WF10 no local path and no name but a stain's in any entry. The fixture
// Tests/SaveFormat/fixtures/wet/scene_r5_wet.hs2nscene is what this writer writes for FixtureScene(). Synthetic ids, names and pictures only.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save.Tests
{
    public static class DocTests_Wet
    {
        public static string Id(int n) => n.ToString("x32");

        public static readonly string CharaA = Id(0xd1), CharaB = Id(0xd2), CharaC = Id(0xd3);

        static string Text(byte[] b) => new UTF8Encoding(false).GetString(b);
        static string Names(JToken o) => string.Join(",", ((JObject)o).Properties().Select(p => p.Name));

        // ---------------- pictures ----------------

        /// <summary>An 8-bit gray PNG of <paramref name="values"/> (row by row), its data in stored deflate blocks, so the bytes are the same on every runtime.</summary>
        public static byte[] GrayPng(int width, int height, byte[] values)
        {
            var raw = new byte[height * (width + 1)];
            for (int y = 0; y < height; y++) Array.Copy(values, y * width, raw, y * (width + 1) + 1, width);
            var z = new MemoryStream();
            z.WriteByte(0x78);
            z.WriteByte(0x01);
            for (int at = 0; ;)
            {
                int n = Math.Min(65535, raw.Length - at);
                z.WriteByte((byte)(at + n == raw.Length ? 1 : 0));
                z.WriteByte((byte)n);
                z.WriteByte((byte)(n >> 8));
                z.WriteByte((byte)~n);
                z.WriteByte((byte)(~n >> 8));
                z.Write(raw, at, n);
                at += n;
                if (at == raw.Length) break;
            }
            uint a = 1, b = 0;
            foreach (byte x in raw)
            {
                a = (a + x) % 65521;
                b = (b + a) % 65521;
            }
            uint adler = b << 16 | a;
            z.Write(new[] { (byte)(adler >> 24), (byte)(adler >> 16), (byte)(adler >> 8), (byte)adler }, 0, 4);
            var png = new MemoryStream();
            png.Write(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }, 0, 8);
            var head = new byte[13];
            Big(head, 0, (uint)width);
            Big(head, 4, (uint)height);
            head[8] = 8;
            Chunk(png, "IHDR", head);
            Chunk(png, "IDAT", z.ToArray());
            Chunk(png, "IEND", new byte[0]);
            return png.ToArray();
        }

        static void Big(byte[] b, int at, uint v)
        {
            b[at] = (byte)(v >> 24);
            b[at + 1] = (byte)(v >> 16);
            b[at + 2] = (byte)(v >> 8);
            b[at + 3] = (byte)v;
        }

        static void Chunk(Stream s, string type, byte[] data)
        {
            var body = new byte[4 + data.Length];
            Encoding.ASCII.GetBytes(type, 0, 4, body, 0);
            Array.Copy(data, 0, body, 4, data.Length);
            var n = new byte[4];
            Big(n, 0, (uint)data.Length);
            s.Write(n, 0, 4);
            s.Write(body, 0, body.Length);
            Big(n, 0, Crc32.Compute(body));
            s.Write(n, 0, 4);
        }

        static IEnumerable<KeyValuePair<string, byte[]>> Chunks(byte[] png)
        {
            for (int at = 8; at + 8 <= png.Length;)
            {
                int n = png[at] << 24 | png[at + 1] << 16 | png[at + 2] << 8 | png[at + 3];
                string type = Encoding.ASCII.GetString(png, at + 4, 4);
                var data = new byte[n];
                Array.Copy(png, at + 8, data, 0, n);
                yield return new KeyValuePair<string, byte[]>(type, data);
                at += 12 + n;
            }
        }

        // Rendering hints a PNG writer may add; everything else beside the image (text, time, palette, a profile) is refused.
        static readonly string[] AllowedChunks = { "IHDR", "IDAT", "IEND", "gAMA", "sRGB", "pHYs" };

        /// <summary>What makes <paramref name="png"/> something other than a map, or null: an 8-bit gray PNG, not interlaced, with nothing beside the image but
        /// a rendering hint. A colour, an alpha or a palette cannot pass, and neither can any text.</summary>
        public static string MapProblem(byte[] png)
        {
            if (png == null || png.Length < 33 || !PngInfo.HasSignature(png)) return "not a PNG";
            var chunks = Chunks(png).ToList();
            if (chunks.Count == 0 || chunks[0].Key != "IHDR" || chunks[0].Value.Length != 13) return "no IHDR first";
            var h = chunks[0].Value;
            if (h[8] != 8) return "bit depth " + h[8];
            if (h[9] != 0) return "colour type " + h[9];
            if (h[12] != 0) return "interlaced";
            foreach (var c in chunks)
                if (Array.IndexOf(AllowedChunks, c.Key) < 0) return "a chunk " + c.Key;
            if (chunks[chunks.Count - 1].Key != "IEND") return "no IEND last";
            return null;
        }

        /// <summary>The values of a map (an 8-bit gray PNG, any row filter), row by row.</summary>
        public static byte[] MapValues(byte[] png, out int width, out int height)
        {
            var chunks = Chunks(png).ToList();
            var h = chunks[0].Value;
            width = h[0] << 24 | h[1] << 16 | h[2] << 8 | h[3];
            height = h[4] << 24 | h[5] << 16 | h[6] << 8 | h[7];
            var z = new MemoryStream();
            foreach (var c in chunks) if (c.Key == "IDAT") z.Write(c.Value, 0, c.Value.Length);
            z.Position = 2;
            var raw = new MemoryStream();
            using (var d = new DeflateStream(z, CompressionMode.Decompress)) d.CopyTo(raw);
            var b = raw.ToArray();
            var o = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                int filter = b[y * (width + 1)];
                for (int x = 0; x < width; x++)
                {
                    int v = b[y * (width + 1) + 1 + x];
                    int left = x > 0 ? o[y * width + x - 1] : 0, up = y > 0 ? o[(y - 1) * width + x] : 0, corner = x > 0 && y > 0 ? o[(y - 1) * width + x - 1] : 0;
                    switch (filter)
                    {
                        case 1: v += left; break;
                        case 2: v += up; break;
                        case 3: v += (left + up) / 2; break;
                        case 4:
                            int p = left + up - corner, pa = Math.Abs(p - left), pb = Math.Abs(p - up), pc = Math.Abs(p - corner);
                            v += pa <= pb && pa <= pc ? left : pb <= pc ? up : corner;
                            break;
                    }
                    o[y * width + x] = (byte)v;
                }
            }
            return o;
        }

        /// <summary>The value a map holds for distance <paramref name="d"/>: 254 stands for <paramref name="reach"/>, 255 is unreached.</summary>
        public static byte MapValue(double d, double reach)
        {
            if (d > reach) return 255;
            return (byte)Math.Round(254.0 * Math.Max(0.0, d) / reach, MidpointRounding.AwayFromZero);
        }

        // The distance of texel (x, y) from a point, in units: what a picture of this fixture holds.
        static double Distance(int x, int y, int width, int height, double cx, double cy, double unitsPerSide)
        {
            double dx = (x + .5) / width - cx, dy = (y + .5) / height - cy;
            return Math.Sqrt(dx * dx + dy * dy) * unitsPerSide;
        }

        /// <summary>The picture of a stain around (cx, cy): every value by <see cref="MapValue"/>.</summary>
        public static byte[] Picture(int width, int height, double cx, double cy, double unitsPerSide, double reach)
        {
            var v = new byte[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++) v[y * width + x] = MapValue(Distance(x, y, width, height, cx, cy, unitsPerSide), reach);
            return GrayPng(width, height, v);
        }

        const float BloodReach = 2.895f, WaterReach = 2.5f;
        static byte[] PictureA() => Picture(64, 32, .25, .5, 4.0, BloodReach);
        static byte[] PictureB() => Picture(32, 64, .5, .75, 6.0, BloodReach);
        static byte[] PictureC() => Picture(48, 48, .5, .5, 3.0, WaterReach);

        // ---------------- a block with every member ----------------

        /// <summary>A kept blood stain on a bone with two maps, a running water stain from the default origin with one, the block's objects away from their
        /// defaults, a puddle of each. Its pictures are <see cref="PictureA"/>, B and C.</summary>
        public static CharaWet Block()
        {
            var blood = new WetStain { Id = "s1", Name = "blood", Kept = true, T = 3.25f, Seed = 1 };
            WetPreset.ApplyTo(blood, WetPreset.Blood);
            blood.Origin = new WetOriginValues { Mode = WetOriginValues.ModeBone, Bone = "cf_J_Spine03", Local = new Vec3(0f, 0.1f, 0.2f) };
            blood.Maps.Add(new WetMapEntry { Renderer = "clothes:1/o_bot_a", Slot = 0, Res = ResStore.NameOf(PictureA()), Reach = BloodReach, Mesh = "o_bot_a|1847|8f3a11c2" });
            blood.Maps.Add(new WetMapEntry { Renderer = "body/o_body_cf", Slot = 2, Res = ResStore.NameOf(PictureB()), Reach = BloodReach, Mesh = "o_body_cf|9120|01ab34ef" });
            var water = new WetStain { Id = "s2", Name = "water", T = 1.5f, Running = true, Loop = true, Hold = 3.5f, Seed = 7 };
            WetPreset.ApplyTo(water, WetPreset.Water);
            water.Origin = new WetOriginValues { Down = 0.25f, Out = 0.05f };
            water.Shape = new WetShapeValues { Radius = 1.8f, Spread = 3f, Up = 2f, Down = 0.5f, Front = 1.25f, Back = 1.5f, Side = 2.5f, Outer = 1f, Noise = 0.25f, Feather = 0.2f };
            water.Drips.Tail = 5f;
            water.Drips.Skin = true;
            water.Drips.On = false;
            water.Puddle.Edge = 0.08f;
            water.Puddle.Scale = 2f;
            water.Maps.Add(new WetMapEntry { Renderer = "hair:0/o_hair_b", Slot = 1, Res = ResStore.NameOf(PictureC()), Reach = WaterReach, Mesh = "o_hair_b|640|55aa0011" });
            var b = new CharaWet { Current = "s2" };
            b.Stains.Add(blood);
            b.Stains.Add(water);
            b.Targets.Hair = false;
            b.Film = new WetFilmValues { On = false, Gloss = 0.5f, Smooth = 0.75f, Opacity = 0.6f };
            b.Mirror = new WetMirrorValues { On = false, Strength = 0.5f, Scale = 0.25f };
            b.Floor = new WetFloorValues { Mode = WetFloorValues.ModeFeet, Offset = 0.25f, Height = 1.5f };
            b.Puddles.Add(new WetPuddleEntry { Stain = "s1", Center = new Vec3(1f, 0.5f, 2f), Area = 0.5f, Seed = 12.5f, Color = new Color4(0.1f, 0.01f, 0.02f, 0.9f) });
            b.Puddles.Add(new WetPuddleEntry { Stain = "s2", Center = new Vec3(-1f, 0.5f, 0f), Area = 0.125f, Seed = 3.25f });
            return b;
        }

        /// <summary>A small block without pictures: one dry stain.</summary>
        public static CharaWet Small()
        {
            var s = new WetStain { Id = "s1", Name = "water" };
            WetPreset.ApplyTo(s, WetPreset.Water);
            var b = new CharaWet { Current = "s1" };
            b.Stains.Add(s);
            return b;
        }

        static void AddPictures(ResStore res)
        {
            res.Add(PictureA());
            res.Add(PictureB());
            res.Add(PictureC());
        }

        // ---------------- the fixture ----------------

        public const string FixtureFile = "scene_r5_wet.hs2nscene";
        public const string FixtureTitle = "Wet fixture scene";
        public static readonly DateTime FixtureClock = new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>The fixture's path under a save-format fixtures root.</summary>
        public static string FixturePath(string fixturesRoot) => Path.Combine(fixturesRoot ?? "", "fixtures", "wet", FixtureFile);

        /// <summary>The options the fixture was written with: fixed clock, placeholder thumbnail, no ext handlers, app 1.3.1.</summary>
        public static SaveOptions FixtureOptions() => new SaveOptions
        {
            Clock = () => FixtureClock,
            Thumbnail = ThumbnailMode.Placeholder,
            Registry = new ExtRegistry(),
            AppVersion = "1.3.1",
        };

        /// <summary>Three synthetic characters (no card data): the first with an advanced-pose block and <see cref="Block"/>, the second with <see cref="Small"/>
        /// and nothing else that later revisions add, the third with no block; an item.</summary>
        public static SceneContainer FixtureScene()
        {
            var s = new SceneContainer { Title = FixtureTitle };
            s.Scene.Nodes.Add(new SceneNode(CharaA, new CharaNodeState { AdvancedPose = DocTests_AdvancedPose.Block(), Wet = Block() }) { Order = 0 });
            s.PlaceCharacter(CharaA, InfraFixtures.SmallChara(1));
            s.Scene.Nodes.Add(new SceneNode(CharaB, new CharaNodeState { Wet = Small() }) { Order = 1 });
            s.PlaceCharacter(CharaB, InfraFixtures.SmallChara(0));
            s.Scene.Nodes.Add(new SceneNode(CharaC, new CharaNodeState()) { Order = 2 });
            s.PlaceCharacter(CharaC, InfraFixtures.SmallChara(0));
            s.Scene.Nodes.Add(new SceneNode(Id(0xa2), new ItemNodeState { Ref = "test.mod#1" }) { Order = 3 });
            AddPictures(s.Res);
            return s;
        }

        /// <summary>Writes the fixture once (refuses an existing file); returns its size.</summary>
        public static long WriteFixture(string fixturesRoot)
        {
            string path = FixturePath(fixturesRoot);
            if (File.Exists(path)) throw new InvalidOperationException("the fixture is never regenerated: " + path + " exists");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var r = FixtureScene().Save(path, FixtureOptions());
            if (!r.Ok) throw new InvalidOperationException(r.Error.Message);
            return new FileInfo(path).Length;
        }

        static int FormatOf(byte[] file) => (int)Entry(file, "manifest.json")["formatVersion"];
        static JObject Entry(byte[] file, string name) => JObject.Parse(Text(ContainerReader.Read(file).Get(name).Data));
        static byte[] EntryBytes(byte[] file, string name) => ContainerReader.Read(file).Get(name)?.Data;
        static bool Same(byte[] a, byte[] b) => a != null && b != null && a.SequenceEqual(b);
        static SaveOptions Opts() => InfraFixtures.Opts();

        static LoadOptions LOpts(CoreMigrationRegistry reg = null)
        {
            var o = InfraFixtures.LOpts();
            if (reg != null) o.Migrations = reg;
            return o;
        }

        static CharaNodeState CharaOf(SceneContainer c, string id) => (CharaNodeState)c.Scene.Find(id).State;

        /// <summary>A registry of revision 4, as 1.3 has.</summary>
        static CoreMigrationRegistry Registry4() => new CoreMigrationRegistry(4, CoreMigrationRegistry.Empty(1), CoreMigrationRegistry.Empty(2), CoreMigrationRegistry.Empty(3));

        public static void Run(TestRunner t)
        {
            DocTests_T1Golden.FixturesDir = t.FixturesDir;
            WM1(t);
            WF1(t);
            WF2(t);
            WF3(t);
            WF4(t);
            WF5(t);
            WF6(t);
            WF7(t);
            WF9(t);
            WF10(t);
        }

        // ---------------- WM1 ----------------

        static void WM1(TestRunner t)
        {
            Defaults(t);
            var d = Block();
            var b1 = d.ToBytes();
            var f = new FindingList();
            var back = CharaWet.Parse(Text(b1), f);
            var b2 = back.ToBytes();
            t.Check(b1.SequenceEqual(b2), "WM1: write -> read -> write byte-stable\n    " + DocIo.FirstDiff(b1, b2));
            t.Check(f.Count == 0, "WM1: the written block reads back with no finding (" + f + ")");
            t.Check(back.Clone().ToBytes().SequenceEqual(b1) && d.Clone().ToBytes().SequenceEqual(b1), "WM1: Clone gives the same bytes");
            var c = back.Clone();
            c.Stains[0].Maps[0].Slot = 9;
            c.Film.Gloss = 0.1f;
            c.Puddles[0].Area = 9f;
            t.Check(back.ToBytes().SequenceEqual(b1), "WM1: a Clone shares nothing");

            var j = back.ToJObject();
            t.Equal("stains,current,targets,film,mirror,floor,puddles", Names(j), "WM1: block members in schema order");
            var s = j["stains"][0];
            t.Equal("id,name,preset,kept,origin,shape,look,drips,puddle,t,running,loop,hold,seed,maps", Names(s), "WM1: stain members in schema order");
            t.Equal("mode,bone,local,down,out", Names(s["origin"]), "WM1: origin members in schema order");
            t.Equal("radius,spread,up,down,front,back,side,outer,noise,feather", Names(s["shape"]), "WM1: shape members in schema order");
            t.Equal("saturate,tint,stain,amount,skin", Names(s["look"]), "WM1: look members in schema order");
            t.Equal("on,rate,tail,size,growth,skin,color,shine", Names(s["drips"]), "WM1: drip members in schema order");
            t.Equal("color,shine,edge,scale", Names(s["puddle"]), "WM1: puddle look members in schema order");
            t.Equal("renderer,slot,res,reach,mesh", Names(s["maps"][0]), "WM1: map members in schema order");
            t.Equal("skin,hair", Names(j["targets"]), "WM1: targets in schema order, a switch that is off not written");
            var wide = Block();
            wide.Targets.Accessories = wide.Targets.Shoes = wide.Targets.Gloves = true;
            var wideBytes = wide.ToBytes();
            var wideBack = CharaWet.Parse(Text(wideBytes), new FindingList());
            t.Equal("skin,hair,accessories,shoes,gloves", Names(wideBack.ToJObject()["targets"]), "WM1: the switches that are on, in schema order");
            t.Check(wideBack.Targets.Accessories && wideBack.Targets.Shoes && wideBack.Targets.Gloves && wideBack.ToBytes().SequenceEqual(wideBytes), "WM1: they read back, byte-stable");
            wide.Targets.Accessories = wide.Targets.Shoes = wide.Targets.Gloves = false;
            t.Check(wide.ToBytes().SequenceEqual(b1), "WM1: switched off again, the block's bytes are those of a block that never used them");
            t.Equal("on,gloss,smooth,opacity", Names(j["film"]), "WM1: film in schema order");
            t.Equal("on,strength,scale", Names(j["mirror"]), "WM1: mirror in schema order");
            t.Equal("mode,offset,height", Names(j["floor"]), "WM1: floor in schema order");
            t.Equal("stain,center,area,seed,color", Names(j["puddles"][0]), "WM1: puddle members in schema order");
            t.Check(j["puddles"][0]["color"].Type == JTokenType.Array && j["puddles"][1]["color"].Type == JTokenType.Null, "WM1: a kept puddle holds its colour, another writes null");
            t.Check(back.Puddles[0].Color.HasValue && !back.Puddles[1].Color.HasValue, "WM1: and both read back");

            // The exact canonical text of a small block.
            var small = new CharaWet { Current = "s1" };
            small.Stains.Add(new WetStain { Id = "s1", Name = "n", T = 1.5f, Seed = 3 });
            const string stainText =
                "    {\n      \"id\": \"s1\",\n      \"name\": \"n\",\n      \"preset\": \"\",\n      \"kept\": false,\n" +
                "      \"origin\": {\n        \"mode\": \"default\",\n        \"bone\": \"\",\n        \"local\": [0.0, 0.0, 0.0],\n        \"down\": 0.0,\n        \"out\": 0.0\n      },\n" +
                "      \"shape\": {\n        \"radius\": 2.2,\n        \"spread\": 6.04,\n        \"up\": 3.0,\n        \"down\": 0.75,\n        \"front\": 1.0,\n        \"back\": 1.3,\n        \"side\": 3.0,\n        \"outer\": 1.5,\n        \"noise\": 0.35,\n        \"feather\": 0.195\n      },\n" +
                "      \"look\": {\n        \"saturate\": 1.15,\n        \"tint\": [1.0, 1.0, 1.0],\n        \"stain\": [0.0, 0.0, 0.0],\n        \"amount\": 0.3,\n        \"skin\": 0.15\n      },\n" +
                "      \"drips\": {\n        \"on\": true,\n        \"rate\": 18.0,\n        \"tail\": 8.0,\n        \"size\": 0.031,\n        \"growth\": 1.0,\n        \"skin\": false,\n        \"color\": [0.85, 0.88, 0.75, 0.2],\n        \"shine\": 0.6\n      },\n" +
                "      \"puddle\": {\n        \"color\": [0.1, 0.097, 0.097, 0.8],\n        \"shine\": 1.0,\n        \"edge\": 0.04,\n        \"scale\": 1.5\n      },\n" +
                "      \"t\": 1.5,\n      \"running\": false,\n      \"loop\": false,\n      \"hold\": 2.0,\n      \"seed\": 3,\n      \"maps\": []\n    }\n";
            const string smallText =
                "{\n  \"stains\": [\n" + stainText + "  ],\n  \"current\": \"s1\",\n" +
                "  \"targets\": {\n    \"skin\": true,\n    \"hair\": true\n  },\n" +
                "  \"film\": {\n    \"on\": true,\n    \"gloss\": 0.9,\n    \"smooth\": 0.8,\n    \"opacity\": 0.85\n  },\n" +
                "  \"mirror\": {\n    \"on\": true,\n    \"strength\": 0.85,\n    \"scale\": 0.5\n  },\n" +
                "  \"floor\": {\n    \"mode\": \"map\",\n    \"offset\": 0.0,\n    \"height\": 0.0\n  },\n" +
                "  \"puddles\": []\n}";
            t.Equal(smallText, small.ToJson(), "WM1: the exact canonical text of a small block");

            // Absent members.
            var empty = CharaWet.Parse("{}");
            t.Check(empty.IsEmpty && empty.Stains.Count == 0 && empty.Puddles.Count == 0 && empty.Current == "", "WM1: {} is an empty block");
            var e1 = CharaWet.Parse("{\"stains\": [{\"id\": \"a\"}]}");
            t.Check(!e1.IsEmpty && e1.Stains[0].Shape.Radius == 2.2f && e1.Stains[0].Hold == 2f && e1.Stains[0].Maps.Count == 0 && e1.Stains[0].Origin.Mode == "default", "WM1: a stain's absent members are the defaults");
            t.Equal(StainJson(new WetStain { Id = "a" }), StainJson(e1.Stains[0]), "WM1: written whole");

            // What empty means.
            t.Check(new CharaWet().IsEmpty, "WM1: a new block is empty");
            var p = new CharaWet();
            p.Puddles.Add(new WetPuddleEntry { Stain = "a", Area = 1f });
            t.Check(!p.IsEmpty, "WM1: a puddle alone is not empty");
            t.Check(!CharaWet.Parse("{\"future\": 1}").IsEmpty, "WM1: an unknown member alone is not empty (never lost)");
            foreach (var m in new[] { "targets", "film", "mirror", "floor" })
                t.Check(!CharaWet.Parse("{\"" + m + "\": {\"future\": 1}}").IsEmpty, "WM1: nor one in " + m);
            t.Check(CharaWet.Parse("{\"film\": {\"gloss\": 0.1}, \"current\": \"x\", \"targets\": {\"hair\": false}, \"floor\": {\"mode\": \"feet\"}}").IsEmpty, "WM1: switches without a stain or a puddle are empty, and are not kept");

            // In the section: last, before the section's own unknown members.
            var node = new SceneNode(CharaA, new CharaNodeState { AdvancedPose = DocTests_AdvancedPose.Block(), Wet = Block() });
            var doc = new SceneDocument();
            doc.Nodes.Add(node);
            var sj = DocReader.Parse(DocIo.Bytes(doc), SceneDocument.EntryName);
            t.Equal("anime,hands,face,look,fk,ik,correction,state,voice,advancedPose,wet", Names(sj["nodes"][0]["chara"]), "WM1: wet is the last member of the section");
            var only = new SceneDocument();
            only.Nodes.Add(new SceneNode(CharaA, new CharaNodeState { Wet = Block() }));
            t.Equal("anime,hands,face,look,fk,ik,correction,state,voice,wet", Names(DocReader.Parse(DocIo.Bytes(only), SceneDocument.EntryName)["nodes"][0]["chara"]), "WM1: after voice without an advanced-pose block");
            var withUnknown = (JObject)sj.DeepClone();
            ((JObject)withUnknown["nodes"][0]["chara"]).Add("future", 1);
            var doc2 = DocIo.Scene(CanonicalJsonWriter.ToBytes(withUnknown, SceneDocument.EntryName));
            t.Equal(Block().ToJson(), ((CharaNodeState)doc2.Find(CharaA).State).Wet.ToJson(), "WM1: the section reads the block back");
            t.Equal("anime,hands,face,look,fk,ik,correction,state,voice,advancedPose,wet,future", Names(DocReader.Parse(DocIo.Bytes(doc2), SceneDocument.EntryName)["nodes"][0]["chara"]), "WM1: the section's unknown members follow it");

            // Lookups.
            t.Check(back.Find("s2")?.Name == "water" && back.Find("nope") == null && back.FindByName("blood")?.Id == "s1" && back.FindByName("nope") == null && back.CurrentStain?.Id == "s2", "WM1: Find, FindByName, CurrentStain");
            t.Check(new CharaWet().CurrentStain == null, "WM1: no current stain in a new block");
            t.Equal("s3", back.NextId(), "WM1: NextId after s1 and s2");
            back.Stains.RemoveAt(0);
            t.Equal("s1", back.NextId(), "WM1: NextId takes a free number");

            // Random blocks.
            int stable = 0;
            const int seeds = 300;
            for (int seed = 0; seed < seeds; seed++)
            {
                var r = RandomBlock(new Random(seed));
                var bb = r.ToBytes();
                var rf = new FindingList();
                var rb = CharaWet.Parse(Text(bb), rf);
                if (bb.SequenceEqual(rb.ToBytes()) && rf.Count == 0 && rb.Clone().ToBytes().SequenceEqual(bb)) stable++;
                else t.Info("seed " + seed + ": " + rf);
            }
            t.Equal(seeds, stable, "WM1: random blocks are byte-stable with no finding");
        }

        static void Defaults(TestRunner t)
        {
            var s = new WetStain();
            var h = s.Shape;
            t.Check(h.Radius == 2.2f && h.Spread == 6.04f && h.Up == 3f && h.Down == 0.75f && h.Front == 1f && h.Back == 1.3f && h.Side == 3f && h.Outer == 1.5f && h.Noise == 0.35f && h.Feather == 0.195f, "WM1: shape defaults");
            var l = s.Look;
            t.Check(l.Saturate == 1.15f && l.Tint.Equals(Vec3.One) && l.Stain.Equals(Vec3.Zero) && l.Amount == 0.3f && l.Skin == 0.15f, "WM1: look defaults");
            var d = s.Drips;
            t.Check(d.On && d.Rate == 18f && d.Tail == 8f && d.Size == 0.031f && d.Growth == 1f && !d.Skin && d.Color.Equals(new Color4(0.85f, 0.88f, 0.75f, 0.2f)) && d.Shine == 0.6f, "WM1: drip defaults");
            var p = s.Puddle;
            t.Check(p.Color.Equals(new Color4(0.1f, 0.097f, 0.097f, 0.8f)) && p.Shine == 1f && p.Edge == 0.04f && p.Scale == 1.5f, "WM1: puddle defaults");
            t.Check(!s.Loop && s.Hold == 2f && !s.Running && !s.Kept && s.T == 0f && s.Maps.Count == 0 && s.Origin.Mode == WetOriginValues.ModeDefault && s.Origin.IsActive, "WM1: run defaults");
            var b = new CharaWet();
            t.Check(b.Targets.Skin && b.Targets.Hair, "WM1: targets default on");
            t.Check(!b.Targets.Accessories && !b.Targets.Shoes && !b.Targets.Gloves, "WM1: accessories, shoes and gloves default off");
            t.Check(b.Film.On && b.Film.Gloss == 0.9f && b.Film.Smooth == 0.8f && b.Film.Opacity == 0.85f, "WM1: film defaults");
            t.Check(b.Mirror.On && b.Mirror.Strength == 0.85f && b.Mirror.Scale == 0.5f, "WM1: mirror defaults");
            t.Check(b.Floor.Mode == WetFloorValues.ModeMap && b.Floor.Offset == 0f && b.Floor.Height == 0f && b.Floor.AppliedMode == WetFloorValues.ModeMap, "WM1: floor defaults");
            t.Check(WetLimits.MaxStains == 8 && WetLimits.MaxMaps == 64 && WetLimits.MaxPuddles == 30 && WetLimits.MaxName == 64, "WM1: the limits");
            t.Equal("map,feet,fixed", string.Join(",", WetFloorValues.Modes), "WM1: the floor modes");

            // The ranges as numbers: WF4 reads its ends from WetRanges, so a wrong number there would pass it.
            void Pin(WetRange r, float min, float max, string what) => t.Check(r.Min == min && r.Max == max, "WM1: the range of " + what + " (" + r + ")");
            Pin(WetRanges.Unit, 0f, 1f, "a unit value");
            Pin(WetRanges.Radius, 0.1f, 10f, "radius");
            Pin(WetRanges.Spread, 0f, 120f, "spread");
            Pin(WetRanges.Up, 0.5f, 6f, "up");
            Pin(WetRanges.Down, 0.1f, 2f, "down");
            Pin(WetRanges.Axis, 0.3f, 3f, "front, back, side");
            Pin(WetRanges.Outer, 0f, 5f, "outer");
            Pin(WetRanges.Noise, 0f, 2f, "noise");
            Pin(WetRanges.Feather, 0.02f, 1.5f, "feather");
            Pin(WetRanges.Saturate, 0.5f, 2f, "saturate");
            Pin(WetRanges.Tint, 0f, 2f, "tint");
            Pin(WetRanges.DripRate, 0f, 80f, "the drip rate");
            Pin(WetRanges.DripTail, 0f, 30f, "the drip tail");
            Pin(WetRanges.DripSize, 0.02f, 0.2f, "the drop size");
            Pin(WetRanges.DripGrowth, 0f, 5f, "the growth");
            Pin(WetRanges.PuddleEdge, 0.005f, 0.5f, "the puddle's edge");
            Pin(WetRanges.PuddleScale, 0.3f, 4f, "the puddle's scale");
            Pin(WetRanges.MirrorScale, 0.1f, 1f, "the mirror's scale");
            Pin(WetRanges.FloorOffset, -2f, 2f, "the floor's offset");
            Pin(WetRanges.Hold, 0f, 20f, "the hold");
            t.Check(WetRanges.NotNegative.Min == 0f && WetRanges.NotNegative.Contains(1e30f) && !WetRanges.NotNegative.Contains(-0.001f), "WM1: not below 0");
            t.Check(new CharaNodeState().Wet != null && new CharaNodeState().Wet.IsEmpty, "WM1: a node's block is never null, empty by default");
        }

        // Valid values only, so no finding.
        static CharaWet RandomBlock(Random g)
        {
            float F(float min, float max) => (float)(min + g.NextDouble() * (max - min));
            float R(WetRange r) => F(r.Min, r.Max);
            Vec3 V(WetRange r) => new Vec3(R(r), R(r), R(r));
            Color4 C() => new Color4(F(0f, 1f), F(0f, 1f), F(0f, 1f), F(0f, 1f));
            string Name() => "n" + g.Next(50) + (g.Next(4) == 0 ? "é'\"" : "");
            bool B() => g.Next(2) == 0;
            var b = new CharaWet();
            for (int i = g.Next(WetLimits.MaxStains + 1); i > 0; i--)
            {
                var s = new WetStain { Id = b.NextId(), Name = Name(), Preset = B() ? WetPreset.Water : "", Kept = B(), T = F(0f, 500f), Running = B(), Loop = B(), Hold = R(WetRanges.Hold), Seed = g.Next() };
                s.Origin = new WetOriginValues { Mode = B() ? WetOriginValues.ModeBone : WetOriginValues.ModeDefault, Bone = Name(), Local = new Vec3(F(-5f, 5f), F(-5f, 5f), F(-5f, 5f)), Down = F(-1f, 1f), Out = F(-1f, 1f) };
                s.Shape = new WetShapeValues
                {
                    Radius = R(WetRanges.Radius), Spread = R(WetRanges.Spread), Up = R(WetRanges.Up), Down = R(WetRanges.Down), Front = R(WetRanges.Axis), Back = R(WetRanges.Axis),
                    Side = R(WetRanges.Axis), Outer = R(WetRanges.Outer), Noise = R(WetRanges.Noise), Feather = R(WetRanges.Feather),
                };
                s.Look = new WetLookValues { Saturate = R(WetRanges.Saturate), Tint = V(WetRanges.Tint), Stain = V(WetRanges.Unit), Amount = F(0f, 1f), Skin = F(0f, 1f) };
                s.Drips = new WetDripValues { On = B(), Rate = R(WetRanges.DripRate), Tail = R(WetRanges.DripTail), Size = R(WetRanges.DripSize), Growth = R(WetRanges.DripGrowth), Skin = B(), Color = C(), Shine = F(0f, 1f) };
                s.Puddle = new WetPuddleLook { Color = C(), Shine = F(0f, 1f), Edge = R(WetRanges.PuddleEdge), Scale = R(WetRanges.PuddleScale) };
                for (int n = g.Next(4); n > 0; n--)
                    s.Maps.Add(new WetMapEntry { Renderer = "clothes:" + g.Next(8) + "/" + Name(), Slot = g.Next(4), Res = ResStore.NameOf(new byte[] { (byte)g.Next(256) }), Reach = F(0f, 12f), Mesh = Name() + "|" + g.Next(9999) + "|" + g.Next().ToString("x8") });
                b.Stains.Add(s);
            }
            b.Current = b.Stains.Count > 0 ? b.Stains[g.Next(b.Stains.Count)].Id : "";
            b.Targets = new WetTargetSwitches { Skin = B(), Hair = B(), Accessories = B(), Shoes = B(), Gloves = B() };
            b.Film = new WetFilmValues { On = B(), Gloss = F(0f, 1f), Smooth = F(0f, 1f), Opacity = F(0f, 1f) };
            b.Mirror = new WetMirrorValues { On = B(), Strength = F(0f, 1f), Scale = R(WetRanges.MirrorScale) };
            b.Floor = new WetFloorValues { Mode = WetFloorValues.Modes[g.Next(3)], Offset = R(WetRanges.FloorOffset), Height = F(-50f, 50f) };
            for (int i = g.Next(WetLimits.MaxPuddles + 1); i > 0; i--)
                b.Puddles.Add(new WetPuddleEntry { Stain = b.NextId(), Center = new Vec3(F(-50f, 50f), F(-5f, 5f), F(-50f, 50f)), Area = F(0f, 9f), Seed = F(0f, 100f), Color = B() ? C() : (Color4?)null });
            return b;
        }

        // ---------------- WF1 ----------------

        /// <summary>The committed files of every revision without a block, with the options each was written with (null: only its entries are compared).</summary>
        static IEnumerable<(string what, string path, SaveOptions options, int revision)> WetFreeScenes(string root)
        {
            yield return ("the Timeline fixture", DocTests_Timeline.R4Path(root), DocTests_Timeline.R4Options(), 4);
            yield return ("the advanced-pose fixture", DocTests_AdvancedPose.PosePath(root), DocTests_AdvancedPose.PoseOptions(), 4);
            yield return ("the material-edit fixture", DocTests_MaterialEdit.MaterialsPath(root), DocTests_MaterialEdit.MaterialsOptions(), 4);
            yield return ("the constraints fixture", DocTests_Constraints.FixturePath(root), DocTests_Constraints.FixtureOptions(), 4);
            yield return ("the v0.2 scene", Path.Combine(FixtureWriter.V02Dir(root), "scene.hs2nscene"), null, 1);
            yield return ("the plugin sample's scene", Path.Combine(root ?? "", "..", "Plugins", "fixtures", "sequence_scene.hs2nscene"), null, 1);
        }

        static void WF1(TestRunner t)
        {
            // Every entry of a real file: loaded and written back, it is what the file holds.
            foreach (var (what, path, options, revision) in WetFreeScenes(t.FixturesDir))
            {
                if (!File.Exists(path)) { t.Check(false, "WF1: " + what + " present at " + path); continue; }
                var file = File.ReadAllBytes(path);
                var l = SceneContainer.Load(file, LOpts());
                t.Check(l.Ok && !l.NewerFormat && !l.Findings.Has(WetFindings.Limit) && !l.Findings.Has(WetFindings.MapMissing), "WF1: " + what + " loads (" + l + ")");
                if (!l.Ok) continue;
                t.Check(l.Value.Scene.Nodes.Select(n => n.State).OfType<CharaNodeState>().All(c => c.Wet != null && c.Wet.IsEmpty), "WF1: " + what + ": its characters read with an empty block, never null");
                t.Check(!Text(EntryBytes(file, "scene.json")).Contains("\"wet\": {"), "WF1: " + what + ": the file has no member");
                var re = l.Value.ToBytes(options ?? new SaveOptions { Clock = () => InfraFixtures.FixedTime, Thumbnail = ThumbnailMode.Keep, Registry = new ExtRegistry(), AppVersion = (string)Entry(file, "manifest.json")["app"]["version"] });
                t.Equal(revision, FormatOf(re), "WF1: " + what + " stays revision " + revision);
                t.Equal((int)Entry(file, "manifest.json")["minReaderVersion"], (int)Entry(re, "manifest.json")["minReaderVersion"], "WF1: " + what + ": minReaderVersion as it was");
                var names = ContainerReader.Read(file).Entries.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
                t.Equal(string.Join(",", names), string.Join(",", ContainerReader.Read(re).Entries.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal)), "WF1: " + what + ": the same entries");
                foreach (var name in names)
                {
                    if (name == "manifest.json") continue;
                    t.Check(Same(EntryBytes(file, name), EntryBytes(re, name)), "WF1: " + what + ": " + name + " byte-equal\n    " + DocIo.FirstDiff(EntryBytes(file, name) ?? new byte[0], EntryBytes(re, name) ?? new byte[0]));
                }
                if (options != null) t.Check(Same(file, re), "WF1: " + what + ": the whole file byte-equal");
            }

            // The frozen golden document, and a character, a coordinate and a pose file: not a scene, no member anywhere.
            var golden = DocTests_T1Golden.Golden("scene.json");
            var doc = DocIo.Scene(golden);
            t.Check(doc.Nodes.Select(n => n.State).OfType<CharaNodeState>().Any() && DocIo.Same(golden, DocIo.Bytes(doc)), "WF1: the golden scene is written byte for byte");
            foreach (var name in new[] { "chara.hs2nchara", "coord.hs2ncoord", "pose.hs2npose" })
            {
                string path = Path.Combine(FixtureWriter.V02Dir(t.FixturesDir ?? ""), name);
                if (!File.Exists(path)) { t.Check(false, "WF1: fixture " + path + " present"); continue; }
                foreach (var e in ContainerReader.Read(File.ReadAllBytes(path)).Entries)
                    if (e.Name.EndsWith(".json", StringComparison.Ordinal)) t.Check(!Text(e.Data).Contains("\"wet\": {"), "WF1: " + name + ": " + e.Name + " holds no block");
            }

            // Written by this writer: a character never given a block, an explicit empty one, one cleared.
            var s = InfraFixtures.SmallScene(2, 1);
            var b0 = s.ToBytes(Opts());
            t.Check(FormatOf(b0) == 1 && !Text(EntryBytes(b0, "scene.json")).Contains("\"wet\": {"), "WF1: a scene without a block has no member, format 1");
            var c0 = (CharaNodeState)s.Scene.Nodes[0].State;
            c0.Wet = new CharaWet();
            t.Check(Same(b0, s.ToBytes(Opts())), "WF1: an explicit empty block changes no byte");
            c0.Wet = new CharaWet { Current = "x", Film = new WetFilmValues { Gloss = 0.1f } };
            t.Check(Same(b0, s.ToBytes(Opts())), "WF1: nor one with switches alone");

            // A hand-made empty member reads silently and is not written.
            var hand = InfraFixtures.Repack(b0, m => InfraFixtures.EditJson(m, "scene.json", j => ((JObject)j["nodes"][0]["chara"]).Add("wet", new JObject())), "scene.json");
            var lh = SceneContainer.Load(hand, LOpts());
            t.Check(lh.Ok && lh.Findings.Count == 0 && Same(EntryBytes(b0, "scene.json"), EntryBytes(lh.Value.ToBytes(Opts()), "scene.json")), "WF1: an empty member from a file reads silently and is not written");
        }

        // ---------------- WF2 ----------------

        static void WF2(TestRunner t)
        {
            var s = InfraFixtures.SmallScene(2, 1);
            var c0 = (CharaNodeState)s.Scene.Nodes[0].State;
            var b1 = s.ToBytes(Opts());
            t.Check(FormatOf(b1) == 1, "WF2: no block: 1");

            int Revision(Action<CharaWet> fill)
            {
                c0.Wet = new CharaWet();
                fill(c0.Wet);
                int r = FormatOf(s.ToBytes(Opts()));
                c0.Wet = new CharaWet();
                return r;
            }
            t.Equal(5, Revision(w => w.Stains.Add(new WetStain { Id = "s1" })), "WF2: a dry stain alone: 5");
            t.Equal(5, Revision(w => w.Puddles.Add(new WetPuddleEntry { Stain = "s1" })), "WF2: a puddle alone: 5");
            t.Equal(5, Revision(w => { var b = CharaWet.Parse("{\"future\": 1}"); w.Unknown = b.Unknown; }), "WF2: an unknown member alone: 5");
            t.Equal(5, Revision(w => { var b = CharaWet.Parse("{\"mirror\": {\"future\": 1}}"); w.Mirror = b.Mirror; }), "WF2: an unknown member in mirror: 5");
            t.Equal(1, Revision(w => { }), "WF2: an empty block: 1");

            AddPictures(s.Res);
            c0.Wet = Block();
            var withBlock = s.ToBytes(Opts());
            var man = Entry(withBlock, "manifest.json");
            t.Check((int)man["formatVersion"] == 5 && (int)man["minReaderVersion"] == 1, "WF2: the block's file says revision 5, minReaderVersion 1");
            t.Check(man["requires"].Count() == Entry(b1, "manifest.json")["requires"].Count(), "WF2: nothing new in requires");
            foreach (var p in new[] { PictureA(), PictureB(), PictureC() })
                t.Check(Same(p, EntryBytes(withBlock, ResStore.NameOf(p))), "WF2: picture " + ResStore.NameOf(p) + " is in the file, byte-equal");
            t.Equal(ContainerReader.Read(b1).Entries.Count() + 3, ContainerReader.Read(withBlock).Entries.Count(), "WF2: the three pictures are the entries the block adds");

            // A picture no map names is not written; one two maps name is written once.
            var two = Block();
            two.Stains[1].Maps[0].Res = two.Stains[0].Maps[0].Res;
            c0.Wet = two;
            var shared = s.ToBytes(Opts());
            t.Equal(ContainerReader.Read(b1).Entries.Count() + 2, ContainerReader.Read(shared).Entries.Count(), "WF2: a shared picture is stored once, an unnamed one not at all");

            // Clearing brings the bytes back, the pictures still in the store.
            c0.Wet = new CharaWet();
            t.Check(s.Res.Contains(ResStore.NameOf(PictureA())) && Same(s.ToBytes(Opts()), b1), "WF2: cleared, the file is byte-equal to one that never had a block");

            // With the other optional members: the highest wins; clearing brings each back.
            c0.Wet = Block();
            var other = (CharaNodeState)s.Scene.Nodes[1].State;
            other.AdvancedPose = DocTests_AdvancedPose.Block();
            t.Check(FormatOf(s.ToBytes(Opts())) == 5, "WF2: with a revision-4 member too: 5");
            c0.Wet = new CharaWet();
            t.Check(FormatOf(s.ToBytes(Opts())) == 4, "WF2: the block cleared: back to 4");
            other.AdvancedPose = new CharaAdvancedPose();
            other.State.NippleThroughClothes = false;
            c0.Wet = Block();
            t.Check(FormatOf(s.ToBytes(Opts())) == 5, "WF2: with a revision-3 member: 5");
            c0.Wet = new CharaWet();
            t.Check(FormatOf(s.ToBytes(Opts())) == 3, "WF2: the block cleared: back to 3");
            other.State.NippleThroughClothes = true;
            t.Check(Same(s.ToBytes(Opts()), b1), "WF2: all cleared: 1, byte-equal to the file before");

            // A second character's block raises the file as well.
            ((CharaNodeState)s.Scene.Nodes[1].State).Wet = Small();
            t.Check(FormatOf(s.ToBytes(Opts())) == 5, "WF2: any character's block raises the file");
            ((CharaNodeState)s.Scene.Nodes[1].State).Wet = new CharaWet();

            // No other file carries it.
            c0.Wet = Block();
            foreach (var e in ContainerReader.Read(s.ToBytes(Opts())).Entries)
                if (e.Name != "scene.json" && e.Name.EndsWith(".json", StringComparison.Ordinal))
                    t.Check(!Text(e.Data).Contains("\"wet\": {"), "WF2: " + e.Name + " does not hold the block");
            c0.Wet = new CharaWet();

            // A writer guard: what the reader would refuse is not written.
            void Refused(Action<CharaWet> spoil, string what)
            {
                var b = Block();
                spoil(b);
                t.ThrowsCode(SaveErrorCode.BadDocument, () => b.ToBytes(), "WF2: the writer refuses " + what);
            }
            Refused(b => b.Stains[0].Id = null, "a null id");
            Refused(b => b.Stains[0].Name = null, "a null name");
            Refused(b => b.Current = null, "a null current");
            Refused(b => b.Stains[0].Maps[0].Renderer = null, "a null renderer");
            Refused(b => b.Stains[0].Maps[0].Res = "res/nope.png", "a res that is no res name");
            Refused(b => b.Stains[0].Maps[0].Res = null, "a null res");
            Refused(b => b.Stains.Add(null), "a null stain");
            Refused(b => b.Puddles[0].Stain = null, "a null stain id on a puddle");
            var nan = Block();
            nan.Stains[0].T = float.NaN;
            t.ThrowsCode(SaveErrorCode.NonFinite, () => nan.ToBytes(), "WF2: the writer refuses a clock that is not finite");
        }

        // ---------------- WF3 ----------------

        static IEnumerable<JObject> Objects(JToken t)
        {
            if (t is JObject o)
            {
                yield return o;
                foreach (var p in o.Properties()) foreach (var x in Objects(p.Value)) yield return x;
            }
            else if (t is JArray a) foreach (var e in a) foreach (var x in Objects(e)) yield return x;
        }

        static void WF3(TestRunner t)
        {
            var whole = Block().ToJObject();

            // Unknown members at every level: every object of a whole block gains one; it comes back at its place, after the known members, and nothing is reported.
            int levels = 0, kept = 0;
            for (int i = 0; ; i++)
            {
                var j = (JObject)whole.DeepClone();
                var objects = Objects(j).ToList();
                if (i >= objects.Count) break;
                levels++;
                objects[i].Add("zzFuture", JObject.Parse("{\"a\": [1, 2], \"b\": null}"));
                var f = new FindingList();
                var d = CharaWet.FromToken(j, f);
                if (f.Count == 0 && CanonicalJsonWriter.ToText(j) == d.ToJson() && CharaWet.Parse(d.ToJson()).ToJson() == d.ToJson()) kept++;
                else t.Info("unknown member at " + objects[i].Path + " " + f);
            }
            t.Check(levels == 22, "WF3: the block has the objects expected (" + levels + ")");
            t.Equal(levels, kept, "WF3: an unknown member is kept at every level, in place, with no finding");

            // A JSON null of every known member fails, but for a puddle's colour.
            int nulls = 0;
            var paths = Objects(whole).SelectMany(o => o.Properties()).Select(p => p.Path).ToList();
            foreach (var path in paths)
            {
                var j = (JObject)whole.DeepClone();
                j.SelectToken(path).Replace(JValue.CreateNull());
                bool hasNullForm = System.Text.RegularExpressions.Regex.IsMatch(path, @"^puddles\[\d+\]\.color$");
                if (hasNullForm) { t.Check(CharaWet.FromToken(j).Puddles.Count == 2, "WF3: a puddle's colour may be null (" + path + ")"); continue; }
                t.ThrowsCode(SaveErrorCode.BadDocument, () => CharaWet.FromToken(j), "WF3: a null fails: " + path);
                nulls++;
            }
            t.Check(nulls > 80, "WF3: every known member was tried (" + nulls + ")");

            // A wrong type of every known member fails.
            int wrong = 0;
            foreach (var path in paths)
            {
                var v = whole.SelectToken(path);
                JToken bad;
                switch (v.Type)
                {
                    case JTokenType.Object: bad = new JArray(); break;
                    case JTokenType.Array: bad = new JObject(); break;
                    case JTokenType.String: bad = 5; break;
                    case JTokenType.Boolean: bad = 1; break;
                    case JTokenType.Null: bad = true; break;
                    default: bad = "x"; break;
                }
                var j = (JObject)whole.DeepClone();
                j.SelectToken(path).Replace(bad);
                t.ThrowsCode(SaveErrorCode.BadDocument, () => CharaWet.FromToken(j), "WF3: a wrong type fails: " + path);
                wrong++;
            }
            t.Equal(paths.Count, wrong, "WF3: every known member, a wrong type");

            // Arrays of a fixed length, their elements, integers.
            void Bad(string json, string what) => t.ThrowsCode(SaveErrorCode.BadDocument, () => CharaWet.Parse(json), "WF3: fails: " + what);
            Bad("[]", "the block is an array");
            Bad("{\"stains\": [1]}", "a stain is a number");
            Bad("{\"stains\": [null]}", "a stain is null");
            Bad("{\"stains\": [{\"maps\": [null]}]}", "a map is null");
            Bad("{\"stains\": [{\"look\": {\"tint\": [1, 2]}}]}", "tint has two numbers");
            Bad("{\"stains\": [{\"look\": {\"stain\": [1, 2, 3, 4]}}]}", "stain has four numbers");
            Bad("{\"stains\": [{\"look\": {\"tint\": [1, \"x\", 3]}}]}", "an element is a string");
            Bad("{\"stains\": [{\"drips\": {\"color\": [1, 2, 3]}}]}", "a drip colour has three numbers");
            Bad("{\"stains\": [{\"seed\": 1.5}]}", "seed is not an integer");
            Bad("{\"stains\": [{\"origin\": {\"local\": [0, 0]}}]}", "local has two numbers");
            Bad("{\"puddles\": [{\"center\": [0, 0]}]}", "center has two numbers");
            Bad("{\"puddles\": [{\"color\": [0, 0, 0]}]}", "a puddle's colour has three numbers");
            Bad("{\"stains\": [{\"id\": \"a\"}, {\"id\": \"b\", \"id\": \"c\"}]}", "a repeated key");
            t.ThrowsCode(SaveErrorCode.NonFinite, () => CharaWet.Parse("{\"stains\": [{\"t\": 1e300}]}"), "WF3: a number outside float32 is NonFinite");

            // A map needs what makes it one.
            const string res = "\"res\": \"res/0000000000000000000000000000000000000000000000000000000000000000.png\"";
            string Map(string members) => "{\"stains\": [{\"maps\": [{" + members + "}]}]}";
            CharaWet.Parse(Map("\"renderer\": \"body/b\", \"slot\": 0, " + res + ", \"reach\": 1, \"mesh\": \"m\""));
            t.ThrowsCode(SaveErrorCode.BadDocument, () => CharaWet.Parse(Map("\"slot\": 0, " + res + ", \"reach\": 1, \"mesh\": \"m\"")), "WF3: a map without renderer fails");
            t.ThrowsCode(SaveErrorCode.BadDocument, () => CharaWet.Parse(Map("\"renderer\": \"body/b\", " + res + ", \"reach\": 1, \"mesh\": \"m\"")), "WF3: a map without slot fails");
            t.ThrowsCode(SaveErrorCode.BadDocument, () => CharaWet.Parse(Map("\"renderer\": \"body/b\", \"slot\": 0, \"reach\": 1, \"mesh\": \"m\"")), "WF3: a map without res fails");
            t.ThrowsCode(SaveErrorCode.BadDocument, () => CharaWet.Parse(Map("\"renderer\": \"body/b\", \"slot\": 0, " + res + ", \"mesh\": \"m\"")), "WF3: a map without reach fails");
            t.ThrowsCode(SaveErrorCode.BadDocument, () => CharaWet.Parse(Map("\"renderer\": \"body/b\", \"slot\": 0, " + res + ", \"reach\": 1")), "WF3: a map without mesh fails");
            t.ThrowsCode(SaveErrorCode.BadDocument, () => CharaWet.Parse(Map("\"renderer\": \"body/b\", \"slot\": 0, \"res\": \"res/x.png\", \"reach\": 1, \"mesh\": \"m\"")), "WF3: a res that is no res name fails");

            // Lenient: a type error is a finding and the absent value.
            var lf = new FindingList();
            var ld = CharaWet.Parse("{\"stains\": [{\"id\": \"a\", \"t\": \"x\", \"kept\": null, \"hold\": 3}]}", lf, "t", true);
            t.Check(lf.CountOf("json.typeError") == 2 && ld.Stains[0].T == 0f && !ld.Stains[0].Kept && ld.Stains[0].Hold == 3f, "WF3: a lenient reader reports and uses the absent value (" + lf + ")");

            // In the section: the same, through a node.
            var node = DocReader.Parse(DocIo.Bytes(OneNode()), SceneDocument.EntryName);
            ((JObject)node["nodes"][0]["chara"]).Add("wet", 5);
            t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Scene(CanonicalJsonWriter.ToBytes(node, SceneDocument.EntryName)), "WF3: the member as a number fails the document");
            node["nodes"][0]["chara"]["wet"] = JValue.CreateNull();
            t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Scene(CanonicalJsonWriter.ToBytes(node, SceneDocument.EntryName)), "WF3: the member as null fails the document");
            t.ThrowsCode(SaveErrorCode.BadDocument, () => CharaWet.FromToken(null), "WF3: no token fails");
        }

        static SceneDocument OneNode()
        {
            var d = new SceneDocument();
            d.Nodes.Add(new SceneNode(CharaA, new CharaNodeState()));
            return d;
        }

        // ---------------- WF4 ----------------

        static void WF4(TestRunner t)
        {
            Ranges(t);
            Limits(t);
            Kept(t);
            MissingPictures(t);
        }

        static void Ranges(TestRunner t)
        {
            float ST(CharaWet b, Func<WetStain, float> f) => f(b.Stains[0]);
            var rows = new (string path, WetRange range, Func<CharaWet, float> applied)[]
            {
                ("stains[0].shape.radius", WetRanges.Radius, b => ST(b, s => s.Shape.Applied().Radius)),
                ("stains[0].shape.spread", WetRanges.Spread, b => ST(b, s => s.Shape.Applied().Spread)),
                ("stains[0].shape.up", WetRanges.Up, b => ST(b, s => s.Shape.Applied().Up)),
                ("stains[0].shape.down", WetRanges.Down, b => ST(b, s => s.Shape.Applied().Down)),
                ("stains[0].shape.front", WetRanges.Axis, b => ST(b, s => s.Shape.Applied().Front)),
                ("stains[0].shape.back", WetRanges.Axis, b => ST(b, s => s.Shape.Applied().Back)),
                ("stains[0].shape.side", WetRanges.Axis, b => ST(b, s => s.Shape.Applied().Side)),
                ("stains[0].shape.outer", WetRanges.Outer, b => ST(b, s => s.Shape.Applied().Outer)),
                ("stains[0].shape.noise", WetRanges.Noise, b => ST(b, s => s.Shape.Applied().Noise)),
                ("stains[0].shape.feather", WetRanges.Feather, b => ST(b, s => s.Shape.Applied().Feather)),
                ("stains[0].look.saturate", WetRanges.Saturate, b => ST(b, s => s.Look.Applied().Saturate)),
                ("stains[0].look.tint[1]", WetRanges.Tint, b => ST(b, s => s.Look.Applied().Tint.Y)),
                ("stains[0].look.stain[2]", WetRanges.Unit, b => ST(b, s => s.Look.Applied().Stain.Z)),
                ("stains[0].look.amount", WetRanges.Unit, b => ST(b, s => s.Look.Applied().Amount)),
                ("stains[0].look.skin", WetRanges.Unit, b => ST(b, s => s.Look.Applied().Skin)),
                ("stains[0].drips.rate", WetRanges.DripRate, b => ST(b, s => s.Drips.Applied().Rate)),
                ("stains[0].drips.tail", WetRanges.DripTail, b => ST(b, s => s.Drips.Applied().Tail)),
                ("stains[0].drips.size", WetRanges.DripSize, b => ST(b, s => s.Drips.Applied().Size)),
                ("stains[0].drips.growth", WetRanges.DripGrowth, b => ST(b, s => s.Drips.Applied().Growth)),
                ("stains[0].drips.color[3]", WetRanges.Unit, b => ST(b, s => s.Drips.Applied().Color.A)),
                ("stains[0].drips.shine", WetRanges.Unit, b => ST(b, s => s.Drips.Applied().Shine)),
                ("stains[0].puddle.color[0]", WetRanges.Unit, b => ST(b, s => s.Puddle.Applied().Color.R)),
                ("stains[0].puddle.shine", WetRanges.Unit, b => ST(b, s => s.Puddle.Applied().Shine)),
                ("stains[0].puddle.edge", WetRanges.PuddleEdge, b => ST(b, s => s.Puddle.Applied().Edge)),
                ("stains[0].puddle.scale", WetRanges.PuddleScale, b => ST(b, s => s.Puddle.Applied().Scale)),
                ("stains[0].hold", WetRanges.Hold, b => ST(b, s => s.AppliedHold)),
                ("film.gloss", WetRanges.Unit, b => b.Film.Applied().Gloss),
                ("film.smooth", WetRanges.Unit, b => b.Film.Applied().Smooth),
                ("film.opacity", WetRanges.Unit, b => b.Film.Applied().Opacity),
                ("mirror.strength", WetRanges.Unit, b => b.Mirror.Applied().Strength),
                ("mirror.scale", WetRanges.MirrorScale, b => b.Mirror.Applied().Scale),
                ("floor.offset", WetRanges.FloorOffset, b => b.Floor.AppliedOffset),
            };
            var whole = Block().ToJObject();
            foreach (var (path, range, applied) in rows)
            {
                string member = System.Text.RegularExpressions.Regex.Replace(path, @"\[\d\]$", "");
                foreach (var (value, label) in new[] { (range.Min - 1f, "below"), (range.Max + 1f, "above") })
                {
                    var j = (JObject)whole.DeepClone();
                    ((JValue)j.SelectToken(path)).Value = (double)value;
                    var f = new FindingList();
                    var d = CharaWet.FromToken(j, f);
                    t.Check(f.Count == 1 && f[0].Code == WetFindings.Range && f[0].Severity == FindingSeverity.Info && f[0].Path.Contains(member), "WF4: " + path + " " + label + " its range: one info at the member (" + f + ")");
                    t.Equal((double)value, (double)(float)d.ToJObject().SelectToken(path), "WF4: " + path + " " + label + " its range is kept and written back");
                    t.Equal(label == "below" ? range.Min : range.Max, applied(d), "WF4: " + path + " " + label + " its range clamps when applied");
                }
                foreach (float end in new[] { range.Min, range.Max })
                {
                    var j = (JObject)whole.DeepClone();
                    ((JValue)j.SelectToken(path)).Value = (double)end;
                    var f = new FindingList();
                    var d = CharaWet.FromToken(j, f);
                    t.Check(f.Count == 0, "WF4: " + path + " at " + end + ": the range's end raises no finding (" + f + ")");
                    t.Equal(end, applied(d), "WF4: " + path + " at " + end + ": applied as it is");
                }
            }

            // Not below 0: the clock, a map's reach, a puddle's area; a kept puddle's colour; values with no range.
            var n = (JObject)whole.DeepClone();
            n["stains"][0]["t"] = -1.5;
            n["stains"][0]["maps"][0]["reach"] = -2.5;
            n["puddles"][0]["area"] = -3.5;
            n["puddles"][0]["color"][0] = 1.5;
            var nf = new FindingList();
            var nd = CharaWet.FromToken(n, nf);
            t.Equal(4, nf.CountOf(WetFindings.Range), "WF4: each has its finding (" + nf + ")");
            t.Check(nd.Stains[0].T == -1.5f && nd.Stains[0].AppliedT == 0f && nd.Stains[0].Maps[0].Reach == -2.5f && nd.Stains[0].Maps[0].AppliedReach == 0f && nd.Puddles[0].Area == -3.5f && nd.Puddles[0].AppliedArea == 0f, "WF4: kept, and not below 0 when applied");
            t.Check(nd.Puddles[0].Color.Value.R == 1.5f, "WF4: a frozen colour is kept");
            var free = (JObject)whole.DeepClone();
            free["floor"]["height"] = 5000;
            free["stains"][0]["origin"]["down"] = -500;
            free["stains"][0]["origin"]["local"][0] = 1000;
            free["stains"][0]["t"] = 1e9;
            free["stains"][0]["seed"] = -5;
            free["stains"][0]["maps"][0]["slot"] = 99;
            free["puddles"][0]["center"][1] = -700;
            var ff = new FindingList();
            CharaWet.FromToken(free, ff);
            t.Equal(0, ff.Count, "WF4: a world height, an origin, a long clock, any seed, any slot have no range (" + ff + ")");
            var fl = new FindingList();
            var fixedFloor = CharaWet.Parse("{\"floor\": {\"mode\": \"fixed\", \"height\": -30}}", fl);
            t.Check(fl.Count == 0 && fixedFloor.Floor.Height == -30f, "WF4: a fixed floor is a world height");

            // Applied copies carry the switches; values in range do not move.
            var bl = Block();
            foreach (var st in bl.Stains)
            {
                st.Shape = st.Shape.Applied();
                st.Look = st.Look.Applied();
                st.Drips = st.Drips.Applied();
                st.Puddle = st.Puddle.Applied();
            }
            bl.Film = bl.Film.Applied();
            bl.Mirror = bl.Mirror.Applied();
            t.Equal(Block().ToJson(), bl.ToJson(), "WF4: values in range are applied unchanged, with their switches");
        }

        static string StainJson(WetStain s)
        {
            var b = new CharaWet();
            b.Stains.Add(s);
            return b.ToJson();
        }

        static JArray Stains(int n)
        {
            var a = new JArray();
            for (int i = 0; i < n; i++) a.Add(JObject.Parse("{\"id\": \"s" + i + "\"}"));
            return a;
        }

        static void Limits(TestRunner t)
        {
            var f0 = new FindingList();
            var at = CharaWet.FromToken(new JObject { ["stains"] = Stains(WetLimits.MaxStains) }, f0);
            t.Check(f0.Count == 0 && at.AppliedStains().Count() == WetLimits.MaxStains, "WF4: 8 stains are within the limit");
            var f1 = new FindingList();
            var over = CharaWet.FromToken(new JObject { ["stains"] = Stains(WetLimits.MaxStains + 3) }, f1);
            t.Check(f1.Count == 1 && f1[0].Code == WetFindings.Limit && f1[0].Severity == FindingSeverity.Warning && f1[0].Message.Contains("3 ") && f1[0].Path.Contains("stains"), "WF4: 11 stains: one warning naming the excess (" + f1 + ")");
            t.Equal(WetLimits.MaxStains + 3, over.Stains.Count, "WF4: the excess is kept in the document");
            t.Equal(WetLimits.MaxStains + 3, over.ToJObject()["stains"].Count(), "WF4: and written back");
            var applied = over.AppliedStains().ToList();
            t.Check(applied.Count == WetLimits.MaxStains && applied[applied.Count - 1].Id == "s7", "WF4: the first 8 act");

            string res = "res/" + new string('0', 64) + ".png";
            var maps = new JArray();
            for (int i = 0; i < WetLimits.MaxMaps + 1; i++) maps.Add(new JObject { ["renderer"] = "body/r" + i, ["slot"] = 0, ["res"] = res, ["reach"] = 1, ["mesh"] = "m" });
            var f2 = new FindingList();
            var md = CharaWet.FromToken(new JObject { ["stains"] = new JArray(new JObject { ["id"] = "a", ["maps"] = maps }) }, f2);
            t.Check(f2.Count == 1 && f2[0].Code == WetFindings.Limit && f2[0].Path.Contains("maps") && md.Stains[0].Maps.Count == WetLimits.MaxMaps + 1 && md.Stains[0].AppliedMaps().Count() == WetLimits.MaxMaps, "WF4: 65 maps: kept, one warning, 64 act (" + f2 + ")");
            maps.RemoveAt(0);
            var f3 = new FindingList();
            CharaWet.FromToken(new JObject { ["stains"] = new JArray(new JObject { ["id"] = "a", ["maps"] = maps }) }, f3);
            t.Equal(0, f3.Count, "WF4: 64 maps are within the limit");

            var puddles = new JArray();
            for (int i = 0; i < WetLimits.MaxPuddles + 1; i++) puddles.Add(new JObject { ["stain"] = "a", ["area"] = i });
            var f4 = new FindingList();
            var pd = CharaWet.FromToken(new JObject { ["puddles"] = puddles }, f4);
            t.Check(f4.Count == 1 && f4[0].Code == WetFindings.Limit && pd.Puddles.Count == WetLimits.MaxPuddles + 1 && pd.AppliedPuddles().Count() == WetLimits.MaxPuddles, "WF4: 31 puddles: kept, one warning, 30 act (" + f4 + ")");
            puddles.RemoveAt(0);
            var f5 = new FindingList();
            CharaWet.FromToken(new JObject { ["puddles"] = puddles }, f5);
            t.Equal(0, f5.Count, "WF4: 30 puddles are within the limit");

            string name = new string('n', WetLimits.MaxName);
            var f6 = new FindingList();
            CharaWet.Parse("{\"stains\": [{\"id\": \"a\", \"name\": \"" + name + "\"}]}", f6);
            t.Equal(0, f6.Count, "WF4: a name of 64 characters is within the limit");
            var f7 = new FindingList();
            var nd = CharaWet.Parse("{\"stains\": [{\"id\": \"a\", \"name\": \"" + name + "n\"}]}", f7);
            t.Check(f7.Count == 1 && f7[0].Code == WetFindings.Limit && f7[0].Severity == FindingSeverity.Warning && f7[0].Path.Contains("name") && nd.Stains[0].Name.Length == WetLimits.MaxName + 1, "WF4: a name of 65 characters: kept, one warning (" + f7 + ")");

            // An edit's check.
            var eight = CharaWet.FromToken(new JObject { ["stains"] = Stains(WetLimits.MaxStains) });
            t.Check(eight.Problem() == null, "WF4: Problem: 8 stains pass");
            var nine = CharaWet.FromToken(new JObject { ["stains"] = Stains(WetLimits.MaxStains + 1) });
            t.Check(nine.Problem()?.StartsWith(WetFindings.Limit, StringComparison.Ordinal) == true && nine.Problem(eight)?.StartsWith(WetFindings.Limit, StringComparison.Ordinal) == true, "WF4: Problem: a ninth stain is refused");
            t.Check(nine.Problem(nine) == null && eight.Problem(nine) == null, "WF4: Problem: a file's nine may stay or shrink");
            var ten = CharaWet.FromToken(new JObject { ["stains"] = Stains(WetLimits.MaxStains + 2) });
            t.Check(ten.Problem(nine) != null, "WF4: Problem: but not grow");
            var manyPuddles = CharaWet.FromToken(new JObject { ["puddles"] = new JArray(Enumerable.Range(0, WetLimits.MaxPuddles + 1).Select(i => JObject.Parse("{\"stain\": \"a\"}"))) });
            t.Check(manyPuddles.Problem()?.Contains("puddles") == true && manyPuddles.Problem(manyPuddles) == null, "WF4: Problem: puddles likewise");
            var manyMaps = CharaWet.FromToken(new JObject { ["stains"] = new JArray(new JObject { ["id"] = "a", ["maps"] = new JArray(Enumerable.Range(0, WetLimits.MaxMaps + 1).Select(i => new JObject { ["renderer"] = "body/r", ["slot"] = 0, ["res"] = res, ["reach"] = 1, ["mesh"] = "m" })) }) });
            t.Check(manyMaps.Problem()?.Contains("maps") == true && manyMaps.Problem(manyMaps) == null, "WF4: Problem: maps likewise");
            var longName = CharaWet.Parse("{\"stains\": [{\"id\": \"a\", \"name\": \"" + name + "n\"}]}");
            t.Check(longName.Problem()?.Contains("name") == true && longName.Problem(longName) == null, "WF4: Problem: a name likewise");
        }

        static void Kept(TestRunner t)
        {
            // An unknown origin mode: kept, inactive; an unknown floor mode: kept, the default acts.
            var f = new FindingList();
            var d = CharaWet.Parse("{\"stains\": [{\"id\": \"a\", \"origin\": {\"mode\": \"future\"}}], \"floor\": {\"mode\": \"later\"}}", f);
            t.Check(f.CountOf("json.unknownEnum") == 2 && f.Count == 2, "WF4: unknown modes: one warning each (" + f + ")");
            t.Check(d.Stains[0].Origin.Mode == "future" && !d.Stains[0].Origin.IsActive, "WF4: an unknown origin mode is kept, inactive");
            t.Check(d.Floor.Mode == "later" && d.Floor.AppliedMode == WetFloorValues.ModeMap, "WF4: an unknown floor mode is kept and acts as the map's");
            t.Check(CharaWet.Parse(d.ToJson()).ToJson() == d.ToJson() && d.ToJObject()["stains"][0]["origin"]["mode"].ToString() == "future", "WF4: and they are written back");

            // A repeated id: the later one derived, in order; none: derived.
            var rf = new FindingList();
            var r = CharaWet.Parse("{\"stains\": [{\"id\": \"a\"}, {\"id\": \"a\"}, {\"id\": \"b\"}, {\"id\": \"a\"}, {}]}", rf);
            t.Equal(3, rf.CountOf(WetFindings.Id), "WF4: each repeat and the missing id has its finding (" + rf + ")");
            t.Check(rf[0].Severity == FindingSeverity.Info && rf[0].Path.Contains("stains[1]"), "WF4: an info at the stain");
            t.Equal("a," + JsonText.DerivedId("wet/stain/1") + ",b," + JsonText.DerivedId("wet/stain/3") + "," + JsonText.DerivedId("wet/stain/4"), string.Join(",", r.Stains.Select(s => s.Id)), "WF4: each by its index");
            var clash = CharaWet.Parse("{\"stains\": [{\"id\": \"a\"}, {\"id\": \"" + JsonText.DerivedId("wet/stain/2") + "\"}, {\"id\": \"a\"}]}");
            t.Equal(3, clash.Stains.Select(s => s.Id).Distinct().Count(), "WF4: a derived id that is taken moves on");
            t.Check(CharaWet.Parse(r.ToJson()).ToJson() == r.ToJson(), "WF4: stable afterwards");
            t.Check(r.IdProblem() == null, "WF4: IdProblem passes such a block");
            t.Check(new CharaWet { Stains = { new WetStain { Id = "a" }, new WetStain { Id = "a" } } }.IdProblem()?.Contains("twice") == true, "WF4: IdProblem: an id used twice");
            t.Check(new CharaWet { Stains = { new WetStain() } }.IdProblem()?.Contains("without") == true, "WF4: IdProblem: no id");

            // A dangling reference from the file's own text is no business of the reader.
            var c = new FindingList();
            var cur = CharaWet.Parse("{\"stains\": [{\"id\": \"a\"}], \"current\": \"nope\", \"puddles\": [{\"stain\": \"gone\"}]}", c);
            t.Check(c.Count == 0 && cur.CurrentStain == null && cur.Puddles[0].Stain == "gone" && cur.ToJObject()["current"].ToString() == "nope", "WF4: a current or a puddle naming no stain is kept, with no finding");
        }

        static void MissingPictures(TestRunner t)
        {
            var s = FixtureScene();
            var file = s.ToBytes(FixtureOptions());
            string lost = ResStore.NameOf(PictureB());
            var hand = InfraFixtures.Repack(file, m => m.Remove(lost), "scene.json");
            var l = SceneContainer.Load(hand, LOpts());
            t.Check(l.Ok && !l.NewerFormat, "WF4: a file without one picture loads (" + l + ")");
            if (!l.Ok) return;
            t.Equal(1, l.Findings.CountOf(WetFindings.MapMissing), "WF4: one finding, for that map");
            var f = Enumerable.Range(0, l.Findings.Count).Select(i => l.Findings[i]).First(x => x.Code == WetFindings.MapMissing);
            t.Check(f.Severity == FindingSeverity.Warning && f.Path.StartsWith("scene.json", StringComparison.Ordinal) && f.Path.Contains("nodes[0].chara.wet.stains[0].maps[1].res"), "WF4: a warning that names entry and path (" + f.Path + ")");
            t.Equal(Block().ToJson(), CharaOf(l.Value, CharaA).Wet.ToJson(), "WF4: the entry stays in the block");
            t.Check(!l.Value.Res.Contains(lost) && l.Value.Res.Contains(ResStore.NameOf(PictureA())), "WF4: the other pictures are there");
            var sr = new FindingList();
            var saved = l.Value.Save(Path.Combine(TempOf(t), "wf4_missing.hs2nscene"), FixtureOptions());
            t.Check(saved.Ok && saved.Findings.Has("res.dangling"), "WF4: a save keeps the entry and says the picture is not in the file (" + saved.Findings + ")");
            t.Equal(0, SceneContainer.Load(file, LOpts()).Findings.CountOf(WetFindings.MapMissing), "WF4: a whole file has no such finding");
            t.Equal(3, Block().ReportMissingMaps(new ResStore(), sr, "e", "p"), "WF4: ReportMissingMaps counts every map of an empty store");
            t.Equal(3, sr.Count, "WF4: and reports each");
            t.Equal(0, new CharaWet().ReportMissingMaps(null, null, "e", "p"), "WF4: a block without stains reports nothing");
        }

        static string TempOf(TestRunner t) => t.TempDir ?? Path.GetTempPath();

        // ---------------- WF5 ----------------

        static void WF5(TestRunner t)
        {
            t.Check(SaveFormats.WetVersion == 5 && SaveFormats.SupportedVersion == 5 && CoreMigrationRegistry.Default.Supported == 5 && SaveFormats.MinReaderVersion == 1 && SaveFormats.FormatVersion == 1,
                "WF5: the block at revision 5, S = 5, minReaderVersion 1, the base revision 1");
            t.Throws<InvalidOperationException>(() => new CoreMigrationRegistry(5, CoreMigrationRegistry.Empty(1), CoreMigrationRegistry.Empty(2), CoreMigrationRegistry.Empty(3)), "WF5: a registry of 5 without the migration 4 → 5 has a gap");
            t.Equal(4, CoreMigrationRegistry.Empty(4).From, "WF5: the step is From 4");
            var manifest = JObject.Parse(InfraFixtures.ManifestJson("hs2next.scene", 4));
            var before = manifest.ToString();
            var fl = new FindingList();
            var docs = new Dictionary<string, JObject> { ["scene.json"] = JObject.Parse("{\"nodes\": []}") };
            CoreMigrationRegistry.Default.Upgrade(4, new MigrationContext("hs2next.scene", manifest, docs, fl, 4), fl);
            t.Check(fl.Count == 0 && manifest.ToString() == before && docs["scene.json"].ToString() == JObject.Parse("{\"nodes\": []}").ToString(), "WF5: the migration changes nothing and reports nothing (" + fl + ")");

            // Revisions 1 to 4: the same file under each number, no finding, written at its lowest covering revision.
            var s = InfraFixtures.SmallScene(1, 1);
            var b0 = s.ToBytes(Opts());
            for (int revision = 1; revision <= 4; revision++)
            {
                int r = revision;
                var plain = InfraFixtures.Repack(b0, m => InfraFixtures.EditJson(m, "manifest.json", j => j["formatVersion"] = r), "scene.json");
                var l = SceneContainer.Load(plain, LOpts());
                t.Check(l.Ok && !l.NewerFormat && l.Findings.Count == 0, "WF5: revision " + r + " loads with no finding (" + l + ")");
                if (l.Ok) t.Check(Same(EntryBytes(b0, "scene.json"), EntryBytes(l.Value.ToBytes(Opts()), "scene.json")) && FormatOf(l.Value.ToBytes(Opts())) == 1, "WF5: revision " + r + ": written back as the base revision, scene.json byte-equal");

                // Such a file holding the member an older app wrote back: known here, read silently, the next save revision 5.
                string blockJson = Small().ToJson();
                var held = InfraFixtures.Repack(b0, m =>
                {
                    InfraFixtures.EditJson(m, "scene.json", j => ((JObject)j["nodes"][0]["chara"]).Add("wet", JObject.Parse(blockJson)));
                    InfraFixtures.EditJson(m, "manifest.json", j => j["formatVersion"] = r);
                }, "scene.json");
                var lh = SceneContainer.Load(held, LOpts());
                t.Check(lh.Ok && !lh.NewerFormat && lh.Findings.Count == 0, "WF5: revision " + r + " holding the member reads silently (" + lh + ")");
                if (lh.Ok)
                {
                    t.Equal(blockJson, ((CharaNodeState)lh.Value.Scene.Nodes[0].State).Wet.ToJson(), "WF5: revision " + r + ": with its values");
                    t.Equal(5, FormatOf(lh.Value.ToBytes(Opts())), "WF5: revision " + r + ": its next save is revision 5");
                }
            }
            var v5 = InfraFixtures.Repack(b0, m => InfraFixtures.EditJson(m, "manifest.json", j => j["formatVersion"] = 5), "scene.json");
            var l5 = SceneContainer.Load(v5, LOpts());
            t.Check(l5.Ok && !l5.NewerFormat && l5.Findings.Count == 0, "WF5: revision 5 is this reader's own");
            var v6 = InfraFixtures.Repack(b0, m => InfraFixtures.EditJson(m, "manifest.json", j => j["formatVersion"] = 6), "scene.json");
            var l6 = SceneContainer.Load(v6, LOpts());
            t.Check(l6.Ok && l6.NewerFormat && l6.Findings.Count == 1 && l6.Findings[0].Code == "manifest.newerFormat" && l6.Findings[0].Message.Contains("format 6 is newer than 5"), "WF5: revision 6 is newer (" + l6 + ")");
        }

        // ---------------- WF6, as far as this reader can show it ----------------

        static void WF6(TestRunner t)
        {
            string path = FixturePath(t.FixturesDir);
            if (!File.Exists(path)) { t.Check(false, "WF6: fixture " + path + " present"); return; }
            var fixture = File.ReadAllBytes(path);
            var now = FixtureScene().ToBytes(FixtureOptions());
            foreach (var e in ContainerReader.Read(fixture).Entries)
                t.Check(Same(e.Data, EntryBytes(now, e.Name)), "WF6: the fixture's " + e.Name + " is what this writer writes for it");
            t.Check(FormatOf(fixture) == 5 && (int)Entry(fixture, "manifest.json")["minReaderVersion"] == 1, "WF6: the fixture is revision 5, minReaderVersion 1");
            t.Equal("anime,hands,face,look,fk,ik,correction,state,voice,advancedPose,wet", Names(Entry(fixture, "scene.json")["nodes"][0]["chara"]), "WF6: the first character holds both blocks");
            t.Equal("anime,hands,face,look,fk,ik,correction,state,voice,wet", Names(Entry(fixture, "scene.json")["nodes"][1]["chara"]), "WF6: the second the wet block alone");
            t.Equal("anime,hands,face,look,fk,ik,correction,state,voice", Names(Entry(fixture, "scene.json")["nodes"][2]["chara"]), "WF6: the third none");

            // This reader: the values, no finding.
            var l5 = SceneContainer.Load(fixture, LOpts());
            t.Check(l5.Ok && !l5.NewerFormat && l5.Findings.Count == 0, "WF6: this reader reads the fixture with no finding (" + l5 + ")");
            if (!l5.Ok) return;
            string blockJson = Block().ToJson(), smallJson = Small().ToJson();
            t.Equal(blockJson, CharaOf(l5.Value, CharaA).Wet.ToJson(), "WF6: and the block's values");
            t.Equal(smallJson, CharaOf(l5.Value, CharaB).Wet.ToJson(), "WF6: and the second block's");
            t.Check(CharaOf(l5.Value, CharaC).Wet.IsEmpty, "WF6: the third character has none");

            // An S = 4 reader: NewerFormat and the one finding.
            var l4 = SceneContainer.Load(fixture, LOpts(Registry4()));
            t.Check(l4.Ok && l4.NewerFormat && l4.Findings.Count == 1 && l4.Findings[0].Code == "manifest.newerFormat" && l4.Findings[0].Message == "format 5 is newer than 4; unknown parts are kept",
                "WF6: an S = 4 registry reads it with NewerFormat and the single finding manifest.newerFormat (" + l4 + ")");
            if (!l4.Ok) return;

            // What that reader holds: `wet` as an unknown member of the section, written back after advancedPose or voice.
            var c = l4.Value;
            var raw = DocReader.Parse(EntryBytes(fixture, "scene.json"), "scene.json");
            for (int i = 0; i < 2; i++)
            {
                var held = (CharaNodeState)c.Scene.Nodes[i].State;
                held.Wet = new CharaWet();
                held.Unknown = new JsonLeftovers();
                held.Unknown.Add("wet", raw["nodes"][i]["chara"]["wet"].DeepClone());
            }
            string saved = Path.Combine(TempOf(t), "wf6_downgraded.hs2nscene");
            var sr = c.Save(saved, FixtureOptions());
            t.Check(sr.Ok && sr.Downgraded && sr.Findings.Count == 0, "WF6: saving it reports Downgraded and nothing else (" + sr.Findings + ")");
            var back = File.ReadAllBytes(saved);
            t.Equal(4, FormatOf(back), "WF6: written as its own lowest covering revision: 4, for the advanced-pose block");
            t.Check(Same(EntryBytes(fixture, "scene.json"), EntryBytes(back, "scene.json")), "WF6: scene.json byte-equal\n    " + DocIo.FirstDiff(EntryBytes(fixture, "scene.json") ?? new byte[0], EntryBytes(back, "scene.json") ?? new byte[0]));
            foreach (var p in new[] { PictureA(), PictureB(), PictureC() })
                t.Check(Same(p, EntryBytes(back, ResStore.NameOf(p))), "WF6: picture " + ResStore.NameOf(p) + " kept, byte-equal");

            // This reader reads that file back: the same values, silently; and writes revision 5 again.
            var lb = SceneContainer.Load(back, LOpts());
            t.Check(lb.Ok && !lb.NewerFormat && lb.Findings.Count == 0, "WF6: this reader then reads it silently (" + lb + ")");
            if (!lb.Ok) return;
            t.Equal(blockJson, CharaOf(lb.Value, CharaA).Wet.ToJson(), "WF6: with the same values");
            t.Check(CharaOf(lb.Value, CharaA).Unknown == null, "WF6: the member is known again");
            t.Check(FormatOf(lb.Value.ToBytes(FixtureOptions())) == 5, "WF6: and writes revision 5 again");
        }

        // ---------------- WF7 ----------------

        static void WF7(TestRunner t)
        {
            // Stopped; running with an awkward clock and a puddle mid-growth; running in a loop, a block with unknown members. Each save -> load -> save the same bytes.
            var cases = new List<(string what, Action<CharaWet> shape)>
            {
                ("a stopped stain", b => { b.Stains[1].Running = false; b.Stains[1].T = 0f; }),
                ("a running stain", b => { b.Stains[1].Running = true; b.Stains[1].T = 1f / 3f; b.Puddles[1].Area = 0.1f + 0.2f; }),
                ("a clock past the spread", b => { b.Stains[1].T = 12345.678f; }),
                ("a block with an unknown member", b => { b.Unknown = new JsonLeftovers(); b.Unknown.Add("future", JObject.Parse("{\"a\": 0.1}")); }),
            };
            foreach (var (what, shape) in cases)
            {
                var s = InfraFixtures.SmallScene(1, 0);
                AddPictures(s.Res);
                var w = Block();
                shape(w);
                ((CharaNodeState)s.Scene.Nodes[0].State).Wet = w;
                var first = s.ToBytes(Opts());
                var l = SceneContainer.Load(first, LOpts());
                t.Check(l.Ok && l.Findings.Count == 0, "WF7: " + what + " loads with no finding (" + l + ")");
                if (!l.Ok) continue;
                var second = l.Value.ToBytes(Opts());
                t.Check(Same(first, second), "WF7: " + what + ": save -> load -> save byte-identical");
                var again = SceneContainer.Load(second, LOpts());
                t.Check(again.Ok && Same(second, again.Value.ToBytes(Opts())), "WF7: " + what + ": and once more");
            }
        }

        // ---------------- WF9 ----------------

        static void WF9(TestRunner t)
        {
            string path = FixturePath(t.FixturesDir);
            if (!File.Exists(path)) { t.Check(false, "WF9: fixture " + path + " present"); return; }
            var file = ContainerReader.Read(File.ReadAllBytes(path));
            var res = file.Entries.Where(e => e.Name.StartsWith("res/", StringComparison.Ordinal)).ToList();
            t.Equal(3, res.Count, "WF9: the fixture's three pictures are its res entries");
            foreach (var e in res) t.Check(MapProblem(e.Data) == null, "WF9: " + e.Name + " is an 8-bit gray PNG (" + MapProblem(e.Data) + ")");

            var pictures = new[] { (PictureA(), 64, 32, .25, .5, 4.0, BloodReach), (PictureB(), 32, 64, .5, .75, 6.0, BloodReach), (PictureC(), 48, 48, .5, .5, 3.0, WaterReach) };
            foreach (var (png, width, height, cx, cy, side, reach) in pictures)
            {
                var values = MapValues(file.Get(ResStore.NameOf(png)).Data, out int w, out int h);
                t.Check(w == width && h == height, "WF9: its size");
                int differ = 0, unreached = 0;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        byte want = MapValue(Distance(x, y, w, h, cx, cy, side), reach);
                        if (values[y * w + x] != want) differ++;
                        if (want == 255) unreached++;
                    }
                t.Equal(0, differ, "WF9: every value is the distance it was made from (" + w + " x " + h + ")");
                t.Check(values.Distinct().Count() > 16, "WF9: a real ramp, not a flat picture");
                if (reach == BloodReach) t.Check(unreached > 0, "WF9: 255 where the stain does not reach");
            }

            // The formula.
            t.Equal(0, (int)MapValue(0, 2), "WF9: distance 0 is 0");
            t.Equal(0, (int)MapValue(-1, 2), "WF9: a distance below 0 is 0");
            t.Equal(127, (int)MapValue(1, 2), "WF9: half the reach is 127");
            t.Equal(254, (int)MapValue(2, 2), "WF9: the reach is 254");
            t.Equal(255, (int)MapValue(2.001, 2), "WF9: past the reach is 255");

            // Row filters: what a real encoder uses decodes as well.
            var plain = new byte[] { 10, 20, 30, 40, 50, 60 };
            t.Check(MapValues(GrayPng(3, 2, plain), out _, out _).SequenceEqual(plain), "WF9: the decoder on filter 0");
            t.Check(MapValues(Filtered(3, new[] { (1, new byte[] { 10, 10, 10 }), (2, new byte[] { 30, 30, 30 }) }), out _, out _).SequenceEqual(plain), "WF9: on filters 1 and 2");
            t.Check(MapValues(Filtered(3, new[] { (0, new byte[] { 10, 20, 30 }), (3, new byte[] { 35, 25, 15 }) }), out _, out _).SequenceEqual(new byte[] { 10, 20, 30, 40, 55, 57 }), "WF9: on filter 3");
            t.Check(MapValues(Filtered(3, new[] { (0, new byte[] { 10, 20, 30 }), (4, new byte[] { 30, 20, 0 }) }), out _, out _).SequenceEqual(new byte[] { 10, 20, 30, 40, 60, 60 }), "WF9: on filter 4, left and up");
            t.Check(MapValues(Filtered(2, new[] { (0, new byte[] { 15, 20 }), (4, new byte[] { 251, 1 }) }), out _, out _).SequenceEqual(new byte[] { 15, 20, 10, 16 }), "WF9: on filter 4, the corner, wrapping");

            // A colour, an alpha, a palette, 16 bits, interlacing and text are each refused.
            string Spoiled(int at, byte value)
            {
                var b = GrayPng(4, 4, new byte[16]);
                b[at] = value;
                return MapProblem(Recrc(b));
            }
            t.Equal(null, MapProblem(GrayPng(4, 4, new byte[16])), "WF9: control: a gray picture passes");
            t.Check(Spoiled(25, 2)?.Contains("colour type") == true, "WF9: an RGB header is refused");
            t.Check(Spoiled(25, 4)?.Contains("colour type") == true, "WF9: gray with alpha is refused");
            t.Check(Spoiled(25, 6)?.Contains("colour type") == true, "WF9: RGBA is refused");
            t.Check(Spoiled(25, 3)?.Contains("colour type") == true, "WF9: a palette is refused");
            t.Check(Spoiled(24, 16)?.Contains("bit depth") == true, "WF9: 16 bits are refused");
            t.Check(Spoiled(28, 1)?.Contains("interlaced") == true, "WF9: interlacing is refused");
            t.Check(MapProblem(new byte[64]) == "not a PNG", "WF9: bytes that are no PNG are refused");
            var ms = new MemoryStream();
            var gray = GrayPng(4, 4, new byte[16]);
            ms.Write(gray, 0, gray.Length - 12);
            Chunk(ms, "tEXt", Encoding.ASCII.GetBytes("Author\0someone"));
            Chunk(ms, "IEND", new byte[0]);
            t.Check(MapProblem(ms.ToArray())?.Contains("tEXt") == true, "WF9: a text chunk is refused");
        }

        // A picture with the given row filters; the rows are as a file holds them.
        static byte[] Filtered(int width, (int filter, byte[] row)[] rows)
        {
            var raw = new MemoryStream();
            foreach (var (filter, row) in rows)
            {
                raw.WriteByte((byte)filter);
                raw.Write(row, 0, width);
            }
            var b = raw.ToArray();
            var z = new MemoryStream();
            z.WriteByte(0x78);
            z.WriteByte(0x01);
            z.WriteByte(1);
            z.WriteByte((byte)b.Length);
            z.WriteByte((byte)(b.Length >> 8));
            z.WriteByte((byte)~b.Length);
            z.WriteByte((byte)(~b.Length >> 8));
            z.Write(b, 0, b.Length);
            z.Write(new byte[4], 0, 4);
            var o = new MemoryStream();
            o.Write(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }, 0, 8);
            var head = new byte[13];
            Big(head, 0, (uint)width);
            Big(head, 4, (uint)rows.Length);
            head[8] = 8;
            Chunk(o, "IHDR", head);
            Chunk(o, "IDAT", z.ToArray());
            Chunk(o, "IEND", new byte[0]);
            return o.ToArray();
        }

        static byte[] Recrc(byte[] png)
        {
            var crc = new byte[17];
            Array.Copy(png, 12, crc, 0, 17);
            Big(png, 29, Crc32.Compute(crc));
            return png;
        }

        // ---------------- WF10 ----------------

        // The name of the profile folder, in pieces: this file holds the check of a path, not a path.
        const string ProfileFolder = "Us" + "ers";

        /// <summary>What makes a string of a file look like a local path, a mail address or a machine's name, or null.</summary>
        public static string LocalText(string s)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^[A-Za-z]:[\\/]") || s.Contains(":\\")) return "a drive path";
            if (s.Contains("\\\\") || s.StartsWith("\\", StringComparison.Ordinal)) return "a network path";
            if (s.StartsWith("/", StringComparison.Ordinal) || s.Contains("/" + ProfileFolder + "/") || s.Contains("/home/") || s.Contains("~/")) return "a home path";
            if (s.IndexOf('@') >= 0) return "an address";
            return null;
        }

        static IEnumerable<JValue> Strings(JToken t)
        {
            if (t is JValue v && v.Type == JTokenType.String) yield return v;
            else if (t is JContainer c) foreach (var x in c.Children()) foreach (var y in Strings(x is JProperty p ? p.Value : x)) yield return y;
        }

        static void WF10(TestRunner t)
        {
            string path = FixturePath(t.FixturesDir);
            if (!File.Exists(path)) { t.Check(false, "WF10: fixture " + path + " present"); return; }
            var file = ContainerReader.Read(File.ReadAllBytes(path));
            int strings = 0;
            foreach (var e in file.Entries)
            {
                if (e.Name.EndsWith(".json", StringComparison.Ordinal))
                {
                    foreach (var s in Strings(JObject.Parse(Text(e.Data))))
                    {
                        strings++;
                        string why = LocalText((string)s.Value);
                        if (why != null) t.Check(false, "WF10: " + e.Name + ":" + s.Path + " holds " + why);
                    }
                }
                else if (e.Name.StartsWith("res/", StringComparison.Ordinal))
                    t.Check(MapProblem(e.Data) == null, "WF10: " + e.Name + " holds nothing beside the image");
                else t.Check(false, "WF10: an entry of a kind this check does not know: " + e.Name);
            }
            t.Check(strings > 50, "WF10: the strings of every document were read (" + strings + ")");

            // The block holds these strings and no other: names that a user types are a stain's and nothing else.
            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "id", "name", "preset", "mode", "bone", "renderer", "res", "mesh", "current", "stain",
            };
            var j = Block().ToJObject();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in Strings(j))
            {
                string member = ((JProperty)s.Parent).Name;
                seen.Add(member);
                t.Check(allowed.Contains(member), "WF10: the block's string member " + member + " is one of the schema's");
            }
            t.Equal(string.Join(",", allowed.OrderBy(x => x, StringComparer.Ordinal)), string.Join(",", seen.OrderBy(x => x, StringComparer.Ordinal)), "WF10: every string member was seen");
            t.Equal("blood,water", string.Join(",", Block().Stains.Select(s => s.Name).OrderBy(x => x, StringComparer.Ordinal)), "WF10: the fixture's names are a stain's");

            // Controls of the check.
            t.Check(LocalText("C" + ":\\" + ProfileFolder + "\\x") != null && LocalText("D" + ":/a") != null && LocalText("\\\\host\\share") != null && LocalText("/home/x") != null && LocalText("~/x")!= null && LocalText("a@b") != null, "WF10: control: local texts are seen");
            t.Equal("a home path", LocalText("a/U" + "sers/b"), "WF10: control: a profile folder inside a text is seen, spelt apart from the check's own constant");
            t.Check(LocalText("res/" + new string('0', 64) + ".png") == null && LocalText("clothes:1/o_bot_a") == null && LocalText("o_bot_a|1847|8f3a11c2") == null && LocalText("acc:" + Id(7) + "/m#2") == null, "WF10: control: the block's own forms pass");
        }
    }
}

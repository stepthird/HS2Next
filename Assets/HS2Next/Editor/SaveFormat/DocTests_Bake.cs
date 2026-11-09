// The bytes of a bake and the two item members that name them (scene.json `item.bake` and `item.cut`, format revision 5). BM1 the blob layout and the main's JSON
// on hand-made blobs of each kind, canonical writing, unknown members kept at every level of the main; BF1 a scene without the members keeps its bytes and its
// revision on the committed files of every revision; BF2 the reader's types (every null and every wrong type of a known member fails), the order of the members,
// written only when present, unknown members kept; BF3 the blob round trip and its refusals (bake.blobCorrupt); BF5 ResStore.AddBlob and the scan of `res/`
// names through `bake` and `cut.source`; BF6's round trip as far as it runs here (a revision-4 registry over this reader, the members moved to the unknown members
// as 1.3 holds them; the run on 1.3's own save layer is recorded); BF7 save -> load -> save byte-identical; BF8 the reader rules
// and limits, and the gathering of a block's blobs from the scene and the store. The fixture Tests/SaveFormat/fixtures/bake/scene_r5_bake.hs2nscene is what this
// writer writes for FixtureScene() over the fixture's own blobs. Synthetic ids, names and texels only.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using HS2Next.Scenes.Bake;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save.Tests
{
    public static class DocTests_Bake
    {
        public static string Id(int n) => n.ToString("x32");

        public static readonly string BakeA = Id(0xb1), BakeB = Id(0xb2), ItemA = Id(0xb4), PieceB = Id(0xb5), ItemC = Id(0xb6), CharaD = Id(0xb7);
        public static readonly DateTime Clock = new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);

        static string Text(byte[] b) => new UTF8Encoding(false).GetString(b);
        static string Names(JToken o) => string.Join(",", ((JObject)o).Properties().Select(p => p.Name));
        static bool Same(byte[] a, byte[] b) => a != null && b != null && a.SequenceEqual(b);
        static string TempOf(TestRunner t) => t.TempDir ?? Path.GetTempPath();

        // ---------------- hand-made data ----------------

        /// <summary>A quad with every channel: normals, tangents, UV channels of dimension 2, 3 and 1, colours, weights on two bones, two bind poses,
        /// a triangle and a line sub-mesh. <paramref name="seed"/> moves its values.</summary>
        public static BakeMeshData HandMesh(int seed = 0)
        {
            float o = 0.125f * seed;
            var m = new BakeMeshData { VertexCount = 4 };
            m.Positions = new[] { o, 0f, 0f, 1f + o, 0f, 0f, 1f + o, 1f, 0.5f, o, 1f, -0.25f };
            m.Normals = new[] { 0f, 0f, 1f, 0f, 0f, 1f, 0f, 0.6f, 0.8f, 0f, 0f, -1f };
            m.Tangents = new[] { 1f, 0f, 0f, 1f, 1f, 0f, 0f, -1f, 0f, 1f, 0f, 1f, 0.6f, 0.8f, 0f, 1f };
            m.SetUv(0, 2, new[] { 0f, 0f, 1f, 0f, 1f, 1f, 0f, 1f });
            m.SetUv(1, 3, new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.1f, 1.2f, 1.3f });
            m.SetUv(7, 1, new[] { 0.25f, 0.5f, 0.75f, 1f + o });
            m.Colors = new byte[] { 255, 0, 0, 255, 0, 255, 0, 128, 0, 0, 255, 64, (byte)(seed * 7), 1, 2, 3 };
            m.BoneIndices = new ushort[] { 0, 1, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0 };
            m.BoneWeights = new[] { 0.75f, 0.25f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0.5f, 0.5f, 0f, 0f };
            m.BindPoses = new[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, -1f, -2f, 0.5f + o, 1f };
            m.SubMeshes.Add(new BakeSubMesh { Topology = BakeSubMesh.Triangles, Indices = new[] { 0, 1, 2, 0, 2, 3 } });
            m.SubMeshes.Add(new BakeSubMesh { Topology = BakeSubMesh.Lines, Indices = new[] { 0, 1 } });
            return m;
        }

        /// <summary>A 4 x 2 texture of 8-bit sRGB texels with its whole chain of three levels.</summary>
        public static BakeTextureData HandTexture(int seed = 0)
        {
            var t = new BakeTextureData { Width = 4, Height = 2, Format = "R8G8B8A8_SRGB", SourceFormat = "DXT5" };
            int[] sizes = { 32, 8, 4 };
            for (int l = 0; l < 3; l++)
            {
                var b = new byte[sizes[l]];
                for (int i = 0; i < b.Length; i++) b[i] = (byte)(i * 13 + l * 51 + seed * 31);
                t.Levels.Add(b);
            }
            return t;
        }

        /// <summary>A main with every member present and every value away from its default, naming one mesh and one texture blob.</summary>
        public static BakeMain HandMain(string id, string meshSha, string texSha)
        {
            var m = new BakeMain
            {
                Id = id,
                Kind = BakeKinds.Parts,
                Created = Clock,
                Source = new BakeMainSource { Kind = BakeKinds.SourceItem, Name = "Hand item", Sex = 1, Ref = "test.mod#7", Group = 11, Category = 12 },
                Parts = new List<string> { "clothes0", "acc3" },
                Origin = BakeKinds.OriginCentre,
                Fk = new List<string> { "cf_J_Hips", "cf_J_Spine01" },
                Correction = new[] { true, false, true, false, false, false, false, true },
                Insides = new BakeMainInsides { Fill = true, Section = CutSectionColors.Flesh, OwnInside = false },
            };
            m.Nodes.Add(new BakeMainNode { Name = "centre", Parent = -1, P = new Vec3(0.5f, 1.25f, -2f), R = new Quat4(0f, 0.6f, 0f, 0.8f), S = new Vec3(1.5f, 1.5f, 1.5f), Layer = 10 });
            m.Nodes.Add(new BakeMainNode { Name = "cf_J_Hips", Parent = 0, P = new Vec3(0f, 9.5f, 0f), Layer = 10 });
            m.Nodes.Add(new BakeMainNode { Name = "cf_J_Spine01", Parent = 1, P = new Vec3(0f, 0.75f, 0.1f), R = new Quat4(0.28f, 0f, 0f, 0.96f), Active = false, Layer = 10 });
            m.Meshes.Add(new BakeMainMesh { Blob = meshSha, Name = "o_body_cf" });
            m.Textures.Add(new BakeMainTexture { Blob = texSha, Name = "create_skin_body", Wrap = new[] { 1, 2 }, Filter = 2, Aniso = 4, Bias = -0.5f });
            var mat = new BakeMainMaterial
            {
                Name = "cf_m_skin_body_00", Shader = "HS2Next/AIT Skin True", Queue = 2000,
                Keywords = new List<string> { "_NORMALMAP", "_ALPHATEST_ON" }, PassesOff = new List<string> { "ShadowCaster" }, RenderType = "TransparentCutout",
                Inside = new[] { 0.21f, 0.15f, 0.13f },
            };
            mat.Props["_Color"] = BakeMainProp.Color(new Vec4(1f, 0.5f, 0.25f, 1f));
            mat.Props["_Glossiness"] = BakeMainProp.Float(0.5f);
            mat.Props["_MainTex"] = BakeMainProp.Texture(0, new Vec4(2f, 2f, 0.5f, 0.25f));
            mat.Props["_BumpMap"] = BakeMainProp.Texture(-1, new Vec4(1f, 1f, 0f, 0f));
            mat.Props["_Mode"] = BakeMainProp.Integer(1);
            mat.Props["_Offset"] = BakeMainProp.Vector(new Vec4(0.1f, 0.2f, 0.3f, 0.4f));
            m.Materials.Add(mat);
            var lit = new BakeMainMaterial { Name = "lit", Shader = "Universal Render Pipeline/Lit" };
            lit.Props["_BaseColor"] = BakeMainProp.Color(new Vec4(0.5f, 0.5f, 0.5f, 1f));
            m.Materials.Add(lit);
            m.Renderers.Add(new BakeMainRenderer
            {
                Node = 1, Skinned = true, Mesh = 0, Materials = new[] { 0, 1 }, Bones = new[] { 1, 2 }, Root = 1, Bounds = new[] { 0f, 1f, 0f, 2f, 4f, 1f },
                Shadow = 2, Receive = false, LightProbe = 0, ReflProbe = 0, Anchor = 0, UpdateOffscreen = true, Quality = 4, Part = "clothes0", Member = true, Inside = BakeKinds.InsideFill,
            });
            m.Renderers.Add(new BakeMainRenderer { Node = 2, Mesh = 0, Materials = new[] { 1 }, Part = "acc3", Inside = BakeKinds.InsideSection });
            return m;
        }

        /// <summary>The blobs of one hand-made bake and the item block naming them.</summary>
        public sealed class HandBake
        {
            public byte[] Main, Mesh, Texture;
            public BakeMain MainDoc;
            public ItemBake Block;

            /// <summary>Adds the three blobs to <paramref name="res"/>.</summary>
            public void AddTo(ResStore res)
            {
                res.AddBlob(Main);
                res.AddBlob(Mesh);
                res.AddBlob(Texture);
            }

            public IEnumerable<KeyValuePair<string, byte[]>> ByName() => new[] { Main, Mesh, Texture }.Select(b => new KeyValuePair<string, byte[]>(BakeBlob.Sha256(b), b));
        }

        /// <summary>A bake of <see cref="HandMesh"/>, <see cref="HandTexture"/> and <see cref="HandMain"/> with identity <paramref name="id"/>.</summary>
        public static HandBake MakeBake(string id, int seed, int? no = null)
        {
            var h = new HandBake { Mesh = HandMesh(seed).ToBlob(), Texture = HandTexture(seed).ToBlob() };
            h.MainDoc = HandMain(id, BakeBlob.Sha256(h.Mesh), BakeBlob.Sha256(h.Texture));
            h.Main = h.MainDoc.ToBlob();
            h.Block = h.MainDoc.ToItemBake(BakeBlob.Sha256(h.Main), no);
            return h;
        }

        /// <summary>A cut of <paramref name="source"/>: a ragged plane and a box, a pose, a section; every member present.</summary>
        public static ItemCut HandCut(ItemBake source)
        {
            var c = new ItemCut { Source = CutSource.Of(source), Pose = new SortedDictionary<string, Vec3>(StringComparer.Ordinal) };
            c.Pose["cf_J_Hips"] = new Vec3(10f, 20f, 30.5f);
            c.Pose["cf_J_Spine01"] = new Vec3(-5f, 0f, 0.25f);
            c.Steps.Add(new CutStep { Shape = CutShapes.Plane, Pos = new Vec3(0f, 8.5f, 0f), Rot = new Quat4(0f, 0f, 0.6f, 0.8f), Size = new Vec3(1f, 1f, 1f), Keep = CutShapes.Front,
                Ragged = new CutRagged { Amp = 0.1f, Tooth = 0.3f, Random = 0.75f, Seed = 42 }, ClothesOnly = true });
            c.Steps.Add(new CutStep { Shape = CutShapes.Box, Pos = new Vec3(1f, 2f, 3f), Size = new Vec3(0.5f, 2f, 0.5f), Keep = CutShapes.Inside });
            c.Section = new CutSection { Color = CutSectionColors.Bone, OwnInside = false };
            return c;
        }

        // ---------------- the fixture ----------------

        public const string FixtureFile = "scene_r5_bake.hs2nscene";
        public const string FixtureTitle = "Bake fixture scene";
        public static readonly DateTime FixtureClock = new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);

        public static string FixturePath(string fixturesRoot) => Path.Combine(fixturesRoot ?? "", "fixtures", "bake", FixtureFile);

        public static SaveOptions FixtureOptions() => new SaveOptions
        {
            Clock = () => FixtureClock,
            Thumbnail = ThumbnailMode.Placeholder,
            Registry = new ExtRegistry(),
            AppVersion = "1.3.1",
        };

        /// <summary>
        /// Three items and a character: an item drawn from bake A (store row 73100001), a piece cut from A (its own result B, the procedure with two steps
        /// and a pose), a plain item, and a synthetic character with no block. <paramref name="a"/> and <paramref name="b"/> give the blobs.
        /// </summary>
        public static SceneContainer FixtureScene(HandBake a, HandBake b)
        {
            var s = new SceneContainer { Title = FixtureTitle };
            s.Scene.Nodes.Add(new SceneNode(ItemA, new ItemNodeState { Ref = "hs2next.bake#73100001", Group = 731406, Category = 0, Bake = a.Block.Clone() }) { Order = 0, Name = "Hand item" });
            s.Scene.Nodes.Add(new SceneNode(PieceB, new ItemNodeState { Ref = "hs2next.bake#73100001", Group = 731406, Category = 0, Bake = b.Block.Clone(), Cut = HandCut(a.Block) }) { Order = 1, Name = "Hand item front" });
            s.Scene.Nodes.Add(new SceneNode(ItemC, new ItemNodeState { Ref = "test.mod#1" }) { Order = 2 });
            s.Scene.Nodes.Add(new SceneNode(CharaD, new CharaNodeState()) { Order = 3 });
            s.PlaceCharacter(CharaD, InfraFixtures.SmallChara(0));
            a.AddTo(s.Res);
            b.AddTo(s.Res);
            return s;
        }

        /// <summary>The fixture's blobs (seed 1 for A, 2 for B, piece B's identity), as this code makes them.</summary>
        public static void FixtureBakes(out HandBake a, out HandBake b)
        {
            a = MakeBake(BakeA, 1, 73100001);
            b = MakeBake(BakeB, 2);
        }

        /// <summary>Writes the fixture once (refuses an existing file); returns its size.</summary>
        public static long WriteFixture(string fixturesRoot)
        {
            string path = FixturePath(fixturesRoot);
            if (File.Exists(path)) throw new InvalidOperationException("the fixture is never regenerated: " + path + " exists");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            FixtureBakes(out var a, out var b);
            var r = FixtureScene(a, b).Save(path, FixtureOptions());
            if (!r.Ok) throw new InvalidOperationException(r.Error.Message);
            return new FileInfo(path).Length;
        }

        static int FormatOf(byte[] file) => (int)Entry(file, "manifest.json")["formatVersion"];
        static JObject Entry(byte[] file, string name) => DocReader.Parse(ContainerReader.Read(file).Get(name).Data, name);
        static byte[] EntryBytes(byte[] file, string name) => ContainerReader.Read(file).Get(name)?.Data;
        static string SceneText(byte[] file) => Text(EntryBytes(file, "scene.json"));
        static SaveOptions Opts() => InfraFixtures.Opts();
        static LoadOptions LOpts(CoreMigrationRegistry reg = null)
        {
            var o = InfraFixtures.LOpts();
            if (reg != null) o.Migrations = reg;
            return o;
        }
        static ItemNodeState ItemOf(SceneContainer c, string id) => (ItemNodeState)c.Scene.Find(id).State;
        static CoreMigrationRegistry Registry4() => new CoreMigrationRegistry(4, CoreMigrationRegistry.Empty(1), CoreMigrationRegistry.Empty(2), CoreMigrationRegistry.Empty(3));

        public static void Run(TestRunner t)
        {
            DocTests_T1Golden.FixturesDir = t.FixturesDir;
            BM1(t);
            BF1(t);
            BF2(t);
            BF3(t);
            BF5(t);
            BF6(t);
            BF7(t);
            BF8(t);
        }

        // ---------------- BM1 ----------------

        /// <summary>The members of each object of the main, in their order (spec 3.3).</summary>
        static readonly (string what, string members)[] MainMembers =
        {
            ("the main", "format,version,id,kind,created,source,parts,origin,nodes,fk,correction,meshes,textures,materials,renderers,insides"),
            ("source", "kind,name,sex,ref,group,category"),
            ("nodes[]", "name,parent,p,r,s,active,layer"),
            ("meshes[]", "blob,name"),
            ("textures[]", "blob,name,wrap,filter,aniso,bias"),
            ("materials[0]", "name,shader,queue,keywords,passesOff,renderType,props,inside"),
            ("renderers[]", "node,skinned,mesh,materials,bones,root,bounds,shadow,receive,lightProbe,reflProbe,anchor,updateOffscreen,quality,part,member,inside"),
            ("insides", "fill,section,ownInside"),
        };

        static void BM1(TestRunner t)
        {
            // The header and the body of each kind.
            var mesh = HandMesh();
            var tex = HandTexture();
            var meshBlob = mesh.ToBlob();
            var texBlob = tex.ToBlob();
            var main = HandMain(BakeA, BakeBlob.Sha256(meshBlob), BakeBlob.Sha256(texBlob));
            var mainBlob = main.ToBlob();
            var cases = new[] { ("main", mainBlob, main.ToJsonBytes(), "HNBK"), ("mesh", meshBlob, mesh.ToBody(), "HNBG"), ("texture", texBlob, tex.ToBody(), "HNBT") };
            foreach (var (what, blob, body, magic) in cases)
            {
                t.Equal(magic, Encoding.ASCII.GetString(blob, 0, 4), "BM1: the " + what + " blob's magic");
                t.Equal(1, blob[4] | blob[5] << 8, "BM1: the " + what + " blob's version (u16)");
                t.Equal(0, blob[6] | blob[7] << 8, "BM1: the " + what + " blob's flags (u16)");
                long len = 0;
                for (int i = 0; i < 8; i++) len |= (long)blob[8 + i] << (8 * i);
                t.Equal((long)body.Length, len, "BM1: the " + what + " blob's body length (u64)");
                var inflated = new MemoryStream();
                using (var z = new DeflateStream(new MemoryStream(blob, 16, blob.Length - 16), CompressionMode.Decompress)) z.CopyTo(inflated);
                t.Check(Same(body, inflated.ToArray()), "BM1: the " + what + " blob's bytes from 16 on are the raw deflate of its body");
                t.Equal("res/" + BakeBlob.Sha256(blob) + ".bin", ResStore.NameOf(blob), "BM1: the " + what + " blob's res name is its SHA-256 with bin");
            }
            t.Check(Same(mesh.ToBlob(), meshBlob) && Same(tex.ToBlob(), texBlob) && Same(HandMain(BakeA, BakeBlob.Sha256(meshBlob), BakeBlob.Sha256(texBlob)).ToBlob(), mainBlob),
                "BM1: the same content gives the same blob (content addressing)");

            // The mesh body byte for byte, written here independently of BakeMeshData.
            var tiny = new BakeMeshData { VertexCount = 3, Positions = new[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f } };
            tiny.SetUv(2, 2, new[] { 0.5f, 0.25f, 1f, 0f, 0f, 1f });
            tiny.BoneIndices = new ushort[] { 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0 };
            tiny.BoneWeights = new[] { 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0.5f, 0f, 0f, 0f };
            tiny.BindPoses = Enumerable.Range(0, 32).Select(i => (float)i).ToArray();
            tiny.SubMeshes.Add(new BakeSubMesh { Topology = BakeSubMesh.Triangles, Indices = new[] { 0, 1, 2 } });
            var want = new MemoryStream();
            var bw = new BinaryWriter(want);
            bw.Write((uint)3);
            bw.Write((byte)8);
            bw.Write(new byte[] { 0, 0, 2, 0, 0, 0, 0, 0 });
            foreach (var x in tiny.Positions) bw.Write(x);
            foreach (var x in tiny.Uv[2]) bw.Write(x);
            for (int i = 0; i < 12; i++) { bw.Write(tiny.BoneIndices[i]); bw.Write(tiny.BoneWeights[i]); }
            bw.Write((uint)2);
            foreach (var x in tiny.BindPoses) bw.Write(x);
            bw.Write((uint)1);
            bw.Write((byte)0);
            bw.Write((uint)3);
            bw.Write(0u); bw.Write(1u); bw.Write(2u);
            bw.Flush();
            t.Check(Same(want.ToArray(), tiny.ToBody()), "BM1: the mesh body: count, flags, UV dimensions, positions, UVs, weights as (u16, f32) pairs, bind poses, sub-meshes\n    " + DocIo.FirstDiff(want.ToArray(), tiny.ToBody()));
            t.Equal(want.Length, tiny.BodyLength(), "BM1: the mesh body's measured length");
            // The texture body byte for byte.
            var tt = new BakeTextureData { Width = 2, Height = 1, Format = "R8G8B8A8_UNorm", SourceFormat = "RGB24", Levels = { new byte[] { 1, 2, 3, 255, 4, 5, 6, 255 }, new byte[] { 7, 8, 9, 255 } } };
            var tw = new MemoryStream();
            var w2 = new BinaryWriter(tw);
            w2.Write(2u); w2.Write(1u); w2.Write((byte)2);
            w2.Write((ushort)14); w2.Write(Encoding.UTF8.GetBytes("R8G8B8A8_UNorm"));
            w2.Write((ushort)5); w2.Write(Encoding.UTF8.GetBytes("RGB24"));
            w2.Write(8u); w2.Write(tt.Levels[0]);
            w2.Write(4u); w2.Write(tt.Levels[1]);
            w2.Flush();
            t.Check(Same(tw.ToArray(), tt.ToBody()), "BM1: the texture body: size, levels, format names, each level's length and bytes, largest first\n    " + DocIo.FirstDiff(tw.ToArray(), tt.ToBody()));
            t.Check(Same(BakeTextureData.Encode(2, 1, "R8G8B8A8_UNorm", "RGB24", tt.Levels), tt.ToBlob()), "BM1: Encode from levels gives the same blob");

            // Round trips of the data.
            var mb = BakeMeshData.FromBlob(meshBlob);
            t.Check(mb.VertexCount == 4 && mb.Positions.SequenceEqual(mesh.Positions) && mb.Normals.SequenceEqual(mesh.Normals) && mb.Tangents.SequenceEqual(mesh.Tangents)
                && Enumerable.Range(0, 8).All(c => mb.UvDims[c] == mesh.UvDims[c] && (mb.Uv[c] == null ? mesh.Uv[c] == null : mb.Uv[c].SequenceEqual(mesh.Uv[c])))
                && mb.Colors.SequenceEqual(mesh.Colors) && mb.BoneIndices.SequenceEqual(mesh.BoneIndices) && mb.BoneWeights.SequenceEqual(mesh.BoneWeights)
                && mb.BindPoses.SequenceEqual(mesh.BindPoses) && mb.SubMeshes.Count == 2 && mb.SubMeshes[0].Indices.SequenceEqual(mesh.SubMeshes[0].Indices)
                && mb.SubMeshes[1].Topology == BakeSubMesh.Lines, "BM1: a mesh reads back with every channel equal");
            t.Check(Same(mb.ToBlob(), meshBlob), "BM1: and writes the same blob");
            var tb = BakeTextureData.FromBlob(texBlob);
            t.Check(tb.Width == 4 && tb.Height == 2 && tb.Format == "R8G8B8A8_SRGB" && tb.SourceFormat == "DXT5" && tb.Levels.Count == 3 && Enumerable.Range(0, 3).All(l => tb.Levels[l].SequenceEqual(tex.Levels[l])),
                "BM1: a texture reads back with every level equal");
            var none = new BakeMeshData { VertexCount = 0 };
            t.Check(BakeMeshData.FromBlob(none.ToBlob()).VertexCount == 0, "BM1: an empty mesh round-trips");

            // The main: every member present, in order, read back byte-stable.
            var json = main.ToJsonBytes();
            var o = DocReader.Parse(json, "main");
            int present = 0, expected = 0;
            foreach (var (what, members) in MainMembers)
            {
                JObject at;
                switch (what)
                {
                    case "the main": at = o; break;
                    case "source": at = (JObject)o["source"]; break;
                    case "nodes[]": at = (JObject)o["nodes"][0]; break;
                    case "meshes[]": at = (JObject)o["meshes"][0]; break;
                    case "textures[]": at = (JObject)o["textures"][0]; break;
                    case "materials[0]": at = (JObject)o["materials"][0]; break;
                    case "renderers[]": at = (JObject)o["renderers"][0]; break;
                    default: at = (JObject)o["insides"]; break;
                }
                t.Equal(members, Names(at), "BM1: " + what + " holds every member, in order");
                expected += members.Split(',').Length;
                present += members.Split(',').Count(n => at.Property(n) != null);
            }
            t.Equal("_BaseColor", Names(o["materials"][1]["props"]), "BM1: a material's props are by property name");
            t.Equal("_BumpMap,_Color,_Glossiness,_MainTex,_Mode,_Offset", Names(o["materials"][0]["props"]), "BM1: in ordinal order");
            t.Equal("t,st", Names(o["materials"][0]["props"]["_MainTex"]), "BM1: a texture value: t and st");
            t.Equal("c|f|i|v", string.Join("|", new[] { "_Color", "_Glossiness", "_Mode", "_Offset" }.Select(n => Names(o["materials"][0]["props"][n]))), "BM1: a colour, a float, an integer, a vector: one member each");
            t.Info("BM1: members present in the hand-made main: " + present + " of " + expected + ", 5 value types");
            t.Check(present == expected && expected >= 60, "BM1: every member present in the hand-made main (" + present + ")");
            var f = new FindingList();
            var back = BakeMain.Parse(json, f);
            t.Check(Same(json, back.ToJsonBytes()) && f.Count == 0, "BM1: the main: write -> read -> write byte-stable, no finding\n    " + DocIo.FirstDiff(json, back.ToJsonBytes()));
            t.Check(Same(json, BakeMain.FromBlob(mainBlob).ToJsonBytes()), "BM1: and from its blob");
            t.Check(Same(json, back.Clone().ToJsonBytes()), "BM1: Clone gives the same bytes");
            t.Equal(main.DataBlobs().Count, 2, "BM1: the main names two data blobs");
            var block = main.ToItemBake(BakeBlob.Sha256(mainBlob), 73100009);
            t.Check(block.Id == BakeA && block.Main == ResStore.NameOf(mainBlob) && block.Data.SequenceEqual(new[] { ResStore.NameOf(meshBlob), ResStore.NameOf(texBlob) }) && block.No == 73100009,
                "BM1: the item block of a main: its id, its main and data as res names in the main's order, the row");

            // Canonical writing: the exact text of a small main, and every main is what the canonical writer gives its own JSON.
            var small = new BakeMain { Id = Id(1), Created = Clock };
            small.Nodes.Add(new BakeMainNode { Name = "root" });
            const string smallText =
                "{\n  \"format\": \"hs2next.bake\",\n  \"version\": 1,\n  \"id\": \"00000000000000000000000000000001\",\n  \"kind\": \"chara\",\n  \"created\": \"2026-11-08T00:00:00Z\",\n" +
                "  \"source\": {\n    \"kind\": \"chara\",\n    \"name\": \"\"\n  },\n  \"origin\": \"chara\",\n" +
                "  \"nodes\": [\n    {\n      \"name\": \"root\",\n      \"parent\": -1,\n      \"p\": [0.0, 0.0, 0.0],\n      \"r\": [0.0, 0.0, 0.0, 1.0],\n      \"s\": [1.0, 1.0, 1.0],\n      \"active\": true,\n      \"layer\": 0\n    }\n  ],\n" +
                "  \"fk\": [],\n  \"meshes\": [],\n  \"textures\": [],\n  \"materials\": [],\n  \"renderers\": [],\n" +
                "  \"insides\": {\n    \"fill\": false,\n    \"section\": [0.35, 0.03, 0.03],\n    \"ownInside\": true\n  }\n}\n";
            t.Equal(smallText, Text(small.ToJsonBytes()), "BM1: the exact canonical text of a small main");
            t.Check(Same(json, CanonicalJsonWriter.ToBytes(DocReader.Parse(json, "main"))), "BM1: the hand-made main is canonical: floats as R, number arrays on one line, LF, a trailing LF");
            t.Check(Text(json).Contains("\"bias\": -0.5") && Text(json).Contains("\"st\": [2.0, 2.0, 0.5, 0.25]") && !Text(json).Contains("\r") && json[json.Length - 1] == (byte)'\n', "BM1: a float, a vector");

            // Unknown members kept at every level of the main.
            var withUnknown = (JObject)o.DeepClone();
            var places = new List<JObject> { withUnknown, (JObject)withUnknown["source"], (JObject)withUnknown["nodes"][1], (JObject)withUnknown["meshes"][0], (JObject)withUnknown["textures"][0],
                (JObject)withUnknown["materials"][0], (JObject)withUnknown["materials"][0]["props"]["_MainTex"], (JObject)withUnknown["renderers"][1], (JObject)withUnknown["insides"] };
            for (int i = 0; i < places.Count; i++) places[i].Add("later" + i, new JObject { ["n"] = i + 0.5 });
            // in its ordinal place among the values (props are written by name in ordinal order)
            ((JObject)withUnknown["materials"][0]["props"]).Property("_Glossiness").AddBeforeSelf(new JProperty("_Future", new JObject { ["m"] = new JArray(1, 2, 3) }));
            var uBytes = CanonicalJsonWriter.ToBytes(withUnknown);
            var fu = new FindingList();
            var u = BakeMain.Parse(uBytes, fu);
            var uBack = u.ToJsonBytes();
            t.Check(Same(uBytes, uBack), "BM1: unknown members at " + places.Count + " places and a value of an unknown type are kept and written back in place\n    " + DocIo.FirstDiff(uBytes, uBack));
            t.Check(u.Materials[0].Props["_Future"].Type == BakePropType.Unknown && fu.Count == 0, "BM1: a value of an unknown type reads as Unknown, silently");
            var e = DocReader.Parse(json, "main");
            e["kind"] = "later";
            e["renderers"][0]["inside"] = "later";
            var fe = new FindingList();
            var ue = BakeMain.Parse(CanonicalJsonWriter.ToBytes(e), fe);
            t.Check(ue.Kind == "later" && ue.Renderers[0].Inside == "later" && fe.CountOf("json.unknownEnum") == 2 && Same(CanonicalJsonWriter.ToBytes(e), ue.ToJsonBytes()),
                "BM1: an unknown kind and inside are kept, reported json.unknownEnum (" + fe + ")");
        }

        // ---------------- BF1 ----------------

        static IEnumerable<(string what, string path, SaveOptions options, int revision)> FreeScenes(string root)
        {
            yield return ("the Timeline fixture", DocTests_Timeline.R4Path(root), DocTests_Timeline.R4Options(), 4);
            yield return ("the advanced-pose fixture", DocTests_AdvancedPose.PosePath(root), DocTests_AdvancedPose.PoseOptions(), 4);
            yield return ("the material-edit fixture", DocTests_MaterialEdit.MaterialsPath(root), DocTests_MaterialEdit.MaterialsOptions(), 4);
            yield return ("the constraints fixture", DocTests_Constraints.FixturePath(root), DocTests_Constraints.FixtureOptions(), 4);
            yield return ("the wet fixture", DocTests_Wet.FixturePath(root), DocTests_Wet.FixtureOptions(), 5);
            yield return ("the ragdoll fixture", DocTests_Ragdoll.FixturePath(root), DocTests_Ragdoll.FixtureOptions(), 5);
            yield return ("the v0.2 scene", Path.Combine(FixtureWriter.V02Dir(root), "scene.hs2nscene"), null, 1);
            yield return ("the plugin sample's scene", Path.Combine(root ?? "", "..", "Plugins", "fixtures", "sequence_scene.hs2nscene"), null, 1);
        }

        static void BF1(TestRunner t)
        {
            int files = 0;
            foreach (var (what, path, options, revision) in FreeScenes(t.FixturesDir))
            {
                if (!File.Exists(path)) { t.Check(false, "BF1: " + what + " present at " + path); continue; }
                var file = File.ReadAllBytes(path);
                var l = SceneContainer.Load(file, LOpts());
                t.Check(l.Ok && !l.NewerFormat && !l.Findings.Has(BakeFindings.Limit) && !l.Findings.Has(BakeFindings.CutLimit) && !l.Findings.Has(BakeFindings.CutRange), "BF1: " + what + " loads (" + l + ")");
                if (!l.Ok) continue;
                files++;
                t.Check(l.Value.Scene.Nodes.Select(n => n.State).OfType<ItemNodeState>().All(i => i.Bake == null && i.Cut == null), "BF1: " + what + ": its items read with no bake and no cut");
                t.Check(!SceneText(file).Contains("\"bake\"") && !SceneText(file).Contains("\"cut\""), "BF1: " + what + ": the file has no member");
                var re = l.Value.ToBytes(options ?? new SaveOptions { Clock = () => InfraFixtures.FixedTime, Thumbnail = ThumbnailMode.Keep, Registry = new ExtRegistry(), AppVersion = (string)Entry(file, "manifest.json")["app"]["version"] });
                t.Equal(revision, FormatOf(re), "BF1: " + what + " stays revision " + revision);
                var names = ContainerReader.Read(file).Entries.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
                t.Equal(string.Join(",", names), string.Join(",", ContainerReader.Read(re).Entries.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal)), "BF1: " + what + ": the same entries");
                foreach (var name in names)
                {
                    if (name == "manifest.json") continue;
                    t.Check(Same(EntryBytes(file, name), EntryBytes(re, name)), "BF1: " + what + ": " + name + " byte-equal\n    " + DocIo.FirstDiff(EntryBytes(file, name) ?? new byte[0], EntryBytes(re, name) ?? new byte[0]));
                }
                if (options != null) t.Check(Same(file, re), "BF1: " + what + ": the whole file byte-equal");
            }
            t.Info("BF1: committed files compared here: " + files + " (the corpus of real files runs on both save layers)");
            t.Check(files >= 8, "BF1: every committed revision was compared (" + files + ")");

            var golden = DocTests_T1Golden.Golden("scene.json");
            var doc = DocIo.Scene(golden);
            t.Check(doc.Nodes.Select(n => n.State).OfType<ItemNodeState>().Any() && DocIo.Same(golden, DocIo.Bytes(doc)), "BF1: the golden scene, with items, is written byte for byte");

            // Written by this writer: items never given a block, and given none explicitly.
            var s = InfraFixtures.SmallScene(1, 2);
            var b0 = s.ToBytes(Opts());
            t.Check(FormatOf(b0) == 1 && !SceneText(b0).Contains("\"bake\""), "BF1: a scene with items and no block has no member, format 1");
            foreach (var n in s.Scene.Nodes) if (n.State is ItemNodeState it) { it.Bake = null; it.Cut = null; }
            t.Check(Same(b0, s.ToBytes(Opts())), "BF1: null blocks change no byte");
            // A block removed again gives the bytes before, its blobs dropped with it.
            var h = MakeBake(BakeA, 3);
            var item = (ItemNodeState)s.Scene.Nodes.First(n => n.State is ItemNodeState).State;
            item.Bake = h.Block;
            h.AddTo(s.Res);
            var b1 = s.ToBytes(Opts());
            t.Check(FormatOf(b1) == 5 && ContainerReader.Read(b1).Entries.Count(e => e.Name.EndsWith(".bin", StringComparison.Ordinal)) == 3, "BF1: with a block: format 5 and its three blobs");
            item.Bake = null;
            t.Check(Same(b0, s.ToBytes(Opts())), "BF1: the block cleared: the bytes before, the blobs not written");
        }

        // ---------------- BF2: types, order, presence, unknown members ----------------

        static SceneContainer TypeScene()
        {
            var a = MakeBake(BakeA, 1, 73100001);
            var b = MakeBake(BakeB, 2);
            var s = InfraFixtures.SmallScene(0, 0);
            var item = new ItemNodeState { Ref = "hs2next.bake#73100001", Group = 731406, Bake = a.Block };
            item.MaterialOverrides.Add(new MeFloat { Material = "mat_a", Property = "Metallic", Value = 0.1f, Original = 0.25f });
            s.Scene.Nodes.Add(new SceneNode(ItemA, item) { Order = 0 });
            s.Scene.Nodes.Add(new SceneNode(PieceB, new ItemNodeState { Ref = "hs2next.bake#73100002", Bake = b.Block, Cut = HandCutPosed(a.Block) }) { Order = 1 });
            a.AddTo(s.Res);
            b.AddTo(s.Res);
            return s;
        }

        /// <summary><see cref="HandCut"/> re-cut after the piece was posed anew: a third step that carries its own pose (spec 8.2 "Per-step pose").</summary>
        public static ItemCut HandCutPosed(ItemBake source)
        {
            var c = HandCut(source);
            c.Steps.Add(new CutStep
            {
                Shape = CutShapes.Plane, Pos = new Vec3(0f, 4f, 0.5f), Keep = CutShapes.Back,
                Pose = new SortedDictionary<string, Vec3>(StringComparer.Ordinal) { ["cf_J_Hips"] = new Vec3(45f, 0f, -12.5f), ["cf_J_Spine01"] = new Vec3(0.1f, 0.2f, 0.3f) },
            });
            return c;
        }

        /// <summary>Wrong values for a token of this type (each must fail the load).</summary>
        static IEnumerable<JToken> WrongFor(JToken v)
        {
            switch (v.Type)
            {
                case JTokenType.String: return new JToken[] { 1, true, new JArray(), new JObject(), JValue.CreateNull() };
                case JTokenType.Integer: return new JToken[] { "x", true, new JArray(), new JObject(), JValue.CreateNull(), 1.5 };
                case JTokenType.Float: return new JToken[] { "x", true, new JArray(), new JObject(), JValue.CreateNull() };
                case JTokenType.Boolean: return new JToken[] { 1, "x", new JArray(), new JObject(), JValue.CreateNull() };
                case JTokenType.Array: return new JToken[] { "x", 1, new JObject(), JValue.CreateNull() };
                case JTokenType.Object: return new JToken[] { "x", 1, new JArray(), JValue.CreateNull() };
                default: return new JToken[0];
            }
        }

        /// <summary>Every property and array element under <paramref name="root"/>, as paths relative to it.</summary>
        static IEnumerable<string> Paths(JToken root, string at)
        {
            if (root is JObject o)
                foreach (var p in o.Properties())
                {
                    string q = at + "." + p.Name;
                    yield return q;
                    foreach (var x in Paths(p.Value, q)) yield return x;
                }
            else if (root is JArray a)
                for (int i = 0; i < a.Count; i++)
                {
                    string q = at + "[" + i + "]";
                    yield return q;
                    foreach (var x in Paths(a[i], q)) yield return x;
                }
        }

        static JToken At(JObject sceneJson, string path) => sceneJson.SelectToken(path.TrimStart('.'));

        static void BF2(TestRunner t)
        {
            var s = TypeScene();
            var file = s.ToBytes(Opts());
            var l0 = SceneContainer.Load(file, LOpts());
            t.Check(l0.Ok && l0.Findings.Count == 0, "BF2: the scene loads with no finding (" + l0 + ")");
            var sj = Entry(file, "scene.json");

            // Order and presence.
            t.Equal("ref,group,category,anime,colors,alpha,emission,lightCancel,panel,fk,dynamicBones,options,materialOverrides,bake", Names(sj["nodes"][0]["item"]), "BF2: bake after materialOverrides");
            t.Equal("ref,group,category,anime,colors,alpha,emission,lightCancel,panel,fk,dynamicBones,options,bake,cut", Names(sj["nodes"][1]["item"]), "BF2: bake, cut after options when there is no material edit");
            t.Equal("id,main,data,no", Names(sj["nodes"][0]["item"]["bake"]), "BF2: the bake block's members");
            t.Equal("id,main,data", Names(sj["nodes"][1]["item"]["bake"]), "BF2: no is written only when set");
            t.Equal("source,pose,steps,section", Names(sj["nodes"][1]["item"]["cut"]), "BF2: the cut block's members");
            t.Equal("id,main,data", Names(sj["nodes"][1]["item"]["cut"]["source"]), "BF2: the source's members");
            t.Equal("shape,pos,rot,size,keep,ragged,clothesOnly", Names(sj["nodes"][1]["item"]["cut"]["steps"][0]), "BF2: a step's members");
            t.Equal("shape,pos,rot,size,keep,clothesOnly", Names(sj["nodes"][1]["item"]["cut"]["steps"][1]), "BF2: ragged is written only when set");
            t.Equal("shape,pos,rot,size,keep,clothesOnly,pose", Names(sj["nodes"][1]["item"]["cut"]["steps"][2]), "BF2: a step's pose after clothesOnly, written only when set (steps 0 and 1 have none)");
            t.Equal("cf_J_Hips,cf_J_Spine01", Names(sj["nodes"][1]["item"]["cut"]["steps"][2]["pose"]), "BF2: a step's pose by bone name in ordinal order");
            t.Check(SceneText(file).Contains("\"cf_J_Hips\": [45.0, 0.0, -12.5]"), "BF2: as local Euler degrees on one line");
            var stepPose = TypeScene();
            ItemOf(stepPose, PieceB).Cut.Steps[2].Pose = new SortedDictionary<string, Vec3>(StringComparer.Ordinal);
            t.Equal("{}", Entry(stepPose.ToBytes(Opts()), "scene.json")["nodes"][1]["item"]["cut"]["steps"][2]["pose"].ToString(Newtonsoft.Json.Formatting.None), "BF2: an empty step pose that was set is written as {}");
            ItemOf(stepPose, PieceB).Cut.Steps[2].Pose = null;
            t.Equal("shape,pos,rot,size,keep,clothesOnly", Names(Entry(stepPose.ToBytes(Opts()), "scene.json")["nodes"][1]["item"]["cut"]["steps"][2]), "BF2: a step pose of null is not written");
            t.Equal("amp,tooth,random,seed", Names(sj["nodes"][1]["item"]["cut"]["steps"][0]["ragged"]), "BF2: ragged's members");
            t.Equal("color,ownInside", Names(sj["nodes"][1]["item"]["cut"]["section"]), "BF2: the section's members");
            var piece = ItemOf(s, PieceB);
            piece.Cut.Pose = null;
            var noPose = Entry(s.ToBytes(Opts()), "scene.json");
            t.Equal("source,steps,section", Names(noPose["nodes"][1]["item"]["cut"]), "BF2: pose is written only when set");
            piece.Cut.Pose = new SortedDictionary<string, Vec3>(StringComparer.Ordinal);
            t.Equal("{}", Entry(s.ToBytes(Opts()), "scene.json")["nodes"][1]["item"]["cut"]["pose"].ToString(Newtonsoft.Json.Formatting.None), "BF2: an empty pose that was set is written as {}");
            piece.Cut = null;
            var bakeOnly = s.ToBytes(Opts());
            t.Check(FormatOf(bakeOnly) == 5 && !Names(Entry(bakeOnly, "scene.json")["nodes"][1]["item"]).Contains("cut"), "BF2: a bake alone: revision 5, no cut member");
            var cutOnly = TypeScene();
            ItemOf(cutOnly, ItemA).Bake = null;
            ItemOf(cutOnly, ItemA).MaterialOverrides.Clear();
            ItemOf(cutOnly, PieceB).Bake = null;
            var cb = cutOnly.ToBytes(Opts());
            t.Check(FormatOf(cb) == 5 && Names(Entry(cb, "scene.json")["nodes"][1]["item"]).EndsWith("options,cut"), "BF2: a cut alone: revision 5, cut after options");

            // Types: every null and every wrong type of every known member fails the load; required members missing fail.
            int mutations = 0, caught = 0, stepPoseMutations = 0;
            var misses = new List<string>();
            void Try(string what, Action<JObject> edit)
            {
                mutations++;
                var bad = InfraFixtures.Repack(file, m => InfraFixtures.EditJson(m, "scene.json", edit), "scene.json");
                var l = SceneContainer.Load(bad, LOpts());
                if (!l.Ok && l.Error != null && l.Error.Code == SaveErrorCode.BadDocument) caught++;
                else misses.Add(what + " (" + (l.Ok ? "loaded" : l.Error.Code.ToString()) + ")");
            }
            foreach (var root in new[] { "nodes[0].item.bake", "nodes[1].item.bake", "nodes[1].item.cut" })
            {
                foreach (var p in Paths(At(sj, root), root).ToList())
                {
                    var v = At(sj, p);
                    foreach (var wrong in WrongFor(v))
                    {
                        var w = wrong;
                        if (p.Contains(".steps[2].pose")) stepPoseMutations++;
                        Try(p + " = " + w.ToString(Newtonsoft.Json.Formatting.None), j => At(j, p).Replace(w.DeepClone()));
                    }
                }
            }
            foreach (var (p, value) in new[] { ("nodes[0].item.bake.id", "not-an-id"), ("nodes[0].item.bake.main", "res/abc.bin"), ("nodes[0].item.bake.data[0]", "x.png"),
                ("nodes[1].item.cut.source.id", new string('Z', 32)), ("nodes[1].item.cut.source.main", "res/" + new string('a', 64) + ".gif") })
                Try(p + " = \"" + value + "\"", j => At(j, p).Replace(value));
            foreach (var p in new[] { "nodes[0].item.bake.id", "nodes[0].item.bake.main", "nodes[1].item.cut.source", "nodes[1].item.cut.source.id", "nodes[1].item.cut.source.main",
                "nodes[1].item.cut.steps[0].shape", "nodes[1].item.cut.steps[1].keep" })
                Try("no " + p, j => At(j, p).Parent.Remove());
            Try("bake = null", j => At(j, "nodes[0].item.bake").Replace(JValue.CreateNull()));
            Try("cut = []", j => At(j, "nodes[1].item.cut").Replace(new JArray()));
            t.Info("BF2: " + mutations + " mutations of the two blocks, " + caught + " caught" + (misses.Count > 0 ? "; missed: " + string.Join("; ", misses.Take(10)) : ""));
            t.Check(caught == mutations, "BF2: every mutation is caught (" + caught + " of " + mutations + ")");
            t.Check(mutations >= 100, "BF2: the list holds at least 100 mutations (" + mutations + ")");
            t.Info("BF2: of them on a step's pose: " + stepPoseMutations);
            t.Check(stepPoseMutations >= 40, "BF2: the list holds the step pose's null and wrong types, of the map, of each bone and of each number (" + stepPoseMutations + ", bar 40)");

            // Unknown members kept at every level of the blocks and in the section after them.
            var withUnknown = InfraFixtures.Repack(file, m => InfraFixtures.EditJson(m, "scene.json", j =>
            {
                int k = 0;
                foreach (var p in new[] { "nodes[0].item.bake", "nodes[1].item.cut", "nodes[1].item.cut.source", "nodes[1].item.cut.steps[0]", "nodes[1].item.cut.steps[0].ragged", "nodes[1].item.cut.section", "nodes[1].item" })
                    ((JObject)At(j, p)).Add("later" + k++, new JObject { ["a"] = 0.1, ["b"] = new JArray("x", 2) });
            }), "scene.json");
            var lu = SceneContainer.Load(withUnknown, LOpts());
            t.Check(lu.Ok && lu.Findings.Count == 0, "BF2: unknown members read silently (" + lu + ")");
            if (lu.Ok)
            {
                var again = lu.Value.ToBytes(Opts());
                for (int i = 0; i < 2; i++)
                {
                    string want = CanonicalJsonWriter.ToText(Entry(withUnknown, "scene.json")["nodes"][i]["item"]), got = CanonicalJsonWriter.ToText(Entry(again, "scene.json")["nodes"][i]["item"]);
                    t.Equal(want, got, "BF2: the item section of node " + i + ": unknown members kept and written back where they were");
                }
                t.Check(Same(EntryBytes(again, "scene.json"), EntryBytes(SceneContainer.Load(again, LOpts()).Value.ToBytes(Opts()), "scene.json")), "BF2: and again byte-stable");
                t.Equal("ref,group,category,anime,colors,alpha,emission,lightCancel,panel,fk,dynamicBones,options,bake,cut,later6", Names(Entry(again, "scene.json")["nodes"][1]["item"]), "BF2: the section's own unknown member after cut");
            }
        }

        // ---------------- BF3: blobs ----------------

        static void Refused(TestRunner t, Action a, string what, List<string> into = null)
        {
            try
            {
                a();
                t.Check(false, "BF3: " + what + ": refused");
            }
            catch (BakeFormatException e)
            {
                t.Check(e.Code == BakeFindings.BlobCorrupt, "BF3: " + what + ": refused with bake.blobCorrupt");
                into?.Add(what + ": " + e.Message);
            }
            catch (Exception e)
            {
                t.Check(false, "BF3: " + what + ": refused with bake.blobCorrupt, not " + e.GetType().Name + ": " + e.Message);
            }
        }

        static byte[] With(byte[] blob, int at, byte value)
        {
            var b = (byte[])blob.Clone();
            b[at] = value;
            return b;
        }

        static byte[] WithLength(byte[] blob, ulong length)
        {
            var b = (byte[])blob.Clone();
            for (int i = 0; i < 8; i++) b[8 + i] = (byte)(length >> (8 * i));
            return b;
        }

        static void BF3(TestRunner t)
        {
            var mesh = HandMesh(5);
            var tex = HandTexture(5);
            var blobs = new List<(string kind, byte[] blob, Func<byte[], object> read)>
            {
                ("main", HandMain(BakeA, BakeBlob.Sha256(mesh.ToBlob()), BakeBlob.Sha256(tex.ToBlob())).ToBlob(), b => BakeMain.FromBlob(b)),
                ("mesh", mesh.ToBlob(), b => BakeMeshData.FromBlob(b)),
                ("texture", tex.ToBlob(), b => BakeTextureData.FromBlob(b)),
            };
            t.Info("BF3: blobs of the three kinds: " + string.Join(", ", blobs.Select(b => b.kind + " " + b.blob.Length + " bytes")));
            var messages = new List<string>();
            foreach (var (kind, blob, read) in blobs)
            {
                t.Check(read(blob) != null, "BF3: the " + kind + " blob reads");
                t.Check(ResStore.IsBakeBlob(blob) && BakeBlob.KindOf(blob).ToString().ToLowerInvariant() == kind, "BF3: the " + kind + " blob's kind by its magic");
                for (int i = 0; i < 4; i++) Refused(t, () => read(With(blob, i, (byte)(blob[i] ^ 0x20))), kind + ": magic byte " + i + " changed", i == 0 ? messages : null);
                Refused(t, () => read(With(blob, 4, 2)), kind + ": version 2", messages);
                Refused(t, () => read(With(blob, 4, 0)), kind + ": version 0");
                Refused(t, () => read(With(blob, 5, 1)), kind + ": version 257");
                Refused(t, () => read(With(blob, 6, 1)), kind + ": flag bit 0", messages);
                Refused(t, () => read(With(blob, 7, 0x80)), kind + ": flag bit 15");
                long len = 0;
                for (int i = 0; i < 8; i++) len |= (long)blob[8 + i] << (8 * i);
                Refused(t, () => read(WithLength(blob, (ulong)len + 1)), kind + ": length + 1", messages);
                Refused(t, () => read(WithLength(blob, (ulong)len - 1)), kind + ": length - 1", messages);
                Refused(t, () => read(WithLength(blob, 1UL << 40)), kind + ": a length over 1 GiB");
                Refused(t, () => read(blob.Take(15).ToArray()), kind + ": shorter than its header");
                Refused(t, () => read(blob.Take(blob.Length / 2).ToArray()), kind + ": the deflated body cut in half", messages);
                foreach (var (other, oblob, _) in blobs)
                    if (other != kind) Refused(t, () => read(oblob), kind + " reader on a " + other + " blob");
            }
            Refused(t, () => BakeBlob.Decode(Encoding.ASCII.GetBytes("\x89PNG\r\n\x1a\n........"), BakeBlobKind.Texture), "a PNG");

            // Bodies that inflate to their length but do not hold what they say.
            Refused(t, () => BakeMeshData.FromBlob(BakeBlob.Encode(BakeBlobKind.Mesh, With(mesh.ToBody(), 4, 0x10))), "mesh: an unknown channel flag");
            Refused(t, () => BakeMeshData.FromBlob(BakeBlob.Encode(BakeBlobKind.Mesh, With(mesh.ToBody(), 5, 5))), "mesh: a UV dimension of 5");
            var badIndex = HandMesh(5);
            badIndex.SubMeshes[0].Indices[2] = 4;
            Refused(t, () => BakeMeshData.FromBody(Force(badIndex)), "mesh: an index past the vertices");
            var badTopology = HandMesh(5);
            badTopology.SubMeshes[1].Topology = 1;
            Refused(t, () => BakeMeshData.FromBody(Force(badTopology)), "mesh: topology 1");
            var badBone = HandMesh(5);
            badBone.BoneIndices[0] = 9;
            Refused(t, () => BakeMeshData.FromBody(Force(badBone)), "mesh: a weight on a bone past the bind poses");
            Refused(t, () => BakeMeshData.FromBody(mesh.ToBody().Concat(new byte[] { 0 }).ToArray()), "mesh: a byte left over");
            Refused(t, () => BakeMeshData.FromBody(mesh.ToBody().Take(mesh.ToBody().Length - 1).ToArray()), "mesh: a byte short");
            var tb = tex.ToBody();
            Refused(t, () => BakeTextureData.FromBody(With(tb, 8, 0)), "texture: no level");
            Refused(t, () => BakeTextureData.FromBody(With(tb, 8, 9)), "texture: more levels than the body");
            var badLevel = HandTexture(5);
            badLevel.Levels[1] = new byte[7];
            Refused(t, () => BakeTextureData.FromBody(ForceTexture(badLevel)), "texture: a level of the wrong length for its format");
            var tooMany = HandTexture(5);
            tooMany.Levels.Add(new byte[4]);
            Refused(t, () => BakeTextureData.FromBody(ForceTexture(tooMany)), "texture: more levels than the chain");
            Refused(t, () => BakeMain.FromBlob(BakeBlob.Encode(BakeBlobKind.Main, Encoding.UTF8.GetBytes("{ not json"))), "main: not JSON");
            var j = DocReader.Parse(HandMain(BakeA, new string('a', 64), new string('b', 64)).ToJsonBytes(), "main");
            foreach (var (what, edit) in new (string, Action<JObject>)[]
            {
                ("main: another format", x => x["format"] = "hs2next.scene"),
                ("main: version 2", x => x["version"] = 2),
                ("main: a renderer's mesh past the meshes", x => x["renderers"][0]["mesh"] = 5),
                ("main: a renderer's node past the nodes", x => x["renderers"][1]["node"] = 3),
                ("main: a bone past the nodes", x => x["renderers"][0]["bones"][1] = 9),
                ("main: a material past the materials", x => x["renderers"][0]["materials"][0] = 2),
                ("main: a texture value past the textures", x => x["materials"][0]["props"]["_MainTex"]["t"] = 1),
                ("main: a parent after its child", x => x["nodes"][1]["parent"] = 2),
                ("main: a root with a parent", x => x["nodes"][0]["parent"] = 0),
                ("main: a blob that is no SHA-256", x => x["meshes"][0]["blob"] = "abc"),
                ("main: a value with two types", x => x["materials"][0]["props"]["_Mode"]["f"] = 1.0),
                ("main: no id", x => x.Remove("id")),
                ("main: a wrong type", x => x["fk"] = 3),
            })
            {
                var copy = (JObject)j.DeepClone();
                edit(copy);
                Refused(t, () => BakeMain.FromBlob(BakeBlob.Encode(BakeBlobKind.Main, CanonicalJsonWriter.ToBytes(copy))), what, messages);
            }
            foreach (var m in messages.Take(6)) t.Info("BF3: " + m);

            // The writers refuse what the readers refuse.
            t.Throws<ArgumentException>(() => badIndex.ToBlob(), "BF3: a mesh with an index past its vertices is not written");
            t.Throws<ArgumentException>(() => badLevel.ToBlob(), "BF3: a texture with a level of the wrong length is not written");
            var badMain = HandMain(BakeA, "abc", new string('b', 64));
            t.Throws<SaveFormatException>(() => badMain.ToBlob(), "BF3: a main naming a blob that is no SHA-256 is not written");
        }

        /// <summary>The body of a mesh the writer would refuse, written field by field for the reader's checks.</summary>
        static byte[] Force(BakeMeshData m)
        {
            var good = HandMesh(5);
            var body = good.ToBody();
            // The fields that differ are the last sub-mesh's topology, an index or a bone: patch them in place.
            if (m.SubMeshes[1].Topology != good.SubMeshes[1].Topology)
            {
                long at = body.Length - (1 + 4 + 4 * m.SubMeshes[1].Indices.Length);
                body[at] = m.SubMeshes[1].Topology;
            }
            if (m.SubMeshes[0].Indices[2] != good.SubMeshes[0].Indices[2])
            {
                long at = body.Length - (1 + 4 + 4 * 2) - 4 * 4;   // the third index of the first sub-mesh
                BitConverter.GetBytes(m.SubMeshes[0].Indices[2]).CopyTo(body, at);
            }
            if (m.BoneIndices[0] != good.BoneIndices[0])
            {
                long weightsAt = 4 + 1 + 8 + 4L * (3 + 3 + 4 + 2 + 3 + 1) * 4 + 16;
                BitConverter.GetBytes(m.BoneIndices[0]).CopyTo(body, weightsAt);
            }
            return body;
        }

        /// <summary>The body of a texture the writer would refuse.</summary>
        static byte[] ForceTexture(BakeTextureData x)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write((uint)x.Width); w.Write((uint)x.Height); w.Write((byte)x.Levels.Count);
            var f = Encoding.UTF8.GetBytes(x.Format); w.Write((ushort)f.Length); w.Write(f);
            var sf = Encoding.UTF8.GetBytes(x.SourceFormat); w.Write((ushort)sf.Length); w.Write(sf);
            foreach (var l in x.Levels) { w.Write((uint)l.Length); w.Write(l); }
            w.Flush();
            return ms.ToArray();
        }

        // ---------------- BF5: res ----------------

        static void BF5(TestRunner t)
        {
            var a = MakeBake(BakeA, 1);
            var res = new ResStore();
            foreach (var b in new[] { a.Main, a.Mesh, a.Texture })
            {
                string name = res.AddBlob(b);
                t.Check(name == ResStore.NameOf(b) && name.EndsWith(".bin", StringComparison.Ordinal) && res.TypeOf(name) == ResMediaType.Other, "BF5: AddBlob stores " + BakeBlob.KindOf(b) + " as bin under its hash");
            }
            t.Equal(res.AddBlob(a.Main), ResStore.NameOf(a.Main), "BF5: the same bytes once");
            t.Equal(3, res.Count, "BF5: three entries");
            var png = InfraFixtures.FakePng(7);
            t.Throws<ArgumentException>(() => res.AddBlob(png), "BF5: AddBlob refuses a PNG");
            t.Throws<ArgumentException>(() => res.AddBlob(new byte[] { 1, 2, 3, 4, 5 }), "BF5: and other content");
            t.Throws<ArgumentException>(() => res.Add(a.Main), "BF5: Add still refuses a blob");
            t.Check(res.Add(png).EndsWith(".png", StringComparison.Ordinal), "BF5: Add still takes a PNG");

            // The scan: bake and cut.source reach their blobs; nothing else keeps them.
            var b2 = MakeBake(BakeB, 2);
            var src = MakeBake(Id(0xb9), 9);
            var s = InfraFixtures.SmallScene(0, 0);
            s.Scene.Nodes.Add(new SceneNode(ItemA, new ItemNodeState { Ref = "hs2next.bake#1", Bake = a.Block }) { Order = 0 });
            s.Scene.Nodes.Add(new SceneNode(PieceB, new ItemNodeState { Ref = "hs2next.bake#2", Bake = b2.Block, Cut = HandCut(src.Block) }) { Order = 1 });
            foreach (var h in new[] { a, b2, src }) h.AddTo(s.Res);
            var stray = MakeBake(Id(0xba), 10);
            stray.AddTo(s.Res);
            var file = s.ToBytes(Opts());
            var entries = ContainerReader.Read(file).Entries.Select(e => e.Name).Where(n => n.StartsWith("res/", StringComparison.Ordinal)).ToList();
            var onlyCut = src.Block.Blobs();
            t.Info("BF5: blobs reachable only through cut.source: " + onlyCut.Count(n => !a.Block.Blobs().Contains(n) && !b2.Block.Blobs().Contains(n)));
            t.Check(onlyCut.Count(n => !a.Block.Blobs().Contains(n) && !b2.Block.Blobs().Contains(n)) >= 1, "BF5: a blob reachable only through cut.source exists");
            t.Check(a.Block.Blobs().Concat(b2.Block.Blobs()).Concat(onlyCut).All(entries.Contains), "BF5: the file holds every blob a bake or a cut's source names");
            t.Check(!stray.Block.Blobs().Any(entries.Contains), "BF5: and none that no node names");
            t.Equal(9, entries.Count, "BF5: nine res entries");
            var l = SceneContainer.Load(file, LOpts());
            t.Check(l.Ok && l.Findings.Count == 0 && l.Value.Res.Count == 9, "BF5: the scene loads with no finding (no res.unknownMediaType) and its nine blobs (" + l + ")");
            if (!l.Ok) return;
            ItemOf(l.Value, PieceB).Cut = null;
            var noCut = l.Value.ToBytes(Opts());
            var after = ContainerReader.Read(noCut).Entries.Select(e => e.Name).Where(n => n.StartsWith("res/", StringComparison.Ordinal)).ToList();
            t.Check(!onlyCut.Any(after.Contains) && a.Block.Blobs().Concat(b2.Block.Blobs()).All(after.Contains), "BF5: the cut removed: its source's blobs are dropped, the bakes' kept");
            ItemOf(l.Value, PieceB).Bake = null;
            ItemOf(l.Value, ItemA).Bake = null;
            var none = l.Value.ToBytes(Opts());
            t.Check(!ContainerReader.Read(none).Entries.Any(e => e.Name.StartsWith("res/", StringComparison.Ordinal)), "BF5: no node names one: none is written");
        }

        // ---------------- BF6 (here: a revision-4 registry over this reader) ----------------

        static void BF6(TestRunner t)
        {
            string path = FixturePath(t.FixturesDir);
            if (!File.Exists(path)) { t.Check(false, "BF6: fixture " + path + " present"); return; }
            var fixture = File.ReadAllBytes(path);
            var fx = ContainerReader.Read(fixture);
            // The fixture's blobs hold what this code makes; the rest of the file is what this writer writes over them.
            FixtureBakes(out var a, out var b);
            var res = fx.Entries.Where(e => e.Name.StartsWith("res/", StringComparison.Ordinal)).ToList();
            t.Equal(6, res.Count, "BF6: the fixture holds six blobs");
            var bodies = new[] { a.MainDoc.ToJsonBytes(), HandMesh(1).ToBody(), HandTexture(1).ToBody(), b.MainDoc.ToJsonBytes(), HandMesh(2).ToBody(), HandTexture(2).ToBody() };
            var kinds = new[] { BakeBlobKind.Main, BakeBlobKind.Mesh, BakeBlobKind.Texture, BakeBlobKind.Main, BakeBlobKind.Mesh, BakeBlobKind.Texture };
            var fileA = new HandBake(); var fileB = new HandBake();
            for (int i = 0; i < 6; i++)
            {
                var e = res.FirstOrDefault(x => BakeBlob.KindOf(x.Data) == kinds[i] && Same(BakeBlob.Decode(x.Data, kinds[i]), Rebind(bodies[i], kinds[i], res)));
                t.Check(e != null, "BF6: the fixture holds the " + kinds[i] + " blob of bake " + (i < 3 ? "A" : "B"));
                if (e == null) return;
                var h = i < 3 ? fileA : fileB;
                if (kinds[i] == BakeBlobKind.Main) h.Main = e.Data; else if (kinds[i] == BakeBlobKind.Mesh) h.Mesh = e.Data; else h.Texture = e.Data;
            }
            fileA.MainDoc = BakeMain.FromBlob(fileA.Main);
            fileB.MainDoc = BakeMain.FromBlob(fileB.Main);
            fileA.Block = fileA.MainDoc.ToItemBake(BakeBlob.Sha256(fileA.Main), 73100001);
            fileB.Block = fileB.MainDoc.ToItemBake(BakeBlob.Sha256(fileB.Main));
            var now = FixtureScene(fileA, fileB).ToBytes(FixtureOptions());
            foreach (var e in fx.Entries)
                t.Check(Same(e.Data, EntryBytes(now, e.Name)), "BF6: the fixture's " + e.Name + " is what this writer writes for it");
            t.Check(FormatOf(fixture) == 5 && (int)Entry(fixture, "manifest.json")["minReaderVersion"] == 1, "BF6: the fixture is revision 5, minReaderVersion 1");
            var sj = Entry(fixture, "scene.json");
            t.Equal("ref,group,category,anime,colors,alpha,emission,lightCancel,panel,fk,dynamicBones,options,bake", Names(sj["nodes"][0]["item"]), "BF6: the first item holds a bake");
            t.Equal("ref,group,category,anime,colors,alpha,emission,lightCancel,panel,fk,dynamicBones,options,bake,cut", Names(sj["nodes"][1]["item"]), "BF6: the piece a bake and a cut");
            t.Equal("ref,group,category,anime,colors,alpha,emission,lightCancel,panel,fk,dynamicBones,options", Names(sj["nodes"][2]["item"]), "BF6: the plain item none");

            var l5 = SceneContainer.Load(fixture, LOpts());
            t.Check(l5.Ok && !l5.NewerFormat && l5.Findings.Count == 0, "BF6: this reader reads the fixture with no finding (" + l5 + ")");
            if (!l5.Ok) return;
            string bakeJson = fileA.Block.ToJson(), pieceJson = fileB.Block.ToJson(), cutJson = HandCut(fileA.Block).ToJson();
            t.Equal(bakeJson, ItemOf(l5.Value, ItemA).Bake?.ToJson(), "BF6: and the bake block's values");
            t.Equal(pieceJson, ItemOf(l5.Value, PieceB).Bake?.ToJson(), "BF6: the piece's bake");
            t.Equal(cutJson, ItemOf(l5.Value, PieceB).Cut?.ToJson(), "BF6: the piece's cut");

            var l4 = SceneContainer.Load(fixture, LOpts(Registry4()));
            t.Check(l4.Ok && l4.NewerFormat && l4.Findings.Count == 1 && l4.Findings[0].Code == "manifest.newerFormat" && l4.Findings[0].Message == "format 5 is newer than 4; unknown parts are kept",
                "BF6: an S = 4 registry reads it with NewerFormat and the single finding manifest.newerFormat (" + l4 + ")");
            if (!l4.Ok) return;
            var c = l4.Value;
            var raw = DocReader.Parse(EntryBytes(fixture, "scene.json"), "scene.json");
            for (int i = 0; i < 2; i++)
            {
                var held = (ItemNodeState)c.Scene.Nodes[i].State;
                held.Bake = null;
                held.Cut = null;
                held.Unknown = new JsonLeftovers();
                held.Unknown.Add("bake", raw["nodes"][i]["item"]["bake"].DeepClone());
                if (i == 1) held.Unknown.Add("cut", raw["nodes"][i]["item"]["cut"].DeepClone());
            }
            string saved = Path.Combine(TempOf(t), "bf6_downgraded.hs2nscene");
            var sr = c.Save(saved, FixtureOptions());
            t.Check(sr.Ok && sr.Downgraded && sr.Findings.Count == 0, "BF6: saving it reports Downgraded and nothing else (" + sr.Findings + ")");
            var back = File.ReadAllBytes(saved);
            t.Equal(1, FormatOf(back), "BF6: written as its own lowest covering revision: 1");
            t.Check(Same(EntryBytes(fixture, "scene.json"), EntryBytes(back, "scene.json")), "BF6: scene.json byte-equal\n    " + DocIo.FirstDiff(EntryBytes(fixture, "scene.json") ?? new byte[0], EntryBytes(back, "scene.json") ?? new byte[0]));
            var bx = ContainerReader.Read(back);
            t.Equal(string.Join(",", fx.Entries.Select(e => e.Name).Where(n => n != "manifest.json").OrderBy(n => n, StringComparer.Ordinal)),
                string.Join(",", bx.Entries.Select(e => e.Name).Where(n => n != "manifest.json").OrderBy(n => n, StringComparer.Ordinal)), "BF6: the same entries");
            foreach (var e in fx.Entries)
                if (e.Name != "manifest.json") t.Check(Same(e.Data, bx.Get(e.Name)?.Data), "BF6: " + e.Name + " byte-equal");
            t.Check(Entry(back, "scene.json")["nodes"][1]["item"]["cut"] != null && Entry(back, "scene.json")["nodes"][0]["item"]["bake"] != null, "BF6: the members are in the output");

            var lb = SceneContainer.Load(back, LOpts());
            t.Check(lb.Ok && !lb.NewerFormat && lb.Findings.Count == 0, "BF6: this reader then reads it silently (" + lb + ")");
            if (!lb.Ok) return;
            t.Equal(cutJson, ItemOf(lb.Value, PieceB).Cut?.ToJson(), "BF6: with the same values");
            t.Check(ItemOf(lb.Value, PieceB).Unknown == null, "BF6: the members are known again");
            t.Check(FormatOf(lb.Value.ToBytes(FixtureOptions())) == 5, "BF6: and writes revision 5 again");
        }

        /// <summary>The body of a main as this code writes it, with the mesh and texture blob names the fixture's own blobs have (deflate's bytes may differ by runtime).</summary>
        static byte[] Rebind(byte[] body, BakeBlobKind kind, List<RawEntry> res)
        {
            if (kind != BakeBlobKind.Main) return body;
            var m = BakeMain.Parse(body);
            foreach (var mm in m.Meshes)
            {
                var want = HandMesh(m.Id == BakeA ? 1 : 2).ToBody();
                var e = res.FirstOrDefault(x => BakeBlob.KindOf(x.Data) == BakeBlobKind.Mesh && Same(BakeBlob.Decode(x.Data, BakeBlobKind.Mesh), want));
                if (e != null) mm.Blob = BakeBlob.Sha256(e.Data);
            }
            foreach (var mt in m.Textures)
            {
                var want = HandTexture(m.Id == BakeA ? 1 : 2).ToBody();
                var e = res.FirstOrDefault(x => BakeBlob.KindOf(x.Data) == BakeBlobKind.Texture && Same(BakeBlob.Decode(x.Data, BakeBlobKind.Texture), want));
                if (e != null) mt.Blob = BakeBlob.Sha256(e.Data);
            }
            return m.ToJsonBytes();
        }

        // ---------------- BF7 ----------------

        static void BF7(TestRunner t)
        {
            var cases = new List<(string what, Action<SceneContainer> shape)>
            {
                ("a bake and a piece", s => { }),
                ("a piece without a pose and a store row", s => { ItemOf(s, PieceB).Cut.Pose = null; ItemOf(s, PieceB).Bake.No = 73100099; }),
                ("a chain of steps, ragged out of range, unknown members", s =>
                {
                    var c = ItemOf(s, PieceB).Cut;
                    c.Steps.Add(new CutStep { Shape = CutShapes.Tear, Keep = CutShapes.Open, Ragged = new CutRagged { Amp = 0.9f, Tooth = 0.01f, Random = 2f, Seed = 0 } });
                    c.Steps.Add(new CutStep { Shape = CutShapes.Hole, Keep = CutShapes.Open, Rot = new Quat4(0.1f, 0.2f, 0.3f, 0.927362f) });
                    c.Unknown = new JsonLeftovers();
                    c.Unknown.Add("future", JObject.Parse("{\"a\": 0.1, \"b\": [1, 2.5]}"));
                    ItemOf(s, ItemA).Bake.Unknown = new JsonLeftovers();
                    ItemOf(s, ItemA).Bake.Unknown.Add("later", "x");
                }),
                ("two items of one bake and a piece of a piece", s =>
                {
                    var a = ItemOf(s, ItemA);
                    s.Scene.Nodes.Add(new SceneNode(Id(0xbe), new ItemNodeState { Ref = a.Ref, Group = a.Group, Bake = a.Bake.Clone() }) { Order = 4 });
                    var second = ItemOf(s, PieceB).Cut.Clone();
                    second.Steps.Add(new CutStep { Shape = CutShapes.Plane, Keep = CutShapes.Back });
                    s.Scene.Nodes.Add(new SceneNode(Id(0xbb), new ItemNodeState { Ref = a.Ref, Bake = ItemOf(s, PieceB).Bake.Clone(), Cut = second }) { Order = 5 });
                }),
                ("a piece whose second step carries a pose, a third an empty one", s =>
                {
                    var c = ItemOf(s, PieceB).Cut;
                    c.Steps[1].Pose = new SortedDictionary<string, Vec3>(StringComparer.Ordinal) { ["cf_J_Spine01"] = new Vec3(-30f, 15.25f, 0.1f), ["cf_J_Hips"] = new Vec3(1e-7f, -179.99f, 90f) };
                    c.Steps.Add(new CutStep { Shape = CutShapes.Box, Keep = CutShapes.Outside, Pose = new SortedDictionary<string, Vec3>(StringComparer.Ordinal) });
                }),
            };
            int blobsSeen = 0, cuts = 0, stepPoses = 0;
            foreach (var (what, shape) in cases)
            {
                FixtureBakes(out var a, out var b);
                var s = FixtureScene(a, b);
                shape(s);
                var first = s.ToBytes(Opts());
                var l = SceneContainer.Load(first, LOpts());
                t.Check(l.Ok, "BF7: " + what + ": loads (" + l + ")");
                if (!l.Ok) continue;
                var second = l.Value.ToBytes(Opts());
                t.Check(Same(first, second), "BF7: " + what + ": save -> load -> save byte-identical\n    " + DocIo.FirstDiff(first, second));
                var third = SceneContainer.Load(second, LOpts()).Value.ToBytes(Opts());
                t.Check(Same(first, third), "BF7: " + what + ": and once more");
                blobsSeen = Math.Max(blobsSeen, ContainerReader.Read(first).Entries.Count(e => e.Name.EndsWith(".bin", StringComparison.Ordinal)));
                cuts = Math.Max(cuts, Regex(SceneText(first), "\"cut\": {"));
                var posed = l.Value.Scene.Nodes.Select(n => n.State).OfType<ItemNodeState>().Where(i => i.Cut != null).SelectMany(i => i.Cut.Steps).Where(x => x.Pose != null).ToList();
                stepPoses = Math.Max(stepPoses, posed.Count);
                if (posed.Count > 0)
                {
                    var c = ItemOf(l.Value, PieceB).Cut;
                    t.Check(c.Steps[1].Pose != null && c.Steps[1].Pose.Count == 2 && c.Steps[1].Pose["cf_J_Hips"].Equals(new Vec3(1e-7f, -179.99f, 90f)) && c.Steps[1].Pose["cf_J_Spine01"].Equals(new Vec3(-30f, 15.25f, 0.1f))
                        && c.Steps[2].Pose != null && c.Steps[2].Pose.Count == 0 && c.Steps[0].Pose == null, "BF7: " + what + ": the step poses read back in bits, the empty one empty, the first step none");
                }
            }
            t.Info("BF7: the scenes held up to " + blobsSeen + " blobs, " + cuts + " cuts and " + stepPoses + " steps with a pose");
            t.Check(blobsSeen >= 2 && cuts >= 1, "BF7: the scene holds at least 2 blobs and a cut");
            t.Check(stepPoses >= 1, "BF7: a scene holds a step with a pose (" + stepPoses + ")");
        }

        static int Regex(string text, string what)
        {
            int n = 0, i = 0;
            while ((i = text.IndexOf(what, i, StringComparison.Ordinal)) >= 0) { n++; i += what.Length; }
            return n;
        }

        // ---------------- BF8: reader rules and limits ----------------

        static void BF8(TestRunner t)
        {
            var a = MakeBake(BakeA, 1);
            var cut = HandCut(a.Block);

            // An unknown shape or keep: kept, reported, and the step cannot run.
            var odd = cut.Clone();
            odd.Steps[0].Shape = "sphere";
            odd.Steps[1].Keep = "middle";
            var f = new FindingList();
            var back = ItemCut.Parse(odd.ToJson(), f);
            t.Check(back.Steps[0].Shape == "sphere" && back.Steps[1].Keep == "middle" && f.CountOf("json.unknownEnum") == 2 && back.ToJson() == odd.ToJson(), "BF8: an unknown shape and keep are kept, json.unknownEnum (" + f + ")");
            t.Check(back.Steps[0].Problem() != null && back.Steps[1].Problem() != null && back.Problem().StartsWith("steps[0]", StringComparison.Ordinal), "BF8: and the steps cannot be run: " + back.Problem());
            var mismatch = new CutStep { Shape = CutShapes.Plane, Keep = CutShapes.Inside };
            t.Check(mismatch.Problem() != null && new CutStep { Shape = CutShapes.Box, Keep = CutShapes.Outside }.Problem() == null && new CutStep { Shape = CutShapes.Hole, Keep = CutShapes.Open }.Problem() == null,
                "BF8: a keep that belongs to another shape cannot run: " + mismatch.Problem());
            t.Check(cut.Problem() == null, "BF8: the hand-made cut can run");

            // The pose each step is decided at (spec 8.2 "Per-step pose"): its own, else the previous step's, the first the block's, none = the baked pose.
            var chain = HandCutPosed(a.Block);
            chain.Steps.Add(new CutStep { Shape = CutShapes.Box, Keep = CutShapes.Inside });
            var fp = new FindingList();
            var chainBack = ItemCut.Parse(chain.ToJson(), fp);
            t.Check(fp.Count == 0 && chainBack.ToJson() == chain.ToJson(), "BF8: a chain with a step pose reads back silently, the same text");
            t.Check(ReferenceEquals(chainBack.PoseOf(0), chainBack.Pose) && ReferenceEquals(chainBack.PoseOf(1), chainBack.Pose), "BF8: steps 0 and 1 (no pose of their own) are decided at the block's pose");
            t.Check(ReferenceEquals(chainBack.PoseOf(2), chainBack.Steps[2].Pose) && ReferenceEquals(chainBack.PoseOf(3), chainBack.Steps[2].Pose) && chainBack.PoseOf(3)["cf_J_Hips"].Equals(new Vec3(45f, 0f, -12.5f)),
                "BF8: step 2 at its own, step 3 (none of its own) at step 2's");
            chainBack.Pose = null;
            t.Check(chainBack.PoseOf(0) == null && chainBack.PoseOf(2) != null, "BF8: no block pose: the first steps at the baked pose (null), the posed step at its own");
            t.Throws<ArgumentOutOfRangeException>(() => chainBack.PoseOf(4), "BF8: PoseOf past the steps");
            var unknownInPose = DocReader.Parse(Encoding.UTF8.GetBytes(chain.ToJson()), "cut");
            ((JObject)unknownInPose["steps"][2]).Add("later", 1);
            var fu = new FindingList();
            var withLater = ItemCut.Parse(CanonicalJsonWriter.ToText(unknownInPose), fu);
            t.Check(fu.Count == 0 && withLater.ToJson().Contains("\"later\": 1") && withLater.Steps[2].Pose.Count == 2, "BF8: an unknown member beside a step's pose is kept");

            // A ragged value out of range: kept, cut.range for each, clamped when applied.
            var rag = cut.Clone();
            rag.Steps[0].Ragged = new CutRagged { Amp = 0.75f, Tooth = 0.01f, Random = -1f, Seed = 10000 };
            var fr = new FindingList();
            var rb = ItemCut.Parse(rag.ToJson(), fr);
            var applied = rb.Steps[0].Ragged.Applied();
            t.Check(rb.ToJson() == rag.ToJson() && fr.CountOf(BakeFindings.CutRange) == 4 && fr.All(x => x.Severity == FindingSeverity.Info), "BF8: four values out of range are kept, cut.range (info) each (" + fr + ")");
            t.Check(applied.Amp == 0.5f && applied.Tooth == 0.05f && applied.Random == 0f && applied.Seed == 9999 && !rb.Steps[0].Ragged.InRange && applied.InRange, "BF8: and clamped when applied");
            var fin = new FindingList();
            ItemCut.Parse(cut.ToJson(), fin);
            t.Check(fin.Count == 0 && cut.Steps[0].Ragged.InRange, "BF8: values in range read silently");

            // Limits: 64 steps, 1,024 data entries, the excess kept and not applied.
            var steps = cut.Clone();
            while (steps.Steps.Count < BakeLimits.MaxSteps) steps.Steps.Add(new CutStep { Shape = CutShapes.Plane, Keep = CutShapes.Front });
            var f64 = new FindingList();
            t.Check(ItemCut.Parse(steps.ToJson(), f64).AppliedSteps.Count == 64 && f64.Count == 0, "BF8: 64 steps read silently");
            steps.Steps.Add(new CutStep { Shape = CutShapes.Box, Keep = CutShapes.Outside });
            var f65 = new FindingList();
            var s65 = ItemCut.Parse(steps.ToJson(), f65);
            t.Check(s65.Steps.Count == 65 && s65.AppliedSteps.Count == 64 && f65.CountOf(BakeFindings.CutLimit) == 1 && s65.ToJson() == steps.ToJson(), "BF8: 65 steps: kept, 64 applied, cut.limit (" + f65 + ")");
            var data = a.Block.Clone();
            while (data.Data.Count < BakeLimits.MaxData) data.Data.Add(ResStore.NameOf(Encoding.ASCII.GetBytes("HNBG" + data.Data.Count)));
            var fd = new FindingList();
            t.Check(ItemBake.Parse(data.ToJson(), fd).AppliedData.Count == 1024 && fd.Count == 0, "BF8: 1,024 data entries read silently");
            data.Data.Add(ResStore.NameOf(Encoding.ASCII.GetBytes("HNBT extra")));
            var fd2 = new FindingList();
            var d2 = ItemBake.Parse(data.ToJson(), fd2);
            t.Check(d2.Data.Count == 1025 && d2.AppliedData.Count == 1024 && fd2.CountOf(BakeFindings.Limit) == 1 && d2.ToJson() == data.ToJson(), "BF8: 1,025: kept, 1,024 applied, bake.limit (" + fd2 + ")");
            var src = cut.Clone();
            src.Source.Data = new List<string>(data.Data);
            var fs = new FindingList();
            t.Check(ItemCut.Parse(src.ToJson(), fs).Source.AppliedData.Count == 1024 && fs.CountOf(BakeFindings.Limit) == 1, "BF8: a source's data over the limit too");
            // The same through a scene file: kept and written back.
            var sc = InfraFixtures.SmallScene(0, 0);
            sc.Scene.Nodes.Add(new SceneNode(PieceB, new ItemNodeState { Ref = "hs2next.bake#1", Bake = d2, Cut = s65 }) { Order = 0 });
            var file = sc.ToBytes(Opts());
            var lf = SceneContainer.Load(file, LOpts());
            t.Check(lf.Ok && lf.Findings.CountOf(BakeFindings.Limit) == 1 && lf.Findings.CountOf(BakeFindings.CutLimit) == 1 && Same(EntryBytes(file, "scene.json"), EntryBytes(lf.Value.ToBytes(Opts()), "scene.json")),
                "BF8: in a scene: both limits reported at load, every entry written back (" + lf + ")");
            t.Check(lf.Findings.All(x => x.Path.Contains("nodes[0].item.")), "BF8: the findings name the item's path: " + string.Join(", ", lf.Findings.Select(x => x.Path)));

            // Gathering a block's blobs: the scene first, the store by id, data the main does not name unused, a blob only the main names.
            var res = new ResStore();
            a.AddTo(res);
            var fg = new FindingList();
            var set = BakeBlobSet.Gather(a.Block, res, null, fg, "nodes[0].item.bake");
            t.Check(set.Status == BakeBlobSet.Result.Ok && set.Blobs.Count == 2 && set.Absent.Count == 0 && fg.Count == 0 && set.Main.Id == BakeA, "BF8: a whole block gathers its main and two blobs silently");
            var extra = a.Block.Clone();
            var strayBlob = MakeBake(Id(0xbc), 12);
            strayBlob.AddTo(res);
            extra.Data.Add(ResStore.NameOf(strayBlob.Mesh));
            var fe = new FindingList();
            var se = BakeBlobSet.Gather(extra, res, null, fe, "x");
            t.Check(se.Status == BakeBlobSet.Result.Ok && se.Blobs.Count == 2 && se.Get(BakeBlob.Sha256(strayBlob.Mesh)) == null && fe.Count == 0, "BF8: a data entry the main does not name is kept and unused");
            var lacking = a.Block.Clone();
            lacking.Data.RemoveAt(1);
            var fl = new FindingList();
            var sl = BakeBlobSet.Gather(lacking, res, null, fl, "x");
            t.Check(sl.Status == BakeBlobSet.Result.Ok && sl.Blobs.Count == 2 && fl.Count == 0, "BF8: a blob the main names and data lacks is taken from the scene anyway");
            var res2 = new ResStore();
            res2.AddBlob(a.Main);
            res2.AddBlob(a.Mesh);
            var fm = new FindingList();
            var sm = BakeBlobSet.Gather(lacking, res2, null, fm, "nodes[3].item.bake");
            t.Check(sm.Status == BakeBlobSet.Result.Ok && sm.Absent.Count == 1 && sm.Absent[0] == BakeBlob.Sha256(a.Texture) && fm.CountOf(BakeFindings.DataMissing) == 1 && fm[0].Path == "nodes[3].item.bake",
                "BF8: neither data nor the scene holds it: absent, bake.dataMissing (" + fm + ")");
            var fmiss = new FindingList();
            var sx = BakeBlobSet.Gather(a.Block, res2, null, fmiss, "x");
            t.Check(sx.Status == BakeBlobSet.Result.Missing && fmiss.CountOf(BakeFindings.Missing) == 1, "BF8: a data blob missing and no store: bake.missing (" + fmiss + ")");
            var dir = Path.Combine(TempOf(t), "bf8_store");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            var store = BakeStore.Open(dir);
            store.Add(new BakeStoreRow { Id = BakeA, Name = "A", Kind = BakeKinds.Parts, Main = BakeBlob.Sha256(a.Main), Data = a.MainDoc.DataBlobs() }, a.ByName());
            var res3 = new ResStore();
            var fst = new FindingList();
            var st = BakeBlobSet.Gather(a.Block, res3, store, fst, "x");
            t.Check(st.Status == BakeBlobSet.Result.Ok && st.FromStore == 3 && res3.Count == 3 && fst.Count == 0, "BF8: a block whose blobs the scene lacks: the store's row of its id supplies them into the scene (" + fst + ")");
            var other = a.Block.Clone();
            other.Id = Id(0xbd);
            var fo = new FindingList();
            t.Check(BakeBlobSet.Gather(other, new ResStore(), store, fo, "x").Status == BakeBlobSet.Result.Missing && fo.CountOf(BakeFindings.Missing) == 1, "BF8: the store gives nothing to another id");
            var res4 = new ResStore();
            res4.AddBlob(BakeBlob.Encode(BakeBlobKind.Main, Encoding.UTF8.GetBytes("{}")));
            var broken = new ItemBake { Id = BakeA, Main = ResStore.NameOf(BakeBlob.Encode(BakeBlobKind.Main, Encoding.UTF8.GetBytes("{}"))) };
            var fc = new FindingList();
            t.Check(BakeBlobSet.Gather(broken, res4, null, fc, "x").Status == BakeBlobSet.Result.Corrupt && fc.CountOf(BakeFindings.BlobCorrupt) == 1, "BF8: a main that does not read: bake.blobCorrupt (" + fc + ")");
            var fsrc = new FindingList();
            t.Check(BakeBlobSet.Gather(cut.Source, res, null, fsrc, "x").Status == BakeBlobSet.Result.Ok, "BF8: a cut's source gathers the same way");
        }
    }
}

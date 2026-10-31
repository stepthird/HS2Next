// The committed pre-release fixtures (Tests/SaveFormat/fixtures/v02_dev1/): one file of each kind (character,
// coordinate, scene with one character subtree, pose) made from the golden documents by the writer of the last
// pre-release format, the one that wrote prerelease "dev.1" (the v0.2 writer: the document schema did not change between
// the v0.2 tag and the format v1 freeze). They prove that files written before the freeze stay readable and re-save.
// The files were written once, before the freeze, and are never regenerated: the writer refuses a folder that already
// holds one of them. Synthetic documents only (references are ids, no user or game data); placeholder thumbnail; fixed
// clock and app version.
using System;
using System.IO;
using System.Text;

namespace HS2Next.Save.Tests
{
    public static class FixtureWriter
    {
        /// <summary>The folder under the save-format fixtures (Tests/SaveFormat/).</summary>
        public const string V02Dev1Folder = "fixtures/v02_dev1";
        public const string V02AppVersion = "0.2.0";
        /// <summary>The character node of the golden scene that holds the subtree.</summary>
        public const string V02CharaNode = "6f1c0e4a9b2d4c3e8f7a6b5c4d3e2f10";
        public const string V02SceneTitle = "v0.2 fixture scene";
        public static readonly DateTime V02Clock = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
        public static readonly string[] V02Files = { "chara.hs2nchara", "coord.hs2ncoord", "scene.hs2nscene", "pose.hs2npose" };

        /// <summary>The save options the fixtures were written with (no ext handlers, so every inline block stays unknown).</summary>
        public static SaveOptions V02Options() => new SaveOptions
        {
            Clock = () => V02Clock,
            Thumbnail = ThumbnailMode.Placeholder,
            Registry = new ExtRegistry(),
            AppVersion = V02AppVersion,
        };

        /// <summary>The fixture folder under a save-format fixtures root.</summary>
        public static string V02Dir(string fixturesRoot) => Path.Combine(fixturesRoot, "fixtures", "v02_dev1");

        /// <summary>
        /// Writes the four fixtures into <paramref name="dir"/> from the golden documents of <paramref name="goldenDir"/>
        /// (default: <see cref="DocTests_T1Golden.GoldenDir"/>). Refuses when any of the four files exists.
        /// Returns one line per file (name, bytes).
        /// </summary>
        public static string WriteV02Dev1(string dir, string goldenDir = null)
        {
            goldenDir = goldenDir ?? DocTests_T1Golden.GoldenDir;
            foreach (var f in V02Files)
                if (File.Exists(Path.Combine(dir, f))) throw new InvalidOperationException("the fixtures are never regenerated: " + f + " exists in " + dir);
            Directory.CreateDirectory(dir);
            byte[] gChara = File.ReadAllBytes(Path.Combine(goldenDir, "chara.json"));
            byte[] gCoord = File.ReadAllBytes(Path.Combine(goldenDir, "coord.json"));
            byte[] gScene = File.ReadAllBytes(Path.Combine(goldenDir, "scene.json"));
            byte[] gPose = File.ReadAllBytes(Path.Combine(goldenDir, "pose.json"));

            var report = new StringBuilder();
            var chara = new CharaContainer();
            chara.Subtree.Chara = DocIo.Chara(gChara);
            chara.Subtree.Coord = DocIo.Coord(gCoord);
            Write(report, dir, "chara.hs2nchara", chara);

            Write(report, dir, "coord.hs2ncoord", new CoordContainer { Coord = DocIo.Coord(gCoord) });

            var scene = new SceneContainer { Scene = DocIo.Scene(gScene), Title = V02SceneTitle };
            var sub = new CharaContainer();
            sub.Subtree.Chara = DocIo.Chara(gChara);
            sub.Subtree.Coord = DocIo.Coord(gCoord);
            scene.PlaceCharacter(V02CharaNode, sub);
            Write(report, dir, "scene.hs2nscene", scene);

            var pose = PoseDocument.Read(DocReader.ForDocument(gPose, new DocReadContext(PoseDocument.EntryName, false, new FindingList())));
            Write(report, dir, "pose.hs2npose", new PoseContainer { Pose = pose });
            return report.ToString();
        }

        static void Write(StringBuilder report, string dir, string name, ContainerBase c)
        {
            string path = Path.Combine(dir, name);
            var r = c.Save(path, V02Options());
            if (!r.Ok) throw new InvalidOperationException(name + ": " + r.Error.Message);
            report.Append(name).Append(' ').Append(new FileInfo(path).Length).Append(" bytes; findings ").Append(r.Findings.Count).AppendLine();
        }
    }
}

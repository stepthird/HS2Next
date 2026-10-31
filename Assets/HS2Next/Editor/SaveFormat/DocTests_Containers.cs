// D2 check of the golden documents through D1's containers (T1 integration): the golden chara/coord/scene JSON
// packed into .hs2nchara / .hs2ncoord / .hs2nscene (fixed clock, placeholder thumbnail, no ext handlers) loads
// and saves byte-identically, and the document entries inside are the golden bytes themselves. The fixture
// container files of T1 are D1's (they add unknown ext files and a foreign entry); this checks that the
// document layer and the container layer agree on the golden contents.
using System;

namespace HS2Next.Save.Tests
{
    public static class DocTests_T1Containers
    {
        static readonly DateTime Clock = new DateTime(2026, 10, 16, 1, 2, 3, DateTimeKind.Utc);
        const string CharaNode = "6f1c0e4a9b2d4c3e8f7a6b5c4d3e2f10";

        static SaveOptions Save() => new SaveOptions { Clock = () => Clock, Thumbnail = ThumbnailMode.Keep, Registry = new ExtRegistry(), AppVersion = "0.1.0" };
        static LoadOptions Load() => new LoadOptions { Registry = new ExtRegistry() };

        public static void Run(TestRunner t)
        {
            DocTests_T1Golden.FixturesDir = t.FixturesDir;
            byte[] gChara = DocTests_T1Golden.Golden("chara.json"), gCoord = DocTests_T1Golden.Golden("coord.json"), gScene = DocTests_T1Golden.Golden("scene.json");

            var chara = new CharaContainer();
            chara.Subtree.Chara = DocIo.Chara(gChara);
            chara.Subtree.Coord = DocIo.Coord(gCoord);
            byte[] c1 = Pack(t, ".hs2nchara", () => chara.ToBytes(Save()));
            if (c1 != null)
            {
                byte[] c2 = Pack(t, ".hs2nchara reloaded", () => CharaContainer.Load(c1, Load()).Unwrap().ToBytes(Save()));
                Same(t, ".hs2nchara load → save", c1, c2);
                Entry(t, c1, "chara.json", gChara);
                Entry(t, c1, "coord/coord.json", gCoord);
            }

            var coord = new CoordContainer { Coord = DocIo.Coord(gCoord) };
            byte[] o1 = Pack(t, ".hs2ncoord", () => coord.ToBytes(Save()));
            if (o1 != null)
            {
                byte[] o2 = Pack(t, ".hs2ncoord reloaded", () => CoordContainer.Load(o1, Load()).Unwrap().ToBytes(Save()));
                Same(t, ".hs2ncoord load → save", o1, o2);
                Entry(t, o1, "coord.json", gCoord);
            }

            var scene = new SceneContainer { Scene = DocIo.Scene(gScene), Title = "Golden scene" };
            scene.PlaceCharacter(CharaNode, chara);
            byte[] s1 = Pack(t, ".hs2nscene", () => scene.ToBytes(Save()));
            if (s1 != null)
            {
                var loaded = SceneContainer.Load(s1, Load());
                byte[] s2 = Pack(t, ".hs2nscene reloaded", () => loaded.Unwrap().ToBytes(Save()));
                Same(t, ".hs2nscene load → save", s1, s2);
                Entry(t, s1, "scene.json", gScene);
                Entry(t, s1, "charas/" + CharaNode + "/chara.json", gChara);
                Entry(t, s1, "charas/" + CharaNode + "/coord/coord.json", gCoord);
                t.Check(loaded.Findings.Has("scene.unknownKind"), "scene container: unknown node kind reported through the container");
            }
        }

        static byte[] Pack(TestRunner t, string what, Func<byte[]> f)
        {
            try { return f(); }
            catch (Exception e) { t.Check(false, what + ": threw " + e.GetType().Name + ": " + e.Message); return null; }
        }

        static void Same(TestRunner t, string what, byte[] a, byte[] b)
        {
            if (b == null) return;
            t.Check(DocIo.Same(a, b), what + " byte-identical");
        }

        static void Entry(TestRunner t, byte[] container, string name, byte[] expected)
        {
            var e = ContainerReader.Read(container).Get(name);
            bool ok = e != null && DocIo.Same(e.Data, expected);
            t.Check(ok, name + " inside the container = golden bytes" + (ok || e == null ? "" : " (" + DocIo.FirstDiff(expected, e.Data) + ")") + (e == null ? " (entry missing)" : ""));
        }
    }
}

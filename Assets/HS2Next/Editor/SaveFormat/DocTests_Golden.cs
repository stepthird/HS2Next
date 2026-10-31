// D2 test T1, document part: the hand-written golden documents in Fixtures\golden_src
// (chara.json, coord.json, scene.json) load and save byte-identically. They cover every member of the schema
// (checked against a "full" generated document: every nullable set, every list non-empty, all 11 ME types, the
// six node kinds), all 45 effect values, unknown members, unknown enum strings, an unknown ME type, an unknown
// node kind with a child, and inline ext blocks. The container-level part of T1 (manifest, unknown ext files,
// foreign entries) is D1's.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save.Tests
{
    public static class DocTests_T1Golden
    {
        /// <summary>Fixture folder from <see cref="TestRunner.FixturesDir"/> (staging Fixtures\, repository Tests/SaveFormat/), set by Run.</summary>
        public static string FixturesDir;

        /// <summary>golden_src under <see cref="FixturesDir"/>; without it, Fixtures\golden_src next to the build folder.</summary>
        public static string GoldenDir => Path.GetFullPath(FixturesDir != null ? Path.Combine(FixturesDir, "golden_src")
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Fixtures", "golden_src"));

        public static byte[] Golden(string name) => File.ReadAllBytes(Path.Combine(GoldenDir, name));

        public static void Run(TestRunner t)
        {
            FixturesDir = t.FixturesDir;
            // Byte-identical load → save.
            var fc = new FindingList();
            var chara = Round(t, "chara.json", b => DocIo.Chara(b, fc), d => DocIo.Bytes(d));
            var fo = new FindingList();
            var coord = Round(t, "coord.json", b => DocIo.Coord(b, fo), d => DocIo.Bytes(d));
            var fs = new FindingList();
            var scene = Round(t, "scene.json", b => DocIo.Scene(b, fs), d => DocIo.Bytes(d));
            if (chara == null || coord == null || scene == null) return;

            // What the goldens are meant to exercise.
            t.Equal(3, fc.CountOf("json.unknownEnum"), "chara: 3 unknown enum strings (overlay target, renderer property, ME type)");
            t.Check(chara.Face.Unknown?.Get("futureFaceMember") != null && chara.Unknown?.Get("futureCharaMember") != null, "chara: unknown members kept");
            t.Check(chara.MaterialOverrides.Count == 15 && chara.MaterialOverrides[14] is MeUnknown u && u.TypeName == "futureMaterialEditorType", "chara: unknown ME type kept in place");
            var meTypes = new HashSet<string>();
            foreach (var m in chara.MaterialOverrides) meTypes.Add(m.Type);
            foreach (var ty in MaterialOverride.TypeOrder) t.Check(meTypes.Contains(ty), "chara: ME type " + ty + " present");
            t.Equal(null, chara.Body.SkinOverlays[3].TargetType, "chara: unknown overlay target → typed view null");
            t.Equal("FutureOverlayTarget", chara.Body.SkinOverlays[3].Target, "chara: unknown overlay target string kept");
            t.Check(chara.Ext.Contains("example.plugin") && chara.Ext.Contains("example.plugin.sub"), "chara: inline ext read");
            t.Equal(0, fo.Count, "coord: no findings");
            t.Check(coord.Accessories[0].Ext.Contains("example.plugin"), "coord: accessory inline ext read");
            t.Check(coord.Accessories[1].Unknown?.Get("futureAccessoryMember") != null, "coord: accessory unknown member kept");
            t.Equal(1, fs.CountOf("scene.unknownKind"), "scene: unknown node kind reported");
            t.Equal(2, fs.CountOf("json.unknownEnum"), "scene: unknown enum strings reported (voice repeat, route ease)");
            var kinds = new HashSet<string>();
            foreach (var n in scene.Nodes) kinds.Add(n.State is OpaqueNode ? "?" : n.Kind);
            foreach (var k in new[] { "chara", "item", "light", "folder", "route", "camera", "?" }) t.Check(kinds.Contains(k), "scene: node kind " + k + " present");
            var opaque = scene.Find("8b8b8b8b8b8b8b8b8b8b8b8b8b8b8b8b");
            t.Check(opaque?.State is OpaqueNode && opaque.Ext.Contains("example.plugin"), "scene: opaque node and its ext kept");
            t.Equal("8b8b8b8b8b8b8b8b8b8b8b8b8b8b8b8b", scene.Find("9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c")?.Parent?.Node, "scene: child of the opaque node kept");
            t.Check(scene.Find("6f1c0e4a9b2d4c3e8f7a6b5c4d3e2f10")?.State is CharaNodeState cs && cs.Ik.Targets[12] == null && cs.Ik.Targets[0] != null, "scene: missing IK target stays missing");
            t.Equal(45, CountEffectValues(Golden("scene.json")), "scene: 45 effect values");

            // Coverage: every member path of a "full" document appears in the goldens.
            Coverage(t, "chara.json", DocIo.Bytes(new DocFuzz(11, extras: false, full: true).Chara()));
            Coverage(t, "coord.json", DocIo.Bytes(new DocFuzz(12, extras: false, full: true).Coord()));
            Coverage(t, "scene.json", DocIo.Bytes(new DocFuzz(13, extras: false, full: true).Scene()));
        }

        static T Round<T>(TestRunner t, string name, Func<byte[], T> read, Func<T, byte[]> write) where T : class
        {
            byte[] golden;
            try { golden = Golden(name); }
            catch (Exception e) { t.Check(false, name + ": cannot read golden (" + e.Message + ")"); return null; }
            try
            {
                var doc = read(golden);
                var back = write(doc);
                bool same = DocIo.Same(golden, back);
                t.Check(same, name + ": load → save byte-identical" + (same ? "" : " (" + DocIo.FirstDiff(golden, back) + ")"));
                if (!same) File.WriteAllBytes(Path.Combine(t.TempDir, name), back);
                return doc;
            }
            catch (Exception e)
            {
                t.Check(false, name + ": threw " + e.GetType().Name + ": " + e.Message);
                return null;
            }
        }

        static int CountEffectValues(byte[] scene)
        {
            var effects = (JObject)DocReader.Parse(scene, "scene.json")["scene"]["effects"];
            int n = 0;
            foreach (var block in effects.Properties()) n += ((JObject)block.Value).Count;
            return n;
        }

        // Paths of every member, with array indices collapsed to [] and data keys (maps, ext namespaces) to *; the
        // eight clothes kinds, four hair parts, two eyes, 13 IK targets and all ME arrays are one shape each.
        static readonly Regex[] Collapse =
        {
            new Regex(@"(^|\.)clothes\.(top|bottom|innerTop|innerBottom|gloves|pantyhose|socks|shoes)\b"),
            new Regex(@"^hair\.(back|front|side|option)\b"),
            new Regex(@"\.eyes\.(left|right)\b"),
            new Regex(@"\.targets\.[A-Za-z]+\b"),
        };

        static readonly Regex FileMember = new Regex(@"\.(panel|file|background|frame)\.(res|user|hs2)$");

        static void Paths(JToken t, string path, HashSet<string> into)
        {
            if (t is JObject o)
            {
                bool map = path.EndsWith(".neckFix") || path.EndsWith(".bones") || path.EndsWith("state.accessories") || path.EndsWith("ext") || path == "ext";
                foreach (var p in o.Properties())
                {
                    string q = (path.Length == 0 ? "" : path + ".") + (map ? "*" : p.Name);
                    into.Add(Normalise(q));
                    Paths(p.Value, q, into);
                }
            }
            else if (t is JArray a)
                foreach (var e in a) Paths(e, path + "[]", into);
        }

        static string Normalise(string p)
        {
            p = Collapse[0].Replace(p, "$1clothes.*");
            p = Collapse[1].Replace(p, "hair.*");
            p = Collapse[2].Replace(p, ".eyes.*");
            p = Collapse[3].Replace(p, ".targets.*");
            p = FileMember.Replace(p, ".$1.<file>");
            // One MaterialEditor schema for every materialOverrides array: its members are checked once.
            int me = p.IndexOf("materialOverrides[].", StringComparison.Ordinal);
            return me < 0 ? p : "ME[]." + p.Substring(me + "materialOverrides[].".Length);
        }

        static void Coverage(TestRunner t, string name, byte[] full)
        {
            var golden = new HashSet<string>();
            Paths(DocReader.Parse(Golden(name), name), "", golden);
            var need = new HashSet<string>();
            Paths(DocReader.Parse(full, name), "", need);
            var missing = new List<string>();
            foreach (var p in need) if (!golden.Contains(p)) missing.Add(p);
            missing.Sort(StringComparer.Ordinal);
            t.Check(missing.Count == 0, name + ": golden covers every member path (" + need.Count + ")" +
                (missing.Count == 0 ? "" : "; missing " + missing.Count + ": " + string.Join(", ", missing.GetRange(0, Math.Min(12, missing.Count)))));
        }
    }
}

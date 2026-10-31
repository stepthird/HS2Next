// Unity-free checks of the pre-upload texture check (Runtime/Content/TextureCheck.cs), plain Mono: Tools/mono_tests.ps1 -Suite
// content (fixtures = the project root). TC1 of the texture check:
//   TextureCheckClassTests     the defect classes and repair actions on header values (the census classes, the mip rule, the
//                              block-size padding, the grey replacement, the healthy shapes that stay as they are).
//   TextureCheckFixtureTests   the synthetic bundles of Editor/Fixtures/TextureCheck (Tools/make_texcheck_fixtures.py; no game
//                              asset): each verdict, its actions and its refused assets (a container entry whose preload range
//                              needs a mesh cut inside its data) equal expect.tsv (the rules of Tools/texguard_repair.py on
//                              the same headers; a mesh whose last field is missing whole is completed with zeros), the patch
//                              gives the same actions and reads back with an "ok" verdict and the repaired fields, objects
//                              without a defect are byte-equal to the original; a node name that runs past its first zero byte
//                              is read past and kept in the patched copy (tc_longname); a node table that names other files
//                              than the textures stream from gets those names (tc_streamname, tc_streamdir; tc_overlap: and
//                              its resource node is moved off the serialized file); a texture whose path names another
//                              directory gets the serialized file's (tc_streamstray); the per-asset redirect fixtures (no
//                              texture) are "ok".
//   TextureCheckCacheTests     the verdict cache (a line round trip, a stale stamp, a corrupted line ignored and the file
//                              rewritten) and the repair cache (the copy and its index line; a second copy of a key gets a new
//                              file name and replaces the line).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HS2Next.Content.Tests
{
    using HS2Next.Save.Tests;

    static class TextureCheckFixtures
    {
        public static string Dir(TestRunner t) => Path.Combine(t.FixturesDir ?? "", "Assets", "HS2Next", "Editor", "Fixtures", "TextureCheck");

        public static TextureCheck.Facts F(string name, int w, int h, int fmt, int mips, long size, long data, bool streamed = false) =>
            new TextureCheck.Facts
            {
                Name = name, W = w, H = h, Format = fmt, Mips = mips, Size = (int)size, Images = 1, HasMipCount = true,
                Inline = streamed ? 0 : (int)data, StreamSize = streamed ? data : 0, StreamPath = streamed ? "archive:/CAB-x/CAB-x.resS" : "",
            };

        public static string Action(TextureCheck.Facts f, out List<string> classes)
        {
            classes = TextureCheck.Classes(f);
            var p = TextureCheck.PlanFor(f, classes, k => new byte[k]);
            return p?.Action;
        }
    }

    public static class TextureCheckClassTests
    {
        public static void Run(TestRunner t)
        {
            long C(int w, int h, int f, int m) => TextureHeaders.ChainSize(w, h, f, m);
            void Case(TextureCheck.Facts f, string cls, string action, string what)
            {
                string a = TextureCheckFixtures.Action(f, out var c);
                t.Check(string.Join(", ", c) == cls && a == action, $"{what}: classes [{string.Join(", ", c)}] action {a ?? "none"} (expected [{cls}] {action ?? "none"})");
            }
            var F = (Func<string, int, int, int, int, long, long, bool, TextureCheck.Facts>)TextureCheckFixtures.F;
            Case(F("a", 512, 512, 12, 10, C(512, 512, 12, 10), C(512, 512, 12, 10), true), "", null, "512 x 512 DXT5, 10 mips, the chain, streamed -> fine");
            Case(F("a", 256, 256, 3, 1, 196634, 196634, false), "", null, "RGB24 256 x 256 with 26 bytes more than the chain -> fine (common, harmless)");
            Case(F("a", 128, 128, 29, 1, 5000, 5000, false), "", null, "a crunched DXT5 (its stream is shorter by design) -> fine");
            Case(F("a", 4, 4, 4, 1, 64, 64, false), "", null, "the 4 x 4 RGBA32 grey -> fine");
            Case(F("a", 256, 256, 12, 0, 65536, 65536, true), "mip < 1", "mips 0->1", "mips 0 with data for one level -> mips 0->1");
            Case(F("a", 32, 32, 4, 0, C(32, 32, 4, 6), C(32, 32, 4, 6), false), "mip < 1", "mips 0->6", "mips 0 with the whole chain -> mips 0->6");
            Case(F("a", 64, 64, 4, 0, 1000, 1000, false), "mip < 1", "grey 4x4", "mips 0 with no whole level -> grey");
            var above = F("a", 2048, 2048, 10, 11, C(2048, 2048, 10, 12), C(2048, 2048, 10, 12), true);
            above.Readable = true;
            Case(above, "readable streamed", "readable off", "a readable streamed DXT1 2048, 11 mips, data of 12 -> not readable (the mip count stays)");
            Case(F("a", 2048, 2048, 10, 11, C(2048, 2048, 10, 12), C(2048, 2048, 10, 12), true), "", null, "the same, not readable -> fine (Unity loads it)");
            Case(F("a", 64, 64, 4, 9, C(64, 64, 4, 7), C(64, 64, 4, 7), false), "mips > chain", "mips 9->7", "64 x 64 with 9 mips, data of 7 -> mips 9->7");
            Case(F("a", 512, 511, 12, 10, C(512, 512, 12, 10), C(512, 512, 12, 10), true), "block size", "pad 512x511->512x512", "512 x 511 DXT5 (the trial's thumbnail) -> padded");
            Case(F("a", 150, 150, 12, 0, C(152, 152, 12, 1), C(152, 152, 12, 1), false), "mip < 1, block size", "pad 150x150->152x152 mips 0->1", "150 x 150 DXT5 without a mip -> padded with its chain length");
            Case(F("a", 493, 388, 10, 9, C(493, 388, 10, 9), C(493, 388, 10, 9), false), "block size", "pad 493x388->496x388", "493 x 388 DXT1, its own chain -> padded (block counts unchanged)");
            Case(F("a", 1097, 1024, 12, 11, C(1100, 1024, 12, 11), C(1100, 1024, 12, 11), true), "block size", "pad 1097x1024->1100x1024", "1097 x 1024 DXT5 whose data are the padded size's chain -> the header padded");
            Case(F("a", 1097, 1024, 12, 11, C(1097, 1024, 12, 11), C(1097, 1024, 12, 11), true), "block size", "pad 1097x1024->1100x1024 mips 11->1", "1097 x 1024 DXT5 with its own chain -> padded, the levels whose block counts change dropped");
            Case(F("a", 128, 128, 4, 1, 65536, 1000, false), "data short", "grey 4x4", "RGBA32 with 1,000 of 65,536 bytes -> grey");
            Case(F("tex_n", 64, 64, 12, 1, 4096, 100, true), "data short", "flat normal 4x4", "a short normal map (name ends _n) -> flat normal");
            Case(F("a", 256, 256, 34, 1, 32768, 32768, false), "cpu format", "grey 4x4", "ETC_RGB4 -> grey");
            Case(F("Font Texture", 0, 0, 1, 1, 0, 0, false), "empty size", null, "an empty size (a dynamic font's texture) -> left as it is");
            var rs = F("a", 64, 64, 10, 7, C(64, 64, 10, 7), C(64, 64, 10, 7), true);
            rs.Readable = true;
            Case(rs, "readable streamed", "readable off", "a readable texture whose data are streamed -> not readable");
            var rs0 = F("a", 32, 32, 4, 0, C(32, 32, 4, 1), C(32, 32, 4, 1), true);
            rs0.Readable = true;
            Case(rs0, "mip < 1, readable streamed", "mips 0->1 readable off", "the same with mips 0 -> both fixes");
            var ri = F("a", 32, 32, 4, 6, C(32, 32, 4, 6), C(32, 32, 4, 6), false);
            ri.Readable = true;
            Case(ri, "", null, "a readable texture with inline data -> fine");
            var outside = F("a", 64, 64, 10, 1, 2048, 2048, true);
            outside.StreamOutside = true;
            Case(outside, "stream outside", "grey 4x4", "streamed range outside the bundle -> grey");
        }
    }

    public static class TextureCheckFixtureTests
    {
        public static void Run(TestRunner t)
        {
            string dir = TextureCheckFixtures.Dir(t);
            string expect = Path.Combine(dir, "expect.tsv");
            t.Check(File.Exists(expect), "Editor/Fixtures/TextureCheck/expect.tsv exists");
            if (!File.Exists(expect)) return;
            int fixtures = 0;
            foreach (var line in File.ReadAllLines(expect, Encoding.UTF8))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split('\t');
                string name = p[0], file = Path.Combine(dir, name);
                fixtures++;
                var bytes = File.ReadAllBytes(file);
                var v = TextureCheck.Analyze(new BundleFileRef { Key = "fixture:" + name, File = file, Offset = 0, Length = bytes.Length, Stamp = "1:1" });
                t.Check(v.KindName == p[2] && v.ActionsText == p[4], $"{name} ({p[1]}): verdict {v.KindName} [{v.ActionsText}] (expected {p[2]} [{p[4]}])");
                t.Check(v.Kind == TextureVerdictKind.Malformed || v.Textures.ToString() == p[3], $"{name}: {v.Textures} textures read (expected {p[3]})");
                t.Check(v.Key == "fixture:" + name && v.Stamp == "1:1" && v.Ms >= 0, $"{name}: the verdict carries its key and stamp");
                string refused = p.Length > 5 ? p[5] : "";
                t.Check(string.Join("|", v.RefusedAssets) == refused, $"{name}: refused assets [{string.Join("|", v.RefusedAssets)}] (expected [{refused}])");
                if (name == "tc_longname.unity3d")
                {
                    var lb = Hs2BundleFile.FromBytes(bytes, name);
                    t.Check(lb.NodesRecovered == 1 && lb.Nodes.Count == 2 && lb.Nodes[1].Path.EndsWith(".resS", StringComparison.Ordinal),
                            $"{name}: the node record after a name with bytes past its zero is found ({lb.NodesRecovered} recovered)");
                }
                if (refused.Length > 0)
                {
                    var parsed = TextureVerdictCache.Parse(TextureVerdictCache.Format(v));
                    t.Check(parsed != null && string.Join("|", parsed.RefusedAssets) == refused, $"{name}: the refused assets survive the verdict cache");
                }
                if (!v.NeedsPatch) continue;
                var src = Hs2BundleFile.FromBytes(bytes, name, lzma: true);
                var patched = TextureCheck.Patch(src, out var acts);
                t.Check(patched != null && string.Join("|", acts.Select(a => a.Texture + "=" + a.Action)) == p[4], $"{name}: the patch does the verdict's actions");
                if (patched == null) continue;
                var back = TextureCheck.Analyze(Hs2BundleFile.FromBytes(patched, name + " patched"));
                t.Check(back.Kind == TextureVerdictKind.Ok && back.Textures == v.Textures, $"{name}: the patched bundle reads back ok ({back})");
                if (name == "tc_longname.unity3d")
                {
                    var pb = Hs2BundleFile.FromBytes(patched, name + " patched");
                    var ob = Hs2BundleFile.FromBytes(bytes, name);
                    t.Check(pb.NodesRecovered == 1 && pb.BlocksInfo.Skip(pb.NodeTableAt).Count() == ob.BlocksInfo.Skip(ob.NodeTableAt).Count()
                            && pb.Nodes.Select(x => x.Path).SequenceEqual(ob.Nodes.Select(x => x.Path)),
                            $"{name}: the patched copy keeps the node table as the file holds it (names, the bytes past the first name)");
                }
                t.Check(Hs2BundleFile.FromBytes(patched, "x").DataCompression == 0, $"{name}: the patched bundle is uncompressed");
                CheckFields(t, name, bytes, patched, acts);
            }
            t.Check(fixtures >= 14, $"{fixtures} texture-check fixtures");

            // the per-asset redirect fixtures hold text assets only: every compression reads, no texture, verdict ok
            foreach (var f in Directory.GetFiles(Path.Combine(t.FixturesDir ?? "", "Assets", "HS2Next", "Editor", "Fixtures", "Redirect"), "*.unity3d"))
            {
                var v = TextureCheck.Analyze(new BundleFileRef { Key = "fixture:" + Path.GetFileName(f), File = f, Offset = 0, Length = new FileInfo(f).Length, Stamp = "1:1" });
                t.Check(v.Kind == TextureVerdictKind.Ok && v.Textures == 0, $"{Path.GetFileName(f)}: ok, no texture ({v})");
            }
        }

        // the patched textures carry the action's fields; the other objects are byte-equal to the original's
        static void CheckFields(TestRunner t, string name, byte[] before, byte[] after, List<TextureDefect> acts)
        {
            var a = Hs2BundleFile.FromBytes(before, "a", lzma: true);
            var b = Hs2BundleFile.FromBytes(after, "b");
            var ad = a.AllData();
            var bd = b.AllData();
            int same = 0, changed = 0, wrong = 0, bundleLevel = 0;
            // the names repair: the patched copy's nodes carry the given names, in the same order
            foreach (var act in acts.Where(x => x.Class == "stream name"))
            {
                bundleLevel++;
                var given = act.Action.Substring("names ".Length).Split(' ').TakeWhile(g => g != "moved").ToList();
                if (b.Nodes.Count != a.Nodes.Count || !given.All(g => b.Nodes.Any(x => x.Path == g))) wrong++;
                // a moved node: the patched copy's nodes lie back to back, none overlapping
                for (int i = 0; i < b.Nodes.Count; i++)
                    for (int j = i + 1; j < b.Nodes.Count; j++)
                        if (b.Nodes[i].Offset < b.Nodes[j].Offset + b.Nodes[j].Size && b.Nodes[j].Offset < b.Nodes[i].Offset + b.Nodes[i].Size) wrong++;
            }
            for (int ni = 0; ni < a.Nodes.Count; ni++)
            {
                var n = a.Nodes[ni];
                if ((n.Flags & 4) == 0) continue;
                Hs2BundleNode? bn = ni < b.Nodes.Count ? b.Nodes[ni] : (Hs2BundleNode?)null;
                if (bn == null) { wrong++; continue; }
                var af = Hs2SerializedFile.FromBytes(ad, (int)n.Offset, (int)n.Size, "a");
                var bf = Hs2SerializedFile.FromBytes(bd, (int)bn.Value.Offset, (int)bn.Value.Size, "b");
                foreach (var o in af.Objects)
                {
                    if (!bf.TryFind(o.PathId, out var q)) { wrong++; continue; }
                    var act = acts.FirstOrDefault(x => x.PathId == o.PathId);
                    if (act == null)
                    {
                        bool eq = ad.Skip((int)(n.Offset + o.Start)).Take((int)o.Size).SequenceEqual(bd.Skip((int)(bn.Value.Offset + q.Start)).Take((int)q.Size));
                        if (eq) same++; else wrong++;
                        continue;
                    }
                    changed++;
                    bool ok;
                    string action = act.Action;
                    if (action.StartsWith("mesh tail +", StringComparison.Ordinal))
                    {
                        // the original bytes, then the zeros that complete the object; it now reads whole
                        int tail = int.Parse(action.Substring("mesh tail +".Length));
                        var now = bd.Skip((int)(bn.Value.Offset + q.Start)).Take((int)q.Size).ToArray();
                        ok = q.Size == o.Size + tail && now.Take((int)o.Size).SequenceEqual(ad.Skip((int)(n.Offset + o.Start)).Take((int)o.Size))
                             && now.Skip((int)o.Size).All(x => x == 0) && Hs2TypeTree.ZeroTail(bf.TypeTreeOf(q), new Hs2ObjectBytes(bd, bn.Value.Offset + q.Start, q.Size)) == 0;
                        if (!ok) wrong++;
                        continue;
                    }
                    var tf = TextureCheck.ReadFacts(bf.TypeTreeOf(q), new Hs2ObjectBytes(bd, bn.Value.Offset + q.Start, q.Size), true);
                    bool streamPath = action.EndsWith("stream path", StringComparison.Ordinal);
                    if (streamPath) action = action.Substring(0, action.Length - "stream path".Length).TrimEnd();
                    bool readableOff = action.EndsWith("readable off", StringComparison.Ordinal);
                    if (readableOff) action = action.Substring(0, action.Length - "readable off".Length).TrimEnd();
                    if (act.Grey) ok = tf.W == 4 && tf.H == 4 && tf.Format == 4 && tf.Mips == 1 && tf.Size == 64 && tf.Inline == 64 && tf.StreamSize == 0 && tf.StreamPath == "";
                    else if (action.Length == 0) ok = true;
                    else if (action.StartsWith("pad ", StringComparison.Ordinal)) ok = tf.W % 4 == 0 && tf.H % 4 == 0 && action.Contains("->" + tf.W + "x" + tf.H);
                    else ok = action.EndsWith("->" + tf.Mips, StringComparison.Ordinal);
                    if (readableOff && tf.Readable) ok = false;
                    if (streamPath && (tf.StreamPath == null || !tf.StreamPath.StartsWith("archive:/" + bn.Value.Path + "/", StringComparison.Ordinal))) ok = false;
                    if (!ok) wrong++;
                }
            }
            t.Check(wrong == 0 && changed + bundleLevel == acts.Count, $"{name}: {changed} patched objects carry their action's fields, {bundleLevel} node-name actions, {same} others byte-equal ({wrong} wrong)");
        }
    }

    public static class TextureCheckCacheTests
    {
        public static void Run(TestRunner t)
        {
            string root = Path.Combine(Path.GetTempPath(), "hs2next_texcheck_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                var c = new TextureVerdictCache(root);
                var v = new TextureVerdict { Key = "mods/a b.zipmod!chara/x.unity3d", Stamp = "100:200", Kind = TextureVerdictKind.PatchGrey, Textures = 3, TextureBytes = 4096, Ms = 1.5 };
                v.Defects.Add(new TextureDefect { Texture = "tex=1", Action = "mips 0->1", Class = "mip < 1" });
                v.Defects.Add(new TextureDefect { Texture = "bump_n", Action = "flat normal 4x4", Class = "data short", Grey = true });
                c.Add(v);
                c.Add(new TextureVerdict { Key = "hs2-install:chara/y.unity3d", Stamp = "5:6", Kind = TextureVerdictKind.Ok, Textures = 0 });
                var line = TextureVerdictCache.Format(v);
                var back = TextureVerdictCache.Parse(line);
                t.Check(back != null && back.Key == v.Key && back.Stamp == v.Stamp && back.Kind == v.Kind && back.Textures == 3 && back.TextureBytes == 4096
                        && back.ActionsText == v.ActionsText && back.Defects[1].Grey && back.Defects[0].Class == "mip < 1", "a verdict line reads back (" + line + ")");
                t.Check(TextureVerdictCache.Parse("x\ty\tbogus\t1\t2\t3\t\t") == null && TextureVerdictCache.Parse("only\tthree\tfields") == null
                        && TextureVerdictCache.Parse("k\ts\tpatch\t1\t2\t3\t\t") == null, "lines that do not parse are refused (unknown verdict, field count, a patch without defects)");

                var d = new TextureVerdictCache(root);
                File.AppendAllText(d.FilePath, "garbage line\n" + "k\ts\tok\tx\t0\t0\t\t\n", new UTF8Encoding(false));
                d.Load();
                t.Check(d.Loaded == 2 && d.Ignored == 2 && d.TryGet(v.Key, "100:200", out var hit) && hit.Cached && hit.Defects.Count == 2,
                        $"a new cache reads the file: {d.Loaded} verdicts, {d.Ignored} lines ignored");
                t.Check(!d.TryGet(v.Key, "100:201", out _), "a changed stamp makes the verdict stale");
                t.Check(File.ReadAllLines(d.FilePath)[0] == TextureVerdictCache.Header, "the file starts with the rules version (" + TextureVerdictCache.Header + ")");
                var lines = File.ReadAllLines(d.FilePath);
                t.Check(lines.Count(l => !l.StartsWith("#")) == 2 && !lines.Any(l => l.StartsWith("garbage")), "the file is rewritten without the corrupted lines");

                // a file written under other rules is emptied
                string old = Path.Combine(root, "old");
                var o = new TextureVerdictCache(old);
                Directory.CreateDirectory(Path.GetDirectoryName(o.FilePath));
                File.WriteAllText(o.FilePath, "#texcheck verdicts rules 1\n" + line + "\n", new UTF8Encoding(false));
                o.Load();
                t.Check(o.Count == 0 && o.Ignored == 2 && File.ReadAllLines(o.FilePath)[0] == TextureVerdictCache.Header, $"a file of other rules is emptied ({o.Ignored} lines dropped)");

                // the repair cache
                string label = "a.guid:chara/x.unity3d";
                var p1 = TextureRepairCache.Store(root, v.Key, label, new byte[] { 1, 2, 3 }, "tex=1=mips 0->1");
                var p2 = TextureRepairCache.Store(root, v.Key, label, new byte[] { 4, 5 }, "tex=1=mips 0->1|bump_n=flat normal 4x4");
                string stem = TextureRepairCache.Stem(v.Key);
                t.Check(p1 != null && Path.GetFileName(p1) == stem + ".unity3d" && File.ReadAllBytes(p1).Length == 3, "the first copy is <sha1(key)[:16]>.unity3d");
                t.Check(p2 != null && Path.GetFileName(p2) == stem + "_2.unity3d" && File.Exists(p1), "a second copy of the key gets a new name; the first stays");
                var idx = File.ReadAllLines(TextureRepairCache.IndexFile(root)).Where(l => !l.StartsWith("#")).ToList();
                t.Check(idx.Count == 1 && idx[0] == string.Join("\t", v.Key, "bundles/" + stem + "_2.unity3d", label, "tex=1=mips 0->1|bump_n=flat normal 4x4"),
                        "the index holds one line for the key, naming the newest copy");
                t.Check(stem.Length == 16 && stem.All(ch => "0123456789abcdef".IndexOf(ch) >= 0), "the stem is 16 lower-case hex digits (as Tools/texguard_repair.py names copies)");
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }
    }
}

// BakeTest, the import of the old bakes (package I). The owner's folder of old bakes
// (<hs2Root>/UserData/CharaToItem) is read only: it is hashed before and after (BH3), copied file by file into the test's scratch for
// BH1 (opened for reading with a share mode that lets others read), and imported in place by BH2 into a store the test makes under
// Spike/out/bake/import/. Nothing is ever written, moved or deleted there, and nothing that deletes is given that folder. Old textures
// are read as numbers; nothing is decoded to be looked at, no render is written.
//   headless (Headless_Import): BH1 the reader (the owner's files' numbers equal the table of their known numbers per file, read from copies; the synthetic files
//            of Tests/Bake/old written by this test and read back member by member; truncated, wrong-magic, wrong-version and unclosed
//            files and a texture whose content does not match its name refused with bake.importCorrupt); BH3 the old folder untouched.
//   graphics (Render_Import): BH2 the import on this machine (7 rows under their old numbers, every source resolved, each bake built
//            from the store and drawn, a second run adds nothing), BH4 the shader mapping, BV7 the members of an imported synthetic
//            bake (o_mnpb and an AIT/mnpb slot), BH3 again around BH2, BW10 the import's time.
// -writeOld writes the synthetic files into Tests/Bake/old (the fixture); without it BH1 compares them with what this writer makes.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HS2Next;
using HS2Next.App;
using HS2Next.Chara.Mosaic;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Bake;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class BakeTest
{
    static string ImportDir => Path.Combine(OutDir, "import");
    static string ImportCopyDir => Path.Combine(ImportDir, "copy");
    static string ImportSynthDir => Path.Combine(ImportDir, "synthetic");
    static string ImportUserData => Path.Combine(ImportDir, "userdata");
    static string ImportUserData2 => Path.Combine(ImportDir, "userdata2");
    /// <summary>The committed synthetic old bakes (made by this test with -writeOld).</summary>
    static string OldFixtureDir => Path.Combine(ContentSettings.ProjectRoot, "Tests", "Bake", "old");
    /// <summary>The owner's folder of old bakes (read only).</summary>
    static string OwnerOldFolder => OldBakeImport.DefaultFolder(CardResolveTest.GameRoot);
    /// <summary>The folder of old bakes is not on this machine (a public checkout): BH1's own files, BH2, BH3, BH4 and BW10 print a SKIP line
    /// and count no FAIL; the synthetic files keep the reader covered (the lead's ruling for the public checkout).</summary>
    static bool OldFolderAbsent => string.IsNullOrEmpty(OwnerOldFolder) || !Directory.Exists(OwnerOldFolder);
    const string OldAbsentText = "the old bakes' folder is absent";
    static void SkipOld(string s) => rep.AppendLine($"SKIP  [{current}] {s}");

    /// <summary>per file: number, nodes, renderers, stored meshes, referenced meshes, stored vertices, stored / source / list textures,
    /// materials, FK names, section renderers, fill (1) or not (0).</summary>
    static readonly int[][] OldExpected =
    {
        new[] { 73100062, 355, 1, 1, 1, 34661, 0, 0, 0, 1, 74, 1, 0 },
        new[] { 73100068, 821, 1, 1, 0, 9107, 2, 0, 0, 1, 114, 0, 1 },
        new[] { 73100069, 821, 1, 1, 0, 1905, 2, 0, 0, 1, 114, 0, 1 },
        new[] { 73100070, 820, 1, 1, 0, 1542, 2, 0, 0, 1, 114, 0, 1 },
        new[] { 73100071, 820, 1, 1, 0, 1328, 2, 0, 0, 1, 114, 0, 1 },
        new[] { 73100072, 857, 11, 10, 20, 83754, 20, 15, 13, 27, 66, 4, 0 },
        new[] { 73100073, 857, 19, 10, 20, 48044, 20, 15, 13, 27, 5, 4, 0 },
    };
    const int OldFileCount = 35, OldBakeCount = 7, OldTextureCount = 27;

    // ------------------------------------------------------------ headless

    static void Headless_Import()
    {
        var before = Want("BH3") ? HashOld("headless, before") : null;
        Group("BH1", "the reader of the old bakes: the owner's files (copies), the synthetic files, refusals", Bh1);
        Group("BH3", "the old folder untouched (SHA-256 of every file before and after)", () => Bh3(before));
        Tidy(ImportCopyDir);
        Tidy(ImportSynthDir);
    }

    /// <summary>The SHA-256 of every file of the owner's folder (by relative path), read with a share mode that lets others read; null when
    /// the folder is not there.</summary>
    static SortedDictionary<string, string> HashOld(string when)
    {
        var dir = OwnerOldFolder;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) { Info("the old folder is not there (" + when + ")"); return null; }
        var d = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            d[f.Substring(dir.Length).TrimStart('\\', '/').Replace('\\', '/')] = Sha256File(f);
        Info("old folder hashed (" + when + "): " + d.Count + " files");
        return d;
    }

    static string Sha256File(string path)
    {
        using (var s = OldBakeReader.OpenRead(path))
        using (var h = SHA256.Create())
            return OldBakeReader.Hex(h.ComputeHash(s));
    }

    static void Bh3(SortedDictionary<string, string> before)
    {
        if (before == null && OldFolderAbsent) { SkipOld("BH3 " + OldAbsentText + ": nothing to hash"); return; }
        var after = HashOld("after");
        int n = before?.Count ?? 0;
        Check("BH3 present: " + OldFileCount + " files hashed", n >= OldFileCount, n + " files");
        if (before == null || after == null) { Check("BH3 the folder was there before and after", false, ""); return; }
        var changed = before.Where(kv => !after.TryGetValue(kv.Key, out var h) || h != kv.Value).Select(kv => kv.Key).ToList();
        var added = after.Keys.Where(k => !before.ContainsKey(k)).ToList();
        Check("BH3 every file's SHA-256 equal before and after, nothing added or removed", changed.Count == 0 && added.Count == 0 && before.Count == after.Count,
            "changed or gone " + changed.Count + (changed.Count > 0 ? " (" + string.Join(", ", changed.Take(5)) + ")" : "") + ", added " + added.Count);
    }

    /// <summary>Deletes the files of a scratch folder made by this test (by name, one level of sub-folders), never a tree.</summary>
    static void Tidy(string dir)
    {
        if (!Directory.Exists(dir) || Flag("-keepImport")) return;
        foreach (var sub in Directory.GetDirectories(dir))
        {
            foreach (var f in Directory.GetFiles(sub)) File.Delete(f);
            if (Directory.GetDirectories(sub).Length == 0) Directory.Delete(sub, false);
        }
        foreach (var f in Directory.GetFiles(dir)) File.Delete(f);
        if (Directory.GetDirectories(dir).Length == 0) Directory.Delete(dir, false);
    }

    static void Bh1()
    {
        // ---- the owner's files, read from copies the test makes in its scratch
        var src = OwnerOldFolder;
        int read = 0;
        if (OldFolderAbsent) SkipOld("BH1 " + OldAbsentText + ": the old files are not read; the synthetic files below keep the reader covered");
        else if (Check("BH1 the old folder exists", Directory.Exists(src), src == null ? "no game root" : ""))
        {
            Tidy(ImportCopyDir);
            Directory.CreateDirectory(Path.Combine(ImportCopyDir, OldBakeReader.TextureFolderName));
            int copied = 0, same = 0;
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                var rel = f.Substring(src.Length).TrimStart('\\', '/');
                var to = Path.Combine(ImportCopyDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                using (var a = OldBakeReader.OpenRead(f))
                using (var b = new FileStream(to, FileMode.Create, FileAccess.Write, FileShare.None))
                    a.CopyTo(b);
                copied++;
                if (Sha256File(to) == Sha256File(f)) same++;
            }
            Check("BH1 the copies equal the old files", copied == same && copied >= OldFileCount, copied + " copied, " + same + " equal");
            var scan = OldBakeReader.Scan(ImportCopyDir);
            Info("files " + scan.Files.Count + ", last_no.txt " + (scan.LastNumber?.ToString(CultureInfo.InvariantCulture) ?? "none"));
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in scan.Files)
            {
                var fl = new FindingList();
                var b = OldBakeReader.ReadFile(p, fl);
                if (!Check("BH1 read " + NumberOf(p), b != null, string.Join("; ", fl.Select(x => x.ToString())))) continue;
                read++;
                foreach (var t in b.Textures) if (t.Stored) used.Add(t.Hash);
                var got = new[] { b.Number, b.Nodes.Count, b.Renderers.Count, b.StoredMeshes, b.ReferencedMeshes, b.StoredVertices, b.TexturesStored, b.TexturesFromSource,
                    b.TexturesFromList, b.Materials.Count, b.Fk.Count, b.SectionRenderers, b.FillInside ? 1 : 0 };
                var want = OldExpected.FirstOrDefault(e => e[0] == b.Number);
                Check("BH1 " + b.Number + " numbers equal the known table (nodes, renderers, meshes stored / referenced, stored vertices, textures stored / source / list, materials, FK, sections, fill)",
                    want != null && want.SequenceEqual(got), "read [" + string.Join(", ", got) + "]" + (want == null ? ", not in the table" : want.SequenceEqual(got) ? "" : ", the known table [" + string.Join(", ", want) + "]"));
                Check("BH1 " + b.Number + " version 6, its body read to the closing magic", b.Version == 6 && b.BodyBytes > 0, "version " + b.Version + ", body " + b.BodyBytes + " bytes, file " + b.FileBytes);
            }
            Check("BH1 present: " + OldBakeCount + " files read", read >= OldBakeCount, read + " read");
            // texture files: every one read and equal to its name, RGBA32 or RGB24 with mips, every one used, none missing
            var tex = Directory.GetFiles(scan.TextureFolder, "*" + OldBakeReader.TextureExtension).OrderBy(x => x, StringComparer.Ordinal).ToList();
            int ok = 0, lin = 0, mips = 0, rgb = 0, rgba = 0;
            long raw = 0, bytes = 0;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in tex)
            {
                var fl = new FindingList();
                long got = 0;
                if (OldBakeReader.ReadTexture(p, (l, w, h, d) => got += (long)w * h, fl, out var t)) ok++;
                else Info("refused: " + string.Join("; ", fl.Select(x => x.ToString())));
                if (t == null) continue;
                names.Add(t.Name);
                if (t.Linear) lin++;
                if (t.Mips) mips++;
                if (t.Format == OldTexture.FormatRgb24) rgb++;
                if (t.Format == OldTexture.FormatRgba32) rgba++;
                raw += t.RawLength; bytes += t.FileBytes;
            }
            Check("BH1 texture files: " + OldTextureCount + ", each equal to its name (MD5 of the size string and the texels)", tex.Count == OldTextureCount && ok == tex.Count, ok + " of " + tex.Count);
            Check("BH1 texture formats RGBA32 and RGB24 only, all with mips", rgb + rgba == tex.Count && mips == tex.Count, "RGBA32 " + rgba + ", RGB24 " + rgb + ", with mips " + mips);
            var missing = used.Where(u => !names.Contains(u)).ToList();
            var unused = names.Where(u => !used.Contains(u)).ToList();
            Check("BH1 every texture file used by a bake, none missing", missing.Count == 0 && unused.Count == 0, "used " + used.Count + ", missing " + missing.Count + ", unused " + unused.Count);
            Measure("BH1 texture files: " + tex.Count + ", linear " + lin + ", raw " + F(raw / 1048576.0, "F1") + " MiB, files " + F(bytes / 1048576.0, "F1") + " MiB");
            // a copy cut in half is refused
            var first = scan.Files.FirstOrDefault();
            if (first != null)
            {
                var all = File.ReadAllBytes(first);
                var fl = new FindingList();
                var b = OldBakeReader.Read(all.Take(all.Length / 2).ToArray(), "half", fl);
                Check("BH1 a copy cut in half is refused with " + OldBakeReader.Corrupt, b == null && fl.Has(OldBakeReader.Corrupt), Short(string.Join("; ", fl.Select(x => x.ToString()))));
            }
        }
        // ---- the synthetic files: written by this test, read back member by member, compared with the committed fixture
        var models = SyntheticOld();
        Directory.CreateDirectory(Path.Combine(ImportSynthDir, OldBakeReader.TextureFolderName));
        bool write = Flag("-writeOld");
        if (write) Directory.CreateDirectory(Path.Combine(OldFixtureDir, OldBakeReader.TextureFolderName));
        int same2 = 0, files = 0;
        foreach (var (name, bytes) in OldWriter.Files(models))
        {
            files++;
            var p = Path.Combine(ImportSynthDir, name);
            File.WriteAllBytes(p, bytes);
            var fix = Path.Combine(OldFixtureDir, name);
            if (write) File.WriteAllBytes(fix, bytes);
            if (File.Exists(fix) && OldWriter.SameContent(File.ReadAllBytes(fix), bytes)) same2++;
            else Info("fixture differs or is missing: " + name);
        }
        Check("BH1 the fixture Tests/Bake/old equals what this writer makes (headers and inflated bodies)", same2 == files && files > 0, same2 + " of " + files + (write ? " (written now)" : ""));
        int round = 0;
        foreach (var m in models)
        {
            var fl = new FindingList();
            var b = OldBakeReader.ReadFile(Path.Combine(ImportSynthDir, OldWriter.FileName(m)), fl);
            string want = OldWriter.Dump(m), got = b == null ? "null" : OldWriter.Dump(b);
            if (Check("BH1 synthetic " + m.Number + " (version " + m.Version + ") read back member by member", want == got, b == null ? string.Join("; ", fl.Select(x => x.ToString())) : FirstDiff(want, got))) round++;
        }
        var sf = OldBakeReader.Scan(ImportSynthDir);
        Check("BH1 synthetic folder: its files in order and its last number", sf.Files.Count == models.Count && sf.LastNumber == OldWriter.SyntheticLastNumber,
            sf.Files.Count + " files, last " + sf.LastNumber);
        foreach (var t in models.SelectMany(m => m.Textures).Where(t => t.Stored).Select(t => t.Hash).Distinct())
        {
            var fl = new FindingList();
            Check("BH1 synthetic texture " + t + " equals its name", OldBakeReader.ReadTexture(OldBakeReader.TexturePath(sf.TextureFolder, t), null, fl, out _), string.Join("; ", fl.Select(x => x.ToString())));
        }
        // ---- refusals
        int refused = 0, tried = 0;
        void Refuse(string what, byte[] bytes)
        {
            tried++;
            var fl = new FindingList();
            var b = OldBakeReader.Read(bytes, what, fl);
            if (Check("BH1 refused: " + what, b == null && fl.Has(OldBakeReader.Corrupt), b != null ? "read" : Short(string.Join("; ", fl.Select(x => x.ToString()))))) refused++;
        }
        var good = OldWriter.Bake(models[0]);
        foreach (int cut in new[] { 3, 9, 20, OldWriter.HeaderLength(models[0]) + 2, good.Length / 2, good.Length - 8 })
            Refuse("cut at byte " + cut + " of " + good.Length, good.Take(cut).ToArray());
        // the last byte of the raw deflate stream alone: the inflater gives the whole body without it (a fact, printed)
        var lastByte = OldBakeReader.Read(good.Take(good.Length - 1).ToArray(), "last byte cut", new FindingList());
        Info("a copy without the last byte of its deflate stream reads " + (lastByte == null ? "refused" : "with its whole body (" + lastByte.BodyBytes + " bytes, the closing magic present)"));
        var bad = (byte[])good.Clone(); bad[0] ^= 0x5a;
        Refuse("a wrong magic", bad);
        foreach (int v in new[] { 3, 7 })
        {
            var x = (byte[])good.Clone();
            BitConverter.GetBytes(v).CopyTo(x, 4);
            Refuse("version " + v, x);
        }
        Refuse("no closing magic", OldWriter.Bake(models[0], closing: false));
        Refuse("bytes after the closing magic", OldWriter.Bake(models[0], extra: 3));
        Refuse("a node whose parent follows it", OldWriter.Bake(models[0], badParent: true));
        // a texture file under a name its content does not hash to, and one cut short
        var tex0 = models[0].Textures.First(t => t.Stored).Hash;
        var tp = OldBakeReader.TexturePath(sf.TextureFolder, tex0);
        var tb = File.ReadAllBytes(tp);
        var wrong = OldBakeReader.TexturePath(sf.TextureFolder, new string('0', 32));
        File.WriteAllBytes(wrong, tb);
        tried++;
        var tf = new FindingList();
        if (Check("BH1 refused: a texture whose content does not match its name", !OldBakeReader.ReadTexture(wrong, null, tf, out _) && tf.Has(OldBakeReader.Corrupt), Short(string.Join("; ", tf.Select(x => x.ToString()))))) refused++;
        File.Delete(wrong);
        File.WriteAllBytes(wrong, tb.Take(tb.Length - 7).ToArray());
        tried++;
        tf = new FindingList();
        if (Check("BH1 refused: a texture file cut short", !OldBakeReader.ReadTexture(wrong, null, tf, out _) && tf.Has(OldBakeReader.Corrupt), Short(string.Join("; ", tf.Select(x => x.ToString()))))) refused++;
        File.Delete(wrong);
        Check("BH1 present: the refusals tried", tried >= 12, refused + " refused of " + tried);
    }

    /// <summary>A switch given on the command line (an argument with no value).</summary>
    static bool Flag(string name) => TestArgs.Get().Contains(name);

    static string NumberOf(string path) => Path.GetFileName(path).Split('_')[0];

    static string FirstDiff(string a, string b)
    {
        if (a == b) return "equal (" + a.Length + " chars)";
        int i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        int s = Math.Max(0, i - 60);
        return "differ at " + i + ": want ..." + a.Substring(s, Math.Min(140, a.Length - s)) + " | got ..." + b.Substring(s, Math.Min(140, b.Length - s));
    }

    // ------------------------------------------------------------ graphics

    static void Render_Import()
    {
        var before = Want("BH3") ? HashOld("render, before") : null;
        OldBakeImportResult first = null;
        Group("BH2", "the import on this machine (the owner's folder read in place, a store of the test's)", () => first = Bh2());
        Group("BH4", "the shader mapping of the imported materials", () => Bh4(first));
        Group("BV7", "R7: an imported synthetic old bake with o_mnpb and an AIT/mnpb slot: members", Bv7);
        Group("BH3", "the old folder untouched by the import (render row)", () => Bh3(before));
        foreach (var ud in new[] { ImportUserData, ImportUserData2 }) ClearStoreAt(ud);
    }

    /// <summary>A test host whose UserData (and so its store) is <paramref name="userData"/>.</summary>
    static Host ImportHost(string userData)
    {
        HS2Next.Motion.Face.FaceSetup.Seed = 0x5eed;
        var s = AppSettings.Defaults(Array.Empty<string>());
        s.Content = CardResolveTest.Settings;
        s.UserData = userData;
        s.Bridge.OutputDir = OutputDir;
        var o = services.NewSessionOptions();
        o.Ids = new CountingIdSource(DefaultSeed);
        return new Host { App = AppCore.ForTests(services, s, null, o) };
    }

    /// <summary>Empties a store folder the test made (files by name, never a tree).</summary>
    static void ClearStoreAt(string userData)
    {
        var dir = Path.Combine(userData, BakeStore.FolderName);
        if (Directory.Exists(dir))
        {
            var blobs = Path.Combine(dir, BakeStore.BlobsName);
            if (Directory.Exists(blobs)) { foreach (var f in Directory.GetFiles(blobs)) File.Delete(f); Directory.Delete(blobs, false); }
            foreach (var f in Directory.GetFiles(dir)) File.Delete(f);
            Directory.Delete(dir, false);
        }
        if (Directory.Exists(userData) && Directory.GetFileSystemEntries(userData).Length == 0) Directory.Delete(userData, false);
    }

    static OldBakeImportResult Bh2()
    {
        var folder = OwnerOldFolder;
        if (OldFolderAbsent) { SkipOld("BH2 " + OldAbsentText + ": nothing imported; BW10 not measured"); return null; }
        if (!Check("BH2 the old folder exists", Directory.Exists(folder), "")) return null;
        ClearStoreAt(ImportUserData);
        var store = BakeStore.ForUserData(ImportUserData);
        var sw = Stopwatch.StartNew();
        var r = OldBakeImport.Run(services.Catalog, store, folder);
        double ms = sw.Elapsed.TotalMilliseconds;
        foreach (var f in r.Findings) Info("finding: " + f);
        var nos = r.Imported.Select(x => x.No).OrderBy(x => x).ToList();
        var want = OldExpected.Select(e => e[0]).ToList();
        Check("BH2 " + OldBakeCount + " rows under their old numbers", nos.SequenceEqual(want) && r.Skipped.Count == 0,
            "imported [" + string.Join(", ", nos) + "], skipped " + r.Skipped.Count + (r.Skipped.Count > 0 ? " (" + string.Join("; ", r.Skipped.Select(s => s.File + " " + s.Code)) + ")" : ""));
        var rows = store.LiveRows();
        Check("BH2 the store's rows are the imported ones, kind imported, with the old names", rows.Count == OldBakeCount && rows.All(x => x.Kind == BakeData.KindImported) &&
            r.Imported.All(x => rows.Any(y => y.Number == x.No && y.Name == x.Name)), rows.Count + " rows, kinds " + string.Join(",", rows.Select(x => x.Kind).Distinct()));
        Check("BH2 the last number is the highest old number or last_no.txt's", store.LastNumber == Math.Max(want.Max(), OldBakeReader.Scan(folder).LastNumber ?? 0), "last " + store.LastNumber);
        int unresolved = 0;
        foreach (var x in r.Imported)
        {
            unresolved += x.Unresolved;
            Info($"{x.No}: renderers {x.Renderers} (members {x.Members}, sections {x.Sections}, fills {x.Fills}), textures {x.Textures}, {F(x.Bytes / 1048576.0, "F1")} MiB, {F(x.Ms, "F0")} ms; " +
                 string.Join(", ", x.Counts.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + " " + k.Value)));
            var old = OldExpected.First(e => e[0] == x.No);
            int oldRenderers = old[2];
            Check("BH2 " + x.No + " every renderer imported, nothing unresolved", x.Renderers == oldRenderers && x.Unresolved == 0,
                "renderers " + x.Renderers + " of " + oldRenderers + ", unresolved " + x.Unresolved + " (sources " + x.Count("source.missing") + ", textures " + x.Count("texture.missing") +
                ", meshes " + x.Count("mesh.missing") + ", renderers left " + x.Count("renderer.left") + ")");
        }
        Measure("BH2 unresolved over the 7 bakes: " + unresolved + " (expected 0)");
        int prefab = r.Imported.Sum(x => x.Count("source.prefab")), material = r.Imported.Sum(x => x.Count("source.material")),
            list = r.Imported.Sum(x => x.Count("texture.list")), stored = r.Imported.Sum(x => x.Count("texture.stored")), fromSource = r.Imported.Sum(x => x.Count("texture.source"));
        Check("BH2 present: >= 1 source of each kind (prefab, material, list texture, stored texture)", prefab >= 1 && material >= 1 && list >= 1 && stored >= 1,
            "prefabs " + prefab + ", materials " + material + ", list textures " + list + ", stored textures " + stored + ", textures from a source " + fromSource);
        Measure("BH2 bounds of the referenced renderers against their prefab's: " + string.Join(", ", new[] { "bounds.size", "bounds.extents", "bounds.other", "bounds.otherCentre" }
            .Select(k => k.Substring(7) + " " + r.Imported.Sum(x => x.Count(k)))));
        // BW10
        Measure("BW10 import of the 7 old bakes: " + F(ms / 1000.0, "F1") + " s (bar 60 s)");
        if (ms > 60000) Warn("BW10 the import took " + F(ms / 1000.0, "F1") + " s, over the bar of 60 s");
        // each imported bake built from the store and drawn
        using (var h = ImportHost(ImportUserData))
        {
            foreach (var x in r.Imported)
            {
                var it = AddFromStore(h, x.Row);
                if (!Check("BH2 " + x.No + " builds from the store's blobs (source bake, not the fallback)", it != null && it.Source == "bake" && !it.IsFallback && it.Instance,
                    it == null ? "no item" : "source " + it.Source + ", fallback " + it.IsFallback)) continue;
                // the view framed on the drawn vertices at the bake's pose (a bake keeps the character's widened bounds, V6: framing on
                // them measures the bounds, not the drawing; the lead's ruling after the first runs)
                int px = Footprint(h, it, true);
                Check("BH2 " + x.No + " renders (footprint >= 100 px)", px >= 100, px + " px, the view framed on the drawn vertices");
                Info("BH2 " + x.No + " footprint framed on the renderers' bounds (information, not a check): " + Footprint(h, it) + " px");
                h.Session.Delete(new[] { it.NodeId });
                h.Rt.ApplyNow();
            }
        }
        // a second run adds nothing
        var index0 = File.ReadAllBytes(store.IndexPath);
        var again = OldBakeImport.Run(services.Catalog, BakeStore.ForUserData(ImportUserData), folder);
        var index1 = File.ReadAllBytes(store.IndexPath);
        Check("BH2 a second run adds nothing (every file skipped with " + BakeFindings.ImportExists + ", the index unchanged)",
            again.Imported.Count == 0 && again.Skipped.Count == OldBakeCount && again.Skipped.All(s => s.Code == BakeFindings.ImportExists) && index0.SequenceEqual(index1),
            "imported " + again.Imported.Count + ", skipped " + again.Skipped.Count + ", index " + (index0.SequenceEqual(index1) ? "unchanged" : "changed"));
        return r;
    }

    /// <summary>Adds a store row as an item (its blobs into the scene's res/, the node's bake block), as the list's add does; the item.</summary>
    static ItemRealization AddFromStore(Host h, BakeStoreRow row)
    {
        var store = BakeProvider.StoreOf(h.Rt);
        var res = h.Session.Model.File.Res;
        foreach (var sha in row.Blobs())
        {
            var bytes = store.ReadBlob(sha);
            if (bytes == null) return null;
            res.AddBlob(bytes);
        }
        var r = h.Session.AddItem(BakeState(row.ToItemBake(), row.Number, row.Name));
        if (!r.Ok) return null;
        h.Rt.ApplyNow();
        return h.Rt.Realizations.OfType<ItemRealization>().FirstOrDefault(x => x.NodeId == r.NewId);
    }

    /// <summary>Pixels that differ between the item drawn and hidden, at a view framing its drawn vertices (<paramref name="vertices"/>)
    /// or its renderers' bounds.</summary>
    static int Footprint(Host h, ItemRealization it, bool vertices = false)
    {
        var rs = it.Instance.GetComponentsInChildren<Renderer>(true).Where(x => x && x.enabled && x.gameObject.activeInHierarchy).ToList();
        if (rs.Count == 0) return 0;
        var b = rs[0].bounds;
        foreach (var x in rs) b.Encapsulate(x.bounds);
        if (vertices && DrawnBounds(rs) is Bounds vb) b = vb;
        float fov = SceneViewCamera.DefaultFov;
        var v = new View
        {
            Name = "bake", Pivot = b.center, Rot = new Vector3(10f, 180f, 0f), Fov = fov, W = 640, H = 360,
            Distance = Mathf.Max(1f, b.extents.magnitude / Mathf.Tan(0.5f * fov * Mathf.Deg2Rad) * 1.2f),
        };
        Color32[] shown, hidden;
        using (new Only(rs, new HashSet<Renderer>(rs))) shown = Shot(h, v);
        using (new Only(rs, new HashSet<Renderer>())) hidden = Shot(h, v);
        int n = 0;
        for (int i = 0; i < shown.Length && i < hidden.Length; i++) if (Diff(shown[i], hidden[i]) > 0) n++;
        return n;
    }

    /// <summary>The world bounds of the vertices the renderers draw (a skinned one posed by BakeMesh), or null.</summary>
    static Bounds? DrawnBounds(List<Renderer> rs)
    {
        Bounds? b = null;
        var tmp = new Mesh { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            foreach (var r in rs)
            {
                Vector3[] v; Matrix4x4 m;
                if (r is SkinnedMeshRenderer s && s.sharedMesh)
                {
                    s.BakeMesh(tmp, false);
                    v = tmp.vertices;
                    m = Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
                }
                else if (r.TryGetComponent<MeshFilter>(out var f) && f.sharedMesh && f.sharedMesh.isReadable) { v = f.sharedMesh.vertices; m = r.transform.localToWorldMatrix; }
                else continue;
                foreach (var p in v)
                {
                    var w = m.MultiplyPoint3x4(p);
                    if (b is Bounds bb) { bb.Encapsulate(w); b = bb; } else b = new Bounds(w, Vector3.zero);
                }
            }
        }
        finally { Object.DestroyImmediate(tmp); }
        return b;
    }

    static void Bh4(OldBakeImportResult r)
    {
        if (r == null && OldFolderAbsent) { SkipOld("BH4 " + OldAbsentText + ": no imported material to map"); return; }
        if (!Check("BH4 the import ran", r != null && r.Imported.Count > 0, "")) return;
        var routes = r.Imported.SelectMany(x => x.Materials.Select(m => (x.No, m))).ToList();
        int original = 0, byName = 0, fallback = 0, bad = 0;
        var wrong = new List<string>();
        foreach (var (no, m) in routes)
        {
            bool ok;
            switch (m.Route)
            {
                case OldMaterialRoute.Original: original++; ok = m.Got == m.Expected && !string.IsNullOrEmpty(m.Got); break;
                case OldMaterialRoute.ByName: byName++; ok = ShaderMap.Fitted.TryGetValue(m.OldShader, out var fit) && m.Got == fit; break;
                default: fallback++; ok = m.Got == OldBakeImport.FallbackShader; break;
            }
            if (!ok) { bad++; wrong.Add(no + " " + m.Name + ": " + m.OldShader + " (" + m.Route + ", loaded " + m.Loaded + ") -> " + m.Got + ", expected " + m.Expected); }
        }
        var names = routes.Select(x => x.m.OldShader).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        Check("BH4 present: >= 5 distinct old shader names", names.Count >= 5, names.Count + ": " + string.Join(", ", names));
        Check("BH4 each old shader mapped to the replacement ShaderMap gives its loaded original (by name when none was loaded)", bad == 0,
            "materials " + routes.Count + ": original " + original + ", by name " + byName + ", fallback " + fallback + (wrong.Count > 0 ? "; " + string.Join("; ", wrong.Take(8)) : ""));
        foreach (var g in routes.GroupBy(x => x.m.OldShader + " -> " + x.m.Got).OrderBy(g => g.Key, StringComparer.Ordinal)) Info("  " + g.Key + " x" + g.Count());
        var shared = routes.Where(x => x.m.SharedCopy).ToList();
        var reported = shared.Count(x => r.Findings.Any(f => f.Code == BakeFindings.ImportShader && f.Message.Contains(x.m.Name)));
        Check("BH4 the shared-copy material reported (" + BakeFindings.ImportShader + ")", shared.Count >= 1 && reported == shared.Count,
            shared.Count + " shared-copy materials (" + string.Join(", ", shared.Select(x => x.No + " " + x.m.Name)) + "), reported " + reported);
    }

    static void Bv7()
    {
        var folder = OldFixtureDir;
        if (!Check("BV7 the synthetic old bakes exist (Tests/Bake/old)", Directory.Exists(folder) && OldBakeReader.Scan(folder).Files.Count > 0, folder)) return;
        ClearStoreAt(ImportUserData2);
        var store = BakeStore.ForUserData(ImportUserData2);
        var r = OldBakeImport.Run(services.Catalog, store, folder);
        foreach (var f in r.Findings) Info("finding: " + f);
        Check("BV7 the synthetic folder imports (versions 4, 5, 6)", r.Imported.Count == OldWriter.SyntheticCount && r.Skipped.Count == 0,
            "imported " + r.Imported.Count + ", skipped " + r.Skipped.Count + "; " + string.Join("; ", r.Imported.Select(x => x.No + ": " + x.Renderers + " renderers, " + x.Members + " members, " + x.Sections + " sections, " + x.Fills + " fills")));
        var row = store.Find(OldWriter.MemberNumber);
        if (!Check("BV7 the bake with members is in the store", row != null, "")) return;
        var main = BakeMain.FromBlob(store.ReadBlob(row.Main), new FindingList());
        var byNode = main.Renderers.ToDictionary(x => main.Nodes[x.Node].Name, x => x.Member);
        Check("BV7 the renderer named o_mnpb and the renderer with an AIT/mnpb slot are marked members, the plain one is not",
            byNode.TryGetValue("o_mnpb", out var a) && a && byNode.TryGetValue("part", out var b) && b && byNode.TryGetValue("plain", out var c) && !c,
            string.Join(", ", byNode.Select(kv => kv.Key + " " + kv.Value)));
        var mosaicMat = main.Materials.Count(m => m.Shader == MosaicSet.ShaderName);
        Check("BV7 the AIT/mnpb slot is stored with the mosaic material (R4)", mosaicMat >= 1, mosaicMat + " of " + main.Materials.Count);
        Check("BV7 no inside or section on a member (R3)", main.Renderers.Where(x => x.Member).All(x => x.Inside == BakeData.InsideNone),
            string.Join(", ", main.Renderers.Select(x => main.Nodes[x.Node].Name + " " + x.Inside)));
        using (var h = ImportHost(ImportUserData2))
        {
            var it = AddFromStore(h, row);
            if (!Check("BV7 the imported bake builds from the store", it != null && it.Source == "bake" && it.Instance, it == null ? "no item" : it.Source)) return;
            var rs = it.Instance.GetComponentsInChildren<Renderer>(true).GroupBy(x => x.name).ToDictionary(g => g.Key, g => g.All(x => MosaicSet.IsMember(x)));
            Check("BV7 the built item's member renderers are members before its first frame (R1)", rs.TryGetValue("o_mnpb", out var m1) && m1 && rs.TryGetValue("part", out var m2) && m2,
                string.Join(", ", rs.Select(kv => kv.Key + " " + kv.Value)));
        }
    }

    // ------------------------------------------------------------ the synthetic old bakes and their writer

    /// <summary>The synthetic old bakes (neutral geometry and texels made here): 73100901 a section piece (version 6, a stored RGBA32
    /// texture), 73100902 a skinned strip (version 5, fill, an RGB24 linear texture, correction rows, FK), 73100903 members (version 6,
    /// o_mnpb and an AIT/mnpb slot), 73100904 a box (version 4).</summary>
    internal static List<OldBake> SyntheticOld()
    {
        var l = new List<OldBake>();
        long ticks = new DateTime(2026, 10, 1, 12, 0, 0).Ticks;
        // 73100901: a piece with a section, an unskinned cube with a stored texture
        {
            var b = new OldBake { Version = 6, Number = 73100901, Name = "Synthetic cube", CharaName = "", Sex = -1, CreatedTicks = ticks, PartsLabel = "piece" };
            Nodes(b, ("cut", -1, V(0, 0, 0)), ("body", 0, V(0, 1, 0)));
            var tex = OldWriter.Texture(16, 8, OldTexture.FormatRgba32, true, false, 1);
            b.TextureHashes.Add(tex.Name);
            b.Textures.Add(new OldTextureRef { Hash = tex.Name, Width = 16, Height = 8, Format = OldTexture.FormatRgba32, Mips = true, Linear = false, WrapU = 1, WrapV = 0, Filter = 1, Aniso = 2 });
            b.Materials.Add(Mat("Synthetic cube (Instance)", "Standard", ("_Glossiness", 0.3f), ("_Metallic", 0.1f)).Vec("_Color", 0.8f, 0.6f, 0.4f, 1f)
                .Slot("_MainTex", OldSlot.ModeStored, 0).Slot("_BumpMap", OldSlot.ModeNone, -1));
            b.Meshes.Add(Cube("cube", 1f));
            b.Renderers.Add(new OldRenderer { Node = 1, Mesh = 0, Materials = new[] { 0 }, Bounds = new[] { 0f, 0f, 0f, 2f, 2f, 2f }, LayerMask = 1, Part = "item", Inside = OldRenderer.InsideSection });
            b.FillInside = false;
            b.InsideColours.Add(new[] { 0.3f, 0.22f, 0.2f, 1f });
            b.SectionColour = new[] { 0.85f, 0.55f, 0.47f, 1f };
            b.OwnInside = false;
            OldWriter.Attach(b, tex);
            l.Add(b);
        }
        // 73100902: a skinned strip, three bones, the middle one bent; a part bake with fill, correction rows, FK
        {
            var b = new OldBake { Version = 5, Number = 73100902, Name = "Synthetic strip", CharaName = "Synthetic", Sex = 1, CreatedTicks = ticks + TimeSpan.TicksPerHour, PartsLabel = "strip" };
            Nodes(b, ("centre", -1, V(0, 0, 0)), ("chaF_900", 0, V(0, 0, 0)), ("b0", 1, V(0, 0, 0)), ("b1", 2, V(0, 2, 0)), ("b2", 3, V(0, 2, 0)), ("strip", 1, V(0, 0, 0)));
            b.Nodes[3].R = Qa(Quaternion.Euler(0f, 0f, 30f));
            b.Nodes[1].Layer = 10;
            b.Fk.AddRange(new[] { "b0", "b1", "b2" });
            var tex = OldWriter.Texture(8, 8, OldTexture.FormatRgb24, true, true, 2);
            b.TextureHashes.Add(tex.Name);
            b.Textures.Add(new OldTextureRef { Name = "strip_n", Hash = tex.Name, Width = 8, Height = 8, Format = OldTexture.FormatRgb24, Mips = true, Linear = true });
            b.Materials.Add(Mat("Synthetic strip", "AIT/Clothes True", ("_Cutoff", 0.5f)).Vec("_Color", 1f, 1f, 1f, 1f).Slot("_BumpMap", OldSlot.ModeStored, 0)
                .Kw("_NORMALMAP").QueueOf(2450, "TransparentCutout"));
            b.Meshes.Add(Strip("strip", 3));
            b.Renderers.Add(new OldRenderer { Node = 5, Skinned = true, Mesh = 0, Materials = new[] { 0 }, Bones = new[] { 2, 3, 4 }, Root = 2, Bounds = new[] { 0f, 3f, 0f, 2f, 8f, 2f }, LayerMask = uint.MaxValue });
            b.CorrectionOn = true;
            b.CorrectionRows.Add(new OldCorrectionRow { Name = "cf_J_ArmUp01_dam_L", Enabled = true, HasCorrect = true, Index = 0, Category = 0 });
            b.CorrectionRows.Add(new OldCorrectionRow { Name = "cf_J_LegKnee_dam_R", Enabled = false, HasCorrect = true, Index = 23, Category = 3 });
            b.FillInside = true;
            b.InsideColours.Add(new[] { 0.3f, 0.22f, 0.2f, 1f });
            OldWriter.Attach(b, tex);
            l.Add(b);
        }
        // 73100903: members: a renderer named o_mnpb, a renderer with an AIT/mnpb slot, a plain one
        {
            var b = new OldBake { Version = 6, Number = 73100903, Name = "Synthetic members", CharaName = "", Sex = -1, CreatedTicks = ticks + 2 * TimeSpan.TicksPerHour, PartsLabel = "" };
            Nodes(b, ("root", -1, V(0, 0, 0)), ("o_mnpb", 0, V(-1.5f, 1, 0)), ("part", 0, V(0, 1, 0)), ("plain", 0, V(1.5f, 1, 0)));
            b.Materials.Add(Mat("plain", "Standard").Vec("_Color", 0.5f, 0.5f, 0.5f, 1f));
            b.Materials.Add(Mat("c_m_mnpb", "AIT/mnpb"));
            b.Meshes.Add(Quad("quad", 1, 0.5f));
            b.Meshes.Add(Quad("quad2", 2, 0.5f));
            b.Renderers.Add(new OldRenderer { Node = 1, Mesh = 0, Materials = new[] { 0 }, Part = "other" });
            b.Renderers.Add(new OldRenderer { Node = 2, Mesh = 1, Materials = new[] { 0, 1 }, Part = "other" });
            b.Renderers.Add(new OldRenderer { Node = 3, Mesh = 0, Materials = new[] { 0 }, Part = "other" });
            b.InsideColours.AddRange(new[] { new[] { 0.3f, 0.22f, 0.2f, 1f }, new[] { 0.3f, 0.22f, 0.2f, 1f } });
            l.Add(b);
        }
        // 73100904: version 4, a box
        {
            var b = new OldBake { Version = 4, Number = 73100904, Name = "Synthetic box", CharaName = "", Sex = -1, CreatedTicks = ticks + 3 * TimeSpan.TicksPerHour };
            Nodes(b, ("root", -1, V(0, 0, 0)), ("box", 0, V(0, 0.5f, 0)));
            b.Materials.Add(Mat("box", "Standard", ("_Glossiness", 0.5f)).Vec("_Color", 0.2f, 0.4f, 0.8f, 1f));
            b.Meshes.Add(Cube("box", 0.5f));
            b.Renderers.Add(new OldRenderer { Node = 1, Mesh = 0, Materials = new[] { 0 } });
            l.Add(b);
        }
        return l;
    }

    static float[] V(float x, float y, float z) => new[] { x, y, z };
    static float[] Qa(Quaternion q) => new[] { q.x, q.y, q.z, q.w };

    static void Nodes(OldBake b, params (string name, int parent, float[] p)[] n)
    {
        foreach (var (name, parent, p) in n) b.Nodes.Add(new OldNode { Name = name, Parent = parent, P = p });
    }

    static OldMaterial Mat(string name, string shader, params (string k, float v)[] floats)
    {
        var m = new OldMaterial { Name = name, Shader = shader, RenderType = "Opaque", Queue = 2000 };
        foreach (var (k, v) in floats) m.Floats.Add(new KeyValuePair<string, float>(k, v));
        return m;
    }

    static OldMaterial Vec(this OldMaterial m, string k, float x, float y, float z, float w) { m.Vectors.Add(new KeyValuePair<string, float[]>(k, new[] { x, y, z, w })); return m; }
    static OldMaterial Slot(this OldMaterial m, string p, int mode, int tex) { m.Slots.Add(new OldSlot { Property = p, Mode = mode, Texture = tex, Scale = new[] { 1f, 1f }, Offset = new[] { 0f, 0f } }); return m; }
    static OldMaterial Kw(this OldMaterial m, string k) { m.Keywords.Add(k); return m; }
    static OldMaterial QueueOf(this OldMaterial m, int q, string rt) { m.Queue = q; m.RenderType = rt; return m; }

    /// <summary>A cube of half size <paramref name="h"/>: 24 vertices, normals, tangents, UV 0 and colours, one triangle list.</summary>
    static OldMesh Cube(string name, float h)
    {
        var m = new OldMesh { Name = name };
        var pos = new List<float>(); var nor = new List<float>(); var tan = new List<float>(); var uv = new List<float>(); var col = new List<byte>(); var idx = new List<int>();
        var axes = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        foreach (var n in axes)
        {
            var u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.Cross(Vector3.up, n).normalized;
            var w = Vector3.Cross(n, u);
            int s = pos.Count / 3;
            for (int k = 0; k < 4; k++)
            {
                float a = (k == 1 || k == 2) ? 1f : -1f, c = k >= 2 ? 1f : -1f;
                var p = (n + u * a + w * c) * h;
                pos.AddRange(new[] { p.x, p.y, p.z }); nor.AddRange(new[] { n.x, n.y, n.z }); tan.AddRange(new[] { u.x, u.y, u.z, 1f });
                uv.AddRange(new[] { (a + 1f) * 0.5f, (c + 1f) * 0.5f, 0f, 0f }); col.AddRange(new byte[] { 255, 255, 255, 255 });
            }
            idx.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
        }
        m.VertexCount = pos.Count / 3; m.Positions = pos.ToArray(); m.Normals = nor.ToArray(); m.Tangents = tan.ToArray(); m.Uv[0] = uv.ToArray(); m.Colors = col.ToArray();
        m.SubMeshes.Add(new OldSubMesh { Topology = (int)MeshTopology.Triangles, Indices = idx.ToArray() });
        m.Bounds = new[] { 0f, 0f, 0f, 2 * h, 2 * h, 2 * h };
        return m;
    }

    /// <summary>A strip along +Y (x ±0.5, y 0..2 × (bones − 1)), skinned on <paramref name="bones"/> bones 2 apart; UV 0 with z set (four
    /// components); bind poses the bones' inverses.</summary>
    static OldMesh Strip(string name, int bones)
    {
        var m = new OldMesh { Name = name };
        int rows = 2 * (bones - 1) * 2 + 1;
        var pos = new List<float>(); var nor = new List<float>(); var uv = new List<float>(); var bi = new List<int>(); var bw = new List<float>(); var idx = new List<int>();
        for (int r = 0; r < rows; r++)
        {
            float y = r * 0.5f;
            float t = y / 2f;
            int b0 = Math.Min(bones - 1, (int)Math.Floor(t)), b1 = Math.Min(bones - 1, b0 + 1);
            float f = Mathf.Clamp01(t - b0);
            float w0 = b0 == b1 ? 1f : 1f - f, w1 = b0 == b1 ? 0f : f;
            for (int c = 0; c < 2; c++)
            {
                pos.AddRange(new[] { c == 0 ? -0.5f : 0.5f, y, 0f }); nor.AddRange(new[] { 0f, 0f, -1f });
                uv.AddRange(new[] { c, y / (rows - 1) * 2f, 0.25f, 0f });
                if (w0 >= w1) { bi.AddRange(new[] { b0, b1, 0, 0 }); bw.AddRange(new[] { w0, w1, 0f, 0f }); }
                else { bi.AddRange(new[] { b1, b0, 0, 0 }); bw.AddRange(new[] { w1, w0, 0f, 0f }); }
            }
            if (r > 0) { int s = 2 * (r - 1); idx.AddRange(new[] { s, s + 2, s + 1, s + 1, s + 2, s + 3 }); }
        }
        m.VertexCount = pos.Count / 3; m.Positions = pos.ToArray(); m.Normals = nor.ToArray(); m.Uv[0] = uv.ToArray();
        m.BoneIndices = bi.ToArray(); m.BoneWeights = bw.ToArray();
        var bp = new float[16 * bones];
        for (int k = 0; k < bones; k++)
        {
            var x = Matrix4x4.Translate(new Vector3(0f, -2f * k, 0f));
            for (int e = 0; e < 16; e++) bp[16 * k + e] = x[e];
        }
        m.BindPoses = bp;
        m.SubMeshes.Add(new OldSubMesh { Topology = (int)MeshTopology.Triangles, Indices = idx.ToArray() });
        m.Bounds = new[] { 0f, (rows - 1) * 0.25f, 0f, 1f, (rows - 1) * 0.5f, 0.1f };
        return m;
    }

    /// <summary><paramref name="quads"/> quads side by side, one sub-mesh each.</summary>
    static OldMesh Quad(string name, int quads, float h)
    {
        var m = new OldMesh { Name = name };
        var pos = new List<float>(); var nor = new List<float>(); var uv = new List<float>();
        for (int q = 0; q < quads; q++)
        {
            float x0 = q * 2 * h;
            foreach (var (x, y) in new[] { (-h, -h), (h, -h), (h, h), (-h, h) })
            {
                pos.AddRange(new[] { x0 + x, y, 0f }); nor.AddRange(new[] { 0f, 0f, -1f }); uv.AddRange(new[] { x > 0 ? 1f : 0f, y > 0 ? 1f : 0f, 0f, 0f });
            }
            int s = 4 * q;
            m.SubMeshes.Add(new OldSubMesh { Topology = (int)MeshTopology.Triangles, Indices = new[] { s, s + 2, s + 1, s, s + 3, s + 2 } });
        }
        m.VertexCount = pos.Count / 3; m.Positions = pos.ToArray(); m.Normals = nor.ToArray(); m.Uv[0] = uv.ToArray();
        m.Bounds = new[] { (quads - 1) * h, 0f, 0f, quads * 2 * h, 2 * h, 0f };
        return m;
    }

    /// <summary>The writer of the old layout (the reader's mirror; tests only). Strings as a 7-bit length and UTF-8, little-endian numbers,
    /// the body and each texture's levels as raw deflate.</summary>
    internal static class OldWriter
    {
        public const int SyntheticCount = 4, MemberNumber = 73100903, SyntheticLastNumber = 73100905;
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<OldBake, List<TexFile>> textures = new System.Runtime.CompilerServices.ConditionalWeakTable<OldBake, List<TexFile>>();

        /// <summary>A texture file made here: its name (the hash), its header fields and its levels as stored.</summary>
        public sealed class TexFile
        {
            public string Name;
            public int W, H, Format;
            public bool Mips, Linear;
            public List<byte[]> Levels = new List<byte[]>();
        }

        public static void Attach(OldBake b, TexFile t) => textures.GetOrCreateValue(b).Add(t);

        /// <summary>A texture of a numeric pattern (seeded), every level, hashed as the old writer named its files.</summary>
        public static TexFile Texture(int w, int h, int format, bool mips, bool linear, int seed)
        {
            var t = new TexFile { W = w, H = h, Format = format, Mips = mips, Linear = linear };
            int ch = format == OldTexture.FormatRgb24 ? 3 : 4;
            int lw = w, lh = h, level = 0;
            while (true)
            {
                var d = new byte[lw * lh * ch];
                for (int y = 0; y < lh; y++)
                    for (int x = 0; x < lw; x++)
                    {
                        int o = (y * lw + x) * ch;
                        d[o] = (byte)(x * 16 + seed * 7 + level); d[o + 1] = (byte)(y * 32 + seed); d[o + 2] = (byte)((x + y) * 8 + level * 20);
                        if (ch == 4) d[o + 3] = (byte)(200 + level);
                    }
                t.Levels.Add(d);
                if (!mips || (lw == 1 && lh == 1)) break;
                lw = Math.Max(1, lw >> 1); lh = Math.Max(1, lh >> 1); level++;
            }
            using (var md5 = MD5.Create())
            {
                var head = new OldTexture { Width = w, Height = h, Format = format, Mips = mips, Linear = linear };
                var pre = Encoding.ASCII.GetBytes(head.HashPrefix);
                md5.TransformBlock(pre, 0, pre.Length, null, 0);
                foreach (var l in t.Levels) md5.TransformBlock(l, 0, l.Length, null, 0);
                md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                t.Name = OldBakeReader.Hex(md5.Hash);
            }
            return t;
        }

        public static string FileName(OldBake b) => b.Number.ToString(CultureInfo.InvariantCulture) + "_" + b.Name.Replace(' ', '_').ToLowerInvariant() + OldBakeReader.Extension;

        /// <summary>Every file of the synthetic folder: the bakes, their texture files, last_no.txt.</summary>
        public static IEnumerable<(string name, byte[] bytes)> Files(List<OldBake> models)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in models)
            {
                yield return (FileName(b), Bake(b));
                if (textures.TryGetValue(b, out var ts))
                    foreach (var t in ts)
                        if (seen.Add(t.Name)) yield return (OldBakeReader.TextureFolderName + "/" + t.Name + OldBakeReader.TextureExtension, TextureFile(t));
            }
            yield return (OldBakeReader.LastNumberName, Encoding.ASCII.GetBytes(SyntheticLastNumber.ToString(CultureInfo.InvariantCulture)));
        }

        public static byte[] TextureFile(TexFile t)
        {
            using (var o = new MemoryStream())
            {
                var w = new BinaryWriter(o, new UTF8Encoding(false));
                w.Write(OldBake.TextureMagic); w.Write(t.W); w.Write(t.H); w.Write(t.Format); w.Write(t.Mips); w.Write(t.Linear);
                w.Write(t.Levels.Sum(l => l.Length));
                w.Flush();
                using (var z = new DeflateStream(o, System.IO.Compression.CompressionLevel.Optimal, true)) foreach (var l in t.Levels) z.Write(l, 0, l.Length);
                return o.ToArray();
            }
        }

        public static int HeaderLength(OldBake b) => Header(b).Length;

        static byte[] Header(OldBake b, int magic = OldBake.Magic)
        {
            using (var o = new MemoryStream())
            {
                var w = new BinaryWriter(o, new UTF8Encoding(false));
                w.Write(magic); w.Write(b.Version); w.Write(b.Number); w.Write(b.Name); w.Write(b.CharaName); w.Write(b.Sex); w.Write(b.CreatedTicks);
                Strs(w, b.Fk); Strs(w, b.TextureHashes);
                if (b.Version >= 5) w.Write(b.PartsLabel);
                w.Flush();
                return o.ToArray();
            }
        }

        static void Strs(BinaryWriter w, List<string> l) { w.Write(l.Count); foreach (var s in l) w.Write(s); }
        static void Fs(BinaryWriter w, float[] a) { foreach (var f in a) w.Write(f); }
        static void Is(BinaryWriter w, int[] a) { w.Write(a.Length); foreach (var i in a) w.Write(i); }

        /// <summary>The body as the old writer laid it out (uncompressed).</summary>
        public static byte[] Body(OldBake b, bool closing = true, int extra = 0, bool badParent = false)
        {
            using (var o = new MemoryStream())
            {
                var w = new BinaryWriter(o, new UTF8Encoding(false));
                w.Write(b.Nodes.Count);
                for (int i = 0; i < b.Nodes.Count; i++)
                {
                    var n = b.Nodes[i];
                    w.Write(n.Name); w.Write(badParent && i == 1 ? 1 : n.Parent); Fs(w, n.P); Fs(w, n.R); Fs(w, n.S); w.Write(n.Active); w.Write(n.Layer);
                }
                w.Write(b.Sources.Count);
                foreach (var s in b.Sources) { w.Write(s.Bundle); w.Write(s.Asset); w.Write(s.Manifest); w.Write(s.Kind); }
                w.Write(b.Textures.Count);
                foreach (var t in b.Textures)
                {
                    w.Write(t.Name); w.Write(t.Hash); w.Write(t.Source); w.Write(t.ListBundle); w.Write(t.ListManifest); w.Write(t.Width); w.Write(t.Height); w.Write(t.Format);
                    w.Write(t.Mips); w.Write(t.Linear); w.Write(t.WrapU); w.Write(t.WrapV); w.Write(t.Filter); w.Write(t.Aniso); w.Write(t.MipBias);
                }
                w.Write(b.Materials.Count);
                foreach (var m in b.Materials)
                {
                    w.Write(m.Name); w.Write(m.Shader); w.Write(m.RenderType); w.Write(m.Queue); Strs(w, m.Keywords); Strs(w, m.PassesOff);
                    w.Write(m.Base); w.Write(m.BasePath); w.Write(m.BaseSlot); w.Write(m.BaseName); w.Write(m.ShaderSource);
                    w.Write(m.Floats.Count); foreach (var kv in m.Floats) { w.Write(kv.Key); w.Write(kv.Value); }
                    w.Write(m.Vectors.Count); foreach (var kv in m.Vectors) { w.Write(kv.Key); Fs(w, kv.Value); }
                    w.Write(m.Slots.Count);
                    foreach (var s in m.Slots) { w.Write(s.Property); Fs(w, s.Scale); Fs(w, s.Offset); w.Write(s.Mode); w.Write(s.Texture); }
                }
                w.Write(b.Meshes.Count);
                foreach (var m in b.Meshes)
                {
                    w.Write(m.Name); w.Write(m.Source); w.Write(m.SourcePath);
                    if (!m.Stored) continue;
                    w.Write(m.VertexCount); Fs(w, m.Positions);
                    w.Write(m.Normals != null); if (m.Normals != null) Fs(w, m.Normals);
                    w.Write(m.Tangents != null); if (m.Tangents != null) Fs(w, m.Tangents);
                    for (int ch = 0; ch < 4; ch++) { w.Write(m.Uv[ch] != null); if (m.Uv[ch] != null) Fs(w, m.Uv[ch]); }
                    w.Write(m.Colors != null); if (m.Colors != null) w.Write(m.Colors);
                    w.Write(m.BoneIndices != null);
                    if (m.BoneIndices != null)
                        for (int k = 0; k < m.VertexCount; k++)
                        {
                            for (int j = 0; j < 4; j++) w.Write(m.BoneIndices[4 * k + j]);
                            for (int j = 0; j < 4; j++) w.Write(m.BoneWeights[4 * k + j]);
                        }
                    w.Write(m.BindPoseCount); Fs(w, m.BindPoses);
                    w.Write(m.SubMeshes.Count);
                    foreach (var s in m.SubMeshes) { w.Write(s.Topology); Is(w, s.Indices); }
                    Fs(w, m.Bounds);
                }
                w.Write(b.Renderers.Count);
                foreach (var r in b.Renderers)
                {
                    w.Write(r.Node); w.Write(r.Skinned); w.Write(r.Mesh); Is(w, r.Materials); Is(w, r.Bones); w.Write(r.Root); Fs(w, r.Bounds);
                    w.Write(r.Quality); w.Write(r.Shadow); w.Write(r.SortingOrder); w.Write(r.UpdateOffscreen); w.Write(r.Receive);
                    w.Write(r.Anchor); w.Write(r.LightProbe); w.Write(r.ReflProbe); w.Write(r.LayerMask);
                    w.Write(r.BlendWeights.Length); Fs(w, r.BlendWeights);
                }
                w.Write(b.CorrectionOn);
                w.Write(b.CorrectionRows.Count);
                foreach (var c in b.CorrectionRows)
                {
                    w.Write(c.Name); w.Write(c.Enabled); w.Write(c.HasLookAt); w.Write(c.HasCorrect); w.Write(c.Index); w.Write(c.Category);
                    w.Write(""); w.Write(""); w.Write("");
                    for (int k = 0; k < 5; k++) w.Write(0);
                    w.Write(0f); w.Write(0f);
                    w.Write(c.HasCorrect ? c.Name : ""); w.Write(""); w.Write(0); w.Write(0); w.Write(0f);
                    for (int k = 0; k < 3; k++) { w.Write(false); w.Write(0f); w.Write(0f); }
                }
                if (b.Version >= 5)
                {
                    w.Write(b.FillInside);
                    w.Write(b.InsideColours.Count); foreach (var c in b.InsideColours) Fs(w, c);
                }
                if (b.Version >= 6)
                {
                    w.Write(b.Renderers.Count);
                    foreach (var r in b.Renderers) { w.Write(r.Part); w.Write(r.Inside); }
                    Fs(w, b.SectionColour); w.Write(b.OwnInside);
                }
                if (closing) w.Write(OldBake.Magic);
                for (int k = 0; k < extra; k++) w.Write((byte)k);
                w.Flush();
                return o.ToArray();
            }
        }

        /// <summary>A whole old bake file: the header, then the body as raw deflate.</summary>
        public static byte[] Bake(OldBake b, bool closing = true, int extra = 0, bool badParent = false)
        {
            using (var o = new MemoryStream())
            {
                var h = Header(b);
                o.Write(h, 0, h.Length);
                var body = Body(b, closing, extra, badParent);
                using (var z = new DeflateStream(o, System.IO.Compression.CompressionLevel.Optimal, true)) z.Write(body, 0, body.Length);
                return o.ToArray();
            }
        }

        /// <summary>Two files hold the same content: a bake's header and inflated body, a texture's header and inflated levels, or the same bytes.</summary>
        public static bool SameContent(byte[] a, byte[] b)
        {
            if (a.SequenceEqual(b)) return true;
            int head(byte[] x)
            {
                if (x.Length >= 4 && BitConverter.ToInt32(x, 0) == OldBake.TextureMagic) return 22;
                var r = OldBakeReader.Read(x, "", null);
                return r == null ? -1 : Header(r).Length;
            }
            int ha = head(a), hb = head(b);
            if (ha < 0 || ha != hb || !a.Take(ha).SequenceEqual(b.Take(hb))) return false;
            return Inflate(a, ha).SequenceEqual(Inflate(b, hb));
        }

        static byte[] Inflate(byte[] x, int from)
        {
            using (var s = new MemoryStream(x, from, x.Length - from, false))
            using (var z = new DeflateStream(s, CompressionMode.Decompress))
            using (var o = new MemoryStream()) { z.CopyTo(o); return o.ToArray(); }
        }

        static string R(float f) => f.ToString("R", CultureInfo.InvariantCulture);
        static string Rs(IEnumerable<float> a) => a == null ? "null" : string.Join(",", a.Select(R));

        /// <summary>Every member of an old bake as text (the round trip's comparison).</summary>
        public static string Dump(OldBake b)
        {
            var s = new StringBuilder();
            s.Append($"v{b.Version} #{b.Number} [{b.Name}] [{b.CharaName}] sex {b.Sex} ticks {b.CreatedTicks} fk [{string.Join(",", b.Fk)}] th [{string.Join(",", b.TextureHashes)}] parts [{b.PartsLabel}]\n");
            foreach (var n in b.Nodes) s.Append($"n {n.Name} {n.Parent} {Rs(n.P)} {Rs(n.R)} {Rs(n.S)} {n.Active} {n.Layer}\n");
            foreach (var x in b.Sources) s.Append($"s {x.Bundle} {x.Asset} {x.Manifest} {x.Kind}\n");
            foreach (var t in b.Textures) s.Append($"t {t.Name} {t.Hash} {t.Source} {t.ListBundle} {t.ListManifest} {t.Width} {t.Height} {t.Format} {t.Mips} {t.Linear} {t.WrapU} {t.WrapV} {t.Filter} {t.Aniso} {R(t.MipBias)}\n");
            foreach (var m in b.Materials)
            {
                s.Append($"m {m.Name} {m.Shader} {m.RenderType} {m.Queue} [{string.Join(",", m.Keywords)}] [{string.Join(",", m.PassesOff)}] {m.Base} {m.BasePath} {m.BaseSlot} {m.BaseName} {m.ShaderSource}\n");
                foreach (var kv in m.Floats) s.Append($"  f {kv.Key} {R(kv.Value)}\n");
                foreach (var kv in m.Vectors) s.Append($"  v {kv.Key} {Rs(kv.Value)}\n");
                foreach (var x in m.Slots) s.Append($"  t {x.Property} {Rs(x.Scale)} {Rs(x.Offset)} {x.Mode} {x.Texture}\n");
            }
            foreach (var m in b.Meshes)
            {
                s.Append($"g {m.Name} {m.Source} {m.SourcePath} {m.VertexCount} p {Rs(m.Positions)} n {Rs(m.Normals)} t {Rs(m.Tangents)}");
                for (int ch = 0; ch < 4; ch++) s.Append($" uv{ch} {Rs(m.Uv[ch])}");
                s.Append($" c {(m.Colors == null ? "null" : string.Join(",", m.Colors))} bi {(m.BoneIndices == null ? "null" : string.Join(",", m.BoneIndices))} bw {Rs(m.BoneWeights)} bp {Rs(m.BindPoses)} b {Rs(m.Bounds)}\n");
                foreach (var x in m.SubMeshes) s.Append($"  sub {x.Topology} {string.Join(",", x.Indices)}\n");
            }
            foreach (var r in b.Renderers)
                s.Append($"r {r.Node} {r.Skinned} {r.Mesh} [{string.Join(",", r.Materials)}] [{string.Join(",", r.Bones)}] {r.Root} {Rs(r.Bounds)} {r.Quality} {r.Shadow} {r.SortingOrder} {r.UpdateOffscreen} {r.Receive} {r.Anchor} {r.LightProbe} {r.ReflProbe} {r.LayerMask} [{Rs(r.BlendWeights)}] {r.Part} {r.Inside}\n");
            s.Append($"corr {b.CorrectionOn}\n");
            foreach (var c in b.CorrectionRows) s.Append($"  {c.Name} {c.Enabled} {c.HasLookAt} {c.HasCorrect} {c.Index} {c.Category}\n");
            s.Append($"fill {b.FillInside} [{string.Join(";", b.InsideColours.Select(Rs))}] section {Rs(b.SectionColour)} own {b.OwnInside}\n");
            return s.ToString();
        }
    }
}

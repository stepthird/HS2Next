// The store of bakes (`<UserData>/bakes/`, index.json and blobs/). BF4 writes are atomic (a stop between the blobs and the index leaves only blobs no row lists, which
// a purge removes), numbers are never given again after a delete or a purge, a purge keeps every blob a live row lists while other rows' files are moved away (the
// set to keep is the index's, not the files present) and waits while the store is busy; renames, deletes and restores write the index only; a blob whose
// content differs from its name is not used. BF9 an unreadable index.json reads as an empty store, the file is kept as it was and copied to index.json.bad by the
// next write, and a purge is refused until then. Every store here lives under the run's temporary folder. Synthetic blobs only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Scenes.Bake;

namespace HS2Next.Save.Tests
{
    public static class DocTests_BakeStore
    {
        static string Id(int n) => n.ToString("x32");
        static bool Same(byte[] a, byte[] b) => a != null && b != null && a.SequenceEqual(b);

        static string Fresh(TestRunner t, string name)
        {
            string dir = Path.Combine(t.TempDir ?? Path.GetTempPath(), "bakestore_" + name);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            return dir;
        }

        static BakeStoreRow RowOf(DocTests_Bake.HandBake h, string name) => new BakeStoreRow
        {
            Id = h.MainDoc.Id, Name = name, Created = DocTests_Bake.Clock, Kind = h.MainDoc.Kind, Main = BakeBlob.Sha256(h.Main), Data = h.MainDoc.DataBlobs(),
            Parts = "clothes0", Fk = new List<string>(h.MainDoc.Fk),
        };

        static string[] BlobFiles(BakeStore s) => Directory.Exists(s.BlobsPath) ? Directory.GetFiles(s.BlobsPath).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray() : new string[0];

        public static void Run(TestRunner t)
        {
            BF4(t);
            BF9(t);
        }

        // ---------------- BF4 ----------------

        static void BF4(TestRunner t)
        {
            // Nothing exists until the first write.
            string dir = Fresh(t, "bf4");
            var findings = new FindingList();
            var s = BakeStore.Open(dir, findings);
            t.Check(!Directory.Exists(dir) && findings.Count == 0 && s.LiveRows().Count == 0 && s.LastNumber == BakeStore.FirstNumber - 1 && !s.Unreadable,
                "BF4: a store that does not exist reads as empty, silently, and makes no folder");

            var a = DocTests_Bake.MakeBake(Id(0xc1), 21);
            var b = DocTests_Bake.MakeBake(Id(0xc2), 22);
            var c = DocTests_Bake.MakeBake(Id(0xc3), 23);
            var ra = s.Add(RowOf(a, "A"), a.ByName());
            t.Check(ra.Number == BakeStore.FirstNumber && ra.Bytes == a.Main.Length + a.Mesh.Length + a.Texture.Length && !ra.Deleted, "BF4: the first row is " + BakeStore.FirstNumber + ", its bytes the blobs' (" + ra.Bytes + ")");
            t.Check(File.Exists(s.IndexPath) && BlobFiles(s).Length == 3, "BF4: index.json and three blobs");
            foreach (var kv in a.ByName()) t.Check(Same(File.ReadAllBytes(Path.Combine(s.BlobsPath, kv.Key + ".bin")), kv.Value), "BF4: blobs/" + kv.Key.Substring(0, 8) + "....bin holds the scene's bytes");
            // A row sharing a blob with another: the shared blob is not written again.
            var shared = new BakeStoreRow { Id = Id(0xc4), Name = "A again", Created = DocTests_Bake.Clock, Kind = "parts", Main = BakeBlob.Sha256(b.Main), Data = new List<string> { BakeBlob.Sha256(a.Mesh), BakeBlob.Sha256(b.Texture) } };
            var stamp = File.GetLastWriteTimeUtc(Path.Combine(s.BlobsPath, BakeBlob.Sha256(a.Mesh) + ".bin"));
            var rs = s.Add(shared, new[] { new KeyValuePair<string, byte[]>(BakeBlob.Sha256(b.Main), b.Main), new KeyValuePair<string, byte[]>(BakeBlob.Sha256(b.Texture), b.Texture) });
            t.Check(rs.Number == ra.Number + 1 && BlobFiles(s).Length == 5 && File.GetLastWriteTimeUtc(Path.Combine(s.BlobsPath, BakeBlob.Sha256(a.Mesh) + ".bin")) == stamp,
                "BF4: a row sharing a held blob: 5 blobs, the shared one untouched");
            t.Throws<ArgumentException>(() => s.Add(RowOf(c, "C"), new KeyValuePair<string, byte[]>[0]), "BF4: a row whose blobs are neither given nor held is refused");
            t.Throws<ArgumentException>(() => s.Add(RowOf(c, "C"), new[] { new KeyValuePair<string, byte[]>(BakeBlob.Sha256(c.Main), c.Mesh) }), "BF4: a blob that does not hold what its name says is refused");
            t.Check(s.AllRows().Count == 2 && BlobFiles(s).Length == 5, "BF4: refusals leave the store as it was");

            // A stop between the blobs and the index: the blobs are there, the index is not changed; a purge removes them.
            var indexBefore = File.ReadAllBytes(s.IndexPath);
            s.BeforeIndexWrite = () => throw new IOException("stopped by the test");
            var stop = t.Throws<BakeStoreException>(() => s.Add(RowOf(c, "C"), c.ByName()), "BF4: a stop between the blobs and the index");
            s.BeforeIndexWrite = null;
            t.Check(stop != null && stop.Code == BakeFindings.StoreWrite, "BF4: reported as bake.storeWrite");
            t.Check(Same(indexBefore, File.ReadAllBytes(s.IndexPath)) && s.AllRows().Count == 2 && s.LastNumber == rs.Number, "BF4: the index on disk and in memory as before");
            t.Check(BlobFiles(s).Length == 8 && c.ByName().All(kv => File.Exists(Path.Combine(s.BlobsPath, kv.Key + ".bin"))), "BF4: the three new blobs are there with no row");
            var reread = BakeStore.Open(dir);
            t.Check(reread.AllRows().Count == 2 && reread.LastNumber == rs.Number, "BF4: a fresh read sees the two rows");
            var preview = s.PurgePreview();
            var p1 = s.Purge();
            t.Info("BF4: the purge after the stop removed " + p1.Blobs + " orphan blobs (" + p1.Bytes + " bytes), " + p1.Rows + " rows");
            t.Check(p1.Blobs >= 1, "BF4: the purge removed at least one orphan");
            t.Check(p1.Blobs == 3 && p1.Rows == 0 && p1.Bytes == c.Main.Length + c.Mesh.Length + c.Texture.Length && BlobFiles(s).Length == 5, "BF4: exactly the three orphans");
            t.Check(preview.Blobs == p1.Blobs && preview.Bytes == p1.Bytes && preview.Rows == p1.Rows, "BF4: the preview said so");

            // Numbers are never given again.
            var rc = s.Add(RowOf(c, "C"), c.ByName());
            t.Equal(rs.Number + 1, rc.Number, "BF4: the next row takes the next number");
            t.Check(s.Delete(rc.Number) && s.LiveRows().Count == 2 && s.AllRows().Count == 3 && s.AllRows().Last().Deleted, "BF4: a delete marks the row");
            t.Check(BlobFiles(s).Length == 8, "BF4: and touches no blob");
            var d = DocTests_Bake.MakeBake(Id(0xc5), 24);
            var rd = s.Add(RowOf(d, "D"), d.ByName());
            t.Equal(rc.Number + 1, rd.Number, "BF4: a deleted row's number is not given again");
            t.Check(s.Delete(rd.Number) && s.Delete(rd.Number, restore: true) && !s.Find(rd.Number).Deleted, "BF4: restore clears the mark");
            s.Delete(rd.Number);
            var p2 = s.Purge();
            t.Check(p2.Rows == 2 && s.AllRows().Count == 2 && s.Find(rc.Number) == null, "BF4: a purge removes the deleted rows (" + p2.Rows + ")");
            t.Check(p2.Blobs == 6 && BlobFiles(s).Length == 5, "BF4: and the blobs no remaining row lists (" + p2.Blobs + ")");
            var e = DocTests_Bake.MakeBake(Id(0xc6), 25);
            t.Equal(rd.Number + 1, s.Add(RowOf(e, "E"), e.ByName()).Number, "BF4: a purged row's number is not given again either");
            t.Equal(rd.Number + 1, BakeStore.Open(dir).LastNumber, "BF4: lastNumber read back");
            t.Equal(73100500, s.Add(RowOf(c, "C kept"), c.ByName(), 73100500).Number, "BF4: a row may take a free number (the importer keeps the old ones)");
            t.Throws<ArgumentException>(() => s.Add(RowOf(c, "C twice"), c.ByName(), 73100500), "BF4: not a taken one");
            s.RaiseLastNumber(73100600);
            t.Equal(73100601, s.Add(RowOf(d, "D"), d.ByName()).Number, "BF4: the last number raised");
            s.RaiseLastNumber(73100001);
            t.Equal(73100601, s.LastNumber, "BF4: and never lowered");

            // V19 replayed: rows whose files are moved away keep their blobs through a purge.
            string v19 = Fresh(t, "v19");
            var st = BakeStore.Open(v19);
            var x = DocTests_Bake.MakeBake(Id(0xd1), 31);
            var y = DocTests_Bake.MakeBake(Id(0xd2), 32);
            var z = DocTests_Bake.MakeBake(Id(0xd3), 33);
            var rx = st.Add(RowOf(x, "X"), x.ByName());
            var ry = st.Add(new BakeStoreRow { Id = Id(0xd2), Name = "Y", Kind = "parts", Main = BakeBlob.Sha256(y.Main), Data = new List<string> { BakeBlob.Sha256(y.Mesh), BakeBlob.Sha256(x.Texture) } },
                new[] { new KeyValuePair<string, byte[]>(BakeBlob.Sha256(y.Main), y.Main), new KeyValuePair<string, byte[]>(BakeBlob.Sha256(y.Mesh), y.Mesh) });
            var rz = st.Add(RowOf(z, "Z"), z.ByName());
            string aside = Path.Combine(v19, "aside");
            Directory.CreateDirectory(aside);
            var own = new[] { BakeBlob.Sha256(x.Main), BakeBlob.Sha256(x.Mesh) };
            foreach (var sha in own) File.Move(Path.Combine(st.BlobsPath, sha + ".bin"), Path.Combine(aside, sha + ".bin"));
            st.Delete(rz.Number);
            var held = BlobFiles(st);
            var pv = st.Purge();
            var left = BlobFiles(st);
            t.Info("BF4 (V19): X's own 2 blob files moved away (its texture, which Y shares, left in place), Z deleted; the purge removed " + pv.Blobs + " blobs, " + pv.Rows + " row; " + left.Length + " blob files left of " + held.Length);
            t.Check(ry.Blobs().All(sha => left.Contains(sha + ".bin")), "BF4 (V19): every blob of the live row Y is kept, the texture it shares with X too");
            t.Check(rx.Blobs().Where(sha => held.Contains(sha + ".bin")).All(sha => left.Contains(sha + ".bin")), "BF4 (V19): every blob of X still present is kept");
            t.Check(pv.Blobs == 3 && rz.Blobs().All(sha => !left.Contains(sha + ".bin")), "BF4 (V19): the deleted row Z's three blobs are removed");
            foreach (var sha in own) File.Move(Path.Combine(aside, sha + ".bin"), Path.Combine(st.BlobsPath, sha + ".bin"));
            t.Check(!st.IsDamaged(st.Find(rx.Number)) && !st.IsDamaged(st.Find(ry.Number)), "BF4 (V19): X's files moved back: both rows whole");
            t.Check(st.Supply(Id(0xd1), BakeBlob.Sha256(x.Main)) != null && st.Supply(Id(0xd9), BakeBlob.Sha256(x.Main)) == null, "BF4: a blob is supplied for a bake the store holds, not for another");

            // Busy, rename, damage.
            using (st.Hold())
            {
                var busy = t.Throws<BakeStoreException>(() => st.Purge(), "BF4: a purge while a bake is being written");
                t.Check(busy != null && busy.Code == BakeFindings.Busy && st.Busy, "BF4: is refused, bake.busy");
            }
            t.Check(!st.Busy && st.Purge().Blobs == 0, "BF4: and runs after it");
            var blobsBefore = BlobFiles(st).Select(n => File.GetLastWriteTimeUtc(Path.Combine(st.BlobsPath, n))).ToArray();
            t.Equal("X", st.Rename(rx.Number, "X renamed"), "BF4: a rename returns the old name");
            t.Check(st.Find(rx.Number).Name == "X renamed" && BakeStore.Open(v19).Find(rx.Number).Name == "X renamed", "BF4: and writes the index");
            t.Check(BlobFiles(st).Select(n => File.GetLastWriteTimeUtc(Path.Combine(st.BlobsPath, n))).SequenceEqual(blobsBefore), "BF4: the index only");
            t.Check(st.Rename(999, "nope") == null && !st.Delete(999), "BF4: a row that does not exist");
            string mainPath = Path.Combine(st.BlobsPath, BakeBlob.Sha256(x.Main) + ".bin");
            var good = File.ReadAllBytes(mainPath);
            var bad = (byte[])good.Clone();
            bad[bad.Length - 1] ^= 1;
            File.WriteAllBytes(mainPath, bad);
            var fd = new FindingList();
            t.Check(st.ReadBlob(BakeBlob.Sha256(x.Main), fd) == null && fd.CountOf(BakeFindings.StoreCorrupt) == 1 && st.IsDamaged(st.Find(rx.Number)), "BF4: a blob that does not hold what its name says is not used, bake.storeCorrupt, its row damaged");
            File.WriteAllBytes(mainPath, good);
            // A crash's temporary files and files of others.
            File.WriteAllBytes(Path.Combine(st.BlobsPath, BakeBlob.Sha256(x.Main) + ".bin.tmp-0123"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(st.BlobsPath, "readme.txt"), new byte[] { 2 });
            var p3 = st.Purge();
            t.Check(p3.Leftovers == 1 && File.Exists(Path.Combine(st.BlobsPath, "readme.txt")) && !File.Exists(Path.Combine(st.BlobsPath, BakeBlob.Sha256(x.Main) + ".bin.tmp-0123")),
                "BF4: a purge removes a stopped write's temporary file and leaves a file the store did not write");
            // Rows: copies; the item block of a row.
            var copy = st.Find(rx.Number);
            copy.Name = "changed";
            t.Check(st.Find(rx.Number).Name == "X renamed", "BF4: rows given out are copies");
            var block = st.Find(rx.Number).ToItemBake();
            t.Check(block.Id == Id(0xd1) && block.No == rx.Number && block.Main == ResStore.NameOf(x.Main) && block.Data.Count == 2, "BF4: a row's item block");
            t.Check(st.FindById(Id(0xd2)).Number == ry.Number && st.FindById(Id(0xff)) == null, "BF4: rows by id");
            var fk = st.Find(rx.Number).Fk;
            t.Check(fk.SequenceEqual(x.MainDoc.Fk), "BF4: a row keeps its FK list: " + string.Join(", ", fk));
            // The renames of a scene's items (spec 7.4).
            var items = new List<(string, string, string)> { ("n1", "X", Id(0xd1)), ("n2", "X", Id(0xd2)), ("n3", "X", Id(0xd1)), ("n4", "Other", Id(0xd1)), ("n5", "X", null) };
            t.Equal("n1,n3", string.Join(",", BakeStore.RenameTargets(items, Id(0xd1), "X")), "BF4: a rename renames the items of that bake that carry the old name");
        }

        // ---------------- BF9 ----------------

        static void BF9(TestRunner t)
        {
            string dir = Fresh(t, "bf9");
            var s = BakeStore.Open(dir);
            var a = DocTests_Bake.MakeBake(Id(0xe1), 41);
            s.Add(RowOf(a, "A"), a.ByName());
            var broken = Encoding.UTF8.GetBytes("{ \"format\": \"hs2next.bakeStore\", \"version\": 1, \"lastNumber\": 73100001, \"rows\": [ { \"number\": ");
            File.WriteAllBytes(s.IndexPath, broken);
            t.Check(File.Exists(s.IndexPath), "BF9: the broken file exists (" + broken.Length + " bytes)");
            var f = new FindingList();
            var u = BakeStore.Open(dir, f);
            t.Check(u.Unreadable && u.AllRows().Count == 0 && u.LastNumber == BakeStore.FirstNumber - 1, "BF9: an unreadable index.json is an empty store");
            t.Check(f.Count == 1 && f[0].Code == BakeFindings.StoreUnreadable && f[0].Severity == FindingSeverity.Warning, "BF9: with the warning bake.storeUnreadable (" + f + ")");
            t.Check(Same(broken, File.ReadAllBytes(u.IndexPath)) && !File.Exists(Path.Combine(dir, BakeStore.BadName)), "BF9: the file is kept as it was until the next write");
            var refused = t.Throws<BakeStoreException>(() => u.Purge(), "BF9: a purge of a store whose index did not read");
            t.Check(refused != null && refused.Code == BakeFindings.StoreUnreadable && File.Exists(Path.Combine(u.BlobsPath, BakeBlob.Sha256(a.Main) + ".bin")), "BF9: is refused, nothing removed");
            var b = DocTests_Bake.MakeBake(Id(0xe2), 42);
            var rb = u.Add(RowOf(b, "B"), b.ByName());
            t.Check(Same(broken, File.ReadAllBytes(Path.Combine(dir, BakeStore.BadName))), "BF9: the next write copies it to index.json.bad first");
            t.Check(!u.Unreadable && BakeStore.Open(dir).AllRows().Count == 1 && rb.Number == BakeStore.FirstNumber, "BF9: and writes a readable index with the new row");
            // Other unreadable forms: another format, a newer version, a missing index in a store folder.
            foreach (var (what, text) in new[] { ("another format", "{\"format\": \"hs2next.scene\", \"version\": 1, \"lastNumber\": 1, \"rows\": []}"),
                ("a newer version", "{\"format\": \"hs2next.bakeStore\", \"version\": 2, \"lastNumber\": 1, \"rows\": []}"),
                ("a wrong type", "{\"format\": \"hs2next.bakeStore\", \"version\": 1, \"lastNumber\": \"x\", \"rows\": []}") })
            {
                File.WriteAllText(u.IndexPath, text);
                var fx = new FindingList();
                t.Check(BakeStore.Open(dir, fx).Unreadable && fx.CountOf(BakeFindings.StoreUnreadable) == 1, "BF9: " + what + ": unreadable (" + fx + ")");
            }
            File.Delete(u.IndexPath);
            var fm = new FindingList();
            t.Check(BakeStore.Open(dir, fm).Unreadable && fm.CountOf(BakeFindings.StoreUnreadable) == 1, "BF9: a store folder without index.json: unreadable (" + fm + ")");
            // A readable index with unknown members keeps them.
            string dir2 = Fresh(t, "bf9b");
            var k = BakeStore.Open(dir2);
            k.Add(RowOf(a, "A"), a.ByName());
            var text2 = File.ReadAllText(k.IndexPath).Replace("\"deleted\": false", "\"deleted\": false,\n      \"later\": 1");
            File.WriteAllText(k.IndexPath, text2);
            var k2 = BakeStore.Open(dir2);
            k2.Rename(BakeStore.FirstNumber, "A2");
            t.Check(File.ReadAllText(k2.IndexPath).Contains("\"later\": 1") && !k2.Unreadable, "BF9: an unknown member of a row is kept through a write");
        }
    }
}

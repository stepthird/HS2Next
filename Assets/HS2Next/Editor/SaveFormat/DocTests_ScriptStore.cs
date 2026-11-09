// The store of scene scripts (<UserData>/scripts/: index.json and <no>.hs2nscript per row). JF5: the script's file is written before the index and a stop between
// them leaves only a file no row lists (the index unchanged on disk and in memory, script.storeWrite), numbers are never given again after a delete and a purge,
// `replace` keeps a row's number while an import without it copies the script under a new id and number, a delete hides a row and a restore shows it, a purge
// removes exactly the files the hidden rows of a readable index name (a file no row names, a leftover temporary file and every other file stay), an unreadable or
// absent index reads as an empty store (script.storeUnreadable), is kept and copied to index.json.bad at the next write, and a purge removes nothing until then and
// nothing the new index does not name after it; the row limit. Synthetic scripts only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Scenes.Scripts;

namespace HS2Next.Save.Tests
{
    public static class DocTests_ScriptStore
    {
        static readonly DateTime T0 = new DateTime(2026, 11, 8, 1, 0, 0, DateTimeKind.Utc);

        static string Fresh(TestRunner t, string name)
        {
            string dir = Path.Combine(t.TempDir ?? Path.GetTempPath(), "script_store_" + name);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            return dir;
        }

        static ScriptStore Open(string dir, FindingList f = null)
        {
            var s = ScriptStore.Open(dir, f);
            int tick = 0;
            s.Clock = () => T0.AddSeconds(tick++);
            int n = 0;
            s.NewId = () => DocTests_Script.Id(0x9000 + ++n);
            return s;
        }

        static SceneScript Named(string name, int id)
        {
            var s = DocTests_Script.WalkScript();
            s.Id = DocTests_Script.Id(id);
            s.Name = name;
            return s;
        }

        static string Files(string dir) => Directory.Exists(dir) ? string.Join(",", Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal)) : "";

        public static void Run(TestRunner t)
        {
            JF5(t);
        }

        static void JF5(TestRunner t)
        {
            // A new store: nothing exists before the first write; numbers from 1; the file is the document.
            string dir = Fresh(t, "jf5");
            var s = Open(dir);
            t.Check(!Directory.Exists(dir) && s.LastNumber == 0 && s.Count == 0 && !s.Unreadable, "JF5: a new store creates nothing and reads as empty");
            var r1 = s.Add(Named("One", 0xa1));
            var r2 = s.Add(Named("Two", 0xa2));
            var r3 = s.Add(Named("Three", 0xa3));
            t.Check(r1.No == 1 && r2.No == 2 && r3.No == 3 && r1.File == "1.hs2nscript" && s.LastNumber == 3, "JF5: rows 1, 2, 3 with their files");
            t.Equal("1.hs2nscript,2.hs2nscript,3.hs2nscript,index.json", Files(dir), "JF5: the folder holds the three files and the index, no temporary file");
            var idx = DocReader.Parse(File.ReadAllBytes(Path.Combine(dir, "index.json")), "index.json");
            t.Check((string)idx["format"] == "hs2next.scriptStore" && (int)idx["version"] == 1 && (int)idx["lastNumber"] == 3 && ((Newtonsoft.Json.Linq.JArray)idx["rows"]).Count == 3
                && string.Join(",", ((Newtonsoft.Json.Linq.JObject)idx["rows"][0]).Properties().Select(p => p.Name)) == "no,id,name,file,created,modified,deleted",
                "JF5: index.json: format, version, lastNumber, rows of no, id, name, file, created, modified, deleted");
            t.Check(File.ReadAllBytes(s.PathOf(2)).SequenceEqual(Named("Two", 0xa2).ToDocumentBytes()), "JF5: a row's file is its script's document");
            var re = Open(dir);
            t.Check(re.Count == 3 && re.Find(2).Name == "Two" && re.FindById(DocTests_Script.Id(0xa3)).No == 3 && re.Read(1).Name == "One", "JF5: a store opened again reads the rows and the scripts");

            // A stop between the file and the index: the file stays, no row lists it, the index on disk and in memory is as before.
            var indexBefore = File.ReadAllBytes(Path.Combine(dir, "index.json"));
            s.BeforeIndexWrite = () => throw new IOException("stopped (test)");
            var e = Catch<ScriptStoreException>(() => s.Add(Named("Stopped", 0xa4)));
            s.BeforeIndexWrite = null;
            t.Check(e != null && e.Code == ScriptFindings.StoreWrite, "JF5: a failed write answers script.storeWrite (" + e?.Message + ")");
            t.Check(File.ReadAllBytes(Path.Combine(dir, "index.json")).SequenceEqual(indexBefore) && s.Count == 3 && s.LastNumber == 3 && File.Exists(s.PathOf(4)),
                "JF5: the index unchanged on disk and in memory; the stopped write's file 4.hs2nscript is there, listed by no row");
            var r5 = s.Add(Named("Five", 0xa5));
            t.Check(r5.No == 5, "JF5: the next row skips the number of the file left behind: 5 (" + r5.No + ")");

            // Replace keeps the number; an import of the same id replaces with replace, copies without it.
            var rr = s.Replace(2, Named("Two again", 0xa2));
            t.Check(rr.No == 2 && rr.Name == "Two again" && rr.Modified > rr.Created && s.Read(2).Name == "Two again" && s.Count == 4, "JF5: replace keeps row 2, its name follows, modified moves");
            var imp1 = s.Import(Named("Imported over three", 0xa3), true, out var st1);
            t.Check(imp1.No == 3 && st1.Id == DocTests_Script.Id(0xa3) && s.Read(3).Name == "Imported over three", "JF5: import with replace: the row of that id keeps its number 3");
            var imp2 = s.Import(Named("Imported copy", 0xa3), false, out var st2);
            t.Check(imp2.No == 6 && st2.Id != DocTests_Script.Id(0xa3) && s.Find(6).Id == st2.Id && s.Read(6).Id == st2.Id && s.Find(3).Name == "Imported over three",
                "JF5: import without replace: a copy under a new id (" + st2.Id + ") and the next number 6; row 3 untouched");
            var imp3 = s.Import(Named("New one", 0xa7), false, out var st3);
            t.Check(imp3.No == 7 && st3.Id == DocTests_Script.Id(0xa7), "JF5: import of a new id: its own id, the next number 7");

            // Delete hides, restore shows; numbers never given again after a delete and a purge.
            t.Check(s.Delete(1) && s.Delete(6) && s.Find(1).Deleted && s.LiveRows().Count == 4 && File.Exists(s.PathOf(1)), "JF5: delete hides rows 1 and 6 and touches no file");
            t.Check(s.Delete(6, restore: true) && !s.Find(6).Deleted && s.LiveRows().Count == 5, "JF5: restore shows row 6 again");
            t.Check(!s.Delete(99), "JF5: no row 99: false");
            File.WriteAllText(Path.Combine(dir, "readme.txt"), "not the store's");
            var preview = s.PurgePreview();
            var p = s.Purge();
            t.Check(p.Rows == 1 && p.Files == 1 && p.Bytes > 0 && !p.Unreadable && preview.Rows == p.Rows && preview.Files == p.Files && preview.Bytes == p.Bytes,
                "JF5: purge removes the hidden row 1 and its file only, as its preview said (" + p.Rows + ", " + p.Files + ", " + p.Bytes + " bytes)");
            t.Info("JF5: present: the purge removed " + p.Files + " file, " + p.Bytes + " bytes");
            t.Check(p.Files >= 1, "JF5: the purge removed at least one file");
            t.Equal("2.hs2nscript,3.hs2nscript,4.hs2nscript,5.hs2nscript,6.hs2nscript,7.hs2nscript,index.json,readme.txt", Files(dir),
                "JF5: what is left: the live rows' files, the stopped write's 4.hs2nscript (no row names it), the index, a file the store does not write");
            var r8 = s.Add(Named("Eight", 0xa8));
            t.Check(r8.No == 8 && s.Find(1) == null && s.LastNumber == 8, "JF5: after the purge the next number is 8: numbers 1 and 4 are never given again");
            var reopened = Open(dir);
            t.Check(reopened.LastNumber == 8 && reopened.Count == 6, "JF5: and so after the store is opened again");

            // The unreadable index: an empty store, the file kept, copied to index.json.bad at the next write; purge refused until then.
            foreach (var (what, bytes) in new (string, byte[])[]
            {
                ("a cut JSON", Encoding.UTF8.GetBytes("{\"format\": \"hs2next.scriptStore\", \"version\": 1, \"lastNumber\"")),
                ("another format", Encoding.UTF8.GetBytes("{\"format\": \"hs2next.bakeStore\", \"version\": 1, \"lastNumber\": 3, \"rows\": []}")),
                ("a newer version", Encoding.UTF8.GetBytes("{\"format\": \"hs2next.scriptStore\", \"version\": 2, \"lastNumber\": 3, \"rows\": []}")),
                ("a row with a bad file name", Encoding.UTF8.GetBytes("{\"format\": \"hs2next.scriptStore\", \"version\": 1, \"lastNumber\": 3, \"rows\": [{\"no\": 1, \"id\": \"" + DocTests_Script.Id(1) + "\", \"name\": \"x\", \"file\": \"../x.hs2nscript\"}]}")),
            })
            {
                string d = Fresh(t, "bad");
                Directory.CreateDirectory(d);
                File.WriteAllBytes(Path.Combine(d, "index.json"), bytes);
                File.WriteAllBytes(Path.Combine(d, "1.hs2nscript"), Named("Old", 0xb1).ToDocumentBytes());
                var f = new FindingList();
                var b = Open(d, f);
                t.Check(b.Unreadable && b.Count == 0 && f.Has(ScriptFindings.StoreUnreadable) && File.ReadAllBytes(Path.Combine(d, "index.json")).SequenceEqual(bytes),
                    "JF5: " + what + ": an empty store with script.storeUnreadable, the file kept (" + f + ")");
                var fp = new FindingList();
                var pr = b.Purge(fp);
                t.Check(pr.Unreadable && pr.Files == 0 && pr.Rows == 0 && fp.Has(ScriptFindings.StoreUnreadable) && File.Exists(Path.Combine(d, "1.hs2nscript")),
                    "JF5: " + what + ": purge removes nothing and answers script.storeUnreadable");
                var w = b.Add(Named("New", 0xb2));
                t.Check(File.ReadAllBytes(Path.Combine(d, "index.json.bad")).SequenceEqual(bytes) && !b.Unreadable && w.No == 2,
                    "JF5: " + what + ": the next write copies the index to index.json.bad and takes a number the left-behind file does not hold (" + w.No + ")");
            }
            string none = Fresh(t, "noindex");
            Directory.CreateDirectory(none);
            File.WriteAllBytes(Path.Combine(none, "3.hs2nscript"), Named("Lost", 0xb3).ToDocumentBytes());
            var fn = new FindingList();
            t.Check(Open(none, fn).Unreadable && fn.Has(ScriptFindings.StoreUnreadable), "JF5: a folder with script files and no index is unreadable");
            PurgeCases(t);
            string empty = Fresh(t, "emptyfolder");
            Directory.CreateDirectory(empty);
            var fe = new FindingList();
            t.Check(!Open(empty, fe).Unreadable && fe.Count == 0, "JF5: an empty folder is an empty store with no finding");

            // Refusals: a script that breaks a rule or is opaque; the file names.
            var bad = Named("Bad", 0xc1);
            bad.Setup.Add(Newtonsoft.Json.Linq.JObject.Parse("{\"cmd\": \"script.play\"}"));
            t.Throws<ArgumentException>(() => s.Add(bad), "JF5: a script with a refused step is not kept");
            var opaque = SceneScript.ParseScene("{\"format\": \"hs2next.script\", \"version\": 3, \"id\": \"" + DocTests_Script.Id(0xc2) + "\"}");
            t.Throws<ArgumentException>(() => s.Add(opaque), "JF5: an opaque script is not kept");
            t.Check(ScriptStore.IsFileName("12.hs2nscript") && !ScriptStore.IsFileName("012.hs2nscript") && !ScriptStore.IsFileName("0.hs2nscript") && !ScriptStore.IsFileName("1.hs2nscene")
                && !ScriptStore.IsFileName("a.hs2nscript") && ScriptStore.NumberOf("7.hs2nscript") == 7, "JF5: file names <no>.hs2nscript only, as the store writes them");
            t.Equal(100000, ScriptLimits.MaxStoreRows, "JF5: the row limit is 100,000 (an add beyond it answers script.limit)");
        }

        static string Sha(string path)
        {
            using (var h = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
        }

        static Dictionary<string, string> Hashes(string dir) =>
            Directory.GetFiles(dir).Where(f => ScriptStore.IsFileName(Path.GetFileName(f))).ToDictionary(f => Path.GetFileName(f), Sha, StringComparer.Ordinal);

        static bool StillThere(string dir, Dictionary<string, string> before) => before.All(kv => File.Exists(Path.Combine(dir, kv.Key)) && Sha(Path.Combine(dir, kv.Key)) == kv.Value);

        // The lead's purge cases (after S's hand-back): a purge never removes a file its readable index does not name as a hidden row's.
        static void PurgeCases(TestRunner t)
        {
            // (a) A stray script file the index does not list, and a leftover temporary file, survive a purge that removes a hidden row's file.
            string a = Fresh(t, "purge_a");
            var sa = Open(a);
            sa.Add(Named("Kept", 0xd1));
            sa.Add(Named("Hidden", 0xd2));
            File.WriteAllBytes(Path.Combine(a, "9.hs2nscript"), Named("Stray", 0xd9).ToDocumentBytes());
            File.WriteAllBytes(Path.Combine(a, "5.hs2nscript.tmp-0123"), new byte[] { 1, 2, 3 });
            sa.Delete(2);
            var ha = Hashes(a);
            t.Info("JF5 (a): present: " + ha.Count + " script files on disk before the purge");
            t.Check(ha.Count >= 2, "JF5 (a): the case starts with at least 2 script files (" + ha.Count + ")");
            var pa = sa.Purge();
            t.Check(pa.Files == 1 && pa.Rows == 1 && !File.Exists(Path.Combine(a, "2.hs2nscript")), "JF5 (a): the hidden row's file 2.hs2nscript removed, and nothing else (" + pa.Files + " file)");
            t.Check(File.Exists(Path.Combine(a, "9.hs2nscript")) && Sha(Path.Combine(a, "9.hs2nscript")) == ha["9.hs2nscript"] && Sha(Path.Combine(a, "1.hs2nscript")) == ha["1.hs2nscript"]
                && File.Exists(Path.Combine(a, "5.hs2nscript.tmp-0123")), "JF5 (a): the stray 9.hs2nscript and the live row's file survive with their hashes, the leftover temporary file stays");

            // (b) An unreadable index, then the next write (the old index copied to index.json.bad): purges remove 0 files; every script file stays with its hash.
            string b = Fresh(t, "purge_b");
            Directory.CreateDirectory(b);
            var broken = Encoding.UTF8.GetBytes("{\"format\": \"hs2next.scriptStore\", \"version\": 1, \"lastNumber\": 3, \"rows\": [");
            File.WriteAllBytes(Path.Combine(b, "index.json"), broken);
            for (int i = 1; i <= 3; i++) File.WriteAllBytes(Path.Combine(b, i + ".hs2nscript"), Named("Old " + i, 0xe0 + i).ToDocumentBytes());
            var hb = Hashes(b);
            t.Info("JF5 (b): present: " + hb.Count + " script files on disk before the purges");
            t.Check(hb.Count >= 2, "JF5 (b): the case starts with at least 2 script files (" + hb.Count + ")");
            var fb = new FindingList();
            var sb = Open(b, fb);
            var fb1 = new FindingList();
            var pb1 = sb.Purge(fb1);
            t.Check(sb.Unreadable && pb1.Unreadable && pb1.Files == 0 && fb1.Has(ScriptFindings.StoreUnreadable) && StillThere(b, hb), "JF5 (b): unreadable: purge removes 0 files, answers script.storeUnreadable");
            var nb = sb.Add(Named("New", 0xe9));
            t.Check(File.ReadAllBytes(Path.Combine(b, "index.json.bad")).SequenceEqual(broken) && !sb.Unreadable && nb.No == 4, "JF5 (b): the next write copies the index to index.json.bad (row 4)");
            sb.Delete(4);
            var pb2 = sb.Purge();
            t.Check(!pb2.Unreadable && pb2.Rows == 1 && pb2.Files == 1 && !File.Exists(Path.Combine(b, "4.hs2nscript")), "JF5 (b): after it a purge removes the hidden row 4's file only");
            t.Check(StillThere(b, hb), "JF5 (b): every script file of the unreadable index is still there with its hash (" + hb.Count + " of " + hb.Count + ")");

            // (c) A folder with script files and no index: unreadable, a purge removes 0.
            string c = Fresh(t, "purge_c");
            Directory.CreateDirectory(c);
            File.WriteAllBytes(Path.Combine(c, "1.hs2nscript"), Named("Lost 1", 0xf1).ToDocumentBytes());
            File.WriteAllBytes(Path.Combine(c, "2.hs2nscript"), Named("Lost 2", 0xf2).ToDocumentBytes());
            var hc = Hashes(c);
            t.Info("JF5 (c): present: " + hc.Count + " script files on disk before the purge");
            t.Check(hc.Count >= 2, "JF5 (c): the case starts with at least 2 script files (" + hc.Count + ")");
            var fc = new FindingList();
            var sc = Open(c, fc);
            var fc1 = new FindingList();
            var pc = sc.Purge(fc1);
            t.Check(sc.Unreadable && pc.Unreadable && pc.Files == 0 && fc1.Has(ScriptFindings.StoreUnreadable) && StillThere(c, hc) && !File.Exists(Path.Combine(c, "index.json")),
                "JF5 (c): no index: purge removes 0 files, answers script.storeUnreadable, writes nothing");
        }

        static TEx Catch<TEx>(Action a) where TEx : Exception
        {
            try { a(); }
            catch (TEx e) { return e; }
            return null;
        }
    }
}

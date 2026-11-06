// MaterialEditTest.full, the texture groups (package C): MT1-MT3, MT5-MT7. See MaterialEditTest.cs. Images are made here, a few pixels of
// one colour; nothing is shown or opened.
//   MT1  import: a PNG and a JPEG in; a WebP, a text file named .png, an animated PNG, a header of 8,193 pixels, 65 MiB and a body that does
//        not decode refused with bad_args on file, the store unchanged; the op's path with the same refusals.
//   MT2  the same bytes on two targets: one res entry, one decode.
//   MT3  import, undo, save: the entry absent; redo, save: present; undo never leaves a name without its bytes.
//   MT5  an import from a folder with a marked name: no reply, history label, finding or saved file holds that name.
//   MT6  a document naming an image with a header of 60,000 pixels: nothing allocated, the finding, the app alive.
//   MT7  an item released: the decoded images go.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using HS2Next;
using HS2Next.Chara.Ext;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static MakerTestKit;

public static partial class MaterialEditTest
{
    static partial void FullTextures(McHost h)
    {
        if (Want("MT1")) Guard("MT1 import", () => Mt1(h));
        if (Want("MT2")) Guard("MT2 the same bytes twice", () => Mt2(h));
        if (Want("MT3")) Guard("MT3 import, undo, save", () => Mt3(h));
        if (Want("MT5")) Guard("MT5 no path anywhere", () => Mt5(h));
        if (Want("MT6")) Guard("MT6 a header of 60,000 pixels", () => Mt6(h));
        if (Want("MT7")) Guard("MT7 released", () => Mt7(h));
    }

    /// <summary>A texture property the target's material offers, and the array it edits.</summary>
    sealed class MtSite
    {
        public string Key, Target, Material, Property;
        public Target Array;
    }

    // the first target with a texture property
    static MtSite MtPick(McHost h, string key, bool second = false)
    {
        string image = TestImage(h, Color.gray);
        bool skipped = false;
        foreach (var t in Targets(h, key))
        {
            var c = Cases(h, key, t, image).FirstOrDefault(x => x.Type == "texture");
            if (c == null) continue;
            if (second && !skipped) { skipped = true; continue; }
            return new MtSite { Key = key, Target = t.Name, Material = c.MaterialName, Property = c.Property.Substring(1), Array = t };
        }
        return null;
    }

    static JObject MtCall(McHost h, MtSite s, string file) =>
        h.Call(new { cmd = "chara.edit.materials.set", key = s.Key, target = s.Target, type = "texture", material = s.Material, property = s.Property, file });

    static byte[] MtImage(Color c, bool jpeg = false, int side = 8)
    {
        var t = new Texture2D(side, side, TextureFormat.RGBA32, false);
        var px = new Color[side * side];
        for (int i = 0; i < px.Length; i++) px[i] = c;
        t.SetPixels(px);
        t.Apply();
        var b = jpeg ? t.EncodeToJPG() : t.EncodeToPNG();
        UnityEngine.Object.DestroyImmediate(t);
        return b;
    }

    // a PNG's signature and header chunk claiming that size, with `tail` after them
    static byte[] MtPngHeader(int w, int h, byte[] tail = null)
    {
        var s = new MemoryStream();
        s.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
        void Be(int v) { s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
        Be(13);
        s.Write(Encoding.ASCII.GetBytes("IHDR"), 0, 4);
        Be(w);
        Be(h);
        s.Write(new byte[] { 8, 6, 0, 0, 0 }, 0, 5);
        Be(0);
        if (tail != null) s.Write(tail, 0, tail.Length);
        return s.ToArray();
    }

    static byte[] MtChunk(string type, int length)
    {
        var b = new byte[12 + length];
        b[3] = (byte)length;
        Encoding.ASCII.GetBytes(type).CopyTo(b, 4);
        return b;
    }

    static string MtFolder(string name)
    {
        string d = Path.Combine(HostDir, name);
        Directory.CreateDirectory(d);
        return d;
    }

    static Dictionary<string, byte[]> MtEntries(string path)
    {
        var d = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using (var ms = new MemoryStream(File.ReadAllBytes(path)))
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
            foreach (var e in zip.Entries)
                using (var s = e.Open())
                using (var o = new MemoryStream())
                {
                    s.CopyTo(o);
                    d[e.FullName] = o.ToArray();
                }
        return d;
    }

    static bool MtHolds(byte[] bytes, string text)
    {
        // UTF-8 and UTF-16: a file holds a name in either
        foreach (var needle in new[] { Encoding.UTF8.GetBytes(text), Encoding.Unicode.GetBytes(text) })
            for (int i = 0; i + needle.Length <= bytes.Length; i++)
            {
                int k = 0;
                while (k < needle.Length && bytes[i + k] == needle[k]) k++;
                if (k == needle.Length) return true;
            }
        return false;
    }

    // ---------------------------------------------------------------- MT1

    static void Mt1(McHost h)
    {
        string key = NewScene(h);
        var s = MtPick(h, key);
        if (!Check("MT1 a target with a texture property", s != null, "")) return;
        var res = h.Session.Model.File.Res;
        string dir = MtFolder("mt1");

        var png = MtImage(new Color(0.8f, 0.2f, 0.1f, 1f));
        var jpg = MtImage(new Color(0.1f, 0.2f, 0.8f, 1f), jpeg: true);
        foreach (var (name, bytes, ext) in new[] { ("mt1_a.png", png, "png"), ("mt1_b.jpg", jpg, "jpg"), ("mt1_c.jpeg", MtImage(new Color(0.1f, 0.8f, 0.2f, 1f), jpeg: true), "jpg") })
        {
            string path = Path.Combine(dir, name);
            File.WriteAllBytes(path, bytes);
            int n0 = res.Count;
            var r = MtCall(h, s, path);
            Check("MT1 " + name + ": accepted", r.Value<bool?>("ok") == true, Short(r.ToString()));
            string want = ResStore.NameOf(bytes);
            var row = (r["items"] as JArray)?.OfType<JObject>().SingleOrDefault();
            Check("MT1 " + name + ": the item names its res entry", (string)row?["image"] == want && want.EndsWith("." + ext, StringComparison.Ordinal), row?.ToString());
            Check("MT1 " + name + ": one entry more, the bytes as read", res.Count == n0 + 1 && res.Get(want)?.SequenceEqual(bytes) == true, res.Count + "");
            var m = Named(h, key, s.Array, s.Material)[0];
            var tex = m.GetTexture("_" + s.Property);
            Check("MT1 " + name + ": on the material", tex && tex.width == 8 && tex.height == 8, tex ? tex.width + "x" + tex.height : "none");
            Check("MT1 " + name + ": no path in the reply", !r.ToString().Contains(dir.Replace("\\", "\\\\")) && !r.ToString().Contains(name), Short(r.ToString()));
        }

        // refused: the file's kind, its header, its length, its body
        string big = Path.Combine(dir, "mt1_big.png");
        using (var f = File.Create(big))
        {
            var head = MtPngHeader(16, 16);
            f.Write(head, 0, head.Length);
            f.SetLength((65L << 20));
        }
        string json = Json(s.Array.List(Stored(h, key)));
        var refused = new List<(string name, string path, string reason)>
        {
            ("a WebP", Write(dir, "mt1_webp.png", Encoding.ASCII.GetBytes("RIFF\x24\0\0\0WEBPVP8 \x18\0\0\0").Concat(new byte[32]).ToArray()), "neither a PNG"),
            ("a text file named .png", Write(dir, "mt1_text.png", Encoding.ASCII.GetBytes("this is no image at all, only text\n")), "neither a PNG"),
            ("an animated PNG", Write(dir, "mt1_apng.png", MtPngHeader(16, 16, MtChunk("acTL", 8).Concat(MtChunk("IDAT", 8)).ToArray())), "animated"),
            ("a header of 8,193 pixels", Write(dir, "mt1_side.png", MtPngHeader(8193, 16, new byte[64])), "8192"),
            ("a tall header of 8,193 pixels", Write(dir, "mt1_tall.png", MtPngHeader(16, 8193, new byte[64])), "8192"),
            ("65 MiB", big, "64 MiB"),
            ("a body that does not decode", Write(dir, "mt1_body.png", MtPngHeader(64, 64, Enumerable.Repeat((byte)0x5a, 256).ToArray())), "decoded"),
        };
        foreach (var (what, path, reason) in refused)
        {
            int n0 = res.Count;
            long bytes0 = res.TotalBytes;
            int history = HistoryCount(h);
            var r = MtCall(h, s, path);
            Check("MT1 " + what + ": bad_args on file", r.Value<string>("code") == "bad_args" && (string)r["detail"]?["arg"] == "file", Short(r.ToString()));
            Check("MT1 " + what + ": the reason", r.Value<string>("error")?.Contains(reason) == true, Short(r.ToString()));
            Check("MT1 " + what + ": no path in the reply", !r.ToString().Contains(Path.GetFileName(path)) && !r.ToString().Contains("mt1"), Short(r.ToString()));
            Check("MT1 " + what + ": the store unchanged, no entry, the array unchanged", res.Count == n0 && res.TotalBytes == bytes0 && HistoryCount(h) == history && Json(s.Array.List(Stored(h, key))) == json, "");
            var op = h.App.Ops.ImportMaterialTexture(path, false, out string name);
            Check("MT1 " + what + ": the op refuses with the same reason", op.Code == "bad_args" && op.Arg == "file" && op.Message.Contains(reason) && name == null && res.Count == n0, op.ToString());
        }
        File.Delete(big);

        // the op: a res name, then an edit that names it
        string good = Write(dir, "mt1_op.png", MtImage(new Color(0.3f, 0.3f, 0.6f, 1f)));
        var live0 = ExtTextures.Live;
        var ok = h.App.Ops.ImportMaterialTexture(good, false, out string res1);
        Check("MT1 the op imports", ok.Ok && res1 != null && res.Contains(res1), ok.ToString());
        Check("MT1 the op holds the decoded image", ExtTextures.Live == live0 + 1, ExtTextures.Live + " / " + live0);
        MaterialEditTarget.TryParse(key, s.Target, out var target, out _);
        var set = h.App.Ops.SetMaterial(target, MaterialEditRequest.Texture(s.Material, s.Property).WithImage(res1), new ExecuteOptions { Source = CommandSource.User, Label = StudioOps.Labels.MaterialTexture });
        Check("MT1 an edit names the image, under the UI's label", set.Ok && TopLabel(h) == StudioOps.Labels.MaterialTexture, set.ToString());
        Check("MT1 the hold is released: the item's image took the place of the one it replaced", ExtTextures.Live == live0, ExtTextures.Live + " / " + live0);
        h.Ok(new { cmd = "chara.edit.materials.clear", key, target = s.Target, all = true }, "MT1 clear");
        Check("MT1 cleared: no reference is left behind", ExtTextures.Live == live0 - 1, ExtTextures.Live + " / " + live0);
    }

    static string Write(string dir, string name, byte[] bytes)
    {
        string p = Path.Combine(dir, name);
        File.WriteAllBytes(p, bytes);
        return p;
    }

    static int HistoryCount(McHost h) => (h.Call(new { cmd = "history" })["undo"] as JArray)?.Count ?? 0;
    static string TopLabel(McHost h) => (string)(h.Call(new { cmd = "history" })["undo"] as JArray)?.FirstOrDefault();

    // ---------------------------------------------------------------- MT2

    static void Mt2(McHost h)
    {
        string key = NewScene(h);
        var a = MtPick(h, key);
        var b = MtPick(h, key, second: true);
        if (!Check("MT2 two targets with a texture property", a != null && b != null, "")) return;
        var res = h.Session.Model.File.Res;
        var bytes = MtImage(new Color(0.5f, 0.1f, 0.5f, 1f));
        string path = Write(MtFolder("mt2"), "mt2.png", bytes), other = Write(MtFolder("mt2b"), "another_name.png", bytes);
        int n0 = res.Count, d0 = ExtTextures.Decoded;
        var r1 = MtCall(h, a, path);
        Check("MT2 the first target", r1.Value<bool?>("ok") == true, Short(r1.ToString()));
        Check("MT2 one decode", ExtTextures.Decoded == d0 + 1, (ExtTextures.Decoded - d0) + "");
        var r2 = MtCall(h, b, other);
        Check("MT2 the second target, by another file of the same bytes", r2.Value<bool?>("ok") == true, Short(r2.ToString()));
        Check("MT2 one res entry", res.Count == n0 + 1, (res.Count - n0) + "");
        Check("MT2 one decode for both", ExtTextures.Decoded == d0 + 1, (ExtTextures.Decoded - d0) + "");
        Check("MT2 both items name it", (string)r1["items"]?[0]?["image"] == (string)r2["items"]?[0]?["image"], "");
        foreach (var s in new[] { a, b }) h.Ok(new { cmd = "chara.edit.materials.clear", key, target = s.Target, all = true }, "MT2 clear");
    }

    // ---------------------------------------------------------------- MT3

    static void Mt3(McHost h)
    {
        string key = NewScene(h);
        var s = MtPick(h, key);
        if (!Check("MT3 a target with a texture property", s != null, "")) return;
        var bytes = MtImage(new Color(0.2f, 0.7f, 0.7f, 1f));
        string name = ResStore.NameOf(bytes);
        var r = MtCall(h, s, Write(MtFolder("mt3"), "mt3.png", bytes));
        Check("MT3 import", r.Value<bool?>("ok") == true, Short(r.ToString()));
        h.Ok(new { cmd = "undo" }, "MT3 undo");
        h.Rt.ApplyNow();
        var res = h.Session.Model.File.Res;
        Check("MT3 undo keeps the bytes in the store", res.Contains(name), "");
        var save = h.Ok(new { cmd = "scene.save", name = "mt3a", overwrite = true, thumb = "placeholder", clock = "2026-11-05T00:00:00Z" }, "MT3 save after undo");
        Check("MT3 the file lacks the image", !MtEntries((string)save["path"]).ContainsKey(name), "");
        Check("MT3 the store still holds it", res.Contains(name), "");
        h.Ok(new { cmd = "redo" }, "MT3 redo");
        h.Rt.ApplyNow();
        var items = h.Ok(new { cmd = "chara.edit.materials.items", key, target = s.Target }, "MT3 items after redo");
        Check("MT3 redo: the item applies, its name has its bytes", (string)items["targets"]?[0]?["items"]?[0]?["status"] == "applied", Short(items.ToString()));
        save = h.Ok(new { cmd = "scene.save", name = "mt3b", overwrite = true, thumb = "placeholder", clock = "2026-11-05T00:00:00Z" }, "MT3 save after redo");
        var entries = MtEntries((string)save["path"]);
        Check("MT3 the file holds the image", entries.TryGetValue(name, out var e) && e.SequenceEqual(bytes), "");
        h.Ok(new { cmd = "chara.edit.materials.clear", key, target = s.Target, all = true }, "MT3 clear");
    }

    // ---------------------------------------------------------------- MT5

    static void Mt5(McHost h)
    {
        const string mark = "MTFIVEMARKEDFOLDER";
        string key = NewScene(h);
        var s = MtPick(h, key);
        if (!Check("MT5 a target with a texture property", s != null, "")) return;
        string file = "picked_" + mark + ".png";
        var r = MtCall(h, s, Write(MtFolder(mark), file, MtImage(new Color(0.6f, 0.6f, 0.1f, 1f))));
        Check("MT5 import", r.Value<bool?>("ok") == true, Short(r.ToString()));
        Check("MT5 the reply", !r.ToString().Contains(mark), Short(r.ToString()));
        foreach (var q in new object[] { new { cmd = "history" }, new { cmd = "scene.doc", capture = false }, new { cmd = "chara.edit.materials.items", key }, new { cmd = "chara.edit.materials", key, part = "clothes" }, new { cmd = "chara.edit.materials", key, part = "face" } })
        {
            var a = h.Call(q);
            Check("MT5 " + a.Value<string>("cmd") + ": no mark", !a.ToString().Contains(mark), "");
        }
        Check("MT5 no finding holds it", h.Rt.Findings.All(f => (f.Message ?? "").IndexOf(mark, StringComparison.Ordinal) < 0), "");
        var files = new List<(string what, string path)>();
        files.Add(("scene", (string)h.Ok(new { cmd = "scene.save", name = "mt5", overwrite = true, thumb = "placeholder", clock = "2026-11-05T00:00:00Z" }, "MT5 scene.save")["path"]));
        files.Add(("character", (string)h.Ok(new { cmd = "chara.save", key, name = "mt5", overwrite = true }, "MT5 chara.save")["path"]));
        files.Add(("coordinate", (string)h.Ok(new { cmd = "coord.save", key, name = "mt5", overwrite = true }, "MT5 coord.save")["path"]));
        foreach (var (what, path) in files)
        {
            Check("MT5 " + what + ": the file's bytes", !MtHolds(File.ReadAllBytes(path), mark), "");
            int hits = 0, n = 0;
            foreach (var kv in MtEntries(path))
            {
                n++;
                if (kv.Key.Contains(mark) || MtHolds(kv.Value, mark)) hits++;
            }
            Check("MT5 " + what + ": 0 of " + n + " entries hold the mark", n > 0 && hits == 0, hits + "");
        }
        h.Ok(new { cmd = "chara.edit.materials.clear", key, target = s.Target, all = true }, "MT5 clear");
    }

    // ---------------------------------------------------------------- MT6

    static void Mt6(McHost h)
    {
        string key = NewScene(h);
        var s = MtPick(h, key);
        if (!Check("MT6 a target with a texture property", s != null, "")) return;
        // a file's image is not held to the import's limits: the store takes it, the guard before decoding answers
        string name = h.Session.Model.File.Res.Add(MtPngHeader(60000, 60000, new byte[64]));
        int failed = ExtTextures.Failed, live = ExtTextures.Live;
        var r = h.Call(new { cmd = "chara.edit.materials.set", key, target = s.Target, type = "texture", material = s.Material, property = s.Property, image = name });
        Check("MT6 the edit is stored", r.Value<bool?>("ok") == true, Short(r.ToString()));
        Check("MT6 the status says it", (string)r["items"]?[0]?["status"] == "undecodable", Short(r.ToString()));
        Check("MT6 nothing decoded, Failed + 1", ExtTextures.Failed > failed && ExtTextures.Live == live, (ExtTextures.Failed - failed) + ", live " + (ExtTextures.Live - live));
        var save = h.Ok(new { cmd = "scene.save", name = "mt6", overwrite = true, thumb = "placeholder", clock = "2026-11-05T00:00:00Z" }, "MT6 scene.save");
        h.Ok(new { cmd = "scene.new" }, "MT6 scene.new");
        failed = ExtTextures.Failed;
        h.Ok(new { cmd = "scene.load", path = (string)save["path"] }, "MT6 scene.load");
        h.Rt.ApplyNow();
        Check("MT6 the load: Failed + 1", ExtTextures.Failed > failed, (ExtTextures.Failed - failed) + "");
        Check("MT6 the finding", h.Rt.Findings.Any(f => f.Code == MaterialEditorApplier.TextureTooLargeKind), string.Join(", ", h.Rt.Findings.Select(f => f.Code).Distinct()));
        h.Ok(new { cmd = "ping" }, "MT6 the app answers");
        h.Ok(new { cmd = "chara.edit.materials.clear", key, target = s.Target, all = true }, "MT6 clear");
    }

    // ---------------------------------------------------------------- MT7

    static void Mt7(McHost h)
    {
        h.Ok(new { cmd = "scene.new" }, "MT7 scene.new");
        int live = ExtTextures.Live;
        var add = h.Ok(new { cmd = "item.add", no = 399 }, "MT7 item.add");
        string id = (string)add["added"]?[0]?["key"];
        h.Rt.ApplyNow();
        var slot = h.Ok(new { cmd = "item.materials", key = id, allProps = true }, "MT7 item.materials")["renderers"]?[0]?["slots"]?[0] as JObject;
        string texture = (slot?["props"] as JObject)?.Properties().FirstOrDefault(p => p.Name == "_MainTex" || p.Name == "_BaseMap")?.Name;
        if (!Check("MT7 the item has a texture property", texture != null, Short(slot?.ToString() ?? "no slot"))) return;
        string path = Write(MtFolder("mt7"), "mt7.png", MtImage(new Color(0.9f, 0.5f, 0.2f, 1f)));
        var r = h.Ok(new { cmd = "item.materials.set", key = id, type = "texture", material = MeKey.Format((string)slot["material"]), property = texture.Substring(1), file = path }, "MT7 item.materials.set file");
        Check("MT7 the item holds its image", ExtTextures.Live == live + 1, (ExtTextures.Live - live) + "");
        h.Ok(new { cmd = "scene.new" }, "MT7 the item released");
        h.Rt.ApplyNow();
        Check("MT7 ExtTextures.Live back to the start", ExtTextures.Live == live, (ExtTextures.Live - live) + "");
    }
}

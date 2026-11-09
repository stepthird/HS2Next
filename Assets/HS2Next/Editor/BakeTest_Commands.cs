// BakeTest parts of the commands of Chara to Item and the Cutter (Phase 6.8 package C):
//   Headless_Commands  BE1 one entry per operation, undo and redo of a cut restore its nodes and the target's visibility, the blobs stay for redo;
//                      BE2 an edit of a piece's cut is one entry, undo gives both blocks back bit for bit; BE3 a rename of a store row renames the
//                      items that carried the old name in one entry, undo renames them back, the index keeps the new name; BE4 duplicate and
//                      scene import carry the blobs under their names. Stored bakes made in code (BakeTest.Synthetic), cuts on the CPU.
//   Render_Commands    BE1r a bake of a live character through bake.make (one entry, the character hidden, undo and redo, the store row stays)
//                      and a cut of the live character through cut.make (one entry, undo and redo). BR-Chan, graphics edit mode.
// The store is the run's own (Spike/out/bake/userdata/bakes), emptied before and after. Every check that compares a difference prints what shows
// the thing compared is there.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2Next.App;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Bake;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>Fixtures of bakes in a scene through the commands, shared by BakeTest and BridgeCoreTest's C49.</summary>
internal static class BakeCmdKit
{
    /// <summary>Puts the app's group (731406) on the host's list when it is not there; true when this call put it there.</summary>
    public static bool RegisterRows(AppCore app, string userData)
    {
        var db = app.Db;
        if (db == null || db.Generated.Holds(AppItemRows.Group)) return false;
        return AppItemRows.Register(db, userData) != null;
    }

    /// <summary>Empties a store folder made by a test (its files by name; never a tree).</summary>
    public static void ClearStore(string userData)
    {
        string dir = Path.Combine(userData, BakeStore.FolderName);
        if (!Directory.Exists(dir)) return;
        string blobs = Path.Combine(dir, BakeStore.BlobsName);
        if (Directory.Exists(blobs))
        {
            foreach (var f in Directory.GetFiles(blobs)) File.Delete(f);
            Directory.Delete(blobs, false);
        }
        foreach (var f in Directory.GetFiles(dir)) File.Delete(f);
        Directory.Delete(dir, false);
    }

    /// <summary>A bake made in code put into the host's store as a row named <paramref name="name"/>; the row's number.</summary>
    public static int StoreSynthetic(AppCore app, string name, Color32 tint)
    {
        var job = BakeTest.SyntheticJob(name, tint);
        try
        {
            var store = BakeProvider.StoreOf(app.Runtime);
            int no = store.Add(job.Row(name), job.AllBlobs()).Number;
            AppItemRows.RefreshBakes(app.Db);
            return no;
        }
        finally { job.Dispose(); }
    }

    /// <summary>A bake made in code put into the scene only, as an item at the root; its key.</summary>
    public static string SceneSynthetic(AppCore app, string name, Color32 tint)
    {
        var job = BakeTest.SyntheticJob(name, tint);
        try
        {
            job.AddTo(app.Session.Model.File.Res);
            var r = app.Session.AddItem(StudioOps.BakeItemState(job.Block(null), null));
            if (!r.Ok) return null;
            app.Session.Set(r.NewId, "name", name);
            app.Runtime.ApplyNow();
            return r.NewId;
        }
        finally { job.Dispose(); }
    }

    public static ItemNodeState Item(AppCore app, string key) => app.Session.Model.Find(key)?.State as ItemNodeState;
    public static bool Visible(AppCore app, string key) => app.Session.Model.Find(key)?.Doc.Visible ?? false;
    public static ItemRealization Built(AppCore app, string key) => app.Runtime.FindItem(key);

    /// <summary>Every resource name a node's bake and cut blocks name.</summary>
    public static List<string> BlobsOf(ItemNodeState s)
    {
        var l = new List<string>();
        if (s?.Bake != null) l.AddRange(s.Bake.Blobs());
        if (s?.Cut?.Source != null) l.AddRange(s.Cut.Source.Blobs());
        return l.Distinct().ToList();
    }
}

public static partial class BakeTest
{
    /// <summary>A marker put <paramref name="up"/> units above an item's origin, with no entry (the strip of the bake made in code runs from 0 to 6 up).</summary>
    static void PlaceMarker(Host h, string marker, string item, float up)
    {
        var at = h.Rt.HolderOf(item).position + Vector3.up * up;
        h.Ok(new { cmd = "obj.transform", key = marker, pos = new[] { at.x, at.y, at.z }, undo = false }, "(the marker " + up + " above the item)");
    }

    static List<string> UndoLabels(Host h) => (h.Call(new { cmd = "history" })["undo"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();

    /// <summary>A host with the app's group and an empty store of its own; a stored bake added as an item through item.add. Null when the fixture fails.</summary>
    static (Host h, string item, int no) CmdFixture(string name)
    {
        ClearStore();
        var h = NewHost();
        BakeCmdKit.RegisterRows(h.App, UserDataDir);
        int no = BakeCmdKit.StoreSynthetic(h.App, name, new Color32(180, 120, 60, 255));
        var r = h.Ok(new { cmd = "item.add", group = AppItemRows.Group, category = AppItemRows.BakesCategory, no }, "item.add of the store row " + no);
        string key = Key(r);
        h.Rt.ApplyNow();
        var it = BakeCmdKit.Built(h.App, key);
        Check("the store row's item is built from its bake", it != null && it.Source == "bake" && !it.IsFallback && it.Instance, it?.Source + " fallback " + it?.IsFallback);
        return (h, key, no);
    }

    static void CmdDone(Host h)
    {
        if (h != null)
        {
            AppItemRows.Remove(h.App.Db);
            h.Dispose();
        }
        ClearStore();
    }

    // ------------------------------------------------------------ headless

    static void Headless_Commands()
    {
        Group("BE1", "one entry per operation; undo and redo of a cut restore the nodes and the target's visibility; blobs kept for redo", Be1);
        Group("BE2", "an edit of a piece's cut: one entry, undo gives the cut and bake blocks back bit for bit", Be2);
        Group("BE3", "a rename of a store row: the index and the items' names; undo renames the items back, the index keeps the new name", Be3);
        Group("BE4", "duplicate and scene import carry the blobs under their names", Be4);
    }

    static void Be1()
    {
        var (h, item, no) = CmdFixture("be1");
        try
        {
            if (item == null) return;
            int n0 = UndoLabels(h).Count;
            var mk = h.Ok(new { cmd = "cut.marker", kind = "plane", target = item }, "cut.marker plane on the bake item");
            string marker = (string)mk["key"];
            PlaceMarker(h, marker, item, 3f);
            var labels = UndoLabels(h);
            Check("cut.marker: one entry «Add item»", labels.Count == n0 + 1 && labels[0] == StudioOps.Labels.AddItem, string.Join(", ", labels.Take(3)));
            Check("cut.marker: the marker is selected", h.App.Session.Selection.Primary == marker, h.App.Session.Selection.Primary);
            int n1 = labels.Count;
            var res = h.Session.Model.File.Res;
            int resBefore = res.Count;
            var cut = h.Ok(new { cmd = "cut.make", key = item, marker }, "cut.make of the bake item by the plane");
            var pieces = (cut["pieces"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
            labels = UndoLabels(h);
            Check("Present: the cut made >= 1 node", pieces.Count >= 1, pieces.Count + " pieces");
            Check("cut.make: one entry «Cut»", labels.Count == n1 + 1 && labels[0] == StudioOps.Labels.CutMake, string.Join(", ", labels.Take(3)));
            Check("cut.make: the target is hidden, not deleted", h.Session.Model.Find(item) != null && !BakeCmdKit.Visible(h.App, item));
            var blobs = pieces.SelectMany(p => BakeCmdKit.BlobsOf(BakeCmdKit.Item(h.App, p))).Distinct().ToList();
            Info("pieces " + pieces.Count + ", their blobs " + blobs.Count + ", resources " + resBefore + " -> " + res.Count);
            h.Ok(new { cmd = "undo" }, "undo of the cut");
            Check("undo: the pieces are gone", pieces.All(p => h.Session.Model.Find(p) == null));
            Check("undo: the target is shown again", BakeCmdKit.Visible(h.App, item));
            int kept = blobs.Count(b => res.Contains(b));
            Check("undo: every blob of the pieces stays in the scene's resources", kept == blobs.Count && blobs.Count >= 2, kept + " of " + blobs.Count);
            h.Ok(new { cmd = "redo" }, "redo of the cut");
            h.Rt.ApplyNow();
            Check("redo: the pieces are back, built from their bakes", pieces.All(p => BakeCmdKit.Built(h.App, p)?.Source == "bake" && BakeCmdKit.Built(h.App, p)?.IsFallback == false),
                string.Join(", ", pieces.Select(p => BakeCmdKit.Built(h.App, p)?.Source + "/" + BakeCmdKit.Built(h.App, p)?.IsFallback)));
            Check("redo: the target is hidden again", !BakeCmdKit.Visible(h.App, item));
            // the marker's own entry: undo and redo
            h.Ok(new { cmd = "undo" }, "undo of the cut again");
            h.Ok(new { cmd = "undo" }, "undo of the marker");
            Check("undo: the marker is gone", h.Session.Model.Find(marker) == null);
            h.Ok(new { cmd = "redo" }, "redo of the marker");
            Check("redo: the marker is back", h.Session.Model.Find(marker) != null);
            // undo: false applies without an entry; a structural change without one clears the history, as for every command (the reply says so)
            int n2 = UndoLabels(h).Count;
            var cut2 = h.Ok(new { cmd = "cut.make", key = item, marker, mode = "front", undo = false }, "cut.make undo: false");
            var after = UndoLabels(h);
            bool warned = (cut2["warnings"] as JArray)?.Any(w => (string)w == HS2Next.App.Commands.CommandKit.HistoryClearedWarning) == true;
            Check("undo: false: no entry «Cut»; the history cleared with the warning, as for every structural command; the piece is made",
                !after.Contains(StudioOps.Labels.CutMake) && after.Count == 0 && warned && n2 > 0 && (cut2["pieces"] as JArray)?.Count == 1, n2 + " -> " + after.Count + ", warned " + warned);
        }
        finally { CmdDone(h); }
    }

    static void Be2()
    {
        var (h, item, no) = CmdFixture("be2");
        try
        {
            if (item == null) return;
            string marker = (string)h.Ok(new { cmd = "cut.marker", kind = "plane", target = item }, "cut.marker")["key"];
            PlaceMarker(h, marker, item, 3f);
            string piece = (string)h.Ok(new { cmd = "cut.make", key = item, marker, mode = "front" }, "cut.make front")["pieces"]?[0];
            var st = BakeCmdKit.Item(h.App, piece);
            if (!Check("the piece holds a cut and a bake", st?.Cut != null && st.Bake != null)) return;
            string bake0 = st.Bake.ToJson(), cut0 = st.Cut.ToJson();
            // a marker put at the piece's last step stands where the marker of the cut stood
            var at = h.App.Ops.CutMarkerAtLastStep(piece);
            var m2 = at.Ok ? h.Rt.HolderOf((string)at.Value) : null;
            var m1 = h.Rt.HolderOf(marker);
            Check("a marker at the piece's last step: where the cut's marker stood, turned and sized as it", m2 && m1 && (m2.position - m1.position).magnitude < 1e-4f && AngleDeg(m2.rotation, m1.rotation) < 1e-3 &&
                (m2.lossyScale - m1.lossyScale).magnitude < 1e-4f, at.ToString() + " " + (m2 ? m2.position.ToString("F5") + " / " + m1.position.ToString("F5") : ""));
            if (at.Ok) h.Ok(new { cmd = "undo" }, "(the second marker undone)");
            int n0 = UndoLabels(h).Count;
            // the marker moves up: the result changes
            PlaceMarker(h, marker, item, 4.5f);
            var set = h.Ok(new { cmd = "cut.set", key = piece, marker }, "cut.set to the moved marker");
            var labels = UndoLabels(h);
            st = BakeCmdKit.Item(h.App, piece);
            string bake1 = st.Bake.ToJson(), cut1 = st.Cut.ToJson();
            Check("cut.set: one entry «Edit cut»", labels.Count == n0 + 1 && labels[0] == StudioOps.Labels.CutEdit, string.Join(", ", labels.Take(3)));
            Check("Present: the edit changed the result's hash", set.Value<bool?>("changed") == true && BakeBlob.ShaOf(BakeCmdKit.Item(h.App, piece).Bake.Main) != BakeBlob.ShaOf(ItemBake.Parse(bake0).Main),
                ItemBake.Parse(bake0).Main + " -> " + st.Bake.Main);
            h.Ok(new { cmd = "undo" }, "undo of the edit");
            st = BakeCmdKit.Item(h.App, piece);
            Check("undo: the bake block bit for bit", st.Bake.ToJson() == bake0);
            Check("undo: the cut block bit for bit", st.Cut.ToJson() == cut0);
            h.Ok(new { cmd = "redo" }, "redo of the edit");
            st = BakeCmdKit.Item(h.App, piece);
            Check("redo: both blocks as after the edit", st.Bake.ToJson() == bake1 && st.Cut.ToJson() == cut1);
            h.Rt.ApplyNow();
            var it = BakeCmdKit.Built(h.App, piece);
            Check("redo: the piece builds from its new result", it?.Source == "bake" && it.IsFallback == false && h.Session.Model.File.Res.Contains(st.Bake.Main));
            int n1 = UndoLabels(h).Count;
            var again = h.Ok(new { cmd = "cut.set", key = piece, recut = true }, "cut.set recut");
            Check("recut with no change: the same bits, no entry", again.Value<bool?>("changed") == false && UndoLabels(h).Count == n1 && BakeCmdKit.Item(h.App, piece).Bake.ToJson() == bake1,
                Short(h.LastText));
            var sec = h.Ok(new { cmd = "cut.set", key = piece, section = new { color = "bone" } }, "cut.set section bone");
            st = BakeCmdKit.Item(h.App, piece);
            Check("a new section colour: one entry, the colour in the cut block", sec.Value<bool?>("changed") == true && UndoLabels(h).Count == n1 + 1 &&
                st.Cut.Section.Color.X == CutSectionColors.Bone.X && st.Cut.Section.Color.Y == CutSectionColors.Bone.Y, st.Cut.ToJson());
        }
        finally { CmdDone(h); }
    }

    static void Be3()
    {
        var (h, item, no) = CmdFixture("be3 old");
        try
        {
            if (item == null) return;
            string other = Key(h.Ok(new { cmd = "item.add", group = AppItemRows.Group, category = AppItemRows.BakesCategory, no }, "a second item of the row"));
            h.Ok(new { cmd = "obj.rename", key = other, name = "kept apart" }, "the second item is named otherwise");
            h.Ok(new { cmd = "obj.rename", key = item, name = "be3 old" }, "the first item carries the row's name");
            int n0 = UndoLabels(h).Count;
            Check("Present: a node had the old name", h.Session.Model.Find(item).Label == "be3 old");
            var r = h.Ok(new { cmd = "bake.rename", no, name = "be3 new" }, "bake.rename");
            var items = (r["items"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
            var store = BakeProvider.StoreOf(h.Rt);
            Check("the index holds the new name", store.Find(no)?.Name == "be3 new", store.Find(no)?.Name);
            Check("the item with the old name is renamed, the other is not", items.SequenceEqual(new[] { item }) && h.Session.Model.Find(item).Label == "be3 new" && h.Session.Model.Find(other).Label == "kept apart",
                string.Join(", ", items));
            var labels = UndoLabels(h);
            Check("one entry «Rename bake»", labels.Count == n0 + 1 && labels[0] == StudioOps.Labels.BakeRename, string.Join(", ", labels.Take(3)));
            h.Ok(new { cmd = "undo" }, "undo of the rename");
            store.Reload();
            Check("undo: the item has the old name, the index keeps the new one", h.Session.Model.Find(item).Label == "be3 old" && store.Find(no)?.Name == "be3 new",
                h.Session.Model.Find(item).Label + " / " + store.Find(no)?.Name);
            var row = h.App.Db.Generated.Group(AppItemRows.Group)?.Rows.FirstOrDefault(x => x.Key.Id == no && x.Category == AppItemRows.BakesCategory);
            Check("the list's row carries the new name", row?.Name == "be3 new", row?.Name);
        }
        finally { CmdDone(h); }
    }

    static void Be4()
    {
        var (h, item, no) = CmdFixture("be4");
        Host h2 = null;
        try
        {
            if (item == null) return;
            string marker = (string)h.Ok(new { cmd = "cut.marker", kind = "plane", target = item }, "cut.marker")["key"];
            PlaceMarker(h, marker, item, 3f);
            string piece = (string)h.Ok(new { cmd = "cut.make", key = item, marker, mode = "back", hide = false }, "cut.make back")["pieces"]?[0];
            var res = h.Session.Model.File.Res;
            var srcBlobs = BakeCmdKit.BlobsOf(BakeCmdKit.Item(h.App, item)).Concat(BakeCmdKit.BlobsOf(BakeCmdKit.Item(h.App, piece))).Distinct().ToList();
            Check("Present: the source scene has blobs (a bake and a piece's source)", srcBlobs.Count >= 3 && srcBlobs.All(res.Contains) && BakeCmdKit.Item(h.App, piece).Cut?.Source != null,
                srcBlobs.Count + " blobs");
            // duplicate
            var dup = h.App.Ops.Duplicate(new[] { item, piece });
            Check("duplicate of the bake item and the piece", dup.Ok && dup.NewIds.Count == 2, dup.ToString());
            if (dup.Ok && dup.NewIds.Count == 2)
            {
                var a = BakeCmdKit.Item(h.App, dup.NewIds[0]);
                var b = BakeCmdKit.Item(h.App, dup.NewIds[1]);
                Check("duplicate: the bake block copied, the same blob names", a?.Bake?.ToJson() == BakeCmdKit.Item(h.App, item).Bake.ToJson());
                Check("duplicate: the piece's bake and cut blocks copied", b?.Bake?.ToJson() == BakeCmdKit.Item(h.App, piece).Bake.ToJson() && b?.Cut?.ToJson() == BakeCmdKit.Item(h.App, piece).Cut.ToJson());
                h.Rt.ApplyNow();
                Check("duplicate: the copies build from the shared blobs", BakeCmdKit.Built(h.App, dup.NewIds[0])?.Source == "bake" && BakeCmdKit.Built(h.App, dup.NewIds[0])?.IsFallback == false &&
                    BakeCmdKit.Built(h.App, dup.NewIds[1])?.Source == "bake" && BakeCmdKit.Built(h.App, dup.NewIds[1])?.IsFallback == false);
            }
            // scene import into another host whose store is empty
            var save = h.Ok(new { cmd = "scene.save", name = "be4_import", overwrite = true, thumb = "placeholder", clock = "2026-11-08T00:00:00Z" }, "scene.save");
            string path = (string)save["path"];
            AppItemRows.Remove(h.App.Db);
            h.Dispose();
            h = null;
            ClearStore();
            h2 = NewHost(DefaultSeed + 1);
            var imp = h2.Ok(new { cmd = "scene.import", path }, "scene.import into a fresh host with no store");
            var added = h2.Session.Model.PreOrder().Where(n => n.State is ItemNodeState s && s.Bake != null).ToList();
            var res2 = h2.Session.Model.File.Res;
            var names = added.SelectMany(n => BakeCmdKit.BlobsOf((ItemNodeState)n.State)).Distinct().ToList();
            Check("scene import: the bake items and pieces came with their blocks", added.Count == 4 && added.Count(n => ((ItemNodeState)n.State).Cut != null) == 2, added.Count + " items");
            Check("scene import: every blob they name is in the scene under its name", names.Count >= 3 && names.All(res2.Contains) && srcBlobs.All(names.Contains),
                names.Count(res2.Contains) + " of " + names.Count);
            h2.Rt.ApplyNow();
            var fb = added.Select(n => BakeCmdKit.Built(h2.App, n.Id)).Where(x => x == null || x.Source != "bake" || x.IsFallback).ToList();
            Check("scene import: every one builds from the scene's blobs (no store)", fb.Count == 0, fb.Count + " not from their bakes");
            try { File.Delete(path); } catch (Exception) { }
        }
        finally
        {
            CmdDone(h);
            h2?.Dispose();
            ClearStore();
        }
    }

    // ------------------------------------------------------------ graphics

    static void Render_Commands()
    {
        Group("BE1r", "bake.make and cut.make of a live character: one entry each, undo and redo, the store row stays", Be1r);
        Group("BG1i", "bake.import of the synthetic old bakes: the reply, no entry, a second run adds nothing", Bg1i);
    }

    static void Bg1i()
    {
        ClearStore();
        var h = NewHost();
        try
        {
            string folder = Path.Combine(ContentSettings.ProjectRoot, "Tests", "Bake", "old");
            if (!Check("the synthetic old bakes are there", Directory.Exists(folder) && Directory.GetFiles(folder, "*.c2i").Length > 0, folder)) return;
            int files = Directory.GetFiles(folder, "*.c2i").Length;
            int n0 = UndoLabels(h).Count;
            var r = h.Ok(new { cmd = "bake.import", path = folder }, "bake.import of the synthetic old bakes");
            var members = r.Properties().Select(p => p.Name).Where(n => n != "ok" && n != "cmd" && n != "id" && n != "warnings").ToList();
            Check("the reply's members: imported, skipped", members.SequenceEqual(new[] { "imported", "skipped" }), string.Join(", ", members));
            var imported = (r["imported"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
            Check("Present: every old bake imported, each {no, name, renderers, textures, findings}", imported.Count == files &&
                imported.All(x => string.Join(",", x.Properties().Select(p => p.Name)) == "no,name,renderers,textures,findings"), imported.Count + " of " + files + " " + Short(h.LastText));
            Check("no history entry", UndoLabels(h).Count == n0);
            var again = h.Ok(new { cmd = "bake.import", path = folder }, "bake.import again");
            var skipped = (again["skipped"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
            Check("a second run adds nothing: every one skipped as there", (again["imported"] as JArray)?.Count == 0 && skipped.Count == files && skipped.All(x => (string)x["code"] == BakeFindings.ImportExists),
                Short(h.LastText));
            var list = h.Ok(new { cmd = "bake.list" }, "bake.list");
            var nos = (list["rows"] as JArray)?.Select(x => (int)x["no"]).ToList() ?? new List<int>();
            Check("the store lists them under their old numbers", imported.All(x => nos.Contains((int)x["no"])), string.Join(", ", nos));
        }
        finally { CmdDone(h); }
    }

    static void Be1r()
    {
        ClearStore();
        var h = NewHost();
        try
        {
            BakeCmdKit.RegisterRows(h.App, UserDataDir);
            string chara = LoadChara(h);
            h.Frames(2);
            int n0 = UndoLabels(h).Count;
            var res = h.Session.Model.File.Res;
            var parts = h.Ok(new { cmd = "bake.parts", key = chara }, "bake.parts of BR-Chan");
            var rows = (parts["parts"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
            Info("parts: " + string.Join(", ", rows.Select(p => (string)p["part"] + " " + (int)p["renderers"])));
            // two garments, one item each
            var chosen = rows.Where(p => ((string)p["part"]).StartsWith("clothes", StringComparison.Ordinal) && (int)p["renderers"] > 0).Select(p => (string)p["part"]).Take(2).ToArray();
            if (!Check("two garments to bake", chosen.Length == 2, string.Join(", ", chosen))) return;
            var mk = h.Ok(new { cmd = "bake.make", key = chara, parts = chosen, split = true, name = "be1r" }, "bake.make of " + string.Join(" and ", chosen) + ", one item each");
            var items = (mk["items"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
            var bakes = (mk["bakes"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
            var labels = UndoLabels(h);
            Check("Present: the bake made >= 1 node", items.Count >= 1, items.Count + " items");
            Check("bake.make: one entry «Bake to item» for both items", items.Count == 2 && labels.Count == n0 + 1 && labels[0] == StudioOps.Labels.BakeMake, items.Count + " / " + string.Join(", ", labels.Take(3)));
            Check("bake.make: the character is hidden, not deleted", h.Session.Model.Find(chara) != null && !BakeCmdKit.Visible(h.App, chara));
            Check("bake.make: each bake has a store row", bakes.Count == 2 && bakes.All(b => b["no"]?.Type == JTokenType.Integer), Short(h.LastText));
            Check("bake.make: the reply's members", string.Join(",", mk.Properties().Select(p => p.Name).Where(n => n != "ok" && n != "cmd" && n != "id" && n != "warnings")) == "items,bakes,ms" &&
                bakes.All(b => string.Join(",", b.Properties().Select(p => p.Name)) == "bake,no,main,bytes"), Short(h.LastText));
            var names = items.Select(k => h.Session.Model.Find(k)?.Label).ToList();
            Check("bake.make: the given name with the part, one item per part", names.All(n => n != null && n.StartsWith("be1r ", StringComparison.Ordinal)) && names.Distinct().Count() == 2, string.Join(" | ", names));
            h.Rt.ApplyNow();
            Check("bake.make: the items are built from their bakes", items.All(k => BakeCmdKit.Built(h.App, k)?.Source == "bake" && BakeCmdKit.Built(h.App, k)?.IsFallback == false));
            var blobs = items.SelectMany(k => BakeCmdKit.BlobsOf(BakeCmdKit.Item(h.App, k))).Distinct().ToList();
            Info("blobs " + blobs.Count + ", bytes " + string.Join(", ", bakes.Select(b => (long)b["bytes"])) + ", ms " + (double)mk["ms"]);
            h.Ok(new { cmd = "undo" }, "undo of the bake");
            Check("undo: the items are gone, the character is shown again", items.All(k => h.Session.Model.Find(k) == null) && BakeCmdKit.Visible(h.App, chara));
            Check("undo: the blobs stay in the scene's resources", blobs.Count >= 2 && blobs.All(res.Contains), blobs.Count(res.Contains) + " of " + blobs.Count);
            var list = h.Ok(new { cmd = "bake.list" }, "bake.list after the undo");
            Check("undo: the store keeps its rows", (list["rows"] as JArray)?.Count == 2, Short(h.LastText));
            h.Ok(new { cmd = "redo" }, "redo of the bake");
            h.Rt.ApplyNow();
            Check("redo: the items are back and built, the character hidden", items.All(k => BakeCmdKit.Built(h.App, k)?.Source == "bake") && !BakeCmdKit.Visible(h.App, chara));
            // a cut of the live character: a bake of it as drawn, then the cut
            h.Ok(new { cmd = "obj.visible", key = chara, visible = true, undo = false }, "the character shown again");
            string marker = (string)h.Ok(new { cmd = "cut.marker", kind = "plane", target = chara }, "cut.marker on the character")["key"];
            int n1 = UndoLabels(h).Count;
            var cut = h.Ok(new { cmd = "cut.make", key = chara, marker }, "cut.make of the live character");
            var pieces = (cut["pieces"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
            labels = UndoLabels(h);
            Check("cut.make: two pieces in one entry «Cut»", pieces.Count == 2 && labels.Count == n1 + 1 && labels[0] == StudioOps.Labels.CutMake, pieces.Count + " / " + string.Join(", ", labels.Take(3)));
            Check("cut.make: the character is hidden", !BakeCmdKit.Visible(h.App, chara));
            var src = BakeCmdKit.Item(h.App, pieces.FirstOrDefault())?.Cut?.Source;
            Check("cut.make: the source bake of the character is in the scene", src != null && src.Blobs().All(res.Contains), src?.Blobs().Count + " blobs");
            Info("cut ms " + (double)cut["ms"]);
            h.Ok(new { cmd = "undo" }, "undo of the cut");
            Check("undo: the pieces are gone, the character is shown", pieces.All(k => h.Session.Model.Find(k) == null) && BakeCmdKit.Visible(h.App, chara));
            h.Ok(new { cmd = "redo" }, "redo of the cut");
            h.Rt.ApplyNow();
            Check("redo: the pieces are back and built", pieces.All(k => BakeCmdKit.Built(h.App, k)?.Source == "bake" && BakeCmdKit.Built(h.App, k)?.IsFallback == false));
        }
        finally { CmdDone(h); }
    }
}

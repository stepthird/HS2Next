// CE7 (Phase 5.1): a clothes part's inline ext travels with the part when a
// coordinate is worn. Clothes mode: the eight parts take the source's, ext included (a part without ext in the source
// leaves none); accessories mode: the parts and their ext are kept; all mode: the parts' ext follows and, when only the
// parts' ext differs, the one reported path is `appearance.coord.ext` (no clothes part is swapped). Undo restores the
// character's own part ext byte for byte. A scene whose character carries part ext saves as format 2 and loads it back;
// without it the scene stays format 1. Synthetic namespaces only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Api.Save;
    using HS2Next.Save;
    using HS2Next.Save.Tests;
    using Newtonsoft.Json.Linq;

    public static class SceneTests_ClothesExt
    {
        const string Ns = "stp3.parts";

        public static void Run(TestRunner t)
        {
            Mode(t, CoordinateApplyMode.Clothes);
            Mode(t, CoordinateApplyMode.Accessories);
            Mode(t, CoordinateApplyMode.All);
            Save(t);
        }

        static CoordDocument Coord(SceneSession s, string id) => s.Model.Find(id).Appearance.Stored.Coord;

        static string PartExt(CoordDocument c, int k) => c.Clothes.Parts[k].Ext.IsEmpty ? "-" : c.Clothes.Parts[k].Ext.Get(Ns)?.ToString(Newtonsoft.Json.Formatting.None) ?? "?";

        static string AllPartExt(CoordDocument c) => string.Join(",", Enumerable.Range(0, ClothesSet.Count).Select(k => PartExt(c, k)));

        /// <summary>The golden character with its own part data on top and gloves.</summary>
        static SceneSession Own(TestRunner t, out string id)
        {
            var s = SceneTests_Appearance.GoldenChara(t, out id);
            var c = Coord(s, id);
            c.Clothes.Parts[0].Ext.Set(Ns, JToken.Parse("{\"own\": true}"));
            c.Clothes.Parts[4].Ext.Set(Ns, JToken.Parse("3"));
            s.Commands.ClearHistory();
            return s;
        }

        /// <summary>The golden coordinate with other part data on top and shoes (the rest equal to the character's).</summary>
        static CoordDocument Source(TestRunner t)
        {
            var c = CharaSubtree.CloneCoord(Kit.GoldenContainer(t, true).Charas[Kit.GoldenChara].Coord);
            c.Clothes.Parts[0].Ext.Set(Ns, JToken.Parse("{\"src\": 1}"));
            c.Clothes.Parts[7].Ext.Set(Ns, JToken.Parse("[2]"));
            return c;
        }

        static CommandResult Load(SceneSession s, string id, CoordDocument src, CoordinateApplyMode mode)
        {
            var data = new FakeMakerData();
            var o = new MakerEditOptions { Rows = data, Defaults = data, NewId = s.NewId };
            using (var tx = s.Commands.BeginOrJoin("Load coordinate"))
            {
                var r = MakerEdits.ApplyCoordinate(tx, s.Model, s.Model.Find(id), src, null, mode, o, new MakerEditReport());
                if (r == null || r.Status == CommandStatus.Error) return r;
                var c = tx.Commit();
                return c.Status == CommandStatus.Error ? c : r;
            }
        }

        static void Mode(TestRunner t, CoordinateApplyMode mode)
        {
            string w = "CE7 " + mode + ": ";
            var s = Own(t, out string id);
            var src = Source(t);
            string own = AllPartExt(Coord(s, id));
            var before = SceneTests_Appearance.CoordBytes(s, id);
            var paths = new List<string>();
            Action<SceneChangeBatch> on = b => paths.AddRange(b.Changes.Where(c => c.NodeId == id && c.Kind == SceneChangeKind.NodeChanged).Select(c => c.Path));
            s.Events.Changed += on;
            CommandResult r;
            try { r = Load(s, id, src, mode); }
            finally { s.Events.Changed -= on; }
            t.Check(r != null && r.Ok, w + "load (" + r + ")");
            string after = AllPartExt(Coord(s, id));
            if (mode == CoordinateApplyMode.Accessories)
                t.Equal(own, after, w + "the character's part data kept");
            else
                t.Equal(AllPartExt(src), after, w + "the parts' data are the source's (a part without data in the source has none)");
            if (mode == CoordinateApplyMode.All)
            {
                var distinct = paths.Distinct().ToList();
                t.Check(distinct.Count == 1 && distinct[0] == SetCoordinateCommand.ExtPath,
                    w + "only the parts' data differ → the one reported path is appearance.coord.ext (" + string.Join(", ", distinct) + ")");
            }
            if (mode == CoordinateApplyMode.Clothes)
                t.Check(paths.Contains(AppearancePaths.Coord("clothes.top")) && paths.Contains(AppearancePaths.Coord("clothes.gloves")) && paths.Contains(AppearancePaths.Coord("clothes.shoes")) &&
                        !paths.Contains(AppearancePaths.Coord("clothes.bottom")), w + "the parts whose data differ are the parts set (" + string.Join(", ", paths.Distinct()) + ")");
            s.Commands.Undo();
            t.Check(Kit.Same(before, SceneTests_Appearance.CoordBytes(s, id)) && AllPartExt(Coord(s, id)) == own, w + "undo restores the character's part data byte for byte");
            s.Commands.Redo();
            t.Equal(after, AllPartExt(Coord(s, id)), w + "redo gives them again");
        }

        static void Save(TestRunner t)
        {
            var s = Own(t, out string id);
            string path = Path.Combine(t.TempDir, "ce7.hs2nscene");
            var so = new SaveOptions { Clock = () => new DateTime(2026, 10, 29, 1, 0, 0, DateTimeKind.Utc), Thumbnail = ThumbnailMode.Placeholder, Registry = new ExtRegistry() };
            var sr = s.Save(path, so);
            var raw = ContainerReader.Read(path);
            var man = JObject.Parse(Encoding.UTF8.GetString(raw.Get("manifest.json").Data));
            t.Check(sr.Ok && (int)man["formatVersion"] == 2 && (int)man["minReaderVersion"] == 1, "CE7: a scene whose character carries part data saves as format 2 (" + (sr.Error?.Message ?? "ok") + ")");
            var s2 = Kit.Session(2);
            var lr = s2.Load(path, new LoadOptions { Registry = new ExtRegistry() });
            var back = lr.Ok ? s2.Model.PreOrder().FirstOrDefault(n => n.Kind == NodeKind.Chara) : null;
            t.Check(back != null && AllPartExt(back.Appearance.Stored.Coord) == AllPartExt(Coord(s, id)), "CE7: loading it gives the part data back (" + lr + ")");
            var g = SceneTests_Appearance.GoldenChara(t, out _);
            string p1 = Path.Combine(t.TempDir, "ce7_plain.hs2nscene");
            g.Save(p1, so);
            var m1 = JObject.Parse(Encoding.UTF8.GetString(ContainerReader.Read(p1).Get("manifest.json").Data));
            t.Equal(1, (int)m1["formatVersion"], "CE7: the same scene without part data stays format 1");
        }
    }
}

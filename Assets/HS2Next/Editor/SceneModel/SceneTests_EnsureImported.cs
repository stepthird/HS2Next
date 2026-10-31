// The first edit of a card-backed character inside the transaction (Phase 4.6; MakerEdits.EnsureImported with
// SetAppearanceCommand's placeFrom): card-backed → edit → undo → save gives the bytes of the save before the edit, and the
// scene carrier holds no subtree for the node; redo → the save after the edit; a failing edit after the import (an error in
// the same transaction) leaves the node card-backed with no subtree; a carrier entry that was there before is restored by the
// undo; the importer comes from SessionOptions.Importer when none is registered, and without any importer the edit is
// unsupported.
using System;
using System.Collections.Generic;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_EnsureImported
    {
        public static void Run(TestRunner t)
        {
            var prev = CharaImport.Importer;
            CharaImport.Importer = null;
            try
            {
                UndoRedo(t);
                Failing(t);
                CarrierRestored(t);
                NoImporter(t);
            }
            finally { CharaImport.Importer = prev; }
        }

        sealed class FakeImporter : ICharaImporter
        {
            public CharaSubtree Subtree;
            public int Calls;
            public CharaContainer Import(CardAppearanceSource source, FindingList findings)
            {
                Calls++;
                var c = new CharaContainer { Subtree = Subtree.Clone() };
                c.Subtree.Import = new ImportProvenance { From = "hs2.chara", FileName = "first_edit.png", Sha256 = source.Sha256, FileSize = 1, Importer = "fake", Hs2Version = "1.0.0" };
                return c;
            }
        }

        static readonly string Shape0 = AppearancePaths.Chara("face.shape[0]");

        static SceneSession Session(TestRunner t, FakeImporter fake, out string id, out CardAppearanceSource card)
        {
            var s = Kit.Session(7, new SessionOptions { Importer = fake });
            card = Kit.Card("first edit", "UserData/chara/female/first_edit.png", 2);
            id = s.AddChara(new CharaAppearance { Card = card }).NewIds[0];
            s.Commands.ClearHistory();
            return s;
        }

        static FakeImporter Fake(TestRunner t) => new FakeImporter { Subtree = Kit.GoldenContainer(t, true).Charas[Kit.GoldenChara] };

        static CommandResult Edit(SceneSession s, string id, IReadOnlyList<(string, object)> sets)
        {
            using (var tx = s.Commands.BeginOrJoin("Edit face"))
            {
                var imp = MakerEdits.EnsureImported(tx, s.Model, s.Model.Find(id));
                if (imp.Status == CommandStatus.Error) return imp;
                var r = MakerEdits.Edit(tx, s.Model, s.Model.Find(id), sets, new MakerEditOptions(), new MakerEditReport());
                if (r.Status == CommandStatus.Error) return r;
                var c = tx.Commit();
                return c.Status == CommandStatus.Error ? c : r;
            }
        }

        static void UndoRedo(TestRunner t)
        {
            // the save before the edit (a twin session: a save imports the card-backed node silently)
            var twin = Session(t, Fake(t), out string tid, out _);
            byte[] before = twin.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder));
            var fake = Fake(t);
            var s = Session(t, fake, out string id, out var card);
            t.Equal(tid, id, "first edit: the twin sessions give the same node id");
            var r = Edit(s, id, new[] { (Shape0, (object)0.125f) });
            var a = s.Model.Find(id).Appearance;
            t.Check(r != null && r.Ok && fake.Calls == 1 && a.Stored != null && a.Card == card && s.Model.File.Charas.TryGetValue(id, out var placed) && ReferenceEquals(placed, a.Stored),
                "first edit: the card is imported, the subtree placed in the carrier and stored (" + r + ")");
            t.Equal(1, s.Commands.UndoList.Count, "first edit: one history entry (import + edit)");
            t.Check(a.Stored.Chara.Face.Shape[0] == 0.125f && a.Stored.Import?.FileName == "first_edit.png", "first edit: the edit and the provenance are on the stored subtree");
            byte[] after = s.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder));
            s.Commands.Undo();
            var u = s.Model.Find(id).Appearance;
            t.Check(u.Stored == null && u.Card == card && !s.Model.File.Charas.ContainsKey(id), "undo: the node is card-backed again and the carrier holds no subtree for it");
            byte[] undone = s.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder));
            t.Check(Kit.Same(before, undone), "undo → save = the save before the edit" + (Kit.Same(before, undone) ? "" : " (" + Kit.Diff(before, undone) + ")"));
            s.Commands.Redo();
            byte[] redone = s.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder));
            t.Check(Kit.Same(after, redone), "redo → save = the save after the edit" + (Kit.Same(after, redone) ? "" : " (" + Kit.Diff(after, redone) + ")"));
            t.Check(s.Model.Find(id).Appearance.Stored.Chara.Face.Shape[0] == 0.125f, "redo: the edit is back on the placed subtree");
        }

        static void Failing(TestRunner t)
        {
            var fake = Fake(t);
            var s = Session(t, fake, out string id, out var card);
            var r = Edit(s, id, new[] { (Shape0, (object)0.25f), (AppearancePaths.Chara("face.noSuchMember"), (object)1f) });
            var a = s.Model.Find(id).Appearance;
            t.Check(r != null && r.Status == CommandStatus.Error && fake.Calls == 1, "failing edit: the import ran, the edit failed (" + r + ")");
            t.Check(a.Stored == null && a.Card == card && !s.Model.File.Charas.ContainsKey(id) && s.Commands.UndoList.Count == 0,
                "failing edit: the transaction is cancelled; the node is card-backed with no subtree in the carrier, no entry");
        }

        static void CarrierRestored(TestRunner t)
        {
            var fake = Fake(t);
            var s = Session(t, fake, out string id, out _);
            var stale = Kit.GoldenContainer(t, true).Charas[Kit.GoldenChara].Clone();
            stale.Chara.Profile.Name = "stale carrier entry";
            s.Model.File.Charas[id] = stale;
            var r = Edit(s, id, new[] { (Shape0, (object)0.375f) });
            t.Check(r != null && r.Ok && !ReferenceEquals(s.Model.File.Charas[id], stale), "carrier: the import replaces an entry already there");
            s.Commands.Undo();
            t.Check(s.Model.File.Charas.TryGetValue(id, out var back) && ReferenceEquals(back, stale), "carrier: undo restores the entry that was there before");
        }

        static void NoImporter(TestRunner t)
        {
            var s = Kit.Session(8);
            var card = Kit.Card("no importer", "UserData/chara/female/none.png", 1);
            var id = s.AddChara(new CharaAppearance { Card = card }).NewIds[0];
            var r = Edit(s, id, new[] { (Shape0, (object)0.5f) });
            t.Check(r != null && r.Status == CommandStatus.Error && r.Code == CommandCodes.Unsupported && s.Model.Find(id).Appearance.Stored == null,
                "no importer: the first edit is unsupported, nothing changes (" + r + ")");
        }
    }
}

// Load character with HS2's five load flags (the Edit tab's File node and the bridge; Phase 4.6): «顔» face, «体» body,
// «髪» hair, «コーデ» coordinate, «キャラ設定» profile from a character file (.hs2nchara) or an HS2 card (imported in
// memory). All five replace the whole appearance exactly as Change character does (both documents, both ext scopes, the
// resources and the import provenance; the node keeps its id, transform, children, animation, FK / IK, face, look, state
// and name). Fewer replace the chosen sections of the stored documents (SetCharaSectionsCommand: plugin data by the
// face / body rule of HeadNames) and, with «コーデ», wear the source's coordinate as the coordinate load «全て読込み» does
// (with the accessory visibility steps); the character keeps its own chara-scope ext data, raw plugin copies and import
// provenance (HS2 copies its user and data ids only with all five). A partial load needs a source of the character's sex;
// a card-backed character is imported first in the same entry. One history entry "Load character".
using System;
using System.Collections.Generic;
using System.IO;
using HS2Next.Save;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>What StudioOps.LoadCharacter read and did: the file, the flags, the source's sex and name, whether the source
    /// was an HS2 card (imported) rather than a character file, the target nodes that were card-backed and imported first, and
    /// the reader's / importer's findings of warning and error severity.</summary>
    public sealed class CharacterLoadSource
    {
        public string File;
        public LoadFlags Flags;
        public int Sex;
        public string Name;
        /// <summary>The source was an HS2 card, imported in memory.</summary>
        public bool Imported;
        /// <summary>Target nodes that were card-backed and were imported in the same entry.</summary>
        public readonly List<string> ImportedTargets = new List<string>();
        public readonly List<string> Warnings = new List<string>();
    }

    public sealed partial class StudioOps
    {
        /// <summary>The reason text of a partial load from a character of the other sex (bad_args `path`).</summary>
        public const string LoadOtherSexCode = "load.otherSex";

        /// <summary>
        /// Loads the parts <paramref name="flags"/> of the character file or HS2 card at <paramref name="path"/> into every
        /// character of <paramref name="ids"/> (one entry "Load character"; see the file comment). bad_args: `keys` (no
        /// character), `flags` (none of the five, or an unknown bit), `path` (missing, unreadable, an import that failed, or a
        /// source of the other sex unless all five are on — reason <see cref="LoadOtherSexCode"/>). Value = MakerResult with
        /// <see cref="MakerResult.Load"/>; Changed lists the member paths each target reported.
        /// </summary>
        public OpResult LoadCharacter(IEnumerable<string> ids, string path, LoadFlags flags, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "keys", out var fail);
            if (nodes == null) return fail;
            if (nodes.Count == 0) return OpResult.BadArgs("keys", "character node ids", "no character keys");
            if ((flags & LoadFlags.All) == LoadFlags.None || (flags & ~LoadFlags.All) != 0)
                return OpResult.BadArgs("flags", "one or more of face, body, hair, coordinate, profile");
            var found = new FindingList();
            var cc = ReadCharacterFile(path, found, out bool imported, out fail);
            if (cc == null) return fail;
            var src = cc.Subtree;
            int sex = src.Chara.Profile.Sex;
            bool all = LoadFlagNames.IsAll(flags);
            if (!all)
                foreach (var n in nodes)
                {
                    int ts = SexOf(n);
                    if (ts >= 0 && ts != sex)
                    {
                        var bad = OpResult.BadArgs("path", "a character of the same sex",
                            LoadOtherSexCode + ": a partial load needs a character of the same sex (the file is " + (sex == 0 ? "male" : "female") +
                            ", character " + n.Id + " is " + (ts == 0 ? "male" : "female") + "; load all five parts to change the sex)");
                        bad.Extra = new Dictionary<string, object> { ["reason"] = LoadOtherSexCode };
                        return bad;
                    }
                }
            MakerServices.UseSession(Session);
            rt.ApplyNow();
            var tx = Open(Labels.LoadCharacter, o, out fail);
            if (tx == null) return fail;
            var rep = new MakerEditReport();
            var info = new CharacterLoadSource { File = path, Flags = flags, Sex = sex, Name = src.Chara.Profile.Name, Imported = imported };
            OpResult result;
            using (tx)
            {
                if (all)
                {
                    foreach (var n in nodes)
                    {
                        // the file's subtree is placed in the scene file under the node too (what a save of the character
                        // extracts before the next scene save), and taken back on undo
                        var res = tx.Execute(new SetAppearanceCommand(n.Id, new CharaAppearance(), Labels.LoadCharacter, cc));
                        if (res.Status == CommandStatus.Error) return OpResult.From(res, "path");
                        rep.Record(n.Id, "appearance", res.Status, false);
                    }
                }
                else
                {
                    Model.File.Res.CopyFrom(cc.Res, src.ReferencedResources());
                    var opts = MakerOptions(false, nodes.Count > 1);
                    var chara = flags & SetCharaSectionsCommand.Allowed;
                    foreach (var n in nodes)
                    {
                        bool wasCard = n.Appearance?.Stored == null && n.Appearance?.Card != null;
                        var imp = MakerEdits.EnsureImported(tx, Model, n, found);
                        if (imp.Status == CommandStatus.Error) return OpResult.From(imp, "keys");
                        if (wasCard && n.Appearance?.Stored != null) info.ImportedTargets.Add(n.Id);
                        if (chara != LoadFlags.None)
                        {
                            var cmd = new SetCharaSectionsCommand(n.Id, src.Chara, chara, HeadNames.Current, Labels.LoadCharacter);
                            var res = tx.Execute(cmd);
                            if (res.Status == CommandStatus.Error) return OpResult.From(res, "path");
                            if (res.Status == CommandStatus.Ok) foreach (var p in cmd.Reported) rep.Record(n.Id, p, res.Status, false);
                        }
                        if ((flags & LoadFlags.Coordinate) != 0)
                        {
                            var res = MakerEdits.ApplyCoordinate(tx, Model, n, src.Coord, src.CoordExt, CoordinateApplyMode.All, opts, rep);
                            if (res.Status == CommandStatus.Error) return OpResult.From(res, "path");
                        }
                    }
                }
                result = Commit(tx, Fill(OpResult.NothingToDo(), rep));
            }
            rt.ApplyNow();
            foreach (var f in found)
                if (f.Severity != FindingSeverity.Info) info.Warnings.Add(f.ToString());
            if (result.Value is MakerResult mr) mr.Load = info;
            else if (result.Ok || result.Status == CommandStatus.NoOp) result.Value = new MakerResult { Load = info };
            return result;
        }

        /// <summary>
        /// A character source in memory: a .hs2nchara file through the reader, any other file as an HS2 card through the card
        /// importer (ImportSetup.ImporterOf, else the session's). Null with <paramref name="fail"/> (bad_args `path`) when the
        /// file is missing or unreadable or the import fails (finding chara.importFailed in <paramref name="found"/>).
        /// </summary>
        CharaContainer ReadCharacterFile(string path, FindingList found, out bool imported, out OpResult fail)
        {
            fail = null;
            imported = false;
            if (string.IsNullOrEmpty(path)) { fail = OpResult.BadArgs("path", "a card or .hs2nchara path"); return null; }
            if (!File.Exists(path)) { fail = OpResult.BadArgs("path", "an existing file", "character file not found: " + path); return null; }
            if (path.EndsWith(CharaFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                LoadResult<CharaContainer> lr;
                try { lr = CharaContainer.Load(path); }
                catch (Exception e) { fail = OpResult.BadArgs("path", null, "character file not readable: " + e.GetType().Name + ": " + e.Message); return null; }
                found.AddRange(lr.Findings);
                if (!lr.Ok) { fail = OpResult.BadArgs("path", null, "character file not readable (" + Path.GetFileName(path) + "): " + lr.Error.Message); return null; }
                return lr.Value;
            }
            CardAppearanceSource card;
            try { card = Services.CardSources.FromFile(path); }
            catch (Exception e) { fail = OpResult.BadArgs("path", null, "card not readable: " + e.GetType().Name + ": " + e.Message); return null; }
            var importer = ImportSetup.ImporterOf(Services) ?? Session.Importer;
            string why = null;
            CharaContainer cc = null;
            if (importer == null) why = "no card importer";
            else
            {
                try { cc = importer.Import(card, found); }
                catch (Exception e) { why = "the importer threw " + e.GetType().Name + ": " + e.Message; }
                if (cc?.Subtree == null && why == null) why = "the importer returned nothing";
            }
            if (why != null)
            {
                found.Error("chara.importFailed", "", why);
                fail = OpResult.BadArgs("path", null, "card not imported (" + Path.GetFileName(path) + "): " + why);
                return null;
            }
            imported = true;
            return cc;
        }
    }
}

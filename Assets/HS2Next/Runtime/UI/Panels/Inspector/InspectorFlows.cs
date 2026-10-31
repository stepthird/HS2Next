// The inspector buttons that open a flow instead of one op: the browsers of package C
// (animation, pose, character change, map) are focused; pose save prompts for a name and writes an HS2Next pose file
// (PoseFragmentOps.Save, not recorded). Phase 4.6 (the Edit tab's File page and the Studio inspector's «保存»):
//   character save — a name prompt whose initial text is the character's name (profile.name made file-safe; else the node's
//     name; else a yyyy_MMdd_HHmm_ss stamp), then <UserData>/chara/<name>.hs2nchara (an existing file is asked about first)
//     through StudioOps.SaveChara with the thumbnail framing of the view-state option maker.thumbnailFraming (default the
//     current view); the character browsers of that sex list the file afterwards;
//   coordinate save — a prompt «コーデ名» whose initial text is the worn coordinate's name (else a stamp), then
//     <UserData>/coordinate/<female|male>/<name>.hs2ncoord of the character's sex (an existing file is asked about first)
//     through StudioOps.SaveCoordinate: the typed name becomes `coord.name` in the file only, the node is untouched; the
//     coordinate browsers of that sex list the file afterwards.
// Saves are never recorded. The prompts and questions are the 3.5 dialogs; their ids are their title keys (`ui.state modal`).
using System;
using System.Collections.Generic;
using System.IO;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;

namespace HS2Next.UI.Panels
{
    public static class InspectorFlows
    {
        /// <summary>The dialog ids (= title keys) of the 4.6 save flows: the character name prompt, the coordinate name prompt
        /// and the overwrite question of both.</summary>
        public const string SaveCharaPromptId = "inspector.charaEdit.file.saveCharaTitle", SaveCoordPromptId = "inspector.charaEdit.file.saveCoordTitle",
            OverwriteQuestionId = "inspector.charaEdit.file.overwriteTitle";

        /// <summary>The result of the last character / coordinate save of a flow (tests; null before).</summary>
        public static OpResult LastCharaSave { get; private set; }
        public static OpResult LastCoordSave { get; private set; }

        /// <summary>Focuses a panel (C's browsers); a toast names it when the shell does not know it.</summary>
        public static OpResult Focus(UiContext ctx, string panelId, string hintKey = null)
        {
            bool ok = ctx?.Shell != null && ctx.Shell.FocusPanel(panelId);
            if (hintKey != null && ok) ctx.Toasts?.Show(L10n.Get(hintKey), ToastKind.Info);
            return ok ? OpResult.Success() : OpResult.Unsupported("no panel " + panelId);
        }

        public static OpResult BrowseAnime(UiContext ctx) => Focus(ctx, "add.anime");
        public static OpResult BrowseMap(UiContext ctx) => Focus(ctx, "add.map");
        /// <summary>The pose browser (C) filtered to the primary character's sex.</summary>
        public static OpResult BrowsePoses(UiContext ctx, IReadOnlyList<string> targets)
        {
            string id = targets != null && targets.Count > 0 ? targets[0] : null;
            var n = id != null ? ctx?.Model.Find(id) : null;
            int sex = n != null ? StudioOps.SexOf(n) : -1;
            var poses = AddModule.Poses;
            if (poses != null) poses.SexFilter = sex >= 0 ? sex : (int?)null;
            return Focus(ctx, "add.pose");
        }

        /// <summary>Change character…: the character browser (C's change flow on the selected characters).</summary>
        public static OpResult ChangeCharacter(UiContext ctx, IReadOnlyList<string> targets) => Focus(ctx, "add.chara", "inspector.chara.character.changeHint");

        // ---------------------------------------------------------------- character save

        /// <summary>The thumbnail framing of the view-state option maker.thumbnailFraming (the current view when unset).</summary>
        public static CharaThumbnailFraming Framing(UiContext ctx)
        {
            string s = null;
            try { if (ctx?.Options != null && OptionDefs.TryGet(OptionDefs.MakerThumbnailFraming, out _)) s = ctx.Options.String(OptionDefs.MakerThumbnailFraming); }
            catch (Exception) { s = null; }
            return s == "front" ? CharaThumbnailFraming.Front : CharaThumbnailFraming.View;
        }

        /// <summary>The initial text of the character name prompt: the character's name made file-safe, else the node's name, else
        /// a yyyy_MMdd_HHmm_ss stamp.</summary>
        public static string CharaInitialName(UiContext ctx, string id)
        {
            var n = id != null ? ctx?.Model.Find(id) : null;
            string p = n?.Appearance?.Stored?.Chara?.Profile?.Name;
            if (!string.IsNullOrWhiteSpace(p) && Safe(p.Trim(), null) is string sp) return sp;
            string l = n?.Label;
            if (!string.IsNullOrWhiteSpace(l) && Safe(l.Trim(), null) is string sl) return sl;
            return Stamp();
        }

        /// <summary>Save character: a name prompt, then the primary as &lt;UserData&gt;/chara/&lt;name&gt;.hs2nchara (an existing
        /// file is confirmed first; StudioOps.SaveChara with the framing option, not recorded).</summary>
        public static OpResult SaveCharacter(UiContext ctx, IReadOnlyList<string> targets)
        {
            string id = targets != null && targets.Count > 0 ? targets[0] : null;
            var dir = ctx?.App?.Paths?.CharaDir;
            if (id == null || dir == null || ctx.Dialogs == null) return OpResult.NothingToDo();
            ctx.Dialogs.Prompt(SaveCharaPromptId, CharaInitialName(ctx, id), name =>
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                string safe = Safe(name.Trim(), "chara");
                string full = Path.Combine(dir, safe + StudioOps.CharaFileExtension);
                if (File.Exists(full))
                    ctx.Dialogs.Confirm(OverwriteQuestionId, "inspector.charaEdit.file.overwriteMessage", ok => { if (ok) WriteChara(ctx, id, full); }, safe + StudioOps.CharaFileExtension);
                else WriteChara(ctx, id, full);
            }, "inspector.charaEdit.file.saveCharaMessage");
            return OpResult.Success();
        }

        /// <summary>Writes the character file now with the framing option (also used by the tests); an existing file is replaced.</summary>
        public static OpResult WriteChara(UiContext ctx, string id, string fullPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            var r = ctx.ShowResult(ctx.Ops.SaveChara(id, fullPath, true, Framing(ctx)));
            LastCharaSave = r;
            if (r.Ok)
            {
                ctx.Toasts?.Show(L10n.Format("inspector.charaEdit.file.savedChara", Path.GetFileName(fullPath)), ToastKind.Success);
                int sex = StudioOps.SexOf(ctx.Model.Find(id));
                CoordinateBrowser.Invalidate(ctx, FileBrowserKind.Character, sex < 0 ? 1 : sex);
            }
            return r;
        }

        // ---------------------------------------------------------------- coordinate save

        /// <summary>The initial text of the coordinate name prompt: the worn coordinate's name, else a yyyy_MMdd_HHmm_ss stamp.</summary>
        public static string CoordInitialName(UiContext ctx, string id)
        {
            string cn = (id != null ? ctx?.Model.Find(id) : null)?.Appearance?.Stored?.Coord?.Name;
            return string.IsNullOrWhiteSpace(cn) ? Stamp() : cn.Trim();
        }

        /// <summary>The coordinate file of a typed name for character <paramref name="id"/>: &lt;UserData&gt;/coordinate/&lt;female|
        /// male&gt;/&lt;file-safe name&gt;.hs2ncoord of the character's sex; null without a user data folder.</summary>
        public static string CoordPath(UiContext ctx, string id, string typedName)
        {
            int sex = StudioOps.SexOf(id != null ? ctx?.Model.Find(id) : null);
            string dir = CoordinateBrowser.OwnDir(ctx, sex < 0 ? 1 : sex);
            return dir == null ? null : Path.Combine(dir, Safe((typedName ?? "").Trim(), "coordinate") + SaveFormats.CoordExtension);
        }

        /// <summary>Save outfit (the File page's «コーデを保存…», the Clothes page's «保存…»): a prompt «コーデ名», then the primary's
        /// worn coordinate as &lt;UserData&gt;/coordinate/&lt;sex&gt;/&lt;name&gt;.hs2ncoord (an existing file is confirmed first;
        /// StudioOps.SaveCoordinate, not recorded; the typed name is the file's coord.name).</summary>
        public static OpResult SaveCoordinate(UiContext ctx, IReadOnlyList<string> targets)
        {
            string id = targets != null && targets.Count > 0 ? targets[0] : null;
            if (id == null || ctx?.Dialogs == null || CoordPath(ctx, id, "x") == null) return OpResult.NothingToDo();
            ctx.Dialogs.Prompt(SaveCoordPromptId, CoordInitialName(ctx, id), name =>
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                string typed = name.Trim();
                string full = CoordPath(ctx, id, typed);
                if (File.Exists(full))
                    ctx.Dialogs.Confirm(OverwriteQuestionId, "inspector.charaEdit.file.overwriteMessage", ok => { if (ok) WriteCoord(ctx, id, full, typed); }, Path.GetFileName(full));
                else WriteCoord(ctx, id, full, typed);
            }, "inspector.charaEdit.file.saveCoordMessage");
            return OpResult.Success();
        }

        /// <summary>Writes the coordinate file now with <paramref name="coordName"/> as its coord.name (also used by the tests); an
        /// existing file is replaced.</summary>
        public static OpResult WriteCoord(UiContext ctx, string id, string fullPath, string coordName)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            var r = ctx.ShowResult(ctx.Ops.SaveCoordinate(id, fullPath, coordName, true));
            LastCoordSave = r;
            if (r.Ok)
            {
                ctx.Toasts?.Show(L10n.Format("inspector.charaEdit.file.savedCoord", Path.GetFileName(fullPath)), ToastKind.Success);
                int sex = StudioOps.SexOf(ctx.Model.Find(id));
                CoordinateBrowser.Invalidate(ctx, FileBrowserKind.Coordinate, sex < 0 ? 1 : sex);
            }
            return r;
        }

        // ---------------------------------------------------------------- pose save

        /// <summary>Save pose: a name prompt, then the primary's pose as &lt;UserData&gt;/pose/&lt;name&gt;.hs2npose (overwrite confirmed).</summary>
        public static OpResult SavePose(UiContext ctx, IReadOnlyList<string> targets)
        {
            string id = targets != null && targets.Count > 0 ? targets[0] : null;
            var dir = ctx?.App?.Paths?.PoseDir;
            if (id == null || dir == null || ctx.Dialogs == null) return OpResult.NothingToDo();
            string initial = Stamp();
            ctx.Dialogs.Prompt("inspector.chara.pose.saveTitle", initial, name =>
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                string safe = Safe(name.Trim(), "pose");
                string full = Path.Combine(dir, safe + SaveFormats.PoseExtension);
                if (File.Exists(full))
                    ctx.Dialogs.Confirm("inspector.chara.pose.overwriteTitle", "inspector.chara.pose.overwrite", ok => { if (ok) WritePose(ctx, id, full, name.Trim(), true); }, safe);
                else WritePose(ctx, id, full, name.Trim(), false);
            }, "inspector.chara.pose.saveMessage");
            return OpResult.Success();
        }

        /// <summary>Writes the pose file now (also used by the tests).</summary>
        public static PoseFragmentSaveResult WritePose(UiContext ctx, string id, string fullPath, string name, bool overwrite)
        {
            ctx.Runtime.ApplyNow();
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            var res = PoseFragmentOps.Save(ctx.Runtime, id, fullPath, name, null, overwrite);
            if (res.Ok) ctx.Toasts?.Show(L10n.Format("inspector.chara.pose.saved", Path.GetFileName(res.Path ?? fullPath)), ToastKind.Success);
            else ctx.Toasts?.Show(L10n.Format("msg.opFailed", res.Code ?? "error", res.Error ?? ""), ToastKind.Error);
            return res;
        }

        static string Stamp() => DateTime.Now.ToString("yyyy_MMdd_HHmm_ss");

        /// <summary>A file-safe name: every character a file name cannot hold becomes '_'; <paramref name="fallback"/> when nothing
        /// is left.</summary>
        public static string Safe(string name, string fallback)
        {
            name ??= "";
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim();
            return name.Length == 0 ? fallback : name;
        }
    }
}

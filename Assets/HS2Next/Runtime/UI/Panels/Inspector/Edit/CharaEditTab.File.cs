// The File page of the Edit tab (Phase 4.6): one page, two sections, built from the schema rows of the File node with the
// page's own widgets:
//   «キャラクター» (`edit-section-file.file.character`): «名前を付けて保存…» (`file-save-chara`: the name prompt with the
//     character's name, the overwrite question, <UserData>/chara/<name>.hs2nchara in the framing below; InspectorFlows), the
//     thumbnail framing «正面» / «現在の視点» (`field-maker.thumbnailFraming`, buttons `-0` front / `-1` view; the view-state
//     option maker.thumbnailFraming, default the current view), the preview with «撮影し直す» (ThumbnailPreview: rendered when
//     the page is shown, on «撮影し直す» and on a framing change, never on an edit), «キャラを読込…» (`file-load-chara`: the
//     character load dialog of the primary's sex with the five parts, one entry "Load character");
//   «コーデ» (`edit-section-file.file.coordinate`): «コーデを保存…» (`file-save-coord`: the prompt «コーデ名», the overwrite
//     question, <UserData>/coordinate/<female|male>/<name>.hs2ncoord, the typed name in the file only) and «コーデを読込…»
//     (`file-load-coord`: the Clothes page's coordinate dialog with HS2's three modes, one entry "Load coordinate").
// The page acts on the primary character only: with more than one character selected its body (`file-page-body`) is
// disabled and the note `file-multi-note` says why. A card-backed primary keeps the import note above the page and every
// button works (a save imports the card, a load imports the character first). Saves and the preview are never recorded.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed partial class CharaEditTab
    {
        public const string FileNode = "file";

        ThumbnailPreview filePreview;
        SegmentedButtons fileFraming;
        VisualElement fileBody;
        Label fileMultiNote;
        int fileSex = 1;
        Action<string> fileOptionHook;

        /// <summary>The File page's thumbnail preview (null until the page is built).</summary>
        public ThumbnailPreview FilePreview => filePreview;
        /// <summary>The File page's framing buttons («正面», «現在の視点»; null until built).</summary>
        public SegmentedButtons FileFraming => fileFraming;
        /// <summary>True while the File page is disabled because more than one character is selected.</summary>
        public bool FileMultiDisabled { get; private set; }
        /// <summary>The last result of a File page button (a flow start or a load; tests).</summary>
        public OpResult LastFileResult { get; private set; }

        VisualElement BuildFilePage(VisualElement c, List<SchemaSection> sections)
        {
            var sw = Stopwatch.StartNew();
            int sex = StudioOps.SexOf(ctx.Model.Find(primary));
            fileSex = sex < 0 ? 1 : sex;
            fileMultiNote = InspectorUi.Note("inspector.charaEdit.file.multi");
            fileMultiNote.name = "file-multi-note";
            fileMultiNote.style.display = DisplayStyle.None;
            c.Add(fileMultiNote);
            fileBody = new VisualElement { name = "file-page-body" };
            c.Add(fileBody);
            foreach (var s in sections)
            {
                var fo = InspectorUi.Section(s.LabelKey, true, "edit-section-" + s.Id);
                sectionFoldouts[s.Id] = fo;
                VisualElement line = null;
                foreach (var f in s.Fields)
                {
                    var e = BuildFileRow(f);
                    if (e == null) continue;
                    if (e is Button)
                    {
                        // consecutive buttons share a wrapping line
                        if (line == null) { line = new VisualElement(); line.AddToClassList("hs-file-buttons"); fo.Add(line); }
                        line.Add(e);
                    }
                    else { line = null; fo.Add(e); }
                }
                fileBody.Add(fo);
            }
            fileOptionHook = k => { if (k == OptionDefs.MakerThumbnailFraming) OnFramingOption(); };
            c.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                ctx.SelectionChanged -= OnFileSelection; ctx.SelectionChanged += OnFileSelection;
                if (ctx.Options != null) { ctx.Options.Changed -= fileOptionHook; ctx.Options.Changed += fileOptionHook; }
                UpdateFileMulti();
                ShowFraming();
            });
            c.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                ctx.SelectionChanged -= OnFileSelection;
                if (ctx.Options != null) ctx.Options.Changed -= fileOptionHook;
            });
            UpdateFileMulti();
            PageBuildMs[FileNode + ".file"] = sw.Elapsed.TotalMilliseconds;
            return c;
        }

        /// <summary>The element of one File row: the buttons, the framing choice, the preview (the «撮影し直す» row).</summary>
        VisualElement BuildFileRow(FieldSpec f)
        {
            if (f.Target == FieldTarget.Option && f.Path == InspectorSchemas.ThumbnailFramingOption) return FramingRow(f);
            if (f.Kind != FieldKind.Button) return null;
            switch (f.Action)
            {
                case InspectorSchemas.ActionSaveCharacter: return FileButton(f, "file-save-chara", () => SaveCharacterFile());
                case InspectorSchemas.ActionRetakeThumbnail:
                    filePreview = new ThumbnailPreview(ctx, primary, () => InspectorFlows.Framing(ctx), f.LabelKey);
                    return filePreview;
                case InspectorSchemas.ActionLoadCharacter: return FileButton(f, "file-load-chara", () => { OpenCharaLoadDialog(); });
                case InspectorSchemas.ActionSaveCoordinate: return FileButton(f, "file-save-coord", () => SaveCoordinateFile());
                case InspectorSchemas.ActionApplyCoordinate: return FileButton(f, "file-load-coord", () => { OpenFileCoordinateDialog(); });
                default: return null;
            }
        }

        static Button FileButton(FieldSpec f, string name, Action click)
        {
            var b = InspectorUi.Button(f.LabelKey, click, name);
            if (f.TooltipKey != null) Loc.Tooltip(b, f.TooltipKey);
            return b;
        }

        /// <summary>«正面» / «現在の視点»: the label and two segmented buttons named as a Maker choice row; a click writes the
        /// view-state option (not the document).</summary>
        VisualElement FramingRow(FieldSpec f)
        {
            var row = new VisualElement { name = "field-" + f.Path };
            row.AddToClassList(HS2Next.UI.Binding.FieldRow.UssClass);
            row.AddToClassList(MakerChoiceRow.UssClass);
            var l = new Label(L10n.Get(f.LabelKey));
            l.AddToClassList("hs-number__label");
            Loc.Bind(l, f.LabelKey);
            row.Add(l);
            int n = f.Choices?.Count ?? 0;
            var labels = new string[n];
            for (int i = 0; i < n; i++) labels[i] = L10n.Get(f.ChoiceLabelKey(i));
            fileFraming = new SegmentedButtons(labels);
            for (int i = 0; i < n; i++)
            {
                fileFraming.Buttons[i].name = row.name + "-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Loc.Bind(fileFraming.Buttons[i], f.ChoiceLabelKey(i));
            }
            fileFraming.Selected += i => SetThumbnailFraming(f.Choices[i] == "front" ? CharaThumbnailFraming.Front : CharaThumbnailFraming.View);
            row.Add(fileFraming);
            ShowFraming();
            return row;
        }

        void ShowFraming() => fileFraming?.SetDisplay(InspectorFlows.Framing(ctx) == CharaThumbnailFraming.Front ? 0 : 1, false);

        /// <summary>Sets the framing option (a framing button; tests); the preview renders again when it changed.</summary>
        public void SetThumbnailFraming(CharaThumbnailFraming framing)
        {
            if (ctx.Options == null || !OptionDefs.TryGet(OptionDefs.MakerThumbnailFraming, out _)) return;
            string v = framing == CharaThumbnailFraming.Front ? "front" : "view";
            if (ctx.Options.String(OptionDefs.MakerThumbnailFraming) == v) { ShowFraming(); return; }
            ctx.Options.Set(OptionDefs.MakerThumbnailFraming, v);   // OnFramingOption follows through Options.Changed
        }

        void OnFramingOption()
        {
            ShowFraming();
            if (filePreview != null && filePreview.panel != null) filePreview.Render();
        }

        void OnFileSelection(SelectionChangedArgs _) => UpdateFileMulti();

        void UpdateFileMulti()
        {
            if (fileBody == null) return;
            int charas = 0;
            foreach (var id in ctx.Selection.Ids) if (ctx.Model.Find(id)?.Kind == NodeKind.Chara) charas++;
            FileMultiDisabled = charas > 1;
            fileBody.SetEnabled(!FileMultiDisabled);
            fileBody.tooltip = FileMultiDisabled ? L10n.Get("inspector.charaEdit.file.multi") : null;
            if (fileMultiNote != null) fileMultiNote.style.display = FileMultiDisabled ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>«名前を付けて保存…»: the character save flow of the primary (the prompt opens; tests answer it).</summary>
        public OpResult SaveCharacterFile() =>
            LastFileResult = FileMultiDisabled ? OpResult.NothingToDo() : InspectorFlows.SaveCharacter(ctx, new[] { primary });

        /// <summary>«コーデを保存…»: the coordinate save flow of the primary (the prompt opens; tests answer it).</summary>
        public OpResult SaveCoordinateFile() =>
            LastFileResult = FileMultiDisabled ? OpResult.NothingToDo() : InspectorFlows.SaveCoordinate(ctx, new[] { primary });

        /// <summary>«キャラを読込…»: the character load dialog of the primary's sex; «読込» loads the chosen parts of the chosen file
        /// into the primary (StudioOps.LoadCharacter, one entry "Load character"). Null without a dialog layer.</summary>
        public CharaLoadDialog OpenCharaLoadDialog()
        {
            if (ctx.Dialogs == null || FileMultiDisabled) return null;
            int sex = StudioOps.SexOf(ctx.Model.Find(primary));
            fileSex = sex < 0 ? fileSex : sex;
            return CharaLoadDialog.Open(ctx, fileSex, (path, flags) =>
                LastFileResult = ctx.ShowResult(ctx.Ops.LoadCharacter(new[] { primary }, path, flags, UiContext.User(OpLabels.LoadCharacter))));
        }

        /// <summary>«コーデを読込…»: the coordinate dialog of the primary's sex with HS2's three modes, loading into the primary
        /// (StudioOps.ApplyCoordinateFile, one entry "Load coordinate"). Null without a dialog layer.</summary>
        public CoordinateDialog OpenFileCoordinateDialog()
        {
            if (ctx.Dialogs == null || FileMultiDisabled) return null;
            int sex = StudioOps.SexOf(ctx.Model.Find(primary));
            fileSex = sex < 0 ? fileSex : sex;
            return CoordinateDialog.Open(ctx, fileSex, (path, mode) =>
                LastFileResult = ctx.ShowResult(ctx.Ops.ApplyCoordinateFile(new[] { primary }, path, mode, UiContext.User(OpLabels.LoadCoordinate))));
        }

        /// <summary>The facts of `ui.state makerFile`: the shown preview, the open load dialog, the two view-state options.</summary>
        public static Dictionary<string, object> FileUiState(UiContext ui)
        {
            var tab = Current;
            return new Dictionary<string, object>
            {
                ["node"] = tab?.CurrentNode,
                ["primary"] = tab?.PrimaryId,
                ["multiDisabled"] = tab != null && tab.FileMultiDisabled,
                ["preview"] = ThumbnailPreview.Current?.State(),
                ["loadDialog"] = CharaLoadDialog.Current?.State(),
                ["thumbnailFraming"] = InspectorFlows.Framing(ui) == CharaThumbnailFraming.Front ? "front" : "view",
                ["loadFlags"] = LoadFlagNames.ToNames(CharaLoadDialog.Remembered(ui)),
            };
        }
    }
}

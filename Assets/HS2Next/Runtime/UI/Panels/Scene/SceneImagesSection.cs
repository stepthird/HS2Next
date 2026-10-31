// The scene panel's Background / frame section (Phase 3.6 package C), replacing
// the 3.5 placeholder: two picker rows (FilePicker kinds `background` and `frame`: the image browser lists HS2's
// DefaultData/bg and UserData/bg, HS2Next's bg / HS2's UserData/frame and HS2Next's frame) with "×" = none, the stored
// reference shown by name ("embedded W × H" for res refs, "missing" / "loading" from the runtime part), a small preview of
// the loaded texture, and the clear-colour shortcut to Options (render.backgroundColor, P's button row). A picked path
// becomes a reference through FileRefs.FromPicked (DefaultData referenced, other files embedded unless images.embed is
// off); writes go through StudioOps.SetBackground / SetFrame (one entry "Background" / "Frame").
using System;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using HS2Next.Screenshots;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public static class SceneImagesSection
    {
        /// <summary>The section (name "section-images") for the scene panel.</summary>
        public static VisualElement Build(UiContext ctx)
        {
            var fo = InspectorUi.Section("inspector.scene.section.images", false, "section-images");
            fo.Add(Row(ctx, "background"));
            fo.Add(Row(ctx, "frame"));
            var clear = InspectorUi.Button("inspector.scene.images.clearColour", () => ctx.Registry.Run(ActionIds.FileOptions, "scene",
                new System.Collections.Generic.Dictionary<string, object> { ["option"] = OptionDefs.RenderBackgroundColor }), "images-clear-colour");   // G36: opens the options at the clear-colour row
            fo.Add(InspectorUi.Line(clear));
            return fo;
        }

        static SceneImages.Slot SlotOf(UiContext ctx, string field)
        {
            var im = ctx.Runtime?.GetPart<SceneImages>();
            return im == null ? null : field == "frame" ? im.Frame : im.Background;
        }

        static VisualElement Row(UiContext ctx, string field)
        {
            var row = new VisualElement { name = "images-" + field };
            row.AddToClassList(InspectorUi.RowClass);
            var line = new VisualElement();
            line.style.flexDirection = FlexDirection.Row;
            line.style.alignItems = Align.Center;
            var fp = new FilePicker(L10n.Get("inspector.scene.images." + field)) { Kind = field, name = "images-" + field + "-picker" };
            Loc.Bind(fp.LabelElement, "inspector.scene.images." + field);
            fp.style.flexGrow = 1;
            line.Add(fp);
            var preview = new Image { name = "images-" + field + "-preview", scaleMode = ScaleMode.ScaleToFit };
            preview.style.width = 64;
            preview.style.height = 36;
            preview.style.marginLeft = 4;
            line.Add(preview);
            row.Add(line);
            var state = new Label { name = "images-" + field + "-state" };
            state.AddToClassList(InspectorUi.Dim);
            row.Add(state);

            FileRef stored = null;
            Texture shown = null;
            string shownState = null;
            void Refresh()
            {
                var s = SlotOf(ctx, field);
                var tex = s?.Texture;
                string st = s?.State ?? SceneImages.StateNone;
                if (tex == shown && st == shownState) return;
                shown = tex;
                shownState = st;
                preview.image = tex;
                preview.style.display = tex ? DisplayStyle.Flex : DisplayStyle.None;
                string text = stored == null ? null : stored.Kind == FileRef.Kinds.Res
                    ? L10n.Format("inspector.scene.images.embedded", s?.Width ?? 0, s?.Height ?? 0)
                    : System.IO.Path.GetFileName(stored.Value ?? "");
                fp.SetDisplay(text, false);
                fp.FileLabel.tooltip = FileRefs.Describe(stored) ?? "";
                state.text = st == SceneImages.StateMissing ? L10n.Format("inspector.scene.images.missing", s?.Error ?? "")
                    : st == SceneImages.StateFailed ? L10n.Format("inspector.scene.images.failed", s?.Error ?? "")
                    : st == SceneImages.StateLoading ? L10n.Get("inspector.scene.images.loading") : "";
                state.style.display = state.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }

            ctx.Bindings.Bind(row, TargetSelector.Scene, field, (el, v) =>
            {
                stored = v.Value as FileRef;
                shownState = "\u0000";   // force a refresh of the text
                Refresh();
            });
            PanelKit.EveryFrame(row, Refresh);
            fp.Picked += p =>
            {
                FileRef f = null;
                if (p != null)
                {
                    var fc = FileRefContext.Of(ctx.Runtime, CaptureSettings.Embed);
                    if (!FileRefs.TryFromPicked(p, field, fc, out f, out var why))
                    {
                        ctx.Toasts?.Show(why, ToastKind.Error);
                        return;
                    }
                }
                var o = UiContext.User();
                ctx.ShowResult(field == "frame" ? ctx.Ops.SetFrame(f, o) : ctx.Ops.SetBackground(f, o));
            };
            return row;
        }
    }
}

// The File page's thumbnail preview (Phase 4.6): an image of 126 × 176 (a quarter of the 504 × 704 render) showing the texture a
// character save would encode (StudioOps.RenderCharaThumbnail in the framing the page's option names), a note line and
// «撮影し直す». It renders when it is shown (a frame after its page is attached, once the character is built), on «撮影し直す»
// and when the page asks (a framing change) — never on an edit. The texture is destroyed when a new one replaces it and when
// the preview leaves the panel (the node is hidden). Nothing is recorded; the render is the save's own capture (isolated, the
// front or the view framing). `ui.state makerFile.preview` reports {width, height, framing, renderedAtFrame, …}. Element
// names: `file-thumb-preview` (the row), `file-thumb-image`, `file-thumb-note`, `file-retake`.
using System;
using System.Collections.Generic;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace HS2Next.UI.Panels
{
    public sealed class ThumbnailPreview : VisualElement
    {
        public const string UssClass = "hs-thumb-preview";
        /// <summary>The shown size (logical px): the 504 × 704 render at a quarter.</summary>
        public const int Width = 126, Height = 176;
        /// <summary>Frames a shown preview waits for its character to be built before it renders anyway (the note then says why
        /// there is no image).</summary>
        public const int MaxWaitFrames = 300;

        /// <summary>The preview attached to a panel now (null when none).</summary>
        public static ThumbnailPreview Current { get; private set; }

        readonly UiContext ctx;
        readonly string charaId;
        readonly Func<CharaThumbnailFraming> framingOf;
        Texture2D texture;
        bool pending;
        int pendingSince;

        public Image ImageElement { get; }
        public Label NoteLabel { get; }
        public Button Retake { get; }
        /// <summary>The texture shown (504 × 704; null before the first render or when none was rendered).</summary>
        public Texture2D Texture => texture;
        /// <summary>The framing of the shown texture (View falls back to Front without a display camera); null without one.</summary>
        public CharaThumbnailFraming? Framing { get; private set; }
        /// <summary>Time.frameCount of the last render that gave a texture (-1 before).</summary>
        public int RenderedAtFrame { get; private set; } = -1;
        /// <summary>Renders so far (with and without a texture) and the last one's time (ms, the op's capture).</summary>
        public int Renders { get; private set; }
        public double LastMs { get; private set; }
        /// <summary>Why the last render gave no texture or fell back (the op's warnings), else null.</summary>
        public string Reason { get; private set; }
        /// <summary>True while a render is waiting for the next frame or the character's build.</summary>
        public bool Pending => pending;
        public OpResult LastResult { get; private set; }

        public ThumbnailPreview(UiContext ctx, string charaId, Func<CharaThumbnailFraming> framingOf, string retakeLabelKey)
        {
            this.ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            this.charaId = charaId;
            this.framingOf = framingOf ?? (() => CharaThumbnailFraming.View);
            name = "file-thumb-preview";
            AddToClassList(UssClass);
            ImageElement = new Image { name = "file-thumb-image", scaleMode = ScaleMode.ScaleToFit };
            ImageElement.AddToClassList(UssClass + "__image");
            Loc.Tooltip(ImageElement, "inspector.charaEdit.file.preview");
            Add(ImageElement);
            var side = new VisualElement { name = "file-thumb-side" };
            side.AddToClassList(UssClass + "__side");
            NoteLabel = new Label { name = "file-thumb-note" };
            NoteLabel.AddToClassList(InspectorUi.Dim);
            NoteLabel.AddToClassList(UssClass + "__note");
            side.Add(NoteLabel);
            Retake = InspectorUi.Button(retakeLabelKey ?? "inspector.charaEdit.file.retakeThumbnail", () => Render(), "file-retake");
            side.Add(Retake);
            Add(side);
            RegisterCallback<AttachToPanelEvent>(_ => { Current = this; RequestRender(); });
            RegisterCallback<DetachFromPanelEvent>(_ => { if (Current == this) Current = null; pending = false; Replace(null); Framing = null; });
            PanelKit.EveryFrame(this, Tick, fromConstruction: false);
        }

        /// <summary>Renders on a later frame (not in the frame that built or attached the page), once the character is built.</summary>
        public void RequestRender()
        {
            pending = true;
            pendingSince = Time.frameCount;
            NoteLabel.text = L10n.Get("inspector.charaEdit.file.previewPending");
        }

        void Tick()
        {
            if (!pending || panel == null || Time.frameCount <= pendingSince) return;
            var r = ctx.Runtime?.FindChara(charaId);
            bool built = r != null && r.State == RealizationState.Built;
            if (!built && Time.frameCount - pendingSince < MaxWaitFrames) return;
            Render();
        }

        /// <summary>Renders the preview now in the page's framing (the «撮影し直す» button; tests). Not recorded.</summary>
        public OpResult Render()
        {
            pending = false;
            var res = ctx.Ops.RenderCharaThumbnail(charaId, framingOf());
            var v = res.Value as CharaThumbnailRender;
            Renders++;
            LastMs = v?.Milliseconds ?? 0;
            string warn = res.Warnings.Count > 0 ? string.Join("; ", res.Warnings) : null;
            if (v?.Texture != null)
            {
                Replace(v.Texture);
                Framing = v.Framing;
                RenderedAtFrame = Time.frameCount;
                Reason = warn;
                NoteLabel.text = warn ?? "";
            }
            else
            {
                Replace(null);
                Framing = null;
                Reason = warn ?? res.Message ?? res.Code ?? "?";
                NoteLabel.text = L10n.Format("inspector.charaEdit.file.previewNone", Reason);
            }
            return LastResult = res;
        }

        void Replace(Texture2D t)
        {
            var old = texture;
            texture = t;
            ImageElement.image = t;
            if (old != null && !ReferenceEquals(old, t)) Object.Destroy(old);
        }

        /// <summary>The facts of `ui.state makerFile.preview`: the texture's size, the framing, the frame of the render.</summary>
        public Dictionary<string, object> State() => new Dictionary<string, object>
        {
            ["chara"] = charaId,
            ["width"] = texture != null ? texture.width : 0,
            ["height"] = texture != null ? texture.height : 0,
            ["framing"] = Framing == null ? null : Framing == CharaThumbnailFraming.Front ? "front" : "view",
            ["renderedAtFrame"] = RenderedAtFrame,
            ["renders"] = Renders,
            ["ms"] = Math.Round(LastMs, 2),
            ["pending"] = pending,
            ["reason"] = Reason,
            ["shownWidth"] = Math.Round(ImageElement.resolvedStyle.width, 1),
            ["shownHeight"] = Math.Round(ImageElement.resolvedStyle.height, 1),
        };
    }
}

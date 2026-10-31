// The pattern of a clothes colour set: a button `clothes-pattern-<kind>-<n>` showing the current 348 row's thumbnail and name
// (HS2's pattern button) that opens and closes the pattern list below it. The list — a PartList of category 348 on
// `appearance.coord.clothes.<kind>.colors[<n-1>].pattern` (thumbnail cells, current mark, the "not listed" cell; a pick is one
// entry "Change item") — is built on the button's first open, not with the section, so a kind group carries one list (the
// item list) until a pattern list is opened; once built it is kept (opened and closed by display). Several characters with
// different patterns: the button shows "—". A pattern reference of the KCOX plugin is shown as not listed and never
// offered. The thumbnail is requested from the shared part thumbnails while the row is attached and drawn only once loaded
// (no placeholder tile).
using System;
using System.Collections.Generic;
using HS2Next.Content;
using HS2Next.Content.Maker;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Panels.Maker;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class ClothesPatternRow : VisualElement
    {
        public const string UssClass = "hs-clothes-pattern", OpenClass = "hs-clothes-pattern--open";
        public const int PatternCategory = 348;
        /// <summary>The source GUID of KCOX's pattern references (never a list row).</summary>
        public const string KcoxGuid = "hs2.kcox";

        readonly UiContext ctx;
        readonly TargetSelector sel;
        readonly VisualElement thumb, holder;
        readonly Label nameLabel, arrow;
        readonly PartThumbnails thumbs;
        Texture2D shownTexture;
        ContentId requested;
        bool hasRequest;

        public FieldSpec Spec { get; }
        /// <summary>The member path (`appearance.coord.clothes.top.colors[0].pattern`).</summary>
        public string Path { get; }
        public string Kind { get; }
        /// <summary>The colour set (1..3).</summary>
        public int Set { get; }
        public Button Button { get; }
        public FieldBinding Binding { get; }
        /// <summary>The pattern list (null until the first open).</summary>
        public PartList List { get; private set; }
        public bool Expanded { get; private set; }
        /// <summary>The bound pattern of the primary (null when none) and whether the selected characters differ.</summary>
        public string CurrentRef { get; private set; }
        public bool Mixed { get; private set; }
        /// <summary>The text the button shows (the pattern's name).</summary>
        public string CurrentName => nameLabel.text;
        /// <summary>The thumbnail texture drawn on the button (null while loading, failed or without a pattern; tests count, never view).</summary>
        public Texture2D ShownTexture => shownTexture;
        /// <summary>Time of the list's construction on the first open (ms).</summary>
        public double ListBuildMs { get; private set; } = -1;

        public ClothesPatternRow(UiContext ctx, FieldSpec spec, TargetSelector selector, string path, string kind, int set)
        {
            this.ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            sel = selector;
            Path = path ?? spec.Path;
            Kind = kind;
            Set = set;
            name = "clothes-pattern-row-" + kind + "-" + set;
            AddToClassList(FieldRow.UssClass);
            AddToClassList(UssClass);
            thumbs = PartThumbnails.Shared(ctx.App?.Catalog);
            var line = new VisualElement();
            line.AddToClassList("hs-clothes-pattern__line");
            var label = new Label(spec.LabelKey != null ? L10n.Get(spec.LabelKey) : "");
            label.AddToClassList("hs-field__label");
            if (spec.LabelKey != null) Loc.Bind(label, spec.LabelKey);
            line.Add(label);
            Button = new Button(() => SetExpanded(!Expanded)) { name = "clothes-pattern-" + kind + "-" + set };
            Button.AddToClassList("hs-clothes-pattern__button");
            Loc.Tooltip(Button, "maker.clothes.patternChoose");
            thumb = new VisualElement { name = "clothes-pattern-thumb" };
            thumb.AddToClassList("hs-clothes-pattern__thumb");
            thumb.pickingMode = PickingMode.Ignore;
            nameLabel = new Label { name = "clothes-pattern-name" };
            nameLabel.AddToClassList("hs-clothes-pattern__name");
            nameLabel.pickingMode = PickingMode.Ignore;
            arrow = new Label("▾");
            arrow.AddToClassList("hs-clothes-pattern__arrow");
            arrow.pickingMode = PickingMode.Ignore;
            Button.Add(thumb);
            Button.Add(nameLabel);
            Button.Add(arrow);
            line.Add(Button);
            Add(line);
            holder = new VisualElement { name = "clothes-pattern-list-" + kind + "-" + set };
            holder.style.display = DisplayStyle.None;
            Add(holder);
            RegisterCallback<AttachToPanelEvent>(_ => { if (thumbs != null) { thumbs.LoadedColumn -= OnThumbLoaded; thumbs.LoadedColumn += OnThumbLoaded; } RequestThumb(); });
            RegisterCallback<DetachFromPanelEvent>(_ => { ReleaseThumb(); if (thumbs != null) thumbs.LoadedColumn -= OnThumbLoaded; });
            Binding = ctx.Bindings.Bind(this, new FieldBinding { Selector = sel, Path = Path, Apply = (el, v) => Show(v) });
        }

        /// <summary>Opens (building the list on the first open) or closes the pattern list.</summary>
        public void SetExpanded(bool on)
        {
            if (on && List == null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                List = new PartList(ctx, Spec, sel, Path);
                holder.Add(List);
                ListBuildMs = sw.Elapsed.TotalMilliseconds;
            }
            Expanded = on;
            holder.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            arrow.text = on ? "▴" : "▾";
            EnableInClassList(OpenClass, on);
        }

        void Show(BoundValues v)
        {
            var targets = v.Targets ?? Array.Empty<string>();
            style.display = targets.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            CurrentRef = v.Value as string;
            Mixed = v.Mixed;
            nameLabel.text = Mixed ? "—" : NameOf(CurrentRef, targets.Count > 0 ? targets[0] : null);
            RequestThumb();
        }

        /// <summary>The display name of a pattern reference: the 348 row's name in the UI language, "not listed" otherwise.</summary>
        string NameOf(string reference, string primaryId)
        {
            if (string.IsNullOrEmpty(reference)) return "";
            if (!ContentId.TryParse(reference, out var key)) return reference;
            if (string.Equals(key.SourceGuid, KcoxGuid, StringComparison.OrdinalIgnoreCase)) return L10n.Format("maker.clothes.kcoxPattern", reference);
            var row = RowOf(key, primaryId);
            return row != null ? MakerLists.NameOf(row, L10n.Language) : L10n.Format("maker.list.notListed", reference);
        }

        ListRow RowOf(ContentId key, string primaryId)
        {
            var lists = MakerCatalog.For(ctx.App?.Catalog);
            if (lists == null) return null;
            int sex = StudioOps.SexOf(primaryId != null ? ctx.Model.Find(primaryId) : null);
            foreach (var r in lists.Rows(PatternCategory, sex < 0 ? 1 : sex))
                if (r.Key.Equals(key)) return r;
            return null;
        }

        void RequestThumb()
        {
            if (thumbs == null || panel == null) return;
            bool want = !Mixed && !string.IsNullOrEmpty(CurrentRef) && ContentId.TryParse(CurrentRef, out var key) && RowOf(key, sel?.Resolve(ctx) is IReadOnlyList<string> t && t.Count > 0 ? t[0] : null) != null;
            if (!want) { ReleaseThumb(); SetThumb(null); return; }
            ContentId.TryParse(CurrentRef, out var k2);
            if (hasRequest && requested.Equals(k2)) { SetThumb(thumbs.TryGet(PatternCategory, k2)); return; }
            ReleaseThumb();
            requested = k2;
            hasRequest = true;
            SetThumb(thumbs.Request(PatternCategory, k2, this));
        }

        void ReleaseThumb()
        {
            if (!hasRequest) return;
            thumbs?.Release(PatternCategory, requested, this);
            hasRequest = false;
        }

        void OnThumbLoaded(int category, ContentId key, string column, Texture2D t)
        {
            if (category != PatternCategory || !hasRequest || !key.Equals(requested) || column != PartThumbnails.ThumbColumn) return;
            SetThumb(t);
        }

        void SetThumb(Texture2D t)
        {
            if (t == shownTexture) return;
            shownTexture = t;
            thumb.style.backgroundImage = t != null ? new StyleBackground(t) : new StyleBackground(StyleKeyword.None);
        }
    }
}

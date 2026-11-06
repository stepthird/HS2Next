// The rows of the Materials view and the drag they share with the hair bone rows. A drag is one UI edit: the array as it stood at the press
// stays in the row, and each step starts from that array, so the result depends only on the last value: a drag through the value the
// target holds without the item keeps an imported item's place, and a drag that ends where it began leaves the array as it was and adds
// no entry. Steps go on the bus with a merge key: in the open edit they then collapse to the first old and the last new array, and a
// step leaves the flush to the frame. The router cancels the edit on Esc: the row adds no Esc of its own, it only drops the moves that
// come after. A typed value is one entry; those of one item chain.
using System;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    /// <summary>One drag on an array, from the press to the release.</summary>
    public sealed class ArrayDrag<T>
    {
        /// <summary>A step's merge key: what puts it on the open edit's bus directly.</summary>
        public const string StepKey = "ui.drag";

        readonly UiContext ctx;
        readonly Func<List<T>> read;
        readonly Action ended;
        UiEdit edit;
        bool dropped;

        public ArrayDrag(UiContext ctx, Func<List<T>> read, Action ended)
        {
            this.ctx = ctx;
            this.read = read;
            this.ended = ended;
        }

        /// <summary>The array at the press; a step starts from it.</summary>
        public List<T> AtPress { get; private set; }
        /// <summary>The label of the open edit.</summary>
        public string Label { get; private set; }
        /// <summary>An edit is open and takes steps.</summary>
        public bool Open => edit != null && edit.IsOpen && !dropped;
        /// <summary>The last result of the drag (tests).</summary>
        public OpResult LastResult { get; private set; }

        /// <summary>The press: keeps the array and opens the edit.</summary>
        public void Begin(string label)
        {
            edit?.Cancel();
            dropped = false;
            Label = label;
            AtPress = read();
            edit = ctx.BeginEdit(label);
            var mine = edit;
            mine.Ended += committed =>
            {
                if (edit == mine) edit = null;
                if (committed) return;
                dropped = true;
                ended();
            };
        }

        /// <summary>What a step's operation answered: a refusal ends the drag, once.</summary>
        public void Stepped(OpResult r)
        {
            LastResult = r;
            if (r.Ok) return;
            ctx.ShowResult(r);
            edit?.Cancel();
        }

        /// <summary>The release: one entry, none when the array is back as it was. A card-backed character's import goes with such a drag.</summary>
        public OpResult End()
        {
            var e = edit;
            var press = AtPress;
            edit = null;
            AtPress = null;
            OpResult r = OpResult.NothingToDo();
            if (e != null && e.IsOpen)
            {
                if (FieldSchema.Same(press, read())) e.Cancel();
                else r = ctx.ShowResult(e.Commit());
            }
            LastResult = r;
            ended();
            return r;
        }

        /// <summary>Takes the drag back.</summary>
        public void Cancel()
        {
            var e = edit;
            edit = null;
            AtPress = null;
            e?.Cancel();
        }
    }

    /// <summary>One row of the selected material.</summary>
    public abstract class MaterialRow : VisualElement
    {
        protected readonly MaterialEditView View;
        protected readonly string Material;
        protected readonly ArrayDrag<MaterialOverride> Drag;
        protected readonly Label Mark;
        protected readonly Button ResetButton;
        OpResult lastStored;

        /// <summary>The stored property, or null for a row of the material.</summary>
        public string Property { get; protected set; }
        /// <summary>The item this row writes.</summary>
        public abstract MeKey Key { get; }
        /// <summary>The string key of why the row is off, or null.</summary>
        public string OffKey { get; private set; }
        /// <summary>The mark shows an item.</summary>
        public bool Marked => Mark.style.display != DisplayStyle.None;
        /// <summary>The last write's result (tests).</summary>
        public OpResult LastResult => lastStored ?? Drag.LastResult;

        protected MaterialRow(MaterialEditView view, string material)
        {
            View = view;
            Material = material;
            Drag = new ArrayDrag<MaterialOverride>(view.Ctx, () => view.Array, view.EditEnded);
            AddToClassList("hs-field");
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            Mark = new Label("●") { name = "mat-mark" };
            Mark.AddToClassList("hs-row__badge");
            Mark.style.display = DisplayStyle.None;
            ResetButton = new Button(() => ResetItem()) { name = "mat-reset", text = "↺" };
            ResetButton.AddToClassList("hs-field__default");
            Loc.Tooltip(ResetButton, "inspector.chara.adv.reset");
            ResetButton.style.display = DisplayStyle.None;
        }

        /// <summary>Adds what every row ends with: the mark and ↺.</summary>
        protected void AddTail(VisualElement into = null)
        {
            (into ?? this).Add(Mark);
            (into ?? this).Add(ResetButton);
        }

        /// <summary>Turns the row off: a rule keeps it from writing.</summary>
        internal void Off(string key)
        {
            OffKey = key;
            SetEnabled(false);
            Loc.Tooltip(this, key);
        }

        /// <summary>Shows the live value and the item.</summary>
        internal abstract void Show(MaterialSelection s, MaterialOverride item);

        /// <summary>The item's mark, ↺ and its stored original.</summary>
        protected void ShowItem(string original, bool hasItem)
        {
            Mark.style.display = hasItem ? DisplayStyle.Flex : DisplayStyle.None;
            ResetButton.style.display = hasItem ? DisplayStyle.Flex : DisplayStyle.None;
            if (OffKey == null) tooltip = hasItem && original != null ? L10n.Format("inspector.materials.original", original) : "";
        }

        protected static string Text(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        protected static string Text(Color4 c) => Text(c.R) + ", " + Text(c.G) + ", " + Text(c.B) + ", " + Text(c.A);
        protected static string Text(Vec2 v) => Text(v.X) + ", " + Text(v.Y);

        internal void CancelEdit() => Drag.Cancel();

        /// <summary>One step of the open drag, from the array at the press.</summary>
        protected void Step(MaterialEditRequest q)
        {
            if (!Drag.Open) return;
            lastStored = null;
            Drag.Stepped(View.Ops.SetMaterial(View.Target.Address, q, UiContext.User(Drag.Label, ArrayDrag<MaterialOverride>.StepKey), Drag.AtPress));
        }

        /// <summary>One entry; typed values of one item chain.</summary>
        protected OpResult Store(MaterialEditRequest q, string label = OpLabels.MaterialEdit, bool typed = false)
        {
            string key = typed ? "ui.typed/" + View.Target.Owner + "/" + View.Target.Address.Text + "/" + q.Key : null;
            lastStored = View.Ctx.ShowResult(View.Ops.SetMaterial(View.Target.Address, q, UiContext.User(label, key)));
            View.EditEnded();
            return lastStored;
        }

        /// <summary>↺: the item leaves, and the value the target holds without it shows.</summary>
        public virtual OpResult ResetItem()
        {
            CancelEdit();
            lastStored = View.Ctx.ShowResult(View.Ops.ClearMaterial(View.Target.Address, Key, null, UiContext.User(OpLabels.MaterialReset)));
            View.EditEnded();
            return lastStored;
        }

        protected static void Fit(VisualElement control)
        {
            control.style.flexGrow = 1;
            control.style.flexShrink = 1;
        }
    }

    /// <summary>A Float, Range or Int property.</summary>
    public sealed class MaterialFloatRow : MaterialRow
    {
        public readonly NumberSlider Number;
        public override MeKey Key => MeKey.Property("float", Material, Property);

        internal MaterialFloatRow(MaterialEditView view, string material, string property, float min, float max, bool whole) : base(view, material)
        {
            Property = property;
            name = "mat-prop-" + property;
            Number = new NumberSlider(property, min, max) { Whole = whole, Format = whole ? "0" : "0.###" };
            Number.Edited += OnEdited;
            Fit(Number);
            Add(Number);
            AddTail();
        }

        MaterialEditRequest Value(double v) => MaterialEditRequest.Float(Material, Property, (float)v);

        void OnEdited(EditPhase phase, double v)
        {
            switch (phase)
            {
                case EditPhase.Begin: Drag.Begin(OpLabels.MaterialEdit); break;
                case EditPhase.End: Drag.End(); break;
                case EditPhase.Preview: Step(Value(v)); break;
                case EditPhase.Typed: Store(Value(v), typed: true); break;
            }
        }

        internal override void Show(MaterialSelection s, MaterialOverride item)
        {
            if (Number.Dragging || Drag.Open) return;
            if (MaterialBaseline.TryRead(s.Shown, MaterialMember.Float, "_" + Property, out var v)) Number.SetDisplay(v.Float, false);
            var f = item as MeFloat;
            ShowItem(f?.Original != null ? Text(f.Original.Value) : f?.OriginalText, f != null);
        }
    }

    /// <summary>A Color property: the swatch opens the picker; the picker's whole life is one drag.</summary>
    public sealed class MaterialColorRow : MaterialRow
    {
        readonly bool hdr;
        public readonly ColorSwatch Swatch;
        public ColorPicker Picker { get; private set; }
        public override MeKey Key => MeKey.Property("color", Material, Property);

        internal MaterialColorRow(MaterialEditView view, string material, string property, bool hdr) : base(view, material)
        {
            this.hdr = hdr;
            Property = property;
            name = "mat-prop-" + property;
            Swatch = new ColorSwatch(property);
            Swatch.Clicked += () => OpenPicker();
            Fit(Swatch);
            Add(Swatch);
            AddTail();
        }

        MaterialEditRequest Value(Color c) => MaterialEditRequest.ColorOf(Material, Property, new Color4(c.r, c.g, c.b, c.a));

        /// <summary>Opens the picker. An HDR colour shows as a colour and an intensity, its largest channel; their product is stored.</summary>
        public bool OpenPicker()
        {
            var layer = View.Ctx.Shell?.Overlay ?? panel?.visualTree;
            if (layer == null) return false;
            Picker?.Close(true);
            var c = Swatch.Value;
            float k = hdr ? Mathf.Max(1f, c.maxColorComponent) : 0f;
            var start = hdr ? new Color(c.r / k, c.g / k, c.b / k, c.a) : c;
            var picker = new ColorPicker(start, true, hdr, k);
            Picker = picker;
            Drag.Begin(OpLabels.MaterialEdit);
            picker.Changed += (col, intensity) => Step(Value(hdr ? new Color(col.r * intensity, col.g * intensity, col.b * intensity, col.a) : col));
            picker.Closed += commit =>
            {
                Picker = null;
                if (commit) Drag.End();
                else
                {
                    Drag.Cancel();
                    View.EditEnded();
                }
            };
            var blocker = new VisualElement { name = "color-blocker" };
            blocker.style.position = Position.Absolute;
            blocker.style.left = 0; blocker.style.top = 0; blocker.style.right = 0; blocker.style.bottom = 0;
            blocker.RegisterCallback<PointerDownEvent>(e => { picker.Close(true); e.StopPropagation(); });
            picker.Closed += _ => blocker.RemoveFromHierarchy();
            picker.style.position = Position.Absolute;
            var wb = Swatch.Button.worldBound;
            picker.style.left = Mathf.Max(0, wb.xMin - 260);
            picker.style.top = wb.yMax;
            picker.RegisterCallback<GeometryChangedEvent>(_ => { float maxY = layer.layout.height - picker.layout.height - 2; if (maxY > 0 && picker.layout.y > maxY + 0.5f) picker.style.top = maxY; });
            layer.Add(blocker);
            layer.Add(picker);
            UiDefer.Frames(2, () => { if (picker.panel != null) picker.Focus(); });
            return true;
        }

        internal override void Show(MaterialSelection s, MaterialOverride item)
        {
            if (Picker != null) return;
            if (MaterialBaseline.TryRead(s.Shown, MaterialMember.Color, "_" + Property, out var v)) Swatch.SetDisplay(v.Color, false);
            var c = item as MeColor;
            ShowItem(c?.Original != null ? Text(c.Original.Value) : null, c != null);
        }
    }

    /// <summary>A 2D texture: the image and, unless the shader says no, its tiling and offset.</summary>
    public sealed class MaterialTextureRow : MaterialRow
    {
        public readonly FilePicker File;
        public readonly VectorField Tiling, Offset;
        public readonly Button RemoveImage;
        public override MeKey Key => MeKey.Property("texture", Material, Property);

        internal MaterialTextureRow(MaterialEditView view, string material, string property, bool tiling) : base(view, material)
        {
            Property = property;
            name = "mat-prop-" + property;
            style.flexDirection = FlexDirection.Column;
            style.alignItems = Align.Stretch;
            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;
            File = new FilePicker(property) { Kind = "image" };
            File.ClearButton.style.display = DisplayStyle.None;
            File.Picked += Pick;
            Fit(File);
            head.Add(File);
            AddTail(head);
            Add(head);
            if (tiling)
            {
                Tiling = new VectorField(L10n.Get("inspector.materials.tiling"), 2) { name = "mat-tiling" };
                Loc.Bind(Tiling.LabelElement, "inspector.materials.tiling");
                Tiling.Edited += (p, v) => OnVector(p, v, true);
                Add(Tiling);
                Offset = new VectorField(L10n.Get("inspector.materials.offset"), 2) { name = "mat-offset" };
                Loc.Bind(Offset.LabelElement, "inspector.materials.offset");
                Offset.Edited += (p, v) => OnVector(p, v, false);
                Add(Offset);
            }
            RemoveImage = InspectorUi.Button("inspector.materials.removeImage", () => TakeImage(), "mat-remove-image");
            Add(RemoveImage);
        }

        /// <summary>What the picker hands over: the toast, then the file is read a moment later, so the toast is drawn before the decode holds the frame.</summary>
        public void Pick(string path)
        {
            if (path == null) return;
            View.Ctx.Toasts?.Show(L10n.Get("inspector.materials.importing"), ToastKind.Info, 2f);
            UiDefer.Frames(2, () => { if (panel != null) Import(path); });
        }

        /// <summary>Reads a picked file into the scene and names it (one entry «Material texture»). No path is kept.</summary>
        public OpResult Import(string path) => Store(MaterialEditRequest.Texture(Material, Property).WithFile(path), OpLabels.MaterialTexture);

        /// <summary>Clears the image of the item; offset and scale stay.</summary>
        public OpResult TakeImage() => Store(MaterialEditRequest.Texture(Material, Property).WithImage(null), OpLabels.MaterialTexture);

        void OnVector(EditPhase phase, Vector4 v, bool scale)
        {
            if (phase == EditPhase.Begin) { Drag.Begin(OpLabels.MaterialEdit); return; }
            if (phase == EditPhase.End) { Drag.End(); return; }
            var q = MaterialEditRequest.Texture(Material, Property);
            var a = new Vec2(v.x, v.y);
            if (scale) q.WithScale(a); else q.WithOffset(a);
            if (phase == EditPhase.Preview) Step(q);
            else Store(q, typed: true);
        }

        internal override void Show(MaterialSelection s, MaterialOverride item)
        {
            if (Drag.Open) return;
            var t = item as MeTexture;
            string prop = "_" + Property;
            string image = t?.Image;
            if (image == null && MaterialBaseline.TryRead(s.Shown, MaterialMember.Texture, prop, out var tv) && tv.Texture) image = tv.Texture.name;
            File.SetDisplay(image, false);
            RemoveImage.style.display = t?.Image != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (Tiling != null && !Tiling.Dragging && MaterialBaseline.TryRead(s.Shown, MaterialMember.Scale, prop, out var sc)) Tiling.SetDisplay(new Vector4(sc.Vector2.x, sc.Vector2.y, 0, 0), null);
            if (Offset != null && !Offset.Dragging && MaterialBaseline.TryRead(s.Shown, MaterialMember.Offset, prop, out var of)) Offset.SetDisplay(new Vector4(of.Vector2.x, of.Vector2.y, 0, 0), null);
            string original = null;
            if (t?.OffsetOriginal != null) original = Text(t.OffsetOriginal.Value);
            if (t?.ScaleOriginal != null) original = (original != null ? original + " / " : "") + Text(t.ScaleOriginal.Value);
            ShowItem(original, t != null);
        }
    }

    /// <summary>A value without an editor: a Vector property, a texture of another dimension.</summary>
    public sealed class MaterialReadOnlyRow : MaterialRow
    {
        readonly ShaderPropertyType type;
        readonly Label value;
        readonly Label note;
        public override MeKey Key => MeKey.Property(type == ShaderPropertyType.Vector ? "color" : "cubemap", Material, Property);

        internal MaterialReadOnlyRow(MaterialEditView view, string material, string property, ShaderPropertyType type) : base(view, material)
        {
            this.type = type;
            Property = property;
            name = "mat-prop-" + property;
            var label = new Label(property);
            label.AddToClassList("hs-field__label");
            Add(label);
            value = new Label { name = "mat-value" };
            value.AddToClassList(InspectorUi.Dim);
            value.style.flexGrow = 1;
            value.style.flexShrink = 1;
            value.style.overflow = Overflow.Hidden;
            value.style.textOverflow = TextOverflow.Ellipsis;
            Add(value);
            note = InspectorUi.Note("inspector.materials.readOnly");
            note.name = "mat-readonly";
            note.style.display = DisplayStyle.None;
            Add(note);
            Add(Mark);
        }

        /// <summary>The note a stored item on this row carries.</summary>
        public bool NoteShown => note.style.display != DisplayStyle.None;

        internal override void Show(MaterialSelection s, MaterialOverride item)
        {
            if (type == ShaderPropertyType.Vector)
            {
                if (MaterialBaseline.TryRead(s.Shown, MaterialMember.Vector, "_" + Property, out var v))
                    value.text = Text(v.Vector.x) + ", " + Text(v.Vector.y) + ", " + Text(v.Vector.z) + ", " + Text(v.Vector.w);
            }
            else value.text = s.Shown.GetTexture("_" + Property) is Texture t && t ? t.name : L10n.Get("common.none");
            bool stored = item != null;
            Mark.style.display = stored ? DisplayStyle.Flex : DisplayStyle.None;
            note.style.display = stored ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public override OpResult ResetItem() => OpResult.NothingToDo();
    }

    /// <summary>The material's shader: a searchable list of the shaders it can take, by family.</summary>
    public sealed class MaterialShaderRow : MaterialRow
    {
        public readonly RefPicker Picker;
        public override MeKey Key => MeKey.Shader(Material);

        internal MaterialShaderRow(MaterialEditView view, string material) : base(view, material)
        {
            name = "mat-shader";
            Picker = new RefPicker(L10n.Get("inspector.materials.shader")) { Choices = Choices };
            Loc.Bind(Picker.LabelElement, "inspector.materials.shader");
            Picker.Picked += v => Pick(v);
            Fit(Picker);
            Add(Picker);
            AddTail();
        }

        static IReadOnlyList<(string value, string label)> Choices()
        {
            var l = new List<(string value, string label)>();
            foreach (var c in MaterialShaders.All) l.Add((c.Name, c.Family + " / " + c.Label));
            l.Sort((a, b) => string.CompareOrdinal(a.label, b.label));
            return l;
        }

        /// <summary>Takes a shader of the list (one entry «Material shader»).</summary>
        public OpResult Pick(string shader) => Store(MaterialEditRequest.ShaderItem(Material).WithShader(shader), OpLabels.MaterialShader);

        internal override void Show(MaterialSelection s, MaterialOverride item)
        {
            var e = item as MeShader;
            string live = s.Shown.shader ? s.Shown.shader.name : null;
            bool named = e?.Shader != null;
            // an item whose shader is not applied shows the name it stores
            string shown = named && e.Shader != live ? e.Shader : live;
            string label = shown != null && shown.StartsWith(MaterialShaders.Prefix, StringComparison.Ordinal) ? shown.Substring(MaterialShaders.Prefix.Length) : shown;
            Picker.SetDisplay(shown, label, false);
            ShowItem(e?.ShaderOriginal, named);
        }

        /// <summary>↺: the item's shader goes; a queue it holds stays.</summary>
        public override OpResult ResetItem() => Store(MaterialEditRequest.ShaderItem(Material).WithShader(null), OpLabels.MaterialReset);
    }

    /// <summary>The material's render queue.</summary>
    public sealed class MaterialQueueRow : MaterialRow
    {
        public readonly NumberSlider Number;
        public override MeKey Key => MeKey.Shader(Material);

        internal MaterialQueueRow(MaterialEditView view, string material) : base(view, material)
        {
            name = "mat-queue";
            Number = new NumberSlider(L10n.Get("inspector.materials.queue"), MaterialEdits.MinQueue, MaterialEdits.MaxQueue)
            {
                TypedMin = MaterialEdits.MinQueue, TypedMax = MaterialEdits.MaxQueue, Whole = true, Format = "0",
            };
            Loc.Bind(Number.LabelElement, "inspector.materials.queue");
            Number.Edited += OnEdited;
            Fit(Number);
            Add(Number);
            AddTail();
        }

        MaterialEditRequest Value(double v) => MaterialEditRequest.ShaderItem(Material).WithQueue((int)Math.Round(v));

        void OnEdited(EditPhase phase, double v)
        {
            switch (phase)
            {
                case EditPhase.Begin: Drag.Begin(OpLabels.MaterialEdit); break;
                case EditPhase.End: Drag.End(); break;
                case EditPhase.Preview: Step(Value(v)); break;
                case EditPhase.Typed: Store(Value(v), typed: true); break;
            }
        }

        internal override void Show(MaterialSelection s, MaterialOverride item)
        {
            if (Number.Dragging || Drag.Open) return;
            Number.SetDisplay(s.Shown.renderQueue, false);
            var e = item as MeShader;
            ShowItem(e?.RenderQueueOriginal?.ToString(CultureInfo.InvariantCulture), e?.RenderQueue != null);
        }

        /// <summary>↺: the item's queue goes; a shader it holds stays.</summary>
        public override OpResult ResetItem() => Store(MaterialEditRequest.ShaderItem(Material).WithQueue(null), OpLabels.MaterialReset);
    }

    /// <summary>A local keyword of an HS2Next shader.</summary>
    public sealed class MaterialKeywordRow : MaterialRow
    {
        public readonly Toggle Check;
        public override MeKey Key => MeKey.Property("keyword", Material, Property);

        internal MaterialKeywordRow(MaterialEditView view, string material, string keyword) : base(view, material)
        {
            Property = keyword;
            name = "mat-kw-" + keyword;
            Check = new Toggle(keyword) { name = "toggle" };
            Check.AddToClassList("hs-field__toggle");
            Check.RegisterValueChangedCallback(e => { if (e.target == Check) Set(e.newValue); });
            Fit(Check);
            Add(Check);
            AddTail();
        }

        /// <summary>Stores the keyword (one entry).</summary>
        public OpResult Set(bool on) => Store(MaterialEditRequest.Keyword(Material, Property, on));

        internal override void Show(MaterialSelection s, MaterialOverride item)
        {
            Check.SetValueWithoutNotify(s.Shown.IsKeywordEnabled("_" + Property));
            var k = item as MeKeyword;
            ShowItem(k != null ? L10n.Get(k.Original ? "common.on" : "common.off") : null, k != null);
        }
    }
}

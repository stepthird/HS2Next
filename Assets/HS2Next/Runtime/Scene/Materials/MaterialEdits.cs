// Editing a materialOverrides array, of a character, a hair part, a clothes part, an accessory, an item or the map. An edit touches
// one element: it is replaced in place or inserted at the end of its type's run, and every other element stays the same object in
// its place, so an item this app cannot read, a number kept as text and an item that changes nothing are written back as they
// were. Unity-free: each function takes a list and returns a new one, or the same list when nothing changes; what only the runtime
// knows (the value the target has without this item) comes in the edit. A value equal to it removes the item, so an array holds only
// what differs. The original members of a stored item are data of its file and are never rewritten; a new item takes them from
// the edit.
using System;
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    /// <summary>The key of one item, the members to write, and the target's values without the item. Made by the factories;
    /// <see cref="MaterialEdits.Set"/> reads it.</summary>
    public sealed class MaterialEdit
    {
        public MeKey Key { get; }

        // the value of a `float`, `color`, `keyword` or `renderer` edit, and what the target holds without the item
        public float Number { get; private set; }
        public float? NumberBaseline { get; private set; }
        public Color4 Color { get; private set; }
        public Color4? ColorBaseline { get; private set; }
        public bool Switch { get; private set; }
        public bool? SwitchBaseline { get; private set; }
        public int Enabled { get; private set; }
        public int? EnabledBaseline { get; private set; }

        // the members of a `texture` or `shader` edit: a member not named stays as it is, one named as null is cleared
        public bool HasImage { get; private set; }
        public string Image { get; private set; }
        public bool HasOffset { get; private set; }
        public Vec2? Offset { get; private set; }
        public Vec2? OffsetBaseline { get; private set; }
        public bool HasScale { get; private set; }
        public Vec2? Scale { get; private set; }
        public Vec2? ScaleBaseline { get; private set; }
        public bool HasShader { get; private set; }
        public string Shader { get; private set; }
        public string ShaderBaseline { get; private set; }
        public bool HasQueue { get; private set; }
        public int? Queue { get; private set; }
        public int? QueueBaseline { get; private set; }

        MaterialEdit(MeKey key) { Key = key; }

        public static MaterialEdit Float(string material, string property, float value, float? baseline = null) =>
            new MaterialEdit(MeKey.Property("float", material, property)) { Number = value, NumberBaseline = baseline };

        public static MaterialEdit ColorOf(string material, string property, Color4 value, Color4? baseline = null) =>
            new MaterialEdit(MeKey.Property("color", material, property)) { Color = value, ColorBaseline = baseline };

        public static MaterialEdit Keyword(string material, string property, bool value, bool? baseline = null) =>
            new MaterialEdit(MeKey.Property("keyword", material, property)) { Switch = value, SwitchBaseline = baseline };

        /// <summary>A renderer's `Enabled`: 0 or 1.</summary>
        public static MaterialEdit RendererEnabled(string renderer, int value, int? baseline = null) =>
            new MaterialEdit(MeKey.Enabled(renderer)) { Enabled = value, EnabledBaseline = baseline };

        /// <summary>A `texture` edit with no member named yet: follow it with <see cref="WithImage"/>, <see cref="WithOffset"/>,
        /// <see cref="WithScale"/>.</summary>
        public static MaterialEdit Texture(string material, string property) => new MaterialEdit(MeKey.Property("texture", material, property));

        /// <summary>A `shader` edit with no member named yet: follow it with <see cref="WithShader"/>, <see cref="WithQueue"/>.</summary>
        public static MaterialEdit ShaderItem(string material) => new MaterialEdit(MeKey.Shader(material));

        /// <summary>The image: a res name, or null to clear it.</summary>
        public MaterialEdit WithImage(string image)
        {
            HasImage = true;
            Image = image;
            return this;
        }

        /// <summary>The texture offset, or null to clear it; <paramref name="baseline"/> is the material's value without the item.</summary>
        public MaterialEdit WithOffset(Vec2? offset, Vec2? baseline = null)
        {
            HasOffset = true;
            Offset = offset;
            OffsetBaseline = baseline;
            return this;
        }

        /// <summary>The texture scale, or null to clear it; <paramref name="baseline"/> is the material's value without the item.</summary>
        public MaterialEdit WithScale(Vec2? scale, Vec2? baseline = null)
        {
            HasScale = true;
            Scale = scale;
            ScaleBaseline = baseline;
            return this;
        }

        /// <summary>The shader's name, or null to clear it; <paramref name="baseline"/> is the name the material showed before.</summary>
        public MaterialEdit WithShader(string shader, string baseline = null)
        {
            HasShader = true;
            Shader = shader;
            ShaderBaseline = baseline;
            return this;
        }

        /// <summary>The render queue, or null to clear it; <paramref name="baseline"/> is the material's queue without the item.</summary>
        public MaterialEdit WithQueue(int? queue, int? baseline = null)
        {
            HasQueue = true;
            Queue = queue;
            QueueBaseline = baseline;
            return this;
        }

        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        static bool Finite(Vec2? v) => !v.HasValue || (Finite(v.Value.X) && Finite(v.Value.Y));
        static bool Finite(Color4 c) => Finite(c.R) && Finite(c.G) && Finite(c.B) && Finite(c.A);

        /// <summary>The first thing that makes this edit impossible to store, as text; null when it can be stored.</summary>
        public string Problem
        {
            get
            {
                if (string.IsNullOrEmpty(Key.Target)) return Key.Type == "renderer" ? "no renderer named" : "no material named";
                switch (Key.Type)
                {
                    case "float":
                        if (string.IsNullOrEmpty(Key.Member)) return "no property named";
                        return Finite(Number) ? null : "value: a finite number";
                    case "color":
                        if (string.IsNullOrEmpty(Key.Member)) return "no property named";
                        return Finite(Color) ? null : "value: finite numbers";
                    case "keyword":
                        return string.IsNullOrEmpty(Key.Member) ? "no keyword named" : null;
                    case "renderer":
                        return Enabled == 0 || Enabled == 1 ? null : "value: 0 or 1";
                    case "texture":
                        if (string.IsNullOrEmpty(Key.Member)) return "no property named";
                        if (!HasImage && !HasOffset && !HasScale) return "no member named: image, offset or scale";
                        if (HasImage && Image != null && !JsonText.IsResName(Image)) return "image: a res name";
                        return Finite(Offset) && Finite(Scale) ? null : "offset and scale: finite numbers";
                    case "shader":
                        if (!HasShader && !HasQueue) return "no member named: shader or renderQueue";
                        if (HasShader && Shader != null && Shader.Length == 0) return "shader: a name";
                        return !Queue.HasValue || (Queue >= MaterialEdits.MinQueue && Queue <= MaterialEdits.MaxQueue) ? null : "renderQueue: an integer from 0 to 5000";
                    default:
                        return "the type " + Key.Type + " cannot be edited";
                }
            }
        }
    }

    public static class MaterialEdits
    {
        /// <summary>The most items a command or the UI leaves in an array (a file's array is not limited).</summary>
        public const int MaxItems = 4096;
        public const int MinQueue = 0, MaxQueue = 5000;

        // ---------- reading ----------

        /// <summary>True for a key of a type that can be edited: `float`, `color`, `keyword`, `texture`, `shader`, a renderer's `Enabled`.</summary>
        public static bool IsEditable(MeKey key)
        {
            switch (key.Type)
            {
                case "float": case "color": case "keyword": case "texture": case "shader": return true;
                case "renderer": return string.Equals(key.Member, MeKey.EnabledProperty, StringComparison.Ordinal);
                default: return false;
            }
        }

        /// <summary>True for an item an edit, a reset or a clear can touch. Any other is kept, written back and applied as it stands.</summary>
        public static bool IsEditable(MaterialOverride item) => MeKey.TryOf(item, out var k) && IsEditable(k);

        /// <summary>The index of the last item with <paramref name="key"/>, or -1. A file may repeat a key: at apply the later item wins.</summary>
        public static int LastIndex(IReadOnlyList<MaterialOverride> list, MeKey key)
        {
            if (list != null) for (int i = list.Count - 1; i >= 0; i--) if (key.Matches(list[i])) return i;
            return -1;
        }

        /// <summary>The item an edit of <paramref name="key"/> would replace, or null.</summary>
        public static MaterialOverride Find(IReadOnlyList<MaterialOverride> list, MeKey key)
        {
            int i = LastIndex(list, key);
            return i < 0 ? null : list[i];
        }

        /// <summary>Where a new item of apply rank <paramref name="rank"/> goes: after the last item of that rank; else before the first
        /// item of a higher rank; else at the end. Items of an unknown type (rank -1) are stepped over.</summary>
        public static int InsertIndex(IReadOnlyList<MaterialOverride> list, int rank)
        {
            int higher = -1;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                int r = MaterialOverride.Rank(list[i].Type);
                if (r == rank) return i + 1;
                if (r > rank) higher = i;
            }
            return higher >= 0 ? higher : list.Count;
        }

        /// <summary>The items of known types in apply order: by type, the array's order inside a type.</summary>
        public static List<MaterialOverride> InApplyOrder(IReadOnlyList<MaterialOverride> list)
        {
            var r = new List<MaterialOverride>();
            if (list == null) return r;
            for (int rank = 0; rank < MaterialOverride.TypeOrder.Length; rank++)
                foreach (var item in list) if (MaterialOverride.Rank(item.Type) == rank) r.Add(item);
            return r;
        }

        // ---------- writing ----------

        /// <summary>
        /// The array after <paramref name="edit"/>: the last item with its key changed in a copy, or removed when the value equals
        /// the edit's baseline or the item is left with nothing, or a new item at the end of its type's run. The same list when
        /// nothing changes: the value is the one stored, or there is no item and the value equals the baseline. Throws
        /// <see cref="CommandException"/>: bad_args for a value that cannot be stored or an array at <see cref="MaxItems"/>.
        /// </summary>
        public static List<MaterialOverride> Set(List<MaterialOverride> list, MaterialEdit edit)
        {
            string problem = edit.Problem;
            if (problem != null) throw new CommandException(CommandCodes.BadArgs, problem);
            list = list ?? new List<MaterialOverride>();
            int i = LastIndex(list, edit.Key);
            if (i >= 0)
            {
                var e = DocClone.Clone(list[i]);
                if (!Write(e, edit)) return list;
                if (Spent(e, edit)) return Without(list, edit.Key);
                FillOriginals(e, edit);
                var changed = new List<MaterialOverride>(list);
                changed[i] = e;
                return changed;
            }
            var item = New(edit);
            if (item == null) return list;
            if (list.Count >= MaxItems) throw new CommandException(CommandCodes.BadArgs, "an array holds at most " + MaxItems + " items");
            var added = new List<MaterialOverride>(list);
            added.Insert(InsertIndex(list, MaterialOverride.Rank(item.Type)), item);
            return added;
        }

        /// <summary>The array without every item with <paramref name="key"/>; the same list when there is none. Throws
        /// <see cref="CommandException"/> (unsupported) for a key of a read-only type: those items stay.</summary>
        public static List<MaterialOverride> Remove(List<MaterialOverride> list, MeKey key)
        {
            if (!IsEditable(key)) throw new CommandException(CommandCodes.Unsupported, "the item " + key + " cannot be edited");
            return Without(list, key);
        }

        /// <summary>The array without its editable items; read-only ones stay. The same list when there is none.</summary>
        public static List<MaterialOverride> Clear(List<MaterialOverride> list) => Filter(list, IsEditable);

        /// <summary>The array without the editable items of one material; the same list when there is none.</summary>
        public static List<MaterialOverride> ClearMaterial(List<MaterialOverride> list, string material) =>
            Filter(list, x => x is MeMaterialItem m && string.Equals(m.Material, material, StringComparison.Ordinal) && IsEditable(x));

        static List<MaterialOverride> Without(List<MaterialOverride> list, MeKey key) => Filter(list, key.Matches);

        // The array without the items `drop` selects: the other elements are the same objects.
        static List<MaterialOverride> Filter(List<MaterialOverride> list, Func<MaterialOverride, bool> drop)
        {
            if (list == null) return null;
            List<MaterialOverride> r = null;
            for (int i = 0; i < list.Count; i++)
            {
                if (!drop(list[i])) { r?.Add(list[i]); continue; }
                if (r == null) r = list.GetRange(0, i);
            }
            return r ?? list;
        }

        // ---------- one item ----------

        // Writes the named members into `e`; false when they are what it holds. A number written over a value kept as text clears the text.
        static bool Write(MaterialOverride e, MaterialEdit x)
        {
            bool changed = false;
            switch (e)
            {
                case MeFloat f:
                    changed = f.ValueText != null || !FloatBits.Same(f.Value, x.Number);
                    f.Value = x.Number;
                    f.ValueText = null;
                    break;
                case MeColor c:
                    changed = !c.Value.Equals(x.Color);
                    c.Value = x.Color;
                    break;
                case MeKeyword k:
                    changed = k.Value != x.Switch;
                    k.Value = x.Switch;
                    break;
                case MeRenderer r:
                    changed = r.ValueText != null || r.Value != x.Enabled;
                    r.Value = x.Enabled;
                    r.ValueText = null;
                    break;
                case MeTexture t:
                    if (x.HasImage && !string.Equals(t.Image, x.Image, StringComparison.Ordinal)) { t.Image = x.Image; changed = true; }
                    if (x.HasOffset && !Nullable.Equals(t.Offset, x.Offset)) { t.Offset = x.Offset; changed = true; }
                    if (x.HasScale && !Nullable.Equals(t.Scale, x.Scale)) { t.Scale = x.Scale; changed = true; }
                    break;
                case MeShader s:
                    if (x.HasShader && !string.Equals(s.Shader, x.Shader, StringComparison.Ordinal)) { s.Shader = x.Shader; changed = true; }
                    if (x.HasQueue && s.RenderQueue != x.Queue) { s.RenderQueue = x.Queue; changed = true; }
                    break;
            }
            return changed;
        }

        // True when the item has nothing left to store: its value equals what the target holds without it, or a `texture` or `shader`
        // item holds no member. A queue at its baseline is dropped first: the item stays when it holds a shader.
        static bool Spent(MaterialOverride e, MaterialEdit x)
        {
            switch (e)
            {
                case MeFloat f: return x.NumberBaseline.HasValue && FloatBits.Same(f.Value, x.NumberBaseline.Value);
                case MeColor c: return x.ColorBaseline.HasValue && c.Value.Equals(x.ColorBaseline.Value);
                case MeKeyword k: return x.SwitchBaseline.HasValue && k.Value == x.SwitchBaseline.Value;
                case MeRenderer r: return x.EnabledBaseline.HasValue && r.Value == x.EnabledBaseline.Value;
                case MeTexture t: return t.Image == null && !t.Offset.HasValue && !t.Scale.HasValue;
                case MeShader s:
                    if (x.HasQueue && x.QueueBaseline.HasValue && s.RenderQueue == x.QueueBaseline) s.RenderQueue = null;
                    return s.Shader == null && !s.RenderQueue.HasValue;
                default: return false;
            }
        }

        // A stored item without an original for the member just written takes the baseline; any other original stays as read.
        static void FillOriginals(MaterialOverride e, MaterialEdit x)
        {
            switch (e)
            {
                case MeFloat f:
                    if (!f.Original.HasValue && f.OriginalText == null) f.Original = x.NumberBaseline;
                    break;
                case MeColor c:
                    if (!c.Original.HasValue) c.Original = x.ColorBaseline;
                    break;
                case MeRenderer r:
                    if (!r.Original.HasValue && r.OriginalText == null) r.Original = x.EnabledBaseline;
                    break;
                case MeTexture t:
                    if (x.HasOffset && t.Offset.HasValue && !t.OffsetOriginal.HasValue) t.OffsetOriginal = x.OffsetBaseline;
                    if (x.HasScale && t.Scale.HasValue && !t.ScaleOriginal.HasValue) t.ScaleOriginal = x.ScaleBaseline;
                    break;
                case MeShader s:
                    if (x.HasShader && s.Shader != null && s.ShaderOriginal == null) s.ShaderOriginal = x.ShaderBaseline;
                    if (x.HasQueue && s.RenderQueue.HasValue && !s.RenderQueueOriginal.HasValue) s.RenderQueueOriginal = x.QueueBaseline;
                    break;
            }
        }

        // The item of an edit with no stored item; null when there is nothing to store.
        static MaterialOverride New(MaterialEdit x)
        {
            MaterialOverride e;
            switch (x.Key.Type)
            {
                case "float": e = new MeFloat { Material = x.Key.Target, Property = x.Key.Member }; break;
                case "color": e = new MeColor { Material = x.Key.Target, Property = x.Key.Member }; break;
                case "keyword": e = new MeKeyword { Material = x.Key.Target, Property = x.Key.Member, Original = x.SwitchBaseline ?? false }; break;
                case "renderer": e = new MeRenderer { Renderer = x.Key.Target, Property = MeKey.EnabledProperty }; break;
                case "texture": e = new MeTexture { Material = x.Key.Target, Property = x.Key.Member }; break;
                default: e = new MeShader { Material = x.Key.Target }; break;
            }
            Write(e, x);
            if (Spent(e, x)) return null;
            FillOriginals(e, x);
            return e;
        }
    }
}

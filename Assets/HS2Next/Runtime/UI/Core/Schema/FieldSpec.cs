// The inspector schema: data rows that package A's widget factory turns into controls
// and package I's inspectors lay out. A row names a model field path (FieldAccessor syntax, relative to the node — the
// kind section root `chara.`, `item.`, … or `name`, `transform.` — or to the scene settings for scene rows), its kind,
// the HS2 UI range (slider) and the typed-input clamp, the default-button value, the gate (feature predicate), whether it
// applies to every selected node of the kind or to the primary only, and the history label of the edit. The tables are
// in InspectorSchemas (Unity-free); the mono SchemaTests resolve every path on the model with the declared kind.
using System;
using System.Collections.Generic;

namespace HS2Next.UI.Core
{
    public enum FieldKind
    {
        Float, Int, Bool,
        /// <summary>Color4; <see cref="FieldSpec.ColorAlpha"/> says whether the picker edits alpha.</summary>
        Color,
        /// <summary>Color4 edited as LDR colour + HDR intensity (item emission: <see cref="FieldSpec.IntensityPath"/>).</summary>
        ColorHdr,
        Vec2, Vec3,
        /// <summary>One of <see cref="FieldSpec.Choices"/> (string enums of the model, or ints 0..n−1 shown as segmented buttons /
        /// a dropdown).</summary>
        Enum,
        /// <summary>A content ref "guid#id" (string) picked from a list (RefPicker).</summary>
        Ref,
        /// <summary>A FileRef (image / audio file picker).</summary>
        File,
        Text,
        /// <summary>A node id (string) picked from the tree or the viewport (DOF focus, sun-shaft caster).</summary>
        Node,
        /// <summary>No value: runs <see cref="FieldSpec.Action"/> (an op or action id).</summary>
        Button,
        /// <summary>
        /// Maker thumbnail list: a `ref` member (string "guid#id") picked from the
        /// Maker list <see cref="FieldSpec.CategoryFemale"/> / <see cref="FieldSpec.CategoryMale"/> (-1 = no list for that sex),
        /// filtered by <see cref="FieldSpec.ListContext"/> ("head": face skins of the character's head). A pick is
        /// StudioOps.ChangeItem (label "Change item"); no reset.
        /// </summary>
        PartList,
        /// <summary>
        /// Maker push grid: cells from <see cref="FieldSpec.PushSource"/> (<see cref="PushSources"/>); a click
        /// runs one push op on <see cref="FieldSpec.Path"/> (the target: the `face` block for face presets, the layout vector for
        /// layout pushes, the colour member for colour swatches). No value of its own, no current mark, no reset.
        /// </summary>
        Push,
        /// <summary>
        /// Maker hair bundle list: one row per hair part on the `List&lt;HairBundle&gt;`
        /// member `hair.&lt;part&gt;.bundles`; the HairBundleList widget builds the per-bundle rows (six component sliders on
        /// `bundles[i].move` / `.rotate`, the «揺らさない» toggle on `bundles[i].noShake`, the handle toggle) from the part's prefab
        /// facts (MakerServices.Hair). Writes go through StudioOps.EditHairBundle (materialization, the «髪の調整をまとめて行う»
        /// rule); ↺ = the prefab default (<see cref="ResetKind.PrefabDefault"/>).
        /// </summary>
        HairBundles,
        /// <summary>
        /// Maker accessory parent: a `parent` member (string) picked from the 54
        /// attach points in the 10 groups of the attach point table (AttachPointPicker); "" = the row's default, a stored text
        /// that is not one of the 54 is shown as unknown and kept until a pick. A pick is StudioOps.EditAppearance ("Edit
        /// accessory"); «初期親» is a button of its own.
        /// </summary>
        AttachPoint,
    }

    /// <summary>
    /// Where the ↺ of a Maker row returns the value (J8).
    /// </summary>
    public enum ResetKind
    {
        /// <summary>The character's reference value (4.1 F1: StudioOps.ResetAppearance; <see cref="FieldSpec.Default"/> as the
        /// fallback). Every row unless noted.</summary>
        Reference,
        /// <summary><see cref="FieldSpec.Default"/> itself (the hair mesh layout's (1, 1, 0, 0), HS2's constants): the row writes
        /// its default through StudioOps.EditAppearance, label "Reset value".</summary>
        Constant,
        /// <summary>The prefab default of the worn item: the hair bundle components (StudioOps.EditHairBundle with reset); the
        /// clothes gloss, metallic, pattern layout and rotation (StudioOps.ResetAppearance answers those paths with the kind's
        /// prefab defaults, so the row resets like a <see cref="Reference"/> row).</summary>
        PrefabDefault,
    }

    /// <summary>Sources of <see cref="FieldKind.Push"/> rows.</summary>
    public static class PushSources
    {
        /// <summary>Face presets: one cell per head row of the sex whose `Preset` column is set (MakerLists.PresetHeads, F4); a
        /// push = StudioOps.ApplyFacePreset(ids, headRef).</summary>
        public const string FacePreset = "facePreset";
        /// <summary>"layout:&lt;category&gt;": the rows of a layout list (6 mole, 7 face paint); a push =
        /// StudioOps.ApplyLayoutPreset(ids, row.Path, rowRef).</summary>
        public const string LayoutPrefix = "layout:";
        public const int MoleLayoutCategory = 6, FacePaintLayoutCategory = 7;

        public static string Layout(int category) => LayoutPrefix + category.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The category of a "layout:&lt;c&gt;" source, or -1.</summary>
        public static int LayoutCategory(string source) => CategoryAfter(LayoutPrefix, source);

        /// <summary>
        /// "colour:&lt;category&gt;": colour-swatch cells from the rows of a colour preset list
        /// (336 skin colours; 4.3: 305 hair colours) — a chip filled with the row's swatch colour (IAppearanceRows.ColourPreset),
        /// a push = StudioOps.ApplyColourPreset(ids, row.Path, rowRef). For 336 only the vanilla ids 0..6 are cells (HS2's seven
        /// buttons, <see cref="SkinColourPresetIds"/>).
        /// </summary>
        public const string ColourPrefix = "colour:";
        public const int SkinColourCategory = 336, HairColourCategory = 305;
        /// <summary>The skin swatch grid's cells: vanilla rows 0..6 of 336, by id.</summary>
        public const int SkinColourPresetIds = 7;
        /// <summary>The hair swatch grid's cells: vanilla rows 0..6 of 305, by id (HS2's seven
        /// «カラーサンプル» buttons). A push = StudioOps.ApplyColourPreset(ids, `appearance.chara.hair.&lt;part&gt;.baseColor`, rowRef):
        /// the six members of the row on the part (and the other parts with «前髪と後ろ髪の色を統一»).</summary>
        public const int HairColourPresetIds = 7;

        /// <summary>The cells a colour source offers: <see cref="SkinColourPresetIds"/> for 336, <see cref="HairColourPresetIds"/>
        /// for 305 (vanilla ids 0..n−1), -1 for other categories (every listed row).</summary>
        public static int ColourPresetIds(int category) =>
            category == SkinColourCategory ? SkinColourPresetIds : category == HairColourCategory ? HairColourPresetIds : -1;

        public static string Colour(int category) => ColourPrefix + category.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The category of a "colour:&lt;c&gt;" source, or -1.</summary>
        public static int ColourCategory(string source) => CategoryAfter(ColourPrefix, source);

        static int CategoryAfter(string prefix, string source) =>
            source != null && source.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(source.Substring(prefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int c) ? c : -1;
    }

    /// <summary>Which selected nodes an edit writes (U25, items T22).</summary>
    public enum Applies { All, Primary }

    /// <summary>What a path is relative to.</summary>
    public enum FieldTarget
    {
        /// <summary>The node (FieldAccessor node path: `chara.face.eyesOpen`, `transform.pos`, `name`).</summary>
        Node,
        /// <summary>The scene settings (FieldAccessor scene path: `effects.bloom.intensity`, `map.pos`).</summary>
        Scene,
        /// <summary>An app option (OptionDefs key: `gizmo.fkSize`); view state, never history.</summary>
        Option,
    }

    public sealed class FieldSpec
    {
        /// <summary>Field path (null for buttons). Templates hold one placeholder in braces, expanded per instance: {slot}
        /// (accessory slot id), {target} (IK target name), {bone} (FK bone name), {k} (index; Maker face paint 0 / 1, from the
        /// section's <see cref="SchemaSection.Instances"/>), {side} (Maker eye "left" / "right", from the Eyes page's side
        /// selector).</summary>
        public string Path;
        public FieldKind Kind;
        public FieldTarget Target = FieldTarget.Node;
        /// <summary>l10n key ("inspector.chara.face.eyesOpen").</summary>
        public string LabelKey;
        /// <summary>Slider range (HS2's UI range).</summary>
        public double? Min, Max;
        /// <summary>Typed-input clamp (null = no clamp on that side).</summary>
        public double? TypedMin, TypedMax;
        /// <summary>Integer steps (whole-number slider).</summary>
        public bool Whole;
        /// <summary>Value of the default button (null = no button); a string "@name" names a per-node default (items'
        /// own defaults, ItemFeatures.defaults member name).</summary>
        public object Default;
        /// <summary>Feature predicate that shows / enables the row ("item.color[1]", "light.spot", "chara.female", …; null = always;
        /// names in <see cref="SchemaGates"/>).</summary>
        public string Gate;
        public Applies Applies = Applies.All;
        /// <summary>History label (OpLabels; null for Option rows).</summary>
        public string OpLabel;
        /// <summary>Section / tab id ("state.clothes", "face", "effects.bloom").</summary>
        public string Group;

        // ---- members beyond the original field set (needed by the tables) ----
        /// <summary>Component of a vector / colour value edited by this row (0..3), or -1 for the whole value (pattern UV:
        /// x = scale U, y = scale V, z = offset U, w = offset V).</summary>
        public int Component = -1;
        /// <summary>Enum choices: model strings for string enums, or label-key suffixes for int enums (value = position).</summary>
        public IReadOnlyList<string> Choices;
        /// <summary>Int enums whose values are not the positions 0..n−1 (neck look buttons {0, 1, 3, 4}); null = positions.</summary>
        public IReadOnlyList<int> ChoiceValues;
        /// <summary>Choice label key prefix ("enum.clothesState." → "enum.clothesState.on").</summary>
        public string ChoiceLabelPrefix;
        /// <summary>Colour rows: the picker edits alpha.</summary>
        public bool ColorAlpha = true;
        /// <summary>
        /// Colour rows with <see cref="ColorAlpha"/>: a gate (a <see cref="SchemaGates"/> name) that narrows the alpha to the
        /// characters where it holds — the clothes main colours have alpha only while the worn prefab sets `useColorA0n`
        /// ("maker.clothes:&lt;kind&gt;:alpha&lt;n&gt;"). The host evaluates it on the primary when the picker opens and on the
        /// section's live re-evaluation (no row rebuild); while it does not hold the picker shows no alpha and keeps the
        /// stored one. Null: <see cref="ColorAlpha"/> alone decides.
        /// </summary>
        public string ColorAlphaGate;
        /// <summary>ColorHdr rows: the path of the HDR intensity (stops) kept per node (items T5).</summary>
        public string IntensityPath;
        /// <summary>Bool rows shown inverted (item pattern "clamp" toggle: on = clamp false).</summary>
        public bool Inverted;
        /// <summary>Nullable value (camera FOV override, FK hips position): the UI offers "none".</summary>
        public bool Nullable;
        /// <summary>Button rows: the op / action to run ("ops.ResetTransform:pos", an ActionIds id, …).</summary>
        public string Action;
        /// <summary>Another row's path whose true value disables this one (dynamic bones while item FK is on).</summary>
        public string DisabledBy;
        /// <summary>Display format of numeric fields (".NET format, e.g. "0.00"); null = the widget default.</summary>
        public string Format;
        /// <summary>Rotation input: typed values wrap with % 360 (map pose, vanilla); transform rot does not (U29). With
        /// <see cref="Grid"/> (the accessory rotation) the wrap is into [0, 360) and steps wrap
        /// too (359 + 5 → 4).</summary>
        public bool Wrap360;
        /// <summary>
        /// The grid of a UI write (the accessory transform rows; null = none): a typed value,
        /// a step or a drag is rounded to a multiple of Grid (half away from zero; position 0.1, scale 0.01) or, with
        /// <see cref="GridTruncate"/>, truncated toward zero (rotation, Grid 1). Applied to every component of a vector row. The
        /// stored value is shown as it is (stored values off the grid are never rewritten without an edit).
        /// </summary>
        public double? Grid;
        /// <summary>The grid truncates toward zero instead of rounding (HS2's integer rotation).</summary>
        public bool GridTruncate;
        /// <summary>
        /// Display scale of numeric rows: the field shows stored × Scale and a typed number
        /// n means n / Scale. Maker sliders use 100: stored 0..1 shows 0–100 (vanilla text = round(100 v)), typed −100..200
        /// stores −1..2. Min / Max / TypedMin / TypedMax / Default stay in stored units. 1 = shown as stored.
        /// </summary>
        public double Scale = 1;

        // ---- Maker rows ----
        /// <summary>PartList rows: the Maker list category for females / males (-1 = no list for that sex: the row is hidden).</summary>
        public int CategoryFemale = -1, CategoryMale = -1;
        /// <summary>PartList rows: what filters the list besides category and sex: "head" = the face skins listed for the bound
        /// character's `face.head` (MakerLists ListContext.Head); null = none.</summary>
        public string ListContext;
        /// <summary>Push rows: the cell source (<see cref="PushSources"/>).</summary>
        public string PushSource;
        /// <summary>PartList / Push rows: the row column naming the cell thumbnail (null = `ThumbTex`; the face preset grid uses
        /// `Preset`).</summary>
        public string ThumbColumn;
        /// <summary>Maker rows whose stored value goes through a clamped Mathf.Lerp (HS2 + slider extender): values outside 0..1
        /// take the effect of 0 or 1 (F12; the slider row shows a tooltip). False for direct members and shapes.</summary>
        public bool LerpClamped;
        /// <summary>l10n key of a tooltip the row shows on its label (null = none; the futanari
        /// toggle says it is data only in Studio).</summary>
        public string TooltipKey;
        /// <summary>Where ↺ returns the value: the reference value (default), the row's
        /// <see cref="Default"/> (<see cref="ResetKind.Constant"/>) or the prefab default (<see cref="ResetKind.PrefabDefault"/>).</summary>
        public ResetKind ResetKind = ResetKind.Reference;
        /// <summary>
        /// A value line without an input element (Phase 4.6: the Character node's sex, futanari, imported game parameters and
        /// import provenance): the host shows the value of <see cref="Path"/> and never writes it; no reset, no history label.
        /// Acceptance rows that edit every row skip it as "read-only".
        /// </summary>
        public bool ReadOnly;

        public const string ListContextHead = "head";
        /// <summary>PartList rows of an accessory item: the list is the accessory's own category (the row's CategoryFemale /
        /// CategoryMale, generated for it).</summary>
        public const string ListContextAccessory = "accessory";

        /// <summary>The list category of a PartList row for a sex (0 male, 1 female), or -1.</summary>
        public int CategoryFor(int sex) => sex == 0 ? CategoryMale : CategoryFemale;

        /// <summary>The number shown for a stored value (stored × <see cref="Scale"/>); stored values outside the slider range
        /// are shown as they are (the knob sits at the end).</summary>
        public double ToDisplay(double stored) => stored * Scale;

        /// <summary>The stored value of a number in display units (÷ <see cref="Scale"/>), without clamping.</summary>
        public double FromDisplay(double shown) => Scale == 0 ? shown : shown / Scale;

        /// <summary>A typed number in display units → the stored value, clamped to TypedMin..TypedMax (<see cref="ClampTyped"/>).</summary>
        public double ParseTyped(double shown) => ClampTyped(FromDisplay(shown));

        /// <summary>The slider knob position for a stored value: clamped to Min..Max (the stored value is not changed).</summary>
        public double KnobOf(double stored) => ClampSlider(stored);

        public bool IsTemplate => Path != null && Path.IndexOf('{') >= 0;

        /// <summary>The label key of choice <paramref name="i"/> (position in Choices).</summary>
        public string ChoiceLabelKey(int i) => Choices == null || i < 0 || i >= Choices.Count ? null : (ChoiceLabelPrefix ?? "") + Choices[i];

        /// <summary>The stored value of choice <paramref name="i"/>: the string for string enums (Choices are values), else
        /// ChoiceValues[i] or i.</summary>
        public object ChoiceValue(int i, bool stringEnum) => stringEnum ? (object)Choices[i] : ChoiceValues != null ? ChoiceValues[i] : i;

        /// <summary>The path with its placeholder replaced (`chara.state.accessories["{slot}"]` + "3" → `…["3"]`).</summary>
        public string Expand(string value)
        {
            if (!IsTemplate) return Path;
            int a = Path.IndexOf('{'), b = Path.IndexOf('}', a);
            return Path.Substring(0, a) + value + Path.Substring(b + 1);
        }

        /// <summary>The placeholder name of a template ("slot"), or null.</summary>
        public string Placeholder
        {
            get
            {
                if (!IsTemplate) return null;
                int a = Path.IndexOf('{'), b = Path.IndexOf('}', a);
                return Path.Substring(a + 1, b - a - 1);
            }
        }

        /// <summary>Clamps a typed value (TypedMin / TypedMax; Whole rounds; on the <see cref="Grid"/>; Wrap360 wraps).</summary>
        public double ClampTyped(double v)
        {
            if (TypedMin.HasValue && v < TypedMin.Value) v = TypedMin.Value;
            if (TypedMax.HasValue && v > TypedMax.Value) v = TypedMax.Value;
            if (Whole) v = Math.Round(v, MidpointRounding.AwayFromZero);
            if (Grid.HasValue) v = OnGrid(v);
            if (Wrap360) v = Grid.HasValue ? WrapPositive(v) : v % 360.0;
            return v;
        }

        /// <summary>
        /// A step or drag write on a grid row: <paramref name="current"/> + <paramref name="delta"/>
        /// on the <see cref="Grid"/>, then wrapped into [0, 360) with <see cref="Wrap360"/> (rotation: 359 + 5 → 4), else clamped to
        /// TypedMin..TypedMax (position −100..100, scale 0.01..100).
        /// </summary>
        public double Step(double current, double delta)
        {
            double v = current + delta;
            if (Grid.HasValue) v = OnGrid(v);
            if (Wrap360) return WrapPositive(v);
            if (TypedMin.HasValue && v < TypedMin.Value) v = TypedMin.Value;
            if (TypedMax.HasValue && v > TypedMax.Value) v = TypedMax.Value;
            return v;
        }

        /// <summary>A value on the <see cref="Grid"/> (rounded half away from zero to the grid's decimals, or truncated toward
        /// zero with <see cref="GridTruncate"/>); unchanged without a grid.</summary>
        public double OnGrid(double v)
        {
            if (!Grid.HasValue || !(Grid.Value > 0)) return v;
            double g = Grid.Value;
            if (GridTruncate) return Math.Truncate(v / g) * g;
            int decimals = (int)Math.Round(-Math.Log10(g));
            if (decimals >= 0 && decimals <= 15 && Math.Abs(Math.Pow(10, -decimals) - g) < 1e-12) return Math.Round(v, decimals, MidpointRounding.AwayFromZero);
            return Math.Round(v / g, MidpointRounding.AwayFromZero) * g;
        }

        static double WrapPositive(double v)
        {
            double r = v % 360.0;
            if (r < 0) r += 360.0;
            return r == 0 ? 0 : r;
        }

        /// <summary>Clamps a slider value (Min / Max; Whole rounds).</summary>
        public double ClampSlider(double v)
        {
            if (Min.HasValue && v < Min.Value) v = Min.Value;
            if (Max.HasValue && v > Max.Value) v = Max.Value;
            if (Whole) v = Math.Round(v, MidpointRounding.AwayFromZero);
            return v;
        }

        /// <summary>A stable id for bindings and tests: target + path (+ component), the button action, or for a push row
        /// "push:&lt;source&gt;:" + path (a push grid can share its target path with a value row: the skin swatches and the skin
        /// colour).</summary>
        public string Id => Kind == FieldKind.Button ? "button:" + (Action ?? LabelKey)
            : Kind == FieldKind.Push ? "push:" + (PushSource ?? "") + ":" + Path
            : Target.ToString().ToLowerInvariant() + ":" + Path + (Component >= 0 ? "#" + Component : "");

        public override string ToString() => Id + " (" + Kind + ")";

        public FieldSpec Clone() => (FieldSpec)MemberwiseClone();
    }

    /// <summary>A named section of an inspector: a tab of the character inspector, a foldout of the others.</summary>
    public sealed class SchemaSection
    {
        public string Id;
        public string LabelKey;
        public readonly List<FieldSpec> Fields = new List<FieldSpec>();
        public SchemaSection(string id, string labelKey) { Id = id; LabelKey = labelKey; }
        /// <summary>
        /// A repeated section: the placeholder its templated rows
        /// hold ("k") and one instance per value; the UI builds one foldout per instance (id, label) with the rows expanded by
        /// the instance's value. Null for an ordinary section (whose Id / LabelKey are used).
        /// </summary>
        public string Placeholder;
        public IReadOnlyList<SectionInstance> Instances;
        public bool IsRepeated => Instances != null && Instances.Count > 0;
        /// <summary>
        /// The gate of the whole section (null = none). Informational: every row of a gated
        /// section carries this gate or a narrower one that implies it, so a host that evaluates the rows only (a section
        /// whose rows are all hidden hides) shows the same.
        /// </summary>
        public string Gate;
        /// <summary>
        /// Watched paths (full `appearance.chara.…` paths; null = none): a
        /// change of one of them on the primary (NodeChanged, after the flush that realized it) makes the host re-evaluate
        /// the section's gates and rebuild its rows — the rows depend on the document value or on the prefab it selects.
        /// </summary>
        public IReadOnlyList<string> Watch;
        public override string ToString() => Id + " (" + Fields.Count + " fields" + (IsRepeated ? ", x" + Instances.Count : "") + ")";
    }

    /// <summary>One instance of a repeated <see cref="SchemaSection"/>: the placeholder value, the section id and label key.</summary>
    public sealed class SectionInstance
    {
        public readonly string Value, Id, LabelKey;
        public SectionInstance(string value, string id, string labelKey) { Value = value; Id = id; LabelKey = labelKey; }
        public override string ToString() => Id + " ({" + Value + "})";
    }
}

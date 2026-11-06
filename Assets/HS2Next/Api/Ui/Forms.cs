// Declarative forms of a plugin (experimental: every type here may change in any minor release). A form is a list of rows
// that the host draws with the widgets of its own inspector, as a panel of the Studio shell or as a section of the
// inspector of one node kind; it needs no engine types, so an engine-free plugin can declare one. The host reads the values
// through the form's read command and writes edits through its write command, both commands of the plugin itself; with
// UiForm.Undo the host records one undo entry per committed edit in the plugin's name. The protocol is described on UiForm,
// the value shapes on RowKind. Part of the Api (assembly HS2Next.Api).
using System;
using System.Collections.Generic;

namespace HS2Next.Api.Ui
{
    /// <summary>
    /// The kind of a form row: the widget the host draws and the JSON shape of its value. The names and values are those of
    /// the host's own inspector rows, so a plugin row draws the widget of the host row of the same kind.
    /// </summary>
    [Experimental("the declarative forms may change in any minor release")]
    public enum RowKind
    {
        /// <summary>A number slider with a field; the value is a number.</summary>
        Float = 0,
        /// <summary>A whole-number slider with a field; the value is a whole number.</summary>
        Int = 1,
        /// <summary>A toggle; the value is a bool.</summary>
        Bool = 2,
        /// <summary>A colour swatch with its picker; the value is [r, g, b, a] in 0–1 (<see cref="UiRow.ColorAlpha"/>: the picker edits alpha).</summary>
        Color = 3,
        /// <summary>A colour with an HDR intensity; the value is {"color": [r, g, b, a], "intensity": number}.</summary>
        ColorHdr = 4,
        /// <summary>Two number fields; the value is [x, y].</summary>
        Vec2 = 5,
        /// <summary>Three number fields; the value is [x, y, z].</summary>
        Vec3 = 6,
        /// <summary>Segmented buttons (up to four choices) or a dropdown; the value is the <see cref="UiChoice.Value"/> of a choice.</summary>
        Enum = 7,
        /// <summary>A reference picker over the choices that <see cref="UiRow.ChoicesCommand"/> answers; the value is a choice's value (a string) or null.</summary>
        Ref = 8,
        /// <summary>A file picker; the value is a file path (a string) or null.</summary>
        File = 9,
        /// <summary>A text field; the value is a string or null.</summary>
        Text = 10,
        /// <summary>A scene node picker; the value is a node id (a string) or null. <see cref="UiRow.ChoicesCommand"/>, when set, names the nodes offered.</summary>
        Node = 11,
        /// <summary>A button without a value: it runs <see cref="UiRow.Command"/>.</summary>
        Button = 12,
        /// <summary>
        /// A Maker thumbnail list of the list category <see cref="UiRow.CategoryFemale"/> / <see cref="UiRow.CategoryMale"/>
        /// (by the sex of the section's character); the value is a content reference "&lt;guid&gt;#&lt;id&gt;" (a string) or null.
        /// </summary>
        PartList = 13,
        /// <summary>
        /// A Maker push grid of the cells of <see cref="UiRow.PushSource"/>, without a value of its own: a click sends a commit
        /// whose value is the cell's content reference.
        /// </summary>
        Push = 14,
        /// <summary>Reserved: it edits the host's own hair bundles of a character, so a form row of this kind is refused.</summary>
        HairBundles = 15,
        /// <summary>The accessory attach-point picker (the 54 points in their groups); the value is a point's name (a string) or null.</summary>
        AttachPoint = 16,
    }

    /// <summary>The phase of an edit a widget reports.</summary>
    [Experimental("the declarative forms may change in any minor release")]
    public enum EditPhase
    {
        /// <summary>A value while a drag moves (the edit is not finished).</summary>
        Preview = 0,
        /// <summary>A finished edit: the end of a drag, a typed value, a toggle, a choice, a pick.</summary>
        Commit = 1,
        /// <summary>The drag was cancelled (Esc): the value is the one before the drag.</summary>
        Cancel = 2,
    }

    /// <summary>One choice of an <see cref="RowKind.Enum"/> row (or of a choices reply).</summary>
    [Experimental("the declarative forms may change in any minor release")]
    public sealed class UiChoice
    {
        /// <summary>An empty choice (set the properties).</summary>
        public UiChoice() { }

        /// <summary>A choice with its value and label.</summary>
        public UiChoice(string value, string labelKey)
        {
            Value = value;
            LabelKey = labelKey;
        }

        /// <summary>The value the read command answers and the write command receives for this choice.</summary>
        public string Value { get; set; }

        /// <summary>The label: a key of the plugin's strings.</summary>
        public string LabelKey { get; set; }

        /// <summary>The value, for logs.</summary>
        public override string ToString() => Value ?? "";
    }

    /// <summary>
    /// One row of a <see cref="UiForm"/>: the value's key, its kind and the widget's settings. Create it with a factory of its
    /// kind and set further properties, or with an object initializer. Ranges and defaults are in the value's own units.
    /// </summary>
    [Experimental("the declarative forms may change in any minor release")]
    public sealed class UiRow
    {
        /// <summary>The value's name in the read and write protocol; unique in the form.</summary>
        public string Key { get; set; }

        /// <summary>The widget and the value's shape.</summary>
        public RowKind Kind { get; set; }

        /// <summary>The row's label: a key of the plugin's strings.</summary>
        public string LabelKey { get; set; }

        /// <summary>A tooltip on the label: a key of the plugin's strings; null = none.</summary>
        public string TooltipKey { get; set; }

        /// <summary>The slider's lower end (Float, Int); null = no slider, the field only.</summary>
        public double? Min { get; set; }

        /// <summary>The slider's upper end (Float, Int); null = no slider, the field only.</summary>
        public double? Max { get; set; }

        /// <summary>The lowest value a typed number is clamped to; null = no clamp.</summary>
        public double? TypedMin { get; set; }

        /// <summary>The highest value a typed number is clamped to; null = no clamp.</summary>
        public double? TypedMax { get; set; }

        /// <summary>Whole-number steps (true for Int rows).</summary>
        public bool Whole { get; set; }

        /// <summary>The value the row's reset button writes (a number, a bool, an array, a string); null = no reset button.</summary>
        public object Default { get; set; }

        /// <summary>The display format of numbers (a .NET numeric format such as "0.00"); null = the widget's own.</summary>
        public string Format { get; set; }

        /// <summary>The display scale of numbers: the field shows value × Scale and a typed n means n / Scale. 1 = as stored.</summary>
        public double Scale { get; set; } = 1;

        /// <summary>The grid of an edit: typed values, steps and drags are rounded to a multiple of it; null = none.</summary>
        public double? Grid { get; set; }

        /// <summary>The grid truncates toward zero instead of rounding.</summary>
        public bool GridTruncate { get; set; }

        /// <summary>Typed values wrap into 0–360 (rotations).</summary>
        public bool Wrap360 { get; set; }

        /// <summary>The value may be null: the widget offers "none".</summary>
        public bool Nullable { get; set; }

        /// <summary>The row shows its value and never writes it.</summary>
        public bool ReadOnly { get; set; }

        /// <summary>Color rows: the picker edits alpha.</summary>
        public bool ColorAlpha { get; set; } = true;

        /// <summary>Enum rows: the choices in their order (a read reply's <c>choices</c> overrides them).</summary>
        public IReadOnlyList<UiChoice> Choices { get; set; }

        /// <summary>Ref and Node rows: a command of the plugin answering <c>{choices: [{value, label}]}</c>.</summary>
        public string ChoicesCommand { get; set; }

        /// <summary>Button rows: the command of the plugin the button runs.</summary>
        public string Command { get; set; }

        /// <summary>Button rows: further arguments of <see cref="Command"/>; null = none.</summary>
        public IDictionary<string, object> CommandArgs { get; set; }

        /// <summary>PartList rows: the Maker list category for female characters; -1 = no list (the row is hidden).</summary>
        public int CategoryFemale { get; set; } = -1;

        /// <summary>PartList rows: the Maker list category for male characters; -1 = no list (the row is hidden).</summary>
        public int CategoryMale { get; set; } = -1;

        /// <summary>
        /// Push rows: one of the host's push sources: "facePreset" (the face presets), "layout:&lt;category&gt;" (the rows of a
        /// layout list) or "colour:&lt;category&gt;" (the colour swatches of a colour preset list).
        /// </summary>
        public string PushSource { get; set; }

        /// <summary>"key (Kind)", for logs.</summary>
        public override string ToString() => (Key ?? "") + " (" + Kind + ")";

        static UiRow Make(string key, RowKind kind, string labelKey) => new UiRow { Key = key, Kind = kind, LabelKey = labelKey };

        /// <summary>A number row with a slider from <paramref name="min"/> to <paramref name="max"/>.</summary>
        public static UiRow Float(string key, string labelKey, double min, double max, double? defaultValue = null, string format = null)
        {
            var r = Make(key, RowKind.Float, labelKey);
            r.Min = min;
            r.Max = max;
            r.Default = defaultValue;
            r.Format = format;
            return r;
        }

        /// <summary>A whole-number row with a slider from <paramref name="min"/> to <paramref name="max"/>.</summary>
        public static UiRow Int(string key, string labelKey, int min, int max, int? defaultValue = null)
        {
            var r = Make(key, RowKind.Int, labelKey);
            r.Min = min;
            r.Max = max;
            r.Whole = true;
            r.Default = defaultValue;
            return r;
        }

        /// <summary>A toggle row.</summary>
        public static UiRow Bool(string key, string labelKey) => Make(key, RowKind.Bool, labelKey);

        /// <summary>A colour row; <paramref name="alpha"/>: the picker edits alpha.</summary>
        public static UiRow Color(string key, string labelKey, bool alpha = true)
        {
            var r = Make(key, RowKind.Color, labelKey);
            r.ColorAlpha = alpha;
            return r;
        }

        /// <summary>A two-component vector row.</summary>
        public static UiRow Vec2(string key, string labelKey) => Make(key, RowKind.Vec2, labelKey);

        /// <summary>A three-component vector row.</summary>
        public static UiRow Vec3(string key, string labelKey) => Make(key, RowKind.Vec3, labelKey);

        /// <summary>A choice row with <paramref name="choices"/> in their order.</summary>
        public static UiRow Enum(string key, string labelKey, params UiChoice[] choices)
        {
            var r = Make(key, RowKind.Enum, labelKey);
            r.Choices = choices;
            return r;
        }

        /// <summary>A reference row whose choices the command <paramref name="choicesCommand"/> answers.</summary>
        public static UiRow Ref(string key, string labelKey, string choicesCommand)
        {
            var r = Make(key, RowKind.Ref, labelKey);
            r.ChoicesCommand = choicesCommand;
            return r;
        }

        /// <summary>A file row.</summary>
        public static UiRow File(string key, string labelKey) => Make(key, RowKind.File, labelKey);

        /// <summary>A text row.</summary>
        public static UiRow Text(string key, string labelKey) => Make(key, RowKind.Text, labelKey);

        /// <summary>A scene node row; <paramref name="choicesCommand"/>, when set, names the nodes offered.</summary>
        public static UiRow Node(string key, string labelKey, string choicesCommand = null)
        {
            var r = Make(key, RowKind.Node, labelKey);
            r.ChoicesCommand = choicesCommand;
            return r;
        }

        /// <summary>A button row that runs the command <paramref name="command"/>.</summary>
        public static UiRow Button(string key, string labelKey, string command)
        {
            var r = Make(key, RowKind.Button, labelKey);
            r.Command = command;
            return r;
        }

        /// <summary>An accessory attach-point row.</summary>
        public static UiRow AttachPoint(string key, string labelKey) => Make(key, RowKind.AttachPoint, labelKey);

        /// <summary>A Maker thumbnail list row of the list categories for female and male characters (-1 = none for that sex).</summary>
        public static UiRow PartList(string key, string labelKey, int categoryFemale, int categoryMale)
        {
            var r = Make(key, RowKind.PartList, labelKey);
            r.CategoryFemale = categoryFemale;
            r.CategoryMale = categoryMale;
            return r;
        }

        /// <summary>A Maker push grid row of the push source <paramref name="pushSource"/>.</summary>
        public static UiRow Push(string key, string labelKey, string pushSource)
        {
            var r = Make(key, RowKind.Push, labelKey);
            r.PushSource = pushSource;
            return r;
        }
    }

    /// <summary>
    /// A declarative form: its rows and the two commands of the plugin that read and write their values. The host validates
    /// and copies the form when it is registered (<see cref="IPluginForms"/>); later changes to this object or its rows have
    /// no effect.
    /// </summary>
    /// <remarks>
    /// <para>The host calls the plugin's commands inline; a command that defers its reply or is asynchronous is refused there,
    /// and the form shows the error.</para>
    /// <para>Read: <c>{"cmd": ReadCommand, "form": id, "node": node id or null}</c>. The reply holds <c>values</c> (an object:
    /// row key → value, shaped as <see cref="RowKind"/> describes) and optionally <c>hidden</c> and <c>disabled</c> (arrays of
    /// row keys) and <c>choices</c> (row key → <c>[{value, label}]</c>, overriding <see cref="UiRow.Choices"/>). A value of the
    /// wrong shape shows "—" and the row reports the error once in the plugin's log; a failed read shows its error text in
    /// place of the rows, which stay disabled until the next good read.</para>
    /// <para>Write: <c>{"cmd": WriteCommand, "form", "node", "key", "value", "phase", "undo": false}</c> with phase
    /// <c>preview</c> (while a drag moves, at most one per frame), <c>commit</c> (the end of a drag, a typed value, a toggle,
    /// a choice, a pick), <c>cancel</c> (Esc during a drag: the value before it), <c>undo</c> or <c>redo</c>. The reply is ok,
    /// or an error whose message the host shows as a toast.</para>
    /// <para>Button: <c>{"cmd": row.Command, "form", "node", "key"}</c> plus <see cref="UiRow.CommandArgs"/>; ok or an error
    /// (a toast). Choices (Ref and Node rows): <c>{"cmd": row.ChoicesCommand, "form", "node", "key"}</c>, answering
    /// <c>{choices: [{value, label}]}</c>.</para>
    /// <para>Undo: with <see cref="Undo"/> true the host records, after each successful commit, one undo entry in the
    /// plugin's name labelled with the row's label, whose undo writes the value read before the edit (phase <c>undo</c>) and
    /// whose redo writes the committed value (phase <c>redo</c>); typed commits of one row merge as the host's own typed edits
    /// do. The write command must not record an entry of its own for these writes (their envelope says <c>"undo":
    /// false</c>).</para>
    /// <para>Reads: when the form is first drawn, after commit, cancel, undo, redo and a button, on
    /// <see cref="IPluginForms.Refresh"/>, after a new or loaded scene, and for a section when the inspected node changes; at
    /// most one read per form and node per frame.</para>
    /// </remarks>
    [Experimental("the declarative forms may change in any minor release")]
    public sealed class UiForm
    {
        /// <summary>An empty form (set the properties).</summary>
        public UiForm() { }

        /// <summary>A form with its read command and, when a row is editable, its write command.</summary>
        public UiForm(string readCommand, string writeCommand = null)
        {
            ReadCommand = readCommand;
            WriteCommand = writeCommand;
        }

        /// <summary>The plugin's command that answers the values (required).</summary>
        public string ReadCommand { get; set; }

        /// <summary>The plugin's command that takes an edit; required when a row is editable.</summary>
        public string WriteCommand { get; set; }

        /// <summary>True (the default): the host records one undo entry per successful commit; false: the host records none.</summary>
        public bool Undo { get; set; } = true;

        /// <summary>The rows, in their order.</summary>
        public IList<UiRow> Rows { get; set; } = new List<UiRow>();
    }

    /// <summary>
    /// The plugin's declarative panels and inspector sections. Implemented by the host. Ids are stored under the plugin's GUID
    /// ("&lt;guid&gt;/&lt;id&gt;") and every form is removed when the plugin unloads or is disabled (an open panel loses its
    /// tab). Registration validates the form and throws ArgumentException, registering nothing, for: an empty or duplicate
    /// id; a region or kind outside the lists; an empty <see cref="UiForm.ReadCommand"/>; a command outside the plugin's
    /// command prefixes (the command itself may be registered later, before the form is first drawn); an empty or duplicate
    /// row key; Min above Max; an Enum row without choices; a Button row without a command; a write command missing while a
    /// row is editable; a PartList row without a category; a Push row whose source is not a host push source; a row of kind
    /// <see cref="RowKind.HairBundles"/>. Without the Studio UI (a batch player, a test host) forms are validated and counted
    /// but not drawn. Elements are named "plugin-form-&lt;guid&gt;/&lt;id&gt;-&lt;key&gt;". Main thread only.
    /// </summary>
    [Experimental("the declarative forms may change in any minor release")]
    public interface IPluginForms
    {
        /// <summary>True when the Studio UI runs and forms are drawn.</summary>
        bool Available { get; }

        /// <summary>
        /// Adds <paramref name="form"/> as a panel of <paramref name="region"/> ("left", "right" or "bottom") titled by the
        /// string key <paramref name="titleKey"/>; lower <paramref name="order"/> comes first.
        /// </summary>
        void Panel(string id, string titleKey, string region, UiForm form, int order = 0);

        /// <summary>
        /// Adds <paramref name="form"/> as a section of the inspector of nodes of <paramref name="kind"/> ("chara", "item",
        /// "light", "camera", "folder", "route"); its read and write calls carry the inspected node's id.
        /// </summary>
        void Section(string kind, string id, string titleKey, UiForm form, int order = 0);

        /// <summary>Removes the panel or section <paramref name="id"/>; true when one was registered.</summary>
        bool Remove(string id);

        /// <summary>
        /// Reads the form <paramref name="id"/> again (for the node <paramref name="nodeId"/>, or for every node it shows when
        /// null), e.g. after the plugin's state changed through one of its own commands; at most one read per form and node
        /// per frame.
        /// </summary>
        void Refresh(string id, string nodeId = null);
    }
}

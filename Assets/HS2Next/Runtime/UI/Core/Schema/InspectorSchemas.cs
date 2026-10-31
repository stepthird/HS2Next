// The inspector schema tables: sections per node kind and for the scene
// panel, built once. Ranges are the HS2 UI ranges (scene model A.1)
// with the HS2Next changes of the confirmation tables (items T22). The tables themselves are in the partial files
// InspectorSchemas.Chara.cs, .Item.cs, .Scene.cs (one per area). Unity-free.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HS2Next.UI.Core
{
    public static partial class InspectorSchemas
    {
        /// <summary>Kind section names of the model (SceneNode kinds).</summary>
        public const string KindChara = "chara", KindItem = "item", KindLight = "light", KindFolder = "folder", KindCamera = "camera", KindRoute = "route";

        // Built on first use (static initialisers of partial-class files run in an unspecified file order, and the builders
        // read the name lists declared in the other files).
        static SchemaSection header, transform;
        static IReadOnlyList<SchemaSection> chara, item, light, camera, folder, scene, route, sceneImages, sceneRoutes, capture;
        static readonly object buildLock = new object();

        static T Once<T>(ref T field, Func<T> build) where T : class
        {
            if (field != null) return field;
            lock (buildLock) return field ?? (field = build());
        }

        /// <summary>Node header rows of every kind: the name override.</summary>
        public static SchemaSection Header => Once(ref header, BuildHeader);
        /// <summary>The Transform section shown for every kind with transform channels.</summary>
        public static SchemaSection Transform => Once(ref transform, BuildTransform);
        /// <summary>The character inspector's tabs, in tab order.</summary>
        public static IReadOnlyList<SchemaSection> Chara => Once(ref chara, () => BuildChara());
        public static IReadOnlyList<SchemaSection> Item => Once(ref item, () => BuildItem());
        public static IReadOnlyList<SchemaSection> Light => Once(ref light, () => BuildLight());
        public static IReadOnlyList<SchemaSection> Camera => Once(ref camera, () => BuildCamera());
        public static IReadOnlyList<SchemaSection> Folder => Once(ref folder, () => BuildFolder());
        /// <summary>The scene panel's sections, vanilla effects order.</summary>
        public static IReadOnlyList<SchemaSection> Scene => Once(ref scene, () => BuildScene());
        /// <summary>The route inspector (Phase 3.6): Route, then Route point (rows gated on a
        /// sub-selected point; paths are templates over the point index {point}).</summary>
        public static IReadOnlyList<SchemaSection> Route => Once(ref route, () => BuildRoute());
        /// <summary>The scene panel's Background / frame section (package C places it, replacing the 3.5 placeholder).
        /// Not in <see cref="Scene"/>, so the generic scene panel loop does not show it before C wires it.</summary>
        public static IReadOnlyList<SchemaSection> SceneImages => Once(ref sceneImages, () => BuildSceneImages());
        /// <summary>The scene panel's Routes section (the vanilla route list; package R places it; the rows of the list
        /// are runtime data, the section holds its three buttons). Not in <see cref="Scene"/> either.</summary>
        public static IReadOnlyList<SchemaSection> SceneRoutes => Once(ref sceneRoutes, () => BuildSceneRoutes());
        /// <summary>The screenshot dialog's option rows (package C): capture.* options (view state, never history).</summary>
        public static IReadOnlyList<SchemaSection> Capture => Once(ref capture, () => BuildCapture());

        /// <summary>The kind sections of a node kind (empty for an unknown kind).</summary>
        public static IReadOnlyList<SchemaSection> ForKind(string kind)
        {
            switch (kind)
            {
                case KindChara: return Chara;
                case KindItem: return Item;
                case KindLight: return Light;
                case KindCamera: return Camera;
                case KindFolder: return Folder;
                case KindRoute: return Route;
                default: return new SchemaSection[0];
            }
        }

        /// <summary>Every table with the target it resolves against ("chara", …, "route", "scene", "node" for Header / Transform,
        /// "options" for option-only tables).</summary>
        public static IEnumerable<(string owner, SchemaSection section)> AllSections()
        {
            yield return ("node", Header);
            yield return ("node", Transform);
            foreach (var s in Chara) yield return (KindChara, s);
            foreach (var s in Item) yield return (KindItem, s);
            foreach (var s in Light) yield return (KindLight, s);
            foreach (var s in Camera) yield return (KindCamera, s);
            foreach (var s in Folder) yield return (KindFolder, s);
            foreach (var s in Route) yield return (KindRoute, s);
            foreach (var s in Scene) yield return ("scene", s);
            foreach (var s in SceneImages) yield return ("scene", s);
            foreach (var s in SceneRoutes) yield return ("scene", s);
            foreach (var s in Capture) yield return ("options", s);
        }

        public static IEnumerable<FieldSpec> AllFields() => AllSections().SelectMany(s => s.section.Fields);

        /// <summary>The first row with a path (and component, -1 = any) in a list of sections, or null.</summary>
        public static FieldSpec Find(IEnumerable<SchemaSection> sections, string path, int component = -1) =>
            sections.SelectMany(s => s.Fields).FirstOrDefault(f => f.Path == path && (component < 0 || f.Component == component));

        // ---------------- builder helpers ----------------

        sealed class B
        {
            readonly SchemaSection s;
            readonly string labelPrefix;
            readonly string op;
            readonly FieldTarget target;
            public B(SchemaSection section, string labelPrefix, string opLabel, FieldTarget target = FieldTarget.Node)
            {
                s = section;
                this.labelPrefix = labelPrefix;
                op = opLabel;
                this.target = target;
            }

            public FieldSpec Add(FieldSpec f)
            {
                f.Group = f.Group ?? s.Id;
                if (f.Target == FieldTarget.Node) f.Target = target;
                if (f.OpLabel == null && f.Target != FieldTarget.Option) f.OpLabel = op;
                s.Fields.Add(f);
                return f;
            }

            string Label(string name) => labelPrefix + name;

            public FieldSpec Float(string path, string name, double min, double max, object def = null, double? typedMin = null, double? typedMax = null, bool whole = false) =>
                Add(new FieldSpec { Path = path, Kind = FieldKind.Float, LabelKey = Label(name), Min = min, Max = max, TypedMin = typedMin, TypedMax = typedMax, Default = def, Whole = whole });

            /// <summary>A float with the typed clamp equal to the slider range (HS2's InputField clamp).</summary>
            public FieldSpec FloatClamped(string path, string name, double min, double max, object def = null, bool whole = false) =>
                Float(path, name, min, max, def, min, max, whole);

            public FieldSpec Int(string path, string name, double min, double max, object def = null) =>
                Add(new FieldSpec { Path = path, Kind = FieldKind.Int, LabelKey = Label(name), Min = min, Max = max, TypedMin = min, TypedMax = max, Default = def, Whole = true });

            /// <summary>A pattern / list position (0..n−1, n from the runtime: ◀ ▶ + dropdown).</summary>
            public FieldSpec Index(string path, string name) =>
                Add(new FieldSpec { Path = path, Kind = FieldKind.Int, LabelKey = Label(name), Min = 0, TypedMin = 0, Whole = true });

            public FieldSpec Bool(string path, string name) =>
                Add(new FieldSpec { Path = path, Kind = FieldKind.Bool, LabelKey = Label(name) });

            public FieldSpec Color(string path, string name, bool alpha, object def = null) =>
                Add(new FieldSpec { Path = path, Kind = FieldKind.Color, LabelKey = Label(name), ColorAlpha = alpha, Default = def });

            public FieldSpec Vec3(string path, string name, object def = null) =>
                Add(new FieldSpec { Path = path, Kind = FieldKind.Vec3, LabelKey = Label(name), Default = def });

            public FieldSpec Enum(string path, string name, string[] choices, string choicePrefix, object def = null) =>
                Add(new FieldSpec { Path = path, Kind = FieldKind.Enum, LabelKey = Label(name), Choices = choices, ChoiceLabelPrefix = choicePrefix, Default = def });

            public FieldSpec Ref(string path, string name) =>
                Add(new FieldSpec { Path = path, Kind = FieldKind.Ref, LabelKey = Label(name) });

            public FieldSpec File(string path, string name) =>
                Add(new FieldSpec { Path = path, Kind = FieldKind.File, LabelKey = Label(name), Nullable = true });

            public FieldSpec Text(string path, string name) =>
                Add(new FieldSpec { Path = path, Kind = FieldKind.Text, LabelKey = Label(name) });

            public FieldSpec Node(string path, string name) =>
                Add(new FieldSpec { Path = path, Kind = FieldKind.Node, LabelKey = Label(name), Nullable = true });

            public FieldSpec Button(string action, string name, string opLabel = null) =>
                Add(new FieldSpec { Kind = FieldKind.Button, LabelKey = Label(name), Action = action, OpLabel = opLabel ?? op });
        }

        static SchemaSection BuildHeader()
        {
            var s = new SchemaSection("header", "inspector.section.header");
            var b = new B(s, "inspector.node.", OpLabels.Rename);
            var name = b.Text("name", "name");
            name.Nullable = true;
            name.Applies = Applies.Primary;   // a header field renames the primary (StudioOps.Rename takes one id)
            return s;
        }

        static SchemaSection BuildTransform()
        {
            var s = new SchemaSection("transform", "inspector.section.transform");
            var b = new B(s, "inspector.transform.", OpLabels.Transform);
            b.Vec3("transform.pos", "pos", new[] { 0.0, 0.0, 0.0 }).Format = "0.#####";
            b.Vec3("transform.rot", "rot", new[] { 0.0, 0.0, 0.0 }).Format = "0.#####";
            var sc = b.Vec3("transform.scale", "scale", new[] { 1.0, 1.0, 1.0 });
            sc.Format = "0.#####";
            sc.TypedMin = 0.01;
            sc.TypedMax = 9999999;
            b.Button("ops.ResetTransform:pos", "resetPos", OpLabels.ResetTransform);
            b.Button("ops.ResetTransform:rot", "resetRot", OpLabels.ResetTransform);
            b.Button("ops.ResetTransform:scale", "resetScale", OpLabels.ResetTransform);
            b.Button(ActionIds.CopyTransform, "copy");
            b.Button(ActionIds.PasteTransform, "paste");
            return s;
        }
    }
}

// MakerEdits, the face pages: the head and linked-eye consequences, the
// face preset push, layout presets, eye copy, the row reset (ResetSets / ResetAppearance, used by every page) and the
// missing-preset fallback. Split out of MakerEdits.cs (Step 0 of Phase 4.4); no change of behaviour.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Content;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    public static partial class MakerEdits
    {
        // ---- head

        static void HeadChange(PlanState ps, string head)
        {
            if (ps.O.Rows == null) { ps.Report.Warn("edit.listsUnavailable: face.skin not checked against the new head"); return; }
            var skins = ps.O.Rows.SkinsForHead(ps.Sex, head);
            if (skins == null) { ps.Report.Warn("edit.listsUnavailable: no skin list for head " + head); return; }
            if (skins.Count == 0) return;   // a head without skin rows keeps the skin
            string skinPath = AppearancePaths.Chara("face.skin");
            var cur = ps.Current(skinPath) as string;
            foreach (var s in skins) if (string.Equals(s, cur, StringComparison.Ordinal)) return;
            ps.Add(skinPath, skins[0], true);
        }

        // ---- eyes

        static void LinkedEye(PlanState ps, string member, object value, object before)
        {
            if (!Bool(ps, AppearancePaths.Chara("face.eyes.linked"), true)) return;
            string other = member.StartsWith("face.eyes.left", StringComparison.Ordinal)
                ? "face.eyes.right" + member.Substring("face.eyes.left".Length)
                : "face.eyes.left" + member.Substring("face.eyes.right".Length);
            string otherPath = AppearancePaths.Chara(other);
            // the size vectors (HS2: pupilW / pupilH are separate controls, CvsF_EyeLR copies only the edited scalar): copy the
            // components that changed, so linked eyes that already differ keep their other component (
            // H's FC7 note; 4.1 impl)
            if ((member.EndsWith(".iris.size", StringComparison.Ordinal) || member.EndsWith(".pupil.size", StringComparison.Ordinal)) &&
                value is Vec2 v && before is Vec2 b && ps.Current(otherPath) is Vec2 o)
            {
                ps.Add(otherPath, new Vec2(v.X != b.X ? v.X : o.X, v.Y != b.Y ? v.Y : o.Y), true);
                return;
            }
            ps.Add(otherPath, value, true);
        }

        // ================================================================ face preset

        /// <summary>The sets of a face preset push: every member of the `face` block (the preset's `head` included).</summary>
        public static List<(string path, object value)> FacePresetSets(FaceData preset)
        {
            if (preset == null) throw Bad("no preset");
            return new List<(string, object)>
            {
                (AppearancePaths.Chara("face.head"), preset.Head),
                (AppearancePaths.Chara("face.skin"), preset.Skin),
                (AppearancePaths.Chara("face.detail"), preset.Detail),
                (AppearancePaths.Chara("face.shape"), preset.Shape),
                (AppearancePaths.Chara("face.eyebrow"), preset.Eyebrow),
                (AppearancePaths.Chara("face.eyes"), preset.Eyes),
                (AppearancePaths.Chara("face.eyelashes"), preset.Eyelashes),
                (AppearancePaths.Chara("face.mole"), preset.Mole),
                (AppearancePaths.Chara("face.makeup"), preset.Makeup),
                (AppearancePaths.Chara("face.beard"), preset.Beard),
            };
        }

        // The `ref` members of the face block with their member paths.
        static readonly (string member, Func<FaceData, string> get, Action<FaceData, string> set)[] FaceRefs =
        {
            ("face.head", f => f.Head, (f, v) => f.Head = v),
            ("face.skin", f => f.Skin, (f, v) => f.Skin = v),
            ("face.detail.item", f => f.Detail.Item, (f, v) => f.Detail.Item = v),
            ("face.eyebrow.item", f => f.Eyebrow.Item, (f, v) => f.Eyebrow.Item = v),
            ("face.eyes.left.iris.item", f => f.Eyes.Left.Iris.Item, (f, v) => f.Eyes.Left.Iris.Item = v),
            ("face.eyes.right.iris.item", f => f.Eyes.Right.Iris.Item, (f, v) => f.Eyes.Right.Iris.Item = v),
            ("face.eyes.left.pupil.item", f => f.Eyes.Left.Pupil.Item, (f, v) => f.Eyes.Left.Pupil.Item = v),
            ("face.eyes.right.pupil.item", f => f.Eyes.Right.Pupil.Item, (f, v) => f.Eyes.Right.Pupil.Item = v),
            ("face.eyes.highlight.item", f => f.Eyes.Highlight.Item, (f, v) => f.Eyes.Highlight.Item = v),
            ("face.eyelashes.item", f => f.Eyelashes.Item, (f, v) => f.Eyelashes.Item = v),
            ("face.mole.item", f => f.Mole.Item, (f, v) => f.Mole.Item = v),
            ("face.makeup.eyeshadow.item", f => f.Makeup.Eyeshadow.Item, (f, v) => f.Makeup.Eyeshadow.Item = v),
            ("face.makeup.cheek.item", f => f.Makeup.Cheek.Item, (f, v) => f.Makeup.Cheek.Item = v),
            ("face.makeup.lip.item", f => f.Makeup.Lip.Item, (f, v) => f.Makeup.Lip.Item = v),
            ("face.makeup.paint[0].item", f => f.Makeup.Paint[0].Item, (f, v) => f.Makeup.Paint[0].Item = v),
            ("face.makeup.paint[1].item", f => f.Makeup.Paint[1].Item, (f, v) => f.Makeup.Paint[1].Item = v),
            ("face.makeup.paint[0].layoutItem", f => f.Makeup.Paint[0].LayoutItem, (f, v) => f.Makeup.Paint[0].LayoutItem = v),
            ("face.makeup.paint[1].layoutItem", f => f.Makeup.Paint[1].LayoutItem, (f, v) => f.Makeup.Paint[1].LayoutItem = v),
            ("face.beard.item", f => f.Beard.Item, (f, v) => f.Beard.Item = v),
        };

        /// <summary>
        /// The face block a preset push writes (a decision): a copy of <paramref name="preset"/> in which every
        /// `ref` member whose row is not listed for the character (<see cref="IAppearanceRows.HasRow"/>) keeps the character's
        /// current value, each reported as `edit.presetUnresolved: &lt;member&gt; &lt;ref&gt; …`. HS2 would write the unlisted id;
        /// HS2Next does not (no unlisted head, no load-order compatibility match). Without lists the preset is used as is
        /// (`edit.listsUnavailable`).
        /// </summary>
        public static FaceData ListedPreset(FaceData preset, FaceData current, int sex, IAppearanceRows rows, MakerEditReport report)
        {
            if (preset == null) throw Bad("no preset");
            var r = DocClone.Clone(preset);
            if (rows == null)
            {
                report?.Warn("edit.listsUnavailable: the face preset's items are not checked against the lists");
                return r;
            }
            bool headFallback = false;
            foreach (var (member, get, set) in FaceRefs)
            {
                string v = get(r), cur = current != null ? get(current) : null;
                if (v == null || string.Equals(v, cur, StringComparison.Ordinal)) continue;
                int cat = CategoryOf(AppearancePaths.Chara(member), sex);
                if (cat < 0 || rows.HasRow(cat, sex, v)) continue;
                string keep = cur ?? v;
                // an unlisted head: HS2's ChangeHeadAsync falls back to the sex's head 0 (the first listed head) and the skin
                // snaps to its list (HF4, a decision)
                if (member == "face.head")
                {
                    var first = rows.FirstRow(cat, sex);
                    if (first != null) { keep = first; headFallback = true; }
                }
                set(r, keep);
                report?.Warn("edit.presetUnresolved: " + member + " " + v + " is not a listed row of category " + cat.ToString(CultureInfo.InvariantCulture) +
                             (member == "face.head" && headFallback ? " (head " + keep + ", HS2's fallback)" : " (kept " + keep + ")"));
            }
            if (headFallback)
            {
                var skins = rows.SkinsForHead(sex, r.Head);
                if (skins != null && skins.Count > 0 && IndexOfRef(skins, r.Skin) < 0) r.Skin = skins[0];
            }
            return r;
        }

        static int IndexOfRef(IReadOnlyList<string> l, string v)
        {
            for (int i = 0; i < l.Count; i++) if (string.Equals(l[i], v, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>A face preset push of node <paramref name="node"/> in <paramref name="tx"/>: <see cref="ListedPreset"/>, then the
        /// sets of every member of the face block (one entry with the caller's commit).</summary>
        public static CommandResult ApplyFacePreset(Transaction tx, SceneModel model, TreeNode node, FaceData preset, MakerEditOptions o, MakerEditReport report)
        {
            report = report ?? new MakerEditReport();
            var chara = node?.Appearance?.Stored?.Chara;
            if (chara == null) return CommandResult.Error(CommandCodes.Unsupported, "character not imported");
            FaceData face;
            try { face = ListedPreset(preset, chara.Face, chara.Profile.Sex, o?.Rows, report); }
            catch (CommandException e) { return CommandResult.Error(e.Code, e.Message); }
            return Edit(tx, model, node, FacePresetSets(face), o, report);
        }

        // ================================================================ face pages

        /// <summary>The layout list a layout path pushes from: 6 for `face.mole.layout`, 7 for
        /// `face.makeup.paint[0|1].layout`, else -1 (paths full or relative to `appearance.`).</summary>
        public static int LayoutCategoryOf(string layoutPath)
        {
            string c = FieldPath.Canonical(AppearancePaths.FromRelative(layoutPath ?? ""));
            if (c == null) return -1;
            if (c == AppearancePaths.Chara("face.mole.layout")) return 6;
            if (c == AppearancePaths.Chara("face.makeup.paint[0].layout") || c == AppearancePaths.Chara("face.makeup.paint[1].layout")) return 7;
            return -1;
        }

        /// <summary>
        /// A layout push (F18) on node <paramref name="node"/>: <paramref name="layoutPath"/> ← (scale, scale, posX, posY)
        /// of row <paramref name="rowRef"/> (looked up by id through <see cref="IAppearanceRows.LayoutValues"/>); rotation and
        /// `layoutItem` untouched. bad_args for another path, not_found for an unknown row, unsupported without lists.
        /// </summary>
        public static CommandResult ApplyLayoutPreset(Transaction tx, SceneModel model, TreeNode node, string layoutPath, string rowRef, MakerEditOptions o, MakerEditReport report)
        {
            int cat = LayoutCategoryOf(layoutPath);
            if (cat < 0) return CommandResult.Error(CommandCodes.BadArgs, "a layout push writes appearance.chara.face.mole.layout or appearance.chara.face.makeup.paint[0|1].layout, not " + layoutPath);
            if (o?.Rows == null) return CommandResult.Error(CommandCodes.Unsupported, "layout presets need the Maker lists (no list service is registered)");
            if (!JsonText.IsRef(rowRef ?? "") || !o.Rows.LayoutValues(cat, rowRef, out float scale, out float x, out float y))
                return CommandResult.Error(CommandCodes.NotFound, rowRef + " is not a row of layout list " + cat.ToString(CultureInfo.InvariantCulture));
            string path = FieldPath.Canonical(AppearancePaths.FromRelative(layoutPath));
            return Edit(tx, model, node, new List<(string, object)> { (path, new Vec4(scale, scale, x, y)) }, o, report);
        }

        /// <summary>
        /// An eye copy button (F10): `face.eyes.right` ← a clone of `face.eyes.left` (<paramref name="leftToRight"/>) or the
        /// reverse, as one whole-object set; with the link on, the linked-eye consequence re-sets the source to its own value.
        /// Highlight, offset and shadow are shared and not copied.
        /// </summary>
        public static CommandResult CopyEye(Transaction tx, SceneModel model, TreeNode node, bool leftToRight, MakerEditOptions o, MakerEditReport report)
        {
            var eyes = node?.Appearance?.Stored?.Chara?.Face?.Eyes;
            if (eyes == null) return CommandResult.Error(CommandCodes.Unsupported, "character not imported");
            var src = DocClone.Clone(leftToRight ? eyes.Left : eyes.Right);
            return Edit(tx, model, node, new List<(string, object)> { (AppearancePaths.Chara(leftToRight ? "face.eyes.right" : "face.eyes.left"), src) }, o, report);
        }

        /// <summary>Splits a reset path "&lt;path&gt;#&lt;component&gt;" (the FieldSpec.Id form of a component row) into the vector
        /// path and the component; component -1 for a plain path.</summary>
        public static string SplitComponent(string path, out int component)
        {
            component = -1;
            if (path == null) return null;
            int h = path.LastIndexOf('#');
            if (h < 0 || path.IndexOf(']', h) >= 0 || path.IndexOf('"', h) >= 0) return path;
            if (!int.TryParse(path.Substring(h + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int c) || c < 0 || c > 3) return path;
            component = c;
            return path.Substring(0, h);
        }

        /// <summary>
        /// The sets of a row reset (F1) for node <paramref name="node"/>: each path (full or relative to `appearance.`, a
        /// component row as "path#c") ← the node's reference value (<paramref name="references"/>), else
        /// <paramref name="defaults"/>[i] (the row's FieldSpec.Default: a number, a bool, a string, double[] for colours and
        /// vectors, a scalar for a component), converted to the member's type; a component row replaces only its component of
        /// the node's current vector. A path with neither is skipped with `edit.resetUnavailable`. Values are not clamped.
        /// </summary>
        public static List<(string path, object value)> ResetSets(SceneModel model, TreeNode node, EditReferences references, IReadOnlyList<string> paths,
            IReadOnlyList<object> defaults, MakerEditReport report, out int fromReference)
        {
            fromReference = 0;
            var sets = new List<(string, object)>();
            if (paths == null) return sets;
            for (int i = 0; i < paths.Count; i++)
            {
                string vec = SplitComponent(paths[i], out int comp);
                string path = FieldPath.Canonical(AppearancePaths.FromRelative(vec ?? ""));
                if (path == null || !AppearancePaths.IsAppearance(path)) throw Bad("reset path " + paths[i] + ": an appearance member path");
                Type leaf;
                try { leaf = FieldAccessor.LeafType(model, node.Id, path); }
                catch (CommandException e) { throw new CommandException(e.Code, "reset path " + paths[i] + ": " + e.Message); }
                if (comp >= 0 && ComponentCount(leaf) <= comp) throw Bad("reset path " + paths[i] + ": a " + leaf.Name + " has no component " + comp.ToString(CultureInfo.InvariantCulture));
                object value = null;
                if (references != null && references.TryGet(node.Id, path, out var refValue))
                {
                    value = comp >= 0 ? (object)ComponentOf(refValue, comp) : refValue;
                    fromReference++;
                }
                else if (defaults != null && i < defaults.Count && defaults[i] != null)
                    value = comp >= 0 ? (object)ScalarOf(defaults[i], comp) : ValueOf(leaf, defaults[i]);
                if (value == null || (comp >= 0 && !(value is float)))
                {
                    report?.Warn("edit.resetUnavailable: node " + node.Id + ": " + paths[i] + " has no reference value and no default");
                    continue;
                }
                if (comp >= 0)
                {
                    var cur = FieldAccessor.Get(model, node.Id, path);
                    value = WithComponent(cur, comp, (float)value);
                }
                sets.Add((path, value));
            }
            return sets;
        }

        /// <summary>A row reset of node <paramref name="node"/> in <paramref name="tx"/> (<see cref="ResetSets"/>, then the
        /// planner with its consequences: a linked eye resets both eyes).</summary>
        public static CommandResult ResetAppearance(Transaction tx, SceneModel model, TreeNode node, EditReferences references, IReadOnlyList<string> paths,
            IReadOnlyList<object> defaults, MakerEditOptions o, MakerEditReport report)
        {
            if (node?.Appearance?.Stored == null) return CommandResult.Error(CommandCodes.Unsupported, "character not imported");
            List<(string, object)> sets;
            try { sets = ResetSets(model, node, references, paths, defaults, report, out _); }
            catch (CommandException e) { return CommandResult.Error(e.Code, e.Message); }
            if (sets.Count == 0) return CommandResult.NoOp(null);
            return Edit(tx, model, node, sets, o, report);
        }

        static int ComponentCount(Type t) =>
            t == typeof(Vec4) || t == typeof(Color4) || t == typeof(Quat4) ? 4 : t == typeof(Vec3) || t == typeof(Vec3?) ? 3 : t == typeof(Vec2) ? 2 : 0;

        static float? ComponentOf(object v, int c)
        {
            switch (v)
            {
                case Vec4 a: return c == 0 ? a.X : c == 1 ? a.Y : c == 2 ? a.Z : a.W;
                case Vec3 b: return c == 0 ? b.X : c == 1 ? b.Y : b.Z;
                case Vec2 d: return c == 0 ? d.X : d.Y;
                case Color4 e: return c == 0 ? e.R : c == 1 ? e.G : c == 2 ? e.B : e.A;
            }
            return null;
        }

        static object WithComponent(object v, int c, float x)
        {
            switch (v)
            {
                case Vec4 a: return new Vec4(c == 0 ? x : a.X, c == 1 ? x : a.Y, c == 2 ? x : a.Z, c == 3 ? x : a.W);
                case Vec3 b: return new Vec3(c == 0 ? x : b.X, c == 1 ? x : b.Y, c == 2 ? x : b.Z);
                case Vec2 d: return new Vec2(c == 0 ? x : d.X, c == 1 ? x : d.Y);
                case Color4 e: return new Color4(c == 0 ? x : e.R, c == 1 ? x : e.G, c == 2 ? x : e.B, c == 3 ? x : e.A);
            }
            throw Bad("a component reset needs a vector or colour member");
        }

        // A UI default of a component row: a scalar, or the row's whole vector (double[]) / a vector value.
        static float? ScalarOf(object d, int c)
        {
            switch (d)
            {
                case double x: return (float)x;
                case float f: return f;
                case int i: return i;
                case double[] arr: return c < arr.Length ? (float)arr[c] : (float?)null;
                case float[] farr: return c < farr.Length ? farr[c] : (float?)null;
            }
            return ComponentOf(d, c);
        }

        /// <summary>
        /// A value in the UI / bridge form converted to a member type: numbers to float / int, double[] / float[] of the right
        /// length to Vec2 / Vec3 / Vec4 / Color4 / float[], values already of the type copied; null when it does not convert.
        /// </summary>
        public static object ValueOf(Type t, object v)
        {
            if (v == null || t == null) return null;
            if (t.IsInstanceOfType(v)) return FieldSchema.Copy(v);
            if (t == typeof(float) || t == typeof(float?))
                return v is double d ? (float)d : v is int i ? (float)i : v is long l ? (float)l : (object)null;
            if (t == typeof(int))
                return v is long l2 && l2 >= int.MinValue && l2 <= int.MaxValue ? (int)l2 : v is double d2 && d2 == Math.Floor(d2) && Math.Abs(d2) < int.MaxValue ? (int)d2 : (object)null;
            float[] a = v is double[] da ? Array.ConvertAll(da, x => (float)x) : v as float[];
            if (a == null) return null;
            if (t == typeof(Vec4) && a.Length == 4) return new Vec4(a[0], a[1], a[2], a[3]);
            if (t == typeof(Color4) && a.Length == 4) return new Color4(a[0], a[1], a[2], a[3]);
            if ((t == typeof(Vec3) || t == typeof(Vec3?)) && a.Length == 3) return new Vec3(a[0], a[1], a[2]);
            if (t == typeof(Vec2) && a.Length == 2) return new Vec2(a[0], a[1]);
            if (t == typeof(float[])) return (float[])a.Clone();
            return null;
        }

        /// <summary>
        /// The missing-preset fallback (GF4, F5): a push of head <paramref name="headRef"/> whose preset asset is
        /// absent does what HS2's push does — the head change with its consequences (skin snap) — and warns
        /// `edit.presetMissing`. not_found when the head is not a listed head of the character's sex (with lists).
        /// </summary>
        public static CommandResult FacePresetFallback(Transaction tx, SceneModel model, TreeNode node, string headRef, MakerEditOptions o, MakerEditReport report)
        {
            var chara = node?.Appearance?.Stored?.Chara;
            if (chara == null) return CommandResult.Error(CommandCodes.Unsupported, "character not imported");
            int sex = chara.Profile.Sex;
            int cat = sex == 0 ? 110 : 210;
            report = report ?? new MakerEditReport();
            if (o?.Rows != null && !string.Equals(headRef, chara.Face.Head, StringComparison.Ordinal) && !o.Rows.HasRow(cat, sex, headRef))
            {
                string msg = "head " + headRef + " is not a listed head of category " + cat.ToString(CultureInfo.InvariantCulture) + " for this character";
                if (!o.SkipUnlistedRows) return CommandResult.Error(CommandCodes.NotFound, msg);
                report.Warn("edit.rowSkipped: node " + node.Id + ": " + msg);   // multi-selection: the other sex is skipped
                return CommandResult.NoOp(null);
            }
            report.Warn("edit.presetMissing: head " + headRef + " has no face preset asset; the push changed the head only (HS2)");
            return Edit(tx, model, node, new List<(string, object)> { (AppearancePaths.Chara("face.head"), headRef) }, o, report);
        }
    }
}

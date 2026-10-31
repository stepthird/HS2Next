// The inspectors' writes: one schema row + the target ids + a
// widget value -> the StudioOps method of that row (never the session or the model directly). The UI clamps to the row's
// typed range first (StudioOps stores what it is given). Rows that edit one component of a vector, or that keep other
// per-node values (animation parameters, FK / IK per character, look target), make one call per node inside one bus
// transaction (BeginOrJoin: a widget drag that is open absorbs it, so a drag is still one entry). "All" rows are written
// only to the targets where the row's gate holds. Buttons: RunButton (the schema's Action strings).
using System;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Motion.Fk;
using HS2Next.Motion.Ik;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using UnityEngine;
using static HS2Next.UI.Panels.InspectorValues;

namespace HS2Next.UI.Panels
{
    public static class InspectorWrites
    {
        /// <summary>The copied animation of "Copy" (vanilla AnimeControl: the primary's clip; Paste loads it on the selection).</summary>
        public static AnimeRef CopiedAnime;

        static ExecuteOptions O(FieldSpec f) => UiContext.User(string.IsNullOrEmpty(f?.OpLabel) ? null : f.OpLabel);

        /// <summary>Targets of a node row: the given ids filtered by the row's gate (Applies.Primary: the first only).</summary>
        public static List<string> TargetsOf(StudioOps ops, FieldSpec f, IReadOnlyList<string> ids)
        {
            var l = new List<string>();
            if (ids == null) return l;
            foreach (var id in ids)
            {
                if (InspectorGates.Holds(ops, f.Gate, id)) l.Add(id);
                if (f.Applies == Applies.Primary) break;
            }
            return l;
        }

        /// <summary>A typed / slider value clamped by the row (numbers only; other values pass).</summary>
        public static object Clamp(FieldSpec f, object v, bool typed)
        {
            if (!(v is double || v is float || v is int || v is long)) return v;
            double d = D(v);
            if (double.IsNaN(d) || double.IsInfinity(d)) return null;
            return typed ? f.ClampTyped(d) : f.ClampSlider(d);
        }

        /// <summary>
        /// Writes a widget value of the inspector's own controls to row <paramref name="f"/> (template placeholder =
        /// <paramref name="arg"/>) of every target (node rows) or of the scene (scene rows): numbers clamped with the typed
        /// clamp, a component row merged into each target's own vector, then <see cref="Route"/>. Shows errors / warnings.
        /// </summary>
        public static OpResult Write(UiContext ctx, FieldSpec f, string arg, IReadOnlyList<string> ids, object value, string mergeKey = null)
        {
            if (ctx?.Ops == null || f == null) return OpResult.NothingToDo();
            if (f.Kind == FieldKind.Button) return RunButton(ctx, f, arg, ids);
            if (f.Target == FieldTarget.Option) return OpResult.NothingToDo();   // view options: UiOptions (A's FieldWriters)
            string path = f.Expand(arg);
            value = Clamp(f, value, true);
            var o = UiContext.User(string.IsNullOrEmpty(f.OpLabel) ? null : f.OpLabel, mergeKey);
            if (f.Target == FieldTarget.Scene)
                return ctx.ShowResult(Route(ctx, f, path, new string[] { null }, f.Component >= 0 ? Merge(ctx, f, path, null, value) : value, o));
            var targets = TargetsOf(ctx.Ops, f, ids);
            if (targets.Count == 0) return OpResult.NothingToDo();
            if (f.Component < 0) return ctx.ShowResult(Route(ctx, f, path, targets, value, o));
            return ctx.ShowResult(PerNode(ctx, f, targets, id => Route(ctx, f, path, new[] { id }, Merge(ctx, f, path, id, value), o)));
        }

        /// <summary>
        /// The schema-row -> StudioOps routing for a model-typed whole value (A's FieldWriter shape: FieldWidgets has merged
        /// components per target already). Node targets are filtered by the row's gate; a single null target = the scene.
        /// </summary>
        public static OpResult Route(UiContext ctx, FieldSpec f, string path, IReadOnlyList<string> ids, object value, ExecuteOptions o)
        {
            if (ctx?.Ops == null || path == null) return OpResult.NothingToDo();
            f ??= new FieldSpec { Path = path };
            if (string.IsNullOrEmpty(o.Label) && !string.IsNullOrEmpty(f.OpLabel)) o.Label = f.OpLabel;
            try
            {
                if (ids == null || (ids.Count == 1 && ids[0] == null)) return WriteScene(ctx, f, path, value, o);
                var targets = TargetsOf(ctx.Ops, f, ids);
                if (targets.Count == 0) return OpResult.NothingToDo();
                return WriteNode(ctx, f, path, targets, value, o);
            }
            catch (Exception e) when (e is InvalidCastException || e is FormatException || e is OverflowException)
            {
                return OpResult.BadArgs(path, f.Kind.ToString(), "inspector value of " + path + " not convertible: " + e.Message);
            }
        }

        /// <summary>A's FieldWriter delegate over <see cref="Route"/>.</summary>
        public static OpResult AsFieldWriter(UiContext ctx, HS2Next.UI.Binding.FieldWrite w) => Route(ctx, w.Spec, w.Path, w.Targets, w.Value, w.Options);

        /// <summary>One call per node inside one transaction (joins an open widget drag).</summary>
        static OpResult PerNode(UiContext ctx, FieldSpec f, IReadOnlyList<string> ids, Func<string, OpResult> one)
        {
            string label = string.IsNullOrEmpty(f?.OpLabel) ? "Edit" : f.OpLabel;
            if (ids.Count == 1) return one(ids[0]);
            var tx = ctx.Session.Commands.BeginOrJoin(label, UiContext.User(label));
            if (!tx.IsOpen) return OpResult.Fail(tx.Failure?.Code ?? CommandCodes.Busy, tx.Failure?.Message ?? "the bus is busy");
            var last = OpResult.NothingToDo();
            using (tx)
            {
                foreach (var id in ids)
                {
                    var r = one(id);
                    if (!r.Ok) { tx.Cancel(); return r; }   // a joined handle reverts only its own steps
                    if (r.Status == CommandStatus.Ok || last.Status == CommandStatus.NoOp) last = r;
                }
                var c = tx.Commit();
                if (c.Status == CommandStatus.Error) return OpResult.From(c);
            }
            ctx.Runtime?.ApplyNow();
            return last;
        }

        /// <summary>The value of a component row merged into each node's own vector.</summary>
        static object Merge(UiContext ctx, FieldSpec f, string path, string id, object value) =>
            f.Component >= 0 ? WithComponent(Raw(ctx.Model, id, path), f.Component, D(value)) : value;

        static int IndexOf(string path, string prefix)
        {
            if (!path.StartsWith(prefix, StringComparison.Ordinal)) return -1;
            int a = prefix.Length, b = path.IndexOf(']', a);
            return b > a && int.TryParse(path.Substring(a, b - a), NumberStyles.Integer, CultureInfo.InvariantCulture, out int k) ? k : -1;
        }

        static bool Is(string path, string p) => string.Equals(path, p, StringComparison.Ordinal);
        static bool Starts(string path, string p) => path.StartsWith(p, StringComparison.Ordinal);

        static Type LeafType(UiContext ctx, string id, string path)
        {
            try { return FieldAccessor.LeafType(ctx.Model, id, path); } catch (Exception) { return typeof(object); }
        }

        // ------------------------------------------------------------ node rows

        static OpResult WriteNode(UiContext ctx, FieldSpec f, string path, List<string> ids, object value, ExecuteOptions o)
        {
            var ops = ctx.Ops;
            string primary = ids[0];

            // header / transform
            if (Is(path, "name")) return ops.Rename(primary, value is string s && s.Length > 0 ? s : null, o);
            if (Starts(path, "transform."))
            {
                string ch = path.Substring(10);
                return PerNode(ctx, f, ids, id =>
                {
                    var v = U3(V3(value));
                    if (ch == "scale" && !(ctx.Model.Find(id) is TreeNode tn && StudioOps.HasScaleChannel(tn))) return OpResult.NothingToDo();
                    return ops.SetTransform(new[] { id }, ch == "pos" ? v : (Vector3?)null, ch == "rot" ? v : (Vector3?)null, ch == "scale" ? v : (Vector3?)null, o);
                });
            }

            // ---- character
            if (Starts(path, "chara."))
            {
                switch (path)
                {
                    case "chara.anime.speed": return ops.SetAnimeSpeed(ids, F(value), null, o);
                    case "chara.anime.pattern": return ops.SetAnimePattern(ids, F(value), o);
                    case "chara.anime.optionVisible": return ops.SetAnimeOption(ids, B(value), null, null, o);
                    case "chara.anime.forceLoop": return ops.SetAnimeOption(ids, null, null, B(value), o);
                    case "chara.anime.ref":
                    {
                        var row = value is AnimeRef ar ? InspectorGates.AnimeRow(ops, ar) : null;
                        return row == null ? OpResult.NotFound("no animation row for the value", path) : ops.SetAnimation(ids, row, 0f, null, null, null, o);
                    }
                    case "chara.face.eyebrow": return ops.SetFace(ids, new FaceEdit { Eyebrow = I(value) }, o);
                    case "chara.face.eyes": return ops.SetFace(ids, new FaceEdit { Eyes = I(value) }, o);
                    case "chara.face.mouth": return ops.SetFace(ids, new FaceEdit { Mouth = I(value) }, o);
                    case "chara.face.eyesOpen": return ops.SetFace(ids, new FaceEdit { EyesOpen = F(value) }, o);
                    case "chara.face.mouthOpen": return ops.SetFace(ids, new FaceEdit { MouthOpen = F(value) }, o);
                    case "chara.face.blink": return ops.SetFace(ids, new FaceEdit { Blink = B(value) }, o);
                    case "chara.face.lipSync": return ops.SetFace(ids, new FaceEdit { LipSync = B(value) }, o);
                    case "chara.look.eyes": return ops.SetLook(ids, I(value), null, o);
                    case "chara.look.neck": return ops.SetLook(ids, null, I(value), o);
                    case "chara.look.target": return ops.SetLookTargetLocal(ids, V3(value), o);
                    case "chara.fk.enabled": return PerNode(ctx, f, ids, id => ops.SetFkEnabled(id, B(value), o));
                    case "chara.ik.enabled": return PerNode(ctx, f, ids, id => ops.SetIkEnabled(id, B(value), o));
                    case "chara.fk.hipsOffset": return ops.SetFkHips(ids, F(value), false, null, o);
                    case "chara.fk.hipsPosition": return ops.SetFkHips(ids, null, true, value == null ? (Vec3?)null : V3(value), o);
                }
                int k;
                if ((k = IndexOf(path, "chara.anime.params[")) >= 0)
                    return PerNode(ctx, f, ids, id =>
                    {
                        var cur = Raw(ctx.Model, id, "chara.anime.params") as float[] ?? new float[2];
                        var p = new[] { cur.Length > 0 ? cur[0] : 0f, cur.Length > 1 ? cur[1] : 0f };
                        if (k < 2) p[k] = F(value);
                        return ops.SetAnimeOption(new[] { id }, null, p, null, o);
                    });
                if ((k = IndexOf(path, "chara.state.clothes[")) >= 0) return ops.SetClothes(ids, k, I(value), true, o);
                if (Starts(path, "chara.state.accessories[")) return ops.SetAccessory(ids, AccessoryKey(path), B(value), o);
                if ((k = IndexOf(path, "chara.hands[")) >= 0) return ops.SetHands(ids, k == 0 ? I(value) : (int?)null, k == 1 ? I(value) : (int?)null, o);
                if ((k = IndexOf(path, "chara.correction[")) >= 0) return ops.SetCorrection(ids, new Dictionary<int, bool> { [k] = B(value) }, o);
                if (Starts(path, "chara.fk.groups."))
                {
                    if (!Enum.TryParse(path.Substring(16), true, out FkGroup g)) return OpResult.BadArgs(path, "an FK group");
                    return PerNode(ctx, f, ids, id => ops.SetFkGroup(id, g, B(value), o));
                }
                if (Starts(path, "chara.ik.groups."))
                {
                    if (!Enum.TryParse(path.Substring(16), true, out IkGroup g)) return OpResult.BadArgs(path, "an IK group");
                    return PerNode(ctx, f, ids, id => ops.SetIkGroups(id, new[] { g }, B(value), o));
                }
                if (Starts(path, "chara.fk.bones["))
                {
                    string bone = AccessoryKey(path);
                    return PerNode(ctx, f, ids, id => ops.SetFkBones(id, new Dictionary<string, Vec3>(StringComparer.Ordinal) { [bone] = V3(value) }, o));
                }
                if (Starts(path, "chara.ik.targets."))
                {
                    var parts = path.Split('.');
                    if (parts.Length != 5 || !Enum.TryParse(parts[3], false, out IkTargetId t)) return OpResult.BadArgs(path, "an IK target path");
                    bool rot = parts[4] == "rot";
                    return PerNode(ctx, f, ids, id =>
                    {
                        var v = U3(V3(value));
                        var edit = rot ? new IkTargetEdit(null, v) : new IkTargetEdit(v, null);
                        return ops.SetIkTargets(id, new Dictionary<IkTargetId, IkTargetEdit> { [t] = edit }, IkSpace.Local, o);
                    });
                }
                // the other `state` members and the face's tears / cheek (SetState: "Character state")
                string member = Starts(path, "chara.state.") ? path.Substring(12) : Is(path, "chara.face.tears") ? "tears" : Is(path, "chara.face.cheek") ? "cheek" : null;
                if (member != null && StudioOps.StatePath(member) != null)
                    return PerNode(ctx, f, ids, id => ops.SetState(new[] { id }, member, To(LeafType(ctx, id, path), value), o));
                return OpResult.Unsupported("no inspector write for " + path);
            }

            // ---- item
            if (Starts(path, "item."))
            {
                if (Is(path, "item.anime.pattern")) return ops.SetItemAnimation(ids, I(value), null, null, o);
                if (Is(path, "item.anime.speed")) return ops.SetItemAnimation(ids, null, F(value), null, o);
                if (f.Component >= 0 || ids.Count == 1)
                    return PerNode(ctx, f, ids, id => ops.SetItem(new[] { id }, new[] { (path, To(LeafType(ctx, id, path), value)) }, o));
                return ops.SetItem(ids, new[] { (path, To(LeafType(ctx, primary, path), value)) }, o);
            }

            // ---- light / camera
            if (Starts(path, "light."))
                return ops.SetLight(ids, new List<(string, object)> { (path, To(LeafType(ctx, primary, path), value)) }, o);
            if (Is(path, "camera.active")) return ops.SetActiveCamera(B(value) ? primary : null, o);
            if (Is(path, "camera.fov")) return ops.SetCameraFov(ids, value == null ? (float?)null : F(value), o);
            if (Starts(path, "route.")) return WriteRoute(ctx, path, ids, value, o);   // Phase 3.6 package R
            return OpResult.Unsupported("no inspector write for " + path);
        }

        /// <summary>
        /// Route rows (Phase 3.6 package R): route.loop / showLine / orient / color on every route
        /// (StudioOps.SetRoute); route.points[i].speed / ease / connection / link, .pos, .rot, .aid.pos on the primary route's
        /// point i (SetRoutePoints, SetRoutePointPose, SetRouteAid).
        /// </summary>
        static OpResult WriteRoute(UiContext ctx, string path, List<string> ids, object value, ExecuteOptions o)
        {
            var ops = ctx.Ops;
            string primary = ids[0];
            int i = IndexOf(path, "route.points[");
            if (i < 0)
            {
                string member = path.Substring(6);
                return ops.SetRoute(ids, new List<(string, object)> { (member, To(LeafType(ctx, primary, path), value)) }, o);
            }
            var st = ctx.Model.Find(primary)?.State as RouteNodeState;
            if (st == null || i >= st.Points.Count) return OpResult.NotFound("no point " + i + " in route " + primary, path);
            string pid = st.Points[i].Id;
            string rest = path.Substring(path.IndexOf("].", StringComparison.Ordinal) + 2);
            switch (rest)
            {
                case "pos": return ops.SetRoutePointPose(primary, pid, U3(V3(value)), null, o);
                case "rot": return ops.SetRoutePointPose(primary, pid, null, U3(V3(value)), o);
                case "aid.pos": return ops.SetRouteAid(primary, pid, U3(V3(value)), o);
                case "speed": case "ease": case "connection": case "link":
                    return ops.SetRoutePoints(primary, new[] { pid }, new List<(string, object)> { (rest, To(LeafType(ctx, primary, path), value)) }, o);
            }
            return OpResult.Unsupported("no inspector write for " + path);
        }

        /// <summary>The key inside `["…"]` of a map path (accessory id, bone name).</summary>
        static string AccessoryKey(string path)
        {
            int a = path.IndexOf("[\"", StringComparison.Ordinal), b = path.LastIndexOf("\"]", StringComparison.Ordinal);
            return a >= 0 && b > a ? path.Substring(a + 2, b - a - 2).Replace("\\\"", "\"").Replace("\\\\", "\\") : null;
        }

        // ------------------------------------------------------------ scene rows

        static OpResult WriteScene(UiContext ctx, FieldSpec f, string path, object value, ExecuteOptions o)
        {
            var ops = ctx.Ops;
            if (Is(path, "map.ref")) return ops.LoadMap(value as string, o);
            if (Is(path, "map.pos") || Is(path, "map.rot"))
            {
                var v = U3(V3(value));
                if (f.Wrap360) v = new Vector3(v.x % 360f, v.y % 360f, v.z % 360f);
                return Is(path, "map.pos") ? ops.SetMap(v, null, null, null, o) : ops.SetMap(null, v, null, null, o);
            }
            if (Is(path, "map.options")) return ops.SetMap(null, null, B(value), null, o);
            if (Is(path, "map.lights")) return ops.SetMap(null, null, null, B(value), o);
            if (Is(path, "effects.sky.enabled")) return ops.SetSky(B(value), null, o);
            if (Is(path, "effects.sky.pattern")) return ops.SetSky(null, value as string, o);
            if (Starts(path, "effects."))
                return ops.SetEffects(new List<(string, object)> { (path, To(LeafType(ctx, null, path), value)) }, o);
            int k;
            if ((k = IndexOf(path, "charaLight.rot[")) >= 0)
            {
                var cur = Raw(ctx.Model, null, "charaLight.rot") as float[] ?? new float[2];
                var r = new[] { cur.Length > 0 ? cur[0] : 0f, cur.Length > 1 ? cur[1] : 0f };
                if (k < 2) r[k] = F(value);
                return ops.SetCharaLight(new List<(string, object)> { ("charaLight.rot", r) }, o);
            }
            if (Starts(path, "charaLight.")) return ops.SetCharaLight(new List<(string, object)> { (path, To(LeafType(ctx, null, path), value)) }, o);
            if (Starts(path, "sound.")) return ops.SetSound(new List<(string, object)> { (path, To(LeafType(ctx, null, path), value)) }, o);
            return OpResult.Unsupported("no scene write for " + path);
        }

        // ------------------------------------------------------------ defaults

        /// <summary>
        /// The value of a row's default button for one target: the literal Default (double / double[] -> the row's component),
        /// or a per-item "@name" from ItemFeatures.Default. Null when there is none (or the item is not built).
        /// </summary>
        public static object DefaultOf(UiContext ctx, FieldSpec f, string id)
        {
            if (f.Default == null) return null;
            if (!(f.Default is string name) || !name.StartsWith("@", StringComparison.Ordinal))
                return f.Component >= 0 && f.Default is double[] arr && f.Component < arr.Length ? (object)arr[f.Component] : f.Default;
            var feat = InspectorGates.Features(ctx.Runtime, id);
            var d = feat?.Default;
            if (d == null) return null;
            int k = -1;
            int a = name.IndexOf('['), b = name.IndexOf(']');
            if (a > 0 && b > a) int.TryParse(name.Substring(a + 1, b - a - 1), out k);
            string head = a > 0 ? name.Substring(1, a - 1) : name.Substring(1);
            switch (head)
            {
                case "color": return k == 0 && d.ParticleColor != null ? d.ParticleColor.Value : k >= 0 && k < 3 ? (object)d.Color[k] : null;
                case "metallic": return k >= 0 && k < 3 ? (object)d.Metallic[k] : null;
                case "glossiness": return k >= 0 && k < 3 ? (object)d.Glossiness[k] : null;
                case "patternColor": return k >= 0 && k < 3 ? (object)d.PatternColor[k] : null;
                case "uv": return k >= 0 && k < 3 ? (f.Component >= 0 ? Component(d.PatternUv[k], f.Component) : (object)d.PatternUv[k]) : null;
                case "glass": return d.Glass;
                case "emission": return d.Emission?.Color;
                case "emission.power": return d.Emission?.Power;
                case "lightCancel": return d.LightCancel;
                case "panel.color": return d.Panel?.Color;
                case "panel.uv": return d.Panel == null ? null : f.Component >= 0 ? Component(d.Panel.Uv, f.Component) : (object)d.Panel.Uv;
                case "panel.clamp": return d.Panel?.Clamp;
                case "panel.rot": return d.Panel?.Rot;
            }
            return null;
        }

        /// <summary>The default button: each target gets its own default (per-item defaults differ), one entry.</summary>
        public static OpResult WriteDefault(UiContext ctx, FieldSpec f, string arg, IReadOnlyList<string> ids)
        {
            if (f.Default == null) return OpResult.NothingToDo();
            if (f.Target == FieldTarget.Scene) return Write(ctx, f, arg, null, DefaultOf(ctx, f, null));
            var targets = TargetsOf(ctx.Ops, f, ids);
            if (targets.Count == 0) return OpResult.NothingToDo();
            if (Is(f.Path, "item.emission.color"))
                return ctx.ShowResult(PerNode(ctx, f, targets, id =>
                {
                    var em = InspectorGates.Features(ctx.Runtime, id)?.Default?.Emission;
                    if (em == null) return OpResult.NothingToDo();
                    return ctx.Ops.SetItem(new[] { id }, new[] { ("item.emission.color", (object)em.Color), ("item.emission.intensity", (object)em.Intensity) }, O(f));
                }));
            return ctx.ShowResult(PerNode(ctx, f, targets, id =>
            {
                var v = DefaultOf(ctx, f, id);
                if (v == null) return OpResult.NothingToDo();
                string path = f.Expand(arg);
                return Route(ctx, f, path, new[] { id }, f.Component >= 0 ? Merge(ctx, f, path, id, v) : To(LeafType(ctx, id, path), v), O(f));
            }));
        }

        /// <summary>
        /// The item emission picker (items T5): the picked LDR colour is stored with alpha 1 and each item keeps
        /// its own HDR intensity unless <paramref name="intensity"/> is given.
        /// </summary>
        public static OpResult WriteEmission(UiContext ctx, FieldSpec f, IReadOnlyList<string> ids, Color4? ldr, float? intensity)
        {
            var targets = TargetsOf(ctx.Ops, f, ids);
            if (targets.Count == 0 || (ldr == null && intensity == null)) return OpResult.NothingToDo();
            var values = new List<(string, object)>();
            if (ldr != null) values.Add(("item.emission.color", new Color4(ldr.Value.R, ldr.Value.G, ldr.Value.B, 1f)));
            if (intensity != null) values.Add(("item.emission.intensity", intensity.Value));
            return ctx.ShowResult(ctx.Ops.SetItem(targets, values, O(f)));
        }

        // ------------------------------------------------------------ buttons

        /// <summary>
        /// Runs a button row: "ops.&lt;Method&gt;[:arg]" on the targets ({k} / {slot} … replaced by <paramref name="arg"/>). "ui.*"
        /// and action ids are the panel's (returns NothingToDo with Value = the action string so the caller can route it).
        /// </summary>
        public static OpResult RunButton(UiContext ctx, FieldSpec f, string arg, IReadOnlyList<string> ids) => ctx?.ShowResult(RunAction(ctx, f, arg, ids));

        /// <summary>As <see cref="RunButton"/> without showing the result (A's ButtonActions show it).</summary>
        public static OpResult RunAction(UiContext ctx, FieldSpec f, string arg, IReadOnlyList<string> ids)
        {
            var ops = ctx?.Ops;
            if (ops == null || f?.Action == null) return OpResult.NothingToDo();
            string action = f.Action.Replace("{k}", arg ?? "").Replace("{slot}", arg ?? "");
            if (!action.StartsWith("ops.", StringComparison.Ordinal))
            {
                var ui = OpResult.NothingToDo();
                ui.Value = action;
                return ui;
            }
            int colon = action.IndexOf(':');
            string m = colon > 0 ? action.Substring(4, colon - 4) : action.Substring(4);
            string a = colon > 0 ? action.Substring(colon + 1) : null;
            var o = O(f);
            var all = ids ?? Array.Empty<string>();
            var targets = TargetsOf(ops, f, all);
            string primary = targets.Count > 0 ? targets[0] : null;
            OpResult r;
            switch (m)
            {
                case "ResetTransform":
                    r = ops.ResetTransform(targets, a == "pos" ? TransformChannel.Pos : a == "rot" ? TransformChannel.Rot : a == "scale" ? TransformChannel.Scale : TransformChannel.All, o);
                    break;
                case "RestartAnimation":
                    r = ops.RestartAnimation(a == "all" ? AllOfKind(ctx, NodeKind.Chara) : targets);
                    break;
                case "SetAnimation":   // paste
                {
                    var row = InspectorGates.AnimeRow(ops, CopiedAnime);
                    r = row == null ? OpResult.NothingToDo() : ops.SetAnimation(targets, row, 0f, null, null, null, o);
                    break;
                }
                case "SetClothesAll": r = ops.SetClothesAll(targets, I(a), o); break;
                case "SetAccessoriesAll": r = ops.SetAccessoriesAll(targets, a == "true", o); break;
                case "FkFromAnimation": r = PerNode(ctx, f, targets, id => ops.FkFromAnimation(id, FkGroupsOf(a), false, o)); break;
                case "IkFromPose": r = PerNode(ctx, f, targets, id => ops.IkFromPose(id, null, false, o)); break;
                case "FkFromPose": r = PerNode(ctx, f, targets, id => ops.FkFromPose(id, null, false, o)); break;
                case "FkInit": r = PerNode(ctx, f, targets, id => FkInit(ops, id, a == "skirt" ? FkGroup.Skirt : FkGroup.Hair, o)); break;
                case "SetIkGroups": r = PerNode(ctx, f, targets, id => ops.SetIkGroups(id, AllIkGroups(), true, o)); break;
                case "IkFromAnimation": r = PerNode(ctx, f, targets, id => ops.IkFromAnimation(id, IkGroupsOf(a), false, o)); break;
                case "BakeIk": r = PerNode(ctx, f, targets, id => ops.BakeIk(id, o)); break;
                case "ConvertIk": r = PerNode(ctx, f, targets, id => ops.ConvertImportedIk(id, o)); break;
                case "UseCardState": r = ops.UseCardState(targets, null, o); break;
                case "SetItemOptionsAll": r = ItemOptionsAll(ctx, f, targets); break;
                case "RestartItemAnimation": r = ops.RestartItemAnimation(targets); break;
                case "SetCameraFov": r = ops.SetCameraFov(targets, null, o); break;
                case "SetActiveCamera": r = ops.SetActiveCamera(primary, o); break;
                case "CameraFromView": r = PerNode(ctx, f, targets, id => ops.UpdateCameraFromView(id, o)); break;
                case "ReleaseCamera": r = ops.SetActiveCamera(null, o); break;
                case "ResetEffects": r = ops.ResetEffects(new[] { a }, o); break;
                case "SaveCameraSlot": r = ops.SaveCameraSlot(I(a), o); break;
                case "LoadCameraSlot": r = ops.LoadCameraSlot(I(a), o); break;
                default:
                {
                    var ui = OpResult.NothingToDo();
                    ui.Value = action;   // LoadPose / SavePose need a file: the panel's flows
                    return ui;
                }
            }
            return r;
        }

        static List<string> AllOfKind(UiContext ctx, NodeKind kind)
        {
            var l = new List<string>();
            foreach (var n in ctx.Model.PreOrder()) if (n.Kind == kind) l.Add(n.Id);
            return l;
        }

        static IEnumerable<FkGroup> FkGroupsOf(string a)
        {
            if (a == null || a == "all") return null;
            return Enum.TryParse(a, true, out FkGroup g) ? new[] { g } : null;
        }

        static IEnumerable<IkGroup> IkGroupsOf(string a)
        {
            if (a == null || a == "all") return null;
            return Enum.TryParse(a, true, out IkGroup g) ? new[] { g } : null;
        }

        static IkGroup[] AllIkGroups() => (IkGroup[])Enum.GetValues(typeof(IkGroup));

        /// <summary>Vanilla FK init of hair / skirt (ChangeAmount.Reset: rotation 0 for every bone of the group).</summary>
        static OpResult FkInit(StudioOps ops, string id, FkGroup g, ExecuteOptions o)
        {
            var real = ops.Runtime.FindChara(id);
            if (real == null || real.State != RealizationState.Built) return OpResult.Unavailable("character " + id + " is not built", "key", real?.State.ToString() ?? "NotRealized");
            var table = FkDriver.TableFor(real);
            if (table == null) return OpResult.NothingToDo();
            var values = new Dictionary<string, Vec3>(StringComparer.Ordinal);
            foreach (var b in table.Of(g)) if (b?.Key != null) values[b.Key] = Vec3.Zero;
            return values.Count == 0 ? OpResult.NothingToDo() : ops.SetFkBones(id, values, o);
        }

        /// <summary>Options "all": all on unless every option of every target is on, then all off.</summary>
        static OpResult ItemOptionsAll(UiContext ctx, FieldSpec f, List<string> ids)
        {
            bool allOn = true;
            var plan = new List<(string id, string path, object value)>();
            foreach (var id in ids)
            {
                int n = InspectorGates.Features(ctx.Runtime, id)?.Options ?? 0;
                for (int k = 0; k < n; k++) if (!(Raw(ctx.Model, id, "item.options[" + k + "]") is bool b && b)) allOn = false;
            }
            foreach (var id in ids)
            {
                int n = InspectorGates.Features(ctx.Runtime, id)?.Options ?? 0;
                for (int k = 0; k < n; k++) plan.Add((id, "item.options[" + k + "]", !allOn));
            }
            return plan.Count == 0 ? OpResult.NothingToDo() : ctx.Ops.SetItem(plan, O(f));
        }

    }
}

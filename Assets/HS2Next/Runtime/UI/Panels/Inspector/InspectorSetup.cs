// Package I's hooks into A's widget factory: the gate evaluator (SchemaGates
// from the runtime), the per-item default resolver ("@name" -> ItemFeatures.Default), the choices of Ref / Node rows
// (LUTs, cubemaps, skies, BGM, maps, pattern items, scene nodes), the write routes of every inspector path to its StudioOps
// method (InspectorWrites.Route; replaces the factory's generic prefixes where the inspector needs gates, component merges or
// another op) and the button handlers of the schema's Action strings. Installed once by InspectorModule.Register.
using System;
using System.Collections.Generic;
using System.IO;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using UiField = HS2Next.UI.Core.FieldSpec;

namespace HS2Next.UI.Panels
{
    public static class InspectorSetup
    {
        static bool installed;

        /// <summary>Category of the item pattern list (pattern textures, category 348).</summary>
        public const int PatternCategory = 348;

        public static void Install()
        {
            if (installed) return;
            installed = true;
            FieldWidgets.GateEvaluator = (ctx, gate, targets) => InspectorGates.Holds(ctx?.Ops, gate, targets != null && targets.Count > 0 ? targets[0] : null);
            FieldWidgets.DefaultResolver = ResolveDefault;
            FieldWidgets.RefProvider = RefChoices;

            FieldWriter route = InspectorWrites.AsFieldWriter;
            // the factory's own prefixes too (the longest prefix wins, so "chara.state" would otherwise beat "chara")
            foreach (var p in new[] { "name", "transform", "transform.pos", "transform.rot", "transform.scale", "chara", "chara.state",
                         "chara.face.tears", "chara.face.cheek", "item", "light", "camera", "camera.fov", "route" })   // route: Phase 3.6 (InspectorWrites.WriteRoute; G found the rows unrouted)
                FieldWriters.Register(p, route);
            foreach (var p in new[] { "map", "map.pos", "map.rot", "map.option", "map.light", "effects", "charaLight", "sound" })
                FieldWriters.Register(p, route, true);

            foreach (var name in new[]
                     {
                         "ops.ResetTransform", "ops.RestartAnimation", "ops.SetAnimation", "ops.SetClothesAll", "ops.SetAccessoriesAll",
                         "ops.FkFromAnimation", "ops.IkFromPose", "ops.FkFromPose", "ops.FkInit", "ops.SetIkGroups", "ops.IkFromAnimation",
                         "ops.BakeIk", "ops.ConvertIk", "ops.UseCardState", "ops.SetItemOptionsAll", "ops.RestartItemAnimation",
                         "ops.SetCameraFov", "ops.SetActiveCamera", "ops.CameraFromView", "ops.ResetEffects", "ops.SaveCameraSlot",
                         "ops.LoadCameraSlot", "ops.ReleaseCamera",
                     })
            {
                string n = name;
                ButtonActions.Register(n, (ctx, targets, arg) =>
                {
                    string action = arg != null ? n + ":" + arg : n;
                    return InspectorWrites.RunAction(ctx, ButtonSpec(action), null, targets);
                });
            }
            ButtonActions.Register("ui.anime.copy", (ctx, targets, arg) => CopyAnime(ctx, targets));
            ButtonActions.Register("ops.SavePose", (ctx, targets, arg) => InspectorFlows.SavePose(ctx, targets));
            ButtonActions.Register("ops.LoadPose", (ctx, targets, arg) => InspectorFlows.BrowsePoses(ctx, targets));
            ButtonActions.Register("ui.pose.browse", (ctx, targets, arg) => InspectorFlows.BrowsePoses(ctx, targets));
            ButtonActions.Register("ui.chara.change", (ctx, targets, arg) => InspectorFlows.ChangeCharacter(ctx, targets));
            ButtonActions.Register("ui.chara.save", (ctx, targets, arg) => InspectorFlows.SaveCharacter(ctx, targets));
        }

        static readonly Dictionary<string, UiField> buttonSpecs = new Dictionary<string, UiField>(StringComparer.Ordinal);

        /// <summary>The schema row of a button action (its op label and gate), or a bare row.</summary>
        public static UiField ButtonSpec(string action)
        {
            if (buttonSpecs.TryGetValue(action, out var f)) return f;
            foreach (var x in InspectorSchemas.AllFields())
                if (x.Kind == FieldKind.Button && x.Action != null &&
                    (x.Action == action || (x.Action.Contains("{k}") && action.StartsWith(x.Action.Substring(0, x.Action.IndexOf("{k}", StringComparison.Ordinal)), StringComparison.Ordinal))))
                {
                    f = x.Clone();
                    f.Action = action;
                    break;
                }
            f ??= new UiField { Kind = FieldKind.Button, Action = action };
            buttonSpecs[action] = f;
            return f;
        }

        static OpResult CopyAnime(UiContext ctx, IReadOnlyList<string> targets)
        {
            var id = targets != null && targets.Count > 0 ? targets[0] : null;
            var st = id != null ? ctx.Model.Find(id)?.State as CharaNodeState : null;
            if (st?.Anime?.Ref == null) return OpResult.NothingToDo();
            var r = st.Anime.Ref;
            InspectorWrites.CopiedAnime = new AnimeRef(r.Source, r.Group, r.Category, r.No);
            ctx.Toasts?.Show(L10n.Get("inspector.chara.anime.copied"), HS2Next.UI.Widgets.ToastKind.Info);
            return OpResult.Success();
        }

        /// <summary>FieldWidgets.DefaultResolver: "@name" for one target (vectors as double[] so the factory picks the component).</summary>
        public static object ResolveDefault(UiContext ctx, string name, string id)
        {
            var v = InspectorWrites.DefaultOf(ctx, new UiField { Default = name }, id);
            switch (v)
            {
                case Vec4 q: return new double[] { q.X, q.Y, q.Z, q.W };
                case Color4 c: return new double[] { c.R, c.G, c.B, c.A };
                case float f: return (double)f;
                case bool b: return b;
            }
            return v;
        }

        // ------------------------------------------------------------ choices of Ref / Node rows

        public static IReadOnlyList<(string value, string label)> RefChoices(UiContext ctx, UiField f)
        {
            var l = new List<(string, string)>();
            var db = ctx?.App?.Db;
            string p = f?.Path ?? "";
            if (f != null && f.Kind == FieldKind.Node)
            {
                l.Add((null, L10n.Get("common.none")));
                foreach (var n in ctx.Model.PreOrder()) l.Add((n.Id, n.Label));
                return l;
            }
            if (db == null) return l;
            void Add<T>(IEnumerable<T> rows, Func<T, string> name) where T : HS2Next.Content.Studio.StudioRecord
            {
                foreach (var r in rows) if (r.Visible) l.Add((r.Key.ToString(), name(r)));
            }
            if (p == "effects.colorGrading.lut") { l.Add((null, L10n.Get("common.none"))); Add(db.Filters.All, r => r.Name); }
            else if (p == "effects.reflectionProbe.cubemap") { l.Add((null, L10n.Get("common.none"))); Add(db.Probes.All, r => r.Name); }
            else if (p == "effects.sky.pattern") Add(db.Skies.All, r => r.Name);
            else if (p == "sound.bgm.ref") { l.Add((null, L10n.Get("common.none"))); Add(db.Bgm.All, r => r.Name); }
            else if (p == "map.ref") { l.Add((null, L10n.Get("inspector.scene.map.none"))); Add(db.Maps.All, r => r.Name); }
            else if (p.StartsWith("item.colors[", StringComparison.Ordinal) && p.EndsWith(".pattern.item", StringComparison.Ordinal))
            {
                l.Add((null, L10n.Get("common.none")));
                var cat = ctx.App.Catalog;
                if (cat != null) foreach (var e in cat.EntriesOf(PatternCategory)) l.Add((e.Key.ToString(), e["Name"] ?? e.Key.ToString()));
            }
            return l;
        }
    }
}

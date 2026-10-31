// Write routing of the widget factory (every document change from the UI goes
// through StudioOps): a FieldWrite (targets, expanded path, model-typed value, options) is routed to the StudioOps method
// owning that path by the longest registered prefix. Built in: transform.pos / rot / scale (SetTransform), name
// (Rename), item.* (SetItem), light.* (SetLight), camera.fov (SetCameraFov), chara.state.* and chara.face.tears / cheek
// (SetState); scene paths effects.* (SetEffects), charaLight.* (SetCharaLight), sound.* (SetSound), map.pos / map.rot
// (SetMap); Option rows write the app options (view state). Package I registers the character rows it edits through
// other ops (face, anime, hands, look, correction, FK / IK) with Register(prefix, writer), or passes its own writer in
// FieldContext. Buttons ("ops.<Method>[:arg]", "ui.<thing>", or an action id) go through ButtonActions.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using UnityEngine;

namespace HS2Next.UI.Binding
{
    public sealed class FieldWrite
    {
        public FieldSpec Spec;
        /// <summary>The expanded path (templates filled).</summary>
        public string Path;
        /// <summary>Target node ids (a single null = the scene).</summary>
        public IReadOnlyList<string> Targets;
        /// <summary>The value in the model's type (FieldValues.ToModel).</summary>
        public object Value;
        public ExecuteOptions Options;
        public bool IsScene => Targets.Count == 1 && Targets[0] == null;
    }

    public delegate OpResult FieldWriter(UiContext ctx, FieldWrite w);

    public static class FieldWriters
    {
        static readonly List<(string prefix, bool scene, FieldWriter w)> routes = new List<(string, bool, FieldWriter)>();
        static bool builtIn;

        /// <summary>Registers a writer for node paths (or scene paths) starting with <paramref name="prefix"/>; later and longer prefixes win.</summary>
        public static void Register(string prefix, FieldWriter writer, bool scene = false)
        {
            routes.RemoveAll(r => r.prefix == prefix && r.scene == scene);
            routes.Add((prefix, scene, writer));
        }

        public static FieldWriter Find(string path, bool scene)
        {
            EnsureBuiltIn();
            FieldWriter best = null;
            int len = -1;
            for (int i = routes.Count - 1; i >= 0; i--)
            {
                var r = routes[i];
                if (r.scene != scene || !FieldPath.IsPrefixOf(r.prefix, path) || r.prefix.Length <= len) continue;
                best = r.w; len = r.prefix.Length;
            }
            return best;
        }

        /// <summary>Routes a write (Option rows: options; else the registered writer; none → unsupported, shown as a toast).</summary>
        public static OpResult Write(UiContext ctx, FieldWrite w)
        {
            if (w.Spec != null && w.Spec.Target == FieldTarget.Option)
            {
                ctx.Options.Set(w.Path, w.Value);
                return OpResult.Success();
            }
            var f = Find(w.Path, w.IsScene);
            if (f == null) return OpResult.Unsupported("no operation writes " + (w.IsScene ? "scene." : "") + w.Path);
            return f(ctx, w);
        }

        static Vector3 V3(object v) => FieldValues.ToVector3(v) ?? Vector3.zero;
        static List<(string, object)> One(FieldWrite w) => new List<(string, object)> { (w.Path, w.Value) };

        static void EnsureBuiltIn()
        {
            if (builtIn) return;
            builtIn = true;
            var keep = new List<(string, bool, FieldWriter)>(routes);
            routes.Clear();
            Register("transform.pos", (c, w) => c.Ops.SetTransform(w.Targets, V3(w.Value), null, null, w.Options));
            Register("transform.rot", (c, w) => c.Ops.SetTransform(w.Targets, null, V3(w.Value), null, w.Options));
            Register("transform.scale", (c, w) => c.Ops.SetTransform(w.Targets, null, null, V3(w.Value), w.Options));
            Register("name", (c, w) =>
            {
                OpResult last = OpResult.NothingToDo();
                using (var e = c.BeginEdit(OpLabels.Rename))
                {
                    foreach (var id in w.Targets) { last = c.Ops.Rename(id, string.IsNullOrEmpty(w.Value as string) ? null : (string)w.Value, w.Options); if (!last.Ok) return last; }
                    return e.Commit();
                }
            });
            Register("item", (c, w) => c.Ops.SetItem(w.Targets, One(w), w.Options));
            Register("light", (c, w) => c.Ops.SetLight(w.Targets, One(w), w.Options));
            Register("camera.fov", (c, w) => c.Ops.SetCameraFov(w.Targets, w.Value is float f ? f : (float?)null, w.Options));
            Register("chara.state", (c, w) => c.Ops.SetState(w.Targets, w.Path, w.Value, w.Options));
            Register("chara.face.tears", (c, w) => c.Ops.SetState(w.Targets, "tears", w.Value, w.Options));
            Register("chara.face.cheek", (c, w) => c.Ops.SetState(w.Targets, "cheek", w.Value, w.Options));
            Register("effects", (c, w) => c.Ops.SetEffects(One(w), w.Options), true);
            Register("charaLight", (c, w) => c.Ops.SetCharaLight(One(w), w.Options), true);
            Register("sound", (c, w) => c.Ops.SetSound(One(w), w.Options), true);
            Register("map.pos", (c, w) => c.Ops.SetMap(V3(w.Value), null, null, null, w.Options), true);
            Register("map.rot", (c, w) => c.Ops.SetMap(null, V3(w.Value), null, null, w.Options), true);
            Register("map.option", (c, w) => c.Ops.SetMap(null, null, w.Value as bool?, null, w.Options), true);
            Register("map.light", (c, w) => c.Ops.SetMap(null, null, null, w.Value as bool?, w.Options), true);
            foreach (var r in keep) routes.Add(r);
        }
    }

    /// <summary>A button row's action: (ctx, targets, argument after ':').</summary>
    public delegate OpResult ButtonAction(UiContext ctx, IReadOnlyList<string> targets, string arg);

    /// <summary>Handlers of FieldSpec.Action ("ops.ResetTransform:pos", "ui.chara.change", or an ActionIds id).</summary>
    public static class ButtonActions
    {
        static readonly Dictionary<string, ButtonAction> handlers = new Dictionary<string, ButtonAction>(StringComparer.Ordinal);
        static bool builtIn;

        public static void Register(string name, ButtonAction a) => handlers[name] = a;

        public static OpResult Run(UiContext ctx, string action, IReadOnlyList<string> targets)
        {
            EnsureBuiltIn();
            if (string.IsNullOrEmpty(action)) return OpResult.NothingToDo();
            string name = action, arg = null;
            int colon = action.IndexOf(':');
            if (colon > 0) { name = action.Substring(0, colon); arg = action.Substring(colon + 1); }
            if (handlers.TryGetValue(name, out var h)) return h(ctx, targets, arg);
            if (ActionManifest.Contains(action))
            {
                var st = ctx.Registry.Run(action, "inspector");
                return st == ActionRunStatus.Ran ? OpResult.Success() : OpResult.Unsupported("action " + action + ": " + st);
            }
            return OpResult.Unsupported("no handler for button " + action);
        }

        static void EnsureBuiltIn()
        {
            if (builtIn) return;
            builtIn = true;
            void Add(string n, ButtonAction a) { if (!handlers.ContainsKey(n)) handlers[n] = a; }
            Add("ops.ResetTransform", (c, t, a) => c.Ops.ResetTransform(t, a == "pos" ? TransformChannel.Pos : a == "rot" ? TransformChannel.Rot : a == "scale" ? TransformChannel.Scale : TransformChannel.All, UiContext.User()));
            Add("ops.SetClothesAll", (c, t, a) => c.Ops.SetClothesAll(t, int.TryParse(a, out var s) ? s : 0, UiContext.User()));
            Add("ops.SetAccessoriesAll", (c, t, a) => c.Ops.SetAccessoriesAll(t, a != "false", UiContext.User()));
            Add("ops.ResetEffects", (c, t, a) => c.Ops.ResetEffects(a == null ? null : new[] { a }, UiContext.User()));
            Add("ops.SaveCameraSlot", (c, t, a) => c.Ops.SaveCameraSlot(int.TryParse(a, out var i) ? i : 0, UiContext.User()));
            Add("ops.LoadCameraSlot", (c, t, a) => c.Ops.LoadCameraSlot(int.TryParse(a, out var i) ? i : 0, UiContext.User()));
            Add("ops.RestartAnimation", (c, t, a) => c.Ops.RestartAnimation(t));
            Add("ops.RestartItemAnimation", (c, t, a) => c.Ops.RestartItemAnimation(t));
            Add("ops.MoveToPivot", (c, t, a) => c.Ops.MoveToPivot(t, UiContext.User()));
        }
    }
}

// StudioOps, item area: item-section sets on one or
// several items (item.set's write logic: all-or-nothing, the label of the first path, enabling item FK first writes the
// missing bone entries, the capture rule), the per-item defaults buttons, the animation pattern / speed /
// time (item.anime) and restart (not recorded).
using System;
using System.Collections.Generic;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>What <see cref="StudioOps.ResetItemDefaults"/> resets (the item inspector's "Defaults" rows).</summary>
    public enum ItemDefaultsPart { Colors, Pattern, Glass, Emission, LightCancel }

    public sealed partial class StudioOps
    {
        List<TreeNode> Items(IEnumerable<string> ids, string arg, out OpResult fail) => OfKind(ids, NodeKind.Item, "item", arg, out fail);

        /// <summary>The canonical `item.` path of a name with or without the section prefix, else null.</summary>
        public static string ItemPath(string name)
        {
            if (name == null) return null;
            string rel = name.StartsWith("item.", StringComparison.Ordinal) ? name.Substring(5) : name;
            return FieldPath.Canonical("item." + rel);
        }

        /// <summary>The refusal of a path under <c>item.materialOverrides</c>: the array has one door, the material commands.</summary>
        public const string ItemMaterialRefusal = "use item.materials.set";

        /// <summary>True for <c>item.materialOverrides</c> and any path under it.</summary>
        public static bool IsItemMaterialPath(string path) => path != null && FieldPath.IsPrefixOf("item.materialOverrides", path);

        /// <summary>
        /// Sets item-section values (canonical `item.` paths with their typed values, per node) as one entry: the label is the
        /// caller's or the first path's (Labels.ForItemPath). Changed = the paths that changed on any item (per-item order),
        /// NoOp = the paths that changed on none; PerTarget = each item's lists.
        /// </summary>
        public OpResult SetItem(IReadOnlyList<(string id, string path, object value)> plan, ExecuteOptions o = default)
        {
            if (plan == null || plan.Count == 0) return OpResult.BadArgs("set", "an object", "set names no field");
            var ids = new List<string>();
            foreach (var (id, _, _) in plan) if (!ids.Contains(id)) ids.Add(id);
            var nodes = Items(ids, ids.Count > 1 ? "keys" : "key", out var fail);
            if (nodes == null) return fail;
            foreach (var (_, path, _) in plan)
                if (path == null || !path.StartsWith("item.", StringComparison.Ordinal) || FieldPath.Canonical(path) != path)
                    return OpResult.BadArgs("set." + path, "an item-section path", "\"" + path + "\" is not a field path");
            foreach (var (_, path, _) in plan)
                if (IsItemMaterialPath(path)) return OpResult.BadArgs("set." + path.Substring(5), "an item-section path", ItemMaterialRefusal);
            string label = Labels.ForItemPath(plan[0].path.Substring(5));
            var perKey = new Dictionary<string, (List<string> ch, List<string> no)>(StringComparer.Ordinal);
            foreach (var n in nodes) perKey[n.Id] = (new List<string>(), new List<string>());
            rt.ApplyNow();
            var tx = Open(label, o, out fail);
            if (tx == null) return fail;
            var r = OpResult.NothingToDo();
            using (tx)
            {
                foreach (var (id, path, value) in plan)
                {
                    var node = Model.Find(id);
                    // enabling item FK first writes the missing bone entries (the capture rule)
                    if (path == "item.fk.enabled" && value is bool on && on)
                    {
                        var e = CaptureMissingItemFk(tx, node);
                        if (e != null) return e;
                    }
                    var s = tx.Set(node.Id, path, value);
                    if (s.Status == CommandStatus.Error) return OpResult.From(s, "set." + path.Substring(5));
                    var pk = perKey[node.Id];
                    if (s.Status == CommandStatus.NoOp) pk.no.Add(path); else pk.ch.Add(path);
                }
                r = Commit(tx, r);
                if (!r.Ok) return r;
            }
            rt.ApplyNow();
            foreach (var kv in perKey)
            {
                foreach (var p in kv.Value.ch) if (!r.Changed.Contains(p)) r.Changed.Add(p);
                r.PerTarget[kv.Key] = kv.Value;
            }
            foreach (var (_, path, _) in plan)
                if (!r.Changed.Contains(path) && !r.NoOp.Contains(path)) r.NoOp.Add(path);
            return r;
        }

        /// <summary>The same values on every item (UI): (path, value) pairs with or without the `item.` prefix.</summary>
        public OpResult SetItem(IEnumerable<string> ids, IReadOnlyList<(string path, object value)> values, ExecuteOptions o = default)
        {
            var plan = new List<(string, string, object)>();
            foreach (var (path, value) in values ?? Array.Empty<(string, object)>())
            {
                string full = ItemPath(path);
                if (full == null) return OpResult.BadArgs("set." + path, "an item-section path", "\"" + path + "\" is not a field path");
                foreach (var id in ids ?? Array.Empty<string>()) plan.Add((id, full, value));
            }
            return SetItem(plan, o);
        }

        OpResult CaptureMissingItemFk(Transaction tx, TreeNode node)
        {
            var real = rt.FindItem(node.Id);
            var st = node.State as ItemNodeState;
            if (real == null || real.State != RealizationState.Built || !real.Instance || real.Features == null || st == null) return null;
            foreach (var name in real.Features.FkBones)
            {
                if (st.Fk.Bones.ContainsKey(name)) continue;
                Vector3? e = null;
                if (ItemMotionHooks.FkBase != null)
                {
                    try { e = ItemMotionHooks.FkBase(real, name); } catch (Exception) { e = null; }
                }
                if (e == null)
                {
                    var b = ItemDefaults.FindBone(real.Instance.transform, name);
                    if (!b) continue;
                    e = b.localEulerAngles;
                }
                var s = tx.Set(node.Id, "item.fk.bones[" + FieldPath.Quote(name) + "]", UnityObjects.V(e.Value));
                if (s.Status == CommandStatus.Error) return OpResult.From(s, "set.fk.enabled");
            }
            return null;
        }

        /// <summary>
        /// The "Defaults" buttons: each item's own prefab defaults of one part (colours, pattern, glass, emission, light cancel), one entry labelled as the part's edits.
        /// </summary>
        public OpResult ResetItemDefaults(IEnumerable<string> ids, ItemDefaultsPart part, ExecuteOptions o = default)
        {
            var nodes = Items(ids, "keys", out var fail);
            if (nodes == null) return fail;
            rt.ApplyNow();
            string label = part == ItemDefaultsPart.Pattern ? Labels.ItemPattern : part == ItemDefaultsPart.Emission || part == ItemDefaultsPart.LightCancel ? Labels.ItemEmission
                : Labels.ItemColour;
            var plan = new List<(string, string, object)>();
            foreach (var n in nodes)
            {
                var real = rt.FindItem(n.Id);
                var d = real != null && real.State == RealizationState.Built ? real.Features?.Default : null;
                if (d == null) return OpResult.Unavailable("item " + n.Id + " is not built: its defaults are not known", "keys", real?.State.ToString() ?? "NotRealized");
                switch (part)
                {
                    case ItemDefaultsPart.Colors:
                        for (int i = 0; i < 3 && i < d.Color.Length; i++)
                        {
                            plan.Add((n.Id, "item.colors[" + i + "].main", d.Color[i]));
                            plan.Add((n.Id, "item.colors[" + i + "].metallic", d.Metallic[i]));
                            plan.Add((n.Id, "item.colors[" + i + "].glossiness", d.Glossiness[i]));
                        }
                        break;
                    case ItemDefaultsPart.Pattern:
                        for (int i = 0; i < 3 && i < d.PatternColor.Length; i++)
                        {
                            plan.Add((n.Id, "item.colors[" + i + "].pattern.color", d.PatternColor[i]));
                            plan.Add((n.Id, "item.colors[" + i + "].pattern.uv", d.PatternUv[i]));
                            plan.Add((n.Id, "item.colors[" + i + "].pattern.clamp", d.PatternClamp[i]));
                            plan.Add((n.Id, "item.colors[" + i + "].pattern.rot", d.PatternRot[i]));
                        }
                        break;
                    case ItemDefaultsPart.Glass: plan.Add((n.Id, "item.colors[3].main", d.Glass)); break;
                    case ItemDefaultsPart.Emission:
                        plan.Add((n.Id, "item.emission.color", d.Emission.Color));
                        plan.Add((n.Id, "item.emission.power", d.Emission.Power));
                        break;
                    case ItemDefaultsPart.LightCancel: plan.Add((n.Id, "item.lightCancel", d.LightCancel)); break;
                }
            }
            if (plan.Count == 0) return OpResult.NothingToDo();
            var tx = Open(label, o, out fail);
            if (tx == null) return fail;
            var r = OpResult.NothingToDo();
            using (tx)
            {
                foreach (var (id, path, value) in plan)
                {
                    var s = tx.Set(id, path, value);
                    if (s.Status == CommandStatus.Error) return OpResult.From(s, part.ToString());
                    r.Record(id, path, s.Status);
                }
                r = Commit(tx, r);
            }
            rt.ApplyNow();
            return r;
        }

        /// <summary>Animation pattern (≥ 0), speed (≥ 0) and time of every item (item.anime; one entry "Item animation").
        /// A warning when an item has no driven Animator (values stored only).</summary>
        public OpResult SetItemAnimation(IEnumerable<string> ids, int? pattern, float? speed, float? time = null, ExecuteOptions o = default)
        {
            var nodes = Items(ids, "key", out var fail);
            if (nodes == null) return fail;
            if (pattern != null && pattern < 0) return OpResult.BadArgs("pattern", "an index ≥ 0");
            if (speed != null && (!Finite(speed.Value) || speed.Value < 0f)) return OpResult.BadArgs("speed", "a finite number ≥ 0");
            if (time != null && !Finite(time.Value)) return OpResult.BadArgs("time", "a finite number");
            var r = OpResult.NothingToDo();
            if (pattern != null || speed != null || time != null)
            {
                rt.ApplyNow();
                var tx = Open(Labels.ItemAnimation, o, out fail);
                if (tx == null) return fail;
                using (tx)
                {
                    foreach (var n in nodes)
                        foreach (var (has, path, value, arg) in new (bool, string, object, string)[]
                                 { (pattern != null, "item.anime.pattern", pattern ?? 0, "pattern"), (speed != null, "item.anime.speed", speed ?? 0f, "speed"), (time != null, "item.anime.time", time ?? 0f, "time") })
                        {
                            if (!has) continue;
                            var s = tx.Set(n.Id, path, value);
                            if (s.Status == CommandStatus.Error) return OpResult.From(s, arg);
                            r.Record(n.Id, path, s.Status);
                        }
                    r = Commit(tx, r);
                    if (!r.Ok) return r;
                }
            }
            rt.ApplyNow();
            if (pattern != null || speed != null || time != null)
                foreach (var n in nodes)
                {
                    var real = rt.FindItem(n.Id);
                    var m = real != null && real.State == RealizationState.Built && !real.InGraveyard ? ItemMotion.Of(real) : null;
                    if (m?.Animation == null) r.Warnings.Add("the item has no driven Animator: the values are stored only");
                }
            return r;
        }

        /// <summary>Restarts the items' animations (runtime only, not recorded); a warning for an item without a driven Animator.</summary>
        public OpResult RestartItemAnimation(IEnumerable<string> ids)
        {
            rt.ApplyNow();
            var r = OpResult.NothingToDo();
            foreach (var id in ids ?? Array.Empty<string>())
            {
                var real = rt.FindItem(id);
                var m = real != null && real.State == RealizationState.Built && !real.InGraveyard ? ItemMotion.Of(real) : null;
                if (m?.Animation != null) { m.Restart(); r.Changed.Add(id); }
                else r.Warnings.Add("restart: the item has no driven Animator");
            }
            return r.Changed.Count > 0 ? r.With(CommandResult.Success(null)) : r;
        }
    }
}

// Field commands of the bus: a property set on a node or the scene, list
// insert/remove/move and map key removal. They hold deep copies of old and new values, so the
// model never shares an object with history; a set whose value equals the current one changes nothing (the
// bus then answers NoOp). Undo of a set that added a map key removes the key again.
using System;
using System.Collections;
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    /// <summary>Base of the commands addressed by (target, field path).</summary>
    public abstract class FieldCommand : ISceneCommand
    {
        protected readonly string parseError;

        protected FieldCommand(string nodeId, string path, string label)
        {
            NodeId = nodeId;
            if (FieldPath.TryParse(path, out var p, out string error)) Path = p;
            else parseError = "bad field path \"" + path + "\": " + error;
            PathText = Path?.Text ?? path;
            Label = label;
        }

        /// <summary>The node, or null for the scene.</summary>
        public string NodeId { get; }
        /// <summary>The parsed path, or null when the text did not parse (Validate then fails with bad_args).</summary>
        public FieldPath Path { get; }
        public string PathText { get; }
        public string Label { get; protected set; }
        /// <summary>True when the path is view state (`ui.*`, scene `camera.current`), applied without history.</summary>
        public bool IsViewState => Path != null && FieldAccessor.IsViewPath(NodeId, Path.Text);

        public abstract void Validate(SceneModel m);
        public abstract void Apply(SceneModel m, ChangeSink s);
        public abstract void Revert(SceneModel m, ChangeSink s);
        public virtual bool TryMerge(ISceneCommand next) => false;
        public abstract long EstimatedBytes { get; }
        public IEnumerable<TreeNode> HeldNodes => Array.Empty<TreeNode>();

        internal FieldAccessor.Slot Slot(SceneModel m)
        {
            if (Path == null) throw new CommandException(CommandCodes.BadArgs, parseError);
            return FieldAccessor.ResolveForWrite(m, NodeId, Path);   // every field command writes (readable-only paths refused)
        }

        internal FieldSchema.Context Ctx(SceneModel m, FieldAccessor.Slot slot) => new FieldSchema.Context { Model = m, Node = slot.Node, Path = PathText };

        protected void Changed(ChangeSink s, string path)
        {
            if (NodeId == null) s.SceneChanged(path);
            else s.NodeChanged(NodeId, path);
        }

        internal static bool SameTarget(FieldCommand a, FieldCommand b) =>
            string.Equals(a.NodeId, b.NodeId, StringComparison.Ordinal) && string.Equals(a.PathText, b.PathText, StringComparison.Ordinal);

        /// <summary>The list a list command works on.</summary>
        internal IList ListOf(SceneModel m, out FieldAccessor.Slot slot)
        {
            slot = Slot(m);
            if (!(slot.Type.IsGenericType && slot.Type.GetGenericTypeDefinition() == typeof(List<>)))
                throw new CommandException(CommandCodes.BadArgs, PathText + " is not a list");
            if (slot.Rule.WriteOrder != null)
                throw new CommandException(CommandCodes.BadArgs, PathText + " is kept in its saved order: set the whole list instead of inserting, removing or moving");
            return (IList)slot.Get() ?? throw new CommandException(CommandCodes.NotFound, PathText + " is null");
        }
    }

    /// <summary>Sets one field: `old value (or Absent)` → `new value`; undo writes the old value back.</summary>
    public sealed class SetPropertyCommand : FieldCommand
    {
        readonly object raw;
        object newValue, oldValue;
        bool coerced, sameOnly, changed;

        public SetPropertyCommand(string nodeId, string path, object value, string label = null) : base(nodeId, path, label ?? "Set " + path)
        {
            raw = value;
        }

        /// <summary>The value written (a checked copy once validated).</summary>
        public object NewValue => coerced ? newValue : raw;
        /// <summary>The value before the last Apply (or <see cref="FieldValue.Absent"/>).</summary>
        public object OldValue => oldValue;
        internal bool NetNoOp => !changed || FieldSchema.Same(oldValue, newValue);

        public override void Validate(SceneModel m)
        {
            var slot = Slot(m);
            if (slot.Rule.ReadOnly) throw new CommandException(CommandCodes.BadArgs, PathText + " is not settable");
            if (slot.Step.Kind == FieldAccessor.StepKind.KindSection) throw new CommandException(CommandCodes.BadArgs, "a kind section is not set as a whole");
            if (coerced) { if (!Same(slot, newValue)) CheckContext(m, slot, newValue); return; }
            object v;
            try { v = FieldSchema.Coerce(slot.Type, slot.Rule, raw, Ctx(m, slot)); }
            catch (CommandException) when (!FieldValue.IsAbsent(raw) && Same(slot, raw))
            {
                // Setting the stored value again (e.g. an unknown enum string kept from a file) changes nothing.
                sameOnly = true;
                return;
            }
            newValue = v;
            coerced = true;
            // A value equal to the stored one changes nothing (a file may hold what new writes may not, e.g. a scale on a light).
            if (!Same(slot, v)) CheckContext(m, slot, v);
        }

        static bool Same(FieldAccessor.Slot slot, object value)
        {
            try { return FieldSchema.Same(slot.Get(), value); }
            catch (Exception) { return false; }
        }

        // Rules that depend on where the value goes (node kind, list neighbours).
        void CheckContext(SceneModel m, FieldAccessor.Slot slot, object v)
        {
            var node = slot.Node;
            if (node != null && !NodeRules.HasScale(node.Kind))
            {
                if (slot.Rule.ScaleChannel && !NodeRules.IsUnitScale((Vec3)v))
                    throw new CommandException(CommandCodes.BadArgs, "a " + node.Doc.Kind + " node has no scale channel: transform.scale stays (1, 1, 1)");
                if (v is NodeTransform t && !NodeRules.IsUnitScale(t.Scale))
                    throw new CommandException(CommandCodes.BadArgs, "a " + node.Doc.Kind + " node has no scale channel: transform.scale stays (1, 1, 1)");
            }
            if (v is NodeUi ui && node != null && node.Kind != NodeKind.Chara && (ui.ExpandedGroups.Count > 0 || ui.ExpandedPoints.Count > 0))
                throw new CommandException(CommandCodes.WrongKind, "only character nodes have attach groups");
            if (slot.Owner is OverlayEntry oe && slot.Step.Kind == FieldAccessor.StepKind.Field && LeavesEmpty(oe, slot.Step.Field.Name, v))
                throw new CommandException(CommandCodes.BadArgs, PathText + ": this leaves the overlay entry without image and clearTarget, which is saved as null (set the entry to null instead)");
            if (slot.Step.Kind == FieldAccessor.StepKind.AccessoryById && v is AccessoryEntry ae && !string.Equals(ae.Id, slot.Step.Key, StringComparison.Ordinal))
                throw new CommandException(CommandCodes.Unsupported, PathText + ": an accessory id never changes (the value's id must be " + slot.Step.Key + ")");
            if (v is RoutePoint rp && slot.Step.Kind == FieldAccessor.StepKind.ListIndex)
            {
                var list = (IList)slot.Owner;
                for (int i = 0; i < list.Count; i++)
                    if (i != slot.Step.Index && ((RoutePoint)list[i]).Id == rp.Id)
                        throw new CommandException(CommandCodes.BadArgs, "duplicate route point id " + rp.Id);
            }
        }

        // The model keeps the save form of overlay entries (an entry without image and clearTarget is null).
        static bool LeavesEmpty(OverlayEntry e, string field, object v)
        {
            string image = field == nameof(OverlayEntry.Image) ? v as string : e.Image;
            bool clear = field == nameof(OverlayEntry.ClearTarget) ? v is bool b && b : e.ClearTarget;
            return image == null && !clear && (e.Unknown == null || e.Unknown.IsEmpty);
        }

        public override void Apply(SceneModel m, ChangeSink s)
        {
            changed = false;
            if (sameOnly) return;
            var slot = Slot(m);
            var cur = slot.Get();
            if (FieldSchema.Same(cur, newValue)) return;
            oldValue = FieldSchema.Copy(cur);
            Write(m, slot, FieldSchema.Copy(newValue));
            changed = true;
            Changed(s, PathText);
        }

        public override void Revert(SceneModel m, ChangeSink s)
        {
            if (!changed) return;
            var slot = Slot(m);
            Write(m, slot, FieldSchema.Copy(oldValue));
            Changed(s, PathText);
        }

        void Write(SceneModel m, FieldAccessor.Slot slot, object v)
        {
            slot.Set(v);
            var node = slot.Node;
            if (node == null) return;
            if (v is NodeUi ui) ui.IsChara = node.Kind == NodeKind.Chara;
            if (Path.Root == "visible") m.UpdateVisibility(node);
        }

        /// <summary>Folds a later set of the same (target, path) into this one: first old → last new.</summary>
        public override bool TryMerge(ISceneCommand next)
        {
            if (!(next is SetPropertyCommand n) || !SameTarget(this, n) || !n.coerced) return false;
            if (!changed && !n.changed) return false;
            if (!changed) { oldValue = n.oldValue; changed = true; }
            newValue = n.newValue;
            coerced = true;
            sameOnly = false;
            return true;
        }

        public override long EstimatedBytes => 64 + FieldSchema.SizeOf(oldValue) + FieldSchema.SizeOf(NewValue);
    }

    /// <summary>Inserts an element into a list field (route points, voice list, camera slots, item options, ui lists).</summary>
    public sealed class ListInsertCommand : FieldCommand
    {
        readonly object raw;
        object element;
        readonly int index;

        public ListInsertCommand(string nodeId, string listPath, int index, object element, string label = null) : base(nodeId, listPath, label ?? "Insert")
        {
            this.index = index;
            raw = element;
        }

        public int Index => index;

        public override void Validate(SceneModel m)
        {
            var list = ListOf(m, out var slot);
            if (index < 0 || index > list.Count) throw new CommandException(CommandCodes.BadArgs, PathText + ": insert index " + index + " outside 0.." + list.Count);
            var et = slot.Type.GetGenericArguments()[0];
            var e = FieldSchema.Coerce(et, FieldSchema.ElementRule(slot.Rule), raw, Ctx(m, slot));
            if (slot.Rule.RoutePoints)
            {
                var all = new List<object>();
                foreach (var x in list) all.Add(x);
                all.Insert(index, e);
                FieldSchema.CheckRoutePoints(Ctx(m, slot), all, -1);
            }
            if (slot.Rule.AccessoryIds) FieldSchema.CheckAccessoryIds(Ctx(m, slot), new[] { e }, list);
            element = e;
        }

        public override void Apply(SceneModel m, ChangeSink s)
        {
            var list = ListOf(m, out _);
            list.Insert(index, FieldSchema.Copy(element));
            Changed(s, PathText);
        }

        public override void Revert(SceneModel m, ChangeSink s)
        {
            var list = ListOf(m, out _);
            list.RemoveAt(index);
            Changed(s, PathText);
        }

        public override long EstimatedBytes => 64 + FieldSchema.SizeOf(element ?? raw);
    }

    /// <summary>Removes the element at an index of a list field (a route keeps at least its start point).</summary>
    public sealed class ListRemoveCommand : FieldCommand
    {
        readonly int index;
        object removed;

        public ListRemoveCommand(string nodeId, string listPath, int index, string label = null) : base(nodeId, listPath, label ?? "Remove")
        {
            this.index = index;
        }

        public int Index => index;

        public override void Validate(SceneModel m)
        {
            var list = ListOf(m, out var slot);
            if (index < 0 || index >= list.Count) throw new CommandException(CommandCodes.NotFound, PathText + "[" + index + "]: index out of range");
            if (slot.Rule.RoutePoints && list.Count <= 1) throw new CommandException(CommandCodes.BadArgs, "a route keeps at least its start point");
        }

        public override void Apply(SceneModel m, ChangeSink s)
        {
            var list = ListOf(m, out _);
            removed = list[index];
            list.RemoveAt(index);
            Changed(s, PathText);
        }

        public override void Revert(SceneModel m, ChangeSink s)
        {
            var list = ListOf(m, out _);
            list.Insert(index, removed);
            Changed(s, PathText);
        }

        public override long EstimatedBytes => 64 + FieldSchema.SizeOf(removed);
    }

    /// <summary>Moves a list element from one index to another (final index after removal).</summary>
    public sealed class ListMoveCommand : FieldCommand
    {
        readonly int from, to;

        public ListMoveCommand(string nodeId, string listPath, int from, int to, string label = null) : base(nodeId, listPath, label ?? "Move element")
        {
            this.from = from;
            this.to = to;
        }

        public override void Validate(SceneModel m)
        {
            var list = ListOf(m, out _);
            if (from < 0 || from >= list.Count || to < 0 || to >= list.Count)
                throw new CommandException(CommandCodes.NotFound, PathText + ": move " + from + " → " + to + " outside 0.." + (list.Count - 1));
        }

        static void Move(IList list, int a, int b)
        {
            var e = list[a];
            list.RemoveAt(a);
            list.Insert(b, e);
        }

        public override void Apply(SceneModel m, ChangeSink s)
        {
            if (from == to) return;
            Move(ListOf(m, out _), from, to);
            Changed(s, PathText);
        }

        public override void Revert(SceneModel m, ChangeSink s)
        {
            if (from == to) return;
            Move(ListOf(m, out _), to, from);
            Changed(s, PathText);
        }

        public override long EstimatedBytes => 64;
    }

    /// <summary>Removes one key of a map field (FK bones, neck rotations, accessory visibility).</summary>
    public sealed class MapRemoveCommand : FieldCommand
    {
        readonly string key;
        object old;
        readonly string keyPath;

        public MapRemoveCommand(string nodeId, string mapPath, string key, string label = null) : base(nodeId, mapPath, label ?? "Remove key")
        {
            this.key = key;
            keyPath = Path != null && key != null ? Path.Text + "[" + FieldPath.Quote(key) + "]" : null;
        }

        public string Key => key;

        IDictionary MapOf(SceneModel m)
        {
            var slot = Slot(m);
            if (!(slot.Type.IsGenericType && slot.Type.GetGenericTypeDefinition() == typeof(SortedDictionary<,>)))
                throw new CommandException(CommandCodes.BadArgs, PathText + " is not a map");
            return (IDictionary)slot.Get() ?? throw new CommandException(CommandCodes.NotFound, PathText + " is null");
        }

        public override void Validate(SceneModel m)
        {
            if (key == null) throw new CommandException(CommandCodes.BadArgs, "null key");
            if (!MapOf(m).Contains(key)) throw new CommandException(CommandCodes.NotFound, PathText + ": no key " + key);
        }

        public override void Apply(SceneModel m, ChangeSink s)
        {
            var map = MapOf(m);
            old = map[key];
            map.Remove(key);
            Changed(s, keyPath);
        }

        public override void Revert(SceneModel m, ChangeSink s)
        {
            MapOf(m)[key] = old;
            Changed(s, keyPath);
        }

        public override long EstimatedBytes => 64 + 2L * (key?.Length ?? 0) + FieldSchema.SizeOf(old);
    }
}

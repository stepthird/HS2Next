// The edits of the advanced-pose block as field writes: what one change of a bone, a chain, a bust chain or a renderer's weights
// sets on the bus. A value is a map element; a value that says nothing any more (an identity correction, a chain or renderer with
// nothing set) leaves the map, and a removal sets the whole map, so the file never holds a no-op made by an edit and an emptied block
// is not written. Unity-free. Each method reads the block as it stands and returns one write, or null when nothing changes: a caller
// with several edits applies each result before it asks for the next, because a removal carries the whole map.
using System;
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    /// <summary>The field paths of the block on a character node.</summary>
    public static class AdvancedPosePaths
    {
        public const string Root = "chara.advancedPose";
        public const string Bones = Root + ".bones";
        public const string Chains = Root + ".chains";
        public const string Bust = Root + ".bust";
        public const string BlendShapes = Root + ".blendShapes";

        /// <summary>The bone entry of <paramref name="address"/>.</summary>
        public static string Bone(string address) => Bones + "[" + FieldPath.Quote(address) + "]";
        /// <summary>The chain entry of <paramref name="address"/>.</summary>
        public static string Chain(string address) => Chains + "[" + FieldPath.Quote(address) + "]";
        /// <summary>The values of a bust chain by its member name (<c>breastL</c>, <c>breastR</c>, <c>hipL</c>, <c>hipR</c>).</summary>
        public static string BustChain(string name) => Bust + "." + name;
        /// <summary>The renderer entry of <paramref name="address"/>.</summary>
        public static string Blend(string address) => BlendShapes + "[" + FieldPath.Quote(address) + "]";

        /// <summary>True for a changed path that touches the block.</summary>
        public static bool Touches(string path) => FieldPath.Overlaps(Root, path);
    }

    /// <summary>One field write: <see cref="Path"/> takes <see cref="Value"/>.</summary>
    public readonly struct AdvancedPoseWrite
    {
        public readonly string Path;
        public readonly object Value;

        public AdvancedPoseWrite(string path, object value)
        {
            Path = path;
            Value = value;
        }
    }

    public static class AdvancedPoseEdits
    {
        // ---------- bones ----------

        /// <summary>Changes the channels given (null keeps a channel). An identity result removes the entry; null when there is
        /// no entry and the result is identity.</summary>
        public static AdvancedPoseWrite? SetBone(CharaAdvancedPose block, string address, Vec3? pos, Vec3? rot, Vec3? scale)
        {
            var next = block.Bones.TryGetValue(address, out var now) ? DocClone.Clone(now) : new BoneCorrection();
            if (pos.HasValue) next.Pos = pos.Value;
            if (rot.HasValue) next.Rot = rot.Value;
            if (scale.HasValue) next.Scale = scale.Value;
            if (next.IsIdentity) return RemoveBone(block, address);
            return new AdvancedPoseWrite(AdvancedPosePaths.Bone(address), next);
        }

        /// <summary>Removes one entry; null when it is not there.</summary>
        public static AdvancedPoseWrite? RemoveBone(CharaAdvancedPose block, string address) => Without(block.Bones, address, AdvancedPosePaths.Bones);

        /// <summary>Removes every entry; null when there is none.</summary>
        public static AdvancedPoseWrite? ClearBones(CharaAdvancedPose block) => Cleared(block.Bones, AdvancedPosePaths.Bones);

        // ---------- chains ----------

        /// <summary>Stores a chain's values as given; values with nothing set remove the entry (null when it is not there).</summary>
        public static AdvancedPoseWrite? SetChain(CharaAdvancedPose block, string address, ChainValues values)
        {
            if (values.IsEmpty) return RemoveChain(block, address);
            return new AdvancedPoseWrite(AdvancedPosePaths.Chain(address), values);
        }

        /// <summary>A copy of a chain's stored values, empty when it has none: change it and hand it to <see cref="SetChain"/>.</summary>
        public static ChainValues ChainOf(CharaAdvancedPose block, string address) =>
            block.Chains.TryGetValue(address, out var now) ? DocClone.Clone(now) : new ChainValues();

        /// <summary>Removes one entry; null when it is not there.</summary>
        public static AdvancedPoseWrite? RemoveChain(CharaAdvancedPose block, string address) => Without(block.Chains, address, AdvancedPosePaths.Chains);

        /// <summary>Removes every entry; null when there is none.</summary>
        public static AdvancedPoseWrite? ClearChains(CharaAdvancedPose block) => Cleared(block.Chains, AdvancedPosePaths.Chains);

        // ---------- bust ----------

        /// <summary>Stores a bust chain's values as given (with nothing set the chain is no longer written).</summary>
        public static AdvancedPoseWrite SetBust(string name, BustValues values)
        {
            if (BustSet.KindOf(name) < 0) throw new ArgumentException("no bust chain \"" + name + "\"", nameof(name));
            return new AdvancedPoseWrite(AdvancedPosePaths.BustChain(name), values);
        }

        /// <summary>A copy of a bust chain's stored values: change it and hand it to <see cref="SetBust"/>.</summary>
        public static BustValues BustOf(CharaAdvancedPose block, string name)
        {
            int kind = BustSet.KindOf(name);
            if (kind < 0) throw new ArgumentException("no bust chain \"" + name + "\"", nameof(name));
            return DocClone.Clone(block.Bust[kind]);
        }

        /// <summary>Removes the values of every bust chain; null when there are none.</summary>
        public static AdvancedPoseWrite? ClearBust(CharaAdvancedPose block) =>
            block.Bust.IsEmpty ? (AdvancedPoseWrite?)null : new AdvancedPoseWrite(AdvancedPosePaths.Bust, new BustSet());

        // ---------- blend shapes ----------

        /// <summary>Changes one renderer's weights: <paramref name="set"/> are stored, <paramref name="remove"/> leave. A renderer left
        /// without a weight is removed; null when nothing changes.</summary>
        public static AdvancedPoseWrite? SetWeights(CharaAdvancedPose block, string address, IEnumerable<KeyValuePair<string, float>> set, IEnumerable<string> remove = null)
        {
            bool had = block.BlendShapes.TryGetValue(address, out var now);
            var next = had ? DocClone.Clone(now) : new BlendValues();
            if (remove != null) foreach (var shape in remove) next.Weights.Remove(shape);
            if (set != null) foreach (var kv in set) next.Weights[kv.Key] = kv.Value;
            if (next.Weights.Count == 0) return had ? RemoveRenderer(block, address) : null;
            return new AdvancedPoseWrite(AdvancedPosePaths.Blend(address), next);
        }

        /// <summary>Removes one renderer with all its weights; null when it is not there.</summary>
        public static AdvancedPoseWrite? RemoveRenderer(CharaAdvancedPose block, string address) => Without(block.BlendShapes, address, AdvancedPosePaths.BlendShapes);

        /// <summary>Removes every renderer; null when there is none.</summary>
        public static AdvancedPoseWrite? ClearWeights(CharaAdvancedPose block) => Cleared(block.BlendShapes, AdvancedPosePaths.BlendShapes);

        // ---------- maps ----------

        static AdvancedPoseWrite? Without<T>(SortedDictionary<string, T> map, string key, string path)
        {
            if (!map.ContainsKey(key)) return null;
            var next = new SortedDictionary<string, T>(map, StringComparer.Ordinal);
            next.Remove(key);
            return new AdvancedPoseWrite(path, next);
        }

        static AdvancedPoseWrite? Cleared<T>(SortedDictionary<string, T> map, string path) =>
            map.Count == 0 ? (AdvancedPoseWrite?)null : new AdvancedPoseWrite(path, new SortedDictionary<string, T>(StringComparer.Ordinal));
    }
}

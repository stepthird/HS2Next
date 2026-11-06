// Editing the `boneMods` array of a character's body, for the bones under the hair root. The rules of a materialOverrides array: an
// edit replaces the last entry of its bone or appends one, an entry that comes back to no effect leaves the array with every other
// entry of that bone, and every other entry stays the same object in its place. A new entry is stored with the location `bodyTop`.
// Unity-free: each function takes a list and returns a new one, or the same list when nothing changes. The array is set as a
// whole on <see cref="Path"/>.
using System;
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    public static class BoneModEdits
    {
        /// <summary>The field path of the array on a character node.</summary>
        public const string Path = "appearance.chara.body.boneMods";

        /// <summary>True when an entry changes nothing: scale 1, length 1, no offset, no rotation.</summary>
        public static bool IsIdentity(BoneModBase m) =>
            m.Scale.Equals(Vec3.One) && m.Length == 1f && m.Position.Equals(Vec3.Zero) && m.Rotation.Equals(Vec3.Zero);

        /// <summary>The index of the last entry of <paramref name="bone"/>, or -1. A character may hold a bone twice.</summary>
        public static int LastIndex(IReadOnlyList<BodyBoneMod> list, string bone)
        {
            if (list != null) for (int i = list.Count - 1; i >= 0; i--) if (string.Equals(list[i].Bone, bone, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>
        /// The array with the members given changed on the last entry of <paramref name="bone"/> (null keeps a member), or a new
        /// entry at the end. A result with no effect removes the bone's entries; the same list when nothing changes. Throws
        /// <see cref="CommandException"/> (bad_args) for an empty name or a value that is not finite.
        /// </summary>
        public static List<BodyBoneMod> Set(List<BodyBoneMod> list, string bone, Vec3? scale = null, float? length = null, Vec3? position = null, Vec3? rotation = null)
        {
            if (string.IsNullOrEmpty(bone)) throw new CommandException(CommandCodes.BadArgs, "no bone named");
            if (!Finite(scale) || !Finite(position) || !Finite(rotation) || (length.HasValue && !Finite(length.Value)))
                throw new CommandException(CommandCodes.BadArgs, "scale, length, position and rotation: finite numbers");
            list = list ?? new List<BodyBoneMod>();
            int i = LastIndex(list, bone);
            var e = i >= 0 ? DocClone.Clone(list[i]) : new BodyBoneMod { Bone = bone, Location = BodyBoneMod.LocationBodyTop };
            bool changed = false;
            if (scale.HasValue && !scale.Value.Equals(e.Scale)) { e.Scale = scale.Value; changed = true; }
            if (length.HasValue && length.Value != e.Length) { e.Length = length.Value; changed = true; }
            if (position.HasValue && !position.Value.Equals(e.Position)) { e.Position = position.Value; changed = true; }
            if (rotation.HasValue && !rotation.Value.Equals(e.Rotation)) { e.Rotation = rotation.Value; changed = true; }
            if (!changed) return list;
            if (IsIdentity(e)) return Remove(list, bone);
            var r = new List<BodyBoneMod>(list);
            if (i >= 0) r[i] = e; else r.Add(e);
            return r;
        }

        /// <summary>The array without the entries of <paramref name="bone"/>; the same list when there is none.</summary>
        public static List<BodyBoneMod> Remove(List<BodyBoneMod> list, string bone) => RemoveWhere(list, b => string.Equals(b, bone, StringComparison.Ordinal));

        /// <summary>The array without the entries of the bones <paramref name="selected"/> takes; the same list when there is none.</summary>
        public static List<BodyBoneMod> RemoveWhere(List<BodyBoneMod> list, Func<string, bool> selected)
        {
            if (list == null) return null;
            List<BodyBoneMod> r = null;
            for (int i = 0; i < list.Count; i++)
            {
                if (!selected(list[i].Bone)) { r?.Add(list[i]); continue; }
                if (r == null) r = list.GetRange(0, i);
            }
            return r ?? list;
        }

        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        static bool Finite(Vec3? v) => !v.HasValue || (Finite(v.Value.X) && Finite(v.Value.Y) && Finite(v.Value.Z));
    }
}

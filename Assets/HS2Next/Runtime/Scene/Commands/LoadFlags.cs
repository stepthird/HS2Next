// The five parts of a character load (HS2's Maker load window: «顔» face, «体» body, «髪» hair, «コーデ» coordinate,
// «キャラ設定» profile; the same bit values as the game's flags). All five replace the whole appearance (Change
// character); fewer replace the chosen sections of the stored documents (SetCharaSectionsCommand, plus the coordinate
// command for «コーデ»). Unity-free.
using System;
using System.Collections.Generic;

namespace HS2Next.Scenes
{
    [Flags]
    public enum LoadFlags
    {
        None = 0,
        /// <summary>«顔»: the face document, the face-group skin overlays, the head-bone modifiers, the head's MaterialEditor
        /// items, `studioOverlays.eyes`.</summary>
        Face = 1,
        /// <summary>«体»: the body document but the face-group overlays and head-bone modifiers, the other MaterialEditor
        /// character items, `studioOverlays.skin`.</summary>
        Body = 2,
        /// <summary>«髪»: the whole hair document.</summary>
        Hair = 4,
        /// <summary>«コーデ»: the whole coordinate with its coordinate-scope ext data, `studioOverlays.clothes`.</summary>
        Coordinate = 8,
        /// <summary>«キャラ設定»: `profile.name`, `profile.futanari`, `profile.hs2` (never the sex).</summary>
        Profile = 16,
        All = Face | Body | Hair | Coordinate | Profile,
    }

    /// <summary>The names of <see cref="LoadFlags"/> for the bridge and the UI ("face", "body", "hair", "coordinate",
    /// "profile"; "all" for the five).</summary>
    public static class LoadFlagNames
    {
        /// <summary>The five names in bit order.</summary>
        public static readonly IReadOnlyList<string> Names = new[] { "face", "body", "hair", "coordinate", "profile" };
        public const string AllName = "all";

        static readonly LoadFlags[] Bits = { LoadFlags.Face, LoadFlags.Body, LoadFlags.Hair, LoadFlags.Coordinate, LoadFlags.Profile };

        /// <summary>The flag of one name (case-insensitive; "all" = <see cref="LoadFlags.All"/>); false for an unknown name.</summary>
        public static bool TryParse(string name, out LoadFlags flag)
        {
            flag = LoadFlags.None;
            if (name == null) return false;
            if (string.Equals(name, AllName, StringComparison.OrdinalIgnoreCase)) { flag = LoadFlags.All; return true; }
            for (int i = 0; i < Names.Count; i++)
                if (string.Equals(name, Names[i], StringComparison.OrdinalIgnoreCase)) { flag = Bits[i]; return true; }
            return false;
        }

        /// <summary>The union of a list of names; false (with the first unknown name) when one is not a flag name.</summary>
        public static bool TryParse(IEnumerable<string> names, out LoadFlags flags, out string unknown)
        {
            flags = LoadFlags.None;
            unknown = null;
            foreach (var n in names ?? Array.Empty<string>())
            {
                if (!TryParse(n, out var f)) { unknown = n; return false; }
                flags |= f;
            }
            return true;
        }

        /// <summary>The names of the set flags in bit order.</summary>
        public static List<string> ToNames(LoadFlags flags)
        {
            var l = new List<string>();
            for (int i = 0; i < Bits.Length; i++) if ((flags & Bits[i]) != 0) l.Add(Names[i]);
            return l;
        }

        /// <summary>True when every one of the five flags is set.</summary>
        public static bool IsAll(LoadFlags flags) => (flags & LoadFlags.All) == LoadFlags.All;
    }
}

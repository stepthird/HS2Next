// Character appearance of a scene node: a stored character subtree (the
// 2.1 documents, ext and provenance that are saved) or an HS2 card the node was added from (imported into the scene file
// by its first edit or the next save). Which builder realizes it is a Unity-layer decision; the model only carries it.
// Also the importer hook that Phase 2.3 fills.
using System;
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    /// <summary>
    /// The appearance of one character node. Once the object is in the model it is changed only by the session
    /// (SetAppearanceCommand, the import cache); everyone else treats it as read-only.
    /// </summary>
    public sealed class CharaAppearance
    {
        /// <summary>The documents, ext data and provenance that are saved (2.1 type); null until imported.</summary>
        public CharaSubtree Stored { get; set; }
        /// <summary>An HS2 card the node was added from; shared (never copied) between duplicates.</summary>
        public CardAppearanceSource Card { get; set; }

        /// <summary>Neither a stored subtree nor a card source.</summary>
        public bool IsMissing => Stored == null && Card == null;

        /// <summary>
        /// Accessory ids of the worn coordinate in slot order: <c>Stored.Coord.Accessories[*].Id</c>, else
        /// <c>Card.AccessoryIds</c>; null when the coordinate is not known.
        /// </summary>
        public IReadOnlyList<string> WornAccessoryIds()
        {
            if (Stored != null)
            {
                var ids = new List<string>();
                foreach (var a in Stored.Coord?.Accessories ?? new List<AccessoryEntry>())
                    if (a?.Id != null) ids.Add(a.Id);
                return ids;
            }
            return Card?.AccessoryIds;
        }

        /// <summary>The copy a duplicated node gets: the stored subtree cloned (accessory ids kept), the card
        /// source shared.</summary>
        public CharaAppearance Clone() => new CharaAppearance
        {
            Stored = Stored?.Clone(),
            Card = Card,
        };
    }

    /// <summary>
    /// An HS2 card a character node was added from (Unity-free record, created by the Unity layer's card source
    /// factory). Immutable once created: duplicates share it.
    /// </summary>
    public sealed class CardAppearanceSource
    {
        /// <summary>Path relative to the HS2 install root, '/'-separated; null for a card outside the install.</summary>
        public string Hs2Path { get; set; }
        /// <summary>The absolute path the card was read from (runtime only, never saved).</summary>
        public string FullPath { get; set; }
        /// <summary>64 lowercase hex of the whole card file.</summary>
        public string Sha256 { get; set; }
        /// <summary>0 male, 1 female (the card's Parameter block).</summary>
        public int Sex { get; set; }
        /// <summary>Card full name (the default label).</summary>
        public string Name { get; set; }
        /// <summary>Ids of the worn coordinate's accessories in slot order (<see cref="AccessoryId"/> of every slot whose
        /// type is 351..363), or null when not yet known.</summary>
        public IReadOnlyList<string> AccessoryIds { get; set; }
        /// <summary>Ids of <see cref="AccessoryIds"/> a Studio load of the card hides (Additional Accessory Controls' startup pass,
        /// MoreAccessories show="false"; Phase 4.5): adding the card folds them once into the node's state.accessories as false;
        /// null = none.</summary>
        public IReadOnlyCollection<string> HiddenAccessoryIds { get; set; }
        /// <summary>Hands the card bytes to the importer (served from the Unity layer's card cache).</summary>
        public Func<byte[]> ReadBytes { get; set; }

        /// <summary>
        /// The id the importer gives the accessory of card slot <paramref name="slot"/>:
        /// <c>JsonText.DerivedId("&lt;sha256&gt;/acc/&lt;slot&gt;")</c>.
        /// </summary>
        public static string AccessoryId(string sha256, int slot) =>
            JsonText.DerivedId((sha256 ?? "") + "/acc/" + slot.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Converts a card into a character file (Phase 2.3 fills <see cref="CharaImport.Importer"/>).</summary>
    public interface ICharaImporter
    {
        /// <summary>Converts the card of <paramref name="source"/> into a character file in memory: subtree, resources,
        /// provenance (manifest.import). Main thread. Throws or returns null on failure.</summary>
        CharaContainer Import(CardAppearanceSource source, FindingList findings);
    }

    /// <summary>The importer hook: null until Phase 2.3.</summary>
    public static class CharaImport
    {
        public static ICharaImporter Importer;
    }
}

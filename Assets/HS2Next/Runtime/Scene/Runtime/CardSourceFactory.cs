// Card sources of character nodes: reads a card file once, hashes it
// (SHA-256 of the whole file), parses it (Card.Load) and its KKEx blocks (CardExtensions.Decode, for the
// MoreAccessories slots), and fills the Unity-free CardAppearanceSource: path relative to the HS2 install, hash,
// sex, name and the ids of the worn coordinate's accessories (the importer's formula
// 6). The parsed card and the bytes go to the CardCache. (The card-reference resolver of the retired hs2next.cardref
// block went with Phase 4.6.)
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HS2Next.Data;
using HS2Next.Data.Ext;
using HS2Next.IO;
using HS2Next.Save;

namespace HS2Next.Scenes.Runtime
{
    public sealed class CardSourceFactory
    {
        readonly RuntimeServices services;

        public CardSourceFactory(RuntimeServices services) { this.services = services; }

        /// <summary>A source for the card file at <paramref name="fullPath"/> (absolute). Throws when the file cannot be read or parsed.</summary>
        public CardAppearanceSource FromFile(string fullPath)
        {
            fullPath = Path.GetFullPath(fullPath);
            var bytes = File.ReadAllBytes(fullPath);
            string sha = Sha256Hex(bytes);
            var cached = services.Cards.Get(sha);
            var card = cached?.Card ?? Card.Load(bytes, fullPath);
            services.Cards.Put(sha, fullPath, bytes, card);
            var src = new CardAppearanceSource
            {
                Hs2Path = RelativeToHs2(fullPath),
                FullPath = fullPath,
                Sha256 = sha,
                Sex = card.Parameter.Get("sex").Int(1),
                Name = card.Name,
                AccessoryIds = AccessoryIds(card, sha),
            };
            var cache = services.Cards;
            src.ReadBytes = () => cache.BytesOf(src);
            // the accessory hides of a Studio load (Phase 4.5): folded into the state when the card is added
            src.HiddenAccessoryIds = HS2Next.Import.CardStateImport.HiddenAtCardLoad(card, src, () => CharaImport.Importer ?? ImportSetup.ImporterFor(services.Catalog));
            return src;
        }

        /// <summary>'/'-separated path relative to the HS2 install root, or null for a file outside it.</summary>
        public string RelativeToHs2(string fullPath)
        {
            if (string.IsNullOrEmpty(services.Hs2Root)) return null;
            string root = Path.GetFullPath(services.Hs2Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(fullPath);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
            return full.Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/');
        }

        /// <summary>
        /// Ids of the worn coordinate's accessories in slot order: slots 0..19 from <c>Accessory.parts</c>, slots ≥ 20
        /// from MoreAccessories (element i = slot 20 + i), every slot of type 351..363 →
        /// <see cref="CardAppearanceSource.AccessoryId"/>(sha, slot).
        /// </summary>
        public static IReadOnlyList<string> AccessoryIds(Card card, string sha256)
        {
            var ids = new List<string>();
            foreach (var slot in AccessorySlots(card))
                ids.Add(CardAppearanceSource.AccessoryId(sha256, slot));
            return ids;
        }

        /// <summary>Card slots (ascending) whose accessory type is 351..363.</summary>
        public static List<int> AccessorySlots(Card card)
        {
            var slots = new List<int>();
            int s = 0;
            var parts = card?.Accessory?.Get("parts");
            if (parts != null)
                foreach (var a in parts.Items())
                {
                    int type = a.Get("type").Int();
                    if (type >= 351 && type <= 363) slots.Add(s);
                    s++;
                }
            try
            {
                var ma = CardExtensions.Decode(card)?.MoreAccessories;
                if (ma != null)
                    foreach (var p in ma.Parts)
                        if (!p.IsEmpty && p.Slot >= 20) slots.Add(p.Slot);
            }
            catch (Exception) { }   // undecodable extension data: the base slots only (the build reports it)
            slots.Sort();
            return slots;
        }

        public static string Sha256Hex(byte[] bytes)
        {
            using (var h = SHA256.Create())
            {
                var d = h.ComputeHash(bytes);
                var sb = new StringBuilder(64);
                foreach (var b in d) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}

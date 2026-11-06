// The address of a bone, a swing chain or a blend-shape renderer of a character: the map key in the scene file and the argument
// of the commands. By name, not by path: a bone is a bare name (the body bones, then the head bones) or a name under a part
// (hair:k, clothes:k, acc:id); a chain is a root name under its part (or other); a renderer a name under head, body, or a part.
// `#n` picks the n-th occurrence of a repeated name in that part (0-based, depth first in child order). The same grammar for
// both sexes: a name a body lacks is simply not found there. Unity-free; the text grammar itself is PoseAddressGrammar in the
// save layer, because the file's reader reports the keys that are not addresses.
using System;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    /// <summary>A parsed address; <see cref="Text"/> is its one canonical text.</summary>
    public readonly struct PoseAddress : IEquatable<PoseAddress>
    {
        public readonly PoseAddressForm Form;
        public readonly PosePart Part;
        /// <summary>The hair slot (0..3) or clothes slot (0..7); else -1.</summary>
        public readonly int Slot;
        /// <summary>The accessory's id; else null.</summary>
        public readonly string AccessoryId;
        /// <summary>The transform's or renderer's name, without the occurrence mark.</summary>
        public readonly string Name;
        /// <summary>Which occurrence of <see cref="Name"/> in the part (0 = the first).</summary>
        public readonly int Occurrence;

        PoseAddress(PoseAddressForm form, PosePart part, int slot, string accessoryId, string name, int occurrence)
        {
            Form = form;
            Part = part;
            Slot = slot;
            AccessoryId = accessoryId;
            Name = name;
            Occurrence = occurrence;
        }

        /// <summary>The canonical text: the map key.</summary>
        public string Text => PoseAddressGrammar.Format(Part, Slot, AccessoryId, Name, Occurrence);

        /// <summary>The part as text (<c>hair:1</c>, <c>acc:&lt;id&gt;</c>, <c>head</c>, <c>body</c>, <c>other</c>; empty for a bare bone name).</summary>
        public string PartText => PoseAddressGrammar.PartText(Part, Slot, AccessoryId);

        public override string ToString() => Text;

        // ---------- reading ----------

        /// <summary>Reads an address of <paramref name="form"/>; false with the reason in <paramref name="error"/>.</summary>
        public static bool TryParse(PoseAddressForm form, string text, out PoseAddress address, out string error)
        {
            if (PoseAddressGrammar.TryParse(form, text, out var part, out int slot, out string id, out string name, out int n, out error))
            {
                address = new PoseAddress(form, part, slot, id, name, n);
                return true;
            }
            address = default;
            return false;
        }

        /// <summary>Reads an address of <paramref name="form"/>; <see cref="FormatException"/> with the reason on bad text.</summary>
        public static PoseAddress Parse(PoseAddressForm form, string text) =>
            TryParse(form, text, out var a, out string error) ? a : throw new FormatException("bad address \"" + text + "\": " + error);

        /// <summary>The canonical text of <paramref name="text"/>, or null when it is not an address of <paramref name="form"/>. Commands
        /// store this form; a key of a file that is not in it stays inactive.</summary>
        public static string Canonical(PoseAddressForm form, string text) => PoseAddressGrammar.Canonical(form, text);

        /// <summary>True for a key that acts: an address of <paramref name="form"/> in its canonical form.</summary>
        public static bool IsCanonical(PoseAddressForm form, string text) => PoseAddressGrammar.IsCanonical(form, text);

        // ---------- making ----------

        /// <summary>The canonical text of an address made of its parts.</summary>
        public static string Format(PosePart part, int slot, string accessoryId, string name, int occurrence = 0) =>
            PoseAddressGrammar.Format(part, slot, accessoryId, name, occurrence);

        /// <summary>The address of a body or head bone: its bare name.</summary>
        public static PoseAddress Bone(string name) => new PoseAddress(PoseAddressForm.Bone, PosePart.Named, -1, null, name, 0);

        /// <summary>A bone under a part (<see cref="PosePart.Hair"/>, <see cref="PosePart.Clothes"/>, <see cref="PosePart.Accessory"/>).</summary>
        public static PoseAddress Bone(PosePart part, int slot, string accessoryId, string name, int occurrence = 0) =>
            new PoseAddress(PoseAddressForm.Bone, part, slot, accessoryId, name, occurrence);

        /// <summary>A swing chain by its root's name under its part (hair, clothes, accessory, other).</summary>
        public static PoseAddress Chain(PosePart part, int slot, string accessoryId, string rootName, int occurrence = 0) =>
            new PoseAddress(PoseAddressForm.Chain, part, slot, accessoryId, rootName, occurrence);

        /// <summary>A skinned renderer by its name under its part (head, body, hair, clothes, accessory).</summary>
        public static PoseAddress Blend(PosePart part, int slot, string accessoryId, string rendererName, int occurrence = 0) =>
            new PoseAddress(PoseAddressForm.Blend, part, slot, accessoryId, rendererName, occurrence);

        // ---------- equality ----------

        public bool Equals(PoseAddress o) => Form == o.Form && string.Equals(Text, o.Text, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is PoseAddress o && Equals(o);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Text ?? "") * 31 + (int)Form;
    }
}

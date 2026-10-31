// The read-only window on imported HS2 plugin data (the core namespace `hs2.kkex` of character and coordinate files). The
// importer keeps every plugin data entry of an HS2 card as its exact MessagePack bytes; a handler that also implements
// IKkexConsumer is handed, while a file loads, the entries of the plugin ids it lists. Nothing it does changes the stored
// entries (they are written back unchanged); a consumer that decodes them stores its own typed block under its namespace at
// the next save. Part of the Api (assembly HS2Next.Api).
using System.Collections.Generic;

namespace HS2Next.Api.Save
{
    /// <summary>
    /// Optional on an <see cref="IExtBlockHandler"/> (implemented by plugins). While a character or coordinate file loads, for
    /// every character and coordinate scope instance the handler takes, the host calls <see cref="ReadKkex"/> once per imported
    /// entry of that instance whose plugin id is in <see cref="KkexIds"/> (entries in the stored order: origin, object, id),
    /// after the handler's own <see cref="IExtBlockHandler.Read"/> of the same instance, also when the handler has no block
    /// there. A throw counts as a fault of the handler, as in Read.
    /// </summary>
    public interface IKkexConsumer
    {
        /// <summary>The HS2 plugin data ids to receive (ordinal comparison).</summary>
        IReadOnlyList<string> KkexIds { get; }
        /// <summary>Reads one imported entry; store what is needed and apply it later, as in Read.</summary>
        void ReadKkex(IKkexReadContext ctx);
    }

    /// <summary>One imported plugin data entry (implemented by the host). Valid only during the call.</summary>
    public interface IKkexReadContext : IExtContext
    {
        /// <summary>The HS2 plugin data id, exact.</summary>
        string Id { get; }
        /// <summary>Where the card held it: "kkex" (the card's extended data), "object" (data attached to one card object) or
        /// "block" (the whole raw block, when it could not be split).</summary>
        string Origin { get; }
        /// <summary>The card object of origin "object", else null.</summary>
        string Object { get; }
        /// <summary>The plugin's own data version as stored by the card, or null.</summary>
        int? Version { get; }
        /// <summary>"source" (the import also decoded it into the document; kept as provenance) or "opaque" (the only copy).</summary>
        string Role { get; }
        /// <summary>Why an opaque entry was not decoded ("noDecoder", "unsupportedVersion", "decodeError", "malformed",
        /// "blockVersion"), or null.</summary>
        string Reason { get; }
        /// <summary>The form of the card it came from ("chara" or "coord").</summary>
        string Form { get; }
        /// <summary>Byte count of the data.</summary>
        long Size { get; }
        /// <summary>A copy of the exact bytes as the card held them.</summary>
        byte[] ReadBytes();
    }
}

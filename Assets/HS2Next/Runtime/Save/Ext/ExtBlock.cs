// In-memory ext data of one scope instance. Inline JSON
// lives in the documents' ExtInline holders (the documents carry it, re-rooting is free); what
// lives here is per namespace: the block's files (paths relative to `ext/<ns>/`), the manifest
// metadata (version, required, unknown members of the entry) and whether a handler owns the block.
// Unknown blocks (no handler, declined, or the handler threw) are written back unchanged.
using System;
using System.Collections.Generic;
using HS2Next.Api.Save;

namespace HS2Next.Save
{
    public sealed class ExtBlock
    {
        public readonly string Ns;
        /// <summary>The manifest `version` (stored at load; the handler's version after it wrote the block).</summary>
        public int Version;
        public bool Required;
        /// <summary>True when the data had a manifest ext entry at load.</summary>
        public bool HadManifestEntry;
        /// <summary>Unknown members of the manifest entry, written back with it.</summary>
        public JsonLeftovers ManifestUnknown;
        /// <summary>Files relative to <c>ext/&lt;ns&gt;/</c>, ordinal order.</summary>
        public readonly SortedDictionary<string, byte[]> Files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        /// <summary>
        /// True when no handler owns the block: none was registered at load, it declined, or it threw in Read. Such a
        /// block is never offered to Write and is written back unchanged.
        /// </summary>
        public bool IsUnknown = true;
        /// <summary>Why the block is unknown ("no handler", "declined: …", "Read threw …"), for reports.</summary>
        public string UnknownReason = "no handler";

        public ExtBlock(string ns) { Ns = ns; }

        public ExtBlock Clone()
        {
            var c = new ExtBlock(Ns)
            {
                Version = Version, Required = Required, HadManifestEntry = HadManifestEntry,
                ManifestUnknown = ManifestUnknown?.Clone(), IsUnknown = IsUnknown, UnknownReason = UnknownReason,
            };
            foreach (var kv in Files) c.Files[kv.Key] = kv.Value;
            return c;
        }
    }

    /// <summary>The file-backed ext data of one scope instance: blocks by namespace plus the core `hs2.kkex` store (chara/coord only).</summary>
    public sealed class ExtScopeData
    {
        public readonly ExtScope Scope;
        public readonly SortedDictionary<string, ExtBlock> Blocks = new SortedDictionary<string, ExtBlock>(StringComparer.Ordinal);
        /// <summary>Imported HS2 plugin data; null when there is none.</summary>
        public KkexStore Kkex;

        public ExtScopeData(ExtScope scope) { Scope = scope; }

        public ExtBlock Get(string ns) => Blocks.TryGetValue(ns, out var b) ? b : null;

        public ExtBlock GetOrAdd(string ns)
        {
            if (!Blocks.TryGetValue(ns, out var b)) Blocks[ns] = b = new ExtBlock(ns);
            return b;
        }

        public bool IsEmpty => Blocks.Count == 0 && (Kkex == null || Kkex.IsEmpty);

        public ExtScopeData Clone()
        {
            var c = new ExtScopeData(Scope) { Kkex = Kkex?.Clone() };
            foreach (var kv in Blocks) c.Blocks[kv.Key] = kv.Value.Clone();
            return c;
        }
    }
}

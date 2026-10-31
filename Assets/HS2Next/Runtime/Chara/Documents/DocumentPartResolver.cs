// The part resolver of a document build: every reference was resolved once by the
// importer, so the build looks the stored `guid#id` up in the catalog by part key and never runs
// the card path's property matching or (category, slot) scan. Missing content falls back like the card path (the
// assembler's own fallbacks) and is listed: part.missingMod (GUID not installed), part.missing (installed GUID or the
// base game without the row), part.unresolved (hs2.unresolved#v). Installing the mod later repairs the build.
using System;
using System.Collections.Generic;
using HS2Next.Content;
using HS2Next.Save;

namespace HS2Next.Chara.Documents
{
    public sealed class DocumentPartResolver : ICharaPartResolver
    {
        public const string UnresolvedGuid = "hs2.unresolved", KcoxGuid = "hs2.kcox";
        public const int KcoxPatternId = 58947543, PatternCategory = 348;

        /// <summary>One registered reference.</summary>
        public sealed class Part
        {
            public string Key;
            /// <summary>The document's `ref` text.</summary>
            public string Ref;
            /// <summary>The category the document's reference means (by its sex).</summary>
            public int Category;
            /// <summary>Document path of the reference (findings).</summary>
            public string Path;
        }

        readonly ContentCatalog catalog;
        readonly FindingList findings;
        readonly Dictionary<string, Part> parts = new Dictionary<string, Part>(StringComparer.Ordinal);
        readonly HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);
        bool sexMismatchReported;

        /// <summary>Missing content met by the lookups so far (one per part key and kind).</summary>
        public readonly List<MissingContent> Missing = new List<MissingContent>();

        public DocumentPartResolver(ContentCatalog catalog, FindingList findings)
        {
            this.catalog = catalog;
            this.findings = findings;
        }

        public IReadOnlyDictionary<string, Part> Parts => parts;

        /// <summary>Registers the reference of a part key (the adapter does this for every reference it writes).</summary>
        public void Register(string key, string refText, int category, string path)
        {
            // an edit that changes the reference re-evaluates its missing content
            if (parts.TryGetValue(key, out var old) && (old.Ref != refText || old.Category != category)) Forget(key);
            parts[key] = new Part { Key = key, Ref = refText, Category = category, Path = path };
        }

        /// <summary>Removes a part key (an accessory slot that no longer exists) and its missing content.</summary>
        public bool Unregister(string key)
        {
            if (key == null || !parts.Remove(key)) return false;
            Forget(key);
            return true;
        }

        void Forget(string key)
        {
            Missing.RemoveAll(m => m.Detail != null && m.Detail.StartsWith(key + " ", StringComparison.Ordinal));
            reported.RemoveWhere(r => r.EndsWith("\u0000" + key, StringComparison.Ordinal));
        }

        /// <summary>The kinds of missing content this resolver records.</summary>
        public static bool IsResolverKind(string kind) => kind == "part.missing" || kind == "part.missingMod" || kind == "part.unresolved";

        public bool TryGetPart(string key, out Part p) => parts.TryGetValue(key ?? "", out p);

        public ListEntry Resolve(int category, int rawId, string property, string part)
        {
            if (part == null) return catalog?.GetEntry(category, ContentId.Vanilla(rawId));
            if (!parts.TryGetValue(part, out var p))
            {
                if (reported.Add("unregistered\u0000" + part))
                    findings?.Warning("build.unregisteredPart", part, "lookup with a part key the document adapter did not register; the base-game row of id " + rawId + " is used");
                return catalog?.GetEntry(category, ContentId.Vanilla(rawId));
            }
            int cat = p.Category;
            if (cat != category && !sexMismatchReported)
            {
                sexMismatchReported = true;
                findings?.Warning("build.coordSexMismatch", p.Path, "the coordinate was made for the other sex: its clothes are resolved in categories " + cat + " (the build asked " + category + ")");
            }
            return ResolveRef(p, cat);
        }

        ListEntry ResolveRef(Part p, int cat)
        {
            if (!ContentId.TryParse(p.Ref, out var id) || id.IsEmpty)
            {
                AddMissing("part.unresolved", null, p, "reference \"" + p.Ref + "\" is not a guid#id");
                return null;
            }
            string guid = id.SourceGuid;
            if (cat == PatternCategory && id.Id == KcoxPatternId && string.Equals(guid, KcoxGuid, StringComparison.OrdinalIgnoreCase))
                return null;   // the KCOX applier draws Pattern_<k>_<n>, as the card path
            if (string.Equals(guid, UnresolvedGuid, StringComparison.OrdinalIgnoreCase))
            {
                AddMissing("part.unresolved", null, p, "id " + id.Id + " could not be resolved at import");
                return null;
            }
            var row = catalog?.GetEntry(cat, id);
            if (row != null) return row;
            if (id.IsVanilla) AddMissing("part.missing", guid, p, "base-game row " + id.Id + " of category " + cat + " is not installed");
            else if (GuidInstalled(guid, cat)) AddMissing("part.missing", guid, p, "row " + id + " of category " + cat + " is not in the installed mod");
            else AddMissing("part.missingMod", guid, p, "mod " + guid + " is not installed (" + id + ", category " + cat + ")");
            return null;
        }

        bool GuidInstalled(string guid, int cat)
        {
            if (catalog == null) return false;
            if (catalog.ZipmodsByGuid.ContainsKey(guid)) return true;
            return catalog.EntriesOf(cat, guid).Count > 0;
        }

        void AddMissing(string kind, string guid, Part p, string message)
        {
            if (!reported.Add(kind + "\u0000" + p.Key)) return;
            Missing.Add(new MissingContent { Kind = kind, Guid = guid ?? "", Detail = p.Key + " " + p.Ref });
            findings?.Warning(kind, p.Path, message);
        }
    }
}

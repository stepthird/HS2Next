// A character's content: chara.json, the worn
// coordinate, the ext data of both scopes (inline in the documents, files and hs2.kkex here) and the
// import provenance. The same unit is a .hs2nchara file (root "") and one character node of a scene
// (root "charas/<nodeId>/"); moving it between the two only changes the root it is written under.
using System;
using System.Collections.Generic;
using HS2Next.Api.Save;

namespace HS2Next.Save
{
    public sealed class CharaSubtree
    {
        public CharaDocument Chara = new CharaDocument();
        public CoordDocument Coord = new CoordDocument();
        /// <summary>File-backed ext data of the chara scope (blocks and hs2.kkex).</summary>
        public ExtScopeData CharaExt = new ExtScopeData(ExtScope.Chara);
        /// <summary>File-backed ext data of the coord scope.</summary>
        public ExtScopeData CoordExt = new ExtScopeData(ExtScope.Coord);
        /// <summary>Provenance of an HS2 import; null for content made in HS2Next.</summary>
        public ImportProvenance Import;
        /// <summary>Scene only: unknown members of the manifest's <c>charas[&lt;id&gt;]</c> value.</summary>
        public JsonLeftovers ManifestCharaUnknown;

        public const string CoordDir = "coord/";

        /// <summary>Entry names of the subtree's documents under <paramref name="root"/>.</summary>
        public static string CharaEntry(string root) => root + CharaDocument.EntryName;
        public static string CoordEntry(string root) => root + CoordDir + CoordDocument.EntryName;

        // ---------- load ----------

        internal static void ParseDocs(LoadSession s, string root, bool required)
        {
            s.Doc(CharaEntry(root), required);
            s.Doc(CoordEntry(root), required);
        }

        /// <summary>Binds the parsed documents and builds the ext data of both scopes. Returns null when the chara document is absent.</summary>
        internal static CharaSubtree Bind(LoadSession s, string root, ImportProvenance import)
        {
            var cr = s.Reader(CharaEntry(root));
            if (cr == null) return null;
            var t = new CharaSubtree { Import = import };
            t.Chara = CharaDocument.Read(cr);
            var kr = s.Reader(CoordEntry(root));
            if (kr == null) throw new SaveFormatException(SaveErrorCode.MissingDocument, "missing document", CoordEntry(root));
            t.Coord = CoordDocument.Read(kr);
            t.CharaExt = s.BuildScope(ExtScope.Chara, "chara", root, root, t.Chara.Ext, null);
            t.CoordExt = s.BuildScope(ExtScope.Coord, "coord", root + CoordDir, root + CoordDir, t.Coord.Ext, new CoordAccessoryExt(t.Coord), new CoordClothesExt(t.Coord));
            return t;
        }

        /// <summary>Dispatcher instances of this subtree: chara scope, then coord scope.</summary>
        internal IEnumerable<ExtInstance> Instances(string format, string root, string ownerId)
        {
            yield return new ExtInstance { Scope = ExtScope.Chara, OwnerId = ownerId, ContainerFormat = format, Root = root, Data = CharaExt, Main = Chara.Ext };
            yield return new ExtInstance
            {
                Scope = ExtScope.Coord, OwnerId = ownerId, ContainerFormat = format, Root = root + CoordDir, Data = CoordExt, Main = Coord.Ext,
                Accessories = new CoordAccessoryExt(Coord), Clothes = new CoordClothesExt(Coord),
            };
        }

        // ---------- save ----------

        internal void Save(SaveSession s, string root)
        {
            s.Covers(Coord);
            s.AddDoc(CharaEntry(root), Chara.Write);
            s.AddDoc(CoordEntry(root), Coord.Write);
            s.AddExt(CharaExt, "chara", root, root, Chara.Ext, null);
            s.AddExt(CoordExt, "coord", root + CoordDir, root + CoordDir, Coord.Ext, new CoordAccessoryExt(Coord), new CoordClothesExt(Coord));
        }

        // ---------- copies ----------

        /// <summary>A deep copy (documents through a write/read round trip; ext data and provenance cloned).</summary>
        public CharaSubtree Clone()
        {
            return new CharaSubtree
            {
                Chara = CloneChara(Chara),
                Coord = CloneCoord(Coord),
                CharaExt = CharaExt.Clone(),
                CoordExt = CoordExt.Clone(),
                Import = Import?.Clone(),
                ManifestCharaUnknown = ManifestCharaUnknown?.Clone(),
            };
        }

        public static CharaDocument CloneChara(CharaDocument d) =>
            CharaDocument.Read(DocReader.ForDocument(CanonicalJsonWriter.ToBytes(CharaDocument.EntryName, d.Write), new DocReadContext(CharaDocument.EntryName)));

        public static CoordDocument CloneCoord(CoordDocument d) =>
            CoordDocument.Read(DocReader.ForDocument(CanonicalJsonWriter.ToBytes(CoordDocument.EntryName, d.Write), new DocReadContext(CoordDocument.EntryName)));

        /// <summary>`res` names referenced by the subtree's documents (to copy resources between containers).</summary>
        public HashSet<string> ReferencedResources()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            ResStore.ScanReferences(CanonicalJsonWriter.ToBytes(CharaDocument.EntryName, Chara.Write), set);
            ResStore.ScanReferences(CanonicalJsonWriter.ToBytes(CoordDocument.EntryName, Coord.Write), set);
            return set;
        }

        /// <summary>`res` names referenced by a coordinate document.</summary>
        public static HashSet<string> ReferencedResources(CoordDocument coord)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            ResStore.ScanReferences(CanonicalJsonWriter.ToBytes(CoordDocument.EntryName, coord.Write), set);
            return set;
        }
    }
}

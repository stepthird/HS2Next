// manifest.json: format name and versions (only here), writing app,
// times, library fields (title, sex, thumbnail size), `requires`, ext entries, import
// provenance and, for scenes, the provenance of each character subtree. Unknown members are
// kept per object. Read and written with DocReader / CanonicalJsonWriter like the documents.
using System;
using System.Collections.Generic;

namespace HS2Next.Save
{
    /// <summary>Format names and the format revision this code reads and writes.</summary>
    public static class SaveFormats
    {
        public const string Chara = "hs2next.chara";
        public const string Coord = "hs2next.coord";
        public const string Scene = "hs2next.scene";
        public const string Pose = "hs2next.pose";
        public const string Prefix = "hs2next.";

        /// <summary>
        /// The base revision: what a writer puts in <c>formatVersion</c> when nothing in the file needs a later one (5.1: writers
        /// write the lowest revision whose schema covers the file, so every file without the later members stays the frozen
        /// v1 byte for byte). The revision this code reads and migrates to is <see cref="SupportedVersion"/>.
        /// </summary>
        public const int FormatVersion = 1;
        /// <summary>The supported revision S: files up to it are read (older ones migrated), newer ones bound with
        /// NewerFormat. Revision 2 adds the clothes parts' inline <c>ext</c>, revision 3 (after 1.1.0) a character node's
        /// <c>state.nippleThroughClothes</c>, revision 4 (after 1.2.0) the scene's <c>timeline</c> and <c>constraints</c>, a character
        /// node's <c>advancedPose</c> and the <c>materialOverrides</c> of an item and of the map, revision 5 (after 1.3.x) a character
        /// node's <c>wet</c> and <c>ragdoll</c>, the scene's <c>ragdoll</c>, an item node's <c>bake</c> and <c>cut</c>, the scene's <c>scripts</c> and a Timeline's
        /// <c>script</c> (optional members; empty migrations).</summary>
        public const int SupportedVersion = 5;
        /// <summary>The revision of the clothes parts' inline <c>ext</c>: a file in which any part carries one is written as it.</summary>
        public const int ClothesPartExtVersion = 2;
        /// <summary>The revision of a character node's <c>state.nippleThroughClothes</c> (written only when false): a scene in
        /// which any character has it is written as it.</summary>
        public const int NippleThroughClothesVersion = 3;
        /// <summary>The revision of a scene's <c>timeline</c> (written only when the scene has a Timeline): a scene with one is
        /// written as it.</summary>
        public const int TimelineVersion = 4;
        /// <summary>The revision of a character node's <c>advancedPose</c> (written only when not empty): a scene in which any
        /// character has one is written as it.</summary>
        public const int AdvancedPoseVersion = 4;
        /// <summary>The revision of the <c>materialOverrides</c> of an item and of <c>scene.map</c> (written only when not empty): a
        /// scene in which either has one is written as it.</summary>
        public const int MaterialEditVersion = 4;
        /// <summary>The revision of a scene's <c>constraints</c> (written only when the list has an entry): a scene with one is
        /// written as it.</summary>
        public const int ConstraintsVersion = 4;
        /// <summary>The revision of a character node's <c>wet</c> (written only when not empty): a scene in which any character has one is
        /// written as it.</summary>
        public const int WetVersion = 5;
        /// <summary>The revision of a character node's <c>ragdoll</c> and of the scene's <c>ragdoll</c> (each written only when not empty): a scene with either is written as it.</summary>
        public const int RagdollVersion = 5;
        /// <summary>The revision of an item node's <c>bake</c> and <c>cut</c> (each written only when set): a scene in which any item has either is written as it.</summary>
        public const int BakeVersion = 5;
        /// <summary>The revision of a scene's <c>scripts</c> (written only when the scene has a script) and of its Timeline's <c>script</c> (written only
        /// when set): a scene with either is written as it.</summary>
        public const int ScriptVersion = 5;
        /// <summary>Written to <c>minReaderVersion</c>.</summary>
        public const int MinReaderVersion = 1;
        /// <summary>Written to <c>prerelease</c>: null since format v1 was frozen. Every later schema change is a new
        /// <see cref="FormatVersion"/> with a migration.</summary>
        public const string Prerelease = null;
        /// <summary>
        /// Pre-release values a frozen reader reads as format v1 with no finding: "dev.1", the value every writer put
        /// before the freeze (its schema is v1's). Any other non-null value is read best effort with info
        /// <c>manifest.prerelease</c>.
        /// </summary>
        public static readonly IReadOnlyList<string> AcceptedPrereleases = new[] { "dev.1" };

        /// <summary>True for null and the values of <see cref="AcceptedPrereleases"/> (ordinal).</summary>
        public static bool IsAcceptedPrerelease(string value)
        {
            if (value == null) return true;
            foreach (var a in AcceptedPrereleases)
                if (string.Equals(a, value, StringComparison.Ordinal)) return true;
            return false;
        }

        public const string AppName = "HS2Next";
        /// <summary>Default app version written to <c>app.version</c> (SaveOptions.AppVersion overrides it).</summary>
        public const string AppVersion = "0.1.0";

        public const string CharaExtension = ".hs2nchara";
        public const string CoordExtension = ".hs2ncoord";
        public const string SceneExtension = ".hs2nscene";
        public const string PoseExtension = ".hs2npose";
    }

    public sealed class Manifest
    {
        public string Format;
        /// <summary>Set by the writer to the lowest covering revision; the base revision by default.</summary>
        public int FormatVersion = SaveFormats.FormatVersion;
        public int MinReaderVersion = SaveFormats.MinReaderVersion;
        public string Prerelease = SaveFormats.Prerelease;
        public string AppName = SaveFormats.AppName;
        public string AppVersion = SaveFormats.AppVersion;
        public JsonLeftovers AppUnknown;
        public DateTime CreatedUtc;
        public DateTime ModifiedUtc;
        public string Title = "";
        public int? Sex;
        /// <summary>Null when the file has no PNG prefix.</summary>
        public int? ThumbnailWidth, ThumbnailHeight;
        public JsonLeftovers ThumbnailUnknown;
        public List<RequiresEntry> Requires = new List<RequiresEntry>();
        public List<ManifestExtEntry> Ext = new List<ManifestExtEntry>();
        public ImportProvenance Import;
        /// <summary>Scene only: character node id → provenance of that subtree.</summary>
        public SortedDictionary<string, ManifestChara> Charas = new SortedDictionary<string, ManifestChara>(StringComparer.Ordinal);
        public JsonLeftovers Unknown;

        public static Manifest Read(DocReader r)
        {
            var m = new Manifest
            {
                Format = r.Str("format"),
                FormatVersion = r.Int("formatVersion"),
                MinReaderVersion = r.Int("minReaderVersion"),
            };
            m.Prerelease = r.StrN("prerelease");
            var app = r.ObjReq("app");
            m.AppName = app.Str("name", "");
            m.AppVersion = app.Str("version", "");
            m.AppUnknown = app.Leftovers();
            m.CreatedUtc = r.Time("createdUtc");
            m.ModifiedUtc = r.Time("modifiedUtc");
            m.Title = r.Str("title", "");
            m.Sex = r.IntN("sex");
            var th = r.ObjN("thumbnail");
            if (th != null)
            {
                m.ThumbnailWidth = th.Int("width");
                m.ThumbnailHeight = th.Int("height");
                m.ThumbnailUnknown = th.Leftovers();
            }
            m.Requires = r.Array("requires", v => RequiresEntry.Read(v.AsObj()));
            m.Ext = r.Array("ext", v => ManifestExtEntry.Read(v.AsObj()));
            var imp = r.ObjN("import");
            if (imp != null) m.Import = ImportProvenance.Read(imp);
            var charas = r.Map("charas", v => ManifestChara.Read(v.AsObj()));
            foreach (var kv in charas) m.Charas[kv.Key] = kv.Value;
            m.Unknown = r.Leftovers();
            return m;
        }

        public void Write(CanonicalJsonWriter w)
        {
            w.StartObject();
            w.Str("format", Format);
            w.Int("formatVersion", FormatVersion);
            w.Int("minReaderVersion", MinReaderVersion);
            w.Str("prerelease", Prerelease);
            w.StartObject("app");
            w.Str("name", AppName);
            w.Str("version", AppVersion);
            w.Leftovers(AppUnknown);
            w.EndObject();
            w.Time("createdUtc", CreatedUtc);
            w.Time("modifiedUtc", ModifiedUtc);
            w.Str("title", Title ?? "");
            w.IntN("sex", Sex);
            w.Name("thumbnail");
            if (ThumbnailWidth.HasValue && ThumbnailHeight.HasValue)
            {
                w.StartObject();
                w.Int("width", ThumbnailWidth.Value);
                w.Int("height", ThumbnailHeight.Value);
                w.Leftovers(ThumbnailUnknown);
                w.EndObject();
            }
            else w.Null();
            w.StartArray("requires");
            foreach (var q in Requires) q.Write(w);
            w.EndArray();
            w.StartArray("ext");
            foreach (var e in Ext) e.Write(w);
            w.EndArray();
            w.Name("import");
            if (Import != null) Import.Write(w); else w.Null();
            w.Map("charas", Charas, (ww, c) => c.Write(ww));
            w.Leftovers(Unknown);
            w.EndObject();
        }
    }

    /// <summary>A `requires` item.</summary>
    public sealed class RequiresEntry
    {
        public string Guid;
        public string Name, Author, Website;
        public JsonLeftovers Unknown;

        public static RequiresEntry Read(DocReader r) => new RequiresEntry
        {
            Guid = r.Str("guid"),
            Name = r.StrN("name"),
            Author = r.StrN("author"),
            Website = r.StrN("website"),
            Unknown = r.Leftovers(),
        };

        public void Write(CanonicalJsonWriter w)
        {
            w.StartObject();
            w.Str("guid", Guid);
            w.Str("name", Name);
            w.Str("author", Author);
            w.Str("website", Website);
            w.Leftovers(Unknown);
            w.EndObject();
        }
    }

    /// <summary>A manifest `ext` entry: one per (namespace, scope, scope root) holding data.</summary>
    public sealed class ManifestExtEntry
    {
        public static readonly string[] ScopeNames = { "chara", "coord", "scene", "node" };

        public string Ns;
        /// <summary>"chara", "coord", "scene" or "node" (an unknown string is kept and reported).</summary>
        public string Scope;
        public string Root = "";
        public int Version;
        public bool Required;
        public JsonLeftovers Unknown;

        public static ManifestExtEntry Read(DocReader r) => new ManifestExtEntry
        {
            Ns = r.Str("ns"),
            Scope = r.EnumStr("scope", "", ScopeNames),
            Root = r.Str("root", ""),
            Version = r.Int("version", 0),
            Required = r.Bool("required", false),
            Unknown = r.Leftovers(),
        };

        public void Write(CanonicalJsonWriter w)
        {
            w.StartObject();
            w.Str("ns", Ns);
            w.Str("scope", Scope);
            w.Str("root", Root);
            w.Int("version", Version);
            w.Bool("required", Required);
            w.Leftovers(Unknown);
            w.EndObject();
        }

        public ManifestExtEntry Clone() => new ManifestExtEntry
        {
            Ns = Ns, Scope = Scope, Root = Root, Version = Version, Required = Required, Unknown = Unknown?.Clone(),
        };

        /// <summary>Sort key: (root, scope, ns) ordinal.</summary>
        public static int Compare(ManifestExtEntry a, ManifestExtEntry b)
        {
            int c = string.CompareOrdinal(a.Root, b.Root);
            if (c == 0) c = string.CompareOrdinal(a.Scope, b.Scope);
            if (c == 0) c = string.CompareOrdinal(a.Ns, b.Ns);
            return c;
        }
    }

    /// <summary>Import provenance.</summary>
    public sealed class ImportProvenance
    {
        public string From = "";
        public string FileName = "";
        public string Sha256 = "";
        public long FileSize;
        public DateTime ImportedUtc;
        public string Importer = "";
        public string Hs2Version = "";
        public string CoordSex = "";
        public List<string> SideloaderGuids = new List<string>();
        public JsonLeftovers Unknown;

        public static ImportProvenance Read(DocReader r) => new ImportProvenance
        {
            From = r.Str("from", ""),
            FileName = r.Str("fileName", ""),
            Sha256 = r.Str("sha256", ""),
            FileSize = r.Int64("fileSize", 0),
            ImportedUtc = r.Time("importedUtc", default),
            Importer = r.Str("importer", ""),
            Hs2Version = r.Str("hs2Version", ""),
            CoordSex = r.Str("coordSex", ""),
            SideloaderGuids = new List<string>(r.StrArray("sideloaderGuids", new string[0])),
            Unknown = r.Leftovers(),
        };

        public void Write(CanonicalJsonWriter w)
        {
            w.StartObject();
            w.Str("from", From);
            w.Str("fileName", FileName);
            w.Str("sha256", Sha256);
            w.Int64("fileSize", FileSize);
            w.Time("importedUtc", ImportedUtc);
            w.Str("importer", Importer);
            w.Str("hs2Version", Hs2Version);
            w.Str("coordSex", CoordSex);
            w.StrArray("sideloaderGuids", SideloaderGuids);
            w.Leftovers(Unknown);
            w.EndObject();
        }

        public ImportProvenance Clone()
        {
            var c = (ImportProvenance)MemberwiseClone();
            c.SideloaderGuids = new List<string>(SideloaderGuids);
            c.Unknown = Unknown?.Clone();
            return c;
        }
    }

    /// <summary>Value of manifest <c>charas[&lt;nodeId&gt;]</c> (scene only).</summary>
    public sealed class ManifestChara
    {
        public ImportProvenance Import;
        public JsonLeftovers Unknown;

        public static ManifestChara Read(DocReader r)
        {
            var c = new ManifestChara();
            var imp = r.ObjN("import");
            if (imp != null) c.Import = ImportProvenance.Read(imp);
            c.Unknown = r.Leftovers();
            return c;
        }

        public void Write(CanonicalJsonWriter w)
        {
            w.StartObject();
            w.Name("import");
            if (Import != null) Import.Write(w); else w.Null();
            w.Leftovers(Unknown);
            w.EndObject();
        }
    }

    /// <summary>`requires` order: OrdinalIgnoreCase, then Ordinal.</summary>
    public sealed class GuidOrder : IComparer<string>
    {
        public static readonly GuidOrder Instance = new GuidOrder();

        public int Compare(string a, string b)
        {
            int c = StringComparer.OrdinalIgnoreCase.Compare(a, b);
            return c != 0 ? c : string.CompareOrdinal(a, b);
        }
    }
}

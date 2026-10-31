// Shared load and save pipeline of the container kinds:
// chara, coord, scene, pose. LoadSession: raw entries → manifest checks (NotHs2Next, ReaderTooOld)
// → core documents parsed to JObjects → core migrations → resources hashed and re-keyed (references
// rewritten in the raw JSON) → binding → ext files grouped by scope instance and matched with the
// manifest ext entries → foreign entries kept. SaveSession: documents serialised (res references
// and `requires` GUIDs collected on the way) → ext files and manifest ext entries → reachable
// resources → manifest → atomic write. ContainerBase holds what every kind has: thumbnail, `res/`,
// foreign entries and the manifest fields that survive a load/save cycle.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HS2Next.Api.Save;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save
{
    /// <summary>What every container kind holds besides its documents.</summary>
    public abstract class ContainerBase
    {
        /// <summary>The PNG prefix as loaded or last saved (null = none yet).</summary>
        public byte[] Thumbnail;
        /// <summary>Embedded resources shared by every document of the file.</summary>
        public ResStore Res = new ResStore();
        /// <summary>Entries outside the core areas, kept byte-identical.</summary>
        public readonly SortedDictionary<string, byte[]> ForeignEntries = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        /// <summary>First save of this content; null until it is loaded or saved (then kept on later saves).</summary>
        public DateTime? CreatedUtc;
        /// <summary>The manifest as loaded (unknown members, `requires` details, the writing app), or null.</summary>
        public Manifest LoadedManifest;
        /// <summary>The loaded file's formatVersion was newer than this reader.</summary>
        public bool NewerFormat;

        public abstract string Format { get; }
        internal abstract string MainDocument { get; }

        /// <summary>The written file's bytes (no atomic save, no disk): for tests and in-memory copies.</summary>
        public byte[] ToBytes(SaveOptions options = null)
        {
            var o = options ?? new SaveOptions();
            var s = new SaveSession(o, this);
            Build(s);
            s.Finish(this);
            return s.Writer.ToBytes();
        }

        /// <summary>Atomic save to <paramref name="path"/>. Failures leave the target unchanged and are returned, not thrown.</summary>
        public SaveResult Save(string path, SaveOptions options = null)
        {
            var o = options ?? new SaveOptions();
            var result = new SaveResult { Downgraded = NewerFormat };
            var s = new SaveSession(o, this) { Findings = result.Findings };
            try
            {
                Build(s);
                s.Finish(this);
                s.Writer.Save(path, o.Verify, s.VerifyEntries, o.StreamWrapper);
                result.Size = new FileInfo(path).Length;
                CreatedUtc = s.CreatedUtc;
                Thumbnail = s.Writer.Thumbnail;
            }
            catch (SaveFormatException e)
            {
                result.Error = e;
                // A document writer that met an unexpected exception (ToDocumentBytes) reports it as BadDocument.
                var inner = e.InnerException;
                if (e.Code == SaveErrorCode.BadDocument && inner != null && !(inner is SaveFormatException))
                    result.Findings.Error("save.unexpectedException", SaveFormatException.Location(e.Entry, e.JsonPath), inner.GetType().FullName + ": " + inner.Message);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                result.Error = new SaveFormatException(SaveErrorCode.IoFailure, e.Message, null, null, 0, e);
            }
            catch (Exception e)
            {
                // Anything else is a model the format code does not guard against (or a bug in it): returned, never
                // thrown, so a save cannot take the caller down; the target is unchanged.
                result.Error = new SaveFormatException(SaveErrorCode.BadDocument, "unexpected " + e.GetType().Name + ": " + e.Message, null, null, 0, e);
                result.Findings.Error("save.unexpectedException", "", e.GetType().FullName + ": " + e.Message);
            }
            return result;
        }

        /// <summary>Adds the kind-specific documents, ext data and manifest fields to the session.</summary>
        internal abstract void Build(SaveSession s);

        /// <summary>Title written to the manifest.</summary>
        internal abstract string ManifestTitle { get; }
        /// <summary>Sex written to the manifest (null for scenes).</summary>
        internal abstract int? ManifestSex { get; }
        internal virtual ImportProvenance ManifestImport => null;

        internal void TakeCommon(LoadSession s)
        {
            Thumbnail = s.Raw.Thumbnail;
            Res = s.Res;
            CreatedUtc = s.Manifest.CreatedUtc;
            LoadedManifest = s.Manifest;
            NewerFormat = s.NewerFormat;
        }

        // ---------- loading (shared entry points) ----------

        internal static LoadResult<T> Run<T>(Func<ContainerReader> open, LoadOptions options, string expectedFormat, Func<LoadSession, T> bind) where T : class
        {
            var o = options ?? new LoadOptions();
            var result = new LoadResult<T>();
            try
            {
                var raw = open();
                result.Findings.AddRange(raw.Findings);
                var s = new LoadSession(raw, o, result.Findings, expectedFormat);
                result.Value = bind(s);
                result.NewerFormat = s.NewerFormat;
                result.MissingRequiredExt = s.MissingRequiredExt;
                if (s.MissingRequiredExt && o.FailOnMissingRequiredExt)
                {
                    result.Value = null;
                    result.Error = new SaveFormatException(SaveErrorCode.MissingRequiredExt, "a required ext block has no working handler");
                }
            }
            catch (SaveFormatException e) { result.Error = e; result.Value = null; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                result.Error = new SaveFormatException(SaveErrorCode.IoFailure, e.Message, null, null, 0, e);
                result.Value = null;
            }
            return result;
        }
    }

    /// <summary>Accessory holders of a coordinate document for the ext dispatcher.</summary>
    sealed class CoordAccessoryExt : IAccessoryExtAccess
    {
        readonly CoordDocument doc;
        public CoordAccessoryExt(CoordDocument doc) { this.doc = doc; }

        public IEnumerable<string> Ids
        {
            get { foreach (var a in doc.Accessories) if (a.Id != null) yield return a.Id; }
        }

        public ExtInline Get(string id, bool create)
        {
            var a = doc.Accessories.Find(x => x.Id == id);
            if (a == null) return null;
            if (a.Ext == null && create) a.Ext = new ExtInline();
            return a.Ext;
        }
    }

    /// <summary>Clothes part holders of a coordinate document for the ext dispatcher (by kind name).</summary>
    sealed class CoordClothesExt : IClothesExtAccess
    {
        readonly CoordDocument doc;
        public CoordClothesExt(CoordDocument doc) { this.doc = doc; }

        public IEnumerable<string> Kinds => ClothesSet.Names;

        public ExtInline Get(string kind, bool create)
        {
            int k = ClothesSet.IndexOf(kind);
            var p = k < 0 ? null : doc.Clothes?.Parts[k];
            if (p == null) return null;
            if (p.Ext == null && create) p.Ext = new ExtInline();
            return p.Ext;
        }
    }

    // ======================================================================================
    // Load
    // ======================================================================================

    sealed class LoadSession
    {
        public readonly ContainerReader Raw;
        public readonly LoadOptions Options;
        public readonly FindingList Findings;
        public readonly string Format;
        public readonly JObject ManifestJson;
        public Manifest Manifest;
        public readonly int FormatVersion;
        public readonly bool NewerFormat;
        public bool MissingRequiredExt;
        public readonly ResStore Res = new ResStore();
        readonly Dictionary<string, string> renames = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, JObject> docs = new Dictionary<string, JObject>(StringComparer.Ordinal);
        readonly HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<ManifestExtEntry> matchedExt = new HashSet<ManifestExtEntry>();
        readonly SortedSet<string> namespacesSeen = new SortedSet<string>(StringComparer.Ordinal);

        public LoadSession(ContainerReader raw, LoadOptions options, FindingList findings, string expectedFormat)
        {
            Raw = raw;
            Options = options;
            Findings = findings;

            // manifest.json exists, parses, format starts with "hs2next.".
            var me = raw.Get(EntryNames.Manifest);
            if (me == null) throw new SaveFormatException(SaveErrorCode.NotHs2Next, "no manifest.json");
            if (me.Data == null) throw new SaveFormatException(SaveErrorCode.EntryCorrupt, me.Error ?? "unreadable", EntryNames.Manifest);
            ManifestJson = DocReader.Parse(me.Data, EntryNames.Manifest, SaveErrorCode.BadManifest);
            used.Add(EntryNames.Manifest);
            var ft = ManifestJson["format"];
            Format = ft != null && ft.Type == JTokenType.String ? (string)ft : null;
            if (Format == null || !Format.StartsWith(SaveFormats.Prefix, StringComparison.Ordinal))
                throw new SaveFormatException(SaveErrorCode.NotHs2Next, "manifest format is not hs2next.*", EntryNames.Manifest, "format");
            if (expectedFormat != null && Format != expectedFormat)
                throw new SaveFormatException(SaveErrorCode.BadManifest, "format " + Format + ", expected " + expectedFormat, EntryNames.Manifest, "format");

            // minReaderVersion before anything else is bound.
            var mctx = new DocReadContext(EntryNames.Manifest, options.Lenient, findings, SaveErrorCode.BadManifest);
            var mr = new DocReader(ManifestJson, mctx);
            int minReader = mr.Int("minReaderVersion");
            int supported = options.MigrationsOrDefault.Supported;
            if (minReader > supported)
            {
                string app = (ManifestJson["app"] as JObject)?["version"]?.ToString() ?? "?";
                throw new SaveFormatException(SaveErrorCode.ReaderTooOld,
                    "the file needs format reader " + minReader + " (written by HS2Next " + app + "); this reader supports " + supported, EntryNames.Manifest);
            }
            FormatVersion = mr.Int("formatVersion");
            NewerFormat = FormatVersion > supported;
            if (NewerFormat) findings.Warning("manifest.newerFormat", EntryNames.Manifest, "format " + FormatVersion + " is newer than " + supported + "; unknown parts are kept");
            var pre = ManifestJson["prerelease"];
            if (pre != null && pre.Type == JTokenType.String && !SaveFormats.IsAcceptedPrerelease((string)pre))
                findings.Info("manifest.prerelease", EntryNames.Manifest, "pre-release file " + (string)pre + " read best effort");
        }

        /// <summary>Parses a core document. Missing → null (or MissingDocument when required); corrupt → EntryCorrupt.</summary>
        public JObject Doc(string name, bool required)
        {
            var e = Raw.Get(name);
            if (e == null)
            {
                if (required) throw new SaveFormatException(SaveErrorCode.MissingDocument, "missing document", name);
                return null;
            }
            if (e.Data == null) throw new SaveFormatException(SaveErrorCode.EntryCorrupt, e.Error ?? "unreadable", name);
            used.Add(name);
            var j = DocReader.Parse(e.Data, name);
            docs[name] = j;
            return j;
        }

        /// <summary>Runs migrations, loads `res/` and rewrites re-keyed references; call after every Doc() and before binding.</summary>
        public void Prepare()
        {
            var reg = Options.MigrationsOrDefault;
            if (FormatVersion < reg.Supported)
            {
                var ctx = new MigrationContext(Format, ManifestJson, docs, Findings, FormatVersion);
                reg.Upgrade(FormatVersion, ctx, Findings);
            }
            LoadRes();
            if (renames.Count > 0)
            {
                foreach (var j in docs.Values) Rewrite(j);
            }
            Manifest = Manifest.Read(new DocReader(ManifestJson, new DocReadContext(EntryNames.Manifest, Options.Lenient, Findings, SaveErrorCode.BadManifest)));
        }

        void LoadRes()
        {
            foreach (var e in Raw.Entries)
            {
                if (!e.Name.StartsWith(EntryNames.ResPrefix, StringComparison.Ordinal)) continue;
                used.Add(e.Name);
                if (!JsonText.IsResName(e.Name))
                {
                    Findings.Warning("res.badName", e.Name, "not a res/<sha256>.<ext> name; dropped on save");
                    continue;
                }
                if (e.Data == null)
                {
                    Findings.Warning("res.unreadable", e.Name, "resource unreadable (" + e.Error + "); references to it are kept dangling");
                    continue;
                }
                Res.LoadEntry(e.Name, e.Data, renames, Findings);
            }
        }

        void Rewrite(JToken t)
        {
            switch (t.Type)
            {
                case JTokenType.Object:
                    foreach (var p in ((JObject)t).Properties()) Rewrite(p.Value);
                    break;
                case JTokenType.Array:
                    foreach (var e in (JArray)t) Rewrite(e);
                    break;
                case JTokenType.String:
                    if (renames.TryGetValue((string)t, out var n)) ((JValue)t).Value = n;
                    break;
            }
        }

        public DocReadContext Ctx(string entry) => new DocReadContext(entry, Options.Lenient, Findings);

        public DocReader Reader(string entry) => docs.TryGetValue(entry, out var j) ? new DocReader(j, Ctx(entry)) : null;

        /// <summary>Marks entries as part of the core content (not foreign, not orphans).</summary>
        public void Use(string name) => used.Add(name);

        /// <summary>
        /// The ext files under <paramref name="fileRoot"/>ext/: namespace → (path relative to ext/&lt;ns&gt;/ → bytes). A corrupt
        /// file drops its whole block (error finding); the namespace is then listed in <paramref name="dropped"/>.
        /// Malformed namespace directories are orphans.
        /// </summary>
        public SortedDictionary<string, SortedDictionary<string, byte[]>> ExtFiles(string fileRoot, ISet<string> dropped)
        {
            var o = new SortedDictionary<string, SortedDictionary<string, byte[]>>(StringComparer.Ordinal);
            string prefix = fileRoot + EntryNames.ExtDir;
            var broken = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in Raw.Entries)
            {
                if (!e.Name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string rest = e.Name.Substring(prefix.Length);
                int slash = rest.IndexOf('/');
                if (slash <= 0 || slash == rest.Length - 1) continue;              // orphan (file directly in ext/)
                string ns = rest.Substring(0, slash);
                if (!ExtNamespaces.IsValid(ns) || !e.Conforming) continue;          // orphan / non-conforming
                used.Add(e.Name);
                namespacesSeen.Add(ns);
                if (e.Data == null)
                {
                    if (broken.Add(ns))
                        Findings.Error("EntryCorrupt", e.Name, "ext file unreadable (" + e.Error + "); the block " + ns + " is dropped");
                    continue;
                }
                if (!o.TryGetValue(ns, out var files)) o[ns] = files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
                files[rest.Substring(slash + 1)] = e.Data;
            }
            foreach (var ns in broken) { o.Remove(ns); dropped?.Add(ns); }
            return o;
        }

        /// <summary>
        /// Builds the file-backed ext data of one scope instance: blocks from files and inline data,
        /// version/required from the matching manifest entry (data without an entry → version 0, reported).
        /// </summary>
        public ExtScopeData BuildScope(ExtScope scope, string scopeName, string manifestRoot, string fileRoot, ExtInline main, IAccessoryExtAccess acc,
            IClothesExtAccess clothes = null)
        {
            var data = new ExtScopeData(scope);
            var dropped = new HashSet<string>(StringComparer.Ordinal);
            var files = ExtFiles(fileRoot, dropped);
            foreach (var ns in dropped)
            {
                main?.Remove(ns);
                if (acc != null) foreach (var id in acc.Ids) acc.Get(id, false)?.Remove(ns);
                if (clothes != null) foreach (var kind in clothes.Kinds) clothes.Get(kind, false)?.Remove(ns);
            }
            var nsSet = new SortedSet<string>(files.Keys, StringComparer.Ordinal);
            if (main != null) foreach (var ns in main.Namespaces) nsSet.Add(ns);
            if (acc != null)
                foreach (var id in acc.Ids)
                {
                    var h = acc.Get(id, false);
                    if (h != null) foreach (var ns in h.Namespaces) nsSet.Add(ns);
                }
            if (clothes != null)
                foreach (var kind in clothes.Kinds)
                {
                    var h = clothes.Get(kind, false);
                    if (h != null) foreach (var ns in h.Namespaces) nsSet.Add(ns);
                }
            foreach (var ns in nsSet) namespacesSeen.Add(ns);
            foreach (var ns in nsSet)
            {
                var entry = FindExtEntry(ns, scopeName, manifestRoot);
                if (ns == ExtNamespaces.Kkex)
                {
                    if (files.TryGetValue(ns, out var kf))
                        data.Kkex = KkexStore.Load(kf, fileRoot + "ext/" + ns + "/", Findings, Options.Lenient);
                    continue;
                }
                var b = data.GetOrAdd(ns);
                if (files.TryGetValue(ns, out var f)) foreach (var kv in f) b.Files[kv.Key] = kv.Value;
                if (entry != null)
                {
                    b.Version = entry.Version;
                    b.Required = entry.Required;
                    b.ManifestUnknown = entry.Unknown;
                    b.HadManifestEntry = true;
                }
                else Findings.Info("ext.noManifestEntry", "ext:" + ns + "@" + fileRoot, "ext data without a manifest entry: version 0, not required");
            }
            return data;
        }

        ManifestExtEntry FindExtEntry(string ns, string scopeName, string root)
        {
            foreach (var e in Manifest.Ext)
            {
                if (e.Ns == ns && e.Scope == scopeName && e.Root == root)
                {
                    matchedExt.Add(e);
                    return e;
                }
            }
            return null;
        }

        /// <summary>Runs the ext Read dispatch over the instances ( order).</summary>
        public void Dispatch(IEnumerable<ExtInstance> instances)
        {
            if (ExtDispatcher.Read(instances, Options.RegistryOrDefault, Findings)) MissingRequiredExt = true;
        }

        /// <summary>
        /// Final pass: unmatched manifest ext entries (dropped at save), namespaces differing only in case, foreign
        /// entries (kept) and orphans in core areas (dropped).
        /// </summary>
        public void Finish(ContainerBase c, Func<string, bool> isCoreArea)
        {
            foreach (var e in Manifest.Ext)
                if (!matchedExt.Contains(e))
                    Findings.Info("ext.entryWithoutData", EntryNames.Manifest, "manifest ext entry " + e.Ns + " (" + e.Scope + ", \"" + e.Root + "\") has no data; dropped on save");
            string prev = null;
            foreach (var ns in namespacesSeen)
            {
                if (prev != null && string.Equals(prev, ns, StringComparison.OrdinalIgnoreCase))
                    Findings.Warning("ext.namespaceCase", "", "ext namespaces differ only in case: " + prev + ", " + ns);
                prev = ns;
            }
            int foreign = 0;
            foreach (var e in Raw.Entries)
            {
                if (used.Contains(e.Name)) continue;
                if (isCoreArea(e.Name))
                {
                    Findings.Warning("container.orphan", e.Name, "entry in a core area that nothing uses; dropped on save");
                    continue;
                }
                if (!e.Conforming)
                {
                    Findings.Warning("NonConformingName", e.Name, "foreign entry name outside the entry-name grammar; dropped on save");
                    continue;
                }
                if (e.Data == null)
                {
                    Findings.Error("EntryCorrupt", e.Name, "foreign entry unreadable (" + e.Error + "); dropped");
                    continue;
                }
                c.ForeignEntries[e.Name] = e.Data;
                foreign++;
            }
            if (foreign > 0) Findings.Info("container.foreignEntries", "", foreign + " foreign entr" + (foreign == 1 ? "y" : "ies") + " kept");
        }
    }

    // ======================================================================================
    // Save
    // ======================================================================================

    sealed class SaveSession
    {
        public readonly SaveOptions Options;
        public FindingList Findings = new FindingList();
        public readonly ContainerWriter Writer = new ContainerWriter();
        public readonly DateTime Now;
        public DateTime CreatedUtc;
        public readonly List<string> VerifyEntries = new List<string> { EntryNames.Manifest };
        readonly HashSet<string> guids = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> named = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> reachable = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, ManifestExtEntry> extEntries = new Dictionary<string, ManifestExtEntry>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, ManifestChara> Charas = new SortedDictionary<string, ManifestChara>(StringComparer.Ordinal);
        /// <summary>The <c>formatVersion</c> written: the lowest revision whose schema covers what the file holds (the base
        /// revision unless a document uses a later member; raised by <see cref="Covers(CoordDocument)"/>).</summary>
        public int WriteFormatVersion = SaveFormats.FormatVersion;

        public SaveSession(SaveOptions options, ContainerBase c)
        {
            Options = options;
            Now = options.Now;
            CreatedUtc = c.CreatedUtc.HasValue ? JsonText.TruncateToSeconds(c.CreatedUtc.Value) : Now;
            Writer.MainDocument = c.MainDocument;
        }

        /// <summary>
        /// Lists a named requirement in `requires`: a feature of this app the file uses that an older reader lacks (an older
        /// app then reports it as required). Unlike the collected GUIDs it is listed although its namespace is reserved.
        /// </summary>
        public void AddRequirement(string name)
        {
            if (!string.IsNullOrEmpty(name)) named.Add(name);
        }

        /// <summary>Serialises a core document (refs → requires, res strings → reachable).</summary>
        public void AddDoc(string name, Action<CanonicalJsonWriter> write)
        {
            var bytes = CanonicalJsonWriter.ToDocumentBytes(name, write, guids);
            ResStore.ScanReferences(bytes, reachable);
            Writer.Add(name, bytes);
            VerifyEntries.Add(name);
        }

        /// <summary>
        /// Writes the ext data of one scope instance: block files under <paramref name="fileRoot"/>ext/&lt;ns&gt;/, the
        /// `hs2.kkex` store, and a manifest entry for every namespace that holds data.
        /// </summary>
        public void AddExt(ExtScopeData data, string scopeName, string manifestRoot, string fileRoot, ExtInline main, IAccessoryExtAccess acc,
            IClothesExtAccess clothes = null)
        {
            var nsWithData = new SortedSet<string>(StringComparer.Ordinal);
            if (main != null) foreach (var ns in main.Namespaces) nsWithData.Add(ns);
            if (acc != null)
                foreach (var id in acc.Ids)
                {
                    var h = acc.Get(id, false);
                    if (h != null) foreach (var ns in h.Namespaces) nsWithData.Add(ns);
                }
            if (clothes != null)
                foreach (var kind in clothes.Kinds)
                {
                    var h = clothes.Get(kind, false);
                    if (h != null) foreach (var ns in h.Namespaces) nsWithData.Add(ns);
                }
            foreach (var kv in data.Blocks)
            {
                if (kv.Value.Files.Count == 0) continue;
                nsWithData.Add(kv.Key);
                foreach (var f in kv.Value.Files) Writer.Add(fileRoot + EntryNames.ExtDir + kv.Key + "/" + f.Key, f.Value);
            }
            if (data.Kkex != null && !data.Kkex.IsEmpty)
            {
                var store = data.Kkex;
                if (Options.StripImportSources)
                {
                    store = store.Clone();
                    int n = store.StripImportSources();
                    if (n > 0) Findings.Info("kkex.stripped", fileRoot + "ext/hs2.kkex/", n + " source entries stripped");
                }
                var files = store.Save();
                if (files.Count > 0)
                {
                    foreach (var f in files) Writer.Add(fileRoot + EntryNames.ExtDir + ExtNamespaces.Kkex + "/" + f.Key, f.Value);
                    AddExtEntry(ExtNamespaces.Kkex, scopeName, manifestRoot, KkexStore.ManifestVersion, false, null);
                }
            }
            nsWithData.Remove(ExtNamespaces.Kkex);
            foreach (var ns in nsWithData)
            {
                var b = data.Get(ns);
                AddExtEntry(ns, scopeName, manifestRoot, b?.Version ?? 0, b?.Required ?? false, b?.ManifestUnknown);
            }
        }

        /// <summary>Raises <see cref="WriteFormatVersion"/> to what a coordinate document needs: format 2 when a clothes part
        /// carries inline ext.</summary>
        public void Covers(CoordDocument coord)
        {
            if (coord?.Clothes != null && coord.Clothes.HasPartExt()) WriteFormatVersion = Math.Max(WriteFormatVersion, SaveFormats.ClothesPartExtVersion);
        }

        void AddExtEntry(string ns, string scope, string root, int version, bool required, JsonLeftovers unknown)
        {
            string key = root + "\u0000" + scope + "\u0000" + ns;
            if (extEntries.TryGetValue(key, out var e))
            {
                e.Required |= required;      // node scope: one entry covers every node
                return;
            }
            extEntries[key] = new ManifestExtEntry { Ns = ns, Scope = scope, Root = root, Version = version, Required = required, Unknown = unknown?.Clone() };
        }

        /// <summary>Last step: resources, foreign entries, thumbnail and manifest.</summary>
        public void Finish(ContainerBase c)
        {
            foreach (var kv in c.Res.Collect(reachable, Findings)) Writer.Add(kv.Key, kv.Value);
            foreach (var kv in c.ForeignEntries)
            {
                if (Writer.Contains(kv.Key)) { Findings.Warning("container.foreignClash", kv.Key, "foreign entry shadowed by a core entry; dropped"); continue; }
                Writer.Add(kv.Key, kv.Value);
            }
            Writer.Thumbnail = MakeThumbnail(c);
            var h = PngInfo.ReadHeader(Writer.Thumbnail);

            var m = new Manifest
            {
                Format = c.Format,
                FormatVersion = WriteFormatVersion,
                AppVersion = Options.AppVersion ?? SaveFormats.AppVersion,
                CreatedUtc = CreatedUtc,
                ModifiedUtc = Now,
                Title = c.ManifestTitle ?? "",
                Sex = c.ManifestSex,
                ThumbnailWidth = h?.Width,
                ThumbnailHeight = h?.Height,
                Import = c.ManifestImport,
            };
            var old = c.LoadedManifest;
            if (old != null)
            {
                m.Unknown = old.Unknown;
                m.AppUnknown = old.AppUnknown;
                m.ThumbnailUnknown = old.ThumbnailUnknown;
            }
            m.Requires = BuildRequires(old);
            var ext = new List<ManifestExtEntry>(extEntries.Values);
            ext.Sort(ManifestExtEntry.Compare);
            m.Ext = ext;
            foreach (var kv in Charas) m.Charas[kv.Key] = kv.Value;
            Writer.Add(EntryNames.Manifest, CanonicalJsonWriter.ToBytes(EntryNames.Manifest, m.Write));
            Writer.CheckLimits();
        }

        byte[] MakeThumbnail(ContainerBase c)
        {
            byte[] png = null;
            switch (Options.Thumbnail)
            {
                case ThumbnailMode.Render:
                    if (Options.Renderer != null)
                    {
                        png = Options.Renderer();
                        if (png != null && PngInfo.ReadHeader(png) == null)
                        {
                            Findings.Warning("thumbnail.invalid", "", "the renderer returned bytes that are not a PNG; previous thumbnail kept");
                            png = null;
                        }
                    }
                    png ??= c.Thumbnail;
                    break;
                case ThumbnailMode.Keep:
                    png = c.Thumbnail;
                    break;
            }
            if (png != null && PngInfo.ReadHeader(png) == null) png = null;
            return png ?? ThumbnailPlaceholder.Png();
        }

        static bool IsReservedGuid(string g) => ExtNamespaces.IsReserved(g);

        List<RequiresEntry> BuildRequires(Manifest old)
        {
            var list = new List<string>();
            foreach (var g in guids) if (!IsReservedGuid(g)) list.Add(g);
            list.AddRange(named);
            list.Sort(GuidOrder.Instance);
            var o = new List<RequiresEntry>();
            string prev = null;
            foreach (var g in list)
            {
                if (prev != null && string.Equals(prev, g, StringComparison.OrdinalIgnoreCase)) continue;
                prev = g;
                RequiresEntry info = Options.RequiresInfo?.Invoke(g);
                if (info == null && old != null)
                    foreach (var r in old.Requires)
                        if (string.Equals(r.Guid, g, StringComparison.OrdinalIgnoreCase)) { info = r; break; }
                o.Add(new RequiresEntry { Guid = g, Name = info?.Name, Author = info?.Author, Website = info?.Website, Unknown = info?.Unknown });
            }
            return o;
        }
    }
}

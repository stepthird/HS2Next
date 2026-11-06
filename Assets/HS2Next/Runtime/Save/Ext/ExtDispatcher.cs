// Calls ext block handlers while a container loads and saves.
// One ExtInstance per scope instance (scene, each node, each character's chara and coord scopes),
// visited in the order the container hands them over, namespaces in ordinal order.
// Read: data without a handler, declined data and data whose handler threw stay "unknown" and are
// written back unchanged. Write: every registered handler is offered every instance of its scopes
// except instances whose block is unknown; an exception keeps the block as loaded. Every call into
// a handler is wrapped (fault isolation); `hs2.kkex` is core data and never dispatched: handlers that are also
// IKkexConsumer get read-only copies of the imported entries of the plugin ids they list, after the instance's Read calls.
// ReadAgain is the same pass limited to some namespaces (a plugin enabled while the app runs reads the open scene's blocks
// of its namespaces, including those kept as unknown because no handler existed when they were read).
using System;
using System.Collections.Generic;
using System.IO;
using HS2Next.Api.Save;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save
{
    /// <summary>Access to the per-accessory inline ext holders of a coordinate (by accessory id).</summary>
    public interface IAccessoryExtAccess
    {
        /// <summary>Accessory ids in list order.</summary>
        IEnumerable<string> Ids { get; }
        /// <summary>The holder of an accessory; created when <paramref name="create"/> and missing; null for an unknown id or a missing holder.</summary>
        ExtInline Get(string id, bool create);
    }

    /// <summary>Access to the per-part inline ext holders of a coordinate's clothes (by kind name: top … shoes).</summary>
    public interface IClothesExtAccess
    {
        /// <summary>The kind names in kind order.</summary>
        IEnumerable<string> Kinds { get; }
        /// <summary>The holder of a part; created when <paramref name="create"/> and missing; null for an unknown kind or a missing holder.</summary>
        ExtInline Get(string kind, bool create);
    }

    /// <summary>One scope instance handed to the dispatcher.</summary>
    public sealed class ExtInstance
    {
        public ExtScope Scope;
        /// <summary>Node id (node scope, and chara/coord scopes of a scene character), else null.</summary>
        public string OwnerId;
        public string ContainerFormat;
        /// <summary>Manifest root of this instance ("" , "coord/", "charas/&lt;id&gt;/", …) for locations in findings.</summary>
        public string Root;
        public ExtScopeData Data;
        /// <summary>Inline holder of the scope's main object (chara/coord/scene root, or the node).</summary>
        public ExtInline Main;
        /// <summary>Coord scope: the accessories' holders; else null.</summary>
        public IAccessoryExtAccess Accessories;
        /// <summary>Coord scope: the clothes parts' holders; else null.</summary>
        public IClothesExtAccess Clothes;

        internal string Where(string ns) => "ext:" + ns + "@" + (OwnerId != null && Scope == ExtScope.Node ? "nodes/" + OwnerId + "/" : Root);

        /// <summary>Namespaces with data in this instance: inline (main, any accessory or any clothes part) or blocks; `hs2.kkex` excluded.</summary>
        public SortedSet<string> NamespacesWithData()
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);
            if (Main != null) foreach (var ns in Main.Namespaces) set.Add(ns);
            if (Accessories != null)
                foreach (var id in Accessories.Ids)
                {
                    var h = Accessories.Get(id, false);
                    if (h != null) foreach (var ns in h.Namespaces) set.Add(ns);
                }
            if (Clothes != null)
                foreach (var kind in Clothes.Kinds)
                {
                    var h = Clothes.Get(kind, false);
                    if (h != null) foreach (var ns in h.Namespaces) set.Add(ns);
                }
            foreach (var kv in Data.Blocks) set.Add(kv.Key);
            set.Remove(ExtNamespaces.Kkex);
            return set;
        }
    }

    public static class ExtDispatcher
    {
        // ---------- read ----------

        /// <summary>Runs Read for every block of every instance (in the given order). Returns true if an unknown block is marked required.</summary>
        public static bool Read(IEnumerable<ExtInstance> instances, ExtRegistry registry, FindingList findings) =>
            ReadCore(instances, registry, findings, null, out _);

        /// <summary>
        /// Read again, for the namespaces of <paramref name="namespaces"/> only: every instance's block of those namespaces
        /// is offered to its handler again (a block kept as unknown is decided again), and the handlers of those namespaces
        /// that are kkex consumers get the instance's imported entries again. Other namespaces are not touched. Returns the
        /// number of blocks a handler read (not declined, not thrown).
        /// </summary>
        public static int ReadAgain(IEnumerable<ExtInstance> instances, ExtRegistry registry, ICollection<string> namespaces, FindingList findings)
        {
            if (registry == null || namespaces == null || namespaces.Count == 0) return 0;
            ReadCore(instances, registry, findings, new HashSet<string>(namespaces, StringComparer.Ordinal), out int read);
            return read;
        }

        static bool ReadCore(IEnumerable<ExtInstance> instances, ExtRegistry registry, FindingList findings, HashSet<string> only, out int read)
        {
            bool missingRequired = false;
            read = 0;
            foreach (var inst in instances)
            {
                foreach (var ns in inst.NamespacesWithData())
                {
                    if (only != null && !only.Contains(ns)) continue;
                    var block = inst.Data.GetOrAdd(ns);
                    var handler = registry?.Get(ns);
                    bool scopeOk = handler != null && (SafeScopes(handler, registry, findings, inst) & inst.Scope) != 0;
                    if (!scopeOk || registry.IsFaulted(ns))
                    {
                        block.IsUnknown = true;
                        block.UnknownReason = handler == null ? "no handler" : !scopeOk ? "handler does not take scope " + inst.Scope : "handler faulted";
                        findings.Info("ext.unknown", inst.Where(ns), "ext block kept unchanged (" + block.UnknownReason + ")");
                    }
                    else
                    {
                        var ctx = new ExtReadContext(inst, ns, block, findings);
                        try
                        {
                            handler.Read(ctx);
                            if (ctx.DeclineReason != null)
                            {
                                block.IsUnknown = true;
                                block.UnknownReason = "declined: " + ctx.DeclineReason;
                                findings.Info("ext.declined", inst.Where(ns), "handler declined the block (" + ctx.DeclineReason + "); kept unchanged");
                            }
                            else
                            {
                                block.IsUnknown = false;
                                block.UnknownReason = null;
                                read++;
                            }
                        }
                        catch (Exception ex)
                        {
                            block.IsUnknown = true;
                            block.UnknownReason = "Read threw " + ex.GetType().Name;
                            Fault(registry, ns, "Read", ex, findings, inst);
                        }
                    }
                    if (block.IsUnknown && block.Required)
                    {
                        missingRequired = true;
                        findings.Warning("ext.missingRequired", inst.Where(ns), "a required ext block has no working handler; the content may be incomplete");
                    }
                }
                ReadKkex(inst, registry, findings, only);
            }
            return missingRequired;
        }

        /// <summary>
        /// The read-only window on the instance's imported plugin data: every registered handler that is an
        /// <see cref="IKkexConsumer"/>, takes the instance's scope and is not faulted gets one ReadKkex call per entry whose
        /// id it lists (handlers in namespace ordinal order, entries in the stored order), after the Read calls of the
        /// instance. The store itself is never handed out: a context holds copies, so it stays byte-equal whatever the
        /// consumer does; a throw is a fault of the handler.
        /// </summary>
        static void ReadKkex(ExtInstance inst, ExtRegistry registry, FindingList findings, HashSet<string> only)
        {
            var store = inst.Data?.Kkex;
            if (registry == null || store == null || store.Entries.Count == 0) return;
            if (inst.Scope != ExtScope.Chara && inst.Scope != ExtScope.Coord) return;
            List<KkexEntry> sorted = null;
            foreach (var h in registry.Handlers)
            {
                if (!(h is IKkexConsumer consumer)) continue;
                string ns = h.Namespace;
                if (only != null && !only.Contains(ns)) continue;
                if (registry.IsFaulted(ns) || (SafeScopes(h, registry, findings, inst) & inst.Scope) == 0) continue;
                IReadOnlyList<string> ids;
                try { ids = consumer.KkexIds; }
                catch (Exception ex)
                {
                    Fault(registry, ns, "KkexIds", ex, findings, inst);
                    continue;
                }
                if (ids == null || ids.Count == 0) continue;
                var wanted = new HashSet<string>(StringComparer.Ordinal);
                foreach (var id in ids) if (id != null) wanted.Add(id);
                if (sorted == null)
                {
                    sorted = new List<KkexEntry>(store.Entries);
                    sorted.Sort(KkexEntry.Compare);
                }
                foreach (var e in sorted)
                {
                    if (e.Id == null || !wanted.Contains(e.Id)) continue;
                    if (registry.IsFaulted(ns)) break;
                    store.Files.TryGetValue(e.File ?? "", out var data);
                    try { consumer.ReadKkex(new KkexReadContext(inst, ns, e, data, findings)); }
                    catch (Exception ex) { Fault(registry, ns, "ReadKkex", ex, findings, inst); }
                }
            }
        }

        static ExtScope SafeScopes(IExtBlockHandler h, ExtRegistry registry, FindingList findings, ExtInstance inst)
        {
            try { return h.Scopes; }
            catch (Exception ex) { Fault(registry, h.Namespace, "Scopes", ex, findings, inst); return 0; }
        }

        static void Fault(ExtRegistry registry, string ns, string hook, Exception ex, FindingList findings, ExtInstance inst)
        {
            findings.Error("ext.handlerFault", inst.Where(ns), "handler threw in " + hook + ": " + ex.GetType().Name + ": " + ex.Message);
            if (registry != null && registry.Fault(ns, hook, ex))
                findings.Error("ext.handlerFaulted", inst.Where(ns), "handler disabled after " + ExtRegistry.MaxFaults + " faults");
        }

        // ---------- write ----------

        /// <summary>Offers every registered handler every instance of its scopes (unknown blocks excepted) and applies what it writes.</summary>
        public static void Write(IEnumerable<ExtInstance> instances, ExtRegistry registry, FindingList findings)
        {
            if (registry == null) return;
            var handlers = registry.Handlers;
            foreach (var inst in instances)
            {
                foreach (var h in handlers)
                {
                    string ns = h.Namespace;
                    if (ns == ExtNamespaces.Kkex || registry.IsFaulted(ns)) continue;
                    if ((SafeScopes(h, registry, findings, inst) & inst.Scope) == 0) continue;
                    var block = inst.Data.Get(ns);
                    if (block != null && block.IsUnknown) continue;
                    ExtWriteContext ctx;
                    int version;
                    try
                    {
                        bool req = h.RequiredByDefault;
                        version = h.Version;
                        ctx = new ExtWriteContext(inst, ns, req, findings);
                        h.Write(ctx);
                    }
                    catch (Exception ex)
                    {
                        Fault(registry, ns, "Write", ex, findings, inst);
                        continue;
                    }
                    try { Apply(inst, ns, ctx, version, findings); }
                    catch (ArgumentException ex)
                    {
                        // e.g. the document already holds a namespace that differs only in case; keep what was loaded
                        findings.Error("ext.applyFailed", inst.Where(ns), "handler output not applied: " + ex.Message);
                    }
                }
            }
        }

        /// <summary>
        /// For core handlers whose value is the document's own inline value (the node scope's <c>hs2.pose</c>, set only by
        /// a scene command): makes <paramref name="ctx"/> write back what the instance holds for its namespace as an unknown
        /// block is written: the current inline value of the main object (the token itself, token for token), the
        /// block's files and the stored <c>required</c> flag of a block that was read. So every save path keeps the value:
        /// a scene session's save and a direct <c>SceneContainer.Save</c> / <c>ToBytes</c> alike. Returns false when the
        /// instance holds nothing (the block is then removed, as for any handler that writes nothing) or the context is not
        /// one of this dispatcher's. Not part of the Api.
        /// </summary>
        internal static bool KeepInline(IExtWriteContext ctx)
        {
            if (!(ctx is ExtWriteContext w)) return false;
            var t = w.Instance.Main?.Get(w.Namespace);
            var b = w.Instance.Data?.Get(w.Namespace);
            if (t != null) w.MainJson = t;
            if (b != null)
            {
                foreach (var kv in b.Files) w.FileData[kv.Key] = kv.Value;
                if (b.HadManifestEntry) w.Required = b.Required;
            }
            return t != null || w.FileData.Count > 0;
        }

        static void Apply(ExtInstance inst, string ns, ExtWriteContext ctx, int version, FindingList findings)
        {
            bool any = ctx.MainJson != null || ctx.AccessoryValues.Count > 0 || ctx.ClothesValues.Count > 0 || ctx.FileData.Count > 0;
            inst.Main?.SetWithoutSizeCheck(ns, ctx.MainJson);   // size checked by SetJson
            if (inst.Accessories != null)
            {
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var id in inst.Accessories.Ids)
                {
                    ids.Add(id);
                    bool has = ctx.AccessoryValues.TryGetValue(id, out var tok);
                    var holder = inst.Accessories.Get(id, has);
                    if (has) holder.SetWithoutSizeCheck(ns, tok);
                    else holder?.Remove(ns);
                }
                foreach (var id in ctx.AccessoryValues.Keys)
                    if (!ids.Contains(id)) findings.Warning("ext.unknownAccessory", inst.Where(ns), "handler wrote data for accessory " + id + ", which is not in the coordinate; ignored");
            }
            else if (ctx.AccessoryValues.Count > 0)
                findings.Warning("ext.unknownAccessory", inst.Where(ns), "accessory data outside the coord scope ignored");
            if (inst.Clothes != null)
            {
                foreach (var kind in inst.Clothes.Kinds)
                {
                    bool has = ctx.ClothesValues.TryGetValue(kind, out var tok);
                    var holder = inst.Clothes.Get(kind, has);
                    if (has) holder.SetWithoutSizeCheck(ns, tok);
                    else holder?.Remove(ns);
                }
            }
            else if (ctx.ClothesValues.Count > 0)
                findings.Warning("ext.unknownClothes", inst.Where(ns), "clothes part data where the instance has no clothes ignored");
            if (!any)
            {
                inst.Data.Blocks.Remove(ns);
                return;
            }
            var block = inst.Data.GetOrAdd(ns);
            block.Files.Clear();
            foreach (var kv in ctx.FileData) block.Files[kv.Key] = kv.Value;
            block.Version = version;
            block.Required = ctx.Required;
            block.IsUnknown = false;
            block.UnknownReason = null;
        }

        // ---------- contexts ----------

        abstract class ContextBase : IExtContext
        {
            protected readonly ExtInstance Inst;
            protected readonly string Ns;
            readonly FindingList findings;

            protected ContextBase(ExtInstance inst, string ns, FindingList findings)
            {
                Inst = inst;
                Ns = ns;
                this.findings = findings;
            }

            public ExtScope Scope => Inst.Scope;
            public string OwnerId => Inst.OwnerId;
            public string ContainerFormat => Inst.ContainerFormat;

            public void Report(ExtSeverity severity, string code, string message) =>
                findings.Add((FindingSeverity)(int)severity, "ext." + Ns + "." + code, Inst.Where(Ns), message);
        }

        sealed class ExtReadContext : ContextBase, IExtReadContext
        {
            readonly ExtBlock block;
            readonly List<string> files;
            Dictionary<string, string> accessoryJson, clothesJson;
            string json;
            bool jsonDone;
            public string DeclineReason;

            public ExtReadContext(ExtInstance inst, string ns, ExtBlock block, FindingList findings) : base(inst, ns, findings)
            {
                this.block = block;
                files = new List<string>(block.Files.Keys);
            }

            public int StoredVersion => block.Version;
            public bool StoredRequired => block.Required;

            public string Json
            {
                get
                {
                    if (!jsonDone)
                    {
                        var t = Inst.Main?.Get(Ns);
                        json = t == null ? null : CanonicalJsonWriter.ToText(t);
                        jsonDone = true;
                    }
                    return json;
                }
            }

            public IReadOnlyDictionary<string, string> AccessoryJson
            {
                get
                {
                    if (accessoryJson == null)
                    {
                        accessoryJson = new Dictionary<string, string>(StringComparer.Ordinal);
                        if (Inst.Accessories != null)
                            foreach (var id in Inst.Accessories.Ids)
                            {
                                var t = Inst.Accessories.Get(id, false)?.Get(Ns);
                                if (t != null) accessoryJson[id] = CanonicalJsonWriter.ToText(t);
                            }
                    }
                    return accessoryJson;
                }
            }

            public IReadOnlyDictionary<string, string> ClothesJson
            {
                get
                {
                    if (clothesJson == null)
                    {
                        clothesJson = new Dictionary<string, string>(StringComparer.Ordinal);
                        if (Inst.Clothes != null)
                            foreach (var kind in Inst.Clothes.Kinds)
                            {
                                var t = Inst.Clothes.Get(kind, false)?.Get(Ns);
                                if (t != null) clothesJson[kind] = CanonicalJsonWriter.ToText(t);
                            }
                    }
                    return clothesJson;
                }
            }

            public IReadOnlyList<string> Files => files;

            public byte[] ReadFile(string path)
            {
                if (path == null || !block.Files.TryGetValue(path, out var b)) throw new KeyNotFoundException("no ext file " + path);
                return (byte[])b.Clone();
            }

            public Stream OpenFile(string path)
            {
                if (path == null || !block.Files.TryGetValue(path, out var b)) throw new KeyNotFoundException("no ext file " + path);
                return new MemoryStream(b, false);
            }

            public void Decline(string reason) => DeclineReason = string.IsNullOrEmpty(reason) ? "declined" : reason;
        }

        sealed class KkexReadContext : ContextBase, IKkexReadContext
        {
            readonly KkexEntry entry;
            readonly byte[] data;

            public KkexReadContext(ExtInstance inst, string ns, KkexEntry entry, byte[] data, FindingList findings) : base(inst, ns, findings)
            {
                this.entry = entry;
                this.data = data ?? Array.Empty<byte>();
            }

            public string Id => entry.Id;
            public string Origin => entry.Origin;
            public string Object => entry.Object;
            public int? Version => entry.Version;
            public string Role => entry.Role;
            public string Reason => entry.Reason;
            public string Form => entry.Form;
            public long Size => data.LongLength;
            public byte[] ReadBytes() => (byte[])data.Clone();
        }

        sealed class ExtWriteContext : ContextBase, IExtWriteContext
        {
            public JToken MainJson;
            public readonly Dictionary<string, JToken> AccessoryValues = new Dictionary<string, JToken>(StringComparer.Ordinal);
            public readonly Dictionary<string, JToken> ClothesValues = new Dictionary<string, JToken>(StringComparer.Ordinal);
            public readonly SortedDictionary<string, byte[]> FileData = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);

            public ExtWriteContext(ExtInstance inst, string ns, bool required, FindingList findings) : base(inst, ns, findings)
            {
                Required = required;
            }

            internal ExtInstance Instance => Inst;
            internal string Namespace => Ns;

            public bool Required { get; set; }

            static JToken Parse(string json)
            {
                var t = DocReader.TryParseValue(json, out string error);
                if (t == null) throw new ArgumentException("ext JSON does not parse as one value: " + error);
                int size = ExtInline.CanonicalSize(t);
                if (size > ExtInline.MaxBlockBytes) throw new ArgumentException("ext JSON of " + size + " bytes (canonical UTF-8) is over 1 MiB");
                return t;
            }

            public void SetJson(string json) => MainJson = json == null ? null : Parse(json);

            public void SetAccessoryJson(string accessoryId, string json)
            {
                if (accessoryId == null) throw new ArgumentNullException(nameof(accessoryId));
                if (Inst.Scope != ExtScope.Coord) throw new InvalidOperationException("accessory data belongs to the coord scope");
                if (json == null) AccessoryValues.Remove(accessoryId);
                else AccessoryValues[accessoryId] = Parse(json);
            }

            public void SetClothesJson(string kind, string json)
            {
                if (kind == null) throw new ArgumentNullException(nameof(kind));
                if (Inst.Scope != ExtScope.Coord) throw new ArgumentException("clothes part data belongs to the coord scope", nameof(kind));
                if (ClothesSet.IndexOf(kind) < 0) throw new ArgumentException("not a clothes kind name: " + kind + " (top, bottom, innerTop, innerBottom, gloves, pantyhose, socks, shoes)", nameof(kind));
                if (json == null) ClothesValues.Remove(kind);
                else ClothesValues[kind] = Parse(json);
            }

            public void AddFile(string path, byte[] data)
            {
                if (!ExtNamespaces.IsValidRelativePath(path)) throw new ArgumentException("ext file path outside the container grammar or over 200 characters: " + path);
                FileData[path] = (byte[])(data ?? throw new ArgumentNullException(nameof(data))).Clone();
            }
        }
    }
}

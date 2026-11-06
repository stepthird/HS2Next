// Extension-block contract of the save format.
// A plugin (or a Core feature) registers one IExtBlockHandler per namespace it owns; Core calls
// Read once per scope instance that holds data for the namespace while a file loads, and Write once
// per scope instance present in the model while a file is saved. The Api assembly has no JSON
// library, so JSON crosses the boundary as canonical text and files as bytes. Part of the stable API
// (assembly HS2Next.Api).
using System.Collections.Generic;
using System.IO;

namespace HS2Next.Api.Save
{
    /// <summary>Where an ext block lives.</summary>
    [System.Flags]
    public enum ExtScope
    {
        /// <summary>A character's appearance (chara.json `ext`, `ext/&lt;ns&gt;/` of the character subtree).</summary>
        Chara = 1,
        /// <summary>A coordinate (coord.json `ext`, accessory entries' and clothes parts' `ext`, `ext/&lt;ns&gt;/` of the coordinate).</summary>
        Coord = 2,
        /// <summary>A scene (scene.json top-level `ext`, `ext/&lt;ns&gt;/`).</summary>
        Scene = 4,
        /// <summary>One scene node (the node's `ext`, `nodes/&lt;nodeId&gt;/ext/&lt;ns&gt;/`).</summary>
        Node = 8,
    }

    /// <summary>The severity of a finding a handler reports.</summary>
    public enum ExtSeverity
    {
        /// <summary>For information.</summary>
        Info,
        /// <summary>A problem worth a look.</summary>
        Warning,
        /// <summary>A failure.</summary>
        Error,
    }

    /// <summary>Reads and writes the data of one namespace.</summary>
    public interface IExtBlockHandler
    {
        /// <summary>The namespace: the plugin GUID or <c>&lt;guid&gt;.&lt;name&gt;</c> (grammar <c>[A-Za-z0-9][A-Za-z0-9._-]{0,127}</c>).</summary>
        string Namespace { get; }
        /// <summary>Data version this handler writes (stored per manifest ext entry; the handler migrates older data in Read).</summary>
        int Version { get; }
        /// <summary>Scopes the handler reads and writes.</summary>
        ExtScope Scopes { get; }
        /// <summary>Initial value of <see cref="IExtWriteContext.Required"/>: true when the content is incomplete or misleading without this handler.</summary>
        bool RequiredByDefault { get; }
        /// <summary>Parses and stores the block of one scope instance. Apply it later (CharaBuilt, SceneLoaded), not here.</summary>
        void Read(IExtReadContext ctx);
        /// <summary>Writes the block of one scope instance; writing nothing means "no block".</summary>
        void Write(IExtWriteContext ctx);
    }

    /// <summary>What every ext call sees: the scope instance and its owner. Implemented by the host; valid during the call.</summary>
    public interface IExtContext
    {
        /// <summary>The scope of the instance being read or written.</summary>
        ExtScope Scope { get; }
        /// <summary>The owner: the scene node id (node scope, and chara/coord scopes inside a scene), or null.</summary>
        string OwnerId { get; }
        /// <summary>"hs2next.chara", "hs2next.coord" or "hs2next.scene".</summary>
        string ContainerFormat { get; }
        /// <summary>Adds a finding to the load or save result.</summary>
        void Report(ExtSeverity severity, string code, string message);
    }

    /// <summary>One block being read while a file loads. Implemented by the host; valid during the call.</summary>
    public interface IExtReadContext : IExtContext
    {
        /// <summary>The `version` of the manifest ext entry (0 when the data had no entry).</summary>
        int StoredVersion { get; }
        /// <summary>The `required` flag of the manifest ext entry (false when the data had no entry).</summary>
        bool StoredRequired { get; }
        /// <summary>Canonical JSON text of the inline value on the scope's main object, or null.</summary>
        string Json { get; }
        /// <summary>Coord scope: accessory id → canonical JSON text of that accessory's inline value (empty otherwise).</summary>
        IReadOnlyDictionary<string, string> AccessoryJson { get; }
        /// <summary>Coord scope: clothes kind name (<c>top</c>, <c>bottom</c>, <c>innerTop</c>, <c>innerBottom</c>, <c>gloves</c>,
        /// <c>pantyhose</c>, <c>socks</c>, <c>shoes</c>) → canonical JSON text of that clothes part's inline value (empty
        /// otherwise). The value travels with the part when a coordinate is worn in any mode.</summary>
        IReadOnlyDictionary<string, string> ClothesJson { get; }
        /// <summary>Paths of the block's files, relative to <c>ext/&lt;ns&gt;/</c>, in ordinal order.</summary>
        IReadOnlyList<string> Files { get; }
        /// <summary>A copy of a file's bytes (KeyNotFoundException for an unknown path).</summary>
        byte[] ReadFile(string path);
        /// <summary>A read-only stream over a file's bytes.</summary>
        Stream OpenFile(string path);
        /// <summary>Refuses the block (e.g. a newer data version): it is kept unchanged and written back as unknown.</summary>
        void Decline(string reason);
    }

    /// <summary>One block being written while a file saves. Implemented by the host; valid during the call.</summary>
    public interface IExtWriteContext : IExtContext
    {
        /// <summary>Sets the inline value of the scope's main object (one JSON value; ArgumentException otherwise). Null clears it.</summary>
        void SetJson(string json);
        /// <summary>Coord scope: sets the inline value of one accessory (by accessory id). Null clears it.</summary>
        void SetAccessoryJson(string accessoryId, string json);
        /// <summary>Coord scope: sets the inline value of one clothes part by its kind name (see
        /// <see cref="IExtReadContext.ClothesJson"/>; one JSON value, at most 1 MiB of canonical text). Null clears it.
        /// ArgumentException for another kind name or outside the coord scope.</summary>
        void SetClothesJson(string kind, string json);
        /// <summary>
        /// Adds a file under <c>ext/&lt;ns&gt;/</c>: ASCII segments of letters, digits and <c>. _ % -</c> joined by '/', no "." or
        /// ".." segment, at most 200 characters (ArgumentException otherwise).
        /// </summary>
        void AddFile(string path, byte[] data);
        /// <summary>Starts at <see cref="IExtBlockHandler.RequiredByDefault"/>.</summary>
        bool Required { get; set; }
    }

    /// <summary>The ext block handlers of the plugin's namespaces (the GUID and &lt;guid&gt;.*). Implemented by the host.</summary>
    public interface IExtRegistry
    {
        /// <summary>Registers a handler. Reserved, malformed or taken namespaces (ignoring case) throw ArgumentException.</summary>
        void Register(IExtBlockHandler handler);
        /// <summary>Removes the handler of a namespace; false when none was registered.</summary>
        bool Unregister(string ns);
    }
}

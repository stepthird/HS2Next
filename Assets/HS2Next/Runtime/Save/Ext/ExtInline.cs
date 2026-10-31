// Inline ext data of one JSON object: the `ext` member, whose
// members are namespaces in ordinal order, each holding any JSON value. Documents hold one per
// object that may carry ext data (chara, coord and scene roots, accessory entries, scene nodes); the
// ext dispatcher reads and fills them, the documents only carry them. Values are detached tokens.
// The 1 MiB limit binds the producers of inline data (Set, the Api's SetJson/SetAccessoryJson); a
// block read from a file is kept and written back whatever its size (preservation).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save
{
    public sealed class ExtInline : IEnumerable<KeyValuePair<string, JToken>>
    {
        /// <summary>Limit: UTF-8 bytes of the canonical text of one inline block (<see cref="CanonicalSize"/>).</summary>
        public const int MaxBlockBytes = 1 << 20;

        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        readonly SortedDictionary<string, JToken> blocks = new SortedDictionary<string, JToken>(StringComparer.Ordinal);

        public int Count => blocks.Count;
        public bool IsEmpty => blocks.Count == 0;

        /// <summary>Namespaces in ordinal order.</summary>
        public IEnumerable<string> Namespaces => blocks.Keys;

        public JToken Get(string ns) => blocks.TryGetValue(ns, out var t) ? t : null;

        public bool Contains(string ns) => blocks.ContainsKey(ns);

        /// <summary>
        /// The size the limit measures: UTF-8 bytes of the value's canonical text on its own (the text
        /// <c>IExtReadContext.Json</c> hands out).
        /// </summary>
        public static int CanonicalSize(JToken value) => value == null ? 0 : Utf8.GetByteCount(CanonicalJsonWriter.ToText(value));

        /// <summary>
        /// Sets (or with null removes) the value of a namespace. The namespace must satisfy the namespace grammar and must not differ only
        /// in case from one already held; a value over <see cref="MaxBlockBytes"/> is refused (ArgumentException).
        /// </summary>
        public void Set(string ns, JToken value) => Put(ns, value, true);

        /// <summary>Set without the size check: values read from a file (kept whatever their size) and values the
        /// dispatcher has already checked.</summary>
        internal void SetWithoutSizeCheck(string ns, JToken value) => Put(ns, value, false);

        void Put(string ns, JToken value, bool checkSize)
        {
            if (!ExtNamespaces.IsValid(ns)) throw new ArgumentException("invalid ext namespace: " + ns, nameof(ns));
            if (value == null) { blocks.Remove(ns); return; }
            foreach (var k in blocks.Keys)
                if (k != ns && string.Equals(k, ns, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("ext namespace differs only in case from " + k, nameof(ns));
            if (checkSize)
            {
                int size = CanonicalSize(value);
                if (size > MaxBlockBytes)
                    throw new ArgumentException("inline ext block " + ns + " is " + size + " bytes of canonical text, over the 1 MiB limit (store large data as ext files)", nameof(value));
            }
            blocks[ns] = value;
        }

        public bool Remove(string ns) => blocks.Remove(ns);

        public void Clear() => blocks.Clear();

        public IEnumerator<KeyValuePair<string, JToken>> GetEnumerator() => blocks.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => blocks.GetEnumerator();

        /// <summary>A deep copy.</summary>
        public ExtInline Clone()
        {
            var c = new ExtInline();
            foreach (var kv in blocks) c.blocks[kv.Key] = kv.Value.DeepClone();
            return c;
        }
    }
}

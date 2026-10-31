// Ext namespace and ext path rules.
using System;

namespace HS2Next.Save
{
    public static class ExtNamespaces
    {
        /// <summary>The core-owned namespace of imported HS2 plugin data.</summary>
        public const string Kkex = "hs2.kkex";

        /// <summary>Longest relative file path inside a namespace directory.</summary>
        public const int MaxRelativePath = 200;

        /// <summary>Grammar <c>[A-Za-z0-9][A-Za-z0-9._-]{0,127}</c>.</summary>
        public static bool IsValid(string ns)
        {
            if (string.IsNullOrEmpty(ns) || ns.Length > 128) return false;
            for (int i = 0; i < ns.Length; i++)
            {
                char c = ns[i];
                bool alnum = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
                if (i == 0 ? !alnum : !(alnum || c == '.' || c == '_' || c == '-')) return false;
            }
            return true;
        }

        /// <summary>True for the namespaces HS2Next reserves: <c>hs2.*</c>, <c>hs2next.*</c>, <c>illusion.*</c> (case-insensitive; also the bare names).</summary>
        public static bool IsReserved(string ns)
        {
            if (ns == null) return false;
            foreach (var p in new[] { "hs2", "hs2next", "illusion" })
                if (string.Equals(ns, p, StringComparison.OrdinalIgnoreCase) || ns.StartsWith(p + ".", StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>True if <paramref name="ns"/> is <paramref name="owner"/> itself or <c>&lt;owner&gt;.*</c> (case-insensitive).</summary>
        public static bool IsOwnedBy(string ns, string owner) =>
            string.Equals(ns, owner, StringComparison.OrdinalIgnoreCase) || ns.StartsWith(owner + ".", StringComparison.OrdinalIgnoreCase);

        /// <summary>A plugin-supplied file path relative to <c>ext/&lt;ns&gt;/</c>: grammar, at most 200 characters.</summary>
        public static bool IsValidRelativePath(string path) =>
            path != null && path.Length > 0 && path.Length <= MaxRelativePath && EntryNames.IsConforming(path);
    }
}

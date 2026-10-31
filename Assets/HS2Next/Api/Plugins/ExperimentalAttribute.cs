// Marks Api types and members that are outside the semver promise until the mark is removed (they may change in any
// minor release). Part of the stable API (assembly HS2Next.Api).
using System;

namespace HS2Next.Api
{
    /// <summary>The marked type or member may change or disappear in any minor release.</summary>
    [AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = false)]
    public sealed class ExperimentalAttribute : Attribute
    {
        public ExperimentalAttribute() { }
        public ExperimentalAttribute(string note) { Note = note; }

        /// <summary>Why it is experimental, or null.</summary>
        public string Note { get; }
    }
}

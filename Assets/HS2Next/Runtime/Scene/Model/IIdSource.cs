// Sources of new node ids. The model creates ids only for
// new nodes, duplicates and new route points; readers never create random ids. Tests inject a counting source
// so every run produces the same ids.
using System;
using System.Globalization;

namespace HS2Next.Scenes
{
    public interface IIdSource
    {
        /// <summary>A new id: 32 lowercase hex digits, never returned before by this source.</summary>
        string NewId();
    }

    /// <summary>The default: <c>Guid.NewGuid().ToString("N")</c>.</summary>
    public sealed class GuidIdSource : IIdSource
    {
        public string NewId() => Guid.NewGuid().ToString("N");
    }

    /// <summary>Deterministic ids for tests: the 16 hex digits of <see cref="Seed"/> followed by 16 hex digits of a counter.</summary>
    public sealed class CountingIdSource : IIdSource
    {
        public readonly ulong Seed;
        /// <summary>The counter value of the next id.</summary>
        public ulong Next;

        public CountingIdSource(ulong seed = 0x5ce4e5ce4e5ce4e5UL, ulong first = 1)
        {
            Seed = seed;
            Next = first;
        }

        public string NewId() => Seed.ToString("x16", CultureInfo.InvariantCulture) + (Next++).ToString("x16", CultureInfo.InvariantCulture);
    }
}

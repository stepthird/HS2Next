// Unknown members of one JSON object: kept as raw tokens in read
// order, never interpreted, written back after the known members (before `ext`) by
// CanonicalJsonWriter.Leftovers. Every document object holds one (null or empty when there is none).
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save
{
    public sealed class JsonLeftovers
    {
        readonly List<KeyValuePair<string, JToken>> members = new List<KeyValuePair<string, JToken>>();

        /// <summary>Members in read order (names unique).</summary>
        public IReadOnlyList<KeyValuePair<string, JToken>> Members => members;

        public int Count => members.Count;
        public bool IsEmpty => members.Count == 0;

        /// <summary>Appends a member (the token is stored as given; callers pass detached tokens).</summary>
        public void Add(string name, JToken value)
        {
            for (int i = 0; i < members.Count; i++)
                if (members[i].Key == name) { members[i] = new KeyValuePair<string, JToken>(name, value); return; }
            members.Add(new KeyValuePair<string, JToken>(name, value));
        }

        public JToken Get(string name)
        {
            foreach (var m in members) if (m.Key == name) return m.Value;
            return null;
        }

        public bool Remove(string name) => members.RemoveAll(m => m.Key == name) > 0;

        /// <summary>A deep copy (tokens cloned).</summary>
        public JsonLeftovers Clone()
        {
            var c = new JsonLeftovers();
            foreach (var m in members) c.members.Add(new KeyValuePair<string, JToken>(m.Key, m.Value.DeepClone()));
            return c;
        }
    }
}

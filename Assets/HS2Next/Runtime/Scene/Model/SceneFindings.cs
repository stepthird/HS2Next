// Findings of a scene session (Appendix A): what the model (bind, save) and
// the realization report about the open scene, keyed by node where there is one. Repeats of one (code, node)
// merge into a count; the severity is the highest seen. Severities and codes follow the save format's findings.
using System;
using System.Collections;
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    public sealed class SceneFinding
    {
        public FindingSeverity Severity;
        public string Code;
        /// <summary>The node the finding is about, or null for the scene.</summary>
        public string NodeId;
        public string Message;
        public int Count = 1;

        public override string ToString() =>
            Severity.ToString().ToLowerInvariant() + " " + Code + (NodeId == null ? "" : " [" + NodeId + "]") +
            (Count > 1 ? " x" + Count : "") + ": " + Message;
    }

    public sealed class SceneFindings : IReadOnlyList<SceneFinding>
    {
        readonly List<SceneFinding> items = new List<SceneFinding>();
        readonly Dictionary<string, SceneFinding> byKey = new Dictionary<string, SceneFinding>(StringComparer.Ordinal);

        /// <summary>Raised for every new finding and every merged repeat.</summary>
        public event Action<SceneFinding> Added;

        public int Count => items.Count;
        public SceneFinding this[int index] => items[index];
        public IEnumerator<SceneFinding> GetEnumerator() => items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();

        public SceneFinding Add(FindingSeverity severity, string code, string nodeId, string message)
        {
            string key = code + "\u0000" + (nodeId ?? "");
            if (byKey.TryGetValue(key, out var f))
            {
                f.Count++;
                if (severity > f.Severity) f.Severity = severity;
            }
            else
            {
                f = new SceneFinding { Severity = severity, Code = code, NodeId = nodeId, Message = message ?? "" };
                items.Add(f);
                byKey[key] = f;
            }
            Added?.Invoke(f);
            return f;
        }

        public SceneFinding Info(string code, string nodeId, string message) => Add(FindingSeverity.Info, code, nodeId, message);
        public SceneFinding Warning(string code, string nodeId, string message) => Add(FindingSeverity.Warning, code, nodeId, message);
        public SceneFinding Error(string code, string nodeId, string message) => Add(FindingSeverity.Error, code, nodeId, message);

        /// <summary>Copies the findings of a load or save result (no node id: their path locates them).</summary>
        public void AddRange(IEnumerable<Finding> findings)
        {
            if (findings == null) return;
            foreach (var f in findings)
                for (int i = 0; i < Math.Max(1, f.Count); i++)
                    Add(f.Severity, f.Code, null, string.IsNullOrEmpty(f.Path) ? f.Message : f.Path + ": " + f.Message);
        }

        /// <summary>Raised when a finding is removed (<see cref="Remove"/>).</summary>
        public event Action<SceneFinding> Removed;

        /// <summary>
        /// Removes the finding of (code, node), e.g. a build's report that a rebuild replaces; true when there was one.
        /// </summary>
        public bool Remove(string code, string nodeId)
        {
            string key = code + "\u0000" + (nodeId ?? "");
            if (!byKey.TryGetValue(key, out var f)) return false;
            byKey.Remove(key);
            items.Remove(f);
            Removed?.Invoke(f);
            return true;
        }

        public bool Has(string code) => items.Exists(f => f.Code == code);

        public bool Has(string code, string nodeId) => byKey.ContainsKey(code + "\u0000" + (nodeId ?? ""));

        /// <summary>Sum of the counts of every finding with this code.</summary>
        public int CountOf(string code)
        {
            int n = 0;
            foreach (var f in items) if (f.Code == code) n += f.Count;
            return n;
        }

        public bool HasErrors => items.Exists(f => f.Severity == FindingSeverity.Error);

        public void Clear()
        {
            items.Clear();
            byKey.Clear();
        }

        public override string ToString() => string.Join("\n", items);
    }
}

// Ring buffer behind `log.tail`: the Unity layer feeds it from
// Application.logMessageReceivedThreaded, so agents see errors without opening Player.log or the Editor log.
// Every entry gets a sequence number; `since` returns what came after a number a client saw before. Thread-safe.
using System;
using System.Collections.Generic;

namespace HS2Next.Bridge
{
    /// <summary>Unity's LogType values, by severity group.</summary>
    public enum LogEntryType { Log, Warning, Error, Exception, Assert }

    public sealed class LogEntry
    {
        public long Seq;
        public DateTime TimeUtc;
        public LogEntryType Type;
        public string Message;
        public string Stack;
    }

    public sealed class LogRing
    {
        /// <summary>Longest kept message and stack trace, characters (the rest is cut with "…").</summary>
        public const int MaxText = 8192;

        readonly object gate = new object();
        readonly LogEntry[] ring;
        long next = 1;
        int start, count;

        public LogRing(int capacity) { ring = new LogEntry[Math.Max(16, capacity)]; }

        public int Capacity => ring.Length;

        /// <summary>Sequence number of the newest entry (0 when empty).</summary>
        public long LastSeq { get { lock (gate) return next - 1; } }

        public void Add(LogEntryType type, string message, string stack = null)
        {
            var e = new LogEntry { TimeUtc = DateTime.UtcNow, Type = type, Message = Cut(message), Stack = string.IsNullOrEmpty(stack) ? null : Cut(stack) };
            lock (gate)
            {
                e.Seq = next++;
                if (count < ring.Length) { ring[(start + count) % ring.Length] = e; count++; }
                else { ring[start] = e; start = (start + 1) % ring.Length; }
            }
        }

        static string Cut(string s) => s == null ? "" : s.Length <= MaxText ? s : s.Substring(0, MaxText) + "…";

        /// <summary>
        /// The newest <paramref name="n"/> entries with Seq &gt; <paramref name="since"/> whose severity is at least
        /// <paramref name="minLevel"/> (Log &lt; Warning &lt; Error; Exception and Assert count as Error), oldest first.
        /// <paramref name="gap"/>: entries after <paramref name="since"/> were already overwritten.
        /// </summary>
        public List<LogEntry> Tail(int n, LogEntryType minLevel, long since, out bool gap)
        {
            var result = new List<LogEntry>();
            lock (gate)
            {
                long oldest = count > 0 ? ring[start].Seq : next;
                gap = since >= 0 && since + 1 < oldest && next - 1 > since;
                for (int i = count - 1; i >= 0 && result.Count < n; i--)
                {
                    var e = ring[(start + i) % ring.Length];
                    if (e.Seq <= since) break;
                    if (Severity(e.Type) >= Severity(minLevel)) result.Add(e);
                }
            }
            result.Reverse();
            return result;
        }

        static int Severity(LogEntryType t) => t == LogEntryType.Log ? 0 : t == LogEntryType.Warning ? 1 : 2;
    }
}

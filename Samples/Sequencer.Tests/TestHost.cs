// Sample sequencer for HS2Next (CC0-1.0: dedicated to the public domain).
// A recording host for the engine-free tests: inline calls answer from a reply rule (ok by default), queued calls are
// held until the test delivers their replies, log lines are kept.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Stp3.Samples.Sequencer.Tests
{
    sealed class TestHost : ISequenceHost
    {
        /// <summary>Every inline request in order.</summary>
        public readonly List<string> Calls = new List<string>();
        /// <summary>Queued requests not answered yet, with their reply callbacks.</summary>
        public readonly List<KeyValuePair<string, Action<string>>> Queued = new List<KeyValuePair<string, Action<string>>>();
        /// <summary>Every queued request ever sent.</summary>
        public readonly List<string> Sent = new List<string>();
        public readonly List<string> Logs = new List<string>();
        /// <summary>Reply rule for inline calls: the request text → reply text (null = ok).</summary>
        public Func<string, string> Reply;

        public string Call(string requestJson)
        {
            Calls.Add(requestJson);
            string cmd = (string)JObject.Parse(requestJson)["cmd"];
            return Reply?.Invoke(requestJson) ?? "{\"ok\":true,\"cmd\":\"" + cmd + "\"}";
        }

        public void CallAsync(string requestJson, Action<string> onReply)
        {
            Sent.Add(requestJson);
            Queued.Add(new KeyValuePair<string, Action<string>>(requestJson, onReply));
        }

        public void Log(string line) => Logs.Add(line);

        /// <summary>Answers every queued request (ok unless <paramref name="reply"/> gives another text).</summary>
        public void DeliverAll(Func<string, string> reply = null)
        {
            var list = new List<KeyValuePair<string, Action<string>>>(Queued);
            Queued.Clear();
            foreach (var q in list)
            {
                string cmd = (string)JObject.Parse(q.Key)["cmd"];
                q.Value(reply?.Invoke(q.Key) ?? "{\"ok\":true,\"cmd\":\"" + cmd + "\"}");
            }
        }

        public static string Error(string cmd, string code, string message = "refused") =>
            "{\"ok\":false,\"cmd\":\"" + cmd + "\",\"error\":\"" + message + "\",\"code\":\"" + code + "\"}";

        /// <summary>The command names of the inline calls since index <paramref name="from"/>.</summary>
        public List<string> CallCmds(int from = 0)
        {
            var l = new List<string>();
            for (int i = from; i < Calls.Count; i++) l.Add((string)JObject.Parse(Calls[i])["cmd"]);
            return l;
        }

        /// <summary>The last inline request for a node key (obj.transform), parsed; null when none.</summary>
        public JObject LastTransform(string key)
        {
            for (int i = Calls.Count - 1; i >= 0; i--)
            {
                var o = JObject.Parse(Calls[i]);
                if ((string)o["cmd"] == "obj.transform" && (string)o["key"] == key) return o;
            }
            return null;
        }

        /// <summary>The note members of queued event requests sent since index <paramref name="from"/> (their "tag" member).</summary>
        public List<string> SentTags(int from = 0)
        {
            var l = new List<string>();
            for (int i = from; i < Sent.Count; i++) l.Add((string)JObject.Parse(Sent[i])["tag"]);
            return l;
        }
    }
}

// A plugin's entries in the scene's undo history (IPluginHistory, experimental): an entry for a change the plugin has
// already made to its own state goes onto the scene bus's own undo list (CommandBus.RecordExternal, source Plugin,
// owner = the plugin's GUID), so it interleaves with the host commands the plugin calls through Call / Request, and undo
// / redo (the UI, the keys, the bridge) step through both. Busy while a scene transaction is open (also a client's batch
// that runs the plugin's own command), while the bus applies a command or delivers a scene event, and inside an undo or
// redo callback; NoScene without a session. A label starting with "plugin.<guid>." is a key of the plugin's strings,
// resolved when the entry is recorded. The callbacks run inside the plugin's fault wrapper: a throw is a fault of the
// plugin, the entry still moves to the other list and the undo / redo reply carries the bus's warning; the history is
// never cleared for it. The host removes the plugin's entries when it leaves the loaded state. Main thread only.
using System;
using System.IO;
using System.Threading;
using HS2Next.Api.Plugins;
using HS2Next.Scenes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HS2Next.App.Plugins
{
    public sealed class PluginHistory : IPluginHistory
    {
        readonly PluginHost host;
        readonly PluginEntry entry;
        readonly int mainThread = Thread.CurrentThread.ManagedThreadId;

        internal PluginHistory(PluginHost host, PluginEntry entry)
        {
            this.host = host;
            this.entry = entry;
        }

        CommandBus Bus => host.Session?.Commands;

        public bool CanRecord
        {
            get
            {
                var b = Bus;
                return b != null && entry.IsLoaded && b.AcceptsExternal;
            }
        }

        public int Count => Bus?.CountOwner(entry.Guid) ?? 0;

        public HistoryRecordResult Record(string label, Action undo, Action redo, string mergeKey = null)
        {
            if (undo == null) throw new ArgumentNullException(nameof(undo));
            if (redo == null) throw new ArgumentNullException(nameof(redo));
            CheckThread();
            string text = Label(label);
            return Push(new PluginHistoryCommand(entry.Guid, text, undo, redo, Runner), text, mergeKey);
        }

        public HistoryRecordResult Record(string label, string beforeJson, string afterJson, Action<string> apply, string mergeKey = null)
        {
            if (apply == null) throw new ArgumentNullException(nameof(apply));
            CheckJson(beforeJson, nameof(beforeJson));
            CheckJson(afterJson, nameof(afterJson));
            CheckThread();
            string text = Label(label);
            return Push(new PluginHistoryCommand(entry.Guid, text, beforeJson, afterJson, apply, Runner), text, mergeKey);
        }

        HistoryRecordResult Push(PluginHistoryCommand cmd, string label, string mergeKey)
        {
            if (!entry.IsLoaded) throw new ObjectDisposedException("plugin history " + entry.Guid, "the plugin is not loaded");
            var bus = Bus;
            if (bus == null) return HistoryRecordResult.NoScene;
            switch (bus.RecordExternal(entry.Guid, cmd, label, mergeKey))
            {
                case ExternalRecordResult.Recorded: return HistoryRecordResult.Recorded;
                case ExternalRecordResult.Merged: return HistoryRecordResult.Merged;
                default: return HistoryRecordResult.Busy;
            }
        }

        /// <summary>Runs one undo or redo callback in the plugin's fault wrapper; the exception it threw (counted), or null.</summary>
        Exception Runner(string step, Action callback)
        {
            Exception thrown = null;
            var guard = entry.Guard;
            if (guard == null)
            {
                try { callback(); }
                catch (Exception e) { thrown = e; }
                return thrown;
            }
            guard.Run("history " + step, () =>
            {
                try { callback(); }
                catch (Exception e)
                {
                    thrown = e;
                    throw;
                }
            });
            return thrown;
        }

        string Label(string label)
        {
            if (string.IsNullOrEmpty(label)) return entry.Manifest?.Name ?? entry.Guid;
            if (label.StartsWith("plugin." + entry.Guid + ".", StringComparison.Ordinal) && HS2Next.UI.Core.L10n.TryGet(label, out var text) && !string.IsNullOrEmpty(text))
                return text;
            return label;
        }

        void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThread) throw new InvalidOperationException("the plugin history is used on the main thread only");
        }

        static void CheckJson(string json, string name)
        {
            if (json == null) throw new ArgumentException("a state is one JSON value (null given)", name);
            try
            {
                using (var r = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None })
                {
                    JToken.ReadFrom(r);
                    if (r.Read()) throw new JsonReaderException("additional text after the JSON value");
                }
            }
            catch (JsonException e) { throw new ArgumentException("a state is one JSON value: " + e.Message, name); }
        }
    }
}

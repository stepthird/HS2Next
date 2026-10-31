// Notification of a scene session. Main thread only. Handlers run in
// subscription order after the operation completed; an exception in a handler
// is caught, logged once per (handler, exception type) and affects neither the model nor the other handlers.
// Handlers must not call the bus synchronously (it answers `busy`); they may queue work for the next frame.
using System;
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    public sealed class SceneLoadedArgs
    {
        /// <summary>Findings of the load (reader, ext dispatch) and of binding the model (normalisations, card references).</summary>
        public FindingList Findings { get; }
        /// <summary>True after <see cref="SceneSession.NewScene"/>; false after a load or a bind.</summary>
        public bool IsNew { get; }
        /// <summary>The file a load read (the path given to SceneSession.Load), or null (new scene, bytes, bind).</summary>
        public string Path { get; }
        public SceneLoadedArgs(FindingList findings, bool isNew = false, string path = null)
        {
            Findings = findings ?? new FindingList();
            IsNew = isNew;
            Path = path;
        }
    }

    public sealed class HistoryChangedArgs
    {
        public int UndoCount { get; }
        public int RedoCount { get; }
        /// <summary>Label of the entry Undo would revert, or null.</summary>
        public string UndoLabel { get; }
        /// <summary>Label of the entry Redo would apply, or null.</summary>
        public string RedoLabel { get; }

        public HistoryChangedArgs(int undoCount, int redoCount, string undoLabel, string redoLabel)
        {
            UndoCount = undoCount;
            RedoCount = redoCount;
            UndoLabel = undoLabel;
            RedoLabel = redoLabel;
        }
    }

    public sealed class SelectionChangedArgs
    {
        public IReadOnlyList<string> OldIds { get; }
        public IReadOnlyList<string> NewIds { get; }
        public SelectionChangedArgs(IReadOnlyList<string> oldIds, IReadOnlyList<string> newIds) { OldIds = oldIds; NewIds = newIds; }
    }

    public sealed class SceneEvents
    {
        /// <summary>Log sink for handler exceptions (the Unity layer sets it to its logger); null = not logged.</summary>
        public static Action<string> Log;

        /// <summary>Once per bus operation and per silent write group.</summary>
        public event Action<SceneChangeBatch> Changed;
        /// <summary>Before a new or loaded model replaces the old one (every node of the old model is gone).</summary>
        public event Action Cleared;
        /// <summary>After <see cref="SceneSession.NewScene"/> / <see cref="SceneSession.Bind"/>.</summary>
        public event Action<SceneLoadedArgs> Loaded;
        /// <summary>Before a save converts the model (the runtime flushes and captures here).</summary>
        public event Action Saving;
        /// <summary>After <see cref="SceneSession.Save"/> / <see cref="SceneSession.ToBytes"/>, success or not.</summary>
        public event Action<SaveResult> Saved;
        public event Action<HistoryChangedArgs> HistoryChanged;
        public event Action<SelectionChangedArgs> SelectionChanged;
        /// <summary>A node left history for good (not in the model, no history entry holds it): its id can never come back.</summary>
        public event Action<string> NodeForgotten;

        readonly HashSet<string> logged = new HashSet<string>(StringComparer.Ordinal);
        int delivering;
        int deliveringChanged;

        /// <summary>True while any handler runs.</summary>
        public bool IsDelivering => delivering > 0;
        /// <summary>True while <see cref="Changed"/> handlers run.</summary>
        internal bool IsDeliveringChanged => deliveringChanged > 0;
        /// <summary>Exceptions caught in handlers so far.</summary>
        public int HandlerFaults { get; private set; }

        internal void RaiseChanged(SceneChangeBatch b)
        {
            deliveringChanged++;
            try { Invoke(Changed, h => ((Action<SceneChangeBatch>)h)(b)); }
            finally { deliveringChanged--; }
        }

        internal void RaiseCleared() => Invoke(Cleared, h => ((Action)h)());
        internal void RaiseLoaded(SceneLoadedArgs a) => Invoke(Loaded, h => ((Action<SceneLoadedArgs>)h)(a));
        internal void RaiseSaving() => Invoke(Saving, h => ((Action)h)());
        internal void RaiseSaved(SaveResult r) => Invoke(Saved, h => ((Action<SaveResult>)h)(r));
        internal void RaiseHistoryChanged(HistoryChangedArgs a) => Invoke(HistoryChanged, h => ((Action<HistoryChangedArgs>)h)(a));
        internal void RaiseSelectionChanged(IReadOnlyList<string> oldIds, IReadOnlyList<string> newIds)
        {
            var a = new SelectionChangedArgs(oldIds, newIds);
            Invoke(SelectionChanged, h => ((Action<SelectionChangedArgs>)h)(a));
        }
        internal void RaiseNodeForgotten(string id) => Invoke(NodeForgotten, h => ((Action<string>)h)(id));

        void Invoke(Delegate multicast, Action<Delegate> call)
        {
            if (multicast == null) return;
            delivering++;
            try
            {
                foreach (var h in multicast.GetInvocationList())
                {
                    try { call(h); }
                    catch (Exception e)
                    {
                        HandlerFaults++;
                        string key = (h.Method.DeclaringType?.FullName ?? "?") + "." + h.Method.Name + "|" + e.GetType().FullName;
                        if (logged.Add(key)) Log?.Invoke("scene event handler " + h.Method.DeclaringType?.Name + "." + h.Method.Name + " threw: " + e);
                    }
                }
            }
            finally { delivering--; }
        }
    }
}

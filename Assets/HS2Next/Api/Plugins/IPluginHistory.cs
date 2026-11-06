// Undo entries of a plugin (experimental: they may change in any minor release). An entry stands for a change of the
// plugin's own state in the scene's own undo history, next to the entries of the host commands the plugin called through
// Call or Request, in call order: undo and redo (the toolbar, the keys, the bridge's undo / redo) step through both. Entries
// are kept while the app runs only (never saved); they are removed when the plugin is unloaded, faults or is disabled, and a
// new or loaded scene clears them with the rest of the history. Part of the Api (assembly HS2Next.Api).
using System;

namespace HS2Next.Api.Plugins
{
    /// <summary>The outcome of a record in <see cref="IPluginHistory"/>.</summary>
    [Experimental("plugin undo entries may change in any minor release")]
    public enum HistoryRecordResult
    {
        /// <summary>A new entry is the newest undo step.</summary>
        Recorded = 0,
        /// <summary>The change joined the newest entry (the same merge key).</summary>
        Merged = 1,
        /// <summary>
        /// Refused: a scene transaction is open (a drag, a client's batch, also when the batch runs the plugin's own command),
        /// the scene is applying a command or delivering an event, or an undo or redo callback runs.
        /// </summary>
        Busy = 2,
        /// <summary>Refused: there is no scene.</summary>
        NoScene = 3,
    }

    /// <summary>
    /// The plugin's entries in the scene's undo history. Implemented by the host. Main thread only (InvalidOperationException
    /// otherwise). An entry is recorded like a host command: the redo list is dropped, the previous entry is closed for
    /// merges, the history's entry and size limits apply, and the scene shows unsaved changes. Its label is shown in the
    /// history; a label that starts with "plugin.&lt;guid&gt;." is a key of the plugin's strings, resolved when the entry is
    /// recorded. Undo and redo call the entry's callback inside the plugin's fault wrapper while the scene is applying, so a
    /// callback restores the plugin's own state only (scene changes through Call answer busy there). A callback that throws
    /// counts as a fault of the plugin; the entry still moves to the other list, the undo or redo reply carries the warning
    /// "plugin &lt;guid&gt;: undo failed (&lt;exception type&gt;)", and the rest of the history is kept.
    /// </summary>
    [Experimental("plugin undo entries may change in any minor release")]
    public interface IPluginHistory
    {
        /// <summary>True when a scene exists and a record would not be refused now.</summary>
        bool CanRecord { get; }

        /// <summary>
        /// Records one entry for a change the plugin has already made: undo calls <paramref name="undo"/>, redo calls
        /// <paramref name="redo"/>. With a non-null <paramref name="mergeKey"/> equal to the newest entry's, when that entry is
        /// this plugin's, nothing was recorded after it and nothing can be redone, the change merges into it: the first undo
        /// is kept and the new redo taken. ArgumentNullException for a null callback.
        /// </summary>
        HistoryRecordResult Record(string label, Action undo, Action redo, string mergeKey = null);

        /// <summary>
        /// Records one entry with states: undo calls <paramref name="apply"/> with <paramref name="beforeJson"/>, redo with
        /// <paramref name="afterJson"/>; each is one JSON value (ArgumentException otherwise). A merge (as in the callback form)
        /// keeps the first <paramref name="beforeJson"/> and takes the new <paramref name="afterJson"/> and
        /// <paramref name="apply"/>. ArgumentNullException for a null apply.
        /// </summary>
        HistoryRecordResult Record(string label, string beforeJson, string afterJson, Action<string> apply, string mergeKey = null);

        /// <summary>The plugin's entries in the undo and the redo list together.</summary>
        int Count { get; }
    }
}

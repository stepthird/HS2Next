// Registration side of the bridge contract: the command
// registry, the readiness stages behind the not_ready gate, the frame clock with update / late-frame hooks, the
// output root guard, batch scopes and hooks (Core opens one model transaction per batch), and the services object
// that groups them. Core reaches them through HS2Next.Bridge; plugins (Phase 5.1) through their plugin context.
// Part of the stable API (assembly HS2Next.Api).
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HS2Next.Api.Bridge
{
    /// <summary>The set of commands the bridge dispatches. Thread-safe.</summary>
    public interface ICommandRegistry
    {
        /// <summary>
        /// Registers a command and freezes its spec. ArgumentException when the name or one of its aliases is taken
        /// (by a name or an alias), reserved for the bridge (ping, help, bridge.version, batch, log.tail) or invalid.
        /// </summary>
        void Register(CommandSpec spec, CommandHandler handler);
        /// <summary>
        /// As Register, for an async handler. A separate name, because a lambda that returns <c>null</c> would bind to
        /// a Task-returning overload and the sync handler would silently be treated as an async one.
        /// </summary>
        void RegisterAsync(CommandSpec spec, AsyncCommandHandler handler);
        /// <summary>Removes a command by its name (not an alias); false when absent. Requests already running finish.</summary>
        bool Unregister(string name);
        /// <summary>Removes every command of an owner; returns the count.</summary>
        int UnregisterOwner(string owner);
        /// <summary>True for a registered name or alias.</summary>
        bool Contains(string name);
        /// <summary>The spec registered under a name or alias, or null.</summary>
        CommandSpec Find(string name);
        /// <summary>A snapshot of every spec, ordered by name (ordinal).</summary>
        IReadOnlyList<CommandSpec> Specs { get; }
    }

    /// <summary>
    /// Named readiness stages (<c>ping.ready</c> lists them). Commands with <see cref="ReadyGate.App"/>
    /// answer not_ready until every stage declared as required for the app is ready; a spec's
    /// <see cref="CommandSpec.Requires"/> adds stages. Before the app is ready, an unknown command also answers
    /// not_ready (its handler may not be registered yet). Thread-safe.
    /// </summary>
    public interface IBridgeReadiness
    {
        /// <summary>Adds a stage (not ready). Declaring a stage again only updates <paramref name="requiredForApp"/>.</summary>
        void Declare(string stage, bool requiredForApp);
        /// <summary>Sets a stage; an undeclared stage is declared as not required for the app.</summary>
        void Set(string stage, bool ready);
        /// <summary>False for an undeclared stage.</summary>
        bool IsReady(string stage);
        /// <summary>True when every stage declared as required for the app is ready.</summary>
        bool AppReady { get; }
        /// <summary>
        /// A progress text for a stage (e.g. "scanning zipmods 1200/5934"), listed by ping as readyDetail; null clears it.
        /// An undeclared stage is declared as not required for the app.
        /// </summary>
        void SetDetail(string stage, string detail);
        /// <summary>The stage's progress text, or null.</summary>
        string Detail(string stage);
        /// <summary>Every declared stage and its state, in declaration order.</summary>
        IReadOnlyList<KeyValuePair<string, bool>> Snapshot();
    }

    /// <summary>
    /// The bridge frame clock. In play mode and the player a frame is one Unity frame: the pump runs in Update
    /// at execution order −10000 and the late point in LateUpdate at 32000, after CharaMotion (1000) and the scene's
    /// late apply (1100). Editor batch tests without a player loop advance it by hand. Thread-safe.
    /// </summary>
    public interface IBridgeFrames
    {
        /// <summary>The frame of the current (or last) pump: Time.frameCount, or the manual counter.</summary>
        int Frame { get; }
        /// <summary>Seconds since the bridge clock started (real time).</summary>
        double Realtime { get; }
        /// <summary>
        /// Runs <paramref name="action"/> on the main thread at the pump of frame <see cref="Frame"/> + <paramref name="afterFrames"/>
        /// (≥ 1), before that pump dequeues requests. 0 runs it at the end of the current pump pass. Exceptions are logged.
        /// </summary>
        void AtUpdate(int afterFrames, Action action);
        /// <summary>
        /// Runs <paramref name="action"/> on the main thread at the late point of frame <see cref="Frame"/> +
        /// <paramref name="afterFrames"/> (0 = this frame's; captures go here). Called after this frame's late point
        /// (between frames, or from a late action), the count starts at the next frame. Exceptions are logged.
        /// </summary>
        void AtLate(int afterFrames, Action action);
        /// <summary>A task completed at the pump of frame <see cref="Frame"/> + <paramref name="frames"/> (≥ 1).</summary>
        Task WaitFrames(int frames);
        /// <summary>A task completed at the late point of frame <see cref="Frame"/> + <paramref name="afterFrames"/>.</summary>
        Task WaitLate(int afterFrames = 0);
        /// <summary>Raised at the start of every pump with the frame number (before scheduled actions and requests).</summary>
        event Action<int> FrameStarted;
        /// <summary>Raised at every late point with the frame number (after the scheduled late actions).</summary>
        event Action<int> LateFrame;
    }

    /// <summary>
    /// A directory that bridge commands may write into. Arguments are file names or relative
    /// paths: rooted paths, drive-relative paths, <c>..</c> segments, ':' (alternate streams), Windows device names and
    /// anything resolving outside the root answer io_denied.
    /// </summary>
    public interface IOutputRoot
    {
        /// <summary>Absolute root directory, or null when none is configured (Resolve then answers io_denied).</summary>
        string Root { get; }
        /// <summary>
        /// The absolute path of <paramref name="relativePath"/> under the root ('/' or '\' separators). An existing file
        /// answers io_denied unless <paramref name="overwrite"/>. Creates the parent directories when asked.
        /// Throws BridgeException (io_denied, or bad_args for an empty name).
        /// </summary>
        string Resolve(string relativePath, bool overwrite = false, bool createDirectory = true);
    }

    /// <summary>One running <c>batch</c> request: one queue entry, members in order.</summary>
    public interface IBatchScope
    {
        /// <summary>Number of members.</summary>
        int Count { get; }
        /// <summary>Index of the member running now (−1 before the first).</summary>
        int Index { get; }
        bool StopOnError { get; }
        /// <summary>The raw JSON text of the batch request's <c>id</c>, or null.</summary>
        string RequestId { get; }
        /// <summary>State that hooks and handlers keep for the batch's lifetime (e.g. Core's model transaction).</summary>
        IDictionary<string, object> Items { get; }
    }

    /// <summary>Observes batches on the main thread (Core opens one transaction per batch).</summary>
    public interface IBatchHook
    {
        /// <summary>Before the first member runs.</summary>
        void BatchBegin(IBatchScope batch);
        /// <summary>After the last member that ran; <paramref name="failedAt"/> = index of the first failed member or −1.</summary>
        void BatchEnd(IBatchScope batch, bool ok, int failedAt);
    }

    /// <summary>The bridge services a command provider works with.</summary>
    public interface IBridgeServices
    {
        ICommandRegistry Commands { get; }
        IBridgeReadiness Readiness { get; }
        IBridgeFrames Frames { get; }
        IOutputRoot Output { get; }
        void AddBatchHook(IBatchHook hook);
        bool RemoveBatchHook(IBatchHook hook);
    }
}

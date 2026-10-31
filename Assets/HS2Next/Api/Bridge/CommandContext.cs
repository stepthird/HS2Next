// What a bridge command handler sees: the argument view over the request
// object, the envelope values, the batch it runs in, the frame clock, the readiness stages and the output root, and
// the means to reply later. Handlers run on the main thread at the bridge pump (Update, execution order −10000)
// unless they defer: a deferred reply may be completed from any later point of the frame loop (a capture at
// LateUpdate 32000), from a later frame, or from another thread (the result must then not change afterwards).
// Part of the stable API (assembly HS2Next.Api).
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HS2Next.Api.Bridge
{
    /// <summary>
    /// A synchronous handler. Returns the result (null, a DTO, an IDictionary&lt;string, object&gt;, a BridgeReply or a
    /// RawJson object), or calls <see cref="ICommandContext.Defer"/> and returns null to reply later. Throws
    /// <see cref="BridgeException"/> to answer with an error; any other exception answers internal (Core) or
    /// plugin_fault (plugin-owned commands).
    /// </summary>
    public delegate object CommandHandler(ICommandContext ctx);

    /// <summary>
    /// An asynchronous handler: the reply is sent when the task completes (a faulted task answers like a thrown
    /// exception; a cancelled one answers timeout). Started on the main thread; Unity's Awaitable can be awaited inside.
    /// </summary>
    public delegate Task<object> AsyncCommandHandler(ICommandContext ctx);

    /// <summary>JSON kind of one argument value.</summary>
    public enum ArgKind { Missing, Null, Bool, Integer, Float, String, Array, Object }

    /// <summary>
    /// Read access to the members of a request object (or of a nested object). Conversions are strict: an int accepts
    /// integral numbers only, a float any number, a bool only true/false, a string a string (numbers are formatted
    /// invariantly), an enum its name (ignoring case) or a defined number; arrays, lists and dictionaries convert
    /// element-wise; HS2Next.Content.ContentId from "&lt;guid&gt;#&lt;id&gt;"; in Unity builds also UnityEngine.Vector2/3/4
    /// ([x,y(,z(,w))]), Quaternion ([x,y,z,w]) and Color ([r,g,b(,a)]); <see cref="RawJson"/> gives the raw text. Replies
    /// write these types in the same forms. A failed conversion throws BridgeException(bad_args) with detail.arg and
    /// detail.expected. An explicit JSON null counts as absent for Has / Get.
    /// </summary>
    public interface ICommandArgs
    {
        /// <summary>Every member name in request order, envelope fields included.</summary>
        IReadOnlyList<string> Names { get; }
        /// <summary>True when the member is present and not null.</summary>
        bool Has(string name);
        ArgKind Kind(string name);
        /// <summary>The converted value, or <paramref name="fallback"/> when absent or null.</summary>
        T Get<T>(string name, T fallback = default);
        /// <summary>False when absent or null; throws bad_args when present but not convertible.</summary>
        bool TryGet<T>(string name, out T value);
        /// <summary>The converted value; bad_args (missing argument) when absent or null.</summary>
        T Require<T>(string name);
        /// <summary>
        /// A new <typeparamref name="TDto"/> whose public fields and properties (as listed by
        /// <see cref="CommandSpec.ArgMembers"/>, named by <see cref="CommandSpec.ArgName"/>) are set from the members
        /// present; absent members keep the DTO's initial values; a [BridgeArg(Required = true)] member that is absent
        /// throws bad_args.
        /// </summary>
        TDto Bind<TDto>() where TDto : new();
        /// <summary>A view of a nested object member, or null when absent or null; bad_args when not an object.</summary>
        ICommandArgs Nested(string name);
        /// <summary>The raw JSON text of the whole object.</summary>
        string Json { get; }
        /// <summary>The raw JSON text of one member, or null when absent.</summary>
        string RawJson(string name);
        /// <summary>
        /// Members that are neither envelope fields nor declared arguments of the command (empty when the spec
        /// allows extra arguments, and for nested views). Reported as warnings, or rejected before the handler runs
        /// under <c>strict</c>.
        /// </summary>
        IReadOnlyList<string> Unknown { get; }
    }

    /// <summary>A reply that a handler completes later (see <see cref="ICommandContext.Defer"/>).</summary>
    public interface IDeferredReply
    {
        /// <summary>True once Complete or Fail was called (later calls are ignored and return false).</summary>
        bool IsCompleted { get; }
        /// <summary>True when the request timed out or the bridge stopped before completion; completing is then ignored.</summary>
        bool IsAbandoned { get; }
        /// <summary>Signalled together with <see cref="IsAbandoned"/>.</summary>
        CancellationToken Cancelled { get; }
        /// <summary>Replies with a result (the same kinds a handler returns). Any thread. False when already done.</summary>
        bool Complete(object result);
        /// <summary>Replies with an error. Any thread. False when already done.</summary>
        bool Fail(BridgeError error);
        /// <summary>Replies as if the handler had thrown <paramref name="e"/>.</summary>
        bool Fail(Exception e);
    }

    /// <summary>The handler's view of one command invocation.</summary>
    public interface ICommandContext
    {
        /// <summary>The name as requested (an alias when the client used one).</summary>
        string Command { get; }
        CommandSpec Spec { get; }
        ICommandArgs Args { get; }
        /// <summary>The raw JSON text of the envelope's <c>id</c>, or null.</summary>
        string RequestId { get; }
        /// <summary>The envelope's <c>waitFrames</c>, or −1 when absent.</summary>
        int WaitFrames { get; }
        /// <summary>The request's effective timeout, seconds.</summary>
        double TimeoutSec { get; }
        /// <summary>The envelope's <c>strict</c>.</summary>
        bool Strict { get; }
        /// <summary>False when the envelope says <c>undo: false</c> (default true).</summary>
        bool Undo { get; }
        /// <summary>The batch this invocation belongs to, or null.</summary>
        IBatchScope Batch { get; }
        IBridgeFrames Frames { get; }
        IBridgeReadiness Readiness { get; }
        /// <summary>The bridge output root (captures and dumps).</summary>
        IOutputRoot Output { get; }
        /// <summary>Signalled when the request times out or the bridge stops.</summary>
        CancellationToken Cancelled { get; }
        /// <summary>Adds a line to the reply's <c>warnings</c>.</summary>
        void Warn(string message);
        /// <summary>
        /// Runs <paramref name="action"/> on the main thread at the first pump after the request's reply was written to
        /// the client (for a batch member: after the batch's reply; also after a timeout reply). For acts that must
        /// not cut the reply off, such as quitting the app.
        /// </summary>
        void AfterReply(Action action);
        /// <summary>Switches this invocation to a deferred reply (idempotent: returns the same object).</summary>
        IDeferredReply Defer();
        bool IsDeferred { get; }
    }
}

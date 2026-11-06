// Helpers around a plugin's in-process bridge calls: a request built from a command name, its arguments and the envelope
// members, and the typed view of a reply that IPluginContext.Request returns. The request is plain data; the reply is
// implemented by the host, which reads the reply text with the bridge's own JSON library, so this assembly keeps no JSON
// dependency and an engine-free plugin compiles against it alone. Part of the stable API (assembly HS2Next.Api).
using System;
using System.Collections;
using System.Collections.Generic;
using HS2Next.Api.Bridge;

namespace HS2Next.Api.Plugins
{
    /// <summary>
    /// One bridge request for <see cref="IPluginContext.Request(CommandRequest)"/> and
    /// <see cref="IPluginContext.RequestAsync(CommandRequest)"/>: the command name, its arguments in the order they were set,
    /// and the envelope members. Plain data, not thread-safe; the host reads it when the request is made.
    /// </summary>
    public sealed class CommandRequest
    {
        readonly ArgList args = new ArgList();

        /// <summary>A request for the command <paramref name="command"/>. ArgumentException when it is null or empty.</summary>
        public CommandRequest(string command)
        {
            if (string.IsNullOrEmpty(command)) throw new ArgumentException("a request needs a command name", nameof(command));
            Command = command;
        }

        /// <summary>The command name (the envelope's <c>cmd</c>).</summary>
        public string Command { get; }

        /// <summary>The arguments, in the order they were first set.</summary>
        public IReadOnlyDictionary<string, object> Args => args;

        /// <summary>The envelope's <c>id</c> (a string or a number), echoed in the reply; null = not sent.</summary>
        public object Id { get; set; }

        /// <summary>The envelope's <c>strict</c>: true makes unknown arguments an error instead of a warning; null = not sent.</summary>
        public bool? Strict { get; set; }

        /// <summary>The envelope's <c>undo</c>: false applies a mutating command without an undo entry; null = not sent.</summary>
        public bool? Undo { get; set; }

        /// <summary>
        /// The envelope's <c>waitFrames</c>, for queued requests: an inline request with a value above 0 is refused as
        /// <see cref="IPluginContext.Call"/> refuses it. 0 = not sent.
        /// </summary>
        public int WaitFrames { get; set; }

        /// <summary>
        /// The envelope's <c>timeout</c> in seconds, for queued requests: an inline request ignores it with Call's warning.
        /// Null = not sent.
        /// </summary>
        public double? Timeout { get; set; }

        /// <summary>
        /// Sets one argument and returns this request; a second value for the same name replaces the first and keeps its
        /// place. A value is what a command result may hold: null, a string, a number, a bool, an array or a list, a
        /// dictionary, a plain object (its public properties and fields), a <see cref="RawJson"/>. ArgumentException for an
        /// empty name and for an envelope member name (<c>cmd</c>, <c>id</c>, <c>waitFrames</c>, <c>timeout</c>,
        /// <c>strict</c>, <c>as</c>, <c>undo</c>): set those through the properties.
        /// </summary>
        public CommandRequest Arg(string name, object value)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("an argument needs a name", nameof(name));
            if (EnvelopeFields.Contains(name))
                throw new ArgumentException("'" + name + "' is an envelope member: set it through the request's properties", nameof(name));
            args.Set(name, value);
            return this;
        }

        /// <summary>"command(arg, …)" for logs.</summary>
        public override string ToString() => Command + "(" + string.Join(", ", args.Keys) + ")";

        /// <summary>A read-only dictionary view that keeps the insertion order.</summary>
        sealed class ArgList : IReadOnlyDictionary<string, object>
        {
            readonly List<KeyValuePair<string, object>> items = new List<KeyValuePair<string, object>>();
            readonly Dictionary<string, int> index = new Dictionary<string, int>(StringComparer.Ordinal);

            public void Set(string name, object value)
            {
                if (index.TryGetValue(name, out int i)) items[i] = new KeyValuePair<string, object>(name, value);
                else
                {
                    index[name] = items.Count;
                    items.Add(new KeyValuePair<string, object>(name, value));
                }
            }

            public object this[string key] => items[index[key]].Value;
            public int Count => items.Count;

            public IEnumerable<string> Keys
            {
                get { foreach (var kv in items) yield return kv.Key; }
            }

            public IEnumerable<object> Values
            {
                get { foreach (var kv in items) yield return kv.Value; }
            }

            public bool ContainsKey(string key) => key != null && index.ContainsKey(key);

            public bool TryGetValue(string key, out object value)
            {
                if (key != null && index.TryGetValue(key, out int i))
                {
                    value = items[i].Value;
                    return true;
                }
                value = null;
                return false;
            }

            public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => items.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }

    /// <summary>
    /// The typed view of one bridge reply, returned by <see cref="IPluginContext.Request(string, object)"/> and its
    /// overloads. Implemented by the host. Immutable; usable on any thread. Values that come back as <c>object</c> (the
    /// elements of a <c>List&lt;object&gt;</c>, the values of a <c>Dictionary&lt;string, object&gt;</c>, a member read as
    /// <c>object</c>) are plain .NET values: string, long, double, bool, null, <c>List&lt;object&gt;</c> and
    /// <c>Dictionary&lt;string, object&gt;</c>.
    /// </summary>
    public interface ICommandReply
    {
        /// <summary>The reply's <c>ok</c>.</summary>
        bool Ok { get; }

        /// <summary>The reply's <c>cmd</c> (the command name as requested), or null when the reply has none.</summary>
        string Command { get; }

        /// <summary>The error code (a value of <see cref="BridgeCodes"/>), or null when <see cref="Ok"/>.</summary>
        string Code { get; }

        /// <summary>The error message, or null when <see cref="Ok"/>.</summary>
        string Error { get; }

        /// <summary>The reply's <c>warnings</c>; empty when it has none.</summary>
        IReadOnlyList<string> Warnings { get; }

        /// <summary>The reply text, exactly as <see cref="IPluginContext.Call"/> returns it for the same envelope.</summary>
        string Json { get; }

        /// <summary>True when the reply has a top-level member of that name whose value is not JSON null.</summary>
        bool Has(string member);

        /// <summary>
        /// A top-level member converted to <typeparamref name="T"/>, or <paramref name="fallback"/> when it is absent or JSON
        /// null. Numbers convert to int, long, float or double, arrays to <c>T[]</c> or <c>List&lt;T&gt;</c>, objects to
        /// <c>Dictionary&lt;string, object&gt;</c> or a plain class (members by name), strings and bools as they are. A value
        /// that does not convert throws FormatException naming the member.
        /// </summary>
        T Get<T>(string member, T fallback = default);

        /// <summary>A top-level member as JSON text, or null when it is absent.</summary>
        string GetJson(string member);

        /// <summary>
        /// The whole reply converted to <typeparamref name="T"/> (a plain class whose members are named as the reply's);
        /// FormatException when it does not convert.
        /// </summary>
        T As<T>();

        /// <summary>
        /// Throws a <see cref="BridgeException"/> with the reply's code, message and detail when the reply is not ok; does
        /// nothing when it is.
        /// </summary>
        void ThrowIfError();
    }
}

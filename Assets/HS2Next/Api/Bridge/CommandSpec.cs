// Self-description of a bridge command: name, group, summary, arguments with type /
// required / default / doc, returned fields, flags, version and owner. `help` is generated from the registered specs,
// and the dispatcher uses the declared arguments to report unknown ones (warning, or bad_args under `strict`). The
// argument list can be generated from the attributes of the handler's argument DTO (WithArgs<T>), so the schema
// cannot drift from the code that binds the arguments (ICommandArgs.Bind<T>). A spec is frozen when it is
// registered; later changes throw InvalidOperationException. Part of the stable API (assembly HS2Next.Api).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace HS2Next.Api.Bridge
{
    /// <summary>Command properties shown by <c>help</c>.</summary>
    [Flags]
    public enum CommandFlags
    {
        /// <summary>No property.</summary>
        None = 0,
        /// <summary>No side effects (a read command).</summary>
        ReadOnly = 1,
        /// <summary>Changes the scene or app state.</summary>
        Mutates = 2,
        /// <summary>Writes files (under the output root or the save folders).</summary>
        WritesFiles = 4,
        /// <summary>May take seconds or longer (loads); clients should allow long timeouts.</summary>
        Slow = 8,
        /// <summary>
        /// The handler may answer later than it returns (a capture at the end of the frame, a load over frames): an
        /// in-process synchronous call refuses the command before it runs (use the queued call instead).
        /// </summary>
        Defers = 16,
        /// <summary>
        /// Outside the compatibility promise: the command may change or disappear in any minor release (UI introspection,
        /// dumps, diagnostics). <c>help</c> lists it as "experimental".
        /// </summary>
        Experimental = 32,
    }

    /// <summary>When the readiness gate lets a command run.</summary>
    public enum ReadyGate
    {
        /// <summary>Only when every stage declared as required for the app is ready (the default).</summary>
        App,
        /// <summary>Always (the command does not need the app's content).</summary>
        None,
    }

    /// <summary>
    /// Schema type names of arguments and returned fields: <c>string int float bool vec3 quat euler color node
    /// contentId path enum(a|b) object array&lt;T&gt;</c> and <c>any</c>.
    /// </summary>
    public static class ArgTypes
    {
        /// <summary>A string.</summary>
        public const string String = "string";
        /// <summary>A whole number.</summary>
        public const string Int = "int";
        /// <summary>Any number.</summary>
        public const string Float = "float";
        /// <summary>true or false.</summary>
        public const string Bool = "bool";
        /// <summary>[x, y, z].</summary>
        public const string Vec3 = "vec3";
        /// <summary>[x, y, z, w].</summary>
        public const string Quat = "quat";
        /// <summary>Euler degrees [x, y, z] (where HS2 used Euler).</summary>
        public const string Euler = "euler";
        /// <summary>[r, g, b, a] in 0–1.</summary>
        public const string Color = "color";
        /// <summary>A scene node id (opaque string).</summary>
        public const string Node = "node";
        /// <summary>"&lt;guid&gt;#&lt;id&gt;" (ContentId.ToString).</summary>
        public const string ContentId = "contentId";
        /// <summary>A file name or relative path (resolved against a root by the handler).</summary>
        public const string Path = "path";
        /// <summary>A JSON object.</summary>
        public const string Object = "object";
        /// <summary>Any JSON value.</summary>
        public const string Any = "any";

        /// <summary>"enum(a|b|c)".</summary>
        public static string Enum(params string[] values) => "enum(" + string.Join("|", values ?? new string[0]) + ")";

        /// <summary>"array&lt;T&gt;".</summary>
        public static string ArrayOf(string elementType) => "array<" + (elementType ?? Any) + ">";

        /// <summary>
        /// The schema type of a CLR type: strings, booleans, integers, floats, enums (their names, camelCase as written),
        /// arrays and lists, dictionaries (object); UnityEngine.Vector3 / Quaternion / Color by name (vec3 / quat /
        /// color); anything else is "object".
        /// </summary>
        public static string Infer(Type t)
        {
            if (t == null) return Any;
            var u = Nullable.GetUnderlyingType(t);
            if (u != null) t = u;
            if (t == typeof(string)) return String;
            if (t == typeof(bool)) return Bool;
            if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) || t == typeof(uint) ||
                t == typeof(ulong) || t == typeof(ushort) || t == typeof(sbyte)) return Int;
            if (t == typeof(float) || t == typeof(double) || t == typeof(decimal)) return Float;
            if (t.IsEnum) return Enum(System.Enum.GetNames(t));
            switch (t.FullName)
            {
                case "UnityEngine.Vector3": return Vec3;
                case "UnityEngine.Quaternion": return Quat;
                case "UnityEngine.Color": return Color;
                case "HS2Next.Content.ContentId": return ContentId;
            }
            if (t == typeof(object)) return Any;
            if (t.IsArray) return ArrayOf(Infer(t.GetElementType()));
            if (typeof(IDictionary).IsAssignableFrom(t)) return Object;
            foreach (var i in t.GetInterfaces())
                if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>)) return Object;
            foreach (var i in t.GetInterfaces())
                if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)) return ArrayOf(Infer(i.GetGenericArguments()[0]));
            return Object;
        }
    }

    /// <summary>One argument or returned field of a command.</summary>
    public sealed class FieldSpec
    {
        /// <summary>A field; a null <paramref name="type"/> is <see cref="ArgTypes.Any"/>.</summary>
        public FieldSpec(string name, string type, string doc = null, bool required = false, object defaultValue = null)
        {
            Name = name;
            Type = type ?? ArgTypes.Any;
            Doc = doc;
            Required = required;
            Default = defaultValue;
        }

        /// <summary>The JSON member name.</summary>
        public string Name { get; }
        /// <summary>A name of <see cref="ArgTypes"/>.</summary>
        public string Type { get; }
        /// <summary>The description shown by help, or null.</summary>
        public string Doc { get; }
        /// <summary>Arguments: the request must give it. Returned fields: always present in a successful reply.</summary>
        public bool Required { get; }
        /// <summary>The value used when the argument is absent (shown by help; null = none or "keeps the current value").</summary>
        public object Default { get; }

        /// <summary>"name: type", with "(required)" when required.</summary>
        public override string ToString() => Name + ": " + Type + (Required ? " (required)" : "");
    }

    /// <summary>Marks a member of an argument DTO (optional: every public field and property counts, see WithArgs).</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class BridgeArgAttribute : Attribute
    {
        /// <summary>Marks the member with the defaults of every property.</summary>
        public BridgeArgAttribute() { }
        /// <summary>Marks the member with its help description.</summary>
        public BridgeArgAttribute(string doc) { Doc = doc; }
        /// <summary>JSON name (default: the member name with a lower-case first letter).</summary>
        public string Name { get; set; }
        /// <summary>Schema type (default: <see cref="ArgTypes.Infer"/> of the member type).</summary>
        public string Type { get; set; }
        /// <summary>The description shown by help.</summary>
        public string Doc { get; set; }
        /// <summary>The request must give the argument (Bind answers bad_args when it is absent).</summary>
        public bool Required { get; set; }
        /// <summary>Default shown by help (default: the member's value in a new instance, unless null).</summary>
        public object Default { get; set; }
    }

    /// <summary>Excludes a public member of an argument DTO from the schema and from binding.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class BridgeIgnoreAttribute : Attribute { }

    /// <summary>The self-description of one command; registered together with its handler.</summary>
    public sealed class CommandSpec
    {
        static readonly Regex NameRx = new Regex(@"^[a-z][A-Za-z0-9]*(\.[a-z][A-Za-z0-9]*)*$", RegexOptions.CultureInvariant);
        static readonly Regex ArgRx = new Regex(@"^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

        readonly List<FieldSpec> args = new List<FieldSpec>();
        readonly List<FieldSpec> returns = new List<FieldSpec>();
        readonly List<string> examples = new List<string>();
        readonly List<string> aliases = new List<string>();
        readonly List<string> requires = new List<string>();
        string group, summary, returnsDoc, since, owner = BridgeOwners.Core;
        CommandFlags flags;
        double defaultTimeoutSec;
        ReadyGate gate = ReadyGate.App;
        bool allowExtraArgs, handlesWaitFrames;

        /// <summary>A spec for the command <paramref name="name"/>. ArgumentException for an invalid name.</summary>
        /// <param name="name">"group.verb" (or a single word): lower-case first letter per part, letters and digits.</param>
        /// <param name="summary">The one-line description shown by help.</param>
        public CommandSpec(string name, string summary = null)
        {
            if (!IsValidName(name)) throw new ArgumentException("invalid command name: " + (name ?? "null"), nameof(name));
            Name = name;
            this.summary = summary;
        }

        /// <summary>True for a valid command name (<c>[a-z][A-Za-z0-9]*</c> parts joined by dots).</summary>
        public static bool IsValidName(string name) => name != null && name.Length <= 64 && NameRx.IsMatch(name);

        /// <summary>True for a valid argument name that is not an envelope field.</summary>
        public static bool IsValidArgName(string name) => name != null && name.Length <= 64 && ArgRx.IsMatch(name) && !EnvelopeFields.Contains(name);

        /// <summary>A spec whose arguments come from <typeparamref name="TArgs"/> (see <see cref="WithArgs{TArgs}"/>).</summary>
        public static CommandSpec For<TArgs>(string name, string summary = null) where TArgs : new() =>
            new CommandSpec(name, summary).WithArgs<TArgs>();

        /// <summary>The command name.</summary>
        public string Name { get; }
        /// <summary>The help group. Default: the part of the name before the first dot (the whole name when it has none).</summary>
        public string Group { get => group ?? (Name.IndexOf('.') > 0 ? Name.Substring(0, Name.IndexOf('.')) : Name); set { Check(); group = value; } }
        /// <summary>The one-line description shown by help.</summary>
        public string Summary { get => summary; set { Check(); summary = value; } }
        /// <summary>The declared arguments, in declaration order.</summary>
        public IReadOnlyList<FieldSpec> Args => args;
        /// <summary>Fields of a successful reply besides ok / cmd / warnings.</summary>
        public IReadOnlyList<FieldSpec> Returns => returns;
        /// <summary>Free text about the reply (shown by <c>help {name}</c>).</summary>
        public string ReturnsDoc { get => returnsDoc; set { Check(); returnsDoc = value; } }
        /// <summary>Example request objects as JSON text (shown by <c>help {name}</c>).</summary>
        public IReadOnlyList<string> Examples => examples;
        /// <summary>The command's properties.</summary>
        public CommandFlags Flags { get => flags; set { Check(); flags = value; } }
        /// <summary>Version in which the command appeared (free text, e.g. "1.0.0"); null = not stated.</summary>
        public string Since { get => since; set { Check(); since = value; } }
        /// <summary><see cref="BridgeOwners"/> or a plugin GUID. Default <see cref="BridgeOwners.Core"/>.</summary>
        public string Owner { get => owner; set { Check(); owner = string.IsNullOrEmpty(value) ? BridgeOwners.Core : value; } }
        /// <summary>Timeout when the request gives none, seconds; 0 = <see cref="BridgeProtocol.DefaultTimeoutSec"/>.</summary>
        public double DefaultTimeoutSec
        {
            get => defaultTimeoutSec;
            set
            {
                Check();
                if (value < 0 || value > BridgeProtocol.MaxTimeoutSec || double.IsNaN(value)) throw new ArgumentOutOfRangeException(nameof(value));
                defaultTimeoutSec = value;
            }
        }
        /// <summary>When the readiness gate lets the command run (default <see cref="ReadyGate.App"/>).</summary>
        public ReadyGate Gate { get => gate; set { Check(); gate = value; } }
        /// <summary>Further readiness stages this command needs besides the gate (e.g. "scene").</summary>
        public IReadOnlyList<string> Requires => requires;
        /// <summary>Old names that still reach this command; the reply then carries a "deprecated: use …" warning.</summary>
        public IReadOnlyList<string> Aliases => aliases;
        /// <summary>True: undeclared arguments are passed through without a warning (and allowed under strict).</summary>
        public bool AllowExtraArgs { get => allowExtraArgs; set { Check(); allowExtraArgs = value; } }
        /// <summary>True: the handler reads <c>waitFrames</c> itself (e.g. a capture); the dispatcher does not delay the reply.</summary>
        public bool HandlesWaitFrames { get => handlesWaitFrames; set { Check(); handlesWaitFrames = value; } }
        /// <summary>True once registered.</summary>
        public bool IsFrozen { get; private set; }

        /// <summary>The declared argument of that name, or null.</summary>
        public FieldSpec FindArg(string name)
        {
            foreach (var a in args) if (a.Name == name) return a;
            return null;
        }

        /// <summary>Adds an argument.</summary>
        public CommandSpec Arg(string name, string type, string doc = null, bool required = false, object defaultValue = null)
        {
            Check();
            if (!IsValidArgName(name)) throw new ArgumentException("invalid argument name (or an envelope field): " + (name ?? "null"), nameof(name));
            if (FindArg(name) != null) throw new ArgumentException("argument declared twice: " + name, nameof(name));
            args.Add(new FieldSpec(name, type, doc, required, required ? null : defaultValue));
            return this;
        }

        /// <summary>Adds a returned field.</summary>
        public CommandSpec Return(string name, string type, string doc = null, bool always = true)
        {
            Check();
            returns.Add(new FieldSpec(name, type, doc, always));
            return this;
        }

        /// <summary>Adds an example request (JSON text) and returns this spec.</summary>
        public CommandSpec Example(string requestJson) { Check(); examples.Add(requestJson); return this; }
        /// <summary>Adds an old name that still reaches the command and returns this spec. ArgumentException for an invalid name.</summary>
        public CommandSpec Alias(string oldName)
        {
            Check();
            if (!IsValidName(oldName)) throw new ArgumentException("invalid alias: " + (oldName ?? "null"), nameof(oldName));
            aliases.Add(oldName);
            return this;
        }
        /// <summary>Adds a readiness stage the command needs and returns this spec (an empty name is ignored).</summary>
        public CommandSpec Require(string stage) { Check(); if (!string.IsNullOrEmpty(stage)) requires.Add(stage); return this; }
        /// <summary>Sets <see cref="Flags"/> and returns this spec.</summary>
        public CommandSpec WithFlags(CommandFlags f) { Flags = f; return this; }
        /// <summary>Sets <see cref="DefaultTimeoutSec"/> and returns this spec.</summary>
        public CommandSpec WithTimeout(double seconds) { DefaultTimeoutSec = seconds; return this; }
        /// <summary>Sets <see cref="Owner"/> and returns this spec.</summary>
        public CommandSpec WithOwner(string o) { Owner = o; return this; }
        /// <summary>Sets <see cref="Since"/> and returns this spec.</summary>
        public CommandSpec WithSince(string s) { Since = s; return this; }
        /// <summary>Sets <see cref="Gate"/> and returns this spec.</summary>
        public CommandSpec WithGate(ReadyGate g) { Gate = g; return this; }

        /// <summary>
        /// Declares one argument per public instance field and property of <typeparamref name="TArgs"/> (except
        /// [BridgeIgnore] ones; fields first, then properties, each in declaration order): name from [BridgeArg(Name)]
        /// or the member name with a lower-case first letter, type from [BridgeArg(Type)] or <see cref="ArgTypes.Infer"/>,
        /// the default from [BridgeArg(Default)] or the member's value in <c>new TArgs()</c> (when not null). The same
        /// names are used by Bind.
        /// </summary>
        public CommandSpec WithArgs<TArgs>() where TArgs : new()
        {
            Check();
            var sample = new TArgs();
            foreach (var m in ArgMembers(typeof(TArgs)))
            {
                var attr = m.GetCustomAttribute<BridgeArgAttribute>();
                object def = attr?.Default;
                if (def == null && !(attr?.Required ?? false))
                {
                    try { def = m is FieldInfo f ? f.GetValue(sample) : ((PropertyInfo)m).GetValue(sample); }
                    catch (Exception) { def = null; }
                }
                Arg(ArgName(m), attr?.Type ?? ArgTypes.Infer(MemberType(m)), attr?.Doc, attr?.Required ?? false, def);
            }
            return this;
        }

        /// <summary>Public instance fields and writable properties of a DTO without [BridgeIgnore] (fields, then properties).</summary>
        public static IEnumerable<MemberInfo> ArgMembers(Type t)
        {
            var list = new List<MemberInfo>();
            foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.GetCustomAttribute<BridgeIgnoreAttribute>() != null) continue;
                if (m is FieldInfo f && !f.IsInitOnly && !f.IsLiteral) list.Add(m);
                else if (m is PropertyInfo p && p.CanWrite && p.GetIndexParameters().Length == 0 && p.SetMethod != null && p.SetMethod.IsPublic) list.Add(m);
            }
            list.Sort((a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
            return list;
        }

        /// <summary>The JSON name of a DTO member: [BridgeArg(Name)] or the member name with a lower-case first letter.</summary>
        public static string ArgName(MemberInfo m)
        {
            var n = m.GetCustomAttribute<BridgeArgAttribute>()?.Name;
            if (!string.IsNullOrEmpty(n)) return n;
            return m.Name.Length > 0 && char.IsUpper(m.Name[0]) ? char.ToLowerInvariant(m.Name[0]) + m.Name.Substring(1) : m.Name;
        }

        /// <summary>The value type of a field or property.</summary>
        public static Type MemberType(MemberInfo m) => m is FieldInfo f ? f.FieldType : ((PropertyInfo)m).PropertyType;

        /// <summary>Called by the registry; afterwards every setter throws.</summary>
        public void Freeze() => IsFrozen = true;

        void Check()
        {
            if (IsFrozen) throw new InvalidOperationException("command spec " + Name + " is registered and cannot change");
        }

        /// <summary>"name(arg, …)".</summary>
        public override string ToString()
        {
            var sb = new StringBuilder(Name);
            sb.Append('(');
            for (int i = 0; i < args.Count; i++) sb.Append(i == 0 ? "" : ", ").Append(args[i].Name);
            return sb.Append(')').ToString();
        }
    }
}

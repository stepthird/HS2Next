// Scene scripts: the substitution of roles in steps and timed commands, and the preparation's mark. Unity-free.
//   A string value at any depth of a request's objects and arrays that equals "$" + a declared role becomes that role's node
//   id; "$$..." becomes "$..." (one "$" removed: the escape). Member names, other strings and strings that merely contain "$"
//   are untouched. A string "$name" whose name has the form of a role but names no declared role is reported (the step or
//   timed command is skipped at play); a declared role that is not cast is reported (with partial the step is skipped).
//   A step's "note" members, at any depth, are removed before it is sent; a timed command keeps its note outside its request.
//   The preparation's mark: the first 16 hex of SHA-256 of the canonical JSON of the preparation steps after substitution.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes.Scripts
{
    /// <summary>What one substitution found.</summary>
    public sealed class ScriptSubstResult
    {
        /// <summary>Strings replaced by a node id.</summary>
        public int Replaced;
        /// <summary>Escapes "$$..." turned into "$...".</summary>
        public int Escaped;
        /// <summary>The deepest replacement: the number of objects and arrays around the string (the request itself is 1).</summary>
        public int MaxDepth;
        /// <summary>Strings of the form "$name" whose name is no declared role (distinct, in order of first use).</summary>
        public readonly List<string> UnknownRoles = new List<string>();
        /// <summary>Declared roles named but not cast (distinct, in order of first use).</summary>
        public readonly List<string> UncastRoles = new List<string>();
        /// <summary>Declared roles named (distinct, in order of first use).</summary>
        public readonly List<string> RolesUsed = new List<string>();

        /// <summary>True when the request names no unknown and no uncast role (it can be sent as substituted).</summary>
        public bool Complete => UnknownRoles.Count == 0 && UncastRoles.Count == 0;

        internal void Clear()
        {
            Replaced = Escaped = MaxDepth = 0;
            UnknownRoles.Clear();
            UncastRoles.Clear();
            RolesUsed.Clear();
        }
    }

    public static class ScriptSubst
    {
        /// <summary>The prefix of a role reference.</summary>
        public const char RoleMark = '$';
        /// <summary>The member removed from steps before they are sent.</summary>
        public const string NoteMember = "note";

        /// <summary>True for a role name: [A-Za-z_][A-Za-z0-9_]{0,31} (the save layer's rule).</summary>
        public static bool IsRoleName(string s) => ScriptText.IsRoleName(s);

        /// <summary>True when <paramref name="s"/> is "$" + a role name (declared or not); <paramref name="role"/> is the name.</summary>
        public static bool IsRoleRef(string s, out string role)
        {
            role = ScriptText.RoleOf(s);
            return role != null;
        }

        /// <summary>"$" + <paramref name="role"/>.</summary>
        public static string Ref(string role) => ScriptText.RoleRef(role);

        /// <summary>
        /// A deep copy of <paramref name="token"/> with every role reference of a declared role replaced by its node id
        /// (<paramref name="cast"/>: role → node id; a declared role absent from it stays as written and is reported uncast),
        /// every "$$..." turned into "$...", and, with <paramref name="stripNotes"/>, every member named "note" removed at any
        /// depth. <paramref name="result"/> (cleared first when given) collects the counts and the roles found.
        /// </summary>
        public static JToken Apply(JToken token, ICollection<string> declared, IReadOnlyDictionary<string, string> cast, bool stripNotes, ScriptSubstResult result = null)
        {
            var r = result ?? new ScriptSubstResult();
            if (result != null) result.Clear();
            return Walk(token, declared, cast, stripNotes, r, 0);
        }

        /// <summary>
        /// A step or timed command as it is sent: <see cref="Apply"/> on the request (notes removed for steps,
        /// <paramref name="stripNotes"/>). Null for a null request.
        /// </summary>
        public static JObject Request(JObject request, ICollection<string> declared, IReadOnlyDictionary<string, string> cast, bool stripNotes, ScriptSubstResult result = null) =>
            request == null ? null : (JObject)Apply(request, declared, cast, stripNotes, result);

        static JToken Walk(JToken t, ICollection<string> declared, IReadOnlyDictionary<string, string> cast, bool stripNotes, ScriptSubstResult r, int depth)
        {
            if (t == null) return null;
            switch (t.Type)
            {
                case JTokenType.Object:
                {
                    var o = new JObject();
                    foreach (var p in ((JObject)t).Properties())
                    {
                        if (stripNotes && p.Name == NoteMember) continue;
                        o.Add(p.Name, Walk(p.Value, declared, cast, stripNotes, r, depth + 1));
                    }
                    return o;
                }
                case JTokenType.Array:
                {
                    var a = new JArray();
                    foreach (var e in (JArray)t) a.Add(Walk(e, declared, cast, stripNotes, r, depth + 1));
                    return a;
                }
                case JTokenType.String:
                {
                    string s = (string)t;
                    if (s.Length < 2 || s[0] != RoleMark) return t.DeepClone();
                    if (s[1] == RoleMark)
                    {
                        r.Escaped++;
                        return new JValue(s.Substring(1));
                    }
                    if (!IsRoleRef(s, out string role)) return t.DeepClone();
                    if (declared == null || !declared.Contains(role))
                    {
                        if (!r.UnknownRoles.Contains(role)) r.UnknownRoles.Add(role);
                        return t.DeepClone();
                    }
                    if (!r.RolesUsed.Contains(role)) r.RolesUsed.Add(role);
                    if (cast == null || !cast.TryGetValue(role, out string node) || string.IsNullOrEmpty(node))
                    {
                        if (!r.UncastRoles.Contains(role)) r.UncastRoles.Add(role);
                        return t.DeepClone();
                    }
                    r.Replaced++;
                    if (depth > r.MaxDepth) r.MaxDepth = depth;
                    return new JValue(node);
                }
                default:
                    return t.DeepClone();
            }
        }

        /// <summary>
        /// The reverse of the substitution for <c>script.capture</c>: a deep copy with every string equal to a cast node id
        /// replaced by "$" + its role, and every other string that starts with "$" escaped as "$$..." (so that a later
        /// substitution gives it back). <paramref name="byNode"/>: node id → role. Returns the number of node ids replaced.
        /// </summary>
        public static JToken Unapply(JToken token, IReadOnlyDictionary<string, string> byNode, out int replaced)
        {
            int n = 0;
            var c = Back(token, byNode, ref n);
            replaced = n;
            return c;
        }

        static JToken Back(JToken t, IReadOnlyDictionary<string, string> byNode, ref int n)
        {
            if (t == null) return null;
            switch (t.Type)
            {
                case JTokenType.Object:
                {
                    var o = new JObject();
                    foreach (var p in ((JObject)t).Properties()) o.Add(p.Name, Back(p.Value, byNode, ref n));
                    return o;
                }
                case JTokenType.Array:
                {
                    var a = new JArray();
                    foreach (var e in (JArray)t) a.Add(Back(e, byNode, ref n));
                    return a;
                }
                case JTokenType.String:
                {
                    string s = (string)t;
                    if (byNode != null && byNode.TryGetValue(s, out string role) && role != null)
                    {
                        n++;
                        return new JValue(Ref(role));
                    }
                    if (s.Length > 0 && s[0] == RoleMark) return new JValue(RoleMark + s);
                    return t.DeepClone();
                }
                default:
                    return t.DeepClone();
            }
        }

        // ------------------------------------------------------------ the preparation's mark

        /// <summary>The number of hex characters of a mark.</summary>
        public const int MarkLength = 16;

        /// <summary>
        /// The preparation's mark of steps already substituted: the save layer's <see cref="ScriptText.PreparationMark"/> (the first 16 hex of
        /// SHA-256 of the canonical JSON of the array of the steps), so that the stored mark has one definition.
        /// </summary>
        public static string Mark(IEnumerable<JObject> substitutedSteps) => ScriptText.PreparationMark(substitutedSteps);

        /// <summary>The preparation's mark of <paramref name="steps"/> as written, substituted with <paramref name="cast"/>.</summary>
        public static string Mark(IEnumerable<JObject> steps, ICollection<string> declared, IReadOnlyDictionary<string, string> cast)
        {
            var list = new List<JObject>();
            if (steps != null) foreach (var s in steps) list.Add(Request(s, declared, cast, true));
            return Mark(list);
        }
    }
}

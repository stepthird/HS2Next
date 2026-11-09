// Scene scripts: casting the roles of a script to nodes of the scene, the part that needs only the model. Unity-free.
//   A character role, in this order: (1) the scene script's cast entry, when that node is a character in the model (two roles may name one
//   node); (2) a character whose
//   name (its tree label) equals the hint's name, cast to no other role of this script, first in scene order; (3) with load and a hint card,
//   chara.load (the player); (4) uncast. A prop role: (1) the cast entry, when that node is an item in the model; (2) an item node whose row
//   is the role's ref and whose parent is the parent role's node (the scene root without a parent role), cast to no other role; (3) with load,
//   item.add + item.parent + obj.transform (the player); (4) uncast. A character whose sex differs from the role's: a warning, cast anyway.
using System;
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes.Scripts
{
    /// <summary>How a role came to its node.</summary>
    public enum ScriptCastSource
    {
        /// <summary>Not cast.</summary>
        None = 0,
        /// <summary>The scene script's cast entry.</summary>
        Cast = 1,
        /// <summary>A character found by the hint's name, or an item by its row and parent.</summary>
        Found = 2,
        /// <summary>Loaded by the play (chara.load, item.add).</summary>
        Loaded = 3,
    }

    /// <summary>The casting of one play: role → node, how each was found, the roles left, warnings.</summary>
    public sealed class ScriptCasting
    {
        public readonly Dictionary<string, string> Cast = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, ScriptCastSource> Source = new Dictionary<string, ScriptCastSource>(StringComparer.Ordinal);
        public readonly List<string> Warnings = new List<string>();

        public bool IsCast(string role) => role != null && Cast.ContainsKey(role);

        /// <summary>Casts <paramref name="role"/> to <paramref name="node"/>.</summary>
        public void Set(string role, string node, ScriptCastSource source)
        {
            Cast[role] = node;
            Source[role] = source;
        }

        /// <summary>True when another role of this casting holds <paramref name="node"/>.</summary>
        public bool Taken(string node, string exceptRole = null)
        {
            foreach (var kv in Cast) if (kv.Value == node && kv.Key != exceptRole) return true;
            return false;
        }

        /// <summary>The declared roles of <paramref name="s"/> that are not cast, in declaration order.</summary>
        public List<string> Uncast(SceneScript s)
        {
            var l = new List<string>();
            if (s?.Roles != null) foreach (var r in s.Roles) if (r?.Role != null && !Cast.ContainsKey(r.Role)) l.Add(r.Role);
            return l;
        }
    }

    public static class ScriptRoles
    {
        /// <summary>0 male, 1 female, null unknown: the sex of a character node from its stored document or its card.</summary>
        public static int? SexOf(TreeNode n)
        {
            if (n == null || n.Kind != NodeKind.Chara) return null;
            var p = n.Appearance?.Stored?.Chara?.Profile;
            if (p != null) return p.Sex;
            var c = n.Appearance?.Card;
            return c != null ? c.Sex : (int?)null;
        }

        /// <summary>"female" / "male" of a role's sex as 1 / 0; null for either.</summary>
        public static int? SexCode(string sex) => sex == ScriptRole.SexFemale ? 1 : sex == ScriptRole.SexMale ? 0 : (int?)null;

        /// <summary>The name a character role's hint is matched against: the node's label (its stored name, else the profile or card name).</summary>
        public static string NameOf(TreeNode n) => n?.Label;

        /// <summary>The row of an item node ("guid#no"), or null.</summary>
        public static string RefOf(TreeNode n) => n?.State is ItemNodeState it ? it.Ref : null;

        /// <summary>
        /// Paths (1) and (2) for every character role of <paramref name="s"/> over <paramref name="model"/>, into <paramref name="casting"/>
        /// (roles already cast there are kept). A role left uncast may be loaded by the player (3).
        /// </summary>
        public static void CastCharacters(SceneScript s, SceneModel model, ScriptCasting casting)
        {
            if (s?.Roles == null || model == null) return;
            // (1) the cast entries first, so that (2) never takes a node an entry names
            foreach (var r in s.Roles)
            {
                if (r == null || !r.IsChara || casting.IsCast(r.Role)) continue;
                string id = s.CastOf(r.Role);
                var n = id != null ? model.Find(id) : null;
                if (n != null && n.Kind == NodeKind.Chara) { casting.Set(r.Role, id, ScriptCastSource.Cast); SexWarning(r, n, casting); }
            }
            foreach (var r in s.Roles)
            {
                if (r == null || !r.IsChara || casting.IsCast(r.Role)) continue;
                string name = r.Hint?.Name;
                if (string.IsNullOrEmpty(name)) continue;
                foreach (var n in model.PreOrder())
                {
                    if (n.Kind != NodeKind.Chara || casting.Taken(n.Id) || !string.Equals(NameOf(n), name, StringComparison.Ordinal)) continue;
                    casting.Set(r.Role, n.Id, ScriptCastSource.Found);
                    SexWarning(r, n, casting);
                    break;
                }
            }
        }

        /// <summary>
        /// Paths (1) and (2) for every prop role of <paramref name="s"/> whose parent role (if any) is cast in <paramref name="casting"/>; props
        /// that hang under other props are resolved after their parents.
        /// </summary>
        public static void CastItems(SceneScript s, SceneModel model, ScriptCasting casting)
        {
            if (s?.Roles == null || model == null) return;
            foreach (var r in s.Roles)
            {
                if (r == null || !r.IsItem || casting.IsCast(r.Role)) continue;
                string id = s.CastOf(r.Role);
                var n = id != null ? model.Find(id) : null;
                if (n != null && n.Kind == NodeKind.Item) casting.Set(r.Role, id, ScriptCastSource.Cast);
            }
            bool progress = true;
            while (progress)
            {
                progress = false;
                foreach (var r in s.Roles)
                {
                    if (r == null || !r.IsItem || casting.IsCast(r.Role) || string.IsNullOrEmpty(r.Ref)) continue;
                    string parentNode = null;
                    if (r.Parent?.Role != null)
                    {
                        if (!casting.Cast.TryGetValue(r.Parent.Role, out parentNode)) continue;   // the parent first
                    }
                    foreach (var n in model.PreOrder())
                    {
                        if (n.Kind != NodeKind.Item || casting.Taken(n.Id) || !string.Equals(RefOf(n), r.Ref, StringComparison.Ordinal)) continue;
                        string p = n.Parent?.Id;
                        if (!string.Equals(p, parentNode, StringComparison.Ordinal)) continue;
                        casting.Set(r.Role, n.Id, ScriptCastSource.Found);
                        progress = true;
                        break;
                    }
                }
            }
        }

        static void SexWarning(ScriptRole r, TreeNode n, ScriptCasting casting)
        {
            int? want = SexCode(r.Sex), have = SexOf(n);
            if (want.HasValue && have.HasValue && want.Value != have.Value)
                casting.Warnings.Add("role " + r.Role + " is " + r.Sex + " and is cast to a " + (have.Value == 1 ? "female" : "male") + " character (" + n.Id + "): cast anyway");
        }

        /// <summary>The roles of the play's casting that differ from the scene script's cast (cast by (2) or (3), or recast): the cast to write.</summary>
        public static SortedDictionary<string, string> CastToWrite(SceneScript s, ScriptCasting casting)
        {
            var after = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (s?.Cast != null) foreach (var kv in s.Cast) after[kv.Key] = kv.Value;
            foreach (var kv in casting.Cast) after[kv.Key] = kv.Value;
            return after;
        }

        /// <summary>True when two casts hold the same entries.</summary>
        public static bool SameCast(IDictionary<string, string> a, IDictionary<string, string> b)
        {
            int na = a?.Count ?? 0, nb = b?.Count ?? 0;
            if (na != nb) return false;
            if (na == 0) return true;
            foreach (var kv in a) if (!b.TryGetValue(kv.Key, out var v) || v != kv.Value) return false;
            return true;
        }
    }
}

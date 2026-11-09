// JM2, JM3 (Phase 6.9, scene scripts): the substitution of roles in steps and timed commands, and the preparation's mark.
// JM2: "$a" at every depth of objects and arrays becomes the node id, "$$a" becomes "$a", member names and strings that merely
// contain "$" are untouched, an undeclared "$x" and an uncast role are reported, notes are removed from steps only; Present: a
// replacement at depth 3 or more. JM3: the mark is equal for equal substituted steps (also when notes differ) and differs when a
// step or a cast node changes; Present: two casts give two marks. Unity-free.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Scenes.Scripts;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save.Tests;

    public static class SceneTests_Scripts
    {
        const string A = "0123456789abcdef0123456789abcdef";
        const string B = "fedcba9876543210fedcba9876543210";
        const string C = "00000000000000000000000000000c0c";

        static readonly string[] Declared = { "a", "b", "p" };

        static Dictionary<string, string> Cast(string a, string b) => new Dictionary<string, string>(StringComparer.Ordinal) { ["a"] = a, ["b"] = b };

        public static void Run(TestRunner t)
        {
            Substitution(t);
            Escapes(t);
            Reports(t);
            Notes(t);
            Mark(t);
            Unapply(t);
        }

        // ---------- JM2 ----------

        static void Substitution(TestRunner t)
        {
            var step = JObject.Parse(@"{""cmd"":""obj.moveTo"",""key"":""$a"",""target"":{""key"":""$b"",""bone"":""cf_J_Spine02"",""offset"":[0,0,""$a""]},
                ""deep"":{""x"":{""y"":{""z"":""$a""}},""list"":[[[""$b""]]]},""$a"":1,""from"":""$a""}");
            var r = new ScriptSubstResult();
            var s = ScriptSubst.Request(step, Declared, Cast(A, B), false, r);
            t.Equal(A, (string)s["key"], "JM2: depth 1 key");
            t.Equal(B, (string)s["target"]["key"], "JM2: depth 2 target.key");
            t.Equal(A, (string)s["target"]["offset"][2], "JM2: depth 3 inside an array of an object");
            t.Equal(A, (string)s["deep"]["x"]["y"]["z"], "JM2: depth 4 in objects");
            t.Equal(B, (string)s["deep"]["list"][0][0][0], "JM2: depth 5 in arrays");
            t.Equal(A, (string)s["from"], "JM2: a string member (from)");
            t.Check(s.Property("$a") != null && (int)s["$a"] == 1, "JM2: a member name is untouched");
            t.Equal("cf_J_Spine02", (string)s["target"]["bone"], "JM2: other strings untouched");
            t.Equal(6, r.Replaced, "JM2: replacements counted");
            t.Check(r.MaxDepth >= 3, "JM2 Present: a replacement at depth >= 3 (deepest " + r.MaxDepth + ")");
            t.Info("JM2 Present: replacements " + r.Replaced + ", deepest at depth " + r.MaxDepth);
            t.Equal("$a", (string)step["key"], "JM2: the input is not changed");
            t.Check(r.Complete, "JM2: no unknown or uncast role");
        }

        static void Escapes(TestRunner t)
        {
            var step = JObject.Parse(@"{""cmd"":""x"",""a"":""$$a"",""b"":""$$"",""c"":""price $a"",""d"":""$a b"",""e"":""$5"",""f"":""a$"",""g"":""$"",""h"":""$$$a"",""i"":""$a.b""}");
            var r = new ScriptSubstResult();
            var s = ScriptSubst.Request(step, Declared, Cast(A, B), false, r);
            t.Equal("$a", (string)s["a"], "JM2: \"$$a\" -> \"$a\"");
            t.Equal("$", (string)s["b"], "JM2: \"$$\" -> \"$\"");
            t.Equal("price $a", (string)s["c"], "JM2: a string that contains \"$a\" is untouched");
            t.Equal("$a b", (string)s["d"], "JM2: \"$a b\" is untouched");
            t.Equal("$5", (string)s["e"], "JM2: \"$5\" (no role form) is untouched");
            t.Equal("a$", (string)s["f"], "JM2: \"a$\" is untouched");
            t.Equal("$", (string)s["g"], "JM2: \"$\" is untouched");
            t.Equal("$$a", (string)s["h"], "JM2: \"$$$a\" -> \"$$a\" (one $ removed)");
            t.Equal("$a.b", (string)s["i"], "JM2: \"$a.b\" is untouched");
            t.Equal(0, r.Replaced, "JM2: escapes replace no role");
            t.Equal(3, r.Escaped, "JM2: three escapes");
            t.Check(r.Complete, "JM2: escapes report nothing");
        }

        static void Reports(TestRunner t)
        {
            var step = JObject.Parse(@"{""cmd"":""x"",""a"":""$x"",""b"":[""$b""],""c"":""$p"",""d"":""$x""}");
            var r = new ScriptSubstResult();
            var s = ScriptSubst.Request(step, Declared, Cast(A, null), false, r);
            t.Check(r.UnknownRoles.Count == 1 && r.UnknownRoles[0] == "x", "JM2: an undeclared \"$x\" is reported once (" + string.Join(",", r.UnknownRoles) + ")");
            t.Equal("$x", (string)s["a"], "JM2: an undeclared role stays as written");
            t.Check(r.UncastRoles.Count == 2 && r.UncastRoles.Contains("b") && r.UncastRoles.Contains("p"), "JM2: uncast roles reported (" + string.Join(",", r.UncastRoles) + ")");
            t.Equal("$b", (string)s["b"][0], "JM2: an uncast role stays as written");
            t.Check(!r.Complete, "JM2: incomplete with an unknown or uncast role");
            t.Check(ScriptSubst.IsRoleName("_a1") && !ScriptSubst.IsRoleName("1a") && !ScriptSubst.IsRoleName("a-b") && !ScriptSubst.IsRoleName(new string('a', 33)) && ScriptSubst.IsRoleName(new string('a', 32)),
                "JM2: the role name form [A-Za-z_][A-Za-z0-9_]{0,31}");
        }

        static void Notes(TestRunner t)
        {
            var step = JObject.Parse(@"{""cmd"":""x"",""note"":""top"",""v"":{""note"":""inner"",""k"":""$a""},""list"":[{""note"":1}]}");
            var s = ScriptSubst.Request(step, Declared, Cast(A, B), true);
            t.Check(s.Property("note") == null && ((JObject)s["v"]).Property("note") == null && ((JObject)s["list"][0]).Property("note") == null, "JM2: a step's notes are removed at any depth");
            t.Equal(A, (string)s["v"]["k"], "JM2: and its roles replaced");
            var kept = ScriptSubst.Request(step, Declared, Cast(A, B), false);
            t.Equal("top", (string)kept["note"], "JM2: a timed command's request keeps its members (stripNotes false)");
        }

        // ---------- JM3 ----------

        static void Mark(TestRunner t)
        {
            var steps = new List<JObject>
            {
                JObject.Parse(@"{""cmd"":""wet.attach"",""key"":""$a"",""preset"":""water"",""note"":""made once""}"),
                JObject.Parse(@"{""wait"":0.5}"),
                JObject.Parse(@"{""cmd"":""ragdoll.build"",""key"":""$b""}"),
            };
            string m1 = ScriptSubst.Mark(steps, Declared, Cast(A, B));
            string m1b = ScriptSubst.Mark(steps, Declared, Cast(A, B));
            t.Check(m1 != null && m1.Length == 16 && JsonText.IsHex(m1, 16, false), "JM3: a mark is 16 lowercase hex (" + m1 + ")");
            t.Equal(m1, m1b, "JM3: equal for equal substituted steps");
            var noteless = new List<JObject> { (JObject)steps[0].DeepClone(), steps[1], steps[2] };
            noteless[0].Remove("note");
            t.Equal(m1, ScriptSubst.Mark(noteless, Declared, Cast(A, B)), "JM3: a note does not change the mark");
            var reordered = new List<JObject> { JObject.Parse(@"{""key"":""$a"",""cmd"":""wet.attach"",""preset"":""water""}"), steps[1], steps[2] };
            t.Check(ScriptSubst.Mark(reordered, Declared, Cast(A, B)) != m1, "JM3: the canonical JSON keeps a step's member order, so a reordered step is another text and another mark");
            string m2 = ScriptSubst.Mark(steps, Declared, Cast(C, B));
            t.Check(m1 != m2, "JM3 Present: two casts give two marks (" + m1 + ", " + m2 + ")");
            var changed = new List<JObject> { steps[0], JObject.Parse(@"{""wait"":0.6}"), steps[2] };
            t.Check(ScriptSubst.Mark(changed, Declared, Cast(A, B)) != m1, "JM3: a changed step changes the mark");
            var substituted = new List<JObject>();
            foreach (var s in steps) substituted.Add(ScriptSubst.Request(s, Declared, Cast(A, B), true));
            t.Equal(m1, ScriptSubst.Mark(substituted), "JM3: the mark of the substituted steps (one definition)");
            t.Info("JM3 Present: marks " + m1 + " (cast a, b) and " + m2 + " (a recast)");
        }

        // ---------- the reverse (script.capture) ----------

        static void Unapply(TestRunner t)
        {
            var d = JObject.Parse(@"{""cmd"":""x"",""key"":""" + A + @""",""t"":{""key"":""" + B + @"""},""lit"":""$a"",""other"":""zz""}");
            var byNode = new Dictionary<string, string>(StringComparer.Ordinal) { [A] = "a", [B] = "b" };
            var back = (JObject)ScriptSubst.Unapply(d, byNode, out int n);
            t.Equal(2, n, "capture: two node ids back to roles");
            t.Equal("$a", (string)back["key"], "capture: key");
            t.Equal("$b", (string)back["t"]["key"], "capture: nested key");
            t.Equal("$$a", (string)back["lit"], "capture: a literal \"$a\" is escaped");
            var again = ScriptSubst.Request(back, Declared, Cast(A, B), false);
            t.Check(JToken.DeepEquals(again, d), "capture: substituting the captured request gives the request back");
        }
    }
}

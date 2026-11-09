// RY10, with no window: the strings of the Ragdoll panel: every key of the block in en, ja and zh-Hans, the same placeholders in each, the words of the block, the history labels as the
// operations name them, the action of the panel in the manifest with no default key, the option of the blood.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HS2Next.UI.Core.Tests
{
    using HS2Next.Save.Tests;
    using HS2Next.UI.Core;

    public static class SchemaTests_Ragdoll
    {
        public static void Run(TestRunner t)
        {
            Action(t);
            Option(t);
            Strings(t);
        }

        static void Action(TestRunner t)
        {
            t.Equal("ragdoll.panel", ActionIds.RagdollPanel, "RY10: the panel's action id");
            t.Check(ActionManifest.Contains(ActionIds.RagdollPanel), "RY10: the action stands in the manifest");
            foreach (var preset in new[] { Keymap.DefaultPreset, Keymap.Hs2StudioPreset })
            {
                string json = UiKit.ReadText(t, Path.Combine("Keymaps", preset + ".json"));
                t.Check(!json.Contains(ActionIds.RagdollPanel), "RY10: " + preset + " binds no key to it");
            }
        }

        static void Option(TestRunner t)
        {
            t.Equal("studio.blood", OptionDefs.StudioBlood, "RY10: the option's key");
            var d = OptionDefs.Get(OptionDefs.StudioBlood);
            t.Check(d.Kind == OptionKind.Bool && Equals(d.Default, true), "RY10: the option is a switch, on by default");
            t.Equal("option.studio.blood", d.LabelKey, "RY10: the option's label");
        }

        // the block's rows as the file holds them
        static List<string[]> Block(TestRunner t)
        {
            var rows = new List<string[]>();
            bool inside = false;
            foreach (string line in UiKit.ReadText(t, Path.Combine("L10n", "strings.tsv")).Split('\n'))
            {
                if (line.StartsWith("#", StringComparison.Ordinal)) { inside = line.StartsWith("#U67", StringComparison.Ordinal); continue; }
                if (inside && line.Length > 0) rows.Add(line.Split('\t'));
            }
            return rows;
        }

        // the keys and the texts the panel's list gives them
        static readonly Dictionary<string, string[]> Spec = new Dictionary<string, string[]>
        {
            ["panel.ragdoll"] = new[] { "Ragdoll", "ラグドール", "布娃娃" },
            ["ragdoll.make"] = new[] { "Ragdoll", "ラグドールにする", "变为布娃娃" },
            ["ragdoll.release"] = new[] { "Release", "解除", "解除" },
            ["ragdoll.releaseAll"] = new[] { "Release all", "すべて解除", "全部解除" },
            ["ragdoll.bake"] = new[] { "Bake pose to FK", "姿勢を FK に焼く", "将姿势烘焙到 FK" },
            ["ragdoll.preset.limp"] = new[] { "Limp", "脱力", "松弛" },
            ["ragdoll.preset.stiff"] = new[] { "Stiff", "硬直", "僵硬" },
            ["ragdoll.preset.puppet"] = new[] { "Puppet", "追従", "跟随" },
            ["ragdoll.preset.hipsPin"] = new[] { "Hips pin", "腰留め", "固定腰部" },
            ["ragdoll.mode.off"] = new[] { "Off", "使わない", "不使用" },
            ["ragdoll.grab"] = new[] { "Grab", "つかむ", "抓取" },
            ["ragdoll.shoot"] = new[] { "Shoot", "撃つ", "射击" },
            ["ragdoll.pin"] = new[] { "Pin", "留める", "固定" },
            ["ragdoll.unpin"] = new[] { "Unpin", "留めを外す", "取消固定" },
            ["ragdoll.tie"] = new[] { "Tie", "つなぐ", "绑定" },
            ["ragdoll.hold"] = new[] { "Hold pose", "姿勢を保つ", "保持姿势" },
            ["ragdoll.push"] = new[] { "Push", "押す", "推" },
            ["ragdoll.stainOnHit"] = new[] { "Stain on hit", "当たった所に染み", "命中处留痕" },
            ["options.blood"] = new[] { "Show blood", "血を表示", "显示血迹" },
            ["ragdoll.state.up"] = new[] { "Up", "立ち", null },
            ["ragdoll.state.down"] = new[] { "Down", "倒れ", null },
            ["ragdoll.state.gettingUp"] = new[] { "Getting up", "起き上がり", null },
            ["ragdoll.state.limp"] = new[] { "Limp", "脱力", null },
            ["ragdoll.tool.hint"] = new[]
            {
                "Drag: grab. Ctrl: push. Shift: throw. Wheel: depth. Space: pin. T: pin to selected. N: follower. Esc: leave.",
                "ドラッグ: つかむ。Ctrl: 押す。Shift: 投げる。ホイール: 奥行き。Space: 留める。T: 選択に留める。N: 追従フォルダー。Esc: 終わる。", null,
            },
        };

        // every English word the block may use: the words the feature goes by and the mechanical words of its rows, nothing else
        static readonly HashSet<string> Words = new HashSet<string>(StringComparer.Ordinal)
        {
            "a", "all", "alt", "and", "angular", "at", "back", "bake", "ball", "blend", "blood", "box", "build", "camera", "cap", "character", "chest", "clear", "colliders", "compensation", "ctrl", "d",
            "damping", "default", "depth", "distance", "down", "drag", "edit", "esc", "face", "falloff", "feet", "fire", "first", "fixed", "fk", "floor", "follow", "follower", "follows", "foot", "force",
            "forearm", "frequency", "friction", "get", "getting", "grab", "gravity", "h", "hand", "head", "heavy", "height", "hips", "hit", "hold", "in", "kind", "knock", "knockdown", "last", "leave",
            "left", "leg", "limp", "map", "mass", "momentum", "muscle", "muscles", "n", "next", "no", "nothing", "obstacle", "offset", "on", "only", "open", "panel", "part", "pelvis", "per", "pin", "pins",
            "pose", "preset", "press", "primary", "prop", "props", "puppet", "push", "pusher", "rate", "ragdoll", "regain", "release", "relocate", "repeat", "reseat", "right", "room", "scale", "select",
            "selected", "several", "share", "shift", "shin", "shoot", "shots", "shows", "show", "size", "snap", "something", "space", "speed", "stagger", "stain", "stay", "stiff", "stiffness", "substep",
            "t", "takes", "the", "thigh", "throw", "tie", "time", "to", "transfer", "twisted", "up", "upper", "arm", "abdomen", "w", "weaken", "what", "wheel", "world", "settings", "drag", "of", "wheel",
            "unpin", "remove", "selection", "out", "collision",
            "off", "view", "while", "has", "body", "or", "thrown", "is", "never", "capture",   // the tool's mode and the outlines' tooltip (the owner's trial 2026-11-09)
        };

        // words of other subjects, in any language
        static readonly string[] Other =
        {
            "kill", "Lab", "plugin", "HS2", "Studio", "player", "first-person", "gun", "weapon", "corpse", "dead", "death", "murder", "wound", "HP", "殺", "死", "銃", "弾丸", "プレイヤー", "杀", "枪", "尸", "玩家",
        };

        static void Strings(TestRunner t)
        {
            L10nTable tb;
            try { tb = UiKit.Strings(t); }
            catch (Exception e) { t.Check(false, "RY10: strings.tsv loads: " + e.Message); return; }
            var rows = Block(t);
            t.Check(rows.Count >= 120, "RY10: the block #U67 holds its rows (" + rows.Count + ")");
            var keys = new List<string>(rows.Select(r => r[0]));
            t.Equal(keys.Count, keys.Distinct().Count(), "RY10: no key twice in the block");
            foreach (var kv in Spec)
            {
                string[] lang = { "en", "ja", "zh-Hans" };
                t.Check(keys.Contains(kv.Key), "RY10: the block holds " + kv.Key);
                for (int i = 0; i < 3; i++)
                    if (kv.Value[i] != null) t.Equal(kv.Value[i], tb.Cell(kv.Key, lang[i]), "RY10: " + kv.Key + " in " + lang[i]);
            }
            var missing = new List<string>();
            foreach (var k in keys)
                foreach (var lang in new[] { "en", "ja", "zh-Hans" })
                    if (string.IsNullOrEmpty(tb.Cell(k, lang))) missing.Add(k + " (" + lang + ")");
            t.Check(missing.Count == 0, "RY10: " + keys.Count + " keys in en, ja and zh-Hans (" + missing.Count + " missing: " + string.Join(", ", missing.Take(20)) + ")");
            var marks = new Regex(@"\{\d\}");
            foreach (var k in keys)
            {
                var en = marks.Matches(tb.Cell(k, "en") ?? "").Cast<Match>().Select(m => m.Value).OrderBy(x => x).ToList();
                t.Check(en.SequenceEqual(marks.Matches(tb.Cell(k, "ja") ?? "").Cast<Match>().Select(m => m.Value).OrderBy(x => x)) &&
                        en.SequenceEqual(marks.Matches(tb.Cell(k, "zh-Hans") ?? "").Cast<Match>().Select(m => m.Value).OrderBy(x => x)), "RY10: " + k + ": the same placeholders in every language");
            }
            foreach (var r in rows) t.Check(r.Length >= 5 && r[4] == "zh?", "RY10: " + r[0] + ": the zh-Hans cell awaits its review");
            var history = new Dictionary<string, string>
            {
                ["build"] = OpLabels.RagdollBuild, ["release"] = OpLabels.RagdollRelease, ["pin"] = OpLabels.RagdollPin, ["unpin"] = OpLabels.RagdollUnpin, ["tie"] = OpLabels.RagdollTie, ["hold"] = OpLabels.RagdollHold,
                ["settings"] = OpLabels.RagdollSettings, ["pusher"] = OpLabels.RagdollPusher, ["obstacle"] = OpLabels.RagdollObstacle, ["bake"] = OpLabels.RagdollBake, ["follower"] = OpLabels.RagdollFollower,
            };
            foreach (var h in history)
            {
                t.Equal(h.Value, tb.Cell("history.ragdoll." + h.Key, "en"), "RY10: the history label " + h.Key + " in en");
                t.Check(OpLabels.All.Contains(h.Value), "RY10: OpLabels holds «" + h.Value + "»");
            }
            var word = new Regex(@"[A-Za-z]+");
            var outside = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var r in rows)
                foreach (Match m in word.Matches(r[1]))
                    if (!Words.Contains(m.Value.ToLowerInvariant())) outside.Add(r[0] + ": " + m.Value);
            t.Check(outside.Count == 0, "RY10: every English word of the block is on the list (" + outside.Count + " not: " + string.Join("; ", outside.Take(20)) + ")");
            int cells = 0;
            foreach (var r in rows)
                foreach (string cell in r.Skip(1).Take(3))
                {
                    cells++;
                    foreach (var w in Other)
                        t.Check(cell.IndexOf(w, StringComparison.OrdinalIgnoreCase) < 0, "RY10: " + r[0] + " names no other subject (" + w + ")");
                }
            t.Check(cells == rows.Count * 3, "RY10: Present: " + cells + " cells searched");
        }
    }
}

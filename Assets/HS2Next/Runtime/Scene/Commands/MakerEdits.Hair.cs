// MakerEdits, the hair page: the hair item and hair colour consequences («前髪と
// 後ろ髪の色を統一», «根本と毛先の色を自動設定»), InverseLerp / Approximately of the bundle facts, the skin match and the bundle
// edits. The hair gates are in MakerGates.cs. Split out of MakerEdits.cs (Step 0 of Phase 4.4); no change of behaviour.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Content;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    public static partial class MakerEdits
    {
        // ---- hair (9, 10; Q25 quirks 1, 3, 6)

        static void HairItem(PlanState ps, int part, string item)
        {
            string pre = AppearancePaths.Chara("hair." + HairData.PartNames[part]);
            ps.Add(pre + ".bundles", new List<HairBundle>(), true);
            if (!ps.O.KeepColours)
            {
                if (ps.O.Defaults != null && ps.O.Defaults.TryHairAccessoryColors(300 + part, item, out var cols) && cols != null && cols.Length == HairPart.AccessoryColorCount)
                    ps.Add(pre + ".accessoryColors", (Color4[])cols.Clone(), true);
                else ps.Report.Warn("edit.defaultsUnavailable: " + pre + ".accessoryColors kept (no prefab defaults for " + item + ")");
            }
            ps.Add(pre + ".materialOverrides", new List<MaterialOverride>(), true);
        }

        static void HairColours(PlanState ps, int part, string member, object value)
        {
            bool same = !ps.O.NoHairLink && Bool(ps, AppearancePaths.Chara("hair.editor.sameSetting"), true);
            bool auto = Bool(ps, AppearancePaths.Chara("hair.editor.autoSetting"), true);
            bool colourMember = Array.IndexOf(HairColourMembers, member) >= 0;
            if (!colourMember) return;
            var parts = new List<int> { part };
            if (same)
            {
                for (int k = 0; k < HairData.PartNames.Length; k++)
                {
                    if (k == part) continue;
                    parts.Add(k);
                    ps.Add(AppearancePaths.Chara("hair." + HairData.PartNames[k] + "." + member), value, true);
                }
            }
            if (member == "baseColor" && auto && value is Color4 basec)
            {
                Derive(basec, out var top, out var under, out var spec);
                foreach (int k in parts)
                {
                    string pre = AppearancePaths.Chara("hair." + HairData.PartNames[k]);
                    ps.Add(pre + ".topColor", KeepAlpha(top, ps.Current(pre + ".topColor")), true);
                    ps.Add(pre + ".underColor", KeepAlpha(under, ps.Current(pre + ".underColor")), true);
                    ps.Add(pre + ".specular", KeepAlpha(spec, ps.Current(pre + ".specular")), true);
                }
            }
        }

        // ================================================================ hair page

        /// <summary>
        /// The default rate of a bundle axis as the hair unit computes it (4.0's EditContext.Inv, so a materialized list equals
        /// what the unit applies for "not stored"): 0 when Mathf.Approximately(a, b), else Unity's Mathf.InverseLerp =
        /// clamp01((v − a) / (b − a)), float arithmetic.
        /// </summary>
        public static float InverseLerp(float a, float b, float v)
        {
            if (Approximately(a, b)) return 0f;
            float t = (v - a) / (b - a);
            return t < 0f ? 0f : t > 1f ? 1f : t;
        }

        /// <summary>Unity's Mathf.Approximately: |b − a| &lt; max(1e-6 · max(|a|, |b|), 8 · Mathf.Epsilon), with Mathf.Epsilon =
        /// float.Epsilon (the smallest denormal; Unity's value while denormals are not flushed, as on x64).</summary>
        public static bool Approximately(float a, float b) =>
            Math.Abs(b - a) < Math.Max(0.000001f * Math.Max(Math.Abs(a), Math.Abs(b)), float.Epsilon * 8f);

        /// <summary><see cref="InverseLerp(float, float, float)"/> per axis.</summary>
        public static Vec3 InverseLerp(Vec3 a, Vec3 b, Vec3 v) => new Vec3(InverseLerp(a.X, b.X, v.X), InverseLerp(a.Y, b.Y, v.Y), InverseLerp(a.Z, b.Z, v.Z));

        static float Comp(Vec3 v, int c) => c == 0 ? v.X : c == 1 ? v.Y : v.Z;
        static Vec3 WithComp(Vec3 v, int c, float x) => new Vec3(c == 0 ? x : v.X, c == 1 ? x : v.Y, c == 2 ? x : v.Z);

        /// <summary>
        /// «肌の色に合わせる» (J12) on node <paramref name="node"/>: part <paramref name="part"/>'s `underColor` =
        /// HSVToRGB(H, max(0, S − 0.06), max(0, V − 0.06)) of `body.skin.color` (Unity's RGBToHSV / HSVToRGB; the stored alpha of
        /// the under colour kept) and `smoothness` = max(0, `body.skin.gloss` − 0.3) — the shown part only, also with
        /// «前髪と後ろ髪の色を統一» on (HS2: the planner runs with <see cref="MakerEditOptions.NoHairLink"/>). The prefab's
        /// `useSameSkinColorButton` is the page's gate, not checked here. "Edit hair" (the caller's entry); bad_args for a part
        /// outside 0..3, unsupported for a node without documents.
        /// </summary>
        public static CommandResult MatchHairToSkin(Transaction tx, SceneModel model, TreeNode node, int part, MakerEditOptions o, MakerEditReport report)
        {
            var chara = node?.Appearance?.Stored?.Chara;
            if (chara == null) return CommandResult.Error(CommandCodes.Unsupported, "character not imported");
            if (part < 0 || part >= HairData.PartNames.Length) return CommandResult.Error(CommandCodes.BadArgs, "part " + part + " outside 0..3 (back, front, side, option)");
            var sets = SkinMatchSets(chara, part);
            var opts = (o ?? new MakerEditOptions()).Clone();
            opts.NoHairLink = true;
            return Edit(tx, model, node, sets, opts, report);
        }

        /// <summary>The two sets of a skin match of <paramref name="part"/>.</summary>
        public static List<(string path, object value)> SkinMatchSets(CharaDocument chara, int part)
        {
            var skin = chara.Body.Skin;
            RgbToHsv(skin.Color.R, skin.Color.G, skin.Color.B, out float h, out float s, out float v);
            var under = HsvToRgb(h, Math.Max(0f, s - 0.06f), Math.Max(0f, v - 0.06f));
            string pre = AppearancePaths.Chara("hair." + HairData.PartNames[part]);
            return new List<(string, object)>
            {
                (pre + ".underColor", KeepAlpha(under, chara.Hair.Parts[part].UnderColor)),
                (pre + ".smoothness", Math.Max(0f, skin.Gloss - 0.3f)),
            };
        }

        /// <summary>True when <paramref name="list"/> holds exactly <paramref name="n"/> entries with the distinct indices
        /// 0..n−1 (the stored bundles apply; otherwise every bundle takes the prefab default).</summary>
        public static bool BundlesComplete(IReadOnlyList<HairBundle> list, int n)
        {
            if (list == null || list.Count != n) return false;
            var seen = new bool[n];
            foreach (var b in list)
            {
                if (b == null || b.Index < 0 || b.Index >= n || seen[b.Index]) return false;
                seen[b.Index] = true;
            }
            return true;
        }

        /// <summary>The prefab's default bundles (one per boneInfo entry, `noShake` false).</summary>
        public static List<HairBundle> DefaultBundles(IReadOnlyList<HairBundleFacts> facts)
        {
            var r = new List<HairBundle>(facts?.Count ?? 0);
            if (facts != null) foreach (var f in facts) r.Add(f.DefaultBundle());
            return r;
        }

        /// <summary>
        /// The effective bundles of a part (J6; HS2's load rule): copies of the stored list sorted by index when it
        /// holds exactly the prefab's bundle count with the indices 0..n−1, else the prefab defaults. Without facts: the stored
        /// list when it holds the indices 0..m−1 of its own count m ≥ 1, else null.
        /// </summary>
        public static List<HairBundle> EffectiveBundles(IReadOnlyList<HairBundle> stored, IReadOnlyList<HairBundleFacts> facts, out bool fromStored)
        {
            fromStored = false;
            int n = facts?.Count ?? (stored?.Count ?? 0);
            if (stored != null && (facts != null || n > 0) && BundlesComplete(stored, n))
            {
                fromStored = true;
                var r = new List<HairBundle>(n);
                foreach (var b in stored) r.Add(DocClone.Clone(b));
                r.Sort((a, b) => a.Index.CompareTo(b.Index));
                return r;
            }
            return facts != null ? DefaultBundles(facts) : null;
        }

        /// <summary>The bundle facts of node <paramref name="node"/>'s part (the stored item's prefab, the sex's variant), or null.</summary>
        static HairBundleFacts[] BundleFacts(CharaDocument chara, int part, MakerEditOptions o)
        {
            if (o?.Defaults == null) return null;
            var hp = chara.Hair.Parts[part];
            return o.Defaults.TryHairBundles(300 + part, hp.Item, chara.Profile.Sex, out var facts) && facts != null ? facts : null;
        }

        static CommandResult SkipOrFail(MakerEditOptions o, MakerEditReport report, TreeNode node, string code, string msg)
        {
            if (o != null && o.SkipUnlistedRows)
            {
                report?.Warn("edit.bundleSkipped: node " + node.Id + ": " + msg);
                return CommandResult.NoOp(null);
            }
            return CommandResult.Error(code, msg);
        }

        /// <summary>
        /// A bundle edit (3, 6; J6, C10) of part <paramref name="part"/>, bundle <paramref name="index"/> of node
        /// <paramref name="node"/>, as the sets of one call (the caller's entry, "Edit hair"):
        /// 1. materialization: when the stored list is not the effective list (count ≠ the prefab's boneInfo count or
        ///    indices not 0..n−1) the whole `bundles` list is first set to the effective list (the prefab defaults);
        /// 2. the given members of bundle <paramref name="index"/> (`bundles[index].move` / `.rotate` / `.noShake`; position =
        ///    index after the materialization), stored unclamped;
        ///    with <paramref name="reset"/> the values are ignored and the given members return to the prefab default — move and
        ///    rotate when none is given; <paramref name="axis"/> 0..2 narrows move / rotate to that component (a slider row's ↺);
        ///    a given noShake returns to false;
        /// 3. «髪の調整をまとめて行う» (`hair.editor.ctrlTogether`): each move / rotate component that changes against the bundle's
        ///    effective value is set to the same value on every other bundle of the part (move and rotate apart); noShake and
        ///    resets never propagate.
        /// Facts: IPartDefaults.TryHairBundles of the stored item for the character's sex. Without facts a stored list with the
        /// indices 0..m−1 is edited as it is (no reset); otherwise unsupported. Errors: bad_args (part, axis, nothing given),
        /// not_found (index outside 0..n−1), unsupported (no documents, no facts); on a multi-selection
        /// (<see cref="MakerEditOptions.SkipUnlistedRows"/>) not_found / unsupported skip the node with `edit.bundleSkipped`.
        /// </summary>
        public static CommandResult EditHairBundle(Transaction tx, SceneModel model, TreeNode node, int part, int index, Vec3? move, Vec3? rotate, bool? noShake,
            bool reset, int axis, MakerEditOptions o, MakerEditReport report)
        {
            var chara = node?.Appearance?.Stored?.Chara;
            if (chara == null) return CommandResult.Error(CommandCodes.Unsupported, "character not imported");
            if (part < 0 || part >= HairData.PartNames.Length) return CommandResult.Error(CommandCodes.BadArgs, "part " + part + " outside 0..3 (back, front, side, option)");
            if (axis < -1 || axis > 2) return CommandResult.Error(CommandCodes.BadArgs, "axis " + axis + " outside 0..2 (−1 = all)");
            if (!reset && move == null && rotate == null && noShake == null) return CommandResult.Error(CommandCodes.BadArgs, "nothing to set (move, rotate, noShake or reset)");
            var hp = chara.Hair.Parts[part];
            var facts = BundleFacts(chara, part, o);
            var eff = EffectiveBundles(hp.Bundles, facts, out bool fromStored);
            string pname = HairData.PartNames[part];
            if (eff == null)
                return SkipOrFail(o, report, node, CommandCodes.Unsupported, "hair." + pname + ": no prefab facts for " + hp.Item + " and the stored bundles are incomplete");
            if (index < 0 || index >= eff.Count)
                return SkipOrFail(o, report, node, CommandCodes.NotFound, "hair." + pname + ": bundle " + index + " outside 0.." + (eff.Count - 1) + " (" + hp.Item + ")");
            if (reset && facts == null)
                return SkipOrFail(o, report, node, CommandCodes.Unsupported, "hair." + pname + ": a bundle reset needs the prefab facts of " + hp.Item);
            var cur = eff[index];
            Vec3 m = cur.Move, r = cur.Rotate;
            bool ns = cur.NoShake;
            bool setMove = false, setRotate = false, setNoShake = false;
            if (reset)
            {
                bool all = move == null && rotate == null && noShake == null;
                var f = facts[index];
                if (move != null || all) { m = axis < 0 ? f.DefaultMove : WithComp(m, axis, Comp(f.DefaultMove, axis)); setMove = true; }
                if (rotate != null || all) { r = axis < 0 ? f.DefaultRotate : WithComp(r, axis, Comp(f.DefaultRotate, axis)); setRotate = true; }
                if (noShake != null) { ns = false; setNoShake = true; }
            }
            else
            {
                if (move.HasValue) { m = move.Value; setMove = true; }
                if (rotate.HasValue) { r = rotate.Value; setRotate = true; }
                if (noShake.HasValue) { ns = noShake.Value; setNoShake = true; }
            }
            string pre = AppearancePaths.Chara("hair." + pname + ".bundles");
            string El(int i) => pre + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
            var sets = new List<(string, object)>();
            if (!fromStored) sets.Add((pre, eff));
            if (setMove) sets.Add((El(index) + ".move", m));
            if (setRotate) sets.Add((El(index) + ".rotate", r));
            if (setNoShake) sets.Add((El(index) + ".noShake", ns));
            if (!reset && chara.Hair.Editor.CtrlTogether)
            {
                for (int j = 0; j < eff.Count; j++)
                {
                    if (j == index) continue;
                    Vec3 mj = eff[j].Move, rj = eff[j].Rotate;
                    bool cm = false, cr = false;
                    for (int c = 0; c < 3; c++)
                    {
                        if (setMove && !FloatBits.Same(Comp(m, c), Comp(cur.Move, c))) { mj = WithComp(mj, c, Comp(m, c)); cm = true; }
                        if (setRotate && !FloatBits.Same(Comp(r, c), Comp(cur.Rotate, c))) { rj = WithComp(rj, c, Comp(r, c)); cr = true; }
                    }
                    if (cm) sets.Add((El(j) + ".move", mj));
                    if (cr) sets.Add((El(j) + ".rotate", rj));
                }
            }
            return Edit(tx, model, node, sets, o, report);
        }

        /// <summary>
        /// «全て初期化» (J8) of part <paramref name="part"/>: the whole `bundles` list = the prefab defaults, each
        /// bundle keeping its effective `noShake` (HS2 resets the rates only); one whole-list set ("Edit hair"). unsupported
        /// without facts (multi-selection: skipped with `edit.bundleSkipped`), bad_args for a part outside 0..3.
        /// </summary>
        public static CommandResult ResetHairBundles(Transaction tx, SceneModel model, TreeNode node, int part, MakerEditOptions o, MakerEditReport report)
        {
            var chara = node?.Appearance?.Stored?.Chara;
            if (chara == null) return CommandResult.Error(CommandCodes.Unsupported, "character not imported");
            if (part < 0 || part >= HairData.PartNames.Length) return CommandResult.Error(CommandCodes.BadArgs, "part " + part + " outside 0..3 (back, front, side, option)");
            var hp = chara.Hair.Parts[part];
            var facts = BundleFacts(chara, part, o);
            string pname = HairData.PartNames[part];
            if (facts == null) return SkipOrFail(o, report, node, CommandCodes.Unsupported, "hair." + pname + ": a bundle reset needs the prefab facts of " + hp.Item);
            var eff = EffectiveBundles(hp.Bundles, facts, out _);
            var list = DefaultBundles(facts);
            for (int i = 0; i < list.Count; i++) list[i].NoShake = eff[i].NoShake;
            return Edit(tx, model, node, new List<(string, object)> { (AppearancePaths.Chara("hair." + pname + ".bundles"), list) }, o, report);
        }
    }
}

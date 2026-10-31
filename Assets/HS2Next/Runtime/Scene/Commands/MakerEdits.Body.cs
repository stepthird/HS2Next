// MakerEdits, the body pages: the colour preset lists (336 skin, 305 hair)
// and the colour swatch push. Split out of MakerEdits.cs (Step 0 of Phase 4.4); no change of behaviour.
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
        // ================================================================ body pages

        /// <summary>The skin colour preset list (HS2's seven swatch buttons read its vanilla rows 0..6 by id).</summary>
        public const int SkinColourCategory = 336, SkinColourPresetIds = 7;

        /// <summary>The hair colour preset list (HS2's seven «カラーサンプル» buttons read its vanilla rows 0..6 by id;
        /// 6).</summary>
        public const int HairColourCategory = 305, HairColourPresetIds = 7;

        /// <summary>The colour preset list a colour path pushes from: 336 for
        /// `appearance.chara.body.skin.color`, 305 for `appearance.chara.hair.&lt;part&gt;.baseColor` (also relative to
        /// `appearance.`), else -1.</summary>
        public static int ColourPresetCategoryOf(string path)
        {
            string c = FieldPath.Canonical(AppearancePaths.FromRelative(path ?? ""));
            if (c == null) return -1;
            if (c == AppearancePaths.Chara("body.skin.color")) return SkinColourCategory;
            if (AppearancePaths.HairPart(c, out string hm) >= 0 && hm == "baseColor") return HairColourCategory;
            return -1;
        }

        /// <summary>True when <paramref name="rowRef"/> is a row a push of <paramref name="category"/> may name: for 336 and 305 the
        /// vanilla ids 0..6 (HS2's seven buttons; no mod adds rows to either); other lists any reference.</summary>
        public static bool ColourPresetRowAllowed(int category, string rowRef)
        {
            if (!JsonText.IsRef(rowRef ?? "") || !ContentId.TryParse(rowRef, out var key)) return false;
            if (category == SkinColourCategory) return key.IsVanilla && key.Id >= 0 && key.Id < SkinColourPresetIds;
            if (category == HairColourCategory) return key.IsVanilla && key.Id >= 0 && key.Id < HairColourPresetIds;
            return true;
        }

        /// <summary>
        /// A colour swatch push (GB6, J11) on node <paramref name="node"/>:
        /// - `appearance.chara.body.skin.color` ← HSVToRGB(BaseH, BaseS, BaseV) of row <paramref name="rowRef"/> of 336 with alpha 1
        ///   (IAppearanceRows.ColourPreset), one set ("Edit body");
        /// - `appearance.chara.hair.&lt;p&gt;.baseColor` + a row of 305: base / top / under / specular = HSVToRGB of the row's Base /
        ///   Top / Under / Specular columns, each keeping the target's stored alpha, metallic / smoothness = the row's columns, on
        ///   part p — and on the other three parts with «前髪と後ろ髪の色を統一» — as caller paths of one call, so the auto colour
        ///   rule derives nothing (HS2's preset never derives); "Edit hair".
        /// Through the planner (the caller's entry). bad_args for another path, unsupported without lists, not_found for an
        /// unknown row or an id outside 0..6.
        /// </summary>
        public static CommandResult ApplyColourPreset(Transaction tx, SceneModel model, TreeNode node, string path, string rowRef, MakerEditOptions o, MakerEditReport report)
        {
            int cat = ColourPresetCategoryOf(path);
            if (cat < 0) return CommandResult.Error(CommandCodes.BadArgs, "a colour preset push writes appearance.chara.body.skin.color or appearance.chara.hair.<part>.baseColor, not " + path);
            if (o?.Rows == null) return CommandResult.Error(CommandCodes.Unsupported, "colour presets need the Maker lists (no list service is registered)");
            string target = FieldPath.Canonical(AppearancePaths.FromRelative(path));
            if (cat == HairColourCategory)
            {
                if (!ColourPresetRowAllowed(cat, rowRef) || !o.Rows.HairColourPreset(rowRef, out var hv))
                    return CommandResult.Error(CommandCodes.NotFound, rowRef + " is not a swatch row of colour preset list 305 (vanilla ids 0.." + (HairColourPresetIds - 1) + ")");
                var chara = node?.Appearance?.Stored?.Chara;
                if (chara == null) return CommandResult.Error(CommandCodes.Unsupported, "character not imported");
                return Edit(tx, model, node, HairPresetSets(chara.Hair, AppearancePaths.HairPart(target, out _), hv), o, report);
            }
            if (!ColourPresetRowAllowed(cat, rowRef) || !o.Rows.ColourPreset(cat, rowRef, out var value, out _))
                return CommandResult.Error(CommandCodes.NotFound, rowRef + " is not a swatch row of colour preset list " + cat.ToString(CultureInfo.InvariantCulture) +
                                                                  (cat == SkinColourCategory ? " (vanilla ids 0.." + (SkinColourPresetIds - 1) + ")" : ""));
            return Edit(tx, model, node, new List<(string, object)> { (target, new Color4(value.R, value.G, value.B, 1f)) }, o, report);
        }

        /// <summary>The sets of a hair preset push: the six members on <paramref name="part"/> and, with same setting,
        /// on the other parts; colours with the target's stored alpha.</summary>
        public static List<(string path, object value)> HairPresetSets(HairData hair, int part, HairColourPresetValues v)
        {
            var sets = new List<(string, object)>();
            bool same = hair?.Editor?.SameSetting ?? true;
            var parts = new List<int> { part };
            if (same) for (int k = 0; k < HairData.PartNames.Length; k++) if (k != part) parts.Add(k);
            foreach (int k in parts)
            {
                var hp = hair?.Parts[k];
                string pre = AppearancePaths.Chara("hair." + HairData.PartNames[k]);
                sets.Add((pre + ".baseColor", KeepAlpha(v.Base, hp?.BaseColor)));
                sets.Add((pre + ".topColor", KeepAlpha(v.Top, hp?.TopColor)));
                sets.Add((pre + ".underColor", KeepAlpha(v.Under, hp?.UnderColor)));
                sets.Add((pre + ".specular", KeepAlpha(v.Specular, hp?.Specular)));
                sets.Add((pre + ".metallic", v.Metallic));
                sets.Add((pre + ".smoothness", v.Smoothness));
            }
            return sets;
        }
    }
}

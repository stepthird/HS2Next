// Read model of a built character's clothes (the clothes pages of the Maker, the bridge's chara.edit.clothes, the gates'
// facts and the tests): per kind 0..7 the document's item, the list row the part unit resolved and its columns (prefab,
// one-piece, NotBra, state type, break mask, the mask textures, the colour textures), whether the part is realized or
// skipped by a one-piece top, the facts of the realized prefab's CmpClothes (the colour sets it uses, the main colour's
// alpha, the break, the option parts, the def / half objects, the colour groups with their renderers, slots and main
// texture size) and its defaults as document values, the compositing passes (per colour group the colour and the detail
// creator with their RT sizes), every material slot of the colour groups as written (_MainTex / _DetailMainTex, _AlphaEx),
// the objects' activity (def / half, option parts) and the inputs of the clothes masks. Mirrors the HS2 side's
// chara.clothesInfo. Nothing here writes the character. Unity-dependent; numbers and data names only.
using System.Collections.Generic;
using System.Linq;
using AIChara;
using HS2Next.Chara.Edit.Units;
using HS2Next.Content;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Chara.Edit
{
    public sealed class ClothesReport
    {
        /// <summary>The character is male (kinds 0, 1, 4, 7 use the male lists; 2, 3, 5, 6 the female lists).</summary>
        public bool Male;
        /// <summary>The eight kinds in HS2 order (top, bottom, innerTop, innerBottom, gloves, pantyhose, socks, shoes).</summary>
        public readonly KindReport[] Kinds = new KindReport[8];
        /// <summary>What the masks unit recorded for the state-dependent masks.</summary>
        public MaskInputs Masks = new MaskInputs();

        /// <summary>The list columns a kind report carries (empty and absent columns are left out).</summary>
        public static readonly string[] RowColumns =
        {
            "MainAB", "MainData", "MainData02", "Coordinate", "NotBra", "StateType", "BreakDisableMask",
            "MainTex", "MainTex02", "MainTex03", "ColorMaskTex", "ColorMask02Tex", "ColorMask03Tex",
            "OverBodyMaskAB", "OverBodyMask", "OverBraMaskAB", "OverBraMask", "OverBodyBMaskAB", "OverBodyBMask",
            "OverInnerTBMaskAB", "OverInnerTBMask", "OverInnerBMaskAB", "OverInnerBMask", "OverPanstMaskAB", "OverPanstMask",
        };

        public sealed class KindReport
        {
            /// <summary>0..7 and "top" … "shoes".</summary>
            public int Kind;
            public string Name;
            /// <summary>The list category of the kind for the character's sex.</summary>
            public int Category;
            /// <summary>The document's item reference ("guid#id").</summary>
            public string Item;
            /// <summary>The row the part unit resolved ("category:key"; the "none" row when the item did not resolve), null
            /// before the unit ran.</summary>
            public string Row;
            /// <summary>The row's columns of <see cref="RowColumns"/> (non-empty values only).</summary>
            public readonly SortedDictionary<string, string> Columns = new SortedDictionary<string, string>(System.StringComparer.Ordinal);
            /// <summary>The prefab asset name of the row (MainData; MainData02 for male rows that name one); null without a row.</summary>
            public string Prefab;
            /// <summary>With <c>served</c> asked for: the source the catalog serves the prefab from ("zipmod" / "abdata" / "none")
            /// and the serving zipmod's GUID (null for abdata), as the per-asset redirect decides; null otherwise.</summary>
            public string ServedKind, ServedGuid;
            /// <summary>The part was skipped by a one-piece top (bottom) or inner top (inner bottom).</summary>
            public bool Skipped;
            /// <summary>The part object exists (CharaContext.Clothes[k]; the "none" rows have none).</summary>
            public bool Realized;
            /// <summary>The realized object carries a CmpClothes ("the kind has a prefab").</summary>
            public bool HasCmpClothes;
            /// <summary>useColorN01..03 and useColorA01..03 (index 0..2).</summary>
            public readonly bool[] UseColorN = new bool[3], UseColorA = new bool[3];
            public bool UseBreak;
            /// <summary>Live objects of objOpt01 / objOpt02.</summary>
            public int Opt01, Opt02;
            /// <summary>objTopDef, objTopHalf, objBotDef, objBotHalf are set.</summary>
            public bool HasTopDef, HasTopHalf, HasBotDef, HasBotHalf;
            /// <summary>The row's Coordinate column is "1" (a one-piece top / inner top), its NotBra column is "1".</summary>
            public bool OnePiece, NotBra;
            /// <summary>The row's StateType column as listed (null when absent).</summary>
            public string StateType;
            /// <summary>uvScalePattern, defMainColor04, defGloss04, defMetallic04 (constants of the creators).</summary>
            public Vector4 UvScalePattern;
            public Color DefMainColor04;
            public float DefGloss04, DefMetallic04;
            /// <summary>The prefab's three colour sets as document values (defMainColor0n, defPtnIndex0n as a pattern reference,
            /// defPatternColor0n, defGloss0n, defMetallic0n, the inverse lerps of defLayout0n and defRotation0n); null without a
            /// CmpClothes.</summary>
            public ClothesColor[] Defaults;
            /// <summary>The colour groups (rendNormal01..03), always three entries.</summary>
            public readonly GroupReport[] Groups = { new GroupReport(), new GroupReport(), new GroupReport() };
            /// <summary>Every slot of every renderer of the colour groups (group order, renderers once).</summary>
            public readonly List<MaterialState> Materials = new List<MaterialState>();
            public ObjectsState Objects = new ObjectsState();

            /// <summary>The colour set n (1..3) is shown by the Maker: useColorN0n or useColorA0n.</summary>
            public bool SetShown(int n) => HasCmpClothes && n >= 1 && n <= 3 && (UseColorN[n - 1] || UseColorA[n - 1]);
            /// <summary>The main colour of set n (1..3) has an alpha: useColorA0n.</summary>
            public bool AlphaShown(int n) => HasCmpClothes && n >= 1 && n <= 3 && UseColorA[n - 1];
            /// <summary>Any option part exists.</summary>
            public bool HasOptions => HasCmpClothes && (Opt01 > 0 || Opt02 > 0);

            public override string ToString() =>
                $"{Name} {Item} {(Skipped ? "skipped" : HasCmpClothes ? Prefab ?? "?" : Realized ? "no CmpClothes" : "none")}" +
                (HasCmpClothes ? $" sets {(SetShown(1) ? 1 : 0)}{(SetShown(2) ? 1 : 0)}{(SetShown(3) ? 1 : 0)} alpha {(AlphaShown(1) ? 1 : 0)}{(AlphaShown(2) ? 1 : 0)}{(AlphaShown(3) ? 1 : 0)} break {(UseBreak ? 1 : 0)} opt {Opt01}/{Opt02}" +
                                 $" groups {string.Join("+", Groups.Where(g => g.HasCreators).Select(g => g.MainWidth + "x" + g.MainHeight))}" : "");
        }

        public sealed class GroupReport
        {
            /// <summary>Live renderers of the group and their material slots in all.</summary>
            public int Renderers, Slots;
            /// <summary>The group's main texture (MainTex / MainTex02 / MainTex03) size as loaded; 0 when the row has none.</summary>
            public int MainWidth, MainHeight;
            /// <summary>The group has compositing passes (a main texture, a colour mask and renderers).</summary>
            public bool HasCreators;
            /// <summary>The colour creator ("create_clothes k.j") and the detail creator ("create_clothes detail k.j").</summary>
            public CreatorState Colour, Detail;
        }

        public sealed class CreatorState
        {
            public string Name;
            /// <summary>The output RT's size (0 when the RT is gone).</summary>
            public int Width, Height;
            /// <summary>Rebuilds since the build (the build's own included).</summary>
            public int Rebuilds;
            /// <summary>The materials the RT is bound to.</summary>
            public int Draw;
        }

        public sealed class MaterialState
        {
            public int Group;
            public string Renderer;
            public int Slot;
            public string Material, Shader;
            /// <summary>_MainTex / _DetailMainTex names (null = none; "(unnamed)" for a texture without a name).</summary>
            public string MainTex, DetailMainTex;
            /// <summary>_MainTex / _DetailMainTex are the group's creator RTs.</summary>
            public bool MainIsCreator, DetailIsCreator;
            /// <summary>_AlphaEx (NaN when the shader lacks it).</summary>
            public float AlphaEx = float.NaN;
        }

        public sealed class ObjectsState
        {
            /// <summary>The part object's own activity.</summary>
            public bool Active;
            /// <summary>objTopDef, objTopHalf, objBotDef, objBotHalf activity (null when the prefab has none).</summary>
            public bool? TopDef, TopHalf, BotDef, BotHalf;
            /// <summary>The option objects' activity (objOpt01 / objOpt02 order).</summary>
            public bool[] Opt01 = new bool[0], Opt02 = new bool[0];
        }

        public sealed class MaskInputs
        {
            /// <summary>Where the lower masks come from: 0 the top, 1 the bottom, -1 none.</summary>
            public int LowerType = -1;
            public bool LowerOk0, LowerOk1, NotBot;
            /// <summary>The reference renderers of mask_braA, mask_innerTB, mask_innerB, mask_panst (null = not found).</summary>
            public string[] Renderers = new string[4];
            /// <summary>"&lt;object&gt;/&lt;renderer&gt;" of the reference renderers the worn parts lack.</summary>
            public string[] Missing = new string[0];
        }

        // ---------------------------------------------------------------- build

        /// <summary>The report of a built character (null for a null context); <paramref name="served"/> also asks the catalog
        /// which copy serves each prefab (the redirect's read model; slower).</summary>
        public static ClothesReport Of(CharaContext ctx, bool served = false)
        {
            if (ctx == null) return null;
            var r = new ClothesReport { Male = ctx.IsMale };
            for (int k = 0; k < 8; k++) r.Kinds[k] = KindOf(ctx, k, served);
            var x = ctx.Edit;
            if (x != null)
            {
                r.Masks.LowerType = x.MaskLowerType; r.Masks.LowerOk0 = x.MaskLowerOk0; r.Masks.LowerOk1 = x.MaskLowerOk1; r.Masks.NotBot = x.MaskNotBot;
                for (int i = 0; i < 4 && i < (x.MaskRenderers?.Length ?? 0); i++) r.Masks.Renderers[i] = x.MaskRenderers[i] ? x.MaskRenderers[i].name : null;
                r.Masks.Missing = x.MaskMissing.ToArray();
            }
            return r;
        }

        /// <summary>One kind's report (null for a null context or a kind out of range); <paramref name="served"/> as in
        /// <see cref="Of"/>; <paramref name="materials"/> false leaves <see cref="KindReport.Materials"/> empty (the facts of the
        /// gates do not read the material slots).</summary>
        public static KindReport KindOf(CharaContext ctx, int k, bool served = false, bool materials = true)
        {
            if (ctx == null || k < 0 || k > 7) return null;
            var p = new KindReport { Kind = k, Name = UnitMap.ClothesKinds[k] };
            var coord = ctx.Edit?.Coord ?? ctx.Coord;
            var doc = coord?.Clothes?.Parts[k];
            p.Item = doc?.Item;
            p.Category = (ctx.IsMale ? ClothesSet.MaleCategories : ClothesSet.FemaleCategories)[k];
            var unit = ctx.Editor?.Unit(UnitMap.ClothesUnits[k]) as ClothesPartUnit;
            var row = unit?.Row;
            p.Skipped = unit != null && unit.Skipped;
            if (row != null)
            {
                p.Row = row.Category + ":" + row.Key;
                foreach (var col in RowColumns)
                {
                    string v = row[col];
                    if (!string.IsNullOrEmpty(v)) p.Columns[col] = v;
                }
                string d2 = row["MainData02"];
                p.Prefab = ctx.IsMale && row.Category >= 100 && row.Category < 200 && !string.IsNullOrEmpty(d2) && d2 != "0" ? d2 : row["MainData"];
                p.OnePiece = row["Coordinate"] == "1";
                p.NotBra = row["NotBra"] == "1";
                p.StateType = row["StateType"];
                if (served && ctx.Catalog != null && !string.IsNullOrEmpty(row["MainAB"]) && !string.IsNullOrEmpty(p.Prefab))
                {
                    try
                    {
                        var ex = ctx.Catalog.ExplainLoad(row, row["MainAB"], p.Prefab);
                        p.ServedKind = ex?.ServedKind; p.ServedGuid = ex?.ServedGuid;
                    }
                    catch (System.Exception e) { p.ServedKind = "error: " + e.GetType().Name; }
                }
            }
            var go = ctx.Clothes != null && k < ctx.Clothes.Length ? ctx.Clothes[k] : null;
            p.Realized = go;
            var cmp = go ? go.GetComponent<CmpClothes>() : null;
            p.HasCmpClothes = cmp != null;
            if (go) p.Objects.Active = go.activeSelf;
            if (cmp == null) return p;

            p.UseColorN[0] = cmp.useColorN01 != 0; p.UseColorN[1] = cmp.useColorN02 != 0; p.UseColorN[2] = cmp.useColorN03 != 0;
            p.UseColorA[0] = cmp.useColorA01 != 0; p.UseColorA[1] = cmp.useColorA02 != 0; p.UseColorA[2] = cmp.useColorA03 != 0;
            p.UseBreak = cmp.useBreak != 0;
            p.Opt01 = cmp.objOpt01?.Count(o => o) ?? 0; p.Opt02 = cmp.objOpt02?.Count(o => o) ?? 0;
            p.HasTopDef = cmp.objTopDef; p.HasTopHalf = cmp.objTopHalf; p.HasBotDef = cmp.objBotDef; p.HasBotHalf = cmp.objBotHalf;
            p.UvScalePattern = cmp.uvScalePattern;
            p.DefMainColor04 = cmp.defMainColor04; p.DefGloss04 = cmp.defGloss04; p.DefMetallic04 = cmp.defMetallic04;
            p.Defaults = DefaultsOf(cmp);
            p.Objects.TopDef = cmp.objTopDef ? cmp.objTopDef.activeSelf : (bool?)null;
            p.Objects.TopHalf = cmp.objTopHalf ? cmp.objTopHalf.activeSelf : (bool?)null;
            p.Objects.BotDef = cmp.objBotDef ? cmp.objBotDef.activeSelf : (bool?)null;
            p.Objects.BotHalf = cmp.objBotHalf ? cmp.objBotHalf.activeSelf : (bool?)null;
            p.Objects.Opt01 = cmp.objOpt01?.Where(o => o).Select(o => o.activeSelf).ToArray() ?? new bool[0];
            p.Objects.Opt02 = cmp.objOpt02?.Where(o => o).Select(o => o.activeSelf).ToArray() ?? new bool[0];

            // colour groups, creators and the materials as written
            var x = ctx.Edit;
            var groups = new[] { cmp.rendNormal01, cmp.rendNormal02, cmp.rendNormal03 };
            string[] mainKeys = { "MainTex", "MainTex02", "MainTex03" };
            var seen = new HashSet<Renderer>();
            for (int j = 0; j < 3; j++)
            {
                var g = p.Groups[j];
                var rends = groups[j]?.Where(r => r).Distinct().ToList() ?? new List<Renderer>();
                g.Renderers = rends.Count;
                var col = x?.ClothesColor[k, j]; var det = x?.ClothesGloss[k, j];
                g.HasCreators = col != null || det != null;
                g.Colour = CreatorOf(col); g.Detail = CreatorOf(det);
                var main = col?.Creator?.Main;
                if (!main && row != null && ctx.Catalog != null && !string.IsNullOrEmpty(row[mainKeys[j]]) && row[mainKeys[j]] != "0")
                {
                    // a group without creators (no colour mask or no renderers): the size of the listed main texture, read under a
                    // short-lived owner (the report runs outside every unit's scope, e.g. for the Maker's clothes facts)
                    string key = mainKeys[j];
                    try { main = ctx.Catalog.LoadTransient("maker:facts", () => ctx.Catalog.LoadAsset<Texture2D>(row, "MainAB", key)); } catch (System.Exception) { main = null; }
                }
                if (main) { g.MainWidth = main.width; g.MainHeight = main.height; }
                foreach (var rend in rends)
                {
                    Material[] mats;
                    try { mats = rend.sharedMaterials; } catch (System.Exception) { mats = new Material[0]; }
                    g.Slots += mats.Length;
                    if (!materials || !seen.Add(rend)) continue;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        var s = new MaterialState { Group = j, Renderer = rend.name, Slot = i, Material = m ? m.name : null, Shader = m && m.shader ? m.shader.name : null };
                        if (m)
                        {
                            s.MainTex = Tex(m, "_MainTex"); s.DetailMainTex = Tex(m, "_DetailMainTex");
                            var mt = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                            var dt = m.HasProperty("_DetailMainTex") ? m.GetTexture("_DetailMainTex") : null;
                            s.MainIsCreator = mt && col?.Creator?.Target && mt == col.Creator.Target;
                            s.DetailIsCreator = dt && det?.Creator?.Target && dt == det.Creator.Target;
                            if (m.HasProperty("_AlphaEx")) s.AlphaEx = m.GetFloat("_AlphaEx");
                        }
                        p.Materials.Add(s);
                    }
                }
            }
            return p;
        }

        static CreatorState CreatorOf(CompositeInputs c)
        {
            if (c?.Creator == null) return null;
            var t = c.Creator.Target;
            return new CreatorState { Name = c.Name, Width = t ? t.width : 0, Height = t ? t.height : 0, Rebuilds = c.Rebuilds, Draw = c.Draw.Count(m => m) };
        }

        /// <summary>The prefab's three colour sets as document values (the Maker's «初期カラー» and item-change defaults):
        /// base = defMainColor0n, pattern = defPtnIndex0n as a vanilla 348 reference (0 = none), patternColor =
        /// defPatternColor0n, gloss / metallic = defGloss0n / defMetallic0n, layout = (InverseLerp(20, 1, x), InverseLerp(20,
        /// 1, y), InverseLerp(−1, 1, z), InverseLerp(−1, 1, w)) of defLayout0n, rotation = InverseLerp(−1, 1, defRotation0n)
        /// (Unity's clamped InverseLerp, as the game).</summary>
        public static ClothesColor[] DefaultsOf(CmpClothes c)
        {
            if (c == null) return null;
            return new[]
            {
                Set(c.defMainColor01, c.defPtnIndex01, c.defPatternColor01, c.defGloss01, c.defMetallic01, c.defLayout01, c.defRotation01),
                Set(c.defMainColor02, c.defPtnIndex02, c.defPatternColor02, c.defGloss02, c.defMetallic02, c.defLayout02, c.defRotation02),
                Set(c.defMainColor03, c.defPtnIndex03, c.defPatternColor03, c.defGloss03, c.defMetallic03, c.defLayout03, c.defRotation03),
            };
        }

        static ClothesColor Set(Color main, int pattern, Color patternColor, float gloss, float metallic, Vector4 layout, float rotation) => new ClothesColor
        {
            Base = C(main), Pattern = pattern == 0 ? DocDefaults.NoneRef : ContentId.Vanilla(pattern).ToString(), PatternColor = C(patternColor),
            Gloss = gloss, Metallic = metallic,
            Layout = new Vec4(Mathf.InverseLerp(20f, 1f, layout.x), Mathf.InverseLerp(20f, 1f, layout.y), Mathf.InverseLerp(-1f, 1f, layout.z), Mathf.InverseLerp(-1f, 1f, layout.w)),
            Rotation = Mathf.InverseLerp(-1f, 1f, rotation),
        };

        static Color4 C(Color c) => new Color4(c.r, c.g, c.b, c.a);

        static string Tex(Material m, string prop)
        {
            if (!m.HasProperty(prop)) return null;
            var t = m.GetTexture(prop);
            return t ? (string.IsNullOrEmpty(t.name) ? "(unnamed)" : t.name) : null;
        }

        public override string ToString() => string.Join("; ", Kinds.Where(p => p != null).Select(p => p.ToString()));
    }
}

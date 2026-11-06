// Clothes units (masks): per kind the part (step 1: prefab on an
// item change, the one-piece skips, dynamic-bone re-pointing, rebind, def / half objects and the option objects), the
// colour pass (step 3: a create_clothes and a create_clothes detail creator per colour group, followed by the kind's
// KCOX re-application in an edit) and the break rate (step 4: _AlphaEx); the masks unit (step 2) writes the hide masks of
// from the items of kinds 0, 1, 2, 3, 5. The writes are the pre-4.0 BuildClothes / ClothesTextures /
// ApplyClothesMasks writes, reading the documents.
// Since Phase 4.4:
//   - a part-unit run without a swap (an option-part toggle) reports the kind in FlushReport.ClothesObjectsReset: it puts
//     the def / half objects back to "dressed", and the scene then re-applies the stored clothes state of the character;
//   - before a top or bottom part is released, the dynamic-bone chains of its components return their bones to the pose
//     they captured (the game's behaviour when such a component is disabled): the part re-points those chains to the
//     body's skirt bones, so without it the new part's chains would capture the old simulated or posed bones, while a
//     fresh build captures the bind pose;
//   - a colour edit rebuilds only the creators the changed members feed (UnitMap.ClothesCreators): base, pattern colour,
//     pattern, layout and rotation the colour creators, gloss and metallic the detail creators. Both creators still get
//     every input written, so the result equals a full pass bit for bit; the KCOX overlays are re-applied after a colour
//     creator rebuild only (a detail rebuild leaves the drawing on the colour creator alone);
//   - the break writes _AlphaEx on the first material slot of each break renderer only, as the game does (other slots
//     keep their prefab value).
using System.Collections.Generic;
using System.Linq;
using AIChara;
using HS2Next.Content;
using HS2Next.Motion.DynamicBones;
using HS2Next.Save;
using UnityEngine;
using EM = HS2Next.Chara.Edit.EditMaterials;
using Object = UnityEngine.Object;

namespace HS2Next.Chara.Edit.Units
{
    public static class ClothesNames
    {
        public static readonly string[] Objects = { "ct_clothesTop", "ct_clothesBot", "ct_inner_t", "ct_inner_b", "ct_gloves", "ct_panst", "ct_socks", "ct_shoes" };
        public static readonly string[] Props =
        {
            "outfit.ChaFileClothes.ClothesTop", "outfit.ChaFileClothes.ClothesBot", "outfit.ChaFileClothes.ClothesInnerT",
            "outfit.ChaFileClothes.ClothesInnerB", "outfit.ChaFileClothes.ClothesGloves", "outfit.ChaFileClothes.ClothesPants",
            "outfit.ChaFileClothes.ClothesSocks", "outfit.ChaFileClothes.ClothesShoes",
        };
        public static string ColorsUnit(int k) => UnitMap.ClothesUnits[k] + "#colors";
        public static string BreakUnit(int k) => UnitMap.ClothesUnits[k] + "#break";
    }

    /// <summary>The part of one clothes kind (step 1).</summary>
    public sealed class ClothesPartUnit : UnitBase
    {
        readonly int k;
        bool loaded;
        ListEntry appliedRow;
        bool appliedSkip;
        string appliedReload;

        public ClothesPartUnit(int kind) : base(UnitMap.ClothesUnits[kind], EditStep.Parts, 20 + kind, "clothes." + UnitMap.ClothesKinds[kind]) { k = kind; }

        /// <summary>Diagnostic (MakerClothesTest WT11): false = the release of a top / bottom leaves the bones of its chains
        /// where the simulation, a clip or FK put them (the behaviour before 4.4). Default true.</summary>
        public static bool RestoreChainsBeforeRelease = true;

        /// <summary>The list row the unit last resolved for its kind (null before its first run; the "none" row when the
        /// stored item did not resolve). Also set when the part was skipped by a one-piece top.</summary>
        public ListEntry Row => appliedRow;
        /// <summary>The last run skipped the part (a bottom under a one-piece top, an inner bottom under a one-piece inner top).</summary>
        public bool Skipped => loaded && appliedSkip;

        public override void Apply(EditContext x)
        {
            var coord = x.CoordDoc;
            var p = coord.Clothes.Parts[k];
            var cats = coord.Sex == 0 ? ClothesSet.MaleCategories : ClothesSet.FemaleCategories;
            string doc = "coord.clothes." + ClothesSet.Names[k];
            for (int j = 0; j < 3; j++) x.Register(PartKeys.ClothesPattern(k, j), p.Colors[j].Pattern, 348, doc + ".colors[" + N(j) + "].pattern");
            x.Register(PartKeys.Clothes(k), p.Item, cats[k], doc + ".item");
            var e = Resolve(x, k);
            // One-piece rules: a top (inner top) whose row has Coordinate = 1 skips the bottom (inner bottom).
            bool skip = k == 1 && Resolve(x, 0)?["Coordinate"] == "1" || k == 3 && Resolve(x, 2)?["Coordinate"] == "1";
            string array = "clothes." + ClothesSet.Names[k] + ".materialOverrides";
            string reload = x.InBuild ? null : Signature.Of(p.Overlays);
            var go = x.Ctx.Clothes[k];
            bool swap = !loaded || e != appliedRow || skip != appliedSkip || (!x.InBuild && (reload != appliedReload || x.MeStructureChanged(array, p.MaterialOverrides)));
            if (!swap && !x.InBuild) x.MeCheckValues(array, EditObject.Clothes(k), p.MaterialOverrides);
            if (swap)
            {
                int sibling = go ? go.transform.GetSiblingIndex() : -1;
                if (go && k <= 1 && !x.InBuild && RestoreChainsBeforeRelease) RestoreChainsOf(x, go);
                if (go) x.ReleasePart(go, Id); else x.ForgetMissing(Id);
                x.ReleaseClothesCreators(k);
                x.Ctx.Clothes[k] = null;
                x.Ctx.ClothesEntries[k] = null;
                loaded = true; appliedRow = e; appliedSkip = skip;
                appliedReload = reload ?? Signature.Of(p.Overlays);
                x.MeLoaded(array, p.MaterialOverrides);
                if (!x.InBuild)
                {
                    x.Swapped(EditObject.Clothes(k));
                    if (x.Report != null) x.Report.ClothesSwapped[k] = true;
                    if (k == 0) x.Editor.MarkUnit(UnitMap.ClothesUnits[1]);
                    if (k == 2) x.Editor.MarkUnit(UnitMap.ClothesUnits[3]);
                    if (k <= 3 || k == 5) x.Editor.MarkUnit(UnitMap.Masks);
                    x.Editor.MarkUnit(ClothesNames.ColorsUnit(k));
                    x.Editor.MarkUnit(ClothesNames.BreakUnit(k));
                    x.MarkDynBone(true);
                    x.Editor.MarkUnit(UnitMap.ExtFinal);
                }
                if (skip) { x.Log.Add(k == 1 ? "bot: skipped (one-piece top)" : "inner_b: skipped (one-piece inner)"); return; }
                x.Ctx.ClothesEntries[k] = e;
                if (k == 7 && !x.InBuild) Heels(x);
                if (e == null || e[x.DataKey(e)] == "p_dummy") return;
                x.BeginPartLoad();
                go = x.LoadPart(e, ClothesNames.Objects[k], x.Ctx.BodyTop.transform);
                x.EndPartLoad(Id);
                if (!go) return;
                if (sibling >= 0) go.transform.SetSiblingIndex(sibling);
                else if (!x.InBuild) EditContext.Place(go, k, Enumerable.Range(0, 8).Select(j => (j, x.Ctx.Clothes[j])), x.Ctx.Body);
                x.Ctx.Clothes[k] = go;
                var dbs = k <= 1 ? x.RepointDynamicBones(go) : null;   // top / bottom: copyDynamicBone
                x.Rebind(go, x.Ctx.BodyBones, "cf_J_Root", x.Ctx.Hips);
                if (dbs != null) x.Ctx.DynamicBonesRepointed += EditContext.DropDestroyedBones(dbs);
                if (!x.InBuild) x.PartSwapped(go, EditObject.Clothes(k));
            }
            if (!go) return;
            var cmp = go.GetComponent<CmpClothes>();
            if (cmp != null)
            {
                // def / half objects back to the build values (the scene's clothes state re-applies on top)
                if (cmp.objTopDef) cmp.objTopDef.SetActive(true);
                if (cmp.objBotDef) cmp.objBotDef.SetActive(true);
                if (cmp.objTopHalf) cmp.objTopHalf.SetActive(false);
                if (cmp.objBotHalf) cmp.objBotHalf.SetActive(false);
                var hide = p.HideOptions ?? new bool[2];
                if (cmp.objOpt01 != null) foreach (var o in cmp.objOpt01) if (o) o.SetActive(!(hide.Length > 0 && hide[0]));
                if (cmp.objOpt02 != null) foreach (var o in cmp.objOpt02) if (o) o.SetActive(!(hide.Length > 1 && hide[1]));
                // without a swap nobody else re-applies the clothes state after these writes: the scene does it for this kind
                if (!swap && !x.InBuild && x.Report != null) x.Report.ClothesObjectsReset[k] = true;
            }
            if (swap) x.Log.Add($"{ClothesNames.Objects[k]}: {x.Name(e)}");
        }

        /// <summary>After 1.0.0: a shoe change lifts or lowers the body at once, before the
        /// motion units of this flush; when the height changed, the bust chains start again from the body as it stands (the
        /// body-shape re-derivation), as in a fresh build, where the body is lifted before the chains are built.</summary>
        static void Heels(EditContext x)
        {
            var lift = x.Ctx.Motion?.Get<HS2Next.Motion.Heels.HeelsLift>();
            if (lift == null || !lift.Apply()) return;
            x.BustRederive = true;
            x.Editor.MarkUnit(UnitMap.Bust);
        }

        /// <summary>The dynamic-bone chains whose component lies under <paramref name="part"/> put their bones back to the
        /// pose they captured (bones still holding it bit for bit are not written). A top or bottom re-points its chains to
        /// the body's skirt bones, which outlive the part.</summary>
        static void RestoreChainsOf(EditContext x, GameObject part)
        {
            var stage = x.Ctx.Motion != null ? x.Ctx.Motion.Get<DynamicBoneStage>() : null;
            if (stage == null) return;
            int n = 0;
            foreach (var c in stage.Chains)
                if (c != null && c.Source && c.Source.transform.IsChildOf(part.transform)) { c.RestorePose(keepHeld: true); n++; }
            if (n > 0) x.Log.Add($"{part.name}: {n} chains restored before the release");
        }

        /// <summary>The row of kind <paramref name="kind"/> as the build resolves it (the "none" row 0 when unresolved).</summary>
        public static ListEntry Resolve(EditContext x, int kind)
        {
            var coord = x.CoordDoc;
            var p = coord.Clothes.Parts[kind];
            var cats = coord.Sex == 0 ? ClothesSet.MaleCategories : ClothesSet.FemaleCategories;
            x.Register(PartKeys.Clothes(kind), p.Item, cats[kind], "coord.clothes." + ClothesSet.Names[kind] + ".item");
            return x.Lookup(x.S.Clothes[kind], EditContext.RawId(p.Item), ClothesNames.Props[kind], PartKeys.Clothes(kind)) ?? x.Lookup(x.S.Clothes[kind], 0, null, null);
        }
    }

    /// <summary>The colour pass of one clothes kind (step 3).</summary>
    public sealed class ClothesColorsUnit : UnitBase
    {
        public override bool LoadsAllEachRun => true;
        readonly int k;
        public ClothesColorsUnit(int kind) : base(ClothesNames.ColorsUnit(kind), EditStep.Composites, 20 + kind, "clothes." + UnitMap.ClothesKinds[kind] + ".colors") { k = kind; }

        public override void Apply(EditContext x)
        {
            var go = x.Ctx.Clothes[k];
            var e = x.Ctx.ClothesEntries[k];
            var p = x.CoordDoc.Clothes.Parts[k];
            string doc = "coord.clothes." + ClothesSet.Names[k];
            for (int j = 0; j < 3; j++) x.Register(PartKeys.ClothesPattern(k, j), p.Colors[j].Pattern, 348, doc + ".colors[" + N(j) + "].pattern");
            var cmp = go ? go.GetComponent<CmpClothes>() : null;
            if (cmp == null || e == null) { if (!x.InBuild) x.ReapplyKcox(k); return; }
            UnitMap.ClothesCreators(k, DirtyPaths, DirtyAll || x.InBuild, out bool colour, out bool detail);
            Pass(x, e, cmp, p, k, colour, detail);
            // the KCOX drawing lives on the colour creators' RTs: it goes on again after they were rebuilt (also when the
            // item has no creator at all, so the kind's KCOX statistics follow the item)
            if (!x.InBuild && colour) x.ReapplyKcox(k);
            x.Touched(EditObject.Clothes(k));
        }

        /// <summary>The colour pass of one kind: per colour group with a main texture and renderers, every input written to
        /// both creators, then the colour creator rebuilt when <paramref name="colour"/>, the detail creator when
        /// <paramref name="detail"/> (both for a group whose creators are new).</summary>
        static void Pass(EditContext x, ListEntry e, CmpClothes cmp, ClothesPart part, int kind, bool colour, bool detail)
        {
            var gd = x.Catalog;
            string[] mainKeys = { "MainTex", "MainTex02", "MainTex03" };
            string[] maskKeys = { "ColorMaskTex", "ColorMask02Tex", "ColorMask03Tex" };
            if (string.IsNullOrEmpty(e["MainTex"]) || e["MainTex"] == "0" || string.IsNullOrEmpty(e["ColorMaskTex"]) || e["ColorMaskTex"] == "0") return;
            var patterns = new Texture2D[3];
            var patternRows = new ListEntry[3];
            string doc = "coord.clothes." + ClothesSet.Names[kind];
            for (int c = 0; c < 3; c++)
            {
                var pe = x.Row(348, part.Colors[c].Pattern, null, PartKeys.ClothesPattern(kind, c), doc + ".colors[" + N(c) + "].pattern");
                patternRows[c] = pe;
                patterns[c] = pe != null ? gd.LoadAsset<Texture2D>(pe, "MainTexAB", "MainTex") : null;
            }
            var rendGroups = new[] { cmp.rendNormal01, cmp.rendNormal02, cmp.rendNormal03 };
            var useA0 = new[] { cmp.useColorA01, cmp.useColorA02, cmp.useColorA03 };
            for (int j = 0; j < 3; j++)
            {
                var main = gd.LoadAsset<Texture2D>(e, "MainAB", mainKeys[j]);
                if (!main || rendGroups[j] == null || rendGroups[j].Count == 0) continue;
                var mask = gd.LoadAsset<Texture2D>(e, "MainAB", maskKeys[j]);
                var coli = x.ClothesColor[kind, j]; var glsi = x.ClothesGloss[kind, j];
                // a creator whose RT is gone (destroyed from outside; the KCOX growth keeps it alive since 4.4) is made anew,
                // with any other RT bound to the group's materials
                if (coli != null && (!coli.Creator.Target || !glsi.Creator.Target))
                {
                    var grown = new HashSet<RenderTexture>();
                    foreach (var r in rendGroups[j].Where(r => r))
                        foreach (var m in r.sharedMaterials)
                            if (m && m.HasProperty("_MainTex") && m.GetTexture("_MainTex") is RenderTexture g && g && g != coli.Creator.Target) grown.Add(g);
                    foreach (var g in grown) { if (RenderTexture.active == g) RenderTexture.active = null; g.Release(); Object.DestroyImmediate(g); }
                    foreach (var c in new[] { coli, glsi })
                    {
                        if (c.Creator.Target) { c.Creator.Target.Release(); Object.DestroyImmediate(c.Creator.Target); }
                        if (c.Creator.Mat) Object.DestroyImmediate(c.Creator.Mat);
                    }
                    coli = glsi = x.ClothesColor[kind, j] = x.ClothesGloss[kind, j] = null;
                }
                bool fresh = coli == null;
                if (fresh)
                {
                    coli = x.ClothesColor[kind, j] = new CompositeInputs("create_clothes " + kind + "." + j, new TexCreator(gd, "create_clothes", main.width, main.height));
                    glsi = x.ClothesGloss[kind, j] = new CompositeInputs("create_clothes detail " + kind + "." + j, new TexCreator(gd, "create_clothes detail", main.width, main.height));
                }
                var col = coli.Creator; var gls = glsi.Creator;
                col.Main = main; coli.Ref("Main", e, "MainAB", mainKeys[j]);
                gls.Main = EditContext.Opaque(main.width, main.height);
                col.Mat.SetTexture("_ColorMask", mask); gls.Mat.SetTexture("_ColorMask", mask);
                coli.Ref("_ColorMask", e, "MainAB", maskKeys[j]); glsi.Ref("_ColorMask", e, "MainAB", maskKeys[j]);
                string[] cprop = { "_Color", "_Color2", "_Color3" }, pprop = { "_Color1_2", "_Color2_2", "_Color3_2" };
                for (int c = 0; c < 3; c++)
                {
                    var cc = part.Colors[c];
                    col.Mat.SetTexture("_PatternMask" + (c + 1), patterns[c]);
                    if (patternRows[c] != null) coli.Ref("_PatternMask" + (c + 1), patternRows[c], "MainTexAB", "MainTex");
                    col.Mat.SetVector("_UVScalePattern", cmp.uvScalePattern == Vector4.zero ? new Vector4(1, 1, 0, 0) : cmp.uvScalePattern);
                    var bc = EditContext.U(cc.Base);
                    if (useA0[c] == 0) bc.a = 1;
                    col.Mat.SetColor(cprop[c], bc);
                    col.Mat.SetColor(pprop[c], EditContext.U(cc.PatternColor));
                    var lay = EditContext.U(cc.Layout);
                    col.Mat.SetVector("_patternuv" + (c + 1), new Vector4(Mathf.Lerp(20, 1, lay.x), Mathf.Lerp(20, 1, lay.y), Mathf.Lerp(-1, 1, lay.z), Mathf.Lerp(-1, 1, lay.w)));
                    col.Mat.SetFloat("_patternuv" + (c + 1) + "Rotator", Mathf.Lerp(-1, 1, cc.Rotation));
                    string sfx = c == 0 ? "" : (c + 1).ToString();
                    gls.Mat.SetFloat("_Glossiness" + sfx, cc.Gloss);
                    gls.Mat.SetFloat("_Metallic" + sfx, cc.Metallic);
                }
                col.Mat.SetColor("_Color4", cmp.defMainColor04);
                gls.Mat.SetFloat("_Glossiness4", cmp.defGloss04); gls.Mat.SetFloat("_Metallic4", cmp.defMetallic04);
                bool all = fresh || x.InBuild, runC = all || colour, runD = all || detail;
                if (runC) x.Rebuild(coli, null, null, false);
                if (runD) x.Rebuild(glsi, null, null, false);
                if ((runC || runD) && x.Report != null) x.Report.ClothesCreatorsRebuilt.Add(new ClothesCreatorRun(kind, j, runC, runD));
                if (all)
                {
                    coli.Draw.Clear(); glsi.Draw.Clear();
                    foreach (var r in rendGroups[j].Where(r => r))
                    {
                        var m = x.OwnMaterial(r);
                        if (!m) continue;
                        EM.SetTexture(m, "_MainTex", col.Target);
                        EM.SetTexture(m, "_DetailMainTex", gls.Target);
                        coli.Draw.Add(m); glsi.Draw.Add(m);
                    }
                }
            }
        }
    }

    /// <summary>The break rate of one clothes kind (step 4): _AlphaEx = 1 - breakRate on the first material slot of every
    /// renderer of the colour groups of an item with useBreak (the game writes the renderer's first material only; the
    /// other slots keep their prefab value).</summary>
    public sealed class ClothesBreakUnit : UnitBase
    {
        readonly int k;
        public ClothesBreakUnit(int kind) : base(ClothesNames.BreakUnit(kind), EditStep.Materials, 30 + kind, "clothes." + UnitMap.ClothesKinds[kind] + ".breakRate") { k = kind; }

        public override void Apply(EditContext x)
        {
            var go = x.Ctx.Clothes[k];
            var cmp = go ? go.GetComponent<CmpClothes>() : null;
            if (cmp == null || cmp.useBreak == 0) return;
            float ex = 1 - x.CoordDoc.Clothes.Parts[k].BreakRate; int n = 0;
            foreach (var r in BreakRenderers(cmp))
            {
                var m = x.OwnMaterial(r);
                if (m && m.HasProperty("_AlphaEx")) { EM.SetFloat(m, "_AlphaEx", ex); n++; }
            }
            x.Log.Add($"{ClothesNames.Objects[k]} _AlphaEx={ex:0.##} ({n} mats)");
            x.Touched(EditObject.Clothes(k));
        }

        /// <summary>The renderers of the three colour groups, each once, in group order.</summary>
        public static List<Renderer> BreakRenderers(CmpClothes cmp)
        {
            var l = new List<Renderer>();
            if (cmp == null) return l;
            foreach (var g in new[] { cmp.rendNormal01, cmp.rendNormal02, cmp.rendNormal03 })
                if (g != null) foreach (var r in g) if (r && !l.Contains(r)) l.Add(r);
            return l;
        }
    }

    // (static, fully dressed: clothesState all 0), applied once after all clothes are loaded,
    // which gives the same end state as the per-loader writes (top, bottom, then the inner parts).
    // Reference renderers: mask_braA = o_bra_a and mask_innerTB = o_shorts_a in the inner top, mask_innerB =
    // o_shorts_a in the inner bottom, mask_panst = o_panst_a (HS2's reference table resolves exactly these object names;
    // a missing object gives null and the part is skipped, confirmed in 4.0).
    public sealed class MasksUnit : UnitBase
    {
        public override bool LoadsAllEachRun => true;
        public MasksUnit() : base(UnitMap.Masks, EditStep.Masks, 0) { }

        public override void Apply(EditContext x)
        {
            var objs = x.Ctx.Clothes;
            var entries = x.Ctx.ClothesEntries;
            var parts = x.CoordDoc.Clothes.Parts;
            var gd = x.Catalog;
            var rep = new List<string>();
            Renderer RefRend(int kind, string name)
            {
                var t = objs[kind] ? EditContext.Find(objs[kind].transform, name) : null;
                if (!t)
                {
                    if (objs[kind]) rep.Add($"{name}: not found in {ClothesNames.Objects[kind]} (renderers: {string.Join(",", objs[kind].GetComponentsInChildren<Renderer>(true).Select(r0 => r0.name))})");
                    return null;
                }
                var r = t.GetComponent<Renderer>();
                if (!r) r = t.GetComponentInChildren<Renderer>(true);
                if (!r) rep.Add($"{name}: no renderer");
                return r;
            }
            var bra = RefRend(2, "o_bra_a");          // mask_braA (rendBra[0])
            var innerTB = RefRend(2, "o_shorts_a");   // mask_innerTB
            var innerB = RefRend(3, "o_shorts_a");    // mask_innerB
            var panst = RefRend(5, "o_panst_a");      // mask_panst
            Texture2D Mask(ListEntry e, string abKey, string key)
            {
                var t = gd.LoadAsset<Texture2D>(e, abKey, key);
                string n = e?[key];
                if (!t && !string.IsNullOrEmpty(n) && n != "0") rep.Add($"{key} {n} ({e[abKey]}): not loaded");
                return t;
            }
            float BreakRate(int k) => parts[k].BreakRate;
            const int topState = 0, botState = 0;   // clothesState (fully dressed)

            var defs = x.MaskTexDefaults;
            var bodyW = new MaskWrites("body", new[] { x.Ctx.BodyDraw }, defs);
            var braW = new MaskWrites("mask_braA", x.Mats(bra), defs);
            var tbW = new MaskWrites("mask_innerTB", x.Mats(innerTB), defs);
            var ibW = new MaskWrites("mask_innerB", x.Mats(innerB), defs);
            var pnW = new MaskWrites("mask_panst", x.Mats(panst), defs);

            // Top loaded (the "none" row counts: its texture columns are "0" -> null).
            var top = entries[0];
            if (top != null)
            {
                bodyW.Tex("_AlphaMask", Mask(top, "OverBodyMaskAB", "OverBodyMask"));
                braW.Tex("_AlphaMask", Mask(top, "OverBraMaskAB", "OverBraMask"));
            }
            else if (!x.InBuild) { bodyW.Restore("_AlphaMask"); braW.Restore("_AlphaMask"); }
            // Lower masks: from the top (type 0) or, loaded later, the bottom (type 1) when it has a valid OverBodyBMask.
            int type = -1; ListEntry lower = null; Texture2D bodyB = null;
            for (int k = 0; k <= 1; k++)
            {
                var bm = Mask(entries[k], "OverBodyBMaskAB", "OverBodyBMask");
                if (bm) { type = k; lower = entries[k]; bodyB = bm; }
            }
            if (lower != null)
            {
                bodyW.Tex("_AlphaMask2", bodyB);
                tbW.Tex("_AlphaMask2", Mask(lower, "OverInnerTBMaskAB", "OverInnerTBMask"));
                ibW.Tex("_AlphaMask2", Mask(lower, "OverInnerBMaskAB", "OverInnerBMask"));
                pnW.Tex("_AlphaMask2", Mask(lower, "OverPanstMaskAB", "OverPanstMask"));
            }
            else if (!x.InBuild)
            {
                // the build writes nothing then: the values the materials had before the first mask write
                bodyW.Restore("_AlphaMask2");
                tbW.Restore("_AlphaMask2"); ibW.Restore("_AlphaMask2"); pnW.Restore("_AlphaMask2");
            }
            // _alpha_a/_alpha_b from the top state: 0 -> (1,1), 1 -> (0,1), 2 -> (0,0); _alpha_c from the top break.
            float a = topState == 0 ? 1 : 0, b = topState == 2 ? 0 : 1, c = BreakRate(0) == 0 ? 0 : 1;
            foreach (var w in new[] { bodyW, braW }) { w.F("_alpha_a", a); w.F("_alpha_b", b); w.F("_alpha_c", c); }
            bool notBot = entries[0]?["Coordinate"] == "1";
            bool BreakDisable(ListEntry e) => e?["BreakDisableMask"] == "1";
            bool d = type == 0 && (!BreakDisable(entries[0]) || BreakRate(0) == 0) && botState == 0
                  || type == 1 && (!BreakDisable(entries[1]) || BreakRate(1) == 0) && botState == 0 && !notBot;
            foreach (var w in new[] { bodyW, tbW, ibW, pnW }) w.F("_alpha_d", d ? 1 : 0);
            foreach (var w in new[] { bodyW, braW, tbW, ibW, pnW }) if (w.Count > 0) rep.Add(w.ToString());
            rep.Add($"lower masks from {(type == 0 ? "top" : type == 1 ? "bot" : "none")}");
            // what ApplyClothesMasks(top, bottom) needs to re-evaluate the state-dependent values later (Phase 3.5)
            x.MaskRenderers = new[] { bra, innerTB, innerB, panst };
            x.MaskLowerType = type;
            x.MaskLowerOk0 = !BreakDisable(entries[0]) || BreakRate(0) == 0;
            x.MaskLowerOk1 = !BreakDisable(entries[1]) || BreakRate(1) == 0;
            x.MaskNotBot = notBot;
            x.MaskMissing.Clear();
            foreach (var (k, name, r) in new[] { (2, "o_bra_a", bra), (2, "o_shorts_a", innerTB), (3, "o_shorts_a", innerB), (5, "o_panst_a", panst) })
                if (objs[k] && !r) x.MaskMissing.Add(ClothesNames.Objects[k] + "/" + name);
            x.Log.Add("masks: " + string.Join("; ", rep));
            if (x.Report != null) x.Report.MasksChanged = true;
        }

        /// <summary>Writes mask properties to a set of materials (only where the property exists) and records them.</summary>
        sealed class MaskWrites
        {
            readonly string target; readonly Material[] mats;
            readonly Dictionary<(Material, string), Texture> defaults;
            readonly List<string> done = new List<string>();
            public int Count => done.Count;
            public MaskWrites(string target, Material[] mats, Dictionary<(Material, string), Texture> defaults)
            {
                this.target = target; this.mats = mats.Where(m => m).ToArray(); this.defaults = defaults;
            }
            public void Tex(string p, Texture t)
            {
                int n = 0;
                foreach (var m in mats)
                    if (m.HasProperty(p))
                    {
                        if (!defaults.ContainsKey((m, p))) defaults[(m, p)] = m.GetTexture(p);
                        m.SetTexture(p, t); n++;
                    }
                if (n > 0) done.Add($"{p}={(t ? t.name : "null")}");
            }
            /// <summary>Writes back the value a material had before its first mask write (edits: the build would not write it).</summary>
            public void Restore(string p)
            {
                foreach (var m in mats)
                    if (defaults.TryGetValue((m, p), out var t)) m.SetTexture(p, t);
            }
            public void F(string p, float v)
            {
                int n = 0;
                foreach (var m in mats) if (m.HasProperty(p)) { m.SetFloat(p, v); n++; }
                if (n > 0) done.Add($"{p}={v:0.##}");
            }
            public override string ToString() => $"{target}[{mats.Length}]: {string.Join(" ", done)}";
        }
    }
}

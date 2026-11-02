// Realization of a built character's `chara.state` (clean room):
//   1 the clothes state types of the worn parts with the pair reconciliation (ClothesStateSet), computed after the build
//   2 the stored clothes states realized as HS2 would show them (an unsupported value as the state the cycling reaches,
//     finding chara.clothesStateAdjusted; the stored value is never written back)
//   3 object activity of the clothes parts and their def / half objects table (on change, not per frame)
//   4 the state-dependent mask values (CharaAssembler.ApplyClothesMasks(top, bottom): _alpha_a / _alpha_b / _alpha_d)
//   5 wet (_ExGloss on hair, face, body and clothes materials that declare it), skin gloss (body _Gloss = the build's
//     value + 0.2 x gloss), liquid (_WeatheringRange1..4 on the body's liquid slot and the clothes of kinds 0..4,
//     _WeatheringRange6 on the face; levels 0 / 1 / 2 -> 0 / 0.5 / 1; females only, as HS2), nipple (body shape key 32 =
//     lerp(0, 0.8, card value) + 0.2 x rate), son (the son object's activity and the son bone's uniform scale; names from
//     the data: the body's son object of the assembler and the Studio Bone row 65)
//   6 silhouette as HS2 draws it (v0.3 trial 2): HS2's simple body (`p_cf_body_silhouette` / `p_cm_body_silhouette` of
//     oo_base, skinned to the body bones; built when the silhouette is first switched on) drawn with `silhouette.color`
//     (`_Color`, alpha honoured; HS2Next/Custom/Silhouette: a depth pass, then the colour blended once); the body mesh, the
//     body tongue, the head, hair, clothes and accessories hidden; the body's mosaic and the son stay as without it (HS2:
//     the son object follows the son toggle only, and the simple body shows its own son and tongue with them); items attached
//     to the character and animation option items are not touched. Everything returns when it is switched off.
// Materials are written in place: after the build every renderer slot holds a material of this character.
// Data names only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using AIChara;
using HS2Next.Chara;
using HS2Next.IO;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class CharaStateRealizer
    {
        readonly CharaRealization real;
        CharaContext Ctx => real.Context;
        /// <summary>The body / face draw materials the renderers hold now (the URP copies; Context.BodyDraw / FaceDraw are the
        /// pre-replacement objects after the scene builder's ShaderMap.ApplyUrp).</summary>
        Material Body => real.Assembler != null ? real.Assembler.LiveBodyDraw() : Ctx.BodyDraw;
        Material Face => real.Assembler != null ? real.Assembler.LiveFaceDraw() : Ctx.FaceDraw;

        /// <summary>The state types of the worn outfit (Unknown until the character is built).</summary>
        public ClothesStateSet States { get; private set; } = ClothesStateSet.Unknown;
        /// <summary>The clothes states as realized (after <see cref="ClothesStateSet.Effective"/>).</summary>
        public int[] Realized { get; private set; } = new int[ClothesStateSet.KindCount];
        /// <summary>Milliseconds of the last clothes apply (visibility + masks), for U-M6.</summary>
        public double LastClothesMs { get; private set; }
        /// <summary>Property writes of the last mask apply.</summary>
        public int LastMaskWrites { get; private set; }
        public bool SilhouetteOn => silOn;
        /// <summary>The simple body of the silhouette (null until the silhouette was first switched on; tests).</summary>
        public GameObject SilhouetteBody => simpleRoot;

        float baseGloss = float.NaN, baseFaceGloss = float.NaN;
        Material baseFaceMat;
        float lastNipple = 0f, lastSonLength = float.NaN;
        bool silOn;
        GameObject simpleRoot;
        CmpBody simpleCmp;
        readonly List<Material> simpleColored = new List<Material>();
        readonly Dictionary<GameObject, bool> silhouetteHidden = new Dictionary<GameObject, bool>();
        readonly Dictionary<Renderer, bool> silhouetteRenderersOff = new Dictionary<Renderer, bool>();

        public CharaStateRealizer(CharaRealization real) { this.real = real; }

        /// <summary>Diagnostic (MakerClothesTest WT3): false = a clothes part run without a swap (an option-part toggle) does
        /// not re-apply the stored clothes state (the behaviour before 4.4). Default true.</summary>
        public static bool ReapplyAfterObjectsReset = true;

        // ------------------------------------------------------------ state types

        /// <summary>The state types of a built character's worn parts.</summary>
        public static ClothesStateSet Compute(CharaContext ctx)
        {
            if (ctx == null) return ClothesStateSet.Unknown;
            var rows = new int[ClothesStateSet.KindCount];
            for (int k = 0; k < rows.Length; k++)
            {
                var e = ctx.ClothesEntries != null && k < ctx.ClothesEntries.Length ? ctx.ClothesEntries[k] : null;
                rows[k] = e != null ? ClothesStateSet.ParseStateType(e["StateType"]) : int.MinValue;
            }
            var top = Entry(ctx, 0);
            var bra = Entry(ctx, 2);
            CmpClothes Cmp(int k) => ctx.Clothes != null && k < ctx.Clothes.Length && ctx.Clothes[k] ? ctx.Clothes[k].GetComponent<CmpClothes>() : null;
            return ClothesStateSet.FromRows(rows,
                notBot: top?["Coordinate"] == "1", notInnerT: top?["NotBra"] == "1", notInnerB: bra?["Coordinate"] == "1",
                botDefOfTop: Cmp(0) && Cmp(0).objBotDef, topDefOfBot: Cmp(1) && Cmp(1).objTopDef,
                botDefOfBra: Cmp(2) && Cmp(2).objBotDef, botDefOfShorts: Cmp(3) && Cmp(3).objBotDef);
        }

        static HS2Next.Content.ListEntry Entry(CharaContext ctx, int k) =>
            ctx.ClothesEntries != null && k < ctx.ClothesEntries.Length ? ctx.ClothesEntries[k] : null;

        // ------------------------------------------------------------ apply

        /// <summary>After a build (or rebuild): types, then every member.</summary>
        public void OnBuilt(CharaState s)
        {
            States = Compute(Ctx);
            baseGloss = float.NaN;
            baseFaceGloss = float.NaN;
            baseFaceMat = null;
            lastNipple = 0f;
            lastSonLength = float.NaN;
            // the old root (and a simple body under it) went with the rebuild
            silOn = false;
            simpleRoot = null;
            simpleCmp = null;
            simpleColored.Clear();
            silhouetteHidden.Clear();
            silhouetteRenderersOff.Clear();
            ApplyAll(s, true);
        }

        /// <summary>Every member (build, load, undo of a whole state object).</summary>
        public void ApplyAll(CharaState s, bool build = false)
        {
            if (s == null || Ctx == null) return;
            if (silOn && !s.Silhouette.Enabled) SilhouetteOff();
            ApplyClothes(s);
            ApplyWet(s.Wet);
            ApplyGloss(s.Gloss);
            ApplyLiquid(s.Liquid);
            if (!build || s.Nipple != 0f) ApplyNipple(s.Nipple);
            ApplySon(s.Son);
            ApplySilhouette(s.Silhouette);
        }

        /// <summary>The members under the changed paths (chara.state.*).</summary>
        public void ApplyChanged(CharaState s, Func<string, bool> touches)
        {
            if (s == null || Ctx == null) return;
            bool sil = touches("chara.state.silhouette");
            if (sil && silOn && !s.Silhouette.Enabled) SilhouetteOff();
            if (touches("chara.state.clothes") || sil) ApplyClothes(s);
            if (touches("chara.state.wet")) ApplyWet(s.Wet);
            if (touches("chara.state.gloss")) ApplyGloss(s.Gloss);
            if (touches("chara.state.liquid")) ApplyLiquid(s.Liquid);
            if (touches("chara.state.nipple")) ApplyNipple(s.Nipple);
            if (touches("chara.state.son")) ApplySon(s.Son);
            if (sil) ApplySilhouette(s.Silhouette);
        }

        /// <summary>
        /// Clothes visibility and the masks. A stored value the kind does not support is realized as the state the
        /// cycling reaches (finding chara.clothesStateAdjusted, info), the model is not written.
        /// </summary>
        public void ApplyClothes(CharaState s)
        {
            var sw = Stopwatch.StartNew();
            var st = States;
            var stored = s.Clothes ?? new int[ClothesStateSet.KindCount];
            var e = st.EffectiveAll(stored);
            for (int k = 0; k < ClothesStateSet.KindCount; k++)
                if (k < stored.Length && e[k] != stored[k])
                {
                    real.Runtime.ReportOnce(FindingSeverity.Info, "chara.clothesStateAdjusted", real.NodeId,
                        "clothes state " + stored[k] + " of " + ClothesStateSet.KindNames[k] + " is not offered by the worn part; realized as " + e[k] + " (the stored value is kept)");
                    break;
                }
            Realized = e;
            bool sil = s.Silhouette != null && s.Silhouette.Enabled;
            const int On = ClothesStateSet.On, Half = ClothesStateSet.Half, Off = ClothesStateSet.Off;
            var objs = Ctx.Clothes ?? new GameObject[0];
            GameObject Obj(int k) => k < objs.Length ? objs[k] : null;
            CmpClothes Cmp(int k) => Obj(k) ? Obj(k).GetComponent<CmpClothes>() : null;
            void Set(GameObject o, bool on) { if (o && o.activeSelf != on) o.SetActive(on); }
            // top
            Set(Obj(0), !sil && e[0] != Off);
            var c0 = Cmp(0);
            if (c0 != null)
            {
                Set(c0.objTopDef, e[0] == On);
                Set(c0.objTopHalf, e[0] == Half);
                Set(c0.objBotDef, e[1] == On && e[0] != Off);
                Set(c0.objBotHalf, e[1] != On && e[0] != Off);
            }
            // bottom
            Set(Obj(1), !sil && !st.NotBot && e[1] != Off);
            var c1 = Cmp(1);
            if (c1 != null)
            {
                Set(c1.objBotDef, e[1] == On);
                Set(c1.objBotHalf, e[1] == Half);
                Set(c1.objTopDef, e[0] == On && e[1] != Off);
                Set(c1.objTopHalf, e[0] != On && e[1] != Off);
            }
            // bra
            Set(Obj(2), !sil && !st.NotInnerT && e[2] != Off);
            var c2 = Cmp(2);
            if (c2 != null)
            {
                Set(c2.objTopDef, e[2] == On);
                Set(c2.objTopHalf, e[2] == Half);
                Set(c2.objBotDef, e[3] == On && e[2] != Off);
                Set(c2.objBotHalf, e[3] != On && e[2] != Off);
            }
            // shorts
            Set(Obj(3), !sil && !st.NotInnerB && e[3] != Off);
            var c3 = Cmp(3);
            if (c3 != null)
            {
                Set(c3.objBotDef, e[3] == On);
                Set(c3.objBotHalf, e[3] == Half);
            }
            // gloves, pantyhose, socks, shoes
            Set(Obj(4), !sil && e[4] == On);
            Set(Obj(5), !sil && e[5] != Off);
            var c5 = Cmp(5);
            if (c5 != null)
            {
                Set(c5.objBotDef, e[5] == On);
                Set(c5.objBotHalf, e[5] == Half);
            }
            Set(Obj(6), !sil && e[6] == On);
            Set(Obj(7), !sil && e[7] == On);
            LastMaskWrites = real.Assembler != null ? real.Assembler.ApplyClothesMasks(e[0], e[1]) : 0;
            // info, not a warning: HS2 looks up the same object names (o_bra_a, o_shorts_a, o_panst_a) and skips a part that
            // lacks one, so a bra without o_shorts_a is normal (v0.2: the status line warned about it for BR-Chan all the time)
            if (real.Assembler != null && real.Assembler.MaskRenderersMissing.Count > 0)
                real.Runtime.ReportOnce(FindingSeverity.Info, "chara.maskRendererMissing", real.NodeId,
                    "no mask reference renderer in the worn part: " + string.Join(", ", real.Assembler.MaskRenderersMissing) + " (as in HS2, those parts get no clothes mask)");
            // the body cuts follow the switch objects this state set (after 1.0.0)
            Ctx.Edit?.ApplyBodyCuts();
            // the heels apply while the shoes are on (after 1.0.0; the stored state, not the silhouette's hiding)
            var heels = Ctx.Motion?.Get<HS2Next.Motion.Heels.HeelsLift>();
            if (heels != null) heels.State.ShoesOn = e[7] == On;
            LastClothesMs = sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// Step 10 of a Maker flush: the state members whose objects or
        /// base values the units replaced are realized again — clothes state types, def / half objects and masks after a
        /// clothes item or mask change or a part-unit run that reset the def / half objects (an option-part toggle); the
        /// gloss over the rewritten body draw (its base is read again); wet and liquid on the
        /// materials of new parts; the silhouette over new renderers. The body shape is not re-applied here: the shape.body
        /// unit writes index 32 with EditContext.NipStandRate itself, and a second BodyShape.Apply would drop the bone
        /// modifiers in edit mode. The stored state is never written.
        /// </summary>
        public void AfterEdit(CharaState s, HS2Next.Chara.Edit.FlushReport r)
        {
            if (s == null || r == null || Ctx == null) return;
            bool clothes = false;
            for (int k = 0; k < r.ClothesSwapped.Length; k++) clothes |= r.ClothesSwapped[k];
            bool hair = false;
            for (int k = 0; k < r.HairSwapped.Length; k++) hair |= r.HairSwapped[k];
            // Phase 4.4: a clothes part unit that ran without a swap (an option-part toggle) put its def / half objects back
            // to "dressed": the stored state goes on again as after a swap (object activity and, for top / bottom, the masks)
            bool objectsReset = false;
            if (ReapplyAfterObjectsReset) for (int k = 0; k < r.ClothesObjectsReset.Length; k++) objectsReset |= r.ClothesObjectsReset[k];
            bool sil = silOn && (r.AnySwap || r.NewObjects.Count > 0);
            if (sil) SilhouetteOff();
            if (clothes) States = Compute(Ctx);
            if (clothes || r.MasksChanged || objectsReset) ApplyClothes(s);
            if (r.BodyDrawChanged) baseGloss = float.NaN;
            if (r.FaceDrawChanged || r.HeadSwapped) baseFaceGloss = float.NaN;
            if (r.BodyDrawChanged || r.FaceDrawChanged || r.HeadSwapped) ApplyGloss(s.Gloss);
            if (clothes || hair || r.HeadSwapped || r.BodyDrawChanged) ApplyWet(s.Wet);
            if (clothes || r.HeadSwapped || r.BodyDrawChanged) ApplyLiquid(s.Liquid);
            if (sil) ApplySilhouette(s.Silhouette);
        }

        // ------------------------------------------------------------ material members

        IEnumerable<Renderer> RenderersOf(GameObject o)
        {
            if (!o) yield break;
            foreach (var r in o.GetComponentsInChildren<Renderer>(true)) if (r) yield return r;
        }

        IEnumerable<Material> Owned(Renderer r)
        {
            // every slot holds a material of this character's build, whether or not the extension stage
            // registered it in OwnedMaterials; a renderer that draws the body's materials has none of its own (after 1.0.0,
            // 8)
            if (Ctx != null && Ctx.DrawsBodyMaterials(r)) yield break;
            foreach (var m in r.sharedMaterials) if (m) yield return m;
        }

        static void SetIf(Material m, string p, float v) { if (m && m.HasProperty(p)) m.SetFloat(p, v); }

        /// <summary>_ExGloss = wet on hair, face, body and clothes materials that declare it (HS2 UpdateWet).</summary>
        public void ApplyWet(float wet)
        {
            foreach (var h in Ctx.Hair ?? new GameObject[0]) foreach (var r in RenderersOf(h)) foreach (var m in Owned(r)) SetIf(m, "_ExGloss", wet);
            SetIf(Face, "_ExGloss", wet);
            SetIf(Body, "_ExGloss", wet);
            foreach (var c in Ctx.Clothes ?? new GameObject[0])
            {
                var cmp = c ? c.GetComponent<CmpClothes>() : null;
                if (cmp == null) continue;
                foreach (var g in new[] { cmp.rendNormal01, cmp.rendNormal02, cmp.rendNormal03 })
                    if (g != null) foreach (var r in g) if (r) foreach (var m in Owned(r)) SetIf(m, "_ExGloss", wet);
            }
        }

        /// <summary>Body and face _Gloss = lerp(0, 0.8, skin gloss power) (the value draw.body / draw.face wrote, captured
        /// once per rewrite) + 0.2 x gloss (HS2 skinGlossRate sets both draws).</summary>
        public void ApplyGloss(float gloss)
        {
            var b = Body;
            if (b && b.HasProperty("_Gloss"))
            {
                if (float.IsNaN(baseGloss)) baseGloss = b.GetFloat("_Gloss");
                b.SetFloat("_Gloss", baseGloss + 0.2f * gloss);
            }
            var f = Face;
            if (f && f.HasProperty("_Gloss"))
            {
                if (float.IsNaN(baseFaceGloss) || f != baseFaceMat) { baseFaceGloss = f.GetFloat("_Gloss"); baseFaceMat = f; }
                f.SetFloat("_Gloss", baseFaceGloss + 0.2f * gloss);
            }
        }

        /// <summary>The face draw's _Gloss as draw.face wrote it (NaN until captured; tests).</summary>
        public float BaseFaceGloss => baseFaceGloss;
        /// <summary>The body draw's _Gloss as draw.body wrote it (NaN until captured; tests).</summary>
        public float BaseBodyGloss => baseGloss;

        static readonly float[] LiquidValues = { 0f, 0.5f, 1f };

        static float Level(int[] l, int i) => l != null && i < l.Length ? LiquidValues[Mathf.Clamp(l[i], 0, 2)] : 0f;

        /// <summary>
        /// Liquid levels (SiruParts: 0 face, 1 front top, 2 front bottom, 3 back top, 4 back bottom): _WeatheringRange6 on the face,
        /// _WeatheringRange1..4 on the body's liquid slot (renderer slot 1) and on the clothes of kinds 0..4 (HS2 iterates five
        /// kinds from 0). Females only (HS2).
        /// </summary>
        public void ApplyLiquid(int[] liquid)
        {
            if (Ctx.IsMale) return;
            SetIf(Face, "_WeatheringRange6", Level(liquid, 0));
            void Four(Material m)
            {
                SetIf(m, "_WeatheringRange1", Level(liquid, 1));
                SetIf(m, "_WeatheringRange2", Level(liquid, 2));
                SetIf(m, "_WeatheringRange3", Level(liquid, 3));
                SetIf(m, "_WeatheringRange4", Level(liquid, 4));
            }
            var br = Ctx.BodyRenderer;
            if (br)
            {
                var mats = br.sharedMaterials;
                if (mats.Length > 1) Four(mats[1]);
            }
            for (int k = 0; k < 5; k++)
            {
                var c = Ctx.Clothes != null && k < Ctx.Clothes.Length ? Ctx.Clothes[k] : null;
                var cmp = c ? c.GetComponent<CmpClothes>() : null;
                if (cmp == null) continue;
                foreach (var g in new[] { cmp.rendNormal01, cmp.rendNormal02, cmp.rendNormal03 })
                    if (g != null) foreach (var r in g) if (r) foreach (var m in Owned(r)) Four(m);
            }
        }

        /// <summary>Body shape key 32 = lerp(0, 0.8, the card's value) + 0.2 x rate, then the body shape is re-applied.</summary>
        public void ApplyNipple(float rate)
        {
            var shape = Ctx.BodyShape;
            if (shape == null) return;
            float v = 0f;
            // the document is the truth (Maker edits change it after the build); card builds without documents read the card
            var values = Ctx.Chara?.Body?.Shape ?? Ctx.Card?.Body.Get("shapeValueBody").Floats();
            if (values != null && values.Length > 32) v = values[32];
            shape.SetValue(32, Mathf.Lerp(0f, 0.8f, v) + 0.2f * rate);
            shape.Apply();
            lastNipple = rate;
            // the body shape unit adds the same 0.2 x rate when an edit re-applies the shapes
            if (Ctx.Edit != null) Ctx.Edit.NipStandRate = rate;
        }

        /// <summary>The son object (the body's N_dan of the assembler) active with son.visible, with or without the silhouette
        /// (HS2: its rule in Studio reduces to the son toggle); the simple body's son object active with son.visible while the
        /// silhouette is on; the son bone (Studio Bone row 65) scaled uniformly by son.length while visible.</summary>
        public void ApplySon(CharaSonState son)
        {
            if (son == null) return;
            var body = Ctx.Body;
            var obj = body ? FindDeep(body.transform, "N_dan") : null;
            if (obj && obj.gameObject.activeSelf != son.Visible) obj.gameObject.SetActive(son.Visible);
            var simpleDan = simpleCmp != null && simpleCmp.targetEtc != null ? simpleCmp.targetEtc.objDanTop : null;
            if (simpleDan && simpleDan.activeSelf != (son.Visible && silOn)) simpleDan.SetActive(son.Visible && silOn);
            var bone = SonBone();
            if (bone && son.Visible && son.Length != lastSonLength)
            {
                bone.localScale = new Vector3(son.Length, son.Length, son.Length);
                lastSonLength = son.Length;
            }
        }

        Transform SonBone()
        {
            var db = real.Runtime.Services?.Db;
            if (db == null || Ctx.BodyBones == null) return null;
            foreach (var row in FkRows.From(db))
                if (row.No == 65 && !string.IsNullOrEmpty(row.Name) && Ctx.BodyBones.TryGetValue(row.Name, out var t)) return t;
            return null;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (!t) return null;
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r) return r;
            }
            return null;
        }

        // ------------------------------------------------------------ silhouette

        /// <summary>True while the silhouette hides the accessories.</summary>
        public bool HidesAccessories => silOn;

        public void ApplySilhouette(CharaSilhouette s)
        {
            if (s == null) return;
            if (!s.Enabled)
            {
                if (silOn) SilhouetteOff();
                return;
            }
            if (!EnsureSimpleBody()) return;
            SetSimpleColor(UnityObjects.C(s.Color));
            if (silOn) return;
            silOn = true;
            simpleRoot.SetActive(true);
            var cmp = Ctx.Body ? Ctx.Body.GetComponent<CmpBody>() : null;
            var etc = cmp != null ? cmp.targetEtc : null;
            Hide(etc?.objBody);
            // the body tongue's activity belongs to the tongue state (the expression switches it): its renderers go off instead
            if (etc != null && etc.objTongue)
                foreach (var r in etc.objTongue.GetComponentsInChildren<Renderer>(true))
                    if (r && !silhouetteRenderersOff.ContainsKey(r)) { silhouetteRenderersOff[r] = r.enabled; r.enabled = false; }
            Hide(Ctx.Head);
            foreach (var h in Ctx.Hair ?? new GameObject[0]) Hide(h);
            foreach (var a in Ctx.Accessories ?? new List<AccessorySlot>()) Hide(a?.Object);
            var se = simpleCmp != null ? simpleCmp.targetEtc : null;
            if (se != null && se.objBody && !se.objBody.activeSelf) se.objBody.SetActive(true);
            // clothes are hidden by ApplyClothes (the silhouette flag), the son objects by ApplySon
            var st = real.Node?.State as CharaNodeState;
            if (st != null) { ApplyClothes(st.State); ApplySon(st.State.Son); }
        }

        /// <summary>
        /// Builds the simple body once: the sex's silhouette prefab of oo_base under BodyTop, skinned to the body bones as the
        /// body is, its materials this character's own copies mapped to their URP stand-ins, its tongue following the tongue
        /// state. Hidden until the silhouette is on. False (finding chara.silhouetteMissing, warning) when the prefab is absent.
        /// </summary>
        bool EnsureSimpleBody()
        {
            if (simpleRoot) return true;
            var x = Ctx.Edit;
            string asset = Ctx.IsMale ? "p_cm_body_silhouette" : "p_cf_body_silhouette";
            GameObject prefab = null;
            try { prefab = Ctx.Catalog?.LoadAsset<GameObject>("chara/oo_base.unity3d", asset); }
            catch (Exception e) { real.Runtime.ReportOnce(FindingSeverity.Warning, "chara.silhouetteMissing", real.NodeId, asset + ": " + e.Message); return false; }
            if (!prefab || x == null || !Ctx.BodyTop || Ctx.BodyBones == null)
            {
                real.Runtime.ReportOnce(FindingSeverity.Warning, "chara.silhouetteMissing", real.NodeId, "the silhouette body " + asset + " is not available; the silhouette is not drawn");
                return false;
            }
            var go = HS2Next.Chara.Edit.EditContext.Spawn(prefab, asset, Ctx.BodyTop.transform);
            go.SetActive(false);
            x.Rebind(go, Ctx.BodyBones, "cf_J_Root", Ctx.Hips);
            if (CharaAssembler.OwnAllMaterials) x.OwnAll(go);
            ShaderMap.ApplyUrp(go, copy: !CharaAssembler.OwnAllMaterials);
            if (!CharaAssembler.OwnAllMaterials)
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) if (m) Ctx.OwnedMaterials.Add(m);
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (x.ForceMatrixRecalculation) smr.forceMatrixRecalculationPerRender = true;
            simpleRoot = go;
            simpleCmp = go.GetComponent<CmpBody>();
            simpleColored.Clear();
            var tc = simpleCmp != null ? simpleCmp.targetCustom : null;
            var te = simpleCmp != null ? simpleCmp.targetEtc : null;
            foreach (var r in new[] { tc?.rendBody, te?.rendTongue })
                if (r) foreach (var m in r.sharedMaterials) if (m && !simpleColored.Contains(m)) simpleColored.Add(m);
            if (te != null && te.objTongue) real.Expression?.AddBodyTongue(te.objTongue);
            if (te != null && te.objDanTop) te.objDanTop.SetActive(false);
            VisibilityApplier.SetOwn(go.transform, real.Node == null || real.Node.EffectiveVisible);
            Ctx.Log?.Add("silhouette: " + asset + " built (" + go.GetComponentsInChildren<Renderer>(true).Length + " renderers, " + simpleColored.Count + " coloured materials)");
            return true;
        }

        void SetSimpleColor(Color c)
        {
            foreach (var m in simpleColored) if (m && m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        void Hide(GameObject o)
        {
            if (!o) return;
            if (!silhouetteHidden.ContainsKey(o)) silhouetteHidden[o] = o.activeSelf;
            if (o.activeSelf) o.SetActive(false);
        }

        void SilhouetteOff()
        {
            if (!silOn) return;
            silOn = false;
            if (simpleRoot) simpleRoot.SetActive(false);
            foreach (var kv in silhouetteHidden) if (kv.Key && kv.Key.activeSelf != kv.Value) kv.Key.SetActive(kv.Value);
            silhouetteHidden.Clear();
            foreach (var kv in silhouetteRenderersOff) if (kv.Key) kv.Key.enabled = kv.Value;
            silhouetteRenderersOff.Clear();
            var st = real.Node?.State as CharaNodeState;
            if (st != null)
            {
                real.ReapplyAccessories();
                ApplyClothes(st.State);
                ApplySon(st.State.Son);
            }
        }

        /// <summary>Numbers only (tests): the silhouette's state, the simple body's renderers and activity, its colour, the
        /// counts of objects hidden and renderers switched off.</summary>
        public Dictionary<string, object> SilhouetteDump()
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal) { ["on"] = silOn, ["built"] = (bool)simpleRoot };
            if (simpleRoot)
            {
                d["active"] = simpleRoot.activeInHierarchy;
                var rs = new List<object>();
                foreach (var r in simpleRoot.GetComponentsInChildren<Renderer>(true))
                    rs.Add(new Dictionary<string, object> { ["name"] = r.name, ["active"] = r.gameObject.activeInHierarchy, ["shader"] = r.sharedMaterial ? r.sharedMaterial.shader.name : null });
                d["renderers"] = rs;
                var m = simpleColored.Count > 0 ? simpleColored[0] : null;
                if (m && m.HasProperty("_Color")) { var c = m.GetColor("_Color"); d["color"] = new double[] { c.r, c.g, c.b, c.a }; }
            }
            d["hidden"] = silhouetteHidden.Count;
            d["renderersOff"] = silhouetteRenderersOff.Count;
            return d;
        }

        /// <summary>The realization is released (the simple body and its materials go with the character's root and owned
        /// materials).</summary>
        public void Release()
        {
            SilhouetteOff();
            simpleRoot = null;
            simpleCmp = null;
            simpleColored.Clear();
        }

        // ------------------------------------------------------------ number-only dump (K12, CharaStateTest)

        /// <summary>
        /// Numbers only: per kind the stored / realized state, the supported states, the part root's and its def / half objects'
        /// activity (null = absent), and the mask values on each target's materials (_alpha_a/_b/_c/_d; the first material that
        /// declares the property).
        /// </summary>
        public Dictionary<string, object> Dump(CharaState s)
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            var kinds = new List<object>();
            var stored = s?.Clothes ?? new int[ClothesStateSet.KindCount];
            for (int k = 0; k < ClothesStateSet.KindCount; k++)
            {
                var o = Ctx?.Clothes != null && k < Ctx.Clothes.Length ? Ctx.Clothes[k] : null;
                var cmp = o ? o.GetComponent<CmpClothes>() : null;
                object Act(GameObject g) => g ? (object)g.activeSelf : null;
                kinds.Add(new Dictionary<string, object>
                {
                    ["kind"] = ClothesStateSet.KindNames[k], ["stored"] = k < stored.Length ? stored[k] : 0, ["realized"] = Realized[k],
                    ["supported"] = States.Supported(k), ["object"] = Act(o),
                    ["topDef"] = cmp != null ? Act(cmp.objTopDef) : null, ["topHalf"] = cmp != null ? Act(cmp.objTopHalf) : null,
                    ["botDef"] = cmp != null ? Act(cmp.objBotDef) : null, ["botHalf"] = cmp != null ? Act(cmp.objBotHalf) : null,
                });
            }
            d["kinds"] = kinds;
            d["types"] = States.ToString();
            d["notBot"] = States.NotBot;
            d["notInnerT"] = States.NotInnerT;
            d["notInnerB"] = States.NotInnerB;
            var masks = new Dictionary<string, object>(StringComparer.Ordinal);
            if (real.Assembler != null)
                foreach (var (target, mats) in real.Assembler.ClothesMaskTargets())
                {
                    var t = new Dictionary<string, object>(StringComparer.Ordinal) { ["materials"] = mats.Length };
                    foreach (var p in new[] { "_alpha_a", "_alpha_b", "_alpha_c", "_alpha_d" })
                    {
                        object v = null;
                        foreach (var m in mats) if (m && m.HasProperty(p)) { v = (double)m.GetFloat(p); break; }
                        t[p.Substring(1)] = v;
                    }
                    masks[target] = t;
                }
            d["masks"] = masks;
            d["maskRenderersMissing"] = real.Assembler != null ? new List<string>(real.Assembler.MaskRenderersMissing) : new List<string>();
            d["lastMs"] = Math.Round(LastClothesMs, 4);
            d["maskWrites"] = LastMaskWrites;
            d["silhouette"] = silOn;
            return d;
        }
    }
}

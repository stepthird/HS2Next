// Plugin sections after vanilla edits: applied, not edited, in 4.0.
//   KSOX     skin layers live on the creators, so every skin rebuild re-applies them; a change of body.skinOverlays
//            re-registers them (the texture-stage statistics of KSOX are reset to the values before the build's texture
//            stage first, so they describe the current layers).
//   KCOX     after every colour pass of kind k, the overlays of kind k only.
//   ME       the items of every object a step 1-4 unit touched: value items (shader, render queue, properties) on an
//            object that stayed, every item on an object that was replaced (part swap) or removed.
//   final    AAC, ABMX, DBE, InvisibleBody again after part, clothes-item or accessory changes and after changes of
//            their own sections, from the state before the build's final stage (statistics, accessory / hair object
//            activity, renderers InvisibleBody disabled, bone modifiers removed first).
// Statistics: the build records each object's ME contribution and each kind's KCOX contribution to ExtStats; a
// re-application subtracts the old contribution and adds the new one, so ctx.Stats always equals a fresh build's.
// Plugin data comes from the documents (ExtSections); a change of a plugin section or of the accessory list
// regenerates it before the flush (document builds only).
using System.Collections.Generic;
using System.Linq;
using HS2Next.Chara.Documents;
using HS2Next.Chara.Ext;
using HS2Next.Data.Ext;
using UnityEngine;
using OT = HS2Next.Data.Ext.MaterialEditorData.ObjectType;
using Object = UnityEngine.Object;

namespace HS2Next.Chara.Edit
{
    public sealed partial class EditContext
    {
        static readonly string[] MeFields = { "Me" }, KcoxFields = { "Kcox" }, SkinFields = { "Skin" };
        static readonly string[] FinalFields = { "Abmx", "Aac", "Dbe", "Invisible", "PushUp" };

        /// <summary>Set SkinnedMeshRenderer.forceMatrixRecalculationPerRender on new parts (test views).</summary>
        public bool ForceMatrixRecalculation;
        /// <summary>A plugin section or the accessory list changed: the plugin data is regenerated before the next flush.</summary>
        public bool ExtDirty;
        /// <summary>body.skinOverlays changed: KSOX re-registers its layers in the next texture step.</summary>
        public bool KsoxDirty;
        /// <summary>Objects replaced or removed in this flush (all their ME items re-run).</summary>
        public readonly HashSet<EditObject> SwappedObjects = new HashSet<EditObject>();

        readonly Dictionary<(OT, int, bool), ExtStats> meContrib = new Dictionary<(OT, int, bool), ExtStats>();
        readonly Dictionary<int, ExtStats> kcoxContrib = new Dictionary<int, ExtStats>();
        ExtStats beforeTextures, beforeFinal;
        readonly Dictionary<GameObject, bool> finalActivity = new Dictionary<GameObject, bool>();
        readonly List<Renderer> invisibleDisabled = new List<Renderer>();

        bool ExtOn(System.Func<CharaExtOptions, bool> module) => Ctx.Ext != null && Ctx.Options != null && Ctx.Options.Enabled && module(Ctx.Options);

        // ---------------------------------------------------------------- the build's texture stage (step 5)

        /// <summary>The texture stage of the build, module by module as CharaExtApplier's Textures stage, recording each
        /// object's (kind's) contribution to the statistics.</summary>
        public void BuildTextures()
        {
            var ext = Ctx.Ext; var o = Ctx.Options;
            if (Ctx == null || ext == null || o == null || !o.Enabled) return;
            beforeTextures = Ctx.Stats.Copy();
            if (o.SkinOverlays) CharaExtApplier.RunModule(Ctx, "ksox", () => SkinOverlayApplier.Apply(Ctx, ext.SkinOverlays, o));
            if (o.ClothesOverlays && ext.ClothesOverlays != null)
                for (int k = 0; k < 8; k++) ApplyKcox(k);
            if (o.MaterialEditor && ext.MaterialEditor != null && ext.MaterialEditor.HasEdits)
            {
                var objects = new List<(OT, int)>();
                foreach (var item in ext.MaterialEditor.AllItems())
                {
                    var key = (item.ObjectType, item.ObjectType == OT.Character ? 0 : item.Slot);
                    if (!objects.Contains(key)) objects.Add(key);
                }
                foreach (var key in objects) ApplyMe(key, structure: true, values: true);
            }
        }

        void ApplyKcox(int kind)
        {
            var before = Ctx.Stats.Copy();
            CharaExtApplier.RunModule(Ctx, "kcox", () => ClothesOverlayApplier.ApplyKind(Ctx, Ctx.Ext.ClothesOverlays, Ctx.Options, kind));
            kcoxContrib[kind] = Delta(before, KcoxFields);
        }

        void ApplyMe((OT, int) key, bool structure, bool values)
        {
            var me = Ctx.Ext?.MaterialEditor;
            if (me == null)
            {
                MaterialEditorApplier.ForgetObject(Ctx, key);
                MaterialEditorApplier.UpdateTextureStats(Ctx);
                return;
            }
            foreach (var phase in new[] { true, false })
            {
                if (phase ? !structure : !values) continue;
                var before = Ctx.Stats.Copy();
                var objects = new HashSet<(OT, int)> { key };
                CharaExtApplier.RunModule(Ctx, "materialEditor", () => MaterialEditorApplier.ApplyObjects(Ctx, me, Ctx.Options, objects, structural: phase, values: !phase));
                meContrib[(key.Item1, key.Item2, phase)] = Delta(before, MeFields);
            }
            MaterialEditorApplier.UpdateTextureStats(Ctx);
        }

        ExtStats Delta(ExtStats before, string[] fields)
        {
            var d = Ctx.Stats.Copy();
            d.Add(before, -1, fields);
            return d;
        }

        // ---------------------------------------------------------------- edits

        /// <summary>KCOX of one clothes kind after its colour pass (edits).</summary>
        public void ReapplyKcox(int kind)
        {
            // the old contribution goes first (new plugin data may have no overlays at all)
            if (kcoxContrib.TryGetValue(kind, out var old)) { Ctx.Stats.Add(old, -1, KcoxFields); kcoxContrib.Remove(kind); }
            if (!ExtOn(o => o.ClothesOverlays) || Ctx.Ext.ClothesOverlays == null) return;
            ApplyKcox(kind);
        }

        /// <summary>Step 5 of an edit: KSOX after a change of its section, the MaterialEditor items of the objects this
        /// flush touched (value items) or replaced (every item).</summary>
        public void ReapplyTextures()
        {
            if (KsoxDirty && ExtOn(o => o.SkinOverlays))
            {
                KsoxDirty = false;
                if (beforeTextures != null) Ctx.Stats.Set(beforeTextures, SkinFields);
                // the skin passes back to their build size without layers (KSOX re-registers and grows them again)
                foreach (var (c, draw, prop, size) in new[] { (BodySkin, Ctx.BodyDraw, "_MainTex", 4096), (BodyDetail, Ctx.BodyDraw, "_DetailMainTex", 4096),
                                                            (FaceSkin, Ctx.FaceDraw, "_MainTex", 2048), (FaceDetail, Ctx.FaceDraw, "_DetailMainTex", 2048) })
                {
                    if (c == null) continue;
                    c.Creator.Under.Clear(); c.Creator.Over.Clear();
                    var t = c.Creator.Target;
                    if (t && (t.width != size || t.height != size))
                    {
                        var old = c.Creator.Resize(size, size);
                        if (draw && draw.GetTexture(prop) == old) draw.SetTexture(prop, c.Creator.Target);
                        old.Release();
                        Object.DestroyImmediate(old);
                    }
                    c.Creator.Rebuild();
                }
                CharaExtApplier.RunModule(Ctx, "ksox", () => SkinOverlayApplier.Apply(Ctx, Ctx.Ext.SkinOverlays, Ctx.Options));
                // the passes rebound their RTs to the draw materials: Character ME texture items go on top again
                TouchedObjects.Add(EditObject.Character);
            }
            var me = Ctx.Ext?.MaterialEditor;
            if (!ExtOn(o => o.MaterialEditor)) return;
            // objects whose items are gone (new plugin data without them) lose their contributions too
            var keys = new HashSet<(OT, int)>(me != null ? me.AllItems().Select(i => (i.ObjectType, i.ObjectType == OT.Character ? 0 : i.Slot)) : Enumerable.Empty<(OT, int)>());
            foreach (var k in meContrib.Keys) keys.Add((k.Item1, k.Item2));
            foreach (var key in keys.OrderBy(k => (int)k.Item1).ThenBy(k => k.Item2))
            {
                var obj = new EditObject(key.Item1, key.Item2);
                bool swapped = SwappedObjects.Contains(obj), touched = swapped || TouchedObjects.Contains(obj);
                if (!touched) continue;
                foreach (var phase in new[] { true, false })
                {
                    if (phase && !swapped) continue;
                    if (meContrib.TryGetValue((key.Item1, key.Item2, phase), out var old)) { Ctx.Stats.Add(old, -1, MeFields); meContrib.Remove((key.Item1, key.Item2, phase)); }
                }
                ApplyMe(key, structure: swapped, values: true);
                // Phase 4.2: Character items may recalculate the body normals
                // (RecalculateNormals replaces the drawn mesh): the breast normals go on top again in step 6
                if (key.Item1 == OT.Character) Editor?.MarkUnit(UnitMap.BustNormals);
            }
        }

        /// <summary>Called by the build before its final stage (step 7): what a later re-run starts from.</summary>
        public void BeforeBuildFinal()
        {
            beforeFinal = Ctx.Stats.Copy();
            finalActivity.Clear();
            foreach (var a in Ctx.Accessories) if (a?.Object) finalActivity[a.Object] = a.Object.activeSelf;
            foreach (var h in Ctx.Hair)
                if (h) foreach (Transform c in h.transform) finalActivity[c.gameObject] = c.gameObject.activeSelf;
            invisibleDisabled.Clear();
            if (Ctx.BodyTop)
                foreach (var r in Ctx.BodyTop.GetComponentsInChildren<Renderer>(true)) if (r.enabled) invisibleDisabled.Add(r);
        }

        /// <summary>Called by the build after its final stage: the renderers InvisibleBody disabled.</summary>
        public void AfterBuildFinal()
        {
            invisibleDisabled.RemoveAll(r => !r || r.enabled);
        }

        /// <summary>Step 7 of an edit: the final stage again from the state before the build's final stage.</summary>
        public void ReapplyFinal()
        {
            if (Ctx.Ext == null || Ctx.Options == null || !Ctx.Options.Enabled) return;
            if (beforeFinal != null) Ctx.Stats.Set(beforeFinal, FinalFields);
            foreach (var kv in finalActivity) if (kv.Key) kv.Key.SetActive(kv.Value);
            foreach (var r in invisibleDisabled) if (r) r.enabled = true;
            var bones = Ctx.Bones ? Ctx.Bones : Ctx.Root ? Ctx.Root.GetComponent<BoneModifierApplier>() : null;
            if (bones)
            {
                // bones back to their bases once, then the component goes: the final stage adds a new one only when the
                // current documents have bone modifiers or rule effects (a fresh build without them has none)
                bones.RestoreBases();
                Object.DestroyImmediate(bones);
                Ctx.Bones = null;
                bones = null;
                // the shape bones freshly evaluated (a base read back from a written bone may differ by an ulp), so the
                // modifiers apply on the same values as in a fresh build
                Ctx.BodyShape?.Apply();
                Ctx.FaceShape?.Apply();
            }
            // the activity a later re-run starts from: objects that are new since the build start as loaded
            foreach (var a in Ctx.Accessories) if (a?.Object && !finalActivity.ContainsKey(a.Object)) finalActivity[a.Object] = a.Object.activeSelf;
            foreach (var h in Ctx.Hair)
                if (h) foreach (Transform c in h.transform) if (!finalActivity.ContainsKey(c.gameObject)) finalActivity[c.gameObject] = c.gameObject.activeSelf;
            var enabledBefore = Ctx.BodyTop ? Ctx.BodyTop.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToList() : new List<Renderer>();
            Hook(ExtStage.Final);
            foreach (var r in enabledBefore) if (r && !r.enabled && !invisibleDisabled.Contains(r)) invisibleDisabled.Add(r);
            bones = Ctx.Bones ? Ctx.Bones : Ctx.Root ? Ctx.Root.GetComponent<BoneModifierApplier>() : null;
            if (bones) bones.Apply();
            Editor?.MarkUnit(UnitMap.AnimRefresh);
            // bone modifiers scale bones the chains are derived from
            MarkBust();
            MarkDynBone(false);
        }

        /// <summary>Plugin data from the documents again (a plugin section or the accessory list changed; document builds).</summary>
        public void RegenerateExt()
        {
            ExtDirty = false;
            if (Ctx.Options == null || !Ctx.Options.Enabled) return;
            var slots = AccessorySlotTable.Assign(CoordDoc.Accessories, null);
            var ext = ExtSections.Build(Doc, CoordDoc, slots, Input.Res, null);
            MaterialEditorApplier.KeepTextures(Ctx, ext.MaterialEditor);
            Ctx.Ext = Input.Ext = ext;
        }

        /// <summary>MoreAccessories counters from the slot table (edits of slots >= 20).</summary>
        public void UpdateExtraSlotStats()
        {
            if (!ExtOn(o => o.MoreAccessories)) return;
            var st = Ctx.Stats;
            int used = 0, loaded = 0, hidden = 0;
            foreach (var a in Ctx.Accessories)
            {
                if (a == null || !a.Extra || a.IsEmpty) continue;
                used++;
                if (a.Object) { loaded++; if (!a.Show) hidden++; }
            }
            st.ExtraSlotsUsed = used; st.ExtraSlotsLoaded = loaded; st.ExtraSlotsHidden = hidden;
        }
    }
}

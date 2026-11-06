// Plugin sections after vanilla edits: applied, not edited, in 4.0.
//   KSOX     skin layers live on the creators, so every skin rebuild re-applies them; a change of body.skinOverlays
//            re-registers them (the texture-stage statistics of KSOX are reset to the values before the build's texture
//            stage first, so they describe the current layers). The layers plugins set (at BodyMesh, or outside builds:
//            KsoxDirty with the unit ext.textures) are composited the same way, also on a character without decoded plugin
//            data; without plugin layers nothing here differs from the card's KSOX alone.
//   KCOX     after every colour pass of kind k, the overlays of kind k only.
//   ME       the items of every object a step 1-4 unit touched: value items (shader, render queue, properties) on an
//            object that stayed, every item on an object that was replaced (part swap) or removed. A change of an array that
//            leaves its structure alone joins the same step as a value pass: the array's items again, the renderers' Enabled items
//            when those changed, then the baseline's sweep puts back what left the array. Nothing reloads and only the material
//            items are regenerated.
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
        /// <summary>Objects whose material array changed in its values only: the next flush re-applies them in place.</summary>
        public readonly HashSet<EditObject> MePending = new HashSet<EditObject>();
        /// <summary>A value change of a material array waits: only the material items are regenerated before the next flush.</summary>
        public bool MeDirty;

        readonly Dictionary<(OT, int, bool), ExtStats> meContrib = new Dictionary<(OT, int, bool), ExtStats>();
        // per array (member path): the structure and the values the object was last loaded or applied with; per object: its Enabled items
        readonly Dictionary<string, string> meStructure = new Dictionary<string, string>(System.StringComparer.Ordinal);
        readonly Dictionary<string, string> meValues = new Dictionary<string, string>(System.StringComparer.Ordinal);
        readonly Dictionary<(OT, int), string> meEnabled = new Dictionary<(OT, int), string>();
        readonly Dictionary<int, ExtStats> kcoxContrib = new Dictionary<int, ExtStats>();
        ExtStats beforeTextures, beforeFinal;
        readonly Dictionary<GameObject, bool> finalActivity = new Dictionary<GameObject, bool>();
        readonly List<Renderer> invisibleDisabled = new List<Renderer>();

        bool ExtOn(System.Func<CharaExtOptions, bool> module) => Ctx.Ext != null && Ctx.Options != null && Ctx.Options.Enabled && module(Ctx.Options);

        static readonly CharaExtOptions NoOptions = new CharaExtOptions();

        /// <summary>A plugin's skin layer is registered on the character.</summary>
        bool HasPluginLayers => Ctx.SkinLayers.Exists(l => l.Owner != null);

        /// <summary>A skin pass holds a plugin's layer from its last composite.</summary>
        bool PassesHoldPluginLayers()
        {
            foreach (var c in new[] { BodySkin, BodyDetail, FaceSkin, FaceDetail })
                if (c?.Creator != null && (c.Creator.Under.Exists(l => l.Owner != null) || c.Creator.Over.Exists(l => l.Owner != null))) return true;
            return false;
        }

        /// <summary>Plugin layers are composited without card data unless the options turn skin layers off.</summary>
        bool PluginLayersOn => Ctx.Options == null || Ctx.Options.SkinOverlays;

        // ---------------------------------------------------------------- the build's texture stage (step 5)

        /// <summary>The texture stage of the build, module by module as CharaExtApplier's Textures stage, recording each
        /// object's (kind's) contribution to the statistics.</summary>
        public void BuildTextures()
        {
            if (Ctx == null) return;
            MeLoaded("materialOverrides", Doc.MaterialOverrides);
            foreach (var a in CoordDoc.Accessories)
                if (a?.Id != null) MeLoaded(UnitMap.AccessoryArray(a.Id), a.MaterialOverrides);
            var ext = Ctx.Ext; var o = Ctx.Options;
            if (ext == null || o == null || !o.Enabled)
            {
                // no decoded plugin data: only the layers plugins set (none in the official build)
                if (!HasPluginLayers || !PluginLayersOn) return;
                beforeTextures = Ctx.Stats.Copy();
                CharaExtApplier.RunModule(Ctx, "ksox", () => SkinOverlayApplier.Apply(Ctx, null, o ?? NoOptions));
                return;
            }
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

        /// <summary>One object's items. <paramref name="enabled"/>: the renderers' Enabled items of an object that stayed, which the
        /// structure phase holds otherwise. An edit's pass sweeps what it did not write; the images go after that, so a value going
        /// back never finds its texture released.</summary>
        void ApplyMe((OT, int) key, bool structure, bool values, bool enabled = false)
        {
            var me = Ctx.Ext?.MaterialEditor;
            var baseline = MaterialEditorApplier.BaselineOf(Ctx, key);
            baseline.Begin();
            baseline.Building = InBuild;
            MaterialEditorApplier.BeginNames(Ctx, key);
            bool log = InBuild || structure;
            bool valueEdit = !structure && !InBuild && !TouchedObjects.Contains(new EditObject(key.Item1, key.Item2));
            var objects = new HashSet<(OT, int)> { key };
            if (enabled && !structure && MaterialEditorApplier.EnabledShare(Ctx, key) is ExtStats old)
            {
                Ctx.Stats.Add(old, -1, MeFields);
                if (meContrib.TryGetValue((key.Item1, key.Item2, true), out var c)) c.Add(old, -1, MeFields);
                MaterialEditorApplier.ForgetEnabledShare(Ctx, key);
            }
            if (me != null)
            {
                foreach (var phase in new[] { true, false })
                {
                    if (phase ? !structure : !values) continue;
                    var before = Ctx.Stats.Copy();
                    CharaExtApplier.RunModule(Ctx, "materialEditor", () => MaterialEditorApplier.ApplyObjects(Ctx, me, Ctx.Options, objects, structural: phase, values: !phase, log: log, valueEdit: valueEdit));
                    meContrib[(key.Item1, key.Item2, phase)] = Delta(before, MeFields);
                }
                if (enabled && !structure)
                {
                    CharaExtApplier.RunModule(Ctx, "materialEditor", () => MaterialEditorApplier.ApplyObjects(Ctx, me, Ctx.Options, objects, structural: false, values: false, enabled: true, log: false, valueEdit: valueEdit));
                    if (MaterialEditorApplier.EnabledShare(Ctx, key) is ExtStats now)
                    {
                        if (!meContrib.TryGetValue((key.Item1, key.Item2, true), out var c)) meContrib[(key.Item1, key.Item2, true)] = c = new ExtStats();
                        c.Add(now, 1, MeFields);
                    }
                }
            }
            if (structure || enabled) meEnabled[key] = MaterialEditorApplier.EnabledSignature(me, key);
            if (!InBuild) baseline.Sweep(structure || enabled ? MaterialMember.All : MaterialMember.Values);
            if (!InBuild) MaterialEditorApplier.SettleNames(Ctx, key, me);
            if (me == null) MaterialEditorApplier.ForgetObject(Ctx, key);
            MaterialEditorApplier.UpdateTextureStats(Ctx);
        }

        // ---------------------------------------------------------------- what the material arrays were last applied with

        /// <summary>The object was built or loaded with this array.</summary>
        public void MeLoaded(string arrayPath, List<HS2Next.Save.MaterialOverride> list)
        {
            meStructure[arrayPath] = HS2Next.Scenes.MeStructure.OfMapped(list);
            meValues[arrayPath] = Units.Signature.Of(list);
        }

        /// <summary>True when the array's structure is not the one the object was loaded with (a reload follows). An array not seen
        /// before (an accessory added after the build) is recorded and counts as equal.</summary>
        public bool MeStructureChanged(string arrayPath, List<HS2Next.Save.MaterialOverride> list)
        {
            string now = HS2Next.Scenes.MeStructure.OfMapped(list);
            if (meStructure.TryGetValue(arrayPath, out var old)) return old != now;
            meStructure[arrayPath] = now;
            return false;
        }

        /// <summary>A part unit ran without a reload: a wider path may have changed the array's values (a morph, a whole part set);
        /// the object then joins the value pass.</summary>
        public void MeCheckValues(string arrayPath, EditObject o, List<HS2Next.Save.MaterialOverride> list)
        {
            string now = Units.Signature.Of(list);
            if (meValues.TryGetValue(arrayPath, out var old) && old == now) return;
            meValues[arrayPath] = now;
            MePending.Add(o);
            Editor?.MarkUnit(UnitMap.ExtTextures);
        }

        /// <summary>
        /// A document path changed. True when it is at or under a material array whose structure stayed: the object goes to the value
        /// pass of the next flush and nothing else is marked. False: the usual route (a wider path, a structure change, an array of
        /// an object this editor has no slot for).
        /// </summary>
        public bool MarkMaterialValues(string documentPath)
        {
            if (!UnitMap.MaterialObject(documentPath, out var kind, out int index, out string id, out string arrayPath)) return false;
            if (Doc == null || CoordDoc == null) return false;
            List<HS2Next.Save.MaterialOverride> list;
            EditObject o;
            switch (kind)
            {
                case UnitMap.MaterialArray.Character: list = Doc.MaterialOverrides; o = EditObject.Character; break;
                case UnitMap.MaterialArray.Hair: list = Doc.Hair.Parts[index].MaterialOverrides; o = EditObject.Hair(index); break;
                case UnitMap.MaterialArray.Clothes: list = CoordDoc.Clothes.Parts[index].MaterialOverrides; o = EditObject.Clothes(index); break;
                default:
                {
                    var e = CoordDoc.FindAccessory(id);
                    if (e == null || Input.Slots == null || !Input.Slots.SlotOf.TryGetValue(e, out int slot)) return false;
                    list = e.MaterialOverrides;
                    o = EditObject.Accessory(slot);
                    break;
                }
            }
            if (!meStructure.TryGetValue(arrayPath, out var old) || old != HS2Next.Scenes.MeStructure.OfMapped(list)) return false;
            meValues.Remove(arrayPath);
            MePending.Add(o);
            MeDirty = true;
            return true;
        }

        /// <summary>The URP mapping ran on the built character: keyword items act on what it left (nothing without such items).</summary>
        public void AfterUrpMapping()
        {
            if (ExtOn(o => o.MaterialEditor)) MaterialEditorApplier.ApplyKeywords(Ctx, Ctx.Ext.MaterialEditor, Ctx.Options);
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
            bool card = ExtOn(o => o.SkinOverlays);
            // without card KSOX: the plugins' layers alone (set, changed or all removed since the last composite)
            bool plugins = !card && KsoxDirty && PluginLayersOn && (HasPluginLayers || PassesHoldPluginLayers());
            if (KsoxDirty && (card || plugins))
            {
                KsoxDirty = false;
                // a character whose first plugin layer came after the build: its statistics before any skin layer are the current ones
                if (beforeTextures == null && plugins) beforeTextures = Ctx.Stats.Copy();
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
                        MaterialEditorApplier.BaselineOf(Ctx, (OT.Character, 0)).ReplaceTexture(old, c.Creator.Target);
                        old.Release();
                        Object.DestroyImmediate(old);
                    }
                    c.Creator.Rebuild();
                }
                CharaExtApplier.RunModule(Ctx, "ksox", () => SkinOverlayApplier.Apply(Ctx, card ? Ctx.Ext.SkinOverlays : null, Ctx.Options ?? NoOptions));
                // the passes rebound their RTs to the draw materials: Character ME texture items go on top again
                TouchedObjects.Add(EditObject.Character);
            }
            var me = Ctx.Ext?.MaterialEditor;
            if (!ExtOn(o => o.MaterialEditor)) return;
            // objects whose items are gone (new plugin data without them) lose their contributions too
            var keys = new HashSet<(OT, int)>(me != null ? me.AllItems().Select(i => (i.ObjectType, i.ObjectType == OT.Character ? 0 : i.Slot)) : Enumerable.Empty<(OT, int)>());
            foreach (var k in meContrib.Keys) keys.Add((k.Item1, k.Item2));
            foreach (var o in MePending) keys.Add((o.Type, o.Type == OT.Character ? 0 : o.Slot));
            foreach (var key in keys.OrderBy(k => (int)k.Item1).ThenBy(k => k.Item2))
            {
                var obj = new EditObject(key.Item1, key.Item2);
                bool swapped = SwappedObjects.Contains(obj), unit = swapped || TouchedObjects.Contains(obj), pending = MePending.Contains(obj);
                if (!unit && !pending) continue;
                foreach (var phase in new[] { true, false })
                {
                    if (phase && !swapped) continue;
                    if (meContrib.TryGetValue((key.Item1, key.Item2, phase), out var old)) { Ctx.Stats.Add(old, -1, MeFields); meContrib.Remove((key.Item1, key.Item2, phase)); }
                }
                bool enabled = !swapped && pending && EnabledChanged(key, me);
                if (enabled) ReleaseInvisible();
                var baseline = MaterialEditorApplier.BaselineOf(Ctx, key);
                bool body = baseline.Holds(Ctx.BodyDraw, "_Gloss"), face = baseline.Holds(Ctx.FaceDraw, "_Gloss");
                ApplyMe(key, structure: swapped, values: true, enabled: enabled);
                if (Report != null)
                {
                    Report.MaterialItemsRan = true;
                    // the face's tears, flush and highlight are the expression's: they go on top again, as after a build
                    if (key.Item1 == OT.Character) Report.EyesChanged = true;
                    // the state's skin gloss adds to the draw's _Gloss: an item that wrote it, or gave it back, is the value it adds to
                    Report.BodyDrawChanged |= body || baseline.Holds(Ctx.BodyDraw, "_Gloss");
                    Report.FaceDrawChanged |= face || baseline.Holds(Ctx.FaceDraw, "_Gloss");
                }
                // Phase 4.2: Character items may recalculate the body normals
                // (RecalculateNormals replaces the drawn mesh): the breast normals go on top again in step 6
                if (key.Item1 == OT.Character && unit) Editor?.MarkUnit(UnitMap.BustNormals);
            }
            MaterialEditorApplier.ForgetMissingShaders(Ctx, me);
        }

        bool EnabledChanged((OT, int) key, MaterialEditorData me) =>
            (meEnabled.TryGetValue(key, out var was) ? was : "") != MaterialEditorApplier.EnabledSignature(me, key);

        // An Enabled item changes under the final stage: the renderers it switched off come back first, so an item and a removal work
        // on what a fresh build has before that stage, and the stage runs again after them.
        void ReleaseInvisible()
        {
            foreach (var r in invisibleDisabled) if (r) r.enabled = true;
            invisibleDisabled.Clear();
            Editor?.MarkUnit(UnitMap.ExtFinal);
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
            HS2Next.Motion.Modifiers.BoneModifierSetup.Sync(Ctx);   // the modifiers' stages exactly as a fresh build has them
            foreach (var r in enabledBefore) if (r && !r.enabled && !invisibleDisabled.Contains(r)) invisibleDisabled.Add(r);
            bones = Ctx.Bones ? Ctx.Bones : Ctx.Root ? Ctx.Root.GetComponent<BoneModifierApplier>() : null;
            if (bones)
            {
                // a foot joint the heels hold: the new modifiers' base is what the heels started from, as in a fresh build
                var foot = Ctx.Motion != null ? Ctx.Motion.Get<HS2Next.Motion.Heels.HeelsFoot>() : null;
                if (foot != null && foot.Written) { bones.RestoreBases(); foot.TakeOff(); }
                bones.Apply();
            }
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
            MeDirty = false;
        }

        /// <summary>Only the material items from the documents again (a value change of an array): the rest of the plugin data stays,
        /// and images whose arrays did not change are not compared. An emptied list is null, so the sweep still runs.</summary>
        public void RegenerateMaterialEdits()
        {
            MeDirty = false;
            if (Ctx.Ext == null || Ctx.Options == null || !Ctx.Options.Enabled) return;
            var slots = Input.Slots ?? AccessorySlotTable.Assign(CoordDoc.Accessories, null);
            var old = Ctx.Ext.MaterialEditor;
            var me = ExtSections.MaterialEditor(Doc, CoordDoc, slots, Input.Res, null);
            MaterialEditorApplier.KeepTextures(Ctx, me, old ?? new MaterialEditorData());
            Ctx.Ext.MaterialEditor = me;
            if (Input.Ext != null) Input.Ext.MaterialEditor = me;
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

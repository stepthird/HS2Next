// What the units share beyond the build helpers: the compositing rebuild, the
// part-swap protocol (release, load bracket, place, record), the accessory loader, the objects a flush touched (for the
// plugin re-application), the new subtrees (ownership and URP mapping in place) and the missing content
// a part brought.
using System.Collections.Generic;
using System.Linq;
using AIChara;
using HS2Next.Chara.Documents;
using HS2Next.Content;
using HS2Next.Data.Ext;
using UnityEngine;
using EM = HS2Next.Chara.Edit.EditMaterials;
using OT = HS2Next.Data.Ext.MaterialEditorData.ObjectType;
using Object = UnityEngine.Object;

namespace HS2Next.Chara.Edit
{
    /// <summary>A MaterialEditor object: Character, Hair kind, Clothing kind, Accessory slot.</summary>
    public readonly struct EditObject : System.IEquatable<EditObject>
    {
        public readonly OT Type;
        public readonly int Slot;
        public EditObject(OT type, int slot) { Type = type; Slot = slot; }
        public static readonly EditObject Character = new EditObject(OT.Character, 0);
        public static EditObject Hair(int k) => new EditObject(OT.Hair, k);
        public static EditObject Clothes(int k) => new EditObject(OT.Clothing, k);
        public static EditObject Accessory(int slot) => new EditObject(OT.Accessory, slot);
        public bool Equals(EditObject o) => Type == o.Type && (Type == OT.Character || Slot == o.Slot);
        public override bool Equals(object obj) => obj is EditObject o && Equals(o);
        public override int GetHashCode() => (int)Type * 397 + (Type == OT.Character ? 0 : Slot);
        public override string ToString() => Type == OT.Character ? "Character" : Type + " " + Slot;
    }

    public sealed partial class EditContext
    {
        /// <summary>The scene's nipple stand rate (chara.state.nipple; CharaStateRealizer sets it), added to body index 32 in edits.</summary>
        public float NipStandRate;
        /// <summary>Mask properties' values before their first mask write (masks unit: restored when an edit stops writing them).</summary>
        public readonly Dictionary<(Material, string), Texture> MaskTexDefaults = new Dictionary<(Material, string), Texture>();

        /// <summary>Objects whose materials a unit of this flush wrote (MaterialEditor re-application, step 5).</summary>
        public readonly HashSet<EditObject> TouchedObjects = new HashSet<EditObject>();
        /// <summary>Roots of subtrees created by this flush (ownership + URP mapping, step 8).</summary>
        public readonly List<GameObject> NewSubtrees = new List<GameObject>();
        /// <summary>Roots whose materials got a Built-in shader again (hair shader passes): URP mapping in place, step 8.</summary>
        public readonly List<GameObject> RemapRoots = new List<GameObject>();
        /// <summary>Original activity of accessory alpha renderer objects (two-way alpha rule, G5).</summary>
        readonly Dictionary<GameObject, bool> alphaActive = new Dictionary<GameObject, bool>();

        partial void BeginFlush()
        {
            TouchedObjects.Clear();
            SwappedObjects.Clear();
            ForceReload.Clear();
            NewSubtrees.Clear();
            // incidents of loads between flushes (another character, the Maker's prefab-default reads) are not this flush's
            if (!InBuild) TextureGuard.TakePending();
            RemapRoots.Clear();
            if (!InBuild && ExtDirty) RegenerateExt();
            else if (!InBuild && MeDirty) RegenerateMaterialEdits();
        }

        partial void EndFlush()
        {
            CurrentUnit = null;
            MePending.Clear();
            if (!InBuild) SyncMissing();
            UpdateBadMipMissing();
        }

        /// <summary>A unit wrote materials of <paramref name="o"/>: its MaterialEditor value items go on top again (step 5).</summary>
        /// <summary>Re-derive the dynamic-bone chains in step 9: <paramref name="full"/> = rebuilt in the order of a full
        /// build with fresh flags (parts or noShake changed), else re-derived from the current bones (shapes, bundles).</summary>
        public void MarkDynBone(bool full)
        {
            if (InBuild) return;
            if (full) DynBoneFull = true;
            Editor?.MarkUnit(UnitMap.DynBone);
        }

        /// <summary>The bust chains are re-derived from the bones in step 9 (body shapes, bone modifiers).</summary>
        public void MarkBust()
        {
            if (InBuild) return;
            BustRederive = true;
            Editor?.MarkUnit(UnitMap.Bust);
        }

        public bool DynBoneFull, BustRederive;
        /// <summary>Accessory slots whose object must be reloaded in this flush (its anchor was inside a released part).</summary>
        public readonly HashSet<int> ForceReload = new HashSet<int>();

        /// <summary>
        /// Puts a newly loaded part where a full build puts it among its siblings (G6): after the last peer of a lower order
        /// that sits under the same parent, else before the first peer of a higher order, else after <paramref name="after"/>
        /// (the body prefab for clothes), else at the end.
        /// </summary>
        public static void Place(GameObject go, int order, IEnumerable<(int order, GameObject obj)> peers, GameObject after = null)
        {
            if (!go) return;
            var parent = go.transform.parent;
            int lower = -1, higher = int.MaxValue;
            foreach (var (o, obj) in peers)
            {
                if (!obj || obj == go || obj.transform.parent != parent) continue;
                int i = obj.transform.GetSiblingIndex();
                if (o < order) lower = System.Math.Max(lower, i);
                else if (o > order) higher = System.Math.Min(higher, i);
            }
            int me = go.transform.GetSiblingIndex();
            if (lower >= 0) go.transform.SetSiblingIndex(lower < me ? lower + 1 : lower);
            else if (higher != int.MaxValue) go.transform.SetSiblingIndex(higher < me ? higher : higher - 1);
            else if (after && after.transform.parent == parent)
            {
                int a = after.transform.GetSiblingIndex();
                go.transform.SetSiblingIndex(a < me ? a + 1 : a);
            }
        }

        public void Touched(EditObject o)
        {
            TouchedObjects.Add(o);
            if (!InBuild) Editor?.MarkUnit(UnitMap.ExtTextures);
        }

        /// <summary>The object was replaced or removed: every MaterialEditor item of it runs again (step 5).</summary>
        public void Swapped(EditObject o)
        {
            if (InBuild) return;
            SwappedObjects.Add(o);
            HS2Next.Chara.Ext.MaterialEditorApplier.ObjectSwapped(Ctx, (o.Type, o.Type == OT.Character ? 0 : o.Slot));
            Touched(o);
        }

        // ---------------------------------------------------------------- compositing

        /// <summary>Rebuilds a pass once in this flush and, when <paramref name="bind"/> (the build or a new
        /// creator), binds its RT to <paramref name="draw"/>.<paramref name="prop"/>. In an edit the RT is already bound (a
        /// MaterialEditor texture that replaced it keeps its place).</summary>
        public RenderTexture Rebuild(CompositeInputs c, Material draw, string prop, bool bind)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var rt = c.Creator.Rebuild();
            c.LastRebuildMs = sw.Elapsed.TotalMilliseconds;
            c.Dirty = false;
            c.Rebuilds++;
            if (Report != null) { Report.Creators++; Report.CreatorsRebuilt.Add(c.Name); }
            if ((bind || InBuild) && draw && prop != null)
            {
                EM.SetTexture(draw, prop, rt);
                if (!c.Draw.Contains(draw)) { c.Draw.Clear(); c.Draw.Add(draw); c.DrawProperty = prop; }
            }
            return rt;
        }

        /// <summary>Releases the colour passes of a clothes kind (its item is being replaced).</summary>
        public void ReleaseClothesCreators(int kind)
        {
            for (int j = 0; j < 3; j++)
            {
                foreach (var c in new[] { ClothesColor[kind, j], ClothesGloss[kind, j] })
                {
                    if (c == null) continue;
                    var rt = c.Creator.Target;
                    if (rt) { if (RenderTexture.active == rt) RenderTexture.active = null; rt.Release(); Object.DestroyImmediate(rt); }
                    if (c.Creator.Mat) Object.DestroyImmediate(c.Creator.Mat);
                }
                ClothesColor[kind, j] = null;
                ClothesGloss[kind, j] = null;
            }
        }

        /// <summary>All compositing passes (skin, then clothes by kind and group).</summary>
        public IEnumerable<CompositeInputs> Composites()
        {
            foreach (var c in new[] { BodySkin, BodyDetail, FaceSkin, FaceDetail }) if (c != null) yield return c;
            for (int k = 0; k < 8; k++)
                for (int j = 0; j < 3; j++)
                {
                    if (ClothesColor[k, j] != null) yield return ClothesColor[k, j];
                    if (ClothesGloss[k, j] != null) yield return ClothesGloss[k, j];
                }
        }

        // ---------------------------------------------------------------- part swaps

        /// <summary>Step 2 of the swap protocol: deactivate, release the render textures of its own materials (not the kept
        /// skin creators'), destroy its owned materials, forget its missing content, destroy it.</summary>
        public void ReleasePart(GameObject go, string unitId)
        {
            if (!go) return;
            // accessories anchored on a transform of this part (a head or hair prefab carries N_* points) are not destroyed
            // with it: they are taken out and reloaded in this flush, so they anchor on the new part as in a fresh build
            foreach (var a in Ctx.Accessories)
            {
                if (a == null || !a.Object || a.Object == go || !a.Object.transform.IsChildOf(go.transform)) continue;
                a.Object.transform.SetParent(Root ? Root.transform : null, true);
                ForceReload.Add(a.Slot);
                if (Input.Slots != null && Input.Slots.IdBySlot.TryGetValue(a.Slot, out var id)) Editor?.MarkUnit(UnitMap.Accessory(id));
            }
            go.SetActive(false);
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t != go.transform && t.GetComponent("SceneNodeHolder") != null)
                    Log.Add("chara.partHolder: a scene holder under " + go.name + " (" + t.name + ") is destroyed with the part");
            var keep = new HashSet<RenderTexture>();
            foreach (var c in new[] { BodySkin, BodyDetail, FaceSkin, FaceDetail }) if (c?.Creator.Target) keep.Add(c.Creator.Target);
            var rts = new HashSet<RenderTexture>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var m in Ctx.DrawsBodyMaterials(r) ? new Material[0] : r.sharedMaterials)
                {
                    if (!m || m == Ctx.BodyDraw || m == Ctx.FaceDraw) continue;   // (a cut body draws the body's material)
                    foreach (var id in m.GetTexturePropertyNameIDs())
                        if (m.GetTexture(id) is RenderTexture rt && rt && !keep.Contains(rt)) rts.Add(rt);
                }
            foreach (var rt in rts) { if (RenderTexture.active == rt) RenderTexture.active = null; rt.Release(); Object.DestroyImmediate(rt); }
            DestroyOwnedMaterials(go);
            DestroyImplants(go);
            DestroyAdditionalBones(go);
            foreach (var a in alphaActive.Keys.Where(k => !k || k.transform.IsChildOf(go.transform)).ToList()) alphaActive.Remove(a);
            ForgetMissing(unitId);
            go.transform.SetParent(null, false);
            Object.DestroyImmediate(go);
        }

        // ---- bad-mip textures (G7): every incident the texture guard reports is kept under the key of what loaded it (a
        // unit id, a part "<unit>" / "acc:#<slot>", "build" before the flush, "build.end" after it). Context.Missing lists,
        // per (source, texture), the incident of the first loader in build order, as the one bracket of a full build did.
        sealed class BadMip { public string Owner, Key; public long Seq; public MissingContent M; }
        readonly List<BadMip> badMips = new List<BadMip>();
        long badSeq;
        /// <summary>The unit the editor is running (loads without a part bracket are recorded under it).</summary>
        public string CurrentUnit;

        /// <summary>Records the guard's pending incidents under <paramref name="owner"/> (null: the running unit).</summary>
        public void DrainBadMip(string owner)
        {
            owner = owner ?? CurrentUnit ?? "build";
            foreach (var bad in TextureGuard.TakePending())
            {
                badMips.Add(new BadMip
                {
                    Owner = owner, Seq = badSeq++, Key = (bad.Source + "/" + bad.Texture).ToLowerInvariant(),
                    M = new MissingContent { Kind = "texture.badmip", Guid = bad.Source, Detail = bad.Asset + ": " + bad.Texture },
                });
                Log.Add("texture guard: " + bad.Detail);
            }
        }

        /// <summary>Step 3 bracket of a part load: what was pending belongs to the running unit, the load to the part.</summary>
        public void BeginPartLoad() => DrainBadMip(null);

        public void EndPartLoad(string unitId) => DrainBadMip(unitId);

        /// <summary>Forgets the incidents of an owner and of its sub-keys ("&lt;owner&gt;#…").</summary>
        public void ForgetMissing(string owner)
        {
            if (owner == null) return;
            badMips.RemoveAll(b => b.Owner == owner || b.Owner.StartsWith(owner + "#", System.StringComparison.Ordinal));
        }

        double OwnerRank(string owner)
        {
            if (owner == "build") return -1;
            if (owner == "build.end") return double.MaxValue;
            if (owner.StartsWith("acc:#", System.StringComparison.Ordinal))
            {
                // "acc:#<slot>" (a slot's load) and its sub-keys ("acc:#<slot>#tex": a hair-type slot's texture)
                int end = 5;
                while (end < owner.Length && char.IsDigit(owner[end])) end++;
                if (end > 5 && int.TryParse(owner.Substring(5, end - 5), out int slot))
                    return Rank(Editor?.Unit(UnitMap.AccList)) + 0.001 * (slot + 1);
            }
            int h = owner.IndexOf('#');
            var u = Editor?.Unit(owner) ?? (h > 0 ? Editor?.Unit(owner.Substring(0, h)) : null);
            return Rank(u);
        }

        static double Rank(EditUnit u) => u == null ? 1e12 : (double)u.Step * 1000000 + u.Order;

        /// <summary>Context.Missing's texture.badmip entries from the recorded incidents.</summary>
        public void UpdateBadMipMissing()
        {
            Ctx.Missing.RemoveAll(m => m != null && m.Kind == "texture.badmip");
            foreach (var g in badMips.GroupBy(b => b.Key))
                Ctx.Missing.Add(g.OrderBy(b => OwnerRank(b.Owner)).ThenBy(b => b.Seq).First().M);
        }

        /// <summary>Records a new part (ownership + URP mapping in step 8, scene layers / visibility in step 10).</summary>
        public void PartSwapped(GameObject go, EditObject o)
        {
            Swapped(o);
            if (!go) return;
            NewSubtrees.Add(go);
            Report?.NewObjects.Add(go);
            Editor?.MarkUnit(UnitMap.Ownership);
        }

        /// <summary>Materials under <paramref name="go"/> got a Built-in shader again: URP mapping in place in step 8.</summary>
        public void Remap(GameObject go)
        {
            if (!go || !UrpMapped) return;
            RemapRoots.Add(go);
            Editor?.MarkUnit(UnitMap.Ownership);
        }

        // ---------------------------------------------------------------- resolver bookkeeping

        public void UnregisterPart(string key)
        {
            if (Parts is DocumentPartResolver d) d.Unregister(key);
        }

        /// <summary>ctx.Missing's resolver entries (part.missing, part.missingMod, part.unresolved) = the resolver's current ones.</summary>
        public void SyncMissing()
        {
            if (!(Parts is DocumentPartResolver d)) return;
            Ctx.Missing.RemoveAll(m => m != null && DocumentPartResolver.IsResolverKind(m.Kind));
            foreach (var m in d.Missing)
                if (!Ctx.Missing.Exists(x => x.Kind == m.Kind && x.Detail == m.Detail)) Ctx.Missing.Add(m);
        }

        // ---------------------------------------------------------------- accessories

        // One accessory slot: list row, prefab ca_slotNN, anchor, moves, colours. Also the loader of the
        // MoreAccessories slots >= 20 (CharaContext.LoadAccessory).
        public GameObject LoadAccessory(AccessorySlot acc)
        {
            DrainBadMip(null);
            try { return LoadAccessoryCore(acc); }
            finally { DrainBadMip(UnitMap.Accessory("#" + acc.Slot)); }
        }

        GameObject LoadAccessoryCore(AccessorySlot acc)
        {
            int slot = acc.Slot, type = acc.Type;
            if (type < 351 || type > 363) return null;
            var e = Lookup(type, acc.Id, acc.Property, PartKeys.Accessory(slot));
            if (e == null) return null;
            acc.Entry = e;
            // Parent "null": head-weighted (under the body top, meshes rebound to the head bones; a stored key is kept in the
            // document and not used); "0": a fixed row (no anchor, a stored key is not used either)
            bool headWeighted = e["Parent"] == "null", fixedRow = e["Parent"] == "0";
            acc.HeadBound = headWeighted;
            acc.Anchor = null;
            acc.HairShaderType = -1;
            var go = LoadPart(e, $"ca_slot{slot:00}", headWeighted ? Ctx.BodyTop.transform : null);
            if (!go) return null;
            acc.Object = go;
            if (headWeighted) Rebind(go, Ctx.HeadBones, "cf_J_FaceRoot", Ctx.Hips);
            MergeSkinnedAccessory(go, acc);
            string key = acc.ParentKey ?? "";
            if (key == "" || fixedRow) key = e["Parent"];
            if (!headWeighted && key != "0" && key != "none")
            {
                var anchor = FindAnchor(Ctx.Anim.transform, key, acc) ?? FindAnchor(Ctx.HeadBone.transform, key, acc);
                if (anchor) { go.transform.SetParent(anchor, false); acc.Anchor = anchor; }
                else Log.Add($"acc{slot}: anchor {key} not found");
            }
            var cmp = go.GetComponent<CmpAccessory>();
            if (cmp != null && cmp.typeHair == 0 && cmp.rendAlpha != null)
                foreach (var r in cmp.rendAlpha.Where(r => Own(r, go))) alphaActive[r.gameObject] = r.gameObject.activeSelf;
            ApplyAccessoryValues(acc);
            Log.Add($"acc{slot}: {Name(e)} -> {key}");
            return go;
        }

        // The anchor of an accessory (HS2 resolves it in the body's bone table): the first transform of that name, depth first,
        // not inside another accessory's slot object (a mod accessory may carry its own copy of the body rig, e.g. an arm
        // with N_Chest; build and edits then anchor to the body's bone whatever the load order).
        Transform FindAnchor(Transform root, string name, AccessorySlot self)
        {
            var skip = new HashSet<Transform>();
            foreach (var a in Ctx.Accessories) if (a != null && a != self && a.Object) skip.Add(a.Object.transform);
            Transform Walk(Transform t)
            {
                if (skip.Contains(t)) return null;
                if (t.name == name) return t;
                for (int i = 0; i < t.childCount; i++) { var r = Walk(t.GetChild(i)); if (r) return r; }
                return null;
            }
            return root ? Walk(root) : null;
        }

        // Skinned accessories: a mod accessory may carry a SkinnedAccessory next to its CmpAccessory. Its listed
        // renderers are then drawn by the character's skeleton: every bone whose name the body rig has is replaced by the
        // character's bone (other bones stay in their slots), the root bone is cf_J_Root and the bounds a wide fixed box;
        // then the prefab's own rig copy, which hangs under the attach point, is destroyed at once so no later name lookup
        // finds it. A null entry in the list stops the merge there and keeps the copy, as the game does.
        static readonly Bounds SkinnedAccessoryBounds = new Bounds(new Vector3(0, 10, 0), new Vector3(20, 20, 20));
        static readonly HashSet<string> SkinnedAccessoryStops = new HashSet<string>(HS2Next.Save.DocEnum.AttachPoints) { "cf_t_root" };

        /// <summary>Puts the listed renderers of the slot object's SkinnedAccessory on the character's skeleton and destroys
        /// the prefab's rig copy (the accessory loader, after the head rebinding); nothing without the component.</summary>
        public void MergeSkinnedAccessory(GameObject go, AccessorySlot self)
        {
            var sa = go.GetComponent<SkinnedAccessory>();
            if (sa == null || sa.meshRenderers == null || go.GetComponent<CmpAccessory>() == null) return;
            if (Ctx.BodyBones == null || !Ctx.BodyBones.TryGetValue("cf_J_Root", out var jRoot) || !jRoot) return;
            var bones = SkinnedAccessoryBones(jRoot, go, self);
            int merged = 0;
            bool stopped = false;
            foreach (var smr in sa.meshRenderers)
            {
                if (!smr) { stopped = true; break; }
                if (!Own(smr, go)) continue;
                var b = smr.bones;
                for (int i = 0; i < b.Length; i++)
                    if (b[i] && bones.TryGetValue(b[i].name, out var t)) b[i] = t;
                smr.rootBone = jRoot;
                smr.bones = b;
                smr.enabled = true;
                smr.localBounds = SkinnedAccessoryBounds;
                merged++;
            }
            var copy = sa.skeleton;
            bool destroyed = false;
            if (!stopped && copy && copy.transform.IsChildOf(go.transform) && !go.transform.IsChildOf(copy.transform))
            {
                copy.transform.SetParent(null);
                Object.DestroyImmediate(copy);
                destroyed = true;
            }
            Log.Add($"acc{self.Slot}: skinned, {merged} renderers on the body rig, rig copy {(destroyed ? "destroyed" : stopped ? "kept (null entry)" : "none")}");
        }

        // The bone table of a skinned accessory: pre-order from cf_J_Root, a later transform of a name
        // replacing an earlier one (the head rig under cf_J_Head_s included); the subtrees of cf_t_root and of the 54 attach
        // points (the accessories hang there) are left out, and so are scene node holders and slot objects.
        Dictionary<string, Transform> SkinnedAccessoryBones(Transform root, GameObject go, AccessorySlot self)
        {
            var skip = new HashSet<Transform> { go.transform };
            foreach (var a in Ctx.Accessories) if (a != null && a != self && a.Object) skip.Add(a.Object.transform);
            var d = new Dictionary<string, Transform>();
            void Walk(Transform t)
            {
                d[t.name] = t;
                for (int i = 0; i < t.childCount; i++)
                {
                    var c = t.GetChild(i);
                    if (!SkinnedAccessoryStops.Contains(c.name) && !skip.Contains(c) && !Scenes.Runtime.SceneNodeHolder.IsHolder(c)) Walk(c);
                }
            }
            Walk(root);
            return d;
        }

        // A renderer of the accessory's own instance. A prefab may reference a renderer of another prefab asset (a stale
        // reference once that bundle was unloaded): writing it would change a shared asset, so only the instance's own
        // renderers are coloured.
        static bool Own(Renderer r, GameObject go)
        {
            // (a renderer under a bone copy the rebind destroyed can keep a live-looking wrapper: probe it)
            try { return r && go && r.transform.IsChildOf(go.transform) && r.sharedMaterials != null; }
            catch (MissingReferenceException) { return false; }
            catch (System.NullReferenceException) { return false; }
        }

        /// <summary>Moves and colours of a loaded accessory (the loader's writes; two-way alpha activity in edits). Hair-type
        /// rows get the hair shader pass (when the applied draw type differs from hair.shaderType) and the hair colours;
        /// other rows the colour areas and the alpha renderers' colour, gloss and metallic.</summary>
        public void ApplyAccessoryValues(AccessorySlot acc)
        {
            var go = acc.Object;
            var cmp = go ? go.GetComponent<CmpAccessory>() : null;
            if (cmp == null) return;
            var moves = new[] { cmp.trfMove01, cmp.trfMove02 };
            for (int i = 0; i < 2; i++)
            {
                if (!moves[i]) continue;
                moves[i].localPosition = acc.AddMove[i, 0] * 0.1f;
                moves[i].localEulerAngles = acc.AddMove[i, 1];
                var sc = acc.AddMove[i, 2];
                moves[i].localScale = sc == Vector3.zero ? Vector3.one : sc;
            }
            if (cmp.typeHair != 0) { ApplyHairTypeAccessory(acc, cmp, go); return; }
            var use = new[] { cmp.useColor01, cmp.useColor02, cmp.useColor03 };
            string[] cp = { "_Color", "_Color2", "_Color3" }, gp = { "_Glossiness", "_Glossiness2", "_Glossiness3" }, mp = { "_Metallic", "_Metallic2", "_Metallic3" };
            if (cmp.rendNormal != null)
                foreach (var r in cmp.rendNormal.Where(r => Own(r, go)))
                {
                    var m = OwnMaterial(r);
                    if (!m) continue;
                    for (int i = 0; i < 3; i++)
                    {
                        if (use[i] == 0) continue;
                        EM.SetColor(m, cp[i], acc.Color[i]);
                        EM.SetFloat(m, gp[i], acc.Gloss[i]);
                        EM.SetFloat(m, mp[i], acc.Metallic[i]);
                    }
                }
            if (cmp.rendAlpha != null)
                foreach (var r in cmp.rendAlpha.Where(r => Own(r, go)))
                {
                    var c = acc.Color[3];
                    var m = OwnMaterial(r);
                    if (m)
                    {
                        EM.SetColor(m, "_Color", c);
                        // colour 4's gloss and metallic (the game writes them on the alpha renderers' first material)
                        EM.SetFloat(m, "_Glossiness4", acc.Gloss[3]);
                        EM.SetFloat(m, "_Metallic4", acc.Metallic[3]);
                    }
                    // G5: active exactly when α != 0 (two-way; "active" = the prefab's own state of the object)
                    if (c.a == 0) r.gameObject.SetActive(false);
                    else if (!InBuild && alphaActive.TryGetValue(r.gameObject, out bool was)) r.gameObject.SetActive(was);
                }
        }

        /// <summary>
        /// A hair-type accessory (CmpAccessory.typeHair): when the draw type applied to the slot differs from hair.shaderType
        /// (always after a load), every material of every own rendNormal renderer gets the draw type's hair shader and the
        /// row's TexD (0) / TexC (1) as _MainTex (none named: the material keeps its own; no _Ringoff), queues kept; then the
        /// first material of each such renderer gets the hair colours: _Color = colour 1 (useColor01), _TopColor = colour 2
        /// (useColor02), _UnderColor = colour 3 (useColor03), _Specular = colour 4, _Metallic and _Smoothness = colour 1's,
        /// colours as stored. The alpha renderers are not touched. In an edit the shader pass maps the slot to the URP
        /// replacements again (step 8) and re-runs the slot's MaterialEditor items.
        /// </summary>
        void ApplyHairTypeAccessory(AccessorySlot acc, CmpAccessory cmp, GameObject go)
        {
            int shaderType = Chara?.Hair?.ShaderType ?? 0;
            var rends = new List<Renderer>();
            if (cmp.rendNormal != null)
                foreach (var r in cmp.rendNormal) if (Own(r, go) && !rends.Contains(r)) rends.Add(r);
            if (acc.HairShaderType != shaderType)
            {
                var shader = HairMaterials.ShaderOf(Catalog, shaderType);
                // the texture's bad-mip incidents are kept under the slot's sub-key: they change with the draw type
                string texOwner = UnitMap.Accessory("#" + acc.Slot) + "#tex";
                DrainBadMip(null);
                ForgetMissing(texOwner);
                var tex = rends.Count > 0 ? HairTypeTexture(acc.Entry, shaderType) : null;
                DrainBadMip(texOwner);
                foreach (var r in rends) HairMaterials.ApplyShader(OwnMaterials(r), shader, tex);
                acc.HairShaderType = shaderType;
                Report?.AccessoryHairShaded.Add(acc.Slot);
                if (!InBuild && shader && rends.Count > 0)
                {
                    Remap(go);
                    Swapped(EditObject.Accessory(acc.Slot));
                }
            }
            bool use1 = cmp.useColor01 != 0, use2 = cmp.useColor02 != 0, use3 = cmp.useColor03 != 0;
            foreach (var r in rends)
                HairMaterials.ApplyColours(OwnMaterial(r), use1 ? acc.Color[0] : (Color?)null, use2 ? acc.Color[1] : (Color?)null,
                    use3 ? acc.Color[2] : (Color?)null, acc.Color[3], acc.Metallic[0], acc.Smoothness[0]);
        }

        /// <summary>The main texture of a hair-type row for a draw type (TexAB with TexD / TexC); null when the row names none.</summary>
        Texture2D HairTypeTexture(ListEntry e, int shaderType)
        {
            if (e == null) return null;
            string ab = e["TexAB"], name = e[HairMaterials.TextureColumn(shaderType)];
            if (string.IsNullOrEmpty(ab) || ab == "0" || string.IsNullOrEmpty(name) || name == "0") return null;
            return Tex(e, "TexAB", HairMaterials.TextureColumn(shaderType));
        }

        /// <summary>Removes the object of a slot (edits).</summary>
        public void UnloadAccessory(int slot)
        {
            if (slot < 0 || slot >= Ctx.Accessories.Count) return;
            var a = Ctx.Accessories[slot];
            ForgetMissing(UnitMap.Accessory("#" + slot));   // also when the load had failed (no object)
            if (a?.Object) { ReleasePart(a.Object, UnitMap.Accessory("#" + slot)); Report?.AccessorySlotsSwapped.Add(slot); Swapped(EditObject.Accessory(slot)); }
            if (a != null) { a.Object = null; a.Entry = null; }
        }

        /// <summary>Replaces the object of a slot by the part of <paramref name="view"/> (edits).</summary>
        public GameObject ReloadAccessory(AccessorySlot view, string id)
        {
            int slot = view.Slot;
            while (Ctx.Accessories.Count <= slot) Ctx.Accessories.Add(Units.AccessorySlots.Empty(Ctx.Accessories.Count));
            var old = Ctx.Accessories[slot];
            Transform oldParent = null; int sibling = -1;
            if (old?.Object)
            {
                oldParent = old.Object.transform.parent;
                sibling = old.Object.transform.GetSiblingIndex();
                ReleasePart(old.Object, UnitMap.Accessory("#" + slot));
            }
            ForgetMissing(UnitMap.Accessory("#" + slot));   // also when the old load had failed (no object)
            Ctx.Accessories[slot] = view;
            GameObject go = null;
            if (!view.IsEmpty)
            {
                BeginPartLoad();
                go = LoadAccessory(view);
                EndPartLoad(UnitMap.Accessory("#" + slot));
                if (go)
                {
                    if (go.transform.parent == null && Root) go.transform.SetParent(Root.transform, true);   // as CharaBuilders' stray roots
                    if (sibling >= 0 && go.transform.parent == oldParent) go.transform.SetSiblingIndex(System.Math.Min(sibling, go.transform.parent.childCount - 1));
                    else Place(go, slot, Ctx.Accessories.Where(a => a != null && a.Object).Select(a => (a.Slot, a.Object)));
                    // (MoreAccessories' show is read data; the scene's state.accessories decides the visibility)
                    PartSwapped(go, EditObject.Accessory(slot));
                }
            }
            Report?.AccessorySlotsSwapped.Add(slot);
            Swapped(EditObject.Accessory(slot));
            UpdateExtraSlotStats();
            MarkDynBone(true);
            Editor?.MarkUnit(UnitMap.ExtFinal);
            return go;
        }
    }
}

// What CharaAssembler.Build exposes to the extension modules (Runtime/Chara/Ext) and to later stages (skin overlays,
// MaterialEditor): the objects, bones, materials, compositing passes and accessory slots of the character being
// built, plus what the extension stage records (skin layers to composite, missing content, statistics).
// Filled by the assembler before each extension hook; fields that a stage has not produced yet are null.
using System.Collections.Generic;
using HS2Next.Chara.Ext;
using HS2Next.Content;
using HS2Next.Data;
using HS2Next.Data.Ext;
using UnityEngine;

namespace HS2Next.Chara
{
    /// <summary>One accessory slot (0..19 from the card's accessory block, ≥ 20 from MoreAccessories), as loaded.</summary>
    public sealed class AccessorySlot
    {
        /// <summary>Absolute HS2 slot number (ChaControl numbering; slot-addressed plugin data uses it).</summary>
        public int Slot;
        /// <summary>Category 351..363; anything else is an empty slot.</summary>
        public int Type, Id;
        /// <summary>Anchor key ("" = the list row's Parent column).</summary>
        public string ParentKey = "";
        /// <summary>[correction 0/1, 0 position / 1 rotation / 2 scale] as stored (scale (0,0,0) is read as (1,1,1) when applied).</summary>
        public Vector3[,] AddMove = new Vector3[2, 3];
        public Color[] Color = new Color[4];
        public float[] Gloss = new float[4], Metallic = new float[4];
        /// <summary>colorInfo[l].smoothnessPower (hair-type accessories draw colour 1's as _Smoothness; default 0.5).</summary>
        public float[] Smoothness = { 0.5f, 0.5f, 0.5f, 0.5f };
        public int HideCategory, HideTiming = 1;
        public bool NoShake;
        /// <summary>Studio "show" flag of the slot as read (MoreAccessories `show`, default true). Read data only: the import
        /// folds it into the scene's state.accessories, the build does not hide the object for it.</summary>
        public bool Show = true;
        /// <summary>The hair shader type (hair.shaderType 0 / 1) the materials of a hair-type slot got from the loader or the
        /// accessory unit; −1 = no hair shader pass (not hair-type, or not loaded).</summary>
        public int HairShaderType = -1;
        /// <summary>The transform the loader parented the slot object to (null: no anchor, or a head-weighted row).</summary>
        public Transform Anchor;
        /// <summary>The row's Parent column is "null": the slot object hangs under the body top and its meshes follow the head
        /// bones; the stored key is not used.</summary>
        public bool HeadBound;
        /// <summary>True for MoreAccessories slots (≥ 20).</summary>
        public bool Extra;
        /// <summary>Sideloader property used to resolve the id (`[outfit.]accessory&lt;Slot&gt;.ChaFileAccessory.PartsInfo.id`).</summary>
        public string Property;
        /// <summary>The list row (null when empty or unresolved).</summary>
        public ListEntry Entry;
        /// <summary>The instantiated slot object `ca_slotNN` (null when empty, unresolved or the prefab is missing).</summary>
        public GameObject Object;

        public bool IsEmpty => Type < 351 || Type > 363;
        public override string ToString() => $"slot {Slot} type {Type} id {Id} {(Object ? Object.name : "-")}";
    }

    /// <summary>A reference the character uses whose content is not installed (kept for a later install).</summary>
    public sealed class MissingContent
    {
        /// <summary>e.g. "me.shader", "texture.badmip".</summary>
        public string Kind;
        public string Guid;
        public string Detail;
        public override string ToString() => $"{Kind} {Guid}{(Detail != null ? " (" + Detail + ")" : "")}";
    }

    /// <summary>A skin texture layer composited into a skin pass (KSOX semantics): the card's KSOX
    /// images (Source "ksox") and the layers build hooks register (Source "&lt;hook id&gt;:&lt;id&gt;").</summary>
    public sealed class SkinLayer
    {
        /// <summary>KSOX TexType: 1 BodyOver, 2 FaceOver, 3 BodyUnder, 4 FaceUnder, 41..44 detail (others).</summary>
        public int TexType;
        /// <summary>The texture (bundle asset), or null when the layer is kept as <see cref="Image"/> and decoded per composite.</summary>
        public Texture2D Texture;
        /// <summary>Encoded image (PNG/JPEG) of a card layer; decoded for each rebuild and released afterwards.</summary>
        public byte[] Image;
        /// <summary>Who registered it, e.g. "ksox" or "&lt;hook id&gt;:&lt;id&gt;" for a layer a build hook registered.</summary>
        public string Source;

        /// <summary>Pixel size (texture, else the image header); false when unknown.</summary>
        public bool TrySize(out int w, out int h)
        {
            if (Texture) { w = Texture.width; h = Texture.height; return true; }
            return ExtTextures.TrySize(Image, out w, out h);
        }
        public override string ToString() =>
            $"type {TexType} {(Texture ? Texture.name + " " + Texture.width + "x" + Texture.height : Image != null ? "image " + Image.Length + " B" : "null")} from {Source}";
    }

    /// <summary>The breast-normal source of a body mesh a build hook replaced: a
    /// NormalData asset in a bundle (of a zipmod, or abdata when <see cref="Zipmod"/> is null).</summary>
    public sealed class BodyNormalsSource
    {
        /// <summary>The hook that replaced the body mesh and the id it gives the replacement (CharaContext.ReplacedBodyMesh).</summary>
        public string HookId, Id;
        public ZipmodInfo Zipmod;
        public string File, Asset;
        /// <summary>"&lt;hook id&gt;:&lt;id&gt;" (BustNormalsState.Source).</summary>
        public string Source => HookId + ":" + Id;
        public override string ToString() => $"{Asset} in {File} ({Zipmod?.Guid ?? "abdata"}, {Source})";
    }

    public sealed class CharaContext
    {
        public Card Card;
        /// <summary>What the build read (card, typed plugin data, part resolver, accessory ids).</summary>
        public CharaBuildInput Input;
        public ContentCatalog Catalog;
        /// <summary>The assembler's log (one line per step).</summary>
        public List<string> Log;
        public bool IsMale;
        /// <summary>Decoded plugin data of the card (null when extensions are off or the card has none).</summary>
        public CardExtData Ext;
        public CharaExtOptions Options;

        /// <summary>The character documents the build read: the node's stored documents (live
        /// objects: an edit changes them and marks the units), or for a card build the documents converted from the card.</summary>
        public HS2Next.Save.CharaDocument Chara;
        public HS2Next.Save.CoordDocument Coord;
        /// <summary>The setter layer of this character: MarkDirty(path) after a document change, Flush() once per frame.</summary>
        public HS2Next.Chara.Edit.CharaEditor Editor;
        /// <summary>The build state the edit units work on.</summary>
        public HS2Next.Chara.Edit.EditContext Edit;
        /// <summary>MaterialEditor's per-character state between applications (decoded textures, textures in use per object).</summary>
        public HS2Next.Chara.Ext.MaterialEditorApplier.Cache MaterialEditorCache;

        public GameObject Root, BodyTop, Anim, HeadBone, Body, Head;
        /// <summary>The list row of <see cref="Head"/> (its face compositing bundle).</summary>
        public ListEntry HeadRow;
        /// <summary>The character's motion orchestrator on Root (Runtime/Motion/CharaMotion.cs); stages (dynamic bones, look, expression, corrections) register here.</summary>
        public HS2Next.Motion.CharaMotion Motion;
        /// <summary>Hair objects by kind: 0 back (ct_hairB), 1 front, 2 side, 3 option (null when absent).</summary>
        public GameObject[] Hair = new GameObject[4];
        /// <summary>Clothes objects by kind 0..7 (null when absent or a "none"/dummy row).</summary>
        public GameObject[] Clothes = new GameObject[8];
        /// <summary>Clothes list rows by kind (the loader ran; includes "none" rows).</summary>
        public ListEntry[] ClothesEntries = new ListEntry[8];
        /// <summary>Static clothes state per kind (0 on, 1 half, 2 off); all 0 = fully dressed.</summary>
        public int[] ClothesState = new int[8];
        /// <summary>DynamicBones of the clothes top / bottom re-pointed to the body bones by the part loader
        /// (copyDynamicBone; components inside the destroyed skeleton copy not counted).</summary>
        public int DynamicBonesRepointed;

        /// <summary>Body skeleton bones from cf_J_Root (first occurrence of a name; includes bones build hooks implanted).</summary>
        public Dictionary<string, Transform> BodyBones;
        /// <summary>Head skeleton bones from p_cf_head_bone.</summary>
        public Dictionary<string, Transform> HeadBones;
        public Transform Hips;

        /// <summary>The body renderer (o_body_cf / o_body_cm) and its draw material (slot 0).</summary>
        public SkinnedMeshRenderer BodyRenderer;
        public Material BodyDraw, FaceDraw;
        /// <summary>The materials made for this character (per-renderer copies, the draw materials, the extension modules'
        /// copies). Every other material is shared (bundle) and is never written: a module copies it first through
        /// <see cref="HS2Next.MaterialCopies"/> and adds the copy here. After the build every renderer slot holds one.</summary>
        public readonly HashSet<Material> OwnedMaterials = new HashSet<Material>();
        /// <summary>Renderers of hair, clothes and accessory parts that draw the body renderer's materials (after 1.0.0,
        /// the parts' ChaCopyBodyMaterial lists), registered by the part loader; the unit
        /// copy.bodyMaterial gives them the body's material array. A target owns no material: the writers skip it. Destroyed
        /// renderers stay until the unit's next run drops them.</summary>
        public readonly HashSet<Renderer> BodyMaterialTargets = new HashSet<Renderer>();
        /// <summary>True for a live body-material target (<see cref="BodyMaterialTargets"/>).</summary>
        public bool DrawsBodyMaterials(Renderer r) => r && BodyMaterialTargets.Contains(r);
        /// <summary>The body cuts of the clothes parts (after 1.0.0, the parts' BodyCut
        /// components), registered by the part loader; the unit body.cut and the scene's clothes state switch the body and the
        /// cut bodies between them. Destroyed components stay until the next switch drops them.</summary>
        public readonly List<MdgqBodyChange.BodyCut> BodyCuts = new List<MdgqBodyChange.BodyCut>();
        /// <summary>The cut-body renderers of <see cref="BodyCuts"/>: their first slot draws the body's first material.</summary>
        public readonly HashSet<Renderer> BodyCutRenderers = new HashSet<Renderer>();
        /// <summary>True for a live cut-body renderer (<see cref="BodyCutRenderers"/>).</summary>
        public bool DrawsBodySkin(Renderer r) => r && BodyCutRenderers.Contains(r);
        /// <summary>The meshes made for this character (Phase 4.2, the per-character
        /// copies of the body mesh the breast normals are blended into; same name and vertex count as the drawn mesh they
        /// copy). Destroyed with the content (<see cref="ReleaseOwnedMeshes"/>, from the realization's ReleaseContent).</summary>
        public readonly List<Mesh> OwnedMeshes = new List<Mesh>();
        /// <summary>The breast-normal state of the bust.normals unit (null until the unit ran on a female;
        /// Runtime/Chara/Edit/Units/BustNormalsUnit.cs).</summary>
        public HS2Next.Chara.Edit.BustNormalsState BustNormals;
        /// <summary>Where the breast normals of a body mesh a build hook replaced come from (set by that hook; null = the
        /// vanilla body's p_cf_body_00_Nml).</summary>
        public BodyNormalsSource BodyNormals;

        /// <summary>Destroys the owned meshes (<see cref="OwnedMeshes"/>); returns how many were destroyed.</summary>
        public int ReleaseOwnedMeshes()
        {
            int n = 0;
            foreach (var m in OwnedMeshes) if (m) { Object.DestroyImmediate(m); n++; }
            OwnedMeshes.Clear();
            BustNormals?.Released();
            return n;
        }
        /// <summary>Skin compositing passes (create_skin_body, create_skin detail_body, create_skin_face, create_skin detail_face);
        /// Rebuild() re-composites into the RT already bound to the draw material.</summary>
        public TexCreator BodySkin, BodyDetail, FaceSkin, FaceDetail;

        /// <summary>Every accessory slot by absolute slot number (index = slot); empty slots included, entries never null.</summary>
        public List<AccessorySlot> Accessories = new List<AccessorySlot>();
        public ShapeEngine BodyShape, FaceShape;

        /// <summary>Loads one accessory slot through the assembler's own path (lookup, prefab, parent, moves, colours).</summary>
        public System.Func<AccessorySlot, GameObject> LoadAccessory;

        // ---- produced by the extension stage
        /// <summary>Skin layers composited by the texture stage (card KSOX layers first, then the layers build hooks registered).</summary>
        public List<SkinLayer> SkinLayers = new List<SkinLayer>();
        public List<MissingContent> Missing = new List<MissingContent>();
        /// <summary>The id a build hook gives the body mesh it put in place of the prefab's (null = the prefab's body mesh).</summary>
        public string ReplacedBodyMesh;
        /// <summary>The bone modifier component (ABMX + rule effects); null when there are no modifiers.</summary>
        public BoneModifierApplier Bones;
        /// <summary>PushUp data (stored; effect off).</summary>
        public PushUpData PushUp;
        public ExtStats Stats = new ExtStats();

        /// <summary>The slot object of an absolute slot, or null.</summary>
        public GameObject AccessoryObject(int slot) => slot >= 0 && slot < Accessories.Count ? Accessories[slot].Object : null;
    }
}

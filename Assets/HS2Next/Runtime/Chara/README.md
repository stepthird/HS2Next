# Runtime/Chara

Character assembly: one character GameObject from a stored HS2Next character (its appearance documents) or from an HS2
card. The behaviour reproduces the game's character loading as measured on the reference install; no game code is copied.

## How a build runs

`CharaAssembler.Build` is orchestration only: it makes the context, the skeleton and the body prefab, runs the BodyMesh
build hooks, then marks every **edit unit** (`Runtime/Chara/Edit`) and flushes them once in the build order, then attaches
the motion stages. The same units apply every later edit of the character's documents (`CharaContext.Editor`), so the
maker and a fresh build share one code path. The units read the documents: the scene node's stored character and
coordinate documents, or, for a card, the documents converted from the card. Every list row comes from the input's part
resolver with a part key (cards and stored characters have their own resolvers, `Runtime/Chara/Documents`).

Build order:

1. Skeleton: `p_cf_anim` (animator disabled, rest pose) and `p_cf_head_bone` under `cf_J_Head_s`.
2. Body prefab and draw material, rebound to `cf_J_Root`; then the **BodyMesh** build hooks (none in the official build).
3. Parts: head (rest pose copied by name into `p_cf_head_bone`), eyebrows, eyes, eyelashes, hair (4 kinds), clothes (8
   kinds), accessories (20 slots plus the extra slots of the card).
4. Masks: the body and inner-clothes hide masks.
5. Composites: the skin passes (body skin, sunburn, paints into the two 4096 creators; face skin, eyeshadow, paints, cheek,
   lip, mole) and the clothes colour and pattern passes (`TexCreator`).
6. Materials: detail, nipple, underhair and nail textures and the colours on the draw materials.
7. Plugin textures: KSOX skin layers and the layers build hooks register, KCOX clothes overlays, MaterialEditor edits.
8. The mosaic stage (`MosaicStageUnit`): the **Textures** build hooks, the mosaic of the mosaic set, the **AfterMosaic**
   build hooks.
9. Shapes: body (`cf_anmShapeBody` / `cf_custombody`) and face (the head entry's shape data + `cf_customhead` /
   `cm_customhead`). The slider-to-bone tables `Resources/shape_body_map.txt` and `shape_face_map.txt` were derived from
   black-box measurements of the running game (`Tools/shape_measure.py`, `Tools/shape_fit.py`, records in
   `Tools/survey/shape/`).
10. Plugin final: push-up stored, Additional Accessory Controls, ABMX, DynamicBoneEditor, InvisibleBody, then the **Final**
    build hooks.
11. Ownership: every renderer slot that still holds a material the build did not make gets a per-character copy (section
    "Materials").
12. `HS2Next.Motion.MotionSetup.Attach(Context)`: the motion stages (`Runtime/Motion`: animation sampling and hand shapes,
    joint correction, neck and eye look, expression, bust and dynamic bones) register on the root's `CharaMotion`. The
    Animator of `p_cf_anim` stays disabled until an animation is loaded.

Accessory moves: the card's `addMove` is a MessagePack two-dimensional array, `[2, 3, [p0, r0, s0, p1, r1, s1]]` (lengths,
then the six vectors in row-major order); a jagged `[[p0, r0, s0], [p1, r1, s1]]` is accepted too. `N_move` / `N_move2` get
position × 0.1, Euler angles and scale.

Top and bottom clothes with `copyDynamicBone`: their DynamicBones' `m_Root`, `m_Exclusions` and `m_notRolls` are re-pointed
to the body bones of the same name before the rebinding destroys the part's own skeleton copy
(`CharaContext.DynamicBonesRepointed` counts them).

Outputs: the character root (`chaF_<name>` / `chaM_<name>`), `Log` (one line per part, MISSING rows, mask writes),
`PartsLoaded`, `BodyShape` / `FaceShape`, `IsMale`, `Context` (`CharaContext`: objects, bone dictionaries, draw materials,
skin compositing passes, the accessory slot list `AccessorySlot`, skin layers, missing-content records, extension
statistics).

## Files

- `CharaAssembler.cs`: the build orchestration and `TexCreator` (one skin or clothes compositing pass through an HS2
  "create" material; optional under / over skin layers, `Resize`).
- `ShapeEngine.cs`: body and face shape sliders.
- `TextureGuard.cs`: the last guard of asset loads in headless runs against mod textures Unity 6 cannot load (mip count 0):
  such textures are swapped for a 4 × 4 fallback and listed in `CharaContext.Missing` ("texture.badmip"). Bundles are
  normally checked and repaired before their first load by the catalog (`Runtime/Content`).
- `CharaContext.cs`: what a build exposes to the edit units, the extension modules and later stages.
- `Edit/`: the edit units and their scheduler. `Documents/`: the inputs (documents, card conversion, part resolvers).
- `Ext/`: the card plugin data applied to the character (section "Extensions"; `Ext/README.md`).

## Male (`Parameter.sex == 0`)

The path is shared; `CharaSex` holds the per-sex categories and assets, and a few steps differ:

| Item | Female | Male |
| --- | --- | --- |
| Root name | `chaF_` | `chaM_` |
| Skeleton / head skeleton | `p_cf_anim` / `p_cf_head_bone` | same |
| Body prefab / mesh / draw material | `p_cf_body_00` / `o_body_cf` / `cf_m_skin_body_00` | `p_cm_body_00` / `o_body_cm` / `cm_m_skin_body_00` (one slot, no liquid) |
| Body skin / detail / sunburn | 231 / 232 / 233 | 131 / 132 / 133 |
| Head / face skin / face detail | 210 (id 0 → `add35` override) / 211 / 212 | 110 (no override) / 111 / 112 |
| Beard | - | 121 `AddTex` → face draw `_Texture5`, `beardColor` → `_Color5` |
| Clothes | 240..247 | top 140, bottom 141, gloves 144, shoes 147; inner top / inner bottom / pantyhose / socks keep 242 / 243 / 245 / 246 |
| Prefab column | `MainData` | `MainData02` when set (not `"0"`), any category |
| Front hair default | 1 | 2 |
| Face slider list | `cf_customhead` | `cm_customhead` (key data from the head entry: `cm_anmShapeHead_00`) |
| Height (body slider 0) | card value | card value by default (as with the HS2_UnlockPlayerHeight plugin, under which the shape measurements were taken); `CharaAssembler.MaleHeightPin075 = true` restores the vanilla 0.75 pin |
| Bust / hip physics | prefab state | the `CmpBoneBody.dbcBust` colliders disabled; the four `DynamicBone_Ver02` (`Mune_L/R`, `Siri_L/R`) keep running with the card's values, because the game never reads its disable flags (`DynamicBoneSetup.Bust.MaleBustPhysics` turns them off) |
| `N_dan` | untouched | set inactive (the Studio's "add from card" default) |
| Range check | per-part fallbacks only | an id missing from its list falls back to the default (0; front hair 2), shape values are clamped to 0..1, face values cut to 59 entries |

Body shape data and the shape evaluator are the female ones for both sexes; the male head rest pose comes from the male
head prefab through the same name-based copy.

## Extensions

Card plugin data (decoders in `Runtime/Data/Ext`) is applied by `Ext/CharaExtApplier.Apply(context, ext, options, stage)`
at the build steps above, in the order vanilla assembly → textures and overlays → MaterialEditor → shapes → ABMX (+ push-up)
→ DynamicBoneEditor. The card's uncensor selection (`body.uncensor`) is kept as data only: no module reads it.

Generic build hooks (`Ext/CharaBuildHooks.cs`; the plugin API's build hooks are built on them): code registers
`CharaBuildHooks.Register(id, stage, run, order)` for BodyMesh (after the body prefab is rebound; may replace body meshes,
implant bones with `ImplantBone`, register skin layers, set `Context.ReplacedBodyMesh` / `BodyNormals`), Textures and
AfterMosaic (around the mosaic stage, in the build and in every edit that runs an earlier step again) and Final (after the
plugin final step); FK node rows through `RegisterFkNodes`. Each run replaces the hook's entry in `Context.Stats.Hooks`.
The official build registers none; the mosaic stage is a fixed unit, not a hook.

| Step | Module | What it does |
| --- | --- | --- |
| Accessories | (accessory units) | Slots 20 and above (`moreAccessories` element i = slot 20 + i) are build slots of the accessory units and load like slots 0..19; the stored `show` is folded once into the scene's accessory state at import; `hideCategory` / `hideTiming` are kept as read. `Context.Accessories` is the full list indexed by absolute slot. |
| Plugin textures | `SkinOverlayApplier` | Card KSOX images (first) and the skin layers build hooks register go on the four `TexCreator`s as `Under` / `Over` lists (body 3 / 1, body detail 43 / 41, face 4 / 2, face detail 44 / 42); each pass with layers is rebuilt. An overlay larger than the render texture grows it (`OverlaySizeLimit`, default 2 ×) and the draw material is rebound. Later `Rebuild()` calls keep the layers. The Studio switch `EnableInStudioSkin` is honoured. |
| | `ClothesOverlayApplier` | KCOX per clothes kind on the colour render texture bound to `_MainTex` of the first `rendNormal01` (or `03`) and `rendNormal02` renderer: override = clear to transparent, then the image (Default alpha-over or LinearAlpha); grown for larger images. |
| | `MaterialEditorApplier` | MaterialEditor items in the plugin's order on the object's renderers (clothes kind, absolute accessory slot, hair kind, character body parts), by material name; materials copied per character first; shaders only through `ShaderMap` (others kept, `Context.Missing` "me.shader"); textures decoded once per character and id (`ExtTextures`), sRGB as the plugin does. |
| Plugin final | push-up | `Context.PushUp` = the decoded data; log "pushUp: stored, off". |
| | `AccessoryControlsApplier` | Additional Accessory Controls' start-up pass for the static state (`Context.ClothesState`, all 0): clothes rules with suppress / override lists, STARTUP, the hair-accessory rule, links, the hair-part rule, body-part rules as bone effects. Studio semantics by default. |
| | `AbmxApplier` + `BoneModifierApplier` | ABMX modifiers resolved to bones (BodyTop = the body under `cf_J_Root` without accessory subtrees, accessories 0..19, Unknown = body then accessories; `c_J_hair` alias); unknown bones listed once. The component on the root combines all modifiers per bone and writes scale (multiply), length / position and rotation (deltas on the current pose) in `LateUpdate`; `Apply()` runs once at build time for headless use. |
| | `DynamicBoneEditorApplier` | Overrides written onto the accessory `DynamicBone` components whose `m_Root` name matches (fields only). |
| | `InvisibleBodyApplier` | `Visible == false`: renderers under `p_cf_body_00` / `p_cm_body_00` / `p_cf_anim` disabled, not descending into `N_*` accessory anchors. |

Options (`CharaAssembler.ExtOptions`, `CharaExtOptions`): `Enabled` (or `ExtOptions = null`) switches everything off; one
bool per module; `AccessoryControlsStudio` (true); `PushUpEffect` (false; not implemented); `BoneModifierComponent` (true:
keep the `LateUpdate` component); `SkinOverlays` / `ClothesOverlays` / `MaterialEditor` (true); `OverlaySizeLimit`
(`OriginalX2`); `OverlaysStudio` (true: honour the cards' Studio overlay switches); `MaterialEditorLinearNormals` (false).
Module exceptions are caught, logged ("ext error ...") and counted in `Context.Stats.Errors`.

Skin passes after the texture step: `Context.BodySkin` / `BodyDetail` / `FaceSkin` / `FaceDetail` carry their layers;
`Rebuild()` re-composites base + layers into `Target` (the render texture bound to `_MainTex` / `_DetailMainTex`) and keeps
that binding, so MaterialEditor texture edits on the draw materials stay in place. Card overlay images are decoded per
rebuild and released; MaterialEditor textures are held by the root's `CharaExtResources` and released when the character
is destroyed (`Ext/README.md`).

## Materials

Bundle materials are shared by every character that uses the asset and are never written. A build's own materials are in
`Context.OwnedMaterials` (ownership by identity, not by name): the per-renderer copies (named `<name> (Instance)`), the body
and face draw materials, and the copies the extension modules and build hooks make. The ownership step copies every slot
that still holds a shared material (hair-type accessories, uncoloured clothes parts, ...), so a finished character renders
only its own materials (switch `CharaAssembler.OwnAllMaterials`, for measurements only).

Copies and shader assignments go through `Runtime/Rendering/MaterialCopies.cs` and keep the material's keywords as loaded.
The reason is an Editor behaviour: the Unity Editor (not a player, not HS2) switches `[Toggle]` / `[MaterialToggle]`
keywords to the float values when a loaded material's properties are first read and on every `new Material(source)`.
Without the recorded keywords a card's build would depend on what was built before it, and Editor copies would carry
keywords the game does not have. `CharaDocBuildTest` P2 checks both (shared materials on renderers, rebuilds after other
builds).

## Known gaps

- Beard under URP: the fitted `HS2Next/AIT Skin True Face` samples `_Texture5` with a placeholder UV
  (`uv0 * _Texture5UV.xy + zw`) and draws nothing on the tested cards, while Built-in shows the beard
  (`CharaRenderTest -male -urp -diff` prints the per-pipeline beard footprint). The original `_Texture5UV` mapping is not
  known.
- The id range check runs on the male path only; the female path leaves missing face and body texture ids empty instead
  of falling back to id 0.
- Not built here: hit meshes, fluids, expression pattern validation (lists 502 / 504). The Studio's son toggle and length
  and the silhouette are scene state (`CharaStateRealizer`); the simulation belongs to the motion stages (`Runtime/Motion`).
- Modded male heads are supported through the generic mod-row resolution; absent mods fall back by the range check.
- The body paint mask `cf_body_paintmask` (female UV) is used for the male body as the game does; whether it lines up with
  the male UVs was not measured.

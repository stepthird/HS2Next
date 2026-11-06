# HS2Next.Chara.Ext

Applies HS2 card plugin data to a character built by `CharaAssembler`: the "geometry" group (Additional Accessory Controls,
ABMX, DynamicBoneEditor, InvisibleBody; MoreAccessories slots 20 and above are loaded by the accessory units like slots
0..19, see `Runtime/Chara/README.md`) and the "textures" group (skin layers including KSOX, KCOX, MaterialEditor). The
behaviour follows the plugins' observable results on the reference install. Decoding is `Runtime/Data/Ext`; this folder
only applies. Order, hooks and options are summarised in `Runtime/Chara/README.md` ("Extensions").

## Files

| File | What it applies |
| --- | --- |
| `CharaExtApplier.cs` | entry point `Apply(context, ext, options, stage)`, `CharaExtOptions`, `ExtStats`, `ExtStage` |
| `CharaBuildHooks.cs` | generic build hooks (BodyMesh / Textures / AfterMosaic / Final), `ImplantBone`, FK node providers; the mosaic stage slot `MosaicStageUnit` |
| `AccessoryControlsApplier.cs` | Additional Accessory Controls |
| `AbmxApplier.cs` | ABMX bone lookup |
| `BoneModifierApplier.cs` | component: bone modifiers on the current pose (ABMX + rule effects) |
| `DynamicBoneEditorApplier.cs` | DynamicBoneEditor |
| `InvisibleBodyApplier.cs` | InvisibleBody |
| `SkinOverlayApplier.cs` | KSOX skin overlays + the skin layers that build hooks register, into the four skin passes |
| `ClothesOverlayApplier.cs` | KCOX clothes overlays on the clothes colour render textures |
| `MaterialEditorApplier.cs` | MaterialEditor, character side: object resolution, skip reasons, statistics, the status read; the writes are `Runtime/Rendering/MaterialWrites.cs`, around them `MaterialBaseline.cs` |
| `MaterialEditorShaders.cs` | MaterialEditor's shader registry: the manifests' `<Shader>` declarations of the loaded zipmods (later wins, cached by zipmod stamp) and the declared copy of a name, loaded through the catalog |
| `OverlayCompositor.cs` | GPU compositing (`ApplyOverlay`, skin pass with under / over layers, render-texture resize) |
| `Resources/HS2NextOverlayComposite.shader` | `Hidden/HS2Next/OverlayComposite`, the overlay blend (Default / LinearAlpha, override) |
| `ExtTextures.cs` | decoding of embedded PNG / JPEG with a reference-counted SHA-1 cache; PNG / JPEG header sizes |
| `CharaExtResources.cs` | component on the root: releases the character's `ExtTextures` references on destroy |

Push-up has no module: the decoded data is stored on `CharaContext.PushUp` and the effect stays off.

## Texture stage (hook `Textures`, after the base assembly, before the shapes)

1. Skin layers. The card's KSOX images (`SkinLayer.Source` "ksox") are put in front of the layers that build hooks register
   on `CharaContext.SkinLayers`. Each skin pass gets its under / over lists on `TexCreator.Under` / `Over` and is rebuilt:
   under-layers are alpha-composited onto a copy of the pass input, the pass runs on that copy, over-layers go on the
   result. Body colour: BodyUnder 3 / BodyOver 1, body detail 43 / 41, face colour 4 / 2, face detail 44 / 42. The lists stay
   on the `TexCreator`, so every later `Rebuild()` re-applies them, as KSOX does inside each skin rebuild. An overlay larger
   than its render texture grows it (`OverlaySizeLimit`, default `OriginalX2`); the draw material is rebound to the new one.
2. KCOX, per main id (`ct_clothesTop` .. `ct_shoes`): the first renderer of `rendNormal01` (or `rendNormal03` when 01 is
   empty) and of `rendNormal02`; their `_MainTex` must be the colour render texture. Override clears it to (0,0,0,0); an
   image is composited with its Override flag and blending mode. `[nil, true]` is reproduced literally (cleared, nothing
   drawn).
3. MaterialEditor items in the plugin's apply order (`MaterialEditorData.AllItems()`), matched by material name over every
   renderer of the object (clothes kind, absolute accessory slot including 20 and above, hair kind, or the character
   body-part renderers). Shared bundle materials are copied per character before the first write. Shader edits go through
   `ShaderMap`: the copy a name stands for comes from the registry (`MaterialEditorShaders`: the copy a zipmod manifest
   declares; none for the plugin's own game shaders and for undeclared names), never from the shaders loaded in the
   process, so a build does not depend on what was built before it. The replacement is ShaderMap's for that copy (without a
   copy, the name's main replacement); it is applied as the declared copy when no render pipeline is active
   (`ShaderMap.ApplyUrp` maps it later), else as the replacement itself; the render queue is kept, then the stored queue
   written. A name without a replacement for its copy keeps the current shader; the item is skipped and a `MissingContent`
   "me.shader" record added. Texture edits decode the image once per character and id (`ExtTextures`).

Memory. Card skin images stay encoded on the `SkinLayer` and are decoded only for the duration of a rebuild, then released.
KCOX images are released right after compositing. MaterialEditor textures are decoded lazily (only for edits whose material
and property exist), mipmapped and non-readable, and held by the character's `CharaExtResources` until the root is
destroyed; identical bytes share one texture across characters (SHA-1 cache, reference-counted). Grown render textures: a
4096² face texture is 85 MB with mips, a 6144² body texture 192 MB.

Without a graphics device (`-nographics`) nothing is drawn: images are still decoded and counted, and render-texture growth
beyond 4096 is only recorded.

Statistics are in `ExtStats` (`SkinLayersComposited` / `Skipped`, `SkinResized`, `Kcox*`, `Me*` including skip reasons,
unknown shaders, properties absent from the fitted URP shader, missing material names) and in one log line per module
("ksox:", "kcox:", "materialEditor:").

## Deliberate deviations and choices

- A card's bone modifiers are applied in the motion stage `bones.modifiers` (phase 120, `Runtime/Motion/Modifiers/BoneModifierStages.cs`), after the
  animation, the hand patterns and the heel lift and before everything else that writes bones, as deltas on this frame's input. On a channel a clip
  holds constant this equals the plugin's frozen baseline; on a channel a clip moves the motion is kept. A second stage (phase 960) records what the
  frame ended on. A channel holding the pass's last write or that end value has not been rewritten, so its base stays; any other value becomes the base.
  Only channels with a value are read or written. A controller's bind and the shape guard's and rest pose's captures see the bones without the modifiers.
- The ABMX BodyTop lookup skips all accessory slot objects, the MoreAccessories slots included. Accessory locations stay
  limited to slots 0..19, as in the plugin.
- Additional Accessory Controls: when STARTUP and a clothes rule meet in one slot, STARTUP wins (the plugin's order there was
  not observable). Studio semantics are the default (`AccessoryControlsStudio`).
- DynamicBoneEditor: components on inactive objects are included. The plugin collects only active ones, a few frames after
  load; without the inactive ones, a slot hidden by an accessory rule would lose its overrides when it is shown again.
- InvisibleBody: the plugin's list of 35 Studio / H prop renderer names is not reproduced. None of those names is part of a
  character.
- Texture stage order: everything is composited once at build time, in the order above, without HS2's frame delays.
  MaterialEditor texture edits on the skin draw materials survive later skin rebuilds because `Rebuild()` keeps the
  binding. This holds for the face as well, where HS2 does not re-apply the edits after rebuilding the face texture.
- KCOX render-texture growth scales the finished colour result into the larger texture. The plugin re-runs the colour pass
  at the larger size.
- MaterialEditor textures: one decoded texture per image. Wrap, filter and aniso are copied from the first texture it
  replaces, and every later use shares them. The plugin blits each image into a render texture of its own; here the
  `Texture2D` is used directly. Decoding is sRGB for all properties, as the plugin does, so normal maps are sampled as
  stored. `MaterialEditorLinearNormals` decodes Bump / Normal properties linear instead.
- MaterialEditor shaders: the registry's per-shader `<Property DefaultValue>` writes are not reproduced. The fitted shaders
  keep the material's values.

## Not implemented

- The card's uncensor selection is kept as data only: the official build applies no body, part or bone of it, and a
  character keeps the body's own bone chain.
- KSOX eye / eyebrow / eyeline types (5-30): counted as skipped. No card on the reference install has one. KCOX
  `Colormask_`, `Pattern_` and `Override_` ids: skipped (none on the reference install). `TextureSizeOverride` is honoured
  for growth only.
- MaterialEditor: projector edits, cubemaps, texture animation (the first texture is shown), "CorrectTongue", the alpha-mask
  copy to the plugin's material copies, and re-application after clothes-state changes (there are none at build time).
  Shaders without a fitted URP replacement (Hanmen/*, Fur 20 B, eye_shader, FX/Hologram Effect, AIT/main standard2, ...) are
  kept as the item's current shader; their properties are then mostly absent from it ("property not on the shader").
- Push-up effects; Additional Accessory Controls' AdvancedParent, CharacterAccessory, AutoMatchBackHairColor and
  re-evaluation after clothes-state changes; dynamic-bone simulation and the particle rebuild after DynamicBoneEditor
  writes; ABMX bust gravity; the per-coordinate data of coordinate cards.

## Verification (CardImportTest, -nographics, on the reference install)

- Female 886 files (849 PNG cards + 10 autosaves loaded; 27 coordinate cards rejected): 0 exceptions, 0 extension errors.
  - Extra slots: 3 cards, 57 used, 55 loaded. The other 2 are missing mods.
  - ABMX: 440 cards, 8,882 modifiers, 8,860 applied. The remaining 22 have unknown bones on 18 cards, all of them accessory
    bones.
  - Uncensor selection set on 286 cards (kept as data).
  - DynamicBoneEditor: 1 card with 6 entries, 4 matched. The other 2 name bones of an accessory that is no longer in that
    slot.
  - InvisibleBody: 5 cards.
  - Push-up: 132 cards, stored.
  - Card KSOX on 32 cards, 35 layers (BodyOver 21, FaceOver 13, BodyUnder 1). Grown render textures: 5 faces to 4096², 1 body
    to 6144² (recorded only without a graphics device).
  - KCOX: 9 cards with entries, 8 with images, 13 entries. 10 applied (9 overrides, 9 images drawn, 1 cleared
    `[nil, true]`, 2 render textures grown). Skipped: 2 slots without clothes, 1 item without a colour render texture.
  - MaterialEditor: 158 cards with edits, 1,413 items, 579 applied. Skipped: property not on the shader 409 (mostly
    properties of Sideloader shaders that were not swapped in, for example `_VeinsOpacity`, `_SubsurfaceAlbedo`,
    `_ThicknessMap`, `_WetBump`), shader without a fitted replacement 162 (Hanmen/Next-Gen Body 69, Hanmen/Next-Gen Face 68,
    then a tail of 14 names), object missing 139 (slot empty or mod missing), material not found 121 (the item names another
    item's or another head's material, for example `c_m_eye_01` on head 00), renderer not found 3. Shader swaps 10, all to
    loaded HS2 originals.
  - MaterialEditor textures: 47 decoded on 25 cards (49 MB of image files, 73 assignments). Only images whose edit has a
    target are decoded, so the other 137 of the 184 on the cards never are.
  - Peak of decoded images alive: 533 MB, on one card with several 4096² textures. 0 alive after the run.
- Male 98 files (96 PNG cards): 0 exceptions.
  - ABMX: 34 cards, 105 modifiers, all applied (with the female cards: 474 cards and 8,987 modifiers).
  - Additional Accessory Controls: 1 card, 4 hair rules, 4 hair objects hidden.
  - InvisibleBody: 2 cards.
  - Push-up: 2 cards, stored.
  - MaterialEditor: 6 cards, 17 items, 0 applied. 16 name materials of heads that are not installed or of an accessory that
    is not in the slot; 1 object missing. No KSOX / KCOX on male cards.
- Rendering (`CharaRenderTest -urp -diff`, `-notex` = plugin data without the texture stage): the 3 default female cards carry
  no texture-stage data. Their Built-in renders are byte-identical with and without the stage; URP renders differ only by
  run-to-run noise (at most 8/255 on 0.00 % of the pixels, also seen between two identical runs). On a KSOX + KCOX card, a
  KCOX card and a MaterialEditor card the stage changes 0.5-33 % of the pixels per view, in both pipelines alike.

# HS2Next.Data.Ext

Decoders for the plugin extension data that HS2 character cards and coordinate cards carry (the ExtensibleSaveFormat block
`KKEx`). This layer only decodes. Applying the data to an assembled character is done by `Runtime/Chara/Ext` (accessory
slots 20 and above, ABMX, InvisibleBody, Additional Accessory Controls, DynamicBoneEditor, KSOX and KCOX overlays,
MaterialEditor; push-up data is stored only).

## Entry points

- `Card.Load(path)` (in `HS2Next.Data`) reads a character card. It keeps the raw KKEx block (`KKExRaw`) and its block version
  (`KKExVersion`). It also reads maker autosaves, which have no PNG thumbnail.
- `CoordCard.Load(path)` reads a coordinate card: the header, `Clothes` and `Accessory` as MessagePack object trees, the KKEx
  trailer (`KKEx`, `KKExRaw`) and `Resolve`.
- `CardExtensions.Decode(card)` and `CardExtensions.Decode(coord)` return a `CardExtData`. A field is null when the plugin is
  absent or its PluginData is nil.

| `CardExtData` field | Plugin data id | Returns |
| --- | --- | --- |
| `Resolve` | `com.bepis.sideloader.universalautoresolver` (legacy id also read) | `Card.ResolveInfo` list. `AccessorySlot`, `TryParseAccessorySlot` and `AccessoryProperty` handle `[outfit.]accessory<N>.ChaFileAccessory.PartsInfo.id` for every N, slots 20 and above included. |
| `MoreAccessories` | `moreAccessories` | `Parts[i]` = slot 20 + i, empty slots included. Type, id, parent key, `AddMove[2,3]`, 4 colours with gloss / metallic / smoothness, hideCategory / hideTiming (as read), noShake, optional `Show`. Missing attributes take the plugin's defaults. |
| `Abmx` | `KKABMPlugin.ABMData` | `Modifiers`: bone name, location (0 Unknown, 1 BodyTop, 10..29 accessory), and `CoordinateModifiers` (HS2 uses `Data` = element 0): scale, length, position, rotation in Euler degrees. Only version 2 is accepted. |
| `Uncensor` | `com.deathweasel.bepinex.uncensorselector` | Body, penis and balls GUIDs (a blank GUID is null), the display flags, and the version-1 `LegacyUncensorGUID`. Kept as data; no module applies it. |
| `MaterialEditor` | `com.deathweasel.bepinex.materialeditor` | All 11 edit lists as typed items (`ObjectType`, `Slot`, names, values) and `Textures` (id → raw PNG / JPEG bytes, not decoded). `HasEdits` is false for the old "every list nil" entries. `AllItems()` lists the items in the order they are applied. |
| `SkinOverlays` | `KSOX` | `Textures` maps `SkinTexType` to PNG bytes; version 1 `EyeUnder` / `EyeOver` are expanded to L and R. Also version 2 `TextureIds` and the Studio switches. |
| `ClothesOverlays` | `KCOX` | `Overlays` maps a clothes id to `ClothesTexData` (PNG or null, Override, BlendingMode). Empty entries are dropped and `[nil, true]` entries are kept. Also `TextureSizeOverride` and `EnableInStudio`. The character and coordinate map shapes are both accepted. |
| `PushUp` | `mikke.pushUpAI` | The `Bra` and `Top` sets, with the plugin's defaults for missing keys. `OtherKeys` holds the advanced keys that the plugin does not read. `IsDefault` is provided. |
| `DynamicBoneEditor` | `com.deathweasel.bepinex.dynamicboneeditor` | Entries: slot, bone root name, and nullable overrides with their `*Original` values. |
| `InvisibleBody` | `KK_InvisibleBody` | `Visible` (default true). |
| `AdditionalAccessoryControls` | `orange.spork.additionalaccessorycontrolsplugin` | Per-slot data (rules, character-accessory flag, advanced parent), the suppress and override rule lists, and both raw blobs. The character and coordinate key names are both handled. |
| `Opaque` | every other id | `OpaqueExt`: GUID, version, `Raw` (the exact PluginData bytes `[version, data]` copied from the card; the importer writes them back verbatim as `ext/<GUID>/data.msgpack`) and the decoded `Data`. A native decoder that declines the data (unsupported version, decode error) also lands here, with a `Reason`. |

`CardExtData` also has:

- `Versions` (id → PluginData version) and `NilIds`.
- `Diagnostics`: unknown versions, decode errors, missing texture ids, non-bundled textures.
- `Counts` (for example `nested.<id>.Lz4Block`).
- `RawPluginData(id)`, a verbatim byte span for any id, native or opaque.

## Not decoded here

- Image bytes (MaterialEditor textures, KSOX / KCOX PNGs) stay as file bytes. MaterialEditor `LOCAL_` / `DEDUPED_` texture
  keys and the non-bundled overlay modes are reported but not resolved. None occur on the test cards.
- These plugins stay opaque: EyeControl, AdvIK, OutfitPainter, EarWiggle, Colliders, PregnancyPlus, Deformers,
  BoobSettings, BeaverAI, AuthorData and the rare ids. Their data is kept and written back unchanged.

## MessagePack and LZ4

- `HS2Next.IO.MsgPack` returns ext values as `MsgPackExt` (type code + payload). `MsgPack.Skip` steps over one value
  without building objects, which is how the span of each PluginData is found.
- Nested documents in `bin` values go through `NestedMsgPack.Read`. It accepts three forms, and every reader must accept both
  plain and LZ4 for the same key, because MessagePack-CSharp stores documents under 64 bytes uncompressed:
  - plain MessagePack;
  - `Lz4Block`: ext 99 with an int (uncompressed length) and one raw LZ4 block. All ABMX blobs on the test cards use it;
  - `Lz4BlockArray`: ext 98, the MessagePack-CSharp version 2 form. Supported, but not seen on the cards.
- `Lz4.DecodeBlock` is a small decoder of the public LZ4 block format (no frame). It checks bounds and handles overlapping
  matches.

## Test

`-executeMethod CardExtTest.Run [-cards <dir>] [-coords <dir>] [-limit N]` decodes every card and coordinate of the install,
compares the counts per plugin and field with the census of the reference install and checks worked examples. It writes
`Spike/out/content/card_ext.txt`.

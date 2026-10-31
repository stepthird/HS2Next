# Changelog

HS2Next follows semantic versioning: the save format, the plugin API and the bridge commands not marked experimental
keep working within a major version ([CONTRIBUTING.md](CONTRIBUTING.md), "Compatibility").

## Unreleased

## 1.0.0

The first public version. Release date: set when the release is published.

### Reading what you own

- Reads the `abdata` folder and the zipmods of a Honey Select 2 install, plus extra mods folders, in place. Zipmods load as
  Sideloader loads them: one version per GUID, the load order, the asset-by-asset replacement between mods and the game,
  loose PNG textures inside zipmods.
- Every part reference is the pair (source GUID, number), so mods never collide by renumbering.
- Mod textures and meshes that would crash the graphics driver (a missing mip chain, block-compressed sizes that are not
  multiples of 4, textures without data, truncated meshes) are found from the bundle headers before the first load and
  repaired in memory; the repaired bundle is
  cached once. A bundle that made the program crash while it loaded is skipped at the next start, with a finding, until
  the file changes.
- Imports HS2 character cards (female and male) and coordinate cards with the plugin data they carry: extra accessory slots,
  ABMX bone modifiers, MaterialEditor edits, KSOX skin and KCOX clothes overlays, Additional Accessory Controls,
  DynamicBoneEditor, InvisibleBody; other plugin data is kept unread in the saved file. Imports HS2 pose files (`.dat` and
  the PNG pose format; FK exactly, IK approximately).

### The Studio

- Scenes with characters, Studio items (vanilla and mod, including the character accessories offered as items), maps,
  lights, cameras, the character light, folders and routes.
- Animation playback of every Studio animation row with speed, pattern, forced loop, time, option items and the 21 hand
  shapes.
- FK and IK of HS2Next's own: the Studio's 13 IK targets in 5 groups, FK and IK usable together (the Studio's exclusive
  behaviour is an option), copying and baking between them.
- Neck and eye look, expressions, blinking, joint correction, dynamic bones and bust physics, implemented from measurements
  of the game.
- Screen effects: colour grading with the Studio's look-up tables, ambient occlusion, bloom, vignette, depth of field,
  sun shafts, height fog, sky and ambient light; screen-space reflections as an option. Rendering is URP: the character,
  item and map shaders of the game and of the common shader mods have replacements fitted to the original's output.
- Screenshots at up to 4 times the window size, with background and frame images; a global pause and single steps.
- Undo and redo for every scene change.

### The character maker

- The face, body, hair, clothes and accessory pages of the game's maker, as tabs of the inspector, applied to the character
  in the scene as you edit.
- Character and coordinate files of HS2Next with a new thumbnail; loading with the HS2 maker's choice of what to load.

### Files

- Save format version 1: scenes (`.hs2nscene`), characters (`.hs2nchara`), coordinates (`.hs2ncoord`) and poses
  (`.hs2npose`). Each is a ZIP behind a PNG thumbnail, so it shows as an image and opens as a ZIP; the contents are JSON.
  Unknown members and ext blocks are kept when a file is saved again.

### The program

- A new UI (UI Toolkit): scene tree, inspector, lists, toolbar, gizmos, a shortcut list; the HS2 Studio key layout as a
  preset. English, Japanese and Simplified Chinese (machine-translated) with the Noto Sans CJK fonts.
- A first-run guide for the language and the folders; File > Options for the common settings; a per-user data folder or a
  portable mode.
- An automation bridge: 147 HTTP/JSON commands on 127.0.0.1:17322, a token in the release build
  ([Reference/COMMANDS.md](Reference/COMMANDS.md)).
- Plugins: DLLs with a manifest in the `Plugins` folder, the plugin API 1.0 and a sample sequencer
  ([Reference/PLUGINS.md](Reference/PLUGINS.md)).

### Known limits of 1.0

- No HS2 scene files, no story, no H scenes, no VR; BepInEx plugins do not run.
- The skin and hair shaders of the Hanmen Next-Gen family that MaterialEditor assigns are not fitted: such characters show
  the game's own skin, with a finding.
- Post-processing volumes that mod maps bring for the old post-processing stack are not applied.
- Some rare mod shaders are drawn with an approximation or the URP default.
- The Simplified Chinese strings are machine translations that no speaker has reviewed yet.

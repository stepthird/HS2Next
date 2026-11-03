# Changelog

HS2Next follows semantic versioning: the save format, the plugin API and the bridge commands not marked experimental
keep working within a major version ([CONTRIBUTING.md](CONTRIBUTING.md), "Compatibility").

## 1.2.0

Released on 2026-11-03 (UTC).

### Additions

- Favourites and recently added: the character, item, animation, map, light and pose lists have «★ Favourites» and
  «Recently added» at the top of their folders or groups. Right-click a card or row (or press the menu key) and choose
  Favourite to mark it; favourites show a star. Recently added lists files newest first by the date they were placed in
  their folder; in the item, animation, map and light lists it shows only rows from mods, newest zipmod first. The outfit
  lists have favourites too.
- A right-click menu on the character, outfit, pose, item, animation, map and light lists: Favourite, Details… (for files
  the name, character name, kind, path, size, dates, the plugin data and the mods a card needs that are not installed; for
  mod rows the zipmod's GUID, name, version, author and website and the bundle), Show in Explorer, Copy path / Copy ID, and
  for character, outfit and pose files Delete…, which moves the file to the Windows recycle bin after a confirmation and
  never deletes it for good (HS2's own files too, with a note that they leave HS2 as well). 1.1.0 deleted only HS2Next
  pose files.
- Pose browser: poses without a picture of their own (most HS2 pose files) show a stick figure drawn from the pose, with
  the left and right limbs in two colours and the face's direction marked, instead of a tile with the name. PoseTools
  pictures and HS2Next pose thumbnails stay as they were. A figure is drawn the first time its cell shows and kept in the
  app's cache.
- Field of view: the Cameras tab has a Field of view row (1 to 60, reset to 23) for the current view, the free camera or
  the camera you look through; the readout at the top right of the view opens the same slider on a click, changes the
  value with the mouse wheel, resets it on a double click and names the keys in its tooltip; the Camera menu has Widen /
  Narrow / Reset the field of view, and Ctrl+Home resets it in the default key map. 1.1.0 changed it only with Ctrl+wheel
  or the HS2 Studio keys.
- A per-character switch in the State tab, «Nipple shape through clothes», on by default: off, the nipples stay flat while
  a top or a bra is on (bone modifiers on them included); the body's own shape returns when the top and the bra are
  half-off or off. It is saved in the scene; 1.1.0 opens such scenes with a note that they are newer and keeps the switch.
- File > Options > Advanced lists the plugins whose data in mod packages HS2Next reads since 1.1.0 (BonesFramework,
  HS2_CopyChaBodyMat, HeadBundleRedirect, Heelz, HooahGameExtension, MdgqBodyChange, ModBoneImplantor), and its paragraph
  covers the data that plugins keep in mod packages.

### Fixes

- Arms, legs, hands, stockings and skirts no longer show thin streaks along the model's edges that moved with the view;
  they now shade as in the game. 1.1.0 used the game's shadow bias numbers for the chara light, which are far too small
  in HS2Next's renderer, so the light shadowed the character's own surface.
- A character added, changed, reshaped, posed, moved or loaded while the scene is paused (or while its own animation is
  paused or at speed 0) shows its breasts, hips, hair and clothes at rest at once, and the animation stays on its frame.
  1.1.0 showed the breasts and hips stretched toward the scene's origin, or left behind after a move, until Resume.
- A character that appears away from the scene's origin no longer swings its breasts and hips in from the origin during
  its first second (1.1.0: about 11 cm on the first frame at a typical placement).
- The nipple stand («乳首») in the State tab keeps its effect while an animation plays; 1.1.0 lost it at the next frame.
- The Costume tab's «読込» keeps the outfit list's order, chosen card and scroll; 1.1.0 showed the list again in name order
  at the top. The outfit and character load dialogs keep their sort, search and scroll when opened again.
- Texts stay inside narrow panels: the Cameras tab's "No cameras yet …" text and status line, the inspector's notes, the
  character and pose lists' count and status lines wrap, and list column titles are cut with … (the whole title in a
  tooltip; the pose list's titles also follow the UI language now). 1.1.0 let them run out of the sidebar.

## 1.1.0

Released on 2026-11-02 (UTC).

### Additions

- Update check: at start the distributed app asks GitHub once whether a newer release exists (nothing about you, your files
  or your characters is sent) and, when one does, offers to open its download page in the web browser, to skip that version
  or to ask later. Help > Check for updates asks at any time; File > Options > General turns the check at start off. The
  app never downloads or installs anything by itself.

### Fixes

- Skinned accessories (mod accessories with the HooahComponents `SkinnedAccessory` component, such as fingernails, arm
  pieces and bodysuits sold as accessories) are drawn on the character's own skeleton, as in the game. 1.0.0 drew them
  away from the body.
- Clothes and accessories with HS2_CopyChaBodyMat's `ChaCopyBodyMaterial` component (skin pressed out by tight clothes,
  such as GDC's tight series and roy12's Skindentations) draw those skin pieces with the character's own skin, as in the
  game. 1.0.0 drew them grey-white.
- Mod heads that bring their own face compositing materials (HeadBundleRedirect's `Head_Bundle_Redirect` in the zipmod
  manifest, as many of cunihinx's heads do) lay out the eyeshadow, cheek and lip layers and the face paint mask with those
  materials, as in the game. 1.0.0 used the game's layouts on every head.
- Clothes with MdgqBodyChange's `BodyCut` component (squeeze stockings and a few tops) hide the body while their cut body is
  shown, and the cut body draws the character's skin, as in the game. 1.0.0 drew the body and the cut body on top of each
  other.
- Clothes with ModBoneImplantor's `BoneImplantProcess` component keep the extra bones they bring (moved under the body
  skeleton), as in the game. 1.0.0 lost those bones with the part's own skeleton copy.
- Heel shoes with Heelz data (`AI_HeelsData` in the zipmod manifest) lift the body and pose the ankles, feet and toes as the
  plugin does in the Studio. 1.0.0 left the feet flat and sunk into the floor.
- Heads, hair and clothes that bring extra bones for BonesFramework (an `additional_bones` list in their bundle, as roy12's
  heads and some skirts, sleeves and coats have) keep those bones, moved under the character's skeleton, as in the game.
  1.0.0 lost them with the part's own skeleton copy, so such heads were drawn with a stretched, broken face.

## 1.0.0

The first public version, released on 2026-10-31 (UTC).

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

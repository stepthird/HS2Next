# Changelog

HS2Next follows semantic versioning: the save format, the plugin API and the bridge commands not marked experimental
keep working within a major version ([CONTRIBUTING.md](CONTRIBUTING.md), "Compatibility").

## 1.4.0

Released on 2026-11-09 (UTC).

### Additions

- Wet and stains. A new panel in the View menu adds stains to the selected character, picks where they start on the body,
  runs, dries or keeps them as layers, and tunes their shape, look, gloss, drips, puddles and floor. The character's State
  tab lists its stains and opens the panel.
  - Stains spread from an origin over time on clothes, skin, face and hair, and look the same after a save and a load.
    Water and blood can lie on one character as layers.
  - Accessories, shoes and gloves can take stains too: each has a switch beside those of skin and hair, off by default.
    An accessory that is not skinned shows the stain's colour without the glossy film.
  - A stain follows the scene clock: the pause stops it, a step steps it, a recording records it.
  - Stains show without changing any material: removing a material edit under a stain shows the edit's original image with
    the stain on top.
  - Wet clothes get a glossy film that follows the garment and its transparency.
  - Drops fall from wet parts and gather into puddles on the map's floor, with the scene reflected in them. Puddles mirror
    the scene in the view, in captures and in recordings, under the same rendering rules as the view.
  - Stains survive part swaps, coordinates and rebuilds: a part that changed stays dry until the stain is applied again,
    and the stain comes back when the part returns.
- Ragdoll. A new panel in the View menu and a section in the character's State tab make a character a physics body,
  release it, bake its pose to FK, and set its presets, pins, ties, holds, room, floor and every value.
  - A body falls, follows its animation, can get up by itself and goes back to its pose; a release can blend back to the
    animation. Parts can be pinned in place or to a moving object, tied to each other and held; a follower folder moves
    with a part.
  - The panel's tool is one of Off, Grab and Shoot. In the view, Grab takes a body or a prop with the pointer: the wheel
    changes the depth, Space pins, T pins to the selected object, N makes a follower, Ctrl pushes and Shift throws balls,
    boxes and heavy balls. Shoot fires from the camera; hold to repeat. Outlines of the colliders and the room show in
    the view while the panel is open, with the tool on or off, and never in a capture.
  - A body lands on the map's own surface on every map, also one whose meshes cannot be read; the floor can also come
    from the character's feet or a fixed height. A Studio item can act as an obstacle that bodies land on and that
    follows the item. The map's walls do not stop a body; the room does.
  - Bodies follow the scene clock: the pause stops them, a step steps them, a recording records them. The same commands
    at the same steps, in a fresh physics world, give the same motion.
  - Thrown props can be grabbed, last 20 seconds and are not saved; a hard hit knocks a body down.
  - Shots pass through bodies and props, weaken what they hit, can knock a body down, and with `kill` leave the first
    body limp. Blood droplets and splats, tracers, muzzle flashes and dust show in captures, recordings and puddle
    mirrors, stop at the pause and are not saved. A shot can leave a stain where it hits a character, at most one in
    half a second and four per character. «Show blood» in the options and in the panel turns blood off; it is not saved
    in the scene.
  - Hair, skirts and other swinging parts restart cleanly when a body jumps back to its pose.
- Chara to Item. A new panel in the View menu bakes the selected characters, whole or by parts, into Studio items with
  FK; a section in the character's State tab bakes the whole character or opens the panel on it.
  - A bake is a self-contained copy of the character as drawn: meshes, materials, textures, FK bones, joint correction
    and inside faces for hollow parts. A scene with bakes opens on a machine without the character's mods. Bakes of the
    same character, and a cut's pieces, share their textures in video memory.
  - Once the character is captured, the bake's data is written in the background while the view keeps its frame rate.
  - Presets (Body, Hair, Clothes, Accessories), one item per part, the FK groups, filling hollow parts, the origin at the
    parts' centre, hiding the character and keeping the bake in the store are options.
  - Bakes are kept in a store in the user data folder (`bakes/`) and listed in the Studio item list under «Chara to
    Item». The panel's store list adds, renames in place, deletes after asking, restores, purges through a dialog that
    shows what goes, and marks a damaged row. Deleting hides a bake; «Purge» removes only data that no remaining bake
    uses; a saved scene never depends on the store.
  - «Import old bakes…» reads the bakes of the earlier tool (`.c2i` and its texture files) from `UserData/CharaToItem`,
    rebuilds each with this install's content and stores it under its old number. The old folder is only read, and
    importing again adds nothing.
  - The item inspector shows a bake's kind, source and size, and keeps it in the store.
- Cutter. A new panel in the View menu cuts a character, an item or a bake with a plane, a box, a tear or a hole marker,
  in one undo step.
  - Both sides of a plane or box become items; a tear or hole opens the surface. «Clothes only» cuts only garments.
  - Cut edges can be ragged (amplitude, tooth size, randomness, pattern number); both pieces of a ragged cut fit each
    other. Cut faces show a flat section colour, or the garment's or hair's own fabric inside.
  - A piece keeps only the FK bones that move what is left in it and is placed at the cut. It shows the pose it was cut
    in; posing it and cutting it again never moves its earlier cuts.
  - Cuts stay editable: a piece remembers how it was made and can be cut again, changed («Apply to piece») or made again
    from its source; a scene loads pieces exactly as saved.
  - Markers are added from the panel and placed with the gizmo; «Place on surface» puts a tear or hole marker on the
    surface under the pointer and the wheel turns it. Markers show in the view and never in a screenshot, capture,
    recording or thumbnail.
- Scene scripts. A new panel in the View menu («Scene scripts») keeps reusable shots: roles cast to the scene's characters
  and props, set-up steps sent at every play, preparation steps sent once, and a Timeline with timed commands.
  - Play casts the roles (from the saved cast, a character of the same name, or the role's card when it loads), loads the
    map and the missing characters and props, prepares once per cast in one undo step, sends the set-up and plays the
    Timeline on the scene clock. A failed step is listed under «Last play» and the play goes on.
  - Playing a script loads its Timeline into the scene's Timeline. The Timeline panel names the script, marks it
    «edited» after a change and offers «Save edits into the script».
  - «Record…» plays one pass at a fixed step and writes every frame.
  - Stop releases the script's camera follow, moves and depth of field; it also releases the cast's ragdolls and dries
    their stains.
  - Scripts are kept in a store in the user data folder (`scripts/`) and exported to or imported from `.hs2nscript`
    files. Deleting hides a script; «Purge» removes only the files of deleted scripts, decided from the store's index,
    and removes nothing when the index cannot be read.
  - Importing a scene brings in its scene scripts, cast to the imported characters and items.
- Scene files can also hold items made from bakes (their meshes, materials and textures inside the scene file) and cut
  pieces with how they were cut (format revision 5). A scene without them is saved exactly as before; 1.3 opens one
  with them with its "newer format" notice, shows those items as missing and keeps them when it saves.
- Scene files can hold a character's stains and puddles (format revision 5). A scene without them is saved exactly as
  before. 1.3 opens a scene with stains with its "newer format" notice, shows the character dry and keeps the stains and
  their pictures when it saves.
- Scene files can also hold a character's ragdoll settings, pins, ties, holds and last settled pose, and the scene's
  ragdoll values, room, floor, pushers and obstacles (format revision 5). A body in motion is not saved: loading shows the
  last pose at rest. A scene without ragdoll data is saved exactly as before; 1.3 opens one with it with its "newer
  format" notice, shows the character without a body and keeps the data when it saves.
- Scene files can also hold scene scripts and the script the Timeline was loaded from (format revision 5). A scene
  without scripts is saved exactly as before; 1.3 opens one with them with its "newer format" notice, plays the loaded
  script as a plain Timeline and keeps the scripts when it saves.
- While a viewport tool runs, the status line says what it asks for and Esc leaves it. The camera, shortcuts and gizmos
  are back as they were when it ends.
- New bridge commands, all experimental.
  - `wet.attach`, `wet.stain`, `wet.use` and `wet.status` work with the names and arguments scene scripts already use.
  - `wet.start`, `wet.dry`, `wet.set`, `wet.keep`, `wet.remove` and `wet.floor` drive stains from a script. Timed commands
    of a Timeline can start, dry and change stains without touching the undo history.
  - `ground.probe` answers the height of the map's surface under a list of points.
  - `ragdoll.build`, `.lock`, `.tie`, `.pin`, `.unpin`, `.follower`, `.turn`, `.settle`, `.pusher`, `.parts`, `.push`,
    `.release` and `fx.shoot` work with the names and arguments scene scripts already use. `ragdoll.release` with no key
    ends every body, prop, pusher and effect, so a Timeline loop that starts with it repeats a run exactly.
  - `ragdoll.status`, `.set`, `.knock`, `.throw`, `.bake` and `.obstacle` drive and read ragdolls from a script.
    `ragdoll.bake` writes a body's pose to FK and releases it in one undo step; it turns IK off for that character.
    Timed commands of a Timeline can build, push and shoot without touching the undo history.
  - `bake.parts`, `.make`, `.list`, `.rename`, `.delete`, `.purge`, `.keep`, `.status` and `.import` bake characters or
    their parts into items, keep bakes in a store and import the old bakes; `cut.marker`, `.make`, `.set` and `.status`
    cut characters and items into pieces and change a piece's cut later. Baking, cutting and changing a cut are single
    undo steps; renaming a bake renames the items that still carry its old name in one step.
  - `script.list`, `.get`, `.set`, `.remove`, `.cast`, `.play`, `.stop`, `.status`, `.capture`, `.borrow`, `.keep`,
    `.add`, `.import`, `.export`, `.delete` and `.purge` make, store and play scene scripts. `script.set` and
    `script.import` take a Timeline in the form `timeline.build` takes, and warn about steps the app does not know,
    steps that answer later and set-up steps that clear the undo history. A step or timed command a script may not hold
    is refused, and the error names its place in the script.
  - `obj.moveTo`, `camera.follow`, `dof` and `chara.accessoryAxis` work with the names and arguments scene scripts
    already use. `fx.shoot`, `wet.attach` and `wet.stain` accept `from: {key, accessory}`, which starts at that
    accessory's muzzle. `fk.enable` accepts `RightArm`, `LeftArm`, `RightLeg` and `LeftLeg`; each switches the Body
    group, with a warning.
  - `timeline.set` with a whole Timeline (for example one read with `timeline.get`) makes it a plain Timeline that no
    longer counts as loaded from a scene script.
- Plugin API 1.3.0; plugins built for earlier versions load unchanged. The plugin guide describes the additions in the
  three languages.
  - Viewport tools (experimental): a plugin's tool takes the viewport's pointer, wheel and keys while it runs, and picks a
    surface with its point, normal, part, UV and bone.
  - The map query (experimental): a plugin can ask the height and normal of the map's surface under a point, also on maps
    with no readable mesh or colliders.
  - Motion stages (experimental): a plugin can add a motion stage to a scene character. The stage comes back after every
    rebuild of the character, shows in `chara.motion` with the plugin as owner, and goes when the plugin leaves.
  - Item rows (experimental): a plugin can add its own groups of rows to the Studio item list and build each row's
    object. Item FK moves the bones the row names, and an item of such a row saves like any item; without the plugin it
    loads as the fallback item and keeps its data. The plugin kit has a third sample, Shapes: five shapes built in code,
    among them a torus that item FK bends.

### Changes

- Playing a Timeline from the start now restarts the animations its tracks set and resets the hair, clothes and breast
  physics of its characters, so a replay or a play after loading the scene moves the same as the first play.
- With the automation bridge disabled (`bridge.enabled` false or `-noBridge`), the app's commands still run inside the
  app: timed commands of a Timeline, scene scripts and plugins' `Call` and `Request` work as with it enabled. Only the
  HTTP listener, its `endpoint.json` and its token file are absent.

### Fixes

- Screenshots, captures, recorded frames and thumbnails show a moving character in the pose of the frame they were
  taken in: they drew its skin one frame late. A character's card thumbnail in the «Front» framing shows the default
  pose with its own skin; it showed the skin of the pose before.

## 1.3.1

Released on 2026-11-06 (UTC).

### Additions

- Favourites on the clothes and accessory lists of the Maker pages. Right-click a cell, or press the menu key or Shift+F10,
  and choose Favourite: the cell shows a star. The «★» button beside the search field shows the favourites only, together
  with the search and the base game or mods filter. An accessory list keeps the favourites of its type.

## 1.3.0

Released on 2026-11-06 (UTC).

### Additions

- A PluginKit folder in the distribution zip for plugin authors: the plugin guide in English, Japanese and Simplified
  Chinese, the command reference, the XML documentation of the plugin API (also next to the API DLLs, for IDEs) and two
  sample projects, Hello and HelloUi, that build with one `dotnet build` (.NET SDK 8 or later, no network).
- Plugins can be turned off and on while HS2Next runs: the plugin switches in File > Options > Automation act at once, and
  the bridge has `plugin.disable` and `plugin.enable`. A plugin whose DLL was replaced loads after a restart.
- Plugin API 1.1.0; plugins built for 1.0 load unchanged.
  - `ctx.Request` and `ctx.RequestAsync` call a command with a name and an argument object and return a typed reply
    (`ICommandReply`).
  - Capture events `CaptureStarting`, `CaptureRendered` and `CaptureFinished` for screenshots and the `capture` command, so
    a plugin can hide its own overlays during a capture.
  - Every public type and member of the API is documented.
- Plugin API, experimental (these may still change before they are marked stable):
  - Declarative panels and inspector sections (`ctx.Forms`, namespace `HS2Next.Api.Ui`): a plugin lists rows (numbers,
    toggles, colours, vectors, choices, references, files, text, buttons, attach points, the Maker lists and push grids)
    and two of its commands; HS2Next draws them with its own widgets, and each change is one undo step.
  - Plugin UI can use HS2Next's own widgets, dialogs, menus and toasts (`IPluginUi.Widgets`).
  - A plugin can put changes of its own state into the scene's undo history (`ctx.History`).
  - A BodyMesh build hook can add skin layers (PNG or JPEG bytes, or a texture) over or under the body and face skin, and
    replace body parts in place (mesh, bones by name, materials, normals), all in the character's one build. Outside
    builds, `SkinLayers(node)` changes a character's skin layers without a rebuild, and `RequestRebuild(node)` builds a
    character again so that its hooks run again.
- `plugin.list` also reports each plugin's number of skin layers, supplied body parts and undo entries, and why its last
  enable failed.
- The READMEs have a Plugins section: where plugins go, that they run with the app's rights, turning them off and on in
  File > Options > Automation, and the PluginKit folder.
- Timeline (View > Timeline panel, or the ◆ button on the toolbar): a scene can hold a Timeline of keyed tracks - node
  position, rotation and scale, character FK bones, animation (row, speed and time), eyes and mouth, item and light
  colours, the camera and the depth of field - plus timed commands. The Timeline is saved with the scene and plays on the
  scene clock, so the global pause holds it. Playing shows the values without changing the scene; "Stop and keep this
  pose" writes the shown pose into the scene as one undo step.
  - The panel has play, pause and stop, scrubbing, keys drawn by their ease, a key editor and an add-track menu for the
    selection. Timeline edits are undoable like other scene edits (a key drag is one step).
  - Keys ease as step, linear, ease in-out, ease in or ease out. Rotations take the short way round.
  - Inspector rows driven by the Timeline or a plugin show a mark. The camera and the gizmos follow the Timeline while it
    plays.
- Frame sequence export (Export frames… in the Timeline panel, or `record.start`, `record.stop` and `record.status`): the
  view is recorded at a fixed time step (1 / fps) as numbered PNG files, with a `record.json` that lists the scene time of
  every frame. "One pass of the Timeline" (on when the scene has a Timeline) plays the Timeline once from the start while
  it records. No frame is dropped, and frames of the global pause are not written. To make a video, use an external tool
  such as ffmpeg (not included). Recording also works with the bridge turned off.
- Bridge commands for the Timeline: `timeline.build`, `timeline.events`, `timeline.play`, `timeline.pause`, `timeline.stop`,
  `timeline.seek`, `timeline.clear` and `timeline.status`, and the editing commands `timeline.get`, `timeline.set`,
  `timeline.track.add`, `timeline.track.set`, `timeline.track.remove`, `timeline.key.set`, `timeline.key.delete` and
  `timeline.key.move`. `COMMANDS.md` lists 169 commands.
- Plugin API 1.2.0; plugins built for 1.0 and 1.1 load unchanged.
  - Capture events also fire for every recorded frame, with the new kind `CaptureKind.Record`.
  - Experimental: runtime overrides (`ctx.Overrides`). A plugin lays values over the scene for display - node position,
    rotation and scale, FK bone rotations, animation row, speed and time, face patterns and openness, item and light
    colours, the camera and the depth of field - without changing the scene. Nothing is saved or put in the undo history,
    and the values go away when the plugin releases them, is turned off, or a scene is loaded. The new command
    `overrides.list` (experimental) lists the override slots with their owners and which one is shown.
  - The plugin guide has a new chapter on runtime overrides.
- Advanced pose: a new tab of the character inspector, for the first selected character.
  - Bones: a searchable tree of the bones of the body, head, hair, clothes and accessories, with position, rotation and
    scale corrections through the rows or the Move, Rotate and Scale tools. Pick in view selects a bone in the viewport.
    Corrections are stored in the scene, added on top of the animation, FK and IK, and keep working after a character
    change or a part swap.
  - Jiggle: per-scene values for each hair, clothes and accessory chain and for the breast and hip chains. A row shows the
    card's value until the scene holds its own; the card's values stay untouched and come back when a value is removed.
  - Blend shapes: a mesh's shapes as sliders. A weight holds over the expression through pattern changes and blinks until
    reset.
  - The tab lists what this character lacks (kept in the scene, no effect, active again when the bone or part returns) with
    a Remove button.
  - Parts that keep the mosaic in place are left alone: bone corrections and blend-shape weights on them are refused, and
    such entries in a scene file are kept but not applied.
  - New experimental bridge commands: `pose.bones`, `pose.chains`, `pose.bust` and `pose.blendShapes`, each with `.set` and
    `.clear`.
- Materials: a new tab of the character inspector edits the material values of the character, its hair, clothes and
  accessories: shader, render queue, keywords, numbers, colours, images with tiling and offset, and whether a renderer
  shows. A drag is one undo step.
  - Studio items and the map gain a Material edits section with the same rows, saved with the scene. Loading another map
    clears the edits made on the previous one, in the same undoable step.
  - The tab lists what a card's edits do not apply to and why, with a Remove button.
  - Most value changes of an edit on a character, hair, clothes or an accessory no longer reload the part or rebuild the
    character, and removing an edit shows what the material has without it. A change the shader mapping depends on still
    reloads the part: a render queue crossing 2450, 2500 or 3000, the first image on a main, normal, specular or metallic
    map, an emission colour leaving or reaching black, a cull value.
  - A render queue above 2500 means the same on items and the map as on a character: such a material takes no realtime
    shadows on the shaders with that switch, and fogs as its original did.
  - Material edits never apply to mosaic parts.
  - Images are taken from PNG or JPEG files, at most 8,192 pixels a side and 64 MiB, and stored in the file. An image whose
    header claims more than 16,384 pixels a side is refused before it is decoded (finding `me.textureTooLarge`).
  - New experimental bridge commands: `materials.shaders`; `chara.edit.materials.items`, `.set` and `.clear`;
    `item.materials.items`, `.set` and `.clear`; `map.materials`; `map.materials.items`, `.set` and `.clear`. Textures can be
    taken from PNG or JPEG files.
  - `item.materials` takes the optional argument `allProps`: every property the shader declares.
  - `item.set` refuses a path under `item.materialOverrides` ("use item.materials.set").
- Hair bone adjustments: a new section of the Hair page, stored with the character, with the new experimental bridge
  commands `chara.edit.boneMods`, `.set` and `.clear`. The Hair, Clothes and Accessories pages gain Edit materials.
- Constraints: a new panel (View > Constraints panel) lists the scene's constraints: a node, a bone or an IK target follows
  another node or bone by position, rotation, scale, look-at or as a child would, with an offset, a weight and a switch.
  - A Constraints section on items, lights, folders, cameras and routes, and at the end of the character's Kinematics tab,
    lists what touches that node. A bone is chosen from a searchable tree, or picked in the view.
  - While a scene has an active constraint its characters step in dependency order, so a chain across characters (a bone
    to a node to another character's IK target and on) shows no lag in any frame. A constraint whose source only moves
    after the character it steers, as a left hand on a rod held in the right, follows one frame behind, and the list says
    so.
  - A constraint on a bone acts inside the character's pose: twist bones, the eye look, physics and skinning follow it in
    the same frame, and a pose correction stays on top of it. A source bone of the same character is read before its pose
    corrections, and the row says so.
  - Rows driven by a constraint show the overridden mark and stay editable; a gizmo drag on a driven channel is refused
    with a toast, the other channels stay free.
  - Every edit is one undo step; a drag of a weight or an offset is one step, and Esc takes it back.
  - A constraint whose bone or node is gone stays in the list, says what it lacks and acts again when it returns. One that
    would close a loop is refused at the edit, and suspended, not dropped, when a later change closes it.
  - Duplicating nodes also copies the constraints between them; Import scene brings the file's constraints between the
    imported nodes.
  - Mosaic parts are not editable.
  - A constraint a file cannot use (a part it needs is missing, an unknown component, more than 256 in the list) is kept
    and written back, never dropped. Removing the last constraint gives the same file as one that never had any.
  - New experimental bridge commands: `constraint.list`, `.add`, `.set`, `.remove`, `.clear`, `.move` and `.status`.
- The scene post stage (experimental): a plugin that implements `IPluginScenePost` gets a call after all motion, once per
  frame and once per scene step; `plugin.list` shows its times as `scenePostMs`.
  - `BoneWritten` (experimental) reports a bone written after the character's motion step, so the next step takes that
    write off.
  - Runtime overrides gain `chara.bone["<address>"].pos`, `.rot`, `.scale` and `chara.ik.targets.<Name>.pos`, `.rot`
    (experimental); the constraints' slots have priority 10.
  - The plugin guide has a chapter on the scene post stage, in English, Japanese and Simplified Chinese.
- `bones.dump` and `chara.bones` are no longer experimental. Their names, arguments and replies do not change.
- Scene format revision 4: a scene with a Timeline, with advanced pose values, with material edits on an item or the map or
  with constraints is saved as revision 4. Every other scene is saved exactly as before. HS2Next 1.2.0 opens such a scene
  with its "newer format" notice, keeps the Timeline, the values, the edits and the constraints (an item and the map show
  without the edits there, and the targets without the constraints) and writes them back when it saves.

### Changes

- The language list and the first-run guide show Simplified Chinese without a machine-translation note: every language of
  the app is made the same way.
- `scene.step` answers busy while frames are recorded, and the toolbar's Step is disabled then. Screenshots and captures can
  still be taken during a recording.
- `camera.get` reports the view as shown. `camera.get`, `camera.set`, `camera.frame`, `camera.state` and `effects.state`
  name the owner of a runtime override (`overridden`); `plugin.list` lists a plugin's override slots, and
  `plugin.disable` counts the ones it released.
- Characters: a card's bone modifiers now also apply to the bones and channels an animation writes, in the order HS2 uses.
  Hands, feet, legs, arms and neck of many cards with such modifiers change size or length during play and in the saved
  scenes that use them, as they do in HS2 Studio. A modifier on a bone the animation also moves acts on top of the
  animation. Cards without bone modifiers do not change.
- Characters: a scale modifier on the mouth's width bone no longer grows each time the width changes. On such a card the
  modifier sets that bone's scale, and the mouth's width does not move with speech.
- Characters: under heel shoes a card's foot scale modifiers act on top of the heel's scale while IK is off.
- Esc during a drag now cancels it on every handle and value row: the value goes back, the history gets no entry, and
  moving on or releasing the mouse does nothing.
  - Maker: Esc while holding a step button of an accessory's transform stops it at once.
  - Field of view: Esc during a drag on the slider puts the value back, also on the free camera.
  - Timeline: Esc during a track row's drag keeps the order, and Esc during a value drag in the key editor no longer
    leaves an empty history entry.
- Maker: the inspector no longer keeps old Maker lists, costume browsers and the rest of a dropped inspector in memory after
  it is rebuilt, and lists of an Edit tab that is not shown no longer run every frame.
- Maker lists: the expand button is no longer cut off at the sidebar's right edge, also at its narrowest.
- Accessories page: adding, removing or copying an accessory to a new slot no longer rebuilds the inspector. The + slot and
  the copy target keep your choice, and the add panel keeps its list. Removing the selected accessory now selects the
  next worn slot, or the lowest when none follows.
- Loading an outfit on the Costume tab no longer rebuilds the inspector.
- State tab: the accessory and clothes rows follow an undo, an outfit load or an item change while the tab is shown, and an
  accessory's row names its current item.
- Bridge: `ui.action` returns the result of `edit.show` as an object instead of a type's name. The help of `select` lists
  every sub-object form it accepts.
- For plugin authors: a motion stage after phase 120 sees card modifiers on every bone they affect. A plugin's inspector
  section is no longer rebuilt when a character's accessory list changes. The documentation comment of `MotionPhase.Final`
  no longer names bone modifiers, which do not run at that phase; the enum does not change.
- Import scene brings the images of items along: panels, patterns and material edits. Before, only characters' images came
  with them.
- Tools: `unity_batch.ps1` stops its own Unity when the job's log has not changed for 30 minutes, or for 3 minutes once Unity
  has started shutting down (`-StallMin`, `-StallEndMin`; 0 turns the watcher off). It says why and exits 125. It never
  touches another Unity process.

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
  preset. English, Japanese and Simplified Chinese with the Noto Sans CJK fonts.
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

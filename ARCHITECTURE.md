# HS2Next architecture

This page describes how HS2Next is built: where the content comes from and how it is named, how a character is assembled
and moved, how it is drawn, how scenes are kept and saved, and the interfaces around the app (UI, automation bridge,
plugins). The folder READMEs under `Assets/HS2Next/Runtime/` and `Assets/HS2Next/Shaders/` go into the details of their
parts.

## The project at a glance

HS2Next is one Unity 6000.6.5f1 project (Mono scripting backend, linear colour space). The release is a Windows player
built from the scene `Assets/HS2Next/Scenes/App.unity`; that scene is empty, and the app starts itself in every player and
Editor play session (setting `app.autoStart`, switch `-noApp`).

| Assembly | Folder | What it holds |
| --- | --- | --- |
| `Assembly-CSharp` | `Assets/HS2Next/Runtime`, `Assets/Compat` | the app: content, characters, motion, rendering, scenes, save format, import, UI, Core commands; and the receiving stubs that bundles deserialize into |
| `HS2Next.Api` | `Assets/HS2Next/Api` | the stable, engine-free API: content ids, motion phases, the bridge command contract, the save ext-block contract, the plugin contract |
| `HS2Next.Api.Unity` | `Assets/HS2Next/ApiUnity` | the engine side of the plugin API: scene node objects, character build hooks, material overrides, UI registration (experimental) |
| `HS2Next.Bridge` | `Assets/HS2Next/Bridge` | the automation bridge: HTTP transport, safety checks, the main-thread queue, the bridge's own commands |
| `Assembly-CSharp-firstpass` | `Assets/Plugins/Compat` | receiving stubs whose original assemblies lived in the game's first-pass assembly |
| `Assembly-CSharp-Editor`, `HS2Next.Bridge.Tests` | `Assets/HS2Next/Editor`, `Assets/HS2Next/Bridge/Tests` | batch tests, measurement and fit tools, the player build |

The app code stays in `Assembly-CSharp` because the receiving stubs must live there (the bundles name that assembly), and an
assembly definition cannot reference the default assembly.

Other top-level folders: `Tools/` (Python and PowerShell tools: the test runner, the Unity runner, the offline compile
check, scanners and generators, the packaging), `Tests/` (fixtures, the sample and test plugins, the probe cases of the
shader fits in `Tests/FitCases/`), `Samples/` (the sample plugins: `Sequencer`, and `PluginKit` with Hello and HelloUi,
the plugin kit of the zip), `Config/` (`settings.example.json`), `Reference/` (the command reference and the plugin guide).

## Content

HS2Next ships no game data. Everything it shows is read at run time from **content sources**
(`Assets/HS2Next/Runtime/Content`):

- **The HS2 install**: its `abdata` folder (bundles, the asset-bundle manifest, the character lists) and the zipmods in its
  `mods` folder.
- **Extra mods folders**: zipmods without an install, at a lower priority.
- **HS2Next packs**: an interface for a later pack format; nothing implements it yet.

The **content catalog** composes the sources in priority order. It builds the **list DB** (one table per list category),
opens bundles with their dependencies and loads assets. Zipmods are treated as the Sideloader loader treats them: the set of
zipmods is one file per GUID chosen by the manifest version, in Sideloader's load order; a named load from a bundle path
that several sources ship is served by the first copy in load order that holds that name; loose PNG textures inside a
zipmod replace the texture of the same path. Scans are cached per source (`Cache/modlists-*.tsv`, keyed by path, size and
time), so a warm start reads no zipmod.

**Ids.** Every part reference is a `ContentId`: the GUID of the source's namespace plus the number inside it, written
`guid#id`. The game's own rows are `illusion.hs2#<number>`; a zipmod row is `<manifest GUID>#<its own number>`, the pair a
card's Sideloader resolve information stores. Numbers are never reassigned at load time, which removes the collisions of the
game's run-time renumbering. A reference is exact together with its list category, which the context gives (the clothes
kind, the accessory slot). Studio lists are a separate DB keyed the same way, except where the game keys globally
(animations by group, category and number, last definition wins).

**Bundle safety.** Unity 6 reads the game's Unity 2018.4 bundles. Before the first asset of a bundle is loaded, the catalog
checks its textures from the headers (the verdict is cached per bundle stamp) and repairs in memory what would crash the
graphics driver: missing mip counts, block-compressed sizes that are not multiples of 4, textures without data, truncated
meshes, broken path tables. A repaired bundle is written once, uncompressed, to the repair cache and opened from there. A
bundle that cannot be repaired is refused with a finding. While the first asset of a zipmod bundle loads, a marker is kept;
if the process dies there, the next start refuses that bundle with a finding until its file changes.

**Bundle lifetime.** Every bundle the catalog hands out is recorded with its current owners (a map, a scene node, a
character part, an animation, a thumbnail). A bundle without owners goes into an LRU of 768 MB (`content.bundleCacheMB`)
and is closed without destroying what was loaded from it; unused assets are released at quiet moments, at most every 30 s.

## Characters

A character is assembled from a card, or from an HS2Next character document, by `CharaAssembler`
(`Assets/HS2Next/Runtime/Chara`): the game's skeleton and body prefab, the head, hair, clothes and accessories from the
lists, the skin and clothes textures composited on the GPU, the shape sliders (body and face tables re-derived by
measurement), then the plugin data of the card. Male characters share the path with per-sex categories and assets.

Bundle materials are shared between characters and are never written: a character renders only its own copies. Copies keep
the keywords a material had when it was loaded (the Unity Editor would otherwise switch toggle keywords on the first read).

**Plugin data.** Cards carry extension data of the game's plugins. `Assets/HS2Next/Runtime/Data/Ext` decodes it (11 typed
decoders: extra accessory slots, ABMX bone modifiers, MaterialEditor, KSOX and KCOX overlays, push-up, DynamicBoneEditor,
InvisibleBody, Additional Accessory Controls, the Sideloader resolve information, the uncensor selection) and keeps every
other plugin's data as raw bytes. `Assets/HS2Next/Runtime/Chara/Ext` applies the decoded data at fixed points of the build.
The uncensor selection is kept as data only and never applied. Some plugins keep their data in the mod packages instead
of the cards: heel data in the zipmod manifest (`Runtime/Content/HeelsData.cs`, applied by `Runtime/Motion/Heels`), a
head's own face compositing materials (`Runtime/Content/Maker/Unity/HeadBundleRedirect.cs`), extra bone lists in the
bundles (`Runtime/Content/AdditionalBones.cs`) and part components (skinned accessories, body material copies, body cuts,
bone implants) handled where the part is built (`Runtime/Chara/Edit`).

**Editing.** The maker pages edit a character through edit units: each unit (head, skin compositing, eyes, shape, bust,
each hair slot, each clothes kind, masks, accessories, dynamic bones, the plugin stages) knows what it depends on, and an
edit redoes only the units it touches, in the build's order.

## Motion

Each character has a `CharaMotion` that steps a fixed list of **motion stages** once per frame (in `LateUpdate`, or by hand
with `Step(dt)` in tests and while paused). The order is the `MotionPhase` value, lower first:

| Phase | Stage |
| --- | --- |
| 0 `Animation` | animation sampling: the game's animator controllers played through a manually evaluated PlayableGraph |
| 100 `Shape` | shape bones on change, the 21 hand shapes, the sway switches of an animation row |
| 150 `IkPrepare`, 200 `Ik` | IK targets and the IK solve |
| 300 `NeckLook` | neck look |
| 400 `Fk` | FK overrides (the Studio's FK groups; also item FK) |
| 500 `EyeLook` | eye look |
| 600 `Face` | expression blend shapes, blinking, mouth, tears, cheek |
| 700 `Correct` | joint correction: the twist bones computed from the final pose |
| 750 `BustPhysics` | bust and hip chains |
| 800 `DynamicBone` | dynamic bones of hair, clothes and accessories |
| 900 `Final` | what must see the final pose (bone modifiers, measurements) |

Every stage is HS2Next's own implementation, written from measured behaviour; a stage's exception is caught and logged
without stopping the others. Two places differ from the game on purpose: the expression
runs after the eye look, so an eyelid correction sees the same frame, and the bust chains run after the joint correction,
which removes a one-frame lag.

**FK and IK.** IK uses the Studio's 13 targets in 5 groups so that posing feels the same; the solver is analytic (two-bone
limbs with a bend goal, the torso distributed over the spine). FK and IK can be used together; the Studio's exclusive
behaviour is an option. HS2 pose files import FK exactly and IK approximately.

**Defaults.** The default of every behaviour is the game without plugins. Much of what a modded HS2 shows comes from plugins
(joint correction, eye-movement range, male height), so such behaviour is a named switch. The switches whose default is not
the vanilla game are `CharaAssembler.MaleHeightPin075` (off: the card's male height is kept), `HairUnit.MeshOnEveryHair`
(on), `HairUnit.LiveChainRootDrags` (on), `ContentCatalog.SideloaderZipmodSet` and `SideloaderAssetRedirect` (on, as the
loader does), `maker.accessoryHandles` (on), `studio.accessoryItems` (on), `render.mosaicWide` (on) and `render.ssr` (off).

## Rendering

The app renders with URP only (Forward+, rendering layers). The Editor project itself stays on the Built-in pipeline: the
Editor draws the game's original shaders there as the reference that the replacements are fitted to; the app switches to
URP when it starts, and the player build makes URP the default only while it builds.

**Shader replacements.** Bundle materials name Built-in shaders. `ShaderMap` replaces them by name with URP shaders that
declare the same property names, types and defaults, so the material's values stay valid. The character shaders, the
Studio item and map families, the generated legacy family and the common shader mods were fitted by black-box probing:
controlled inputs drawn with the original in the Editor, the replacement written and compared; the measured differences
of every family are in `Assets/HS2Next/Shaders/README.md`. A shader name can stand for several bundle copies
with different properties; the map routes each copy to the replacement that matches it. Anything unmapped falls back to
`URP/Lit` with its main texture and colour, and the fallback is reported.

**Lights.** The game does not use linear light intensities, so the intensity is folded into the colour. The game's culling
masks become rendering layers (Forward+ ignores culling masks). Point and spot lights use the game's range-normalised
falloff by default (`render.lightFalloff`).

**Screen effects.** The Studio's 12 effect blocks map to URP volumes and HS2Next's own passes: colour grading with look-up
tables baked on the CPU (the game's base grade is read from the user's install), ambient occlusion, bloom, vignette, the
item-focus depth of field, sun shafts and height fog reproduced from the original formulas, and screen-space reflections as
an option. A "modern" HDR grading is an option.

**Mosaic.** The parts of the app's own content that the game covers with a mosaic always draw it. The plugin API guards it
at its points: material overrides refuse mosaic slots, build hooks offer no stage after the mosaic, the host restores a
member's mosaic material after plugin hooks ran, and added members stay members. A body part a plugin supplies is drawn as
the plugin supplies it. The wide screen-space mosaic over the same area is an option (`render.mosaicWide`, on by default).

## Scenes and files

**The scene model** (`Assets/HS2Next/Runtime/Scene/Model`, `Commands`, `Events`) is engine-free and runs under plain Mono:
a node tree (characters, items, lights, cameras, folders, routes), each node with a GUID, a parent and an attach point. Every
change is a command on the command bus; a drag is one transaction; the history keeps 200 steps or 64 MiB. Undo and redo
cover structure changes too (add, delete, duplicate, parent, reorder).

**The scene runtime** (`Assets/HS2Next/Runtime/Scene/Runtime`) realizes the model in Unity: it builds characters, items and
maps, applies states, keeps deleted objects for undo, and steps item motion after the characters. The UI and the bridge go
through one operation layer (`StudioOps`), so a click and a command do the same thing and make the same undo entry.

**Save format version 1** (`Assets/HS2Next/Runtime/Save`). Four kinds: scenes (`.hs2nscene`), characters (`.hs2nchara`),
coordinates (`.hs2ncoord`) and poses (`.hs2npose`).

- The container is a ZIP behind a 504 × 704 PNG thumbnail: image viewers show the picture and ZIP tools read the archive.
  The thumbnail is not repeated inside.
- `manifest.json` carries the format name and version (nowhere else), the writing app, library fields, `requires`, the
  ext entries and the import provenance (source file name, SHA-256, time, Sideloader GUIDs; the original card's bytes are
  not stored).
- The document (`scene.json`, `chara.json`, `coord.json` or `pose.json`) is canonical JSON: members in a fixed order,
  floats in round-trip form, non-finite numbers refused. References are `guid#id` strings; the place in the document gives
  the category. A scene holds its characters as subtrees, not as nested files.
- Binary resources (overlay images) are stored once under `res/` by their SHA-256.
- Extension blocks live under `ext/<namespace>/`: a plugin's namespace is its GUID; data imported from HS2 plugins is kept
  raw under `ext/hs2.kkex/`. Unknown members and unknown blocks survive a load and save unchanged.
- `formatVersion` 1 is frozen. A file is written with the lowest revision that covers it (revision 2 only adds an optional
  member for clothes parts); readers migrate older revisions and refuse newer ones with a finding. Saving writes a temporary
  file and replaces the target atomically, and the same document always gives the same bytes.

**Import** (`Assets/HS2Next/Runtime/Import`) turns HS2 character and coordinate cards into these documents when they are
placed in a scene: it validates the card, applies the game's version updates and the Sideloader resolve rules, and keeps
the plugin data as typed sections plus the raw copy. HS2 pose files (`.dat` and the PNG pose format) load into a character.
Nothing is ever written back in HS2's formats.

## The UI

The UI (`Assets/HS2Next/Runtime/UI`) is UI Toolkit with C# and USS, without UXML. Its core (actions, key maps, the key
resolver with text input and IME handling, the localisation table, search, the layout and options files) is engine-free.
The shell has fixed regions: the scene tree on the left, the inspector on the right (characters have nine tabs, the maker
pages among them), the add lists at the bottom, the toolbar at the top. The viewport has its own camera, CPU picking and
gizmos drawn only into the view camera (never into captures). Key maps: `default` and `hs2studio` (the HS2 Studio layout).
Strings come from `Assets/HS2Next/Resources/UI/L10n/strings.tsv` in English, Japanese and Simplified Chinese; the fonts
are the bundled Noto Sans CJK JP and SC.

## The automation bridge

The bridge (`Assets/HS2Next/Bridge`) is an HTTP/JSON server on `127.0.0.1:17322`, on by default in every build, so that
scripts and tests drive the app the way a user would. Requests with an `Origin` or `Sec-Fetch-*` header are refused,
whatever the value, which keeps web pages out; the release build also requires a per-start token. Requests are queued and
run on the main thread in order, within a per-frame time budget; a command may defer its reply to a later frame. `help`
describes every command, and names that HS2 Studio's automation already used keep HS2's arguments and reply members. The
147 commands of 1.0 are listed in [Reference/COMMANDS.md](Reference/COMMANDS.md).

## Plugins

Plugins are .NET assemblies in `<app>/Plugins/<guid>/` with a `manifest.json` (`Assets/HS2Next/Runtime/App/Plugins`).
The loader checks the manifests, the API version, dependencies and command prefixes, orders the plugins by their
dependencies and loads them in the start-up stage `plugins`. A plugin registers commands under its prefixes, ext blocks under
its GUID, event handlers (the capture events among them), shader supplies and, through `HS2Next.Api.Unity`, character build
hooks, material overrides and additional mosaic members; it calls commands through typed helpers. Experimental in 1.1: undo
entries in the scene's own history, forms (panels and inspector sections that the app draws with its own widgets from the
plugin's read and write commands), UI panels of its own with the app's widgets, and at the build's `BodyMesh` point skin
layers and body-part replacement, with rebuild requests outside builds. A plugin can be disabled and enabled again while the
app runs: disabling unloads it and removes what it registered, enabling runs its load again on the loaded assembly and reads
the open scene's data of its namespaces again; a replaced DLL loads after a restart. Every call into a plugin is guarded: an
exception is logged and counted, and after 10 faults the plugin is stopped. Plugins run with the app's rights; there is no
sandbox. [Reference/PLUGINS.md](Reference/PLUGINS.md) is the guide (also in Japanese and Simplified Chinese). The
distribution zip carries it in `PluginKit/` with the command reference, the XML documentation of the API and two
engine-free samples whose sources are `Samples/PluginKit`; `Samples/Sequencer` is a complete example.

## Start-up

`AppCore` boots in stages that `ping` reports: settings, render pipeline, bridge, the first-run guide when no valid HS2
folder is set, the catalog (vanilla lists at once, the zipmod scan on a worker thread), the Studio lists, the initial scene,
and last the plugins. With warm caches the app is ready about 2 seconds after the start.

## Data folders and settings

The **data root** holds `Config/settings.local.json`, `UserData/` (HS2Next's scenes, characters, coordinates, poses, the
bridge's files) and `Cache/` (scan caches, texture verdicts, repaired bundles, thumbnails). It is

- the project folder in the Editor;
- for a player: the folder of `HS2Next.exe` when `portable.txt` lies next to it, else the folder named by the environment
  variable `HS2NEXT_DATA_ROOT`, else `%USERPROFILE%\AppData\LocalLow\stp3\HS2Next`.

The HS2 install is only read: its `UserData` is a source of cards and poses. One app at a time uses a `UserData` folder.
Every setting is listed with a one-line explanation in `Config/settings.example.json`; the common ones are in File > Options,
and most have a command-line switch for one run.

## Tests

`Tools/run_tests.ps1` runs the batch tests: Editor methods in a batch-mode Unity (most of them headless, sharing one Unity
process), graphics rows, player rows against the built app, plain-Mono rows for the engine-free code, and `LeakCheck`, the
publication check. Comparisons with the game use recorded measurements: the HS2 side is driven by an automation plugin for
HS2 Studio, its numbers are recorded, and HS2Next's are compared with them within stated tolerances. Renders are compared
as numbers. [Tools/README_tests.md](Tools/README_tests.md) lists the rows and what each needs.

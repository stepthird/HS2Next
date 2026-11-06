# Unattended tests

HS2Next is tested by batch jobs: Editor methods (`Assets/HS2Next/Editor/*Test.cs`) run as `-executeMethod <Class>.Run` in a
batch-mode Unity, plain-Mono suites run the engine-free code without Unity, and player rows test the built app through its
automation bridge. `Tools/run_tests.ps1` runs them one after the other and writes a summary. This page says how to run the
suite, what a row needs, and where the results go.

## What you need

- Unity 6000.6.5f1, PowerShell 7 (`pwsh`) and Python 3 (standard library only), as for building
  ([README.md](../README.md), "Building from source").
- A Honey Select 2 install, named by `hs2Root` in `Config/settings.local.json` (copy it from `Config/settings.example.json`).
  Most rows build characters, items or maps from the game's data and cannot run without it.
- The test targets: `Tools/targets.json` names the cards, animation rows and folders the rows use (the visual female and
  male cards, the card folders, cards to skip). Copy `Tools/targets.example.json` to `Tools/targets.json` and fill it with
  files of your install; `TestTargets` checks it. Without `Tools/targets.json` the runner reads the example, and rows that need
  a target report WARN.
- Some rows compare HS2Next with recordings of the running game (numbers taken through an automation plugin of HS2 Studio)
  or with baselines recorded on the reference install (`Cache/compare*`, `Cache/maker`, `Cache/shapemeasure`, ...). These
  recordings are not part of the repository. A row whose recording is absent is not started and reports
  **WARN "development input absent: <path>"**; the summary counts these WARNs on a line of their own. They are not failures.
- Free memory: `Tools/unity_batch.ps1` waits until 8 GB are free before it starts Unity.

## Running the suite

```powershell
pwsh -File Tools\run_tests.ps1 -Quick                  # the everyday suite: headless rows + the plain-Mono rows (about 14 min of Unity)
pwsh -File Tools\run_tests.ps1                         # headless + graphics + player rows
pwsh -File Tools\run_tests.ps1 -Full                   # + the long rows (every card once, the item and map corpora): several hours
pwsh -File Tools\run_tests.ps1 -Only LookTest,ShapeMapTest.*   # named rows, wildcards allowed
pwsh -File Tools\run_tests.ps1 -Full -Changed HEAD -List       # the rows a working-tree change affects, without running them
pwsh -File Tools\run_tests.ps1 -Quick -NoMono          # without the plain-Mono rows
```

- `-List` prints the rows a command would run (id, group, areas, the last known minutes) and starts nothing.
- Groups: **headless** (edit-mode Editor jobs with `-nographics`; most of them share one Unity process, the HeadlessBatch),
  **graphics** (Editor jobs that render, and play-mode jobs), **player** (the app player is built and tested through its
  bridge), **full** (long rows: all cards, corpora, timing), **mono** (plain-Mono suites, `Tools/mono_tests.ps1`, started in
  the background with `-Quick`). `-Quick` = headless + mono; the default = headless + graphics + player; `-Full` = all.
- `-Area <a,b>` selects the rows tagged with these areas; `-Changed <revision or range>` maps the changed paths through
  `Tools/test_areas.json` to areas and rows (`-Changed HEAD` = the working tree against HEAD, `A..B` = a range). Both narrow the
  group selection; `-List` shows what they selected. A path that no entry of `test_areas.json` matches is printed as UNMAPPED.
- `-Gfx d3d11 | d3d12` runs the graphics jobs with that graphics API (the default is Unity's, D3D12). A row that lost the
  graphics device is re-run alone with `-Only`, once with each API.
- `-GpuLock <file>` and `-InputLock <file>`: before every graphics job and every player run the runner waits until both files
  are absent and holds the GPU lock while the job runs; windowed players also take the input lock. Use them when several
  processes share the machine.
- `-NoOfflineCompile` leaves out the offline compile check that the runner runs once before the first row.

Only one Unity may open the project at a time: `Tools/unity_batch.ps1` takes a machine-wide lock, and the runner waits for it.
Every row has a time limit; a row that exceeds it FAILs and its process is stopped.

`Tools/unity_batch.ps1` run by hand stops its own Unity when the job's log has not changed for `-StallMin` minutes (default 30;
0 turns this off), or for `-StallEndMin` minutes (default 3) once the log shows that Unity is shutting down. It prints why and
exits 125; a lock wait also exits 125, and the printed line tells them apart. It never touches another Unity process. Pass
`-StallMin 0` to a job that can stay quiet that long, such as an idle `BridgeServe`. `-StallSelfTest` checks the watcher with a
fake Unity (no Unity, no project, about 1.5 min). The runner passes `-StallMin 0` to its rows: it watches them itself.

## Results

Each run writes `Spike/out/tests/<yyyyMMdd-HHmm>/`:

- `summary.md`: one line per row (PASS / FAIL / WARN, minutes, key numbers), the graphics API and device-loss line, the WARN
  counts, notes;
- a copy of every row's report (`<row id>__<report>.txt`) and each Unity job's console (`<row id>.console.txt`); the Unity logs
  are in the log folder of `Tools/unity_batch.ps1` (`HS2N_LOG_DIR`, else `tmp\hs2next_logs` on the system drive).

The runner exits 1 when a row FAILed. WARN does not fail the suite. Reports live under `Spike/out/` (git-ignored); most have a
first line `SUMMARY: ...` or `N passed, M failed`.

## What the rows need

| Input | Rows | Without it |
| --- | --- | --- |
| the game install (`hs2Root`) | almost every Editor and player row | the row fails at its start (missing content) |
| `Tools/targets.json` | in `-Quick`: `TestTargets`, `ImportTest`, `SceneRuntimeTest`, `ItemMotionTest` (and `.play`), `FkWriterTest`, `IkSolverTest`, `PoseImportTest`, `IkDriverTest`, `UiOpsTest`, `PauseTest`, `PausedEditTest`, `CharaStateTest`, `UiShellTest.play`, `UiListsV12Test.play`, `HAnimeTest`, `MosaicTest`, `BridgeCoreTest`, `TimelineTest`, `AdvancedPoseTest`, `ModifierOrderTest`, `MaterialEditTest`, `ConstraintTest`, `AssetRedirectTest` and the Maker rows (`MakerCoreTest`, `MakerUnitTest`, `MakerEditTest`, `MakerFaceTest`, `MakerBodyTest`, `MakerHairTest`, `MakerClothesTest`, `MakerAccessoriesTest`, `MakerFileTest`, `MakerSchemaTest`); also `CharaRenderTest`, `StreakProbe.check`, `PlayerSmoke`, `CommandsDoc`, `TimelineTest.play`, `TimelineCheck.player`, `AdvancedPoseTest.play`, `AdvancedPoseCheck.player`, `UiEscTest.play`, `AdvancedPoseTest.recon` and the player acceptance rows | WARN (development input absent) |
| a local list of cards with bone modifiers (`Cache/compare_mods/cards.json`) | `ModifierOrderTest`, `MosaicTest`, `AdvancedPoseTest.play`, `AdvancedPoseCheck.player` | the row runs and leaves those checks out with a WARN line in its report |
| recordings of the running game (`Tools/compare_poses.json`, `Cache/compare`, `Cache/shapemeasure`) | `PoseImportTest`, `IkDriverTest`, `CompareTest`, `ShapeMapTest.*` | WARN (development input absent) |
| the S1 baselines (`Cache/maker/baseline`) | `MakerCoreTest` | WARN (development input absent) |
| the record of the imported material arrays (`Cache/maker/me_corpus_7b8c37c.tsv`, kept locally, never taken again) | `MaterialEditTest.corpus` | WARN (development input absent) |
| census texts of the reference install (`Tools/survey/redirect_step0_census.txt`) | `AssetRedirectTest` | WARN (development input absent) |
| further recordings (`Cache/items`, `Cache/compare/routes`, `Cache/compare/maker`) | `ItemTest`, `ItemMotionTest`, `RouteTest`, `BridgeCoreTest` | the row runs and skips those checks with a WARN line in its report |
| a graphics device | the graphics and player groups | the row cannot run headless; run it on a machine with a GPU |
| the built app (`Builds/App`) | player rows after `PlayerBuild.app` | the player rows build it first |
| the .NET SDK | the plugin rows (sample and test plugins are built with `Tools/plugins.ps1`) | the plugin rows FAIL |

Each row declares its development-only inputs in `Tools/run_tests.ps1` (`-DevInputs`); `-List` marks a row whose input is
absent ("WARN, not started here"). Without any of them, `-Quick` runs 36 of its 76 rows and reports the other 40 as WARN; with
only your own `Tools/targets.json`, 5 rows remain WARN (`PoseImportTest`, `IkDriverTest`, `CompareTest`, `MakerCoreTest`,
`AssetRedirectTest`) and the five `ShapeMapTest` rows, which have no measured sets to compare. The rows outside `-Quick`
(graphics, player, full) need `Tools/targets.json` and often the recordings as well; only the rows named above declare them so far.

The first run on an empty cache scans the whole install (the zipmod list cache, the texture verdicts): expect several extra
minutes in the first row that loads content.

## The publication check (`LeakCheck`)

`python -I Tools/leak_check.py` is the first row of every suite (no Unity). It checks the files that the repository publishes:
every tracked file must be classified by `Tools/export_manifest.json` as published or not, and the published files may hold
no drive or profile path, e-mail address or private string (local patterns in `Config/leak_tokens.local.txt`, git-ignored and
optional), and no switch that would turn the mosaic off. Synthetic test paths are allowed by `Tools/leak_allow.json`. PASS =
exit 0 (no hard hit, no unclassified path). Report `Spike/out/leak/leak_report.txt`; `--tree <dir>` checks an exported tree,
`--manifest-coverage` only the classification.

## The public contract (`ApiSurfaceTest`)

`ApiSurfaceTest` (headless, part of `-Quick`) compares the public contract with its committed listing `Tools/api/*.surface.*`:
the public types and members of `HS2Next.Api` and `HS2Next.Api.Unity`, the bridge protocol, every command not flagged
experimental (help texts left out) and the plugin manifest schema. A removed or changed entry is a MAJOR change and fails; an
added entry fails unless the Api's minor version is above the listing's; changes to experimental entries are reported only.
It needs no game install. `-write` rewrites the listing, for a release whose version changes. Report
`Spike/out/api/api_surface_report.txt`.

## The API documentation (`ApiDocs`)

`ApiDocs` (headless, part of `-Quick`, no Unity) runs `python -I Tools/offline_compile.py --doc Spike/out/api/doc --asm-only -q`:
it compiles `HS2Next.Api` and `HS2Next.Api.Unity` with their XML documentation and fails when a public member has no doc comment
(CS1591) or a parameter has none (CS1573), listing them. PASS = exit 0. The two XML files are the ones the distribution ships
(`Tools/package_dist.ps1` generates them the same way).

## The offline compile check (`Tools/offline_compile.py`)

`python -I Tools/offline_compile.py` compiles `Assembly-CSharp`, `Assembly-CSharp-Editor` and the project's own assemblies with
Unity's compiler without starting Unity (10-40 s), from the response files of the last Unity compile (open the project once
first). `--head` compiles the committed sources of HEAD, `--overlay <file>` replaces or adds files from a scratch folder
(`<scratch file>|<project path>` per line), `--tree <dir>` compiles an exported tree. Exit 0 = no compile errors. The runner
and `Tools/unity_batch.ps1` run it before Unity and print a warning on errors.

## Player rows

- `PlayerBuild.app` builds the app player: `& Tools\unity_batch.ps1 -Method PlayerBuild.BuildApp -LogName appbuild` →
  `Builds/App/HS2Next.exe` (report `Builds/App.build_report.txt`, with the shader program counts). The row FAILs when more
  than 18,537 shader programs remain after URP stripping, and it scans every file of the build with
  `python -I Tools/dist/binary_scan.py --tree Builds/App --all-files`: no path, project folder or private string in an assembly
  built from this project or in a data file, and no engine or runtime file above its recorded count. `PlayerBuild.dist` stages
  the release folder from it (`Tools/package_dist.ps1`, the same scan and the leak check included); `-Zip` makes the release
  zip.
- The player rows start `Builds/App/HS2Next.exe` with their own UserData and output folders and absolute `-settings` /
  `-cacheDir`, drive it through the bridge on their own port (17391-17399) and stop only the process they started.
  `-bridgeAccept <dir>` runs the in-app acceptance against the player's own bridge.
- Rows that run the plain exe of the release folder (`PlayerBuild.noargs`, `FirstRun.player`) copy the folder first, set
  `HS2NEXT_DATA_ROOT` to a scratch folder (so they never write into your per-user folder) and need the default port 17322 free.
- `PluginKit.dist` checks the plugin kit as a user gets it: `python -I Tools/pluginkit_check.py` packages the tested app build
  into a zip of its own (`Spike/out/player/pluginkit/dist`), extracts it outside the repository, builds the kit's two samples
  with one `dotnet build` in the extracted `PluginKit` folder (no argument, no package source; .NET SDK 8 or later), copies them
  into the extracted `Plugins` folder and starts the extracted exe on port 17388 with its own data root: both samples load,
  `hello.greet` answers, `app.quit` exits 0. Report `Spike/out/player/pluginkit/pluginkit_report.txt`.
- `TimelineCheck.player` checks the Timeline and the frame recording in the app player: `python -I Tools/timeline_player_check.py
  --exe Builds/App/HS2Next.exe --project <project> --out Spike/out/player/timeline --port 17387` starts the player with the UI
  off, builds the test compositions of `Tests/Timeline/compositions` on the test targets and measures the Timeline's time per
  frame with 9 FK bone tracks (median at most 0.479 ms), records frame sequences (the PNG files are checked by their header
  bytes, then removed; `record.json` stays) and checks that a scene with a Timeline saves and loads the same. Report
  `Spike/out/player/timeline/timeline_player_report.txt`.
- `AdvancedPoseCheck.player` checks advanced pose in the app player: `python -I Tools/advancedpose_player_check.py --exe
  Builds/App/HS2Next.exe --project <project> --out Spike/out/player/advancedpose --port 17389` starts the player with the UI off on
  the test targets, measures the time per frame of 1, 8 and 64 corrected bones and of 16 blend-shape weights (over the bars of
  0.5 ms and 0.1 ms the row says WARN), compares corrected bones with a twin of the same card over real frames, and checks that
  a scene with these values saves and loads the same. It also measures a card's bone modifiers on the card with the most of them
  in a local list (`--modifier-card`, or `Cache/compare_mods/cards.json`; over 0.2 ms the row says WARN, and without either the
  check is left out with a WARN). Report `Spike/out/player/advancedpose/advancedpose_player_report.txt`.
- `MaterialEditCheck.player` checks material edits in the app player: `python -I Tools/material_edit_player_check.py --exe
  Builds/App/HS2Next.exe --project <project> --out Spike/out/player/materialedit --port 17403` starts the player with the UI off on
  the test targets. It compares the shaders a material edit can pick, their keywords and property lists with the Editor's list
  (`Spike/out/maker/material_shaders_editor.json`, written by `MaterialEditTest.full`: run that row first, or those checks are a
  WARN), checks that a keyword changes pixels (in memory), edits, swaps, saves and loads every kind of target with a map, and measures a
  drag step (bars 2 ms median, 4 ms p95), a commit (10 ms), a colour track over 16 edits (0.05 ms a frame), a 4,096 px import and a hair
  bone's step (16 ms); over a bar the row says WARN. It imports its scaffold from `Tools/advancedpose_player_check.py`. Report
  `Spike/out/player/materialedit/material_edit_player_report.txt`.
- `ConstraintCheck.player` checks constraints in the app player: `python -I Tools/constraint_player_check.py --exe
  Builds/App/HS2Next.exe --project <project> --out Spike/out/player/constraints --port 17828` starts the player with the UI off on the
  test targets. It checks that an empty list leaves no trace, a chain across two characters shows no lag and an entry of each target
  kind survives a save and a load, and measures the evaluation of 8 constraints (bars 0.10 ms median, 0.25 ms p95) and of 64 (0.50 ms,
  1.0 ms), the pass beside the characters' steps (0.05 ms) and the three driven-bone stages with 16 bones (0.1 ms); over a bar the row
  says WARN. Report `Spike/out/player/constraints/constraint_player_report.txt`.
- With the setting `mapAudioLink` a player makes a junction `<player folder>/abdata` into the game's `abdata`. Never delete a
  player folder recursively while it holds that junction: list reparse points first
  (`Get-ChildItem -Recurse -Force -Attributes ReparsePoint`) and remove the junction itself with `cmd /c rmdir`.

## Plugins in test runs

The runner sets `HS2NEXT_NO_PLUGINS=1` for every process it starts, so no row loads a plugin from a `Plugins` folder by
accident. The plugin rows load their own folders (`Spike/out/plugins/...`) or, for `PluginCheck.player`, stage the sample next
to the player and remove exactly what they staged. `& Tools\plugins.ps1 -Build all -Stage <editor | player | dir>` builds and
stages the samples (the Sequencer and the kit's Hello and HelloUi) and the test plugins. The plugins written for Api 1.0 are
also built against the Api 1.0.0 reference assemblies in `Tests/Plugins/fixtures/api10` (`-ApiDir`), so every run checks that a
1.0 plugin still loads on the current host; `PluginCheck.player` builds the Sequencer that way (`--api-dir`) and turns it off
and on again with `plugin.disable` / `plugin.enable` while the player runs.

## The app for external clients (`BridgeServe`)

`& Tools\unity_batch.ps1 -Method BridgeServe.Run -NoQuit -MethodArgs @('-serveMinutes','90')` enters play mode with the app
and serves the bridge on the settings port until `{"cmd":"app.quit"}` or the time limit. Clients: `Tools/hs2n_bridge.py`,
or `curl -s -X POST http://127.0.0.1:17322/ -d '{"cmd":"ping"}'` (always 127.0.0.1, never localhost).

## Adding a test

- Write the test as an Editor class with a static `Run()` (headless where it can be), a report under `Spike/out/<area>/` with
  a summary line, and an exit code (0 pass, 1 fail). Engine-free logic goes into a plain-Mono suite as well.
- Add a row to `Tools/run_tests.ps1` in its group's place: `Row '<Id>' <group> '<areas>' '<Class>.Run' @() '<report>'` (or a
  `-Run { ... }` block for rows with logic of their own), and, for the new test file, an entry in `Tools/test_areas.json` so
  that `-Changed` selects the row.
- Declare the inputs the row reads that are not in the repository, so that it reports WARN instead of FAIL where they are
  absent.
- Tests judge numbers: renders are compared as pixel statistics against references.

## The everyday rows (`-Quick`)

`-List` prints the full set (185 rows with `-Full`). The `-Quick` rows:

| Row | Group | Areas |
| --- | --- | --- |
| LeakCheck | headless | publication, plugins |
| ApiDocs | headless | api, plugins, publication |
| TestTargets | headless | infra |
| ContentIdTest | headless | content |
| SaveFormatTest | headless | save, materials, constraints |
| ImportTest | headless | import, save |
| SceneModelTest | headless | scene, save, materials, constraints |
| PoseFileTest | headless | pose |
| ZipmodListTest | headless | content |
| CardExtTest | headless | content, import |
| CardResolveTest | headless | content, chara-build |
| JointCorrectionTest | headless | motion |
| LookTest | headless | motion |
| ExpressionTest | headless | motion |
| DynamicBoneTest | headless | motion |
| AnimationTest | headless | motion |
| SceneRuntimeTest | headless | scene, items, chara-build, save |
| ItemMotionTest | headless | items, motion |
| ItemMotionTest.play | headless | items, motion |
| ItemTest | headless | items, shaders |
| MapTest | headless | maps, shaders |
| MapTest.play | headless | maps, scene, bridge |
| LightCameraTest | headless | effects, scene |
| FkWriterTest | headless | pose, motion |
| IkSolverTest | headless | motion, pose |
| PoseImportTest | headless | pose, import, scene |
| IkDriverTest | headless | motion, pose, scene |
| UiOpsTest | headless | ui, scene, bridge |
| PauseTest | headless | routes, motion, scene, bridge |
| PausedEditTest | headless | routes, motion, scene, chara-build, save, ui |
| RouteTest | headless | routes, scene, bridge |
| CharaStateTest | headless | ui, chara-build, scene |
| UiCoreTest | headless | ui |
| UiShellTest.play | headless | ui, bridge |
| UiListsV12Test.play | headless | ui |
| PoseThumbTest | headless | pose, ui |
| PoseBrowserWidthTest.play | headless | ui |
| CompareTest | headless | compare, motion, pose, chara-build |
| ShapeMapTest.female_body | headless | chara-build, compare |
| ShapeMapTest.female_face | headless | chara-build, compare |
| ShapeMapTest.male_body | headless | chara-build, compare |
| ShapeMapTest.male_face | headless | chara-build, compare |
| ShapeMapTest.oor | headless | chara-build, compare |
| BridgeTest | headless | bridge |
| BridgeTest.play | headless | bridge |
| MakerCoreTest | headless | maker, chara-build |
| MakerUnitTest | headless | maker, chara-build |
| MakerListTest | headless | maker, content |
| MakerEditTest | headless | maker, scene, chara-build |
| MakerFaceTest | headless | maker, chara-build |
| MakerBodyTest | headless | maker, chara-build |
| MakerHairTest | headless | maker, chara-build |
| AssetRedirectTest | headless | content, chara-build, maker |
| MakerClothesTest | headless | maker, chara-build |
| MakerAccessoriesTest | headless | maker, chara-build |
| MakerFileTest | headless | maker, save, scene |
| MakerSchemaTest | headless | maker, bridge, ui |
| HAnimeTest | headless | motion, ui, scene, pose |
| MosaicTest | headless | maker, chara-build, shaders, advancedPose, materials, constraints |
| AccessoryItemsTest | headless | items, content, ui |
| LooseTextureTest | headless | content, maker |
| BridgeCoreTest | headless | bridge, scene, items, maps, chara-build, motion, pose, effects, ui, plugins, timeline, advancedPose, materials, constraints |
| ApiSurfaceTest | headless | api, bridge, plugins |
| TimelineTest | headless | timeline, scene, save, bridge, motion |
| AdvancedPoseTest | headless | advancedPose, motion, scene, save, bridge |
| ModifierOrderTest | headless | motion, advancedPose, maker |
| MaterialEditTest | headless | materials, chara-build, items, scene, save |
| ConstraintTest | headless | constraints, motion, scene, save, bridge |
| Mono.scene | mono | scene, save, maker, timeline, advancedPose, materials, constraints |
| Mono.ui | mono | ui, setup, advancedPose, materials, constraints |
| Mono.maker | mono | maker, content |
| Mono.save | mono | save, timeline, advancedPose, materials, constraints |
| Mono.pose | mono | pose |
| Mono.bridge | mono | bridge, setup |
| Mono.content | mono | content, setup |
| Mono.plugins | mono | plugins, constraints |

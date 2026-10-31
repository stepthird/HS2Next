# Contributing to HS2Next

Contributions are welcome: bug reports with the steps and the log, fixes, tests, translations, documentation. Answers,
reviews and merges happen when the author can, with no promise of any of them. This page says how to build and test,
which rules a change has to follow, and what the 1.x versions keep stable.

## Build and test

- Unity 6000.6.5f1, Python 3 and PowerShell 7, as [README.md](README.md) describes. Copy `Config/settings.example.json` to
  `Config/settings.local.json` and set `hs2Root`.
- Start Unity from scripts only through `Tools/unity_batch.ps1`. One Unity may open the project at a time; the script
  enforces it with a machine-wide lock and waits for 8 GB of free memory.
- Before a change goes into `Assets/`, `python -I Tools/offline_compile.py` must report 0 errors (it compiles the working
  tree with Unity's own compiler, without starting Unity). Use `--overlay` to check a file from a scratch folder before
  copying it into the project.
- Run the rows your change affects: `& Tools\run_tests.ps1 -Changed <revision or range>` selects them through
  `Tools/test_areas.json` (`-Changed HEAD` for the working tree, `-List` to see the selection first), `-Only <ids>` runs
  named rows, `-Quick` the everyday suite. A new feature comes with a test row
  or new cases in an existing row ([Tools/README_tests.md](Tools/README_tests.md), "Adding a test").
- Never delete a folder recursively when it holds a junction or another reparse point: a player build with the setting
  `mapAudioLink` makes a junction into the game's `abdata`, and a recursive delete through it deletes the game data. List
  them first (`Get-ChildItem -Recurse -Force -Attributes ReparsePoint`) and remove a junction itself with `cmd /c rmdir`.

## Clean-room rules

HS2Next contains no code of Honey Select 2, of its plugins or of mods ([CLEANROOM.md](CLEANROOM.md)). A contribution must
keep it so.

- Do not contribute decompiled, disassembled or copied code of the game, of game plugins or of mods, not even in a
  translated or renamed form. Describe the behaviour you observed and implement it.
- Comments and documents describe behaviour in plain sentences. They do not name methods of the game or of plugins.
- Data names are allowed: bone and transform names, material property and shader names, list columns, enum numbers,
  zipmod GUIDs, the type and field names that the bundles' TypeTrees carry.
- Numeric behaviour (shaders, sliders, physics) is fitted by measurement: controlled inputs, outputs measured in the running
  game, the replacement compared with them. Keep the probe cases and the comparison with the change (`Tests/FitCases/`,
  the fit tools in `Assets/HS2Next/Editor/Fit/`).
- No asset of the game or of mods enters the repository: no model, texture, animation, bundle, list file, card or capture.
  Test fixtures are synthetic.
- New third-party code or data must have a permissive licence (CC0, MIT, BSD, Apache-2.0, or SIL OFL for fonts) and be
  listed in [THIRD_PARTY.md](THIRD_PARTY.md). Copyleft licences (GPL and similar) are not accepted.

## The publication check

`python -I Tools/leak_check.py` scans the files the repository publishes (`Tools/export_manifest.json` lists them) for
local paths, e-mail addresses and other private strings, and checks that every tracked file is classified as published or
not. It is the `LeakCheck` row of every suite. It must report 0 hard hits before a change is merged. Do not add machine
paths, user names or e-mail addresses to files; tests use synthetic paths, which `Tools/leak_allow.json` lists.

## Code

- C# for Unity 6 (Mono scripting backend). The app code is in `Assembly-CSharp` (`Assets/HS2Next/Runtime`); the stable API
  is in the assemblies `HS2Next.Api` (no engine references) and `HS2Next.Api.Unity`; the bridge in `HS2Next.Bridge`; the
  Editor tests in `Assets/HS2Next/Editor`. [ARCHITECTURE.md](ARCHITECTURE.md) gives the map.
- Code that touches Unity objects runs on the main thread.
- Every user-visible string goes through the localisation table `Assets/HS2Next/Resources/UI/L10n/strings.tsv` (English,
  Japanese, Simplified Chinese); no string is written into the code. A new key needs all three columns; a cell that is a
  machine translation not yet read by a speaker of the language carries `zh?` or `ja?` in the note column.
- Bridge commands follow the naming convention in [Reference/COMMANDS.md](Reference/COMMANDS.md). After a command change,
  regenerate the reference (`BridgeHelpDump.Run`, then `python -I Tools/gen_commands_md.py`) and commit it with the change.
- Settings keys are listed with a one-line explanation in `Config/settings.example.json`.

## Compatibility

HS2Next follows semantic versioning from 1.0.0. Within 1.x:

- **Save files.** The save format version 1 (`.hs2nscene`, `.hs2nchara`, `.hs2ncoord`, `.hs2npose`) stays readable. A change
  adds members or ext blocks; unknown members and blocks are kept when a file is saved again.
- **Plugin API** (`HS2Next.Api`, `HS2Next.Api.Unity`). Public types and members without `[Experimental]` are not removed or
  changed. Interfaces the host implements may gain members in a minor version; interfaces plugins implement never change
  (a new capability comes as a new optional interface). A plugin built for 1.x loads on any 1.y with y ≥ x.
- **Bridge.** The protocol number, the error codes and the commands not marked experimental keep their meaning: new
  commands, optional arguments and returned fields may be added, nothing is removed or renamed without an alias.
- **The plugin manifest** keeps its keys and rules (`manifestVersion` 1).
- Settings keys, command-line switches, the UI and the file names inside the data folder are not part of the promise.

A change that breaks one of these needs a new major version. `Tools/api/` holds the committed listing of the current
version's contract (the API types and members, the commands with the protocol, the plugin manifest schema) that the
`ApiSurfaceTest` row compares the code with. A change of a frozen entry must come with a version change and a regenerated
listing: an addition with a minor version, a removal or change with a major one. Changes that touch only experimental
entries (`[Experimental]` types and members, the 12 experimental commands marked in
[Reference/COMMANDS.md](Reference/COMMANDS.md)) need no version change; the row reports them as INFO.

## Licence of contributions

HS2Next is dedicated to the public domain under CC0 1.0 ([LICENSE](LICENSE)). By contributing you agree that your
contribution is released under the same terms, and you confirm that you have the right to release it so.

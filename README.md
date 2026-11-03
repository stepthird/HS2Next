# HS2Next 1.2.0

HS2Next is a scene and character editor for Honey Select 2 content, built anew on Unity 6. It puts the work of HS2
Studio and of the game's character maker in one window: characters, items, maps, lights, cameras, animation, FK / IK,
expressions, screen effects, and the face, body, hair, clothes and accessory pages of the maker. It reads the game data,
character cards, coordinate cards, pose files and zipmods of your own Honey Select 2 install where they are, and saves
its work in its own file formats.

日本語: [README_ja.md](README_ja.md) · 简体中文 (machine translation): [README_zh-Hans.md](README_zh-Hans.md)

## What it is not

- Not a mod or a plugin of Honey Select 2. It is a separate program; BepInEx plugins do not run in it.
- Not a copy of the game. It contains no game data and needs your own install to show anything.
- It does not open HS2 scene files (the `.png` scenes of HS2 Studio) and has none of the game's story, H scenes or
  game progress. Characters, coordinates and poses are imported from HS2 files; scenes are made in HS2Next.

## Status

Version 1.0.0 is the first public version. The save format (version 1) and the plugin API (1.0, semantic versioning)
are frozen: later 1.x versions read every 1.0 file and load every 1.0 plugin. What 1.0 has and lacks is in
[CHANGELOG.md](CHANGELOG.md).

## Requirements

- Windows 10 or 11, 64-bit.
- A graphics card with Direct3D 12. 8 GB of video memory is recommended: the largest maps use about 2.8 GB on top of the
  app.
- Your own Honey Select 2 install (with its `mods` folder if you use mods). HS2Next builds characters from the game's
  own skeleton, body and head data, so no character can be built without the install.
- Free disk space for the cache: about 0.3 GB at first, growing to about 10 GB as repaired mods are opened.

## Using the release

The release zip (`HS2Next-1.2.0-win64.zip`, with its SHA-256 next to it) holds the program. Extract it anywhere and start
`HS2Next.exe`; there is no installer. The program is not code-signed, so Windows SmartScreen may ask once ("More info",
then "Run anyway"). A first-run guide asks for the language, the Honey Select 2 folder, extra mods folders and the cache
folder, then builds the content index (a few seconds to a few minutes, depending on the mods and the disk).

HS2Next keeps its settings, your scenes and characters and its cache in
`%USERPROFILE%\AppData\LocalLow\stp3\HS2Next`. A file named `portable.txt` next to `HS2Next.exe` keeps them next to the
program instead. `README.txt` in the zip covers the camera keys, the automation bridge, graphics problems and
uninstalling.

## Building from source

You need:

- Unity 6000.6.5f1 (Unity Hub; the Windows editor includes the Windows Mono player support).
- Python 3 (standard library only) for the tools under `Tools/`, and PowerShell 7 (`pwsh`) for the scripts.
- A Honey Select 2 install, as above.
- For the sample plugin only: a .NET SDK that builds `netstandard2.1`.

Steps:

1. Clone the repository (about 44 MB; the four Noto Sans CJK fonts are 25 MB of it).
2. Copy `Config/settings.example.json` to `Config/settings.local.json` and set `hs2Root` to the folder that holds
   `abdata` and `mods`. Every other key has a default; the example file explains each one in a line.
3. Open the project in Unity 6000.6.5f1. The first import creates `Library/` (about 1.7 GB) and compiles every
   assembly. From a shell, `& Tools\unity_batch.ps1 -Compile` does the same without the Editor window.
4. Press Play: the app starts in the Editor with the project folder as its data root (settings, `UserData/`, `Cache/`).
5. Build the player: `& Tools\unity_batch.ps1 -Method PlayerBuild.BuildApp -LogName appbuild -TimeoutMin 90` writes
   `Builds/App/HS2Next.exe`. `& Tools\package_dist.ps1 -Zip` turns that tested build into the release folder and zip under
   `Builds/Dist/` (it checks the files and adds the READMEs, `LICENSE.txt` and `THIRD_PARTY.txt`).

`Tools/unity_batch.ps1` is the way to start Unity from scripts: it lets only one Unity open the project at a time, waits
for 8 GB of free memory and returns Unity's exit code. `python -I Tools/offline_compile.py` checks that the C# compiles
without starting Unity (it needs one Unity compile of the project first).

## Tests

`Tools/run_tests.ps1 -Quick` runs the everyday suite (about 65 rows, mostly headless Unity jobs and some plain-Mono
tests); `-Full` adds the long rows, `-Only <ids>` picks rows, `-List` shows them. Most rows need the game install, and
some compare against recordings that only exist on the development machine: without them those rows report WARN, not
FAIL. [Tools/README_tests.md](Tools/README_tests.md) explains the rows, what each needs and where the reports go.

## Documents

| Document | What it covers |
| --- | --- |
| [ARCHITECTURE.md](ARCHITECTURE.md) | How HS2Next is built: content sources and ids, the save format, rendering, motion, UI, the bridge, plugins, start-up, data folders, tests |
| [Reference/COMMANDS.md](Reference/COMMANDS.md) | Every command of the automation bridge (HTTP/JSON on 127.0.0.1:17322) and how to drive HS2Next from a script |
| [Reference/PLUGINS.md](Reference/PLUGINS.md) | Writing a plugin against the 1.0 API; the sample in [Samples/Sequencer](Samples/Sequencer/README.md) |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Building, testing and the rules for contributions |
| [CLEANROOM.md](CLEANROOM.md) | How the code was written without copying the game's code |
| [THIRD_PARTY.md](THIRD_PARTY.md) | Third-party software, fonts and runtime components, and the names kept for interoperability |
| [CHANGELOG.md](CHANGELOG.md) | Versions |

The folders under `Assets/HS2Next/Runtime/` and `Assets/HS2Next/Shaders/` have READMEs of their own.

## Terms

- HS2Next is an unofficial project. It is not affiliated with, endorsed by or supported by ILLUSION or any successor of
  ILLUSION. "Honey Select 2" is named only to say whose data HS2Next reads.
- HS2Next contains no game data. No model, texture, animation, list or card of the game or of mods is part of this
  repository or of a release.
- It reads the copy of Honey Select 2 that you own, and your mods, where they are installed. It writes nothing into the
  game folder, except when you delete a character, outfit or pose file of the game from a list yourself: then it moves
  that file to the Windows recycle bin.
- It was made so that the characters, outfits and community mods you already own stay usable.
- For that, HS2Next has its own compatible shaders for the shaders of mods and asset packs (named `HS2Next/Compatible/…`),
  and it reads, with its own code, Sideloader's zipmod packages and the data that community plugins store in character
  cards, outfit cards, pose files and those mod packages. Neither is the original authors' work, and they have not endorsed or supported
  HS2Next; the end of File > Options > Advanced lists them.
- Honey Select 2 is a game for adults. Use HS2Next only where you may use the game itself.
- stp3 made HS2Next with AI coding tools: the tools wrote most of the code, and stp3 set the design, directed the work and
  checked it. stp3 dedicates the code to the public domain under CC0 1.0 ([LICENSE](LICENSE)): use, change and share it
  freely. This does not extend to Honey Select 2, its assets or other people's mods: the parts of them inside what you
  make with HS2Next (scenes, characters, captures) stay under their owners' terms.
- The release zip also contains runtime components of Unity, Microsoft and the Mono project (the engine, DirectX 12,
  DirectStorage, the .NET class libraries) under their own terms, which [THIRD_PARTY.md](THIRD_PARTY.md) names; by using
  HS2Next you accept those terms for them. CC0 covers only HS2Next's own code.
- HS2Next is provided as is, without warranty of any kind.

## Licence

HS2Next's own code: CC0 1.0 ([LICENSE](LICENSE)). The bundled Noto Sans CJK fonts: SIL Open Font License 1.1.
Newtonsoft.Json: MIT. The Unity runtime and the other components of the release zip: see [THIRD_PARTY.md](THIRD_PARTY.md).

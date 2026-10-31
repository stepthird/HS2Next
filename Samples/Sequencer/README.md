# Sample sequencer (HS2Next plugin)

A minimal sequencer, written as the starting point for HS2Next plugins. It shows the four things most plugins need,
working together:

- **commands**: eight bridge commands under the prefix `seq.`, owned by the plugin's GUID;
- **per-frame work**: the sequence plays in `IPluginUpdate.Update`, following the scene's pause and steps;
- **saving with the scene**: the sequence is stored as the scene's ext block of the plugin's namespace;
- **events**: it reacts to scene loads and clears, node removal and undo, and steps while paused.

It references only the engine-free `HS2Next.Api` and the host-provided `Newtonsoft.Json`: a sequencer needs no engine
types. Everything it does to the scene goes through bridge commands (`ctx.Call` inline for the tracks,
`ctx.CallAsync` for the timed events), so it uses exactly the vocabulary any HTTP client of the bridge uses.

License: CC0-1.0 (public domain dedication). Copy it, rename it, make it yours.

## Build

Requires the .NET SDK (any version that builds `netstandard2.1`). The project references `HS2Next.Api.dll` and
`Newtonsoft.Json.dll` without copying them (the app provides both):

```
dotnet build -c Release -p:HS2NextApi=<app>\HS2Next_Data\Managed -p:NewtonsoftJson=<app>\HS2Next_Data\Managed\Newtonsoft.Json.dll
```

Inside the HS2Next repository the defaults find both (`Library/ScriptAssemblies` and the package cache), and
`Tools/plugins.ps1 -Build` builds and stages it.

## Install

Copy `manifest.json` and `bin/Release/netstandard2.1/Stp3.SampleSequencer.dll` into
`<app>/Plugins/stp3.sample.sequencer/` (the folder name must equal the manifest's `guid`) and start the app. The bridge's
`bridge.version` lists the plugin; `help` lists its commands.

**Plugins are full-trust code.** A plugin runs inside the app with the app's rights; there is no sandbox. Install only
plugins whose source you trust.

## Commands

| Command | Arguments | Effect |
|---|---|---|
| `seq.set` | `sequence` (object) | validates and replaces the scene's sequence; stopped at 0, the first pose written at the next frame |
| `seq.get` | — | `{sequence (or null), state, time}` |
| `seq.play` | `from` (optional) | plays from `from`, else from the current time (from 0 when stopped or ended) |
| `seq.pause` | — | holds the current time |
| `seq.stop` | — | stopped at 0 |
| `seq.seek` | `t` | moves to `t` without firing events; a playing sequence goes on from there, otherwise it is paused there |
| `seq.status` | — | state, time, tracks (active or not), events fired / failed, writes done / busy / missing node / error, update times |
| `seq.measure` | `keys` (node ids), `frames` | measures the cost of the inline calls on a temporary sequence; without `keys` it reports the result |

None of them is undoable: the sequence is the plugin's state, not part of the scene's history. The track writes use
`undo: false`, so playing never fills the undo list.

## The sequence (data version 1)

```json
{
  "duration": 4.0,
  "loop": true,
  "autoplay": false,
  "tracks": [
    {"kind": "transform", "key": "<node id>", "ease": "linear",
     "keys": [{"t": 0.0, "pos": [0, 0, 0], "rot": [0, 0, 0]}, {"t": 4.0, "pos": [1, 0, 0], "rot": [0, 90, 0]}]},
    {"kind": "camera", "ease": "linear",
     "keys": [{"t": 0.0, "target": [0, 1, 0], "rot": [10, 180, 0], "dist": 3, "fov": 23}]}
  ],
  "events": [
    {"t": 2.0, "do": {"cmd": "obj.rename", "key": "<node id>", "name": "halfway"}, "note": "any text"}
  ]
}
```

- `duration` 0–3600 s; `loop` and `autoplay` (play after the scene loads) default to false.
- Up to 64 tracks: transform tracks (one per node; `pos` parent-local, `rot` Euler degrees) and at most one camera
  track (`target`, `rot`, `dist`, `fov` as `camera.set` takes them). Keys strictly ascending in `t`, 4096 keys in all.
- `ease`: `linear` blends positions and numbers linearly and rotations along the shortest arc (a 350° → 10° pair turns
  20°); `step` holds each key until the next one. At a key the key's own angles are written unchanged.
- Up to 1024 events: `do` is a bridge request without `id`; it is sent through the queue when playback passes `t`
  (not on seek). Events cannot call `seq.*` commands.
- Unknown members are refused, so a misspelt member does not go unnoticed. `seq.set` answers `bad_args` with the
  member path of the first problem in `detail.path`.

The plugin writes the sequence canonically (members in the order above, numbers in round-trip form), so saving and
loading gives back the same text.

## How it plays

Once per frame, before the scene applies its changes: a pending apply (after `seq.set`, `seq.seek`, `seq.stop`, a
load, or an undo that brought a node back) or the advancing time writes every active track with `obj.transform` /
`camera.set` (`undo: false`). Writes made there are visible in the same frame. While the scene is paused the frame
delta is 0 and the sequence holds; steps while paused advance it by their count × delta.

A write answered `busy` (someone holds a scene transaction open, e.g. a drag) is skipped and retried at the next frame.
A node that was deleted makes its track inactive; an undo that brings it back makes it active again. Event handlers
never change the scene themselves; they record what happened and the next frame acts.

## Files

- `src/SequencerPlugin.cs` — the entry type (`IPlugin`, `IPluginUpdate`, `IPluginSceneLoaded`) and the event handlers
- `src/Commands.cs` — the `seq.*` commands
- `src/Sequence.cs` — the model, its JSON form and validation
- `src/Playback.cs` — interpolation, the clock with its event crossings, the player (engine-free, tested without the app)
- `src/SequenceBlock.cs` — the scene's ext block
- `src/Measure.cs` — the cost measurement
- `../Sequencer.Tests/` — engine-free tests of the model and the player

# Writing HS2Next plugins

A plugin is a .NET assembly that HS2Next loads at start-up. It can add bridge commands, keep its own data in scenes,
characters and coordinates, react to scene events, run code every frame, supply shaders and, through the engine-side
assembly, hook into character builds, override materials and add UI. This guide covers the 1.0 plugin API. The sample
in [Samples/Sequencer](../Samples/Sequencer/README.md) uses most of it and is the quickest start.

**Plugins are full-trust code.** A plugin runs inside the app with the app's rights; there is no sandbox. Install only
plugins whose source you trust.

## The two API assemblies

| Assembly | References | What it gives |
| --- | --- | --- |
| `HS2Next.Api` | no engine types | `IPlugin` and the lifecycle interfaces, the plugin context, events, the bridge command contract (`HS2Next.Api.Bridge`), the ext-block contract (`HS2Next.Api.Save`), content ids, motion phases |
| `HS2Next.Api.Unity` | UnityEngine | the realized objects of scene nodes and characters, build hooks, material overrides, additional mosaic members, the UI registration (experimental) |

A plugin that needs no engine types references only `HS2Next.Api` (and `Newtonsoft.Json` if it wants a JSON library: the
app provides version 13 to every plugin). Both assemblies are in `HS2Next_Data/Managed/` of the app; inside the repository
the Editor's `Library/ScriptAssemblies` holds them. Do not ship them with the plugin.

## Folder and manifest

A plugin is a folder `<app>/Plugins/<guid>/` (next to `HS2Next.exe`; the project folder's `Plugins/` in the Editor) with
`manifest.json` and the assemblies it lists. Private dependencies go into `lib/` inside the folder; the loader resolves them
from there. The folder name must equal the manifest's `guid`, ignoring case.

```json
{
  "manifestVersion": 1,
  "guid": "example.myplugin",
  "name": "My plugin",
  "version": "1.0.0",
  "apiVersion": "1.0",
  "assemblies": ["Example.MyPlugin.dll"],
  "entryType": "Example.MyPlugin.Plugin",
  "commandPrefixes": ["myp."],
  "dependencies": [{"guid": "example.other", "minVersion": "1.2.0"}],
  "loadAfter": [],
  "loadBefore": [],
  "minAppVersion": "1.0.0",
  "author": "you",
  "license": "CC0-1.0",
  "url": "",
  "description": "What it does, in one sentence."
}
```

| Key | Required | Rule |
| --- | --- | --- |
| `manifestVersion` | yes | the integer `1` |
| `guid` | yes | reverse-DNS style: `[A-Za-z0-9][A-Za-z0-9._-]{0,127}` with at least one dot. `core`, `bridge` and names starting with `hs2.`, `hs2next.` or `illusion.` are reserved. The GUID owns the plugin's commands and its ext namespace |
| `name` | yes | 1–64 characters |
| `version` | yes | semver `MAJOR.MINOR.PATCH[-pre]` |
| `apiVersion` | yes | `MAJOR.MINOR` of the API the plugin was built against (see "Versions") |
| `assemblies` | yes | one or more file names in the plugin folder (no paths), each listed once |
| `entryType` | no | the full name of the `IPlugin` type; without it the loader takes the single public non-abstract `IPlugin` type with a public parameterless constructor |
| `commandPrefixes` | no | command-name prefixes ending with a dot (`"seq."`); every command the plugin registers must start with one, and a prefix belongs to one plugin |
| `dependencies` | no | `[{"guid", "minVersion"}]`: the plugin loads only after these, and only if they load with at least that version |
| `loadAfter`, `loadBefore` | no | order hints for plugins that are not dependencies; a hint that would make a cycle is dropped and reported |
| `minAppVersion` | no | the lowest app version (semver) |
| `author`, `license`, `url`, `description` | no | shown by `plugin.list` |

Unknown keys are ignored (and reported). A manifest that breaks a rule refuses the plugin with a code that `plugin.list`
shows and the log repeats: `plugin.badManifest`, `plugin.folderMismatch`, `plugin.duplicateGuid` (both plugins are
refused), `plugin.reservedGuid`, `plugin.apiVersion`, `plugin.appVersion`, `plugin.missingAssembly`,
`plugin.assemblyCollision`, `plugin.prefixTaken`, `plugin.dependency`, `plugin.dependencyCycle`, `plugin.disabled`,
`plugin.staleAssembly`, `plugin.noEntry`, `plugin.typeLoad`, `plugin.loadFailed`.

The settings `plugins.enabled`, `plugins.dir` and `plugins.disabled` (a list of GUIDs), the switch `-noPlugins` and the
environment variable `HS2NEXT_NO_PLUGINS=1` control loading.

## Lifecycle

```csharp
public sealed class Plugin : IPlugin, IPluginUpdate, IPluginSceneLoaded
{
    IPluginContext ctx;

    public void Load(IPluginContext context)        // once per process: register everything here
    {
        ctx = context;
        ctx.Commands.Register(/* a CommandSpec under "myp." */);
        ctx.Ext.Register(new MyBlockHandler());
        ctx.Events.SceneCleared += OnCleared;
    }

    public void SceneLoaded(SceneLoadInfo info) { } // after every new or loaded scene, and once at start (Initial)
    public void Update(in PluginFrame frame) { }    // once per frame
    public void Unload() { }                        // at quit, at the end of an Editor play session, or when faulted
}
```

- The host creates the entry type once through its public parameterless constructor and calls `Load` with the plugin's
  context in the start-up stage `plugins`, after the initial scene. Plugins load in dependency order (ties by GUID).
- `IPluginUpdate.Update` runs once per frame in load order, after the bridge has run this frame's requests and before the
  scene applies its changes, so writes made there show in the same frame. `frame.SceneDt` is 0 while the scene is paused;
  steps made while paused arrive as the `SceneStepped` event instead.
- `IPluginSceneLoaded.SceneLoaded` follows every new or loaded scene (the scene's ext blocks were read before) and comes
  once with `SceneLoadKind.Initial` right after all plugins loaded.
- Every call comes on the main thread. After `Unload` returns, the host removes everything the plugin registered through
  its context; the context then throws `ObjectDisposedException` except for `Guid` and `Log`.
- Mono does not unload assemblies: a disabled plugin stays in memory until the app restarts.

**Faults.** Every call into the plugin goes through a guard. An exception is logged once per hook and exception type, with
its stack, and never stops the caller. After 10 counted faults the plugin is faulted: its updates and events stop, its other
registrations are removed, `Unload` is tried once, and its commands answer `plugin_fault`.

## The context

`IPluginContext` is the plugin's view of the host:

| Member | What it is |
| --- | --- |
| `Guid`, `Name`, `Version`, `Directory` | from the manifest and the plugin folder (treat the folder as read-only: an update may replace it) |
| `DataDirectory` | `<UserData>/Plugins/<guid>/`, the plugin's own data, created on first use |
| `Host` | app and API versions, Editor / batch / player, whether the UI runs, the UserData folder |
| `Log` | Unity's log with the prefix `[plugin <guid>]`; the bridge's `log.tail` shows it |
| `Settings` | `<UserData>/Plugins/<guid>/settings.json` as JSON text; the plugin owns the schema; writes are atomic |
| `Commands` | the bridge's command registry, scoped: the owner is the GUID, names outside `commandPrefixes` are refused |
| `Frames`, `Readiness`, `Output` | the bridge's frame clock (scheduled and late actions), readiness stages (a plugin may declare its own), the output folder for captures and dumps |
| `Ext` | handlers for ext blocks of the plugin's namespaces |
| `Events` | typed main-thread events |
| `Clock` | the scene clock (time, frame delta, paused, frames, steps) |
| `Shaders` | shader replacements by name for materials loaded later |
| `Call(json)` | runs one bridge request now, on the main thread, and returns the reply text |
| `CallAsync(json)` | queues one bridge request behind the others; any command is allowed |

`Call` refuses `batch`, commands that may defer their reply, asynchronous commands and `waitFrames` (use `CallAsync`); a
command that changes the scene answers `busy` while someone else holds a scene transaction (a drag, a running batch) or
while a scene event is being delivered, so a plugin's writes never join another party's undo step. A plugin that writes
the scene on its own should pass `"undo": false` when the change is the plugin's state, not the user's.

## Commands

A plugin's commands are ordinary bridge commands: `help` lists them, HTTP clients and other plugins call them, and the
reply rules of [COMMANDS.md](COMMANDS.md) apply. A command is a `CommandSpec` (name, summary, arguments with types and
defaults, returned fields, flags) with a handler; handlers can reply at once, defer the reply to a later frame or run
asynchronously. Mark commands that change the scene with `CommandFlags.Mutates` and read-only ones with
`CommandFlags.ReadOnly`; commands that may change between versions with `CommandFlags.Experimental`. A plugin command's
owner is always the plugin's GUID; its help group defaults to the GUID and its `since` to the plugin's version.

## Events

`ctx.Events` (`IAppEvents`): `AppReady`, `SceneCleared`, `SceneLoaded`, `SceneSaving`, `SceneSaved`, `NodesAdded`,
`NodesRemoved`, `NodesMoved`, `NodeForgotten` (a deleted node can no longer come back by undo), `CharaBuilt`,
`PausedChanged`, `SceneStepped`, `Quitting`. Handlers run on the main thread after the host's own handlers. A throwing
handler is a fault of its plugin and does not stop the other handlers. A handler is never re-entered for the same plugin:
an event raised while it runs is delivered right after it returns. Change the scene from the next `Update`, not from inside
an event.

## Saving data with scenes, characters and coordinates

A plugin stores its data as ext blocks of its own namespaces: the GUID or `<guid>.<name>`. Register an `IExtBlockHandler`
with `ctx.Ext.Register`:

- `Scopes`: where the block lives: a character's appearance, a coordinate (also per accessory and per clothes part), a
  scene, or one scene node.
- `Read(IExtReadContext)` parses the block when a file loads: the inline JSON value and the block's files (under
  `ext/<namespace>/` in the container). Store the data; apply it later (`CharaBuilt`, `SceneLoaded`), not inside `Read`.
- `Write(IExtWriteContext)` writes the block when a file is saved: an inline JSON value and files (path grammar of the
  container, at most 200 characters). Writing nothing means "no block".
- `Version` (the data version the handler writes) is stored with the block; `Read` migrates older data. A block the handler refuses (for example a newer data
  version) is kept unchanged and written back as unknown, and so is every block whose plugin is not installed.
- `RequiredByDefault` marks data without which the content is incomplete or misleading; a loader without the plugin then
  reports it.
- A handler that also implements `IKkexConsumer` receives the HS2 plugin data imported from cards: while a character or
  coordinate file loads, `ReadKkex` is called once per imported entry whose plugin id is in `KkexIds`, after the handler's own
  `Read` of the same instance (also when the handler has no block there).

## Engine-side services

`ctx.Unity()` (an extension method of `HS2Next.Api.Unity`) returns `IUnityPluginServices`, or null in a host without an
engine:

- `NodeObject(id)`, `CharaRoot(id)`: the realized objects of scene nodes. Reading them is supported. Changing state that the
  scene model owns (transforms, visibility, materials outside the overrides) is not: the next apply or build may overwrite
  it.
- `BuildHooks`: code that runs at fixed points of every character build: `BodyMesh` (after the body is spawned, before the
  skin is composited: replace part meshes or materials, implant bones), `Textures` (after the plugin texture stage) and
  `Final` (after the final plugin stage). `Textures` and `Final` run again in the edits that redo that point, so a hook's
  result must depend only on the character's state.
- `Materials`: typed overrides of one material slot's float, colour, texture or shader, or a renderer's enabled flag.
- `AddMosaic(renderer)`: makes a renderer a mosaic member.
- `Ui` (`[Experimental]`): panels in a region of the Studio shell, inspector sections for a node kind, actions reachable
  through `ui.action` and shortcuts, with labels from the plugin's own strings.

None of these can turn the mosaic off, change its look or remove a member: overrides of mosaic slots are refused (a warning,
not a fault), the build hooks offer no stage after the mosaic, and the host restores a member's mosaic material after the
final hooks.

## Shaders

`ctx.Shaders.Supply(original, replacement)` maps a shader name to a replacement that the host can find by name (for example
one the plugin loaded from its own asset bundle) for materials loaded later. A later supply of the same name by another
plugin stacks on top; withdrawing restores the one below, and unloading withdraws all of a plugin's supplies. The mosaic
shaders cannot be supplied or replaced.

HS2Next's own replacements for the shaders of mods and asset packs are its compatible shaders, named
`HS2Next/Compatible/<rest>` (the end of File > Options > Advanced lists their families); its replacements for the game's and Unity's shaders are
named `HS2Next/<rest>`. Materials are always looked up by their original shader name, so a supply names the original.

## Versions

The API follows semantic versioning; `ApiVersion` (and the informational version of both assemblies) gives it, and the
assembly version is `MAJOR.0.0.0`, so a plugin binds across minor and patch releases.

- A plugin declares the `MAJOR.MINOR` it was built against in `apiVersion`. It loads when the host has the same major and
  a minor at least as high: a `1.0` plugin loads on every 1.x host, a `1.2` plugin needs a 1.2 or later host.
- Within a major version nothing public is removed or changed. Interfaces the host implements (the context, the services,
  the events) may gain members in a minor version; plugins only call them. Interfaces plugins implement (`IPlugin`,
  `IPluginUpdate`, `IPluginSceneLoaded`, `IExtBlockHandler`, `IKkexConsumer`, and `IBatchHook` and `IMotionStage`, which the
  freeze treats the same way) never change: a new capability comes as a new optional interface.
- Types and members marked `[Experimental]` are outside the promise and may change in any minor version.
- `Tools/api/` holds one listing, of the current version, in four files: the public types and members of `HS2Next.Api` and
  of `HS2Next.Api.Unity`, the commands with the protocol (error codes, envelope fields, headers), and the plugin manifest
  schema. The `ApiSurfaceTest` row compares the code with it, and a release that changes the version rewrites it, so the
  repository history shows what each version added.

## Testing a plugin

- Build it against the app's `Managed` folder and copy the folder into `<app>/Plugins/`; `plugin.list` shows its state,
  assemblies, commands, ext namespaces, handlers, faults and update times.
- In the repository, `Tools/plugins.ps1` builds plugin projects (`-Build <project folder>`, or `-Build all` for the sample
  and the test plugins) and stages them into a plugin folder with their private dependencies in `lib/` and their
  `strings.tsv`. It never writes through a junction and never deletes a folder.
- Engine-free logic can be tested without the app, as `Samples/Sequencer.Tests` does.

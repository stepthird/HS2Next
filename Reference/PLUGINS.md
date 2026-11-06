# Writing HS2Next plugins

A plugin is a .NET assembly that HS2Next loads at start-up. It can add bridge commands, keep its own data in scenes,
characters and coordinates, react to scene events and captures, run code every frame, add entries to the undo history,
show panels and inspector sections drawn with the app's own widgets, lay values over the scene for display, supply shaders
and, through the engine-side assembly, hook into character builds (skin layers, body-part replacement), override materials
and add UI of its own. This guide covers the 1.2 plugin API. It is the same text in [Japanese](PLUGINS_ja.md) and
[Simplified Chinese](PLUGINS_zh-Hans.md); [COMMANDS.md](COMMANDS.md) next to it lists every command of the app.

**Plugins are full-trust code.** A plugin runs inside the app with the app's rights; there is no sandbox. Install only
plugins whose source you trust.

## The plugin kit

The distribution zip holds the folder `PluginKit/` next to `HS2Next.exe`:

| Entry | What it is |
| --- | --- |
| `PLUGINS.md`, `PLUGINS_ja.md`, `PLUGINS_zh-Hans.md` | this guide |
| `COMMANDS.md` | the command reference (every command a plugin can call with `Request` or `Call`) |
| `HS2NextPlugin.props` | the build settings the projects share: one property, `HS2NextDir`, finds every reference |
| `PluginKit.sln` | the two samples; one `dotnet build` in this folder builds both |
| `NuGet.config` | clears every package source: the kit needs no package and no network |
| `Hello/` | an engine-free sample: one command, a call helper, an event handler |
| `HelloUi/` | an engine-free sample: a panel and an item inspector section declared as forms, with its own strings |
| `doc/` | the XML documentation of `HS2Next.Api` and `HS2Next.Api.Unity` (also next to the DLLs in `HS2Next_Data/Managed/`, where IDEs find it) |

Building the samples needs the .NET SDK 8 or later (`dotnet --list-sdks` shows it):

1. Open a terminal in the `PluginKit` folder and run `dotnet build`. Both samples build against the app's own API
   assemblies, and each one is copied into `out/<guid>/` (`out/stp3.sample.hello/`, `out/stp3.sample.helloui/`).
2. Copy those folders into the `Plugins` folder next to `HS2Next.exe` (create it the first time), so that you have
   `Plugins/stp3.sample.hello/manifest.json` and so on.
3. Start HS2Next. File > Options > Automation lists the plugins and their state; the bridge's `plugin.list` shows the
   details. `hello.greet {"name": "kit"}` answers `{"text": "Hello, kit!", "objects": …}`.

`HS2NextDir` defaults to the folder above the kit, which is the extracted app. A kit copied somewhere else passes the
app's folder: `dotnet build -p:HS2NextDir=<the folder of HS2Next.exe>` (an absolute path); without it the build stops with
"HS2Next.Api.dll not found under …". To start a plugin of your own, copy `Hello/` to a new folder, rename the project,
the assembly and the namespace, give the manifest your own `guid` and `commandPrefixes`, set `PluginGuid` in the project to
the same GUID, and add the project to the solution (`dotnet sln add <folder>/<name>.csproj`). The props file documents
its other properties: `HS2NextUseUnity=true` adds the engine-side references (`HS2Next.Api.Unity`,
`UnityEngine.CoreModule`, `UnityEngine.UIElementsModule`), `HS2NextUseJson=true` adds `Newtonsoft.Json`.

## The two API assemblies

| Assembly | References | What it gives |
| --- | --- | --- |
| `HS2Next.Api` | no engine types | `IPlugin` and the lifecycle interfaces, the plugin context, the call helpers, events, undo entries, runtime overrides, forms (`HS2Next.Api.Ui`), the bridge command contract (`HS2Next.Api.Bridge`), the ext-block contract (`HS2Next.Api.Save`), content ids, motion phases |
| `HS2Next.Api.Unity` | UnityEngine | the realized objects of scene nodes and characters, build hooks with skin layers and body-part replacement, rebuild requests, material overrides, additional mosaic members, the UI registration and the host's widgets (experimental) |

A plugin that needs no engine types references only `HS2Next.Api` (and `Newtonsoft.Json` if it wants a JSON library: the
app provides version 13 to every plugin). Both assemblies are in `HS2Next_Data/Managed/` of the app. Do not ship them
with the plugin; the app loads its own.

## Folder and manifest

A plugin is a folder `<app>/Plugins/<guid>/` (next to `HS2Next.exe`) with `manifest.json` and the assemblies it lists.
Private dependencies go into `lib/` inside the folder; the loader resolves them from there. The folder name must equal the
manifest's `guid`, ignoring case. A `strings.tsv` in the folder adds the plugin's own UI strings (below).

```json
{
  "manifestVersion": 1,
  "guid": "example.myplugin",
  "name": "My plugin",
  "version": "1.0.0",
  "apiVersion": "1.1",
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
environment variable `HS2NEXT_NO_PLUGINS=1` control loading. The plugin toggles of File > Options > Automation disable and
enable a plugin at once and record the choice in `plugins.disabled`; `plugins.enabled` applies at the next start.

## Lifecycle

```csharp
public sealed class Plugin : IPlugin, IPluginUpdate, IPluginSceneLoaded
{
    IPluginContext ctx;

    public void Load(IPluginContext context)        // register everything here
    {
        ctx = context;
        ctx.Commands.Register(/* a CommandSpec under "myp." */);
        ctx.Ext.Register(new MyBlockHandler());
        ctx.Events.SceneCleared += OnCleared;
    }

    public void SceneLoaded(SceneLoadInfo info) { } // after every new or loaded scene, and once with Initial after Load
    public void Update(in PluginFrame frame) { }    // once per frame
    public void Unload() { }                        // at quit, when disabled, when faulted, at the end of an Editor play session
}
```

- The host creates the entry type through its public parameterless constructor and calls `Load` with the plugin's
  context in the start-up stage `plugins`, after the initial scene. Plugins load in dependency order (ties by GUID).
- `IPluginUpdate.Update` runs once per frame in load order, after the bridge has run this frame's requests and before the
  scene applies its changes, so writes made there show in the same frame. `frame.SceneDt` is 0 while the scene is paused;
  steps made while paused arrive as the `SceneStepped` event instead.
- `IPluginSceneLoaded.SceneLoaded` follows every new or loaded scene (the scene's ext blocks were read before) and comes
  once with `SceneLoadKind.Initial` right after the plugin loaded.
- `IPluginScenePost.ScenePost` (`[Experimental]`) comes after all motion, once per frame and once per scene step (see
  "The scene post stage").
- Every call comes on the main thread. After `Unload` returns, the host removes everything the plugin registered through
  its context; the context then throws `ObjectDisposedException` except for `Guid`, `Log` and `IsActive`. `IsActive` is
  also false once the plugin is faulted.
- A plugin can be disabled and enabled again while the app runs (`plugin.disable`, `plugin.enable`, the Options toggles).
  Enabling creates a **new instance** of the entry type from the assembly already loaded and calls `Load` on it again,
  then `SceneLoaded` with `Initial` for the open scene and `AppReady`. So `Load` can run more than once per process, each
  time on a new instance.
- Mono does not unload assemblies: a disabled plugin's assembly stays in memory until the app restarts, and a DLL that
  was replaced in the plugin folder loads only after a restart (`plugin.enable` answers `plugin.staleAssembly`).

**Clean up in `Unload`.** Undo there every effect the plugin made outside its context: static fields, GameObjects and
components it created, Unity callbacks it subscribed, threads and timers it started, files it holds open, deferred replies
it still owes. A disabled plugin's assembly, its static state and its objects stay in the process, and a re-enabled plugin
starts from them. What the host removes itself: commands, event handlers, ext handlers, build hooks, FK providers, shader
supplies, UI registrations and forms, undo entries, runtime override slots, runtime skin layers, frame actions scheduled
through `ctx.Frames` and its frame clock handlers, strings. What stays: mosaic members the plugin added (nothing removes a
member), values it set through the material overrides (until that object is built again) and files it wrote.
The plugin's ext blocks in the open scene keep the values of the last load or save: unsaved plugin state is lost, file data
is not, and the new instance reads the open scene's blocks again before its `SceneLoaded`.

**Faults.** Every call into the plugin goes through a guard. An exception is logged once per hook and exception type, with
its stack, and never stops the caller. After 10 counted faults the plugin is faulted: its updates, post-stage calls and
events stop, its other registrations are removed, `Unload` is tried once, and its commands answer `plugin_fault`.
`plugin.enable` loads a faulted plugin again.

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
| `Frames`, `Readiness`, `Output` | the bridge's frame clock (scheduled and late actions; those of a plugin that unloads are dropped, not run; a running action counts as the plugin's code, so a disable answers `busy` meanwhile), readiness stages (a plugin may declare its own), the output folder for captures and dumps |
| `Ext` | handlers for ext blocks of the plugin's namespaces |
| `Events` | typed main-thread events |
| `Clock` | the scene clock (time, frame delta, paused, frames, steps) |
| `Shaders` | shader replacements by name for materials loaded later |
| `Request(command, args)`, `RequestAsync(…)` | call a command with an argument object and read a typed reply (below) |
| `Call(json)`, `CallAsync(json)` | the same with the request envelope as JSON text and the reply as text |
| `IsActive` | true from `Load` until `Unload` returns; never throws (for continuations that may run after a disable) |
| `History` (`[Experimental]`) | entries in the scene's undo history for changes of the plugin's own state |
| `Forms` (`[Experimental]`) | panels and inspector sections declared as forms and drawn by the app |
| `Overrides` (`[Experimental]`) | values laid over the scene for display, never saved (runtime overrides) |

## Calling commands

```csharp
ICommandReply r = ctx.Request("obj.transform", new { key = id, pos = new[] { 0.0, 1.0, 0.0 }, undo = false });
if (!r.Ok) ctx.Log.Warn(r.Code + ": " + r.Error);

var scene = ctx.Request("scene");
int objects = scene.Get<List<object>>("objects", new List<object>()).Count;

var req = new CommandRequest("chara.edit").Arg("key", id).Arg("path", "body.shape.height").Arg("value", 0.6);
req.Undo = true;
ctx.Request(req).ThrowIfError();
```

- `Request(command, args)` builds the envelope `{"cmd": command, …args}` and runs it at once on the main thread, as `Call`
  does; `RequestAsync` queues it behind the other requests, as `CallAsync` does (any thread, any command). The arguments
  are an object whose public properties and fields become members (anonymous types, classes), an
  `IDictionary<string, object>` (or any sequence of string-keyed pairs), a Newtonsoft `JObject`, or null. They are written
  as the bridge writes its replies: member names in camelCase (`Pos` → `pos`; `[BridgeArg("name")]` renames,
  `[BridgeIgnore]` leaves out), enums by name, floats round-trip (NaN as null), Unity vectors and colours as arrays, nulls
  written; so numbers arrive exactly as an HTTP client's. A string, number, bool or array as `args`, or a member named
  `cmd`, throws `ArgumentException` (use `Call` for envelope text).
- Envelope members (`id`, `strict`, `undo`, and for the queued form `waitFrames`, `timeout`) may be members of `args` or
  properties of a `CommandRequest`. `CommandRequest.Arg` keeps the order of the arguments, replaces a value given twice in
  place, and refuses the envelope names (`cmd`, `id`, `waitFrames`, `timeout`, `strict`, `as`, `undo`): set those through
  the properties (`WaitFrames` 0 is not sent).
- The reply (`ICommandReply`): `Ok`, `Command`, `Code` and `Error` (null when ok), `Warnings`, `Json` (the reply text
  exactly as `Call` returns it), `Has(member)` (present and not null), `Get<T>(member, fallback)` (absent or null → the
  fallback; numbers convert to `int`, `long`, `float`, `double`; arrays to `T[]` or `List<T>`; objects to
  `Dictionary<string, object>` or a class; a value that does not convert throws `FormatException` naming the member; a
  value read as `object` is a plain .NET value: `string`, `long`, `double`, `bool`, null, `List<object>` or
  `Dictionary<string, object>`), `GetJson(member)`, `As<T>()` (the whole reply into a class), `ThrowIfError()` (a
  `BridgeException` with the code, the message and the detail).
- A command error never throws: read `Ok`. `Request` and `Call` give the same reply text for the same envelope.

The inline forms (`Request`, `Call`) refuse `batch`, commands that may defer their reply, asynchronous commands and
`waitFrames` (use the queued forms). A command that changes the scene answers `busy` while someone else holds a scene
transaction (a drag, a running batch), while a scene event or a capture event is being delivered and inside an undo or
redo, so a plugin's writes never join another party's undo step. A plugin that writes the scene on its own should pass
`"undo": false` when the change is the plugin's state, not the user's.

## Commands

A plugin's commands are ordinary bridge commands: `help` lists them, HTTP clients and other plugins call them, and the
reply rules of [COMMANDS.md](COMMANDS.md) apply. A command is a `CommandSpec` (name, summary, arguments with types and
defaults, returned fields, flags) with a handler; handlers can reply at once, defer the reply to a later frame or run
asynchronously. Mark commands that change the scene with `CommandFlags.Mutates` and read-only ones with
`CommandFlags.ReadOnly`; commands that may change between versions with `CommandFlags.Experimental`. A plugin command's
owner is always the plugin's GUID; its help group defaults to the GUID and its `since` to the plugin's version.

The Hello sample in full:

```csharp
public sealed class HelloPlugin : IPlugin
{
    IPluginContext ctx;

    public void Load(IPluginContext context)
    {
        ctx = context;
        ctx.Commands.Register(new CommandSpec("hello.greet", "Greets and counts the scene's objects.")
            .Arg("name", ArgTypes.String, "who to greet", defaultValue: "world")
            .Return("text", ArgTypes.String, "the greeting")
            .Return("objects", ArgTypes.Int, "objects in the scene")
            .WithFlags(CommandFlags.ReadOnly), Greet);
        ctx.Events.SceneLoaded += info => ctx.Log.Info("scene " + info.Kind + " with " + info.Nodes + " nodes");
    }

    object Greet(ICommandContext c)
    {
        var scene = ctx.Request("scene");
        int n = scene.Ok ? scene.Get<List<object>>("objects", new List<object>()).Count : 0;
        return new BridgeReply().With("text", "Hello, " + c.Args.Get("name", "world") + "!").With("objects", n);
    }

    public void Unload() { }   // the host removes the command and the event handler
}
```

## Events

`ctx.Events` (`IAppEvents`): `AppReady`, `SceneCleared`, `SceneLoaded`, `SceneSaving`, `SceneSaved`, `NodesAdded`,
`NodesRemoved`, `NodesMoved`, `NodeForgotten` (a deleted node can no longer come back by undo), `CharaBuilt`,
`PausedChanged`, `SceneStepped`, `Quitting`, and the capture events below. Handlers run on the main thread after the host's
own handlers. A throwing handler is a fault of its plugin and does not stop the other handlers. A handler is never
re-entered for the same plugin: an event raised while it runs is delivered right after it returns. Change the scene from
the next `Update`, not from inside an event.

**Captures.** Three events follow every screenshot (F11, the toolbar, the screenshot dialog, `ui.action
capture.screenshot`, the `screenshot` command), every `capture` command and every frame of a frame recording
(`record.start`, Export frames in the Timeline panel):

| Event | When | Use it to |
| --- | --- | --- |
| `CaptureStarting` (`CaptureInfo`) | on the main thread, right before the render | hide the plugin's own overlays (objects it created) |
| `CaptureRendered` (`CaptureInfo`) | right after the render, in the same frame (also when the render failed) | show them again |
| `CaptureFinished` (`CaptureResultInfo`) | after the file was written or the capture failed: a few frames later for a screenshot and a recorded frame, in the same frame for `capture` | read the result |

`CaptureInfo`: `Id` (from 1 per run), `Kind` (`Screenshot`, `Capture`, `Record`), `Origin` (`Ui`, `Command`), `Width`,
`Height` (the output), `Supersample` (1, 2, 4), `RenderWidth`, `RenderHeight`, `DrawsFrame` (the scene's frame image is drawn),
`FrameNumber`, `Path` (the file it writes). `CaptureResultInfo`: `Capture`, `Ok`, `Path` (null on failure), `Bytes`, `Error`,
`Milliseconds`. The three events of one capture carry the same `CaptureInfo` object. Every `CaptureStarting` is followed by
exactly one `CaptureRendered` and one `CaptureFinished`; a request
refused before anything renders (a bad size, the pixel limit, a screenshot still in flight) raises none. While a capture
event is delivered, scene changes through `Request` and `Call` answer `busy`: hide your own objects directly. Thumbnails
of scenes, characters and coordinates raise no capture event.

A frame recording raises the three events for every frame it writes, in frame order: `Kind` is `Record`, `Origin` is
`Command` for `record.start` and `Ui` for the dialog, `Path` is the frame's file. Frames of the global pause are not
written and raise nothing. While it records, the scene advances by one fixed step per frame whatever the real time
(`frame.SceneDt` is 1 / fps): a plugin that moves its objects by the scene clock is recorded at that step, one that uses
real time is not.

## Saving data with scenes, characters and coordinates

A plugin stores its data as ext blocks of its own namespaces: the GUID or `<guid>.<name>`. Register an `IExtBlockHandler`
with `ctx.Ext.Register`:

- `Scopes`: where the block lives: a character's appearance, a coordinate (also per accessory and per clothes part), a
  scene, or one scene node.
- `Read(IExtReadContext)` parses the block when a file loads: the inline JSON value and the block's files (under
  `ext/<namespace>/` in the container). Store the data; apply it later (`CharaBuilt`, `SceneLoaded`), not inside `Read`.
- `Write(IExtWriteContext)` writes the block when a file is saved: an inline JSON value and files (path grammar of the
  container, at most 200 characters). Writing nothing means "no block".
- `Version` (the data version the handler writes) is stored with the block; `Read` migrates older data. A block the
  handler refuses (for example a newer data version) is kept unchanged and written back as unknown, and so is every block
  whose plugin is not installed or is disabled.
- `RequiredByDefault` marks data without which the content is incomplete or misleading; a loader without the plugin then
  reports it.
- A handler that also implements `IKkexConsumer` receives the HS2 plugin data imported from cards: while a character or
  coordinate file loads, `ReadKkex` is called once per imported entry whose plugin id is in `KkexIds`, after the handler's own
  `Read` of the same instance (also when the handler has no block there).

The Sequencer sample of the source repository (`Samples/Sequencer`) keeps a sequence in the scene's block and is the
larger example of this.

## Forms (experimental)

A form is a panel or an inspector section that the plugin declares as data and the app draws with its own widgets: the
same sliders, colour pickers, choices and pickers as the app's inspector. Values come from the plugin's read command and go
back through its write command. Everything in `HS2Next.Api.Ui` and `ctx.Forms` is `[Experimental]`. The HelloUi sample of
the kit is a complete example.

```csharp
var form = new UiForm("helloui.read", "helloui.write");             // Undo is true: the app records the undo entries
form.Rows.Add(UiRow.Float("size", "plugin.stp3.sample.helloui.size", 0, 10, defaultValue: 1));
form.Rows.Add(UiRow.Bool("enabled", "plugin.stp3.sample.helloui.enabled"));
form.Rows.Add(UiRow.Button("reset", "plugin.stp3.sample.helloui.reset", "helloui.reset"));
ctx.Forms.Panel("settings", "plugin.stp3.sample.helloui.panel", "bottom", form);

var tag = new UiForm("helloui.read", "helloui.write");
tag.Rows.Add(UiRow.Float("weight", "plugin.stp3.sample.helloui.weight", 0, 1, defaultValue: 0));
ctx.Forms.Section("item", "tag", "plugin.stp3.sample.helloui.section", tag);   // one value per item node
```

**The two commands.** Register them in `Load` under the plugin's own prefix. The read gets `{"form": <id>, "node": <node id
or null>}` (null for a panel, the inspected node for a section: keep a section's values by node id) and answers `values`.
The write gets `{"form", "node", "key", "value", "phase"}` and answers ok (any reply) or an error whose message the app
shows as a notice. The write changes the plugin's own state, not the scene: do not flag it `CommandFlags.Mutates` (the
`undo` and `redo` writes run while the scene applies the step, and there a command that changes the scene answers `busy`);
the read may be `CommandFlags.ReadOnly`.

- **Registration.** `ctx.Forms.Panel(id, titleKey, region, form, order)` (region `left`, `right` or `bottom`; the title is
  the panel's tab) and `ctx.Forms.Section(kind, id, titleKey, form, order)` (kind `chara`, `item`, `light`, `camera`,
  `folder` or `route`: a section of the inspector for nodes of that kind); `Remove(id)`; `Refresh(id, nodeId)` reads again
  now (after the plugin changed a value by other means, for example through its own command; a null node: every node
  shown). Ids are the plugin's own; the app keeps them as `<guid>/<id>`. The form is checked and **copied** at registration:
  later changes to the `UiForm` or its rows have no effect. `Available` is false where no UI runs (batch players, `-noUi`):
  forms are then checked and counted in `plugin.list` (`ui.forms`) but not drawn. Everything is removed when the plugin
  unloads or is disabled.
- **Rows.** `UiForm(readCommand, writeCommand)` (`Undo`, `Rows`); `UiChoice(value, labelKey)`. `UiRow` has a factory per
  kind: `Float`, `Int` (whole numbers), `Bool`, `Color` (alpha on by default), `Vec2`, `Vec3`, `Enum`, `Ref`, `File`,
  `Text`, `Node`, `Button`, `AttachPoint`, `PartList`, `Push` (a ColorHdr row is a `UiRow` with that `Kind`); and settable
  members: `Key` (unique in the form: the value's name), `LabelKey`, `TooltipKey`, `Min` and `Max` (the slider range),
  `TypedMin` and `TypedMax` (typed values are clamped to these), `Whole`, `Default` (the value of the reset button; null: no
  reset button), `Format`, `Scale` (the display scale), `Grid`, `GridTruncate`, `Wrap360`, `Nullable`, `ReadOnly`,
  `ColorAlpha`, `Choices` (Enum rows), `ChoicesCommand` (Ref and Node rows: a command answering
  `{"choices": [{"value", "label"}]}`), `Command` and `CommandArgs` (Button rows), `CategoryFemale` and `CategoryMale`
  (PartList rows: a list category of the character editor), `PushSource` (Push rows: `facePreset`, `layout:6`,
  `layout:7`, `colour:336` or `colour:305`). `RowKind` has the names and values of the app's own row kinds; `HairBundles`
  is reserved and refused.
- **Checks at registration** (`ArgumentException`, nothing registered; a null form: `ArgumentNullException`): an empty or
  duplicate id (also one already shown in the UI); a region or kind outside the lists; an empty read command; a read,
  write, button or choices command outside the plugin's `commandPrefixes` (also one equal to a prefix); a null row; an
  empty or duplicate row key; `Min` above `Max`; an Enum row without choices; a Button row without a command; an editable
  row (any kind but Button, not `ReadOnly`) without a write command; a PartList row without a category; a Push row whose
  source is not one of the list; a `HairBundles` row; an undefined kind; a `Default` or `CommandArgs` value that is not a
  JSON value.

The app calls the plugin's commands inline (a form command that defers its reply or runs asynchronously is refused and the
form shows the error):

| Call | Envelope the app sends | Reply the app reads |
| --- | --- | --- |
| read | `{"cmd": <ReadCommand>, "form": <id>, "node": <node id or null>}` | `values` (row key → value), optional `hidden` and `disabled` (lists of keys), `choices` (key → `[{"value", "label"}]`, replaces the row's choices) |
| write | `{"cmd": <WriteCommand>, "form", "node", "key", "value", "phase", "undo": false}` with phase `preview`, `commit`, `cancel`, `undo` or `redo` | ok, or an error whose message the app shows as a notice |
| button | `{"cmd": <row.Command>, "form", "node", "key"}` and the row's `CommandArgs` | ok or an error (a notice) |
| choices | `{"cmd": <row.ChoicesCommand>, "form", "node", "key"}` | `choices: [{"value", "label"}]` |

Values in JSON: Float and Int a number; Bool a bool; Color `[r, g, b, a]` (four numbers from 0 to 1); ColorHdr
`{"color": [r, g, b, a], "intensity": n}`; Vec2 and Vec3 `[x, y]` and `[x, y, z]`; Enum the choice's value (a string); Ref,
Node, File, Text and AttachPoint a string or null; PartList the part's `"guid#id"` or null; Button and Push none (a click
on a Push row's cell sends a `commit` whose value is the cell's `"guid#id"`). A value missing from `values` shows "—"; a
value of the wrong shape shows "—", marks the row and is logged once (`[plugin <guid>] form <id> row <key>: …`). A failed
read shows "The values could not be read: <code>: <message>" in place of the rows, which stay disabled until the next good
read.

- **What the user sees.** The rows are the inspector's own widgets: a number slider with its field and a reset button
  (`Default`), vector fields, a toggle, segmented buttons (up to four choices) or a dropdown, a colour swatch with the
  app's colour picker, a reference picker (choices from `ChoicesCommand` or from the read's `choices`; a Node row without a
  `ChoicesCommand` lists the scene's nodes), the image file picker, a text field (sent on Enter or when it loses the
  focus), a button, the attach-point picker, the character editor's thumbnail list of the row's category (by the sex of the
  section's character, else the female list) and the character editor's push grid.
- **Writes and undo.** A drag sends `preview` writes while it moves (at most one per frame), `commit` on release and
  `cancel` on Esc (with the value from before the drag); a typed value, a toggle, a choice or a pick sends one `commit`.
  Every phase carries the value to show. With `UiForm.Undo` true (the default) the app records, in the plugin's name, one
  undo entry for each commit that changed the value (a whole drag is one entry), labelled with the row's label: its undo
  writes the value read before the edit with phase `undo`, its redo the committed value with phase `redo`; typed values of
  one row merge into one entry. The write command must then not record its own undo entry for these writes (the envelope
  says `"undo": false`). With `Undo` false the app records nothing.
- **Reads.** When the form is first drawn, after every `commit`, `cancel`, `undo`, `redo` and button, on `Refresh`, after
  `SceneLoaded` and `SceneCleared`, and for sections when the inspected node changes; at most once per form and node per
  frame.
- **Labels.** Label, title, tooltip and choice keys are keys of the plugin's strings: a `strings.tsv` in the plugin folder
  (columns `key`, `en`, `ja`, `zh-Hans`, `note`; keys start with `plugin.<guid>.`), added before `Load`, or `Ui.AddStrings`
  on the engine side. A key without a string shows the key itself. A choice label in a reply is such a key when it starts
  with `plugin.<guid>.`, else plain text.
- **Element names** (for `ui.query` and `ui.input` in tests): the form `plugin-form-<guid>/<id>`, its rows
  `plugin-form-<guid>/<id>-<key>` (with parts such as `-field`, `-slider`, `-reset`, `-toggle`, `-dropdown`, `-swatch`,
  `-text`, `-button`), its error line `…-error`; a panel's tab `tab-<guid>/<id>`.

## Widgets for engine-side UI (experimental)

`ctx.Unity().Ui` registers panels, inspector sections and actions built from UI Toolkit elements (`IPluginUi`). Its
`Widgets` (`IUiWidgets`, null without the UI) gives such a plugin the app's own widgets, wired to the plugin's callbacks
(every callback runs inside the plugin's guard, and none after the plugin unloaded): `Number` (a slider with drag phases),
`Vector`, `Toggle`, `Choice`, `Color`, `Ref`, `File`, `Text`, `Button`, `AttachPoint`, each returning an `IUiWidget`
(`Element`, `SetValue(value, mixed)` shows a value without calling back, `Enabled` to read and set); `Toast(text, level)`
(plain text); `Dialog` (its callback gets the button's index, -1 when it was closed another way), `Confirm` and `Prompt`
(null when cancelled); `Menu(items, anchor)` with `UiMenuItem(labelKey, run)` entries (`Enabled` true by default). The forms
above are drawn with the same widgets. Element names: `plugin-widget-<labelKey>` with the same part suffixes, a dialog
`dialog-plugin-<guid>` with buttons `dialog-button-<index>`, a menu `plugin-menu-<guid>` with items
`menu-item-plugin-<guid>-<index>`.

## Undo entries (experimental)

`ctx.History` (`IPluginHistory`) puts a change of the plugin's own state into the scene's undo history, next to the
entries of the commands the plugin called:

```csharp
float before = weight, after = weight + 1;
weight = after;                                        // the change is made first
ctx.History.Record("plugin.example.myplugin.weight",  // a key of the plugin's strings, or plain text
    undo: () => weight = before, redo: () => weight = after, mergeKey: "weight");
```

- `Record(label, undo, redo, mergeKey)` records a change the plugin **has already made**; `Record(label, beforeJson,
  afterJson, apply, mergeKey)` does the same with two states (undo calls `apply(beforeJson)`, redo `apply(afterJson)`).
  The result is a `HistoryRecordResult`: `Recorded`, `Merged`, `Busy` or `NoScene`. `CanRecord` tells whether a record
  would be accepted now; `Count` is the plugin's entries in the undo and redo lists. Main thread only; a null callback or
  `apply` throws `ArgumentNullException`.
- The entry joins the same list as the app's own: the redo list is dropped, the 200-entry limit applies, the scene shows
  unsaved changes, and undo and redo (the keys, the menu, the bridge's `undo` / `redo`) step through the plugin's entries and
  the app's in the order they were made. A label that starts with `plugin.<guid>.` is looked up in the plugin's strings.
- A record is refused with `Busy` while a scene transaction is open (a drag, a client's batch, also when the plugin's own
  command runs inside it), while the scene applies a command or delivers an event, and inside an undo or redo callback.
- A record with the same non-null `mergeKey` as the newest entry merges into it while that entry is the plugin's and
  nothing was undone since: the first undo (or `beforeJson`) stays, the newest redo (or `afterJson`) wins.
- Undo and redo callbacks restore the plugin's own state only (scene writes from inside them answer `busy`). A throwing
  callback counts as a fault of the plugin; the entry still moves to the other list, the reply of `undo` / `redo` carries a
  warning, and the rest of the history is kept.
- When the plugin is disabled, faults or unloads, its entries leave both lists at once; the others keep their order. A new
  or loaded scene clears the history. Entries are never saved.

## Runtime overrides (experimental)

`ctx.Overrides` (`IRuntimeOverrides`) lays values over the scene for display without changing the scene. A **slot**
stands for one value of the scene model: a node's position, rotation or scale, a character's FK bone rotation, animation
row, animation speed or time, eye and mouth patterns and openness, an item's colour, a light's colour, the camera or the
depth of field; or for a character's bone or IK target. While a slot holds a value and wins, the app shows that value
instead of the model's; the model keeps its own value, which shows again when the slot is cleared or released. The app's
Timeline and its constraints lay their values through the same slots. Everything here is `[Experimental]`.

```csharp
IOverrideSlot lift;                                     // one item's position, shown but never saved

object Lift(ICommandContext c)                          // myp.lift {"key": "<item id>"}
{
    lift?.Release();
    lift = ctx.Overrides.Acquire(c.Args.Require<string>("key"), "transform.pos");   // priority 0
    if (lift == null) throw new BridgeException(BridgeError.NotFound("no node with that id", "key"));
    return new BridgeReply();
}

public void Update(in PluginFrame frame)                // before the scene applies: it shows in this frame
{
    if (lift == null || lift.IsReleased) return;        // a new or loaded scene released it
    lift.Set(0, 1 + 0.25 * Math.Sin(2 * frame.SceneTime), 0);   // the scene clock: pauses and recordings follow it
}
```

The command `myp.lift` changes nothing that is saved, so it is not flagged `CommandFlags.Mutates`.

- **Slots.** `Acquire(nodeId, path, priority)` returns the plugin's slot of one channel: a node id and a path (the node id
  is null for the scene-wide `camera` and `effects.depthOfField`). A plugin holds at most one slot per channel; acquiring
  the same channel again returns the same slot with the new priority. A slot starts without a value. `Acquire` returns null
  when the host has no scene (`Available` false) or when the id names no node, not even a deleted one that undo could bring
  back. An unknown path, a path the node's kind does not have (`chara.*` on an item), a node path without a node id and a
  scene-wide path with one throw `ArgumentException`.

| Path | Nodes | Value (`Kind`) and what shows |
| --- | --- | --- |
| `transform.pos`, `transform.rot` | characters, items, lights, folders, routes, cameras | `Vector3`; `Rotation` (the local rotation) |
| `transform.scale` | characters, items | `Vector3`, meant as the model's scale |
| `chara.fk.bones["<bone>"]` | characters | `Rotation`, the bone's local rotation; like an FK value of the model it shows only where the FK pose writes the bone (FK on, the bone's group on, IK not solving it) |
| `chara.anime.ref` | characters | `Animation`: group, category, number (the source optional); the row is loaded when it differs from the row playing |
| `chara.anime.speed` | characters | `Number`, at least 0 |
| `chara.anime.time` | characters | `Number`, the normalized time (not wrapped): the character is posed at that time at once, also at speed 0, and stays at it while the slot has a value |
| `chara.face.eyes`, `chara.face.mouth` | characters | `Integer`, the pattern id |
| `chara.face.eyesOpen`, `chara.face.mouthOpen` | characters | `Number` |
| `chara.bone["<address>"].pos`, `chara.bone["<address>"].scale` | characters | `Vector3`, the bone's local position or scale |
| `chara.bone["<address>"].rot` | characters | `Rotation`, the bone's local rotation |
| `chara.ik.targets.<Name>.pos` | characters | `Vector3`, the target's position local to the character's root |
| `chara.ik.targets.<Name>.rot` | characters | `Rotation`, local to the character's root; hands and feet only |
| `item.colors[i].main` (i 0–3) | items | `Color` |
| `light.color` | lights | `Color` |
| `camera` | the scene | `Camera`: target, rotation, distance, field of view; above the scene's view and an active camera node |
| `effects.depthOfField` | the scene | `DepthOfField`, in place of the scene's depth of field |

- **Bones and IK targets.** `<address>` is the address the Advanced pose uses: `<name>` for a bone of the body or the head,
  `hair:<k>/<name>[#n]`, `clothes:<k>/<name>[#n]`, `acc:<id>/<name>[#n]`. `<Name>` is one of the 13 IK targets:
  `Body`, `LeftShoulder`, `LeftArmChain`, `LeftHand`, `RightShoulder`, `RightArmChain`, `RightHand`, `LeftThigh`,
  `LeftLegChain`, `LeftFoot`, `RightThigh`, `RightLegChain`, `RightFoot`. A bad address or name, or `.rot` on a target
  other than a hand or a foot, throws `ArgumentException` at `Acquire`. A bone value is written after FK, needs no FK group,
  and the Advanced pose's corrections stay on top of it. It has no effect while the address does not resolve on the built
  character, or on a bone another stage writes later (the eye look, the mouth width, joint correction, the bust). Mosaic
  parts are not editable: a slot on such a bone writes nothing. An IK target's value has no effect while that target's group is not
  applied; a slot never switches IK on. After `Clear` or `Release` the next motion step shows the pose without the value.
- **Values** are in the model's units. `Set(v)` takes a Number or an Integer (rounded); `Set(x, y, z)` a vector, a rotation
  as Euler angles in degrees (Unity's order: Z, then X, then Y), a colour with alpha 1 or an animation row (group,
  category, number); `Set(x, y, z, w)` a quaternion or a colour with alpha; `Set(double[])` any kind as its numbers (a
  camera 8: target, Euler rotation, distance, field of view; a depth of field 7: enabled as 0 or 1, focal size, aperture,
  blur, focus point); `SetJson(json)` any kind as JSON: a number, `[x, y, z]`, `[x, y, z, w]`, `[r, g, b, a]`,
  `{"source", "group", "category", "no"}` (`source` optional),
  `{"target": [x, y, z], "rot": [x, y, z], "dist": d, "fov": f}` (all four),
  `{"enabled", "focus", "focalSize", "aperture", "blur", "focusTo", "focusBlend"}` (without `focus` the pivot; the
  last three optional). Colours are the stored values (sRGB-encoded, straight alpha, not clamped). A form that does not
  fit the kind (also a JSON object with a member its form does not name), or a number that is not finite, throws
  `ArgumentException` naming the path and the forms it takes (an animation speed counts NaN and negative values as 0). A
  value equal to the current one changes nothing. `Clear()` removes the value and keeps the slot; `Release()` (or
  `Dispose()`) gives the slot up. Every setter returns false once the slot is released.
- **Depth-of-field focus.** `null` or `"pivot"` (the pivot of the camera as shown), `{"node": id}` (a character between the
  eyes, else its head; another node its position), `{"node": id, "bone": name, "offset": [x, y, z]}` (the transform of that
  name under the node, plus the offset in world axes), `{"point": [x, y, z]}` (a world position) or `{"dist": d}` (d ahead
  of the camera). A node or bone that cannot be found acts as the pivot. `focusTo` (a second focus) and `focusBlend` (0 to
  1) blend two foci. `blur` above 0 is the largest blur radius on a 1080-line frame; 0 or absent keeps the renderer's own.
- **Priorities.** Among the slots of a channel that hold a value, the highest priority wins; at equal priority the slot
  acquired last wins. The Timeline's slots have priority 0; the Timeline acquires a channel's slot when it first shows a
  value there after a play, a seek or an edit of its tracks, and releases them all when it stops. So a plugin slot of
  priority 0 wins over the Timeline only if the Timeline already showed that channel when the plugin acquired its slot;
  priority -1 yields to the Timeline, 1 wins whatever the order. The constraints' slots have priority 10: a plugin slot
  stays above a constraint with priority 11 or more, and at 10 the slot acquired last wins. `IsWinning` tells whether the
  slot's value is the one shown.
- **Lifetime.** A slot lives until the plugin releases it (`Release`, `Dispose`, `ReleaseAll`). The app releases every slot
  of the plugin when the plugin is disabled, faults or unloads; every slot of every owner when a new scene is made or a
  scene is loaded (acquire again in `SceneLoaded`); and the slots of a node that is forgotten (its id can never come back).
  A deleted node keeps its slots (`TargetExists` is false), and they show again when an undo brings the node back. A
  character that is built again keeps its slots and shows them on the new body. A released slot keeps answering its read
  members.
- **When it shows.** A position, rotation, scale, colour, face value, animation speed or time shows when it is set and at
  every later apply of that model value, so a slot set in `Update` shows in the same frame. An animation row loads at the
  end of the frame's scene update, an FK bone, a bone and an IK target at the next motion step, the camera and the depth of
  field in the frame's late update, before rendering. Screenshots, captures and recordings show what the slots show.
- **Nothing is saved or undone.** Setting a slot runs no command, records no undo entry, does not mark the scene as changed
  and is never written to a file: a save writes the model's values (also the animation time of a character whose animation
  a slot drives, and the neck and eye directions of a character with an FK bone slot, are saved as the model has them). The
  user's edits still go to the model: the inspector shows the model's value with a mark that names the owner, a gizmo drag
  changes the model while the view keeps showing the slot, and while a plugin's slot holds the camera the viewport's camera
  controls are off and the viewport names the plugin that drives the camera.
- **Cost.** A numeric setter allocates nothing and takes about a microsecond; `SetJson` parses its text and allocates.
  Setting slots every frame is the intended use: far cheaper than a command every frame, and it leaves the saved scene alone.
- **Threads.** `Acquire`, `List`, `ReleaseAll`, the setters, `Clear` and `Release` run on the main thread only
  (`InvalidOperationException` otherwise). `Available` and a slot's read members (`NodeId`, `Path`, `Kind`, `Priority`,
  `IsReleased`, `HasValue`, `IsWinning`, `TargetExists`) answer on any thread. After the plugin unloads, `ctx.Overrides`
  throws `ObjectDisposedException` like the rest of the context.
- **Listing.** `List(nodeId)` gives the slots of every owner that hold a value (all of them, or one node's) as
  `OverrideInfo`: `NodeId`, `Path`, `Owner` (`timeline`, `constraints` or a plugin's GUID), `Priority`, `Winning`, `Effective`
  (false when the slot does not win or its value has no effect now: an FK bone whose group is off or that the body lacks, a
  bone or an IK target in one of the cases above, a focus that cannot be found), `TargetExists`. The bridge command
  `overrides.list {key?}` answers the same with the values; `camera.get`
  and `camera.state` describe the camera as shown and name the winning owner in `overridden`, `effects.state` in
  `realized.depthOfField.overridden`; `plugin.list` lists each plugin's slots (`overrides`) and `plugin.disable` answers how
  many it released.

## The scene post stage (experimental)

A plugin whose entry type also implements `IPluginScenePost` gets one call after all motion: after every character, every
item and the constraints have moved, and before world-scale enforcement, the camera and the depth of field. What it reads
there is the final pose of the frame. Everything here is `[Experimental]`.

```csharp
public sealed class Plugin : IPlugin, IPluginScenePost
{
    IPluginContext ctx;
    IOverrideSlot marker;                               // an item at the top of the tree: its position, shown but never saved
    Transform hand;

    public void Load(IPluginContext context) => ctx = context;
    public void Unload() { }

    void Follow(string item, string chara)              // from one of the plugin's commands
    {
        marker = ctx.Overrides.Acquire(item, "transform.pos");
        hand = Array.Find(ctx.Unity().CharaRoot(chara).GetComponentsInChildren<Transform>(), t => t.name == "cf_J_Hand_R");
    }

    public void ScenePost(in PluginFrame frame)         // after all motion: the bone's final value
    {
        if (marker == null || marker.IsReleased || !hand) return;   // !hand: the character was built again
        Vector3 p = hand.position;
        marker.Set(p.x, p.y, p.z);                      // a node's slot shows in this frame
    }
}
```

- **When.** Once per frame and once per scene step, in plugin load order, on the main thread. `frame.SceneDt` is 0 while the
  scene is paused; in a step it is the step's delta and `frame.Paused` is true. `frame.SceneTime` is the clock before this
  frame or step, as in `Update`. A step started from inside this call runs no `ScenePost`.
- **What shows when.** A node's slot set here shows in this frame, so a camera node or an item can follow a final bone. A
  bone's or an IK target's slot shows at the next motion step. `ctx.Call` works here, and a scene edit made here is an edit
  like any other.
- **Writing a bone directly.** A slot needs nothing more. A plugin that writes a bone's transform here, after the character's
  motion step, reports it with `ctx.Unity().BoneWritten(nodeId, bone)` (false when the node is no built character): the next
  motion step then takes that write off, where the Advanced pose's corrections and the card's modifiers would otherwise keep
  it as new input. Twist bones and physics have already run, so they do not follow such a write.
- **Faults and time.** The call goes through the plugin's guard, counted under `scenePost`; a plugin that faults leaves the
  stage and the others go on. `plugin.list` gives the times as `scenePostMs` (`median`, `max`, `frames`), on a plugin with
  the interface only. The stage is there only while a loaded plugin has the interface.

## Engine-side services

`ctx.Unity()` (an extension method of `HS2Next.Api.Unity`) returns `IUnityPluginServices`, or null in a host without an
engine:

- `NodeObject(id)`, `CharaRoot(id)`: the realized objects of scene nodes. Reading them is supported. Changing state that the
  scene model owns (transforms, visibility, materials outside the overrides) is not: the next apply or build may overwrite
  it. To show another value of a channel the runtime overrides cover, set a slot instead.
- `BuildHooks`: code that runs at fixed points of every character build: `BodyMesh` (after the body is spawned, before the
  skin is composited: skin layers, body-part replacement, implanted bones), `Textures` (after the plugin texture stage) and
  `Final` (after the final plugin stage). `Textures` and `Final` run again in the edits that redo that point, so a hook's
  result must depend only on the character's state.
- `Materials`: typed overrides of one material slot's float, colour, texture or shader, or a renderer's enabled flag.
- `AddMosaic(renderer)`: makes a renderer a mosaic member.
- `SkinLayers(nodeId)`, `RequestRebuild(nodeId)` (`[Experimental]`): skin layers outside a build, and a new build of a
  character (below).
- `BoneWritten(nodeId, bone)` (`[Experimental]`): reports a bone written after the character's motion step (see "The scene
  post stage").
- `Ui` (`[Experimental]`): panels in a region of the Studio shell, inspector sections for a node kind, actions reachable
  through `ui.action` and shortcuts, with labels from the plugin's own strings, and the app's widgets (`Ui.Widgets`).

### Skin layers and body-part replacement (experimental)

At the `BodyMesh` stage a hook's `IBuildHookArgs` can change the character's skin and body in the build's one pass: the
skin is composited, the parts are drawn and the normals are blended in the same build, with no second build.

```csharp
ctx.Unity().BuildHooks.Register("tattoo", BuildStage.BodyMesh, args =>
{
    args.SetSkinLayer("tattoo", SkinLayerKind.BodyOver, tattooPng);   // PNG or JPEG bytes, or a Texture2D
});
```

- **Skin layers.** `SetSkinLayer(id, kind, png)` and `SetSkinLayer(id, kind, texture)` add a layer of one of the skin's
  passes (`SkinLayerKind`: `BodyOver`, `FaceOver`, `BodyUnder`, `FaceUnder` and their `Detail` forms); an empty id throws
  `ArgumentException`; setting the same id again replaces it; layers are composited after the card's own layers, in the
  order set. Bytes that are not a PNG or JPEG give false. A texture stays the plugin's (the app never destroys it; a
  destroyed one is skipped with a log line). `RemoveSkinLayer(id)` removes this plugin's layer of that id (never a card's
  or another plugin's); `SkinLayers` lists the plugin's layer ids on this character in this build.
- **Body-part replacement.** `BodyParts` lists the names of the renderers under the character's body object.
  `ReplaceBodyPart(part, replacement)` replaces one of them in place with a `BodyPart` (null, or one without a mesh, throws
  `ArgumentNullException`): `Mesh` (required), `BoneNames` (the mesh's bone order by name; null: the part's current bones,
  and then the mesh must have as many bones), `RootBone`, `Materials` (null: keep the part's; a null entry keeps that slot's
  material), `Normals` (a `BodyNormals`: `BodyNormals.FromAsset(zipmodGuid, bundle, asset)` names a normals asset in an
  installed zipmod, or in the game's data when the GUID is null or empty, an empty bundle or asset throws
  `ArgumentException`; `BodyNormals.None` blends none; null keeps the current source). `BodyPart.From(renderer)` takes all
  of them from a renderer the plugin built or loaded. The app binds each bone by name to the character's body bones, else to
  a bone of that name the part already uses (implant missing ones first with `ImplantBone`), and copies the materials per
  character; on the body renderer the first slot stays the body's skin material and `Materials[0]` is not used there, the
  other entries fill the other slots. The result is `Replaced`, `NotFound` (no part of that name) or `Mismatch` (a bone the
  app cannot bind, a bone count that differs when no names are given, or bones for a part without skinning; nothing
  changed). Replacing the body renderer marks the body as replaced, so FK providers add their rows. A replaced part stays
  until the character's next build.
- **Outside builds.** `ctx.Unity().SkinLayers(nodeId)` (`ISkinLayers`: `Set`, `Remove`, `Ids`; null when the node is not a
  built character) changes layers without a rebuild: the next apply composites the skin again. These layers share the ids
  of the hook's layers (the later set or removal of an id wins) and join every later build of that character.
  `RequestRebuild(nodeId)` builds the character again at the next apply, so its `BodyMesh` hooks run again (the way to
  change a replaced part after the build); requests made before that apply give one rebuild.
- `plugin.list` counts the layers and replaced parts of each plugin (`skinLayers`, `bodyParts`); the character's build log
  names each with `<guid>/<id>`. When the plugin is disabled, faults or unloads, its layers set outside builds are dropped
  (the skin is composited again), and a disable rebuilds the characters its hooks touched, so its parts leave them.

**The mosaic.** The mosaic guards cover these points: material overrides refuse mosaic members and materials with a mosaic
shader (a warning, not a fault); the build hooks offer no stage right after the mosaic; after the `Final` hooks of a build
in which a plugin hook ran, the host gives every member slot its mosaic material back; `AddMosaic` only adds members; the
shader supply refuses the mosaic shaders. In every build, the renderers of the app's own content follow the mosaic set's
rules. A body part a plugin supplies through `ReplaceBodyPart` is drawn as supplied; it is a mosaic member when the plugin
adds it with `AddMosaic`.

## Shaders

`ctx.Shaders.Supply(original, replacement)` maps a shader name to a replacement that the host can find by name (for example
one the plugin loaded from its own asset bundle) for materials loaded later. A later supply of the same name by another
plugin stacks on top; withdrawing restores the one below, and unloading withdraws all of a plugin's supplies. The mosaic
shaders cannot be supplied or replaced.

HS2Next's own replacements for the shaders of mods and asset packs are its compatible shaders, named
`HS2Next/Compatible/<rest>` (the end of File > Options > Advanced lists their families); its replacements for the game's and
Unity's shaders are named `HS2Next/<rest>`. Materials are always looked up by their original shader name, so a supply names
the original.

## Versions

The API follows semantic versioning; `ApiVersion` (and the informational version of both assemblies) gives it, and the
assembly version is `MAJOR.0.0.0`, so a plugin binds across minor and patch releases. This guide describes 1.2.

- A plugin declares the `MAJOR.MINOR` it was built against in `apiVersion`. It loads when the host has the same major and
  a minor at least as high: a `1.0` plugin loads on every 1.x host, a `1.2` plugin needs a 1.2 or later host. A plugin that
  uses a member added in 1.1 declares `"1.1"`, one that uses a member added in 1.2 `"1.2"`.
- Within a major version nothing public is removed or changed. Interfaces the host implements (the context, the services,
  the events, the replies) may gain members in a minor version; plugins only call them. Interfaces plugins implement
  (`IPlugin`, `IPluginUpdate`, `IPluginSceneLoaded`, `IPluginScenePost`, `IExtBlockHandler`, `IKkexConsumer`, `IBatchHook`,
  `IMotionStage`) never change: a new capability comes as a new optional interface or a delegate.
- Types and members marked `[Experimental]` are outside the promise and may change in any minor version. In 1.2 these are
  the forms (`HS2Next.Api.Ui`, `ctx.Forms`), the widgets, the undo entries (`ctx.History`), the runtime overrides
  (`ctx.Overrides`), the scene post stage (`IPluginScenePost`, `BoneWritten`), skin layers, body-part replacement and
  rebuild requests, and the engine-side UI registration. The call
  helpers, the capture events with their kinds and the commands `plugin.disable` / `plugin.enable` are stable.
- 1.1 added: `Request` / `RequestAsync` with `CommandRequest` and `ICommandReply`, `IsActive`, the capture events, undo
  entries, forms and widgets, skin layers, body-part replacement, rebuild requests, disabling and enabling at run time, and
  the XML documentation of every public member.
- 1.2 added: the runtime overrides (`ctx.Overrides`, `IRuntimeOverrides`, `IOverrideSlot`, `OverrideValueKind`,
  `OverrideInfo`) with the paths of bones and IK targets, the scene post stage (`IPluginScenePost`, `BoneWritten`) and the
  capture kind `Record` for the frames of a recording.

## Testing a plugin

- `plugin.list` shows each plugin's state, assemblies, commands, ext namespaces, handlers, forms (`ui.forms`), skin layers
  and replaced parts (counts), undo entries (`history`, a count), runtime override slots (`overrides`), faults, the last
  failed enable (`enableError`), update times and, on a plugin with `IPluginScenePost`, the times of its post-stage calls
  (`scenePostMs`). `overrides.list` shows every slot of the scene with its owner, priority and value.
- `plugin.disable {"guid": "<guid>"}` unloads a plugin while the app runs and `plugin.enable {"guid": "<guid>"}` loads it
  again (`persist: true` also records the choice in the settings, also when an enable fails, with a warning;
  `withDependents: true` disables the plugins that depend on it first). The replies give the state, the time, the
  characters queued for a rebuild (`rebuilt`), for a disable the runtime override slots released (`overrides`) and, for an
  enable, the ext blocks read again (`extBlocks`). Disable and enable a plugin a few times and compare `plugin.list` before
  and after: everything it registers should come back, and nothing it made outside its context should pile up.
- A plugin built against 1.0 can be enabled again too; the reply warns that such a plugin may expect one `Load` per
  process.
- Engine-free logic can be tested without the app, in a plain .NET test project that references the same `HS2Next.Api`.

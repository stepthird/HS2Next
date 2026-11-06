# 编写 HS2Next 插件

插件是 HS2Next 在启动时加载的 .NET 程序集。它可以添加桥接命令，在场景、角色和服装中保存自己的数据，响应场景事件和
截图，每帧运行代码，向撤销历史添加条目，显示由程序自身的控件绘制的面板和检查器分节，把仅用于显示的值叠加在场景上，提供
着色器；通过引擎侧的程序集，还可以挂接角色的构建过程（皮肤图层、身体部件替换）、覆盖材质以及添加自己的界面。本文档介绍
插件 API 1.2。内容相同的[英文版](PLUGINS.md)和[日文版](PLUGINS_ja.md)也已提供；旁边的 [COMMANDS.md](COMMANDS.md)
列出了程序的所有命令。

**插件是完全受信任的代码。** 插件在程序内部以与程序相同的权限运行，没有沙箱。请只安装来源可信的插件。

## 插件工具包

分发的 zip 中，`HS2Next.exe` 旁边有一个 `PluginKit/` 文件夹：

| 条目 | 内容 |
| --- | --- |
| `PLUGINS.md`、`PLUGINS_ja.md`、`PLUGINS_zh-Hans.md` | 本文档 |
| `COMMANDS.md` | 命令参考（插件可以通过 `Request` 或 `Call` 调用的所有命令） |
| `HS2NextPlugin.props` | 各项目共用的构建设置：只需一个属性 `HS2NextDir`，即可找到所有引用 |
| `PluginKit.sln` | 两个示例；在此文件夹中运行一次 `dotnet build` 即可构建两者 |
| `NuGet.config` | 清除所有包源：工具包不需要任何包，也不需要网络 |
| `Hello/` | 不使用引擎的示例：一个命令、一个调用辅助、一个事件处理程序 |
| `HelloUi/` | 不使用引擎的示例：以表单声明的面板和物品检查器分节，以及它自己的字符串 |
| `doc/` | `HS2Next.Api` 和 `HS2Next.Api.Unity` 的 XML 文档（`HS2Next_Data/Managed/` 中的 DLL 旁边也有一份，供 IDE 使用） |

构建示例需要 .NET SDK 8 或更高版本（可用 `dotnet --list-sdks` 查看）：

1. 在 `PluginKit` 文件夹中打开终端，运行 `dotnet build`。两个示例会针对程序自身的 API 程序集进行构建，并分别复制到
   `out/<guid>/`（`out/stp3.sample.hello/`、`out/stp3.sample.helloui/`）。
2. 将这些文件夹复制到 `HS2Next.exe` 旁边的 `Plugins` 文件夹（第一次需要新建），使之成为
   `Plugins/stp3.sample.hello/manifest.json` 这样的结构。
3. 启动 HS2Next。“文件 > 选项”的自动化部分会列出插件及其状态；桥接命令 `plugin.list` 显示详细信息。
   `hello.greet {"name": "kit"}` 会返回 `{"text": "Hello, kit!", "objects": …}`。

`HS2NextDir` 的默认值是工具包的上一级文件夹，也就是解压后的程序文件夹。如果把工具包复制到其他位置，请传入程序的文件夹：
`dotnet build -p:HS2NextDir=<HS2Next.exe 所在的文件夹>`（绝对路径）；否则构建会以“HS2Next.Api.dll not found under …”停止。
要开始编写自己的插件，请把 `Hello/` 复制到一个新文件夹，修改项目、程序集和命名空间的名称，在清单中填写您自己的 `guid` 和
`commandPrefixes`，把项目中的 `PluginGuid` 设为同一 GUID，然后将项目加入解决方案（`dotnet sln add <文件夹>/<名称>.csproj`）。
props 文件中还说明了其他属性：`HS2NextUseUnity=true` 添加引擎侧的引用（`HS2Next.Api.Unity`、`UnityEngine.CoreModule`、
`UnityEngine.UIElementsModule`），`HS2NextUseJson=true` 添加 `Newtonsoft.Json`。

## 两个 API 程序集

| 程序集 | 引用 | 提供的内容 |
| --- | --- | --- |
| `HS2Next.Api` | 不含引擎类型 | `IPlugin` 和生命周期接口、插件上下文、调用辅助、事件、撤销条目、运行时覆盖、表单（`HS2Next.Api.Ui`）、桥接命令约定（`HS2Next.Api.Bridge`）、扩展块约定（`HS2Next.Api.Save`）、内容 ID、动作阶段 |
| `HS2Next.Api.Unity` | UnityEngine | 场景节点和角色的实体、包含皮肤图层和身体部件替换的构建挂钩、重新构建请求、材质覆盖、额外的马赛克成员、界面注册和程序的控件（实验性） |

不需要引擎类型的插件只引用 `HS2Next.Api`（如需 JSON 库，还可引用 `Newtonsoft.Json`：程序为每个插件提供 13 版）。两个
程序集都位于程序的 `HS2Next_Data/Managed/` 中。请不要随插件一起分发它们；程序会加载自己的版本。

## 文件夹和清单

插件是一个文件夹 `<程序>/Plugins/<guid>/`（位于 `HS2Next.exe` 旁边），其中包含 `manifest.json` 和清单列出的程序集。
私有依赖放在该文件夹内的 `lib/` 中，加载器会从那里解析。文件夹名称必须与清单的 `guid` 相同（不区分大小写）。文件夹中的
`strings.tsv` 会添加插件自己的界面字符串（见下文）。

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

| 键 | 必需 | 规则 |
| --- | --- | --- |
| `manifestVersion` | 是 | 整数 `1` |
| `guid` | 是 | 反向域名形式：`[A-Za-z0-9][A-Za-z0-9._-]{0,127}`，且至少含一个点。`core`、`bridge` 以及以 `hs2.`、`hs2next.`、`illusion.` 开头的名称为保留名称。GUID 拥有插件的命令和扩展命名空间 |
| `name` | 是 | 1–64 个字符 |
| `version` | 是 | semver `MAJOR.MINOR.PATCH[-pre]` |
| `apiVersion` | 是 | 构建插件时所用 API 的 `MAJOR.MINOR`（见“版本”） |
| `assemblies` | 是 | 插件文件夹中的一个或多个文件名（不含路径），每个只列一次 |
| `entryType` | 否 | `IPlugin` 类型的完整名称；省略时，加载器使用唯一一个具有公共无参数构造函数的公共非抽象 `IPlugin` 类型 |
| `commandPrefixes` | 否 | 以点结尾的命令名前缀（`"seq."`）；插件注册的每个命令都必须以其中之一开头，一个前缀只属于一个插件 |
| `dependencies` | 否 | `[{"guid", "minVersion"}]`：插件只在这些插件之后加载，并且只有当它们以不低于该版本加载时才加载 |
| `loadAfter`、`loadBefore` | 否 | 对非依赖插件的顺序提示；会形成循环的提示将被舍弃并报告 |
| `minAppVersion` | 否 | 程序的最低版本（semver） |
| `author`、`license`、`url`、`description` | 否 | 由 `plugin.list` 显示 |

未知的键会被忽略（并报告）。违反规则的清单会使插件被拒绝，`plugin.list` 显示其代码，日志中也会重复：`plugin.badManifest`、
`plugin.folderMismatch`、`plugin.duplicateGuid`（两个插件都被拒绝）、`plugin.reservedGuid`、`plugin.apiVersion`、
`plugin.appVersion`、`plugin.missingAssembly`、`plugin.assemblyCollision`、`plugin.prefixTaken`、`plugin.dependency`、
`plugin.dependencyCycle`、`plugin.disabled`、`plugin.staleAssembly`、`plugin.noEntry`、`plugin.typeLoad`、`plugin.loadFailed`。

加载由设置 `plugins.enabled`、`plugins.dir` 和 `plugins.disabled`（GUID 列表）、启动参数 `-noPlugins` 以及环境变量
`HS2NEXT_NO_PLUGINS=1` 控制。“文件 > 选项”的自动化部分中的插件开关会立即停用或启用插件，并把选择记录在
`plugins.disabled` 中；`plugins.enabled` 在下次启动时生效。

## 生命周期

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

- 宿主通过入口类型的公共无参数构造函数创建它，并在启动阶段 `plugins`（初始场景之后）用插件的上下文调用 `Load`。插件按
  依赖顺序加载（顺序相同时按 GUID）。
- `IPluginUpdate.Update` 每帧按加载顺序运行一次，位于桥接处理完本帧请求之后、场景应用其更改之前，因此在这里的写入会在
  同一帧显示。场景暂停时 `frame.SceneDt` 为 0；暂停期间的逐帧步进改由 `SceneStepped` 事件通知。
- `IPluginSceneLoaded.SceneLoaded` 在每次新建或加载场景之后调用（场景的扩展块已在此之前读取），并在插件加载之后立即以
  `SceneLoadKind.Initial` 调用一次。
- `IPluginScenePost.ScenePost`（`[Experimental]`）在所有运动之后调用，每帧一次，每次场景步进一次（见“场景后置阶段”）。
- 所有调用都在主线程上进行。`Unload` 返回后，宿主会移除插件通过其上下文注册的所有内容；此后上下文除 `Guid`、`Log` 和
  `IsActive` 外都会抛出 `ObjectDisposedException`。插件进入故障状态后，`IsActive` 也为 false。
- 程序运行期间可以停用插件并再次启用（`plugin.disable`、`plugin.enable`、选项中的开关）。启用时，宿主从已加载的程序集
  创建入口类型的**新实例**，再次调用其 `Load`，然后为打开的场景发送 `Initial` 的 `SceneLoaded` 以及 `AppReady`。因此
  `Load` 在一个进程中可能运行多次，每次都在新的实例上。
- Mono 不会卸载程序集：被停用插件的程序集会留在内存中，直到程序重新启动；在插件文件夹中被替换的 DLL 只有在重新启动后才会
  加载（`plugin.enable` 返回 `plugin.staleAssembly`）。

**在 `Unload` 中清理。** 请在其中撤销插件在上下文之外所做的一切：静态字段、它创建的 GameObject 和组件、它订阅的 Unity
回调、它启动的线程和计时器、它仍打开的文件、它尚未完成的延迟回复。被停用插件的程序集、静态状态和对象都留在进程中，
再次启用的插件从这些状态开始。宿主自行移除的内容：命令、事件处理程序、扩展块处理程序、构建挂钩、FK 提供者、着色器供应、
界面注册和表单、撤销条目、运行时覆盖的槽位、运行时皮肤图层、通过 `ctx.Frames` 安排的帧操作及其帧时钟处理程序、字符串。
保留的内容：插件添加的马赛克成员（没有任何方式移除成员）、通过材质覆盖设置的值（直到该对象再次构建）以及它写入的文件。
打开的场景中该插件的扩展块保持上次加载或保存时的值：未保存的插件状态会丢失，文件中的数据不会丢失，新实例会在其
`SceneLoaded` 之前重新读取打开场景的块。

**故障。** 对插件的每次调用都经过保护。异常按挂钩和异常类型各记录一次（附带堆栈），且不会中断调用方。累计 10 次故障后，
插件进入故障状态：其更新、后置阶段调用和事件停止，其他注册被移除，尝试调用一次 `Unload`，其命令返回 `plugin_fault`。
`plugin.enable` 可以重新加载处于故障状态的插件。

## 上下文

`IPluginContext` 是插件所看到的宿主：

| 成员 | 内容 |
| --- | --- |
| `Guid`、`Name`、`Version`、`Directory` | 来自清单和插件文件夹（请把该文件夹视为只读：更新可能替换它） |
| `DataDirectory` | `<UserData>/Plugins/<guid>/`，插件自己的数据，首次使用时创建 |
| `Host` | 程序和 API 的版本、编辑器 / 批处理 / 播放器、界面是否运行、UserData 文件夹 |
| `Log` | 带前缀 `[plugin <guid>]` 的 Unity 日志；桥接命令 `log.tail` 可以显示它 |
| `Settings` | 以 JSON 文本表示的 `<UserData>/Plugins/<guid>/settings.json`；结构由插件决定；写入是原子性的 |
| `Commands` | 桥接的命令注册表，有作用范围：所有者是 GUID，`commandPrefixes` 之外的名称会被拒绝 |
| `Frames`、`Readiness`、`Output` | 桥接的帧时钟（预定操作和延后操作；插件被移除后，其操作被丢弃而不执行；正在运行的操作算作插件的代码，因此此时停用会返回 `busy`）、就绪阶段（插件可以声明自己的阶段）、截图和转储的输出文件夹 |
| `Ext` | 插件命名空间的扩展块处理程序 |
| `Events` | 主线程上的类型化事件 |
| `Clock` | 场景时钟（时间、帧间隔、暂停、帧数、步进） |
| `Shaders` | 按名称为之后加载的材质替换着色器 |
| `Request(command, args)`、`RequestAsync(…)` | 用参数对象调用命令并读取类型化的回复（见下文） |
| `Call(json)`、`CallAsync(json)` | 同上，但以 JSON 文本传递请求信封，并以文本返回回复 |
| `IsActive` | 从 `Load` 到 `Unload` 返回为止为 true；从不抛出异常（用于可能在停用之后运行的后续处理） |
| `History`（`[Experimental]`） | 把插件自身状态的更改记入场景的撤销历史 |
| `Forms`（`[Experimental]`） | 以表单声明、由程序绘制的面板和检查器分节 |
| `Overrides`（`[Experimental]`） | 为显示而叠加在场景上、不会保存的值（运行时覆盖） |

## 调用命令

```csharp
ICommandReply r = ctx.Request("obj.transform", new { key = id, pos = new[] { 0.0, 1.0, 0.0 }, undo = false });
if (!r.Ok) ctx.Log.Warn(r.Code + ": " + r.Error);

var scene = ctx.Request("scene");
int objects = scene.Get<List<object>>("objects", new List<object>()).Count;

var req = new CommandRequest("chara.edit").Arg("key", id).Arg("path", "body.shape.height").Arg("value", 0.6);
req.Undo = true;
ctx.Request(req).ThrowIfError();
```

- `Request(command, args)` 构建信封 `{"cmd": command, …args}`，并像 `Call` 一样在主线程上立即运行；`RequestAsync` 像
  `CallAsync` 一样把它排在其他请求之后（任意线程、任意命令）。参数可以是公共属性和字段会成为成员的对象（匿名类型、类）、
  `IDictionary<string, object>`（或任何以字符串为键的键值对序列）、Newtonsoft 的 `JObject` 或 null。它们按桥接写回复的
  规则写出：成员名称为 camelCase（`Pos` → `pos`；`[BridgeArg("name")]` 改名，`[BridgeIgnore]` 省略），枚举按名称，浮点数
  可往返（NaN 写为 null），Unity 的向量和颜色写为数组，null 也会写出；因此数字与 HTTP 客户端发送时完全相同。把字符串、
  数字、布尔值或数组作为 `args`，或包含名为 `cmd` 的成员，会抛出 `ArgumentException`（信封文本请使用 `Call`）。
- 信封成员（`id`、`strict`、`undo`，排队形式还有 `waitFrames`、`timeout`）既可以是 `args` 的成员，也可以是
  `CommandRequest` 的属性。`CommandRequest.Arg` 保持参数的顺序，同名的值给出两次时原位替换，并拒绝信封名称（`cmd`、`id`、
  `waitFrames`、`timeout`、`strict`、`as`、`undo`）：这些请通过属性设置（`WaitFrames` 为 0 时不发送）。
- 回复（`ICommandReply`）：`Ok`、`Command`、`Code` 和 `Error`（成功时为 null）、`Warnings`、`Json`（与 `Call` 返回的回复
  文本完全相同）、`Has(member)`（存在且不为 null）、`Get<T>(member, fallback)`（缺失或为 null 时返回 fallback；数字可转换
  为 `int`、`long`、`float`、`double`，数组可转换为 `T[]` 或 `List<T>`，对象可转换为 `Dictionary<string, object>` 或类；
  无法转换的值会抛出指明成员的 `FormatException`；以 `object` 读取的值是普通的 .NET 值：`string`、`long`、`double`、`bool`、
  null、`List<object>` 或 `Dictionary<string, object>`）、`GetJson(member)`、`As<T>()`（把整个回复转换为类）、
  `ThrowIfError()`（带有代码、消息和详情的 `BridgeException`）。
- 命令错误从不抛出异常：请检查 `Ok`。对于同一信封，`Request` 和 `Call` 返回相同的回复文本。

立即运行的形式（`Request`、`Call`）拒绝 `batch`、可能延迟回复的命令、异步命令和 `waitFrames`（请使用排队形式）。在其他方
持有场景事务时（拖动中、正在运行的批处理）、在分发场景事件或截图事件时，以及在撤销或重做过程中，更改场景的命令会返回
`busy`，因此插件的写入不会混入其他方的撤销步骤。插件自行写入场景时，如果更改属于插件的状态而非用户的操作，请传入
`"undo": false`。

## 命令

插件的命令是普通的桥接命令：`help` 会列出它们，HTTP 客户端和其他插件都可以调用，并适用 [COMMANDS.md](COMMANDS.md) 中的
回复规则。命令是一个 `CommandSpec`（名称、摘要、带类型和默认值的参数、返回字段、标志）加上一个处理程序；处理程序可以立即
回复、把回复推迟到之后的帧，或异步运行。请用 `CommandFlags.Mutates` 标记更改场景的命令，用 `CommandFlags.ReadOnly` 标记
只读命令，用 `CommandFlags.Experimental` 标记可能在版本之间变化的命令。插件命令的所有者始终是插件的 GUID；其 help 分组
默认为 GUID，`since` 默认为插件的版本。

Hello 示例的全部代码：

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

## 事件

`ctx.Events`（`IAppEvents`）：`AppReady`、`SceneCleared`、`SceneLoaded`、`SceneSaving`、`SceneSaved`、`NodesAdded`、
`NodesRemoved`、`NodesMoved`、`NodeForgotten`（已删除的节点再也无法通过撤销恢复）、`CharaBuilt`、`PausedChanged`、
`SceneStepped`、`Quitting`，以及下文的截图事件。处理程序在主线程上、在宿主自身的处理程序之后运行。抛出异常的处理程序算作
其插件的故障，不会中断其他处理程序。同一插件的处理程序不会被重入：在它运行期间引发的事件会在它返回后立即送达。请在下一次
`Update` 中更改场景，而不要在事件内部更改。

**截图。** 每次截图（F11、工具栏、截图对话框、`ui.action capture.screenshot`、`screenshot` 命令）、每次 `capture` 命令，
以及序列帧导出（`record.start`、时间轴面板中的“导出序列帧”）的每一帧之后，都会依次发生三个事件：

| 事件 | 时机 | 用途 |
| --- | --- | --- |
| `CaptureStarting`（`CaptureInfo`） | 在主线程上，渲染之前 | 隐藏插件自己的叠加显示（它创建的对象） |
| `CaptureRendered`（`CaptureInfo`） | 渲染之后，在同一帧（渲染失败时也会发生） | 重新显示它们 |
| `CaptureFinished`（`CaptureResultInfo`） | 文件写入之后或截图失败之后：截图和导出的帧在几帧之后，`capture` 在同一帧 | 读取结果 |

`CaptureInfo`：`Id`（每次运行从 1 开始）、`Kind`（`Screenshot`、`Capture`、`Record`）、`Origin`（`Ui`、`Command`）、`Width`、
`Height`（输出尺寸）、`Supersample`（1、2、4）、`RenderWidth`、`RenderHeight`、`DrawsFrame`（是否绘制场景的边框图像）、
`FrameNumber`、`Path`（要写入的文件）。`CaptureResultInfo`：`Capture`、`Ok`、`Path`（失败时为 null）、`Bytes`、`Error`、
`Milliseconds`。同一次截图的三个事件传递同一个 `CaptureInfo` 对象。每个 `CaptureStarting` 之后都恰好跟随一个 `CaptureRendered` 和一个 `CaptureFinished`；在渲染任何内容之前
就被拒绝的请求（尺寸错误、像素上限、尚未完成的截图）不会引发任何事件。在分发截图事件期间，通过 `Request` 和 `Call` 更改
场景会返回 `busy`：请直接隐藏您自己的对象。场景、角色和服装的缩略图不会引发截图事件。

序列帧导出会为写入的每一帧按帧的顺序引发这三个事件：`Kind` 为 `Record`，`Origin` 对 `record.start` 为 `Command`，
对对话框为 `Ui`，`Path` 为该帧的文件。全局暂停期间的帧不会写入，也不会引发事件。导出期间，无论实际时间如何，场景每帧都
前进一个固定的步长（`frame.SceneDt` 为 1 / fps）：按场景时钟移动自己对象的插件会按该步长被导出，使用实际时间的插件则不会。

## 在场景、角色和服装中保存数据

插件把数据保存为自己命名空间（GUID 或 `<guid>.<名称>`）的扩展块。用 `ctx.Ext.Register` 注册一个 `IExtBlockHandler`：

- `Scopes`：块所在的位置：角色的外观、服装（也可以按饰品和服装部件）、场景或某个场景节点。
- `Read(IExtReadContext)` 在加载文件时解析块：内联的 JSON 值以及块的文件（位于容器的 `ext/<命名空间>/` 下）。请先保存数据，
  稍后（`CharaBuilt`、`SceneLoaded`）再应用，不要在 `Read` 中应用。
- `Write(IExtWriteContext)` 在保存文件时写入块：内联的 JSON 值和文件（使用容器的路径语法，最多 200 个字符）。什么都不写
  表示“没有块”。
- `Version`（处理程序写入的数据版本）与块一起保存；`Read` 负责迁移旧数据。处理程序拒绝的块（例如数据版本过新）保持不变，
  作为未知块写回；插件未安装或已停用时，其块也同样处理。
- `RequiredByDefault` 标记缺少后内容会不完整或产生误导的数据；没有该插件的加载器会报告它。
- 同时实现 `IKkexConsumer` 的处理程序会接收从角色卡导入的 HS2 插件数据：在加载角色或服装文件时，对插件 ID 位于 `KkexIds`
  中的每个导入条目调用一次 `ReadKkex`，时机在该处理程序对同一实例的 `Read` 之后（即使处理程序在那里没有块）。

源代码仓库中的序列器示例（`Samples/Sequencer`）把序列保存在场景的块中，是这种用法的较大示例。

## 表单（实验性）

表单是插件以数据声明、由程序用自身控件绘制的面板或检查器分节：与程序的检查器使用相同的滑块、颜色选择器、选项和选择器。
值来自插件的读取命令，并通过其写入命令写回。`HS2Next.Api.Ui` 和 `ctx.Forms` 的全部内容都是 `[Experimental]`。工具包中的
HelloUi 示例是一个完整的例子。

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

**两个命令。** 都在 `Load` 中、在插件自己的前缀下注册。读取命令接收 `{"form": <id>, "node": <节点 ID 或 null>}`（面板为
null，分节为所检查的节点：分节的值请按节点 ID 保存），返回 `values`。写入命令接收 `{"form", "node", "key", "value",
"phase"}`，返回成功（内容不限），或一个错误（程序把其消息显示为通知）。写入命令更改的是插件自身的状态而不是场景：请不要
为它加上 `CommandFlags.Mutates`（`undo` 和 `redo` 的写入是在场景应用该步骤期间运行的，此时更改场景的命令会返回 `busy`）；
读取命令可以标记为 `CommandFlags.ReadOnly`。

- **注册。** `ctx.Forms.Panel(id, titleKey, region, form, order)`（region 为 `left`、`right` 或 `bottom`；标题即面板的
  选项卡）和 `ctx.Forms.Section(kind, id, titleKey, form, order)`（kind 为 `chara`、`item`、`light`、`camera`、`folder`
  或 `route`：该类节点的检查器分节）；`Remove(id)`；`Refresh(id, nodeId)` 立即重新读取（在插件通过其他方式更改值之后，
  例如通过它自己的命令；node 为 null 时表示所有显示中的节点）。ID 属于插件自己；程序以 `<guid>/<id>` 保存它们。表单在注册
  时被检查并**复制**：之后再更改 `UiForm` 或其行不会产生效果。在没有界面的环境中（批处理播放器、`-noUi`），`Available` 为
  false：表单会被检查并在 `plugin.list`（`ui.forms`）中计数，但不会绘制。插件被卸载或停用时，全部内容都会被移除。
- **行。** `UiForm(readCommand, writeCommand)`（`Undo`、`Rows`）；`UiChoice(value, labelKey)`。`UiRow` 为每种类型提供
  工厂方法：`Float`、`Int`（整数）、`Bool`、`Color`（默认带 alpha）、`Vec2`、`Vec3`、`Enum`、`Ref`、`File`、`Text`、`Node`、
  `Button`、`AttachPoint`、`PartList`、`Push`（ColorHdr 行是 `Kind` 为该类型的 `UiRow`）；可设置的成员：`Key`（在表单中
  唯一：值的名称）、`LabelKey`、`TooltipKey`、`Min` 和 `Max`（滑块范围）、`TypedMin` 和 `TypedMax`（输入的值会被限制在
  此范围内）、`Whole`、`Default`（重置按钮的值；null 表示没有重置按钮）、`Format`、`Scale`（显示比例）、`Grid`、
  `GridTruncate`、`Wrap360`、`Nullable`、`ReadOnly`、`ColorAlpha`、`Choices`（Enum 行）、`ChoicesCommand`（Ref 和 Node
  行：返回 `{"choices": [{"value", "label"}]}` 的命令）、`Command` 和 `CommandArgs`（Button 行）、`CategoryFemale` 和
  `CategoryMale`（PartList 行：角色编辑器中列表的类别）、`PushSource`（Push 行：`facePreset`、`layout:6`、`layout:7`、
  `colour:336` 或 `colour:305`）。`RowKind` 与程序自身行类型的名称和值相同；`HairBundles` 为保留类型，会被拒绝。
- **注册时的检查**（抛出 `ArgumentException`，不注册任何内容；表单为 null 时抛出 `ArgumentNullException`）：空或重复的 ID
  （包括已在界面中显示的 ID）、列表之外的 region 或 kind、空的读取命令、插件 `commandPrefixes` 之外的读取、写入、按钮或
  选项命令（包括与前缀相同的名称）、null 的行、空或重复的行键、`Min` 大于 `Max`、没有选项的 Enum 行、没有命令的 Button 行、
  没有写入命令却可编辑的行（Button 以外且不是 `ReadOnly` 的行）、没有类别的 PartList 行、来源不在列表中的 Push 行、
  `HairBundles` 行、未定义的类型、不是 JSON 值的 `Default` 或 `CommandArgs`。

程序以立即运行的形式调用插件的命令（会延迟回复或异步运行的表单命令将被拒绝，表单会显示该错误）：

| 调用 | 程序发送的信封 | 程序读取的回复 |
| --- | --- | --- |
| 读取 | `{"cmd": <ReadCommand>, "form": <id>, "node": <节点 ID 或 null>}` | `values`（行键 → 值），可选的 `hidden` 和 `disabled`（键的列表）、`choices`（键 → `[{"value", "label"}]`，替换该行的选项） |
| 写入 | `{"cmd": <WriteCommand>, "form", "node", "key", "value", "phase", "undo": false}`，phase 为 `preview`、`commit`、`cancel`、`undo` 或 `redo` | 成功，或一个错误（程序把其消息显示为通知） |
| 按钮 | `{"cmd": <row.Command>, "form", "node", "key"}` 加上该行的 `CommandArgs` | 成功或错误（通知） |
| 选项 | `{"cmd": <row.ChoicesCommand>, "form", "node", "key"}` | `choices: [{"value", "label"}]` |

JSON 中的值：Float 和 Int 为数字；Bool 为布尔值；Color 为 `[r, g, b, a]`（四个 0 到 1 的数字）；ColorHdr 为
`{"color": [r, g, b, a], "intensity": n}`；Vec2 和 Vec3 为 `[x, y]` 和 `[x, y, z]`；Enum 为选项的值（字符串）；Ref、Node、
File、Text 和 AttachPoint 为字符串或 null；PartList 为部件的 `"guid#id"` 或 null；Button 和 Push 没有值（点击 Push 行的
格子会发送一个以该格子的 `"guid#id"` 为值的 `commit`）。`values` 中缺少的值显示为“—”；形状不对的值也显示为“—”，该行会被
标记，并在日志中记录一次（`[plugin <guid>] form <id> row <key>: …`）。读取失败时，会在行的位置显示“无法读取数值：<代码>:
<消息>”，在下一次成功读取之前这些行不可用。

- **用户看到的内容。** 行使用检查器自身的控件：带输入框和重置按钮（`Default`）的数字滑块、向量输入框、开关、分段按钮
  （最多四个选项）或下拉列表、颜色样块和程序的颜色选择器、引用选择器（选项来自 `ChoicesCommand` 或读取结果的 `choices`；
  没有 `ChoicesCommand` 的 Node 行列出场景中的节点）、图像文件选择器、文本框（按 Enter 或失去焦点时发送）、按钮、挂接点
  选择器、该行类别的角色编辑器缩略图列表（按分节所属角色的性别，否则为女性列表）以及角色编辑器的推送网格。
- **写入和撤销。** 拖动时，每次移动都会发送 `preview` 写入（每帧最多一次），松开时发送 `commit`，按 Esc 发送 `cancel`
  （带有拖动之前的值）；输入值、切换、选择或拾取会发送一次 `commit`。每个阶段都带有要显示的值。当 `UiForm.Undo` 为 true
  （默认）时，程序会对每次改变了值的 commit（整个拖动算一次）以插件的名义记录一个撤销条目，以该行的标签命名：其撤销操作
  以 phase `undo` 写入编辑前读取的值，其重做操作以 phase `redo` 写入确定的值；同一行的输入会合并为一个条目。此时，写入命令
  不得为这些写入自行记录撤销条目（信封中写有 `"undo": false`）。当 `Undo` 为 false 时，程序不记录任何内容。
- **读取。** 表单首次绘制时；每次 `commit`、`cancel`、`undo`、`redo` 和按钮之后；调用 `Refresh` 时；`SceneLoaded` 和
  `SceneCleared` 之后；对于分节，在所检查的节点改变时。每个表单和节点的组合每帧最多读取一次。
- **标签。** 标签、标题、提示和选项的键是插件字符串的键：插件文件夹中的 `strings.tsv`（列为 `key`、`en`、`ja`、`zh-Hans`、
  `note`；键以 `plugin.<guid>.` 开头）会在 `Load` 之前添加；在引擎侧也可以使用 `Ui.AddStrings`。没有字符串的键会直接显示
  键本身。回复中的选项标签以 `plugin.<guid>.` 开头时是这样的键，否则是普通文本。
- **元素名称**（供测试中的 `ui.query` 和 `ui.input` 使用）：表单为 `plugin-form-<guid>/<id>`，行为
  `plugin-form-<guid>/<id>-<key>`（带有 `-field`、`-slider`、`-reset`、`-toggle`、`-dropdown`、`-swatch`、`-text`、
  `-button` 等部分），错误行为 `…-error`；面板的选项卡为 `tab-<guid>/<id>`。

## 引擎侧界面的控件（实验性）

`ctx.Unity().Ui` 注册由 UI Toolkit 元素构成的面板、检查器分节和操作（`IPluginUi`）。它的 `Widgets`（`IUiWidgets`；没有界面时
为 null）让这类插件也能使用程序自身的控件，并连接到插件的回调（每个回调都在插件的保护中调用，插件卸载后不再调用）：
`Number`（带拖动阶段的滑块）、`Vector`、`Toggle`、`Choice`、`Color`、`Ref`、`File`、`Text`、`Button`、`AttachPoint`，每个
都返回一个 `IUiWidget`（`Element`；`SetValue(value, mixed)` 显示一个值而不触发回调；可读写的 `Enabled`）；此外还有
`Toast(text, level)`（普通文本）、`Dialog`（回调得到所按按钮的序号，以其他方式关闭时为 -1）、`Confirm` 和 `Prompt`（取消时
为 null），以及使用 `UiMenuItem(labelKey, run)` 条目（`Enabled` 默认为 true）的 `Menu(items, anchor)`。上文的表单也使用相同
的控件绘制。元素名称：`plugin-widget-<labelKey>` 加上相同的部分后缀，对话框为 `dialog-plugin-<guid>`，其按钮为
`dialog-button-<序号>`，菜单为 `plugin-menu-<guid>`，其条目为 `menu-item-plugin-<guid>-<序号>`。

## 撤销条目（实验性）

`ctx.History`（`IPluginHistory`）把插件自身状态的更改与插件所调用命令的条目一起放入场景的撤销历史：

```csharp
float before = weight, after = weight + 1;
weight = after;                                        // the change is made first
ctx.History.Record("plugin.example.myplugin.weight",  // a key of the plugin's strings, or plain text
    undo: () => weight = before, redo: () => weight = after, mergeKey: "weight");
```

- `Record(label, undo, redo, mergeKey)` 记录插件**已经做出**的更改；`Record(label, beforeJson, afterJson, apply, mergeKey)`
  以两个状态做同样的事（撤销时调用 `apply(beforeJson)`，重做时调用 `apply(afterJson)`）。结果是 `HistoryRecordResult`：
  `Recorded`、`Merged`、`Busy` 或 `NoScene`。`CanRecord` 表示当前是否会接受记录；`Count` 是撤销列表和重做列表中该插件的
  条目数。仅限主线程；回调或 `apply` 为 null 时抛出 `ArgumentNullException`。
- 条目与程序自身的条目进入同一列表：重做列表被丢弃，适用 200 个条目的上限，场景显示为有未保存的更改，撤销和重做（按键、
  菜单、桥接的 `undo` / `redo`）按创建顺序遍历插件的条目和程序的条目。以 `plugin.<guid>.` 开头的标签会在插件的字符串中查找。
- 在场景事务打开期间（拖动中、客户端的批处理，包括插件自己的命令在其中运行时）、在场景应用命令或分发事件期间，以及在撤销
  或重做回调内部，记录会以 `Busy` 被拒绝。
- 若记录的非 null `mergeKey` 与最新条目相同，且该条目属于本插件、此后没有撤销过，则合并到该条目：保留最初的 undo（或
  `beforeJson`），采用最新的 redo（或 `afterJson`）。
- 撤销和重做回调只恢复插件自身的状态（在其中写入场景会返回 `busy`）。抛出异常的回调算作插件的故障；该条目仍会移到另一个
  列表，`undo` / `redo` 的回复会带有警告，历史的其余部分保持不变。
- 插件被停用、进入故障状态或被卸载时，其条目会立即从两个列表中移除，其他条目保持顺序。新建或加载场景会清空历史。条目从不
  保存。

## 运行时覆盖（实验性）

`ctx.Overrides`（`IRuntimeOverrides`）在不更改场景的情况下，把仅用于显示的值叠加在场景之上。一个**槽位**代表场景模型中的
一个值：节点的位置、旋转或缩放，角色的 FK 骨骼旋转、动画行、动画速度或时间、眼睛和嘴的图案及张开程度，物品的颜色，灯光的
颜色，相机或景深；槽位也可以代表角色的骨骼或 IK 目标。当槽位持有值并胜出时，程序显示该值而不是模型的值；模型保留自己的值，
槽位被清除或释放后会再次显示该值。程序的时间轴和约束也通过同样的槽位叠加它们的值。这里的所有内容都是 `[Experimental]`。

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

命令 `myp.lift` 不更改任何会被保存的内容，因此不标记 `CommandFlags.Mutates`。

- **槽位。** `Acquire(nodeId, path, priority)` 返回插件在一个通道上的槽位：通道由节点 ID 和路径组成（对于场景范围的
  `camera` 和 `effects.depthOfField`，节点 ID 为 null）。插件在每个通道上最多持有一个槽位：再次获取同一通道会返回同一个
  槽位，并采用新的优先级。槽位开始时没有值。宿主没有场景（`Available` 为 false），或 ID 不指向任何节点（也不是可以通过撤销
  恢复的已删除节点）时，`Acquire` 返回 null。未知的路径、节点类型没有的路径（物品上的 `chara.*`）、没有节点 ID 的节点路径
  以及带有节点 ID 的场景范围路径会抛出 `ArgumentException`。

| 路径 | 节点 | 值（`Kind`）及显示方式 |
| --- | --- | --- |
| `transform.pos`、`transform.rot` | 角色、物品、灯光、文件夹、路线、相机 | `Vector3`；`Rotation`（局部旋转） |
| `transform.scale` | 角色、物品 | `Vector3`，与模型的缩放含义相同 |
| `chara.fk.bones["<bone>"]` | 角色 | `Rotation`，骨骼的局部旋转；与模型的 FK 值一样，只在 FK 姿势写入该骨骼的地方显示（FK 开启、骨骼所在的组开启、IK 未在求解它） |
| `chara.anime.ref` | 角色 | `Animation`：组、类别、编号（来源可选）；与正在播放的行不同时加载 |
| `chara.anime.speed` | 角色 | `Number`，不小于 0 |
| `chara.anime.time` | 角色 | `Number`，归一化时间（不回绕）：即使速度为 0，角色也会立即摆成该时间的姿势，并在槽位持有值期间停留在该时间 |
| `chara.face.eyes`、`chara.face.mouth` | 角色 | `Integer`，图案 ID |
| `chara.face.eyesOpen`、`chara.face.mouthOpen` | 角色 | `Number` |
| `chara.bone["<address>"].pos`、`chara.bone["<address>"].scale` | 角色 | `Vector3`，骨骼的局部位置或缩放 |
| `chara.bone["<address>"].rot` | 角色 | `Rotation`，骨骼的局部旋转 |
| `chara.ik.targets.<Name>.pos` | 角色 | `Vector3`，相对于角色根节点的目标位置 |
| `chara.ik.targets.<Name>.rot` | 角色 | `Rotation`，相对于角色根节点；仅限手和脚 |
| `item.colors[i].main`（i 为 0–3） | 物品 | `Color` |
| `light.color` | 灯光 | `Color` |
| `camera` | 场景 | `Camera`：注视点、旋转、距离、视野；优先于场景的视角和激活的相机节点 |
| `effects.depthOfField` | 场景 | `DepthOfField`，代替场景的景深 |

- **骨骼和 IK 目标。** `<address>` 与高级姿势使用的地址相同：身体和头部的骨骼为 `<name>`，其余为 `hair:<k>/<name>[#n]`、
  `clothes:<k>/<name>[#n]`、`acc:<id>/<name>[#n]`。`<Name>` 是 13 个 IK 目标之一：
  `Body`、`LeftShoulder`、`LeftArmChain`、`LeftHand`、`RightShoulder`、`RightArmChain`、`RightHand`、`LeftThigh`,
  `LeftLegChain`、`LeftFoot`、`RightThigh`、`RightLegChain`、`RightFoot`。错误的地址或名称，或在手和脚之外的目标上使用
  `.rot`，都会在 `Acquire` 时抛出 `ArgumentException`。骨骼的值在 FK 之后写入，不需要 FK 组，高级姿势的修正仍然叠加在它之上。
  地址在已构建的角色上无法解析时，或骨骼之后还会被其他阶段写入时（视线、嘴的宽度、关节修正、胸部），它不起作用。马赛克部件不可编辑：
  这类骨骼上的槽位不写入任何内容。IK 目标所在的组未应用时，它的值不起作用；槽位不会开启 IK。`Clear` 或 `Release` 之后，下一个
  动作阶段显示没有该值的姿势。
- **值**使用模型的单位。`Set(v)` 接受 Number 或 Integer（取整）；`Set(x, y, z)` 接受向量、以度为单位的欧拉角旋转（Unity 的
  顺序：Z、X、Y）、alpha 为 1 的颜色或动画行（组、类别、编号）；`Set(x, y, z, w)` 接受四元数或带 alpha 的颜色；
  `Set(double[])` 以数字序列接受任何类型（相机 8 个：注视点、欧拉角旋转、距离、视野；景深 7 个：启用为 0 或 1、焦点大小、
  光圈、模糊、焦点位置）；`SetJson(json)` 以 JSON 接受任何类型：数字、`[x, y, z]`、`[x, y, z, w]`、`[r, g, b, a]`、
  `{"source", "group", "category", "no"}`（`source` 可选）、
  `{"target": [x, y, z], "rot": [x, y, z], "dist": d, "fov": f}`（四项都必须给出）、
  `{"enabled", "focus", "focalSize", "aperture", "blur", "focusTo", "focusBlend"}`（省略 `focus` 时为注视点；后三个
  可选）。颜色是保存的值本身（sRGB 编码、非预乘 alpha、不截断）。与类型不符的形式（包括带有该形式未列出的成员的 JSON 对象）
  或非有限的数字会抛出 `ArgumentException`，并写明路径和可接受的形式（动画速度把 NaN 和负值视为 0）。设置与当前相同的值
  不会改变任何东西。`Clear()` 清除值但保留槽位；`Release()`（或 `Dispose()`）放弃槽位。槽位释放后，所有设置方法都返回
  false。
- **景深的焦点。** `null` 或 `"pivot"`（当前显示的相机的注视点）、`{"node": id}`（角色为两眼之间，没有时为头部；其他节点为
  其位置）、`{"node": id, "bone": name, "offset": [x, y, z]}`（节点下该名称的变换的位置，加上以世界坐标轴表示的偏移）、
  `{"point": [x, y, z]}`（世界坐标位置）或 `{"dist": d}`（相机前方 d 处）。找不到的节点或骨骼按注视点处理。`focusTo`
  （第二个焦点）和 `focusBlend`（0 到 1）混合两个焦点。`blur` 大于 0 时表示 1080 行画面上的最大模糊半径；为 0 或省略时
  保留渲染器自己的值。
- **优先级。** 在一个通道中持有值的槽位里，优先级最高者胜出；优先级相同时，后获取的胜出。时间轴槽位的优先级为 0；时间轴在
  播放、跳转或编辑轨道之后首次在某个通道显示值时获取该通道的槽位，在停止时全部释放。因此，只有当插件获取槽位时时间轴已经
  在显示该通道，优先级为 0 的插件槽位才会胜过时间轴；-1 让给时间轴，1 无论顺序如何都会胜出。约束槽位的优先级为 10：插件槽位
  取 11 或更高时位于约束之上，取 10 时后获取的胜出。`IsWinning` 表示槽位的值是否就是正在显示的值。
- **生命周期。** 槽位一直存在，直到插件释放它（`Release`、`Dispose`、`ReleaseAll`）。程序会在以下情况释放槽位：插件被
  停用、进入故障状态或被卸载时，释放该插件的所有槽位；新建或加载场景时，释放所有所有者的所有槽位（请在 `SceneLoaded` 中
  重新获取）；节点被遗忘（其 ID 再也不会恢复）时，释放该节点的槽位。已删除的节点保留其槽位（`TargetExists` 为 false），
  撤销使节点恢复后会再次显示。重新构建的角色保留其槽位，并在新的身体上显示。已释放的槽位仍会回答其读取成员。
- **显示时机。** 位置、旋转、缩放、颜色、表情值、动画速度和时间在设置时以及之后每次应用该模型值时显示，因此在 `Update`
  中设置的槽位会在同一帧显示。动画行在该帧场景更新结束时加载，FK 骨骼、骨骼和 IK 目标在下一个动作阶段显示，相机和景深在
  该帧的延后更新（渲染之前）显示。截图、`capture` 和序列帧导出都会拍下槽位所显示的内容。
- **不保存，也不进入撤销历史。** 设置槽位不运行命令，不记录撤销条目，不把场景标记为已更改，也从不写入文件：保存写入的是
  模型的值（由槽位驱动动画的角色的动画时间，以及带有 FK 骨骼槽位的角色的颈部和眼睛朝向，也按模型的值保存）。用户的编辑
  照常进入模型：检查器显示模型的值，并带有写明所有者的标记；Gizmo 拖动会更改模型（视图仍显示槽位的值）；插件的槽位控制
  相机期间，视口的相机操作被禁用，视口会显示控制相机的插件名称。
- **开销。** 以数字设置不分配内存，只需约 1 微秒；`SetJson` 会解析文本并分配内存。每帧设置槽位正是预期的用法：远比每帧调用
  命令便宜，也不会更改要保存的场景。
- **线程。** `Acquire`、`List`、`ReleaseAll`、设置方法、`Clear` 和 `Release` 仅限主线程（否则抛出
  `InvalidOperationException`）。`Available` 和槽位的读取成员（`NodeId`、`Path`、`Kind`、`Priority`、`IsReleased`、
  `HasValue`、`IsWinning`、`TargetExists`）可在任何线程上回答。插件卸载后，`ctx.Overrides` 与上下文的其他部分一样抛出
  `ObjectDisposedException`。
- **列表。** `List(nodeId)` 以 `OverrideInfo` 返回所有所有者持有值的槽位（全部，或某一个节点的）：`NodeId`、`Path`、
  `Owner`（`timeline`、`constraints` 或插件的 GUID）、`Priority`、`Winning`、`Effective`（槽位未胜出或其值当前不起作用时
  为 false：所在组关闭的 FK 骨骼、身体中没有的骨骼、上述情况下的骨骼和 IK 目标、找不到的焦点）、`TargetExists`。桥接命令
  `overrides.list {key?}` 返回同样的内容并
  附带值；`camera.get` 和 `camera.state` 描述显示中的相机，并在 `overridden` 中给出胜出的所有者，`effects.state` 则在
  `realized.depthOfField.overridden` 中给出；`plugin.list` 在 `overrides` 中列出每个插件的槽位，`plugin.disable` 返回
  释放的数量。

## 场景后置阶段（实验性）

入口类型同时实现 `IPluginScenePost` 的插件，会在所有运动之后收到一次调用：在所有角色、物品和约束都运动之后，在世界缩放的保持、
相机和景深之前。在这里读取到的是该帧的最终姿势。这里的所有内容都是 `[Experimental]`。

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

- **时机。** 每帧一次，每次场景步进一次，按插件加载顺序，在主线程上调用。场景暂停时 `frame.SceneDt` 为 0；步进时它是步进的
  时间间隔，且 `frame.Paused` 为 true。`frame.SceneTime` 与 `Update` 中一样，是本帧或本次步进之前的时钟。从这次调用内部开始的
  步进不会调用 `ScenePost`。
- **何时显示。** 在这里设置的节点槽位在本帧显示，因此相机节点或物品可以跟随最终的骨骼。骨骼和 IK 目标的槽位在下一个动作
  阶段显示。`ctx.Call` 可以使用，在这里进行的场景编辑与其他编辑相同。
- **直接写入骨骼。** 使用槽位时不需要这一步。在这里（角色的动作阶段之后）写入骨骼变换的插件，要用 `ctx.Unity().BoneWritten(nodeId, bone)`
  报告（节点不是已构建的角色时返回 false）：下一个动作阶段会去掉这次写入；否则高级姿势的修正和角色卡的骨骼修饰会把它当作新的
  输入保留下来。扭转骨骼和物理已经运行过，不会跟随这样的写入。
- **故障和时间。** 调用经过插件的保护，并计入 `scenePost`；进入故障状态的插件离开此阶段，其他插件继续运行。`plugin.list` 以
  `scenePostMs`（`median`、`max`、`frames`）给出耗时，只有实现此接口的插件才有。只有在已加载的插件实现了此接口时，此阶段才存在。

## 引擎侧服务

`ctx.Unity()`（`HS2Next.Api.Unity` 的扩展方法）返回 `IUnityPluginServices`；在没有引擎的宿主中返回 null：

- `NodeObject(id)`、`CharaRoot(id)`：场景节点的实体。支持读取。不支持更改场景模型所拥有的状态（变换、可见性、覆盖之外的
  材质）：下一次应用或构建可能会覆盖它。若要让运行时覆盖所涵盖的值显示为另一个值，请改为设置槽位。
- `BuildHooks`：在每次角色构建的固定时点运行的代码：`BodyMesh`（身体生成之后、皮肤合成之前：皮肤图层、身体部件替换、植入
  骨骼）、`Textures`（插件纹理阶段之后）和 `Final`（最后的插件阶段之后）。`Textures` 和 `Final` 会在重做该时点的编辑中再次
  运行，因此挂钩的结果必须只取决于角色的状态。
- `Materials`：对某个材质槽的浮点数、颜色、纹理或着色器，或渲染器启用状态的类型化覆盖。
- `AddMosaic(renderer)`：使渲染器成为马赛克成员。
- `SkinLayers(nodeId)`、`RequestRebuild(nodeId)`（`[Experimental]`）：构建之外的皮肤图层，以及重新构建角色（见下文）。
- `BoneWritten(nodeId, bone)`（`[Experimental]`）：报告在角色的动作阶段之后写入的骨骼（见“场景后置阶段”）。
- `Ui`（`[Experimental]`）：Studio 界面某个区域中的面板、某类节点的检查器分节、可通过 `ui.action` 和快捷键使用的操作，
  标签来自插件自己的字符串，并可使用程序的控件（`Ui.Widgets`）。

### 皮肤图层和身体部件替换（实验性）

在 `BodyMesh` 阶段，挂钩的 `IBuildHookArgs` 可以在构建的一次流程中更改角色的皮肤和身体：皮肤合成、部件绘制和法线混合都在
同一次构建中完成，没有第二次构建。

```csharp
ctx.Unity().BuildHooks.Register("tattoo", BuildStage.BodyMesh, args =>
{
    args.SetSkinLayer("tattoo", SkinLayerKind.BodyOver, tattooPng);   // PNG or JPEG bytes, or a Texture2D
});
```

- **皮肤图层。** `SetSkinLayer(id, kind, png)` 和 `SetSkinLayer(id, kind, texture)` 向皮肤的某个合成通道
  （`SkinLayerKind`：`BodyOver`、`FaceOver`、`BodyUnder`、`FaceUnder` 及其 `Detail` 形式）添加图层；空 ID 抛出
  `ArgumentException`；再次设置同一 ID 会替换它；图层在角色卡自身的图层之后按设置顺序合成。不是 PNG 或 JPEG 的字节返回
  false。纹理始终属于插件（程序从不销毁它；已销毁的纹理会被跳过并记录一行日志）。`RemoveSkinLayer(id)` 移除本插件该 ID 的
  图层（从不移除角色卡的或其他插件的图层）；`SkinLayers` 列出本次构建中本插件在该角色上的图层 ID。
- **身体部件替换。** `BodyParts` 列出角色身体对象下渲染器的名称。`ReplaceBodyPart(part, replacement)` 用一个 `BodyPart`
  原位替换其中之一（为 null 或没有网格时抛出 `ArgumentNullException`）：`Mesh`（必需）、`BoneNames`（按名称表示的网格骨骼
  顺序；null 表示部件当前的骨骼，此时网格的骨骼数必须相同）、`RootBone`、`Materials`（null 表示保留部件的材质；为 null 的
  元素保留该槽的材质）、`Normals`（一个 `BodyNormals`：`BodyNormals.FromAsset(zipmodGuid, bundle, asset)` 指向已安装 zipmod
  中的法线资源，GUID 为 null 或空时指向游戏数据中的资源，资源包或资源为空时抛出 `ArgumentException`；`BodyNormals.None`
  不进行混合；null 保持当前来源）。`BodyPart.From(renderer)` 从插件构建或加载的渲染器中一并获取这些内容。程序按名称把每根
  骨骼绑定到角色的身体骨骼上，没有时绑定到部件已在使用的同名骨骼（缺少的骨骼请先用 `ImplantBone` 植入），并按角色复制材质；
  在身体渲染器上，第一个槽保持为身体的皮肤材质，此处不使用 `Materials[0]`，其余元素填入其余各槽。结果为 `Replaced`、
  `NotFound`（没有该名称的部件）或 `Mismatch`（存在程序无法绑定的骨骼、未给出名称时骨骼数不同，或为没有蒙皮的部件给出了
  骨骼；不做任何更改）。替换身体渲染器后，身体被视为已替换，FK 提供者会添加它们的行。已替换的部件保留到该角色下一次构建为止。
- **构建之外。** `ctx.Unity().SkinLayers(nodeId)`（`ISkinLayers`：`Set`、`Remove`、`Ids`；节点不是已构建的角色时为 null）
  无需重新构建即可更改图层：下一次应用时会重新合成皮肤。这些图层与挂钩的图层共用 ID（对某个 ID 的后一次设置或移除生效），
  并加入该角色之后的每次构建。`RequestRebuild(nodeId)` 在下一次应用时重新构建角色，使其 `BodyMesh` 挂钩再次运行（这是在构建
  之后更改已替换部件的方法）；在该次应用之前的多次请求只会重新构建一次。
- `plugin.list` 统计每个插件的图层和已替换的部件（`skinLayers`、`bodyParts`）；角色的构建日志以 `<guid>/<id>` 列出每一项。
  插件被停用、进入故障状态或被卸载时，其在构建之外设置的图层会被移除（皮肤会重新合成）；停用时还会重新构建其挂钩涉及的
  角色，因此其部件也会随之移除。

**马赛克。** 马赛克保护覆盖以下各点：材质覆盖拒绝马赛克成员以及带有马赛克着色器的材质（这是警告，不是故障）；构建挂钩
没有紧接在马赛克之后的阶段；在运行过插件挂钩的构建中，`Final` 挂钩之后，宿主会把马赛克材质还给每个成员槽；`AddMosaic`
只会添加成员；着色器供应拒绝马赛克着色器。在每次构建中，程序自身内容的渲染器都遵循马赛克集合的规则。插件通过
`ReplaceBodyPart` 提供的身体部件按所提供的样子绘制；当插件用 `AddMosaic` 添加它时，它才是马赛克成员。

## 着色器

`ctx.Shaders.Supply(original, replacement)` 为之后加载的材质，把着色器名称映射到宿主可以按名称找到的替换着色器（例如插件
从自己的资源包中加载的着色器）。其他插件之后对同一名称的供应会叠加在上面；撤回后恢复下面的那个，卸载会撤回该插件的所有
供应。马赛克着色器既不能被供应，也不能被替换。

HS2Next 自身对 mod 和资源包着色器的替换是其兼容着色器，命名为 `HS2Next/Compatible/<其余部分>`（“文件 > 选项”的高级部分
末尾列出了它们的系列）；它对游戏和 Unity 着色器的替换命名为 `HS2Next/<其余部分>`。材质总是按其原始着色器名称查找，因此
供应时请指定原始名称。

## 版本

API 遵循语义化版本；`ApiVersion`（以及两个程序集的信息版本）给出版本号，程序集版本为 `MAJOR.0.0.0`，因此插件可以跨次版本和
修订版本绑定。本文档介绍 1.2。

- 插件在 `apiVersion` 中声明构建时所用的 `MAJOR.MINOR`。当宿主的主版本相同且次版本不低于它时，插件会被加载：`1.0` 的
  插件可在任何 1.x 宿主上加载，`1.2` 的插件需要 1.2 或更高版本的宿主。使用 1.1 新增成员的插件请声明 `"1.1"`，使用 1.2
  新增成员的插件请声明 `"1.2"`。
- 在同一主版本内，不会删除或更改任何公开内容。由宿主实现的接口（上下文、服务、事件、回复）可能在次版本中增加成员；插件
  只调用它们。由插件实现的接口（`IPlugin`、`IPluginUpdate`、`IPluginSceneLoaded`、`IPluginScenePost`、`IExtBlockHandler`、
  `IKkexConsumer`、`IBatchHook`、`IMotionStage`）从不改变：新功能以新的可选接口或委托的形式提供。
- 标记为 `[Experimental]` 的类型和成员不在承诺范围内，可能在任何次版本中改变。在 1.2 中，这包括表单（`HS2Next.Api.Ui`、
  `ctx.Forms`）、控件、撤销条目（`ctx.History`）、运行时覆盖（`ctx.Overrides`）、场景后置阶段（`IPluginScenePost`、
  `BoneWritten`）、皮肤图层、身体部件替换和重新构建请求，以及引擎侧的界面注册。调用辅助、截图事件及其种类以及命令
  `plugin.disable` / `plugin.enable` 是稳定的。
- 1.1 新增：使用 `CommandRequest` 和 `ICommandReply` 的 `Request` / `RequestAsync`、`IsActive`、截图事件、撤销条目、表单和
  控件、皮肤图层、身体部件替换、重新构建请求、运行时的停用和启用，以及所有公开成员的 XML 文档。
- 1.2 新增：运行时覆盖（`ctx.Overrides`、`IRuntimeOverrides`、`IOverrideSlot`、`OverrideValueKind`、`OverrideInfo`）及其
  骨骼和 IK 目标的路径，场景后置阶段（`IPluginScenePost`、`BoneWritten`），以及表示序列帧导出中各帧的截图种类 `Record`。

## 测试插件

- `plugin.list` 显示每个插件的状态、程序集、命令、扩展命名空间、处理程序、表单（`ui.forms`）、皮肤图层和已替换的部件
  （数量）、撤销条目（`history`，数量）、运行时覆盖的槽位（`overrides`）、故障、最近一次失败的启用（`enableError`）、
  更新耗时，以及实现 `IPluginScenePost` 的插件的后置阶段调用耗时（`scenePostMs`）。`overrides.list` 显示场景中的所有槽位及
  其所有者、优先级和值。
- `plugin.disable {"guid": "<guid>"}` 在程序运行期间卸载插件，`plugin.enable {"guid": "<guid>"}` 重新加载它（`persist: true`
  还会把选择记录到设置中，启用失败时也会记录并附带警告；`withDependents: true` 会先停用依赖它的插件）。回复中包含状态、
  耗时、等待重新构建的角色（`rebuilt`）、停用时释放的运行时覆盖槽位数（`overrides`），以及启用时重新读取的扩展块
  （`extBlocks`）。
  请把插件停用、启用几次，并比较前后的 `plugin.list`：
  它注册的一切都应当恢复，而它在上下文之外创建的东西不应不断累积。
- 针对 1.0 构建的插件也可以再次启用；此时回复会警告，这类插件可能假定每个进程只调用一次 `Load`。
- 不依赖引擎的逻辑可以在不运行程序的情况下，用引用同一个 `HS2Next.Api` 的普通 .NET 测试项目进行测试。

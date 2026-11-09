# HS2Next のプラグインの作り方

プラグインは、HS2Next が起動時に読み込む .NET のアセンブリです。ブリッジのコマンドを足す、シーン・キャラクター・
コーディネートに自分のデータを保存する、シーンのイベントや撮影に応じて動く、毎フレーム処理を走らせる、元に戻す履歴に
項目を加える、アプリ自身のウィジェットで描くパネルやインスペクターの節を出す、表示する値をシーンに重ねる、シェーダーを
供給する、といったことができます。エンジン側のアセンブリを使えば、キャラクターの組み立てへのフック（肌のレイヤー、
体のパーツの差し替え）、マテリアルの上書き、独自の UI、Studio のアイテムの一覧の行も加えられます。この文書はプラグイン API 1.3 を説明します。同じ内容の
[英語版](PLUGINS.md)と[簡体字中国語版](PLUGINS_zh-Hans.md)があり、隣の [COMMANDS.md](COMMANDS.md) にアプリの全コマンド
が載っています。

**プラグインは完全に信頼されたコードとして動きます。** プラグインはアプリの中でアプリと同じ権限で動き、サンドボックス
はありません。出どころを信頼できるプラグインだけを入れてください。

## プラグインキット

配布の zip には、`HS2Next.exe` の隣に `PluginKit/` フォルダーがあります。

| 項目 | 中身 |
| --- | --- |
| `PLUGINS.md`、`PLUGINS_ja.md`、`PLUGINS_zh-Hans.md` | この文書 |
| `COMMANDS.md` | コマンドの説明（プラグインが `Request` や `Call` で呼べるすべてのコマンド） |
| `HS2NextPlugin.props` | プロジェクトが共有するビルド設定。プロパティ `HS2NextDir` 一つで、すべての参照が見つかります |
| `PluginKit.sln` | 三つのサンプル。このフォルダーで `dotnet build` を 1 回実行するとすべてがビルドされます |
| `NuGet.config` | パッケージの取得元をすべて外します。キットはパッケージもネットワークも使いません |
| `Hello/` | エンジンを使わないサンプル。コマンド一つ、呼び出しの補助、イベントの受け取り |
| `HelloUi/` | エンジンを使わないサンプル。フォームとして宣言したパネルとアイテムのインスペクターの節、独自の文字列 |
| `Shapes/` | エンジン側のサンプル。Studio のアイテムの一覧のグループ一つと、コードで作る五つの行 |
| `doc/` | `HS2Next.Api` と `HS2Next.Api.Unity` の XML ドキュメント（IDE が見つけられるよう、`HS2Next_Data/Managed/` の DLL の隣にも置いてあります） |

サンプルのビルドには .NET SDK 8 以降が要ります（`dotnet --list-sdks` で確かめられます）。

1. `PluginKit` フォルダーでターミナルを開き、`dotnet build` を実行します。サンプルがアプリ自身の API アセンブリ
   に対してビルドされ、それぞれ `out/<guid>/`（`out/stp3.sample.hello/`、`out/stp3.sample.helloui/`、
   `out/stp3.sample.shapes/`）に写されます。
2. そのフォルダーを `HS2Next.exe` の隣の `Plugins` フォルダー（初めてなら作ります）に写し、
   `Plugins/stp3.sample.hello/manifest.json` のような形にします。
3. HS2Next を起動します。「ファイル > オプション」の自動操作の節にプラグインとその状態が並び、ブリッジの `plugin.list`
   で詳しく見られます。`hello.greet {"name": "kit"}` は `{"text": "Hello, kit!", "objects": …}` を返します。

`HS2NextDir` の既定値はキットの一つ上のフォルダー、つまり展開したアプリのフォルダーです。キットを別の場所へ写した
場合は、アプリのフォルダーを渡します: `dotnet build -p:HS2NextDir=<HS2Next.exe のフォルダー>`（絶対パス）。渡さないと
「HS2Next.Api.dll not found under …」でビルドが止まります。自分のプラグインを始めるときは、`Hello/` を新しいフォルダー
に写し、プロジェクト・アセンブリ・名前空間の名前を変え、マニフェストに自分の `guid` と `commandPrefixes` を書き、
プロジェクトの `PluginGuid` を同じ GUID にして、ソリューションに加えます（`dotnet sln add <フォルダー>/<名前>.csproj`）。
props ファイルにはほかのプロパティの説明もあります。`HS2NextUseUnity=true` でエンジン側の参照（`HS2Next.Api.Unity`、
`UnityEngine.CoreModule`、`UnityEngine.UIElementsModule`）が、`HS2NextUseJson=true` で `Newtonsoft.Json` が加わります。

## 二つの API アセンブリ

| アセンブリ | 参照 | 提供するもの |
| --- | --- | --- |
| `HS2Next.Api` | エンジンの型なし | `IPlugin` とライフサイクルのインターフェース、プラグインのコンテキスト、呼び出しの補助、イベント、元に戻す履歴の項目、実行時の上書き、フォーム（`HS2Next.Api.Ui`）、ブリッジのコマンドの取り決め（`HS2Next.Api.Bridge`）、拡張ブロックの取り決め（`HS2Next.Api.Save`）、コンテンツの ID、モーションの段階 |
| `HS2Next.Api.Unity` | UnityEngine | シーンのノードとキャラクターの実体、肌のレイヤーと体のパーツの差し替えを含む組み立てのフック、組み立て直しの要求、マテリアルの上書き、モザイクの対象の追加、UI の登録とアプリのウィジェット、ビューポートのツール、マップの問い合わせ、キャラクターのステージ、アイテムの行（試験的） |

エンジンの型を使わないプラグインは `HS2Next.Api` だけを参照します（JSON のライブラリが欲しければ `Newtonsoft.Json`
も。アプリはすべてのプラグインにバージョン 13 を提供します）。どちらのアセンブリもアプリの `HS2Next_Data/Managed/`
にあります。プラグインと一緒に配らないでください。アプリは自分のものを読み込みます。

## フォルダーとマニフェスト

プラグインは `<アプリ>/Plugins/<guid>/`（`HS2Next.exe` の隣）というフォルダーで、`manifest.json` と、そこに並べた
アセンブリを持ちます。プラグイン専用の依存先はフォルダーの中の `lib/` に置き、ローダーはそこから解決します。フォルダー
の名前はマニフェストの `guid` と同じでなければなりません（大文字と小文字は区別しません）。フォルダーに `strings.tsv`
があれば、プラグイン独自の UI の文字列になります（後述）。

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

| キー | 必須 | 規則 |
| --- | --- | --- |
| `manifestVersion` | はい | 整数の `1` |
| `guid` | はい | 逆ドメイン形式: `[A-Za-z0-9][A-Za-z0-9._-]{0,127}` で、ドットを一つ以上含みます。`core`、`bridge` と、`hs2.`、`hs2next.`、`illusion.` で始まる名前は予約済みです。GUID がプラグインのコマンドと拡張の名前空間の持ち主になります |
| `name` | はい | 1〜64 文字 |
| `version` | はい | semver の `MAJOR.MINOR.PATCH[-pre]` |
| `apiVersion` | はい | ビルドに使った API の `MAJOR.MINOR`（「バージョン」を参照） |
| `assemblies` | はい | プラグインのフォルダーにあるファイルの名前を一つ以上（パスなし、重複なし） |
| `entryType` | いいえ | `IPlugin` の型の完全な名前。省くと、引数なしの public コンストラクターを持つ、public で抽象でない `IPlugin` の型がただ一つあればそれを使います |
| `commandPrefixes` | いいえ | ドットで終わるコマンド名の接頭辞（`"seq."`）。プラグインが登録するコマンドはどれかで始まる必要があり、一つの接頭辞は一つのプラグインのものです |
| `dependencies` | いいえ | `[{"guid", "minVersion"}]`: これらの後に、これらがそのバージョン以上で読み込まれた場合だけ読み込みます |
| `loadAfter`、`loadBefore` | いいえ | 依存先ではないプラグインとの順序の希望。循環になる希望は捨てて報告します |
| `minAppVersion` | いいえ | アプリの最低バージョン（semver） |
| `author`、`license`、`url`、`description` | いいえ | `plugin.list` に出ます |

知らないキーは無視します（報告はします）。規則に反するマニフェストのプラグインは、`plugin.list` に出てログにも繰り返さ
れるコードで拒否されます: `plugin.badManifest`、`plugin.folderMismatch`、`plugin.duplicateGuid`（両方のプラグインが拒否
されます）、`plugin.reservedGuid`、`plugin.apiVersion`、`plugin.appVersion`、`plugin.missingAssembly`、
`plugin.assemblyCollision`、`plugin.prefixTaken`、`plugin.dependency`、`plugin.dependencyCycle`、`plugin.disabled`、
`plugin.staleAssembly`、`plugin.noEntry`、`plugin.typeLoad`、`plugin.loadFailed`。

読み込みは、設定の `plugins.enabled`、`plugins.dir`、`plugins.disabled`（GUID の一覧）、起動引数 `-noPlugins`、
環境変数 `HS2NEXT_NO_PLUGINS=1` で決まります。「ファイル > オプション」の自動操作の節にあるプラグインの切り替えは、
その場でプラグインを止めたり動かしたりし、その選択を `plugins.disabled` に記録します。`plugins.enabled` は次の起動から
効きます。

## ライフサイクル

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

- ホストは入口の型を引数なしの public コンストラクターで作り、起動の段階 `plugins`（最初のシーンの後）で、プラグインの
  コンテキストを渡して `Load` を呼びます。プラグインは依存の順に読み込まれます（同じ順位なら GUID の順）。
- `IPluginUpdate.Update` は毎フレーム 1 回、読み込みの順に走ります。ブリッジがそのフレームの要求を処理した後、シーンが
  変更を反映する前なので、ここでの書き込みは同じフレームに表れます。シーンが一時停止している間 `frame.SceneDt` は 0 で、
  一時停止中のコマ送りは代わりに `SceneStepped` のイベントで届きます。
- `IPluginSceneLoaded.SceneLoaded` は、シーンを新しく作るたび、読み込むたびに呼ばれ（シーンの拡張ブロックはその前に読ま
  れています）、プラグインの読み込みの直後にも `SceneLoadKind.Initial` で 1 回呼ばれます。
- `IPluginScenePost.ScenePost`（`[Experimental]`）は、すべての動きの後に、毎フレーム 1 回、コマ送りごとに 1 回呼ばれます
  （「シーンの後段」を見てください）。
- すべての呼び出しはメインスレッドで来ます。`Unload` が戻ると、ホストはプラグインがコンテキストを通して登録したものを
  すべて外します。それ以降、コンテキストは `Guid`、`Log`、`IsActive` 以外で `ObjectDisposedException` を投げます。
  障害状態になった後も `IsActive` は false です。
- アプリの実行中にプラグインを止め、また動かすことができます（`plugin.disable`、`plugin.enable`、オプションの切り替え）。
  動かすときは、読み込み済みのアセンブリから入口の型の**新しいインスタンス**を作ってもう一度 `Load` を呼び、開いている
  シーンについての `Initial` の `SceneLoaded` と `AppReady` を送ります。つまり `Load` は一つのプロセスで何度も、そのたびに
  新しいインスタンスで走ることがあります。
- Mono はアセンブリを解放しません。止めたプラグインのアセンブリはアプリを起動し直すまでメモリに残り、プラグインの
  フォルダーで差し替えた DLL は起動し直した後に読み込まれます（`plugin.enable` は `plugin.staleAssembly` を返します）。

**後片付けは `Unload` で。** コンテキストの外でプラグインがしたことは、すべてそこで元に戻してください: static フィールド、
作った GameObject とコンポーネント、登録した Unity のコールバック、始めたスレッドとタイマー、開いたままのファイル、
まだ返していない遅延の返答。止めたプラグインのアセンブリ、static な状態、オブジェクトはプロセスに残り、もう一度動かした
プラグインはそこから始まります。ホストが自分で外すもの: コマンド、イベントのハンドラー、拡張ブロックのハンドラー、
組み立てのフック、FK の提供元、シェーダーの供給、UI の登録とフォーム、元に戻す履歴の項目、実行時の上書きのスロット、
実行時の肌のレイヤー、`ctx.Frames` で予約したフレームの処理とフレームの時計のハンドラー、文字列。残るもの: プラグインが加えたモザイクの対象（対象を外す手段はありません）、
マテリアルの上書きで設定した値（そのオブジェクトが組み立て直されるまで）、書いたファイル。開いているシーンの
プラグインの拡張ブロックは、最後に読み込むか保存したときの値のままです。保存していないプラグインの状態は失われますが、
ファイルのデータは失われず、新しいインスタンスは `SceneLoaded` の前に開いているシーンのブロックを読み直します。

**障害。** プラグインへの呼び出しはすべてガードを通ります。例外はフックと例外の型ごとに 1 回、スタックとともにログに
出し、呼び出し元を止めません。数えた障害が 10 回になるとプラグインは障害状態になり、更新、後段の呼び出し、イベントが
止まり、ほかの登録が外され、`Unload` が 1 回試され、コマンドは `plugin_fault` を返します。障害状態のプラグインは
`plugin.enable` で読み込み直せます。

## コンテキスト

`IPluginContext` は、プラグインから見たホストです。

| メンバー | 中身 |
| --- | --- |
| `Guid`、`Name`、`Version`、`Directory` | マニフェストとプラグインのフォルダーから（フォルダーは読み取り専用として扱ってください。更新で置き換わることがあります） |
| `DataDirectory` | `<UserData>/Plugins/<guid>/`。プラグイン自身のデータの置き場で、初めて使うときに作られます |
| `Host` | アプリと API のバージョン、エディター・バッチ・プレイヤーの別、UI が動いているか、UserData のフォルダー |
| `Log` | 接頭辞 `[plugin <guid>]` 付きの Unity のログ。ブリッジの `log.tail` で見られます |
| `Settings` | `<UserData>/Plugins/<guid>/settings.json` を JSON の文字列で。形式はプラグインが決め、書き込みは原子的です |
| `Commands` | ブリッジのコマンドの登録先で、範囲が限られています。持ち主は GUID で、`commandPrefixes` の外の名前は拒否されます |
| `Frames`、`Readiness`、`Output` | ブリッジのフレームの時計（予約した処理と遅い時点の処理。外されたプラグインのものは実行されずに捨てられます。走っている処理はプラグインのコードとして数えるので、その間の停止は `busy` を返します）、準備の段階（プラグインが自分の段階を宣言できます）、撮影やダンプの出力先 |
| `Ext` | プラグインの名前空間の拡張ブロックのハンドラー |
| `Events` | メインスレッドの型付きイベント |
| `Clock` | シーンの時計（時刻、フレームの間隔、一時停止、フレーム数、コマ送り） |
| `Shaders` | 後から読み込まれるマテリアルのための、名前によるシェーダーの置き換え |
| `Request(command, args)`、`RequestAsync(…)` | 引数のオブジェクトでコマンドを呼び、型付きの返答を読みます（後述） |
| `Call(json)`、`CallAsync(json)` | 同じことを、要求の封筒を JSON の文字列で渡し、返答を文字列で受け取ります |
| `IsActive` | `Load` から `Unload` が戻るまで true。例外を投げません（止めた後に走るかもしれない継続の処理のため） |
| `History`（`[Experimental]`） | プラグイン自身の状態の変更を、シーンの元に戻す履歴に入れます |
| `Forms`（`[Experimental]`） | フォームとして宣言し、アプリが描くパネルとインスペクターの節 |
| `Overrides`（`[Experimental]`） | 表示のためにシーンに重ね、保存されない値（実行時の上書き） |

## コマンドの呼び出し

```csharp
ICommandReply r = ctx.Request("obj.transform", new { key = id, pos = new[] { 0.0, 1.0, 0.0 }, undo = false });
if (!r.Ok) ctx.Log.Warn(r.Code + ": " + r.Error);

var scene = ctx.Request("scene");
int objects = scene.Get<List<object>>("objects", new List<object>()).Count;

var req = new CommandRequest("chara.edit").Arg("key", id).Arg("path", "body.shape.height").Arg("value", 0.6);
req.Undo = true;
ctx.Request(req).ThrowIfError();
```

- `Request(command, args)` は封筒 `{"cmd": command, …args}` を作り、`Call` と同じくメインスレッドですぐに実行します。
  `RequestAsync` は `CallAsync` と同じく、ほかの要求の後ろに並べます（どのスレッドからでも、どのコマンドでも）。引数は、
  public なプロパティとフィールドがメンバーになるオブジェクト（匿名型、クラス）、`IDictionary<string, object>`（または
  文字列のキーを持つ組の並び）、Newtonsoft の `JObject`、または null です。ブリッジが返答を書くときと同じ規則で書きます。
  メンバーの名前は camelCase（`Pos` → `pos`。`[BridgeArg("name")]` で名前を変え、`[BridgeIgnore]` で外します）、列挙型は
  名前、浮動小数点数は往復しても変わらない形（NaN は null）、Unity のベクトルと色は配列、null も書きます。そのため、数値は
  HTTP のクライアントから送ったときとまったく同じに届きます。`args` に文字列・数値・真偽値・配列を渡すか、`cmd` という
  名前のメンバーがあると `ArgumentException` になります（封筒の文字列には `Call` を使います）。
- 封筒のメンバー（`id`、`strict`、`undo`、並べる形では `waitFrames` と `timeout` も）は、`args` のメンバーにも
  `CommandRequest` のプロパティにもできます。`CommandRequest.Arg` は引数の順を保ち、同じ名前を 2 回渡すとその場で値を
  置き換え、封筒の名前（`cmd`、`id`、`waitFrames`、`timeout`、`strict`、`as`、`undo`）を拒否します。これらはプロパティ
  で設定してください（`WaitFrames` が 0 なら送りません）。
- 返答（`ICommandReply`）: `Ok`、`Command`、`Code` と `Error`（成功なら null）、`Warnings`、`Json`（`Call` が返す返答の
  文字列そのもの）、`Has(member)`（あって null でない）、`Get<T>(member, fallback)`（ないか null なら fallback。数値は
  `int`・`long`・`float`・`double` に、配列は `T[]` か `List<T>` に、オブジェクトは `Dictionary<string, object>` かクラスに
  変換し、変換できない値はメンバーの名前を添えた `FormatException`。`object` として読んだ値は .NET の素の値で、`string`、
  `long`、`double`、`bool`、null、`List<object>`、`Dictionary<string, object>` のどれかです）、`GetJson(member)`、
  `As<T>()`（返答全体をクラスに）、`ThrowIfError()`（コード、メッセージ、詳細を持つ `BridgeException`）。
- コマンドのエラーで例外は出ません。`Ok` を見てください。同じ封筒なら、`Request` と `Call` は同じ返答の文字列を返します。
- ブリッジを無効にしても（`bridge.enabled` が false、`-noBridge`）、`Request`、`Call` とその後ろに並べる形は、有効なときと同じようにアプリのコマンドに届きます。

すぐに実行する形（`Request`、`Call`）は、`batch`、返答を遅らせることがあるコマンド、非同期のコマンド、`waitFrames` を
拒否します（並べる形を使います）。シーンを変えるコマンドは、ほかの誰かがシーンのトランザクションを持っている間（ドラッグ
中、実行中のバッチ）、シーンのイベントや撮影のイベントを配っている間、元に戻す・やり直すの処理の中では `busy` を返します。
そのため、プラグインの書き込みがほかの誰かの元に戻す一歩に紛れ込むことはありません。プラグインが自分の判断でシーンに
書き込むとき、その変更が利用者のものではなくプラグインの状態であれば、`"undo": false` を渡してください。

## コマンド

プラグインのコマンドは普通のブリッジのコマンドです。`help` に並び、HTTP のクライアントもほかのプラグインも呼べ、
[COMMANDS.md](COMMANDS.md) の返答の規則が当てはまります。コマンドは `CommandSpec`（名前、要約、型と既定値付きの引数、返す
フィールド、フラグ）とハンドラーの組です。ハンドラーはすぐに返答することも、後のフレームまで返答を遅らせることも、非同期
に走ることもできます。シーンを変えるコマンドには `CommandFlags.Mutates` を、読み取りだけのコマンドには
`CommandFlags.ReadOnly` を、バージョンの間で変わるかもしれないコマンドには `CommandFlags.Experimental` を付けてください。
プラグインのコマンドの持ち主は常にプラグインの GUID で、help のグループの既定値は GUID、`since` の既定値はプラグインの
バージョンです。

Hello のサンプルの全体:

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

## イベント

`ctx.Events`（`IAppEvents`）: `AppReady`、`SceneCleared`、`SceneLoaded`、`SceneSaving`、`SceneSaved`、`NodesAdded`、
`NodesRemoved`、`NodesMoved`、`NodeForgotten`（削除したノードが元に戻す操作でも戻らなくなった）、`CharaBuilt`、
`PausedChanged`、`SceneStepped`、`Quitting`、そして後述の撮影のイベント。ハンドラーはメインスレッドで、ホスト自身の
ハンドラーの後に走ります。例外を投げたハンドラーはそのプラグインの障害で、ほかのハンドラーは止まりません。同じ
プラグインのハンドラーに再入することはなく、ハンドラーの実行中に起きたイベントは、それが戻った直後に届きます。シーンの
変更はイベントの中ではなく、次の `Update` から行ってください。

**撮影。** スクリーンショット（F11、ツールバー、スクリーンショットのダイアログ、`ui.action capture.screenshot`、
`screenshot` コマンド）、`capture` コマンド、連番画像の書き出し（`record.start`、タイムラインのパネルの
「連番画像を書き出す」）の 1 フレームのたびに、三つのイベントが続きます。

| イベント | いつ | 使い道 |
| --- | --- | --- |
| `CaptureStarting`（`CaptureInfo`） | メインスレッドで、描画の直前 | プラグイン自身の重ね表示（作ったオブジェクト）を隠す |
| `CaptureRendered`（`CaptureInfo`） | 描画の直後、同じフレームで（描画に失敗したときも） | それをまた表示する |
| `CaptureFinished`（`CaptureResultInfo`） | ファイルを書いた後か撮影に失敗した後。スクリーンショットと書き出しのフレームでは数フレーム後、`capture` では同じフレーム | 結果を読む |

`CaptureInfo`: `Id`（実行ごとに 1 から）、`Kind`（`Screenshot`、`Capture`、`Record`）、`Origin`（`Ui`、`Command`）、`Width`、
`Height`（出力の大きさ）、`Supersample`（1、2、4）、`RenderWidth`、`RenderHeight`、`DrawsFrame`（シーンのフレーム画像を
描くか）、`FrameNumber`、`Path`（書くファイル）。`CaptureResultInfo`: `Capture`、`Ok`、`Path`（失敗なら null）、`Bytes`、
`Error`、`Milliseconds`。一つの撮影の三つのイベントは、同じ `CaptureInfo` のオブジェクトを渡します。`CaptureStarting` の後には必ず `CaptureRendered` と `CaptureFinished` が 1 回ずつ続きます。何も
描く前に拒否された要求（大きさの誤り、画素数の上限、まだ終わっていないスクリーンショット）では、どれも起きません。撮影
のイベントを配っている間、`Request` と `Call` によるシーンの変更は `busy` を返すので、自分のオブジェクトは直接隠して
ください。シーン・キャラクター・コーディネートのサムネイルでは撮影のイベントは起きません。

連番画像の書き出しは、書くフレームごとに三つのイベントをフレームの順に起こします。`Kind` は `Record`、`Origin` は
`record.start` なら `Command`、ダイアログなら `Ui`、`Path` はそのフレームのファイルです。全体の一時停止中のフレームは
書かれず、イベントも起きません。書き出しの間、シーンは実際の時間にかかわらず 1 フレームに一定の一歩ずつ進みます
（`frame.SceneDt` は 1 / fps）。シーンの時計で自分のオブジェクトを動かすプラグインはその歩みのとおりに書き出され、実際の
時間を使うプラグインはそうなりません。

## シーン・キャラクター・コーディネートへのデータの保存

プラグインは、自分の名前空間（GUID か `<guid>.<名前>`）の拡張ブロックとしてデータを保存します。`ctx.Ext.Register` で
`IExtBlockHandler` を登録します。

- `Scopes`: ブロックの置き場所。キャラクターの外見、コーディネート（アクセサリーごと、服のパーツごとも）、シーン、
  シーンのノード一つ。
- `Read(IExtReadContext)` は、ファイルを読み込むときにブロックを解釈します（インラインの JSON の値と、コンテナの
  `ext/<名前空間>/` にあるブロックのファイル）。データは取っておき、反映は `Read` の中ではなく後で（`CharaBuilt`、
  `SceneLoaded`）行います。
- `Write(IExtWriteContext)` は、ファイルを保存するときにブロックを書きます（インラインの JSON の値とファイル。パスは
  コンテナの文法で 200 文字まで）。何も書かなければ「ブロックなし」です。
- `Version`（ハンドラーが書くデータのバージョン）はブロックと一緒に保存され、`Read` が古いデータを移行します。ハンドラー
  が拒否したブロック（たとえば新しすぎるデータのバージョン）は変えずに取っておき、不明なブロックとして書き戻します。
  プラグインが入っていないか止まっているブロックも同じです。
- `RequiredByDefault` は、それがないと内容が不完全か誤解を招くデータの印です。プラグインのないローダーはそれを報告します。
- `IKkexConsumer` も実装したハンドラーは、カードから取り込んだ HS2 のプラグインのデータを受け取ります。キャラクターか
  コーディネートのファイルを読み込む間、プラグインの ID が `KkexIds` にある取り込んだ項目ごとに 1 回、同じインスタンスの
  ハンドラー自身の `Read` の後に `ReadKkex` が呼ばれます（そこにハンドラーのブロックがなくても）。

ソースのリポジトリにあるシーケンサーのサンプル（`Samples/Sequencer`）は、シーケンスをシーンのブロックに保存しており、
この使い方の大きめの例です。

## フォーム（試験的）

フォームは、プラグインがデータとして宣言し、アプリが自身のウィジェットで描くパネルやインスペクターの節です。アプリの
インスペクターと同じスライダー、色の選択、選択肢、ピッカーが使われます。値はプラグインの読み取りのコマンドから来て、
書き込みのコマンドで戻ります。`HS2Next.Api.Ui` と `ctx.Forms`（`IPluginForms`）のすべてが `[Experimental]` です。キットの HelloUi の
サンプルが一通りそろった例です。

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

**二つのコマンド。** どちらも `Load` で、プラグイン自身の接頭辞の下に登録します。読み取りのコマンドは
`{"form": <id>, "node": <ノードの ID か null>}` を受け取り（パネルでは null、節では調べているノード。節の値はノードの ID
ごとに持ちます）、`values` を返します。書き込みのコマンドは `{"form", "node", "key", "value", "phase"}` を受け取り、成功
（中身は問いません）か、アプリが通知に出すメッセージのエラーを返します。書き込みが変えるのはシーンではなくプラグイン
自身の状態なので、`CommandFlags.Mutates` を付けないでください（`undo` と `redo` の書き込みはシーンがその一歩を反映して
いる間に走り、そこではシーンを変えるコマンドが `busy` を返します）。読み取りには `CommandFlags.ReadOnly` を付けられます。

- **登録。** `ctx.Forms.Panel(id, titleKey, region, form, order)`（region は `left`、`right`、`bottom`。タイトルはパネル
  のタブになります）と `ctx.Forms.Section(kind, id, titleKey, form, order)`（kind は `chara`、`item`、`light`、`camera`、
  `folder`、`route` で、その種類のノードのインスペクターの節になります）。`Remove(id)`、`Refresh(id, nodeId)` はすぐに
  読み直します（たとえばプラグイン自身のコマンドなど、ほかの手段で値を変えた後に。node が null なら表示中のすべての
  ノード）。ID はプラグイン自身のもので、アプリは `<guid>/<id>` として持ちます。フォームは登録時に検査され、**写されます**。
  その後に `UiForm` や行を変えても効きません。UI が動いていない所（バッチのプレイヤー、`-noUi`）では `Available` が false
  で、フォームは検査され `plugin.list`（`ui.forms`）で数えられますが、描かれません。プラグインを外すか止めると、すべて
  外れます。
- **行。** `UiForm(readCommand, writeCommand)`（`Undo`、`Rows`）、`UiChoice(value, labelKey)`。`UiRow` には種類ごとの
  ファクトリーがあります: `Float`、`Int`（整数）、`Bool`、`Color`（既定でアルファあり）、`Vec2`、`Vec3`、`Enum`、`Ref`、
  `File`、`Text`、`Node`、`Button`、`AttachPoint`、`PartList`、`Push`（ColorHdr の行は、その `Kind` を持つ `UiRow` です）。
  設定できるメンバー: `Key`（フォームの中で一意。値の名前）、`LabelKey`、`TooltipKey`、`Min` と `Max`（スライダーの範囲）、
  `TypedMin` と `TypedMax`（入力した値はこの範囲に収めます）、`Whole`、`Default`（戻すボタンの値。null なら戻すボタン
  なし）、`Format`、`Scale`（表示の倍率）、`Grid`、`GridTruncate`、`Wrap360`、`Nullable`、`ReadOnly`、`ColorAlpha`、
  `Choices`（Enum の行）、`ChoicesCommand`（Ref と Node の行。`{"choices": [{"value", "label"}]}` を返すコマンド）、
  `Command` と `CommandArgs`（Button の行）、`CategoryFemale` と `CategoryMale`（PartList の行。キャラクターの編集の一覧の
  カテゴリー）、`PushSource`（Push の行。`facePreset`、`layout:6`、`layout:7`、`colour:336`、`colour:305` のどれか）。
  `RowKind` はアプリ自身の行の種類と同じ名前と値を持ちます。`HairBundles` は予約済みで拒否されます。
- **登録時の検査**（`ArgumentException` で、何も登録されません。フォームが null なら `ArgumentNullException`）: 空か重複
  した ID（UI にすでに出ているものも）、一覧にない region や kind、空の読み取りのコマンド、プラグインの `commandPrefixes`
  の外にある読み取り・書き込み・ボタン・選択肢のコマンド（接頭辞と同じ名前も）、null の行、空か重複した行のキー、`Max`
  より大きい `Min`、選択肢のない Enum の行、コマンドのない Button の行、書き込みのコマンドがないのに編集できる行（Button
  以外で `ReadOnly` でない行）、カテゴリーのない PartList の行、一覧にない供給元の Push の行、`HairBundles` の行、定義され
  ていない種類、JSON の値でない `Default` や `CommandArgs`。

アプリはプラグインのコマンドをすぐに実行する形で呼びます（返答を遅らせるか非同期に走るフォームのコマンドは拒否され、
フォームにそのエラーが出ます）。

| 呼び出し | アプリが送る封筒 | アプリが読む返答 |
| --- | --- | --- |
| 読み取り | `{"cmd": <ReadCommand>, "form": <id>, "node": <ノードの ID か null>}` | `values`（行のキー → 値）、任意で `hidden` と `disabled`（キーの一覧）、`choices`（キー → `[{"value", "label"}]`。行の選択肢を置き換えます） |
| 書き込み | `{"cmd": <WriteCommand>, "form", "node", "key", "value", "phase", "undo": false}`。phase は `preview`、`commit`、`cancel`、`undo`、`redo` | 成功、またはエラー（アプリはそのメッセージを通知に出します） |
| ボタン | `{"cmd": <row.Command>, "form", "node", "key"}` と行の `CommandArgs` | 成功かエラー（通知） |
| 選択肢 | `{"cmd": <row.ChoicesCommand>, "form", "node", "key"}` | `choices: [{"value", "label"}]` |

JSON での値: Float と Int は数値、Bool は真偽値、Color は `[r, g, b, a]`（0〜1 の数値四つ）、ColorHdr は
`{"color": [r, g, b, a], "intensity": n}`、Vec2 と Vec3 は `[x, y]` と `[x, y, z]`、Enum は選択肢の値（文字列）、Ref・
Node・File・Text・AttachPoint は文字列か null、PartList はパーツの `"guid#id"` か null、Button と Push は値なし（Push の行
のマスを押すと、そのマスの `"guid#id"` を値にした `commit` を送ります）。`values` にない値は「—」と表示します。形の違う
値も「—」と表示し、その行に印を付けて、ログに 1 回出します（`[plugin <guid>] form <id> row <key>: …`）。読み取りに失敗
すると、行の代わりに「値を読み込めませんでした: <コード>: <メッセージ>」を出し、次に読み取れるまで行は使えなくなります。

- **利用者に見えるもの。** 行はインスペクター自身のウィジェットです。入力欄と戻すボタン（`Default`）の付いた数値の
  スライダー、ベクトルの入力欄、切り替え、区切りボタン（選択肢四つまで）かドロップダウン、色の見本とアプリの色の選択、
  参照のピッカー（選択肢は `ChoicesCommand` か読み取りの `choices` から。`ChoicesCommand` のない Node の行はシーンのノード
  を並べます）、画像ファイルのピッカー、文字の入力欄（Enter かフォーカスが外れたときに送ります）、ボタン、取り付け位置
  のピッカー、行のカテゴリーのキャラクター編集のサムネイル一覧（節のキャラクターの性別で。それ以外は女性の一覧）、
  キャラクター編集のプッシュのグリッド。
- **書き込みと元に戻す履歴。** ドラッグ中は動くたびに `preview` の書き込みを送り（1 フレームに 1 回まで）、離したときに
  `commit`、Esc で `cancel`（ドラッグの前の値）を送ります。値の入力、切り替え、選択、ピックは `commit` を 1 回送ります。
  どの段階でも、表示すべき値が付いてきます。`UiForm.Undo` が true（既定値）なら、アプリは値が変わった commit ごとに
  （ドラッグ全体で一つ）、プラグインの名前で、行のラベルを名前にした元に戻す項目を記録します。その元に戻す操作は編集前に
  読んだ値を phase `undo` で、やり直す操作は確定した値を phase `redo` で書きます。同じ行への入力は一つの項目にまとめられ
  ます。このとき、書き込みのコマンドはこれらの書き込みについて自分で元に戻す項目を記録してはいけません（封筒に
  `"undo": false` とあります）。`Undo` が false ならアプリは何も記録しません。
- **読み取り。** フォームを初めて描くとき、`commit`・`cancel`・`undo`・`redo`・ボタンの後、`Refresh` のとき、
  `SceneLoaded` と `SceneCleared` の後、節なら調べているノードが変わったとき。フォームとノードの組ごとに 1 フレームに
  1 回までです。
- **ラベル。** ラベル・タイトル・ツールチップ・選択肢のキーは、プラグインの文字列のキーです。プラグインのフォルダーの
  `strings.tsv`（列は `key`、`en`、`ja`、`zh-Hans`、`note`。キーは `plugin.<guid>.` で始まります）が `Load` の前に加え
  られるほか、エンジン側では `Ui.AddStrings` も使えます。文字列のないキーは、キーそのものが表示されます。返答の選択肢の
  ラベルは、`plugin.<guid>.` で始まればそのキー、それ以外はそのままの文字です。
- **要素の名前**（テストの `ui.query` と `ui.input` のため）: フォームは `plugin-form-<guid>/<id>`、行は
  `plugin-form-<guid>/<id>-<key>`（`-field`、`-slider`、`-reset`、`-toggle`、`-dropdown`、`-swatch`、`-text`、`-button`
  などの部分付き）、エラーの行は `…-error`、パネルのタブは `tab-<guid>/<id>` です。

## エンジン側の UI のためのウィジェット（試験的）

`ctx.Unity().Ui` は、UI Toolkit の要素で作ったパネル、インスペクターの節、操作を登録します（`IPluginUi`）。節を作る関数は
`IUiSectionContext`（インスペクターが表示しているノードの `NodeId` とその `Kind`）を受け取ります。その `Widgets`
（`IUiWidgets`。UI がなければ null）で、そうしたプラグインもアプリ自身のウィジェットを、プラグインのコールバックにつない
だ形で使えます（どのコールバックもプラグインのガードの中で呼ばれ、プラグインが外れた後は呼ばれません）: `Number`
（ドラッグの段階を持つスライダー）、`Vector`、`Toggle`、`Choice`、`Color`、`Ref`、`File`、`Text`、`Button`、
`AttachPoint`。どれも `IUiWidget`（`Element`、コールバックなしで値を表示する `SetValue(value, mixed)`、読み書きできる
`Enabled`）を返します。`Number`、`Vector`、`Color` のコールバックは値とその `EditPhase` を受け取ります。ドラッグや色の
選択が動いている間は `Preview`、編集を終えたときは `Commit`、Esc でドラッグを終えたときや色の選択を元に戻したときは
`Cancel` です。ほかに `Toast(text, level)`（そのままの文字。`ToastLevel` は `Info`、`Success`、`Warning`、`Error`）、`Dialog`（コールバックは押したボタンの番号、ほかの
方法で閉じたときは -1）、`Confirm`、`Prompt`（取り消したときは null）、`UiMenuItem(labelKey, run)` の項目（`Enabled` の
既定値は true）を並べる `Menu(items, anchor)` があります。上のフォームも同じウィジェットで描かれます。要素の名前は、
`plugin-widget-<labelKey>` と同じ部分の接尾辞、ダイアログは `dialog-plugin-<guid>` とボタン `dialog-button-<番号>`、
メニューは `plugin-menu-<guid>` と項目 `menu-item-plugin-<guid>-<番号>` です。

## 元に戻す履歴の項目（試験的）

`ctx.History`（`IPluginHistory`）は、プラグイン自身の状態の変更を、プラグインが呼んだコマンドの項目と並べて、シーンの
元に戻す履歴に入れます。

```csharp
float before = weight, after = weight + 1;
weight = after;                                        // the change is made first
ctx.History.Record("plugin.example.myplugin.weight",  // a key of the plugin's strings, or plain text
    undo: () => weight = before, redo: () => weight = after, mergeKey: "weight");
```

- `Record(label, undo, redo, mergeKey)` は、プラグインが**すでに行った**変更を記録します。`Record(label, beforeJson,
  afterJson, apply, mergeKey)` は同じことを二つの状態で行います（元に戻すと `apply(beforeJson)`、やり直すと
  `apply(afterJson)`）。結果は `HistoryRecordResult` の `Recorded`、`Merged`、`Busy`、`NoScene` のどれかです。
  `CanRecord` は今記録を受け付けるかを、`Count` は元に戻す一覧とやり直す一覧にあるプラグインの項目の数を返します。
  メインスレッド専用で、null のコールバックや `apply` は `ArgumentNullException` になります。
- 項目はアプリ自身の項目と同じ一覧に入ります。やり直す一覧は捨てられ、200 項目の上限が当てはまり、シーンは未保存の
  変更ありになり、元に戻す・やり直す（キー、メニュー、ブリッジの `undo` / `redo`）は、プラグインの項目とアプリの項目を
  作られた順にたどります。`plugin.<guid>.` で始まるラベルは、プラグインの文字列から引きます。
- シーンのトランザクションが開いている間（ドラッグ中、クライアントのバッチ。プラグイン自身のコマンドがその中で走って
  いる場合も）、シーンがコマンドを反映しているかイベントを配っている間、元に戻す・やり直すのコールバックの中では、記録は
  `Busy` で拒否されます。
- 最新の項目と同じ null でない `mergeKey` の記録は、その項目がプラグインのもので、その後に元に戻す操作がなければ、その
  項目にまとめられます。最初の undo（または `beforeJson`）が残り、最新の redo（または `afterJson`）が使われます。
- 元に戻す・やり直すのコールバックは、プラグイン自身の状態だけを戻します（その中からのシーンへの書き込みは `busy` を返し
  ます）。例外を投げたコールバックはプラグインの障害として数え、項目はそれでももう一方の一覧へ移り、`undo` / `redo` の
  返答に警告が付き、履歴のほかの部分は残ります。
- プラグインが止められる、障害状態になる、外されると、その項目は二つの一覧からすぐに消え、ほかの項目は順序を保ちます。
  新しいシーンや読み込んだシーンは履歴を消します。項目が保存されることはありません。

## 実行時の上書き（試験的）

`ctx.Overrides`（`IRuntimeOverrides`）は、シーンを変えずに、表示する値だけをシーンの上に重ねます。**スロット**は、
シーンのモデルの一つの値を受け持ちます。ノードの位置・回転・拡大縮小、キャラクターの FK の骨の回転、アニメの行・速さ・
時刻、目と口のパターンと開き、アイテムの色、ライトの色、カメラ、被写界深度です。キャラクターの骨と IK の標的も受け持ち
ます。スロットが値を持ち、ほかのスロットに勝っている間、アプリはモデルの値の代わりにその値を表示します。モデルは自分の
値を持ち続け、スロットを空にするか手放すと、その値がまた表示されます。アプリのタイムラインと拘束も、同じスロットを通して
値を重ねます。ここにあるものはすべて `[Experimental]` です。

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

コマンド `myp.lift` は保存されるものを何も変えないので、`CommandFlags.Mutates` を付けません。

- **スロット。** `Acquire(nodeId, path, priority)` は、一つのチャンネル（ノードの ID とパス。シーン全体の `camera` と
  `effects.depthOfField` ではノードの ID は null）についての、プラグインのスロットを返します。プラグインが一つの
  チャンネルに持てるスロットは一つで、同じチャンネルをもう一度取得すると、優先度を新しい値にした同じスロットが返ります。
  スロットは値のない状態で始まります。ホストにシーンがない（`Available` が false）とき、ID がどのノードも指さない（元に
  戻す操作で戻せる削除済みのノードでもない）ときは null を返します。知らないパス、ノードの種類にないパス（アイテムに
  `chara.*`）、ノードの ID のないノードのパス、ノードの ID のあるシーン全体のパスは `ArgumentException` になります。

| パス | ノード | 値（`Kind`。`OverrideValueKind`）と表示 |
| --- | --- | --- |
| `transform.pos`、`transform.rot` | キャラクター、アイテム、ライト、フォルダー、ルート、カメラ | `Vector3`、`Rotation`（ローカルの回転） |
| `transform.scale` | キャラクター、アイテム | `Vector3`。モデルの拡大縮小と同じ意味 |
| `chara.fk.bones["<bone>"]` | キャラクター | `Rotation`。骨のローカルの回転。モデルの FK の値と同じく、FK の姿勢がその骨を書くところ（FK がオン、骨のグループがオン、IK が解いていない）でだけ表示されます |
| `chara.anime.ref` | キャラクター | `Animation`: グループ、カテゴリー、番号（ソースは任意）。再生中の行と違うときに読み込まれます |
| `chara.anime.speed` | キャラクター | `Number`。0 以上 |
| `chara.anime.time` | キャラクター | `Number`。正規化した時刻（折り返しません）。速さが 0 でもすぐにその時刻の姿勢になり、スロットが値を持つ間はその時刻にとどまります |
| `chara.face.eyes`、`chara.face.mouth` | キャラクター | `Integer`。パターンの ID |
| `chara.face.eyesOpen`、`chara.face.mouthOpen` | キャラクター | `Number` |
| `chara.bone["<address>"].pos`、`chara.bone["<address>"].scale` | キャラクター | `Vector3`。骨のローカルの位置、拡大縮小 |
| `chara.bone["<address>"].rot` | キャラクター | `Rotation`。骨のローカルの回転 |
| `chara.ik.targets.<Name>.pos` | キャラクター | `Vector3`。キャラクターのルートから見た標的の位置 |
| `chara.ik.targets.<Name>.rot` | キャラクター | `Rotation`。キャラクターのルートから見た回転。手と足だけ |
| `item.colors[i].main`（i は 0〜3） | アイテム | `Color` |
| `light.color` | ライト | `Color` |
| `camera` | シーン | `Camera`: 注視点、回転、距離、画角。シーンの視点と、有効なカメラのノードより優先されます |
| `effects.depthOfField` | シーン | `DepthOfField`。シーンの被写界深度の代わりに使われます |

- **骨と IK の標的。** `<address>` は高度なポーズと同じ書き方です。体と頭の骨は `<name>`、ほかは `hair:<k>/<name>[#n]`、
  `clothes:<k>/<name>[#n]`、`acc:<id>/<name>[#n]` です。`<Name>` は 13 個の IK の標的の名前です:
  `Body`、`LeftShoulder`、`LeftArmChain`、`LeftHand`、`RightShoulder`、`RightArmChain`、`RightHand`、`LeftThigh`,
  `LeftLegChain`、`LeftFoot`、`RightThigh`、`RightLegChain`、`RightFoot`。間違ったアドレスや名前、手と足以外の
  標的の `.rot` は、`Acquire` で `ArgumentException` を投げます。骨の値は FK の後に書かれ、FK のグループを必要としません。
  高度なポーズの補正はその上に載ったままです。組み立てたキャラクターでアドレスが解決できない間と、別の段がその後で書く骨
  （視線、口の幅、関節補正、胸）では効きません。モザイクの部材は編集できません。そのような骨のスロットは何も書きません。IK の標的の
  値は、その標的のグループが適用されていない間は効きません。スロットが IK をオンにすることはありません。`Clear` か
  `Release` の後は、次のモーションの段階で値のない姿勢が表示されます。
- **値**はモデルの単位で渡します。`Set(v)` は Number と Integer（丸めます）、`Set(x, y, z)` はベクトル、度のオイラー角
  による回転（Unity の順: Z、X、Y）、アルファが 1 の色、アニメの行（グループ、カテゴリー、番号）、`Set(x, y, z, w)` は
  クォータニオンかアルファ付きの色、`Set(double[])` は数の並びでどの種類も受け取ります（カメラは 8 個: 注視点、オイラー
  角の回転、距離、画角。被写界深度は 7 個: 有効を 0 か 1、焦点の大きさ、絞り、ぼかし、焦点の位置）。`SetJson(json)` は
  JSON でどの種類も受け取ります: 数、`[x, y, z]`、`[x, y, z, w]`、`[r, g, b, a]`、
  `{"source", "group", "category", "no"}`（`source` は任意）、
  `{"target": [x, y, z], "rot": [x, y, z], "dist": d, "fov": f}`（四つとも必要）、
  `{"enabled", "focus", "focalSize", "aperture", "blur", "focusTo", "focusBlend"}`（`focus` を省くと注視点。後ろの
  三つは任意）。色は保存される値そのもの（sRGB で符号化、ストレートのアルファ、範囲に切り詰めない）です。種類に合わない
  形（その形にないメンバーを持つ JSON のオブジェクトも）や有限でない数は、パスと受け付ける形を書いた
  `ArgumentException` になります（アニメの速さは NaN と負の値を 0 として扱います）。今と同じ値を設定しても何も
  変わりません。`Clear()` は値を消してスロットを残し、`Release()`（または `Dispose()`）はスロットを手放します。手放した
  後の設定はどれも false を返します。
- **被写界深度の焦点。** `null` か `"pivot"`（表示中のカメラの注視点）、`{"node": id}`（キャラクターなら両目の間、
  なければ頭。ほかのノードはその位置）、`{"node": id, "bone": name, "offset": [x, y, z]}`（ノードの下にあるその名前の
  トランスフォームに、ワールドの軸での offset を足した位置）、`{"point": [x, y, z]}`（ワールドの位置）、`{"dist": d}`
  （カメラの前方 d）です。見つからないノードや骨は注視点として扱います。`focusTo`（二つ目の焦点）と `focusBlend`（0〜1）
  で二つの焦点を混ぜます。`blur` が 0 より大きいと、1080 行の画面での最大のぼかしの半径になり、0 か省略ならレンダラー
  自身の値のままです。
- **優先度。** 一つのチャンネルで値を持つスロットのうち、優先度が最も高いものが勝ち、同じ優先度なら後から取得したものが
  勝ちます。タイムラインのスロットの優先度は 0 で、タイムラインは再生、シーク、トラックの編集の後でチャンネルに初めて
  値を表示するときにそのチャンネルのスロットを取得し、停止のときにすべて手放します。そのため優先度 0 のプラグインの
  スロットがタイムラインに勝つのは、プラグインが取得したときに、タイムラインがすでにそのチャンネルを表示していた場合
  だけです。-1 ならタイムラインに譲り、1 なら順序にかかわらず勝ちます。拘束のスロットの優先度は 10 です。拘束より上に
  置くには 11 以上にします。10 なら後から取得したスロットが勝ちます。`IsWinning` で、スロットの値が表示されている値
  かどうかがわかります。
- **寿命。** スロットは、プラグインが手放す（`Release`、`Dispose`、`ReleaseAll`）まで残ります。アプリが手放すのは、
  プラグインが止められる、障害状態になる、外されるときのそのプラグインのすべてのスロット、新しいシーンを作るかシーンを
  読み込むときのすべての持ち主のすべてのスロット（`SceneLoaded` で取得し直してください）、忘れられた
  （ID が二度と戻らない）ノードのスロットです。削除したノードはスロットを持ち続け（`TargetExists` は false）、元に戻す操作でノードが戻ると
  また表示されます。組み立て直したキャラクターはスロットを持ち続け、新しい体に表示します。手放したスロットも、読み取りの
  メンバーには答え続けます。
- **表示される時点。** 位置、回転、拡大縮小、色、表情の値、アニメの速さと時刻は、設定した時点と、その後モデルの値を反映
  するたびに表示されるので、`Update` で設定したスロットは同じフレームに表示されます。アニメの行はそのフレームのシーンの
  更新の最後に読み込まれ、FK の骨、骨、IK の標的は次のモーションの段階で、カメラと被写界深度はそのフレームの遅い更新
  （描画の前）で表示されます。スクリーンショット、撮影、連番画像の書き出しは、スロットが表示しているものを写します。
- **保存も元に戻す履歴もありません。** スロットの設定はコマンドを走らせず、元に戻す履歴の項目を作らず、シーンを変更あり
  にせず、ファイルに書かれることもありません。保存はモデルの値を書きます（スロットがアニメを動かすキャラクターのアニメの
  時刻と、FK の骨のスロットがあるキャラクターの首と目の向きも、モデルが持つ値のまま保存されます）。ユーザーの編集は
  これまでどおりモデルに入ります。インスペクターはモデルの値を、持ち主の名前を示す印を付けて表示し、ギズモのドラッグは
  モデルを変え（表示はスロットの値のまま）、プラグインのスロットがカメラを持っている間はビューポートのカメラ操作が止まり、
  ビューポートにカメラを動かしているプラグインの名前が出ます。
- **負荷。** 数で渡す設定はメモリーを割り当てず、1 マイクロ秒ほどで済みます。`SetJson` は文字列を解析し、割り当てを行います。
  毎フレームスロットを設定するのが本来の使い方で、毎フレームコマンドを呼ぶよりずっと軽く、保存されるシーンも変えません。
- **スレッド。** `Acquire`、`List`、`ReleaseAll`、設定、`Clear`、`Release` はメインスレッド専用です（ほかのスレッドでは
  `InvalidOperationException`）。`Available` とスロットの読み取りのメンバー（`NodeId`、`Path`、`Kind`、`Priority`、
  `IsReleased`、`HasValue`、`IsWinning`、`TargetExists`）は、どのスレッドでも答えます。プラグインが外された後は、
  コンテキストのほかの部分と同じく、`ctx.Overrides` は `ObjectDisposedException` を投げます。
- **一覧。** `List(nodeId)` は、値を持つすべての持ち主のスロット（全部か、一つのノードの分）を `OverrideInfo` で返します:
  `NodeId`、`Path`、`Owner`（`timeline`、`constraints`、プラグインの GUID のどれか）、`Priority`、`Winning`、`Effective`
  （勝っていないとき、または今は値が効かないとき false。グループがオフの FK の骨、体にない骨、上に挙げた場合の骨と IK の
  標的、見つからない焦点）、`TargetExists`。ブリッジの
  コマンド `overrides.list {key?}` は同じものを値付きで返します。`camera.get` と `camera.state` は表示中のカメラを示し、
  勝っている持ち主を `overridden` に入れます。`effects.state` では `realized.depthOfField.overridden` です。`plugin.list`
  はプラグインごとのスロットを `overrides` に並べ、`plugin.disable` は手放した数を返します。

## シーンの後段（試験的）

入口の型が `IPluginScenePost` も実装するプラグインには、すべての動きの後に呼び出しが 1 回来ます。すべてのキャラクター、
アイテム、拘束が動いた後で、ワールドの拡大縮小の保持、カメラ、被写界深度の前です。ここで読む値はそのフレームの最終的な姿勢です。
ここにあるものはすべて `[Experimental]` です。

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

- **呼ばれる時。** 毎フレーム 1 回、コマ送りごとに 1 回、読み込みの順に、メインスレッドで呼ばれます。シーンが一時停止して
  いる間 `frame.SceneDt` は 0 で、コマ送りではその間隔になり、`frame.Paused` は true です。`frame.SceneTime` は `Update` と
  同じく、このフレームかコマ送りの前の時刻です。この呼び出しの中で始めたコマ送りでは `ScenePost` は呼ばれません。
- **いつ表示されるか。** ここで設定したノードのスロットはこのフレームに表示されます。そのためカメラのノードやアイテムを
  骨の最終的な位置に追従させることができます。骨と IK の標的のスロットは次のモーションの段階で表示されます。`ctx.Call` は使え、ここで
  行うシーンの編集はほかの編集と同じです。
- **骨を直接書くとき。** スロットならこの手順は要りません。ここで、つまりキャラクターのモーションの段階の後に骨のトランス
  フォームを書くプラグインは、`ctx.Unity().BoneWritten(nodeId, bone)` で知らせます（ノードが組み立て済みのキャラクターでなければ
  false）。すると次のモーションの段階がその書き込みを取り除きます。知らせないと、高度なポーズの補正とカードの骨の修飾が
  それを新しい入力として残します。ひねりの骨と物理はすでに走った後なので、この書き込みには追従しません。
- **障害と時間。** 呼び出しはプラグインのガードを通り、`scenePost` として数えます。障害状態になったプラグインは後段から
  外れ、ほかのプラグインは続きます。`plugin.list` は時間を `scenePostMs`（`median`、`max`、`frames`）で返します。これが出る
  のは、このインターフェースを持つプラグインだけです。読み込まれたプラグインのどれかがこのインターフェースを持つ間だけ、
  この段は存在します。

## エンジン側のサービス

`ctx.Unity()`（`HS2Next.Api.Unity` の拡張メソッド）は `IUnityPluginServices` を返します。エンジンのないホストでは null
です。

- `NodeObject(id)`、`CharaRoot(id)`: シーンのノードの実体です。読むことはできます。シーンのモデルが持つ状態（トランス
  フォーム、表示、上書き以外のマテリアル）を変えることはできません。次の反映や組み立てで上書きされることがあります。
  実行時の上書きが受け持つ値を別の値で見せたいときは、代わりにスロットを設定してください。
- `BuildHooks`: すべてのキャラクターの組み立ての決まった時点で走る処理です。`BodyMesh`（体を出した後、肌を合成する前:
  肌のレイヤー、体のパーツの差し替え、骨の追加）、`Textures`（プラグインのテクスチャの段階の後）、`Final`（最後の
  プラグインの段階の後）。`Textures` と `Final` はその時点をやり直す編集でもう一度走るので、フックの結果はキャラクターの
  状態だけで決まるようにしてください。
- `Materials`: マテリアルのスロット一つの float・色・テクスチャ・シェーダー、またはレンダラーの有効・無効の型付きの
  上書き。
- `AddMosaic(renderer)`: レンダラーをモザイクの対象にします。
- `SkinLayers(nodeId)`、`RequestRebuild(nodeId)`（`[Experimental]`）: 組み立ての外での肌のレイヤーと、キャラクターの
  組み立て直し（後述）。
- `BoneWritten(nodeId, bone)`（`[Experimental]`）: キャラクターのモーションの段階の後に書いた骨を知らせます（「シーンの
  後段」を見てください）。
- `Ui`（`[Experimental]`）: Studio の画面の領域のパネル、ノードの種類ごとのインスペクターの節、`ui.action` とショート
  カットから使える操作。ラベルはプラグイン自身の文字列から取り、アプリのウィジェット（`Ui.Widgets`）も使えます。
- `Tools`（`[Experimental]`）: ビューポートのポインター、ホイール、キーを受け取るツールと、面の拾い（後述）。UI が動いて
  いないときは null です。
- `Map`（`[Experimental]`）: 読み込まれたマップのルートと、点の下の面の高さ（後述）。
- `Stages`（`[Experimental]`）: シーンのキャラクターに付けるモーションのステージ。組み立て直しを越えて保たれます（後述）。
- `ItemRows`（`[Experimental]`）: プラグインが行を作る Studio のアイテムの一覧のグループ（後述）。

### 肌のレイヤーと体のパーツの差し替え（試験的）

`BodyMesh` の段階では、フックの `IBuildHookArgs` で、組み立ての一回の流れの中でキャラクターの肌と体を変えられます。
肌の合成、パーツの描画、法線の混合が同じ組み立ての中で行われ、二回目の組み立てはありません。

```csharp
ctx.Unity().BuildHooks.Register("tattoo", BuildStage.BodyMesh, args =>
{
    args.SetSkinLayer("tattoo", SkinLayerKind.BodyOver, tattooPng);   // PNG or JPEG bytes, or a Texture2D
});
```

- **肌のレイヤー。** `SetSkinLayer(id, kind, png)` と `SetSkinLayer(id, kind, texture)` は、肌の合成の一つ
  （`SkinLayerKind`: `BodyOver`、`FaceOver`、`BodyUnder`、`FaceUnder` と、それぞれの `Detail` の形）にレイヤーを加えます。
  空の ID は `ArgumentException` です。同じ ID でもう一度設定すると置き換わり、レイヤーはカード自身のレイヤーの後に、
  設定した順に合成されます。PNG でも JPEG でもないバイト列は false になります。テクスチャはプラグインのもののままです
  （アプリは破棄せず、破棄されたものはログに 1 行出して飛ばします）。`RemoveSkinLayer(id)` はこのプラグインのその ID の
  レイヤーを外します（カードのものやほかのプラグインのものは外しません）。`SkinLayers` は、この組み立てでこのキャラクター
  に付けたプラグインのレイヤーの ID の一覧です。
- **体のパーツの差し替え。** `BodyParts` は、キャラクターの体のオブジェクトの下にあるレンダラーの名前の一覧です。
  `ReplaceBodyPart(part, replacement)` は、そのうちの一つをその場で `BodyPart` に差し替えます（null やメッシュのないものは
  `ArgumentNullException`）: `Mesh`（必須）、`BoneNames`（メッシュの骨の順を名前で。null ならパーツの今の骨で、そのとき
  メッシュの骨の数は同じでなければなりません）、`RootBone`、`Materials`（null ならパーツのものをそのまま。null の要素は
  そのスロットのマテリアルを残します）、`Normals`（`BodyNormals`。`BodyNormals.FromAsset(zipmodGuid, bundle, asset)` は
  入っている zipmod の法線のアセットを、GUID が null か空ならゲームのデータのものを指し、空のバンドルやアセットは
  `ArgumentException` です。`BodyNormals.None` は混ぜません。null なら今の元のまま）。`BodyPart.From(renderer)` は、
  プラグインが作るか読み込んだレンダラーからこれらをまとめて取ります。アプリは骨を一つずつ名前でキャラクターの体の骨に、
  なければパーツがすでに使っている同じ名前の骨に結び付け（足りない骨は先に `ImplantBone` で加えます）、マテリアルは
  キャラクターごとに写します。体のレンダラーでは、最初のスロットは体の肌のマテリアルのままで `Materials[0]` は使われず、
  ほかの要素がほかのスロットに入ります。結果（`BodyPartResult`）は `Replaced`、`NotFound`（その名前のパーツがない）、`Mismatch`（アプリが
  結び付けられない骨がある、名前がないのに骨の数が違う、スキンのないパーツに骨を渡した。何も変えません）のどれかです。
  体のレンダラーを差し替えると体は差し替え済みとして扱われ、FK の提供元が自分の行を加えます。差し替えたパーツは、その
  キャラクターの次の組み立てまで残ります。
- **組み立ての外で。** `ctx.Unity().SkinLayers(nodeId)`（`ISkinLayers`: `Set`、`Remove`、`Ids`。ノードが組み立て済みの
  キャラクターでなければ null）は、組み立て直さずにレイヤーを変えます。次の反映で肌がもう一度合成されます。これらの
  レイヤーはフックのレイヤーと ID を共有し（ある ID への後からの設定か削除が勝ちます）、そのキャラクターの以後のすべての
  組み立てに加わります。`RequestRebuild(nodeId)` は次の反映でキャラクターを組み立て直すので、`BodyMesh` のフックがもう
  一度走ります（組み立ての後で差し替えたパーツを変える方法です）。その反映までの要求は 1 回の組み立て直しになります。
- `plugin.list` は、プラグインごとのレイヤーと差し替えたパーツを数えます（`skinLayers`、`bodyParts`）。キャラクターの
  組み立てのログには、それぞれが `<guid>/<id>` で載ります。プラグインを止める、障害状態になる、外すと、組み立ての外で
  設定したレイヤーは外れ（肌はもう一度合成されます）、止めたときはフックが触れたキャラクターが組み立て直されるので、
  そのパーツも外れます。

**モザイク。** モザイクの保護が掛かるのは次の点です: マテリアルの上書きは、モザイクの対象と、モザイクのシェーダーを持つ
マテリアルを拒否します（障害ではなく警告）。組み立てのフックには、モザイクの直後の段階がありません。プラグインのフックが
走った組み立てでは、`Final` のフックの後で、ホストが対象のすべてのスロットにモザイクのマテリアルを戻します。`AddMosaic`
は対象を加えるだけです。シェーダーの供給はモザイクのシェーダーを拒否します。どの組み立てでも、アプリ自身の内容の
レンダラーはモザイクの対象の規則に従います。プラグインが `ReplaceBodyPart` で供給した体のパーツは、供給されたとおりに
描かれます。プラグインが `AddMosaic` で加えたときは、モザイクの対象になります。

### ビューポートのツール（試験的）

ツールは、動いている間ビューポートのポインター、ホイール、キーを受け取ります。キャラクターの上の一点を拾う、何かを
ドラッグする、面の上に線を引くといった操作に使います。`ViewportTool` を継承して要る呼び出しだけを上書きし、`Tools.Begin` で
始めます。`Tools` は UI が動いていないときは null なので、ツールを始めるときに読んでください。

```csharp
sealed class PickPoint : ViewportTool
{
    readonly IViewportTools tools;
    readonly Action<ToolPick> done;

    public PickPoint(IViewportTools tools, Action<ToolPick> done) { this.tools = tools; this.done = done; }

    public override bool PointerDown(in ToolPointer p)
    {
        if (p.Button != 0 || !p.Hit.Hit) return false;
        done(p.Hit);
        tools.End(this);
        return true;
    }
}

var tools = ctx.Unity().Tools;
tools?.Begin(new PickPoint(tools, hit => ctx.Log.Info(hit.Part + " " + hit.Point)),
    new ToolOptions { Filter = new ToolPickFilter { CharactersOnly = true }, Hint = "plugin.my.plugin.pick" });
```

- **ツールが動いている間。** カメラとその押し続けるキー、ショートカット、ギズモ、選択、ビューポートのメニューは止まります。
  それらの状態は変わらないので、ツールが終わればすべて元のとおりに戻ります。パネル、メニュー、ダイアログは入力を受け取り
  続け、元に戻す操作はメニューから使えます。`ToolOptions.PassThrough` を付けると、ツールが取らなかった入力はツールがない
  ときと同じに働きます。`PointerDown` が false を返した押下はカメラを動かすか選択になり、取らなかったキーやホイールも
  いつもどおり働きます。同時に動くツールは一つで、`Begin` は前のツールを終わらせます（`Replaced`）。ダイアログが開いている
  ときのように、ホストが断ると `Begin` は false を返します。
- **呼び出し。** 最初に `Started`、最後に `Ended(reason)` が来ます。`PointerDown` が true を返すとその押下はツールのものに
  なり、`PointerUp` まで、または離さずに終わったとき（Esc、ウィンドウがフォーカスを失った）の `PointerCancel` まで、ポインターは
  ツールに留まります。`PointerMove` は、ツールが取った押下の間と、ボタンを押していないホバーのときに来ます。ツールが
  取らなかった押下の間には来ません。`Wheel`（ノッチ数、奥へ回すと正）と `Key`（キーマップの名前: `Space`、`T`、`F1`。リピートと
  Esc は来ません）は、入力を取ったときに true を返します。`Draw(IToolDraw)` はビューの 1 フレームに 1 回走ります。線、十字、
  文字をワールド座標で描け、ビューにだけ出て、撮影や録画には出ません。ツールがシーンを変えるときは、ほかの場所と同じように
  コマンドか元に戻す履歴の項目を通します。
- **ポインター。** `ToolPointer` は `Panel`（パネルの中の位置）、`Ray`（ワールド座標）、`Button`（0 左、1 右、2 中。移動では
  -1）、`Buttons`（押しているボタン。ビット 1、2、4）、`Mods`（`ToolMods`: `Ctrl`、`Shift`、`Alt`）、`Hit` を持ちます。`Hit` は
  `ToolOptions.Filter` に従ったポインターの下の拾いです。最初に読んだときに走り、キャラクターでは数ミリ秒かかるので、`Ray` で
  自前の問い合わせをするツールはこの時間を払いません。渡された呼び出しの中で読んでください。
- **拾い。** `Tools.Pick(ray, filter)` は、ツールの有無によらず、いつでも同じ答えを返します。`ToolPick` は null になりません。
  外れたときは `Hit` が false です。ノード（`NodeId`）、場所（`Point`、`Normal`、`Distance`）、部品（`Renderer`、`Part`、`Slot`）、
  三角形（`Triangle`、`Uv`）、そこで重みの最も大きい骨（`Bone`、`BoneLocal`、`BoneAddress`）を示します。読めないメッシュと UV の
  ない部品では `Precise` が false で、点と距離だけが正しく、三角形のメンバーは使えません。`ToolPickFilter` は一つのノード
  （`NodeId`）やキャラクター（`CharactersOnly`）に絞ります。`MapSurface` は、ノードに当たらなかったときにマップの面を足します
  （`OnMap`）。隠れているレンダラーには当たりません。
- **終わり。** `ToolEnd` が理由を示します。`Finished`（`End`）、`Cancelled`（押下のないときの Esc、またはツールが例外を投げた）、
  `Replaced`、`SceneChanged`（新しいシーンや読み込み、または `Filter.NodeId` のノードの削除）、`UiClosed` です。押下の間の Esc は
  `PointerCancel` になり、ツールは残ります。押下を持ったままツールが終わると、ほかの呼び出しは来ません。
- **失敗。** ツールの呼び出しが投げた例外はプラグインの失敗として数えられ（`plugin.list` では `tool <呼び出し>`）、ツールは
  終わります。プラグインが停止、失敗、アンロードされると、動いているツールは、プラグインの `Unload` の後に `UiClosed` で
  終わります。

### マップの問い合わせ（試験的）

`ctx.Unity().Map`（`IMapQuery`、null になりません）は、マップが読み込まれているかと、その面の高さを答えます。

```csharp
var map = ctx.Unity().Map;
if (map.Surface(point, out var hit)) Place(hit.Height, hit.Normal);
```

- `Loaded`、`Roots`: マップが読み込まれているかと、そのルートのオブジェクトのすべて（マップは複数持つことがあります。読み込まれて
  いないときは空です）。
- `Surface(point, out hit, above, below)`: 点の x、z の下で、`point.y + above` から `point.y - below` の間にある最も高い面
  （既定は 0 と 60）。面がないとき、マップが読み込まれていないときは false です。法線の y が 0.3 未満の面は壁なので数え
  ません。立っている点を渡してください。屋根の下の点は床を、屋根より上の点は屋根を見つけます。
- `Surfaces(points, hits, above, below)`: 同じことを複数の点について 1 回の待ちで行います。答えのあった点の数を返し、答えの
  ない点では `hits[i].Source` が null です。`hits` には `points` 以上の長さが要ります。
- `MapSurfaceHit`: `Height`、`Normal`（長さ 1、上向き）、`Source`（見つけ方: `gpu`、`mesh`、`terrain`、`collider`）。

答えはコライダーの有無やメッシュが読めるかによりません。例外はグラフィックスデバイスがないときで、そのときはこの二つから
答えます。アルファで抜いた面と透明な面も数えます（水、ガラス、葉）。キャラクターとアイテムはマップではありません。呼び出しは
答えを待ちます。およそ 1 ミリ秒で、マップの最初の 1 回は長くかかるので、クリックや数点には向きますが、毎フレーム多くの点を
問うのには向きません。メインスレッド専用です。コマンド `ground.probe` も同じ問い合わせをします。

### キャラクターのステージ（試験的）

`ctx.Unity().Stages`（`ICharaStages`、null になりません）は、シーンのキャラクターにモーションのステージを足します。ステージは
プラグインのクラスで、キャラクターのモーションの段階の中で、アプリ自身のステージの間に入って動きます。組み立て直しは体を
作り直すので、プラグインが渡すのはステージそのものではなく、ステージを作る関数です。

```csharp
sealed class HeadWatch : IMotionStage
{
    readonly Transform head;

    public HeadWatch(GameObject root) { head = Array.Find(root.GetComponentsInChildren<Transform>(), t => t.name == "cf_J_Head"); }

    public Vector3 Position { get; private set; }
    public MotionPhase Phase => MotionPhase.Final;
    public string Name => "head";
    public bool Enabled { get; set; } = true;
    public void Step(in MotionFrame frame) { if (head) Position = head.position; }
    public void Reset() { }
}

IDisposable stage = ctx.Unity().Stages.Add(nodeId, root => new HeadWatch(root));
```

- **作る関数。** キャラクターが組み立て済みならその場で呼ばれ、組み立て直しのたびに新しい体のルートを渡して呼ばれます。
  `chara.change`、`RequestRebuild`、新しい体が要る編集は組み立て直しになります。服、髪、アクセサリーの変更はならないので、
  ステージはそのまま残ります。`SceneLoaded` の時点のようにまだ組み立てられていないキャラクターには、組み立てられたときに
  付きます。古いステージのオブジェクトは古い体と一緒に消えるので、組み立て直しを越えて残したいものは、関数がキャプチャする
  変数に置きます。止まっていたステージは止まったままです。関数が null を返すと、その体にはステージが付きません。
- **フェーズ。** `Phase` と `Name` は関数の後に一度だけ読みます。フェーズは -90 から 940 の間です。これより下には骨を元に
  戻すアプリのステージが、上にはフレームを締めくくるステージがあります。`MotionPhase` が主なフェーズに名前を付けており、
  その間のどの数も使えます。たとえば 420 のステージは FK の後、補正の前に動きます。範囲の外のフェーズは拒否され、プラグイン
  の障害に数えられます。`chara.motion` はキャラクターのステージを実行順に並べます。プラグインのステージの名前は
  `<guid>/<Name>` で、`owner` は GUID です。コマンドの `stages` 引数で切り替えられます。
- **骨を書くとき。** 骨を書くステージは、早いフェーズに置いた自分のステージで、その書き込みを自分で取り除きます。アニメー
  ションが動いていないとき、ほかに骨を戻すものはありません。
- **フレームの中で。** ステージはキャラクターのモーションの段階の中で動きます。拘束のためにシーンがキャラクターの順番を決めて
  いる間も同じです。シーンの一時停止中は `MotionFrame.Dt` が 0 です。ホストが姿勢を読むためだけにステージを動かして骨を元に
  戻すときは、プラグインのステージは動きません。
- **終わり。** 返り値の `Dispose` でステージを外します。2 回目の呼び出しは何もしません。ステージ自身の `Step` の中から呼ぶと、
  ステージはその場で止まり、そのフレームのうちに一覧から外れます。削除されたキャラクターのステージは、元に戻す操作に備えて
  止まったまま残り、ノードが戻らないと決まったときに消えます。新しいシーンと読み込みですべてのステージが終わります。保存は
  されないので、`SceneLoaded` で足し直してください。
- **障害。** ステージからの例外はプラグインの障害に数えられ（`plugin.list` の名前は `stage <guid>/<Name>`）、ステージは
  残ります。関数からの例外は `stage factory` として数えられ、次に組み立てられるまでそのキャラクターにステージは付きません。
  プラグインの停止、障害、アンロードで、そのステージはすべて外れます。キャラクターでないノードには `Add` が
  `ArgumentException` を投げます。メインスレッド専用です。

### アイテムの行（試験的）

`ctx.Unity().ItemRows`（`IItemRows`、null になりません）は、プラグイン自身が行を作るグループを Studio のアイテムの一覧に
足します。行はほかのアイテムと一緒に一覧と `item.search` に並び、その行のアイテムが組み立てられるたびに、アプリはプラグインに
オブジェクトを求めます。

```csharp
IItemRowGroup group = ctx.Unity().ItemRows.AddGroup(820001, "Shapes", request => MakeShape(request.Id));
group.SetCategory(0, "Shapes");
group.SetRows(new[]
{
    new ItemRow { Category = 0, Id = 1, Name = "Cube" },
    new ItemRow { Category = 0, Id = 4, Name = "Torus", FkBones = new[] { "ring0", "ring1" } },
});
```

- **グループ。** グループの番号は、アイテムの一覧のグループと同じ一つの番号の空間にあります。一覧、アプリ、ほかのプラグインが
  使っている番号は `ArgumentException`（`rows.groupTaken`）で拒否されるので、自分の番号を選んでください。グループは空で
  始まります。`SetCategory` でカテゴリーに名前を付け、`SetRows` ですべての行を一度に渡します（ID はグループの中で一意）。
  カテゴリーは行を持つ間だけ一覧に出て、グループは一覧のグループの後に並びます。
- **行。** 行のキーは `<プラグインの GUID>#<ID>` です。行のアイテムはほかのアイテムと同じように保存され、`ref` がこのキー、
  それにグループとカテゴリーが付きます。`item.add` は mod のアイテムと同じく `{"ref": "<guid>#<id>"}` か
  `{"group", "category", "guid", "no"}` を受け取ります。
- **オブジェクト。** `make` はアイテムの組み立て（追加、読み込み、複製、組み立て直し）の中でメインスレッドで呼ばれ、
  `ItemRowRequest`（行の `Guid`、`Group`、`Category`、`Id` と、アイテムの `NodeId`）を受け取り、呼ばれるたびに新しい
  オブジェクトか null を返します。アプリはそのオブジェクトを引き取り、アイテムの下にローカルの原点で置き、一覧の
  アイテムと同じようにマテリアルのスロットをすべて写してシェーダーを置き換え、行の `FkBones` を名前で探し（アイテムの FK が
  動かします）、アイテムがなくなるときに壊します。モザイクの対象の名前を
  持つレンダラーと、モザイクのマテリアルを持つレンダラーは対象です。ほかは `AddMosaic` で加えます。`make` はシーンのノードを
  足したりシーンを変えたりしません（警告 `item.generatorWrote`）。
- **変化。** `SetRows` は、行が新しいか、名前・カテゴリー・FK の骨が変わったアイテムを次のフレームで組み立て直します。行が
  まだないうちに代わりのアイテムで読み込まれたアイテムは、そのとき行のオブジェクトになります。`Refresh(id)` は一つの行の
  アイテムを組み立て直します。`Load` でグループを足すプラグインは、こうして起動時に読み込まれたシーンにも届きます。
- **プラグインがないとき**（入っていない、停止した、障害で止まった）、その行のアイテムは `item.missing` 付きで代わりの
  アイテムとして読み込まれ、保存されたデータはそのまま保たれます。もう一度保存したシーンは変わらず、プラグインが戻れば
  ふたたび行のオブジェクトになります。
- **終わり。** `Dispose` でグループとその行が消えます。プラグインのアンロード、停止、障害でも同じです。組み立て済みの
  アイテムは、組み立て直されるまで今のオブジェクトを保ち、そのとき代わりのアイテムとして読み込まれます。
- **障害。** `make` が例外を投げるか null を返すと、警告 `item.generatorFailed` 付きで代わりのアイテムが読み込まれ、
  プラグインの障害に数えられます（`plugin.list` の名前は `item rows <グループ>`）。メインスレッド専用です。

キットのサンプル `Shapes/` は、コードで作る行を五つ足します。その一つのトーラスは、八つの骨をアイテムの FK で曲げられます。

## シェーダー

`ctx.Shaders.Supply(original, replacement)` は、後から読み込まれるマテリアルのために、シェーダーの名前を、ホストが名前で
見つけられる置き換え（たとえばプラグインが自分のアセットバンドルから読み込んだもの）に結び付けます。ほかのプラグインが
同じ名前を後から供給すると上に積まれ、取り下げると下のものに戻り、外すとそのプラグインの供給はすべて取り下げられます。
モザイクのシェーダーは供給も置き換えもできません。

mod やアセットパックのシェーダーに対する HS2Next 自身の置き換えは互換シェーダーで、`HS2Next/Compatible/<残り>` という
名前です（「ファイル > オプション」の詳細の節の最後にその系統が並びます）。ゲームと Unity のシェーダーに対する置き換えは
`HS2Next/<残り>` という名前です。マテリアルは常に元のシェーダーの名前で引くので、供給には元の名前を指定します。

## バージョン

API はセマンティック バージョニングに従います。`ApiVersion`（と両方のアセンブリの情報バージョン）がそれを示し、
アセンブリのバージョンは `MAJOR.0.0.0` なので、プラグインはマイナーとパッチのリリースをまたいで結び付きます。この文書は
1.3 を説明します。

- プラグインは、ビルドに使った `MAJOR.MINOR` を `apiVersion` に書きます。ホストのメジャーが同じで、マイナーが同じか
  それ以上なら読み込まれます。`1.0` のプラグインはどの 1.x のホストでも、`1.3` のプラグインは 1.3 以降のホストで読み込
  まれます。1.1 で加わったメンバーを使うプラグインは `"1.1"`、1.2 で加わったメンバーを使うプラグインは `"1.2"`、1.3 で
  加わったメンバーを使うプラグインは `"1.3"` と書きます。
- 同じメジャーの中では、公開されたものを消したり変えたりしません。ホストが実装するインターフェース（コンテキスト、
  サービス、イベント、返答）は、マイナーのバージョンでメンバーが増えることがあり、プラグインはそれを呼ぶだけです。
  プラグインが実装するインターフェース（`IPlugin`、`IPluginUpdate`、`IPluginSceneLoaded`、`IPluginScenePost`、
  `IExtBlockHandler`、`IKkexConsumer`、`IBatchHook`、`IMotionStage`）は変わりません。新しい機能は、任意の新しいインターフェースか
  デリゲートとして加わります。プラグインが継承するクラス（`ViewportTool`）は、仮想メソッドが増えることはありますが、抽象
  メソッドは増えません。
- `[Experimental]` の付いた型とメンバーは約束の外で、どのマイナーのバージョンでも変わることがあります。1.3 では、
  フォーム（`HS2Next.Api.Ui`、`ctx.Forms`）、ウィジェット、元に戻す履歴の項目（`ctx.History`）、実行時の上書き
  （`ctx.Overrides`）、シーンの後段（`IPluginScenePost`、`BoneWritten`）、肌のレイヤー、体のパーツの差し替え、組み立て
  直しの要求、エンジン側の UI の登録、ビューポートのツール（`Tools`、`ViewportTool` とその周りの型）、マップの問い合わせ
  （`Map`、`IMapQuery`、`MapSurfaceHit`）、キャラクターのステージ（`Stages`、`ICharaStages`）、アイテムの行
  （`ItemRows`、`IItemRows`、`IItemRowGroup`、`ItemRow`、`ItemRowRequest`）がそれに当たります。
  呼び出しの補助、撮影のイベントとその種類、コマンド `plugin.disable` / `plugin.enable` は安定です。
- 1.1 で加わったもの: `CommandRequest` と `ICommandReply` を使う `Request` / `RequestAsync`、`IsActive`、撮影の
  イベント、元に戻す履歴の項目、フォームとウィジェット、肌のレイヤー、体のパーツの差し替え、組み立て直しの要求、実行中の
  停止と再開、そしてすべての公開メンバーの XML ドキュメント。
- 1.2 で加わったもの: 実行時の上書き（`ctx.Overrides`、`IRuntimeOverrides`、`IOverrideSlot`、`OverrideValueKind`、
  `OverrideInfo`）とその骨と IK の標的のパス、シーンの後段（`IPluginScenePost`、`BoneWritten`）、連番画像の書き出しの
  フレームを表す撮影の種類 `Record`。
- 1.3 で加わったもの: ビューポートのツール（`Tools`、`IViewportTools`、`ViewportTool`、`ToolPointer`、`ToolPick`）、
  マップの問い合わせ（`Map`、`IMapQuery`、`MapSurfaceHit`）、キャラクターのステージ（`Stages`、`ICharaStages`）、
  アイテムの行（`ItemRows`、`IItemRows`、`IItemRowGroup`、`ItemRow`、`ItemRowRequest`）。

## プラグインの確かめ方

- `plugin.list` に、プラグインごとの状態、アセンブリ、コマンド、拡張の名前空間、ハンドラー、フォーム（`ui.forms`）、
  肌のレイヤーと差し替えたパーツ（数）、元に戻す履歴の項目（`history`、数）、実行時の上書きのスロット（`overrides`）、
  障害、最後に失敗した再開（`enableError`）、更新にかかった時間が出ます。`IPluginScenePost` を持つプラグインには後段の
  呼び出しにかかった時間（`scenePostMs`）も出ます。`overrides.list` には、シーンのすべてのスロットが持ち主、優先度、値と
  ともに出ます。
- `plugin.disable {"guid": "<guid>"}` はアプリの実行中にプラグインを外し、`plugin.enable {"guid": "<guid>"}` はもう一度
  読み込みます（`persist: true` で選択を設定にも記録し、再開に失敗したときも警告付きで記録します。`withDependents: true`
  でそれに依存するプラグインを先に止めます）。返答には、状態、かかった時間、組み立て直しを待つキャラクター（`rebuilt`）、
  停止では手放した実行時の上書きのスロットの数（`overrides`）、再開ではもう一度読んだ拡張ブロック（`extBlocks`）が入ります。
  プラグインを何度か止めては動かし、前後の `plugin.list` を比べてください。登録したものはすべて戻り、コンテキストの外で
  作ったものが積み上がらないはずです。
- 1.0 に対してビルドしたプラグインも動かし直せます。その場合、返答に「そのようなプラグインは 1 回のプロセスで `Load` が
  1 回だと想定しているかもしれない」という警告が付きます。
- エンジンを使わない処理は、同じ `HS2Next.Api` を参照する普通の .NET のテストプロジェクトで、アプリなしで確かめられます。

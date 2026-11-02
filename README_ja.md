# HS2Next 1.1.0

HS2Next は、Honey Select 2 の内容を使うシーンとキャラクターのエディターです。Unity 6 で新しく作りました。HS2 の
Studio の作業と、ゲーム本体のキャラクターメイクの作業を一つのウィンドウで行います。キャラクター、アイテム、マップ、
ライト、カメラ、アニメーション、FK と IK、表情、画面効果に加えて、顔、体、髪、服、アクセサリーの編集のページがあり
ます。お手持ちの Honey Select 2 のゲームデータ、キャラカード、コーデカード、ポーズファイル、zipmod を、置いてある
場所のまま読みます。作ったものは HS2Next 自身のファイル形式で保存します。

English: [README.md](README.md)　简体中文（機械翻訳）: [README_zh-Hans.md](README_zh-Hans.md)

## HS2Next ではないもの

- Honey Select 2 の mod やプラグインではありません。別のプログラムで、BepInEx のプラグインは動きません。
- ゲームの複製ではありません。ゲームのデータを含まず、何かを表示するにはお手持ちのインストールが要ります。
- HS2 のシーンファイル（HS2 Studio の `.png` のシーン）は開けません。ゲームのストーリー、H シーン、ゲームの進行も
  ありません。キャラクター、コーデ、ポーズは HS2 のファイルから取り込み、シーンは HS2Next で作ります。

## 状態

1.0.0 は最初の公開版です。保存形式（版 1）とプラグインの API（1.0、セマンティック バージョニング）は凍結しました。
後の 1.x は 1.0 のファイルを読み、1.0 のプラグインを読み込みます。1.0 でできることとできないことは
[CHANGELOG.md](CHANGELOG.md)（英語）にあります。

## 動作環境

- Windows 10 または 11（64 ビット）
- Direct3D 12 に対応したグラフィックカード。ビデオメモリは 8 GB を推奨します。大きなマップは、アプリ本体の分に加えて
  約 2.8 GB を使います。
- ご自身の Honey Select 2 のインストール（mod を使う場合は `mods` フォルダーも）。HS2Next はゲームの骨格、体、頭の
  データからキャラクターを組み立てるので、インストールが無いとキャラクターを作れません。
- キャッシュ用の空き容量。最初に約 0.3 GB を使い、修復した mod を開くにつれて約 10 GB まで増えます。

## 配布物の使い方

配布の zip（`HS2Next-1.1.0-win64.zip`。隣に SHA-256 のファイルがあります）がプログラムです。好きな場所に展開して
`HS2Next.exe` を起動します。インストーラーはありません。コード署名をしていないので、Windows SmartScreen が一度だけ
確認を求めることがあります（「詳細情報」を押してから「実行」）。初回の案内が、表示言語、Honey Select 2 のフォルダー、
追加の mod のフォルダー、キャッシュのフォルダーを順に尋ね、内容の索引を作ります（mod の数とディスクによって数秒から
数分）。

設定、作ったシーンとキャラクター、キャッシュは `%USERPROFILE%\AppData\LocalLow\stp3\HS2Next` に置きます。
`HS2Next.exe` の隣に `portable.txt` という名前のファイルを置くと、代わりにプログラムの隣に置きます。カメラのキー、
自動操作のブリッジ、描画の問題、アンインストールは zip の中の `README_ja.txt` に書いてあります。

## ソースからのビルド

必要なもの:

- Unity 6000.6.5f1（Unity Hub から。Windows 版のエディターに Windows の Mono の player が含まれます）
- `Tools/` の道具を動かす Python 3（標準ライブラリだけを使います）と、スクリプトを動かす PowerShell 7（`pwsh`）
- 上と同じ Honey Select 2 のインストール
- 見本のプラグインをビルドするときだけ、`netstandard2.1` をビルドできる .NET SDK

手順:

1. リポジトリを clone します（約 44 MB。そのうち 25 MB は Noto Sans CJK のフォント 4 本です）。
2. `Config/settings.example.json` を `Config/settings.local.json` に写し、`hs2Root` に `abdata` と `mods` のある
   フォルダーを書きます。ほかの鍵には既定値があり、例のファイルに 1 行ずつ説明があります。
3. Unity 6000.6.5f1 でプロジェクトを開きます。最初の読み込みが `Library/`（約 1.7 GB）を作り、すべてのアセンブリを
   コンパイルします。シェルからは `& Tools\unity_batch.ps1 -Compile` で、エディターのウィンドウを出さずに同じことが
   できます。
4. 再生ボタンを押すと、エディターの中でアプリが起動します。データの置き場（設定、`UserData/`、`Cache/`）は
   プロジェクトのフォルダーです。
5. player をビルドします。`& Tools\unity_batch.ps1 -Method PlayerBuild.BuildApp -LogName appbuild -TimeoutMin 90` が
   `Builds/App/HS2Next.exe` を書きます。`& Tools\package_dist.ps1 -Zip` は、試験したそのビルドから `Builds/Dist/` の下に
   配布のフォルダーと zip を作ります（ファイルを検査し、README、`LICENSE.txt`、`THIRD_PARTY.txt` を加えます）。

スクリプトから Unity を起動するときは `Tools/unity_batch.ps1` を使います。プロジェクトを開く Unity を同時に 1 本に
限り、空きメモリが 8 GB になるまで待ち、Unity の終了コードを返します。`python -I Tools/offline_compile.py` は、Unity を
起動せずに C# がコンパイルできるかを確かめます（先にプロジェクトを一度 Unity でコンパイルしておく必要があります）。

## 試験

`Tools/run_tests.ps1 -Quick` が普段の試験の一式（約 65 行。大半は headless の Unity の処理で、一部は素の Mono の試験）を
回します。`-Full` で長い行が加わり、`-Only <id>` で行を選び、`-List` で行を一覧します。大半の行はゲームのインストールを
使います。開発の機械にしか無い記録と比べる行もあり、その記録が無いと FAIL ではなく WARN を報告します。行ごとの中身、
必要なもの、報告の置き場は [Tools/README_tests.md](Tools/README_tests.md)（英語）にあります。

## 文書

文書は README を除いて英語です。

| 文書 | 内容 |
| --- | --- |
| [ARCHITECTURE.md](ARCHITECTURE.md) | HS2Next の作り。内容の供給源と ID、保存形式、描画、運動、UI、ブリッジ、プラグイン、起動、データの置き場、試験 |
| [Reference/COMMANDS.md](Reference/COMMANDS.md) | 自動操作のブリッジ（127.0.0.1:17322 の HTTP/JSON）の全命令と、スクリプトからの操作の仕方 |
| [Reference/PLUGINS.md](Reference/PLUGINS.md) | 1.0 の API でプラグインを書く方法。見本は [Samples/Sequencer](Samples/Sequencer/README.md) |
| [CONTRIBUTING.md](CONTRIBUTING.md) | ビルド、試験、貢献の決まり |
| [CLEANROOM.md](CLEANROOM.md) | ゲームのコードを写さずにコードを書いた方法 |
| [THIRD_PARTY.md](THIRD_PARTY.md) | 第三者のソフトウェア、フォント、実行時の部品と、相互運用のために残した名前 |
| [CHANGELOG.md](CHANGELOG.md) | 版の記録 |

`Assets/HS2Next/Runtime/` と `Assets/HS2Next/Shaders/` の下のフォルダーには、それぞれの README があります。

## 条件

- HS2Next は非公式のプロジェクトです。ILLUSION とその後継の会社とは関係がなく、承認や支援も受けていません。
  「Honey Select 2」の名前は、HS2Next が誰のデータを読むかを示すためだけに使っています。
- HS2Next はゲームのデータを含みません。ゲームと mod のモデル、テクスチャ、アニメーション、一覧、カードは、この
  リポジトリにも配布物にも入っていません。
- HS2Next は、お手持ちの Honey Select 2 と mod を、インストールした場所のまま読みます。ゲームのフォルダーには何も
  書き込みません。
- HS2Next は、すでにお持ちのキャラクター、衣装、コミュニティの mod を使い続けられるように作りました。
- そのために、HS2Next は mod やアセットのシェーダーに合わせた自前の互換シェーダー（名前は `HS2Next/Compatible/` で
  始まります）を持ち、有志のプラグインがキャラカード、コーデカード、ポーズファイルに保存したデータと、Sideloader の
  zipmod を自前のコードで読みます。どちらも元の作者が作ったものではなく、作者の承認や支援も受けていません。一覧は
  「ファイル > オプション」の「詳細」の末尾にあります。
- Honey Select 2 は成人向けのゲームです。HS2Next は、ゲーム自体を使ってよい場所でだけ使ってください。
- HS2Next は stp3 が AI のコーディングツールを使って作りました。コードの大半はツールが書き、stp3 が設計を決め、
  作業を指示して点検しました。stp3 はこのコードを CC0 1.0（[LICENSE](LICENSE)）でパブリックドメインに供しています。
  自由に使い、変え、配ってください。これは Honey Select 2、その資産、他の人の mod には及びません。HS2Next で作った
  もの（シーン、キャラクター、撮影した画像）に含まれるそれらの部分は、それぞれの権利者の条件に従います。
- 配布の zip には、Unity、Microsoft、Mono プロジェクトの実行時の部品（エンジン、DirectX 12、DirectStorage、.NET の
  クラスライブラリ）も入っています。これらはそれぞれの条件に従い、その条件は [THIRD_PARTY.md](THIRD_PARTY.md) に
  載せています。HS2Next を使うことで、これらの部品についてその条件に同意したことになります。CC0 が及ぶのは HS2Next
  自身のコードだけです。
- HS2Next は現状のまま提供し、いかなる保証もしません。

## ライセンス

HS2Next 自身のコードは CC0 1.0（[LICENSE](LICENSE)）です。同梱の Noto Sans CJK のフォントは SIL Open Font License 1.1、
Newtonsoft.Json は MIT です。Unity の実行時の部品と、配布の zip に入るほかの部品は [THIRD_PARTY.md](THIRD_PARTY.md) に
あります。

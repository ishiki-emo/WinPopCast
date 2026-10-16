# WinPopCast

指定したアプリのウィンドウ**だけ**を、画面上の位置と重なり順のとおりに合成し、Spout2 で OBS に送る Windows 用のキャプチャツールです。macOS 版 OBS の「アプリケーションキャプチャ」に近いことを Windows で実現します。

OBS の「ウィンドウキャプチャ」は 1 ウィンドウ単位なので、Unity エディタのカラーピッカー、ドロップダウン、右クリックメニュー、Preferences などの小窓が映りません。「画面キャプチャ」にすると、今度は Discord や通知まで映ってしまいます。WinPopCast は対象アプリのウィンドウだけを集めて 1 枚の映像にするので、小窓は映り、他のアプリは映りません。

- 対象は任意のアプリです（Unity、Blender、DAW など）。実行ファイル名で覚えるので、アプリを再起動しても自動で再接続します
- ポップアップやメニューは、出現からおよそ 1 フレーム（60fps で約 18ms）で映り始めます
- 他のアプリが手前に重なっていても、対象アプリの内容がそのまま映ります
- 背景は透明です。DPI が混在するマルチモニター環境にも対応しています

> [!NOTE]
> 開発中のソフトウェアです。配信で使う前に、手元で映り方を確認してください。

## 必要なもの

- Windows 11（Windows 10 2004 以降でも動きますが、キャプチャ中のウィンドウに黄色い枠が付きます）
- [OBS Studio](https://obsproject.com/) と [obs-spout2-plugin](https://github.com/Off-World-Live/obs-spout2-plugin)
- DirectX 11 に対応した GPU

## 使い方

1. zip を展開し、`WinPopCast.exe` を起動します。タスクトレイにアイコンが出ます
2. 設定画面で「対象プロセス」を選び（例: `Unity`）、「適用」を押します
3. OBS で「ソース」→「Spout2 Capture」を追加し、Spout Senders で `WinPopCast` を選びます

設定画面を閉じてもトレイに常駐し続けます。トレイアイコンの左クリックで設定画面、右クリックで「送信を停止／再開」と「終了」のメニューが開きます。アイコンが青いときは送信中、灰色のときは対象アプリを待っているか停止中です。

### 設定項目

| 項目 | 内容 |
|---|---|
| 対象プロセス | 映すアプリの実行ファイル名（拡張子なし）。同じ名前のプロセスはすべて対象になります |
| キャンバス | 出力する範囲。既定の「メインウィンドウの範囲」は、対象アプリのメインウィンドウだけを切り出します（窓を動かすと付いていき、余白は出ません）。メインウィンドウの外にはみ出したメニューや、離れた場所に置いたフローティング窓は切れます。また、窓のサイズを変えると出力の解像度も変わります。それらも映したいときは、モニター（「メインウィンドウがあるモニター」や特定のモニター）か「全モニター」を選んでください。窓がない部分は透明になります |
| FPS | 30 または 60 |
| Spout 送信者名 | OBS 側で選ぶ名前（既定: `WinPopCast`）。複数起動はできません |
| 許可リスト | 対象プロセス以外でも映すウィンドウ（IME の候補窓など）。**プロセス名は必須**で、クラス名・タイトルでさらに絞り込めます。プロセス名がないと、タイトルが一致しただけの無関係なアプリ（ブラウザのタブなど）まで映ってしまうためです |
| 除外リスト | 対象プロセスのウィンドウでも映さないもの。許可リストより優先されます |
| 映り込み防止 | 「ファイルを開く／保存」ダイアログを映しません（既定でオン）。フォルダー名やユーザー名が配信に映るのを防ぎます。対象アプリの標準ダイアログは、ファイルダイアログかどうか判定できるまでの約 0.3 秒間は映しません |
| プレビュー | 設定画面に、送信中の映像を縮小して表示します |

設定は `%APPDATA%\WinPopCast\settings.json` に保存されます。

> [!WARNING]
> 対象アプリ自身の窓は、ログイン画面やアカウント情報のダイアログも含めてすべて映ります。配信中に個人情報を表示する操作は避けてください。

設定画面の状態欄には、送信の状況に加えて、直近のエラーや GPU のエラー（ドライバーの更新など）から自動で復帰したことが表示されます。

### うまく映らないとき

- **OBS で半透明部分の色が濁る／縁が黒い**: Spout2 Capture の Composite mode を「Premultiplied alpha」にしてください（WinPopCast は乗算済みアルファで送っています）
- **管理者として実行しているアプリが映らない**: WinPopCast も管理者として実行してください
- **ゲームの排他フルスクリーンや、キャプチャ禁止に設定されたウィンドウ**: Windows の仕様で映りません
- **クラス名がわからない**: リポジトリの `WinPopCast.Probe`（下記）で、対象アプリのウィンドウ一覧をクラス名つきで確認できます

## 方針

迷ったときは「余計に映る」より「映らない」側に倒しています。たとえば、最初のフレームが届く前のウィンドウは描きません。また、対象アプリが起動していないときは透明な映像を送ります。

## ソースからのビルド

必要なもの: .NET 10 SDK、Visual Studio 2022 以降（「C++ によるデスクトップ開発」）、CMake 3.24 以降

```bash
cmake -S native/SpoutBridge -B native/SpoutBridge/build -G "Visual Studio 17 2022" -A x64
cmake --build native/SpoutBridge/build --config Release
dotnet build WinPopCast.slnx
```

配布用の zip（単一 exe）は次のコマンドで `artifacts/` に作られます。`-FrameworkDependent` を付けると、.NET 10 デスクトップランタイムを別途必要とする小さい版になります。

```bash
pwsh scripts/package.ps1
```

### 構成

```
[WindowTracker]  対象プロセスのトップレベルウィンドウと Z 順・矩形を追跡（EnumWindows + WinEventHook）
      ↓
[CaptureManager] ウィンドウごとに Windows.Graphics.Capture のセッションを生成・破棄
      ↓
[Compositor]     キャンバスに Z 順の下から描く（D3D11、乗算済みアルファ）
      ↓
[IFrameSink]     Spout2 送信
```

| パス | 内容 |
|---|---|
| `src/WinPopCast.Core` | 追跡・キャプチャ・合成（CaptureEngine）。出力や UI には依存しない。将来の OBS プラグイン版で移植する部分 |
| `src/WinPopCast.Spout` | Spout2 送信（`native/SpoutBridge` を P/Invoke で呼ぶ） |
| `src/WinPopCast.App` | トレイ常駐アプリ（WinForms） |
| `src/WinPopCast.Cli` | コマンドライン版。`--measure`（開始遅延の計測）や `--dump`（合成結果の PNG 保存）などの開発用オプション付き |
| `src/WinPopCast.Probe` | ウィンドウの出現・Z 順・クラス名をリアルタイムにログ出力する観察ツール |
| `native/SpoutBridge` | SpoutDX（Spout2 SDK 2.007h）を C ABI で公開する薄い DLL |
| `docs/` | Unity での観察結果と、開始遅延の計測結果 |

## ライセンス

WinPopCast 自身のソースコードは [MIT License](LICENSE) です。

配布している実行ファイルには、Spout2 SDK（BSD 2-Clause）、Vortice.Windows（MIT）、Microsoft の Windows SDK の投影アセンブリ（Windows SDK ライセンス）、.NET ランタイム（MIT）などの第三者のソフトウェアが含まれ、それぞれのライセンスが適用されます。一覧は [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) に、各ライセンスの原文は配布 zip の `licenses/` フォルダーにあります。アプリのトレイメニューの「バージョン情報とライセンス」からも確認できます。

## 商標

本文中の会社名・製品名（Windows、Unity、OBS Studio、CLIP STUDIO PAINT、Cubase、Spout など）は、各社・各権利者の商標または登録商標です。WinPopCast はこれらの企業・団体とは関係がなく、承認や支援を受けたものではありません。

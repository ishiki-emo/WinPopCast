# AppCapture（仮称）

指定したアプリのウィンドウ**だけ**を、画面上の位置と重なり順のとおりに合成し、Spout2 で OBS に送る Windows 用のキャプチャツールです。macOS 版 OBS の「アプリケーションキャプチャ」に近いことを Windows で実現します。

OBS の「ウィンドウキャプチャ」は 1 ウィンドウ単位なので、Unity エディタのカラーピッカー、ドロップダウン、右クリックメニュー、Preferences などの小窓が映りません。「画面キャプチャ」にすると、今度は Discord や通知まで映ってしまいます。AppCapture は対象アプリのウィンドウだけを集めて 1 枚の映像にするので、小窓は映り、他のアプリは映りません。

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

1. zip を展開し、`AppCapture.exe` を起動します。タスクトレイにアイコンが出ます
2. 設定画面で「対象プロセス」を選び（例: `Unity`）、「適用」を押します
3. OBS で「ソース」→「Spout2 Capture」を追加し、Spout Senders で `AppCapture` を選びます

設定画面を閉じてもトレイに常駐し続けます。トレイアイコンの左クリックで設定画面、右クリックで「送信を停止／再開」と「終了」のメニューが開きます。アイコンが青いときは送信中、灰色のときは対象アプリを待っているか停止中です。

### 設定項目

| 項目 | 内容 |
|---|---|
| 対象プロセス | 映すアプリの実行ファイル名（拡張子なし）。同じ名前のプロセスはすべて対象になります |
| キャンバス | 出力する範囲。「自動」はメインウィンドウがあるモニターで、ウィンドウを別のモニターへ動かすと出力の解像度も変わります。モニターを固定したいときは、モニター名を選んでください |
| FPS | 30 または 60 |
| Spout 送信者名 | OBS 側で選ぶ名前（既定: `AppCapture`）。複数起動はできません |
| 許可リスト | 対象プロセス以外でも映すウィンドウ（IME の候補窓など）。クラス名・タイトル・プロセス名で指定します |
| 除外リスト | 対象プロセスのウィンドウでも映さないもの。許可リストより優先されます |
| プレビュー | 設定画面に、送信中の映像を縮小して表示します |

設定は `%APPDATA%\AppCapture\settings.json` に保存されます。

### うまく映らないとき

- **OBS で半透明部分の色が濁る／縁が黒い**: Spout2 Capture の Composite mode を「Premultiplied alpha」にしてください（AppCapture は乗算済みアルファで送っています）
- **管理者として実行しているアプリが映らない**: AppCapture も管理者として実行してください
- **ゲームの排他フルスクリーンや、キャプチャ禁止に設定されたウィンドウ**: Windows の仕様で映りません
- **クラス名がわからない**: リポジトリの `AppCapture.Probe`（下記）で、対象アプリのウィンドウ一覧をクラス名つきで確認できます

## 方針

迷ったときは「余計に映る」より「映らない」側に倒しています。たとえば、最初のフレームが届く前のウィンドウは描きません。また、対象アプリが起動していないときは透明な映像を送ります。

## ソースからのビルド

必要なもの: .NET 10 SDK、Visual Studio 2022 以降（「C++ によるデスクトップ開発」）、CMake 3.24 以降

```bash
cmake -S native/SpoutBridge -B native/SpoutBridge/build -G "Visual Studio 17 2022" -A x64
cmake --build native/SpoutBridge/build --config Release
dotnet build AppCapture.slnx
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
| `src/AppCapture.Core` | 追跡・キャプチャ・合成（CaptureEngine）。出力や UI には依存しない。将来の OBS プラグイン版で移植する部分 |
| `src/AppCapture.Spout` | Spout2 送信（`native/SpoutBridge` を P/Invoke で呼ぶ） |
| `src/AppCapture.App` | トレイ常駐アプリ（WinForms） |
| `src/AppCapture.Cli` | コマンドライン版。`--measure`（開始遅延の計測）や `--dump`（合成結果の PNG 保存）などの開発用オプション付き |
| `src/AppCapture.Probe` | ウィンドウの出現・Z 順・クラス名をリアルタイムにログ出力する観察ツール |
| `native/SpoutBridge` | SpoutDX（Spout2 SDK 2.007h）を C ABI で公開する薄い DLL |
| `docs/` | Unity での観察結果と、開始遅延の計測結果 |

## ライセンス

未定です（公開時に決めます）。同梱しているサードパーティーのソフトウェアについては [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) を参照してください。

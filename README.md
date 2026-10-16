# AppCapture（仮称）

指定したプロセスのウィンドウだけを、画面上の位置と重なり順のとおりに合成し、Spout2 で OBS に送るツールです（開発中）。

## 構成

| パス | 内容 |
|---|---|
| `src/AppCapture.Core` | ウィンドウ追跡（WindowTracker）、WGC キャプチャ、D3D11。出力や UI には依存しない |
| `src/AppCapture.Spout` | Spout2 送信（`native/SpoutBridge` の DLL を P/Invoke で呼ぶ） |
| `src/AppCapture.Cli` | 送信アプリ（現在は M2：メインウィンドウ 1 枚を送信） |
| `src/AppCapture.Probe` | M1 の観察ツール（ウィンドウの出現・Z 順をログに出す） |
| `native/SpoutBridge` | SpoutDX（Spout2 SDK 2.007h、BSD-2）を C ABI で公開する薄い DLL |

## ビルド

必要なもの: .NET 10 SDK、Visual Studio 2022 以降（C++ デスクトップ開発）、CMake 3.24 以降

```bash
cmake -S native/SpoutBridge -B native/SpoutBridge/build -G "Visual Studio 17 2022" -A x64
cmake --build native/SpoutBridge/build --config Release
dotnet build AppCapture.slnx
```

## 使い方

```bash
dotnet run --project src/AppCapture.Cli -- --process Unity --sender AppCapture --fps 60
```

OBS では obs-spout2-plugin の「Spout2 Capture」ソースを追加し、送信者に `AppCapture` を選択します。

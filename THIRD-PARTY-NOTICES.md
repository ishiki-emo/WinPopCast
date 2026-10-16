# Third-party notices

WinPopCast 自身のソースコードは [MIT License](LICENSE)（Copyright (c) 2026 ishiki-emo）です。
配布している実行ファイル（`WinPopCast.exe`）には、次の第三者のソフトウェアが含まれています。
**これらには、それぞれのライセンスが適用されます**（WinPopCast の MIT ライセンスの対象ではありません）。
配布用 zip の `licenses/` フォルダーに、各ライセンスの原文を同梱しています。

| ソフトウェア | バージョン | ライセンス | 用途 | 原文 |
|---|---|---|---|---|
| [Spout2 SDK](https://github.com/leadedge/Spout2)（SpoutDX と SpoutGL の一部） | 2.007h | BSD 2-Clause | Spout2 送信。`WinPopCastSpoutBridge.dll` に静的リンク | `licenses/Spout2-LICENSE.txt` |
| [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows)（Direct3D11 / DXGI / DirectX / D3DCompiler） | 3.8.3 | MIT | Direct3D 11 | `licenses/Vortice-LICENSE.txt` |
| [Vortice.Mathematics](https://github.com/amerkoleci/Vortice.Mathematics) | 2.1.0 | MIT | 数学型 | `licenses/Vortice-LICENSE.txt` |
| [SharpGen.Runtime](https://github.com/SharpGenTools/SharpGenTools) / .COM | 2.4.2-beta | MIT | COM 相互運用（Vortice の依存） | `licenses/SharpGen.Runtime-LICENSE.txt` |
| [Velopack](https://github.com/velopack/velopack)（インストーラー版の更新機能と、インストーラー・Update.exe） | 1.2.161 | MIT | インストールと更新 | `licenses/Velopack-LICENSE.txt` |
| Microsoft.Windows.SDK.NET / WinRT.Runtime（[C#/WinRT](https://github.com/microsoft/CsWinRT)） | 10.0.22621.57 | Windows SDK ライセンス（Distributable Code） | Windows.Graphics.Capture などの Windows Runtime API | `licenses/Microsoft.Windows.SDK.NET-NOTICE.txt` |
| [.NET ランタイム](https://github.com/dotnet/runtime)（自己完結版のみ） | 配布 zip の表記を参照 | MIT（＋.NET が含む第三者コンポーネント） | ランタイム | `licenses/dotnet-LICENSE.txt`、`licenses/dotnet-THIRD-PARTY-NOTICES.txt` |
| [Windows Forms / Windows Desktop ランタイム](https://github.com/dotnet/winforms)（自己完結版のみ） | 同上 | MIT | 設定画面・トレイ | `licenses/windowsdesktop-LICENSE.txt` |

自己完結版には、.NET ランタイムと Windows Desktop ランタイムが丸ごと含まれます。その中にある Microsoft のネイティブ DLL（`coreclr.dll`、`msquic.dll`、WPF 用の `D3DCompiler_47_cor3.dll`、`vcruntime140_cor3.dll` など）も、各ランタイムの一部として、そのライセンスに従って再配布しています。
シェーダーのコンパイル（Vortice.D3DCompiler）には、Windows に標準で入っている `d3dcompiler_47.dll` を使います。

---

## Spout2 SDK

各ソースファイルの著作権表示は「Copyright (c) 2014-2022, Lynn Jarvis. All rights reserved.」です。
各ファイルのコメントには、Malcolm Bechard 氏などの貢献が記されています。

```
BSD 2-Clause License

Copyright (c) 2020, Lynn Jarvis
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## Microsoft.Windows.SDK.NET / WinRT.Runtime

`Microsoft.Windows.SDK.NET.dll` と `WinRT.Runtime.dll` は Microsoft Corporation の著作物です（© Microsoft Corporation. All rights reserved.）。
NuGet パッケージ Microsoft.Windows.SDK.NET.Ref に含まれるファイルを、[Windows SDK のライセンス条項](https://aka.ms/WinSDKLicenseURL)の Distributable Code の規定に基づいて再配布しています。
これらの利用には同条項が適用されます。第三者の表記は https://aka.ms/thirdpartynotices を参照してください。

## MIT License（Vortice.Windows / Vortice.Mathematics / SharpGen.Runtime / Velopack / .NET / Windows Forms）

- Vortice.Windows, Vortice.Mathematics: Copyright (c) Amer Koleci and Contributors
- SharpGen.Runtime: (c) 2010-2017 Alexandre Mutel, 2017-2023 Jeremy Koritzinsky, 2023-2024 Amer Koleci
- Velopack: 下記の licenses/Velopack-LICENSE.txt を参照
- .NET, Windows Forms: Copyright (c) .NET Foundation and Contributors

```
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

.NET ランタイムが含む第三者コンポーネントの表記は、配布 zip の `licenses/dotnet-THIRD-PARTY-NOTICES.txt`（実際に同梱しているランタイムのバージョンのもの）を参照してください。

## ビルドにだけ使うもの（配布物には含まれません）

CMake、MSBuild / .NET SDK、GitHub Actions（actions/checkout、actions/setup-dotnet、actions/upload-artifact、softprops/action-gh-release）は、ビルドとリリース作業にだけ使い、配布物には含まれません。

## 商標

本文中の会社名・製品名（Windows、Unity、OBS Studio、CLIP STUDIO PAINT、Cubase、Spout など）は、各社・各権利者の商標または登録商標です。
WinPopCast はこれらの企業・団体とは関係がなく、承認や支援を受けたものではありません。

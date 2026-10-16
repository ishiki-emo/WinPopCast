# Third-party notices

WinPopCast には次のソフトウェアが含まれています。

| ソフトウェア | ライセンス | 用途 |
|---|---|---|
| [Spout2 SDK](https://github.com/leadedge/Spout2)（SpoutDX、2.007h） | BSD 2-Clause | Spout2 送信（native/SpoutBridge に静的リンク） |
| [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows)（Direct3D11 / DXGI / D3DCompiler） | MIT | Direct3D 11 |
| [Vortice.Mathematics](https://github.com/amerkoleci/Vortice.Mathematics) | MIT | 数学型 |
| [SharpGen.Runtime](https://github.com/SharpGenTools/SharpGenTools) | MIT | COM 相互運用（Vortice の依存） |
| [.NET](https://github.com/dotnet/runtime)（自己完結版のみ同梱） | MIT | ランタイム |

---

## Spout2 SDK

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

## MIT License（Vortice.Windows / Vortice.Mathematics / SharpGen.Runtime / .NET）

- Vortice.Windows, Vortice.Mathematics: Copyright (c) Amer Koleci and Contributors
- SharpGen.Runtime: (c) 2010-2017 Alexandre Mutel, 2017-2023 Jeremy Koritzinsky, 2023-2024 Amer Koleci
- .NET: Copyright (c) .NET Foundation and Contributors

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

.NET ランタイム自体が含むサードパーティーコンポーネントの表記は
https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT を参照してください。

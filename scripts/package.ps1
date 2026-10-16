<#
.SYNOPSIS
  配布用の zip（単一 exe + README + ライセンス表記）を artifacts/ に作る。

.EXAMPLE
  pwsh scripts/package.ps1
  pwsh scripts/package.ps1 -FrameworkDependent   # .NET 10 デスクトップランタイムが必要な小さい版
#>
param(
    [switch]$FrameworkDependent,
    [string]$Generator = "Visual Studio 17 2022"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

[xml]$props = Get-Content Directory.Build.props
$version = $props.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$flavor = if ($FrameworkDependent) { "win-x64-fd" } else { "win-x64" }
$name = "WinPopCast-$version-$flavor"
$publishDir = Join-Path $root "artifacts/publish/$name"
$zipPath = Join-Path $root "artifacts/$name.zip"

Write-Host "== SpoutBridge (C++) ==" -ForegroundColor Cyan
$nativeBuild = "native/SpoutBridge/build"
if (-not (Test-Path "$nativeBuild/CMakeCache.txt")) {
    cmake -S native/SpoutBridge -B $nativeBuild -G $Generator -A x64
    if ($LASTEXITCODE) { throw "cmake configure failed" }
}
cmake --build $nativeBuild --config Release
if ($LASTEXITCODE) { throw "cmake build failed" }

Write-Host "== WinPopCast ($flavor) ==" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
$selfContained = if ($FrameworkDependent) { "false" } else { "true" }
dotnet publish src/WinPopCast.App/WinPopCast.App.csproj -c Release -o $publishDir `
    --self-contained $selfContained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=$selfContained `
    -p:DebugType=none
if ($LASTEXITCODE) { throw "dotnet publish failed" }

Copy-Item README.md, LICENSE, THIRD-PARTY-NOTICES.md $publishDir

Write-Host "== licenses ==" -ForegroundColor Cyan
$licDir = Join-Path $publishDir "licenses"
New-Item -ItemType Directory -Force $licDir | Out-Null
Copy-Item licenses/* $licDir
# Spout2 の原文は、実際にビルドに使ったソースから取る
Copy-Item "$nativeBuild/_deps/spout2-src/LICENSE" (Join-Path $licDir "Spout2-LICENSE-original.txt")

if (-not $FrameworkDependent) {
    # 自己完結版は .NET ランタイムを同梱するので、実際に同梱したバージョンのライセンスと第三者表記を入れる
    $rtVersion = (dotnet msbuild src/WinPopCast.App/WinPopCast.App.csproj -getProperty:BundledNETCoreAppPackageVersion).Trim()
    $nuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE ".nuget/packages" }
    $core = Join-Path $nuget "microsoft.netcore.app.runtime.win-x64/$rtVersion"
    $desktop = Join-Path $nuget "microsoft.windowsdesktop.app.runtime.win-x64/$rtVersion"
    foreach ($f in @(@("$core/LICENSE.TXT", "dotnet-LICENSE.txt"),
                     @("$core/THIRD-PARTY-NOTICES.TXT", "dotnet-THIRD-PARTY-NOTICES.txt"),
                     @("$desktop/LICENSE", "windowsdesktop-LICENSE.txt"))) {
        if (-not (Test-Path $f[0])) { throw "ランタイムのライセンスが見つかりません: $($f[0])" }
        Copy-Item $f[0] (Join-Path $licDir $f[1])
    }
    "$rtVersion" | Set-Content (Join-Path $licDir "dotnet-VERSION.txt")
}

# 単一 exe にまとめきれなかったファイルが出ていないか確認する
$extra = Get-ChildItem $publishDir -File | Where-Object { $_.Name -notin @("WinPopCast.exe", "README.md", "LICENSE", "THIRD-PARTY-NOTICES.md") }
if ($extra) { Write-Warning "単一 exe 以外のファイルがあります: $($extra.Name -join ', ')" }

if (Test-Path $zipPath) { Remove-Item $zipPath }
Compress-Archive -Path "$publishDir/*" -DestinationPath $zipPath
Get-ChildItem $licDir | ForEach-Object { Write-Host "  licenses/$($_.Name)" }
$size = (Get-Item "$publishDir/WinPopCast.exe").Length / 1MB
Write-Host ("done: {0} (WinPopCast.exe {1:F1} MB)" -f $zipPath, $size) -ForegroundColor Green

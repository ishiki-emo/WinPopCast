<#
.SYNOPSIS
  配布物を artifacts/ に作る。
    - ポータブル版 zip（単一 exe + README + ライセンス表記）
    - インストーラー（Velopack。artifacts/velopack/ に Setup.exe と更新用のファイル）

.EXAMPLE
  pwsh scripts/package.ps1                          # バージョンは Directory.Build.props から
  pwsh scripts/package.ps1 -Version 0.2.0 -DownloadPrevious   # CI：前の版から差分更新を作る
  pwsh scripts/package.ps1 -FrameworkDependent      # .NET 10 デスクトップランタイムが必要な小さい zip（インストーラーなし）
#>
param(
    [string]$Version = "",
    [switch]$FrameworkDependent,
    [switch]$NoInstaller,
    [switch]$DownloadPrevious,
    [string]$PackId = "WinPopCast",
    [string]$RepoUrl = "https://github.com/ishiki-emo/WinPopCast",
    [string]$Generator = "Visual Studio 17 2022"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

[xml]$props = Get-Content Directory.Build.props
$version = if ($Version) { $Version.TrimStart("v") } else { $props.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }
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
    -p:DebugType=none `
    -p:Version=$version
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
Write-Host ("portable: {0} (WinPopCast.exe {1:F1} MB)" -f $zipPath, $size) -ForegroundColor Green

if ($FrameworkDependent -or $NoInstaller) { return }

Write-Host "== installer (Velopack) ==" -ForegroundColor Cyan
# 差分更新を効率よくするため、インストーラー版は単一 exe にせず通常のフォルダーで発行する
$vpIn = Join-Path $root "artifacts/publish/velopack-$version"
$vpOut = Join-Path $root "artifacts/velopack"
if (Test-Path $vpIn) { Remove-Item -Recurse -Force $vpIn }
dotnet publish src/WinPopCast.App/WinPopCast.App.csproj -c Release -o $vpIn --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=none -p:Version=$version
if ($LASTEXITCODE) { throw "dotnet publish (installer) failed" }
Copy-Item README.md, LICENSE, THIRD-PARTY-NOTICES.md $vpIn
Copy-Item $licDir $vpIn -Recurse

dotnet tool restore | Out-Null
if ($DownloadPrevious) {
    # 前の版のパッケージがあれば、それとの差分更新を作る（初回リリースなどで無ければそのまま進む）
    dotnet vpk download github --repoUrl $RepoUrl -o $vpOut
    if ($LASTEXITCODE) { Write-Warning "前の版を取得できませんでした。差分更新なしで作ります" }
}
dotnet vpk pack -u $PackId -v $version -r win-x64 -p $vpIn -e WinPopCast.exe -o $vpOut `
    --packTitle WinPopCast --packAuthors ishiki-emo -i assets/WinPopCast.ico
if ($LASTEXITCODE) { throw "vpk pack failed" }
Get-ChildItem $vpOut -File | ForEach-Object { Write-Host ("  {0} ({1:F1} MB)" -f $_.Name, ($_.Length / 1MB)) }
Write-Host "installer: $vpOut" -ForegroundColor Green

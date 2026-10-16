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
$name = "AppCapture-$version-$flavor"
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

Write-Host "== AppCapture ($flavor) ==" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
$selfContained = if ($FrameworkDependent) { "false" } else { "true" }
dotnet publish src/AppCapture.App/AppCapture.App.csproj -c Release -o $publishDir `
    --self-contained $selfContained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=$selfContained `
    -p:DebugType=none
if ($LASTEXITCODE) { throw "dotnet publish failed" }

Copy-Item README.md, LICENSE, THIRD-PARTY-NOTICES.md $publishDir

# 単一 exe にまとめきれなかったファイルが出ていないか確認する
$extra = Get-ChildItem $publishDir -File | Where-Object { $_.Name -notin @("AppCapture.exe", "README.md", "LICENSE", "THIRD-PARTY-NOTICES.md") }
if ($extra) { Write-Warning "単一 exe 以外のファイルがあります: $($extra.Name -join ', ')" }

if (Test-Path $zipPath) { Remove-Item $zipPath }
Compress-Archive -Path "$publishDir/*" -DestinationPath $zipPath
$size = (Get-Item "$publishDir/AppCapture.exe").Length / 1MB
Write-Host ("done: {0} (AppCapture.exe {1:F1} MB)" -f $zipPath, $size) -ForegroundColor Green

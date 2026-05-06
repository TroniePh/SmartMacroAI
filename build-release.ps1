<#
  SmartMacroAI - Local Build and Package Script
  Usage:  .\build-release.ps1           (default: win-x64)
          .\build-release.ps1 win-x64
          .\build-release.ps1 win-x86
          .\build-release.ps1 win-arm64
          .\build-release.ps1 all

  Output: release_output/SmartMacroAI-v{version}-{runtime}.zip

  Created by Pham Duy - Giai phap tu dong hoa thong minh.
#>

param(
    [ValidateSet("win-x64", "win-x86", "win-arm64", "all")]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$csproj = Join-Path $PSScriptRoot "SmartMacroAI.csproj"
[xml]$xml = Get-Content $csproj
$version = $xml.Project.PropertyGroup.Version
if (-not $version) { $version = "1.0.0" }

$line = "=" * 40
Write-Host $line -ForegroundColor Cyan
Write-Host " SmartMacroAI v$version - Build Release" -ForegroundColor Cyan
Write-Host $line -ForegroundColor Cyan
Write-Host ""

if ($Runtime -eq "all") {
    $runtimes = @("win-x64", "win-x86", "win-arm64")
} else {
    $runtimes = @($Runtime)
}

$outRoot = Join-Path $PSScriptRoot "release_output"
if (Test-Path $outRoot) { Remove-Item $outRoot -Recurse -Force }
New-Item -ItemType Directory -Path $outRoot -Force | Out-Null

foreach ($rt in $runtimes) {
    Write-Host ""
    Write-Host ">>> Building $rt ..." -ForegroundColor Yellow

    $publishDir = Join-Path $PSScriptRoot "publish\$rt"
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

    dotnet publish $csproj `
        -c Release `
        -r $rt `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $publishDir

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed for $rt"
        exit 1
    }

    Get-ChildItem $publishDir -Recurse -Filter "*.pdb" | Remove-Item -Force

    $zipName = "SmartMacroAI-v$version-$rt.zip"
    $zipPath = Join-Path $outRoot $zipName
    Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -Force

    $sizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    Write-Host ">>> $zipName  ${sizeMB} MB" -ForegroundColor Green
}

$publishRoot = Join-Path $PSScriptRoot "publish"
Remove-Item $publishRoot -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host $line -ForegroundColor Cyan
Write-Host " Done! Packages in: release_output/" -ForegroundColor Cyan
Get-ChildItem $outRoot -Filter "*.zip" | ForEach-Object {
    Write-Host "   $($_.Name)" -ForegroundColor White
}
Write-Host $line -ForegroundColor Cyan

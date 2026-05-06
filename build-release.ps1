<#
  SmartMacroAI - Local Build and Package Script
  Usage:  .\build-release.ps1           (default: win-x64)
          .\build-release.ps1 win-x64
          .\build-release.ps1 all

  Output: release_output/
    SmartMacroAI-v{ver}-win-x64.zip          (portable)
    SmartMacroAI-v{ver}-win-x64.exe          (standalone)
    SmartMacroAI_Setup_v{ver}.exe            (installer)

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
    Write-Host ">>> Building portable $rt ..." -ForegroundColor Yellow

    $publishDir = Join-Path $PSScriptRoot "publish"
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

    # ZIP
    $zipName = "SmartMacroAI-v$version-$rt.zip"
    $zipPath = Join-Path $outRoot $zipName
    Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -Force
    $sizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    Write-Host ">>> $zipName  ${sizeMB} MB" -ForegroundColor Green

    # Standalone EXE
    Write-Host ">>> Building standalone EXE ..." -ForegroundColor Yellow
    $sfDir = Join-Path $PSScriptRoot "publish_singlefile"
    if (Test-Path $sfDir) { Remove-Item $sfDir -Recurse -Force }

    dotnet publish $csproj `
        -c Release `
        -r $rt `
        --self-contained true `
        -p:PublishSingleFile=true `
        -o $sfDir

    if ($LASTEXITCODE -ne 0) {
        Write-Error "SingleFile build failed for $rt"
        exit 1
    }

    Get-ChildItem $sfDir -Recurse -Filter "*.pdb" | Remove-Item -Force

    $exeName = "SmartMacroAI-v$version-$rt.exe"
    $exeSrc = Join-Path $sfDir "SmartMacroAI.exe"
    $exeDst = Join-Path $outRoot $exeName
    Copy-Item $exeSrc $exeDst
    $sizeMB = [math]::Round((Get-Item $exeDst).Length / 1MB, 1)
    Write-Host ">>> $exeName  ${sizeMB} MB" -ForegroundColor Green

    # Cleanup singlefile
    Remove-Item $sfDir -Recurse -Force -ErrorAction SilentlyContinue
}

# Installer (Inno Setup)
$issFile = Join-Path $PSScriptRoot "installer\SmartMacroAI_Setup.iss"
$issCompiler = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if (Test-Path $issCompiler) {
    Write-Host ""
    Write-Host ">>> Building installer ..." -ForegroundColor Yellow
    & $issCompiler $issFile "/DMyAppVersion=$version"

    $setupSrc = Join-Path $PSScriptRoot "installer_out\SmartMacroAI_Setup_v$version.exe"
    if (Test-Path $setupSrc) {
        $setupDst = Join-Path $outRoot "SmartMacroAI_Setup_v$version.exe"
        Copy-Item $setupSrc $setupDst
        $sizeMB = [math]::Round((Get-Item $setupDst).Length / 1MB, 1)
        Write-Host ">>> SmartMacroAI_Setup_v$version.exe  ${sizeMB} MB" -ForegroundColor Green
    }
} else {
    Write-Host ""
    Write-Host ">>> Inno Setup not found, skipping installer" -ForegroundColor DarkYellow
}

# Cleanup
Remove-Item (Join-Path $PSScriptRoot "publish") -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host $line -ForegroundColor Cyan
Write-Host " Done! Packages in: release_output/" -ForegroundColor Cyan
Get-ChildItem $outRoot | ForEach-Object {
    $s = [math]::Round($_.Length / 1MB, 1)
    Write-Host "   $($_.Name)  ${s} MB" -ForegroundColor White
}
Write-Host $line -ForegroundColor Cyan

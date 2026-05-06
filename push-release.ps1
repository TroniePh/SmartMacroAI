<#
  push-release.ps1
  Quick release script - bumps version, commits, tags, pushes.
  GitHub Actions will auto-build + upload ZIP + Setup.exe to Releases.

  Usage:
    .\push-release.ps1 -Version 1.5.7 -Message "Fix DialogResult + template actions"

  Created by Pham Duy - Giai phap tu dong hoa thong minh.
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$Version,

    [Parameter(Mandatory=$true)]
    [string]$Message
)

$ErrorActionPreference = "Stop"

Write-Host "Releasing SmartMacroAI v$Version ..." -ForegroundColor Cyan

# ── Update version in csproj ──
$csprojPath = Join-Path $PSScriptRoot "SmartMacroAI.csproj"
$csproj = Get-Content $csprojPath -Raw
$csproj = $csproj -replace '<Version>.*?</Version>', "<Version>$Version</Version>"
$csproj = $csproj -replace '<AssemblyVersion>.*?</AssemblyVersion>', "<AssemblyVersion>$Version.0</AssemblyVersion>"
$csproj = $csproj -replace '<FileVersion>.*?</FileVersion>', "<FileVersion>$Version.0</FileVersion>"
Set-Content $csprojPath $csproj -NoNewline
Write-Host "  Updated csproj" -ForegroundColor Gray

# ── Update version in AssemblyInfo.cs ──
$asmPath = Join-Path $PSScriptRoot "AssemblyInfo.cs"
if (Test-Path $asmPath) {
    $asm = Get-Content $asmPath -Raw
    $asm = $asm -replace 'AssemblyVersion\("[^"]+"\)', "AssemblyVersion(`"$Version.0`")"
    $asm = $asm -replace 'AssemblyFileVersion\("[^"]+"\)', "AssemblyFileVersion(`"$Version.0`")"
    Set-Content $asmPath $asm -NoNewline
    Write-Host "  Updated AssemblyInfo.cs" -ForegroundColor Gray
}

# ── Update version in MainWindow.xaml.cs ──
$mwPath = Join-Path $PSScriptRoot "MainWindow.xaml.cs"
if (Test-Path $mwPath) {
    $mw = Get-Content $mwPath -Raw
    $mw = $mw -replace 'CurrentVersion\s*=\s*"v[^"]*"', "CurrentVersion   = `"v$Version`""
    Set-Content $mwPath $mw -NoNewline
    Write-Host "  Updated MainWindow.xaml.cs" -ForegroundColor Gray
}

# ── Update version in localization files ──
foreach ($loc in @("Localization\Strings.en.xaml", "Localization\Strings.vi.xaml")) {
    $locPath = Join-Path $PSScriptRoot $loc
    if (Test-Path $locPath) {
        $content = Get-Content $locPath -Raw
        $content = $content -replace 'v\d+\.\d+\.\d+', "v$Version"
        Set-Content $locPath $content -NoNewline
        Write-Host "  Updated $loc" -ForegroundColor Gray
    }
}

# ── Update version in installer .iss ──
$issPath = Join-Path $PSScriptRoot "installer\SmartMacroAI_Setup.iss"
if (Test-Path $issPath) {
    $iss = Get-Content $issPath -Raw
    $iss = $iss -replace '#define MyAppVersion\s+"[^"]*"', "#define MyAppVersion `"$Version`""
    Set-Content $issPath $iss -NoNewline
    Write-Host "  Updated installer .iss" -ForegroundColor Gray
}

# ── Update README badge ──
$readmePath = Join-Path $PSScriptRoot "README.md"
if (Test-Path $readmePath) {
    $rm = Get-Content $readmePath -Raw
    $rm = $rm -replace 'version-\d+\.\d+\.\d+', "version-$Version"
    Set-Content $readmePath $rm -NoNewline
    Write-Host "  Updated README.md" -ForegroundColor Gray
}

# ── Git commit + tag + push ──
Write-Host ""
Write-Host "Git commit + tag + push ..." -ForegroundColor Yellow

git add -A
git commit -m "Release v$Version - $Message"
git tag "v$Version"
git push origin main
git push origin "v$Version"

Write-Host ""
Write-Host "Done! GitHub Actions is building ..." -ForegroundColor Green
Write-Host "Check: https://github.com/TroniePh/SmartMacroAI/actions" -ForegroundColor Yellow

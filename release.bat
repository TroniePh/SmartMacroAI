@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion
REM Created by Phạm Duy – Giải pháp tự động hóa thông minh.
REM ═══════════════════════════════════════════════════════════════
REM  SmartMacroAI — Full Release Automation
REM  Nhập version → tự động build, đóng gói, push, tạo GitHub Release
REM ═══════════════════════════════════════════════════════════════

set REPO=TroniePh/SmartMacroAI
set APP_NAME=SmartMacroAI
set ISCC="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

echo.
echo ╔══════════════════════════════════════════════╗
echo ║   SmartMacroAI — Release Automation Tool    ║
echo ╚══════════════════════════════════════════════╝
echo.

REM ── Nhập version ──
if "%~1"=="" (
    set /p VERSION="Nhap version (vd: 1.5.8): "
) else (
    set VERSION=%~1
)

if "!VERSION!"=="" (
    echo [ERROR] Chua nhap version!
    pause
    exit /b 1
)

echo.
echo [INFO] Version: !VERSION!
echo [INFO] Repo:    %REPO%
echo.

REM ── Tạo thư mục ──
set PUBLISH_DIR=publish\%APP_NAME%
set RELEASE_DIR=release
if not exist "%RELEASE_DIR%" mkdir "%RELEASE_DIR%"

REM ═══════════════════════════════════════════════════════════════
echo [1/8] Updating version in .csproj...
REM ═══════════════════════════════════════════════════════════════

powershell -NoProfile -Command ^
  "$f='SmartMacroAI.csproj'; $c=Get-Content $f -Raw; ^
   $c=$c -replace '<Version>[^<]+</Version>','<Version>%VERSION%</Version>'; ^
   $c=$c -replace '<AssemblyVersion>[^<]+</AssemblyVersion>','<AssemblyVersion>%VERSION%.0</AssemblyVersion>'; ^
   $c=$c -replace '<FileVersion>[^<]+</FileVersion>','<FileVersion>%VERSION%.0</FileVersion>'; ^
   Set-Content $f $c -NoNewline"

if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Failed to update .csproj
    pause
    exit /b 1
)
echo        Done.

REM ═══════════════════════════════════════════════════════════════
echo [2/8] Building Release (win-x64, SingleFile, self-contained)...
REM ═══════════════════════════════════════════════════════════════

dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:Version=%VERSION% ^
  -o "%PUBLISH_DIR%" 2>&1

if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Build failed!
    pause
    exit /b 1
)
echo        Done.

REM ═══════════════════════════════════════════════════════════════
echo [3/8] Building Release (win-x86, SingleFile, self-contained)...
REM ═══════════════════════════════════════════════════════════════

set PUBLISH_X86=publish\%APP_NAME%-x86
dotnet publish -c Release -r win-x86 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:Version=%VERSION% ^
  -o "%PUBLISH_X86%" 2>&1

if %ERRORLEVEL% NEQ 0 (
    echo [WARN] x86 build failed — skipping x86 assets.
    set HAS_X86=0
) else (
    set HAS_X86=1
    echo        Done.
)

REM ═══════════════════════════════════════════════════════════════
echo [4/8] Creating portable ZIP (x64)...
REM ═══════════════════════════════════════════════════════════════

set ZIP_X64=%RELEASE_DIR%\%APP_NAME%-v%VERSION%-portable-win-x64.zip
if exist "%ZIP_X64%" del "%ZIP_X64%"
powershell -NoProfile -Command "Compress-Archive -Path '%PUBLISH_DIR%\*' -DestinationPath '%ZIP_X64%' -Force"
echo        Created: %ZIP_X64%

if "!HAS_X86!"=="1" (
    echo [4b/8] Creating portable ZIP (x86^)...
    set ZIP_X86=%RELEASE_DIR%\%APP_NAME%-v%VERSION%-portable-win-x86.zip
    if exist "!ZIP_X86!" del "!ZIP_X86!"
    powershell -NoProfile -Command "Compress-Archive -Path '%PUBLISH_X86%\*' -DestinationPath '!ZIP_X86!' -Force"
    echo        Created: !ZIP_X86!
)

REM ═══════════════════════════════════════════════════════════════
echo [5/8] Compiling Inno Setup installer (x64)...
REM ═══════════════════════════════════════════════════════════════

set SETUP_X64=%RELEASE_DIR%\%APP_NAME%-v%VERSION%-win-x64-Setup.exe
if exist %ISCC% (
    %ISCC% "installer\SmartMacroAI_Setup.iss" /DMyAppVersion=%VERSION% /Q
    if %ERRORLEVEL% EQU 0 (
        echo        Created: %SETUP_X64%
    ) else (
        echo [WARN] Inno Setup compile failed!
    )
) else (
    echo [SKIP] Inno Setup not found at %ISCC%
)

REM ═══════════════════════════════════════════════════════════════
echo [6/8] Git commit + tag...
REM ═══════════════════════════════════════════════════════════════

git add -A
git commit -m "release: v%VERSION%" --allow-empty
git tag -f "v%VERSION%"
git push origin main --force-with-lease
git push origin "v%VERSION%" --force

echo        Tagged: v%VERSION%

REM ═══════════════════════════════════════════════════════════════
echo [7/8] Creating GitHub Release...
REM ═══════════════════════════════════════════════════════════════

REM Generate release notes
call generate-release-notes.bat %VERSION%

REM Delete existing release if any
gh release delete "v%VERSION%" --yes 2>nul

REM Build asset list
set ASSETS=
if exist "%SETUP_X64%" set ASSETS=!ASSETS! "%SETUP_X64%"
if exist "%ZIP_X64%" set ASSETS=!ASSETS! "%ZIP_X64%"
if "!HAS_X86!"=="1" (
    if exist "!ZIP_X86!" set ASSETS=!ASSETS! "!ZIP_X86!"
)

REM Create release
gh release create "v%VERSION%" !ASSETS! ^
  --repo %REPO% ^
  --title "SmartMacroAI v%VERSION%" ^
  --notes-file release-notes-temp.md ^
  2>nul

if %ERRORLEVEL% NEQ 0 (
    REM Fallback: create with auto-generated notes
    gh release create "v%VERSION%" !ASSETS! ^
      --repo %REPO% ^
      --title "SmartMacroAI v%VERSION%" ^
      --generate-notes
)

echo        Release created: https://github.com/%REPO%/releases/tag/v%VERSION%

REM ═══════════════════════════════════════════════════════════════
echo [8/8] Cleanup...
REM ═══════════════════════════════════════════════════════════════

if exist release-notes-temp.md del release-notes-temp.md

echo.
echo ╔══════════════════════════════════════════════╗
echo ║         RELEASE v%VERSION% COMPLETE!             ║
echo ╚══════════════════════════════════════════════╝
echo.
echo  Files in release\:
dir /b "%RELEASE_DIR%\*v%VERSION%*" 2>nul
echo.
echo  GitHub: https://github.com/%REPO%/releases/tag/v%VERSION%
echo.
pause

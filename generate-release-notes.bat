@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion
REM Created by Phạm Duy – Giải pháp tự động hóa thông minh.
REM Generates release-notes-temp.md from CHANGELOG.md for current version

if "%~1"=="" (
    echo Usage: generate-release-notes.bat 1.5.8
    exit /b 1
)

set VERSION=%~1

echo ## SmartMacroAI v%VERSION%> release-notes-temp.md
echo.>> release-notes-temp.md
echo ### Downloads>> release-notes-temp.md
echo.>> release-notes-temp.md
echo ^| File ^| Description ^|>> release-notes-temp.md
echo ^|---^|---^|>> release-notes-temp.md
echo ^| `SmartMacroAI-v%VERSION%-win-x64-Setup.exe` ^| Windows installer (recommended) ^|>> release-notes-temp.md
echo ^| `SmartMacroAI-v%VERSION%-portable-win-x64.zip` ^| Portable — extract and run ^|>> release-notes-temp.md
echo ^| `SmartMacroAI-v%VERSION%-portable-win-x86.zip` ^| Portable x86 (32-bit) ^|>> release-notes-temp.md
echo.>> release-notes-temp.md
echo --->> release-notes-temp.md
echo.>> release-notes-temp.md
echo *Created by Phạm Duy – Giải pháp tự động hóa thông minh.*>> release-notes-temp.md
echo.>> release-notes-temp.md
echo Full Changelog: [v%VERSION%](https://github.com/TroniePh/SmartMacroAI/blob/main/CHANGELOG.md)>> release-notes-temp.md

echo [OK] Generated release-notes-temp.md

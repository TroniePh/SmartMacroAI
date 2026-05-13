# Changelog

All notable changes to SmartMacroAI will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.6.0] - 2026-05-13

### Added
- Multi-Image Search — add up to 20 images per IfImage action, first match wins with priority ordering
- Pixel Color Detection action (`IfPixelColorAction`) with Scan Region mode for area-based color search
- Scroll action — record and playback mouse wheel events
- Drag action — mouse down → Bézier move → up with configurable duration
- MapleStory macro template for Driver Level mode
- ROI Picker button for IfImage search region (visual drag-select on screen)

### Fixed
- **Critical:** Coordinate picker returning (0,0) — root cause: `Hide()` on modal dialog corrupts WPF internal state, replaced with `WindowState.Minimized`
- **Critical:** `DialogResult = true` throwing InvalidOperationException — replaced all 11 dialog files with try/catch pattern
- **Critical:** `MouseLeftButtonDown` on transparent Window unreliable — changed to `PreviewMouseLeftButtonDown` in CoordinatePickerWindow
- Driver Level mode now calls `SetForegroundWindow` before every input operation
- Driver Level `Initialize()` auto-retries on failure
- IfImageAction click handler now supports Driver Level click mode
- Emulator support (LDPlayer/Nox/BlueStacks) auto-detects render child window for accurate targeting

### Changed
- Version bumped to 1.6.0 across all assemblies and UI strings
- Removed debug log statements from production code

## [1.5.7] - 2026-05-08

### Added
- Undo/Redo system (Ctrl+Z / Ctrl+Y) in macro editor with 50-state stack
- Copy/Paste actions (Ctrl+C / Ctrl+V) via JSON serialization
- Keyboard shortcuts: OnPreviewKeyDown handler for editor commands
- Self-contained single-file publish with compression (255 MB exe)
- Inno Setup installer script with optional Playwright component
- LICENSE.txt for distribution

### Fixed
- GDI resource leak in `ScreenshotHelper.CaptureWindow` — proper try/finally cleanup
- `CancellationTokenSource` leak in `App.SafeCheckInterceptionDriver`
- `Drawing.Bitmap` not disposed in `MainWindow.CreateFallbackTrayIcon`
- Scheduler macro runs now use dedicated `CancellationToken` (was `CancellationToken.None`)
- DLL whitelist expanded with 30+ entries — eliminates false WPF runtime warnings
- 9 hardcoded English strings in ShowToast calls replaced with localized versions
- 3 XAML placeholder texts converted to DynamicResource bindings

### Changed
- Version bumped to 1.5.7 across all assemblies and UI strings
- Localization keys increased to 826 (EN + VI perfectly synchronized)
- Release build: 0 errors, 0 warnings in both Debug and Release configurations

## [1.5.6] - 2026-05-06

### Added
- IfImageAction Then/Else container block UI on canvas (BuildIfImageBlock)
- NestedIfImageChildTag / IfImageInsertTag for canvas child routing
- Drag-drop into nested Then/Else branches (both IfImage and IfVariable)
- GitHub Actions release workflow (auto-build win-x64/x86/arm64 on tag push)
- build-release.ps1 local build script
- Image Detection Loop template with sample ElseActions (1s wait)
- Localization keys for IfImage Then/Else labels (EN + VI)

### Fixed
- DialogResult crash in 11 dialog windows (ComponentDispatcher.IsThreadModal guard)
- Removed redundant UIAutomationClient / UIAutomationTypes references (0 warnings)

## [1.5.5] - 2026-05-04

### Added
- Full bilingual UI (English / Vietnamese) with 700+ localization keys
- LanguageManager with absolute pack URIs for reliable runtime language switching
- All user-facing strings localized: XAML `{DynamicResource}` + C# `LanguageManager.Get()`
- Middle click support — `MouseButton` enum (Left / Right / Middle) across all 4 click modes
- Interception driver: real device scanning via `interception_get_hardware_id` P/Invoke
- `RescanDevices()` method for runtime hot-plug device re-detection
- 6 macro templates with full action chains (ready to use after selection)
- Game Farming Loop template (3-skill rotation with RawInput mode)
- Macro template names and DisplayNames fully localized
- GitHub Actions CI workflow (`.github/workflows/build.yml`)
- Inno Setup installer builder
- `MK_MBUTTON` constant in Win32Api
- `INTERCEPTION_MOUSE_MIDDLE_BUTTON_DOWN/UP` constants in InterceptionDriver

### Fixed
- ResourceDictionary duplicate key crash on startup (`ui_Schedule_Once`)
- `DialogResult` exception in DriverInstallDialog (wrapped in `Dispatcher.InvokeAsync`)
- Interception device scan — now uses `interception_get_hardware_id` to detect real connected devices instead of assuming index ranges
- `SendMouseClick` and `SendKey` now use cached device indices instead of re-scanning per call
- Template macros created without displaying actions in MacroEditor (missing `RebuildCanvas()`)
- Dashboard `DataTrigger Value="Loki"` never matched — changed to `Value="Error"` to match MacroRunnerState status
- Telegram tooltip strings in MainWindow.xaml not using DynamicResource
- "Clear all notifications" button not localized

### Changed
- `ClickAction.IsRightClick` (bool) replaced with `ClickAction.Button` (MouseButton enum)
- `InterceptionService.SendMouseClick` signature: `bool rightClick` → `MouseButton button`
- `GlobalHookManager.MouseClicked` event: `Action<int, int, bool>` → `Action<int, int, MouseButton>`
- `MacroRecorder.OnMouseClicked` updated to use MouseButton enum
- Stealth mode PostMessage now supports WM_MBUTTONDOWN/UP for middle click

## [1.5.4] - 2026-05-02

### Added
- Interception Driver Level mode for anti-cheat game bypass
- Auto-detect game window and switch to Driver Level mode
- Driver self-test tool in Settings
- Interception driver embedded resources (`interception.dll`, `install-interception.exe`)
- `InterceptionInstaller` class for automated driver installation with admin elevation
- `InterceptionService` singleton with kernel-level mouse/keyboard injection
- `DriverInstallDialog` with step-by-step installation UI
- Debug logging for Interception initialization status

### Fixed
- Interception device scan — dynamically finds mouse (11-20) and keyboard (1-10) devices
- P/Invoke declarations for `interception_is_mouse` and `interception_is_keyboard`

## [1.5.3] - 2026-04-28

### Fixed
- Concurrent macro OS resource contention — `_osResourceLock` prevents SendInput/PostMessage conflicts
- Multiple macros running simultaneously no longer corrupt each other's input

## [1.5.2] - 2026-04-25

### Added
- Inno Setup installer script (`installer/SmartMacroAI_Setup.iss`)
- Automated installer build in CI pipeline

### Fixed
- Installer source path configuration

## [1.5.1] - 2026-04-22

### Added
- Multi Dashboard — monitor and control multiple macros from a single view
- Run History with date/keyword filters
- Macro Lock (password protection via `MacroLockService`)
- Scheduler — run macros at specific times, intervals, or on startup
- Visual Coordinate Picker — click-to-pick coordinates on screen
- `DashboardRowVm` with INotifyPropertyChanged for live UI updates
- `MacroRunnerState` for per-instance macro execution isolation

### Fixed
- Race condition on multi-window macros
- OCR engine stability improvements

## [1.5.0] - 2026-04-15

### Added
- AI Integration — OpenAI & Gemini support for smart macro decision-making
- Image Recognition — `IfImageAction` with confidence threshold and auto-click
- OCR Text Detection — read text from screen regions via Tesseract
- CSV Auto Fill — `RepeatAction` with CSV data loop
- Web Automation — Playwright-based browser control
- Telegram Notifications — get notified when macros complete
- Script Sharing — share macros via encoded strings
- Anti-Detection — human-like Bézier mouse movement and random delays
- `BehaviorRandomizer` for anti-detection patterns
- `BezierMouseMover` with configurable mouse profiles (Relaxed, Normal, Fast, Instant)

## [1.0.0] - 2026-04-01

### Added
- Initial release
- Core macro engine with action pipeline
- Click action (PostMessage, SendInput modes)
- KeyPress action (PostMessage, SendInput modes)
- Delay action
- TypeText action (clipboard and WM_CHAR modes)
- Repeat action (count-based and infinite loops)
- IfVariable conditional branching
- SetVariable / ClearVariable actions
- Macro recording via global mouse/keyboard hooks
- Macro save/load (JSON serialization)
- Target window binding
- Dashboard with run/stop controls
- Settings panel with hotkey configuration
- Stealth mode (background PostMessage injection)
- Raw Input mode (SendInput with scan codes)
- Hardware mode (SetCursorPos + mouse_event)

---

*Created by Phạm Duy – Giải pháp tự động hóa thông minh.*

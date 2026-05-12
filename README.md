<p align="center">
  <img src="Assets/logo.ico" alt="SmartMacroAI Logo" width="80" height="80">
</p>

<h1 align="center">SmartMacroAI</h1>

<p align="center">
  <strong>Smart RPA Automation for Windows</strong>
</p>

<p align="center">
  <a href="https://github.com/TroniePh/SmartMacroAI/releases/latest"><img src="https://img.shields.io/badge/version-1.5.8-0078D4?style=flat-square" alt="Version"></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%2B-0078D4?style=flat-square&logo=windows" alt="Platform">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet" alt=".NET">
  <img src="https://img.shields.io/badge/language-VI%20%7C%20EN-F7931E?style=flat-square" alt="Language">
  <a href="https://github.com/TroniePh/SmartMacroAI/releases/latest"><img src="https://img.shields.io/github/downloads/TroniePh/SmartMacroAI/total?style=flat-square&color=success" alt="Downloads"></a>
</p>

<p align="center">
  <a href="#features">Features</a> •
  <a href="#download">Download</a> •
  <a href="#quick-start">Quick Start</a> •
  <a href="#driver-level-mode">Driver Level</a> •
  <a href="#build-from-source">Build</a> •
  <a href="#donate">Donate</a> •
  <a href="#tiếng-việt">Tiếng Việt</a>
</p>

---

## Overview

SmartMacroAI is a professional-grade Windows macro automation tool with AI integration. It automates repetitive tasks — game farming, web form filling, data entry, UI testing — with support for anti-cheat games via kernel-level Interception driver.

> *Created by Phạm Duy – Giải pháp tự động hóa thông minh.*

<p align="center">
  <img src="docs/screenshot_dashboard.png" alt="Dashboard" width="800">
</p>

---

## Features

### Input Automation
| Mode | Click | Key Press | Scroll | Drag | Description |
|------|:-----:|:---------:|:------:|:----:|-------------|
| **Stealth** | ✅ | ✅ | ✅ | ✅ | Background PostMessage — window doesn't need focus |
| **Raw Input** | ✅ | ✅ | ✅ | ✅ | SendInput with hardware scan codes |
| **Hardware** | ✅ | ✅ | ✅ | ✅ | SetCursorPos + mouse_event |
| **Driver Level** | ✅ | ✅ | ✅ | ✅ | Kernel Interception driver — bypasses anti-cheat |

### Core Capabilities

- **Image Recognition** — Find image on screen with confidence threshold, auto-click at found position
- **Pixel Color Detection** — Check pixel color at coordinates (lightweight alternative to image match)
- **OCR Text Detection** — Read text from screen regions via Tesseract 5.2
- **If/Else Branching** — Conditional logic with nested Then/Else action branches
- **Loop Control** — Count-based, infinite, and break-condition loops
- **Variables** — Set, read, branch (supports regex matching)
- **CSV Auto Fill** — Loop CSV rows into web/desktop forms automatically
- **AI Integration** — OpenAI & Gemini for smart decision-making within macros
- **Web Automation** — Playwright-based browser control for web tasks
- **CDP Stealth** — Chrome DevTools Protocol for 100% background Chromium clicks

### Productivity

- **Multi Dashboard** — Monitor and control multiple macros simultaneously
- **Macro Recording** — Record mouse clicks, scroll, keyboard in real-time
- **Scheduler** — Time-based, interval, or startup-triggered execution
- **Undo/Redo** — Full Ctrl+Z / Ctrl+Y support in macro editor
- **Copy/Paste** — Duplicate actions with Ctrl+C / Ctrl+V
- **Drag & Drop** — Reorder actions, move in/out of If/Else branches
- **ROI Picker** — Visual drag-select region for image search area
- **6 Templates** — Pre-built macro templates ready to customize
- **Run History** — Execution logs with filtering and statistics

### Security & Distribution

- **Macro Lock** — Password-protect individual macros
- **Script Sharing** — Share macros via encoded strings
- **Anti-Detection** — Bézier mouse curves + randomized delays
- **Telegram Alerts** — Receive notifications on macro completion
- **Bilingual UI** — Full English & Vietnamese (830+ localization keys)
- **Driver Uninstall Guide** — Built-in manual removal instructions

---

## Download

| File | Description |
|------|-------------|
| [**SmartMacroAI-v1.5.8-Setup.exe**](https://github.com/TroniePh/SmartMacroAI/releases/latest) | Windows installer (recommended) |
| [**SmartMacroAI-v1.5.8-win-x64.exe**](https://github.com/TroniePh/SmartMacroAI/releases/latest) | Portable standalone — run directly |
| [**SmartMacroAI-v1.5.8-win-x64.zip**](https://github.com/TroniePh/SmartMacroAI/releases/latest) | Portable — extract and run |

---

## System Requirements

| Component | Requirement |
|-----------|-------------|
| OS | Windows 10 x64 (build 19041+) or later |
| Runtime | .NET 8.0 (bundled in self-contained builds) |
| RAM | 4 GB minimum |
| Storage | 300 MB (core) / 600 MB (with web automation) |
| Admin | Required for Driver Level mode installation |

---

## Quick Start

1. Download the latest installer from [Releases](https://github.com/TroniePh/SmartMacroAI/releases/latest)
2. Run `SmartMacroAI-v1.5.8-Setup.exe` as Administrator
3. Launch SmartMacroAI
4. Click **+ New Macro** — choose a template or start blank
5. Select your target window → add actions → press **Run** (F5)

---

## Driver Level Mode

> Enables macros inside games protected by anti-cheat systems (MapleStory, Cabal Online, etc.)

1. Navigate to **Settings → Driver**
2. Click **Install Now** — approve the UAC prompt
3. **Restart your PC** (required for kernel driver)
4. Open SmartMacroAI → select **Driver Level** mode in any Click/Key action
5. Click **Test Driver** to verify

**Supported:** MapleStory, Cabal Online, MU Online, and most DirectX/OpenGL games with kernel anti-cheat.

**Uninstall:** Settings → Driver → Uninstall, or use the Manual Removal Guide button.

---

## Build from Source

**Prerequisites:** .NET 8 SDK, Windows 10+

```powershell
git clone https://github.com/TroniePh/SmartMacroAI.git
cd SmartMacroAI
dotnet restore
dotnet build
dotnet run
```

### Publish Release

```powershell
.\build-release.ps1 win-x64
# Output: release_output/
```

---

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for full version history.

### v1.5.8 (2026-05-12)
- **New:** Scroll action (record & playback mouse wheel)
- **New:** Drag action (mouse down → move → up with interpolation)
- **New:** IF Pixel Color condition (lightweight alternative to image match)
- **New:** Regex support in IF Variable (matches/notmatches operators)
- **New:** CDP Stealth Service for 100% background Chromium clicks
- **New:** ROI Picker button for image search region (drag-select on screen)
- **New:** Donate tab with QR Bank + PayPal
- **New:** Driver tab in Settings (install/uninstall/manual guide)
- **Fixed:** Stealth click on Edge/Chrome (Chromium detection + proper child window targeting)
- **Fixed:** Double-click bug (was clicking both parent + child renderer)
- **Fixed:** Coordinate picker returning (0,0) in Stealth mode
- **Fixed:** Dashboard Stop button not working (IsRunning sync)
- **Fixed:** Loading macros from external folders now imports to Scripts/
- **Fixed:** Menu dropdown invisible text (dark theme MenuItem style)
- **Fixed:** Installer missing native DLLs + tessdata
- **Improved:** Stealth mode hides from taskbar + Alt+Tab for Chromium
- **Improved:** Image template caching (ConcurrentDictionary)
- **Improved:** Recorder skips non-client area clicks

---

## Donate

If SmartMacroAI helps your workflow, consider supporting continued development:

<p align="center">
  <img src="Assets/qr_bank.png" alt="QR Bank Transfer" width="250"><br>
  <strong>MB Bank — PHAM QUOC DUY — 379997999</strong>
</p>

<p align="center">
  <a href="https://www.paypal.com/paypalme/nhocbobi22">
    <img src="https://img.shields.io/badge/Donate-PayPal-00457C?style=for-the-badge&logo=paypal" alt="Donate via PayPal">
  </a>
</p>

**PayPal:** nhocbobi22@gmail.com

Every contribution motivates continued development and free updates. Thank you! 🙏

---

## Contact

- **Issues:** [github.com/TroniePh/SmartMacroAI/issues](https://github.com/TroniePh/SmartMacroAI/issues)
- **Website:** [tronieph.github.io/SmartMacroAI-Website](https://tronieph.github.io/SmartMacroAI-Website/)

---

## Tiếng Việt

### SmartMacroAI là gì?

SmartMacroAI là công cụ tự động hóa macro chuyên nghiệp cho Windows, tích hợp AI. Tự động hóa tác vụ lặp lại — farm game, điền form web, nhập liệu, kiểm thử UI — hỗ trợ game anti-cheat qua driver Interception cấp nhân.

### Tính năng chính

- **4 chế độ input** — Stealth, Raw Input, Hardware, Driver Level (vượt anti-cheat)
- **Nhận diện hình ảnh** — Tìm ảnh trên màn hình, tự click vào vị trí
- **Kiểm tra màu pixel** — Nhẹ hơn 100x so với image match, dùng cho game check HP/MP
- **OCR đọc chữ** — Nhận dạng text từ vùng màn hình (Tesseract 5.2)
- **Cuộn chuột & Kéo thả** — Ghi và phát lại scroll + drag
- **Logic If/Else** — Phân nhánh điều kiện (hỗ trợ regex)
- **Vòng lặp** — Lặp theo số lần, vô hạn, hoặc điều kiện dừng
- **Tự động điền CSV** — Lặp dữ liệu CSV vào form web/desktop
- **Web Automation** — Điều khiển trình duyệt qua Playwright
- **CDP Stealth** — Click background 100% trên Chrome/Edge
- **Multi Dashboard** — Quản lý nhiều macro cùng lúc
- **Ghi macro** — Thu hành động chuột/phím/scroll thời gian thực
- **Chống phát hiện** — Chuột Bézier + delay ngẫu nhiên
- **Giao diện song ngữ** — 830+ khóa ngôn ngữ Anh/Việt

### Ủng hộ tác giả

<p align="center">
  <img src="Assets/qr_bank.png" alt="QR Chuyển khoản" width="250"><br>
  <strong>MB Bank — PHAM QUOC DUY — 379997999</strong>
</p>

<p align="center">
  <a href="https://www.paypal.com/paypalme/nhocbobi22">
    <img src="https://img.shields.io/badge/Ủng_hộ-PayPal-00457C?style=for-the-badge&logo=paypal" alt="Donate via PayPal">
  </a>
</p>

**PayPal:** nhocbobi22@gmail.com

Mỗi đóng góp đều giúp tác giả có thêm động lực phát triển SmartMacroAI tốt hơn. Cảm ơn bạn! 🙏

---

<p align="center">
  <sub>Made with ❤️ in Vietnam</sub><br>
  <em>Created by Phạm Duy – Giải pháp tự động hóa thông minh.</em>
</p>

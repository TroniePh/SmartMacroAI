// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System.Drawing;
using System.Text.Json.Serialization;

namespace SmartMacroAI.Models;

/// <summary>
/// Click delivery mode, matching the existing KeyInputMode pattern for consistency.
/// Created by Phạm Duy – Giải pháp tự động hóa thông minh.
/// </summary>
public enum ClickMode
{
    /// <summary>PostMessage WM_LBUTTON — no cursor hijack, runs in background (default).</summary>
    Stealth = 0,

    /// <summary>SendInput mouse_event — hijacks physical mouse, games/Anti-Cheat receive it.</summary>
    Raw = 1,

    /// <summary>SetCursorPos + mouse_event + SetForegroundWindow — full hardware, pulls window to foreground.</summary>
    Hardware = 2,

    /// <summary>Interception driver — kernel-level HID emulation, bypasses anti-cheat (HackShield, NGS, EAC).</summary>
    DriverLevel = 3,
}

/// <summary>
/// Base class for every action that can appear in a macro workflow.
/// Uses .NET 8 polymorphic JSON serialization so save/load preserves the concrete type.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ClickAction), "Click")]
[JsonDerivedType(typeof(WaitAction), "Wait")]
[JsonDerivedType(typeof(RepeatAction), "Repeat")]
[JsonDerivedType(typeof(SetVariableAction), "SetVar")]
[JsonDerivedType(typeof(IfVariableAction), "IfVar")]
[JsonDerivedType(typeof(LogAction), "Log")]
[JsonDerivedType(typeof(TryCatchAction), "TryCatch")]
[JsonDerivedType(typeof(TypeAction), "Type")]
[JsonDerivedType(typeof(IfImageAction), "IfImage")]
[JsonDerivedType(typeof(IfTextAction), "IfText")]
[JsonDerivedType(typeof(OcrRegionAction), "OcrRegion")]
[JsonDerivedType(typeof(ClearVariableAction), "ClearVar")]
[JsonDerivedType(typeof(LogVariableAction), "LogVar")]
[JsonDerivedType(typeof(WebNavigateAction), "WebNavigate")]
[JsonDerivedType(typeof(WebClickAction), "WebClick")]
[JsonDerivedType(typeof(WebTypeAction), "WebType")]
[JsonDerivedType(typeof(WebAction), "WebAction")]
[JsonDerivedType(typeof(SystemAction), "System")]
[JsonDerivedType(typeof(LaunchAndBindAction), "LaunchAndBind")]
[JsonDerivedType(typeof(KeyPressAction), "KeyPress")]
[JsonDerivedType(typeof(TelegramAction), "Telegram")]
[JsonDerivedType(typeof(CallMacroAction), "CallMacro")]
[JsonDerivedType(typeof(ResetVariablesAction), "ResetVars")]
public abstract class MacroAction
{
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Per-action notes the user can attach in the editor.
    /// </summary>
    public string? Comment { get; set; }
}

/// <summary>
/// Sends a non-invasive left-click at (X, Y) client-coordinates on the target HWND.
/// Uses PostMessage by default — the physical cursor is never moved.
/// </summary>
public class ClickAction : MacroAction
{
    public int X { get; set; }
    public int Y { get; set; }

    /// <summary>
    /// When true, sends WM_RBUTTONDOWN/UP instead of WM_LBUTTONDOWN/UP.
    /// </summary>
    public bool IsRightClick { get; set; }

    /// <summary>
    /// Monitor index (0-based) where this click was captured.
    /// Used for multi-monitor setups to resolve absolute coordinates correctly.
    /// -1 = not set / use primary monitor.
    /// </summary>
    public int MonitorIndex { get; set; } = -1;

    /// <summary>
    /// Click delivery mode: Stealth (PostMessage), Raw (SendInput), or Hardware (full HW).
    /// Created by Phạm Duy – Giải pháp tự động hóa thông minh.
    /// </summary>
    public ClickMode Mode { get; set; } = ClickMode.Stealth;

    public ClickAction()
    {
        DisplayName = "Click";
    }
}

/// <summary>
/// Pauses execution: optional “wait until template appears”, else a fixed delay or legacy random <see cref="DelayMin"/>/<see cref="DelayMax"/>.
/// </summary>
public class WaitAction : MacroAction
{
    /// <summary>Fixed delay (ms) when <see cref="WaitForImage"/> is empty and min/max are equal.</summary>
    public int Milliseconds { get; set; } = 1000;

    /// <summary>When set, polls vision until the template is found or <see cref="WaitTimeoutMs"/> elapses.</summary>
    public string WaitForImage { get; set; } = string.Empty;

    public double WaitThreshold { get; set; } = 0.8;

    public int WaitTimeoutMs { get; set; } = 10000;

    /// <summary>Legacy inclusive minimum (ms); used with <see cref="DelayMax"/> when they differ for a random wait.</summary>
    public int DelayMin { get; set; } = 1000;

    /// <summary>Legacy inclusive maximum (ms).</summary>
    public int DelayMax { get; set; } = 1000;

    /// <summary>When set with a valid ROI, polls Windows OCR until the text contains this substring.</summary>
    public string WaitForOcrContains { get; set; } = string.Empty;

    public int OcrRegionX { get; set; }
    public int OcrRegionY { get; set; }
    public int OcrRegionWidth { get; set; }
    public int OcrRegionHeight { get; set; }

    public int OcrPollIntervalMs { get; set; } = 500;

    public WaitAction()
    {
        DisplayName = "Chờ";
    }
}

/// <summary>
/// Repeats <see cref="LoopActions"/> a fixed or infinite number of times with an optional vision break image.
/// </summary>
public class RepeatAction : MacroAction
{
    /// <summary>0 = infinite until cancel or break image.</summary>
    public int RepeatCount { get; set; } = 1;

    public int IntervalMs { get; set; } = 500;

    public string BreakIfImagePath { get; set; } = string.Empty;

    public double BreakThreshold { get; set; } = 0.8;

    public List<MacroAction> LoopActions { get; set; } = [];

    public RepeatAction()
    {
        DisplayName = "Lặp lại";
    }
}

/// <summary>Sets or adjusts a runtime variable (see engine <c>VariableManager</c>).</summary>
public class SetVariableAction : MacroAction
{
    public string VarName { get; set; } = "myVar";

    /// <summary>Literal or placeholders <c>{otherVar}</c> (expanded at runtime).</summary>
    public string Value { get; set; } = "0";

    /// <summary><c>Manual</c> uses <see cref="Value"/>; <c>Clipboard</c> reads clipboard text at runtime.</summary>
    public string ValueSource { get; set; } = "Manual";

    /// <summary><c>Set</c>, <c>Increment</c>, or <c>Decrement</c>.</summary>
    public string Operation { get; set; } = "Set";

    public SetVariableAction()
    {
        DisplayName = "Gán biến";
    }
}

/// <summary>Branches on a variable value compared to <see cref="Value"/>.</summary>
public class IfVariableAction : MacroAction
{
    public string VarName { get; set; } = "myVar";

    /// <summary>One of: ==, !=, &gt;, &lt;, &gt;=, &lt;=</summary>
    public string CompareOp { get; set; } = "==";

    public string Value { get; set; } = "0";

    public List<MacroAction> ThenActions { get; set; } = [];

    public List<MacroAction> ElseActions { get; set; } = [];

    public IfVariableAction()
    {
        DisplayName = "Nếu biến thỏa mãn";
    }
}

/// <summary>Writes a message to the execution log and optional run report.</summary>
public class LogAction : MacroAction
{
    public string Message { get; set; } = "Log: {myVar}";

    public LogAction()
    {
        DisplayName = "Ghi nhật ký";
    }
}

/// <summary>Runs <see cref="TryActions"/> and on failure runs <see cref="CatchActions"/>.</summary>
public class TryCatchAction : MacroAction
{
    public List<MacroAction> TryActions { get; set; } = [];

    public List<MacroAction> CatchActions { get; set; } = [];

    public TryCatchAction()
    {
        DisplayName = "Bẫy lỗi (Try/Catch)";
    }
}

/// <summary>
/// Input method for typing text into a target window.
/// </summary>
public enum TypeInputMethod
{
    /// <summary>Default — use clipboard paste (recommended for Vietnamese/Unikey).</summary>
    Clipboard = 0,

    /// <summary>WM_CHAR char-by-char (fallback for apps that block clipboard).</summary>
    WmChar = 1,
}

/// <summary>
/// Types text into the target HWND using clipboard paste or WM_CHAR messages.
/// </summary>
public class TypeAction : MacroAction
{
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Optional inter-keystroke delay (ms) when using WM_CHAR method.
    /// Default = 0 means clipboard paste is used (recommended for Vietnamese/Unikey).
    /// </summary>
    public int KeyDelayMs { get; set; }

    /// <summary>
    /// Input method: Clipboard (default, recommended for Vietnamese) or WM_CHAR.
    /// </summary>
    public TypeInputMethod InputMethod { get; set; } = TypeInputMethod.Clipboard;

    public TypeAction()
    {
        DisplayName = "Type Text";
    }
}

/// <summary>
/// Conditional: captures the target window in the background (PrintWindow),
/// runs OpenCV template matching, and executes <see cref="ThenActions"/> only if
/// the template image is found above <see cref="Threshold"/>.
/// </summary>
public class IfImageAction : MacroAction
{
    public string ImagePath { get; set; } = string.Empty;

    /// <summary>
    /// Match confidence threshold (0.0 – 1.0). Default 0.8 = 80 %.
    /// </summary>
    public double Threshold { get; set; } = 0.8;

    /// <summary>Whether to click the center of the found image.</summary>
    public bool ClickOnFound { get; set; } = true;

    /// <summary>Half-range (pixels) for random offset passed to stealth click.</summary>
    public int RandomOffset { get; set; } = 3;

    /// <summary>
    /// Click mode used when <see cref="ClickOnFound"/> is true.
    /// Created by Phạm Duy – Giải pháp tự động hóa thông minh.
    /// </summary>
    public ClickMode ClickMode { get; set; } = ClickMode.Stealth;

    /// <summary>
    /// When true, keeps retrying until the image is found (loop with <see cref="RetryIntervalMs"/>).
    /// Only then runs ThenActions; if <see cref="MaxRetryCount"/> is hit, runs ElseActions.
    /// </summary>
    public bool RetryUntilFound { get; set; } = false;

    /// <summary>Delay (ms) between each retry attempt when <see cref="RetryUntilFound"/> is true.</summary>
    public int RetryIntervalMs { get; set; } = 500;

    /// <summary>
    /// Maximum retry count when <see cref="RetryUntilFound"/> is true.
    /// 0 = unlimited retries until timeout.
    /// </summary>
    public int MaxRetryCount { get; set; } = 0;

    /// <summary>
    /// Maximum total elapsed time (ms) before giving up and running <see cref="ElseActions"/>.
    /// Created by Phạm Duy – Giải pháp tự động hóa thông minh.
    /// </summary>
    public int TimeoutMs { get; set; } = 5000;

    /// <summary>Optional ROI origin X (client pixels). Null with other ROI fields = full window.</summary>
    public int? RoiX { get; set; }

    public int? RoiY { get; set; }

    public int? RoiWidth { get; set; }

    public int? RoiHeight { get; set; }

    /// <summary>
    /// Actions executed when the image IS found.
    /// </summary>
    public List<MacroAction> ThenActions { get; set; } = [];

    /// <summary>
    /// Actions executed when the image is NOT found (optional).
    /// </summary>
    public List<MacroAction> ElseActions { get; set; } = [];

    /// <summary>
    /// ROI for multi-scale template match; null = full client area.
    /// </summary>
    [JsonIgnore]
    public Rectangle? SearchRegion =>
        RoiX.HasValue && RoiY.HasValue
        && RoiWidth.HasValue && RoiHeight.HasValue
        && RoiWidth.Value > 0 && RoiHeight.Value > 0
            ? new Rectangle(RoiX.Value, RoiY.Value, RoiWidth.Value, RoiHeight.Value)
            : null;

    public IfImageAction()
    {
        DisplayName = "IF Image Found";
    }
}

/// <summary>
/// Conditional: captures the target window, runs OCR (Tesseract),
/// and executes <see cref="ThenActions"/> only if the specified text is detected.
/// </summary>
public class IfTextAction : MacroAction
{
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// When true, the OCR comparison is case-insensitive.
    /// </summary>
    public bool IgnoreCase { get; set; } = true;

    /// <summary>
    /// When true, matches if the OCR result *contains* the text (substring match).
    /// When false, requires an exact match of the full OCR output.
    /// </summary>
    public bool PartialMatch { get; set; } = true;

    public List<MacroAction> ThenActions { get; set; } = [];
    public List<MacroAction> ElseActions { get; set; } = [];

    public IfTextAction()
    {
        DisplayName = "IF Text Found";
    }
}

/// <summary>
/// Reads text from a screen rectangle via Windows.Media.Ocr and stores it in a variable (<c>{{name}}</c>).
/// </summary>
public class OcrRegionAction : MacroAction
{
    public int ScreenX { get; set; }
    public int ScreenY { get; set; }
    public int ScreenWidth { get; set; } = 200;
    public int ScreenHeight { get; set; } = 80;

    /// <summary>Variable name without braces (e.g. <c>ocr_result</c> → <c>{{ocr_result}}</c>).</summary>
    public string OutputVariableName { get; set; } = "ocr_result";

    public OcrRegionAction()
    {
        DisplayName = "Đọc văn bản (OCR)";
    }
}

/// <summary>Clears one variable from the runtime string store, or all when <see cref="VarName"/> is empty.</summary>
public class ClearVariableAction : MacroAction
{
    /// <summary>Empty = clear all user variables in the runtime <c>VariableStore</c>.</summary>
    public string VarName { get; set; } = string.Empty;

    public ClearVariableAction()
    {
        DisplayName = "Xóa biến";
    }
}

/// <summary>Resets all runtime variables (<c>VariableManager</c> and <c>VariableStore</c>) to initial state.</summary>
public class ResetVariablesAction : MacroAction
{
    public ResetVariablesAction()
    {
        DisplayName = "Reset biến";
    }
}

/// <summary>Writes <c>name = value</c> to the execution log.</summary>
public class LogVariableAction : MacroAction
{
    public string VarName { get; set; } = "myVar";

    public LogVariableAction()
    {
        DisplayName = "In biến vào log";
    }
}

// ── Unified Web Action (new single-button model) ──

public enum WebActionType { Navigate, Click, Type, Scrape }

/// <summary>
/// Unified Playwright web action — one class for Navigate / Click / Type / Scrape.
/// Users pick the action type from a dropdown, then fill in URL, Selector, and/or Text.
/// </summary>
public class WebAction : MacroAction
{
    public string Url { get; set; } = string.Empty;
    public string Selector { get; set; } = string.Empty;
    public WebActionType ActionType { get; set; } = WebActionType.Navigate;
    public string TextToType { get; set; } = string.Empty;

    public WebAction()
    {
        DisplayName = "Web Action";
    }
}

// ── Legacy Web Actions (kept for backward-compat with saved scripts) ──

/// <summary>
/// Opens a URL in the Playwright-controlled browser (visible window).
/// Hybrid workflows: use alongside Win32 desktop actions in the same script.
/// </summary>
public class WebNavigateAction : MacroAction
{
    public string Url { get; set; } = string.Empty;

    public WebNavigateAction()
    {
        DisplayName = "Web: Navigate";
    }
}

/// <summary>
/// Clicks an element via Playwright. <see cref="CssSelector"/> accepts CSS or XPath (xpath=...).
/// </summary>
public class WebClickAction : MacroAction
{
    public string CssSelector { get; set; } = string.Empty;

    public WebClickAction()
    {
        DisplayName = "Web: Click";
    }
}

/// <summary>
/// Fills an input via Playwright (clears then types).
/// </summary>
public class WebTypeAction : MacroAction
{
    public string CssSelector { get; set; } = string.Empty;
    public string TextToType { get; set; } = string.Empty;

    public WebTypeAction()
    {
        DisplayName = "Web: Type";
    }
}

// ── System / file operations ──

public enum SystemActionKind
{
    CreateFolder,
    CopyFile,
    MoveFile,
    DeleteFile,
}

/// <summary>
/// File-system steps (create folder, copy/move/delete files or directories).
/// Paths support macro variable expansion at runtime.
/// </summary>
public class SystemAction : MacroAction
{
    public SystemActionKind Kind { get; set; } = SystemActionKind.CreateFolder;

    /// <summary>Used for <see cref="SystemActionKind.CreateFolder"/> and <see cref="SystemActionKind.DeleteFile"/>.</summary>
    public string? Path { get; set; }

    public string? SourcePath { get; set; }
    public string? DestinationPath { get; set; }

    /// <summary>For copy/move when the destination file already exists.</summary>
    public bool Overwrite { get; set; }

    /// <summary>For delete when <see cref="Path"/> is a directory.</summary>
    public bool RecursiveDelete { get; set; }

    public SystemAction()
    {
        DisplayName = "System / Files";
    }
}

// ── Launch browser and bind Win32 target HWND ──

public enum LaunchBrowserKind
{
    Edge,
    Chrome,
}

/// <summary>
/// Starts Chrome or Edge with a URL, then binds the macro desktop target to the browser main window.
/// </summary>
public class LaunchAndBindAction : MacroAction
{
    public string Url { get; set; } = string.Empty;
    public LaunchBrowserKind Browser { get; set; } = LaunchBrowserKind.Edge;

    /// <summary>Max time to wait for a main window (ms). Values ≤ 1000 fall back to 60s in the engine.</summary>
    public int BindTimeoutMs { get; set; } = 60_000;

    /// <summary>Polling interval while waiting for the window (ms). Clamped to 100–2000 in the engine.</summary>
    public int PollIntervalMs { get; set; } = 500;

    public LaunchAndBindAction()
    {
        DisplayName = "Launch & Bind";
    }
}

/// <summary>
/// Input mode for key press actions, determining how keystrokes are sent.
/// Created by Phạm Duy – Giải pháp tự động hóa thông minh.
/// </summary>
public enum KeyInputMode
{
    /// <summary>PostMessage (stealth, default) — runs in background, no foreground window needed.</summary>
    Auto = 0,

    /// <summary>SendInput with VirtualKey — for Chrome, Electron, VS Code (requires foreground).</summary>
    SendInput = 1,

    /// <summary>SendInput with ScanCode only — for DirectX games and Anti-Cheat systems.</summary>
    RawInput = 2,

    /// <summary>Interception driver — kernel-level HID emulation, bypasses anti-cheat (HackShield, NGS, EAC).</summary>
    DriverLevel = 3,
}

/// <summary>
/// Sends a single keystroke (key-down followed by key-up via PostMessage) to the target window.
/// Unlike TypeAction which sends WM_CHAR for printable characters, this sends raw VK codes
/// via WM_KEYDOWN / WM_KEYUP — ideal for modifier keys (Ctrl, Alt, F-keys, etc.).
/// </summary>
public class KeyPressAction : MacroAction
{
    /// <summary>Win32 virtual-key code (VK_*). Zero means "not set / not executed".</summary>
    public int VirtualKeyCode { get; set; }

    /// <summary>Win32 scan code extracted via MapVirtualKey(VK, 0).</summary>
    public int ScanCode { get; set; }

    /// <summary>Human-readable name set when the key is captured, e.g. "F5", "Return", "A", "Ctrl+S".</summary>
    public string KeyName { get; set; } = string.Empty;

    /// <summary>Modifier key state active when this action was recorded.</summary>
    public KeyModifiers Modifiers { get; set; } = new();

    /// <summary>How long (ms) the key is held between down and up events.</summary>
    public int HoldDurationMs { get; set; } = 50;

    /// <summary>
    /// Input mode: Auto (PostMessage/stealth), SendInput (Chrome/Electron), or RawInput (DirectX games).
    /// </summary>
    public KeyInputMode InputMode { get; set; } = KeyInputMode.Auto;

    public KeyPressAction()
    {
        DisplayName = "Nhấn phím";
    }
}

/// <summary>
/// Describes which modifier keys (Shift, Ctrl, Alt) were held when a <see cref="KeyPressAction"/>
/// was recorded. Used to reconstruct the exact key combo during playback.
/// </summary>
public class KeyModifiers
{
    /// <summary>True if Left or Right Shift was held.</summary>
    public bool Shift { get; set; }
    /// <summary>True if Left or Right Ctrl was held.</summary>
    public bool Ctrl { get; set; }
    /// <summary>True if Left or Right Alt was held.</summary>
    public bool Alt { get; set; }
}

// ── Telegram notification ──

/// <summary>
/// Sends an HTML-formatted message to a Telegram chat via the Bot API.
/// Supports <c>{{variable}}</c> placeholders resolved from the current data row.
/// Created by Phạm Duy – Giải pháp tự động hóa thông minh.
/// </summary>
public class TelegramAction : MacroAction
{
    /// <summary>Bot token obtained from @BotFather (e.g. 123456789:ABCdefGHI…).</summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>Target chat ID (numeric or @channel_username).</summary>
    public string ChatId { get; set; } = string.Empty;

    /// <summary>
    /// Message body with optional <c>{{variable}}</c> tokens. HTML formatting is supported
    /// (e.g. &lt;b&gt;bold&lt;/b&gt;, &lt;code&gt;…&lt;/code&gt;).
    /// </summary>
    public string Message { get; set; } = string.Empty;

    public TelegramAction()
    {
        DisplayName = "Gửi Telegram";
    }
}

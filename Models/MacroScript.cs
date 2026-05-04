using SmartMacroAI.Localization;

namespace SmartMacroAI.Models;

/// <summary>
/// Root object representing a complete macro workflow.
/// Contains metadata + the ordered list of actions that make up the automation.
/// This is the unit that gets serialized to / deserialized from a .json file.
/// </summary>
public class MacroScript
{
    public string Name { get; set; } = "Untitled Macro";
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Static key/value pairs merged into the runtime variable map each iteration (before CSV columns).
    /// </summary>
    public Dictionary<string, string>? Variables { get; set; }

    /// <summary>
    /// Optional CSV: each iteration maps row columns into runtime variables (requires a finite <see cref="RepeatCount"/>; infinite repeat is not allowed with CSV).
    /// </summary>
    public string? LoopCsvFilePath { get; set; }

    public List<string> LoopCsvColumnNames { get; set; } = [];

    public bool LoopCsvHasHeader { get; set; } = true;

    /// <summary>
    /// When true and a data-driven CSV run hits an error on one row, execution skips that row
    /// and continues with the next row instead of stopping the entire macro.
    /// </summary>
    public bool LoopCsvSkipOnError { get; set; } = true;

    /// <summary>
    /// The Win32 window title pattern used to locate the target HWND at runtime.
    /// Supports partial matching (e.g. "Notepad" will match "Untitled - Notepad").
    /// </summary>
    public string TargetWindowTitle { get; set; } = string.Empty;

    /// <summary>
    /// Default click mode for actions in this script. Auto-set to DriverLevel when a game window is detected.
    /// </summary>
    public ClickMode DefaultClickMode { get; set; } = ClickMode.Stealth;

    /// <summary>
    /// Default key input mode for actions in this script. Auto-set to DriverLevel when a game window is detected.
    /// </summary>
    public KeyInputMode DefaultKeyPressMode { get; set; } = KeyInputMode.Auto;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// How many times the entire action list should loop. 0 = infinite.
    /// </summary>
    public int RepeatCount { get; set; } = 1;

    /// <summary>
    /// When greater than 0, execution stops after this many minutes (linked cancellation),
    /// even when <see cref="RepeatCount"/> is 0 (infinite loop). 0 = no time limit.
    /// </summary>
    public int AutoStopMinutes { get; set; }

    /// <summary>
    /// Delay in minutes between each iteration. 0 = no delay (back-to-back).
    /// Useful for scheduled tasks like "run every 5 minutes".
    /// </summary>
    public int IntervalMinutes { get; set; }

    /// <summary>
    /// Ordered list of actions that form the macro workflow.
    /// </summary>
    public List<MacroAction> Actions { get; set; } = [];

    /// <summary>
    /// Default Telegram Bot Token used for auto-completion notifications (optional, per-script override).
    /// </summary>
    public string? TelegramBotToken { get; set; }

    /// <summary>
    /// Default Telegram Chat ID used for auto-completion notifications (optional, per-script override).
    /// </summary>
    public string? TelegramChatId { get; set; }

    /// <summary>
    /// When true, SmartMacroAI sends a Telegram notification on macro completion (success or error).
    /// Uses <see cref="TelegramBotToken"/> and <see cref="TelegramChatId"/>.
    /// </summary>
    public bool SendTelegramOnComplete { get; set; } = false;

    /// <summary>
    /// HTML-formatted message sent via Telegram when the macro completes successfully.
    /// Supports placeholders: {MacroName}, {RowsDone}, {RowsTotal}, {Duration}, {MachineName}.
    /// </summary>
    public string TelegramCompleteMessage { get; set; } =
        LanguageManager.GetString("ui_Telegram_SuccessTemplate");

    /// <summary>
    /// HTML-formatted message sent via Telegram when the macro completes with errors.
    /// Supports placeholders: {MacroName}, {RowsDone}, {RowsTotal}, {ErrorMessage}, {MachineName}.
    /// </summary>
    public string TelegramErrorMessage { get; set; } =
        LanguageManager.GetString("ui_Telegram_ErrorTemplate");

    // === ADDED for scheduler and lock features ===

    /// <summary>Runtime-only: file path this script was loaded from (not serialized).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? FilePath { get; set; }

    /// <summary>Scheduler settings for this macro.</summary>
    public ScheduleSettings? Schedule { get; set; }

    /// <summary>SHA256 hash of lock password. Null = no lock.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>Require password to RUN this macro.</summary>
    public bool LockRun { get; set; } = false;

    /// <summary>Require password to EDIT this macro.</summary>
    public bool LockEdit { get; set; } = false;

    /// <summary>Run in hardware/anti-cheat mode.</summary>
    public bool HwMode { get; set; } = false;

    /// <summary>Use stealth mode for target window.</summary>
    public bool StealthMode { get; set; } = false;
}

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
        "✅ <b>{MacroName}</b> chạy xong!\n" +
        "📊 Hoàn thành: <b>{RowsDone}/{RowsTotal}</b> dòng\n" +
        "⏱ Thời gian: <code>{Duration}</code>\n" +
        "💻 Máy: <code>{MachineName}</code>";

    /// <summary>
    /// HTML-formatted message sent via Telegram when the macro completes with errors.
    /// Supports placeholders: {MacroName}, {RowsDone}, {RowsTotal}, {ErrorMessage}, {MachineName}.
    /// </summary>
    public string TelegramErrorMessage { get; set; } =
        "❌ <b>{MacroName}</b> bị lỗi!\n" +
        "📊 Hoàn thành: <b>{RowsDone}/{RowsTotal}</b> dòng\n" +
        "🔴 Lỗi: <code>{ErrorMessage}</code>\n" +
        "💻 Máy: <code>{MachineName}</code>";
}

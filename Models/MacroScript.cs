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
}

namespace SmartMacroAI.Models;

/// <summary>
/// Represents a single top-level window with its process metadata.
/// Used by the target-window ComboBoxes so users can distinguish
/// multiple instances of the same application (e.g. several 9Dragons clients).
/// </summary>
public sealed class WindowEntry
{
    public IntPtr Handle { get; init; }
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = "";
    public string Title { get; init; } = "";

    /// <summary>
    /// The ComboBox displays this when IsEditable = True.
    /// Format: [ProcessName] — PID: 1234 — Window Title
    /// </summary>
    public override string ToString() =>
        $"[{ProcessName}] — PID: {ProcessId} — {Title}";
}

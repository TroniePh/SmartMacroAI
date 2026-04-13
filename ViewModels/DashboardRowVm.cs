using System.ComponentModel;
using System.Runtime.CompilerServices;
using SmartMacroAI.Core;
using SmartMacroAI.Models;

namespace SmartMacroAI.ViewModels;

/// <summary>
/// ViewModel for a single row in the Dashboard DataGrid.
/// Implements INotifyPropertyChanged so the DataGrid auto-updates
/// when Status, IsRunning, etc. change at runtime.
/// </summary>
public sealed class DashboardRowVm : INotifyPropertyChanged
{
    public string FilePath { get; set; } = "";
    public MacroScript Script { get; set; } = new();

    public string MacroName => Script.Name;
    public int ActionCount => Script.Actions.Count;

    private string _targetWindow = "";
    public string TargetWindow
    {
        get => _targetWindow;
        set { if (_targetWindow != value) { _targetWindow = value; Notify(); } }
    }

    private int _runCount = 1;
    public int RunCount
    {
        get => _runCount;
        set { if (_runCount != value) { _runCount = value; Notify(); } }
    }

    private int _intervalMinutes;
    public int IntervalMinutes
    {
        get => _intervalMinutes;
        set { if (_intervalMinutes != value) { _intervalMinutes = value; Notify(); } }
    }

    private string _status = "Ready";
    public string Status
    {
        get => _status;
        set { if (_status != value) { _status = value; Notify(); } }
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning != value)
            {
                _isRunning = value;
                Notify();
                Notify(nameof(CanStart));
                Notify(nameof(CanStop));
            }
        }
    }

    public bool CanStart => !_isRunning;
    public bool CanStop => _isRunning;

    private bool _stealthMode;
    public bool StealthMode
    {
        get => _stealthMode;
        set { if (_stealthMode != value) { _stealthMode = value; Notify(); } }
    }

    private bool _hardwareMode;
    public bool HardwareMode
    {
        get => _hardwareMode;
        set { if (_hardwareMode != value) { _hardwareMode = value; Notify(); } }
    }

    // Runtime state (not bound to UI)
    public MacroEngine? Engine { get; set; }
    public CancellationTokenSource? Cts { get; set; }
    public IntPtr TargetHwnd { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Call after changing <see cref="Script"/>.Name or action list so the Dashboard row refreshes.</summary>
    public void NotifyScriptMetadataChanged()
    {
        Notify(nameof(MacroName));
        Notify(nameof(ActionCount));
    }
}

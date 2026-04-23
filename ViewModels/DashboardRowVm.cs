using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
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
    public MacroScript Script
    {
        get => _script;
        set
        {
            _script = value;
            _sendTelegramOnComplete = value.SendTelegramOnComplete;
            _originalName = value.Name;
            Notify(nameof(IsLocked));
            Notify(nameof(LockStatus));
            Notify(nameof(ShowLockedIcon));
            Notify(nameof(MacroName));
        }
    }
    private MacroScript _script = new();

    private bool _isEditing = false;
    public bool IsEditing
    {
        get => _isEditing;
        set { _isEditing = value; Notify(); Notify(nameof(IsNotEditing)); }
    }
    public bool IsNotEditing => !_isEditing;

    private string _originalName = "";
    public string OriginalName
    {
        get => _originalName;
        set { _originalName = value; Notify(); }
    }

    public string MacroName
    {
        get => _script.Name;
        set
        {
            if (_script.Name == value) return;
            _script.Name = value;
            Notify();
        }
    }
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
        set
        {
            if (_status != value)
            {
                _status = value;
                Notify();
                Notify(nameof(CanStart));
                Notify(nameof(CanStop));
                Notify(nameof(RunButtonColor));
                Notify(nameof(RunButtonText));
                Notify(nameof(IsStopMode));
            }
        }
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

    public ScheduleSettings? Schedule => Script.Schedule;

    public bool HasSchedule => Script.Schedule?.Enabled == true;

    public string ScheduleSummary => Script.Schedule?.Enabled == true ? Script.Schedule.Mode switch
    {
        "Daily" => $"Hàng ngày {Script.Schedule.RunAt:hh\\:mm}",
        "Interval" => $"Mỗi {Script.Schedule.IntervalMinutes} phút",
        "Once" => Script.Schedule.RunOnce.HasValue ? $"1 lần {Script.Schedule.RunOnce:dd/MM HH:mm}" : "1 lần",
        "OnStartup" => "Khởi động",
        _ => "Đã lên lịch"
    } : "—";

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

    private bool _sendTelegramOnComplete;
    public bool SendTelegramOnComplete
    {
        get => _sendTelegramOnComplete;
        set
        {
            if (_sendTelegramOnComplete != value)
            {
                _sendTelegramOnComplete = value;
                Script.SendTelegramOnComplete = value;
                Notify();
                ScriptManager.Save(Script, FilePath);
            }
        }
    }

    public bool IsLocked => MacroLockService.IsLocked(Script);

    public string LockStatus => MacroLockService.GetLockStatus(Script);

    /// <summary>Lock icon path data for UI — filled by IsLocked.</summary>
    public bool ShowLockedIcon => IsLocked;

    /// <summary>Run button color based on current macro status.</summary>
    public string RunButtonColor => Status switch
    {
        "Running" => "#FAB387",
        "Error" => "#F38BA8",
        _ => "#A6E3A1"
    };

    /// <summary>Run button text based on current macro status.</summary>
    public string RunButtonText => Status switch
    {
        "Running" => "Dừng",
        _ => "Chạy"
    };

    /// <summary>Whether the primary action should show Stop (true) or Run (false).</summary>
    public bool IsStopMode => Status == "Running";

    // Runtime state (not bound to UI)
    public MacroEngine? Engine { get; set; }
    public CancellationTokenSource? Cts { get; set; }
    public IntPtr TargetHwnd { get; set; }

    /// <summary>Per-instance runner for dashboard execution. Replaces shared Cts/Engine.</summary>
    public MacroRunnerState Runner { get; set; } = new();

    /// <summary>Wires the runner's StatusChanged to update the UI-bound Status property.</summary>
    public void InitRunner(MacroScript script, IntPtr hwnd, bool stealth, bool hwMode)
    {
        Runner = new MacroRunnerState
        {
            Script = script,
            TargetHwnd = hwnd,
            IsStealthMode = stealth,
            HwMode = hwMode
        };
        Runner.StatusChanged += s =>
        {
            Application.Current.Dispatcher.InvokeAsync(() => Status = s);
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Call from MainWindow when properties change externally (e.g., after schedule edit).</summary>
    public void NotifyExternal(string propertyName)
        => Notify(propertyName);

    /// <summary>Call after changing <see cref="Script"/>.Name or action list so the Dashboard row refreshes.</summary>
    public void NotifyScriptMetadataChanged()
    {
        Notify(nameof(MacroName));
        Notify(nameof(ActionCount));
    }
}

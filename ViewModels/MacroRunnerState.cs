using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SmartMacroAI.Core;
using SmartMacroAI.Models;

namespace SmartMacroAI.ViewModels;

/// <summary>
/// Encapsulates all per-instance macro execution state.
/// Each dashboard row owns its own <see cref="MacroRunnerState"/> — no sharing, no static state.
/// Created by Phạm Duy – Giải pháp tự động hóa thông minh.
/// </summary>
public class MacroRunnerState
{
    public MacroScript Script { get; set; } = new();
    public IntPtr TargetHwnd { get; set; }
    public bool IsStealthMode { get; set; }
    public bool HwMode { get; set; }

    private CancellationTokenSource? _cts;
    private volatile bool _isRunning;
    public bool IsRunning => _isRunning;

    public event Action<string>? StatusChanged;

    public void Start(Action<string> logAction)
    {
        if (_isRunning) return;

        _cts = new CancellationTokenSource();
        _isRunning = true;
        StatusChanged?.Invoke("Running");

        void SafeLog(string msg) =>
            Application.Current.Dispatcher.InvokeAsync(() => logAction(msg));

        _ = Task.Run(async () =>
        {
            try
            {
                SafeLog($"[{Script.Name}] Bắt đầu...");

                var engine = new MacroEngine(Script, TargetHwnd, SafeLog) { HardwareMode = HwMode };
                await engine.ExecuteScriptAsync(Script, TargetHwnd, _cts!.Token);
                SafeLog($"[{Script.Name}] Hoàn tất.");
                StatusChanged?.Invoke("Ready");
            }
            catch (OperationCanceledException)
            {
                SafeLog($"[{Script.Name}] Đã dừng.");
                StatusChanged?.Invoke("Ready");
            }
            catch (Exception ex)
            {
                SafeLog($"[{Script.Name}] Lỗi: {ex.Message}");
                StatusChanged?.Invoke("Error");
            }
            finally
            {
                _isRunning = false;
                _cts?.Dispose();
                _cts = null;
            }
        });
    }

    public void Stop() => _cts?.Cancel();
}

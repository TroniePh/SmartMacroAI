using System.Diagnostics;
using System.Text;
using SmartMacroAI.Models;

namespace SmartMacroAI.Core;

/// <summary>
/// Records user mouse clicks and keystrokes into a list of <see cref="MacroAction"/>
/// objects using <see cref="GlobalHookManager"/>.  Clicks are filtered to the target
/// window and coordinates are converted to client-relative via ScreenToClient.
/// Consecutive keystrokes are batched into a single <see cref="TypeAction"/>.
/// </summary>
public sealed class MacroRecorder : IDisposable
{
    private const uint VK_F10 = 0x79;
    private const int MIN_WAIT_MS = 100;

    private readonly GlobalHookManager _hookManager = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly List<MacroAction> _recordedActions = [];
    private readonly StringBuilder _textBuffer = new();

    private IntPtr _targetHwnd;
    private long _lastActionMs;
    private bool _disposed;

    // ═══════════════════════════════════════════════
    //  EVENTS
    // ═══════════════════════════════════════════════

    public event Action<string>? Log;
    public event Action<int>? ActionRecorded;

    /// <summary>
    /// Fires when the user presses F10 during recording (the global stop key).
    /// The subscriber should call <see cref="StopRecording"/> (preferably via
    /// Dispatcher.BeginInvoke to avoid unhooking inside the hook callback).
    /// </summary>
    public event Action? StopKeyPressed;

    // ═══════════════════════════════════════════════
    //  STATE
    // ═══════════════════════════════════════════════

    public bool IsRecording => _hookManager.IsRecording;
    public IReadOnlyList<MacroAction> RecordedActions => _recordedActions;
    public TimeSpan Elapsed => _stopwatch.Elapsed;

    // ═══════════════════════════════════════════════
    //  START / STOP
    // ═══════════════════════════════════════════════

    public void StartRecording(IntPtr targetHwnd)
    {
        if (IsRecording) return;

        if (targetHwnd == IntPtr.Zero || !Win32Api.IsWindow(targetHwnd))
            throw new ArgumentException("Invalid target window handle.", nameof(targetHwnd));

        _targetHwnd = targetHwnd;
        _recordedActions.Clear();
        _textBuffer.Clear();
        _lastActionMs = 0;

        _hookManager.MouseClicked += OnMouseClicked;
        _hookManager.KeyPressed += OnKeyPressed;

        _stopwatch.Restart();
        _hookManager.StartRecording();

        string title = Win32Api.GetWindowTitle(_targetHwnd);
        Log?.Invoke($"Recording started — target: \"{title}\" (HWND=0x{_targetHwnd:X})");
    }

    /// <summary>
    /// Stops recording and returns a copy of all captured actions.
    /// </summary>
    public List<MacroAction> StopRecording()
    {
        if (!IsRecording) return [.. _recordedActions];

        _hookManager.StopRecording();
        _stopwatch.Stop();

        _hookManager.MouseClicked -= OnMouseClicked;
        _hookManager.KeyPressed -= OnKeyPressed;

        FlushTextBuffer();

        Log?.Invoke($"Recording stopped. {_recordedActions.Count} actions captured in {_stopwatch.Elapsed:mm\\:ss}.");
        return [.. _recordedActions];
    }

    // ═══════════════════════════════════════════════
    //  MOUSE HANDLER
    // ═══════════════════════════════════════════════

    private void OnMouseClicked(int screenX, int screenY, bool isRightClick)
    {
        if (!Win32Api.GetWindowRect(_targetHwnd, out var rect))
            return;

        if (screenX < rect.Left || screenX > rect.Right ||
            screenY < rect.Top || screenY > rect.Bottom)
            return;

        FlushTextBuffer();
        AddWaitIfNeeded();

        var pt = new Win32Api.POINT { X = screenX, Y = screenY };
        Win32Api.ScreenToClient(_targetHwnd, ref pt);

        var click = new ClickAction
        {
            X = pt.X,
            Y = pt.Y,
            IsRightClick = isRightClick,
        };
        _recordedActions.Add(click);

        string btn = isRightClick ? "Right" : "Left";
        Log?.Invoke($"  {btn}Click at ({pt.X}, {pt.Y})");
        ActionRecorded?.Invoke(_recordedActions.Count);
    }

    // ═══════════════════════════════════════════════
    //  KEYBOARD HANDLER
    // ═══════════════════════════════════════════════

    private void OnKeyPressed(uint vkCode, char ch)
    {
        if (vkCode == VK_F10)
        {
            StopKeyPressed?.Invoke();
            return;
        }

        if (ch == '\0' || char.IsControl(ch))
        {
            FlushTextBuffer();
            return;
        }

        if (_textBuffer.Length == 0)
            AddWaitIfNeeded();

        _textBuffer.Append(ch);
    }

    // ═══════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════

    private void FlushTextBuffer()
    {
        if (_textBuffer.Length == 0) return;

        var type = new TypeAction { Text = _textBuffer.ToString() };
        _recordedActions.Add(type);

        Log?.Invoke($"  TypeText \"{type.Text}\"");
        ActionRecorded?.Invoke(_recordedActions.Count);
        _textBuffer.Clear();
    }

    private void AddWaitIfNeeded()
    {
        long now = _stopwatch.ElapsedMilliseconds;
        long delay = now - _lastActionMs;
        _lastActionMs = now;

        if (delay > MIN_WAIT_MS && _recordedActions.Count > 0)
        {
            int d = (int)delay;
            var wait = new WaitAction { DelayMin = d, DelayMax = d, Milliseconds = d };
            _recordedActions.Add(wait);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (IsRecording) StopRecording();
        _hookManager.Dispose();
        _disposed = true;
    }
}

using System.Diagnostics;
using System.Text;
using System.Windows.Input;
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

    // Modifiers-only VK codes — captured but not recorded as standalone actions
    private static readonly uint[] MODIFIER_KEYS =
    [
        0x10, 0x11, 0x12,       // Shift, Ctrl, Alt
        0xA0, 0xA1,             // Left/Right Shift
        0xA2, 0xA3,             // Left/Right Ctrl
        0xA4, 0xA5,             // Left/Right Alt
        0x5B, 0x5C,             // Left/Right Win
    ];

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
        _hookManager.KeyPressedFull += OnKeyPressedFull;

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
        _hookManager.KeyPressedFull -= OnKeyPressedFull;

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
    //  KEYBOARD HANDLER — printable chars (TypeAction)
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
    //  KEYBOARD HANDLER — non-printable keys (KeyPressAction)
    // ═══════════════════════════════════════════════

    private void OnKeyPressedFull(uint vkCode, uint scanCode, bool shift, bool ctrl, bool alt)
    {
        if (vkCode == VK_F10)
        {
            StopKeyPressed?.Invoke();
            return;
        }

        // Skip pure modifier key presses (they're tracked for combo building only)
        if (MODIFIER_KEYS.Contains(vkCode))
            return;

        // Only printable chars go into the text buffer; everything else → KeyPressAction
        FlushTextBuffer();

        var key = KeyInterop.KeyFromVirtualKey((int)vkCode);
        string keyName = key.ToString();

        // Build modifier-prefixed display name
        if (ctrl && shift) keyName = $"Ctrl+Shift+{keyName}";
        else if (ctrl)     keyName = $"Ctrl+{keyName}";
        else if (shift)    keyName = $"Shift+{keyName}";
        else if (alt)      keyName = $"Alt+{keyName}";

        var kpa = new KeyPressAction
        {
            VirtualKeyCode = (int)vkCode,
            ScanCode       = (int)scanCode,
            KeyName        = keyName,
            Modifiers      = new KeyModifiers { Shift = shift, Ctrl = ctrl, Alt = alt },
            HoldDurationMs = 50,
        };
        _recordedActions.Add(kpa);
        Log?.Invoke($"  KeyPress [{keyName}] VK=0x{vkCode:X2} SC=0x{scanCode:X2}");
        ActionRecorded?.Invoke(_recordedActions.Count);
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

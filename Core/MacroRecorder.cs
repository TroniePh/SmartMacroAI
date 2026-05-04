using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;
using SmartMacroAI;
using SmartMacroAI.Localization;
using SmartMacroAI.Models;

namespace SmartMacroAI.Core;

/// <summary>
/// Records user mouse clicks and keystrokes into a list of <see cref="MacroAction"/>
/// objects using <see cref="GlobalHookManager"/>.  Clicks are filtered to the target
/// window and coordinates are converted to client-relative via ScreenToClient.
/// Consecutive keystrokes are batched into a single <see cref="TypeAction"/>.
/// Supports Unikey Vietnamese input by buffering keystrokes and applying backspaces
/// (from Unikey corrections) before flushing.
/// </summary>
public sealed class MacroRecorder : IDisposable
{
    private const uint VK_F10 = 0x79;
    private const int MIN_WAIT_MS = 100;

    // Keys that should never be recorded as standalone actions
    private static readonly HashSet<uint> SKIP_KEYS = new()
    {
        0x10, 0x11, 0x12,       // Shift, Ctrl, Alt
        0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, // L/R modifiers
        0x5B, 0x5C,             // Left/Right Win
        0xE7, 0xE8, 0xE9, 0xEA, 0xEB,        // Unikey virtual keys
        0x15, 0x19, 0x1C, 0x1F, 0xE5,        // IME keys
        0xF0, 0xF1, 0xF2, 0xF3, 0xF4,        // Unikey internal
        0xFF                                  // Unknown/undefined
    };

    private readonly GlobalHookManager _hookManager = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly List<MacroAction> _recordedActions = [];
    private readonly object _bufferLock = new();
    private string _textBuffer = "";
    private System.Threading.Timer? _flushTimer;

    private IntPtr _targetHwnd;
    private long _lastActionMs;
    private bool _disposed;
    private uint _lastBufferedVkCode;
    private volatile bool _isPaused = false;

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
        lock (_bufferLock) { _textBuffer = ""; }
        _lastActionMs = 0;
        _lastBufferedVkCode = 0;

        _hookManager.MouseClicked += OnMouseClicked;
        _hookManager.KeyPressed += OnKeyPressed;
        _hookManager.KeyPressedFull += OnKeyPressedFull;
        _hookManager.SetSpecialKeyCallbacks(QueueChar, HandleBackspace, OnHotkey);

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

        _flushTimer?.Dispose();
        _flushTimer = null;

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

    private void OnMouseClicked(int screenX, int screenY, MouseButton button)
    {
        if (_isPaused) return;
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
            Button = button,
        };
        _recordedActions.Add(click);

        Log?.Invoke($"  {button}Click at ({pt.X}, {pt.Y})");
        ActionRecorded?.Invoke(_recordedActions.Count);
    }

    // ═══════════════════════════════════════════════
    //  KEYBOARD HANDLER — printable chars (TypeAction)
    // ═══════════════════════════════════════════════

    private void OnKeyPressed(uint vkCode, char ch)
    {
        if (_isPaused) return;
        if (vkCode == VK_F10)
        {
            StopKeyPressed?.Invoke();
            return;
        }

        // Backspace: routed to HandleBackspace()
        if (vkCode == 0x08)
        {
            HandleBackspace();
            return;
        }

        // Treat control chars (including carriage return from Enter) as non-printable —
        // they trigger their own KeyPressAction and must flush the buffer first.
        if (ch == '\0' || ch == '\r' || ch == '\n' || char.IsControl(ch))
        {
            FlushTextBuffer();
            return;
        }

        QueueChar(ch);
    }

    public void QueueChar(char c)
    {
        if (_isPaused) return;
        lock (_bufferLock)
        {
            if (_textBuffer.Length == 0)
                AddWaitIfNeeded();

            _textBuffer += c;
            _lastBufferedVkCode = (uint)c;
            ResetFlushTimer();
        }
        Log?.Invoke($"  [Buffer+] '{c}' ({((int)c):X4}). Current: \"{_textBuffer}\"");
    }

    public void HandleBackspace()
    {
        if (_isPaused) return;
        lock (_bufferLock)
        {
            if (_textBuffer.Length > 0)
            {
                // Unikey is deleting the base char to replace it. Do NOT record this backspace.
                _textBuffer = _textBuffer[..^1];
                ResetFlushTimer();
                return;
            }
        }

        // If buffer is empty, this is a real user backspace
        FlushTextBuffer();
        _recordedActions.Add(new KeyPressAction
        {
            VirtualKeyCode = 0x08,
            ScanCode = 0,
            KeyName = "Back",
            Modifiers = new KeyModifiers(),
            HoldDurationMs = 50
        });
        Log?.Invoke($"  KeyPress [Back] VK=0x08");
        ActionRecorded?.Invoke(_recordedActions.Count);
    }

    private void ResetFlushTimer()
    {
        _flushTimer?.Dispose();
        _flushTimer = new System.Threading.Timer(_ =>
        {
            try { System.Windows.Application.Current?.Dispatcher.InvokeAsync(FlushTextBuffer); }
            catch { }
        }, null, 400, Timeout.Infinite);
    }

    public void ClearTextBuffer()
    {
        lock (_bufferLock)
        {
            _textBuffer = "";
            _flushTimer?.Dispose();
        }
    }

    private void OnHotkey()
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            ClearTextBuffer();
            _isPaused = true;

            try
            {
                var dialog = new InputDialog(LanguageManager.GetString("ui_Recorder_ManualInput"), LanguageManager.GetString("ui_Recorder_ManualInputPrompt"));
                dialog.Owner = System.Windows.Application.Current.MainWindow;
                dialog.Topmost = true;

                if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.InputText))
                {
                    _recordedActions.Add(new TypeAction { Text = dialog.InputText });
                    Log?.Invoke(string.Format(LanguageManager.GetString("ui_Recorder_ManualInputLog"), dialog.InputText));
                    ActionRecorded?.Invoke(_recordedActions.Count);
                }
            }
            finally
            {
                _isPaused = false;
            }
        });
    }

    // ═══════════════════════════════════════════════
    //  KEYBOARD HANDLER — non-printable keys (KeyPressAction)
    // ═══════════════════════════════════════════════

    private void OnKeyPressedFull(uint vkCode, uint scanCode, bool shift, bool ctrl, bool alt)
    {
        if (_isPaused) return;
        if (vkCode == VK_F10)
        {
            StopKeyPressed?.Invoke();
            return;
        }

        // Skip Unikey internal keys, modifiers, and IME keys
        if (SKIP_KEYS.Contains(vkCode))
            return;

        // Skip VK_BACK — handled by OnBackspace()
        if (vkCode == 0x08)
            return;

        // Skip printable chars — handled by OnKeyPressed text buffer
        char? printable = TryGetPrintableChar(vkCode, shift, ctrl, alt);
        if (printable.HasValue && !ctrl && !alt)
            return;

        // Skip if this VK code was already buffered as a printable char in OnKeyPressed.
        // OnKeyPressed and OnKeyPressedFull both fire for every keydown.
        // Without this guard, a printable key would create both a TypeAction (from the
        // buffer) AND a KeyPressAction, doubling every character.
        if (_lastBufferedVkCode == vkCode)
        {
            _lastBufferedVkCode = 0;
            return;
        }
        _lastBufferedVkCode = 0;

        // Only non-printable keys reach here → KeyPressAction
        FlushTextBuffer();
        AddWaitIfNeeded();

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

    /// <summary>
    /// Returns the printable character for a virtual-key code, or null if the key
    /// has no printable representation (arrows, function keys, etc.).
    /// Uses ToUnicode to translate through the current keyboard layout.
    /// </summary>
    private static char? TryGetPrintableChar(uint vkCode, bool shift, bool ctrl, bool alt)
    {
        if (ctrl || alt) return null; // Ctrl+A, Alt+F4, etc. are not printable

        // Get keyboard state
        var keyState = new byte[256];
        if (!GetKeyboardState(keyState))
            return null;

        // Set shift state
        if (shift)
            keyState[0x10] = 0x80;
        else
            keyState[0x10] = 0;

        uint scanCode = MapVirtualKey(vkCode, 0);
        var sb = new System.Text.StringBuilder(4);
        int result = ToUnicode(vkCode, scanCode, keyState, sb, sb.Capacity, 0);

        if (result > 0)
        {
            char ch = sb[0];
            if (!char.IsControl(ch))
                return ch;
        }
        return null;
    }

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern int ToUnicode(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
        [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszBuff, int cchBuff, uint wFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardState(byte[] lpKeyState);

    // ═══════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════

    private void FlushTextBuffer()
    {
        string toFlush;
        lock (_bufferLock)
        {
            if (string.IsNullOrEmpty(_textBuffer)) return;
            toFlush = _textBuffer;
            _textBuffer = "";
        }

        _flushTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _flushTimer?.Dispose();
        _flushTimer = null;

        var type = new TypeAction { Text = toFlush };
        _recordedActions.Add(type);
        Log?.Invoke($"  [Ghi nhận gõ chữ] \"{toFlush}\"");
        ActionRecorded?.Invoke(_recordedActions.Count);
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

using System.Runtime.InteropServices;
using System.Text;

namespace SmartMacroAI.Core;

/// <summary>
/// Manages low-level Windows global hooks (WH_MOUSE_LL, WH_KEYBOARD_LL)
/// to capture mouse clicks and keystrokes system-wide.
/// Must be installed from a thread with a message pump (WPF UI thread).
/// </summary>
public sealed class GlobalHookManager : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;

    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    // ═══════════════════════════════════════════════
    //  P/INVOKE
    // ═══════════════════════════════════════════════

    private delegate IntPtr LowLevelHookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelHookProc lpfn,
        IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern int ToUnicode(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff, int cchBuff, uint wFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardState(byte[] lpKeyState);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_CONTROL = 0x11;

    // ═══════════════════════════════════════════════
    //  HOOK STRUCTURES
    // ═══════════════════════════════════════════════

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public Win32Api.POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // ═══════════════════════════════════════════════
    //  EVENTS
    // ═══════════════════════════════════════════════

    /// <summary>
    /// Fires on every left/right/middle mouse click. Coordinates are screen-absolute.
    /// </summary>
    public event Action<int, int, MouseButton>? MouseClicked;

    /// <summary>
    /// Fires on every key-down. Provides both the virtual-key code and the
    /// translated Unicode character ('\0' if non-printable).
    /// For Unikey VK_PACKET (0xE7), the character is extracted from the scanCode field.
    /// </summary>
    public event Action<uint, char>? KeyPressed;

    /// <summary>
    /// Fires on every key-down with full details: virtual-key code, scan code,
    /// and current modifier-key state. Use this for KeyPressAction recording.
    /// NOTE: For VK_PACKET (0xE7) and VK_BACK (0x08), this event is NOT fired;
    /// instead the special char/backspace callbacks are used to prevent double-fire.
    /// </summary>
    public event Action<uint, uint, bool, bool, bool>? KeyPressedFull;

    private Action<char>? _queueCharCallback;
    private Action? _handleBackspaceCallback;
    private Action? _hotkeyCallback;

    // ═══════════════════════════════════════════════
    //  STATE
    // ═══════════════════════════════════════════════

    public bool IsRecording { get; private set; }
    public bool IsPaused { get; set; }

    private IntPtr _mouseHookHandle;
    private IntPtr _keyboardHookHandle;

    private LowLevelHookProc? _mouseProc;
    private LowLevelHookProc? _keyboardProc;

    private bool _disposed;

    /// <summary>
    /// Registers callbacks for special keys (VK_PACKET char, VK_BACK, and hotkeys) to avoid
    /// double-firing through the event system. Set before StartRecording().
    /// </summary>
    public void SetSpecialKeyCallbacks(Action<char>? queueChar, Action? handleBackspace, Action? hotkeyCallback)
    {
        _queueCharCallback = queueChar;
        _handleBackspaceCallback = handleBackspace;
        _hotkeyCallback = hotkeyCallback;
    }

    // ═══════════════════════════════════════════════
    //  START / STOP
    // ═══════════════════════════════════════════════

    public void StartRecording()
    {
        if (IsRecording) return;

        _mouseProc = MouseHookCallback;
        _keyboardProc = KeyboardHookCallback;

        IntPtr hMod = GetModuleHandle(null);

        _mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hMod, 0);
        if (_mouseHookHandle == IntPtr.Zero)
            throw new InvalidOperationException(
                $"Failed to install mouse hook (error {Marshal.GetLastWin32Error()}).");

        _keyboardHookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, hMod, 0);
        if (_keyboardHookHandle == IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
            throw new InvalidOperationException(
                $"Failed to install keyboard hook (error {Marshal.GetLastWin32Error()}).");
        }

        IsRecording = true;
    }

    public void StopRecording()
    {
        if (!IsRecording) return;

        if (_mouseHookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
        }

        if (_keyboardHookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = IntPtr.Zero;
        }

        _mouseProc = null;
        _keyboardProc = null;
        _queueCharCallback = null;
        _handleBackspaceCallback = null;
        IsRecording = false;
    }

    // ═══════════════════════════════════════════════
    //  HOOK CALLBACKS
    // ═══════════════════════════════════════════════

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsPaused)
            return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);

        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                var btn = msg switch
                {
                    WM_RBUTTONDOWN => MouseButton.Right,
                    WM_MBUTTONDOWN => MouseButton.Middle,
                    _ => MouseButton.Left,
                };
                MouseClicked?.Invoke(data.pt.X, data.pt.Y, btn);
            }
        }
        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // If paused (dialog is open), let keys pass through without recording
        if (nCode >= 0 && IsPaused)
            return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);

        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

                // Skip injected keys EXCEPT Unikey's VK_PACKET (0xE7) and injected Backspace (0x08)
                bool isInjected = (data.flags & 0x10) != 0;
                if (isInjected && data.vkCode != 0xE7 && data.vkCode != 0x08)
                    return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);

                // Check for Ctrl+T hotkey — use GetAsyncKeyState for physical modifier state
                bool isKeyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                if (isKeyDown && data.vkCode == 0x54 && (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0)
                {
                    _hotkeyCallback?.Invoke();
                    return (IntPtr)1; // Block the key from reaching the target app
                }

                // Handle VK_PACKET — Unikey sends composed Unicode via KEYEVENTF_UNICODE.
                // The actual Unicode character is stored in the scanCode field.
                if (data.vkCode == 0xE7)
                {
                    char unikeyChar = (char)data.scanCode;
                    if (unikeyChar > 31)
                        _queueCharCallback?.Invoke(unikeyChar);
                    return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
                }

                // Handle Backspace — both physical and Unikey-injected
                if (data.vkCode == 0x08)
                {
                    _handleBackspaceCallback?.Invoke();
                    return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
                }

                char ch = VkCodeToChar(data.vkCode, data.scanCode);
                KeyPressed?.Invoke(data.vkCode, ch);

                bool shift = (GetKeyState(0x10) & 0x8000) != 0;
                bool ctrl  = (GetKeyState(0x11) & 0x8000) != 0;
                bool alt   = (GetKeyState(0x12) & 0x8000) != 0;
                KeyPressedFull?.Invoke(data.vkCode, data.scanCode, shift, ctrl, alt);
            }
        }
        return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    // ═══════════════════════════════════════════════
    //  VIRTUAL-KEY → CHARACTER
    // ═══════════════════════════════════════════════

    private static char VkCodeToChar(uint vkCode, uint scanCode)
    {
        var keyState = new byte[256];
        GetKeyboardState(keyState);

        var sb = new StringBuilder(4);
        int result = ToUnicode(vkCode, scanCode, keyState, sb, sb.Capacity, 0);
        return result > 0 ? sb[0] : '\0';
    }

    // ═══════════════════════════════════════════════
    //  CLEANUP
    // ═══════════════════════════════════════════════

    public void Dispose()
    {
        if (_disposed) return;
        StopRecording();
        _disposed = true;
    }
}

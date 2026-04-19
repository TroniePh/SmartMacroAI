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
    /// Fires on every left/right mouse click. Coordinates are screen-absolute.
    /// </summary>
    public event Action<int, int, bool>? MouseClicked;

    /// <summary>
    /// Fires on every key-down. Provides both the virtual-key code and the
    /// translated Unicode character ('\0' if non-printable).
    /// </summary>
    public event Action<uint, char>? KeyPressed;

    /// <summary>
    /// Fires on every key-down with full details: virtual-key code, scan code,
    /// and current modifier-key state. Use this for KeyPressAction recording.
    /// </summary>
    public event Action<uint, uint, bool, bool, bool>? KeyPressedFull;

    // ═══════════════════════════════════════════════
    //  STATE
    // ═══════════════════════════════════════════════

    public bool IsRecording { get; private set; }

    private IntPtr _mouseHookHandle;
    private IntPtr _keyboardHookHandle;

    private LowLevelHookProc? _mouseProc;
    private LowLevelHookProc? _keyboardProc;

    private bool _disposed;

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
        IsRecording = false;
    }

    // ═══════════════════════════════════════════════
    //  HOOK CALLBACKS
    // ═══════════════════════════════════════════════

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                MouseClicked?.Invoke(data.pt.X, data.pt.Y, msg == WM_RBUTTONDOWN);
            }
        }
        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
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

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SmartMacroAI.Core;

/// <summary>
/// Central Win32 interop layer for SmartMacroAI.
/// Every automation call goes through here — all methods target window handles
/// directly so the physical mouse and keyboard are NEVER hijacked.
///
/// Created by Phạm Duy - Giải pháp tự động hóa thông minh.
/// </summary>
public static class Win32Api
{
    // ═══════════════════════════════════════════════
    //  CONSTANTS — Window Messages
    // ═══════════════════════════════════════════════

    public const uint WM_LBUTTONDOWN   = 0x0201;
    public const uint WM_LBUTTONUP     = 0x0202;
    public const uint WM_RBUTTONDOWN   = 0x0204;
    public const uint WM_RBUTTONUP     = 0x0205;
    public const uint WM_MBUTTONDOWN   = 0x0207;
    public const uint WM_MBUTTONUP     = 0x0208;
    public const uint WM_KEYDOWN       = 0x0100;
    public const uint WM_KEYUP         = 0x0101;
    public const uint WM_CHAR          = 0x0102;
    public const uint WM_PASTE         = 0x0302;
    public const uint WM_SETTEXT       = 0x000C;
    public const uint WM_GETTEXT       = 0x000D;
    public const uint WM_GETTEXTLENGTH = 0x000E;
    public const uint WM_MOUSEMOVE     = 0x0200;
    public const uint WM_CLOSE         = 0x0010;
    public const uint WM_ACTIVATE      = 0x0006;
    public const uint WA_ACTIVE        = 1;

    public const uint MK_LBUTTON = 0x0001;
    public const uint MK_RBUTTON = 0x0002;
    public const uint MK_MBUTTON = 0x0010;

    // ═══════════════════════════════════════════════
    //  CONSTANTS — ShowWindow / Hotkey
    // ═══════════════════════════════════════════════

    public const int SW_HIDE      = 0;
    public const int SW_MAXIMIZE  = 3;
    public const int SW_SHOW      = 5;
    /// <summary>Restore / normal placement (e.g. after temporary maximize for snip).</summary>
    public const int SW_RESTORE   = 9;
    public const int WM_HOTKEY  = 0x0312;

    // ═══════════════════════════════════════════════
    //  CONSTANTS — PrintWindow / GDI
    // ═══════════════════════════════════════════════

    public const uint PW_CLIENTONLY         = 0x00000001;
    /// <summary>Forces composition of layered / GPU-accelerated content (Windows 8.1+).</summary>
    public const uint PW_RENDERFULLCONTENT  = 0x00000002;
    public const uint SRCCOPY               = 0x00CC0020;

    // ═══════════════════════════════════════════════
    //  P/INVOKE — Message Dispatch
    // ═══════════════════════════════════════════════

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // ═══════════════════════════════════════════════
    //  P/INVOKE — Window Discovery
    // ═══════════════════════════════════════════════

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter,
                                              string? lpszClass, string? lpszWindow);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    // ═══════════════════════════════════════════════
    //  P/INVOKE — Window Geometry
    // ═══════════════════════════════════════════════

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    // ═══════════════════════════════════════════════
    //  P/INVOKE — Background Window Capture
    // ═══════════════════════════════════════════════

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, int nFlags);

    // ═══════════════════════════════════════════════
    //  P/INVOKE — GDI (for bitmap operations)
    // ═══════════════════════════════════════════════

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, uint rop);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteDC(IntPtr hdc);

    // ═══════════════════════════════════════════════
    //  P/INVOKE — Process Info
    // ═══════════════════════════════════════════════

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // ═══════════════════════════════════════════════
    //  P/INVOKE — Coordinate Conversion
    // ═══════════════════════════════════════════════

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    // ═══════════════════════════════════════════════
    //  P/INVOKE — Window Visibility & Hotkeys
    // ═══════════════════════════════════════════════

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ═══════════════════════════════════════════════
    //  STRUCTURES
    // ═══════════════════════════════════════════════

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    public const uint FLASHW_ALL      = 0x00000003;
    public const uint FLASHW_TIMERNOFG = 0x0000000C;

    // ═══════════════════════════════════════════════
    //  HELPER — Coordinate Packing
    // ═══════════════════════════════════════════════

    public static IntPtr MakeLParam(int x, int y)
        => (IntPtr)(((y & 0xFFFF) << 16) | (x & 0xFFFF));

    /// <summary>Maps client coordinates to screen pixels for the given window.</summary>
    public static Point ClientPointToScreen(IntPtr hWnd, int clientX, int clientY)
    {
        var pt = new POINT { X = clientX, Y = clientY };
        if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
            return new Point(clientX, clientY);
        ClientToScreen(hWnd, ref pt);
        return new Point(pt.X, pt.Y);
    }

    // ═══════════════════════════════════════════════
    //  STEALTH CLICK — Humanized async sequence that
    //  sends MOUSEMOVE → BUTTONDOWN → delay → BUTTONUP
    //  to defeat apps that reject instant PostMessage clicks.
    // ═══════════════════════════════════════════════

    /// <summary>
    /// PostMessage-based left click with WM_ACTIVATE trick for DirectX games.
    /// Sends WM_MOUSEMOVE first to establish position, then a staggered click sequence.
    /// </summary>
    public static async Task ControlClickAsync(IntPtr hWnd, int x, int y, int moveDelayMs = 10)
    {
        PostMessage(hWnd, WM_ACTIVATE, (IntPtr)WA_ACTIVE, IntPtr.Zero);
        await Task.Delay(10);

        IntPtr lParam = MakeLParam(x, y);
        PostMessage(hWnd, WM_MOUSEMOVE, IntPtr.Zero, lParam);
        await Task.Delay(moveDelayMs);
        PostMessage(hWnd, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, lParam);
        await Task.Delay(Random.Shared.Next(20, 50));
        PostMessage(hWnd, WM_LBUTTONUP, IntPtr.Zero, lParam);
    }

    /// <summary>
    /// Stealth-aware click: WM_ACTIVATE trick for DirectX games, then
    /// WM_MOUSEMOVE → staggered DOWN → HOLD → UP sequence.
    /// </summary>
    public static async Task StealthClickAsync(IntPtr hWnd, int x, int y)
    {
        PostMessage(hWnd, WM_ACTIVATE, (IntPtr)WA_ACTIVE, IntPtr.Zero);
        await Task.Delay(10);

        IntPtr lParam = MakeLParam(x, y);
        PostMessage(hWnd, WM_MOUSEMOVE, IntPtr.Zero, lParam);
        await Task.Delay(5);
        PostMessage(hWnd, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, lParam);
        await Task.Delay(20);
        PostMessage(hWnd, WM_LBUTTONUP, IntPtr.Zero, lParam);
    }

    /// <summary>
    /// PostMessage-based left click at a client point from template matching, with small
    /// random offset and ClientToScreen/ScreenToClient round-trip for DPI-aware hosts
    /// (emulators, per-monitor DPI). Prefer this over <see cref="ControlClickAsync"/> for
    /// image-found coordinates only.
    /// </summary>
    public static async Task StealthClickOnFoundImage(
        IntPtr hwnd,
        Point bitmapPoint,
        int randomOffsetRange = 3,
        CancellationToken cancellationToken = default)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            return;

        var rng = new Random();
        int offsetX = rng.Next(-randomOffsetRange, randomOffsetRange + 1);
        int offsetY = rng.Next(-randomOffsetRange, randomOffsetRange + 1);

        int clientX = bitmapPoint.X + offsetX;
        int clientY = bitmapPoint.Y + offsetY;

        var pt = new POINT { X = clientX, Y = clientY };
        ClientToScreen(hwnd, ref pt);
        ScreenToClient(hwnd, ref pt);

        IntPtr lParam = MakeLParam(pt.X, pt.Y);
        IntPtr wParam = (IntPtr)MK_LBUTTON;

        PostMessage(hwnd, WM_MOUSEMOVE, IntPtr.Zero, lParam);
        await Task.Delay(rng.Next(20, 60), cancellationToken);

        PostMessage(hwnd, WM_LBUTTONDOWN, wParam, lParam);
        await Task.Delay(rng.Next(30, 90), cancellationToken);

        PostMessage(hwnd, WM_LBUTTONUP, IntPtr.Zero, lParam);

        Debug.WriteLine(
            $"[StealthClick] HWND=0x{hwnd:X} FinalClient=({pt.X},{pt.Y}) Offset=({offsetX},{offsetY})");
    }

    public static async Task ControlRightClickAsync(IntPtr hWnd, int x, int y)
    {
        IntPtr lParam = MakeLParam(x, y);
        PostMessage(hWnd, WM_MOUSEMOVE, IntPtr.Zero, lParam);
        await Task.Delay(10);
        PostMessage(hWnd, WM_RBUTTONDOWN, (IntPtr)MK_RBUTTON, lParam);
        await Task.Delay(Random.Shared.Next(20, 50));
        PostMessage(hWnd, WM_RBUTTONUP, IntPtr.Zero, lParam);
    }

    // ═══════════════════════════════════════════════
    //  STEALTH KEYBOARD — PostMessage-based
    // ═══════════════════════════════════════════════

    public static void ControlSendText(IntPtr hWnd, string text)
    {
        foreach (char c in text)
            PostMessage(hWnd, WM_CHAR, (IntPtr)c, IntPtr.Zero);
    }

    public static void ControlSendKey(IntPtr hWnd, int virtualKeyCode)
    {
        PostMessage(hWnd, WM_ACTIVATE, (IntPtr)WA_ACTIVE, IntPtr.Zero);
        uint scan = MapVirtualKeyA((uint)virtualKeyCode, 0);
        IntPtr lpDn = (IntPtr)(1 | (scan << 16));
        IntPtr lpUp = (IntPtr)(1 | (scan << 16) | (1 << 30) | unchecked((int)(1u << 31)));
        PostMessage(hWnd, WM_KEYDOWN, (IntPtr)virtualKeyCode, lpDn);
        PostMessage(hWnd, WM_KEYUP, (IntPtr)virtualKeyCode, lpUp);
    }

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyA(uint uCode, uint uMapType);

    /// <summary>
    /// Shows or hides a window for stealth automation.
    /// For Chromium/Electron windows: moves off-screen + sets WS_EX_TOOLWINDOW to hide
    /// from taskbar and Alt+Tab, while keeping the window "alive" for PostMessage.
    /// For other windows: uses SW_HIDE (fully invisible, PostMessage still works).
    /// Toggle show restores original position and brings window to foreground.
    /// </summary>
    public static void SetWindowVisibility(IntPtr hwnd, bool visible)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            return;

        if (visible)
        {
            // Check if we used off-screen approach (Chromium)
            if (GetProp(hwnd, "SmartMacro_OrigX") != IntPtr.Zero)
            {
                int origX = (int)GetProp(hwnd, "SmartMacro_OrigX") - 1;
                int origY = (int)GetProp(hwnd, "SmartMacro_OrigY") - 1;
                int origW = (int)GetProp(hwnd, "SmartMacro_OrigW");
                int origH = (int)GetProp(hwnd, "SmartMacro_OrigH");
                long origExStyle = (long)GetProp(hwnd, "SmartMacro_OrigExStyle");

                // Restore original extended style (taskbar visibility)
                SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)origExStyle);

                // Move back to original position
                SetWindowPos(hwnd, IntPtr.Zero, origX, origY, origW, origH,
                    SWP_NOZORDER | SWP_FRAMECHANGED);
                ShowWindow(hwnd, SW_SHOW);
                SetForegroundWindow(hwnd);

                RemoveProp(hwnd, "SmartMacro_OrigX");
                RemoveProp(hwnd, "SmartMacro_OrigY");
                RemoveProp(hwnd, "SmartMacro_OrigW");
                RemoveProp(hwnd, "SmartMacro_OrigH");
                RemoveProp(hwnd, "SmartMacro_OrigExStyle");
            }
            else
            {
                // Was hidden with SW_HIDE
                ShowWindow(hwnd, SW_SHOW);
                SetForegroundWindow(hwnd);
            }
        }
        else
        {
            // Detect Chromium/Electron windows
            string className = GetWindowClassName(hwnd);
            bool isChromium = className.Contains("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase) ||
                              className.Contains("CefBrowserWindow", StringComparison.OrdinalIgnoreCase) ||
                              className.Contains("MozillaWindowClass", StringComparison.OrdinalIgnoreCase);

            if (isChromium)
            {
                if (GetWindowRect(hwnd, out RECT rect))
                {
                    int w = rect.Right - rect.Left;
                    int h = rect.Bottom - rect.Top;
                    long currentExStyle = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);

                    // Save original state
                    SetProp(hwnd, "SmartMacro_OrigX", (IntPtr)(rect.Left + 1));
                    SetProp(hwnd, "SmartMacro_OrigY", (IntPtr)(rect.Top + 1));
                    SetProp(hwnd, "SmartMacro_OrigW", (IntPtr)w);
                    SetProp(hwnd, "SmartMacro_OrigH", (IntPtr)h);
                    SetProp(hwnd, "SmartMacro_OrigExStyle", (IntPtr)currentExStyle);

                    // Hide from taskbar + Alt+Tab: set WS_EX_TOOLWINDOW, remove WS_EX_APPWINDOW
                    long newExStyle = (currentExStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
                    SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)newExStyle);

                    // Move off-screen — window stays "visible" so Chromium keeps processing messages
                    SetWindowPos(hwnd, IntPtr.Zero, -32000, -32000, w, h,
                        SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
                }
            }
            else
            {
                // Standard Win32 apps: SW_HIDE is fully stealth and PostMessage works
                ShowWindow(hwnd, SW_HIDE);
            }
        }
    }

    // ═══════════════════════════════════════════════
    //  P/INVOKE — Window Positioning & Style (for stealth)
    // ═══════════════════════════════════════════════

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_APPWINDOW = 0x00040000L;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetProp(IntPtr hWnd, string lpString, IntPtr hData);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr GetProp(IntPtr hWnd, string lpString);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr RemoveProp(IntPtr hWnd, string lpString);

    /// <summary>
    /// Brings the window to the foreground and flashes it so the user
    /// can visually identify which window handle they selected.
    /// </summary>
    public static void IdentifyWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            return;

        if (IsIconic(hwnd))
            ShowWindow(hwnd, SW_RESTORE);

        SetForegroundWindow(hwnd);

        var fi = new FLASHWINFO
        {
            cbSize   = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd     = hwnd,
            dwFlags  = FLASHW_ALL | FLASHW_TIMERNOFG,
            uCount   = 5,
            dwTimeout = 0,
        };
        FlashWindowEx(ref fi);
    }

    // ═══════════════════════════════════════════════
    //  CHILD-WINDOW RESOLUTION (text fields)
    //  ── Notepad, WinForms, etc. host the real edit
    //     control in a child window (class "Edit").
    // ═══════════════════════════════════════════════

    private static readonly HashSet<string> TextInputClassNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Edit", "RichEdit20W", "RichEdit20A", "RichEditD2DPT",
            "RICHEDIT50W", "TextBox", "WindowsForms10.EDIT.app",
            "Scintilla", "ATL:Edit",
        };

    /// <summary>
    /// Finds the first child window whose class name indicates a text-input
    /// control (Edit, RichEdit, etc.).  Returns <paramref name="parentHwnd"/>
    /// unchanged when no text-input child exists (single-window apps, games).
    /// </summary>
    public static IntPtr FindInputChild(IntPtr parentHwnd)
    {
        if (parentHwnd == IntPtr.Zero || !IsWindow(parentHwnd))
            return parentHwnd;

        IntPtr found = IntPtr.Zero;

        EnumChildWindows(parentHwnd, (child, _) =>
        {
            string className = GetWindowClassName(child);
            if (TextInputClassNames.Contains(className))
            {
                found = child;
                return false;
            }

            // Partial match for runtime-suffixed WinForms class names
            if (className.StartsWith("WindowsForms10.EDIT", StringComparison.OrdinalIgnoreCase)
             || className.StartsWith("WindowsForms10.RichEdit", StringComparison.OrdinalIgnoreCase))
            {
                found = child;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return found != IntPtr.Zero ? found : parentHwnd;
    }

    // ═══════════════════════════════════════════════
    //  DPI-SAFE COORDINATE HELPERS
    // ═══════════════════════════════════════════════

    /// <summary>
    /// Returns true when (<paramref name="x"/>, <paramref name="y"/>) falls
    /// inside the client rectangle of <paramref name="hWnd"/>.
    /// Useful for guarding against off-target clicks caused by DPI mismatch.
    /// </summary>
    public static bool IsInsideClientArea(IntPtr hWnd, int x, int y)
    {
        if (!GetClientRect(hWnd, out RECT rc)) return false;
        int cw = rc.Right - rc.Left;
        int ch = rc.Bottom - rc.Top;
        return x >= 0 && y >= 0 && x < cw && y < ch;
    }

    // ═══════════════════════════════════════════════
    //  WINDOW INFO HELPERS
    // ═══════════════════════════════════════════════

    public static string GetWindowTitle(IntPtr hWnd)
    {
        int length = GetWindowTextLength(hWnd);
        if (length == 0) return string.Empty;

        var sb = new StringBuilder(length + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetWindowClassName(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>
    /// Represents a window discovered during enumeration.
    /// Includes class name and process name so Electron/Chromium windows
    /// (which may have no title) can still be identified.
    /// </summary>
    public sealed class WindowInfo
    {
        public IntPtr Handle { get; init; }
        public string Title { get; init; } = "";
        public string ClassName { get; init; } = "";
        public string ProcessName { get; init; } = "";
        public int Pid { get; init; }
    }

    /// <summary>
    /// Returns all visible top-level windows including Electron/Chromium windows
    /// that have no title. Used by the UI to populate target-window pickers.
    /// </summary>
    public static List<WindowInfo> GetAllVisibleWindows()
    {
        var result = new List<WindowInfo>();

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;

            var titleSb = new StringBuilder(256);
            GetWindowText(hwnd, titleSb, 256);
            string title = titleSb.ToString();

            var classSb = new StringBuilder(256);
            GetClassName(hwnd, classSb, 256);
            string className = classSb.ToString();

            GetWindowThreadProcessId(hwnd, out uint pid);
            string processName = "";
            try { processName = Process.GetProcessById((int)pid).ProcessName; }
            catch { return true; }

            // Include if: has title OR is a known browser/Electron/process
            bool isKnownBrowser = processName.Contains("discord", StringComparison.OrdinalIgnoreCase) ||
                                  processName.Contains("chrome", StringComparison.OrdinalIgnoreCase) ||
                                  processName.Contains("msedge", StringComparison.OrdinalIgnoreCase) ||
                                  processName.Contains("firefox", StringComparison.OrdinalIgnoreCase) ||
                                  processName.Contains("opera", StringComparison.OrdinalIgnoreCase) ||
                                  processName.Contains("brave", StringComparison.OrdinalIgnoreCase) ||
                                  processName.Contains("electron", StringComparison.OrdinalIgnoreCase) ||
                                  processName.Contains("cef", StringComparison.OrdinalIgnoreCase);

            bool isElectronClass = className.Contains("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase) ||
                                   className.Contains("CefBrowserWindow", StringComparison.OrdinalIgnoreCase) ||
                                   className.Contains("MozillaWindowClass", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(title) || isKnownBrowser || isElectronClass)
            {
                result.Add(new WindowInfo
                {
                    Handle = hwnd,
                    Title = title,
                    ClassName = className,
                    ProcessName = processName,
                    Pid = (int)pid
                });
            }

            return true;
        }, IntPtr.Zero);

        // Sort: named windows first, then by process name
        return result
            .OrderBy(w => string.IsNullOrWhiteSpace(w.Title) ? 1 : 0)
            .ThenBy(w => w.ProcessName)
            .ThenBy(w => w.Title)
            .ToList();
    }

    /// <summary>
    /// Finds the first visible top-level window whose title contains
    /// <paramref name="partialTitle"/> (case-insensitive).
    /// Returns <see cref="IntPtr.Zero"/> if no match.
    /// </summary>
    public static IntPtr FindWindowByPartialTitle(string partialTitle)
    {
        IntPtr found = IntPtr.Zero;

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            string title = GetWindowTitle(hWnd);
            if (title.Contains(partialTitle, StringComparison.OrdinalIgnoreCase))
            {
                found = hWnd;
                return false; // stop enumeration
            }
            return true;
        }, IntPtr.Zero);

        return found;
    }

    // ═══════════════════════════════════════════════
    //  BACKGROUND WINDOW CAPTURE (PrintWindow + BitBlt fallback)
    //  ── Client-area capture; PW_RENDERFULLCONTENT for GPU composition.
    // ═══════════════════════════════════════════════

    /// <summary>Per capture spec: nudge suspended hosts (same numeric value as <c>WM_CREATE</c>).</summary>
    private const uint WM_CAPTURE_SPEC_WAKE = 0x0001;

    /// <summary>
    /// Background client-area capture: <c>PrintWindow</c> with GPU-friendly flags, then
    /// <see cref="CaptureWindowBitBlt"/> if the result looks like a black frame.
    /// </summary>
    public static Bitmap? CaptureHiddenWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            return null;

        SendMessage(hwnd, WM_CAPTURE_SPEC_WAKE, IntPtr.Zero, IntPtr.Zero);

        if (!GetClientRect(hwnd, out RECT rect))
            return null;

        int width  = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        bool iconicOrZero = IsIconic(hwnd) || width <= 0 || height <= 0;
        if (iconicOrZero)
        {
            ShowWindow(hwnd, SW_RESTORE);
            Thread.Sleep(300);
            if (!GetClientRect(hwnd, out rect))
                return null;
            width  = rect.Right - rect.Left;
            height = rect.Bottom - rect.Top;
        }

        if (width <= 0 || height <= 0)
            return null;

        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            IntPtr hdc = g.GetHdc();
            try
            {
                // PW_RENDERFULLCONTENT = 0x00000002
                bool success = PrintWindow(hwnd, hdc, 0x00000002);
                if (!success)
                    PrintWindow(hwnd, hdc, 0x00000003); // PW_CLIENTONLY | PW_RENDERFULLCONTENT
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }

        if (IsBlackBitmap(bmp))
        {
            bmp.Dispose();
            return CaptureWindowBitBlt(hwnd, width, height);
        }

        return bmp;
    }

    /// <summary>BitBlt fallback when <see cref="CaptureHiddenWindow"/> yields a black <c>PrintWindow</c> frame.</summary>
    public static Bitmap CaptureWindowBitBlt(IntPtr hwnd, int width, int height)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        IntPtr hdcDest = g.GetHdc();
        IntPtr hdcSrc  = GetDC(hwnd);
        try
        {
            // SRCCOPY = 0x00CC0020
            BitBlt(hdcDest, 0, 0, width, height, hdcSrc, 0, 0, 0x00CC0020);
        }
        finally
        {
            g.ReleaseHdc(hdcDest);
            ReleaseDC(hwnd, hdcSrc);
        }

        return bmp;
    }

    private static bool IsBlackBitmap(Bitmap bmp)
    {
        var rng = new Random();
        for (int i = 0; i < 10; i++)
        {
            int x = rng.Next(bmp.Width);
            int y = rng.Next(bmp.Height);
            var pixel = bmp.GetPixel(x, y);
            if (pixel.R > 10 || pixel.G > 10 || pixel.B > 10)
                return false;
        }

        return true;
    }
}

using System.Drawing;
using System.Runtime.InteropServices;

namespace SmartMacroAI.Core;

/// <summary>
/// Low-level absolute mouse moves and clicks via single-item <c>SendInput</c> calls
/// (one <see cref="INPUT"/> per step — no batched injection).
/// </summary>
public static class Win32MouseInput
{
    public const uint INPUT_MOUSE = 0;

    public const uint MOUSEEVENTF_MOVE = 0x0001;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetCursorPos(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    /// <summary>Converts screen pixels to normalized absolute coordinates for <c>SendInput</c> (virtual desktop).</summary>
    public static void ScreenToAbsoluteNormalized(int screenX, int screenY, out int absX, out int absY)
    {
        int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (vw <= 1) vw = 1;
        if (vh <= 1) vh = 1;
        absX = (int)((screenX - vx) * 65535.0 / (vw - 1));
        absY = (int)((screenY - vy) * 65535.0 / (vh - 1));
        absX = Math.Clamp(absX, 0, 65535);
        absY = Math.Clamp(absY, 0, 65535);
    }

    /// <summary>Sends exactly one absolute move via <c>SendInput</c>.</summary>
    /// <param name="timeJitterMs">Small offset added to <see cref="MOUSEINPUT.time"/> (0 lets the system stamp).</param>
    public static void SendMouseMoveAbsolute(int screenX, int screenY, int timeJitterMs = 0)
    {
        ScreenToAbsoluteNormalized(screenX, screenY, out int ax, out int ay);
        uint t = timeJitterMs == 0 ? 0 : (uint)Math.Clamp(Environment.TickCount + timeJitterMs, 0, int.MaxValue);
        var mi = new MOUSEINPUT
        {
            dx = ax,
            dy = ay,
            mouseData = 0,
            dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
            time = t,
            dwExtraInfo = UIntPtr.Zero,
        };
        var inp = new INPUT { type = INPUT_MOUSE, mi = mi };
        SendInput(1, [inp], InputSize);
    }

    public static void SendMouseButtonDown(MouseButton button, int timeJitterMs = 0)
    {
        uint flags = button switch
        {
            MouseButton.Left => MOUSEEVENTF_LEFTDOWN,
            MouseButton.Right => MOUSEEVENTF_RIGHTDOWN,
            MouseButton.Middle => MOUSEEVENTF_MIDDLEDOWN,
            _ => MOUSEEVENTF_LEFTDOWN,
        };
        uint t = timeJitterMs == 0 ? 0 : (uint)Math.Clamp(Environment.TickCount + timeJitterMs, 0, int.MaxValue);
        var mi = new MOUSEINPUT
        {
            dx = 0,
            dy = 0,
            mouseData = 0,
            dwFlags = flags,
            time = t,
            dwExtraInfo = UIntPtr.Zero,
        };
        var inp = new INPUT { type = INPUT_MOUSE, mi = mi };
        SendInput(1, [inp], InputSize);
    }

    public static void SendMouseButtonUp(MouseButton button, int timeJitterMs = 0)
    {
        uint flags = button switch
        {
            MouseButton.Left => MOUSEEVENTF_LEFTUP,
            MouseButton.Right => MOUSEEVENTF_RIGHTUP,
            MouseButton.Middle => MOUSEEVENTF_MIDDLEUP,
            _ => MOUSEEVENTF_LEFTUP,
        };
        uint t = timeJitterMs == 0 ? 0 : (uint)Math.Clamp(Environment.TickCount + timeJitterMs, 0, int.MaxValue);
        var mi = new MOUSEINPUT
        {
            dx = 0,
            dy = 0,
            mouseData = 0,
            dwFlags = flags,
            time = t,
            dwExtraInfo = UIntPtr.Zero,
        };
        var inp = new INPUT { type = INPUT_MOUSE, mi = mi };
        SendInput(1, [inp], InputSize);
    }

    public static Point GetCursorScreenPoint()
    {
        if (!GetCursorPos(out POINT pt))
            return Point.Empty;
        return new Point(pt.X, pt.Y);
    }

    /// <summary>
    /// Optional raw-input-friendly path: <c>SetCursorPos</c> plus <see cref="Win32Api.WM_MOUSEMOVE"/>
    /// in client space for a specific top-level window.
    /// </summary>
    public static void SetCursorAndNotifyWindowMove(IntPtr hwnd, int screenX, int screenY)
    {
        SetCursorPos(screenX, screenY);
        if (hwnd == IntPtr.Zero || !Win32Api.IsWindow(hwnd))
            return;

        var pt = new Win32Api.POINT { X = screenX, Y = screenY };
        if (!Win32Api.ScreenToClient(hwnd, ref pt))
            return;
        Win32Api.PostMessage(hwnd, Win32Api.WM_MOUSEMOVE, IntPtr.Zero, Win32Api.MakeLParam(pt.X, pt.Y));
    }
}

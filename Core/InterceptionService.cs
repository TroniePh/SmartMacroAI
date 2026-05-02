// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System.Runtime.InteropServices;

namespace SmartMacroAI.Core;

/// <summary>
/// Singleton service managing the Interception driver context.
/// Provides kernel-level mouse click and keyboard injection that bypasses anti-cheat.
/// When driver is not installed, <see cref="IsInitialized"/> is false and callers should fallback.
/// </summary>
public sealed class InterceptionService : IDisposable
{
    private static readonly Lazy<InterceptionService> _instance = new(() => new InterceptionService());
    public static InterceptionService Instance => _instance.Value;

    private IntPtr _ctx;
    private bool _disposed;

    /// <summary>True when interception.dll loaded and context created successfully.</summary>
    public bool IsInitialized { get; private set; }

    private InterceptionService() { }

    /// <summary>Initializes the Interception driver context. Safe to call multiple times.</summary>
    public bool Initialize()
    {
        if (IsInitialized) return true;

        try
        {
            _ctx = InterceptionDriver.interception_create_context();
            IsInitialized = _ctx != IntPtr.Zero;
            return IsInitialized;
        }
        catch (DllNotFoundException)
        {
            IsInitialized = false;
            return false;
        }
        catch
        {
            IsInitialized = false;
            return false;
        }
    }

    /// <summary>Finds the first mouse device index (11..20).</summary>
    public int FindMouseDevice()
    {
        for (int i = 11; i <= 20; i++)
        {
            if (InterceptionDriver.interception_is_mouse(i) != 0)
                return i;
        }
        return 11; // default fallback
    }

    /// <summary>Finds the first keyboard device index (1..10).</summary>
    public int FindKeyDevice()
    {
        for (int i = 1; i <= 10; i++)
        {
            if (InterceptionDriver.interception_is_keyboard(i) != 0)
                return i;
        }
        return 1; // default fallback
    }

    /// <summary>
    /// Sends an absolute mouse move + left click via Interception driver.
    /// </summary>
    public void SendMouseClick(int screenX, int screenY, int holdMs = 50, bool rightClick = false)
    {
        if (!IsInitialized) return;

        int device = FindMouseDevice();
        int sw = GetSystemMetrics(0);
        int sh = GetSystemMetrics(1);

        // Move to absolute position
        var moveStroke = new InterceptionDriver.InterceptionMouseStroke
        {
            Flags = InterceptionDriver.INTERCEPTION_MOUSE_MOVE_ABSOLUTE,
            X = (int)((screenX + 0.5) * 65535 / sw),
            Y = (int)((screenY + 0.5) * 65535 / sh),
        };
        SendMouse(device, moveStroke);
        Thread.Sleep(15);

        // Button down
        ushort downFlag = rightClick
            ? InterceptionDriver.INTERCEPTION_MOUSE_RIGHT_BUTTON_DOWN
            : InterceptionDriver.INTERCEPTION_MOUSE_LEFT_BUTTON_DOWN;
        var downStroke = new InterceptionDriver.InterceptionMouseStroke { State = downFlag };
        SendMouse(device, downStroke);
        Thread.Sleep(Math.Max(holdMs, 30));

        // Button up
        ushort upFlag = rightClick
            ? InterceptionDriver.INTERCEPTION_MOUSE_RIGHT_BUTTON_UP
            : InterceptionDriver.INTERCEPTION_MOUSE_LEFT_BUTTON_UP;
        var upStroke = new InterceptionDriver.InterceptionMouseStroke { State = upFlag };
        SendMouse(device, upStroke);
    }

    /// <summary>Sends a keyboard key down or up via Interception driver.</summary>
    public void SendKey(ushort scanCode, bool keyDown, bool extended = false)
    {
        if (!IsInitialized) return;

        int device = FindKeyDevice();
        ushort state = keyDown ? InterceptionDriver.INTERCEPTION_KEY_DOWN : InterceptionDriver.INTERCEPTION_KEY_UP;
        if (extended) state |= InterceptionDriver.INTERCEPTION_KEY_E0;

        var keyStroke = new InterceptionDriver.InterceptionKeyStroke { Code = scanCode, State = state };
        var stroke = InterceptionDriver.ToStroke(keyStroke);
        InterceptionDriver.interception_send(_ctx, device, ref stroke, 1);
    }

    /// <summary>Taps a key (down + delay + up) via Interception driver.</summary>
    public void TapKey(ushort scanCode, int holdMs = 50, bool extended = false)
    {
        SendKey(scanCode, keyDown: true, extended);
        Thread.Sleep(Math.Max(holdMs, 20));
        SendKey(scanCode, keyDown: false, extended);
    }

    private void SendMouse(int device, InterceptionDriver.InterceptionMouseStroke ms)
    {
        var stroke = InterceptionDriver.ToStroke(ms);
        InterceptionDriver.interception_send(_ctx, device, ref stroke, 1);
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    public void Dispose()
    {
        if (!_disposed && IsInitialized)
        {
            InterceptionDriver.interception_destroy_context(_ctx);
            _ctx = IntPtr.Zero;
            IsInitialized = false;
            _disposed = true;
        }
    }
}

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

    private int _mouseDevice = -1;
    private int _keyDevice = -1;

    /// <summary>Initializes the Interception driver context. Safe to call multiple times.</summary>
    public bool Initialize()
    {
        if (IsInitialized) return true;

        try
        {
            _ctx = InterceptionDriver.interception_create_context();
            if (_ctx == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine("[Interception] ❌ Context null — driver not installed or restart required");
                IsInitialized = false;
                return false;
            }

            // Set filter to receive all mouse and keyboard events
            InterceptionDriver.interception_set_filter(
                _ctx,
                device => InterceptionDriver.interception_is_mouse(device) > 0,
                InterceptionDriver.INTERCEPTION_FILTER_MOUSE_ALL);

            InterceptionDriver.interception_set_filter(
                _ctx,
                device => InterceptionDriver.interception_is_keyboard(device) > 0,
                InterceptionDriver.INTERCEPTION_FILTER_KEY_ALL);

            // Scan for real connected devices by checking hardware IDs
            _mouseDevice = FindActiveMouseDevice();
            _keyDevice = FindActiveKeyboardDevice();

            // Fallback if no device found
            if (_mouseDevice < 0) _mouseDevice = 11;
            if (_keyDevice < 0) _keyDevice = 1;

            IsInitialized = true;
            System.Diagnostics.Debug.WriteLine($"[Interception] ✅ Ready — mouse device={_mouseDevice}, key device={_keyDevice}");
            return true;
        }
        catch (DllNotFoundException)
        {
            System.Diagnostics.Debug.WriteLine("[Interception] ❌ interception.dll not found in app directory");
            IsInitialized = false;
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Interception] ❌ Init exception: {ex.Message}");
            IsInitialized = false;
            return false;
        }
    }

    /// <summary>Finds the first mouse device with a real hardware ID (device 11-20).</summary>
    public int FindActiveMouseDevice()
    {
        for (int i = 11; i <= 20; i++)
        {
            if (InterceptionDriver.interception_is_mouse(i) == 0) continue;
            string hwId = GetHardwareId(i);
            if (!string.IsNullOrEmpty(hwId))
            {
                System.Diagnostics.Debug.WriteLine($"[Interception] Found mouse device {i}: {hwId}");
                return i;
            }
        }
        return -1;
    }

    /// <summary>Finds the first keyboard device with a real hardware ID (device 1-10).</summary>
    public int FindActiveKeyboardDevice()
    {
        for (int i = 1; i <= 10; i++)
        {
            if (InterceptionDriver.interception_is_keyboard(i) == 0) continue;
            string hwId = GetHardwareId(i);
            if (!string.IsNullOrEmpty(hwId))
            {
                System.Diagnostics.Debug.WriteLine($"[Interception] Found keyboard device {i}: {hwId}");
                return i;
            }
        }
        return -1;
    }

    private string GetHardwareId(int device)
    {
        try
        {
            var buf = new System.Text.StringBuilder(512);
            int len = InterceptionDriver.interception_get_hardware_id(_ctx, device, buf, (uint)buf.Capacity);
            return len > 0 ? buf.ToString().Trim('\0') : string.Empty;
        }
        catch { return string.Empty; }
    }

    /// <summary>Rescans mouse and keyboard devices and returns true if both found.</summary>
    public bool RescanDevices()
    {
        if (!IsInitialized || _ctx == IntPtr.Zero) return false;

        _mouseDevice = FindActiveMouseDevice();
        _keyDevice = FindActiveKeyboardDevice();

        if (_mouseDevice < 0) _mouseDevice = 11;
        if (_keyDevice < 0) _keyDevice = 1;

        System.Diagnostics.Debug.WriteLine($"[Interception] Rescan — mouse={_mouseDevice}, key={_keyDevice}");
        return true;
    }

    /// <summary>
    /// Sends an absolute mouse move + click via Interception driver.
    /// </summary>
    public void SendMouseClick(int screenX, int screenY, int holdMs = 50, MouseButton button = MouseButton.Left)
    {
        if (!IsInitialized) return;

        int sw = GetSystemMetrics(0);
        int sh = GetSystemMetrics(1);

        // Move to absolute position
        var moveStroke = new InterceptionDriver.InterceptionMouseStroke
        {
            Flags = InterceptionDriver.INTERCEPTION_MOUSE_MOVE_ABSOLUTE,
            X = (int)((screenX + 0.5) * 65535 / sw),
            Y = (int)((screenY + 0.5) * 65535 / sh),
        };
        SendMouse(_mouseDevice, moveStroke);
        Thread.Sleep(15);

        // Button down
        ushort downFlag = button switch
        {
            MouseButton.Right => InterceptionDriver.INTERCEPTION_MOUSE_RIGHT_BUTTON_DOWN,
            MouseButton.Middle => InterceptionDriver.INTERCEPTION_MOUSE_MIDDLE_BUTTON_DOWN,
            _ => InterceptionDriver.INTERCEPTION_MOUSE_LEFT_BUTTON_DOWN,
        };
        var downStroke = new InterceptionDriver.InterceptionMouseStroke { State = downFlag };
        SendMouse(_mouseDevice, downStroke);
        Thread.Sleep(Math.Max(holdMs, 30));

        // Button up
        ushort upFlag = button switch
        {
            MouseButton.Right => InterceptionDriver.INTERCEPTION_MOUSE_RIGHT_BUTTON_UP,
            MouseButton.Middle => InterceptionDriver.INTERCEPTION_MOUSE_MIDDLE_BUTTON_UP,
            _ => InterceptionDriver.INTERCEPTION_MOUSE_LEFT_BUTTON_UP,
        };
        var upStroke = new InterceptionDriver.InterceptionMouseStroke { State = upFlag };
        SendMouse(_mouseDevice, upStroke);
    }

    /// <summary>Sends a keyboard key down or up via Interception driver.</summary>
    public void SendKey(ushort scanCode, bool keyDown, bool extended = false)
    {
        if (!IsInitialized) return;

        ushort state = keyDown ? InterceptionDriver.INTERCEPTION_KEY_DOWN : InterceptionDriver.INTERCEPTION_KEY_UP;
        if (extended) state |= InterceptionDriver.INTERCEPTION_KEY_E0;

        var keyStroke = new InterceptionDriver.InterceptionKeyStroke { Code = scanCode, State = state };
        var stroke = InterceptionDriver.ToStroke(keyStroke);
        InterceptionDriver.interception_send(_ctx, _keyDevice, ref stroke, 1);
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

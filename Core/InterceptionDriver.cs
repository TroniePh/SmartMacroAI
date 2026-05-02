// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System.Runtime.InteropServices;

namespace SmartMacroAI.Core;

/// <summary>
/// P/Invoke declarations for the Interception driver (interception.dll).
/// Provides kernel-level HID input injection that bypasses anti-cheat systems.
/// </summary>
public static class InterceptionDriver
{
    private const string DLL = "interception.dll";

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr interception_create_context();

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void interception_destroy_context(IntPtr context);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void interception_set_filter(IntPtr context, InterceptionPredicate predicate, ushort filter);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int interception_receive(IntPtr context, int device, ref InterceptionStroke stroke, uint nstroke);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int interception_send(IntPtr context, int device, ref InterceptionStroke stroke, uint nstroke);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int interception_wait(IntPtr context);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int interception_is_keyboard(int device);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int interception_is_mouse(int device);

    // ── Callback ──
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate bool InterceptionPredicate(int device);

    // ── Stroke union (largest of mouse/key stroke) ──
    [StructLayout(LayoutKind.Sequential)]
    public struct InterceptionStroke
    {
        // 20 bytes covers both mouse (20) and key (8) strokes
        public ushort Data0; public ushort Data1; public ushort Data2;
        public int Data3; public int Data4; public uint Data5;
    }

    // ── Mouse stroke ──
    [StructLayout(LayoutKind.Sequential)]
    public struct InterceptionMouseStroke
    {
        public ushort State;
        public ushort Flags;
        public short Rolling;
        public int X;
        public int Y;
        public uint Information;
    }

    // ── Key stroke ──
    [StructLayout(LayoutKind.Sequential)]
    public struct InterceptionKeyStroke
    {
        public ushort Code;
        public ushort State;
        public uint Information;
    }

    // ── Filter flags ──
    public const ushort INTERCEPTION_FILTER_MOUSE_ALL       = 0xFFFF;
    public const ushort INTERCEPTION_FILTER_KEY_ALL         = 0xFFFF;
    public const ushort INTERCEPTION_FILTER_MOUSE_LEFT_DOWN = 0x0001;
    public const ushort INTERCEPTION_FILTER_MOUSE_LEFT_UP   = 0x0002;

    // ── Mouse state flags ──
    public const ushort INTERCEPTION_MOUSE_LEFT_BUTTON_DOWN = 0x0001;
    public const ushort INTERCEPTION_MOUSE_LEFT_BUTTON_UP   = 0x0002;
    public const ushort INTERCEPTION_MOUSE_RIGHT_BUTTON_DOWN = 0x0004;
    public const ushort INTERCEPTION_MOUSE_RIGHT_BUTTON_UP  = 0x0008;
    public const ushort INTERCEPTION_MOUSE_MOVE              = 0x0001;
    public const ushort INTERCEPTION_MOUSE_MOVE_ABSOLUTE     = 0x0002;

    // ── Key state flags ──
    public const ushort INTERCEPTION_KEY_DOWN    = 0x00;
    public const ushort INTERCEPTION_KEY_UP      = 0x01;
    public const ushort INTERCEPTION_KEY_E0      = 0x02;
    public const ushort INTERCEPTION_KEY_E1      = 0x04;

    // ── Conversion helpers ──

    public static InterceptionStroke ToStroke(InterceptionMouseStroke ms)
    {
        var raw = new byte[20];
        var src = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref ms, 1));
        src.CopyTo(raw);
        return MemoryMarshal.Read<InterceptionStroke>(raw);
    }

    public static InterceptionStroke ToStroke(InterceptionKeyStroke ks)
    {
        var raw = new byte[20];
        var src = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref ks, 1));
        src.CopyTo(raw);
        return MemoryMarshal.Read<InterceptionStroke>(raw);
    }
}

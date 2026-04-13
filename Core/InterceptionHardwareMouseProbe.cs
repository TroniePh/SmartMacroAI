using System.IO;

namespace SmartMacroAI.Core;

/// <summary>
/// Detects optional Interception driver assets next to the executable.
/// Full kernel-level injection is not bundled; when a DLL is present we still use
/// per-step <see cref="Win32MouseInput"/> and emit a diagnostic note once per process.
/// </summary>
public static class InterceptionHardwareMouseProbe
{
    private static readonly string[] ProbeNames = ["interception.dll", "build/interception.dll"];

    /// <summary>Returns true when a known Interception user-mode DLL name exists beside the app.</summary>
    public static bool IsDriverDllPresent()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        foreach (string name in ProbeNames)
        {
            try
            {
                if (File.Exists(Path.Combine(baseDir, name)))
                    return true;
            }
            catch
            {
                // ignore IO errors
            }
        }

        return false;
    }
}

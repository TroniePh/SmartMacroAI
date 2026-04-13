using System.IO;
using System.Text.Json;

namespace SmartMacroAI.Localization;

/// <summary>
/// Application preferences persisted next to the executable (separate from hotkey_settings.json).
/// </summary>
public sealed class AppSettings
{
    /// <summary>UI language: "en" or "vi".</summary>
    public string LanguageCode { get; set; } = "en";

    /// <summary>Minimum template scale for multi-scale match (DPI / resolution drift).</summary>
    public double VisionMatchMinScale { get; set; } = 0.80;

    /// <summary>Maximum template scale for multi-scale match.</summary>
    public double VisionMatchMaxScale { get; set; } = 1.25;

    /// <summary>Physical mouse profile when Hardware mode is enabled: Relaxed, Normal, Fast, Instant.</summary>
    public string MouseProfileName { get; set; } = "Normal";

    /// <summary>Jitter intensity 0–100 for Gaussian path noise (0 disables noise when jitter is enabled in UI).</summary>
    public int MouseJitterIntensity { get; set; } = 50;

    /// <summary>When false, overshoot-and-correct segments are never applied.</summary>
    public bool MouseOvershootEnabled { get; set; } = true;

    /// <summary>When false, no random 8–25 ms micro-pause is inserted along the path.</summary>
    public bool MouseMicroPauseEnabled { get; set; } = true;

    /// <summary>Use <c>SetCursorPos</c> plus <c>WM_MOUSEMOVE</c> to the bound target window instead of absolute SendInput moves.</summary>
    public bool MouseRawInputBypass { get; set; }

    /// <summary>Prefer interception-style driver when <c>interception.dll</c> is present (falls back to SendInput).</summary>
    public bool MouseHardwareSimulationDriver { get; set; }

    private static readonly string PathFile = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "app_settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(PathFile))
            {
                string json = File.ReadAllText(PathFile);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(PathFile, json);
        }
        catch { }
    }
}

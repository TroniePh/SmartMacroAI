using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SmartMacroAI.Localization;

/// <summary>
/// Application preferences persisted next to the executable (separate from hotkey_settings.json).
/// </summary>
public sealed class AppSettings
{
    private static AppSettings? _cached;
    public static AppSettings Instance => _cached ??= Load();
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

    /// <summary>Windows.Media.Ocr language: <c>auto</c>, <c>vi-VN</c>, or <c>en-US</c>.</summary>
    public string OcrLanguageTag { get; set; } = "auto";

    /// <summary>Use <c>SetCursorPos</c> plus <c>WM_MOUSEMOVE</c> to the bound target window instead of absolute SendInput moves.</summary>
    public bool MouseRawInputBypass { get; set; }

    /// <summary>Prefer interception-style driver when <c>interception.dll</c> is present (falls back to SendInput).</summary>
    public bool MouseHardwareSimulationDriver { get; set; }

    // ── Anti-Detection (v1.1.0) ──

    public bool AntiDetectionEnabled { get; set; } = true;

    public bool AntiDetectionFatigueEnabled { get; set; } = true;

    public bool AntiDetectionMicroPauseBehavior { get; set; } = true;

    /// <summary>0–15 (%): misclick-away simulation in hardware mode.</summary>
    public int AntiDetectionMisclickPercent { get; set; } = 5;

    public bool AntiDetectionHideFromCapture { get; set; } = true;

    public List<string> AntiDetectionDecoyTitles { get; set; } =
    [
        "Notepad",
        "Calculator",
        "Windows Security",
        "Settings",
        "File Explorer",
    ];

    public int AntiDetectionSessionMinutes { get; set; } = 45;

    public int AntiDetectionSessionBreakMinMinutes { get; set; } = 3;

    public int AntiDetectionSessionBreakMaxMinutes { get; set; } = 10;

    public bool AntiDetectionSessionBreakEnabled { get; set; } = true;

    public bool AntiDetectionCpuIdleTweak { get; set; } = true;

    /// <summary>When true and hardware mode, type via scan-code <c>SendInput</c> instead of <c>WM_CHAR</c>.</summary>
    public bool AntiDetectionUseScanCodeTyping { get; set; } = true;

    public bool AntiDetectionHookScanOnStartup { get; set; } = true;

    // ── Telegram notification defaults ──

    /// <summary>Default Bot Token pre-filled in TelegramAction when user adds the action.</summary>
    public string TelegramBotToken { get; set; } = "";

    /// <summary>Default Chat ID pre-filled in TelegramAction when user adds the action.</summary>
    public string TelegramChatId { get; set; } = "";

    /// <summary>True when both TelegramBotToken and TelegramChatId are non-empty.</summary>
    public bool HasTelegramToken =>
        !string.IsNullOrWhiteSpace(TelegramBotToken) &&
        !string.IsNullOrWhiteSpace(TelegramChatId);

    private static readonly string PathFile = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "app_settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(PathFile))
            {
                string json = File.ReadAllText(PathFile);
                var s = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                if (s.AntiDetectionDecoyTitles is null || s.AntiDetectionDecoyTitles.Count == 0)
                {
                    s.AntiDetectionDecoyTitles =
                    [
                        "Notepad",
                        "Calculator",
                        "Windows Security",
                        "Settings",
                        "File Explorer",
                    ];
                }

                return s;
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
            // Refresh the cached singleton so XAML bindings pick up the new values immediately.
            try
            {
                if (File.Exists(PathFile))
                {
                    string fresh = File.ReadAllText(PathFile);
                    var reloaded = JsonSerializer.Deserialize<AppSettings>(fresh);
                    if (reloaded != null && _cached != null)
                    {
                        foreach (var prop in typeof(AppSettings).GetProperties(
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                        {
                            if (prop.CanWrite)
                            {
                                var val = prop.GetValue(reloaded);
                                prop.SetValue(_cached, val);
                            }
                        }
                    }
                }
            }
            catch { }
        }
        catch { }
    }
}

using System.Windows;

namespace SmartMacroAI.Localization;

/// <summary>
/// Swaps the merged string <see cref="ResourceDictionary"/> for English / Vietnamese at runtime.
/// </summary>
public static class LanguageManager
{
    private static ResourceDictionary? _stringsDictionary;
    private static readonly object Gate = new();

    /// <summary>Current active language code ("en" or "vi").</summary>
    public static string CurrentLanguage { get; private set; } = "vi";

    /// <summary>Fired after the active string dictionary has been replaced (UI should refresh code-bound strings).</summary>
    public static event EventHandler? UiLanguageChanged;

    /// <summary>Call once at startup (before showing main window) to apply saved language.</summary>
    public static void ApplySavedLanguage()
    {
        var settings = AppSettings.Load();
        ChangeLanguage(settings.LanguageCode);
    }

    /// <summary>
    /// Loads the language XAML (en/vi), removes the previous strings dictionary from app resources, and merges the new one.
    /// </summary>
    public static void ChangeLanguage(string languageCode)
    {
        var app = Application.Current;
        if (app is null)
            return;

        string code = string.IsNullOrWhiteSpace(languageCode) ? "en" : languageCode.Trim().ToLowerInvariant();
        if (code != "vi")
            code = "en";

        CurrentLanguage = code;

        string relative = code == "vi"
            ? "Localization/Strings.vi.xaml"
            : "Localization/Strings.en.xaml";

        // Use absolute pack URI for reliability
        var uri = new Uri($"pack://application:,,,/SmartMacroAI;component/{relative}", UriKind.Absolute);
        var newDict = new ResourceDictionary { Source = uri };

        lock (Gate)
        {
            // Remove ALL existing language dictionaries before adding the new one.
            // This prevents accumulating duplicate dicts if ChangeLanguage is called
            // before _stringsDictionary has been assigned (e.g., after app restart).
            var existing = app.Resources.MergedDictionaries
                .Where(d => d.Source is not null &&
                            (d.Source.OriginalString.Contains("Strings.vi") ||
                             d.Source.OriginalString.Contains("Strings.en")))
                .ToList();

            foreach (var d in existing)
                app.Resources.MergedDictionaries.Remove(d);

            // Add the new dict at the end so WPF framework dictionaries keep priority.
            app.Resources.MergedDictionaries.Add(newDict);
            _stringsDictionary = newDict;
        }

        // Persist preference (reuse HotkeySettings folder pattern via AppSettings)
        var s = AppSettings.Load();
        s.LanguageCode = code;
        s.Save();

        System.Diagnostics.Debug.WriteLine($"[LanguageManager] Applied: {code} ({relative})");

        UiLanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Looks up a string resource on the application scope.</summary>
    public static string GetString(string key)
    {
        if (Application.Current?.TryFindResource(key) is string str)
            return str;
        return key;
    }
}

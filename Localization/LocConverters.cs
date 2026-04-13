using System.Globalization;
using System.Windows.Data;

namespace SmartMacroAI.Localization;

/// <summary>Concatenates <see cref="Prefix"/> with the bound value and resolves <c>ui_*</c> string from app resources.</summary>
public sealed class ResourceKeyConcatConverter : IValueConverter
{
    public string Prefix { get; set; } = "";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string suffix = value?.ToString() ?? "";
        string prefix = parameter as string ?? Prefix;
        string key = prefix + suffix;
        return LanguageManager.GetString(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Formats action count using localized format string <c>ui_ActionCountFmt</c>.</summary>
public sealed class ActionCountFormatConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int n)
            return string.Format(LanguageManager.GetString("ui_ActionCountFmt"), n);
        return "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

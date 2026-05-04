using SmartMacroAI.Localization;

namespace SmartMacroAI.Models;

/// <summary>
/// Application notification model for the Notification Center.
/// Created by Phạm Duy – Giải pháp tự động hóa thông minh.
/// </summary>
public enum NotificationType { Success, Error, Warning, Info, Schedule }

public sealed class AppNotification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public NotificationType Type { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime Time { get; set; } = DateTime.Now;
    public bool IsRead { get; set; }
    public string? MacroName { get; set; }
    public string? ActionPath { get; set; }

    public string TypeIcon => Type switch
    {
        NotificationType.Success => "✅",
        NotificationType.Error => "❌",
        NotificationType.Warning => "⚠️",
        NotificationType.Schedule => "⏰",
        _ => "ℹ️"
    };

    public string TypeColor => Type switch
    {
        NotificationType.Success => "#A6E3A1",
        NotificationType.Error => "#F38BA8",
        NotificationType.Warning => "#FAB387",
        NotificationType.Schedule => "#89B4FA",
        _ => "#CDD6F4"
    };

    public string TimeAgo
    {
        get
        {
            var diff = DateTime.Now - Time;
            if (diff.TotalSeconds < 60) return LanguageManager.GetString("ui_Time_JustNow");
            if (diff.TotalMinutes < 60) return string.Format(LanguageManager.GetString("ui_Time_MinutesAgo"), (int)diff.TotalMinutes);
            if (diff.TotalHours < 24) return string.Format(LanguageManager.GetString("ui_Time_HoursAgo"), (int)diff.TotalHours);
            return Time.ToString("dd/MM HH:mm");
        }
    }
}

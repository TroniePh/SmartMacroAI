// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmartMacroAI.ViewModels;

public sealed class ProfileRowVm : INotifyPropertyChanged
{
    public string ProfileId { get; set; } = "";

    private string _name = "";
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string ProxyHost { get; set; } = "";
    public string ProxyPort { get; set; } = "";
    public string ProxyUser { get; set; } = "";
    public string ProxyPassword { get; set; } = "";

    private string _status = "";
    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    public string ProxySummary =>
        string.IsNullOrWhiteSpace(ProxyHost)
            ? "(none)"
            : $"{ProxyHost}:{ProxyPort}";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? prop = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}

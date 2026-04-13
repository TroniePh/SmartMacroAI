using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmartMacroAI.ViewModels;

public sealed class StealthWindowVm : INotifyPropertyChanged
{
    public IntPtr Hwnd { get; set; }
    public string WindowTitle { get; set; } = "";

    private bool _isHidden;
    public bool IsHidden
    {
        get => _isHidden;
        set
        {
            if (_isHidden != value)
            {
                _isHidden = value;
                Notify();
                Notify(nameof(StatusKey));
                Notify(nameof(ToggleKey));
            }
        }
    }

    /// <summary>Localization key suffix for <c>ui_StealthStatus_*</c>.</summary>
    public string StatusKey => _isHidden ? "Hidden" : "Visible";

    /// <summary>Localization key suffix for <c>ui_StealthToggle_*</c>.</summary>
    public string ToggleKey => _isHidden ? "Show" : "Hide";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

using System.Windows;
using SmartMacroAI.Localization;

namespace SmartMacroAI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        LanguageManager.ApplySavedLanguage();
        base.OnStartup(e);
    }
}

// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System.Diagnostics;
using System.Windows;
using SmartMacroAI.Core;
using SmartMacroAI.Localization;

namespace SmartMacroAI;

public partial class DriverInstallDialog : Window
{
    public bool InstallSucceeded { get; private set; }

    public DriverInstallDialog()
    {
        InitializeComponent();
    }

    private void SafeClose()
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (IsLoaded)
                DialogResult = InstallSucceeded;
            else
                Close();
        });
    }

    private async void BtnInstall_Click(object sender, RoutedEventArgs e)
    {
        BtnInstall.IsEnabled = false;
        BtnCancel.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;

        var result = await InterceptionInstaller.InstallAsync(msg =>
        {
            Dispatcher.Invoke(() => TxtStatus.Text = msg);
        });

        ProgressPanel.Visibility = Visibility.Collapsed;
        BtnCancel.IsEnabled = true;

        switch (result)
        {
            case InstallResult.NeedRestart:
                InstallSucceeded = true;
                var restart = MessageBox.Show(
                    LanguageManager.GetString("ui_Drv_InstallSuccessMsg"),
                    LanguageManager.GetString("ui_Drv_InstallComplete"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                if (restart == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "shutdown",
                        Arguments = "/r /t 5 /c \"SmartMacroAI: Interception driver installed\"",
                        UseShellExecute = false
                    });
                    Application.Current.Shutdown();
                }
                else
                {
                    MessageBox.Show(
                        LanguageManager.GetString("ui_Drv_RestartRequired"),
                        LanguageManager.GetString("ui_Drv_Note"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                SafeClose();
                break;

            case InstallResult.UserCancelled:
                BtnInstall.IsEnabled = true;
                TxtStatus.Text = LanguageManager.GetString("ui_Drv_CancelledAdmin");
                ProgressPanel.Visibility = Visibility.Visible;
                break;

            case InstallResult.Failed:
                string logText = InterceptionInstaller.GetLastLogs();
                var osVer = Environment.OSVersion.Version;
                bool isWin11 = osVer.Major == 10 && osVer.Build >= 22000;

                string hint = isWin11
                    ? LanguageManager.GetString("ui_Drv_Win11Hint")
                    : "";

                MessageBox.Show(
                    LanguageManager.GetString("ui_Drv_InstallFailedMsg") +
                    logText + hint + "\n\n" +
                    LanguageManager.GetString("ui_Drv_FallbackMsg"),
                    LanguageManager.GetString("ui_Drv_InstallError"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                BtnInstall.IsEnabled = true;
                break;

            case InstallResult.AlreadyInstalled:
                InstallSucceeded = true;
                SafeClose();
                break;
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();
}

// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System.Diagnostics;
using System.Windows;
using SmartMacroAI.Core;

namespace SmartMacroAI;

public partial class DriverInstallDialog : Window
{
    public bool InstallSucceeded { get; private set; }

    public DriverInstallDialog()
    {
        InitializeComponent();
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
                    "Cài đặt thành công!\n\nCần khởi động lại máy để hoàn tất.\nKhởi động lại ngay bây giờ?",
                    "Cài đặt hoàn tất",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                if (restart == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "shutdown",
                        Arguments = "/r /t 5 /c \"SmartMacroAI: Hoàn tất cài driver Interception\"",
                        UseShellExecute = false
                    });
                    Application.Current.Shutdown();
                }
                else
                {
                    MessageBox.Show(
                        "Vui lòng khởi động lại máy trước khi dùng Driver Level mode.",
                        "Lưu ý", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                Close();
                break;

            case InstallResult.UserCancelled:
                BtnInstall.IsEnabled = true;
                TxtStatus.Text = "Đã hủy — cần quyền Admin để cài driver";
                ProgressPanel.Visibility = Visibility.Visible;
                break;

            case InstallResult.Failed:
                string logText = InterceptionInstaller.GetLastLogs();
                var osVer = Environment.OSVersion.Version;
                bool isWin11 = osVer.Major == 10 && osVer.Build >= 22000;

                string hint = isWin11
                    ? "\n\n[Windows 11] Có thể do driver unsigned bị block.\n" +
                      "Thử chạy lệnh sau với quyền Admin rồi restart:\n" +
                      "bcdedit /set testsigning on"
                    : "";

                MessageBox.Show(
                    "Cài đặt thất bại.\n\n" +
                    "Chi tiết lỗi:\n" +
                    logText + hint + "\n\n" +
                    "Chuyển sang Raw mode tạm thời. Vui lòng chụp màn hình gửi cho hỗ trợ.",
                    "Lỗi cài đặt — Driver Level",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                BtnInstall.IsEnabled = true;
                break;

            case InstallResult.AlreadyInstalled:
                InstallSucceeded = true;
                Close();
                break;
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();
}

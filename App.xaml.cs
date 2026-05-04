// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System.IO;
using System.Windows;
using SmartMacroAI.Core;
using SmartMacroAI.Localization;

namespace SmartMacroAI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // CATCH ALL unhandled exceptions and show them
        AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
        {
            string msg = ex.ExceptionObject?.ToString() ?? "Unknown error";
            try { File.WriteAllText("crash.log", $"[{DateTime.Now}] CRASH:\n{msg}"); } catch { }
            MessageBox.Show($"{LanguageManager.GetString("ui_Msg_InitError")}\n\n{msg}", "SmartMacroAI — Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (s, ex) =>
        {
            string msg = ex.Exception?.ToString() ?? "Unknown";
            try { File.WriteAllText("crash.log", $"[{DateTime.Now}] DISPATCHER CRASH:\n{msg}"); } catch { }
            MessageBox.Show($"{LanguageManager.GetString("ui_Msg_UiError")}\n\n{msg}", "SmartMacroAI — UI Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, ex) =>
        {
            try { File.AppendAllText("crash.log", $"[{DateTime.Now}] TASK CRASH:\n{ex.Exception}\n"); } catch { }
            ex.SetObserved();
        };

        try
        {
            // BƯỚC 1: Apply language TRƯỚC KHI tạo bất kỳ Window nào
            LanguageManager.ApplySavedLanguage();
            System.Diagnostics.Debug.WriteLine($"[Startup] Language applied: {LanguageManager.CurrentLanguage}");

            // BƯỚC 2: Initialize Interception driver if DLL + driver already installed
            try
            {
                if (InterceptionInstaller.IsReady())
                {
                    bool ok = InterceptionService.Instance.Initialize();
                    System.Diagnostics.Debug.WriteLine(ok
                        ? "[Startup] ✅ Interception ready"
                        : "[Startup] ⚠️ Driver file present but init failed — using Raw mode");
                }
            }
            catch { }

            // BƯỚC 3: Tạo và hiển thị MainWindow SAU KHI language đã apply
            base.OnStartup(e);
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            try { File.WriteAllText("crash.log", $"[{DateTime.Now}] ONSTARTUP CRASH:\n{ex}"); } catch { }
            MessageBox.Show($"{LanguageManager.GetString("ui_Msg_OnStartupError")}\n\n{ex.Message}\n\n{ex.StackTrace}", "SmartMacroAI — Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { InterceptionService.Instance.Dispose(); } catch { }
        base.OnExit(e);
    }
}

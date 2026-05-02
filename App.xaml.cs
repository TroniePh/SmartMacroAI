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
            MessageBox.Show($"Lỗi khởi động:\n\n{msg}", "SmartMacroAI — Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (s, ex) =>
        {
            string msg = ex.Exception?.ToString() ?? "Unknown";
            try { File.WriteAllText("crash.log", $"[{DateTime.Now}] DISPATCHER CRASH:\n{msg}"); } catch { }
            MessageBox.Show($"Lỗi UI:\n\n{msg}", "SmartMacroAI — UI Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, ex) =>
        {
            try { File.AppendAllText("crash.log", $"[{DateTime.Now}] TASK CRASH:\n{ex.Exception}\n"); } catch { }
            ex.SetObserved();
        };

        try
        {
            LanguageManager.ApplySavedLanguage();

            // Initialize Interception driver if DLL + driver already installed (non-blocking)
            try
            {
                if (InterceptionInstaller.IsReady())
                    InterceptionService.Instance.Initialize();
            }
            catch { }

            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText("crash.log", $"[{DateTime.Now}] ONSTARTUP CRASH:\n{ex}"); } catch { }
            MessageBox.Show($"Lỗi OnStartup:\n\n{ex.Message}\n\n{ex.StackTrace}", "SmartMacroAI — Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { InterceptionService.Instance.Dispose(); } catch { }
        base.OnExit(e);
    }
}

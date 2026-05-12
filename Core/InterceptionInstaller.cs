// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Windows;
using Microsoft.Win32;
using SmartMacroAI.Localization;

namespace SmartMacroAI.Core;

/// <summary>
/// Auto-installs the Interception driver.
/// Extracts embedded resources, runs the installer with UAC, and copies interception.dll to the app directory.
/// </summary>
public static class InterceptionInstaller
{
    private static readonly string AppDir;
    private static readonly string DllPath;
    private static readonly string InstallerPath;

    static InterceptionInstaller()
    {
        try
        {
            // AppContext.BaseDirectory is safe for .NET 8 SingleFile publish
            // (Assembly.GetExecutingAssembly().Location returns "" in SingleFile)
            AppDir = AppContext.BaseDirectory;

            if (string.IsNullOrEmpty(AppDir))
            {
                AppDir = Path.GetDirectoryName(
                    System.Diagnostics.Process.GetCurrentProcess()
                          .MainModule?.FileName) ?? Environment.CurrentDirectory;
            }

            DllPath = Path.Combine(AppDir, "interception.dll");
            InstallerPath = Path.Combine(Path.GetTempPath(), "SmartMacroAI-interception-install.exe");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[InterceptionInstaller] Init warning: {ex.Message}");
            AppDir = Environment.CurrentDirectory;
            DllPath = Path.Combine(AppDir, "interception.dll");
            InstallerPath = Path.Combine(Path.GetTempPath(), "SmartMacroAI-interception-install.exe");
        }
    }

    // ── Log buffer for diagnostic output ──
    private static readonly List<string> _logBuffer = new();

    private static void Log(string msg)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
        _logBuffer.Add(line);
        Debug.WriteLine(line);
    }

    /// <summary>Returns last N log lines for display in error dialogs.</summary>
    public static string GetLastLogs() =>
        string.Join("\n", _logBuffer.TakeLast(30));

    /// <summary>Clears the log buffer.</summary>
    public static void ClearLogs() => _logBuffer.Clear();

    /// <summary>
    /// Kiểm tra driver đã cài và dll đã có chưa.
    /// </summary>
    public static bool IsReady()
    {
        bool dllExists = File.Exists(DllPath);
        bool driverInstalled = IsDriverInstalled();
        return dllExists && driverInstalled;
    }

    /// <summary>
    /// Kiểm tra Interception driver đã có trong registry/service chưa.
    /// </summary>
    private static bool IsDriverInstalled()
    {
        try
        {
            // Check 1: Interception service registry key
            using var key1 = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\interception");
            if (key1 != null) return true;

            // Check 2: keyboard.sys driver file (Interception installs as keyboard filter)
            string sysPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "drivers", "keyboard.sys");
            if (File.Exists(sysPath)) return true;

            // Check 3: Multi-class device driver variant
            using var key2 = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Enum\ROOT\SYSTEM\0001");
            if (key2 != null)
            {
                string? svc = key2.GetValue("Service") as string;
                if (!string.IsNullOrEmpty(svc) &&
                    svc.Contains("interception", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
        catch { return false; }
    }

    /// <summary>
    /// Cài driver với quyền Admin. Trả về kết quả cài đặt.
    /// </summary>
    public static async Task<InstallResult> InstallAsync(Action<string>? progressLog = null)
    {
        ClearLogs();
        Log("=== Interception Install Start ===");
        Log($"AppDir: {AppDir}");
        Log($"DllPath: {DllPath}");
        Log($"InstallerPath: {InstallerPath}");
        Log($"IsAdmin: {IsRunningAsAdmin()}");
        Log($"OS: {Environment.OSVersion} (Build {Environment.OSVersion.Version.Build})");

        void Report(string msg)
        {
            Log(msg);
            progressLog?.Invoke(msg);
        }

        try
        {
            // ── STEP A: Enumerate embedded resources ──
            if (IsReady())
            {
                Log("Interception driver and DLL are already available.");
                return InstallResult.AlreadyInstalled;
            }

            var asm = Assembly.GetExecutingAssembly();
            var allResources = asm.GetManifestResourceNames();
            Log($"Embedded resources ({allResources.Length}): {string.Join(" | ", allResources)}");

            // ── STEP B: Find resources (dùng EndsWith để tránh lệch namespace) ──
            string? dllResource = allResources
                .FirstOrDefault(r => r.EndsWith("interception.dll", StringComparison.OrdinalIgnoreCase));
            string? installerResource = allResources
                .FirstOrDefault(r => r.EndsWith("install-interception.exe", StringComparison.OrdinalIgnoreCase));

            if (installerResource == null)
            {
                Log(LanguageManager.GetString("ui_Install_NoInstallerResource"));
                return InstallResult.Failed;
            }

            // ── STEP C: Extract installer ──
            Report(string.Format(LanguageManager.GetString("ui_Install_ExtractInstaller"), installerResource));
            Directory.CreateDirectory(Path.GetDirectoryName(InstallerPath)!);
            using (var stream = asm.GetManifestResourceStream(installerResource)!)
            using (var fs = File.Create(InstallerPath))
                await stream!.CopyToAsync(fs);

            var fi = new FileInfo(InstallerPath);
            Log(string.Format(LanguageManager.GetString("ui_Install_Extracted"), fi.Length / 1024, InstallerPath));
            if (fi.Length < 1024)
            {
                Log(LanguageManager.GetString("ui_Install_ExtractTooSmall"));
                return InstallResult.Failed;
            }

            // ── STEP D: Extract interception.dll ──
            if (dllResource != null && !File.Exists(DllPath))
            {
                Log(string.Format(LanguageManager.GetString("ui_Install_ExtractDll"), dllResource));
                Directory.CreateDirectory(Path.GetDirectoryName(DllPath)!);
                using var ds = asm.GetManifestResourceStream(dllResource)!;
                using var df = File.Create(DllPath);
                await ds.CopyToAsync(df);
                Log($"interception.dll extracted: {new FileInfo(DllPath).Length / 1024}KB");
            }

            // ── STEP E: Windows 11 driver signing warning ──
            var osVer = Environment.OSVersion.Version;
            bool isWin11 = osVer.Major == 10 && osVer.Build >= 22000;
            if (isWin11)
            {
                Log(string.Format(LanguageManager.GetString("ui_Install_Win11Detected"), osVer.Build));
                Log(LanguageManager.GetString("ui_Install_TryTestSigning"));
            }

            // ── STEP F: Run installer with UAC ──
            Report(LanguageManager.GetString("ui_Install_RunningInstaller"));

            var psi = new ProcessStartInfo
            {
                FileName        = InstallerPath,
                Arguments       = "/install",
                Verb            = "runas",
                UseShellExecute = true,
                CreateNoWindow  = false
            };

            Process? process;
            try
            {
                process = Process.Start(psi);
            }
            catch (Exception startEx)
            {
                Log($"[ERROR] Process.Start exception: {startEx.GetType().Name}: {startEx.Message}");
                return startEx is Win32Exception win32 && win32.NativeErrorCode == 1223
                    ? InstallResult.UserCancelled
                    : InstallResult.Failed;
            }

            if (process == null)
            {
                Log(LanguageManager.GetString("ui_Install_ProcessNull"));
                return InstallResult.Failed;
            }

            Log($"Process started, PID={process.Id}");

            bool exited = await Task.Run(() => process.WaitForExit(30_000));
            int exitCode = exited ? process.ExitCode : -1;
            Log($"Process exited: {exited}, ExitCode={exitCode}");

            if (!exited)
            {
                Log("[ERROR] Installer timeout 30s — kill");
                try { process.Kill(); } catch { }
                return InstallResult.Failed;
            }

            // ── STEP G: Interpret exit code ──
            if (exitCode == 0)
            {
                Log(LanguageManager.GetString("ui_Install_ExitCode0"));
            }
            else if (exitCode == 1)
            {
                Log(LanguageManager.GetString("ui_Install_ExitCode1"));
            }
            else
            {
                Log($"[ERROR] Installer ExitCode={exitCode}");
                if (isWin11)
                    Log(LanguageManager.GetString("ui_Install_Win11Hint"));
                return InstallResult.Failed;
            }

            // ── STEP H: Verify dll is in app directory ──
            if (!File.Exists(DllPath))
            {
                Log(LanguageManager.GetString("ui_Install_CopyDllFromTemp"));
                string tempDll = Path.Combine(Path.GetTempPath(), "interception.dll");
                if (File.Exists(tempDll))
                    File.Copy(tempDll, DllPath);
                else
                    Log(LanguageManager.GetString("ui_Install_DllNotInTemp"));
            }

            // ── STEP I: Post-install verification ──
            bool driverNowInstalled = IsDriverInstalled();
            Log($"Post-install check: IsDriverInstalled={driverNowInstalled}, DllExists={File.Exists(DllPath)}");
            if (!driverNowInstalled || !File.Exists(DllPath))
            {
                Log("[ERROR] Installer exited without a verifiable driver/DLL state.");
                return InstallResult.Failed;
            }

            Report(LanguageManager.GetString("ui_Install_Done"));
            return InstallResult.NeedRestart;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Log(LanguageManager.GetString("ui_Install_UacCancelled"));
            return InstallResult.UserCancelled;
        }
        catch (Exception ex)
        {
            Log($"[ERROR] {ex.GetType().Name}: {ex.Message}");
            Log($"[ERROR] Stack: {ex.StackTrace}");
            return InstallResult.Failed;
        }
    }

    private static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Restart app với quyền Admin để apply driver.
    /// </summary>
    public static void RestartAsAdmin()
    {
        var psi = new ProcessStartInfo
        {
            FileName        = Process.GetCurrentProcess().MainModule?.FileName
                              ?? Path.Combine(AppContext.BaseDirectory, "SmartMacroAI.exe"),
            Verb            = "runas",
            UseShellExecute = true
        };
        Process.Start(psi);
        Application.Current.Shutdown();
    }

    /// <summary>
    /// Gỡ cài đặt Interception driver. Chạy installer với /uninstall flag.
    /// Nếu thất bại, trả về hướng dẫn gỡ thủ công.
    /// </summary>
    public static async Task<(bool Success, string Message)> UninstallAsync(Action<string>? progressLog = null)
    {
        ClearLogs();
        Log("=== Interception Uninstall Start ===");

        void Report(string msg)
        {
            Log(msg);
            progressLog?.Invoke(msg);
        }

        try
        {
            if (!IsDriverInstalled())
            {
                return (true, LanguageManager.GetString("ui_Drv_NotInstalled"));
            }

            // Try to extract and run installer with /uninstall
            var asm = Assembly.GetExecutingAssembly();
            var allResources = asm.GetManifestResourceNames();
            string? installerResource = allResources
                .FirstOrDefault(r => r.EndsWith("install-interception.exe", StringComparison.OrdinalIgnoreCase));

            if (installerResource != null)
            {
                Report(LanguageManager.GetString("ui_Drv_Uninstalling"));
                Directory.CreateDirectory(Path.GetDirectoryName(InstallerPath)!);
                using (var stream = asm.GetManifestResourceStream(installerResource)!)
                using (var fs = File.Create(InstallerPath))
                    await stream.CopyToAsync(fs);

                var psi = new ProcessStartInfo
                {
                    FileName = InstallerPath,
                    Arguments = "/uninstall",
                    Verb = "runas",
                    UseShellExecute = true,
                    CreateNoWindow = false
                };

                Process? process;
                try
                {
                    process = Process.Start(psi);
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    return (false, LanguageManager.GetString("ui_Drv_CancelledAdmin"));
                }
                catch (Exception ex)
                {
                    Log($"[ERROR] {ex.Message}");
                    return (false, GetManualUninstallGuide());
                }

                if (process != null)
                {
                    bool exited = await Task.Run(() => process.WaitForExit(30_000));
                    if (exited && process.ExitCode == 0)
                    {
                        // Also delete the DLL
                        try { if (File.Exists(DllPath)) File.Delete(DllPath); } catch { }
                        return (true, LanguageManager.GetString("ui_Drv_UninstallSuccess"));
                    }
                }
            }

            // Installer failed or not available — return manual guide
            return (false, GetManualUninstallGuide());
        }
        catch (Exception ex)
        {
            Log($"[ERROR] {ex.Message}");
            return (false, GetManualUninstallGuide());
        }
    }

    /// <summary>
    /// Trả về hướng dẫn gỡ driver thủ công bằng command prompt / registry.
    /// </summary>
    public static string GetManualUninstallGuide()
    {
        return @"═══ HƯỚNG DẪN GỠ INTERCEPTION DRIVER THỦ CÔNG ═══

Nếu driver gây khoá chuột/phím sau khi restart:

▶ CÁCH 1: Dùng Command Prompt (khuyến nghị)
  1. Khởi động lại máy, giữ Shift + nhấn Restart
  2. Chọn: Troubleshoot → Advanced Options → Command Prompt
  3. Gõ lần lượt:
     sc delete keyboard
     sc delete mouse
  4. Restart bình thường

▶ CÁCH 2: Boot USB Windows PE
  1. Boot từ USB cài Windows
  2. Chọn 'Repair your computer' → Command Prompt
  3. Gõ:
     sc delete keyboard
     sc delete mouse

▶ CÁCH 3: Registry Editor (nếu vào được Safe Mode)
  1. Mở regedit
  2. Xoá key:
     HKLM\SYSTEM\CurrentControlSet\Services\keyboard
     HKLM\SYSTEM\CurrentControlSet\Services\mouse
  3. Restart

▶ CÁCH 4: Safe Mode + Device Manager
  1. Boot Safe Mode (F8 hoặc Shift+Restart)
  2. Mở Device Manager
  3. Tìm 'Interception' trong Keyboards/Mice
  4. Right-click → Uninstall device
  5. Restart";
    }
}

/// <summary>Result of an Interception driver installation attempt.</summary>
public enum InstallResult
{
    AlreadyInstalled,
    NeedRestart,
    UserCancelled,
    Failed
}

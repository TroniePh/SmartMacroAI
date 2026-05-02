// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Principal;
using System.Windows;
using Microsoft.Win32;

namespace SmartMacroAI.Core;

/// <summary>
/// Auto-installs the Interception driver. Extracts embedded resources or downloads from GitHub,
/// runs the installer with UAC, and copies interception.dll to the app directory.
/// </summary>
public static class InterceptionInstaller
{
    private static readonly string AppDir =
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

    private static readonly string DllPath =
        Path.Combine(AppDir, "interception.dll");

    private static readonly string InstallerPath =
        Path.Combine(Path.GetTempPath(), "SmartMacroAI-interception-install.exe");

    /// <summary>Interception release URL for downloading installer + dll.</summary>
    private const string DownloadBaseUrl = "https://github.com/oblitum/Interception/releases/download/1.0.0";

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
        Log("=== Interception Installer Start ===");
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
            var asm = Assembly.GetExecutingAssembly();
            var allResources = asm.GetManifestResourceNames();
            Log($"Embedded resources ({allResources.Length}): {string.Join(" | ", allResources)}");

            // ── STEP B: Find installer resource ──
            string? installerResource = allResources
                .FirstOrDefault(r => r.Contains("install-interception", StringComparison.OrdinalIgnoreCase));

            // ── STEP C: Extract or download installer ──
            bool installerOk = false;
            if (installerResource != null)
            {
                Report($"Extracting installer từ embedded: {installerResource}");
                Directory.CreateDirectory(Path.GetDirectoryName(InstallerPath)!);
                using (var stream = asm.GetManifestResourceStream(installerResource)!)
                using (var fs = File.Create(InstallerPath))
                    stream!.CopyTo(fs);

                var fi = new FileInfo(InstallerPath);
                Log($"Extracted: {fi.Length / 1024}KB tại {InstallerPath}");
                installerOk = fi.Length >= 1024;
                if (!installerOk)
                    Log("[ERROR] File extract ra quá nhỏ — resource bị corrupt?");
            }
            else
            {
                Log("install-interception.exe KHÔNG có trong embedded resources — thử tải từ GitHub");
                string url = $"{DownloadBaseUrl}/install-interception.exe";
                installerOk = await DownloadFileAsync(url, InstallerPath);
            }

            if (!installerOk)
            {
                Report("[ERROR] Không có file cài đặt Interception");
                return InstallResult.Failed;
            }

            // ── STEP D: Extract or download interception.dll ──
            string? dllResource = allResources
                .FirstOrDefault(r => r.Contains("interception.dll", StringComparison.OrdinalIgnoreCase));

            if (dllResource != null && !File.Exists(DllPath))
            {
                Log($"Extracting interception.dll từ embedded: {dllResource}");
                using var ds = asm.GetManifestResourceStream(dllResource)!;
                using var df = File.Create(DllPath);
                ds.CopyTo(df);
                Log($"interception.dll extracted: {new FileInfo(DllPath).Length / 1024}KB");
            }
            else if (!File.Exists(DllPath))
            {
                Log("interception.dll KHÔNG có trong embedded — thử tải từ GitHub");
                string dllUrl = $"{DownloadBaseUrl}/interception.dll";
                await DownloadFileAsync(dllUrl, DllPath);
            }

            // ── STEP E: Windows 11 driver signing warning ──
            var osVer = Environment.OSVersion.Version;
            bool isWin11 = osVer.Major == 10 && osVer.Build >= 22000;
            if (isWin11)
            {
                Log($"[INFO] Windows 11 detected (build {osVer.Build}) — có thể cần Test Signing mode");
                Log("[INFO] Nếu cài thất bại, thử: bcdedit /set testsigning on → restart");
            }

            // ── STEP F: Run installer with UAC ──
            Report("Đang chạy install-interception.exe /install (UAC prompt)...");

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
                return startEx is System.ComponentModel.Win32Exception win32 && win32.NativeErrorCode == 1223
                    ? InstallResult.UserCancelled
                    : InstallResult.Failed;
            }

            if (process == null)
            {
                Log("[ERROR] Process.Start trả về null");
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
                Log("[OK] Installer ExitCode=0 — thành công");
            }
            else if (exitCode == 1)
            {
                Log("[INFO] ExitCode=1 — driver có thể đã cài trước đó (already installed)");
                // Still counts as success
            }
            else
            {
                Log($"[ERROR] Installer ExitCode={exitCode}");
                if (isWin11)
                    Log("[HINT] Win11 có thể block unsigned driver. Thử: bcdedit /set testsigning on → restart");
                return InstallResult.Failed;
            }

            // ── STEP H: Verify dll is in app directory ──
            if (!File.Exists(DllPath))
            {
                Log("interception.dll chưa có trong app dir — copy từ temp");
                string tempDll = Path.Combine(Path.GetTempPath(), "interception.dll");
                if (File.Exists(tempDll))
                    File.Copy(tempDll, DllPath);
                else
                    Log("[WARN] interception.dll không có trong temp — driver cài nhưng DLL chưa sẵn sàng");
            }

            // ── STEP I: Post-install verification ──
            bool driverNowInstalled = IsDriverInstalled();
            Log($"Post-install check: IsDriverInstalled={driverNowInstalled}, DllExists={File.Exists(DllPath)}");

            Report("Cài đặt hoàn tất! Cần restart máy.");
            return InstallResult.NeedRestart;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Log("[INFO] User bấm No trên UAC prompt");
            return InstallResult.UserCancelled;
        }
        catch (Exception ex)
        {
            Log($"[ERROR] {ex.GetType().Name}: {ex.Message}");
            Log($"[ERROR] Stack: {ex.StackTrace}");
            return InstallResult.Failed;
        }
    }

    /// <summary>
    /// Download file from URL to disk if not present.
    /// </summary>
    private static async Task<bool> DownloadFileAsync(string url, string outputPath)
    {
        if (File.Exists(outputPath)) return true;
        try
        {
            Log($"Downloading: {url}");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            var bytes = await http.GetByteArrayAsync(url);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllBytesAsync(outputPath, bytes);
            Log($"Downloaded: {outputPath} ({bytes.Length / 1024}KB)");
            return true;
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Download failed: {ex.Message}");
            return false;
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
                              ?? Assembly.GetExecutingAssembly().Location,
            Verb            = "runas",
            UseShellExecute = true
        };
        Process.Start(psi);
        Application.Current.Shutdown();
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

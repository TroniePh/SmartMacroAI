// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace SmartMacroAI.Core;

/// <summary>
/// Detects whether a window belongs to a game with anti-cheat protection.
/// Uses process name matching, anti-cheat DLL scanning, and window style heuristics.
/// </summary>
public static class GameWindowDetector
{
    // Known game process names with anti-cheat
    private static readonly HashSet<string> KnownGameProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        // MapleStory
        "MapleStory", "MapleStoryT", "msw",
        // PUBG
        "TslGame", "PUBG",
        // Games with HackShield
        "BlackShot", "Audition", "Cabal", "FlyFF", "Ragnarok",
        "PristonTale", "GunZ", "Silkroad", "KalOnline",
        // Games with GameGuard
        "Lineage", "Aion", "Blade", "ArcheAge",
        "DNF", "DungeonFighter", "Sudden",
        // Games with EasyAntiCheat
        "FortniteClient", "RustClient", "NewWorld",
        // Games with BattlEye
        "arma3", "DayZ", "Escape",
    };

    // Anti-cheat DLLs commonly injected into game processes
    private static readonly string[] AntiCheatModules =
    {
        "hackshield", "gameguard", "nprotect",
        "easyanticheat", "battleye", "xigncode",
        "mhyprot", "vgk", "faceit"
    };

    /// <summary>
    /// Analyzes a window handle and returns whether it belongs to a game.
    /// </summary>
    public static GameDetectResult Detect(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return GameDetectResult.NotGame;

        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return GameDetectResult.NotGame;

            using var process = Process.GetProcessById((int)pid);
            string processName = process.ProcessName;

            // CHECK 1: Process name matches known games
            if (KnownGameProcesses.Contains(processName))
                return GameDetectResult.KnownGame;

            // CHECK 2: Scan loaded modules for anti-cheat DLLs
            try
            {
                foreach (ProcessModule module in process.Modules)
                {
                    string modName = Path.GetFileNameWithoutExtension(module.ModuleName)
                                        .ToLowerInvariant();
                    foreach (var acName in AntiCheatModules)
                        if (modName.Contains(acName))
                            return GameDetectResult.DetectedAntiCheat;
                }
            }
            catch { /* Process may deny module access — skip */ }

            // CHECK 3: Window style — games often use fullscreen/borderless
            long style   = GetWindowLong(hwnd, GWL_STYLE);
            long exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            bool isFullscreenStyle =
                (style & WS_POPUP) != 0 &&
                (exStyle & WS_EX_TOPMOST) != 0;

            if (isFullscreenStyle)
                return GameDetectResult.LikelyGame;

            // CHECK 4: Executable name heuristic
            string exePath = process.MainModule?.FileName ?? "";
            string exeName = Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
            bool looksLikeGame =
                exeName.Contains("game") ||
                exeName.Contains("client") ||
                exeName.Contains("launcher");

            if (looksLikeGame)
                return GameDetectResult.LikelyGame;

            return GameDetectResult.NotGame;
        }
        catch
        {
            return GameDetectResult.NotGame;
        }
    }

    /// <summary>Quick check: is this window a game?</summary>
    public static bool IsGame(IntPtr hwnd) =>
        Detect(hwnd) != GameDetectResult.NotGame;

    // P/Invoke
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern long GetWindowLong(IntPtr hWnd, int nIndex);

    private const int GWL_STYLE   = -16;
    private const int GWL_EXSTYLE = -20;
    private const long WS_POPUP      = 0x80000000L;
    private const long WS_EX_TOPMOST = 0x00000008L;
}

/// <summary>Result of game window detection.</summary>
public enum GameDetectResult
{
    NotGame,
    LikelyGame,
    KnownGame,
    DetectedAntiCheat
}

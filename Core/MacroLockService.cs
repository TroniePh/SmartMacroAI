// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System.Security.Cryptography;
using System.Text;
using SmartMacroAI.Localization;
using SmartMacroAI.Models;

namespace SmartMacroAI.Core;

public static class MacroLockService
{
    public static string HashPassword(string password)
    {
        string salted = $"SmartMacroAI_{password}_salt";
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(salted));
        return Convert.ToHexString(bytes);
    }

    public static bool Verify(string password, string hash)
        => HashPassword(password) == hash;

    public static bool IsLocked(MacroScript? script)
        => !string.IsNullOrEmpty(script?.PasswordHash);

    public static string GetLockStatus(MacroScript? script)
        => IsLocked(script) ? LanguageManager.GetString("ui_Lock_Locked") : LanguageManager.GetString("ui_Lock_Unlocked");
}

// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System.Windows;
using System.Windows.Input;
using SmartMacroAI.Core;
using SmartMacroAI.Localization;
using SmartMacroAI.Models;

namespace SmartMacroAI;

public partial class MacroLockDialog : Window
{
    public string? NewPasswordHash { get; private set; }
    public bool RemoveLock { get; private set; }

    public MacroLockDialog(MacroScript script)
    {
        InitializeComponent();
        if (!string.IsNullOrEmpty(script.PasswordHash))
        {
            BtnRemoveLock.Visibility = Visibility.Visible;
        }
    }

    private void BtnSetLock_Click(object sender, RoutedEventArgs e)
    {
        string pwd = PwdNew.Password;
        string confirm = PwdConfirm.Password;

        if (string.IsNullOrWhiteSpace(pwd))
        {
            TxtError.Text = LanguageManager.GetString("ui_Lock_EmptyPwd");
            return;
        }

        if (pwd.Length < 4)
        {
            TxtError.Text = LanguageManager.GetString("ui_Lock_PwdTooShort");
            return;
        }

        if (pwd != confirm)
        {
            TxtError.Text = LanguageManager.GetString("ui_Lock_PwdMismatch");
            return;
        }

        NewPasswordHash = MacroLockService.HashPassword(pwd);
        RemoveLock = false;
        if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) DialogResult = true; else Close();
    }

    private void BtnRemoveLock_Click(object sender, RoutedEventArgs e)
    {
        RemoveLock = true;
        if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) DialogResult = true; else Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) DialogResult = false; else Close();
    }
}

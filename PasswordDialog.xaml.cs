// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System.Windows;
using System.Windows.Input;
using SmartMacroAI.Localization;

namespace SmartMacroAI;

public partial class PasswordDialog : Window
{
    public string Password => PwdInput?.Password ?? "";

    public PasswordDialog(string prompt = "")
    {
        InitializeComponent();
        if (!string.IsNullOrEmpty(prompt))
            TxtPrompt.Text = prompt;
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(PwdInput.Password))
        {
            TxtError.Text = LanguageManager.GetString("ui_Pwd_EnterPwd");
            return;
        }
        if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) DialogResult = true; else Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) DialogResult = false; else Close();
    }

    private void PwdInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) BtnConfirm_Click(sender, e);
    }
}

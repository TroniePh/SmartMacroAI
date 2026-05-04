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
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

    private void PwdInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) BtnConfirm_Click(sender, e);
    }
}

using System.Windows;

namespace SmartMacroAI;

public partial class RenameMacroDialog : Window
{
    public string? NewName { get; private set; }

    public RenameMacroDialog(string currentName)
    {
        InitializeComponent();
        TxtNewName.Text = currentName;
        TxtNewName.SelectAll();
        TxtNewName.Focus();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        NewName = TxtNewName.Text.Trim();
        if (string.IsNullOrWhiteSpace(NewName))
        {
            MessageBox.Show(Localization.LanguageManager.GetString("ui_Msg_EnterName"),
                Localization.LanguageManager.GetString("ui_Rename_Title"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) DialogResult = true; else Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) DialogResult = false; else Close();
    }
}

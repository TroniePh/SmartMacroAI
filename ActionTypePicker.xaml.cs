// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System.Windows;
using System.Windows.Controls;
using SmartMacroAI.Localization;

namespace SmartMacroAI;

public sealed record ActionTypePickItem(string Key, string Label);

public partial class ActionTypePicker : Window
{
    public string? SelectedType { get; private set; }

    public ActionTypePicker()
    {
        InitializeComponent();
        LstTypes.ItemsSource = new ActionTypePickItem[]
        {
            new("Click", LanguageManager.GetString("ui_Action_Click")),
            new("TypeText", LanguageManager.GetString("ui_Action_TypeText")),
            new("Wait", LanguageManager.GetString("ui_Action_Wait")),
            new("Repeat", LanguageManager.GetString("ui_Action_Repeat")),
            new("SetVariable", LanguageManager.GetString("ui_Action_SetVariable")),
            new("IfVariable", LanguageManager.GetString("ui_Action_IfVariable")),
            new("Log", LanguageManager.GetString("ui_Action_Log")),
            new("TryCatch", LanguageManager.GetString("ui_Action_TryCatch")),
            new("IfImageFound", LanguageManager.GetString("ui_Action_IfImage")),
            new("IfTextFound", LanguageManager.GetString("ui_Action_IfText")),
            new("OcrRegion", LanguageManager.GetString("ui_Action_OcrRegion")),
            new("ClearVar", LanguageManager.GetString("ui_Action_ClearVar")),
            new("LogVar", LanguageManager.GetString("ui_Action_LogVar")),
            new("WebAction", LanguageManager.GetString("ui_Action_WebAction")),
            new("WebNavigate", LanguageManager.GetString("ui_ActionType_WebNavigate")),
            new("WebClick", LanguageManager.GetString("ui_ActionType_WebClick")),
            new("WebType", LanguageManager.GetString("ui_ActionType_WebType")),
            new("KeyPress", LanguageManager.GetString("ui_Action_KeyPress")),
            new("Telegram", LanguageManager.GetString("ui_Action_Telegram")),
            new("CallMacro", LanguageManager.GetString("ui_Action_CallMacro")),
        };
        LstTypes.DisplayMemberPath = nameof(ActionTypePickItem.Label);
        LstTypes.SelectedValuePath = nameof(ActionTypePickItem.Key);
        if (LstTypes.Items.Count > 0)
            LstTypes.SelectedIndex = 0;
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        if (LstTypes.SelectedValue is string key)
        {
            SelectedType = key;
            DialogResult = true;
        }
    }
}

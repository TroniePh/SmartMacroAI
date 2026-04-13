using System.Windows;
using System.Windows.Controls;

namespace SmartMacroAI;

public sealed record ActionTypePickItem(string Key, string LabelVi);

public partial class ActionTypePicker : Window
{
    public string? SelectedType { get; private set; }

    public ActionTypePicker()
    {
        InitializeComponent();
        LstTypes.ItemsSource = new ActionTypePickItem[]
        {
            new("Click", "Nhấp (Click)"),
            new("TypeText", "Gõ chữ (Type)"),
            new("Wait", "Chờ (Wait)"),
            new("Repeat", "Lặp lại (Repeat)"),
            new("SetVariable", "Gán biến"),
            new("IfVariable", "Nếu biến thỏa mãn"),
            new("Log", "Ghi nhật ký"),
            new("TryCatch", "Bẫy lỗi (Try/Catch)"),
            new("IfImageFound", "Nếu thấy ảnh"),
            new("IfTextFound", "Nếu thấy chữ (OCR)"),
            new("WebAction", "Thao tác web"),
            new("WebNavigate", "Web: Điều hướng"),
            new("WebClick", "Web: Nhấp"),
            new("WebType", "Web: Gõ chữ"),
        };
        LstTypes.DisplayMemberPath = nameof(ActionTypePickItem.LabelVi);
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

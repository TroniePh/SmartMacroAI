// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartMacroAI;

public sealed class ProfileEditDialog : Window
{
    private readonly TextBox _txtProfileId = new();
    private readonly TextBox _txtName = new();
    private readonly TextBox _txtProxyHost = new();
    private readonly TextBox _txtProxyPort = new();
    private readonly TextBox _txtProxyUser = new();
    private readonly PasswordBox _txtProxyPass = new();

    public string ResultProfileId => _txtProfileId.Text.Trim();
    public string ResultName => _txtName.Text.Trim();
    public string ResultProxyHost => _txtProxyHost.Text.Trim();
    public string ResultProxyPort => _txtProxyPort.Text.Trim();
    public string ResultProxyUser => _txtProxyUser.Text.Trim();
    public string ResultProxyPassword => _txtProxyPass.Password.Trim();

    public ProfileEditDialog()
    {
        Title = "AdsPower Profile";
        Width = 500;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E2E"));

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0),
        };

        var panel = new StackPanel { Margin = new Thickness(24) };

        var titleBlock = new TextBlock
        {
            Text = "Thêm / Sửa AdsPower Profile",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CDD6F4")),
            Margin = new Thickness(0, 0, 0, 16),
        };
        panel.Children.Add(titleBlock);

        AddField(panel, "Profile ID *", _txtProfileId, "ID từ AdsPower (user_id trong danh sách profile)");
        AddField(panel, "Tên gợi nhớ", _txtName, "Tên dễ nhớ, ví dụ: Tài khoản Gmail 1");
        panel.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#45475A")),
            Margin = new Thickness(0, 12, 0, 12),
        });

        var proxyTitle = new TextBlock
        {
            Text = "Proxy (tùy chọn)",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A6ADC8")),
            Margin = new Thickness(0, 0, 0, 8),
        };
        panel.Children.Add(proxyTitle);

        var proxyNote = new TextBlock
        {
            Text = "Điền thông tin proxy nếu cần thay đổi IP cho profile này. Để trống = dùng proxy mặc định của AdsPower.",
            FontSize = 11,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6C7086")),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        panel.Children.Add(proxyNote);

        AddField(panel, "Proxy Host", _txtProxyHost, "Ví dụ: proxy.example.com");
        AddField(panel, "Proxy Port", _txtProxyPort, "Ví dụ: 8080");

        var proxyAuth = new Grid();
        proxyAuth.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        proxyAuth.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
        proxyAuth.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        proxyAuth.Margin = new Thickness(0, 0, 0, 12);

        var userBlock = new StackPanel();
        AddField(userBlock, "Proxy User", _txtProxyUser, "Username");
        Grid.SetColumn(userBlock, 0);
        proxyAuth.Children.Add(userBlock);

        var passBlock = new StackPanel();
        AddField(passBlock, "Proxy Password", _txtProxyPass, "Password");
        Grid.SetColumn(passBlock, 2);
        proxyAuth.Children.Add(passBlock);
        panel.Children.Add(proxyAuth);

        panel.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#45475A")),
            Margin = new Thickness(0, 8, 0, 16),
        });

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var btnCancel = new Button
        {
            Content = "Hủy",
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#45475A")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CDD6F4")),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        btnCancel.Click += (_, _) => { DialogResult = false; };

        var btnOk = new Button
        {
            Content = "Lưu",
            Padding = new Thickness(20, 8, 20, 8),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#89B4FA")),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontWeight = FontWeights.SemiBold,
        };
        btnOk.Click += BtnOk_Click;

        btnPanel.Children.Add(btnCancel);
        btnPanel.Children.Add(btnOk);
        panel.Children.Add(btnPanel);

        scroll.Content = panel;
        Content = scroll;
    }

    private static void AddField(StackPanel parent, string label, TextBox textBox, string hint)
    {
        parent.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A6ADC8")),
            Margin = new Thickness(0, 0, 0, 4),
        });
        textBox.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E2E"));
        textBox.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CDD6F4"));
        textBox.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#45475A"));
        textBox.BorderThickness = new Thickness(1);
        textBox.Padding = new Thickness(8, 6, 8, 6);
        textBox.FontSize = 13;
        textBox.CaretBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CDD6F4"));
        textBox.Margin = new Thickness(0, 0, 0, 12);
        parent.Children.Add(textBox);
    }

    private static void AddField(StackPanel parent, string label, PasswordBox passBox, string hint)
    {
        parent.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A6ADC8")),
            Margin = new Thickness(0, 0, 0, 4),
        });
        passBox.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E2E"));
        passBox.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CDD6F4"));
        passBox.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#45475A"));
        passBox.BorderThickness = new Thickness(1);
        passBox.Padding = new Thickness(8, 6, 8, 6);
        passBox.FontSize = 13;
        passBox.Margin = new Thickness(0, 0, 0, 12);
        parent.Children.Add(passBox);
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtProfileId.Text))
        {
            MessageBox.Show("Profile ID không được để trống.", "Thiếu thông tin",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }
}

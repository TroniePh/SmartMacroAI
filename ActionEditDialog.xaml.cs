using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using SmartMacroAI.Models;

namespace SmartMacroAI;

public partial class ActionEditDialog : Window
{
    private readonly MacroAction _action;
    private readonly Dictionary<string, TextBox> _fields = [];
    private readonly Dictionary<string, CheckBox> _checkFields = [];
    private readonly Dictionary<string, ComboBox> _comboFields = [];
    private readonly Dictionary<string, Slider> _sliders = [];

    private static readonly Brush LabelBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A6ADC8"));
    private static readonly Brush InputBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E2E"));
    private static readonly Brush InputFg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CDD6F4"));
    private static readonly Brush InputBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#45475A"));
    private static readonly Brush AccentBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#89B4FA"));

    public ActionEditDialog(MacroAction action)
    {
        InitializeComponent();
        _action = action;
        TxtDialogTitle.Text = $"Chỉnh sửa: {action.DisplayName}";
        BuildFields();
    }

    private void BuildFields()
    {
        switch (_action)
        {
            case ClickAction c:
                AddField("X", c.X.ToString(), displayCaption: "Tọa độ X");
                AddField("Y", c.Y.ToString(), displayCaption: "Tọa độ Y");
                AddCheckField("IsRightClick", c.IsRightClick, "Nhấp chuột phải thay vì trái");
                break;
            case TypeAction t:
                AddField("Text", t.Text, displayCaption: "Nội dung gõ");
                AddField("KeyDelayMs", t.KeyDelayMs.ToString(), displayCaption: "Độ trễ giữa các phím (ms)");
                break;
            case WaitAction w:
                AddField("Milliseconds", (w.DelayMin == w.DelayMax ? w.DelayMin : w.Milliseconds).ToString(), displayCaption: "Thời gian chờ cố định (ms)");
                AddField("WaitForImage", w.WaitForImage, browse: true, displayCaption: "Chờ ảnh mẫu (đường dẫn)");
                AddField("WaitThreshold", w.WaitThreshold.ToString("F2"), displayCaption: "Ngưỡng khớp ảnh");
                AddField("WaitTimeoutMs", w.WaitTimeoutMs.ToString(), displayCaption: "Hết thời chờ tối đa (ms)");
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = "Để trống \"Chờ ảnh mẫu\" nếu chỉ cần chờ cố định (ms). Khi có đường dẫn, macro sẽ quét cho đến khi thấy ảnh hoặc hết thời gian.",
                    Foreground = LabelBrush,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });
                if (w.DelayMin != w.DelayMax)
                {
                    FieldsPanel.Children.Add(new TextBlock
                    {
                        Text = $"Lưu ý: kịch bản cũ đang dùng chờ ngẫu nhiên ({w.DelayMin}–{w.DelayMax} ms). Lưu từ hộp thoại này sẽ chuyển sang một giá trị cố định ở trên.",
                        Foreground = AccentBrush,
                        FontSize = 10,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 8, 0, 0),
                    });
                }
                break;
            case RepeatAction rep:
                AddField("RepeatCount", rep.RepeatCount.ToString(), displayCaption: "Số lần lặp (0 = vô hạn)");
                AddField("IntervalMs", rep.IntervalMs.ToString(), displayCaption: "Khoảng cách giữa các lần (ms)");
                AddField("BreakIfImagePath", rep.BreakIfImagePath, browse: true, displayCaption: "Ảnh thoát vòng lặp (tuỳ chọn)");
                AddSliderField("BreakThreshold", rep.BreakThreshold, 0.5, 1.0, "Ngưỡng ảnh thoát (0,5–1,0)");
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = "Số lần lặp = 0 nghĩa là lặp vô hạn. Các bước trong vòng lặp chỉnh trên sơ đồ macro, không phải ở đây.",
                    Foreground = LabelBrush,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });
                break;
            case IfImageAction img:
                AddField("ImagePath", img.ImagePath, browse: true, displayCaption: "Đường dẫn ảnh mẫu");
                AddField("Threshold", img.Threshold.ToString("F2"), displayCaption: "Ngưỡng khớp");
                AddCheckField("ClickOnFound", img.ClickOnFound,
                    "Tự nhấp vào tâm ảnh khi tìm thấy");
                AddField("RandomOffset", img.RandomOffset.ToString(), displayCaption: "Độ lệch ngẫu nhiên (px)");
                AddField("TimeoutMs", img.TimeoutMs.ToString(), displayCaption: "Hết thời chờ (ms)");
                AddRoiExpander(img);
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = "Độ lệch: ± pixel khi nhấp stealth. Hết thời chờ: thời gian tối đa trước nhánh Else (0 = thử một lần).",
                    Foreground = LabelBrush,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });
                break;
            case IfTextAction txt:
                AddField("Text", txt.Text, displayCaption: "Chuỗi cần nhận dạng");
                AddCheckField("IgnoreCase", txt.IgnoreCase, "Không phân biệt hoa thường");
                AddCheckField("PartialMatch", txt.PartialMatch, "Khớp nếu chuỗi xuất hiện ở bất kỳ đâu (chuỗi con)");
                break;
            case WebAction wa:
                AddComboField("ActionType",
                    ["Navigate", "Click", "Type", "Scrape"],
                    wa.ActionType.ToString(),
                    displayCaption: "Loại thao tác web");
                AddField("Url", wa.Url, displayCaption: "URL");
                AddField("Selector", wa.Selector, displayCaption: "Bộ chọn (CSS / xpath=)");
                AddField("TextToType", wa.TextToType, displayCaption: "Nội dung gõ (Type)");
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = "Điều hướng: điền URL. Nhấp/Gõ/Trích: điền bộ chọn. Gõ: thêm nội dung cần gõ.",
                    Foreground = LabelBrush,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 0),
                });
                break;
            case WebNavigateAction wn:
                AddField("Url", wn.Url, displayCaption: "URL");
                break;
            case WebClickAction wc:
                AddField("CssSelector", wc.CssSelector, displayCaption: "Bộ chọn CSS");
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = "Dùng CSS (#id, .class) hoặc Playwright text= / xpath=.",
                    Foreground = LabelBrush,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });
                break;
            case WebTypeAction wt:
                AddField("CssSelector", wt.CssSelector, displayCaption: "Bộ chọn CSS");
                AddField("TextToType", wt.TextToType, displayCaption: "Nội dung gõ");
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = "Trường được xoá rồi điền (FillAsync).",
                    Foreground = LabelBrush,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });
                break;
            case SetVariableAction sv:
                AddField("VarName", sv.VarName, displayCaption: "Tên biến");
                AddField("Value", sv.Value, displayCaption: "Giá trị");
                AddComboFieldTagged("Operation",
                [
                    ("Set", "Thiết lập (Set)"),
                    ("Increment", "Tăng (Increment)"),
                    ("Decrement", "Giảm (Decrement)"),
                ], sv.Operation, "Thao tác");
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = "Giá trị hỗ trợ {biến_khác} và ${biến_kịch_bản} (thay khi chạy).",
                    Foreground = LabelBrush,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });
                break;
            case IfVariableAction iv:
                AddField("VarName", iv.VarName, displayCaption: "Tên biến");
                AddComboField("CompareOp", ["==", "!=", ">", "<", ">=", "<="], iv.CompareOp, displayCaption: "Toán tử so sánh");
                AddField("Value", iv.Value, displayCaption: "Giá trị so sánh");
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = "Nhánh THỎA MÃN / TRÁI LẠI chỉnh trên sơ đồ macro.",
                    Foreground = LabelBrush,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });
                break;
            case LogAction lg:
                AddField("Message", lg.Message, displayCaption: "Nội dung ghi");
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = "Hỗ trợ {tên_biến} và ${biến_kịch_bản}.",
                    Foreground = LabelBrush,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });
                break;
            case TryCatchAction:
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = "Khối THỬ NGHIỆM và XỬ LÝ LỖI chỉnh trên sơ đồ macro (không phải ở đây).",
                    Foreground = LabelBrush,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 0),
                });
                break;
        }
    }

    private void AddField(string fieldKey, string value, bool browse = false, string? displayCaption = null)
    {
        string header = string.IsNullOrEmpty(displayCaption) ? fieldKey.ToUpperInvariant() : displayCaption;
        FieldsPanel.Children.Add(new TextBlock
        {
            Text = header,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = LabelBrush,
            Margin = new Thickness(0, 8, 0, 4),
        });

        var textBox = new TextBox
        {
            Text = value,
            Background = InputBg,
            Foreground = InputFg,
            BorderBrush = InputBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 12,
            CaretBrush = InputFg,
        };
        _fields[fieldKey] = textBox;

        if (browse)
        {
            var panel = new DockPanel();
            var btn = new Button
            {
                Content = "...",
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#313244")),
                Foreground = InputFg,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10, 6, 10, 6),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(4, 0, 0, 0),
            };
            btn.Click += (_, _) =>
            {
                var dlg = new OpenFileDialog { Filter = "Ảnh|*.png;*.jpg;*.jpeg;*.bmp|Tất cả|*.*" };
                if (dlg.ShowDialog() == true) textBox.Text = dlg.FileName;
            };
            DockPanel.SetDock(btn, Dock.Right);
            panel.Children.Add(btn);
            panel.Children.Add(textBox);
            FieldsPanel.Children.Add(panel);
        }
        else
        {
            FieldsPanel.Children.Add(textBox);
        }
    }

    private void AddCheckField(string key, bool value, string description)
    {
        var cb = new CheckBox
        {
            IsChecked = value,
            Foreground = InputFg,
            Margin = new Thickness(0, 10, 0, 2),
            Content = new TextBlock
            {
                Text = description,
                Foreground = InputFg,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
            },
        };
        _checkFields[key] = cb;
        FieldsPanel.Children.Add(cb);
    }

    private void AddRoiExpander(IfImageAction img)
    {
        var exp = new Expander
        {
            Header = "\U0001F50D Vùng tìm (ROI) — tuỳ chọn, để trống = toàn cửa sổ",
            IsExpanded = false,
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = InputFg,
        };

        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        for (int i = 0; i < 4; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        string[] labels = ["X", "Y", "Rộng", "Cao"];
        for (int i = 0; i < 4; i++)
        {
            var labelTb = new TextBlock
            {
                Text = labels[i],
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = LabelBrush,
                FontSize = 11,
            };
            Grid.SetRow(labelTb, 0);
            Grid.SetColumn(labelTb, i);
            grid.Children.Add(labelTb);
        }

        string[] keys = ["RoiX", "RoiY", "RoiWidth", "RoiHeight"];
        string[] vals =
        [
            img.RoiX?.ToString() ?? "",
            img.RoiY?.ToString() ?? "",
            img.RoiWidth?.ToString() ?? "",
            img.RoiHeight?.ToString() ?? "",
        ];
        for (int i = 0; i < 4; i++)
        {
            var tb = new TextBox
            {
                Text = vals[i],
                Margin = new Thickness(2),
                Background = InputBg,
                Foreground = InputFg,
                BorderBrush = InputBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 12,
                CaretBrush = InputFg,
            };
            Grid.SetRow(tb, 1);
            Grid.SetColumn(tb, i);
            _fields[keys[i]] = tb;
            grid.Children.Add(tb);
        }

        exp.Content = grid;
        FieldsPanel.Children.Add(exp);
    }

    private void AddSliderField(string dictKey, double value, double minimum, double maximum, string displayCaption)
    {
        FieldsPanel.Children.Add(new TextBlock
        {
            Text = displayCaption,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = LabelBrush,
            Margin = new Thickness(0, 8, 0, 4),
        });

        var slider = new Slider
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = value,
            TickFrequency = 0.05,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = InputFg,
        };
        _sliders[dictKey] = slider;
        FieldsPanel.Children.Add(slider);
    }

    private void AddComboField(string fieldKey, string[] options, string selectedValue, string? displayCaption = null)
    {
        FieldsPanel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(displayCaption) ? fieldKey.ToUpperInvariant() : displayCaption,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = LabelBrush,
            Margin = new Thickness(0, 8, 0, 4),
        });

        var combo = new ComboBox
        {
            ItemsSource = options,
            SelectedItem = selectedValue,
            Background = InputBg,
            Foreground = InputFg,
            BorderBrush = InputBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 12,
        };
        _comboFields[fieldKey] = combo;
        FieldsPanel.Children.Add(combo);
    }

    /// <summary>Combo whose <see cref="ComboBoxItem.Tag"/> holds the machine value (e.g. Set/Increment).</summary>
    private void AddComboFieldTagged(string fieldKey, (string value, string labelVi)[] options, string selectedValue, string displayCaption)
    {
        FieldsPanel.Children.Add(new TextBlock
        {
            Text = displayCaption,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = LabelBrush,
            Margin = new Thickness(0, 8, 0, 4),
        });

        var combo = new ComboBox
        {
            Background = InputBg,
            Foreground = InputFg,
            BorderBrush = InputBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 12,
        };
        foreach (var o in options)
            combo.Items.Add(new ComboBoxItem { Content = o.labelVi, Tag = o.value });

        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem it && it.Tag as string == selectedValue)
            {
                combo.SelectedItem = it;
                break;
            }
        }

        if (combo.SelectedItem is null && combo.Items.Count > 0)
            combo.SelectedIndex = 0;

        _comboFields[fieldKey] = combo;
        FieldsPanel.Children.Add(combo);
    }

    private static int? ParseOptionalInt(string raw)
    {
        string s = raw.Trim();
        return string.IsNullOrEmpty(s) ? null : int.Parse(s);
    }

    private string GetFieldValue(string key) => _fields.TryGetValue(key, out var tb) ? tb.Text.Trim() : "";
    private bool GetCheckValue(string key) => _checkFields.TryGetValue(key, out var cb) && cb.IsChecked == true;
    private string GetComboValue(string key)
    {
        if (!_comboFields.TryGetValue(key, out ComboBox? cb))
            return "";
        return cb.SelectedItem switch
        {
            ComboBoxItem { Tag: string tag } => tag,
            string s => s,
            _ => cb.SelectedItem?.ToString() ?? "",
        };
    }

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            switch (_action)
            {
                case ClickAction c:
                    c.X = int.Parse(GetFieldValue("X"));
                    c.Y = int.Parse(GetFieldValue("Y"));
                    c.IsRightClick = GetCheckValue("IsRightClick");
                    break;
                case TypeAction t:
                    t.Text = GetFieldValue("Text");
                    t.KeyDelayMs = int.Parse(GetFieldValue("KeyDelayMs"));
                    break;
                case WaitAction w:
                    w.Milliseconds = int.Parse(GetFieldValue("Milliseconds"));
                    w.WaitForImage = GetFieldValue("WaitForImage");
                    w.WaitThreshold = double.Parse(GetFieldValue("WaitThreshold"));
                    w.WaitTimeoutMs = int.Parse(GetFieldValue("WaitTimeoutMs"));
                    w.DelayMin = w.DelayMax = w.Milliseconds;
                    break;
                case RepeatAction rep:
                    rep.RepeatCount = int.Parse(GetFieldValue("RepeatCount"));
                    rep.IntervalMs = int.Parse(GetFieldValue("IntervalMs"));
                    rep.BreakIfImagePath = GetFieldValue("BreakIfImagePath");
                    if (_sliders.TryGetValue("BreakThreshold", out var breakSl))
                        rep.BreakThreshold = breakSl.Value;
                    break;
                case IfImageAction img:
                    img.ImagePath = GetFieldValue("ImagePath");
                    img.Threshold = double.Parse(GetFieldValue("Threshold"));
                    img.ClickOnFound = GetCheckValue("ClickOnFound");
                    var ro = GetFieldValue("RandomOffset");
                    img.RandomOffset = string.IsNullOrWhiteSpace(ro)
                        ? 3
                        : Math.Clamp(int.Parse(ro), 0, 64);
                    var to = GetFieldValue("TimeoutMs");
                    img.TimeoutMs = string.IsNullOrWhiteSpace(to) ? 5000 : Math.Max(0, int.Parse(to));
                    img.RoiX = ParseOptionalInt(GetFieldValue("RoiX"));
                    img.RoiY = ParseOptionalInt(GetFieldValue("RoiY"));
                    img.RoiWidth = ParseOptionalInt(GetFieldValue("RoiWidth"));
                    img.RoiHeight = ParseOptionalInt(GetFieldValue("RoiHeight"));
                    break;
                case IfTextAction txt:
                    txt.Text = GetFieldValue("Text");
                    txt.IgnoreCase = GetCheckValue("IgnoreCase");
                    txt.PartialMatch = GetCheckValue("PartialMatch");
                    break;
                case WebAction wa:
                    if (Enum.TryParse<WebActionType>(GetComboValue("ActionType"), out var at))
                        wa.ActionType = at;
                    wa.Url = GetFieldValue("Url");
                    wa.Selector = GetFieldValue("Selector");
                    wa.TextToType = GetFieldValue("TextToType");
                    break;
                case WebNavigateAction wn:
                    wn.Url = GetFieldValue("Url");
                    break;
                case WebClickAction wc:
                    wc.CssSelector = GetFieldValue("CssSelector");
                    break;
                case WebTypeAction wt:
                    wt.CssSelector = GetFieldValue("CssSelector");
                    wt.TextToType = GetFieldValue("TextToType");
                    break;
                case SetVariableAction sv:
                    sv.VarName = GetFieldValue("VarName");
                    sv.Value = GetFieldValue("Value");
                    sv.Operation = GetComboValue("Operation");
                    break;
                case IfVariableAction iv:
                    iv.VarName = GetFieldValue("VarName");
                    iv.CompareOp = GetComboValue("CompareOp");
                    iv.Value = GetFieldValue("Value");
                    break;
                case LogAction lg:
                    lg.Message = GetFieldValue("Message");
                    break;
                case TryCatchAction:
                    break;
            }
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Dữ liệu không hợp lệ: {ex.Message}", "Lỗi nhập liệu",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SmartMacroAI.Core;
using SmartMacroAI.Models;

namespace SmartMacroAI;

public partial class ActionEditDialog : Window
{
    public event Action<string>? Log;

    private readonly MacroAction _action;
    private readonly IntPtr _targetHwnd;
    private readonly Dictionary<string, TextBox> _fields = [];
    private readonly Dictionary<string, PasswordBox> _passFields = [];
    private readonly Dictionary<string, CheckBox> _checkFields = [];
    private readonly Dictionary<string, ComboBox> _comboFields = [];
    private readonly Dictionary<string, Slider> _sliders = [];

    private static readonly Brush LabelBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A6ADC8"));
    private static readonly Brush InputBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E2E"));
    private static readonly Brush InputFg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CDD6F4"));
    private static readonly Brush InputBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#45475A"));
    private static readonly Brush AccentBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#89B4FA"));

    public ActionEditDialog(MacroAction action) : this(action, IntPtr.Zero) { }
    public ActionEditDialog(MacroAction action, IntPtr targetHwnd)
    {
        InitializeComponent();
        _action = action;
        _targetHwnd = targetHwnd;
        TxtDialogTitle.Text = string.Format(Localization.LanguageManager.GetString("ui_ActionEdit_EditPrefix"), action.DisplayName);
        BuildFields();
    }

    private void BuildFields()
    {
        switch (_action)
        {
            case ClickAction c:
                AddFieldWithPickerButton("X", c.X.ToString(), Localization.LanguageManager.GetString("ui_ActionEdit_CoordX"));
                AddFieldWithPickerButton("Y", c.Y.ToString(), Localization.LanguageManager.GetString("ui_ActionEdit_CoordY"));
                AddMouseButtonSelector("MouseButton", c.Button);
                AddClickModeSelector("ClickMode", c.Mode);
                break;
            case TypeAction t:
                AddField("Text", t.Text, displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_TypeContent"));
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = Localization.LanguageManager.GetString("ui_ActionEdit_InputMethod"),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = LabelBrush,
                    Margin = new Thickness(0, 8, 0, 4),
                });

                var clipboardRb = new System.Windows.Controls.RadioButton
                {
                    Content = Localization.LanguageManager.GetString("ui_ActionEdit_Clipboard"),
                    Foreground = InputFg,
                    IsChecked = t.InputMethod == TypeInputMethod.Clipboard,
                    Margin = new Thickness(0, 2, 0, 2),
                    Tag = "Clipboard"
                };
                var wmcharRb = new System.Windows.Controls.RadioButton
                {
                    Content = Localization.LanguageManager.GetString("ui_ActionEdit_WmChar"),
                    Foreground = InputFg,
                    IsChecked = t.InputMethod == TypeInputMethod.WmChar,
                    Margin = new Thickness(0, 2, 0, 2),
                    Tag = "WmChar"
                };
                FieldsPanel.Children.Add(clipboardRb);
                FieldsPanel.Children.Add(wmcharRb);

                AddField("KeyDelayMs", t.KeyDelayMs.ToString(), displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_KeyDelay"));
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = Localization.LanguageManager.GetString("ui_ActionEdit_KeyDelayHint"),
                    Foreground = LabelBrush,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });
                break;
            case WaitAction w:
                AddField("Milliseconds", (w.DelayMin == w.DelayMax ? w.DelayMin : w.Milliseconds).ToString(), displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_WaitFixed"));
                AddField("WaitForImage", w.WaitForImage, browse: true, displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_WaitImage"));
                AddField("WaitThreshold", w.WaitThreshold.ToString("F2"), displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_WaitThreshold"));
                AddField("WaitTimeoutMs", w.WaitTimeoutMs.ToString(), displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_Timeout"));
                AddField("WaitForOcrContains", w.WaitForOcrContains, displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_WaitOcr"));
                AddField("OcrRegionX", w.OcrRegionX.ToString(), displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_OcrX"));
                AddField("OcrRegionY", w.OcrRegionY.ToString(), displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_OcrY"));
                AddField("OcrRegionWidth", w.OcrRegionWidth.ToString(), displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_OcrW"));
                AddField("OcrRegionHeight", w.OcrRegionHeight.ToString(), displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_OcrH"));
                AddField("OcrPollIntervalMs", w.OcrPollIntervalMs.ToString(), displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_OcrPoll"));
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = Localization.LanguageManager.GetString("ui_ActionEdit_WaitHint"),
                    Foreground = LabelBrush,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });
                if (w.DelayMin != w.DelayMax)
                {
                    FieldsPanel.Children.Add(new TextBlock
                    {
                        Text = string.Format(Localization.LanguageManager.GetString("ui_ActionEdit_RandomDelayNote"), w.DelayMin, w.DelayMax),
                        Foreground = AccentBrush,
                        FontSize = 10,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 8, 0, 0),
                    });
                }
                break;
            case RepeatAction rep:
                AddField("RepeatCount", rep.RepeatCount.ToString(), displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_RepeatCount"));
                AddField("IntervalMs", rep.IntervalMs.ToString(), displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_RepeatInterval"));
                AddField("BreakIfImagePath", rep.BreakIfImagePath, browse: true, displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_BreakImage"));
                AddSliderField("BreakThreshold", rep.BreakThreshold, 0.5, 1.0, Localization.LanguageManager.GetString("ui_ActionEdit_BreakThreshold"));
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = Localization.LanguageManager.GetString("ui_ActionEdit_RepeatHint"),
                    Foreground = LabelBrush,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });
                break;
            case IfImageAction img:
                AddField("ImagePath", img.ImagePath, browse: true, displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_TemplatePath"));
                AddField("Threshold", img.Threshold.ToString("F2"), displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_MatchThreshold"));
                AddCheckField("ClickOnFound", img.ClickOnFound,
                    Localization.LanguageManager.GetString("ui_ActionEdit_ClickOnFound"));
                AddClickModeSelector("IfImageClickMode", img.ClickMode);
                AddField("RandomOffset", img.RandomOffset.ToString(), displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_RandomOffset"));
                AddCheckField("RetryUntilFound", img.RetryUntilFound,
                    Localization.LanguageManager.GetString("ui_ActionEdit_RetryUntilFound"));
                AddRetrySettingsPanel(img);
                AddField("TimeoutMs", img.TimeoutMs.ToString(), displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_Timeout"));
                AddRoiExpander(img);
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = Localization.LanguageManager.GetString("ui_ActionEdit_RetryHint"),
                    Foreground = LabelBrush,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });
                break;
            case IfTextAction txt:
                AddField("Text", txt.Text, displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_OcrText"));
                AddCheckField("IgnoreCase", txt.IgnoreCase, Localization.LanguageManager.GetString("ui_ActionEdit_IgnoreCase"));
                AddCheckField("PartialMatch", txt.PartialMatch, Localization.LanguageManager.GetString("ui_ActionEdit_PartialMatch"));
                break;
            case WebAction wa:
                AddComboField("ActionType",
                    ["Navigate", "Click", "Type", "Scrape"],
                    wa.ActionType.ToString(),
                    displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_WebType"));
                AddField("Url", wa.Url, displayCaption: "URL");
                AddField("Selector", wa.Selector, displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_Selector"));
                AddField("TextToType", wa.TextToType, displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_TypeText"));
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = Localization.LanguageManager.GetString("ui_ActionEdit_WebHint"),
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
                AddField("CssSelector", wc.CssSelector, displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_CssSelector"));
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = Localization.LanguageManager.GetString("ui_ActionEdit_CssHint"),
                    Foreground = LabelBrush,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });
                break;
            case WebTypeAction wt:
                AddField("CssSelector", wt.CssSelector, displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_CssSelector"));
                AddField("TextToType", wt.TextToType, displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_TypeContent"));
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = Localization.LanguageManager.GetString("ui_ActionEdit_FillHint"),
                    Foreground = LabelBrush,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });
                break;
            case SetVariableAction sv:
                AddField("VarName", sv.VarName, displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_VarName"));
                AddField("Value", sv.Value, displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_VarValue"));
                AddComboFieldTagged("ValueSource",
                [
                    ("Manual", Localization.LanguageManager.GetString("ui_ActionEdit_Manual")),
                    ("Clipboard", "Clipboard"),
                ], sv.ValueSource, Localization.LanguageManager.GetString("ui_ActionEdit_ValueSource"));
                AddComboFieldTagged("Operation",
                [
                    ("Set", Localization.LanguageManager.GetString("ui_ActionEdit_Set")),
                    ("Increment", Localization.LanguageManager.GetString("ui_ActionEdit_Increment")),
                    ("Decrement", Localization.LanguageManager.GetString("ui_ActionEdit_Decrement")),
                ], sv.Operation, Localization.LanguageManager.GetString("ui_ActionEdit_Operation"));
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = Localization.LanguageManager.GetString("ui_ActionEdit_VarHint"),
                    Foreground = LabelBrush,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });
                break;
            case IfVariableAction iv:
                AddField("VarName", iv.VarName, displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_VarName"));
                AddComboField("CompareOp", ["==", "!=", "contains", "notcontains", ">", "<", ">=", "<="], iv.CompareOp, displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_CompareOp"));
                AddField("Value", iv.Value, displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_CompareValue"));
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = Localization.LanguageManager.GetString("ui_ActionEdit_BranchHint"),
                    Foreground = LabelBrush,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });
                break;
            case LogAction lg:
                AddField("Message", lg.Message, displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_LogContent"));
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = Localization.LanguageManager.GetString("ui_ActionEdit_LogHint"),
                    Foreground = LabelBrush,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });
                break;
            case KeyPressAction kpa:
                AddKeyPressField(kpa);
                break;
            case TryCatchAction:
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = Localization.LanguageManager.GetString("ui_ActionEdit_TryCatchHint"),
                    Foreground = LabelBrush,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 0),
                });
                break;
            case OcrRegionAction ocr:
                AddField("ScreenX", ocr.ScreenX.ToString(), displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_ScreenX"));
                AddField("ScreenY", ocr.ScreenY.ToString(), displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_ScreenY"));
                AddField("ScreenWidth", ocr.ScreenWidth.ToString(), displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_Width"));
                AddField("ScreenHeight", ocr.ScreenHeight.ToString(), displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_Height"));
                AddField("OutputVariableName", ocr.OutputVariableName, displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_OutputVar"));
                var btnSnip = new Button
                {
                    Content = Localization.LanguageManager.GetString("ui_ActionEdit_PickRegion"),
                    Margin = new Thickness(0, 8, 0, 0),
                    Padding = new Thickness(12, 8, 12, 8),
                    Background = AccentBrush,
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    ToolTip = Localization.LanguageManager.GetString("ui_ActionEdit_RoiTip"),
                };
                btnSnip.Click += (_, _) =>
                {
                    var snip = new SnippingToolWindow();
                    if (snip.ShowDialog() != true)
                        return;
                    System.Drawing.Rectangle r = snip.SelectedScreenRectangle;
                    if (_fields.TryGetValue("ScreenX", out var tbx)) tbx.Text = r.X.ToString();
                    if (_fields.TryGetValue("ScreenY", out var tby)) tby.Text = r.Y.ToString();
                    if (_fields.TryGetValue("ScreenWidth", out var tbw)) tbw.Text = r.Width.ToString();
                    if (_fields.TryGetValue("ScreenHeight", out var tbh)) tbh.Text = r.Height.ToString();
                };
                FieldsPanel.Children.Add(btnSnip);
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = Localization.LanguageManager.GetString("ui_ActionEdit_OcrHint"),
                    Foreground = LabelBrush,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });
                break;
            case ClearVariableAction cv:
                AddField("VarName", cv.VarName, displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_ClearVarName"));
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = Localization.LanguageManager.GetString("ui_ActionEdit_ClearHint"),
                    Foreground = LabelBrush,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });
                break;
            case LogVariableAction lv:
                AddField("VarName", lv.VarName, displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_VarName"));
                break;
            case TelegramAction tg:
                AddFieldPassword("BotToken", tg.BotToken, displayCaption: "Bot Token");
                AddField("ChatId", tg.ChatId, displayCaption: "Chat ID");
                AddFieldMultiLine("Message", tg.Message, displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_TelegramMsg"));
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = Localization.LanguageManager.GetString("ui_ActionEdit_TelegramHint"),
                    Foreground = LabelBrush,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 6, 0, 0),
                });

                var btnTest = new Button
                {
                    Content = Localization.LanguageManager.GetString("ui_ActionEdit_TestNow"),
                    Margin = new Thickness(0, 12, 0, 0),
                    Padding = new Thickness(16, 8, 16, 8),
                    Background = AccentBrush,
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    FontWeight = FontWeights.SemiBold,
                };
                btnTest.Click += async (_, _) => await BtnTestTelegram_Click(tg);
                FieldsPanel.Children.Add(btnTest);

                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = string.Format(Localization.LanguageManager.GetString("ui_ActionEdit_PcTestInfo"), Environment.MachineName),
                    Foreground = LabelBrush,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 6, 0, 0),
                });
                break;
            case CallMacroAction cma:
                AddFieldWithFileBrowse("MacroFilePath", cma.MacroFilePath, Localization.LanguageManager.GetString("ui_ActionEdit_ScriptFile"), "JSON Files|*.json|All Files|*.*");
                AddField("MacroName", cma.MacroName, displayCaption: Localization.LanguageManager.GetString("ui_ActionEdit_MacroName"));
                AddCheckField("PassVariables", cma.PassVariables, Localization.LanguageManager.GetString("ui_ActionEdit_PassVars"));
                AddCheckField("WaitForFinish", cma.WaitForFinish, Localization.LanguageManager.GetString("ui_ActionEdit_WaitChild"));
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = Localization.LanguageManager.GetString("ui_ActionEdit_CallMacroHint"),
                    Foreground = LabelBrush,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 6, 0, 0),
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
                var dlg = new OpenFileDialog { Filter = Localization.LanguageManager.GetString("ui_ActionEdit_ImageFilter") + "|*.png;*.jpg;*.jpeg;*.bmp|" + Localization.LanguageManager.GetString("ui_ActionEdit_AllFilter") + "|*.*" };
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

    private void AddFieldWithFileBrowse(string fieldKey, string value, string displayCaption, string filter)
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

        var panel = new DockPanel();
        var btn = new Button
        {
            Content = Localization.LanguageManager.GetString("ui_ActionEdit_Browse"),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#313244")),
            Foreground = InputFg,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 6, 10, 6),
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(4, 0, 0, 0),
        };
        btn.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = filter };
            if (dlg.ShowDialog() == true)
            {
                textBox.Text = dlg.FileName;
            }
        };
        DockPanel.SetDock(btn, Dock.Right);
        panel.Children.Add(btn);
        panel.Children.Add(textBox);
        FieldsPanel.Children.Add(panel);
    }

    private TextBox? _coordXBox;
    private TextBox? _coordYBox;

    private void AddFieldWithPickerButton(string fieldKey, string value, string displayCaption)
    {
        FieldsPanel.Children.Add(new TextBlock
        {
            Text = displayCaption,
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
        if (fieldKey == "X") _coordXBox = textBox;
        if (fieldKey == "Y") _coordYBox = textBox;

        var panel = new DockPanel();
        var btnPick = new Button
        {
            Content = Localization.LanguageManager.GetString("ui_ActionEdit_PickCoord"),
            Background = AccentBrush,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#11111B")),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 6, 10, 6),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4, 0, 0, 0),
        };
        btnPick.Click += BtnPickCoord_Click;

        DockPanel.SetDock(btnPick, Dock.Right);
        panel.Children.Add(btnPick);
        panel.Children.Add(textBox);
        FieldsPanel.Children.Add(panel);
    }

    private async void BtnPickCoord_Click(object sender, RoutedEventArgs e)
    {
        if (_coordXBox is null && _coordYBox is null) return;

        if (_targetHwnd != IntPtr.Zero)
        {
            Win32Api.ShowWindow(_targetHwnd, Win32Api.SW_RESTORE);
            Win32Api.SetForegroundWindow(_targetHwnd);
            await Task.Delay(300);
        }

        Hide();
        await Task.Delay(200);

        var picker = new CoordinatePickerWindow(_targetHwnd);
        if (picker.ShowDialog() == true)
        {
            var pt = picker.PickedPoint;
            if (_coordXBox != null) _coordXBox.Text = pt.X.ToString();
            if (_coordYBox != null) _coordYBox.Text = pt.Y.ToString();

            // For ClickAction, store the monitor index where the coordinate was captured
            if (_action is ClickAction ca)
            {
                ca.MonitorIndex = picker.PickedMonitorIndex;
            }
        }

        Show();
    }

    private void AddFieldPassword(string fieldKey, string value, string? displayCaption = null)
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

        var passBox = new PasswordBox
        {
            Password = value,
            Background = InputBg,
            Foreground = InputFg,
            BorderBrush = InputBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 12,
            CaretBrush = InputFg,
        };
        _passFields[fieldKey] = passBox;
        FieldsPanel.Children.Add(passBox);
    }

    private void AddFieldMultiLine(string fieldKey, string value, string? displayCaption = null)
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
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 80,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _fields[fieldKey] = textBox;
        FieldsPanel.Children.Add(textBox);
    }

    private async Task BtnTestTelegram_Click(TelegramAction tg)
    {
        string botToken = GetFieldValue("BotToken");
        string chatId = GetFieldValue("ChatId");

        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
        {
            MessageBox.Show(Localization.LanguageManager.GetString("ui_Msg_TelegramEnterCredentials"),
                Localization.LanguageManager.GetString("ui_Msg_InvalidInput"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string testMessage = Localization.LanguageManager.GetString("ui_ActionEdit_TelegramTestMsg");
        Log?.Invoke(Localization.LanguageManager.GetString("ui_ActionEdit_SendingTest"));

        bool ok = await TelegramService.SendAsync(botToken, chatId, testMessage, msg =>
            Dispatcher.Invoke(() => Log?.Invoke(msg)));

        if (ok)
            MessageBox.Show(Localization.LanguageManager.GetString("ui_Msg_TelegramSent"),
                Localization.LanguageManager.GetString("ui_Msg_ShareTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        else
            MessageBox.Show(Localization.LanguageManager.GetString("ui_Msg_TelegramFailed"),
                Localization.LanguageManager.GetString("ui_Msg_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
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
            Header = Localization.LanguageManager.GetString("ui_ActionEdit_RoiHeader"),
            IsExpanded = false,
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = InputFg,
        };

        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        for (int i = 0; i < 4; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        string[] labels = ["X", "Y", Localization.LanguageManager.GetString("ui_ActionEdit_RoiW"), Localization.LanguageManager.GetString("ui_ActionEdit_RoiH")];
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
    private string GetPassFieldValue(string key) => _passFields.TryGetValue(key, out var pb) ? pb.Password.Trim() : "";
    private int GetIntFieldValue(string key) =>
        int.TryParse(GetFieldValue(key), out int val) ? val : 0;
    private bool GetCheckValue(string key) => _checkFields.TryGetValue(key, out var cb) && cb.IsChecked == true;
    private string GetRadioValue(string prefix)
    {
        foreach (var child in FieldsPanel.Children)
        {
            if (child is System.Windows.Controls.RadioButton rb && rb.Tag?.ToString()?.StartsWith(prefix) == true && rb.IsChecked == true)
                return rb.Tag.ToString()!;
        }
        return prefix + "0"; // default
    }

    private KeyInputMode GetInputModeValue()
    {
        foreach (var child in FieldsPanel.Children)
        {
            if (child is System.Windows.Controls.RadioButton rb && rb.IsChecked == true)
            {
                string? tag = rb.Tag as string;
                if (tag == "Auto") return KeyInputMode.Auto;
                if (tag == "SendInput") return KeyInputMode.SendInput;
                if (tag == "RawInput") return KeyInputMode.RawInput;
                if (tag == "DriverLevel") return KeyInputMode.DriverLevel;
            }
        }
        return KeyInputMode.Auto;
    }

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

    private Models.ClickMode GetClickModeValue(string prefix)
    {
        foreach (var child in FieldsPanel.Children)
        {
            if (child is System.Windows.Controls.RadioButton rb && rb.IsChecked == true)
            {
                string? tag = rb.Tag as string;
                if (tag == prefix + "_Stealth") return Models.ClickMode.Stealth;
                if (tag == prefix + "_Raw")     return Models.ClickMode.Raw;
                if (tag == prefix + "_Hardware") return Models.ClickMode.Hardware;
                if (tag == prefix + "_DriverLevel") return Models.ClickMode.DriverLevel;
            }
        }
        return Models.ClickMode.Stealth;
    }

    /// <summary>Builds the Key Catcher TextBox + Clear button for a KeyPressAction.</summary>
    private void AddKeyPressField(KeyPressAction kpa)
    {
        FieldsPanel.Children.Add(new TextBlock
        {
            Text = Localization.LanguageManager.GetString("ui_ActionEdit_KeyPressLabel"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = LabelBrush,
            Margin = new Thickness(0, 8, 0, 4),
        });

        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        var keyBox = new TextBox
        {
            Width = 180,
            IsReadOnly = true,
            Focusable = true,
            IsTabStop = true,
            Text = string.IsNullOrEmpty(kpa.KeyName)
                ? Localization.LanguageManager.GetString("ui_ActionEdit_KeyPressPlaceholder")
                : kpa.KeyName,
            Foreground = string.IsNullOrEmpty(kpa.KeyName)
                ? Brushes.Gray
                : Brushes.White,
            Background = InputBg,
            BorderBrush = InputBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 12,
            CaretBrush = InputFg,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        keyBox.PreviewKeyDown += txtKeyCapture_PreviewKeyDown;
        keyBox.GotFocus += txtKeyCapture_GotFocus;
        keyBox.LostFocus += txtKeyCapture_LostFocus;
        keyBox.PreviewMouseLeftButtonDown += txtKeyCapture_MouseDown;

        _fields["KeyName"] = keyBox;
        _fields["VirtualKeyCode"] = new TextBox { Text = kpa.VirtualKeyCode.ToString() };
        _fields["HoldDurationMs"] = new TextBox { Text = kpa.HoldDurationMs.ToString() };

        var btnClear = new Button
        {
            Content = Localization.LanguageManager.GetString("ui_ActionEdit_ClearBtn"),
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(10, 6, 10, 6),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#313244")),
            Foreground = InputFg,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        btnClear.Click += btnClearKey_Click;

        panel.Children.Add(keyBox);
        panel.Children.Add(btnClear);
        FieldsPanel.Children.Add(panel);

        FieldsPanel.Children.Add(new TextBlock
        {
            Text = Localization.LanguageManager.GetString("ui_ActionEdit_HoldMs"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = LabelBrush,
            Margin = new Thickness(0, 10, 0, 4),
        });

        var holdBox = new TextBox
        {
            Text = kpa.HoldDurationMs.ToString(),
            Width = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = InputBg,
            Foreground = InputFg,
            BorderBrush = InputBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 12,
            CaretBrush = InputFg,
        };
        _fields["HoldDurationMs"] = holdBox;
        FieldsPanel.Children.Add(holdBox);

        FieldsPanel.Children.Add(new TextBlock
        {
            Text = Localization.LanguageManager.GetString("ui_ActionEdit_KeyPressHint"),
            Foreground = LabelBrush,
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        });

        // 3-way Input Mode selector
        FieldsPanel.Children.Add(new TextBlock
        {
            Text = Localization.LanguageManager.GetString("ui_ActionEdit_KeySendMode"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = LabelBrush,
            Margin = new Thickness(0, 12, 0, 4),
        });

        var rbAuto = new System.Windows.Controls.RadioButton
        {
            Content = Localization.LanguageManager.GetString("ui_ActionEdit_AutoStealth"),
            IsChecked = kpa.InputMode == KeyInputMode.Auto,
            Foreground = InputFg,
            Margin = new Thickness(0, 2, 0, 2),
            Tag = "Auto"
        };
        var rbSendInput = new System.Windows.Controls.RadioButton
        {
            Content = Localization.LanguageManager.GetString("str_mode_sendinput_key"),
            IsChecked = kpa.InputMode == KeyInputMode.SendInput,
            Foreground = InputFg,
            Margin = new Thickness(0, 2, 0, 2),
            Tag = "SendInput",
            ToolTip = Localization.LanguageManager.GetString("ui_ActionEdit_SendInputTip")
        };
        var rbRawInput = new System.Windows.Controls.RadioButton
        {
            Content = Localization.LanguageManager.GetString("str_mode_raw_key"),
            IsChecked = kpa.InputMode == KeyInputMode.RawInput,
            Foreground = InputFg,
            Margin = new Thickness(0, 2, 0, 2),
            Tag = "RawInput",
            ToolTip = Localization.LanguageManager.GetString("ui_ActionEdit_RawInputTip")
        };

        FieldsPanel.Children.Add(rbAuto);
        FieldsPanel.Children.Add(rbSendInput);
        FieldsPanel.Children.Add(rbRawInput);

        var rbDriverKey = new System.Windows.Controls.RadioButton
        {
            Content = Localization.LanguageManager.GetString("str_mode_driver_key"),
            IsChecked = kpa.InputMode == KeyInputMode.DriverLevel,
            Foreground = InputFg,
            Margin = new Thickness(0, 2, 0, 2),
            Tag = "DriverLevel",
            ToolTip = Localization.LanguageManager.GetString("ui_ActionEdit_DriverTip")
        };
        rbDriverKey.Checked += (s, e) =>
        {
            if (InterceptionInstaller.IsReady()) return;
            var dialog = new DriverInstallDialog { Owner = Window.GetWindow(this) };
            dialog.ShowDialog();
            if (!dialog.InstallSucceeded)
            {
                rbAuto.IsChecked = true;
            }
        };
        FieldsPanel.Children.Add(rbDriverKey);

        FieldsPanel.Children.Add(new TextBlock
        {
            Text = Localization.LanguageManager.GetString("ui_ActionEdit_ForegroundNote"),
            Foreground = LabelBrush,
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 2, 0, 0),
        });
    }

    private void txtKeyCapture_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        Key pressedKey = e.Key == Key.System ? e.SystemKey : e.Key;

        // Ignore modifier-only keys (they'll fire their own events)
        if (pressedKey is Key.LeftShift or Key.RightShift or
            Key.LeftCtrl or Key.RightCtrl or
            Key.LeftAlt or Key.RightAlt or
            Key.LWin or Key.RWin or Key.System or
            Key.CapsLock or Key.NumLock or Key.Scroll)
            return;

        int vkCode = KeyInterop.VirtualKeyFromKey(pressedKey);

        var keyBox = (TextBox)sender;
        keyBox.Text = pressedKey.ToString();
        keyBox.Foreground = Brushes.White;

        if (_fields.TryGetValue("VirtualKeyCode", out var vkBox))
            vkBox.Text = vkCode.ToString();

        if (_action is KeyPressAction kpa)
        {
            kpa.VirtualKeyCode = vkCode;
            kpa.KeyName = pressedKey.ToString();
        }
    }

    private void txtKeyCapture_GotFocus(object sender, RoutedEventArgs e)
    {
        var keyBox = (TextBox)sender;
        if (keyBox.Text == Localization.LanguageManager.GetString("ui_ActionEdit_KeyPressPlaceholder"))
            keyBox.Text = string.Empty;
        keyBox.Focus();
        Keyboard.Focus(keyBox);
    }

    private void txtKeyCapture_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var keyBox = (TextBox)sender;
        keyBox.Focus();
        Keyboard.Focus(keyBox);
        e.Handled = true;
    }

    private void txtKeyCapture_LostFocus(object sender, RoutedEventArgs e)
    {
        var keyBox = (TextBox)sender;
        if (string.IsNullOrEmpty(keyBox.Text))
        {
            keyBox.Text = Localization.LanguageManager.GetString("ui_ActionEdit_KeyPressPlaceholder");
            keyBox.Foreground = Brushes.Gray;
        }
    }

    private void btnClearKey_Click(object sender, RoutedEventArgs e)
    {
        if (!_fields.TryGetValue("KeyName", out var keyBox))
            return;

        keyBox.Text = Localization.LanguageManager.GetString("ui_ActionEdit_KeyPressPlaceholder");
        keyBox.Foreground = Brushes.Gray;

        if (_fields.TryGetValue("VirtualKeyCode", out var vkBox))
            vkBox.Text = "0";
    }

    // ── ClickMode selector for ClickAction and IfImageAction ───────────────────────
    // Created by Phạm Duy – Giải pháp tự động hóa thông minh.

    private void AddClickModeSelector(string dictKey, Models.ClickMode currentMode)
    {
        FieldsPanel.Children.Add(new TextBlock
        {
            Text = Localization.LanguageManager.GetString("ui_ActionEdit_ClickMode"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = LabelBrush,
            Margin = new Thickness(0, 12, 0, 4),
        });

        var rbStealth = new System.Windows.Controls.RadioButton
        {
            Content = Localization.LanguageManager.GetString("ui_ActionEdit_ClickStealth"),
            IsChecked = currentMode == Models.ClickMode.Stealth,
            Foreground = InputFg,
            Margin = new Thickness(0, 2, 0, 2),
            Tag = dictKey + "_Stealth"
        };
        var rbRaw = new System.Windows.Controls.RadioButton
        {
            Content = Localization.LanguageManager.GetString("ui_ActionEdit_ClickRaw"),
            IsChecked = currentMode == Models.ClickMode.Raw,
            Foreground = InputFg,
            Margin = new Thickness(0, 2, 0, 2),
            Tag = dictKey + "_Raw",
            ToolTip = Localization.LanguageManager.GetString("ui_ActionEdit_ClickRawTip")
        };
        var rbHw = new System.Windows.Controls.RadioButton
        {
            Content = Localization.LanguageManager.GetString("ui_ActionEdit_ClickHw"),
            IsChecked = currentMode == Models.ClickMode.Hardware,
            Foreground = InputFg,
            Margin = new Thickness(0, 2, 0, 2),
            Tag = dictKey + "_Hardware",
            ToolTip = "SetForegroundWindow + SetCursorPos + mouse_event — full hardware click."
        };

        FieldsPanel.Children.Add(rbStealth);
        FieldsPanel.Children.Add(rbRaw);
        FieldsPanel.Children.Add(rbHw);

        var rbDriver = new System.Windows.Controls.RadioButton
        {
            Content = Localization.LanguageManager.GetString("ui_ActionEdit_ClickDriver"),
            IsChecked = currentMode == Models.ClickMode.DriverLevel,
            Foreground = InputFg,
            Margin = new Thickness(0, 2, 0, 2),
            Tag = dictKey + "_DriverLevel",
            ToolTip = Localization.LanguageManager.GetString("ui_ActionEdit_ClickDriverTip")
        };
        rbDriver.Checked += (s, e) =>
        {
            if (InterceptionInstaller.IsReady()) return;
            var dialog = new DriverInstallDialog { Owner = Window.GetWindow(this) };
            dialog.ShowDialog();
            if (!dialog.InstallSucceeded)
            {
                rbStealth.IsChecked = true;
            }
        };
        FieldsPanel.Children.Add(rbDriver);
    }

    // ── MouseButton selector for ClickAction ────────────────────────────

    private void AddMouseButtonSelector(string dictKey, Core.MouseButton current)
    {
        FieldsPanel.Children.Add(new TextBlock
        {
            Text = Localization.LanguageManager.GetString("ui_ActionEdit_MouseButton"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = LabelBrush,
            Margin = new Thickness(0, 12, 0, 4),
        });

        var rbLeft = new System.Windows.Controls.RadioButton
        {
            Content = Localization.LanguageManager.GetString("ui_ActionEdit_MouseLeft"),
            IsChecked = current == Core.MouseButton.Left,
            Foreground = InputFg,
            Margin = new Thickness(0, 2, 0, 2),
            Tag = dictKey + "_Left"
        };
        var rbRight = new System.Windows.Controls.RadioButton
        {
            Content = Localization.LanguageManager.GetString("ui_ActionEdit_MouseRight"),
            IsChecked = current == Core.MouseButton.Right,
            Foreground = InputFg,
            Margin = new Thickness(0, 2, 0, 2),
            Tag = dictKey + "_Right"
        };
        var rbMiddle = new System.Windows.Controls.RadioButton
        {
            Content = Localization.LanguageManager.GetString("ui_ActionEdit_MouseMiddle"),
            IsChecked = current == Core.MouseButton.Middle,
            Foreground = InputFg,
            Margin = new Thickness(0, 2, 0, 2),
            Tag = dictKey + "_Middle"
        };
        FieldsPanel.Children.Add(rbLeft);
        FieldsPanel.Children.Add(rbRight);
        FieldsPanel.Children.Add(rbMiddle);
    }

    private Core.MouseButton GetMouseButtonValue(string prefix)
    {
        foreach (var child in FieldsPanel.Children)
        {
            if (child is System.Windows.Controls.RadioButton rb && rb.IsChecked == true)
            {
                string? tag = rb.Tag as string;
                if (tag == prefix + "_Right") return Core.MouseButton.Right;
                if (tag == prefix + "_Middle") return Core.MouseButton.Middle;
            }
        }
        return Core.MouseButton.Left;
    }

    private void AddRetrySettingsPanel(IfImageAction img)
    {
        var exp = new Expander
        {
            Header = Localization.LanguageManager.GetString("ui_ActionEdit_RetrySettings"),
            IsExpanded = true,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = InputFg,
        };

        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var retryIntervalTb = new TextBox
        {
            Text = img.RetryIntervalMs.ToString(),
            Background = InputBg,
            Foreground = InputFg,
            BorderBrush = InputBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 12,
            CaretBrush = InputFg,
        };
        _fields["RetryIntervalMs"] = retryIntervalTb;

        var maxRetryTb = new TextBox
        {
            Text = img.MaxRetryCount.ToString(),
            Background = InputBg,
            Foreground = InputFg,
            BorderBrush = InputBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 12,
            CaretBrush = InputFg,
        };
        _fields["MaxRetryCount"] = maxRetryTb;

        var lblInterval = new TextBlock
        {
            Text = Localization.LanguageManager.GetString("ui_ActionEdit_RetryInterval"),
            Foreground = LabelBrush,
            FontSize = 11,
            Margin = new Thickness(0, 0, 4, 4),
        };
        Grid.SetRow(lblInterval, 0);
        Grid.SetColumn(lblInterval, 0);
        Grid.SetRow(retryIntervalTb, 1);
        Grid.SetColumn(retryIntervalTb, 0);

        var lblMax = new TextBlock
        {
            Text = Localization.LanguageManager.GetString("ui_ActionEdit_MaxRetry"),
            Foreground = LabelBrush,
            FontSize = 11,
            Margin = new Thickness(8, 0, 4, 4),
        };
        Grid.SetRow(lblMax, 0);
        Grid.SetColumn(lblMax, 1);
        Grid.SetRow(maxRetryTb, 1);
        Grid.SetColumn(maxRetryTb, 1);

        grid.Children.Add(lblInterval);
        grid.Children.Add(retryIntervalTb);
        grid.Children.Add(lblMax);
        grid.Children.Add(maxRetryTb);

        exp.Content = grid;
        FieldsPanel.Children.Add(exp);
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
                    c.Button = GetMouseButtonValue("MouseButton");
                    c.Mode = GetClickModeValue("ClickMode");
                    break;
                case TypeAction t:
                    t.Text = GetFieldValue("Text");
                    t.KeyDelayMs = int.TryParse(GetFieldValue("KeyDelayMs"), out int delay) ? delay : 0;
                    t.InputMethod = GetRadioValue("Clipboard").Contains("Clipboard")
                        ? TypeInputMethod.Clipboard
                        : TypeInputMethod.WmChar;
                    break;
                case WaitAction w:
                    w.Milliseconds = int.Parse(GetFieldValue("Milliseconds"));
                    w.WaitForImage = GetFieldValue("WaitForImage");
                    w.WaitThreshold = double.Parse(GetFieldValue("WaitThreshold"));
                    w.WaitTimeoutMs = int.Parse(GetFieldValue("WaitTimeoutMs"));
                    w.WaitForOcrContains = GetFieldValue("WaitForOcrContains");
                    w.OcrRegionX = int.TryParse(GetFieldValue("OcrRegionX"), out int ox) ? ox : 0;
                    w.OcrRegionY = int.TryParse(GetFieldValue("OcrRegionY"), out int oy) ? oy : 0;
                    w.OcrRegionWidth = int.TryParse(GetFieldValue("OcrRegionWidth"), out int ow) ? ow : 0;
                    w.OcrRegionHeight = int.TryParse(GetFieldValue("OcrRegionHeight"), out int oh) ? oh : 0;
                    w.OcrPollIntervalMs = int.TryParse(GetFieldValue("OcrPollIntervalMs"), out int op) ? Math.Clamp(op, 50, 5000) : 500;
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
                    img.ClickMode = GetClickModeValue("IfImageClickMode");
                    var ro = GetFieldValue("RandomOffset");
                    img.RandomOffset = string.IsNullOrWhiteSpace(ro)
                        ? 3
                        : Math.Clamp(int.Parse(ro), 0, 64);
                    img.RetryUntilFound = GetCheckValue("RetryUntilFound");
                    img.RetryIntervalMs = int.TryParse(GetFieldValue("RetryIntervalMs"), out int ri) ? Math.Max(50, ri) : 500;
                    img.MaxRetryCount = int.TryParse(GetFieldValue("MaxRetryCount"), out int mr) ? mr : 0;
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
                    sv.ValueSource = GetComboValue("ValueSource");
                    if (string.IsNullOrWhiteSpace(sv.ValueSource))
                        sv.ValueSource = "Manual";
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
                case KeyPressAction kpa:
                    kpa.VirtualKeyCode = GetIntFieldValue("VirtualKeyCode");
                    kpa.KeyName = GetFieldValue("KeyName");
                    kpa.HoldDurationMs = GetIntFieldValue("HoldDurationMs");
                    kpa.InputMode = GetInputModeValue();
                    break;
                case TryCatchAction:
                    break;
                case OcrRegionAction ocr:
                    ocr.ScreenX = int.Parse(GetFieldValue("ScreenX"));
                    ocr.ScreenY = int.Parse(GetFieldValue("ScreenY"));
                    ocr.ScreenWidth = int.Parse(GetFieldValue("ScreenWidth"));
                    ocr.ScreenHeight = int.Parse(GetFieldValue("ScreenHeight"));
                    ocr.OutputVariableName = GetFieldValue("OutputVariableName");
                    break;
                case ClearVariableAction cv:
                    cv.VarName = GetFieldValue("VarName");
                    break;
                case LogVariableAction lv:
                    lv.VarName = GetFieldValue("VarName");
                    break;
                case TelegramAction tg:
                    tg.BotToken = GetPassFieldValue("BotToken");
                    tg.ChatId = GetFieldValue("ChatId");
                    tg.Message = GetFieldValue("Message");
                    break;
                case CallMacroAction cma:
                    cma.MacroFilePath = GetFieldValue("MacroFilePath");
                    cma.MacroName = GetFieldValue("MacroName");
                    cma.PassVariables = GetCheckValue("PassVariables");
                    cma.WaitForFinish = GetCheckValue("WaitForFinish");
                    break;
            }
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{Localization.LanguageManager.GetString("ui_Msg_InvalidData")} {ex.Message}", Localization.LanguageManager.GetString("ui_Msg_InvalidInput"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

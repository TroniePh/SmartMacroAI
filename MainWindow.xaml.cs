using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SmartMacroAI.Core;
using SmartMacroAI.Localization;
using SmartMacroAI.Models;
using SmartMacroAI.ViewModels;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace SmartMacroAI;

public partial class MainWindow : Window
{
    private MacroScript _currentScript = new();
    private readonly ObservableCollection<MacroAction> _actions = [];
    private MacroEngine? _macroEngine;
    private CancellationTokenSource? _cts;
    private MacroRecorder? _recorder;
    private int _runsToday;
    private IntPtr _editorTargetHwnd = IntPtr.Zero;

    private Point _dragStartPoint;
    private MacroAction? _potentialDragAction;

    /// <summary>Identifies a step inside a <see cref="RepeatAction.LoopActions"/> list for edit/delete on the canvas.</summary>
    private readonly record struct NestedLoopTag(RepeatAction Parent, int ChildIndex);

    private readonly record struct NestedTryCatchChildTag(TryCatchAction Parent, int ChildIndex, bool IsTry);

    private readonly record struct NestedIfVarChildTag(IfVariableAction Parent, int ChildIndex, bool IsThen);

    private readonly record struct TryCatchInsertTag(TryCatchAction Parent, bool IsTry);

    private readonly record struct IfVarInsertTag(IfVariableAction Parent, bool IsThen);

    private readonly ObservableCollection<DashboardRowVm> _dashboardRows = [];

    private readonly ObservableCollection<VariableLiveRowVm> _dashboardVariableRows = [];

    private DispatcherTimer? _dashboardVariablesTimer;

    private bool _suppressWinRtOcrCombo;

    // ── Hotkey & Tray ──
    private const int HOTKEY_TOGGLE_APP    = 1;
    private const int HOTKEY_TOGGLE_TARGET = 2;
    private HotkeySettings _hotkeySettings = new();
    private HwndSource? _hwndSource;
    private WinForms.NotifyIcon? _trayIcon;
    private bool _appHidden;

    // ── Stealth Tracker (HWND → title) ──
    private readonly Dictionary<IntPtr, string> _hiddenWindows = new();
    private readonly ObservableCollection<StealthWindowVm> _stealthRows = [];

    private string _activeView = "Dashboard";
    private bool _suppressLanguageCombo;

    /// <summary>Last successful vision test match (client coords) for stealth click demo.</summary>
    private Drawing.Point? _visionLastFoundClientPoint;
    private IntPtr _visionLastFoundHwnd = IntPtr.Zero;

    // ── Update Checker ──
    /// <summary>Fallback display / parse if assembly version is unavailable.</summary>
    private const string CurrentVersion   = "v1.2.3";
    private const string GitHubApiUrl     = "https://api.github.com/repos/TroniePh/SmartMacroAI/releases/latest";
    private const string LandingPageUrl   = "https://tronieph.github.io/SmartMacroAI-Website/";
    /// <summary>GitHub rejects API calls without a descriptive User-Agent.</summary>
    private const string GitHubUserAgent  = "SmartMacroAI/UpdateChecker (+https://github.com/TroniePh/SmartMacroAI)";

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    public MainWindow()
    {
        InitializeComponent();
        MacroCanvas.PreviewMouseLeftButtonUp += Workflow_PreviewMouseLeftButtonUp;
        LanguageManager.UiLanguageChanged += OnUiLanguageChanged;
        DashboardGrid.ItemsSource = _dashboardRows;
        StealthGrid.ItemsSource = _stealthRows;
        _hotkeySettings = HotkeySettings.Load();
        InitializeTrayIcon();
        SyncScriptToUi();
        LoadDashboard();
        UpdateProcessBar();
    }

    // ═══════════════════════════════════════════════════
    //  WINDOW LOADED
    // ═══════════════════════════════════════════════════

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _hwndSource?.AddHook(WndProc);
        RegisterHotkeys();
        InitSettingsUi();
        StartAntiDetectionServices();
        DashboardVariablesGrid.ItemsSource = _dashboardVariableRows;
        _dashboardVariablesTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(600),
        };
        _dashboardVariablesTimer.Tick += (_, _) => RefreshDashboardVariablesPanel();
        _dashboardVariablesTimer.Start();
        _ = CheckForUpdatesAsync(silent: true);
    }

    // ═══════════════════════════════════════════════════
    //  GLOBAL HOTKEYS
    // ═══════════════════════════════════════════════════

    private void RegisterHotkeys()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        Win32Api.RegisterHotKey(hwnd, HOTKEY_TOGGLE_APP,
            (uint)_hotkeySettings.ToggleAppModifier, (uint)_hotkeySettings.ToggleAppKey);
        Win32Api.RegisterHotKey(hwnd, HOTKEY_TOGGLE_TARGET,
            (uint)_hotkeySettings.ToggleTargetModifier, (uint)_hotkeySettings.ToggleTargetKey);
        AppendLog($"Hotkeys registered: App={_hotkeySettings.ToggleAppDisplay}, Target={_hotkeySettings.ToggleTargetDisplay}");
    }

    private void UnregisterHotkeys()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        Win32Api.UnregisterHotKey(hwnd, HOTKEY_TOGGLE_APP);
        Win32Api.UnregisterHotKey(hwnd, HOTKEY_TOGGLE_TARGET);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32Api.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HOTKEY_TOGGLE_APP) ToggleAppVisibility();
            else if (id == HOTKEY_TOGGLE_TARGET) ToggleTargetVisibility();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ToggleAppVisibility()
    {
        if (_appHidden)
        {
            Show();
            WindowState = WindowState.Normal;
            ShowInTaskbar = true;
            Win32Api.SetForegroundWindow(new WindowInteropHelper(this).Handle);
            _appHidden = false;
        }
        else
        {
            Hide();
            ShowInTaskbar = false;
            _appHidden = true;
        }
    }

    private void ToggleTargetVisibility()
    {
        string targetTitle = CmbTargetWindow.Text.Trim();
        if (string.IsNullOrWhiteSpace(targetTitle)) return;

        var alreadyHidden = _hiddenWindows
            .FirstOrDefault(kv => kv.Value.Contains(targetTitle, StringComparison.OrdinalIgnoreCase));

        if (alreadyHidden.Key != IntPtr.Zero)
        {
            StealthShowWindow(alreadyHidden.Key);
            Dispatcher.Invoke(() => AppendLog($"Hotkey: restored \"{alreadyHidden.Value}\""));
        }
        else
        {
            IntPtr hwnd = Win32Api.FindWindowByPartialTitle(targetTitle);
            if (hwnd != IntPtr.Zero)
            {
                string title = Win32Api.GetWindowTitle(hwnd);
                StealthHideWindow(hwnd, title);
                Dispatcher.Invoke(() => AppendLog($"Hotkey: hidden \"{title}\""));
            }
        }
    }

    // ═══════════════════════════════════════════════════
    //  STEALTH TRACKER — central hide/show + tray sync
    // ═══════════════════════════════════════════════════

    private void StealthHideWindow(IntPtr hwnd, string title)
    {
        if (_hiddenWindows.ContainsKey(hwnd)) return;
        Win32Api.SetWindowVisibility(hwnd, false);
        _hiddenWindows[hwnd] = title;
        SyncStealthRowState(hwnd, true);
        RebuildTrayMenu();
    }

    private void StealthShowWindow(IntPtr hwnd)
    {
        Win32Api.SetWindowVisibility(hwnd, true);
        _hiddenWindows.Remove(hwnd);
        SyncStealthRowState(hwnd, false);
        RebuildTrayMenu();
    }

    private void SyncStealthRowState(IntPtr hwnd, bool hidden)
    {
        var row = _stealthRows.FirstOrDefault(r => r.Hwnd == hwnd);
        if (row is not null) row.IsHidden = hidden;
    }

    private void ShowAllHiddenWindows()
    {
        int count = 0;
        foreach (var (hwnd, _) in _hiddenWindows.ToList())
        {
            if (Win32Api.IsWindow(hwnd))
            {
                Win32Api.SetWindowVisibility(hwnd, true);
                count++;
            }
        }
        _hiddenWindows.Clear();

        foreach (var row in _dashboardRows)
        {
            if (row.TargetHwnd != IntPtr.Zero && row.StealthMode)
            {
                Win32Api.SetWindowVisibility(row.TargetHwnd, true);
                count++;
            }
        }

        foreach (var sr in _stealthRows) sr.IsHidden = false;
        RebuildTrayMenu();

        if (_appHidden)
        {
            Show();
            WindowState = WindowState.Normal;
            ShowInTaskbar = true;
            _appHidden = false;
        }

        AppendLog($"Emergency: restored {count} hidden window(s) + SmartMacroAI.");
    }

    // ═══════════════════════════════════════════════════
    //  STEALTH MANAGER UI
    // ═══════════════════════════════════════════════════

    private void LoadStealthManager()
    {
        _stealthRows.Clear();
        IntPtr myHwnd = new WindowInteropHelper(this).Handle;

        foreach (var (hwnd, title) in _hiddenWindows)
        {
            _stealthRows.Add(new StealthWindowVm { Hwnd = hwnd, WindowTitle = title, IsHidden = true });
        }

        foreach (var (hwnd, title) in Win32Api.GetAllVisibleWindows())
        {
            if (hwnd == myHwnd) continue;
            if (_hiddenWindows.ContainsKey(hwnd)) continue;
            _stealthRows.Add(new StealthWindowVm { Hwnd = hwnd, WindowTitle = title, IsHidden = false });
        }
    }

    private void StealthToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: StealthWindowVm row }) return;

        if (row.IsHidden)
        {
            StealthShowWindow(row.Hwnd);
            AppendLog($"[Stealth] Restored: \"{row.WindowTitle}\"");
        }
        else
        {
            StealthHideWindow(row.Hwnd, row.WindowTitle);
            AppendLog($"[Stealth] Hidden: \"{row.WindowTitle}\"");
        }
    }

    private void BtnRefreshStealth_Click(object sender, RoutedEventArgs e) => LoadStealthManager();

    private void BtnShowAllStealth_Click(object sender, RoutedEventArgs e) => ShowAllHiddenWindows();

    // ═══════════════════════════════════════════════════
    //  SYSTEM TRAY — dynamic menu
    // ═══════════════════════════════════════════════════

    private void InitializeTrayIcon()
    {
        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = "SmartMacroAI — Phạm Duy",
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) =>
            Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; ShowInTaskbar = true; _appHidden = false;
                Win32Api.SetForegroundWindow(new WindowInteropHelper(this).Handle); });
        RebuildTrayMenu();
    }

    private void RebuildTrayMenu()
    {
        if (_trayIcon is null) return;

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Show SmartMacroAI", null, (_, _) =>
            Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; ShowInTaskbar = true; _appHidden = false;
                Win32Api.SetForegroundWindow(new WindowInteropHelper(this).Handle); }));
        menu.Items.Add(new WinForms.ToolStripSeparator());

        if (_hiddenWindows.Count > 0)
        {
            var sub = new WinForms.ToolStripMenuItem($"Hidden Windows ({_hiddenWindows.Count})");
            foreach (var (hwnd, title) in _hiddenWindows.ToList())
            {
                var capturedHwnd = hwnd;
                string shortTitle = title.Length > 40 ? string.Concat(title.AsSpan(0, 37), "...") : title;
                sub.DropDownItems.Add(shortTitle, null, (_, _) =>
                    Dispatcher.Invoke(() =>
                    {
                        StealthShowWindow(capturedHwnd);
                        AppendLog($"[Tray] Restored: \"{title}\"");
                    }));
            }
            menu.Items.Add(sub);
        }
        else
        {
            var noItems = new WinForms.ToolStripMenuItem("(no hidden windows)") { Enabled = false };
            menu.Items.Add(noItems);
        }

        menu.Items.Add("Show All Hidden Windows", null, (_, _) =>
            Dispatcher.Invoke(ShowAllHiddenWindows));
        menu.Items.Add("Stop All Macros", null, (_, _) =>
            Dispatcher.Invoke(() => BtnStopAllMacros_Click(this, new RoutedEventArgs())));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) =>
            Dispatcher.Invoke(() => { ShowAllHiddenWindows(); Close(); }));

        _trayIcon.ContextMenuStrip = menu;
    }

    private static Drawing.Icon CreateTrayIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/logo.ico", UriKind.Absolute);
            var sri = System.Windows.Application.GetResourceStream(uri);
            if (sri?.Stream is not null)
            {
                using (sri.Stream)
                {
                    using var buf = new MemoryStream();
                    sri.Stream.CopyTo(buf);
                    byte[] data = buf.ToArray();
                    return new Drawing.Icon(new MemoryStream(data));
                }
            }
        }
        catch
        {
            /* try fallbacks */
        }

        try
        {
            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            {
                using Drawing.Icon? embedded = Drawing.Icon.ExtractAssociatedIcon(exe);
                if (embedded is not null)
                    return (Drawing.Icon)embedded.Clone();
            }
        }
        catch
        {
            /* last resort */
        }

        return CreateFallbackTrayIcon();
    }

    private static Drawing.Icon CreateFallbackTrayIcon()
    {
        var bmp = new Drawing.Bitmap(16, 16);
        using var g = Drawing.Graphics.FromImage(bmp);
        g.Clear(Drawing.Color.FromArgb(137, 180, 250));
        using var font = new Drawing.Font("Segoe UI", 9, Drawing.FontStyle.Bold);
        g.DrawString("S", font, Drawing.Brushes.Black, 1, 0);
        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    // ═══════════════════════════════════════════════════
    //  NAVIGATION
    // ═══════════════════════════════════════════════════

    private void SetActiveView(string viewName)
    {
        _activeView = viewName;
        DashboardView.Visibility = Visibility.Collapsed;
        MacroEditorView.Visibility = Visibility.Collapsed;
        ImageRecognitionView.Visibility = Visibility.Collapsed;
        OcrEngineView.Visibility = Visibility.Collapsed;
        StealthManagerView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        AboutView.Visibility = Visibility.Collapsed;
        ResetSidebarButtons();

        switch (viewName)
        {
            case "Dashboard":
                DashboardView.Visibility = Visibility.Visible;
                BtnDashboard.Style = (Style)FindResource("SidebarButtonActiveStyle");
                LoadDashboard();
                break;
            case "MacroEditor":
                MacroEditorView.Visibility = Visibility.Visible;
                BtnMacroEditor.Style = (Style)FindResource("SidebarButtonActiveStyle");
                break;
            case "ImageRecognition":
                ImageRecognitionView.Visibility = Visibility.Visible;
                BtnImageRecognition.Style = (Style)FindResource("SidebarButtonActiveStyle");
                break;
            case "OcrEngine":
                OcrEngineView.Visibility = Visibility.Visible;
                BtnOcrEngine.Style = (Style)FindResource("SidebarButtonActiveStyle");
                break;
            case "StealthManager":
                StealthManagerView.Visibility = Visibility.Visible;
                BtnStealthManager.Style = (Style)FindResource("SidebarButtonActiveStyle");
                LoadStealthManager();
                break;
            case "Settings":
                SettingsView.Visibility = Visibility.Visible;
                BtnSettingsNav.Style = (Style)FindResource("SidebarButtonActiveStyle");
                break;
            case "About":
                AboutView.Visibility = Visibility.Visible;
                BtnAbout.Style = (Style)FindResource("SidebarButtonActiveStyle");
                break;
        }

        ApplyPageChromeForView(viewName);
    }

    private void ApplyPageChromeForView(string viewName)
    {
        (string titleKey, string subKey) = viewName switch
        {
            "Dashboard" => ("ui_Page_Dashboard", "ui_PageSub_Dashboard"),
            "MacroEditor" => ("ui_Page_MacroEditor", "ui_PageSub_MacroEditor"),
            "ImageRecognition" => ("ui_Page_ImageRecognition", "ui_PageSub_ImageRecognition"),
            "OcrEngine" => ("ui_Page_OcrEngine", "ui_PageSub_OcrEngine"),
            "StealthManager" => ("ui_Page_StealthManager", "ui_PageSub_StealthManager"),
            "Settings" => ("ui_Page_Settings", "ui_PageSub_Settings"),
            "About" => ("ui_Page_About", "ui_PageSub_About"),
            _ => ("ui_Page_Dashboard", "ui_PageSub_Dashboard"),
        };
        TxtPageTitle.Text = LanguageManager.GetString(titleKey);
        TxtPageSubtitle.Text = LanguageManager.GetString(subKey);
    }

    private void OnUiLanguageChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            ApplyPageChromeForView(_activeView);
            InitLanguageCombo();
            UpdateProcessBar();
            if (BtnRunMacro is { IsEnabled: false } && BtnStopMacro is { IsEnabled: true })
                TxtStatus.Text = LanguageManager.GetString("ui_Header_Running");
            else
                TxtStatus.Text = LanguageManager.GetString("ui_Header_Ready");
            InitWinRtOcrLanguageCombo();
            LoadAntiDetectionFromSettings();
        });
    }

    private void ResetSidebarButtons()
    {
        var s = (Style)FindResource("SidebarButtonStyle");
        BtnDashboard.Style = s;
        BtnMacroEditor.Style = s;
        BtnImageRecognition.Style = s;
        BtnOcrEngine.Style = s;
        BtnStealthManager.Style = s;
        BtnSettingsNav.Style = s;
        BtnAbout.Style = s;
    }

    private void BtnDashboard_Click(object sender, RoutedEventArgs e) => SetActiveView("Dashboard");
    private void BtnMacroEditor_Click(object sender, RoutedEventArgs e) => SetActiveView("MacroEditor");
    private void BtnImageRecognition_Click(object sender, RoutedEventArgs e) => SetActiveView("ImageRecognition");
    private void BtnOcrEngine_Click(object sender, RoutedEventArgs e) => SetActiveView("OcrEngine");
    private void BtnStealthManager_Click(object sender, RoutedEventArgs e) => SetActiveView("StealthManager");
    private void BtnSettings_Click(object sender, RoutedEventArgs e) => SetActiveView("Settings");
    private void BtnAbout_Click(object sender, RoutedEventArgs e) => SetActiveView("About");

    private void BtnNewMacro_Click(object sender, RoutedEventArgs e)
    {
        _currentScript = new MacroScript();
        _actions.Clear();
        SyncScriptToUi();
        SetActiveView("MacroEditor");
        AppendLog("New macro created.");
    }

    // ═══════════════════════════════════════════════════
    //  SETTINGS — HOTKEY UI
    // ═══════════════════════════════════════════════════

    private static readonly string[] ModifierOptions =
        ["Ctrl", "Alt", "Shift", "Ctrl+Alt", "Ctrl+Shift", "Alt+Shift", "Ctrl+Alt+Shift"];

    private static readonly string[] KeyOptions =
        ["F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12",
         "A","B","C","D","E","F","G","H","I","J","K","L","M",
         "N","O","P","Q","R","S","T","U","V","W","X","Y","Z"];

    private void InitSettingsUi()
    {
        CmbToggleAppMod.ItemsSource = ModifierOptions;
        CmbToggleAppKey.ItemsSource = KeyOptions;
        CmbToggleTargetMod.ItemsSource = ModifierOptions;
        CmbToggleTargetKey.ItemsSource = KeyOptions;

        CmbToggleAppMod.SelectedItem = HotkeySettings.ModifierToString(_hotkeySettings.ToggleAppModifier);
        CmbToggleAppKey.SelectedItem = HotkeySettings.KeyToString(_hotkeySettings.ToggleAppKey);
        CmbToggleTargetMod.SelectedItem = HotkeySettings.ModifierToString(_hotkeySettings.ToggleTargetModifier);
        CmbToggleTargetKey.SelectedItem = HotkeySettings.KeyToString(_hotkeySettings.ToggleTargetKey);

        InitLanguageCombo();
        LoadVisionScaleSlidersFromSettings();
        InitMouseSettingsUi();
        InitWinRtOcrLanguageCombo();
        LoadAntiDetectionFromSettings();
    }

    private void InitWinRtOcrLanguageCombo()
    {
        if (CmbWinRtOcrLanguage is null)
            return;

        _suppressWinRtOcrCombo = true;
        CmbWinRtOcrLanguage.Items.Clear();
        CmbWinRtOcrLanguage.Items.Add(new ComboBoxItem
        {
            Tag = "auto",
            Content = LanguageManager.GetString("ui_OcrLang_Auto"),
        });
        CmbWinRtOcrLanguage.Items.Add(new ComboBoxItem
        {
            Tag = "vi-VN",
            Content = LanguageManager.GetString("ui_OcrLang_Vi"),
        });
        CmbWinRtOcrLanguage.Items.Add(new ComboBoxItem
        {
            Tag = "en-US",
            Content = LanguageManager.GetString("ui_OcrLang_En"),
        });

        string tag = AppSettings.Load().OcrLanguageTag?.Trim() ?? "auto";
        if (!tag.Equals("vi-VN", StringComparison.OrdinalIgnoreCase)
            && !tag.Equals("en-US", StringComparison.OrdinalIgnoreCase))
            tag = "auto";

        foreach (object? item in CmbWinRtOcrLanguage.Items)
        {
            if (item is ComboBoxItem ci && ci.Tag is string t
                && t.Equals(tag, StringComparison.OrdinalIgnoreCase))
            {
                CmbWinRtOcrLanguage.SelectedItem = ci;
                break;
            }
        }

        if (CmbWinRtOcrLanguage.SelectedItem is null && CmbWinRtOcrLanguage.Items.Count > 0)
            CmbWinRtOcrLanguage.SelectedIndex = 0;

        _suppressWinRtOcrCombo = false;
    }

    private void CmbWinRtOcrLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressWinRtOcrCombo)
            return;
        if (CmbWinRtOcrLanguage?.SelectedItem is not ComboBoxItem { Tag: string t })
            return;

        var app = AppSettings.Load();
        app.OcrLanguageTag = t;
        app.Save();
    }

    private void RefreshDashboardVariablesPanel()
    {
        if (_activeView != "Dashboard")
            return;

        DashboardRowVm? row = _dashboardRows.FirstOrDefault(r => r.IsRunning);
        if (row?.Engine is null)
        {
            _dashboardVariableRows.Clear();
            return;
        }

        IReadOnlyList<(string Name, string Value, string Source)> live = row.Engine.GetLiveVariableRows();
        _dashboardVariableRows.Clear();
        foreach ((string name, string value, string source) in live)
        {
            _dashboardVariableRows.Add(new VariableLiveRowVm
            {
                Name = name,
                Value = value,
                Source = source,
            });
        }
    }

    private void BtnDashboardAddVariable_Click(object sender, RoutedEventArgs e)
    {
        if (DashboardGrid.SelectedItem is not DashboardRowVm row)
        {
            ShowToast(LanguageManager.GetString("ui_Dashboard_AddVar_SelectRow"), isError: true);
            return;
        }

        string? varName = PromptSimpleString(
            LanguageManager.GetString("ui_Dashboard_AddVar_NameTitle"),
            LanguageManager.GetString("ui_Dashboard_AddVar_NamePrompt"),
            "myVar");
        if (string.IsNullOrWhiteSpace(varName))
            return;

        varName = varName.Trim();
        string? varValue = PromptSimpleString(
            LanguageManager.GetString("ui_Dashboard_AddVar_ValueTitle"),
            LanguageManager.GetString("ui_Dashboard_AddVar_ValuePrompt"),
            "");

        if (varValue is null)
            return;

        if (row.IsRunning && row.Engine is not null)
        {
            row.Engine.RuntimeStringVariables.Set(varName, varValue, "Manual");
            AppendLog("[Biến] Đã đặt {{" + varName + "}} = \"" + Truncate(varValue, 40) + "\" (runtime).");
            RefreshDashboardVariablesPanel();
            return;
        }

        row.Script.Actions.Add(new SetVariableAction
        {
            VarName = varName,
            Value = varValue,
            Operation = "Set",
            ValueSource = "Manual",
        });
        row.NotifyScriptMetadataChanged();
        if (!string.IsNullOrWhiteSpace(row.FilePath))
        {
            try
            {
                ScriptManager.Save(row.Script, row.FilePath);
                ShowToast(LanguageManager.GetString("ui_Dashboard_AddVar_SavedStep"), isError: false);
            }
            catch (Exception ex)
            {
                ShowToast($"{LanguageManager.GetString("ui_Dashboard_AddVar_SaveFailed")}: {ex.Message}", isError: true);
            }
        }
        else
            ShowToast(LanguageManager.GetString("ui_Dashboard_AddVar_UnsavedFile"), isError: false);
    }

    /// <summary>Small modal prompt; returns null if cancelled.</summary>
    private string? PromptSimpleString(string title, string label, string initial)
    {
        var dlg = new Window
        {
            Title = title,
            Width = 400,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = TryFindResource("BaseBrush") as Brush ?? Brushes.WhiteSmoke,
        };

        var tb = new TextBox
        {
            Text = initial,
            Margin = new Thickness(16, 8, 16, 0),
            Foreground = TryFindResource("TextBrush") as Brush ?? Brushes.Black,
            Background = TryFindResource("Surface0Brush") as Brush ?? Brushes.White,
            BorderBrush = TryFindResource("Surface1Brush") as Brush ?? Brushes.Gray,
            CaretBrush = TryFindResource("TextBrush") as Brush ?? Brushes.Black,
            Padding = new Thickness(6, 4, 6, 4),
        };

        string? result = null;
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(16, 16, 16, 0),
            Foreground = TryFindResource("TextBrush") as Brush ?? Brushes.Black,
            TextWrapping = TextWrapping.Wrap,
        });
        sp.Children.Add(tb);

        var btnOk = new Button
        {
            Content = LanguageManager.GetString("ui_Ok"),
            Margin = new Thickness(0, 16, 8, 0),
            Padding = new Thickness(16, 6, 16, 6),
            IsDefault = true,
        };
        btnOk.Click += (_, _) => { result = tb.Text; dlg.DialogResult = true; };

        var btnCancel = new Button
        {
            Content = LanguageManager.GetString("ui_Cancel"),
            Margin = new Thickness(8, 16, 0, 0),
            Padding = new Thickness(16, 6, 16, 6),
            IsCancel = true,
        };
        var hp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16) };
        hp.Children.Add(btnOk);
        hp.Children.Add(btnCancel);
        sp.Children.Add(hp);
        dlg.Content = sp;
        tb.Focus();
        tb.SelectAll();

        bool? ok = dlg.ShowDialog();
        return ok == true ? result : null;
    }

    private void InitMouseSettingsUi()
    {
        CmbMouseProfile.ItemsSource = new[] { "Relaxed", "Normal", "Fast", "Instant" };
        LoadMouseSettingsFromDisk();
        UpdateMouseJitterLabel();
        UpdateMousePreviewPolyline();
    }

    private void LoadMouseSettingsFromDisk()
    {
        var app = AppSettings.Load();
        string prof = string.IsNullOrWhiteSpace(app.MouseProfileName) ? "Normal" : app.MouseProfileName;
        if (!CmbMouseProfile.Items.Cast<string>().Contains(prof, StringComparer.OrdinalIgnoreCase))
            prof = "Normal";
        CmbMouseProfile.SelectedItem = prof;
        SldMouseJitter.Value = Math.Clamp(app.MouseJitterIntensity, (int)SldMouseJitter.Minimum, (int)SldMouseJitter.Maximum);
        ChkMouseOvershoot.IsChecked = app.MouseOvershootEnabled;
        ChkMouseMicroPause.IsChecked = app.MouseMicroPauseEnabled;
        ChkMouseRawBypass.IsChecked = app.MouseRawInputBypass;
        ChkMouseHwSim.IsChecked = app.MouseHardwareSimulationDriver;
    }

    private void BtnSaveMouseSettings_Click(object sender, RoutedEventArgs e)
    {
        var app = AppSettings.Load();
        app.MouseProfileName = (CmbMouseProfile.SelectedItem as string)?.Trim() ?? "Normal";
        app.MouseJitterIntensity = (int)Math.Round(SldMouseJitter.Value);
        app.MouseOvershootEnabled = ChkMouseOvershoot.IsChecked == true;
        app.MouseMicroPauseEnabled = ChkMouseMicroPause.IsChecked == true;
        app.MouseRawInputBypass = ChkMouseRawBypass.IsChecked == true;
        app.MouseHardwareSimulationDriver = ChkMouseHwSim.IsChecked == true;
        app.Save();
        ShowToast(LanguageManager.GetString("ui_Toast_MouseSettingsSaved"), isError: false);
    }

    private void SldMouseJitter_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        UpdateMouseJitterLabel();
        UpdateMousePreviewPolyline();
    }

    private void CmbMouseProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateMousePreviewPolyline();
    }

    private void ChkMousePreview_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateMousePreviewPolyline();
    }

    private void UpdateMouseJitterLabel()
    {
        if (TxtMouseJitterValue is null) return;
        TxtMouseJitterValue.Text = $"{(int)Math.Round(SldMouseJitter.Value)}%";
    }

    /// <summary>Draws a sample Bézier path on the settings canvas (fixed seed, jitter from slider).</summary>
    private void UpdateMousePreviewPolyline()
    {
        if (MousePreviewPolyline is null || !IsLoaded) return;

        const float x0 = 28f, y0 = 165f, x1 = 320f, y1 = 28f;
        var rng = new Random(42);
        IReadOnlyList<Drawing.PointF> path = BezierCurveGenerator.BuildPath(
            new Drawing.PointF(x0, y0),
            new Drawing.PointF(x1, y1),
            rng);

        int jitterPct = (int)Math.Round(SldMouseJitter.Value);
        var pc = new PointCollection();
        if (jitterPct <= 0)
        {
            foreach (var p in path)
                pc.Add(new System.Windows.Point(p.X, p.Y));
        }
        else
        {
            double sigmaBase = 0.55;
            double sigma = sigmaBase * (jitterPct / 100.0);
            for (int i = 0; i < path.Count; i++)
            {
                float jx = 0, jy = 0;
                if (i > 0 && i < path.Count - 1)
                {
                    jx = (float)(NextGaussianPreview(rng) * sigma);
                    jy = (float)(NextGaussianPreview(rng) * sigma);
                }

                pc.Add(new System.Windows.Point(path[i].X + jx, path[i].Y + jy));
            }
        }

        MousePreviewPolyline.Points = pc;
    }

    private static double NextGaussianPreview(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    private void LoadVisionScaleSlidersFromSettings()
    {
        var app = AppSettings.Load();
        SldVisionMinScale.Value = Math.Clamp(app.VisionMatchMinScale, SldVisionMinScale.Minimum, SldVisionMinScale.Maximum);
        SldVisionMaxScale.Value = Math.Clamp(app.VisionMatchMaxScale, SldVisionMaxScale.Minimum, SldVisionMaxScale.Maximum);
        UpdateVisionScaleLabelTexts();
    }

    private void UpdateVisionScaleLabelTexts()
    {
        TxtVisionMinScaleValue.Text = SldVisionMinScale.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "×";
        TxtVisionMaxScaleValue.Text = SldVisionMaxScale.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "×";
    }

    private void SldVisionScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        UpdateVisionScaleLabelTexts();
    }

    private void BtnSaveVisionScales_Click(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Load();
        double min = SldVisionMinScale.Value;
        double max = SldVisionMaxScale.Value;
        if (min > max)
            (min, max) = (max, min);
        s.VisionMatchMinScale = min;
        s.VisionMatchMaxScale = max;
        s.Save();
        LoadVisionScaleSlidersFromSettings();
        ShowToast(LanguageManager.GetString("ui_Toast_VisionScalesSaved"), isError: false);
    }

    private void StartAntiDetectionServices()
    {
        try
        {
            ModuleAuditService.Instance.AttachWindow(this);
            var app = AppSettings.Load();
            ModuleAuditService.Instance.StartTitleRandomizerIfEnabled(app);
            ApplyCaptureAffinityFromSettings();
            ModuleAuditService.ScanForeignModulesOnStartupIfEnabled(msg =>
                Dispatcher.BeginInvoke(() =>
                    MessageBox.Show(this, msg, "SmartMacroAI", MessageBoxButton.OK, MessageBoxImage.Warning)));
        }
        catch (Exception ex)
        {
            AppendLog($"[Anti-Detection] Startup: {ex.Message}");
        }
    }

    private void ApplyCaptureAffinityFromSettings()
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
                return;
            var s = AppSettings.Load();
            ModuleAuditService.ApplyExcludeFromCapture(hwnd, s.AntiDetectionEnabled && s.AntiDetectionHideFromCapture);
        }
        catch (Exception ex)
        {
            AppendLog($"[Anti-Detection] Capture affinity: {ex.Message}");
        }
    }

    private void LoadAntiDetectionFromSettings()
    {
        if (ChkAntiEnabled is null)
            return;
        var s = AppSettings.Load();
        ChkAntiEnabled.IsChecked = s.AntiDetectionEnabled;
        ChkAntiFatigue.IsChecked = s.AntiDetectionFatigueEnabled;
        ChkAntiMicroPause.IsChecked = s.AntiDetectionMicroPauseBehavior;
        ChkAntiSessionBreak.IsChecked = s.AntiDetectionSessionBreakEnabled;
        ChkAntiCpuTweak.IsChecked = s.AntiDetectionCpuIdleTweak;
        ChkAntiHookScan.IsChecked = s.AntiDetectionHookScanOnStartup;
        ChkAntiScanTyping.IsChecked = s.AntiDetectionUseScanCodeTyping;
        ChkAntiCapture.IsChecked = s.AntiDetectionHideFromCapture;
        SldAntiMisclick.Value = Math.Clamp(s.AntiDetectionMisclickPercent, 0, 15);
        TxtAntiMisclickValue.Text = $"{(int)Math.Round(SldAntiMisclick.Value)}%";
        TxtAntiSessionMin.Text = s.AntiDetectionSessionMinutes.ToString();
        TxtAntiBreakMin.Text = s.AntiDetectionSessionBreakMinMinutes.ToString();
        TxtAntiBreakMax.Text = s.AntiDetectionSessionBreakMaxMinutes.ToString();
        TxtAntiDecoyTitles.Text = string.Join(Environment.NewLine, s.AntiDetectionDecoyTitles);
    }

    private void SldAntiMisclick_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || TxtAntiMisclickValue is null)
            return;
        TxtAntiMisclickValue.Text = $"{(int)Math.Round(SldAntiMisclick.Value)}%";
    }

    private void BtnSaveAntiDetection_Click(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Load();
        s.AntiDetectionEnabled = ChkAntiEnabled.IsChecked == true;
        s.AntiDetectionFatigueEnabled = ChkAntiFatigue.IsChecked == true;
        s.AntiDetectionMicroPauseBehavior = ChkAntiMicroPause.IsChecked == true;
        s.AntiDetectionSessionBreakEnabled = ChkAntiSessionBreak.IsChecked == true;
        s.AntiDetectionCpuIdleTweak = ChkAntiCpuTweak.IsChecked == true;
        s.AntiDetectionHookScanOnStartup = ChkAntiHookScan.IsChecked == true;
        s.AntiDetectionUseScanCodeTyping = ChkAntiScanTyping.IsChecked == true;
        s.AntiDetectionHideFromCapture = ChkAntiCapture.IsChecked == true;
        s.AntiDetectionMisclickPercent = (int)Math.Round(SldAntiMisclick.Value);

        if (int.TryParse(TxtAntiSessionMin.Text.Trim(), out int sess) && sess > 0)
            s.AntiDetectionSessionMinutes = sess;
        int bmin = s.AntiDetectionSessionBreakMinMinutes;
        if (int.TryParse(TxtAntiBreakMin.Text.Trim(), out int bminP) && bminP > 0)
            bmin = bminP;
        int bmax = s.AntiDetectionSessionBreakMaxMinutes;
        if (int.TryParse(TxtAntiBreakMax.Text.Trim(), out int bmaxP) && bmaxP > 0)
            bmax = bmaxP;
        s.AntiDetectionSessionBreakMinMinutes = bmin;
        s.AntiDetectionSessionBreakMaxMinutes = Math.Max(bmin, bmax);

        var lines = TxtAntiDecoyTitles.Text
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToList();
        if (lines.Count > 0)
            s.AntiDetectionDecoyTitles = lines;

        s.Save();
        Win32MouseInput.UseAntiDetectionMouseStyle = s.AntiDetectionEnabled;
        ModuleAuditService.Instance.StopTitleRandomizer();
        ModuleAuditService.Instance.StartTitleRandomizerIfEnabled(s);
        ApplyCaptureAffinityFromSettings();
        ShowToast(LanguageManager.GetString("ui_Toast_AntiSaved"), isError: false);
    }

    private void InitLanguageCombo()
    {
        _suppressLanguageCombo = true;
        CmbUiLanguage.ItemsSource = new[]
        {
            new { Code = "en", Display = LanguageManager.GetString("ui_Lang_English") },
            new { Code = "vi", Display = LanguageManager.GetString("ui_Lang_Vietnamese") },
        };
        CmbUiLanguage.DisplayMemberPath = "Display";
        CmbUiLanguage.SelectedValuePath = "Code";
        CmbUiLanguage.SelectedValue = AppSettings.Load().LanguageCode;
        _suppressLanguageCombo = false;
    }

    private void CmbUiLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageCombo) return;
        if (CmbUiLanguage.SelectedValue is string code)
            LanguageManager.ChangeLanguage(code);
    }

    private void BtnSaveHotkeys_Click(object sender, RoutedEventArgs e)
    {
        string? appMod = CmbToggleAppMod.SelectedItem as string;
        string? appKey = CmbToggleAppKey.SelectedItem as string;
        string? tgtMod = CmbToggleTargetMod.SelectedItem as string;
        string? tgtKey = CmbToggleTargetKey.SelectedItem as string;

        if (appMod is null || appKey is null || tgtMod is null || tgtKey is null)
        {
            ShowToast(LanguageManager.GetString("ui_Toast_SelectHotkeys"), isError: true);
            return;
        }

        UnregisterHotkeys();

        _hotkeySettings.ToggleAppModifier = HotkeySettings.StringToModifier(appMod);
        _hotkeySettings.ToggleAppKey = HotkeySettings.StringToKey(appKey);
        _hotkeySettings.ToggleTargetModifier = HotkeySettings.StringToModifier(tgtMod);
        _hotkeySettings.ToggleTargetKey = HotkeySettings.StringToKey(tgtKey);
        _hotkeySettings.Save();

        RegisterHotkeys();
        ShowToast(string.Format(LanguageManager.GetString("ui_Toast_HotkeysSavedFmt"), _hotkeySettings.ToggleAppDisplay, _hotkeySettings.ToggleTargetDisplay), isError: false);
    }

    // ═══════════════════════════════════════════════════
    //  DASHBOARD — MULTI-TASKING HUB (DataGrid)
    // ═══════════════════════════════════════════════════

    private void BtnRefreshDashboard_Click(object sender, RoutedEventArgs e) => LoadDashboard();

    private void LoadDashboard()
    {
        var keepRows = _dashboardRows.Where(r => r.IsRunning).ToList();
        _dashboardRows.Clear();

        var files = ScriptManager.EnumerateSavedScripts().ToList();
        TxtTotalMacros.Text = files.Count.ToString();

        foreach (string filePath in files)
        {
            var existing = keepRows.FirstOrDefault(r => r.FilePath == filePath);
            if (existing is not null)
            {
                _dashboardRows.Add(existing);
                continue;
            }

            MacroScript? script;
            try { script = ScriptManager.Load(filePath); }
            catch { continue; }
            if (script is null) continue;

            string fileStem = Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrWhiteSpace(script.Name) ||
                string.Equals(script.Name, "Untitled Macro", StringComparison.OrdinalIgnoreCase))
            {
                script.Name = string.IsNullOrWhiteSpace(fileStem) ? script.Name : fileStem;
                try { ScriptManager.Save(script, filePath); }
                catch { /* keep in-memory name only */ }
            }

            _dashboardRows.Add(new DashboardRowVm
            {
                FilePath = filePath,
                Script = script,
                TargetWindow = script.TargetWindowTitle,
                RunCount = script.RepeatCount,
                IntervalMinutes = script.IntervalMinutes,
            });
        }

        DashboardEmptyHint.Visibility = _dashboardRows.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        UpdateProcessBar();
        TxtRunsToday.Text = _runsToday.ToString();
    }

    private void DashRowWindowCombo_DropDownOpened(object sender, EventArgs e)
    {
        if (sender is ComboBox cmb)
        {
            string current = cmb.Text;
            cmb.ItemsSource = GetWindowEntries();
            cmb.Text = current;
        }
    }

    private async void DashboardStart_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: DashboardRowVm row }) return;

        if (string.IsNullOrWhiteSpace(row.TargetWindow))
        {
            ShowToast("Chọn cửa sổ mục tiêu cho macro này.", isError: true);
            return;
        }

        row.Script.RepeatCount = row.RunCount;
        row.Script.IntervalMinutes = row.IntervalMinutes;

        IntPtr targetHwnd = (row.TargetHwnd != IntPtr.Zero && Win32Api.IsWindow(row.TargetHwnd))
            ? row.TargetHwnd
            : ResolveHwnd(row.TargetWindow);
        if (targetHwnd == IntPtr.Zero)
        {
            ShowToast($"Không tìm thấy cửa sổ: \"{row.TargetWindow}\"", isError: true);
            return;
        }
        row.TargetHwnd = targetHwnd;
        row.Script.TargetWindowTitle = Win32Api.GetWindowTitle(targetHwnd);

        if (row.StealthMode && !_hiddenWindows.ContainsKey(targetHwnd))
        {
            string title = Win32Api.GetWindowTitle(targetHwnd);
            StealthHideWindow(targetHwnd, title);
            AppendLog($"[{row.MacroName}] Stealth ON — ẩn cửa sổ mục tiêu.");
        }

        row.Cts = new CancellationTokenSource();
        row.Engine = new MacroEngine { HardwareMode = row.HardwareMode };
        row.IsRunning = true;
        row.Status = "Running";
        UpdateProcessBar();

        row.Engine.Log += msg => Dispatcher.Invoke(() =>
        {
            AppendLog($"[{row.MacroName}] {msg}");
            if (msg.Contains("Waiting")) row.Status = "Waiting";
            else if (msg.Contains("Iteration")) row.Status = "Running";
        });
        row.Engine.ExecutionFinished += () => Dispatcher.Invoke(() =>
        {
            _runsToday++;
            row.IsRunning = false;
            row.Status = "Ready";
            RestoreStealthWindow(row);
            UpdateProcessBar();
            AppendLog($"[{row.MacroName}] Hoàn tất.");
        });
        row.Engine.ExecutionFaulted += ex => Dispatcher.Invoke(() =>
        {
            row.IsRunning = false;
            row.Status = "Error";
            RestoreStealthWindow(row);
            UpdateProcessBar();
            AppendLog($"[{row.MacroName}] Lỗi: {ex.Message}");
        });

        try
        {
            AppendLog($"[{row.MacroName}] Bắt đầu trên \"{row.TargetWindow}\"...");
            await row.Engine.ExecuteScriptAsync(row.Script, targetHwnd, row.Cts.Token);
        }
        catch (OperationCanceledException)
        {
            bool autoStop = row.Script.AutoStopMinutes > 0 && row.Cts is { Token.IsCancellationRequested: false };
            AppendLog($"[{row.MacroName}] {(autoStop ? "Đã dừng (hẹn giờ tự động)." : "Đã dừng.")}");
        }
        catch (Exception ex)
        {
            AppendLog($"[{row.MacroName}] Lỗi: {ex.Message}");
        }
        finally
        {
            row.IsRunning = false;
            if (row.Status == "Running" || row.Status == "Waiting")
                row.Status = "Stopped";
            RestoreStealthWindow(row);
            UpdateProcessBar();
        }
    }

    private void RestoreStealthWindow(DashboardRowVm row)
    {
        if (row.StealthMode && row.TargetHwnd != IntPtr.Zero)
        {
            StealthShowWindow(row.TargetHwnd);
            AppendLog($"[{row.MacroName}] Stealth OFF — hiện lại cửa sổ mục tiêu.");
        }
    }

    private void DashboardStop_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: DashboardRowVm row }) return;
        row.Cts?.Cancel();
        AppendLog($"[{row.MacroName}] Yêu cầu dừng.");
    }

    private async void DashboardRename_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: DashboardRowVm row }) return;

        if (row.IsRunning)
        {
            ShowToast("Dừng macro trước khi đổi tên.", isError: true);
            return;
        }

        var dlg = new RenameMacroDialog(row.Script.Name) { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.NewName))
            return;

        string newDisplayName = dlg.NewName.Trim();
        string oldPath = row.FilePath;
        string dir = Path.GetDirectoryName(oldPath) ?? ScriptManager.DefaultScriptsFolder;

        string newStem = ScriptManager.SanitizeFileStem(newDisplayName);
        string newPath = Path.GetFullPath(Path.Combine(dir, newStem + ".json"));
        string oldFull = Path.GetFullPath(oldPath);

        try
        {
            if (!string.Equals(newPath, oldFull, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(newPath))
                {
                    MessageBox.Show(
                        $"Đã tồn tại file:\n{newPath}",
                        "Đổi tên macro",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                File.Move(oldPath, newPath);
                row.FilePath = newPath;
            }

            row.Script.Name = newDisplayName;
            await ScriptManager.SaveAsync(row.Script, row.FilePath);
            row.NotifyScriptMetadataChanged();
            LoadDashboard();
            ShowToast($"Đã đổi tên thành \"{newDisplayName}\".", isError: false);
        }
        catch (Exception ex)
        {
            ShowToast($"Đổi tên thất bại: {ex.Message}", isError: true);
        }
    }

    private void DashboardDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: DashboardRowVm row }) return;

        if (row.IsRunning)
        {
            ShowToast("Dừng macro trước khi xóa.", isError: true);
            return;
        }

        var result = MessageBox.Show(
            $"Bạn có chắc muốn xóa kịch bản \"{row.MacroName}\"?\nFile: {Path.GetFileName(row.FilePath)}",
            "Xác nhận xóa",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            if (File.Exists(row.FilePath))
                File.Delete(row.FilePath);

            AppendLog($"Đã xóa: {row.FilePath}");
            LoadDashboard();
            ShowToast($"Đã xóa \"{row.MacroName}\".", isError: false);
        }
        catch (Exception ex)
        {
            ShowToast($"Lỗi khi xóa: {ex.Message}", isError: true);
        }
    }

    // ═══════════════════════════════════════════════════
    //  PROCESS BAR & STOP ALL
    // ═══════════════════════════════════════════════════

    private void UpdateProcessBar()
    {
        int active = _dashboardRows.Count(r => r.IsRunning);
        TxtActiveProcesses.Text = string.Format(LanguageManager.GetString("ui_ProcessBar_Fmt"), active, _hiddenWindows.Count);
        TxtActiveThreads.Text = active.ToString();
        ProcessDot.Fill = active > 0
            ? (Brush)FindResource("AccentYellowBrush")
            : (Brush)FindResource("AccentGreenBrush");
    }

    private void BtnStopAllMacros_Click(object sender, RoutedEventArgs e)
    {
        int count = 0;
        foreach (var row in _dashboardRows.Where(r => r.IsRunning))
        {
            row.Cts?.Cancel();
            count++;
        }
        _cts?.Cancel();
        AppendLog($"STOP ALL: đã dừng {count} macro đang chạy.");
    }

    // ═══════════════════════════════════════════════════
    //  MACRO EDITOR — DRAG & DROP
    // ═══════════════════════════════════════════════════

    private void ActionBlock_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Child is StackPanel panel)
        {
            string actionType = panel.Tag?.ToString() ?? "Unknown";
            DragDrop.DoDragDrop(border, actionType, DragDropEffects.Copy);
        }
    }

    private void Canvas_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.StringFormat))
            e.Effects = DragDropEffects.Copy;
        else if (e.Data.GetData(typeof(MacroAction)) is MacroAction)
            e.Effects = DragDropEffects.Move;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void Canvas_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.StringFormat)) return;
        string actionType = (string)e.Data.GetData(DataFormats.StringFormat);
        MacroAction? action = CreateActionFromType(actionType);
        if (action is null) return;
        _actions.Add(action);
        RebuildCanvas();
        AppendLog($"Added action: {action.DisplayName}");
    }

    private static MacroAction? CreateActionFromType(string actionType) => actionType switch
    {
        "Click" => new ClickAction(),
        "TypeText" => new TypeAction(),
        "Wait" => new WaitAction(),
        "Repeat" => new RepeatAction(),
        "SetVariable" => new SetVariableAction(),
        "IfVariable" => new IfVariableAction(),
        "Log" => new LogAction(),
        "TryCatch" => new TryCatchAction(),
        "IfImageFound" => new IfImageAction(),
        "IfTextFound" => new IfTextAction(),
        "WebAction" => new WebAction(),
        "WebNavigate" => new WebNavigateAction(),
        "WebClick" => new WebClickAction(),
        "WebType" => new WebTypeAction(),
        "OcrRegion" => new OcrRegionAction(),
        "ClearVar" => new ClearVariableAction(),
        "LogVar" => new LogVariableAction(),
        _ => null,
    };

    private void Workflow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _potentialDragAction = null;
        ReleaseWorkflowCaptureIfNeeded();

        if (e.OriginalSource is not DependencyObject src)
            return;
        if (IsDescendantOfType<Button>(src))
            return;

        MacroAction? action = FindMacroActionInWorkflowAncestors(src);
        if (action is null)
            return;
        if (!_actions.Contains(action))
            return;

        _dragStartPoint = e.GetPosition(this);
        _potentialDragAction = action;
        Mouse.Capture(MacroCanvas);
    }

    private void Workflow_MouseMove(object sender, MouseEventArgs e)
    {
        if (_potentialDragAction is null)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ReleaseWorkflowCaptureIfNeeded();
            _potentialDragAction = null;
            return;
        }

        Point current = e.GetPosition(this);
        Vector delta = current - _dragStartPoint;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        MacroAction action = _potentialDragAction;
        _potentialDragAction = null;
        ReleaseWorkflowCaptureIfNeeded();

        DragDrop.DoDragDrop(MacroCanvas, new DataObject(typeof(MacroAction), action), DragDropEffects.Move);
    }

    private void Workflow_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ReleaseWorkflowCaptureIfNeeded();
        _potentialDragAction = null;
    }

    private void Workflow_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(MacroAction)) is not MacroAction dragged)
            return;

        int oldIndex = _actions.IndexOf(dragged);
        if (oldIndex < 0)
            return;

        if (sender is not ItemsControl itemsControl)
            return;

        StackPanel? panel = FindItemsHostStackPanel(itemsControl);
        if (panel is null || panel.Children.Count != _actions.Count)
            return;

        Point pos = e.GetPosition(panel);
        int insertBefore = panel.Children.Count;
        for (int i = 0; i < panel.Children.Count; i++)
        {
            if (panel.Children[i] is not FrameworkElement child)
                continue;
            Point topLeft = child.TranslatePoint(new Point(0, 0), panel);
            double midY = topLeft.Y + child.ActualHeight * 0.5;
            if (pos.Y < midY)
            {
                insertBefore = i;
                break;
            }
        }

        if (insertBefore == oldIndex)
        {
            e.Handled = true;
            return;
        }

        if (insertBefore > oldIndex)
            insertBefore--;

        _actions.RemoveAt(oldIndex);
        _actions.Insert(insertBefore, dragged);

        RebuildCanvas();
        e.Handled = true;
    }

    private void ReleaseWorkflowCaptureIfNeeded()
    {
        if (ReferenceEquals(Mouse.Captured, MacroCanvas))
            MacroCanvas.ReleaseMouseCapture();
    }

    private MacroAction? FindMacroActionInWorkflowAncestors(DependencyObject? src)
    {
        while (src is not null)
        {
            if (ReferenceEquals(src, MacroCanvas))
                break;
            if (src is FrameworkElement fe && fe.DataContext is MacroAction ma)
                return ma;
            src = VisualTreeHelper.GetParent(src);
        }

        return null;
    }

    private static bool IsDescendantOfType<T>(DependencyObject? src) where T : DependencyObject
    {
        while (src is not null)
        {
            if (src is T)
                return true;
            src = VisualTreeHelper.GetParent(src);
        }

        return false;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                return match;
            T? nested = FindVisualChild<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    /// <summary>Finds the generated <see cref="StackPanel"/> that hosts <see cref="ItemsControl.Items"/> (not nested panels inside item templates).</summary>
    private static StackPanel? FindItemsHostStackPanel(ItemsControl itemsControl)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(itemsControl); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(itemsControl, i);
            StackPanel? found = FindItemsHostStackPanelRecursive(child);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static StackPanel? FindItemsHostStackPanelRecursive(DependencyObject o)
    {
        if (o is StackPanel sp && VisualTreeHelper.GetParent(sp) is ItemsPresenter)
            return sp;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
        {
            StackPanel? nested = FindItemsHostStackPanelRecursive(VisualTreeHelper.GetChild(o, i));
            if (nested is not null)
                return nested;
        }

        return null;
    }

    // ═══════════════════════════════════════════════════
    //  MACRO EDITOR — CANVAS RENDERING
    // ═══════════════════════════════════════════════════

    private void RebuildCanvas()
    {
        MacroCanvas.Items.Clear();
        CanvasPlaceholder.Visibility = _actions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < _actions.Count; i++)
            MacroCanvas.Items.Add(CreateRootWorkflowElement(_actions[i], i));
    }

    private UIElement CreateRootWorkflowElement(MacroAction a, int i) => a switch
    {
        RepeatAction r => BuildRepeatBlock(r, i),
        TryCatchAction t => BuildTryCatchBlock(t, i),
        IfVariableAction v => BuildIfVariableBlock(v, i),
        _ => BuildActionCard(a, i, i),
    };

    private UIElement BuildWorkflowChildUniversal(MacroAction child, int displayIndex, object editDeleteTag) => child switch
    {
        RepeatAction r => BuildRepeatBlock(r, editDeleteTag),
        TryCatchAction t => BuildTryCatchBlock(t, editDeleteTag),
        IfVariableAction v => BuildIfVariableBlock(v, editDeleteTag),
        _ => BuildActionCard(child, displayIndex, editDeleteTag),
    };

    private UIElement BuildTryCatchBlock(TryCatchAction tc, object headerTag)
    {
        var card = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 100, 60)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(4, 2, 4, 2),
            Padding = new Thickness(8),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A1810")),
            DataContext = tc,
        };

        var rootStack = new StackPanel();
        var headerRow = new DockPanel { LastChildFill = false };
        var titleTb = new TextBlock
        {
            Text = $"🛡 {tc.DisplayName} — THỬ NGHIỆM ({tc.TryActions.Count}) / XỬ LÝ LỖI ({tc.CatchActions.Count})",
            Foreground = new SolidColorBrush(Color.FromRgb(255, 180, 120)),
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        DockPanel.SetDock(titleTb, Dock.Left);
        headerRow.Children.Add(titleTb);

        var hdrButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnEdit = new Button
        {
            Content = "Sửa",
            FontSize = 11,
            Foreground = Brushes.White,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#89B4FA")),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 2, 8, 2),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 6, 0),
            Tag = headerTag,
            ToolTip = "Chỉnh sửa cấu hình khối Try/Catch",
        };
        btnEdit.Click += BtnEditAction_Click;
        var btnDel = new Button
        {
            Content = "X",
            FontSize = 11,
            Foreground = Brushes.White,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F38BA8")),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 2, 6, 2),
            Cursor = Cursors.Hand,
            Tag = headerTag,
            ToolTip = "Xoá khối Try/Catch khỏi luồng",
        };
        btnDel.Click += BtnDeleteAction_Click;
        hdrButtons.Children.Add(btnEdit);
        hdrButtons.Children.Add(btnDel);
        DockPanel.SetDock(hdrButtons, Dock.Right);
        headerRow.Children.Add(hdrButtons);
        rootStack.Children.Add(headerRow);

        var tryBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 180, 100)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(12, 6, 4, 4),
            Padding = new Thickness(6),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#152418")),
        };
        var tryPanel = new StackPanel();
        for (int j = 0; j < tc.TryActions.Count; j++)
            tryPanel.Children.Add(BuildWorkflowChildUniversal(tc.TryActions[j], j, new NestedTryCatchChildTag(tc, j, true)));

        var btnAddTry = new Button
        {
            Content = "+ Thêm vào THỬ NGHIỆM (TRY)",
            Margin = new Thickness(2, 6, 2, 2),
            Padding = new Thickness(10, 6, 10, 6),
            Background = new SolidColorBrush(Color.FromRgb(35, 80, 45)),
            Foreground = Brushes.LightGreen,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Tag = new TryCatchInsertTag(tc, true),
            ToolTip = "Thêm thao tác con vào nhánh thử nghiệm",
        };
        btnAddTry.Click += BtnAddTryCatchBranch_Click;
        tryPanel.Children.Add(btnAddTry);
        tryBorder.Child = tryPanel;

        var catchBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(200, 70, 70)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(12, 4, 4, 4),
            Padding = new Thickness(6),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#241010")),
        };
        var catchPanel = new StackPanel();
        for (int j = 0; j < tc.CatchActions.Count; j++)
            catchPanel.Children.Add(BuildWorkflowChildUniversal(tc.CatchActions[j], j, new NestedTryCatchChildTag(tc, j, false)));

        var btnAddCatch = new Button
        {
            Content = "+ Thêm vào XỬ LÝ LỖI (CATCH)",
            Margin = new Thickness(2, 6, 2, 2),
            Padding = new Thickness(10, 6, 10, 6),
            Background = new SolidColorBrush(Color.FromRgb(90, 35, 35)),
            Foreground = Brushes.MistyRose,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Tag = new TryCatchInsertTag(tc, false),
            ToolTip = "Thêm thao tác con vào nhánh xử lý khi lỗi",
        };
        btnAddCatch.Click += BtnAddTryCatchBranch_Click;
        catchPanel.Children.Add(btnAddCatch);
        catchBorder.Child = catchPanel;

        var expTry = new Expander
        {
            Header = "THỬ NGHIỆM (TRY)",
            IsExpanded = true,
            Foreground = Brushes.LightGreen,
            Margin = new Thickness(0, 4, 0, 0),
            Content = tryBorder,
            ToolTip = "Các bước chạy trước; nếu lỗi sẽ chuyển sang nhánh xử lý lỗi",
        };
        var expCatch = new Expander
        {
            Header = "XỬ LÝ LỖI (CATCH)",
            IsExpanded = true,
            Foreground = Brushes.IndianRed,
            Margin = new Thickness(0, 4, 0, 0),
            Content = catchBorder,
            ToolTip = "Chạy khi nhánh thử nghiệm báo lỗi (trừ khi bị hủy)",
        };
        rootStack.Children.Add(expTry);
        rootStack.Children.Add(expCatch);

        card.Child = rootStack;
        return card;
    }

    private UIElement BuildIfVariableBlock(IfVariableAction iv, object headerTag)
    {
        var card = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(100, 150, 220)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(4, 2, 4, 2),
            Padding = new Thickness(8),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#151F2A")),
            DataContext = iv,
        };

        var rootStack = new StackPanel();
        var headerRow = new DockPanel { LastChildFill = false };
        var titleTb = new TextBlock
        {
            Text = $"❓ {iv.DisplayName}: {iv.VarName} {iv.CompareOp} {iv.Value}",
            Foreground = new SolidColorBrush(Color.FromRgb(150, 190, 255)),
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        DockPanel.SetDock(titleTb, Dock.Left);
        headerRow.Children.Add(titleTb);

        var hdrButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnEdit = new Button
        {
            Content = "Sửa",
            FontSize = 11,
            Foreground = Brushes.White,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#89B4FA")),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 2, 8, 2),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 6, 0),
            Tag = headerTag,
            ToolTip = "Chỉnh sửa điều kiện biến",
        };
        btnEdit.Click += BtnEditAction_Click;
        var btnDel = new Button
        {
            Content = "X",
            FontSize = 11,
            Foreground = Brushes.White,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F38BA8")),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 2, 6, 2),
            Cursor = Cursors.Hand,
            Tag = headerTag,
            ToolTip = "Xoá khối điều kiện biến",
        };
        btnDel.Click += BtnDeleteAction_Click;
        hdrButtons.Children.Add(btnEdit);
        hdrButtons.Children.Add(btnDel);
        DockPanel.SetDock(hdrButtons, Dock.Right);
        headerRow.Children.Add(hdrButtons);
        rootStack.Children.Add(headerRow);

        var thenBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 120, 200)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(12, 6, 4, 4),
            Padding = new Thickness(6),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#121820")),
        };
        var thenPanel = new StackPanel();
        for (int j = 0; j < iv.ThenActions.Count; j++)
            thenPanel.Children.Add(BuildWorkflowChildUniversal(iv.ThenActions[j], j, new NestedIfVarChildTag(iv, j, true)));

        var btnThen = new Button
        {
            Content = "+ Thêm vào THỎA MÃN (THEN)",
            Margin = new Thickness(2, 6, 2, 2),
            Padding = new Thickness(10, 6, 10, 6),
            Background = new SolidColorBrush(Color.FromRgb(35, 55, 90)),
            Foreground = Brushes.LightBlue,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Tag = new IfVarInsertTag(iv, true),
            ToolTip = "Thêm thao tác khi điều kiện đúng",
        };
        btnThen.Click += BtnAddIfVariableBranch_Click;
        thenPanel.Children.Add(btnThen);
        thenBorder.Child = thenPanel;

        var elseBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(12, 4, 4, 4),
            Padding = new Thickness(6),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1A1A")),
        };
        var elsePanel = new StackPanel();
        for (int j = 0; j < iv.ElseActions.Count; j++)
            elsePanel.Children.Add(BuildWorkflowChildUniversal(iv.ElseActions[j], j, new NestedIfVarChildTag(iv, j, false)));

        var btnElse = new Button
        {
            Content = "+ Thêm vào TRÁI LẠI (ELSE)",
            Margin = new Thickness(2, 6, 2, 2),
            Padding = new Thickness(10, 6, 10, 6),
            Background = new SolidColorBrush(Color.FromRgb(55, 55, 55)),
            Foreground = Brushes.LightGray,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Tag = new IfVarInsertTag(iv, false),
            ToolTip = "Thêm thao tác khi điều kiện sai",
        };
        btnElse.Click += BtnAddIfVariableBranch_Click;
        elsePanel.Children.Add(btnElse);
        elseBorder.Child = elsePanel;

        rootStack.Children.Add(new Expander
        {
            Header = "THỎA MÃN (THEN)",
            IsExpanded = true,
            Foreground = Brushes.LightSkyBlue,
            Margin = new Thickness(0, 4, 0, 0),
            Content = thenBorder,
            ToolTip = "Nhánh khi biến thỏa mãn điều kiện",
        });
        rootStack.Children.Add(new Expander
        {
            Header = "TRÁI LẠI (ELSE)",
            IsExpanded = true,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 4, 0, 0),
            Content = elseBorder,
            ToolTip = "Nhánh khi điều kiện không thỏa mãn",
        });

        card.Child = rootStack;
        return card;
    }

    private UIElement BuildRepeatBlock(RepeatAction repeat, object headerTag)
    {
        var repeatCard = new Border
        {
            BorderBrush = Brushes.Orange,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(4, 2, 4, 2),
            Padding = new Thickness(8),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2418")),
            DataContext = repeat,
        };

        var rootStack = new StackPanel();

        var headerRow = new DockPanel { LastChildFill = false };
        var titleTb = new TextBlock
        {
            Text = BuildRepeatLabel(repeat),
            Foreground = Brushes.Orange,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        DockPanel.SetDock(titleTb, Dock.Left);
        headerRow.Children.Add(titleTb);

        var hdrButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnEditRepeat = new Button
        {
            Content = "Sửa",
            FontSize = 11,
            Foreground = Brushes.White,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#89B4FA")),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 2, 8, 2),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            Tag = headerTag,
            ToolTip = "Chỉnh sửa số lần lặp, khoảng cách, ảnh thoát",
        };
        btnEditRepeat.Click += BtnEditAction_Click;
        var btnDelRepeat = new Button
        {
            Content = "X",
            FontSize = 11,
            Foreground = Brushes.White,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F38BA8")),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 2, 6, 2),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = headerTag,
            ToolTip = "Xoá khối lặp lại",
        };
        btnDelRepeat.Click += BtnDeleteAction_Click;
        hdrButtons.Children.Add(btnEditRepeat);
        hdrButtons.Children.Add(btnDelRepeat);
        DockPanel.SetDock(hdrButtons, Dock.Right);
        headerRow.Children.Add(hdrButtons);
        rootStack.Children.Add(headerRow);

        var nestedBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(16, 6, 4, 4),
            Padding = new Thickness(6),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E2E")),
        };

        var nestedPanel = new StackPanel();
        for (int j = 0; j < repeat.LoopActions.Count; j++)
        {
            MacroAction child = repeat.LoopActions[j];
            nestedPanel.Children.Add(BuildWorkflowChildUniversal(child, j, new NestedLoopTag(repeat, j)));
        }

        var btnAddChild = new Button
        {
            Content = "+ Thêm thao tác vào vòng lặp",
            Margin = new Thickness(2, 6, 2, 2),
            Padding = new Thickness(10, 6, 10, 6),
            Background = new SolidColorBrush(Color.FromRgb(40, 70, 45)),
            Foreground = Brushes.LightGreen,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Tag = repeat,
            ToolTip = "Chèn thao tác mới vào cuối vòng lặp",
        };
        btnAddChild.Click += BtnAddChildAction_Click;
        nestedPanel.Children.Add(btnAddChild);

        nestedBorder.Child = nestedPanel;
        var exp = new Expander
        {
            Header = "Thân vòng lặp",
            IsExpanded = true,
            Foreground = Brushes.Orange,
            Margin = new Thickness(0, 4, 0, 0),
            Content = nestedBorder,
            ToolTip = "Danh sách thao tác chạy lặp lại theo cấu hình phía trên",
        };
        rootStack.Children.Add(exp);

        repeatCard.Child = rootStack;
        return repeatCard;
    }

    private static string BuildRepeatLabel(RepeatAction r)
    {
        string count = r.RepeatCount == 0 ? "∞" : $"{r.RepeatCount}x";
        string breakStr = string.IsNullOrEmpty(r.BreakIfImagePath)
            ? ""
            : $" | Thoát khi: {Path.GetFileName(r.BreakIfImagePath)}";
        return $"🔁 LẶP LẠI {count} (mỗi {r.IntervalMs}ms){breakStr}";
    }

    private UIElement BuildActionCard(MacroAction action, int displayIndex, object editDeleteTag)
    {
        bool isNested = editDeleteTag is not int;
        object buttonTag = editDeleteTag;
        int bracketIndex = displayIndex;

        var (label, color, detail) = action switch
        {
            ClickAction c => ("Click", "#89B4FA", $"X={c.X}  Y={c.Y}"),
            TypeAction t => ("Type Text", "#A6E3A1", $"\"{Truncate(t.Text, 25)}\""),
            WaitAction w => (w.DisplayName, "#F9E2AF", FormatWaitCardDetail(w)),
            SetVariableAction sv => ("📦 " + sv.DisplayName, "#CBA6F7", $"GÁN {sv.VarName} = {Truncate(sv.Value, 18)} [{sv.Operation}]"),
            IfVariableAction iv => ("❓ " + iv.DisplayName, "#89B4FA", $"{iv.VarName} {iv.CompareOp} {Truncate(iv.Value, 20)}"),
            LogAction lg => ("📝 " + lg.DisplayName, "#9399B2", $"GHI: {Truncate(lg.Message, 40)}"),
            TryCatchAction tc => ("🛡 " + tc.DisplayName, "#FE640B", $"THỬ ({tc.TryActions.Count}) / BẮT ({tc.CatchActions.Count})"),
            IfImageAction img => ("IF Image Found", "#FAB387", FormatIfImageCardDetail(img)),
            IfTextAction txt => ("IF Text Found", "#B4BEFE", $"\"{Truncate(txt.Text, 25)}\""),
            WebAction wa => ("\U0001F310 Web Action", "#94E2D5", wa.ActionType switch
            {
                WebActionType.Navigate => $"Navigate → {Truncate(wa.Url, 35)}",
                WebActionType.Click => $"Click → {Truncate(wa.Selector, 35)}",
                WebActionType.Type => $"Type → {Truncate(wa.Selector, 18)} ← \"{Truncate(wa.TextToType, 15)}\"",
                WebActionType.Scrape => $"Scrape → {Truncate(wa.Selector, 35)}",
                _ => wa.ActionType.ToString(),
            }),
            WebNavigateAction wn => ("Web: Navigate", "#94E2D5", Truncate(wn.Url, 40)),
            WebClickAction wc => ("Web: Click", "#94E2D5", Truncate(wc.CssSelector, 35)),
            WebTypeAction wt => ("Web: Type", "#94E2D5", $"{Truncate(wt.CssSelector, 20)} ← \"{Truncate(wt.TextToType, 15)}\""),
            OcrRegionAction ocr => ("📋 " + ocr.DisplayName, "#74C7EC", $"ROI {ocr.ScreenX},{ocr.ScreenY} {ocr.ScreenWidth}x{ocr.ScreenHeight} → {{" + ocr.OutputVariableName + "}}"),
            ClearVariableAction cv => ("🗑 " + cv.DisplayName, "#F5C2E7", string.IsNullOrWhiteSpace(cv.VarName) ? "Xóa tất cả" : "Xóa {{" + cv.VarName + "}}"),
            LogVariableAction lv => ("📋 " + lv.DisplayName, "#A6E3A1", "Log {{" + lv.VarName + "}}"),
            _ => (action.DisplayName, "#CDD6F4", ""),
        };

        var card = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#313244")),
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = isNested ? new Thickness(8, 2, 2, 2) : new Thickness(0, 2, 0, 2),
            DataContext = action,
        };

        var outer = new DockPanel();

        var btnDel = new Button { Content = "X", FontSize = 11, Foreground = Brushes.White, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F38BA8")), BorderThickness = new Thickness(0), Padding = new Thickness(6, 2, 6, 2), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Tag = buttonTag, ToolTip = "Xoá thao tác" };
        btnDel.Click += BtnDeleteAction_Click;
        DockPanel.SetDock(btnDel, Dock.Right);
        outer.Children.Add(btnDel);

        var btnEdit = new Button { Content = "Sửa", FontSize = 11, Foreground = Brushes.White, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#89B4FA")), BorderThickness = new Thickness(0), Padding = new Thickness(8, 2, 8, 2), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Tag = buttonTag, ToolTip = "Chỉnh sửa thao tác" };
        btnEdit.Click += BtnEditAction_Click;
        DockPanel.SetDock(btnEdit, Dock.Right);
        outer.Children.Add(btnEdit);

        var cs = new StackPanel { Orientation = Orientation.Horizontal };
        cs.Children.Add(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        cs.Children.Add(new TextBlock { Text = $"[{bracketIndex}] {label}", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CDD6F4")), FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        if (!string.IsNullOrEmpty(detail))
            cs.Children.Add(new TextBlock { Text = detail, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A6ADC8")), FontSize = 11, VerticalAlignment = VerticalAlignment.Center });

        outer.Children.Add(cs);
        card.Child = outer;
        return card;
    }

    private void BtnDeleteAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        switch (btn.Tag)
        {
            case NestedLoopTag nl:
                if (nl.ChildIndex < 0 || nl.ChildIndex >= nl.Parent.LoopActions.Count)
                    return;
                RemoveAtAndLog(nl.Parent.LoopActions, nl.ChildIndex, "Đã xoá khỏi vòng lặp");
                return;
            case NestedTryCatchChildTag tc:
                if (tc.IsTry)
                {
                    if (tc.ChildIndex < 0 || tc.ChildIndex >= tc.Parent.TryActions.Count) return;
                    RemoveAtAndLog(tc.Parent.TryActions, tc.ChildIndex, "Đã xoá khỏi THỬ NGHIỆM");
                }
                else
                {
                    if (tc.ChildIndex < 0 || tc.ChildIndex >= tc.Parent.CatchActions.Count) return;
                    RemoveAtAndLog(tc.Parent.CatchActions, tc.ChildIndex, "Đã xoá khỏi XỬ LÝ LỖI");
                }

                return;
            case NestedIfVarChildTag iv:
                if (iv.IsThen)
                {
                    if (iv.ChildIndex < 0 || iv.ChildIndex >= iv.Parent.ThenActions.Count) return;
                    RemoveAtAndLog(iv.Parent.ThenActions, iv.ChildIndex, "Đã xoá khỏi THỎA MÃN");
                }
                else
                {
                    if (iv.ChildIndex < 0 || iv.ChildIndex >= iv.Parent.ElseActions.Count) return;
                    RemoveAtAndLog(iv.Parent.ElseActions, iv.ChildIndex, "Đã xoá khỏi TRÁI LẠI");
                }

                return;
            case int idx when idx >= 0 && idx < _actions.Count:
                string name = _actions[idx].DisplayName;
                _actions.RemoveAt(idx);
                RebuildCanvas();
                AppendLog($"Đã xoá thao tác [{idx}]: {name}");
                return;
        }
    }

    private void RemoveAtAndLog(List<MacroAction> list, int index, string prefix)
    {
        string name = list[index].DisplayName;
        list.RemoveAt(index);
        RebuildCanvas();
        AppendLog($"{prefix}: {name}");
    }

    private void BtnEditAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        MacroAction? action = FindActionForCanvasTag(btn.Tag);
        if (action is null) return;
        var dlg = new ActionEditDialog(action, _editorTargetHwnd) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            RebuildCanvas();
            AppendLog($"Đã sửa: {action.DisplayName}");
        }
    }

    private MacroAction? FindActionForCanvasTag(object tag) => tag switch
    {
        int idx when idx >= 0 && idx < _actions.Count => _actions[idx],
        NestedLoopTag nl when nl.ChildIndex >= 0 && nl.ChildIndex < nl.Parent.LoopActions.Count => nl.Parent.LoopActions[nl.ChildIndex],
        NestedTryCatchChildTag tc when tc.IsTry && tc.ChildIndex >= 0 && tc.ChildIndex < tc.Parent.TryActions.Count => tc.Parent.TryActions[tc.ChildIndex],
        NestedTryCatchChildTag tc when !tc.IsTry && tc.ChildIndex >= 0 && tc.ChildIndex < tc.Parent.CatchActions.Count => tc.Parent.CatchActions[tc.ChildIndex],
        NestedIfVarChildTag iv when iv.IsThen && iv.ChildIndex >= 0 && iv.ChildIndex < iv.Parent.ThenActions.Count => iv.Parent.ThenActions[iv.ChildIndex],
        NestedIfVarChildTag iv when !iv.IsThen && iv.ChildIndex >= 0 && iv.ChildIndex < iv.Parent.ElseActions.Count => iv.Parent.ElseActions[iv.ChildIndex],
        _ => null,
    };

    private void BtnAddChildAction_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not RepeatAction parentRepeat)
            return;

        var picker = new ActionTypePicker { Owner = this };
        if (picker.ShowDialog() != true || string.IsNullOrEmpty(picker.SelectedType))
            return;

        MacroAction? newAction = CreateActionFromType(picker.SelectedType);
        if (newAction is null)
            return;

        var dialog = new ActionEditDialog(newAction, _editorTargetHwnd) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        parentRepeat.LoopActions.Add(newAction);
        RebuildCanvas();
        AppendLog($"Đã thêm {newAction.DisplayName} vào vòng lặp");
    }

    private void BtnAddTryCatchBranch_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not TryCatchInsertTag marker)
            return;

        var picker = new ActionTypePicker { Owner = this };
        if (picker.ShowDialog() != true || string.IsNullOrEmpty(picker.SelectedType))
            return;

        MacroAction? newAction = CreateActionFromType(picker.SelectedType);
        if (newAction is null)
            return;

        var dialog = new ActionEditDialog(newAction, _editorTargetHwnd) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (marker.IsTry)
            marker.Parent.TryActions.Add(newAction);
        else
            marker.Parent.CatchActions.Add(newAction);

        RebuildCanvas();
        AppendLog($"Đã thêm {newAction.DisplayName} vào {(marker.IsTry ? "THỬ NGHIỆM" : "XỬ LÝ LỖI")}");
    }

    private void BtnAddIfVariableBranch_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not IfVarInsertTag marker)
            return;

        var picker = new ActionTypePicker { Owner = this };
        if (picker.ShowDialog() != true || string.IsNullOrEmpty(picker.SelectedType))
            return;

        MacroAction? newAction = CreateActionFromType(picker.SelectedType);
        if (newAction is null)
            return;

        var dialog = new ActionEditDialog(newAction, _editorTargetHwnd) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (marker.IsThen)
            marker.Parent.ThenActions.Add(newAction);
        else
            marker.Parent.ElseActions.Add(newAction);

        RebuildCanvas();
        AppendLog($"Đã thêm {newAction.DisplayName} vào {(marker.IsThen ? "THỎA MÃN" : "TRÁI LẠI")}");
    }

    private void BtnClearCanvas_Click(object sender, RoutedEventArgs e) { _actions.Clear(); RebuildCanvas(); AppendLog("Canvas cleared."); }

    // ═══════════════════════════════════════════════════
    //  SAVE / LOAD
    // ═══════════════════════════════════════════════════

    private async void BtnSaveMacro_Click(object sender, RoutedEventArgs e)
    {
        SyncUiToScript();
        string suggest = ScriptManager.SanitizeFileStem(_currentScript.Name) + ".json";
        var dlg = new SaveFileDialog
        {
            Filter = "JSON Macro|*.json",
            FileName = suggest,
            InitialDirectory = ScriptManager.DefaultScriptsFolder,
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            string nameFromFile = Path.GetFileNameWithoutExtension(dlg.FileName);
            if (!string.IsNullOrWhiteSpace(nameFromFile))
                _currentScript.Name = nameFromFile;
            TxtMacroName.Text = _currentScript.Name;

            await ScriptManager.SaveAsync(_currentScript, dlg.FileName);
            AppendLog($"Saved: {dlg.FileName}");
            ShowToast($"Macro saved to {Path.GetFileName(dlg.FileName)}", isError: false);
        }
        catch (Exception ex) { ShowToast($"Save failed: {ex.Message}", isError: true); }
    }

    private async void BtnLoadMacro_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "JSON Macro|*.json", InitialDirectory = ScriptManager.DefaultScriptsFolder };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var script = await ScriptManager.LoadAsync(dlg.FileName);
            if (script is null) { ShowToast("Failed to parse macro file.", isError: true); return; }
            _currentScript = script;
            string baseName = Path.GetFileNameWithoutExtension(dlg.FileName);
            if (string.IsNullOrWhiteSpace(_currentScript.Name) ||
                string.Equals(_currentScript.Name, "Untitled Macro", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(baseName))
                    _currentScript.Name = baseName;
            }

            _actions.Clear();
            foreach (var a in _currentScript.Actions) _actions.Add(a);
            SyncScriptToUi();
            RebuildCanvas();
            AppendLog($"Loaded: {dlg.FileName} ({_actions.Count} actions)");
            ShowToast($"Loaded \"{_currentScript.Name}\" ({_actions.Count} actions)", isError: false);
        }
        catch (Exception ex) { ShowToast($"Load failed: {ex.Message}", isError: true); }
    }

    private void SyncScriptToUi()
    {
        TxtMacroName.Text = _currentScript.Name;
        CmbTargetWindow.Text = _currentScript.TargetWindowTitle;
        TxtRepeatCount.Text = _currentScript.RepeatCount.ToString();
        TxtAutoStopMinutes.Text = _currentScript.AutoStopMinutes.ToString();
        _editorTargetHwnd = IntPtr.Zero;
    }

    private void SyncUiToScript()
    {
        _currentScript.Name = TxtMacroName.Text.Trim();

        if (_editorTargetHwnd != IntPtr.Zero && Win32Api.IsWindow(_editorTargetHwnd))
            _currentScript.TargetWindowTitle = Win32Api.GetWindowTitle(_editorTargetHwnd);
        else
            _currentScript.TargetWindowTitle = CmbTargetWindow.Text.Trim();

        _currentScript.Actions = [.. _actions];
        if (int.TryParse(TxtRepeatCount.Text.Trim(), out int repeat)) _currentScript.RepeatCount = repeat;
        if (int.TryParse(TxtAutoStopMinutes.Text.Trim(), out int autoStop)) _currentScript.AutoStopMinutes = Math.Max(0, autoStop);
    }

    // ═══════════════════════════════════════════════════
    //  WINDOW-LIST COMBO HELPERS
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// Resolves a target HWND by partial title, checking the stealth tracker
    /// first so hidden windows are still found.
    /// </summary>
    private IntPtr ResolveHwnd(string partialTitle)
    {
        var hidden = _hiddenWindows
            .FirstOrDefault(kv => kv.Value.Contains(partialTitle, StringComparison.OrdinalIgnoreCase));
        if (hidden.Key != IntPtr.Zero && Win32Api.IsWindow(hidden.Key))
            return hidden.Key;

        return Win32Api.FindWindowByPartialTitle(partialTitle);
    }

    private List<WindowEntry> GetWindowEntries() =>
        Win32Api.GetAllVisibleWindows()
            .Where(w => w.Title != Title)
            .Select(w =>
            {
                Win32Api.GetWindowThreadProcessId(w.Handle, out uint pid);
                string procName;
                try { using var p = Process.GetProcessById((int)pid); procName = p.ProcessName; }
                catch { procName = "???"; }
                return new WindowEntry
                {
                    Handle = w.Handle,
                    ProcessId = (int)pid,
                    ProcessName = procName,
                    Title = w.Title,
                };
            })
            .OrderBy(e => e.ProcessName)
            .ThenBy(e => e.ProcessId)
            .ToList();

    private List<string> GetWindowTitles() =>
        Win32Api.GetAllVisibleWindows()
            .Where(w => w.Title != Title)
            .Select(w => w.Title).ToList();

    private void PopulateWindowCombo(ComboBox cmb)
    {
        string current = cmb.Text;
        cmb.ItemsSource = GetWindowEntries();
        cmb.Text = current;
    }

    /// <summary>
    /// Resolves the Image Recognition tab target HWND from a <see cref="WindowEntry"/>
    /// selection or from the editable title text (including legacy plain-title text).
    /// </summary>
    private bool TryResolveVisionTargetHwnd(out IntPtr hwnd)
    {
        hwnd = IntPtr.Zero;

        if (CmbVisionWindowTitle.SelectedItem is WindowEntry entry && Win32Api.IsWindow(entry.Handle))
        {
            hwnd = entry.Handle;
            return true;
        }

        string text = CmbVisionWindowTitle.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        hwnd = ResolveHwnd(text);
        if (hwnd != IntPtr.Zero)
            return true;

        int sep = text.LastIndexOf(" — ", StringComparison.Ordinal);
        if (sep >= 0 && sep + 3 < text.Length)
        {
            string titleOnly = text[(sep + 3)..].Trim();
            if (titleOnly.Length > 0)
            {
                hwnd = ResolveHwnd(titleOnly);
                return hwnd != IntPtr.Zero;
            }
        }

        return false;
    }

    private void PopulateCombo(ComboBox cmb)
    {
        string current = cmb.Text;
        cmb.ItemsSource = GetWindowTitles();
        cmb.Text = current;
    }

    private void BtnRefreshWindows_Click(object sender, RoutedEventArgs e) => PopulateWindowCombo(CmbTargetWindow);
    private void CmbTargetWindow_DropDownOpened(object sender, EventArgs e) => PopulateWindowCombo(CmbTargetWindow);

    private void CmbTargetWindow_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbTargetWindow.SelectedItem is WindowEntry entry)
            _editorTargetHwnd = entry.Handle;
    }

    private void DashRowWindowCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: WindowEntry entry, DataContext: DashboardRowVm row })
            row.TargetHwnd = entry.Handle;
    }

    private void BtnRefreshVisionWindows_Click(object sender, RoutedEventArgs e) => PopulateWindowCombo(CmbVisionWindowTitle);
    private void CmbVisionWindowTitle_DropDownOpened(object sender, EventArgs e) => PopulateWindowCombo(CmbVisionWindowTitle);

    private void BtnRefreshOcrWindows_Click(object sender, RoutedEventArgs e) => PopulateCombo(CmbOcrWindowTitle);
    private void CmbOcrWindowTitle_DropDownOpened(object sender, EventArgs e) => PopulateCombo(CmbOcrWindowTitle);

    // ═══════════════════════════════════════════════════
    //  IDENTIFY WINDOW (🎯 flash + bring to front)
    // ═══════════════════════════════════════════════════

    private void BtnIdentifyWindow_Click(object sender, RoutedEventArgs e)
    {
        IntPtr hwnd = _editorTargetHwnd;
        if (hwnd == IntPtr.Zero || !Win32Api.IsWindow(hwnd))
        {
            string text = CmbTargetWindow.Text.Trim();
            if (!string.IsNullOrWhiteSpace(text))
                hwnd = ResolveHwnd(text);
        }
        if (hwnd == IntPtr.Zero) { ShowToast("Select a window first.", isError: true); return; }
        Win32Api.IdentifyWindow(hwnd);
        AppendLog($"Identify → HWND=0x{hwnd:X} \"{Win32Api.GetWindowTitle(hwnd)}\"");
    }

    private void DashboardIdentify_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: DashboardRowVm row }) return;

        IntPtr hwnd = row.TargetHwnd;
        if (hwnd == IntPtr.Zero || !Win32Api.IsWindow(hwnd))
        {
            if (!string.IsNullOrWhiteSpace(row.TargetWindow))
                hwnd = ResolveHwnd(row.TargetWindow);
        }
        if (hwnd == IntPtr.Zero) { ShowToast("Select a window first.", isError: true); return; }

        row.TargetHwnd = hwnd;
        Win32Api.IdentifyWindow(hwnd);
        AppendLog($"[{row.MacroName}] Identify → HWND=0x{hwnd:X} \"{Win32Api.GetWindowTitle(hwnd)}\"");
    }

    // ═══════════════════════════════════════════════════
    //  RUN / STOP MACRO (sidebar buttons — editor macro)
    // ═══════════════════════════════════════════════════

    private async void BtnRunMacro_Click(object sender, RoutedEventArgs e)
    {
        SyncUiToScript();
        if (_actions.Count == 0) { ShowToast("No actions to run.", isError: true); return; }
        if (string.IsNullOrWhiteSpace(_currentScript.TargetWindowTitle) && _editorTargetHwnd == IntPtr.Zero)
        { ShowToast("Set a Target Window Title.", isError: true); return; }

        IntPtr editorHwnd = (_editorTargetHwnd != IntPtr.Zero && Win32Api.IsWindow(_editorTargetHwnd))
            ? _editorTargetHwnd
            : ResolveHwnd(_currentScript.TargetWindowTitle);
        if (editorHwnd == IntPtr.Zero) { ShowToast("Target window not found (even in hidden list).", isError: true); return; }

        SetRunningState(true);
        _cts = new CancellationTokenSource();
        _macroEngine = new MacroEngine { HardwareMode = ChkHardwareMode.IsChecked == true };
        _macroEngine.Log += msg => Dispatcher.Invoke(() => AppendLog(msg));
        _macroEngine.ActionStarted += (action, idx) => Dispatcher.Invoke(() =>
            TxtStatus.Text = $"{LanguageManager.GetString("ui_Status_Running")} [{idx}] {action.DisplayName}");
        _macroEngine.ExecutionFinished += () => Dispatcher.Invoke(() => { _runsToday++; SetRunningState(false); ShowToast("Macro completed.", isError: false); UpdateProcessBar(); });
        _macroEngine.ExecutionFaulted += ex => Dispatcher.Invoke(() => { SetRunningState(false); ShowToast($"Error: {ex.Message}", isError: true); UpdateProcessBar(); });

        try
        {
            AppendLog($"Starting macro \"{_currentScript.Name}\"...");
            await _macroEngine.ExecuteScriptAsync(_currentScript, editorHwnd, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            bool autoStop = _currentScript.AutoStopMinutes > 0 && _cts is { Token.IsCancellationRequested: false };
            ShowToast(autoStop ? "Macro stopped (auto-stop timer)." : "Macro stopped by user.", isError: false);
        }
        catch (Exception ex) { ShowToast($"Error: {ex.Message}", isError: true); }
        finally { SetRunningState(false); UpdateProcessBar(); }
    }

    private void BtnStopMacro_Click(object sender, RoutedEventArgs e) { _cts?.Cancel(); AppendLog("Stop requested."); }

    private void SetRunningState(bool running)
    {
        BtnRunMacro.IsEnabled = !running;
        BtnStopMacro.IsEnabled = running;
        StatusIndicator.Color = running ? (Color)FindResource("AccentYellowColor") : (Color)FindResource("AccentGreenColor");
        TxtStatus.Text = running ? LanguageManager.GetString("ui_Header_Running") : LanguageManager.GetString("ui_Header_Ready");
    }

    // ═══════════════════════════════════════════════════
    //  MACRO RECORDING
    // ═══════════════════════════════════════════════════

    private void BtnRecordMacro_Click(object sender, RoutedEventArgs e)
    {
        SyncUiToScript();
        string targetTitle = _currentScript.TargetWindowTitle;
        if (string.IsNullOrWhiteSpace(targetTitle) && _editorTargetHwnd == IntPtr.Zero)
        { ShowToast("Set a Target Window Title before recording.", isError: true); SetActiveView("MacroEditor"); return; }
        IntPtr hwnd = (_editorTargetHwnd != IntPtr.Zero && Win32Api.IsWindow(_editorTargetHwnd))
            ? _editorTargetHwnd
            : Win32Api.FindWindowByPartialTitle(targetTitle);
        if (hwnd == IntPtr.Zero) { ShowToast($"Window not found: \"{targetTitle}\".", isError: true); return; }

        _recorder?.Dispose();
        _recorder = new MacroRecorder();
        _recorder.Log += msg => Dispatcher.Invoke(() => AppendLog(msg));

        try { _recorder.StartRecording(hwnd); }
        catch (Exception ex) { ShowToast($"Recording failed: {ex.Message}", isError: true); _recorder.Dispose(); _recorder = null; return; }

        var toolbar = new RecordToolbar(_recorder) { Owner = null };
        toolbar.RecordingFinished += OnRecordingFinished;
        WindowState = WindowState.Minimized;
        toolbar.Show();
    }

    private void OnRecordingFinished(List<MacroAction> recorded)
    {
        WindowState = WindowState.Normal; Activate();
        if (recorded.Count == 0) { ShowToast("No actions recorded.", isError: false); return; }
        foreach (var a in recorded) _actions.Add(a);
        RebuildCanvas();
        SetActiveView("MacroEditor");
        ShowToast($"Recorded {recorded.Count} actions.", isError: false);
    }

    // ═══════════════════════════════════════════════════
    //  IMAGE RECOGNITION TEST
    // ═══════════════════════════════════════════════════

    private void BtnBrowseTemplate_Click(object sender, RoutedEventArgs e)
    {
        string filePath = TxtTemplatePath.Text.Trim();
        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            try
            {
                filePath = Path.GetFullPath(filePath);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                    UseShellExecute = true,
                });
            }
            catch (Exception ex) { ShowToast($"Could not open Explorer: {ex.Message}", isError: true); }
            return;
        }

        var dlg = new OpenFileDialog { Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp|All|*.*" };
        if (dlg.ShowDialog() == true) TxtTemplatePath.Text = dlg.FileName;
    }

    private async void BtnSnipArea_Click(object sender, RoutedEventArgs e)
    {
        IntPtr targetHwnd = IntPtr.Zero;
        if (TryResolveVisionTargetHwnd(out targetHwnd))
        {
            Win32Api.ShowWindow(targetHwnd, Win32Api.SW_MAXIMIZE);
            Win32Api.SetForegroundWindow(targetHwnd);
            await Task.Delay(400);
        }

        try
        {
            var snip = new SnippingToolWindow();
            if (snip.ShowDialog() == true && !string.IsNullOrEmpty(snip.CapturedFilePath))
            {
                TxtTemplatePath.Text = snip.CapturedFilePath;
                AppendLog($"[Snip] Saved template: {snip.CapturedFilePath}");
            }
        }
        finally
        {
            if (targetHwnd != IntPtr.Zero && Win32Api.IsWindow(targetHwnd))
                Win32Api.ShowWindow(targetHwnd, Win32Api.SW_RESTORE);
        }
    }

    private async void BtnTestVision_Click(object sender, RoutedEventArgs e)
    {
        BtnVisionStealthClick.IsEnabled = false;
        _visionLastFoundClientPoint = null;
        _visionLastFoundHwnd = IntPtr.Zero;

        string templatePath = TxtTemplatePath.Text.Trim();
        if (string.IsNullOrEmpty(templatePath))
        {
            TxtVisionResult.Text = "Provide both a Window Title and Template Image Path.";
            TxtVisionResult.Foreground = (Brush)FindResource("AccentRedBrush"); return;
        }
        if (!TryResolveVisionTargetHwnd(out IntPtr hWnd))
        {
            TxtVisionResult.Text = "Provide both a Window Title and Template Image Path.";
            TxtVisionResult.Foreground = (Brush)FindResource("AccentRedBrush"); return;
        }
        if (!double.TryParse(TxtThreshold.Text.Trim(), out double threshold)) threshold = 0.8;

        Drawing.Rectangle? visionRoi = GetRoiFromInputs();

        TxtVisionResult.Text = "Searching...";
        TxtVisionResult.Foreground = (Brush)FindResource("SubtextBrush");

        try
        {
            this.WindowState = WindowState.Minimized;
            await Task.Delay(250);

            Win32Api.ShowWindow(hWnd, Win32Api.SW_MAXIMIZE);
            Win32Api.SetForegroundWindow(hWnd);
            await Task.Delay(400);

            (string msg, bool found, Drawing.Point? clickPoint, string visionLogLine) = await Task.Run(() =>
            {
                if (!Win32Api.IsWindow(hWnd))
                    return ("Window not found.", false, (Drawing.Point?)null, string.Empty);
                var detailed = VisionEngine.FindImageOnWindowDetailed(hWnd, templatePath, visionRoi);
                if (detailed is null)
                    return ("Template matching returned no data.", false, (Drawing.Point?)null, string.Empty);
                var (loc, conf, scale, scanned) = detailed.Value;
                bool ok = conf >= threshold;
                string roiPart = scanned.IsEmpty ? "Full window" : scanned.ToString();
                string logLine =
                    $"[Vision] {(ok ? "FOUND" : "NOT FOUND")} | Conf: {conf * 100:F1}% " +
                    $"| Scale: {scale:F2}x | Center: ({loc.X},{loc.Y}) | ROI: {roiPart}";
                string m = ok
                    ? $"FOUND at ({loc.X}, {loc.Y}) — {conf:P1} — Scale: {scale:F2}x (DPI compensated)"
                    : $"NOT FOUND — Best: {conf:P1} — Scale: {scale:F2}x (threshold: {threshold:P1})";
                return (m, ok, ok ? (Drawing.Point?)loc : null, logLine);
            });
            TxtVisionResult.Text = msg;
            TxtVisionResult.Foreground = found ? (Brush)FindResource("AccentGreenBrush") : (Brush)FindResource("AccentRedBrush");
            if (!string.IsNullOrEmpty(visionLogLine))
                AppendLog(visionLogLine);

            if (found && clickPoint.HasValue)
            {
                _visionLastFoundClientPoint = clickPoint;
                _visionLastFoundHwnd = hWnd;
                BtnVisionStealthClick.IsEnabled = true;
            }
        }
        catch (Exception ex) { TxtVisionResult.Text = $"Error: {ex.Message}"; TxtVisionResult.Foreground = (Brush)FindResource("AccentRedBrush"); }
        finally
        {
            this.WindowState = WindowState.Normal;
            this.Activate();
            this.Topmost = true;
            await Task.Delay(100);
            this.Topmost = false;
        }
    }

    private Drawing.Rectangle? GetRoiFromInputs()
    {
        bool xOk = int.TryParse(RoiX.Text.Trim(), out int rx);
        bool yOk = int.TryParse(RoiY.Text.Trim(), out int ry);
        bool wOk = int.TryParse(RoiWidth.Text.Trim(), out int rw);
        bool hOk = int.TryParse(RoiHeight.Text.Trim(), out int rh);

        if (xOk && yOk && wOk && hOk && rw > 0 && rh > 0)
            return new Drawing.Rectangle(rx, ry, rw, rh);

        return null;
    }

    private void BtnClearRoi_Click(object sender, RoutedEventArgs e)
    {
        RoiX.Text = RoiY.Text = RoiWidth.Text = RoiHeight.Text = string.Empty;
    }

    private async void BtnVisionStealthClick_Click(object sender, RoutedEventArgs e)
    {
        if (_visionLastFoundClientPoint is not { } p || _visionLastFoundHwnd == IntPtr.Zero
            || !Win32Api.IsWindow(_visionLastFoundHwnd))
        {
            AppendLog("[Vision] No valid match to click — run Test Capture & Match first.");
            return;
        }

        try
        {
            await Win32Api.StealthClickOnFoundImage(_visionLastFoundHwnd, p, randomOffsetRange: 3);
            AppendLog($"Click sent to ({p.X},{p.Y})");
        }
        catch (Exception ex) { AppendLog($"[Vision] Stealth click failed: {ex.Message}"); }
    }

    // ═══════════════════════════════════════════════════
    //  OCR TEST
    // ═══════════════════════════════════════════════════

    private async void BtnTestOcr_Click(object sender, RoutedEventArgs e)
    {
        string windowTitle = CmbOcrWindowTitle.Text.Trim();
        if (string.IsNullOrEmpty(windowTitle)) { TxtOcrResult.Text = "Provide a Window Title."; TxtOcrResult.Foreground = (Brush)FindResource("AccentRedBrush"); return; }

        string langTag = "eng";
        if (CmbTesseractLanguage?.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string tag)
            langTag = tag;

        TxtOcrResult.Text = "Extracting text...";
        TxtOcrResult.Foreground = (Brush)FindResource("SubtextBrush");

        try
        {
            string text = await Task.Run(() =>
            {
                IntPtr hwnd = Win32Api.FindWindowByPartialTitle(windowTitle);
                if (hwnd == IntPtr.Zero) throw new InvalidOperationException($"Window not found: \"{windowTitle}\"");
                return VisionEngine.ExtractTextAndSave(hwnd, langTag);
            });
            TxtOcrResult.Text = string.IsNullOrWhiteSpace(text) ? "(no text detected)" : text;
            TxtOcrResult.Foreground = (Brush)FindResource("TextBrush");
            AppendLog($"[OCR Test] Extracted {text.Length} chars ({langTag}).");
        }
        catch (Exception ex) { TxtOcrResult.Text = $"Error: {ex.Message}"; TxtOcrResult.Foreground = (Brush)FindResource("AccentRedBrush"); }
    }

    // ═══════════════════════════════════════════════════
    //  UPDATE CHECKER
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// Fetches the latest GitHub release <c>tag_name</c> and compares it to the running assembly version (fallback: <see cref="CurrentVersion"/>).
    /// When silent=true (startup), shows a dialog only if a newer version exists.
    /// When silent=false (manual), always reports the result to the user.
    /// </summary>
    private async Task CheckForUpdatesAsync(bool silent = false)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, GitHubApiUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", GitHubUserAgent);
            request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
            request.Headers.TryAddWithoutValidation("Pragma", "no-cache");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using HttpResponseMessage response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            using JsonDocument doc = JsonDocument.Parse(json);
            string latestTag = doc.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;

            if (!TryParseReleaseTag(latestTag, out Version remoteVersion))
            {
                await Dispatcher.InvokeAsync(() =>
                    AppendLog($"[Update] Không đọc được tag_name từ GitHub: \"{latestTag}\""));
                return;
            }

            if (!TryGetLocalReleaseVersion(out Version localVersion))
            {
                await Dispatcher.InvokeAsync(() =>
                    AppendLog("[Update] Không xác định được phiên bản app để so sánh."));
                return;
            }

            bool isNewer = CompareReleaseVersion(remoteVersion, localVersion) > 0;
            string localDisplay = FormatVersionDisplay(localVersion);

            await Dispatcher.InvokeAsync(() =>
            {
                if (isNewer)
                {
                    AppendLog($"Đã có bản cập nhật mới: {latestTag.Trim()}");

                    var result = MessageBox.Show(
                        $"Đã có bản cập nhật mới: {latestTag.Trim()}\n\n" +
                        $"Phiên bản bạn đang dùng: {localDisplay}\n\n" +
                        "Bạn có muốn mở trang tải về để cập nhật không?",
                        "SmartMacroAI — Cập nhật mới",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                        Process.Start(new ProcessStartInfo(LandingPageUrl) { UseShellExecute = true });
                }
                else if (!silent)
                {
                    MessageBox.Show(
                        $"✅ Bạn đang dùng phiên bản mới nhất ({localDisplay}).",
                        "SmartMacroAI — Kiểm tra cập nhật",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                AppendLog(isNewer
                    ? $"[Update] Phiên bản GitHub: {latestTag.Trim()} (đang chạy: {localDisplay})"
                    : $"[Update] Đang dùng phiên bản mới nhất: {localDisplay} (GitHub: {latestTag.Trim()})");
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (!silent)
            {
                await Dispatcher.InvokeAsync(() =>
                    MessageBox.Show(
                        "Không thể kiểm tra cập nhật. Vui lòng kiểm tra kết nối mạng.\n\n" +
                        $"Chi tiết: {ex.Message}",
                        "SmartMacroAI — Lỗi kết nối",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning));
            }
        }
    }

    /// <summary>Parses a GitHub <c>tag_name</c> (e.g. <c>v1.1.1</c>, <c>1.2.0-rc1</c>) into <see cref="Version"/>.</summary>
    private static bool TryParseReleaseTag(string? tagName, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(tagName))
            return false;

        string trimmed = tagName.Trim().TrimStart('v', 'V');
        int dash = trimmed.IndexOf('-', StringComparison.Ordinal);
        if (dash > 0)
            trimmed = trimmed[..dash];

        if (!Version.TryParse(trimmed, out Version? parsed) || parsed is null)
            return false;

        version = parsed;
        return true;
    }

    /// <summary>Uses assembly version when set; otherwise parses <see cref="CurrentVersion"/>.</summary>
    private static bool TryGetLocalReleaseVersion(out Version version)
    {
        Version av = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
        if (av.Major != 0 || av.Minor != 0 || av.Build != 0 || av.Revision != 0)
        {
            version = av;
            return true;
        }

        return TryParseReleaseTag(CurrentVersion, out version);
    }

    /// <summary>Compares Major / Minor / Build only so tag <c>1.1.1</c> matches assembly <c>1.1.1.0</c>.</summary>
    private static int CompareReleaseVersion(Version remote, Version local)
    {
        int c = remote.Major.CompareTo(local.Major);
        if (c != 0)
            return c;
        c = remote.Minor.CompareTo(local.Minor);
        if (c != 0)
            return c;
        int rb = remote.Build >= 0 ? remote.Build : 0;
        int lb = local.Build >= 0 ? local.Build : 0;
        return rb.CompareTo(lb);
    }

    private static string FormatVersionDisplay(Version v)
    {
        int build = v.Build >= 0 ? v.Build : 0;
        return $"v{v.Major}.{v.Minor}.{build}";
    }

    private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            btn.IsEnabled = false;
            btn.Content   = "Đang kiểm tra…";
        }

        await CheckForUpdatesAsync(silent: false);

        if (sender is Button b)
        {
            b.IsEnabled = true;
            b.Content   = LanguageManager.GetString("ui_About_CheckUpdates");
        }
    }

    // ═══════════════════════════════════════════════════
    //  LOG CONSOLE
    // ═══════════════════════════════════════════════════

    private void AppendLog(string message)
    {
        string ts = DateTime.Now.ToString("HH:mm:ss");
        TxtLogConsole.Text += $"[{ts}] {message}\n";
        LogScrollViewer.ScrollToEnd();
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e) => TxtLogConsole.Text = string.Empty;

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    // ═══════════════════════════════════════════════════
    //  TOAST
    // ═══════════════════════════════════════════════════

    private async void ShowToast(string message, bool isError)
    {
        AppendLog(isError ? $"[ERROR] {message}" : message);
        TxtStatus.Text = Truncate(message, 60);
        StatusIndicator.Color = isError ? (Color)FindResource("AccentRedColor") : (Color)FindResource("AccentGreenColor");
        await Task.Delay(3000);
        if (_cts is null or { IsCancellationRequested: true })
        {
            StatusIndicator.Color = (Color)FindResource("AccentGreenColor");
            TxtStatus.Text = LanguageManager.GetString("ui_Header_Ready");
        }
    }

    // ═══════════════════════════════════════════════════
    //  CLEANUP
    // ═══════════════════════════════════════════════════

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _dashboardVariablesTimer?.Stop();
        ModuleAuditService.Instance.StopTitleRandomizer();
        LanguageManager.UiLanguageChanged -= OnUiLanguageChanged;
        UnregisterHotkeys();
        ShowAllHiddenWindows();

        foreach (var row in _dashboardRows.Where(r => r.IsRunning)) row.Cts?.Cancel();
        _cts?.Cancel();
        _cts?.Dispose();
        _recorder?.Dispose();

        _trayIcon?.Dispose();
        _trayIcon = null;

        VisionEngine.Shutdown();
    }

    private static string FormatWaitCardDetail(WaitAction w)
    {
        if (!string.IsNullOrWhiteSpace(w.WaitForImage))
            return $"Chờ ảnh hiện ≤{w.WaitTimeoutMs}ms: {Path.GetFileName(w.WaitForImage)}";
        if (!string.IsNullOrWhiteSpace(w.WaitForOcrContains)
            && w.OcrRegionWidth > 0
            && w.OcrRegionHeight > 0)
        {
            return $"Chờ OCR chứa \"{Truncate(w.WaitForOcrContains, 22)}\" " +
                   $"(màn hình {w.OcrRegionX},{w.OcrRegionY} {w.OcrRegionWidth}x{w.OcrRegionHeight}, " +
                   $"mỗi {w.OcrPollIntervalMs}ms, ≤{w.WaitTimeoutMs}ms)";
        }

        if (w.DelayMin != w.DelayMax)
            return $"{w.DelayMin}-{w.DelayMax}ms (random)";
        return $"{w.Milliseconds}ms";
    }

    private static string FormatIfImageCardDetail(IfImageAction img)
    {
        string roiInfo = img.SearchRegion.HasValue
            ? $" | ROI({img.RoiX},{img.RoiY},{img.RoiWidth}x{img.RoiHeight})"
            : " | Full window";
        string clickPart = img.ClickOnFound ? " \U0001F3AF Auto-Click" : " No-Click";
        return Path.GetFileName(img.ImagePath) + clickPart + roiInfo;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "...");
}

// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace SmartMacroAI;

public enum LogLevel { Info, Ok, Warn, Error }

public class LogEntry : INotifyPropertyChanged
{
    private string _color = "#d0d0d0";

    public string Time { get; set; } = string.Empty;
    public string MacroName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public LogLevel Level { get; set; } = LogLevel.Info;

    public string Color
    {
        get => _color;
        set
        {
            _color = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Color)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class LogWindow : Window
{
    private readonly ObservableCollection<LogEntry> _entries = new();
    private const int MaxEntries = 2000;

    public bool AutoScroll { get; set; } = true;

    public LogWindow()
    {
        InitializeComponent();
        DataContext = this;
        LogList.ItemsSource = _entries;
        TxtCount.Text = string.Format(Localization.LanguageManager.GetString("ui_LogWin_LineCount"), 0);
    }

    public void AppendLog(string macroName, string message, LogLevel level = LogLevel.Info)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => AppendLog(macroName, message, level));
            return;
        }

        string color = level switch
        {
            LogLevel.Warn  => "#fdab43",
            LogLevel.Error => "#ff7575",
            LogLevel.Ok    => "#6daa45",
            _              => "#d0d0d0"
        };

        var entry = new LogEntry
        {
            Time      = DateTime.Now.ToString("HH:mm:ss.fff"),
            MacroName = string.IsNullOrWhiteSpace(macroName) ? "(global)" : macroName,
            Message   = message,
            Level     = level,
            Color     = color
        };

        _entries.Add(entry);
        TxtCount.Text = string.Format(Localization.LanguageManager.GetString("ui_LogWin_LineCount"), _entries.Count);

        if (_entries.Count > MaxEntries)
            _entries.RemoveAt(0);

        if (AutoScroll && LogList.Items.Count > 0)
            LogList.ScrollIntoView(_entries);
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        _entries.Clear();
        TxtCount.Text = string.Format(Localization.LanguageManager.GetString("ui_LogWin_LineCount"), 0);
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Text files|*.txt",
            FileName = $"log_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            Title = Localization.LanguageManager.GetString("ui_Msg_SaveLogTitle")
        };

        if (dlg.ShowDialog() == true)
        {
            var lines = _entries.Select(entry =>
                $"[{entry.Time}] [{entry.MacroName}] {entry.Message}");
            File.WriteAllLines(dlg.FileName, lines);
            AppendLog("LogWindow", string.Format(Localization.LanguageManager.GetString("ui_LogWin_Saved"), Path.GetFileName(dlg.FileName)), LogLevel.Ok);
        }
    }

    private void LogWindow_Closing(object sender, CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}

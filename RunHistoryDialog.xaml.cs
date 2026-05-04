using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SmartMacroAI.Core;
using SmartMacroAI.Models;

namespace SmartMacroAI;

/// <summary>
/// Run history dialog showing macro execution history.
/// Created by Phạm Duy – Giải pháp tự động hóa thông minh.
/// </summary>
public partial class RunHistoryDialog : Window
{
    private readonly RunHistoryService _historyService = new();
    private readonly string? _macroNameFilter;
    private readonly ObservableCollection<MacroRunRecord> _records = [];

    public RunHistoryDialog(string? macroName = null)
    {
        InitializeComponent();
        _macroNameFilter = macroName;
        HistoryGrid.ItemsSource = _records;

        if (!string.IsNullOrWhiteSpace(macroName))
        {
            Title = string.Format(Localization.LanguageManager.GetString("ui_History_TitleFmt"), macroName);
            TxtMacroNameFilter.Text = $"Macro: {macroName}";
        }
        else
        {
            Title = Localization.LanguageManager.GetString("ui_History_TitleAll");
            TxtMacroNameFilter.Text = Localization.LanguageManager.GetString("ui_History_ShowAll");
        }

        LoadHistory();
    }

    private void LoadHistory()
    {
        _records.Clear();

        List<MacroRunRecord> records;
        if (string.IsNullOrWhiteSpace(_macroNameFilter))
            records = _historyService.LoadAll();
        else
            records = _historyService.Load(_macroNameFilter);

        foreach (var record in records)
            _records.Add(record);
    }

    private void HistoryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is MacroRunRecord record)
        {
            TxtLogPreview.Text = string.IsNullOrWhiteSpace(record.LogSnapshot)
                ? Localization.LanguageManager.GetString("ui_History_NoLog")
                : record.LogSnapshot;
        }
        else
        {
            TxtLogPreview.Text = Localization.LanguageManager.GetString("ui_History_SelectRecord");
        }
    }

    private void BtnViewLog_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is MacroRunRecord record)
        {
            if (string.IsNullOrWhiteSpace(record.LogSnapshot))
            {
                MessageBox.Show(Localization.LanguageManager.GetString("ui_Msg_NoLogForRecord"), "Log", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Show log in a simple text window or copy to clipboard
            var logWindow = new Window
            {
                Title = $"Log: {record.MacroName} ({record.StartTime:yyyy-MM-dd HH:mm:ss})",
                Width = 600,
                Height = 400,
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E1E2E")),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(12)
            };

            var textBlock = new TextBlock
            {
                Text = record.LogSnapshot,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#CDD6F4")),
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 11,
                TextWrapping = TextWrapping.NoWrap
            };

            scrollViewer.Content = textBlock;
            Grid.SetRow(scrollViewer, 1);

            grid.Children.Add(scrollViewer);
            logWindow.Content = grid;
            logWindow.ShowDialog();
        }
    }

    private void BtnViewScreenshot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is MacroRunRecord record)
        {
            if (string.IsNullOrWhiteSpace(record.ScreenshotPath) || !File.Exists(record.ScreenshotPath))
            {
                MessageBox.Show(Localization.LanguageManager.GetString("ui_Msg_ScreenshotNotFound"), "Screenshot", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = record.ScreenshotPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Localization.LanguageManager.GetString("ui_Msg_CannotOpenImage"), ex.Message), Localization.LanguageManager.GetString("ui_Msg_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void BtnClearHistory_Click(object sender, RoutedEventArgs e)
    {
        string message = string.IsNullOrWhiteSpace(_macroNameFilter)
            ? Localization.LanguageManager.GetString("ui_Msg_DeleteHistoryConfirm")
            : string.Format(Localization.LanguageManager.GetString("ui_Msg_DeleteHistoryMacroConfirm"), _macroNameFilter);

        var result = MessageBox.Show(message, Localization.LanguageManager.GetString("ui_Msg_ConfirmDelete"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            _historyService.Clear(_macroNameFilter);
            LoadHistory();
            TxtLogPreview.Text = Localization.LanguageManager.GetString("ui_History_Deleted");
        }
    }
}

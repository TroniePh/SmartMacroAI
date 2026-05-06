// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System.Windows;
using System.Windows.Controls;
using SmartMacroAI.Models;

namespace SmartMacroAI;

public partial class ScheduleEditDialog : Window
{
    public ScheduleSettings? Result { get; private set; }

    private readonly ScheduleSettings _original;

    public ScheduleEditDialog(ScheduleSettings schedule)
    {
        InitializeComponent();
        _original = schedule;

        // Load current values
        ChkEnabled.IsChecked = schedule.Enabled;

        // Select correct mode
        foreach (ComboBoxItem item in CmbMode.Items)
        {
            if (item.Tag?.ToString() == schedule.Mode)
            {
                CmbMode.SelectedItem = item;
                break;
            }
        }
        if (CmbMode.SelectedItem == null)
            CmbMode.SelectedIndex = 0;

        // Parse time
        int hour = schedule.RunAt.Hours;
        int minute = schedule.RunAt.Minutes;
        TxtHour.Text = hour.ToString("D2");
        TxtMinute.Text = minute.ToString("D2");
        TxtInterval.Text = Math.Max(1, schedule.IntervalMinutes).ToString();

        // Visibility
        UpdatePanelVisibility();
        CmbMode.SelectionChanged += (_, _) => UpdatePanelVisibility();
    }

    private void UpdatePanelVisibility()
    {
        string mode = (CmbMode.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Daily";
        PanelDaily.Visibility = mode == "Daily" ? Visibility.Visible : Visibility.Collapsed;
        PanelInterval.Visibility = mode == "Interval" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        string mode = (CmbMode.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Daily";

        int hour = 8, minute = 0;
        if (mode == "Daily")
        {
            if (!int.TryParse(TxtHour.Text.Trim(), out hour) || hour < 0 || hour > 23)
            {
                MessageBox.Show(Localization.LanguageManager.GetString("ui_Msg_InvalidHour"),
                    Localization.LanguageManager.GetString("ui_Msg_InvalidInput"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtHour.Focus();
                return;
            }
            if (!int.TryParse(TxtMinute.Text.Trim(), out minute) || minute < 0 || minute > 59)
            {
                MessageBox.Show(Localization.LanguageManager.GetString("ui_Msg_InvalidMinute"),
                    Localization.LanguageManager.GetString("ui_Msg_InvalidInput"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtMinute.Focus();
                return;
            }
        }

        int interval = 30;
        if (mode == "Interval")
        {
            if (!int.TryParse(TxtInterval.Text.Trim(), out interval) || interval < 1)
            {
                MessageBox.Show(Localization.LanguageManager.GetString("ui_Msg_InvalidInterval"),
                    Localization.LanguageManager.GetString("ui_Msg_InvalidInput"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtInterval.Focus();
                return;
            }
        }

        Result = new ScheduleSettings
        {
            Enabled = ChkEnabled.IsChecked == true,
            Mode = mode,
            RunAt = new TimeSpan(hour, minute, 0),
            IntervalMinutes = interval,
            RunOnStartup = mode == "OnStartup",
        };

        if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) DialogResult = true; else Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) DialogResult = false; else Close();
    }
}

// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartMacroAI.Localization;

namespace SmartMacroAI;

public sealed class InputDialog : Window
{
    public string InputText { get; private set; } = "";

    private readonly TextBox _inputBox;

    public InputDialog(string title, string label)
    {
        Title = title;
        Width = 480;
        Height = 200;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E2E"));

        var grid = new Grid { Margin = new Thickness(24) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var lbl = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CDD6F4")),
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(lbl, 0);
        grid.Children.Add(lbl);

        _inputBox = new TextBox
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E2E")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CDD6F4")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#45475A")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 13,
            CaretBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CDD6F4")),
        };
        Grid.SetRow(_inputBox, 1);
        grid.Children.Add(_inputBox);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        Grid.SetRow(btnPanel, 2);

        var btnCancel = new Button
        {
            Content = LanguageManager.GetString("ui_CancelBtn"),
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
            Content = LanguageManager.GetString("ui_Ok"),
            Padding = new Thickness(16, 8, 16, 8),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#89B4FA")),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        btnOk.Click += (_, _) =>
        {
            InputText = _inputBox.Text.Trim();
            DialogResult = true;
        };

        btnPanel.Children.Add(btnCancel);
        btnPanel.Children.Add(btnOk);
        grid.Children.Add(btnPanel);

        Content = grid;
    }
}

// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SmartMacroAI.Core;

namespace SmartMacroAI;

public partial class TemplatePickerDialog : Window
{
    public MacroTemplate? SelectedTemplate { get; private set; }
    private string _selectedCategory = "All";

    public TemplatePickerDialog()
    {
        InitializeComponent();
        LoadCategories();
        LoadTemplates();
    }

    private void LoadCategories()
    {
        var categories = new List<string> { "All" };
        categories.AddRange(MacroTemplateService.GetCategories());
        CategoryList.ItemsSource = categories;
    }

    private void LoadTemplates()
    {
        List<MacroTemplate> templates;
        if (_selectedCategory == "All")
            templates = MacroTemplateService.GetTemplates();
        else
            templates = MacroTemplateService.GetTemplatesByCategory(_selectedCategory);

        TemplateList.ItemsSource = templates;
    }

    private void Category_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string category)
        {
            _selectedCategory = category;
            LoadTemplates();
        }
    }

    private void TemplateCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is MacroTemplate template)
        {
            SelectTemplate(template);
        }
    }

    private void TemplateSelect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is MacroTemplate template)
        {
            SelectTemplate(template);
        }
    }

    private void SelectTemplate(MacroTemplate template)
    {
        SelectedTemplate = template;
        if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) DialogResult = true;
        Close();
    }

    private void BlankMacro_Click(object sender, RoutedEventArgs e)
    {
        SelectedTemplate = null;
        if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) DialogResult = false;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        SelectedTemplate = null;
        if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) DialogResult = false;
        Close();
    }
}

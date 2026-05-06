// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using SmartMacroAI.Models;
using SmartMacroAI.Localization;

namespace SmartMacroAI.Core;

public class MacroTemplate
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "General";
    public List<MacroAction> Actions { get; set; } = [];
    public string TargetWindowTitle { get; set; } = "";
}

public static class MacroTemplateService
{
    public static List<MacroTemplate> GetTemplates() => new()
    {
        new MacroTemplate
        {
            Name = LanguageManager.GetString("ui_Tmpl_NameAutoLogin"),
            Description = LanguageManager.GetString("ui_Tmpl_AutoLoginDesc"),
            Category = "Web",
            TargetWindowTitle = "",
            Actions = new List<MacroAction>
            {
                new LaunchAndBindAction { DisplayName = LanguageManager.GetString("ui_Tmpl_OpenBrowser"), Url = "{{url}}", Browser = LaunchBrowserKind.Edge, BindTimeoutMs = 30000, PollIntervalMs = 500 },
                new WaitAction { DisplayName = LanguageManager.GetString("ui_Tmpl_WaitPageLoad"), DelayMin = 2000, DelayMax = 3000 },
                new WebClickAction { DisplayName = LanguageManager.GetString("ui_Tmpl_ClickUsername"), CssSelector = "{{username_selector}}" },
                new WebTypeAction { DisplayName = LanguageManager.GetString("ui_Tmpl_TypeUsername"), CssSelector = "{{username_selector}}", TextToType = "{{username}}" },
                new WebClickAction { DisplayName = LanguageManager.GetString("ui_Tmpl_ClickPassword"), CssSelector = "{{password_selector}}" },
                new WebTypeAction { DisplayName = LanguageManager.GetString("ui_Tmpl_TypePassword"), CssSelector = "{{password_selector}}", TextToType = "{{password}}" },
                new WebClickAction { DisplayName = LanguageManager.GetString("ui_Tmpl_ClickLogin"), CssSelector = "{{login_button_selector}}" }
            }
        },
        new MacroTemplate
        {
            Name = LanguageManager.GetString("ui_Tmpl_NameAutoFill"),
            Description = LanguageManager.GetString("ui_Tmpl_AutoFillDesc"),
            Category = "Web",
            TargetWindowTitle = "",
            Actions = new List<MacroAction>
            {
                new LaunchAndBindAction { DisplayName = LanguageManager.GetString("ui_Tmpl_OpenForm"), Url = "{{form_url}}", Browser = LaunchBrowserKind.Edge, BindTimeoutMs = 30000 },
                new WaitAction { DisplayName = LanguageManager.GetString("ui_Tmpl_WaitFormLoad"), DelayMin = 2000, DelayMax = 3000 },
                new RepeatAction
                {
                    DisplayName = LanguageManager.GetString("ui_Tmpl_LoopEachCsvRow"),
                    RepeatCount = 0,
                    IntervalMs = 1000,
                    LoopActions = new List<MacroAction>
                    {
                        new WebClickAction { DisplayName = LanguageManager.GetString("ui_Tmpl_ClickField1"), CssSelector = "{{field1_selector}}" },
                        new WebTypeAction { DisplayName = LanguageManager.GetString("ui_Tmpl_TypeValue1"), CssSelector = "{{field1_selector}}", TextToType = "{{col1}}" },
                        new WebClickAction { DisplayName = LanguageManager.GetString("ui_Tmpl_ClickField2"), CssSelector = "{{field2_selector}}" },
                        new WebTypeAction { DisplayName = LanguageManager.GetString("ui_Tmpl_TypeValue2"), CssSelector = "{{field2_selector}}", TextToType = "{{col2}}" },
                        new WebClickAction { DisplayName = LanguageManager.GetString("ui_Tmpl_ClickSubmit"), CssSelector = "{{submit_selector}}" },
                        new WaitAction { DisplayName = LanguageManager.GetString("ui_Tmpl_WaitProcess"), DelayMin = 1000, DelayMax = 2000 }
                    }
                }
            }
        },
        new MacroTemplate
        {
            Name = LanguageManager.GetString("ui_Tmpl_NameAutoRepeat"),
            Description = LanguageManager.GetString("ui_Tmpl_AutoRepeatDesc"),
            Category = "Desktop",
            TargetWindowTitle = "{{target_window}}",
            Actions = new List<MacroAction>
            {
                new RepeatAction
                {
                    DisplayName = LanguageManager.GetString("ui_Tmpl_LoopActions"),
                    RepeatCount = 10,
                    IntervalMs = 5000,
                    LoopActions = new List<MacroAction>
                    {
                        new ClickAction { DisplayName = LanguageManager.GetString("ui_Tmpl_ClickPosition"), X = 0, Y = 0 },
                        new WaitAction { DisplayName = LanguageManager.GetString("ui_Tmpl_Wait"), DelayMin = 1000, DelayMax = 1500 }
                    }
                }
            }
        },
        new MacroTemplate
        {
            Name = LanguageManager.GetString("ui_Tmpl_NameImageDetect"),
            Description = LanguageManager.GetString("ui_Tmpl_ImageDetectDesc"),
            Category = "Desktop",
            TargetWindowTitle = "{{target_window}}",
            Actions = new List<MacroAction>
            {
                new RepeatAction
                {
                    DisplayName = LanguageManager.GetString("ui_Tmpl_CheckImage"),
                    RepeatCount = 0,
                    IntervalMs = 2000,
                    LoopActions = new List<MacroAction>
                    {
                        new IfImageAction
                        {
                            DisplayName = LanguageManager.GetString("ui_Tmpl_IfImageFound"),
                            ImagePath = "{{image_path}}",
                            Threshold = 0.8f,
                            TimeoutMs = 5000,
                            ClickOnFound = true,
                            RandomOffset = 5,
                            ThenActions = new List<MacroAction> { new WaitAction { DisplayName = LanguageManager.GetString("ui_Tmpl_WaitProcess"), DelayMin = 500, DelayMax = 1000 } },
                            ElseActions = new List<MacroAction>
                            {
                                new WaitAction
                                {
                                    DisplayName = LanguageManager.GetString("ui_Tmpl_IfImageElseWait"),
                                    DelayMin = 1000,
                                    DelayMax = 1000
                                }
                            }
                        },
                        new WaitAction { DisplayName = LanguageManager.GetString("ui_Tmpl_WaitAgain"), DelayMin = 500, DelayMax = 800 }
                    }
                }
            }
        },
        new MacroTemplate
        {
            Name = LanguageManager.GetString("ui_Tmpl_NameHotkey"),
            Description = LanguageManager.GetString("ui_Tmpl_HotkeyAutoDesc"),
            Category = "Desktop",
            TargetWindowTitle = "{{target_window}}",
            Actions = new List<MacroAction>
            {
                new RepeatAction
                {
                    DisplayName = LanguageManager.GetString("ui_Tmpl_LoopHotkey"),
                    RepeatCount = 5,
                    IntervalMs = 3000,
                    LoopActions = new List<MacroAction>
                    {
                        new KeyPressAction { DisplayName = LanguageManager.GetString("ui_Tmpl_PressCtrlS"), KeyName = "S", VirtualKeyCode = 0x53, Modifiers = new KeyModifiers { Ctrl = true }, HoldDurationMs = 100 },
                        new WaitAction { DisplayName = LanguageManager.GetString("ui_Tmpl_Wait"), DelayMin = 500, DelayMax = 1000 }
                    }
                }
            }
        },
        new MacroTemplate
        {
            Name = LanguageManager.GetString("ui_Tmpl_NameGameFarm"),
            Description = LanguageManager.GetString("ui_Tmpl_GameFarmingDesc"),
            Category = "Desktop",
            TargetWindowTitle = "{{target_window}}",
            Actions = new List<MacroAction>
            {
                new RepeatAction
                {
                    DisplayName = LanguageManager.GetString("ui_Tmpl_LoopSkills"),
                    RepeatCount = 0,
                    IntervalMs = 2000,
                    LoopActions = new List<MacroAction>
                    {
                        new KeyPressAction { DisplayName = string.Format(LanguageManager.GetString("ui_Tmpl_SkillKey"), 1), KeyName = "D1", VirtualKeyCode = 0x31, HoldDurationMs = 80, InputMode = KeyInputMode.RawInput },
                        new WaitAction { DisplayName = LanguageManager.GetString("ui_Tmpl_Wait"), DelayMin = 400, DelayMax = 600 },
                        new KeyPressAction { DisplayName = string.Format(LanguageManager.GetString("ui_Tmpl_SkillKey"), 2), KeyName = "D2", VirtualKeyCode = 0x32, HoldDurationMs = 80, InputMode = KeyInputMode.RawInput },
                        new WaitAction { DisplayName = LanguageManager.GetString("ui_Tmpl_Wait"), DelayMin = 400, DelayMax = 600 },
                        new KeyPressAction { DisplayName = string.Format(LanguageManager.GetString("ui_Tmpl_SkillKey"), 3), KeyName = "D3", VirtualKeyCode = 0x33, HoldDurationMs = 80, InputMode = KeyInputMode.RawInput },
                        new WaitAction { DisplayName = LanguageManager.GetString("ui_Tmpl_Wait"), DelayMin = 800, DelayMax = 1200 }
                    }
                }
            }
        },
        new MacroTemplate
        {
            Name = LanguageManager.GetString("ui_Tmpl_NameBlank"),
            Description = LanguageManager.GetString("ui_Tmpl_BlankDesc"),
            Category = "General",
            TargetWindowTitle = "",
            Actions = new List<MacroAction>()
        }
    };

    public static List<string> GetCategories() =>
        GetTemplates().Select(t => t.Category).Distinct().OrderBy(c => c).ToList();

    public static List<MacroTemplate> GetTemplatesByCategory(string category) =>
        GetTemplates().Where(t => t.Category == category).ToList();
}

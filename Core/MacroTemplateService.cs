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
                            Threshold = 0.7f,
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
            Name = "🍁 MapleStory Auto Farm",
            Description = LanguageManager.GetString("ui_Tmpl_MapleDesc"),
            Category = "Game",
            TargetWindowTitle = "MapleStory",
            Actions = new List<MacroAction>
            {
                // Buff phase (every loop iteration)
                new SetVariableAction { DisplayName = "Set loop counter", VarName = "loop", Value = "0", Operation = "Set" },
                new RepeatAction
                {
                    DisplayName = "🔄 Main Farm Loop (infinite)",
                    RepeatCount = 0,
                    IntervalMs = 500,
                    LoopActions = new List<MacroAction>
                    {
                        // Increment counter
                        new SetVariableAction { DisplayName = "loop++", VarName = "loop", Value = "1", Operation = "Increment" },

                        // Buff every 30 loops (~60s)
                        new IfVariableAction
                        {
                            DisplayName = "IF loop % 30 == 0 → Buff",
                            VarName = "loop",
                            CompareOp = "==",
                            Value = "30",
                            ThenActions = new List<MacroAction>
                            {
                                new KeyPressAction { DisplayName = "🛡️ Buff (Page Up)", KeyName = "PageUp", VirtualKeyCode = 0x21, HoldDurationMs = 100, InputMode = KeyInputMode.DriverLevel },
                                new WaitAction { DisplayName = "Wait buff cast", DelayMin = 800, DelayMax = 1200 },
                                new SetVariableAction { DisplayName = "Reset loop", VarName = "loop", Value = "0", Operation = "Set" },
                            },
                            ElseActions = new List<MacroAction>()
                        },

                        // HP Check — pixel color at HP bar position
                        new IfPixelColorAction
                        {
                            DisplayName = "⚠️ IF HP low (check red pixel)",
                            X = 100, Y = 580,
                            ExpectedColor = "#1A1A1A", // Dark = HP depleted
                            Tolerance = 40,
                            ThenActions = new List<MacroAction>
                            {
                                new KeyPressAction { DisplayName = "💊 HP Pot (Insert)", KeyName = "Insert", VirtualKeyCode = 0x2D, HoldDurationMs = 50, InputMode = KeyInputMode.DriverLevel },
                                new WaitAction { DisplayName = "Pot cooldown", DelayMin = 200, DelayMax = 400 },
                            },
                            ElseActions = new List<MacroAction>()
                        },

                        // Attack combo
                        new KeyPressAction { DisplayName = "⚔️ Attack (Ctrl)", KeyName = "LControlKey", VirtualKeyCode = 0xA2, HoldDurationMs = 80, InputMode = KeyInputMode.DriverLevel },
                        new WaitAction { DisplayName = "Attack delay", DelayMin = 300, DelayMax = 500 },

                        new KeyPressAction { DisplayName = "⚔️ Skill 1 (A)", KeyName = "A", VirtualKeyCode = 0x41, HoldDurationMs = 80, InputMode = KeyInputMode.DriverLevel },
                        new WaitAction { DisplayName = "Skill delay", DelayMin = 400, DelayMax = 700 },

                        // Move (alternate left/right)
                        new KeyPressAction { DisplayName = "➡️ Move Right", KeyName = "Right", VirtualKeyCode = 0x27, HoldDurationMs = 600, InputMode = KeyInputMode.DriverLevel },
                        new WaitAction { DisplayName = "Move delay", DelayMin = 200, DelayMax = 400 },

                        // Loot
                        new KeyPressAction { DisplayName = "💰 Loot (Z)", KeyName = "Z", VirtualKeyCode = 0x5A, HoldDurationMs = 50, InputMode = KeyInputMode.DriverLevel },
                        new WaitAction { DisplayName = "Loot delay", DelayMin = 100, DelayMax = 300 },
                    }
                }
            }
        },
        new MacroTemplate
        {
            Name = "⚔️ Path of Exile — Ultimatum Auto",
            Description = "Auto farm PoE: Vision AI tìm icon Ultimatum, auto flask, skill rotation. Dùng Raw/Hardware mode (game cần foreground cho Vision).",
            Category = "Game",
            TargetWindowTitle = "Path of Exile",
            Actions = new List<MacroAction>
            {
                // Variables init
                new SetVariableAction { DisplayName = "⚙️ Init flask_timer", VarName = "flask_timer", Value = "0", Operation = "Set" },

                new RepeatAction
                {
                    DisplayName = "🔄 Main Loop (infinite)",
                    RepeatCount = 0,
                    IntervalMs = 300,
                    LoopActions = new List<MacroAction>
                    {
                        // ═══ FLASK MANAGEMENT ═══
                        // Increment flask timer
                        new SetVariableAction { DisplayName = "flask_timer++", VarName = "flask_timer", Value = "1", Operation = "Increment" },

                        // Every 15 loops (~5s) → press all flasks
                        new IfVariableAction
                        {
                            DisplayName = "🧪 IF flask_timer >= 15 → Use Flasks",
                            VarName = "flask_timer",
                            CompareOp = ">=",
                            Value = "15",
                            ThenActions = new List<MacroAction>
                            {
                                new KeyPressAction { DisplayName = "Flask 1", KeyName = "D1", VirtualKeyCode = 0x31, HoldDurationMs = 30, InputMode = KeyInputMode.RawInput },
                                new WaitAction { DisplayName = "delay", DelayMin = 50, DelayMax = 80 },
                                new KeyPressAction { DisplayName = "Flask 2", KeyName = "D2", VirtualKeyCode = 0x32, HoldDurationMs = 30, InputMode = KeyInputMode.RawInput },
                                new WaitAction { DisplayName = "delay", DelayMin = 50, DelayMax = 80 },
                                new KeyPressAction { DisplayName = "Flask 3", KeyName = "D3", VirtualKeyCode = 0x33, HoldDurationMs = 30, InputMode = KeyInputMode.RawInput },
                                new WaitAction { DisplayName = "delay", DelayMin = 50, DelayMax = 80 },
                                new KeyPressAction { DisplayName = "Flask 4", KeyName = "D4", VirtualKeyCode = 0x34, HoldDurationMs = 30, InputMode = KeyInputMode.RawInput },
                                new WaitAction { DisplayName = "delay", DelayMin = 50, DelayMax = 80 },
                                new KeyPressAction { DisplayName = "Flask 5", KeyName = "D5", VirtualKeyCode = 0x35, HoldDurationMs = 30, InputMode = KeyInputMode.RawInput },
                                new WaitAction { DisplayName = "delay", DelayMin = 30, DelayMax = 60 },
                                new SetVariableAction { DisplayName = "Reset flask_timer", VarName = "flask_timer", Value = "0", Operation = "Set" },
                            },
                            ElseActions = new List<MacroAction>()
                        },

                        // ═══ ULTIMATUM ICON DETECTION ═══
                        // Multi-image search: add your Ultimatum option icons here
                        new IfImageAction
                        {
                            DisplayName = "👁️ Scan Ultimatum Icons",
                            ImagePath = "",
                            ImagePaths = new List<string>(), // User adds their icon screenshots here
                            Threshold = 0.70,
                            TimeoutMs = 1000,
                            RetryUntilFound = false,
                            ClickOnFound = true,
                            ClickMode = ClickMode.Raw,
                            RandomOffset = 5,
                            ThenActions = new List<MacroAction>
                            {
                                new LogAction { DisplayName = "📝 Log found", Message = "Found Ultimatum icon: {{foundImageName}} at ({{image_x}}, {{image_y}})" },
                                new WaitAction { DisplayName = "Wait after click", DelayMin = 300, DelayMax = 600 },
                            },
                            ElseActions = new List<MacroAction>
                            {
                                // Not in Ultimatum — continue normal farming
                            }
                        },

                        // ═══ SKILL ROTATION ═══
                        // Right-click (main skill) — hold for channeling builds
                        new ClickAction
                        {
                            DisplayName = "⚔️ Main Skill (Right Click center)",
                            X = 960, Y = 540,
                            Button = MouseButton.Right,
                            Mode = ClickMode.Raw,
                        },
                        new WaitAction { DisplayName = "Skill delay", DelayMin = 100, DelayMax = 200 },

                        // Movement skill (optional — user configures key)
                        new KeyPressAction { DisplayName = "🏃 Move Skill (W)", KeyName = "W", VirtualKeyCode = 0x57, HoldDurationMs = 50, InputMode = KeyInputMode.RawInput },
                        new WaitAction { DisplayName = "Move delay", DelayMin = 200, DelayMax = 400 },
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

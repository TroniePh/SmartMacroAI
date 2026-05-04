// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System.Text.Json.Serialization;
using SmartMacroAI.Localization;

namespace SmartMacroAI.Models;

/// <summary>
/// Runs another macro script as a sub-workflow.
/// Variables can be passed from the parent to the child macro.
/// Supports both waiting for completion and fire-and-forget execution.
/// </summary>
[JsonDerivedType(typeof(CallMacroAction), "CallMacro")]
public class CallMacroAction : MacroAction
{
    /// <summary>
    /// Full path to the .json macro file to invoke.
    /// </summary>
    public string MacroFilePath { get; set; } = "";

    /// <summary>
    /// Display name of the target macro (auto-populated when file is selected).
    /// </summary>
    public string MacroName { get; set; } = "";

    /// <summary>
    /// When true, CSV and runtime variables from the parent are passed
    /// to the child macro's variable store.
    /// </summary>
    public bool PassVariables { get; set; } = true;

    /// <summary>
    /// When true, the parent waits for the child macro to finish before
    /// continuing. When false, the child runs in parallel (fire-and-forget).
    /// </summary>
    public bool WaitForFinish { get; set; } = true;

    public CallMacroAction()
    {
        DisplayName = LanguageManager.GetString("ui_Action_CallMacro");
    }
}

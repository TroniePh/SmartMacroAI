using SmartMacroAI.Models;

namespace SmartMacroAI.Core;

/// <summary>
/// Classifies a script for HWND requirements and Playwright-only execution.
/// </summary>
public static class MacroScriptValidation
{
    public sealed record ScriptCompatibility(bool RequiresDesktopTarget, bool IsWebOnly);

    public static ScriptCompatibility ValidateScriptCompatibility(MacroScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        bool requiresDesktop = ActionsRequireDesktop(script.Actions);
        bool hasWeb = ActionsContainWebAutomation(script.Actions);
        bool webOnly = !requiresDesktop && hasWeb;
        return new ScriptCompatibility(requiresDesktop, webOnly);
    }

    /// <summary>
    /// Infinite repeat (<see cref="MacroScript.RepeatCount"/> ≤ 0) is allowed unless a loop CSV is configured.
    /// </summary>
    public static void ValidateRepeatAndLoopCsv(MacroScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        if (script.RepeatCount > 0)
            return;

        if (!string.IsNullOrWhiteSpace(script.LoopCsvFilePath))
        {
            throw new InvalidOperationException(
                "Repeat cannot be 0 (infinite) while a Loop CSV path is set. " +
                "Use a finite repeat count that matches your CSV rows, or clear the CSV loop.");
        }
    }

    private static bool ActionsRequireDesktop(IReadOnlyList<MacroAction> actions)
    {
        foreach (var a in actions)
        {
            if (ActionRequiresDesktop(a))
                return true;
        }

        return false;
    }

    private static bool ActionRequiresDesktop(MacroAction action) => action switch
    {
        ClickAction or TypeAction or IfImageAction or IfTextAction => true,
        RepeatAction rep => ActionsRequireDesktop(rep.LoopActions),
        TryCatchAction tc => ActionsRequireDesktop(tc.TryActions) || ActionsRequireDesktop(tc.CatchActions),
        IfVariableAction iv => ActionsRequireDesktop(iv.ThenActions) || ActionsRequireDesktop(iv.ElseActions),
        SetVariableAction or LogAction => false,
        _ => false,
    };

    private static bool ActionsContainWebAutomation(IReadOnlyList<MacroAction> actions)
    {
        foreach (var a in actions)
        {
            if (ActionContainsWebAutomation(a))
                return true;
        }

        return false;
    }

    private static bool ActionContainsWebAutomation(MacroAction action) => action switch
    {
        WebAction or WebNavigateAction or WebClickAction or WebTypeAction => true,
        IfImageAction img =>
            ActionsContainWebAutomation(img.ThenActions)
            || ActionsContainWebAutomation(img.ElseActions),
        IfTextAction txt =>
            ActionsContainWebAutomation(txt.ThenActions)
            || ActionsContainWebAutomation(txt.ElseActions),
        RepeatAction rep => ActionsContainWebAutomation(rep.LoopActions),
        TryCatchAction tc => ActionsContainWebAutomation(tc.TryActions) || ActionsContainWebAutomation(tc.CatchActions),
        IfVariableAction iv => ActionsContainWebAutomation(iv.ThenActions) || ActionsContainWebAutomation(iv.ElseActions),
        SetVariableAction or LogAction => false,
        _ => false,
    };
}

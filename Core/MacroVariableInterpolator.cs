using System.Text.RegularExpressions;

namespace SmartMacroAI.Core;

/// <summary>
/// Expands <c>${VariableName}</c> placeholders using the current runtime variable map.
/// Unknown names are left unchanged.
/// </summary>
public static class MacroVariableInterpolator
{
    private static readonly Regex Placeholder = new(
        @"\$\{([^}]+)\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Expand(string input, IReadOnlyDictionary<string, string> vars)
    {
        if (string.IsNullOrEmpty(input) || vars.Count == 0)
            return input;

        return Placeholder.Replace(input, m =>
        {
            string key = m.Groups[1].Value.Trim();
            return vars.TryGetValue(key, out string? v) ? v : m.Value;
        });
    }
}

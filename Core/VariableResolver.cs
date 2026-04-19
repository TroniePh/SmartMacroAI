// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SmartMacroAI.Core;

/// <summary>
/// Resolves <c>{{key}}</c> placeholders in a template string using values from a data row.
/// Lookup is case-insensitive. Keys not found in the row are left unchanged (no crash).
/// Created by Phạm Duy – Giải pháp tự động hóa thông minh.
/// </summary>
public static partial class VariableResolver
{
    [GeneratedRegex(@"\{\{([^}]+)\}\}", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex DoubleCurlyRegex();

    /// <summary>
    /// Replaces every <c>{{key}}</c> in <paramref name="template"/> with the corresponding
    /// value from <paramref name="row"/>. The lookup is case-insensitive. If a key is not
    /// present in the row, the <c>{{key}}</c> token is left as-is.
    /// </summary>
    /// <param name="template">String potentially containing {{key}} tokens.</param>
    /// <param name="row">Dictionary of column-name → column-value from a CSV row.</param>
    /// <returns>A new string with all matched tokens replaced.</returns>
    public static string Resolve(string template, Dictionary<string, string> row)
    {
        if (string.IsNullOrEmpty(template))
            return template;

        if (row == null || row.Count == 0)
            return template;

        return DoubleCurlyRegex().Replace(template, m =>
        {
            string key = m.Groups[1].Value.Trim();

            // Case-insensitive lookup
            foreach (var kvp in row)
            {
                if (string.Equals(kvp.Key, key, System.StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }

            return m.Value; // key not found — leave placeholder unchanged
        });
    }

    /// <summary>
    /// Overload that accepts an <c>IReadOnlyDictionary&lt;string, string&gt;</c>, useful when
    /// passing merged variable dictionaries.
    /// </summary>
    public static string Resolve(string template, IReadOnlyDictionary<string, string> row)
    {
        if (string.IsNullOrEmpty(template))
            return template;

        if (row == null || row.Count == 0)
            return template;

        return DoubleCurlyRegex().Replace(template, m =>
        {
            string key = m.Groups[1].Value.Trim();

            foreach (var kvp in row)
            {
                if (string.Equals(kvp.Key, key, System.StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }

            return m.Value;
        });
    }
}

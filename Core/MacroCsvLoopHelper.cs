using System.IO;

namespace SmartMacroAI.Core;

/// <summary>
/// Minimal CSV reader for data-driven macro loops (comma-separated, no embedded commas in fields).
/// </summary>
public static class MacroCsvLoopHelper
{
    public static List<string[]> ReadDataRows(string path, bool hasHeader)
    {
        var rows = new List<string[]>();
        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            rows.Add(line.Split(','));
        }

        if (hasHeader && rows.Count > 0)
            rows.RemoveAt(0);

        return rows;
    }
}

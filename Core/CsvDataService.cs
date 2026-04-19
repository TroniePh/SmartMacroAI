// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;
using System.Windows;

namespace SmartMacroAI.Core;

/// <summary>
/// Loads .csv or pipe-separated .txt files via OpenFileDialog and returns a list of
/// dictionaries keyed by normalized header names (lowercased, spaces stripped).
/// Created by Phạm Duy – Giải pháp tự động hóa thông minh.
/// </summary>
public static class CsvDataService
{
    private static readonly char[] Separators = ['|', ','];

    /// <summary>
    /// Opens a file dialog filtered for CSV and pipe-TXT files, parses the selected file,
    /// and returns a list of row dictionaries. Row 0 headers are normalized as variable keys.
    /// Returns null if the user cancels or validation fails.
    /// </summary>
    public static List<Dictionary<string, string>>? LoadCsvFile()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "CSV/TXT files (*.csv;*.txt)|*.csv;*.txt|All files (*.*)|*.*",
            Title = "Chọn file dữ liệu CSV hoặc TXT",
        };

        if (dlg.ShowDialog() != true)
            return null;

        string path = dlg.FileName;

        try
        {
            var rows = ParseFile(path);
            if (rows.Count == 0)
            {
                MessageBox.Show(
                    "File trống hoặc không có dòng dữ liệu.",
                    "Dữ liệu rỗng",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return null;
            }

            return rows;
        }
        catch (CsvParseException ex)
        {
            MessageBox.Show(ex.Message, "Lỗi định dạng file", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    private static List<Dictionary<string, string>> ParseFile(string path)
    {
        var allLines = File.ReadAllLines(path, Encoding.UTF8);

        if (allLines.Length == 0)
            throw new CsvParseException("File trống.");

        var nonEmpty = new List<string[]>();
        char separator = DetectSeparator(allLines[0]);

        foreach (string line in allLines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var cells = SplitLine(line, separator);
            if (cells.Length == 0)
                continue;

            if (nonEmpty.Count == 0 && string.IsNullOrWhiteSpace(cells[0]))
                continue;

            nonEmpty.Add(cells);
        }

        if (nonEmpty.Count == 0)
            throw new CsvParseException("File không có dòng dữ liệu hợp lệ.");

        int headerCount = nonEmpty[0].Length;
        if (headerCount == 0)
            throw new CsvParseException("File thiếu header hoặc header trống.");

        var headers = new string[headerCount];
        for (int i = 0; i < headerCount; i++)
        {
            string raw = nonEmpty[0][i].Trim();
            if (string.IsNullOrEmpty(raw))
                throw new CsvParseException($"Header cột {i + 1} trống. Vui lòng điền tên cho mỗi cột.");
            headers[i] = NormalizeKey(raw);
        }

        if (nonEmpty.Count < 2)
            throw new CsvParseException("File chỉ có header, không có dòng dữ liệu.");

        var result = new List<Dictionary<string, string>>();
        for (int r = 1; r < nonEmpty.Count; r++)
        {
            string[] cells = nonEmpty[r];
            if (cells.Length != headerCount)
                throw new CsvParseException(
                    $"Dòng {r + 1} có {cells.Length} cột nhưng header có {headerCount} cột. " +
                    "Số cột phải khớp với header.");

            var row = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            for (int c = 0; c < headerCount; c++)
            {
                row[headers[c]] = cells[c].Trim();
            }
            result.Add(row);
        }

        return result;
    }

    private static char DetectSeparator(string firstLine)
    {
        int pipeCount = 0;
        int commaCount = 0;

        foreach (char ch in firstLine)
        {
            if (ch == '|') pipeCount++;
            else if (ch == ',') commaCount++;
        }

        if (pipeCount > 0 || commaCount > 0)
            return pipeCount >= commaCount ? '|' : ',';

        return '|';
    }

    private static string[] SplitLine(string line, char separator)
    {
        return line.Split(separator);
    }

    /// <summary>
    /// Strips spaces and converts to lowercase so that header names
    /// become consistent variable keys.
    /// </summary>
    private static string NormalizeKey(string raw)
    {
        return raw.Replace(" ", "").Trim().ToLowerInvariant();
    }

    public class CsvParseException : Exception
    {
        public CsvParseException(string message) : base(message) { }
    }
}

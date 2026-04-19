// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace SmartMacroAI.Core;

/// <summary>
/// Exports a macro script JSON to a compact "SMA-..." shareable code string using GZip compression
/// and URL-safe Base64 encoding. Import reverses the process.
/// Created by Phạm Duy – Giải pháp tự động hóa thông minh.
/// </summary>
public static class ScriptShareService
{
    private const string Prefix = "SMA-";

    /// <summary>
    /// Compresses the JSON string and encodes it as a URL-safe "SMA-..." code.
    /// </summary>
    /// <param name="json">Serialized macro script JSON.</param>
    /// <returns>An "SMA-..." shareable code string.</returns>
    public static string Export(string json)
    {
        if (string.IsNullOrEmpty(json))
            throw new ArgumentException("JSON content is empty.", nameof(json));

        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

        using var output = new MemoryStream();

        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(jsonBytes, 0, jsonBytes.Length);
        }

        byte[] compressed = output.ToArray();
        string base64 = Base64UrlEncode(compressed);
        return Prefix + base64;
    }

    /// <summary>
    /// Decodes an "SMA-..." code string back into its original JSON.
    /// Validates that the decoded content is valid JSON.
    /// </summary>
    /// <param name="code">An "SMA-..." code string previously created by <see cref="Export"/>.</param>
    /// <returns>The original macro script JSON string.</returns>
    public static string Import(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ShareCodeException("Mã SMA- trống. Vui lòng dán mã đầy đủ.");

        string trimmed = code.Trim()
            .Replace("\r", "")
            .Replace("\n", "")
            .Replace(" ", "");

        if (!trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            throw new ShareCodeException(
                $"Mã không hợp lệ — phải bắt đầu bằng \"{Prefix}\". Mã bạn dán: \"{Truncate(trimmed, 10)}...\"");

        string base64 = trimmed.Substring(Prefix.Length);

        if (base64.Length < 4)
            throw new ShareCodeException("Mã quá ngắn — có thể bị cắt mất. Vui lòng copy lại toàn bộ.");

        byte[] compressed;
        try
        {
            compressed = Base64UrlDecode(base64);
        }
        catch (FormatException ex)
        {
            throw new ShareCodeException($"Mã không đúng định dạng Base64Url: {ex.Message}", ex);
        }

        if (compressed.Length < 2)
            throw new ShareCodeException("Dữ liệu nén bị lỗi (quá ngắn). Kiểm tra lại mã.");

        string json;
        try
        {
            using var input = new MemoryStream(compressed);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var decompressed = new MemoryStream();
            gzip.CopyTo(decompressed);
            byte[] jsonBytes = decompressed.ToArray();
            json = Encoding.UTF8.GetString(jsonBytes);
        }
        catch (InvalidDataException ex)
        {
            throw new ShareCodeException("Dữ liệu nén bị lỗi — mã có thể bị hỏng hoặc không phải định dạng SmartMacroAI.", ex);
        }

        if (string.IsNullOrWhiteSpace(json))
            throw new ShareCodeException("Nội dung giải nén trống.");

        try
        {
            JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new ShareCodeException($"Nội dung không phải JSON hợp lệ: {ex.Message}", ex);
        }

        return json;
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static byte[] Base64UrlDecode(string base64Url)
    {
        string padded = base64Url
            .Replace('-', '+')
            .Replace('_', '/');

        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "…");

    public class ShareCodeException : Exception
    {
        public ShareCodeException(string message) : base(message) { }
        public ShareCodeException(string message, Exception inner) : base(message, inner) { }
    }
}

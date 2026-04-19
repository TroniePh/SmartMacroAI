// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace SmartMacroAI.Core;

/// <summary>
/// Sends HTML-formatted messages to Telegram using the Bot API.
/// Uses a singleton HttpClient to avoid socket exhaustion.
/// Created by Phạm Duy – Giải pháp tự động hóa thông minh.
/// </summary>
public static class TelegramService
{
    private static readonly HttpClient _httpClient;

    static TelegramService()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = true };
        _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "SmartMacroAI/1.0");
    }

    /// <summary>
    /// Sends an HTML message to the specified Telegram chat.
    /// Errors are logged but never thrown — the macro continues running.
    /// A machine-name footer is automatically appended so users know
    /// which PC sent the message when running across multiple machines.
    /// </summary>
    /// <param name="botToken">Bot token from @BotFather.</param>
    /// <param name="chatId">Target chat ID or @channel_username.</param>
    /// <param name="message">HTML-formatted message body.</param>
    /// <param name="onLog">Optional logging callback. When null, errors are silently swallowed.</param>
    /// <returns>True if the message was sent successfully; false otherwise.</returns>
    public static async Task<bool> SendAsync(
        string botToken,
        string chatId,
        string message,
        Action<string>? onLog = null)
    {
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
        {
            onLog?.Invoke("[Telegram] BotToken hoặc ChatId trống — bỏ qua gửi.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            onLog?.Invoke("[Telegram] Message trống — bỏ qua gửi.");
            return false;
        }

        string pcName = Environment.MachineName;
        string footer = $"\n\n<i>[PC: {EscapeHtml(pcName)}]</i>";
        string bodyWithFooter = message + footer;

        var payload = new
        {
            chat_id = chatId.Trim(),
            text = bodyWithFooter,
            parse_mode = "HTML",
        };

        string json = JsonSerializer.Serialize(payload);
        string url = $"https://api.telegram.org/bot{botToken.Trim()}/sendMessage";

        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            HttpResponseMessage response = await _httpClient.PostAsync(url, content, cts.Token)
                .ConfigureAwait(false);

            string responseBody = await response.Content.ReadAsStringAsync(cts.Token)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                onLog?.Invoke($"[Telegram] Đã gửi thành công → {chatId}");
                return true;
            }

            string error = ExtractTelegramError(responseBody) ?? $"HTTP {response.StatusCode}";
            onLog?.Invoke($"[Telegram] Lỗi gửi: {error}");
            return false;
        }
        catch (TaskCanceledException) when (!CancellationToken.None.IsCancellationRequested)
        {
            onLog?.Invoke("[Telegram] Lỗi: hết thời gian kết nối (10s). Kiểm tra Internet hoặc Bot Token.");
            return false;
        }
        catch (HttpRequestException ex)
        {
            onLog?.Invoke($"[Telegram] Lỗi kết nối: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            onLog?.Invoke($"[Telegram] Lỗi không xác định: {ex.Message}");
            return false;
        }
    }

    private static string? ExtractTelegramError(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            JsonElement root = doc.RootElement;
            if (root.TryGetProperty("description", out JsonElement desc))
                return desc.GetString();
        }
        catch { }

        return null;
    }

    private static string EscapeHtml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }
}

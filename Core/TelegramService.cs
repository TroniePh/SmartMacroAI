// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using SmartMacroAI.Localization;

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
            onLog?.Invoke(LanguageManager.GetString("ui_TgLog_EmptyCredentials"));
            return false;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            onLog?.Invoke(LanguageManager.GetString("ui_TgLog_EmptyMessage"));
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
                onLog?.Invoke(string.Format(LanguageManager.GetString("ui_TgLog_Sent"), chatId));
                return true;
            }

            string error = ExtractTelegramError(responseBody) ?? $"HTTP {response.StatusCode}";
            onLog?.Invoke(string.Format(LanguageManager.GetString("ui_TgLog_SendError"), error));
            return false;
        }
        catch (TaskCanceledException) when (!CancellationToken.None.IsCancellationRequested)
        {
            onLog?.Invoke(LanguageManager.GetString("ui_TgLog_Timeout"));
            return false;
        }
        catch (HttpRequestException ex)
        {
            onLog?.Invoke(string.Format(LanguageManager.GetString("ui_TgLog_ConnectionError"), ex.Message));
            return false;
        }
        catch (Exception ex)
        {
            onLog?.Invoke(string.Format(LanguageManager.GetString("ui_TgLog_UnknownError"), ex.Message));
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

    /// <summary>
    /// Sends a photo with optional HTML caption to the specified Telegram chat.
    /// Errors are logged but never thrown.
    /// </summary>
    public static async Task<bool> SendPhotoAsync(
        string botToken,
        string chatId,
        string imagePath,
        string caption = "",
        Action<string>? onLog = null)
    {
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
        {
            onLog?.Invoke(LanguageManager.GetString("ui_TgLog_EmptyCredentialsPhoto"));
            return false;
        }

        if (!File.Exists(imagePath))
        {
            onLog?.Invoke(string.Format(LanguageManager.GetString("ui_TgLog_PhotoNotFound"), imagePath));
            return false;
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

            using var content = new MultipartFormDataContent();

            content.Add(new StringContent(chatId.Trim()), "chat_id");

            if (!string.IsNullOrEmpty(caption))
            {
                content.Add(new StringContent(caption), "caption");
                content.Add(new StringContent("HTML"), "parse_mode");
            }

            using var stream = File.OpenRead(imagePath);
            var streamContent = new StreamContent(stream);
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            content.Add(streamContent, "photo", Path.GetFileName(imagePath));

            string url = $"https://api.telegram.org/bot{botToken.Trim()}/sendPhoto";

            HttpResponseMessage response = await http.PostAsync(url, content)
                .ConfigureAwait(false);

            string responseBody = await response.Content.ReadAsStringAsync()
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                onLog?.Invoke(string.Format(LanguageManager.GetString("ui_TgLog_PhotoSent"), chatId));
                return true;
            }

            string error = ExtractTelegramError(responseBody) ?? $"HTTP {response.StatusCode}";
            onLog?.Invoke(string.Format(LanguageManager.GetString("ui_TgLog_PhotoSendError"), error));
            return false;
        }
        catch (Exception ex)
        {
            onLog?.Invoke(string.Format(LanguageManager.GetString("ui_TgLog_PhotoError"), ex.Message));
            return false;
        }
    }
}

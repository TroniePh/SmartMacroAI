// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SmartMacroAI.Localization;

namespace SmartMacroAI.Core;

/// <summary>
/// Client for AdsPower Local API (http://local.adspower.net:50325).
/// Handles browser profile start/stop for multi-account web automation.
/// Created by Phạm Duy – Giải pháp tự động hóa thông minh.
/// </summary>
public sealed class AdsPowerService : IDisposable
{
    private const string BaseUrl = "http://local.adspower.net:50325/api/v1";
    private static readonly HttpClient _httpClient;

    static AdsPowerService()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = true };
        _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "SmartMacroAI/1.0");
    }

    /// <summary>
    /// Starts an AdsPower browser profile and returns the WebSocket CDP endpoint URL.
    /// </summary>
    /// <param name="profileId">AdsPower profile user_id.</param>
    /// <param name="cancellationToken">Cancellation token (15s timeout built-in).</param>
    /// <returns>The CDP WebSocket endpoint, e.g. "ws://127.0.0.1:50325/..."</returns>
    public async Task<string> StartProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            throw new ArgumentException("Profile ID cannot be empty.", nameof(profileId));

        string url = $"{BaseUrl}/browser/start?user_id={Uri.EscapeDataString(profileId)}";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        try
        {
            string responseJson = await _httpClient.GetStringAsync(url, cts.Token).ConfigureAwait(false);
            return ParseStartResponse(responseJson);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                LanguageManager.GetString("ui_AdsPower_Timeout"));
        }
        catch (HttpRequestException ex)
        {
            throw new IOException(
                string.Format(LanguageManager.GetString("ui_AdsPower_ConnectionFailed"), BaseUrl), ex);
        }
    }

    /// <summary>
    /// Stops an AdsPower browser profile.
    /// </summary>
    /// <param name="profileId">AdsPower profile user_id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StopProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return;

        string url = $"{BaseUrl}/browser/stop?user_id={Uri.EscapeDataString(profileId)}";

        try
        {
            await _httpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // Non-critical — browser may already be closed
        }
        catch (TaskCanceledException)
        {
            // Ignore cancellation
        }
    }

    private static string ParseStartResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("code", out JsonElement codeEl) && codeEl.TryGetInt32(out int code) && code != 0)
            {
                string msg = root.TryGetProperty("msg", out JsonElement msgEl)
                    ? msgEl.GetString() ?? "Unknown error"
                    : "Unknown error";
                throw new InvalidOperationException(
                    string.Format(LanguageManager.GetString("ui_AdsPower_ApiError"), code, msg));
            }

            if (root.TryGetProperty("data", out JsonElement data))
            {
                // Primary path: data.ws.puppeteer
                if (data.TryGetProperty("ws", out JsonElement ws) &&
                    ws.TryGetProperty("puppeteer", out JsonElement puppeteerEl))
                {
                    string? endpoint = puppeteerEl.GetString();
                    if (!string.IsNullOrWhiteSpace(endpoint))
                        return endpoint;
                }

                // Fallback: data.ws
                if (ws.TryGetProperty("selenium", out JsonElement seleniumEl))
                {
                    string? endpoint = seleniumEl.GetString();
                    if (!string.IsNullOrWhiteSpace(endpoint))
                        return endpoint;
                }

                // data.remote_debugging_port for CDP
                if (data.TryGetProperty("remote_debugging_port", out JsonElement portEl) &&
                    portEl.TryGetInt32(out int port))
                {
                    return $"http://127.0.0.1:{port}";
                }

                // data.http for direct CDP
                if (data.TryGetProperty("http", out JsonElement httpEl))
                {
                    string? endpoint = httpEl.GetString();
                    if (!string.IsNullOrWhiteSpace(endpoint))
                        return endpoint;
                }
            }

            throw new InvalidOperationException(
                LanguageManager.GetString("ui_AdsPower_NoWebSocket") +
                Truncate(json, 200));
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(string.Format(LanguageManager.GetString("ui_AdsPower_InvalidJson"), ex.Message), ex);
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "…");

    public void Dispose() { GC.SuppressFinalize(this); }
}

/// <summary>
/// Persistent configuration for an AdsPower browser profile, mapped from a CSV column or UI.
/// </summary>
public sealed class AdsPowerProfileEntry
{
    [JsonPropertyName("profile_id")]
    public string ProfileId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("proxy_host")]
    public string ProxyHost { get; set; } = string.Empty;

    [JsonPropertyName("proxy_port")]
    public string ProxyPort { get; set; } = string.Empty;

    [JsonPropertyName("proxy_user")]
    public string ProxyUser { get; set; } = string.Empty;

    [JsonPropertyName("proxy_password")]
    public string ProxyPassword { get; set; } = string.Empty;

    public string ProxySummary =>
        string.IsNullOrWhiteSpace(ProxyHost)
            ? "(none)"
            : $"{ProxyHost}:{ProxyPort}";
}

/// <summary>
/// Stores the list of AdsPower profiles in config/adspower_profiles.json.
/// </summary>
public sealed class AdsPowerProfileStore
{
    private static readonly string ConfigDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "config");

    private static readonly string StorePath = Path.Combine(ConfigDir, "adspower_profiles.json");

    public List<AdsPowerProfileEntry> Profiles { get; set; } = [];

    public static AdsPowerProfileStore Load()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                string json = File.ReadAllText(StorePath);
                return JsonSerializer.Deserialize<AdsPowerProfileStore>(json) ?? new AdsPowerProfileStore();
            }
        }
        catch { }

        return new AdsPowerProfileStore();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StorePath, json);
        }
        catch { }
    }
}

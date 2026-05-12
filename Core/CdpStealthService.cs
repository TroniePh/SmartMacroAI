// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace SmartMacroAI.Core;

/// <summary>
/// Lightweight CDP (Chrome DevTools Protocol) client for background clicking
/// on Chromium-based windows (Edge, Chrome) without requiring window visibility.
/// Uses <c>Input.dispatchMouseEvent</c> via WebSocket.
/// Falls back gracefully — returns false if CDP is unavailable.
/// </summary>
public static class CdpStealthService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private static readonly int[] DebugPorts = [9222, 9223, 9224, 9225, 9226, 9227, 9228, 9229];

    /// <summary>Cache: port → (WebSocket, webSocketDebuggerUrl) to avoid reconnecting every click.</summary>
    private static readonly ConcurrentDictionary<int, CachedConnection> _connectionCache = new();

    /// <summary>Cache: hwnd → found port (0 = not available, checked recently).</summary>
    private static readonly ConcurrentDictionary<IntPtr, (int Port, DateTime CheckedAt)> _portCache = new();

    /// <summary>How long to cache a "no CDP" result before re-checking.</summary>
    private static readonly TimeSpan PortCacheExpiry = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(2);
    private static int _jsonRpcId;

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    /// <summary>
    /// Tries to find the CDP debug port for the process owning the given hwnd.
    /// Scans common debug ports (9222–9229) and verifies connectivity.
    /// Results are cached to avoid repeated slow scans.
    /// Returns 0 if not found (browser not launched with --remote-debugging-port).
    /// </summary>
    public static int FindDebugPort(IntPtr hwnd)
    {
        try
        {
            // Check cache first — avoid re-scanning every click
            if (_portCache.TryGetValue(hwnd, out var cached))
            {
                if (cached.Port > 0 || DateTime.UtcNow - cached.CheckedAt < PortCacheExpiry)
                    return cached.Port;
            }

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0)
            {
                _portCache[hwnd] = (0, DateTime.UtcNow);
                return 0;
            }

            // Scan known debug ports with very short timeout (150ms each)
            foreach (int port in DebugPorts)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
                    string json = _http.GetStringAsync($"http://127.0.0.1:{port}/json/version", cts.Token)
                        .ConfigureAwait(false).GetAwaiter().GetResult();

                    if (!string.IsNullOrEmpty(json) && json.Contains("webSocketDebuggerUrl", StringComparison.OrdinalIgnoreCase))
                    {
                        _portCache[hwnd] = (port, DateTime.UtcNow);
                        return port;
                    }
                }
                catch
                {
                    // Port not responding — try next
                }
            }

            // No port found — cache this result
            _portCache[hwnd] = (0, DateTime.UtcNow);
        }
        catch
        {
            _portCache[hwnd] = (0, DateTime.UtcNow);
        }

        return 0;
    }

    /// <summary>
    /// Sends a background click at (x, y) client coordinates via CDP.
    /// Returns true if successful, false if CDP not available.
    /// </summary>
    /// <param name="hwnd">Window handle of the browser.</param>
    /// <param name="x">X coordinate (client/viewport-relative).</param>
    /// <param name="y">Y coordinate (client/viewport-relative).</param>
    /// <param name="rightClick">True for right-click, false for left-click.</param>
    /// <param name="token">Cancellation token.</param>
    public static async Task<bool> TryCdpClickAsync(IntPtr hwnd, int x, int y, bool rightClick, CancellationToken token)
    {
        try
        {
            int port = FindDebugPort(hwnd);
            if (port == 0)
                return false;

            var ws = await GetOrCreateConnectionAsync(port, token).ConfigureAwait(false);
            if (ws == null || ws.State != WebSocketState.Open)
            {
                // Evict stale connection and retry once
                _connectionCache.TryRemove(port, out var stale);
                stale?.Dispose();

                ws = await GetOrCreateConnectionAsync(port, token).ConfigureAwait(false);
                if (ws == null || ws.State != WebSocketState.Open)
                    return false;
            }

            string button = rightClick ? "right" : "left";

            // mousePressed
            int id1 = Interlocked.Increment(ref _jsonRpcId);
            string pressMsg = JsonSerializer.Serialize(new
            {
                id = id1,
                method = "Input.dispatchMouseEvent",
                @params = new
                {
                    type = "mousePressed",
                    x,
                    y,
                    button,
                    clickCount = 1
                }
            });

            await SendMessageAsync(ws, pressMsg, token).ConfigureAwait(false);

            // Small delay between press and release for realism
            await Task.Delay(Random.Shared.Next(30, 80), token).ConfigureAwait(false);

            // mouseReleased
            int id2 = Interlocked.Increment(ref _jsonRpcId);
            string releaseMsg = JsonSerializer.Serialize(new
            {
                id = id2,
                method = "Input.dispatchMouseEvent",
                @params = new
                {
                    type = "mouseReleased",
                    x,
                    y,
                    button,
                    clickCount = 1
                }
            });

            await SendMessageAsync(ws, releaseMsg, token).ConfigureAwait(false);

            // Read responses (non-blocking drain)
            await DrainResponsesAsync(ws, token).ConfigureAwait(false);

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Clears all cached WebSocket connections. Call on shutdown or when browser restarts.
    /// </summary>
    public static void ClearCache()
    {
        foreach (var kvp in _connectionCache)
        {
            if (_connectionCache.TryRemove(kvp.Key, out var conn))
                conn.Dispose();
        }
    }

    #region Private helpers

    private static async Task<ClientWebSocket?> GetOrCreateConnectionAsync(int port, CancellationToken token)
    {
        if (_connectionCache.TryGetValue(port, out var cached) &&
            cached.WebSocket.State == WebSocketState.Open)
        {
            return cached.WebSocket;
        }

        // Get page list from CDP
        string? wsUrl = await GetFirstPageWsUrlAsync(port, token).ConfigureAwait(false);
        if (string.IsNullOrEmpty(wsUrl))
            return null;

        // Connect WebSocket
        var ws = new ClientWebSocket();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(ConnectionTimeout);

        try
        {
            await ws.ConnectAsync(new Uri(wsUrl), cts.Token).ConfigureAwait(false);
        }
        catch
        {
            ws.Dispose();
            return null;
        }

        var newConn = new CachedConnection(ws, wsUrl);

        // Evict old if exists
        if (_connectionCache.TryRemove(port, out var old))
            old.Dispose();

        _connectionCache[port] = newConn;
        return ws;
    }

    private static async Task<string?> GetFirstPageWsUrlAsync(int port, CancellationToken token)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(ConnectionTimeout);

            string json = await _http.GetStringAsync($"http://127.0.0.1:{port}/json", cts.Token).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                // Find first target with type == "page"
                if (element.TryGetProperty("type", out var typeEl) &&
                    typeEl.GetString()?.Equals("page", StringComparison.OrdinalIgnoreCase) == true &&
                    element.TryGetProperty("webSocketDebuggerUrl", out var wsEl))
                {
                    return wsEl.GetString();
                }
            }

            // Fallback: take first entry with webSocketDebuggerUrl regardless of type
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.TryGetProperty("webSocketDebuggerUrl", out var wsEl))
                    return wsEl.GetString();
            }
        }
        catch
        {
            // fail-safe
        }

        return null;
    }

    private static async Task SendMessageAsync(ClientWebSocket ws, string message, CancellationToken token)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(message);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
    }

    private static async Task DrainResponsesAsync(ClientWebSocket ws, CancellationToken token)
    {
        // Non-blocking read: drain any pending responses with a short timeout
        var buffer = new byte[4096];
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(500));

        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
                // We don't need the response content — just drain it
                if (result.EndOfMessage)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout is expected — we just wanted to drain
        }
        catch
        {
            // fail-safe
        }
    }

    #endregion

    #region CachedConnection

    private sealed class CachedConnection : IDisposable
    {
        public ClientWebSocket WebSocket { get; }
        public string WsUrl { get; }

        public CachedConnection(ClientWebSocket ws, string wsUrl)
        {
            WebSocket = ws;
            WsUrl = wsUrl;
        }

        public void Dispose()
        {
            try
            {
                if (WebSocket.State == WebSocketState.Open)
                    WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None)
                        .ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch { }

            WebSocket.Dispose();
        }
    }

    #endregion
}

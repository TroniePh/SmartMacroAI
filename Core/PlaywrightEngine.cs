// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace SmartMacroAI.Core;

/// <summary>
/// Browser launch mode for web automation actions.
/// </summary>
public enum BrowserMode
{
    /// <summary>Use the system's installed Chromium (default, no extra setup).</summary>
    Internal,

    /// <summary>Connect to an AdsPower-managed browser profile via CDP.</summary>
    AdsPower,
}

/// <summary>
/// One Playwright browser + page per macro execution context.
/// Headful mode so operators can watch web steps. Desktop automation remains on Win32.
/// Created by Phạm Duy – Giải pháp tự động hóa thông minh.
/// </summary>
public sealed class PlaywrightEngine : IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;
    private readonly AdsPowerService _adsPower = new();

    /// <summary>Which browser launch mode to use. Default is <see cref="BrowserMode.Internal"/>.</summary>
    public BrowserMode Mode { get; set; } = BrowserMode.Internal;

    /// <summary>
    /// The AdsPower profile ID to launch when <see cref="Mode"/> is <see cref="BrowserMode.AdsPower"/>.
    /// Set this before the first web action is executed.
    /// </summary>
    public string? AdsPowerProfileId { get; set; }

    /// <summary>
    /// Creates Playwright, launches Chromium (non-headless), and opens a page if needed.
    /// Runs the heavy initialization on a thread-pool thread to guarantee no UI blocking.
    /// </summary>
    public async Task EnsureBrowserStartedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_page is not null)
            return;

        await Task.Run(async () =>
        {
            _playwright = await Playwright.CreateAsync().ConfigureAwait(false);

            if (Mode == BrowserMode.AdsPower && !string.IsNullOrWhiteSpace(AdsPowerProfileId))
            {
                _browser = await LaunchAdsPowerBrowserAsync(AdsPowerProfileId, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = false,
                }).ConfigureAwait(false);
            }

            _context = await _browser.NewContextAsync().ConfigureAwait(false);
            _page = await _context.NewPageAsync().ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IBrowser> LaunchAdsPowerBrowserAsync(string profileId, CancellationToken ct)
    {
        string wsEndpoint = await _adsPower.StartProfileAsync(profileId, ct).ConfigureAwait(false);

        // wsEndpoint can be a ws:// URL or a http:// CDP endpoint
        bool isWebSocket = wsEndpoint.StartsWith("ws://", StringComparison.OrdinalIgnoreCase);

        if (isWebSocket)
        {
            return await _playwright!.Chromium.ConnectOverCDPAsync(wsEndpoint).ConfigureAwait(false);
        }
        else
        {
            return await _playwright!.Chromium.ConnectOverCDPAsync(wsEndpoint).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Navigates the Playwright page to <paramref name="url"/>.
    /// </summary>
    public async Task MapsAsync(string url, CancellationToken cancellationToken = default)
    {
        await EnsureBrowserStartedAsync(cancellationToken).ConfigureAwait(false);

        await _page!.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded })
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ClickSelectorAsync(string selector, CancellationToken cancellationToken = default)
    {
        await EnsureBrowserStartedAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await _page!.ClickAsync(selector).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task TypeSelectorAsync(string selector, string text, CancellationToken cancellationToken = default)
    {
        await EnsureBrowserStartedAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await _page!.FillAsync(selector, text).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ScrapeSelectorAsync(string selector, CancellationToken cancellationToken = default)
    {
        await EnsureBrowserStartedAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var element = await _page!.QuerySelectorAsync(selector).ConfigureAwait(false);
        if (element is null) return string.Empty;
        return await element.InnerTextAsync().ConfigureAwait(false) ?? string.Empty;
    }

    /// <summary>
    /// Stops the AdsPower profile if <see cref="Mode"/> is <see cref="BrowserMode.AdsPower"/>.
    /// Call this when a CSV row completes so each profile is cleanly closed before the next starts.
    /// </summary>
    public async Task StopAdsPowerProfileAsync(CancellationToken cancellationToken = default)
    {
        if (Mode == BrowserMode.AdsPower && !string.IsNullOrWhiteSpace(AdsPowerProfileId))
        {
            await _adsPower.StopProfileAsync(AdsPowerProfileId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_page is not null)
            {
                await _page.CloseAsync().ConfigureAwait(false);
                _page = null;
            }

            if (_context is not null)
            {
                await _context.CloseAsync().ConfigureAwait(false);
                _context = null;
            }

            if (_browser is not null)
            {
                await _browser.CloseAsync().ConfigureAwait(false);
                _browser = null;
            }
        }
        finally
        {
            _playwright?.Dispose();
            _playwright = null;
        }

        _adsPower.Dispose();
        GC.SuppressFinalize(this);
    }
}

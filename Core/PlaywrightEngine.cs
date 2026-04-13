using Microsoft.Playwright;

namespace SmartMacroAI.Core;

/// <summary>
/// One Playwright browser + page per macro execution context.
/// Headful mode so operators can watch web steps. Desktop automation remains on Win32.
///
/// Created by Phạm Duy - Giải pháp tự động hóa thông minh.
/// </summary>
public sealed class PlaywrightEngine : IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;

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

            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = false,
            }).ConfigureAwait(false);

            _page = await _browser.NewPageAsync().ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
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

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_page is not null)
            {
                await _page.CloseAsync().ConfigureAwait(false);
                _page = null;
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
    }
}

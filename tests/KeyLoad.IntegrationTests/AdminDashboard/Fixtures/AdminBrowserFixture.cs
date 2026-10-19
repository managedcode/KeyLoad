using KeyLoad.Server;
using Microsoft.Playwright;

namespace KeyLoad.IntegrationTests.Features.AdminDashboard;

internal sealed class AdminBrowserFixture : IAsyncDisposable
{
    private readonly Lock gate = new();
    private IPlaywright? driver;
    private IBrowser? browser;
    private IBrowserContext? context;
    private CancellationTokenRegistration cancellation;
    private Task? disposal;
    internal IPage Page { get; private set; } = default!;
    internal bool Connected => browser?.IsConnected is true;

    internal static async Task<AdminBrowserFixture> StartAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var owner = new AdminBrowserFixture();
        try
        {
            owner.driver = await Playwright.CreateAsync();
            owner.browser = await owner.driver.Chromium.LaunchAsync(new()
            {
                Headless = true,
                ExecutablePath = Environment.GetEnvironmentVariable(AdminBrowserProtocol.BrowserEnvironment),
                Timeout = AdminBrowserProtocol.ActionTimeout
            });
            owner.context = await owner.browser.NewContextAsync(new()
            {
                ViewportSize = new() { Width = AdminBrowserProtocol.DesktopWidth, Height = AdminBrowserProtocol.Height }
            });
            owner.context.SetDefaultTimeout(AdminBrowserProtocol.ActionTimeout);
            owner.Page = await owner.context.NewPageAsync();
            owner.cancellation = token.Register(() => { _ = owner.DisposeAsync().AsTask(); });
            token.ThrowIfCancellationRequested();
            return owner;
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            await ServerFailureObserver.ObserveAsync(() => owner.DisposeAsync().AsTask(), failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            return new(disposal ??= CloseAsync());
        }
    }

    private async Task CloseAsync()
    {
        var failures = new List<Exception>();
        if (context is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => context.CloseAsync(), failures);
        }
        if (browser is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => browser.CloseAsync(), failures);
        }
        ServerFailureObserver.Observe(() => driver?.Dispose(), failures);
        cancellation.Unregister();
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

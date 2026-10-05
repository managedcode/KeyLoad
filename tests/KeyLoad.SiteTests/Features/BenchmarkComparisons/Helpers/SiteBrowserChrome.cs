using System.Diagnostics;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteBrowserChrome : IAsyncDisposable
{
    private readonly List<JsonElement> coverageSnapshots = [];
    private readonly SiteBrowserProcessStart processOwner;
    private readonly HttpClient http = new()
    {
        Timeout = TimeSpan.FromMilliseconds(SiteBrowserTokens.BrowserCommandTimeoutMilliseconds),
    };
    private SiteBrowserCdpClient? cdp;
    public Process Process => processOwner.Process;
    public SiteBrowserCdpClient Cdp => cdp ?? throw new InvalidOperationException(SiteBrowserTokens.BrowserStartFailure);
    private int disposalStarted;
    private int cdpDisposed;
    private int coverageStopped;

    private SiteBrowserChrome(SiteBrowserProcessStart processOwner, string version, Uri endpoint)
    {
        this.processOwner = processOwner;
        Version = version;
        Endpoint = endpoint;
    }

    public string Version { get; }
    public Uri Endpoint { get; }
    internal bool OriginalProcessExitObserved => processOwner.OriginalProcessExitObserved;
    internal bool CdpDisposed => Volatile.Read(ref cdpDisposed) == SiteBrowserTokens.One;
    internal bool CoverageStopped => Volatile.Read(ref coverageStopped) == SiteBrowserTokens.One;
    private int ownershipTransferredToCaller;

    public static async Task<SiteBrowserChrome> StartAsync(string browserPath, string profilePath,
        CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(browserPath) || !File.Exists(browserPath))
        {
            throw new InvalidOperationException(SiteBrowserTokens.BrowserMissing);
        }

        return await StartOwnedBrowserAsync(browserPath, profilePath, cancellationToken);
    }

    private static async Task<SiteBrowserChrome> StartOwnedBrowserAsync(string browserPath, string profilePath,
        CancellationToken cancellationToken)
    {
        using var lease = await SiteBrowserSessionAdmission.Shared.AcquireAsync(cancellationToken);
        await using var started = await SiteBrowserProcess.StartAsync(browserPath, profilePath, lease, cancellationToken);
        var baseUri = await SiteBrowserTarget.WaitForEndpointFile(started.Process, profilePath, cancellationToken);
        await using var browser = new SiteBrowserChrome(started, started.Version, baseUri);
        await browser.ConnectAndEnableInstrumentation(cancellationToken);
        started.Transfer();
        browser.TransferToCaller();
        lease.TransferToCaller();
        return browser;
    }

    private void TransferToCaller() => Interlocked.Exchange(ref ownershipTransferredToCaller, SiteBrowserTokens.One);

    private async Task ConnectAndEnableInstrumentation(CancellationToken cancellationToken)
    {
        cdp = await SiteBrowserTarget.ConnectPage(http, Endpoint, cancellationToken);
        await EnableInstrumentation(cancellationToken);
    }

    public async Task NavigateAsync(string url, CancellationToken cancellationToken)
    {
        await CaptureCoverageAsync(cancellationToken);
        var response = await SendNavigation(url, cancellationToken);
        await SiteBrowserNavigation.WaitForReadyAsync(Cdp, url, response, cancellationToken);
    }

    public async Task SetDownloadDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        await Cdp.CommandAsync(SiteBrowserTokens.PageSetDownloadBehavior, new Dictionary<string, object?>
        {
            [SiteBrowserTokens.BehaviorField] = SiteBrowserTokens.DownloadBehaviorAllow,
            [SiteBrowserTokens.DownloadPathField] = directory,
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<JsonElement>> CompleteCoverageAsync(CancellationToken cancellationToken)
    {
        await CaptureCoverageAsync(cancellationToken);
        await StopCoverageAsync(cancellationToken);
        Volatile.Write(ref coverageStopped, SiteBrowserTokens.One);
        return coverageSnapshots.Select(snapshot => snapshot.Clone()).ToArray();
    }

    public IReadOnlyList<SiteBrowserError> ReadErrors() => SiteBrowserRuntimeErrors.Read(Cdp);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref ownershipTransferredToCaller, SiteBrowserTokens.Zero) == SiteBrowserTokens.One)
        {
            return;
        }

        if (Interlocked.Exchange(ref disposalStarted, SiteBrowserTokens.One) != SiteBrowserTokens.Zero)
        {
            return;
        }

        var cdpSettled = cdp is null;
        try
        {
            if (cdp is not null)
            {
                await cdp.DisposeAsync();
                Volatile.Write(ref cdpDisposed, SiteBrowserTokens.One);
                cdpSettled = true;
            }
        }
        finally
        {
            var httpSettled = false;
            try
            {
                http.Dispose();
                httpSettled = true;
            }
            finally
            {
                await processOwner.StopAndDisposeAsync(cdpSettled && httpSettled);
            }
        }
    }

    private async Task EnableInstrumentation(CancellationToken cancellationToken)
    {
        await Cdp.CommandAsync(SiteBrowserTokens.RuntimeEnable, null, cancellationToken);
        await Cdp.CommandAsync(SiteBrowserTokens.LogEnable, null, cancellationToken);
        await Cdp.CommandAsync(SiteBrowserTokens.PageEnable, null, cancellationToken);
        await Cdp.CommandAsync(SiteBrowserTokens.NetworkEnable, null, cancellationToken);
        await Cdp.CommandAsync(SiteBrowserTokens.ProfilerEnable, null, cancellationToken);
        await Cdp.CommandAsync(SiteBrowserTokens.StartPreciseCoverage, new Dictionary<string, object?>
        {
            [SiteBrowserTokens.CallCountField] = true,
            [SiteBrowserTokens.DetailedField] = true,
        }, cancellationToken);
    }

    private Task<JsonElement> SendNavigation(string url, CancellationToken cancellationToken) =>
        Cdp.CommandAsync(SiteBrowserTokens.PageNavigate, new Dictionary<string, object?>
        {
            [SiteBrowserTokens.UrlField] = url,
        }, cancellationToken);

    private async Task<JsonElement> StopCoverage(CancellationToken cancellationToken)
        => await Cdp.CommandAsync(SiteBrowserTokens.StopPreciseCoverage, null, cancellationToken);

    private async Task CaptureCoverageAsync(CancellationToken cancellationToken)
    {
        var snapshot = await Cdp.CommandAsync(SiteBrowserTokens.TakePreciseCoverage, null, cancellationToken);
        coverageSnapshots.Add(snapshot.Clone());
    }

    private Task<JsonElement> StopCoverageAsync(CancellationToken cancellationToken) => StopCoverage(cancellationToken);

}

internal sealed record SiteBrowserError(string Event, string Message);

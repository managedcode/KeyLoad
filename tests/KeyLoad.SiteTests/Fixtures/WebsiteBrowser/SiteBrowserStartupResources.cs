namespace KeyLoad.SiteTests.BenchmarkComparisons;

internal sealed class SiteBrowserStartupResources : IAsyncDisposable
{
    private SiteTempDirectory? temporary;
    private SiteStaticFileHost? host;
    private SiteBrowserChrome? chrome;

    private SiteBrowserStartupResources()
    {
    }

    public string Path => RequiredTemporary.Path;
    public string Output => RequiredTemporary.Output;
    public SiteStaticFileHost Host => host ?? throw new InvalidOperationException(SiteBrowserTokens.BrowserStartFailure);

    private SiteTempDirectory RequiredTemporary => temporary ??
                                                   throw new InvalidOperationException(SiteBrowserTokens.BrowserStartFailure);

    public static SiteBrowserStartupResources Create() => new();

    public void CreateTemporary() => temporary = SiteTempDirectory.Create();

    public void StartHost() => host = SiteStaticFileHost.Start(Output);

    public async Task<SiteBrowserChrome> StartChromeAsync(string browserPath, string profilePath,
        CancellationToken cancellationToken)
    {
        chrome = await SiteBrowserChrome.StartAsync(browserPath, profilePath, cancellationToken);
        return chrome;
    }

    public SiteBrowserSession Transfer(string sessionDirectory, SiteBrowserCoverageMetadata metadata)
    {
        var session = new SiteBrowserSession(RequiredTemporary, Host,
            chrome ?? throw new InvalidOperationException(SiteBrowserTokens.BrowserStartFailure),
            sessionDirectory, metadata);
        temporary = null;
        host = null;
        chrome = null;
        return session;
    }

    public async ValueTask DisposeAsync()
    {
        if (temporary is not null)
        {
            await SiteBrowserSession.CleanupStartupFailure(chrome, host, temporary);
        }
    }
}

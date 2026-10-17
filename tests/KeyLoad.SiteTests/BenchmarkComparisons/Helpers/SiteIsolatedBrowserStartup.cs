namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteIsolatedBrowserStartup
{
    public static async Task<SiteBrowserSession> StartAsync(SiteIsolatedFixture fixture, CancellationToken token)
    {
        var browserPath = Environment.GetEnvironmentVariable(SiteBrowserTokens.BrowserEnvironment);
        var coverage = Environment.GetEnvironmentVariable(SiteBrowserTokens.CoverageEnvironment);
        if (string.IsNullOrWhiteSpace(browserPath) || string.IsNullOrWhiteSpace(coverage) ||
            !Path.IsPathFullyQualified(coverage))
        {
            throw new InvalidOperationException(SiteBrowserTokens.BrowserMissing);
        }
        var manifest = await SiteBrowserSession.ReadManifest(fixture.Inputs.Site, coverage, token);
        await using var startup = SiteBrowserStartupResources.Create();
        startup.CreateTemporary();
        var result = await SiteIsolatedBuilderProcess.RunAsync(fixture, startup.Output, token);
        if (result.ExitCode != 0 || result.StandardError.Length != 0)
        {
            throw new InvalidOperationException(SiteBuilderDiagnostics.PreChromeFailure(result));
        }
        startup.StartHost();
        var profile = Path.Combine(startup.Path, SiteBrowserTokens.ChromeProfileDirectory);
        var download = Path.Combine(startup.Path, SiteBrowserTokens.DownloadDirectory);
        Directory.CreateDirectory(profile);
        Directory.CreateDirectory(download);
        var chrome = await startup.StartChromeAsync(browserPath, profile, token);
        await chrome.SetDownloadDirectoryAsync(download, token);
        var session = Path.Combine(coverage, SiteBrowserTokens.BrowserDirectory, SiteBrowserTokens.SessionsDirectory,
            SiteBrowserTokens.BrowserSessionPrefix + Guid.NewGuid().ToString("N"));
        var metadata = SiteBrowserCoverageMetadata.Create(fixture.Inputs.Site.SiteRevision, chrome.Version,
            startup.Host.BaseUrl, manifest);
        await chrome.NavigateAsync(startup.Host.BaseUrl + SiteAssetTokens.IndexHtml + SiteBrowserTokens.PageHideFragment, token);
        return startup.Transfer(session, metadata);
    }
}

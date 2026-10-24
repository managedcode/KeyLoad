using Microsoft.Extensions.Options;

namespace KeyLoad.SiteTests.BenchmarkComparisons;

[KeyLoad.ConfigurationBinding]
internal static class SiteEnvironmentConfiguration
{
    internal static IOptions<SiteEnvironmentOptions> Capture() => Options.Create(new SiteEnvironmentOptions
    {
        Repository = Environment.GetEnvironmentVariable(SiteTokens.RepositoryEnvironment),
        Browser = Environment.GetEnvironmentVariable(SiteBrowserTokens.BrowserEnvironment),
        PipeDirectory = Environment.GetEnvironmentVariable(SiteBrowserTokens.PipeDirectoryEnvironment),
        CoverageMode = Environment.GetEnvironmentVariable(SiteBrowserTokens.CoverageEnvironment),
        BenchmarkMode = Environment.GetEnvironmentVariable(SiteCoverageTokens.BenchmarkModeEnvironment),
        CoverageRoot = Environment.GetEnvironmentVariable(SiteCoverageTokens.CoverageRootEnvironment),
        SourceRevision = Environment.GetEnvironmentVariable(SitePublicationTokens.SourceRevisionEnvironment),
        ControlRevision = Environment.GetEnvironmentVariable(SitePublicationTokens.ControlRevisionEnvironment),
        Aggregate = Environment.GetEnvironmentVariable(SiteIsolatedInputs.AggregateEnvironment)
    });
}

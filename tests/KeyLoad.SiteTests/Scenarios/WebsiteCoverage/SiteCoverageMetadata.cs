namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteCoverageMetadata(string BrowserVersion, IReadOnlySet<string> Origins,
    IReadOnlyList<string> CoverageFiles);

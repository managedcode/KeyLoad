namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteCoverageBrowserSession(string Id, string Version, string MetadataPath, string MetadataSha256,
    IReadOnlyList<string> Origins, IReadOnlyList<string> ReceiptPaths);

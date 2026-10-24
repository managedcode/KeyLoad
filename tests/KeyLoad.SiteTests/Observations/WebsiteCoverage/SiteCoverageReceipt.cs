namespace KeyLoad.SiteTests.BenchmarkComparisons;

internal sealed record SiteCoverageReceipt(string Path, string Sha256, string Runtime, string RuntimeVersion,
    int MappedFunctions);

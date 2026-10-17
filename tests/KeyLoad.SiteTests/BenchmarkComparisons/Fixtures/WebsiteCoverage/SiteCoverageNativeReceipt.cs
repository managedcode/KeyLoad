namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteCoverageNativeReceipt(byte[] Bytes, string Identity, string Sha256,
    string StandardOutput);

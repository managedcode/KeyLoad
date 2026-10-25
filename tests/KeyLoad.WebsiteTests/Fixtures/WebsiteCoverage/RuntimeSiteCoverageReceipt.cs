namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal sealed record RuntimeSiteCoverageReceipt(byte[] Bytes, string Identity, string Sha256,
    string StandardOutput);

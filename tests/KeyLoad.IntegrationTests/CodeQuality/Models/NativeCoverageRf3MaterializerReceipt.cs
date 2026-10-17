namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageRf3MaterializerReceipt(string Directory, string ManifestPath,
    string ManifestHash, int FileCount, long TotalBytes);

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageClusterMaterializerReceipt(string Directory, string ManifestPath,
    string ManifestHash, int FileCount, long TotalBytes);

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageRf3PreparedContext(string InvocationId, NativeCoverageRf3Server Server,
    NativeCoverageRf3Collector Collector, string DockerfileSha256, string SourceTemplateDockerfileSha256,
    string BaseImageReference, string BaseImageReceiptSha256, int FileCount, long TotalBytes, int ManifestLength);

internal sealed record NativeCoverageRf3MaterializerReceipt(string Directory, string ManifestPath,
    string ManifestHash, int FileCount, long TotalBytes);

internal sealed record NativeCoverageRf3BaseImageReceipt(string ImageReference, string SourceSha256);

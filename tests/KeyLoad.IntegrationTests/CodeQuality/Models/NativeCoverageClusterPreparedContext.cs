namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageClusterPreparedContext(string InvocationId, NativeCoverageClusterServer Server,
    NativeCoverageClusterCollector Collector, string DockerfileSha256, string SourceTemplateDockerfileSha256,
    string BaseImageReference, string BaseImageReceiptSha256, int FileCount, long TotalBytes, int ManifestLength);

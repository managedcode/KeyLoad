namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageClusterFixtureArtifacts(string ContextDirectory, string ContextHash,
    string DockerfileHash, string ImageId, NativeCoverageClusterServer Server,
    NativeCoverageClusterCollector Collector, string BaseImageReference, string BaseReceiptHash);

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageClusterFixtureContext(string RunId, string RunManifestPath,
    string SourceManifestPath, string SourceManifestSha256, string SourceRevision, string ImageReference,
    string ImageId, string ContextDirectory, string ContextManifestSha256, string DockerfileSha256,
    string EvidenceRoot, string FixtureId, string FixtureRoot,
    IReadOnlyList<NativeCoverageClusterCaseIdentity> SelectedCases, NativeCoverageClusterServer Server,
    NativeCoverageClusterCollector Collector, NativeCoverageClusterExecutionBounds Bounds,
    string BaseImageReference, string BaseImageReceiptSha256);

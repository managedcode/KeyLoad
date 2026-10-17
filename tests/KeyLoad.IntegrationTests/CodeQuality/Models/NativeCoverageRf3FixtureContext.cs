namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageRf3FixtureContext(string RunId, string RunManifestPath,
    string SourceManifestPath, string SourceManifestSha256, string SourceRevision, string ImageReference,
    string ImageId, string ContextDirectory, string ContextManifestSha256, string DockerfileSha256,
    string EvidenceRoot, string FixtureId, string FixtureRoot,
    IReadOnlyList<NativeCoverageRf3CaseIdentity> SelectedCases, NativeCoverageRf3Server Server,
    NativeCoverageRf3Collector Collector, NativeCoverageRf3ExecutionBounds Bounds,
    string BaseImageReference, string BaseImageReceiptSha256);

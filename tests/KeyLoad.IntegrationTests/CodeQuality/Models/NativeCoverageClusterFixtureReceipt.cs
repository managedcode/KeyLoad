namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageClusterFixtureReceipt(int SchemaVersion, string FixtureId,
    string FunctionalRunId, string Suite, string SourceRevision, string SourceManifestSha256,
    IReadOnlyList<NativeCoverageClusterCaseIdentity> CaseIdentities, NativeCoverageClusterSourceImage SourceImage,
    NativeCoverageClusterCoverageImage CoverageImage, NativeCoverageClusterServerReceipt Server,
    NativeCoverageClusterCollectorReceipt Collector, IReadOnlyList<NativeCoverageClusterNodeReceipt> Nodes);

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageRf3FixtureReceipt(int SchemaVersion, string FixtureId,
    string FunctionalRunId, string Suite, string SourceRevision, string SourceManifestSha256,
    IReadOnlyList<NativeCoverageRf3CaseIdentity> CaseIdentities, NativeCoverageRf3SourceImage SourceImage,
    NativeCoverageRf3CoverageImage CoverageImage, NativeCoverageRf3ServerReceipt Server,
    NativeCoverageRf3CollectorReceipt Collector, IReadOnlyList<NativeCoverageRf3NodeReceipt> Nodes);

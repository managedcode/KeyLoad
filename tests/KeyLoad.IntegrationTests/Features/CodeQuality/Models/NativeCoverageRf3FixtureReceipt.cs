namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageRf3FixtureReceipt(int SchemaVersion, string FixtureId,
    string FunctionalRunId, string Suite, string SourceRevision, string SourceManifestSha256,
    IReadOnlyList<NativeCoverageRf3CaseIdentity> CaseIdentities, NativeCoverageRf3SourceImage SourceImage,
    NativeCoverageRf3CoverageImage CoverageImage, NativeCoverageRf3ServerReceipt Server,
    NativeCoverageRf3CollectorReceipt Collector, IReadOnlyList<NativeCoverageRf3NodeReceipt> Nodes);

internal sealed record NativeCoverageRf3SourceImage(string Reference, string ManifestDigest,
    string SourceReceiptSha256);

internal sealed record NativeCoverageRf3CoverageImage(string Reference, string ImageId,
    string ContextManifestSha256, string DockerfileSha256, NativeCoverageRf3ArtifactReference MaterializerReceipt,
    NativeCoverageRf3ArtifactReference InspectReceipt);

internal sealed record NativeCoverageRf3ServerReceipt(string AssemblyName, string Mvid, string DllSha256,
    string PdbSha256, string SourceReceiptSha256);

internal sealed record NativeCoverageRf3CollectorReceipt(string PackageId, string Version, string ClosureDigest,
    string SettingsSha256, NativeCoverageRf3ExecutionBounds Bounds);

internal sealed record NativeCoverageRf3NodeReceipt(string Node, string ContainerId, string ImageId,
    string Session, long ServerPid, long ServerStartTicks, NativeCoverageRf3ArtifactReference Terminal,
    NativeCoverageRf3ArtifactReference Coverage, NativeCoverageRf3ArtifactReference ContextManifest);

internal sealed record NativeCoverageRf3ArtifactReference(string Path, long Length, string Sha256);

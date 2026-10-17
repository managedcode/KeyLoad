namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageRf3FixtureContext(string RunId, string RunManifestPath,
    string SourceManifestPath, string SourceManifestSha256, string SourceRevision, string ImageReference,
    string ImageId, string ContextDirectory, string ContextManifestSha256, string DockerfileSha256,
    string EvidenceRoot, string FixtureId, string FixtureRoot,
    IReadOnlyList<NativeCoverageRf3CaseIdentity> SelectedCases, NativeCoverageRf3Server Server,
    NativeCoverageRf3Collector Collector, NativeCoverageRf3ExecutionBounds Bounds,
    string BaseImageReference, string BaseImageReceiptSha256);

internal sealed record NativeCoverageRf3CaseIdentity(string ClassName, string MethodName, string InstanceName);

internal sealed record NativeCoverageRf3Server(string Mvid, string DllSha256, string PdbSha256,
    string SourceReceiptSha256);

internal sealed record NativeCoverageRf3Collector(string PackageId, string Version,
    string ClosureDigest, string SettingsSha256);

internal sealed record NativeCoverageRf3ExecutionBounds(int MaximumDescriptorBytes, int MaximumFiles,
    int ReadBufferBytes, long MaximumTotalBytes, long MaximumFileBytes, int MaximumPathCharacters,
    long MaximumManifestBytes, long MaximumReportBytes, string ShutdownTimeout, string SettlementTimeout,
    string ContainerStopTimeout, string ApplicationCleanupTimeout);

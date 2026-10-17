namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageRf3ExecutionBounds(int MaximumDescriptorBytes, int MaximumFiles,
    int ReadBufferBytes, long MaximumTotalBytes, long MaximumFileBytes, int MaximumPathCharacters,
    long MaximumManifestBytes, long MaximumReportBytes, string ShutdownTimeout, string SettlementTimeout,
    string ContainerStopTimeout, string ApplicationCleanupTimeout);

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageClusterExecutionBounds(int MaximumDescriptorBytes, int MaximumFiles,
    int ReadBufferBytes, long MaximumTotalBytes, long MaximumFileBytes, int MaximumPathCharacters,
    long MaximumManifestBytes, long MaximumReportBytes, string ShutdownTimeout, string SettlementTimeout,
    string ContainerStopTimeout, string ApplicationCleanupTimeout);

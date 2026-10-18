namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageClusterCollectorReceipt(string PackageId, string Version, string ClosureDigest,
    string SettingsSha256, NativeCoverageClusterExecutionBounds Bounds);

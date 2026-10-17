namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageRf3CollectorReceipt(string PackageId, string Version, string ClosureDigest,
    string SettingsSha256, NativeCoverageRf3ExecutionBounds Bounds);

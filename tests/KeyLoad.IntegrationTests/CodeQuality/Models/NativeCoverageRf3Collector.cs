namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageRf3Collector(string PackageId, string Version,
    string ClosureDigest, string SettingsSha256);

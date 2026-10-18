namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageClusterCollector(string PackageId, string Version,
    string ClosureDigest, string SettingsSha256);

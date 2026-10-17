namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record NativeClusterStartupFile(string Role, long InspectedBytes,
    bool Truncated, string InspectedSha256);

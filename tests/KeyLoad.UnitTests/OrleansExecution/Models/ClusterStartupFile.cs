namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed record ClusterStartupFile(string Role, long InspectedBytes,
    bool Truncated, string InspectedSha256);

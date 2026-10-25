namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed record ClusterStartupFile(string Role, long InspectedBytes,
    bool Truncated, string InspectedSha256);

namespace KeyLoad.UnitTests.OrleansExecution;

internal sealed record ClusterStartupEvent(string Role, long Offset,
    string Level, string Event, string Category, string? ExceptionType = null,
    string? Reason = null);

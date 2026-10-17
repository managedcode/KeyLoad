namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record NativeClusterStartupEvent(string Role, long Offset,
    string Level, string Event, string Category, string? ExceptionType = null,
    string? Reason = null);

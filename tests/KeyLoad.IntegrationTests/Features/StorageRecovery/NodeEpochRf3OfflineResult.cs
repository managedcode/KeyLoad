namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal sealed record NodeEpochRf3OfflineResult(int ExitCode, string Stdout, string Stderr);

internal static class NodeEpochRf3OfflineProtocol
{
    internal const int MaximumOutputBytes = 65_536;
    internal const int BufferBytes = 4_096;
    internal const string OutputExceeded = "The owned offline helper exceeded its output bound.";
    internal const string FailedStart = "The owned offline helper could not start.";
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan CleanupDeadline = TimeSpan.FromSeconds(10);
}

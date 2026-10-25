namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class CheckpointOriginalCauseProtocol
{
    internal const string Compact = "compact";
    internal const string Install = "install";
    internal const string Identity = "identity.json";
    internal const string Temporary = "identity.json.tmp";
    internal const string Journal = "commands.wal";
    internal const string Interrupted = "Snapshot installation was interrupted. Recover before resuming service.";
    internal const string Poisoned = "The database must recover before accepting another operation.";
    internal const long Position = 1;
    internal const long Applied = 0;
    internal const int Records = 2;
    internal const int ProbeRecords = 3;
    internal const int ColdCycles = 2;
}

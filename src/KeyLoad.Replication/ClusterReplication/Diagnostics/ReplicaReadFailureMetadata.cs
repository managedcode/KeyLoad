namespace KeyLoad.Replication;

internal static class ReplicaReadFailureMetadata
{
    private const string StageKey = "KeyLoad.Replication.ReadBarrier.Stage";
    private const string FlagsKey = "KeyLoad.Replication.ReadBarrier.Flags";
    internal const int Transport = 1;
    internal const int Route = 2;
    internal const int LeaderRound = 3;
    internal const int PeerRound = 4;
    internal const int Apply = 5;
    internal const int LeaderReady = 6;
    internal const int LeaderQuorum = 7;
    internal const int LeaderTerm = 8;
    private const int NoFlags = 0;
    private const int Caller = 1;
    private const int Lifetime = 2;
    private const int Deadline = 4;

    internal static void Mark(Exception error, int stage, bool caller, bool lifetime, bool deadline)
    {
        MarkStage(error, stage);
        if (error.Data[FlagsKey] is int)
        {
            return;
        }
        error.Data[FlagsKey] = (caller ? Caller : NoFlags) | (lifetime ? Lifetime : NoFlags) | (deadline ? Deadline : NoFlags);
    }

    internal static void MarkStage(Exception error, int stage)
    {
        if (error.Data[StageKey] is not int)
        {
            error.Data[StageKey] = stage;
        }
    }

    internal static Exception Preserve(Exception original, Exception replacement)
    {
        if (original.Data[StageKey] is int stage && original.Data[FlagsKey] is int flags)
        {
            replacement.Data[StageKey] = stage;
            replacement.Data[FlagsKey] = flags;
        }
        return replacement;
    }
}

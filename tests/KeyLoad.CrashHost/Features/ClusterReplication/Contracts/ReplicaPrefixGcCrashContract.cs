namespace KeyLoad.CrashHost;

internal static class ReplicaPrefixGcCrashContract
{
    internal const string Mode = "replica-prefix-gc";
    internal const int SnapshotCut = 4;
    internal const int TailCut = 5;
    internal const int InitialTerm = 1;
    internal const int NoMutationIndex = 0;
    internal const int LastPrefixMutationIndex = 3;
}

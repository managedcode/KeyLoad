namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

internal static class AtomicPartitionPlacementProtocol
{
    internal const int CurrentVersion = 1;
    internal const int MaximumExplicitAssignments = 4096;
    internal const int MaximumEncodedBytes = 8192;
    internal const long InitialRowRevision = 1;
    internal const long InitialDirectoryRevision = 1;
}

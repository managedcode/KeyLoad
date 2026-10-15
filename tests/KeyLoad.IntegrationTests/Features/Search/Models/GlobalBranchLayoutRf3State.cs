using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed record GlobalBranchLayoutRf3State(DistributedSearchRf3Seed Seed,
    AtomicPartitionPlacementResolution Original, PartitionMoveRequest Move,
    PhysicalShardRecord SourceOwner, PhysicalShardRecord TargetOwner, PartitionMoveResult? Moved = null)
{
    internal const int Version = 1;
    internal const int Fusion = 60;
    internal const long Revision = 1;
    internal const string Generic = "The database operation could not be completed.";
    internal const string ScopeDenied = "The principal cannot perform this operation in this scope.";
    internal const string MissingDetail = "The database request could not complete. Retry the same command ID for writes.";
}

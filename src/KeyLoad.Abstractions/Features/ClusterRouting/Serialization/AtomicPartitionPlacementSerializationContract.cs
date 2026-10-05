namespace KeyLoad;

internal static class AtomicPartitionPlacementAliases
{
    internal const string Directory = "keyload.contract.atomic-partition-placement-directory.v1";
    internal const string Row = "keyload.contract.atomic-partition-placement.v1";
    internal const string BindRequest = "keyload.contract.atomic-partition-placement-bind-request.v1";
    internal const string ReadRequest = "keyload.contract.atomic-partition-placement-read-request.v1";
    internal const string Resolution = "keyload.contract.atomic-partition-placement-resolution.v1";
}

internal static class AtomicPartitionPlacementDirectoryFieldIds
{
    internal const uint Version = 0;
    internal const uint Revision = 1;
    internal const uint ExplicitAssignmentCount = 2;
}

internal static class AtomicPartitionPlacementRowFieldIds
{
    internal const uint Version = 0;
    internal const uint Partition = 1;
    internal const uint PhysicalShardId = 2;
    internal const uint Revision = 3;
    internal const uint Incarnation = 4;
    internal const uint VoterIds = 5;
    internal const uint PlacementEpoch = 6;
}

internal static class AtomicPartitionPlacementRequestFieldIds
{
    internal const uint Version = 0;
    internal const uint ExpectedRevision = 1;
    internal const uint Partition = 2;
    internal const uint PhysicalShardId = 3;
}

internal static class AtomicPartitionPlacementReadRequestFieldIds
{
    internal const uint Version = 0;
    internal const uint Partition = 1;
}

internal static class AtomicPartitionPlacementResolutionFieldIds
{
    internal const uint Version = 0;
    internal const uint Partition = 1;
    internal const uint PhysicalShardId = 2;
    internal const uint Incarnation = 3;
    internal const uint VoterIds = 4;
    internal const uint PlacementEpoch = 5;
    internal const uint DirectoryRevision = 6;
    internal const uint Revision = 7;
    internal const uint IsFallback = 8;
}

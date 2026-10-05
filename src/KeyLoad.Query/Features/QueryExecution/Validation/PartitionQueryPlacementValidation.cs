namespace KeyLoad.Query.Features.QueryExecution;

internal static class PartitionQueryPlacementValidation
{
    private const string InvalidResolution = "The atomic partition placement resolution is invalid.";
    private const string OwnerLost = "The partition query owner no longer matches this server.";

    internal static void Validate(AtomicPartitionPlacementResolution? resolution,
        PartitionRef partition, PhysicalShardRecord expectedOwner)
    {
        if (resolution is null || resolution.Version != 1 || resolution.Partition != partition
            || resolution.PhysicalShardId == Guid.Empty || resolution.Incarnation == Guid.Empty
            || resolution.VoterIds.IsDefaultOrEmpty || resolution.PlacementEpoch < 1
            || resolution.DirectoryRevision < 0 || resolution.Revision < 0
            || resolution.IsFallback && resolution.Revision != 0)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidResolution);
        }

        if (resolution.PhysicalShardId != expectedOwner.PhysicalShardId
            || resolution.Incarnation != expectedOwner.Incarnation
            || resolution.PlacementEpoch != expectedOwner.PlacementEpoch
            || !resolution.VoterIds.SequenceEqual(expectedOwner.VoterIds, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, OwnerLost);
        }
    }
}

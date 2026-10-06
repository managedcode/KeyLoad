namespace KeyLoad.Query.Features.QueryExecution;

internal static class PartitionQueryPlacementValidation
{
    private const int VersionOne = 1;
    private const int MinimumPositiveCount = 1;
    private const int InitialSequence = 0;

    private const string InvalidResolution = "The atomic partition placement resolution is invalid.";
    private const string OwnerLost = "The partition query owner no longer matches this server.";

    internal static void Validate(AtomicPartitionPlacementResolution? resolution,
        PartitionRef partition, PhysicalShardRecord expectedOwner)
    {
        if (resolution is null || resolution.Version != VersionOne || resolution.Partition != partition
            || resolution.PhysicalShardId == Guid.Empty || resolution.Incarnation == Guid.Empty
            || resolution.VoterIds.IsDefaultOrEmpty || resolution.PlacementEpoch < MinimumPositiveCount
            || resolution.DirectoryRevision < InitialSequence || resolution.Revision < InitialSequence
            || resolution.IsFallback && resolution.Revision != InitialSequence)
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

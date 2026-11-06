namespace KeyLoad.Clustering;

/// <summary>Requires one available database copy to cover every observed partition cut.</summary>
internal static class StorageCopyDatabaseReadSelection
{
    internal static StorageCopyOwner Select(StorageCopyOwner[] copies, SiloAddress? preferred)
    {
        StorageCopyOwner? selected = null;
        foreach (var candidate in copies)
        {
            if (!copies.All(other => Covers(candidate.Cut, other.Cut)))
            {
                continue;
            }
            if (selected is null || candidate.Address.Equals(preferred))
            {
                selected = candidate;
            }
        }
        return selected ?? throw Errors.Fail(ErrorCode.OwnershipLost, StorageCopyContract.RecoveryRequired);
    }

    private static bool Covers(SiloStorageCut candidate, SiloStorageCut other)
        => other.Partitions.All(required => candidate.Partitions.Any(current =>
            current.Partition == required.Partition && current.Position >= required.Position));
}

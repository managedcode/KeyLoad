namespace KeyLoad.Clustering;

/// <summary>Requires one primary to cover both the partition sequence and the selected metadata authority.</summary>
internal static class StorageCopyPrimarySelection
{
    internal static int Require(StorageCopyOwner[] owners, SiloAddress authorized)
    {
        for (var index = 0; index < owners.Length; index++)
        {
            if (owners[index].Address.Equals(authorized) && owners[index].Cut.Position == owners[0].Cut.Position)
            {
                return index;
            }
        }
        throw Errors.Fail(ErrorCode.OwnershipLost, StorageCopyContract.RecoveryRequired);
    }

    internal static StorageCopyOwner[] ForWrite(StorageCopyOwner[] owners, SiloAddress authorized)
    {
        var index = Require(owners, authorized);
        if (index == 0)
        {
            return owners;
        }
        var ordered = owners.ToArray();
        (ordered[0], ordered[index]) = (ordered[index], ordered[0]);
        return ordered;
    }
}

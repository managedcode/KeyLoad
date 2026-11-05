namespace KeyLoad.Clustering;

/// <summary>Prepares on the coordinator's selected local copy only when it has the same canonical cut.</summary>
internal static class StorageCopyPrimarySelection
{
    internal static void PreferLocal(StorageCopyOwner[] owners, SiloAddress local)
    {
        for (var index = 1; index < owners.Length; index++)
        {
            if (owners[index].Address.Equals(local) && owners[index].Cut == owners[0].Cut)
            {
                (owners[0], owners[index]) = (owners[index], owners[0]);
                return;
            }
        }
    }
}

namespace KeyLoad.Server;

internal static class OrleansNodeCatalogAdmission
{
    internal static void Open(Lock lifecycle, ref Task? shutdown,
        ref PhysicalShardCatalogStartup? current, PhysicalShardCatalogStartup catalog,
        string principalId, CancellationToken cancellationToken)
    {
        lock (lifecycle)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (shutdown is not null || !ReferenceEquals(Volatile.Read(ref current), catalog))
            { throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalShardCatalogFence.NotReady); }
            catalog.OpenAdmission(principalId);
        }
    }
}

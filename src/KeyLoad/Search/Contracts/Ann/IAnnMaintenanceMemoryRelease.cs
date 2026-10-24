namespace KeyLoad.Server.Search;

/// <summary>Returns only the exact reciprocal maintenance reservation; the ledger retains shutdown ownership.</summary>
internal interface IAnnMaintenanceMemoryRelease
{
    void Release(AnnMaintenanceMemoryLease lease);
}

namespace KeyLoad.Server.Features.Search;

/// <summary>Returns only the exact reciprocal maintenance reservation; the ledger retains shutdown ownership.</summary>
internal interface INativeAnnMaintenanceMemoryRelease
{
    void Release(NativeAnnMaintenanceMemoryLease lease);
}

namespace KeyLoad.Server.Search;

internal sealed class AnnMaintenanceAdmissionLease(AnnMaintenanceAdmissions admissions) : IDisposable
{
    private AnnMaintenanceAdmissions? owner = admissions;
    public void Dispose() => Interlocked.Exchange(ref owner, null)?.Release();
}

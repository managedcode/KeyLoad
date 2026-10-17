namespace KeyLoad.Server.Features.Search;

internal sealed class NativeAnnMaintenanceAdmissionLease(NativeAnnMaintenanceAdmissions admissions) : IDisposable
{
    private NativeAnnMaintenanceAdmissions? owner = admissions;
    public void Dispose() => Interlocked.Exchange(ref owner, null)?.Release();
}

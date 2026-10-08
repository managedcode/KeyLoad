namespace KeyLoad.Server.Features.Search;

internal sealed class NativeAnnMaintenanceMemoryLease(INativeAnnMaintenanceMemoryRelease owner, long bytes) : IDisposable
{
    private INativeAnnMaintenanceMemoryRelease? current = owner;
    internal long Bytes { get; set; } = bytes;
    public void Dispose() => Interlocked.Exchange(ref current, null)?.Release(this);
}

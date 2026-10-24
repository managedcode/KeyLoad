namespace KeyLoad.Server.Search;

internal sealed class AnnMaintenanceMemoryLease(IAnnMaintenanceMemoryRelease owner, long bytes) : IDisposable
{
    private IAnnMaintenanceMemoryRelease? current = owner;
    internal long Bytes { get; set; } = bytes;
    public void Dispose() => Interlocked.Exchange(ref current, null)?.Release(this);
}

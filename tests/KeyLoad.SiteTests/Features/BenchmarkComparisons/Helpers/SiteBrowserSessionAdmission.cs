namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteBrowserSessionAdmission
{
    private const int ActiveCapacity = SiteTokens.One;
    private const int MaximumQueued = SiteHeavyChildTokens.MaximumQueued;
    private const int AdmissionMinutes = SiteHeavyChildTokens.AdmissionMinutes;
    private readonly SiteHeavyChildAdmission _pool = new(ActiveCapacity, MaximumQueued,
        TimeSpan.FromMinutes(AdmissionMinutes));

    internal static SiteBrowserSessionAdmission Shared { get; } = new();

    private SiteBrowserSessionAdmission() { }

    internal int PendingCount => _pool.PendingCount;

    internal bool HasPendingWaiter(CancellationToken token) => _pool.HasPendingWaiter(token);

    internal Task<SiteHeavyChildLease> AcquireAsync(CancellationToken token) => _pool.AcquireAsync(token);
}

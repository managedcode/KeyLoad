namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal sealed class SiteBrowserSessionAdmission
{
    private readonly SiteHeavyChildAdmission _pool = new(SiteHeavyChildConfiguration.Bind(SiteExecutionConfiguration.Options.Value.BrowserSessionCapacity, SiteExecutionConfiguration.Options.Value.MaximumQueued,
        SiteExecutionConfiguration.Options.Value.AdmissionDeadline));

    internal static SiteBrowserSessionAdmission Shared { get; } = new();

    private SiteBrowserSessionAdmission()
    {
    }

    internal int PendingCount => _pool.PendingCount;

    internal bool HasPendingWaiter(CancellationToken token) => _pool.HasPendingWaiter(token);

    internal Task<SiteHeavyChildLease> AcquireAsync(CancellationToken token) => _pool.AcquireAsync(token);
}

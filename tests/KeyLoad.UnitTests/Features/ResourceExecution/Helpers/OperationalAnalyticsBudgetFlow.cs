using KeyLoad.Server;
using P = KeyLoad.UnitTests.Features.ResourceExecution.OperationalAnalyticsNativeProtocol;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class OperationalAnalyticsBudgetFlow
{
    private const int RefusedReportBytes = 1;
    private const string ExpectedReportRefusal = "The private analytics report exceeds its fixed contract.";

    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        string? directory = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var owner = new OperationalAnalyticsBudgetLifetime();
            directory = owner.Canonical.NodeDirectory;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var from = Hour(TimeProvider.System.GetUtcNow());
                var constrained = owner.Open(new()
                {
                    MaximumReportBytes = RefusedReportBytes
                });
                var cut = await OperationalAnalyticsNativeSeed.RecordAsync(owner.Canonical, constrained, token);
                await constrained.FlushAsync(token);
                var to = Hour(TimeProvider.System.GetUtcNow()).AddHours(P.NextHour);
                var original = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => constrained.ReadAsync(from, to, token));
                ArgumentNullException.ThrowIfNull(original);
                await Assert.That(original.Message).IsEqualTo(ExpectedReportRefusal);
                await LatchedAsync(constrained, cut, from, to, original, token);
                await OperationalAnalyticsNativeAssertions.CanonicalAsync(owner.Canonical, cut);
                await OperationalAnalyticsNativeAssertions.PrivateStorageAsync(owner.Canonical);
                await owner.CloseExpectedFailureAsync(original);
                Assert.ThrowsExactly<ObjectDisposedException>(() => constrained.TryRecord(cut.Snapshot));
                await HealthyRoundAsync(owner, cut, from, to, token);
                for (var cycle = 0; cycle < P.ColdCycles; cycle++)
                {
                    await HealthyRoundAsync(owner, cut, from, to, token);
                }
            }, failures);
            if (failures.Count != 0)
            {
                owner.RetainEvidence();
            }
        }, failures);
        if (failures.Count == 0)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                ArgumentNullException.ThrowIfNull(directory);
                await Assert.That(Directory.Exists(directory)).IsFalse();
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task LatchedAsync(OperationalAnalyticsOwner owner, OperationalAnalyticsNativeCut cut,
        DateTimeOffset from, DateTimeOffset to, InvalidOperationException original, CancellationToken token)
    {
        var record = Assert.ThrowsExactly<InvalidOperationException>(() => owner.TryRecord(cut.Snapshot));
        await Assert.That(ReferenceEquals(record, original)).IsTrue();
        var read = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => owner.ReadAsync(from, to, token));
        await Assert.That(ReferenceEquals(read, original)).IsTrue();
        var flush = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => owner.FlushAsync(token));
        await Assert.That(ReferenceEquals(flush, original)).IsTrue();
    }

    private static async Task HealthyRoundAsync(OperationalAnalyticsBudgetLifetime owner,
        OperationalAnalyticsNativeCut cut, DateTimeOffset from, DateTimeOffset to, CancellationToken token)
    {
        var healthy = owner.Open(new());
        await OperationalAnalyticsNativeAssertions.ReportAsync(healthy, from, to, originalOwner: false, token);
        await OperationalAnalyticsNativeAssertions.CanonicalAsync(owner.Canonical, cut);
        await OperationalAnalyticsNativeAssertions.PrivateStorageAsync(owner.Canonical);
        await owner.CloseHealthyAsync();
        await Assert.That(healthy.HasJoinedEngine).IsTrue();
        Assert.ThrowsExactly<ObjectDisposedException>(() => healthy.TryRecord(cut.Snapshot));
    }

    private static DateTimeOffset Hour(DateTimeOffset value)
        => new(value.UtcTicks - value.UtcTicks % TimeSpan.TicksPerHour, TimeSpan.Zero);
}

using KeyLoad.Server;
using P = KeyLoad.UnitTests.Features.ResourceExecution.OperationalAnalyticsNativeProtocol;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class OperationalAnalyticsHistoryFlow
{
    private const int SingleRow = 1;
    private const long NoSamples = 0;
    private const double FreshSamples = 1;
    private const double FreshPositionSum = 2;
    private const double FreshBacklog = 0;

    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        string? root = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var owner = new OperationalAnalyticsBudgetLifetime();
            root = owner.Canonical.NodeDirectory;
            await ServerFailureObserver.ObserveAsync(() => ExecuteAsync(owner, token), failures);
            if (failures.Count != 0)
            {
                owner.RetainEvidence();
            }
        }, failures);
        if (failures.Count == 0)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                ArgumentNullException.ThrowIfNull(root);
                await Assert.That(Directory.Exists(root)).IsFalse();
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(OperationalAnalyticsBudgetLifetime owner, CancellationToken token)
    {
        var from = Hour(TimeProvider.System.GetUtcNow());
        var original = owner.Open(new());
        var cut = await OperationalAnalyticsNativeSeed.RecordAsync(owner.Canonical, original, token);
        await original.FlushAsync(token);
        var to = Hour(TimeProvider.System.GetUtcNow()).AddHours(P.NextHour);
        await OperationalAnalyticsNativeAssertions.ReportAsync(original, from, to, originalOwner: true, token);
        await owner.CloseHealthyAsync();
        await Assert.That(original.HasJoinedEngine).IsTrue();
        var derived = Path.Combine(owner.Canonical.NodeDirectory, P.DerivedDirectory);
        await Assert.That(Directory.Exists(derived)).IsTrue();
        Directory.Delete(derived, recursive: true);
        await Assert.That(Directory.Exists(derived)).IsFalse();
        await OperationalAnalyticsNativeAssertions.CanonicalAsync(owner.Canonical, cut);
        var fresh = owner.Open(new());
        var absent = await fresh.ReadAsync(from, to, token);
        await Assert.That(absent.Rows.IsEmpty).IsTrue();
        await EnvelopeAsync(absent, from, to);
        await CountersAsync(absent, NoSamples);
        await OperationalAnalyticsNativeAssertions.PrivateStorageAsync(owner.Canonical);
        await OperationalAnalyticsNativeAssertions.CanonicalAsync(owner.Canonical, cut);
        // This is the original real journal observation, not a canonical operation replay or backdated event.
        await Assert.That(fresh.TryRecord(cut.Snapshot)).IsTrue();
        await fresh.FlushAsync(token);
        to = Hour(TimeProvider.System.GetUtcNow()).AddHours(P.NextHour);
        await FreshAsync(fresh, from, to, (long)FreshSamples, token);
        await ConservationAsync(owner, cut);
        for (var cycle = 0; cycle < P.ColdCycles; cycle++)
        {
            var cold = owner.Open(new());
            await FreshAsync(cold, from, to, NoSamples, token);
            await ConservationAsync(owner, cut);
        }
    }

    private static async Task ConservationAsync(OperationalAnalyticsBudgetLifetime owner, OperationalAnalyticsNativeCut cut)
    {
        await OperationalAnalyticsNativeAssertions.CanonicalAsync(owner.Canonical, cut);
        await OperationalAnalyticsNativeAssertions.PrivateStorageAsync(owner.Canonical);
        await owner.CloseHealthyAsync();
    }

    private static async Task FreshAsync(OperationalAnalyticsOwner owner, DateTimeOffset from, DateTimeOffset to,
        long accepted, CancellationToken token)
    {
        var report = await owner.ReadAsync(from, to, token);
        await EnvelopeAsync(report, from, to);
        await Assert.That(report.NativePlanComplete).IsTrue();
        await Assert.That(report.Rows.Length).IsEqualTo(SingleRow);
        var row = report.Rows.Single();
        await Assert.That(row.Hour.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(row.Hour.UtcTicks % TimeSpan.TicksPerHour).IsEqualTo(NoSamples);
        await Assert.That(row.Hour >= from && row.Hour < to).IsTrue();
        await Assert.That(row.Samples).IsEqualTo(FreshSamples);
        await Assert.That(row.CommittedSum).IsEqualTo(FreshPositionSum);
        await Assert.That(row.AppliedSum).IsEqualTo(FreshPositionSum);
        await Assert.That(row.BacklogSum).IsEqualTo(FreshBacklog);
        await CountersAsync(report, accepted);
    }

    private static async Task CountersAsync(OperationalAnalyticsReport report, long accepted)
    {
        await Assert.That(report.Counters.Accepted).IsEqualTo(accepted);
        await Assert.That(report.Counters.Processed).IsEqualTo(accepted);
        await Assert.That(report.Counters.Dropped).IsEqualTo(NoSamples);
        await Assert.That(report.Counters.Rejected).IsEqualTo(NoSamples);
        await Assert.That(report.Counters.Errors).IsEqualTo(NoSamples);
        await Assert.That(report.Counters.QueueLength).IsEqualTo(NoSamples);
    }

    private static async Task EnvelopeAsync(OperationalAnalyticsReport report, DateTimeOffset from, DateTimeOffset to)
    {
        await Assert.That(report.From).IsEqualTo(from);
        await Assert.That(report.To).IsEqualTo(to);
        var bytes = JsonDefaults.Serialize(report);
        await Assert.That(bytes.Length).IsLessThanOrEqualTo(P.MaximumReportBytes);
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        await Assert.That(text.Contains(P.PrivateDocument, StringComparison.Ordinal)
            || text.Contains(P.PrivateVoter, StringComparison.Ordinal) || text.Contains(P.PrivateLeader, StringComparison.Ordinal)).IsFalse();
    }

    private static DateTimeOffset Hour(DateTimeOffset value)
        => new(value.UtcTicks - value.UtcTicks % TimeSpan.TicksPerHour, TimeSpan.Zero);
}

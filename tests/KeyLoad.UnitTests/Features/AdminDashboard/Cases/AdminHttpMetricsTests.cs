using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.AdminDashboard;

internal sealed class AdminHttpMetricsTests
{
    private const int Requests = 128;
    private const int DurationMilliseconds = 5;
    private const int FailureBaseStatus = 400;
    private const int SuccessesPerFailure = 3;
    private const string FailureMethod = "POST";
    private const string FailureRoute = "/v1/documents/get";

    [Test]
    public async Task AcAd003ConcurrentObservationsPreserveCountersAndProcessIdentity()
    {
        var metrics = new AdminHttpMetrics();
        var initial = metrics.Snapshot();
        await Task.WhenAll(Enumerable.Range(0, Requests).Select(index => Task.Run(() =>
            metrics.Record(TimeSpan.FromMilliseconds(DurationMilliseconds), index % 2 == 0 ? Failure(index) : null))));
        var final = metrics.Snapshot();
        await Assert.That(final.CompletedRequests).IsEqualTo(Requests);
        await Assert.That(final.FailedRequests).IsEqualTo(Requests / 2);
        await Assert.That(final.ElapsedMilliseconds).IsEqualTo((double)Requests * DurationMilliseconds);
        await Assert.That(final.ProcessInstance).IsEqualTo(initial.ProcessInstance);
        await Assert.That(final.StartedAt).IsEqualTo(initial.StartedAt);
        await Assert.That(new AdminHttpMetrics().Snapshot().ProcessInstance).IsNotEqualTo(final.ProcessInstance);
    }

    [Test]
    [Arguments(0)]
    [Arguments(3)]
    [Arguments(AdminDashboardProtocol.RecentFailureLimit)]
    [Arguments(AdminDashboardProtocol.RecentFailureLimit + 1)]
    [Arguments(75)]
    [Arguments(AdminDashboardProtocol.RecentFailureLimit * 2)]
    public async Task AcVi004FailureLogIsBoundedNewestFirstWithExactFields(int count)
    {
        var metrics = new AdminHttpMetrics(TimeProvider.System);
        var lower = TimeProvider.System.GetUtcNow();
        for (var index = 0; index < count; index++)
        {
            metrics.Record(TimeSpan.FromMilliseconds(index), Failure(index));
        }

        var upper = TimeProvider.System.GetUtcNow();
        var snapshot = metrics.Snapshot();
        var expected = Math.Min(count, AdminDashboardProtocol.RecentFailureLimit);
        await Assert.That(snapshot.CompletedRequests).IsEqualTo(count);
        await Assert.That(snapshot.FailedRequests).IsEqualTo(count);
        await Assert.That(snapshot.RecentFailures.Length).IsEqualTo(expected);
        for (var position = 0; position < expected; position++)
        {
            var index = count - 1 - position;
            var entry = snapshot.RecentFailures[position];
            await Assert.That(entry.StatusCode).IsEqualTo(FailureBaseStatus + index);
            await Assert.That(entry.Method).IsEqualTo(FailureMethod);
            await Assert.That(entry.Route).IsEqualTo(FailureRoute);
            await Assert.That(entry.Aborted).IsFalse();
            await Assert.That(entry.ElapsedMilliseconds).IsEqualTo((double)index);
            await Assert.That(entry.At).IsGreaterThanOrEqualTo(lower);
            await Assert.That(entry.At).IsLessThanOrEqualTo(upper);
            await Assert.That(position == 0 || entry.At <= snapshot.RecentFailures[position - 1].At).IsTrue();
        }
    }

    [Test]
    public async Task AcVi004SuccessesAreCountedButNeverLogged()
    {
        var metrics = new AdminHttpMetrics();
        const int failures = 4;
        for (var index = 0; index < failures; index++)
        {
            for (var success = 0; success < SuccessesPerFailure; success++)
            {
                metrics.Record(TimeSpan.FromMilliseconds(DurationMilliseconds), null);
            }

            metrics.Record(TimeSpan.FromMilliseconds(DurationMilliseconds), Failure(index));
        }

        var snapshot = metrics.Snapshot();
        await Assert.That(snapshot.CompletedRequests).IsEqualTo(failures * (SuccessesPerFailure + 1));
        await Assert.That(snapshot.FailedRequests).IsEqualTo(failures);
        await Assert.That(snapshot.RecentFailures.Select(entry => entry.StatusCode)
            .SequenceEqual(Enumerable.Range(0, failures).Reverse().Select(index => FailureBaseStatus + index))).IsTrue();
        await Assert.That(new AdminHttpMetrics().Snapshot().RecentFailures.IsDefaultOrEmpty).IsTrue();
    }

    [Test]
    public async Task AcVi004ConcurrentFailuresKeepExactCountersAndBoundedDistinctLog()
    {
        var metrics = new AdminHttpMetrics();
        await Task.WhenAll(Enumerable.Range(0, Requests).Select(index => Task.Run(() =>
            metrics.Record(TimeSpan.FromMilliseconds(DurationMilliseconds), Failure(index)))));
        var snapshot = metrics.Snapshot();
        await Assert.That(snapshot.CompletedRequests).IsEqualTo(Requests);
        await Assert.That(snapshot.FailedRequests).IsEqualTo(Requests);
        await Assert.That(snapshot.RecentFailures.Length).IsEqualTo(AdminDashboardProtocol.RecentFailureLimit);
        await Assert.That(snapshot.RecentFailures.Select(entry => entry.StatusCode).Distinct().Count())
            .IsEqualTo(AdminDashboardProtocol.RecentFailureLimit);
        await Assert.That(snapshot.RecentFailures.All(entry => entry.StatusCode is >= FailureBaseStatus and < FailureBaseStatus + Requests))
            .IsTrue();
    }

    [Test]
    public async Task AcVi004EarlierSnapshotIsNotAliasedByLaterFailures()
    {
        var metrics = new AdminHttpMetrics();
        metrics.Record(TimeSpan.FromMilliseconds(DurationMilliseconds), Failure(0));
        var earlier = metrics.Snapshot();
        metrics.Record(TimeSpan.FromMilliseconds(DurationMilliseconds), Failure(1));
        var later = metrics.Snapshot();
        await Assert.That(earlier.RecentFailures.Length).IsEqualTo(1);
        await Assert.That(earlier.RecentFailures[0].StatusCode).IsEqualTo(FailureBaseStatus);
        await Assert.That(later.RecentFailures.Length).IsEqualTo(2);
        await Assert.That(later.RecentFailures[0].StatusCode).IsEqualTo(FailureBaseStatus + 1);
    }

    private static AdminHttpFailureDetail Failure(int index) =>
        new(FailureMethod, FailureRoute, FailureBaseStatus + index, false);
}

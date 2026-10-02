using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.AdminDashboard;

internal sealed class AdminHttpMetricsTests
{
    private const int Requests = 128;
    private const int DurationMilliseconds = 5;

    [Test]
    public async Task AcAd003ConcurrentObservationsPreserveCountersAndProcessIdentity()
    {
        var metrics = new AdminHttpMetrics();
        var initial = metrics.Snapshot();
        await Task.WhenAll(Enumerable.Range(0, Requests).Select(index => Task.Run(() =>
            metrics.Record(TimeSpan.FromMilliseconds(DurationMilliseconds), index % 2 == 0))));
        var final = metrics.Snapshot();
        await Assert.That(final.CompletedRequests).IsEqualTo(Requests);
        await Assert.That(final.FailedRequests).IsEqualTo(Requests / 2);
        await Assert.That(final.ElapsedMilliseconds).IsEqualTo((double)Requests * DurationMilliseconds);
        await Assert.That(final.ProcessInstance).IsEqualTo(initial.ProcessInstance);
        await Assert.That(final.StartedAt).IsEqualTo(initial.StartedAt);
        await Assert.That(new AdminHttpMetrics().Snapshot().ProcessInstance).IsNotEqualTo(final.ProcessInstance);
    }
}

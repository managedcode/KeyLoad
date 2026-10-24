using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.GraphTraversal;

namespace KeyLoad.UnitTests.TimeSeries;

internal static class TimeSeriesChunkQualificationCurrent
{
    internal static async Task RunAsync(TimeSeriesChunkQualificationFixture fixture, DatabaseEngine owner,
        ZoneTreeStore store, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(TimeSeriesChunkQualificationScopeImage.Capture(store, fixture.Partitions[TimeSeriesChunkQualificationData.SiblingScope])
                .SequenceEqual(fixture.SiblingImage, StringComparer.Ordinal)).IsTrue();
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationReads.RunAsync(fixture, owner, store, false, token), failures);
        foreach (var original in fixture.Originals)
        {
            await ServerFailureObserver.ObserveAsync(() => GraphLabelUseOperations.ReplayAsync(owner, store,
                original.Command, original.Receipt, original.Outcome), failures);
            await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationReads.RunAsync(fixture, owner, store, false, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

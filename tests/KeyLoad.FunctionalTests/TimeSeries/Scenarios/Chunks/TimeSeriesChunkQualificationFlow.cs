using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class TimeSeriesChunkQualificationFlow
{
    private const long FirstFullPurgeCount = 2L;
    private const long CompletePurgeCount = 3L;
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new TimeSeriesChunkQualificationFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                var db = fixture.Harness;
                await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture, db.Database, db.Store, token), failures);
                await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationRefusals.RunAsync(fixture, db.Database, db.Store, false, token), failures);
                await ServerFailureObserver.ObserveAsync(() => fixture.PolicyAsync(TimeSeriesChunkQualificationData.WithdrawnPolicyEpoch, false, false), failures);
                await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationRefusals.RunAsync(fixture, db.Database, db.Store, true, token), failures);
                await ServerFailureObserver.ObserveAsync(() => fixture.PolicyAsync(TimeSeriesChunkQualificationData.RepairedPolicyEpoch, true, true), failures);
                await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationRefusals.TagRepairAsync(fixture, db.Database, db.Store, token), failures);
                await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture, db.Database, db.Store, token), failures);
                await ServerFailureObserver.ObserveAsync(() => RetainAsync(fixture, token), failures);
                await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationCold.RunAsync(fixture, token), failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task RetainAsync(TimeSeriesChunkQualificationFixture fixture, CancellationToken token)
    {
        var partition = fixture.Partitions[TimeSeriesChunkQualificationData.PrimaryScope];
        var failures = new List<Exception>();
        await fixture.CommitAsync(new(Guid.NewGuid(), partition, [new ExpireSamples(TimeSeriesChunkQualificationData.Set,
            TimeSeriesChunkQualificationData.Series, TimeSeriesChunkQualificationData.Base, TimeSeriesChunkQualificationData.OneRetentionDelete)]),
            [new("expireSamples", TimeSeriesChunkQualificationData.Set, TimeSeriesChunkQualificationData.Series, 1L)], TimeSeriesChunkQualificationData.PartialPhase, 1L);
        await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture, fixture.Harness.Database, fixture.Harness.Store, token), failures);
        for (var purged = FirstFullPurgeCount; purged <= CompletePurgeCount; purged++)
        {
            await fixture.CommitAsync(new(Guid.NewGuid(), partition, [new ExpireSamples(TimeSeriesChunkQualificationData.Set,
                TimeSeriesChunkQualificationData.Series, TimeSeriesChunkQualificationData.Until, TimeSeriesChunkQualificationData.OneRetentionDelete)]),
                [new("expireSamples", TimeSeriesChunkQualificationData.Set, TimeSeriesChunkQualificationData.Series, purged)], TimeSeriesChunkQualificationData.ExpiredPhase, purged, purged == FirstFullPurgeCount);
            await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture, fixture.Harness.Database, fixture.Harness.Store, token), failures);
        }
        await fixture.CommitAsync(new(Guid.NewGuid(), partition, [new DropSampleChunkWindow(TimeSeriesChunkQualificationData.Set,
            TimeSeriesChunkQualificationData.Series, fixture.Window, TimeSeriesChunkQualificationData.MergedRevision)]),
            [new("dropSampleChunkWindow", TimeSeriesChunkQualificationData.Set, TimeSeriesChunkQualificationData.Series,
                TimeSeriesChunkQualificationData.DroppedRevision)], TimeSeriesChunkQualificationData.DroppedPhase);
        await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture, fixture.Harness.Database, fixture.Harness.Store, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task WaveAsync(TimeSeriesChunkQualificationFixture fixture, DatabaseEngine owner, ZoneTreeStore store, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationRefusals.CancellationAsync(fixture, owner, store), failures);
        await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationRefusals.BoundAsync(fixture, owner, store, token), failures);
        await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationReads.RunAsync(fixture, owner, store, true, token), failures);
        await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationCurrent.RunAsync(fixture, owner, store, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

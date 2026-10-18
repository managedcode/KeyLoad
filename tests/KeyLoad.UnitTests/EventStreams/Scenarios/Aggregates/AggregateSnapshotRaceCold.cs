using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class AggregateSnapshotRaceCold
{
    internal static async Task RunAsync(AggregateSnapshotRaceFixture fixture, CancellationToken token)
    {
        var image = fixture.ColdImage;
        var identity = fixture.ColdIdentity;
        var position = fixture.ColdPosition;
        ZoneTreeStore? store = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            store = new(new(fixture.Harness.Directory), fixture.Harness.StorageExecution, UnitExecutionOptions.PointCacheExecution());
            fixture.Store = store;
            fixture.Database = fixture.Harness.CreateConfiguredDatabase(store);
            await ServerFailureObserver.ObserveAsync(() => AggregateSnapshotRaceValues.ConservedAsync(fixture, image, identity, position), failures);
            await ServerFailureObserver.ObserveAsync(() => AggregateSnapshotRaceFlow.ObserveAsync(fixture,
                () => AggregateSnapshotRaceModel.RequireAsync(fixture, true)), failures);
            await ServerFailureObserver.ObserveAsync(() => AggregateSnapshotRaceFlow.ObserveAsync(fixture,
                () => AggregateSnapshotRaceValues.ReplayAsync(fixture, true)), failures);
            await ServerFailureObserver.ObserveAsync(() => HealthyReplayAsync(fixture), failures);
        }, failures);
        if (store is not null)
        {
            ServerFailureObserver.Observe(store.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static Task HealthyReplayAsync(AggregateSnapshotRaceFixture fixture)
        => AggregateSnapshotRaceFlow.ObserveAsync(fixture, async () =>
        {
            var command = fixture.Healthy ?? throw new InvalidOperationException("The healthy snapshot command is absent.");
            var expected = fixture.HealthyResult ?? throw new InvalidOperationException("The healthy snapshot receipt is absent.");
            await NativeReplayResultAssertions.Same<CommitReceipt>(fixture.Database.Apply(fixture.Operation(command)), expected);
            var raw = fixture.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(fixture.Harness.Partition,
                AggregateSnapshotRaceFixture.Worker, command.CommandId)));
            await Assert.That(raw is not null && raw.AsSpan().SequenceEqual(fixture.HealthyRaw)).IsTrue();
            await AggregateSnapshotRaceValues.OriginalsAsync(fixture);
        });
}

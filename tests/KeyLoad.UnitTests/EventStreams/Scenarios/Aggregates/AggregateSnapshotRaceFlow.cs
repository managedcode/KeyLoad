using KeyLoad.Server;
using KeyLoad.UnitTests.Messaging;
using KeyLoad.UnitTests.StorageRecovery.Assertions;

namespace KeyLoad.UnitTests.EventStreams;

internal static class AggregateSnapshotRaceFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        AggregateSnapshotRaceFixture? fixture = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fixture = new();
            fixture.Initialize();
            var position = fixture.Store.Position;
            var canonicalPosition = fixture.Database.LastApplied;
            await AggregateSnapshotRaceCompetitors.RunAsync(fixture, token);
            await Assert.That(fixture.Store.Position).IsEqualTo(position + 2);
            var winner = fixture.Outcomes.Single(outcome => Errors.Code(outcome.Result.Problem).Value is null);
            await AggregateSnapshotRaceValues.ReceiptAsync(fixture, fixture.Winner!, winner.Result, canonicalPosition + 1, 1);
            await ServerFailureObserver.ObserveAsync(() => ObserveAsync(fixture,
                () => AggregateSnapshotRaceModel.RequireAsync(fixture, false)), failures);
            await ServerFailureObserver.ObserveAsync(() => ObserveAsync(fixture,
                () => AggregateSnapshotRaceValues.ReplayAsync(fixture, false)), failures);
            await ServerFailureObserver.ObserveAsync(() => RepairAsync(fixture), failures);
        }, failures);
        if (fixture is not null)
        {
            ServerFailureObserver.Observe(() =>
            {
                fixture.ColdImage = QueueWholeFlowStorage.Bytes(fixture.Store);
                fixture.ColdPosition = fixture.Store.Position;
                fixture.ColdIdentity = ReadCutStoreStateAssertions.SnapshotIdentity(fixture.Store);
            }, failures);
            ServerFailureObserver.Observe(fixture.Store.Dispose, failures);
            await ServerFailureObserver.ObserveAsync(() => AggregateSnapshotRaceCold.RunAsync(fixture, token), failures);
            await ServerFailureObserver.ObserveAsync(() => AggregateSnapshotRaceCold.RunAsync(fixture, token), failures);
            ServerFailureObserver.Observe(fixture.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task RepairAsync(AggregateSnapshotRaceFixture fixture)
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => fixture.Policy(2, false), failures);
        await ServerFailureObserver.ObserveAsync(() => ObserveAsync(fixture, async () =>
        {
            var error = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Read());
            await Assert.That(error.Code).IsEqualTo(ErrorCode.PermissionDenied);
        }), failures);
        await ServerFailureObserver.ObserveAsync(() => ObserveAsync(fixture,
            () => AggregateSnapshotRaceValues.ReplayAsync(fixture, true, rawInputDenied: true)), failures);
        ServerFailureObserver.Observe(() => fixture.Policy(3, true), failures);
        await ServerFailureObserver.ObserveAsync(() => ObserveAsync(fixture,
            () => AggregateSnapshotRaceValues.ReplayAsync(fixture, true)), failures);
        await ServerFailureObserver.ObserveAsync(() => ObserveAsync(fixture,
            () => AggregateSnapshotRaceModel.RequireAsync(fixture, false)), failures);
        await ServerFailureObserver.ObserveAsync(() => CreateHealthyAsync(fixture), failures);
        await ServerFailureObserver.ObserveAsync(() => ObserveAsync(fixture,
            () => AggregateSnapshotRaceModel.RequireAsync(fixture, true)), failures);
        await ServerFailureObserver.ObserveAsync(() => AggregateSnapshotRaceCold.HealthyReplayAsync(fixture), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task CreateHealthyAsync(AggregateSnapshotRaceFixture fixture)
    {
        fixture.Healthy = fixture.Snapshot(3, "{\"total\":6}", 1);
        var position = checked(fixture.Database.LastApplied + 1);
        var result = fixture.Database.Apply(fixture.Operation(fixture.Healthy));
        fixture.HealthyResult = result;
        fixture.HealthyRaw = fixture.Store.Read(view => view.ReadOwnedValue(KeyLoad.Core.KeySpace.PartitionOutcome(
            fixture.Harness.Partition, AggregateSnapshotRaceFixture.Worker, fixture.Healthy.CommandId)))!;
        await AggregateSnapshotRaceValues.ReceiptAsync(fixture, fixture.Healthy, result, position, 2);
    }
    internal static async Task ObserveAsync(AggregateSnapshotRaceFixture fixture, Func<Task> action)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = ReadCutStoreStateAssertions.SnapshotIdentity(fixture.Store);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(action, failures);
        await ServerFailureObserver.ObserveAsync(() => AggregateSnapshotRaceValues.ConservedAsync(fixture, image, identity, position), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class AggregateSnapshotRaceValues
{
    private const string CompleteReplayInputDenied = "Complete replay input requires every raw-read and raw-use grant.";
    private const string ChangedPrincipalPolicyDenied = "The principal policy changed since this command was evaluated.";
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(System.Text.Json.JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(System.Text.Json.JsonSerializer.Serialize(expected, JsonDefaults.Options));
    internal static async Task ReceiptAsync(AggregateSnapshotRaceFixture fixture, CommandRequest command,
        OperationResult result, long position, long version)
    {
        var actual = result.Get<CommitReceipt>();
        await EqualAsync(actual, new CommitReceipt(command.CommandId,
            new(fixture.Store.Identity.Incarnation, fixture.Harness.Partition.AtomicPartitionId, position, 1),
            [new("storeAggregateSnapshot", AggregateSnapshotRaceFixture.StreamSet, AggregateSnapshotRaceFixture.StreamId, version)],
            DurabilityProfile.ProcessDurable));
        await Assert.That(actual.Mutations.Single().CompositionReferences).IsEmpty();
    }
    internal static async Task ConservedAsync(AggregateSnapshotRaceFixture fixture, string[] image,
        StoreIdentity identity, long position)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Bytes(fixture.Store))
                .IsEquivalentTo(image, CollectionOrdering.Matching), failures);
        await ServerFailureObserver.ObserveAsync(() => NativeReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(
            fixture.Store, identity, position), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task OriginalsAsync(AggregateSnapshotRaceFixture fixture)
    {
        await SeedAsync(fixture);
        await Assert.That(fixture.EventImage()).IsEquivalentTo(fixture.OriginalEvents, CollectionOrdering.Matching);
        foreach (var original in fixture.Outcomes)
        {
            var raw = fixture.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(
                fixture.Harness.Partition, AggregateSnapshotRaceFixture.Worker, original.Operation.Id)));
            await Assert.That(raw is not null && raw.AsSpan().SequenceEqual(original.Raw)).IsTrue();
        }
    }
    private static async Task SeedAsync(AggregateSnapshotRaceFixture fixture)
    {
        var expected = new CommitReceipt(fixture.SeedOperation.Id,
            new(fixture.Store.Identity.Incarnation, fixture.Harness.Partition.AtomicPartitionId, fixture.SeedPosition, 1),
            [new("appendEvents", AggregateSnapshotRaceFixture.StreamSet, AggregateSnapshotRaceFixture.StreamId, 3)], DurabilityProfile.ProcessDurable);
        await EqualAsync(fixture.SeedResult.Get<CommitReceipt>(), expected);
        await Assert.That(fixture.SeedResult.Get<CommitReceipt>().Mutations.Single().CompositionReferences).IsEmpty();
        await NativeReplayResultAssertions.Same<CommitReceipt>(fixture.Database.Apply(fixture.SeedOperation), fixture.SeedResult);
        var raw = fixture.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(
            fixture.Harness.Partition, "root", fixture.SeedOperation.Id)));
        await Assert.That(raw is not null && raw.AsSpan().SequenceEqual(fixture.SeedRaw)).IsTrue();
    }
    internal static async Task ReplayAsync(AggregateSnapshotRaceFixture fixture, bool fenced, bool rawInputDenied = false)
    {
        var failures = new List<Exception>();
        foreach (var original in fixture.Outcomes)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var actual = fixture.Database.Apply(original.Operation);
                if (fenced)
                {
                    await EqualAsync(actual.Error, ErrorCode.PermissionDenied);
                    await Assert.That(actual.SafeDetail).IsEqualTo(rawInputDenied ? CompleteReplayInputDenied : ChangedPrincipalPolicyDenied);
                    await Assert.That(actual.Json).IsNull();
                    await Assert.That(actual.NativeValue).IsNull();
                }
                else if (original.Result.Error is null)
                {
                    await NativeReplayResultAssertions.Same<CommitReceipt>(actual, original.Result);
                }
                else
                {
                    await EqualAsync(actual, original.Result);
                    await Assert.That(actual.NativeValue).IsNull();
                }
            }, failures);
        }
        await ServerFailureObserver.ObserveAsync(() => OriginalsAsync(fixture), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class RevokedOutcomeAdmissionValues
{
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(System.Text.Json.JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(System.Text.Json.JsonSerializer.Serialize(expected, JsonDefaults.Options));
    internal static async Task ReceiptAsync(RevokedOutcomeAdmissionFixture fixture, CommandRequest command,
        Result<object> result, long position, bool fresh)
    {
        var effects = Effects(fresh);
        await EqualAsync(result.Get<CommitReceipt>(), new CommitReceipt(command.CommandId, new(fixture.Store.Identity.Incarnation, fixture.Harness.Partition.AtomicPartitionId, position), [.. effects], DurabilityProfile.ProcessDurable, 1));
        foreach (var effect in result.Get<CommitReceipt>().Mutations)
        {
            await Assert.That(effect.CompositionReferences).IsEmpty();
        }
    }
    internal static MutationReceipt[] Effects(bool fresh) => fresh ?
            [new("putDocument", RevokedOutcomeAdmissionFixture.Documents, "c", 1), new("appendEvents", RevokedOutcomeAdmissionFixture.Events, "stream", 2),
             new("enqueue", RevokedOutcomeAdmissionFixture.Queue, "healthy", 1)] :
            [new("putDocument", RevokedOutcomeAdmissionFixture.Documents, "a", 1), new("putDocument", RevokedOutcomeAdmissionFixture.Documents, "b", 1),
             new("appendEvents", RevokedOutcomeAdmissionFixture.Events, "stream", 1), new("enqueue", RevokedOutcomeAdmissionFixture.Queue, "original", 1)];
    internal static async Task OriginalAsync(RevokedOutcomeAdmissionFixture fixture)
    {
        await Assert.That(fixture.ReadOutcome(fixture.Original.CommandId).AsSpan().SequenceEqual(fixture.OriginalOutcome)).IsTrue();
        var locator = fixture.Store.Read(view => view.ReadOwnedValue(KeySpace.OutcomeLocatorV2(
            fixture.Harness.Partition, RevokedOutcomeAdmissionFixture.Worker, fixture.Original.CommandId)));
        await Assert.That(locator is not null && locator.AsSpan().SequenceEqual(fixture.OriginalLocator)).IsTrue();
        await Assert.That(fixture.OriginalLocator.AsSpan().SequenceEqual(KeySpace.PartitionOutcome(
            fixture.Harness.Partition, RevokedOutcomeAdmissionFixture.Worker, fixture.Original.CommandId))).IsTrue();
        var stored = NativeSerialization.Deserialize<StoredOutcome>(fixture.OriginalOutcome);
        await NativeReplayResultAssertions.Same<CommitReceipt>(stored.Result, fixture.OriginalResult);
        await Assert.That(stored.PolicyEpoch).IsEqualTo(1L);
        await Assert.That(stored.Incarnation).IsEqualTo(fixture.Store.Identity.Incarnation);
    }
    internal static async Task ConservedAsync(RevokedOutcomeAdmissionFixture fixture, string[] image,
        StoreIdentity identity, long position)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Bytes(fixture.Store))
                .IsEquivalentTo(image, CollectionOrdering.Matching), failures);
        await ServerFailureObserver.ObserveAsync(() => NativeReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(
            fixture.Store, identity, position), failures);
        await ServerFailureObserver.ObserveAsync(() => OriginalAsync(fixture), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task RefusedAsync(RevokedOutcomeAdmissionFixture fixture, ErrorCode code, string detail)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var actual = fixture.Database.Apply(RevokedOutcomeAdmissionFixture.Operation(fixture.Original));
            await EqualAsync(actual, Result<object>.Fail(Errors.Problem(code, detail)));
            await Assert.That(actual.Value).IsNull();
            await OriginalAsync(fixture);
        }
    }
    internal static async Task FreshReplayAsync(RevokedOutcomeAdmissionFixture fixture)
    {
        var command = fixture.Fresh ?? throw new InvalidOperationException("The fresh producer is missing.");
        await NativeReplayResultAssertions.Same<CommitReceipt>(fixture.Database.Apply(RevokedOutcomeAdmissionFixture.Operation(command)), fixture.FreshResult!);
        await Assert.That(fixture.ReadOutcome(command.CommandId).AsSpan().SequenceEqual(fixture.FreshOutcome)).IsTrue();
        await OriginalAsync(fixture);
    }
}

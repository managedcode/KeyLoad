using KeyLoad.Core.CommandOutcomes.Contracts;
using KeyLoad.FunctionalTests.StorageRecovery.Assertions;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class SubscriptionOldCutSavedFailure
{
    internal static async Task RunAsync(DatabaseEngine database, ZoneTreeStore store, SubscriptionOldCutState state,
        Command<DatabaseCommand> operation, ErrorCode code, string detail)
    {
        var partition = state.Group.Source.Partition;
        var expected = QueueWholeFlowStorage.Bytes(store).ToDictionary(row => row[..row.IndexOf(':', StringComparison.Ordinal)],
            row => row[(row.IndexOf(':', StringComparison.Ordinal) + 1)..], StringComparer.Ordinal);
        var position = store.Position;
        var identity = ReadCutStoreStateAssertions.SnapshotIdentity(store);
        var outcomeKey = KeySpace.PartitionOutcome(partition, operation.UserId!, operation.CommandId);
        var fingerprint = operation.Value!.Fingerprint;
        var next = checked(database.LastApplied + 1);
        var outcome = new StoredOutcome(fingerprint, store.Identity.Incarnation, 2, Result<object>.Fail(Errors.Problem(code, detail)), operation)
        {
            ScopeKind = CommandOutcomeScopeKind.Partition,
            Partition = partition,
            CanonicalSequence = next
        };

        expected.Add(Convert.ToHexString(KeySpace.OutcomeLocator(partition, operation.UserId!, operation.CommandId)),
            Convert.ToHexString(outcomeKey));
        expected[Convert.ToHexString(KeySpace.AppliedBytes)] = Convert.ToHexString(OrleansSerialization.Serialize(next));
        expected[Convert.ToHexString(KeySpace.LastCommandBytes)] = Convert.ToHexString(OrleansSerialization.Serialize(outcomeKey));
        expected[Convert.ToHexString(KeySpace.ClockBytes)] = Convert.ToHexString(OrleansSerialization.Serialize(new DateTimeOffset(operation.Timestamp)));
        var failures = new List<Exception>();
        byte[]? raw = null;
        for (var repeat = 0; repeat < 2; repeat++)
        {
            await ServerFailureObserver.ObserveAsync(() => ResultAsync(database.Apply(operation), code, detail), failures);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var current = store.Read(view => view.ReadOwnedValue(outcomeKey))
                    ?? throw new InvalidOperationException("The original failure outcome is missing.");
                Retain(store, state, operation, current);
                raw ??= current;
                await OutcomeAsync(OrleansSerialization.Deserialize<StoredOutcome>(current), outcome);
                await Assert.That(current.AsSpan().SequenceEqual(raw)).IsTrue();
            }, failures);
            if (raw is not null)
            {
                expected[Convert.ToHexString(outcomeKey)] = Convert.ToHexString(raw);
            }
            var expectedImage = expected.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + ":" + pair.Value).ToArray();
            await ServerFailureObserver.ObserveAsync(() => SubscriptionOldCutAssertions.ConservedAsync(store,
                expectedImage, position + 1, identity), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task ResultAsync(Result<object> result, ErrorCode code, string detail)
    {
        await Assert.That(Errors.Code(result.Problem).Value).IsEqualTo(code);
        await Assert.That(result.Problem?.Detail).IsEqualTo(detail);

        await Assert.That(result.Value).IsNull();
    }
    private static async Task OutcomeAsync(StoredOutcome actual, StoredOutcome expected)
    {
        await Assert.That(actual.Fingerprint).IsEqualTo(expected.Fingerprint);
        await Assert.That(actual.Incarnation).IsEqualTo(expected.Incarnation);
        await Assert.That(actual.PolicyEpoch).IsEqualTo(expected.PolicyEpoch);
        await ResultAsync(actual.Result, Errors.Code(expected.Result.Problem).Value!.Value, expected.Result.Problem?.Detail!);
        await Assert.That(actual.ScopeKind).IsEqualTo(expected.ScopeKind);
        await Assert.That(actual.Partition).IsEqualTo(expected.Partition);
        await Assert.That(actual.CompositionAuthority).IsNull();
        await Assert.That(actual.OnlineTextAuthority).IsNull();
    }

    private static void Retain(ZoneTreeStore store, SubscriptionOldCutState state, Command<DatabaseCommand> operation, byte[] bytes)
    {
        if (state.DeniedOperation is null)
        {
            state.DeniedOperation = operation;
            state.DeniedOutcome = bytes.ToArray();
            state.DeniedLocator = store.Read(view => view.ReadOwnedValue(KeySpace.OutcomeLocator(
                state.Group.Source.Partition, operation.UserId!, operation.CommandId)))
                ?? throw new InvalidOperationException("The original failure locator is missing.");
        }
    }
    internal static async Task RetainedAsync(DatabaseEngine database, ZoneTreeStore store, SubscriptionOldCutState state)
    {
        var operation = state.DeniedOperation ?? throw new InvalidOperationException("The original denial was not retained.");
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = ReadCutStoreStateAssertions.SnapshotIdentity(store);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => OriginalBytesAsync(store, state, operation), failures);
        await ServerFailureObserver.ObserveAsync(() => ResultAsync(database.Apply(operation), ErrorCode.PermissionDenied,
            "The principal policy changed since this command was evaluated."), failures);
        await ServerFailureObserver.ObserveAsync(() => OriginalBytesAsync(store, state, operation), failures);
        await ServerFailureObserver.ObserveAsync(() => SubscriptionOldCutAssertions.ConservedAsync(store,
            image, position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task OriginalBytesAsync(ZoneTreeStore store, SubscriptionOldCutState state, Command<DatabaseCommand> operation)
    {
        await Assert.That(state.DeniedOutcome.Length > 0 && state.DeniedLocator.Length > 0).IsTrue();
        var key = KeySpace.PartitionOutcome(state.Group.Source.Partition, operation.UserId!, operation.CommandId);
        var raw = store.Read(view => view.ReadOwnedValue(key));
        var locator = store.Read(view => view.ReadOwnedValue(KeySpace.OutcomeLocator(
            state.Group.Source.Partition, operation.UserId!, operation.CommandId)));
        await Assert.That(raw).IsNotNull();
        await Assert.That(locator).IsNotNull();
        await Assert.That(raw!.AsSpan().SequenceEqual(state.DeniedOutcome)).IsTrue();
        await Assert.That(locator!.AsSpan().SequenceEqual(state.DeniedLocator)).IsTrue();
        await Assert.That(locator.AsSpan().SequenceEqual(key)).IsTrue();
    }

}

using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionOldCutSavedFailure
{
    internal static async Task RunAsync(DatabaseEngine database, ZoneTreeStore store, SubscriptionOldCutState state,
        ReplicatedOperation operation, ErrorCode code, string detail)
    {
        var partition = state.Group.Source.Partition;
        var expected = QueueWholeFlowStorage.Bytes(store).ToDictionary(row => row[..row.IndexOf(':', StringComparison.Ordinal)],
            row => row[(row.IndexOf(':', StringComparison.Ordinal) + 1)..], StringComparer.Ordinal);
        var position = store.Position;
        var identity = NativeReadCutStoreStateAssertions.SnapshotIdentity(store);
        var outcomeKey = KeySpace.PartitionOutcome(partition, operation.PrincipalId, operation.Id);
        var fingerprint = JsonData.Fingerprint(new
        {
            operation.Id,
            operation.Kind,
            operation.PrincipalId,
            operation.PayloadJson
        });
        var outcome = new StoredOutcome(fingerprint, store.Identity.Incarnation, 2, new(null, code, detail))
        {
            ScopeKind = CommandOutcomeScopeKind.Partition,
            Partition = partition
        };

        expected.Add(Convert.ToHexString(KeySpace.OutcomeLocatorV2(partition, operation.PrincipalId, operation.Id)),
            Convert.ToHexString(outcomeKey));
        expected[Convert.ToHexString(KeySpace.ClockBytes)] = Convert.ToHexString(NativeSerialization.Serialize(operation.EvaluatedAt));
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
                await OutcomeAsync(NativeSerialization.Deserialize<StoredOutcome>(current), outcome);
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
    private static async Task ResultAsync(OperationResult result, ErrorCode code, string detail)
    {
        await Assert.That(result.Error).IsEqualTo(code);
        await Assert.That(result.SafeDetail).IsEqualTo(detail);
        await Assert.That(result.Json).IsNull();
        await Assert.That(result.NativeValue).IsNull();
    }
    private static async Task OutcomeAsync(StoredOutcome actual, StoredOutcome expected)
    {
        await Assert.That(actual.Fingerprint).IsEqualTo(expected.Fingerprint);
        await Assert.That(actual.Incarnation).IsEqualTo(expected.Incarnation);
        await Assert.That(actual.PolicyEpoch).IsEqualTo(expected.PolicyEpoch);
        await ResultAsync(actual.Result, expected.Result.Error!.Value, expected.Result.SafeDetail!);
        await Assert.That(actual.ScopeKind).IsEqualTo(expected.ScopeKind);
        await Assert.That(actual.Partition).IsEqualTo(expected.Partition);
        await Assert.That(actual.BlobAuthority).IsNull();
        await Assert.That(actual.CompositionAuthority).IsNull();
        await Assert.That(actual.OnlineTextAuthority).IsNull();
        await Assert.That(actual.RemoteTransferFailureAuthority).IsNull();
        await Assert.That(actual.RemoteTransferAuthority).IsNull();
    }

    private static void Retain(ZoneTreeStore store, SubscriptionOldCutState state, ReplicatedOperation operation, byte[] bytes)
    {
        if (state.DeniedOperation is null)
        {
            state.DeniedOperation = operation;
            state.DeniedOutcome = bytes.ToArray();
            state.DeniedLocator = store.Read(view => view.ReadOwnedValue(KeySpace.OutcomeLocatorV2(
                state.Group.Source.Partition, operation.PrincipalId, operation.Id)))
                ?? throw new InvalidOperationException("The original failure locator is missing.");
        }
    }
    internal static async Task RetainedAsync(DatabaseEngine database, ZoneTreeStore store, SubscriptionOldCutState state)
    {
        var operation = state.DeniedOperation ?? throw new InvalidOperationException("The original denial was not retained.");
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = NativeReadCutStoreStateAssertions.SnapshotIdentity(store);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => OriginalBytesAsync(store, state, operation), failures);
        await ServerFailureObserver.ObserveAsync(() => ResultAsync(database.Apply(operation), ErrorCode.PermissionDenied,
            "The principal policy changed since this command was evaluated."), failures);
        await ServerFailureObserver.ObserveAsync(() => OriginalBytesAsync(store, state, operation), failures);
        await ServerFailureObserver.ObserveAsync(() => SubscriptionOldCutAssertions.ConservedAsync(store,
            image, position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task OriginalBytesAsync(ZoneTreeStore store, SubscriptionOldCutState state, ReplicatedOperation operation)
    {
        await Assert.That(state.DeniedOutcome.Length > 0 && state.DeniedLocator.Length > 0).IsTrue();
        var key = KeySpace.PartitionOutcome(state.Group.Source.Partition, operation.PrincipalId, operation.Id);
        var raw = store.Read(view => view.ReadOwnedValue(key));
        var locator = store.Read(view => view.ReadOwnedValue(KeySpace.OutcomeLocatorV2(
            state.Group.Source.Partition, operation.PrincipalId, operation.Id)));
        await Assert.That(raw).IsNotNull();
        await Assert.That(locator).IsNotNull();
        await Assert.That(raw!.AsSpan().SequenceEqual(state.DeniedOutcome)).IsTrue();
        await Assert.That(locator!.AsSpan().SequenceEqual(state.DeniedLocator)).IsTrue();
        await Assert.That(locator.AsSpan().SequenceEqual(key)).IsTrue();
    }

}

using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueueViewLeaseIsolationOperations
{
    internal static async Task<QueueViewLeaseIsolationHistory> SendAsync<T>(QueueViewLeaseIsolationState state,
        OperationKind kind, T request, Guid id, object expected)
        => await SendAsync(state, kind, request, id, _ => expected);

    internal static async Task<QueueViewLeaseIsolationHistory> SendAsync<T>(QueueViewLeaseIsolationState state,
        OperationKind kind, T request, Guid id, Func<ReplicatedOperation, object> expected)
    {
        state.Token.ThrowIfCancellationRequested();
        var owner = state.Owner;
        var index = checked(owner.JournalCut().LastIndex + QueueViewLeaseIsolationProtocol.One);
        var operation = owner.Database.CreateNativeOperation(kind, id, QueueViewLeaseIsolationProtocol.Root,
            owner.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        operation = operation with
        {
            NativePayload = operation.NativePayload.ToArray()
        };
        var predicted = expected(operation);
        var result = owner.Submit(operation);
        var entry = owner.Entry(operation);
        await Assert.That(entry.Index).IsEqualTo(index);
        await QueueViewLeaseIsolationResults.SameAsync(result, new(null)
        {
            NativeValue = predicted
        });
        var global = kind is OperationKind.ConfigureResource or OperationKind.ConfigurePrincipal;
        var key = global ? KeySpace.GlobalOutcome(QueueViewLeaseIsolationProtocol.Root, id)
            : KeySpace.PartitionOutcome(state.Partition, QueueViewLeaseIsolationProtocol.Root, id);
        var locatorKey = KeySpace.OutcomeLocatorV2(state.Partition, QueueViewLeaseIsolationProtocol.Root, id);
        var outcome = owner.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException(QueueViewLeaseIsolationProtocol.Absent);
        var locator = owner.Store.Read(view => view.ReadOwnedValue(locatorKey));
        var stored = NativeSerialization.Deserialize<StoredOutcome>(outcome);
        await Assert.That(stored.Incarnation).IsEqualTo(owner.Store.Identity.Incarnation);
        await Assert.That(stored.PolicyEpoch).IsEqualTo(QueueViewLeaseIsolationProtocol.Initial);
        await Assert.That(stored.ScopeKind).IsEqualTo(global ? CommandOutcomeScopeKind.Global : CommandOutcomeScopeKind.Partition);
        await Assert.That(stored.Partition == (global ? null : state.Partition)).IsTrue();
        await Assert.That(global ? locator is null : locator is not null && locator.AsSpan().SequenceEqual(key)).IsTrue();
        await QueueViewLeaseIsolationResults.SameAsync(stored.Result, result);
        var retained = new QueueViewLeaseIsolationHistory(operation, result, entry, key, outcome, locatorKey, locator);
        state.History.Add(retained);
        return retained;
    }

    internal static CommitReceipt Receipt(QueueViewLeaseIsolationState state, Guid id,
        System.Collections.Immutable.ImmutableArray<MutationReceipt> mutations)
    {
        var placement = state.Owner.Database.ReadAtomicPartitionPlacement(QueueViewLeaseIsolationProtocol.Root,
            state.Partition);
        return new(id, NextToken(state, placement.Incarnation, placement.PlacementEpoch),
            mutations, state.Owner.Database.Durability);
    }

    internal static CommitToken NextToken(QueueViewLeaseIsolationState state)
    {
        var placement = state.Owner.Database.ReadAtomicPartitionPlacement(QueueViewLeaseIsolationProtocol.Root,
            state.Partition);
        return NextToken(state, placement.Incarnation, placement.PlacementEpoch);
    }

    private static CommitToken NextToken(QueueViewLeaseIsolationState state, Guid incarnation, long epoch)
        => new(incarnation, state.Partition.AtomicPartitionId,
            checked(state.Owner.JournalCut().LastIndex + QueueViewLeaseIsolationProtocol.One), epoch);

    internal static ReplicatedOperation Original(QueueViewLeaseIsolationHistory history)
        => history.Entry.Operation ?? throw new InvalidOperationException(QueueViewLeaseIsolationProtocol.Absent);

    internal static async Task BytesAsync(byte[]? actual, byte[]? expected)
    {
        await Assert.That((actual is null) == (expected is null)).IsTrue();
        if (actual is not null && expected is not null)
        {
            await Assert.That(actual.AsSpan().SequenceEqual(expected)).IsTrue();
        }
    }
}

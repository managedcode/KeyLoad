using KeyLoad.Core.CommandOutcomes.Contracts;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class QueueViewLeaseIsolationOperations
{
    internal static async Task<QueueViewLeaseIsolationHistory> SendAsync<T>(QueueViewLeaseIsolationState state,
        OperationKind kind, T request, Guid id, object expected) where T : notnull
        => await SendAsync(state, kind, request, id, _ => expected);

    internal static async Task<QueueViewLeaseIsolationHistory> SendAsync<T>(QueueViewLeaseIsolationState state,
        OperationKind kind, T request, Guid id, Func<Command<DatabaseCommand>, object> expected) where T : notnull
    {
        state.Token.ThrowIfCancellationRequested();
        var owner = state.Owner;
        var index = checked(owner.ReadCut().AppliedPosition + QueueViewLeaseIsolationProtocol.One);
        var operation = owner.Database.CreateCommand(kind, id, QueueViewLeaseIsolationProtocol.Root,
            owner.Database.EvaluationClock.GetUtcNow(), request);
        operation = owner.Database.OwnCommand(operation);
        var predicted = expected(operation);
        var result = owner.Submit(operation);
        var entry = owner.Entry(operation);
        await Assert.That(entry.CanonicalSequence).IsEqualTo(index);
        await QueueViewLeaseIsolationResults.SameAsync(result, Result<object>.Succeed(predicted));
        var global = kind is OperationKind.ConfigureResource or OperationKind.ConfigurePrincipal;
        var key = global ? KeySpace.GlobalOutcome(QueueViewLeaseIsolationProtocol.Root, id)
            : KeySpace.PartitionOutcome(state.Partition, QueueViewLeaseIsolationProtocol.Root, id);
        var locatorKey = KeySpace.OutcomeLocator(state.Partition, QueueViewLeaseIsolationProtocol.Root, id);
        var outcome = owner.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException(QueueViewLeaseIsolationProtocol.Absent);
        var locator = owner.Store.Read(view => view.ReadOwnedValue(locatorKey));
        var stored = OrleansSerialization.Deserialize<StoredOutcome>(outcome);
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
        return new(id, NextToken(state, placement.Incarnation), mutations, state.Owner.Database.Durability,
            state.Owner.Database.Durability == DurabilityProfile.Buffered ? 0 : 1);
    }

    internal static CommitToken NextToken(QueueViewLeaseIsolationState state)
    {
        var placement = state.Owner.Database.ReadAtomicPartitionPlacement(QueueViewLeaseIsolationProtocol.Root,
            state.Partition);
        return NextToken(state, placement.Incarnation);
    }

    private static CommitToken NextToken(QueueViewLeaseIsolationState state, Guid incarnation)
        => new(incarnation, state.Partition.AtomicPartitionId,
            checked(state.Owner.ReadCut().AppliedPosition + QueueViewLeaseIsolationProtocol.One));

    internal static Command<DatabaseCommand> Original(QueueViewLeaseIsolationHistory history)
        => history.Entry.PreparedCommand;

    internal static async Task BytesAsync(byte[]? actual, byte[]? expected)
    {
        await Assert.That((actual is null) == (expected is null)).IsTrue();
        if (actual is not null && expected is not null)
        {
            await Assert.That(actual.AsSpan().SequenceEqual(expected)).IsTrue();
        }
    }
}

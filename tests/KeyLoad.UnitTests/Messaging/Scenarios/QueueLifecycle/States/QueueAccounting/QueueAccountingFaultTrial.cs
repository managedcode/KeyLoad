using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueAccountingFaultTrial
{
    internal static async Task RunAsync(QueueAccountingOperation kind, QueueAccountingColumn column, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var fixture = new TestDatabase();
        var state = await QueueAccountingFaultSetup.SeedAsync(fixture, kind);
        var expectedError = RefusalCode(kind, column);
        QueueAccountingFaultSetup.SetCounters(fixture.Store, state.Lane, QueueAccountingFaultSetup.Damage(state, column));
        var damaged = QueueWholeFlowStorage.Bytes(fixture.Store);
        await QueueAccountingFaultAssertions.RefusedAsync(fixture.Database, fixture.Store, state, damaged, expectedError);
        await QueueAccountingFaultAssertions.RefusedAsync(fixture.Database, fixture.Store, state, damaged, expectedError);
        fixture.Store.Dispose();
        using var reopened = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(reopened);
        await QueueAccountingFaultAssertions.RefusedAsync(database, reopened, state, damaged, expectedError);
        QueueAccountingFaultSetup.SetCounters(reopened, state.Lane, state.Counters);
        var result = database.Apply(state.Operation);
        await QueueAccountingFaultAssertions.CompletedAsync(database, reopened, state, result);
        await ReplayAsync(database, state, result);
        var healthy = await QueueAccountingFaultHealthy.RunAsync(database, reopened, state);
        var complete = QueueWholeFlowStorage.Bytes(reopened);
        reopened.Dispose();
        using var cold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var restored = QueueWholeFlowStorage.Open(cold);
        await ReplayAsync(restored, state, result);
        await ReplayResultAssertions.Same<CommitReceipt>(QueueWholeFlowStorage.Apply(restored, OperationKind.Batch,
            state.Producer, state.Producer.CommandId, new DateTimeOffset(state.Operation.Timestamp)), state.Produced);
        await QueueAccountingFaultHealthy.ReplayAsync(restored, state, healthy);
        await Assert.That(QueueWholeFlowStorage.Bytes(cold)).IsEquivalentTo(complete, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static ErrorCode RefusalCode(QueueAccountingOperation kind, QueueAccountingColumn column)
        => (kind, column) switch
        {
            (QueueAccountingOperation.Ack, QueueAccountingColumn.InFlightMessages)
                or (QueueAccountingOperation.Ack, QueueAccountingColumn.StoredMessages)
                or (QueueAccountingOperation.Ack, QueueAccountingColumn.StoredBytes)
                or (QueueAccountingOperation.Processing, QueueAccountingColumn.InFlightMessages)
                => ErrorCode.RecoveryRequired,
            _ => ErrorCode.Corruption
        };

    private static Task ReplayAsync(DatabaseEngine database, QueueAccountingFaultState state, Result<object> result)
        => state.Operation.Value!.Kind == OperationKind.Receive
            ? ReplayResultAssertions.Same<ReceiveResult>(database.Apply(state.Operation), result)
            : ReplayResultAssertions.Same<CommitReceipt>(database.Apply(state.Operation), result);
}

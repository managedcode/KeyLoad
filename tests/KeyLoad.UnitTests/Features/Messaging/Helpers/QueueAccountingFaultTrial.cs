using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueAccountingFaultTrial
{
    internal static async Task RunAsync(QueueAccountingOperation kind, QueueAccountingColumn column, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var fixture = new TestDatabase();
        var state = await QueueAccountingFaultSetup.SeedAsync(fixture, kind);
        QueueAccountingFaultSetup.SetCounters(fixture.Store, state.Lane, QueueAccountingFaultSetup.Damage(state, column));
        var damaged = QueueWholeFlowStorage.Bytes(fixture.Store);
        await QueueAccountingFaultAssertions.RefusedAsync(fixture.Database, fixture.Store, state, damaged);
        await QueueAccountingFaultAssertions.RefusedAsync(fixture.Database, fixture.Store, state, damaged);
        fixture.Store.Dispose();
        using var reopened = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(reopened);
        await QueueAccountingFaultAssertions.RefusedAsync(database, reopened, state, damaged);
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
        await NativeReplayResultAssertions.Same<CommitReceipt>(QueueWholeFlowStorage.Apply(restored, OperationKind.Batch,
            state.Producer, state.Producer.CommandId, state.Operation.EvaluatedAt), state.Produced);
        await QueueAccountingFaultHealthy.ReplayAsync(restored, state, healthy);
        await Assert.That(QueueWholeFlowStorage.Bytes(cold)).IsEquivalentTo(complete, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static Task ReplayAsync(DatabaseEngine database, QueueAccountingFaultState state, OperationResult result)
        => state.Operation.Kind == OperationKind.Receive
            ? NativeReplayResultAssertions.Same<ReceiveResult>(database.Apply(state.Operation), result)
            : NativeReplayResultAssertions.Same<CommitReceipt>(database.Apply(state.Operation), result);
}

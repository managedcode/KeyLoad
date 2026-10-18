using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueuePendingIncrementCapacityFlow
{
    private const int FaultFailureRepairCommits = 3;

    internal static async Task RunAsync(QueueLifecycleCeilingFault fault, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var fixture = new TestDatabase();
        QueueLifecycleProvisioning.Seed(fixture, byteSublimit: false);
        var lane = new QueueLaneRef(fixture.Partition, QueueLifecycleTestProtocol.Queue);
        var recorder = new QueueLifecycleNativeRecorder(fixture.Database, lane, QueueLifecycleHealthy.Enqueue);
        var state = new QueueLifecycleTestState(fixture.Partition, QueueWholeFlowStorage.Clock(fixture.Store))
        {
            NativeSubmit = recorder.Apply
        };
        await QueueLifecycleInitialPhase.RunAsync(fixture.Database, fixture.Store, state, token);
        var cancel = state.CancelledCommand!;
        state.Execute(fixture.Database, OperationKind.Batch, cancel, cancel.CommandId).Get<CommitReceipt>();
        await recorder.AssertAsync(token);
        var image = QueueLifecycleImage.Capture(fixture.Store, lane);
        var position = fixture.Store.Position;
        var rows = QueueLifecycleCeilingInjection.Inject(fixture.Store, lane, QueueLifecycleTestProtocol.Pending, fault);
        var failed = await QueueLifecycleCeilingFailureAssertions.FailAsync(fixture.Database, rows,
            QueueLifecycleTestProtocol.Administrator, state.Time, token);
        await ColdAsync(fixture, state, recorder, rows, failed, image, position, token);
    }

    private static async Task ColdAsync(TestDatabase fixture, QueueLifecycleTestState state,
        QueueLifecycleNativeRecorder recorder, QueueLifecycleCeilingRows rows, Result<object> failed,
        string[] original, long originalPosition, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = NativeSerialization.Serialize(fixture.Store.Identity);
        fixture.Store.Dispose();
        using var cold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(cold);
        recorder.Database = database;
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, image, position, identity);
        await QueueLifecycleCeilingFailureAssertions.ReplayAsync(database, rows, failed,
            QueueLifecycleTestProtocol.Administrator, state.Time, token);
        QueueLifecycleCeilingInjection.Repair(cold, rows);
        await QueueLifecycleImage.SameAsync(cold, state.Lane, original);
        await Assert.That(cold.Position).IsEqualTo(originalPosition + FaultFailureRepairCommits);
        await QueueLifecycleCeilingFailureAssertions.ReplayAsync(database, rows, failed,
            QueueLifecycleTestProtocol.Administrator, state.Time, token);
        await QueueLifecycleContinuation.RunAsync(database, cold, state, token);
        await QueueLifecycleHealthy.RunAsync(database, state, token);
        await recorder.AssertAsync(token);
        await TerminalAsync(database, cold, state);
        await FinalAsync(cold, fixture.Directory, state, recorder, rows, failed, token);
    }

    private static async Task FinalAsync(ZoneTreeStore store, string directory, QueueLifecycleTestState state,
        QueueLifecycleNativeRecorder recorder, QueueLifecycleCeilingRows rows, Result<object> failed,
        CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        store.Dispose();
        using var cold = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(cold);
        recorder.Database = database;
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, image, position, identity);
        await TerminalAsync(database, cold, state);
        await QueueLifecycleNativeHistoryAssertions.ReplayAsync(recorder, state.Lane, token);
        await QueueLifecycleCeilingFailureAssertions.ReplayAsync(database, rows, failed,
            QueueLifecycleTestProtocol.Administrator, state.Time, token);
        await TerminalAsync(database, cold, state);
    }

    private static async Task TerminalAsync(DatabaseEngine database, ZoneTreeStore store, QueueLifecycleTestState state)
    {
        await QueueLifecycleImage.BodyAsync(database, state, QueueLifecycleTestProtocol.Parked, MessageState.Cancelled,
            QueueLifecycleTestProtocol.Four, QueueLifecycleTestProtocol.Two, QueueLifecycleTestProtocol.None);
        await QueueLifecycleImage.BodyAsync(database, state, QueueLifecycleTestProtocol.Pending, MessageState.Acked,
            QueueLifecycleTestProtocol.Seven, QueueLifecycleTestProtocol.Two, QueueLifecycleTestProtocol.None);
        await QueueLifecycleImage.BodyAsync(database, state, QueueLifecycleTestProtocol.Held, MessageState.Cancelled,
            QueueLifecycleTestProtocol.Three, QueueLifecycleTestProtocol.Two, QueueLifecycleTestProtocol.None);
        await QueueLifecycleImage.BodyAsync(database, state, QueueLifecycleTestProtocol.Healthy, MessageState.Acked,
            QueueLifecycleTestProtocol.Three, QueueLifecycleTestProtocol.One, QueueLifecycleTestProtocol.None);
        await QueueLifecycleAccountingAssertions.TerminalAsync(store, state, healthy: true);
        await QueueLifecycleCeilingTerminalIndices.EmptyAsync(store, state.Lane);
    }
}

using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Messaging;

internal static class QueueFullDeadLetterSequenceControl
{
    internal static async Task RunAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var fixture = new TestDatabase();
        QueueLifecycleProvisioning.Seed(fixture, byteSublimit: false);
        var lane = new QueueLaneRef(fixture.Partition, QueueLifecycleTestProtocol.Queue);
        var recorder = new QueueLifecycleRecorder(fixture.Database, lane, QueueLifecycleHealthy.Enqueue);
        var state = new QueueLifecycleTestState(fixture.Partition, QueueWholeFlowStorage.Clock(fixture.Store))
        {
            RuntimeSubmit = recorder.Apply
        };
        await QueueLifecycleInitialPhase.RunAsync(fixture.Database, fixture.Store, state, token);
        var counters = QueueLifecycleControlCold.Read(fixture.Database, lane);
        QueueLifecycleControlCold.Write(fixture.Database, lane, counters with
        {
            NextParkedSequence = long.MaxValue
        });
        QueueLifecycleOperations.Complete(fixture.Database, state, state.OriginalHeld!, DeliveryAction.Nack).Get<CommitReceipt>();
        await QueueFullDeadLetterControlAssertions.PendingAsync(fixture.Database, state);
        await recorder.AssertAsync(token);
        await ColdAsync(fixture, state, recorder, token);
    }

    private static async Task ColdAsync(TestDatabase fixture, QueueLifecycleTestState state,
        QueueLifecycleRecorder recorder, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = OrleansSerialization.Serialize(fixture.Store.Identity);
        fixture.Store.Dispose();
        using var cold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(cold);
        recorder.Database = database;
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, image, position, identity);
        await QueueFullDeadLetterControlAssertions.PendingAsync(database, state);
        var counters = QueueLifecycleControlCold.Read(database, state.Lane);
        QueueLifecycleControlCold.Write(database, state.Lane, counters with
        {
            NextParkedSequence = QueueLifecycleTestProtocol.One
        });
        await QueueFullDeadLetterControlContinuation.RunAsync(database, cold, state, token);
        await recorder.AssertAsync(token);
        await QueueFullDeadLetterControlAssertions.TerminalAsync(database, state);
        await QueueLifecycleControlCold.FinalAsync(cold, fixture.Directory, recorder, state.Lane,
            current => QueueFullDeadLetterControlAssertions.TerminalAsync(current, state), token);
    }
}

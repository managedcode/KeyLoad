using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class QueueExhaustedNackCapacityFlow
{

    internal static async Task RunAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var fixture = new TestDatabase();
        QueueLifecycleProvisioning.Seed(fixture, byteSublimit: false);
        var lane = new QueueLaneRef(fixture.Partition, QueueLifecycleTestProtocol.Queue);
        var recorder = new QueueLifecycleRecorder(fixture.Database, lane, QueueLifecycleHealthy.Enqueue);
        var state = new QueueLifecycleTestState(fixture.Partition, QueueWholeFlowStorage.Clock(fixture.Store, fixture.Partition))
        {
            RuntimeSubmit = recorder.Apply
        };
        await QueueLifecycleInitialPhase.RunAsync(fixture.Database, fixture.Store, state, token);
        var cancel = state.CancelledCommand!;
        state.Execute(fixture.Database, OperationKind.Batch, cancel, cancel.CommandId).Get<CommitReceipt>();
        var image = QueueLifecycleImage.Capture(fixture.Store, lane);
        var injected = QueueLifecycleCeilingInjection.Inject(fixture.Store, lane, QueueLifecycleTestProtocol.Held,
            QueueLifecycleCeilingFault.ParkSequence);
        var rows = injected with
        {
            Delivery = new(injected.Request.CommandId, lane, state.OriginalHeld!.Token, DeliveryAction.Nack)
        };
        var failed = await QueueLifecycleCeilingFailureAssertions.FailAsync(fixture.Database, rows,
            QueueLifecycleTestProtocol.Administrator, state.Time, token);
        await ColdAsync(fixture, state, recorder, rows, failed, image, token);
    }

    private static async Task ColdAsync(TestDatabase fixture, QueueLifecycleTestState state,
        QueueLifecycleRecorder recorder, QueueLifecycleCeilingRows rows, Result<object> failed,
        string[] original, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = OrleansSerialization.Serialize(fixture.Store.Identity);
        fixture.Store.Dispose();
        using var cold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution());
        var database = QueueWholeFlowStorage.Open(cold);
        recorder.Database = database;
        position = await QueueLeaseExpiryEqualityAssertions.ReopenedImageAsync(cold, image, position, identity);
        await QueueLifecycleCeilingFailureAssertions.ReplayAsync(database, rows, failed,
            QueueLifecycleTestProtocol.Administrator, state.Time, token);
        QueueLifecycleCeilingInjection.Repair(cold, rows);
        await QueueLifecycleImage.SameAsync(cold, state.Lane, original);
        position = await NativeStoreAssertions.AdvancedAsync(cold, position);
        await QueueLifecycleCeilingFailureAssertions.ReplayAsync(database, rows, failed,
            QueueLifecycleTestProtocol.Administrator, state.Time, token);
        QueueLifecycleOperations.Complete(database, state, state.OriginalHeld!, DeliveryAction.Nack).Get<CommitReceipt>();
        await QueueFullDeadLetterControlContinuation.RunAsync(database, cold, state, token);
        await recorder.AssertAsync(token);
        await QueueFullDeadLetterControlAssertions.TerminalAsync(database, state, QueueLifecycleTestProtocol.Three);
        await QueueLifecycleControlCold.FinalAsync(cold, fixture.Directory, recorder, state.Lane,
            current => TerminalAsync(current, state, rows, failed, token), token);
    }

    private static async Task TerminalAsync(DatabaseEngine database, QueueLifecycleTestState state,
        QueueLifecycleCeilingRows rows, Result<object> failed, CancellationToken token)
    {
        await QueueFullDeadLetterControlAssertions.TerminalAsync(database, state, QueueLifecycleTestProtocol.Three);
        await QueueLifecycleCeilingFailureAssertions.ReplayAsync(database, rows, failed,
            QueueLifecycleTestProtocol.Administrator, state.Time, token);
    }
}

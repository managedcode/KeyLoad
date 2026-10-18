using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueExhaustedNackCapacityFlow
{
    private const int FaultFailureRepairCommits = 3;

    internal static async Task RunAsync(CancellationToken token)
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
        var image = QueueLifecycleImage.Capture(fixture.Store, lane);
        var position = fixture.Store.Position;
        var injected = QueueLifecycleCeilingInjection.Inject(fixture.Store, lane, QueueLifecycleTestProtocol.Held,
            QueueLifecycleCeilingFault.ParkSequence);
        var rows = injected with
        {
            Delivery = new(injected.Request.CommandId, lane, state.OriginalHeld!.Token, DeliveryAction.Nack)
        };
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

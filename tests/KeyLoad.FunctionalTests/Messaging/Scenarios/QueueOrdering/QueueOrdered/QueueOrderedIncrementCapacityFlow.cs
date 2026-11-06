using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class QueueOrderedIncrementCapacityFlow
{
    private const int MaximumAttempts = 2;
    private const int StoredMessages = 3;

    internal static async Task RunAsync(QueueLifecycleCeilingFault fault, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var fixture = new TestDatabase();
        fixture.Configure(QueueOrderedRetryProtocol.Queue, ResourceKind.WorkQueue, queuePolicy: new()
        {
            MaxAttempts = MaximumAttempts,
            MaxStoredMessages = StoredMessages,
            MaxDeadLetterMessages = 1,
            OrderingProfile = QueueOrderingProfile.StrictPerKey,
            ParkedHeadPolicy = QueueParkedHeadPolicy.Continue,
            RetryJitter = QueueRetryJitter.Full
        });
        var lane = new QueueLaneRef(fixture.Partition, QueueOrderedRetryProtocol.Queue);
        var recorder = new QueueLifecycleRecorder(fixture.Database, lane, id => QueueOrderedRetryOperations.Literal(id));
        var state = new QueueOrderedRetryState(fixture.Partition, QueueWholeFlowStorage.Clock(fixture.Store, fixture.Partition), QueueParkedHeadPolicy.Continue, QueueOrderedRetryRestoreAuthority.Administrator)
        {
            BeforeApply = recorder.Observe
        };
        await QueueOrderedRetryRestoreAuthority.ProvisionAsync(fixture.Database, state);
        await QueueOrderedRetryInitial.RunAsync(fixture.Database, state, token);
        recorder.Capture(state.Outcomes);
        await recorder.AssertAsync(token);
        var image = QueueOrderedRetryImage.Capture(fixture.Store, lane);
        var rows = QueueLifecycleCeilingInjection.Inject(fixture.Store, lane, QueueOrderedRetryProtocol.First, fault);
        var failed = await QueueLifecycleCeilingFailureAssertions.FailAsync(fixture.Database, rows,
            state.Principal, state.Time, token);
        await ColdAsync(fixture, state, recorder, rows, failed, image, fault, token);
    }

    private static async Task ColdAsync(TestDatabase fixture, QueueOrderedRetryState state,
        QueueLifecycleRecorder recorder, QueueLifecycleCeilingRows rows, Result<object> failed,
        string[] original, QueueLifecycleCeilingFault fault, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = OrleansSerialization.Serialize(fixture.Store.Identity);
        fixture.Store.Dispose();
        using var cold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution());
        var database = QueueWholeFlowStorage.Open(cold);
        recorder.Database = database;
        position = await QueueLeaseExpiryEqualityAssertions.ReopenedImageAsync(cold, image, position, identity);
        await QueueLifecycleCeilingFailureAssertions.ReplayAsync(database, rows, failed, state.Principal, state.Time, token);
        QueueLifecycleCeilingInjection.Repair(cold, rows);
        await QueueOrderedRetryImage.SameAsync(cold, state.Lane, original);
        position = await NativeStoreAssertions.AdvancedAsync(cold, position);
        await QueueLifecycleCeilingFailureAssertions.ReplayAsync(database, rows, failed, state.Principal, state.Time, token);
        if (QueueLifecycleCeilingInjection.IsCancel(fault))
        {
            await QueueOrderedCeilingCancellationContinuation.RunAsync(database, state, token);
        }
        else
        {
            await QueueOrderedRetryContinuation.RunAsync(database, state, token);
            await QueueOrderedRetryAssertions.TerminalAsync(database, state, healthy: false);
        }
        await QueueOrderedRetryHealthy.RunAsync(database, state, token);
        recorder.Capture(state.Outcomes);
        await recorder.AssertAsync(token);
        await FinalAsync(cold, fixture.Directory, state, recorder, rows, failed, fault, token);
    }

    private static async Task FinalAsync(ZoneTreeStore store, string directory, QueueOrderedRetryState state,
        QueueLifecycleRecorder recorder, QueueLifecycleCeilingRows rows, Result<object> failed,
        QueueLifecycleCeilingFault fault, CancellationToken token)
    {
        await TerminalAsync(recorder.Database, state, fault);
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        store.Dispose();
        using var cold = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution());
        recorder.Database = QueueWholeFlowStorage.Open(cold);
        position = await QueueLeaseExpiryEqualityAssertions.ReopenedImageAsync(cold, image, position, identity);
        await TerminalAsync(recorder.Database, state, fault);
        await QueueLifecycleHistoryAssertions.ReplayAsync(recorder, state.Lane, token);
        await QueueLifecycleCeilingFailureAssertions.ReplayAsync(recorder.Database, rows, failed, state.Principal, state.Time, token);
        await TerminalAsync(recorder.Database, state, fault);
    }

    private static Task TerminalAsync(DatabaseEngine database, QueueOrderedRetryState state, QueueLifecycleCeilingFault fault)
        => QueueLifecycleCeilingInjection.IsCancel(fault)
            ? QueueOrderedCeilingCancellationContinuation.TerminalAsync(database, state)
            : QueueOrderedRetryAssertions.TerminalAsync(database, state, healthy: true);
}

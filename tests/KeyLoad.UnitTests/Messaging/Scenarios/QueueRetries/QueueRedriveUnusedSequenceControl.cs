using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Messaging;

internal static class QueueRedriveUnusedSequenceControl
{
    private const int Attempts = 2;
    private const int StoredCapacity = 3;
    private const int ParkedCapacity = 1;
    private const int ScheduledDelaySeconds = 10;
    private const int ParkedVersion = 6;
    private const int OriginalGeneration = 1;
    private const int RedrivenVersion = 7;
    private const int OriginalReady = 4;
    private const int OriginalOrder = 3;

    internal static async Task RunAsync(QueueLifecycleNonIncrementControl control, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var block = control == QueueLifecycleNonIncrementControl.BlockRedriveKeepsOrder;
        using var fixture = new TestDatabase();
        var policy = block ? QueueParkedHeadPolicy.Block : QueueParkedHeadPolicy.Continue;
        fixture.Configure(QueueOrderedRetryProtocol.Queue, ResourceKind.WorkQueue, queuePolicy: new()
        {
            MaxAttempts = Attempts,
            MaxStoredMessages = StoredCapacity,
            MaxDeadLetterMessages = ParkedCapacity,
            OrderingProfile = QueueOrderingProfile.StrictPerKey,
            ParkedHeadPolicy = policy,
            RetryJitter = QueueRetryJitter.Full
        });
        var lane = new QueueLaneRef(fixture.Partition, QueueOrderedRetryProtocol.Queue);
        var recorder = new QueueLifecycleRecorder(fixture.Database, lane, id => QueueOrderedRetryOperations.Literal(id));
        var state = new QueueOrderedRetryState(fixture.Partition, QueueWholeFlowStorage.Clock(fixture.Store), policy, QueueOrderedRetryRestoreAuthority.Administrator)
        {
            BeforeApply = recorder.Observe
        };
        await QueueOrderedRetryRestoreAuthority.ProvisionAsync(fixture.Database, state);
        await QueueOrderedRetryInitial.RunAsync(fixture.Database, state, token);
        var counters = QueueLifecycleControlCold.Read(fixture.Database, lane);
        QueueLifecycleControlCold.Write(fixture.Database, lane, block
            ? counters with
            {
                NextOrderSequence = long.MaxValue
            }
            : counters with
            {
                NextReadySequence = long.MaxValue
            });
        if (block)
        {
            await QueueOrderedRetryContinuation.RunAsync(fixture.Database, state, token);
        }
        else
        {
            var available = state.Time.AddSeconds(ScheduledDelaySeconds);
            state.Batch(fixture.Database, new RedriveQueueMessage(lane.Queue, QueueOrderedRetryProtocol.First,
                ParkedVersion, OriginalGeneration, available)).Get<CommitReceipt>();
            await QueueRedriveUnusedSequenceAssertions.ScheduledAsync(fixture.Database, state, available);
            state.Batch(fixture.Database, new CancelQueueMessage(lane.Queue, QueueOrderedRetryProtocol.First,
                RedrivenVersion, QueueOrderedRetryProtocol.Two)).Get<CommitReceipt>();
            QueueOrderedRetryOperations.Complete(fixture.Database, state, state.SecondLease!, DeliveryAction.Ack).Get<CommitReceipt>();
        }
        recorder.Capture(state.Outcomes);
        await recorder.AssertAsync(token);
        await ColdAsync(fixture, state, recorder, block, token);
    }

    private static async Task ColdAsync(TestDatabase fixture, QueueOrderedRetryState state,
        QueueLifecycleRecorder recorder, bool block, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = OrleansSerialization.Serialize(fixture.Store.Identity);
        fixture.Store.Dispose();
        using var cold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(cold);
        recorder.Database = database;
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, image, position, identity);
        await QueueRedriveUnusedSequenceAssertions.ExhaustedCountersAsync(database, state, block);
        var counters = QueueLifecycleControlCold.Read(database, state.Lane);
        QueueLifecycleControlCold.Write(database, state.Lane, block
            ? counters with
            {
                NextOrderSequence = OriginalOrder
            }
            : counters with
            {
                NextReadySequence = OriginalReady
            });
        await QueueOrderedRetryHealthy.RunAsync(database, state, token);
        recorder.Capture(state.Outcomes);
        await recorder.AssertAsync(token);
        await QueueRedriveUnusedSequenceAssertions.TerminalAsync(database, state, block);
        await QueueLifecycleControlCold.FinalAsync(cold, fixture.Directory, recorder, state.Lane,
            current => QueueRedriveUnusedSequenceAssertions.TerminalAsync(current, state, block), token);
    }
}

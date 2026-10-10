using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueRetryLeaseRaceTrial
{
    internal static async Task RunAsync(bool renewFirst, CancellationToken token)
    {
        using var fixture = new TestDatabase();
        fixture.Configure(QueueOrderedRetryProtocol.Queue, ResourceKind.WorkQueue, queuePolicy: new()
        { OrderingProfile = QueueOrderingProfile.StrictPerKey, RetryJitter = QueueRetryJitter.Full });
        var state = new QueueOrderedRetryState(fixture.Partition, QueueWholeFlowStorage.Clock(fixture.Store), QueueParkedHeadPolicy.Continue);
        state.Batch(fixture.Database, QueueOrderedRetryOperations.Literal(QueueOrderedRetryProtocol.First)).Get<CommitReceipt>();
        var original = await QueueOrderedRetryOperations.ClaimAsync(fixture.Database, state, QueueOrderedRetryProtocol.First);
        if (renewFirst)
        { await QueueRetryLeaseRenewFirst.RunAsync(fixture.Database, state, original, token); }
        else
        { await QueueRetryLeaseReclaimFirst.RunAsync(fixture.Database, state, original, token); }
        await QueueRetryLeaseRaceAssertions.TerminalAsync(fixture.Database, state, renewFirst, healthy: false);
        await QueueOrderedRetryOperations.ReplayAsync(fixture.Database, state);
        var image = QueueOrderedRetryImage.Capture(fixture.Store, state.Lane);
        fixture.Store.Dispose();
        using var cold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(cold);
        await QueueOrderedRetryImage.SameAsync(cold, state.Lane, image);
        await QueueOrderedRetryOperations.ReplayAsync(database, state);
        await QueueOrderedRetryHealthy.RunAsync(database, state, token);
        await QueueRetryLeaseRaceAssertions.TerminalAsync(database, state, renewFirst, healthy: true);
        await QueueRetryStrictExpiry.RunAsync(fixture.Directory, cold, database, state, renewFirst, token);
    }
}

using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueOrderedRetryTrial
{
    internal static async Task RunAsync(QueueParkedHeadPolicy parkedHead, CancellationToken token)
    {
        using var fixture = new TestDatabase();
        fixture.Configure(QueueOrderedRetryProtocol.Queue, ResourceKind.WorkQueue, queuePolicy: new()
        {
            MaxAttempts = QueueOrderedRetryProtocol.Two,
            MaxStoredMessages = QueueOrderedRetryProtocol.Three,
            MaxDeadLetterMessages = QueueOrderedRetryProtocol.One,
            OrderingProfile = QueueOrderingProfile.StrictPerKey,
            ParkedHeadPolicy = parkedHead,
            RetryJitter = QueueRetryJitter.Full
        });
        var state = new QueueOrderedRetryState(fixture.Partition, QueueWholeFlowStorage.Clock(fixture.Store), parkedHead, QueueOrderedRetryRestoreAuthority.Administrator);
        await QueueOrderedRetryRestoreAuthority.ProvisionAsync(fixture.Database, state);
        await QueueOrderedRetryInitial.RunAsync(fixture.Database, state, token);
        await QueueOrderedRetryBackupRestore.RunAsync(fixture.Store, state, token);
        await QueueOrderedRetryContinuation.RunAsync(fixture.Database, state, token);
        await QueueOrderedRetryAssertions.TerminalAsync(fixture.Database, state, healthy: false);
        await QueueOrderedRetryOperations.ReplayAsync(fixture.Database, state);
        var image = QueueOrderedRetryImage.Capture(fixture.Store, state.Lane);
        fixture.Store.Dispose();
        using var cold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(cold);
        await QueueOrderedRetryImage.SameAsync(cold, state.Lane, image);
        await QueueOrderedRetryOperations.ReplayAsync(database, state);
        await QueueOrderedRetryHealthy.RunAsync(database, state, token);
        await QueueOrderedRetryAssertions.TerminalAsync(database, state, healthy: true);
        await QueueOrderedRetryEmptyProfile.RunAsync(fixture.Directory, cold, database, state, token);
    }
}

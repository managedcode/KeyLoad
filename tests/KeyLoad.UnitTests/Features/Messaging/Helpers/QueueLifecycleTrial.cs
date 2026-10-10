using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLifecycleTrial
{
    internal static async Task RunAsync(bool byteSublimit, CancellationToken token)
    {
        using var fixture = new TestDatabase();
        QueueLifecycleProvisioning.Seed(fixture, byteSublimit);
        var state = new QueueLifecycleTestState(fixture.Partition, QueueWholeFlowStorage.Clock(fixture.Store));
        await QueueLifecycleInitialPhase.RunAsync(fixture.Database, fixture.Store, state, token);
        await QueueLifecycleBackupRestore.RunAsync(fixture.Store, state, token);
        var initial = QueueLifecycleImage.Capture(fixture.Store, state.Lane);
        fixture.Store.Dispose();
        using var reopened = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(reopened);
        await QueueLifecycleImage.SameAsync(reopened, state.Lane, initial);
        await QueueLifecycleOperations.ReplayAsync(database, state);
        await QueueLifecycleContinuation.RunAsync(database, reopened, state, token);
        var terminal = QueueLifecycleImage.Capture(reopened, state.Lane);
        reopened.Dispose();
        using var cold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var restored = QueueWholeFlowStorage.Open(cold);
        await QueueLifecycleImage.SameAsync(cold, state.Lane, terminal);
        await QueueLifecycleOperations.ReplayAsync(restored, state);
        await QueueLifecycleHealthy.RunAsync(restored, state, token);
        await QueueLifecycleAccountingAssertions.TerminalAsync(cold, state, true);
    }
}

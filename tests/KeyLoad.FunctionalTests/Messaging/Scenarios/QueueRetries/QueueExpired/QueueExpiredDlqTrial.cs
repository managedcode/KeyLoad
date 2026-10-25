using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class QueueExpiredDlqTrial
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new TestDatabase();
            await ServerFailureObserver.ObserveAsync(() => OwnedAsync(fixture, failures, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task OwnedAsync(TestDatabase fixture, List<Exception> failures, CancellationToken token)
    {
        var state = await QueueExpiredDlqOperations.SeedAsync(fixture, token);
        await QueueExpiredDlqOperations.RefusalsAsync(fixture.Database, fixture.Store, state, false);
        await QueueExpiredDlqOperations.RollbackAsync(fixture.Database, fixture.Store, state);
        var original = QueueLifecycleImage.Capture(fixture.Store, state.Operations.Lane);
        fixture.Store.Dispose();
        string[]? afterCancel = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var reopened = new ZoneTreeStore(new(fixture.Directory),
                UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var database = QueueWholeFlowStorage.Open(reopened);
                await QueueLifecycleImage.SameAsync(reopened, state.Operations.Lane, original);
                await QueueExpiredDlqOperations.RefusalsAsync(database, reopened, state, false);
                await QueueExpiredDlqAssertions.CancelAsync(database, state.Operations, QueueLifecycleTestProtocol.Parked);
                await QueueExpiredDlqOperations.RefusalsAsync(database, reopened, state, true);
                afterCancel = QueueLifecycleImage.Capture(reopened, state.Operations.Lane);
            }, failures);
        }, failures);
        if (failures.Count != 0)
        {
            return;
        }
        await SecondColdAsync(fixture, state, afterCancel
            ?? throw new InvalidOperationException("The first cold DLQ cancellation did not finish."), failures, token);
    }

    private static async Task SecondColdAsync(TestDatabase fixture, QueueExpiredDlqState state,
        string[] expected, List<Exception> failures, CancellationToken token)
    {
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cold = new ZoneTreeStore(new(fixture.Directory),
                UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var database = QueueWholeFlowStorage.Open(cold);
                await QueueLifecycleImage.SameAsync(cold, state.Operations.Lane, expected);
                await QueueExpiredDlqOperations.RefusalsAsync(database, cold, state, true);
                await QueueExpiredDlqAssertions.CancelAsync(database, state.Operations, QueueLifecycleTestProtocol.Pending);
                await QueueExpiredDlqAssertions.TerminalAsync(database, state, false);
                await QueueLifecycleOperations.ReplayAsync(database, state.Operations);
                await QueueExpiredDlqOperations.HealthyAsync(database, state, token);
            }, failures);
        }, failures);
    }
}

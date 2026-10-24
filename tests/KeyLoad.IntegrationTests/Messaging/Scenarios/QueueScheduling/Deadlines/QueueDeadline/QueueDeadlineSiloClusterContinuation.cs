using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueDeadlineSiloClusterContinuation
{
    internal static async Task StopSiloAsync(ClusterFixture fixture, QueueDeadlineSiloClusterState state,
        QueueDeadlineSiloClusterOwnership ownership, CancellationToken token)
    {
        await WithAsync(fixture, state, ownership.SurvivorNode, async (admin, _) =>
        {
            await QueueDeadlineSiloClusterCurrent.ObserveAsync(admin, state, token);
            await Assert.That(TimeProvider.System.GetUtcNow() < state.Due).IsTrue();
        }, token);
        await fixture.KillServerAsync(ownership.OriginalSiloNode, QueueDeadlineSiloClusterProtocol.Fault, token);
        await Assert.That(TimeProvider.System.GetUtcNow() < state.Due).IsTrue();
        await ownership.RequireAvailableSilosAsync(fixture, token);
        await Assert.That(TimeProvider.System.GetUtcNow() < state.Due).IsTrue();
        await WithAsync(fixture, state, ownership.SurvivorNode, async (admin, _) =>
        {
            await QueueDeadlineSiloClusterCurrent.ObserveAsync(admin, state, token);
            await DueRecurringClusterAssertions.WaitForDueTimeAsync(state.Due, token);
        }, token);
    }

    internal static async Task RejoinAndColdAsync(ClusterFixture fixture, QueueDeadlineSiloClusterState state,
        QueueDeadlineSiloClusterOwnership ownership, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
            await fixture.BeginProcessRestartAsync(ownership.OriginalSiloNode, token), failures);
        await ServerFailureObserver.ObserveAsync(() =>
            fixture.RestartServerAsync(ownership.OriginalSiloNode, token), failures);
        await ServerFailureObserver.ObserveAsync(() => ownership.RequireRejoinedAsync(fixture, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await WithAsync(fixture, state, ownership.OriginalSiloNode, async (admin, worker) =>
        {
            var ready = QueueDeadlineSiloClusterExpected.Ready(false);
            await QueueDeadlineClusterAssertions.WaitReadyAsync(admin, state.Lane, ready, token);
            state.Original = ready;
            await QueueDeadlineSiloClusterCurrent.ObserveAsync(admin, state, token);
            await QueueDeadlineSiloClusterActions.StaleAsync(admin, state, token);
            await QueueDeadlineSiloClusterActions.ReplayAsync(admin, worker, state, token);
        }, token);
        await QueueLeaseClusterCold.RestartAsync(fixture, token);
        await WithAsync(fixture, state, ownership.OriginalSiloNode, async (admin, worker) =>
        {
            await QueueDeadlineSiloClusterCurrent.ObserveAsync(admin, state, token);
            await QueueDeadlineSiloClusterActions.StaleAsync(admin, state, token);
            await QueueDeadlineSiloClusterDelivery.SettleAsync(admin, worker, state, false, token);
            await QueueDeadlineSiloClusterActions.ReplayAsync(admin, worker, state, token);
            await QueueDeadlineSiloClusterActions.PublishAsync(admin, state, true, token);
            await QueueDeadlineSiloClusterDelivery.SettleAsync(admin, worker, state, true, token);
            await QueueDeadlineSiloClusterActions.ReplayAsync(admin, worker, state, token);
            await QueueDeadlineSiloClusterActions.StaleAsync(admin, state, token);
        }, token);
    }

    private static Task<bool> WithAsync(ClusterFixture fixture, QueueDeadlineSiloClusterState state, string node,
        Func<RequestCqrsClusterCallers, RequestCqrsClusterCallers, Task> operation, CancellationToken token)
        => QueueDeadlineClusterCallers.UseAsync(fixture, node, fixture.AdminKey,
            admin => QueueDeadlineClusterCallers.UseAsync(fixture, node, state.Identity.Secret,
                async worker => { await operation(admin, worker); return true; }, token), token);
}

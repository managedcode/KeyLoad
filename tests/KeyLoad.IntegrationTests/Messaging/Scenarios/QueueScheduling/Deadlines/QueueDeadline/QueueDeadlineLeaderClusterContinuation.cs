using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadlineLeaderClusterContinuation
{
    internal static async Task LoseLeaderAsync(ClusterFixture fixture, QueueDeadlineLeaderClusterState state,
        QueueDeadlineLeaderClusterOwnership ownership, CancellationToken token)
    {
        await WithAsync(fixture, state, ownership.SurvivorNode, async (admin, _) =>
        {
            await QueueDeadlineLeaderClusterCurrent.ObserveAsync(admin, state, token);
            await Assert.That(TimeProvider.System.GetUtcNow() < state.Due).IsTrue();
        }, token);
        await fixture.KillContainerAsync(ownership.OriginalLeaderNode, QueueDeadlineLeaderClusterProtocol.Fault, token);
        await Assert.That(TimeProvider.System.GetUtcNow() < state.Due).IsTrue();
        await ownership.RequireReplacementAsync(fixture, token);
        await Assert.That(TimeProvider.System.GetUtcNow() < state.Due).IsTrue();
        await WithAsync(fixture, state, ownership.SurvivorNode, async (admin, worker) =>
        {
            await QueueDeadlineLeaderClusterCurrent.ObserveAsync(admin, state, token);
            await DueRecurringClusterAssertions.WaitForDueTimeAsync(state.Due, token);
            var ready = QueueDeadlineLeaderClusterExpected.Ready(false);
            await QueueDeadlineClusterAssertions.WaitReadyAsync(admin, state.Lane, ready, token);
            state.Original = ready;
            await QueueDeadlineLeaderClusterCurrent.ObserveAsync(admin, state, token);
            await QueueDeadlineLeaderClusterActions.StaleAsync(admin, state, token);
            await QueueDeadlineLeaderClusterActions.ReplayAsync(admin, worker, state, token);
        }, token);
    }

    internal static async Task RejoinAndColdAsync(ClusterFixture fixture, QueueDeadlineLeaderClusterState state,
        QueueDeadlineLeaderClusterOwnership ownership, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
            await fixture.BeginContainerRestartAsync(ownership.OriginalLeaderNode, token), failures);
        await ServerFailureObserver.ObserveAsync(() =>
            fixture.RestartContainerAsync(ownership.OriginalLeaderNode, token), failures);
        await ServerFailureObserver.ObserveAsync(() => ownership.RequireRejoinedAsync(fixture, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await WithAsync(fixture, state, ownership.OriginalLeaderNode, async (admin, worker) =>
        {
            await QueueDeadlineLeaderClusterCurrent.ObserveAsync(admin, state, token);
            await QueueDeadlineLeaderClusterActions.StaleAsync(admin, state, token);
            await QueueDeadlineLeaderClusterActions.ReplayAsync(admin, worker, state, token);
        }, token);
        await QueueLeaseClusterCold.RestartAsync(fixture, token);
        await WithAsync(fixture, state, ownership.OriginalLeaderNode, async (admin, worker) =>
        {
            await QueueDeadlineLeaderClusterCurrent.ObserveAsync(admin, state, token);
            await QueueDeadlineLeaderClusterActions.StaleAsync(admin, state, token);
            await QueueDeadlineLeaderClusterDelivery.SettleAsync(admin, worker, state, false, token);
            await QueueDeadlineLeaderClusterActions.ReplayAsync(admin, worker, state, token);
            await QueueDeadlineLeaderClusterActions.PublishAsync(admin, state, true, token);
            await QueueDeadlineLeaderClusterDelivery.SettleAsync(admin, worker, state, true, token);
            await QueueDeadlineLeaderClusterActions.ReplayAsync(admin, worker, state, token);
            await QueueDeadlineLeaderClusterActions.StaleAsync(admin, state, token);
        }, token);
    }

    private static Task<bool> WithAsync(ClusterFixture fixture, QueueDeadlineLeaderClusterState state, string node,
        Func<RequestCqrsClusterCallers, RequestCqrsClusterCallers, Task> operation, CancellationToken token)
        => QueueDeadlineClusterCallers.UseAsync(fixture, node, fixture.AdminKey,
            admin => QueueDeadlineClusterCallers.UseAsync(fixture, node, state.Identity.Secret,
                async worker => { await operation(admin, worker); return true; }, token), token);
}

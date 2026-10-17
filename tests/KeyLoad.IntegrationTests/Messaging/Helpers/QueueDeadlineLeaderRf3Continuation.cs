using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadlineLeaderRf3Continuation
{
    internal static async Task LoseLeaderAsync(ClusterFixture fixture, QueueDeadlineLeaderRf3State state,
        QueueDeadlineLeaderRf3Ownership ownership, CancellationToken token)
    {
        await WithAsync(fixture, state, ownership.SurvivorNode, async (admin, _) =>
        {
            await QueueDeadlineLeaderRf3Current.ObserveAsync(admin, state, token);
            await Assert.That(TimeProvider.System.GetUtcNow() < state.Due).IsTrue();
        }, token);
        await fixture.KillContainerAsync(ownership.OriginalLeaderNode, QueueDeadlineLeaderRf3Protocol.Fault, token);
        await Assert.That(TimeProvider.System.GetUtcNow() < state.Due).IsTrue();
        await ownership.RequireReplacementAsync(fixture, token);
        await Assert.That(TimeProvider.System.GetUtcNow() < state.Due).IsTrue();
        await WithAsync(fixture, state, ownership.SurvivorNode, async (admin, worker) =>
        {
            await QueueDeadlineLeaderRf3Current.ObserveAsync(admin, state, token);
            await DueRecurringRf3Assertions.WaitForDueTimeAsync(state.Due, token);
            var ready = QueueDeadlineLeaderRf3Expected.Ready(false);
            await QueueDeadlineRf3Assertions.WaitReadyAsync(admin, state.Lane, ready, token);
            state.Original = ready;
            await QueueDeadlineLeaderRf3Current.ObserveAsync(admin, state, token);
            await QueueDeadlineLeaderRf3Actions.StaleAsync(admin, state, token);
            await QueueDeadlineLeaderRf3Actions.ReplayAsync(admin, worker, state, token);
        }, token);
    }

    internal static async Task RejoinAndColdAsync(ClusterFixture fixture, QueueDeadlineLeaderRf3State state,
        QueueDeadlineLeaderRf3Ownership ownership, CancellationToken token)
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
            await QueueDeadlineLeaderRf3Current.ObserveAsync(admin, state, token);
            await QueueDeadlineLeaderRf3Actions.StaleAsync(admin, state, token);
            await QueueDeadlineLeaderRf3Actions.ReplayAsync(admin, worker, state, token);
        }, token);
        await QueueLeaseRf3Cold.RestartAsync(fixture, token);
        await WithAsync(fixture, state, ownership.OriginalLeaderNode, async (admin, worker) =>
        {
            await QueueDeadlineLeaderRf3Current.ObserveAsync(admin, state, token);
            await QueueDeadlineLeaderRf3Actions.StaleAsync(admin, state, token);
            await QueueDeadlineLeaderRf3Delivery.SettleAsync(admin, worker, state, false, token);
            await QueueDeadlineLeaderRf3Actions.ReplayAsync(admin, worker, state, token);
            await QueueDeadlineLeaderRf3Actions.PublishAsync(admin, state, true, token);
            await QueueDeadlineLeaderRf3Delivery.SettleAsync(admin, worker, state, true, token);
            await QueueDeadlineLeaderRf3Actions.ReplayAsync(admin, worker, state, token);
            await QueueDeadlineLeaderRf3Actions.StaleAsync(admin, state, token);
        }, token);
    }

    private static Task<bool> WithAsync(ClusterFixture fixture, QueueDeadlineLeaderRf3State state, string node,
        Func<RequestCqrsRf3Callers, RequestCqrsRf3Callers, Task> operation, CancellationToken token)
        => QueueDeadlineRf3Callers.UseAsync(fixture, node, fixture.AdminKey,
            admin => QueueDeadlineRf3Callers.UseAsync(fixture, node, state.Identity.Secret,
                async worker => { await operation(admin, worker); return true; }, token), token);
}

using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueTerminalPolicyClusterFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await AdminAsync(fixture, async admin =>
        {
            var original = await QueueTerminalPolicyClusterSeed.CreateAsync(fixture, admin, token);
            await WorkerAsync(fixture, original, worker => InitialAsync(admin, worker, original, token), token);
            return original;
        }, token);
        await QueueTerminalPolicyClusterCold.RunAsync(fixture, token);
        _ = await AdminAsync(fixture, async admin =>
        {
            await WorkerAsync(fixture, state, async worker =>
            {
                await RetainedAsync(admin, worker, state, token);
                await QueueTerminalPolicyClusterSeed.PublishAsync(admin, worker, state, healthy: true, token);
                var delivery = await QueueTerminalPolicyClusterDelivery.ClaimAsync(admin, worker, state, 1, healthy: true, token);
                await QueueTerminalPolicyClusterDelivery.CompleteAsync(admin, worker, state, delivery, 1, healthy: true, token);
            }, token);
            return true;
        }, token);
        await QueueTerminalPolicyClusterCold.RunAsync(fixture, token);
        _ = await AdminAsync(fixture, async admin =>
        {
            await WorkerAsync(fixture, state, worker => RetainedAsync(admin, worker, state, token), token);
            return true;
        }, token);
    }

    private static async Task InitialAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueTerminalPolicyClusterState state, CancellationToken token)
    {
        await QueueTerminalPolicyClusterSeed.PublishAsync(admin, worker, state, healthy: false, token);
        var first = await QueueTerminalPolicyClusterDelivery.ClaimAsync(admin, worker, state, 1, healthy: false, token);
        await QueueTerminalPolicyClusterDelivery.CompleteAsync(admin, worker, state, first, 1, healthy: false, token);
        await QueueTerminalPolicyClusterPolicy.RefuseAsync(admin, worker, state, token);
        await QueueTerminalPolicyClusterPolicy.VerifyCurrentAsync(admin, worker, state, token);
        for (var attempt = 2; attempt <= QueueTerminalPolicyClusterProtocol.Attempts; attempt++)
        {
            await DueRecurringClusterAssertions.WaitForDueTimeAsync(state.Original.Metadata.NotBefore!.Value, token);
            var delivery = await QueueTerminalPolicyClusterDelivery.ClaimAsync(admin, worker, state, attempt, healthy: false, token);
            await QueueTerminalPolicyClusterDelivery.CompleteAsync(admin, worker, state, delivery, attempt, healthy: false, token);
        }
        await QueueTerminalPolicyClusterReplay.AllAsync(admin, worker, state, token);
        await QueueTerminalPolicyClusterPolicy.RefuseProducerAsync(admin, worker, state, token);
    }

    private static async Task RetainedAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueTerminalPolicyClusterState state, CancellationToken token)
    {
        await QueueTerminalPolicyClusterAssertions.CurrentAsync(admin, worker, state, token);
        await QueueTerminalPolicyClusterReplay.AllAsync(admin, worker, state, token);
        await QueueTerminalPolicyClusterPolicy.RefuseAsync(admin, worker, state, token);
        await QueueTerminalPolicyClusterPolicy.RefuseProducerAsync(admin, worker, state, token);
        await QueueTerminalPolicyClusterPolicy.VerifyCurrentAsync(admin, worker, state, token);
    }

    private static Task<T> AdminAsync<T>(ClusterFixture fixture, Func<RequestCqrsClusterCallers, Task<T>> operation,
        CancellationToken token) => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, operation, token);

    private static Task<bool> WorkerAsync(ClusterFixture fixture, QueueTerminalPolicyClusterState state,
        Func<RequestCqrsClusterCallers, Task> operation, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync<bool>(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret,
            async worker => { await operation(worker); return true; }, token);
}

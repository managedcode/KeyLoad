using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueParkedPolicyClusterFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await AdminAsync(fixture, async admin =>
        {
            var original = await QueueParkedPolicyClusterSeed.CreateAsync(fixture, admin, token);
            await WorkerAsync(fixture, original, worker => InitialAsync(admin, worker, original, token), token);
            return original;
        }, token);
        await QueueParkedPolicyClusterCold.RunAsync(fixture, token);
        _ = await AdminAsync(fixture, async admin =>
        {
            await WorkerAsync(fixture, state, worker => RetainedAsync(admin, worker, state, token), token);
            return true;
        }, token);
        await QueueParkedPolicyClusterCold.RunAsync(fixture, token);
        _ = await AdminAsync(fixture, async admin =>
        {
            await WorkerAsync(fixture, state, worker => RetainedAsync(admin, worker, state, token), token);
            return true;
        }, token);
    }

    private static async Task InitialAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueParkedPolicyClusterState state, CancellationToken token)
    {
        await QueueParkedPolicyClusterSeed.PublishAsync(admin, worker, state, fresh: false, token);
        await QueueParkedPolicyClusterPolicy.RefuseAsync(admin, worker, state, token);
        await QueueParkedPolicyClusterPolicy.VerifyCurrentAsync(admin, worker, state, token);
        await QueueParkedPolicyClusterDelivery.EmptyAsync(admin, worker, state, token);
        await QueueParkedPolicyClusterDelivery.CancelAsync(admin, worker, state, QueueParkedPolicyClusterProtocol.OriginalHead, token);
        var original = await QueueParkedPolicyClusterDelivery.ClaimAsync(admin, worker, state, QueueParkedPolicyClusterProtocol.OriginalSibling, token);
        await QueueParkedPolicyClusterDelivery.AckAsync(admin, worker, state, original, token);
        await QueueParkedPolicyClusterPolicy.RefuseAsync(admin, worker, state, token);
        await QueueParkedPolicyClusterPolicy.ChangeAsync(admin, worker, state, token);
        await QueueParkedPolicyClusterSeed.PublishAsync(admin, worker, state, fresh: true, token);
        var fresh = await QueueParkedPolicyClusterDelivery.ClaimAsync(admin, worker, state, QueueParkedPolicyClusterProtocol.FreshSibling, token);
        await QueueParkedPolicyClusterDelivery.AckAsync(admin, worker, state, fresh, token);
        await QueueParkedPolicyClusterDelivery.CancelAsync(admin, worker, state, QueueParkedPolicyClusterProtocol.FreshHead, token);
        await RetainedAsync(admin, worker, state, token);
    }

    private static async Task RetainedAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueParkedPolicyClusterState state, CancellationToken token)
    {
        await QueueParkedPolicyClusterAssertions.CurrentAsync(admin, worker, state, token);
        await QueueParkedPolicyClusterReplay.AllAsync(admin, worker, state, token);
        await QueueParkedPolicyClusterPolicy.RefuseAsync(admin, worker, state, token);
        await QueueParkedPolicyClusterPolicy.VerifyCurrentAsync(admin, worker, state, token);
    }

    private static Task<T> AdminAsync<T>(ClusterFixture fixture, Func<RequestCqrsClusterCallers, Task<T>> operation,
        CancellationToken token) => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, operation, token);

    private static Task<bool> WorkerAsync(ClusterFixture fixture, QueueParkedPolicyClusterState state,
        Func<RequestCqrsClusterCallers, Task> operation, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync<bool>(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret,
            async worker => { await operation(worker); return true; }, token);
}

using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueTerminalPolicyRf3Flow
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await AdminAsync(fixture, async admin =>
        {
            var original = await QueueTerminalPolicyRf3Seed.CreateAsync(fixture, admin, token);
            await WorkerAsync(fixture, original, worker => InitialAsync(admin, worker, original, token), token);
            return original;
        }, token);
        await QueueTerminalPolicyRf3Cold.RunAsync(fixture, token);
        _ = await AdminAsync(fixture, async admin =>
        {
            await WorkerAsync(fixture, state, async worker =>
            {
                await RetainedAsync(admin, worker, state, token);
                await QueueTerminalPolicyRf3Seed.PublishAsync(admin, worker, state, healthy: true, token);
                var delivery = await QueueTerminalPolicyRf3Delivery.ClaimAsync(admin, worker, state, 1, healthy: true, token);
                await QueueTerminalPolicyRf3Delivery.CompleteAsync(admin, worker, state, delivery, 1, healthy: true, token);
            }, token);
            return true;
        }, token);
        await QueueTerminalPolicyRf3Cold.RunAsync(fixture, token);
        _ = await AdminAsync(fixture, async admin =>
        {
            await WorkerAsync(fixture, state, worker => RetainedAsync(admin, worker, state, token), token);
            return true;
        }, token);
    }

    private static async Task InitialAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers worker,
        QueueTerminalPolicyRf3State state, CancellationToken token)
    {
        await QueueTerminalPolicyRf3Seed.PublishAsync(admin, worker, state, healthy: false, token);
        var first = await QueueTerminalPolicyRf3Delivery.ClaimAsync(admin, worker, state, 1, healthy: false, token);
        await QueueTerminalPolicyRf3Delivery.CompleteAsync(admin, worker, state, first, 1, healthy: false, token);
        await QueueTerminalPolicyRf3Policy.RefuseAsync(admin, worker, state, token);
        await QueueTerminalPolicyRf3Policy.VerifyCurrentAsync(admin, worker, state, token);
        for (var attempt = 2; attempt <= QueueTerminalPolicyRf3Protocol.Attempts; attempt++)
        {
            await DueRecurringRf3Assertions.WaitForDueTimeAsync(state.Original.Metadata.NotBefore!.Value, token);
            var delivery = await QueueTerminalPolicyRf3Delivery.ClaimAsync(admin, worker, state, attempt, healthy: false, token);
            await QueueTerminalPolicyRf3Delivery.CompleteAsync(admin, worker, state, delivery, attempt, healthy: false, token);
        }
        await QueueTerminalPolicyRf3Replay.AllAsync(admin, worker, state, token);
        await QueueTerminalPolicyRf3Policy.RefuseProducerAsync(admin, worker, state, token);
    }

    private static async Task RetainedAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers worker,
        QueueTerminalPolicyRf3State state, CancellationToken token)
    {
        await QueueTerminalPolicyRf3Assertions.CurrentAsync(admin, worker, state, token);
        await QueueTerminalPolicyRf3Replay.AllAsync(admin, worker, state, token);
        await QueueTerminalPolicyRf3Policy.RefuseAsync(admin, worker, state, token);
        await QueueTerminalPolicyRf3Policy.RefuseProducerAsync(admin, worker, state, token);
        await QueueTerminalPolicyRf3Policy.VerifyCurrentAsync(admin, worker, state, token);
    }

    private static Task<T> AdminAsync<T>(ClusterFixture fixture, Func<RequestCqrsRf3Callers, Task<T>> operation,
        CancellationToken token) => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, operation, token);

    private static Task<bool> WorkerAsync(ClusterFixture fixture, QueueTerminalPolicyRf3State state,
        Func<RequestCqrsRf3Callers, Task> operation, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync<bool>(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret,
            async worker => { await operation(worker); return true; }, token);
}

using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueParkedPolicyRf3Flow
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await AdminAsync(fixture, async admin =>
        {
            var original = await QueueParkedPolicyRf3Seed.CreateAsync(fixture, admin, token);
            await WorkerAsync(fixture, original, worker => InitialAsync(admin, worker, original, token), token);
            return original;
        }, token);
        await QueueParkedPolicyRf3Cold.RunAsync(fixture, token);
        _ = await AdminAsync(fixture, async admin =>
        {
            await WorkerAsync(fixture, state, worker => RetainedAsync(admin, worker, state, token), token);
            return true;
        }, token);
        await QueueParkedPolicyRf3Cold.RunAsync(fixture, token);
        _ = await AdminAsync(fixture, async admin =>
        {
            await WorkerAsync(fixture, state, worker => RetainedAsync(admin, worker, state, token), token);
            return true;
        }, token);
    }

    private static async Task InitialAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers worker,
        QueueParkedPolicyRf3State state, CancellationToken token)
    {
        await QueueParkedPolicyRf3Seed.PublishAsync(admin, worker, state, fresh: false, token);
        await QueueParkedPolicyRf3Policy.RefuseAsync(admin, worker, state, token);
        await QueueParkedPolicyRf3Policy.VerifyCurrentAsync(admin, worker, state, token);
        await QueueParkedPolicyRf3Delivery.EmptyAsync(admin, worker, state, token);
        await QueueParkedPolicyRf3Delivery.CancelAsync(admin, worker, state, QueueParkedPolicyRf3Protocol.OriginalHead, token);
        var original = await QueueParkedPolicyRf3Delivery.ClaimAsync(admin, worker, state, QueueParkedPolicyRf3Protocol.OriginalSibling, token);
        await QueueParkedPolicyRf3Delivery.AckAsync(admin, worker, state, original, token);
        await QueueParkedPolicyRf3Policy.RefuseAsync(admin, worker, state, token);
        await QueueParkedPolicyRf3Policy.ChangeAsync(admin, worker, state, token);
        await QueueParkedPolicyRf3Seed.PublishAsync(admin, worker, state, fresh: true, token);
        var fresh = await QueueParkedPolicyRf3Delivery.ClaimAsync(admin, worker, state, QueueParkedPolicyRf3Protocol.FreshSibling, token);
        await QueueParkedPolicyRf3Delivery.AckAsync(admin, worker, state, fresh, token);
        await QueueParkedPolicyRf3Delivery.CancelAsync(admin, worker, state, QueueParkedPolicyRf3Protocol.FreshHead, token);
        await RetainedAsync(admin, worker, state, token);
    }

    private static async Task RetainedAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers worker,
        QueueParkedPolicyRf3State state, CancellationToken token)
    {
        await QueueParkedPolicyRf3Assertions.CurrentAsync(admin, worker, state, token);
        await QueueParkedPolicyRf3Replay.AllAsync(admin, worker, state, token);
        await QueueParkedPolicyRf3Policy.RefuseAsync(admin, worker, state, token);
        await QueueParkedPolicyRf3Policy.VerifyCurrentAsync(admin, worker, state, token);
    }

    private static Task<T> AdminAsync<T>(ClusterFixture fixture, Func<RequestCqrsRf3Callers, Task<T>> operation,
        CancellationToken token) => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, operation, token);

    private static Task<bool> WorkerAsync(ClusterFixture fixture, QueueParkedPolicyRf3State state,
        Func<RequestCqrsRf3Callers, Task> operation, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync<bool>(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret,
            async worker => { await operation(worker); return true; }, token);
}

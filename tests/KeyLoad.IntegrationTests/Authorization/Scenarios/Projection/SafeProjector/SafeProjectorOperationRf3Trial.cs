using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SafeProjectorOperationRf3Trial
{
    internal static async Task RunAsync(ClusterFixture fixture)
    {
        using var deadline = McpCallerDeadline.Create();
        var suffix = Guid.NewGuid().ToString(SafeProjectorOperationRf3Protocol.GuidFormat);
        var partition = new PartitionRef(SafeProjectorOperationRf3Protocol.TenantPrefix + suffix,
            SafeProjectorOperationRf3Protocol.Database, SafeProjectorOperationRf3Protocol.TransactionDomain, suffix);
        var history = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            var state = new SafeProjectorOperationRf3History(await SafeProjectorOperationRf3Seed.CreateAsync(root, partition, deadline.Token));
            await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, state.State.Key, async reader =>
            {
                await SafeProjectorOperationRf3Initial.RunAsync(root, reader, state, deadline.Token);
                await SafeProjectorOperationRf3Continuation.RunAsync(root, reader, state, deadline.Token);
                return true;
            }, deadline.Token);
            return state;
        }, deadline.Token);
        await SafeProjectorOperationRf3Cold.RunAsync(fixture, history, deadline.Token);
    }
}

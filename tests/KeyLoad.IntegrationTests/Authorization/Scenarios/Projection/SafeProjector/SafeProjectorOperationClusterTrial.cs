using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class SafeProjectorOperationClusterTrial
{
    internal static async Task RunAsync(ClusterFixture fixture)
    {
        using var deadline = McpCallerDeadline.Create();
        var suffix = Guid.NewGuid().ToString(SafeProjectorOperationClusterProtocol.GuidFormat);
        var partition = new PartitionRef(SafeProjectorOperationClusterProtocol.TenantPrefix + suffix,
            SafeProjectorOperationClusterProtocol.Database, SafeProjectorOperationClusterProtocol.TransactionDomain, suffix);
        var history = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            var state = new SafeProjectorOperationClusterHistory(await SafeProjectorOperationClusterSeed.CreateAsync(root, partition, deadline.Token));
            await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, state.State.Key, async reader =>
            {
                await SafeProjectorOperationClusterInitial.RunAsync(root, reader, state, deadline.Token);
                await SafeProjectorOperationClusterContinuation.RunAsync(root, reader, state, deadline.Token);
                return true;
            }, deadline.Token);
            return state;
        }, deadline.Token);
        await SafeProjectorOperationClusterCold.RunAsync(fixture, history, deadline.Token);
    }
}

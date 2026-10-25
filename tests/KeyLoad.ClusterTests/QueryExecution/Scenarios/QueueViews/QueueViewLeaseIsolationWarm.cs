using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class QueueViewLeaseIsolationWarm
{
    internal static Task<bool> RunAsync(ClusterFixture fixture, QueueViewLeaseIsolationState state, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, root =>
            RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Reader.Secret, async reader =>
            {
                await QueueViewLeaseIsolationSeed.ClaimAsync(root, state, token);
                await QueueViewLeaseIsolationReads.RunAsync(root, reader, state, token);
                await QueueViewLeaseIsolationRefusals.InputsAsync(root, reader, state, token);
                await QueueViewLeaseIsolationCancellation.RunAsync(root, reader, state, token);
                await QueueViewLeaseIsolationPolicy.RunAsync(root, reader, state, token);
                await QueueViewLeaseIsolationDelivery.ActiveReplayAsync(root, state, token);
                await QueueViewLeaseIsolationDelivery.RenewAsync(root, state, token);
                await QueueViewLeaseIsolationReads.RunAsync(root, reader, state, token);
                await QueueViewLeaseIsolationDelivery.AckAsync(root, state, token);
                await QueueViewLeaseIsolationHealthy.RunAsync(root, state, token);
                await QueueViewLeaseIsolationReads.RunAsync(root, reader, state, token);
                await QueueViewLeaseIsolationHistory.RunAsync(root, state, token);
                return true;
            }, token), token);
}

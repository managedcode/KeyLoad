using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class CompetingInboxPublicStatus
{
    private const string MissingStatus = "The original node status observation is unavailable.";
    internal static async Task RequireAsync(CompetingInboxPublicState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        var nodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in CompetingInboxPublicProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                _ = await RequestCqrsClusterCallers.RunOwnedAsync(state.Fixture.App, node, state.Fixture.AdminKey, async caller =>
                {
                    var status = await ReadConvergedAsync(state, caller, node, token);
                    await Assert.That(Guid.TryParse(status.NodeId, out var id)).IsTrue();
                    await Assert.That(id).IsNotEqualTo(Guid.Empty);
                    await Assert.That(nodes.Add(status.NodeId)).IsTrue();
                    await Assert.That(status.ProcessId).IsGreaterThan(0);
                    await Assert.That(status.SiloCount).IsEqualTo(CompetingInboxPublicProtocol.Nodes.Length);
                    await Assert.That(status.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
                    await Assert.That(status.RoutingReady).IsTrue();
                    await Assert.That(status.ReadGeneration).IsGreaterThanOrEqualTo(0L);
                    if (state.Statuses.TryGetValue(node, out var prior))
                    {
                        await Assert.That(status.NodeId).IsEqualTo(prior.NodeId);
                        await Assert.That(status.Position).IsGreaterThanOrEqualTo(prior.Position);
                        await Assert.That(status.ReadGeneration).IsGreaterThanOrEqualTo(prior.ReadGeneration);
                    }
                    state.Statuses[node] = status;
                    return true;
                }, token);
            }, failures);
        }
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(nodes.Count).IsEqualTo(
            CompetingInboxPublicProtocol.Nodes.Length), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task<NodeStatus> ReadConvergedAsync(CompetingInboxPublicState state,
        RequestCqrsClusterCallers caller, string node, CancellationToken token)
    {
        var minimum = state.Statuses.TryGetValue(node, out var prior) ? prior.Position : 0L;
        NodeStatus? observed = null;
        await ClusterReplicationTestSupport.EventuallyAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            var current = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
            if (current.Position < minimum || !current.RoutingReady)
            {
                return false;
            }
            observed = current;
            return true;
        }, token);
        return observed ?? throw new InvalidOperationException(MissingStatus);
    }

}

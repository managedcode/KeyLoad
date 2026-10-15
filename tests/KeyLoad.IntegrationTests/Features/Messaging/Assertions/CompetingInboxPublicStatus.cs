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
                _ = await RequestCqrsRf3Callers.RunOwnedAsync(state.Fixture.App, node, state.Fixture.AdminKey, async caller =>
                {
                    var status = await ReadConvergedAsync(state, caller, node, token);
                    await Assert.That(Guid.TryParse(status.NodeId, out var id)).IsTrue();
                    await Assert.That(id).IsNotEqualTo(Guid.Empty);
                    await Assert.That(nodes.Add(status.NodeId)).IsTrue();
                    await Assert.That(status.ProcessId).IsGreaterThan(0);
                    await Assert.That(status.Incarnation).IsEqualTo(state.Placement.Incarnation);
                    await Assert.That(status.Voters).IsEqualTo(CompetingInboxPublicProtocol.Nodes.Length);
                    await Assert.That(status.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
                    await Assert.That(status.RoutingReady).IsTrue();
                    await Assert.That(status.ConsensusTerm).IsGreaterThan(0L);
                    await Assert.That(status.ReadGeneration).IsGreaterThanOrEqualTo(0L);
                    await Assert.That(status.Applied).IsGreaterThanOrEqualTo(state.Producers.Select(value => value.Receipt.Token.Position)
                        .Concat(state.Current.Select(value => value.Receipt.Token.Position)).Append(state.Winner.Token.Position).Max());
                    await Assert.That(CompetingInboxPublicProtocol.Nodes.Contains(new Uri(status.Leader!).Host, StringComparer.Ordinal)).IsTrue();
                    if (state.Statuses.TryGetValue(node, out var prior))
                    {
                        await Assert.That(status.NodeId).IsEqualTo(prior.NodeId);
                        await Assert.That(status.Applied).IsGreaterThanOrEqualTo(prior.Applied);
                        await Assert.That(status.ReadGeneration).IsGreaterThanOrEqualTo(prior.ReadGeneration);
                        await Assert.That(status.ConsensusTerm).IsGreaterThanOrEqualTo(prior.ConsensusTerm);
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
        RequestCqrsRf3Callers caller, string node, CancellationToken token)
    {
        var minimum = state.Producers.Select(value => value.Receipt.Token.Position)
            .Concat(state.Current.Select(value => value.Receipt.Token.Position)).Append(state.Winner.Token.Position).Max();
        if (state.Statuses.TryGetValue(node, out var prior))
        {
            minimum = Math.Max(minimum, prior.Applied);
        }
        NodeStatus? observed = null;
        await ClusterReplicationTestSupport.EventuallyAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            var current = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
            if (current.Applied < minimum || !current.RoutingReady)
            {
                return false;
            }
            observed = current;
            return true;
        }, token);
        return observed ?? throw new InvalidOperationException(MissingStatus);
    }

}

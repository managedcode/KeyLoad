using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class MetadataBootstrapRf3Authority
{
    internal static async Task ValidateAsync(MetadataBootstrapRf3State state, NodeStatus status)
    {
        await Assert.That(Guid.TryParse(status.NodeId, out var nodeId)).IsTrue();
        await Assert.That(nodeId).IsNotEqualTo(Guid.Empty);
        await Assert.That(status.Incarnation).IsEqualTo(state.Owner.Incarnation);
        await Assert.That(status.Voters).IsEqualTo(MetadataBootstrapRf3State.Nodes.Length);
        await Assert.That(status.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(status.RoutingReady).IsTrue();
        await Assert.That(status.ProcessId).IsGreaterThan(0);
        await Assert.That(status.ReadGeneration).IsGreaterThanOrEqualTo(0L);
        await Assert.That(status.ConsensusTerm).IsGreaterThan(0L);
        await Assert.That(status.Applied).IsGreaterThanOrEqualTo(state.Commands.Max(command => command.Receipt.Token.Position));
        await Assert.That(MetadataBootstrapRf3State.Nodes.Contains(new Uri(status.Leader!).Host, StringComparer.Ordinal)).IsTrue();
    }
    internal static async Task<string> ObserveAsync(MetadataBootstrapRf3State state, string lost, CancellationToken token)
    {
        var survivors = MetadataBootstrapRf3State.Nodes.Where(node => node != lost).ToArray();
        var attempt = 0;
        var observations = state.AuthorityObservations;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var node = survivors[attempt];
            attempt = (attempt + 1) % survivors.Length;
            using var http = McpCallerHttp.Create(state.Fixture, node);
            var client = new KeyLoadClient(http, state.Fixture.AdminKey, IntegrationClientOptions.Execution());
            var actual = await client.StatusAsync(token);
            if (observations.Count == 32)
            {
                observations.Dequeue();
            }
            observations.Enqueue(node + ":" + actual.Problem?.ErrorCode + ":" + actual.Value?.ConsensusTerm);
            if (!actual.IsSuccess)
            {
                await MetadataBootstrapRf3Calls.EqualAsync(Errors.Problem(ErrorCode.OwnershipLost,
                    "The cluster has no current leader with a reachable majority."), actual.Problem);
                continue;
            }
            var status = actual.Value ?? throw new InvalidOperationException("The actual native authority status is absent.");
            var leader = status.Leader is null ? null : new Uri(status.Leader).Host;
            if (leader is not null && survivors.Contains(leader, StringComparer.Ordinal)
                && status.ConsensusTerm > state.BeforeLoss.ConsensusTerm && status.RoutingReady)
            {
                await ValidateAsync(state, status);
                state.AfterLoss = status;
                return leader;
            }
        }
    }
}

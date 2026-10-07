using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Replication;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GraphSearchRf3SurvivorReadiness
{
    internal static async Task<int> WaitAsync(GraphSearchRf3Scenario scenario, KeyLoadClient[] administrators,
        string[] nodes, int stopped, CancellationToken token)
    {
        var survivors = Enumerable.Range(0, nodes.Length).Where(index => index != stopped).ToArray();
        var selected = -1;
        await ClusterReplicationTestSupport.EventuallyAsync(async () =>
        {
            var statuses = new List<NodeStatus>();
            foreach (var index in survivors)
            { statuses.Add(await McpCallerAssertions.SdkSuccessAsync(await administrators[index].StatusAsync(token))); }
            var leader = statuses[0].Leader;
            if (statuses.Any(status => !status.RoutingReady || status.Voters != nodes.Length
                || string.IsNullOrWhiteSpace(status.Leader) || !StringComparer.Ordinal.Equals(status.Leader, leader)))
            { return false; }
            if (!Uri.TryCreate(leader, UriKind.Absolute, out var address))
            { throw new InvalidOperationException("The agreed native replica leader address was invalid."); }
            var indexOfLeader = survivors.SingleOrDefault(index => StringComparer.Ordinal.Equals(address.Host, nodes[index]), -1);
            if (indexOfLeader < 0)
            { return false; }
            var probe = await administrators[indexOfLeader].GetAsync(
                new(scenario.Partition, GraphSearchRf3Scenario.Projects, GraphSearchRf3Scenario.SecondSeed), token);
            if (!probe.IsSuccess && probe.Problem?.ErrorCode == nameof(ErrorCode.OwnershipLost)
                && probe.Problem.Detail == ReplicaProtocol.NoLeader)
            { return false; }
            var document = await McpCallerAssertions.SdkSuccessAsync(probe);
            await Assert.That(document).IsNotNull();
            await Assert.That(document!.Reference).IsEqualTo(scenario.Vertex(GraphSearchRf3Scenario.Projects, GraphSearchRf3Scenario.SecondSeed));
            await Assert.That(document.Revision).IsEqualTo(1L);
            await Assert.That(document.Json).IsEqualTo("""{"name":"second"}""");
            await Assert.That(document.Redacted).IsFalse();
            await Assert.That(document.RedactedFields).IsEmpty();
            selected = indexOfLeader;
            return true;
        }, token);
        return selected;
    }
}

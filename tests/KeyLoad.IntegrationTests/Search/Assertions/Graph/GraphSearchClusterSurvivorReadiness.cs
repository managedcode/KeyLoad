using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.StorageReplication;

namespace KeyLoad.IntegrationTests.Search;

internal static class GraphSearchClusterSurvivorReadiness
{
    internal static async Task<int> WaitAsync(GraphSearchClusterScenario scenario, KeyLoadClient[] administrators,
        string[] nodes, int stopped, CancellationToken token)
    {
        var survivors = Enumerable.Range(0, nodes.Length).Where(index => index != stopped).ToArray();
        var selected = -1;
        await ClusterReplicationTestSupport.EventuallyAsync(async () =>
        {
            var statuses = new List<NodeStatus>();
            foreach (var index in survivors)
            {
                statuses.Add(await McpCallerAssertions.SdkSuccessAsync(await administrators[index].StatusAsync(token)));
            }
            if (statuses.Any(status => !status.RoutingReady || status.SiloCount != nodes.Length
                || status.ReplicaCount != nodes.Length))
            {
                return false;
            }
            var selectedIndex = survivors[0];
            var probe = await administrators[selectedIndex].GetAsync(
                new(scenario.Partition, GraphSearchClusterScenario.Projects, GraphSearchClusterScenario.SecondSeed), token);
            if (probe.IsFailed && probe.Problem.ErrorCode == nameof(ErrorCode.OwnershipLost))
            {
                return false;
            }
            var document = await PublicReadAssertions.DocumentAsync(probe);
            await Assert.That(document).IsNotNull();
            await Assert.That(document!.Reference).IsEqualTo(scenario.StoredVertex(GraphSearchClusterScenario.Projects, GraphSearchClusterScenario.SecondSeed));
            await Assert.That(document.Revision).IsEqualTo(1L);
            await Assert.That(document.Json).IsEqualTo("""{"name":"second"}""");
            await Assert.That(document.Redacted).IsFalse();
            await Assert.That(document.RedactedFields).IsEmpty();
            selected = selectedIndex;
            return true;
        }, token);
        return selected;
    }
}

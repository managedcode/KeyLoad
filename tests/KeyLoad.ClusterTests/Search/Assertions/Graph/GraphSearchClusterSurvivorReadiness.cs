using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.StorageReplication;

namespace KeyLoad.ClusterTests.Search;

internal static class GraphSearchClusterSurvivorReadiness
{
    internal static async Task<int> WaitAsync(GraphSearchClusterScenario scenario, KeyLoadClient[] administrators,
        string[] nodes, int stopped, CancellationToken token)
    {
        var survivors = Enumerable.Range(0, nodes.Length).Where(index => index != stopped).ToArray();
        // NodeStatus.SiloCount is the active Orleans membership: the stopped silo must leave it on every survivor.
        var statuses = await ClusterMembershipReadiness.WaitForActiveSilosAsync(
            [.. survivors.Select(index => administrators[index])], token);
        await Assert.That(statuses.All(status => status.ReplicaCount == nodes.Length)).IsTrue();
        var selected = -1;
        await ClusterReplicationTestSupport.EventuallyAsync(async () =>
        {
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

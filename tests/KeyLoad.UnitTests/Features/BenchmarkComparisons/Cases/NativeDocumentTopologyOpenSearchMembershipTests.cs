using System.Text.Json;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-003 checks exact native connected-node and shard-placement response sets.</summary>
internal sealed class NativeDocumentTopologyOpenSearchMembershipTests
{
    private const string Index = "controlled-index";
    private const string NodePrefix = "node-";

    [Test]
    public async Task AC_ISO_003_AcceptsExactTwoNativeNodesAndRejectsMissingOrExtraCopies()
    {
        var state = State(2);
        var nodes = Nodes(2);
        var observation = OpenSearchReplicaProof.VerifyMembershipAndPlacement(state, nodes, Index, 2, 2);
        await Assert.That(observation.NodeIds.Length).IsEqualTo(2);
        await Assert.That(observation.Placements.Length).IsEqualTo(2);
        await Assert.That(observation.DataNodes).IsEqualTo(2);
        Assert.ThrowsExactly<ComparisonFailureException>(() => OpenSearchReplicaProof.VerifyMembershipAndPlacement(State(3), nodes, Index, 2, 2));
        Assert.ThrowsExactly<ComparisonFailureException>(() => OpenSearchReplicaProof.VerifyMembershipAndPlacement(state, Nodes(3), Index, 2, 2));
        Assert.ThrowsExactly<ComparisonFailureException>(() => OpenSearchReplicaProof.VerifyMembershipAndPlacement(state, nodes, Index, 2, 3));
    }

    private static JsonElement State(int count)
        => JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            [OpenSearchNames.ClusterManagerNode] = NodePrefix + 0,
            [OpenSearchNames.Nodes] = Enumerable.Range(0, count).ToDictionary(index => NodePrefix + index, _ => new object()),
            [OpenSearchNames.RoutingTable] = new Dictionary<string, object>
            {
                [OpenSearchNames.Indices] = new Dictionary<string, object>
                {
                    [Index] = new Dictionary<string, object>
                    {
                        [OpenSearchNames.Shards] = new Dictionary<string, object>
                        {
                            [OpenSearchNames.ShardZero] = Enumerable.Range(0, count).Select(index => new Dictionary<string, object>
                            {
                                [OpenSearchNames.Node] = NodePrefix + index,
                                [OpenSearchNames.Primary] = index == 0,
                                [OpenSearchNames.State] = OpenSearchNames.Started,
                            }).ToArray(),
                        },
                    },
                },
            },
        });

    private static JsonElement Nodes(int count)
        => JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            [OpenSearchNames.NodesMetadata] = new Dictionary<string, int>
            {
                [OpenSearchNames.Total] = count,
                [OpenSearchNames.Successful] = count,
                [OpenSearchNames.Failed] = 0,
            },
            [OpenSearchNames.Nodes] = Enumerable.Range(0, count).ToDictionary(index => NodePrefix + index, _ => new Dictionary<string, object>
            {
                [OpenSearchNames.Version] = OpenSearchNames.ExpectedVersion,
                [OpenSearchNames.Roles] = new[] { OpenSearchNames.DataRole, OpenSearchNames.ClusterManagerRole },
            }),
        });
}

using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class OpenSearchReplicaProof
{
    private sealed record ShardPlacement(string Node, bool Primary, string State);

    internal static (string[] NodeIds, string[] Placements, int DataNodes, string ClusterManager)
        VerifyMembershipAndPlacement(JsonElement state, JsonElement nodesInfo, string index,
            int expectedNodes, int expectedCopies)
    {
        var manager = OpenSearchJson.RequiredString(state, OpenSearchNames.ClusterManagerNode);
        var stateNodeIds = ReadStateNodeIds(state, expectedNodes);
        var nodeInfoNodes = ReadNodeInfoNodes(nodesInfo, expectedNodes);
        var nodeInfoNodeIds = nodeInfoNodes.EnumerateObject().Select(node => node.Name)
            .ToHashSet(StringComparer.Ordinal);
        VerifyNodeMembership(stateNodeIds, nodeInfoNodeIds, expectedNodes);
        var dataNodes = CountDataNodes(nodeInfoNodes, expectedNodes);
        var placements = ReadPlacements(state, index);
        VerifyPlacements(placements, expectedCopies, stateNodeIds, nodeInfoNodeIds);
        VerifyClusterManager(manager, stateNodeIds, nodeInfoNodeIds);
        var labels = placements.Select(item => item.Node +
            (item.Primary ? OpenSearchNames.PrimaryCopyLabel : OpenSearchNames.ReplicaCopyLabel)).ToArray();
        return (nodeInfoNodeIds.ToArray(), labels, dataNodes, manager);
    }

    private static HashSet<string> ReadStateNodeIds(JsonElement state, int expectedNodes)
    {
        var stateNodes = OpenSearchJson.RequiredObject(state, OpenSearchNames.Nodes);
        var ids = stateNodes.EnumerateObject().Select(node => node.Name).ToHashSet(StringComparer.Ordinal);
        if (ids.Count != expectedNodes)
        {
            throw new ComparisonFailureException(OpenSearchNames.MissingNodeCount);
        }
        return ids;
    }

    private static JsonElement ReadNodeInfoNodes(JsonElement nodesInfo, int expectedNodes)
    {
        var counts = OpenSearchJson.RequiredObject(nodesInfo, OpenSearchNames.NodesMetadata);
        if (OpenSearchJson.RequiredInt32(counts, OpenSearchNames.Total) != expectedNodes ||
            OpenSearchJson.RequiredInt32(counts, OpenSearchNames.Successful) != expectedNodes ||
            OpenSearchJson.RequiredInt32(counts, OpenSearchNames.Failed) != OpenSearchNames.NoFailures)
        {
            throw new ComparisonFailureException(OpenSearchNames.MissingNodeCount);
        }

        return OpenSearchJson.RequiredObject(nodesInfo, OpenSearchNames.Nodes);
    }

    private static void VerifyNodeMembership(HashSet<string> stateNodeIds, HashSet<string> nodeInfoNodeIds,
        int expectedNodes)
    {
        if (nodeInfoNodeIds.Count != expectedNodes || !stateNodeIds.SetEquals(nodeInfoNodeIds))
        {
            throw new ComparisonFailureException(OpenSearchNames.NodeMembershipMismatch);
        }
    }

    private static int CountDataNodes(JsonElement nodeInfoNodes, int expectedNodes)
    {
        const int FirstElementIndex = 0;

        var count = FirstElementIndex;
        foreach (var node in nodeInfoNodes.EnumerateObject())
        {
            VerifyNode(node.Value);
            count++;
        }

        if (count != expectedNodes)
        {
            throw new ComparisonFailureException(OpenSearchNames.MissingDataNodes);
        }
        return count;
    }

    private static void VerifyNode(JsonElement node)
    {
        if (OpenSearchJson.RequiredString(node, OpenSearchNames.Version) != OpenSearchNames.ExpectedVersion)
        {
            throw new ComparisonFailureException(OpenSearchNames.ServerVersionMismatch);
        }

        var roles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in OpenSearchJson.RequiredArray(node, OpenSearchNames.Roles).EnumerateArray())
        {
            if (role.ValueKind != JsonValueKind.String)
            {
                throw new ComparisonFailureException(OpenSearchNames.ExpectedString);
            }
            roles.Add(role.GetString()!);
        }

        if (!roles.Contains(OpenSearchNames.DataRole) || !roles.Contains(OpenSearchNames.ClusterManagerRole))
        {
            throw new ComparisonFailureException(OpenSearchNames.MissingDataNodes);
        }
    }

    private static ShardPlacement[] ReadPlacements(JsonElement state, string index)
        => OpenSearchJson.RequiredPath(state, OpenSearchNames.RoutingTable, OpenSearchNames.Indices, index,
            OpenSearchNames.Shards).GetProperty(OpenSearchNames.ShardZero).EnumerateArray().Select(ReadPlacement).ToArray();

    private static ShardPlacement ReadPlacement(JsonElement shard)
        => new(OpenSearchJson.RequiredString(shard, OpenSearchNames.Node),
            OpenSearchJson.RequiredBoolean(shard, OpenSearchNames.Primary), OpenSearchJson.RequiredString(shard, OpenSearchNames.State));

    private static void VerifyPlacements(ShardPlacement[] placements, int expectedCopies,
        HashSet<string> stateNodeIds, HashSet<string> nodeInfoNodeIds)
    {
        var placementNodeIds = placements.Select(item => item.Node).ToHashSet(StringComparer.Ordinal);
        if (placements.Length != expectedCopies || placementNodeIds.Count != expectedCopies ||
            !stateNodeIds.SetEquals(placementNodeIds) || !nodeInfoNodeIds.SetEquals(placementNodeIds) ||
            placements.Count(item => item.Primary) != OpenSearchNames.PrimaryShardCount ||
            placements.Any(item => item.State != OpenSearchNames.Started))
        {
            throw new ComparisonFailureException(OpenSearchNames.ShardPlacementMismatch);
        }
    }

    private static void VerifyClusterManager(string manager, HashSet<string> stateNodeIds,
        HashSet<string> nodeInfoNodeIds)
    {
        if (!stateNodeIds.Contains(manager) || !nodeInfoNodeIds.Contains(manager))
        {
            throw new ComparisonFailureException(OpenSearchNames.MissingClusterManager);
        }
    }
}

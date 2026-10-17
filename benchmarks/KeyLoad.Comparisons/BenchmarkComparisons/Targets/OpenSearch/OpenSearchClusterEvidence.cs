using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal sealed record OpenSearchClusterObservation(string Status, string[] NodeIds, string[] ShardPlacements,
    int DataNodes, int ActiveCopies, string ClusterManager)
{
    internal ClusterEvidence ToClusterEvidence(string version, OpenSearchClusterObservation beforeSeed, string settings, int expectedCopies)
        => new(NodeIds.Length, ActiveCopies, Status,
        [
            OpenSearchNames.EvidenceVersion + version,
            OpenSearchNames.EvidenceDataNodes + DataNodes,
            OpenSearchNames.EvidenceClusterManager + ClusterManager,
            OpenSearchNames.EvidenceNodeIds + string.Join(OpenSearchNames.CommaSeparator, NodeIds.Order(StringComparer.Ordinal)),
            OpenSearchNames.EvidencePlacements + string.Join(OpenSearchNames.CommaSeparator, ShardPlacements.Order(StringComparer.Ordinal)),
            OpenSearchNames.EvidenceCopies + ActiveCopies + OpenSearchNames.CopyCountSeparator + expectedCopies,
            OpenSearchNames.EvidenceSettings + settings,
            OpenSearchNames.EvidencePreSeed + string.Join(OpenSearchNames.CommaSeparator, beforeSeed.ShardPlacements.Order(StringComparer.Ordinal)),
            OpenSearchNames.EvidenceProbe
        ]);
}

internal static class OpenSearchClusterEvidence
{

    internal static string BuildHealthPath(string index, IOptions<NativeComparisonExecutionOptions> nativeExecutionOptions)
    {
        var executionOptions = NativeComparisonExecutionOptions.Require(nativeExecutionOptions).Value;
        return OpenSearchNames.ClusterHealthPath + index + OpenSearchNames.HealthWaitParametersPrefix
            + executionOptions.OpenSearchHealthWaitTimeoutSeconds.ToString(CultureInfo.InvariantCulture)
            + OpenSearchNames.HealthWaitParametersSuffix;
    }

    internal static async Task<OpenSearchClusterObservation> ObserveAsync(HttpClient client, string index, int expectedCopies,
        ComparisonTopology topology, IOptions<NativeComparisonExecutionOptions> nativeExecutionOptions, CancellationToken cancellationToken)
    {
        using var healthResponse = await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Get,
            BuildHealthPath(index, nativeExecutionOptions), null, cancellationToken);
        using var stateResponse = await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Get,
            OpenSearchNames.ClusterStatePath + index, null, cancellationToken);
        using var nodesInfoResponse = await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Get,
            OpenSearchNames.NodesInfoPath, null, cancellationToken);
        var health = healthResponse.RootElement;
        var state = stateResponse.RootElement;
        var nodesInfo = nodesInfoResponse.RootElement;
        var expectedNodes = ComparisonTopologies.NodeCount(topology);
        VerifyHealth(health, expectedNodes, expectedCopies);
        var observation = OpenSearchReplicaProof.VerifyMembershipAndPlacement(state, nodesInfo, index,
            expectedNodes, expectedCopies);
        return new(OpenSearchNames.Green, observation.NodeIds, observation.Placements, observation.DataNodes,
            observation.Placements.Length, observation.ClusterManager);
    }

    private static void VerifyHealth(JsonElement health, int expectedNodes, int expectedCopies)
    {
        if (OpenSearchJson.RequiredBoolean(health, OpenSearchNames.SearchTimedOut)
            || OpenSearchJson.RequiredString(health, OpenSearchNames.HealthStatus) != OpenSearchNames.Green
            || OpenSearchJson.RequiredInt32(health, OpenSearchNames.NumberOfNodes) != expectedNodes)
        {
            throw new ComparisonFailureException(OpenSearchNames.ClusterNotGreen);
        }

        if (OpenSearchJson.RequiredInt32(health, OpenSearchNames.NumberOfDataNodes) != expectedNodes)
        {
            throw new ComparisonFailureException(OpenSearchNames.MissingDataNodes);
        }

        if (OpenSearchJson.RequiredInt32(health, OpenSearchNames.ActivePrimaryShards) != OpenSearchNames.PrimaryShardCount
            || OpenSearchJson.RequiredInt32(health, OpenSearchNames.ActiveShards) != expectedCopies
            || OpenSearchJson.RequiredInt32(health, OpenSearchNames.UnassignedShards) != OpenSearchNames.NoUnassignedShards)
        {
            throw new ComparisonFailureException(OpenSearchNames.ClusterNotGreen);
        }
    }

}

using System.Collections.Immutable;

namespace KeyLoad.Comparisons.BenchmarkComparisons.TimeSeries.Intensive;

internal static class KeyLoadTimeSeriesIntensiveTopology
{
    internal static void ValidateStatuses(ImmutableArray<NodeStatus> statuses,
        ImmutableArray<string> expectedSilos, Guid expectedIncarnation)
    {
        if (statuses.IsDefaultOrEmpty || expectedSilos.IsDefaultOrEmpty
            || statuses.Length != expectedSilos.Length
            || statuses.Length is not (KeyLoadTimeSeriesIntensiveProtocol.MinimumNodeCount
                or KeyLoadTimeSeriesIntensiveProtocol.MaximumNodeCount)
            || expectedSilos.Distinct(StringComparer.Ordinal).Count() != expectedSilos.Length
            || expectedSilos.Any(string.IsNullOrWhiteSpace))
        {
            throw Invalid();
        }
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var status in statuses)
        {
            if (status is null || string.IsNullOrWhiteSpace(status.NodeId) || !nodeIds.Add(status.NodeId)
                || status.Incarnation != expectedIncarnation || status.SiloCount != statuses.Length
                || status.ReplicaCount != statuses.Length || !status.RoutingReady
                || status.Durability != DurabilityProfile.ProcessDurable)
            {
                throw Invalid();
            }
        }
    }

    internal static void ValidateDashboard(AdminNodeSnapshot snapshot, string expectedLocalSilo,
        ImmutableArray<string> expectedSilos, Guid expectedIncarnation)
    {
        if (snapshot is null || expectedSilos.IsDefaultOrEmpty
            || expectedSilos.Length is not (KeyLoadTimeSeriesIntensiveProtocol.MinimumNodeCount
                or KeyLoadTimeSeriesIntensiveProtocol.MaximumNodeCount)
            || expectedSilos.Distinct(StringComparer.Ordinal).Count() != expectedSilos.Length
            || !expectedSilos.Contains(expectedLocalSilo, StringComparer.Ordinal)
            || snapshot.SiloName != expectedLocalSilo
            || snapshot.ActiveSilos.IsDefault || !snapshot.ActiveSilos.SequenceEqual(expectedSilos, StringComparer.Ordinal))
        {
            throw Invalid();
        }
        var node = snapshot.Node;
        if (node is null || string.IsNullOrWhiteSpace(node.NodeId) || node.Incarnation != expectedIncarnation
            || node.SiloCount != expectedSilos.Length || node.ReplicaCount != expectedSilos.Length
            || !node.RoutingReady || node.Durability != DurabilityProfile.ProcessDurable)
        {
            throw Invalid();
        }
    }

    private static KeyLoadTimeSeriesIntensiveReplyException Invalid() =>
        new(KeyLoadTimeSeriesIntensiveProtocol.InvalidTopology);
}

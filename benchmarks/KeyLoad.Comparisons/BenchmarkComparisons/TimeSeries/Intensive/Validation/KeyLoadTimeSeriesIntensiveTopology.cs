using System.Collections.Immutable;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class KeyLoadTimeSeriesIntensiveTopology
{
    internal static void ValidateStatuses(ImmutableArray<NodeStatus> statuses,
        ImmutableArray<string> expectedVoters, Guid expectedIncarnation)
    {
        const int FirstElementIndex = 0;

        if (statuses.IsDefaultOrEmpty || expectedVoters.IsDefaultOrEmpty
            || statuses.Length != expectedVoters.Length
            || statuses.Length is not (KeyLoadTimeSeriesIntensiveProtocol.MinimumNodeCount
                or KeyLoadTimeSeriesIntensiveProtocol.MaximumNodeCount))
        {
            throw Invalid();
        }

        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        var expected = new HashSet<string>(expectedVoters, StringComparer.Ordinal);
        if (expected.Count != expectedVoters.Length || expectedVoters.Any(string.IsNullOrWhiteSpace))
        {
            throw Invalid();
        }

        var leader = statuses[KeyLoadTimeSeriesIntensiveProtocol.FirstStatusIndex]?.Leader;
        for (var index = FirstElementIndex; index < statuses.Length; index++)
        {
            var status = statuses[index];
            if (status is null || string.IsNullOrWhiteSpace(status.NodeId)
                || !nodeIds.Add(status.NodeId)
                || status.Incarnation != expectedIncarnation || status.Voters != statuses.Length
                || !status.RoutingReady || status.Durability != DurabilityProfile.QuorumProcessDurable
                || string.IsNullOrWhiteSpace(leader) || status.Leader != leader)
            {
                throw Invalid();
            }
        }

        if (!expected.Contains(leader!))
        {
            throw Invalid();
        }
    }

    internal static void ValidateDashboard(AdminNodeSnapshot snapshot, string expectedLocalVoter,
        ImmutableArray<string> expectedVoters, Guid expectedIncarnation)
    {
        if (snapshot is null || expectedVoters.IsDefaultOrEmpty
            || expectedVoters.Length is not (KeyLoadTimeSeriesIntensiveProtocol.MinimumNodeCount
                or KeyLoadTimeSeriesIntensiveProtocol.MaximumNodeCount)
            || expectedVoters.Distinct(StringComparer.Ordinal).Count() != expectedVoters.Length
            || !expectedVoters.Contains(expectedLocalVoter, StringComparer.Ordinal)
            || snapshot.LocalVoter != expectedLocalVoter
            || snapshot.Voters.IsDefault || !snapshot.Voters.SequenceEqual(expectedVoters, StringComparer.Ordinal))
        {
            throw Invalid();
        }

        var node = snapshot.Node;
        if (node is null || string.IsNullOrWhiteSpace(node.NodeId) || node.Incarnation != expectedIncarnation
            || node.Voters != expectedVoters.Length || !node.RoutingReady
            || node.Durability != DurabilityProfile.QuorumProcessDurable
            || string.IsNullOrWhiteSpace(node.Leader) || !expectedVoters.Contains(node.Leader, StringComparer.Ordinal))
        {
            throw Invalid();
        }
    }

    private static KeyLoadTimeSeriesIntensiveReplyException Invalid() =>
        new(KeyLoadTimeSeriesIntensiveProtocol.InvalidTopology);
}
